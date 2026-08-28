using System.Windows;

// System.Windows.Controls es donde vive Canvas, y de ahi salen Canvas.SetLeft y
// Canvas.SetTop. Sin este using el error que sale es un CS0103 diciendo que «Canvas» no
// existe, que despista bastante: parece que falte una referencia y lo que falta es el
// espacio de nombres.
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La sección de concreto <b>en 3D</b>: la jaula de armado del elemento, girable.
/// </summary>
/// <remarks>
/// <para>
/// Es el mismo armado del corte, con las mismas varillas, el mismo estribo y las mismas
/// grapas —salen de <see cref="TodasLasVarillas"/>, <see cref="TrazoEstribo"/> y
/// <see cref="TrazoDiamante"/>, las mismas funciones que el corte y que el dibujante de
/// AutoCAD—, solo levantado a su longitud y proyectado. Si se calcularan aparte, la vista en
/// 3D enseñaría un armado y el plano otro.
/// </para>
/// <para>
/// <b>La proyección es a mano sobre un <c>Canvas</c>, sin <c>Viewport3D</c>.</b> Es la misma
/// decisión que ya está razonada en <c>VistaModelo</c>: WPF 3D no tiene primitiva de línea, y
/// una jaula de armado es toda líneas. Y la cámara es la misma de ahí —giro alrededor del eje
/// vertical más inclinación—, no un isométrico fijo, porque hay que poder mirar la jaula desde
/// donde haga falta.
/// </para>
/// <para>
/// <b>Solo la sección se ve en 3D.</b> El alzado se queda plano: enseña el reparto de estribos
/// a lo largo de la pieza, y para eso una vista de lado se lee mejor que un isométrico donde
/// los estribos del fondo se confunden con los de delante.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Si la sección se ve en 3D en lugar del corte plano.</summary>
    private bool _alzado3D;

    /// <summary>Largo que se supone cuando la fila no lo trae, en metros.</summary>
    /// <remarks>
    /// Tres metros es un tramo de trabe corriente. Hace falta un valor porque sin largo no
    /// hay pieza que levantar, y dejarlo en cero enseñaba un recuadro vacío sin explicar por
    /// qué.
    /// </remarks>
    public const double LargoPorOmisionM = 3.0;

    // ======================================================================
    //  El giro
    // ======================================================================

    /// <summary>Giro alrededor del eje vertical de la pieza, en grados.</summary>
    private double _giro3DAzimut = GiroAzimutPorOmision;

    /// <summary>Inclinación de la vista, en grados.</summary>
    private double _giro3DElevacion = GiroElevacionPorOmision;

    /// <remarks>
    /// 35° y 22° son los mismos valores de arranque que usa el visor de ETABS, y por el
    /// mismo motivo: es la orientación en la que se ven las tres caras de un prisma sin que
    /// ninguna quede de canto.
    /// </remarks>
    private const double GiroAzimutPorOmision = 35;

    private const double GiroElevacionPorOmision = 22;

    /// <summary>Devuelve el 3D a su orientación de arranque.</summary>
    private void ReiniciarGiro3D()
    {
        _giro3DAzimut = GiroAzimutPorOmision;
        _giro3DElevacion = GiroElevacionPorOmision;
    }

    private void OnAlternarAlzado3D(object sender, RoutedEventArgs e)
    {
        _alzado3D = !_alzado3D;
        AlzadoVistaButton.Content = _alzado3D ? "3D" : "2D";

        AlzadoVistaButton.ToolTip = _alzado3D
            ? "Viendo la sección en 3D. Arrastra con el botón izquierdo para girarla.\n"
              + "Toca para volver al corte."
            : "Viendo el corte plano. Toca para ver la sección en 3D.";

        DibujarVistaPrevia();
    }

    // ======================================================================
    //  La cámara
    // ======================================================================

    /// <summary>
    /// La cámara del 3D: giro, inclinación, encuadre y escala, resueltos una sola vez.
    /// </summary>
    /// <remarks>
    /// Es la misma proyección que <c>VistaModelo.Camara</c>, y está escrita igual a
    /// propósito: <c>u</c> va a la derecha en pantalla, <c>v</c> hacia abajo —que es como
    /// crece la coordenada de un lienzo— y <c>d</c> es la distancia hacia el fondo.
    /// <para>
    /// Lo que se ve hacia ARRIBA es <c>z·cos(e) + d·sen(e)</c>: la altura aporta todo cuando
    /// se mira en horizontal y nada cuando se mira desde arriba, donde en cambio aporta todo
    /// la profundidad. Los DOS términos van con el mismo signo; sumarlos con signos opuestos
    /// deja la vista espejeada y pone lo lejano abajo.
    /// </para>
    /// </remarks>
    private readonly record struct Camara3D(
        double Sa, double Ca, double Se, double Ce,
        double K, double Cu, double Cv, double Ox, double Oy)
    {
        public (double U, double V) Proyectar(double x, double y, double z)
        {
            var d = (x * Sa) + (y * Ca);

            return ((x * Ca) - (y * Sa), -((z * Ce) + (d * Se)));
        }

        public Point APantalla(double x, double y, double z)
        {
            var (u, v) = Proyectar(x, y, z);

            return new Point(Ox + ((u - Cu) * K), Oy + ((v - Cv) * K));
        }

        /// <summary>Distancia al fondo. Cuanto mayor, más cerca del ojo.</summary>
        public double Prof(double x, double y) => (x * Sa) + (y * Ca);
    }

    /// <summary>
    /// Arma la cámara encajando la caja <c>bx · by · bz</c> en el área dada.
    /// </summary>
    /// <remarks>
    /// El encuadre se recalcula <b>en cada giro</b>, midiendo las ocho esquinas de la caja ya
    /// proyectadas. Es lo que hace que la pieza siga cabiendo al girarla: con una escala fija
    /// calculada para el isométrico de arranque, al ponerse de perfil se saldría del recuadro
    /// por arriba.
    /// </remarks>
    private static Camara3D? PrepararCamara3D(
        double bx, double by, double bz, double azimut, double elevacion, Rect area)
    {
        if (bx <= 0 || by <= 0 || bz <= 0 || area.Width < 30 || area.Height < 30)
        {
            return null;
        }

        var a = azimut * Math.PI / 180.0;
        var e = elevacion * Math.PI / 180.0;

        var basica = new Camara3D(
            Math.Sin(a), Math.Cos(a), Math.Sin(e), Math.Cos(e), 1, 0, 0, 0, 0);

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;

        foreach (var (x, y, z) in new[]
        {
            (0.0, 0.0, 0.0), (bx, 0.0, 0.0), (bx, by, 0.0), (0.0, by, 0.0),
            (0.0, 0.0, bz), (bx, 0.0, bz), (bx, by, bz), (0.0, by, bz)
        })
        {
            var (u, v) = basica.Proyectar(x, y, z);

            uMin = Math.Min(uMin, u);
            uMax = Math.Max(uMax, u);
            vMin = Math.Min(vMin, v);
            vMax = Math.Max(vMax, v);
        }

        if (uMax - uMin < 1e-9 || vMax - vMin < 1e-9)
        {
            return null;
        }

        var k = Math.Min(area.Width / (uMax - uMin), area.Height / (vMax - vMin));

        if (k <= 0 || double.IsInfinity(k))
        {
            return null;
        }

        return new Camara3D(
            basica.Sa, basica.Ca, basica.Se, basica.Ce, k,
            (uMin + uMax) / 2, (vMin + vMax) / 2,
            area.X + (area.Width / 2), area.Y + (area.Height / 2));
    }

    // ======================================================================
    //  El dibujo
    // ======================================================================

    /// <summary>Hasta dónde puede llegar el 3D por la derecha, en píxeles del lienzo.</summary>
    /// <remarks>
    /// El alzado empieza ahí, y el 3D no debe montarse encima. Se guarda al dibujar y lo usa
    /// <c>LimitarEncuadre3D</c> para topar el desplazamiento.
    /// </remarks>
    private double _limite3DDerecha;

    /// <summary>Borde derecho de lo dibujado en 3D, en coordenadas del lienzo.</summary>
    private double _borde3DDerecha;

    /// <summary>
    /// La sección en 3D: <b>el elemento levantado</b>, con sus estribos a la separación real.
    /// </summary>
    /// <remarks>
    /// No es una rebanada: es la pieza levantada a su longitud, con los estribos repartidos
    /// como dice la tabla. Lo que se ve en el corte es una sección; puesta de pie y con sus
    /// estribos, se ve el elemento.
    /// </remarks>
    private void DibujarSeccion3DPrevia(SeccionConcretoRow s, double ancho, double alto)
    {
        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            return;
        }

        var largoM = s.LongitudM > 0 ? s.LongitudM : LargoPorOmisionM;

        // La base en X, el peralte en Y y la LONGITUD en Z, que es la que sube.
        var bx = s.BaseCm;
        var by = s.AlturaCm;
        var bz = largoM * 100.0;

        // El 3D se queda en su mitad: el alzado ocupa la otra y no deben montarse.
        _limite3DDerecha = ancho * 0.5;

        var area = new Rect(26, 44, _limite3DDerecha - 52, alto - 78);

        var cam = PrepararCamara3D(bx, by, bz, _giro3DAzimut, _giro3DElevacion, area);

        if (cam is null)
        {
            return;
        }

        var c = cam.Value;

        // ---------- La sombra en el suelo ----------
        // Va PRIMERO, para quedar debajo de todo lo demás.
        SombraEnElSuelo(c, bx, by);

        // ---------- La caja de concreto, en alambre y tenue ----------
        // Lo que hay que mirar es el armado; una caja opaca lo taparía entero. Es lo mismo
        // que hace el visor de ETABS con el modelo.
        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));

        var v = new[]
        {
            c.APantalla(0, 0, 0), c.APantalla(bx, 0, 0),
            c.APantalla(bx, by, 0), c.APantalla(0, by, 0),
            c.APantalla(0, 0, bz), c.APantalla(bx, 0, bz),
            c.APantalla(bx, by, bz), c.APantalla(0, by, bz)
        };

        foreach (var (i, j) in new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        })
        {
            PreviewCanvas.Children.Add(new Line
            {
                X1 = v[i].X, Y1 = v[i].Y, X2 = v[j].X, Y2 = v[j].Y,
                Stroke = azul, StrokeThickness = 1.0, Opacity = 0.4
            });
        }

        // ---------- Todo el armado se apunta y se pinta de atrás hacia delante ----------
        //
        // Sin esto el orden de encima/debajo lo decide el orden del código: todas las
        // varillas taparían a todos los estribos, también las de detrás, y en el cruce de un
        // estribo con el diamante no se sabría cuál pasa por delante. Es el algoritmo del
        // pintor, lo mismo que hace el visor de ETABS con las barras extruidas.
        var piezas = new List<(double Prof, Action Pintar)>();

        var minX = double.MaxValue;
        var maxX = double.MinValue;

        // El rango de profundidad de la pieza, para poder apagar lo que queda al fondo. Se
        // mide en las cuatro esquinas de la planta: la profundidad no depende de la cota, así
        // que con la planta basta.
        var profs = new[] { c.Prof(0, 0), c.Prof(bx, 0), c.Prof(bx, by), c.Prof(0, by) };

        var dMin = profs.Min();
        var dMax = profs.Max();
        var dRango = dMax - dMin;

        void Barra(
            double x1, double y1, double z1,
            double x2, double y2, double z2,
            Color color, double diamCm)
        {
            var p = c.APantalla(x1, y1, z1);
            var q = c.APantalla(x2, y2, z2);

            minX = Math.Min(minX, Math.Min(p.X, q.X));
            maxX = Math.Max(maxX, Math.Max(p.X, q.X));

            // EL GRUESO ES EL DIÁMETRO a la escala del dibujo. El tope de 0.7 px es solo
            // para que una barra no se vuelva invisible, y es el MISMO para todas: con un
            // tope por familia, a escalas normales mandaba el tope y no el diámetro, así
            // que todo salía casi del mismo ancho.
            var grueso = Math.Max(diamCm * c.K, 0.7);

            var prof = (c.Prof(x1, y1) + c.Prof(x2, y2)) / 2;

            var luz = dRango > 1e-9 ? (prof - dMin) / dRango : 1;

            piezas.Add((prof, () => BarraRedonda3D(PreviewCanvas, p, q, color, grueso, luz)));
        }

        // Pinta un recorrido del plano de la sección a una cota z dada.
        void Recorrido(
            List<(double X, double Y)> puntos, double z, bool cerrado,
            Color color, double diamCm)
        {
            var hasta = cerrado ? puntos.Count : puntos.Count - 1;

            for (var i = 0; i < hasta; i++)
            {
                var a = puntos[i];
                var b = puntos[(i + 1) % puntos.Count];

                Barra(a.X, a.Y, z, b.X, b.Y, z, color, diamCm);
            }
        }

        var colorEstribo = Color.FromRgb(0x1F, 0x6F, 0xB2);
        var colorVarilla = Color.FromRgb(0xC0, 0x39, 0x2B);

        var rec = s.RecubrimientoCm;

        Varilla.TryDiametroCm(s.Estribo, out var de);

        var varillas = TodasLasVarillas(s, de, rec);

        // ---------- Las varillas longitudinales ----------
        foreach (var (_, vx, vy, vr) in varillas)
        {
            Barra(vx, vy, 0, vx, vy, bz, colorVarilla, vr * 2);
        }

        // ---------- El recorrido del estribo, con sus dobleces y su gancho ----------
        //
        // Se calcula UNA vez: es el mismo en todas las posiciones, y armarlo pasa por los
        // arcos de los cuatro dobleces y las dos colas.
        var trazo = TrazoDelEstribo3D(s, de, rec);

        var dDia = DiametroDelDiamante(s, de);
        var hayDiamante = s.LlevaDiamante && dDia > 0;

        var recorridoDia = hayDiamante
            ? RecorridoDelDiamante3D(s, de, rec, dDia)
            : null;

        var sep = Separaciones(s.SeparacionCm);

        var centros = Estribos.CentrosDeAlzado(
            largoM,
            sep[0] / 100, sep[1] / 100, sep[2] / 100,
            vertical: true,
            esColumna: true);

        foreach (var pos in centros)
        {
            var zEst = pos * 100.0;

            // El estribo, con su cuerpo y sus dos colas de gancho.
            if (trazo is not null)
            {
                Recorrido(trazo.Value.Cuerpo, zEst, trazo.Value.Cerrado, colorEstribo, de);

                foreach (var cola in trazo.Value.Colas)
                {
                    Recorrido(cola, zEst, false, colorEstribo, de);
                }
            }

            // El diamante, apilado sobre el estribo y tangente a él: dos barras del mismo
            // calibre en el mismo plano se atravesarían, que en la pieza no puede pasar.
            var zDia = zEst + ((de + dDia) / 2);

            if (recorridoDia is not null)
            {
                Recorrido(recorridoDia, zDia, true, colorEstribo, dDia);
            }

            // Y las grapas encima, cada una con SU diámetro y apilada sobre la anterior.
            var zGrapa = hayDiamante ? zDia + (dDia / 2) : zEst + (de / 2);

            foreach (var g in s.Grapas)
            {
                if (!Varilla.TryDiametroCm(g.Diametro, out var dGrapa) || dGrapa <= 0)
                {
                    // Sin diámetro reconocido se usa el del estribo, la misma regla que
                    // sigue el dibujo del corte.
                    dGrapa = de;
                }

                var va = BuscarVarillaPrevia(varillas, g.A);
                var vb = BuscarVarillaPrevia(varillas, g.B);

                if (va is null || vb is null)
                {
                    continue;
                }

                zGrapa += dGrapa / 2;

                // EL EJE DE LA GRAPA, CON SUS DOS DOBLECES Y SUS DOS COLAS.
                //
                // Antes era una raya recta de centro a centro: ni envolvía las varillas ni
                // tenía ganchos. Sale de TrazoGrapa.Eje, que resuelve la tangencia con la
                // MISMA función que el contorno del plano, así que la grapa del 3D y la de
                // AutoCAD son la misma pieza.
                //
                // El largo de la cola es la misma regla del corte: el gancho capturado, y
                // si no hay, seis diámetros, que es el mínimo de norma.
                var eje = TrazoGrapa.Eje(
                    va.Value.X, va.Value.Y, va.Value.R,
                    vb.Value.X, vb.Value.Y, vb.Value.R,
                    dGrapa,
                    s.GanchoCm > 0 ? s.GanchoCm : dGrapa * 6);

                if (eje is not null)
                {
                    Recorrido(eje, zGrapa, false, colorEstribo, dGrapa);
                }
                else
                {
                    // Sin tangente común -dos varillas demasiado juntas- no hay grapa que
                    // envuelva nada, pero el usuario la puso: se dibuja recta para que se
                    // vea que está, igual que hace el corte.
                    Barra(va.Value.X, va.Value.Y, zGrapa,
                          vb.Value.X, vb.Value.Y, zGrapa, colorEstribo, dGrapa);
                }

                zGrapa += dGrapa / 2;
            }
        }

        foreach (var (_, pintar) in piezas.OrderBy(p => p.Prof))
        {
            pintar();
        }

        _borde3DDerecha = maxX > minX ? maxX : _limite3DDerecha;

        Etiqueta(PreviaFijaCanvas,
            $"SECCIÓN 3D   ·   L = {largoM:N2} m   ·   {centros.Count} estribos"
            + $"   ·   giro {_giro3DAzimut:N0}°/{_giro3DElevacion:N0}°"
            + (s.LongitudM > 0 ? string.Empty : "   ·   largo por omisión"),
            26, alto - 18);
    }

    /// <summary>
    /// La <b>sombra</b> de la pieza apoyada en el suelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es una <b>sombra de contacto</b>: la huella de la pieza, corrida un poco al lado
    /// opuesto a la luz y con tres capas cada vez más grandes y más tenues para que el borde
    /// se vea difuso. Sirve para lo único que una sombra tiene que hacer aquí: que la pieza
    /// se apoye en algo en lugar de flotar.
    /// </para>
    /// <para>
    /// <b>No es la sombra proyectada de la pieza entera, y es a propósito.</b> La de verdad
    /// se calcula llevando cada punto al suelo por la dirección de la luz, y para un elemento
    /// de tres metros eso da una mancha de dos metros de largo: encuadrar la pieza y su
    /// sombra dejaría la pieza del tamaño de un dedo, y lo que hay que mirar es el armado. La
    /// huella corrida da la misma sensación de apoyo y no se come el recuadro.
    /// </para>
    /// <para>
    /// El corrimiento se mide con el <b>tamaño de la sección</b> y no con la altura, que es
    /// justo lo que evita que crezca con la longitud de la pieza.
    /// </para>
    /// </remarks>
    private void SombraEnElSuelo(Camara3D c, double bx, double by)
    {
        var corrimiento = 0.20 * Math.Min(bx, by);

        if (corrimiento <= 0)
        {
            return;
        }

        // Tres capas: la de dentro más oscura y pequeña, las de fuera más grandes y tenues.
        foreach (var (crece, alfa) in new[] { (0.0, (byte)0x2E), (0.7, (byte)0x1C), (1.6, (byte)0x0E) })
        {
            var d = corrimiento * (1 + crece);

            var brocha = new SolidColorBrush(Color.FromArgb(alfa, 0x1B, 0x2A, 0x3A));
            brocha.Freeze();

            var poly = new Polygon { Fill = brocha };

            // La huella, corrida al lado contrario de la luz y crecida un poco.
            foreach (var (x, y) in new[]
            {
                (-d + corrimiento, -d + corrimiento),
                (bx + d + corrimiento, -d + corrimiento),
                (bx + d + corrimiento, by + d + corrimiento),
                (-d + corrimiento, by + d + corrimiento)
            })
            {
                poly.Points.Add(c.APantalla(x, y, 0));
            }

            PreviewCanvas.Children.Add(poly);
        }
    }

    /// <summary>
    /// El recorrido del <b>estribo</b> de la fila, listo para el 3D.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos reglas de geometría salen del dibujo del corte, no de aquí. En
    /// <c>EstriboExterior</c> la cara de fuera va a <c>rec</c> del paño con radio
    /// <c>dEst + dVar/2</c>, y la de dentro a <c>rec + dEst</c> con radio <c>dVar/2</c>, las
    /// dos con el mismo centro. De ahí que el <b>eje</b> vaya a <c>rec + dEst/2</c> con radio
    /// <c>(dEst + dVar)/2</c>: es la consecuencia de que el doblez envuelva la varilla de la
    /// esquina.
    /// </para>
    /// <para>
    /// Los radios de arriba y de abajo salen distintos cuando los lechos llevan calibres
    /// distintos, que es lo normal en una trabe.
    /// </para>
    /// </remarks>
    private static TrazoEstribo.Trazo? TrazoDelEstribo3D(
        SeccionConcretoRow s, double de, double rec)
    {
        if (de <= 0)
        {
            return null;
        }

        Varilla.TryDiametroCm(s.DiamEsqSup, out var dSup);
        Varilla.TryDiametroCm(s.DiamEsqInfEfectivo, out var dInf);

        // Sin calibre reconocido en un lecho se usa el del otro, y si tampoco, el del
        // estribo: el radio del doblez tiene que salir de algo.
        if (dSup <= 0) { dSup = dInf > 0 ? dInf : de; }
        if (dInf <= 0) { dInf = dSup; }

        var medio = de / 2;

        return TrazoEstribo.Eje(
            rec + medio, rec + medio,
            s.BaseCm - rec - medio, s.AlturaCm - rec - medio,
            (de + dSup) / 2, (de + dInf) / 2,
            s.GanchoCm);
    }
}


public partial class MainWindow
{
    /// <summary>
    /// De dónde viene la luz, en coordenadas de <b>pantalla</b>.
    /// </summary>
    /// <remarks>
    /// Arriba a la izquierda, que es de donde se supone que viene la luz en cualquier dibujo
    /// técnico. Va en pantalla y no en el modelo a propósito: así el brillo se queda del
    /// mismo lado al girar la pieza, que es lo que hace que las barras se lean como un solo
    /// grupo iluminado y no como piezas sueltas cada una con su brillo. Recuérdese que en un
    /// lienzo la Y crece hacia ABAJO, de ahí el signo.
    /// </remarks>
    private const double LuzX = -0.5547;

    private const double LuzY = -0.8320;

    private static Color Mezcla(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255),
        (byte)Math.Clamp(c.G * f, 0, 255),
        (byte)Math.Clamp(c.B * f, 0, 255));

    /// <summary>
    /// Una <b>barra redonda</b> en el 3D: cilíndrica, no una raya plana.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El volumen se consigue con dos cosas: las puntas <b>redondeadas</b>, que cierran el
    /// cilindro en lugar de cortarlo a escuadra, y un <b>degradado a lo ancho</b> —oscuro en
    /// el borde de sombra, con una banda de brillo hacia el de la luz— que es como se lee un
    /// tubo. En un lienzo no hay iluminación, así que el relieve se pinta.
    /// </para>
    /// <para>
    /// <b>El degradado va en coordenadas ABSOLUTAS, y ahí estaba el defecto.</b> Antes iba
    /// en coordenadas relativas al recuadro de la barra, y eso solo sale bien cuando el
    /// recuadro es cuadrado: en una barra tumbada el recuadro es mucho más ancho que alto,
    /// así que el eje del degradado se estiraba con él y dejaba de ser perpendicular a la
    /// barra. Resultado: las varillas, que van casi verticales, se veían redondas, y los
    /// estribos y el diamante, que en isométrico van en diagonal, salían planos. Era
    /// exactamente lo que se reportó. Con coordenadas absolutas el eje es perpendicular de
    /// verdad, y una barra se ve igual de redonda en cualquier dirección.
    /// </para>
    /// <para>
    /// El precio es que la brocha depende de <b>dónde</b> está la barra, así que ya no se
    /// puede guardar en caché por dirección. Se cambió a propósito: la caché era rápida
    /// porque suponía justo lo que estaba mal.
    /// </para>
    /// </remarks>
    /// <param name="luz">
    /// Cuánta luz le toca por su <b>profundidad</b>, de 0 al fondo a 1 al frente. Lo que
    /// está lejos se ve más apagado, y es lo que separa el acero del fondo del de delante
    /// cuando la jaula tiene treinta estribos superpuestos.
    /// </param>
    private void BarraRedonda3D(
        Canvas lienzo, Point p, Point q, Color color, double grueso, double luz)
    {
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 0.4 || grueso <= 0)
        {
            return;
        }

        // La normal a la barra, orientada hacia la luz: así el brillo cae siempre del
        // mismo lado, gire la pieza como gire.
        var nx = -dy / largo;
        var ny = dx / largo;

        if ((nx * LuzX) + (ny * LuzY) < 0)
        {
            nx = -nx;
            ny = -ny;
        }

        // El eje del degradado: perpendicular a la barra, centrado en ella y del ancho
        // exacto del grueso. De la sombra (0) al lado de la luz (1).
        var mx = (p.X + q.X) / 2;
        var my = (p.Y + q.Y) / 2;

        var mitad = grueso / 2;

        // La profundidad apaga la barra entera, sin tocar el relieve: se multiplica el
        // color base y el degradado se calcula sobre ese.
        var f = 0.62 + (0.38 * Math.Clamp(luz, 0, 1));

        var baseColor = Mezcla(color, f);

        var brocha = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(mx - (nx * mitad), my - (ny * mitad)),
            EndPoint = new Point(mx + (nx * mitad), my + (ny * mitad)),
            GradientStops =
            {
                new GradientStop(Mezcla(baseColor, 0.42), 0.00),
                new GradientStop(Mezcla(baseColor, 0.80), 0.30),
                new GradientStop(Mezcla(baseColor, 1.32), 0.62),
                new GradientStop(Mezcla(baseColor, 1.08), 0.85),
                new GradientStop(Mezcla(baseColor, 0.74), 1.00)
            }
        };

        brocha.Freeze();

        lienzo.Children.Add(new Line
        {
            X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
            Stroke = brocha,
            StrokeThickness = grueso,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    /// <summary>Busca una varilla por su señal en la tabla de la vista previa.</summary>
    /// <remarks>
    /// Igual que <c>BuscarVarilla</c>, pero devolviendo solo X e Y, que es lo que hace falta
    /// para colocar una grapa en el 3D. Devuelve <c>null</c> si la señal ya no apunta a nada,
    /// y entonces esa grapa se salta: es lo mismo que hace el dibujo del corte cuando el
    /// lecho se quedó con menos varillas.
    /// </remarks>
    private static (double X, double Y, double R)? BuscarVarillaPrevia(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas, RefVarilla señal)
    {
        foreach (var v in varillas)
        {
            if (v.Ref.Equals(señal))
            {
                // El RADIO también: el eje de la grapa envuelve la varilla, así que sin su
                // radio no se puede saber por dónde pasa el doblez.
                return (v.X, v.Y, v.R);
            }
        }

        return null;
    }

    /// <summary>El diámetro del estribo <b>diamante</b>, en centímetros.</summary>
    /// <remarks>
    /// Sin diámetro propio capturado se usa el del estribo principal, que es exactamente la
    /// regla que sigue el dibujante de AutoCAD en <c>EstriboDiamante</c>.
    /// </remarks>
    private static double DiametroDelDiamante(SeccionConcretoRow s, double de) =>
        Varilla.TryDiametroCm(
            string.IsNullOrWhiteSpace(s.DiamEstriboDiamante)
                ? s.Estribo
                : s.DiamEstriboDiamante,
            out var dDia) && dDia > 0
            ? dDia
            : de;

    /// <summary>
    /// El <b>eje</b> del estribo diamante en el plano de la sección, muestreado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sale de <see cref="TrazoDiamante"/>, la misma clase que usa el corte y el dibujante de
    /// AutoCAD. Si se calculara aquí, el diamante del 3D podría abrazar otras varillas que el
    /// de la sección.
    /// </para>
    /// <para>
    /// <b>Se pide la cinta a <c>dDia/2</c> y no a 0.</b> A cero, <c>Cinta</c> devuelve la cara
    /// de DENTRO del diamante, que es lo que el corte necesita para trazar sus dos caras. Aquí
    /// se dibuja una barra con grueso, así que hace falta el EJE, que va medio diámetro por
    /// fuera de esa cara. Con la cara de dentro, el diamante salía corrido medio diámetro
    /// respecto a las varillas que abraza.
    /// </para>
    /// </remarks>
    /// <returns>El recorrido cerrado, o <c>null</c> si no se pudo armar.</returns>
    private List<(double X, double Y)>? RecorridoDelDiamante3D(
        SeccionConcretoRow s, double de, double rec, double dDia)
    {
        if (dDia <= 0)
        {
            return null;
        }

        var x1 = rec;
        var y1 = rec;
        var x2 = s.BaseCm - rec;
        var y2 = s.AlturaCm - rec;

        if (x2 <= x1 || y2 <= y1)
        {
            return null;
        }

        var varSup = PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                       arriba: true, intermedio: false);

        var varInf = PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                       arriba: false, intermedio: false);

        var varLat = PosicionesLaterales(s, de, rec);

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dDia, varSup, varInf, varLat);

        if (centros is null)
        {
            return null;
        }

        var geo = TrazoDiamante.Cinta(centros, dDia / 2);

        if (geo is null)
        {
            return null;
        }

        var puntos = TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, 8);

        return puntos.Count < 3 ? null : puntos;
    }
}
