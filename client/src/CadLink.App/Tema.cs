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
        ["WindowBrush"] = "#FFF2F5F9",
        ["SurfaceBrush"] = "#FFFFFFFF",
        ["CardBrush"] = "#FFFAFCFE",
        ["TotalesBrush"] = "#FFEEF3F9",

        ["TabStripBrush"] = "#FFE8EEF5",
        ["TabInactiveBrush"] = "#FFF5F8FB",
        ["TabHoverBrush"] = "#FFE0E8F1",

        ["BorderBrush"] = "#FFD7DFE9",
        ["TextBrush"] = "#FF11202D",
        ["MutedTextBrush"] = "#FF64748B",
        ["DisabledTextBrush"] = "#FFA7B3C0",
        ["ToolbarPressedBrush"] = "#FFD5DFEA",

        ["SelectionBrush"] = "#FFD6E8F7",
        ["FocoBrush"] = "#FF15679F",
        ["SombraBrush"] = "#FF0A2F4C",

        ["BrandBrush"] = "#FF15679F",
        ["BrandDarkBrush"] = "#FF0A2F4C",
        ["AccentBrush"] = "#FFF2A32C",

        ["DangerBrush"] = "#FFC0392B",
        ["WarningBrush"] = "#FFB07908",
        ["SuccessBrush"] = "#FF1B7A33",

        ["GridRowBrush"] = "#FFFFFFFF",
        ["GridAltRowBrush"] = "#FFF8FBFD",
        ["GridLineBrush"] = "#FFE6EDF4",
        ["HeaderBrush"] = "#FFE5ECF4",

        ["NoticeFondoBrush"] = "#FFFFF6DC",
        ["NoticeBordeBrush"] = "#FFE6C97A",
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
    ///     <b>La cuadrícula va en un gris INTERMEDIO</b>, no en negro. El marco es
    ///     negro y las celdas conservan sus pasteles claros, así que si las filas
    ///     fueran también negras el salto sería durísimo justo donde está la vista.
    ///     Un gris a media altura amortigua ese contraste sin apagar los colores de
    ///     las columnas.
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
        ["WindowBrush"] = "#FF0B0F14",
        ["SurfaceBrush"] = "#FF121820",
        ["CardBrush"] = "#FF19212B",
        ["TotalesBrush"] = "#FF121820",

        ["TabStripBrush"] = "#FF0E141B",
        ["TabInactiveBrush"] = "#FF19212B",
        ["TabHoverBrush"] = "#FF243040",

        ["BorderBrush"] = "#FF334155",
        ["TextBrush"] = "#FFE6EDF5",
        ["MutedTextBrush"] = "#FF94A6B8",
        ["DisabledTextBrush"] = "#FF5A6B7C",
        ["ToolbarPressedBrush"] = "#FF2A3745",

        ["SelectionBrush"] = "#FF2C4A63",
        ["FocoBrush"] = "#FF3B9BE0",
        ["SombraBrush"] = "#FF000000",

        ["BrandBrush"] = "#FF3B9BE0",
        ["BrandDarkBrush"] = "#FF8FC5EC",
        ["AccentBrush"] = "#FFF5B133",

        ["DangerBrush"] = "#FFE45A4A",
        ["WarningBrush"] = "#FFE0A82E",
        ["SuccessBrush"] = "#FF3FB56B",

        ["GridRowBrush"] = "#FF4A4A4A",
        ["GridAltRowBrush"] = "#FF525252",
        ["GridLineBrush"] = "#FF6A6A6A",
        ["HeaderBrush"] = "#FF3C3C3C",

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
            var color = (Color)ColorConverter.ConvertFromString(hex);

            // Camino 1: mutar la brocha que ya está. Repinta en el sitio, y funciona
            // igual con StaticResource que con DynamicResource.
            if (recursos[clave] is SolidColorBrush brocha && !brocha.IsFrozen)
            {
                brocha.Color = color;
                continue;
            }

            // Camino 2: la brocha está CONGELADA, así que mutarla lanzaría excepción.
            //
            // Y esto pasa de verdad: WPF congela los Freezable de un ResourceDictionary
            // cargado de BAML cuando puede, y entonces el camino 1 no hacía nada y el
            // tema «no aplicaba» sin dar ningún error. Era exactamente el síntoma que se
            // reportó. Aquí se SUSTITUYE el recurso, que es lo que obliga a que las
            // referencias sean DynamicResource: una StaticResource ya resuelta no se
            // enteraría del cambio.
            recursos[clave] = new SolidColorBrush(color);
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
