namespace CadLink.Revit.Nucleo;

/// <summary>
/// Une en <b>una sola losa</b> los tableros que se tocan, del mismo nivel, la misma cota y la
/// misma seccion.
/// </summary>
/// <remarks>
/// <para>
/// ETABS parte la losa en tableros -uno por cada cuadro entre trabes o muros- y cada uno llegaba
/// a Revit como un suelo aparte, con su orilla en el EJE de la trabe interior. En la obra eso no
/// existe: la losa se cuela de una pieza, y en Revit se modela continua sobre las trabes y a paño
/// en sus orillas, igual que el plano de AutoCAD la dibuja como un solo paño sin la raya del medio
/// (<c>PanoDeLosa.ContornoCompartido</c>).
/// </para>
/// <para>
/// Despues de unirlas, <see cref="LosasAPano"/> lleva TODAS sus orillas al paño: ya no queda
/// ningun lado en el eje, porque los lados que se compartian desaparecen.
/// </para>
/// <para>
/// La union se hace por <b>cancelacion de lados</b>: cada tablero en sentido antihorario, cada lado
/// partido donde le cae un vertice de otro, y los lados que aparecen dos veces en sentido contrario
/// -los compartidos- se quitan. Lo que queda se encadena en el contorno de fuera y, si los tableros
/// rodean un vacio (una escalera), en sus huecos.
/// </para>
/// <para>
/// <b>Si algo no cuadra no se une</b> y los tableros se quedan como venian: tableros encimados, que
/// se tocan solo por una esquina, o un area unida que no es la suma de las de los tableros. Es
/// preferible una junta de mas a una losa con la forma equivocada.
/// </para>
/// </remarks>
public static class UnirLosas
{
    /// <summary>Dos vertices a menos de esto son el mismo. 1 cm.</summary>
    public const double ToleranciaM = 0.01;

    /// <summary>Lo minimo que tienen que compartir dos tableros para unirse. 5 cm.</summary>
    public const double ContactoMinimoM = 0.05;

    /// <summary>Une los tableros del modelo, en su sitio. Devuelve el aviso.</summary>
    public static List<string> AplicarATodos(ModeloJson modelo)
    {
        var avisos = new List<string>();

        var candidatas = modelo.Panos
            .Where(p => p.Clase == ClasePieza.Losa && (p.Huecos?.Count ?? 0) == 0 && EsPlana(p))
            .ToList();

        var grupos = candidatas.GroupBy(p => (
            p.Nivel ?? string.Empty,
            p.Seccion?.Nombre ?? string.Empty,
            Math.Round(p.Seccion?.EspesorM ?? 0, 3),
            Math.Round(p.Vertices.Average(v => v.Z) * 100)));

        var unidas = 0;
        var tableros = 0;
        var sinUnir = 0;

        foreach (var g in grupos)
        {
            foreach (var componente in Componentes(g.ToList()))
            {
                if (componente.Count < 2)
                {
                    continue;
                }

                var losa = Unir(componente);

                if (losa is null)
                {
                    sinUnir += componente.Count;
                    continue;
                }

                var donde = modelo.Panos.IndexOf(componente[0]);

                foreach (var t in componente)
                {
                    modelo.Panos.Remove(t);
                }

                modelo.Panos.Insert(Math.Min(Math.Max(donde, 0), modelo.Panos.Count), losa);
                unidas++;
                tableros += componente.Count;
            }
        }

        if (unidas > 0)
        {
            avisos.Add($"{tableros} tablero(s) de losa se unieron en {unidas} losa(s) continuas: "
                       + "sin junta en el eje de las trabes interiores.");
        }

        if (sinUnir > 0)
        {
            avisos.Add($"«losas»: {sinUnir} tablero(s) que se tocan no se pudieron unir (encimados o "
                       + "tocandose solo por una esquina) y se modelan por separado.");
        }

        return avisos;
    }

    /// <summary>
    /// La losa unida, o <c>null</c> si los tableros no se pueden unir sin cambiar su forma.
    /// </summary>
    public static PanoJson? Unir(IReadOnlyList<PanoJson> tableros)
    {
        if (tableros.Count == 0)
        {
            return null;
        }

        var z = tableros.SelectMany(t => t.Vertices).Average(v => v.Z);

        // ---- Los poligonos, en sentido antihorario ----
        var poligonos = new List<List<(double X, double Y)>>();
        var areaSuma = 0.0;

        foreach (var t in tableros)
        {
            var v = Contornos.SinRepetidos(t.Vertices, 0.001).Select(p => (p.X, p.Y)).ToList();

            if (v.Count < 3)
            {
                return null;
            }

            var a = Area(v);

            if (Math.Abs(a) < 1e-6)
            {
                return null;
            }

            if (a < 0)
            {
                v.Reverse();
            }

            poligonos.Add(v);
            areaSuma += Math.Abs(a);
        }

        // ---- Los vertices, juntando los que estan a menos de 1 cm ----
        var puntos = new List<(double X, double Y)>();

        int Indice((double X, double Y) p)
        {
            for (var i = 0; i < puntos.Count; i++)
            {
                if (Math.Abs(puntos[i].X - p.X) <= ToleranciaM && Math.Abs(puntos[i].Y - p.Y) <= ToleranciaM)
                {
                    return i;
                }
            }

            puntos.Add(p);
            return puntos.Count - 1;
        }

        var indices = poligonos.Select(pl => pl.Select(Indice).ToList()).ToList();

        // ---- Los lados, partidos donde les cae un vertice de otro tablero ----
        var cuenta = new Dictionary<(int, int), int>();

        foreach (var pl in indices)
        {
            for (var k = 0; k < pl.Count; k++)
            {
                var i = pl[k];
                var j = pl[(k + 1) % pl.Count];

                if (i == j)
                {
                    continue;
                }

                var cadena = Partir(i, j, puntos);

                for (var m = 0; m + 1 < cadena.Count; m++)
                {
                    var u = cadena[m];
                    var w = cadena[m + 1];

                    if (u == w)
                    {
                        continue;
                    }

                    if (cuenta.TryGetValue((w, u), out var contrario) && contrario > 0)
                    {
                        cuenta[(w, u)] = contrario - 1;
                    }
                    else
                    {
                        cuenta[(u, w)] = cuenta.TryGetValue((u, w), out var c) ? c + 1 : 1;
                    }
                }
            }
        }

        // Un lado que queda dos veces en el mismo sentido es de dos tableros encimados.
        if (cuenta.Values.Any(c => c > 1))
        {
            return null;
        }

        var siguiente = new Dictionary<int, int>();

        foreach (var ((u, w), c) in cuenta)
        {
            if (c == 0)
            {
                continue;
            }

            // Dos lados saliendo del mismo vertice: tableros que se tocan por una esquina.
            if (!siguiente.TryAdd(u, w))
            {
                return null;
            }
        }

        // ---- Encadenar los contornos ----
        var lazos = new List<List<(double X, double Y)>>();
        var pendientes = new HashSet<int>(siguiente.Keys);

        while (pendientes.Count > 0)
        {
            var inicio = pendientes.First();
            var lazo = new List<(double X, double Y)>();
            var actual = inicio;

            do
            {
                if (!pendientes.Remove(actual) || !siguiente.TryGetValue(actual, out var prox))
                {
                    return null;
                }

                lazo.Add(puntos[actual]);
                actual = prox;
            }
            while (actual != inicio);

            if (lazo.Count < 3)
            {
                return null;
            }

            lazos.Add(lazo);
        }

        var fuera = lazos.Where(l => Area(l) > 0).ToList();
        var huecos = lazos.Where(l => Area(l) < 0).ToList();

        if (fuera.Count != 1)
        {
            return null;
        }

        // El area unida tiene que ser la suma de los tableros.
        var areaUnida = Area(fuera[0]) + huecos.Sum(Area);

        if (Math.Abs(areaUnida - areaSuma) > Math.Max(0.01, areaSuma * 0.002))
        {
            return null;
        }

        // Sin los vertices que quedan en medio de un lado recto, donde se tocaban dos tableros: el
        // paño los vuelve a poner donde haga falta (LosasAPano parte cada lado por tramos).
        List<PuntoJson> AJson(List<(double X, double Y)> l) =>
            Contornos.SinColineales(l.Select(p => new PuntoJson { X = p.X, Y = p.Y, Z = z }).ToList(), 0.005);

        var primero = tableros[0];

        return new PanoJson
        {
            Etiqueta = Etiqueta(tableros),
            Clase = ClasePieza.Losa,
            Nivel = primero.Nivel,
            Seccion = primero.Seccion,
            Vertices = AJson(fuera[0]),
            Huecos = huecos.Select(AJson).ToList()
        };
    }

    /// <summary>
    /// El nombre de la losa unida: las etiquetas de sus tableros. Es estable entre importaciones
    /// mientras los tableros sean los mismos, que es lo que necesita la llave de Revit.
    /// </summary>
    public static string Etiqueta(IEnumerable<PanoJson> tableros)
    {
        var nombres = tableros
            .Select(t => (t.Etiqueta ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (nombres.Count == 0)
        {
            return "Losa";
        }

        var todo = string.Join("+", nombres);

        return todo.Length <= 40 ? todo : $"{nombres[0]}+{nombres.Count - 1} mas";
    }

    /// <summary>Los grupos de tableros que se tocan por un lado, directa o indirectamente.</summary>
    private static List<List<PanoJson>> Componentes(List<PanoJson> losas)
    {
        var padre = Enumerable.Range(0, losas.Count).ToArray();

        int Raiz(int i)
        {
            while (padre[i] != i)
            {
                padre[i] = padre[padre[i]];
                i = padre[i];
            }

            return i;
        }

        for (var i = 0; i < losas.Count; i++)
        {
            for (var j = i + 1; j < losas.Count; j++)
            {
                if (Raiz(i) != Raiz(j) && Comparten(losas[i], losas[j]))
                {
                    padre[Raiz(i)] = Raiz(j);
                }
            }
        }

        return Enumerable.Range(0, losas.Count)
            .GroupBy(Raiz)
            .Select(g => g.Select(i => losas[i]).ToList())
            .ToList();
    }

    /// <summary>Si dos tableros tienen un lado encima del otro, de al menos 5 cm.</summary>
    public static bool Comparten(PanoJson a, PanoJson b)
    {
        var va = a.Vertices;
        var vb = b.Vertices;

        for (var i = 0; i < va.Count; i++)
        {
            var p0 = va[i];
            var p1 = va[(i + 1) % va.Count];
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var largo = Math.Sqrt((dx * dx) + (dy * dy));

            if (largo < 1e-6)
            {
                continue;
            }

            var (ux, uy) = (dx / largo, dy / largo);

            for (var j = 0; j < vb.Count; j++)
            {
                var q0 = vb[j];
                var q1 = vb[(j + 1) % vb.Count];

                double Fuera(PuntoJson q) => Math.Abs(((q.X - p0.X) * -uy) + ((q.Y - p0.Y) * ux));
                double S(PuntoJson q) => ((q.X - p0.X) * ux) + ((q.Y - p0.Y) * uy);

                if (Fuera(q0) > ToleranciaM || Fuera(q1) > ToleranciaM)
                {
                    continue;
                }

                var (s0, s1) = (S(q0), S(q1));
                var solape = Math.Min(largo, Math.Max(s0, s1)) - Math.Max(0, Math.Min(s0, s1));

                if (solape > ContactoMinimoM)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>El lado i-j con los vertices que le caen encima, en orden.</summary>
    private static List<int> Partir(int i, int j, List<(double X, double Y)> puntos)
    {
        var a = puntos[i];
        var b = puntos[j];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var l2 = (dx * dx) + (dy * dy);
        var largo = Math.Sqrt(l2);
        var medio = new List<(double T, int K)>();

        for (var k = 0; k < puntos.Count; k++)
        {
            if (k == i || k == j)
            {
                continue;
            }

            var p = puntos[k];
            var t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / l2;

            if (t * largo <= ToleranciaM || (1 - t) * largo <= ToleranciaM)
            {
                continue;
            }

            var fuera = Math.Abs(((p.X - a.X) * dy) - ((p.Y - a.Y) * dx)) / largo;

            if (fuera <= ToleranciaM)
            {
                medio.Add((t, k));
            }
        }

        var res = new List<int> { i };
        res.AddRange(medio.OrderBy(m => m.T).Select(m => m.K));
        res.Add(j);

        return res;
    }

    private static bool EsPlana(PanoJson p) =>
        p.Vertices.Count >= 3
        && p.Vertices.Max(v => v.Z) - p.Vertices.Min(v => v.Z) <= Losas.ToleranciaPlanaM;

    /// <summary>Area con signo: positiva en sentido antihorario.</summary>
    private static double Area(IReadOnlyList<(double X, double Y)> v)
    {
        var s = 0.0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];
            s += (a.X * b.Y) - (b.X * a.Y);
        }

        return s / 2;
    }
}
