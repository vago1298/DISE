namespace CadLink.Cad;

/// <summary>
/// Las dos condiciones de apoyo que traen las macros originales.
/// </summary>
/// <remarks>
/// No son dos dibujos distintos: es el <b>mismo</b> dibujo con el dado corrido. Lo
/// que cambia entre una y otra está acotado a tres cosas, y conviene tenerlas juntas
/// porque son justo las que se salían de lugar:
/// <list type="number">
///   <item>Dónde queda el dado: centrado en la zapata, o pegado a un paño.</item>
///   <item>De qué lado va el rótulo del dado y de la columna.</item>
///   <item>Hacia dónde doblan los ganchos de arranque del dado.</item>
/// </list>
/// El título, el subtítulo y la línea de escala son los mismos en las dos, salvo el
/// nombre del tipo. Ver <see cref="ZapataAisladaRotulos"/>.
/// </remarks>
public enum TipoZapata
{
    /// <summary>
    /// <c>ZAPATA AISLADA CENTRAL</c>. El dado va <b>centrado</b> en la zapata y sus
    /// rótulos salen hacia la derecha.
    /// </summary>
    Central,

    /// <summary>
    /// <c>ZAPATA AISLADA DE LINDERO</c>. El dado va pegado al <b>paño derecho</b>,
    /// que es el lindero, y por eso sus rótulos salen hacia la izquierda y ningún
    /// gancho puede sobresalir de ese lado.
    /// </summary>
    Lindero
}

/// <summary>
/// Una parrilla de la zapata: la varilla que se ve <b>de canto</b> y la que se ve
/// <b>de punta</b>.
/// </summary>
/// <remarks>
/// En el alzado una parrilla se dibuja siempre con dos armados distintos, y en la
/// macro se llaman por cómo se ven, no por su dirección:
/// <list type="bullet">
///   <item>
///     <see cref="Barra"/> es la que corre en el plano del corte: se dibuja como una
///     banda con sus dos ganchos. Celdas <c>C9</c> / <c>T9</c>.
///   </item>
///   <item>
///     <see cref="Transversal"/> es la perpendicular: se ve de punta, así que son
///     círculos repartidos a su separación. Celdas <c>C11</c> / <c>T11</c>.
///   </item>
/// </list>
/// </remarks>
public sealed class ParrillaCad
{
    /// <summary>Varilla que se ve de canto, con sus ganchos.</summary>
    public VarCad Barra { get; set; }

    /// <summary>Separación de <see cref="Barra"/>, en centímetros.</summary>
    public double SepBarraCm { get; set; }

    /// <summary>Varilla perpendicular, la que se ve de punta.</summary>
    public VarCad Transversal { get; set; }

    /// <summary>Separación de <see cref="Transversal"/>, en centímetros.</summary>
    public double SepTransversalCm { get; set; }

    public bool Existe => Barra.Existe;

    /// <summary>
    /// Las dos direcciones llevan el mismo armado, y entonces el rótulo se escribe
    /// una sola vez con <c>AMBOS SENTIDOS</c>.
    /// </summary>
    public bool AmbosSentidos =>
        ZapataAisladaRotulos.MismoArmado(Barra, SepBarraCm, Transversal, SepTransversalCm);
}

/// <summary>
/// El elemento vertical que desplanta sobre la zapata: el <b>dado</b> o la
/// <b>columna</b> de concreto.
/// </summary>
public sealed class ElementoVerticalCad
{
    /// <summary>Nombre del elemento en el título del rótulo: DADO o COLUMNA.</summary>
    public string Elemento { get; set; } = "DADO";

    /// <summary>ID que se entrecomilla en el rótulo, y nombre del bloque en planta.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Ancho de la cara que se ve en el alzado, en centímetros.</summary>
    public double AnchoCm { get; set; }

    /// <summary>Recubrimiento propio. Por omisión 5 cm, igual que la macro.</summary>
    public double RecubrimientoCm { get; set; } = 5;

    /// <summary>Varillas del paño que en el alzado queda arriba.</summary>
    public int NSuperior { get; set; }

    public VarCad Superior { get; set; }

    /// <summary>Varillas del paño que en el alzado queda abajo.</summary>
    public int NInferior { get; set; }

    public VarCad Inferior { get; set; }

    /// <summary>Varillas intermedias entre las de esquina.</summary>
    public int NIntermedias { get; set; }

    /// <summary>
    /// Diámetro de las intermedias. Si viene vacío, la macro usa el de las de
    /// esquina superiores.
    /// </summary>
    public VarCad Intermedia { get; set; }

    public VarCad Estribo { get; set; }

    /// <summary>Separaciones del estribo, tal como se capturan: <c>8-10-8</c>.</summary>
    public string Separaciones { get; set; } = string.Empty;

    /// <summary>Diámetro que se usa para las intermedias, ya resuelto.</summary>
    public VarCad IntermediaEfectiva => Intermedia.Existe ? Intermedia : Superior;
}

/// <summary>
/// Datos de una zapata aislada listos para dibujar.
/// </summary>
/// <remarks>
/// <para>
/// Es el port de lo que las dos macros leen de la hoja. Las longitudes de la zapata
/// vienen en <b>metros</b> —así están en las celdas <c>E4..E7</c> / <c>V4..V7</c>— y
/// los anchos del dado y de la columna en <b>centímetros</b>, porque la macro los
/// multiplica por <c>SCALE_ELEVATION</c>. Se conserva esa distinción a propósito:
/// cambiar de unidad aquí es la forma más fácil de que un dado de 50 cm salga de
/// 50 m.
/// </para>
/// </remarks>
public sealed class ZapataAisladaCad
{
    public TipoZapata Tipo { get; set; } = TipoZapata.Central;

    /// <summary>Nombre de la zapata: <c>ZE-1</c>, <c>ZL-1</c>. Celda G1 / X1.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Ancho de la zapata en el plano del corte, en metros. E4 / V4.</summary>
    public double AnchoM { get; set; }

    /// <summary>Largo de la zapata, para la vista en planta, en metros. E5 / V5.</summary>
    public double LargoM { get; set; }

    /// <summary>Profundidad de desplante, en metros. E6 / V6.</summary>
    public double ProfundidadM { get; set; }

    /// <summary>Espesor de la zapata, en metros. E7 / V7.</summary>
    public double EspesorM { get; set; }

    /// <summary>Recubrimiento de las parrillas. La macro lo tiene fijo en 5 cm.</summary>
    public double RecubrimientoM { get; set; } = 0.05;

    /// <summary>
    /// La columna que desplanta es de concreto, y entonces se dibuja su arranque
    /// encima del dado. Celda H4 / Y4.
    /// </summary>
    public bool ColumnaDeConcreto { get; set; }

    /// <summary>Lleva parrilla superior además de la inferior. Celda H9 / Y9.</summary>
    public bool DobleParrilla { get; set; }

    /// <summary>f'c tal como se captura. Celda H10 / Y10.</summary>
    public string Fc { get; set; } = string.Empty;

    /// <summary>Escala del rótulo. Las macros la tienen fija en 10.</summary>
    public string Escala { get; set; } = "10";

    public ParrillaCad ParrillaInferior { get; set; } = new();

    public ParrillaCad ParrillaSuperior { get; set; } = new();

    public ElementoVerticalCad Dado { get; set; } = new() { Elemento = "DADO" };

    public ElementoVerticalCad Columna { get; set; } = new() { Elemento = "COLUMNA" };

    public ModoSeccion Modo { get; set; } = ModoSeccion.Tipo1SinRelleno;

    /// <summary>
    /// Largo con el que se dibuja la planta. Si la hoja no lo trae, la macro usa el
    /// ancho: <c>If largoZapata &lt;= 0# Then largoZapata = anchoZapata</c>.
    /// </summary>
    public double LargoEfectivoM => LargoM > 0 ? LargoM : AnchoM;

    /// <summary>
    /// Altura con la que se representa el dado: la profundidad de desplante,
    /// <c>alturaDadoRep = profundidad</c>.
    /// </summary>
    public double AlturaDadoM => ProfundidadM;

    /// <summary>Nombre del tipo tal como entra en el título del alzado.</summary>
    public string TipoTexto => Tipo == TipoZapata.Lindero
        ? "ZAPATA AISLADA DE LINDERO"
        : "ZAPATA AISLADA CENTRAL";

    /// <summary>La sección se dibuja rellena.</summary>
    public bool Rellena => Modo == ModoSeccion.Tipo2Rellena;
}
