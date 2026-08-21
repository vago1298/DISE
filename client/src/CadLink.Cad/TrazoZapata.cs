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
    /// El <b>paño izquierdo</b> de la zapata número <paramref name="indice"/>, en metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí está la diferencia de acomodo entre las dos macros, y es de sentido, no de número:
    /// </para>
    /// <list type="bullet">
    /// <item>La <b>central</b> arranca en 0 y crece hacia la derecha: cada zapata se pone a un
    /// metro del borde derecho de la anterior. La macro lo hace acumulando
    /// <c>xBase = xBase + anchoZapata + SEPARACION_SECCIONES</c>, así que el sitio de una
    /// depende de los anchos de <b>todas</b> las de antes.</item>
    /// <item>La de <b>lindero</b> arranca en −3 y crece hacia la izquierda: se le resta la
    /// separación y el ancho de la zapata que se va a dibujar. Ojo con el detalle: se resta el
    /// ancho de la <b>nueva</b>, no el de la anterior, porque lo que se coloca es su paño
    /// izquierdo.</item>
    /// </list>
    /// </remarks>
    /// <param name="anchos">Los anchos, en metros, en el orden de la tabla.</param>
    /// <param name="indice">Cuál de ellas.</param>
    public static double XBase(string tipo, IReadOnlyList<double> anchos, int indice)
    {
        // La PRIMERA en cero, y cada siguiente un metro a la IZQUIERDA del paño izquierdo de la
        // anterior. El tipo ya no cambia el acomodo: sea central o de lindero, la fila crece
        // hacia la izquierda, que es como se pidió y como se lee un juego de zapatas puesto en
        // hilera. Antes las centrales crecían a la derecha desde cero y los linderos a la
        // izquierda desde −3, y al mezclar los dos tipos en una misma hoja se encimaban.
        var x = 0.0;

        for (var i = 1; i <= indice; i++)
        {
            x -= SeparacionIzquierda + Ancho(anchos, i);
        }

        _ = tipo;

        return x;
    }

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
