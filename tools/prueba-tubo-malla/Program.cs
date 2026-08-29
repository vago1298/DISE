using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>Prueba de <see cref="TuboDeMalla"/>: la malla de las barras del 3D.</summary>
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
        Console.WriteLine("PRUEBA DE TuboDeMalla");
        Console.WriteLine(new string('=', 70));

        ElVolumenDeUnTuboRecto();
        ElVolumenEnCualquierDireccion();
        LosAnillosEstanAlRadioYPerpendiculares();
        LasNormalesApuntanHaciaFuera();
        NoSeRetuerceAlDoblar();
        UnRecorridoCerradoNoLlevaTapas();
        NoHayTriangulosDegenerados();
        SeAcumulanVariasBarras();
        Degenerados();

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
    //  Utilidades de la prueba
    // ==================================================================

    /// <summary>
    /// El volumen que encierra la malla, por el teorema de la divergencia.
    /// </summary>
    /// <remarks>
    /// Para una superficie cerrada de triangulos, el volumen es la suma de
    /// <c>a · (b × c) / 6</c>. Sale POSITIVO solo si los triangulos giran hacia fuera, asi
    /// que este numero comprueba el sentido y la geometria de una vez.
    /// </remarks>
    private static double Volumen(TuboDeMalla.Malla m)
    {
        var total = 0.0;

        for (var i = 0; i + 2 < m.Triangulos.Count; i += 3)
        {
            var a = m.Puntos[m.Triangulos[i]];
            var b = m.Puntos[m.Triangulos[i + 1]];
            var c = m.Puntos[m.Triangulos[i + 2]];

            total +=
                (a.X * ((b.Y * c.Z) - (b.Z * c.Y)))
                - (a.Y * ((b.X * c.Z) - (b.Z * c.X)))
                + (a.Z * ((b.X * c.Y) - (b.Y * c.X)));
        }

        return total / 6;
    }

    /// <summary>El volumen de un prisma regular de <paramref name="lados"/> lados.</summary>
    private static double VolumenDelPrisma(double radio, double largo, int lados) =>
        0.5 * lados * radio * radio * Math.Sin(2 * Math.PI / lados) * largo;

    private static double Area(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        (double X, double Y, double Z) c)
    {
        var ux = b.X - a.X;
        var uy = b.Y - a.Y;
        var uz = b.Z - a.Z;

        var vx = c.X - a.X;
        var vy = c.Y - a.Y;
        var vz = c.Z - a.Z;

        var cx = (uy * vz) - (uz * vy);
        var cy = (uz * vx) - (ux * vz);
        var cz = (ux * vy) - (uy * vx);

        return 0.5 * Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
    }

    // ==================================================================

    private static void ElVolumenDeUnTuboRecto()
    {
        Console.WriteLine();
        Console.WriteLine("El volumen de un tubo recto");

        const double r = 0.955;
        const double largo = 300.0;
        const int lados = 8;

        var m = new TuboDeMalla.Malla();

        var cuantos = TuboDeMalla.Agregar(
            m, new[] { (0.0, 0.0, 0.0), (0.0, largo, 0.0) }, r, cerrado: false, lados: lados);

        Comprobar(cuantos > 0, "se genera la malla", "no se genero ningun triangulo");

        var esperado = VolumenDelPrisma(r, largo, lados);
        var medido = Volumen(m);

        Console.WriteLine($"        medido {medido:F6} cm3, prisma {esperado:F6} cm3");

        Comprobar(Math.Abs(medido - esperado) < 1e-6,
            "coincide con la formula del prisma",
            $"se desvia {Math.Abs(medido - esperado):E3}");

        Comprobar(medido > 0,
            "y sale POSITIVO, o sea que los triangulos giran hacia fuera",
            "salio negativo: la malla esta del reves y el motor la pintaria negra");

        // Y con mas lados tiene que acercarse al cilindro.
        var m64 = new TuboDeMalla.Malla();
        TuboDeMalla.Agregar(m64, new[] { (0.0, 0.0, 0.0), (0.0, largo, 0.0) }, r, false, 64);

        var cilindro = Math.PI * r * r * largo;
        var error = Math.Abs(Volumen(m64) - cilindro) / cilindro;

        Console.WriteLine($"        con 64 lados se queda a {100 * error:F3} % del cilindro");

        Comprobar(error < 0.002, "y con 64 lados es practicamente el cilindro",
            $"se queda al {100 * error:F3} %");
    }

    private static void ElVolumenEnCualquierDireccion()
    {
        Console.WriteLine();
        Console.WriteLine("El volumen no depende de la direccion");

        const double r = 1.27;
        const double largo = 55.0;
        const int lados = 8;

        var esperado = VolumenDelPrisma(r, largo, lados);

        var rnd = new Random(99);
        var peor = 0.0;

        for (var caso = 0; caso < 500; caso++)
        {
            // Una direccion al azar, incluidas las que van justo por un eje: ahi es donde
            // una perpendicular mal elegida se cae.
            var dx = rnd.NextDouble() - 0.5;
            var dy = rnd.NextDouble() - 0.5;
            var dz = rnd.NextDouble() - 0.5;

            if (caso < 6)
            {
                (dx, dy, dz) = caso switch
                {
                    0 => (1.0, 0.0, 0.0),
                    1 => (-1.0, 0.0, 0.0),
                    2 => (0.0, 1.0, 0.0),
                    3 => (0.0, -1.0, 0.0),
                    4 => (0.0, 0.0, 1.0),
                    _ => (0.0, 0.0, -1.0)
                };
            }

            var l = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (l < 1e-6)
            {
                continue;
            }

            var m = new TuboDeMalla.Malla();

            TuboDeMalla.Agregar(m, new[]
            {
                (0.0, 0.0, 0.0),
                (dx / l * largo, dy / l * largo, dz / l * largo)
            }, r, false, lados);

            peor = Math.Max(peor, Math.Abs(Volumen(m) - esperado));
        }

        Console.WriteLine($"        el peor caso se desvia {peor:E3} cm3");

        Comprobar(peor < 1e-6,
            "500 direcciones al azar y los seis ejes dan el mismo volumen",
            $"el peor se desvia {peor:E3}");
    }

    private static void LosAnillosEstanAlRadioYPerpendiculares()
    {
        Console.WriteLine();
        Console.WriteLine("Los anillos");

        const double r = 0.955;
        const int lados = 8;

        // Un recorrido con dos dobleces, como el de un estribo.
        var eje = new List<(double X, double Y, double Z)>();

        for (var i = 0; i <= 20; i++)
        {
            eje.Add((i * 2.0, 0, 0));
        }

        for (var i = 1; i <= 12; i++)
        {
            var a = Math.PI / 2 * i / 12;
            eje.Add((40 + (6 * Math.Sin(a)), 0, 6 - (6 * Math.Cos(a))));
        }

        for (var i = 1; i <= 20; i++)
        {
            eje.Add((46, 0, 6 + (i * 2.0)));
        }

        var m = new TuboDeMalla.Malla();
        var cuantos = TuboDeMalla.Agregar(m, eje, r, false, lados);

        Comprobar(cuantos > 0, "se genera la malla del recorrido doblado");

        // Los primeros lados*n vertices son los anillos, en orden.
        var peorRadio = 0.0;

        for (var i = 0; i < eje.Count; i++)
        {
            for (var j = 0; j < lados; j++)
            {
                var v = m.Puntos[(i * lados) + j];

                var dx = v.X - eje[i].X;
                var dy = v.Y - eje[i].Y;
                var dz = v.Z - eje[i].Z;

                peorRadio = Math.Max(
                    peorRadio,
                    Math.Abs(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) - r));
            }
        }

        Comprobar(peorRadio < 1e-9,
            "cada vertice esta al radio EXACTO de su punto del eje",
            $"el peor se desvia {peorRadio:E3} cm");

        // Y el anillo tiene que ser perpendicular al eje: el vector radial no puede tener
        // componente en la direccion de avance.
        var peorPerp = 0.0;

        for (var i = 1; i < eje.Count - 1; i++)
        {
            var tx = eje[i + 1].X - eje[i - 1].X;
            var ty = eje[i + 1].Y - eje[i - 1].Y;
            var tz = eje[i + 1].Z - eje[i - 1].Z;

            var tl = Math.Sqrt((tx * tx) + (ty * ty) + (tz * tz));

            for (var j = 0; j < lados; j++)
            {
                var v = m.Puntos[(i * lados) + j];

                var dx = v.X - eje[i].X;
                var dy = v.Y - eje[i].Y;
                var dz = v.Z - eje[i].Z;

                var d = ((dx * tx) + (dy * ty) + (dz * tz)) / tl / r;

                peorPerp = Math.Max(peorPerp, Math.Abs(d));
            }
        }

        Comprobar(peorPerp < 1e-9,
            "y el anillo es perpendicular al eje en ese punto",
            $"el peor tiene componente {peorPerp:E3} en la direccion de avance");
    }

    private static void LasNormalesApuntanHaciaFuera()
    {
        Console.WriteLine();
        Console.WriteLine("Las normales");

        const double r = 1.0;
        const int lados = 8;

        var m = new TuboDeMalla.Malla();
        TuboDeMalla.Agregar(m, new[] { (0.0, 0.0, 0.0), (0.0, 40.0, 0.0) }, r, false, lados);

        Comprobar(m.Normales.Count == m.Puntos.Count,
            "hay una normal por vertice",
            $"{m.Normales.Count} normales para {m.Puntos.Count} vertices");

        var peorNorma = 0.0;

        foreach (var nn in m.Normales)
        {
            peorNorma = Math.Max(
                peorNorma,
                Math.Abs(Math.Sqrt((nn.X * nn.X) + (nn.Y * nn.Y) + (nn.Z * nn.Z)) - 1));
        }

        Comprobar(peorNorma < 1e-9, "todas son unitarias",
            $"la peor se desvia {peorNorma:E3}");

        // Las del tubo -los dos primeros anillos- tienen que ir del eje hacia fuera.
        var haciaDentro = 0;

        for (var i = 0; i < 2 * lados; i++)
        {
            var v = m.Puntos[i];
            var nn = m.Normales[i];

            // El eje de este tubo es la recta x=0, z=0.
            if ((v.X * nn.X) + (v.Z * nn.Z) <= 0)
            {
                haciaDentro++;
            }
        }

        Comprobar(haciaDentro == 0, "las del tubo van del eje hacia FUERA",
            $"{haciaDentro} apuntan hacia dentro");
    }

    private static void NoSeRetuerceAlDoblar()
    {
        Console.WriteLine();
        Console.WriteLine("El retorcido");

        const double r = 1.0;
        const int lados = 8;

        // Una espiral apretada, que es lo peor para un transporte de terna mal hecho.
        var eje = new List<(double X, double Y, double Z)>();

        for (var i = 0; i <= 240; i++)
        {
            var a = 6 * Math.PI * i / 240;

            eje.Add((20 * Math.Cos(a), i * 0.25, 20 * Math.Sin(a)));
        }

        var m = new TuboDeMalla.Malla();
        TuboDeMalla.Agregar(m, eje, r, false, lados);

        // Si la terna se transporta bien, el primer vertice de cada anillo apenas gira
        // respecto al del anillo anterior ALREDEDOR del eje. Se mide el angulo entre los
        // dos vectores radiales, descontando el cambio de direccion del eje.
        var peorGiro = 0.0;

        for (var i = 1; i < eje.Count; i++)
        {
            var a = m.Puntos[((i - 1) * lados)];
            var b = m.Puntos[(i * lados)];

            var ra = (a.X - eje[i - 1].X, a.Y - eje[i - 1].Y, a.Z - eje[i - 1].Z);
            var rb = (b.X - eje[i].X, b.Y - eje[i].Y, b.Z - eje[i].Z);

            var cos = ((ra.Item1 * rb.Item1) + (ra.Item2 * rb.Item2) + (ra.Item3 * rb.Item3))
                      / (r * r);

            peorGiro = Math.Max(peorGiro, Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI);
        }

        Console.WriteLine($"        el radial gira como mucho {peorGiro:F3} grados por anillo");

        // Un lado del octogono son 45 grados. Si girara eso, el tubo estaria trenzado.
        Comprobar(peorGiro < 5,
            "el anillo apenas gira de un punto al siguiente: no se trenza",
            $"gira hasta {peorGiro:F1} grados, o sea que el tubo sale retorcido");
    }

    private static void UnRecorridoCerradoNoLlevaTapas()
    {
        Console.WriteLine();
        Console.WriteLine("Recorridos cerrados");

        const double r = 0.5;
        const int lados = 8;

        // Un aro cuadrado con las esquinas partidas, como un estribo sin gancho.
        var eje = new List<(double X, double Y, double Z)>();

        for (var i = 0; i < 40; i++)
        {
            var a = 2 * Math.PI * i / 40;

            eje.Add((30 * Math.Cos(a), 0, 30 * Math.Sin(a)));
        }

        var abierta = new TuboDeMalla.Malla();
        TuboDeMalla.Agregar(abierta, eje, r, cerrado: false, lados: lados);

        var cerrada = new TuboDeMalla.Malla();
        TuboDeMalla.Agregar(cerrada, eje, r, cerrado: true, lados: lados);

        Comprobar(cerrada.Puntos.Count < abierta.Puntos.Count,
            "cerrado gasta menos vertices que abierto: no pone tapas",
            $"cerrado {cerrada.Puntos.Count}, abierto {abierta.Puntos.Count}");

        // Un toro cerrado tambien encierra volumen, y tambien tiene que salir positivo.
        var v = Volumen(cerrada);

        Console.WriteLine($"        el aro cerrado encierra {v:F3} cm3");

        Comprobar(v > 0, "y sigue girando hacia fuera",
            $"encierra {v:F3} cm3, o sea del reves");

        // Volumen del toro de N lados: area de la seccion por el largo del eje.
        var largoEje = 0.0;

        for (var i = 0; i < eje.Count; i++)
        {
            var a = eje[i];
            var b = eje[(i + 1) % eje.Count];

            largoEje += Math.Sqrt(
                ((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
        }

        var areaSeccion = 0.5 * lados * r * r * Math.Sin(2 * Math.PI / lados);

        var error = Math.Abs(v - (areaSeccion * largoEje)) / (areaSeccion * largoEje);

        Console.WriteLine($"        contra seccion por largo del eje: {100 * error:F3} % de error");

        Comprobar(error < 0.01,
            "y su volumen es la seccion por el largo del eje",
            $"se desvia el {100 * error:F2} %");
    }

    private static void NoHayTriangulosDegenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Triangulos degenerados");

        var eje = new List<(double X, double Y, double Z)>
        {
            // Con puntos REPETIDOS a proposito: salen solos donde una recta empalma con un
            // doblez, y un anillo duplicado daria triangulos de area cero.
            (0, 0, 0), (0, 0, 0), (10, 0, 0), (10, 0, 0), (10, 0, 0), (20, 0, 0)
        };

        var m = new TuboDeMalla.Malla();
        var cuantos = TuboDeMalla.Agregar(m, eje, 1.0, false, 8);

        Comprobar(cuantos > 0, "se genera la malla aunque haya puntos repetidos");

        var degenerados = 0;

        for (var i = 0; i + 2 < m.Triangulos.Count; i += 3)
        {
            if (Area(m.Puntos[m.Triangulos[i]],
                     m.Puntos[m.Triangulos[i + 1]],
                     m.Puntos[m.Triangulos[i + 2]]) < 1e-12)
            {
                degenerados++;
            }
        }

        Comprobar(degenerados == 0, "y ninguno tiene area cero",
            $"{degenerados} triangulos degenerados de {m.CuantosTriangulos}");

        // Y los indices tienen que estar dentro de la lista de vertices.
        var fuera = m.Triangulos.Count(i => i < 0 || i >= m.Puntos.Count);

        Comprobar(fuera == 0, "los indices apuntan a vertices que existen",
            $"{fuera} indices fuera de rango");
    }

    private static void SeAcumulanVariasBarras()
    {
        Console.WriteLine();
        Console.WriteLine("Varias barras en una malla");

        const double r = 1.0;
        const int lados = 8;
        const double largo = 30.0;

        var m = new TuboDeMalla.Malla();

        for (var i = 0; i < 25; i++)
        {
            TuboDeMalla.Agregar(
                m, new[] { (i * 5.0, 0.0, 0.0), (i * 5.0, largo, 0.0) }, r, false, lados);
        }

        var esperado = 25 * VolumenDelPrisma(r, largo, lados);
        var medido = Volumen(m);

        Comprobar(Math.Abs(medido - esperado) < 1e-6,
            "25 barras en una sola malla suman su volumen",
            $"medido {medido:F6}, esperado {esperado:F6}");

        var fuera = m.Triangulos.Count(i => i < 0 || i >= m.Puntos.Count);

        Comprobar(fuera == 0,
            "y los indices de la segunda barra en adelante siguen bien desplazados",
            $"{fuera} indices fuera de rango");
    }

    private static void Degenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Casos degenerados");

        var m = new TuboDeMalla.Malla();

        Comprobar(TuboDeMalla.Agregar(m, Array.Empty<(double, double, double)>(), 1) == 0,
            "sin puntos no genera nada");

        Comprobar(TuboDeMalla.Agregar(m, new[] { (0.0, 0.0, 0.0) }, 1) == 0,
            "un solo punto no genera nada");

        Comprobar(
            TuboDeMalla.Agregar(m, new[] { (0.0, 0.0, 0.0), (0.0, 0.0, 0.0) }, 1) == 0,
            "dos puntos iguales no generan nada");

        Comprobar(
            TuboDeMalla.Agregar(m, new[] { (0.0, 0.0, 0.0), (0.0, 10.0, 0.0) }, 0) == 0,
            "radio cero no genera nada");

        Comprobar(
            TuboDeMalla.Agregar(
                m, new[] { (0.0, 0.0, 0.0), (0.0, 10.0, 0.0) }, 1, false, 2) == 0,
            "menos de tres lados no generan nada");

        Comprobar(m.Puntos.Count == 0 && m.Triangulos.Count == 0,
            "y ninguno de esos casos deja basura en la malla",
            $"quedaron {m.Puntos.Count} vertices y {m.Triangulos.Count} indices");
    }
}
