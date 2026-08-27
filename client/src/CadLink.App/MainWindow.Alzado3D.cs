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
