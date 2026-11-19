using System.Globalization;

namespace CadLink.Cad;

/// <summary>Las formas de la hoja «Formas especiales».</summary>
public static class FormaEspecial
{
    public const string L = "L";
    public const string T = "T";
    public const string Irregular = "IRREGULAR";

    public static readonly string[] Todas = { L, T, Irregular };
}

/// <summary>
/// Un castillo, columna, cabezal o dado que NO es rectangular ni circular: en L, en T o con el
/// contorno que se quiera. Todo en centimetros, como <see cref="SeccionCad"/>.
/// </summary>
public sealed class SeccionEspecialCad
{
    public string Id { get; init; } = string.Empty;

    /// <summary>CASTILLO, COLUMNA, CABEZAL o DADO: va en el rotulo.</summary>
    public string Elemento { get; init; } = "COLUMNA";

    /// <summary>El contorno del concreto, ya en sentido antihorario y con su esquina en (0, 0).</summary>
    public IReadOnlyList<(double X, double Y)> Contorno { get; init; } = Array.Empty<(double, double)>();

    /// <summary>Como se escribio la forma, para el rotulo: «L 60x60, alas 20 y 20».</summary>
    public string Descripcion { get; init; } = string.Empty;

    public double RecubrimientoCm { get; init; }

    public VarCad Estribo { get; init; } = new(string.Empty, 0);

    public string SeparacionEstribo { get; init; } = string.Empty;

    public VarCad Esquina { get; init; } = new(string.Empty, 0);

    public VarCad Intermedia { get; init; } = new(string.Empty, 0);

    /// <summary>Lo mas que puede haber entre dos varillas a lo largo de una cara.</summary>
    public double SepMaxCm { get; init; } = 15;

    public string Fc { get; init; } = string.Empty;

    public string Escala { get; init; } = string.Empty;

    public ModoSeccion Modo { get; init; } = ModoSeccion.Tipo2Rellena;
}

/// <summary>Una varilla de la seccion especial: su centro, su diametro y si es de esquina.</summary>
public readonly record struct VarillaEspecial(double X, double Y, double DiamCm, string Clave, bool Esquina);

/// <summary>
/// La geometria de las secciones especiales, sin nada de AutoCAD: el contorno de cada forma, el
/// estribo que lo sigue por dentro y donde cae cada varilla.
/// </summary>
/// <remarks>
/// <para>
/// <b>El estribo es el contorno desplazado hacia dentro</b> el recubrimiento: cada cara se corre
/// paralela y las esquinas salen de cortar las caras vecinas. Asi sirve igual para una L que para
/// cualquier poligono, y en la esquina de DENTRO de la L -la concava- el estribo dobla hacia fuera
/// como debe. En obra una L se suele armar con dos estribos rectangulares encimados; aqui se
/// dibuja el contorno que cubren los dos.
/// </para>
/// <para>
/// <b>Las varillas van en cada esquina del estribo</b> -tambien en las concavas, que es donde una L
/// o una T necesita la suya- y entre esquinas las que hagan falta para no pasar de la separacion
/// maxima a lo largo de cada cara.
/// </para>
/// </remarks>
public static class GeometriaEspecial
{
    /// <summary>
    /// El contorno de la forma, en sentido antihorario, con la esquina de abajo a la izquierda de su
    /// caja en (0, 0). Vacio si las medidas no forman la figura.
    /// </summary>
    /// <param name="b">Ancho total.</param>
    /// <param name="h">Alto total.</param>
    /// <param name="t1">L: espesor del ala de ABAJO. T: espesor del patin (arriba).</param>
    /// <param name="t2">L: espesor del ala de la IZQUIERDA. T: espesor del alma.</param>
    /// <param name="vertices">Solo IRREGULAR: «x,y; x,y; …» en cm.</param>
    public static List<(double X, double Y)> Contorno(string forma, double b, double h, double t1, double t2, string? vertices)
    {
        var f = (forma ?? string.Empty).Trim().ToUpperInvariant();

        List<(double X, double Y)> p;

        if (f == FormaEspecial.L)
        {
            if (b <= 0 || h <= 0 || t1 <= 0 || t2 <= 0 || t1 >= h || t2 >= b)
            {
                return new();
            }

            p = new() { (0, 0), (b, 0), (b, t1), (t2, t1), (t2, h), (0, h) };
        }
        else if (f == FormaEspecial.T)
        {
            if (b <= 0 || h <= 0 || t1 <= 0 || t2 <= 0 || t1 >= h || t2 >= b)
            {
                return new();
            }

            var a = (b - t2) / 2;
            p = new() { (a, 0), (a + t2, 0), (a + t2, h - t1), (b, h - t1), (b, h), (0, h), (0, h - t1), (a, h - t1) };
        }
        else
        {
            p = LeerVertices(vertices);
        }

        return Normalizar(p);
    }

    /// <summary>«0,0; 60,0; 60,20» a puntos. Acepta punto y coma o salto de linea entre vertices.</summary>
    public static List<(double X, double Y)> LeerVertices(string? texto)
    {
        var res = new List<(double, double)>();

        foreach (var par in (texto ?? string.Empty).Split(new[] { ';', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = par.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (xy.Length >= 2
                && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                res.Add((x, y));
            }
        }

        return res;
    }

    /// <summary>Sin repetidos ni colineales, antihorario y con su caja en (0, 0). Vacio si no es un poligono.</summary>
    public static List<(double X, double Y)> Normalizar(List<(double X, double Y)> p)
    {
        var q = new List<(double X, double Y)>();

        foreach (var v in p)
        {
            if (q.Count == 0 || Math.Abs(q[^1].X - v.X) > 1e-6 || Math.Abs(q[^1].Y - v.Y) > 1e-6)
            {
                q.Add(v);
            }
        }

        if (q.Count > 1 && Math.Abs(q[0].X - q[^1].X) < 1e-6 && Math.Abs(q[0].Y - q[^1].Y) < 1e-6)
        {
            q.RemoveAt(q.Count - 1);
        }

        // Fuera los vertices que estan en medio de una recta: no son esquinas.
        for (var i = 0; i < q.Count && q.Count > 3;)
        {
            var a = q[(i - 1 + q.Count) % q.Count];
            var c = q[(i + 1) % q.Count];
            var cruz = ((q[i].X - a.X) * (c.Y - q[i].Y)) - ((q[i].Y - a.Y) * (c.X - q[i].X));

            if (Math.Abs(cruz) < 1e-9)
            {
                q.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        if (q.Count < 3 || Math.Abs(Area(q)) < 1e-6 || SeCruza(q))
        {
            return new();
        }

        if (Area(q) < 0)
        {
            q.Reverse();
        }

        var x0 = q.Min(v => v.X);
        var y0 = q.Min(v => v.Y);

        return q.Select(v => (v.X - x0, v.Y - y0)).ToList();
    }

    /// <summary>Area con signo: positiva si va en sentido antihorario.</summary>
    public static double Area(IReadOnlyList<(double X, double Y)> p)
    {
        var a = 0.0;

        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Count];
            a += (x1 * y2) - (x2 * y1);
        }

        return a / 2;
    }

    /// <summary>
    /// El contorno corrido <paramref name="d"/> hacia DENTRO. Null si a esa distancia la figura ya
    /// no cabe: alguna cara se invierte o desaparece.
    /// </summary>
    public static List<(double X, double Y)>? HaciaDentro(IReadOnlyList<(double X, double Y)> p, double d)
    {
        var n = p.Count;

        if (n < 3)
        {
            return null;
        }

        // Cada cara, corrida hacia su izquierda -dentro, en un poligono antihorario-.
        var lineas = new List<(double Px, double Py, double Dx, double Dy)>();

        for (var i = 0; i < n; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % n];
            var dx = x2 - x1;
            var dy = y2 - y1;
            var l = Math.Sqrt((dx * dx) + (dy * dy));

            if (l < 1e-9)
            {
                return null;
            }

            lineas.Add((x1 - (dy / l * d), y1 + (dx / l * d), dx / l, dy / l));
        }

        var res = new List<(double X, double Y)>();

        for (var i = 0; i < n; i++)
        {
            var a = lineas[(i - 1 + n) % n];
            var b = lineas[i];
            var den = (a.Dx * b.Dy) - (a.Dy * b.Dx);

            if (Math.Abs(den) < 1e-12)
            {
                return null;
            }

            var t = (((b.Px - a.Px) * b.Dy) - ((b.Py - a.Py) * b.Dx)) / den;
            res.Add((a.Px + (a.Dx * t), a.Py + (a.Dy * t)));
        }

        // Cada cara sigue en su mismo sentido y el area sigue positiva: si no, se dio la vuelta.
        for (var i = 0; i < n; i++)
        {
            var (x1, y1) = res[i];
            var (x2, y2) = res[(i + 1) % n];

            if (((x2 - x1) * lineas[i].Dx) + ((y2 - y1) * lineas[i].Dy) <= 1e-9)
            {
                return null;
            }
        }

        return Area(res) > 1e-9 && !SeCruza(res) ? res : null;
    }

    /// <summary>
    /// Las varillas: una en cada esquina del camino de las varillas y, a lo largo de cada cara, las
    /// intermedias que hagan falta para no pasar de <see cref="SeccionEspecialCad.SepMaxCm"/>.
    /// </summary>
    /// <returns>Vacio si no caben.</returns>
    public static List<VarillaEspecial> Varillas(SeccionEspecialCad s)
    {
        var dEsq = s.Esquina.Cm;
        var dInt = s.Intermedia.Existe ? s.Intermedia.Cm : dEsq;
        var camino = HaciaDentro(s.Contorno, s.RecubrimientoCm + s.Estribo.Cm + (dEsq / 2));
        var res = new List<VarillaEspecial>();

        if (camino is null || dEsq <= 0)
        {
            return res;
        }

        var n = camino.Count;

        for (var i = 0; i < n; i++)
        {
            var (x1, y1) = camino[i];
            var (x2, y2) = camino[(i + 1) % n];

            res.Add(new VarillaEspecial(x1, y1, dEsq, s.Esquina.Clave, true));

            var l = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            var tramos = s.SepMaxCm > 0 ? (int)Math.Ceiling((l / s.SepMaxCm) - 1e-9) : 1;

            for (var k = 1; k < tramos; k++)
            {
                var t = (double)k / tramos;
                res.Add(new VarillaEspecial(
                    x1 + ((x2 - x1) * t), y1 + ((y2 - y1) * t), dInt,
                    s.Intermedia.Existe ? s.Intermedia.Clave : s.Esquina.Clave, false));
            }
        }

        return res;
    }

    /// <summary>El menor hueco libre entre dos varillas, en cm. Negativo si se enciman.</summary>
    public static double HuecoMinimo(IReadOnlyList<VarillaEspecial> v)
    {
        var min = double.MaxValue;

        for (var i = 0; i < v.Count; i++)
        {
            for (var j = i + 1; j < v.Count; j++)
            {
                var d = Math.Sqrt(Math.Pow(v[i].X - v[j].X, 2) + Math.Pow(v[i].Y - v[j].Y, 2))
                        - ((v[i].DiamCm + v[j].DiamCm) / 2);
                min = Math.Min(min, d);
            }
        }

        return min == double.MaxValue ? 0 : min;
    }

    /// <summary>¿Dos caras que no son vecinas se cortan? Entonces el poligono se cruza consigo mismo.</summary>
    private static bool SeCruza(IReadOnlyList<(double X, double Y)> p)
    {
        var n = p.Count;

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1))
                {
                    continue;
                }

                if (Cortan(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Cortan((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double O((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) =>
            ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));

        var o1 = O(a, b, c);
        var o2 = O(a, b, d);
        var o3 = O(c, d, a);
        var o4 = O(c, d, b);

        return ((o1 > 1e-9 && o2 < -1e-9) || (o1 < -1e-9 && o2 > 1e-9))
               && ((o3 > 1e-9 && o4 < -1e-9) || (o3 < -1e-9 && o4 > 1e-9));
    }
}
