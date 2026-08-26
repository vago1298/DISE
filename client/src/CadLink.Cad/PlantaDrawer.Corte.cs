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
        }

        var piezas = CorteEnAlzado.Piezas(c.Elementos, c.EnX, c.Ordenada, c.EspesorM);

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

        var cx = IzquierdaDeLoDibujado() ?? 0;

        // LA BASE DEL CORTE va EXACTAMENTE a tope + separación: se le resta la cota más baja
        // de sus piezas para que la de abajo —una zapata, un desplante con Z negativa— caiga
        // en esa línea y no por debajo. Así el corte entero queda por encima, siempre.
        var zBase = piezas.Min(q => q.Z);
        var cy = tope + separacion - zBase;

        var hechas = 0;

        foreach (var p in piezas)
        {
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
                if (p.Cortada && p.Clase == ClasePlanta.Columna
                    && _cfg.Bandera("CORTE_RELLENAR_COLUMNAS", true))
                {
                    RellenarPieza(pl, capa);
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

                hechas++;
            }
        }

        DibujarNivelesDelCorte(c, cx, cy, piezas);
        DibujarEjesDelCorte(c, cx, cy, piezas);
        AcotarElCorte(c, cx, cy, piezas);
        RotularElCorte(c, cx, cy, piezas);

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
    private void RellenarPieza(object? pl, string capa)
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
                h.Color = ColorDelRelleno();
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
