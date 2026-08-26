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
    /// <param name="EnSeccion">
    /// <c>true</c> si lo que se ve es la <b>sección</b> de la pieza —el plano la cruza por su
    /// lado corto, que es donde se dibuja el armado—; <c>false</c> si el corte va <b>a lo largo</b>
    /// de ella y lo que se ve es su costado.
    /// </param>
    /// <param name="Notas">
    /// Las notas de la propiedad, tal como vienen del modelo. De ellas sale <b>de qué es</b> un
    /// muro —tabique, tabicón, adobe o concreto—, que es lo que decide su achurado.
    /// </param>
    public sealed record Pieza(
        ClasePlanta Clase, string Etiqueta, string Seccion,
        double X, double Z, double Ancho, double Alto,
        string Tipo = "", bool Cortada = true, bool EnSeccion = true, string Notas = "");

    /// <summary>Espesor mínimo con el que se dibuja algo, en metros.</summary>
    private const double Minimo = 0.02;

    /// <summary>
    /// ¿Esta pieza es una <b>cadena intermedia</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pregunta aparte porque la cadena intermedia tiene un trato propio en el corte: se
    /// <b>rellena y lleva bloque</b> aunque el corte vaya a lo largo de ella. Se pidió tres veces y
    /// tiene su razón de obra: la intermedia es la que <b>confina los vanos</b> de puertas y
    /// ventanas y la que remata un antepecho, va <b>metida en el muro</b> y es lo que hay que
    /// revisar en un corte. Sin relleno se pierde entre las líneas del paño, y sin bloque no se
    /// puede cambiar por su detalle armado.
    /// </para>
    /// <para>
    /// El tipo lo pone el modelo en las notas de la propiedad —<c>CADENA INTERMEDIA</c>—, así que
    /// esto no adivina nada: lo dice el ingeniero.
    /// </para>
    /// </remarks>
    public static bool EsIntermedia(Pieza p) =>
        (p.Tipo ?? string.Empty).Contains("INTERMEDIA", StringComparison.OrdinalIgnoreCase)
        || (p.Tipo ?? string.Empty).Contains("INTERMEDIO", StringComparison.OrdinalIgnoreCase)
        || (p.Notas ?? string.Empty).Contains("INTERMEDIA", StringComparison.OrdinalIgnoreCase)
        || (p.Notas ?? string.Empty).Contains("INTERMEDIO", StringComparison.OrdinalIgnoreCase);

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
        var (min, max) = Extremos(el, enX);

        var medio = MedioPerpendicular(el, enX) + Holgura(espesorM);

        return max >= ordenada - medio && min <= ordenada + medio;
    }

    /// <summary>
    /// La <b>holgura</b> del corte: lo que se admite de desajuste del modelo, y poco más.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí estaba lo de los <b>elementos encimados</b>. El corte se tomaba como una rebanada del
    /// espesor de la hoja —<c>CORTE_ESPESOR_CM</c>, 60 cm—, así que <b>todo lo que hubiera a 30 cm
    /// del eje se dibujaba como cortado</b>: dos muros paralelos, la cadena del muro de al lado y
    /// las columnas de la fila siguiente salían todos a la vez, unos encima de otros, y el alzado
    /// se volvía ilegible.
    /// </para>
    /// <para>
    /// Cortado es lo que el plano <b>cruza de verdad</b>: el elemento con su propio ancho encima
    /// del eje. La holgura solo tapa el desajuste del modelo —un nudo movido un centímetro, un
    /// muro dibujado a su paño en lugar de a su eje—, y por eso se <b>topa en 5 cm</b> por lado
    /// aunque la hoja diga 60: quien tenía 60 en su hoja no quería 60 cm de rebanada, quería que
    /// el corte no saliera vacío.
    /// </para>
    /// </remarks>
    private static double Holgura(double espesorM) =>
        Math.Min(Math.Max(espesorM, 0.02), 0.10) / 2;

    /// <summary>
    /// Cuánto se extiende un elemento <b>hacia los lados del corte</b>, más allá de su eje.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que «cortado» signifique cortado: un muro de 15 cm dibujado en su eje cruza
    /// el plano si el eje pasa a menos de 7.5 cm, y no si pasa a 30. Un <b>área</b> devuelve cero
    /// porque sus vértices ya traen su contorno; una <b>barra</b>, su medio espesor <b>proyectado</b>
    /// —la que cruza el corte no necesita ninguno, porque su propio eje ya lo atraviesa—; y una
    /// <b>columna</b>, la caja que envuelve a su sección girada, la misma cuenta con la que se
    /// coloca su rótulo.
    /// </remarks>
    private static double MedioPerpendicular(ElementoPlanta el, bool enX)
    {
        if (el.Vertices.Count > 0)
        {
            return 0;
        }

        if (el.Clase == ClasePlanta.Columna)
        {
            var b = el.AnchoM > Minimo ? el.AnchoM : 0.15;
            var h = el.PeralteM > Minimo ? el.PeralteM : b;

            var a = el.AnguloGrados * Math.PI / 180;
            var ca = Math.Abs(Math.Cos(a));
            var sa = Math.Abs(Math.Sin(a));

            return enX ? (b / 2 * ca) + (h / 2 * sa) : (b / 2 * sa) + (h / 2 * ca);
        }

        var esp = (el.AnchoM > Minimo ? el.AnchoM : 0.15) / 2;

        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            return esp;
        }

        // El espesor de una barra va PERPENDICULAR a su eje: se proyecta en la dirección del
        // corte. Una barra paralela al corte aporta su medio espesor entero; una que lo cruza,
        // nada, porque su eje ya lo atraviesa.
        return esp * Math.Abs(enX ? -dy / largo : dx / largo);
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
        bool verElFondo = true, bool haciaMas = true)
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

            if (!cortada && !(verElFondo && AlFondo(el, enX, ordenada, espesorM, haciaMas)))
            {
                continue;
            }

            var p = DeUnElemento(el, enX, elementos);

            if (p is not null)
            {
                piezas.Add(p with { Cortada = cortada });
            }
        }

        return UnirElFondo(SinEncimados(piezas));
    }

    /// <summary>
    /// Los <b>tramos</b> de un muro donde el achurado de mampostería sí va: los que no pisan
    /// concreto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió: el achurado va en los muros de mampostería del fondo <b>y</b> en los que el plano
    /// corta, «siempre y cuando no corte en un elemento de concreto». Y es lo correcto: donde el
    /// corte pasa por un castillo, una cadena o un muro de concreto, lo que hay ahí es
    /// <b>concreto</b>, no piezas con mortero. Achurarlo de tabique sería decir que ese trozo se
    /// levantó con ladrillos.
    /// </para>
    /// <para>
    /// Así que del ancho del muro se <b>quitan</b> los trozos que ocupan las piezas de concreto que
    /// el plano corta, y se devuelve lo que queda. Un castillo en el medio del muro parte su
    /// achurado en dos, que es exactamente lo que se ve en obra: dos paños de mampostería con su
    /// castillo entre los dos.
    /// </para>
    /// <para>
    /// Solo las <b>cortadas</b>: una columna que se ve al fondo no interrumpe la mampostería que
    /// está delante de ella. Y solo las que se <b>encima en vertical</b>: una cadena que va tres
    /// metros más arriba no le quita nada a este muro.
    /// </para>
    /// </remarks>
    /// <param name="muro">La pieza del muro que se va a achurar.</param>
    /// <param name="piezas">Todas las piezas del corte.</param>
    public static List<(double X1, double X2)> TramosSinConcreto(
        Pieza muro, IReadOnlyList<Pieza> piezas)
    {
        var libres = new List<(double X1, double X2)>
        {
            (muro.X, muro.X + muro.Ancho)
        };

        foreach (var q in piezas)
        {
            if (!EsConcretoQueTapa(q, muro))
            {
                continue;
            }

            var siguiente = new List<(double X1, double X2)>();

            foreach (var (a, b) in libres)
            {
                // Lo que queda a la izquierda de la pieza de concreto…
                if (q.X > a)
                {
                    siguiente.Add((a, Math.Min(b, q.X)));
                }

                // …y lo que queda a su derecha.
                var derecha = q.X + q.Ancho;

                if (derecha < b)
                {
                    siguiente.Add((Math.Max(a, derecha), b));
                }
            }

            libres = siguiente
                .Where(t => t.X2 - t.X1 > Minimo)
                .ToList();

            if (libres.Count == 0)
            {
                break;
            }
        }

        return libres;
    }

    /// <summary>¿Esta pieza es de concreto y tapa a ese muro?</summary>
    /// <remarks>
    /// De concreto son el <b>castillo</b> y la <b>columna</b>, la <b>cadena</b> y la <b>trabe</b>, y
    /// también otro <b>muro de concreto</b>. Tiene que estar <b>cortada</b> por el plano —lo que se
    /// ve al fondo no interrumpe lo que está delante—, <b>encimarse en vertical</b> con el muro y
    /// no ser el muro mismo.
    /// </remarks>
    private static bool EsConcretoQueTapa(Pieza q, Pieza muro)
    {
        if (ReferenceEquals(q, muro) || !q.Cortada)
        {
            return false;
        }

        var deConcreto = q.Clase is ClasePlanta.Columna or ClasePlanta.Trabe
                         || (q.Clase == ClasePlanta.Muro
                             && HatchDeMamposteria.Para(q.Notas, q.Seccion) is null);

        if (!deConcreto)
        {
            return false;
        }

        // Que se encimen en vertical: una cadena tres metros más arriba no le quita nada.
        return Math.Min(q.Z + q.Alto, muro.Z + muro.Alto) - Math.Max(q.Z, muro.Z) > Minimo;
    }

    /// <summary>
    /// Une las piezas del <b>fondo</b> que se tocan: una silueta, no una reja de rectángulos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la mitad que faltaba para que el corte se lea como el de un programa de modelado: ahí,
    /// de lo que hay detrás del plano <b>se ve la silueta</b>, no las aristas de cada pieza. En un
    /// muro de mampostería el fondo son cinco o seis paños seguidos a distinta profundidad, y
    /// dibujando cada uno por separado el alzado sale con una raya vertical en cada junta: rayas
    /// que no existen, porque ahí el muro sigue.
    /// </para>
    /// <para>
    /// Así que los paños del fondo que van <b>a la misma altura</b> y se <b>tocan o se encima</b>
    /// se unen en uno. Se repite hasta que no quede nada por unir, porque al unir dos puede que el
    /// resultado alcance a un tercero.
    /// </para>
    /// <para>
    /// <b>Lo cortado no se une nunca</b>: cada pieza cortada es una pieza de obra —esta cadena,
    /// aquel castillo— y fundirlas sería perder lo que el corte tiene que decir. Y solo se unen
    /// piezas de la <b>misma clase</b>, para no fundir un muro con una losa.
    /// </para>
    /// </remarks>
    public static List<Pieza> UnirElFondo(List<Pieza> piezas)
    {
        var salida = piezas.Where(p => p.Cortada).ToList();
        var fondo = piezas.Where(p => !p.Cortada).ToList();

        var cambio = true;

        while (cambio)
        {
            cambio = false;

            for (var i = 0; i < fondo.Count && !cambio; i++)
            {
                for (var j = i + 1; j < fondo.Count && !cambio; j++)
                {
                    if (!SeUnen(fondo[i], fondo[j]))
                    {
                        continue;
                    }

                    var x1 = Math.Min(fondo[i].X, fondo[j].X);
                    var x2 = Math.Max(fondo[i].X + fondo[i].Ancho, fondo[j].X + fondo[j].Ancho);

                    fondo[i] = fondo[i] with { X = x1, Ancho = x2 - x1 };
                    fondo.RemoveAt(j);

                    cambio = true;
                }
            }
        }

        salida.AddRange(fondo);

        return salida;
    }

    /// <summary>¿Estas dos piezas del fondo son el mismo paño visto de largo?</summary>
    /// <remarks>
    /// A la <b>misma altura</b> —el mismo arranque y el mismo alto, con dos centímetros de
    /// holgura— y <b>tocándose</b> a lo largo. Dos paños separados por un vano <b>no</b> se unen:
    /// el hueco es un dato del alzado, no una junta.
    /// </remarks>
    private static bool SeUnen(Pieza a, Pieza b)
    {
        const double h = 0.02;

        if (a.Clase != b.Clase
            || Math.Abs(a.Z - b.Z) > h
            || Math.Abs(a.Alto - b.Alto) > h)
        {
            return false;
        }

        return Math.Min(a.X + a.Ancho, b.X + b.Ancho) >= Math.Max(a.X, b.X) - h;
    }

    /// <summary>
    /// Quita las piezas <b>encimadas</b>: la misma silueta dibujada dos veces no dice nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió que en el corte no se vean elementos unos encima de otros, y aparte de lo que
    /// arregla la holgura queda esto: en el fondo de un alzado, <b>muchas piezas distintas caen en
    /// el mismo sitio</b>. Tres muros paralelos a distinta profundidad se proyectan en el mismo
    /// rectángulo, y la fila de columnas de atrás cae dentro del muro que tienen delante. En el
    /// dibujo eso son rayas sobre rayas: no es información, es ruido.
    /// </para>
    /// <para>
    /// Así que de las siluetas repetidas se queda <b>una</b>, y de las que caen <b>dentro</b> de
    /// otra, la de fuera. Con dos reglas que no se negocian:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Lo CORTADO no se quita nunca.</b> Es el objeto del corte, va con su línea gruesa y
    ///     tiene que estar aunque coincida con algo del fondo.
    ///   </item>
    ///   <item>
    ///     Solo se comparan piezas de la <b>misma clase</b>: una columna dentro de un muro se
    ///     queda, porque en el alzado dicen cosas distintas —una es el paño y la otra el apoyo—.
    ///   </item>
    /// </list>
    /// </remarks>
    public static List<Pieza> SinEncimados(List<Pieza> piezas)
    {
        // Lo cortado primero: así, cuando una del fondo coincida con una cortada, la que se
        // queda es la cortada.
        var orden = piezas
            .Select((p, i) => (p, i))
            .OrderByDescending(x => x.p.Cortada)
            .ThenBy(x => x.i)
            .Select(x => x.p)
            .ToList();

        var salida = new List<Pieza>();

        foreach (var p in orden)
        {
            if (!p.Cortada && salida.Any(q => Tapa(q, p)))
            {
                continue;
            }

            salida.Add(p);
        }

        return salida;
    }

    /// <summary>¿La pieza <paramref name="grande"/> tapa del todo a la otra?</summary>
    private static bool Tapa(Pieza grande, Pieza chica)
    {
        if (grande.Clase != chica.Clase)
        {
            return false;
        }

        // Dos centímetros de holgura: en un plano de obra, por debajo de eso es la misma raya.
        const double h = 0.02;

        return chica.X >= grande.X - h
               && chica.Z >= grande.Z - h
               && chica.X + chica.Ancho <= grande.X + grande.Ancho + h
               && chica.Z + chica.Alto <= grande.Z + grande.Alto + h;
    }

    /// <summary>
    /// ¿Este elemento queda <b>detrás</b> del plano del corte, o sea a la vista?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un corte <b>mira hacia un lado</b>: lo que queda por detrás se ve y lo que queda por
    /// delante se quita, que es lo que hace que el plano enseñe algo en lugar de todo. Y qué lado
    /// es «detrás» <b>lo elige quien dibuja</b>, con <paramref name="haciaMas"/>:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     En un corte cuyo plano está en <b>X</b> —la línea corre en Y— se elige entre ver lo de
    ///     la <b>derecha</b> (X mayores) o lo de la <b>izquierda</b>.
    ///   </item>
    ///   <item>
    ///     En uno cuyo plano está en <b>Y</b> —la línea corre en X— entre lo de <b>arriba</b>
    ///     (Y mayores) o lo de <b>abajo</b>.
    ///   </item>
    /// </list>
    /// <para>
    /// Antes era siempre hacia las coordenadas mayores, y eso dejaba cortes en los que no se veía
    /// nada al fondo: el edificio estaba del otro lado. Es lo mismo que voltear un corte en
    /// cualquier programa de modelado.
    /// </para>
    /// <para>
    /// Y solo entra lo que está <b>del todo</b> a ese lado: lo que el plano cruza ya se dibujó
    /// como cortado, y meterlo dos veces dejaría dos rectángulos encima del otro.
    /// </para>
    /// </remarks>
    public static bool AlFondo(
        ElementoPlanta el, bool enX, double ordenada, double espesorM, bool haciaMas = true)
    {
        var (min, max) = Extremos(el, enX);

        // El mismo margen que usa «cortado», para que ningún elemento sea las dos cosas: lo que
        // el plano cruza se dibuja cortado y lo que queda al lado que se mira, al fondo. Sin el
        // mismo margen en las dos preguntas, los de la frontera salían dos veces, encimados.
        var margen = MedioPerpendicular(el, enX) + Holgura(espesorM);

        return haciaMas
            ? min > ordenada + margen
            : max < ordenada - margen;
    }

    /// <summary>
    /// Lo que <b>se ve</b> de una sección en el corte: su caja envolvente, ya girada.
    /// </summary>
    /// <remarks>
    /// Medida en la dirección que <b>recorre</b> el corte —la Y si el plano está en X—, que es la
    /// horizontal del alzado. Es la misma cuenta con la que se coloca el rótulo de una columna en
    /// planta, y por eso lo que se ve en el corte coincide con lo que se ve en la planta.
    /// </remarks>
    public static double AnchoVisto(ElementoPlanta el, bool enX)
    {
        var b = el.AnchoM > Minimo ? el.AnchoM : 0.15;
        var h = el.PeralteM > Minimo ? el.PeralteM : b;

        var a = el.AnguloGrados * Math.PI / 180;
        var ca = Math.Abs(Math.Cos(a));
        var sa = Math.Abs(Math.Sin(a));

        // A lo largo del corte se mide con la coordenada que NO es la del plano: con el plano en
        // X, lo que se recorre es la Y.
        return enX ? (b * sa) + (h * ca) : (b * ca) + (h * sa);
    }

    /// <summary>
    /// Cuánto le quita a un muro la <b>cadena que lleva encima</b>, en metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El peralte de la trabe o la cadena que corre <b>sobre su misma línea</b> y remata a su
    /// altura. Es lo que hace que el muro llegue al paño de <b>abajo</b> de la cadena: en el
    /// modelo el muro sube hasta la cota del nivel, que es el <b>eje</b> de la cadena, así que sin
    /// esto el muro se mete el peralte entero dentro de ella.
    /// </para>
    /// <para>
    /// De varias, la <b>más peraltada</b>: el muro no puede meterse en ninguna. Y solo cuentan las
    /// que van <b>a lo largo</b> del muro —las que lo cruzan pasan por encima y no lo rematan— y
    /// las que están <b>a su altura</b>, con la holgura de un desajuste de modelo.
    /// </para>
    /// </remarks>
    public static double AlturaQueTapaLaCadena(
        ElementoPlanta muro, IReadOnlyList<ElementoPlanta> todos, double tolM = 0.10)
    {
        var dx = muro.X2 - muro.X1;
        var dy = muro.Y2 - muro.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            return 0;
        }

        var ux = dx / largo;
        var uy = dy / largo;

        var arribaDelMuro = Math.Max(muro.Z1, muro.Z2);

        double peralte = 0;

        foreach (var c in todos)
        {
            if (c.Clase != ClasePlanta.Trabe || c.PeralteM <= Minimo)
            {
                continue;
            }

            // A SU ALTURA: la cadena remata el muro si su eje está a la cota de arriba del muro.
            if (Math.Abs(Math.Max(c.Z1, c.Z2) - arribaDelMuro) > tolM)
            {
                continue;
            }

            var vx = c.X2 - c.X1;
            var vy = c.Y2 - c.Y1;
            var largoC = Math.Sqrt((vx * vx) + (vy * vy));

            if (largoC < 1e-9)
            {
                continue;
            }

            // A LO LARGO DEL MURO: paralela y sobre su línea. Una que lo cruza pasa por encima.
            if (Math.Abs((ux * (vy / largoC)) - (uy * (vx / largoC))) > 0.10)
            {
                continue;
            }

            if (Math.Abs((-uy * (c.X1 - muro.X1)) + (ux * (c.Y1 - muro.Y1))) > tolM)
            {
                continue;
            }

            // Y QUE SE ENCIMEN de verdad a lo largo: una cadena del muro de al lado, alineada con
            // este pero en otro tramo, no lo remata.
            var t1 = (ux * (c.X1 - muro.X1)) + (uy * (c.Y1 - muro.Y1));
            var t2 = (ux * (c.X2 - muro.X1)) + (uy * (c.Y2 - muro.Y1));

            if (t2 < t1)
            {
                (t1, t2) = (t2, t1);
            }

            if (Math.Min(t2, largo) - Math.Max(t1, 0) <= tolM)
            {
                continue;
            }

            peralte = Math.Max(peralte, c.PeralteM);
        }

        return peralte;
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

            // La losa se ve por su canto a lo largo de todo el corte: es un corte LONGITUDINAL
            // de ella, no su sección, así que va con su línea y sin relleno.
            return largo > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            min, zArriba - espesor, largo, espesor, el.Tipo, EnSeccion: false)
                : null;
        }

        // EL MURO: su paño, de vértice a vértice y de su cota más baja a la de abajo de su cadena.
        if (el.Clase == ClasePlanta.Muro)
        {
            // ==========================================================================
            //  HASTA EL PAÑO DE ABAJO DE LA TRABE O LA CADENA
            // ==========================================================================
            //  Se pidió, y es lo que se construye: el muro sube hasta donde empieza la cadena,
            //  no hasta su eje. En el modelo el muro llega a la COTA DEL NIVEL —que es el eje de
            //  la cadena— así que dibujándolo tal cual se mete todo el peralte de la cadena
            //  dentro de ella: en el corte se veían el muro y la cadena pisándose, y la cadena
            //  perdía su franja.
            var alto = (zArriba - AlturaQueTapaLaCadena(el, todos)) - zAbajo;

            // ==========================================================================
            //  Y A LO ANCHO, HASTA EL PAÑO DEL CASTILLO, NO HASTA SU EJE
            // ==========================================================================
            //  Se pidió, y es la misma regla que ya cumple la planta: en el modelo el muro va de
            //  NUDO a NUDO —del eje de un castillo al del siguiente— pero el muro de verdad
            //  arranca en la CARA del castillo, porque contra él se levanta. Dibujado a ejes, en
            //  el alzado el muro se mete medio castillo por cada punta y lo pisa justo donde el
            //  castillo tiene que verse entero, que es donde lleva su armado.
            //
            //  Es lo contrario de lo que se hace con la trabe —a ella se le SUMA medio apoyo,
            //  porque su concreto se cuela hasta la cara exterior— y por eso es la misma cuenta
            //  con el signo cambiado.
            var caraA = MedioApoyoEn(el, enX, min, todos);
            var caraB = MedioApoyoEn(el, enX, max, todos);

            var izquierda = min + caraA;
            var derecha = max - caraB;

            // Si los castillos se comieran el muro entero —un tramo más corto que sus dos
            // apoyos— se deja como estaba: mejor un muro de más que un hueco donde hay pared.
            if (derecha - izquierda <= Minimo)
            {
                izquierda = min;
                derecha = max;
            }

            // El muro se ve de frente, no en sección: es su paño.
            return alto > Minimo && derecha - izquierda > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            izquierda, zAbajo, derecha - izquierda, alto, el.Tipo,
                            EnSeccion: false, Notas: el.Notas)
                : null;
        }

        // LA COLUMNA: de canto y de nudo a nudo. El ancho es lo que SE VE del corte.
        if (el.Clase == ClasePlanta.Columna)
        {
            // ==========================================================================
            //  LO QUE SE VE ES LA SECCIÓN PROYECTADA, NO SU LADO MÁS LARGO
            // ==========================================================================
            //  Aquí estaba el castillo de 80 cm. Se tomaba AnchoM a secas, y en un castillo de
            //  área ese ancho es su LARGO —«K 15X80» mide 80 a lo largo del muro y 15 de
            //  espesor—: en un corte que lo cruza de frente se veía un rectángulo amarillo de
            //  80 cm cuando lo que se ve de verdad son sus 15 cm de espesor.
            //
            //  Lo que se ve es la sección GIRADA, medida en la dirección que recorre el corte:
            //  la caja que la envuelve, la misma cuenta con la que se coloca su rótulo en
            //  planta. Así un castillo de 15×80 se ve de 15 cuando el corte lo cruza y de 80
            //  cuando el corte va a lo largo de él, que es lo correcto en los dos casos, y una
            //  columna de 20×60 girada 90° se ve de 60 en lugar de 20.
            var ancho = AnchoVisto(el, enX);
            var alto = zArriba - zAbajo;

            // Una columna de altura nula no es una columna: es un nudo mal leído.
            return alto > Minimo
                ? new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                            ((min + max) / 2) - (ancho / 2), zAbajo, ancho, alto, el.Tipo,
                            EnSeccion: PorSuLadoCorto(el, enX))
                : null;
        }

        // LA TRABE, LA CADENA Y LA VIGA: su peralte, siempre. Lo que cambia es el ancho.
        var peralte = el.PeralteM > Minimo ? el.PeralteM : 0.20;
        var largoBarra = max - min;

        // Si corre a lo largo del corte se ve entera; si lo cruza, solo de canto. El
        // criterio es su propio largo: una barra que solo asoma el ancho de su sección está
        // cruzando.
        var deCanto = largoBarra <= (el.AnchoM > Minimo ? el.AnchoM : 0.20) + 0.01;

        // ==============================================================================
        //  DE CANTO ES SU SECCIÓN; A LO LARGO, SU COSTADO
        // ==============================================================================
        //  Es la convención de cualquier plano de obra, y es lo que se pidió: la barra que el
        //  plano CRUZA se ve por su sección —el lado corto, el que lleva el armado y los
        //  estribos— y esa se rellena; la que corre A LO LARGO del corte se ve de costado, y esa
        //  va solo con su línea. Rellenar las dos es decir que las dos están cortadas igual, y
        //  entonces el alzado no dice por dónde pasa el plano.
        if (deCanto)
        {
            var ancho = el.AnchoM > Minimo ? el.AnchoM : 0.20;

            return new Pieza(el.Clase, el.Etiqueta, el.Seccion,
                             ((min + max) / 2) - (ancho / 2), zAbajo - peralte,
                             ancho, peralte, el.Tipo, Notas: el.Notas);
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
                         largoBarra + mediaA + mediaB, peralte, el.Tipo,
                         EnSeccion: false, Notas: el.Notas);
    }

    /// <summary>
    /// ¿El plano corta a esta sección por su <b>lado corto</b>, o sea la ve <b>en sección</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la pregunta que decide si la pieza se rellena. Lo que se ve en el alzado es la sección
    /// proyectada; si lo que se ve es su lado <b>corto</b>, el plano la está cruzando y lo que hay
    /// ahí es <b>su sección</b>: la cara donde se dibuja el armado. Si lo que se ve es su lado
    /// <b>largo</b>, el corte va a lo largo de la pieza y lo que se ve es su <b>costado</b>.
    /// </para>
    /// <para>
    /// Es el caso del castillo de área «K 15X80»: cortado por su lado de 15 es una sección —se
    /// rellena—, y cortado a lo largo de sus 80 es un costado —solo su línea—. En una sección
    /// <b>cuadrada</b> las dos medidas son la misma, así que siempre se ve «en sección», que es lo
    /// que corresponde: un castillo de 15×15 se rellena se corte por donde se corte.
    /// </para>
    /// </remarks>
    public static bool PorSuLadoCorto(ElementoPlanta el, bool enX)
    {
        var b = el.AnchoM > Minimo ? el.AnchoM : 0.15;
        var h = el.PeralteM > Minimo ? el.PeralteM : b;

        var corto = Math.Min(b, h);
        var largo = Math.Max(b, h);

        // Cuadrada —o casi—: no hay lado largo que valga, y se ve en sección siempre.
        if (largo - corto <= 0.02)
        {
            return true;
        }

        // Lo que se ve tiene que parecerse al lado CORTO, no al largo. Se compara con el punto
        // medio de los dos para que un giro de unos grados no cambie la respuesta.
        return AnchoVisto(el, enX) < (corto + largo) / 2;
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
