namespace CadLink.Cad;

/// <summary>
/// Dónde va cada zapata aislada de la corrida y dónde va cada cosa dentro de ella.
/// </summary>
/// <remarks>
/// <para>
/// Aritmética pura, sin nada de COM, igual que <see cref="AlzadoLayout"/>. Aquí viven
/// las dos cosas que estaban saliendo mal:
/// </para>
/// <list type="number">
///   <item>
///     <b>Las secciones se encimaban.</b> Cada zapata se colocaba respecto del
///     <i>centro</i>, así que dos zapatas seguidas caían una sobre otra y los dos
///     títulos se leían uno encima del otro. La macro no hace eso: ancla la zapata
///     por su <b>paño izquierdo</b> y separa una de otra
///     <see cref="SepSecciones"/> = <c>-0.8</c>, corriéndose <b>hacia la izquierda</b>.
///     Ver <see cref="XSiguiente"/>.
///   </item>
///   <item>
///     <b>Los rótulos.</b> Sus tres alturas y sus tres separaciones son constantes de
///     la macro, no valores a criterio: 0.32, 0.41 y 0.49 por debajo del desplante.
///     Ver <see cref="ZapataAisladaRotulos"/> para el texto y
///     <see cref="Puesta"/> para el punto donde va cada uno.
///   </item>
/// </list>
/// <para>
/// El origen es el de la macro de lindero: la esquina inferior izquierda de la
/// primera zapata en <c>(-3, -8)</c>. <b>No</b> es el origen del dibujo, y esto es
/// deliberado: así el alzado de las zapatas no cae encima de las secciones y los
/// alzados de trabes y columnas, que se dibujan a partir del origen hacia arriba y a
/// la derecha.
/// </para>
/// </remarks>
public static class ZapataAisladaLayout
{
    /// <summary>De centímetros de la hoja a metros de dibujo: <c>SCALE_ELEVATION</c>.</summary>
    public const double EscalaElevacion = 0.01;

    // ---------------------------------------------------------------- origen ----

    /// <summary>X del paño izquierdo de la primera zapata: <c>ELEVACION_X_BASE</c>.</summary>
    public const double XBase = -3.0;

    /// <summary>Y del desplante de la zapata: <c>ELEVACION_Y_BASE</c>.</summary>
    public const double YBase = -8.0;

    /// <summary>
    /// Separación entre una zapata y la siguiente: <c>SEPARACION_SECCIONES</c>.
    /// </summary>
    /// <remarks>
    /// Se anota <b>negativa</b> porque las zapatas se acomodan hacia la izquierda, y
    /// así el signo recuerda el sentido. Para medir se usa su valor absoluto, igual
    /// que la macro: <c>xBase = xBase - Abs(SEPARACION_SECCIONES) - anchoZapata</c>.
    /// </remarks>
    public const double SepSecciones = -0.8;

    /// <summary>El aire entre dos zapatas, ya en positivo.</summary>
    public static double SepSeccionesM => Math.Abs(SepSecciones);

    // ------------------------------------------------------------- elevación ----

    /// <summary>Altura con la que se representa la columna que desplanta.</summary>
    public const double AlturaColumnaM = 0.8;

    /// <summary>
    /// Fracción de la columna que se dibuja antes de la línea de rotura:
    /// <c>COLUMNA_FRACCION_CORTE</c>.
    /// </summary>
    public const double ColumnaFraccionCorte = 8.0 / 9.0;

    /// <summary>Espesor de la plantilla de concreto simple.</summary>
    public const double PlantillaEspesorM = 0.05;

    /// <summary>Cuánto sobresale la línea del terreno a cada lado de la zapata.</summary>
    public const double TerrenoVueloM = 0.2;

    /// <summary>Cuánto se levanta el texto del terreno sobre su línea.</summary>
    public const double TerrenoTextoDy = 0.03;

    /// <summary>Alto mínimo del dado para que valga la pena rotularlo.</summary>
    public const double DadoAltoMinimoRotulo = 0.02;

    // ----------------------------------------------------- rótulos y cotas ------

    public const double RotuloTituloOffset = 0.32;

    public const double RotuloSubtituloOffset = 0.41;

    public const double RotuloEscalaOffset = 0.49;

    /// <summary>Primera línea de cotas verticales, la de los tramos.</summary>
    public const double CotaOffsetVert1 = 0.08;

    /// <summary>Segunda línea de cotas verticales, la del total.</summary>
    public const double CotaOffsetVert2 = 0.16;

    /// <summary>Cadena de cotas horizontales: zapata, dado, zapata.</summary>
    public const double CotaOffsetCadena = 0.14;

    /// <summary>Cota horizontal del ancho total.</summary>
    public const double CotaOffsetTotal = 0.22;

    /// <summary>Separación de las cotas de los dobleces de los ganchos del dado.</summary>
    public const double CotaGanchoOffset = 0.06;

    // ------------------------------------------------------------ en planta ----

    public const double PlantaCotaOffset = 0.12;

    public const double PlantaCotaOffsetDado = 0.1;

    public const double PlantaTituloOffset = 0.24;

    public const double PlantaEscalaOffset = 0.33;

    /// <summary>
    /// Aire entre el fondo del alzado y el tope de la planta, en la zapata central:
    /// <c>PLANTA_OFFSET_Y</c> con <c>PLANTA_OFFSET_DESDE_TOPE = True</c>.
    /// </summary>
    public const double PlantaOffsetCentral = -3.0;

    /// <summary>Tope al que se baja la planta en la de lindero: <c>PLANTA_Y_BASE</c>.</summary>
    public const double PlantaTopeLindero = -15.0;

    /// <summary>Aire mínimo entre el rótulo del alzado y la cota más alta de la planta.</summary>
    public const double PlantaSeparacionMinLindero = 1.2;

    // -------------------------------------------------- rótulos de parrilla ----

    /// <summary>
    /// Corrimiento del rótulo de la parrilla <b>inferior</b> respecto del paño
    /// izquierdo.
    /// </summary>
    /// <remarks>
    /// Se deja escrito como la suma que trae el VBA
    /// (<c>-0.18 + 0.272 - 0.11 + DESPLAZAMIENTO_PARRILLA_INF_CENTRAR</c>) en lugar
    /// del resultado, para que se pueda seguir contra la macro sin tener que
    /// deshacer la cuenta.
    /// </remarks>
    public const double RotuloParrillaInfDx = -0.18 + 0.272 - 0.11 + 0.2;

    /// <summary>Altura del rótulo de la parrilla inferior sobre el desplante.</summary>
    public const double RotuloParrillaInfDy = 0.1 + 0.4164 - 0.16;

    /// <summary>
    /// Rótulo de la parrilla <b>superior</b> en la zapata central: sale por la
    /// derecha, medido desde el paño derecho.
    /// </summary>
    public const double RotuloParrillaSupDxCentral = 0.16 - 0.4302;

    /// <summary>Altura de ese rótulo sobre el lomo de la zapata.</summary>
    public const double RotuloParrillaSupDyCentral = 0.02 + 0.2908 - 0.16;

    /// <summary>
    /// En la de lindero el rótulo de la parrilla superior va <b>centrado</b> sobre la
    /// zapata, a esta altura del lomo: <c>LINDERO_ROTULO_SUP_DY</c>.
    /// </summary>
    /// <remarks>
    /// Va centrado y no a la derecha porque en el lindero ese costado lo ocupa el
    /// dado, y el rótulo de la parrilla inferior ya está del lado izquierdo.
    /// </remarks>
    public const double RotuloParrillaSupDyLindero = 0.23;

    /// <summary>Punta del leader de la barra, como fracción del ancho.</summary>
    public const double RotuloSupFxBarraLindero = 0.32;

    /// <summary>Punta del leader de los círculos, como fracción del ancho.</summary>
    public const double RotuloSupFxCircLindero = 0.66;

    /// <summary>Separación de los dos textos respecto del centro, en el lindero.</summary>
    public const double RotuloSupGapXLindero = 0.03;

    /// <summary>
    /// Separación horizontal del rótulo del dado y de la columna respecto de su paño,
    /// en la de lindero: <c>LINDERO_ROTULO_ELEM_DX</c>.
    /// </summary>
    public const double RotuloElementoDxLindero = 0.3;

    // --------------------------------------------------------------- la fila ----

    /// <summary>X del paño izquierdo de la primera zapata de la corrida.</summary>
    public static double XPrimera(double? xInicial = null) => xInicial ?? XBase;

    /// <summary>
    /// X del paño izquierdo de la zapata siguiente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la línea del VBA:
    /// </para>
    /// <code>
    /// If dibujadas &gt; 0 Then
    ///     xBase = xBase - Abs(SEPARACION_SECCIONES) - anchoZapata
    /// End If
    /// </code>
    /// <para>
    /// Se resta el ancho de la <b>que viene</b>, no el de la anterior, porque la
    /// zapata crece hacia la derecha desde su paño izquierdo: para que quede a 0.8 de
    /// la anterior hay que dejarle su propio ancho de sitio. Restar el ancho
    /// equivocado es lo que dejaba la zapata ancha montada sobre la angosta.
    /// </para>
    /// </remarks>
    /// <param name="xBaseAnterior">Paño izquierdo de la zapata ya colocada.</param>
    /// <param name="anchoSiguienteM">Ancho de la que se va a colocar, en metros.</param>
    public static double XSiguiente(double xBaseAnterior, double anchoSiguienteM) =>
        xBaseAnterior - SepSeccionesM - Math.Max(anchoSiguienteM, 0);

    /// <summary>
    /// Paño izquierdo de cada zapata de la corrida, en el orden en que se dibujan.
    /// </summary>
    public static List<double> Fila(IReadOnlyList<double> anchosM, double? xInicial = null)
    {
        var xs = new List<double>(anchosM.Count);
        var x = XPrimera(xInicial);

        for (var i = 0; i < anchosM.Count; i++)
        {
            if (i > 0)
            {
                x = XSiguiente(x, anchosM[i]);
            }

            xs.Add(x);
        }

        return xs;
    }

    /// <summary>Todo lo que hace falta para dibujar una zapata ya colocada.</summary>
    public sealed class Puesta
    {
        public required TipoZapata Tipo { get; init; }

        // ---- zapata ----

        /// <summary>Paño izquierdo. Es la X con la que se coloca la zapata.</summary>
        public double XIzq { get; init; }

        public double XDer { get; init; }

        /// <summary>Eje de la zapata. Solo se usa para centrar textos y cotas.</summary>
        public double XCentro { get; init; }

        public double YDesplante { get; init; }

        /// <summary>Lomo de la zapata.</summary>
        public double YLomo { get; init; }

        /// <summary>Fondo de la plantilla de concreto simple.</summary>
        public double YPlantilla { get; init; }

        public double YTerreno { get; init; }

        // ---- dado y columna ----

        public double XDadoIzq { get; init; }

        public double XDadoDer { get; init; }

        /// <summary>Tope del dado, que es donde arranca la columna.</summary>
        public double YDadoTope { get; init; }

        public double XColIzq { get; init; }

        public double XColDer { get; init; }

        /// <summary>Tope de la columna, ya con su fracción de corte.</summary>
        public double YColTope { get; init; }

        // ---- rótulos del alzado ----

        /// <summary>Punto centrado del título; el texto va centrado en X.</summary>
        public (double X, double Y) Titulo { get; init; }

        public (double X, double Y) Subtitulo { get; init; }

        public (double X, double Y) Escala { get; init; }

        /// <summary>Arranque del texto del nivel del terreno, alineado a la izquierda.</summary>
        public (double X, double Y) TextoTerreno { get; init; }

        /// <summary>
        /// Rótulo del dado: a dónde apunta el leader, dónde va el texto y de qué lado
        /// crece. <c>null</c> si el dado no tiene alto suficiente para rotularlo.
        /// </summary>
        public RotuloConLeader? RotuloDado { get; init; }

        /// <summary>Rótulo de la columna. <c>null</c> si no es de concreto.</summary>
        public RotuloConLeader? RotuloColumna { get; init; }

        /// <summary>Punto del rótulo de la parrilla inferior.</summary>
        public (double X, double Y) RotuloParrillaInf { get; init; }

        /// <summary>
        /// Punto del rótulo de la parrilla superior. <c>null</c> si la zapata no lleva
        /// doble parrilla.
        /// </summary>
        public (double X, double Y)? RotuloParrillaSup { get; init; }

        // ---- cotas ----

        public double XCota1 { get; init; }

        public double XCota2 { get; init; }

        public double YCotaCadena { get; init; }

        public double YCotaTotal { get; init; }

        // ---- planta ----

        /// <summary>Borde inferior de la vista en planta.</summary>
        public double YPlanta { get; init; }

        public double LargoPlanta { get; init; }

        public (double X, double Y) TituloPlanta { get; init; }

        public (double X, double Y) EscalaPlanta { get; init; }

        /// <summary>Ancho de la zapata, para no volver a restar paños.</summary>
        public double Ancho => XDer - XIzq;
    }

    /// <summary>Un rótulo con leader: la punta, el texto y hacia dónde crece.</summary>
    /// <param name="Punta">A qué paño apunta la flecha.</param>
    /// <param name="Texto">Dónde se ancla el MText.</param>
    /// <param name="HaciaIzquierda">
    /// El texto queda a la <b>izquierda</b> de la punta. Es lo que pasa en el lindero,
    /// donde el dado está pegado al paño derecho y no hay sitio de ese lado.
    /// </param>
    public readonly record struct RotuloConLeader(
        (double X, double Y) Punta, (double X, double Y) Texto, bool HaciaIzquierda);

    /// <summary>
    /// Coloca una zapata con su paño izquierdo en <paramref name="xIzq"/> y resuelve
    /// todos sus puntos.
    /// </summary>
    /// <remarks>
    /// La zapata se ancla por el paño izquierdo, <b>nunca</b> por el centro. Si se
    /// ancla por el centro, dos zapatas de anchos distintos no respetan el aire de
    /// 0.8 entre sí y los rótulos se solapan, que es exactamente lo que se veía.
    /// </remarks>
    public static Puesta Colocar(ZapataAisladaCad z, double xIzq, double yDesplante = YBase)
    {
        var ancho = z.AnchoM;
        var xDer = xIzq + ancho;
        var xCentro = xIzq + (ancho / 2.0);

        var yLomo = yDesplante + z.EspesorM;
        var yTerreno = yDesplante + z.ProfundidadM;
        var yDadoTope = yDesplante + z.AlturaDadoM;
        var yColTope = yDadoTope + (AlturaColumnaM * ColumnaFraccionCorte);

        var wDado = z.Dado.AnchoCm * EscalaElevacion;
        var wCol = z.Columna.AnchoCm * EscalaElevacion;

        double xDadoIzq, xDadoDer, xColIzq, xColDer;

        if (z.Tipo == TipoZapata.Lindero)
        {
            // El paño derecho ES el lindero: el dado y la columna van pegados a él.
            xDadoDer = xDer;
            xDadoIzq = Math.Max(xDer - wDado, xIzq);
            xColDer = xDer;
            xColIzq = Math.Max(xDer - wCol, xIzq);
        }
        else
        {
            xDadoIzq = xCentro - (wDado / 2.0);
            xDadoDer = xCentro + (wDado / 2.0);
            xColIzq = xCentro - (wCol / 2.0);
            xColDer = xCentro + (wCol / 2.0);
        }

        var esLindero = z.Tipo == TipoZapata.Lindero;

        // Rótulo del dado: en el lindero sale por la izquierda, porque el dado está
        // pegado al paño derecho y de ese lado no queda zapata donde apoyarlo.
        RotuloConLeader? rotDado = null;
        if (yDadoTope > yLomo + DadoAltoMinimoRotulo)
        {
            var y = (yLomo + yDadoTope) / 2.0;

            rotDado = esLindero
                ? new RotuloConLeader((xDadoIzq, y), (xDadoIzq - RotuloElementoDxLindero, y), true)
                : new RotuloConLeader((xDadoDer, y), ((xDadoDer + xDer) / 2.0, y), false);
        }

        RotuloConLeader? rotCol = null;
        if (z.ColumnaDeConcreto)
        {
            var y = yDadoTope + (AlturaColumnaM * ColumnaFraccionCorte / 2.0);

            rotCol = esLindero
                ? new RotuloConLeader((xColIzq, y), (xColIzq - RotuloElementoDxLindero, y), true)
                : new RotuloConLeader((xColDer, y), ((xColDer + xDer) / 2.0, y), false);
        }

        (double, double)? rotSup = null;
        if (z.DobleParrilla)
        {
            rotSup = esLindero
                ? (xCentro, yLomo + RotuloParrillaSupDyLindero)
                : (xDer + RotuloParrillaSupDxCentral, yLomo + RotuloParrillaSupDyCentral);
        }

        var largoPlanta = z.LargoEfectivoM;
        var yPlanta = YPlanta(z.Tipo, yDesplante, largoPlanta);
        var xCentroPlanta = xIzq + (ancho / 2.0);

        return new Puesta
        {
            Tipo = z.Tipo,

            XIzq = xIzq,
            XDer = xDer,
            XCentro = xCentro,
            YDesplante = yDesplante,
            YLomo = yLomo,
            YPlantilla = yDesplante - PlantillaEspesorM,
            YTerreno = yTerreno,

            XDadoIzq = xDadoIzq,
            XDadoDer = xDadoDer,
            YDadoTope = yDadoTope,
            XColIzq = xColIzq,
            XColDer = xColDer,
            YColTope = yColTope,

            Titulo = (xCentro, yDesplante - RotuloTituloOffset),
            Subtitulo = (xCentro, yDesplante - RotuloSubtituloOffset),
            Escala = (xCentro, yDesplante - RotuloEscalaOffset),
            TextoTerreno = (xIzq, yTerreno + TerrenoTextoDy),

            RotuloDado = rotDado,
            RotuloColumna = rotCol,
            RotuloParrillaInf = (xIzq + RotuloParrillaInfDx, yDesplante + RotuloParrillaInfDy),
            RotuloParrillaSup = rotSup,

            XCota1 = xIzq - CotaOffsetVert1,
            XCota2 = xIzq - CotaOffsetVert2,
            YCotaCadena = yDesplante - CotaOffsetCadena,
            YCotaTotal = yDesplante - CotaOffsetTotal,

            YPlanta = yPlanta,
            LargoPlanta = largoPlanta,
            TituloPlanta = (xCentroPlanta, yPlanta - PlantaTituloOffset),
            EscalaPlanta = (xCentroPlanta, yPlanta - PlantaEscalaOffset)
        };
    }

    /// <summary>
    /// Borde inferior de la vista en planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos macros la bajan distinto y se conservan las dos reglas, porque las dos
    /// resuelven el mismo problema con distinta prioridad:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Central.</b> La planta cuelga a 3 m del fondo del alzado, sea larga o
    ///     corta: <c>tope = (desplante - 0.49) - 3</c> y de ahí se le resta el largo.
    ///   </item>
    ///   <item>
    ///     <b>Lindero.</b> Se calcula el sitio que hace falta para que nunca se encime
    ///     —rótulo, aire de 1.2 y la cota del dado— y además se obliga a bajar hasta
    ///     <c>-15</c> como tope; se toma la más baja de las dos.
    ///   </item>
    /// </list>
    /// </remarks>
    public static double YPlanta(TipoZapata tipo, double yDesplante, double largoM)
    {
        var fondoAlzado = yDesplante - RotuloEscalaOffset;

        if (tipo == TipoZapata.Central)
        {
            return fondoAlzado + PlantaOffsetCentral - largoM;
        }

        var necesaria = fondoAlzado - PlantaSeparacionMinLindero - largoM - PlantaCotaOffsetDado;
        return Math.Min(necesaria, PlantaTopeLindero);
    }
}
