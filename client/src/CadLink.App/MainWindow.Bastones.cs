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
    ///   <item>Mitad de <b>arriba</b>: bastón superior; mitad de <b>abajo</b>: inferior.
    ///   Uno por lecho: si ya hay, se cambia.</item>
    ///   <item>La ubicación la da el tipo: en la trabe, arriba en ambos extremos y abajo
    ///   al centro; en la contratrabe, al revés.</item>
    ///   <item>La distancia es la del clic al paño más cercano.</item>
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

        // DOS FRANJAS: arriba y abajo. La ubicación la da el tipo, no el clic:
        //   trabe        arriba = ambos extremos, abajo = centro
        //   contratrabe  arriba = centro,         abajo = ambos extremos
        var posicion = p.Y < a.Top + (a.H / 2)
            ? BastonSeccion.TextoSuperior
            : BastonSeccion.TextoInferior;

        var ubicacion = BastonSeccion.UbicacionTexto(posicion, EsContratrabe(fila));

        // La distancia sale de dónde se hizo clic, medida desde el paño más cercano. En los
        // extremos es lo que mide cada bastón; al centro, donde empieza, igual de cada paño.
        var xm = (p.X - a.Izq) / a.Esc;
        var distancia = Math.Min(xm, a.Largo - xm);

        if (ubicacion == BastonSeccion.TextoCentro)
        {
            // Que quede bastón: al centro no puede empezar pasada la mitad.
            distancia = Math.Min(distancia, (a.Largo / 2) - (2 * PasoBastonM));
        }

        distancia = Math.Max(PasoBastonM, Math.Round(distancia / PasoBastonM) * PasoBastonM);

        var nuevo = new BastonSeccion
        {
            Posicion = posicion,
            Ubicacion = ubicacion,
            Cantidad = CantidadBastonElegida,
            Diametro = DiametroBastonElegido,
            DistanciaM = distancia
        };

        // UNO POR POSICIÓN: el clic en una franja que ya tiene bastón lo cambia, no añade
        // otro. Así nunca hay más de dos.
        var habia = fila.Bastones.Any(b => b.Posicion == posicion);
        fila.ReemplazarBastones(
            fila.Bastones.Where(b => b.Posicion != posicion).Append(nuevo).ToList());

        StatusText.Text =
            $"Bastón {nuevo.Cantidad} {nuevo.Diametro} {nuevo.Posicion.ToLowerInvariant()}, " +
            $"{nuevo.Ubicacion.ToLowerInvariant()}, a {distancia:0.00} m del paño" +
            (habia ? ", en lugar del que había. " : ". ") +
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

        var contratrabe = EsContratrabe(fila);
        var copias = new ObservableCollection<BastonSeccion>(fila.Bastones.Select(b => b.Copia()));

        // LA UBICACIÓN NO SE ELIGE: sale del lecho y del tipo. Se fija al abrir y se rehace
        // cada vez que se cambia el lecho de una fila.
        void FijarUbicacion(BastonSeccion b) =>
            b.Ubicacion = BastonSeccion.UbicacionTexto(b.Posicion, contratrabe);

        void Vigilar(BastonSeccion b)
        {
            FijarUbicacion(b);
            b.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(BastonSeccion.Posicion))
                {
                    FijarUbicacion(b);
                }
            };
        }

        foreach (var b in copias)
        {
            Vigilar(b);
        }

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

        tabla.Columns.Add(new DataGridTextColumn
        {
            Header = "Ubicación",
            Binding = new Binding(nameof(BastonSeccion.Ubicacion)) { Mode = BindingMode.OneWay },
            IsReadOnly = true,
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
                (contratrabe
                    ? "Contratrabe: abajo en ambos extremos y arriba al centro.\n"
                    : "Trabe: arriba en ambos extremos y abajo al centro.\n") +
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
            // Dos como mucho, uno por lecho: el nuevo toma el que quede libre.
            var libre = BastonSeccion.Posiciones.FirstOrDefault(
                pos => copias.All(b => b.Posicion != pos));

            if (libre is null)
            {
                // La ventana del botón: el cuadro todavía no tiene nombre en este punto.
                MessageBox.Show(Window.GetWindow(agregar)!,
                    "Ya hay un bastón arriba y otro abajo, que son los dos que caben.",
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var nuevo = new BastonSeccion();
            nuevo.Posicion = libre;
            Vigilar(nuevo);
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

            var repetida = copias.GroupBy(b => b.Posicion).FirstOrDefault(g => g.Count() > 1);
            if (repetida is not null)
            {
                MessageBox.Show(ventana,
                    $"Hay más de un bastón «{repetida.Key}». Va uno arriba y otro abajo.",
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
