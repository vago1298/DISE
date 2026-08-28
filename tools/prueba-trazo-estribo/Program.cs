using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>Prueba de <see cref="TrazoEstribo"/>: el eje del estribo con sus dobleces.</summary>
internal static class Program
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string que, string porque)
    {
        if (condicion)
        {
            Console.WriteLine($"  OK    {que}");
            return;
        }

        Console.WriteLine($"  FALLA {que}");
        Console.WriteLine($"        {porque}");
        _fallos++;
    }

    // Una columna de 40 x 60, recubrimiento 4, estribo #3, varillas #6.
    // El EJE del estribo va insetado rec + dEst/2, y sus radios son (dEst + dVar)/2:
    // esas dos reglas salen de EstriboExterior y aqui se aplican tal cual.
    private const double Rec = 4.0;
    private const double DEst = 0.95;
    private const double DVar = 1.91;

    private static (double X1, double Y1, double X2, double Y2, double RSup, double RInf)
        Caso(double b = 40, double h = 60) =>
        (Rec + (DEst / 2), Rec + (DEst / 2),
         b - Rec - (DEst / 2), h - Rec - (DEst / 2),
         (DEst + DVar) / 2, (DEst + DVar) / 2);

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE TrazoEstribo");
        Console.WriteLine(new string('=', 70));

        SinGanchoElCuerpoSeCierra();
        TodoCaeDentroDelRectangulo();
        LosDoblecesEstanASuRadio();
        LosLadosSonRectos();
        ElCuerpoVaAntihorario();
        ConGanchoElCuerpoQuedaAbierto();
        LasDosColasSonParalelasYSeparadas();
        LasColasApuntanAlNucleo();
        UnRadioImposibleSeRecorta();
        SeccionDegeneradaDevuelveNull();
        ElEjeDeLaGrapa();
        ElGanchoSuelto();

        Console.WriteLine(new string('=', 70));

        if (_fallos == 0)
        {
            Console.WriteLine("TODO PASA");
            return 0;
        }

        Console.WriteLine($"{_fallos} COMPROBACIONES FALLAN");
        return 1;
    }

    private static void SinGanchoElCuerpoSeCierra()
    {
        Console.WriteLine();
        Console.WriteLine("Sin gancho");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, 0);

        if (t is null)
        {
            Comprobar(false, "se arma el trazo", "devolvio null");
            return;
        }

        Comprobar(t.Value.Cerrado, "el cuerpo se marca como cerrado",
            "quedo abierto, y sin gancho un estribo es un aro cerrado");

        Comprobar(t.Value.Colas.Count == 0, "no hay colas",
            $"salieron {t.Value.Colas.Count}");

        var p = t.Value.Cuerpo;

        // Cerrado de verdad: el ultimo punto vuelve al primero.
        var d = Math.Sqrt(
            Math.Pow(p[^1].X - p[0].X, 2) + Math.Pow(p[^1].Y - p[0].Y, 2));

        Comprobar(d < 1e-9, "y el recorrido vuelve de verdad a su arranque",
            $"el ultimo punto queda a {d:E3} cm del primero");
    }

    private static void TodoCaeDentroDelRectangulo()
    {
        Console.WriteLine();
        Console.WriteLine("Todo el cuerpo cae dentro del rectangulo del eje");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, DVar * 6);

        if (t is null)
        {
            Comprobar(false, "se arma el trazo", "devolvio null");
            return;
        }

        var peor = 0.0;

        foreach (var (x, y) in t.Value.Cuerpo)
        {
            peor = Math.Max(peor, Math.Max(
                Math.Max(x1 - x, x - x2),
                Math.Max(y1 - y, y - y2)));
        }

        Comprobar(peor < 1e-9,
            "ningun punto del cuerpo se sale del rectangulo",
            $"el peor se sale {peor:E3} cm");
    }

    private static void LosDoblecesEstanASuRadio()
    {
        Console.WriteLine();
        Console.WriteLine("Los dobleces estan a su radio exacto");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        // Los cuatro centros de doblez, con el radio que le toca a cada uno.
        var esquinas = new[]
        {
            (Cx: x2 - rS, Cy: y2 - rS, R: rS),
            (Cx: x1 + rS, Cy: y2 - rS, R: rS),
            (Cx: x1 + rI, Cy: y1 + rI, R: rI),
            (Cx: x2 - rI, Cy: y1 + rI, R: rI)
        };

        // Se miran los DOS casos, y con distinta cuenta a proposito: sin gancho el cuerpo
        // describe los CUATRO dobleces, pero con gancho la esquina del gancho ya no lleva
        // arco de cuerpo -lo ocupan las colas-, asi que son TRES.
        foreach (var (gancho, cuantasEsquinas, cual) in
            new[] { (0.0, 4, "sin gancho"), (DVar * 6, 3, "con gancho") })
        {
            var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, gancho);

            if (t is null)
            {
                Comprobar(false, $"se arma el trazo {cual}", "devolvio null");
                continue;
            }

            var peor = 0.0;
            var enDoblez = 0;

            foreach (var (x, y) in t.Value.Cuerpo)
            {
                // Un punto esta en un doblez si alguna esquina lo tiene a la distancia de
                // su radio; en un lado recto la distancia es mayor y no cuenta.
                foreach (var e in esquinas)
                {
                    var d = Math.Sqrt(
                        ((x - e.Cx) * (x - e.Cx)) + ((y - e.Cy) * (y - e.Cy)));

                    if (Math.Abs(d - e.R) > 1e-6)
                    {
                        continue;
                    }

                    enDoblez++;
                    peor = Math.Max(peor, Math.Abs(d - e.R));
                    break;
                }
            }

            Comprobar(enDoblez >= cuantasEsquinas * TrazoEstribo.TramosPorDoblez,
                $"{cual}, los {cuantasEsquinas} dobleces salen descritos",
                $"solo {enDoblez} puntos caen sobre un doblez, y hacen falta al menos "
                + $"{cuantasEsquinas * TrazoEstribo.TramosPorDoblez}");

            Comprobar(peor < 1e-9,
                $"{cual}, todos a su radio exacto del centro de su esquina",
                $"el peor se desvia {peor:E3} cm");
        }
    }

    private static void LosLadosSonRectos()
    {
        Console.WriteLine();
        Console.WriteLine("Los lados rectos son rectos");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, 0);

        if (t is null)
        {
            return;
        }

        // En un lado recto los puntos comparten una coordenada con el borde. Se mira que
        // exista al menos un tramo largo sobre cada uno de los cuatro bordes.
        var enBorde = new int[4];

        foreach (var (x, y) in t.Value.Cuerpo)
        {
            if (Math.Abs(y - y2) < 1e-9) { enBorde[0]++; }
            if (Math.Abs(x - x1) < 1e-9) { enBorde[1]++; }
            if (Math.Abs(y - y1) < 1e-9) { enBorde[2]++; }
            if (Math.Abs(x - x2) < 1e-9) { enBorde[3]++; }
        }

        Comprobar(enBorde.All(n => n >= 2),
            "los cuatro lados tienen su tramo recto sobre el borde",
            $"puntos por borde: {string.Join(", ", enBorde)}");
    }

    private static void ElCuerpoVaAntihorario()
    {
        Console.WriteLine();
        Console.WriteLine("El sentido del recorrido");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, 0);

        if (t is null)
        {
            return;
        }

        var p = t.Value.Cuerpo;
        var area = 0.0;

        for (var i = 0; i < p.Count; i++)
        {
            var j = (i + 1) % p.Count;
            area += (p[i].X * p[j].Y) - (p[j].X * p[i].Y);
        }

        area /= 2;

        Comprobar(area > 0, "el cuerpo va en sentido antihorario",
            $"el area con signo salio {area:F3} cm2, o sea horario");

        // Y el area tiene que parecerse a la del rectangulo con las esquinas comidas.
        var rect = (x2 - x1) * (y2 - y1);

        Comprobar(area < rect && area > rect * 0.9,
            "y encierra casi el rectangulo, menos lo que comen los dobleces",
            $"area {area:F2} cm2 contra un rectangulo de {rect:F2} cm2");
    }

    private static void ConGanchoElCuerpoQuedaAbierto()
    {
        Console.WriteLine();
        Console.WriteLine("Con gancho");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, DVar * 6);

        if (t is null)
        {
            Comprobar(false, "se arma el trazo", "devolvio null");
            return;
        }

        Comprobar(!t.Value.Cerrado, "el cuerpo queda abierto",
            "se cerro, y con gancho el hueco de la esquina lo ocupan las colas");

        Comprobar(t.Value.Colas.Count == 2, "salen DOS colas",
            $"salieron {t.Value.Colas.Count}, y un gancho sismico tiene dos extremos");

        // El hueco tiene que estar en la esquina del gancho: el arranque sobre el borde
        // de arriba y el final sobre el borde derecho.
        var a = t.Value.Cuerpo[0];
        var b = t.Value.Cuerpo[^1];

        Comprobar(
            Math.Abs(a.Y - y2) < 1e-9 && Math.Abs(b.X - x2) < 1e-9,
            "el hueco queda justo en la esquina de arriba a la derecha",
            $"arranca en ({a.X:F3}, {a.Y:F3}) y acaba en ({b.X:F3}, {b.Y:F3})");
    }

    private static void LasDosColasSonParalelasYSeparadas()
    {
        Console.WriteLine();
        Console.WriteLine("Las dos colas del gancho");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, DVar * 6);

        if (t is null || t.Value.Colas.Count != 2)
        {
            Comprobar(false, "hay dos colas", "no las hay");
            return;
        }

        // El tramo recto de cada cola son sus dos ultimos puntos.
        static (double X, double Y, double Dx, double Dy) Recta(
            List<(double X, double Y)> cola)
        {
            var p = cola[^2];
            var q = cola[^1];
            var l = Math.Sqrt(
                ((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y)));

            return (p.X, p.Y, (q.X - p.X) / l, (q.Y - p.Y) / l);
        }

        var r1 = Recta(t.Value.Colas[0]);
        var r2 = Recta(t.Value.Colas[1]);

        // Paralelas: el producto cruzado de sus direcciones es cero.
        var cruz = Math.Abs((r1.Dx * r2.Dy) - (r1.Dy * r2.Dx));

        Comprobar(cruz < 1e-9, "las dos colas salen PARALELAS",
            $"el cruzado de sus direcciones es {cruz:E3}, o sea que divergen");

        // Y separadas el DIAMETRO del doblez: sus puntos de salida son opuestos.
        var sep = Math.Sqrt(
            ((r2.X - r1.X) * (r2.X - r1.X)) + ((r2.Y - r1.Y) * (r2.Y - r1.Y)));

        Console.WriteLine($"        separacion entre colas: {sep:F4} cm"
            + $", diametro del doblez: {2 * rS:F4} cm");

        Comprobar(Math.Abs(sep - (2 * rS)) < 1e-9,
            "y separadas exactamente el diametro del doblez",
            $"estan a {sep:F4} cm y el diametro es {2 * rS:F4} cm");
    }

    private static void LasColasApuntanAlNucleo()
    {
        Console.WriteLine();
        Console.WriteLine("Las colas apuntan al nucleo");

        var (x1, y1, x2, y2, rS, rI) = Caso();

        var t = TrazoEstribo.Eje(x1, y1, x2, y2, rS, rI, DVar * 6);

        if (t is null || t.Value.Colas.Count != 2)
        {
            return;
        }

        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;

        var haciaDentro = 0;

        foreach (var cola in t.Value.Colas)
        {
            var p = cola[^2];
            var q = cola[^1];

            // La punta tiene que quedar mas cerca del centro que el arranque.
            var dp = Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy)));
            var dq = Math.Sqrt(((q.X - cx) * (q.X - cx)) + ((q.Y - cy) * (q.Y - cy)));

            if (dq < dp)
            {
                haciaDentro++;
            }
        }

        Comprobar(haciaDentro == 2,
            "las dos colas se meten hacia el nucleo",
            $"solo {haciaDentro} de 2 se acercan al centro");
    }

    private static void UnRadioImposibleSeRecorta()
    {
        Console.WriteLine();
        Console.WriteLine("Un radio que no cabe");

        // Un estribo estrecho con un radio enorme: si no se recortara, los dos dobleces
        // de un lado se solaparian y el recorrido se cruzaria consigo mismo.
        var t = TrazoEstribo.Eje(0, 0, 10, 4, 8, 8, 0);

        if (t is null)
        {
            Comprobar(false, "se arma el trazo", "devolvio null y el rectangulo es valido");
            return;
        }

        var peor = 0.0;

        foreach (var (x, y) in t.Value.Cuerpo)
        {
            peor = Math.Max(peor, Math.Max(
                Math.Max(0 - x, x - 10), Math.Max(0 - y, y - 4)));
        }

        Comprobar(peor < 1e-9,
            "el radio se recorta y el recorrido sigue dentro",
            $"algun punto se sale {peor:E3} cm");

        // Con el radio recortado a la mitad del lado corto, el recorrido sigue teniendo
        // area positiva: no se ha doblado sobre si mismo.
        var p = t.Value.Cuerpo;
        var area = 0.0;

        for (var i = 0; i < p.Count; i++)
        {
            var j = (i + 1) % p.Count;
            area += (p[i].X * p[j].Y) - (p[j].X * p[i].Y);
        }

        Comprobar(area / 2 > 0, "y no se cruza consigo mismo",
            $"el area con signo salio {area / 2:F3}");
    }

    private static void SeccionDegeneradaDevuelveNull()
    {
        Console.WriteLine();
        Console.WriteLine("Rectangulos imposibles");

        Comprobar(TrazoEstribo.Eje(10, 0, 10, 5, 1, 1, 0) is null,
            "ancho cero devuelve null", "devolvio un trazo");

        Comprobar(TrazoEstribo.Eje(0, 5, 10, 5, 1, 1, 0) is null,
            "alto cero devuelve null", "devolvio un trazo");

        Comprobar(TrazoEstribo.Eje(10, 0, 0, 5, 1, 1, 0) is null,
            "un rectangulo al reves devuelve null", "devolvio un trazo");
    }

    /// <summary>Corre la comprobacion del eje de la grapa y la imprime.</summary>
    private static void ElEjeDeLaGrapa()
    {
        var (fallos, lineas) = EjeDeGrapa.Correr();

        foreach (var l in lineas)
        {
            Console.WriteLine(l);
        }

        _fallos += fallos;
    }

    /// <summary>Corre la comprobacion del gancho suelto y la imprime.</summary>
    private static void ElGanchoSuelto()
    {
        var (fallos, lineas) = GanchoSuelto.Correr();

        foreach (var l in lineas)
        {
            Console.WriteLine(l);
        }

        _fallos += fallos;
    }
}



internal static class EjeDeGrapa
{
    /// <summary>
    /// El eje de la grapa: dos dobleces que envuelven cada varilla y dos colas.
    /// </summary>
    /// <remarks>
    /// Lo que se comprueba es que el eje sea de verdad <b>tangente</b>: que cada punto de un
    /// doblez esté al radio exacto de su varilla y que el tramo recto salga perpendicular al
    /// radio en la tangencia. Con eso, el eje no puede estar corrido respecto al contorno que
    /// se dibuja en el plano.
    /// </remarks>
    public static (int Fallos, string[] Lineas) Correr()
    {
        var fallos = 0;
        var salida = new List<string>();

        void Comprobar(bool cond, string que, string porque)
        {
            if (cond)
            {
                salida.Add($"  OK    {que}");
                return;
            }

            salida.Add($"  FALLA {que}");
            salida.Add($"        {porque}");
            fallos++;
        }

        salida.Add(string.Empty);
        salida.Add("El eje de la grapa");

        // Dos varillas de distinto calibre, para que la tangencia no sea el caso facil de
        // dos radios iguales.
        const double ax = 6, ay = 20, ra = 1.91 / 2;
        const double bx = 34, by = 26, rb = 2.54 / 2;
        const double dGrapa = 0.95;
        const double cola = 6 * dGrapa;

        var eje = TrazoGrapa.Eje(ax, ay, ra, bx, by, rb, dGrapa, cola);

        if (eje is null)
        {
            Comprobar(false, "se arma el eje", "devolvio null");
            return (fallos, salida.ToArray());
        }

        Comprobar(eje.Count > 2 * TrazoGrapa.TramosPorDoblez,
            "el eje trae sus dos dobleces muestreados",
            $"solo {eje.Count} puntos, y dos dobleces ya piden mas");

        var r1 = ra + (dGrapa / 2);
        var r2 = rb + (dGrapa / 2);

        // Cada punto tiene que estar, o al radio del eje de una de las dos varillas -si
        // esta en un doblez-, o sobre el tramo recto o una cola.
        var enA = 0;
        var enB = 0;
        var peor = 0.0;

        foreach (var (x, y) in eje)
        {
            var da = Math.Sqrt(((x - ax) * (x - ax)) + ((y - ay) * (y - ay)));
            var db = Math.Sqrt(((x - bx) * (x - bx)) + ((y - by) * (y - by)));

            if (Math.Abs(da - r1) < 1e-9) { enA++; peor = Math.Max(peor, Math.Abs(da - r1)); }
            if (Math.Abs(db - r2) < 1e-9) { enB++; peor = Math.Max(peor, Math.Abs(db - r2)); }
        }

        Comprobar(enA > TrazoGrapa.TramosPorDoblez / 2 && enB > TrazoGrapa.TramosPorDoblez / 2,
            "los dos dobleces envuelven su varilla al radio del eje",
            $"{enA} puntos al radio de A y {enB} al de B");

        Comprobar(peor < 1e-9,
            "y al radio EXACTO, no aproximado",
            $"el peor se desvia {peor:E3} cm");

        // Las dos colas: sus tramos rectos tienen que ser paralelos entre si, porque los
        // dos son paralelos al tramo recto de la grapa.
        static (double Dx, double Dy) Dir(
            (double X, double Y) p, (double X, double Y) q)
        {
            var l = Math.Sqrt(
                ((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y)));

            return ((q.X - p.X) / l, (q.Y - p.Y) / l);
        }

        var d1 = Dir(eje[1], eje[0]);
        var d2 = Dir(eje[^2], eje[^1]);

        var cruz = Math.Abs((d1.Dx * d2.Dy) - (d1.Dy * d2.Dx));

        Comprobar(cruz < 1e-9,
            "las dos colas salen paralelas al tramo recto",
            $"el cruzado de sus direcciones es {cruz:E3}");

        // Y las puntas apuntan a lados OPUESTOS: una cola sale por cada extremo.
        var punto = (d1.Dx * d2.Dx) + (d1.Dy * d2.Dy);

        Comprobar(punto < -0.99,
            "y cada una hacia su extremo, no las dos al mismo lado",
            $"el producto punto de sus direcciones es {punto:F4}");

        return (fallos, salida.ToArray());
    }
}


internal static class GanchoSuelto
{
    /// <summary>
    /// El gancho que envuelve una barra: el del diamante y cualquier remate parecido.
    /// </summary>
    /// <remarks>
    /// Lo que se comprueba es lo que hace que un gancho sea un gancho: que el doblez este al
    /// radio exacto de la barra, que envuelva por el lado OPUESTO a las colas -o sea que el
    /// acero pase por detras de la barra- y que las dos colas salgan paralelas y separadas el
    /// diametro del doblez, que es lo que se ve como dos rayas.
    /// </remarks>
    public static (int Fallos, string[] Lineas) Correr()
    {
        var fallos = 0;
        var salida = new List<string>();

        void Comprobar(bool cond, string que, string porque)
        {
            if (cond)
            {
                salida.Add($"  OK    {que}");
                return;
            }

            salida.Add($"  FALLA {que}");
            salida.Add($"        {porque}");
            fallos++;
        }

        salida.Add(string.Empty);
        salida.Add("El gancho que envuelve una barra");

        // Una varilla del #6 en el costado, con un diamante del #3 dandole la vuelta, y las
        // colas apuntando al nucleo. Los numeros son los de una columna de 40 x 60.
        const double bx = 5.5, by = 30.0;
        const double rVar = 1.91 / 2;
        const double dDia = 0.95;
        const double rEje = rVar + (dDia / 2);
        const double cola = 6 * dDia;

        // Al nucleo: del centro de la barra al centro de la seccion.
        var ux = 20.0 - bx;
        var uy = 30.0 - by;
        var lu = Math.Sqrt((ux * ux) + (uy * uy));
        ux /= lu;
        uy /= lu;

        var g = TrazoEstribo.GanchoAlrededorDeBarra(
            bx, by, rEje, ux, uy, Math.PI, cola);

        if (g is null)
        {
            Comprobar(false, "se arma el gancho", "devolvio null");
            return (fallos, salida.ToArray());
        }

        Comprobar(g.Count >= TrazoEstribo.TramosPorDoblez + 2,
            "trae el doblez muestreado y las dos puntas",
            $"solo {g.Count} puntos");

        // El doblez son todos los puntos menos las dos puntas.
        var peorRadio = 0.0;

        for (var i = 1; i < g.Count - 1; i++)
        {
            var d = Math.Sqrt(
                ((g[i].X - bx) * (g[i].X - bx)) + ((g[i].Y - by) * (g[i].Y - by)));

            peorRadio = Math.Max(peorRadio, Math.Abs(d - rEje));
        }

        Comprobar(peorRadio < 1e-9,
            "el doblez esta al radio EXACTO de la barra que envuelve",
            $"el peor se desvia {peorRadio:E3} cm");

        // Envuelve por DETRAS: el punto medio del doblez cae del lado opuesto a las colas.
        var medio = g[g.Count / 2];

        var haciaElMedio = ((medio.X - bx) * ux) + ((medio.Y - by) * uy);

        Comprobar(haciaElMedio < -0.9 * rEje,
            "y envuelve por el lado OPUESTO a las colas",
            $"su punto medio proyecta {haciaElMedio:F4} sobre la direccion de las colas, "
            + $"y deberia ser cerca de {-rEje:F4}");

        // Las dos colas: paralelas y separadas el diametro del doblez.
        static (double Dx, double Dy) Dir(
            (double X, double Y) p, (double X, double Y) q)
        {
            var l = Math.Sqrt(
                ((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y)));

            return ((q.X - p.X) / l, (q.Y - p.Y) / l);
        }

        // La primera cola va de su punta al arranque del doblez; la segunda al contrario.
        var d1 = Dir(g[0], g[1]);
        var d2 = Dir(g[^2], g[^1]);

        var cruz = Math.Abs((d1.Dx * d2.Dy) - (d1.Dy * d2.Dx));

        Comprobar(cruz < 1e-9, "las dos colas salen PARALELAS",
            $"el cruzado de sus direcciones es {cruz:E3}");

        // Y opuestas: una entra al doblez y la otra sale.
        var punto = (d1.Dx * d2.Dx) + (d1.Dy * d2.Dy);

        Comprobar(punto < -0.99, "una entra al doblez y la otra sale",
            $"su producto punto es {punto:F4}");

        // La segunda apunta al nucleo.
        Comprobar((d2.Dx * ux) + (d2.Dy * uy) > 0.99,
            "y la que sale apunta al nucleo",
            $"proyecta {(d2.Dx * ux) + (d2.Dy * uy):F4} sobre la direccion pedida");

        var sep = Math.Sqrt(
            ((g[^2].X - g[1].X) * (g[^2].X - g[1].X))
            + ((g[^2].Y - g[1].Y) * (g[^2].Y - g[1].Y)));

        salida.Add($"        separacion entre colas: {sep:F4} cm"
            + $", diametro del doblez: {2 * rEje:F4} cm");

        Comprobar(Math.Abs(sep - (2 * rEje)) < 1e-9,
            "y estan separadas el diametro del doblez",
            $"estan a {sep:F4} y el diametro es {2 * rEje:F4}");

        // Y el largo de cada cola es el pedido.
        var largoCola = Math.Sqrt(
            ((g[1].X - g[0].X) * (g[1].X - g[0].X))
            + ((g[1].Y - g[0].Y) * (g[1].Y - g[0].Y)));

        Comprobar(Math.Abs(largoCola - cola) < 1e-9,
            "cada cola mide lo que se pidio",
            $"mide {largoCola:F4} y se pidio {cola:F4}");

        // Degenerados
        Comprobar(
            TrazoEstribo.GanchoAlrededorDeBarra(0, 0, 0, 1, 0, Math.PI, 5) is null,
            "radio cero devuelve null", "devolvio un trazo");

        Comprobar(
            TrazoEstribo.GanchoAlrededorDeBarra(0, 0, 1, 0, 0, Math.PI, 5) is null,
            "sin direccion de colas devuelve null", "devolvio un trazo");

        Comprobar(
            TrazoEstribo.GanchoAlrededorDeBarra(0, 0, 1, 1, 0, 0, 5) is null,
            "barrido cero devuelve null", "devolvio un trazo");

        return (fallos, salida.ToArray());
    }
}
