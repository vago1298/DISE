using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.Cad.PlanoEstructural;
using CadLink.Etabs;

namespace CadLink.App;

/// <summary>
/// Dibuja el modelo de ETABS en 3D y en planta, sobre un <see cref="Canvas"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no se usa <c>Viewport3D</c>.</b> Un modelo estructural es alámbrico:
/// son miles de líneas. WPF 3D no tiene primitiva de línea, así que cada barra
/// habría que construirla como un cilindro o una cinta de triángulos, y aun así el
/// grosor cambiaría con la distancia a la cámara y las barras lejanas
/// desaparecerían. Proyectando a mano sobre un <c>Canvas</c> el grosor se controla
/// en píxeles, el resultado se parece al de ETABS, y no hay que pelearse con
/// cámaras ni luces.
/// </para>
/// <para>
/// Las unidades del modelo llegan en metros, porque la conexión fija kN·m·C.
/// </para>
/// </remarks>
public sealed partial class VistaModelo
{
    /// <summary>Margen en píxeles entre el dibujo y el borde del lienzo.</summary>
    private const double Margen = 26;

    /// <summary>
    /// Margen del lado <b>anotado</b>: arriba y a la izquierda, donde van cotas y burbujas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió que las cotas vayan <b>solo arriba y a la izquierda</b>, así que solo esos
    /// dos lados necesitan sitio, y necesitan bastante: contando desde el dibujo hacia
    /// afuera van la cota parcial, la cota total y por último la burbuja del eje. Los otros
    /// dos lados se quedan con un margen de cortesía.
    /// </para>
    /// <para>
    /// Por eso los márgenes son <b>asimétricos</b>: reservar 78 píxeles en los cuatro lados
    /// para usar dos sería regalar un tercio del lienzo.
    /// </para>
    /// </remarks>
    private const double MargenAnotado = 78;

    /// <summary>Margen de los lados sin anotaciones: abajo y a la derecha.</summary>
    private const double MargenLibre = 18;

    private static readonly Brush ColorColumna = Pincel(0x1F, 0x6F, 0xB2);
    private static readonly Brush ColorTrabe = Pincel(0x1D, 0x8A, 0x4E);
    private static readonly Brush ColorDiagonal = Pincel(0x8E, 0x44, 0xAD);
    private static readonly Brush ColorMuro = Pincel(0x6B, 0x7A, 0x89);
    private static readonly Brush ColorLosa = Pincel(0xB0, 0xBE, 0xC5);
    private static readonly Brush RellenoLosa = Pincel(0xB0, 0xBE, 0xC5, 0x38);

    // Los rellenos de las secciones en planta. Van translúcidos a propósito: así se ve por
    // debajo la losa y se nota dónde se cruzan dos piezas.
    private static readonly Brush RellenoColumna = Pincel(0x1F, 0x6F, 0xB2, 0x40);
    private static readonly Brush RellenoTrabe = Pincel(0x1D, 0x8A, 0x4E, 0x33);
    private static readonly Brush RellenoDiagonal = Pincel(0x8E, 0x44, 0xAD, 0x33);
    private static readonly Brush RellenoMuro = Pincel(0x6B, 0x7A, 0x89, 0x55);
    private static readonly Brush ColorEje = Pincel(0xB0, 0xB0, 0xB0);

    // La burbuja del eje va RELLENA, y opaca: si fuera hueca, la línea a trazos del propio
    // eje le pasaría por dentro y el nombre no se leería.
    private static readonly Brush RellenoBurbuja = Pincel(0xFF, 0xFF, 0xFF);

    private static readonly Brush ColorEjeTexto = Pincel(0x60, 0x6A, 0x74);

    private static readonly Brush ColorCota = Pincel(0x8A, 0x93, 0x9C);

    // LA TERNA, con los colores de siempre: X roja, Y verde, Z azul. Es lo que usan ETABS y
    // cualquier programa de 3D, así que no hay que explicarlo.
    private static readonly Brush ColorEjeX = Pincel(0xC0, 0x39, 0x2B);

    private static readonly Brush ColorEjeY = Pincel(0x27, 0x8A, 0x3E);

    private static readonly Brush ColorEjeZ = Pincel(0x2A, 0x62, 0xB8);

    // LA PLACA DE LA TERNA, TRASLUCIDA: se pidió así, y con razón. Opaca tapaba la esquina
    // del modelo, y en una vista que se gira la esquina es justo donde uno mira para
    // orientarse. Con 0x3C de opacidad —un 23%— se ve lo que hay debajo y los ejes se siguen
    // leyendo, porque los colores van a plena intensidad.
    private static readonly Brush FondoTerna = Pincel(0xFF, 0xFF, 0xFF, 0x3C);

    private static SolidColorBrush Pincel(byte r, byte g, byte b, byte a = 0xFF) =>
        new(Color.FromArgb(a, r, g, b));

    public ModeloEtabs? Modelo { get; set; }

    /// <summary>Giro alrededor del eje vertical, en grados.</summary>
    public double Azimut { get; set; } = 35;

    /// <summary>Inclinación de la vista, en grados. 90 sería planta pura.</summary>
    public double Elevacion { get; set; } = 22;

    public double Zoom { get; set; } = 1;

    public double PanX { get; set; }

    public double PanY { get; set; }

    public bool VerColumnas { get; set; } = true;

    public bool VerTrabes { get; set; } = true;

    public bool VerDiagonales { get; set; } = true;

    public bool VerMuros { get; set; } = true;

    public bool VerLosas { get; set; } = true;

    /// <summary>
    /// Si la planta enseña la <b>cuadrícula de ejes</b>, con sus burbujas.
    /// </summary>
    /// <remarks>
    /// Es un filtro aparte de los de tipo de elemento porque los ejes <b>no son elementos</b>:
    /// son la referencia del replanteo. Y se puede apagar porque en un modelo con muchos ejes
    /// la cuadrícula tapa lo que se quiere mirar.
    /// </remarks>
    public bool VerEjes { get; set; } = true;

    /// <summary>
    /// El <b>eje del corte</b>: su nombre, o vacío si no hay corte. Solo en las vistas de
    /// volumen.
    /// </summary>
    /// <remarks>
    /// Un corte por un eje es un <b>alzado</b>: se ve únicamente lo que hay sobre ese eje, y
    /// es la forma de entender un edificio que en isométrica es una maraña de muros. Se
    /// guarda el nombre —el que dice la burbuja— para poder decirlo en la leyenda.
    /// </remarks>
    public string CorteEje { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> si el corte es por un eje <b>vertical</b> —de los que van en X—.
    /// </summary>
    public bool CorteEnX { get; set; }

    /// <summary>La coordenada del eje del corte, en metros.</summary>
    public double CorteOrdenada { get; set; }

    /// <summary>
    /// Espesor de la <b>rebanada</b> del corte, en metros.
    /// </summary>
    /// <remarks>
    /// No es cero por una razón práctica: en un modelo real los muros de un mismo eje no
    /// están todos exactamente en su ordenada —el eje pasa por el paño y el muro se modela
    /// en su línea media, o un nudo quedó movido— así que un corte de espesor cero se
    /// quedaría vacío. Con 60 cm entra lo que de verdad está sobre el eje y no entra lo del
    /// eje de al lado.
    /// </remarks>
    public double CorteEspesorM { get; set; } = 0.6;

    /// <summary>Quita el corte y vuelve a verse el modelo completo.</summary>
    public void SinCorte()
    {
        CorteEje = string.Empty;
        CorteOrdenada = 0;
    }

    public void Reiniciar()
    {
        Azimut = 35;
        Elevacion = 22;
        Zoom = 1;
        PanX = 0;
        PanY = 0;
    }

    private bool Visible(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => VerColumnas,
        ClaseElemento.Trabe => VerTrabes,
        ClaseElemento.Diagonal => VerDiagonales,
        ClaseElemento.Muro => VerMuros,
        ClaseElemento.Losa => VerLosas,
        _ => true
    };

    private static Brush Color3D(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => ColorColumna,
        ClaseElemento.Trabe => ColorTrabe,
        ClaseElemento.Diagonal => ColorDiagonal,
        ClaseElemento.Muro => ColorMuro,
        _ => ColorLosa
    };

    // ==================================================================
    // Vista 3D
    // ==================================================================

    /// <summary>Dibuja la vista 3D completa del modelo.</summary>
    public void Dibujar3D(Canvas lienzo)
    {
        lienzo.Children.Clear();

        var elementos = Elementos(conCorte: true);
        if (elementos.Count == 0)
        {
            Aviso(lienzo, Modelo is null
                ? "Lee el modelo de ETABS para verlo aquí."
                : "El modelo no trae elementos que mostrar.");
            return;
        }

        // El patron 'is not Camara cam' en lugar de comprobar null: asi 'cam' queda
        // como no anulable y el compilador no avisa al usarla dentro de la funcion
        // local de abajo, que podria llamarse en cualquier momento.
        if (PrepararCamara(lienzo, elementos) is not Camara cam)
        {
            return;
        }

        var (w, h) = (cam.W, cam.H);
        var (sa, ca, se, ce) = (cam.Sa, cam.Ca, cam.Se, cam.Ce);

        // Algoritmo del pintor: primero lo que está más lejos, para que los
        // elementos del frente tapen a los de atrás y se lea la profundidad.
        // 'd' crece hacia el fondo, así que lo más lejano es el 'd' MAYOR y el
        // orden va de mayor a menor. Al revés, los muros del fondo taparían a los
        // del frente y el modelo se vería del revés.
        var ordenados = elementos
            .Select(el => (El: el, Prof: Profundidad(el, sa, ca)))
            .OrderByDescending(t => t.Prof)
            .ToList();

        foreach (var (el, _) in ordenados)
        {
            // Losas y muros se dibujan como paño, así el 3D no queda transparente
            if (el.Vertices3D.Count >= 3)
            {
                var poly = new Polygon
                {
                    Stroke = Color3D(el.Clase),
                    StrokeThickness = 0.8,
                    Fill = el.Clase == ClaseElemento.Muro ? RellenoMuro : RellenoLosa,
                    ToolTip = Etiqueta(el)
                };

                foreach (var p in el.Vertices3D)
                {
                    poly.Points.Add(cam.APantalla(p.X, p.Y, p.Z));
                }

                lienzo.Children.Add(poly);
                continue;
            }

            var p1 = cam.APantalla(el.X1, el.Y1, el.Z1);
            var p2 = cam.APantalla(el.X2, el.Y2, el.Z2);

            lienzo.Children.Add(new Line
            {
                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
                Stroke = Color3D(el.Clase),
                StrokeThickness = el.Clase == ClaseElemento.Columna ? 1.9 : 1.3,
                ToolTip = Etiqueta(el)
            });
        }

        DibujarTerna(lienzo, w, h, sa, ca, se, ce);
        Leyenda(lienzo, elementos.Count);
    }

    /// <summary>
    /// La cámara: proyección, encuadre y escala, resueltos una sola vez.
    /// </summary>
    /// <remarks>
    /// Está extraído porque lo usan la vista de alambre y la extruida. Duplicar
    /// estas cuentas en dos sitios acaba siempre igual: se corrige un signo en una y
    /// no en la otra, y una de las dos vistas queda espejeada sin que se sepa por qué.
    /// </remarks>
    private sealed class Camara
    {
        public double W { get; init; }
        public double H { get; init; }
        public double Escala { get; init; }
        public double CxModelo { get; init; }
        public double CyModelo { get; init; }
        public double Sa { get; init; }
        public double Ca { get; init; }
        public double Se { get; init; }
        public double Ce { get; init; }
        public double PanX { get; init; }
        public double PanY { get; init; }

        /// <summary>
        /// Proyección axonométrica: giro alrededor del eje vertical y luego inclinación.
        /// </summary>
        /// <remarks>
        /// 'u' va a la derecha en pantalla y 'v' hacia abajo, que es como crece la
        /// coordenada de un lienzo. 'd' es la distancia hacia el fondo.
        /// <para>
        /// Lo que se ve hacia ARRIBA es <c>Z*cos(e) + d*sin(e)</c>: la altura aporta
        /// todo cuando se mira en horizontal (e=0) y nada cuando se mira desde arriba
        /// (e=90), donde en cambio aporta todo la profundidad. Como en el lienzo la
        /// 'v' crece hacia abajo, se invierte el signo.
        /// </para>
        /// <para>
        /// Los DOS términos van con el mismo signo. Sumarlos con signos opuestos deja
        /// la planta espejeada de norte a sur y pone lo lejano abajo.
        /// </para>
        /// </remarks>
        public (double U, double V, double Prof) Proyectar(double x, double y, double z)
        {
            var u = (x * Ca) - (y * Sa);
            var d = (x * Sa) + (y * Ca);
            return (u, -((z * Ce) + (d * Se)), d);
        }

        public Point APantalla(double x, double y, double z)
        {
            var (u, v, _) = Proyectar(x, y, z);
            return new Point(
                (W / 2) + ((u - CxModelo) * Escala) + PanX,
                (H / 2) + ((v - CyModelo) * Escala) + PanY);
        }

        /// <summary>Distancia al fondo de un punto. Sirve para ordenar el dibujo.</summary>
        public double Prof(double x, double y) => (x * Sa) + (y * Ca);
    }

    /// <summary>
    /// Arma la cámara para el modelo dado, o devuelve <c>null</c> y deja el aviso.
    /// </summary>
    private Camara? PrepararCamara(Canvas lienzo, List<ElementoEtabs> elementos)
    {
        var w = lienzo.ActualWidth;
        var h = lienzo.ActualHeight;

        if (w < 40 || h < 40)
        {
            return null;
        }

        var a = Azimut * Math.PI / 180.0;
        var e = Elevacion * Math.PI / 180.0;
        var (sa, ca) = (Math.Sin(a), Math.Cos(a));
        var (se, ce) = (Math.Sin(e), Math.Cos(e));

        var base_ = new Camara
        {
            W = w, H = h, Escala = 1, CxModelo = 0, CyModelo = 0,
            Sa = sa, Ca = ca, Se = se, Ce = ce, PanX = 0, PanY = 0
        };

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;

        void Medir(double x, double y, double z)
        {
            var (u, v, _) = base_.Proyectar(x, y, z);
            uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
            vMin = Math.Min(vMin, v); vMax = Math.Max(vMax, v);
        }

        foreach (var el in elementos)
        {
            if (el.Vertices3D.Count >= 3)
            {
                foreach (var p in el.Vertices3D)
                {
                    Medir(p.X, p.Y, p.Z);
                }
            }
            else
            {
                Medir(el.X1, el.Y1, el.Z1);
                Medir(el.X2, el.Y2, el.Z2);
            }
        }

        if (uMax <= uMin || vMax <= vMin)
        {
            Aviso(lienzo, "El modelo no tiene extensión suficiente para dibujarlo.");
            return null;
        }

        return new Camara
        {
            W = w,
            H = h,
            Escala = Math.Min((w - (2 * Margen)) / (uMax - uMin),
                              (h - (2 * Margen)) / (vMax - vMin)) * Zoom,
            CxModelo = (uMin + uMax) / 2,
            CyModelo = (vMin + vMax) / 2,
            Sa = sa, Ca = ca, Se = se, Ce = ce,
            PanX = PanX, PanY = PanY
        };
    }

    /// <summary>Profundidad media, para ordenar el dibujo de lejos a cerca.</summary>
    private static double Profundidad(ElementoEtabs el, double sa, double ca)
    {
        if (el.Vertices3D.Count >= 3)
        {
            return el.Vertices3D.Average(p => (p.X * sa) + (p.Y * ca));
        }

        var d1 = (el.X1 * sa) + (el.Y1 * ca);
        var d2 = (el.X2 * sa) + (el.Y2 * ca);
        return (d1 + d2) / 2;
    }

    /// <summary>
    /// La <b>terna XYZ</b> en una esquina: para saber dónde está el norte del modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió que se vea, y antes no se veía: estaba dibujada, pero en el mismo gris claro
    /// de todo lo demás y con una línea de 1.2 píxeles, así que sobre el fondo claro de la
    /// pestaña era invisible. En una vista que se puede girar, no saber para dónde cae la X
    /// deja al plano sin referencia.
    /// </para>
    /// <para>
    /// Ahora va con los <b>colores de siempre</b> —X roja, Y verde, Z azul, que es lo que usa
    /// ETABS y cualquier programa de 3D—, con punta de flecha, la letra en su color y una
    /// placa detrás para que se lea igual sobre el modelo que sobre el fondo.
    /// </para>
    /// <para>
    /// Y usa la <b>misma proyección</b> que el modelo: si aquí se usara otra fórmula, la
    /// terna indicaría un giro distinto del que se está viendo, que es peor que no ponerla.
    /// </para>
    /// </remarks>
    private void DibujarTerna(
        Canvas lienzo, double w, double h, double sa, double ca, double se, double ce)
    {
        const double L = 34;
        const double Radio = 46;

        var ox = 52.0;
        var oy = h - 46;

        // La placa: un círculo translúcido que despega la terna del dibujo, y SIN CONTORNO
        // —se pidió quitarlo—: el círculo dibujado competía con los ejes y hacía parecer que
        // la terna era un objeto del modelo. Sin la raya, la placa solo aclara el fondo lo
        // justo para que las tres letras se lean, y los ejes se quedan con todo el
        // protagonismo.
        var placa = new Ellipse
        {
            Width = Radio * 2,
            Height = Radio * 2,
            Fill = FondoTerna
        };

        Canvas.SetLeft(placa, ox - Radio);
        Canvas.SetTop(placa, oy - Radio);
        lienzo.Children.Add(placa);

        void Eje(double x, double y, double z, string nombre, Brush color)
        {
            // Misma proyección que el modelo. Si aquí se usara otra fórmula, la
            // terna indicaría un giro distinto al que se está viendo.
            var u = (x * ca) - (y * sa);
            var d = (x * sa) + (y * ca);
            var v = -((z * ce) + (d * se));

            var fx = ox + (u * L);
            var fy = oy + (v * L);

            lienzo.Children.Add(new Line
            {
                X1 = ox, Y1 = oy, X2 = fx, Y2 = fy,
                Stroke = color,
                StrokeThickness = 2
            });

            // LA PUNTA DE FLECHA, que es lo que dice el SENTIDO del eje. Sin ella, un eje
            // que apunta hacia el observador y otro que se aleja se dibujan igual.
            var lx = fx - ox;
            var ly = fy - oy;
            var largo = Math.Sqrt((lx * lx) + (ly * ly));

            if (largo > 1e-6)
            {
                lx /= largo;
                ly /= largo;

                // La perpendicular, para abrir la flecha.
                var px = -ly;
                var py = lx;

                var punta = new Polygon
                {
                    Fill = color,
                    Stroke = color,
                    StrokeThickness = 0.5
                };

                punta.Points.Add(new Point(fx, fy));
                punta.Points.Add(new Point(fx - (lx * 7) + (px * 3), fy - (ly * 7) + (py * 3)));
                punta.Points.Add(new Point(fx - (lx * 7) - (px * 3), fy - (ly * 7) - (py * 3)));

                lienzo.Children.Add(punta);
            }

            var t = new TextBlock
            {
                Text = nombre,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = color
            };

            // La letra, un poco más allá de la punta y en la misma dirección, para que no
            // se monte sobre la flecha.
            Canvas.SetLeft(t, fx + (lx * 4) - 4);
            Canvas.SetTop(t, fy + (ly * 4) - 9);
            lienzo.Children.Add(t);
        }

        Eje(1, 0, 0, "X", ColorEjeX);
        Eje(0, 1, 0, "Y", ColorEjeY);
        Eje(0, 0, 1, "Z", ColorEjeZ);
    }

    // ==================================================================
    // Vista en planta
    // ==================================================================

    /// <summary>
    /// Dibuja la planta de un nivel: X a la derecha, Y hacia arriba.
    /// </summary>
    /// <param name="nivel">
    /// Nombre del nivel. Vacío o <c>null</c> dibuja todos los niveles juntos.
    /// </param>
    public void DibujarPlanta(Canvas lienzo, string? nivel)
    {
        lienzo.Children.Clear();

        var elementos = Elementos();

        if (!string.IsNullOrWhiteSpace(nivel))
        {
            elementos = elementos
                .Where(el => string.Equals(el.Story, nivel, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (elementos.Count == 0)
        {
            Aviso(lienzo, Modelo is null
                ? "Lee el modelo de ETABS para verlo aquí."
                : "Este nivel no tiene elementos visibles con los filtros actuales.");
            return;
        }

        var w = lienzo.ActualWidth;
        var h = lienzo.ActualHeight;
        if (w < 40 || h < 40)
        {
            return;
        }

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        void Medir(double x, double y)
        {
            xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x);
            yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y);
        }

        foreach (var el in elementos)
        {
            if (el.Vertices.Count >= 3)
            {
                foreach (var p in el.Vertices)
                {
                    Medir(p.X, p.Y);
                }
            }
            else
            {
                Medir(el.X1, el.Y1);
                Medir(el.X2, el.Y2);
            }
        }

        // ==============================================================================
        //  LA CUADRÍCULA DE EJES
        // ==============================================================================
        //  Se mide ANTES de calcular la escala, junto con los elementos, porque un eje puede
        //  quedar por fuera de lo construido —el eje de una fachada que no lleva muro— y si
        //  no se contara, ese eje se dibujaría fuera del lienzo.
        var ejesX = EjesDeLaPlanta(true);
        var ejesY = EjesDeLaPlanta(false);

        foreach (var (_, o) in ejesX)
        {
            xMin = Math.Min(xMin, o);
            xMax = Math.Max(xMax, o);
        }

        foreach (var (_, o) in ejesY)
        {
            yMin = Math.Min(yMin, o);
            yMax = Math.Max(yMax, o);
        }

        // Un nivel de una sola columna tiene extensión cero: se le da holgura
        // para no dividir por cero al calcular la escala.
        if (xMax - xMin < 1e-6) { xMin -= 1; xMax += 1; }
        if (yMax - yMin < 1e-6) { yMin -= 1; yMax += 1; }

        // El hueco util es el lienzo menos los dos margenes, que NO son iguales: arriba y a
        // la izquierda hay que dejar sitio para las cotas y las burbujas.
        var escala = Math.Min((w - MargenAnotado - MargenLibre) / (xMax - xMin),
                              (h - MargenAnotado - MargenLibre) / (yMax - yMin)) * Zoom;

        var cx = (xMin + xMax) / 2;
        var cy = (yMin + yMax) / 2;

        // Y el dibujo se centra en ese hueco, no en el lienzo: de ahi el desplazamiento de
        // media diferencia de margenes, que lo corre hacia el lado libre.
        var centroX = (w + MargenAnotado - MargenLibre) / 2;
        var centroY = (h + MargenAnotado - MargenLibre) / 2;

        // La Y del modelo sube y la del lienzo baja, así que se invierte
        Point APantallaPlanta(double x, double y) => new(
            centroX + ((x - cx) * escala) + PanX,
            centroY - ((y - cy) * escala) + PanY);

        // EL ORDEN DE PINTADO, de atrás hacia adelante: losa, muro, trabe y columna. Ahora
        // que las piezas se dibujan con su huella real y rellenas, el orden importa: con
        // todo al mismo nivel una trabe ancha podía tapar la sección de la columna, que es
        // justo lo que se viene a comprobar en la vista previa. Es el mismo criterio del
        // plano, donde las secciones quedan al frente.
        static int Capa(ElementoEtabs el) => el.Clase switch
        {
            ClaseElemento.Losa => 0,
            ClaseElemento.Muro => 1,
            ClaseElemento.Columna => 3,
            _ => 2
        };

        // Los EJES van primero, o sea al fondo de todo: son la referencia, no el dibujo. Es
        // el mismo orden que se pidió para el plano de AutoCAD, donde la capa de los ejes se
        // manda al fondo.
        DibujarEjesEnPlanta(lienzo, ejesX, ejesY, APantallaPlanta, xMin, yMin, xMax, yMax);

        foreach (var el in elementos.OrderBy(Capa))
        {
            if (el.Clase == ClaseElemento.Losa && el.Vertices.Count >= 3)
            {
                var poly = new Polygon
                {
                    Stroke = ColorLosa,
                    StrokeThickness = 0.7,
                    Fill = RellenoLosa,
                    ToolTip = Etiqueta(el)
                };

                foreach (var p in el.Vertices)
                {
                    poly.Points.Add(APantallaPlanta(p.X, p.Y));
                }

                lienzo.Children.Add(poly);
                continue;
            }

            // La columna es un punto en planta: se dibuja su sección real, GIRADA
            if (el.Clase == ClaseElemento.Columna)
            {
                DibujarColumnaEnPlanta(lienzo, el, APantallaPlanta, escala);
                continue;
            }

            // ==========================================================================
            //  LA TRABE Y EL MURO, CON SU GROSOR DE VERDAD
            // ==========================================================================
            //  Se pidió tal cual, y era lo que faltaba: la trabe salía como una línea de
            //  1.4 píxeles pase lo que pase —una de 15 y otra de 35 se veían iguales— y el
            //  muro como un trazo grueso con un mínimo de 2.2 píxeles, que a poco zoom
            //  engorda el muro y a mucho zoom lo adelgaza. Ninguna de las dos cosas dice
            //  por dónde pasan los PAÑOS, que es lo que se viene a comprobar aquí.
            //
            //  Ahora se dibuja la HUELLA EN PLANTA de la pieza: el rectángulo de largo por
            //  ancho, en metros del modelo, proyectado como todo lo demás. Así el grosor de
            //  la pantalla es el grosor de verdad a la escala del momento, y crece y decrece
            //  con el zoom igual que la planta.
            //
            //  El ancho es el AnchoM —en una trabe es el t2 de ETABS, la dimensión
            //  horizontal; el peralte es vertical y en planta no se ve—, y si el modelo no
            //  lo dio se usa el MISMO valor de omisión que usará el dibujante.
            var anchoReal = AnchoEnPlanta(el);

            // Con el zoom muy lejos la huella mediría menos de un píxel y no se vería: ahí
            // se dibuja una línea de un pelo, que es lo honesto —a esa escala el grosor no
            // se puede representar— y así el elemento no desaparece del dibujo.
            if (anchoReal * escala >= 1.2)
            {
                DibujarBarraEnPlanta(lienzo, el, APantallaPlanta, anchoReal);
                continue;
            }

            var p1 = APantallaPlanta(el.X1, el.Y1);
            var p2 = APantallaPlanta(el.X2, el.Y2);

            lienzo.Children.Add(new Line
            {
                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
                Stroke = el.Clase == ClaseElemento.Muro ? ColorMuro : Color3D(el.Clase),
                StrokeThickness = 1.2,
                ToolTip = Etiqueta(el)
            });
        }

        Leyenda(lienzo, elementos.Count, nivel);
    }

    /// <summary>
    /// La columna en planta: su <b>sección de verdad</b>, girada como en el modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes era un <c>Rectangle</c> de WPF, y un <c>Rectangle</c> <b>no gira</b>: está
    /// alineado a los ejes del lienzo. Así que una columna de 20×60 girada 90° se veía de
    /// 20×60 derecha —justo lo contrario de lo que dice el modelo— y la previsualización no
    /// coincidía con el plano que después sale en AutoCAD.
    /// </para>
    /// <para>
    /// Ahora se usa el <b>mismo</b> par de funciones que el dibujante de AutoCAD:
    /// <c>SeccionEnPlanta.Contorno</c> da el perfil real de la forma —I, C, T, L, cajón o
    /// rectángulo— y <c>SeccionEnPlanta.Colocar</c> lo gira con <c>AnguloGrados</c> y lo
    /// centra en el nudo. Al compartir la geometría, lo que se ve en la vista previa es lo
    /// que se va a dibujar, y no una aproximación parecida.
    /// </para>
    /// <para>
    /// El giro se calcula en <b>coordenadas del modelo</b> y solo después se proyecta cada
    /// vértice a pantalla, que es lo que hace que el sentido del giro salga bien sin tener
    /// que cambiarle el signo: la inversión de la Y ya está metida en la proyección.
    /// </para>
    /// </remarks>
    private static void DibujarColumnaEnPlanta(
        Canvas lienzo, ElementoEtabs el, Func<double, double, Point> aPantalla, double escala)
    {
        var b = el.AnchoM;
        var h = el.PeralteM;

        // Sin medidas no se puede dibujar la sección: se deja la marca mínima de siempre,
        // que al menos dice que ahí hay una columna.
        if (b <= 0.01 || h <= 0.01 || b * escala < 3 || h * escala < 3)
        {
            MarcaDeColumna(lienzo, el, aPantalla);
            return;
        }

        // La sección REDONDA se dibuja redonda: un círculo girado sigue siendo el mismo
        // círculo, así que aquí el ángulo no hace falta.
        if (SeccionEnPlanta.EsRedonda(el.Forma))
        {
            var centro = aPantalla(el.X1, el.Y1);
            var d = b * escala;

            var elipse = new Ellipse
            {
                Width = d,
                Height = d,
                Stroke = ColorColumna,
                StrokeThickness = 1.1,
                Fill = RellenoColumna,
                ToolTip = Etiqueta(el)
            };

            Canvas.SetLeft(elipse, centro.X - (d / 2));
            Canvas.SetTop(elipse, centro.Y - (d / 2));
            lienzo.Children.Add(elipse);
            return;
        }

        // El contorno de la forma, centrado en el origen, y luego girado y llevado al nudo.
        // Son las MISMAS funciones del dibujante de AutoCAD.
        var contorno = SeccionEnPlanta.Contorno(
            el.Forma, b, h, el.PatinM, el.AlmaM, el.ParedM);

        // Una forma que no da contorno —o que lo da incompleto— se queda con la marca: es
        // mejor un cuadradito honesto que un polígono de dos puntos, que no se ve.
        if (contorno.Length < 6)
        {
            MarcaDeColumna(lienzo, el, aPantalla);
            return;
        }

        var puesto = SeccionEnPlanta.Colocar(
            contorno, el.X1, el.Y1, el.AnguloGrados);

        var poly = new Polygon
        {
            Stroke = ColorColumna,
            StrokeThickness = 1.1,
            Fill = RellenoColumna,
            ToolTip = Etiqueta(el)
        };

        for (var i = 0; i + 1 < puesto.Length; i += 2)
        {
            poly.Points.Add(aPantalla(puesto[i], puesto[i + 1]));
        }

        lienzo.Children.Add(poly);
    }

    /// <summary>La marca mínima de una columna: cuando su sección no se puede dibujar.</summary>
    /// <remarks>
    /// Pasa en dos casos: que el modelo no diera las medidas de la sección, o que el zoom
    /// esté tan lejos que la sección mida menos de tres píxeles. En los dos, un cuadradito
    /// fijo es mejor que nada, porque dice que <b>ahí hay una columna</b>; lo que no puede
    /// hacer es pretender que ese tamaño significa algo.
    /// </remarks>
    private static void MarcaDeColumna(
        Canvas lienzo, ElementoEtabs el, Func<double, double, Point> aPantalla)
    {
        var c = aPantalla(el.X1, el.Y1);

        var r = new Rectangle
        {
            Width = 5,
            Height = 5,
            Stroke = ColorColumna,
            StrokeThickness = 1.1,
            Fill = RellenoColumna,
            ToolTip = Etiqueta(el)
        };

        Canvas.SetLeft(r, c.X - 2.5);
        Canvas.SetTop(r, c.Y - 2.5);
        lienzo.Children.Add(r);
    }

    /// <summary>
    /// La cuadrícula del modelo en una dirección, ya <b>sin ejes repetidos</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sale del modelo si el programa la dio y, si no, se <b>deduce de las columnas</b>, que
    /// es el mismo respaldo del plano: <c>GetGridSys_2</c> no existe en todas las versiones
    /// de ETABS.
    /// </para>
    /// <para>
    /// Y pasa por el mismo filtro de repetidos que el plano —<c>EjesPlano.SinRepetidos</c>,
    /// con un centímetro de holgura—, porque la cuadrícula del modelo suele traer el mismo
    /// eje declarado dos veces y entonces se dibujarían dos líneas y dos burbujas encima de
    /// la otra.
    /// </para>
    /// </remarks>
    /// <param name="enX"><c>true</c> = los verticales; <c>false</c> = los horizontales.</param>
    private List<(string Id, double Ordenada)> EjesDeLaPlanta(bool enX)
    {
        if (!VerEjes || Modelo is null)
        {
            return new List<(string Id, double Ordenada)>();
        }

        var ejes = Modelo.Ejes ?? EjesModelo.DesdeGeometria(Modelo);
        var lista = (enX ? ejes.X : ejes.Y)
            .Select(e => (e.Id, e.Ordenada))
            .ToList();

        return EjesPlano.SinRepetidos(lista, 0.01);
    }

    /// <summary>
    /// Dibuja la <b>cuadrícula</b>: sus líneas a trazos y una burbuja con su nombre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las líneas van en coordenadas del modelo —así se estiran con el zoom, como todo lo
    /// demás— pero las <b>burbujas van en píxeles</b>: son un rótulo, y un rótulo tiene que
    /// leerse igual de cerca que de lejos. Es lo mismo que hace el plano, donde la burbuja
    /// tiene su radio en papel y no en metros.
    /// </para>
    /// <para>
    /// Una burbuja por eje, del lado de arriba en los verticales y del lado izquierdo en los
    /// horizontales. En el plano van en los cuatro lados porque ahí se acotan; aquí sobra con
    /// una, y así la vista no se llena de círculos.
    /// </para>
    /// </remarks>
    private static void DibujarEjesEnPlanta(
        Canvas lienzo,
        List<(string Id, double Ordenada)> ejesX,
        List<(string Id, double Ordenada)> ejesY,
        Func<double, double, Point> aPantalla,
        double xMin, double yMin, double xMax, double yMax)
    {
        if (ejesX.Count == 0 && ejesY.Count == 0)
        {
            return;
        }

        // El rectángulo de lo dibujado, en píxeles. La Y está invertida, así que la esquina
        // de arriba a la izquierda es (xMin, yMax).
        var arriba = aPantalla(xMin, yMax);
        var abajo = aPantalla(xMax, yMin);

        // ==============================================================================
        //  EL REPARTO DEL MARGEN, DESDE EL DIBUJO HACIA AFUERA
        // ==============================================================================
        //  Se pidió que las cotas vayan SOLO arriba y a la izquierda, así que en esos dos
        //  lados se apilan tres cosas y el orden es el del plano de obra:
        //
        //        dibujo | 22 cota parcial | 40 cota total | 58 burbuja
        //
        //  La burbuja va la ÚLTIMA, la más afuera. Antes estaba pegada al dibujo y las cotas
        //  iban por el otro lado; ahora que comparten lado, si la burbuja se quedara dentro
        //  las líneas de cota le pasarían por encima.
        const double Sale = 10;
        const double Radio = 9;
        const double SaleBurbuja = 58;

        var trazos = new DoubleCollection { 8, 4, 2, 4 };

        void Linea(double x1, double y1, double x2, double y2)
        {
            lienzo.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = ColorEje,
                StrokeThickness = 0.8,

                // A trazos, como el DASHDOT de la capa E-EJES del plano: así el eje no se
                // confunde con una pieza.
                StrokeDashArray = trazos
            });
        }

        void Burbuja(double cx, double cy, string id)
        {
            var burbuja = new Ellipse
            {
                Width = Radio * 2,
                Height = Radio * 2,
                Stroke = ColorEje,
                StrokeThickness = 0.9,
                Fill = RellenoBurbuja
            };

            Canvas.SetLeft(burbuja, cx - Radio);
            Canvas.SetTop(burbuja, cy - Radio);
            lienzo.Children.Add(burbuja);

            // El texto se centra a mano en un cuadro del tamaño de la burbuja: es más
            // predecible que medir la cadena, y con uno o dos caracteres es exacto.
            var t = new TextBlock
            {
                Text = id,
                FontSize = 9.5,
                Foreground = ColorEjeTexto,
                TextAlignment = TextAlignment.Center,
                Width = Radio * 2,
                Height = Radio * 2,
                Padding = new Thickness(0, Radio - 7, 0, 0)
            };

            Canvas.SetLeft(t, cx - Radio);
            Canvas.SetTop(t, cy - Radio);
            lienzo.Children.Add(t);
        }

        // ---- los verticales, con su burbuja arriba ---------------------------------
        //  La LÍNEA del eje sí llega hasta la burbuja: es lo que la ata a su eje y lo que
        //  hace que se lea de un golpe cuál es cuál.
        foreach (var (id, o) in ejesX)
        {
            var x = aPantalla(o, yMin).X;

            Linea(x, arriba.Y - SaleBurbuja + Radio, x, abajo.Y + Sale);
            Burbuja(x, arriba.Y - SaleBurbuja, id);
        }

        // ---- los horizontales, con su burbuja a la izquierda -----------------------
        foreach (var (id, o) in ejesY)
        {
            var y = aPantalla(xMin, o).Y;

            Linea(arriba.X - SaleBurbuja + Radio, y, abajo.X + Sale, y);
            Burbuja(arriba.X - SaleBurbuja, y, id);
        }

        // ==============================================================================
        //  Y LAS COTAS, QUE ES PARA LO QUE SIRVE UNA CUADRÍCULA
        // ==============================================================================
        //  Un eje sin cota no dice nada: lo que se replantea en obra son las DISTANCIAS
        //  entre ejes, y comprobarlas antes de mandar el plano es justo lo que se viene a
        //  hacer a esta pantalla.
        //
        //  Van ARRIBA las de los ejes verticales y a la IZQUIERDA las de los horizontales,
        //  que es como se pidió y como se lee un plano: las cotas de un lado y el dibujo
        //  libre por el otro. Comparten lado con las burbujas, y por eso la burbuja se fue
        //  más afuera: primero la cota parcial, luego la total y al final la burbuja.
        AcotarEjes(lienzo, ejesX, ejesY, aPantalla, arriba, abajo);
    }

    /// <summary>
    /// Las <b>cotas</b> entre ejes consecutivos, y la total.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En <b>metros con tres decimales</b>, como las del plano, porque una cota de
    /// replanteo se lee al milímetro.
    /// </para>
    /// <para>
    /// El número se escribe <b>solo si cabe</b>. Con la vista alejada, dos ejes pueden
    /// quedar a diez píxeles uno de otro y ahí los rótulos se encimarían hasta ser
    /// ilegibles: es mejor ver la línea de cota sin número —y acercarse para leerlo— que un
    /// borrón de cifras superpuestas. La línea siempre se dibuja.
    /// </para>
    /// </remarks>
    private static void AcotarEjes(
        Canvas lienzo,
        List<(string Id, double Ordenada)> ejesX,
        List<(string Id, double Ordenada)> ejesY,
        Func<double, double, Point> aPantalla,
        Point arriba, Point abajo)
    {
        // Las mismas distancias del reparto del margen: la parcial a 22 del dibujo y la
        // total a 40. La burbuja va después, a 58.
        const double Parcial = 22;
        const double Total = 40;

        string Metros(double v) =>
            v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        void Raya(double x1, double y1, double x2, double y2)
        {
            lienzo.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = ColorCota,
                StrokeThickness = 0.8
            });
        }

        // La rayita oblicua de los extremos, la de toda la vida en un plano de obra.
        void Tick(double x, double y)
        {
            Raya(x - 3, y + 3, x + 3, y - 3);
        }

        // El número de una cota HORIZONTAL: encima de su línea, centrado en el claro.
        void NumeroArriba(double cx, double y, string texto, double hueco)
        {
            // 5.6 píxeles por carácter a 9 puntos. Si no cabe, no se escribe: vale más la
            // línea de cota limpia que un borrón de cifras encimadas.
            if (hueco < texto.Length * 5.6)
            {
                return;
            }

            var t = new TextBlock
            {
                Text = texto,
                FontSize = 9,
                Foreground = ColorEjeTexto,
                TextAlignment = TextAlignment.Center,
                Width = hueco
            };

            Canvas.SetLeft(t, cx - (hueco / 2));
            Canvas.SetTop(t, y - 13);
            lienzo.Children.Add(t);
        }

        // El número de una cota VERTICAL: GIRADO, centrado sobre su línea.
        //
        //  Girarlo es lo correcto aquí y no un adorno: en un plano las cotas verticales se
        //  leen de abajo arriba, y además el número cabe donde no cabría en horizontal —a la
        //  izquierda solo hay 18 píxeles entre la cota parcial y la total—.
        //
        //  Con RotateTransform(-90) y el origen en la esquina, la caja de W x H pasa a ocupar
        //  H de ancho y W de alto CRECIENDO HACIA ARRIBA, así que para centrarla en la cota
        //  se la coloca media caja más abajo y medio renglón a la izquierda.
        void NumeroAlLado(double x, double cy, string texto, double hueco)
        {
            // Aquí lo que limita es el ALTO del renglón, no el ancho del texto: por debajo
            // de 13 píxeles dos cotas seguidas se encimarían.
            if (hueco < 13)
            {
                return;
            }

            var t = new TextBlock
            {
                Text = texto,
                FontSize = 9,
                Foreground = ColorEjeTexto,
                TextAlignment = TextAlignment.Center,
                Width = hueco,
                RenderTransform = new RotateTransform(-90)
            };

            Canvas.SetLeft(t, x - 13);
            Canvas.SetTop(t, cy + (hueco / 2));
            lienzo.Children.Add(t);
        }

        // ---- las de los ejes verticales, ARRIBA ------------------------------------
        if (ejesX.Count >= 2)
        {
            var orden = ejesX.OrderBy(e => e.Ordenada).ToList();
            var y = arriba.Y - Parcial;

            for (var i = 0; i + 1 < orden.Count; i++)
            {
                var x1 = aPantalla(orden[i].Ordenada, 0).X;
                var x2 = aPantalla(orden[i + 1].Ordenada, 0).X;

                Raya(x1, y, x2, y);
                Tick(x1, y);
                Tick(x2, y);

                NumeroArriba((x1 + x2) / 2, y,
                             Metros(orden[i + 1].Ordenada - orden[i].Ordenada),
                             Math.Abs(x2 - x1));
            }

            // LA TOTAL, un renglón más afuera. Solo con tres ejes o más: con dos sería la
            // misma cota escrita dos veces.
            if (orden.Count > 2)
            {
                var xa = aPantalla(orden[0].Ordenada, 0).X;
                var xb = aPantalla(orden[^1].Ordenada, 0).X;
                var yt = arriba.Y - Total;

                Raya(xa, yt, xb, yt);
                Tick(xa, yt);
                Tick(xb, yt);

                NumeroArriba((xa + xb) / 2, yt,
                             Metros(orden[^1].Ordenada - orden[0].Ordenada),
                             Math.Abs(xb - xa));
            }
        }

        // ---- las de los ejes horizontales, a la IZQUIERDA --------------------------
        if (ejesY.Count >= 2)
        {
            var orden = ejesY.OrderBy(e => e.Ordenada).ToList();
            var x = arriba.X - Parcial;

            for (var i = 0; i + 1 < orden.Count; i++)
            {
                var y1 = aPantalla(0, orden[i].Ordenada).Y;
                var y2 = aPantalla(0, orden[i + 1].Ordenada).Y;

                Raya(x, y1, x, y2);
                Tick(x, y1);
                Tick(x, y2);

                NumeroAlLado(x, (y1 + y2) / 2,
                             Metros(orden[i + 1].Ordenada - orden[i].Ordenada),
                             Math.Abs(y2 - y1));
            }

            if (orden.Count > 2)
            {
                var ya = aPantalla(0, orden[0].Ordenada).Y;
                var yb = aPantalla(0, orden[^1].Ordenada).Y;
                var xt = arriba.X - Total;

                Raya(xt, ya, xt, yb);
                Tick(xt, ya);
                Tick(xt, yb);

                NumeroAlLado(xt, (ya + yb) / 2,
                             Metros(orden[^1].Ordenada - orden[0].Ordenada),
                             Math.Abs(yb - ya));
            }
        }
    }

    /// <summary>
    /// El ancho en planta que le toca a la pieza, con el <b>mismo</b> respaldo del dibujante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cuando ETABS no da la medida —pasa con las propiedades de mampostería y con las
    /// secciones que el lector no puede desglosar— el dibujante de planos no se queda de
    /// brazos cruzados: usa <c>ESPESOR_MURO_CM</c>, 15 cm, en el muro y 20 cm en la trabe, y
    /// sigue dibujando. Aquí se usan <b>los mismos dos números</b>, y por el mismo motivo:
    /// si la vista previa dibujara un pelo donde el plano va a dibujar 15 cm, la vista previa
    /// estaría mintiendo.
    /// </para>
    /// <para>
    /// Están escritos aquí como constantes en vez de leerse de la hoja porque el visor no
    /// tiene por qué depender de la configuración del plano: son el respaldo del respaldo, y
    /// lo que importa es que coincidan con <c>PlantaDrawer</c>.
    /// </para>
    /// </remarks>
    private static double AnchoEnPlanta(ElementoEtabs el) =>
        el.AnchoM > 0.01
            ? el.AnchoM
            : el.Clase == ClaseElemento.Muro
                ? EspesorMuroPorOmision
                : AnchoTrabePorOmision;

    /// <summary>Los mismos valores de omisión que <c>PlantaDrawer</c>, en metros.</summary>
    private const double EspesorMuroPorOmision = 0.15;

    private const double AnchoTrabePorOmision = 0.20;

    /// <summary>
    /// La <b>huella en planta</b> de una barra: su largo por su ancho, en su sitio.
    /// </summary>
    /// <remarks>
    /// El rectángulo se construye a partir de la dirección de la propia barra, así que sale
    /// bien en cualquier orientación —también en las trabes en diagonal— y sin depender del
    /// giro de los ejes locales: en una trabe el ancho se mide perpendicular a su eje.
    /// </remarks>
    private void DibujarBarraEnPlanta(
        Canvas lienzo, ElementoEtabs el, Func<double, double, Point> aPantalla, double ancho)
    {
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            return;
        }

        // La perpendicular unitaria, que es por donde se abre el ancho.
        var nx = -dy / largo * (ancho / 2);
        var ny = dx / largo * (ancho / 2);

        var poly = new Polygon
        {
            Stroke = el.Clase == ClaseElemento.Muro ? ColorMuro : Color3D(el.Clase),
            StrokeThickness = 0.9,
            Fill = el.Clase switch
            {
                ClaseElemento.Muro => RellenoMuro,
                ClaseElemento.Trabe => RellenoTrabe,
                _ => RellenoDiagonal
            },
            ToolTip = Etiqueta(el)
        };

        poly.Points.Add(aPantalla(el.X1 + nx, el.Y1 + ny));
        poly.Points.Add(aPantalla(el.X2 + nx, el.Y2 + ny));
        poly.Points.Add(aPantalla(el.X2 - nx, el.Y2 - ny));
        poly.Points.Add(aPantalla(el.X1 - nx, el.Y1 - ny));

        lienzo.Children.Add(poly);
    }

    // ==================================================================
    // Auxiliares
    // ==================================================================

    /// <summary>
    /// Los elementos que toca dibujar: los de los filtros y, si hay, los <b>del corte</b>.
    /// </summary>
    /// <param name="conCorte">
    /// <c>true</c> en las vistas de volumen, que son las que admiten corte por un eje;
    /// <c>false</c> en la planta, donde un corte no tiene sentido —la planta YA es un corte
    /// horizontal— y donde además se elige el nivel aparte.
    /// </param>
    private List<ElementoEtabs> Elementos(bool conCorte = false) =>
        Modelo is null
            ? new List<ElementoEtabs>()
            : Modelo.Elementos
                .Where(el => Visible(el.Clase) && (!conCorte || EnElCorte(el)))
                .ToList();

    /// <summary>
    /// ¿Este elemento entra en el <b>corte</b> por el eje elegido?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un corte por un eje es lo que en obra se llama un <b>alzado</b>: se mira solo lo que
    /// hay sobre ese eje. Aquí se resuelve como una <b>rebanada</b> de espesor
    /// <see cref="CorteEspesorM"/> centrada en la ordenada del eje, y entra todo lo que la
    /// toque, aunque sea de refilón.
    /// </para>
    /// <para>
    /// Y se mira el elemento COMPLETO, no su centro: una trabe que cruza el eje entra,
    /// aunque su centro esté a diez metros. Si se filtrara por el centro, en un corte por el
    /// eje 3 desaparecerían justamente las trabes que llegan a él, que son las que se quiere
    /// ver.
    /// </para>
    /// <para>
    /// El <b>muro y la losa</b> se miran por sus vértices, por lo mismo: un muro que corre a
    /// lo largo del eje del corte tiene que salir entero.
    /// </para>
    /// </remarks>
    private bool EnElCorte(ElementoEtabs el)
    {
        if (CorteEje.Length == 0)
        {
            return true;
        }

        var medio = Math.Max(CorteEspesorM, 0.05) / 2;

        double Coord(double x, double y) => CorteEnX ? x : y;

        var min = double.MaxValue;
        var max = double.MinValue;

        if (el.Vertices.Count > 0)
        {
            foreach (var p in el.Vertices)
            {
                var c = Coord(p.X, p.Y);
                min = Math.Min(min, c);
                max = Math.Max(max, c);
            }
        }
        else
        {
            var c1 = Coord(el.X1, el.Y1);
            var c2 = Coord(el.X2, el.Y2);
            min = Math.Min(c1, c2);
            max = Math.Max(c1, c2);
        }

        // Se solapan la rebanada del corte y la extensión del elemento.
        return max >= CorteOrdenada - medio && min <= CorteOrdenada + medio;
    }

    private static string Etiqueta(ElementoEtabs el)
    {
        var s = $"{el.Clase}  {el.Etiqueta}";

        if (!string.IsNullOrWhiteSpace(el.Story))
        {
            s += $"\nNivel: {el.Story}";
        }

        if (!string.IsNullOrWhiteSpace(el.Seccion))
        {
            s += $"\nSección: {el.Seccion}  {el.Dimensiones}";
        }

        return s + $"\nLargo: {el.LargoM:N2} m";
    }

    private static void Aviso(Canvas lienzo, string texto)
    {
        var t = new TextBlock
        {
            Text = texto,
            FontSize = 12,
            Foreground = Pincel(0x77, 0x88, 0x99),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        };

        Canvas.SetLeft(t, 18);
        Canvas.SetTop(t, 16);
        lienzo.Children.Add(t);
    }

    private void Leyenda(Canvas lienzo, int cuantos, string? nivel = null)
    {
        var texto = nivel is null
            ? $"{cuantos} elementos   ·   giro {Azimut:N0}°/{Elevacion:N0}°   ·   zoom {Zoom:N2}x"
            : $"Nivel {nivel}   ·   {cuantos} elementos   ·   zoom {Zoom:N2}x";

        // Y SI HAY CORTE, SE DICE. Un corte deja fuera media estructura, así que tiene que
        // estar escrito en la pantalla: si no, se mira un modelo incompleto creyendo que
        // está entero, y eso es peor que no tener corte.
        if (CorteEje.Length > 0 && nivel is null)
        {
            texto += $"   ·   corte por el eje {CorteEje}" +
                     $" ({(CorteEnX ? "X" : "Y")} = {CorteOrdenada:N3} m," +
                     $" rebanada de {CorteEspesorM * 100:N0} cm)";
        }

        var t = new TextBlock
        {
            Text = texto,
            FontSize = 10.5,
            Foreground = Pincel(0x77, 0x88, 0x99)
        };

        Canvas.SetLeft(t, 14);
        Canvas.SetTop(t, 10);
        lienzo.Children.Add(t);
    }
}
