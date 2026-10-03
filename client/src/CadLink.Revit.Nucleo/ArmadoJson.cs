namespace CadLink.Revit.Nucleo;

// ============================================================================
//  EL ARMADO EN EL ARCHIVO DE INTERCAMBIO
//
//  CadLink lo escribe desde su tabla de secciones de concreto, con las MISMAS reglas que usa
//  para dibujar en AutoCAD: posiciones de las varillas en la seccion, estribos por zonas,
//  bastones con su longitud real. El complemento de Revit no vuelve a decidir nada de eso;
//  solo lo coloca en la pieza.
//
//  Va en dos partes, a proposito:
//
//    * ArmadoJson, UNO POR FILA de la tabla: lo que no depende de la longitud de la pieza
//      (medidas, recubrimiento, varillas en la seccion, estribo).
//    * ArmadoBarraJson, UNO POR BARRA del modelo: lo que si depende de su longitud real
//      (donde cae cada estribo, donde empieza y acaba cada baston). Una misma fila arma
//      trabes de 4 m y de 6 m, y sus estribos no caen en el mismo sitio.
//
//  Unidades: centimetros dentro de la seccion -que es como se captura en la tabla- y metros
//  a lo largo de la pieza, como el resto del archivo.
// ============================================================================

/// <summary>Una varilla longitudinal, vista en la seccion.</summary>
public sealed class VarillaJson
{
    /// <summary>Clave de la varilla: <c>#4</c>.</summary>
    public string Clave { get; set; } = string.Empty;

    public double DiamCm { get; set; }

    /// <summary>Centro, en cm desde la esquina de abajo a la izquierda de la seccion.</summary>
    public double XCm { get; set; }

    public double YCm { get; set; }

    /// <summary><c>Superior</c>, <c>Inferior</c> o <c>Lateral</c>.</summary>
    public string Lecho { get; set; } = string.Empty;
}

/// <summary>El armado de una fila de la tabla de secciones.</summary>
public sealed class ArmadoJson
{
    /// <summary>El ID de la fila: <c>T-01</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>Trabe</c>, <c>Contratrabe</c>, <c>Columna</c> o <c>Dado</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Base de la seccion, en cm. En la trabe es el ancho; en la columna, su lado X.</summary>
    public double BaseCm { get; set; }

    /// <summary>Peralte, en cm. En la columna, su lado Y.</summary>
    public double AlturaCm { get; set; }

    public double RecubrimientoCm { get; set; }

    public string ClaveEstribo { get; set; } = string.Empty;

    public double DiamEstriboCm { get; set; }

    /// <summary>Las varillas corridas y las laterales.</summary>
    public List<VarillaJson> Varillas { get; set; } = new();

    /// <summary>Si es una pieza tendida: trabe o contratrabe.</summary>
    public bool EsHorizontal =>
        Tipo is "Trabe" or "Contratrabe";
}

/// <summary>Un tramo de baston, ya colocado a lo largo de su pieza.</summary>
public sealed class TramoBastonJson
{
    public string Clave { get; set; } = string.Empty;

    public double DiamCm { get; set; }

    /// <summary><c>Superior</c> o <c>Inferior</c>.</summary>
    public string Lecho { get; set; } = string.Empty;

    /// <summary>Las X de sus varillas en la seccion, en cm, todas a la misma <see cref="YCm"/>.</summary>
    public List<double> XsCm { get; set; } = new();

    public double YCm { get; set; }

    /// <summary>Donde empieza y acaba, en m desde el arranque de la pieza.</summary>
    public double IniM { get; set; }

    public double FinM { get; set; }

    public bool GanchoIni { get; set; }

    public bool GanchoFin { get; set; }
}

/// <summary>Lo que lleva UNA barra del modelo: que armado y como cae en su longitud.</summary>
public sealed class ArmadoBarraJson
{
    /// <summary>El <see cref="ArmadoJson.Id"/> de su fila.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Como se emparejo la seccion con la fila: <c>nombre</c> o <c>medidas</c>.</summary>
    public string Por { get; set; } = string.Empty;

    /// <summary>La longitud con la que se calcularon estribos y bastones, en m.</summary>
    public double LargoM { get; set; }

    /// <summary>Los centros de los estribos, en m desde el arranque.</summary>
    public List<double> EstribosM { get; set; } = new();

    public List<TramoBastonJson> Bastones { get; set; } = new();
}

/// <summary>
/// Que fila de la tabla arma cada seccion del modelo de calculo.
/// </summary>
/// <remarks>
/// <para>
/// La tabla de CadLink se llama por ID -<c>T-01</c>- y ETABS por el nombre de la propiedad
/// -<c>T-01</c>, <c>V30X60</c>-. Se prueba en este orden:
/// </para>
/// <list type="number">
///   <item><b>Por nombre</b>: el de la seccion igual al ID de la fila, sin distinguir
///   mayusculas, espacios, guiones ni puntos.</item>
///   <item><b>Por medidas</b>: si no, la UNICA fila del mismo tipo con la misma base y el
///   mismo peralte, con medio centimetro de tolerancia. Si hay dos, no se elige: dos trabes de
///   30x60 con distinto armado no se pueden distinguir por sus medidas, y elegir una al azar
///   armaria mal la otra sin decir nada.</item>
/// </list>
/// </remarks>
public static class EmparejarArmado
{
    public sealed record Resultado(ArmadoJson? Armado, string Por, string Motivo);

    public static Resultado Buscar(
        string nombreSeccion, ClasePieza clase, double anchoM, double peralteM,
        IReadOnlyList<ArmadoJson> armados)
    {
        var compatibles = armados.Where(a => Compatible(a, clase)).ToList();

        var nombre = Normalizar(nombreSeccion);
        var porNombre = compatibles.FirstOrDefault(a => Normalizar(a.Id) == nombre);

        if (porNombre is not null)
        {
            return new Resultado(porNombre, "nombre", string.Empty);
        }

        bool Mide(ArmadoJson a, double b, double h) =>
            Math.Abs(a.BaseCm - b) <= 0.5 && Math.Abs(a.AlturaCm - h) <= 0.5;

        var bCm = anchoM * 100;
        var hCm = peralteM * 100;

        // En la columna los dos lados se pueden leer al reves: ETABS la puede tener girada.
        var porMedidas = compatibles
            .Where(a => Mide(a, bCm, hCm) || (clase == ClasePieza.Columna && Mide(a, hCm, bCm)))
            .ToList();

        if (porMedidas.Count == 1)
        {
            return new Resultado(porMedidas[0], "medidas", string.Empty);
        }

        if (porMedidas.Count > 1)
        {
            return new Resultado(null, string.Empty,
                $"ninguna fila se llama «{nombreSeccion}» y hay {porMedidas.Count} con sus medidas "
                + $"({bCm:0.#}x{hCm:0.#}): " + string.Join(", ", porMedidas.Select(a => a.Id))
                + ". Ponle a la fila el nombre de la seccion de ETABS.");
        }

        return new Resultado(null, string.Empty,
            $"ninguna fila de la tabla se llama «{nombreSeccion}» ni mide {bCm:0.#}x{hCm:0.#}");
    }

    private static bool Compatible(ArmadoJson a, ClasePieza clase) => clase switch
    {
        ClasePieza.Trabe => a.Tipo is "Trabe" or "Contratrabe",
        ClasePieza.Columna => a.Tipo is "Columna" or "Dado",
        _ => false
    };

    /// <summary>Sin mayusculas, espacios, guiones, guiones bajos ni puntos.</summary>
    public static string Normalizar(string? s) =>
        new string((s ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '_' or '.'))
            .Select(char.ToUpperInvariant)
            .ToArray());
}
