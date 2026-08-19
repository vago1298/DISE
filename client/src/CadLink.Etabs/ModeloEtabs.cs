namespace CadLink.Etabs;

/// <summary>Clasificación de un elemento leído del modelo.</summary>
public enum ClaseElemento
{
    Columna,
    Trabe,
    Diagonal,
    Muro,
    Losa
}

/// <summary>
/// Un elemento del modelo de ETABS, ya clasificado.
/// </summary>
/// <remarks>
/// La macro guarda esto en 56 arreglos paralelos con <c>ReDim Preserve</c>. Aquí es
/// una clase y una lista: desaparecen ~200 líneas de <c>ReDim</c> y con ellas el
/// riesgo de agregar un campo y olvidarlo en uno de los dos bloques, que es un
/// error que solo aparece cuando el modelo pasa de 2.000 elementos, es decir en el
/// proyecto grande del cliente y no en las pruebas.
/// </remarks>
public sealed class ElementoEtabs
{
    public ClaseElemento Clase { get; set; }

    /// <summary>Nivel al que pertenece.</summary>
    public string Story { get; set; } = string.Empty;

    /// <summary>Etiqueta de ETABS, o el nombre único si no tiene etiqueta.</summary>
    public string Etiqueta { get; set; } = string.Empty;

    /// <summary>Nombre de la sección o propiedad asignada.</summary>
    public string Seccion { get; set; } = string.Empty;

    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double Z1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double Z2 { get; set; }

    /// <summary>Ancho o espesor, en metros.</summary>
    public double AnchoM { get; set; }

    /// <summary>Peralte, en metros.</summary>
    public double PeralteM { get; set; }

    /// <summary>RECT, CIRC, I, TUBO, PIPE, C, T, L.</summary>
    public string Forma { get; set; } = "RECT";

    /// <summary>Espesor del patín, en metros. Solo en perfiles I, C y T.</summary>
    /// <remarks>
    /// Hace falta para dibujar el perfil DE VERDAD. Con solo el ancho y el peralte lo
    /// único que se puede dibujar es una caja, que es lo que se veía antes.
    /// </remarks>
    public double PatinM { get; set; }

    /// <summary>Espesor del alma, en metros. Solo en perfiles I, C y T.</summary>
    public double AlmaM { get; set; }

    /// <summary>Espesor de pared, en metros. Solo en tubos y cajones.</summary>
    public double ParedM { get; set; }

    /// <summary>Vértices en planta, solo para losas y muros.</summary>
    public List<(double X, double Y)> Vertices { get; } = new();

    /// <summary>
    /// Vértices con su elevación, solo para losas y muros.
    /// </summary>
    /// <remarks>
    /// Se guarda además de <see cref="Vertices"/> porque en un muro la Z de cada
    /// vértice es imprescindible: proyectado solo en planta, un muro se aplasta
    /// contra una línea y en la vista 3D no se podría dibujar su paño.
    /// </remarks>
    public List<(double X, double Y, double Z)> Vertices3D { get; } = new();

    public double LargoM =>
        Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)) + ((Z2 - Z1) * (Z2 - Z1)));

    /// <summary>Descripción corta para mostrar en una cuadrícula.</summary>
    public string Dimensiones =>
        AnchoM > 0 && PeralteM > 0
            ? $"{PeralteM * 100:N0} x {AnchoM * 100:N0} cm"
            : AnchoM > 0
                ? $"e = {AnchoM * 100:N0} cm"
                : "—";
}

/// <summary>Un nivel del modelo.</summary>
public sealed class NivelEtabs
{
    public string Nombre { get; set; } = string.Empty;

    public double ElevacionM { get; set; }

    public double AlturaM { get; set; }
}

/// <summary>
/// Resultado completo de leer el modelo.
/// </summary>
public sealed class ModeloEtabs
{
    public string Programa { get; set; } = string.Empty;

    public string Archivo { get; set; } = string.Empty;

    public List<NivelEtabs> Niveles { get; } = new();

    public List<ElementoEtabs> Elementos { get; } = new();

    /// <summary>Avisos no fatales: cosas que no se pudieron leer.</summary>
    public List<string> Avisos { get; } = new();

    public int Puntos { get; set; }

    public int Frames { get; set; }

    public int Areas { get; set; }

    public int Contar(ClaseElemento c) => Elementos.Count(e => e.Clase == c);

    /// <summary>Resumen para mostrarle al usuario.</summary>
    public string Resumen()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"Programa : {Programa}");
        s.AppendLine($"Modelo   : {Archivo}");
        s.AppendLine();
        s.AppendLine($"Se leyeron:      {Puntos} puntos, {Frames} frames, {Areas} áreas");
        s.AppendLine($"Niveles  : {Niveles.Count}");
        s.AppendLine();
        s.AppendLine($"  Columnas   : {Contar(ClaseElemento.Columna)}");
        s.AppendLine($"  Trabes     : {Contar(ClaseElemento.Trabe)}");
        s.AppendLine($"  Diagonales : {Contar(ClaseElemento.Diagonal)}");
        s.AppendLine($"  Muros      : {Contar(ClaseElemento.Muro)}");
        s.AppendLine($"  Losas      : {Contar(ClaseElemento.Losa)}");

        if (Avisos.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("Avisos:");
            foreach (var a in Avisos.Distinct().Take(12))
            {
                s.AppendLine("  - " + a);
            }
        }

        return s.ToString();
    }
}
