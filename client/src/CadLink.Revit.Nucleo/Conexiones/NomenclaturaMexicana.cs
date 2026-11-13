using System.Globalization;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LA NOMENCLATURA MEXICANA DE LOS PERFILES (IMCA), PARA LOS TEXTOS DEL PLANO
//
//  El libro y los catalogos nombran los perfiles como el AISC («W - 18'' x 97.03 lb/ft»,
//  «HSS - 5" x 5/16"»). En el plano van con el NOMBRE mexicano y las medidas en PULGADAS, que es
//  como se ubican en obra y en el taller. Solo cambia el TEXTO: los datos y las busquedas siguen
//  con el nombre del libro.
//
//      W - 18'' x 97.03 lb/ft      →  IR 18" x 97.03 lb/ft
//      WT - 8'' x 13.0 lb/ft       →  TR 8" x 13.0 lb/ft
//      S - 10'' x 25.4 lb/ft       →  IE 10" x 25.4 lb/ft
//      C - 8'' x 12.0 lb/ft        →  CE 8" x 12.0 lb/ft
//      HSS - 5" x 5/16"            →  OR 5" x 5/16"
//      HSS - 8" x 4" x 1/2"        →  OR 8" x 4" x 1/2"
//      PIPE - 4.02 in x 0.19 in    →  OC 4.02" x 0.19"
//      L - 3'' x 1/4''             →  LI 3" x 1/4"   (LD si los lados son distintos)
// ============================================================================

public static class NomenclaturaMexicana
{
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
            return $"{prefijo} {m.Groups[2].Value.Trim()}\" x {m.Groups[3].Value} lb/ft";
        });

        t = Tubo.Replace(t, m => m.Groups[2].Success
            ? $"OR {m.Groups[1].Value.Trim()}\" x {m.Groups[2].Value.Trim()}\" x {m.Groups[3].Value.Trim()}\""
            : $"OR {m.Groups[1].Value.Trim()}\" x {m.Groups[3].Value.Trim()}\"");

        t = TuboRedondo.Replace(t, m => $"OC {m.Groups[1].Value}\" x {m.Groups[2].Value}\"");

        t = Angulo.Replace(t, m =>
        {
            var iguales = !m.Groups[2].Success || Math.Abs(Pulg(m.Groups[1].Value) - Pulg(m.Groups[2].Value)) < 1e-6;
            return iguales
                ? $"LI {m.Groups[1].Value.Trim()}\" x {m.Groups[3].Value.Trim()}\""
                : $"LD {m.Groups[1].Value.Trim()}\" x {m.Groups[2].Value.Trim()}\" x {m.Groups[3].Value.Trim()}\"";
        });

        return t;
    }

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
