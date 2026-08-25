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
    /// <param name="Tipo">
    /// El tipo que trae el elemento —el que sale de las <b>notas</b> de su propiedad—, que es
    /// lo que decide la capa: CADENA DE CERRAMIENTO y CADENA DE DESPLANTE van a las capas de
    /// las cadenas, y TRABE a la de las trabes.
    /// </param>
    /// <param name="Cortada">
    /// <c>true</c> si la pieza la <b>corta</b> el plano del corte; <c>false</c> si solo se
    /// <b>ve al fondo</b>. En un corte de verdad se dibujan las dos cosas: lo que se corta y
    /// lo que se ve detrás.
    /// </param>
    public sealed record Pieza(
        ClasePlanta Clase, string Etiqueta, string Seccion,
        double X, double Z, double Ancho, double Alto,
        string Tipo = "", bool Cortada = true);

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
        IReadOnlyList<ElementoPlanta> elementos, bool enX, double ordenada, double espesorM,
        bool verElFondo = true)
    {
        var piezas = new List<Pieza>();

        foreach (var el in elementos)
        {
            // ==========================================================================
            //  LO QUE SE CORTA Y LO QUE SE VE AL FONDO
            // ==========================================================================
            //  Un corte no es solo la rebanada: es una VISTA. Se corta por el eje y se
            //  dibuja además todo lo que queda DETRÁS, que es lo que le da el fondo al
            //  alzado —los muros del otro extremo, las losas que siguen, las columnas de
            //  atrás—. Con solo la rebanada, el corte queda flotando: dos columnas y una
            //  cadena en el aire, que es lo que salía.
            //
            //  Se distinguen para poder dibujarlas distinto: lo cortado con su línea normal
            //  y el fondo más flojo, como en cualquier plano de obra.
            var cortada = Entra(el, enX, ordenada, espesorM);

            if (!cortada && !(verElFondo && AlFondo(el, enX, ordenada, espesorM)))
            {
                continue;
            }

            var p = DeUnElemento(el, enX, elementos);

            if (p is not null)
            {
                piezas.Add(p with { Cortada = cortada });
            }
        }

        return piezas;
    }

    /// <summary>
    /// ¿Este elemento queda <b>detrás</b> del plano del corte, o sea a la vista?
    /// </summary>
    /// <remarks>
    /// <para>
    /// «Detrás» es hacia las coordenadas <b>mayores</b> que la del corte: se mira en el sentido
    /// en que crece el eje, que es el mismo criterio con el que la vista extruida se pone de
    /// frente al corte. Así lo que se ve en la pantalla y lo que se dibuja coinciden.
    /// </para>
    /// <para>
    /// Y solo entra lo que está <b>del todo</b> detrás: lo que cruza la rebanada ya se dibujó
    /// como cortado, y meterlo dos veces dejaría dos rectángulos encima del otro.
    /// </para>
    /// </remarks>
    public static bool AlFondo(
        ElementoPlanta el, bool enX, double ordenada, double espesorM)
    {
        var medio = Math.Max(espesorM, 0.05) / 2;
        var (min, _) = Extremos(el, enX);

        return min > ordenada + medio;
    }

    /// <summary>El rectángulo de un elemento, o nulo si no tiene nada que enseñar.</summary>
    private static Pieza? DeUnElemento(
        ElementoPlanta el, bool enX, IReadOnlyList<ElementoPlanta> todos)
    {
        // A LO LARGO del corte se mide con la coordenada que NO es la del eje: en un corte
        // por un eje vertical —de los que van en X— lo que se recorre es la Y.
        var (min, max) = ALoLargo(el, enX);

        var zAbajo = Math.Min(el.Z1, el.Z2);
        var zArriba = Math.Max(el.Z1, el.Z2);

        // ==============================================================================
        //  LA LOSA: UNA FRANJA DE SU ESPESOR
        // ==============================================================================
        //  Se pidió: en la vista extruida la losa se ve y al dibujar el corte no aparecía,
        //  porque se descartaba. En un corte la losa es una franja horizontal de su espesor,
        //  colgada de la cota de su paño, y es lo que da la lectura de los entrepisos: sin
        //  ella el alzado son dos columnas y una cadena en el aire.
        if (el.Clase == ClasePlanta.Losa)
        {
            // ==========================================================================
            //  EL ESPESOR DE LA LOSA NO SE INVENTA
            // ==========================================================================
            //  Aquí había un respaldo de 10 cm y estaba mal, porque esto es un PLANO: la
            //  franja que se dibuja se mide y se acota, así que un espesor puesto a dedo no es
            //  una aproximación, es un dato falso que alguien va a construir.
            //
            //  Si el modelo no lo dio, la losa se dibuja como UNA LÍNEA —alto 0— a la cota de
            //  su paño. La línea dice la verdad: ahí hay una losa y su espesor no se sabe. En
            //  la vista extruida sí hay un mínimo, porque ahí no se acota nada y una losa sin
            //  volumen no se vería; en el plano no.
            var espesor = el.AnchoM > Minimo ? el.AnchoM : 0;
            var largo = max - min;

            return largo > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            min, zArriba - espesor, largo, espesor, el.Tipo)
                : null;
        }

        // EL MURO: su paño, de vértice a vértice y de su cota más baja a la más alta.
        if (el.Clase == ClasePlanta.Muro)
        {
            var alto = zArriba - zAbajo;

            return alto > Minimo && max - min > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            min, zAbajo, max - min, alto, el.Tipo)
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
                            ((min + max) / 2) - (ancho / 2), zAbajo, ancho, alto, el.Tipo)
                : null;
        }

        // LA TRABE, LA CADENA Y LA VIGA: su peralte, siempre. Lo que cambia es el ancho.
        var peralte = el.PeralteM > Minimo ? el.PeralteM : 0.20;
        var largoBarra = max - min;

        // Si corre a lo largo del corte se ve entera; si lo cruza, solo de canto. El
        // criterio es su propio largo: una barra que solo asoma el ancho de su sección está
        // cruzando.
        var deCanto = largoBarra <= (el.AnchoM > Minimo ? el.AnchoM : 0.20) + 0.01;

        if (deCanto)
        {
            var ancho = el.AnchoM > Minimo ? el.AnchoM : 0.20;

            return new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                             ((min + max) / 2) - (ancho / 2), zAbajo - peralte,
                             ancho, peralte, el.Tipo);
        }

        if (largoBarra <= Minimo)
        {
            return null;
        }

        // ==============================================================================
        //  LA TRABE SE DIBUJA COMPLETA, NO DE EJE A EJE
        // ==============================================================================
        //  Se pidió tal cual, y es como se construye: en el modelo la barra va de NUDO a
        //  NUDO —o sea, del eje de una cadena al eje de la otra— pero el concreto de la trabe
        //  llega hasta la CARA EXTERIOR de sus apoyos: se cuela contra la cimbra del apoyo,
        //  no hasta su eje. Dibujada a ejes, en el alzado aparece un hueco a cada punta justo
        //  donde hay más concreto que en ningún otro sitio.
        //
        //  Así que a cada extremo se le suma la MITAD del apoyo que encuentra ahí. Si en ese
        //  nudo no hay nada —un voladizo—, no se le suma nada: ahí la trabe termina de verdad,
        //  y alargarla sería inventarse concreto en el aire.
        var mediaA = MedioApoyoEn(el, enX, min, todos);
        var mediaB = MedioApoyoEn(el, enX, max, todos);

        return new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                         min - mediaA, zAbajo - peralte,
                         largoBarra + mediaA + mediaB, peralte, el.Tipo);
    }

    /// <summary>
    /// <b>Medio apoyo</b> en un extremo de la barra: cuánto hay que alargarla para llegar a su
    /// cara exterior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se busca lo que hay <b>en ese nudo</b> —una columna, un castillo o la cadena que cruza—
    /// y se devuelve la mitad de lo que mide <b>a lo largo del corte</b>, que es la dirección
    /// en la que hay que alargar. De todos los que haya manda el <b>mayor</b>: es el que fija
    /// la cara exterior.
    /// </para>
    /// <para>
    /// Si en el nudo no hay nada, se devuelve <b>0</b>: es el extremo libre de un voladizo, y
    /// ahí la trabe termina donde dice el modelo.
    /// </para>
    /// </remarks>
    public static double MedioApoyoEn(
        ElementoPlanta barra, bool enX, double donde,
        IReadOnlyList<ElementoPlanta> todos)
    {
        var medio = 0d;

        foreach (var otro in todos)
        {
            if (ReferenceEquals(otro, barra) || otro.Clase == ClasePlanta.Losa)
            {
                continue;
            }

            var (min, max) = ALoLargo(otro, enX);

            // ¿Cae en ese extremo? Con la holgura de un centímetro, que es lo que separa dos
            // nudos que en el modelo son el mismo.
            if (donde < min - 0.01 || donde > max + 0.01)
            {
                continue;
            }

            // Y lo que mide a lo largo del corte: una columna aporta su dimensión, y una
            // cadena que cruza, su ancho.
            var mide = otro.Clase == ClasePlanta.Columna
                ? Math.Max(max - min, otro.AnchoM > Minimo ? otro.AnchoM : 0.15)
                : max - min;

            // Una barra que corre A LO LARGO del corte no es un apoyo de esta: es otra trabe
            // en la misma dirección, y sumar su medio largo alargaría la trabe metros.
            if (otro.Clase != ClasePlanta.Columna && mide > 0.6)
            {
                continue;
            }

            medio = Math.Max(medio, mide / 2);
        }

        return medio;
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
