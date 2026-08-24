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
