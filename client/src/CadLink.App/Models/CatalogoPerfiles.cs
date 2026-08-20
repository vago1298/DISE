using System.Globalization;

namespace CadLink.App.Models;

/// <summary>Un perfil del catálogo, con sus medidas en centímetros.</summary>
/// <param name="Familia">IR, OR, OC o CF.</param>
/// <param name="Nombre">Nombre de catálogo, tal como se escribe: <c>W12X30</c>.</param>
/// <param name="PeralteCm">Peralte. En el OC es el diámetro exterior.</param>
/// <param name="AnchoCm">Ancho de patín en IR y CF, de la cara en OR. El OC no lo usa.</param>
/// <param name="EspesorAlmaCm">Espesor del alma en el IR, de la pared en los demás.</param>
/// <param name="EspesorPatinCm">Espesor del patín. Solo el IR.</param>
/// <param name="LabioCm">Largo del labio. Solo el CF.</param>
/// <param name="RadioCm">Radio de doblez. Solo el CF.</param>
public sealed record PerfilCatalogo(
    string Familia,
    string Nombre,
    double PeralteCm,
    double AnchoCm,
    double EspesorAlmaCm,
    double EspesorPatinCm,
    double LabioCm,
    double RadioCm);

/// <summary>
/// El <b>catálogo de perfiles</b>: la lista de secciones de cada familia, con sus medidas.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que las dimensiones <b>no se tecleen a mano</b>. Capturar a mano el peralte,
/// el ancho y dos espesores de cada perfil es cuatro oportunidades de equivocarse por fila,
/// y un espesor de patín mal escrito no se ve en el dibujo: sale un perfil creíble con la
/// medida equivocada, y eso llega a obra.
/// </para>
/// <para>
/// <b>El catálogo es un archivo de datos, no código.</b> Se lee de
/// <c>perfiles-acero.csv</c>, así que se puede crecer sin recompilar nada: se exporta la
/// hoja de perfiles a CSV, se deja el archivo junto al ejecutable y el desplegable se llena
/// solo. Un catálogo dentro del programa obligaría a pedir una versión nueva por cada perfil
/// que falte.
/// </para>
/// <para>
/// Se busca en tres sitios, en este orden: junto al ejecutable, en la carpeta de trabajo y
/// en <c>%LOCALAPPDATA%\CadLink</c>. Si no aparece en ninguno, se usa la <b>semilla</b> de
/// abajo, que trae solo cuatro perfiles: los justos para que la interfaz no arranque con los
/// desplegables vacíos.
/// </para>
/// </remarks>
public static class CatalogoPerfiles
{
    /// <summary>Nombre del archivo de catálogo.</summary>
    public const string Archivo = "perfiles-acero.csv";

    private static List<PerfilCatalogo>? _perfiles;

    /// <summary>De dónde se leyó el catálogo, para poder decírselo al usuario.</summary>
    public static string Origen { get; private set; } = string.Empty;

    /// <summary>
    /// Los cuatro perfiles de arranque, uno por familia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es una semilla, no un catálogo.</b> Son cuatro perfiles de uso corriente puestos
    /// para que los desplegables no arranquen vacíos, y sus medidas <b>hay que cotejarlas
    /// contra la tabla de perfiles del proyecto</b> antes de fiarse de ellas. No se ponen
    /// cien de memoria a propósito: un peralte inventado dibuja un perfil creíble con la
    /// medida equivocada, y eso es peor que no tener catálogo.
    /// </para>
    /// </remarks>
    private static readonly PerfilCatalogo[] Semilla =
    {
        new(FamiliaPerfil.Ir, "W12X30", 31.3, 16.5, 0.66, 1.11, 0, 0),
        new(FamiliaPerfil.Or, "HSS6X6X1/4", 15.24, 15.24, 0.635, 0, 0, 0),
        new(FamiliaPerfil.Oc, "PIPE 4 STD", 11.43, 0, 0.602, 0, 0, 0),
        new(FamiliaPerfil.Cf, "CF 6X2 #14", 15.0, 5.0, 0.19, 0, 1.5, 0.4)
    };

    /// <summary>Todos los perfiles del catálogo.</summary>
    public static IReadOnlyList<PerfilCatalogo> Todos => _perfiles ??= Cargar();

    /// <summary>Los nombres de los perfiles de una familia, en orden alfabético.</summary>
    /// <remarks>
    /// Va con una entrada vacía al principio: el perfil se puede dejar en blanco, y sobre
    /// todo se puede escribir uno que no esté en el catálogo. La lista sugiere, no obliga.
    /// </remarks>
    public static string[] NombresDe(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return Todos
            .Where(p => p.Familia == f)
            .Select(p => p.Nombre)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>El perfil del catálogo con ese nombre, o <c>null</c> si no está.</summary>
    public static PerfilCatalogo? Buscar(string? familia, string? nombre)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();
        var n = (nombre ?? string.Empty).Trim();

        if (n.Length == 0)
        {
            return null;
        }

        return Todos.FirstOrDefault(
            p => p.Familia == f && string.Equals(p.Nombre, n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Vuelve a leer el archivo, por si se editó con el programa abierto.</summary>
    public static void Recargar() => _perfiles = Cargar();

    private static List<PerfilCatalogo> Cargar()
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

        Origen = "semilla interna (cuatro perfiles, revisa sus medidas)";
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
    /// Lee el CSV: <c>familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tolerante a propósito, porque el archivo lo va a hacer una persona exportando de
    /// Excel: se saltan las líneas en blanco y las que empiezan por <c>#</c>, se acepta el
    /// punto y coma o la coma como separador, y el punto o la coma como decimal. Una línea
    /// que no se entienda se <b>salta</b> en lugar de tumbar el catálogo entero: es mejor un
    /// catálogo con un perfil de menos que un programa que no abre.
    /// </para>
    /// <para>
    /// La familia se normaliza con <see cref="FamiliaPerfil.DelNombre"/> si la columna viene
    /// vacía, así que una lista que solo traiga nombres y medidas también sirve.
    /// </para>
    /// </remarks>
    public static List<PerfilCatalogo> Leer(IEnumerable<string> lineas)
    {
        var perfiles = new List<PerfilCatalogo>();

        foreach (var cruda in lineas)
        {
            var linea = (cruda ?? string.Empty).Trim();

            if (linea.Length == 0 || linea.StartsWith('#'))
            {
                continue;
            }

            // El separador: punto y coma si lo hay, y si no, coma. Se decide por línea
            // porque un CSV exportado con coma decimal usa punto y coma de separador.
            var sep = linea.Contains(';') ? ';' : ',';
            var campos = linea.Split(sep);

            if (campos.Length < 3)
            {
                continue;
            }

            var familia = campos[0].Trim().ToUpperInvariant();
            var nombre = campos[1].Trim();

            if (nombre.Length == 0)
            {
                continue;
            }

            if (!FamiliaPerfil.Todas.Contains(familia))
            {
                // Sin familia válida se intenta deducir del nombre, que es lo que hace la
                // propia cuadrícula al capturar.
                familia = FamiliaPerfil.DelNombre(nombre) ?? string.Empty;

                if (familia.Length == 0)
                {
                    continue;
                }
            }

            // La cabecera de un CSV exportado cae aquí: su tercer campo no es un número.
            if (Numero(campos, 2) <= 0)
            {
                continue;
            }

            perfiles.Add(new PerfilCatalogo(
                familia,
                nombre,
                Numero(campos, 2),
                Numero(campos, 3),
                Numero(campos, 4),
                Numero(campos, 5),
                Numero(campos, 6),
                Numero(campos, 7)));
        }

        return perfiles;
    }

    private static double Numero(string[] campos, int i)
    {
        if (i >= campos.Length)
        {
            return 0;
        }

        var texto = campos[i].Trim().Replace(',', '.');

        return double.TryParse(
            texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }
}
