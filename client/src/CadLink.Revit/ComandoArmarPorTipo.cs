using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

/// <summary>
/// El boton <b>Armar por tipo</b>: lista los tipos de columna y trabe del proyecto y arma
/// todas las piezas de cada tipo con la seccion de CadLink que se le asigne.
/// </summary>
/// <remarks>
/// <para>
/// En Revit el armado se pone eligiendo una pieza ya dibujada; con cien trabes iguales eso es
/// elegir cien veces. Aqui se elige una vez por tipo.
/// </para>
/// <para>
/// Las secciones llegan en el <c>.cadlink-armado.json</c> que escribe CadLink con el boton
/// «Armado para Revit» de la hoja de secciones de concreto. No hace falta haber modelado
/// desde ETABS: sirve en cualquier proyecto con columnas y trabes de concreto.
/// </para>
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoArmarPorTipo : IExternalCommand
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

        // ---- 1. Las secciones de CadLink ----
        var abrir = new OpenFileDialog
        {
            Title = "Abrir las secciones de CadLink (boton «Armado para Revit» de la hoja de concreto)",
            Filter = ArchivoArmado.Filtro + "|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (abrir.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        ArchivoArmadoJson archivo;

        try
        {
            archivo = ArchivoArmado.Leer(abrir.FileName);
        }
        catch (Exception e)
        {
            TaskDialog.Show("CadLink", "No se pudo leer el archivo de secciones.\n\n" + e.Message);
            return Result.Failed;
        }

        if (!archivo.Armados.Any(a => a.TieneReceta))
        {
            TaskDialog.Show("CadLink",
                "El archivo no trae secciones que se puedan armar.\n\n"
                + "En CadLink, hoja Secciones Concreto, pulsa «Armado para Revit» para escribirlo: "
                + "van las trabes, contratrabes, columnas y dados rectangulares.");
            return Result.Cancelled;
        }

        // ---- 2. Los tipos del proyecto ----
        var tipos = Armador.TiposArmables(doc, out var piezas);

        if (tipos.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "Este proyecto no tiene columnas estructurales ni trabes (armazon estructural) "
                + "dibujadas. Dibujalas y vuelve a abrir este boton.");
            return Result.Cancelled;
        }

        // ---- 3. El cuadro ----
        var vista = new VistaArmadoPorTipo(tipos, archivo.Armados);
        var ventana = new VentanaArmadoPorTipo(vista);

        try
        {
            var asaDeRevit = commandData.Application?.MainWindowHandle;

            if (asaDeRevit is not null)
            {
                new WindowInteropHelper(ventana).Owner = asaDeRevit.Value;
            }
        }
        catch (Exception)
        {
            // Cosmetico: sin dueno la ventana se ensena igual.
        }

        if (ventana.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        // ---- 4. Armar ----
        var trabajo = vista.AArmar
            .Select(f => new Armador.TrabajoPorTipo(
                f.Tipo.Nombre, piezas[f.Tipo.Id], f.Armado!))
            .ToList();

        var total = trabajo.Sum(w => w.Piezas.Count);
        var r = Armador.EjecutarPorTipo(doc, trabajo);

        TaskDialog.Show("CadLink", ComandoArmar.Informe(r, total));

        return Result.Succeeded;
    }
}
