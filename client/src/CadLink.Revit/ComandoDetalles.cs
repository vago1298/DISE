using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

// ============================================================================
//  LOS BOTONES DE PLACAS BASE, CIMENTACION Y SOLDADURA
//
//  Los tres funcionan igual: se ELIGEN las piezas en el modelo -antes de pulsar el boton o
//  cuando lo pide-, se escoge la fila de la tabla de CadLink, y se pone el detalle en cada una.
//  Las tablas de CadLink son de TIPOS, no de piezas colocadas: la posicion, el giro y la cota
//  salen de la pieza de Revit.
// ============================================================================

/// <summary>El boton <b>Placas base</b>: pone la placa, sus anclas, el grout y la soldadura bajo las columnas elegidas.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoPlacasBase : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de poner placas base.";
            return Result.Failed;
        }

        var columnas = DetallesEnRevit.Piezas(uidoc, "Elige las columnas que llevan la placa y pulsa Terminar.",
            BuiltInCategory.OST_StructuralColumns).OfType<FamilyInstance>().ToList();

        if (columnas.Count == 0)
        {
            return Result.Cancelled;
        }

        var archivo = DetallesEnRevit.Archivo();

        if (archivo is null)
        {
            return Result.Cancelled;
        }

        if (archivo.Placas.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "El archivo no trae placas base.\n\nEn CadLink, llena la hoja Placa Base y pulsa «Para Revit…».");
            return Result.Cancelled;
        }

        var ventana = new VentanaDetalle("CadLink - Placas base",
            $"Se pone la placa elegida bajo las {columnas.Count} columna(s): la placa con sus barrenos, "
            + "las anclas con su tuerca y su doblez, el grout y el filete de la columna todo alrededor.\n\n"
            + "La placa se orienta sola, midiendo la columna. Si queda girada, marca la casilla.");

        var lista = ventana.Lista("Placa:", archivo.Placas.Select(Describir));
        var girar = ventana.Casilla("Girar la placa 90° sobre la columna", false);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Poner placas") || lista.SelectedIndex < 0)
        {
            return Result.Cancelled;
        }

        var placa = archivo.Placas[lista.SelectedIndex];
        var faltan = PlacaBase.Falta(placa);

        if (faltan.Count > 0)
        {
            TaskDialog.Show("CadLink", $"A la placa {placa.Marca} le falta: {string.Join(", ", faltan)}.");
            return Result.Cancelled;
        }

        var r = Detallador.Placas(doc, columnas, placa, new OpcionesDetalle { Girar = girar.IsChecked == true });

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "columna(s) con su placa base", archivo.Avisos));

        return Result.Succeeded;
    }

    private static string Describir(PlacaBaseJson p) =>
        $"{p.Marca}  -  {p.LadoXM * 100:0.#} x {p.LadoYM * 100:0.#} cm, {p.Anclas.Count} anclas"
        + (p.Seccion.Length > 0 ? $", columna {p.Seccion}" : string.Empty);
}

/// <summary>
/// El boton <b>Cimentacion</b>: zapata aislada con dado y plantilla bajo las columnas, y zapata
/// corrida con contratrabe o enrase bajo los muros.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoCimentacion : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de poner la cimentacion.";
            return Result.Failed;
        }

        var piezas = DetallesEnRevit.Piezas(uidoc,
            "Elige las columnas (zapata aislada) y los muros (zapata corrida) y pulsa Terminar.",
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Walls);

        var columnas = piezas.OfType<FamilyInstance>().ToList();
        var muros = piezas.OfType<Wall>().ToList();

        if (columnas.Count == 0 && muros.Count == 0)
        {
            return Result.Cancelled;
        }

        var archivo = DetallesEnRevit.Archivo();

        if (archivo is null)
        {
            return Result.Cancelled;
        }

        if (columnas.Count > 0 && archivo.Zapatas.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "Elegiste columnas, pero el archivo no trae zapatas aisladas.\n\n"
                + "En CadLink, llena la hoja Zapatas Aisladas y pulsa «Para Revit…».");
            return Result.Cancelled;
        }

        if (muros.Count > 0 && archivo.Corridas.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "Elegiste muros, pero el archivo no trae zapatas corridas.\n\n"
                + "En CadLink, llena la hoja Zapatas Corridas y pulsa «Para Revit…».");
            return Result.Cancelled;
        }

        var ventana = new VentanaDetalle("CadLink - Cimentacion",
            $"Columnas elegidas: {columnas.Count}. Muros elegidos: {muros.Count}.\n\n"
            + "La zapata baja desde el arranque de la columna -o desde el grout de su placa base de "
            + "CadLink, si ya la tiene- la profundidad de desplante de la tabla. La corrida baja desde "
            + "la base del muro.");

        var listaZapata = columnas.Count > 0
            ? ventana.Lista("Zapata aislada para las columnas:", archivo.Zapatas.Select(z =>
                $"{z.Id}  -  {z.AnchoM * 100:0.#} x {z.LargoM * 100:0.#} x {z.EspesorM * 100:0.#} cm, desplante {z.ProfundidadM:0.00} m"))
            : null;

        var listaCorrida = muros.Count > 0
            ? ventana.Lista("Zapata corrida para los muros:", archivo.Corridas.Select(z =>
                $"{z.Id}  -  {z.AnchoM * 100:0.#} x {z.EspesorM * 100:0.#} cm, desplante {z.ProfundidadM:0.00} m"
                + (z.Contratrabe.Length > 0 ? $", contratrabe {z.Contratrabe}" : string.Empty)))
            : null;

        var plantilla = ventana.Numero("Plantilla de concreto pobre, en cm (0 = sin plantilla):",
            Cimentacion.PlantillaPorOmisionM * 100);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Poner cimentacion"))
        {
            return Result.Cancelled;
        }

        var o = new OpcionesDetalle
        {
            PlantillaM = VentanaDetalle.Leer(plantilla, Cimentacion.PlantillaPorOmisionM * 100) / 100
        };

        var texto = new StringBuilder();

        if (listaZapata is not null && listaZapata.SelectedIndex >= 0)
        {
            var z = archivo.Zapatas[listaZapata.SelectedIndex];
            var faltan = Cimentacion.Falta(z);

            texto.AppendLine(faltan.Count > 0
                ? $"A la zapata {z.Id} le falta: {string.Join(", ", faltan)}. No se puso."
                : DetallesEnRevit.Informe(Detallador.Zapatas(doc, columnas, z, o), "columna(s) con su zapata", null, false));
        }

        if (listaCorrida is not null && listaCorrida.SelectedIndex >= 0)
        {
            var z = archivo.Corridas[listaCorrida.SelectedIndex];
            var faltan = Cimentacion.Falta(z);

            if (texto.Length > 0)
            {
                texto.AppendLine();
            }

            texto.AppendLine(faltan.Count > 0
                ? $"A la zapata corrida {z.Id} le falta: {string.Join(", ", faltan)}. No se puso."
                : DetallesEnRevit.Informe(Detallador.Corridas(doc, muros, z, o), "muro(s) con su zapata corrida", null, false));
        }

        // Las aisladas y las corridas van en DOS transacciones: cada una se deshace con su Ctrl+Z.
        texto.AppendLine().Append(listaZapata is not null && listaCorrida is not null
            ? "Ctrl+Z deshace las zapatas corridas; un segundo Ctrl+Z, las aisladas."
            : "Un solo Ctrl+Z deshace todo lo de este boton.");

        TaskDialog.Show("CadLink", DetallesEnRevit.ConAvisos(texto.ToString().TrimEnd(), archivo.Avisos));

        return Result.Succeeded;
    }
}

/// <summary>
/// El boton <b>Soldar vigas</b>: el filete todo alrededor en los extremos de las vigas que llegan a
/// una columna.
/// </summary>
/// <remarks>
/// No necesita archivo de CadLink: el contorno sale de la viga ya modelada, sea OR, IR o cualquier
/// otro perfil, y lo unico que se pregunta es el tamaño del filete.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoSoldarVigas : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de soldar.";
            return Result.Failed;
        }

        var vigas = DetallesEnRevit.Piezas(uidoc, "Elige las vigas que se sueldan a las columnas y pulsa Terminar.",
            BuiltInCategory.OST_StructuralFraming).OfType<FamilyInstance>().ToList();

        if (vigas.Count == 0)
        {
            return Result.Cancelled;
        }

        var ventana = new VentanaDetalle("CadLink - Soldar vigas",
            $"Se pone un filete todo alrededor del perfil en los extremos de las {vigas.Count} viga(s), "
            + "sobre la cara de la columna. El contorno sale de la viga tal como esta dibujada.");

        var lado = ventana.Numero("Tamaño del filete, en mm:", Cordon.PorOmisionM * 1000);
        var soloColumnas = ventana.Casilla("Solo los extremos que llegan a una columna", true,
            "Apagado, se sueldan los dos extremos de cada viga, lleguen a donde lleguen.");

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Soldar"))
        {
            return Result.Cancelled;
        }

        var o = new OpcionesDetalle
        {
            SoldaduraM = VentanaDetalle.Leer(lado, Cordon.PorOmisionM * 1000) / 1000,
            SoloEnColumnas = soloColumnas.IsChecked == true
        };

        if (o.SoldaduraM <= 0)
        {
            TaskDialog.Show("CadLink", "El filete tiene que medir mas de cero.");
            return Result.Cancelled;
        }

        var columnas = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .ToList();

        var r = Detallador.Soldaduras(doc, vigas, columnas, o);

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "viga(s) soldada(s)", null));

        return Result.Succeeded;
    }
}

/// <summary>Lo comun a los tres botones: elegir las piezas, abrir el archivo y el informe.</summary>
internal static class DetallesEnRevit
{
    /// <summary>
    /// Las piezas ya elegidas en Revit de esas categorias; si no hay ninguna, se piden.
    /// </summary>
    public static List<Element> Piezas(UIDocument uidoc, string indicacion, params BuiltInCategory[] categorias)
    {
        var doc = uidoc.Document;

        var ya = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<Element>()
            .Where(e => EsDe(e, categorias))
            .ToList();

        if (ya.Count > 0)
        {
            return ya;
        }

        try
        {
            return uidoc.Selection
                .PickObjects(ObjectType.Element, new Filtro(categorias), indicacion)
                .Select(r => doc.GetElement(r))
                .OfType<Element>()
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return new List<Element>();
        }
    }

    private static bool EsDe(Element e, BuiltInCategory[] categorias) =>
        e.Category is { } c && categorias.Contains(c.BuiltInCategory);

    private sealed class Filtro : ISelectionFilter
    {
        private readonly BuiltInCategory[] _categorias;

        public Filtro(BuiltInCategory[] categorias) => _categorias = categorias;

        public bool AllowElement(Element elem) => EsDe(elem, _categorias);

        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    /// <summary>El <c>.cadlink-detalles.json</c>, o null si se cancela o no se puede leer.</summary>
    public static ArchivoDetallesJson? Archivo()
    {
        var abrir = new OpenFileDialog
        {
            Title = "Abrir las placas y cimentaciones de CadLink",
            Filter = ArchivoDetalles.Filtro + "|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (abrir.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            return ArchivoDetalles.Leer(abrir.FileName);
        }
        catch (Exception e)
        {
            TaskDialog.Show("CadLink", "No se pudo leer el archivo.\n\n" + e.Message);
            return null;
        }
    }

    /// <summary>
    /// El informe: cuantas se hicieron y, agrupados por motivo, los errores y los avisos.
    /// </summary>
    public static string Informe(
        ResultadoDetalles r, string que, IReadOnlyList<string>? avisosDelArchivo, bool conDeshacer = true)
    {
        var sb = new StringBuilder();

        sb.Append(r.Hechas).Append(' ').Append(que).Append('.');

        if (r.Reemplazadas > 0)
        {
            sb.Append(' ').Append(r.Reemplazadas).Append(" ya tenia(n) uno de CadLink y se cambio.");
        }

        if (r.Errores.Count > 0)
        {
            sb.AppendLine().AppendLine().Append(Agrupador.Texto(r.Errores, 6, "No se pudieron hacer"));
        }

        if (r.Avisos.Count > 0)
        {
            sb.AppendLine().AppendLine().Append(Agrupador.Texto(r.Avisos, 6, "Hay algo que decir de"));
        }

        if (r.Hechas > 0 && conDeshacer)
        {
            sb.AppendLine().AppendLine().Append("Un solo Ctrl+Z deshace todo lo de este boton.");
        }

        return ConAvisos(sb.ToString(), avisosDelArchivo);
    }

    public static string ConAvisos(string texto, IReadOnlyList<string>? avisos) =>
        avisos is null || avisos.Count == 0
            ? texto
            : texto + "\n\nDesde CadLink:\n  - " + string.Join("\n  - ", avisos.Take(8))
              + (avisos.Count > 8 ? $"\n  ... y {avisos.Count - 8} mas." : string.Empty);
}
