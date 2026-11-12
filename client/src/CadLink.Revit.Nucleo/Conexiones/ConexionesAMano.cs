namespace CadLink.Revit.Nucleo;

// ============================================================================
//  CONEXIONES CAPTURADAS A MANO, SIN EL LIBRO DE EXCEL
//
//  Se capturan los mismos datos que en la hoja del libro -los que la conexion usa para su
//  dibujo- y lo que el libro calcula con sus formulas (las medidas del perfil, el angulo de la
//  diagonal, el despatinado efectivo…) se completa aqui con el catalogo de perfiles de CadLink.
//  Despues es una hoja mas: se arma con las mismas piezas que la del libro.
// ============================================================================

/// <summary>Las medidas de un perfil del catalogo, en cm (cm³ el Zx).</summary>
/// <param name="D">Peralte (OR: alto).</param>
/// <param name="B">Ancho del patin (OR: ancho).</param>
/// <param name="Tf">Grueso del patin (OR: la pared).</param>
/// <param name="Tw">Grueso del alma (OR: la pared).</param>
public sealed record PerfilDeCatalogo(double D, double B, double Tf, double Tw, double Zx, bool EsTubo);

public static class ConexionesAMano
{
    private static readonly Dictionary<string, List<DatoDelLibro>> Usados = new(StringComparer.Ordinal);

    /// <summary>Los tipos que se pueden capturar a mano: los mismos que se arman del libro.</summary>
    public static IReadOnlyList<string> Codigos { get; } = new[] { "UV", "WF", "RB", "EP", "SC", "MW", "GP", "CE", "CX", "BP" };

    /// <summary>
    /// Los datos que hay que capturar para ese tipo, en el orden de su hoja: los capturables que
    /// la conexion usa para armarse (se averigua armandola una vez con los valores de fabrica).
    /// </summary>
    public static IReadOnlyList<DatoDelLibro> Datos(string codigo)
    {
        lock (Usados)
        {
            if (Usados.TryGetValue(codigo, out var ya))
            {
                return ya;
            }
        }

        var tipo = EtiquetasExcel.DeCodigo(codigo) ?? throw new ArgumentException($"No hay hoja «{codigo}».");
        var h = Hoja(codigo, tipo.Hoja, Defectos(codigo), _ => new PerfilDeCatalogo(40, 20, 1.5, 1, 1500, false));
        ConexionesDelLibro.Construir(h, out _);

        var lista = tipo.Datos
            .Where(d => d.EsCaptura && (h.Pedidos.Contains(d.Nombre) || d.Nombre == "ID"))
            .GroupBy(d => d.Nombre).Select(g => g.First())
            .ToList();

        lock (Usados)
        {
            Usados[codigo] = lista;
        }

        return lista;
    }

    /// <summary>Los valores de fabrica de la hoja, como texto.</summary>
    public static Dictionary<string, string> Defectos(string codigo)
    {
        var tipo = EtiquetasExcel.DeCodigo(codigo) ?? throw new ArgumentException($"No hay hoja «{codigo}».");
        var d = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dato in tipo.Datos.Where(x => x.EsCaptura))
        {
            d.TryAdd(dato.Nombre, dato.Defecto ?? string.Empty);
        }

        return d;
    }

    /// <summary>
    /// La hoja con lo capturado y con lo que el libro calcularia: las medidas de los perfiles, el
    /// angulo de las diagonales, los atiesadores del eslabon…
    /// </summary>
    public static HojaLeida Hoja(string codigo, string nombre, IReadOnlyDictionary<string, string> valores,
        Func<string, PerfilDeCatalogo?> perfil)
    {
        var tipo = EtiquetasExcel.DeCodigo(codigo) ?? throw new ArgumentException($"No hay hoja «{codigo}».");
        var h = HojaLeida.DeValores(tipo, nombre, valores);
        Completar(h, perfil);
        return h;
    }

    private static void Completar(HojaLeida h, Func<string, PerfilDeCatalogo?> buscar)
    {
        PerfilDeCatalogo P(string dato) => buscar(h.Txt(dato)) ?? new PerfilDeCatalogo(0, 0, 0, 0, 0, false);

        // La distancia k no viene en el catalogo: patin mas alma es lo que mide en los IR.
        static double K(PerfilDeCatalogo p) => p.Tf + p.Tw;

        // La pared de diseño de un OR es 0.93 de la nominal (tdist del IMCA).
        static double Pared(PerfilDeCatalogo p) => p.EsTubo ? 0.93 * p.Tw : p.Tw;

        void Viga(string dato, string d, string bf, string tf, string tw, string? k = null, string? h0 = null)
        {
            var p = P(dato);
            h.SiFalta(d, p.D);
            h.SiFalta(bf, p.B);
            h.SiFalta(tf, p.Tf);
            h.SiFalta(tw, p.Tw);

            if (k is not null)
            {
                h.SiFalta(k, K(p));
            }

            if (h0 is not null)
            {
                h.SiFalta(h0, p.D - (2 * p.Tf));
            }
        }

        switch (h.Codigo)
        {
            case "UV":
            case "WF":
            case "RB":
            case "EP":
                Viga("PerfV", "db", "bfb", "tfb", "twb", h0: "hb");
                Viga("PerfC", "dc", "bfc", "tfc", "twc", k: "kc", h0: "hc");
                h.SiFalta("Atr", h.Opcion("Tcp") == "ATORNILLADA" ? "SI" : "NO");
                break;

            case "SC":
                Viga("PerfS", "ds", "bfs", "tfs", "tws", k: "ks");
                Viga("PerfP", "dp", "bfp", "tfp", "twp", k: "kp");
                h.SiFalta("dce", h.Si("Desp") ? h.Num("dc") : 0);
                h.SiFalta("ytop", h.Opcion("Union") == "SOLDADA" ? h.Num("Ltp") : h.Num("Ltop") - h.Num("Lev"));
                break;

            case "MW":
            {
                Viga("Perf", "d", "bf", "tf", "tw");
                Viga("PerfT", "dt", "bft", "tft", "twt", k: "kt");
                var emp = h.Opcion("Tipo") == "EMPALME";
                h.SiFalta("Pen", emp && h.Opcion("Upat") == "PENETRACION" ? "SI" : "NO");
                h.SiFalta("PenW", emp && h.Opcion("Ualm") == "PENETRACION" ? "SI" : "NO");
                h.SiFalta("Lp", emp ? (2 * h.Num("Lw")) + h.Num("gj") : (2 * h.Num("Lw")) + h.Num("bft") + (2 * h.Num("gj")));
                h.SiFalta("ccop", (h.Num("bft") - h.Num("twt")) / 2);
                h.SiFalta("hwx", Math.Max(0, Math.Min(h.Num("dt") - (2 * h.Num("kt")), h.Num("d") - (2 * h.Num("tf")))));
                break;
            }

            case "GP":
            {
                var v = P("PerfV");
                var c = P("PerfC");
                h.SiFalta("dvb", v.D);
                h.SiFalta("eb", v.D / 2);
                h.SiFalta("dcc", c.D);
                h.SiFalta("tfb", v.Tf);
                h.SiFalta("twb", v.Tw);
                h.SiFalta("tfc", c.Tf);
                h.SiFalta("twc", c.Tw);
                h.SiFalta("ec", c.D / 2);
                h.SiFalta("beta", h.Num("Lgc") / 2);
                h.SiFalta("alfar", h.Num("Lgb") / 2);
                break;
            }

            case "CE":
            {
                var v = P("PerfV");
                var b = P("PerfB");
                h.SiFalta("d", v.D);
                h.SiFalta("bf", v.B);
                h.SiFalta("tf", v.Tf);
                h.SiFalta("tw", v.Tw);
                h.SiFalta("kdv", K(v));
                h.SiFalta("Bb", b.B);
                h.SiFalta("Hb", b.D);
                h.SiFalta("tb", Pared(b));
                h.SiFalta("bcn", b.B);
                h.SiFalta("tmw", Pared(b));

                var e = h.Num("e");
                var xh = (h.Num("Lb") - e) / 2;
                h.SiFalta("alfa", xh > 0 ? Math.Atan(h.Num("Hs") / xh) * 180 / Math.PI : 45);

                // El tipo de eslabon: e·Vp/Mp, con Vp = 0.6·Fy·(d − 2tf)·tw y Mp = Fy·Zx.
                var e0 = v.Zx > 0 ? e * 0.6 * (v.D - (2 * v.Tf)) * v.Tw / v.Zx : 1;
                var tip = e0 <= 1.6 ? "CORTO" : e0 >= 2.6 ? "LARGO" : "INTERMEDIO";
                h.SiFalta("Tip", tip);
                h.SiFalta("Req", e0 <= 5 ? "SI" : "NO");
                h.SiFalta("Lado", v.D < 64 ? "UN LADO" : "DOS LADOS");

                if (tip == "CORTO")
                {
                    var s = h.Num("si");
                    h.SiFalta("nint", s > 0 ? Math.Max(0, Math.Ceiling(e / s) - 1) : 0);
                }
                else
                {
                    h.SiFalta("nint", 2);
                    h.SiFalta("Pos15", "SI");
                }

                break;
            }

            case "CX":
            {
                var p = P("Perf");
                h.SiFalta("Din", p.B);
                h.SiFalta("tw_", Pared(p));
                var xd = h.Opcion("Conf") == "1 PISO" ? h.Num("Lb") : h.Num("Lb") / 2;
                h.SiFalta("alfa", xd > 0 ? Math.Atan(h.Num("Hs") / xd) * 180 / Math.PI : 45);
                break;
            }

            case "BP":
            {
                var c = P("PerfC");
                h.SiFalta("dc", c.D);
                h.SiFalta("bfc", c.B);
                var d = h.Num("do");
                h.SiFalta("Hol", d + (d < 2.54 ? 0.8 : 1.3));
                break;
            }
        }

    }
}
