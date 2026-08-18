using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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
        public required double Profundidad { get; init; }
        public required Brush Relleno { get; init; }
        public required Brush Borde { get; init; }
        public string? Info { get; init; }
    }

    public void DibujarExtruido(Canvas lienzo)
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

                    // La profundidad de la cara es la media de sus vértices. Con el
                    // vértice más lejano en su lugar, dos caras que comparten arista
                    // se ordenarían por un empate y parpadearían al girar.
                    Profundidad = cara.Average(p => cam.Prof(p.X, p.Y)),

                    Relleno = Sombra(color, brillo),
                    Borde = Sombra(color, brillo * 0.62),
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

        // De lejos a cerca: 'Prof' crece hacia el fondo, así que se pinta primero el
        // mayor. Al revés, el fondo taparía el frente y el edificio se vería del revés.
        foreach (var cara in caras.OrderByDescending(c => c.Profundidad))
        {
            var poly = new Polygon
            {
                Fill = cara.Relleno,
                Stroke = cara.Borde,
                StrokeThickness = GrosorArista,
                ToolTip = cara.Info
            };

            foreach (var p in cara.Pantalla)
            {
                poly.Points.Add(p);
            }

            lienzo.Children.Add(poly);
        }

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

        var b = el.AnchoM > 0.01 ? el.AnchoM : 0.12;
        var d = el.PeralteM > 0.01 ? el.PeralteM : 0.12;

        var e1 = Escalar(n1, b / 2);
        var e2 = Escalar(n2, d / 2);

        (double X, double Y, double Z) Esquina(bool fin, int s1, int s2)
        {
            var bx = fin ? el.X2 : el.X1;
            var by = fin ? el.Y2 : el.Y1;
            var bz = fin ? el.Z2 : el.Z1;

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
        var t = el.AnchoM;

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
