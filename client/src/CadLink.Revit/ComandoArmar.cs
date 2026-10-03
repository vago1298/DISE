using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

/// <summary>
/// El boton <b>Armar</b>: pone el armado de CadLink en las trabes y columnas ya modeladas.
/// </summary>
/// <remarks>
/// Lee el MISMO archivo que <see cref="ComandoImportar"/>, el <c>.cadlink-modelo.json</c>, que
/// desde la version 4 trae el armado de la tabla de secciones. Va en un boton aparte para
/// poder rehacer el armado -por ejemplo, despues de cambiar una seccion en CadLink- sin volver
/// a pasar por el cuadro de mapeo.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoArmar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData?.Application?.ActiveUIDocument?.Document;

        if (doc is null)
        {
            message = "Abre un proyecto de Revit antes de armar.";
            return Result.Failed;
        }

        if (doc.IsFamilyDocument)
        {
            TaskDialog.Show("CadLink", "El armado se pone en un PROYECTO, no en el editor de familias.");
            return Result.Cancelled;
        }

        var abrir = new OpenFileDialog
        {
            Title = "Abrir el modelo exportado por CadLink, con su armado",
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
            TaskDialog.Show("CadLink", "No se pudo leer el modelo.\n\n" + e.Message);
            return Result.Failed;
        }

        var conArmado = modelo.Barras.Count(b => b.Armado is not null);

        if (modelo.Armados.Count == 0 || conArmado == 0)
        {
            TaskDialog.Show("CadLink",
                "El archivo no trae armado.\n\n"
                + "En CadLink, llena la tabla de secciones de concreto y vuelve a exportar a "
                + "Revit: el armado viaja en el mismo archivo. Las secciones de ETABS se "
                + "emparejan con las filas de la tabla por su nombre o, si no, por sus medidas.");
            return Result.Cancelled;
        }

        var r = Armador.Ejecutar(doc, modelo);

        // Lo que CadLink no pudo emparejar al exportar: va primero, es lo que hay que arreglar.
        r.Avisos.InsertRange(0, modelo.Avisos.Where(a => a.StartsWith("«armado", StringComparison.Ordinal)));

        TaskDialog.Show("CadLink", Informe(r, conArmado));

        return Result.Succeeded;
    }

    private static string Informe(ResultadoArmado r, int conArmado)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Armado terminado.");
        sb.AppendLine();
        sb.Append("Piezas armadas: ").Append(r.Piezas).Append(" de ").Append(conArmado)
          .AppendLine(" que traen armado en el archivo");
        sb.Append("Varillas y juegos de estribos creados: ").Append(r.Varillas).AppendLine();

        if (r.SinArmado > 0)
        {
            sb.Append("Piezas de CadLink sin armado en el archivo: ").Append(r.SinArmado).AppendLine();
        }

        if (r.TiposCreados.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Se crearon porque no estaban en el proyecto:");

            foreach (var t in r.TiposCreados)
            {
                sb.Append("  - ").AppendLine(t);
            }
        }

        if (r.Avisos.Count > 0)
        {
            sb.AppendLine();
            sb.Append(Agrupador.Texto(r.Avisos, 6, "Hay algo que decir de")).AppendLine();
        }

        if (r.Errores.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Agrupador.Texto(r.Errores));
        }

        sb.AppendLine();
        sb.AppendLine("Volver a armar rehace el armado de CadLink de cada pieza; el que pusiste a mano no se toca.");
        sb.Append("Un solo Ctrl+Z deshace todo el armado.");

        return sb.ToString();
    }
}
