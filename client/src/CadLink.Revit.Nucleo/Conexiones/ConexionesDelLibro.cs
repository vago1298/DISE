using static CadLink.Revit.Nucleo.PiezasLocales;
using static CadLink.Revit.Nucleo.TextoMedida;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  DE LAS HOJAS DEL LIBRO DE ACERO A SUS PIEZAS
//
//  Cada tipo de hoja sabe de que piezas se hace su conexion. Las medidas salen TAL CUAL de la
//  hoja (en cm, aqui a metros); lo que la hoja no dice -cuanto sobresale la placa de la diagonal,
//  hasta donde llega la placa de refuerzo- se resuelve con una regla escrita en su lugar y se
//  avisa, para que el dibujante lo revise.
// ============================================================================

/// <summary>Una hoja del libro y lo que CadLink puede hacer con ella.</summary>
public sealed class ConexionDelLibro
{
    public ConexionDelLibro(HojaLeida hoja, ModeloConexion? modelo, string tipo, string? porQueNo)
    {
        Hoja = hoja;
        Modelo = modelo;
        Tipo = tipo;
        PorQueNo = porQueNo;
    }

    public HojaLeida Hoja { get; }

    /// <summary>Las piezas; null si esta hoja todavia no se dibuja.</summary>
    public ModeloConexion? Modelo { get; }

    /// <summary>«Unión viga-columna», «Placa gusset»…</summary>
    public string Tipo { get; }

    /// <summary>Por que no hay modelo, si no lo hay.</summary>
    public string? PorQueNo { get; }

    public bool SeDibuja => Modelo is not null;
}

/// <summary>Arma las conexiones de todas las hojas del libro.</summary>
public static class ConexionesDelLibro
{
    /// <summary>Las hojas del libro que son de conexiones, con el nombre de cada tipo.</summary>
    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        ["UV"] = "Unión viga-columna con placas en los patines",
        ["RB"] = "Conexión RBS (viga de sección reducida)",
        ["EP"] = "Conexión con placa de extremo",
        ["WF"] = "Conexión con patines soldados (WUF-W)",
        ["DF"] = "Conexión con diafragmas (columna OR)",
        ["SC"] = "Conexión a cortante viga-viga",
        ["MW"] = "Conexión de momento soldada viga-viga",
        ["GP"] = "Placa gusset de esquina",
        ["CE"] = "Contraventeo excéntrico: eslabón y placas",
        ["CX"] = "Contraventeo en X: placa del cruce",
        ["CW"] = "Contraventeo en V o K",
        ["CV"] = "Contraventeo: conexión de la diagonal",
        ["AT"] = "Atiesadores",
        ["BP"] = "Placa base"
    };

    /// <summary>Abre el libro y arma lo que se pueda de cada hoja de conexiones.</summary>
    public static List<ConexionDelLibro> Leer(LibroExcel libro)
    {
        var lista = new List<ConexionDelLibro>();

        foreach (var h in LectorDelLibro.Leer(libro))
        {
            if (!Tipos.TryGetValue(h.Codigo, out var tipo))
            {
                continue;
            }

            ModeloConexion? m = null;
            string? porQue = null;

            try
            {
                m = Construir(h, out porQue);
            }
            catch (Exception e)
            {
                porQue = "No se pudo armar: " + e.Message;
            }

            if (m is not null)
            {
                Avisos(h, m, libro);
            }

            lista.Add(new ConexionDelLibro(h, m, tipo, porQue));
        }

        return lista;
    }

    private static void Avisos(HojaLeida h, ModeloConexion m, LibroExcel libro)
    {
        if (h.NoCumplen > 0)
        {
            m.Avisos.Add($"La hoja tiene {h.NoCumplen} revision(es) que NO CUMPLEN. Corrigela antes de mandar el detalle a obra.");
        }

        if (h.DeEjemplo)
        {
            m.Avisos.Add("La hoja trae los datos de ejemplo del libro: todavia no se lleno con los de tu proyecto.");
        }

        if (libro.RecalcularAlAbrir)
        {
            m.Avisos.Add("El libro se modifico fuera de Excel y pide recalcular: abrelo en Excel, guardalo y vuelve a leerlo para que los valores calculados esten al dia.");
        }
    }

    /// <summary>Las piezas de una hoja, o null con el motivo.</summary>
    public static ModeloConexion? Construir(HojaLeida h, out string? porQueNo)
    {
        porQueNo = null;
        var m = new ModeloConexion { Hoja = h.Hoja, Codigo = h.Codigo, Id = h.Id };

        foreach (var nombre in new[] { "RID" }.Concat(Enumerable.Range(1, 30).Select(i => $"R{i:00}")))
        {
            if (h.Tiene(nombre) && h.Txt(nombre).Length > 0 && nombre != "RID")
            {
                m.Renglones.Add(h.Txt(nombre));
            }
        }

        switch (h.Codigo)
        {
            case "UV":
                UnionVigaColumna(h, m);
                break;
            case "WF":
                PatinesSoldados(h, m, rbs: false);
                break;
            case "RB":
                PatinesSoldados(h, m, rbs: true);
                break;
            case "EP":
                PlacaDeExtremo(h, m);
                break;
            case "SC":
                Cortante(h, m);
                break;
            case "MW":
                VigaVigaSoldada(h, m);
                break;
            case "GP":
                Gusset(h, m);
                break;
            case "CE":
                Excentrico(h, m);
                break;
            case "CX":
                if (h.Opcion("Conf") != "1 PISO")
                {
                    porQueNo = "La X en dos niveles (nudo en la viga) todavia no se dibuja; la X en un entrepiso si.";
                    return null;
                }

                CruceEnX(h, m);
                break;
            case "BP":
                PlacaBase(h, m);
                break;
            default:
                porQueNo = "Este tipo de hoja todavia no se dibuja desde el libro.";
                return null;
        }

        if (m.Placas.Count == 0 && m.Cordones.Count == 0 && m.PlacaBase is null)
        {
            porQueNo = "La hoja no tiene las medidas para armar la conexion.";
            return null;
        }

        return m;
    }

    // =====================================================================
    //  Comunes
    // =====================================================================

    private static double M(HojaLeida h, string nombre) => h.Num(nombre) / 100;

    private static string Acero(HojaLeida h, string nombre) => h.Txt(nombre) is { Length: > 0 } a ? " " + a : string.Empty;

    private static string Pl(double ancho, double largo, double espesor, string acero) =>
        $"PL {Cm(ancho * 100)} × {Cm(largo * 100)} cm × {Pulgadas(espesor * 100)}{acero}";

    private static string Filete(double lado, string electrodo) =>
        $"Filete {Pulgadas(lado * 100)}" + (electrodo.Length > 0 ? $" ({electrodo})" : string.Empty);

    private static string Penetracion(string electrodo) =>
        "Penetración completa" + (electrodo.Length > 0 ? $" ({electrodo})" : string.Empty);

    /// <summary>
    /// Las placas de continuidad y la placa adosada de la columna, frente a los patines de la viga.
    /// Columna IR con su patin en Z = 0 y su alma en el plano YZ.
    /// </summary>
    private static void ColumnaIR(PiezasLocales p, HojaLeida h, double ySup, double yInf, double dc, double tfc, double twc, string el)
    {
        var bpc = M(h, "bpc");
        var tpc = M(h, "tpc");
        var cpc = M(h, "cpc");
        var wpc = M(h, "wpc");
        var tdp = M(h, "tdp");
        var kc = Math.Max(M(h, "kc"), tfc);
        var ac = Acero(h, "AcPC") is { Length: > 0 } a ? a : Acero(h, "AcP");

        if (tdp > 1e-6)
        {
            p.Caja("Placa adosada", $"Placa adosada al alma {Pulgadas(tdp * 100)}{ac}",
                new V3(twc / 2, yInf - (tpc / 2), -dc + kc), new V3((twc / 2) + tdp, ySup + (tpc / 2), -kc), 'X');
        }

        if (bpc <= 1e-6 || tpc <= 1e-6)
        {
            return;
        }

        var rot = $"2 placas de continuidad por patín, {Cm(bpc * 100)} cm × {Pulgadas(tpc * 100)}{ac}";
        var z0 = -dc + tfc;
        var z1 = -tfc;

        foreach (var y in new[] { ySup, yInf })
        {
            foreach (var s in new[] { 1.0, -1.0 })
            {
                var x0 = s * ((twc / 2) + (s > 0 ? tdp : 0));
                var x1 = x0 + (s * bpc);
                var c = Math.Min(cpc, Math.Min(bpc, (z1 - z0) / 2) * 0.9);

                // Contorno en (Z, X), con las esquinas recortadas junto al alma.
                var contorno = new List<(double U, double V)>
                {
                    (z0, x0 + (s * c)), (z0, x1), (z1, x1), (z1, x0 + (s * c)), (z1 - c, x0), (z0 + c, x0)
                };

                p.Placa("Placa de continuidad", rot, new V3(0, y - (tpc / 2), 0), Z, X, contorno, tpc);

                // Filete al alma (arriba y abajo de la placa) y penetracion a los patines.
                foreach (var cara in new[] { 1.0, -1.0 })
                {
                    p.Cordon("Soldadura", Filete(wpc, el) + " al alma (placas de continuidad)",
                        new V3(x0, y + (cara * tpc / 2), z0 + c), new V3(x0, y + (cara * tpc / 2), z1 - c),
                        new V3(0, cara, 0), new V3(s, 0, 0), wpc);
                }

                p.Cordon("Soldadura", Penetracion(el) + " a los patines de la columna",
                    new V3(x0 + (s * c), y + (tpc / 2), z1), new V3(x1, y + (tpc / 2), z1), -Y, -Z, tpc, true);
                p.Cordon("Soldadura", Penetracion(el) + " a los patines de la columna",
                    new V3(x0 + (s * c), y + (tpc / 2), z0), new V3(x1, y + (tpc / 2), z0), -Y, Z, tpc, true);
            }
        }
    }

    /// <summary>La viga IR que llega por +Z y la columna IR con su patin en Z = 0.</summary>
    private static void VigaYColumna(PiezasLocales p, HojaLeida h, double hueco, double largoViga)
    {
        var db = M(h, "db");
        var dc = M(h, "dc");
        p.Miembro("Viga", h.Txt("PerfV"), FormaMiembro.I, new V3(0, 0, hueco), new V3(0, 0, hueco + largoViga), Y,
            db, M(h, "bfb"), M(h, "tfb"), M(h, "twb"));
        var alto = (db / 2) + 0.45;
        p.Miembro("Columna", h.Txt("PerfC"), FormaMiembro.I, new V3(0, -alto, -dc / 2), new V3(0, alto, -dc / 2), Z,
            dc, M(h, "bfc"), M(h, "tfc"), M(h, "twc"));
    }

    private static void VistasDeExtremo(PiezasLocales p)
    {
        p.Vista("ELEVACIÓN", Z, Y);
        p.Vista("PLANTA", Z, -X);
    }

    /// <summary>La placa de cortante del alma: soldada o atornillada, y su soldadura a la columna.</summary>
    private static void PlacaDeCortante(
        PiezasLocales p, HojaLeida h, double twb, double tsp, double hsp, double bsp, double wCol, bool soldada,
        double wAlma, double largoFileteH, double dv, int nv, double sv, double ev, string el, string acero)
    {
        if (tsp <= 1e-6 || hsp <= 1e-6 || bsp <= 1e-6)
        {
            return;
        }

        var x0 = twb / 2;
        var x1 = x0 + tsp;
        p.Caja("Placa de cortante", "Placa de cortante " + Pl(bsp, hsp, tsp, acero),
            new V3(x0, -hsp / 2, 0), new V3(x1, hsp / 2, bsp), 'X');

        p.Cordon("Soldadura", Filete(wCol, el) + " placa de cortante a la columna, dos lados",
            new V3(x0, -hsp / 2, 0), new V3(x0, hsp / 2, 0), Z, -X, wCol);
        p.Cordon("Soldadura", Filete(wCol, el) + " placa de cortante a la columna, dos lados",
            new V3(x1, -hsp / 2, 0), new V3(x1, hsp / 2, 0), Z, X, wCol);

        if (soldada)
        {
            var lh = largoFileteH > 1e-6 ? Math.Min(largoFileteH, bsp) : bsp;
            var r = Filete(wAlma, el) + " placa de cortante al alma, en C";
            p.Cordon("Soldadura", r, new V3(x0, hsp / 2, bsp - lh), new V3(x0, hsp / 2, bsp), Y, X, wAlma);
            p.Cordon("Soldadura", r, new V3(x0, -hsp / 2, bsp - lh), new V3(x0, -hsp / 2, bsp), -Y, X, wAlma);
            p.Cordon("Soldadura", r, new V3(x0, -hsp / 2, bsp), new V3(x0, hsp / 2, bsp), Z, X, wAlma);
        }
        else if (nv > 0 && dv > 1e-6)
        {
            var rot = $"{nv} tornillos Ø {Pulgadas(dv * 100)} @ {Cm(sv * 100)} cm" + (h.Txt("Tor") is { Length: > 0 } t ? " " + t : string.Empty);

            for (var i = 0; i < nv; i++)
            {
                var y = (((nv - 1) / 2.0) - i) * sv;
                p.Tornillo("Tornillo", rot, new V3(-(twb / 2) - (0.7 * dv), y, ev), X, twb + tsp + (1.4 * dv), dv);
            }
        }

        p.Cota(new V3(x1, -hsp / 2, bsp), new V3(x1, hsp / 2, bsp), Z);
        p.Cota(new V3(x1, -hsp / 2, 0), new V3(x1, -hsp / 2, bsp), -Y);
    }

    // =====================================================================
    //  UNION VIGA-COLUMNA: placas en los patines (soldadas o atornilladas)
    // =====================================================================

    private static void UnionVigaColumna(HojaLeida h, ModeloConexion m)
    {
        m.Titulo = "UNIÓN VIGA-COLUMNA CON PLACAS EN LOS PATINES";
        m.Colocacion = ColocacionConexion.ExtremoDeViga;
        m.ZCeroEnLaColumna = true;
        var p = new PiezasLocales(m);

        var db = M(h, "db");
        var bfb = M(h, "bfb");
        var tfb = M(h, "tfb");
        var twb = M(h, "twb");
        var dc = M(h, "dc");
        var tfc = M(h, "tfc");
        var twc = M(h, "twc");
        var gap = M(h, "gap");
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcP");
        var hv = db / 2;
        m.HolguraM = gap;
        m.CaraArribaM = hv;

        var tps = M(h, "tps");
        var bps = M(h, "bps");
        var lps = M(h, "Lps");
        var tpi = M(h, "tpi");
        var bpi = M(h, "bpi");
        var lpi = M(h, "Lpi");
        var atornillada = h.Opcion("Tcp") == "ATORNILLADA" || h.Si("Atr");

        VigaYColumna(p, h, gap, Math.Max(Math.Max(lps, lpi), M(h, "bsp")) + 0.5);

        p.Caja("Placa de patín", "Placa superior " + Pl(bps, lps, tps, ac), new V3(-bps / 2, hv, 0), new V3(bps / 2, hv + tps, lps), 'Y');
        p.Caja("Placa de patín", "Placa inferior " + Pl(bpi, lpi, tpi, ac), new V3(-bpi / 2, -hv - tpi, 0), new V3(bpi / 2, -hv, lpi), 'Y');

        // Penetracion completa de las placas al patin de la columna.
        p.Cordon("Soldadura", Penetracion(el) + " placas al patín de la columna",
            new V3(-bps / 2, hv + tps, 0), new V3(bps / 2, hv + tps, 0), -Y, Z, tps, true);
        p.Cordon("Soldadura", Penetracion(el) + " placas al patín de la columna",
            new V3(-bpi / 2, -hv - tpi, 0), new V3(bpi / 2, -hv - tpi, 0), Y, Z, tpi, true);

        if (atornillada)
        {
            var d = M(h, "dbt");
            var nf = (int)Math.Round(h.Num("nf"));
            var s = M(h, "s");
            var g = M(h, "g");
            var s1 = M(h, "S1");
            var rot = $"{2 * nf} tornillos Ø {Pulgadas(d * 100)} por placa, a {Cm(s * 100)} cm" + (h.Txt("Tor") is { Length: > 0 } t ? " " + t : string.Empty);

            for (var i = 0; i < nf; i++)
            {
                var z = s1 + (i * s);

                foreach (var x in new[] { -g / 2, g / 2 })
                {
                    p.Tornillo("Tornillo", rot, new V3(x, hv - tfb - (0.7 * d), z), Y, tfb + tps + (1.4 * d), d);
                    p.Tornillo("Tornillo", rot, new V3(x, -hv + tfb + (0.7 * d), z), -Y, tfb + tpi + (1.4 * d), d);
                }
            }

            p.Cota(new V3(g / 2, hv + tps, 0), new V3(g / 2, hv + tps, s1), Y);
        }
        else
        {
            var w = M(h, "wfp");
            var lw = Math.Min(M(h, "Lw"), Math.Max(lps, lpi));
            var rot = Filete(w, el) + $" placa-patín, {Cm(lw * 100)} cm por lado";
            FiletesDePlaca(p, rot, hv, bfb, bps, lps, lw, w, arriba: true);
            FiletesDePlaca(p, rot, -hv, bfb, bpi, lpi, lw, w, arriba: false);
            p.Cota(new V3(bps / 2, hv, lps - lw), new V3(bps / 2, hv, lps), X);
        }

        PlacaDeCortante(p, h, twb, M(h, "tsp"), M(h, "hsp"), M(h, "bsp"), M(h, "wsp"), h.Opcion("Tsp") == "SOLDADA",
            M(h, "wsw"), M(h, "Lhw"), M(h, "dv"), (int)Math.Round(h.Num("nv")), M(h, "sv"), M(h, "ev"), el, ac);

        ColumnaIR(p, h, hv + (tps / 2), -hv - (tpi / 2), dc, tfc, twc, el);

        p.Cota(new V3(0, hv + tps, 0), new V3(0, hv + tps, lps), Y);
        p.Cota(new V3(0, -hv - tpi, 0), new V3(0, -hv - tpi, lpi), -Y);
        p.Cota(new V3(-bps / 2, hv + tps, lps), new V3(bps / 2, hv + tps, lps), Z);
        VistasDeExtremo(p);
    }

    /// <summary>
    /// Los filetes longitudinales de una placa de patin: en los bordes de la placa si es mas angosta
    /// que el patin, en los bordes del patin si es mas ancha.
    /// </summary>
    private static void FiletesDePlaca(PiezasLocales p, string rotulo, double yPatin, double bf, double bp, double lp, double lw, double w, bool arriba)
    {
        var hacia = arriba ? Y : -Y;
        var angosta = bp + (2 * w) <= bf + 1e-6;
        var z0 = lp - lw;

        foreach (var s in new[] { -1.0, 1.0 })
        {
            if (angosta)
            {
                // Sobre el patin, en el canto de la placa.
                p.Cordon("Soldadura", rotulo, new V3(s * bp / 2, yPatin, z0), new V3(s * bp / 2, yPatin, lp), hacia, new V3(s, 0, 0), w);
            }
            else
            {
                // Bajo la placa, en el canto del patin.
                p.Cordon("Soldadura", rotulo, new V3(s * bf / 2, yPatin, z0), new V3(s * bf / 2, yPatin, lp), -hacia, new V3(s, 0, 0), w);
            }
        }
    }

    // =====================================================================
    //  PATINES SOLDADOS (WUF-W) Y RBS
    // =====================================================================

    private static void PatinesSoldados(HojaLeida h, ModeloConexion m, bool rbs)
    {
        m.Titulo = rbs ? "CONEXIÓN RBS (VIGA DE SECCIÓN REDUCIDA)" : "CONEXIÓN CON PATINES SOLDADOS (WUF-W)";
        m.Colocacion = ColocacionConexion.ExtremoDeViga;
        m.ZCeroEnLaColumna = true;
        var p = new PiezasLocales(m);

        var db = M(h, "db");
        var bfb = M(h, "bfb");
        var tfb = M(h, "tfb");
        var twb = M(h, "twb");
        var hv = db / 2;
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcSP");
        m.CaraArribaM = hv;

        var a = M(h, "a");
        var b = M(h, "b");
        var c = M(h, "c");
        VigaYColumna(p, h, 0, Math.Max(0.6, rbs ? a + b + 0.4 : 0.6));

        foreach (var (y, nombre) in new[] { (hv, "superior"), (-hv + tfb, "inferior") })
        {
            p.Cordon("Soldadura", Penetracion(el) + " patines de la viga a la columna",
                new V3(-bfb / 2, y, 0), new V3(bfb / 2, y, 0), -Y, Z, tfb, true);
        }

        if (rbs || h.Opcion("Web") == "PENETRACION")
        {
            p.Cordon("Soldadura", Penetracion(el) + " alma de la viga a la columna",
                new V3(twb / 2, -hv + tfb, 0), new V3(twb / 2, hv - tfb, 0), -X, Z, twb, true);
        }

        var tsp = M(h, "tsp");
        var hsp = M(h, "hsp");
        var bsp = M(h, "bsp");

        if (bsp <= 1e-6)
        {
            // La RBS no dice el ancho de la placa: la linea de tornillos mas su distancia al borde.
            bsp = M(h, "ev") + Math.Max(M(h, "Lev"), 0.04);
            m.Avisos.Add("La hoja no da el ancho de la placa de cortante; se dibujo hasta la línea de tornillos más su distancia al borde.");
        }

        var soldada = !rbs && h.Opcion("Tws") != "ATORNILLADA";
        PlacaDeCortante(p, h, twb, tsp, hsp, bsp, M(h, "wsp"), soldada, M(h, "w2"), 0,
            M(h, "dv"), (int)Math.Round(h.Num("nv")), M(h, "sv"), M(h, "ev"), el, ac);

        ColumnaIR(p, h, hv - (tfb / 2), -hv + (tfb / 2), M(h, "dc"), M(h, "tfc"), M(h, "twc"), el);

        if (rbs && b > 1e-6 && c > 1e-6)
        {
            // El recorte: un arco de radio R = (4c² + b²) / 8c en cada borde del patin.
            var r = ((4 * c * c) + (b * b)) / (8 * c);
            var zc = a + (b / 2);

            foreach (var s in new[] { -1.0, 1.0 })
            {
                var puntos = new List<V3>();

                for (var i = 0; i <= 16; i++)
                {
                    var z = a + (b * i / 16.0);
                    var dz = z - zc;
                    var flecha = c - (r - Math.Sqrt(Math.Max(0, (r * r) - (dz * dz))));
                    puntos.Add(new V3(s * ((bfb / 2) - flecha), hv, z));
                }

                p.Trazo("Recorte RBS", puntos);
            }

            p.Cota(new V3(bfb / 2, hv, 0), new V3(bfb / 2, hv, a), X);
            p.Cota(new V3(bfb / 2, hv, a), new V3(bfb / 2, hv, a + b), X);
            p.Cota(new V3(bfb / 2, hv, zc), new V3((bfb / 2) - c, hv, zc), Z);
            m.Avisos.Add("El recorte RBS se dibuja en AutoCAD; en Revit hay que recortar la viga (no se modela como pieza aparte).");
        }

        VistasDeExtremo(p);
    }

    // =====================================================================
    //  PLACA DE EXTREMO (4E, 4ES, 8ES)
    // =====================================================================

    private static void PlacaDeExtremo(HojaLeida h, ModeloConexion m)
    {
        var cfg = h.Opcion("Cfg");
        m.Titulo = $"CONEXIÓN CON PLACA DE EXTREMO {cfg}";
        m.Colocacion = ColocacionConexion.ExtremoDeViga;
        m.ZCeroEnLaColumna = true;
        var p = new PiezasLocales(m);

        var db = M(h, "db");
        var bfb = M(h, "bfb");
        var tfb = M(h, "tfb");
        var twb = M(h, "twb");
        var hv = db / 2;
        var tp = M(h, "tp");
        var bp = M(h, "bp");
        var g = M(h, "g");
        var pfo = M(h, "pfo");
        var pfi = M(h, "pfi");
        var pb = M(h, "pb");
        var de = M(h, "de");
        var d = M(h, "dbt");
        var ts = M(h, "ts");
        var ww = M(h, "ww");
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcP");
        var ocho = cfg == "8ES";
        var vuelo = ocho ? pfo + pb + de : pfo + de;
        m.CaraArribaM = hv;
        m.HolguraM = tp;

        VigaYColumna(p, h, tp, 0.6);
        p.Caja("Placa de extremo", "Placa de extremo " + Pl(bp, db + (2 * vuelo), tp, ac),
            new V3(-bp / 2, -hv - vuelo, 0), new V3(bp / 2, hv + vuelo, tp), 'Z');

        p.Cordon("Soldadura", Penetracion(el) + " patines a la placa de extremo",
            new V3(-bfb / 2, hv, tp), new V3(bfb / 2, hv, tp), -Y, Z, tfb, true);
        p.Cordon("Soldadura", Penetracion(el) + " patines a la placa de extremo",
            new V3(-bfb / 2, -hv + tfb, tp), new V3(bfb / 2, -hv + tfb, tp), -Y, Z, tfb, true);

        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Cordon("Soldadura", Filete(ww, el) + " alma a la placa, dos lados",
                new V3(s * twb / 2, -hv + tfb, tp), new V3(s * twb / 2, hv - tfb, tp), Z, new V3(s, 0, 0), ww);
        }

        var filas = new List<double> { hv + pfo, hv - tfb - pfi, -hv - pfo, -hv + tfb + pfi };

        if (ocho)
        {
            filas.AddRange(new[] { hv + pfo + pb, hv - tfb - pfi - pb, -hv - pfo - pb, -hv + tfb + pfi + pb });
        }

        var tfc = M(h, "tfc");
        var rot = $"{2 * filas.Count} tornillos Ø {Pulgadas(d * 100)} pretensados, gramil {Cm(g * 100)} cm" + (h.Txt("Tor") is { Length: > 0 } t ? " " + t : string.Empty);

        foreach (var y in filas)
        {
            foreach (var x in new[] { -g / 2, g / 2 })
            {
                p.Tornillo("Tornillo", rot, new V3(x, y, tp + (0.7 * d)), -Z, tp + tfc + (1.4 * d), d);
            }
        }

        if (cfg != "4E" && ts > 1e-6)
        {
            var hst = Math.Max(M(h, "hst"), vuelo);
            var lst = M(h, "Lst") > 1e-6 ? M(h, "Lst") : hst / Math.Tan(Math.PI / 6);

            foreach (var lado in new[] { 1.0, -1.0 })
            {
                var y0 = lado * hv;
                var contorno = new List<(double U, double V)>
                {
                    (y0, tp), (y0 + (lado * hst), tp), (y0 + (lado * hst), tp + 0.025), (y0, tp + lst)
                };
                p.Placa("Atiesador de la placa", $"Atiesador {Pulgadas(ts * 100)}, {Cm(lst * 100)} × {Cm(hst * 100)} cm{ac}",
                    new V3(-ts / 2, 0, 0), Y, Z, contorno, ts);
            }
        }

        ColumnaIR(p, h, hv - (tfb / 2), -hv + (tfb / 2), M(h, "dc"), tfc, M(h, "twc"), el);
        p.Cota(new V3(bp / 2, hv, tp), new V3(bp / 2, hv + vuelo, tp), Z);
        p.Cota(new V3(-g / 2, hv + pfo, tp), new V3(g / 2, hv + pfo, tp), Y);
        p.Vista("ELEVACIÓN", Z, Y);
        p.Vista("VISTA DE LA PLACA", -X, Y);
    }

    // =====================================================================
    //  CONEXION A CORTANTE: viga secundaria al alma de la que soporta
    // =====================================================================

    private static void Cortante(HojaLeida h, ModeloConexion m)
    {
        m.Titulo = "CONEXIÓN A CORTANTE " + h.Txt("Nivel");
        m.Colocacion = ColocacionConexion.ExtremoDeViga;
        m.ZCeroEnLaColumna = false;
        var p = new PiezasLocales(m);

        var ds = M(h, "ds");
        var tws = M(h, "tws");
        var tfs = M(h, "tfs");
        var dp = M(h, "dp");
        var twp = M(h, "twp");
        var bfp = M(h, "bfp");
        var g0 = M(h, "g0");
        var dce = M(h, "dce");
        var cc = M(h, "cc");
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcL");
        var hv = ds / 2;
        m.CaraArribaM = hv - dce;

        // La viga soportada llega por +Z; la que soporta corre a lo largo de X con su alma a g0.
        p.Miembro("Viga soportada", h.Txt("PerfS"), FormaMiembro.I, new V3(0, 0, 0), new V3(0, 0, 0.7), Y,
            ds, M(h, "bfs"), tfs, tws);
        var yp = hv - (dp / 2);
        p.Miembro("Viga que soporta", h.Txt("PerfP"), FormaMiembro.I, new V3(-0.45, yp, -g0 - (twp / 2)),
            new V3(0.45, yp, -g0 - (twp / 2)), Y, dp, bfp, M(h, "tfp") > 0 ? M(h, "tfp") : tfs, twp);

        if (dce > 1e-6 && cc > 1e-6)
        {
            p.Trazo("Despatinado", new[] { new V3(0, hv, 0), new V3(0, hv - dce, 0), new V3(0, hv - dce, cc), new V3(0, hv, cc) });
            p.Cota(new V3(0, hv, cc), new V3(0, hv - dce, cc), Z);
            p.Cota(new V3(0, hv, 0), new V3(0, hv, cc), Y);
        }

        var tpl = M(h, "tpl");
        var hpl = M(h, "hpl");
        var bpl = M(h, "bpl");
        var arribaDelAlma = hv - (dce > 1e-6 ? dce : tfs);
        var yTop = arribaDelAlma - M(h, "ytop");
        var x0 = tws / 2;
        var x1 = x0 + tpl;
        var z0 = -g0;
        var z1 = -g0 + bpl;
        p.Caja("Placa de cortante", "Placa " + Pl(bpl, hpl, tpl, ac), new V3(x0, yTop - hpl, z0), new V3(x1, yTop, z1), 'X');

        var wsc = M(h, "wsc");
        p.Cordon("Soldadura", Filete(wsc, el) + " placa al alma de la viga que soporta, dos lados",
            new V3(x0, yTop - hpl, z0), new V3(x0, yTop, z0), Z, -X, wsc);
        p.Cordon("Soldadura", Filete(wsc, el) + " placa al alma de la viga que soporta, dos lados",
            new V3(x1, yTop - hpl, z0), new V3(x1, yTop, z0), Z, X, wsc);

        if (h.Opcion("Union") == "SOLDADA")
        {
            var w2 = M(h, "ws2");
            var lh = M(h, "Lhs");
            var r = Filete(w2, el) + " placa al alma de la viga soportada";
            p.Cordon("Soldadura", r, new V3(x0, yTop - hpl, z1), new V3(x0, yTop, z1), Z, X, w2);

            if (lh > 1e-6)
            {
                p.Cordon("Soldadura", r, new V3(x0, yTop, z1 - lh), new V3(x0, yTop, z1), Y, X, w2);
                p.Cordon("Soldadura", r, new V3(x0, yTop - hpl, z1 - lh), new V3(x0, yTop - hpl, z1), -Y, X, w2);
            }
        }
        else
        {
            var d = M(h, "dbt");
            var nv = (int)Math.Round(h.Num("nv"));
            var sv = M(h, "sv");
            var lev = M(h, "Lev");
            var za = -g0 + M(h, "aa");
            var rot = $"{nv} tornillos Ø {Pulgadas(d * 100)} @ {Cm(sv * 100)} cm" + (h.Txt("Tor") is { Length: > 0 } t ? " " + t : string.Empty);

            for (var i = 0; i < nv; i++)
            {
                p.Tornillo("Tornillo", rot, new V3(-(tws / 2) - (0.7 * d), yTop - lev - (i * sv), za), X, tws + tpl + (1.4 * d), d);
            }

            p.Cota(new V3(x1, yTop, z0), new V3(x1, yTop, za), Y);
        }

        p.Cota(new V3(x1, yTop - hpl, z0), new V3(x1, yTop - hpl, z1), -Y);
        p.Cota(new V3(x1, yTop - hpl, z1), new V3(x1, yTop, z1), Z);
        p.Vista("ELEVACIÓN", Z, Y);
        p.Vista("PLANTA", Z, -X);
    }

    // =====================================================================
    //  MOMENTO VIGA-VIGA SOLDADA: empalme, continuidad sobre trabe y cruce
    // =====================================================================

    private static void VigaVigaSoldada(HojaLeida h, ModeloConexion m)
    {
        var tipo = h.Opcion("Tipo");
        m.Colocacion = ColocacionConexion.ExtremoDeViga;
        m.ZCeroEnLaColumna = false;
        var p = new PiezasLocales(m);

        var d = M(h, "d");
        var bf = M(h, "bf");
        var tf = M(h, "tf");
        var tw = M(h, "tw");
        var hv = d / 2;
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcP");
        var dt = M(h, "dt");
        var bft = M(h, "bft");
        var tft = M(h, "tft");
        var twt = M(h, "twt");
        var gj = M(h, "gj");
        m.CaraArribaM = hv;

        if (tipo == "EMPALME")
        {
            m.Titulo = "EMPALME SOLDADO DE VIGA";

            // Z = 0 en el extremo del tramo elegido; la junta queda a gj/2 hacia fuera.
            var zj = -gj / 2;
            p.Miembro("Viga", h.Txt("Perf"), FormaMiembro.I, new V3(0, 0, 0), new V3(0, 0, 0.7), Y, d, bf, tf, tw);
            p.Miembro("Viga", h.Txt("Perf"), FormaMiembro.I, new V3(0, 0, -gj), new V3(0, 0, -gj - 0.7), Y, d, bf, tf, tw);

            if (h.Si("Pen"))
            {
                foreach (var y in new[] { hv, -hv + tf })
                {
                    p.Cordon("Soldadura", Penetracion(el) + " en los patines", new V3(-bf / 2, y, zj), new V3(bf / 2, y, zj), -Y, Z, tf, true);
                }
            }
            else
            {
                var tp = M(h, "tp");
                var bp = M(h, "bp");
                var lp = M(h, "Lp");
                var lw = M(h, "Lw");
                var w = M(h, "wp");
                p.Caja("Cubreplaca", "2 cubreplacas " + Pl(bp, lp, tp, ac), new V3(-bp / 2, hv, zj - (lp / 2)), new V3(bp / 2, hv + tp, zj + (lp / 2)), 'Y');
                p.Caja("Cubreplaca", "2 cubreplacas " + Pl(bp, lp, tp, ac), new V3(-bp / 2, -hv - tp, zj - (lp / 2)), new V3(bp / 2, -hv, zj + (lp / 2)), 'Y');
                var rot = Filete(w, el) + $", {Cm(lw * 100)} cm a cada lado de la junta";

                foreach (var lado in new[] { -1.0, 1.0 })
                {
                    var za = zj + (lado * (gj / 2));
                    var zb = za + (lado * lw);
                    FiletesDePlaca(p, rot, hv, bf, bp, Math.Max(za, zb), lw, w, true);
                    FiletesDePlaca(p, rot, -hv, bf, bp, Math.Max(za, zb), lw, w, false);
                }

                p.Cota(new V3(0, hv + tp, zj - (lp / 2)), new V3(0, hv + tp, zj + (lp / 2)), Y);
            }

            if (h.Si("PenW"))
            {
                p.Cordon("Soldadura", Penetracion(el) + " en el alma", new V3(tw / 2, -hv + tf, zj), new V3(tw / 2, hv - tf, zj), -X, Z, tw, true);
            }
            else
            {
                var np = Math.Max(1, (int)Math.Round(h.Num("npl")));
                var tsp = M(h, "tsp");
                var hsp = M(h, "hsp");
                var lhw = M(h, "Lhw");
                var ws = M(h, "ws");
                var largo = (2 * lhw) + gj;

                for (var k = 0; k < np; k++)
                {
                    var s = k == 0 ? 1.0 : -1.0;
                    var x0 = s * tw / 2;
                    p.Caja("Placa de alma", $"{np} placa(s) de alma " + Pl(largo, hsp, tsp, ac),
                        new V3(Math.Min(x0, x0 + (s * tsp)), -hsp / 2, zj - (largo / 2)),
                        new V3(Math.Max(x0, x0 + (s * tsp)), hsp / 2, zj + (largo / 2)), 'X');
                    var r = Filete(ws, el) + " placa de alma, en C a cada lado";

                    foreach (var lado in new[] { -1.0, 1.0 })
                    {
                        var zf = zj + (lado * largo / 2);
                        p.Cordon("Soldadura", r, new V3(x0, -hsp / 2, zf), new V3(x0, hsp / 2, zf), new V3(0, 0, lado), new V3(s, 0, 0), ws);
                    }
                }
            }

            p.Vista("ELEVACIÓN", Z, Y);
            p.Vista("PLANTA", Z, -X);
            return;
        }

        if (tipo == "CONTINUIDAD")
        {
            m.Titulo = "CONTINUIDAD DE VIGAS SOBRE TRABE";
            var zAlma = -gj - ((bft - twt) / 2);
            var zc = -gj - (bft / 2);
            p.Miembro("Viga", h.Txt("Perf"), FormaMiembro.I, new V3(0, 0, 0), new V3(0, 0, 0.7), Y, d, bf, tf, tw);
            p.Miembro("Trabe", h.Txt("PerfT"), FormaMiembro.I, new V3(-0.45, hv - (dt / 2), zc), new V3(0.45, hv - (dt / 2), zc), Y, dt, bft, tft, twt);

            var tp = M(h, "tp");
            var bp = M(h, "bp");
            var lp = M(h, "Lp");
            var lw = M(h, "Lw");
            var w = M(h, "wp");
            p.Caja("Placa de continuidad", "Placa de continuidad " + Pl(bp, lp, tp, ac), new V3(-bp / 2, hv, zc - (lp / 2)), new V3(bp / 2, hv + tp, zc + (lp / 2)), 'Y');
            FiletesDePlaca(p, Filete(w, el) + $", {Cm(lw * 100)} cm sobre la viga", hv, bf, bp, zc + (lp / 2), lw, w, true);
            p.Cordon("Soldadura", Penetracion(el) + " patín inferior al alma de la trabe",
                new V3(-bf / 2, -hv + tf, zAlma), new V3(bf / 2, -hv + tf, zAlma), -Y, Z, tf, true);

            var tsp = M(h, "tsp");
            var hsp = M(h, "hsp");
            var wst = M(h, "wst");
            var ancho = -zAlma + M(h, "Lhw");
            p.Caja("Placa de cortante", "Placa de cortante " + Pl(ancho, hsp, tsp, ac), new V3(tw / 2, -hsp / 2, zAlma), new V3((tw / 2) + tsp, hsp / 2, -zAlma > 0 ? M(h, "Lhw") : 0.1), 'X');
            p.Cordon("Soldadura", Filete(wst, el) + " placa al alma de la trabe, dos lados",
                new V3(tw / 2, -hsp / 2, zAlma), new V3(tw / 2, hsp / 2, zAlma), Z, -X, wst);
            p.Cota(new V3(0, hv + tp, zc - (lp / 2)), new V3(0, hv + tp, zc + (lp / 2)), Y);
            p.Vista("ELEVACIÓN", Z, Y);
            p.Vista("PLANTA", Z, -X);
            return;
        }

        // CRUCE directo: los patines a penetracion al canto de los patines de la trabe, el alma
        // despatinada hasta el alma de la trabe. Las caras de arriba al mismo nivel.
        m.Titulo = "CRUCE DE VIGAS SOLDADO, SIN PLACAS";
        var c = M(h, "ccop") > 1e-6 ? M(h, "ccop") : (bft - twt) / 2;
        var hw = M(h, "hwx") > 1e-6 ? M(h, "hwx") : d - (2 * tf);
        var zt = -(bft / 2);
        p.Miembro("Viga", h.Txt("Perf"), FormaMiembro.I, new V3(0, 0, 0), new V3(0, 0, 0.7), Y, d, bf, tf, tw);
        p.Miembro("Trabe", h.Txt("PerfT"), FormaMiembro.I, new V3(-0.45, hv - (dt / 2), zt), new V3(0.45, hv - (dt / 2), zt), Y, dt, bft, tft, twt);

        // La lengüeta del alma que entra hasta el alma de la trabe: es la viga misma.
        p.Caja("Alma despatinada", $"Alma despatinada c = {Cm(c * 100)} cm", new V3(-tw / 2, -(d / 2) + tf, -c), new V3(tw / 2, (d / 2) - tf, 0), 'X', soloDibujo: true);

        foreach (var y in new[] { hv, -hv + tf })
        {
            p.Cordon("Soldadura", Penetracion(el) + " patines al canto del patín de la trabe",
                new V3(-bf / 2, y, 0), new V3(bf / 2, y, 0), -Y, Z, tf, true);
        }

        var ww = M(h, "wwx");
        var rotAlma = h.Opcion("UwX") == "PENETRACION"
            ? Penetracion(el) + " alma al alma de la trabe"
            : Filete(ww, el) + " alma al alma de la trabe, dos caras";

        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Cordon("Soldadura", rotAlma, new V3(s * tw / 2, -hw / 2, -c), new V3(s * tw / 2, hw / 2, -c),
                Z, new V3(s, 0, 0), h.Opcion("UwX") == "PENETRACION" ? tw / 2 : ww, h.Opcion("UwX") == "PENETRACION");
        }

        p.Cota(new V3(0, hv, -c), new V3(0, hv, 0), Y);
        p.Cota(new V3(tw / 2, -hw / 2, -c), new V3(tw / 2, hw / 2, -c), -Z);
        p.Vista("ELEVACIÓN", Z, Y);
        p.Vista("PLANTA", Z, -X);
    }

    // =====================================================================
    //  PLACA GUSSET DE ESQUINA
    // =====================================================================

    private static void Gusset(HojaLeida h, ModeloConexion m)
    {
        m.Titulo = "PLACA GUSSET DE ESQUINA (DIAGONAL-VIGA-COLUMNA)";
        m.Colocacion = ColocacionConexion.ExtremoDeDiagonal;
        var p = new PiezasLocales(m);

        var tp = M(h, "tp");
        var th = h.Num("th") * Math.PI / 180;
        var eb = M(h, "eb") > 1e-6 ? M(h, "eb") : M(h, "dvb") / 2;
        var ec = M(h, "ec");
        var lgc = M(h, "Lgc");
        var lgb = M(h, "Lgb");
        var lw = M(h, "Lw");
        var w = M(h, "w");
        var b = M(h, "bws");
        var wb = M(h, "wb");
        var wc = M(h, "wc");
        var dlin = M(h, "dlin");
        var el = h.Txt("Elec");
        var ac = Acero(h, "Acero");
        m.AnguloDiagonalGrados = h.Num("th");

        var u = new V3(Math.Sin(th), Math.Cos(th), 0);
        var n = new V3(Math.Cos(th), -Math.Sin(th), 0);
        var libre = Math.Max(Math.Max(dlin, 2 * tp), 0.025);
        var mitad = (b / 2) + Math.Max(w, 0.006) + 0.01;

        // Donde empieza el tubo: sus dos esquinas libran la viga y la columna.
        var t0 = 0.0;

        foreach (var s in new[] { -1.0, 1.0 })
        {
            if (Math.Sin(th) > 1e-6)
            {
                t0 = Math.Max(t0, (ec + libre - (s * (b / 2) * Math.Cos(th))) / Math.Sin(th));
            }

            if (Math.Cos(th) > 1e-6)
            {
                t0 = Math.Max(t0, (eb + libre + (s * (b / 2) * Math.Sin(th))) / Math.Cos(th));
            }
        }

        var t1 = t0 + lw + 0.01;

        (double, double) En(double t, double desfase) => ((u * t).X + (n * desfase).X, (u * t).Y + (n * desfase).Y);

        var puntos = new List<(double U, double V)>
        {
            (ec, eb), (ec, eb + lgc), (ec + lgb, eb),
            En(t0, mitad), En(t0, -mitad), En(t1, mitad), En(t1, -mitad)
        };
        var contorno = Envolvente(puntos);
        p.Placa("Placa gusset", $"Placa gusset {Pulgadas(tp * 100)}{ac}", new V3(0, 0, -tp / 2), X, Y, contorno, tp);

        // Miembros: viga, columna y diagonal.
        var dvb = 2 * eb;
        var esAlPatin = ec > 0.02;
        p.Miembro("Viga", h.Txt("PerfV"), FormaMiembro.I, new V3(ec, 0, 0), new V3(ec + lgb + 0.4, 0, 0), Y,
            dvb, Math.Max(M(h, "tfb") * 12, dvb * 0.6), M(h, "tfb"), M(h, "twb"));
        var dcc = M(h, "dcc");

        if (esAlPatin)
        {
            p.Miembro("Columna", h.Txt("PerfC"), FormaMiembro.I, new V3(0, -0.2, 0), new V3(0, eb + lgc + 0.4, 0), X,
                dcc, Math.Max(M(h, "tfc") * 12, dcc * 0.6), M(h, "tfc"), M(h, "twc"));
        }
        else
        {
            // Al alma: el alma de la columna corre a lo largo de Z.
            p.Miembro("Columna", h.Txt("PerfC"), FormaMiembro.I, new V3(0, -0.2, 0), new V3(0, eb + lgc + 0.4, 0), Z,
                dcc, Math.Max(M(h, "tfc") * 12, dcc * 0.6), M(h, "tfc"), M(h, "twc"));
        }

        p.Miembro("Diagonal", string.Empty, FormaMiembro.Cajon, u * t0, u * (t1 + 0.6), n, b, b, M(h, "tm"), M(h, "tm"));

        // Soldaduras: a la viga y a la columna, por las dos caras; la diagonal con sus cordones.
        foreach (var s in new[] { -1.0, 1.0 })
        {
            var cara = new V3(0, 0, s * tp / 2);
            var haciaFuera = new V3(0, 0, s);
            p.Cordon("Soldadura", Filete(wb, el) + " placa a la viga, ambos lados",
                new V3(ec, eb, 0) + cara, new V3(ec + lgb, eb, 0) + cara, Y, haciaFuera, wb);
            p.Cordon("Soldadura", Filete(wc, el) + " placa a la columna, ambos lados",
                new V3(ec, eb, 0) + cara, new V3(ec, eb + lgc, 0) + cara, X, haciaFuera, wc);

            foreach (var lado in new[] { -1.0, 1.0 })
            {
                var o = n * (lado * b / 2);
                p.Cordon("Soldadura", $"{(int)Math.Round(h.Num("nw"))} cordones de {Cm(lw * 100)} cm, " + Filete(w, el),
                    (u * t0) + o + cara, (u * (t0 + lw)) + o + cara, n * lado, haciaFuera, w);
            }
        }

        p.Cota(new V3(ec, eb, 0), new V3(ec, eb + lgc, 0), -X);
        p.Cota(new V3(ec, eb, 0), new V3(ec + lgb, eb, 0), -Y);
        p.Cota((u * t0) + (n * (b / 2)), (u * (t0 + lw)) + (n * (b / 2)), n);
        m.Avisos.Add("La placa gusset se trazo con la envolvente de sus lados Lgc y Lgb y del extremo de la diagonal con su ranura; revisa en el dibujo las longitudes libres L1, L2, L3 y la distancia a la línea de doblez.");
        p.Vista("ELEVACIÓN", X, Y);
    }

    // =====================================================================
    //  CONTRAVENTEO EXCENTRICO: atiesadores del eslabon y placas de las diagonales
    // =====================================================================

    private static void Excentrico(HojaLeida h, ModeloConexion m)
    {
        m.Titulo = "CONTRAVENTEO EXCÉNTRICO: ESLABÓN, ATIESADORES Y PLACAS DE LAS DIAGONALES";
        m.Colocacion = ColocacionConexion.CentroDeViga;
        var p = new PiezasLocales(m);

        var lb = M(h, "Lb");
        var e = M(h, "e");
        var d = M(h, "d");
        var bf = M(h, "bf");
        var tf = M(h, "tf");
        var tw = M(h, "tw");
        var k = Math.Max(M(h, "kdv"), tf);
        var alfa = h.Num("alfa") * Math.PI / 180;
        var el = h.Txt("Elec");
        var hv = d / 2;

        var bse = M(h, "bse");
        var tse = M(h, "tse");
        var bsi = M(h, "bsi");
        var tsi = M(h, "tsi");
        var ws = M(h, "ws");
        var cls = M(h, "cls");
        var acS = Acero(h, "AcS");

        var tg = M(h, "tg");
        var lgi = M(h, "Lgi");
        var xc = M(h, "xc");
        var xin = M(h, "xin");
        var lwg = M(h, "Lwg");
        var wg = M(h, "wg");
        var b = M(h, "bcn") > 1e-6 ? M(h, "bcn") : M(h, "Bb");
        var tb = M(h, "tb") > 1e-6 ? M(h, "tb") : M(h, "tmw");
        var acG = Acero(h, "AcG");

        var largo = Math.Max(lb / 2, (e / 2) + Math.Abs(xc) + (lgi / 2) + 0.4);
        p.Miembro("Viga", h.Txt("PerfV"), FormaMiembro.I, new V3(-largo, 0, 0), new V3(largo, 0, 0), Y, d, bf, tf, tw);
        p.Trazo("Eslabón", new[] { new V3(-e / 2, hv + 0.03, 0), new V3(-e / 2, hv + 0.06, 0), new V3(e / 2, hv + 0.06, 0), new V3(e / 2, hv + 0.03, 0) });

        // Atiesadores: de extremo a los dos lados del alma; intermedios a uno o a los dos.
        void Atiesador(double x, double ancho, double t, bool dosLados, string rot)
        {
            var c = Math.Min(cls, Math.Min(ancho, d - (2 * tf)) * 0.4);
            var y0 = -hv + tf;
            var y1 = hv - tf;

            foreach (var s in dosLados ? new[] { 1.0, -1.0 } : new[] { 1.0 })
            {
                var z0 = s * tw / 2;
                var z1 = z0 + (s * ancho);
                var contorno = new List<(double U, double V)>
                {
                    (y0, z0 + (s * c)), (y0 + c, z0), (y1 - c, z0), (y1, z0 + (s * c)), (y1, z1), (y0, z1)
                };
                p.Placa("Atiesador", rot, new V3(x - (t / 2), 0, 0), Y, Z, contorno, t);

                foreach (var cara in new[] { -1.0, 1.0 })
                {
                    var xf = x + (cara * t / 2);
                    p.Cordon("Soldadura", Filete(ws, el) + " atiesadores al alma y a los patines",
                        new V3(xf, -hv + k, z0), new V3(xf, hv - k, z0), new V3(cara, 0, 0), new V3(0, 0, s), ws);
                    p.Cordon("Soldadura", Filete(ws, el) + " atiesadores al alma y a los patines",
                        new V3(xf, y1, z0 + (s * c)), new V3(xf, y1, z1), new V3(cara, 0, 0), -Y, ws);
                    p.Cordon("Soldadura", Filete(ws, el) + " atiesadores al alma y a los patines",
                        new V3(xf, y0, z0 + (s * c)), new V3(xf, y0, z1), new V3(cara, 0, 0), Y, ws);
                }
            }
        }

        var rotE = $"Atiesadores de extremo {Cm(bse * 100)} cm × {Pulgadas(tse * 100)}, dos lados{acS}";
        Atiesador(-e / 2, bse, tse, true, rotE);
        Atiesador(e / 2, bse, tse, true, rotE);

        var nint = (int)Math.Round(h.Num("nint"));
        var unLado = h.Opcion("Lado").Contains("UN", StringComparison.Ordinal);
        var rotI = $"Atiesadores intermedios {Cm(bsi * 100)} cm × {Pulgadas(tsi * 100)}{(unLado ? ", un lado" : ", dos lados")}{acS}";
        var posiciones = new List<double>();

        if (h.Si("Req") && nint > 0 && bsi > 1e-6 && tsi > 1e-6)
        {
            if (EtiquetasExcel.Normalizar(h.Txt("Tip")).Contains("corto", StringComparison.Ordinal) || !h.Tiene("Pos15"))
            {
                // Corto: a cada s desde un extremo, repartidos parejo en el eslabon.
                var paso = e / (nint + 1);
                posiciones.AddRange(Enumerable.Range(1, nint).Select(i => (-e / 2) + (i * paso)));
            }
            else
            {
                // Intermedio o largo: a 1.5 bf de cada extremo.
                posiciones.Add((-e / 2) + (1.5 * bf));
                posiciones.Add((e / 2) - (1.5 * bf));
            }

            foreach (var x in posiciones)
            {
                Atiesador(x, bsi, tsi, !unLado, rotI);
            }
        }

        // Las placas de las diagonales, bajo el patin, en cada extremo del eslabon.
        var libre = Math.Max(2 * tg, 0.025);
        var mitad = (b / 2) + Math.Max(wg, 0.006) + 0.01;
        var pd = new V3(Math.Cos(alfa), -Math.Sin(alfa), 0);
        var t0 = ((hv + libre) + ((b / 2) * Math.Cos(alfa))) / Math.Max(Math.Sin(alfa), 0.1);
        var t1 = t0 + lwg + 0.01;
        var penetracion = h.Opcion("Twi") == "PENETRACION";
        var wgi = M(h, "wgi");

        foreach (var s in new[] { 1.0, -1.0 })
        {
            var wp = new V3(s * ((e / 2) - xin), 0, 0);
            var u = new V3(s * pd.X, pd.Y, 0);
            var n = new V3(Math.Sin(alfa), s * Math.Cos(alfa), 0);
            var centro = wp.X + (s * xc);

            (double, double) En(double t, double desfase) => (wp.X + (u * t).X + (n * desfase).X, (u * t).Y + (n * desfase).Y);

            var puntos = new List<(double U, double V)>
            {
                (centro - (lgi / 2), -hv), (centro + (lgi / 2), -hv),
                En(t0, mitad), En(t0, -mitad), En(t1, mitad), En(t1, -mitad)
            };
            p.Placa("Placa de la diagonal", $"Placa de la diagonal {Pulgadas(tg * 100)}, Lgi = {Cm(lgi * 100)} cm{acG}",
                new V3(0, 0, -tg / 2), X, Y, Envolvente(puntos), tg);

            p.Miembro("Diagonal", h.Txt("PerfB"), FormaMiembro.Cajon, wp + (u * t0), wp + (u * (t1 + 0.6)), n, b, b, tb, tb);

            foreach (var cara in new[] { -1.0, 1.0 })
            {
                var cz = new V3(0, 0, cara * tg / 2);
                var fuera = new V3(0, 0, cara);

                if (penetracion)
                {
                    p.Cordon("Soldadura", Penetracion(el) + " placa al patín de la viga",
                        new V3(centro - (lgi / 2), -hv, 0), new V3(centro + (lgi / 2), -hv, 0), -Y, fuera, tg / 2, true);
                }
                else
                {
                    p.Cordon("Soldadura", Filete(wgi, el) + " placa al patín de la viga, ambos lados",
                        new V3(centro - (lgi / 2), -hv, 0) + cz, new V3(centro + (lgi / 2), -hv, 0) + cz, -Y, fuera, wgi);
                }

                foreach (var lado in new[] { -1.0, 1.0 })
                {
                    var o = n * (lado * b / 2);
                    p.Cordon("Soldadura", $"{(int)Math.Round(h.Num("nwc"))} cordones de {Cm(lwg * 100)} cm, " + Filete(wg, el),
                        wp + (u * t0) + o + cz, wp + (u * (t0 + lwg)) + o + cz, n * lado, fuera, wg);
                }
            }

            p.Cota(new V3(centro - (lgi / 2), -hv, 0), new V3(centro + (lgi / 2), -hv, 0), Y);
        }

        p.Cota(new V3(-e / 2, hv, 0), new V3(e / 2, hv, 0), Y, $"e = {Cm(e * 100)}");

        if (posiciones.Count > 0)
        {
            var cadena = new List<double> { -e / 2 };
            cadena.AddRange(posiciones.OrderBy(x => x));
            cadena.Add(e / 2);

            for (var i = 0; i + 1 < cadena.Count; i++)
            {
                p.Cota(new V3(cadena[i], -hv, 0), new V3(cadena[i + 1], -hv, 0), -Y);
            }
        }

        m.Avisos.Add("El eslabón es zona protegida: sin soldaduras de accesorios, conectores de cortante ni agujeros.");
        p.Vista("ELEVACIÓN", X, Y);
        p.Vista("CORTE EN EL ATIESADOR", -Z, Y);
    }

    // =====================================================================
    //  CONTRAVENTEO EN X EN UN ENTREPISO: placa del cruce o empalme
    // =====================================================================

    private static void CruceEnX(HojaLeida h, ModeloConexion m)
    {
        var placa = h.Opcion("Cruce") != "EMPALMADA";
        m.Titulo = placa ? "CONTRAVENTEO EN X: PLACA DEL CRUCE" : "CONTRAVENTEO EN X: CRUCE EMPALMADO";
        m.Colocacion = ColocacionConexion.CentroDeDiagonal;
        var p = new PiezasLocales(m);

        var alfa = h.Num("alfa") * Math.PI / 180;
        var b = M(h, "Din");
        var t = M(h, "tw_");
        var tp = M(h, "tp");
        var lw = M(h, "Lw");
        var w = M(h, "w");
        var el = h.Txt("Elec");
        var ac = Acero(h, "AcP");
        var nr = (int)Math.Round(h.Num("nr"));
        var br = M(h, "br");
        var trf = M(h, "trf");
        var dlin = M(h, "dlin");

        var brazos = new[]
        {
            new V3(Math.Cos(alfa), Math.Sin(alfa), 0), new V3(-Math.Cos(alfa), -Math.Sin(alfa), 0),
            new V3(Math.Cos(alfa), -Math.Sin(alfa), 0), new V3(-Math.Cos(alfa), Math.Sin(alfa), 0)
        };

        if (placa)
        {
            // Los cuatro brazos llegan a la placa; los tubos se libran entre si por 2·max(a, 2.5 cm).
            var phi = Math.Min(2 * alfa, Math.PI - (2 * alfa));
            var libre = 2 * Math.Max(dlin, 0.025);
            var t0 = ((libre / 2) + ((b / 2) * Math.Cos(phi / 2))) / Math.Max(Math.Sin(phi / 2), 0.1);
            var t1 = t0 + lw + 0.01;
            var mitad = (b / 2) + Math.Max(w, 0.006) + 0.01;
            var puntos = new List<(double U, double V)>();

            foreach (var u in brazos)
            {
                var n = new V3(-u.Y, u.X, 0);

                foreach (var tt in new[] { t0, t1 })
                {
                    foreach (var s in new[] { -1.0, 1.0 })
                    {
                        var q = (u * tt) + (n * (s * mitad));
                        puntos.Add((q.X, q.Y));
                    }
                }
            }

            p.Placa("Placa del cruce", $"Placa del cruce {Pulgadas(tp * 100)}{ac}", new V3(0, 0, -tp / 2), X, Y, Envolvente(puntos), tp);

            foreach (var u in brazos)
            {
                var n = new V3(-u.Y, u.X, 0);
                p.Miembro("Diagonal", h.Txt("Perf"), FormaMiembro.Cajon, u * t0, u * (t1 + 0.5), n, b, b, t, t);

                foreach (var cara in new[] { -1.0, 1.0 })
                {
                    var cz = new V3(0, 0, cara * tp / 2);

                    foreach (var lado in new[] { -1.0, 1.0 })
                    {
                        var o = n * (lado * b / 2);
                        p.Cordon("Soldadura", $"{(int)Math.Round(h.Num("nw"))} cordones de {Cm(lw * 100)} cm por diagonal, " + Filete(w, el),
                            (u * t0) + o + cz, (u * (t0 + lw)) + o + cz, n * lado, new V3(0, 0, cara), w);
                    }
                }

                Refuerzos(p, h, u, n, t0, lw, b, nr, br, trf, ac, el, m);
            }

            p.Cota(brazos[0] * t0 + (new V3(-brazos[0].Y, brazos[0].X, 0) * (b / 2)),
                brazos[0] * (t0 + lw) + (new V3(-brazos[0].Y, brazos[0].X, 0) * (b / 2)), new V3(-brazos[0].Y, brazos[0].X, 0));
        }
        else
        {
            // Una diagonal sigue; la otra se interrumpe y se une con dos cubreplacas.
            var bsp = M(h, "bsp");
            var tsp = M(h, "tsp");
            var ls = M(h, "Ls");
            var wsp = M(h, "wsp");
            var g0 = M(h, "g0");
            var u = brazos[0];
            var n = new V3(-u.Y, u.X, 0);
            p.Miembro("Diagonal continua", h.Txt("Perf"), FormaMiembro.Cajon, u * -1.0, u * 1.0, n, b, b, t, t);
            var v = brazos[2];
            var nv = new V3(-v.Y, v.X, 0);
            var corte = (b / 2 / Math.Max(Math.Sin(2 * alfa), 0.2)) + g0;
            p.Miembro("Diagonal interrumpida", h.Txt("Perf"), FormaMiembro.Cajon, v * corte, v * 1.0, nv, b, b, t, t);
            p.Miembro("Diagonal interrumpida", h.Txt("Perf"), FormaMiembro.Cajon, v * -corte, v * -1.0, nv, b, b, t, t);
            var largo = (2 * corte) + (2 * ls);

            foreach (var lado in new[] { -1.0, 1.0 })
            {
                var z0 = lado > 0 ? b / 2 : -(b / 2) - tsp;
                var contorno = new List<(double U, double V)>
                {
                    (-largo / 2, -bsp / 2), (largo / 2, -bsp / 2), (largo / 2, bsp / 2), (-largo / 2, bsp / 2)
                };
                p.Placa("Cubreplaca", $"2 cubreplacas {Cm(bsp * 100)} × {Cm(largo * 100)} cm × {Pulgadas(tsp * 100)}{ac}",
                    new V3(0, 0, z0), v, nv, contorno, tsp);

                foreach (var s in new[] { -1.0, 1.0 })
                {
                    foreach (var mitadDe in new[] { -1.0, 1.0 })
                    {
                        var a0 = v * (mitadDe * corte);
                        var a1 = v * (mitadDe * (corte + ls));
                        var o = (nv * (s * bsp / 2)) + new V3(0, 0, lado > 0 ? b / 2 : -b / 2);
                        p.Cordon("Soldadura", Filete(wsp, el) + $" cubreplacas, {Cm(ls * 100)} cm sobre cada mitad",
                            a0 + o, a1 + o, nv * s, new V3(0, 0, lado), wsp);
                    }
                }
            }

            m.Avisos.Add("El cruce empalmado se dibuja con la diagonal continua a lo largo de X positivo; revisa que la interrumpida sea la que dice el plano.");
        }

        p.Vista("ELEVACIÓN", X, Y);
    }

    /// <summary>Las placas de refuerzo de la seccion neta, en las caras del tubo paralelas a la placa.</summary>
    private static void Refuerzos(PiezasLocales p, HojaLeida h, V3 u, V3 n, double t0, double lw, double b, int nr, double br, double trf,
        string ac, string el, ModeloConexion m)
    {
        if (nr <= 0 || br <= 1e-6 || trf <= 1e-6)
        {
            return;
        }

        // La hoja no dice el largo: se cubre la ranura y otro tanto mas alla de su fondo.
        var a0 = t0 + (lw / 2);
        var a1 = t0 + (1.5 * lw);
        var contorno = new List<(double U, double V)> { (a0, -br / 2), (a1, -br / 2), (a1, br / 2), (a0, br / 2) };

        foreach (var lado in nr >= 2 ? new[] { 1.0, -1.0 } : new[] { 1.0 })
        {
            var z0 = lado > 0 ? b / 2 : -(b / 2) - trf;
            p.Placa("Placa de refuerzo", $"{nr} placas de refuerzo {Cm(br * 100)} cm × {Pulgadas(trf * 100)}{ac}",
                new V3(0, 0, z0), u, n, contorno, trf);
        }

        if (!m.Avisos.Any(a => a.StartsWith("Las placas de refuerzo", StringComparison.Ordinal)))
        {
            m.Avisos.Add("Las placas de refuerzo se dibujaron de Lw de largo, desde la mitad de la ranura: la hoja no da su longitud.");
        }
    }

    // =====================================================================
    //  PLACA BASE: va por el camino de la hoja Placa Base de CadLink
    // =====================================================================

    private static void PlacaBase(HojaLeida h, ModeloConexion m)
    {
        m.Titulo = "PLACA BASE";
        m.Colocacion = ColocacionConexion.BaseDeColumna;
        m.PlacaBase = PlacaBaseDelLibro.AJson(h);
    }
}
