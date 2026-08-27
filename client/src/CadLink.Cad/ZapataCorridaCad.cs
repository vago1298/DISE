namespace CadLink.Cad;

/// <summary>
/// Datos de una <b>zapata corrida</b> listos para dibujar.
/// </summary>
/// <remarks>
/// <para>
/// Port de lo que leen <c>ZAPATA CORRIDA CENTRAL V2</c> y <c>ZAPATA CORRIDA LINDERO V2</c>. Las dos
/// macros leen <b>lo mismo</b> en columnas distintas —la central en C/D/E/G/H/J y la de lindero en
/// O/P/R/T—, así que aquí hay un solo juego de campos y cada uno lleva anotadas <b>las dos</b>
/// celdas: primero la de la central, después la del lindero.
/// </para>
/// <para>
/// Las medidas de la zapata vienen en <b>metros</b> —así están en la hoja— y los espesores del muro
/// en <b>centímetros</b>, porque la macro los divide entre 100. Se conserva la distinción a
/// propósito: es la misma que ya evitó que un dado de 50 cm saliera de 50 m.
/// </para>
/// </remarks>
public sealed class ZapataCorridaCad
{
    /// <summary>La zapata es de <b>lindero</b>: el muro va pegado a su paño derecho.</summary>
    public const string Lindero = "LINDERO";

    /// <summary>La zapata es <b>central</b>: el muro va centrado.</summary>
    public const string Central = "CENTRAL";

    /// <summary>Los dos tipos, para el desplegable de la hoja.</summary>
    public static string[] Tipos => new[] { Central, Lindero };

    /// <summary>Muro de <b>mampostería</b>: lleva cadena de desplante y muro de enrase.</summary>
    public const string MuroMamposteria = "MAMPOSTERIA";

    /// <summary>Muro de <b>concreto</b>: lleva su acero vertical con doblez.</summary>
    public const string MuroConcreto = "CONCRETO";

    /// <summary>Los dos tipos de muro que aceptan las macros.</summary>
    public static string[] TiposDeMuro => new[] { MuroMamposteria, MuroConcreto };

    // ---------------------------------------------------------------- la zapata ----

    /// <summary><b>CENTRAL</b> o <b>LINDERO</b>. Decide el acomodo y dónde va el muro.</summary>
    public string Tipo { get; init; } = Central;

    /// <summary>Nombre de la zapata: el ID del elemento. <c>G1</c> / <c>P1</c>.</summary>
    /// <remarks>
    /// Se lee en <c>G1</c> / <c>P1</c> —y en <c>G17</c>, <c>G33</c>… porque cada zapata ocupa
    /// <b>16 renglones</b>—, y una celda vacía es la señal de que ya no hay más zapatas.
    /// </para>
    /// <para>
    /// Es también el <b>nombre del bloque</b>: la central lo usa tal cual, y le pega
    /// <c>-ZAP</c> detrás si choca con el nombre de la contratrabe o de la cadena; la de lindero le
    /// pone delante <c>ZAPATA_LINDERO_</c>.
    /// </remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>Ancho de la zapata, en metros. <c>E4</c> / <c>O4</c>.</summary>
    public double AnchoM { get; init; }

    /// <summary>Profundidad de desplante, en metros. <c>E5</c> / <c>O5</c>.</summary>
    public double ProfundidadM { get; init; }

    /// <summary>Espesor de la zapata, en metros. <c>E6</c> / <c>O6</c>.</summary>
    public double EspesorM { get; init; }

    /// <summary>Recubrimiento de las parrillas. Las macros lo fijan en 5 cm.</summary>
    public double RecM { get; init; } = 0.05;

    /// <summary>f'c tal como se capturó, para el rótulo. <c>J8</c> / <c>T8</c>.</summary>
    public string Fc { get; init; } = string.Empty;

    // ------------------------------------------------------------- las parrillas ----

    /// <summary>Varilla que corre en el plano del corte, parrilla inferior. <c>C8</c> / <c>C8</c>.</summary>
    public string VarInf { get; init; } = string.Empty;

    /// <summary>Su separación, en cm de captura. <c>E8</c> / <c>O8</c>.</summary>
    public string SepInf { get; init; } = string.Empty;

    /// <summary>Varilla perpendicular de la parrilla inferior: la que se ve de punta. <c>C10</c>.</summary>
    public string VarInfTrans { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E10</c> / <c>O10</c>.</summary>
    public string SepInfTrans { get; init; } = string.Empty;

    /// <summary>Lleva parrilla superior. <c>H8</c> / <c>R8</c>.</summary>
    public bool DobleParrilla { get; init; }

    /// <summary>Varilla de canto de la parrilla superior. <c>C12</c>.</summary>
    public string VarSup { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E12</c> / <c>O12</c>.</summary>
    public string SepSup { get; init; } = string.Empty;

    /// <summary>Varilla perpendicular de la parrilla superior. <c>C14</c>.</summary>
    public string VarSupTrans { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E14</c> / <c>O14</c>.</summary>
    public string SepSupTrans { get; init; } = string.Empty;

    // -------------------------------------------------- la contratrabe y la cadena ----

    /// <summary>
    /// ID del bloque de la <b>contratrabe</b>. <c>H6</c> / <c>R6</c>. Vacío o <c>0</c> = no lleva.
    /// </summary>
    /// <remarks>
    /// No es geometría: es el nombre con el que el dibujante <b>busca el bloque</b> en el dibujo, en
    /// lugar de volver a dibujar la contratrabe. Su caja manda en tres cosas —el hatch de concreto
    /// de la zapata, el hueco de su línea superior y hasta dónde llega el muro de enrase—, así que
    /// se inserta <b>antes</b> de dibujar la zapata.
    /// </remarks>
    public string IdContratrabe { get; init; } = string.Empty;

    /// <summary>ID del bloque de la <b>cadena de desplante</b>. <c>H5</c> / <c>R5</c>.</summary>
    /// <remarks>Solo en mampostería: es la que remata el muro de enrase por arriba.</remarks>
    public string IdCadena { get; init; } = string.Empty;

    // -------------------------------------------------------------------- el muro ----

    /// <summary><b>MAMPOSTERIA</b> o <b>CONCRETO</b>. <c>H4</c> / <c>R4</c>.</summary>
    public string TipoMuro { get; init; } = MuroMamposteria;

    /// <summary>
    /// Espesor del muro, en centímetros. Con concreto <c>H9</c> / <c>R9</c>; con mampostería
    /// <c>G7</c> / <c>P7</c>.
    /// </summary>
    /// <remarks>
    /// No es la misma celda en los dos casos, y no es un despiste de las macros: el espesor del
    /// muro de mampostería lo pone el block —10, 12, 15— y vive en su propio bloque de celdas, en
    /// otra columna. Si no se captura, las dos macros usan <b>15 cm</b>.
    /// </remarks>
    public double EspesorMuroCm { get; init; } = 15;

    /// <summary>El muro de concreto lleva <b>doble parrilla</b>. <c>H10</c> / <c>R10</c>.</summary>
    /// <remarks>La mampostería no lo pregunta: las macros la fijan en <c>NO</c>.</remarks>
    public bool MuroDobleParrilla { get; init; }

    /// <summary>
    /// Varilla del muro. Con concreto <c>H11</c> / <c>R11</c>; con mampostería <c>H10</c> /
    /// <c>R10</c>.
    /// </summary>
    /// <remarks>
    /// <b>Con mampostería las tres celdas del acero suben un renglón</b>, porque no hay celda de
    /// doble parrilla. Es la única trampa de la lectura de la hoja, y leerla mal saca la varilla del
    /// muro de la casilla de al lado.
    /// </remarks>
    public string VarMuro { get; init; } = string.Empty;

    /// <summary>
    /// Varilla <b>vertical</b> del muro: la que arranca de la zapata con su pata.
    /// </summary>
    /// <remarks>
    /// En la hoja de las macros el muro lleva <b>una sola</b> casilla de varilla y las dos
    /// direcciones la comparten; se pidió poder elegir las dos. Vacía = se usa
    /// <see cref="VarMuro"/>, que es como se portaba antes, así que una hoja vieja dibuja igual.
    /// </remarks>
    public string VarMuroVert { get; init; } = string.Empty;

    /// <summary>La clave de la varilla vertical de verdad: la suya, o la horizontal si no hay.</summary>
    public string VarMuroVertical =>
        string.IsNullOrWhiteSpace(VarMuroVert) ? VarMuro : VarMuroVert;

    /// <summary>
    /// Separación <b>horizontal</b>. Con concreto <c>H12</c> / <c>R12</c>; con mampostería
    /// <c>H11</c> / <c>R11</c>.
    /// </summary>
    /// <remarks>
    /// En el corte transversal <b>no se ve</b>: es la separación de las varillas verticales medida
    /// a lo largo del muro, y en la sección solo entra en el rótulo. Las macros la usan además, sin
    /// decirlo, para el pequeño desplazamiento de la varilla vertical respecto del eje del acero.
    /// </remarks>
    public string SepMuroHoriz { get; init; } = string.Empty;

    /// <summary>
    /// Separación <b>vertical</b>. Con concreto <c>H13</c> / <c>R13</c>; con mampostería
    /// <c>H12</c> / <c>R12</c>.
    /// </summary>
    /// <remarks>
    /// Esta <b>sí</b> se ve: es la que reparte hacia arriba las varillas que en el corte salen de
    /// punta, o sea los círculos del muro. Ver
    /// <see cref="TrazoZapataCorrida.CirculosDelMuro"/>.
    /// </remarks>
    public string SepMuroVert { get; init; } = string.Empty;

    // ----------------------------------------------------------------- el dibujo ----

    /// <summary>Estilo de dibujo: la celda <b>B3</b>. 1 = rellena, 2 = tal cual.</summary>
    /// <remarks>
    /// Las dos macros no la leen igual: la central lee <b>B3 una sola vez</b> y vale para todo el
    /// dibujo, y la de lindero busca la de cada zapata —B3, B19, B35…— y, si está vacía,
    /// <b>hereda la B3</b>. La segunda es la buena: con poner 1 en B3 se rellenan todas, y una
    /// sección se puede sacar distinta escribiendo su propio valor. Por eso el port la trae por
    /// zapata en las dos.
    /// </remarks>
    public ModoSeccion Modo { get; init; } = ModoSeccion.Tipo1SinRelleno;

    /// <summary>La zapata es de lindero.</summary>
    public bool EsLindero =>
        Lindero.Equals((Tipo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>El muro es de concreto.</summary>
    public bool MuroEsConcreto =>
        (TipoMuro ?? string.Empty).Trim()
        .Contains(MuroConcreto, StringComparison.OrdinalIgnoreCase);

    /// <summary>El muro es de mampostería.</summary>
    public bool MuroEsMamposteria =>
        (TipoMuro ?? string.Empty).Trim()
        .Contains(MuroMamposteria, StringComparison.OrdinalIgnoreCase);

    /// <summary>Espesor del muro en metros, con el respaldo de 15 cm de las macros.</summary>
    public double EspesorMuroM => EspesorMuroCm > 0 ? EspesorMuroCm / 100.0 : 0.15;

    /// <summary>Lleva contratrabe: el ID no está vacío ni es <c>0</c>.</summary>
    public bool HayContratrabe => HayBloque(IdContratrabe);

    /// <summary>Lleva cadena de desplante.</summary>
    public bool HayCadena => HayBloque(IdCadena);

    /// <summary>La sección se dibuja rellena.</summary>
    public bool Rellena => Modo == ModoSeccion.Tipo2Rellena;

    /// <summary>Nombre del tipo tal como entra en el título del rótulo.</summary>
    /// <remarks>
    /// Son los de las macros, literales: <c>ZAPATA CORRIDA CENTRAL "Z-1"</c> y
    /// <c>ZAPATA DE LINDERO "Z-1"</c>. Ojo con el de lindero: <b>no</b> dice «corrida», y así está
    /// en su macro.
    /// </remarks>
    public string TipoTexto => EsLindero ? "ZAPATA DE LINDERO" : "ZAPATA CORRIDA CENTRAL";

    /// <summary>
    /// El ID de un bloque está capturado de verdad.
    /// </summary>
    /// <remarks>
    /// Las dos macros preguntan lo mismo: <c>If ctLabel &lt;&gt; "" And ctLabel &lt;&gt; "0"</c>. El
    /// <c>"0"</c> aparece cuando la celda está vacía y Excel la devuelve como cero, y tomarlo por un
    /// ID hace que el dibujante busque un bloque llamado «0».
    /// </remarks>
    public static bool HayBloque(string? id)
    {
        var t = (id ?? string.Empty).Trim();

        return t.Length > 0 && t != "0";
    }
}
