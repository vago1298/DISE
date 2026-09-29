namespace CadLink.Ifc;

/// <summary>Vectores en 3D, lo minimo para colocar las piezas. Todo en metros.</summary>
internal static class Geometria
{
    /// <summary>Debajo de esto se considera que un vector es cero o dos direcciones iguales.</summary>
    public const double Cero = 1e-9;

    public static double[] Restar(double[] a, double[] b) =>
        new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

    public static double[] Sumar(double[] a, double[] b) =>
        new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };

    public static double[] Escalar(double[] a, double k) =>
        new[] { a[0] * k, a[1] * k, a[2] * k };

    public static double Punto(double[] a, double[] b) =>
        (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    public static double[] Cruz(double[] a, double[] b) =>
        new[]
        {
            (a[1] * b[2]) - (a[2] * b[1]),
            (a[2] * b[0]) - (a[0] * b[2]),
            (a[0] * b[1]) - (a[1] * b[0])
        };

    public static double Largo(double[] a) => Math.Sqrt(Punto(a, a));

    /// <summary>El mismo vector con largo 1.</summary>
    public static double[] Normalizar(double[] a)
    {
        var l = Largo(a);

        if (l < Cero)
        {
            throw new ArgumentException("No se puede normalizar un vector de largo cero.", nameof(a));
        }

        return Escalar(a, 1.0 / l);
    }

    public static double[] Negar(double[] a) => new[] { -a[0], -a[1], -a[2] };

    /// <summary>Si los tres vectores forman una terna derecha y ortonormal.</summary>
    /// <remarks>
    /// Se comprueba antes de exportar porque una terna izquierda produce un IFC que ABRE
    /// sin ningun error y tiene las piezas asimetricas -angulos, canales, tes- volteadas.
    /// Es de los fallos que no se ven hasta que alguien mide en obra.
    /// </remarks>
    public static bool EsTernaDerecha(double[] e1, double[] e2, double[] e3, double tol = 1e-6)
    {
        if (Math.Abs(Largo(e1) - 1) > tol
            || Math.Abs(Largo(e2) - 1) > tol
            || Math.Abs(Largo(e3) - 1) > tol)
        {
            return false;
        }

        if (Math.Abs(Punto(e1, e2)) > tol
            || Math.Abs(Punto(e1, e3)) > tol
            || Math.Abs(Punto(e2, e3)) > tol)
        {
            return false;
        }

        var esperado = Cruz(e1, e2);

        return Math.Abs(esperado[0] - e3[0]) <= tol
               && Math.Abs(esperado[1] - e3[1]) <= tol
               && Math.Abs(esperado[2] - e3[2]) <= tol;
    }

    /// <summary>
    /// La normal de un poligono por el metodo de Newell, y su area.
    /// </summary>
    /// <remarks>
    /// Newell y no el producto cruz de dos lados: con tres vertices casi alineados -cosa
    /// normal en el contorno de un muro mallado- el producto cruz de dos lados da un vector
    /// diminuto y una direccion que es puro ruido. Newell suma la aportacion de TODOS los
    /// lados, asi que un vertice malo no manda.
    /// </remarks>
    public static (double[] Normal, double AreaM2) NormalDePoligono(
        IReadOnlyList<(double X, double Y, double Z)> v)
    {
        if (v.Count < 3)
        {
            return (new double[] { 0, 0, 1 }, 0);
        }

        double nx = 0, ny = 0, nz = 0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }

        var n = new[] { nx, ny, nz };
        var l = Largo(n);

        if (l < Cero)
        {
            // Degenerado: todos los vertices en una linea, o el poligono se cierra sobre
            // si mismo. Se devuelve una normal cualquiera y area cero, y quien llama decide.
            return (new double[] { 0, 0, 1 }, 0);
        }

        return (Escalar(n, 1.0 / l), l / 2.0);
    }

    /// <summary>
    /// Dos direcciones perpendiculares a <paramref name="normal"/>, para armar el plano.
    /// </summary>
    /// <remarks>
    /// La primera se elige tan horizontal como se pueda: asi un muro sale con su contorno
    /// derecho -una direccion a lo largo y la otra hacia arriba- en vez de girado un angulo
    /// arbitrario, y el archivo se lee igual que el modelo.
    /// </remarks>
    public static (double[] Ex, double[] Ey) PlanoDe(double[] normal)
    {
        var n = Normalizar(normal);
        var z = new double[] { 0, 0, 1 };

        // Si la normal es casi vertical -una losa- no hay "horizontal a lo largo" que valga:
        // se toma el eje X global y queda la losa con su contorno en planta, como se dibuja.
        var ex = Math.Abs(Punto(n, z)) > 1 - 1e-6
            ? new double[] { 1, 0, 0 }
            : Normalizar(Cruz(z, n));

        var ey = Normalizar(Cruz(n, ex));

        return (ex, ey);
    }

    /// <summary>Pasa el poligono a las coordenadas 2D de su propio plano.</summary>
    public static List<(double U, double V)> Aplanar(
        IReadOnlyList<(double X, double Y, double Z)> v,
        double[] origen, double[] ex, double[] ey)
    {
        var salida = new List<(double U, double V)>(v.Count);

        foreach (var p in v)
        {
            var d = Restar(new[] { p.X, p.Y, p.Z }, origen);
            salida.Add((Punto(d, ex), Punto(d, ey)));
        }

        return salida;
    }

    /// <summary>Area con signo de un poligono ya aplanado. Positiva si va antihorario.</summary>
    public static double AreaConSigno(IReadOnlyList<(double U, double V)> p)
    {
        double s = 0;

        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += (a.U * b.V) - (b.U * a.V);
        }

        return s / 2.0;
    }

    /// <summary>Quita vertices repetidos seguidos, y el ultimo si repite al primero.</summary>
    /// <remarks>
    /// El contorno que llega de CSI suele traer el primer vertice repetido al final para
    /// cerrar. En un <c>IfcPolyline</c> de perfil eso es un error: el perfil se cierra solo,
    /// y un punto doble deja un lado de largo cero que algunos visores rechazan.
    /// </remarks>
    public static List<(double X, double Y, double Z)> SinRepetidos(
        IReadOnlyList<(double X, double Y, double Z)> v, double tol = 1e-7)
    {
        var salida = new List<(double X, double Y, double Z)>(v.Count);

        foreach (var p in v)
        {
            if (salida.Count > 0 && Cerca(salida[^1], p, tol))
            {
                continue;
            }

            salida.Add(p);
        }

        while (salida.Count > 1 && Cerca(salida[0], salida[^1], tol))
        {
            salida.RemoveAt(salida.Count - 1);
        }

        return salida;
    }

    private static bool Cerca(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b, double tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol && Math.Abs(a.Z - b.Z) <= tol;
}
