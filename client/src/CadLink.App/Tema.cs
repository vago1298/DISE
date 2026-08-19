using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace CadLink.App;

/// <summary>
/// El <b>tema</b> de la ventana: claro u oscuro, y cómo se cambia en caliente.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué se mutan los colores y no se cambia el diccionario.</b> Lo natural
/// parecería tener dos <c>ResourceDictionary</c>, <c>Claro.xaml</c> y
/// <c>Oscuro.xaml</c>, e intercambiarlos en <c>App.Current.Resources</c>. No funciona
/// aquí: los 221 usos de la paleta son <c>StaticResource</c>, y un
/// <c>StaticResource</c> se resuelve <b>una sola vez</b>, al cargar el árbol visual.
/// Con la ventana ya abierta, sustituir el diccionario no repinta nada, porque cada
/// control se quedó con la referencia a la brocha vieja.
/// </para>
/// <para>
/// La alternativa sería pasar esos 221 usos a <c>DynamicResource</c>. Es mucho más
/// invasivo y además rompería las comprobaciones que exigen
/// <c>CellStyle="{StaticResource Celda…}"</c> y
/// <c>Background="{StaticResource PreviewFondoBrush}"</c> literales.
/// </para>
/// <para>
/// Lo que sí funciona, y es lo que se hace: <b>mutar el <c>Color</c> de las brochas</b>.
/// Las <c>SolidColorBrush</c> declaradas en XAML no están <c>Frozen</c>, así que
/// cambiarles el color repinta en el sitio todos los controles que la usan, sin tocar
/// ni una referencia y sin recargar la ventana. La paleta es la misma en los dos temas;
/// lo único que cambia son sus valores.
/// </para>
/// <para>
/// <b>El lienzo de la vista previa se queda CLARO en los dos temas</b>, y no es un
/// olvido. El dibujo que va encima se pinta desde código con dos docenas de brochas
/// oscuras —el azul del concreto, el negro de los contornos, el gris de las cotas— y
/// sobre fondo oscuro desaparecería. Además es la convención en CAD: tinta oscura sobre
/// papel claro. Así que <c>PreviewFondoBrush</c> vale lo mismo en los dos temas, y el
/// dibujo se sigue leyendo igual. Si algún día se quiere una previa oscura, hay que
/// invertir <b>también</b> la tinta, que es un trabajo aparte.
/// </para>
/// </remarks>
public static class Tema
{
    /// <summary>Si está puesto el tema oscuro.</summary>
    public static bool Oscuro { get; private set; }

    /// <summary>
    /// Colores del tema <b>claro</b>. Son los que el programa tuvo siempre.
    /// </summary>
    /// <remarks>
    /// Están aquí y no solo en el XAML porque hay que poder <b>volver</b> a ellos: una
    /// vez que se muta la brocha, el valor original del XAML se pierde.
    /// </remarks>
    private static readonly Dictionary<string, string> Claro = new()
    {
        ["WindowBrush"] = "#FFF7F9FB",
        ["SurfaceBrush"] = "#FFFFFFFF",
        ["CardBrush"] = "#FFF3F6F9",
        ["TotalesBrush"] = "#FFEDF3F8",

        ["TabStripBrush"] = "#FFE9EDF1",
        ["TabInactiveBrush"] = "#FFF3F5F7",
        ["TabHoverBrush"] = "#FFE1E7EC",

        ["BorderBrush"] = "#FFC9D2DA",
        ["TextBrush"] = "#FF1F2933",
        ["MutedTextBrush"] = "#FF6B7A88",
        ["DisabledTextBrush"] = "#FFB6C2CC",
        ["ToolbarPressedBrush"] = "#FFD2DAE1",

        ["BrandBrush"] = "#FF1776BF",
        ["BrandDarkBrush"] = "#FF0B3D6B",
        ["AccentBrush"] = "#FFFFC72C",

        ["DangerBrush"] = "#FFC0392B",
        ["WarningBrush"] = "#FFB8860B",
        ["SuccessBrush"] = "#FF1E7E34",

        ["GridRowBrush"] = "#FFFFFFFF",
        ["GridAltRowBrush"] = "#FFF8FAFB",
        ["GridLineBrush"] = "#FFE3E8ED",
        ["HeaderBrush"] = "#FFD6DEE6",

        ["NoticeFondoBrush"] = "#FFFFF4CE",
        ["NoticeBordeBrush"] = "#FFE0C36A",
        ["NoticeTextoBrush"] = "#FF7A5B00",

    };

    /// <summary>
    /// Colores del tema <b>oscuro</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No es el claro invertido: invertir deja los azules de marca chillones y los
    /// pasteles de las celdas sucios. Se eligieron a mano con tres reglas:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>Los grises suben en escalones.</b> Ventana más oscura que las superficies,
    ///     y las superficies más oscuras que las tarjetas, para que la jerarquía se lea
    ///     igual que en claro.
    ///   </item>
    ///   <item>
    ///     <b>El azul de marca se ACLARA.</b> <c>BrandDarkBrush</c> se usa como color de
    ///     <i>texto</i> en los encabezados de la hoja y en el botón de guardar; dejarlo
    ///     azul oscuro lo haría invisible. En oscuro pasa a ser el tono claro.
    ///   </item>
    ///   <item>
    ///     <b>Los nueve colores de celda de la hoja NO se tocan.</b> No están en esta
    ///     tabla a propósito, así que en oscuro conservan sus pasteles. Es lo que pidió
    ///     el usuario: <i>«deja los colores de las columnas de las secciones
    ///     estructurales de concreto»</i>. Y tiene sentido: esos tonos son la única cosa
    ///     que separa los 27 grupos de columnas al capturar, y son la referencia que ya
    ///     tiene memorizada de la hoja de Excel. Cambiarlos con el tema obligaría a
    ///     reaprenderlos.
    ///   </item>
    /// </list>
    /// <para>
    /// La consecuencia es que la <b>cuadrícula de captura se queda clara</b> aunque el
    /// resto de la ventana esté oscuro, y eso es intencionado: es la superficie de
    /// trabajo, con sus colores de referencia y su texto en tinta oscura. El marco, las
    /// barras, las pestañas, las tarjetas y los paneles sí se oscurecen.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> Noche = new()
    {
        ["WindowBrush"] = "#FF000000",
        ["SurfaceBrush"] = "#FF0D0D0D",
        ["CardBrush"] = "#FF161616",
        ["TotalesBrush"] = "#FF0D0D0D",

        ["TabStripBrush"] = "#FF0A0A0A",
        ["TabInactiveBrush"] = "#FF161616",
        ["TabHoverBrush"] = "#FF242424",

        ["BorderBrush"] = "#FF3A3A3A",
        ["TextBrush"] = "#FFE6EBF0",
        ["MutedTextBrush"] = "#FF95A3B0",
        ["DisabledTextBrush"] = "#FF5A6672",
        ["ToolbarPressedBrush"] = "#FF2A2A2A",

        ["BrandBrush"] = "#FF3B9BE0",

        // Se ACLARA: es color de TEXTO en los encabezados y en el boton de guardar.
        ["BrandDarkBrush"] = "#FF8FC5EC",
        ["AccentBrush"] = "#FFFFC72C",

        ["DangerBrush"] = "#FFE45A4A",
        ["WarningBrush"] = "#FFE0A82E",
        ["SuccessBrush"] = "#FF3FB56B",

        ["GridRowBrush"] = "#FF0D0D0D",
        ["GridAltRowBrush"] = "#FF141414",
        ["GridLineBrush"] = "#FF2E2E2E",
        ["HeaderBrush"] = "#FF1C1C1C",

        ["NoticeFondoBrush"] = "#FF3A3320",
        ["NoticeBordeBrush"] = "#FF7A6A32",
        ["NoticeTextoBrush"] = "#FFF0DFA0",

    };

    /// <summary>Pone el tema pedido, repintando en caliente.</summary>
    public static void Aplicar(bool oscuro)
    {
        var paleta = oscuro ? Noche : Claro;

        var recursos = Application.Current?.Resources;

        if (recursos is null)
        {
            return;
        }

        foreach (var (clave, hex) in paleta)
        {
            // Si la brocha no existe se salta en silencio: puede pasar mientras se
            // reorganiza el XAML, y una ventana con un color viejo es mejor que una
            // excepcion al arrancar.
            if (recursos[clave] is not SolidColorBrush brocha)
            {
                continue;
            }

            if (brocha.IsFrozen)
            {
                // No deberia pasar con las brochas del XAML, pero si alguien las
                // congela, mutarlas lanza excepcion.
                continue;
            }

            var color = (Color)ColorConverter.ConvertFromString(hex);
            brocha.Color = color;
        }

        Oscuro = oscuro;
    }

    /// <summary>Cambia al otro tema y lo recuerda.</summary>
    public static void Alternar()
    {
        Aplicar(!Oscuro);
        Guardar();
    }

    /// <summary>Texto para el botón: dice a qué tema se va, no en cuál se está.</summary>
    public static string TextoDelBoton => Oscuro ? "Tema claro" : "Tema oscuro";

    // ==================================================================
    //  Recordar la elección
    // ==================================================================
    //
    // Va en %LOCALAPPDATA%, imitando a CadLink.Licensing.LicenseCache, y NO en el
    // .clk del proyecto: el tema es del usuario y de su maquina, no del trabajo. Si
    // viviera en el proyecto, abrir el archivo de un compañero te cambiaria el tema, y
    // habria que subir la version del formato.

    private const string Carpeta = "CadLink";
    private const string Archivo = "preferencias.json";

    private sealed class Preferencias
    {
        public bool TemaOscuro { get; set; }
    }

    private static string Ruta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Carpeta, Archivo);

    /// <summary>
    /// Lee la preferencia y la aplica. Se llama al arrancar.
    /// </summary>
    /// <remarks>
    /// Cualquier fallo se traga a propósito: si el archivo no está, está corrupto o no
    /// hay permiso, el programa arranca en claro. Quedarse sin abrir por no poder leer
    /// una preferencia de color sería absurdo.
    /// </remarks>
    public static void Cargar()
    {
        var oscuro = false;

        try
        {
            if (File.Exists(Ruta))
            {
                var p = JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(Ruta));
                oscuro = p?.TemaOscuro ?? false;
            }
        }
        catch (Exception)
        {
            oscuro = false;
        }

        Aplicar(oscuro);
    }

    /// <summary>Guarda la preferencia. Si no se puede, no pasa nada.</summary>
    private static void Guardar()
    {
        try
        {
            var dir = Path.GetDirectoryName(Ruta);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(
                Ruta,
                JsonSerializer.Serialize(new Preferencias { TemaOscuro = Oscuro }));
        }
        catch (Exception)
        {
            // El tema ya está puesto en esta sesión; solo no se recordará.
        }
    }
}
