using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CadLink.Revit;

/// <summary>
/// El cuadro corto de los botones de placas, cimentacion y soldadura: que fila de la tabla y un
/// par de opciones.
/// </summary>
/// <remarks>
/// <para>
/// Va armado en codigo y no en XAML porque son tres o cuatro renglones, distintos en cada boton:
/// un XAML por boton seria repetir la misma ventana tres veces.
/// </para>
/// <para>
/// Este archivo NO usa <c>Autodesk.Revit.UI</c> a proposito: ahi tambien hay un TextBox, un
/// ComboBox y un CheckBox -los de la cinta-, y con los dos usings el nombre es ambiguo (CS0104).
/// </para>
/// </remarks>
internal sealed class VentanaDetalle : Window
{
    private readonly StackPanel _renglones = new() { Margin = new Thickness(16) };

    public VentanaDetalle(string titulo, string explicacion)
    {
        Title = titulo;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _renglones.Children.Add(new TextBlock
        {
            Text = explicacion,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        Content = _renglones;
    }

    /// <summary>Una lista desplegable con la primera opcion elegida.</summary>
    public ComboBox Lista(string etiqueta, IEnumerable<string> opciones)
    {
        Etiqueta(etiqueta);

        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };

        foreach (var o in opciones)
        {
            combo.Items.Add(o);
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        _renglones.Children.Add(combo);

        return combo;
    }

    public CheckBox Casilla(string texto, bool valor, string ayuda = "")
    {
        var casilla = new CheckBox
        {
            Content = texto,
            IsChecked = valor,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = ayuda.Length == 0 ? null : ayuda
        };

        _renglones.Children.Add(casilla);

        return casilla;
    }

    public TextBox Numero(string etiqueta, double valor)
    {
        Etiqueta(etiqueta);

        var caja = new TextBox
        {
            Text = valor.ToString("0.##", CultureInfo.CurrentCulture),
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };

        _renglones.Children.Add(caja);

        return caja;
    }

    /// <summary>Lee un numero escrito con punto o con coma decimal.</summary>
    public static double Leer(TextBox caja, double siNoSirve)
    {
        var t = (caja.Text ?? string.Empty).Trim().Replace(',', '.');

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : siNoSirve;
    }

    /// <summary>Pone los botones y ensena el cuadro. True si se pulso Aceptar.</summary>
    public bool Mostrar(IntPtr? dueno, string aceptar)
    {
        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var si = new Button { Content = aceptar, IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        si.Click += (_, _) => DialogResult = true;

        var no = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90 };

        botones.Children.Add(si);
        botones.Children.Add(no);
        _renglones.Children.Add(botones);

        try
        {
            if (dueno is not null)
            {
                new WindowInteropHelper(this).Owner = dueno.Value;
            }
        }
        catch (Exception)
        {
            // Cosmetico: sin dueno la ventana se ensena igual.
        }

        return ShowDialog() == true;
    }

    private void Etiqueta(string texto) =>
        _renglones.Children.Add(new TextBlock { Text = texto, Margin = new Thickness(0, 0, 0, 3) });
}
