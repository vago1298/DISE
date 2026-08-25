namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>corte por un eje</b>: qué se ve y dónde, en el plano vertical del corte.
/// </summary>
/// <remarks>
/// <para>
/// Un corte por un eje es lo que en obra se llama un <b>alzado</b>: se mira únicamente lo que
/// hay sobre ese eje, de frente. En planta se ven los espesores pero no las alturas; aquí es
/// al revés, y las dos cosas juntas son lo que se replantea.
/// </para>
/// <para>
/// Esto es <b>pura aritmética</b> y está aparte del dibujante a propósito, igual que
/// <see cref="EjesPlano"/>: así se puede comprobar contra números sin abrir AutoCAD. Lo que
/// devuelve son rectángulos en el plano del corte, con la coordenada horizontal medida
/// <b>a lo largo del eje del corte</b> y la vertical en cotas del modelo.
/// </para>
/// </remarks>
public static class CorteEnAlzado
{
    /// <summary>Una pieza vista en el corte: un rectángulo y de qué es.</summary>
    /// <param name="X">Borde izquierdo, medido a lo largo del eje del corte.</param>
    /// <param name="Z">Borde inferior, en cota del modelo.</param>
    /// <param name="Ancho">Lo que mide a lo largo del corte.</param>
    /// <param name="Alto">Lo que mide en vertical.</param>
    public sealed record Pieza(
        ClasePlanta Clase, string Etiqueta, string Seccion,
        double X, double Z, double Ancho, double Alto);

    /// <summary>Espesor mínimo con el que se dibuja algo, en metros.</summary>
    private const double Minimo = 0.02;

    /// <summary>
    /// ¿Este elemento entra en la <b>rebanada</b> del corte?
    /// </summary>
    /// <remarks>
    /// <para>
    /// El corte es una rebanada y no un plano de espesor cero, y no por comodidad: en un
    /// modelo real los muros de un eje no están todos exactamente en su ordenada —el eje pasa
    /// por el paño y el muro se modela en su línea media, o un nudo quedó movido un
    /// centímetro—, así que un corte de espesor cero se quedaría <b>vacío</b>.
    /// </para>
    /// <para>
    /// Y se mira el elemento <b>completo</b>, no su centro: una trabe que cruza el eje entra
    /// aunque su centro esté a diez metros, porque en el corte se ve su sección. Filtrando por
    /// el centro desaparecerían justo las trabes que llegan al eje.
    /// </para>
    /// </remarks>
    public static bool Entra(
        ElementoPlanta el, bool enX, double ordenada, double espesorM)
    {
        var medio = Math.Max(espesorM, 0.05) / 2;

        var (min, max) = Extremos(el, enX);

        return max >= ordenada - medio && min <= ordenada + medio;
    }

    /// <summary>
    /// Las <b>piezas</b> que se ven en el corte, ya como rectángulos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada tipo se ve de una forma distinta, y es lo que hace que un corte se entienda:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     La <b>columna</b> —y el castillo— se ve de canto: su ancho es la dimensión que
    ///     cruza el corte y su alto es de nudo a nudo, o sea la altura de entrepiso.
    ///   </item>
    ///   <item>
    ///     La <b>trabe o cadena que corre A LO LARGO</b> del corte se ve entera, con su
    ///     peralte: es la que más dice del alzado.
    ///   </item>
    ///   <item>
    ///     La que lo <b>cruza</b> se ve solo de canto, del ancho de su sección: en el corte
    ///     se está viendo su costado.
    ///   </item>
    ///   <item>
    ///     El <b>muro</b> se ve como el paño que es: de su vértice más bajo al más alto y de
    ///     un extremo a otro a lo largo del corte.
    ///   </item>
    /// </list>
    /// <para>
    /// Las losas no se devuelven como pieza: en un corte se ven como una línea, y esa la pone
    /// el dibujante junto a la cota del nivel.
    /// </para>
    /// </remarks>
    public static List<Pieza> Piezas(
        IReadOnlyList<ElementoPlanta> elementos, bool enX, double ordenada, double espesorM)
    {
        var piezas = new List<Pieza>();

        foreach (var el in elementos)
        {
            if (el.Clase == ClasePlanta.Losa || !Entra(el, enX, ordenada, espesorM))
            {
                continue;
            }

            var p = DeUnElemento(el, enX);

            if (p is not null)
            {
                piezas.Add(p);
            }
        }

        return piezas;
    }

    /// <summary>El rectángulo de un elemento, o nulo si no tiene nada que enseñar.</summary>
    private static Pieza? DeUnElemento(ElementoPlanta el, bool enX)
    {
        // A LO LARGO del corte se mide con la coordenada que NO es la del eje: en un corte
        // por un eje vertical —de los que van en X— lo que se recorre es la Y.
        var (min, max) = ALoLargo(el, enX);

        var zAbajo = Math.Min(el.Z1, el.Z2);
        var zArriba = Math.Max(el.Z1, el.Z2);

        // EL MURO: su paño, de vértice a vértice y de su cota más baja a la más alta.
        if (el.Clase == ClasePlanta.Muro)
        {
            var alto = zArriba - zAbajo;

            return alto > Minimo && max - min > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion, min, zAbajo, max - min, alto)
                : null;
        }

        // LA COLUMNA: de canto y de nudo a nudo. El ancho es lo que cruza el corte.
        if (el.Clase == ClasePlanta.Columna)
        {
            var ancho = el.AnchoM > Minimo ? el.AnchoM : 0.15;
            var alto = zArriba - zAbajo;

            // Una columna de altura nula no es una columna: es un nudo mal leído.
            return alto > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            ((min + max) / 2) - (ancho / 2), zAbajo, ancho, alto)
                : null;
        }

        // LA TRABE, LA CADENA Y LA VIGA: su peralte, siempre. Lo que cambia es el ancho.
        var peralte = el.PeralteM > Minimo ? el.PeralteM : 0.20;
        var largo = max - min;

        // Si corre a lo largo del corte se ve entera; si lo cruza, solo de canto. El
        // criterio es su propio largo: una barra que solo asoma el ancho de su sección está
        // cruzando.
        var deCanto = largo <= (el.AnchoM > Minimo ? el.AnchoM : 0.20) + 0.01;

        if (deCanto)
        {
            var ancho = el.AnchoM > Minimo ? el.AnchoM : 0.20;

            return new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                             ((min + max) / 2) - (ancho / 2), zAbajo - peralte, ancho, peralte);
        }

        // La trabe cuelga DEBAJO de la cota de su eje: en el modelo el eje de la barra es su
        // línea de cálculo, y el peralte va para abajo, que es donde está el concreto.
        return largo > Minimo
            ? new Pieza(el.Clase, el.Etiqueta, el.Seccion, min, zAbajo - peralte, largo, peralte)
            : null;
    }

    /// <summary>Extremos del elemento en la dirección <b>del corte</b>.</summary>
    private static (double Min, double Max) Extremos(ElementoPlanta el, bool enX)
    {
        return Recorrer(el, enX);
    }

    /// <summary>Extremos del elemento <b>a lo largo</b> del corte.</summary>
    private static (double Min, double Max) ALoLargo(ElementoPlanta el, bool enX)
    {
        return Recorrer(el, !enX);
    }

    private static (double Min, double Max) Recorrer(ElementoPlanta el, bool enX)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        void Ver(double v)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        if (el.Vertices.Count > 0)
        {
            foreach (var (x, y) in el.Vertices)
            {
                Ver(enX ? x : y);
            }
        }
        else
        {
            Ver(enX ? el.X1 : el.Y1);
            Ver(enX ? el.X2 : el.Y2);
        }

        return (min, max);
    }
}
