using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CadLink.Cad;

namespace CadLink.App;

// ============================================================================
//  LA VENTANA «ESTILO DE DIBUJO»: LETRAS, COTAS Y COLORES
//
//  Un estilo por tipo de dibujo -a la izquierda- y sus ajustes -a la derecha-, agrupados en
//  Letras, Cotas y Colores de capa. Cada ajuste dice su valor por defecto, que es el que tenia el
//  programa, y se puede volver a el con un clic. Vale para toda la aplicacion: se guarda en el
//  equipo, no en el trabajo.
//
//  Se edita una COPIA del estilo actual: Cancelar no deja nada a medias.
// ============================================================================
public partial class MainWindow
{
    private void OnEstiloDibujo(object sender, RoutedEventArgs e)
    {
        var copia = EstiloDibujo.Actual.Copia();

        var lista = new ListBox
        {
            MinWidth = 230,
            Margin = new Thickness(0, 0, 12, 0),
            DisplayMemberPath = nameof(PerfilEstilo.Nombre),
            ItemsSource = copia.Perfiles
        };

        var titulo = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
        var descripcion = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 10)
        };

        var cuerpo = new StackPanel();
        var desplazable = new ScrollViewer
        {
            Content = cuerpo,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        void MostrarPerfil()
        {
            cuerpo.Children.Clear();

            if (lista.SelectedItem is not PerfilEstilo p)
            {
                return;
            }

            titulo.Text = p.Nombre;
            descripcion.Text = p.Descripcion;

            foreach (var grupo in p.Ajustes.GroupBy(a => a.Grupo))
            {
                cuerpo.Children.Add(new TextBlock
                {
                    Text = grupo.Key.ToUpperInvariant(),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, cuerpo.Children.Count == 0 ? 0 : 14, 0, 6)
                });

                var rejilla = new Grid();
                rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
                rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var fila = 0;

                foreach (var a in grupo)
                {
                    rejilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var nombre = new TextBlock
                    {
                        Text = a.Nombre,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 10, 3),
                        ToolTip = a.Ayuda.Length > 0 ? a.Ayuda : null
                    };

                    var (editor, poner) = EditorDeAjuste(a);

                    var defecto = new TextBlock
                    {
                        Text = "Por defecto: " + TextoDeValor(a, a.Defecto),
                        Foreground = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 6, 0)
                    };

                    var volver = new Button
                    {
                        Content = "↺",
                        Width = 28,
                        ToolTip = "Volver al valor por defecto",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    volver.Click += (_, _) => poner(a.Defecto);

                    Grid.SetRow(nombre, fila);
                    Grid.SetRow(editor, fila);
                    Grid.SetRow(defecto, fila);
                    Grid.SetRow(volver, fila);
                    Grid.SetColumn(editor, 1);
                    Grid.SetColumn(defecto, 2);
                    Grid.SetColumn(volver, 3);

                    rejilla.Children.Add(nombre);
                    rejilla.Children.Add(editor);
                    rejilla.Children.Add(defecto);
                    rejilla.Children.Add(volver);
                    fila++;
                }

                cuerpo.Children.Add(rejilla);
            }

            desplazable.ScrollToTop();
        }

        lista.SelectionChanged += (_, _) => MostrarPerfil();

        // ---------- Los botones ----------
        var restaurarEste = new Button { Content = "Restaurar este estilo", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        var restaurarTodo = new Button { Content = "Restaurar todos", Padding = new Thickness(10, 3, 10, 3) };
        var guardar = new Button { Content = "Guardar", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancelar = new Button { Content = "Cancelar", MinWidth = 90, IsCancel = true };

        restaurarEste.Click += (_, _) =>
        {
            if (lista.SelectedItem is PerfilEstilo p)
            {
                p.Restaurar();
                MostrarPerfil();
            }
        };

        restaurarTodo.Click += (_, _) =>
        {
            foreach (var p in copia.Perfiles)
            {
                p.Restaurar();
            }

            MostrarPerfil();
        };

        var izquierda = new StackPanel { Orientation = Orientation.Horizontal };
        izquierda.Children.Add(restaurarEste);
        izquierda.Children.Add(restaurarTodo);

        var derecha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        derecha.Children.Add(guardar);
        derecha.Children.Add(cancelar);

        var botones = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(izquierda, Dock.Left);
        botones.Children.Add(izquierda);
        botones.Children.Add(derecha);

        var cabecera = new StackPanel();
        cabecera.Children.Add(titulo);
        cabecera.Children.Add(descripcion);

        var lado = new DockPanel();
        DockPanel.SetDock(cabecera, Dock.Top);
        lado.Children.Add(cabecera);
        lado.Children.Add(desplazable);

        var centro = new DockPanel();
        DockPanel.SetDock(lista, Dock.Left);
        centro.Children.Add(lista);
        centro.Children.Add(lado);

        var raiz = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(botones, Dock.Bottom);
        raiz.Children.Add(botones);
        raiz.Children.Add(centro);

        var ventana = new Window
        {
            Title = "Estilo de dibujo: letras, cotas y colores",
            Owner = this,
            Content = raiz,
            Width = 1000,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };

        guardar.Click += (_, _) =>
        {
            var malos = copia.Perfiles
                .SelectMany(p => p.Ajustes.Select(a => (p, a, problema: a.Problema(a.Valor))))
                .Where(x => x.problema.Length > 0)
                .Select(x => $"  - {x.p.Nombre} › {x.a.Nombre}: {x.problema}.")
                .ToList();

            if (malos.Count > 0)
            {
                MessageBox.Show(ventana, "Corrige esto antes de guardar:\n\n" + string.Join("\n", malos),
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ventana.DialogResult = true;
        };

        lista.SelectedIndex = 0;

        if (ventana.ShowDialog() != true)
        {
            return;
        }

        // Desde ya, todos los dibujos salen con el estilo nuevo.
        EstiloDibujo.Actual = copia;

        var error = EstiloDibujoArchivo.Guardar(copia);
        var cambios = copia.ParaGuardar().Sum(p => p.Value.Count);

        StatusText.Text = error.Length == 0
            ? (cambios == 0
                ? "Estilo de dibujo: todo con los valores por defecto."
                : $"Estilo de dibujo guardado: {cambios} ajuste(s) distintos del valor por defecto.")
            : "El estilo se usará en esta sesión, pero no se pudo guardar: " + error;

        // Las vistas previas que leen el estilo.
        DibujarVistaPreviaMuro();
    }

    /// <summary>Lo que se ve de un valor: la marca con su nombre, lo demas tal cual.</summary>
    private static string TextoDeValor(AjusteEstilo a, string valor)
    {
        if (a.Tipo == TipoAjuste.Marca)
        {
            var m = EstiloDibujo.Marcas.FirstOrDefault(x => string.Equals(x.Bloque, valor, StringComparison.OrdinalIgnoreCase));
            return m is null ? valor : m.Nombre;
        }

        return valor;
    }

    /// <summary>
    /// El editor de un ajuste, segun su tipo, y como ponerle un valor desde fuera -el boton de
    /// volver al defecto-. Cada cambio se escribe enseguida en el ajuste de la copia.
    /// </summary>
    private (FrameworkElement Editor, Action<string> Poner) EditorDeAjuste(AjusteEstilo a)
    {
        switch (a.Tipo)
        {
            case TipoAjuste.Fuente:
            {
                var cb = new ComboBox
                {
                    IsEditable = true,
                    ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList(),
                    Text = a.Valor,
                    Margin = new Thickness(0, 2, 0, 2),
                    ToolTip = "Elige una fuente instalada o escribe su nombre."
                };
                cb.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => a.Valor = cb.Text));
                cb.SelectionChanged += (_, _) =>
                {
                    if (cb.SelectedItem is string f)
                    {
                        a.Valor = f;
                    }
                };
                return (cb, v => { cb.Text = v; a.Valor = v; });
            }

            case TipoAjuste.Marca:
            {
                var cb = new ComboBox
                {
                    ItemsSource = EstiloDibujo.Marcas,
                    DisplayMemberPath = nameof(MarcaCota.Nombre),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                void Elegir(string v) => cb.SelectedItem = EstiloDibujo.Marcas.FirstOrDefault(
                    m => string.Equals(m.Bloque, v, StringComparison.OrdinalIgnoreCase));

                Elegir(a.Valor);
                cb.SelectionChanged += (_, _) =>
                {
                    if (cb.SelectedItem is MarcaCota m)
                    {
                        a.Valor = m.Bloque;
                    }
                };
                return (cb, v => { Elegir(v); a.Valor = v; });
            }

            case TipoAjuste.ColorAci:
            {
                var tb = new TextBox { Text = a.Valor, Width = 60, VerticalContentAlignment = VerticalAlignment.Center };
                var muestra = new Border
                {
                    Width = 22,
                    Height = 18,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(6, 0, 6, 0)
                };
                var elegir = new Button { Content = "Elegir…", Padding = new Thickness(8, 1, 8, 1) };

                void Pintar()
                {
                    var ok = a.Problema(tb.Text).Length == 0;
                    tb.BorderBrush = ok ? Brushes.Gray : Brushes.Red;
                    tb.ToolTip = ok ? null : a.Problema(tb.Text);

                    if (ok && int.TryParse(tb.Text.Trim(), out var aci) && aci >= 1 && aci <= 255)
                    {
                        muestra.Background = PincelAci(aci);
                        muestra.ToolTip = $"Color {aci}";
                    }
                    else
                    {
                        muestra.Background = Brushes.Transparent;
                        muestra.ToolTip = tb.Text.Trim() == "0" ? "0 = por bloque / sin color propio"
                            : tb.Text.Trim() == "256" ? "256 = por capa" : null;
                    }
                }

                tb.TextChanged += (_, _) => { a.Valor = tb.Text; Pintar(); };
                elegir.Click += (_, _) =>
                {
                    if (ElegirColorAci(Window.GetWindow(elegir)!, tb.Text) is int c)
                    {
                        tb.Text = c.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                };
                Pintar();

                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                panel.Children.Add(tb);
                panel.Children.Add(muestra);
                panel.Children.Add(elegir);
                return (panel, v => tb.Text = v);
            }

            default:
            {
                var tb = new TextBox { Text = a.Valor, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
                tb.TextChanged += (_, _) =>
                {
                    a.Valor = tb.Text;
                    var problema = a.Problema(tb.Text);
                    tb.BorderBrush = problema.Length == 0 ? Brushes.Gray : Brushes.Red;
                    tb.ToolTip = problema.Length == 0 ? null : problema;
                };
                return (tb, v => tb.Text = v);
            }
        }
    }

    /// <summary>El pincel de un color ACI, con la rueda de <see cref="EstiloDibujo.Rgb"/>.</summary>
    private static SolidColorBrush PincelAci(int aci)
    {
        var (r, g, b) = EstiloDibujo.Rgb(aci);
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    /// <summary>
    /// La paleta de AutoCAD: los 255 colores en su orden -los nueve basicos, la rueda en 24
    /// columnas de tono y los grises-. Devuelve el elegido, o null si se cierra.
    /// </summary>
    private static int? ElegirColorAci(Window duena, string actual)
    {
        int? elegido = null;

        var ventana = new Window
        {
            Title = "Color de AutoCAD",
            Owner = duena,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize
        };

        var info = new TextBlock { Text = "Color actual: " + actual, Margin = new Thickness(0, 8, 0, 0) };

        Button Muestra(int aci)
        {
            var b = new Button
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Background = PincelAci(aci),
                BorderBrush = Brushes.Gray,
                ToolTip = $"Color {aci}"
            };
            b.MouseEnter += (_, _) => info.Text = $"Color {aci}";
            b.Click += (_, _) => { elegido = aci; ventana.DialogResult = true; };
            return b;
        }

        var raiz = new StackPanel { Margin = new Thickness(12) };

        var basicos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 1; i <= 9; i++) { basicos.Children.Add(Muestra(i)); }
        raiz.Children.Add(basicos);

        // La rueda: el tono va por columnas (10, 20, … 240) y el brillo por renglones (+0 … +9).
        var rueda = new UniformGrid { Columns = 24, Rows = 10 };
        for (var fila = 0; fila < 10; fila++)
        {
            for (var col = 0; col < 24; col++)
            {
                rueda.Children.Add(Muestra(10 + (col * 10) + fila));
            }
        }
        raiz.Children.Add(rueda);

        var grises = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 250; i <= 255; i++) { grises.Children.Add(Muestra(i)); }
        raiz.Children.Add(grises);

        raiz.Children.Add(info);
        ventana.Content = raiz;

        return ventana.ShowDialog() == true ? elegido : null;
    }
}
