namespace CadLink.Revit.Nucleo;

/// <summary>
/// Las categorias de Revit en las que se puede modelar lo que trae un modelo de ETABS.
/// </summary>
/// <remarks>
/// Son cinco clases de pieza repartidas en cuatro categorias: una trabe y una diagonal
/// viven las dos en <see cref="Estructura"/>, que es la categoria de Revit
/// <c>Structural Framing</c>, y se distinguen por su parametro de uso estructural.
/// </remarks>
public enum CategoriaRevit
{
    /// <summary>Pilares estructurales. En la API, <c>OST_StructuralColumns</c>.</summary>
    ColumnaEstructural,

    /// <summary>Estructura: vigas y arriostres. <c>OST_StructuralFraming</c>.</summary>
    Estructura,

    /// <summary>Muros. <c>OST_Walls</c>. Familia de sistema.</summary>
    Muro,

    /// <summary>Suelos. <c>OST_Floors</c>. Familia de sistema.</summary>
    Piso
}

/// <summary>
/// Un tipo de Revit que EXISTE en el proyecto abierto, descrito sin tocar la Revit API.
/// </summary>
/// <remarks>
/// <para>
/// Lo rellena el complemento recorriendo el documento; el nucleo solo lo consume. Esa es la
/// frontera que permite probar el emparejador sin Revit: aqui no hay ningun
/// <c>FamilySymbol</c>, solo texto y numeros.
/// </para>
/// <para>
/// <see cref="Id"/> es el <c>ElementId</c> de Revit, guardado como entero largo. El nucleo
/// no lo interpreta: lo arrastra para que el complemento pueda volver a encontrar el tipo
/// sin buscarlo por nombre, que es ambiguo.
/// </para>
/// </remarks>
public sealed class TipoRevit
{
    public CategoriaRevit Categoria { get; set; }

    /// <summary>La familia. En muros y pisos, el nombre de la familia de sistema.</summary>
    public string Familia { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> en muros y pisos: son familias de SISTEMA y no se cargan de un archivo.
    /// </summary>
    /// <remarks>
    /// Importa para el cuadro de mapeo: en una familia de sistema no se puede ofrecer
    /// "carga otra", y si falta el tipo que hace falta hay que duplicarlo dentro del
    /// proyecto.
    /// </remarks>
    public bool EsSistema { get; set; }

    /// <summary>El <c>ElementId</c>, tal cual, sin interpretar.</summary>
    public long Id { get; set; }

    /// <summary>Lo que el complemento consiguio leer de los parametros del tipo, en metros.</summary>
    public double? AnchoM { get; set; }

    public double? PeralteM { get; set; }

    public double? EspesorM { get; set; }

    /// <summary>
    /// La forma, si el complemento la pudo determinar leyendo los parametros del tipo.
    /// </summary>
    /// <remarks>
    /// <c>null</c> significa "no se sabe", no "no tiene". Cuando es nula, el emparejador la
    /// deduce del nombre, que es menos fiable pero funciona con las plantillas normales.
    /// </remarks>
    public FormaSeccion? Forma { get; set; }

    /// <summary>Como se ensena en el cuadro: <c>Familia : Tipo</c>.</summary>
    public string NombreCompleto =>
        string.IsNullOrWhiteSpace(Familia) ? Tipo : Familia + " : " + Tipo;

    public override string ToString() => NombreCompleto;
}

/// <summary>Todo lo que hay cargado en el proyecto de Revit.</summary>
public sealed class CatalogoRevit
{
    public List<TipoRevit> Tipos { get; set; } = new();

    /// <summary>Los niveles del proyecto de Revit, por nombre y cota en metros.</summary>
    public List<NivelJson> Niveles { get; set; } = new();

    /// <summary>Los tipos de una categoria, ordenados para que el desplegable sea estable.</summary>
    public List<TipoRevit> DeCategoria(CategoriaRevit c) =>
        Tipos.Where(t => t.Categoria == c)
             .OrderBy(t => t.Familia, StringComparer.CurrentCultureIgnoreCase)
             .ThenBy(t => t.Tipo, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    /// <summary>Las familias distintas de una categoria.</summary>
    public List<string> FamiliasDe(CategoriaRevit c) =>
        Tipos.Where(t => t.Categoria == c)
             .Select(t => t.Familia)
             .Distinct(StringComparer.CurrentCultureIgnoreCase)
             .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    /// <summary>Los tipos de una familia concreta.</summary>
    public List<TipoRevit> TiposDe(CategoriaRevit c, string familia) =>
        Tipos.Where(t => t.Categoria == c
                         && string.Equals(t.Familia, familia, StringComparison.CurrentCultureIgnoreCase))
             .OrderBy(t => t.Tipo, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    public TipoRevit? PorId(long id) => Tipos.FirstOrDefault(t => t.Id == id);

    public TipoRevit? PorNombre(CategoriaRevit c, string familia, string tipo) =>
        Tipos.FirstOrDefault(t =>
            t.Categoria == c
            && string.Equals(t.Familia, familia, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(t.Tipo, tipo, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>La categoria de Revit que le toca a cada clase de pieza.</summary>
public static class Categorias
{
    public static CategoriaRevit De(ClasePieza clase) => clase switch
    {
        ClasePieza.Columna => CategoriaRevit.ColumnaEstructural,
        ClasePieza.Trabe => CategoriaRevit.Estructura,
        ClasePieza.Diagonal => CategoriaRevit.Estructura,
        ClasePieza.Muro => CategoriaRevit.Muro,
        ClasePieza.Losa => CategoriaRevit.Piso,

        // No hay respaldo silencioso: una clase nueva tiene que decidirse a mano, porque
        // meterla en la categoria equivocada produce un modelo que parece bien.
        _ => throw new ArgumentOutOfRangeException(
            nameof(clase), clase, "No se sabe en que categoria de Revit va esta clase.")
    };

    /// <summary>Si la pieza se modela como familia de sistema (muros y pisos).</summary>
    public static bool EsDeSistema(ClasePieza clase) =>
        clase is ClasePieza.Muro or ClasePieza.Losa;

    /// <summary>Como se llama la categoria en la pantalla.</summary>
    public static string Nombre(CategoriaRevit c) => c switch
    {
        CategoriaRevit.ColumnaEstructural => "Pilares estructurales",
        CategoriaRevit.Estructura => "Estructura",
        CategoriaRevit.Muro => "Muros",
        CategoriaRevit.Piso => "Suelos",
        _ => c.ToString()
    };

    /// <summary>Como se llama la clase de pieza en la pantalla.</summary>
    public static string Nombre(ClasePieza c) => c switch
    {
        ClasePieza.Columna => "columna",
        ClasePieza.Trabe => "trabe",
        ClasePieza.Diagonal => "diagonal",
        ClasePieza.Muro => "muro",
        ClasePieza.Losa => "losa",
        _ => c.ToString().ToLowerInvariant()
    };
}
