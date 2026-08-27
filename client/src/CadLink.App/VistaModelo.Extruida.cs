using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.Cad;
using CadLink.Etabs;

namespace CadLink.App;

/// <summary>
/// Vista <b>extruida</b> del modelo: cada elemento con su volumen y sombreado.
/// </summary>
/// <remarks>
/// <para>
/// La vista de alambre sirve para ver la traza, pero no para reconocer el edificio:
/// una columna y una trabe se ven igual, dos líneas, y no se aprecia ni el peralte ni
/// el espesor de los muros. Esta vista dibuja cada barra como un <b>prisma</b> con su
/// sección real y cada muro o losa como un <b>panel con espesor</b>.
/// </para>
/// <para>
/// <b>Cómo se resuelve el 3D sin motor 3D.</b> No se usa <c>Viewport3D</c> a
/// propósito, por lo mismo que el resto de la vista: aquí manda el control del
/// grosor de línea y del color, y un <c>Viewport3D</c> obliga a mallas, materiales y
/// luces para acabar necesitando de todos modos el sombreado a mano.
/// </para>
/// <para>
/// El método es el del pintor, pero <b>cara por cara y no elemento por elemento</b>.
/// Todas las caras de todos los elementos van a una sola lista, se ordenan de lejos a
/// cerca y se pintan. Ordenar por elemento no basta: una trabe que atraviesa una
/// columna tiene caras delante y detrás de ella a la vez, y con un único orden por
/// elemento una de las dos queda mal siempre.
/// </para>
/// <para>
/// El sombreado es lambertiano sencillo: el brillo de cada cara sale del ángulo entre
/// su normal y una luz fija. Es lo que hace que un prisma se lea como un volumen y no
/// como una silueta plana.
/// </para>
/// </remarks>
public sealed partial class VistaModelo
{
    /// <summary>Dirección de la luz, en coordenadas del modelo. Desde arriba y de lado.</summary>
    private static readonly (double X, double Y, double Z) Luz = Normalizar((-0.4, -0.5, 0.77));

    /// <summary>Brillo mínimo, para que la cara en sombra no salga negra.</summary>
    private const double BrilloMin = 0.42;

    /// <summary>Grosor con que se dibuja la arista de cada cara.</summary>
    private const double GrosorArista = 0.6;

    /// <summary>Una cara ya proyectada, lista para pintar.</summary>
    private sealed class Cara
    {
        public required Point[] Pantalla { get; init; }

        /// <summary>
        /// La profundidad de CADA vértice, no la media de la cara.
        /// </summary>
        /// <remarks>
        /// Es el cambio de fondo: con una sola profundidad por cara solo se puede ORDENAR, y
        /// ordenar no resuelve dos caras que se atraviesan. Con la de cada vértice se puede
        /// interpolar por píxel, que es lo que hace que la intersección salga exacta.
        /// </remarks>
        public required double[] Prof { get; init; }

        public required int Relleno { get; init; }

        public required int Borde { get; init; }

        public string? Info { get; init; }
    }

    public void DibujarExtruido(Canvas lienzo)
    {
        lienzo.Children.Clear();

        var elementos = Elementos(conCorte: true);

        if (elementos.Count == 0)
        {
            Aviso(lienzo, Modelo is null
                ? "Lee el modelo de ETABS para verlo aquí."
                : CorteEje.Length > 0
                    ? $"El corte por el eje {CorteEje} no toca ningún elemento. Prueba con " +
                      "otro eje o con un espesor de corte mayor."
                    : "El modelo no trae elementos que mostrar.");
            return;
        }

        if (PrepararCamara(lienzo, elementos) is not Camara cam)
        {
            return;
        }

        var caras = new List<Cara>();

        foreach (var el in elementos)
        {
            var color = ColorBase(el.Clase);

            foreach (var cara in CarasDe(el))
            {
                if (cara.Count < 3)
                {
                    continue;
                }

                var brillo = Brillo(cara);

                caras.Add(new Cara
                {
                    Pantalla = cara.Select(p => cam.APantalla(p.X, p.Y, p.Z)).ToArray(),

                    // La profundidad de CADA vértice: es lo que permite decidir por píxel
                    // quién está delante en lugar de ordenar caras enteras.
                    Prof = cara.Select(p => cam.Prof(p.X, p.Y)).ToArray(),

                    Relleno = Argb(color, brillo),
                    Borde = Argb(color, brillo * 0.62),
                    Info = Etiqueta(el)
                });
            }
        }

        if (caras.Count == 0)
        {
            Aviso(lienzo,
                "El modelo no trae dimensiones de sección, así que no hay volumen que " +
                "extruir. Usa la vista 3D de alambre.");
            return;
        }

        // ==============================================================================
        //  SE PINTA CON Z-BUFFER, NO ORDENANDO CARAS
        // ==============================================================================
        //  Aquí estaba la losa cortada. Ordenar las caras por su profundidad MEDIA y pintarlas
        //  de lejos a cerca —el algoritmo del pintor— falla siempre en el mismo caso: cuando
        //  dos caras se ATRAVIESAN. Una losa y un muro que la cruza no tienen orden correcto,
        //  porque cada uno está delante en una parte; el pintor tiene que elegir uno entero, y
        //  de ahí que la losa se viera cortada por el muro o el muro pasándole por encima.
        //
        //  No era el motor de dibujo: era el método. Con Z-buffer se guarda la profundidad de
        //  CADA PÍXEL y se pinta solo lo que está más cerca, así que la intersección sale
        //  exacta y no hay nada que ordenar.
        var lienzoZ = new RasterZ((int)Math.Ceiling(cam.W), (int)Math.Ceiling(cam.H));

        lienzoZ.Limpiar(ArgbDe(FondoDeLaExtruida));

        foreach (var cara in caras)
        {
            // Cada cara se parte en triángulos —abanico desde el primer vértice—, que es lo
            // que sabe pintar un rasterizador. Las caras de un prisma son planas y convexas,
            // así que el abanico las cubre exactamente.
            for (var i = 1; i + 1 < cara.Pantalla.Length; i++)
            {
                lienzoZ.Triangulo(
                    cara.Pantalla[0].X, cara.Pantalla[0].Y, cara.Prof[0],
                    cara.Pantalla[i].X, cara.Pantalla[i].Y, cara.Prof[i],
                    cara.Pantalla[i + 1].X, cara.Pantalla[i + 1].Y, cara.Prof[i + 1],
                    cara.Relleno);
            }

            // Y sus aristas, que es lo que deja ver la forma de cada pieza. Con el sesgo del
            // rasterizador quedan justo delante de su propia cara.
            for (var i = 0; i < cara.Pantalla.Length; i++)
            {
                var j = (i + 1) % cara.Pantalla.Length;

                lienzoZ.Linea(
                    cara.Pantalla[i].X, cara.Pantalla[i].Y, cara.Prof[i],
                    cara.Pantalla[j].X, cara.Pantalla[j].Y, cara.Prof[j],
                    cara.Borde);
            }
        }

        MostrarRaster(lienzo, lienzoZ);

        DibujarTerna(lienzo, cam.W, cam.H, cam.Sa, cam.Ca, cam.Se, cam.Ce);
        Leyenda(lienzo, elementos.Count);
    }

    // ==================================================================
    // Caras de cada tipo de elemento
    // ==================================================================

    private static IEnumerable<List<(double X, double Y, double Z)>> CarasDe(ElementoEtabs el)
    {
        return el.Vertices3D.Count >= 3 ? CarasDePanel(el) : CarasDeBarra(el);
    }

    /// <summary>
    /// Las seis caras del prisma de una barra: columna, trabe o diagonal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El prisma se construye sobre un triedro local: el eje de la barra, y dos
    /// direcciones perpendiculares a él. Para elegirlas se toma la vertical como
    /// referencia, <b>salvo cuando la barra ya es vertical</b>: en una columna, la
    /// vertical y el eje coinciden y su producto vectorial sale cero, así que el
    /// triedro degeneraría y la columna se dibujaría aplastada. En ese caso la
    /// referencia pasa a ser el eje Y.
    /// </para>
    /// <para>
    /// El ancho va en la primera perpendicular y el peralte en la segunda. Si la
    /// sección no trae medidas se usa una mínima, para que el elemento se vea: es
    /// mejor un prisma delgado que un elemento que desaparece del dibujo sin avisar.
    /// </para>
    /// </remarks>
    private static IEnumerable<List<(double X, double Y, double Z)>> CarasDeBarra(
        ElementoEtabs el)
    {
        var eje = (el.X2 - el.X1, el.Y2 - el.Y1, el.Z2 - el.Z1);
        var largo = Norma(eje);

        if (largo < 1e-9)
        {
            yield break;
        }

        eje = Escalar(eje, 1 / largo);

        // Referencia para el triedro: la vertical, o el eje Y si la barra es vertical
        var arriba = Math.Abs(eje.Item3) > 0.99 ? (0d, 1d, 0d) : (0d, 0d, 1d);

        var n1 = Cruz(eje, arriba);
        var l1 = Norma(n1);

        if (l1 < 1e-9)
        {
            yield break;
        }

        n1 = Escalar(n1, 1 / l1);
        var n2 = Normalizar(Cruz(eje, n1));

        // ==============================================================================
        //  EL GIRO DE LOS EJES LOCALES, QUE TAMBIÉN FALTABA AQUÍ
        // ==============================================================================
        //  El triedro de arriba sale solo de la geometría del eje, así que todas las
        //  columnas quedaban alineadas con la X y la Y globales: una columna de 20×60
        //  girada 90° se veía de 20×60 derecha, igual que pasaba en la planta.
        //
        //  El ángulo de GetLocalAxes es un giro de los ejes 2 y 3 ALREDEDOR DEL EJE 1, o
        //  sea alrededor del eje de la barra, así que se aplica aquí a las dos
        //  perpendiculares y el prisma sale orientado como en el modelo.
        if (Math.Abs(el.AnguloGrados) > 1e-9)
        {
            var a = el.AnguloGrados * Math.PI / 180;
            var ca = Math.Cos(a);
            var sa = Math.Sin(a);

            var g1 = (
                (n1.Item1 * ca) + (n2.Item1 * sa),
                (n1.Item2 * ca) + (n2.Item2 * sa),
                (n1.Item3 * ca) + (n2.Item3 * sa));

            var g2 = (
                (n2.Item1 * ca) - (n1.Item1 * sa),
                (n2.Item2 * ca) - (n1.Item2 * sa),
                (n2.Item3 * ca) - (n1.Item3 * sa));

            n1 = g1;
            n2 = g2;
        }

        var b = el.AnchoM > 0.01 ? el.AnchoM : 0.12;
        var d = el.PeralteM > 0.01 ? el.PeralteM : 0.12;

        var e1 = Escalar(n1, b / 2);
        var e2 = Escalar(n2, d / 2);

        (double X, double Y, double Z) Esquina(bool fin, int s1, int s2)
        {
            var bx = fin ? el.X2 : el.X1;
            var by = fin ? el.Y2 : el.Y1;

            // ==========================================================================
            //  EL PUNTO DE INSERCIÓN, QUE AQUÍ SÍ SE VE
            // ==========================================================================
            //  Se pidió: «las trabes las inserta en su punto céntrico y está mal, debe ser top
            //  center para que el paño coincida con el de la losa, así como en ETABS».
            //
            //  El punto cardinal de una trabe es casi siempre el 8 —arriba al centro—, y eso
            //  significa que la CARA DE ARRIBA de la trabe va a la cota de la línea, no su
            //  centro: la trabe cuelga por debajo del piso. Dibujándola centrada, medio peralte
            //  quedaba POR ENCIMA de la losa, y en la vista extruida se veía la trabe montada
            //  sobre el piso en lugar de colgada de él.
            //
            //  El movimiento en planta ya viene aplicado en las coordenadas —lo hace el lector—;
            //  la Z se guarda aparte a propósito, porque de la elevación depende el nivel al que
            //  se reparte la pieza, y aquí es donde toca usarla.
            var bz = fin ? el.Z2 + el.MovidoZJ : el.Z1 + el.MovidoZI;

            return (bx + (s1 * e1.Item1) + (s2 * e2.Item1),
                    by + (s1 * e1.Item2) + (s2 * e2.Item2),
                    bz + (s1 * e1.Item3) + (s2 * e2.Item3));
        }

        // Los cuatro signos recorren el contorno de la sección EN ORDEN, no en
        // diagonal: con el orden equivocado las caras salen en aspa.
        var vueltas = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };

        // Tapas
        yield return vueltas.Select(s => Esquina(false, s.Item1, s.Item2)).ToList();
        yield return vueltas.Select(s => Esquina(true, s.Item1, s.Item2)).ToList();

        // Costados
        for (var i = 0; i < 4; i++)
        {
            var a = vueltas[i];
            var c = vueltas[(i + 1) % 4];

            yield return new List<(double X, double Y, double Z)>
            {
                Esquina(false, a.Item1, a.Item2),
                Esquina(false, c.Item1, c.Item2),
                Esquina(true, c.Item1, c.Item2),
                Esquina(true, a.Item1, a.Item2)
            };
        }
    }

    /// <summary>
    /// El panel de un muro o una losa, extruido su espesor.
    /// </summary>
    /// <remarks>
    /// Se desplaza el contorno media espesor a cada lado de su propio plano y se
    /// cierran los costados. Si el espesor no viene en el modelo se dibuja el panel
    /// plano, sin volumen, en lugar de inventarse una medida: un muro de 30 cm y otro
    /// de 15 tienen que verse distintos, y si el dato no está es mejor que se note.
    /// </remarks>
    private static IEnumerable<List<(double X, double Y, double Z)>> CarasDePanel(
        ElementoEtabs el)
    {
        var v = el.Vertices3D.Select(p => (p.X, p.Y, p.Z)).ToList();

        var normal = NormalDe(v);

        // ==============================================================================
        //  EL MURO SIEMPRE CON ESPESOR, AUNQUE EL MODELO NO LO DIGA
        // ==============================================================================
        //  Se pidió, y era un caso real y frecuente: cuando GetWall no devuelve el espesor
        //  —pasa con las propiedades de mampostería— el muro se dibujaba PLANO, como una
        //  hoja de papel, y en la vista extruida eso es justo lo que no se quiere ver: la
        //  gracia de la extruida es entender el volumen.
        //
        //  Y el plano de AutoCAD sí lo dibuja con espesor, porque allá hay un respaldo de 15
        //  cm. Así que aquí se usa el MISMO respaldo: si las dos vistas del mismo muro no
        //  coinciden, una de las dos está mintiendo.
        var t = EspesorDePanel(el);

        if (t <= 0.01 || Norma(normal) < 1e-9)
        {
            yield return v;
            yield break;
        }

        var mitad = Escalar(Normalizar(normal), t / 2);

        var cara1 = v.Select(p => Sumar(p, mitad)).ToList();
        var cara2 = v.Select(p => Restar(p, mitad)).ToList();

        yield return cara1;
        yield return cara2;

        for (var i = 0; i < v.Count; i++)
        {
            var j = (i + 1) % v.Count;

            yield return new List<(double X, double Y, double Z)>
            {
                cara1[i], cara1[j], cara2[j], cara2[i]
            };
        }
    }

    // ==================================================================
    // Sombreado
    // ==================================================================

    /// <summary>Brillo de una cara, entre <see cref="BrilloMin"/> y 1.</summary>
    /// <remarks>
    /// Se usa el <b>valor absoluto</b> del producto con la luz a propósito. Las caras
    /// se generan sin cuidar si su normal apunta hacia fuera o hacia dentro del
    /// sólido, y sin el absoluto la mitad de ellas saldrían en sombra total: un
    /// prisma se vería con dos costados negros.
    /// </remarks>
    private static double Brillo(List<(double X, double Y, double Z)> cara)
    {
        var n = NormalDe(cara);

        if (Norma(n) < 1e-12)
        {
            return 1;
        }

        n = Normalizar(n);
        var cos = Math.Abs((n.Item1 * Luz.X) + (n.Item2 * Luz.Y) + (n.Item3 * Luz.Z));

        return BrilloMin + ((1 - BrilloMin) * Math.Clamp(cos, 0, 1));
    }

    /// <summary>El color de fondo de la vista extruida.</summary>
    /// <remarks>
    /// Con Z-buffer el fondo hay que pintarlo: antes lo ponía el propio lienzo y ahora la
    /// imagen lo tapa entera. Se toma el mismo tono claro de las tarjetas para que la vista no
    /// cambie de aspecto.
    /// </remarks>
    private static readonly Color FondoDeLaExtruida = Color.FromRgb(0xF7, 0xF9, 0xFB);

    /// <summary>El color, ya sombreado, como entero <c>0xAARRGGBB</c>.</summary>
    private static int Argb(Color c, double brillo)
    {
        byte Canal(byte v) => (byte)Math.Clamp(Math.Round(v * brillo), 0, 255);

        return ArgbDe(Color.FromRgb(Canal(c.R), Canal(c.G), Canal(c.B)));
    }

    private static int ArgbDe(Color c) =>
        (0xFF << 24) | (c.R << 16) | (c.G << 8) | c.B;

    /// <summary>
    /// Vuelca el buffer del rasterizador al lienzo, como una <b>imagen</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un solo objeto en el lienzo en lugar de miles de polígonos, así que además de salir
    /// bien, sale más rápido: WPF ya no tiene que medir, recortar y ordenar cada cara.
    /// </para>
    /// <para>
    /// <c>Bgra32</c> es el formato en que un <c>int</c> de <c>0xAARRGGBB</c> se copia tal cual
    /// en memoria en una máquina little-endian, que son todas las que corren Windows: sin
    /// conversión y sin recorrer los píxeles otra vez.
    /// </para>
    /// </remarks>
    private static void MostrarRaster(Canvas lienzo, RasterZ raster)
    {
        var mapa = new System.Windows.Media.Imaging.WriteableBitmap(
            raster.Ancho, raster.Alto, 96, 96, PixelFormats.Bgra32, null);

        mapa.WritePixels(
            new Int32Rect(0, 0, raster.Ancho, raster.Alto),
            raster.Pixeles, raster.Ancho * 4, 0);

        var img = new Image
        {
            Source = mapa,
            Width = raster.Ancho,
            Height = raster.Alto,

            // Sin suavizado: cada píxel del buffer es un píxel de la pantalla, que es lo que
            // deja las aristas limpias en lugar de emborronadas.
            SnapsToDevicePixels = true
        };

        RenderOptions.SetBitmapScalingMode(
            img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);

        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        lienzo.Children.Add(img);
    }

    private static Brush Sombra(Color c, double brillo)
    {
        byte Canal(byte v) => (byte)Math.Clamp(Math.Round(v * brillo), 0, 255);

        return new SolidColorBrush(Color.FromRgb(Canal(c.R), Canal(c.G), Canal(c.B)));
    }

    private static Color ColorBase(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => Color.FromRgb(0x2E, 0x86, 0xC1),
        ClaseElemento.Trabe => Color.FromRgb(0x28, 0xA7, 0x45),
        ClaseElemento.Diagonal => Color.FromRgb(0x9B, 0x59, 0xB6),
        ClaseElemento.Muro => Color.FromRgb(0x8A, 0x99, 0xA8),
        _ => Color.FromRgb(0xC8, 0xD2, 0xD8)
    };

    // ==================================================================
    // Vectores
    // ==================================================================

    /// <summary>
    /// Normal del polígono, por la fórmula del área con signo de Newell.
    /// </summary>
    /// <remarks>
    /// Se usa Newell y no el producto vectorial de los tres primeros vértices porque
    /// esos tres pueden salir <b>casi alineados</b> —pasa a menudo en una losa con un
    /// vértice intermedio en un lado— y entonces la normal saldría diminuta y con la
    /// dirección dominada por el error de redondeo. Newell promedia todos los
    /// vértices y no tiene ese problema.
    /// </remarks>
    private static (double, double, double) NormalDe(List<(double X, double Y, double Z)> v)
    {
        double nx = 0, ny = 0, nz = 0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }

        return (nx, ny, nz);
    }

    private static double Norma((double, double, double) v) =>
        Math.Sqrt((v.Item1 * v.Item1) + (v.Item2 * v.Item2) + (v.Item3 * v.Item3));

    /// <summary>
    /// El espesor con el que se extruye un paño: el del modelo o el <b>respaldo</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los 15 cm del muro son los mismos que usa <c>PlantaDrawer</c> —su
    /// <c>ESPESOR_MURO_CM</c>— y los mismos que usa la vista en planta, para que las tres
    /// vistas del mismo muro digan lo mismo.
    /// </para>
    /// <para>
    /// La LOSA es otra cosa: su espesor manda en el armado y en el rótulo del plano, así que
    /// inventarlo sería peor que no dibujarlo. Una losa sin espesor se queda <b>plana</b>, y
    /// que se note.
    /// </para>
    /// </remarks>
    private static double EspesorDePanel(ElementoEtabs el)
    {
        if (el.AnchoM > 0.01)
        {
            return el.AnchoM;
        }

        // ==============================================================================
        //  Y SI EL MODELO NO DIO EL ESPESOR, TAMPOCO SE DIBUJA PLANA
        // ==============================================================================
        //  Se pidió el ancho real de las losas, y el real es el del modelo: eso es lo que se
        //  dibuja siempre que esté —y ahora llega también en las losas nervadas y
        //  reticulares, que no responden a GetSlab y por eso venían en cero—.
        //
        //  Cuando de verdad no está, una losa PLANA es lo peor de las tres opciones: en una
        //  vista extruida se lee como si la losa no tuviera espesor, que es imposible. Se
        //  dibuja con 10 cm, que es la losa más delgada que se construye, y el resumen del
        //  modelo dice cuántas salieron así para que se pueda corregir la propiedad en ETABS.
        //
        //  Es el mismo criterio del muro —15 cm— y el mismo de la planta: entre callar el
        //  dato y dibujar algo con sentido avisando, se dibuja y se avisa.
        return el.Clase == ClaseElemento.Muro ? 0.15 : 0.10;
    }

    private static (double X, double Y, double Z) Normalizar((double, double, double) v)
    {
        var n = Norma(v);
        return n < 1e-12 ? (0, 0, 0) : (v.Item1 / n, v.Item2 / n, v.Item3 / n);
    }

    private static (double, double, double) Escalar((double, double, double) v, double k) =>
        (v.Item1 * k, v.Item2 * k, v.Item3 * k);

    private static (double, double, double) Cruz(
        (double, double, double) a, (double, double, double) b) =>
        ((a.Item2 * b.Item3) - (a.Item3 * b.Item2),
         (a.Item3 * b.Item1) - (a.Item1 * b.Item3),
         (a.Item1 * b.Item2) - (a.Item2 * b.Item1));

    private static (double X, double Y, double Z) Sumar(
        (double X, double Y, double Z) a, (double, double, double) b) =>
        (a.X + b.Item1, a.Y + b.Item2, a.Z + b.Item3);

    private static (double X, double Y, double Z) Restar(
        (double X, double Y, double Z) a, (double, double, double) b) =>
        (a.X - b.Item1, a.Y - b.Item2, a.Z - b.Item3);
}
