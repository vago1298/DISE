namespace CadLink.Revit.Nucleo;

/// <summary>Un eje de la cuadricula: el nombre que va en la burbuja y su coordenada.</summary>
public sealed class EjeJson
{
    public string Id { get; set; } = string.Empty;

    /// <summary>La coordenada, en metros. X en un eje vertical, Y en uno horizontal.</summary>
    public double Ordenada { get; set; }
}

/// <summary>La cuadricula de ejes del modelo.</summary>
public sealed class CuadriculaJson
{
    /// <summary>Los verticales: los de X, de izquierda a derecha.</summary>
    public List<EjeJson> X { get; set; } = new();

    /// <summary>Los horizontales: los de Y, de abajo arriba.</summary>
    public List<EjeJson> Y { get; set; } = new();

    public bool Hay => X.Count > 0 || Y.Count > 0;

    public int Cuantos => X.Count + Y.Count;
}

/// <summary>
/// Coloca los ejes como en el plano de AutoCAD: <b>los extremos a paño y el interior al eje</b>.
/// </summary>
/// <remarks>
/// <para>
/// Es el mismo criterio que el plano estructural, y el motivo es de dibujo: ETABS modela los
/// muros por su LINEA MEDIA, asi que el eje de fachada pasa por el centro del muro. En un plano
/// lo que se acota por fuera es la cara exterior, no el centro del muro, de modo que el eje
/// extremo se corre hacia fuera medio espesor y queda <b>a paño</b>. Los ejes interiores no se
/// mueven: esos si van por el eje de la pieza.
/// </para>
/// <para>
/// Se corre <b>solo el primero y el ultimo</b> de cada direccion, y por la mitad del ancho de
/// la pieza mas gruesa que corre a lo largo de ese eje, con esta preferencia: <b>muro, luego
/// trabe, luego columna</b>. Un muro manda sobre una trabe porque es lo que define el paño de
/// la fachada.
/// </para>
/// <para>
/// El algoritmo es el del plano -<c>EjesPlano.AlPanoExterior</c> en la capa de AutoCAD- pero
/// escrito sobre los datos que ve el complemento de Revit, que son <see cref="BarraJson"/> y
/// <see cref="PanoJson"/>. Se reescribio en vez de referenciar la capa de AutoCAD porque esa
/// capa habla de COM y de su propia configuracion, y el nucleo no puede depender de ninguna de
/// las dos si tiene que seguir siendo probable sin Revit y sin AutoCAD.
/// </para>
/// </remarks>
public static class Cuadriculas
{
    /// <summary>
    /// Cuanto puede desviarse una pieza del eje y seguir contando como que va sobre el.
    /// </summary>
    /// <remarks>
    /// 25 cm, el mismo valor que el plano. Es generoso a proposito: ETABS modela el muro por su
    /// linea media, asi que la pieza casi nunca esta exactamente sobre la linea del eje.
    /// </remarks>
    public const double ToleranciaPanoM = 0.25;

    /// <summary>Dos ejes a menos de esto son el mismo.</summary>
    public const double ToleranciaUnirM = 0.01;

    /// <summary>Espesor que se supone a un muro que no lo trae.</summary>
    public const double EspesorMuroPorOmisionM = 0.15;

    /// <summary>Ancho que se supone a una trabe que no lo trae.</summary>
    public const double AnchoTrabePorOmisionM = 0.20;

    /// <summary>Medidas que se suponen a una columna que no las trae.</summary>
    public const double LadoColumnaPorOmisionM = 0.15;

    /// <summary>Quita los ejes repetidos, quedandose con el primero de cada grupo.</summary>
    /// <remarks>
    /// El primero es el que trae el nombre bueno. Va ANTES de correr los extremos: si se
    /// corriera primero, el duplicado del eje extremo se quedaria sin correr y dejaria una
    /// linea suelta a medio espesor de distancia.
    /// </remarks>
    public static List<EjeJson> SinRepetidos(IEnumerable<EjeJson>? ejes, double tolM)
    {
        var salida = new List<EjeJson>();

        foreach (var e in ejes ?? Enumerable.Empty<EjeJson>())
        {
            if (tolM <= 0 || !salida.Any(y => Math.Abs(y.Ordenada - e.Ordenada) <= tolM))
            {
                salida.Add(new EjeJson { Id = e.Id, Ordenada = e.Ordenada });
            }
        }

        return salida;
    }

    /// <summary>
    /// Corre hacia fuera el primer y el ultimo eje, para que queden al paño.
    /// </summary>
    /// <param name="ejes">Los ejes de una direccion.</param>
    /// <param name="verticales">
    /// <c>true</c> para los de X. Decide sobre que direccion se mide el espesor.
    /// </param>
    /// <param name="modelo">De donde se saca el espesor de las piezas.</param>
    public static List<EjeJson> AlPanoExterior(
        IEnumerable<EjeJson>? ejes, bool verticales, ModeloJson? modelo)
    {
        var salida = SinRepetidos(ejes, 0);

        if (modelo is null || salida.Count < 2)
        {
            return salida;
        }

        // Por ORDENADA, no por posicion en la lista: la cuadricula puede llegar desordenada.
        var iMin = 0;
        var iMax = 0;

        for (var i = 1; i < salida.Count; i++)
        {
            if (salida[i].Ordenada < salida[iMin].Ordenada)
            {
                iMin = i;
            }

            if (salida[i].Ordenada > salida[iMax].Ordenada)
            {
                iMax = i;
            }
        }

        if (iMin == iMax)
        {
            return salida;
        }

        var medioMin = MedioAnchoSobreEje(salida[iMin].Ordenada, verticales, modelo);
        var medioMax = MedioAnchoSobreEje(salida[iMax].Ordenada, verticales, modelo);

        if (medioMin > 0)
        {
            salida[iMin].Ordenada -= medioMin;
        }

        if (medioMax > 0)
        {
            salida[iMax].Ordenada += medioMax;
        }

        return salida;
    }

    /// <summary>La cuadricula entera, ya colocada.</summary>
    public static CuadriculaJson Colocar(CuadriculaJson? cuadricula, ModeloJson? modelo)
    {
        if (cuadricula is null)
        {
            return new CuadriculaJson();
        }

        return new CuadriculaJson
        {
            X = AlPanoExterior(SinRepetidos(cuadricula.X, ToleranciaUnirM), true, modelo),
            Y = AlPanoExterior(SinRepetidos(cuadricula.Y, ToleranciaUnirM), false, modelo)
        };
    }

    /// <summary>
    /// Medio ancho de la pieza mas gruesa que corre a lo largo de este eje.
    /// </summary>
    private static double MedioAnchoSobreEje(double ordenada, bool vertical, ModeloJson modelo)
    {
        var deMuro = 0.0;
        var deTrabe = 0.0;
        var deApoyo = 0.0;

        // ---- Muros ----
        foreach (var p in modelo.Panos)
        {
            if (p.Clase != ClasePieza.Muro || p.Vertices.Count == 0)
            {
                continue;
            }

            // Todos sus vertices sobre el eje, y con desarrollo en la otra direccion. Asi un
            // muro que solo CRUZA el eje no cuenta: el paño lo define el que va a lo largo.
            var sobre = p.Vertices.All(v =>
                Math.Abs((vertical ? v.X : v.Y) - ordenada) <= ToleranciaPanoM);

            if (!sobre)
            {
                continue;
            }

            var otras = p.Vertices.Select(v => vertical ? v.Y : v.X).ToList();

            if (otras.Max() - otras.Min() <= ToleranciaPanoM)
            {
                continue;
            }

            var e = p.Seccion.EspesorM > 0 ? p.Seccion.EspesorM : EspesorMuroPorOmisionM;

            deMuro = Math.Max(deMuro, e);
        }

        // ---- Barras ----
        foreach (var b in modelo.Barras)
        {
            var a1 = vertical ? b.P1.X : b.P1.Y;
            var a2 = vertical ? b.P2.X : b.P2.Y;

            if (b.Clase == ClasePieza.Columna)
            {
                // A una columna solo se le pide que su centro caiga sobre el eje.
                if (Math.Abs(a1 - ordenada) > ToleranciaPanoM)
                {
                    continue;
                }

                deApoyo = Math.Max(deApoyo, MedioDeApoyo(b, vertical));

                continue;
            }

            if (b.Clase != ClasePieza.Trabe)
            {
                continue;
            }

            var o1 = vertical ? b.P1.Y : b.P1.X;
            var o2 = vertical ? b.P2.Y : b.P2.X;

            if (Math.Abs(a1 - ordenada) > ToleranciaPanoM
                || Math.Abs(a2 - ordenada) > ToleranciaPanoM
                || Math.Abs(o2 - o1) <= ToleranciaPanoM)
            {
                continue;
            }

            var an = b.Seccion.AnchoM > 0 ? b.Seccion.AnchoM : AnchoTrabePorOmisionM;

            deTrabe = Math.Max(deTrabe, an);
        }

        // Muro, luego trabe, luego apoyo. Los dos primeros vienen como ANCHO y se parten;
        // MedioDeApoyo ya devuelve la mitad.
        if (deMuro > 0)
        {
            return deMuro / 2;
        }

        return deTrabe > 0 ? deTrabe / 2 : deApoyo;
    }

    /// <summary>
    /// Medio ancho de una columna medido sobre la direccion que interesa, con su giro.
    /// </summary>
    /// <remarks>
    /// Es la caja de la seccion ya girada: una columna de 15x40 girada 90 grados ocupa 40 en la
    /// direccion en que sin girar ocupaba 15, y el paño lo marca lo que ocupa DE VERDAD.
    /// </remarks>
    private static double MedioDeApoyo(BarraJson b, bool vertical)
    {
        var an = b.Seccion.AnchoM > 0 ? b.Seccion.AnchoM : LadoColumnaPorOmisionM;
        var pe = b.Seccion.PeralteM > 0 ? b.Seccion.PeralteM : an;

        var g = b.AnguloGrados * Math.PI / 180.0;
        var c = Math.Abs(Math.Cos(g));
        var s = Math.Abs(Math.Sin(g));

        return vertical
            ? (an / 2 * c) + (pe / 2 * s)
            : (an / 2 * s) + (pe / 2 * c);
    }
}
