namespace CadLink.Cad;

/// <summary>
/// La geometría de una <b>zapata corrida</b>: dónde cae cada línea, sin tocar AutoCAD.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>ZAPATA CORRIDA CENTRAL V2</c> y <c>ZAPATA CORRIDA LINDERO V2</c>. Las dos macros
/// dibujan casi lo mismo —plantilla, zapata, parrillas, muro, muro de enrase, cotas y rótulo— y se
/// separan en cuatro decisiones: hacia dónde crece la fila, dónde va el muro sobre la zapata, y
/// <b>cómo</b> se resuelve el arranque del acero del muro de concreto: la central dobla cada
/// varilla hacia <b>su</b> lado, y la de lindero las dobla las dos a la izquierda y a <b>dos
/// alturas distintas</b>, porque por la derecha está el lindero y las patas no caben.
/// </para>
/// <para>
/// Está aparte de la ventana y del dibujante a propósito: las mismas cuentas las necesitan el
/// dibujante de AutoCAD <b>y</b> la vista previa. Cuando cada uno calculaba lo suyo, la previa
/// enseñaba una cosa y el plano salía con otra; ese error ya se pagó una vez con las aisladas.
/// </para>
/// <para>
/// Lo que <b>no</b> está aquí: nada que necesite el dibujo abierto. La caja de los bloques de
/// contratrabe y de cadena de desplante la lee el dibujante y entra por parámetro, porque de otro
/// modo esta clase dejaría de ser comprobable sin AutoCAD.
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
    /// <c>−8</c>, <see cref="TrazoZapata.YBaseElevacion"/>—, así que las dos familias <b>no</b>
    /// comparten este número: en las corridas, dos zapatas con desplantes distintos quedan con el
    /// terreno a la misma altura, que es como se lee un corte de cimentación.
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
    /// Paso <b>fijo</b>, no «ancho más holgura»: las dos macros mueven el juego con un
    /// <c>offsetX</c> que salta de dos en dos y la zapata se dibuja <b>centrada</b> en él.
    /// </remarks>
    public const double SeparacionSecciones = 2.0;

    /// <summary>
    /// Primer <c>offsetX</c> de la fila de <b>lindero</b>: <c>−2</c>.
    /// </summary>
    /// <remarks>
    /// El lindero se acomoda a la <b>izquierda</b> del origen —<c>offsetX = −2 − i · 2</c>— y la
    /// central a la <b>derecha</b> —<c>offsetX = i · 2</c>, empezando en el propio origen—, así que
    /// las dos familias pueden convivir en el mismo dibujo sin encimarse.
    /// </remarks>
    public const double LinderoPrimerOffset = -2.0;

    /// <summary>Alto que se le supone a la contratrabe cuando no hay bloque. La macro usa 30 cm.</summary>
    public const double ContratrabeAltoPorOmision = 0.3;

    /// <summary>Lo que baja la cadena de desplante del terreno cuando no hay bloque: 20 cm.</summary>
    public const double CadenaAltoPorOmision = 0.2;

    // ======================================================================
    // El acomodo de la fila
    // ======================================================================

    /// <summary>La zapata es de lindero.</summary>
    public static bool EsLindero(string? tipo) =>
        ZapataCorridaCad.Lindero.Equals(
            (tipo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El <c>offsetX</c> de la sección número <paramref name="indice"/>, contando desde 0.
    /// </summary>
    /// <remarks>
    /// Central: <c>0, 2, 4…</c> hacia la derecha. Lindero: <c>−2, −4, −6…</c> hacia la izquierda.
    /// Es el <b>eje</b> de la sección, no su paño: ver <see cref="XBase"/>.
    /// </remarks>
    public static double OffsetX(string? tipo, int indice)
    {
        var i = Math.Max(indice, 0);

        return EsLindero(tipo)
            ? LinderoPrimerOffset - (i * SeparacionSecciones)
            : i * SeparacionSecciones;
    }

    /// <summary>
    /// Paño <b>izquierdo</b> de la sección número <paramref name="indice"/>.
    /// </summary>
    /// <remarks>
    /// Las dos macros hacen <c>xBase = offsetX − ancho / 2</c>: la zapata se dibuja <b>centrada</b>
    /// en su offset, no arrancando en él. Se respeta porque de ello depende que la sección quede
    /// alineada con lo que ya está dibujado en el plano —y con el rótulo, que va centrado en el
    /// mismo eje—; arrancar en el offset corre media zapata cada sección.
    /// </remarks>
    public static double XBase(string? tipo, int indice, double anchoM) =>
        OffsetX(tipo, indice) - (anchoM / 2);

    // ======================================================================
    // Las alturas y los paños
    // ======================================================================

    /// <summary>Todo lo que hace falta para empezar a dibujar una sección.</summary>
    /// <param name="XBase">Paño izquierdo de la zapata.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="XCentro">Eje de la zapata: el <c>offsetX</c> de la macro.</param>
    /// <param name="YZapBot">Fondo de la zapata: el desplante.</param>
    /// <param name="YZapTop">Lomo de la zapata, de donde arranca el muro.</param>
    /// <param name="YPlantillaBot">Fondo de la plantilla: el <c>yBase</c> de la macro.</param>
    /// <param name="YTerreno">Nivel de terreno natural.</param>
    /// <param name="XMuroIzq">Paño izquierdo del muro.</param>
    /// <param name="XMuroDer">Paño derecho del muro.</param>
    /// <param name="XCentroMuro">Eje del muro. En la central coincide con el de la zapata.</param>
    public readonly record struct Acomodo(
        double XBase,
        double XDer,
        double XCentro,
        double YZapBot,
        double YZapTop,
        double YPlantillaBot,
        double YTerreno,
        double XMuroIzq,
        double XMuroDer,
        double XCentroMuro);

    /// <summary>
    /// Coloca una zapata corrida: sus alturas, sus paños y los del muro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Las alturas.</b> El terreno manda. La macro escribe
    /// <c>yBase = yNivTerr − profundidad − espPlantilla</c> y de ahí sube: el fondo de la zapata
    /// queda en <c>yNivTerr − profundidad</c> y la plantilla, debajo. El <c>yBase</c> de la macro
    /// es el <b>fondo de la plantilla</b>, y es el que manda en las cotas y en el rótulo.
    /// </para>
    /// <para>
    /// <b>El muro.</b> En la central va <b>centrado</b> en el eje; en el lindero su paño derecho
    /// <b>es</b> el paño derecho de la zapata, que es lo que significa lindero. No se recorta si
    /// sale más ancho que la zapata: las macros no lo recortan, y taparlo esconde el dato mal
    /// capturado en lugar de enseñarlo.
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
        }
        else
        {
            xMuroIzq = xCentro - (espMuro / 2);
            xMuroDer = xCentro + (espMuro / 2);
        }

        return new Acomodo(
            xBase, xDer, xCentro,
            yZapBot, yZapTop, yZapBot - PlantillaEspesor, yTerreno,
            xMuroIzq, xMuroDer, (xMuroIzq + xMuroDer) / 2);
    }

    /// <summary>El muro, ya colocado: de dónde arranca y hasta dónde llega.</summary>
    /// <param name="XIzq">Paño izquierdo.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="XCentro">Su eje. Es el que usa el acero cuando va una sola parrilla.</param>
    /// <param name="YBase">Arranque: el lomo de la contratrabe, o el de la zapata si no hay.</param>
    /// <param name="YTope">Tope: el terreno en el de concreto, el fondo de la cadena en el enrase.</param>
    public readonly record struct Muro(
        double XIzq, double XDer, double XCentro, double YBase, double YTope);

    /// <summary>
    /// Coloca el muro entre lo que tenga debajo y lo que tenga encima.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los dos límites entran por parámetro porque los dos pueden venir de un <b>bloque</b>: si hay
    /// contratrabe el muro arranca de su lomo, y si hay cadena de desplante remata en su fondo. La
    /// macro además sube el arranque al lomo de la zapata cuando la contratrabe queda por debajo
    /// —<c>If yMuroConcretoBot &lt; yZapTop Then yMuroConcretoBot = yZapTop</c>—, y eso se conserva.
    /// </para>
    /// <para>
    /// Si el tope queda por debajo del arranque el muro sale de alto <b>cero</b> y no negativo: la
    /// macro se planta con un aviso, y aquí quien llama decide, pero nunca dibuja un muro al revés.
    /// </para>
    /// </remarks>
    public static Muro ColocarMuro(Acomodo a, double yContratrabeTop, double yTope)
    {
        var yBase = Math.Max(yContratrabeTop, a.YZapTop);

        return new Muro(a.XMuroIzq, a.XMuroDer, a.XCentroMuro, yBase, Math.Max(yTope, yBase));
    }

    // ======================================================================
    // El muro de enrase
    // ======================================================================
    //
    // Es la hilada de piezas que sube del lomo de la contratrabe hasta el fondo de la cadena de
    // desplante, y su gracia es que NO se dibuja con piezas de un alto fijo: la macro busca en
    // cuántas piezas iguales cabe el hueco para que cada una salga lo más cerca posible de los 8 cm
    // de una pieza de verdad. Así el enrase remata justo contra la cadena, sin media pieza al final.

    /// <summary>Alto al que la macro <b>quiere</b> que salga cada pieza, en metros.</summary>
    public const double EnraseAltoObjetivo = 0.08;

    /// <summary>Junta de mortero entre piezas, en metros.</summary>
    public const double EnraseJunta = 0.01;

    /// <summary>
    /// Cuánto se mete la marca de la junta respecto del paño del muro, en metros.
    /// </summary>
    /// <remarks>
    /// El centímetro que hace que la hilada se <b>lea</b> como piezas: la pieza ocupa el paño
    /// completo y la junta va 1 cm adentro por cada lado.
    /// </remarks>
    public const double EnraseDesfaseLado = 0.01;

    /// <summary>Hasta cuántas piezas se prueban al buscar el reparto. La macro llega a 50.</summary>
    public const int EnraseMaxPiezas = 50;

    /// <summary>
    /// Hueco mínimo para que haya enrase, en metros: los <b>2 cm</b> de las macros.
    /// </summary>
    /// <remarks>
    /// Las dos preguntan <c>If altEnrase &gt; 0.02</c> antes de dibujar la hilada. Por debajo de
    /// eso lo que hay no es un muro de enrase: es la holgura entre la contratrabe y la cadena, y
    /// dibujarla como una pieza aplastada se lee como un error de armado.
    /// </remarks>
    public const double EnraseAltoMinimo = 0.02;

    /// <summary>El enrase repartido: cuántas piezas, de qué alto y dónde va cada una.</summary>
    /// <param name="Piezas">Número de piezas. 0 = no cabe, no hay enrase.</param>
    /// <param name="AltoPieza">Alto de cada pieza, en metros.</param>
    /// <param name="Junta">Junta usada entre piezas.</param>
    /// <param name="YBases">Y del fondo de cada pieza, de abajo hacia arriba.</param>
    /// <param name="XIzq">Paño izquierdo de la hilada.</param>
    /// <param name="Ancho">Ancho de la hilada.</param>
    public readonly record struct Enrase(
        int Piezas, double AltoPieza, double Junta, double[] YBases, double XIzq, double Ancho);

    /// <summary>
    /// Reparte el hueco del enrase en piezas iguales, lo más cerca posible de los 8 cm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se prueban de 1 a <see cref="EnraseMaxPiezas"/> piezas: con <c>n</c> piezas hay
    /// <c>n − 1</c> juntas entre ellas, así que cada pieza mide
    /// <c>(alto − (n − 1) · junta) / n</c>. Gana el reparto cuyo alto de pieza se acerque más a
    /// <see cref="EnraseAltoObjetivo"/>, y solo se admiten repartos con alto positivo: con muchas
    /// piezas las juntas se comen el hueco.
    /// </para>
    /// <para>
    /// <b>El ancho no es el del muro.</b> Cuando hay cadena de desplante, las dos macros toman el
    /// ancho y el paño izquierdo <b>de la caja de la cadena</b>: el enrase se enrasa con ella, que
    /// es de donde le viene el nombre. Por eso entran por parámetro y no se sacan del muro.
    /// </para>
    /// </remarks>
    /// <param name="xIzq">Paño izquierdo de la hilada: el de la cadena si la hay.</param>
    /// <param name="ancho">Ancho de la hilada: el de la cadena si la hay.</param>
    /// <param name="yBase">Arranque del enrase: el lomo de la contratrabe.</param>
    /// <param name="yTope">Tope: el fondo de la cadena de desplante.</param>
    public static Enrase MuroDeEnrase(double xIzq, double ancho, double yBase, double yTope)
    {
        var hueco = yTope - yBase;

        if (hueco <= EnraseAltoMinimo || ancho <= 0)
        {
            return new Enrase(0, 0, EnraseJunta, Array.Empty<double>(), xIzq, ancho);
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
            return new Enrase(0, 0, EnraseJunta, Array.Empty<double>(), xIzq, ancho);
        }

        var bases = new double[mejorN];

        for (var i = 0; i < mejorN; i++)
        {
            bases[i] = yBase + (i * (mejorAlto + EnraseJunta));
        }

        return new Enrase(mejorN, mejorAlto, EnraseJunta, bases, xIzq, ancho);
    }

    // ======================================================================
    // Las parrillas
    // ======================================================================

    /// <summary>
    /// Una parrilla de la zapata corrida vista de canto.
    /// </summary>
    /// <remarks>
    /// Es <b>el mismo</b> cálculo que las aisladas: las cuatro macros llaman a la misma rutina
    /// <c>DibujarParrillaZapata</c>, con el mismo gancho de 3 cm y la misma tolerancia del 20 %
    /// en el reparto de los círculos. Se delega en
    /// <see cref="TrazoZapata.ParrillaEnAlzado"/> en lugar de copiarla.
    /// </remarks>
    public static TrazoZapata.Parrilla ParrillaEnAlzado(
        Acomodo a, double espesorM, double recM,
        double diam, double diamCirculos, double sepCirculosM, bool superior) =>
        TrazoZapata.ParrillaEnAlzado(
            a.XBase, a.YZapBot, a.XDer - a.XBase, espesorM, recM,
            diam, diamCirculos, sepCirculosM, superior);

    /// <summary>Largo del gancho de las parrillas, en metros. Las dos macros pasan 0.03.</summary>
    public const double GanchoParrilla = 0.03;

    // ======================================================================
    // El acero del muro de concreto
    // ======================================================================

    /// <summary>
    /// Retiro del acero del muro respecto de su paño, en metros: el <c>offsetMuro = 0.05</c>.
    /// </summary>
    /// <remarks>
    /// Ojo: son <b>5 cm al eje de la varilla</b>, no «recubrimiento más medio diámetro». Así está
    /// en las dos macros, y cambiarlo mueve el acero de todos los muros del plano.
    /// </remarks>
    public const double MuroRetiroAcero = 0.05;

    /// <summary>
    /// Lo que se corre la varilla vertical respecto del eje del acero, en metros.
    /// </summary>
    /// <remarks>
    /// Sale de una rareza de las dos macros que se porta <b>tal cual</b>: el desplazamiento lo
    /// calculan con <c>DiametroVarilla(varMuroHoriz)</c>, o sea le pasan a la tabla de diámetros
    /// la celda de la <b>separación</b> horizontal. Una celda que dice «20» no es ninguna varilla,
    /// así que la tabla cae en su valor por omisión —el del <b>#3</b>, 0.009525 m— y ese es el
    /// desplazamiento que sale en el plano. Se conserva porque es lo que está dibujado en las obras
    /// ya entregadas; si algún día se captura ahí una varilla de verdad, sale su diámetro, como en
    /// la macro.
    /// </remarks>
    public const double MuroDesplazamientoPorOmision = 0.009525;

    /// <summary>
    /// El desplazamiento de la varilla del muro: lo que salga de la celda, o el del <b>#3</b>.
    /// </summary>
    /// <param name="diamDeLaCeldaM">
    /// Lo que la tabla de diámetros devuelve para la celda de separación horizontal, en metros.
    /// </param>
    public static double DesplazamientoDelMuro(double diamDeLaCeldaM) =>
        diamDeLaCeldaM > 0 ? diamDeLaCeldaM : MuroDesplazamientoPorOmision;

    /// <summary>Los ejes verticales donde va el acero del muro.</summary>
    /// <param name="X1">Paño izquierdo, o el eje del muro si va una sola parrilla.</param>
    /// <param name="X2">Paño derecho, o el eje del muro si va una sola parrilla.</param>
    /// <param name="Doble">Cabe doble parrilla de verdad.</param>
    public readonly record struct EjesAcero(double X1, double X2, bool Doble);

    /// <summary>
    /// Los dos ejes del acero del muro, o uno si no cabe la doble parrilla.
    /// </summary>
    /// <remarks>
    /// La macro se protege sola: si con los 5 cm de retiro el eje derecho queda a la izquierda del
    /// izquierdo —un muro de 8 cm—, los dos se van al eje del muro y la doble parrilla se
    /// <b>desactiva</b>. Sin eso, un muro delgado sale con el acero cruzado.
    /// </remarks>
    public static EjesAcero EjesDelAcero(Muro m, bool dobleParrilla)
    {
        if (dobleParrilla)
        {
            var x1 = m.XIzq + MuroRetiroAcero;
            var x2 = m.XDer - MuroRetiroAcero;

            if (x2 > x1)
            {
                return new EjesAcero(x1, x2, true);
            }
        }

        return new EjesAcero(m.XCentro, m.XCentro, false);
    }

    /// <summary>
    /// Las varillas del muro que en el corte se ven <b>de punta</b>, como círculos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se reparten desde el arranque del muro con la separación <b>vertical</b> —la celda
    /// <c>H13</c> / <c>R13</c>—, que es la que manda porque son las que se van repitiendo hacia
    /// arriba. La primera va a medio diámetro del arranque y el tope está a medio diámetro del
    /// terreno.
    /// </para>
    /// <para>
    /// Y se dibuja <b>una menos</b> de las que caben: las macros cuentan cuántas entran y luego
    /// hacen <c>numADibujar = totalVarillas − 1</c>. La de más caía pegada al terreno, encima de la
    /// línea del nivel.
    /// </para>
    /// </remarks>
    /// <param name="m">El muro ya colocado.</param>
    /// <param name="yTerreno">Nivel de terreno: el tope del reparto.</param>
    /// <param name="diam">Diámetro de la varilla del muro, en metros.</param>
    /// <param name="sepVertM">Separación vertical, en metros.</param>
    public static double[] CirculosDelMuro(Muro m, double yTerreno, double diam, double sepVertM)
    {
        var sep = sepVertM > 0 ? sepVertM : 0.12;

        var yInicio = m.YBase + (diam / 2);
        var yTope = yTerreno - (diam / 2);

        // La cuenta de la macro, tal cual: se cuenta cuántas caben y se dibuja una menos.
        var caben = 0;
        var y = yInicio;

        while (y <= yTope + 0.0001)
        {
            caben++;
            y += sep;
        }

        if (Math.Abs(y - sep - yTope) > 0.0001)
        {
            caben++;
        }

        var aDibujar = caben - 1;

        if (aDibujar <= 0)
        {
            return Array.Empty<double>();
        }

        var ys = new double[aDibujar];

        for (var i = 0; i < aDibujar; i++)
        {
            ys[i] = yInicio + (i * sep);
        }

        return ys;
    }

    /// <summary>Una varilla vertical del muro, con su pata de anclaje.</summary>
    /// <param name="X">Eje de la varilla.</param>
    /// <param name="YTop">Arriba: el nivel de terreno.</param>
    /// <param name="YEsquina">Eje del tramo horizontal de la pata.</param>
    /// <param name="XFinDoblez">Dónde acaba la pata.</param>
    /// <param name="Sentido">Hacia dónde dobla: <c>+1</c> derecha, <c>−1</c> izquierda.</param>
    public readonly record struct VarillaMuro(
        double X, double YTop, double YEsquina, double XFinDoblez, int Sentido);

    /// <summary>
    /// Factor del doblez del muro, en <b>diámetros</b>: el <c>FACTOR_DOBLES_MURO = 15</c>.
    /// </summary>
    /// <remarks>
    /// Es el mismo criterio que el arranque del dado en las aisladas, así que se valida con
    /// <see cref="TrazoZapata.FactorGanchoValido"/>: si un plano lleva las patas a 40 diámetros,
    /// las lleva en las cuatro macros o el armador ve dos criterios en la misma obra.
    /// </remarks>
    public const double FactorDoblezMuro = TrazoZapata.FactorGanchoAbajo;

    /// <summary>Separación entre los dos dobleces del lindero, en diámetros.</summary>
    public const double LinderoSepDoblecesFactor = 4.0;

    /// <summary>Y esa separación nunca baja de 5 cm.</summary>
    public const double LinderoSepDoblecesMin = 0.05;

    /// <summary>Si no cabe, se aprieta hasta 2.5 diámetros y no menos.</summary>
    public const double LinderoSepDoblecesFactorMin = 2.5;

    /// <summary>Holgura que el lindero deja sobre la parrilla antes de doblar: 3 mm.</summary>
    public const double LinderoHolguraSobreParrilla = 0.003;

    /// <summary>
    /// La Y del tramo horizontal de la pata: justo <b>encima</b> de la parrilla inferior.
    /// </summary>
    /// <remarks>
    /// La pata se apoya sobre la varilla transversal de la parrilla inferior —la que se ve de
    /// punta—, y si esa cuenta queda por debajo de la barra que corre, manda la barra. Son las dos
    /// condiciones de las macros, y son las que evitan que la pata caiga <b>encima</b> del acero de
    /// la parrilla. El lindero añade 3 mm de holgura; la central no.
    /// </remarks>
    /// <param name="yBarraInf">Eje de la barra que corre de la parrilla inferior.</param>
    /// <param name="diamInfLong">Su diámetro.</param>
    /// <param name="yCirculosInf">Eje de las transversales de la parrilla inferior.</param>
    /// <param name="diamInfTrans">Su diámetro.</param>
    /// <param name="diamMuro">Diámetro de la varilla del muro.</param>
    /// <param name="lindero">Es la macro de lindero: añade la holgura de 3 mm.</param>
    public static double YDeLaPata(
        double yBarraInf, double diamInfLong, double yCirculosInf, double diamInfTrans,
        double diamMuro, bool lindero)
    {
        var y = yCirculosInf + (diamInfTrans / 2) + (diamMuro / 2)
                + (lindero ? LinderoHolguraSobreParrilla : 0);

        var piso = yBarraInf + (diamInfLong / 2) + (diamMuro / 2);

        return y < piso ? piso : y;
    }

    /// <summary>
    /// Las varillas verticales del muro de concreto, con su pata, para la <b>central</b>.
    /// </summary>
    /// <remarks>
    /// Cada varilla dobla hacia <b>su</b> lado —la izquierda a la izquierda y la derecha a la
    /// derecha—, las dos a la <b>misma</b> altura, y la pata mide <c>factor · diámetro</c> desde el
    /// eje. Con una sola parrilla va la del eje del muro, doblada a la izquierda.
    /// </remarks>
    /// <param name="ejes">Los ejes del acero.</param>
    /// <param name="yTerreno">Arriba de las varillas.</param>
    /// <param name="yPata">La Y del tramo horizontal, de <see cref="YDeLaPata"/>.</param>
    /// <param name="diamMuro">Diámetro de la varilla del muro.</param>
    /// <param name="desplazamiento">Lo que se corre respecto del eje del acero.</param>
    /// <param name="factorDoblez">Diámetros capturados en la hoja. 0 = los 15 de la macro.</param>
    public static VarillaMuro[] VerticalesCentral(
        EjesAcero ejes, double yTerreno, double yPata, double diamMuro,
        double desplazamiento, double factorDoblez)
    {
        var doblez = TrazoZapata.FactorGanchoValido(factorDoblez) * diamMuro;

        if (!ejes.Doble)
        {
            var x = ejes.X1 + desplazamiento;

            return new[] { new VarillaMuro(x, yTerreno, yPata, x - doblez, -1) };
        }

        var xIzq = ejes.X1 + desplazamiento;
        var xDer = ejes.X2 - desplazamiento;

        return new[]
        {
            new VarillaMuro(xIzq, yTerreno, yPata, xIzq - doblez, -1),
            new VarillaMuro(xDer, yTerreno, yPata, xDer + doblez, 1),
        };
    }

    /// <summary>
    /// Las varillas verticales del muro de concreto, con su pata, para el <b>lindero</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí las dos patas doblan a la <b>izquierda</b>: por la derecha está el lindero y no hay
    /// concreto donde anclar. Y por eso mismo van a <b>dos alturas distintas</b> —la del paño
    /// derecho abajo y la del izquierdo más arriba—, porque dobladas al mismo nivel se montarían
    /// una sobre otra y sus cotas saldrían encimadas.
    /// </para>
    /// <para>
    /// La separación entre las dos alturas son 4 diámetros, con un mínimo de 5 cm, y si no cabe por
    /// debajo del recubrimiento del lomo de la zapata se aprieta hasta 2.5 diámetros. La pata se
    /// recorta al recubrimiento del paño izquierdo de la zapata: una pata que se sale del concreto
    /// no ancla nada.
    /// </para>
    /// </remarks>
    /// <param name="a">El acomodo de la zapata.</param>
    /// <param name="ejes">Los ejes del acero.</param>
    /// <param name="yPata">La Y del tramo horizontal de la varilla más baja.</param>
    /// <param name="diamMuro">Diámetro de la varilla del muro.</param>
    /// <param name="desplazamiento">Lo que se corre respecto del eje del acero.</param>
    /// <param name="recM">Recubrimiento de la zapata.</param>
    /// <param name="factorDoblez">Diámetros capturados en la hoja. 0 = los 15 de la macro.</param>
    public static VarillaMuro[] VerticalesLindero(
        Acomodo a, EjesAcero ejes, double yPata, double diamMuro,
        double desplazamiento, double recM, double factorDoblez)
    {
        var rec = recM > 0 ? recM : RecPorOmision;

        var doblez = TrazoZapata.FactorGanchoValido(factorDoblez) * diamMuro;

        // El tope de la pata: el recubrimiento del paño izquierdo de la zapata.
        var xLimIzq = a.XBase + rec + (diamMuro / 2);

        // Radio al centro del doblez del lindero: rInt = diámetro, rExt = 2 diámetros.
        var radioCentro = 1.5 * diamMuro;

        double XFin(double xVar)
        {
            var x = xVar - doblez;

            if (x < xLimIzq)
            {
                x = xLimIzq;
            }

            var maximo = xVar - radioCentro - diamMuro;

            return x > maximo ? maximo : x;
        }

        if (!ejes.Doble)
        {
            var x = ejes.X1 + desplazamiento;

            return new[] { new VarillaMuro(x, a.YTerreno, yPata, XFin(x), -1) };
        }

        var sep = SepDeLosDobleces(a, yPata, diamMuro, rec);

        var xIzq = ejes.X1 + desplazamiento;
        var xDer = ejes.X2 - desplazamiento;

        // La DERECHA lleva el doblez más bajo y la IZQUIERDA el de arriba: así están en la macro,
        // y así la pata de arriba no cruza por encima de la varilla de la derecha.
        return new[]
        {
            new VarillaMuro(xDer, a.YTerreno, yPata, XFin(xDer), -1),
            new VarillaMuro(xIzq, a.YTerreno, yPata + sep, XFin(xIzq), -1),
        };
    }

    /// <summary>
    /// La X del círculo para que quede <b>tangente</b> a la varilla vertical de su eje.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos van sobre el mismo eje de acero, pero la vertical se corre de él lo que manda la
    /// macro —el <see cref="DesplazamientoDelMuro"/>—, así que dibujar el círculo en el eje pelado
    /// lo mete <b>dentro</b> de la vertical: en el plano se veía la varilla vertical atravesada por
    /// el círculo. Se pidió que se toquen y no se monten.
    /// </para>
    /// <para>
    /// El círculo se aparta <b>al lado contrario</b> del que se corrió la vertical, que es donde
    /// tiene sitio: hacia el paño. Y se queda dentro del muro, para que no asome por la cara.
    /// </para>
    /// <para>
    /// Vive aquí, con la geometría, porque la usan los dos: el dibujante y la vista previa. Cada uno
    /// con su copia era la forma segura de que dejaran de coincidir.
    /// </para>
    /// </remarks>
    public static double TangenteALaVertical(
        double xEje, VarillaMuro[] barras, double diamCirculo, double diamVertical, Muro m)
    {
        if (barras.Length == 0)
        {
            return xEje;
        }

        // La vertical de ESTE eje: la más cercana.
        var cerca = barras[0];

        foreach (var b in barras)
        {
            if (Math.Abs(b.X - xEje) < Math.Abs(cerca.X - xEje))
            {
                cerca = b;
            }
        }

        var sep = (diamCirculo + diamVertical) / 2;

        var x = xEje <= cerca.X ? cerca.X - sep : cerca.X + sep;

        return Math.Clamp(x, m.XIzq + (diamCirculo / 2), m.XDer - (diamCirculo / 2));
    }

    /// <summary>
    /// La separación entre los dos dobleces del lindero, ya ajustada a lo que cabe.
    /// </summary>
    public static double SepDeLosDobleces(
        Acomodo a, double yPata, double diamMuro, double recM)
    {
        var rec = recM > 0 ? recM : RecPorOmision;

        var sep = LinderoSepDoblecesFactor * diamMuro;

        if (sep < LinderoSepDoblecesMin)
        {
            sep = LinderoSepDoblecesMin;
        }

        var tope = a.YZapTop - rec - (diamMuro / 2);

        if (yPata + sep > tope)
        {
            sep = tope - yPata;

            var minimo = LinderoSepDoblecesFactorMin * diamMuro;

            if (sep < minimo)
            {
                sep = minimo;
            }
        }

        return sep;
    }

    // ======================================================================
    // La anotación: cotas y rótulo
    // ======================================================================
    //
    // Los cuatro offsets de cota y los tres del rótulo son los de las macros, y todos se miden
    // desde el FONDO DE LA PLANTILLA -el yBase de la macro- o desde el paño izquierdo. No se
    // redondean ni se acomodan a ojo: la lección de las aisladas fue que mover estas distancias
    // despega la cota del elemento que mide.

    /// <summary>La cota horizontal del <b>ancho total</b>, por debajo de la plantilla.</summary>
    /// <remarks><c>yBase − 0.13</c>. Es la de más abajo de las dos filas de cotas horizontales.</remarks>
    public const double CotaAnchoTotal = 0.13;

    /// <summary>Las cotas horizontales <b>parciales</b>, más cerca de la plantilla.</summary>
    /// <remarks>
    /// <c>yBase − 0.075</c>. Son los tramos que parte la contratrabe: en la central, paño
    /// izquierdo → contratrabe, la contratrabe, y contratrabe → paño derecho. La de lindero dibuja
    /// solo las <b>dos primeras</b>, porque la contratrabe llega al paño derecho.
    /// </remarks>
    public const double CotaAnchosParciales = 0.075;

    /// <summary>La cota vertical <b>total</b>, a la izquierda del paño izquierdo.</summary>
    /// <remarks><c>xBase − 0.1445</c>. Del nivel de terreno al fondo de la plantilla.</remarks>
    public const double CotaAlturaTotal = 0.1445;

    /// <summary>Las cotas verticales <b>parciales</b>, en la línea de dentro.</summary>
    /// <remarks>
    /// <c>xBase − 0.0585</c>. Tres: el espesor de la zapata, del lomo al terreno, y los 5 cm de la
    /// plantilla. Esa última lleva el <b>texto adentro</b> en el lindero, que es lo que evita que
    /// AutoCAD la saque con una flecha encima del dibujo.
    /// </remarks>
    public const double CotaAlturasParciales = 0.0585;

    /// <summary>La cota de la pata del muro, por encima de su tramo horizontal.</summary>
    /// <remarks>
    /// La central la pone a 4.5 cm del eje de la pata. El lindero usa 2.2 cm en la varilla de
    /// arriba y <b>45 % de la separación</b> en la de abajo, para que las dos cotas no se toquen.
    /// </remarks>
    public const double CotaDoblezCentral = 0.045;

    /// <summary>La cota de la pata de arriba, en el lindero.</summary>
    public const double CotaDoblezLindero = 0.022;

    /// <summary>Y la de abajo, como fracción de la separación entre dobleces.</summary>
    public const double CotaDoblezLinderoFraccion = 0.45;

    /// <summary>Primer renglón del rótulo, por debajo de la plantilla.</summary>
    public const double RotuloOffset = 0.25;

    /// <summary>Segundo renglón: el «ELEVACION».</summary>
    public const double RotuloSalto1 = 0.34;

    /// <summary>Tercer renglón: f'c, recubrimiento y escala.</summary>
    public const double RotuloSalto2 = 0.42;

    /// <summary>Alto de letra del título.</summary>
    public const double RotuloAltoTitulo = 0.07;

    /// <summary>Alto de letra del «ELEVACION».</summary>
    public const double RotuloAltoElevacion = 0.05;

    /// <summary>Alto de letra del renglón de f'c y escala.</summary>
    public const double RotuloAltoEscala = 0.04;

    /// <summary>Alto de letra del texto de la plantilla.</summary>
    public const double AltoTextoPlantilla = 0.02;

    /// <summary>Alto de letra del «Nivel del terreno».</summary>
    public const double AltoTextoNivel = 0.025;

    /// <summary>
    /// Y del renglón <paramref name="renglon"/> del rótulo, contando desde 0.
    /// </summary>
    /// <remarks>
    /// Se miden <b>desde el fondo de la plantilla</b>, que es el <c>yBase</c> de la macro. Si se
    /// midieran desde el fondo de la zapata, el rótulo se metería dentro de la plantilla y taparía
    /// su texto.
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

    /// <summary>
    /// Dónde va el texto «Nivel del terreno»: <b>a la izquierda</b>, encima de su línea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Arranca en el <b>paño izquierdo</b> de la zapata y crece hacia la derecha —se escribe con
    /// anclaje a la izquierda—, que es donde se pidió y donde está en el plano de referencia. Antes
    /// se centraba a <c>xCentro + 0.35 − 0.313</c>, la resta de la macro: eso lo dejaba encima del
    /// muro, y en una zapata angosta el renglón acababa tapando el arranque del muro de enrase.
    /// </para>
    /// <para>
    /// La altura no cambia: la misma de la macro, poco más de 3 cm por encima de la línea de
    /// terreno.
    /// </para>
    /// </remarks>
    public static (double X, double Y) PosicionTextoNivel(Acomodo a) =>
        (a.XBase, a.YTerreno + (AltoTextoNivel / 2) + 0.035);

    // ======================================================================
    // Los rellenos: colores y escalas
    // ======================================================================

    /// <summary>Color del sólido de cada pieza del enrase, en modo relleno.</summary>
    public const int EnraseColorPieza = 253;

    /// <summary>Color del sólido de la junta de mortero.</summary>
    public const int EnraseColorJunta = 252;

    /// <summary>Color del sólido de fondo del concreto, en modo relleno.</summary>
    public const int ConcretoColorSolido = 9;

    /// <summary>Color del patrón <c>AR-CONC</c> encima del sólido.</summary>
    public const int ConcretoColorPatron = 251;

    /// <summary>Escala del <c>AR-CONC</c> en la sección rellena.</summary>
    public const double ConcretoEscalaPatron = 0.0003;

    /// <summary>Escala del <c>AR-CONC</c> de la zapata y la plantilla, sin relleno.</summary>
    public const double ConcretoEscalaZapata = 0.0005;

    /// <summary>Escala del <c>AR-CONC</c> del muro de concreto, sin relleno.</summary>
    /// <remarks>
    /// Sí: <b>0.05</b>, cien veces la de la zapata, y es lo que dicen las dos macros. En modo
    /// relleno las dos usan la misma que la zapata.
    /// </remarks>
    public const double ConcretoEscalaMuro = 0.05;

    /// <summary>Escala del patrón <c>EARTH</c> del terreno.</summary>
    public const double TerrenoEscalaPatron = 0.01;

    /// <summary>Transparencia del hatch de terreno, en porcentaje.</summary>
    public const int TerrenoTransparencia = 45;

    /// <summary>Gris del terreno: RGB 135,135,135 en la capa <c>TERRENO_HATCH</c>.</summary>
    public const int TerrenoGris = 135;
}
