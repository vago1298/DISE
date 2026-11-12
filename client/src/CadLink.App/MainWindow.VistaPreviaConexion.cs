using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CadLink.App.Models;
using CadLink.Cad;
using CadLink.Revit.Nucleo;
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

/// <summary>
/// La vista previa de la subpestaña «Capturar a mano»: la conexion elegida tal como se dibujaria
/// en AutoCAD.
/// </summary>
/// <remarks>
/// No es un dibujo aparte: el mismo <see cref="ConexionAceroDrawer"/> hace el acomodo en su modo
/// <see cref="ConexionAceroDrawer.ParaVistaPrevia"/> y aqui solo se pintan sus trazos. Lo que se ve
/// -llamadas, simbolos, cotas, tablas- es lo que sale en el plano. Los colores son los de las capas
/// del estilo, sobre fondo oscuro como el de AutoCAD.
/// </remarks>
public partial class MainWindow
{
    private DispatcherTimer? _previaConexion;

    /// <summary>Pide la vista previa; se redibuja un momento despues, no a cada tecla.</summary>
    private void PedirVistaPreviaConexionAMano()
    {
        if (_previaConexion is null)
        {
            _previaConexion = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _previaConexion.Tick += (_, _) =>
            {
                _previaConexion.Stop();
                DibujarVistaPreviaConexionAMano();
            };
        }

        _previaConexion.Stop();
        _previaConexion.Start();
    }

    private void DibujarVistaPreviaConexionAMano()
    {
        var lienzo = ConexionAManoPreviewCanvas;

        if (lienzo is null)
        {
            return;
        }

        lienzo.Children.Clear();
        ConexionAManoPreviewText.Text = string.Empty;

        if (ConexionesAManoLista.SelectedItem is not ConexionAMano c)
        {
            ConexionAManoPreviewText.Text = "Elige una conexion de la lista para verla.";
            return;
        }

        List<TrazoConexion> trazos;

        try
        {
            var (m, h) = ConexionesAMano.Armar(c.Codigo, c.Titulo, c.Valores(), PerfilDelCatalogo, out var porQueNo);

            if (m is null)
            {
                ConexionAManoPreviewText.Text = "No se puede armar: " + porQueNo;
                return;
            }

            if (m.PlacaBase is not null)
            {
                ConexionAManoPreviewText.Text = "La placa base se dibuja con el detalle de la hoja Placa Base: pasala a esa hoja para verla.";
                return;
            }

            var dibujante = ConexionAceroDrawer.ParaVistaPrevia(0.01);
            dibujante.Dibujar(ACad(m, h), 0, 0);
            trazos = dibujante.Grabado.ToList();
        }
        catch (Exception ex)
        {
            ConexionAManoPreviewText.Text = "No se pudo armar la vista previa: " + ex.Message;
            return;
        }

        PintarTrazos(lienzo, trazos);
    }

    /// <summary>Pinta los trazos del dibujante en el lienzo, encuadrados y con la Y hacia abajo.</summary>
    private static void PintarTrazos(Canvas lienzo, IReadOnlyList<TrazoConexion> trazos)
    {
        var ancho = lienzo.ActualWidth > 0 ? lienzo.ActualWidth : lienzo.Width;
        var alto = lienzo.ActualHeight > 0 ? lienzo.ActualHeight : lienzo.Height;

        if (trazos.Count == 0 || !(ancho > 0) || !(alto > 0))
        {
            return;
        }

        // El encuadre, con lo que ocupan los textos (a ojo, como los mide el dibujante).
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

        void Incluir(double x, double y)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        foreach (var t in trazos)
        {
            for (var i = 0; i + 1 < t.Puntos.Length; i += 2)
            {
                Incluir(t.Puntos[i], t.Puntos[i + 1]);
            }

            if (t.Tipo == TipoTrazo.Texto && t.Puntos.Length >= 2)
            {
                var w = t.Texto.Split('\n').Max(r => r.Length) * 0.56 * t.Alto;
                Incluir(t.Anclaje == 6 ? t.Puntos[0] - w : t.Puntos[0] + w, t.Puntos[1]);
            }
        }

        const double margen = 12;
        var escala = Math.Min((ancho - (2 * margen)) / Math.Max(1e-9, maxX - minX), (alto - (2 * margen)) / Math.Max(1e-9, maxY - minY));

        if (!(escala > 0) || double.IsInfinity(escala))
        {
            return;
        }

        // Centrado en el lienzo.
        var dx = margen + (((ancho - (2 * margen)) - ((maxX - minX) * escala)) / 2);
        var dy = margen + (((alto - (2 * margen)) - ((maxY - minY) * escala)) / 2);
        Point P(double x, double y) => new(dx + ((x - minX) * escala), dy + ((maxY - y) * escala));

        var lineas = new Dictionary<CapaConexion, GeometryGroup>();
        var rellenos = new Dictionary<CapaConexion, GeometryGroup>();

        GeometryGroup Grupo(Dictionary<CapaConexion, GeometryGroup> d, CapaConexion capa)
        {
            if (!d.TryGetValue(capa, out var g))
            {
                g = new GeometryGroup();
                d[capa] = g;
            }

            return g;
        }

        Geometry Poligonal(double[] a, bool cerrada)
        {
            var puntos = new List<Point>();

            for (var i = 0; i + 1 < a.Length; i += 2)
            {
                puntos.Add(P(a[i], a[i + 1]));
            }

            var figura = new PathFigure(puntos[0], new PathSegment[] { new PolyLineSegment(puntos.Skip(1), true) }, cerrada);
            return new PathGeometry(new[] { figura });
        }

        var textos = new List<TrazoConexion>();

        foreach (var t in trazos)
        {
            switch (t.Tipo)
            {
                case TipoTrazo.Linea when t.Puntos.Length >= 4:
                    Grupo(lineas, t.Capa).Children.Add(new LineGeometry(P(t.Puntos[0], t.Puntos[1]), P(t.Puntos[2], t.Puntos[3])));
                    break;
                case TipoTrazo.Polilinea when t.Puntos.Length >= 4:
                    Grupo(lineas, t.Capa).Children.Add(Poligonal(t.Puntos, t.Cerrado));
                    break;
                case TipoTrazo.Relleno when t.Puntos.Length >= 6:
                    Grupo(rellenos, t.Capa).Children.Add(Poligonal(t.Puntos, true));
                    break;
                case TipoTrazo.Circulo when t.Puntos.Length >= 2:
                    var r = Math.Max(0.8, t.Radio * escala);
                    Grupo(lineas, t.Capa).Children.Add(new EllipseGeometry(P(t.Puntos[0], t.Puntos[1]), r, r));
                    break;
                case TipoTrazo.Cota when t.Puntos.Length >= 6:
                    Grupo(lineas, t.Capa).Children.Add(new LineGeometry(P(t.Puntos[0], t.Puntos[1]), P(t.Puntos[2], t.Puntos[3])));
                    textos.Add(t with { Puntos = new[] { t.Puntos[4], t.Puntos[5] }, Anclaje = 5 });
                    break;
                case TipoTrazo.Texto when t.Puntos.Length >= 2:
                    textos.Add(t);
                    break;
            }
        }

        foreach (var (capa, g) in rellenos)
        {
            var tinta = Tinta(capa);
            lienzo.Children.Add(new FormaPath { Data = g, Fill = tinta, Opacity = 0.75 });
        }

        foreach (var (capa, g) in lineas)
        {
            lienzo.Children.Add(new FormaPath { Data = g, Stroke = Tinta(capa), StrokeThickness = 0.8 });
        }

        foreach (var t in textos)
        {
            // La altura del MTEXT es la de las mayusculas: la letra de pantalla es algo mayor.
            var h = t.Alto * escala;
            var rotulo = new TextBlock
            {
                Text = t.Texto,
                Foreground = Tinta(t.Capa),
                FontSize = Math.Max(2.5, h / 0.72),
                FontFamily = new FontFamily(ConexionAceroCapas.Fuente + ", Bahnschrift, Segoe UI"),
                LineHeight = Math.Max(3, h * 1.667),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = t.Anclaje is 2 or 5 ? TextAlignment.Center : t.Anclaje == 6 ? TextAlignment.Right : TextAlignment.Left
            };

            rotulo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = rotulo.DesiredSize.Width;
            var hh = rotulo.DesiredSize.Height;
            var p = P(t.Puntos[0], t.Puntos[1]);

            var x = t.Anclaje switch
            {
                2 or 5 => p.X - (w / 2),
                6 => p.X - w,
                _ => p.X
            };

            var y = t.Anclaje is 1 or 2 ? p.Y - (0.2 * h) : p.Y - (hh / 2);
            Canvas.SetLeft(rotulo, x);
            Canvas.SetTop(rotulo, y);
            lienzo.Children.Add(rotulo);
        }
    }

    /// <summary>El color de la capa del estilo, como en AutoCAD.</summary>
    private static Brush Tinta(CapaConexion capa)
    {
        var aci = ConexionAceroCapas.ColorDeCapa(capa);
        return PincelAci(aci is > 0 and < 256 ? aci : 7);
    }
}
