namespace CadLink.Revit.Nucleo;

/// <summary>
/// Ayudas para armar las piezas de una conexion en sus ejes locales. Todo en metros.
/// </summary>
internal sealed class PiezasLocales
{
    public static readonly V3 X = new(1, 0, 0);
    public static readonly V3 Y = new(0, 1, 0);
    public static readonly V3 Z = new(0, 0, 1);

    public PiezasLocales(ModeloConexion m) => M = m;

    public ModeloConexion M { get; }

    /// <summary>Una caja de <paramref name="min"/> a <paramref name="max"/>; el espesor va a lo largo de <paramref name="eje"/>.</summary>
    public PlacaPieza Caja(string que, string rotulo, V3 min, V3 max, char eje, bool soloDibujo = false)
    {
        // El espesor tiene que salir HACIA el eje elegido: U × V = eje.
        //   eje X: U = Y, V = Z  ·  eje Y: U = Z, V = X  ·  eje Z: U = X, V = Y
        PlacaPieza p = eje switch
        {
            'X' => new PlacaPieza(que, rotulo, new V3(min.X, 0, 0), Y, Z,
                Rect(min.Y, max.Y, min.Z, max.Z), max.X - min.X, soloDibujo),
            'Y' => new PlacaPieza(que, rotulo, new V3(0, min.Y, 0), Z, X,
                Rect(min.Z, max.Z, min.X, max.X), max.Y - min.Y, soloDibujo),
            _ => new PlacaPieza(que, rotulo, new V3(0, 0, min.Z), X, Y,
                Rect(min.X, max.X, min.Y, max.Y), max.Z - min.Z, soloDibujo)
        };

        if (p.Espesor > 1e-6 && p.Contorno.Count >= 3)
        {
            M.Placas.Add(p);
        }

        return p;
    }

    /// <summary>Una placa con contorno libre en el plano (U, V) por <paramref name="origen"/>.</summary>
    public PlacaPieza Placa(string que, string rotulo, V3 origen, V3 u, V3 v, IReadOnlyList<(double U, double V)> contorno, double espesor)
    {
        var p = new PlacaPieza(que, rotulo, origen, u, v, contorno, espesor);

        if (espesor > 1e-6 && contorno.Count >= 3)
        {
            M.Placas.Add(p);
        }

        return p;
    }

    private static List<(double, double)> Rect(double u0, double u1, double v0, double v1) =>
        new() { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };

    public void Cordon(string que, string rotulo, V3 p0, V3 p1, V3 pata1, V3 pata2, double lado, bool penetracion = false)
    {
        if (lado <= 1e-6 || (p1 - p0).Largo < 1e-6)
        {
            return;
        }

        M.Cordones.Add(new CordonPieza(que, rotulo, p0, p1, pata1.Unitario(), pata2.Unitario(), lado, penetracion)
        {
            Simbolo = SimboloDe(rotulo, penetracion),
            Tamano = penetracion ? string.Empty : TextoMedida.Pulgadas(lado * 100),
            Electrodo = System.Text.RegularExpressions.Regex.Match(rotulo, @"\((E\d+XX)\)").Groups[1].Value,
            Descripcion = Descripcion(rotulo)
        });
    }

    /// <summary>
    /// El simbolo, por lo que dice el rotulo: los rotulos los escriben los mismos ayudantes
    /// (<c>Filete(…)</c> y <c>Penetracion(…)</c> de <see cref="ConexionesDelLibro"/>), asi que su
    /// forma es fija.
    /// </summary>
    public static SimboloDeSoldadura SimboloDe(string rotulo, bool penetracion)
    {
        if (penetracion)
        {
            return SimboloDeSoldadura.BiselSimple;
        }

        var r = EtiquetasExcel.Normalizar(rotulo);

        if (r.Contains("todo alrededor", StringComparison.Ordinal))
        {
            return SimboloDeSoldadura.TodoAlrededor;
        }

        return r.Contains("dos lados", StringComparison.Ordinal) || r.Contains("ambos lados", StringComparison.Ordinal)
               || r.Contains("dos caras", StringComparison.Ordinal)
            ? SimboloDeSoldadura.FileteAmbosLados
            : SimboloDeSoldadura.Filete;
    }

    /// <summary>«Filete 3/8" (E70XX) placa a la viga, ambos lados» → «placa a la viga, ambos lados».</summary>
    public static string Descripcion(string rotulo)
    {
        var d = System.Text.RegularExpressions.Regex.Replace(rotulo,
            @"^\s*(Filete\s+[0-9 /]+""?|Penetración completa)\s*(\(E\d+XX\))?\s*,?\s*", string.Empty);
        d = System.Text.RegularExpressions.Regex.Replace(d, @"\s*,?\s*(Filete\s+[0-9 /]+""?)\s*(\(E\d+XX\))?", string.Empty);
        d = d.Trim().TrimStart(',').Trim();
        return d.Length > 0 && rotulo.StartsWith("Penetración", StringComparison.Ordinal)
            ? "penetración completa: " + d
            : d.Length > 0 ? d : rotulo;
    }

    public void Tornillo(string que, string rotulo, V3 desde, V3 eje, double largo, double diametro)
    {
        if (diametro > 1e-6 && largo > 1e-6)
        {
            M.Tornillos.Add(new TornilloPieza(que, rotulo, desde, eje.Unitario(), largo, diametro));
        }
    }

    public void Miembro(string que, string nombre, FormaMiembro forma, V3 p0, V3 p1, V3 peralte, double d, double b, double tf, double tw)
    {
        if (d > 1e-6 && b > 1e-6 && (p1 - p0).Largo > 1e-6)
        {
            M.Miembros.Add(new MiembroPieza(que, nombre, forma, p0, p1, peralte.Unitario(), d, b, tf, tw));
        }
    }

    /// <summary>Despatina el extremo P0 del ultimo miembro llamado asi.</summary>
    public void Despatinar(string que, bool arriba, double largo, double profundo)
    {
        var i = M.Miembros.FindLastIndex(x => x.Que == que);

        if (i < 0 || largo <= 1e-6 || profundo <= 1e-6)
        {
            return;
        }

        var mi = M.Miembros[i];
        M.Miembros[i] = mi with { Despatinados = mi.Despatinados.Append(new DespatinadoPieza(arriba, largo, Math.Min(profundo, mi.D / 2))).ToList() };
    }

    public void Cota(V3 a, V3 b, V3 lado, string? texto = null)
    {
        var l = (b - a).Largo;

        if (l < 1e-4)
        {
            return;
        }

        M.Cotas.Add(new CotaPieza(a, b, lado.Unitario(), texto ?? TextoMedida.Cota(l * 100)));
    }

    public void Trazo(string que, IReadOnlyList<V3> puntos, bool cerrado = false, bool oculto = false)
    {
        if (puntos.Count >= 2)
        {
            M.Trazos.Add(new TrazoPieza(que, puntos, cerrado, oculto));
        }
    }

    public void Vista(string titulo, V3 derecha, V3 arriba) => M.Vistas.Add(new VistaPieza(titulo, derecha, arriba));

    /// <summary>La envolvente convexa de puntos en un plano (u, v), en sentido antihorario.</summary>
    public static List<(double U, double V)> Envolvente(IEnumerable<(double U, double V)> puntos)
    {
        var p = puntos.Distinct().OrderBy(a => a.U).ThenBy(a => a.V).ToList();

        if (p.Count < 3)
        {
            return p;
        }

        static double Giro((double U, double V) o, (double U, double V) a, (double U, double V) b) =>
            ((a.U - o.U) * (b.V - o.V)) - ((a.V - o.V) * (b.U - o.U));

        var h = new List<(double, double)>();

        foreach (var pt in p)
        {
            while (h.Count >= 2 && Giro(h[^2], h[^1], pt) <= 1e-12)
            {
                h.RemoveAt(h.Count - 1);
            }

            h.Add(pt);
        }

        var inferior = h.Count + 1;

        for (var i = p.Count - 2; i >= 0; i--)
        {
            var pt = p[i];

            while (h.Count >= inferior && Giro(h[^2], h[^1], pt) <= 1e-12)
            {
                h.RemoveAt(h.Count - 1);
            }

            h.Add(pt);
        }

        h.RemoveAt(h.Count - 1);
        return h;
    }
}
