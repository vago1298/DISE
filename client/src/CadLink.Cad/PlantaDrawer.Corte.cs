using CadLink.Cad.PlanoEstructural;

namespace CadLink.Cad;

/// <summary>
/// El <b>corte por un eje</b>, dibujado al lado de la planta estructural.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió: que se dibuje <b>el corte que se haya elegido</b> en la pestaña del modelo, a
/// <c>CORTE_SEPARACION_M</c> —10 m— de la planta. Y tiene todo el sentido tenerlos juntos: la
/// planta dice los espesores y las distancias entre ejes, y el corte dice las alturas. Un
/// juego de planos con la planta sola obliga a adivinar las alturas de entrepiso.
/// </para>
/// <para>
/// El corte se arma con la geometría de <see cref="CorteEnAlzado"/> —pura aritmética, aparte y
/// comprobable sin AutoCAD— y aquí solo se dibuja: cada pieza como una polilínea cerrada en la
/// capa que le toca, la línea de cada nivel con su cota, y el rótulo debajo.
/// </para>
/// </remarks>
public sealed partial class PlantaDrawer
{
    /// <summary>
    /// Dibuja el corte elegido, a la derecha de la planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="dx"/> y <paramref name="dy"/> son el desplazamiento de la planta, y el
    /// corte se pone a partir de ahí: así los dos dibujos van del mismo juego y no hay que
    /// buscar el corte por el dibujo.
    /// </para>
    /// <para>
    /// La coordenada horizontal del corte es la que recorre el eje —en un corte por un eje de
    /// los que van en X se recorre la Y— y la vertical es la <b>cota del modelo</b>, tal cual.
    /// Eso deja el corte a la misma escala que la planta, que es lo que permite medir de uno a
    /// otro.
    /// </para>
    /// </remarks>
    /// <returns>Cuántas piezas se dibujaron.</returns>
    public int DibujarCorte(CorteCad c, double dx, double dy)
    {
        if (c.Elementos.Count == 0 || c.Eje.Length == 0)
        {
            return 0;
        }

        // El castillo modelado como SHELL de muro también es un castillo aquí: en el alzado
        // se dibuja como columna —rellena si el plano la corta— y no como el paño de un muro.
        // Es la misma normalización que en planta, para que el corte y la planta no discutan.
        if (_cfg.Bandera("SHELL_CASTILLO_COMO_COLUMNA", true))
        {
            PlanoEstructural.CastilloDeMuro.Normalizar(
                c.Elementos, EspesorMuroPorOmision,
                _cfg.Numero("SHELL_CASTILLO_UNIR_TOL_CM", 2) / 100,
                _cfg.Texto("SHELL_CASTILLO_PREFIJO", "K"),
                _cfg.Bandera("SHELL_CASTILLO_AL_PANO", true)
                    ? _cfg.Numero("PANO_TOLERANCIA_CM", 25) / 100
                    : 0);

            // Y LAS CADENAS DE SHELL, que es lo que hacía que la cadena intermedia saliera como un
            // paño de muro: sin relleno, sin bloque y en la capa equivocada.
            PlanoEstructural.CadenaDeMuro.Normalizar(c.Elementos, AnchoTrabePorOmision);
        }

        var piezas = CorteEnAlzado.Piezas(
            c.Elementos, c.EnX, c.Ordenada, c.EspesorM,
            _cfg.Bandera("CORTE_VER_EL_FONDO", true), c.HaciaMas);

        if (piezas.Count == 0)
        {
            Nota($"El corte por el eje {c.Eje} no toca ningún elemento, así que no se " +
                 "dibujó. Prueba con otro eje o sube CORTE_ESPESOR_CM.");
            return 0;
        }

        // ==============================================================================
        //  DÓNDE SE PONE: 10 UNIDADES ARRIBA DE LO QUE YA ESTÁ DIBUJADO
        // ==============================================================================
        //  Esto estaba mal y salía encima de la planta. El error era de referencia: se
        //  colocaba respecto del ORIGEN DEL MODELO —midiendo el alto de los elementos— pero
        //  las plantas no se dibujan en el origen: DibujarTodas las reparte y las sube al
        //  tope de lo que ya hubiera en el dibujo. Así que «alto del modelo + 10» caía justo
        //  en medio del juego.
        //
        //  Se pregunta al DIBUJO: el mismo TopeDeLoDibujado que usa el reparto de las
        //  plantas, que recorre lo que hay y devuelve su Y más alta. Diez unidades por encima
        //  de eso es diez unidades por encima de la planta, siempre, sin depender de cuántos
        //  niveles haya ni de dónde estuviera el modelo.
        //
        //  Y alineado por la IZQUIERDA con lo dibujado, para que el corte y la planta se lean
        //  en la misma columna.
        var separacion = _cfg.Numero("CORTE_SEPARACION_M", 10);

        //  Se toma el MAYOR de dos medidas, y las dos hacen falta:
        //
        //    * el TOPE DEL JUEGO que se acaba de dibujar, que se calculó con aritmética al
        //      repartir las plantas y por tanto siempre está;
        //    * y lo que se lee del DIBUJO, que además cubre lo que hubiera de antes.
        //
        //  Con solo lo leído, un fallo de COM dejaba el corte en Y = 10 mientras las plantas
        //  estaban en Y = 40, o sea DEBAJO de la planta. Con solo lo calculado se ignoraría lo
        //  que ya hubiera en el plano. El mayor de los dos no puede quedar debajo de ninguno.
        var tope = Math.Max(_topeDelJuego ?? 0, TopeDeLoDibujado() ?? 0);

        // ==============================================================================
        //  Y CADA CORTE, A LA DERECHA DEL ANTERIOR
        // ==============================================================================
        //  Se pidió: los cortes que se añadan van a la derecha, +8 del último que haya, y así
        //  para cuantos sean. El dibujante se acuerda de dónde acabó el último y encadena, que es
        //  más fiable que calcularlo desde fuera: quien pide los cortes no sabe cuánto va a
        //  ocupar cada uno —depende de las piezas que toque, de sus ejes y de sus cotas— y con una
        //  cuenta a ojo los cortes se encimaban o quedaban a diez metros unos de otros.
        var cx = _derechaDelUltimoCorte is { } yaHay
            ? yaHay + _cfg.Numero("CORTE_SEPARACION_CORTES_M", 8)
            : IzquierdaDeLoDibujado() ?? 0;

        // LA BASE DEL CORTE va EXACTAMENTE a tope + separación: se le resta la cota más baja
        // de sus piezas para que la de abajo —una zapata, un desplante con Z negativa— caiga
        // en esa línea y no por debajo. Así el corte entero queda por encima, siempre.
        var zBase = piezas.Min(q => q.Z);
        var cy = tope + separacion - zBase;

        var hechas = 0;

        foreach (var p in piezas)
        {
            // ==========================================================================
            //  LOS CASTILLOS DEL FONDO NO SE DIBUJAN
            // ==========================================================================
            //  Se pidió: «los castillos del fondo no se deben ver, solamente los que hayan en el
            //  lugar del corte, en esa línea». Y tiene sentido de dibujo: en una casa de
            //  mampostería hay un castillo cada dos metros en TODOS los ejes, así que el fondo de
            //  un alzado se llena de rectángulos verticales que no son de este corte y que tapan
            //  lo que sí lo es. Del fondo interesa el paño de los muros y la losa que sigue, no
            //  la fila de castillos de tres ejes más allá.
            if (!p.Cortada && p.Clase == ClasePlanta.Columna
                && !_cfg.Bandera("CORTE_FONDO_CON_COLUMNAS", false))
            {
                continue;
            }

            var capa = CapaDeLaPieza(p);

            var pts = new[]
            {
                cx + p.X, cy + p.Z,
                cx + p.X + p.Ancho, cy + p.Z,
                cx + p.X + p.Ancho, cy + p.Z + p.Alto,
                cx + p.X, cy + p.Z + p.Alto
            };

            // Una pieza de alto CERO es la losa cuyo espesor no dio el modelo: se dibuja
            // como una línea a la cota de su paño, que es lo honesto —hay losa y no se sabe
            // cuánto mide— en lugar de una franja de un espesor inventado.
            var pl = p.Alto <= 0.001
                ? Linea(cx + p.X, cy + p.Z, cx + p.X + p.Ancho, cy + p.Z, capa)
                : PolilineaCerrada(pts, capa);

            // ==========================================================================
            //  EL CONTORNO DE LOS MUROS DEL FONDO NO SE DIBUJA
            // ==========================================================================
            //  Se pidió: «el contorno de los muros del fondo bórralos, solo deja el contorno de los
            //  muros que se cortan sobre la línea de corte». Y se entiende al verlo: el fondo de un
            //  alzado son cinco o seis paños seguidos, y cada rectángulo mete cuatro líneas que no
            //  son del corte. Del fondo lo que dice algo es su ACHURADO —la mancha de mampostería—,
            //  no sus aristas.
            //
            //  El contorno se usa igual como LAZO del achurado y se borra después: se puede porque
            //  el achurado no es asociativo.
            //
            //  Lo que se borra es el MURO del fondo y nada más: una cadena o una trabe modelada
            //  como área llega con la clase Muro, y ésas sí dejan su contorno —son piezas, no paño—.
            var soloParaAchurar = !p.Cortada
                                  && p.Clase == ClasePlanta.Muro
                                  && !PlanoEstructural.CorteEnAlzado.DiceCadena(p)
                                  && !PlanoEstructural.CorteEnAlzado.DiceTrabe(p)
                                  && !_cfg.Bandera("CORTE_FONDO_CONTORNO_MUROS", false);

            // ==========================================================================
            //  LA TRABE, LA CADENA Y LA VIGA DE ACERO, COMO BLOQUE
            // ==========================================================================
            //  Se pidió, y es la misma idea que ya se usa con las columnas en planta: el bloque
            //  se llama como la sección —con su medida detrás— así que un BLOCKREPLACE cambia de
            //  golpe TODAS las cadenas de 15×25 del corte por el detalle armado, con sus
            //  varillas y sus estribos. Con rectángulos sueltos hay que dibujar el armado uno
            //  por uno, y en un corte de una casa eso son treinta veces el mismo trabajo.
            //
            //  Y SOLO LA CARA CORTA, LA QUE LLEGA. Es lo que se pidió y es lo único que tiene
            //  sentido: el bloque de una trabe de 20×30 es su CARA de 20×30 —la sección donde se
            //  dibujan las varillas y los estribos— no el rectángulo de tres metros que se ve
            //  cuando el corte va a lo largo de ella. Un bloque de tres metros de largo no se
            //  puede reemplazar por ningún detalle armado: no es una sección, es un costado.
            //
            //  Solo las CORTADAS: lo que se ve al fondo no lleva armado que enseñar, y meterlo en
            //  un bloque invitaría a reemplazarlo por un detalle que ahí no va.
            //  Y LA CADENA INTERMEDIA SIEMPRE, vaya el corte a lo largo o de canto: se pidió tres
            //  veces y tiene su razón de obra. La intermedia es la que confina los vanos de
            //  puertas y ventanas y la que remata un antepecho, va metida en el muro y es lo que
            //  hay que revisar en un corte: sin bloque no se puede cambiar por su detalle armado.
            var conBloque = p.EnSeccion
                            || (PlanoEstructural.CorteEnAlzado.EsIntermedia(p)
                                && _cfg.Bandera("CORTE_INTERMEDIA_SIEMPRE", true));

            //  Y TAMBIÉN POR LO QUE DICE LA PIEZA, no solo por su clase: una cadena modelada
            //  como área llega como muro, y mirando la clase se quedaba sin bloque. Es lo mismo
            //  que le pasaba al relleno.
            var deBarra = p.Clase == ClasePlanta.Trabe
                          || PlanoEstructural.CorteEnAlzado.DiceCadena(p)
                          || PlanoEstructural.CorteEnAlzado.DiceTrabe(p);

            if (p.Cortada && conBloque && deBarra
                && PiezaComoBloque(p, cx, cy, capa))
            {
                hechas++;
                continue;
            }

            if (pl is not null)
            {
                // ======================================================================
                //  LOS CASTILLOS Y LAS COLUMNAS, RELLENOS
                // ======================================================================
                //  Como en la planta, y por el mismo motivo: el relleno es lo que distingue de
                //  un golpe el elemento CORTADO del que solo se ve. En un alzado, una columna
                //  cortada se raya o se rellena; hueca se confunde con el hueco de una ventana.
                //
                //  Solo las CORTADAS: la que se ve al fondo no se rellena, porque no está
                //  cortada por el plano.
                //  Y CADA COSA DE SU COLOR, que es lo que se pidió y lo que hace legible un
                //  alzado de mampostería: el CASTILLO amarillo, la CADENA morada y la TRABE
                //  verde. En un corte por un muro hay tres piezas de concreto distintas a la
                //  vista —el castillo que sube, la cadena que cierra y la trabe que carga— y
                //  del contorno solo no se distinguen: las tres son un rectángulo.
                //  Y SOLO LO QUE SE VE EN SECCIÓN. Se pidió: «si cortas a lo largo de la
                //  sección solo dale el tipo de línea, pero si lo cortas donde se ve el armado
                //  —que debe ser el lado corto— sí rellena la sección». Es la convención de
                //  cualquier plano de obra: el relleno dice «aquí el plano cruza la pieza y esto
                //  es su sección, la cara donde va el armado». Rellenando también lo que se ve de
                //  costado, el alzado deja de decir por dónde pasa el corte.
                //  Y VALE PARA TODO, sin excepciones por clase: la cadena, la viga o la trabe
                //  vistas A LO LARGO se quedan VACÍAS, con su línea y nada más. Solo se rellenan
                //  las CARAS QUE LLEGAN, o sea las secciones.
                //
                //  Hubo una excepción para las cadenas y duró poco, con razón: rellenar una
                //  cadena de cuatro metros de largo pinta de morado media fachada del alzado y
                //  entierra debajo lo único que ese relleno tenía que señalar, que son las caras
                //  cortadas. Y la CADENA INTERMEDIA no pierde nada: la que confina un vano se ve
                //  por su cara —el plano la cruza— y esa sí se rellena y sí lleva su bloque.
                var soloEnSeccion = _cfg.Bandera("CORTE_RELLENAR_SOLO_EN_SECCION", true);

                //  CON UNA EXCEPCIÓN, Y SOLO UNA: LA CADENA INTERMEDIA. Se pidió tres veces que se
                //  rellene y lleve bloque, y no es un capricho: es la que confina los vanos de
                //  puertas y ventanas, va metida en el muro y es lo que se viene a revisar en un
                //  corte. Sin relleno se pierde entre las dos líneas del paño. Las demás cadenas y
                //  trabes vistas a lo largo siguen yendo vacías, que es lo que se pidió después.
                var enSeccion = p.EnSeccion
                                || !soloEnSeccion
                                || (PlanoEstructural.CorteEnAlzado.EsIntermedia(p)
                                    && _cfg.Bandera("CORTE_INTERMEDIA_SIEMPRE", true));

                if (p.Cortada && enSeccion && _cfg.Bandera("CORTE_RELLENAR_COLUMNAS", true))
                {
                    var color = ColorDelRellenoEnElCorte(p);

                    if (color > 0)
                    {
                        RellenarPieza(pl, capa, color);
                    }
                }

                // ======================================================================
                //  LO CORTADO CON SU LÍNEA, EL FONDO MÁS FLOJO
                // ======================================================================
                //  Es la convención de cualquier plano de obra, y aquí hace falta más que en
                //  ninguna parte: sin distinguirlas, el fondo y la sección se leen igual y no
                //  se sabe por dónde pasa el corte. Lo que se ve al fondo va a trazos, que es
                //  como se dibuja lo que no se corta.
                if (!p.Cortada)
                {
                    ALineaDeFondo(pl);
                }

                // ======================================================================
                //  EL ÁREA DE LOS MUROS DE MAMPOSTERÍA, ACHURADA
                // ======================================================================
                //  Se pidió: en el corte, el área de los muros de MAMPOSTERÍA lleva su patrón
                //  —AR-BRSTD para el tabique y el adobe, AR-B816 para el tabicón y el tabique
                //  ligero— y los de CONCRETO no llevan ninguno. Es una diferencia de obra: uno se
                //  levanta con piezas y mortero y el otro se cimbra y se cuela, y en el corte se
                //  tiene que ver de un golpe cuál es cuál.
                //
                //  De qué es el muro sale de las NOTAS de su propiedad, que es donde se escribe.
                if (p.Clase == ClasePlanta.Muro)
                {
                    AchurarMamposteria(pl, capa, p, piezas, cx, cy);
                }

                // Y el contorno del muro del fondo se va, que era solo el lazo del achurado.
                if (soloParaAchurar)
                {
                    Borrar(pl);
                }

                hechas++;
            }
        }

        DibujarNivelesDelCorte(c, cx, cy, piezas);
        DibujarEjesDelCorte(c, cx, cy, piezas);
        AcotarElCorte(c, cx, cy, piezas);
        RotularElCorte(c, cx, cy, piezas);

        // DÓNDE ACABÓ ESTE CORTE, para que el siguiente arranque a su derecha. Se mide sobre las
        // piezas y se le suma lo que sobresale a su derecha —las burbujas de sus ejes y sus
        // cotas—, que si no el siguiente corte se le metería encima de las burbujas.
        _derechaDelUltimoCorte = cx + piezas.Max(q => q.X + q.Ancho)
                                 + Ejes.SaleEjes() + Ejes.RadioBurbuja;

        Nota($"Corte por el eje {c.Eje} dibujado con {hechas} pieza(s), {separacion:0.##} " +
             "unidades ARRIBA de lo que ya había dibujado.");

        return hechas;
    }

    /// <summary>
    /// Las <b>líneas de nivel</b> del corte, con su nombre y su cota.
    /// </summary>
    /// <remarks>
    /// Es lo que convierte un montón de rectángulos en un corte que se puede leer: sin las
    /// cotas de nivel no se sabe a qué altura está cada cosa, que es justo lo que se viene a
    /// buscar en un corte. La línea se saca un poco por los dos lados, como en un plano.
    /// </remarks>
    private void DibujarNivelesDelCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        if (c.Niveles.Count == 0)
        {
            return;
        }

        var xMin = piezas.Min(p => p.X);
        var xMax = piezas.Max(p => p.X + p.Ancho);

        var vuela = _cfg.Numero("CORTE_NIVEL_VUELA_M", 0.6);
        var capa = _capas.Prefijo + "EJES";
        var capaTxt = CapaTextos;

        foreach (var (nombre, z) in c.Niveles)
        {
            Linea(cx + xMin - vuela, cy + z, cx + xMax + vuela, cy + z, capa);

            var texto = $"{Rot.NombreDeNivel(nombre)}  " +
                        z.ToString("+0.000;-0.000;±0.000",
                                   System.Globalization.CultureInfo.InvariantCulture);

            Mtexto(cx + xMax + vuela, cy + z, texto, AlturaSecciones(c.AlturaTexto),
                   capaTxt, 0, EstiloSecciones, false, 1);
        }
    }

    /// <summary>
    /// Los <b>ejes</b> del corte, con su línea y su burbuja arriba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió: el corte con sus ejes, como la planta. Y hace falta para leerlo: sin ellos, un
    /// alzado es un dibujo bonito del que no se puede replantear nada, porque no se sabe qué
    /// columna es cuál. Con la burbuja, cada pieza del corte se puede casar con su eje en la
    /// planta.
    /// </para>
    /// <para>
    /// Son los ejes <b>perpendiculares</b> al del corte, los que se cruzan. La línea va de
    /// abajo del corte hasta la burbuja, y a trazos, con el mismo tipo de línea que los ejes de
    /// la planta: es el mismo objeto del plano, así que se dibuja igual.
    /// </para>
    /// </remarks>
    private void DibujarEjesDelCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        if (c.Ejes.Count == 0 || !_cfg.Bandera("CORTE_CON_EJES", true))
        {
            return;
        }

        var zMin = piezas.Min(p => p.Z);
        var zMax = piezas.Max(p => p.Z + p.Alto);

        var capa = _capas.Prefijo + "EJES";
        var capaBur = _capas.Prefijo + "EJES-BURBUJA";
        var capaTxt = _capas.Prefijo + "EJES-TEXTO";

        var r = Ejes.RadioBurbuja;
        var sale = _cfg.Numero("CORTE_EJES_SALE_M", 1.2);
        var escalaLt = _cfg.Numero("EJES_ESCALA_TIPOLINEA", 1);

        foreach (var (id, o) in c.Ejes)
        {
            var x = cx + o;
            var arriba = cy + zMax + sale;

            LineaDeEje(x, cy + zMin - sale, x, arriba, capa, escalaLt);
            Burbuja(x, arriba + r, id, capaBur, capaTxt, 0, 1);
        }
    }

    /// <summary>
    /// Las <b>cotas</b> del corte: entre ejes, la total, y las alturas de entrepiso.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió acotar los cortes, y un corte se acota en las <b>dos direcciones</b>, que es lo
    /// que lo hace útil: en horizontal las distancias entre ejes —las mismas de la planta, que
    /// así se pueden comprobar— y en <b>vertical las alturas</b>, que son el dato que solo el
    /// corte puede dar. Un corte sin las alturas de entrepiso no sirve para nada.
    /// </para>
    /// <para>
    /// Se usa la MISMA cota alineada que la planta —<c>CotaAlineada</c>, con el estilo
    /// <c>COTA_DIM</c> y su separador decimal por objeto— para que las cotas del corte y las de
    /// la planta salgan idénticas, con la misma letra y el mismo tamaño.
    /// </para>
    /// </remarks>
    private void AcotarElCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        if (!_cfg.Bandera("CORTE_ACOTAR", true))
        {
            return;
        }

        var capa = _capas.Prefijo + "COTAS";

        var zMin = piezas.Min(p => p.Z);
        var zMax = piezas.Max(p => p.Z + p.Alto);

        var sep = _cfg.Numero("COTAS_SEPARACION", 0.75);
        var sepTotal = _cfg.Numero("COTAS_SEPARACION_TOTAL", 1.17);

        // ---- las horizontales, entre ejes, DEBAJO del corte ------------------------
        var ejes = c.Ejes.OrderBy(e => e.Ordenada).ToList();

        if (ejes.Count >= 2)
        {
            var y = cy + zMin - sep;

            for (var i = 0; i + 1 < ejes.Count; i++)
            {
                var xa = cx + ejes[i].Ordenada;
                var xb = cx + ejes[i + 1].Ordenada;

                CotaAlineada(xa, y, xb, y, (xa + xb) / 2, y, capa, -1);
            }

            // Y LA TOTAL, más abajo. Solo con tres ejes o más: con dos sería la misma cota
            // escrita dos veces.
            if (ejes.Count > 2)
            {
                var xa = cx + ejes[0].Ordenada;
                var xb = cx + ejes[^1].Ordenada;
                var yt = cy + zMin - sepTotal;

                CotaAlineada(xa, yt, xb, yt, (xa + xb) / 2, yt, capa, 0);
            }
        }

        // ---- las VERTICALES: las alturas, que es lo que solo el corte dice ---------
        var niveles = c.Niveles
            .Select(n => n.Z)
            .Distinct()
            .OrderBy(z => z)
            .ToList();

        // Si el modelo no trajo niveles, se acota al menos lo que mide el corte de abajo
        // arriba: sin ninguna cota vertical el corte no dice ni la altura del edificio.
        if (niveles.Count < 2)
        {
            niveles = new List<double> { zMin, zMax };
        }

        var xCota = cx + AnchoDelCorte(piezas) + sep;

        for (var i = 0; i + 1 < niveles.Count; i++)
        {
            var za = cy + niveles[i];
            var zb = cy + niveles[i + 1];

            CotaAlineada(xCota, za, xCota, zb, xCota, (za + zb) / 2, capa, -1);
        }

        if (niveles.Count > 2)
        {
            var za = cy + niveles[0];
            var zb = cy + niveles[^1];
            var xt = cx + AnchoDelCorte(piezas) + sepTotal;

            CotaAlineada(xt, za, xt, zb, xt, (za + zb) / 2, capa, 0);
        }
    }

    /// <summary>Lo que ocupa el corte a lo largo, para colgar las cotas verticales a su derecha.</summary>
    private static double AnchoDelCorte(List<CorteEnAlzado.Pieza> piezas) =>
        piezas.Max(p => p.X + p.Ancho);

    /// <summary>El rótulo del corte, debajo: <c>CORTE POR EL EJE 3</c>.</summary>
    private void RotularElCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        var xMin = piezas.Min(p => p.X);
        var xMax = piezas.Max(p => p.X + p.Ancho);
        var zMin = piezas.Min(p => p.Z);

        var plantilla = _cfg.Texto("CORTE_ROTULO", "CORTE  POR  EL  EJE  %E");
        var texto = plantilla.Replace("%E", c.Eje);

        var altura = _cfg.Numero("ROTULO_ALTURA_NIVEL", 0.26);
        var abajo = _cfg.Numero("CORTE_ROTULO_ABAJO_M", 1.2);

        Mtexto((cx + ((xMin + xMax) / 2)), cy + zMin - abajo, texto, altura,
               _capas.CapaDeTipo("TITULO"), 0, Rot.Estilo, false);
    }

    /// <summary>
    /// Rellena una pieza del corte con el <b>SOLID</b> de la planta.
    /// </summary>
    /// <remarks>
    /// El mismo achurado y el mismo color que usa la sección de la columna en la planta
    /// —<c>COLOR_RELLENO_BLOQUE</c>—, para que las dos vistas del mismo castillo se vean
    /// iguales. Va por la cascada de arreglos de <see cref="AcadArreglos"/>, que es la vía que
    /// funciona en AutoCAD 2026, y si no se deja, el corte se queda con la pieza hueca: se ve
    /// peor, pero está.
    /// </remarks>
    /// <summary>
    /// Dónde acabó, <b>a la derecha</b>, el último corte dibujado en esta pasada.
    /// </summary>
    /// <remarks>
    /// Es lo que encadena los cortes: el siguiente arranca a su derecha más la separación de la
    /// hoja. Vive en el dibujante y no en quien pide los cortes porque solo aquí se sabe cuánto
    /// ocupó de verdad cada uno —depende de las piezas que toque, de sus ejes y de sus cotas—.
    /// </remarks>
    private double? _derechaDelUltimoCorte;

    /// <summary>
    /// El <b>achurado de mampostería</b> de un muro del corte, si es de mampostería.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El patrón, la escala y el color salen de la hoja, y de qué es el muro sale de las
    /// <b>notas</b> de su propiedad: tabique y adobe llevan <c>AR-BRSTD</c>, y tabicón y tabique
    /// ligero, <c>AR-B816</c>. Un muro de <b>concreto</b> no lleva ninguno y se queda como está.
    /// </para>
    /// <para>
    /// El achurado va <b>por objeto</b> con su color, no por capa: la capa del muro es la de su
    /// tipo y el patrón tiene que verse igual en todas. Y <b>no asociativo</b>, que es lo que se
    /// aprendió con el achurado de las losas: un hatch asociado a una polilínea que luego se mueve
    /// o se borra deja el dibujo con un achurado huérfano.
    /// </para>
    /// </remarks>
    private void AchurarMamposteria(
        object? pl, string capa, PlanoEstructural.CorteEnAlzado.Pieza p,
        IReadOnlyList<PlanoEstructural.CorteEnAlzado.Pieza> piezas, double cx, double cy)
    {
        if (pl is null || !_cfg.Bandera("CORTE_HATCH_MAMPOSTERIA", true))
        {
            return;
        }

        var cual = PlanoEstructural.HatchDeMamposteria.Para(
            p.Notas, p.Seccion,
            _cfg.Texto("CORTE_HATCH_TABIQUE", "AR-BRSTD"),
            _cfg.Numero("CORTE_HATCH_TABIQUE_ESCALA", 0.0010),
            _cfg.Texto("CORTE_HATCH_TABICON", "AR-B816"),
            _cfg.Numero("CORTE_HATCH_TABICON_ESCALA", 0.0005),
            (int)_cfg.Numero("CORTE_HATCH_MAMPOSTERIA_COLOR", 12));

        if (cual is null)
        {
            return;
        }

        // ==============================================================================
        //  NO SE ACHURA DONDE EL CORTE PASA POR CONCRETO
        // ==============================================================================
        //  Se pidió: en los muros de mampostería del fondo y en los que el plano corta, «siempre y
        //  cuando no corte en un elemento de concreto». Donde el corte pasa por un castillo, una
        //  cadena o un muro de concreto, lo que hay ahí es CONCRETO: achurarlo de tabique sería
        //  decir que ese trozo se levantó con ladrillos.
        //
        //  Así que del ancho del muro se quitan los trozos de las piezas de concreto que el plano
        //  corta y se achura lo que queda. Un castillo en medio del muro parte su achurado en dos,
        //  que es lo que se ve en obra: dos paños de mampostería con su castillo entre los dos.
        var tramos = PlanoEstructural.CorteEnAlzado.TramosSinConcreto(p, piezas);

        if (tramos.Count == 0)
        {
            return;
        }

        // El caso normal —un muro sin concreto encima— se achura sobre su PROPIA polilínea, sin
        // crear nada: es lo más barato y lo que menos toca el dibujo.
        var entero = tramos.Count == 1
                     && Math.Abs(tramos[0].X1 - p.X) < 0.001
                     && Math.Abs(tramos[0].X2 - (p.X + p.Ancho)) < 0.001;

        if (entero)
        {
            AchurarConPatron(pl, capa, cual, borrarElLazo: false);
            return;
        }

        // Y si hay concreto en medio, un achurado por tramo. El contorno de cada tramo es un LAZO
        // DE PASO: se dibuja, se achura y se borra, porque esas líneas no existen en el muro —lo
        // que se ve es su paño entero— y dejarlas sería inventar juntas donde no las hay.
        foreach (var (x1, x2) in tramos)
        {
            var lazo = PolilineaCerrada(
                new[]
                {
                    cx + x1, cy + p.Z,
                    cx + x2, cy + p.Z,
                    cx + x2, cy + p.Z + p.Alto,
                    cx + x1, cy + p.Z + p.Alto
                },
                capa);

            AchurarConPatron(lazo, capa, cual, borrarElLazo: true);
        }
    }

    /// <summary>Un achurado con patrón sobre un contorno, y el contorno de paso si toca.</summary>
    private void AchurarConPatron(
        object? pl, string capa, PlanoEstructural.HatchDeMamposteria.Achurado cual,
        bool borrarElLazo)
    {
        if (pl is null)
        {
            return;
        }

        object? ht;

        try
        {
            ht = AcadConnection.Retry(
                () => (object)_ms.AddHatch(0, cual.Patron, false, 0));
        }
        catch (Exception ex)
        {
            Fallo($"Achurado '{cual.Patron}' del muro del corte", ex);
            return;
        }

        dynamic h = ht!;

        var conLazo = AcadArreglos.Llamar(
            $"AppendOuterLoop del achurado '{cual.Patron}'",
            new[] { pl },
            arr => { h.AppendOuterLoop(arr); },
            Fallo, Nota);

        if (!conLazo)
        {
            Borrar(ht);
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                // La escala ANTES de evaluar: si se evalúa a escala 1 y se cambia después, hay
                // versiones que se quedan con el achurado de la primera evaluación.
                h.PatternScale = cual.Escala;
                h.Evaluate();
                h.Layer = capa;
                h.Color = cual.Color;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Escala del achurado '{cual.Patron}'", ex);
        }

        // EL LAZO DE PASO SE BORRA. Se puede porque el achurado NO es asociativo: si lo fuera, al
        // borrar su contorno el achurado se iría con él.
        if (borrarElLazo)
        {
            Borrar(pl);
        }
    }

    /// <summary>
    /// Inserta una pieza del corte como <b>bloque</b>, con su relleno dentro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El bloque se llama como su <b>sección</b> con la <b>medida</b> detrás —«CORTE-CC 15X25
    /// 15X25»— y ahí está la gracia: con un <c>BLOCKREPLACE</c> se cambian de golpe todas las
    /// cadenas de esa medida por el detalle armado. La medida va en el nombre porque la misma
    /// sección se ve de dos formas en un corte: de canto son 15×25, y a lo largo son tres metros
    /// por 25, que es otro dibujo y no puede compartir bloque con el primero.
    /// </para>
    /// <para>
    /// El <b>relleno va dentro</b> del bloque, como en la planta: así se mueve con él y quien
    /// reemplace el bloque por su detalle se lleva el relleno con el cambio. Y la inserción va al
    /// <b>centro</b> de la pieza, que es el punto con el que se dibuja cualquier detalle de
    /// sección.
    /// </para>
    /// <para>
    /// Si algo falla se devuelve <c>false</c> y quien llama dibuja el rectángulo de siempre: el
    /// corte no se queda sin la pieza.
    /// </para>
    /// </remarks>
    private bool PiezaComoBloque(
        PlanoEstructural.CorteEnAlzado.Pieza p, double cx, double cy, string capa)
    {
        if (!_cfg.Bandera("CORTE_PIEZAS_COMO_BLOQUE", true)
            || p.Ancho <= 0.001 || p.Alto <= 0.001)
        {
            return false;
        }

        var nombre = NombreDelBloqueDeLaPieza(p);

        if (nombre.Length == 0 || !AsegurarBloqueDeLaPieza(nombre, p))
        {
            return false;
        }

        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic ins = _ms.InsertBlock(
                    new[]
                    {
                        cx + p.X + (p.Ancho / 2),
                        cy + p.Z + (p.Alto / 2),
                        0d
                    },
                    nombre, 1d, 1d, 1d, 0d);

                ins.Layer = capa;

                // POR CAPA: el relleno lleva su color dentro del bloque y el contorno tiene que
                // salir del color de la capa del tipo —E-CADENA, E-TRABE, E-ACERO—, como el resto.
                try
                {
                    ins.Color = PorCapa;
                }
                catch (Exception)
                {
                    // Un color que no se deja poner no estropea la pieza.
                }

                return true;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Inserción del bloque '{nombre}' del corte", ex);
            return false;
        }
    }

    /// <summary>
    /// El nombre del bloque de una pieza del corte: su sección y su medida.
    /// </summary>
    /// <remarks>
    /// Con el prefijo <c>CORTE_BLOQUE_PREFIJO</c> —«CORTE-»— para no chocar con los bloques de la
    /// planta: la sección de una columna se llama igual en los dos dibujos y no es el mismo dibujo,
    /// uno es su sección en planta y el otro su alzado. Sin sección se usa el <b>tipo</b>, que
    /// siempre está: así una cadena sin nombre de sección sigue teniendo su bloque.
    /// </remarks>
    private string NombreDelBloqueDeLaPieza(PlanoEstructural.CorteEnAlzado.Pieza p)
    {
        var que = !string.IsNullOrWhiteSpace(p.Seccion)
            ? p.Seccion
            : !string.IsNullOrWhiteSpace(p.Tipo)
                ? p.Tipo
                : "PIEZA";

        var medida = $"{p.Ancho * 100:0.##}X{p.Alto * 100:0.##}";

        return LimpiaNombreDeBloque(
            _cfg.Texto("CORTE_BLOQUE_PREFIJO", "CORTE-") + que.Trim() + " " + medida);
    }

    /// <summary>Crea el bloque de una pieza del corte: su rectángulo y su relleno.</summary>
    /// <remarks>
    /// El rectángulo va <b>centrado en el origen</b> del bloque, que es lo que hace que la
    /// inserción caiga en el centro de la pieza y que un detalle dibujado a ese centro encaje sin
    /// mover nada. Si el bloque ya existe se respeta, salvo que <c>REDEFINIR_BLOQUES</c> esté en
    /// SI: es la diferencia entre conservar el detalle que ya se cambió a mano y actualizarlo.
    /// </remarks>
    private bool AsegurarBloqueDeLaPieza(
        string nombre, PlanoEstructural.CorteEnAlzado.Pieza p)
    {
        if (_bloquesListos.Contains(nombre))
        {
            return true;
        }

        var color = ColorDelRellenoEnElCorte(p);

        try
        {
            var ok = AcadConnection.Retry(() =>
            {
                dynamic bloques = _doc.Blocks;
                dynamic blk;
                var existia = true;

                try
                {
                    blk = bloques.Item(nombre);
                }
                catch (Exception)
                {
                    existia = false;
                    blk = bloques.Add(new[] { 0d, 0d, 0d }, nombre);
                }

                if (existia)
                {
                    if (!_cfg.Bandera("REDEFINIR_BLOQUES", true))
                    {
                        return true;
                    }

                    // De atrás hacia adelante: borrar por índice hacia adelante recoloca los que
                    // quedan y se saltarían la mitad.
                    for (var i = (int)blk.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            blk.Item(i).Delete();
                        }
                        catch (Exception)
                        {
                            // Una entidad que no se deja borrar no impide rearmar el resto.
                        }
                    }
                }

                var mediaA = p.Ancho / 2;
                var mediaH = p.Alto / 2;

                dynamic contorno = blk.AddLightWeightPolyline(
                    new[]
                    {
                        -mediaA, -mediaH,
                        mediaA, -mediaH,
                        mediaA, mediaH,
                        -mediaA, mediaH
                    });

                contorno.Closed = true;
                contorno.Layer = "0";

                if (color > 0)
                {
                    RellenarDentroDelBloqueDelCorte(blk, contorno, color, nombre);
                }

                return true;
            });

            if (ok)
            {
                _bloquesListos.Add(nombre);
            }

            return ok;
        }
        catch (Exception ex)
        {
            Fallo($"Bloque '{nombre}' del corte", ex);
            return false;
        }
    }

    /// <summary>El relleno sólido <b>dentro</b> del bloque de una pieza del corte.</summary>
    private void RellenarDentroDelBloqueDelCorte(
        dynamic blk, object contorno, int color, string nombre)
    {
        try
        {
            dynamic h = blk.AddHatch(0, "SOLID", true, 0);

            var conLazo = AcadArreglos.Llamar(
                $"AppendOuterLoop del relleno del bloque '{nombre}'",
                new[] { contorno },
                arr => { h.AppendOuterLoop(arr); },
                Fallo, Nota);

            if (!conLazo)
            {
                try
                {
                    h.Delete();
                }
                catch (Exception)
                {
                    // Un achurado vacío de más no estropea el bloque.
                }

                return;
            }

            h.Evaluate();
            h.Layer = "0";
            h.Color = color;
        }
        catch (Exception ex)
        {
            Fallo($"Relleno dentro del bloque '{nombre}' del corte", ex);
        }
    }

    /// <summary>
    /// El color del relleno de una pieza <b>cortada</b>: amarillo, morado o verde.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió así: el <b>castillo</b> —y la columna— amarillo, como en la planta; la
    /// <b>cadena</b> morada; y la <b>trabe</b> verde. No es decoración: en un corte por un muro
    /// hay tres piezas de concreto distintas a la vista —el castillo que sube, la cadena que
    /// cierra y la trabe que carga— y del contorno solo no se distinguen, porque las tres son un
    /// rectángulo. Con el color se leen de un golpe.
    /// </para>
    /// <para>
    /// Devuelve <b>0</b> para lo que no se rellena —la losa y el muro—, que en un alzado se leen
    /// por su franja y por su paño. Los tres colores salen de la hoja.
    /// </para>
    /// </remarks>
    private int ColorDelRellenoEnElCorte(PlanoEstructural.CorteEnAlzado.Pieza p)
    {
        // ==============================================================================
        //  POR LO QUE DICE LA PIEZA, NO POR SU CLASE
        // ==============================================================================
        //  Aquí estaba la cadena intermedia que no se rellenaba, por más vueltas que le dimos: si
        //  llega como MURO —porque en el modelo es un área y la conversión no la alcanzó— este
        //  método devolvía 0 y se quedaba sin relleno, sin importar lo que dijeran sus notas.
        //
        //  Así que se pregunta primero por lo que la pieza DICE ser: si sus notas o su tipo dicen
        //  CADENA o DALA, es una cadena y va morada; si dicen TRABE o VIGA, verde. La clase se mira
        //  después, como respaldo. El dato lo pone el modelo en las property notes, así que esto no
        //  adivina nada.
        if (PlanoEstructural.CorteEnAlzado.DiceCadena(p))
        {
            return Color((int)_cfg.Numero("CORTE_COLOR_RELLENO_CADENA", 6), 6);
        }

        if (PlanoEstructural.CorteEnAlzado.DiceTrabe(p))
        {
            return Color((int)_cfg.Numero("CORTE_COLOR_RELLENO_TRABE", 3), 3);
        }

        if (p.Clase == ClasePlanta.Columna)
        {
            return ColorDelRelleno();
        }

        // El muro y la losa no se rellenan: en un alzado se leen por su paño y por su franja.
        return p.Clase == ClasePlanta.Trabe
            ? Color((int)_cfg.Numero("CORTE_COLOR_RELLENO_TRABE", 3), 3)
            : 0;

        // Un color fuera de la paleta de AutoCAD no se puede poner: se vuelve al de la hoja.
        static int Color(int color, int porOmision) =>
            color is > 0 and <= 255 ? color : porOmision;
    }

    private void RellenarPieza(object? pl, string capa, int color)
    {
        if (pl is null)
        {
            return;
        }

        object? ht;

        try
        {
            ht = AcadConnection.Retry(() => (object)_ms.AddHatch(0, "SOLID", true, 0));
        }
        catch (Exception ex)
        {
            Fallo("Relleno de la pieza del corte", ex);
            return;
        }

        dynamic h = ht!;

        var conLazo = AcadArreglos.Llamar(
            "AppendOuterLoop del relleno del corte",
            new[] { pl },
            arr => { h.AppendOuterLoop(arr); },
            Fallo, Nota);

        if (!conLazo)
        {
            Borrar(ht);
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                h.Evaluate();
                h.Layer = capa;
                h.Color = color;
            });
        }
        catch (Exception ex)
        {
            Fallo("Evaluate del relleno del corte", ex);
        }
    }

    /// <summary>
    /// La X más a la <b>izquierda</b> de lo que ya hay dibujado, o nulo si el dibujo está vacío.
    /// </summary>
    /// <remarks>
    /// Hermana de <c>TopeDeLoDibujado</c>: con las dos, el corte se coloca respecto del DIBUJO
    /// y no respecto del origen del modelo, que es lo que lo ponía encima de la planta. Se
    /// recorre el espacio modelo una vez y se toma la menor X de las cajas envolventes.
    /// </remarks>
    private double? IzquierdaDeLoDibujado()
    {
        try
        {
            return AcadConnection.Retry<double?>(() =>
            {
                double? minimo = null;

                foreach (var ent in _ms)
                {
                    if (CajaEnvolvente(ent) is not { } c)
                    {
                        continue;
                    }

                    var x = c.Min[0];

                    if (minimo is null || x < minimo)
                    {
                        minimo = x;
                    }
                }

                return minimo;
            });
        }
        catch (Exception)
        {
            // Sin poder recorrer el dibujo, el corte arranca en el origen en X: queda
            // desalineado con la planta, pero no encima de ella, que es lo que importaba.
            return null;
        }
    }

    /// <summary>
    /// Deja una pieza como <b>fondo</b>: a trazos, para que no se confunda con lo cortado.
    /// </summary>
    /// <remarks>
    /// El tipo de línea va <b>por objeto</b> y no por capa a propósito: la pieza se queda en la
    /// capa que le toca —E-CADENA, E-TRABE, E-MURO— para que apagarla la apague también en el
    /// corte, y lo único que cambia es cómo se dibuja. Con una capa aparte para el fondo habría
    /// que apagar dos capas para quitar un muro.
    /// </remarks>
    private void ALineaDeFondo(object? pl)
    {
        if (pl is null)
        {
            return;
        }

        var tipo = _cfg.Texto("CORTE_FONDO_LINETYPE", "ACAD_ISO02W100");

        if (tipo.Length == 0 || !AsegurarTipoDeLinea(tipo))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)pl).Linetype = tipo; });
        }
        catch (Exception)
        {
            // Sin el tipo de línea el fondo se ve continuo: se distingue peor, pero está.
        }
    }

    /// <summary>
    /// Cuánto ocupa lo dibujado en planta, para saber dónde empieza el corte.
    /// </summary>
    /// <remarks>
    /// Con <paramref name="enY"/> se mide el ALTO, que es lo que hace falta para poner el corte
    /// encima. Si no hay nada que medir se devuelven 10 m: es mejor separar de más que dibujar
    /// el corte sobre la planta.
    /// </remarks>
    private static double ExtensionDeLoDibujado(
        IReadOnlyList<ElementoPlanta> elementos, bool enY)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        void Ver(double v)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        foreach (var el in elementos)
        {
            if (el.Vertices.Count > 0)
            {
                foreach (var (x, y) in el.Vertices)
                {
                    Ver(enY ? y : x);
                }
            }
            else
            {
                Ver(enY ? el.Y1 : el.X1);
                Ver(enY ? el.Y2 : el.X2);
            }
        }

        return max > min ? max - min : 10;
    }

    /// <summary>
    /// La capa que le toca a cada pieza del corte: <b>las mismas</b> de la planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compartir capas con la planta no es pereza: es lo que hace que apagar E-MURO apague el
    /// muro en los dos dibujos, y que los colores y los grosores de impresión salgan iguales
    /// en el corte y en la planta sin configurar nada dos veces.
    /// </para>
    /// <para>
    /// Y la capa sale del <b>TIPO</b>, que es el que trae las <i>property notes</i>: se pidió
    /// que en los alzados una <b>cadena de cerramiento</b> o de <b>desplante</b> vaya a las
    /// capas de las cadenas y una <b>trabe</b> a <c>E-TRABE</c>. Antes la capa salía solo de la
    /// CLASE, así que todas las barras horizontales del corte caían en <c>E-TRABE</c> —las
    /// cadenas también— y el corte no coincidía con la planta, donde sí van separadas.
    /// <c>CapaDeTipo</c> es quien sabe que CADENA DE DESPLANTE tiene capa propia.
    /// </para>
    /// <para>
    /// Sin tipo se cae a la clase, que es lo que había: un modelo sin notas sigue saliendo como
    /// antes, no se va a la capa de lo que no se sabe.
    /// </para>
    /// </remarks>
    private string CapaDeLaPieza(CorteEnAlzado.Pieza p)
    {
        if (p.Tipo.Length > 0)
        {
            return _capas.CapaDeTipo(p.Tipo);
        }

        return p.Clase switch
        {
            ClasePlanta.Columna => _capas.CapaDeTipo("COLUMNA"),
            ClasePlanta.Muro => _capas.CapaDeTipo("MURO"),
            ClasePlanta.Losa => _capas.CapaDeTipo("LOSA"),
            _ => _capas.CapaDeTipo("TRABE")
        };
    }
}
