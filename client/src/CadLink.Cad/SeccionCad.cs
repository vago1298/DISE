namespace CadLink.Cad;

/// <summary>
/// Estilo con el que se dibujan TODAS las secciones.
/// </summary>
/// <remarks>
/// <para>
/// <b>CUIDADO: estos números están al revés que la celda AC de la macro.</b> Es
/// intencional, a pedido expreso del usuario, pero hay que tenerlo presente:
/// </para>
/// <list type="table">
///   <item>
///     <term>Aquí</term>
///     <description>tipo 1 = SIN relleno · tipo 2 = RELLENA</description>
///   </item>
///   <item>
///     <term>La macro</term>
///     <description><c>MODO_RELLENA = 1</c> · <c>MODO_SOLO_HATCH = 2</c></description>
///   </item>
/// </list>
/// <para>
/// Por eso el importador de Excel <b>no puede</b> convertir la celda AC a este enum
/// con una conversión numérica directa: tiene que usar
/// <see cref="ModoSeccionExt.DesdeCeldaAC"/>, que hace la traducción explícita.
/// </para>
/// </remarks>
public enum ModoSeccion
{
    /// <summary>
    /// <b>Tipo 1</b>, sección sin relleno. Solo el patrón AR-CONC: sin fondo
    /// sólido, sin relleno del estribo, y el contorno del estribo con el color de
    /// su capa.
    /// </summary>
    Tipo1SinRelleno = 1,

    /// <summary>
    /// <b>Tipo 2</b>, sección rellena. Lleva fondo sólido gris (ACI 9), el patrón
    /// AR-CONC encima, el cuerpo del estribo relleno (ACI 152) y su contorno en
    /// negro.
    /// </summary>
    Tipo2Rellena = 2
}

/// <summary>Conversiones del estilo de sección.</summary>
public static class ModoSeccionExt
{
    /// <summary>
    /// Traduce el valor de la <b>celda AC de la hoja de Excel</b> al estilo.
    /// </summary>
    /// <remarks>
    /// Existe precisamente porque los números NO coinciden. En la macro
    /// <c>AC = 1</c> significa <b>rellena</b>, que aquí es el tipo 2. Hacer
    /// <c>(ModoSeccion)ac</c> daría el estilo contrario y todas las secciones
    /// saldrían al revés, así que la traducción va escrita a mano y en un solo
    /// sitio.
    /// </remarks>
    /// <param name="ac">Valor de la celda AC: 1 o 2.</param>
    public static ModoSeccion DesdeCeldaAC(int ac) => ac switch
    {
        1 => ModoSeccion.Tipo2Rellena,      // AC = 1 -> MODO_RELLENA
        2 => ModoSeccion.Tipo1SinRelleno,   // AC = 2 -> MODO_SOLO_HATCH
        _ => ModoSeccion.Tipo2Rellena       // igual que la macro: si falta, rellena
    };

    /// <summary>Valor que le correspondería en la celda AC de la macro.</summary>
    public static int ACequivalente(this ModoSeccion m) =>
        m == ModoSeccion.Tipo2Rellena ? 1 : 2;
}

/// <summary>
/// Una varilla: su clave de captura y su diámetro ya resuelto en centímetros.
/// </summary>
/// <remarks>
/// El diámetro llega <b>ya resuelto</b>. La tabla de diámetros y la validación
/// viven en la capa de datos, no aquí: así el motor de dibujo no puede recibir un
/// diámetro sin reconocer, que es exactamente el bug de la macro.
/// </remarks>
public readonly record struct VarCad(string Clave, double Cm)
{
    public bool Existe => Cm > 0;

    public double RadioCm => Cm / 2.0;
}

/// <summary>Un lecho de refuerzo: varillas de esquina más intermedias.</summary>
public sealed class LechoCad
{
    public int NEsquina { get; set; }

    public VarCad Esquina { get; set; }

    public int NIntermedia { get; set; }

    public VarCad Intermedia { get; set; }
}

/// <summary>
/// Datos de una sección listos para dibujar, en centímetros.
/// </summary>
public sealed class SeccionCad
{
    public string Elemento { get; set; } = string.Empty;

    /// <summary>Nombre del bloque de AutoCAD.</summary>
    public string Id { get; set; } = string.Empty;

    public double BaseCm { get; set; }

    public double AlturaCm { get; set; }

    public double RecubrimientoCm { get; set; }

    /// <summary>Longitud de la cola del gancho sísmico. 0 = sin gancho.</summary>
    public double GanchoCm { get; set; }

    public VarCad Estribo { get; set; }

    public LechoCad Superior { get; set; } = new();

    public LechoCad Inferior { get; set; } = new();

    /// <summary>Varillas intermedias laterales, por lado.</summary>
    public int NLateral { get; set; }

    public VarCad Lateral { get; set; }

    public string Fc { get; set; } = string.Empty;

    public string Escala { get; set; } = string.Empty;

    public string Separacion { get; set; } = string.Empty;

    /// <summary>Columna R: si lleva estribo diamante.</summary>
    public bool Diamante { get; set; }

    /// <summary>
    /// Columna S: varilla del estribo diamante. Si no se indica, se usa la del
    /// estribo principal, igual que la macro.
    /// </summary>
    public VarCad EstriboDiamanteVar { get; set; }

    /// <summary>Estilo de dibujo. Equivale a la celda AC de la hoja.</summary>
    public ModoSeccion Modo { get; set; } = ModoSeccion.Tipo2Rellena;
}
