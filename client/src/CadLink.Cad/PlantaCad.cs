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
    /// La etiqueta de <b>PIER</b> del muro: <c>M1</c>, <c>M2X</c>… Vacío si no tiene.
    /// </summary>
    /// <remarks>
    /// Es <b>lo único</b> que la macro rotula en un muro, y va en su capa aparte
    /// —<c>PIERS</c>—. El nombre de la propiedad no se rotula a propósito: era lo que
    /// llenaba la planta de «MURO TABICON 2 APLANADOS 15 CM» repetido en los 31 muros.
    /// Un muro sin pier asignado se queda sin rótulo, igual que allá.
    /// </remarks>
    public string Pier { get; set; } = string.Empty;

    /// <summary>
    /// El <b>giro de la sección</b> en planta, en grados. Solo en columnas y castillos.
    /// </summary>
    /// <remarks>
    /// Es el ángulo del eje local 2 que da <c>GetLocalAxes</c>, y es lo que hace que una
    /// columna de 20×60 girada 90° se vea de 60×20 en el plano, como se ve en ETABS. Va a
    /// la <b>inserción del bloque</b>, no a su geometría: así el bloque de la sección es
    /// uno solo y un <c>BLOCKREPLACE</c> conserva la orientación de cada columna.
    /// </remarks>
    public double AnguloGrados { get; set; }

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

    /// <summary>
    /// Las <b>cotas</b> de los dos extremos, en metros. Solo hacen falta en el CORTE.
    /// </summary>
    /// <remarks>
    /// En planta la Z no se usa —para eso es una planta— pero un corte por un eje es un
    /// alzado, y ahí la altura es la mitad del dibujo: sin la Z, una columna no tiene de
    /// dónde a dónde y un muro no tiene alto. Llegan del modelo tal cual, sin tocar.
    /// </remarks>
    public double Z1 { get; set; }

    public double Z2 { get; set; }

    /// <summary>Ancho de la sección en metros: el espesor en un muro.</summary>
    public double AnchoM { get; set; }

    /// <summary>Peralte de la sección en metros.</summary>
    public double PeralteM { get; set; }

    /// <summary>Espesor del <b>patín</b> del perfil —el <c>Tf</c> de ETABS—, en metros.</summary>
    /// <remarks>
    /// Con este y con <see cref="AlmaM"/> la sección de acero se dibuja <b>como es</b>: la I
    /// con sus dos patines y su alma, la canal con el alma a un lado, el ángulo con sus dos
    /// alas. Sin ellos no hay más remedio que la caja, y una IR de 25×15 y un cajón de 25×15
    /// se veían iguales en el plano.
    /// </remarks>
    public double PatinM { get; set; }

    /// <summary>Espesor del <b>alma</b> —el <c>Tw</c>—, en metros.</summary>
    public double AlmaM { get; set; }

    /// <summary>
    /// ¿Esta cadena lleva debajo un <b>muro de piso a techo</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decide una cosa que se ve en el plano: la cadena de cerramiento que <b>no</b> lleva su
    /// muro completo se dibuja con <c>ACAD_ISO02W100</c> —a trazos— y la que sí, con línea
    /// normal. Es información de obra: esa cadena no tiene sobre qué apoyarse en todo su
    /// tramo, porque ahí hay un vano o una ventana corrida.
    /// </para>
    /// <para>
    /// Lo calcula la <b>ventana</b>, no el dibujante, y no es un capricho: hay que mirar el
    /// nivel de <i>abajo</i> del modelo para saber si el muro sube de piso a techo, y el
    /// dibujante solo ve una planta. En una planta sin cadenas, o en la cimentación, el valor
    /// no se usa.
    /// </para>
    /// </remarks>
    public bool MuroDePisoATecho { get; set; }

    /// <summary>
    /// Este castillo se modeló como <b>shell de muro</b>, no como frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Importa para <b>una</b> cosa, y se ve en el plano: el <b>nombre del bloque</b>. En un
    /// frame, la sección fija las medidas —«K 15X15» mide 15×15 en todo el modelo—, así que el
    /// bloque puede llamarse como ella. En un shell no: la sección es la propiedad del
    /// <b>muro</b>, que solo fija el espesor, y el <b>largo lo pone cada shell</b>. Con el
    /// nombre de la sección a secas, el primer castillo creaba el bloque y todos los demás se
    /// insertaban con <i>sus</i> medidas: un castillo de 15×40 salía de 15×15.
    /// </para>
    /// <para>
    /// Con esta marca el nombre del bloque lleva las medidas detrás y cada tamaño tiene el
    /// suyo, así que <b>cada castillo sale completo</b> y un <c>BLOCKREPLACE</c> sigue
    /// cambiando de golpe todos los de ese tamaño.
    /// </para>
    /// </remarks>
    public bool DeShell { get; set; }

    /// <summary>Espesor de la <b>pared</b> del cajón o del tubo, en metros.</summary>
    /// <remarks>
    /// Es lo que le da su hueco: un cajón dibujado macizo parece una placa, y en un plano
    /// estructural eso es un dato equivocado, no un detalle de dibujo.
    /// </remarks>
    public double ParedM { get; set; }

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

    /// <summary>
    /// Las <b>notas</b> de la propiedad en el modelo, tal como vienen.
    /// </summary>
    /// <remarks>
    /// De aquí sale una decisión que se pidió explícita: el achurado <c>ANSI37</c> va
    /// <b>solo</b> en las losas cuya nota dice <c>VOLADO</c>. Reconocer el voladizo por la
    /// nota y no por la geometría es lo correcto en un modelo real: el ingeniero <b>sabe</b>
    /// cuál es el volado y lo escribe, mientras que contar lados apoyados se equivoca en
    /// cuanto una cadena está partida en el modelo.
    /// </remarks>
    public string Notas { get; set; } = string.Empty;

    /// <summary>Contorno del paño, para las losas.</summary>
    public List<(double X, double Y)> Vertices { get; } = new();

    /// <summary>Largo en planta, que NO es el largo real de una diagonal.</summary>
    public double LargoPlanta =>
        Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
}

/// <summary>
/// Todo lo que hace falta para dibujar una planta en AutoCAD.
/// </summary>
/// <summary>
/// Lo que hace falta para dibujar un <b>corte por un eje</b>: el alzado del modelo.
/// </summary>
/// <remarks>
/// <para>
/// Va aparte de <see cref="PlantaCad"/> porque un corte no es de un nivel: <b>atraviesa el
/// edificio entero</b>, así que lleva los elementos de todos los niveles con su cota. Es la
/// diferencia de fondo entre una planta y un alzado.
/// </para>
/// <para>
/// Se dibuja al lado de la planta estructural, a la distancia que diga
/// <c>CORTE_SEPARACION_M</c>, para que los dos dibujos se lean juntos: el corte dice las
/// alturas que la planta no puede decir.
/// </para>
/// </remarks>
public sealed class CorteCad
{
    /// <summary>Nombre del eje del corte: lo que dice su burbuja.</summary>
    public string Eje { get; set; } = string.Empty;

    /// <summary><c>true</c> si el corte va por un eje de los que corren en X.</summary>
    public bool EnX { get; set; }

    /// <summary>Coordenada del eje del corte, en metros.</summary>
    public double Ordenada { get; set; }

    /// <summary>Espesor de la rebanada que entra en el corte, en metros.</summary>
    public double EspesorM { get; set; } = 0.6;

    /// <summary>Nombre del modelo, para el rótulo.</summary>
    public string Modelo { get; set; } = string.Empty;

    /// <summary>Los elementos de TODOS los niveles, con su cota.</summary>
    public List<ElementoPlanta> Elementos { get; } = new();

    /// <summary>Los niveles con su cota, para rotularlos en el corte.</summary>
    public List<(string Nombre, double Z)> Niveles { get; } = new();

    /// <summary>
    /// Los <b>ejes que se ven</b> en el corte, con su nombre y su coordenada.
    /// </summary>
    /// <remarks>
    /// Son los <b>perpendiculares</b> al del corte: en un corte por un eje de los que van en X
    /// se recorre la Y, así que los que se cruzan —y los que hay que acotar— son los de la Y.
    /// Con ellos el corte lleva sus burbujas y sus cotas, igual que la planta, y las dos cosas
    /// se pueden comparar eje por eje.
    /// </remarks>
    public List<(string Id, double Ordenada)> Ejes { get; } = new();

    /// <summary>Altura del texto de los rótulos, en metros.</summary>
    public double AlturaTexto { get; set; } = 0.25;
}

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
