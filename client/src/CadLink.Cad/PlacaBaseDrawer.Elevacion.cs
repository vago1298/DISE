namespace CadLink.Cad;

/// <summary>
/// El <b>alzado</b> de la placa base, dibujado a la derecha de la planta.
/// </summary>
/// <remarks>
/// Port de <c>DibujarDetallesElevacion</c> y compañía. El reparto —cuántas vistas, dónde y con qué
/// medidas— lo hace <see cref="ElevacionPlacaBase"/>, que no toca COM y se puede comprobar sin
/// AutoCAD delante. Aquí solo se manda a dibujar lo que esa clase diga.
/// </remarks>
public sealed partial class PlacaBaseDrawer
{
    /// <summary>
    /// Las vistas de canto, con su concreto, su placa, su columna, sus cartabones y sus anclas.
    /// </summary>
    /// <param name="xInicio">El canto derecho de la planta. El alzado arranca 60 cm más allá.</param>
    /// <param name="yPlaca">La cara de abajo de la placa en el alzado.</param>
    /// <remarks>
    /// <para>
    /// <b>CADA CORTE ES SU PROPIO BLOQUE</b>, y la planta el suyo. Antes iba todo en uno solo
    /// —la macro lo hacía así— y entonces no se podía llevar el corte a otro sitio de la hoja sin
    /// arrastrar la planta detrás. Lo pidió el usuario y además es lo razonable: en el plano la
    /// planta y sus cortes casi nunca acaban uno al lado del otro.
    /// </para>
    /// <para>
    /// Por eso esto se llama <b>después</b> del bloqueo de la planta y no antes: cada vista se
    /// dibuja, se agrupa en su bloque y se acota, en ese orden. Las cotas quedan fuera del bloque
    /// sin hacer nada especial, porque <c>Bloquear</c> salta la capa COTAS.
    /// </para>
    /// <para>
    /// El rótulo <c>ELEVACION "X"</c> también se queda fuera, por la misma razón: va en la capa
    /// ROTULOS. Es lo que ya pasaba antes.
    /// </para>
    /// </remarks>
    /// <param name="xDerecha">
    /// Sale la <b>X del canto derecho</b> del último corte, que es lo que necesita el reparto para
    /// no encimarle la placa siguiente. Sin cortes vale cero y manda la planta.
    /// </param>
    /// <returns>Los nombres de los bloques de los cortes, uno por vista.</returns>
    private List<string> Elevacion(
        PlacaBaseCad p, double xInicio, double yPlaca,
        double b, double h, double dadoX, double dadoY, double pX, double pY,
        double sepX, double sepY, double dAncX, double dAncY,
        out double xDerecha)
    {
        var bloques = new List<string>();
        xDerecha = 0;

        if (!p.DibujarElevacion)
        {
            return bloques;
        }

        var vistas = ElevacionPlacaBase.Construir(
            xInicio + (ElevacionPlacaBase.SeparacionDeLaPlantaCm * _escala),
            yPlaca, _escala, _hTxt,
            p.EspesorCm * _escala,
            p.ConCartabones,
            DireccionDeElevacion(p, _escala, b, dadoX, pX, esX: true, sepX, dAncX),
            DireccionDeElevacion(p, _escala, h, dadoY, pY, esX: false, sepY, dAncY),
            p.ConGrout ? p.EspesorGroutCm * _escala : 0);

        foreach (var v in vistas)
        {
            // UN BLOQUE POR CORTE. El rango se abre y se cierra alrededor de ESTA vista, así que
            // cada corte se lleva solo su geometría y no la de su vecino.
            var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);

            DibujarVistaDeElevacion(v);

            var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

            // El punto base es la esquina inferior izquierda del dado de ESTE corte: el mismo
            // criterio que la planta, que se bloquea por la esquina de la placa.
            var nombre = Bloquear(
                NombreDelCorte(p.Seccion, v.Id, p.EsPlacaAMuro),
                inicio, fin, v.Concreto[0], v.Concreto[1]);

            if (nombre.Length > 0)
            {
                bloques.Add(nombre);
            }

            // Y LAS COTAS DESPUÉS, para que se queden fuera del bloque. Da igual el orden —la capa
            // COTAS nunca entra—, pero así se lee en el mismo orden en que ocurre.
            CotasDelCorte(v);
        }

        // El canto derecho del que llegue más lejos. Cada vista sabe su centro y su ancho, así que
        // no hay que volver a recorrer la geometría ni repetir la cuenta de dónde se colocó.
        foreach (var v in vistas)
        {
            var canto = v.XCentro + (v.Ancho / 2);

            if (canto > xDerecha)
            {
                xDerecha = canto;
            }
        }

        // ---------- Y AL FINAL DE LOS CORTES, EL DETALLE DEL ANCLA SOLA ----------
        var bloqueAncla = DetalleDelAnclaSuelta(p, vistas, yPlaca, ref xDerecha);

        if (bloqueAncla.Length > 0)
        {
            bloques.Add(bloqueAncla);
        }

        return bloques;
    }

    /// <summary>
    /// El detalle de <b>una ancla sola</b>, acotado, a la derecha del último corte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo pidió el usuario: <i>«hazme aparte un detalle de la pura ancla con los datos
    /// correspondientes; ese detalle ponlo al final de los cortes»</i>. Y hace falta por algo
    /// concreto: en el corte de la placa el ancla sale enterrada —el concreto rayado detrás, la
    /// placa cruzándola— así que ni se lee el doblez ni se puede acotar sin amontonar cotas sobre
    /// las del dado.
    /// </para>
    /// <para>
    /// Va en <b>su propio bloque</b>, como cada corte: es un detalle que se lleva a otro sitio de la
    /// hoja o se repite en otro plano, y dentro del bloque de un corte no se podría.
    /// </para>
    /// <para>
    /// Se dibuja el ancla <b>de la primera vista</b>, que es la que manda en el detalle: las de X y
    /// las de Y comparten longitud y doblez salvo que se capturen distintas, y en ese caso el corte
    /// de cada dirección ya las enseña por separado.
    /// </para>
    /// </remarks>
    /// <param name="xDerecha">
    /// Entra el canto derecho de los cortes y <b>sale</b> el del detalle: así el reparto de la placa
    /// siguiente cuenta también con esto.
    /// </param>
    private string DetalleDelAnclaSuelta(
        PlacaBaseCad p, List<ElevacionPlacaBase.Vista> vistas, double yPlaca, ref double xDerecha)
    {
        if (!p.DibujarDetalleDeAncla || vistas.Count == 0)
        {
            return string.Empty;
        }

        var ancla = vistas[0].Anclas.FirstOrDefault();

        if (ancla.Diametro <= 0 || ancla.Ahogo <= 0)
        {
            return string.Empty;
        }

        var x = xDerecha + (DetalleDeAncla.SeparacionDelUltimoCorteCm * _escala)
                + (ancla.Diametro * 2);

        var detalle = DetalleDeAncla.Construir(
            x, yPlaca, ancla.Ahogo, LargoDeLaPata(ancla), ancla.Diametro,
            p.TextoDiamAnclaX.Trim().Length > 0 ? p.TextoDiamAnclaX : p.TextoDiamAnclaY,
            p.Marca, _escala, _hTxt);

        if (detalle is null)
        {
            return string.Empty;
        }

        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);

        Polilinea(detalle.Barra, PlacaBaseCapas.Anclas);

        Polilinea(detalle.Tuerca, PlacaBaseCapas.Anclas,
                  color: PlacaBaseCapas.ColorRoscaYTuerca);

        foreach (var arista in detalle.AristasTuerca)
        {
            Linea(arista[0], arista[1], arista[2], arista[3], PlacaBaseCapas.Anclas,
                  PlacaBaseCapas.ColorRoscaYTuerca);
        }

        foreach (var hebra in detalle.Rosca)
        {
            Polilinea(hebra, PlacaBaseCapas.Anclas, cerrada: false,
                      color: PlacaBaseCapas.ColorRoscaYTuerca);
        }

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        // El bloque se cierra AQUÍ, antes de las cotas y el rótulo: igual que la planta y los
        // cortes, el bloque se lleva solo la geometría y las cotas se quedan fuera para poder
        // moverlas sin entrar en la definición.
        var nombre = Bloquear(
            NombreDelDetalleDeAncla(p.Seccion), inicio, fin, detalle.Barra[0], detalle.Barra[1]);

        foreach (var c in detalle.Cotas)
        {
            if (c.Vertical)
            {
                CotaV(c.Desde, c.Hasta, c.Origen, c.Ref);
            }
            else
            {
                CotaH(c.Desde, c.Hasta, c.Origen, c.Ref);
            }
        }

        // El rótulo, centrado bajo la pieza y en un solo MTEXT: son cuatro renglones que se leen
        // juntos —qué ancla, su longitud, su doblez y el desarrollo que se pide al proveedor—.
        Mtexto(
            "\\pxqc;" + string.Join("\\P", detalle.Renglones.Select(Escapar)),
            detalle.Rotulo.X, detalle.Rotulo.Y, anclaje: 2);

        if (x + detalle.Ancho > xDerecha)
        {
            xDerecha = x + detalle.Ancho;
        }

        return nombre;
    }

    /// <summary>La pata del doblez de un ancla ya construida, medida sobre su eje.</summary>
    /// <remarks>
    /// Se saca del EJE y no del dato de la hoja: si el ancla se dibujó sin pata —porque la celda
    /// vino en cero— el detalle tampoco la lleva, y así no puede acotar un doblez que el plano no
    /// dibuja.
    /// </remarks>
    private static double LargoDeLaPata(ElevacionPlacaBase.AnclaDeCanto a) =>
        a.ConDoblez ? Math.Abs(a.Vastago[4] - a.Vastago[2]) : 0;

    /// <summary>El nombre del bloque del detalle del ancla.</summary>
    /// <remarks>
    /// Con el nombre de la sección delante, como los cortes: los bloques de una placa se ordenan
    /// juntos en el administrador de bloques, que es donde se van a buscar.
    /// </remarks>
    private static string NombreDelDetalleDeAncla(string seccion)
    {
        var s = (seccion ?? string.Empty).Trim();

        return (s.Length == 0 ? "PLACA BASE" : s) + " ANCLA";
    }

    /// <summary>El nombre del bloque de un corte: el de la sección más «CORTE X».</summary>
    /// <remarks>
    /// Con el nombre de la sección delante, los tres bloques de una placa se ordenan juntos en el
    /// administrador de bloques de AutoCAD, que es donde se van a buscar.
    /// </remarks>
    private static string NombreDelCorte(string seccion, string id, bool esPlacaAMuro)
    {
        var s = (seccion ?? string.Empty).Trim();

        // El respaldo dice QUE CLASE de placa es, por lo mismo que el titulo del rotulo: un bloque
        // llamado «PLACA BASE CORTE X» para una placa sobre una cadena se busca donde no esta.
        var respaldo = esPlacaAMuro ? "PLACA A MURO" : "PLACA BASE";

        return (s.Length == 0 ? respaldo : s) + " CORTE " + id;
    }

    /// <summary>Los datos de una dirección, ya en unidades de dibujo.</summary>
    /// <param name="anchoPlaca">La placa a lo ancho EN ESTA VISTA, ya orientada.</param>
    /// <param name="esX">
    /// <b>Los datos van cruzados igual que en planta.</b> La vista X toma la cantidad de cartabones
    /// de X, su longitud —E19— y su altura —F18—, que son los que en planta salen de las caras Y.
    /// Es la corrección que la propia macro documenta, y cambiarla aquí dejaría el alzado
    /// contradiciendo a la planta.
    /// </param>
    private static ElevacionPlacaBase.Direccion DireccionDeElevacion(
        PlacaBaseCad p, double escala, double anchoPlaca, double anchoDado, double anchoPerfil,
        bool esX, double sepBorde, double diamAncla) =>
        new(
            AnchoPlaca: anchoPlaca,
            AnchoDado: p.DibujarDado ? anchoDado : 0,
            AnchoPerfil: anchoPerfil,
            LongCartabon: (esX ? p.LongCartabonXCm : p.LongCartabonYCm) * escala,
            AltoCartabon: (esX ? p.AltoCartabonXCm : p.AltoCartabonYCm) * escala,
            CuantosCartabones: Math.Max(0, esX ? p.NCartabonesX : p.NCartabonesY),
            LongAnclaje: (esX ? p.LongAnclajeXCm : p.LongAnclajeYCm) * escala,
            LongAncla: (esX ? p.LongAnclaXCm : p.LongAnclaYCm) * escala,
            DoblezAncla: (esX ? p.DoblezAnclaXCm : p.DoblezAnclaYCm) * escala,
            SepBorde: sepBorde,
            DiamAncla: diamAncla,
            CuantasAnclas: Math.Max(0, esX ? p.NAnclasX : p.NAnclasY));

    private void DibujarVistaDeElevacion(ElevacionPlacaBase.Vista v)
    {
        var concreto = Polilinea(v.Concreto, PlacaBaseCapas.Concreto);

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // EL DADO DEL CORTE, RAYADO COMO EN PLANTA.
        //
        // Salía como un rectángulo vacío, así que en el corte el concreto no se distinguía del
        // aire: se leía como un hueco con las anclas colgando dentro. Va con el MISMO patrón, la
        // MISMA escala y la MISMA capa que el rayado del dado en planta —AR-CONC a 0.0002 en
        // CONCRETO, color por capa—, porque es la misma pieza vista de otro lado.
        //
        // SIN ISLAS, a diferencia de la planta. Allí el contorno de la placa entra como isla
        // porque la placa se dibuja ENCIMA del dado y taparía el rayado; aquí la placa y la cama
        // de grout están por fuera de la caja del concreto —empiezan justo en su cara de arriba—,
        // y lo único que la cruza son las anclas, que se dibujan después y con su grueso real, así
        // que se pintan sobre el rayado sin necesidad de recortarlo.
        //
        // Y AL FONDO, como en planta: si el rayado queda por encima, al seleccionar el corte se
        // agarra el achurado en lugar de la pieza.
        // ═══════════════════════════════════════════════════════════════════════════════════════
        if (concreto is not null)
        {
            var hatch = Hatch(
                PlacaBaseCapas.PatronDado, PlacaBaseCapas.EscalaHatchDado,
                concreto, null, PlacaBaseCapas.Concreto, PorCapa);

            if (hatch is not null)
            {
                AlFondo(new List<object> { hatch });
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // LA CAMA DE GROUT, ENTRE LA PLACA Y EL DADO.
        //
        // Solo si la hoja lo pide: sin grout, v.Grout viene en null y aquí no pasa nada.
        //
        // Va RAYADA, y con otro patrón que el dado —ANSI31 a 45° contra el AR-CONC del concreto—,
        // porque a esta escala una junta de 2 o 3 cm dibujada solo con su contorno se lee como una
        // línea doble y no como un material. Y en su propia capa, para poder apagarla sola.
        // ═══════════════════════════════════════════════════════════════════════════════════════
        if (v.Grout is { } grout)
        {
            var contorno = Polilinea(grout, PlacaBaseCapas.Grout);

            if (contorno is not null)
            {
                Hatch(PlacaBaseCapas.PatronGrout, PlacaBaseCapas.EscalaHatchGrout,
                      contorno, null, PlacaBaseCapas.Grout, PlacaBaseCapas.ColorGrout);
            }
        }

        var placa = Polilinea(v.Placa, PlacaBaseCapas.Placa);

        if (placa is not null)
        {
            // El mismo grueso de línea que la placa en planta: es la misma pieza.
            try
            {
                AcadConnection.Retry(() =>
                {
                    ((dynamic)placa).ConstantWidth = PlacaBaseCapas.AnchoLineaPlaca;
                    ((dynamic)placa).Update();
                });
            }
            catch (Exception ex)
            {
                Fallo("Ancho de la polilínea de la placa en el alzado", ex);
            }
        }

        Polilinea(v.Columna, PlacaBaseCapas.Perfiles);

        foreach (var c in v.Cartabones)
        {
            Polilinea(c, PlacaBaseCapas.Cartabones);
        }

        foreach (var a in v.Anclas)
        {
            // ═════════════════════════════════════════════════════════════════════════════════
            // EL ANCLA, VACÍA Y CON SUS DOS CARAS. SIN ANCHO DE POLILÍNEA.
            //
            // Lo pidió el usuario: «las anclas no las hagas con PEDIT, déjalas vacías pero con 2
            // líneas representando su grosor». Antes el grueso era el ANCHO de la polilínea —lo que
            // en AutoCAD se toca con PEDIT—, y eso dibuja una barra MACIZA: al plotear sale una
            // mancha negra, encima del rayado del concreto tapa lo que cruza, y el ancla no se
            // puede rotular por dentro.
            //
            // Ahora se dibuja el CONTORNO: las dos caras a medio diámetro del eje, cerrado, con el
            // codo del doblez resuelto a escuadra. Sigue siendo UNA entidad —se selecciona y se
            // mueve entera— y sigue saliendo a la medida de la barra: un 3/4" y un 2" se ven
            // distintos, que era lo que el ancho de polilínea vino a resolver en su día.
            //
            // El EJE —a.Vastago— se queda, pero solo para medir: de él salen las cotas, el ahogo y
            // la profundidad del concreto. Esa separación es lo que permitió cambiar el dibujo sin
            // tocar una sola cota.
            // ═════════════════════════════════════════════════════════════════════════════════
            Polilinea(a.Contorno, PlacaBaseCapas.Anclas);

            // ═════════════════════════════════════════════════════════════════════════════════
            // LA TUERCA Y EL ENROSCADO, EN LA CAPA DE ANCLAS Y EN COLOR 253.
            //
            // Los pidió el usuario: «con su enroscado al inicio y con su tuerca, en color 253 en la
            // capa de anclas». El color va en la ENTIDAD porque la capa es la misma: ahí está el
            // ancla entera y se apaga de una vez, pero el vástago va rojo y lleno y estas piezas
            // son líneas finas justo encima. En el mismo rojo se empastan con la barra.
            //
            // La tuerca lleva ahora sus dos ARISTAS: una tuerca hexagonal de frente enseña tres
            // caras, y sin las aristas el dibujo es una caja, que se puede leer como una silleta.
            // ═════════════════════════════════════════════════════════════════════════════════
            Polilinea(a.Tuerca, PlacaBaseCapas.Anclas,
                      color: PlacaBaseCapas.ColorRoscaYTuerca);

            foreach (var arista in a.AristasTuerca)
            {
                Linea(arista[0], arista[1], arista[2], arista[3], PlacaBaseCapas.Anclas,
                      PlacaBaseCapas.ColorRoscaYTuerca);
            }

            foreach (var hebra in a.Rosca)
            {
                Polilinea(hebra, PlacaBaseCapas.Anclas, cerrada: false,
                          color: PlacaBaseCapas.ColorRoscaYTuerca);
            }

            Linea(a.Arandela[0], a.Arandela[1], a.Arandela[2], a.Arandela[3], PlacaBaseCapas.Anclas);

            if (a.Remate is { } remate)
            {
                Linea(remate[0], remate[1], remate[2], remate[3], PlacaBaseCapas.Anclas);
            }
        }

        // El identificador SIEMPRE entre comillas, como en la macro: ELEVACION "X".
        Texto("ELEVACION \"" + v.Id + "\"", v.Rotulo.X, v.Rotulo.Y);
    }

    /// <summary>
    /// Las cotas del corte: el cartabón, el ancla y la cama de grout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El corte no llevaba ninguna: lo que se captura en F18, F19, E12 y E13 salía dibujado pero
    /// sin número, así que del plano no se podía sacar con cuánto se dibujó ni verificarlo en obra.
    /// Lo pidió el usuario y son <b>las medidas que se capturan</b>, no todas las que se podrían
    /// poner: la longitud y la altura del cartabón, la longitud vertical del ancla y su doblez, más
    /// el espesor del grout cuando lo hay.
    /// </para>
    /// <para>
    /// <b>Salen de la geometría de la vista y no de preguntarle a AutoCAD dónde quedó nada.</b> Es
    /// el mismo cuidado que con los leaders de los cartabones: para cuando esto corre,
    /// <c>Bloquear</c> ya copió el corte a la definición de su bloque y borró las originales, así
    /// que un <c>GetBoundingBox</c> devolvería un cero y las cotas se irían al origen del dibujo.
    /// </para>
    /// <para>
    /// El reparto de lados evita que se pisen entre ellas: a la <b>izquierda</b> el grout y el
    /// ancla, a la <b>derecha</b> la altura del cartabón, <b>arriba</b> su longitud y <b>abajo</b>
    /// el doblez. En cada lado hay como mucho dos, y con dos van a distancias distintas.
    /// </para>
    /// <para>
    /// Se acota <b>un</b> cartabón y <b>un</b> ancla, no los dos de cada pareja: son la misma pieza
    /// repetida en espejo —el mismo dato de la hoja— y acotar las dos es ensuciar el detalle con un
    /// número que ya está. Del ancla se elige la que NO se subió por el desfase, que es la que mide
    /// lo que dice la hoja.
    /// </para>
    /// </remarks>
    private void CotasDelCorte(ElevacionPlacaBase.Vista v)
    {
        // Las cuatro referencias de la vista, leídas de sus cajas: Caja() devuelve
        // {x1,y1, x2,y1, x2,y2, x1,y2}, así que [0] y [1] son la esquina inferior izquierda.
        var xIzq = v.Concreto[0];
        var xDer = v.Concreto[2];
        var yFondoDado = v.Concreto[1];
        var yDado = v.Concreto[5];
        var yPlaca = v.Placa[1];
        var yColumna = v.Columna[5];

        var o1 = 2.0 * _hTxt;
        var o2 = o1 + (2.5 * _hTxt);

        // ---------- El cartabón: su longitud arriba y su altura a la derecha ----------
        // El de la derecha, que es el que tiene sitio para las dos cotas. Sus puntos son los de
        // CartabonDeCanto: [0] el paño de la columna, [2] el canto de fuera, [7] su cara de arriba.
        if (v.Cartabones.Length > 0)
        {
            var c = v.Cartabones[^1];

            var xPano = c[0];
            var xFuera = c[2];
            var yAlto = c[7];

            // La longitud, por encima de la columna: ahí no hay nada que tapar.
            CotaH(Math.Min(xPano, xFuera), Math.Max(xPano, xFuera), yAlto, yColumna + o1);

            // Y la altura, medida desde la cara de arriba de la placa, que es de donde arranca.
            CotaV(v.Placa[5], yAlto, xFuera, xDer + o1);
        }

        // ---------- El ancla: su longitud vertical a la izquierda y su doblez abajo ----------
        if (v.Anclas.Length > 0)
        {
            // La más honda es la que no se subió por el desfase, y es la que mide lo capturado.
            var a = v.Anclas[0];

            foreach (var otra in v.Anclas)
            {
                if (otra.Ahogo > a.Ahogo)
                {
                    a = otra;
                }
            }

            var xAncla = a.Vastago[0];
            var yFondoAncla = a.Vastago[3];

            // DESDE LA CARA DE ARRIBA DEL DADO, no desde la placa: es la longitud que se captura,
            // la que se ahoga en el concreto. Sin grout las dos caras coinciden y da lo mismo; con
            // grout, medir desde la placa daría el espesor de la cama de más.
            CotaV(yFondoAncla, yDado, xAncla, xIzq - o2);

            if (a.ConDoblez)
            {
                // La pata, por debajo del dado: dentro del concreto la cota se perdería en el
                // rayado. Y el rótulo del corte ya baja cinco alturas de texto para dejarle sitio.
                CotaH(Math.Min(a.Vastago[2], a.Vastago[4]), Math.Max(a.Vastago[2], a.Vastago[4]),
                      yFondoAncla, yFondoDado - o1);
            }
        }

        // ---------- El grout: su espesor, pegado al canto izquierdo ----------
        // Es la cota más chica del corte —dos o tres centímetros— así que va en el offset corto y
        // sin nada más en su lado a esa distancia.
        if (v.Grout is not null)
        {
            CotaV(yDado, yPlaca, xIzq, xIzq - o1);
        }
    }

    /// <summary>Un TEXT de una línea, centrado en el punto.</summary>
    /// <remarks>
    /// TEXT y no MTEXT: es un renglón suelto y no lleva ningún código de párrafo, así que un MTEXT
    /// solo añadiría el ancho del cuadro y su enganche. Es lo que hace la macro.
    /// </remarks>
    private object? Texto(string s, double x, double y)
    {
        if (s.Trim().Length == 0)
        {
            return null;
        }

        // El rótulo de la vista —«ELEVACION "X"»— también cuenta para la envolvente: va centrado
        // en x, y con un identificador largo sobresale del concreto por los dos lados.
        Apuntar(x - (AnchoDeTexto(s, _hTxt) / 2), x + (AnchoDeTexto(s, _hTxt) / 2));

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _ms.AddText(s, Punto(x, y), _hTxt);

                t.Layer = PlacaBaseCapas.Rotulos;
                t.Color = PorCapa;
                t.StyleName = PlacaBaseCapas.EstiloTexto;

                // 10 = acAlignmentMiddleCenter. Y el punto se reafirma después, porque al cambiar
                // la alineación AutoCAD recoloca el texto respecto al punto anterior.
                t.Alignment = 10;
                t.TextAlignmentPoint = Punto(x, y);

                return (object?)t;
            });
        }
        catch (Exception ex)
        {
            Fallo("Texto del alzado", ex);
            return null;
        }
    }
}
