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
    /// <summary>Y de arranque de todo: <c>Y_BLOQUES</c>.</summary>
    public const double YBloques = 2.0;

    /// <summary>Separación entre un elemento y el siguiente: <c>SEP_SECCIONES</c>.</summary>
    public const double SepSecciones = 0.6;

    /// <summary>Margen que se abre antes de una columna: <c>MARGEN_COL</c>.</summary>
    public const double MargenCol = 0.4;

    /// <summary>Separación entre las dos caras de una columna: <c>SEP_CARAS</c>.</summary>
    public const double SepCaras = 0.3;

    /// <summary>Separación entre la sección y su alzado: <c>SEP_SEC_ALZ</c>.</summary>
    public const double SepSecAlz = 0.2;

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
    public static Puesto Colocar(
        double x0, bool vertical, double anchoSeccion, double topeSeccion,
        double largo, bool dosCaras)
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

            var y1 = topeSeccion + SepSecAlz;

            return new Puesto
            {
                XSeccion = xSec,
                XAlzado = xAlz,
                YAlzado = y1,

                // La segunda cara, a SEP_CARAS del paño superior de la primera.
                YAlzado2 = dosCaras ? y1 + largo + SepCaras : null,

                // OJO: se avanza desde x0, no desde xSec. El MARGEN_COL se abre y no
                // se vuelve a contar, tal como está en el VBA:
                //     totalWidth = blockWidth + alzadoWidth
                //     x0 = x0 + totalWidth + SEP_SECCIONES
                XSiguiente = xSec + anchoSeccion + AnchoCotasVertical + SepSecciones
            };
        }

        // Trabe: la sección a la izquierda y el alzado a su derecha.
        return new Puesto
        {
            XSeccion = x0,
            XAlzado = x0 + anchoSeccion + SepSecAlz,
            YAlzado = YBloques,
            YAlzado2 = null,
            XSiguiente = x0 + anchoSeccion + SepSecAlz + largo + HookDimOff2 + SepSecciones
        };
    }
}
