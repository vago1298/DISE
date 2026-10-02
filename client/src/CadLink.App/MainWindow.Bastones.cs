using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// El cuadro de los <b>bastones</b> de la fila seleccionada.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió que los bastones <b>no se capturen en la tabla</b>, sino aparte, como las
/// grapas: la mayoría de los elementos no llevan, y cuatro columnas casi siempre vacías
/// harían la hoja más difícil de leer.
/// </para>
/// <para>
/// El cuadro se arma en código y no en XAML para que no dependa de los estilos de la
/// ventana principal: es una tabla con cinco columnas y cuatro botones.
/// </para>
/// <para>
/// Se edita una <b>copia</b>: hasta pulsar <i>Aceptar</i>, la fila no cambia, y
/// <i>Cancelar</i> la deja como estaba.
/// </para>
/// </remarks>
public partial class MainWindow
{
    // ======================================================================
    //  Poner bastones con un clic sobre el ALZADO de la vista previa
    // ======================================================================
    //
    //  Pedido del usuario: que se dibujen desde el alzado, como las grapas desde la
    //  sección, y no solo desde el botón. El cuadro del botón sigue para afinar números.

    /// <summary>Dónde quedó el alzado en la vista previa, en píxeles de su lienzo fijo.</summary>
    private sealed record AlzadoEnPrevia(
        SeccionConcretoRow Fila, double Izq, double Top, double W, double H,
        double Esc, double Largo);

    private AlzadoEnPrevia? _alzadoPrevio;

    /// <summary>La caja de cada tramo de bastón pintado, con su índice en la fila.</summary>
    private readonly List<(Rect Caja, int Indice)> _bastonesEnPrevia = new();

    /// <summary>Redondeo de la distancia que da el clic: 5 cm.</summary>
    private const double PasoBastonM = 0.05;

    /// <summary>¿El punto cae sobre el alzado que admite bastones?</summary>
    private bool EnElAlzado(Point p)
    {
        var a = _alzadoPrevio;

        return a is not null
               && ReferenceEquals(a.Fila, Seleccionada)
               && p.X >= a.Izq && p.X <= a.Izq + a.W
               && p.Y >= a.Top && p.Y <= a.Top + a.H;
    }

    /// <summary>
    /// Un clic sobre el alzado: pone o quita un bastón.
    /// </summary>
    /// <returns><c>true</c> si el clic era del alzado y ya se atendió.</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>Sobre un bastón: lo <b>quita</b>.</item>
    ///   <item>Mitad de <b>arriba</b> del alzado: bastón superior; mitad de <b>abajo</b>:
    ///   inferior.</item>
    ///   <item>En el <b>primer o último tercio</b>: bastón de extremo, desde el paño hasta
    ///   donde se hizo clic. Va en los dos extremos; con <b>Ctrl</b>, solo en ese.</item>
    ///   <item>En el <b>tercio central</b>: bastón al centro, que empieza a L/4 de cada
    ///   paño.</item>
    /// </list>
    /// Las varillas y el diámetro salen de los mandos «Bastón» de la vista previa. Para
    /// afinar la distancia, el botón <i>Bastones…</i>.
    /// </remarks>
    private bool ProcesarClicEnAlzado(Point p)
    {
        if (!EnElAlzado(p))
        {
            return false;
        }

        var a = _alzadoPrevio!;
        var fila = a.Fila;

        // Sobre uno que ya está: se quita.
        foreach (var (caja, indice) in _bastonesEnPrevia)
        {
            var holgada = caja;
            holgada.Inflate(2, 3);

            if (holgada.Contains(p) && indice < fila.Bastones.Count)
            {
                var quitado = fila.Bastones[indice];
                fila.ReemplazarBastones(fila.Bastones.Where((_, i) => i != indice).ToList());

                StatusText.Text =
                    $"Bastón quitado ({quitado.Posicion.ToLowerInvariant()}, " +
                    $"{quitado.Ubicacion.ToLowerInvariant()}). Quedan {fila.Bastones.Count}.";
                DibujarVistaPrevia();
                return true;
            }
        }

        var arriba = p.Y < a.Top + (a.H / 2);
        var xm = (p.X - a.Izq) / a.Esc;
        var soloUno = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        string ubicacion;
        double distancia;

        if (xm <= a.Largo / 3)
        {
            ubicacion = soloUno ? BastonSeccion.TextoIzquierdo : BastonSeccion.TextoExtremos;
            distancia = xm;
        }
        else if (xm >= 2 * a.Largo / 3)
        {
            ubicacion = soloUno ? BastonSeccion.TextoDerecho : BastonSeccion.TextoExtremos;
            distancia = a.Largo - xm;
        }
        else
        {
            ubicacion = BastonSeccion.TextoCentro;
            distancia = a.Largo / 4;
        }

        distancia = Math.Max(PasoBastonM, Math.Round(distancia / PasoBastonM) * PasoBastonM);

        var nuevo = new BastonSeccion
        {
            Posicion = arriba ? BastonSeccion.TextoSuperior : BastonSeccion.TextoInferior,
            Ubicacion = ubicacion,
            Cantidad = CantidadBastonElegida,
            Diametro = DiametroBastonElegido,
            DistanciaM = distancia
        };

        fila.ReemplazarBastones(fila.Bastones.Append(nuevo).ToList());

        StatusText.Text =
            $"Bastón {nuevo.Cantidad} {nuevo.Diametro} {nuevo.Posicion.ToLowerInvariant()}, " +
            $"{nuevo.Ubicacion.ToLowerInvariant()}, a {distancia:0.00} m del paño. " +
            "Clic encima para quitarlo; «Bastones…» para afinar.";

        DibujarVistaPrevia();
        return true;
    }

    /// <summary>Las varillas del bastón que se ponga con clic. 2 si no se entiende.</summary>
    private int CantidadBastonElegida =>
        int.TryParse(BastonCantidadTxt.Text, out var n) && n > 0 ? n : 2;

    /// <summary>El diámetro del bastón que se ponga con clic. #4 si no se eligió.</summary>
    private string DiametroBastonElegido =>
        BastonDiametroCombo.SelectedItem is string d && !string.IsNullOrWhiteSpace(d) ? d : "#4";

    private void OnEditarBastones(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        var fila = Seleccionada;

        if (fila is null)
        {
            MessageBox.Show("Selecciona en la hoja la trabe o contratrabe a la que le quieres poner bastones.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!LlevaBastones(fila))
        {
            MessageBox.Show(
                $"\"{fila.Id}\" es {fila.Elemento}. Los bastones solo van en trabes y contratrabes.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var copias = new ObservableCollection<BastonSeccion>(fila.Bastones.Select(b => b.Copia()));

        var tabla = new DataGrid
        {
            ItemsSource = copias,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 160
        };

        tabla.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Lecho",
            ItemsSource = BastonSeccion.Posiciones,
            SelectedItemBinding = new Binding(nameof(BastonSeccion.Posicion)),
            Width = 90
        });

        tabla.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Ubicación",
            ItemsSource = BastonSeccion.Ubicaciones,
            SelectedItemBinding = new Binding(nameof(BastonSeccion.Ubicacion)),
            Width = 130
        });

        tabla.Columns.Add(new DataGridTextColumn
        {
            Header = "Varillas",
            Binding = new Binding(nameof(BastonSeccion.Cantidad)),
            Width = 65
        });

        tabla.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Diámetro",
            ItemsSource = Varilla.Diametros,
            SelectedItemBinding = new Binding(nameof(BastonSeccion.Diametro)),
            Width = 80
        });

        tabla.Columns.Add(new DataGridTextColumn
        {
            Header = "Desde el paño (m)",
            Binding = new Binding(nameof(BastonSeccion.DistanciaM))
            {
                // Con el punto decimal, como el resto del programa: con la coma de un
                // Windows en español, 1.20 se leería como 120.
                ConverterCulture = System.Globalization.CultureInfo.InvariantCulture,
                StringFormat = "0.00"
            },
            Width = 120
        });

        var ayuda = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text =
                "La distancia se mide desde el paño. En un extremo es lo que mide el bastón; " +
                "al centro, es donde empieza, igual desde cada paño.\n" +
                "En el corte A-A' salen los bastones que cruza su línea (a L/4 + 5 cm)."
        };

        var agregar = new Button { Content = "Agregar", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var quitar = new Button { Content = "Quitar", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var aceptar = new Button { Content = "Aceptar", MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancelar = new Button { Content = "Cancelar", MinWidth = 80, IsCancel = true };

        agregar.Click += (_, _) =>
        {
            // El primero va arriba en los extremos, que es el bastón típico; el siguiente
            // abajo al centro, que es el otro. A partir de ahí, copia el último.
            var nuevo = copias.Count switch
            {
                0 => new BastonSeccion(),
                1 => new BastonSeccion
                {
                    Posicion = BastonSeccion.TextoInferior,
                    Ubicacion = BastonSeccion.TextoCentro
                },
                _ => copias[^1].Copia()
            };

            copias.Add(nuevo);
            tabla.SelectedItem = nuevo;
        };

        quitar.Click += (_, _) =>
        {
            if (tabla.SelectedItem is BastonSeccion b)
            {
                copias.Remove(b);
            }
        };

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        botones.Children.Add(agregar);
        botones.Children.Add(quitar);
        botones.Children.Add(aceptar);
        botones.Children.Add(cancelar);

        var raiz = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(ayuda, Dock.Top);
        DockPanel.SetDock(botones, Dock.Bottom);
        raiz.Children.Add(ayuda);
        raiz.Children.Add(botones);
        raiz.Children.Add(tabla);

        var ventana = new Window
        {
            Title = $"Bastones de \"{fila.Id}\"",
            Owner = this,
            Content = raiz,
            Width = 560,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };

        aceptar.Click += (_, _) =>
        {
            // Que la celda que se esté escribiendo cuente.
            tabla.CommitEdit(DataGridEditingUnit.Row, true);
            ventana.DialogResult = true;
        };

        if (ventana.ShowDialog() != true)
        {
            return;
        }

        fila.ReemplazarBastones(copias);

        StatusText.Text = copias.Count == 0
            ? $"\"{fila.Id}\" ya no lleva bastones."
            : $"\"{fila.Id}\" lleva {copias.Count} bastón(es). Se dibujan en su alzado y, si los cruza, en el corte A-A'.";

        DibujarVistaPrevia();
    }
}
