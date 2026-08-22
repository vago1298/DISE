namespace CadLink.Cad;

/// <summary>
/// La geometría de una <b>zapata corrida</b>: dónde cae cada línea, sin tocar AutoCAD.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>ZAPATA CORRIDA CENTRAL V2</c> y <c>ZAPATA CORRIDA LINDERO V2</c>. Las dos macros
/// dibujan <b>lo mismo</b> —plantilla, zapata, parrillas, muro, muro de enrase, cotas y rótulo— y
/// solo se separan en dos decisiones: <b>hacia dónde crece la fila</b> de secciones y <b>dónde va
/// el muro</b> sobre la zapata. Por eso hay una sola clase y el tipo entra como dato, igual que ya
/// se hizo con las aisladas en <see cref="TrazoZapata"/>.
/// </para>
/// <para>
/// Está aparte de la ventana y del dibujante a propósito: las mismas cuentas las necesitan el
/// dibujante de AutoCAD <b>y</b> la vista previa. Cuando cada uno calculaba lo suyo, la previa
/// enseñaba una cosa y el plano salía con otra; ese error ya se pagó una vez con las aisladas.
/// </para>
/// <para>
/// Lo que <b>no</b> está aquí: nada que necesite el dibujo abierto. La caja de los bloques de
/// contratrabe y de cadena de desplante la lee el dibujante y entra por parámetro —
/// <see cref="MuroDeEnrase"/> y <see cref="ColocarMuro"/> reciben la Y de arranque y la de tope—,
/// porque de otro modo esta clase dejaría de ser comprobable sin AutoCAD.
/// </para>
/// </remarks>
public static class TrazoZapataCorrida
{
    // ======================================================================
    // Niveles y escala
    // ======================================================================

    /// <summary>De centímetros de captura a metros de dibujo. El <c>SCALEELEVATION</c>.</summary>
    public const double EscalaElevacion = 0.01;

    /// <summary>
    /// Nivel de terreno natural: el <c>yNivTerr = −3.5</c> de las dos macros.
    /// </summary>
    /// <remarks>
    /// Es el nivel del que <b>cuelga todo</b>: la zapata se baja desde aquí su profundidad de
    /// desplante, no se sube desde un fondo fijo. Las aisladas hacen lo contrario —fondo fijo en
    /// <c>−8</c> y el terreno arriba, <see cref="TrazoZapata.YBaseElevacion"/>—, así que las dos
    /// familias <b>no</b> comparten este número y no se pueden mezclar: en las corridas, dos
    /// zapatas con desplantes distintos quedan con el terreno a la misma altura, que es como se
    /// lee un corte de cimentación.
    /// </remarks>
    public const double YNivelTerreno = -3.5;

    /// <summary>Espesor de la plantilla de concreto simple, en metros.</summary>
    public const double PlantillaEspesor = 0.05;

    /// <summary>Recubrimiento de las parrillas, en metros. Las macros lo fijan en 5 cm.</summary>
    public const double RecPorOmision = 0.05;

    /// <summary>
    /// Paso entre una sección y la siguiente, en metros: los <b>2 m</b> de las macros.
    /// </summary>
    /// <remarks>
    /// Es paso <b>fijo</b>, no «ancho más holgura»: las dos macros mueven el juego entero con un
    /// <c>offsetX</c> que salta de dos en dos y la zapata se dibuja dentro. Con anchos muy grandes
    /// las secciones se acercan, y así es en el original; se deja igual porque el que revisa el
    /// plano compara contra lo que ya tiene dibujado.
    /// </remarks>
    public const double SeparacionSecciones = 2.0;

    /// <summary>
    /// Arranque de la fila de <b>lindero</b>, en metros: el primer <c>offsetX = −2</c>.
    /// </summary>
    /// <remarks>
    /// El lindero se acomoda a la <b>izquierda</b> del origen y la central a la <b>derecha</b>, de
    /// modo que las dos familias pueden convivir en el mismo dibujo sin encimarse. La central
    /// arranca en el propio origen: su primer <c>offsetX</c> es <b>0</b>.
    /// </remarks>
    public const double LinderoXPrimera = -2.0;

    // ======================================================================
    // El acomodo de la fila
    // ======================================================================

    /// <summary>La zapata es de lindero.</summary>
    public static bool EsLindero(string? tipo) =>
        ZapataCorridaCad.Lindero.Equals(
            (tipo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// X de arranque de la sección número <paramref name="indice"/>, contando desde 0.
    /// </summary>
    /// <remarks>
    /// Central: <c>0, 2, 4…</c> hacia la derecha. Lindero: <c>−2, −4, −6…</c> hacia la izquierda.
    /// Son los dos <c>offsetX</c> de las macros, tal cual.
    /// </remarks>
    public static double XBase(string? tipo, int indice)
    {
        var i = Math.Max(indice, 0);

        return EsLindero(tipo)
            ? LinderoXPrimera - (i * SeparacionSecciones)
            : i * SeparacionSecciones;
    }

    // ======================================================================
    // Las alturas y los paños
    // ======================================================================

    /// <summary>Todo lo que hace falta para empezar a dibujar una sección.</summary>
    /// <param name="XBase">Paño izquierdo de la zapata.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="XCentro">Eje de la zapata.</param>
    /// <param name="YZapBot">Fondo de la zapata: el desplante.</param>
    /// <param name="YZapTop">Lomo de la zapata, de donde arranca el muro.</param>
    /// <param name="YPlantillaBot">Fondo de la plantilla de concreto simple.</param>
    /// <param name="YTerreno">Nivel de terreno natural.</param>
    /// <param name="XMuroIzq">Paño izquierdo del muro.</param>
    /// <param name="XMuroDer">Paño derecho del muro.</param>
    public readonly record struct Acomodo(
        double XBase,
        double XDer,
        double XCentro,
        double YZapBot,
        double YZapTop,
        double YPlantillaBot,
        double YTerreno,
        double XMuroIzq,
        double XMuroDer);

    /// <summary>
    /// Coloca una zapata corrida: sus alturas, sus paños y los del muro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Las alturas.</b> El terreno manda: <c>yZapBot = yNivTerr − profundidad</c>. La plantilla
    /// va debajo del desplante y el lomo, un espesor por encima.
    /// </para>
    /// <para>
    /// <b>El muro.</b> En la central va <b>centrado</b> —<c>xCentro − espesor / 2</c>—; en el
    /// lindero su paño derecho <b>es</b> el paño derecho de la zapata, que es lo que significa
    /// lindero: por ese lado no hay dónde salirse. Si el muro fuera más ancho que la zapata se
    /// recorta al otro paño, igual que las aisladas hacen con el dado.
    /// </para>
    /// </remarks>
    public static Acomodo Colocar(ZapataCorridaCad z, double xBase)
    {
        var yTerreno = YNivelTerreno;
        var yZapBot = yTerreno - z.ProfundidadM;
        var yZapTop = yZapBot + z.EspesorM;

        var xDer = xBase + z.AnchoM;
        var xCentro = xBase + (z.AnchoM / 2);

        var espMuro = z.EspesorMuroM;

        var xMuroIzq = 0.0;
        var xMuroDer = 0.0;

        if (EsLindero(z.Tipo))
        {
            xMuroDer = xDer;
            xMuroIzq = xMuroDer - espMuro;

            if (xMuroIzq < xBase)
            {
                xMuroIzq = xBase;
            }
        }
        else
        {
            xMuroIzq = xCentro - (espMuro / 2);
            xMuroDer = xCentro + (espMuro / 2);

            if (xMuroIzq < xBase)
            {
                xMuroIzq = xBase;
            }

            if (xMuroDer > xDer)
            {
                xMuroDer = xDer;
            }
        }

        return new Acomodo(
            xBase, xDer, xCentro,
            yZapBot, yZapTop, yZapBot - PlantillaEspesor, yTerreno,
            xMuroIzq, xMuroDer);
    }

    /// <summary>
    /// El muro, ya colocado: de dónde arranca y hasta dónde llega.
    /// </summary>
    /// <param name="XIzq">Paño izquierdo.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="YBase">Arranque: el lomo de la zapata, o el de la contratrabe si la hay.</param>
    /// <param name="YTope">Tope: el fondo de la cadena de desplante, o el terreno si no la hay.</param>
    public readonly record struct Muro(double XIzq, double XDer, double YBase, double YTope);

    /// <summary>
    /// Coloca el muro entre lo que tenga debajo y lo que tenga encima.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los dos límites entran por parámetro porque los dos pueden venir de un <b>bloque</b> del
    /// dibujo: si hay contratrabe, el muro arranca de su lomo y no del de la zapata; y si hay
    /// cadena de desplante, remata en su fondo. El dibujante lee esas cajas; esta clase solo hace
    /// la cuenta, y así se puede comprobar sin AutoCAD.
    /// </para>
    /// <para>
    /// Si lo de arriba queda por debajo de lo de abajo —una cadena mal colocada, un desplante
    /// ridículo— el muro sale de alto <b>cero</b> y no negativo: dibujar un muro al revés llena el
    /// plano de líneas cruzadas y esconde el dato mal capturado.
    /// </para>
    /// </remarks>
    public static Muro ColocarMuro(Acomodo a, double yBase, double yTope) =>
        new(a.XMuroIzq, a.XMuroDer, yBase, Math.Max(yTope, yBase));

    // ======================================================================
    // El muro de enrase
    // ======================================================================
    //
    // Es la hilada de piezas que sube del lomo de la zapata —o de la contratrabe— hasta el fondo
    // de la cadena de desplante, y su gracia es que NO se dibuja con piezas de un alto fijo: la
    // macro busca en cuántas piezas iguales cabe el hueco para que cada una salga lo más cerca
    // posible de los 8 cm de una pieza de verdad. Así el enrase remata justo contra la cadena, sin
    // media pieza al final, que es lo que se ve mal en el plano.

    /// <summary>Alto al que la macro <b>quiere</b> que salga cada pieza, en metros.</summary>
    public const double EnraseAltoObjetivo = 0.08;

    /// <summary>Junta de mortero entre piezas, en metros.</summary>
    public const double EnraseJunta = 0.01;

    /// <summary>
    /// Cuánto se mete la pieza respecto del paño del muro, en metros.
    /// </summary>
    /// <remarks>
    /// El centímetro que hace que la hilada se <b>lea</b> como piezas y no como un bloque macizo:
    /// la pieza se dibuja 1 cm adentro por cada lado y la junta ocupa el paño completo.
    /// </remarks>
    public const double EnraseDesfaseLado = 0.01;

    /// <summary>Hasta cuántas piezas se prueban al buscar el reparto. La macro llega a 50.</summary>
    public const int EnraseMaxPiezas = 50;

    /// <summary>El enrase repartido: cuántas piezas, de qué alto y a qué altura va cada una.</summary>
    /// <param name="Piezas">Número de piezas. 0 = no cabe ninguna, no hay enrase.</param>
    /// <param name="AltoPieza">Alto de cada pieza, en metros.</param>
    /// <param name="Junta">Junta usada entre piezas.</param>
    /// <param name="YBases">Y del fondo de cada pieza, de abajo hacia arriba.</param>
    public readonly record struct Enrase(
        int Piezas, double AltoPieza, double Junta, double[] YBases);

    /// <summary>
    /// Reparte el hueco del enrase en piezas iguales, lo más cerca posible de los 8 cm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se prueban de 1 a <see cref="EnraseMaxPiezas"/> piezas: con <c>n</c> piezas hay
    /// <c>n − 1</c> juntas entre ellas, así que cada pieza mide
    /// <c>(alto − (n − 1) · junta) / n</c>. Gana el reparto cuyo alto de pieza se acerque más a
    /// <see cref="EnraseAltoObjetivo"/>, y solo se admiten repartos con alto de pieza positivo:
    /// con muchas piezas las juntas se comen el hueco y el alto sale negativo.
    /// </para>
    /// <para>
    /// Con un hueco más chico que nada no hay enrase: se devuelven <b>cero</b> piezas y quien
    /// dibuja se salta la hilada. Eso pasa de verdad cuando la contratrabe llega hasta la cadena.
    /// </para>
    /// </remarks>
    /// <param name="yBase">Arranque del enrase.</param>
    /// <param name="yTope">Tope del enrase.</param>
    public static Enrase MuroDeEnrase(double yBase, double yTope)
    {
        var hueco = yTope - yBase;

        if (hueco <= EnraseJunta)
        {
            return new Enrase(0, 0, EnraseJunta, Array.Empty<double>());
        }

        var mejorN = 0;
        var mejorAlto = 0.0;
        var mejorError = double.MaxValue;

        for (var n = 1; n <= EnraseMaxPiezas; n++)
        {
            var alto = (hueco - ((n - 1) * EnraseJunta)) / n;

            if (alto <= 0)
            {
                break;
            }

            var error = Math.Abs(alto - EnraseAltoObjetivo);

            if (error < mejorError)
            {
                mejorError = error;
                mejorN = n;
                mejorAlto = alto;
            }
        }

        if (mejorN <= 0)
        {
            return new Enrase(0, 0, EnraseJunta, Array.Empty<double>());
        }

        var bases = new double[mejorN];

        for (var i = 0; i < mejorN; i++)
        {
            bases[i] = yBase + (i * (mejorAlto + EnraseJunta));
        }

        return new Enrase(mejorN, mejorAlto, EnraseJunta, bases);
    }

    // ======================================================================
    // Las parrillas
    // ======================================================================

    /// <summary>
    /// Una parrilla de la zapata corrida vista de canto.
    /// </summary>
    /// <remarks>
    /// Es <b>el mismo</b> cálculo que las aisladas: las dos familias de macros llaman a la misma
    /// rutina <c>DibujarParrillaZapata</c>. Se delega en
    /// <see cref="TrazoZapata.ParrillaEnAlzado"/> en lugar de copiarla, para que el día que se
    /// corrija el reparto de los círculos se corrija en las cuatro macros a la vez.
    /// </remarks>
    public static TrazoZapata.Parrilla ParrillaEnAlzado(
        Acomodo a, double espesorM, double recM,
        double diam, double diamCirculos, double sepCirculosM, bool superior) =>
        TrazoZapata.ParrillaEnAlzado(
            a.XBase, a.YZapBot, a.XDer - a.XBase, espesorM, recM,
            diam, diamCirculos, sepCirculosM, superior);

    // ======================================================================
    // El acero del muro de concreto
    // ======================================================================

    /// <summary>
    /// El acero vertical de un muro de concreto: las barras y su doblez de arranque.
    /// </summary>
    /// <param name="X">X de cada barra vertical.</param>
    /// <param name="YBase">Fondo de la barra, ya dentro de la zapata.</param>
    /// <param name="YTope">Arriba de la barra.</param>
    /// <param name="Doblez">Largo del doblez horizontal del arranque, en metros.</param>
    /// <param name="Sentido">Hacia dónde dobla cada barra: <c>+1</c> derecha, <c>−1</c> izquierda.</param>
    public readonly record struct AceroVertical(
        double[] X, double YBase, double YTope, double Doblez, int[] Sentido);

    /// <summary>
    /// Coloca las barras verticales del muro de concreto, con su pata de anclaje.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cuántas.</b> Con doble parrilla, una barra por paño, cada una a su recubrimiento; con
    /// una sola, una barra al eje del muro. Es lo que decide la celda de doble parrilla del muro.
    /// </para>
    /// <para>
    /// <b>La pata.</b> Baja hasta el recubrimiento del fondo de la zapata y ahí dobla en
    /// horizontal <c>factor · diámetro</c>. El factor son los <b>15 diámetros</b> de las macros,
    /// y se pasa por <see cref="TrazoZapata.FactorGanchoValido"/> para que la casilla de la hoja
    /// —la que ya manda en las aisladas— mande también aquí: si un plano lleva las patas a 40
    /// diámetros, las lleva en las cuatro macros o el armador ve dos criterios en la misma obra.
    /// </para>
    /// <para>
    /// <b>Hacia dónde dobla.</b> Hacia <b>adentro</b> de la zapata, esto es, hacia su eje: es el
    /// único lado donde hay concreto para anclar. En la central las dos patas se miran; en el
    /// lindero las dos doblan hacia la izquierda, porque por la derecha está el lindero. Una barra
    /// justo al eje dobla a la derecha, que es lo que dibuja la macro.
    /// </para>
    /// </remarks>
    /// <param name="a">El acomodo de la zapata.</param>
    /// <param name="m">El muro ya colocado.</param>
    /// <param name="dobleParrilla">El muro lleva acero en los dos paños.</param>
    /// <param name="diam">Diámetro de la varilla del muro, en metros.</param>
    /// <param name="recM">Recubrimiento, en metros.</param>
    /// <param name="factorDoblez">Diámetros de doblez capturados en la hoja. 0 = el de la macro.</param>
    public static AceroVertical VerticalesDelMuro(
        Acomodo a, Muro m, bool dobleParrilla, double diam, double recM, double factorDoblez)
    {
        var rec = recM > 0 ? recM : RecPorOmision;

        var xs = new List<double>();

        if (dobleParrilla && m.XDer - m.XIzq > (2 * rec) + diam)
        {
            xs.Add(m.XIzq + rec + (diam / 2));
            xs.Add(m.XDer - rec - (diam / 2));
        }
        else
        {
            xs.Add((m.XIzq + m.XDer) / 2);
        }

        var doblez = TrazoZapata.FactorGanchoValido(factorDoblez) * diam;

        var sentidos = new int[xs.Count];

        for (var i = 0; i < xs.Count; i++)
        {
            // Hacia el eje de la zapata; si la barra cae justo en el eje, a la derecha.
            sentidos[i] = xs[i] > a.XCentro + 1e-9 ? -1 : 1;
        }

        return new AceroVertical(
            xs.ToArray(),
            a.YZapBot + rec + (diam / 2),
            m.YTope,
            doblez,
            sentidos);
    }

    /// <summary>
    /// Las varillas <b>horizontales</b> del muro, las que en el corte se ven de punta.
    /// </summary>
    /// <remarks>
    /// Se reparten del arranque al tope del muro con su separación, y se apoyan por <b>dentro</b>
    /// de las verticales —igual que las transversales de la parrilla—, que es el orden real de
    /// armado. La primera arranca a media separación del pie: pegarla al arranque la pondría
    /// encima del doblez.
    /// </remarks>
    /// <param name="m">El muro ya colocado.</param>
    /// <param name="sepM">Separación entre varillas horizontales, en metros.</param>
    public static double[] HorizontalesDelMuro(Muro m, double sepM)
    {
        var alto = m.YTope - m.YBase;
        var sep = sepM > 0 ? sepM : 0.2;

        if (alto <= 0)
        {
            return Array.Empty<double>();
        }

        var ys = new List<double>();

        var y = m.YBase + (sep / 2);

        while (y < m.YTope - 1e-9)
        {
            ys.Add(y);
            y += sep;
        }

        return ys.ToArray();
    }

    // ======================================================================
    // La anotación: cotas y rótulo
    // ======================================================================
    //
    // Los cuatro números de las cotas y los tres del rótulo son los de las macros, con el valor que
    // tenían allí. No se redondean ni se «acomodan a ojo»: la lección de las aisladas fue que mover
    // estas distancias despega la cota del elemento que mide, y que el encimado de los títulos no
    // se arregla moviéndolos de sitio sino con el ancho de letra.

    /// <summary>Cotas verticales, primera línea: a la izquierda del paño izquierdo.</summary>
    public const double CotaOffsetVert1 = 0.13;

    /// <summary>Cotas horizontales, por debajo del desplante.</summary>
    public const double CotaOffsetHoriz = 0.075;

    /// <summary>Segunda línea de cotas verticales: la del total.</summary>
    public const double CotaOffsetVert2 = 0.1445;

    /// <summary>Segunda línea de cotas horizontales.</summary>
    public const double CotaOffsetHoriz2 = 0.0585;

    /// <summary>Primer renglón del rótulo, por debajo de la plantilla.</summary>
    public const double RotuloOffset = 0.25;

    /// <summary>Segundo renglón del rótulo.</summary>
    public const double RotuloSalto1 = 0.34;

    /// <summary>Tercer renglón: el de la escala.</summary>
    public const double RotuloSalto2 = 0.42;

    /// <summary>
    /// Y del renglón <paramref name="renglon"/> del rótulo, contando desde 0.
    /// </summary>
    /// <remarks>
    /// Se miden <b>desde el fondo de la plantilla</b>, no desde el de la zapata: si se midieran
    /// desde la zapata, el rótulo se metería dentro de la plantilla y taparía su texto.
    /// </remarks>
    public static double YRotulo(double yZapBot, int renglon)
    {
        var yFondo = yZapBot - PlantillaEspesor;

        return renglon switch
        {
            <= 0 => yFondo - RotuloOffset,
            1 => yFondo - RotuloSalto1,
            _ => yFondo - RotuloSalto2,
        };
    }

    // ======================================================================
    // Los colores del relleno
    // ======================================================================

    /// <summary>Color del sólido de la pieza del enrase.</summary>
    public const int EnraseColorPieza = 253;

    /// <summary>Color del sólido de la junta de mortero.</summary>
    public const int EnraseColorJunta = 252;

    /// <summary>Color del sólido de fondo del concreto.</summary>
    public const int ConcretoColorSolido = 9;

    /// <summary>Color del patrón <c>AR-CONC</c> encima del sólido.</summary>
    public const int ConcretoColorPatron = 251;

    /// <summary>Escala del patrón <c>AR-CONC</c> en la sección rellena.</summary>
    public const double ConcretoEscalaPatron = 0.0003;
}
