using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba de <see cref="CintaConHuecos"/>: abrir huecos en la cinta del diamante.
/// </summary>
internal static class Program
{
    private static int _fallos;

    private static void Bien(string que) => Console.WriteLine($"  OK    {que}");

    private static void Mal(string que, string porque)
    {
        Console.WriteLine($"  FALLA {que}");
        Console.WriteLine($"        {porque}");
        _fallos++;
    }

    private static void Comprobar(bool condicion, string que, string porque)
    {
        if (condicion)
        {
            Bien(que);
        }
        else
        {
            Mal(que, porque);
        }
    }

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE CintaConHuecos");
        Console.WriteLine(new string('=', 70));

        SinHuecosNoSeTocaNada();
        UnHuecoEnUnaDiagonal();
        DosHuecosEnDiagonalesDistintas();
        DosHuecosEnLaMismaDiagonal();
        ElSeguroSalta();
        LosDoblecesNoSeDeforman();
        ElCasoDeVerdadDiamanteYGrapa();

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
    //  Una cinta de mentira, pero con la misma forma que la de verdad:
    //  dos vertices por circulo, arco en los pares y recta en los impares.
    // ==================================================================

    private static (double[] Pts, double[] Bulges) CintaDePrueba()
    {
        // Cuatro circulos en rombo. Los bulges de los arcos van en los indices
        // pares, como la cinta de verdad.
        var pts = new double[]
        {
            50, 10,  60, 12,     // circulo de abajo:    vertices 0 y 1
            90, 45,  90, 55,     // circulo de derecha:  vertices 2 y 3
            60, 88,  50, 90,     // circulo de arriba:   vertices 4 y 5
            10, 55,  10, 45      // circulo de izquierda:vertices 6 y 7
        };

        var bulges = new double[] { 0.12, 0, 0.12, 0, 0.12, 0, 0.12, 0 };

        return (pts, bulges);
    }

    private static double LargoDeUnTrozo(double[] pts)
    {
        var total = 0.0;

        for (var i = 0; i + 3 < pts.Length; i += 2)
        {
            var dx = pts[i + 2] - pts[i];
            var dy = pts[i + 3] - pts[i + 1];
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    private static double ContornoCerrado(double[] pts)
    {
        var m = pts.Length / 2;
        var total = 0.0;

        for (var v = 0; v < m; v++)
        {
            var w = (v + 1) % m;
            var dx = pts[2 * w] - pts[2 * v];
            var dy = pts[(2 * w) + 1] - pts[(2 * v) + 1];
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    // ==================================================================

    private static void SinHuecosNoSeTocaNada()
    {
        Console.WriteLine();
        Console.WriteLine("Sin huecos no se toca nada");

        var (pts, bulges) = CintaDePrueba();

        var trozos = CintaConHuecos.Abrir(
            pts, bulges, new List<CintaConHuecos.Hueco>(), 0.01, 0.5);

        Comprobar(
            trozos is null,
            "una lista de huecos vacia devuelve null",
            "devolvio trozos, asi que la cinta cerrada se sustituiria sin motivo");

        // Un hueco de ancho cero tampoco cuenta.
        var nulo = CintaConHuecos.Abrir(
            pts, bulges,
            new[] { new CintaConHuecos.Hueco(1, 0.4, 0.4) }, 0.01, 0.5);

        Comprobar(
            nulo is null,
            "un hueco de ancho cero devuelve null",
            "un hueco degenerado partiria la linea en dos entidades pegadas");
    }

    private static void UnHuecoEnUnaDiagonal()
    {
        Console.WriteLine();
        Console.WriteLine("Un hueco en una diagonal");

        var (pts, bulges) = CintaDePrueba();

        // El segmento 1 va del vertice 1 al 2: es una diagonal recta.
        var hueco = new CintaConHuecos.Hueco(1, 0.30, 0.45);

        var trozos = CintaConHuecos.Abrir(
            pts, bulges, new[] { hueco }, 0.001, 0.5);

        if (trozos is null)
        {
            Mal("un hueco produce un trozo", "devolvio null");
            return;
        }

        Comprobar(trozos.Count == 1, "un hueco produce UN trozo",
            $"produjo {trozos.Count}");

        var t = trozos[0];

        // El trozo tiene que recorrer la cinta entera menos el hueco: arranca en
        // s=0.45 del segmento 1, pasa por los 8 vertices y muere en s=0.30.
        Comprobar(
            t.Pts.Length / 2 == 8 + 2,
            "recorre los 8 vertices, mas los dos extremos partidos",
            $"tiene {t.Pts.Length / 2} puntos y deberia tener 10");

        Comprobar(
            t.Bulges.Length == t.Pts.Length / 2,
            "hay un bulge por punto",
            $"{t.Bulges.Length} bulges para {t.Pts.Length / 2} puntos");

        // El largo: el contorno menos el hueco.
        var largoSeg1 = Math.Sqrt(
            Math.Pow(pts[4] - pts[2], 2) + Math.Pow(pts[5] - pts[3], 2));

        var esperado = ContornoCerrado(pts) - ((0.45 - 0.30) * largoSeg1);
        var medido = LargoDeUnTrozo(t.Pts);

        Comprobar(
            Math.Abs(medido - esperado) < 1e-9,
            "lo que sobrevive mas el hueco suma el contorno original",
            $"midio {medido:F6} y se esperaba {esperado:F6}");

        // Y arranca y muere donde debe.
        var ax = pts[2] + (0.45 * (pts[4] - pts[2]));
        var ay = pts[3] + (0.45 * (pts[5] - pts[3]));

        Comprobar(
            Math.Abs(t.Pts[0] - ax) < 1e-12 && Math.Abs(t.Pts[1] - ay) < 1e-12,
            "arranca justo donde ACABA el hueco",
            $"arranca en ({t.Pts[0]:F4}, {t.Pts[1]:F4}) y deberia en ({ax:F4}, {ay:F4})");

        var bx = pts[2] + (0.30 * (pts[4] - pts[2]));
        var by = pts[3] + (0.30 * (pts[5] - pts[3]));

        Comprobar(
            Math.Abs(t.Pts[^2] - bx) < 1e-12 && Math.Abs(t.Pts[^1] - by) < 1e-12,
            "muere justo donde EMPIEZA el hueco",
            $"muere en ({t.Pts[^2]:F4}, {t.Pts[^1]:F4}) y deberia en ({bx:F4}, {by:F4})");
    }

    private static void DosHuecosEnDiagonalesDistintas()
    {
        Console.WriteLine();
        Console.WriteLine("Dos huecos en diagonales distintas");

        var (pts, bulges) = CintaDePrueba();

        var trozos = CintaConHuecos.Abrir(
            pts, bulges,
            new[]
            {
                new CintaConHuecos.Hueco(1, 0.30, 0.45),
                new CintaConHuecos.Hueco(5, 0.20, 0.35)
            },
            0.001, 0.5);

        if (trozos is null)
        {
            Mal("dos huecos producen dos trozos", "devolvio null");
            return;
        }

        Comprobar(trozos.Count == 2, "dos huecos producen DOS trozos",
            $"produjo {trozos.Count}");

        var largoSeg1 = Math.Sqrt(
            Math.Pow(pts[4] - pts[2], 2) + Math.Pow(pts[5] - pts[3], 2));
        var largoSeg5 = Math.Sqrt(
            Math.Pow(pts[12] - pts[10], 2) + Math.Pow(pts[13] - pts[11], 2));

        var esperado = ContornoCerrado(pts)
            - ((0.45 - 0.30) * largoSeg1)
            - ((0.35 - 0.20) * largoSeg5);

        var medido = trozos.Sum(t => LargoDeUnTrozo(t.Pts));

        Comprobar(
            Math.Abs(medido - esperado) < 1e-9,
            "los dos trozos suman el contorno menos los dos huecos",
            $"midio {medido:F6} y se esperaba {esperado:F6}");
    }

    private static void DosHuecosEnLaMismaDiagonal()
    {
        Console.WriteLine();
        Console.WriteLine("Dos huecos en la MISMA diagonal");

        var (pts, bulges) = CintaDePrueba();

        var trozos = CintaConHuecos.Abrir(
            pts, bulges,
            new[]
            {
                new CintaConHuecos.Hueco(1, 0.20, 0.30),
                new CintaConHuecos.Hueco(1, 0.60, 0.70)
            },
            0.001, 0.5);

        if (trozos is null)
        {
            Mal("dos huecos en el mismo segmento", "devolvio null");
            return;
        }

        Comprobar(trozos.Count == 2, "producen DOS trozos",
            $"produjo {trozos.Count}");

        // Uno de los dos es el pedacito que queda ENTRE los dos huecos: dos
        // puntos y nada mas. Es el caso que un algoritmo mal planteado se come.
        var corto = trozos.OrderBy(t => t.Pts.Length).First();

        Comprobar(
            corto.Pts.Length / 2 == 2,
            "el trozo de entre los dos huecos es una recta de dos puntos",
            $"tiene {corto.Pts.Length / 2} puntos");

        var largoSeg1 = Math.Sqrt(
            Math.Pow(pts[4] - pts[2], 2) + Math.Pow(pts[5] - pts[3], 2));

        Comprobar(
            Math.Abs(LargoDeUnTrozo(corto.Pts) - (0.30 * largoSeg1)) < 1e-9,
            "y mide lo que separa un hueco del otro",
            $"mide {LargoDeUnTrozo(corto.Pts):F6} y deberia {0.30 * largoSeg1:F6}");
    }

    private static void ElSeguroSalta()
    {
        Console.WriteLine();
        Console.WriteLine("El seguro");

        var (pts, bulges) = CintaDePrueba();

        // Huecos que se comen las cuatro diagonales enteras.
        var trozos = CintaConHuecos.Abrir(
            pts, bulges,
            new[]
            {
                new CintaConHuecos.Hueco(1, 0.0, 1.0),
                new CintaConHuecos.Hueco(3, 0.0, 1.0),
                new CintaConHuecos.Hueco(5, 0.0, 1.0),
                new CintaConHuecos.Hueco(7, 0.0, 1.0)
            },
            0.001, 0.5);

        Comprobar(
            trozos is null,
            "un hueco que se come mas de la mitad devuelve null",
            "recorto de todas formas, y eso borraria la cinta del diamante");
    }

    private static void LosDoblecesNoSeDeforman()
    {
        Console.WriteLine();
        Console.WriteLine("Los dobleces no se deforman");

        var (pts, bulges) = CintaDePrueba();

        var trozos = CintaConHuecos.Abrir(
            pts, bulges,
            new[] { new CintaConHuecos.Hueco(1, 0.30, 0.45) }, 0.001, 0.5);

        if (trozos is null)
        {
            Mal("los bulges se conservan", "devolvio null");
            return;
        }

        // Los cuatro bulges de los arcos tienen que aparecer intactos.
        var salen = trozos[0].Bulges.Where(b => Math.Abs(b) > 1e-12).ToList();

        Comprobar(
            salen.Count == 4 && salen.All(b => Math.Abs(b - 0.12) < 1e-15),
            "los cuatro arcos salen con su bulge exacto",
            $"salieron {salen.Count} bulges no nulos: "
            + string.Join(", ", salen.Select(b => b.ToString("F15"))));
    }

    // ==================================================================
    //  Y EL CASO DE VERDAD
    // ==================================================================

    private static void ElCasoDeVerdadDiamanteYGrapa()
    {
        Console.WriteLine();
        Console.WriteLine("Un diamante y una grapa de verdad");

        // Una columna de 40 x 60 cm, recubrimiento 4, estribo del #3.
        // Todo en centimetros, que es como llega a TrazoDiamante.
        const double rec = 4.0;
        const double dEst = 0.95;
        const double dVar = 1.91;
        const double rVar = dVar / 2;

        var x1 = rec;
        var y1 = rec;
        var x2 = 40 - rec;
        var y2 = 60 - rec;

        // Cuatro varillas arriba, cuatro abajo, dos por costado.
        var varSup = new List<(double X, double Y, double R)>();
        var varInf = new List<(double X, double Y, double R)>();

        for (var i = 0; i < 4; i++)
        {
            var x = x1 + dEst + rVar + (i * ((x2 - x1 - (2 * dEst) - dVar) / 3));
            varSup.Add((x, y2 - dEst - rVar, rVar));
            varInf.Add((x, y1 + dEst + rVar, rVar));
        }

        var varLat = new List<(double X, double Y, double R)>
        {
            (x1 + dEst + rVar, 20, rVar),
            (x2 - dEst - rVar, 20, rVar),
            (x1 + dEst + rVar, 38, rVar),
            (x2 - dEst - rVar, 38, rVar)
        };

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dEst, varSup, varInf, varLat);

        if (centros is null)
        {
            Mal("se arma el recorrido del diamante", "TrazoDiamante.Centros devolvio null");
            return;
        }

        Bien($"el diamante abraza {centros.Count} circulos");

        var geo = TrazoDiamante.Cinta(centros, 0);

        if (geo is null)
        {
            Mal("se arma la cinta interior", "TrazoDiamante.Cinta devolvio null");
            return;
        }

        var pts = geo.Value.Pts;
        var bulges = geo.Value.Bulges;

        // Una grapa entre las dos varillas intermedias de arriba: cruza las
        // diagonales del rombo, que es el caso que hay que abrir.
        var grapa = TrazoGrapa.Contorno(
            varSup[1].X, varSup[1].Y, rVar,
            varSup[2].X, varSup[2].Y, rVar,
            dEst, dEst * 6);

        if (grapa is null)
        {
            Mal("se arma el contorno de la grapa", "TrazoGrapa.Contorno devolvio null");
            return;
        }

        var poly = new double[grapa.Count * 2];

        for (var i = 0; i < grapa.Count; i++)
        {
            poly[2 * i] = grapa[i].X;
            poly[(2 * i) + 1] = grapa[i].Y;
        }

        Bien($"la grapa tiene {grapa.Count} puntos de contorno");

        var huecos = CintaConHuecos.Huecos(pts, bulges, new[] { poly }, 0.0005);

        Console.WriteLine($"        huecos encontrados: {huecos.Count}"
            + $" (en segmentos {string.Join(", ", huecos.Select(h => h.Segmento))})");

        Comprobar(
            huecos.Count > 0,
            "la grapa abre al menos un hueco en la cinta",
            "no abrio ninguno, asi que la linea del diamante seguiria cruzando la grapa");

        if (huecos.Count == 0)
        {
            return;
        }

        var trozos = CintaConHuecos.Abrir(pts, bulges, huecos, 0.0005, 0.5);

        if (trozos is null)
        {
            Mal("la cinta se vuelve a montar", "Abrir devolvio null");
            return;
        }

        Bien($"la cinta se monta en {trozos.Count} trozo(s)");

        // LA COMPROBACION QUE IMPORTA: ningun punto de lo que sobrevive puede
        // quedar dentro del contorno de la grapa. Si quedara, la linea del
        // diamante seguiria metida por debajo del acero de la grapa, que es
        // justo el defecto que esto arregla.
        var dentro = 0;

        foreach (var t in trozos)
        {
            for (var i = 0; i + 1 < t.Pts.Length; i += 2)
            {
                if (CintaConHuecos.PuntoEnPoligono(t.Pts[i], t.Pts[i + 1], poly))
                {
                    dentro++;
                }
            }
        }

        Comprobar(
            dentro == 0,
            "ningun punto que sobrevive queda DENTRO de la grapa",
            $"{dentro} puntos siguen dentro del contorno de la grapa");

        // Y el punto medio de cada tramo recto que sobrevive, tambien fuera:
        // los extremos podrian estar justo en el borde y colarse.
        var mediosDentro = 0;

        foreach (var t in trozos)
        {
            for (var i = 0; i + 3 < t.Pts.Length; i += 2)
            {
                var k = i / 2;

                if (k < t.Bulges.Length && Math.Abs(t.Bulges[k]) > 1e-12)
                {
                    // Es un arco: su punto medio por la cuerda no dice nada.
                    continue;
                }

                var mx = (t.Pts[i] + t.Pts[i + 2]) / 2;
                var my = (t.Pts[i + 1] + t.Pts[i + 3]) / 2;

                if (CintaConHuecos.PuntoEnPoligono(mx, my, poly))
                {
                    mediosDentro++;
                }
            }
        }

        Comprobar(
            mediosDentro == 0,
            "ni el centro de ningun tramo recto que sobrevive",
            $"{mediosDentro} tramos siguen atravesando la grapa por dentro");

        // El hueco tiene que ser modesto: es el ancho de una grapa del #3
        // sobre diagonales de decenas de centimetros.
        var contorno = ContornoCerrado(pts);
        var vive = trozos.Sum(t => LargoDeUnTrozo(t.Pts));
        var abierto = 100 * (contorno - vive) / contorno;

        Console.WriteLine($"        se abrio el {abierto:F1} % del contorno de la cinta");

        Comprobar(
            abierto is > 0 and < 25,
            "el hueco abierto es del orden del ancho de la grapa",
            $"se abrio el {abierto:F1} %, que no es un hueco de una grapa");

        // Y LA OTRA QUE IMPORTA: un doblez partido tiene que seguir estando SOBRE
        // su circunferencia. Es lo que comprueba que el bulge parcial se
        // recalculo bien; con el bulge entero el pedazo saldria abombado y se
        // saldria del doblez, que es el error que ya se cazo una vez en
        // TrazoDiamante.Muestrear.
        var arcos = 0;
        var peor = 0.0;

        foreach (var t in trozos)
        {
            for (var i = 0; i + 3 < t.Pts.Length; i += 2)
            {
                var k = i / 2;

                if (k >= t.Bulges.Length || Math.Abs(t.Bulges[k]) <= 1e-12)
                {
                    continue;
                }

                arcos++;

                var (cx, cy, r) = DesdeElBulge(
                    t.Pts[i], t.Pts[i + 1], t.Pts[i + 2], t.Pts[i + 3], t.Bulges[k]);

                // El circulo del diamante mas cercano a este centro.
                var cerca = centros
                    .OrderBy(c => ((c.X - cx) * (c.X - cx)) + ((c.Y - cy) * (c.Y - cy)))
                    .First();

                var errCentro = Math.Sqrt(
                    ((cerca.X - cx) * (cerca.X - cx)) + ((cerca.Y - cy) * (cerca.Y - cy)));

                var errRadio = Math.Abs(cerca.R - r);

                peor = Math.Max(peor, Math.Max(errCentro, errRadio));
            }
        }

        Console.WriteLine($"        arcos que sobreviven: {arcos}"
            + $", peor desvio del doblez original: {peor:E2} cm");

        Comprobar(
            arcos > 0,
            "sobrevive al menos un pedazo de doblez",
            "no quedo ningun arco, asi que esta prueba no comprueba nada");

        Comprobar(
            peor < 1e-9,
            "cada pedazo de doblez sigue sobre su circunferencia original",
            $"el peor se desvia {peor:E3} cm del doblez de verdad");
    }

    /// <summary>Centro y radio de un arco, a partir de sus extremos y su bulge.</summary>
    /// <remarks>
    /// Es la cuenta de AutoCAD, rehecha aquí <b>aparte</b> a propósito: si la prueba llamara
    /// a la misma función que el código que comprueba, un error en esa función saldría en
    /// verde. Escribirla dos veces es lo que le da valor a la comprobación.
    /// </remarks>
    private static (double Cx, double Cy, double R) DesdeElBulge(
        double ax, double ay, double bx, double by, double bulge)
    {
        var theta = 4 * Math.Atan(bulge);

        var dx = bx - ax;
        var dy = by - ay;
        var cuerda = Math.Sqrt((dx * dx) + (dy * dy));

        var r = cuerda / (2 * Math.Sin(theta / 2));
        var h = r * Math.Cos(theta / 2);

        return (((ax + bx) / 2) + (-dy / cuerda * h),
                ((ay + by) / 2) + (dx / cuerda * h),
                Math.Abs(r));
    }
}
