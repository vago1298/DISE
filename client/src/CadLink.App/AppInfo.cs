// System.IO va EXPLICITO a proposito: en un proyecto de WPF no forma parte de
// los using implicitos, al contrario de lo que ocurre en una libreria normal.
using System.IO;
using System.Reflection;
using System.Text.Json;
using CadLink.Licensing;

namespace CadLink.App;

/// <summary>
/// Configuración leída de <c>cadlink.config.json</c>, junto al ejecutable.
/// </summary>
/// <remarks>
/// Los valores por omisión apuntan a un servidor local, de modo que la
/// aplicación funcione recién instalada sin tocar nada.
/// </remarks>
public sealed class AppConfig
{
    public string ServidorLicencias { get; set; } = "http://localhost:8000";
    public string NombreProducto { get; set; } = "CadLink";
    /// <summary>Razón social. Vacío = no se muestra ningún nombre de empresa.</summary>
    public string NombreEmpresa { get; set; } = string.Empty;

    public string Lema { get; set; } = "Excel - ETABS - AutoCAD";
    public string CorreoSoporte { get; set; } = "soporte@miempresa.com";

    /// <summary>Ruta al logo. Vacío = usar el embebido en el ejecutable.</summary>
    public string Logo { get; set; } = string.Empty;

    /// <summary>
    /// Ruta a <c>ETABSv1.dll</c>, la librería de la API de ETABS. Se admite tanto
    /// la carpeta como el archivo.
    /// </summary>
    /// <remarks>
    /// Normalmente <b>no hace falta ponerla</b>: la librería se busca sola junto al
    /// ETABS que esté abierto. Sirve solo para instalaciones fuera de lo común.
    /// </remarks>
    public string RutaLibreriaEtabs { get; set; } = string.Empty;
}

/// <summary>
/// Datos de marca y configuración de la aplicación.
/// </summary>
/// <remarks>
/// <para>
/// Antes estos valores eran constantes del código, y cambiar la dirección del
/// servidor obligaba a <b>recompilar</b>. Eso está mal para un producto que se
/// instala en máquinas ajenas: mover el servidor a otra dirección no puede
/// requerir un compilador.
/// </para>
/// <para>
/// Ahora se leen de <c>cadlink.config.json</c>, que se copia junto al
/// ejecutable. Si el archivo falta o está corrupto, se usan los valores por
/// omisión y la aplicación sigue arrancando.
/// </para>
/// </remarks>
public static class AppInfo
{
    public const string ConfigFileName = "cadlink.config.json";

    private static readonly Lazy<AppConfig> Cargada = new(Cargar, isThreadSafe: true);

    public static AppConfig Config => Cargada.Value;

    public static string ProductName => Config.NombreProducto;

    public static string CompanyName => Config.NombreEmpresa;

    /// <summary>Ruta a la librería de ETABS indicada en la configuración.</summary>
    public static string RutaLibreriaEtabs => Config.RutaLibreriaEtabs;

    /// <summary>
    /// Ajusta la licencia para que el nombre de empresa que se muestra sea el de
    /// <c>cadlink.config.json</c>, y no el que venga en el token.
    /// </summary>
    /// <remarks>
    /// Lo que se ve en pantalla es asunto del cliente, no del servidor. Sin esto,
    /// para quitar el nombre de empresa habría que editar el <c>.env</c> del
    /// servidor y reactivar, porque el valor del <c>.env</c> pisa el del programa.
    /// Con <c>nombreEmpresa</c> vacío no se muestra ningún nombre.
    /// </remarks>
    public static LicenseInfo ConNombreDeEmpresa(LicenseInfo info) =>
        info with { Organization = CompanyName };

    public static string Tagline => Config.Lema;

    /// <summary>URL del servidor de licencias. En producción DEBE ser HTTPS.</summary>
    public static string LicenseServerUrl => Config.ServidorLicencias;

    public static string SupportEmail => Config.CorreoSoporte;

    /// <summary>Ruta del archivo de configuración, útil para mostrarla en errores.</summary>
    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static LicensingOptions CreateLicensingOptions() => new()
    {
        ServerUrl = LicenseServerUrl,
        AppVersion = Version,
        AppFolderName = ProductName
    };

    private static AppConfig Cargar()
    {
        try
        {
            var ruta = ConfigPath;
            if (!File.Exists(ruta))
            {
                return new AppConfig();
            }

            var opciones = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ruta), opciones)
                   ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException
                                      or JsonException
                                      or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            // Configuración ilegible: se sigue con los valores por omisión en
            // lugar de impedir el arranque por un archivo mal editado.
            return new AppConfig();
        }
    }
}
