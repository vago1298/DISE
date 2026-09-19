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
    /// <returns>Los nombres de los bloques de los cortes, uno por vista.</returns>
    private List<string> Elevacion(
        PlacaBaseCad p, double xInicio, double yPlaca,
        double b, double h, double dadoX, double dadoY, double pX, double pY,
        double sepX, double sepY, double dAncX, double dAncY)
    {
        var bloques = new List<string>();

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
                NombreDelCorte(p.Seccion, v.Id), inicio, fin, v.Concreto[0], v.Concreto[1]);

            if (nombre.Length > 0)
            {
                bloques.Add(nombre);
            }

            // Y LAS COTAS DESPUÉS, para que se queden fuera del bloque. Da igual el orden —la capa
            // COTAS nunca entra—, pero así se lee en el mismo orden en que ocurre.
            CotasDelCorte(v);
        }

        return bloques;
    }

    /// <summary>El nombre del bloque de un corte: el de la sección más «CORTE X».</summary>
    /// <remarks>
    /// Con el nombre de la sección delante, los tres bloques de una placa se ordenan juntos en el
    /// administrador de bloques de AutoCAD, que es donde se van a buscar.
    /// </remarks>
    private static string NombreDelCorte(string seccion, string id)
    {
        var s = (seccion ?? string.Empty).Trim();

        return (s.Length == 0 ? "PLACA BASE" : s) + " CORTE " + id;
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
            // POLILÍNEA ABIERTA y no dos líneas: con doblez el vástago tiene tres puntos, y dos
            // líneas suetas se pueden mover por separado. El ancla es una pieza.
            var vastago = Polilinea(a.Vastago, PlacaBaseCapas.Anclas, cerrada: false);

            // ═════════════════════════════════════════════════════════════════════════════════
            // EL ANCLA, CON SU GRUESO REAL.
            //
            // El diámetro está capturado en la hoja —«Ø ancla X: 3/4"»— y en planta ya se
            // dibujaba con él: los dos círculos de cada ancla salen a su medida. En el alzado, en
            // cambio, el vástago era una línea de eje, así que un ancla del 3/4" y otra de 2" se
            // veían idénticas y el detalle no decía de qué barra hablaba.
            //
            // Se resuelve con el ANCHO DE LA POLILÍNEA y no con un contorno de dos caras a
            // propósito. Es lo mismo que ya se hace con la placa unas líneas más arriba, deja el
            // ancla como UNA pieza —una sola entidad que se selecciona y se mueve entera, con su
            // doblez resuelto por el vértice— y no toca la geometría, así que la previa y el
            // dibujo siguen saliendo de los mismos puntos.
            // ═════════════════════════════════════════════════════════════════════════════════
            if (vastago is not null && a.Diametro > 0)
            {
                try
                {
                    AcadConnection.Retry(() =>
                    {
                        ((dynamic)vastago).ConstantWidth = a.Diametro;
                        ((dynamic)vastago).Update();
                    });
                }
                catch (Exception ex)
                {
                    Fallo("Grueso del vástago del ancla en el alzado", ex);
                }
            }

            Polilinea(a.Tuerca, PlacaBaseCapas.Anclas);
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
