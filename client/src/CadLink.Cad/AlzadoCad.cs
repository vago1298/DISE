namespace CadLink.Cad;

/// <summary>Qué tipo de elemento es, para decidir cómo se dibuja el alzado.</summary>
public enum TipoElemento
{
    /// <summary>Trabe o contratrabe: alzado <b>horizontal</b>.</summary>
    Trabe,

    /// <summary>Contratrabe. Cambia el título y la posición del rótulo.</summary>
    Contratrabe,

    /// <summary>Columna: alzado <b>vertical</b>, y se le quita el último estribo.</summary>
    Columna,

    /// <summary>Dado: alzado vertical, de 1 m si no se indica longitud.</summary>
    Dado
}

/// <summary>
/// Datos de un alzado listos para dibujar.
/// </summary>
/// <remarks>
/// Se alimenta de las mismas columnas que la sección, más la <b>W</b>, que es la
/// longitud del elemento.
/// </remarks>
public sealed class AlzadoCad
{
    public TipoElemento Tipo { get; set; } = TipoElemento.Trabe;

    public string Elemento { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    /// <summary>Columna C.</summary>
    public double BaseCm { get; set; }

    /// <summary>Columna D.</summary>
    public double AlturaCm { get; set; }

    public double RecubrimientoCm { get; set; }

    /// <summary>
    /// Columna W, en metros. <c>0</c> = se calcula a partir de las separaciones.
    /// </summary>
    public double LongitudM { get; set; }

    /// <summary>Columna T. La macro la lee en cm y la pasa a metros.</summary>
    public double GanchoCm { get; set; }

    /// <summary>
    /// Separaciones de las tres zonas L/4 - L/2 - L/4, en centímetros.
    /// </summary>
    public double[] SeparacionesCm { get; set; } = { 15, 15, 15 };

    public VarCad Estribo { get; set; }

    /// <summary>
    /// Estribo que se dibuja en el alzado. Con diamante, la macro usa el diámetro
    /// del diamante en lugar del principal.
    /// </summary>
    public VarCad EstriboDibujo { get; set; }

    public LechoCad Superior { get; set; } = new();

    public LechoCad Inferior { get; set; } = new();

    public int NLateral { get; set; }

    public VarCad Lateral { get; set; }

    public string Fc { get; set; } = string.Empty;

    public string Escala { get; set; } = string.Empty;

    public string Separacion { get; set; } = string.Empty;

    public bool Diamante { get; set; }

    public VarCad EstriboDiamanteVar { get; set; }

    public ModoSeccion Modo { get; set; } = ModoSeccion.Tipo1SinRelleno;

    /// <summary>Alzado vertical: columnas y dados.</summary>
    public bool EsVertical => Tipo is TipoElemento.Columna or TipoElemento.Dado;

    /// <summary>Nombre del tipo como lo escribe la macro en el título.</summary>
    public string TipoTexto => Tipo switch
    {
        TipoElemento.Contratrabe => "CONTRATRABE",
        TipoElemento.Columna => "COLUMNA",
        TipoElemento.Dado => "DADO",
        _ => "TRABE"
    };
}
