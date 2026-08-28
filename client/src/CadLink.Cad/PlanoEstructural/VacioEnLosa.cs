namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// Los <b>vacíos</b> de la planta: dónde no hay piso, y la <b>cruz</b> que lo dice.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió: «delimita los vacíos con líneas punteadas… de los vértices de donde se forma el
/// vacío, de ahí salen las líneas para formar la cruz, que hay vacío, o sea no hay piso». Eso es
/// la convención de siempre en un plano de losas: el hueco de la escalera, el del elevador o el
/// del ducto van con su contorno a trazos y una <b>X</b> dentro, y quien lee el plano sabe que
/// por ahí no se camina.
/// </para>
/// <para>
/// <b>El vacío no viene del modelo: se deduce.</b> En ETABS nadie modela un agujero —lo que hay
/// son shells de losa, y el hueco es <i>donde no pusieron ninguno</i>—. Así que aquí se toman
/// todos los paños del nivel y se buscan los <b>agujeros de su unión</b>: el trozo que no tiene
/// losa pero está <b>rodeado</b> de losa. Esa es justo la definición que se pidió, «no hay
/// piso», porque un agujero en el piso está rodeado de piso.
/// </para>
/// <para>
/// Y hay una consecuencia que sale gratis y que es la correcta: al descartar las losas de
/// escalera —<see cref="LosaEnPlanta.Descartar"/>—, el hueco de la escalera se queda <b>sin
/// losa</b> y aparece aquí como vacío, con su contorno a trazos y su cruz. O sea que las dos
/// cosas que se pidieron encajan: la escalera sale del plano y en su sitio queda el hueco
/// dibujado como marca la convención, que es exactamente lo que lleva un plano de losas.
/// </para>
///
/// <para><b>CÓMO SE BUSCAN LOS AGUJEROS, Y POR QUÉ ASÍ</b></para>
/// <para>
/// Unir polígonos de verdad —una operación booleana con sus agujeros— es un algoritmo largo y
/// delicado, y aquí no hace falta: los paños de losa de un modelo son <b>ortogonales y encajan
/// en la retícula</b> que forman sus propios vértices. Así que se monta esa retícula —todas las
/// X y todas las Y de todos los paños—, se pregunta por cada celda si tiene losa mirando su
/// <b>centro</b>, y ya está: la unión, sus agujeros y sus contornos salen de contar celdas.
/// </para>
/// <para>
/// El precio, dicho claro: un paño con un lado <b>en diagonal</b> se aproxima a la retícula, así
/// que el borde del vacío junto a esa diagonal queda escalonado al tamaño de la celda. Es una
/// pérdida aceptable —el vacío se dibuja para señalar, no para cotar— y a cambio el método no
/// tiene casos degenerados: no hay que resolver solapes, ni bordes coincidentes, ni polígonos
/// que se tocan en un punto, que es donde una unión booleana escrita a mano se rompe.
/// </para>
/// <para>
/// <b>Las juntas del mallado no son vacíos.</b> Un modelo real trae paños contiguos separados
/// por milímetros, y sin cuidado cada junta saldría como un vacío larguísimo y flaco que
/// llenaría el plano de cruces. Por eso las coordenadas de la retícula se <b>juntan</b> con una
/// tolerancia antes de nada: dos bordes a menos de esa distancia son el mismo borde, y entonces
/// entre ellos no queda ninguna celda. Y además se descartan los vacíos con menos área que la
/// pedida, que es la segunda red.
/// </para>
/// </remarks>
public static class VacioEnLosa
{
    /// <summary>Un vacío: sus contornos, su cruz y su área.</summary>
    /// <param name="Contornos">
    /// Los contornos cerrados, en metros, sin repetir el primer punto al final. Es una
    /// <b>lista</b> porque un vacío puede tener islas: el hueco de una escalera con su descanso
    /// en medio lleva el contorno de fuera y el del descanso, y las dos rayas hacen falta.
    /// </param>
    /// <param name="Cruz">
    /// Los trazos de la cruz. Dos —las diagonales— cuando el hueco es rectangular, que es lo
    /// normal, y más si el hueco tiene forma rara y una diagonal entra y sale de él.
    /// </param>
    /// <param name="Area">El área en metros cuadrados. Sirve para descartar astillas y avisar.</param>
    public sealed record Vacio(
        List<List<(double X, double Y)>> Contornos,
        List<(double X1, double Y1, double X2, double Y2)> Cruz,
        double Area);

    private const double Nada = 1e-9;

    /// <summary>Tope de celdas de la retícula, por encima del cual no se buscan vacíos.</summary>
    /// <remarks>
    /// Es una <b>válvula</b>, no un ajuste de calidad. La retícula la ponen los vértices de los
    /// paños, así que un nivel con el mallado muy fino puede pedir una de miles por miles y el
    /// coste va con el producto. Un millón de celdas se resuelve en un instante; cien millones
    /// no se resuelven. Si se llega aquí, lo que hay que subir es <c>VACIO_TOL_CM</c>, que junta
    /// los bordes casi iguales y baja la retícula de golpe.
    /// </remarks>
    public const long MaximoDeCeldas = 4_000_000;

    /// <summary>
    /// Los vacíos que dejan los paños de losa: lo que no tiene piso y está rodeado de piso.
    /// </summary>
    /// <param name="panos">Los contornos de las losas del nivel, en metros.</param>
    /// <param name="tol">
    /// Con cuánto se juntan dos coordenadas de la retícula, en metros. Es lo que hace que la
    /// junta del mallado no sea un vacío.
    /// </param>
    /// <summary>
    /// Cuántas celdas pediría la retícula de estos paños. Sirve para <b>avisar</b> antes.
    /// </summary>
    /// <remarks>
    /// Existe para que quien dibuja pueda decir <i>por qué</i> no se buscaron los vacíos cuando
    /// la retícula se pasa de <see cref="MaximoDeCeldas"/>. <see cref="Detectar"/> devolvería una
    /// lista vacía, que es lo mismo que devuelve cuando no hay ningún hueco, y no son la misma
    /// cosa: en un caso no hay nada que marcar y en el otro hay que subir la tolerancia.
    /// </remarks>
    public static long CeldasQueHacenFalta(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> panos, double tol)
    {
        var buenos = panos.Where(v => v is not null && v.Count >= 3).ToList();

        if (buenos.Count == 0)
        {
            return 0;
        }

        var xs = Juntar(buenos.SelectMany(v => v.Select(p => p.X)), tol);
        var ys = Juntar(buenos.SelectMany(v => v.Select(p => p.Y)), tol);

        return (long)Math.Max(0, xs.Count - 1) * Math.Max(0, ys.Count - 1);
    }

    /// <param name="areaMinima">
    /// Área por debajo de la cual un vacío no se dibuja, en metros cuadrados. La segunda red
    /// contra las astillas que deja el mallado.
    /// </param>
    public static List<Vacio> Detectar(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> panos,
        double tol,
        double areaMinima)
    {
        var salida = new List<Vacio>();

        var buenos = panos.Where(v => v is not null && v.Count >= 3).ToList();

        if (buenos.Count == 0)
        {
            return salida;
        }

        // ---------- La retícula ----------
        var xs = Juntar(buenos.SelectMany(v => v.Select(p => p.X)), tol);
        var ys = Juntar(buenos.SelectMany(v => v.Select(p => p.Y)), tol);

        // Con menos de tres líneas en un sentido no cabe ninguna celda rodeada por los dos
        // lados, así que no hay nada que buscar.
        if (xs.Count < 3 || ys.Count < 3)
        {
            return salida;
        }

        var nx = xs.Count - 1;
        var ny = ys.Count - 1;

        // Un modelo con el mallado muy fino puede dar una retícula enorme —cada nudo del mesh
        // mete una línea en cada sentido—, y el coste va con el PRODUCTO de las dos. Antes de
        // reservar nada se comprueba, porque pasado cierto tamaño esto no tarda: se cuelga.
        // Quien llama se entera por el conteo en cero y por su propio aviso.
        if ((long)nx * ny > MaximoDeCeldas)
        {
            return salida;
        }

        // ---------- ¿Qué celda tiene losa? ----------
        //
        // Se pregunta por el CENTRO de la celda y no por sus esquinas: una esquina cae justo
        // sobre el borde de los paños —de ahí salió la retícula— y ahí «dentro o fuera» es
        // indeterminado. El centro está estrictamente dentro o estrictamente fuera.
        //
        // Y SE RECORRE POR PAÑO, no por celda. Recorriendo por celda y preguntando a todos los
        // paños, un nivel con 400 shells y una retícula de 400×400 son 64 millones de pruebas
        // de punto en polígono: eso no es lento, es que no acaba. Por paño, cada uno solo mira
        // las celdas de SU caja, y el trabajo total es del orden del número de celdas.
        var conLosa = new bool[nx, ny];

        foreach (var v in buenos)
        {
            var iDesde = Math.Max(0, Tramo(xs, v.Min(p => p.X)));
            var iHasta = Math.Min(nx - 1, Tramo(xs, v.Max(p => p.X)));
            var jDesde = Math.Max(0, Tramo(ys, v.Min(p => p.Y)));
            var jHasta = Math.Min(ny - 1, Tramo(ys, v.Max(p => p.Y)));

            for (var i = iDesde; i <= iHasta; i++)
            {
                var cx = (xs[i] + xs[i + 1]) / 2;

                for (var j = jDesde; j <= jHasta; j++)
                {
                    if (conLosa[i, j])
                    {
                        continue;
                    }

                    var cy = (ys[j] + ys[j + 1]) / 2;

                    if (TableroDeLosa.Dentro(v, cx, cy))
                    {
                        conLosa[i, j] = true;
                    }
                }
            }
        }

        // ---------- Lo de FUERA, desde la orilla ----------
        //
        // Aquí está la diferencia entre un agujero y una escotadura. Una planta en L tiene una
        // esquina sin losa, pero esa esquina NO es un vacío: se sale del edificio. Se distingue
        // por si se puede llegar a ella desde la orilla del dibujo sin pisar losa. Lo que queda
        // sin alcanzar está rodeado de losa, y eso sí es un agujero en el piso.
        var fuera = DeFuera(conLosa, nx, ny);

        // ---------- Cada agujero, por separado ----------
        var visto = new bool[nx, ny];

        for (var i0 = 0; i0 < nx; i0++)
        {
            for (var j0 = 0; j0 < ny; j0++)
            {
                if (conLosa[i0, j0] || fuera[i0, j0] || visto[i0, j0])
                {
                    continue;
                }

                var celdas = Mancha(conLosa, fuera, visto, nx, ny, i0, j0);

                var area = celdas.Sum(c =>
                    (xs[c.I + 1] - xs[c.I]) * (ys[c.J + 1] - ys[c.J]));

                if (area < areaMinima)
                {
                    continue;
                }

                var contornos = Contornos(celdas, xs, ys);

                if (contornos.Count == 0)
                {
                    continue;
                }

                salida.Add(new Vacio(contornos, Cruz(celdas, xs, ys), area));
            }
        }

        // De mayor a menor: es el orden en que se querría leer un aviso, y hace que el
        // resultado no dependa de por qué esquina empezó el barrido.
        return salida.OrderByDescending(v => v.Area).ToList();
    }

    // =================================================================================
    //  LA RETÍCULA
    // =================================================================================

    /// <summary>
    /// Las coordenadas ordenadas, <b>juntando</b> las que están a menos de <paramref name="tol"/>.
    /// </summary>
    /// <remarks>
    /// Es lo que borra las juntas del mallado: si dos paños contiguos traen su borde común a
    /// medio centímetro, entre ellos no queda ninguna celda y la junta no es un vacío.
    /// </remarks>
    private static List<double> Juntar(IEnumerable<double> valores, double tol)
    {
        var t = Math.Max(tol, Nada);

        var salida = new List<double>();

        foreach (var v in valores.OrderBy(v => v))
        {
            if (salida.Count == 0 || v - salida[^1] > t)
            {
                salida.Add(v);
            }
        }

        return salida;
    }

    /// <summary>
    /// Entre qué dos líneas de la retícula cae una coordenada, <b>por bisección</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devuelve el índice del tramo, o <c>-1</c> si el valor se sale de la retícula. Se busca por
    /// bisección y no recorriendo: esto se llama una vez por cada trozo en que se parte cada
    /// diagonal de cada vacío, y recorriendo la lista el recorte de la cruz pasaría a costar el
    /// cuadrado del tamaño de la retícula.
    /// </para>
    /// <para>
    /// Un valor que caiga <b>justo sobre una línea</b> devuelve el tramo de abajo. No importa
    /// para lo que se usa —siempre se pregunta por puntos medios, que están estrictamente
    /// dentro— pero conviene que sea una regla y no una casualidad.
    /// </para>
    /// </remarks>
    private static int Tramo(List<double> lineas, double valor)
    {
        if (lineas.Count < 2 || valor < lineas[0] || valor > lineas[^1])
        {
            return -1;
        }

        var bajo = 0;
        var alto = lineas.Count - 1;

        while (alto - bajo > 1)
        {
            var medio = (bajo + alto) / 2;

            if (valor < lineas[medio])
            {
                alto = medio;
            }
            else
            {
                bajo = medio;
            }
        }

        return bajo;
    }

    // =================================================================================
    //  QUÉ ESTÁ FUERA Y QUÉ ES UN AGUJERO
    // =================================================================================

    /// <summary>Las celdas sin losa a las que se llega <b>desde la orilla</b>.</summary>
    /// <remarks>
    /// Se avanza en cruz —arriba, abajo, izquierda y derecha— y <b>no en diagonal</b>. En
    /// diagonal, dos celdas de losa que se tocan solo por una esquina dejarían pasar el relleno
    /// por ese punto, y un patio cerrado se leería como si estuviera abierto al exterior.
    /// </remarks>
    private static bool[,] DeFuera(bool[,] conLosa, int nx, int ny)
    {
        var fuera = new bool[nx, ny];
        var cola = new Queue<(int I, int J)>();

        void Sembrar(int i, int j)
        {
            if (conLosa[i, j] || fuera[i, j])
            {
                return;
            }

            fuera[i, j] = true;
            cola.Enqueue((i, j));
        }

        for (var i = 0; i < nx; i++)
        {
            Sembrar(i, 0);
            Sembrar(i, ny - 1);
        }

        for (var j = 0; j < ny; j++)
        {
            Sembrar(0, j);
            Sembrar(nx - 1, j);
        }

        while (cola.Count > 0)
        {
            var (i, j) = cola.Dequeue();

            foreach (var (di, dj) in Cruceta)
            {
                var a = i + di;
                var b = j + dj;

                if (a >= 0 && a < nx && b >= 0 && b < ny)
                {
                    Sembrar(a, b);
                }
            }
        }

        return fuera;
    }

    private static readonly (int I, int J)[] Cruceta =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1)
    };

    /// <summary>Las celdas de <b>un</b> agujero, a partir de una suya.</summary>
    private static List<(int I, int J)> Mancha(
        bool[,] conLosa, bool[,] fuera, bool[,] visto, int nx, int ny, int i0, int j0)
    {
        var celdas = new List<(int I, int J)>();
        var cola = new Queue<(int I, int J)>();

        visto[i0, j0] = true;
        cola.Enqueue((i0, j0));

        while (cola.Count > 0)
        {
            var c = cola.Dequeue();

            celdas.Add(c);

            foreach (var (di, dj) in Cruceta)
            {
                var a = c.I + di;
                var b = c.J + dj;

                if (a < 0 || a >= nx || b < 0 || b >= ny
                    || conLosa[a, b] || fuera[a, b] || visto[a, b])
                {
                    continue;
                }

                visto[a, b] = true;
                cola.Enqueue((a, b));
            }
        }

        return celdas;
    }

    // =================================================================================
    //  EL CONTORNO DE UN AGUJERO
    // =================================================================================

    /// <summary>
    /// Los contornos cerrados de un grupo de celdas: el de fuera y los de sus islas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se toman los lados de celda que <b>no</b> comparte con otra celda del mismo grupo —esos
    /// son el borde— y se van encadenando por sus extremos hasta cerrar. Los extremos se
    /// comparan por <b>índice de retícula</b> y no en metros: así dos lados que comparten un
    /// punto lo comparten exacto, sin depender de decimales.
    /// </para>
    /// <para>
    /// Al encadenar se prefiere <b>seguir recto</b>. En un punto donde el borde se cruza consigo
    /// mismo —dos partes del vacío que se tocan solo por una esquina— salen cuatro lados del
    /// mismo punto, y girando se cerraría un lazo pequeño dejando el resto suelto. Siguiendo
    /// recto el contorno pasa de largo por el cruce, que es como se dibujaría a mano.
    /// </para>
    /// </remarks>
    private static List<List<(double X, double Y)>> Contornos(
        List<(int I, int J)> celdas, List<double> xs, List<double> ys)
    {
        var dentro = new HashSet<(int, int)>(celdas);

        var lados = new List<((int I, int J) A, (int I, int J) B)>();

        foreach (var (i, j) in celdas)
        {
            if (!dentro.Contains((i, j - 1)))
            {
                lados.Add(((i, j), (i + 1, j)));
            }

            if (!dentro.Contains((i, j + 1)))
            {
                lados.Add(((i, j + 1), (i + 1, j + 1)));
            }

            if (!dentro.Contains((i - 1, j)))
            {
                lados.Add(((i, j), (i, j + 1)));
            }

            if (!dentro.Contains((i + 1, j)))
            {
                lados.Add(((i + 1, j), (i + 1, j + 1)));
            }
        }

        var indice = new Dictionary<((int, int), (int, int)), int>();

        for (var k = 0; k < lados.Count; k++)
        {
            indice[Clave(lados[k].A, lados[k].B)] = k;
        }

        // De cada punto, a qué puntos va.
        var vecinos = new Dictionary<(int, int), List<(int, int)>>();

        void Anotar((int, int) a, (int, int) b)
        {
            if (!vecinos.TryGetValue(a, out var lista))
            {
                lista = new List<(int, int)>();
                vecinos[a] = lista;
            }

            lista.Add(b);
        }

        foreach (var (a, b) in lados)
        {
            Anotar(a, b);
            Anotar(b, a);
        }

        var usados = new HashSet<int>();
        var salida = new List<List<(double X, double Y)>>();

        foreach (var (a0, b0) in lados)
        {
            if (usados.Contains(indice[Clave(a0, b0)]))
            {
                continue;
            }

            var lazo = new List<(int I, int J)> { a0 };

            var actual = a0;
            var siguiente = b0;

            while (true)
            {
                var k = indice[Clave(actual, siguiente)];

                if (usados.Contains(k))
                {
                    break;
                }

                usados.Add(k);
                lazo.Add(siguiente);

                if (siguiente == a0)
                {
                    break;
                }

                var previo = actual;

                actual = siguiente;

                var opciones = vecinos[actual]
                    .Where(v => v != previo && !usados.Contains(indice[Clave(actual, v)]))
                    .ToList();

                if (opciones.Count == 0)
                {
                    break;
                }

                // Se sigue RECTO si se puede: es lo que evita cerrar un lazo corto en un cruce.
                var dI = actual.I - previo.I;
                var dJ = actual.J - previo.J;

                siguiente = opciones.FirstOrDefault(
                    v => v.Item1 - actual.I == dI && v.Item2 - actual.J == dJ,
                    opciones[0]);
            }

            // El lazo cierra repitiendo el primer punto: se quita, y hacen falta cuatro puntos
            // —tres lados y el cierre— para encerrar algo.
            if (lazo.Count >= 4 && lazo[^1] == lazo[0])
            {
                lazo.RemoveAt(lazo.Count - 1);

                var limpio = SinLosDePaso(lazo, xs, ys);

                if (limpio.Count >= 3)
                {
                    salida.Add(limpio);
                }
            }
        }

        return salida;
    }

    /// <summary>El mismo lado en los dos sentidos da la misma clave.</summary>
    private static ((int, int), (int, int)) Clave((int I, int J) a, (int I, int J) b) =>
        (a.I, a.J).CompareTo((b.I, b.J)) <= 0
            ? ((a.I, a.J), (b.I, b.J))
            : ((b.I, b.J), (a.I, a.J));

    /// <summary>
    /// El contorno en metros, <b>sin los vértices de paso</b>.
    /// </summary>
    /// <remarks>
    /// El trazado va celda a celda, así que un lado recto de cuatro celdas llega con cinco
    /// puntos y tres de ellos no doblan. Se quitan porque lo que interesa son los
    /// <b>vértices</b>: son los que se pidió que marquen el vacío, y con los de paso dentro la
    /// polilínea llevaría el triple de puntos sin dibujar nada distinto.
    /// </remarks>
    private static List<(double X, double Y)> SinLosDePaso(
        List<(int I, int J)> lazo, List<double> xs, List<double> ys)
    {
        var salida = new List<(double X, double Y)>();

        for (var k = 0; k < lazo.Count; k++)
        {
            var previo = lazo[(k - 1 + lazo.Count) % lazo.Count];
            var punto = lazo[k];
            var siguiente = lazo[(k + 1) % lazo.Count];

            var entra = (punto.I - previo.I, punto.J - previo.J);
            var sale = (siguiente.I - punto.I, siguiente.J - punto.J);

            if (entra != sale)
            {
                salida.Add((xs[punto.I], ys[punto.J]));
            }
        }

        return salida;
    }

    // =================================================================================
    //  LA CRUZ
    // =================================================================================

    /// <summary>La <b>cruz</b> del vacío: sus diagonales, recortadas a lo que es vacío.</summary>
    /// <remarks>
    /// <para>
    /// En el caso normal —un hueco rectangular— las dos diagonales van de <b>vértice a vértice
    /// opuesto</b>, que es lo que se pidió: la X de esquina a esquina.
    /// </para>
    /// <para>
    /// Cuando el hueco no es un rectángulo, la diagonal de su caja se sale por fuera, y una raya
    /// cruzando la losa de al lado diría que ahí no hay piso cuando sí lo hay. Así que se
    /// <b>recorta</b>, y se recorta contra las <b>celdas</b> y no contra el contorno: contra el
    /// contorno de fuera, un hueco con una isla de losa en medio —una escalera con su descanso—
    /// dejaría la cruz pintada encima del descanso, que es piso. Con las celdas eso no puede
    /// pasar, porque la isla no es celda del vacío.
    /// </para>
    /// <para>
    /// El recorte es exacto y no necesita cortar rectas contra polígonos: la diagonal solo puede
    /// cambiar de celda al cruzar una línea de la retícula, así que basta partirla en esas
    /// líneas y quedarse con los trozos cuyo centro cae en una celda del vacío.
    /// </para>
    /// </remarks>
    private static List<(double X1, double Y1, double X2, double Y2)> Cruz(
        List<(int I, int J)> celdas, List<double> xs, List<double> ys)
    {
        var dentro = new HashSet<(int, int)>(celdas);

        var iMin = celdas.Min(c => c.I);
        var iMax = celdas.Max(c => c.I);
        var jMin = celdas.Min(c => c.J);
        var jMax = celdas.Max(c => c.J);

        // Las esquinas de la caja del vacío. En un hueco rectangular son SUS vértices, así que
        // la cruz va de esquina a esquina, tal como se pidió.
        var xa = xs[iMin];
        var xb = xs[iMax + 1];
        var ya = ys[jMin];
        var yb = ys[jMax + 1];

        var cruz = new List<(double, double, double, double)>();

        cruz.AddRange(Recortar(dentro, xs, ys, xa, ya, xb, yb));
        cruz.AddRange(Recortar(dentro, xs, ys, xa, yb, xb, ya));

        return cruz;
    }

    /// <summary>Los trozos del segmento que caen en una celda del vacío, ya unidos.</summary>
    private static List<(double X1, double Y1, double X2, double Y2)> Recortar(
        HashSet<(int, int)> dentro, List<double> xs, List<double> ys,
        double xa, double ya, double xb, double yb)
    {
        var dx = xb - xa;
        var dy = yb - ya;

        // Los parámetros donde la diagonal cruza una línea de la retícula. Ahí, y solo ahí,
        // puede cambiar de celda.
        var cortes = new List<double> { 0, 1 };

        if (Math.Abs(dx) > Nada)
        {
            foreach (var x in xs)
            {
                var t = (x - xa) / dx;

                if (t > Nada && t < 1 - Nada)
                {
                    cortes.Add(t);
                }
            }
        }

        if (Math.Abs(dy) > Nada)
        {
            foreach (var y in ys)
            {
                var t = (y - ya) / dy;

                if (t > Nada && t < 1 - Nada)
                {
                    cortes.Add(t);
                }
            }
        }

        cortes.Sort();

        var salida = new List<(double, double, double, double)>();

        // Se acumulan los trozos buenos SEGUIDOS en una sola línea: una diagonal que cruza seis
        // celdas del vacío es una raya, no seis rayas pegadas. Con seis, el DASHDOT reinicia su
        // patrón en cada una y la cruz se ve a parches.
        double? desde = null;
        double hasta = 0;

        for (var k = 0; k + 1 < cortes.Count; k++)
        {
            var t1 = cortes[k];
            var t2 = cortes[k + 1];

            if (t2 - t1 < 1e-9)
            {
                continue;
            }

            var tm = (t1 + t2) / 2;

            var i = Tramo(xs, xa + (dx * tm));
            var j = Tramo(ys, ya + (dy * tm));

            var bueno = i >= 0 && j >= 0 && dentro.Contains((i, j));

            if (bueno)
            {
                desde ??= t1;
                hasta = t2;

                continue;
            }

            if (desde is not null)
            {
                salida.Add(Trozo(xa, ya, dx, dy, desde.Value, hasta));
                desde = null;
            }
        }

        if (desde is not null)
        {
            salida.Add(Trozo(xa, ya, dx, dy, desde.Value, hasta));
        }

        return salida;
    }

    private static (double, double, double, double) Trozo(
        double xa, double ya, double dx, double dy, double t1, double t2) =>
        (xa + (dx * t1), ya + (dy * t1), xa + (dx * t2), ya + (dy * t2));
}
