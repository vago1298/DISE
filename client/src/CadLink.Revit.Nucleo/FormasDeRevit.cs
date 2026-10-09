namespace CadLink.Revit.Nucleo;

/// <summary>
/// Traduce el nombre de forma estructural que da Revit a la forma que entiende el nucleo.
/// </summary>
/// <remarks>
/// <para>
/// Revit sabe de verdad que forma tiene un perfil: <c>FamilySymbol.GetStructuralSection()</c>
/// devuelve una seccion cuya <c>StructuralSectionShape</c> dice si es una I de patin ancho,
/// un HSS rectangular, un angulo... Eso es mucho mas fiable que adivinarlo del nombre de la
/// familia, que depende de la plantilla de cada despacho.
/// </para>
/// <para>
/// Pero el complemento no pasa el ENUM: pasa su nombre como TEXTO, y la traduccion ocurre
/// aqui. Hay dos razones y las dos importan:
/// </para>
/// <list type="number">
///   <item>
///   Asi esta traduccion tiene pruebas. Si estuviera en el complemento, estaria en la unica
///   parte del proyecto que no se puede compilar ni ejecutar sin Revit instalado.
///   </item>
///   <item>
///   Y asi no se escribe en el codigo el nombre de ningun miembro de ese enum. La lista de
///   formas de Revit cambia entre versiones; nombrar un miembro que en la version instalada
///   no existe es un error de COMPILACION, mientras que comparar textos que no coinciden es
///   como mucho una forma que no se reconoce, que el emparejador ya sabe tratar.
///   </item>
/// </list>
/// </remarks>
public static class FormasDeRevit
{
    /// <summary>
    /// Palabras que identifican la forma SIN ambiguedad.
    /// </summary>
    /// <remarks>
    /// Se prueban antes que la letra del prefijo, y el orden entre ellas tambien importa:
    /// <list type="bullet">
    ///   <item>la te va primero, porque en Revit se llama <c>ISplitTee</c> y empieza por I;</item>
    ///   <item>los huecos antes que los macizos, y el redondo antes que el rectangular,
    ///   porque los dos llevan "HSS";</item>
    ///   <item><c>Circular</c> va aqui aunque empiece por C, para que la regla del prefijo no
    ///   la confunda con un canal.</item>
    /// </list>
    /// </remarks>
    private static readonly (FormaSeccion Forma, string[] Palabras)[] Inequivocas =
    {
        (FormaSeccion.PerfilT, new[] { "splittee", "tee", "tshape", "tprofile" }),
        (FormaSeccion.Tubo, new[] { "roundhss", "pipe", "circularhollow", "chs" }),
        (FormaSeccion.Cajon, new[] { "rectanglehss", "rectangularhss", "squarehss", "hss", "box" }),
        (FormaSeccion.Circulo, new[] { "roundbar", "circular", "round" }),
        (FormaSeccion.Rectangulo, new[] { "rectangularbar", "rectangle", "rectangular", "square" })
    };

    /// <summary>
    /// Palabras que NO distinguen por si solas, y por eso se prueban al final.
    /// </summary>
    /// <remarks>
    /// "ParallelFlange" sale igual en <c>IParallelFlange</c> -una I- y en
    /// <c>CParallelFlange</c> -un canal-. La palabra sola no dice cual es; la letra del
    /// prefijo si. De ahi que estas vayan despues.
    /// </remarks>
    private static readonly (FormaSeccion Forma, string[] Palabras)[] Ambiguas =
    {
        (FormaSeccion.PerfilI, new[] { "wideflange", "ibeam", "ishape", "iprofile" }),
        (FormaSeccion.PerfilC, new[] { "channel", "cshape", "cprofile" }),
        (FormaSeccion.PerfilL, new[] { "angle", "lshape", "lprofile" })
    };

    /// <summary>
    /// La forma que corresponde al nombre, o <c>null</c> si no se reconoce.
    /// </summary>
    /// <remarks>
    /// Devolver <c>null</c> no es un fallo: el emparejador lo trata como "no se sabe" y pasa
    /// a deducirlo del nombre de la familia. Es mejor que adivinar mal.
    /// </remarks>
    public static FormaSeccion? Interpretar(string? nombreDeForma)
    {
        if (string.IsNullOrWhiteSpace(nombreDeForma))
        {
            return null;
        }

        var original = nombreDeForma.Trim();

        // Se quitan los separadores para que "Rectangle HSS", "RectangleHSS" y
        // "rectangle_hss" den lo mismo.
        var t = Limpio(original.ToLowerInvariant());

        if (t.Length == 0)
        {
            return null;
        }

        // "NotDefined" y "Other" son valores reales del enum de Revit y significan justo
        // "no se sabe", asi que se contesta que no se sabe en vez de buscarles forma.
        if (t.Contains("notdefined", StringComparison.Ordinal)
            || t.Equals("other", StringComparison.Ordinal)
            || t.Contains("undefined", StringComparison.Ordinal))
        {
            return null;
        }

        var porPalabra = Buscar(t, Inequivocas);

        if (porPalabra is not null)
        {
            return porPalabra;
        }

        var porLetra = PorPrefijo(original);

        if (porLetra is not null)
        {
            return porLetra;
        }

        return Buscar(t, Ambiguas);
    }

    private static FormaSeccion? Buscar(string limpio, (FormaSeccion Forma, string[] Palabras)[] tabla)
    {
        foreach (var (forma, palabras) in tabla)
        {
            foreach (var p in palabras)
            {
                if (limpio.Contains(Limpio(p), StringComparison.Ordinal))
                {
                    return forma;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// La letra con que Revit prefija el nombre de un perfil laminado, si la lleva.
    /// </summary>
    /// <remarks>
    /// Los nombres de <c>StructuralSectionShape</c> van en PascalCase, y los perfiles
    /// laminados empiezan por SU letra seguida de otra mayuscula: <c>IWideFlange</c>,
    /// <c>CParallelFlange</c>, <c>LAngle</c>. Se exige que la segunda sea mayuscula para no
    /// atrapar palabras normales: "Circular" empieza por C y no es un canal.
    /// </remarks>
    private static FormaSeccion? PorPrefijo(string original)
    {
        if (original.Length < 2 || !char.IsUpper(original[0]) || !char.IsUpper(original[1]))
        {
            return null;
        }

        return original[0] switch
        {
            'I' => FormaSeccion.PerfilI,
            'C' => FormaSeccion.PerfilC,
            'L' => FormaSeccion.PerfilL,
            'T' => FormaSeccion.PerfilT,
            _ => null
        };
    }

    private static string Limpio(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}
