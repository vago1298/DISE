using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// Las <b>grapas</b> de la vista previa: elegir dos varillas con el ratón y poner
/// entre ellas el estribo suplementario.
/// </summary>
/// <remarks>
/// <para>
/// Va en un archivo parcial aparte, como el acero y las zapatas, porque son tres cosas
/// distintas juntas: saber dónde está cada varilla, convertir un clic en una varilla, y
/// dibujar la grapa. Meterlo en <c>MainWindow.xaml.cs</c>, que ya pasa de las cinco mil
/// líneas, lo dejaría enterrado entre el dibujo de la sección.
/// </para>
/// <para>
/// <b>Lo que esto NO hace todavía:</b> las grapas se ven en la vista previa y se
/// guardan en el proyecto, pero <b>no se dibujan en AutoCAD</b>. El dibujante
/// —<c>SeccionDrawer</c>— no se ha tocado. La geometría ya está en
/// <see cref="TrazoGrapa"/>, en la capa de CAD y sin WPF, justo para que llevarlas al
/// plano sea llamarla desde ahí y no reescribirla.
/// </para>
/// </remarks>
public partial class MainWindow
{
    // ======================================================================
    //  La transformada de la vista previa, guardada para poder deshacerla
    // ======================================================================
    //  DibujarVistaPrevia las calcula y las deja aquí. Un clic llega en píxeles y
    //  las varillas se conocen en centímetros, así que sin esto no hay manera de
    //  saber sobre qué varilla cayó el cursor.
    private double _previaEscala;
    private double _previaX0;
    private double _previaY0;
    private double _previaAlturaCm;

    /// <summary>La primera varilla marcada, esperando la segunda.</summary>
    private RefVarilla? _grapaPrimera;

    /// <summary>La varilla que el cursor tiene encima, para realzarla.</summary>
    private RefVarilla? _varillaBajoCursor;

    /// <summary>A cuántos píxeles del centro de una varilla cuenta como clic en ella.</summary>
    /// <remarks>
    /// Una varilla del #3 a la escala de este recuadro mide dos o tres píxeles: pedir
    /// puntería exacta sobre eso sería imposible. Con este alcance se puede marcar
    /// apuntando cerca, y como en el empate gana la más próxima, dos varillas juntas
    /// siguen siendo distinguibles.
    /// </remarks>
    private const double AlcanceDelClicPx = 9.0;

    // ======================================================================
    //  Dónde está cada varilla
    // ======================================================================

    /// <summary>
    /// <b>Todas</b> las varillas longitudinales de la sección, en centímetros y con su
    /// señal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Junta los cinco repartos que ya existían —los cuatro lechos y las laterales— sin
    /// recalcular nada: llama a <c>PosicionesDeLecho</c> y <c>PosicionesLaterales</c>,
    /// las mismas funciones con las que se pintan las varillas. Tiene que ser así, o
    /// las grapas se agarrarían de varillas que no son las que se ven dibujadas, que es
    /// el mismo error que ya está razonado en el comentario de <c>PosicionesDeLecho</c>.
    /// </para>
    /// <para>
    /// El orden de los cinco grupos y el índice dentro de cada uno son lo que
    /// <see cref="RefVarilla"/> guarda, así que <b>no se pueden reordenar</b> sin mover
    /// de sitio las grapas de los proyectos ya guardados.
    /// </para>
    /// </remarks>
    private static List<(RefVarilla Ref, double X, double Y, double R)> TodasLasVarillas(
        SeccionConcretoRow s, double de, double rec)
    {
        var salida = new List<(RefVarilla Ref, double X, double Y, double R)>();

        void Agregar(LechoVarilla lecho, List<(double X, double Y, double R)> posiciones)
        {
            for (var i = 0; i < posiciones.Count; i++)
            {
                salida.Add((new RefVarilla(lecho, i),
                            posiciones[i].X, posiciones[i].Y, posiciones[i].R));
            }
        }

        // Los mismos cuatro lechos, con los mismos diámetros efectivos, que
        // DibujarVistaPrevia le pasa a DibujarLecho.
        Agregar(LechoVarilla.EsquinaSuperior,
                PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                  arriba: true, intermedio: false));

        Agregar(LechoVarilla.IntermediaSuperior,
                PosicionesDeLecho(s, s.NIntSup, s.DiamIntSupEfectivo, de, rec,
                                  arriba: true, intermedio: true));

        Agregar(LechoVarilla.EsquinaInferior,
                PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                  arriba: false, intermedio: false));

        Agregar(LechoVarilla.IntermediaInferior,
                PosicionesDeLecho(s, s.NIntInf, s.DiamIntInfEfectivo, de, rec,
                                  arriba: false, intermedio: true));

        Agregar(LechoVarilla.Lateral, PosicionesLaterales(s, de, rec));

        return salida;
    }

    /// <summary>Busca una varilla por su señal, o <c>null</c> si ya no existe.</summary>
    /// <remarks>
    /// Devolver <c>null</c> es parte del diseño, no un descuido: si el lecho se quedó
    /// con menos varillas de las que había cuando se puso la grapa, esa grapa ya no
    /// señala a nada y se descarta sola al dibujar, en vez de agarrarse de otra varilla.
    /// </remarks>
    private static (double X, double Y, double R)? BuscarVarilla(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas, RefVarilla señal)
    {
        foreach (var v in varillas)
        {
            if (v.Ref.Equals(señal))
            {
                return (v.X, v.Y, v.R);
            }
        }

        return null;
    }

    /// <summary>Las varillas de la sección seleccionada, o una lista vacía.</summary>
    private List<(RefVarilla Ref, double X, double Y, double R)> VarillasDeLaSeleccionada()
    {
        var s = Seleccionada;

        if (s is null || s.EsCircular || s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            return new List<(RefVarilla, double, double, double)>();
        }

        Varilla.TryDiametroCm(s.Estribo, out var de);

        return TodasLasVarillas(s, de, s.RecubrimientoCm);
    }

    // ======================================================================
    //  De un clic a una varilla
    // ======================================================================

    /// <summary>Pasa un punto del lienzo a centímetros de la sección.</summary>
    /// <remarks>
    /// Es la inversa exacta de <c>PX</c>/<c>PY</c>. El punto tiene que venir de
    /// <c>e.GetPosition(PreviewCanvas)</c>: al medir contra el propio lienzo, WPF ya
    /// deshace su <c>RenderTransform</c>, así que el zoom y el encuadre no entran en
    /// esta cuenta y no hay que descontarlos a mano.
    /// </remarks>
    private (double X, double Y)? PreviaACm(Point p)
    {
        if (_previaEscala <= 0)
        {
            return null;
        }

        return ((p.X - _previaX0) / _previaEscala,
                _previaAlturaCm - ((p.Y - _previaY0) / _previaEscala));
    }

    /// <summary>Qué varilla hay en ese punto del lienzo, si hay alguna.</summary>
    private RefVarilla? VarillaEn(Point p)
    {
        var cm = PreviaACm(p);

        if (cm is null)
        {
            return null;
        }

        var alcanceCm = AlcanceDelClicPx / _previaEscala;

        RefVarilla? mejor = null;
        var mejorDistancia = double.MaxValue;

        foreach (var v in VarillasDeLaSeleccionada())
        {
            var dx = v.X - cm.Value.X;
            var dy = v.Y - cm.Value.Y;
            var distancia = Math.Sqrt((dx * dx) + (dy * dy));

            // El alcance es el radio de la varilla o el mínimo cómodo, el que sea
            // mayor: una varilla gruesa se marca tocándola, y una fina también se puede
            // marcar apuntando cerca.
            if (distancia > Math.Max(v.R, alcanceCm))
            {
                continue;
            }

            // En el empate gana la más cercana al cursor, que es la que uno quiso.
            if (distancia < mejorDistancia)
            {
                mejorDistancia = distancia;
                mejor = v.Ref;
            }
        }

        return mejor;
    }

    /// <summary>El diámetro con el que se arma la grapa que se ponga.</summary>
    /// <remarks>
    /// Si la lista está en blanco se usa el <b>estribo de la sección</b>, que es lo que
    /// se pone en obra cuando nadie lo especifica, en lugar de un calibre inventado.
    /// </remarks>
    private string DiametroGrapaElegido
    {
        get
        {
            if (GrapaDiametroCombo.SelectedItem is string elegido
                && !string.IsNullOrWhiteSpace(elegido))
            {
                return elegido;
            }

            var estribo = Seleccionada?.Estribo;

            return string.IsNullOrWhiteSpace(estribo) ? "#3" : estribo;
        }
    }

    /// <summary>
    /// Un clic en la vista previa: marca una varilla y, con la segunda, pone o quita la
    /// grapa.
    /// </summary>
    /// <remarks>
    /// Marcar el <b>mismo par</b> otra vez la quita. Es lo que evita tener que inventar
    /// un modo de borrado: la misma acción que la pone la saca, como una casilla.
    /// </remarks>
    private void ProcesarClicEnPrevia(Point p)
    {
        var s = Seleccionada;

        if (s is null)
        {
            return;
        }

        var tocada = VarillaEn(p);

        // Clic en el vacío: cancela lo que hubiera empezado.
        if (tocada is null)
        {
            if (_grapaPrimera is not null)
            {
                _grapaPrimera = null;
                StatusText.Text = "Grapa cancelada.";
                DibujarVistaPrevia();
            }

            return;
        }

        // Primera varilla: se queda marcada esperando la otra.
        if (_grapaPrimera is null)
        {
            _grapaPrimera = tocada;
            StatusText.Text = "Varilla marcada. Toca la segunda para poner la grapa.";
            DibujarVistaPrevia();
            return;
        }

        var primera = _grapaPrimera.Value;
        var segunda = tocada.Value;

        _grapaPrimera = null;

        // La misma varilla dos veces: se entiende como «déjalo».
        if (primera.Equals(segunda))
        {
            StatusText.Text = "Grapa cancelada.";
            DibujarVistaPrevia();
            return;
        }

        if (s.QuitarGrapa(primera, segunda))
        {
            StatusText.Text = $"Grapa quitada. Quedan {s.Grapas.Count}.";
        }
        else if (s.AgregarGrapa(primera, segunda, DiametroGrapaElegido))
        {
            StatusText.Text =
                $"Grapa {DiametroGrapaElegido} puesta. Van {s.Grapas.Count} en la sección.";
        }

        DibujarVistaPrevia();
    }

    /// <summary>
    /// Actualiza cuál es la varilla bajo el cursor y dice si <b>cambió</b>.
    /// </summary>
    /// <remarks>
    /// Devuelve si cambió para no redibujar la sección entera en cada píxel que se
    /// mueve el ratón: el dibujo se rehace de cero —<c>Children.Clear()</c>— y hacerlo
    /// en cada <c>MouseMove</c> dejaría la vista previa a tirones.
    /// </remarks>
    private bool ActualizarVarillaBajoCursor(Point p)
    {
        var antes = _varillaBajoCursor;
        _varillaBajoCursor = VarillaEn(p);

        return !Nullable.Equals(antes, _varillaBajoCursor);
    }

    /// <summary>Olvida la varilla marcada y la del cursor.</summary>
    private void CancelarGrapaPendiente()
    {
        _grapaPrimera = null;
        _varillaBajoCursor = null;
    }

    private void OnQuitarGrapas(object sender, RoutedEventArgs e)
    {
        var s = Seleccionada;

        if (s is null || s.Grapas.Count == 0)
        {
            StatusText.Text = "No hay grapas que quitar en esta sección.";
            return;
        }

        var cuantas = s.Grapas.Count;

        _grapaPrimera = null;
        s.LimpiarGrapas();

        StatusText.Text = $"Se quitaron {cuantas} grapa(s).";
        DibujarVistaPrevia();
    }

    // ======================================================================
    //  Dibujo
    // ======================================================================

    /// <summary>Las grapas de la sección, en la vista previa.</summary>
    /// <remarks>
    /// <para>
    /// La geometría sale de <see cref="TrazoGrapa.Contorno"/>, en la capa de CAD, por lo
    /// mismo que el diamante saca la suya de <c>TrazoDiamante</c>: el día que las grapas
    /// se dibujen en AutoCAD las dos tienen que salir del mismo sitio o acabarán siendo
    /// dos grapas distintas.
    /// </para>
    /// <para>
    /// Respeta los dos estilos igual que el estribo: en el tipo 2 el cuerpo va
    /// <b>relleno</b> con el mismo gris del estribo —el ACI 152 del dibujo—, y en el
    /// tipo 1 va hueco, con el trazo gris del contorno.
    /// </para>
    /// </remarks>
    /// <param name="rellenoConcreto">
    /// El color con el que se pinta el concreto en el estilo activo. Se usa como relleno
    /// de la grapa en el tipo 1, donde el estribo no lleva relleno: es lo que <b>tapa</b>
    /// la línea del estribo y la del diamante por debajo de la grapa, y produce el efecto
    /// de que la grapa pasa por arriba sin tener que recortarlas.
    /// </param>
    private void DibujarGrapasPrevias(
        SeccionConcretoRow s,
        List<(RefVarilla Ref, double X, double Y, double R)> varillas,
        Func<double, double> px, Func<double, double> py, bool conFondoSolido,
        Brush rellenoConcreto)
    {
        if (s.Grapas.Count == 0)
        {
            return;
        }

        var trazo = conFondoSolido
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            : new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));

        // En el tipo 2, el mismo ACI 152 del cuerpo del estribo: una grapa es un
        // estribo. En el tipo 1, el color del concreto, que hace de goma de borrar
        // sobre lo que la grapa cruza.
        //
        // Nunca null: sin relleno se vería la línea del estribo atravesando la grapa, que
        // es justo el defecto que se está corrigiendo.
        var relleno = conFondoSolido
            ? new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x7B))
            : rellenoConcreto;

        // ===== EL ORDEN: LA MÁS LARGA DEBAJO, LA MÁS CORTA ENCIMA =====
        //
        // Se resuelven primero todas las grapas y se ordenan con la regla compartida
        // TrazoGrapa.ClaveDeOrden, la MISMA que usa el dibujante de AutoCAD, para que la
        // pantalla y el plano no pongan encima a grapas distintas.
        //
        // Aquí el orden es todo lo que hace falta: como cada grapa se pinta con relleno
        // opaco, la que se dibuja después tapa el trozo de la anterior por donde se
        // cruzan, y eso ES el efecto de pasar por encima. En AutoCAD no basta y el
        // recorte hay que hacerlo de verdad.
        var resueltas = new List<(GrapaSeccion G,
                                  (double X, double Y, double R) A,
                                  (double X, double Y, double R) B,
                                  double Dg)>();

        foreach (var g in s.Grapas)
        {
            var va = BuscarVarilla(varillas, g.A);
            var vb = BuscarVarilla(varillas, g.B);

            // La sección cambió y una de las dos varillas ya no existe.
            if (va is null || vb is null)
            {
                continue;
            }

            if (!Varilla.TryDiametroCm(g.Diametro, out var dgg) || dgg <= 0)
            {
                continue;
            }

            resueltas.Add((g, va.Value, vb.Value, dgg));
        }

        resueltas.Sort((p, q) =>
        {
            var kp = TrazoGrapa.ClaveDeOrden(p.A.X, p.A.Y, p.B.X, p.B.Y);
            var kq = TrazoGrapa.ClaveDeOrden(q.A.X, q.A.Y, q.B.X, q.B.Y);

            var porLargo = kp.Primero.CompareTo(kq.Primero);

            return porLargo != 0 ? porLargo : kp.Segundo.CompareTo(kq.Segundo);
        });

        foreach (var (g, a, b, dg) in resueltas)
        {
            // El gancho de la sección es el largo de las colas. Si no está capturado se
            // usan seis diámetros, que es el mínimo de norma para un doblez sísmico.
            var cola = s.GanchoCm > 0 ? s.GanchoCm : dg * 6;

            var contorno = TrazoGrapa.Contorno(
                a.X, a.Y, a.R,
                b.X, b.Y, b.R,
                dg, cola);

            if (contorno is null || contorno.Count < 3)
            {
                continue;
            }

            var puntos = new PointCollection();

            foreach (var (x, y) in contorno)
            {
                puntos.Add(new Point(px(x), py(y)));
            }

            PreviewCanvas.Children.Add(new Polygon
            {
                Points = puntos,
                Stroke = trazo,
                StrokeThickness = 1.0,
                Fill = relleno
            });
        }
    }

    /// <summary>
    /// El realce de la varilla marcada y de la que está bajo el cursor.
    /// </summary>
    /// <remarks>
    /// Es un anillo <b>alrededor</b> de la varilla y no un cambio de color de la
    /// varilla misma: el color de la varilla dice su calibre, y pisarlo para señalar una
    /// selección haría que el dibujo mintiera sobre el armado.
    /// </remarks>
    private void DibujarRealceDeVarillas(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas,
        double escala, Func<double, double> px, Func<double, double> py)
    {
        if (_grapaPrimera is null && _varillaBajoCursor is null)
        {
            return;
        }

        foreach (var v in varillas)
        {
            var marcada = _grapaPrimera is not null && _grapaPrimera.Value.Equals(v.Ref);
            var bajoCursor = _varillaBajoCursor is not null
                             && _varillaBajoCursor.Value.Equals(v.Ref);

            if (!marcada && !bajoCursor)
            {
                continue;
            }

            // El mismo suelo de 1.8 px que usa Barra, para que el anillo no quede dentro
            // de la varilla cuando el calibre es fino y la escala pequeña.
            var r = Math.Max(v.R * escala, 1.8) + (marcada ? 4.5 : 3.0);

            var anillo = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = null,
                Stroke = marcada
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0x7E, 0x04))   // naranja
                    : new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B)),  // azul de marca
                StrokeThickness = marcada ? 2.2 : 1.4
            };

            Canvas.SetLeft(anillo, px(v.X) - r);
            Canvas.SetTop(anillo, py(v.Y) - r);

            PreviewCanvas.Children.Add(anillo);
        }
    }
}
