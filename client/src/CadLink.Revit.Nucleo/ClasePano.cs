namespace CadLink.Revit.Nucleo;

/// <summary>
/// Decide si un pano es MURO o LOSA, que es lo que determina la categoria de Revit y, por
/// tanto, las familias que se ofrecen en el cuadro.
/// </summary>
/// <remarks>
/// <para>
/// Hace falta porque el lector lo decide <b>solo por la geometria</b>: en
/// <c>EtabsReader.cs</c> la clase sale de <c>esVertical ? Muro : Losa</c> y las NOTAS de la
/// propiedad no se miran. Si la nota dice LOSA y el paño acaba clasificado como muro, en el
/// cuadro salen familias de muro, y al modelar se llama a <c>Wall.Create</c> con un contorno
/// horizontal, que Revit rechaza. Un modelo entero de losas puede fallar asi de golpe.
/// </para>
/// <para>
/// Aqui se juntan las tres fuentes, y el orden importa:
/// </para>
/// <list type="number">
///   <item>
///   <b>La geometria, cuando es clara.</b> Manda porque es la unica que decide si la
///   llamada a Revit puede funcionar: no existe un suelo vertical ni un muro tumbado. Un
///   contorno casi horizontal es una losa aunque la nota diga otra cosa.
///   </item>
///   <item>
///   <b>Las notas</b>, cuando la geometria no es concluyente. Son la intencion de quien
///   modelo, y es lo que el usuario ve escrito en la propiedad.
///   </item>
///   <item>
///   <b>Lo que traiga el lector</b>, como ultimo recurso.
///   </item>
/// </list>
/// <para>
/// Y cuando la geometria contradice a la nota se deja <b>aviso</b>: seguramente hay algo mal
/// en el modelo de calculo, y callarlo dejaria una pieza en una categoria que nadie pidio.
/// </para>
/// </remarks>
public static class ClasePano
{
    /// <summary>Por encima de este coseno con la vertical, el pano es horizontal.</summary>
    /// <remarks>
    /// 0.85 son unos 32 grados de inclinacion. Una losa inclinada -una rampa, una escalera-
    /// sigue siendo una losa; mas alla de eso ya no se puede afirmar.
    /// </remarks>
    public const double CosHorizontal = 0.85;

    /// <summary>Por debajo de este coseno, el pano es vertical.</summary>
    public const double CosVertical = 0.25;

    /// <summary>Lo que se decidio y por que.</summary>
    public sealed record Resultado(ClasePieza Clase, string Motivo, string? Aviso);

    /// <summary>
    /// Si el texto del tipo de la propiedad habla de una losa.
    /// </summary>
    /// <remarks>
    /// Se compara con <c>Contains</c> para que LOSACERO cuente tambien: una losacero es una
    /// losa a efectos de en que categoria de Revit va.
    /// </remarks>
    public static bool NotaDiceLosa(string? tipoDeLasNotas) =>
        (tipoDeLasNotas ?? string.Empty).ToUpperInvariant().Contains("LOSA", StringComparison.Ordinal);

    public static bool NotaDiceMuro(string? tipoDeLasNotas) =>
        (tipoDeLasNotas ?? string.Empty).ToUpperInvariant().Contains("MURO", StringComparison.Ordinal);

    /// <summary>Decide la clase del pano.</summary>
    /// <param name="vertices">El contorno, en metros y en globales.</param>
    /// <param name="tipoDeLasNotas">
    /// Lo que devuelve <c>SeccionesModelo.TipoDeLasNotas(...)</c>: MURO, LOSA, LOSACERO... o
    /// vacio si las notas no dicen nada. Llega como TEXTO para que este proyecto no tenga que
    /// referenciar CadLink.Etabs.
    /// </param>
    /// <param name="declarada">La clase que trae el lector.</param>
    public static Resultado De(
        IReadOnlyList<PuntoJson>? vertices, string? tipoDeLasNotas, ClasePieza declarada)
    {
        var diceLosa = NotaDiceLosa(tipoDeLasNotas);
        var diceMuro = NotaDiceMuro(tipoDeLasNotas);

        var nz = VerticalidadDe(vertices);

        if (nz is null)
        {
            // Sin contorno utilizable no se puede mirar la geometria. Deciden las notas.
            if (diceLosa)
            {
                return new Resultado(ClasePieza.Losa, "lo dicen las notas de la propiedad", null);
            }

            if (diceMuro)
            {
                return new Resultado(ClasePieza.Muro, "lo dicen las notas de la propiedad", null);
            }

            return new Resultado(Normaliza(declarada), "es lo que trae el modelo", null);
        }

        var v = Math.Abs(nz.Value);

        if (v >= CosHorizontal)
        {
            var aviso = diceMuro
                ? "El pano «{0}» tiene el contorno horizontal, asi que se modela como LOSA, "
                  + "pero sus notas dicen MURO. Revisa la propiedad en el modelo de calculo."
                : null;

            return new Resultado(ClasePieza.Losa, "su contorno es horizontal", aviso);
        }

        if (v <= CosVertical)
        {
            var aviso = diceLosa
                ? "El pano «{0}» tiene el contorno vertical, asi que se modela como MURO, "
                  + "pero sus notas dicen LOSA. Revisa la propiedad en el modelo de calculo."
                : null;

            return new Resultado(ClasePieza.Muro, "su contorno es vertical", aviso);
        }

        // Inclinado de verdad: la geometria no decide y hablan las notas.
        if (diceLosa)
        {
            return new Resultado(ClasePieza.Losa,
                "esta inclinado y sus notas dicen losa", null);
        }

        if (diceMuro)
        {
            return new Resultado(ClasePieza.Muro,
                "esta inclinado y sus notas dicen muro", null);
        }

        return new Resultado(Normaliza(declarada),
            "esta inclinado y no hay notas, asi que se respeta lo que trae el modelo", null);
    }

    /// <summary>Un pano solo puede ser muro o losa; cualquier otra clase se toma como losa.</summary>
    private static ClasePieza Normaliza(ClasePieza c) =>
        c == ClasePieza.Muro ? ClasePieza.Muro : ClasePieza.Losa;

    /// <summary>
    /// La componente Z de la normal del contorno, o <c>null</c> si no se puede calcular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Por el metodo de Newell, y no con el producto cruz de dos lados: con tres vertices casi
    /// alineados -cosa normal en el contorno de un pano mallado- el producto cruz de dos lados
    /// da un vector diminuto y una direccion que es puro ruido. Newell suma la aportacion de
    /// todos los lados, asi que un vertice malo no manda.
    /// </para>
    /// <para>
    /// Esta aritmetica tambien esta en <c>CadLink.Ifc</c>. Se repite a proposito: este
    /// proyecto es el contrato con el complemento y no referencia a ningun otro, para que se
    /// pueda compilar y probar solo. Son doce lineas y no cambian nunca; atar el contrato a
    /// otro modulo por ahorrarlas costaria mas de lo que ahorra.
    /// </para>
    /// </remarks>
    public static double? VerticalidadDe(IReadOnlyList<PuntoJson>? v)
    {
        if (v is null || v.Count < 3)
        {
            return null;
        }

        double nx = 0, ny = 0, nz = 0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }

        var largo = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        // Area cero: todos los vertices en una linea, o el contorno se cierra sobre si mismo.
        return largo < 1e-9 ? null : nz / largo;
    }
}
