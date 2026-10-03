using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CadLink.App;

/// <summary>
/// Carga el logo de la empresa.
/// </summary>
/// <remarks>
/// <para>
/// Busca primero la ruta indicada en <c>cadlink.config.json</c>. Si no está o no
/// existe el archivo, usa el logo embebido en el ejecutable. Así se puede apuntar
/// al logo real de la empresa <b>sin recompilar</b> y sin que la aplicación
/// dependa de que ese archivo siga existiendo.
/// </para>
/// <para>
/// Acepta <c>.ico</c>, <c>.png</c>, <c>.jpg</c> y <c>.bmp</c>. Con un <c>.ico</c>
/// se elige a propósito el <b>fotograma más grande</b>: un icono suele traer
/// varias resoluciones y, si se deja al decodificador elegir, muchas veces toma la
/// de 16x16 y el logo se ve pixelado al ampliarlo.
/// </para>
/// </remarks>
public static class Branding
{
    private static ImageSource? _logo;

    /// <summary>Logo listo para usar. Se carga una sola vez.</summary>
    public static ImageSource Logo => _logo ??= Cargar();

    private static ImageSource? _icono;

    /// <summary>
    /// El <b>icono</b> de las ventanas -barra de título y barra de tareas-. NO es el logo.
    /// </summary>
    /// <remarks>
    /// Antes las ventanas usaban el logo, así que con el logo de la empresa en la configuración la
    /// barra de tareas enseñaba ese logo aunque el ejecutable tuviera el perfil I. Ahora el icono es
    /// el de la clave <c>icono</c> si se puso, y si no, el mismo que va incrustado en el ejecutable
    /// -<c>Assets\app.ico</c>, el perfil I-. El logo se queda para la pantalla de inicio.
    /// </remarks>
    public static ImageSource Icono => _icono ??= CargarIcono();

    private static ImageSource CargarIcono()
    {
        var ruta = AppInfo.Config.Icono;

        if (!string.IsNullOrWhiteSpace(ruta) && DesdeArchivo(ruta.Trim()) is { } propio)
        {
            return propio;
        }

        try
        {
            var decodificador = BitmapDecoder.Create(
                new Uri("pack://application:,,,/Assets/app.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // De un icono, el fotograma de 32 px o el más cercano: es el que mejor se ve en la
            // barra de título; Windows toma los demás tamaños del propio ejecutable.
            var mejor = decodificador.Frames
                .OrderBy(f => Math.Abs(f.PixelWidth - 32))
                .First();

            if (mejor.CanFreeze)
            {
                mejor.Freeze();
            }

            return mejor;
        }
        catch (Exception ex) when (ex is IOException
                                      or UriFormatException
                                      or NotSupportedException
                                      or ArgumentException
                                      or InvalidOperationException)
        {
            // Sin icono incrustado, la ventana usa el logo, como antes.
            return Logo;
        }
    }

    /// <summary>Ruta que se acabó usando, para mostrarla en diagnósticos.</summary>
    public static string Origen { get; private set; } = "(embebido)";

    private static ImageSource Cargar()
    {
        var ruta = AppInfo.Config.Logo;

        if (!string.IsNullOrWhiteSpace(ruta))
        {
            var propia = DesdeArchivo(ruta.Trim());
            if (propia is not null)
            {
                Origen = ruta.Trim();
                return propia;
            }
        }

        Origen = "(embebido)";
        var embebido = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        embebido.Freeze();
        return embebido;
    }

    private static ImageSource? DesdeArchivo(string ruta)
    {
        try
        {
            if (!File.Exists(ruta))
            {
                return null;
            }

            var uri = new Uri(Path.GetFullPath(ruta));

            if (Path.GetExtension(ruta).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                return DesdeIcono(uri);
            }

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = uri;
            bi.CacheOption = BitmapCacheOption.OnLoad;   // no deja el archivo bloqueado
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex) when (ex is IOException
                                      or UriFormatException
                                      or NotSupportedException
                                      or ArgumentException
                                      or UnauthorizedAccessException)
        {
            // Logo ilegible: se cae al embebido en lugar de impedir el arranque.
            return null;
        }
    }

    /// <summary>Del .ico se toma el fotograma de mayor resolución.</summary>
    private static ImageSource DesdeIcono(Uri uri)
    {
        var decodificador = BitmapDecoder.Create(
            uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        BitmapFrame? mejor = null;
        foreach (var frame in decodificador.Frames)
        {
            if (mejor is null || frame.PixelWidth > mejor.PixelWidth)
            {
                mejor = frame;
            }
        }

        var elegido = mejor ?? decodificador.Frames[0];
        if (elegido.CanFreeze)
        {
            elegido.Freeze();
        }

        return elegido;
    }
}
