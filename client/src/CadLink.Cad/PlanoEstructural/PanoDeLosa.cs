namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El paño <b>de verdad</b> de una losa: su contorno metido hasta la <b>cara</b> del muro.
/// </summary>
/// <remarks>
/// <para>
/// En el modelo la losa se dibuja hasta el <b>eje</b> del muro o de la cadena, porque ahí es
/// donde están los nudos. Pero el concreto de la losa no llega al eje: llega al <b>paño</b>, y
/// medio espesor antes ya es muro. Achurar hasta el eje mete el rayado por dentro de la
/// cadena, que es lo que se pidió quitar.
/// </para>
/// <para>
/// Aquí se resuelve metiendo cada lado del contorno hacia dentro <b>medio espesor</b> del muro
/// que corre sobre él, y volviendo a cerrar las esquinas cortando los lados movidos. Es lo
/// mismo que hace un albañil cuando replantea: la línea de la losa se mide al paño.
/// </para>
/// <para>
/// Es <b>pura aritmética</b> y está aparte del dibujante para poder comprobarla sin AutoCAD.
/// </para>
/// </remarks>
public static class PanoDeLosa
{
    /// <summary>Tolerancia de colinealidad, en metros.</summary>
    private const double Junto = 0.02;

    /// <summary>
    /// El contorno de la losa <b>metido al paño</b> de los muros que lo llevan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada lado se prueba contra las huellas: si una corre <b>a lo largo</b> del lado —o sea,
    /// el muro está debajo de esa orilla de la losa— el lado se mete hacia el interior medio
    /// ancho de esa huella. Los lados que dan al aire —el borde libre de un voladizo— no se
    /// tocan: ahí la losa termina donde dice el modelo.
    /// </para>
    /// <para>
    /// <b>Y se mete SOLO EL TROZO QUE TIENE MURO DEBAJO, no el lado entero.</b> Esto se
    /// corrigió, y era lo que dejaba el achurado del voladizo separado de la línea de la losa:
    /// un lado de seis metros con una cadena de dos se metía completo, así que en los cuatro
    /// metros libres el rayado se quedaba a siete centímetros y medio de la línea dibujada, con
    /// una franja en blanco entre los dos. Ahora el contorno del molde <b>escalona</b>: va por la
    /// cara de la cadena donde hay cadena y por la línea de la losa donde no la hay, que es
    /// justo lo que se pidió —el hatch hasta el paño de la losa—.
    /// </para>
    /// <para>
    /// Las esquinas se recalculan <b>cortando</b> los dos lados movidos, no moviendo los
    /// vértices uno a uno: si se movieran los vértices, un lado con muro y otro sin muro
    /// dejarían la esquina abierta o cruzada. Y si dos lados salen paralelos —una losa con un
    /// pico— se deja el vértice de en medio, que es lo que menos deforma.
    /// </para>
    /// </remarks>
    /// <param name="vertices">El contorno de la losa, en orden y cerrado.</param>
    /// <param name="huellas">Las huellas de muros y cadenas, las mismas del recorte.</param>
    /// <param name="maximo">
    /// Cuánto se admite meter un lado, en metros. Es una válvula: una huella enorme mal leída
    /// no puede comerse la losa.
    /// </param>
    public static List<(double X, double Y)> AlPano(
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<ElementoPlanta> huellas,
        double maximo = 0.6)
    {
        var n = vertices.Count;

        if (n < 3)
        {
            return vertices.ToList();
        }

        // El centro sirve para saber hacia dónde es «adentro».
        var cx = vertices.Average(v => v.X);
        var cy = vertices.Average(v => v.Y);

        // Cada lado con sus TRAMOS: los pedazos en que se parte según lo que tenga debajo.
        var lados = new List<Lado>(n);

        for (var i = 0; i < n; i++)
        {
            lados.Add(DelLado(vertices[i], vertices[(i + 1) % n], huellas, maximo, cx, cy));
        }

        // ---------- Las esquinas ----------
        //
        // Cortando la recta del ÚLTIMO tramo de cada lado con la del PRIMERO del siguiente. Con
        // los lados enteros esto es lo de siempre; con tramos, lo que se corta es el pedazo que
        // de verdad llega a la esquina, que es lo correcto.
        var esquinas = new (double X, double Y)[n];

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var corte = Cruce(lados[i].Tramos[^1].Recta, lados[j].Tramos[0].Recta);

            // Sin corte —lados paralelos— se queda el vértice original, que es lo que menos
            // deforma el contorno.
            esquinas[i] = corte ?? vertices[j];
        }

        // ---------- Y el contorno, tramo a tramo ----------
        var salida = new List<(double X, double Y)>();

        for (var i = 0; i < n; i++)
        {
            var lado = lados[i];

            for (var k = 0; k < lado.Tramos.Count; k++)
            {
                var t = lado.Tramos[k];

                // El arranque del primer tramo y el final del último son las esquinas, ya
                // cortadas. Los de en medio van sobre el propio lado, y ahí el salto entre un
                // tramo y el siguiente es lo que ESCALONA el contorno: sube a la cara de la
                // cadena y vuelve a bajar a la línea de la losa.
                Poner(salida, k == 0
                    ? esquinas[(i - 1 + n) % n]
                    : lado.En(t.Desde, t.Mete));

                Poner(salida, k == lado.Tramos.Count - 1
                    ? esquinas[i]
                    : lado.En(t.Hasta, t.Mete));
            }
        }

        // Y si al cerrar el último punto repite el primero, se quita.
        if (salida.Count > 1 && Pegados(salida[0], salida[^1]))
        {
            salida.RemoveAt(salida.Count - 1);
        }

        return salida;
    }

    /// <summary>Un lado del contorno, ya partido en tramos.</summary>
    private sealed record Lado(
        (double X, double Y) A,
        double Dx, double Dy, double Nx, double Ny,
        List<Tramo> Tramos)
    {
        /// <summary>El punto del lado a <paramref name="s"/> metros, metido <paramref name="mete"/>.</summary>
        public (double X, double Y) En(double s, double mete) =>
            (A.X + (Dx * s) + (Nx * mete), A.Y + (Dy * s) + (Ny * mete));
    }

    /// <summary>Un pedazo de lado con lo que hay que meterlo, y su recta ya movida.</summary>
    private sealed record Tramo(
        double Desde, double Hasta, double Mete,
        (double X, double Y, double Dx, double Dy) Recta);

    /// <summary>Añade el punto si no está pegado al anterior.</summary>
    private static void Poner(List<(double X, double Y)> lista, (double X, double Y) p)
    {
        if (lista.Count == 0 || !Pegados(lista[^1], p))
        {
            lista.Add(p);
        }
    }

    private static bool Pegados((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

    /// <summary>
    /// Un lado partido en tramos según <b>cuánto muro</b> tiene debajo en cada pedazo.
    /// </summary>
    /// <remarks>
    /// Los cortes son los extremos de las huellas proyectados sobre el lado. En cada pedazo que
    /// queda entre dos cortes manda la huella <b>más ancha</b> de las que lo cubren: es la que
    /// más adentro deja el paño, y con dos muros encimados hay que respetar el peor.
    /// </remarks>
    private static Lado DelLado(
        (double X, double Y) a, (double X, double Y) b,
        IReadOnlyList<ElementoPlanta> huellas, double maximo, double cx, double cy)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            // Un lado de largo cero no tiene dirección: se deja quieto y no estorba.
            return new Lado(a, 1, 0, 0, 0,
                new List<Tramo> { new(0, 0, 0, (a.X, a.Y, 1, 0)) });
        }

        dx /= largo;
        dy /= largo;

        // La normal que apunta HACIA DENTRO: se prueba con el centro del paño.
        var nx = -dy;
        var ny = dx;

        var mx = (a.X + b.X) / 2;
        var my = (a.Y + b.Y) / 2;

        if (((cx - mx) * nx) + ((cy - my) * ny) < 0)
        {
            nx = -nx;
            ny = -ny;
        }

        var lado = new Lado(a, dx, dy, nx, ny, new List<Tramo>());

        // ---------- Qué huella cubre qué pedazo ----------
        var cubren = new List<(double Desde, double Hasta, double Mete)>();

        foreach (var h in huellas)
        {
            var ang = h.AnguloGrados * Math.PI / 180;
            var hx = Math.Cos(ang);
            var hy = Math.Sin(ang);

            // ¿Paralela al lado? Un muro perpendicular que solo cruza la orilla no está
            // DEBAJO de ella, así que no mete nada.
            if (Math.Abs((hx * dx) + (hy * dy)) < 0.98)
            {
                continue;
            }

            // ¿Su eje cae encima del lado?
            var ex = h.X1 - a.X;
            var ey = h.Y1 - a.Y;

            if (Math.Abs((ex * -dy) + (ey * dx)) > (h.PeralteM / 2) + Junto)
            {
                continue;
            }

            // Hasta dónde llega A LO LARGO del lado. X1,Y1 de una huella es su CENTRO y AnchoM
            // su largo, así que se extiende medio a cada lado.
            var centro = (ex * dx) + (ey * dy);

            var desde = Math.Max(0, centro - (h.AnchoM / 2));
            var hasta = Math.Min(largo, centro + (h.AnchoM / 2));

            if (hasta - desde <= Junto)
            {
                continue;
            }

            cubren.Add((desde, hasta, Math.Min(h.PeralteM / 2, maximo)));
        }

        if (cubren.Count == 0)
        {
            lado.Tramos.Add(new Tramo(0, largo, 0, (a.X, a.Y, dx, dy)));

            return lado;
        }

        // ---------- Los cortes ----------
        var cortes = new List<double> { 0, largo };

        foreach (var (desde, hasta, _) in cubren)
        {
            cortes.Add(desde);
            cortes.Add(hasta);
        }

        cortes.Sort();

        for (var i = 0; i + 1 < cortes.Count; i++)
        {
            var desde = cortes[i];
            var hasta = cortes[i + 1];

            // Los pedazos de nada que deja ordenar los cortes no son tramos.
            if (hasta - desde <= 1e-9)
            {
                continue;
            }

            var medio = (desde + hasta) / 2;

            var mete = 0d;

            foreach (var c in cubren)
            {
                if (medio >= c.Desde && medio <= c.Hasta)
                {
                    mete = Math.Max(mete, c.Mete);
                }
            }

            var p = lado.En(0, mete);

            // Dos pedazos seguidos con el mismo metido son UN tramo: así el contorno no lleva
            // vértices de más ni escalones de altura cero.
            if (lado.Tramos.Count > 0 && Math.Abs(lado.Tramos[^1].Mete - mete) < 1e-9)
            {
                lado.Tramos[^1] = lado.Tramos[^1] with { Hasta = hasta };

                continue;
            }

            lado.Tramos.Add(new Tramo(desde, hasta, mete, (p.X, p.Y, dx, dy)));
        }

        if (lado.Tramos.Count == 0)
        {
            lado.Tramos.Add(new Tramo(0, largo, 0, (a.X, a.Y, dx, dy)));
        }

        return lado;
    }

    /// <summary>
    /// <b>Medio ancho</b> del muro que corre a lo largo de este lado, o 0 si no hay ninguno.
    /// </summary>
    /// <remarks>
    /// Se mira que la huella sea <b>paralela</b> al lado y que su eje caiga <b>encima</b> del
    /// lado: un muro perpendicular que solo toca la orilla no cuenta, porque no está debajo de
    /// esa orilla. Si hay varios, manda el más ancho: es el que más adentro deja el paño.
    /// </remarks>
    public static double MedioAnchoDelMuro(
        (double X, double Y) a, (double X, double Y) b,
        IReadOnlyList<ElementoPlanta> huellas)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9 || huellas.Count == 0)
        {
            return 0;
        }

        dx /= largo;
        dy /= largo;

        var medio = 0d;

        foreach (var h in huellas)
        {
            // El eje de la huella: su largo va en AnchoM y su grosor en PeralteM, con su giro.
            var ang = h.AnguloGrados * Math.PI / 180;
            var hx = Math.Cos(ang);
            var hy = Math.Sin(ang);

            // ¿Paralela al lado? El valor absoluto del producto punto de las dos direcciones
            // vale 1 cuando lo son, sin importar el sentido.
            if (Math.Abs((hx * dx) + (hy * dy)) < 0.98)
            {
                continue;
            }

            // ¿Su eje cae ENCIMA del lado? Se mide la distancia del centro de la huella a la
            // recta del lado.
            var ex = h.X1 - a.X;
            var ey = h.Y1 - a.Y;

            var fuera = Math.Abs((ex * -dy) + (ey * dx));

            if (fuera > (h.PeralteM / 2) + Junto)
            {
                continue;
            }

            // Y que el trozo de muro esté a lo largo de este lado, no en la prolongación.
            var sobre = (ex * dx) + (ey * dy);

            if (sobre < -h.AnchoM / 2 || sobre > largo + (h.AnchoM / 2))
            {
                continue;
            }

            medio = Math.Max(medio, h.PeralteM / 2);
        }

        return medio;
    }

    /// <summary>Dónde se cortan dos rectas dadas por punto y dirección, o nulo si son paralelas.</summary>
    private static (double X, double Y)? Cruce(
        (double X, double Y, double Dx, double Dy) r,
        (double X, double Y, double Dx, double Dy) s)
    {
        var det = (r.Dx * -s.Dy) - (r.Dy * -s.Dx);

        if (Math.Abs(det) < 1e-9)
        {
            return null;
        }

        var bx = s.X - r.X;
        var by = s.Y - r.Y;

        var t = ((bx * -s.Dy) - (by * -s.Dx)) / det;

        return (r.X + (r.Dx * t), r.Y + (r.Dy * t));
    }

    /// <summary>
    /// ¿Este tramo del contorno lo <b>comparte</b> con otra losa?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sirve para lo que se pidió: que dos voladizos pegados se vean como <b>un solo paño</b>,
    /// con un perímetro y sin la raya del medio. Esa raya es la orilla que las dos losas
    /// comparten, y en la obra no existe: el concreto es continuo.
    /// </para>
    /// <para>
    /// Se reconoce porque el tramo cae <b>encima de un lado</b> de la otra losa, con su misma
    /// dirección. No se compara vértice a vértice a propósito: dos losas contiguas pueden
    /// tener sus vértices en sitios distintos —una partida en dos por un eje— y compartir
    /// igualmente la orilla.
    /// </para>
    /// </remarks>
    public static bool ContornoCompartido(
        LosaEnPlanta.Segmento tramo,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> otras)
    {
        var dx = tramo.X2 - tramo.X1;
        var dy = tramo.Y2 - tramo.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            return false;
        }

        dx /= largo;
        dy /= largo;

        // El punto medio del tramo: si ese cae sobre un lado de la otra losa, el tramo es
        // compartido. Con el punto medio basta y sobra, y no se confunde con una esquina que
        // apenas se toca.
        var mx = (tramo.X1 + tramo.X2) / 2;
        var my = (tramo.Y1 + tramo.Y2) / 2;

        foreach (var otra in otras)
        {
            foreach (var lado in LosaEnPlanta.Lados(otra))
            {
                var ex = lado.X2 - lado.X1;
                var ey = lado.Y2 - lado.Y1;
                var l = Math.Sqrt((ex * ex) + (ey * ey));

                if (l < 1e-9)
                {
                    continue;
                }

                ex /= l;
                ey /= l;

                // Misma dirección —en un sentido o en el otro— y el punto medio encima.
                if (Math.Abs((ex * dx) + (ey * dy)) < 0.98)
                {
                    continue;
                }

                var vx = mx - lado.X1;
                var vy = my - lado.Y1;

                var fuera = Math.Abs((vx * -ey) + (vy * ex));
                var sobre = (vx * ex) + (vy * ey);

                if (fuera <= Junto && sobre >= -Junto && sobre <= l + Junto)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
