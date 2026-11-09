using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CajaDeTexto = System.Windows.Controls.TextBox;
using ListaDesplegable = System.Windows.Controls.ComboBox;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// El armado de columnas y trabes ESCRITO en Revit: se eligen las piezas, se escribe la seccion y
/// se arman todas con el mismo armador que usa la hoja de CadLink.
/// </summary>
/// <remarks>
/// La seccion de cada tipo se mide de la familia -base y peralte-, asi que con un solo armado
/// escrito se arman piezas de tipos de distinto tamaño: las varillas se acomodan a cada una. Las
/// zonas de estribos -L/4, L/2, L/4- se reparten con la longitud real de cada pieza, como desde la
/// hoja.
/// </remarks>
internal static class ArmadoEscritoEnRevit
{
    public static Result Ejecutar(ExternalCommandData data, ClasePieza? solo)
    {
        var uidoc = data.Application.ActiveUIDocument;
        var doc = uidoc.Document;

        var categorias = solo switch
        {
            ClasePieza.Columna => new[] { BuiltInCategory.OST_StructuralColumns },
            ClasePieza.Trabe => new[] { BuiltInCategory.OST_StructuralFraming },
            _ => new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming }
        };

        var elegidas = DetallesEnRevit.Piezas(uidoc, "Elige las columnas o trabes de CONCRETO que se arman y pulsa Terminar.",
            categorias).OfType<FamilyInstance>().ToList();
        var piezas = elegidas.Where(Detallador.EsDeConcreto).ToList();

        if (piezas.Count == 0)
        {
            if (elegidas.Count > 0)
            {
                TaskDialog.Show("CadLink", "Lo elegido no es de concreto. El armado va en columnas y trabes de concreto.");
            }

            return Result.Cancelled;
        }

        bool EsColumna(Element e) => e.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns;

        var hayColumnas = piezas.Any(EsColumna);
        var hayTrabes = piezas.Any(p => !EsColumna(p));

        var varillas = Armador.TiposDeVarilla(doc)
            .GroupBy(v => v.Nombre).Select(g => g.First())
            .OrderBy(v => v.DiamM).ThenBy(v => v.Nombre)
            .ToList();

        if (varillas.Count == 0)
        {
            TaskDialog.Show("CadLink", "El proyecto no tiene tipos de varilla (Rebar Bar). Crea o carga alguno.");
            return Result.Cancelled;
        }

        var nombres = varillas.Select(v => $"{v.Nombre}  (Ø{v.DiamM * 1000:0.#} mm)").ToList();
        var o = new OpcionesArmadoEscrito();

        var ventana = new VentanaDetalle("CadLink - Armar, escrito en Revit",
            $"Columnas: {piezas.Count(EsColumna)}. Trabes: {piezas.Count(p => !EsColumna(p))}."
            + (elegidas.Count > piezas.Count ? $" {elegidas.Count - piezas.Count} de acero se saltan." : string.Empty)
            + "\n\nLa seccion de cada pieza se mide de su tipo, y las zonas de estribos con su longitud real. "
            + "Volver a armar reemplaza el armado de CadLink; el puesto a mano no se toca.");

        var rec = ventana.Numero("(A) Recubrimiento libre, en cm:", o.RecubrimientoCm);
        var estribo = ventana.Lista("(B) Estribo:", nombres);
        var sepExt = ventana.Numero("(C) Separacion de estribos en los extremos (L/4), en cm:", o.SepExtremosCm);
        var sepCen = ventana.Numero("(D) Separacion de estribos al centro (L/2), en cm:", o.SepCentroCm);

        CajaDeTexto? nSup = null, nInf = null, nLat = null, nX = null, nY = null;
        ListaDesplegable? vSup = null, vInf = null, vLat = null, vCol = null;

        if (hayTrabes)
        {
            nSup = ventana.Numero("(E) Trabe: varillas arriba:", o.NSuperior);
            vSup = ventana.Lista("(E) Trabe: varilla de arriba:", nombres);
            nInf = ventana.Numero("(F) Trabe: varillas abajo:", o.NInferior);
            vInf = ventana.Lista("(F) Trabe: varilla de abajo:", nombres);
            nLat = ventana.Numero("(G) Trabe: laterales por cara (0 = sin):", 0);
            vLat = ventana.Lista("(G) Trabe: varilla lateral:", nombres);
        }

        if (hayColumnas)
        {
            nX = ventana.Numero("(H) Columna: varillas por cara en X, con las esquinas:", o.NCaraX);
            nY = ventana.Numero("(I) Columna: varillas por cara en Y, con las esquinas:", o.NCaraY);
            vCol = ventana.Lista("(J) Columna: varilla:", nombres);
        }

        ventana.Guia("Guia", 300, 230, CroquisDeGuia.ArmadoSeccion);

        if (!ventana.Mostrar(data.Application?.MainWindowHandle, "Armar"))
        {
            return Result.Cancelled;
        }

        (string Nombre, double DiamCm) De(ListaDesplegable? lista)
        {
            if (lista is null || lista.SelectedIndex < 0)
            {
                return (string.Empty, 0);
            }

            var v = varillas[lista.SelectedIndex];
            return (v.Nombre, v.DiamM * 100);
        }

        int N(CajaDeTexto? caja, int siNo) =>
            caja is null ? siNo : (int)Math.Round(VentanaDetalle.Leer(caja, siNo));

        o.RecubrimientoCm = VentanaDetalle.Leer(rec, o.RecubrimientoCm);
        (o.Estribo, o.DiamEstriboCm) = De(estribo);
        o.SepExtremosCm = VentanaDetalle.Leer(sepExt, o.SepExtremosCm);
        o.SepCentroCm = VentanaDetalle.Leer(sepCen, o.SepCentroCm);
        o.NSuperior = N(nSup, o.NSuperior);
        (o.VarSuperior, o.DiamSuperiorCm) = De(vSup);
        o.NInferior = N(nInf, o.NInferior);
        (o.VarInferior, o.DiamInferiorCm) = De(vInf);
        o.NLateral = N(nLat, 0);
        (o.VarLateral, o.DiamLateralCm) = De(vLat);
        o.NCaraX = N(nX, o.NCaraX);
        o.NCaraY = N(nY, o.NCaraY);
        (o.VarColumna, o.DiamColumnaCm) = De(vCol);

        var falta = ArmadoEscrito.Falta(o, hayTrabes, hayColumnas);

        if (falta.Count > 0)
        {
            TaskDialog.Show("CadLink", "Falta: " + string.Join(", ", falta) + ".");
            return Result.Cancelled;
        }

        // ---- Un trabajo por tipo, con la seccion medida de ese tipo ----
        var tipos = Armador.TiposArmables(doc, out _).ToDictionary(t => t.Id);
        var trabajo = new List<Armador.TrabajoPorTipo>();
        var sinMedidas = new List<string>();

        foreach (var g in piezas.GroupBy(p => p.GetTypeId().Value))
        {
            if (!tipos.TryGetValue(g.Key, out var tipo) || tipo.AnchoM is not double b || tipo.PeralteM is not double h)
            {
                sinMedidas.Add($"«{(tipos.TryGetValue(g.Key, out var t) ? t.Nombre : g.Key.ToString())}»: "
                               + "No se pudo leer su base y su peralte del tipo; no se armo.");
                continue;
            }

            var id = "Revit: " + tipo.Tipo;
            var armado = EsColumna(g.First())
                ? ArmadoEscrito.Columna(id, b * 100, h * 100, o)
                : ArmadoEscrito.Trabe(id, b * 100, h * 100, o);

            trabajo.Add(new Armador.TrabajoPorTipo(tipo.Nombre, g.ToList(), armado));
        }

        // Cada varilla lleva por clave el NOMBRE de su tipo de Revit: de ahi sale cual se pone.
        var ids = varillas.ToDictionary(v => v.Nombre, v => v.Id, StringComparer.OrdinalIgnoreCase);

        var r = Armador.EjecutarPorTipo(doc, trabajo,
            (_, v) => ids.TryGetValue(v.Clave, out var idVarilla) ? idVarilla : null);

        r.Errores.InsertRange(0, sinMedidas);

        TaskDialog.Show("CadLink", ComandoArmar.Informe(r, trabajo.Sum(w => w.Piezas.Count)));

        return Result.Succeeded;
    }
}
