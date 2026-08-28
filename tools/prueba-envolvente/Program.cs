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

    private static void Cerca(double esperado, double real, string que, double tol = 1e-9) =>
        Comprobar(Math.Abs(esperado - real) <= tol, que,
                  $"esperado {esperado}, salió {real}");

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE Envolvente.Convexa y Envolvente.Ensanchada");
        Console.WriteLine(new string('=', 70));

        UnCuadrado();
        LosDeDentroSeQuedanFuera();
        LosAlineadosSeDescartan();
        LosRepetidosNoDuplicanVertices();
        ElCasoDeLaSombra();
        ElCasoDeLaSombraGIRADA();
        Degenerados();
        TodoLoQueEntraQuedaDentro();
        LaPenumbraSeEnsanchaIgualPorTodOS();
        ElTopeDelPico();
        EnsancharDegenerados();

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

    // =================================================================================
    //  LA PENUMBRA: LA BANDA TIENE QUE SER DEL MISMO ANCHO POR TODOS LADOS
    // =================================================================================
    //  Es lo que arregla la sombra. Antes se apilaban siluetas ESCALADAS desde el centro, y
    //  al escalar la banda sale proporcional a la distancia al centro: en una sombra
    //  alargada la punta se ensancha muchisimo y los costados casi nada. Eso no se lee como
    //  una sombra difuminada, se lee como platos apilados.
    //
    //  Ensanchada desplaza cada LADO una distancia fija, asi que la banda es de ancho
    //  constante. La prueba mide justo eso, y con un rectangulo MUY alargado, que es donde
    //  la diferencia entre escalar y desplazar se ve.
    private static void LaPenumbraSeEnsanchaIgualPorTodOS()
    {
        Console.WriteLine();
        Console.WriteLine("La penumbra se ensancha igual por todos lados:");

        // 10 de largo por 1 de ancho: escalar esto deformaria la banda 10 a 1.
        var largo = new List<(double X, double Y)>
        {
            (0, 0), (10, 0), (10, 1), (0, 1)
        };

        var ancha = Envolvente.Ensanchada(largo, 0.5);

        Comprobar(ancha.Count == 4, "un rectangulo ensanchado sigue teniendo cuatro esquinas",
            $"salieron {ancha.Count}");

        Cerca(-0.5, ancha.Min(p => p.X), "se sale 0.5 por la izquierda");
        Cerca(10.5, ancha.Max(p => p.X), "y 0.5 por la derecha");
        Cerca(-0.5, ancha.Min(p => p.Y), "0.5 por abajo");
        Cerca(1.5, ancha.Max(p => p.Y), "y 0.5 por arriba: LA MISMA banda en el lado corto");

        // Escalar habria dado otra cosa completamente: el ancho pasaria de 1 a 1.1 mientras
        // el largo pasa de 10 a 11. Se comprueba que NO es eso.
        var comoSiEscalara = 1 * 1.1;

        Comprobar(Math.Abs((ancha.Max(p => p.Y) - ancha.Min(p => p.Y)) - comoSiEscalara) > 0.5,
            "y NO es un escalado: el lado corto crece lo mismo que el largo");

        // El area crece como area + perimetro*d + pi*d^2 aproximado por las esquinas en punta.
        // Con esquinas rectas y mitre, crece exactamente area + perimetro*d + 4*d^2.
        Cerca(11 * 2, Math.Abs(Area(ancha)), "y el area es la de un 11 x 2", 1e-6);

        // ---- EN SENTIDO HORARIO TAMBIEN ----
        // El sentido de «fuera» se saca del signo del area, asi que no importa como venga. Y el
        // sentido se CONSERVA: lo que entra horario sale horario. Eso importa porque quien
        // dibuja la sombra hace un abanico de triangulos, y si el sentido cambiara de un
        // ensanchado a otro las caras mirarian al lado contrario.
        var alReves = new List<(double X, double Y)>(largo);
        alReves.Reverse();

        Comprobar(Area(largo) > 0, "el rectangulo de partida es antihorario");
        Comprobar(Area(alReves) < 0, "y su reverso, horario");

        var anchaAlReves = Envolvente.Ensanchada(alReves, 0.5);

        Cerca(-0.5, anchaAlReves.Min(p => p.X), "en sentido horario tambien se ensancha, no se mete");
        Cerca(10.5, anchaAlReves.Max(p => p.X), "por los dos lados");
        Cerca(11 * 2, Math.Abs(Area(anchaAlReves)), "y con la misma area", 1e-6);
        Comprobar(Area(anchaAlReves) < 0, "y sigue siendo horario: el sentido se conserva");

        // ---- Y SIEMPRE CONTIENE AL ORIGINAL ----
        // DentroOEnElBorde da por hecho el sentido antihorario, asi que se endereza antes de
        // preguntar. Es cosa de la prueba, no del calculo.
        var enderezado = Antihorario(anchaAlReves);

        var dentro = largo.Count(p => DentroOEnElBorde(p, enderezado));

        Comprobar(dentro == largo.Count,
            "el poligono original queda entero dentro del ensanchado");
    }

    /// <summary>El mismo poligono en sentido antihorario, para poder preguntar si algo cae dentro.</summary>
    private static List<(double X, double Y)> Antihorario(List<(double X, double Y)> p)
    {
        if (Area(p) >= 0)
        {
            return p;
        }

        var alReves = new List<(double X, double Y)>(p);

        alReves.Reverse();

        return alReves;
    }

    // =================================================================================
    //  EL TOPE DEL PICO: UNA ESQUINA AGUDA NO PUEDE SACAR UNA AGUJA
    // =================================================================================
    private static void ElTopeDelPico()
    {
        Console.WriteLine();
        Console.WriteLine("El tope del pico:");

        // Un triangulo con una punta muy aguda: la de (0,0), que abre poquisimo.
        var punta = new List<(double X, double Y)>
        {
            (0, 0), (20, 0.4), (20, -0.4)
        };

        // Sin tope, los dos lados de la punta se cortarian LEJISIMOS: el semiangulo es de
        // unos 1.1 grados, asi que la esquina se iria a d/sen(1.1) = mas de 50 veces d.
        var conTope = Envolvente.Ensanchada(punta, 0.2, topeDePico: 3);

        var masLejos = conTope.Max(p =>
            Math.Sqrt((p.X * p.X) + (p.Y * p.Y)));

        Comprobar(conTope.Count > 3,
            "la punta se chaflana, asi que salen mas de tres vertices",
            $"salieron {conTope.Count}");

        // La punta original esta en (0,0). Ningun vertice nuevo puede estar a mas de
        // 3 * 0.2 = 0.6 de ella POR EL LADO DE LA PUNTA.
        var cercaDeLaPunta = conTope
            .Where(p => p.X < 1)
            .Select(p => Math.Sqrt((p.X * p.X) + (p.Y * p.Y)))
            .ToList();

        Comprobar(cercaDeLaPunta.Count > 0, "hay vertices en la zona de la punta");
        Comprobar(cercaDeLaPunta.All(d => d <= 0.6 + 1e-9),
            "y ninguno se va mas alla del tope de 3 x 0.2 = 0.6",
            $"el mas lejano esta a {(cercaDeLaPunta.Count > 0 ? cercaDeLaPunta.Max() : 0):0.###}");

        // CON EL TOPE MUY ALTO si sale la aguja: asi se ve que el tope es lo que la corta y
        // que la prueba no esta pasando por otro motivo.
        var sinTope = Envolvente.Ensanchada(punta, 0.2, topeDePico: 1000);

        var agujaLejos = sinTope
            .Where(p => p.X < 1)
            .Select(p => Math.Sqrt((p.X * p.X) + (p.Y * p.Y)))
            .DefaultIfEmpty(0)
            .Max();

        Comprobar(agujaLejos > 5,
            "con el tope muy alto SI sale la aguja, o sea que el tope es lo que la corta",
            $"la aguja llego a {agujaLejos:0.##}");

        // Y en los dos casos el original sigue dentro.
        Comprobar(punta.All(p => DentroOEnElBorde(p, Antihorario(conTope))),
            "chaflanada o no, el triangulo original sigue dentro");
    }

    // =================================================================================
    //  LOS DEGENERADOS DEL ENSANCHADO
    // =================================================================================
    private static void EnsancharDegenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Los degenerados del ensanchado:");

        var cuadro = new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1), (0, 1) };

        Comprobar(Envolvente.Ensanchada(cuadro, 0).Count == 4,
            "ensanchar cero no cambia el numero de vertices");
        Cerca(1, Area(Envolvente.Ensanchada(cuadro, 0)), "ni el area");

        Comprobar(Envolvente.Ensanchada(cuadro, -1).Count == 4,
            "y ensanchar un negativo tampoco: se devuelve tal cual");

        Comprobar(Envolvente.Ensanchada(
            new List<(double X, double Y)> { (0, 0), (1, 1) }, 0.5).Count == 2,
            "dos puntos no son un poligono: se devuelven tal cual");

        Comprobar(Envolvente.Ensanchada(
            new List<(double X, double Y)>(), 0.5).Count == 0,
            "y sin puntos, nada");

        // El caso de verdad: la silueta de la sombra, ensanchada. Tiene que seguir siendo
        // convexa, porque de eso depende que el abanico de triangulos valga.
        var silueta = Envolvente.Convexa(new List<(double X, double Y)>
        {
            (0, 0), (30, 0), (30, 60), (0, 60),
            (12, 25), (42, 25), (42, 85), (12, 85)
        });

        var conPenumbra = Envolvente.Ensanchada(silueta, 1.5);

        Comprobar(EsConvexoAntihorario(conPenumbra),
            "la silueta de la sombra ensanchada sigue siendo convexa y antihoraria");
        Comprobar(silueta.All(p => DentroOEnElBorde(p, conPenumbra)),
            "y contiene a la silueta original entera");
        Comprobar(Math.Abs(Area(conPenumbra)) > Math.Abs(Area(silueta)),
            "y es mas grande, no mas chica");
    }
}
