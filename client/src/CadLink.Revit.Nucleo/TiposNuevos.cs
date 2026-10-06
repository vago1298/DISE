namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LAS SECCIONES DE CADLINK QUE REVIT NO TIENE
//
//  Se pidio: «que me cree las secciones de concreto que no tenga en Revit pero si en CadLink;
//  si en CadLink tengo una trabe de 50x90 y en Revit no, debes crearla». Aqui se decide CUALES
//  faltan y como se llama el tipo nuevo. Crearlo -duplicar un tipo de la familia de concreto
//  y cambiarle b y h- lo hace el complemento.
// ============================================================================

/// <summary>Un tipo de columna o de trabe que ya hay en el proyecto, con o sin piezas.</summary>
public sealed record TipoExistente(
    ClasePieza Clase, string Familia, string Tipo, double? AnchoM, double? PeralteM, string? Descripcion = null);

/// <summary>
/// Un tipo que hay que crear: una MEDIDA de CadLink que Revit no tiene.
/// </summary>
/// <param name="Secciones">Las secciones de CadLink con esa medida. Si es una sola, el tipo
/// nuevo lleva sus propiedades -su Descripcion es su ID- y «Armar por tipo» la elige sola.</param>
public sealed record TipoPorCrear(ClasePieza Clase, string Nombre, double BaseCm, double AlturaCm, List<ArmadoJson> Secciones)
{
    /// <summary>La seccion que lo arma, si no hay duda de cual es.</summary>
    public ArmadoJson? Unica => Secciones.Count == 1 ? Secciones[0] : null;

    public string Texto =>
        $"{Nombre} ({string.Join(", ", Secciones.Select(s => s.Id.Trim()))})";
}

public static class TiposNuevos
{
    /// <summary>En que categoria de Revit va una seccion: trabe y contratrabe en armazon, columna y dado en pilar.</summary>
    public static ClasePieza? ClaseDe(ArmadoJson a) => a.Tipo switch
    {
        "Trabe" or "Contratrabe" => ClasePieza.Trabe,
        "Columna" or "Dado" => ClasePieza.Columna,
        _ => null
    };

    /// <summary>
    /// Las medidas de CadLink sin tipo en Revit. Una por medida y por elemento: si T-01 y T-07
    /// miden 50x90, sale un solo «TRABE 50x90», porque las dos se arman en el mismo tipo.
    /// </summary>
    /// <remarks>
    /// Un tipo ya existe si su Descripcion es el ID de la seccion -lo escribe «Armar por tipo»- o
    /// si mide lo mismo con medio centimetro de tolerancia. En la columna vale tambien girada:
    /// una de 30x50 sirve para una seccion de 50x30.
    /// </remarks>
    public static List<TipoPorCrear> Faltantes(
        IReadOnlyList<ArmadoJson> armados, IReadOnlyList<TipoExistente> existentes)
    {
        bool Mide(TipoExistente t, double b, double h) =>
            t.AnchoM is double tb && t.PeralteM is double th
            && Math.Abs((tb * 100) - b) <= 0.5 && Math.Abs((th * 100) - h) <= 0.5;

        bool Existe(ArmadoJson a, ClasePieza clase) => existentes.Any(t =>
            t.Clase == clase
            && ((EmparejarArmado.Normalizar(t.Descripcion).Length > 0
                 && EmparejarArmado.Normalizar(t.Descripcion) == EmparejarArmado.Normalizar(a.Id))
                || Mide(t, a.BaseCm, a.AlturaCm)
                || (clase == ClasePieza.Columna && Mide(t, a.AlturaCm, a.BaseCm))));

        var res = new List<TipoPorCrear>();

        foreach (var a in armados)
        {
            if (ClaseDe(a) is not { } clase || a.BaseCm <= 0 || a.AlturaCm <= 0 || Existe(a, clase))
            {
                continue;
            }

            var nombre = NombreDe(a);
            var ya = res.FirstOrDefault(p => p.Nombre == nombre);

            if (ya is null)
            {
                res.Add(new TipoPorCrear(clase, nombre, a.BaseCm, a.AlturaCm, new List<ArmadoJson> { a }));
            }
            else if (!ya.Secciones.Any(s => EmparejarArmado.Normalizar(s.Id) == EmparejarArmado.Normalizar(a.Id)))
            {
                ya.Secciones.Add(a);
            }
        }

        return res;
    }

    /// <summary>El nombre del tipo nuevo: el elemento y sus medidas en cm, «TRABE 50x90».</summary>
    public static string NombreDe(ArmadoJson a) =>
        $"{a.Tipo.Trim().ToUpperInvariant()} {Cm(a.BaseCm)}x{Cm(a.AlturaCm)}";

    private static string Cm(double v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
