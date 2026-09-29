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

        panel.AddItem(boton);
    }
}
