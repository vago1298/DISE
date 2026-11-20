namespace CadLink.Revit.Nucleo;

/// <summary>Una malla electrosoldada: su nombre comercial, el diametro del alambre y la separacion.</summary>
public sealed record TipoDeMalla(string Nombre, double AlambreM, double SeparacionM)
{
    /// <summary>Las de catalogo en Mexico: 6x6 (15.24 cm) calibres 10, 8, 6 y 4.</summary>
    public static readonly TipoDeMalla[] Catalogo =
    {
        new("6x6-10/10", 0.00343, 0.1524),
        new("6x6-8/8", 0.00411, 0.1524),
        new("6x6-6/6", 0.00488, 0.1524),
        new("6x6-4/4", 0.00572, 0.1524),
        new("4x4-10/10", 0.00343, 0.1016)
    };
}

/// <summary>Como se ve el aplanado sobre la malla en el detalle.</summary>
public enum AplanadoDeMalla
{
    /// <summary>Roto en diagonal, como en un detalle: se ve el tabique, la malla y el aplanado.</summary>
    Roto,
    Completo,
    Ninguno
}

/// <summary>
/// La malla electrosoldada sobre la cara de un muro y el aplanado que la cubre, en el plano del muro:
/// S a lo largo del muro y Z hacia arriba, en metros desde su esquina de abajo.
/// </summary>
public static class MallaDeMuro
{
    /// <summary>Lo que se queda la malla antes de las orillas del muro.</summary>
    public const double LibreM = 0.05;

    /// <summary>Donde va cada alambre: los verticales en S y los horizontales en Z.</summary>
    public static (List<double> Verticales, List<double> Horizontales) Alambres(double largoM, double altoM, TipoDeMalla t)
    {
        static List<double> Reparto(double total, double sep)
        {
            var res = new List<double>();
            var util = total - (2 * LibreM);

            if (util <= 0 || sep <= 0)
            {
                return res;
            }

            // Centrada: lo que sobra se reparte a los dos lados.
            var n = (int)Math.Floor((util / sep) + 1e-9);
            var inicio = LibreM + ((util - (n * sep)) / 2);

            for (var i = 0; i <= n; i++)
            {
                res.Add(inicio + (i * sep));
            }

            return res;
        }

        return (Reparto(largoM, t.SeparacionM), Reparto(altoM, t.SeparacionM));
    }

    /// <summary>
    /// El contorno del aplanado roto: cubre la parte de abajo a la derecha y deja ver arriba a la
    /// izquierda la malla y el tabique, con una orilla quebrada en diagonal como en los detalles.
    /// </summary>
    public static List<(double S, double Z)> AplanadoRoto(double largoM, double altoM)
    {
        var (l, h) = (largoM, altoM);

        return new List<(double, double)>
        {
            (0.06 * l, 0), (l, 0), (l, 0.88 * h), (0.66 * l, 0.76 * h),
            (0.50 * l, 0.56 * h), (0.38 * l, 0.32 * h), (0.18 * l, 0.16 * h)
        };
    }

    /// <summary>
    /// Los tramos de un alambre que caen DENTRO de la cara del muro: la malla sigue su forma (muro con
    /// remate inclinado, perfil editado, puertas y ventanas). <paramref name="lazos"/> son los contornos
    /// de la cara en (S, Z) -el de fuera y los de los huecos-; se cuenta par/impar, asi que el orden y el
    /// sentido no importan. Cada tramo se recorta <paramref name="libre"/> en sus dos puntas.
    /// </summary>
    /// <param name="vertical">true: el alambre va en Z por la S <paramref name="pos"/>; false: en S por la Z.</param>
    public static List<(double Desde, double Hasta)> DentroDeLaCara(
        IReadOnlyList<IReadOnlyList<(double S, double Z)>> lazos, double pos, bool vertical, double libre)
    {
        var cortes = new List<double>();

        foreach (var lazo in lazos)
        {
            for (var i = 0; i < lazo.Count; i++)
            {
                var a = lazo[i];
                var b = lazo[(i + 1) % lazo.Count];
                var (u1, v1, u2, v2) = vertical ? (a.S, a.Z, b.S, b.Z) : (a.Z, a.S, b.Z, b.S);

                // Medio abierto, para no contar dos veces un vertice justo en la linea.
                if ((u1 <= pos) == (u2 <= pos))
                {
                    continue;
                }

                cortes.Add(v1 + ((pos - u1) / (u2 - u1) * (v2 - v1)));
            }
        }

        cortes.Sort();
        var res = new List<(double, double)>();

        for (var i = 0; i + 1 < cortes.Count; i += 2)
        {
            var (d, h) = (cortes[i] + libre, cortes[i + 1] - libre);

            if (h - d > 0.02)
            {
                res.Add((d, h));
            }
        }

        return res;
    }

    /// <summary>El aplanado completo: todo el muro.</summary>
    public static List<(double S, double Z)> AplanadoCompleto(double largoM, double altoM) =>
        new() { (0, 0), (largoM, 0), (largoM, altoM), (0, altoM) };
}
