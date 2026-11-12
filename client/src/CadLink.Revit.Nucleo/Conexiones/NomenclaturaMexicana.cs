using System.Globalization;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LA NOMENCLATURA MEXICANA DE LOS PERFILES (IMCA), PARA LOS TEXTOS DEL PLANO
//
//  El libro y los catalogos nombran los perfiles como el AISC («W - 18'' x 97.03 lb/ft»,
//  «HSS - 5" x 5/16"»). En el plano van como el IMCA: peralte en mm y peso en kg/m, o las caras y
//  la pared en mm. Solo cambia el TEXTO: los datos y las busquedas siguen con el nombre del libro.
//
//      W - 18'' x 97.03 lb/ft      →  IR 457 x 144.4 kg/m
//      WT - 8'' x 13.0 lb/ft       →  TR 203 x 19.3 kg/m
//      S - 10'' x 25.4 lb/ft       →  IE 254 x 37.8 kg/m
//      C - 8'' x 12.0 lb/ft        →  CE 203 x 17.9 kg/m
//      HSS - 5" x 5/16"            →  OR 127 x 127 x 7.9 mm
//      HSS - 8" x 4" x 1/2"        →  OR 203 x 102 x 12.7 mm
//      PIPE - 4.02 in x 0.19 in    →  OC 102 x 4.8 mm
//      L - 3'' x 1/4''             →  LI 76 x 6.4 mm   (LD si los lados son distintos)
// ============================================================================

public static class NomenclaturaMexicana
{
    private const double Pulgada = 25.4;
    private const double LbPorPieAKgPorM = 1.48816;

    // Las comillas del libro: '' o " (y sus tipograficas).
    private const string Comillas = "(?:''|\"|”|″)";
    private const string Medida = @"(\d+(?:\.\d+)?(?:\s+\d+/\d+)?|\d+/\d+)";

    private static readonly Regex Laminado = new(
        @"\b(W|WT|S|C)\s*-\s*" + Medida + @"\s*" + Comillas + @"\s*x\s*(\d+(?:\.\d+)?)\s*lb/ft",
        RegexOptions.Compiled);

    private static readonly Regex Tubo = new(
        @"\bHSS\s*-\s*" + Medida + @"\s*" + Comillas + @"(?:\s*x\s*" + Medida + @"\s*" + Comillas + @")?\s*x\s*" + Medida + @"\s*" + Comillas,
        RegexOptions.Compiled);

    private static readonly Regex TuboRedondo = new(
        @"\bPIPE\s*-\s*(\d+(?:\.\d+)?)\s*in\s*x\s*(\d+(?:\.\d+)?)\s*in", RegexOptions.Compiled);

    private static readonly Regex Angulo = new(
        @"\bL\s*-\s*" + Medida + @"\s*" + Comillas + @"(?:\s*x\s*" + Medida + @"\s*" + Comillas + @")?\s*x\s*" + Medida + @"\s*" + Comillas,
        RegexOptions.Compiled);

    /// <summary>El texto con cada perfil del AISC escrito como el IMCA.</summary>
    public static string Convertir(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto ?? string.Empty;
        }

        var t = Laminado.Replace(texto, m =>
        {
            var prefijo = m.Groups[1].Value switch { "W" => "IR", "WT" => "TR", "S" => "IE", _ => "CE" };
            var d = Pulg(m.Groups[2].Value) * Pulgada;
            var kg = N(m.Groups[3].Value) * LbPorPieAKgPorM;
            return $"{prefijo} {Mm(d)} x {kg.ToString("0.0", CultureInfo.InvariantCulture)} kg/m";
        });

        t = Tubo.Replace(t, m =>
        {
            var a = Pulg(m.Groups[1].Value) * Pulgada;
            var b = m.Groups[2].Success ? Pulg(m.Groups[2].Value) * Pulgada : a;
            var e = Pulg(m.Groups[3].Value) * Pulgada;
            return $"OR {Mm(a)} x {Mm(b)} x {Dec(e)} mm";
        });

        t = TuboRedondo.Replace(t, m => $"OC {Mm(N(m.Groups[1].Value) * Pulgada)} x {Dec(N(m.Groups[2].Value) * Pulgada)} mm");

        t = Angulo.Replace(t, m =>
        {
            var a = Pulg(m.Groups[1].Value) * Pulgada;
            var b = m.Groups[2].Success ? Pulg(m.Groups[2].Value) * Pulgada : a;
            var e = Pulg(m.Groups[3].Value) * Pulgada;
            return Math.Abs(a - b) < 0.5
                ? $"LI {Mm(a)} x {Dec(e)} mm"
                : $"LD {Mm(a)} x {Mm(b)} x {Dec(e)} mm";
        });

        return t;
    }

    private static string Mm(double mm) => Math.Round(mm).ToString("0", CultureInfo.InvariantCulture);

    private static string Dec(double mm) => mm.ToString("0.0", CultureInfo.InvariantCulture);

    private static double N(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>«5», «4 1/2», «5/16», «1.5» en pulgadas.</summary>
    private static double Pulg(string s)
    {
        s = s.Trim();
        var partes = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var total = 0.0;

        foreach (var p in partes)
        {
            var i = p.IndexOf('/');
            total += i > 0 ? N(p[..i]) / N(p[(i + 1)..]) : N(p);
        }

        return total;
    }
}
