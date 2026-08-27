namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El muro que va <b>debajo de una cadena</b>: si la cadena lo tapa, el muro no se dibuja.
/// </summary>
/// <remarks>
/// <para>
/// Es <c>MarcarMurosTapados</c>. En el modelo el muro y su cadena de cerramiento son dos
/// elementos que ocupan <b>la misma línea en planta</b>: la cadena corre encima del muro, de
/// castillo a castillo. Dibujados los dos, el plano sale con <b>dos parejas de líneas</b>
/// pegadas —la del muro y la de la cadena— y eso es lo que se ve como una raya de más a cada
/// lado de la cadena.
/// </para>
/// <para>
/// La regla es la de la macro: si las cadenas cubren <c>TRASLAPE_MINIMO</c> —el <b>80 %</b>—
/// del largo del muro, el muro se marca como <b>tapado</b> y su geometría no se dibuja. Los
/// muros que <b>no</b> llevan cadena sí se dibujan, porque son los que hay que ver: un muro
/// sin cadena de cerramiento es una cosa que hay que revisar, no un dibujo de más.
/// </para>
/// <para>
/// La <b>línea de mampostería</b> del muro tapado sí se dibuja —<c>MAMPOSTERIA_AUNQUE_TAPADO</c>
/// en SI—, y con razón: es la marca de que ahí va block, y si desapareciera con el muro el
/// plano no diría de qué es la pared.
/// </para>
/// <para>
/// La cuenta es la <b>unión</b> de los tramos cubiertos, no la suma: dos cadenas que se
/// traslapan en un nudo cubren su tramo una sola vez, y sumándolas un muro con dos cadenitas
/// encimadas pasaría del 100 %.
/// </para>
/// </remarks>
public static class MuroBajoCadena
{
    private const double Nada = 1e-9;

    /// <summary>Qué le pasa a un muro: si está tapado y por qué cadena.</summary>
    /// <param name="Tapado">La cadena lo cubre lo suficiente: no se dibuja.</param>
    /// <param name="Cobertura">Fracción del muro que lleva cadena encima, de 0 a 1.</param>
    /// <param name="AnchoCadena">
    /// El ancho de la cadena <b>más ancha</b> que lo tapa. Es el <c>eTapaB</c> de la macro, y
    /// se usa para separar el rótulo del pier: si el pier se midiera solo con el espesor del
    /// muro, en un muro de 15 con una cadena de 25 el texto caería sobre la cadena.
    /// </param>
    public readonly record struct Estado(bool Tapado, double Cobertura, double AnchoCadena);

    /// <summary>
    /// ¿Este elemento es una <b>cadena</b> a efectos de tapar el muro? Es <c>EsCadena</c>.
    /// </summary>
    /// <remarks>
    /// La dala siempre; las trabes y las contratrabes solo con
    /// <c>CADENA_INCLUYE_TRABES</c> en SI, que es como está en la hoja: una trabe de
    /// entrepiso pasa por encima de un muro sin ser su cerramiento, y contarla escondería un
    /// muro que sí hay que ver.
    /// </remarks>
    public static bool EsCadena(ElementoPlanta el, bool incluirTrabes)
    {
        if (el.Clase != ClasePlanta.Trabe)
        {
            return false;
        }

        if (el.Tipo.Equals("DALA", StringComparison.OrdinalIgnoreCase) ||
            el.Tipo.Contains("CADENA", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return incluirTrabes &&
               (el.Tipo.Equals("TRABE", StringComparison.OrdinalIgnoreCase) ||
                el.Tipo.Equals("CONTRATRABE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cuánto del muro va debajo de una cadena, y cuál es la cadena más ancha.
    /// </summary>
    /// <param name="tolM">
    /// Desviación perpendicular que se admite: <c>TOLERANCIA_CADENA_CM</c>, 10 cm. Hace falta
    /// porque el eje de la cadena y el del muro casi nunca coinciden al milímetro.
    /// </param>
    /// <param name="traslapeMin">
    /// Fracción a partir de la cual el muro se da por tapado: <c>TRASLAPE_MINIMO</c>, 0.8.
    /// </param>
    public static Estado Como(
        ElementoPlanta muro,
        IReadOnlyList<ElementoPlanta> elementos,
        bool incluirTrabes = false,
        double tolM = 0.10,
        double traslapeMin = 0.8)
    {
        var dx = muro.X2 - muro.X1;
        var dy = muro.Y2 - muro.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-4)
        {
            return new Estado(false, 0, 0);
        }

        var ux = dx / largo;
        var uy = dy / largo;

        var tramos = new List<(double A, double B)>();
        double anchoCadena = 0;

        foreach (var c in elementos)
        {
            if (!EsCadena(c, incluirTrabes))
            {
                continue;
            }

            var vx = c.X2 - c.X1;
            var vy = c.Y2 - c.Y1;
            var lc = Math.Sqrt((vx * vx) + (vy * vy));

            if (lc < 1e-4)
            {
                continue;
            }

            vx /= lc;
            vy /= lc;

            // PARALELA al muro: el producto cruzado es el seno del ángulo entre las dos.
            // 0.035 son 2°, la misma holgura de la macro.
            if (Math.Abs((ux * vy) - (uy * vx)) >= 0.035)
            {
                continue;
            }

            // Y ENCIMADA: sus dos extremos, a menos de la tolerancia de la línea del muro.
            var d1 = Math.Abs((-uy * (c.X1 - muro.X1)) + (ux * (c.Y1 - muro.Y1)));
            var d2 = Math.Abs((-uy * (c.X2 - muro.X1)) + (ux * (c.Y2 - muro.Y1)));

            if (d1 >= tolM || d2 >= tolM)
            {
                continue;
            }

            var t1 = (ux * (c.X1 - muro.X1)) + (uy * (c.Y1 - muro.Y1));
            var t2 = (ux * (c.X2 - muro.X1)) + (uy * (c.Y2 - muro.Y1));

            if (t2 < t1)
            {
                (t1, t2) = (t2, t1);
            }

            t1 = Math.Max(0, t1);
            t2 = Math.Min(largo, t2);

            if (t2 <= t1)
            {
                continue;
            }

            tramos.Add((t1, t2));

            if (c.AnchoM > anchoCadena)
            {
                anchoCadena = c.AnchoM;
            }
        }

        if (tramos.Count == 0)
        {
            return new Estado(false, 0, 0);
        }

        var cubierto = LosaEnPlanta.Unidos(tramos).Sum(t => t.B - t.A) / largo;

        return new Estado(cubierto >= traslapeMin, cubierto, anchoCadena);
    }
}
