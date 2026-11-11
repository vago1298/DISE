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

        var todo = DetallesEnRevit.Piezas(uidoc,
            "Elige las columnas o vigas de ACERO, o los muros, castillos y cabezales donde se apoyan, y pulsa Terminar.",
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Columns, BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation);

        // Lo elegido puede ser el ACERO -la pieza que lleva la placa- o el CONCRETO en que se apoya:
        // un muro, un castillo, un cabezal. Del concreto se buscan las vigas y columnas de acero que
        // llegan a el; asi se pueden poner todas las placas de un muro de un solo clic.
        var elegidas = todo.OfType<FamilyInstance>().Where(Detallador.EsMetalOMadera).ToList();
        var concreto = todo.Where(e => e is not FamilyInstance fi || !Detallador.EsMetalOMadera(fi)).ToList();
        var soloEn = concreto.Count > 0 ? concreto.Select(e => e.Id).ToHashSet() : null;

        foreach (var pieza in Detallador.AceroSobre(doc, concreto))
        {
            if (elegidas.All(e => e.Id != pieza.Id))
            {
                elegidas.Add(pieza);
            }
        }

        var deAcero = elegidas.Where(c => !Detallador.EsDeConcreto(c)).ToList();

        // Las columnas tienen que ser de ACERO de verdad: un castillo con material generico no dice
        // que es de concreto, y recibia una placa base de columna en vez de servir de apoyo a la viga.
        var columnas = deAcero.Where(c => c.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns
                                          && Detallador.EsMetalOMadera(c)).ToList();
        var vigas = deAcero.Where(c => c.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFraming).ToList();

        if (columnas.Count + vigas.Count == 0)
        {
            if (todo.Count > 0)
            {
                TaskDialog.Show("CadLink",
                    "En lo elegido no se apoya ninguna viga ni columna de ACERO. La placa es de la pieza de "
                    + "acero: elige la viga o la columna, o el muro o cabezal en que se apoyan.");
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
            $"Columnas de acero: {columnas.Count}. Vigas de acero: {vigas.Count}.\n\n"
            + "En las columnas la placa va en su base. En las vigas va bajo cada extremo que se APOYA en un "
            + "muro, un cabezal, una columna de concreto o una zapata, con su cara de arriba en el patin; si "
            + "la viga llega al costado del muro, usa «Conectar vigas».\n\n"
            + "Se modela la placa con sus barrenos, "
            + "las anclas con su tuerca y su doblez, el grout y el filete de la columna todo alrededor.\n\n"
            + "La placa se orienta sola, midiendo la columna. Si queda girada, marca la casilla.");

        var lista = ventana.Lista("Placa:", archivo.Placas.Select(Describir));
        var posicion = ventana.Lista("Donde va la placa:", new[]
        {
            "Sobre el dado o el muro de concreto, con su grout",
            "Embebida: su cara de arriba a paño del arranque de la columna"
        });
        var grout = ventana.Numero("(A) Grout bajo la placa, espesor en cm (0 = sin grout):",
            GroutPorOmision(archivo.Placas[0]));
        lista.SelectionChanged += (_, _) =>
        {
            if (lista.SelectedIndex >= 0)
            {
                grout.Text = GroutPorOmision(archivo.Placas[lista.SelectedIndex]).ToString("0.##");
            }
        };
        // LA VIGA: a donde llega su placa. Se pregunta en vez de adivinarlo solo por la geometria:
        // al costado de un muro, cabezal, castillo o columna la placa va VERTICAL, embebida a paño de
        // su cara; encima de un dado o de la cimentacion va HORIZONTAL, bajo el patin.
        var llegaA = vigas.Count > 0
            ? ventana.Lista("Vigas: a donde llega la placa:", new[]
            {
                "Al muro, cabezal, castillo o columna (placa VERTICAL en su cara)",
                "Al dado o a la cimentacion (placa HORIZONTAL bajo la viga)",
                "Que lo decida CadLink en cada extremo, por la geometria"
            })
            : null;
        var subir = ventana.Casilla("Subir el arranque de la columna a la cara de la placa", true,
            "Solo con la placa sobre el apoyo. Apagado, la columna se queda donde esta y atraviesa la placa.");
        var soldar = ventana.Casilla("(C) Soldar la columna -o el patin de la viga- a la placa", true);
        var contra = ventana.Casilla("(B) Con contraplaca al fondo de las anclas", false,
            "Una segunda placa ahogada en el dado, al fondo de las anclas, con su tuerca abajo. Con ella las anclas van rectas.");
        var girar = ventana.Casilla("Girar la placa 90° sobre la columna", false);
        ventana.Guia("Guia", 255, 250, CroquisDeGuia.PlacaBase);

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

        var opciones = new OpcionesDetalle
        {
            Girar = girar.IsChecked == true,
            SobreElApoyo = posicion.SelectedIndex != 1,
            SubirColumna = subir.IsChecked == true,
            SoldarColumna = soldar.IsChecked == true,
            Contraplaca = contra.IsChecked == true,
            GroutM = VentanaDetalle.Leer(grout, GroutPorOmision(placa)) / 100
        };

        var texto = new StringBuilder();

        if (columnas.Count > 0)
        {
            texto.AppendLine(DetallesEnRevit.Informe(
                Detallador.Placas(doc, columnas, placa, opciones), "columna(s) con su placa base", null, false));
        }

        if (vigas.Count > 0)
        {
            if (texto.Length > 0)
            {
                texto.AppendLine();
            }

            // 0 = vertical en el costado, 1 = horizontal encima, 2 = las dos, que decida la geometria.
            var modo = llegaA?.SelectedIndex ?? 2;

            var horizontales = modo == 0
                ? new ResultadoDetalles()
                : Detallador.PlacasDeViga(doc, vigas, placa, opciones, soloEn);

            // LOS EXTREMOS QUE LLEGAN AL COSTADO del muro, el cabezal o la columna de concreto: placa
            // VERTICAL embebida a paño de su cara, con sus anclas hacia dentro y la viga soldada a
            // ella. Es la misma de «Conectar vigas»; los extremos que ya llevan la horizontal se saltan.
            // La placa VERTICAL va en un muro, cabezal, castillo o columna: nunca en una losa ni en una
            // zapata, que quedan encima o debajo de la viga y no enfrente de su punta.
            bool DeCostado(Element e) => e.Category?.BuiltInCategory is BuiltInCategory.OST_Walls
                or BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_Columns
                or BuiltInCategory.OST_StructuralFraming;

            var concretos = (soloEn is not null
                    ? concreto
                    : Detallador.ApoyosDeConcreto(doc).Select(a => doc.GetElement(a.Id)).OfType<Element>().ToList())
                .Where(DeCostado)
                .ToList();

            var verticales = modo == 1 ? new ResultadoDetalles() : Detallador.Soldaduras(doc,
                vigas.Select(v => new Detallador.VigaElegida(v, null)).ToList(),
                new List<Element>(), concretos,
                new OpcionesDetalle
                {
                    SoldaduraM = placa.SoldaduraM > 0 ? placa.SoldaduraM : Cordon.PorOmisionM,
                    PlacaEnMuro = true,
                    SoloEnApoyos = true,
                    ApoyoHorizontal = false
                });

            // Que una viga no llegue a ningun costado no es un aviso: puede ir encima.
            // Con «decidelo tu» sobra decir que una viga no llega a un costado; si se pidio la vertical, no.
            if (modo == 2)
            {
                verticales.Avisos.RemoveAll(x => x.Contains("No llega a una columna ni a un muro", StringComparison.Ordinal));
            }
            horizontales.Avisos.RemoveAll(x => x.Contains("Llega al costado", StringComparison.Ordinal)
                                             || x.Contains("Ningun extremo se apoya", StringComparison.Ordinal));

            if (modo != 0)
            {
                texto.AppendLine(DetallesEnRevit.Informe(horizontales, "viga(s) con placa horizontal en el dado o la cimentacion", null, false));
            }

            if (modo == 2)
            {
                texto.AppendLine();
            }

            if (modo != 1)
            {
                texto.AppendLine(DetallesEnRevit.Informe(verticales, "viga(s) con placa vertical en el muro, cabezal, castillo o columna", null, false));
            }

            // Con la respuesta del cuadro el aviso de que no encontro donde ponerla si importa.
            if (modo != 2 && horizontales.Hechas + verticales.Hechas == 0)
            {
                texto.AppendLine(modo == 0
                    ? "Ninguna viga llega al costado de un muro, cabezal, castillo o columna de concreto."
                    : "Ninguna viga queda encima de un dado o de la cimentacion: la cara de arriba del concreto tiene que quedar bajo el patin.");
            }
        }

        texto.AppendLine().Append(vigas.Count > 0
            ? "Cada grupo de placas es su propio Ctrl+Z: las verticales, las horizontales y las de columnas."
            : "Un solo Ctrl+Z deshace todo lo de este boton.");

        TaskDialog.Show("CadLink", DetallesEnRevit.ConAvisos(texto.ToString().TrimEnd(), archivo.Avisos));

        return Result.Succeeded;
    }

    /// <summary>
    /// El grout que se propone: el de la hoja; si la hoja dice que no lleva, 2.5 cm, que es lo que
    /// se construye. Se puede poner en 0.
    /// </summary>
    private static double GroutPorOmision(PlacaBaseJson p) => p.GroutM > 0 ? p.GroutM * 100 : 2.5;

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

        var plantilla = ventana.Numero("(A) Plantilla de concreto pobre, en cm (0 = sin plantilla):",
            Cimentacion.PlantillaPorOmisionM * 100);
        ventana.Guia("Guia", 230, 235, CroquisDeGuia.Cimentacion);

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
            "A momento, IR a IR: placas en los patines y placa de cortante atornillada",
            "A cortante, IR a columna: solo la placa de cortante atornillada al alma"
        });
        var extremos = ventana.Lista("Extremos:", new[]
        {
            "Los dos extremos de cada viga",
            "Uno: das clic en cada viga cerca del extremo; se conecta con cada clic y Esc termina"
        });
        var lado = ventana.Numero("Filete general (perfil soldado, placa embebida), en mm:", Cordon.PorOmisionM * 1000);
        var anchoPatin = ventana.Numero("(A) Placas de patin: ancho, en cm (0 = el del patin de la viga):", 0);
        var largoPatin = ventana.Numero("(B) Placas de patin: largo, en cm:", medidas.PlacaPatinLargoM * 100);
        var patin = ventana.Numero("(C) Placas de patin: espesor, en mm:", medidas.PlacaPatinEspesorM * 1000);
        var soldPatin = ventana.Numero("(D) Placas de patin: filete a la columna, en mm (0 = el general):", 0);
        var altoCortante = ventana.Numero("(E) Placa de cortante: alto, en cm (0 = el que cabe entre patines):", 0);
        var largoCortante = ventana.Numero("(F) Placa de cortante: largo, en cm:", medidas.PlacaCortanteLargoM * 100);
        var cortante = ventana.Numero("(G) Placa de cortante: espesor, en mm:", medidas.PlacaCortanteEspesorM * 1000);
        var soldCortante = ventana.Numero("(H) Placa de cortante: filete a la columna, en mm (0 = el general):", 0);
        var union = ventana.Lista("Placa de cortante al alma de la viga:", new[]
        {
            "Atornillada",
            "Soldada, todo alrededor de la placa (sin tornillos)"
        });
        var dosCaras = ventana.Lista("Placa de cortante en:", new[]
        {
            "Una cara del alma",
            "Las dos caras del alma"
        });
        var soldAlma = ventana.Numero("(I) Placa de cortante soldada: filete al alma, en mm (0 = el general):", 0);
        var tornillo = ventana.Numero("(J) Placa de cortante atornillada: diametro de los tornillos, en mm:", medidas.TornilloM * 1000);
        var sepTornillo = ventana.Numero("(K) Placa de cortante atornillada: separacion de los tornillos, en cm:", medidas.SeparacionTornillosM * 100);
        var enMuro = ventana.Casilla("En muros, cabezales y columnas de concreto: placa embebida con anclas", true,
            "Apagado, la viga que llega a un muro solo se suelda todo alrededor, sin placa.");
        var soloApoyos = ventana.Casilla("Solo los extremos que llegan a una columna o a un muro", true,
            "Apagado, se conectan los extremos elegidos lleguen a donde lleguen.");
        ventana.Guia("Guia: la conexion de lado", 300, 235, CroquisDeGuia.ConexionAlzado);
        ventana.Guia("Guia: corte de la viga", 170, 195, CroquisDeGuia.ConexionCorte);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Elegir vigas"))
        {
            return Result.Cancelled;
        }

        medidas.PlacaPatinEspesorM = VentanaDetalle.Leer(patin, medidas.PlacaPatinEspesorM * 1000) / 1000;
        medidas.PlacaPatinLargoM = VentanaDetalle.Leer(largoPatin, medidas.PlacaPatinLargoM * 100) / 100;
        medidas.PlacaCortanteEspesorM = VentanaDetalle.Leer(cortante, medidas.PlacaCortanteEspesorM * 1000) / 1000;
        medidas.PlacaPatinAnchoM = VentanaDetalle.Leer(anchoPatin, 0) / 100;
        medidas.SoldaduraPatinM = VentanaDetalle.Leer(soldPatin, 0) / 1000;
        medidas.PlacaCortanteAltoM = VentanaDetalle.Leer(altoCortante, 0) / 100;
        medidas.PlacaCortanteLargoM = VentanaDetalle.Leer(largoCortante, medidas.PlacaCortanteLargoM * 100) / 100;
        medidas.SoldaduraCortanteM = VentanaDetalle.Leer(soldCortante, 0) / 1000;
        medidas.CortanteSoldada = union.SelectedIndex == 1;
        medidas.CortanteDosCaras = dosCaras.SelectedIndex == 1;
        medidas.SoldaduraAlmaM = VentanaDetalle.Leer(soldAlma, 0) / 1000;
        var sep = VentanaDetalle.Leer(sepTornillo, medidas.SeparacionTornillosM * 100) / 100;
        medidas.SeparacionTornillosM = sep >= 0.03 ? sep : medidas.SeparacionTornillosM;
        medidas.TornilloM = VentanaDetalle.Leer(tornillo, medidas.TornilloM * 1000) / 1000;

        var o = new OpcionesDetalle
        {
            SoldaduraM = VentanaDetalle.Leer(lado, Cordon.PorOmisionM * 1000) / 1000,
            SoloEnApoyos = soloApoyos.IsChecked == true,
            PlacaEnMuro = enMuro.IsChecked == true,
            Conexion = tipo.SelectedIndex switch
            {
                1 => TipoConexion.Momento,
                2 => TipoConexion.Cortante,
                _ => TipoConexion.Filete
            },
            MedidasDeConexion = medidas
        };

        if (o.SoldaduraM <= 0)
        {
            TaskDialog.Show("CadLink", "El filete tiene que medir mas de cero.");
            return Result.Cancelled;
        }

        // Las columnas de ACERO reciben la conexion; el concreto -muros, columnas de concreto y
        // cabezales- recibe la placa embebida. Antes una columna de concreto contaba como de acero y
        // se le ponia la conexion a momento encima.
        var todasLasColumnas = DetallesEnRevit.Todas(doc, BuiltInCategory.OST_StructuralColumns);
        var columnas = todasLasColumnas.Where(c => c is not FamilyInstance fi || !Detallador.EsDeConcreto(fi)).ToList();
        var muros = DetallesEnRevit.Todas(doc, BuiltInCategory.OST_Walls)
            .Concat(todasLasColumnas.OfType<FamilyInstance>().Where(Detallador.EsDeConcreto))
            .Concat(DetallesEnRevit.Todas(doc, BuiltInCategory.OST_StructuralFraming)
                .OfType<FamilyInstance>().Where(Detallador.EsDeConcreto))
            .ToList();

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
        var lado = ventana.Numero("(A) Tamaño del filete, en mm:", Cordon.PorOmisionM * 1000);
        ventana.Guia("Guia", 160, 155, CroquisDeGuia.SoldarColumna);

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

/// <summary>
/// El boton <b>Encamisar trabes</b>: refuerza las trabes de concreto elegidas con placas de acero,
/// o con angulos en las esquinas y soleras.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoEncamisar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de encamisar.";
            return Result.Failed;
        }

        var elegidas = DetallesEnRevit.Piezas(uidoc, "Elige las trabes de concreto que se encamisan y pulsa Terminar.",
            BuiltInCategory.OST_StructuralFraming).OfType<FamilyInstance>().ToList();

        // Solo las de concreto: una viga de acero no se encamisa.
        var trabes = elegidas.Where(Detallador.EsDeConcreto).ToList();

        if (trabes.Count == 0)
        {
            if (elegidas.Count > 0)
            {
                TaskDialog.Show("CadLink", "Las vigas elegidas no son de concreto. El encamisado es para trabes de concreto.");
            }

            return Result.Cancelled;
        }

        var o = new OpcionesEncamisado();

        var ventana = new VentanaDetalle("CadLink - Encamisar trabes",
            $"Se encamisan {trabes.Count} trabe(s) de concreto, de cara a cara de columna. La seccion se "
            + "mide de la trabe dibujada, ya recortada bajo la losa."
            + (elegidas.Count > trabes.Count ? $" {elegidas.Count - trabes.Count} de acero se saltan." : string.Empty));

        var tipo = ventana.Lista("Encamisado:", new[]
        {
            "Placas corridas, con tornillos pasantes en los lados y anclas abajo y arriba",
            "Angulos en las esquinas y soleras"
        });
        var fondo = ventana.Casilla("Cara de abajo", true);
        var lados = ventana.Casilla("Caras laterales", true);
        var arriba = ventana.Casilla("Cara de arriba", false, "Casi siempre la trabe tiene la losa encima.");
        var espesor = ventana.Numero("(A) Espesor de las placas o los angulos, en mm:", o.EspesorM * 1000);
        var adhesivo = ventana.Numero("(B) Adhesivo epoxico o grout entre concreto y acero, en mm (0 = sin):", o.AdhesivoM * 1000);
        var lado = ventana.Numero("(C) Angulos: lado, en mm:", o.LadoAnguloM * 1000);
        var solera = ventana.Numero("(D) Soleras: ancho, en mm:", o.AnchoSoleraM * 1000);
        var espSolera = ventana.Numero("(E) Soleras: espesor, en mm:", o.EspesorSoleraM * 1000);
        var sep = ventana.Numero("(F) Separacion de soleras o tornillos, en cm:", o.SeparacionM * 100);
        var tornillo = ventana.Numero("(G) Placas laterales: diametro de los tornillos pasantes, en mm (0 = sin):", o.TornilloM * 1000);
        var filas = ventana.Numero("(H) Placas laterales: filas de tornillos (0 = las que quepan a 20 cm):", 0);
        var ancla = ventana.Numero("(I) Placas de abajo y arriba: diametro de las anclas al concreto, en mm (0 = sin):", o.AnclaM * 1000);
        var empotre = ventana.Numero("(J) Anclas: lo que se meten en el concreto, en cm:", o.AnclaEmpotreM * 100);
        var tres = ventana.Casilla("Tornillos y anclas en tresbolillo", true,
            "Cada fila corrida media separacion de la de al lado, en los huecos que esa deja.");
        var libre = ventana.Numero("(K) Libre antes de la cara de la columna, en cm:", 0);
        ventana.Guia("Guia: corte con placas", 230, 225, CroquisDeGuia.EncamisadoPlacas);
        ventana.Guia("Guia: corte con angulos y soleras", 230, 160, CroquisDeGuia.EncamisadoAngulos);
        ventana.Guia("Guia: la trabe de lado", 230, 120, CroquisDeGuia.EncamisadoAlzado);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Encamisar"))
        {
            return Result.Cancelled;
        }

        o.Tipo = tipo.SelectedIndex == 1 ? TipoEncamisado.AngulosYSoleras : TipoEncamisado.Placas;
        o.Fondo = fondo.IsChecked == true;
        o.Laterales = lados.IsChecked == true;
        o.Arriba = arriba.IsChecked == true;
        o.EspesorM = VentanaDetalle.Leer(espesor, o.EspesorM * 1000) / 1000;
        o.AdhesivoM = VentanaDetalle.Leer(adhesivo, o.AdhesivoM * 1000) / 1000;
        o.LadoAnguloM = VentanaDetalle.Leer(lado, o.LadoAnguloM * 1000) / 1000;
        o.AnchoSoleraM = VentanaDetalle.Leer(solera, o.AnchoSoleraM * 1000) / 1000;
        o.EspesorSoleraM = VentanaDetalle.Leer(espSolera, o.EspesorSoleraM * 1000) / 1000;
        o.SeparacionM = VentanaDetalle.Leer(sep, o.SeparacionM * 100) / 100;
        o.TornilloM = VentanaDetalle.Leer(tornillo, o.TornilloM * 1000) / 1000;
        o.FilasLaterales = (int)Math.Round(VentanaDetalle.Leer(filas, 0));
        o.AnclaM = VentanaDetalle.Leer(ancla, o.AnclaM * 1000) / 1000;
        o.AnclaEmpotreM = VentanaDetalle.Leer(empotre, o.AnclaEmpotreM * 100) / 100;
        o.Tresbolillo = tres.IsChecked == true;
        o.LibreEnPuntasM = VentanaDetalle.Leer(libre, 0) / 100;

        var falta = Encamisado.Falta(o);

        if (falta.Count > 0)
        {
            TaskDialog.Show("CadLink", "Falta: " + string.Join(", ", falta) + ".");
            return Result.Cancelled;
        }

        var r = Detallador.Encamisar(doc, trabes, o);

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "trabe(s) encamisada(s)", null));

        return Result.Succeeded;
    }
}

/// <summary>
/// El boton <b>Armar muros</b>: pone verticales y horizontales, en una o dos parrillas, en TODOS
/// los muros de concreto elegidos.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoArmarMuros : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de armar muros.";
            return Result.Failed;
        }

        var muros = DetallesEnRevit.Piezas(uidoc, "Elige los muros de concreto que se arman y pulsa Terminar.",
            BuiltInCategory.OST_Walls).OfType<Wall>().ToList();

        if (muros.Count == 0)
        {
            return Result.Cancelled;
        }

        var varillas = ArmadorDeMuros.Varillas(doc).Keys.OrderBy(n => n).ToList();

        if (varillas.Count == 0)
        {
            TaskDialog.Show("CadLink", "El proyecto no tiene tipos de varilla (Rebar Bar). Crea o carga alguno.");
            return Result.Cancelled;
        }

        var o = new OpcionesArmadoMuro();

        var ventana = new VentanaDetalle("CadLink - Armar muros",
            $"Se pone el armado en los {muros.Count} muro(s) elegidos, de punta a punta y de la base a la "
            + "corona, con el recubrimiento que ya tenga cada muro. Recorta solo en puertas y ventanas. "
            + "Los muros que no sean estructurales de concreto se saltan y se dicen.\n\n"
            + "Volver a armar un muro reemplaza el armado de CadLink; el puesto a mano no se toca.\n\n"
            + "Con el gancho de arriba distinto del de abajo, el armado queda en varillas sueltas en lugar de "
            + "Area Reinforcement: es la unica forma de que cada punta lleve el suyo.");

        var parrillas = ventana.Lista("Parrillas:", new[] { "Dos: una en cada cara", "Una: en la cara exterior" });
        var vertical = ventana.Lista("Varilla vertical:", varillas);
        var sepV = ventana.Numero("(C) Separacion de las verticales, en cm:", o.SeparacionVerticalM * 100);
        var horizontal = ventana.Lista("Varilla horizontal:", varillas);
        var sepH = ventana.Numero("(D) Separacion de las horizontales, en cm:", o.SeparacionHorizontalM * 100);

        // Los ganchos de cada punta de las verticales, con su largo en diametros: el de arriba
        // (A) y el de abajo (B), que entra a la zapata y suele llevar la longitud de desarrollo.
        var angulos = new[] { "Sin gancho", "90°", "135°", "180°" };
        var ganchoArriba = ventana.Lista("(A) Gancho de arriba de las verticales:", angulos);
        ganchoArriba.SelectedIndex = 1;
        var largoArriba = ventana.Numero("(A) Largo del gancho de arriba, en diametros de la varilla:", o.GanchoSuperior.Diametros);
        var ganchoAbajo = ventana.Lista("(B) Gancho de abajo de las verticales:", angulos);
        ganchoAbajo.SelectedIndex = 1;
        var largoAbajo = ventana.Numero("(B) Largo del gancho de abajo, en diametros (desarrollo):", o.GanchoInferior.Diametros);
        var libre = ventana.Numero("Libre en las orillas del muro, en cm (0 = al canto, con su recubrimiento):", 0);
        ventana.Guia("Guia", 300, 260, CroquisDeGuia.ArmadoMuro);

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Armar"))
        {
            return Result.Cancelled;
        }

        o.DosParrillas = parrillas.SelectedIndex != 1;
        o.VarillaVertical = vertical.SelectedItem as string ?? string.Empty;
        o.VarillaHorizontal = horizontal.SelectedItem as string ?? string.Empty;
        o.SeparacionVerticalM = VentanaDetalle.Leer(sepV, 20) / 100;
        o.SeparacionHorizontalM = VentanaDetalle.Leer(sepH, 20) / 100;
        o.GanchoSuperior = new GanchoMuro(GanchoMuro.Angulos[Math.Max(0, ganchoArriba.SelectedIndex)],
            VentanaDetalle.Leer(largoArriba, o.GanchoSuperior.Diametros));
        o.GanchoInferior = new GanchoMuro(GanchoMuro.Angulos[Math.Max(0, ganchoAbajo.SelectedIndex)],
            VentanaDetalle.Leer(largoAbajo, o.GanchoInferior.Diametros));
        o.LibreM = VentanaDetalle.Leer(libre, 0) / 100;

        var falta = ArmadoDeMuros.Falta(o);

        if (falta.Count > 0)
        {
            TaskDialog.Show("CadLink", "Falta: " + string.Join(", ", falta) + ".");
            return Result.Cancelled;
        }

        var r = ArmadorDeMuros.Armar(doc, muros, o);

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "muro(s) armado(s)", null));

        return Result.Succeeded;
    }
}

/// <summary>
/// El boton <b>Armar cimentacion</b>: las parrillas de las zapatas elegidas, escritas aqui o leidas
/// de la hoja de CadLink.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoArmarCimentacion : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de armar la cimentacion.";
            return Result.Failed;
        }

        // Las zapatas: las de Revit -de cimentacion o modeladas como FLOOR, que es como muchos las
        // modelan para poder editar su contorno- y las de CadLink, sin el dado, la plantilla ni la
        // contratrabe de estas, que van en la misma categoria.
        var zapatas = DetallesEnRevit.Piezas(uidoc, "Elige las zapatas (de cimentacion o Floor) que se arman y pulsa Terminar.",
                BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Floors)
            .Where(e => !EsPiezaAuxiliar(e))
            .ToList();

        if (zapatas.Count == 0)
        {
            return Result.Cancelled;
        }

        var dueno = commandData!.Application?.MainWindowHandle;

        var fuente = new VentanaDetalle("CadLink - Armar cimentacion",
            $"Se arman {zapatas.Count} zapata(s). ¿De donde sale el armado?\n\n"
            + "En las zapatas de Revit van varillas nativas, que se cuantifican. En las de CadLink -que son "
            + "DirectShape y Revit no acepta como anfitrion de armado- van como solidos que se ven pero no "
            + "se cuantifican.");
        var deDonde = fuente.Lista("Armado:", new[]
        {
            "Escribirlo aqui",
            "De la hoja de CadLink (.cadlink-detalles.json)"
        });

        if (!fuente.Mostrar(dueno, "Siguiente"))
        {
            return Result.Cancelled;
        }

        var elegidas = deDonde.SelectedIndex == 1
            ? DeArchivo(doc, zapatas, dueno)
            : Escrito(doc, zapatas, dueno);

        if (elegidas is null)
        {
            return Result.Cancelled;
        }

        var r = ArmadorDeZapatas.Armar(doc, elegidas);

        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, "zapata(s) armada(s)", null));

        return Result.Succeeded;
    }

    /// <summary>¿Es el dado, la plantilla, la contratrabe o el enrase de una zapata de CadLink?</summary>
    private static bool EsPiezaAuxiliar(Element e)
    {
        if (e is not DirectShape)
        {
            return false;
        }

        var c = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;

        return new[] { ", dado", ", plantilla", ", contratrabe", ", enrase" }
            .Any(x => c.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// De la hoja de CadLink: las zapatas de CadLink toman SU fila por su marca; las demas, la que se
    /// elija en el cuadro.
    /// </summary>
    private static List<ArmadorDeZapatas.ZapataElegida>? DeArchivo(Document doc, List<Element> zapatas, IntPtr? dueno)
    {
        var archivo = DetallesEnRevit.Archivo();

        if (archivo is null)
        {
            return null;
        }

        var filas = archivo.Zapatas.Where(z => z.Armado is not null).Select(z => (z.Id, "aislada", z.Armado!))
            .Concat(archivo.Corridas.Where(z => z.Armado is not null).Select(z => (z.Id, "corrida", z.Armado!)))
            .ToList();

        if (filas.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "El archivo no trae armado de zapatas. En CadLink, llena las varillas de las hojas Zapatas "
                + "Aisladas o Zapatas Corridas y vuelve a pulsar «Para Revit…».");
            return null;
        }

        ArmadoZapataJson? PorMarca(Element e)
        {
            var marca = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;

            return e is DirectShape && marca.Length > 0
                ? filas.Where(f => string.Equals(f.Id, marca, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Item3).FirstOrDefault()
                : null;
        }

        var sinFila = zapatas.Count(z => PorMarca(z) is null);

        var ventana = new VentanaDetalle("CadLink - Armar cimentacion",
            sinFila == 0
                ? "Todas las zapatas elegidas son de CadLink y toman su fila por su marca."
                : $"{zapatas.Count - sinFila} zapata(s) de CadLink toman su fila por su marca. Para las otras "
                  + $"{sinFila}, elige la fila de la hoja.");

        var parrillas = ventana.Lista("Parrillas:", new[]
        {
            "Como dice la hoja (doble si la fila trae la de arriba)",
            "Doble: abajo y arriba (si la fila no trae la de arriba, se repite la de abajo)",
            "Una: solo abajo"
        });

        var lista = sinFila > 0
            ? ventana.Lista("Fila:", filas.Select(f =>
                $"{f.Id} ({f.Item2})  -  {(ArmadoZapata.EsDoble(f.Item3) ? "doble" : "una")} parrilla: "
                + ArmadorDeZapatas.Resumen(f.Item3)))
            : null;

        if (!ventana.Mostrar(dueno, "Armar") || (lista is not null && lista.SelectedIndex < 0))
        {
            return null;
        }

        var modo = parrillas.SelectedIndex switch
        {
            1 => ModoParrillas.Doble,
            2 => ModoParrillas.Una,
            _ => ModoParrillas.ComoLaHoja
        };

        var general = lista is null ? null : filas[lista.SelectedIndex].Item3;

        ArmadoZapataJson? Con(ArmadoZapataJson? a) => a is null ? null : ArmadoZapata.ConParrillas(a, modo);

        return zapatas
            .Select(z => PorMarca(z) is { } propio
                ? new ArmadorDeZapatas.ZapataElegida(z, Con(propio), "su marca")
                : new ArmadorDeZapatas.ZapataElegida(z, Con(general), "la fila elegida"))
            .ToList();
    }

    /// <summary>Escrito en el cuadro, igual para todas las zapatas elegidas.</summary>
    private static List<ArmadorDeZapatas.ZapataElegida>? Escrito(Document doc, List<Element> zapatas, IntPtr? dueno)
    {
        var tipos = ArmadorDeZapatas.TiposDeVarilla(doc)
            .GroupBy(t => t.Tipo.Name ?? string.Empty)
            .Select(g => g.First())
            .OrderBy(t => t.DiametroM).ThenBy(t => t.Tipo.Name)
            .ToList();

        if (tipos.Count == 0)
        {
            TaskDialog.Show("CadLink", "El proyecto no tiene tipos de varilla (Rebar Bar). Crea o carga alguno.");
            return null;
        }

        var nombres = tipos.Select(t => $"{t.Tipo.Name}  (Ø{t.DiametroM * 1000:0.#} mm)").ToList();

        var ventana = new VentanaDetalle("CadLink - Armar cimentacion",
            "Las «largas» corren a lo largo del lado MAYOR de la zapata y las «cortas» a lo largo del menor. "
            + "Las separaciones son maximas: las varillas se reparten parejo de recubrimiento a recubrimiento.");

        var parrillas = ventana.Lista("Parrillas:", new[]
        {
            "Una: solo abajo (A y B)",
            "Doble: abajo (A y B) y arriba (C y D)"
        });
        var infL = ventana.Lista("(A) Abajo, varillas largas:", nombres);
        var sepInfL = ventana.Numero("(A) Su separacion, en cm:", 15);
        var infC = ventana.Lista("(B) Abajo, varillas cortas:", nombres);
        var sepInfC = ventana.Numero("(B) Su separacion, en cm:", 15);
        var supL = ventana.Lista("(C) Arriba, varillas largas (solo doble):", nombres);
        var sepSupL = ventana.Numero("(C) Su separacion, en cm:", 20);
        var supC = ventana.Lista("(D) Arriba, varillas cortas (solo doble):", nombres);
        var sepSupC = ventana.Numero("(D) Su separacion, en cm:", 20);
        var rec = ventana.Numero("(E) Recubrimiento, en cm:", 5);
        var ganchos = ventana.Casilla("(F) Con patas en las puntas, hasta la otra parrilla", true);
        ventana.Guia("Guia", 260, 230, CroquisDeGuia.ArmadoZapata);

        if (!ventana.Mostrar(dueno, "Armar"))
        {
            return null;
        }

        CapaZapataJson? Capa(System.Windows.Controls.ComboBox lista, System.Windows.Controls.TextBox sep, bool usar)
        {
            if (!usar || lista.SelectedIndex < 0)
            {
                return null;
            }

            var t = tipos[lista.SelectedIndex];

            return new CapaZapataJson
            {
                Varilla = t.Tipo.Name ?? string.Empty,
                DiametroM = t.DiametroM,
                SeparacionM = VentanaDetalle.Leer(sep, 0) / 100
            };
        }

        var armado = new ArmadoZapataJson
        {
            InfLargas = Capa(infL, sepInfL, true),
            InfCortas = Capa(infC, sepInfC, true),
            SupLargas = Capa(supL, sepSupL, parrillas.SelectedIndex == 1),
            SupCortas = Capa(supC, sepSupC, parrillas.SelectedIndex == 1),
            RecM = VentanaDetalle.Leer(rec, 5) / 100,
            Ganchos = ganchos.IsChecked == true
        };

        return zapatas.Select(z => new ArmadorDeZapatas.ZapataElegida(z, armado, "el cuadro")).ToList();
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
