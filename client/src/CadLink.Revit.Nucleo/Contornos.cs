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

    /// <summary>
    /// Quita los vertices que caen EN MEDIO de un lado recto: los que deja la malla de ETABS
    /// donde otro paño toca a este.
    /// </summary>
    /// <remarks>
    /// El muro junto a una puerta trae un vertice de mas a la altura del dintel, en su canto:
    /// ahi se une con el paño del dintel. Con cinco vertices ya no se reconocia como
    /// rectangulo, asi que no se llevaba al paño de sus columnas -se metia en ellas- ni se
    /// bajaba bajo su cadena.
    /// </remarks>
    public static List<PuntoJson> SinColineales(IReadOnlyList<PuntoJson>? v, double tolM = 0.005)
    {
        var lista = SinRepetidos(v, 0.001);
        var quitado = true;

        while (quitado && lista.Count > 3)
        {
            quitado = false;

            for (var i = 0; i < lista.Count && lista.Count > 3; i++)
            {
                var a = lista[(i - 1 + lista.Count) % lista.Count];
                var p = lista[i];
                var b = lista[(i + 1) % lista.Count];

                var (ux, uy, uz) = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                var (wx, wy, wz) = (p.X - a.X, p.Y - a.Y, p.Z - a.Z);
                var l2 = (ux * ux) + (uy * uy) + (uz * uz);

                if (l2 < 1e-12)
                {
                    continue;
                }

                // Que este sobre la recta de sus vecinos Y entre ellos.
                var t = ((wx * ux) + (wy * uy) + (wz * uz)) / l2;
                var (cx, cy, cz) = ((wy * uz) - (wz * uy), (wz * ux) - (wx * uz), (wx * uy) - (wy * ux));
                var distancia = Math.Sqrt(((cx * cx) + (cy * cy) + (cz * cz)) / l2);

                if (t > 0 && t < 1 && distancia <= tolM)
                {
                    lista.RemoveAt(i);
                    quitado = true;
                    i--;
                }
            }
        }

        return lista;
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

        return Donde(puntos);
    }

    /// <summary>
    /// La etiqueta del modelo <b>mas</b> donde esta la pieza, siempre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la que hay que usar en los <b>paños</b>, y la razon es que en un muro la etiqueta que
    /// trae el modelo es su <b>pier</b>, y un pier NO identifica un paño: identifica un grupo
    /// de paños. Un muro de planta baja mallado en seis trozos, todos con pier <c>P1</c>, daba
    /// seis veces la misma llave <c>CadLink|Muro|P1|Story1</c>, y el planificador se quedaba con
    /// uno y descartaba cinco.
    /// </para>
    /// <para>
    /// El sintoma era exactamente el que se reporto: la planta baja -que es donde el muro suele
    /// venir mallado en mas trozos porque tiene mas huecos- se quedaba sin nada, mientras que
    /// columnas y trabes, que en ETABS si tienen etiqueta unica por pieza, salian bien.
    /// </para>
    /// <para>
    /// <see cref="Estable"/> no bastaba: solo sustituia la etiqueta cuando estaba VACIA, asi
    /// que arreglaba el modelo sin piers y no tocaba el que si los tiene, que es este.
    /// </para>
    /// </remarks>
    public static string Unica(string? etiqueta, IEnumerable<PuntoJson>? puntos)
    {
        var e = (etiqueta ?? string.Empty).Trim();

        // El pier se conserva delante porque es lo que hace la marca legible para una persona
        // que la lee en los comentarios de Revit: «P1@1234,-560,300» dice el pier Y el sitio.
        return e.Length > 0 ? e + Donde(puntos) : Donde(puntos);
    }

    /// <summary>Donde esta la pieza, redondeado a centimetro: <c>@x,y,z</c>.</summary>
    public static string Donde(IEnumerable<PuntoJson>? puntos)
    {
        var c = Contornos.Centro(puntos);

        return "@" + Cm(c.X) + "," + Cm(c.Y) + "," + Cm(c.Z);
    }

    private static string Cm(double metros) =>
        Math.Round(metros * 100, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
}
