using System.Windows;

// System.Windows.Controls es donde vive Canvas, y de ahi salen Canvas.SetLeft y
// Canvas.SetTop, que son las que colocan una figura dentro del lienzo. Sin este using el
// error que sale es un CS0103 diciendo que «Canvas» no existe, que despista bastante:
// parece que falte una referencia y lo que falta es el espacio de nombres.
using System.Windows.Controls;
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

            var varillasAlz = TodasLasVarillas(fila, de, fila.RecubrimientoCm);

            // Grosor REAL, como en la seccion de pie.
            foreach (var (_, zx, vy, vr) in varillasAlz)
            {
                // En el corte la X es a lo ancho de la sección, que aquí es la Z; y la Y
                // del corte es el peralte, que aquí sigue siendo Y.
                Linea3D(P(0, vy, zx), P(lx, vy, zx), verde, Math.Max(vr * 2 * k, 1.2));
            }

            // Las grapas y el diamante, a la altura de cada estribo.
            var grosorEstAlz = Math.Max(de * k, 1.0);

            foreach (var c in centros)
            {
                var xq = c * 100.0;

                foreach (var g in fila.Grapas)
                {
                    var va = BuscarVarillaPrevia(varillasAlz, g.A);
                    var vb = BuscarVarillaPrevia(varillasAlz, g.B);

                    if (va is null || vb is null)
                    {
                        continue;
                    }

                    Linea3D(P(xq, va.Value.Y, va.Value.X), P(xq, vb.Value.Y, vb.Value.X),
                            brochaEst, grosorEstAlz);
                }
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
    /// <summary>
    /// La sección en 3D: <b>el elemento de pie</b>, con sus estribos a la separación real.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No es una rebanada: es la pieza <b>levantada</b> a su longitud, con los estribos
    /// repartidos como dice la tabla. Lo que se ve en el corte es una sección; puesta de pie
    /// y con sus estribos, se ve el elemento.
    /// </para>
    /// <para>
    /// Las posiciones de los estribos salen de <c>Estribos.CentrosDeAlzado</c> y las
    /// varillas de <see cref="TodasLasVarillas"/>: las MISMAS funciones que el corte y que
    /// el dibujo de AutoCAD, así que las tres vistas no pueden discrepar.
    /// </para>
    /// </remarks>
    private void DibujarSeccion3DPrevia(SeccionConcretoRow s, double ancho, double alto)
    {
        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            return;
        }

        // De pie: el ancho de la sección en X, el fondo en Z y la LONGITUD en Y, que es la
        // que sube. Si la fila no trae largo se usa el de respaldo para poder dibujar.
        var largoM = s.LongitudM > 0 ? s.LongitudM : LargoPorOmisionM;

        var bx0 = s.BaseCm;
        var dz = s.AlturaCm;
        var hy = largoM * 100.0;

        const double c30 = 0.86602540378443864;
        const double s30 = 0.5;

        var anchoIso = (bx0 + dz) * c30;
        var altoIso = hy + ((bx0 + dz) * s30);

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

        var ox = 30 + (dz * c30 * k);
        var oy = 46 + (hy * k);

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

        // La caja del elemento, en alambre y tenue: lo que hay que mirar es el armado.
        var v = new[]
        {
            P(0, 0, 0), P(bx0, 0, 0), P(bx0, hy, 0), P(0, hy, 0),
            P(0, 0, dz), P(bx0, 0, dz), P(bx0, hy, dz), P(0, hy, dz)
        };

        foreach (var (i, j) in new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        })
        {
            L3(v[i], v[j], azul, 1.0, 0.4);
        }

        // Los estribos, a la separación de la tabla. En un elemento de pie el reparto es el
        // de columna, que es justo lo que dice esVertical.
        // Separaciones(...) es el mismo lector de la columna «Sep cm» que usa el dibujo de
        // AutoCAD, así que el reparto de aquí sale de lo que dice la tabla.
        var sep = Separaciones(s.SeparacionCm);

        var centros = Estribos.CentrosDeAlzado(
            largoM,
            sep[0] / 100, sep[1] / 100, sep[2] / 100,
            vertical: true,
            esColumna: true);

        var rec = s.RecubrimientoCm;

        // El diametro del estribo, que aqui es GROSOR y no solo una cota: el 3D es para ver
        // los espesores reales, asi que un #3 y un #4 tienen que verse distintos.
        Varilla.TryDiametroCm(s.Estribo, out var de);

        if (rec > 0 && rec * 2 < bx0 && rec * 2 < dz)
        {
            foreach (var c in centros)
            {
                var y = c * 100.0;

                var e = new[]
                {
                    P(rec, y, rec), P(bx0 - rec, y, rec),
                    P(bx0 - rec, y, dz - rec), P(rec, y, dz - rec)
                };

                for (var i = 0; i < 4; i++)
                {
                    BarraRedonda3D(e[i], e[(i + 1) % 4],
                        Color.FromRgb(0x1F, 0x6F, 0xB2), Math.Max(de * k, 1.4));
                }
            }
        }

        // Las varillas, de abajo arriba. La X del corte es la X, y su Y es el fondo.
        //
        // Grosor REAL: el diametro de la varilla a la escala del dibujo, no una linea
        // fija. Un #8 y un #3 tienen que verse distintos, como en la pieza.
        foreach (var (_, vx, vz, vr) in TodasLasVarillas(s, de, rec))
        {
            BarraRedonda3D(P(vx, 0, vz), P(vx, hy, vz),
                           Color.FromRgb(0xC0, 0x39, 0x2B), Math.Max(vr * 2 * k, 1.6));
        }

        // ===== LAS GRAPAS Y EL DIAMANTE, EN CADA ESTRIBO =====
        //
        // Se repiten a la misma altura que los estribos, porque van amarradas a ellos: una
        // grapa suelta en el aire no existe. Salen de las mismas funciones que el corte, asi
        // que si en la seccion hay tres grapas, aqui se ven tres.
        var varillas = TodasLasVarillas(s, de, rec);
        var grosorEst = Math.Max(de * k, 1.0);

        foreach (var c in centros)
        {
            var y = c * 100.0;

            foreach (var g in s.Grapas)
            {
                var va = BuscarVarillaPrevia(varillas, g.A);
                var vb = BuscarVarillaPrevia(varillas, g.B);

                if (va is null || vb is null)
                {
                    continue;
                }

                L3(P(va.Value.X, y, va.Value.Y), P(vb.Value.X, y, vb.Value.Y),
                   brochaEst, grosorEst);
            }

            if (s.LlevaDiamante)
            {
                DibujarDiamante3D(s, de, rec, y, P, brochaEst, grosorEst);
            }
        }
    }
}


public partial class MainWindow
{

    /// <summary>
    /// Una <b>barra redonda</b> en el 3D: cilíndrica, no una raya plana.
    /// </summary>
    /// <remarks>
    /// El volumen se consigue con dos cosas: las puntas <b>redondeadas</b>, que cierran el
    /// cilindro en lugar de cortarlo a escuadra, y un <b>degradado</b> a lo ancho —claro en
    /// el borde de la luz, oscuro en el otro— que es como se lee un tubo. Es la misma idea
    /// que usa el visor de ETABS para las barras extruidas: en un lienzo no hay iluminación,
    /// así que el relieve se pinta.
    /// <para>
    /// El degradado va PERPENDICULAR a la barra, así que se calcula con su dirección: un
    /// degradado fijo se vería girado en las barras que no van en el mismo sentido.
    /// </para>
    /// </remarks>
    private void BarraRedonda3D(Point p, Point q, Color color, double grueso)
    {
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 0.5 || grueso <= 0)
        {
            return;
        }

        // La normal en coordenadas de la propia barra: el degradado cruza su ancho.
        var nx = -dy / largo;
        var ny = dx / largo;

        Color Mezcla(Color c, double f) => Color.FromRgb(
            (byte)Math.Clamp(c.R * f, 0, 255),
            (byte)Math.Clamp(c.G * f, 0, 255),
            (byte)Math.Clamp(c.B * f, 0, 255));

        var brocha = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0.5 - (nx / 2), 0.5 - (ny / 2)),
            EndPoint = new Point(0.5 + (nx / 2), 0.5 + (ny / 2)),
            GradientStops =
            {
                new GradientStop(Mezcla(color, 0.55), 0.0),
                new GradientStop(Mezcla(color, 1.25), 0.35),
                new GradientStop(color, 0.62),
                new GradientStop(Mezcla(color, 0.5), 1.0)
            }
        };

        PreviewCanvas.Children.Add(new Line
        {
            X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
            Stroke = brocha,
            StrokeThickness = grueso,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    /// <summary>
    /// Busca una varilla por su señal en la tabla de la vista previa.
    /// </summary>
    /// <remarks>
    /// Igual que <c>BuscarVarilla</c>, pero devolviendo solo X e Y, que es lo que hace
    /// falta para colocar una grapa en el 3D. Devuelve <c>null</c> si la señal ya no
    /// apunta a nada, y entonces esa grapa se salta: es lo mismo que hace el dibujo del
    /// corte cuando el lecho se quedó con menos varillas.
    /// </remarks>
    private static (double X, double Y)? BuscarVarillaPrevia(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas, RefVarilla señal)
    {
        foreach (var v in varillas)
        {
            if (v.Ref.Equals(señal))
            {
                return (v.X, v.Y);
            }
        }

        return null;
    }

    /// <summary>
    /// El estribo <b>diamante</b> a una altura dada, en el 3D.
    /// </summary>
    /// <remarks>
    /// El recorrido sale de <see cref="TrazoDiamante"/>, la misma clase que usa el corte y
    /// el dibujante de AutoCAD, y se muestrea en tramos rectos porque el lienzo no tiene
    /// arcos. Si se calculara aquí, el diamante del 3D podría abrazar otras varillas que
    /// el de la sección.
    /// </remarks>
    private void DibujarDiamante3D(
        SeccionConcretoRow s, double de, double rec, double y,
        Func<double, double, double, Point> proyecta, Brush brocha, double grosor)
    {
        if (!Varilla.TryDiametroCm(
                string.IsNullOrWhiteSpace(s.DiamEstriboDiamante) ? s.Estribo : s.DiamEstriboDiamante,
                out var dDia) || dDia <= 0)
        {
            return;
        }

        var x1 = rec;
        var y1 = rec;
        var x2 = s.BaseCm - rec;
        var y2 = s.AlturaCm - rec;

        if (x2 <= x1 || y2 <= y1)
        {
            return;
        }

        var varSup = PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                       arriba: true, intermedio: false);

        var varInf = PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                       arriba: false, intermedio: false);

        var varLat = PosicionesLaterales(s, de, rec);

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dDia, varSup, varInf, varLat);

        if (centros is null)
        {
            return;
        }

        var geo = TrazoDiamante.Cinta(centros, 0);

        if (geo is null)
        {
            return;
        }

        var puntos = TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, 8);

        if (puntos.Count < 3)
        {
            return;
        }

        // Cerrado: el diamante es un estribo cerrado.
        for (var i = 0; i < puntos.Count; i++)
        {
            var a = puntos[i];
            var b = puntos[(i + 1) % puntos.Count];

            PreviewCanvas.Children.Add(new Line
            {
                X1 = proyecta(a.X, y, a.Y).X, Y1 = proyecta(a.X, y, a.Y).Y,
                X2 = proyecta(b.X, y, b.Y).X, Y2 = proyecta(b.X, y, b.Y).Y,
                Stroke = brocha,
                StrokeThickness = grosor
            });
        }
    }
}
