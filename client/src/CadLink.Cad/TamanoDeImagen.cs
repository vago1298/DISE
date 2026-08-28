namespace CadLink.Cad;

/// <summary>
/// A qué tamaño en píxeles se guarda una imagen, respetando el <b>aspecto</b> y los topes.
/// </summary>
/// <remarks>
/// <para>
/// Es la cuenta de la exportación en JPG de la vista en 3D. Parece trivial y no lo es, porque hay
/// que cumplir tres cosas a la vez y dos de ellas tiran en contra:
/// </para>
/// <list type="number">
///   <item>
///     <b>El aspecto que entra es el que sale.</b> La cámara se colocó con el aspecto del recuadro
///     de pantalla, así que si la imagen sale con otro, la pieza sale estirada. Esto no se negocia.
///   </item>
///   <item>
///     <b>El supermuestreo.</b> La imagen se <i>dibuja</i> más grande de lo que se guarda —dos
///     veces por lado— y luego se reduce promediando, que es lo que le quita los dientes de
///     sierra. Así que los topes hay que aplicarlos al tamaño <b>dibujado</b>, no al guardado: es
///     cuatro veces más superficie, y ahí está la trampa.
///   </item>
///   <item>
///     <b>Los topes.</b> Ningún lado de la superficie que se dibuja puede pasar del límite del
///     rasterizador, y su área tiene que caber en memoria. Un mapa de 100 megapíxeles son 400 MB.
///   </item>
/// </list>
/// <para>
/// <b>Está aquí y no en la aplicación a propósito</b>, por lo mismo que
/// <see cref="Envolvente"/>: <c>CadLink.App</c> no se puede compilar ni probar en el entorno donde
/// se trabaja, y una cuenta con dos topes que se pisan entre ellos sin una prueba que los recorra
/// no aguanta. La prueba está en <c>tools/prueba-tamano-imagen</c>.
/// </para>
/// </remarks>
public static class TamanoDeImagen
{
    /// <summary>El tamaño que se guarda y el tamaño al que hay que dibujar.</summary>
    /// <param name="Ancho">Ancho del archivo, en píxeles.</param>
    /// <param name="Alto">Alto del archivo, en píxeles.</param>
    /// <param name="AnchoQueSeDibuja">Ancho de la superficie intermedia, con el supermuestreo.</param>
    /// <param name="AltoQueSeDibuja">Alto de la superficie intermedia.</param>
    public sealed record Tamano(int Ancho, int Alto, int AnchoQueSeDibuja, int AltoQueSeDibuja);

    /// <summary>
    /// El tamaño de la imagen, bajándolo lo que haga falta para que quepa en los topes.
    /// </summary>
    /// <param name="aspecto">Ancho partido por alto del recuadro de origen. Tiene que salir igual.</param>
    /// <param name="anchoDeseado">El ancho que se querría guardar, en píxeles.</param>
    /// <param name="superMuestreo">Cuántas veces por lado se dibuja más grande. 1 = sin supermuestreo.</param>
    /// <param name="topeDeLado">Tope de píxeles por lado de la superficie que se DIBUJA.</param>
    /// <param name="topeDeArea">Tope de píxeles totales de la superficie que se DIBUJA.</param>
    /// <remarks>
    /// Los dos topes se aplican <b>en cadena y sobre los dos lados a la vez</b>, nunca sobre uno
    /// solo: recortando solo el ancho, la imagen saldría con otro aspecto y la pieza deformada. El
    /// de área baja por la <b>raíz</b> del exceso, que es lo que conserva la proporción.
    /// </remarks>
    public static Tamano Calcular(
        double aspecto,
        int anchoDeseado,
        int superMuestreo,
        int topeDeLado,
        long topeDeArea)
    {
        var s = Math.Max(1, superMuestreo);

        // Un aspecto imposible no puede dar una imagen imposible: se cae al cuadrado, que al
        // menos se ve.
        if (double.IsNaN(aspecto) || double.IsInfinity(aspecto) || aspecto <= 0)
        {
            aspecto = 1;
        }

        double ancho = Math.Max(1, anchoDeseado);
        var alto = ancho / aspecto;

        // ---------- Tope por LADO ----------
        //
        // El tope es de la superficie que se dibuja, así que en el tamaño que se guarda vale el
        // tope partido por el supermuestreo.
        if (topeDeLado > 0)
        {
            var cabe = (double)topeDeLado / s;

            var factor = Math.Min(1, Math.Min(cabe / ancho, cabe / alto));

            ancho *= factor;
            alto *= factor;
        }

        // ---------- Tope por ÁREA ----------
        if (topeDeArea > 0)
        {
            var area = ancho * s * alto * s;

            if (area > topeDeArea)
            {
                var factor = Math.Sqrt(topeDeArea / area);

                ancho *= factor;
                alto *= factor;
            }
        }

        // ---------- A ENTEROS, SIEMPRE HACIA ABAJO ----------
        //
        // Con redondeo al más cercano los topes se incumplían por poco y de verdad: en el caso
        // cuadrado, el área ideal daba 3316.6 por lado, 3317 al redondear, y 6634² son 44 009 956
        // píxeles contra un tope de 44 000 000. Poco, pero un tope que se puede pasar no es un
        // tope. Hacia abajo se cumple siempre, y lo que se pierde es medio píxel.
        var w = Math.Max(1, (int)Math.Floor(ancho));
        var h = Math.Max(1, (int)Math.Floor(alto));

        return new Tamano(w, h, w * s, h * s);
    }
}
