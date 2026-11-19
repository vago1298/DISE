using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La pestaña «Losas»: el armado de losas de entrepiso y de cimentacion, tablero por tablero, con
/// corridas, bayonetas y bastones a L/4 del claro, su vista previa y su dibujo en AutoCAD.
/// </summary>
public partial class MainWindow
{
    private bool _losasListas;

    /// <summary>Enlaza la hoja con los datos. Se llama desde <c>Enlazar</c>, cada vez que cambian.</summary>
    private void EnlazarLosas()
    {
        if (!_losasListas)
        {
            var varillas = Varilla.DiametrosCm.Keys.ToList();
            var opcionales = new List<string> { string.Empty };
            opcionales.AddRange(varillas);

            ColLosaTipo.ItemsSource = ArmadoLosaRow.Tipos;
            ColLosaVarX.ItemsSource = varillas;
            ColLosaVarY.ItemsSource = varillas;
            ColLosaBaston.ItemsSource = opcionales;
            ColLosaIzq.ItemsSource = ArmadoLosaRow.Bordes;
            ColLosaDer.ItemsSource = ArmadoLosaRow.Bordes;
            ColLosaAbajo.ItemsSource = ArmadoLosaRow.Bordes;
            ColLosaArriba.ItemsSource = ArmadoLosaRow.Bordes;

            LosasGrid.SelectionChanged += (_, _) => DibujarPreviaLosa();
            LosaPreviewCanvas.SizeChanged += (_, _) => DibujarPreviaLosa();
            _losasListas = true;
        }

        LosasGrid.ItemsSource = _datos.Losas;

        _datos.Losas.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (Row fila in e.NewItems)
                {
                    fila.PropertyChanged += (_, _) => DibujarPreviaLosa();
                }
            }

            DibujarPreviaLosa();
        };

        foreach (var fila in _datos.Losas)
        {
            fila.PropertyChanged += (_, _) => DibujarPreviaLosa();
        }

        if (_datos.Losas.Count > 0)
        {
            LosasGrid.SelectedIndex = 0;
        }

        DibujarPreviaLosa();
    }

    private ArmadoLosaRow? LosaElegida =>
        LosasGrid.SelectedItem as ArmadoLosaRow ?? _datos.Losas.FirstOrDefault();

    // ======================================================================
    //  Botones
    // ======================================================================

    private void OnAgregarLosa(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        var base_ = LosaElegida;
        var n = _datos.Losas.Count + 1;
        string id;

        do
        {
            id = $"LS-{n++}";
        }
        while (_datos.Losas.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)));

        var fila = new ArmadoLosaRow { Id = id };

        if (base_ is not null)
        {
            FilaSerializable.Aplicar(fila, FilaSerializable.Leer(base_));
            fila.Id = id;
        }

        _datos.Losas.Add(fila);
        LosasGrid.SelectedItem = fila;
        LosasGrid.ScrollIntoView(fila);
    }

    private void OnQuitarLosa(object sender, RoutedEventArgs e)
    {
        if (LosasGrid.SelectedItem is ArmadoLosaRow fila)
        {
            _datos.Losas.Remove(fila);
        }
    }

    private void OnRevisarLosas(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        var problemas = ProblemasLosas();

        MessageBox.Show(this,
            problemas.Count == 0
                ? $"Sin errores.\n\n{_datos.Losas.Count} tablero(s) revisado(s)."
                : "Corrige esto:\n\n" + string.Join("\n", problemas),
            AppInfo.ProductName, MessageBoxButton.OK,
            problemas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private List<string> ProblemasLosas()
    {
        var problemas = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renglon = 0;

        foreach (var s in _datos.Losas)
        {
            renglon++;
            var falta = s.Falta();

            if (falta.Count > 0)
            {
                problemas.Add($"  - Renglon {renglon} «{s.Id}»: falta {string.Join(", ", falta)}.");
            }

            if (s.Id.Trim().Length > 0 && !ids.Add(s.Id.Trim()))
            {
                problemas.Add($"  - Renglon {renglon}: el ID «{s.Id}» esta repetido; es el nombre del bloque.");
            }
        }

        return problemas;
    }

    private void OnDibujarLosas(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show(this, "Tu licencia no incluye la generacion de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_datos.Losas.Count == 0)
        {
            MessageBox.Show(this, "No hay tableros que dibujar. Agrega uno con «Agregar tablero».",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var problemas = ProblemasLosas();

        if (problemas.Count > 0)
        {
            MessageBox.Show(this, "Corrige esto antes de dibujar:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            var escala = LeerEscala();
            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new SeccionDrawer(doc, escala)
            {
                EscalaHatch = LeerEscalaHatch(),
                Redibujar = RedibujarLosasChk.IsChecked == true
            };

            var losas = _datos.Losas.Select(s => s.AFormatoCad()).ToList();

            dibujante.AsegurarCapas(losas
                .SelectMany(s => new[] { s.VarX, s.VarY, s.Baston })
                .Where(v => v.Existe)
                .Select(v => v.Clave)
                .Distinct(StringComparer.OrdinalIgnoreCase));

            MarcasCad.Registrar(doc);
            dynamic ms = AcadConnection.Retry(() => doc.ModelSpace);

            var x = dibujante.PosicionInicialX();
            var dibujadas = 0;

            foreach (var s in losas)
            {
                var saltadasAntes = dibujante.Saltadas.Count;

                if (dibujante.Redibujar && dibujante.BloqueYaExiste(s.Bloque))
                {
                    List<MarcasCad.Marcada> viejas = MarcasCad.Buscar(doc, ms, MarcasCad.ClaveSeccion(s.Bloque));
                    MarcasCad.Borrar(viejas.Where(m => !MarcasCad.EsBloque(m.Entidad)));
                }

                dibujante.DibujarLosa(s, x, 0);

                if (dibujante.InicioUltima >= 0)
                {
                    MarcasCad.Marcar(ms, dibujante.InicioUltima, MarcasCad.ClaveSeccion(s.Bloque), x, 0);
                }

                if (dibujante.Saltadas.Count > saltadasAntes)
                {
                    continue;
                }

                dibujadas++;

                if (!dibujante.UltimaFueASuSitio)
                {
                    x += dibujante.AnchoDeLosa(s) + (2.0 * escala / 0.01);
                }
            }

            dibujante.RotulosAlFrente();
            dibujante.RevisarColorNegro();
            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var saltadas = dibujante.Saltadas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var avisos = dibujante.Fallos.Concat(dibujante.Notas).ToList();

            StatusText.Text = $"Dibujados {dibujadas} tablero(s) de losa en AutoCAD.";

            MessageBox.Show(this,
                $"Listo: {dibujadas} tablero(s) dibujado(s), cada uno en un bloque LOSA-ID."
                + (saltadas.Count == 0 ? string.Empty
                    : $"\n\nSe saltaron {saltadas.Count} porque su bloque ya existe: {string.Join(", ", saltadas)}. "
                      + "Marca «Redibujar las que ya existen» para rehacerlos en su sitio.")
                + (avisos.Count == 0 ? string.Empty : "\n\nAvisos:\n  - " + string.Join("\n  - ", avisos.Take(12))),
                AppInfo.ProductName, MessageBoxButton.OK,
                avisos.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (AcadBusyException ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error al dibujar en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ======================================================================
    //  Vista previa
    // ======================================================================

    /// <summary>
    /// Pinta el tablero elegido con la MISMA geometria que se dibuja en AutoCAD
    /// -<see cref="GeometriaArmadoLosa.Dibujo"/>-: planta y los dos cortes.
    /// </summary>
    private void DibujarPreviaLosa()
    {
        if (LosaPreviewCanvas is null)
        {
            return;
        }

        var c = LosaPreviewCanvas;
        c.Children.Clear();

        var fila = LosaElegida;

        if (fila is null)
        {
            LosaRotuloText.Text = "Agrega un tablero con «Agregar tablero».";
            return;
        }

        var falta = fila.Falta();
        var l = fila.AFormatoCad();
        var dib = GeometriaArmadoLosa.Dibujo(l);

        if (dib.Trazos.Count == 0 || c.ActualWidth < 20 || c.ActualHeight < 20)
        {
            LosaRotuloText.Text = falta.Count > 0 ? "Falta: " + string.Join(", ", falta) + "." : string.Empty;
            return;
        }

        var (x0, y0, x1, y1) = dib.Caja();
        var k = Math.Min((c.ActualWidth - 20) / Math.Max(x1 - x0, 0.01), (c.ActualHeight - 20) / Math.Max(y1 - y0, 0.01));
        var ox = (c.ActualWidth - ((x1 - x0) * k)) / 2;
        var oy = (c.ActualHeight + ((y1 - y0) * k)) / 2;

        Point P(double x, double y) => new(ox + ((x - x0) * k), oy - ((y - y0) * k));

        foreach (var t in dib.Trazos)
        {
            var esVarilla = t.Capa.StartsWith("VAR_", StringComparison.Ordinal);
            var poli = new Polyline
            {
                Points = new PointCollection(t.Puntos.Select(p => P(p.X, p.Y))),
                Stroke = esVarilla ? Brushes.Firebrick : Brushes.DimGray,
                StrokeThickness = esVarilla ? Math.Max(1.2, t.Ancho * k) : 1
            };

            if (t.Cerrado && t.Puntos.Count > 0)
            {
                poli.Points.Add(P(t.Puntos[0].X, t.Puntos[0].Y));
            }

            c.Children.Add(poli);
        }

        foreach (var p in dib.Puntos)
        {
            var d = Math.Max(2.5, p.Radio * 2 * k);
            var punto = new Ellipse { Width = d, Height = d, Fill = Brushes.Black };
            Canvas.SetLeft(punto, P(p.X, p.Y).X - (d / 2));
            Canvas.SetTop(punto, P(p.X, p.Y).Y - (d / 2));
            c.Children.Add(punto);
        }

        LosaRotuloText.Text = $"{l.Nombre} \"{l.Id}\"" + Environment.NewLine
            + $"Claro libre X {GeometriaArmadoLosa.ClaroLibre(l.ClaroXM, l.ApoyoAnchoCm):0.00} m · L/4 = {fila.BastonXM:0.00} m" + Environment.NewLine
            + $"Claro libre Y {GeometriaArmadoLosa.ClaroLibre(l.ClaroYM, l.ApoyoAnchoCm):0.00} m · L/4 = {fila.BastonYM:0.00} m" + Environment.NewLine
            + Environment.NewLine + "EN X:" + Environment.NewLine + string.Join(Environment.NewLine, GeometriaArmadoLosa.Notas(l, l.VarX, l.SepXCm, l.ClaroXM))
            + Environment.NewLine + Environment.NewLine + "EN Y:" + Environment.NewLine + string.Join(Environment.NewLine, GeometriaArmadoLosa.Notas(l, l.VarY, l.SepYCm, l.ClaroYM))
            + (falta.Count > 0 ? Environment.NewLine + Environment.NewLine + "Falta: " + string.Join(", ", falta) + "." : string.Empty);
    }
}
