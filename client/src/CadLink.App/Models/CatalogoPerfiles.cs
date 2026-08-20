using System.Globalization;

namespace CadLink.App.Models;

/// <summary>Un perfil del catálogo, con sus medidas en centímetros.</summary>
/// <param name="Familia">Una de las doce de <see cref="FamiliaPerfil.Todas"/>.</param>
/// <param name="Nombre">Nombre de catálogo, tal como lo designa el IMCA.</param>
/// <param name="PeralteCm">Peralte. En OC y OS es el diámetro; en la L, el ala larga.</param>
/// <param name="AnchoCm">Ancho de patín; cara del tubo; ala corta del ángulo.</param>
/// <param name="EspesorAlmaCm">Espesor del alma; de pared en los tubos; de lámina en frío.</param>
/// <param name="EspesorPatinCm">Espesor del patín. Solo I, te y canal laminada.</param>
/// <param name="LabioCm">Largo del labio. Solo la canal con labios.</param>
/// <param name="RadioCm">Radio de doblez. La canal con labios y la zeta.</param>
/// <param name="AnchoMenorCm">El patín angosto. Solo la zeta.</param>
public sealed record PerfilCatalogo(
    string Familia,
    string Nombre,
    double PeralteCm,
    double AnchoCm,
    double EspesorAlmaCm,
    double EspesorPatinCm,
    double LabioCm,
    double RadioCm,
    double AnchoMenorCm = 0);

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
/// abajo, que trae un perfil por familia: los justos para que la interfaz no arranque con
/// los desplegables vacíos.
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
    /// Un perfil de arranque por familia, <b>copiados del manual IMCA</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es una semilla, no un catálogo.</b> Son doce perfiles de uso corriente puestos
    /// para que los desplegables no arranquen vacíos si el CSV no aparece. Sus medidas
    /// salen del mismo archivo que genera el catálogo completo —no están puestas de
    /// memoria— pero si el programa está usando la semilla es porque el catálogo se perdió,
    /// y con doce perfiles no se dibuja un plano: lo que hay que hacer es recuperar el CSV.
    /// </para>
    /// </remarks>
    private static readonly PerfilCatalogo[] Semilla =
    {
        new(FamiliaPerfil.Ir, "W - 12'' x 30.04 lb/ft", 31.3, 16.6, 0.67, 1.12, 0, 0),
        new(FamiliaPerfil.Is, "IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm", 77.5, 22.5, 0.95, 1.27, 0, 0),
        new(FamiliaPerfil.Ic, "IC - 16 '' x 52.14 lb/ft", 39.9, 14.0, 0.64, 0.88, 0, 0),
        new(FamiliaPerfil.S, "S - 10'' x 25.4 lb/ft", 25.4, 11.8, 0.79, 1.25, 0, 0),
        new(FamiliaPerfil.Wt, "WT - 8'' x 13.0 lb/ft", 19.9, 14.0, 0.64, 0.88, 0, 0),
        new(FamiliaPerfil.C, "C - 8'' x 12.0 lb/ft", 20.3, 5.7, 0.56, 0.99, 0, 0),
        new(FamiliaPerfil.Cf, "CF - 6\" x 2\" x #14", 15.24, 5.08, 0.19, 0, 1.52, 0.24),
        new(FamiliaPerfil.Zf, "ZF - 8\" x 2 3/8\" x #14", 20.32, 6.03, 0.19, 0, 0, 0.476, 5.4),
        new(FamiliaPerfil.L, "L - 3'' x 1/4''", 7.62, 7.62, 0.635, 0, 0, 0),
        new(FamiliaPerfil.Or, "HSS - 6\" x 1/4\"", 15.2, 15.2, 0.64, 0, 0, 0),
        new(FamiliaPerfil.Oc, "PIPE - 4.02 in x 0.19 in", 10.2, 0, 0.48, 0, 0, 0),
        new(FamiliaPerfil.Os, "OS - 3/4\"", 1.91, 0, 0, 0, 0, 0)
    };

    /// <summary>Todos los perfiles del catálogo.</summary>
    public static IReadOnlyList<PerfilCatalogo> Todos => _perfiles ??= Cargar();

    /// <summary>Los nombres de los perfiles de una familia, en el orden del catálogo.</summary>
    /// <remarks>
    /// <para>
    /// <b>No se ordena alfabéticamente, y eso es a propósito.</b> El manual trae los
    /// perfiles de cada familia por peralte creciente y, dentro de cada peralte, por peso,
    /// que es como se busca: primero se sabe el peralte que cabe y luego se sube de peso
    /// hasta que resista. Ordenar por texto pone la de 10" entre la de 1" y la de 12",
    /// porque «1» va antes que «2», y deja la lista inservible para buscar.
    /// </para>
    /// </remarks>
    public static string[] NombresDe(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return Todos
            .Where(p => p.Familia == f)
            .Select(p => p.Nombre)
            .ToArray();
    }

    /// <summary>Cuántos perfiles hay de cada familia, para el renglón de totales.</summary>
    public static int CuantosDe(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return Todos.Count(p => p.Familia == f);
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

        Origen = $"semilla interna ({Semilla.Length} perfiles, uno por familia)";
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
    /// Lee el CSV: <c>familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio;ancho2</c>.
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
    /// La novena columna, <c>ancho2</c>, se agregó para el patín angosto de la zeta y <b>es
    /// opcional</b>: un CSV de ocho columnas de los de antes se sigue leyendo igual, con el
    /// ancho 2 en cero, que es lo que quiere decir «zeta simétrica».
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
                Numero(campos, 7),
                Numero(campos, 8)));
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
