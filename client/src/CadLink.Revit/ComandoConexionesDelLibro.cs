using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

/// <summary>
/// El boton <b>Conexiones del libro</b>: lee el libro de revisiones de acero (.xlsm) y modela la
/// conexion elegida en las piezas que se piquen.
/// </summary>
/// <remarks>
/// <para>
/// No hace falta pasar por CadLink: el libro se lee aqui mismo con el nucleo, el mismo que arma el
/// detalle de AutoCAD, asi que el modelo y el plano salen de las mismas medidas.
/// </para>
/// <para>
/// Segun el tipo de conexion se pica una cosa distinta:
/// uniones viga-columna, cortante y viga-viga → clic en la viga CERCA del extremo;
/// contraventeo excentrico → la viga del eslabon;
/// placa gusset → clic en la diagonal cerca del extremo de la esquina;
/// X → una de las diagonales;
/// placa base → las columnas.
/// Cada clic es su propia transaccion y Esc termina.
/// </para>
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoConexionesDelLibro : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null || doc.IsFamilyDocument)
        {
            message = "Abre un proyecto de Revit antes de poner conexiones.";
            return Result.Failed;
        }

        var abrir = new OpenFileDialog
        {
            Title = "Abrir el libro de revisiones de acero",
            Filter = "Libro de Excel (*.xlsm;*.xlsx)|*.xlsm;*.xlsx|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (abrir.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        List<ConexionDelLibro> todas;
        bool recalcular;

        try
        {
            var libro = LibroExcel.Abrir(abrir.FileName);
            recalcular = libro.RecalcularAlAbrir;
            todas = ConexionesDelLibro.Leer(libro);
        }
        catch (Exception e)
        {
            TaskDialog.Show("CadLink", "No se pudo leer el libro.\n\n" + e.Message);
            return Result.Cancelled;
        }

        var lista = todas.Where(c => c.SeDibuja).OrderBy(c => c.Hoja.DeEjemplo).ToList();

        if (lista.Count == 0)
        {
            TaskDialog.Show("CadLink", "El libro no trae conexiones que CadLink sepa modelar.");
            return Result.Cancelled;
        }

        var ventana = new VentanaDetalle("CadLink - Conexiones del libro de acero",
            "Elige la conexion y despues las piezas donde va. Las medidas son las de la hoja del libro.\n\n"
            + "- Union viga-columna, RBS, placa de extremo, patines soldados, cortante y viga-viga: clic en la viga CERCA del extremo.\n"
            + "- Contraventeo excentrico: la viga del eslabon.\n"
            + "- Placa gusset: clic en la diagonal CERCA del extremo de la esquina.\n"
            + "- Contraventeo en X: una de las dos diagonales.\n"
            + "- Placa base: las columnas.\n\n"
            + "Cada clic pone la conexion al momento; Esc termina. Volver a ponerla en la misma pieza la cambia, no la duplica."
            + (recalcular ? "\n\nOJO: el libro pide recalcular. Abrelo en Excel y guardalo para que sus valores esten al dia." : string.Empty));

        var opcion = ventana.Lista("Conexion:", lista.Select(Describir));

        if (!ventana.Mostrar(commandData!.Application?.MainWindowHandle, "Elegir piezas") || opcion.SelectedIndex < 0)
        {
            return Result.Cancelled;
        }

        var elegida = lista[opcion.SelectedIndex];
        var m = elegida.Modelo!;
        ResultadoDetalles r;

        switch (m.Colocacion)
        {
            case ColocacionConexion.ExtremoDeViga:
            {
                var columnas = DetallesEnRevit.Todas(doc, BuiltInCategory.OST_StructuralColumns)
                    .Where(c => c is not FamilyInstance fi || !Detallador.EsDeConcreto(fi)).ToList();
                r = DetallesEnRevit.ConectarConCadaClic(uidoc, v => Detallador.ConexionEnExtremo(doc, v, m, columnas));
                break;
            }

            case ColocacionConexion.ExtremoDeDiagonal:
                r = DetallesEnRevit.ConectarConCadaClic(uidoc, v => Detallador.ConexionEnDiagonal(doc, v.Viga, v.Clic, m, false));
                break;

            case ColocacionConexion.CentroDeViga:
            case ColocacionConexion.CentroDeDiagonal:
            {
                var piezas = DetallesEnRevit.Piezas(uidoc,
                    m.Colocacion == ColocacionConexion.CentroDeViga
                        ? "Elige la viga del eslabon y pulsa Terminar."
                        : "Elige una diagonal de cada X y pulsa Terminar.",
                    BuiltInCategory.OST_StructuralFraming).OfType<FamilyInstance>().ToList();
                r = new ResultadoDetalles();

                foreach (var p in piezas)
                {
                    var uno = m.Colocacion == ColocacionConexion.CentroDeViga
                        ? Detallador.ConexionEnViga(doc, p, m)
                        : Detallador.ConexionEnDiagonal(doc, p, null, m, true);
                    Sumar(r, uno);
                }

                break;
            }

            case ColocacionConexion.BaseDeColumna when m.PlacaBase is { } placa:
            {
                var columnas = DetallesEnRevit.Piezas(uidoc, "Elige las columnas de acero y pulsa Terminar.",
                        BuiltInCategory.OST_StructuralColumns)
                    .OfType<FamilyInstance>().Where(c => !Detallador.EsDeConcreto(c)).ToList();

                if (columnas.Count == 0)
                {
                    return Result.Cancelled;
                }

                r = Detallador.Placas(doc, columnas, placa, new OpcionesDetalle
                {
                    SobreElApoyo = true,
                    SubirColumna = true,
                    SoldarColumna = true,
                    GroutM = placa.GroutM
                });
                break;
            }

            default:
                TaskDialog.Show("CadLink", "Esta conexion no se sabe poner en Revit.");
                return Result.Cancelled;
        }

        if (r.Hechas == 0 && r.Errores.Count == 0 && r.Avisos.Count == 0)
        {
            return Result.Cancelled;
        }

        var avisos = new List<string>(m.Avisos);
        TaskDialog.Show("CadLink", DetallesEnRevit.Informe(r, $"conexion(es) «{elegida.Hoja.Hoja.Trim()}» puesta(s)", avisos,
            m.Colocacion is ColocacionConexion.CentroDeViga or ColocacionConexion.CentroDeDiagonal or ColocacionConexion.BaseDeColumna));

        return Result.Succeeded;
    }

    private static void Sumar(ResultadoDetalles total, ResultadoDetalles uno)
    {
        total.Hechas += uno.Hechas;
        total.Reemplazadas += uno.Reemplazadas;
        total.Avisos.AddRange(uno.Avisos);
        total.Errores.AddRange(uno.Errores);
    }

    private static string Describir(ConexionDelLibro c)
    {
        var sb = new StringBuilder();
        sb.Append(c.Hoja.Hoja.Trim()).Append("  -  ").Append(c.Tipo);

        if (c.Hoja.Txt("ID") is { Length: > 0 } id)
        {
            sb.Append("  -  ").Append(id);
        }

        if (c.Hoja.NoCumplen > 0)
        {
            sb.Append($"  [{c.Hoja.NoCumplen} NO CUMPLEN]");
        }

        if (c.Hoja.DeEjemplo)
        {
            sb.Append("  [ejemplo]");
        }

        return sb.ToString();
    }
}
