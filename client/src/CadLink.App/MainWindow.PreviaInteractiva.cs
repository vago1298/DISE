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

    /// <summary>
    /// Cuánto se tiene que mover el ratón para que deje de ser un clic y pase a ser un
    /// arrastre.
    /// </summary>
    /// <remarks>
    /// Cuatro píxeles es el margen que usa Windows para lo mismo. Con menos, el temblor
    /// normal de la mano al hacer clic contaría como arrastre; con mucho más, un
    /// desplazamiento corto de verdad se perdería y parecería que el dibujo se resiste.
    /// </remarks>
    private const double UmbralDeArrastrePx = 4.0;

    /// <summary>Desde dónde se está arrastrando, en coordenadas del contenedor.</summary>
    private Point _previaArrastreDesde;

    /// <summary>Dónde se apretó el botón, para medir el umbral contra el sitio inicial.</summary>
    /// <remarks>
    /// Es distinto de <see cref="_previaArrastreDesde"/>, que se va actualizando en cada
    /// movimiento. Si el umbral se midiera contra ese, nunca se alcanzaría: entre dos
    /// mensajes de ratón seguidos hay uno o dos píxeles, así que el movimiento siempre
    /// parecería demasiado pequeño y el dibujo no se movería jamás.
    /// </remarks>
    private Point _previaPresionEn;

    private bool _previaMoviendo;

    /// <summary>Si el arrastre ya pasó del umbral y por tanto no es un clic.</summary>
    private bool _previaHuboArrastre;

    /// <summary>Si este arrastre está <b>girando</b> el 3D en lugar de mover el dibujo.</summary>
    /// <remarks>
    /// Se decide al apretar el botón y no en cada movimiento: si se consultara el modo a
    /// mitad del arrastre, tocar el botón de 2D/3D con el ratón apretado cambiaría lo que
    /// hace el gesto a media faena.
    /// </remarks>
    private bool _previaGirando;

    /// <summary>Cuánto gira el 3D por píxel arrastrado.</summary>
    /// <remarks>
    /// Los mismos números que el visor de ETABS: medio grado por píxel en horizontal y
    /// cuatro décimas en vertical. La vertical va más despacio a propósito, porque su
    /// recorrido útil es de 180° y el de la horizontal 360°.
    /// </remarks>
    private const double GradosPorPixelAzimut = 0.5;

    private const double GradosPorPixelElevacion = 0.4;

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
        // El doble clic reajusta, PERO solo en el vacío.
        //
        // Este matiz es lo que deja convivir el encuadre con las grapas. Poner una
        // grapa son dos clics, y dos clics seguidos sobre varillas vecinas los reporta
        // Windows como un doble clic: sin la comprobación, marcar la segunda varilla
        // deshacía el zoom en lugar de poner la grapa.
        //
        // Sobre una varilla, un doble clic se trata como un clic normal y la marca.
        // En el vacío no hay nada que marcar, así que ahí sí reajusta.
        //
        // Canvas es un Panel, no un Control, así que NO tiene el evento
        // MouseDoubleClick: hay que contar los clics a mano.
        if (e.ChangedButton == MouseButton.Left
            && e.ClickCount == 2
            && VarillaEn(e.GetPosition(PreviewCanvas)) is null)
        {
            ReiniciarEncuadrePrevia();
            CancelarGrapaPendiente();
            DibujarVistaPrevia();
            e.Handled = true;
            return;
        }

        _previaArrastreDesde = e.GetPosition(PreviaHost);
        _previaPresionEn = _previaArrastreDesde;
        _previaHuboArrastre = false;
        _previaMoviendo = true;

        // EN 3D EL BOTÓN IZQUIERDO GIRA. El derecho sigue moviendo.
        //
        // Es el reparto del visor de ETABS, y aquí ahora tiene sentido: el comentario de
        // arriba decía que no había nada que girar «porque una sección es plana», y eso
        // dejó de ser verdad al levantarla en 3D. En el corte plano no cambia nada, los dos
        // botones siguen moviendo.
        _previaGirando = _alzado3D && e.ChangedButton == MouseButton.Left;

        // Sin capturar, al salirse del lienzo mientras se arrastra el botón se
        // suelta fuera, no llega el MouseUp y el dibujo se queda pegado al ratón.
        PreviewCanvas.CaptureMouse();

        // El cursor NO cambia todavía: hasta que se pase del umbral esto podría ser un
        // clic, y poner la mano de mover en un clic simple da la impresión de que el
        // dibujo se va a arrastrar cuando en realidad se está marcando una varilla.
    }

    /// <summary>Mueve el dibujo con el ratón, o realza la varilla que hay debajo.</summary>
    private void OnPreviaMouseMove(object sender, MouseEventArgs e)
    {
        // Sin botón apretado: lo único que se hace es realzar la varilla bajo el
        // cursor, para que se vea que son cosas que se pueden tocar.
        if (!_previaMoviendo)
        {
            if (ActualizarVarillaBajoCursor(e.GetPosition(PreviewCanvas)))
            {
                DibujarVistaPrevia();
            }

            return;
        }

        // Se mide contra PreviaHost, que NO lleva transformación, y no contra el
        // propio Canvas: GetPosition devuelve el punto en el espacio del elemento,
        // o sea deshaciendo su RenderTransform, así que medir contra el Canvas
        // mientras se le cambia esa transformación se realimenta y el dibujo se va
        // acelerando solo mientras se arrastra.
        var p = e.GetPosition(PreviaHost);

        // EL UMBRAL: hasta que el ratón no se mueve de verdad, esto sigue siendo un
        // clic en potencia y el dibujo no se toca.
        //
        // Es lo que permite que el mismo botón izquierdo sirva para mover el dibujo y
        // para marcar varillas, sin un modo que haya que encender. Nadie deja el ratón
        // completamente quieto al hacer clic, así que sin margen de tolerancia cada
        // clic movería el dibujo unos píxeles y marcar dos varillas dejaría la sección
        // descentrada.
        if (!_previaHuboArrastre)
        {
            var dxTotal = p.X - _previaPresionEn.X;
            var dyTotal = p.Y - _previaPresionEn.Y;

            if (Math.Sqrt((dxTotal * dxTotal) + (dyTotal * dyTotal)) < UmbralDeArrastrePx)
            {
                return;
            }

            _previaHuboArrastre = true;
            PreviewCanvas.Cursor = _previaGirando ? Cursors.SizeAll : Cursors.ScrollAll;
        }

        // ---------- Girando el 3D ----------
        if (_previaGirando)
        {
            _giro3DAzimut += (p.X - _previaArrastreDesde.X) * GradosPorPixelAzimut;

            // La elevación se limita: pasando de ±90° la vista se voltea y se pierde la
            // noción de qué es arriba. Es el mismo tope que el visor de ETABS.
            _giro3DElevacion = Math.Clamp(
                _giro3DElevacion + ((p.Y - _previaArrastreDesde.Y) * GradosPorPixelElevacion),
                -89, 89);

            _previaArrastreDesde = p;

            // Hay que redibujar: el giro cambia la proyección, y el encuadre se recalcula
            // con ella para que la pieza siga cabiendo al ponerse de perfil. Una
            // transformación del lienzo no podría hacer eso.
            DibujarVistaPrevia();
            return;
        }

        // El desplazamiento se suma tal cual, en píxeles: como en el XAML el
        // TranslateTransform va DESPUÉS del ScaleTransform, no hay que dividirlo
        // entre la escala. Así el dibujo acompaña al puntero exactamente, esté al
        // 30% o al 2000%.
        PreviaMueve.X += p.X - _previaArrastreDesde.X;
        PreviaMueve.Y += p.Y - _previaArrastreDesde.Y;

        _previaArrastreDesde = p;

        LimitarEncuadre3D();
    }

    /// <summary>Al salir el cursor, se apaga el realce.</summary>
    /// <remarks>
    /// Solo el realce. El arrastre NO se corta aquí —está razonado en
    /// <see cref="OnPreviaMouseUp"/>—, y por eso esto no hace nada mientras se mueve el
    /// dibujo: redibujar la sección entera en medio de un arrastre la dejaría a tirones.
    /// </remarks>
    private void OnPreviaMouseLeave(object sender, MouseEventArgs e)
    {
        if (_previaMoviendo || _varillaBajoCursor is null)
        {
            return;
        }

        _varillaBajoCursor = null;
        DibujarVistaPrevia();
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

        // El orden importa: apagar la bandera ANTES de soltar la captura.
        //
        // ReleaseMouseCapture dispara LostMouseCapture, que está enganchado a este
        // mismo método, así que esto se llama a sí mismo. Con la bandera ya apagada, esa
        // segunda entrada se va por el return de arriba y no pasa nada.
        _previaMoviendo = false;

        var fueArrastre = _previaHuboArrastre;
        _previaHuboArrastre = false;

        // Se apaga el giro y, si se estaba girando, se REDIBUJA fino: mientras se arrastra el
        // 3D se dibuja basto para que siga al ratón, y al soltar hay que rehacerlo con todo
        // el detalle. Sin apagar la bandera aquí, el dibujo se quedaría basto para siempre.
        var estabaGirando = _previaGirando;
        _previaGirando = false;

        if (estabaGirando)
        {
            DibujarVistaPrevia();
        }

        PreviewCanvas.ReleaseMouseCapture();
        PreviewCanvas.Cursor = null;

        // Si el ratón no se movió, esto no era un arrastre: era un clic, y va a las
        // grapas. Solo el botón izquierdo: el derecho es para mover.
        //
        // Se comprueba el tipo del argumento porque este método atiende tres eventos y
        // solo dos traen botón; LostMouseCapture llega como MouseEventArgs pelado, y
        // perder la captura no es un clic.
        if (!fueArrastre
            && e is MouseButtonEventArgs boton
            && boton.ChangedButton == MouseButton.Left)
        {
            ProcesarClicEnPrevia(e.GetPosition(PreviewCanvas));
        }
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

    private void OnPreviaAjustar(object sender, RoutedEventArgs e)
    {
        var era3D = _alzado3D;

        ReiniciarEncuadrePrevia();

        // En el corte plano no hace falta redibujar: volver la transformación a la identidad
        // ya devuelve el encuadre de siempre. En 3D SÍ, porque lo que se reinicia es el
        // GIRO, y el giro está metido en la proyección con la que se calcularon los puntos:
        // ninguna transformación del lienzo puede deshacerlo.
        if (era3D)
        {
            DibujarVistaPrevia();
        }
    }

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

        LimitarEncuadre3D();

        ActualizarZoomPreviaTexto();
    }

    /// <summary>
    /// En 3D, impide que la sección se monte encima del <b>alzado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El alzado va en el lienzo fijo, que no se mueve; la sección va en el que sí. Sin tope,
    /// al mover o acercar la sección se le metía por encima y las dos se leían como una sola
    /// maraña.
    /// </para>
    /// <para>
    /// El tope se pone sobre el borde DERECHO de lo dibujado, no sobre el recuadro: lo que
    /// molesta es que el acero cruce a la otra mitad, y el recuadro de la caja de concreto es
    /// más ancho que el acero. Los dos números los deja el dibujo del 3D, que es quien sabe
    /// dónde acabó cada cosa.
    /// </para>
    /// <para>
    /// Solo se topa por la derecha. Por la izquierda y en vertical se puede mover libremente:
    /// ahí no hay nada con lo que chocar, y limitarlo de más daría la impresión de que la
    /// vista está trabada.
    /// </para>
    /// </remarks>
    private void LimitarEncuadre3D()
    {
        if (!_alzado3D || _borde3DDerecha <= 0 || _limite3DDerecha <= 0)
        {
            return;
        }

        // Dónde cae ahora mismo en pantalla el borde derecho del dibujo.
        var enPantalla = (PreviaEscala.ScaleX * _borde3DDerecha) + PreviaMueve.X;

        if (enPantalla > _limite3DDerecha)
        {
            PreviaMueve.X -= enPantalla - _limite3DDerecha;
        }
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

        // Y el giro del 3D, que también es encuadre: «Ajustar» tiene que devolver la vista
        // a como estaba, y una sección girada de canto no es el encuadre de siempre.
        ReiniciarGiro3D();

        ActualizarZoomPreviaTexto();
    }

    private void ActualizarZoomPreviaTexto()
        => PreviaZoomText.Text = $"{PreviaEscala.ScaleX * 100:N0}%";
}
