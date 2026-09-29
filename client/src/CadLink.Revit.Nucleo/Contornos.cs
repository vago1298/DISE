using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>Arreglos del contorno de un pano antes de mandarlo a Revit.</summary>
public static class Contornos
{
    /// <summary>Cuanto puede desviarse un vertice del plano medio sin avisar. 1 cm.</summary>
    public const double ToleranciaPlanoM = 0.01;

    /// <summary>
    /// Deja el contorno de una losa PLANO y paralelo al plano XY.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Floor.Create</c> lo exige, y cuando no se cumple contesta esto:
    /// </para>
    /// <para>
    /// <i>"The input curve loops cannot compose a valid boundary... each curve loop is not
    /// planar; or each curve loop is not in a plane parallel to the horizontal(XY) plane"</i>
    /// </para>
    /// <para>
    /// Y un contorno que viene de ETABS casi nunca cumple: los vertices salen de una malla y
    /// dos de ellos pueden diferir unos milimetros en Z, o el paño puede tener una pendiente
    /// de desague. Para Revit, unos milimetros ya no es plano.
    /// </para>
    /// <para>
    /// Se aplanan todos los vertices a UNA cota. Se toma la <b>mas alta</b> y no la media,
    /// porque el contorno de una losa es su cara superior: con la media, una losa con
    /// pendiente quedaria medio espesor por debajo de donde la puso el calculista.
    /// </para>
    /// <para>
    /// Si la desviacion es de verdad grande -una rampa- se aplana igual y se avisa: es
    /// preferible una losa horizontal que se corrige a mano, a ninguna losa. Revit no puede
    /// hacer un suelo inclinado con esta llamada.
    /// </para>
    /// </remarks>
    public static (List<PuntoJson> Contorno, double ZM, double DesviacionM) AHorizontal(
        IReadOnlyList<PuntoJson>? v)
    {
        if (v is null || v.Count == 0)
        {
            return (new List<PuntoJson>(), 0, 0);
        }

        var zMax = v.Max(p => p.Z);
        var zMin = v.Min(p => p.Z);

        var plano = v
            .Select(p => new PuntoJson { X = p.X, Y = p.Y, Z = zMax })
            .ToList();

        return (plano, zMax, zMax - zMin);
    }

    /// <summary>Quita vertices repetidos seguidos, y el ultimo si repite al primero.</summary>
    /// <remarks>
    /// Un lado de largo cero hace que Revit rechace el contorno entero, y al aplanar una losa
    /// aparecen nuevos repetidos: dos vertices que solo se distinguian en Z pasan a ser el
    /// mismo punto.
    /// </remarks>
    public static List<PuntoJson> SinRepetidos(
        IReadOnlyList<PuntoJson>? v, double tolM = 0.001)
    {
        var salida = new List<PuntoJson>();

        if (v is null)
        {
            return salida;
        }

        foreach (var p in v)
        {
            if (salida.Count > 0 && Cerca(salida[^1], p, tolM))
            {
                continue;
            }

            salida.Add(p);
        }

        while (salida.Count > 1 && Cerca(salida[0], salida[^1], tolM))
        {
            salida.RemoveAt(salida.Count - 1);
        }

        return salida;
    }

    private static bool Cerca(PuntoJson a, PuntoJson b, double tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol && Math.Abs(a.Z - b.Z) <= tol;

    /// <summary>El centro de un conjunto de puntos.</summary>
    public static PuntoJson Centro(IEnumerable<PuntoJson>? puntos)
    {
        var lista = (puntos ?? Enumerable.Empty<PuntoJson>()).ToList();

        if (lista.Count == 0)
        {
            return new PuntoJson();
        }

        return new PuntoJson
        {
            X = lista.Average(p => p.X),
            Y = lista.Average(p => p.Y),
            Z = lista.Average(p => p.Z)
        };
    }
}

/// <summary>
/// La etiqueta con que se identifica una pieza, incluso cuando el modelo no le puso ninguna.
/// </summary>
/// <remarks>
/// <para>
/// Hace falta porque el lector pone el <b>pier</b> como etiqueta de un muro, y un modelo sin
/// piers asignados deja TODOS los muros con la etiqueta vacia. Entonces la llave de todos
/// resulta la misma -<c>CadLink|Muro||Story1</c>- y el planificador, que se defiende de las
/// llaves repetidas, modela uno y descarta el resto con un aviso por cada uno.
/// </para>
/// <para>
/// El sintoma es inconfundible: decenas de avisos identicos con la etiqueta vacia entre las
/// dos barras, y muchos menos muros de los que tiene el modelo.
/// </para>
/// </remarks>
public static class Etiquetas
{
    /// <summary>
    /// La etiqueta del modelo, o una derivada de DONDE esta la pieza si no tiene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La derivada sale de las coordenadas del centro, redondeadas a centimetro. Se eligio eso
    /// y no un contador por dos razones:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///   es <b>estable entre exportaciones</b>: la misma pieza da la misma etiqueta aunque el
    ///   modelo se reordene, y eso es lo que permite volver a importar y ACTUALIZAR en vez de
    ///   duplicar;
    ///   </item>
    ///   <item>
    ///   y es <b>unica</b> mientras dos piezas no tengan el mismo centro, que en un modelo
    ///   sano no pasa.
    ///   </item>
    /// </list>
    /// <para>
    /// A centimetro y no a milimetro para que un reajuste minimo del modelo no cambie la
    /// etiqueta y convierta una actualizacion en un duplicado.
    /// </para>
    /// </remarks>
    public static string Estable(string? etiqueta, IEnumerable<PuntoJson>? puntos)
    {
        var e = (etiqueta ?? string.Empty).Trim();

        if (e.Length > 0)
        {
            return e;
        }

        var c = Contornos.Centro(puntos);

        return "@" + Cm(c.X) + "," + Cm(c.Y) + "," + Cm(c.Z);
    }

    private static string Cm(double metros) =>
        Math.Round(metros * 100, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
}
