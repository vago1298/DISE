using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

// WPF y la Revit API tienen tipos con el MISMO nombre: Autodesk.Revit.DB.Binding y
// Autodesk.Revit.UI.TextBox (el de la cinta). Aqui se usan los de WPF.
using Binding = System.Windows.Data.Binding;
using TextBox = System.Windows.Controls.TextBox;

namespace CadLink.Revit;

/// <summary>
/// El boton <b>Corte de sección</b>: un corte NUEVO de la seccion que se pida, con su nombre,
/// sus llamadas, su etiqueta y sus cotas, sin volver a armar nada.
/// </summary>
/// <remarks>
/// <para>
/// Si hay trabes o columnas seleccionadas, sale un renglon por cada una: el corte se hace en ESA
/// pieza. Si no, un renglon por cada tipo con seccion de CadLink, y el corte se hace en su
/// primera pieza. En los dos casos se marca la que se quiere y se le pone el nombre.
/// </para>
/// <para>
/// La seccion de cada tipo sale de su Descripcion -la escribe «Armar por tipo»- o, si no, de su
/// nombre o sus medidas. El archivo de secciones es el ultimo que se abrio: no se vuelve a pedir.
/// </para>
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoCorte : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData?.Application?.ActiveUIDocument;
        var doc = uidoc?.Document;

        if (uidoc is null || doc is null)
        {
            message = "Abre un proyecto de Revit.";
            return Result.Failed;
        }

        if (doc.IsFamilyDocument)
        {
            TaskDialog.Show("CadLink", "Los cortes se hacen en un PROYECTO, no en el editor de familias.");
            return Result.Cancelled;
        }

        // ---- 1. Las secciones: el ultimo archivo, o se pide ----
        var ruta = UltimoArchivoArmado.Leer();

        if (ruta is null)
        {
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

            ruta = abrir.FileName;
        }

        ArchivoArmadoJson archivo;

        try
        {
            archivo = ArchivoArmado.Leer(ruta);
            UltimoArchivoArmado.Guardar(ruta);
        }
        catch (Exception e)
        {
            TaskDialog.Show("CadLink", "No se pudo leer el archivo de secciones.\n\n" + e.Message);
            return Result.Failed;
        }

        // ---- 2. Que se puede cortar ----
        var tipos = Armador.TiposArmables(doc, out var piezas);
        var porId = tipos.ToDictionary(t => t.Id);

        ArmadoJson? SeccionDe(TipoArmable t) =>
            VistaCortes.SeccionDe(t, doc.GetElement(new ElementId(t.Id)) is Element e ? Despiece.DescripcionDe(e) : null,
                archivo.Armados);

        double Largo(FamilyInstance fi) =>
            fi.Location is LocationCurve lc ? Unidades.AMetros(lc.Curve.Length) : 0;

        var filas = new List<FilaCorte>();
        var piezaDe = new Dictionary<FilaCorte, FamilyInstance>();

        var elegidas = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<FamilyInstance>()
            .Where(fi => porId.ContainsKey(fi.GetTypeId().Value))
            .ToList();

        if (elegidas.Count > 0)
        {
            foreach (var fi in elegidas)
            {
                var t = porId[fi.GetTypeId().Value];

                if (SeccionDe(t) is { } a)
                {
                    var f = new FilaCorte(t, a, Largo(fi)) { Crear = true };
                    filas.Add(f);
                    piezaDe[f] = fi;
                }
            }
        }
        else
        {
            foreach (var t in tipos)
            {
                if (SeccionDe(t) is { } a && piezas[t.Id].Count > 0)
                {
                    var fi = piezas[t.Id][0];
                    var f = new FilaCorte(t, a, Largo(fi));
                    filas.Add(f);
                    piezaDe[f] = fi;
                }
            }
        }

        if (filas.Count == 0)
        {
            TaskDialog.Show("CadLink",
                elegidas.Count > 0
                    ? "Las piezas seleccionadas no tienen sección de CadLink.\n\nArmalas antes con «Armar por tipo»: ahí se les asigna."
                    : "Ningún tipo de columna o trabe del proyecto tiene sección de CadLink.\n\nArmalos antes con «Armar por tipo».");
            return Result.Cancelled;
        }

        // ---- 3. El cuadro ----
        var vista = new VistaCortes(filas);

        if (!Preguntar(commandData!, vista))
        {
            return Result.Cancelled;
        }

        var pedidos = vista.ACrear
            .Select(f => (f.Armado, piezaDe[f], f.Nombre))
            .ToList();

        // ---- 4. Los cortes ----
        var (r, vistas) = Armador.Cortes(doc, pedidos);

        // Se abre el primero: es lo que se queria ver.
        if (vistas.Count > 0)
        {
            try
            {
                uidoc.ActiveView = vistas[0];
            }
            catch (Exception)
            {
                // Si no se puede abrir, queda en el navegador de proyecto.
            }
        }

        if (r.Errores.Count > 0 || r.Avisos.Count > 0 || vistas.Count == 0)
        {
            var sb = new StringBuilder();
            sb.Append("Cortes creados: ").Append(vistas.Count).AppendLine();

            if (r.Avisos.Count > 0)
            {
                sb.AppendLine().AppendLine(Agrupador.Texto(r.Avisos, 6, "Hay algo que decir de"));
            }

            if (r.Errores.Count > 0)
            {
                sb.AppendLine().AppendLine(Agrupador.Texto(r.Errores));
            }

            TaskDialog.Show("CadLink", sb.ToString());
        }

        return Result.Succeeded;
    }

    /// <summary>El cuadro: marcar la seccion y ponerle nombre a su corte.</summary>
    private static bool Preguntar(ExternalCommandData datos, VistaCortes vista)
    {
        var reja = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 28,
            ItemsSource = vista.Filas,
            Margin = new Thickness(0, 8, 0, 8)
        };

        // La casilla, como plantilla para que marque al primer clic.
        var casilla = new FrameworkElementFactory(typeof(CheckBox));
        casilla.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        casilla.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        casilla.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(FilaCorte.Crear)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

        reja.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Crear",
            Width = 50,
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = casilla }
        });

        void Texto(string encabezado, string campo, double ancho) =>
            reja.Columns.Add(new DataGridTextColumn
            {
                Header = encabezado,
                Binding = new Binding(campo),
                IsReadOnly = true,
                Width = ancho
            });

        Texto("Sección", nameof(FilaCorte.Seccion), 90);
        Texto("Tipo de Revit", nameof(FilaCorte.NombreTipo), 240);
        Texto("Piezas", nameof(FilaCorte.Piezas), 55);
        Texto("Ya armadas", nameof(FilaCorte.YaArmadas), 75);

        // El nombre se escribe en la celda: un TextBox de plantilla, que edita al primer clic.
        var caja = new FrameworkElementFactory(typeof(TextBox));
        caja.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        caja.SetBinding(TextBox.TextProperty,
            new Binding(nameof(FilaCorte.Nombre)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

        reja.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Nombre del corte",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = caja }
        });

        var resumen = new TextBlock { Text = vista.Resumen, Foreground = System.Windows.Media.Brushes.DimGray };
        var crear = new Button { Content = "Crear cortes", Padding = new Thickness(18, 5, 18, 5), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelar = new Button { Content = "Cancelar", Padding = new Thickness(14, 5, 14, 5), IsCancel = true };

        void Refrescar()
        {
            resumen.Text = vista.Resumen;
            crear.IsEnabled = vista.ACrear.Any();
        }

        vista.PropertyChanged += (_, _) => Refrescar();
        Refrescar();

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botones.Children.Add(crear);
        botones.Children.Add(cancelar);

        var raiz = new DockPanel { Margin = new Thickness(12) };
        var titulo = new TextBlock
        {
            Text = "Marca la sección de la que quieres un corte nuevo y ponle su nombre:",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        DockPanel.SetDock(titulo, Dock.Top);
        DockPanel.SetDock(botones, Dock.Bottom);
        DockPanel.SetDock(resumen, Dock.Bottom);
        raiz.Children.Add(titulo);
        raiz.Children.Add(botones);
        raiz.Children.Add(resumen);
        raiz.Children.Add(reja);

        var ventana = new Window
        {
            Title = "Corte de sección: CadLink",
            Content = raiz,
            Width = 820,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        crear.Click += (_, _) =>
        {
            // Poner DialogResult ya cierra el cuadro.
            ventana.DialogResult = true;
        };

        try
        {
            var asa = datos.Application?.MainWindowHandle;

            if (asa is not null)
            {
                new WindowInteropHelper(ventana).Owner = asa.Value;
            }
        }
        catch (Exception)
        {
            // Cosmetico.
        }

        return ventana.ShowDialog() == true;
    }
}
