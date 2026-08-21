using System.Globalization;

namespace CadLink.App.Models;

/// <summary>
/// Un acero estructural del catálogo: su <b>Fy</b>, su <b>Fu</b> y en qué secciones se hace.
/// </summary>
/// <remarks>
/// <para>
/// Sale de <c>aceros.csv</c>, que se genera de la hoja <c>ACEROS.xlsx</c> con
/// <c>tools/catalogo_aceros.py</c>. El archivo va <b>suelto</b> junto al ejecutable a
/// propósito, igual que el catálogo de perfiles: el día que cambie una norma o aparezca un
/// acero nuevo se edita el archivo y ya está, sin recompilar nada.
/// </para>
/// <para>
/// <b>La disponibilidad tiene TRES respuestas, no dos</b>: se hace, hay que verificarlo, o
/// no se hace. <c>VERIFICAR</c> no es <c>NO</c>, y confundirlos tiene precio en las dos
/// direcciones: marcar en rojo un acero que sí se puede conseguir hace cambiar de acero sin
/// necesidad, y dar por bueno en silencio uno que hay que confirmar deja al calculista
/// creyendo que ya está confirmado.
/// </para>
/// </remarks>
/// <param name="Grupo">CARBÓN, ALTA RESISTENCIA…, RESISTENTE A CORROSIÓN o TEMPLADO REVENIDO.</param>
/// <param name="Astm">La designación ASTM, que es lo que se ve en el desplegable.</param>
/// <param name="Nmx">La norma mexicana equivalente, o <c>-</c> si no tiene.</param>
/// <param name="FyKgCm2">Esfuerzo de fluencia, en kg/cm².</param>
/// <param name="FyMpa">El mismo en MPa, que es como lo dan las normas.</param>
/// <param name="FuKgCm2">Esfuerzo último, en kg/cm².</param>
/// <param name="FuMpa">El mismo en MPa.</param>
/// <param name="Disponibilidad">Familia de perfil a <c>SI</c>, <c>VERIFICAR</c> o <c>NO</c>.</param>
/// <param name="Placa">Si se consigue en placa. No es una familia: CadLink no dibuja placas.</param>
public sealed record AceroCatalogo(
    string Grupo,
    string Astm,
    string Nmx,
    double FyKgCm2,
    double? FyMpa,
    double? FuKgCm2,
    double? FuMpa,
    IReadOnlyDictionary<string, string> Disponibilidad,
    string Placa)
{
    /// <summary>Se consigue en esa sección.</summary>
    public const string Si = "SI";

    /// <summary>Puede conseguirse; hay que confirmarlo con el proveedor.</summary>
    public const string Verificar = "VERIFICAR";

    /// <summary>No se hace en esa sección.</summary>
    public const string No = "NO";

    /// <summary>Qué dice el manual de este acero en esa familia de perfil.</summary>
    /// <remarks>
    /// Una familia que no esté en el diccionario devuelve <see cref="Verificar"/>, no
    /// <see cref="No"/>. Es la respuesta honesta: que el catálogo no diga nada de una
    /// familia no significa que el acero no se haga en ella, significa que no se sabe, y
    /// marcarla en rojo sería afirmar algo que el archivo no dice.
    /// </remarks>
    public string DisponibleEn(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return f.Length > 0 && Disponibilidad.TryGetValue(f, out var v) ? v : Verificar;
    }

    /// <summary>Lo que se pone en la celda de disponibilidad de la cuadrícula.</summary>
    public string LeyendaEn(string? familia) => DisponibleEn(familia) switch
    {
        Si => "Sí",
        No => "No se hace",
        _ => "Verificar"
    };

    /// <summary>El acero con sus dos esfuerzos, para el globo de ayuda de la celda.</summary>
    public string Detalle
    {
        get
        {
            var c = CultureInfo.CurrentCulture;

            var texto = $"{Astm}";

            if (Nmx.Length > 0 && Nmx != "-")
            {
                texto += $"   (NMX {Nmx})";
            }

            texto += $"\nFy = {FyKgCm2.ToString("N0", c)} kg/cm²";

            if (FyMpa is { } fym)
            {
                texto += $"  ({fym.ToString("N0", c)} MPa)";
            }

            if (FuKgCm2 is { } fu)
            {
                texto += $"\nFu = {fu.ToString("N0", c)} kg/cm²";

                if (FuMpa is { } fum)
                {
                    texto += $"  ({fum.ToString("N0", c)} MPa)";
                }
            }

            if (Grupo.Length > 0)
            {
                texto += $"\n{Grupo}";
            }

            return texto;
        }
    }
}

/// <summary>
/// El catálogo de aceros: se lee de <c>aceros.csv</c> y llena el desplegable «Acero».
/// </summary>
public static class CatalogoAceros
{
    /// <summary>Nombre del archivo de catálogo.</summary>
    public const string Archivo = "aceros.csv";

    private static List<AceroCatalogo>? _aceros;
    private static Dictionary<string, AceroCatalogo>? _porClave;

    /// <summary>De dónde se leyó, para poder decírselo al usuario.</summary>
    public static string Origen { get; private set; } = string.Empty;

    /// <summary>
    /// Cinco aceros de arranque, por si el CSV no aparece.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son los cinco que la pestaña traía escritos a mano antes de que hubiera catálogo, con
    /// su Fy y su Fu del manual. <b>Su disponibilidad va vacía a propósito</b>: la semilla
    /// existe para que el desplegable no arranque vacío, no para opinar sobre qué se
    /// consigue. Con el diccionario vacío, <see cref="AceroCatalogo.DisponibleEn"/> contesta
    /// «verificar» a todo, que es la verdad cuando el catálogo se perdió.
    /// </para>
    /// </remarks>
    private static readonly AceroCatalogo[] Semilla =
    {
        new("CARBÓN", "A-36", "B-255", 2530, 250, 4080, 400,
            new Dictionary<string, string>(), AceroCatalogo.Verificar),
        new("ALTA RESISTENCIA Y BAJA ALEACIÓN", "A-572-Gr. 50", "B-284-Gr. 50",
            3515, 345, 4570, 450,
            new Dictionary<string, string>(), AceroCatalogo.Verificar),
        new("ALTA RESISTENCIA Y BAJA ALEACIÓN", "A-992", "-", 3515, 345, 4570, 450,
            new Dictionary<string, string>(), AceroCatalogo.Verificar),
        new("CARBÓN", "A-500-Gr. B'", "B-199-Gr. B'", 3235, 315, 4080, 400,
            new Dictionary<string, string>(), AceroCatalogo.Verificar),
        new("CARBÓN", "A-53-Gr. B", "B-177-Gr. B", 2460, 240, 4220, 415,
            new Dictionary<string, string>(), AceroCatalogo.Verificar)
    };

    /// <summary>Todos los aceros del catálogo, en el orden del manual.</summary>
    /// <remarks>
    /// <b>No se ordena por nombre</b>, por lo mismo que los perfiles: el manual los trae
    /// agrupados —los al carbón, los de alta resistencia, los resistentes a corrosión y los
    /// templados— y dentro de cada grupo por resistencia creciente, que es como se eligen.
    /// </remarks>
    public static IReadOnlyList<AceroCatalogo> Todos => _aceros ??= Cargar();

    /// <summary>Las designaciones, que son lo que se ve en el desplegable.</summary>
    public static string[] Nombres => Todos.Select(a => a.Astm).ToArray();

    /// <summary>Vuelve a leer el archivo, por si se editó con el programa abierto.</summary>
    public static void Recargar()
    {
        _aceros = Cargar();
        _porClave = null;
    }

    /// <summary>
    /// El acero con esa designación, o <c>null</c> si no está en el catálogo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La comparación no es literal, y tiene que no serlo.</b> El mismo acero se escribe
    /// «A-572 GR. 50», «A-572-Gr. 50» o «A572 Gr50» según quién lo teclee, y los tres son el
    /// mismo. Así que se comparan solo las letras y los dígitos, en mayúsculas.
    /// </para>
    /// <para>
    /// <b>El apóstrofo sí cuenta.</b> En esta hoja distingue dos aceros de verdad: el
    /// <c>A-500-Gr. B</c> es el tubo redondo, con Fy 2955, y el <c>A-500-Gr. B'</c> es el
    /// rectangular, con Fy 3235. Es la misma norma con dos Fy según la forma del tubo —42 y
    /// 46 ksi—, así que perder el apóstrofo da un Fy equivocado en un 9 %.
    /// </para>
    /// </remarks>
    public static AceroCatalogo? Buscar(string? designacion)
    {
        var clave = Clave(designacion);

        if (clave.Length == 0)
        {
            return null;
        }

        _porClave ??= Todos
            .GroupBy(a => Clave(a.Astm))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return _porClave.TryGetValue(clave, out var acero) ? acero : null;
    }

    /// <summary>La designación tal como la escribe el catálogo, si el acero está en él.</summary>
    /// <remarks>
    /// Sirve para arreglar una fila vieja: un proyecto guardado con «A-572 GR. 50» se lee
    /// igual, pero la celda guarda la designación del catálogo, así que el desplegable la
    /// muestra marcada en lugar de salir en blanco como si el acero no existiera.
    /// </remarks>
    public static string ComoEnElCatalogo(string? designacion) =>
        Buscar(designacion)?.Astm ?? (designacion ?? string.Empty).Trim();

    /// <summary>Solo letras, dígitos y apóstrofos, en mayúsculas.</summary>
    private static string Clave(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(texto.Length);

        foreach (var c in texto.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static List<AceroCatalogo> Cargar()
    {
        foreach (var ruta in Rutas())
        {
            try
            {
                if (!File.Exists(ruta))
                {
                    continue;
                }

                var leidos = Leer(File.ReadAllLines(ruta));

                if (leidos.Count > 0)
                {
                    Origen = ruta;
                    return leidos;
                }
            }
            catch (Exception)
            {
                // Un catálogo ilegible no puede impedir que el programa abra: se sigue
                // buscando y, si no hay ninguno bueno, queda la semilla.
            }
        }

        Origen = $"semilla interna ({Semilla.Length} aceros)";
        return Semilla.ToList();
    }

    private static IEnumerable<string> Rutas()
    {
        yield return Path.Combine(AppContext.BaseDirectory, Archivo);
        yield return Path.Combine(Directory.GetCurrentDirectory(), Archivo);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CadLink", Archivo);
    }

    /// <summary>
    /// Lee el CSV: grupo, designaciones, los cuatro esfuerzos y una columna por familia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>grupo;astm;nmx;fy_kgcm2;fy_mpa;fu_kgcm2;fu_mpa;IR;IS;IC;S;WT;C;CF;ZF;L;OR;OC;OS;PLACA</c>
    /// </para>
    /// <para>
    /// Tolerante por el mismo motivo que el de perfiles —lo va a editar una persona en
    /// Excel—: se saltan las líneas en blanco y las que empiezan por <c>#</c>, se acepta el
    /// punto y coma o la coma de separador y el punto o la coma de decimal, y una línea que
    /// no se entienda se salta en lugar de tumbar el catálogo.
    /// </para>
    /// <para>
    /// <b>Las columnas de familia se leen por POSICIÓN, en el orden de
    /// <see cref="FamiliaPerfil.Todas"/></b>, que es el que escribe el generador. Las que
    /// falten se quedan sin dato —o sea «verificar»— en vez de darse por buenas o por malas:
    /// un CSV recortado a mano no puede convertirse en una afirmación que nadie escribió.
    /// </para>
    /// </remarks>
    public static List<AceroCatalogo> Leer(IEnumerable<string> lineas)
    {
        var aceros = new List<AceroCatalogo>();

        foreach (var cruda in lineas)
        {
            var linea = (cruda ?? string.Empty).Trim();

            if (linea.Length == 0 || linea.StartsWith('#'))
            {
                continue;
            }

            var sep = linea.Contains(';') ? ';' : ',';
            var campos = linea.Split(sep);

            // Sin designación y sin Fy no hay acero que leer.
            if (campos.Length < 4)
            {
                continue;
            }

            var astm = campos[1].Trim();
            var fy = Numero(campos[3]);

            if (astm.Length == 0 || fy is null or <= 0)
            {
                continue;
            }

            var disp = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < FamiliaPerfil.Todas.Length; i++)
            {
                var col = 7 + i;

                if (col < campos.Length)
                {
                    disp[FamiliaPerfil.Todas[i]] = Respuesta(campos[col]);
                }
            }

            var colPlaca = 7 + FamiliaPerfil.Todas.Length;

            aceros.Add(new AceroCatalogo(
                Grupo: campos[0].Trim(),
                Astm: astm,
                Nmx: campos[2].Trim(),
                FyKgCm2: fy.Value,
                FyMpa: campos.Length > 4 ? Numero(campos[4]) : null,
                FuKgCm2: campos.Length > 5 ? Numero(campos[5]) : null,
                FuMpa: campos.Length > 6 ? Numero(campos[6]) : null,
                Disponibilidad: disp,
                Placa: colPlaca < campos.Length
                    ? Respuesta(campos[colPlaca])
                    : AceroCatalogo.Verificar));
        }

        return aceros;
    }

    /// <summary>
    /// Qué respuesta es la de esa celda: <c>SI</c>, <c>NO</c> o <c>VERIFICAR</c>.
    /// </summary>
    /// <remarks>
    /// Una celda que no se entiende cae en <c>VERIFICAR</c>, no en <c>NO</c>: es lo mismo
    /// que hace <see cref="AceroCatalogo.DisponibleEn"/> con una familia que no está, y por
    /// el mismo motivo. Lo único que se lee como «no se hace» es lo que lo dice: un
    /// <c>NO</c> o el guion del manual.
    /// </remarks>
    private static string Respuesta(string? celda)
    {
        var t = (celda ?? string.Empty).Trim().ToUpperInvariant();

        if (t is "SI" or "SÍ" or "S" or "X")
        {
            return AceroCatalogo.Si;
        }

        if (t is "NO" or "-" or "" or "--")
        {
            return AceroCatalogo.No;
        }

        return AceroCatalogo.Verificar;
    }

    private static double? Numero(string? campo)
    {
        var t = (campo ?? string.Empty).Trim();

        if (t.Length == 0 || t == "-")
        {
            return null;
        }

        // La coma decimal se cambia por punto, y así el mismo archivo sirve venga de un
        // Excel en español o en inglés. Es lo mismo que hace el catálogo de perfiles.
        t = t.Replace(',', '.');

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
