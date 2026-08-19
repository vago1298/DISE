using System.Globalization;

namespace CadLink.Cad;

/// <summary>
/// Dibuja el modelo <b>en 3D</b> en AutoCAD: cada barra con su perfil real, extruido a
/// lo largo de su eje.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cómo se coloca cada barra.</b> El perfil se construye plano, en el XY, centrado en
/// el origen; se extruye en <c>+Z</c> el largo de la barra; y se transforma <b>de una
/// vez</b> con una matriz que lo lleva a su sitio y a su dirección. Se hace así, y no con
/// giros sucesivos, porque una barra puede apuntar a cualquier parte —las diagonales de
/// una armadura no están en ningún plano cómodo— y encadenar rotaciones alrededor de ejes
/// distintos acumula error y es imposible de depurar. Con la matriz, la colocación es una
/// sola operación y se puede comprobar con números.
/// </para>
/// <para>
/// <b>Cómo se orienta el perfil sobre su propio eje.</b> Falta un dato que el modelo no da
/// aquí: el giro del perfil alrededor de la barra. Así que se elige el criterio con el que
/// se arma de verdad: que el <b>alto</b> del perfil quede lo más vertical posible. Para una
/// barra no vertical eso sale de tomar la perpendicular común entre su eje y la vertical
/// global. Para una <b>columna</b> no hay «lo más vertical»: su eje ya es la vertical, así
/// que el perfil se orienta en planta, con su ancho en X.
/// </para>
/// <para>
/// <b>Sólidos y no mallas.</b> Un sólido se puede seccionar, medir y acotar en AutoCAD, y
/// eso es lo que hace que el dibujo sirva para trabajar. Cuesta más que una malla, y con
/// un modelo de cientos de barras se nota; por eso se avisa del tiempo y se lleva la cuenta
/// de lo que se creó. Si la extrusión falla en una barra —una sección degenerada, un largo
/// nulo— esa barra <b>no se pierde</b>: se dibuja su eje como línea 3D y se anota, en lugar
/// de dejar un hueco silencioso en el modelo.
/// </para>
/// </remarks>
public sealed class Modelo3dDrawer
{
    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly List<string> _notas = new();

    /// <summary>Lo que hay que contarle al usuario del último dibujo.</summary>
    public IReadOnlyList<string> Notas => _notas;

    public Modelo3dDrawer(dynamic doc)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);

        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Una barra a dibujar, ya con su perfil resuelto.</summary>
    /// <remarks>
    /// Es un dato plano a propósito: este dibujante no sabe de ETABS ni de SAP2000, solo
    /// de barras con extremos y contorno. Quien lea el modelo hace la traducción.
    /// </remarks>
    public sealed class Barra
    {
        /// <summary>Extremo inicial, en metros.</summary>
        public required double[] P1 { get; init; }

        /// <summary>Extremo final, en metros.</summary>
        public required double[] P2 { get; init; }

        /// <summary>Contorno de la sección: X e Y en metros, centrado en el origen.</summary>
        public required double[] PerfilX { get; init; }

        /// <summary>La otra mitad del contorno.</summary>
        public required double[] PerfilY { get; init; }

        /// <summary>Capa donde va, normalmente por tipo de elemento.</summary>
        public string Capa { get; init; } = "MODELO3D";

        /// <summary>Para poder nombrarla en un aviso.</summary>
        public string Id { get; init; } = string.Empty;
    }

    /// <summary>Cuántos sólidos y cuántos ejes de respaldo se dibujaron.</summary>
    public sealed record Resumen(int Solidos, int Lineas)
    {
        public override string ToString() =>
            Solidos + " sólido(s)"
            + (Lineas > 0
                ? ", y " + Lineas + " barra(s) solo como eje"
                : string.Empty);
    }

    /// <summary>Largo por debajo del cual una barra no se extruye.</summary>
    /// <remarks>
    /// Un décimo de milímetro. Por debajo de eso la dirección no se puede normalizar sin
    /// que el error domine, y AutoCAD rechaza la extrusión.
    /// </remarks>
    private const double LargoMinimo = 1e-4;

    /// <summary>Dibuja todas las barras y devuelve la cuenta.</summary>
    public Resumen Dibujar(IEnumerable<Barra> barras)
    {
        _notas.Clear();

        var solidos = 0;
        var lineas = 0;
        var fallos = 0;

        foreach (var b in barras)
        {
            var largo = Distancia(b.P1, b.P2);

            if (largo < LargoMinimo)
            {
                // Una barra de largo cero no es un fallo del dibujo: es un dato del
                // modelo, y se dice.
                _notas.Add(
                    $"La barra '{b.Id}' mide "
                    + largo.ToString("0.######", CultureInfo.InvariantCulture)
                    + " m y no se pudo extruir.");
                fallos++;
                continue;
            }

            if (Solido(b, largo))
            {
                solidos++;
                continue;
            }

            // Respaldo: el eje. Mejor una linea donde va la barra que un hueco.
            if (Eje(b))
            {
                lineas++;
            }
            else
            {
                fallos++;
            }
        }

        if (lineas > 0)
        {
            _notas.Add(
                lineas + " barra(s) no se pudieron extruir y se dibujaron solo como "
                + "eje. Suele ser una sección sin dimensiones en el modelo.");
        }

        if (fallos > 0)
        {
            _notas.Add(fallos + " barra(s) no se pudieron dibujar.");
        }

        return new Resumen(solidos, lineas);
    }

    /// <summary>
    /// El sólido de una barra: región del perfil, extrusión y colocación.
    /// </summary>
    private bool Solido(Barra b, double largo)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                // 1) El contorno del perfil, cerrado, en el plano XY.
                var n = Math.Min(b.PerfilX.Length, b.PerfilY.Length);

                if (n < 3)
                {
                    return false;
                }

                var pts = new double[n * 3];

                for (var i = 0; i < n; i++)
                {
                    pts[(3 * i) + 0] = b.PerfilX[i];
                    pts[(3 * i) + 1] = b.PerfilY[i];
                    pts[(3 * i) + 2] = 0;
                }

                dynamic pl = _ms.Add3DPoly(pts);
                pl.Closed = true;

                // 2) La region, que es lo unico que AutoCAD sabe extruir.
                dynamic regiones;

                try
                {
                    regiones = _ms.AddRegion(new object[] { pl });
                }
                catch (Exception)
                {
                    Borrar(pl);
                    return false;
                }

                // La polilinea ya no hace falta: la region es independiente.
                Borrar(pl);

                if (regiones is null || (int)regiones.Length < 1)
                {
                    return false;
                }

                dynamic region = regiones[0];

                // 3) La extrusion, a lo largo de +Z.
                dynamic solido;

                try
                {
                    solido = region.AddExtrudedSolid(largo, 0d);
                }
                catch (Exception)
                {
                    Borrar(region);
                    return false;
                }

                // La region se consume al extruir en algunas versiones y en otras no.
                Borrar(region);

                // 4) Y la colocacion, de una sola vez.
                solido.TransformBy(Matriz(b.P1, b.P2, largo));
                solido.Layer = b.Capa;

                return true;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>El eje de la barra, como respaldo cuando no se puede extruir.</summary>
    private bool Eje(Barra b)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic l = _ms.AddLine(
                    new[] { b.P1[0], b.P1[1], b.P1[2] },
                    new[] { b.P2[0], b.P2[1], b.P2[2] });

                l.Layer = b.Capa;
                return true;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// La matriz 4×4 que lleva el perfil extruido a su sitio y a su dirección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sus tres primeras columnas son un <b>marco ortonormal</b>: <c>u</c> el ancho del
    /// perfil, <c>v</c> su alto y <c>w</c> la dirección de la barra, que es hacia donde se
    /// extruyó. La cuarta es el extremo inicial. Al aplicarla, el perfil que estaba en el
    /// XY queda perpendicular a la barra y el sólido va de <c>P1</c> a <c>P2</c>.
    /// </para>
    /// <para>
    /// <b>De dónde sale <c>v</c>.</b> Se toma <c>u</c> como la perpendicular común entre
    /// la barra y la vertical global, y entonces <c>v = w × u</c> queda lo más vertical
    /// que permite la barra. Eso es lo que hace que una viga salga con su alma de pie y no
    /// tumbada al azar.
    /// </para>
    /// <para>
    /// <b>El caso de la columna.</b> Si la barra ya es vertical, la perpendicular común no
    /// existe: el producto vectorial se anula. No es un caso raro que se pueda ignorar
    /// —son todas las columnas del modelo— así que se resuelve aparte, orientando el perfil
    /// en planta. Y se distingue si va hacia arriba o hacia abajo, porque el modelo puede
    /// traer la columna definida en cualquier sentido y el marco tiene que quedar
    /// derecho.
    /// </para>
    /// </remarks>
    private static double[] Matriz(double[] p1, double[] p2, double largo)
    {
        var w = new[]
        {
            (p2[0] - p1[0]) / largo,
            (p2[1] - p1[1]) / largo,
            (p2[2] - p1[2]) / largo
        };

        // u = Z x w, la perpendicular comun entre la barra y la vertical.
        var u = new[] { -w[1], w[0], 0d };

        var n = Math.Sqrt((u[0] * u[0]) + (u[1] * u[1]));

        double[] v;

        if (n < 1e-9)
        {
            // La barra es VERTICAL: una columna. No hay 'lo mas vertical posible', asi
            // que el perfil se orienta en planta.
            u = new[] { 1d, 0d, 0d };
            v = new[] { 0d, w[2] > 0 ? 1d : -1d, 0d };
        }
        else
        {
            u = new[] { u[0] / n, u[1] / n, 0d };

            // v = w x u, que queda lo mas vertical que permite la barra.
            v = new[]
            {
                (w[1] * u[2]) - (w[2] * u[1]),
                (w[2] * u[0]) - (w[0] * u[2]),
                (w[0] * u[1]) - (w[1] * u[0])
            };
        }

        // Por FILAS, que es como la quiere AutoCAD.
        return new[]
        {
            u[0], v[0], w[0], p1[0],
            u[1], v[1], w[1], p1[1],
            u[2], v[2], w[2], p1[2],
            0d,   0d,   0d,   1d
        };
    }

    private static double Distancia(double[] a, double[] b)
    {
        var dx = b[0] - a[0];
        var dy = b[1] - a[1];
        var dz = b[2] - a[2];

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            ((dynamic)ent).Delete();
        }
        catch (Exception)
        {
            // Si no se puede borrar, queda una entidad auxiliar. No es grave.
        }
    }

    /// <summary>Capas del modelo 3D, una por tipo de elemento.</summary>
    public void AsegurarCapas()
    {
        foreach (var (capa, color) in new[]
                 {
                     ("MODELO3D", 7),
                     ("MODELO3D-COLUMNAS", 5),
                     ("MODELO3D-TRABES", 3),
                     ("MODELO3D-DIAGONALES", 6),
                     ("MODELO3D-MUROS", 8),
                     ("MODELO3D-LOSAS", 9)
                 })
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic c = _doc.Layers.Add(capa);
                    c.Color = color;
                });
            }
            catch (Exception)
            {
                // Sin la capa el dibujo sigue saliendo, en la que este activa.
            }
        }
    }
}
