using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba de <see cref="EjeDeBarra"/>: la geometría con la que la jaula de armado se vuelve
/// sólidos dentro de AutoCAD.
/// </summary>
/// <remarks>
/// El razonamiento de qué se cubre y por qué está en el .csproj. En resumen: que simplificar el
/// eje no mueva las puntas, que los tramos se solapen en las uniones y <b>solo</b> en las uniones,
/// y que la matriz lleve de verdad las dos tapas del cilindro a las dos puntas del tramo.
/// </remarks>
internal static class Program
{
    private static int _fallos;

    private const double Tol = 1e-9;

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
        Console.WriteLine("PRUEBA DE EjeDeBarra");
        Console.WriteLine(new string('=', 70));

        LoQueYaHabia();
        SimplificadoNoMueveLasPuntas();
        SimplificadoGarantizaElError();
        SimplificadoBajaLaCuenta();
        SimplificadoNoSeComeLasEsquinas();
        TramosNoAlarganLasPuntasLibres();
        TramosSolapanEnLasUniones();
        TramosDeUnRecorridoCerrado();
        LaMatrizEsUnMarcoDerecho();
        LaMatrizLlevaElCilindroASuSitio();
        ElCasoVertical();
        UnEstriboCompleto();
        Degenerados();

        Console.WriteLine(new string('=', 70));

        if (_fallos == 0)
        {
            Console.WriteLine("TODO PASA");
            return 0;
        }

        Console.WriteLine($"{_fallos} FALLO(S)");
        return 1;
    }

    // ================================================================= utilidades

    private static (double X, double Y, double Z) P(double x, double y, double z) => (x, y, z);

    private static double Dist(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// Aplica la matriz a un punto <b>como lo hace AutoCAD</b>: por filas, con la cuarta columna
    /// de traslación.
    /// </summary>
    private static (double X, double Y, double Z) Aplicar(double[,] m, double x, double y, double z) =>
        ((m[0, 0] * x) + (m[0, 1] * y) + (m[0, 2] * z) + m[0, 3],
         (m[1, 0] * x) + (m[1, 1] * y) + (m[1, 2] * z) + m[1, 3],
         (m[2, 0] * x) + (m[2, 1] * y) + (m[2, 2] * z) + m[2, 3]);

    /// <summary>Una columna del marco: 0 = u, 1 = v, 2 = w.</summary>
    private static (double X, double Y, double Z) Col(double[,] m, int c) =>
        (m[0, c], m[1, c], m[2, c]);

    private static double Punto(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double Norma((double X, double Y, double Z) a) => Math.Sqrt(Punto(a, a));

    /// <summary>Un doblez de 90° muestreado, como los que hace la vista previa.</summary>
    /// <remarks>
    /// Arranca recto por la X, dobla noventa grados con <paramref name="muestras"/> puntos y sigue
    /// recto por la Y. Es la forma de la esquina de un estribo.
    /// </remarks>
    private static List<(double X, double Y, double Z)> Codo(int muestras, double radio = 2)
    {
        var pts = new List<(double X, double Y, double Z)> { P(-10, -radio, 0) };

        // El arco, centrado en el origen, de la direccion +X a la direccion +Y.
        for (var i = 0; i <= muestras; i++)
        {
            var a = -Math.PI / 2 + (Math.PI / 2 * i / muestras);

            pts.Add(P(radio * Math.Cos(a), radio * Math.Sin(a), 0));
        }

        pts.Add(P(radio, 10, 0));

        return pts;
    }

    /// <summary>
    /// Un <b>gancho sísmico</b>: 135° en un radio pequeño y una cola. Es la forma que se veía con
    /// aristas.
    /// </summary>
    private static List<(double X, double Y, double Z)> Gancho(int muestras, double radio = 1.2)
    {
        var pts = new List<(double X, double Y, double Z)> { P(-6, -radio, 0) };

        for (var i = 0; i <= muestras; i++)
        {
            var a = -Math.PI / 2 + (Math.PI * 0.75 * i / muestras);

            pts.Add(P(radio * Math.Cos(a), radio * Math.Sin(a), 0));
        }

        // La cola, en la direccion en la que quedo el doblez.
        var fin = -Math.PI / 2 + (Math.PI * 0.75);

        pts.Add(P(
            (radio * Math.Cos(fin)) - (6 * Math.Sin(fin)),
            (radio * Math.Sin(fin)) + (6 * Math.Cos(fin)),
            0));

        return pts;
    }

    /// <summary>Un aro cerrado, muestreado.</summary>
    private static List<(double X, double Y, double Z)> Aro(int muestras, double radio = 10)
    {
        var pts = new List<(double X, double Y, double Z)>();

        for (var i = 0; i < muestras; i++)
        {
            var a = 2 * Math.PI * i / muestras;

            pts.Add(P(radio * Math.Cos(a), radio * Math.Sin(a), 0));
        }

        pts.Add(pts[0]);

        return pts;
    }

    /// <summary>Lo que se separa un punto del <b>recorrido entero</b>, tramo a tramo.</summary>
    /// <remarks>
    /// Es la medida con la que se comprueba la garantía de <c>Simplificado</c>. Se calcula aquí, en
    /// la prueba, y a propósito: si se usara la misma cuenta que usa la clase probada, la prueba no
    /// probaría nada.
    /// </remarks>
    private static double AlRecorrido(
        (double X, double Y, double Z) p, List<(double X, double Y, double Z)> r)
    {
        var mejor = double.MaxValue;

        for (var i = 1; i < r.Count; i++)
        {
            var a = r[i - 1];
            var b = r[i];

            var vx = b.X - a.X;
            var vy = b.Y - a.Y;
            var vz = b.Z - a.Z;

            var largo2 = (vx * vx) + (vy * vy) + (vz * vz);

            double d;

            if (largo2 <= 1e-18)
            {
                d = Dist(p, a);
            }
            else
            {
                var t = (((p.X - a.X) * vx) + ((p.Y - a.Y) * vy) + ((p.Z - a.Z) * vz)) / largo2;

                t = Math.Clamp(t, 0d, 1d);

                d = Dist(p, (a.X + (vx * t), a.Y + (vy * t), a.Z + (vz * t)));
            }

            if (d < mejor)
            {
                mejor = d;
            }
        }

        return mejor;
    }

    // ================================================================= las pruebas

    /// <summary>Lo que ya existía sigue igual: no se rompió nada al añadir lo nuevo.</summary>
    private static void LoQueYaHabia()
    {
        Console.WriteLine("\nLo que ya habia (Limpio, Largo, Cerrado, TangenteInicial, Tira)");

        var conRepes = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(0, 0, 0), P(1, 0, 0), P(1, 0, 0), P(1, 1, 0)
        };

        var limpio = EjeDeBarra.Limpio(conRepes);

        Comprobar(limpio.Count == 3, $"Limpio quita los repetidos seguidos: {limpio.Count} de 5");

        var largoBien = Math.Abs(EjeDeBarra.Largo(limpio) - 2) < Tol;

        Comprobar(largoBien, "Largo suma tramo a tramo: 2");

        var cuadro = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), P(0, 0, 0)
        };

        Comprobar(EjeDeBarra.Cerrado(cuadro), "Cerrado reconoce el recorrido que vuelve");
        Comprobar(!EjeDeBarra.Cerrado(limpio), "y el que no vuelve, no");

        var t = EjeDeBarra.TangenteInicial(limpio);

        var tangenteVaPorX = Math.Abs(t.X - 1) < Tol && Math.Abs(t.Y) < Tol;

        Comprobar(tangenteVaPorX, "TangenteInicial va por el primer tramo con largo");

        var tira = EjeDeBarra.Tira(limpio);

        Comprobar(tira.Length == 9, $"Tira sale de tres en tres: {tira.Length}");
        var enOrden = Math.Abs(tira[3] - 1) < Tol;

        Comprobar(enOrden, "y en el orden x, y, z");
    }

    /// <summary>
    /// Lo que el usuario acota: el principio y el final de la varilla no se mueven al simplificar.
    /// </summary>
    private static void SimplificadoNoMueveLasPuntas()
    {
        Console.WriteLine("\nSimplificado NO mueve las puntas (es lo que se acota)");

        var codo = Codo(14);

        foreach (var t in new[] { 0.001, 0.02, 0.1, 0.5, 2.0 })
        {
            var s = EjeDeBarra.Simplificado(codo, t);

            Comprobar(Dist(s[0], codo[0]) < Tol,
                $"con tolerancia {t} el primer punto es el mismo");

            Comprobar(Dist(s[^1], codo[^1]) < Tol,
                $"con tolerancia {t} el ultimo punto es el mismo");

            Comprobar(s.Count >= 2, $"con tolerancia {t} quedan al menos dos puntos: {s.Count}");
        }

        // Y el largo no se dispara: enderezar acorta un poco, pero no puede alargar.
        var largoO = EjeDeBarra.Largo(codo);
        var largoS = EjeDeBarra.Largo(EjeDeBarra.Simplificado(codo, 0.02));

        Comprobar(largoS <= largoO + Tol,
            $"simplificar no alarga la varilla: {largoS:0.####} <= {largoO:0.####}");

        Comprobar(largoS > largoO * 0.98,
            $"y tampoco la acorta de forma apreciable: {largoS:0.####} vs {largoO:0.####}");

        // Un recorrido CERRADO sigue cerrado, porque su ultimo punto es su primero.
        var aro = new List<(double X, double Y, double Z)>();

        for (var i = 0; i < 40; i++)
        {
            var a = 2 * Math.PI * i / 40;

            aro.Add(P(Math.Cos(a) * 10, Math.Sin(a) * 10, 0));
        }

        aro.Add(aro[0]);

        var aroS = EjeDeBarra.Simplificado(aro, 0.02);

        Comprobar(EjeDeBarra.Cerrado(aroS),
            $"un recorrido cerrado sigue cerrado al simplificar ({aroS.Count} puntos)");
    }

    /// <summary>
    /// <b>La garantía</b>: ningún punto del eje original queda a más de la tolerancia del recorrido
    /// que sale. Es lo que hace que se pueda razonar «la varilla no se sale de su sitio más de
    /// tanto», y es la razón de simplificar por distancia y no por grados.
    /// </summary>
    private static void SimplificadoGarantizaElError()
    {
        Console.WriteLine("\nSimplificado GARANTIZA el error (ningun punto se sale de la tolerancia)");

        // Se prueba sobre formas distintas: un codo, un aro cerrado y un gancho de 135 grados,
        // que es el caso que se veia con aristas.
        var formas = new (string Nombre, List<(double X, double Y, double Z)> Eje)[]
        {
            ("un codo de 90°", Codo(14)),
            ("un codo abierto de radio 8", Codo(14, 8)),
            ("un gancho de 135°", Gancho(14)),
            ("un aro cerrado", Aro(40))
        };

        foreach (var (nombre, eje) in formas)
        {
            foreach (var tol in new[] { 0.005, 0.02, 0.1 })
            {
                var s = EjeDeBarra.Simplificado(eje, tol);

                // El peor punto del original contra el recorrido que salio.
                var peor = eje.Max(p => AlRecorrido(p, s));

                Comprobar(peor <= tol + 1e-12,
                    $"{nombre}, tolerancia {tol}: el peor punto se separa {peor:0.######}");
            }
        }

        // Y afinar la tolerancia NO puede quitar puntos: mas fino, mas tramos.
        var gancho = Gancho(14);

        var fino = EjeDeBarra.Simplificado(gancho, 0.005).Count;
        var medio = EjeDeBarra.Simplificado(gancho, 0.02).Count;
        var basto = EjeDeBarra.Simplificado(gancho, 0.1).Count;

        Comprobar(fino >= medio && medio >= basto,
            $"mas fino, mas puntos: {fino} >= {medio} >= {basto}");

        // Y el gancho, que es lo que se veia mal, recibe tramos de sobra con la tolerancia que usa
        // el dibujante DE VERDAD: se toma su constante, no una copia, para que no se separen.
        const double radioVarilla = 0.635;

        var comoEnElDibujo = EjeDeBarra.Simplificado(
            gancho, radioVarilla * Jaula3dDrawer.ToleranciaEnRadios);

        Comprobar(comoEnElDibujo.Count >= 6,
            $"el gancho de una varilla del 4 sale con {comoEnElDibujo.Count} puntos (>= 6)");
    }

    /// <summary>El motivo de existir de <c>Simplificado</c>: que bajen los sólidos.</summary>
    private static void SimplificadoBajaLaCuenta()
    {
        Console.WriteLine("\nSimplificado baja la cuenta de solidos");

        var codo = Codo(14);

        var s = EjeDeBarra.Simplificado(codo, 0.02);

        Comprobar(s.Count < codo.Count,
            $"un codo de 14 muestras baja de {codo.Count} a {s.Count} puntos");

        Comprobar(s.Count <= 12,
            $"un doblez de 90° queda en {s.Count} puntos (<= 12)");

        // Y con mas margen, menos puntos: la tolerancia hace algo.
        var basto = EjeDeBarra.Simplificado(codo, 0.3);

        Comprobar(basto.Count <= s.Count,
            $"con 0,3 no quedan mas puntos que con 0,02: {basto.Count} <= {s.Count}");

        // Una RECTA con puntos de sobra se queda en sus dos extremos: no se separa de su cuerda
        // en ningun sitio, asi que no hace falta ni un vertice.
        var recta = new List<(double X, double Y, double Z)>();

        for (var i = 0; i <= 20; i++)
        {
            recta.Add(P(i * 0.5, 0, 0));
        }

        var rectaS = EjeDeBarra.Simplificado(recta, 0.02);

        Comprobar(rectaS.Count == 2,
            $"una recta de 21 puntos se queda en 2: {rectaS.Count}");

        var rectaIntacta = Math.Abs(EjeDeBarra.Largo(rectaS) - 10) < Tol;

        Comprobar(rectaIntacta, "y con el largo intacto");

        // Con tolerancia cero o negativa NO se toca nada: es la valvula de escape.
        var igual = EjeDeBarra.Simplificado(codo, 0);

        Comprobar(igual.Count == codo.Count,
            $"con tolerancia 0 el eje no se toca: {igual.Count} de {codo.Count}");

        Comprobar(EjeDeBarra.Simplificado(codo, -5).Count == codo.Count,
            "y con una tolerancia negativa tampoco");
    }

    /// <summary>Una esquina de verdad no se puede perder: es la forma del estribo.</summary>
    private static void SimplificadoNoSeComeLasEsquinas()
    {
        Console.WriteLine("\nSimplificado NO se come las esquinas vivas");

        // Un cuadro con esquinas de 90 grados exactos y muchos puntos en los lados rectos.
        var cuadro = new List<(double X, double Y, double Z)>();

        void Lado(double x0, double y0, double x1, double y1)
        {
            for (var i = 0; i < 10; i++)
            {
                var f = (double)i / 10;

                cuadro.Add(P(x0 + ((x1 - x0) * f), y0 + ((y1 - y0) * f), 0));
            }
        }

        Lado(0, 0, 30, 0);
        Lado(30, 0, 30, 50);
        Lado(30, 50, 0, 50);
        Lado(0, 50, 0, 0);

        cuadro.Add(cuadro[0]);

        var s = EjeDeBarra.Simplificado(cuadro, 0.02);

        // Las cuatro esquinas tienen que seguir ahi. Se buscan por posicion.
        foreach (var esq in new[] { P(30, 0, 0), P(30, 50, 0), P(0, 50, 0) })
        {
            Comprobar(s.Any(p => Dist(p, esq) < 1e-6),
                $"la esquina ({esq.X}, {esq.Y}) sobrevive");
        }

        Comprobar(Dist(s[0], P(0, 0, 0)) < Tol && Dist(s[^1], P(0, 0, 0)) < Tol,
            "y el cuadro sigue arrancando y acabando en su origen");

        // El perimetro se conserva: si se hubiera comido una esquina, se acortaria.
        Comprobar(Math.Abs(EjeDeBarra.Largo(s) - 160) < 1e-6,
            $"el perimetro sigue siendo 160: {EjeDeBarra.Largo(s):0.####}");

        Comprobar(s.Count <= 8,
            $"y bajo de {cuadro.Count} a {s.Count} puntos");
    }

    /// <summary>
    /// Lo que rompe la medida: alargar las puntas libres haría la varilla más larga que la de la
    /// tabla.
    /// </summary>
    private static void TramosNoAlarganLasPuntasLibres()
    {
        Console.WriteLine("\nTramos NO alargan las puntas libres");

        const double r = 0.5;

        // Dos puntos: UN tramo, y las dos puntas son libres.
        var recta = new List<(double X, double Y, double Z)> { P(0, 0, 0), P(10, 0, 0) };

        var t1 = EjeDeBarra.Tramos(recta, r);

        Comprobar(t1.Count == 1, $"una varilla recta es un solo tramo: {t1.Count}");
        Comprobar(Dist(t1[0].A, P(0, 0, 0)) < Tol, "que arranca exactamente donde el eje");
        Comprobar(Dist(t1[0].B, P(10, 0, 0)) < Tol, "y acaba exactamente donde el eje");

        // Tres puntos: dos tramos. Se alarga la union y NO las dos puntas.
        var codo = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(10, 0, 0), P(10, 10, 0)
        };

        var t2 = EjeDeBarra.Tramos(codo, r);

        Comprobar(t2.Count == 2, $"un codo son dos tramos: {t2.Count}");

        Comprobar(Dist(t2[0].A, P(0, 0, 0)) < Tol, "la punta libre del principio no se mueve");
        Comprobar(Dist(t2[^1].B, P(10, 10, 0)) < Tol, "ni la punta libre del final");

        Comprobar(Dist(t2[0].B, P(10 + r, 0, 0)) < Tol,
            $"el primer tramo pasa la union por {r}: ({t2[0].B.X}, {t2[0].B.Y})");

        Comprobar(Dist(t2[1].A, P(10, -r, 0)) < Tol,
            $"y el segundo arranca antes de la union por {r}: ({t2[1].A.X}, {t2[1].A.Y})");

        // Sin alargue, los tramos son los del eje pelado.
        var t0 = EjeDeBarra.Tramos(codo, 0);

        Comprobar(Dist(t0[0].B, P(10, 0, 0)) < Tol && Dist(t0[1].A, P(10, 0, 0)) < Tol,
            "con alargue 0 los tramos se tocan sin solaparse");
    }

    /// <summary>Que el doblez quede lleno: la unión tiene que caer dentro de los dos cilindros.</summary>
    private static void TramosSolapanEnLasUniones()
    {
        Console.WriteLine("\nTramos solapan en las uniones (el doblez no queda comido)");

        const double r = 0.6;

        var codo = EjeDeBarra.Simplificado(Codo(14), 0.02);

        var tramos = EjeDeBarra.Tramos(codo, r);

        Comprobar(tramos.Count == codo.Count - 1,
            $"un tramo por segmento del eje: {tramos.Count} de {codo.Count - 1}");

        var todasDentro = true;

        for (var k = 1; k < codo.Count - 1; k++)
        {
            var union = codo[k];

            // La union es el final del tramo k-1 y el principio del tramo k. Tiene que caer
            // DENTRO de los dos, o sea con su proyeccion sobre el eje entre 0 y el largo.
            foreach (var i in new[] { k - 1, k })
            {
                var (a, b) = tramos[i];

                var largo = Dist(a, b);

                var w = P((b.X - a.X) / largo, (b.Y - a.Y) / largo, (b.Z - a.Z) / largo);

                var d = Punto(P(union.X - a.X, union.Y - a.Y, union.Z - a.Z), w);

                if (d <= Tol || d >= largo - Tol)
                {
                    todasDentro = false;
                }
            }
        }

        Comprobar(todasDentro,
            "cada union cae dentro de los dos cilindros que la comparten");

        // Y el solape es de verdad: los dos cilindros se pisan.
        var pisan = true;

        for (var i = 1; i < tramos.Count; i++)
        {
            // El final del tramo anterior tiene que estar mas alla del principio del actual,
            // medido sobre el eje del actual.
            var (a, b) = tramos[i];

            var largo = Dist(a, b);

            var w = P((b.X - a.X) / largo, (b.Y - a.Y) / largo, (b.Z - a.Z) / largo);

            var fin = tramos[i - 1].B;

            var d = Punto(P(fin.X - a.X, fin.Y - a.Y, fin.Z - a.Z), w);

            if (d <= 0)
            {
                pisan = false;
            }
        }

        Comprobar(pisan, "y el final de cada tramo entra en el siguiente");
    }

    /// <summary>Un estribo no tiene puntas libres: se alarga por los dos lados.</summary>
    private static void TramosDeUnRecorridoCerrado()
    {
        Console.WriteLine("\nTramos de un recorrido CERRADO (un estribo, un diamante)");

        const double r = 0.5;

        var cuadro = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0), P(0, 0, 0)
        };

        Comprobar(EjeDeBarra.Cerrado(cuadro), "el cuadro se reconoce cerrado");

        var t = EjeDeBarra.Tramos(cuadro, r);

        Comprobar(t.Count == 4, $"cuatro lados, cuatro tramos: {t.Count}");

        // NINGUNA punta se queda sin alargar: en un cerrado todas son uniones.
        Comprobar(Dist(t[0].A, P(-r, 0, 0)) < Tol,
            $"el primer tramo tambien se alarga hacia atras: ({t[0].A.X}, {t[0].A.Y})");

        Comprobar(Dist(t[^1].B, P(0, -r, 0)) < Tol,
            $"y el ultimo hacia delante: ({t[^1].B.X}, {t[^1].B.Y})");

        // Todos miden lo mismo: el lado mas los dos alargues.
        var todos = t.All(x => Math.Abs(Dist(x.A, x.B) - (10 + (2 * r))) < Tol);

        Comprobar(todos, $"los cuatro miden {10 + (2 * r)}");

        // Y el mismo cuadro ABIERTO -sin repetir el primer punto- da tres tramos con las dos
        // puntas intactas. Es exactamente el fallo que tenia el diamante.
        var abierto = cuadro.Take(4).ToList();

        var ta = EjeDeBarra.Tramos(abierto, r);

        Comprobar(ta.Count == 3, $"sin cerrar son tres tramos y no cuatro: {ta.Count}");

        Comprobar(Dist(ta[0].A, P(0, 0, 0)) < Tol,
            "y entonces si hay punta libre, que no se alarga");
    }

    /// <summary>El marco tiene que ser ortonormal y derecho, o AutoCAD voltea el sólido.</summary>
    private static void LaMatrizEsUnMarcoDerecho()
    {
        Console.WriteLine("\nMatrizDeTramo es un marco ortonormal y DERECHO");

        var casos = new[]
        {
            (P(0, 0, 0), P(10, 0, 0), "por la X"),
            (P(0, 0, 0), P(0, 10, 0), "por la Y"),
            (P(0, 0, 0), P(0, 0, 10), "por la Z (una varilla longitudinal)"),
            (P(0, 0, 0), P(0, 0, -10), "por la Z hacia abajo"),
            (P(1, 2, 3), P(4, 8, 15), "en diagonal"),
            (P(-5, 7, -2), P(-5, 7, 9), "vertical fuera del origen"),
            (P(3, 3, 3), P(3.001, 3, 3.0001), "un tramo cortisimo")
        };

        foreach (var (a, b, como) in casos)
        {
            var m = EjeDeBarra.MatrizDeTramo(a, b);

            if (m is null)
            {
                Comprobar(false, $"{como}: hay matriz");
                continue;
            }

            var u = Col(m, 0);
            var v = Col(m, 1);
            var w = Col(m, 2);

            Comprobar(
                Math.Abs(Norma(u) - 1) < 1e-12
                && Math.Abs(Norma(v) - 1) < 1e-12
                && Math.Abs(Norma(w) - 1) < 1e-12,
                $"{como}: las tres columnas son unitarias");

            Comprobar(
                Math.Abs(Punto(u, v)) < 1e-12
                && Math.Abs(Punto(u, w)) < 1e-12
                && Math.Abs(Punto(v, w)) < 1e-12,
                $"{como}: y perpendiculares entre si");

            // w tiene que ser la direccion del tramo.
            var largo = Dist(a, b);

            var dir = P((b.X - a.X) / largo, (b.Y - a.Y) / largo, (b.Z - a.Z) / largo);

            Comprobar(Dist(w, dir) < 1e-12,
                $"{como}: la tercera columna es la direccion del tramo");

            // u x v = w: el marco es derecho, determinante +1.
            var cruz = P(
                (u.Y * v.Z) - (u.Z * v.Y),
                (u.Z * v.X) - (u.X * v.Z),
                (u.X * v.Y) - (u.Y * v.X));

            Comprobar(Dist(cruz, w) < 1e-12,
                $"{como}: u x v = w, o sea determinante +1 y sin espejo");

            // La traslacion es el PUNTO MEDIO, porque el cilindro nace centrado.
            var medio = P((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);

            Comprobar(Math.Abs(m[0, 3] - medio.X) < 1e-12
                      && Math.Abs(m[1, 3] - medio.Y) < 1e-12
                      && Math.Abs(m[2, 3] - medio.Z) < 1e-12,
                $"{como}: la traslacion es el punto medio y no el principio");

            // Y la ultima fila es la de una transformacion afin.
            Comprobar(m[3, 0] == 0 && m[3, 1] == 0 && m[3, 2] == 0 && m[3, 3] == 1,
                $"{como}: la ultima fila es 0 0 0 1");
        }
    }

    /// <summary>
    /// Lo único que de verdad se mide en el plano: que las dos tapas del cilindro caigan en las dos
    /// puntas del tramo.
    /// </summary>
    private static void LaMatrizLlevaElCilindroASuSitio()
    {
        Console.WriteLine("\nAplicada, la matriz lleva las tapas del cilindro a las puntas");

        var casos = new[]
        {
            (P(0, 0, 0), P(10, 0, 0)),
            (P(0, 0, 0), P(0, 0, 300)),
            (P(1, 2, 3), P(4, 8, 15)),
            (P(-5, 7, -2), P(-5, 7, 9)),
            (P(2, 2, 9), P(2, 2, 1))
        };

        const double radio = 0.6;

        foreach (var (a, b) in casos)
        {
            var m = EjeDeBarra.MatrizDeTramo(a, b);

            if (m is null)
            {
                Comprobar(false, "hay matriz");
                continue;
            }

            var largo = Dist(a, b);

            // AutoCAD hace el cilindro DE PIE y CENTRADO en el origen: sus tapas estan en
            // (0, 0, -largo/2) y (0, 0, +largo/2).
            var tapaA = Aplicar(m, 0, 0, -largo / 2);
            var tapaB = Aplicar(m, 0, 0, +largo / 2);

            Comprobar(Dist(tapaA, a) < 1e-9,
                $"la tapa de abajo cae en el principio del tramo ({a.X}, {a.Y}, {a.Z})");

            Comprobar(Dist(tapaB, b) < 1e-9,
                $"y la de arriba en el final ({b.X}, {b.Y}, {b.Z})");

            // Un punto del BORDE de la tapa sigue a su radio del eje: el cilindro no se
            // deforma ni cambia de grueso.
            var borde = Aplicar(m, radio, 0, -largo / 2);

            Comprobar(Math.Abs(Dist(borde, a) - radio) < 1e-9,
                $"y el borde de la tapa sigue a {radio} del eje: {Dist(borde, a):0.#########}");

            // El largo se conserva: la matriz gira y traslada, no escala.
            Comprobar(Math.Abs(Dist(tapaA, tapaB) - largo) < 1e-9,
                $"el cilindro conserva su largo: {largo:0.####}");
        }
    }

    /// <summary>
    /// Todas las varillas longitudinales son verticales: si esto falla, falla la mitad del acero.
    /// </summary>
    private static void ElCasoVertical()
    {
        Console.WriteLine("\nEl caso VERTICAL (son todas las varillas longitudinales)");

        // Una varilla de columna: de la base a la corona, en metros de dibujo.
        var a = P(0.05, 0.05, 0);
        var b = P(0.05, 0.05, 3.2);

        var m = EjeDeBarra.MatrizDeTramo(a, b);

        Comprobar(m is not null, "una varilla vertical tiene matriz (no se anula el producto)");

        if (m is null)
        {
            return;
        }

        var w = Col(m, 2);

        Comprobar(Math.Abs(w.Z - 1) < 1e-12 && Math.Abs(w.X) < 1e-12 && Math.Abs(w.Y) < 1e-12,
            "y su direccion es la vertical del dibujo");

        var largo = Dist(a, b);

        Comprobar(Dist(Aplicar(m, 0, 0, -largo / 2), a) < 1e-9, "arranca en la base");
        Comprobar(Dist(Aplicar(m, 0, 0, largo / 2), b) < 1e-9, "y acaba en la corona");

        // Hacia abajo tambien: el modelo puede traer la varilla definida al reves.
        var mAbajo = EjeDeBarra.MatrizDeTramo(b, a);

        Comprobar(mAbajo is not null, "y definida al reves, tambien");

        if (mAbajo is not null)
        {
            Comprobar(Math.Abs(Col(mAbajo, 2).Z + 1) < 1e-12, "con la direccion volteada");

            var cruz = P(
                (Col(mAbajo, 0).Y * Col(mAbajo, 1).Z) - (Col(mAbajo, 0).Z * Col(mAbajo, 1).Y),
                (Col(mAbajo, 0).Z * Col(mAbajo, 1).X) - (Col(mAbajo, 0).X * Col(mAbajo, 1).Z),
                (Col(mAbajo, 0).X * Col(mAbajo, 1).Y) - (Col(mAbajo, 0).Y * Col(mAbajo, 1).X));

            Comprobar(Dist(cruz, Col(mAbajo, 2)) < 1e-12,
                "y el marco sigue derecho, no espejado");
        }

        // Y un tramo casi vertical, que es donde una perpendicular mal elegida se degrada.
        foreach (var inclina in new[] { 1e-3, 1e-5, 1e-7 })
        {
            var c = P(0.05 + inclina, 0.05, 3.2);

            var mc = EjeDeBarra.MatrizDeTramo(a, c);

            Comprobar(mc is not null && Math.Abs(Norma(Col(mc, 0)) - 1) < 1e-9,
                $"un tramo casi vertical (desvio {inclina}) sigue dando un marco sano");
        }
    }

    /// <summary>
    /// El caso de verdad, de punta a punta: el eje de un estribo cerrado se vuelve una fila de
    /// cilindros sin huecos y sin pasarse de largo.
    /// </summary>
    private static void UnEstriboCompleto()
    {
        Console.WriteLine("\nUn estribo completo: del eje a los cilindros");

        // Un estribo de 30x50 con las esquinas redondeadas a 14 muestras, como la vista previa.
        const double rad = 2.5;
        const double diam = 0.95;

        var eje = new List<(double X, double Y, double Z)>();

        void Esquina(double cx, double cy, double desde)
        {
            for (var i = 0; i <= 14; i++)
            {
                var ang = desde + (Math.PI / 2 * i / 14);

                eje.Add(P(cx + (rad * Math.Cos(ang)), cy + (rad * Math.Sin(ang)), 0));
            }
        }

        Esquina(30 - rad, rad, -Math.PI / 2);
        Esquina(30 - rad, 50 - rad, 0);
        Esquina(rad, 50 - rad, Math.PI / 2);
        Esquina(rad, rad, Math.PI);

        eje.Add(eje[0]);

        Comprobar(EjeDeBarra.Cerrado(eje), "el estribo se reconoce cerrado");

        var crudo = EjeDeBarra.Tramos(eje, diam / 2).Count;

        var simple = EjeDeBarra.Simplificado(
            eje, diam / 2 * Jaula3dDrawer.ToleranciaEnRadios);

        var tramos = EjeDeBarra.Tramos(simple, diam / 2);

        Comprobar(EjeDeBarra.Cerrado(simple), "y sigue cerrado despues de simplificar");

        Comprobar(tramos.Count < crudo,
            $"los solidos bajan de {crudo} a {tramos.Count} por estribo");

        Comprobar(tramos.Count <= 40,
            $"que con 30 estribos son {tramos.Count * 30} y no {crudo * 30}");

        // Ninguno degenerado: todos tienen matriz.
        var conMatriz = tramos.Count(t => EjeDeBarra.MatrizDeTramo(t.A, t.B) is not null);

        Comprobar(conMatriz == tramos.Count,
            $"todos los tramos tienen matriz: {conMatriz} de {tramos.Count}");

        // El perimetro no se fue: es lo que el usuario compara con su tabla de acero.
        var pO = EjeDeBarra.Largo(eje);
        var pS = EjeDeBarra.Largo(simple);

        Comprobar(Math.Abs(pO - pS) / pO < 0.005,
            $"el perimetro se conserva dentro del 0,5%: {pS:0.###} vs {pO:0.###}");

        // Y la figura sigue dentro de su caja: no se salio por ningun lado al enderezar.
        Comprobar(simple.All(p => p.X >= -Tol && p.X <= 30 + Tol
                                              && p.Y >= -Tol && p.Y <= 50 + Tol),
            "y el estribo simplificado sigue dentro de los 30x50");
    }

    /// <summary>Lo que no puede tirar una excepción ni devolver un disparate.</summary>
    private static void Degenerados()
    {
        Console.WriteLine("\nCasos degenerados");

        Comprobar(EjeDeBarra.Simplificado(null, 0.02).Count == 0, "Simplificado de null es vacio");
        Comprobar(EjeDeBarra.Tramos(null, 1).Count == 0, "Tramos de null es vacio");

        var uno = new List<(double X, double Y, double Z)> { P(1, 1, 1) };

        Comprobar(EjeDeBarra.Simplificado(uno, 0.02).Count == 1, "un solo punto se devuelve tal cual");
        Comprobar(EjeDeBarra.Tramos(uno, 1).Count == 0, "y no da ningun tramo");

        var dos = new List<(double X, double Y, double Z)> { P(0, 0, 0), P(1, 0, 0) };

        Comprobar(EjeDeBarra.Simplificado(dos, 0.02).Count == 2, "dos puntos se devuelven tal cual");

        // Todos los puntos iguales: ni tramos ni matriz, y sin excepcion.
        var pegados = new List<(double X, double Y, double Z)>
        {
            P(2, 2, 2), P(2, 2, 2), P(2, 2, 2)
        };

        Comprobar(EjeDeBarra.Tramos(pegados, 1).Count == 0,
            "un recorrido de puntos pegados no da tramos");

        Comprobar(EjeDeBarra.MatrizDeTramo(P(2, 2, 2), P(2, 2, 2)) is null,
            "y un tramo sin largo no da matriz");

        // Un pico de 180 grados -la barra vuelve sobre si misma- no rompe nada.
        var pico = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(10, 0, 0), P(0, 0, 0)
        };

        var picoS = EjeDeBarra.Simplificado(pico, 0.02);

        Comprobar(picoS.Count == 3, $"un pico de 180° conserva su vertice: {picoS.Count}");

        Comprobar(EjeDeBarra.Tramos(pico, 0.5).Count == 2, "y da sus dos tramos");

        // Un alargue absurdo no puede dar un tramo del reves.
        var t = EjeDeBarra.Tramos(
            new List<(double X, double Y, double Z)> { P(0, 0, 0), P(1, 0, 0), P(1, 1, 0) },
            100);

        Comprobar(t.Count == 2, "con un alargue absurdo siguen saliendo los dos tramos");

        Comprobar(t.All(x => Dist(x.A, x.B) > 0),
            "y ninguno queda con largo cero");

        // Y con puntos repetidos en medio, Tramos los salta en lugar de dar un tramo nulo.
        var conRepe = new List<(double X, double Y, double Z)>
        {
            P(0, 0, 0), P(5, 0, 0), P(5, 0, 0), P(5, 5, 0)
        };

        Comprobar(EjeDeBarra.Tramos(conRepe, 0).Count == 2,
            "los puntos repetidos no dan tramos de largo cero");
    }
}
