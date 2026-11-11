using System.Globalization;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  EL MODELO DE UNA CONEXION DE ACERO, EN SUS EJES LOCALES Y EN METROS
//
//  Una conexion leida del libro de Excel se convierte en piezas -placas, tornillos, cordones de
//  soldadura- en unos ejes propios de la conexion. De ese mismo modelo salen:
//    - el detalle de AutoCAD: cada vista es una proyeccion de las piezas;
//    - el modelo de Revit: cada pieza es un solido, puesto en los ejes de la viga, la columna o
//      la diagonal que el usuario elige.
//  Asi el dibujo y el modelo no pueden decir cosas distintas.
//
//  Los ejes dependen de donde va la conexion (ColocacionConexion):
//    ExtremoDeViga      X = de lado, Y = arriba, Z = a lo largo de la viga hacia dentro de ella.
//                       Z = 0 es la cara del apoyo (columna) o el extremo de la viga, segun
//                       ZCeroEnLaColumna. El origen va en el centro del perfil.
//    CentroDeViga       X = a lo largo de la viga, Y = arriba, Z = de lado. Origen en el eje de
//                       la viga a la mitad del claro.
//    ExtremoDeDiagonal  X = horizontal, de la columna hacia la diagonal; Y = vertical, de la viga
//                       hacia la diagonal; Z = de lado. Origen en el punto de trabajo (cruce de
//                       los ejes de viga y columna).
//    CentroDeDiagonal   X = horizontal en el plano del marco, Y = arriba, Z = de lado. Origen en
//                       el cruce de las diagonales.
//    BaseDeColumna      la placa base: va por su propio camino (PlacaBaseJson).
// ============================================================================

/// <summary>Donde se pone la conexion en el modelo.</summary>
public enum ColocacionConexion
{
    ExtremoDeViga,
    CentroDeViga,
    ExtremoDeDiagonal,
    CentroDeDiagonal,
    BaseDeColumna
}

/// <summary>Seccion de un miembro de referencia.</summary>
public enum FormaMiembro
{
    /// <summary>IR (W): D = peralte, B = ancho del patin, Tf, Tw.</summary>
    I,

    /// <summary>OR (HSS) rectangular: D = peralte, B = ancho, Tw = pared.</summary>
    Cajon
}

/// <summary>
/// Una placa: su contorno en el plano (U, V) desde <see cref="Origen"/> y su espesor hacia
/// <c>U × V</c>.
/// </summary>
/// <param name="SoloDibujo">Se dibuja en AutoCAD pero no se modela en Revit (parte de la viga misma).</param>
public sealed record PlacaPieza(
    string Que, string Rotulo, V3 Origen, V3 U, V3 V, IReadOnlyList<(double U, double V)> Contorno,
    double Espesor, bool SoloDibujo = false)
{
    public V3 N => U.Cruz(V).Unitario();

    public V3 Punto(double u, double v, double n = 0) => Origen + (U * u) + (V * v) + (N * n);

    /// <summary>Las esquinas de las dos caras.</summary>
    public IEnumerable<V3> Vertices()
    {
        foreach (var (u, v) in Contorno)
        {
            yield return Punto(u, v);
            yield return Punto(u, v, Espesor);
        }
    }
}

/// <summary>Un tornillo o un ancla: de <see cref="Desde"/> a lo largo de <see cref="Eje"/> (unitario).</summary>
public sealed record TornilloPieza(string Que, string Rotulo, V3 Desde, V3 Eje, double Largo, double Diametro)
{
    public V3 Hasta => Desde + (Eje * Largo);
}

/// <summary>
/// Un cordon de soldadura recto de <see cref="P0"/> a <see cref="P1"/>. Su seccion es el
/// triangulo P0, P0 + Lado·Pata1, P0 + Lado·Pata2: las dos patas van sobre las dos piezas que une.
/// </summary>
public sealed record CordonPieza(
    string Que, string Rotulo, V3 P0, V3 P1, V3 Pata1, V3 Pata2, double Lado, bool Penetracion)
{
    public IEnumerable<V3> Vertices()
    {
        yield return P0;
        yield return P0 + (Pata1 * Lado);
        yield return P0 + (Pata2 * Lado);
        yield return P1;
        yield return P1 + (Pata1 * Lado);
        yield return P1 + (Pata2 * Lado);
    }
}

/// <summary>
/// Un miembro de referencia -la viga, la columna, la diagonal-: se dibuja en AutoCAD para que el
/// detalle se entienda, pero en Revit ya esta modelado y no se toca.
/// </summary>
/// <param name="Peralte">Unitario, hacia donde mide <see cref="D"/>.</param>
public sealed record MiembroPieza(
    string Que, string Nombre, FormaMiembro Forma, V3 P0, V3 P1, V3 Peralte, double D, double B, double Tf, double Tw);

/// <summary>Una linea que solo se dibuja: el recorte RBS, el despatinado.</summary>
public sealed record TrazoPieza(string Que, IReadOnlyList<V3> Puntos, bool Cerrado, bool Oculto);

/// <summary>Una cota de <see cref="A"/> a <see cref="B"/>, desplazada hacia <see cref="Lado"/>.</summary>
public sealed record CotaPieza(V3 A, V3 B, V3 Lado, string Texto);

/// <summary>Una vista del detalle: hacia donde quedan la derecha y el arriba del papel.</summary>
public sealed record VistaPieza(string Titulo, V3 Derecha, V3 Arriba);

/// <summary>Una conexion lista para dibujar o modelar.</summary>
public sealed class ModeloConexion
{
    public string Hoja { get; init; } = string.Empty;

    public string Codigo { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    /// <summary>«Unión viga-columna con placas soldadas».</summary>
    public string Titulo { get; set; } = string.Empty;

    public ColocacionConexion Colocacion { get; set; }

    /// <summary>
    /// ExtremoDeViga: Z = 0 es la cara de la COLUMNA (la viga empieza en <see cref="HolguraM"/>).
    /// Si es false, Z = 0 es el extremo de la viga (conexiones a otra viga).
    /// </summary>
    public bool ZCeroEnLaColumna { get; set; }

    /// <summary>Del paño de la columna al extremo de la viga, segun la hoja.</summary>
    public double HolguraM { get; set; }

    /// <summary>
    /// ExtremoDeViga: del centro del perfil al borde de arriba de la CARA del extremo. Es la mitad
    /// del peralte, o menos si la viga va despatinada. Con eso Revit encuentra el centro del perfil
    /// aunque la cara del extremo sea solo el alma que queda bajo el despatinado.
    /// </summary>
    public double CaraArribaM { get; set; }

    /// <summary>
    /// ExtremoDeDiagonal: el angulo de la diagonal medido desde la vertical, en grados, segun la
    /// hoja. Revit lo compara con el de la diagonal del modelo.
    /// </summary>
    public double AnguloDiagonalGrados { get; set; }

    public List<PlacaPieza> Placas { get; } = new();

    public List<TornilloPieza> Tornillos { get; } = new();

    public List<CordonPieza> Cordones { get; } = new();

    public List<MiembroPieza> Miembros { get; } = new();

    public List<TrazoPieza> Trazos { get; } = new();

    public List<CotaPieza> Cotas { get; } = new();

    public List<VistaPieza> Vistas { get; } = new();

    /// <summary>El cuadro de datos: los renglones del «RESUMEN PARA EL DIBUJANTE» de la hoja.</summary>
    public List<string> Renglones { get; } = new();

    /// <summary>Lo que hay que saber de esta conexion: datos que faltan, revisiones que no cumplen.</summary>
    public List<string> Avisos { get; } = new();

    /// <summary>La placa base, si la conexion es una (colocacion BaseDeColumna).</summary>
    public PlacaBaseJson? PlacaBase { get; set; }

    /// <summary>Lo que se modela en Revit: las piezas que no son solo de dibujo.</summary>
    public int PiezasDeRevit => Placas.Count(p => !p.SoloDibujo) + Tornillos.Count + Cordones.Count;
}

/// <summary>Medidas en texto, para los rotulos: centimetros y pulgadas como en el libro.</summary>
public static class TextoMedida
{
    /// <summary>12.5 → «12.5»; 30 → «30».</summary>
    public static string Cm(double cm) => cm.ToString(Math.Abs(cm - Math.Round(cm)) < 0.05 ? "0" : "0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// Un espesor en cm a pulgadas, redondeado a 1/16": 3.18 → «1 1/4"», 0.95 → «3/8"».
    /// </summary>
    public static string Pulgadas(double cm)
    {
        var dieciseisavos = (int)Math.Round(cm / 2.54 * 16);

        if (dieciseisavos <= 0)
        {
            return "0\"";
        }

        var enteros = dieciseisavos / 16;
        var resto = dieciseisavos % 16;
        var den = 16;

        while (resto > 0 && resto % 2 == 0)
        {
            resto /= 2;
            den /= 2;
        }

        var frac = resto == 0 ? string.Empty : $"{resto}/{den}";
        return (enteros, frac) switch
        {
            (0, _) => frac + "\"",
            (_, "") => enteros + "\"",
            _ => $"{enteros} {frac}\""
        };
    }

    /// <summary>«13 mm (1/2")».</summary>
    public static string MmYPulgadas(double cm) =>
        (cm * 10).ToString("0", CultureInfo.InvariantCulture) + " mm (" + Pulgadas(cm) + ")";
}
