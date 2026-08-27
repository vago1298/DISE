using System.Windows;
using System.Windows.Input;

namespace CadLink.App;

/// <summary>
/// El <b>encuadre</b> de la vista previa de secciones de concreto: mover con el
/// ratón, acercar con la rueda y volver al ajuste original.
/// </summary>
/// <remarks>
/// <para>
/// Va en un archivo parcial aparte porque no tiene nada que ver con el dibujo. En
/// <c>MainWindow.xaml.cs</c> está QUÉ se pinta —concreto, estribo, lechos, cotas— y
/// aquí está DESDE DÓNDE se mira. Son dos cosas independientes: el dibujo se rehace
/// entero en cada tecla que se toca en la tabla, y el encuadre tiene que sobrevivir a
/// esos redibujados.
/// </para>
/// <para>
/// Justo por eso el zoom es un <c>RenderTransform</c> del propio <c>PreviewCanvas</c>
/// —declarado en el XAML— y no una transformación de las figuras:
/// <c>DibujarVistaPrevia</c> arranca con <c>Children.Clear()</c>, así que cualquier
/// transformación puesta en los hijos se perdería al editar una celda y la vista
/// saltaría de golpe a su posición original en medio del trabajo.
/// </para>
/// <para>
/// Y por eso mismo el encuadre <b>no</b> se toca al redibujar: el dibujo sigue
/// calculando su escala de ajuste con <c>ActualWidth</c>/<c>ActualHeight</c>, que un
/// <c>RenderTransform</c> no altera porque no interviene en el layout. Las dos escalas
/// —la de ajuste y la del zoom— se multiplican sin estorbarse.
/// </para>
/// </remarks>
public partial class MainWindow
{
    // ======================================================================
    //  Límites del zoom
    // ======================================================================
    //  Por debajo de 0.3 la sección queda del tamaño de una moneda y no se
    //  distingue una varilla de un estribo; por encima de 20 se ve el interior de
    //  un solo redondo y se pierde de vista la pieza. Son topes de sentido común,
    //  del mismo estilo que los del visor de ETABS (0.08 a 60), pero más
    //  cerrados: aquí se mira UNA sección, no un edificio entero.
    private const double PreviaZoomMin = 0.3;
    private const double PreviaZoomMax = 20.0;

    /// <summary>Cuánto acerca un golpe de rueda o un toque de los botones.</summary>
    /// <remarks>
    /// 1.15 da unos ocho pasos entre el ajuste original y el tope: suficiente para
    /// llegar rápido a un detalle sin pasarse de largo. El visor de ETABS usa 1.12 por
    /// lo mismo.
    /// </remarks>
    private const double PreviaPasoZoom = 1.15;

    /// <summary>Desde dónde se está arrastrando, en coordenadas del contenedor.</summary>
    private Point _previaArrastreDesde;

    private bool _previaMoviendo;

    // ======================================================================
    //  Mover arrastrando
    // ======================================================================

    /// <summary>Empieza a mover el dibujo, o lo reajusta si es un doble clic.</summary>
    /// <remarks>
    /// Sirven los dos botones del ratón. En el visor de ETABS el izquierdo gira y solo
    /// el derecho mueve, pero aquí no hay nada que girar: una sección es plana, así que
    /// reservar el izquierdo no ganaría nada y arrastrar con él —que es lo que hace
    /// cualquiera— parecería que la vista está clavada. Es la misma corrección que ya
    /// se le hizo a la vista en planta.
    /// </remarks>
    private void OnPreviaMouseDown(object sender, MouseButtonEventArgs e)
    {
        // El doble clic reajusta. Es el atajo de siempre para «devuélveme la vista»,
        // y evita tener que apuntar al botón de Ajustar cuando uno se ha perdido
        // dentro del dibujo.
        //
        // Canvas es un Panel, no un Control, así que NO tiene el evento
        // MouseDoubleClick: hay que contar los clics a mano.
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ReiniciarEncuadrePrevia();
            e.Handled = true;
            return;
        }

        _previaArrastreDesde = e.GetPosition(PreviaHost);
        _previaMoviendo = true;

        // Sin capturar, al salirse del lienzo mientras se arrastra el botón se
        // suelta fuera, no llega el MouseUp y el dibujo se queda pegado al ratón.
        PreviewCanvas.CaptureMouse();
        PreviewCanvas.Cursor = Cursors.ScrollAll;
    }

    /// <summary>Mueve el dibujo con el ratón.</summary>
    private void OnPreviaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_previaMoviendo)
        {
            return;
        }

        // Se mide contra PreviaHost, que NO lleva transformación, y no contra el
        // propio Canvas: GetPosition devuelve el punto en el espacio del elemento,
        // o sea deshaciendo su RenderTransform, así que medir contra el Canvas
        // mientras se le cambia esa transformación se realimenta y el dibujo se va
        // acelerando solo mientras se arrastra.
        var p = e.GetPosition(PreviaHost);

        // El desplazamiento se suma tal cual, en píxeles: como en el XAML el
        // TranslateTransform va DESPUÉS del ScaleTransform, no hay que dividirlo
        // entre la escala. Así el dibujo acompaña al puntero exactamente, esté al
        // 30% o al 2000%.
        PreviaMueve.X += p.X - _previaArrastreDesde.X;
        PreviaMueve.Y += p.Y - _previaArrastreDesde.Y;

        _previaArrastreDesde = p;
    }

    /// <summary>Suelta el dibujo.</summary>
    /// <remarks>
    /// <para>
    /// Está enganchado también a <c>LostMouseCapture</c>, como red de seguridad: si otra
    /// ventana roba el foco a mitad del arrastre, el <c>MouseUp</c> no llega nunca y sin
    /// esto el dibujo se quedaría pegado al puntero.
    /// </para>
    /// <para>
    /// A <c>MouseLeave</c> NO se engancha, y es a propósito, aunque los lienzos del visor
    /// de ETABS sí lo hagan. Ahí el lienzo mide 430 px de alto y salirse es raro; este
    /// recuadro tiene 230 y al mover el dibujo se llega al borde constantemente. Con
    /// <c>MouseLeave</c> el arrastre se cortaría justo ahí, que es lo contrario de lo que
    /// se quiere. Capturar el ratón ya garantiza que el <c>MouseUp</c> llegue aunque el
    /// puntero esté fuera del lienzo, así que cortar al salir no aporta nada.
    /// </para>
    /// </remarks>
    private void OnPreviaMouseUp(object sender, MouseEventArgs e)
    {
        if (!_previaMoviendo)
        {
            return;
        }

        _previaMoviendo = false;
        PreviewCanvas.ReleaseMouseCapture();
        PreviewCanvas.Cursor = null;
    }

    // ======================================================================
    //  Zoom
    // ======================================================================

    /// <summary>Acerca y aleja con la rueda, dejando quieto lo que hay bajo el puntero.</summary>
    private void OnPreviaWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? PreviaPasoZoom : 1 / PreviaPasoZoom;

        // El ancla es el puntero: se hace zoom SOBRE el detalle que se está mirando,
        // que es lo que hace AutoCAD. Anclar al centro obligaría a mover el dibujo
        // después de cada golpe de rueda para recuperar lo que se estaba viendo.
        AplicarZoomPrevia(PreviaEscala.ScaleX * factor, e.GetPosition(PreviaHost));

        // Sin esto la rueda ADEMÁS desplaza el ScrollViewer de la pestaña, y la hoja
        // entera se va de la pantalla mientras uno cree estar haciendo zoom. Es el
        // mismo motivo por el que los lienzos del visor de ETABS marcan el evento.
        e.Handled = true;
    }

    private void OnPreviaAcercar(object sender, RoutedEventArgs e)
        => AplicarZoomPrevia(PreviaEscala.ScaleX * PreviaPasoZoom, CentroPrevia());

    private void OnPreviaAlejar(object sender, RoutedEventArgs e)
        => AplicarZoomPrevia(PreviaEscala.ScaleX / PreviaPasoZoom, CentroPrevia());

    private void OnPreviaAjustar(object sender, RoutedEventArgs e) => ReiniciarEncuadrePrevia();

    /// <summary>El centro del recuadro, que es el ancla de los botones de zoom.</summary>
    /// <remarks>
    /// Los botones no tienen puntero al que agarrarse —el ratón está sobre el botón,
    /// arriba a la derecha, no sobre lo que se quiere mirar—, así que anclan al centro
    /// de la vista, que es lo que uno espera al pulsar «+».
    /// </remarks>
    private Point CentroPrevia() => new(PreviaHost.ActualWidth / 2, PreviaHost.ActualHeight / 2);

    /// <summary>
    /// Fija la escala del zoom dejando <b>quieto</b> el punto <paramref name="ancla"/>.
    /// </summary>
    /// <param name="escalaDeseada">
    /// Escala pedida. Se recorta a <see cref="PreviaZoomMin"/>–<see cref="PreviaZoomMax"/>.
    /// </param>
    /// <param name="ancla">
    /// El punto que no se debe mover, en coordenadas de <c>PreviaHost</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// La cuenta sale de la propia transformación. Con el <c>ScaleTransform</c> antes del
    /// <c>TranslateTransform</c>, un punto <c>p</c> del dibujo cae en pantalla en:
    /// </para>
    /// <code>pantalla = (k * p) + t</code>
    /// <para>
    /// De ahí, el punto del dibujo que ahora mismo está bajo el ancla es
    /// <c>p = (ancla - t) / k</c>. Para que siga estando bajo el ancla con la escala
    /// nueva <c>k'</c> hace falta <c>t' = ancla - (k' * p)</c>. Eso es todo lo que hacen
    /// las cuatro líneas de abajo.
    /// </para>
    /// </remarks>
    private void AplicarZoomPrevia(double escalaDeseada, Point ancla)
    {
        var k = PreviaEscala.ScaleX;
        var kNueva = Math.Clamp(escalaDeseada, PreviaZoomMin, PreviaZoomMax);

        // Al llegar a un tope, seguir girando la rueda no debe correr el dibujo: sin
        // esta salida el ancla se recalcularía con la MISMA escala y el desplazamiento
        // iría acumulando error hasta sacar la sección del cuadro.
        if (Math.Abs(kNueva - k) < 1e-9)
        {
            return;
        }

        // El punto del dibujo que está bajo el ancla, antes de cambiar la escala.
        var px = (ancla.X - PreviaMueve.X) / k;
        var py = (ancla.Y - PreviaMueve.Y) / k;

        PreviaEscala.ScaleX = kNueva;
        PreviaEscala.ScaleY = kNueva;

        PreviaMueve.X = ancla.X - (kNueva * px);
        PreviaMueve.Y = ancla.Y - (kNueva * py);

        ActualizarZoomPreviaTexto();
    }

    /// <summary>Devuelve la vista previa a su encuadre original.</summary>
    /// <remarks>
    /// Basta con volver la transformación a la identidad: la escala con la que el dibujo
    /// se ajusta al recuadro la calcula <c>DibujarVistaPrevia</c> por su cuenta, en
    /// píxeles reales, así que con el zoom al 100% y sin desplazamiento se ve
    /// exactamente el encuadre de siempre. No hace falta redibujar.
    /// </remarks>
    private void ReiniciarEncuadrePrevia()
    {
        PreviaEscala.ScaleX = 1;
        PreviaEscala.ScaleY = 1;
        PreviaMueve.X = 0;
        PreviaMueve.Y = 0;

        ActualizarZoomPreviaTexto();
    }

    private void ActualizarZoomPreviaTexto()
        => PreviaZoomText.Text = $"{PreviaEscala.ScaleX * 100:N0}%";
}
