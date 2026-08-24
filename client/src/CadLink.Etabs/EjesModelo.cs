namespace CadLink.Etabs;

/// <summary>
/// La <b>cuadrícula de ejes</b> del modelo: los que llevan burbuja y cota en el plano.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>LeerEjes</c> y de su respaldo <c>EjesDesdeGeometria</c>. La macro pide primero
/// la cuadrícula al programa —<c>GridSys.GetGridSys_2</c>— y, si no la puede leer, <b>la
/// deduce de la geometría</b>: cada X distinta donde hay una columna o un muro es un eje
/// vertical, y cada Y distinta, uno horizontal.
/// </para>
/// <para>
/// Ese respaldo no es un adorno: <c>GetGridSys_2</c> no existe en todas las versiones de
/// ETABS —la propia macro lo advierte en un comentario— y en SAP2000 la cuadrícula puede
/// estar definida de otra forma. Con la deducción, el plano <b>siempre</b> sale con sus ejes
/// acotados, aunque sea con los nombres puestos por orden.
/// </para>
/// </remarks>
public sealed class EjesModelo
{
    /// <summary>Un eje: su nombre —el que va en la burbuja— y su coordenada.</summary>
    public sealed record Eje(string Id, double Ordenada);

    /// <summary>Los verticales, los de la cuadrícula en X, ordenados de izquierda a derecha.</summary>
    public List<Eje> X { get; } = new();

    /// <summary>Los horizontales, los de la cuadrícula en Y, de abajo arriba.</summary>
    public List<Eje> Y { get; } = new();

    /// <summary>Origen de la cuadrícula, en metros.</summary>
    public double OrigenX { get; set; }

    public double OrigenY { get; set; }

    /// <summary>Giro de la cuadrícula, en grados.</summary>
    public double RotacionGrados { get; set; }

    /// <summary>De dónde salieron: para decírselo al usuario en el resumen.</summary>
    public string Origen { get; set; } = string.Empty;

    public bool Hay => X.Count > 0 || Y.Count > 0;

    /// <summary>
    /// Deduce la cuadrícula de la <b>geometría</b>: es el <c>EjesDesdeGeometria</c> de la
    /// macro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se miran los extremos de las <b>columnas y los muros</b> —no de las trabes ni de las
    /// losas—, porque un eje estructural pasa por los apoyos. Dos coordenadas que estén a
    /// menos de <paramref name="tolM"/> son el mismo eje: en un modelo real dos columnas del
    /// mismo eje pueden no estar exactamente a la misma X por un nudo movido un milímetro,
    /// y si no se agruparan saldrían dos burbujas pegadas.
    /// </para>
    /// <para>
    /// Los verticales se numeran <c>1, 2, 3…</c> y los horizontales se letran
    /// <c>A, B, C…</c>, que es la convención y lo que hace la macro.
    /// </para>
    /// </remarks>
    public static EjesModelo DesdeGeometria(ModeloEtabs modelo, double tolM = 0.05)
    {
        // ==============================================================================
        //  PRIMERO SOLO LAS COLUMNAS, Y SIN PASARSE
        // ==============================================================================
        //  Antes se metían también los DOS extremos de cada muro, y en un modelo de SAP2000
        //  —donde los muros vienen partidos en muchos tramos— salía una burbuja en cada
        //  quiebre: veinte ejes donde el modelo tiene cinco. Y los ejes de más no son un
        //  adorno: cada uno se acota, así que el plano se llenaba de cotas inventadas.
        //
        //  Un eje estructural pasa por los APOYOS, así que se deducen de las columnas y los
        //  castillos. Los muros solo entran si no hay columnas suficientes para formar una
        //  cuadrícula, que es el caso del modelo hecho solo con muros de mampostería.
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var e in modelo.Elementos)
        {
            if (e.Clase != ClaseElemento.Columna)
            {
                continue;
            }

            Agregar(xs, e.X1, tolM);
            Agregar(ys, e.Y1, tolM);
        }

        var conColumnas = xs.Count >= 2 || ys.Count >= 2;

        if (!conColumnas)
        {
            foreach (var e in modelo.Elementos)
            {
                if (e.Clase != ClaseElemento.Muro)
                {
                    continue;
                }

                Agregar(xs, e.X1, tolM);
                Agregar(xs, e.X2, tolM);
                Agregar(ys, e.Y1, tolM);
                Agregar(ys, e.Y2, tolM);
            }
        }

        xs.Sort();
        ys.Sort();

        var ejes = new EjesModelo
        {
            Origen = conColumnas
                ? "deducida de las columnas del modelo"
                : "deducida de la geometría"
        };

        for (var i = 0; i < xs.Count; i++)
        {
            ejes.X.Add(new Eje((i + 1).ToString(), xs[i]));
        }

        for (var i = 0; i < ys.Count; i++)
        {
            ejes.Y.Add(new Eje(Letra(i), ys[i]));
        }

        return ejes;

        static void Agregar(List<double> v, double valor, double tol)
        {
            foreach (var ya in v)
            {
                if (Math.Abs(ya - valor) < tol)
                {
                    return;
                }
            }

            v.Add(valor);
        }
    }

    /// <summary>A, B, C… y después AA, AB… Es el <c>LetraDeIndice</c> de la macro.</summary>
    public static string Letra(int i) =>
        i < 26
            ? ((char)('A' + i)).ToString()
            : $"{(char)('A' + (i / 26) - 1)}{(char)('A' + (i % 26))}";
}
