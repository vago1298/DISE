using System.Globalization;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

/// <summary>
/// Saca las medidas del NOMBRE de un tipo de Revit, cuando no se pudieron leer de sus
/// parametros.
/// </summary>
/// <remarks>
/// <para>
/// Es un respaldo, no la via principal. Lo normal es leer los parametros del tipo -"b" y
/// "h", o la seccion estructural- y solo cuando eso no da nada se mira el nombre, porque los
/// tipos de las plantillas se llaman precisamente por sus medidas: <c>300 x 450</c>,
/// <c>Hormigón 200 mm</c>, <c>Losa 120 mm</c>.
/// </para>
/// <para>
/// Y es un respaldo PRUDENTE. Un "12 x 18" puede ser pulgadas o centimetros, y equivocarse
/// ahi propondria una familia con las medidas mal por un factor de dos y medio. Asi que solo
/// se contesta cuando se puede estar razonablemente seguro: con unidad escrita, o con numeros
/// tan grandes que solo pueden ser milimetros. En la duda se devuelve <c>null</c>, que el
/// emparejador ya sabe tratar como "no se sabe".
/// </para>
/// </remarks>
public static class MedidasPorNombre
{
    /// <summary>
    /// Por encima de esto, un numero sin unidad solo puede ser milimetros.
    /// </summary>
    /// <remarks>
    /// Ninguna seccion de edificio mide 50 cm de canto escrito como "50"; un 300 es 300 mm.
    /// Por debajo del umbral no se adivina.
    /// </remarks>
    public const double MinimoParaMilimetros = 50;

    private static readonly Regex DosNumeros = new(
        @"(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnNumero = new(
        @"(\d+(?:[.,]\d+)?)",
        RegexOptions.Compiled);

    /// <summary>Dos medidas, en metros, o <c>null</c> si no se puede saber.</summary>
    public static (double AnchoM, double PeralteM)? Dos(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return null;
        }

        var m = DosNumeros.Match(nombre);

        if (!m.Success)
        {
            return null;
        }

        var a = Numero(m.Groups[1].Value);
        var b = Numero(m.Groups[2].Value);

        if (a is null || b is null)
        {
            return null;
        }

        var factor = Factor(nombre, Math.Min(a.Value, b.Value));

        if (factor is null)
        {
            return null;
        }

        return (a.Value * factor.Value, b.Value * factor.Value);
    }

    /// <summary>Una sola medida -un espesor-, en metros, o <c>null</c>.</summary>
    public static double? Una(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return null;
        }

        // Si hay dos numeros con una x en medio, esto no es un espesor: es una seccion.
        if (DosNumeros.IsMatch(nombre))
        {
            return null;
        }

        var m = UnNumero.Match(nombre);

        if (!m.Success)
        {
            return null;
        }

        var v = Numero(m.Groups[1].Value);

        if (v is null)
        {
            return null;
        }

        var factor = Factor(nombre, v.Value);

        return factor is null ? null : v.Value * factor.Value;
    }

    private static double? Numero(string s)
    {
        var limpio = s.Replace(',', '.');

        return double.TryParse(limpio, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
               && v > 0
            ? v
            : null;
    }

    /// <summary>Con que multiplicar para llegar a metros, o <c>null</c> si no se sabe.</summary>
    private static double? Factor(string nombre, double menor)
    {
        var t = nombre.ToLowerInvariant();

        // Con la unidad escrita no hay que adivinar. Se busca "mm" antes que "m" porque "mm"
        // contiene una "m".
        if (Tiene(t, "mm"))
        {
            return 0.001;
        }

        if (Tiene(t, "cm"))
        {
            return 0.01;
        }

        if (Tiene(t, "m"))
        {
            return 1.0;
        }

        if (Tiene(t, "\"") || Tiene(t, "in") || Tiene(t, "pulg"))
        {
            // Las pulgadas se reconocen para NO adivinar: se sabe que no son milimetros,
            // pero convertirlas aqui abriria la puerta a fracciones como 1/4 y a nombres
            // como HSS3X2X1/4, que no son medidas de la seccion completa.
            return null;
        }

        // Sin unidad: solo se acepta si los numeros son demasiado grandes para ser otra cosa.
        return menor >= MinimoParaMilimetros ? 0.001 : null;
    }

    /// <summary>Si la unidad aparece como palabra suelta o pegada a un numero.</summary>
    private static bool Tiene(string texto, string unidad) =>
        Regex.IsMatch(texto, @"\d\s*" + Regex.Escape(unidad) + @"(\b|$)")
        || Regex.IsMatch(texto, @"(^|\s)" + Regex.Escape(unidad) + @"(\b|$)");
}
