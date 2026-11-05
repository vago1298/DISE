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

        var elegidas = DetallesEnRevit.Piezas(uidoc, "Elige las columnas de acero que llevan la placa y pulsa Terminar.",
            BuiltInCategory.OST_StructuralColumns).OfType<FamilyInstance>().ToList();

        // Los dados modelados como columna de concreto se quedan fuera: la placa es de la columna
        // de acero que se apoya en ellos. Elegir con ventana se los lleva, y antes salia una
        // segunda placa en el fondo del dado.
        var columnas = elegidas.Where(c => !Detallador.EsDeConcreto(c)).ToList();

        if (columnas.Count == 0)
        {
            if (elegidas.Count > 0)
            {
                TaskDialog.Show("CadLink", "Las columnas elegidas son de concreto. La placa base va en la columna de acero.");
            }

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
        var posicion = ventana.Lista("Donde va la placa:", new[]
        {
            "Sobre el dado o el muro de concreto, con su grout",
            "Embebida: su cara de arriba a paño del arranque de la columna"
        });
        var subir = ventana.Casilla("Subir el arranque de la columna a la cara de la placa", true,
            "Solo con la placa sobre el apoyo. Apagado, la columna se queda donde esta y atraviesa la placa.");
        var soldar = ventana.Casilla("Soldar la columna a la placa, todo alrededor", true);
        var contra = ventana.Casilla("Con contraplaca al fondo de las anclas", false,
            "Una segunda placa ahogada en el dado, al fondo de las anclas, con su tuerca abajo. Con ella las anclas van rectas.");
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

        var r = Detallador.Placas(doc, columnas, placa, new OpcionesDetalle
        {
            Girar = girar.IsChecked == true,
            SobreElApoyo = posicion.SelectedIndex != 1,
            SubirColumna = subir.IsChecked == true,
            SoldarColumna = soldar.IsChecked == true,
            Contraplaca = contra.IsChecked == true
        });

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
/// El boton <b>Conectar vigas</b>: en cada extremo que llega a una columna, el filete todo
/// alrededor o la conexion a momento; en el que llega a un muro de concreto, la placa embebida.
/// </summary>
/// <remarks>
/// No necesita archivo de CadLink: el contorno sale de la viga ya modelada. Primero se pregunta
/// como, y despues se eligen las vigas: con «un extremo», se da clic en cada viga CERCA del extremo
/// que se conecta.
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
            message = "Abre un proyecto de Revit antes de conectar vigas.";
            return Result.Failed;
        }

        var medidas = new OpcionesConexion();

        var ventana = new VentanaDetalle("CadLink - Conectar vigas",
            "En cada extremo de viga que llega a una COLUMNA se pone la conexion elegida; en el que "
            + "llega a un MURO de concreto, una placa embebida a paño del muro con sus anclas, y la "
            + "viga soldada a ella. El contorno sale de la viga tal como esta dibujada.");

        var tipo = ventana.Lista("Conexion a la columna:", new[]
        {
            "Filete todo alrededor del perfil",
            "A momento, IR a IR: placas en los patines y placa de cortante atornillada"
        });
        var extremos = ventana.Lista("Extremos:", new[]
        {
            "Los dos extremos de cada viga",
            "Uno: das clic en cada viga cerca del extremo; se conecta con cada clic y Esc termina"
        });
        var lado = ventana.Numero("Tamaño del filete, en mm:", Cordon.PorOmisionM * 1000);
        var patin = ventana.Numero("A momento: espesor de las placas de los patines, en mm:", medidas.PlacaPatinEspesorM * 1000);
        var largoPatin = ventana.Numero("A momento: largo de las placas de los patines, en cm:", medidas.PlacaPatinLargoM * 100);
        var cortante = ventana.Numero("A momento: espesor de la placa de cortante, en mm:", medidas.PlacaCortanteEspesorM * 1000);
        var tornillo = ventana.Numero("A momento: diametro de los tornillos, en mm:", medidas.TornilloM * 1000);
        var enMuro = ventana.Casilla("En los muros: placa embebida con anclas", true,
            "Apagado, la viga que llega a un muro solo se suelda todo alrededor, sin placa.");
        var soloApoyos = ventana.Casilla("Solo los extremos que llegan a una columna o a un muro", true,
            "Apagado, se conectan los extremos elegidos lleguen a donde lleguen.");

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Elegir vigas"))
        {
            return Result.Cancelled;
        }

        medidas.PlacaPatinEspesorM = VentanaDetalle.Leer(patin, medidas.PlacaPatinEspesorM * 1000) / 1000;
        medidas.PlacaPatinLargoM = VentanaDetalle.Leer(largoPatin, medidas.PlacaPatinLargoM * 100) / 100;
        medidas.PlacaCortanteEspesorM = VentanaDetalle.Leer(cortante, medidas.PlacaCortanteEspesorM * 1000) / 1000;
        medidas.TornilloM = VentanaDetalle.Leer(tornillo, medidas.TornilloM * 1000) / 1000;

        var o = new OpcionesDetalle
        {
            SoldaduraM = VentanaDetalle.Leer(lado, Cordon.PorOmisionM * 1000) / 1000,
            SoloEnApoyos = soloApoyos.IsChecked == true,
            PlacaEnMuro = enMuro.IsChecked == true,
            Conexion = tipo.SelectedIndex == 1 ? TipoConexion.Momento : TipoConexion.Filete,
            MedidasDeConexion = medidas
        };

        if (o.SoldaduraM <= 0)
        {
            TaskDialog.Show("CadLink", "El filete tiene que medir mas de cero.");
            return Result.Cancelled;
        }

        var columnas = DetallesEnRevit.Todas(doc, BuiltInCategory.OST_StructuralColumns);
        var muros = DetallesEnRevit.Todas(doc, BuiltInCategory.OST_Walls);

        ResultadoDetalles r;

        if (extremos.SelectedIndex == 1)
        {
            // UN EXTREMO: se conecta CON CADA CLIC, para que se vea al momento, y Esc termina.
            // Antes se juntaban los clics y se conectaba todo al final, al pulsar Esc; quien
            // pulsaba Enter -como en cualquier otra seleccion- se quedaba esperando sin que pasara
            // nada, porque una seleccion de un punto no termina con Enter.
            r = DetallesEnRevit.ConectarConCadaClic(uidoc, viga =>
                Detallador.Soldaduras(doc, new[] { viga }, columnas, muros, o));

            if (r.Hechas == 0 && r.Errores.Count == 0 && r.Avisos.Count == 0)
            {
                return Result.Cancelled;
            }
        }
        else
        {
            var vigas = DetallesEnRevit.Piezas(uidoc, "Elige las vigas que se conectan y pulsa Terminar (o Enter).",
                    BuiltInCategory.OST_StructuralFraming)
                .OfType<FamilyInstance>()
                .Select(v => new Detallador.VigaElegida(v, null))
                .ToList();

            if (vigas.Count == 0)
            {
                return Result.Cancelled;
            }

            r = Detallador.Soldaduras(doc, vigas, columnas, muros, o);
        }

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "viga(s) conectada(s)", null,
            extremos.SelectedIndex != 1));

        return Result.Succeeded;
    }
}

/// <summary>
/// El boton <b>Soldar columnas</b>: el filete todo alrededor de la base o la cabeza de cada
/// columna, sobre lo que tenga: una placa, una losa, un dado o la columna de abajo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoSoldarColumnas : IExternalCommand
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

        var columnas = DetallesEnRevit.Piezas(uidoc, "Elige las columnas que se sueldan y pulsa Terminar.",
            BuiltInCategory.OST_StructuralColumns).OfType<FamilyInstance>().ToList();

        if (columnas.Count == 0)
        {
            return Result.Cancelled;
        }

        var ventana = new VentanaDetalle("CadLink - Soldar columnas",
            $"Se pone un filete todo alrededor del perfil de las {columnas.Count} columna(s), sobre la "
            + "pieza en que se apoya o que tiene encima. El contorno sale de la columna tal como esta "
            + "dibujada.\n\nLa placa base de CadLink ya trae su soldadura: este boton es para lo demas.");

        var donde = ventana.Lista("Donde:", new[] { "En la base", "En la cabeza", "En la base y en la cabeza" });
        var lado = ventana.Numero("Tamaño del filete, en mm:", Cordon.PorOmisionM * 1000);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Soldar"))
        {
            return Result.Cancelled;
        }

        var o = new OpcionesDetalle
        {
            SoldaduraM = VentanaDetalle.Leer(lado, Cordon.PorOmisionM * 1000) / 1000,
            ExtremosColumna = donde.SelectedIndex switch
            {
                1 => ExtremosColumna.Cabeza,
                2 => ExtremosColumna.Ambos,
                _ => ExtremosColumna.Base
            }
        };

        if (o.SoldaduraM <= 0)
        {
            TaskDialog.Show("CadLink", "El filete tiene que medir mas de cero.");
            return Result.Cancelled;
        }

        var r = Detallador.SoldarColumnas(doc, columnas, o);

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "columna(s) soldada(s)", null));

        return Result.Succeeded;
    }
}

/// <summary>Lo comun a los botones: elegir las piezas, abrir el archivo y el informe.</summary>
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

    /// <summary>
    /// Clic en cada viga cerca del extremo que se conecta, uno por uno, y se conecta al momento.
    /// Esc termina. Devuelve lo de todos los clics junto.
    /// </summary>
    /// <remarks>
    /// Se elige con un PUNTO sobre la viga, no la viga entera: asi Revit devuelve donde se dio el
    /// clic, y de ahi sale el extremo. Una misma viga se puede picar dos veces para sus dos lados.
    /// Cada clic es su propia transaccion: Ctrl+Z deshace el ultimo.
    /// </remarks>
    public static ResultadoDetalles ConectarConCadaClic(
        UIDocument uidoc, Func<Detallador.VigaElegida, ResultadoDetalles> conectar)
    {
        var total = new ResultadoDetalles();
        var filtro = new Filtro(new[] { BuiltInCategory.OST_StructuralFraming });

        while (true)
        {
            Reference r;

            try
            {
                r = uidoc.Selection.PickObject(ObjectType.PointOnElement, filtro,
                    $"Clic en la viga cerca del extremo que se conecta ({total.Hechas} hechas). Esc para terminar.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return total;
            }

            if (uidoc.Document.GetElement(r) is not FamilyInstance viga || r.GlobalPoint is not { } punto)
            {
                continue;
            }

            var uno = conectar(new Detallador.VigaElegida(viga, punto));

            total.Hechas += uno.Hechas;
            total.Reemplazadas += uno.Reemplazadas;
            total.Avisos.AddRange(uno.Avisos);
            total.Errores.AddRange(uno.Errores);
        }
    }

    /// <summary>Todas las piezas de una categoria del proyecto.</summary>
    public static List<Element> Todas(Document doc, BuiltInCategory categoria) =>
        new FilteredElementCollector(doc).OfCategory(categoria).WhereElementIsNotElementType().ToList();

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
