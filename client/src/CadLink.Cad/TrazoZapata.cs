namespace CadLink.Cad;

/// <summary>
/// Los datos de una <b>zapata aislada</b>, en las unidades en que se capturan.
/// </summary>
/// <remarks>
/// Las medidas de la zapata van en <b>metros</b> —es como las lee la macro de la hoja, con
/// <c>ValorCeldaM</c>— y las del dado y la columna en <b>centímetros</b>, porque la macro las
/// multiplica por <c>SCALEELEVATION = 0.01</c> al dibujarlas. Se respetan las dos unidades a
/// propósito: cambiarlas aquí obligaría a revisar cada fórmula portada para ver si el factor
/// sigue estando donde debe.
/// </remarks>
public sealed class ZapataCad
{
    /// <summary>Zapata aislada <b>central</b>: el dado va centrado.</summary>
    public const string Central = "CENTRAL";

    /// <summary>Zapata aislada <b>de lindero</b>: el dado va pegado al paño derecho.</summary>
    public const string Lindero = "LINDERO";

    /// <summary>Los dos tipos, en el orden del desplegable.</summary>
    public static readonly string[] Tipos = { Central, Lindero };

    /// <summary>Nombre de la sección. Es la celda <c>G1</c> / <c>X1</c> de la macro.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary><see cref="Central"/> o <see cref="Lindero"/>.</summary>
    public string Tipo { get; init; } = Central;

    /// <summary>Ancho de la zapata, en metros. <c>E4</c> / <c>V4</c>.</summary>
    public double AnchoM { get; init; }

    /// <summary>Largo de la zapata en planta, en metros. <c>E5</c> / <c>V5</c>.</summary>
    public double LargoM { get; init; }

    /// <summary>Profundidad de desplante, en metros. <c>E6</c> / <c>V6</c>.</summary>
    public double ProfundidadM { get; init; }

    /// <summary>Espesor de la zapata, en metros. <c>E7</c> / <c>V7</c>.</summary>
    public double EspesorM { get; init; }

    /// <summary>Recubrimiento de la zapata, en metros. La macro lo fija en 0.05.</summary>
    public double RecM { get; init; } = 0.05;

    /// <summary>Ancho del dado, en <b>centímetros</b>. <c>G8</c> / <c>X8</c>.</summary>
    public double AnchoDadoCm { get; init; }

    /// <summary>Ancho de la columna, en <b>centímetros</b>. <c>G6</c> / <c>X6</c>.</summary>
    public double AnchoColumnaCm { get; init; }

    /// <summary>Recubrimiento del dado, en centímetros. <c>N8</c> / <c>AE8</c>.</summary>
    public double RecDadoCm { get; init; } = 5;

    /// <summary>Recubrimiento de la columna, en centímetros. <c>N6</c> / <c>AE6</c>.</summary>
    public double RecColumnaCm { get; init; } = 5;

    /// <summary>Si la columna que desplanta es de <b>concreto</b>. <c>H4</c> / <c>Y4</c>.</summary>
    /// <remarks>
    /// Manda dos cosas: si se dibuja la columna encima del dado, y hacia dónde doblan los
    /// ganchos de arranque del dado. Con columna de acero los ganchos van hacia <b>afuera</b>
    /// —no hay columna de concreto que los reciba— y con columna de concreto hacia adentro.
    /// </remarks>
    public bool ColumnaDeConcreto { get; init; }

    /// <summary>Si lleva <b>doble parrilla</b>. <c>H9</c> / <c>Y9</c>.</summary>
    public bool DobleParrilla { get; init; }

    /// <summary>Varilla de la parrilla inferior, la que corre a lo largo. <c>C9</c>.</summary>
    public string VarInf { get; init; } = "#4";

    /// <summary>Su separación, en cm de texto. <c>E9</c>.</summary>
    public string SepInf { get; init; } = "15";

    /// <summary>Varilla transversal de la parrilla inferior. <c>C11</c>.</summary>
    public string VarInfTrans { get; init; } = "#4";

    /// <summary>Su separación. <c>E11</c>.</summary>
    public string SepInfTrans { get; init; } = "15";

    /// <summary>Varilla de la parrilla superior. <c>C13</c>.</summary>
    public string VarSup { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E13</c>.</summary>
    public string SepSup { get; init; } = string.Empty;

    /// <summary>Varilla transversal de la parrilla superior. <c>C15</c>.</summary>
    public string VarSupTrans { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E15</c>.</summary>
    public string SepSupTrans { get; init; } = string.Empty;

    /// <summary>Estribo del dado. <c>O7</c> / <c>AF7</c>.</summary>
    public string EstriboDado { get; init; } = "#3";

    /// <summary>Separación de los estribos del dado, del tipo <c>10-15-20</c>. <c>O8</c>.</summary>
    public string SepEstriboDado { get; init; } = "15";

    /// <summary>Varilla de arranque del dado, paño superior en el sistema local. <c>J7</c>.</summary>
    public string VarDadoSup { get; init; } = "#4";

    /// <summary>La del otro paño. <c>J8</c>.</summary>
    public string VarDadoInf { get; init; } = "#4";

    /// <summary>Cuántas intermedias lleva el dado. <c>K7</c>.</summary>
    public int NIntDado { get; init; }

    /// <summary>Diámetro de las intermedias del dado. <c>L7</c>.</summary>
    public string VarIntDado { get; init; } = string.Empty;

    /// <summary>
    /// Cuántas varillas <b>representa</b> la del paño superior del dado, para el rótulo.
    /// <c>Z7</c> / <c>I7</c>.
    /// </summary>
    /// <remarks>
    /// En el alzado se dibuja UNA varilla por paño, pero el rótulo tiene que decir cuántas hay de
    /// verdad: son los conteos <c>Z7</c> y <c>Z8</c> que la macro lee de la hoja. Aquí salen de la
    /// sección del dado —lecho superior completo, lecho inferior completo y laterales—, así que
    /// <c>NVarDadoSup + NVarDadoInf + NVarIntDadoTotal</c> es el total de varillas de la sección.
    /// En cero, el rótulo escribe los diámetros sin conteo, como antes.
    /// </remarks>
    public int NVarDadoSup { get; init; }

    /// <summary>Las del otro paño. <c>Z8</c> / <c>I8</c>.</summary>
    public int NVarDadoInf { get; init; }

    /// <summary>
    /// Cuántas intermedias tiene el dado <b>en total</b> (las dos caras), para el rótulo.
    /// </summary>
    /// <remarks>
    /// No es <see cref="NIntDado"/>: ese es cuántas se <b>dibujan</b> por cara en el alzado. En
    /// una sección cuadrada son los laterales de los dos costados y en una circular todas las que
    /// no son las dos de los paños.
    /// </remarks>
    public int NVarIntDadoTotal { get; init; }

    /// <summary>Largo del gancho de arranque, en metros. La macro lo fija en 0.12.</summary>
    public double GanchoM { get; init; } = 0.12;

    /// <summary>
    /// ID del <b>dado</b> que va encima, que es el nombre de su bloque. <c>N7</c>.
    /// </summary>
    /// <remarks>
    /// No es un dato de geometría: es el nombre con el que el dibujante <b>busca el bloque</b> del
    /// dado en el dibujo para insertarlo en la planta, en lugar de volver a dibujarlo. Va aquí, y
    /// no como parámetro aparte del dibujante, porque cada zapata lleva el suyo.
    /// </remarks>
    public string IdDado { get; init; } = string.Empty;

    /// <summary>
    /// El dado que va encima es <b>circular</b>.
    /// </summary>
    /// <remarks>
    /// Cambia la planta: el hueco del dado deja de ser un cuadrado y las varillas de las dos
    /// mallas llegan <b>hasta el contorno circular</b>, cada una a su corte. Con el hueco
    /// cuadrado, entre la circunferencia y el cuadrado quedaban cuatro esquinas de varilla que en
    /// la obra no se cortan, y el plano decía que sí.
    /// </remarks>
    public bool DadoCircular { get; init; }

    /// <summary>ID de la columna que desplanta, para su rótulo. <c>H5</c> / <c>Y5</c>.</summary>
    public string IdColumna { get; init; } = string.Empty;

    /// <summary>Varilla de una cara de la columna. <c>J5</c> / <c>AA5</c>.</summary>
    /// <remarks>
    /// La macro lee el armado de la columna de sus propias celdas. Aquí sale de la <b>sección</b>
    /// de la columna elegida en la hoja de concreto, que es el mismo dato en el sitio donde ya
    /// estaba capturado: así el arranque que se dibuja en la zapata es el de la columna de verdad.
    /// </remarks>
    public string VarColSup { get; init; } = string.Empty;

    /// <summary>La de la otra cara. <c>J6</c> / <c>AA6</c>.</summary>
    public string VarColInf { get; init; } = string.Empty;

    /// <summary>Cuántas intermedias lleva la columna. <c>K5</c> / <c>AB5</c>.</summary>
    public int NIntColumna { get; init; }

    /// <summary>Diámetro de las intermedias de la columna. <c>L5</c> / <c>AC5</c>.</summary>
    public string VarIntColumna { get; init; } = string.Empty;

    /// <summary>Cuántas varillas representa la del paño superior de la columna. <c>Z5</c>.</summary>
    public int NVarColSup { get; init; }

    /// <summary>Las del otro paño. <c>Z6</c>.</summary>
    public int NVarColInf { get; init; }

    /// <summary>Las intermedias de la columna en total, para el rótulo.</summary>
    public int NVarIntColumnaTotal { get; init; }

    /// <summary>Estribo de la columna. <c>O4</c> / <c>AF4</c>.</summary>
    public string EstriboColumna { get; init; } = string.Empty;

    /// <summary>Su separación. <c>O5</c> / <c>AF5</c>.</summary>
    public string SepEstriboColumna { get; init; } = string.Empty;

    /// <summary>Resistencia del concreto, tal como se capturó, para el rótulo.</summary>
    /// <remarks>
    /// Se lleva como <b>texto</b> a propósito: en la celda se escriben cosas como «250» y
    /// «f'c=250 kg/cm²», y lo que va al rótulo es lo que el usuario escribió. Convertirlo a número
    /// solo serviría para tener que volver a inventar cómo se escribe.
    /// </remarks>
    public string Fc { get; init; } = string.Empty;
}

/// <summary>
/// La <b>geometría de una zapata aislada</b>, portada de las macros. Sin AutoCAD y sin WPF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué está aquí.</b> Es el mismo motivo de <see cref="TrazoAcero"/> y de
/// <see cref="TrazoDiamante"/>: este cálculo lo necesitan dos programas —el dibujante de
/// AutoCAD y la vista previa de la pestaña— y no puede haber dos copias. Una vista previa que
/// coloca la planta a otra distancia, o que reparte los estribos de otra manera, enseña un
/// dibujo que no es el que se va a generar.
/// </para>
/// <para>
/// <b>Las distancias son las de las macros, no unas parecidas.</b> Están todas como constantes
/// con el nombre que tenían en el VBA, para poder cotejarlas una por una:
/// </para>
/// <list type="bullet">
/// <item><b>Central</b>: las secciones crecen hacia la <b>derecha</b>, separadas
/// <c>SEPARACION_SECCIONES = 1</c> m más el ancho de la zapata; el dado va <b>centrado</b>; y
/// la planta se cuelga de la vista de corte, con su borde superior a
/// <c>PLANTA_OFFSET_Y = −3</c> del renglón más bajo del rótulo.</item>
/// <item><b>Lindero</b>: la primera zapata arranca en <c>(−3, −8)</c> y las siguientes crecen
/// hacia la <b>izquierda</b>, separadas <c>0.8</c> m; el dado va pegado al <b>paño derecho</b>
/// —ese es el lindero— y la planta arranca en <c>−15</c>, o más abajo si la zapata es tan larga
/// que se encimaría, dejando <c>PLANTA_SEPARACION_MIN = 1.2</c> m de holgura.</item>
/// </list>
/// </remarks>
public static class TrazoZapata
{
    /// <summary>De centímetros de captura a metros de dibujo. El <c>SCALEELEVATION</c>.</summary>
    public const double EscalaElevacion = 0.01;

    /// <summary>Espesor de la plantilla de concreto simple, en metros.</summary>
    public const double PlantillaEspesor = 0.05;

    /// <summary>Separación entre secciones de la <b>central</b>, en metros.</summary>
    public const double SeparacionCentral = 1.0;

    /// <summary>Separación entre secciones del <b>lindero</b>, en metros.</summary>
    /// <remarks>
    /// En la macro está escrita en negativo —<c>SEPARACION_SECCIONES = −0.8</c>— porque las
    /// secciones se acomodan hacia la izquierda, y la macro usa su valor absoluto. Aquí va en
    /// positivo y el sentido lo pone <see cref="XBase"/>, que es donde se decide.
    /// </remarks>
    public const double SeparacionLindero = 0.8;

    /// <summary>Origen de la primera zapata de lindero: <c>ELEVACION_X_BASE</c>.</summary>
    public const double LinderoXBase = -3.0;

    /// <summary>Y de desplante. <c>ELEVACION_Y_BASE</c> en lindero, <c>yBase</c> en central.</summary>
    public const double YBaseElevacion = -8.0;

    /// <summary>Cuánto baja la planta respecto de la vista de corte, en la central.</summary>
    public const double PlantaOffsetY = -3.0;

    /// <summary>
    /// Separación entre una zapata y la siguiente, en metros, medida <b>a la izquierda</b>.
    /// </summary>
    /// <remarks>
    /// Cada zapata se acomoda con su paño derecho a un metro del paño <b>izquierdo</b> de la
    /// anterior, y eso vale igual para el corte y para la planta —las dos usan esta X—, así que
    /// las dos vistas de una zapata quedan siempre en la misma vertical. Son <b>80 cm</b>, la
    /// <c>SEPARACION_SECCIONES</c> de la macro del lindero, y ese hueco es donde caben las cotas
    /// verticales y los rótulos de la zapata de la izquierda, que se cuelgan de su paño derecho.
    /// </remarks>
    public const double SeparacionIzquierda = 0.8;

    /// <summary>Y de arranque de la planta en el lindero.</summary>
    public const double PlantaYBaseLindero = -15.0;

    /// <summary>Holgura mínima entre el rótulo de la elevación y la planta, en lindero.</summary>
    public const double PlantaSeparacionMin = 1.2;

    /// <summary>Renglón más bajo de la elevación: <c>ROTULO_ESCALA_OFFSET</c>.</summary>
    public const double RotuloEscalaOffset = 0.49;

    /// <summary>Offset de la cota del dado en planta.</summary>
    public const double PlantaCotaOffsetDado = 0.1;

    /// <summary>Largo del gancho de las parrillas, en metros. La macro pasa 0.03.</summary>
    public const double GanchoParrilla = 0.03;

    /// <summary>Factor del gancho de arranque del dado: <c>FACTOR_GANCHO_ABAJO</c>.</summary>
    public const double FactorGanchoAbajo = 15.0;

    /// <summary>Separación mínima de estribos, en metros.</summary>
    public const double SepEstriboMinima = 0.05;

    /// <summary>Retiro del primer y último estribo: <c>STIRRUP_EDGE_OFFSET</c>.</summary>
    public const double EstriboRetiroBorde = 0.05;

    /// <summary>Lo que sobresale la cápsula del estribo: <c>CAPSULE_PROTRUSION</c>.</summary>
    public const double EstriboSobresale = 0.0086;

    /// <summary>Cierre de la última varilla de la malla: <c>PLANTA_FRACCION_CIERRE</c>.</summary>
    public const double PlantaFraccionCierre = 0.3;

    // ======================================================================
    // El acomodo
    // ======================================================================

    /// <summary>
    /// Donde <b>empieza</b> la fila: el paño <b>derecho</b> de la primera zapata, en
    /// <c>x = −0.8</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LO QUE SE PIDIÓ: <i>«empezar en x = −0.8»</i>, <i>«no lo dibujes a partir del centro»</i>.
    /// Antes la primera zapata se colocaba con su paño izquierdo <b>en el origen</b> y de ahí
    /// crecía hacia la izquierda, así que el dibujo arrancaba encima del <c>0,0</c> y se metía en
    /// la zona donde viven las secciones y los alzados.
    /// </para>
    /// <para>
    /// Son los mismos <see cref="SeparacionIzquierda"/> = 0.8 que separan una zapata de la
    /// siguiente: el origen se trata como si fuera una zapata más, así que el hueco antes del
    /// dibujo es igual al que hay entre dos zapatas. Con esto la fila entera queda en
    /// <c>x ≤ −0.8</c> y nada toca el origen.
    /// </para>
    /// <para>
    /// Si algún día se quiere que sea el paño <b>izquierdo</b> el que arranque en −0.8, y no el
    /// derecho, se le quita el <c>− Ancho(anchos, 0)</c> a <see cref="XBase"/>: es el único
    /// sitio donde se decide.
    /// </para>
    /// </remarks>
    public const double XArranque = -SeparacionIzquierda;

    /// <summary>
    /// El <b>paño izquierdo</b> de la zapata número <paramref name="indice"/>, en metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La fila <b>empieza en <see cref="XArranque"/> = −0.8</b> y crece hacia la <b>izquierda</b>:
    /// la primera zapata queda con su paño derecho ahí, y cada siguiente a 0.8 del paño izquierdo
    /// de la anterior.
    /// </para>
    /// <para>
    /// Dos detalles que se ven poco y se notan mucho:
    /// </para>
    /// <list type="bullet">
    /// <item>Lo que se coloca es el <b>paño izquierdo</b>, así que se resta el ancho de la zapata
    /// <b>nueva</b>, no el de la anterior. Restar el que no toca es lo que dejaba la zapata ancha
    /// montada sobre la angosta.</item>
    /// <item>El tipo ya no cambia el acomodo. Las dos familias crecen hacia la izquierda.
    /// Antes las centrales crecían a la derecha desde cero y los linderos a la izquierda desde
    /// −3, como en cada macro, y al mezclar los dos tipos en una misma hoja se encimaban.</item>
    /// </list>
    /// </remarks>
    /// <param name="anchos">Los anchos, en metros, en el orden de la tabla.</param>
    /// <param name="indice">Cuál de ellas.</param>
    public static double XBase(string tipo, IReadOnlyList<double> anchos, int indice)
    {
        // La PRIMERA con su paño DERECHO en -0.8 -por eso se le resta su propio ancho-, y cada
        // siguiente a 0.8 del paño izquierdo de la anterior.
        var x = XArranque - Ancho(anchos, 0);

        for (var i = 1; i <= indice; i++)
        {
            x -= SeparacionIzquierda + Ancho(anchos, i);
        }

        _ = tipo;

        return x;
    }

    /// <summary>
    /// El punto más a la <b>derecha</b> que ocupa la fila de zapatas: <see cref="XArranque"/>.
    /// </summary>
    /// <remarks>
    /// Existe para poder comprobar de un tiro que ninguna zapata pasa de aquí, que es la forma
    /// corta de decir «no arranca en el centro».
    /// </remarks>
    public static double XDerechaDeLaFila => XArranque;

    /// <summary>Si el tipo es el de lindero.</summary>
    public static bool EsLindero(string? tipo) =>
        (tipo ?? string.Empty).Trim().Equals(ZapataCad.Lindero, StringComparison.OrdinalIgnoreCase);

    private static double Ancho(IReadOnlyList<double> anchos, int i)
    {
        // La macro usa 1 m cuando el ancho no es válido, para que una fila incompleta no
        // amontone todas las demás en el mismo sitio.
        var a = i >= 0 && i < anchos.Count ? anchos[i] : 0.0;

        return a > 0 ? a : 1.0;
    }

    /// <summary>
    /// Las alturas y los paños de una zapata, ya colocada.
    /// </summary>
    /// <param name="XBase">Paño izquierdo.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="YZapBot">Desplante: cara inferior de la zapata.</param>
    /// <param name="YZapTop">Lomo de la zapata.</param>
    /// <param name="YTerreno">Nivel del terreno.</param>
    /// <param name="YDadoTop">Donde acaba el dado y arranca la columna.</param>
    /// <param name="XDadoIzq">Paño izquierdo del dado.</param>
    /// <param name="XDadoDer">Paño derecho del dado.</param>
    /// <param name="XColIzq">Paño izquierdo de la columna.</param>
    /// <param name="XColDer">Paño derecho de la columna.</param>
    /// <param name="YPlantillaBot">Fondo de la plantilla de concreto simple.</param>
    /// <param name="YPlanta">Borde inferior de la vista en planta.</param>
    public readonly record struct Acomodo(
        double XBase,
        double XDer,
        double YZapBot,
        double YZapTop,
        double YTerreno,
        double YDadoTop,
        double XDadoIzq,
        double XDadoDer,
        double XColIzq,
        double XColDer,
        double YPlantillaBot,
        double YPlanta);

    /// <summary>
    /// Coloca una zapata: sus alturas, sus paños y de dónde cuelga la planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El dado.</b> En la central va centrado en el ancho de la zapata; en el lindero, su
    /// paño derecho <b>es</b> el paño derecho de la zapata, que es lo que significa lindero: por
    /// ese lado no hay dónde salirse. La columna se coloca igual que el dado.
    /// </para>
    /// <para>
    /// <b>La planta.</b> En la central se cuelga de la vista de corte: su borde superior queda a
    /// tres metros por debajo del renglón más bajo del rótulo —el de «Rec. / Escala»—, así que
    /// la planta baja sola cuando el rótulo baja. En el lindero arranca en −15, y si la zapata
    /// es tan larga que se encimaría con el rótulo, se baja todavía más para dejar 1.2 m de
    /// holgura. Las dos reglas son de las macros y no son intercambiables.
    /// </para>
    /// </remarks>
    public static Acomodo Colocar(ZapataCad z, double xBase)
    {
        var yZapBot = YBaseElevacion;
        var yZapTop = yZapBot + z.EspesorM;
        var yTerreno = yZapBot + z.ProfundidadM;

        // El dado llega hasta la profundidad de desplante: alturaDadoRep = profundidad.
        var yDadoTop = yZapBot + z.ProfundidadM;

        var xDer = xBase + z.AnchoM;

        var wDado = z.AnchoDadoCm * EscalaElevacion;
        var wCol = z.AnchoColumnaCm * EscalaElevacion;

        // Los cuatro paños, declarados uno por uno: una declaracion multiple aqui es mas
        // corta, pero el analizador de tools/validar.py no la entiende y reporta los cuatro
        // como no declarados. Vale mas que la comprobacion pueda leer el codigo.
        var xDadoIzq = 0.0;
        var xDadoDer = 0.0;
        var xColIzq = 0.0;
        var xColDer = 0.0;

        if (EsLindero(z.Tipo))
        {
            xDadoDer = xDer;
            xDadoIzq = xDadoDer - wDado;
            xColDer = xDer;
            xColIzq = xColDer - wCol;

            // La macro los recorta al paño izquierdo: un dado más ancho que la zapata no
            // puede salirse por el otro lado.
            if (xDadoIzq < xBase)
            {
                xDadoIzq = xBase;
            }

            if (xColIzq < xBase)
            {
                xColIzq = xBase;
            }
        }
        else
        {
            var xCentro = xBase + (z.AnchoM / 2);

            xDadoIzq = xCentro - (wDado / 2);
            xDadoDer = xCentro + (wDado / 2);
            xColIzq = xCentro - (wCol / 2);
            xColDer = xCentro + (wCol / 2);
        }

        // LA PLANTA ARRANCA EN −15, SIEMPRE. No depende del tipo ni de dónde acabe el rótulo:
        // el punto de inserción del juego es (x, −8) para el corte y (x, −15) para la planta, y
        // el corte y la planta de una zapata comparten la X. Colgar la planta del renglón más
        // bajo del rótulo —lo que hacía la macro central— movía la planta cada vez que el rótulo
        // cambiaba de alto, y con ella se movían sus cotas: es lo que se veía descuadrado.
        var yPlanta = YPlantaLindero(yZapBot, z.LargoM);

        return new Acomodo(
            xBase, xDer, yZapBot, yZapTop, yTerreno, yDadoTop,
            xDadoIzq, xDadoDer, xColIzq, xColDer,
            yZapBot - PlantillaEspesor, yPlanta);
    }

    // ======================================================================
    // LA ANOTACIÓN: TODA CUELGA DE LA ESQUINA INFERIOR DERECHA
    // ======================================================================
    //
    // LO QUE SE PIDIO, TEXTUAL: «necesito que se alineen con la esquina inferior derecha para que
    // siempre se muevan con ese», «siempre lo pones mas abajo y a la izquierda de las cotas».
    //
    // Asi que hay UN SOLO punto de anclaje para todo lo que se escribe alrededor del dibujo -las
    // cotas verticales, la cadena de anchos, el total, las patas de los ganchos y los tres
    // renglones del rotulo-: la ESQUINA INFERIOR DERECHA de la zapata, (xDer, yZapBot).
    //
    // Antes cada cosa colgaba de un punto distinto: las verticales del paño izquierdo, la cadena
    // del desplante y el rotulo del fondo de la plantilla, y encima centrado en el eje. Con tres
    // anclas distintas, cambiar el ancho o el espesor de una zapata movia cada anotacion en una
    // direccion diferente, y de ahi que el rotulo apareciera «mas abajo y a la izquierda» de sus
    // cotas. Con un solo ancla, todo se mueve junto y no hay nada que volver a acomodar.
    //
    // Los numeros de abajo son distancias A ESA ESQUINA, en el orden en el que salen del dibujo:
    // hacia la derecha las verticales, y hacia abajo la cadena, el total, las patas y el rotulo.

    /// <summary>Cotas verticales, primera línea: a la <b>derecha</b> del paño derecho.</summary>
    public const double AnotacionCotaVert1 = 0.08;

    /// <summary>Cotas verticales, la del total. Los mismos 0.08 de salto.</summary>
    public const double AnotacionCotaVert2 = 0.16;

    /// <summary>Cadena de anchos, por debajo del desplante.</summary>
    public const double AnotacionCadena = 0.14;

    /// <summary>Cota del ancho total.</summary>
    public const double AnotacionTotal = 0.22;

    /// <summary>Pata del gancho del paño izquierdo.</summary>
    public const double AnotacionGanchoIzq = 0.30;

    /// <summary>Pata del gancho del paño derecho.</summary>
    public const double AnotacionGanchoDer = 0.38;

    /// <summary>
    /// Primer renglón del rótulo: 14 cm por debajo de la última cota.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>0.38 de la última cota + 0.14 de aire = 0.52.</b> El rótulo tiene que ir por debajo de
    /// las cotas —no hay otro sitio: arriba está el dibujo—, pero cuelga de la <b>misma</b> esquina
    /// que ellas y va alineado a su <b>paño derecho</b>, así que se mueve con ellas y no se
    /// desplaza a la izquierda cuando cambia el ancho.
    /// </para>
    /// <para>
    /// Los 0.8 por debajo del fondo de la plantilla que se usaban antes ya no están: dejaban el
    /// rótulo 33 cm más abajo de lo necesario, y medidos desde otro punto que las cotas.
    /// </para>
    /// </remarks>
    public const double AnotacionRotulo = 0.52;

    /// <summary>Del título al segundo renglón. Es el salto de la macro: 0.41 − 0.32.</summary>
    public const double RotuloSalto1 = 0.09;

    /// <summary>Del título al tercero. El de la macro: 0.49 − 0.32.</summary>
    public const double RotuloSalto2 = 0.17;

    /// <summary>
    /// La Y de un renglón del rótulo del <b>corte</b>: 0 = título, 1 = subtítulo, 2 = escala.
    /// </summary>
    /// <param name="yZapBot">El desplante: la Y de la esquina inferior derecha.</param>
    public static double YRotulo(double yZapBot, int renglon)
    {
        var y = yZapBot - AnotacionRotulo;

        return renglon switch
        {
            0 => y,
            1 => y - RotuloSalto1,
            _ => y - RotuloSalto2
        };
    }

    /// <summary>Los dos renglones del rótulo de la <b>planta</b>, con los saltos de la macro.</summary>
    /// <remarks>
    /// La planta cuelga de <b>su</b> esquina inferior derecha, y ahí abajo solo tiene la cota del
    /// ancho, a 0.12, así que el rótulo cabe a los 0.24 y 0.33 de la macro sin bajarlo más.
    /// </remarks>
    /// <param name="yBot">Paño inferior de la planta.</param>
    public static double YRotuloPlanta(double yBot, int renglon) =>
        renglon == 0 ? yBot - PlantaTituloOffset : yBot - PlantaEscalaOffset;

    /// <summary>Renglón del título de la planta, el de la macro.</summary>
    public const double PlantaTituloOffset = 0.24;

    /// <summary>Renglón de la escala de la planta.</summary>
    public const double PlantaEscalaOffset = 0.33;

    /// <summary>
    /// El ancho del que dispone un rótulo: el de su zapata más el hueco de 80 cm que la fila
    /// deja a su izquierda.
    /// </summary>
    /// <remarks>
    /// Es lo que le toca a cada dibujo en la fila —<c>X = −0.8</c>—, así que un rótulo que quepa
    /// aquí no puede meterse en el de la zapata de al lado.
    /// </remarks>
    public static double AnchoParaElRotulo(double anchoM) => anchoM + SeparacionIzquierda;

    /// <summary>
    /// El alto con el que un texto de una línea <b>cabe</b> en el ancho disponible.
    /// </summary>
    /// <remarks>
    /// Devuelve <paramref name="altoMaximo"/> si ya cabe, y si no lo baja en proporción. Es la
    /// misma cuenta con la que la macro encoge el texto de la plantilla y el ID del dado, con su
    /// factor de 0.62 de ancho de letra: sin esto, «ZAPATA AISLADA DE LINDERO "ZE-1"» mide 1.3 m
    /// a 7 cm de alto y se mete en el dibujo vecino aunque esté en su propio renglón.
    /// </remarks>
    public static double AltoQueQuepa(
        int letras, double altoMaximo, double anchoDisponible, double factorLetra = 0.62)
    {
        if (letras <= 0 || altoMaximo <= 0 || anchoDisponible <= 0)
        {
            return altoMaximo;
        }

        var anchoTexto = letras * altoMaximo * factorLetra;

        if (anchoTexto <= anchoDisponible)
        {
            return altoMaximo;
        }

        return altoMaximo * anchoDisponible / anchoTexto;
    }

    /// <summary>
    /// Port de <c>YBasePlanta</c> de la macro central: la planta colgada del rótulo.
    /// </summary>
    /// <remarks>
    /// <b>Ya no se usa para colocar la planta</b> —ahora todas arrancan en −15, como el lindero—
    /// y se conserva porque es el cálculo de la macro central y permite comparar: colgar la
    /// planta del renglón más bajo del rótulo la movía cada vez que el rótulo cambiaba de alto.
    /// </remarks>
    public static double YPlantaCentral(double yZapBot, double largoM)
    {
        var yFondoCorte = yZapBot - RotuloEscalaOffset;
        var yTopePlanta = yFondoCorte + PlantaOffsetY;

        return yTopePlanta - largoM;
    }

    /// <summary>
    /// La planta del lindero: en −15, o más abajo si la zapata es larga.
    /// </summary>
    public static double YPlantaLindero(double yZapBot, double largoM)
    {
        var y = yZapBot - RotuloEscalaOffset - PlantaSeparacionMin - largoM - PlantaCotaOffsetDado;

        return y > PlantaYBaseLindero ? PlantaYBaseLindero : y;
    }

    // ======================================================================
    // Las parrillas en la elevación
    // ======================================================================

    /// <summary>
    /// Una parrilla vista de canto: la barra que corre y los círculos de la transversal.
    /// </summary>
    /// <param name="YBarra">Eje de la barra que corre a lo largo.</param>
    /// <param name="YCirculos">Eje de las varillas transversales, vistas de punta.</param>
    /// <param name="XCaraIzq">Cara exterior del gancho izquierdo.</param>
    /// <param name="XCaraDer">La del derecho.</param>
    /// <param name="Diam">Diámetro de la barra que corre.</param>
    /// <param name="DiamCirculos">Diámetro de la transversal.</param>
    /// <param name="Circulos">Centros en X de las transversales.</param>
    public readonly record struct Parrilla(
        double YBarra,
        double YCirculos,
        double XCaraIzq,
        double XCaraDer,
        double Diam,
        double DiamCirculos,
        double[] Circulos);

    /// <summary>
    /// Port de <c>DibujarParrillaZapata</c>: dónde va cada cosa de una parrilla.
    /// </summary>
    /// <remarks>
    /// La barra que corre se apoya en el recubrimiento y las transversales van <b>por dentro</b>
    /// de ella —arriba en la parrilla inferior y abajo en la superior—, que es el orden real de
    /// armado. Los círculos arrancan a medio diámetro de la cara del gancho y se reparten con su
    /// separación; el último se pone solo si cabe.
    /// </remarks>
    public static Parrilla ParrillaEnAlzado(
        double xBase, double yZapBot, double anchoM, double espesorM, double recM,
        double diam, double diamCirculos, double sepCirculosM, bool superior)
    {
        var yBarra = superior
            ? yZapBot + espesorM - recM - (diam / 2)
            : yZapBot + recM + (diam / 2);

        var yCirculos = superior
            ? yBarra - (diam / 2) - (diamCirculos / 2)
            : yBarra + (diam / 2) + (diamCirculos / 2);

        var xCaraIzq = xBase + recM;
        var xCaraDer = xBase + anchoM - recM;

        var sep = sepCirculosM > 0 ? sepCirculosM : 0.12;

        var xIzq = xCaraIzq + (diam / 2) + (diamCirculos / 2);
        var xDer = xCaraDer - (diam / 2) - (diamCirculos / 2);

        var circulos = new List<double>();

        if (xDer > xIzq)
        {
            circulos.Add(xIzq);

            // La tolerancia es la de la macro: un 20 % de la separación. Sin ella, el
            // último círculo puede caer pegado al del extremo.
            var tol = sep * 0.2;
            var x = xIzq + sep;

            while (x < xDer - tol)
            {
                circulos.Add(x);
                x += sep;
            }

            circulos.Add(xDer);
        }

        return new Parrilla(
            yBarra, yCirculos, xCaraIzq, xCaraDer, diam, diamCirculos, circulos.ToArray());
    }

    // ======================================================================
    // Los estribos del dado
    // ======================================================================

    /// <summary>
    /// Port de <c>BuildStirrupCenters</c>: los centros de los estribos del dado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con separación <b>variable</b> —una celda del tipo <c>10-15-20</c>— el elemento se parte
    /// en tres zonas de 25 %, 50 % y 25 %, que es el confinamiento de los extremos. Con
    /// separación única se reparten por igual, con un mínimo de tres.
    /// </para>
    /// <para>
    /// Devuelve las posiciones medidas <b>a lo largo del dado</b>, desde su arranque en la
    /// zapata: el dibujante las rota 90° con el resto del elemento, igual que la macro.
    /// </para>
    /// </remarks>
    public static double[] CentrosEstribos(
        double largo, double s1Cm, double s2Cm, double s3Cm, double offIni, double offFin)
    {
        var centros = new List<double>();

        var iniInterior = offIni;
        var finInterior = largo - offFin;
        var largoInterior = finInterior - iniInterior;

        if (largoInterior <= 0)
        {
            return centros.ToArray();
        }

        var s1 = Math.Max(s1Cm / 100.0, SepEstriboMinima);
        var s2 = s2Cm > 0 ? Math.Max(s2Cm / 100.0, SepEstriboMinima) : s1;
        var s3 = s3Cm > 0 ? Math.Max(s3Cm / 100.0, SepEstriboMinima) : s1;

        var variable = Math.Abs(s1 - s2) > 1e-4 || Math.Abs(s2 - s3) > 1e-4;

        void Agregar(double v)
        {
            if (centros.Count == 0 || Math.Abs(centros[^1] - v) > 1e-4)
            {
                centros.Add(v);
            }
        }

        void PorSeparacion(double desde, double hasta, double sep)
        {
            var n = (int)((hasta - desde) / sep);

            if (n < 1)
            {
                n = 1;
            }

            for (var i = 1; i <= n; i++)
            {
                var pos = desde + (i * sep);

                if (pos < hasta - 1e-4)
                {
                    Agregar(iniInterior + pos);
                }
            }
        }

        if (variable)
        {
            var zona1 = largoInterior * 0.25;
            var zona2 = zona1 + (largoInterior * 0.5);

            PorSeparacion(0, zona1, s1);
            PorSeparacion(zona1, zona2, s2);
            PorSeparacion(zona2, largoInterior, s3);
        }
        else
        {
            var n = (int)(largoInterior / s1);

            if (n < 3)
            {
                n = 3;
            }

            var paso = largoInterior / n;

            for (var i = 1; i <= n - 1; i++)
            {
                Agregar(iniInterior + (i * paso));
            }
        }

        return centros.ToArray();
    }

    /// <summary>
    /// Igual que la anterior, con el <c>forzarEstriboFin</c> de la macro.
    /// </summary>
    /// <remarks>
    /// Port de <c>BuildStirrupCenters</c> con <c>forzarEstriboFin = True</c>: pone un estribo
    /// <b>justo en el tope</b> del dado, y para hacerle sitio quita los que quedaran a menos del
    /// 60 % de la separación mínima. Es lo que la macro usa cuando encima del dado va una columna
    /// de concreto: sin ese estribo, la última varilla del dado se queda sin confinar.
    /// </remarks>
    public static double[] CentrosEstribos(
        double largo, double s1Cm, double s2Cm, double s3Cm, double offIni, double offFin,
        bool forzarFin)
    {
        var centros = CentrosEstribos(largo, s1Cm, s2Cm, s3Cm, offIni, offFin).ToList();

        if (!forzarFin)
        {
            return centros.ToArray();
        }

        var fin = largo - offFin;
        var minima = SepEstriboMinima * 0.6;

        while (centros.Count > 0 && fin - centros[^1] < minima)
        {
            centros.RemoveAt(centros.Count - 1);
        }

        centros.Add(fin);

        return centros.ToArray();
    }

    /// <summary>
    /// Port de <c>BuildStirrupCentersUniforme</c>: separación única de punta a punta.
    /// </summary>
    /// <remarks>
    /// Es lo que usa la <b>columna</b>: no se reparte en tres zonas, se pone un estribo cada
    /// separación desde el retiro inicial. Y la separación es la <b>más cerrada</b> de la celda
    /// (<see cref="SeparacionMinimaCm"/>): en el tramo de columna que se dibuja —80 cm justo
    /// encima del dado— se está en zona de confinamiento, así que la macro no abre el paso.
    /// </remarks>
    public static double[] CentrosUniformes(
        double largo, double sepCm, double offIni, double offFin)
    {
        var salida = new List<double>();

        var ini = offIni;
        var fin = largo - offFin;

        var sep = Math.Max(sepCm / 100.0, SepEstriboMinima);

        if (fin <= ini)
        {
            return salida.ToArray();
        }

        var p = ini;

        while (p <= fin + 1e-4)
        {
            if (salida.Count == 0 || Math.Abs(salida[^1] - p) > 1e-4)
            {
                salida.Add(p);
            }

            p += sep;
        }

        return salida.ToArray();
    }

    /// <summary>Port de <c>SeparacionMinima</c>: la más cerrada de los tres tramos.</summary>
    public static double SeparacionMinimaCm(double[] tramos)
    {
        var min = 0.0;

        foreach (var t in tramos)
        {
            if (t > 0 && (min == 0 || t < min))
            {
                min = t;
            }
        }

        return min <= 0 ? 12 : min;
    }

    /// <summary>
    /// Port de <c>ApplyCapsuleProtrusion</c>: el primero y el último salen un poco.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que la cápsula del estribo de los extremos asome del acero que abraza, y
    /// no un adorno: sin eso, el estribo del extremo queda escondido detrás de la barra.
    /// </remarks>
    public static void Sobresalir(double[] centros)
    {
        if (centros.Length == 0)
        {
            return;
        }

        centros[0] -= EstriboSobresale;
        centros[^1] += EstriboSobresale;
    }

    /// <summary>Port de <c>QuitarPrimerosEstribos</c>.</summary>
    /// <remarks>
    /// El dado se salta los primeros porque ahí está la parrilla de la zapata: dibujarlos
    /// pondría estribos encima del acero de la parrilla. Son dos con doble parrilla y uno con
    /// una sola, que es lo que la macro decide con <c>DADO_ESTRIBOS_OMITIR_*</c>.
    /// </remarks>
    public static double[] QuitarPrimeros(double[] centros, int n) =>
        n <= 0 || centros.Length <= n ? (n > 0 ? Array.Empty<double>() : centros) : centros[n..];

    // ======================================================================
    // La malla en planta
    // ======================================================================

    /// <summary>
    /// Port de <c>PosicionesConSeparacion</c>: dónde va cada varilla de la malla.
    /// </summary>
    /// <remarks>
    /// La última se agrega solo si el hueco que queda pasa del 30 % de la separación —el
    /// <c>PLANTA_FRACCION_CIERRE</c>—. Sin ese tope, en un ancho que no es múltiplo de la
    /// separación aparece una varilla pegada a la anterior, que en el plano se lee como un error
    /// de armado.
    /// </remarks>
    public static double[] Posiciones(double ini, double fin, double sep)
    {
        var salida = new List<double>();

        if (fin <= ini || sep <= 0)
        {
            return salida.ToArray();
        }

        var p = ini;
        var ultima = ini;

        while (p <= fin + 1e-4)
        {
            salida.Add(p);
            ultima = p;
            p += sep;
        }

        if (fin - ultima > sep * PlantaFraccionCierre)
        {
            salida.Add(fin);
        }

        return salida.ToArray();
    }

    /// <summary>
    /// El hueco del dado en planta, que es donde se recortan las varillas.
    /// </summary>
    /// <remarks>
    /// En la central el dado va centrado y en el lindero pegado al paño derecho, igual que en la
    /// elevación: la planta y el corte tienen que contar la misma historia.
    /// </remarks>
    public static (double X1, double Y1, double X2, double Y2) HuecoDelDado(
        ZapataCad z, double xBase, double yPlanta)
    {
        var wDado = z.AnchoDadoCm * EscalaElevacion;

        var yCen = yPlanta + (z.LargoM / 2);

        var y1 = yCen - (wDado / 2);
        var y2 = yCen + (wDado / 2);

        if (EsLindero(z.Tipo))
        {
            var xDer = xBase + z.AnchoM;

            return (xDer - wDado, y1, xDer, y2);
        }

        var xCen = xBase + (z.AnchoM / 2);

        return (xCen - (wDado / 2), y1, xCen + (wDado / 2), y2);
    }

    /// <summary>
    /// La separación de una celda de texto, <b>en metros</b>. Vacía, cero o ilegible cae en
    /// <paramref name="porOmisionM"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vive aquí, junto a la geometría, porque la leen <b>dos</b> sitios: la vista previa y el
    /// dibujante de AutoCAD. Con una copia en cada lado, el día que uno aprenda a leer «@20» o
    /// las comas decimales y el otro no, la previa enseñaría una malla y el plano saldría con
    /// otra. Es el mismo motivo por el que el acomodo también está aquí y no en la ventana.
    /// </para>
    /// <para>
    /// Se tolera lo que la gente escribe de verdad en una celda: «20», «20 cm», «@20», «20,5» y
    /// los espacios de sobra. Si de la celda sale un número que no es positivo, se devuelve el
    /// valor por omisión: dibujar con separación cero sería un ciclo infinito de varillas.
    /// </para>
    /// </remarks>
    public static double SeparacionM(string? texto, double porOmisionM = 0.12)
    {
        var t = (texto ?? string.Empty)
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("@", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.')
            .Trim();

        return double.TryParse(
            t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v / 100.0
            : porOmisionM;
    }

    /// <summary>
    /// Los tres tramos de una celda de estribos del tipo <c>9-18-9</c>, <b>en centímetros</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devuelve siempre tres valores. Con una sola separación —«15»— el segundo y el tercero
    /// salen en cero, que es lo que <see cref="CentrosEstribos"/> entiende como «separación
    /// única de punta a punta». Con dos, el tercero queda en cero.
    /// </para>
    /// <para>
    /// Si el primer tramo no se puede leer se devuelve <paramref name="porOmisionCm"/> en él:
    /// un dado sin estribos no es un dibujo incompleto, es un dibujo <b>equivocado</b>, así que
    /// se dibuja con una separación razonable y quien llama lo avisa.
    /// </para>
    /// </remarks>
    public static double[] TramosCm(string? texto, double porOmisionCm = 15)
    {
        var partes = (texto ?? string.Empty)
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("@", string.Empty, StringComparison.Ordinal)
            .Split('-');

        var salida = new double[3];

        for (var i = 0; i < 3; i++)
        {
            salida[i] = i < partes.Length
                && double.TryParse(
                    partes[i].Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v > 0
                    ? v
                    : 0;
        }

        if (salida[0] <= 0)
        {
            salida[0] = porOmisionCm;
        }

        return salida;
    }
}
