namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// La losa en planta: <b>qué lados tiene apoyados</b>, si está <b>volada</b>, y la
/// <b>parrilla</b> de su armado recortada al paño.
/// </summary>
/// <remarks>
/// <para>
/// Es la parte de <c>LadoApoyado</c>, <c>ArmadoLosa</c> y <c>CortesEnX</c> / <c>CortesEnY</c>
/// de la macro que es pura aritmética. Aquí no hay AutoCAD: se decide <b>dónde</b> va cada
/// varilla y qué trozos del contorno se dibujan, y el dibujante solo los pasa a líneas. Con
/// eso se puede comprobar contra los números de la macro sin abrir AutoCAD, y está en
/// <c>tools/prueba-ejes-plano</c>.
/// </para>
/// <para>
/// <b>Las tres decisiones que salen de aquí</b>, y por qué importan en el plano:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Qué lados están apoyados.</b> Un tablero apoyado en sus cuatro lados trabaja en dos
///     direcciones y se arma con parrilla; uno apoyado en un solo lado <b>está volado</b> y
///     lleva su hatch y su armado arriba. Confundirlos es un error de cálculo, no de dibujo.
///   </item>
///   <item>
///     <b>Dónde empieza y acaba cada varilla.</b> La parrilla se recorta al <b>contorno
///     real</b> del paño, no a su rectángulo envolvente: en una losa en L, media parrilla
///     quedaría en el aire.
///   </item>
///   <item>
///     <b>Qué trozos del contorno se ven.</b> El contorno no se dibuja por dentro del muro ni
///     de la cadena: ahí la losa apoya, y una línea en medio del muro se lee como una junta
///     que no existe.
///   </item>
/// </list>
/// </remarks>
public static class LosaEnPlanta
{
    private const double Nada = 1e-9;

    /// <summary>Un tramo de recta, en metros.</summary>
    public readonly record struct Segmento(double X1, double Y1, double X2, double Y2)
    {
        public double Largo =>
            Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
    }

    /// <summary>Los lados del paño, cerrando del último vértice al primero.</summary>
    public static List<Segmento> Lados(IReadOnlyList<(double X, double Y)> v)
    {
        var salida = new List<Segmento>();

        if (v.Count < 3)
        {
            return salida;
        }

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            if (Math.Abs(b.X - a.X) > Nada || Math.Abs(b.Y - a.Y) > Nada)
            {
                salida.Add(new Segmento(a.X, a.Y, b.X, b.Y));
            }
        }

        return salida;
    }

    /// <summary>
    /// <b>Medio ancho</b> del apoyo que corre a lo largo de un <b>borde del armado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que hace que la varilla del tablero llegue al <b>paño</b> del apoyo y no a su
    /// <b>eje</b>. Y hace falta porque en el modelo la losa se dibuja <b>hasta el eje</b> de la
    /// trabe o del muro que la sostiene: tomando el borde del paño tal cual, la varilla se mete
    /// media trabe dentro de ella, que no es donde empieza el claro ni donde se pone el acero.
    /// </para>
    /// <para>
    /// <b>Por qué se pregunta por el BORDE y no por el lado del polígono</b>, que es como estaba
    /// antes y por lo que las trabes no entraban: el armado se traza sobre la <b>caja</b> del
    /// tablero, y el lado del polígono que buscaba la cuenta vieja tenía que ser el de la
    /// coordenada extrema <b>y</b> estar alineado con los ejes al milímetro de millón. Un tablero
    /// que no es un rectángulo perfecto —con un quiebre, con un vértice de más, o con las
    /// coordenadas que trae ETABS, que casi nunca son exactas— dejaba sin encontrar ese lado, y
    /// entonces no se corría nada: ni por la trabe ni por el muro. Preguntando por el borde de la
    /// caja se acabó el problema: ahí siempre está.
    /// </para>
    /// <para>
    /// Cuenta cualquier apoyo —<b>muro, cadena o trabe</b>—: lo único que se le pide es ir
    /// <b>paralelo</b> al borde, estar <b>sobre su línea</b> —con la holgura del encuentro— y
    /// <b>correr a lo largo</b> de él, no cruzarlo. Y de todos los que cumplan, el <b>más
    /// ancho</b>: el paño que manda es el más saliente.
    /// </para>
    /// </remarks>
    /// <param name="borde">El borde de la caja del armado, como segmento.</param>
    /// <param name="huellas">Las huellas de los apoyos: muros, cadenas y trabes.</param>
    /// <param name="tolM">Holgura para tomarlo como que va sobre el borde: la del encuentro.</param>
    /// <param name="fraccionMin">
    /// Qué parte del borde tiene que recorrer el apoyo. Con 0.2 basta con que lo acompañe en una
    /// quinta parte: una trabe que solo cruza el borde por un punto no lo apoya.
    /// </param>
    public static double MedioApoyoEnBorde(
        Segmento borde, IReadOnlyList<ElementoPlanta> huellas, double tolM,
        double fraccionMin = 0.2)
    {
        var largo = borde.Largo;

        if (largo < Nada || huellas.Count == 0)
        {
            return 0;
        }

        var ux = (borde.X2 - borde.X1) / largo;
        var uy = (borde.Y2 - borde.Y1) / largo;

        double ancho = 0;

        foreach (var h in huellas)
        {
            if (h.AnchoM < Nada)
            {
                continue;
            }

            // La huella es un rectángulo: su eje va en AnchoM —su largo— con su giro.
            var a = h.AnguloGrados * Math.PI / 180;
            var vx = Math.Cos(a);
            var vy = Math.Sin(a);

            // PARALELO al borde: si lo cruza, no lo apoya.
            if (Math.Abs((ux * vy) - (uy * vx)) > 0.10)
            {
                continue;
            }

            // SOBRE SU LÍNEA: lo que separa su eje del borde, medido de través.
            if (Math.Abs((-uy * (h.X1 - borde.X1)) + (ux * (h.Y1 - borde.Y1))) > tolM)
            {
                continue;
            }

            // Y QUE LO RECORRA: sus dos puntas, proyectadas sobre el borde.
            var medio = h.AnchoM / 2;

            var t1 = ((h.X1 - (vx * medio) - borde.X1) * ux)
                     + ((h.Y1 - (vy * medio) - borde.Y1) * uy);

            var t2 = ((h.X1 + (vx * medio) - borde.X1) * ux)
                     + ((h.Y1 + (vy * medio) - borde.Y1) * uy);

            if (t2 < t1)
            {
                (t1, t2) = (t2, t1);
            }

            if (Math.Min(t2, largo) - Math.Max(t1, 0) < largo * fraccionMin)
            {
                continue;
            }

            ancho = Math.Max(ancho, h.PeralteM);
        }

        return ancho / 2;
    }

    /// <summary>
    /// Qué <b>fracción</b> de un lado tiene apoyo debajo: es <c>LongitudUnion</c>.
    /// </summary>
    /// <remarks>
    /// Se proyecta cada apoyo sobre la recta del lado y se suma la <b>unión</b> de los tramos
    /// cubiertos, no la suma: dos cadenas que se traslapan en el nudo cubren su longitud una
    /// sola vez, y sumándolas se pasaría del 100 % y un lado con dos cadenitas parecería
    /// apoyado entero.
    /// </remarks>
    public static double FraccionApoyada(
        Segmento lado, IReadOnlyList<ElementoPlanta> huellas)
    {
        var largo = lado.Largo;

        if (largo < Nada || huellas.Count == 0)
        {
            return 0;
        }

        var ux = (lado.X2 - lado.X1) / largo;
        var uy = (lado.Y2 - lado.Y1) / largo;

        var tramos = new List<(double A, double B)>();

        foreach (var h in huellas)
        {
            foreach (var t in PanoDeApoyo.Intervalos(h, lado.X1, lado.Y1, ux, uy))
            {
                var a = Math.Max(0, Math.Min(t.A, t.B));
                var b = Math.Min(largo, Math.Max(t.A, t.B));

                if (b > a)
                {
                    tramos.Add((a, b));
                }
            }
        }

        return Unidos(tramos).Sum(t => t.B - t.A) / largo;
    }

    /// <summary>
    /// Cuántos lados del paño están apoyados, y en cuáles.
    /// </summary>
    /// <param name="cubre">
    /// Fracción del lado que tiene que estar apoyada: <c>LOSA_APOYO_CUBRE</c>, 0.7. No se pide
    /// el 100 % porque en el modelo las cadenas se cortan en los nudos y siempre falta un
    /// pedacito.
    /// </param>
    public static bool[] LadosApoyados(
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<ElementoPlanta> huellas,
        double cubre = 0.7)
    {
        var lados = Lados(vertices);
        var salida = new bool[lados.Count];

        for (var i = 0; i < lados.Count; i++)
        {
            salida[i] = FraccionApoyada(lados[i], huellas) >= cubre;
        }

        return salida;
    }

    /// <summary>
    /// ¿La losa está <b>volada</b>? Lo está si le apoya <b>un lado o ninguno</b>.
    /// </summary>
    /// <remarks>
    /// Es la regla de la macro y la del cálculo: con un solo lado apoyado el tablero es un
    /// voladizo —trabaja en cantiléver y su acero va <b>arriba</b>—, y con dos o más es un
    /// tablero normal. Un paño sin ningún lado apoyado también se marca como volado: o es un
    /// volado, o es un dato malo, y las dos cosas hay que verlas en el plano.
    /// </remarks>
    public static bool EsVolada(
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<ElementoPlanta> huellas,
        double cubre = 0.7) =>
        vertices.Count >= 3 && LadosApoyados(vertices, huellas, cubre).Count(a => a) <= 1;

    /// <summary>
    /// La <b>parrilla</b> del armado, recortada al contorno real del paño.
    /// </summary>
    /// <param name="sep">Separación de las varillas, en metros: <c>MALLA_SEP_CM</c>.</param>
    /// <param name="margen">Cuánto se retira del borde: <c>ARMADO_LOSA_MARGEN_CM</c>.</param>
    /// <param name="dosDirecciones">Parrilla en las dos direcciones, o solo en la corta.</param>
    /// <param name="maxLineas">
    /// Tope de varillas por dirección: <c>MALLA_MAX_LINEAS</c>, 200. Es la válvula de escape
    /// de la macro, y hace falta: una losa mal leída de 300 m con varillas a 15 cm son dos mil
    /// líneas y AutoCAD se arrodilla.
    /// </param>
    /// <param name="minTramo">
    /// Tramos más cortos que esto no se dibujan: <c>MALLA_SEGMENTO_MIN_CM</c>. En una esquina
    /// en punta, la parrilla deja rabitos de dos centímetros que solo ensucian.
    /// </param>
    /// <remarks>
    /// <para>
    /// Es el barrido de <c>CortesEnX</c> / <c>CortesEnY</c>: por cada línea de la parrilla se
    /// buscan los cortes con los lados del polígono, se ordenan y se toman <b>por parejas</b>
    /// —dentro, fuera, dentro, fuera—. Así la parrilla sale bien en una losa en L o con un
    /// hueco, no solo en un rectángulo.
    /// </para>
    /// <para>
    /// El detalle que hay que copiar tal cual es la <b>regla semiabierta</b>
    /// —<c>(a &lt;= c &amp;&amp; b &gt; c)</c>—: un vértice que cae justo en la línea de la
    /// parrilla cuenta <b>una</b> vez y no dos. Sin eso, las parejas se descuadran a partir de
    /// ese vértice y la mitad de la parrilla sale fuera de la losa.
    /// </para>
    /// </remarks>
    public static List<Segmento> Parrilla(
        IReadOnlyList<(double X, double Y)> vertices,
        double sep,
        double margen = 0,
        bool dosDirecciones = true,
        int maxLineas = 200,
        double minTramo = 0.15)
    {
        var salida = new List<Segmento>();

        if (vertices.Count < 3 || sep <= Nada)
        {
            return salida;
        }

        var xMin = vertices.Min(v => v.X) + margen;
        var xMax = vertices.Max(v => v.X) - margen;
        var yMin = vertices.Min(v => v.Y) + margen;
        var yMax = vertices.Max(v => v.Y) - margen;

        if (xMax <= xMin || yMax <= yMin)
        {
            return salida;
        }

        // La dirección CORTA lleva el acero principal, así que si solo va una, va esa.
        var corta = (xMax - xMin) <= (yMax - yMin);

        if (dosDirecciones || corta)
        {
            Barrer(vertices, xMin, xMax, sep, maxLineas, minTramo, true, salida);
        }

        if (dosDirecciones || !corta)
        {
            Barrer(vertices, yMin, yMax, sep, maxLineas, minTramo, false, salida);
        }

        return salida;
    }

    private static void Barrer(
        IReadOnlyList<(double X, double Y)> v,
        double desde, double hasta, double sep, int maxLineas, double minTramo,
        bool enX, List<Segmento> salida)
    {
        var cuantas = (int)Math.Floor((hasta - desde) / sep);

        if (cuantas < 1)
        {
            return;
        }

        if (maxLineas > 0 && cuantas > maxLineas)
        {
            cuantas = maxLineas;
        }

        for (var i = 1; i <= cuantas; i++)
        {
            var c = desde + (i * sep);

            foreach (var (a, b) in Cortes(v, c, enX))
            {
                if (b - a < minTramo)
                {
                    continue;
                }

                salida.Add(enX
                    ? new Segmento(c, a, c, b)
                    : new Segmento(a, c, b, c));
            }
        }
    }

    /// <summary>
    /// Los tramos en que una línea de la parrilla va <b>por dentro</b> del paño.
    /// </summary>
    /// <remarks>
    /// La regla semiabierta va aquí: se cuenta el cruce cuando un extremo del lado está a un
    /// lado o justo encima de la línea y el otro <b>estrictamente</b> al otro. Es lo que hace
    /// que un vértice sobre la línea no cuente dos veces.
    /// </remarks>
    public static List<(double A, double B)> Cortes(
        IReadOnlyList<(double X, double Y)> v, double c, bool enX)
    {
        var cruces = new List<double>();

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            var ca = enX ? a.X : a.Y;
            var cb = enX ? b.X : b.Y;

            // Semiabierta: (ca <= c && cb > c) o al revés. Un vértice justo en la línea
            // cuenta UNA vez.
            if (!((ca <= c && cb > c) || (cb <= c && ca > c)))
            {
                continue;
            }

            var t = (c - ca) / (cb - ca);

            cruces.Add(enX
                ? a.Y + (t * (b.Y - a.Y))
                : a.X + (t * (b.X - a.X)));
        }

        cruces.Sort();

        var salida = new List<(double A, double B)>();

        for (var i = 0; i + 1 < cruces.Count; i += 2)
        {
            salida.Add((cruces[i], cruces[i + 1]));
        }

        return salida;
    }

    /// <summary>Un trazo del armado: sus puntos y si va en <b>doble línea</b>.</summary>
    /// <param name="Doble">
    /// <c>true</c> = la varilla, que se dibuja con sus <b>dos líneas</b> separadas su
    /// diámetro, como hace <c>DobleLineaDesde</c> en la macro. <c>false</c> = una rayita
    /// suelta, como la marca del extremo del bastón.
    /// </param>
    /// <param name="EnX">La varilla corre a lo largo de X: el doble se separa en Y.</param>
    public sealed record Trazo(List<(double X, double Y)> Puntos, bool Doble, bool EnX);

    /// <summary>
    /// El armado del <b>tablero apoyado en sus cuatro lados</b>: bayoneta, bastones y
    /// corrida.
    /// </summary>
    /// <param name="escala">
    /// <c>ARMADO_LOSA_ESCALA_VARILLA</c>: multiplica el grosor de la varilla y, con él,
    /// todas las separaciones. En 1 salen las medidas de la macro.
    /// </param>
    /// <remarks>
    /// <para>
    /// Es <c>ArmadoDireccionX</c> y <c>ArmadoDireccionY</c>, con <b>sus números</b>: varilla
    /// de 1.57 cm, separación de la bayoneta 1.57, corrida a 3.44 y bastones a 2.87. Por
    /// dirección salen <b>cuatro</b> trazos, que son los que se ven en su plano:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     La <b>bayoneta</b>: seis vértices con sus dos quiebres a 45°. Va arriba junto a
    ///     los apoyos y baja al centro del claro, que es donde el momento cambia de signo.
    ///     En planta ese cambio se ve como un salto de lado, y es el símbolo con el que se
    ///     lee en obra.
    ///   </item>
    ///   <item>Dos <b>bastones</b> de L/4, uno en cada apoyo, con su rayita en la punta.</item>
    ///   <item>Y la <b>corrida</b>, de lado a lado.</item>
    /// </list>
    /// <para>
    /// Los quiebres van <b>en pico</b> y no redondeados: la macro los filetea con radio de
    /// 1.5 cm —<c>ARMADO_LOSA_FILETE</c>— y a la escala de un plano de planta ese redondeo
    /// mide dos décimas de milímetro en el papel. Se deja anotado por si algún día se quiere
    /// el <i>bulge</i>.
    /// </para>
    /// </remarks>
    public static List<Trazo> ArmadoDeTablero(
        double x0, double y0, double x1, double y1,
        bool dosDirecciones = true, double escala = 1)
    {
        var salida = new List<Trazo>();

        if (x1 - x0 <= Nada || y1 - y0 <= Nada)
        {
            return salida;
        }

        if (escala <= 0)
        {
            escala = 1;
        }

        // Las medidas de la macro, en metros.
        var barD = 0.0157 * escala;
        var sepB = 0.0157 * escala;
        var corrOff = 0.0344 * escala;
        var bastOff = 0.0287 * escala;

        EnUnaDireccion(x0, y0, x1, y1, true);

        if (dosDirecciones)
        {
            EnUnaDireccion(y0, x0, y1, x1, false);
        }

        return salida;

        // a0..a1 = a lo largo de la varilla; b0..b1 = la otra dirección.
        void EnUnaDireccion(double a0, double b0, double a1, double b1, bool enX)
        {
            var largo = a1 - a0;

            if (largo <= Nada)
            {
                return;
            }

            var medio = (b0 + b1) / 2;
            var arriba = medio + sepB;
            var abajo = medio - sepB;
            var run45 = arriba - abajo;

            var hApoyo = Math.Max(0, (largo / 4) - run45);
            var hCentro = Math.Max(0, (largo / 2) - (2 * run45));
            var hBaston = largo / 4;

            var q1 = a0 + hApoyo;
            var q2 = q1 + run45;
            var q3 = q2 + hCentro;
            var q4 = Math.Min(a1, q3 + run45);

            // 1) LA BAYONETA, seis vértices.
            salida.Add(Trazo6(a0, arriba, q1, arriba, q2, abajo, q3, abajo, q4, arriba,
                              a1, arriba, enX));

            // 2) LOS DOS BASTONES, con su rayita en la punta de adentro.
            var bBaston = arriba + (barD / 2) + bastOff;

            salida.Add(Recta(a0, bBaston, a0 + hBaston, bBaston, enX, true));
            salida.Add(Recta(a1 - hBaston, bBaston, a1, bBaston, enX, true));

            salida.Add(Recta(a0 + hBaston, bBaston - (barD / 2),
                             a0 + hBaston, bBaston + (barD / 2), enX, false));
            salida.Add(Recta(a1 - hBaston, bBaston - (barD / 2),
                             a1 - hBaston, bBaston + (barD / 2), enX, false));

            // 3) Y LA CORRIDA, de lado a lado.
            var bCorrida = abajo - (barD / 2) - corrOff;

            salida.Add(Recta(a0, bCorrida, a1, bCorrida, enX, true));
        }

        static Trazo Recta(double a1, double b1, double a2, double b2, bool enX, bool doble) =>
            new(new List<(double X, double Y)>
                {
                    enX ? (a1, b1) : (b1, a1),
                    enX ? (a2, b2) : (b2, a2)
                },
                doble, enX);

        static Trazo Trazo6(
            double a1, double b1, double a2, double b2, double a3, double b3,
            double a4, double b4, double a5, double b5, double a6, double b6, bool enX)
        {
            var pares = new[] { (a1, b1), (a2, b2), (a3, b3), (a4, b4), (a5, b5), (a6, b6) };

            return new Trazo(
                pares.Select(p => enX ? (X: p.Item1, Y: p.Item2) : (X: p.Item2, Y: p.Item1))
                     .ToList(),
                true, enX);
        }
    }

    /// <summary>
    /// El desplazamiento de la <b>doble línea</b> de una varilla, en metros.
    /// </summary>
    /// <remarks>
    /// La macro dibuja el eje de la varilla y le hace <c>Offset(±d/2)</c>; aquí se dibujan
    /// las dos líneas directamente, que es lo mismo y no depende de que <c>Offset</c>
    /// funcione por COM.
    /// </remarks>
    public static double MedioDiametroDeVarilla(double escala = 1) =>
        0.0157 * (escala > 0 ? escala : 1) / 2;

    /// <summary>
    /// Las <b>franjas de losacero</b>: dónde va cada una y hasta dónde llega.
    /// </summary>
    /// <param name="ancho">Ancho de la franja: <c>LOSACERO_FRANJA_ANCHO_M</c>, 0.15.</param>
    /// <param name="paso">De centro a centro: <c>LOSACERO_FRANJA_SEP_M</c>, 0.8.</param>
    /// <param name="minLargo">Una franja más corta que esto no se dibuja.</param>
    /// <remarks>
    /// <para>
    /// Es <c>FranjasLosacero</c>. Las franjas van en el sentido <b>corto</b> del tablero
    /// —que es como se coloca la lámina, apoyada en el claro menor— y se reparten
    /// <b>centradas</b> a lo largo del otro. Cada una se recorta contra el contorno real del
    /// paño, así que en una losa en L no se salen.
    /// </para>
    /// <para>
    /// Se devuelve el eje de cada franja; el ancho lo pone el dibujante al armar el
    /// rectángulo que después se achura con <c>FLEX</c>.
    /// </para>
    /// </remarks>
    public static List<Segmento> Franjas(
        IReadOnlyList<(double X, double Y)> vertices,
        double ancho = 0.15, double paso = 0.8, double minLargo = 0.3)
    {
        var salida = new List<Segmento>();

        if (vertices.Count < 3 || ancho <= Nada)
        {
            return salida;
        }

        var xMin = vertices.Min(v => v.X);
        var xMax = vertices.Max(v => v.X);
        var yMin = vertices.Min(v => v.Y);
        var yMax = vertices.Max(v => v.Y);

        if (paso < ancho * 1.1)
        {
            paso = ancho * 1.1;
        }

        // La franja corre en el sentido CORTO; se repiten a lo largo del otro.
        var horizontal = (xMax - xMin) <= (yMax - yMin);

        var largo = horizontal ? yMax - yMin : xMax - xMin;

        if (largo < ancho)
        {
            return salida;
        }

        var cuantas = (int)Math.Floor((largo - ancho) / paso) + 1;

        if (cuantas < 1)
        {
            cuantas = 1;
        }

        var total = ((cuantas - 1) * paso) + ancho;
        var inicio = (horizontal ? yMin : xMin) + ((largo - total) / 2) + (ancho / 2);

        for (var i = 0; i < cuantas; i++)
        {
            var c = inicio + (i * paso);

            // Dónde entra y sale la franja del contorno: la línea es horizontal si la
            // franja va en X, así que se cortan las Y.
            foreach (var (a, b) in Cortes(vertices, c, !horizontal))
            {
                if (b - a < minLargo)
                {
                    continue;
                }

                salida.Add(horizontal
                    ? new Segmento(a, c, b, c)
                    : new Segmento(c, a, c, b));
            }
        }

        return salida;
    }

    /// <summary>
    /// ¿Es una <b>losacero</b>? Lo dicen la etiqueta, las notas o la sección.
    /// </summary>
    /// <remarks>
    /// Es <c>EsLosacero</c>, y mira <b>la etiqueta primero</b>, igual que allá: en un modelo
    /// real la propiedad se llama «DECK1» y quien dice de verdad qué es son las notas o la
    /// etiqueta que el ingeniero puso.
    /// </remarks>
    public static bool DiceLosacero(
        string? etiqueta, string? notas, string? seccion, string palabras)
    {
        var texto = ((etiqueta ?? string.Empty) + " " + (notas ?? string.Empty) + " " +
                     (seccion ?? string.Empty)).ToUpperInvariant();

        if (texto.Trim().Length == 0)
        {
            return false;
        }

        foreach (var palabra in palabras.Split(','))
        {
            var p = palabra.Trim().ToUpperInvariant();

            if (p.Length > 0 && texto.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// El <b>calibre</b> de la losacero que traen las notas: el número después de
    /// <c>CAL</c>.
    /// </summary>
    /// <remarks>
    /// Es <c>CalibreDeTexto</c>, con su misma regla: primero el número que sigue a
    /// <c>CAL</c> —«LOSACERO CAL 24» o «CALIBRE 22»— y, si no hay ninguno, el <b>último</b>
    /// número del texto, que es donde suele acabar el dato. Devuelve vacío si no trae
    /// números y entonces manda <c>LOSACERO_CALIBRE_OMISION</c>.
    /// </remarks>
    public static string Calibre(string? texto)
    {
        var t = new string((texto ?? string.Empty)
            .ToUpperInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '.')
            .ToArray());

        if (t.Length == 0)
        {
            return string.Empty;
        }

        var cal = t.IndexOf("CAL", StringComparison.Ordinal);

        if (cal >= 0)
        {
            var n = new System.Text.StringBuilder();

            for (var i = cal + 3; i < t.Length; i++)
            {
                if (char.IsAsciiDigit(t[i]))
                {
                    n.Append(t[i]);
                }
                else if (n.Length > 0)
                {
                    break;
                }
            }

            if (n.Length > 0)
            {
                return n.ToString();
            }
        }

        // El último número del texto.
        var ultimo = string.Empty;
        var actual = new System.Text.StringBuilder();

        foreach (var ch in t)
        {
            if (char.IsAsciiDigit(ch))
            {
                actual.Append(ch);
            }
            else if (actual.Length > 0)
            {
                ultimo = actual.ToString();
                actual.Clear();
            }
        }

        return actual.Length > 0 ? actual.ToString() : ultimo;
    }

    /// <summary>
    /// ¿La nota o la sección de la losa dicen que es un <b>voladizo</b>?
    /// </summary>
    /// <remarks>
    /// Se pidió tal cual: el achurado <c>ANSI37</c> va <b>solo</b> en las losas cuya etiqueta
    /// de nota diga <c>VOLADO</c>. Y es lo correcto en un modelo real: el ingeniero sabe cuál
    /// es el volado y lo escribe en la propiedad, mientras que deducirlo contando lados
    /// apoyados se equivoca en cuanto una cadena viene partida en el modelo.
    /// </remarks>
    public static bool DiceVolado(string? notas, string? seccion, string palabras) =>
        PalabraVolado(notas, seccion, palabras).Length > 0;

    /// <summary>
    /// <b>Cuál</b> es la palabra que marca el voladizo: la de las <b>notas</b> primero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devuelve la palabra hallada —<c>VOLADO</c>, <c>VOLADIZO</c>…— y no solo un sí o un no,
    /// porque esa palabra es la que se <b>rotula</b>: el primer renglón del voladizo dice
    /// «Losa VOLADO», con lo que diga el modelo.
    /// </para>
    /// <para>
    /// Y se buscan <b>primero las NOTAS</b>, que es donde el ingeniero lo escribe —la
    /// propiedad de la losa en ETABS tiene su campo de notas— y solo después el nombre de la
    /// sección. Si las notas dicen VOLADIZO y la sección se llama «LOSA VOLADO», manda la
    /// nota.
    /// </para>
    /// </remarks>
    public static string PalabraVolado(string? notas, string? seccion, string palabras)
    {
        // Las notas primero, la sección después: el orden es el que se pidió.
        foreach (var donde in new[] { notas, seccion })
        {
            var texto = (donde ?? string.Empty).ToUpperInvariant();

            if (texto.Trim().Length == 0)
            {
                continue;
            }

            foreach (var palabra in palabras.Split(','))
            {
                var p = palabra.Trim().ToUpperInvariant();

                if (p.Length > 0 && texto.Contains(p, StringComparison.Ordinal))
                {
                    return p;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Los trozos de un tramo que quedan <b>fuera</b> de los muros y las cadenas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que hace que el contorno de la losa no lleve línea por dentro del muro ni de la
    /// cadena. Ahí la losa se apoya —el paño de la losa y el del muro son la misma línea— y
    /// dibujar el contorno encima deja una raya en medio del muro que se lee como una junta
    /// que no existe.
    /// </para>
    /// <para>
    /// Se calcula quitando del tramo los pedazos que caen dentro de cada huella y quedándose
    /// con los huecos. Si no queda nada fuera, el lado entero va por dentro del muro y no se
    /// dibuja, que es justo lo que se pidió.
    /// </para>
    /// </remarks>
    public static List<Segmento> TramosFuera(
        Segmento s, IReadOnlyList<ElementoPlanta> huellas, double minTramo = 0.02)
    {
        var salida = new List<Segmento>();
        var largo = s.Largo;

        if (largo < Nada)
        {
            return salida;
        }

        var ux = (s.X2 - s.X1) / largo;
        var uy = (s.Y2 - s.Y1) / largo;

        var dentro = new List<(double A, double B)>();

        foreach (var h in huellas)
        {
            foreach (var t in PanoDeApoyo.Intervalos(h, s.X1, s.Y1, ux, uy))
            {
                var a = Math.Max(0, Math.Min(t.A, t.B));
                var b = Math.Min(largo, Math.Max(t.A, t.B));

                if (b > a)
                {
                    dentro.Add((a, b));
                }
            }
        }

        var cubiertos = Unidos(dentro);

        double cursor = 0;

        foreach (var t in cubiertos)
        {
            if (t.A - cursor >= minTramo)
            {
                salida.Add(Trozo(s, ux, uy, cursor, t.A));
            }

            cursor = Math.Max(cursor, t.B);
        }

        if (largo - cursor >= minTramo)
        {
            salida.Add(Trozo(s, ux, uy, cursor, largo));
        }

        return salida;
    }

    private static Segmento Trozo(Segmento s, double ux, double uy, double a, double b) =>
        new(s.X1 + (ux * a), s.Y1 + (uy * a), s.X1 + (ux * b), s.Y1 + (uy * b));

    /// <summary>Une los tramos que se tocan o se traslapan.</summary>
    public static List<(double A, double B)> Unidos(List<(double A, double B)> tramos)
    {
        if (tramos.Count == 0)
        {
            return tramos;
        }

        tramos.Sort((p, q) => p.A.CompareTo(q.A));

        var salida = new List<(double A, double B)> { tramos[0] };

        foreach (var t in tramos.Skip(1))
        {
            var ultimo = salida[^1];

            if (t.A <= ultimo.B + Nada)
            {
                salida[^1] = (ultimo.A, Math.Max(ultimo.B, t.B));
            }
            else
            {
                salida.Add(t);
            }
        }

        return salida;
    }
}
