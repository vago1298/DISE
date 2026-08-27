using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// El alzado <b>en 3D</b>: la jaula de armado vista en isométrico.
/// </summary>
/// <remarks>
/// <para>
/// Es la misma pieza que el alzado plano, con los mismos datos y las mismas posiciones
/// —los estribos salen de <c>Estribos.CentrosDeAlzado</c> y las varillas de
/// <c>PosicionesDeLecho</c>/<c>PosicionesLaterales</c>, igual que el corte—, solo
/// proyectada. Si se calcularan aparte, la vista en 3D enseñaría un armado y el plano
/// otro.
/// </para>
/// <para>
/// La proyección es un <b>isométrico</b> hecho a mano sobre un <c>Canvas</c>, sin
/// <c>Viewport3D</c>. Es la misma decisión que ya está razonada en <c>VistaModelo</c>: WPF
/// 3D no tiene primitiva de línea, y una jaula de armado es toda líneas.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Si el alzado se ve en 3D en lugar de plano.</summary>
    private bool _alzado3D;

    /// <summary>Largo que se supone cuando la fila no lo trae, en metros.</summary>
    /// <remarks>
    /// Tres metros es un tramo de trabe corriente. Hace falta un valor porque sin largo
    /// no hay alzado que dibujar, y dejarlo en cero enseñaba un recuadro vacío sin
    /// explicar por qué.
    /// </remarks>
    public const double LargoPorOmisionM = 3.0;

    private void OnAlternarAlzado3D(object sender, RoutedEventArgs e)
    {
        _alzado3D = !_alzado3D;
        AlzadoVistaButton.Content = _alzado3D ? "3D" : "2D";

        AlzadoVistaButton.ToolTip = _alzado3D
            ? "Viendo el alzado en 3D. Toca para volver al plano."
            : "Viendo el alzado plano. Toca para verlo en 3D.";

        DibujarVistaPrevia();
    }

    /// <summary>Dibuja la jaula de armado en isométrico.</summary>
    /// <param name="a">Los datos del alzado, los mismos que usa el dibujo plano.</param>
    /// <param name="izquierda">Desde dónde se puede ocupar el lienzo.</param>
    /// <param name="alto">Alto disponible.</param>
    private void DibujarAlzado3DPrevio(AlzadoCad a, double izquierda, double alto)
    {
        var largoM = a.LongitudM > 0 ? a.LongitudM : LargoPorOmisionM;

        if (a.BaseCm <= 0 || a.AlturaCm <= 0)
        {
            return;
        }

        // Todo en centímetros: el largo de la pieza va en X, el peralte en Y y la base
        // en Z. Así la pieza se ve tumbada, como en el alzado plano.
        var lx = largoM * 100.0;
        var hy = a.AlturaCm;
        var bz = a.BaseCm;

        // ---------- El isométrico ----------
        //
        // Los tres ejes a 120°, que es el isométrico de toda la vida: X y Z se abren en
        // diagonal y Y sube. cos30 y sen30 son las dos constantes que hacen falta.
        const double c30 = 0.86602540378443864;
        const double s30 = 0.5;

        // La escala sale de encajar la caja proyectada en el hueco que queda. Se calcula
        // con las esquinas de la caja, no con el largo a secas: en isométrico el ancho en
        // pantalla depende de los tres lados a la vez.
        var anchoIso = (lx + bz) * c30;
        var altoIso = hy + ((lx + bz) * s30);

        var anchoDisp = PreviaFijaCanvas.ActualWidth - izquierda - 24;
        var altoDisp = alto - 52;

        if (anchoDisp < 40 || altoDisp < 40 || anchoIso <= 0 || altoIso <= 0)
        {
            return;
        }

        var k = Math.Min(anchoDisp / anchoIso, altoDisp / altoIso);

        if (k <= 0 || double.IsInfinity(k))
        {
            return;
        }

        // El origen: la esquina de atrás a la izquierda de la caja, colocada de modo que
        // todo lo proyectado caiga dentro del hueco.
        var ox = izquierda + (bz * c30 * k);
        var oy = 34 + (hy * k) + (bz * s30 * k);

        Point P(double x, double y, double z) => new(
            ox + ((x - z) * c30 * k),
            oy - (y * k) + ((x + z) * s30 * k));

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var brochaEst = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xB2));
        var verde = new SolidColorBrush(Color.FromRgb(0x1D, 0x8A, 0x4E));

        void Linea3D(Point p, Point q, Brush brocha, double grosor, double opacidad = 1.0)
        {
            PreviaFijaCanvas.Children.Add(new Line
            {
                X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
                Stroke = brocha,
                StrokeThickness = grosor,
                Opacity = opacidad
            });
        }

        // ---------- La caja de concreto, en alambre ----------
        //
        // Va tenue y en alambre a propósito: lo que hay que mirar es el armado, y una
        // caja opaca lo taparía entero. Es lo mismo que hace el visor de ETABS con el
        // modelo.
        var esquinas = new[]
        {
            P(0, 0, 0), P(lx, 0, 0), P(lx, hy, 0), P(0, hy, 0),
            P(0, 0, bz), P(lx, 0, bz), P(lx, hy, bz), P(0, hy, bz)
        };

        var aristas = new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        };

        foreach (var (i, j) in aristas)
        {
            Linea3D(esquinas[i], esquinas[j], azul, 1.0, 0.45);
        }

        // ---------- Los estribos ----------
        //
        // Uno por cada posición que dice Estribos.CentrosDeAlzado, la MISMA función que
        // usa el dibujo plano y la que usa AutoCAD.
        var centros = Estribos.CentrosDeAlzado(
            largoM,
            a.SeparacionesCm[0] / 100, a.SeparacionesCm[1] / 100, a.SeparacionesCm[2] / 100,
            vertical: a.EsVertical,
            esColumna: a.Tipo == TipoElemento.Columna);

        var rec = a.RecubrimientoCm;

        if (rec > 0 && rec * 2 < bz && rec * 2 < hy)
        {
            foreach (var c in centros)
            {
                var x = c * 100.0;

                // El estribo es un rectángulo en el plano de la sección, o sea a X fija.
                var e = new[]
                {
                    P(x, rec, rec), P(x, rec, bz - rec),
                    P(x, hy - rec, bz - rec), P(x, hy - rec, rec)
                };

                for (var v = 0; v < 4; v++)
                {
                    Linea3D(e[v], e[(v + 1) % 4], brochaEst, 1.1);
                }
            }
        }

        // ---------- Las varillas longitudinales ----------
        //
        // De las mismas funciones que reparten las varillas del corte, así que la jaula
        // en 3D lleva exactamente las varillas que se ven en la sección.
        var fila = Seleccionada;

        if (fila is not null && !fila.EsCircular)
        {
            Varilla.TryDiametroCm(fila.Estribo, out var de);

            foreach (var (_, zx, vy, _) in TodasLasVarillas(fila, de, fila.RecubrimientoCm))
            {
                // En el corte la X es a lo ancho de la sección, que aquí es la Z; y la Y
                // del corte es el peralte, que aquí sigue siendo Y.
                Linea3D(P(0, vy, zx), P(lx, vy, zx), verde, 1.6);
            }
        }

        Etiqueta(PreviaFijaCanvas,
            $"ALZADO 3D  {a.TipoTexto}  {a.Id}", izquierda, 12);

        Etiqueta(PreviaFijaCanvas,
            $"L = {largoM:N2} m   ·   {centros.Count} estribos   ·   "
            + $"{a.SeparacionesCm[0]:N0}-{a.SeparacionesCm[1]:N0}-{a.SeparacionesCm[2]:N0} cm"
            + (a.LongitudM > 0 ? string.Empty : "   ·   largo por omisión"),
            izquierda, alto - 16);
    }
}


public partial class MainWindow
{
    /// <summary>
    /// La <b>sección en corte</b> vista en 3D: una rebanada de la pieza en isométrico.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la misma sección del corte plano, con las mismas varillas y el mismo estribo
    /// —salen de <see cref="TodasLasVarillas"/> y del recubrimiento de la fila—, solo
    /// proyectada y con un poco de fondo para que se vea que es un cuerpo y no un dibujo.
    /// </para>
    /// <para>
    /// Se dibuja una <b>rebanada corta</b>, no la pieza entera: lo que interesa aquí es el
    /// acomodo del armado en la sección, y una rebanada lo enseña sin tapar las varillas
    /// del fondo. La pieza completa ya se ve a la derecha, en el alzado en 3D.
    /// </para>
    /// </remarks>
    private void DibujarSeccion3DPrevia(SeccionConcretoRow s, double ancho, double alto)
    {
        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            return;
        }

        var bz = s.BaseCm;
        var hy = s.AlturaCm;

        // El fondo de la rebanada: un tercio del ancho de la sección, que da perspectiva
        // sin esconder las varillas de atrás.
        var lx = Math.Max(bz / 3.0, 6.0);

        const double c30 = 0.86602540378443864;
        const double s30 = 0.5;

        var anchoIso = (lx + bz) * c30;
        var altoIso = hy + ((lx + bz) * s30);

        // La mitad izquierda del lienzo, que es donde vive el corte.
        var anchoDisp = (ancho * 0.46) - 28;
        var altoDisp = alto - 76;

        if (anchoDisp < 30 || altoDisp < 30 || anchoIso <= 0 || altoIso <= 0)
        {
            return;
        }

        var k = Math.Min(anchoDisp / anchoIso, altoDisp / altoIso);

        if (k <= 0 || double.IsInfinity(k))
        {
            return;
        }

        var ox = 30 + (bz * c30 * k);
        var oy = 46 + (hy * k) + (bz * s30 * k);

        Point P(double x, double y, double z) => new(
            ox + ((x - z) * c30 * k),
            oy - (y * k) + ((x + z) * s30 * k));

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var brochaEst = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xB2));
        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        void L3(Point p, Point q, Brush brocha, double grosor, double opacidad = 1.0)
        {
            PreviewCanvas.Children.Add(new Line
            {
                X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
                Stroke = brocha,
                StrokeThickness = grosor,
                Opacity = opacidad
            });
        }

        // La cara del corte, rellena con el color del concreto: es la que da la idea de
        // estar mirando una pieza cortada y no un alambre.
        var cara = new PointCollection
        {
            P(0, 0, 0), P(0, 0, bz), P(0, hy, bz), P(0, hy, 0)
        };

        PreviewCanvas.Children.Add(new Polygon
        {
            Points = cara,
            Fill = new SolidColorBrush(Color.FromRgb(0xD4, 0xD8, 0xDC)),
            Stroke = azul,
            StrokeThickness = 1.3
        });

        // La rebanada, en alambre.
        var esquinas = new[]
        {
            P(0, 0, 0), P(lx, 0, 0), P(lx, hy, 0), P(0, hy, 0),
            P(0, 0, bz), P(lx, 0, bz), P(lx, hy, bz), P(0, hy, bz)
        };

        foreach (var (i, j) in new[]
        {
            (0, 1), (1, 2), (2, 3), (4, 5), (5, 6), (6, 7),
            (1, 5), (2, 6), (3, 7)
        })
        {
            L3(esquinas[i], esquinas[j], azul, 1.0, 0.5);
        }

        // El estribo: un anillo en la cara del corte y otro al fondo de la rebanada.
        var rec = s.RecubrimientoCm;

        if (rec > 0 && rec * 2 < bz && rec * 2 < hy)
        {
            foreach (var x in new[] { 0.0, lx })
            {
                var e = new[]
                {
                    P(x, rec, rec), P(x, rec, bz - rec),
                    P(x, hy - rec, bz - rec), P(x, hy - rec, rec)
                };

                for (var v = 0; v < 4; v++)
                {
                    L3(e[v], e[(v + 1) % 4], brochaEst, x == 0 ? 1.4 : 1.0, x == 0 ? 1 : 0.55);
                }
            }
        }

        // Las varillas: un tramo por cada una, con su bolita en la cara del corte, que es
        // como se ven en el corte plano.
        Varilla.TryDiametroCm(s.Estribo, out var de);

        foreach (var (_, zx, vy, r) in TodasLasVarillas(s, de, rec))
        {
            L3(P(0, vy, zx), P(lx, vy, zx), rojo, 1.5);

            var p = P(0, vy, zx);
            var rr = Math.Max(r * k, 1.6);

            var bolita = new Ellipse
            {
                Width = rr * 2,
                Height = rr * 2,
                Fill = rojo,
                Stroke = new SolidColorBrush(Color.FromRgb(0x7B, 0x24, 0x1B)),
                StrokeThickness = 0.7
            };

            Canvas.SetLeft(bolita, p.X - rr);
            Canvas.SetTop(bolita, p.Y - rr);
            PreviewCanvas.Children.Add(bolita);
        }
    }
}
