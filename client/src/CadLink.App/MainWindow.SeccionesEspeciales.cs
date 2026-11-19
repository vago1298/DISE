using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La subpestaña «Formas especiales» de Secciones Concreto: castillos, columnas, cabezales y dados
/// en L, en T o de forma irregular, con su armado, su vista previa y su dibujo en AutoCAD.
/// </summary>
public partial class MainWindow
{
    private bool _especialesListos;

    /// <summary>Enlaza la hoja con los datos. Se llama desde <c>Enlazar</c>, cada vez que cambian.</summary>
    private void EnlazarEspeciales()
    {
        if (!_especialesListos)
        {
            var varillas = Varilla.DiametrosCm.Keys.ToList();
            var opcionales = new List<string> { string.Empty };
            opcionales.AddRange(varillas);

            ColEspElemento.ItemsSource = SeccionEspecialRow.Elementos;
            ColEspForma.ItemsSource = FormaEspecial.Todas;
            ColEspEsquina.ItemsSource = varillas;
            ColEspIntermedia.ItemsSource = opcionales;
            ColEspEstribo.ItemsSource = opcionales;

            EspecialesGrid.SelectionChanged += (_, _) => DibujarPreviaEspecial();
            EspecialPreviewCanvas.SizeChanged += (_, _) => DibujarPreviaEspecial();
            _especialesListos = true;
        }

        EspecialesGrid.ItemsSource = _datos.SeccionesEspeciales;

        // Cada celda editada se ve al momento en la previa, como en la hoja de concreto.
        _datos.SeccionesEspeciales.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (Row fila in e.NewItems)
                {
                    fila.PropertyChanged += (_, _) => DibujarPreviaEspecial();
                }
            }

            DibujarPreviaEspecial();
        };

        foreach (var fila in _datos.SeccionesEspeciales)
        {
            fila.PropertyChanged += (_, _) => DibujarPreviaEspecial();
        }

        if (_datos.SeccionesEspeciales.Count > 0)
        {
            EspecialesGrid.SelectedIndex = 0;
        }

        DibujarPreviaEspecial();
    }

    private SeccionEspecialRow? EspecialElegida =>
        EspecialesGrid.SelectedItem as SeccionEspecialRow ?? _datos.SeccionesEspeciales.FirstOrDefault();

    // ======================================================================
    //  Botones
    // ======================================================================

    private void OnAgregarEspecial(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        // Copia de la elegida, con un ID libre: en una obra hay varias iguales salvo el ID.
        var base_ = EspecialElegida;
        var n = _datos.SeccionesEspeciales.Count + 1;
        string id;

        do
        {
            id = $"CE-{n++}";
        }
        while (_datos.SeccionesEspeciales.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)));

        var fila = new SeccionEspecialRow { Id = id };

        if (base_ is not null)
        {
            FilaSerializable.Aplicar(fila, FilaSerializable.Leer(base_));
            fila.Id = id;
        }

        _datos.SeccionesEspeciales.Add(fila);
        EspecialesGrid.SelectedItem = fila;
        EspecialesGrid.ScrollIntoView(fila);
    }

    private void OnQuitarEspecial(object sender, RoutedEventArgs e)
    {
        if (EspecialesGrid.SelectedItem is SeccionEspecialRow fila)
        {
            _datos.SeccionesEspeciales.Remove(fila);
        }
    }

    private void OnRevisarEspeciales(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        var problemas = ProblemasEspeciales();

        MessageBox.Show(this,
            problemas.Count == 0
                ? $"Sin errores.\n\n{_datos.SeccionesEspeciales.Count} forma(s) especial(es) revisada(s)."
                : "Corrige esto:\n\n" + string.Join("\n", problemas),
            AppInfo.ProductName, MessageBoxButton.OK,
            problemas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private List<string> ProblemasEspeciales()
    {
        var problemas = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renglon = 0;

        foreach (var s in _datos.SeccionesEspeciales)
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

            // El mismo ID en la hoja de concreto pisaria su bloque en AutoCAD.
            if (_datos.SeccionesConcreto.Any(c => string.Equals(c.Id?.Trim(), s.Id.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                problemas.Add($"  - Renglon {renglon}: el ID «{s.Id}» ya lo usa una seccion rectangular.");
            }
        }

        return problemas;
    }

    private void OnDibujarEspeciales(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show(this, "Tu licencia no incluye la generacion de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_datos.SeccionesEspeciales.Count == 0)
        {
            MessageBox.Show(this, "No hay formas especiales que dibujar. Agrega una con «Agregar forma».",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var problemas = ProblemasEspeciales();

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
                Redibujar = RedibujarEspecialesChk.IsChecked == true
            };

            var secciones = _datos.SeccionesEspeciales.Select(s => s.AFormatoCad(ModoElegido)).ToList();

            dibujante.AsegurarCapas(secciones
                .SelectMany(s => new[] { s.Esquina, s.Intermedia, s.Estribo })
                .Where(v => v.Existe)
                .Select(v => v.Clave)
                .Distinct(StringComparer.OrdinalIgnoreCase));

            MarcasCad.Registrar(doc);
            dynamic ms = AcadConnection.Retry(() => doc.ModelSpace);

            var x = dibujante.PosicionInicialX();
            var dibujadas = 0;

            foreach (var s in secciones)
            {
                var saltadasAntes = dibujante.Saltadas.Count;

                if (dibujante.Redibujar && dibujante.BloqueYaExiste(s.Id))
                {
                    List<MarcasCad.Marcada> viejas = MarcasCad.Buscar(doc, ms, MarcasCad.ClaveSeccion(s.Id));
                    MarcasCad.Borrar(viejas.Where(m => !MarcasCad.EsBloque(m.Entidad)));
                }

                dibujante.DibujarEspecial(s, x, 0);

                if (dibujante.InicioUltima >= 0)
                {
                    MarcasCad.Marcar(ms, dibujante.InicioUltima, MarcasCad.ClaveSeccion(s.Id), x, 0);
                }

                if (dibujante.Saltadas.Count > saltadasAntes)
                {
                    continue;
                }

                dibujadas++;

                if (!dibujante.UltimaFueASuSitio)
                {
                    x += (s.Contorno.Max(v => v.X) + 35) * escala;
                }
            }

            dibujante.RotulosAlFrente();
            dibujante.RevisarColorNegro();
            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var saltadas = dibujante.Saltadas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var avisos = dibujante.Fallos.Concat(dibujante.Notas).ToList();

            StatusText.Text = $"Dibujadas {dibujadas} forma(s) especial(es) en AutoCAD.";

            MessageBox.Show(this,
                $"Listo: {dibujadas} forma(s) especial(es) dibujada(s), cada una en un bloque con su ID."
                + (saltadas.Count == 0 ? string.Empty
                    : $"\n\nSe saltaron {saltadas.Count} porque su bloque ya existe: {string.Join(", ", saltadas)}. "
                      + "Marca «Redibujar las que ya existen» para rehacerlas en su sitio.")
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
    /// Pinta la forma elegida: concreto, estribo, varillas y su rotulo, con la MISMA geometria que
    /// se dibuja en AutoCAD -<see cref="GeometriaEspecial"/>-.
    /// </summary>
    private void DibujarPreviaEspecial()
    {
        if (EspecialPreviewCanvas is null)
        {
            return;
        }

        var c = EspecialPreviewCanvas;
        c.Children.Clear();

        var fila = EspecialElegida;

        if (fila is null)
        {
            EspecialRotuloText.Text = "Agrega una forma con «Agregar forma».";
            return;
        }

        var s = fila.AFormatoCad();
        var falta = fila.Falta();

        if (s.Contorno.Count < 3 || c.ActualWidth < 20 || c.ActualHeight < 20)
        {
            EspecialRotuloText.Text = falta.Count > 0 ? "Falta: " + string.Join(", ", falta) + "." : string.Empty;
            return;
        }

        var ancho = s.Contorno.Max(v => v.X);
        var alto = s.Contorno.Max(v => v.Y);
        var k = Math.Min((c.ActualWidth - 40) / ancho, (c.ActualHeight - 40) / alto);
        var ox = (c.ActualWidth - (ancho * k)) / 2;
        var oy = (c.ActualHeight + (alto * k)) / 2;

        Point P(double x, double y) => new(ox + (x * k), oy - (y * k));

        Polygon Poli(IEnumerable<(double X, double Y)> p, Brush relleno, Brush borde, double grueso) => new()
        {
            Points = new PointCollection(p.Select(v => P(v.X, v.Y))),
            Fill = relleno,
            Stroke = borde,
            StrokeThickness = grueso
        };

        c.Children.Add(Poli(s.Contorno, new SolidColorBrush(Color.FromRgb(215, 215, 210)), Brushes.DimGray, 1.2));

        if (s.Estribo.Existe && GeometriaEspecial.HaciaDentro(s.Contorno, s.RecubrimientoCm + (s.Estribo.Cm / 2)) is { } eje)
        {
            c.Children.Add(Poli(eje, Brushes.Transparent, Brushes.Firebrick, Math.Max(1.2, s.Estribo.Cm * k)));
        }

        var varillas = GeometriaEspecial.Varillas(s);

        foreach (var v in varillas)
        {
            var d = Math.Max(3, v.DiamCm * k);
            var punto = new Ellipse { Width = d, Height = d, Fill = v.Esquina ? Brushes.Black : Brushes.DimGray };
            Canvas.SetLeft(punto, P(v.X, v.Y).X - (d / 2));
            Canvas.SetTop(punto, P(v.X, v.Y).Y - (d / 2));
            c.Children.Add(punto);
        }

        EspecialRotuloText.Text = string.Join(Environment.NewLine, SeccionDrawer.LineasDeRotuloEspecial(s, varillas))
            + Environment.NewLine + Environment.NewLine
            + $"Area {fila.AreaCm2:0.#} cm²  ·  As {fila.AreaAceroCm2:0.00} cm²  ·  cuantia {fila.CuantiaPorcentaje:0.###} %"
            + (falta.Count > 0 ? Environment.NewLine + Environment.NewLine + "Falta: " + string.Join(", ", falta) + "." : string.Empty);
    }
}
