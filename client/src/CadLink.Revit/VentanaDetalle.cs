using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

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
    private readonly StackPanel _renglones = new() { Margin = new Thickness(16), Width = 480 };

    /// <summary>La columna de la derecha: los croquis con las letras de cada renglon.</summary>
    private readonly StackPanel _croquis = new() { Margin = new Thickness(0, 16, 16, 16) };

    public VentanaDetalle(string titulo, string explicacion)
    {
        Title = titulo;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _renglones.Children.Add(new TextBlock
        {
            Text = explicacion,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Los renglones con su barra, por si el cuadro no cabe en pantalla; el croquis a la derecha,
        // siempre a la vista, para saber que es cada letra mientras se escribe.
        var raiz = new Grid();
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var barra = new ScrollViewer { Content = _renglones, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(barra, 0);
        Grid.SetColumn(_croquis, 1);
        raiz.Children.Add(barra);
        raiz.Children.Add(_croquis);

        Content = raiz;
    }

    /// <summary>
    /// Un croquis de guia, a la derecha: lo que mide cada letra de los renglones.
    /// </summary>
    public void Guia(string titulo, double ancho, double alto, Action<Croquis> dibujar)
    {
        _croquis.Children.Add(new TextBlock
        {
            Text = titulo,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var lienzo = new Canvas { Width = ancho, Height = alto, Background = Brushes.White, ClipToBounds = true };

        dibujar(new Croquis(lienzo));

        _croquis.Children.Add(new Border
        {
            Child = lienzo,
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        });
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

/// <summary>
/// Lo minimo para dibujar un croquis de guia: rectangulos, lineas, cotas y la LETRA en un circulo
/// que corresponde a cada renglon del cuadro.
/// </summary>
internal sealed class Croquis
{
    public static readonly Brush Acero = Pincel(150, 172, 196);
    public static readonly Brush AceroOscuro = Pincel(80, 104, 132);
    public static readonly Brush Placa = Pincel(70, 130, 200);
    public static readonly Brush Concreto = Pincel(212, 212, 206);
    public static readonly Brush Grout = Pincel(205, 180, 120);
    public static readonly Brush Soldadura = Pincel(30, 30, 30);
    public static readonly Brush Varilla = Pincel(200, 50, 50);
    public static readonly Brush Rojo = Pincel(190, 40, 40);
    public static readonly Brush Gris = Pincel(60, 60, 60);

    private readonly Canvas _c;

    public Croquis(Canvas c) => _c = c;

    private static Brush Pincel(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    private void Poner(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _c.Children.Add(e);
    }

    public void Rect(double x, double y, double w, double h, Brush relleno, bool punteado = false)
    {
        var r = new Rectangle
        {
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Fill = relleno,
            Stroke = Brushes.DimGray,
            StrokeThickness = 0.8
        };

        if (punteado)
        {
            r.StrokeDashArray = new DoubleCollection { 4, 3 };
        }

        Poner(r, x, y);
    }

    public void Linea(double x1, double y1, double x2, double y2, Brush color, double grueso = 1, bool punteado = false)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = grueso };

        if (punteado)
        {
            l.StrokeDashArray = new DoubleCollection { 4, 3 };
        }

        _c.Children.Add(l);
    }

    public void Circulo(double cx, double cy, double r, Brush relleno) =>
        Poner(new Ellipse { Width = 2 * r, Height = 2 * r, Fill = relleno, Stroke = Brushes.DimGray, StrokeThickness = 0.6 },
            cx - r, cy - r);

    /// <summary>El filete: un triangulo con el angulo recto en (x, y).</summary>
    public void Filete(double x, double y, double dx, double dy) =>
        _c.Children.Add(new Polygon
        {
            Points = new PointCollection { new Point(x, y), new Point(x + dx, y), new Point(x, y + dy) },
            Fill = Soldadura
        });

    public void Texto(double x, double y, string t, double tam = 10)
    {
        Poner(new TextBlock { Text = t, FontSize = tam, Foreground = Gris }, x, y);
    }

    /// <summary>La letra de un renglon, en su circulo rojo.</summary>
    public void Letra(double cx, double cy, string letra)
    {
        Poner(new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, Stroke = Rojo, StrokeThickness = 1.2 },
            cx - 8, cy - 8);

        var t = new TextBlock { Text = letra, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Rojo };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Poner(t, cx - (t.DesiredSize.Width / 2), cy - (t.DesiredSize.Height / 2));
    }

    /// <summary>Una cota de (x1, y1) a (x2, y2), con sus marcas y la letra al centro.</summary>
    public void Cota(double x1, double y1, double x2, double y2, string letra)
    {
        Linea(x1, y1, x2, y2, Rojo, 0.9);

        var dx = x2 - x1;
        var dy = y2 - y1;
        var l = Math.Sqrt((dx * dx) + (dy * dy));

        if (l > 1e-6)
        {
            // Marcas a 45 grados, como las de AutoCAD.
            var (ux, uy) = (dx / l, dy / l);
            var (mx, my) = ((ux - uy) * 3, (uy + ux) * 3);
            Linea(x1 - mx, y1 - my, x1 + mx, y1 + my, Rojo, 1.2);
            Linea(x2 - mx, y2 - my, x2 + mx, y2 + my, Rojo, 1.2);
        }

        Letra((x1 + x2) / 2, (y1 + y2) / 2, letra);
    }

    /// <summary>Una flecha de la letra a lo que senala.</summary>
    public void Llamada(double x, double y, double lx, double ly, string letra)
    {
        Linea(lx, ly, x, y, Rojo, 0.9);
        Circulo(x, y, 1.8, Rojo);
        Letra(lx, ly, letra);
    }
}

