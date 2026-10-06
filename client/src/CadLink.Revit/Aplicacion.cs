using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace CadLink.Revit;

/// <summary>Pone el boton de CadLink en la cinta de Revit.</summary>
public sealed class Aplicacion : IExternalApplication
{
    /// <summary>La pestana de la cinta.</summary>
    public const string Pestana = "CadLink";

    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            CrearCinta(app);
        }
        catch (Exception e)
        {
            // Si esto revienta, Revit ensena un cuadro de complemento roto y lo DESACTIVA.
            // Se avisa con algo que se entienda en vez de dejar que salga la excepcion cruda.
            TaskDialog.Show("CadLink",
                "No se pudo poner el boton de CadLink en la cinta.\n\n" + e.Message);

            return Result.Failed;
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    private static void CrearCinta(UIControlledApplication app)
    {
        try
        {
            app.CreateRibbonTab(Pestana);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe, porque otro complemento de la casa la creo antes. No es un error:
            // se sigue y se anade el panel a la que hay.
        }

        var panel = app.CreateRibbonPanel(Pestana, "Modelo de calculo");

        // La ruta de ESTA dll, que es lo que Revit necesita para encontrar el comando.
        var dll = typeof(Aplicacion).Assembly.Location;

        var boton = new PushButtonData(
            "CadLinkImportarModelo",
            "Importar\nmodelo",
            dll,
            typeof(ComandoImportar).FullName)
        {
            ToolTip = "Importa el modelo estructural exportado por CadLink desde ETABS o "
                      + "SAP2000.",

            LongDescription =
                "Lee el archivo .cadlink-modelo.json que escribe CadLink, revisa que familias "
                + "hay cargadas en este proyecto, y pregunta que familia y tipo le toca a cada "
                + "seccion del modelo de calculo.\n\n"
                + "Despues crea columnas, trabes, diagonales, muros y losas nativas de Revit, "
                + "cada una en su nivel.\n\n"
                + "La eleccion se guarda: la siguiente vez no hay que volver a mapear."
        };

        // Sin imagen, el boton sale EN BLANCO con el texto solo debajo. Revit no se queja:
        // simplemente se ve mal y no se distingue de los botones de los demas complementos.
        //
        // Pide las dos medidas y usa una u otra segun como dibuje el boton: la de 32 en el
        // boton grande del panel, que es el caso normal, y la de 16 cuando lo apila o lo
        // mete en un desplegable. Dandole solo la grande, Revit la reduce al vuelo y a 16 px
        // queda una mancha.
        boton.LargeImage = Imagen("CadLink.Revit.importar-32.png");
        boton.Image = Imagen("CadLink.Revit.importar-16.png");

        panel.AddItem(boton);

        // ---- El armado ----
        //
        // Un boton aparte para poder REHACER el armado -despues de cambiar una seccion en
        // CadLink- sin volver a pasar por el cuadro de mapeo. Lleva por ahora el icono del de
        // importar.
        var armar = new PushButtonData(
            "CadLinkArmar",
            "Armar",
            dll,
            typeof(ComandoArmar).FullName)
        {
            ToolTip = "Pone el armado de CadLink -varillas, bastones y estribos- en las trabes y "
                      + "columnas ya modeladas.",

            LongDescription =
                "Lee el mismo archivo .cadlink-modelo.json, que trae el armado de la tabla de "
                + "secciones de concreto, y crea varillas nativas de Revit en cada pieza que "
                + "importo CadLink: corridas y laterales con sus ganchos, bastones con su "
                + "longitud real y estribos por zonas.\n\n"
                + "Volver a armar rehace el armado de CadLink; el puesto a mano no se toca."
        };

        armar.LargeImage = Imagen("CadLink.Revit.importar-32.png");
        armar.Image = Imagen("CadLink.Revit.importar-16.png");

        panel.AddItem(armar);

        // ---- El armado POR TIPO ----
        //
        // Para cualquier proyecto, no solo los modelados desde ETABS: un renglon por tipo de
        // columna y de trabe, se le elige la seccion de CadLink y se arman todas sus piezas.
        var porTipo = new PushButtonData(
            "CadLinkArmarPorTipo",
            "Armar\npor tipo",
            dll,
            typeof(ComandoArmarPorTipo).FullName)
        {
            ToolTip = "Arma de un jalon todas las columnas y trabes de cada tipo con una seccion "
                      + "de la tabla de CadLink.",

            LongDescription =
                "Lee el .cadlink-armado.json que escribe CadLink con el boton «Armado para "
                + "Revit» de la hoja de secciones de concreto, y ensena un renglon por cada TIPO "
                + "de columna y de trabe del proyecto, con cuantas piezas tiene.\n\n"
                + "A cada tipo se le elige su seccion -se sugiere la que se llama o mide igual- y "
                + "al pulsar Armar se ponen varillas nativas en todas sus piezas: corridas, "
                + "laterales, bastones y estribos por zonas con la longitud real de cada una.\n\n"
                + "Volver a armar rehace el armado de CadLink; el puesto a mano no se toca."
        };

        porTipo.LargeImage = Imagen("CadLink.Revit.importar-32.png");
        porTipo.Image = Imagen("CadLink.Revit.importar-16.png");

        panel.AddItem(porTipo);
    }

    /// <summary>Carga un icono embebido en esta DLL.</summary>
    /// <remarks>
    /// <para>
    /// Se lee del ENSAMBLADO, no de un archivo ni por una URI <c>pack://</c>. Las URI pack
    /// dependen del sistema de recursos de WPF, que dentro de Revit no siempre esta
    /// inicializado como una aplicacion WPF normal; leer el flujo del ensamblado funciona
    /// siempre.
    /// </para>
    /// <para>
    /// <b>El <c>CacheOption</c> es imprescindible.</b> Por omision un <c>BitmapImage</c> lee
    /// el flujo de forma diferida, y aqui el flujo se cierra al salir del <c>using</c>: la
    /// imagen se quedaria sin datos y el boton saldria en blanco, que es justo lo que se
    /// quiere arreglar. Con <c>OnLoad</c> se lee entero antes de cerrarlo.
    /// </para>
    /// <para>
    /// Si el icono no se puede cargar se devuelve <c>null</c> y el boton sale sin imagen,
    /// como antes. Un icono no es motivo para que el complemento no arranque.
    /// </para>
    /// </remarks>
    private static ImageSource? Imagen(string nombre)
    {
        try
        {
            using var flujo = typeof(Aplicacion).Assembly.GetManifestResourceStream(nombre);

            if (flujo is null)
            {
                return null;
            }

            var imagen = new BitmapImage();

            imagen.BeginInit();
            imagen.StreamSource = flujo;
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.EndInit();

            // Congelarla permite usarla desde cualquier hilo y evita que WPF la vuelva a
            // leer. En la cinta de Revit, que no es el hilo de una aplicacion WPF propia,
            // una imagen sin congelar puede dar problemas de afinidad de hilo.
            imagen.Freeze();

            return imagen;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
