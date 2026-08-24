namespace CadLink.Cad;

/// <summary>
/// La tabla de <b>capas y colores</b> de las macros, en un solo sitio.
/// </summary>
/// <remarks>
/// <para>
/// Es la lista de <c>CrearCapa</c> de la macro de sección estructural, palabra por palabra y número
/// por número:
/// </para>
/// <code>
/// CrearCapa "VAR_#2",   150
/// CrearCapa "VAR_#2.5",   6
/// CrearCapa "VAR_#3",   132
/// CrearCapa "VAR_#4",   142
/// CrearCapa "VAR_#5",   160
/// CrearCapa "VAR_#6",     4
/// CrearCapa "VAR_#8",     1
/// CrearCapa "VAR_#10",    6
/// CrearCapa "VAR_#12",   15
/// CrearCapa "TEXTOS",     3
/// CrearCapa "CONCRETO",   8
/// CrearCapa "ESTRIBOS", 150
/// </code>
/// <para>
/// <b>Por qué está aquí y no en cada dibujante.</b> Estaba escrita en el de secciones y en ningún
/// otro, así que el de zapatas creaba <c>VAR_#5</c> <b>sin color</b> y AutoCAD la dejaba en blanco:
/// se capturaba una varilla del #5 y salía blanca en lugar del 160 de la macro. Con la tabla
/// compartida, los cinco dibujantes ponen el mismo color a la misma capa, que es lo que permite que
/// un plano hecho a trozos —una sección aquí, una zapata allá— se vea de una pieza.
/// </para>
/// <para>
/// <b>El color se fuerza, no se respeta.</b> Para estas doce capas, si ya existen se les vuelve a
/// poner su color. Es lo que hace <c>CrearCapa</c> en el módulo de la macro —<c>Layers.Add</c>
/// devuelve la que ya está y le asigna el color igual—, y es lo que se pidió: son los colores del
/// juego de planos, no una preferencia de cada dibujo. Las capas que <b>no</b> están en la tabla
/// —<c>COTAS</c>, <c>ROTULOS</c>, <c>TERRENO</c>, los bloques— se dejan como estén si ya existen.
/// </para>
/// </remarks>
internal static class CapasCad
{
    /// <summary>Prefijo de las capas de varilla: <c>VAR_#4</c>.</summary>
    public const string PrefijoVarilla = "VAR_";

    /// <summary>Lo que devuelve <see cref="Color"/> cuando la capa no es de la macro.</summary>
    public const int SinColor = -1;

    /// <summary>La tabla, tal cual.</summary>
    private static readonly Dictionary<string, int> Tabla =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["VAR_#2"] = 150,
            ["VAR_#2.5"] = 6,
            ["VAR_#3"] = 132,
            ["VAR_#4"] = 142,
            ["VAR_#5"] = 160,
            ["VAR_#6"] = 4,
            ["VAR_#8"] = 1,
            ["VAR_#10"] = 6,
            ["VAR_#12"] = 15,
            ["TEXTOS"] = 3,
            ["CONCRETO"] = 8,
            ["ESTRIBOS"] = 150,
        };

    /// <summary>El color ACI de una capa de la macro, o <see cref="SinColor"/>.</summary>
    /// <remarks>
    /// Se llama <c>ColorDeCapa</c> y no <c>Color</c> a propósito: <c>Color</c> a secas es el tipo de
    /// WPF, y un estático con ese nombre hace que el verificador de <c>validar.py</c> señale como
    /// sospechoso cada <c>Color</c> del proyecto de ventana.
    /// </remarks>
    public static int ColorDeCapa(string? capa) =>
        capa is not null && Tabla.TryGetValue(capa.Trim(), out var c) ? c : SinColor;

    /// <summary>¿Esta capa lleva color de macro, y por tanto se le fuerza?</summary>
    public static bool EsDeLaMacro(string? capa) => ColorDeCapa(capa) != SinColor;

    /// <summary>
    /// El color de la capa de una varilla por su clave —<c>#5</c>— en lugar de por su capa.
    /// </summary>
    /// <remarks>
    /// La clave llega ya normalizada con su <c>#</c>. Un diámetro que no esté en la tabla —la hoja
    /// solo ofrece los nueve de la macro, así que no debería pasar— se queda sin color en lugar de
    /// caer en el 7: el blanco es justo el síntoma que se reportó, y una capa sin tocar es más fácil
    /// de ver que una capa mal pintada.
    /// </remarks>
    public static int ColorDeVarilla(string? clave) =>
        ColorDeCapa(PrefijoVarilla + (clave ?? string.Empty).Trim());
}
