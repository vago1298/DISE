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
public class ComandoArmarPorTipo : IExternalCommand
{
    /// <summary>Solo los tipos de este elemento -los botones «Acero» de Columnas y de Vigas-, o todos.</summary>
    protected virtual ClasePieza? Solo => null;

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

            // Para que «Corte de seccion» use las mismas secciones sin volver a preguntar.
            UltimoArchivoArmado.Guardar(abrir.FileName);
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

        // ---- 2. Las secciones de CadLink que Revit no tiene: se ofrecen crear ----
        CrearLasQueFaltan(doc, archivo, Solo);

        // ---- 3. Los tipos del proyecto ----
        var tipos = Armador.TiposArmables(doc, out var piezas)
            .Where(t => Solo is null || t.Clase == Solo)
            .ToList();

        if (tipos.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "Este proyecto no tiene columnas estructurales ni trabes (armazon estructural) "
                + "dibujadas. Dibujalas y vuelve a abrir este boton.");
            return Result.Cancelled;
        }

        // ---- 4. El cuadro ----
        // Los tipos de armadura del proyecto: la tabla de abajo pregunta cual va en cada uso,
        // porque una oficina maneja varios por diametro (TRABES, COLUMNAS, BASTON, ESTRIBOS...).
        var vista = new VistaArmadoPorTipo(
            tipos, archivo.Armados, Armador.TiposDeVarilla(doc), ArchivoVarillas.Leer());
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

        // ---- 5. Armar ----
        var trabajo = vista.AArmar
            .Select(f => new Armador.TrabajoPorTipo(
                f.Tipo.Nombre, piezas[f.Tipo.Id], f.Armado!))
            .ToList();

        var total = trabajo.Sum(w => w.Piezas.Count);
        ArchivoVarillas.Guardar(vista.Elecciones());

        var r = Armador.EjecutarPorTipo(
            doc, trabajo, vista.IdDeVarilla, vista.EscribirPropiedades, vista.CrearDespiece);

        TaskDialog.Show("CadLink", ComandoArmar.Informe(r, total));

        return Result.Succeeded;
    }

    /// <summary>
    /// Si CadLink tiene secciones -una trabe de 50x90, por ejemplo- que no existen como tipo en
    /// Revit, pregunta si se crean. Se crean duplicando un tipo de la familia de concreto.
    /// </summary>
    private static void CrearLasQueFaltan(Document doc, ArchivoArmadoJson archivo, ClasePieza? solo)
    {
        List<TipoPorCrear> faltan;

        try
        {
            faltan = TiposNuevos.Faltantes(archivo.Armados, CreadorDeTipos.Existentes(doc))
                .Where(f => solo is null || f.Clase == solo)
                .ToList();
        }
        catch (Exception)
        {
            return;
        }

        if (faltan.Count == 0)
        {
            return;
        }

        var lista = string.Join("\n", faltan.Take(15).Select(f => "  • " + f.Texto))
                    + (faltan.Count > 15 ? $"\n  … y {faltan.Count - 15} más" : string.Empty);

        var pregunta = new TaskDialog("CadLink")
        {
            MainInstruction = $"{faltan.Count} sección(es) de CadLink no tienen tipo en Revit",
            MainContent = lista + "\n\n¿Las creo? Se duplica un tipo de tu familia de concreto con la base y el "
                          + "peralte de cada una, y se le escriben sus propiedades de tipo.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes
        };

        if (pregunta.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        var r = new ResultadoArmado();
        var creados = CreadorDeTipos.Crear(doc, faltan, r);

        var texto = $"Tipos creados: {creados.Count}\n" + string.Join("\n", creados.Select(c => "  • " + c));

        if (r.Errores.Count > 0)
        {
            texto += "\n\n" + Agrupador.Texto(r.Errores);
        }

        if (r.Avisos.Count > 0)
        {
            texto += "\n\n" + Agrupador.Texto(r.Avisos, 6, "Hay algo que decir de");
        }

        texto += "\n\nYa están en el proyecto para dibujar con ellos. Los que no tienen piezas no salen "
                 + "en la tabla de armado hasta que dibujes alguna.";

        TaskDialog.Show("CadLink", texto);
    }
}

/// <summary>El boton «Acero» del panel Columnas: «Armar por tipo» solo con las columnas.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoAceroColumnas : ComandoArmarPorTipo
{
    protected override ClasePieza? Solo => ClasePieza.Columna;
}

/// <summary>El boton «Acero» del panel Vigas: «Armar por tipo» solo con las trabes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoAceroVigas : ComandoArmarPorTipo
{
    protected override ClasePieza? Solo => ClasePieza.Trabe;
}
