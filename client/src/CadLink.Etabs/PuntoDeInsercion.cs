namespace CadLink.Etabs;

/// <summary>
/// El <b>punto de inserción</b> de un marco: su punto cardinal y sus <b>offsets de nudo</b>.
/// </summary>
/// <remarks>
/// <para>
/// Es lo que en ETABS se asigna con <c>Assign → Frame → Insertion Point</c>, y es la razón
/// por la que un elemento puede <b>aparecer movido</b> respecto del eje de la cuadrícula: en
/// el modelo la barra se calcula sobre la línea que une sus dos nudos, pero la pieza REAL
/// —la que se construye y la que hay que dibujar— está donde la ponen su punto cardinal y
/// sus offsets. Un caso corriente: la trabe modelada en el eje de la losa y bajada
/// <c>-0.025</c> en el eje local 3 para que su paño coincida con el del muro.
/// </para>
/// <para>
/// Sin leer esto, el plano sale con las trabes y las columnas en el eje del nudo mientras
/// que en la pantalla de ETABS se ven corridas, y no hay forma de que las dos cosas cuadren.
/// </para>
/// <para>
/// <b>Las convenciones de CSI</b>, que son las que aquí se aplican y las mismas que ya usa
/// el lector para las dimensiones:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Columna</b> (barra vertical): el eje local 1 va hacia arriba, el local 2 es
///     horizontal y apunta al <c>+X</c> global cuando el giro es 0, y el local 3 sale de
///     <c>1 × 2</c>, o sea el <c>+Y</c>. Los dos ejes de la sección son horizontales, así
///     que <b>cualquier</b> offset mueve la columna en planta.
///   </item>
///   <item>
///     <b>Trabe</b> (barra horizontal): el eje local 1 corre a lo largo de la barra, el
///     local 2 es <b>vertical</b> —el <c>+Z</c>— y el local 3 es horizontal y perpendicular
///     a la barra. Por eso en una trabe el offset en el eje 2 solo la sube o la baja, y el
///     que la mueve en planta es el del <b>eje 3</b>.
///   </item>
///   <item>
///     Las dimensiones: <c>t3</c> se mide sobre el eje local <b>2</b> y <c>t2</c> sobre el
///     eje local <b>3</b>. Es la misma regla que ya sigue el lector —«en la columna el ancho
///     se mide sobre el eje 3, al contrario que en la viga»— y la que hace que una columna
///     de 20×60 salga de 20×60.
///   </item>
/// </list>
/// </remarks>
public static class PuntoDeInsercion
{
    /// <summary>El punto cardinal <b>centroide</b>, el de omisión: no mueve nada.</summary>
    public const int Centroide = 10;

    /// <summary>
    /// Los <b>ejes locales</b> de la barra, en coordenadas globales.
    /// </summary>
    /// <param name="vertical"><c>true</c> = columna; <c>false</c> = trabe o diagonal.</param>
    /// <param name="ux">Dirección de la barra en planta, componente X (sin normalizar).</param>
    /// <param name="uy">Ídem en Y.</param>
    /// <param name="anguloGrados">El giro de los ejes locales, el de <c>GetLocalAxes</c>.</param>
    public static (double[] E1, double[] E2, double[] E3) Ejes(
        bool vertical, double ux, double uy, double anguloGrados)
    {
        double[] e1, e2c, e3c;

        if (vertical)
        {
            // Columna: el 1 hacia arriba, el 2 al +X y el 3 = 1 x 2 = +Y.
            e1 = new[] { 0d, 0d, 1d };
            e2c = new[] { 1d, 0d, 0d };
            e3c = new[] { 0d, 1d, 0d };
        }
        else
        {
            var largo = Math.Sqrt((ux * ux) + (uy * uy));

            // Una barra sin largo en planta se trata como columna: no hay dirección que
            // seguir y así al menos los offsets globales se aplican igual.
            if (largo < 1e-9)
            {
                return Ejes(true, 0, 0, anguloGrados);
            }

            var dx = ux / largo;
            var dy = uy / largo;

            // Trabe: el 1 a lo largo, el 2 vertical (+Z) y el 3 = 1 x 2, que queda
            // horizontal y perpendicular a la barra.
            e1 = new[] { dx, dy, 0d };
            e2c = new[] { 0d, 0d, 1d };
            e3c = new[] { dy, -dx, 0d };
        }

        var t = anguloGrados * Math.PI / 180;
        var c = Math.Cos(t);
        var s = Math.Sin(t);

        // El giro de los ejes locales es un giro de 2 y 3 alrededor del 1.
        var e2 = new[]
        {
            (c * e2c[0]) + (s * e3c[0]),
            (c * e2c[1]) + (s * e3c[1]),
            (c * e2c[2]) + (s * e3c[2])
        };

        var e3 = new[]
        {
            (c * e3c[0]) - (s * e2c[0]),
            (c * e3c[1]) - (s * e2c[1]),
            (c * e3c[2]) - (s * e2c[2])
        };

        return (e1, e2, e3);
    }

    /// <summary>
    /// Pasa un offset a <b>globales</b>: si viene en locales, lo proyecta en los ejes.
    /// </summary>
    /// <param name="off">Las tres componentes: <c>1, 2, 3</c> si es local; <c>X, Y, Z</c> si no.</param>
    /// <param name="enLocales"><c>true</c> = el offset viene en ejes locales.</param>
    public static (double X, double Y, double Z) AGlobales(
        IReadOnlyList<double> off, bool enLocales,
        bool vertical, double ux, double uy, double anguloGrados)
    {
        var o1 = off.Count > 0 ? off[0] : 0;
        var o2 = off.Count > 1 ? off[1] : 0;
        var o3 = off.Count > 2 ? off[2] : 0;

        if (!enLocales)
        {
            // Ya vienen en globales: X, Y, Z.
            return (o1, o2, o3);
        }

        var (e1, e2, e3) = Ejes(vertical, ux, uy, anguloGrados);

        return (
            (o1 * e1[0]) + (o2 * e2[0]) + (o3 * e3[0]),
            (o1 * e1[1]) + (o2 * e2[1]) + (o3 * e3[1]),
            (o1 * e1[2]) + (o2 * e2[2]) + (o3 * e3[2]));
    }

    /// <summary>
    /// Cuánto se corre el <b>centro de la sección</b> por su punto cardinal, en ejes locales.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El punto cardinal dice <b>qué punto de la sección</b> va sobre la línea de los nudos,
    /// así que el centro de la pieza se corre al lado contrario: con el punto 8 —arriba al
    /// centro— la cara de arriba queda en la línea y el centro baja media altura.
    /// </para>
    /// <para>
    /// La numeración de CSI es una <b>cuadrícula de 3 × 3</b>: 1, 2 y 3 abajo; 4, 5 y 6 a
    /// media altura; 7, 8 y 9 arriba; y de izquierda a derecha dentro de cada terna. El 10 es
    /// el <b>centroide</b> y el 11 el centro de cortante, y ninguno de los dos corre nada en
    /// una sección simétrica, que es el caso de todo lo que se dibuja aquí.
    /// </para>
    /// <para>
    /// «Arriba» y «abajo» van sobre el eje local <b>2</b> —donde se mide <c>t3</c>— e
    /// «izquierda» y «derecha» sobre el <b>3</b>, donde se mide <c>t2</c>. Los espejos
    /// invierten el lado que les toca: el espejo respecto del eje 2 cambia el signo en 3.
    /// </para>
    /// </remarks>
    /// <param name="punto">El punto cardinal, de 1 a 11.</param>
    /// <param name="dim2">Dimensión de la sección sobre el eje local 2, o sea <c>t3</c>.</param>
    /// <param name="dim3">Dimensión sobre el eje local 3, o sea <c>t2</c>.</param>
    public static (double D2, double D3) PorPuntoCardinal(
        int punto, double dim2, double dim3, bool espejo2 = false, bool espejo3 = false)
    {
        // El centroide y el centro de cortante no corren nada; fuera de rango, tampoco.
        if (punto < 1 || punto > 9)
        {
            return (0, 0);
        }

        var columna = (punto - 1) % 3;   // 0 izquierda, 1 centro, 2 derecha
        var fila = (punto - 1) / 3;      // 0 abajo,     1 medio,  2 arriba

        // Si la cara IZQUIERDA está en la línea, el centro se va a la derecha, y al
        // contrario. Lo mismo arriba y abajo.
        var d3 = columna switch
        {
            0 => dim3 / 2,     // izquierda en la línea -> el centro a la derecha
            2 => -dim3 / 2,    // derecha en la línea   -> el centro a la izquierda
            _ => 0d
        };

        var d2 = fila switch
        {
            0 => dim2 / 2,     // abajo en la línea  -> el centro sube
            2 => -dim2 / 2,    // arriba en la línea -> el centro baja
            _ => 0d
        };

        if (espejo2)
        {
            d3 = -d3;
        }

        if (espejo3)
        {
            d2 = -d2;
        }

        return (d2, d3);
    }

    /// <summary>
    /// Lo que hay que <b>mover en planta</b> un extremo de la barra: todo junto.
    /// </summary>
    /// <remarks>
    /// Suma el corrimiento del punto cardinal —que es el mismo en los dos extremos, porque la
    /// sección es la misma— y el offset del nudo de ese extremo, y devuelve solo las
    /// componentes X e Y. La Z no se toca a propósito: <b>en planta no se ve</b>, y mover la
    /// elevación de una trabe 2.5 cm podría cambiar el nivel al que se asigna, que es un
    /// destrozo mucho peor que el que se quiere arreglar.
    /// </remarks>
    /// <param name="offset">El offset del nudo, tres componentes.</param>
    /// <param name="enLocales"><c>true</c> = el offset viene en ejes locales.</param>
    public static (double Dx, double Dy) EnPlanta(
        bool vertical, double ux, double uy, double anguloGrados,
        IReadOnlyList<double> offset, bool enLocales,
        int puntoCardinal, double dim2, double dim3,
        bool espejo2 = false, bool espejo3 = false)
    {
        var (c2, c3) = PorPuntoCardinal(puntoCardinal, dim2, dim3, espejo2, espejo3);

        // El corrimiento del punto cardinal SIEMPRE va en ejes locales: es una medida de la
        // sección, no del modelo.
        var (cx, cy, _) = AGlobales(
            new[] { 0d, c2, c3 }, true, vertical, ux, uy, anguloGrados);

        var (ox, oy, _) = AGlobales(offset, enLocales, vertical, ux, uy, anguloGrados);

        return (cx + ox, cy + oy);
    }
}
