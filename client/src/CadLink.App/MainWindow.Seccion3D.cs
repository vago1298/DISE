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

    /// <summary>
    /// Cuánto se ha girado <b>la pieza</b> sobre su eje vertical, en grados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gira la PIEZA, no la cámara.</b> Es la diferencia entre un plato giratorio y una
    /// cámara dando vueltas, y aquí importa: el <b>sol y el suelo están quietos</b>, así que
    /// si girara la cámara el suelo entero giraría con ella y la sombra se pasearía por la
    /// pantalla. Girando la pieza, la sombra se queda donde está —solo cambia de forma, como
    /// la de un objeto que uno hace girar al sol— y lo único que se mueve es la sección.
    /// </para>
    /// <para>
    /// Para la sección el resultado se ve igual que girar la cámara: girar el objeto un
    /// ángulo o el ojo el contrario son la misma imagen. La diferencia está solo en lo que
    /// está clavado al mundo, que es justo la sombra.
    /// </para>
    /// </remarks>
    private double _giro3DAzimut = GiroAzimutPorOmision;

    /// <summary>Inclinación de la vista, en grados. Esta sí es de la cámara.</summary>
    /// <remarks>
    /// La inclinación no se puede pasar a la pieza: inclinar la pieza la volcaría, y lo que
    /// se quiere es mirarla desde más arriba o más abajo. Al inclinar, el suelo se inclina
    /// también y la sombra acompaña, que es lo que pasa de verdad cuando uno se agacha.
    /// </remarks>
    private double _giro3DElevacion = GiroElevacionPorOmision;

    /// <summary>La pieza arranca sin girar; el escorzo lo da la cámara.</summary>
    private const double GiroAzimutPorOmision = 0;

    /// <remarks>
    /// 22° es el valor de arranque del visor de ETABS, y por el mismo motivo: es la
    /// inclinación en la que se ven las tres caras de un prisma sin que ninguna quede de
    /// canto.
    /// </remarks>
    private const double GiroElevacionPorOmision = 22;

    /// <summary>
    /// Desde qué lado mira la cámara. <b>Fijo</b>, porque lo que gira es la pieza.
    /// </summary>
    /// <remarks>
    /// 32° pone el suelo en escorzo —ni de frente ni de canto— así que la sombra se lee como
    /// apoyada en un piso y no como una mancha pegada a la pieza.
    /// </remarks>
    private const double AzimutDeLaCamara = 32;

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

        /// <summary>Distancia hacia el fondo en planta. Cuanto MAYOR, más lejos del ojo.</summary>
        public double Prof(double x, double y) => (x * Sa) + (y * Ca);

        /// <summary>
        /// Lo <b>cerca del ojo</b> que queda un punto. Cuanto mayor, más cerca.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Es la proyección del punto sobre la dirección que sale de la pantalla. Con esta
        /// cámara —giro <c>a</c> y luego inclinación <c>e</c>— esa dirección es
        /// <c>(−sen a·cos e, −cos a·cos e, sen e)</c>, así que la cuenta sale
        /// <c>z·sen e − cos e·Prof</c>.
        /// </para>
        /// <para>
        /// <b>Los dos términos hacen falta, y el de la altura suele MANDAR.</b> Aquí se mira
        /// una pieza levantada tres metros con una sección de medio, así que mirándola desde
        /// 22° el término <c>z·sen e</c> barre más de un metro y el de la planta apenas medio.
        /// Ordenar solo por <see cref="Prof"/> —que es lo que hace el visor de ETABS, donde
        /// los elementos están repartidos en planta y no apilados— deja el orden decidido por
        /// lo que menos pesa, y el armado se pinta entremezclado.
        /// </para>
        /// <para>
        /// Y el <b>signo</b> importa: <see cref="Prof"/> crece hacia el fondo, así que entra
        /// RESTANDO. Ordenar de menor a mayor por <c>Prof</c> pinta lo cercano primero y lo
        /// lejano encima, que es justo lo contrario de lo que se quiere.
        /// </para>
        /// </remarks>
        public double Cercania(double x, double y, double z) =>
            (z * Se) - (Ce * Prof(x, y));
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
        IReadOnlyList<(double X, double Y, double Z)> encuadra,
        double azimut, double elevacion, Rect area)
    {
        if (encuadra.Count < 2 || area.Width < 30 || area.Height < 30)
        {
            return null;
        }

        var a = azimut * Math.PI / 180.0;
        var e = elevacion * Math.PI / 180.0;

        var basica = new Camara3D(
            Math.Sin(a), Math.Cos(a), Math.Sin(e), Math.Cos(e), 1, 0, 0, 0, 0);

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;

        foreach (var (x, y, z) in encuadra)
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

        // ===== EL GIRO ES DE LA PIEZA =====
        //
        // Se rota alrededor del eje vertical que pasa por el centro de la sección. El sol, el
        // suelo y la cámara se quedan quietos, así que la sombra no se pasea: solo cambia de
        // forma, y lo que se ve moverse es la sección.
        var ejeX = bx / 2;
        var ejeY = by / 2;

        var gr = _giro3DAzimut * Math.PI / 180.0;
        var cosG = Math.Cos(gr);
        var senG = Math.Sin(gr);

        (double X, double Y) Gira(double x, double y)
        {
            var dx = x - ejeX;
            var dy = y - ejeY;

            return (ejeX + (dx * cosG) - (dy * senG), ejeY + (dx * senG) + (dy * cosG));
        }

        // ===== EL ENCUADRE NO DEBE CAMBIAR AL GIRAR =====
        //
        // Si se midiera la pieza tal como queda girada, su silueta cambia con cada grado y el
        // encuadre se recalcularía: la pieza daría saltos de tamaño y de sitio mientras se
        // gira. Se mide el CILINDRO que la envuelve —de radio media diagonal de la sección—,
        // que es lo mismo en cualquier giro. Con eso la pieza se queda quieta girando en su
        // sitio.
        //
        // Y entra la sombra de ese cilindro, no la de la pieza, por lo mismo: así el encuadre
        // deja sitio para la sombra sin depender del giro.
        var radio = Math.Sqrt((bx * bx) + (by * by)) / 2;

        var encuadra = new List<(double X, double Y, double Z)>();

        foreach (var (dx, dy) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            var x = ejeX + (dx * radio);
            var y = ejeY + (dy * radio);

            encuadra.Add((x, y, 0));
            encuadra.Add((x, y, bz));

            // Donde cae su sombra: corrida lo que dice el sol.
            encuadra.Add((x + (SolX / SolZ * bz), y + (SolY / SolZ * bz), 0));
        }

        var cam = PrepararCamara3D(encuadra, AzimutDeLaCamara, _giro3DElevacion, area);

        if (cam is null)
        {
            return;
        }

        var c = cam.Value;

        // Lo grande que se ve de verdad: la escala de la cámara POR el zoom del lienzo. De
        // aquí sale cuántos tramos lleva cada doblez y en cuántos trozos se parte cada barra,
        // así que al acercarse las curvas se afinan solas en lugar de verse facetadas.
        var kPantalla = c.K * PreviaEscala.ScaleX;

        // En qué trozos se parte cada barra para decidir quién tapa a quién.
        //
        // ANTES SE DIBUJABA MÁS BASTO MIENTRAS SE GIRABA —trozos de 22 px— para que siguiera
        // al ratón. Se quitó: los dobleces salían facetados justo cuando se está mirando cómo
        // gira la pieza, que es cuando más se nota. Vale más que el giro cueste un poco que
        // ver la pieza deshecha mientras se mueve.
        const double trozoPx = 9.0;

        // ---------- La sombra en el suelo ----------
        // Va PRIMERO, para quedar debajo de todo lo demás. Su silueta es la envolvente de la
        // base GIRADA y de la tapa girada y corrida: con la pieza girada la base ya no está
        // alineada con los ejes, así que no se puede escribir a mano.
        var puntosDeSombra = new List<(double X, double Y)>();

        foreach (var (x, y) in new[] { (0.0, 0.0), (bx, 0.0), (bx, by), (0.0, by) })
        {
            var (gx, gy) = Gira(x, y);

            puntosDeSombra.Add((gx, gy));
            puntosDeSombra.Add((gx + (SolX / SolZ * bz), gy + (SolY / SolZ * bz)));
        }

        SombraEnElSuelo(c, Envolvente.Convexa(puntosDeSombra));

        // ---------- La caja de concreto, en alambre y tenue ----------
        // Lo que hay que mirar es el armado; una caja opaca lo taparía entero. Es lo mismo
        // que hace el visor de ETABS con el modelo.
        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));

        // Las esquinas, ya giradas: la caja acompaña a la pieza.
        Point Esquina(double x, double y, double z)
        {
            var (gx, gy) = Gira(x, y);

            return c.APantalla(gx, gy, z);
        }

        var v = new[]
        {
            Esquina(0, 0, 0), Esquina(bx, 0, 0),
            Esquina(bx, by, 0), Esquina(0, by, 0),
            Esquina(0, 0, bz), Esquina(bx, 0, bz),
            Esquina(bx, by, bz), Esquina(0, by, bz)
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
        // Cada trozo con su CERCANÍA al ojo. Se pinta de menor a mayor, o sea de lo más
        // lejano a lo más cercano, para que lo de delante tape lo de atrás.
        var piezas = new List<(double Cerca, Action Pintar)>();

        var minX = double.MaxValue;
        var maxX = double.MinValue;

        // El rango de cercanía de la pieza, para poder apagar lo que queda al fondo. Se mide
        // en las OCHO esquinas y no en las cuatro de la planta: la cercanía sí depende de la
        // cota, y en una pieza de tres metros ese término es el que más pesa.
        // Se mide sobre el CILINDRO que envuelve la pieza, igual que el encuadre y por lo
        // mismo: así el rango no cambia al girar y una barra no cambia de brillo sola
        // mientras se gira. Con el rango medido sobre la pieza girada, la misma barra saldría
        // más clara o más oscura según el ángulo.
        var cercanias = new List<double>();

        foreach (var (dx, dy) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            var x = ejeX + (dx * radio);
            var y = ejeY + (dy * radio);

            cercanias.Add(c.Cercania(x, y, 0));
            cercanias.Add(c.Cercania(x, y, bz));
        }

        var dMin = cercanias.Min();
        var dMax = cercanias.Max();
        var dRango = dMax - dMin;

        void Barra(
            double sx1, double sy1, double z1,
            double sx2, double sy2, double z2,
            Color color, double diamCm)
        {
            // Las coordenadas llegan en el plano de la SECCIÓN y aquí se pasan al mundo
            // aplicándoles el giro de la pieza. Se hace en un solo sitio a propósito: si cada
            // familia de barras lo aplicara por su cuenta, bastaría con olvidarlo en una para
            // que esa se quedara sin girar.
            var (x1, y1) = Gira(sx1, sy1);
            var (x2, y2) = Gira(sx2, sy2);

            var p = c.APantalla(x1, y1, z1);
            var q = c.APantalla(x2, y2, z2);

            minX = Math.Min(minX, Math.Min(p.X, q.X));
            maxX = Math.Max(maxX, Math.Max(p.X, q.X));

            // EL GRUESO ES EL DIÁMETRO a la escala del dibujo. El tope de 0.7 px es solo
            // para que una barra no se vuelva invisible, y es el MISMO para todas: con un
            // tope por familia, a escalas normales mandaba el tope y no el diámetro, así
            // que todo salía casi del mismo ancho.
            var grueso = Math.Max(diamCm * c.K, 0.7);

            // ==========================================================================
            //  LA BARRA SE PARTE EN TROZOS, Y ES LO QUE EVITA QUE SE TRASPASEN
            // ==========================================================================
            //
            // El orden de pintado se decide por la profundidad de cada pieza. Con la barra
            // entera como una sola pieza, su profundidad es la del CENTRO, así que una
            // varilla y el costado de un estribo que se cruzan se pintaban una entera
            // delante de la otra: en el cruce, la de atrás asomaba por encima y parecía
            // atravesarla. Partidas en trozos, en el cruce manda la profundidad de ESE
            // trozo, que es la de verdad.
            //
            // El corte se hace en el espacio del modelo y no en pantalla porque lo que hay
            // que interpolar es la profundidad. La proyección es afín, así que el punto de
            // pantalla del trozo sale igual interpolando los extremos: no hace falta volver
            // a proyectar.
            var largoPx = Math.Sqrt(((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y)));

            var trozos = Math.Clamp((int)Math.Ceiling(largoPx / trozoPx), 1, 48);

            // ==========================================================================
            //  LA LUZ ES DE LA BARRA ENTERA, NO DE CADA TROZO
            // ==========================================================================
            //
            // Aquí estaba lo de «no se ve sólida». Cada trozo calculaba su propia luz con su
            // propia cercanía, así que una barra que se va al fondo salía en franjas: cada
            // trozo un poco más oscuro que el anterior, con el escalón justo en la junta. Y
            // como cada trozo remata en punta redonda, los dos casquetes de la junta se
            // superponían con colores distintos y se veía el bulto.
            //
            // Con una sola luz para toda la barra, todos sus trozos quedan IDÉNTICOS: el
            // degradado va perpendicular a la barra, así que es constante a lo largo de ella.
            // Las juntas desaparecen y la barra se lee de una pieza.
            //
            // El trozo sigue teniendo su propia cercanía, pero solo para ORDENAR. Que es para
            // lo que se partió la barra.
            var luzBarra = dRango > 1e-9
                ? (c.Cercania((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2) - dMin) / dRango
                : 1;

            for (var i = 0; i < trozos; i++)
            {
                var t0 = (double)i / trozos;
                var t1 = (double)(i + 1) / trozos;

                var a = new Point(p.X + ((q.X - p.X) * t0), p.Y + ((q.Y - p.Y) * t0));
                var b = new Point(p.X + ((q.X - p.X) * t1), p.Y + ((q.Y - p.Y) * t1));

                var tm = (t0 + t1) / 2;

                // La cercanía del CENTRO del trozo: decide el orden de pintado y nada más.
                var cerca = c.Cercania(
                    x1 + ((x2 - x1) * tm),
                    y1 + ((y2 - y1) * tm),
                    z1 + ((z2 - z1) * tm));

                piezas.Add((
                    cerca,
                    () => BarraRedonda3D(PreviewCanvas, a, b, color, grueso, luzBarra)));
            }
        }

        // Pinta un recorrido del plano de la sección, subiendo de z0 a z1 a lo largo de él.
        //
        // ===== POR QUÉ SUBE: UN ESTRIBO CERRADO ES UNA HÉLICE MUY PLANA =====
        //
        // Los dos extremos se juntan en la misma esquina y los dos envuelven la varilla, así
        // que en un plano exacto se ocuparían el mismo sitio. En la pieza uno lapa sobre el
        // otro, y ese lape es de un diámetro.
        //
        // La primera versión de esto puso el lape como un SALTO: la segunda cola arrancaba un
        // diámetro más arriba que el cuerpo. El resultado fue peor que el problema —la cola
        // quedaba separada del cuerpo, como un muñón flotando— y es lo que se reportó.
        //
        // Ahora el lape se reparte a lo largo del recorrido: el cuerpo sube un diámetro desde
        // donde arranca hasta donde acaba, y cada cola queda a la cota del extremo al que se
        // engancha. Todo empalma sin saltos, y es lo que hace la varilla de verdad.
        //
        // La subida se reparte por LARGO recorrido y no por número de puntos: los dobleces
        // llevan muchos puntos y los lados rectos pocos, así que por índice casi toda la
        // subida caería dentro de los dobleces y se vería el escalón ahí.
        void Recorrido(
            List<(double X, double Y)> puntos, double z0, double z1, bool cerrado,
            Color color, double diamCm)
        {
            var hasta = cerrado ? puntos.Count : puntos.Count - 1;

            if (hasta < 1)
            {
                return;
            }

            // El largo acumulado en cada punto, para repartir la subida.
            var acumulado = new double[puntos.Count + 1];

            for (var i = 0; i < hasta; i++)
            {
                var a = puntos[i];
                var b = puntos[(i + 1) % puntos.Count];

                acumulado[i + 1] = acumulado[i]
                    + Math.Sqrt(
                        ((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            }

            var total = acumulado[hasta];

            for (var i = 0; i < hasta; i++)
            {
                var a = puntos[i];
                var b = puntos[(i + 1) % puntos.Count];

                var f0 = total > 1e-9 ? acumulado[i] / total : 0;
                var f1 = total > 1e-9 ? acumulado[i + 1] / total : 0;

                Barra(
                    a.X, a.Y, z0 + ((z1 - z0) * f0),
                    b.X, b.Y, z0 + ((z1 - z0) * f1),
                    color, diamCm);
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
        var trazo = TrazoDelEstribo3D(s, de, rec, kPantalla);

        var dDia = DiametroDelDiamante(s, de);
        var hayDiamante = s.LlevaDiamante && dDia > 0;

        var recorridoDia = hayDiamante
            ? RecorridoDelDiamante3D(s, de, rec, dDia, kPantalla)
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
                // Sin gancho el estribo es un aro cerrado y no lapa: se queda plano. Con
                // gancho sube un diámetro de punta a punta, que es el lape de sus extremos.
                var lape = trazo.Value.Cerrado ? 0 : de;

                Recorrido(
                    trazo.Value.Cuerpo, zEst, zEst + lape,
                    trazo.Value.Cerrado, colorEstribo, de);

                // Cada cola va a la cota del extremo del cuerpo al que se engancha, así que
                // empalma sin salto.
                //
                // Colas[0] arranca en la tangencia del COSTADO, que es donde ACABA el
                // cuerpo; Colas[1] arranca en la tangencia de ARRIBA, que es donde EMPIEZA.
                // Ese reparto lo fija TrazoEstribo y no se puede invertir aquí sin que las
                // colas se despeguen.
                if (trazo.Value.Colas.Count > 0)
                {
                    Recorrido(trazo.Value.Colas[0], zEst + lape, zEst + lape,
                              false, colorEstribo, de);
                }

                if (trazo.Value.Colas.Count > 1)
                {
                    Recorrido(trazo.Value.Colas[1], zEst, zEst,
                              false, colorEstribo, de);
                }
            }

            // El diamante, apilado sobre el estribo y tangente a él: dos barras del mismo
            // calibre en el mismo plano se atravesarían, que en la pieza no puede pasar.
            var zDia = zEst + ((de + dDia) / 2);

            if (recorridoDia is not null)
            {
                // El diamante se dibuja cerrado, así que no lapa: se queda plano.
                Recorrido(recorridoDia, zDia, zDia, true, colorEstribo, dDia);
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
                    // La grapa es una pieza abierta con sus dos ganchos en los extremos
                    // opuestos, así que no se lapa consigo misma: va plana.
                    Recorrido(eje, zGrapa, zGrapa, false, colorEstribo, dGrapa);
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

        // De lo MÁS LEJANO a lo más cercano. Cercania crece hacia el ojo, así que de menor a
        // mayor: lo último que se pinta es lo que está delante y tapa a lo demás.
        foreach (var (_, pintar) in piezas.OrderBy(p => p.Cerca))
        {
            pintar();
        }

        _borde3DDerecha = maxX > minX ? maxX : _limite3DDerecha;

        Etiqueta(PreviaFijaCanvas,
            $"SECCIÓN 3D   ·   L = {largoM:N2} m   ·   {centros.Count} estribos"
            + $"   ·   giro {((_giro3DAzimut % 360) + 360) % 360:N0}°"
            + $"   ·   vista {_giro3DElevacion:N0}°"
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
    /// <summary>
    /// El <b>sol</b>, en coordenadas del modelo: de la pieza hacia el suelo.
    /// </summary>
    /// <remarks>
    /// Alto y algo de lado, como el sol a media mañana. De aquí sale el largo de la sombra:
    /// un punto a cota <c>z</c> cae en el suelo corrido <c>z · Sol/|SolZ|</c>, así que con
    /// estos números la sombra de una pieza de tres metros se extiende unos dos, que es lo
    /// que se ve en obra. Bajar más el sol la alargaría hasta comerse el recuadro.
    /// </remarks>
    private const double SolX = 0.30;

    private const double SolY = 0.42;

    private const double SolZ = 0.86;


    /// <summary>
    /// La <b>sombra proyectada</b> de la pieza en el suelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la sombra de verdad: cada esquina de la pieza se lleva al suelo siguiendo la
    /// dirección del sol, y la silueta es la <b>envolvente convexa</b> de todas ellas —las
    /// cuatro de la base, que se quedan donde están, y las cuatro de arriba, que caen lejos—.
    /// Sale tumbada a lo largo, no como una huella pegada a la base.
    /// </para>
    /// <para>
    /// Hizo falta la envolvente porque esa unión <b>no es un rectángulo</b>: son dos
    /// polígonos iguales desplazados en diagonal, y su silueta tiene más lados que cada uno.
    /// Dibujar los dos por separado se notaría, porque al ser translúcidos la zona común
    /// saldría del doble de oscura. La cuenta vive en <see cref="Envolvente"/>, fuera de aquí
    /// y con su prueba, porque esta parte del programa no se puede compilar en el entorno de
    /// trabajo.
    /// </para>
    /// <para>
    /// La silueta llega <b>ya girada</b>: gira la pieza y no la cámara, así que la sombra se
    /// queda donde está y solo cambia de forma, que es lo que hace un objeto al que se le da
    /// vueltas al sol.
    /// </para>
    /// </remarks>
    /// <param name="silueta">
    /// El contorno de la sombra en el suelo, ya resuelto. Con menos de tres vértices no hay
    /// nada que rellenar: pasa si la pieza es degenerada o si el sol cae a plomo.
    /// </param>
    private void SombraEnElSuelo(Camara3D c, List<(double X, double Y)> silueta)
    {
        if (silueta.Count < 3)
        {
            return;
        }

        // Dos capas: la sombra y un halo un poco mayor y más tenue, para que el borde no
        // salga como recortado con tijeras.
        foreach (var (alfa, crece) in new[] { ((byte)0x12, 1.06), ((byte)0x22, 1.0) })
        {
            var cx = silueta.Average(p => p.X);
            var cy = silueta.Average(p => p.Y);

            var brocha = new SolidColorBrush(Color.FromArgb(alfa, 0x16, 0x24, 0x33));
            brocha.Freeze();

            var poly = new Polygon { Fill = brocha };

            foreach (var (x, y) in silueta)
            {
                poly.Points.Add(c.APantalla(
                    cx + ((x - cx) * crece), cy + ((y - cy) * crece), 0));
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
        SeccionConcretoRow s, double de, double rec, double kPantalla)
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

        var rSup = (de + dSup) / 2;
        var rInf = (de + dInf) / 2;

        return TrazoEstribo.Eje(
            rec + medio, rec + medio,
            s.BaseCm - rec - medio, s.AlturaCm - rec - medio,
            rSup, rInf,
            s.GanchoCm,
            TramosDeDoblez(Math.Max(rSup, rInf), kPantalla));
    }

    /// <summary>
    /// Cuántos tramos rectos lleva un doblez para que <b>no se vea facetado</b>.
    /// </summary>
    /// <remarks>
    /// Sale del radio del doblez medido <b>en píxeles</b>, no de un número fijo: al 100 % un
    /// doblez de estribo ocupa dos o tres píxeles y con seis tramos ya se ve redondo, pero al
    /// 350 % —que es a lo que se revisa el armado— ocupa treinta y los seis tramos se ven como
    /// un hexágono. Con esto las curvas se afinan solas al acercarse.
    /// <para>
    /// La cuenta es la del arco: un cuadrante mide <c>r · π/2</c>, y se pide que cada tramo no
    /// pase de unos tres píxeles de cuerda. Entre 6 y 40 para no quedarse corto ni disparar el
    /// número de figuras.
    /// </para>
    /// </remarks>
    private static int TramosDeDoblez(double radioCm, double kPantalla)
    {
        var arcoPx = radioCm * kPantalla * Math.PI / 2;

        return Math.Clamp((int)Math.Ceiling(arcoPx / 3), 6, 40);
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
        SeccionConcretoRow s, double de, double rec, double dDia, double kPantalla)
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

        // Los dobleces del diamante se muestrean según lo grandes que se vean, igual que los
        // del estribo: con un número fijo, al acercarse se veían como polígonos.
        var puntos = TrazoDiamante.Muestrear(
            geo.Value.Pts, geo.Value.Bulges,
            TramosDeDoblez(dDia, kPantalla));

        return puntos.Count < 3 ? null : puntos;
    }
}
