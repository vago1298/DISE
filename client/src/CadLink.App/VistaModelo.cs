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

        var elementos = Elementos();
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

    /// <summary>Terna de ejes X, Y, Z en una esquina, como referencia de giro.</summary>
    private void DibujarTerna(
        Canvas lienzo, double w, double h, double sa, double ca, double se, double ce)
    {
        var ox = 46.0;
        var oy = h - 40;
        const double L = 26;

        void Eje(double x, double y, double z, string nombre)
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
                Stroke = ColorEje, StrokeThickness = 1.2
            });

            var t = new TextBlock
            {
                Text = nombre,
                FontSize = 10,
                Foreground = ColorEje
            };
            Canvas.SetLeft(t, fx + 2);
            Canvas.SetTop(t, fy - 8);
            lienzo.Children.Add(t);
        }

        Eje(1, 0, 0, "X");
        Eje(0, 1, 0, "Y");
        Eje(0, 0, 1, "Z");
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

        // Un nivel de una sola columna tiene extensión cero: se le da holgura
        // para no dividir por cero al calcular la escala.
        if (xMax - xMin < 1e-6) { xMin -= 1; xMax += 1; }
        if (yMax - yMin < 1e-6) { yMin -= 1; yMax += 1; }

        var escala = Math.Min((w - (2 * Margen)) / (xMax - xMin),
                              (h - (2 * Margen)) / (yMax - yMin)) * Zoom;

        var cx = (xMin + xMax) / 2;
        var cy = (yMin + yMax) / 2;

        // La Y del modelo sube y la del lienzo baja, así que se invierte
        Point APantallaPlanta(double x, double y) => new(
            (w / 2) + ((x - cx) * escala) + PanX,
            (h / 2) - ((y - cy) * escala) + PanY);

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

    private List<ElementoEtabs> Elementos() =>
        Modelo is null
            ? new List<ElementoEtabs>()
            : Modelo.Elementos.Where(el => Visible(el.Clase)).ToList();

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
