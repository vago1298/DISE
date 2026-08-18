namespace CadLink.Cad;

/// <summary>
/// Colocación de cada elemento en la fila de alzados: dónde va la sección, dónde el
/// alzado, y cuánto avanza la fila.
/// </summary>
/// <remarks>
/// <para>
/// Port de la aritmética del bucle principal de <c>Alzados_Trabes_Desde_Excel</c>.
/// Va en su propia clase, sin nada de COM, por dos razones: para poder comprobarla
/// número a número contra el VBA, y porque el usuario dijo <i>«no respetas la
/// separación entre elementos»</i> y eso son cinco constantes y dos fórmulas que
/// tienen que estar exactas, no aproximadas.
/// </para>
/// <para>
/// La colocación NO es la misma en los dos tipos, y ahí estaba el error:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Trabe / contratrabe.</b> La sección va a la izquierda y el alzado a su
///     derecha, separados <c>SEP_SEC_ALZ</c>. Los dos apoyados en la misma Y.
///   </item>
///   <item>
///     <b>Columna / dado.</b> El alzado va <b>encima</b> de la sección, no al lado:
///     arranca en el paño superior de la sección más <c>SEP_SEC_ALZ</c>, y comparte
///     con ella la misma banda de X. Por eso el ancho que consume el alzado de una
///     columna son solo sus cotas y su rótulo (<c>alzadoWidth</c>), no su longitud.
///   </item>
/// </list>
/// </remarks>
public static class AlzadoLayout
{
    /// <summary>
    /// Aire que se deja entre las secciones y la fila de alzados: <b>1 m</b>.
    /// </summary>
    /// <remarks>
    /// En la macro este valor es el <c>Y_BLOQUES</c> y vale 2, pero como una <b>cota
    /// absoluta</b>: todo se colocaba en Y=2 pasara lo que pasara. Funcionaba porque
    /// las secciones se dibujaban en Y=0 y ninguna medía más de 2 m de alto en el
    /// papel.
    /// <para>
    /// Aquí es una separación <b>relativa</b> a la sección más alta, y por eso se
    /// puede apretar: con 2 m el hueco entre las dos filas quedaba mayor que las
    /// propias secciones, y el plano salía con una banda vacía en medio. Con 1 m las
    /// dos filas se leen como partes del mismo juego.
    /// </para>
    /// </remarks>
    public const double AireSobreSecciones = 1.0;

    /// <summary>Y de arranque de la fila de alzados cuando no hay secciones medidas.</summary>
    /// <remarks>
    /// Solo se usa como respaldo: el camino normal es <see cref="YArranque"/>.
    /// </remarks>
    public const double YBloques = AireSobreSecciones;

    /// <summary>
    /// Y donde arranca la fila de alzados: <b>1 m por encima de la sección más
    /// alta</b> de las que se dibujaron al principio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué no vale la constante de la macro.</b> Allí los alzados se ponen
    /// siempre en Y=2. Con secciones de trabe de 60 cm eso deja 1.40 m de aire y se
    /// ve bien, pero en cuanto entra un elemento alto —una columna de 3 m dibujada a
    /// escala 1:10 mide 30 cm, pero un muro o una contratrabe de 2.50 m ya no— la
    /// sección <b>invade la fila de alzados</b> y el plano queda encimado. El usuario
    /// lo pidió explícitamente: los bloques y los alzados por encima de la sección más
    /// alta, no a una cota fija desde el origen.
    /// </para>
    /// <para>
    /// Las secciones se dibujan apoyadas en <c>Y=0</c>, así que el paño superior de
    /// la más alta es su propio alto y basta con sumarle el aire.
    /// </para>
    /// <para>
    /// <b>El aire es SIEMPRE 1 m</b>, no «1 m como mínimo». Con una trabe de 60 cm
    /// dibujada a escala 1:100 la fila queda en Y=1.6, no en Y=1. Y tiene una
    /// consecuencia que conviene tener presente: un plano acomodado con una versión
    /// anterior verá la fila de alzados desplazada la próxima vez que se generen.
    /// </para>
    /// </remarks>
    /// <param name="altoMaximoSeccion">
    /// Alto de la sección más alta, en <b>metros de dibujo</b>, ya multiplicado por la
    /// escala. Cero si no hay ninguna.
    /// </param>
    public static double YArranque(double altoMaximoSeccion)
    {
        // Sin secciones no hay nada que esquivar, así que se cae a la cota de la
        // macro. Es el único caso en que el resultado no es «alto + aire».
        if (altoMaximoSeccion <= 0)
        {
            return YBloques;
        }

        return altoMaximoSeccion + AireSobreSecciones;
    }

    /// <summary>Separación entre un elemento y el siguiente: <c>SEP_SECCIONES</c>.</summary>
    public const double SepSecciones = 0.6;

    /// <summary>Margen que se abre antes de una columna: <c>MARGEN_COL</c>.</summary>
    public const double MargenCol = 0.4;

    /// <summary>Separación entre las dos caras de una columna: <c>SEP_CARAS</c>.</summary>
    public const double SepCaras = 0.3;

    /// <summary>Separación entre la sección y su alzado: <c>SEP_SEC_ALZ</c>.</summary>
    public const double SepSecAlz = 0.2;

    /// <summary>
    /// Aire extra que se abre <b>debajo del alzado vertical</b> para su rótulo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El rótulo del alzado va siempre <b>debajo del bloque insertado</b>, y en el
    /// alzado vertical eso choca con la sección: el bloque arranca en
    /// <c>topeSeccion + SepSecAlz</c>, o sea a 20 cm del paño superior de la sección, y
    /// el rótulo necesita más que eso.
    /// </para>
    /// <para>
    /// La cuenta, con el rótulo más largo que puede salir —elemento, ID, tres lechos,
    /// estribo, recubrimiento, f'c y escala, nueve renglones de 2.5 mm con el
    /// interlineado de AutoCAD— son 35.8 cm de texto, más los 5 cm de
    /// <c>ROTULO_GAP</c> y 5 cm de holgura contra la sección: 45.8 cm. Ya hay 20, así
    /// que faltan 26. Se redondean al alza.
    /// </para>
    /// <para>
    /// En el alzado <b>horizontal</b> no hace falta ninguna constante nueva: debajo del
    /// bloque están sus cotas de estribos, las etiquetas de zona, el título y la
    /// escala, y por debajo de todo eso queda el metro de
    /// <see cref="AireSobreSecciones"/>, que da de sobra.
    /// </para>
    /// </remarks>
    public const double AireRotuloAlzado = 0.30;

    /// <summary>
    /// Y de la <b>segunda cara</b> de una columna rectangular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La segunda cara va encima del paño superior de la primera, separada
    /// <see cref="SepCaras"/>… más el aire del rótulo, porque la segunda cara también
    /// lleva el suyo debajo y con solo los 30 cm de <c>SEP_CARAS</c> el rótulo de arriba
    /// caía <b>dentro</b> del alzado de abajo.
    /// </para>
    /// <para>
    /// Está como función y no como constante porque hasta ahora este cálculo estaba
    /// <b>escrito dos veces</b>: aquí, en <c>YAlzado2</c>, y otra vez a mano en
    /// <c>AlzadoDrawer.DibujarVertical</c> con un <c>0.3</c> literal. Coincidían por
    /// suerte, y al abrir el hueco del rótulo habrían dejado de coincidir.
    /// </para>
    /// </remarks>
    /// <param name="yPrimera">Y de inserción de la primera cara.</param>
    /// <param name="largo">Longitud del elemento, en metros de dibujo.</param>
    public static double YSegundaCara(double yPrimera, double largo) =>
        yPrimera + largo + SepCaras + AireRotuloAlzado;

    /// <summary>Aire a la derecha del alzado horizontal: <c>HOOK_DIM_OFF_2</c>.</summary>
    public const double HookDimOff2 = 0.14;

    /// <summary>Ancho de las cotas y el rótulo del alzado vertical.</summary>
    /// <remarks><c>DIM_OFF_3 + ROTULO_OFF_COL + 0.1</c> = 0.24 + 0.09 + 0.1.</remarks>
    public const double AnchoCotasVertical = 0.24 + 0.09 + 0.1;

    /// <summary>Ancho que se supone a la sección cuando su bloque no existe.</summary>
    /// <remarks>
    /// La macro usa <c>0.8</c> en el <c>Else</c> de <c>If Not br Is Nothing</c>. Sin
    /// esto, un ID sin bloque dejaría a los elementos siguientes encimados.
    /// </remarks>
    public const double AnchoSeccionSupuesto = 0.8;

    /// <summary>Alto supuesto de la sección cuando su bloque no existe.</summary>
    public const double AltoSeccionSupuesto = 0.4;

    /// <summary>
    /// X del borde izquierdo de la sección. Hace falta <b>antes</b> de medirla.
    /// </summary>
    /// <remarks>
    /// Está aparte porque el orden obliga: para colocar el elemento hay que saber
    /// cuánto mide su bloque de sección, y para medirlo hay que insertarlo, y para
    /// insertarlo hay que saber su X. Con esta función el <c>MARGEN_COL</c> de la
    /// columna se calcula en <b>un solo sitio</b> en lugar de escribirlo dos veces y
    /// arriesgarse a que una de las dos se quede sin actualizar.
    /// </remarks>
    public static double XSeccion(double x0, bool vertical) =>
        vertical ? x0 + MargenCol : x0;

    /// <summary>Dónde va cada cosa de un elemento, y cuánto avanza la fila.</summary>
    public sealed class Puesto
    {
        /// <summary>X del borde izquierdo del bloque de la sección.</summary>
        public double XSeccion { get; init; }

        /// <summary>X de inserción del alzado.</summary>
        public double XAlzado { get; init; }

        /// <summary>Y de inserción del alzado.</summary>
        public double YAlzado { get; init; }

        /// <summary>Y de inserción de la segunda cara, o <c>null</c> si no hay.</summary>
        public double? YAlzado2 { get; init; }

        /// <summary>X desde donde arranca el elemento siguiente.</summary>
        public double XSiguiente { get; init; }
    }

    /// <summary>
    /// Coloca un elemento y devuelve dónde arranca el siguiente.
    /// </summary>
    /// <param name="x0">X acumulada de la fila.</param>
    /// <param name="vertical">Columna o dado.</param>
    /// <param name="anchoSeccion">Ancho real del bloque de la sección, ya medido.</param>
    /// <param name="topeSeccion">Y del paño superior del bloque de la sección.</param>
    /// <param name="largo">Longitud del elemento, en metros de dibujo.</param>
    /// <param name="dosCaras">La columna es rectangular y lleva dos alzados.</param>
    /// <param name="yArranque">
    /// Y de la fila, la que devuelve <see cref="YArranque"/>. Es un parámetro y no la
    /// constante porque depende de las secciones que se hayan dibujado, y eso solo lo
    /// sabe quien llama.
    /// </param>
    public static Puesto Colocar(
        double x0, bool vertical, double anchoSeccion, double topeSeccion,
        double largo, bool dosCaras, double yArranque)
    {
        if (vertical)
        {
            // El margen se abre ANTES de la sección, así que la sección de una
            // columna no arranca en x0 sino en x0 + MARGEN_COL. Faltaba, y era lo
            // que dejaba las columnas pegadas al elemento anterior.
            var xSec = x0 + MargenCol;

            // El alzado va ENCIMA, y su X de inserción es el borde DERECHO de la
            // sección: el alzado vertical se dibuja hacia la izquierda de su punto
            // de inserción.
            var xAlz = xSec + anchoSeccion;

            // El aire del rótulo se suma AQUÍ y no en SepSecAlz porque SepSecAlz es una
            // constante de la macro que se usa también en la trabe, donde el rótulo no
            // estorba. Ver AireRotuloAlzado.
            var y1 = topeSeccion + SepSecAlz + AireRotuloAlzado;

            return new Puesto
            {
                XSeccion = xSec,
                XAlzado = xAlz,
                YAlzado = y1,

                // La segunda cara, encima del paño superior de la primera.
                YAlzado2 = dosCaras ? YSegundaCara(y1, largo) : null,

                // OJO: se avanza desde x0, no desde xSec. El MARGEN_COL se abre y no
                // se vuelve a contar, tal como está en el VBA:
                //     totalWidth = blockWidth + alzadoWidth
                //     x0 = x0 + totalWidth + SEP_SECCIONES
                XSiguiente = xSec + anchoSeccion + AnchoCotasVertical + SepSecciones
            };
        }

        // Trabe: la sección a la izquierda y el alzado a su derecha, los dos apoyados
        // en la misma Y de la fila.
        return new Puesto
        {
            XSeccion = x0,
            XAlzado = x0 + anchoSeccion + SepSecAlz,
            YAlzado = yArranque,
            YAlzado2 = null,
            XSiguiente = x0 + anchoSeccion + SepSecAlz + largo + HookDimOff2 + SepSecciones
        };
    }
}
