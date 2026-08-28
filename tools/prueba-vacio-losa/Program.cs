using CadLink.Cad.PlanoEstructural;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba de <see cref="VacioEnLosa"/>: dónde <b>no hay piso</b>, y la cruz que lo dice.
/// </summary>
/// <remarks>
/// El razonamiento de qué se está cubriendo y por qué está en el .csproj. En resumen: que una
/// escotadura no se confunda con un hueco, que la junta del mallado no se confunda con un hueco,
/// y que la cruz no acabe pintada encima de la losa.
/// </remarks>
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

    private static void Cerca(double esperado, double real, string que, double tol = 1e-6) =>
        Comprobar(Math.Abs(esperado - real) <= tol, que,
                  $"esperado {esperado}, salió {real}");

    // ---------------------------------------------------------------------------------
    //  Ayudas para escribir los casos
    // ---------------------------------------------------------------------------------

    /// <summary>Un paño rectangular, de esquina a esquina.</summary>
    private static List<(double X, double Y)> Rect(
        double x1, double y1, double x2, double y2) =>
        new() { (x1, y1), (x2, y1), (x2, y2), (x1, y2) };

    private static List<VacioEnLosa.Vacio> Detectar(
        List<List<(double X, double Y)>> panos,
        double tol = 0.05,
        double areaMin = 0.10) =>
        VacioEnLosa.Detectar(
            panos.Select(p => (IReadOnlyList<(double X, double Y)>)p).ToList(),
            tol, areaMin);

    /// <summary>
    /// Un piso de 10×10 con un hueco rectangular en medio, hecho con cuatro paños.
    /// </summary>
    /// <remarks>
    /// Es como llega de verdad: nadie modela un polígono con agujero, se modelan los paños que
    /// rodean el hueco.
    /// </remarks>
    private static List<List<(double X, double Y)>> ConHueco(
        double hx1, double hy1, double hx2, double hy2)
    {
        return new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, hy1),      // abajo, de lado a lado
            Rect(0, hy2, 10, 10),     // arriba, de lado a lado
            Rect(0, hy1, hx1, hy2),   // izquierda del hueco
            Rect(hx2, hy1, 10, hy2),  // derecha del hueco
        };
    }

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE VacioEnLosa: donde NO hay piso");
        Console.WriteLine(new string('=', 70));

        ElHuecoDeLaEscalera();
        UnPisoEnteroNoTieneVacios();
        LaEscotaduraDeUnaLNoEsUnVacio();
        DosHuecosSonDosVacios();
        LaJuntaDelMalladoNoEsUnVacio();
        ElAreaMinima();
        UnHuecoEnL();
        ElHuecoConSuDescansoEnMedio();
        Degenerados();

        Console.WriteLine(new string('=', 70));

        if (_fallos == 0)
        {
            Console.WriteLine("TODO PASA");
            return 0;
        }

        Console.WriteLine($"FALLAN {_fallos} comprobacion(es)");
        return 1;
    }

    // =================================================================================
    //  EL CASO NORMAL: EL HUECO DE LA ESCALERA
    // =================================================================================

    private static void ElHuecoDeLaEscalera()
    {
        Console.WriteLine();
        Console.WriteLine("El hueco de la escalera: 10x10 con un 2x2 en medio");

        var vacios = Detectar(ConHueco(4, 4, 6, 6));

        Comprobar(vacios.Count == 1, "sale UN vacio", $"salieron {vacios.Count}");

        if (vacios.Count != 1)
        {
            return;
        }

        var v = vacios[0];

        Cerca(4, v.Area, "y mide los 4 m2 del hueco");

        Comprobar(v.Contornos.Count == 1, "con UN contorno: no tiene islas");
        Comprobar(v.Contornos[0].Count == 4,
                  "de CUATRO vertices, sin los puntos de paso de la reticula",
                  $"salieron {v.Contornos[0].Count}");

        // Los cuatro vertices son las esquinas del hueco, en el orden que sea.
        var esquinas = new[] { (4.0, 4.0), (6.0, 4.0), (6.0, 6.0), (4.0, 6.0) };

        foreach (var (ex, ey) in esquinas)
        {
            Comprobar(
                v.Contornos[0].Any(p => Math.Abs(p.X - ex) < 1e-9 && Math.Abs(p.Y - ey) < 1e-9),
                $"el contorno pasa por la esquina ({ex}, {ey})");
        }

        // ---- LA CRUZ ----
        //
        // Se pidió que salga DE LOS VERTICES: en un hueco rectangular son las dos diagonales
        // completas, de esquina a esquina opuesta.
        Comprobar(v.Cruz.Count == 2, "la cruz son DOS trazos", $"salieron {v.Cruz.Count}");

        Comprobar(
            v.Cruz.Any(c => Mismo(c, 4, 4, 6, 6)),
            "una diagonal va de (4,4) a (6,6), completa");
        Comprobar(
            v.Cruz.Any(c => Mismo(c, 4, 6, 6, 4)),
            "y la otra de (4,6) a (6,4)");
    }

    /// <summary>¿El trazo va de esquina a esquina, en cualquiera de los dos sentidos?</summary>
    private static bool Mismo(
        (double X1, double Y1, double X2, double Y2) c,
        double xa, double ya, double xb, double yb)
    {
        bool Igual(double a, double b) => Math.Abs(a - b) < 1e-9;

        return (Igual(c.X1, xa) && Igual(c.Y1, ya) && Igual(c.X2, xb) && Igual(c.Y2, yb))
            || (Igual(c.X1, xb) && Igual(c.Y1, yb) && Igual(c.X2, xa) && Igual(c.Y2, ya));
    }

    // =================================================================================
    //  LO QUE NO ES UN VACIO
    // =================================================================================

    private static void UnPisoEnteroNoTieneVacios()
    {
        Console.WriteLine();
        Console.WriteLine("Un piso entero no tiene vacios");

        var uno = Detectar(new List<List<(double X, double Y)>> { Rect(0, 0, 10, 10) });

        Comprobar(uno.Count == 0, "un paño solo y macizo no deja hueco",
                  $"salieron {uno.Count}");

        // Y en cuatro pedazos, como lo deja el mallado: las juntas están pegadas, así que
        // tampoco hay hueco.
        var cuatro = Detectar(new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 5, 5), Rect(5, 0, 10, 5), Rect(0, 5, 5, 10), Rect(5, 5, 10, 10),
        });

        Comprobar(cuatro.Count == 0, "y en cuatro pedazos que se tocan, tampoco",
                  $"salieron {cuatro.Count}");
    }

    private static void LaEscotaduraDeUnaLNoEsUnVacio()
    {
        Console.WriteLine();
        Console.WriteLine("La escotadura de una planta en L NO es un vacio");

        // Una L: falta la esquina de arriba a la derecha. Esa esquina no tiene losa, pero se
        // sale del edificio: no es un agujero en el piso.
        var enL = Detectar(new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, 5),
            Rect(0, 5, 5, 10),
        });

        Comprobar(enL.Count == 0,
                  "la esquina que le falta a la L no se marca como vacio",
                  $"salieron {enL.Count}");

        // Y UNA U: la escotadura entra hasta el medio pero sigue abierta al exterior. Es el
        // caso que distingue «rodeado de losa» de «rodeado por tres lados».
        var enU = Detectar(new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, 4),
            Rect(0, 4, 4, 10),
            Rect(6, 4, 10, 10),
        });

        Comprobar(enU.Count == 0,
                  "la escotadura de una U tampoco: esta abierta por arriba",
                  $"salieron {enU.Count}");

        // Y AHORA SE CIERRA: el mismo hueco con una losa que lo tapa por arriba SI es un
        // vacio. Es la misma geometria con una losa mas, asi que compara lo que importa.
        var cerrada = Detectar(new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, 4),
            Rect(0, 4, 4, 10),
            Rect(6, 4, 10, 10),
            Rect(4, 8, 6, 10),
        });

        Comprobar(cerrada.Count == 1,
                  "tapando esa escotadura por arriba, ya SI es un vacio",
                  $"salieron {cerrada.Count}");

        if (cerrada.Count == 1)
        {
            Cerca(8, cerrada[0].Area, "y mide los 8 m2 que quedan encerrados");
        }
    }

    private static void DosHuecosSonDosVacios()
    {
        Console.WriteLine();
        Console.WriteLine("Dos huecos separados son dos vacios");

        // Un 12x10 con dos huecos: la escalera y el ducto.
        var panos = new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 12, 3),
            Rect(0, 7, 12, 10),
            Rect(0, 3, 2, 7),
            Rect(4, 3, 8, 7),
            Rect(10, 3, 12, 7),
        };

        var vacios = Detectar(panos);

        Comprobar(vacios.Count == 2, "salen DOS vacios", $"salieron {vacios.Count}");

        if (vacios.Count != 2)
        {
            return;
        }

        // Vienen ordenados de mayor a menor, asi que el orden no depende del barrido.
        Cerca(8, vacios[0].Area, "el mayor mide 8 m2 (2x4)");
        Cerca(8, vacios[1].Area, "y el otro tambien 8 m2 (2x4)");

        Comprobar(vacios.All(v => v.Cruz.Count == 2), "cada uno lleva su cruz de dos trazos");
    }

    // =================================================================================
    //  LA JUNTA DEL MALLADO
    // =================================================================================

    private static void LaJuntaDelMalladoNoEsUnVacio()
    {
        Console.WriteLine();
        Console.WriteLine("La junta del mallado NO es un vacio");

        // El mismo 10x10 con hueco, pero el hueco es una rendija de 1 cm: es lo que deja un
        // modelo cuyos paños no cierran exactos.
        var rendija = ConHueco(4, 4, 4.01, 4.01);

        // Con la tolerancia de 5 cm, los dos bordes son el MISMO borde y no queda ni una celda
        // entre ellos. El area minima se pone en cero para que quede claro que lo que la borra
        // es la TOLERANCIA y no el filtro de area.
        var conTol = Detectar(rendija, tol: 0.05, areaMin: 0);

        Comprobar(conTol.Count == 0,
                  "con 5 cm de tolerancia, la rendija de 1 cm desaparece",
                  $"salieron {conTol.Count}");

        // Y sin tolerancia SI aparece: asi se ve que lo que la quita es la tolerancia, y que la
        // prueba no esta pasando por otro motivo.
        var sinTol = Detectar(rendija, tol: 0.0001, areaMin: 0);

        Comprobar(sinTol.Count == 1,
                  "y sin tolerancia SI aparece, o sea que es la tolerancia la que la quita",
                  $"salieron {sinTol.Count}");

        // El hueco de verdad NO se lo lleva la tolerancia por delante.
        var deVerdad = Detectar(ConHueco(4, 4, 6, 6), tol: 0.05, areaMin: 0);

        Comprobar(deVerdad.Count == 1, "y el hueco de 2x2 sigue apareciendo");
    }

    private static void ElAreaMinima()
    {
        Console.WriteLine();
        Console.WriteLine("El area minima");

        // Un hueco de 30x30 cm: 0.09 m2.
        var chico = ConHueco(4, 4, 4.3, 4.3);

        Comprobar(Detectar(chico, tol: 0.05, areaMin: 0.10).Count == 0,
                  "un hueco de 0.09 m2 no pasa el minimo de 0.10");

        Comprobar(Detectar(chico, tol: 0.05, areaMin: 0.05).Count == 1,
                  "y con el minimo en 0.05 si pasa");
    }

    // =================================================================================
    //  LA CRUZ NO PUEDE PISAR LA LOSA
    // =================================================================================

    private static void UnHuecoEnL()
    {
        Console.WriteLine();
        Console.WriteLine("Un hueco en L: la cruz se recorta");

        // El hueco ocupa [4,7]x[4,5] mas [4,5]x[5,7]. La esquina [5,7]x[5,7] SI tiene losa,
        // asi que la diagonal de la caja del hueco le pasa por encima y hay que recortarla.
        var panos = new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, 4),
            Rect(0, 7, 10, 10),
            Rect(0, 4, 4, 7),
            Rect(7, 4, 10, 7),
            Rect(5, 5, 7, 7),
        };

        var vacios = Detectar(panos);

        Comprobar(vacios.Count == 1, "sale UN vacio", $"salieron {vacios.Count}");

        if (vacios.Count != 1)
        {
            return;
        }

        var v = vacios[0];

        Cerca(5, v.Area, "mide los 5 m2 de la L");
        Comprobar(v.Contornos.Count == 1, "con un solo contorno");
        Comprobar(v.Contornos[0].Count == 6,
                  "de SEIS vertices, que son los de una L",
                  $"salieron {v.Contornos[0].Count}");

        // LO QUE IMPORTA: ningun trazo de la cruz pisa la losa. Se comprueba con el punto
        // medio de cada trazo, que es donde se saldria.
        Comprobar(v.Cruz.Count > 0, "hay cruz");

        foreach (var c in v.Cruz)
        {
            var mx = (c.X1 + c.X2) / 2;
            var my = (c.Y1 + c.Y2) / 2;

            Comprobar(EnLaL(mx, my),
                      $"el trazo de ({c.X1:0.###},{c.Y1:0.###}) a ({c.X2:0.###},{c.Y2:0.###}) " +
                      "va por dentro del hueco");
        }

        // Y NINGUNO pasa por la esquina que tiene losa: (6,6) esta en el paño (5,5)-(7,7).
        Comprobar(!v.Cruz.Any(c => PasaPor(c, 6, 6)),
                  "y ninguno cruza la esquina (6,6), que SI tiene losa");
    }

    /// <summary>¿El punto cae dentro de la L del caso de arriba?</summary>
    private static bool EnLaL(double x, double y) =>
        (x >= 4 && x <= 7 && y >= 4 && y <= 5) || (x >= 4 && x <= 5 && y >= 5 && y <= 7);

    /// <summary>¿El trazo pasa por ese punto?</summary>
    private static bool PasaPor(
        (double X1, double Y1, double X2, double Y2) c, double x, double y)
    {
        var dx = c.X2 - c.X1;
        var dy = c.Y2 - c.Y1;

        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-12)
        {
            return false;
        }

        // Distancia del punto a la recta, y que el pie caiga dentro del trazo.
        var t = (((x - c.X1) * dx) + ((y - c.Y1) * dy)) / (largo * largo);

        if (t < 0 || t > 1)
        {
            return false;
        }

        var px = c.X1 + (dx * t);
        var py = c.Y1 + (dy * t);

        return Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py))) < 1e-6;
    }

    private static void ElHuecoConSuDescansoEnMedio()
    {
        Console.WriteLine();
        Console.WriteLine("El hueco con una isla de losa dentro: la escalera con su descanso");

        // Hueco de 3x3 en [4,7]x[4,7], con un descanso de 1x1 en [5,6]x[5,6].
        var panos = new List<List<(double X, double Y)>>
        {
            Rect(0, 0, 10, 4),
            Rect(0, 7, 10, 10),
            Rect(0, 4, 4, 7),
            Rect(7, 4, 10, 7),
            Rect(5, 5, 6, 6),
        };

        var vacios = Detectar(panos);

        Comprobar(vacios.Count == 1, "sale UN vacio", $"salieron {vacios.Count}");

        if (vacios.Count != 1)
        {
            return;
        }

        var v = vacios[0];

        Cerca(8, v.Area, "mide 8 m2: los 9 del hueco menos el descanso");

        // DOS contornos: el de fuera y el del descanso. Los dos hacen falta, porque el
        // descanso es piso y su orilla es orilla de vacio.
        Comprobar(v.Contornos.Count == 2,
                  "salen DOS contornos: el del hueco y el de la isla",
                  $"salieron {v.Contornos.Count}");

        if (v.Contornos.Count == 2)
        {
            var areas = v.Contornos.Select(Area).OrderBy(a => a).ToList();

            Cerca(1, areas[0], "el contorno chico encierra el 1 m2 del descanso");
            Cerca(9, areas[1], "y el grande los 9 m2 del hueco entero");
        }

        // Y LA CRUZ NO PISA EL DESCANSO. Cada diagonal se parte en dos: entra al hueco, salta
        // el descanso y sigue.
        Comprobar(v.Cruz.Count == 4,
                  "la cruz sale en CUATRO trazos: cada diagonal partida por el descanso",
                  $"salieron {v.Cruz.Count}");

        foreach (var c in v.Cruz)
        {
            var mx = (c.X1 + c.X2) / 2;
            var my = (c.Y1 + c.Y2) / 2;

            var enElDescanso = mx > 5 && mx < 6 && my > 5 && my < 6;

            Comprobar(!enElDescanso,
                      $"el trazo de ({c.X1:0.###},{c.Y1:0.###}) a ({c.X2:0.###},{c.Y2:0.###}) " +
                      "no va por el descanso");
        }

        Comprobar(!v.Cruz.Any(c => PasaPor(c, 5.5, 5.5)),
                  "y ninguno pasa por el centro del descanso");
    }

    private static double Area(List<(double X, double Y)> v)
    {
        double doble = 0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            doble += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(doble) / 2;
    }

    // =================================================================================
    //  LOS CASOS DEGENERADOS
    // =================================================================================

    private static void Degenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Los casos degenerados");

        Comprobar(Detectar(new List<List<(double X, double Y)>>()).Count == 0,
                  "sin paños no hay vacios");

        Comprobar(
            Detectar(new List<List<(double X, double Y)>>
            {
                new() { (0, 0), (1, 1) },
            }).Count == 0,
            "un paño de dos puntos no cierra nada, y no revienta");

        // Un solo triangulo: la reticula tiene tres lineas en cada sentido, asi que hay celdas,
        // pero ninguna queda encerrada.
        Comprobar(
            Detectar(new List<List<(double X, double Y)>>
            {
                new() { (0, 0), (10, 0), (0, 10) },
            }).Count == 0,
            "un triangulo solo no deja hueco");

        // Dos paños que se SOLAPAN: la union no tiene agujero, y el solape no debe inventar uno.
        Comprobar(
            Detectar(new List<List<(double X, double Y)>>
            {
                Rect(0, 0, 6, 10), Rect(4, 0, 10, 10),
            }).Count == 0,
            "dos paños solapados no inventan un hueco");

        LaValvulaDeLaReticula();
    }

    // =================================================================================
    //  LA VALVULA: UN MALLADO MUY FINO NO PUEDE COLGAR EL DIBUJO
    // =================================================================================

    private static void LaValvulaDeLaReticula()
    {
        Console.WriteLine();
        Console.WriteLine("La valvula de la reticula");

        var hueco = ConHueco(4, 4, 6, 6)
            .Select(p => (IReadOnlyList<(double X, double Y)>)p)
            .ToList();

        // El caso normal pide muy pocas celdas: la reticula sale de los vertices, y aqui hay
        // cuatro paños.
        var pocas = VacioEnLosa.CeldasQueHacenFalta(hueco, 0.05);

        Comprobar(pocas > 0 && pocas < 100,
                  $"el caso normal pide {pocas} celdas, muy por debajo del tope");

        // Y AHORA UN MALLADO FINO DE VERDAD: un piso de 30x30 partido en paños de 10 cm son
        // 300 lineas en cada sentido, o sea 90 000 celdas. Sigue por debajo del tope, asi que
        // se resuelve, y de paso se comprueba que no tarda una eternidad.
        var fino = new List<List<(double X, double Y)>>();

        for (var i = 0; i < 60; i++)
        {
            for (var j = 0; j < 60; j++)
            {
                // Se deja un hueco de verdad en medio, de 1x1, para tener algo que encontrar.
                if (i >= 30 && i < 32 && j >= 30 && j < 32)
                {
                    continue;
                }

                fino.Add(Rect(i * 0.5, j * 0.5, (i + 1) * 0.5, (j + 1) * 0.5));
            }
        }

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var vacios = Detectar(fino, tol: 0.01, areaMin: 0.10);

        reloj.Stop();

        Console.WriteLine($"        (3600 paños resueltos en {reloj.ElapsedMilliseconds} ms)");

        Comprobar(vacios.Count == 1,
                  "con 3600 paños de 50 cm encuentra el hueco de 1x1",
                  $"salieron {vacios.Count}");

        if (vacios.Count == 1)
        {
            Cerca(1, vacios[0].Area, "y mide 1 m2");
        }

        Comprobar(reloj.ElapsedMilliseconds < 10000,
                  $"y tarda menos de 10 s ({reloj.ElapsedMilliseconds} ms)");

        // EL TOPE. Se comprueba que la cuenta previa y Detectar dicen lo mismo: si la cuenta
        // pasa del tope, Detectar devuelve vacio. Asi el aviso que da el dibujante -«sube
        // VACIO_TOL_CM»- no puede desincronizarse de lo que de verdad hace el calculo.
        Comprobar(VacioEnLosa.MaximoDeCeldas > 0, "el tope existe y es positivo");

        Comprobar(
            VacioEnLosa.CeldasQueHacenFalta(
                new List<IReadOnlyList<(double X, double Y)>>(), 0.05) == 0,
            "sin paños, la cuenta previa da cero");
    }
}
