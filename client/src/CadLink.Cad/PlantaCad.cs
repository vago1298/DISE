namespace CadLink.Cad;

/// <summary>Tipo de elemento de una planta, a efectos del dibujo.</summary>
/// <remarks>
/// Es un espejo de <c>ClaseElemento</c> de CadLink.Etabs, y está duplicado
/// <b>a propósito</b>: CadLink.Cad no referencia a CadLink.Etabs. El dibujante no
/// tiene por qué saber que los datos vienen de ETABS, igual que
/// <see cref="SeccionCad"/> no sabe que vienen de una cuadrícula de WPF. Quien
/// traduce es la ventana, en un solo sitio.
/// </remarks>
public enum ClasePlanta
{
    Columna,
    Trabe,
    Muro,
    Losa,
    Diagonal
}

/// <summary>Un elemento de la planta, ya en el plano XY y en METROS.</summary>
public sealed class ElementoPlanta
{
    public ClasePlanta Clase { get; set; }

    /// <summary>Etiqueta del elemento en el modelo. Es lo que se rotula.</summary>
    public string Etiqueta { get; set; } = string.Empty;

    /// <summary>Nombre de la sección. Se rotula debajo de la etiqueta.</summary>
    public string Seccion { get; set; } = string.Empty;

    /// <summary>
    /// El <b>tipo</b> de la macro: CASTILLO, COLUMNA, DALA, TRABE, CONTRATRABE, DIAGONAL,
    /// MURO o LOSA.
    /// </summary>
    /// <remarks>
    /// Es más fino que <see cref="Clase"/> y hace falta para la <b>capa</b>: la macro manda
    /// el castillo a <c>E-CASTILLO</c> y la columna a <c>E-COLUMNA</c>, la dala a
    /// <c>E-DALA</c> y la trabe a <c>E-TRABE</c>, cada una con su color. Lo clasifica la
    /// ventana con <c>SeccionesModelo.ClasificaTipo</c>, que es el <c>ClasificaTipo</c> de
    /// la macro; si llega en blanco, el dibujante lo deduce de la clase.
    /// </remarks>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// La forma de la sección: RECT, CIRC, I, TUBO, PIPE, C, T, L, AREA.
    /// </summary>
    /// <remarks>
    /// Se usa para una sola cosa, pero importante: un perfil de acero va a la capa
    /// <c>E-ACERO</c>, como en la macro, en lugar de a la de su tipo.
    /// </remarks>
    public string Forma { get; set; } = "RECT";

    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    /// <summary>Ancho de la sección en metros: el espesor en un muro.</summary>
    public double AnchoM { get; set; }

    /// <summary>Peralte de la sección en metros.</summary>
    public double PeralteM { get; set; }

    /// <summary>
    /// De qué es el muro: <c>MAMPOSTERIA</c> o <c>CONCRETO</c>, si el modelo lo dice.
    /// </summary>
    /// <remarks>
    /// Decide una cosa que se ve mucho en el plano: <b>la línea de mampostería</b>, la
    /// polilínea ancha que la macro dibuja al centro del muro de block y no en el de
    /// concreto. Lo clasifica la ventana con la regla de la macro
    /// —<c>PALABRAS_MAMPOSTERIA</c>— porque es la que tiene las notas del modelo.
    /// </remarks>
    public string Material { get; set; } = string.Empty;

    /// <summary>Contorno del paño, para las losas.</summary>
    public List<(double X, double Y)> Vertices { get; } = new();

    /// <summary>Largo en planta, que NO es el largo real de una diagonal.</summary>
    public double LargoPlanta =>
        Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
}

/// <summary>
/// Todo lo que hace falta para dibujar una planta en AutoCAD.
/// </summary>
public sealed class PlantaCad
{
    /// <summary>Nombre del nivel, tal como lo llama el modelo.</summary>
    public string Nivel { get; set; } = string.Empty;

    /// <summary>Nombre del modelo, para el rótulo.</summary>
    public string Modelo { get; set; } = string.Empty;

    public List<ElementoPlanta> Elementos { get; } = new();

    /// <summary>
    /// Altura del texto de los rótulos, en metros de papel.
    /// </summary>
    /// <remarks>
    /// 0.25 m sobre una planta de 20 m se lee sin estorbar. Es el mismo criterio de
    /// la macro: el texto se dimensiona respecto al dibujo, no en puntos.
    /// </remarks>
    public double AlturaTexto { get; set; } = 0.25;

    /// <summary>¿Se rotula cada elemento con su etiqueta y su sección?</summary>
    public bool ConRotulos { get; set; } = true;

    /// <summary>
    /// Los ejes <b>verticales</b> de la cuadrícula: nombre y X, de izquierda a derecha.
    /// </summary>
    /// <remarks>
    /// Salen de la cuadrícula del modelo o, si el programa no la da, deducidos de las
    /// columnas y los muros. Vacío significa «esta planta va sin ejes».
    /// </remarks>
    public List<(string Id, double Ordenada)> EjesX { get; } = new();

    /// <summary>Los <b>horizontales</b>: nombre y Y, de abajo arriba.</summary>
    public List<(string Id, double Ordenada)> EjesY { get; } = new();
}
