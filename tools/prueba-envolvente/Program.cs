using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>Prueba de <see cref="Envolvente.Convexa"/>: la silueta de la sombra.</summary>
internal static class Program
{
    private static int _fallos;

    private static void Comprobar(bool cond, string que, string porque = "")
    {
        if (cond)
        {
            Console.WriteLine($"  OK    {que}");
            return;
        }

        Console.WriteLine($"  FALLA {que}");

        if (porque.Length > 0)
        {
            Console.WriteLine($"        {porque}");
        }

        _fallos++;
    }

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE Envolvente.Convexa");
        Console.WriteLine(new string('=', 70));

        UnCuadrado();
        LosDeDentroSeQuedanFuera();
        LosAlineadosSeDescartan();
        LosRepetidosNoDuplicanVertices();
        ElCasoDeLaSombra();
        ElCasoDeLaSombraGIRADA();
        Degenerados();
        TodoLoQueEntraQuedaDentro();

        Console.WriteLine(new string('=', 70));

        if (_fallos == 0)
        {
            Console.WriteLine("TODO PASA");
            return 0;
        }

        Console.WriteLine($"{_fallos} COMPROBACIONES FALLAN");
        return 1;
    }

    // ==================================================================

    private static double Area(List<(double X, double Y)> p)
    {
        var a = 0.0;

        for (var i = 0; i < p.Count; i++)
        {
            var j = (i + 1) % p.Count;
            a += (p[i].X * p[j].Y) - (p[j].X * p[i].Y);
        }

        return a / 2;
    }

    /// <summary>¿Todos los giros van a la izquierda? O sea, ¿es convexo y antihorario?</summary>
    private static bool EsConvexoAntihorario(List<(double X, double Y)> p)
    {
        if (p.Count < 3)
        {
            return false;
        }

        for (var i = 0; i < p.Count; i++)
        {
            var o = p[i];
            var a = p[(i + 1) % p.Count];
            var b = p[(i + 2) % p.Count];

            var giro = ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

            if (giro <= 1e-9)
            {
                return false;
            }
        }

        return true;
    }

    private static bool DentroOEnElBorde(
        (double X, double Y) q, List<(double X, double Y)> p, double tol = 1e-7)
    {
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];

            var giro = ((b.X - a.X) * (q.Y - a.Y)) - ((b.Y - a.Y) * (q.X - a.X));

            if (giro < -tol)
            {
                return false;
            }
        }

        return true;
    }

    private static (double X, double Y) Girar(
        (double X, double Y) p, double cx, double cy, double grados)
    {
        var r = grados * Math.PI / 180;
        var co = Math.Cos(r);
        var se = Math.Sin(r);

        var dx = p.X - cx;
        var dy = p.Y - cy;

        return (cx + (dx * co) - (dy * se), cy + (dx * se) + (dy * co));
    }

    // ==================================================================

    private static void UnCuadrado()
    {
        Console.WriteLine();
        Console.WriteLine("Un cuadrado");

        var h = Envolvente.Convexa(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) });

        Comprobar(h.Count == 4, "cuatro vertices", $"salieron {h.Count}");
        Comprobar(EsConvexoAntihorario(h), "convexo y antihorario",
            $"area con signo {Area(h):F3}");
        Comprobar(Math.Abs(Area(h) - 100) < 1e-9, "y encierra los 100",
            $"encierra {Area(h):F6}");
    }

    private static void LosDeDentroSeQuedanFuera()
    {
        Console.WriteLine();
        Console.WriteLine("Puntos de dentro");

        var h = Envolvente.Convexa(new[]
        {
            (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0),
            (5.0, 5.0), (2.0, 7.0), (8.0, 3.0)
        });

        Comprobar(h.Count == 4, "los tres de dentro no aparecen",
            $"salieron {h.Count} vertices y deberian 4");
    }

    private static void LosAlineadosSeDescartan()
    {
        Console.WriteLine();
        Console.WriteLine("Puntos alineados en un lado");

        // El (5,0) esta en medio del lado de abajo: no cambia la figura.
        var h = Envolvente.Convexa(new[]
        {
            (0.0, 0.0), (5.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)
        });

        Comprobar(h.Count == 4, "el de en medio del lado se descarta",
            $"salieron {h.Count} vertices y deberian 4");

        Comprobar(EsConvexoAntihorario(h), "y sigue siendo convexo estricto",
            "quedo algun giro nulo, o sea un vertice de mas");
    }

    private static void LosRepetidosNoDuplicanVertices()
    {
        Console.WriteLine();
        Console.WriteLine("Puntos repetidos");

        var h = Envolvente.Convexa(new[]
        {
            (0.0, 0.0), (0.0, 0.0), (10.0, 0.0), (10.0, 0.0),
            (10.0, 10.0), (0.0, 10.0), (0.0, 0.0)
        });

        Comprobar(h.Count == 4, "no dejan vertices dobles",
            $"salieron {h.Count} vertices y deberian 4");

        Comprobar(EsConvexoAntihorario(h), "y sigue convexo estricto");
    }

    private static void ElCasoDeLaSombra()
    {
        Console.WriteLine();
        Console.WriteLine("La sombra: un rectangulo y su copia corrida");

        // La base de una seccion de 40 x 60 y su tapa caida en diagonal.
        const double bx = 40, by = 60, ox = 105, oy = 146;

        var puntos = new List<(double X, double Y)>();

        foreach (var (x, y) in new[] { (0.0, 0.0), (bx, 0.0), (bx, by), (0.0, by) })
        {
            puntos.Add((x, y));
            puntos.Add((x + ox, y + oy));
        }

        var h = Envolvente.Convexa(puntos);

        Comprobar(h.Count == 6, "la silueta tiene SEIS lados, no cuatro",
            $"salieron {h.Count}");

        Comprobar(EsConvexoAntihorario(h), "convexa y antihoraria");

        var fuera = puntos.Count(p => !DentroOEnElBorde(p, h));

        Comprobar(fuera == 0, "y ningun punto de la pieza se queda fuera",
            $"{fuera} puntos quedaron fuera");
    }

    private static void ElCasoDeLaSombraGIRADA()
    {
        Console.WriteLine();
        Console.WriteLine("La sombra con la seccion GIRADA");

        // Es el caso que la formula a mano NO cubria: girada, la base ya no esta
        // alineada con los ejes.
        const double bx = 40, by = 60, ox = 105, oy = 146;

        var peorLados = 0;
        var fueraTotal = 0;
        var noConvexos = 0;

        for (var g = 0; g < 360; g += 7)
        {
            var puntos = new List<(double X, double Y)>();

            foreach (var (x, y) in new[] { (0.0, 0.0), (bx, 0.0), (bx, by), (0.0, by) })
            {
                var p = Girar((x, y), bx / 2, by / 2, g);

                puntos.Add(p);
                puntos.Add((p.X + ox, p.Y + oy));
            }

            var h = Envolvente.Convexa(puntos);

            peorLados = Math.Max(peorLados, h.Count);

            if (!EsConvexoAntihorario(h))
            {
                noConvexos++;
            }

            fueraTotal += puntos.Count(p => !DentroOEnElBorde(p, h));
        }

        Comprobar(noConvexos == 0, "convexa y antihoraria en los 52 giros probados",
            $"{noConvexos} giros dieron un poligono mal orientado o concavo");

        Comprobar(fueraTotal == 0, "y en ninguno se queda un punto fuera",
            $"{fueraTotal} puntos fuera en total");

        Console.WriteLine($"        el maximo de lados que salio fue {peorLados}");

        Comprobar(peorLados is >= 6 and <= 8,
            "y la silueta tiene entre seis y ocho lados, como toca",
            $"llego a {peorLados}, que no describe dos rectangulos corridos");
    }

    private static void Degenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Casos degenerados");

        Comprobar(Envolvente.Convexa(Array.Empty<(double, double)>()).Count == 0,
            "sin puntos devuelve vacio");

        Comprobar(Envolvente.Convexa(new[] { (1.0, 2.0) }).Count == 1,
            "un punto devuelve ese punto");

        Comprobar(Envolvente.Convexa(new[] { (1.0, 2.0), (3.0, 4.0) }).Count == 2,
            "dos puntos devuelven los dos");

        // Todos en linea: no hay poligono, y lo que se devuelve NO puede ser algo que
        // el que llama dibuje como area.
        var linea = Envolvente.Convexa(new[]
        {
            (0.0, 0.0), (1.0, 1.0), (2.0, 2.0), (3.0, 3.0), (4.0, 4.0)
        });

        Comprobar(linea.Count < 3, "todos en linea no devuelven poligono",
            $"devolvio {linea.Count} vertices para cinco puntos alineados");
    }

    private static void TodoLoQueEntraQuedaDentro()
    {
        Console.WriteLine();
        Console.WriteLine("Al azar, mucha veces");

        var rnd = new Random(4242);

        var malos = 0;
        var fuera = 0;

        for (var caso = 0; caso < 3000; caso++)
        {
            var n = 3 + rnd.Next(20);

            var puntos = new List<(double X, double Y)>();

            for (var i = 0; i < n; i++)
            {
                puntos.Add((Math.Round(rnd.NextDouble() * 40, 1),
                            Math.Round(rnd.NextDouble() * 40, 1)));
            }

            var h = Envolvente.Convexa(puntos);

            if (h.Count < 3)
            {
                // Puede pasar de verdad: puntos repetidos o alineados.
                continue;
            }

            if (!EsConvexoAntihorario(h))
            {
                malos++;
            }

            fuera += puntos.Count(p => !DentroOEnElBorde(p, h));
        }

        Comprobar(malos == 0, "siempre sale convexa y antihoraria",
            $"{malos} casos salieron mal");

        Comprobar(fuera == 0, "y nunca deja un punto de entrada fuera",
            $"{fuera} puntos quedaron fuera");
    }
}
