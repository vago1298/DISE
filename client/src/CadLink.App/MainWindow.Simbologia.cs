using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CadLink.Cad;

// La figura de WPF con la que se pinta la vista previa, con alias y SIN importar
// System.Windows.Shapes entero: ese espacio de nombres trae un Path y System.IO —que es using
// GLOBAL, está en el .csproj— trae otro, así que con los dos importados escribir «Path» a secas es
// un CS0104, referencia ambigua. Es el mismo apaño de MainWindow.PlacaBase.cs y MainWindow.Acero.cs.
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>Conexiones/Detalles</b>: por ahora, la simbología de soldadura.
/// </summary>
/// <remarks>
/// <para>
/// El reparto de la simbología —las proporciones de cada símbolo y dónde cae cada pieza— lo hace
/// <see cref="SimbolosSoldadura"/>, que no toca COM ni WPF. Así la vista previa y el dibujo de
/// AutoCAD salen de <b>la misma geometría</b>: lo que se ve aquí es lo que se va a dibujar, y no dos
/// dibujos parecidos que pueden separarse con el tiempo. Es el mismo criterio que la previa del
/// alzado de la placa base.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// La altura de texto con la que se arma la simbología de la previa, en centímetros.
    /// </summary>
    /// <remarks>
    /// La previa trabaja en centímetros y ajusta al final, igual que las otras. En AutoCAD la altura
    /// es la del estilo de la macro; aquí solo fija la proporción, que es lo único que se ve.
    /// </remarks>
    private const double AlturaSimbologiaPrevia = 1.6;

    /// <summary>El título que se está escribiendo, ya limpio.</summary>
    private string TituloDeLaSimbologia
    {
        get
        {
            var s = (TituloSimbologiaBox?.Text ?? string.Empty).Trim();

            return s.Length == 0 ? "SIMBOLOGIA DE SOLDADURA" : s;
        }
    }

    private void OnTituloSimbologiaCambiado(object sender, TextChangedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        DibujarSimbologiaPrevia();
    }

    /// <summary>
    /// Pinta la simbología en su recuadro, encuadrada a lo que quepa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se dibuja <b>en centímetros con la Y hacia arriba</b> y el ajuste al lienzo se hace al final
    /// con una sola transformación, como en las demás previas: así la escala de pantalla no se
    /// mezcla con las proporciones del símbolo.
    /// </para>
    /// <para>
    /// <b>La pluma NO se escala</b> con la geometría —un <c>StrokeThickness</c> se mide en píxeles—,
    /// y eso aquí es lo que se quiere: un símbolo de soldadura se dibuja con línea fina a cualquier
    /// tamaño, igual que en el plano.
    /// </para>
    /// </remarks>
    private void DibujarSimbologiaPrevia()
    {
        if (SimbologiaPreviewCanvas is null)
        {
            return;
        }

        SimbologiaPreviewCanvas.Children.Clear();

        var ancho = SimbologiaPreviewCanvas.ActualWidth > 0
            ? SimbologiaPreviewCanvas.ActualWidth
            : SimbologiaPreviewCanvas.Width;

        var alto = SimbologiaPreviewCanvas.ActualHeight > 0
            ? SimbologiaPreviewCanvas.ActualHeight
            : SimbologiaPreviewCanvas.Height;

        if (ancho <= 0 || alto <= 0)
        {
            return;
        }

        var h = AlturaSimbologiaPrevia;
        var legenda = SimbolosSoldadura.Construir(0, 0, h, TituloDeLaSimbologia);

        if (legenda.Renglones.Count == 0 || legenda.Ancho <= 0 || legenda.Alto <= 0)
        {
            return;
        }

        // El encuadre: la caja de la simbología más un margen, y la escala la manda el lado que
        // menos sitio tenga. El texto se mide a ojo -0.62 alturas por letra- en Construir, así que
        // el ancho de la caja ya lo trae contado.
        const double margen = 18;

        var escala = Math.Min(
            (ancho - (2 * margen)) / legenda.Ancho,
            (alto - (2 * margen)) / legenda.Alto);

        if (escala <= 0 || double.IsInfinity(escala) || double.IsNaN(escala))
        {
            return;
        }

        // De centímetros con la Y hacia arriba a píxeles con la Y hacia abajo. El origen de la
        // simbología es el renglón del título, arriba a la izquierda, así que se baja un margen.
        var transformar = new TransformGroup();
        transformar.Children.Add(new ScaleTransform(escala, -escala));
        transformar.Children.Add(new TranslateTransform(margen, margen + (0.9 * h * escala)));

        var tinta = new SolidColorBrush(Color.FromRgb(0x11, 0x20, 0x2D));

        var lineas = new GeometryGroup { Transform = transformar };
        var rellenos = new GeometryGroup { Transform = transformar };

        foreach (var r in legenda.Renglones)
        {
            AgregarAbierta(lineas, r.Leader);

            // La punta de la flecha, rellena: del primer punto del leader hacia el codo.
            rellenos.Children.Add(PuntaDeFlecha(
                r.Leader[0], r.Leader[1], r.Leader[2], r.Leader[3], 0.55 * h));

            foreach (var a in r.Abiertas)
            {
                AgregarAbierta(lineas, a);
            }

            foreach (var c in r.Cerradas)
            {
                AgregarPoligonal(lineas, c, null);
            }

            foreach (var f in r.Rellenas)
            {
                AgregarPoligonal(rellenos, f, null);
            }

            if (r.Circulo is { } c2)
            {
                lineas.Children.Add(new EllipseGeometry(new Point(c2.X, c2.Y), c2.R, c2.R));
            }
        }

        SimbologiaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = lineas,
            Stroke = tinta,
            StrokeThickness = 1.3,
            StrokeLineJoin = PenLineJoin.Round
        });

        SimbologiaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = rellenos,
            Fill = tinta,
            Stroke = tinta,
            StrokeThickness = 1
        });

        // ---------- Los textos ----------
        // Van como TextBlock y no como geometría: WPF no convierte texto a trazo sin cargar la
        // fuente a mano, y para una previa el texto real se lee mejor que un trazo aproximado.
        PonerTexto(legenda.Titulo, transformar, tinta, negrita: true);

        foreach (var r in legenda.Renglones)
        {
            foreach (var t in r.Textos)
            {
                PonerTexto(t, transformar, tinta, negrita: false);
            }
        }
    }

    /// <summary>
    /// Un texto de la simbología, colocado con su mismo anclaje que en AutoCAD.
    /// </summary>
    /// <remarks>
    /// Los anclajes son los del TEXT de AutoCAD: <c>9</c> izquierda, <c>10</c> centro y <c>11</c>
    /// derecha, siempre a media altura. Aquí se traducen a un desplazamiento del rótulo, que es lo
    /// único que WPF entiende: se mide el texto ya compuesto y se corre media altura hacia arriba.
    /// </remarks>
    private void PonerTexto(
        SimbolosSoldadura.TextoDwg t, Transform transformar, Brush tinta, bool negrita)
    {
        if (t.S.Trim().Length == 0)
        {
            return;
        }

        // El %%d de AutoCAD es el símbolo de grado. En pantalla se escribe tal cual.
        var texto = t.S.Replace("%%d", "°", StringComparison.Ordinal);

        var punto = transformar.Transform(new Point(t.X, t.Y));

        // La altura en píxeles sale de la MISMA escala de la geometría, leída de la transformación:
        // así el texto crece y se encoge con el dibujo en lugar de quedarse a un tamaño fijo.
        var escala = transformar.Transform(new Point(1, 0)).X
                     - transformar.Transform(new Point(0, 0)).X;

        var alturaPx = Math.Max(7.0, t.Altura * escala);

        var rotulo = new TextBlock
        {
            Text = texto,
            Foreground = tinta,
            FontSize = alturaPx,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = new FontFamily("Segoe UI Semibold, Segoe UI")
        };

        rotulo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var dx = t.Anclaje switch
        {
            10 => -rotulo.DesiredSize.Width / 2,
            11 => -rotulo.DesiredSize.Width,
            _ => 0.0,
        };

        Canvas.SetLeft(rotulo, punto.X + dx);
        Canvas.SetTop(rotulo, punto.Y - (rotulo.DesiredSize.Height / 2));

        SimbologiaPreviewCanvas.Children.Add(rotulo);
    }

    /// <summary>La punta rellena del leader: el mismo triángulo que dibuja el dibujante.</summary>
    private static Geometry PuntaDeFlecha(
        double xPunta, double yPunta, double xHacia, double yHacia, double largo)
    {
        var dx = xHacia - xPunta;
        var dy = yHacia - yPunta;
        var d = Math.Sqrt((dx * dx) + (dy * dy));

        if (d < 1e-9)
        {
            return Geometry.Empty;
        }

        var ux = dx / d;
        var uy = dy / d;

        var xBase = xPunta + (largo * ux);
        var yBase = yPunta + (largo * uy);

        // Perpendicular al eje de la flecha, para los dos vértices de atrás.
        var semi = 0.4 * largo;
        var px = -uy * semi;
        var py = ux * semi;

        var figura = new PathFigure
        {
            StartPoint = new Point(xPunta, yPunta),
            IsClosed = true,
            IsFilled = true
        };

        figura.Segments.Add(new LineSegment(new Point(xBase + px, yBase + py), true));
        figura.Segments.Add(new LineSegment(new Point(xBase - px, yBase - py), true));

        var geo = new PathGeometry();
        geo.Figures.Add(figura);

        return geo;
    }

    // ======================================================================
    //  EL BOTÓN
    // ======================================================================

    /// <summary>
    /// Dibuja la simbología en AutoCAD, en el origen del dibujo y como un bloque.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>En el origen y no donde esté la vista</b> a propósito: es un cuadro de notas que se coloca
    /// una vez en la hoja, y como sale agrupado en un bloque se arrastra a su sitio con un solo
    /// movimiento. Preguntarle el punto a AutoCAD obligaría a pasar el foco a su ventana y a que el
    /// usuario acertara el clic, con el programa esperando en medio.
    /// </para>
    /// <para>
    /// Se reutiliza el dibujante de la placa base —ahí viven las capas, el estilo de texto, la
    /// flecha y el bloqueo—; el motivo está escrito en <c>PlacaBaseDrawer.Simbologia.cs</c>.
    /// </para>
    /// </remarks>
    private void OnDibujarSimbologiaSoldadura(object sender, RoutedEventArgs e)
    {
        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;

            var escala = LeerEscala();

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new PlacaBaseDrawer(doc, escala);

            var n = dibujante.DibujarSimbologiaSoldadura(0, 0, TituloDeLaSimbologia);

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            if (n <= 0)
            {
                MessageBox.Show(
                    this,
                    "No se dibujó nada. Revisa que AutoCAD tenga un dibujo abierto.",
                    Branding.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            var resumen = $"Simbología de soldadura dibujada: {n} entidades.";

            if (dibujante.UltimoBloque.Length > 0)
            {
                resumen += $"\n\nBloque: {dibujante.UltimoBloque}";
            }

            resumen += "\n\nEstá en el origen del dibujo (0,0). Muévela al sitio de la hoja que " +
                       "te convenga: va agrupada, así que se arrastra de una pieza.";

            if (dibujante.Fallos.Count > 0)
            {
                resumen += "\n\nAvisos:\n" + string.Join("\n", dibujante.Fallos);
            }

            StatusText.Text = $"Simbología de soldadura: {n} entidades.";

            MessageBox.Show(this, resumen, Branding.ProductName,
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Branding.ProductName,
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }
    }
}
