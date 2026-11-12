using System.Collections.ObjectModel;
using System.Windows;
using CadLink.App.Models;
using CadLink.Cad;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.App;

/// <summary>
/// La tarjeta «Conexiones desde el libro de Excel» de la pestaña Conexiones/Detalles: lee el libro
/// de revisiones de acero (NTC-Acero 2023), lista sus conexiones y dibuja en AutoCAD las que se
/// palomeen.
/// </summary>
/// <remarks>
/// <para>
/// Las piezas las arma el nucleo (<see cref="ConexionesDelLibro"/>), el mismo que usa el
/// complemento de Revit: lo que se dibuja aqui y lo que se modela alla salen de las mismas
/// medidas. Las vistas son proyecciones de esas piezas (<see cref="VistasConexion"/>), y aqui solo
/// se pasan a centimetros para el dibujante.
/// </para>
/// <para>
/// Las placas base no tienen dibujante propio: se arma un renglon de la hoja Placa Base con los
/// datos del libro y se dibuja con su detalle de siempre -planta, alzado, anclas, simbologia-.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Donde empiezan los detalles en AutoCAD, en unidades del dibujo: aparte del resto.</summary>
    private const double XDeLosDetalles = -25;

    private readonly ObservableCollection<FilaConexionDelLibro> _conexionesDelLibro = new();
    private string? _libroDeConexiones;

    private void EnlazarConexionesDelLibro()
    {
        if (ConexionesLibroGrid is not null && ConexionesLibroGrid.ItemsSource is null)
        {
            ConexionesLibroGrid.ItemsSource = _conexionesDelLibro;
        }
    }

    private void OnLeerLibroDeConexiones(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Abrir el libro de revisiones de acero",
            Filter = "Libro de Excel (*.xlsm;*.xlsx)|*.xlsm;*.xlsx|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        LeerLibroDeConexiones(dialogo.FileName);
    }

    private void OnVolverALeerLibro(object sender, RoutedEventArgs e)
    {
        if (_libroDeConexiones is { } ruta && File.Exists(ruta))
        {
            LeerLibroDeConexiones(ruta);
        }
        else
        {
            OnLeerLibroDeConexiones(sender, e);
        }
    }

    private void LeerLibroDeConexiones(string ruta)
    {
        EnlazarConexionesDelLibro();

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var libro = LibroExcel.Abrir(ruta);
            var lista = ConexionesDelLibro.Leer(libro);

            _conexionesDelLibro.Clear();

            foreach (var c in lista)
            {
                _conexionesDelLibro.Add(new FilaConexionDelLibro(c));
            }

            _libroDeConexiones = ruta;
            ConexionesLibroText.Text = ruta;

            var notas = new List<string>();
            var dibujables = lista.Count(c => c.SeDibuja);
            notas.Add($"{lista.Count} hoja(s) de conexiones; {dibujables} se pueden dibujar. Las de datos de ejemplo y las que no se dibujan van sin palomita.");

            if (libro.RecalcularAlAbrir)
            {
                notas.Add("El libro se modificó fuera de Excel y pide recalcular: ábrelo en Excel, guárdalo y vuelve a leerlo, para que los valores calculados estén al día.");
            }

            var noCumplen = lista.Where(c => c.Hoja.NoCumplen > 0 && !c.Hoja.DeEjemplo)
                .Select(c => $"«{c.Hoja.Hoja.Trim()}»: {string.Join("; ", c.Hoja.Fallas)}").ToList();

            if (noCumplen.Count > 0)
            {
                notas.Add("Revisiones que el libro GUARDÓ como no cumple (es lo que dice la columna D de la hoja la última vez que Excel guardó el libro): "
                    + string.Join(" · ", noCumplen) + ". Si en Excel ya cumplen, guarda el libro y pulsa «Volver a leer».");
            }

            ConexionesLibroNotas.Text = string.Join("\n", notas);
            StatusText.Text = $"Libro de acero: {lista.Count} conexiones leídas.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo leer el libro.\n\n" + ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void OnTodasLasConexiones(object sender, RoutedEventArgs e)
    {
        var todas = _conexionesDelLibro.Where(f => f.SePuedeDibujar).All(f => f.Dibujar);

        foreach (var f in _conexionesDelLibro.Where(f => f.SePuedeDibujar))
        {
            f.Dibujar = !todas;
        }
    }

    private void OnDibujarConexionesDelLibro(object sender, RoutedEventArgs e)
    {
        var elegidas = _conexionesDelLibro.Where(f => f.Dibujar && f.Conexion.Modelo is not null)
            .Select(f => (f.Conexion.Modelo!, f.Conexion.Hoja)).ToList();

        if (elegidas.Count == 0)
        {
            MessageBox.Show(this, "Lee un libro y palomea las conexiones que quieres dibujar.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DibujarConexiones(elegidas);
    }

    /// <summary>
    /// Dibuja las conexiones en AutoCAD, APARTE: a la izquierda de X = -25, un renglon por cada
    /// tipo de hoja (todas las uniones viga-columna juntas, luego las placas gusset…) en el orden
    /// en que aparecen, y en cada renglon de derecha a izquierda.
    /// </summary>
    private void DibujarConexiones(List<(ModeloConexion M, HojaLeida H)> elegidas)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var escala = LeerEscala();

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            ConexionAceroDrawer dibujante = new ConexionAceroDrawer(doc, escala);
            dibujante.AsegurarCapas();
            PlacaBaseDrawer? placas = null;

            // La separacion entre detalles es la del estilo (Conexiones de acero), en metros de dibujo.
            var separacion = ConexionAceroCapas.SeparacionDetalles * escala / 0.01;
            var hechas = 0;
            var fallos = new List<string>();
            var y = 0.0;

            // Un renglon por tipo de hoja, en el orden en que salen en la lista.
            var grupos = elegidas.GroupBy(c => c.M.Codigo).ToList();

            foreach (var grupo in grupos)
            {
                var x = XDeLosDetalles;
                var altoDelRenglon = 0.0;
                var enElRenglon = 0;

                foreach (var (m, h) in grupo)
                {
                    double ancho, alto;

                    if (m.PlacaBase is not null)
                    {
                        placas ??= NuevoDibujanteDePlacas(doc, escala);
                        var p = PlacaBaseDelLibroAFila(PlacaBaseDelLibro.Datos(h)).AFormatoCad();

                        // La insercion de la placa va en unidades del dibujo, como en la hoja Placa
                        // Base, y en su esquina izquierda: se recorre a la izquierda lo que va a medir.
                        var anchoPlaca = (p.AnchoTotalDibujoCm + 60) * escala;
                        p.InsercionX = x - anchoPlaca;
                        p.InsercionY = y + p.InsercionY;

                        if (placas.Dibujar(p) <= 0)
                        {
                            fallos.Add($"{h.Hoja.Trim()}: la placa base no se dibujó (revisa las separaciones de las anclas en la hoja Placa Base).");
                            continue;
                        }

                        ancho = Math.Max(placas.UltimoAnchoDibujado, anchoPlaca);
                        alto = 250 * escala;
                    }
                    else
                    {
                        var detalle = ACad(m, h);
                        var medida = dibujante.Medir(detalle);

                        if (dibujante.Dibujar(detalle, x - medida, y) <= 0)
                        {
                            continue;
                        }

                        ancho = Math.Max(medida, dibujante.UltimoAncho);
                        alto = dibujante.UltimoAlto;
                    }

                    hechas++;
                    altoDelRenglon = Math.Max(altoDelRenglon, alto);
                    x -= ancho + separacion;

                    // Mas de cuatro del mismo tipo: se sigue en el renglon de abajo.
                    if (++enElRenglon >= 4)
                    {
                        x = XDeLosDetalles;
                        y -= altoDelRenglon + separacion;
                        altoDelRenglon = 0;
                        enElRenglon = 0;
                    }
                }

                if (enElRenglon > 0)
                {
                    y -= altoDelRenglon + (2 * separacion);
                }
            }

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            fallos.AddRange(dibujante.Fallos);

            if (placas is not null)
            {
                fallos.AddRange(placas.Fallos);
            }

            var resumen = $"{hechas} de {elegidas.Count} conexión(es) dibujada(s) en tamaño real, aparte: a la izquierda de X = {XDeLosDetalles:0}, un renglón por tipo de hoja.";

            if (fallos.Count > 0)
            {
                resumen += "\n\nAvisos:\n" + string.Join("\n", fallos.Distinct().Take(12));
            }

            StatusText.Text = $"Conexiones: {hechas} dibujadas.";
            MessageBox.Show(this, resumen, AppInfo.ProductName, MessageBoxButton.OK,
                fallos.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }
    }

    private static PlacaBaseDrawer NuevoDibujanteDePlacas(dynamic doc, double escala)
    {
        PlacaBaseDrawer d = new PlacaBaseDrawer(doc, escala);
        d.AsegurarCapas();
        return d;
    }

    /// <summary>Copia las placas base del libro a la hoja Placa Base, para revisarlas y ajustarlas ahi.</summary>
    private void OnPasarPlacasBaseDelLibro(object sender, RoutedEventArgs e)
    {
        var placas = _conexionesDelLibro.Where(f => f.Conexion.Modelo?.PlacaBase is not null).ToList();

        if (placas.Count == 0)
        {
            MessageBox.Show(this, "El libro no tiene hojas PLACA BASE (o todavía no se lee ninguno).",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var f in placas)
        {
            _datos.PlacasBase.Add(PlacaBaseDelLibroAFila(PlacaBaseDelLibro.Datos(f.Conexion.Hoja)));
        }

        StatusText.Text = $"{placas.Count} placa(s) base del libro pasadas a la hoja Placa Base.";
        MessageBox.Show(this, $"Se agregaron {placas.Count} placa(s) base a la hoja Placa Base. Ahí se ven en la vista previa, se ajustan y se mandan a Revit con «Para Revit…».",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// La placa base del libro como renglon de la hoja Placa Base.
    /// </summary>
    /// <remarks>
    /// En el libro, N va a lo largo del momento -el peralte de la columna- y las anclas van en las
    /// dos lineas extremas a ±f, con ny anclas cada una, mas las intermedias de los bordes largos.
    /// En la hoja Placa Base las anclas X son TODAS las de las dos hileras de los bordes largos
    /// (nx por hilera) y las Y, las que quedan entre ellas en los lados cortos (ny − 2 por lado).
    /// </remarks>
    private static PlacaBaseRow PlacaBaseDelLibroAFila(DatosPlacaBase d)
    {
        return new PlacaBaseRow
        {
            Marca = d.Marca,
            LargoCm = d.NCm,
            AnchoCm = d.BCm,
            Espesor = SinComillas(TextoMedida.Pulgadas(d.EspesorCm)),
            AceroPlaca = d.Acero,
            DadoXCm = d.DadoNCm,
            DadoYCm = d.DadoBCm,
            Familia = d.TipoColumna == "HSS" ? "OR" : FamiliaPerfil.Ir,
            Seccion = d.Perfil,
            NAnclasX = 2 * Math.Max(d.AnclasN, 2),
            NAnclasY = 2 * Math.Max(d.AnclasB - 2, 0),
            SepBordeXCm = Math.Round(d.BordeNCm, 2),
            SepBordeYCm = Math.Round(d.BordeBCm, 2),
            DiamAnclaX = SinComillas(TextoMedida.Pulgadas(d.DiametroAnclaCm)),
            DiamAnclaY = SinComillas(TextoMedida.Pulgadas(d.DiametroAnclaCm)),
            Electrodo = d.Electrodo,
            Soldadura = SinComillas(TextoMedida.Pulgadas(d.SoldaduraCm)),
            LongAnclajeXCm = d.AnclajeCm,
            LongAnclajeYCm = d.AnclajeCm,
            DoblezAnclaXCm = d.GanchoCm,
            DoblezAnclaYCm = d.GanchoCm,
            Grout = d.GroutCm > 0 ? "SI" : "NO",
            EspesorGroutCm = d.GroutCm > 0 ? d.GroutCm : 2.5,
            Escala = 10
        };
    }

    private static string SinComillas(string pulgadas) => pulgadas.TrimEnd('"');

    /// <summary>El simbolo de la simbologia de soldadura de CadLink para cada cordon.</summary>
    private static SimbolosSoldadura.Tipo Simbolo(SimboloDeSoldadura s) => s switch
    {
        SimboloDeSoldadura.FileteAmbosLados => SimbolosSoldadura.Tipo.FileteAmbosLados,
        SimboloDeSoldadura.TodoAlrededor => SimbolosSoldadura.Tipo.TodoAlrededor,
        SimboloDeSoldadura.BiselSimple => SimbolosSoldadura.Tipo.BiselSimple,
        _ => SimbolosSoldadura.Tipo.Filete
    };

    /// <summary>De las piezas del nucleo (m) al detalle del dibujante (cm).</summary>
    private static ConexionAceroCad ACad(ModeloConexion m, HojaLeida h)
    {
        var c = new ConexionAceroCad
        {
            Titulo = m.Titulo,
            Subtitulo = $"Hoja «{h.Hoja.Trim()}» del libro de acero · {m.Id}"
        };

        c.Renglones.AddRange(m.Renglones);
        c.Notas.AddRange(m.Avisos);

        static CapaConexion Capa(CapaDetalle d) => d switch
        {
            CapaDetalle.Perfil => CapaConexion.Perfil,
            CapaDetalle.PerfilSecundario => CapaConexion.PerfilSecundario,
            CapaDetalle.PerfilOculto => CapaConexion.Oculto,
            CapaDetalle.Eje => CapaConexion.Eje,
            CapaDetalle.Placa => CapaConexion.Placa,
            CapaDetalle.Tornillo => CapaConexion.Tornillo,
            CapaDetalle.Soldadura => CapaConexion.Soldadura,
            CapaDetalle.Cota => CapaConexion.Cota,
            _ => CapaConexion.Texto
        };

        foreach (var v in VistasConexion.Proyectar(m))
        {
            var vc = new VistaConexionCad
            {
                Titulo = v.Titulo,
                MinX = v.MinX * 100,
                MinY = v.MinY * 100,
                MaxX = v.MaxX * 100,
                MaxY = v.MaxY * 100
            };

            vc.Lineas.AddRange(v.Lineas.Select(l => new LineaConexion(l.X1 * 100, l.Y1 * 100, l.X2 * 100, l.Y2 * 100, Capa(l.Capa))));
            vc.Contornos.AddRange(v.Poligonos.Select(p => new ContornoConexion(
                p.Puntos.Select(q => (q.X * 100, q.Y * 100)).ToList(), p.Cerrado, Capa(p.Capa), p.Relleno)));
            vc.Circulos.AddRange(v.Circulos.Select(q => new CirculoConexion(q.X * 100, q.Y * 100, q.R * 100, Capa(q.Capa))));
            vc.Llamadas.AddRange(v.Llamadas.Select(q => q.Soldadura is { } s
                ? new LlamadaConexion(q.X * 100, q.Y * 100, q.Texto)
                {
                    Simbolo = Simbolo(s.Simbolo),
                    Tamano = s.Tamano,
                    Electrodo = s.Electrodo,
                    Descripcion = s.Descripcion
                }
                : new LlamadaConexion(q.X * 100, q.Y * 100, q.Texto)));
            vc.Cotas.AddRange(v.Cotas.Select(q => new CotaConexion(q.X1 * 100, q.Y1 * 100, q.X2 * 100, q.Y2 * 100, q.Nx, q.Ny, q.Texto)));
            c.Vistas.Add(vc);
        }

        return c;
    }
}
