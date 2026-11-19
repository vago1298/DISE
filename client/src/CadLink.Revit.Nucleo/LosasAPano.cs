namespace CadLink.Revit.Nucleo;

/// <summary>El espesor con que se va a modelar un muro: el de su tipo de Revit elegido.</summary>
public delegate double? EspesorModelado(PanoJson muro);

/// <summary>
/// Las losas <b>a paño</b>: el borde de la losa en la cara EXTERIOR de los muros y trabes de
/// orilla, no en su eje.
/// </summary>
/// <remarks>
/// <para>
/// ETABS modela losas, muros y trabes por su eje, asi que la losa llega al centro del muro de
/// fachada y medio muro queda asomado por fuera. En el plano la losa llega al paño, igual que
/// los ejes de orilla, que ya se corren medio espesor hacia fuera (<see cref="Cuadricula"/>).
/// </para>
/// <para>
/// Va DESPUES de <see cref="UnirLosas"/>: los tableros que se tocan ya son una sola losa, asi que
/// no queda ningun lado en el eje de una trabe interior. Los huecos de la losa (una escalera
/// rodeada de tableros) tambien se llevan al paño de lo que los rodea.
/// </para>
/// <para>
/// <b>Por tramos, como el plano de AutoCAD</b> (<c>PanoDeLosa.AlPano</c>): cada lado se parte donde
/// empieza y acaba el muro o la trabe que lleva debajo, y solo se corre el trozo que la tiene. Un
/// lado con muro en una parte y volado en el resto escalona: va al paño donde hay muro y se queda
/// donde dice el modelo en el volado.
/// </para>
/// <para>
/// Un lado que comparte con otra losa que no se unio (otro tipo, otra cota) se queda en el eje:
/// las dos se siguen tocando ahi. Las losas inclinadas se dejan como estan.
/// </para>
/// </remarks>
public static class LosasAPano
{
    /// <summary>Lo que puede separarse un muro o una trabe del lado de la losa y seguir siendo «su» orilla.</summary>
    public const double ToleranciaEjeM = 0.05;

    /// <summary>Lo que se puede alejar en altura la pieza de orilla de la losa.</summary>
    public const double ToleranciaZM = 0.30;

    /// <summary>Lo que puede quedar la punta de un muro por debajo de la losa: el peralte de su cadena.</summary>
    public const double HolguraMuroM = 1.0;

    /// <summary>Un tramo mas corto que esto no se parte: seria un escalon que no se ve. 2 cm.</summary>
    public const double TramoMinimoM = 0.02;

    /// <summary>
    /// Una pieza de orilla en planta: su eje y medio espesor, y entre que cotas esta. Un muro solo
    /// cuenta si esta DEBAJO de la losa: el muro del piso de arriba que arranca en ella no es su orilla.
    /// </summary>
    public sealed record Orilla(
        double X1, double Y1, double X2, double Y2, double MedioM, double ZMin, double ZMax, bool EsMuro = false);

    /// <summary>Corre a paño todas las losas planas del modelo, en su sitio. Devuelve el aviso.</summary>
    public static List<string> AplicarATodos(
        ModeloJson modelo, MedidasModeladas? medidas = null, EspesorModelado? espesor = null)
    {
        var orillas = Orillas(modelo, medidas, espesor);
        var losas = modelo.Panos.Where(p => p.Clase == ClasePieza.Losa).ToList();
        var movidas = 0;

        // Todas se calculan con los contornos ORIGINALES: si no, la primera ya corrida dejaria
        // de compartir lado con su vecina y la vecina se correria tambien.
        var nuevos = losas
            .Select(l => (Losa: l, Contorno: Ajustar(l, losas, orillas), Huecos: AjustarHuecos(l, losas, orillas)))
            .ToList();

        foreach (var (l, c, h) in nuevos)
        {
            if (c is null && h is null)
            {
                continue;
            }

            if (c is not null)
            {
                l.Vertices.Clear();
                l.Vertices.AddRange(c);
            }

            if (h is not null)
            {
                l.Huecos = h;
            }

            movidas++;
        }

        return movidas == 0
            ? new List<string>()
            : new List<string> { $"{movidas} losa(s) se dibujaron a paño de sus muros y trabes de orilla." };
    }

    /// <summary>Los muros y trabes que pueden ser orilla de una losa.</summary>
    public static List<Orilla> Orillas(ModeloJson modelo, MedidasModeladas? medidas = null, EspesorModelado? espesor = null)
    {
        var res = new List<Orilla>();

        foreach (var b in modelo.Barras.Where(b => b.Clase == ClasePieza.Trabe))
        {
            var ancho = medidas?.Invoke(b)?.AnchoM ?? b.Seccion.AnchoM;

            if (ancho > 0)
            {
                res.Add(new Orilla(b.P1.X, b.P1.Y, b.P2.X, b.P2.Y, ancho / 2,
                    Math.Min(b.P1.Z, b.P2.Z), Math.Max(b.P1.Z, b.P2.Z)));
            }
        }

        foreach (var p in modelo.Panos.Where(p => p.Clase == ClasePieza.Muro))
        {
            var e = espesor?.Invoke(p) ?? p.Seccion.EspesorM;

            if (e > 0 && AjusteDeMuros.ComoRecto(p.Vertices) is { } m)
            {
                // El muro de abajo ya se bajo a la cara inferior de su cadena (AjusteDeMuros):
                // su punta queda un peralte por debajo de la losa. Se le da un metro de holgura.
                res.Add(new Orilla(m.X1, m.Y1, m.X2, m.Y2, e / 2, m.ZBase, m.ZAlta + HolguraMuroM, EsMuro: true));
            }
        }

        return res;
    }

    /// <summary>
    /// El contorno a paño de una losa, o <c>null</c> si no se mueve: inclinada, sin orillas, o
    /// sin un contorno que se pueda correr.
    /// </summary>
    public static List<PuntoJson>? Ajustar(PanoJson losa, IReadOnlyList<PanoJson> todas, IReadOnlyList<Orilla> orillas)
    {
        var v = Contornos.SinRepetidos(losa.Vertices, 0.001);

        if (v.Count < 3 || v.Max(p => p.Z) - v.Min(p => p.Z) > Losas.ToleranciaPlanaM)
        {
            return null;
        }

        return AjustarLazo(v, false, v.Average(p => p.Z), losa, todas, orillas);
    }

    /// <summary>Los huecos de la losa a paño de lo que los rodea, o <c>null</c> si ninguno se mueve.</summary>
    public static List<List<PuntoJson>>? AjustarHuecos(PanoJson losa, IReadOnlyList<PanoJson> todas, IReadOnlyList<Orilla> orillas)
    {
        if (losa.Huecos is not { Count: > 0 } huecos || losa.Vertices.Count < 3
            || losa.Vertices.Max(p => p.Z) - losa.Vertices.Min(p => p.Z) > Losas.ToleranciaPlanaM)
        {
            return null;
        }

        var z = losa.Vertices.Average(p => p.Z);
        var algo = false;
        var res = new List<List<PuntoJson>>();

        foreach (var h in huecos)
        {
            var v = Contornos.SinRepetidos(h, 0.001);
            var nuevo = v.Count >= 3 ? AjustarLazo(v, true, z, losa, todas, orillas) : null;

            algo |= nuevo is not null;
            res.Add(nuevo ?? h);
        }

        return algo ? res : null;
    }

    /// <summary>
    /// Corre un contorno a paño. En el de fuera la losa crece hacia fuera; en un hueco, hacia dentro
    /// del hueco, que es lo mismo visto desde la losa: su orilla se va a la cara de la trabe.
    /// </summary>
    private static List<PuntoJson>? AjustarLazo(
        List<PuntoJson> original, bool esHueco, double z,
        PanoJson losa, IReadOnlyList<PanoJson> todas, IReadOnlyList<Orilla> orillas)
    {
        var deEstaLosa = orillas
            .Where(o => o.ZMin - ToleranciaZM <= z && z <= o.ZMax + ToleranciaZM)
            .Where(o => !o.EsMuro || o.ZMin < z - ToleranciaZM)
            .ToList();

        // Cada lado partido donde empieza o acaba una pieza que corre sobre el.
        var v = PorTramos(original, deEstaLosa, z);

        // Hacia fuera del lazo: con el contorno en sentido antihorario, la normal exterior de un
        // lado (dx, dy) es (dy, -dx). En un hueco se le da la vuelta.
        var area = 0.0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        var signo = (area >= 0 ? 1.0 : -1.0) * (esHueco ? -1.0 : 1.0);
        var n = v.Count;
        var corrimiento = new double[n];
        var normales = new (double X, double Y)[n];

        for (var i = 0; i < n; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % n];
            var largo = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

            if (largo < 1e-6)
            {
                return null;
            }

            var (ux, uy) = ((b.X - a.X) / largo, (b.Y - a.Y) / largo);
            normales[i] = (uy * signo, -ux * signo);

            if (Compartido(losa, a, b, z, todas))
            {
                continue;
            }

            corrimiento[i] = deEstaLosa
                .Where(o => Cubre(o, a, b))
                .Select(o => o.MedioM)
                .DefaultIfEmpty(0)
                .Max();
        }

        if (corrimiento.All(d => d < 1e-6))
        {
            return null;
        }

        // Cada vertice nuevo es el cruce de sus dos lados ya corridos. Si los dos lados siguen
        // la misma recta -donde empieza un muro a media orilla-, el cruce no existe: se corre el
        // punto, y si los dos corrimientos son distintos queda un escalon.
        var res = new List<PuntoJson>();

        for (var i = 0; i < n; i++)
        {
            var prev = (i - 1 + n) % n;
            var p = v[i];
            var (n1x, n1y) = normales[prev];
            var (n2x, n2y) = normales[i];
            var d1 = corrimiento[prev];
            var d2 = corrimiento[i];

            var cruz = (n1x * n2y) - (n1y * n2x);

            if (Math.Abs(cruz) < 1e-6)
            {
                Agregar(res, p.X + (n1x * d1), p.Y + (n1y * d1), z);
                Agregar(res, p.X + (n2x * d2), p.Y + (n2y * d2), z);
                continue;
            }

            // El punto X que cumple n1·(X - p) = d1 y n2·(X - p) = d2.
            var dx = ((d1 * n2y) - (d2 * n1y)) / cruz;
            var dy = ((n1x * d2) - (n2x * d1)) / cruz;
            Agregar(res, p.X + dx, p.Y + dy, z);
        }

        if (res.Count > 1 && Math.Abs(res[0].X - res[^1].X) < 1e-6 && Math.Abs(res[0].Y - res[^1].Y) < 1e-6)
        {
            res.RemoveAt(res.Count - 1);
        }

        // Los vertices que quedaron en medio de un lado recto -dos tramos seguidos con el mismo
        // corrimiento- sobran.
        res = Contornos.SinColineales(res, 0.0005);

        return res.Count >= 3 ? res : null;
    }

    /// <summary>
    /// El contorno con un vertice nuevo en cada lado donde empieza o acaba una pieza que corre
    /// sobre el: asi cada tramo tiene debajo una sola cosa, o nada.
    /// </summary>
    private static List<PuntoJson> PorTramos(List<PuntoJson> v, IReadOnlyList<Orilla> orillas, double z)
    {
        var res = new List<PuntoJson>();

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];
            res.Add(a);

            var largo = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

            if (largo < 1e-6)
            {
                continue;
            }

            var (ux, uy) = ((b.X - a.X) / largo, (b.Y - a.Y) / largo);
            var cortes = new List<double>();

            foreach (var o in orillas)
            {
                if (!SobreLaRecta(o, a, ux, uy))
                {
                    continue;
                }

                foreach (var s in new[] { S(o.X1, o.Y1), S(o.X2, o.Y2) })
                {
                    if (s > TramoMinimoM && s < largo - TramoMinimoM
                        && !cortes.Any(c => Math.Abs(c - s) < TramoMinimoM))
                    {
                        cortes.Add(s);
                    }
                }
            }

            foreach (var s in cortes.OrderBy(c => c))
            {
                res.Add(new PuntoJson { X = a.X + (ux * s), Y = a.Y + (uy * s), Z = z });
            }

            double S(double x, double y) => ((x - a.X) * ux) + ((y - a.Y) * uy);
        }

        return res;
    }

    private static void Agregar(List<PuntoJson> res, double x, double y, double z)
    {
        if (res.Count > 0 && Math.Abs(res[^1].X - x) < 1e-6 && Math.Abs(res[^1].Y - y) < 1e-6)
        {
            return;
        }

        res.Add(new PuntoJson { X = x, Y = y, Z = z });
    }

    /// <summary>Si la pieza es paralela al lado y su eje va por la misma recta.</summary>
    private static bool SobreLaRecta(Orilla o, PuntoJson a, double ux, double uy)
    {
        var lo = Math.Sqrt(Math.Pow(o.X2 - o.X1, 2) + Math.Pow(o.Y2 - o.Y1, 2));

        if (lo < 1e-6)
        {
            return false;
        }

        var (ox, oy) = ((o.X2 - o.X1) / lo, (o.Y2 - o.Y1) / lo);

        // Paralelos, con 2 grados de holgura.
        if (Math.Abs((ux * oy) - (uy * ox)) > 0.035)
        {
            return false;
        }

        double Fuera(double x, double y) => Math.Abs(((x - a.X) * -uy) + ((y - a.Y) * ux));

        return Fuera(o.X1, o.Y1) <= ToleranciaEjeM && Fuera(o.X2, o.Y2) <= ToleranciaEjeM;
    }

    /// <summary>
    /// Si la pieza va por debajo de este tramo: sobre su recta y cubriendo su punto medio. Ya
    /// partido por tramos, un tramo esta cubierto entero o no lo esta.
    /// </summary>
    private static bool Cubre(Orilla o, PuntoJson a, PuntoJson b)
    {
        var largo = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        if (largo < 1e-6)
        {
            return false;
        }

        var (ux, uy) = ((b.X - a.X) / largo, (b.Y - a.Y) / largo);

        if (!SobreLaRecta(o, a, ux, uy))
        {
            return false;
        }

        double S(double x, double y) => ((x - a.X) * ux) + ((y - a.Y) * uy);
        var (s1, s2) = (S(o.X1, o.Y1), S(o.X2, o.Y2));
        var medio = largo / 2;

        return Math.Min(s1, s2) - 1e-6 <= medio && medio <= Math.Max(s1, s2) + 1e-6;
    }

    /// <summary>Si la pieza corre a lo largo del lado: paralela, en su eje y solapada mas de 5 cm.</summary>
    private static bool SobreElLado(Orilla o, PuntoJson a, PuntoJson b)
    {
        var largo = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        if (largo < 1e-6)
        {
            return false;
        }

        var (ux, uy) = ((b.X - a.X) / largo, (b.Y - a.Y) / largo);

        if (!SobreLaRecta(o, a, ux, uy))
        {
            return false;
        }

        double S(double x, double y) => ((x - a.X) * ux) + ((y - a.Y) * uy);
        var (s1, s2) = (S(o.X1, o.Y1), S(o.X2, o.Y2));

        return Math.Min(largo, Math.Max(s1, s2)) - Math.Max(0, Math.Min(s1, s2)) > 0.05;
    }

    /// <summary>Si otra losa del mismo nivel tiene un lado encima de este: entonces es interior.</summary>
    private static bool Compartido(PanoJson losa, PuntoJson a, PuntoJson b, double z, IReadOnlyList<PanoJson> todas)
    {
        foreach (var otra in todas)
        {
            if (ReferenceEquals(otra, losa) || otra.Vertices.Count < 3
                || Math.Abs(otra.Vertices.Average(p => p.Z) - z) > ToleranciaZM)
            {
                continue;
            }

            for (var i = 0; i < otra.Vertices.Count; i++)
            {
                var c = otra.Vertices[i];
                var d = otra.Vertices[(i + 1) % otra.Vertices.Count];

                if (SobreElLado(new Orilla(c.X, c.Y, d.X, d.Y, 0, z, z), a, b))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
