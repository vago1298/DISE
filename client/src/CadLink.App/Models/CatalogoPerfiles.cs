using System.Globalization;

namespace CadLink.App.Models;

/// <summary>
/// Las <b>propiedades geométricas</b> de un perfil: con las que se diseña.
/// </summary>
/// <remarks>
/// <para>
/// Van en un tipo aparte de las medidas, y no revueltas con ellas, porque son dos cosas con
/// papeles distintos: <b>las medidas se dibujan y las propiedades solo se muestran</b>. El
/// dibujante no recibe ninguna de estas, y la fila las lleva para que se puedan leer en la
/// cuadrícula y compararlas entre perfiles al elegir.
/// </para>
/// <para>
/// <b>Todas son <c>double?</c>, y el nulo significa algo.</b> Quiere decir «el manual no da
/// esta propiedad para esta familia», que no es lo mismo que cero: el redondo macizo no trae
/// <c>Sx</c>, la canal formada en frío no trae <c>rx</c>, y las dos cosas son huecos del
/// manual, no valores. Con cero, la cuadrícula mostraría «0.00» y eso se lee como un dato.
/// Siendo nulo, la celda sale vacía, que es lo que hay que decir. Y no se calcula ninguna:
/// un <c>Ix</c> deducido de <c>rx</c> y del área sería un número que nadie firmó.
/// </para>
/// </remarks>
/// <param name="PesoKgM">Peso propio, en kg/m.</param>
/// <param name="AreaCm2">Área de la sección, en cm².</param>
/// <param name="IxCm4">Momento de inercia respecto al eje fuerte, en cm⁴.</param>
/// <param name="SxCm3">Módulo de sección elástico del eje fuerte, en cm³.</param>
/// <param name="RxCm">Radio de giro del eje fuerte, en cm.</param>
/// <param name="ZxCm3">Módulo de sección plástico del eje fuerte, en cm³.</param>
/// <param name="IyCm4">Momento de inercia del eje débil, en cm⁴.</param>
/// <param name="SyCm3">Módulo elástico del eje débil, en cm³.</param>
/// <param name="RyCm">Radio de giro del eje débil, en cm.</param>
/// <param name="ZyCm3">Módulo plástico del eje débil, en cm³.</param>
/// <param name="JCm4">Constante de torsión de Saint-Venant, en cm⁴.</param>
/// <param name="CwCm6">Constante de torsión por alabeo, en cm⁶.</param>
/// <param name="XbarCm">Distancia del centroide al paño, en cm. Canal, CF y ángulo.</param>
/// <param name="YbarCm">La misma en el otro eje, en cm. Te y ángulo.</param>
/// <param name="RminCm">Radio de giro mínimo, en cm. El del eje principal débil.</param>
/// <param name="IxyCm4">Producto de inercia, en cm⁴. Solo la zeta lo trae.</param>
public sealed record PropiedadesPerfil(
    double? PesoKgM = null,
    double? AreaCm2 = null,
    double? IxCm4 = null,
    double? SxCm3 = null,
    double? RxCm = null,
    double? ZxCm3 = null,
    double? IyCm4 = null,
    double? SyCm3 = null,
    double? RyCm = null,
    double? ZyCm3 = null,
    double? JCm4 = null,
    double? CwCm6 = null,
    double? XbarCm = null,
    double? YbarCm = null,
    double? RminCm = null,
    double? IxyCm4 = null)
{
    /// <summary>Las de un perfil capturado a mano: ninguna, porque no hay de dónde.</summary>
    public static readonly PropiedadesPerfil Ninguna = new();

    /// <summary>Cuántas de las dieciséis trae este perfil.</summary>
    /// <remarks>
    /// Sirve para poder decirle al usuario, en el renglón de totales, que su perfil
    /// capturado a mano no trae ninguna: si no, vería dieciséis celdas vacías sin saber por
    /// qué.
    /// </remarks>
    public int Cuantas =>
        new[]
        {
            PesoKgM, AreaCm2, IxCm4, SxCm3, RxCm, ZxCm3, IyCm4, SyCm3,
            RyCm, ZyCm3, JCm4, CwCm6, XbarCm, YbarCm, RminCm, IxyCm4
        }.Count(v => v is not null);
}

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
/// <param name="Propiedades">Las propiedades geométricas, para mostrar.</param>
public sealed record PerfilCatalogo(
    string Familia,
    string Nombre,
    double PeralteCm,
    double AnchoCm,
    double EspesorAlmaCm,
    double EspesorPatinCm,
    double LabioCm,
    double RadioCm,
    double AnchoMenorCm = 0,
    PropiedadesPerfil? Propiedades = null)
{
    /// <summary>Las propiedades, nunca nulas: si no hay, las vacías.</summary>
    public PropiedadesPerfil Props => Propiedades ?? PropiedadesPerfil.Ninguna;
}

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
    /// <para>
    /// <b>Van sin propiedades geométricas a propósito.</b> Son dieciséis números por perfil,
    /// y transcribir ciento noventa y dos valores a mano para un caso que solo ocurre cuando
    /// el catálogo se perdió es justo la clase de tarea en la que se cuela un dígito. Con la
    /// semilla, las columnas de propiedad salen vacías, que es lo honesto: dicen «esto no lo
    /// sé», y el renglón de totales avisa de que se está usando la semilla.
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
    /// Lee el CSV: nueve columnas de <b>medida</b> y dieciséis de <b>propiedad</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio;ancho2</c> y a continuación
    /// <c>peso;area;ix;sx;rx;zx;iy;sy;ry;zy;j;cw;xbar;ybar;rmin;ixy</c>.
    /// </para>
    /// <remarks>
    /// <para>
    /// Tolerante a propósito, porque el archivo lo va a hacer una persona exportando de
    /// Excel: se saltan las líneas en blanco y las que empiezan por <c>#</c>, se acepta el
    /// punto y coma o la coma como separador, y el punto o la coma como decimal. Una línea
    /// que no se entienda se <b>salta</b> en lugar de tumbar el catálogo entero: es mejor un
    /// catálogo con un perfil de menos que un programa que no abre.
    /// </para>
    /// <para>
    /// <b>Las columnas de más son opcionales, todas.</b> La novena, <c>ancho2</c>, se agregó
    /// para el patín angosto de la zeta, y las dieciséis de propiedades después; un CSV de
    /// ocho columnas de los primeros se sigue leyendo igual, con el ancho 2 en cero —que es
    /// lo que quiere decir «zeta simétrica»— y sin propiedades. Se lee por índice y lo que
    /// no está no está: así el archivo puede crecer sin romper los que ya existen.
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
                Numero(campos, 8),
                new PropiedadesPerfil(
                    Opcional(campos, 9),    // peso kg/m
                    Opcional(campos, 10),   // area cm2
                    Opcional(campos, 11),   // Ix cm4
                    Opcional(campos, 12),   // Sx cm3
                    Opcional(campos, 13),   // rx cm
                    Opcional(campos, 14),   // Zx cm3
                    Opcional(campos, 15),   // Iy cm4
                    Opcional(campos, 16),   // Sy cm3
                    Opcional(campos, 17),   // ry cm
                    Opcional(campos, 18),   // Zy cm3
                    Opcional(campos, 19),   // J cm4
                    Opcional(campos, 20),   // Cw cm6
                    Opcional(campos, 21),   // x barra cm
                    Opcional(campos, 22),   // y barra cm
                    Opcional(campos, 23),   // rmin cm
                    Opcional(campos, 24))));   // Ixy cm4
        }

        return perfiles;
    }

    /// <summary>Un número de una columna de MEDIDA: lo que falta vale cero.</summary>
    /// <remarks>
    /// En las medidas el cero significa «esta forma no usa esta medida» —un tubo redondo no
    /// tiene ancho— y así lo entiende la columna «Falta». Por eso aquí el hueco es cero y no
    /// nulo.
    /// </remarks>
    private static double Numero(string[] campos, int i) => Opcional(campos, i) ?? 0;

    /// <summary>Un número de una columna de PROPIEDAD: lo que falta es <c>null</c>.</summary>
    /// <remarks>
    /// En las propiedades el hueco es un hueco: quiere decir que el manual no da esa
    /// propiedad para esa familia, y eso no es cero. Devolviendo nulo, la celda de la
    /// cuadrícula sale vacía en lugar de mostrar un «0.00» que se leería como un dato.
    /// </remarks>
    private static double? Opcional(string[] campos, int i)
    {
        if (i >= campos.Length)
        {
            return null;
        }

        var texto = campos[i].Trim().Replace(',', '.');

        if (texto.Length == 0)
        {
            return null;
        }

        return double.TryParse(
            texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
