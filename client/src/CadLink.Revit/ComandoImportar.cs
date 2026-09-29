using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

/// <summary>
/// El comando del boton: importa el modelo de CadLink a este proyecto de Revit.
/// </summary>
/// <remarks>
/// <c>TransactionMode.Manual</c> porque la transaccion la abre <see cref="Modelador"/> cuando
/// toca, y no antes: primero hay que leer el documento y ensenar el cuadro de mapeo, y eso no
/// debe ocurrir con una transaccion abierta.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoImportar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData?.Application?.ActiveUIDocument;

        if (uiDoc?.Document is null)
        {
            message = "Abre un proyecto de Revit antes de importar.";

            return Result.Failed;
        }

        var doc = uiDoc.Document;

        if (doc.IsFamilyDocument)
        {
            TaskDialog.Show("CadLink",
                "Esto se importa en un PROYECTO, no dentro del editor de familias.");

            return Result.Cancelled;
        }

        // ---- 1. El archivo del modelo ----
        var abrir = new OpenFileDialog
        {
            Title = "Abrir el modelo exportado por CadLink",
            Filter = ArchivoModelo.Filtro + "|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (abrir.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        ModeloJson modelo;

        try
        {
            modelo = ArchivoModelo.Leer(abrir.FileName);
        }
        catch (Exception e)
        {
            // Se dice el motivo. "No se pudo leer" no deja hacer nada.
            TaskDialog.Show("CadLink", "No se pudo leer el modelo.\n\n" + e.Message);

            return Result.Failed;
        }

        if (modelo.Piezas == 0)
        {
            TaskDialog.Show("CadLink", "El archivo no trae ninguna pieza que modelar.");

            return Result.Cancelled;
        }

        // ---- 2. Que hay en ESTE proyecto de Revit ----
        var catalogo = LectorDeCatalogo.Leer(doc);

        if (catalogo.Tipos.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "En este proyecto no hay ninguna familia estructural cargada, asi que no hay "
                + "nada con lo que modelar.\n\n"
                + "Carga al menos una familia de pilares y una de vigas, o empieza de una "
                + "plantilla estructural, y vuelve a intentarlo.");

            return Result.Cancelled;
        }

        // ---- 3. El mapeo, con lo elegido la vez anterior si lo hay ----
        var rutaMapeo = ArchivoMapeo.RutaPara(abrir.FileName);
        Mapeo guardado;

        try
        {
            guardado = ArchivoMapeo.LeerOVacio(rutaMapeo);
        }
        catch (Exception e)
        {
            // Un mapeo ilegible no debe impedir importar: se avisa y se sigue sin el.
            TaskDialog.Show("CadLink",
                "Habia un mapeo guardado pero no se pudo leer, asi que se empieza de cero.\n\n"
                + e.Message);

            guardado = new Mapeo();
        }

        var vista = new VistaMapeo(modelo, catalogo, guardado);

        // ---- 4. El cuadro ----
        var ventana = new VentanaMapeo(vista);

        // Sin dueno, la ventana se puede quedar DETRAS de Revit y parece que el comando se
        // colgo. Se le pone la ventana principal de Revit como dueno.
        try
        {
            new WindowInteropHelper(ventana).Owner = commandData.Application.MainWindowHandle;
        }
        catch (Exception)
        {
            // Si no se puede, se ensena igual: es cosmetico.
        }

        if (ventana.ShowDialog() != true || ventana.Eleccion is null)
        {
            return Result.Cancelled;
        }

        var modo = ventana.Eleccion.Value;
        var mapeo = vista.AMapeo();

        // ---- 5. Guardar el mapeo para la proxima ----
        try
        {
            ArchivoMapeo.Guardar(mapeo, rutaMapeo);
        }
        catch (Exception e)
        {
            // No poder guardar el mapeo no debe abortar la importacion, que es lo que el
            // usuario vino a hacer.
            TaskDialog.Show("CadLink",
                "No se pudo guardar el mapeo para la proxima vez, pero la importacion sigue.\n\n"
                + e.Message);
        }

        // ---- 6. El plan y el modelado ----
        var plan = Planificador.Armar(
            modelo, mapeo, catalogo, LectorDeCatalogo.Existentes(doc), modo);

        if (!plan.HayTrabajo)
        {
            TaskDialog.Show("CadLink", "No hay nada que hacer: " + plan.Resumen() + ".");

            return Result.Succeeded;
        }

        var r = Modelador.Ejecutar(doc, plan);

        TaskDialog.Show("CadLink", Informe(modo, plan, r, rutaMapeo));

        return Result.Succeeded;
    }

    private static string Informe(Modo modo, Plan plan, ResultadoModelado r, string rutaMapeo)
    {
        var sb = new StringBuilder();

        sb.AppendLine(modo == Modo.ModelarNuevos
            ? "Se modelaron las piezas nuevas."
            : "Se actualizaron las piezas que ya estaban.");

        sb.AppendLine();
        sb.Append("Creadas: ").Append(r.Creadas).AppendLine();
        sb.Append("Actualizadas: ").Append(r.Actualizadas).AppendLine();

        if (r.NivelesCreados > 0)
        {
            sb.Append("Niveles creados: ").Append(r.NivelesCreados).AppendLine();
        }

        if (plan.SinMapeo > 0)
        {
            sb.Append("Sin tipo elegido, no modeladas: ").Append(plan.SinMapeo).AppendLine();
        }

        if (plan.Sobra > 0)
        {
            sb.AppendLine();
            sb.Append(plan.Sobra).AppendLine(" pieza(s) siguen en Revit con marca de CadLink "
                + "y ya no estan en el modelo. NO se borraron: revisalas a mano.");
        }

        if (r.Avisos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Avisos:");

            foreach (var a in r.Avisos.Take(6))
            {
                sb.Append("  - ").AppendLine(a);
            }

            if (r.Avisos.Count > 6)
            {
                sb.Append("  ... y ").Append(r.Avisos.Count - 6).AppendLine(" mas.");
            }
        }

        if (r.Errores.Count > 0)
        {
            sb.AppendLine();
            sb.Append("NO se pudieron modelar ").Append(r.Errores.Count).AppendLine(":");

            foreach (var e in r.Errores.Take(8))
            {
                sb.Append("  - ").AppendLine(e);
            }

            if (r.Errores.Count > 8)
            {
                sb.Append("  ... y ").Append(r.Errores.Count - 8).AppendLine(" mas.");
            }
        }

        sb.AppendLine();
        sb.Append("El mapeo quedo guardado en ").Append(Path.GetFileName(rutaMapeo))
          .AppendLine(", asi que la proxima vez no hay que volver a elegir.");

        sb.AppendLine();
        sb.Append("Un solo Ctrl+Z deshace toda la importacion.");

        return sb.ToString();
    }
}
