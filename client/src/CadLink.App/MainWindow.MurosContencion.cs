using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La hoja de <b>muros de contencion</b>: de concreto armado y de concreto ciclopeo.
/// </summary>
/// <remarks>
/// <para>
/// Dos tablas, una por tipo, porque piden datos distintos: el de concreto armado los del muro
/// en voladizo con su espolon y su acero; el ciclopeo las letras de su dibujo -h, d, c, b, M, E,
/// G y N-. Comparten la vista previa y el boton de dibujar.
/// </para>
/// <para>
/// La vista previa pinta el MISMO <see cref="DibujoMuro"/> que manda a AutoCAD
/// <see cref="ZapataDrawer.DibujarMuros"/>: los dos salen de <see cref="TrazoMuroContencion"/>.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Se llama una vez, en el constructor.</summary>
    private void EngancharVistaPreviaMuros()
    {
        MurosPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaMuro();
        MurosArmadosGrid.SelectionChanged += (_, _) => DibujarVistaPreviaMuro();
        MurosCiclopeosGrid.SelectionChanged += (_, _) => DibujarVistaPreviaMuro();
        MurosTabs.SelectionChanged += (s, e) =>
        {
            // Solo los cambios de la pestaña, no los que suben de las celdas de dentro.
            if (ReferenceEquals(e.OriginalSource, MurosTabs))
            {
                DibujarVistaPreviaMuro();
            }
        };
    }

    /// <summary>Se llama cada vez que cambian los datos del proyecto.</summary>
    private void EnlazarMuros()
    {
        MurosArmadosGrid.ItemsSource = _datos.MurosArmados;
        MurosCiclopeosGrid.ItemsSource = _datos.MurosCiclopeos;

        Enganchar(_datos.MurosArmados);
        Enganchar(_datos.MurosCiclopeos);

        ActualizarTotalesMuros();
        DibujarVistaPreviaMuro();
    }

    private void Enganchar<T>(System.Collections.ObjectModel.ObservableCollection<T> lista)
        where T : INotifyPropertyChanged
    {
        lista.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (INotifyPropertyChanged f in e.OldItems) { f.PropertyChanged -= OnFilaMuroEditada; }
            }

            if (e.NewItems is not null)
            {
                foreach (INotifyPropertyChanged f in e.NewItems) { f.PropertyChanged += OnFilaMuroEditada; }
            }

            ActualizarTotalesMuros();
            DibujarVistaPreviaMuro();
        };

        foreach (var f in lista)
        {
            f.PropertyChanged -= OnFilaMuroEditada;
            f.PropertyChanged += OnFilaMuroEditada;
        }
    }

    private void OnFilaMuroEditada(object? sender, PropertyChangedEventArgs e)
    {
        ActualizarTotalesMuros();

        if (ReferenceEquals(sender, MurosArmadosGrid.SelectedItem)
            || ReferenceEquals(sender, MurosCiclopeosGrid.SelectedItem))
        {
            DibujarVistaPreviaMuro();
        }
    }

    private void ActualizarTotalesMuros()
    {
        var a = _datos.MurosArmados.Count;
        var c = _datos.MurosCiclopeos.Count;
        var incompletos = _datos.MurosArmados.Count(f => f.Falta.Length > 0)
                          + _datos.MurosCiclopeos.Count(f => f.Falta.Length > 0);

        TotalesMurosText.Text =
            $"{a} muro(s) de concreto armado   ·   {c} de concreto ciclópeo"
            + (incompletos == 0 ? "   ·   todos completos" : $"   ·   {incompletos} incompleto(s)");
    }

    /// <summary>La fila que se ve: la seleccionada de la pestaña que esté abierta.</summary>
    private MuroContencionCad? MuroSeleccionado(out string falta)
    {
        falta = string.Empty;

        if (MurosTabs.SelectedIndex == 1)
        {
            if (MurosCiclopeosGrid.SelectedItem is MuroCiclopeoRow c)
            {
                falta = c.Falta;
                return c.AFormatoCad();
            }

            return null;
        }

        if (MurosArmadosGrid.SelectedItem is MuroArmadoRow a)
        {
            falta = a.Falta;
            return a.AFormatoCad();
        }

        return null;
    }

    // ======================================================================
    //  Revisar y dibujar
    // ======================================================================

    /// <summary>¿Esta abierta la pestaña del ciclopeo? Si no, la del concreto armado.</summary>
    private bool EnPestanaCiclopeo => MurosTabs.SelectedIndex == 1;

    /// <summary>Lo que dice la pestaña abierta, para los avisos.</summary>
    private string NombrePestanaMuros => EnPestanaCiclopeo ? "de concreto ciclópeo" : "de concreto armado";

    /// <summary>Cuantos muros tiene la pestaña abierta.</summary>
    private int MurosDeLaPestana =>
        EnPestanaCiclopeo ? _datos.MurosCiclopeos.Count : _datos.MurosArmados.Count;

    /// <summary>
    /// Los muros de la pestaña ABIERTA, nada mas: dibujar en la de concreto armado dibuja solo
    /// los de concreto armado, y en la de ciclopeo solo los ciclopeos.
    /// </summary>
    private List<MuroContencionCad> MurosParaDibujar() =>
        EnPestanaCiclopeo
            ? _datos.MurosCiclopeos.Select(f => f.AFormatoCad()).ToList()
            : _datos.MurosArmados.Select(f => f.AFormatoCad()).ToList();

    /// <summary>Revisa solo la pestaña abierta, que es la que se va a dibujar.</summary>
    private List<string> RevisarMuros()
    {
        var problemas = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Una(string hoja, int renglon, MuroContencionCad m)
        {
            var id = m.Id.Trim();

            foreach (var p in TrazoMuroContencion.Problemas(m))
            {
                problemas.Add($"  - {hoja}, renglón {renglon} «{id}»: {p}.");
            }

            if (id.Length > 0 && !ids.Add(id))
            {
                problemas.Add($"  - {hoja}, renglón {renglon}: el ID «{id}» está repetido.");
            }
        }

        if (EnPestanaCiclopeo)
        {
            for (var i = 0; i < _datos.MurosCiclopeos.Count; i++)
            {
                Una("Ciclópeo", i + 1, _datos.MurosCiclopeos[i].AFormatoCad());
            }
        }
        else
        {
            for (var i = 0; i < _datos.MurosArmados.Count; i++)
            {
                Una("Concreto armado", i + 1, _datos.MurosArmados[i].AFormatoCad());
            }
        }

        return problemas;
    }

    private void OnRevisarMuros(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        if (MurosDeLaPestana == 0)
        {
            MessageBox.Show($"No hay muros {NombrePestanaMuros} capturados en esta pestaña.", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var problemas = RevisarMuros();

        MessageBox.Show(
            problemas.Count == 0
                ? $"Los muros {NombrePestanaMuros} están completos."
                : $"Hay {problemas.Count} cosa(s) que corregir:\n\n" + string.Join("\n", problemas),
            AppInfo.ProductName, MessageBoxButton.OK,
            problemas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnExportMuros(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MurosDeLaPestana == 0)
        {
            MessageBox.Show($"No hay muros {NombrePestanaMuros} capturados en esta pestaña.", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var problemas = RevisarMuros();

        if (problemas.Count > 0)
        {
            MessageBox.Show(
                "Corrige esto antes de dibujar los muros:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            // Con el tipo escrito: con 'doc' dynamic, el delegado no se puede inferir (CS1976).
            Func<string?, double> catalogoDeVarillas = DiametroCmDeVarilla;

            ZapataDrawer dibujante = new ZapataDrawer(doc, catalogoDeVarillas)
            {
                SeccionRellena = ModoElegido == ModoSeccion.Tipo2Rellena
            };

            // Solo los de la pestaña abierta.
            var muros = MurosParaDibujar();

            var r = dibujante.DibujarMuros(muros);

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var fallos = dibujante.Fallos;
            var nl = Environment.NewLine;

            if (fallos.Count == 0)
            {
                StatusText.Text = $"Dibujados {r.Armados + r.Ciclopeos} muro(s) {NombrePestanaMuros} en AutoCAD.";

                MostrarNotas(dibujante.Notas.Count == 0
                    ? string.Empty
                    : "Notas del último dibujo:" + nl + string.Join(nl, dibujante.Notas.Select(n => "  - " + n)));

                MessageBox.Show("Listo.\n\n" + r, AppInfo.ProductName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var detalle = string.Join(nl, fallos.Select(f => "  - " + f));

                StatusText.Text = $"Muros dibujados con {fallos.Count} aviso(s). Ver el detalle bajo la vista previa.";
                MostrarNotas("AVISOS DEL ULTIMO DIBUJO (" + fallos.Count + "):" + nl + detalle);

                MessageBox.Show(r + "\n\nPERO hubo " + fallos.Count + " fallo(s) que se toleraron:\n\n" + detalle,
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (AcadBusyException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error al dibujar los muros en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ======================================================================
    //  La vista previa
    // ======================================================================

    private static readonly Brush TintaMuroConcreto = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
    private static readonly Brush TintaMuroAcero = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    /// <summary>
    /// El color de la capa VAR_ de cada diametro: el de «Estilo de dibujo» -perfil «Capas
    /// compartidas»-, pasado a RGB. Lo que se ve en la vista previa es lo que sale en AutoCAD.
    /// </summary>
    private static Brush TintaDeVarilla(string? clave)
    {
        var aci = EstiloDibujo.Actual.ColorDeVarilla(clave);
        return aci >= 1 && aci <= 255 ? PincelAci(aci) : TintaMuroAcero;
    }

    private static readonly Brush TintaMuroCota = new SolidColorBrush(Color.FromRgb(0x70, 0x78, 0x82));
    private static readonly Brush TintaMuroRotulo = new SolidColorBrush(Color.FromRgb(0x1D, 0x7A, 0x3E));
    private static readonly Brush TintaMuroTexto = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x33));
    private static readonly Brush RellenoPiedra = new SolidColorBrush(Color.FromRgb(0xE4, 0xE0, 0xD6));
    private static readonly Brush TintaPiedra = new SolidColorBrush(Color.FromRgb(0x7D, 0x76, 0x6A));

    private void DibujarVistaPreviaMuro()
    {
        var lienzo = MurosPreviewCanvas;
        lienzo.Children.Clear();

        var ancho = lienzo.ActualWidth;
        var alto = lienzo.ActualHeight;

        if (ancho < 120 || alto < 120)
        {
            return;
        }

        var m = MuroSeleccionado(out var falta);

        if (m is null)
        {
            TextoMuro("Selecciona un muro de la tabla para verlo.", 14, 34, 12, Brushes.Gray);
            return;
        }

        if (falta.Length > 0)
        {
            TextoMuro($"«{m.Id}» todavía no se puede dibujar: {falta}.", 14, 34, 12, Brushes.Gray);
            return;
        }

        var d = TrazoMuroContencion.Dibujar(m, 0, 0);

        // ---------- La escala: que quepa todo, con margen ----------
        const double margen = 14;
        const double arriba = 30;

        var w = d.XMax - d.XMin;
        var h = d.YMax - d.YMin;
        var esc = Math.Min((ancho - (2 * margen)) / w, (alto - arriba - margen) / h);

        if (esc <= 0 || double.IsInfinity(esc))
        {
            return;
        }

        var offX = margen + (((ancho - (2 * margen)) - (w * esc)) / 2);
        var offY = arriba + (((alto - arriba - margen) - (h * esc)) / 2);

        double PX(double x) => offX + ((x - d.XMin) * esc);
        double PY(double y) => offY + ((d.YMax - y) * esc);

        Point Pt((double X, double Y) p) => new(PX(p.X), PY(p.Y));

        // El tamaño de letra sigue a la escala, con tope para que no se pierda ni se coma el dibujo.
        double Letra(double altoM) => Math.Clamp(altoM * esc * 1.25, 7, 16);

        // ---------- Terreno ----------
        foreach (var t in d.Terreno)
        {
            lienzo.Children.Add(new Polygon
            {
                Points = new PointCollection(t.Select(q => Pt(q))),
                Fill = PincelTerreno,
                Stroke = null
            });
        }

        // ---------- Concreto ----------
        foreach (var c in d.Concreto)
        {
            var pol = new Polygon
            {
                Points = new PointCollection(c.Select(q => Pt(q))),
                Fill = m.EsCiclopeo ? RellenoPiedra : PincelConcreto,
                Stroke = TintaMuroConcreto,
                StrokeThickness = 1.4
            };

            lienzo.Children.Add(pol);

            if (m.EsCiclopeo)
            {
                PiedrasDelCiclopeo(lienzo, pol.Points);
            }
        }

        foreach (var l in d.Lineas)
        {
            lienzo.Children.Add(TrazoMuroPrevio(l.Puntos.Select(q => Pt(q)), l.Cerrada, TintaMuroConcreto, 1.2, false));
        }

        // ---------- Acero ----------
        // Con su DIAMETRO REAL a dos lineas -el mismo contorno que va a AutoCAD- y el color de la
        // capa de su diametro. Si a esta escala las dos caras se juntan, se ve como una linea.
        foreach (var v in d.Varillas)
        {
            var contorno = TrazoMuroContencion.ContornoVarilla(v.Puntos, TrazoMuroContencion.DiametroM(m, v.Clave));
            lienzo.Children.Add(TrazoMuroPrevio(
                contorno.Select(q => Pt(q)), contorno.Count > v.Puntos.Count, TintaDeVarilla(v.Clave), 0.8, v.Oculta));
        }

        foreach (var p in d.Puntos)
        {
            var r = Math.Max(p.R * esc, 1.2);
            var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = TintaDeVarilla(p.Clave) };
            Canvas.SetLeft(e, PX(p.X) - r);
            Canvas.SetTop(e, PY(p.Y) - r);
            lienzo.Children.Add(e);
        }

        // ---------- Cotas ----------
        foreach (var c in d.Cotas)
        {
            CotaMuroPrevia(lienzo, c, PX, PY, Letra(TrazoMuroContencion.AltoCotaMuro));
        }

        // ---------- Llamadas ----------
        var letraRotulo = Letra(TrazoMuroContencion.AltoRotulo);

        // Flecha -> codo -> hombro, y el texto ENCIMA del hombro: ninguna linea lo cruza. El
        // texto se mide, para que el hombro acabe donde acaba y las llamadas de la izquierda
        // terminen todas en su codo, alineadas.
        foreach (var t in d.Rotulos)
        {
            var tb = new TextBlock { Text = t.Texto, FontSize = letraRotulo, Foreground = TintaMuroRotulo };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var xCodo = PX(t.XCodo);
            var yCodo = PY(t.YCodo);
            var aLaIzquierda = t.XHombro < t.XCodo;

            var xTexto = aLaIzquierda ? xCodo - 2 - tb.DesiredSize.Width : xCodo + 2;
            var xHombro = aLaIzquierda ? xTexto - 2 : xTexto + tb.DesiredSize.Width + 2;

            lienzo.Children.Add(TrazoMuroPrevio(
                new[] { new Point(PX(t.XPunta), PY(t.YPunta)), new Point(xCodo, yCodo), new Point(xHombro, yCodo) },
                false, TintaMuroRotulo, 0.9, false));

            var punta = new Ellipse { Width = 4, Height = 4, Fill = TintaMuroRotulo };
            Canvas.SetLeft(punta, PX(t.XPunta) - 2);
            Canvas.SetTop(punta, PY(t.YPunta) - 2);
            lienzo.Children.Add(punta);

            Canvas.SetLeft(tb, xTexto);
            Canvas.SetTop(tb, yCodo - tb.DesiredSize.Height);
            lienzo.Children.Add(tb);
        }

        // ---------- Textos centrados ----------
        foreach (var t in d.Textos)
        {
            var tb = new TextBlock
            {
                Text = t.Texto,
                FontSize = Letra(t.Alto),
                FontWeight = t.Alto >= TrazoMuroContencion.AltoSubtitulo ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = TintaMuroTexto
            };

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, PX(t.X) - (tb.DesiredSize.Width / 2));
            Canvas.SetTop(tb, PY(t.Y) - (tb.DesiredSize.Height / 2));
            lienzo.Children.Add(tb);
        }

        TextoMuro(
            m.EsCiclopeo
                ? $"MURO CICLÓPEO  {m.Id}   ·   base {m.BaseCiclopeoM:0.00} m   ·   h {m.AlturaM:0.00} m"
                : $"MURO DE CONCRETO ARMADO  {m.Id}   ·   base {m.BaseM:0.00} m   ·   talón {m.TalonM:0.00} m"
                  + (m.Espolon ? "   ·   con espolón" : "   ·   sin espolón"),
            12, 8, 11.5, TintaMuroConcreto, true);
    }

    /// <summary>
    /// Las piedras del ciclopeo: contornos irregulares repartidos dentro del cuerpo. Con semilla
    /// fija, para que no salten de sitio en cada redibujado.
    /// </summary>
    private static void PiedrasDelCiclopeo(Canvas lienzo, PointCollection contorno)
    {
        var minX = contorno.Min(p => p.X);
        var maxX = contorno.Max(p => p.X);
        var minY = contorno.Min(p => p.Y);
        var maxY = contorno.Max(p => p.Y);
        var lado = Math.Max(6, Math.Min(maxX - minX, maxY - minY) / 7);

        var forma = new PathGeometry();
        var fig = new PathFigure { StartPoint = contorno[0], IsClosed = true };
        foreach (var p in contorno.Skip(1)) { fig.Segments.Add(new LineSegment(p, false)); }
        forma.Figures.Add(fig);

        var rnd = new Random(20261002);

        for (var y = minY + (lado / 2); y < maxY; y += lado * 0.9)
        {
            for (var x = minX + (lado / 2); x < maxX; x += lado)
            {
                var cx = x + ((rnd.NextDouble() - 0.5) * lado * 0.4);
                var cy = y + ((rnd.NextDouble() - 0.5) * lado * 0.4);

                if (!forma.FillContains(new Point(cx, cy)))
                {
                    continue;
                }

                var r = lado * (0.30 + (rnd.NextDouble() * 0.12));
                var piedra = new Polygon { Stroke = TintaPiedra, StrokeThickness = 0.9, Fill = Brushes.Transparent };

                for (var k = 0; k < 7; k++)
                {
                    var a = (k / 7.0 * 2 * Math.PI) + (rnd.NextDouble() * 0.5);
                    var rr = r * (0.75 + (rnd.NextDouble() * 0.35));
                    piedra.Points.Add(new Point(cx + (rr * Math.Cos(a)), cy + (rr * 0.8 * Math.Sin(a))));
                }

                // Recortada al cuerpo: una piedra no asoma por la cara del muro.
                piedra.Clip = forma;
                lienzo.Children.Add(piedra);
            }
        }
    }

    private static Polyline TrazoMuroPrevio(IEnumerable<Point> puntos, bool cerrada, Brush tinta, double grosor, bool trazos)
    {
        var pc = new PointCollection(puntos);

        if (cerrada && pc.Count > 0)
        {
            pc.Add(pc[0]);
        }

        var pl = new Polyline
        {
            Points = pc,
            Stroke = tinta,
            StrokeThickness = grosor,
            StrokeLineJoin = PenLineJoin.Round
        };

        if (trazos)
        {
            pl.StrokeDashArray = new DoubleCollection { 5, 3 };
        }

        return pl;
    }

    /// <summary>Una cota: linea, remates, lineas de extension y el numero medido.</summary>
    private static void CotaMuroPrevia(
        Canvas lienzo, TrazoCota c, Func<double, double> px, Func<double, double> py, double letra)
    {
        var largo = c.Vertical ? Math.Abs(c.Y2 - c.Y1) : Math.Abs(c.X2 - c.X1);

        if (largo < 1e-6)
        {
            return;
        }

        void L(double x1, double y1, double x2, double y2, double grosor) =>
            lienzo.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = TintaMuroCota, StrokeThickness = grosor
            });

        var texto = largo.ToString("0.00", CultureInfo.InvariantCulture);
        var tb = new TextBlock { Text = texto, FontSize = letra, Foreground = TintaMuroCota };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        if (c.Vertical)
        {
            var x = px(c.Xt);
            var y1 = py(c.Y1);
            var y2 = py(c.Y2);

            L(px(c.X1), y1, x + 3, y1, 0.6);
            L(px(c.X2), y2, x + 3, y2, 0.6);
            L(x, y1, x, y2, 0.8);
            L(x - 3, y1 + 3, x + 3, y1 - 3, 0.8);
            L(x - 3, y2 + 3, x + 3, y2 - 3, 0.8);

            tb.RenderTransform = new RotateTransform(-90);
            Canvas.SetLeft(tb, x - tb.DesiredSize.Height - 1);
            Canvas.SetTop(tb, ((y1 + y2) / 2) + (tb.DesiredSize.Width / 2));
        }
        else
        {
            var y = py(c.Yt);
            var x1 = px(c.X1);
            var x2 = px(c.X2);

            L(x1, py(c.Y1), x1, y + 3, 0.6);
            L(x2, py(c.Y2), x2, y + 3, 0.6);
            L(x1, y, x2, y, 0.8);
            L(x1 - 3, y + 3, x1 + 3, y - 3, 0.8);
            L(x2 - 3, y + 3, x2 + 3, y - 3, 0.8);

            Canvas.SetLeft(tb, ((x1 + x2) / 2) - (tb.DesiredSize.Width / 2));
            Canvas.SetTop(tb, y - tb.DesiredSize.Height - 1);
        }

        lienzo.Children.Add(tb);
    }

    private void TextoMuro(string texto, double x, double y, double tamano, Brush color, bool negrita = false)
    {
        var t = new TextBlock
        {
            Text = texto,
            FontSize = tamano,
            Foreground = color,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal
        };

        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        MurosPreviewCanvas.Children.Add(t);
    }
}
