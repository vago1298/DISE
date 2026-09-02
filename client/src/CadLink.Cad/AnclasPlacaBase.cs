namespace CadLink.Cad;

/// <summary>
/// El reparto de las anclas de una placa base y los <b>libramientos</b> que hay que respetar.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>ConstruirAnclas</c>, <c>ValidarSeparacionAnclas</c>,
/// <c>ValidarDistanciaBordeK</c>, <c>SeparacionMinimaTablaJMM</c>,
/// <c>DistanciaMinimaTablaKMM</c>, <c>PosLineal</c> y <c>SepAuto</c> de la macro
/// <c>DibujarPlacaBase_BloqueXX</c>.
/// </para>
/// <para>
/// Vive <b>aparte del dibujante y sin nada de COM</b>, igual que <see cref="Estribos"/>, y por el
/// mismo motivo: es aritmética pura, es la parte que decide si un detalle es constructible o no, y
/// así se puede comprobar sin AutoCAD delante. Las dos tablas —J y K— son las del cuadro
/// «Libramientos requeridos para anclas en placas base», y equivocarse en ellas no se ve en el
/// dibujo: se ve en obra, cuando la tuerca no entra o el concreto se desconcha en el borde.
/// </para>
/// </remarks>
public static class AnclasPlacaBase
{
    /// <summary>Cómo se reparten las anclas sobre la placa.</summary>
    public enum Modo
    {
        /// <summary>
        /// Los valores de la hoja son <b>totales</b> y se reparten en el perímetro.
        /// </summary>
        /// <remarks>
        /// X: mitad abajo y mitad arriba. Y: mitad izquierda y mitad derecha, y <b>entre</b> las
        /// hileras de X, así que las anclas de las esquinas —que son de X— no se cuentan dos veces.
        /// Es el <c>MODO_ANCLAS = "PERIMETRAL"</c> de la macro y el caso normal.
        /// </remarks>
        Perimetral,

        /// <summary>Los valores son el número de anclas por dirección: una malla nx × ny.</summary>
        Malla
    }

    /// <summary>Una ancla colocada, con sus dos diámetros.</summary>
    /// <param name="X">Centro, en unidades de dibujo.</param>
    /// <param name="Y">Centro, en unidades de dibujo.</param>
    /// <param name="DAncla">Diámetro del ancla, en unidades de dibujo.</param>
    /// <param name="DAgujero">Diámetro del agujero —el ancla más su holgura—.</param>
    /// <param name="EsX">
    /// Pertenece a las hileras de la dirección X. Decide qué diámetro le toca y con qué texto se
    /// rotula, y <b>no se gira con la placa</b>: las de X quedan siempre en horizontal.
    /// </param>
    public readonly record struct Ancla(
        double X, double Y, double DAncla, double DAgujero, bool EsX);

    /// <summary>
    /// Coloca las anclas sobre la placa <b>ya orientada</b>, en coordenadas del dibujo.
    /// </summary>
    /// <param name="nx">
    /// «N. de anclas (X)»: se reparten a lo <b>ancho</b>, en horizontal. Sale de C11.
    /// </param>
    /// <param name="ny">
    /// «N. de anclas (y)»: se reparten a lo <b>alto</b>, en vertical. Sale de C10.
    /// </param>
    /// <remarks>
    /// Se admite <c>0</c> en cualquiera de las dos direcciones: una placa puede llevar anclas solo
    /// en un sentido, y la macro lo permite explícitamente.
    /// </remarks>
    public static List<Ancla> Construir(
        double x0, double y0, double ancho, double alto,
        int nx, int ny, double sepX, double sepY,
        double dAncX, double dAguX, double dAncY, double dAguY,
        Modo modo = Modo.Perimetral)
    {
        var anclas = new List<Ancla>();

        if (nx < 0) { nx = 0; }
        if (ny < 0) { ny = 0; }

        if (modo == Modo.Perimetral)
        {
            // ---------- ANCLAS (X): el TOTAL se reparte entre abajo y arriba ----------
            // 6 -> 3 abajo + 3 arriba. Si es impar, la de más va ABAJO, como en la macro.
            for (var j = 0; j <= 1; j++)
            {
                var enFila = j == 0 ? (nx + 1) / 2 : nx / 2;
                var yj = j == 0 ? y0 + sepY : y0 + alto - sepY;

                for (var i = 0; i < enFila; i++)
                {
                    anclas.Add(new Ancla(
                        PosLineal(x0 + sepX, x0 + ancho - sepX, enFila, i),
                        yj, dAncX, dAguX, EsX: true));
                }
            }

            // ---------- ANCLAS (y): el TOTAL se reparte entre izquierda y derecha ----------
            // Van ENTRE las hileras de X, así que las esquinas —que son de X— no se repiten.
            for (var i = 0; i <= 1; i++)
            {
                var enCol = i == 0 ? (ny + 1) / 2 : ny / 2;
                var xi = i == 0 ? x0 + sepX : x0 + ancho - sepX;

                for (var k = 1; k <= enCol; k++)
                {
                    anclas.Add(new Ancla(
                        xi,
                        y0 + sepY + (k * ((alto - (2 * sepY)) / (enCol + 1))),
                        dAncY, dAguY, EsX: false));
                }
            }

            return anclas;
        }

        // ---------- MALLA: nx en horizontal por ny en vertical ----------
        for (var j = 0; j < ny; j++)
        {
            var yj = PosLineal(y0 + sepY, y0 + alto - sepY, ny, j);

            for (var i = 0; i < nx; i++)
            {
                var xi = PosLineal(x0 + sepX, x0 + ancho - sepX, nx, i);

                // Las hileras horizontales extremas llevan el diámetro de X; las interiores, el
                // de Y. La salvedad del nx = 1 es de la macro: una sola columna de anclas es una
                // hilera vertical, así que le toca el diámetro de Y aunque esté en el extremo.
                var esExtrema = (j == 0 || j == ny - 1) && !(nx == 1 && ny > 1);

                anclas.Add(esExtrema
                    ? new Ancla(xi, yj, dAncX, dAguX, EsX: true)
                    : new Ancla(xi, yj, dAncY, dAguY, EsX: false));
            }
        }

        return anclas;
    }

    /// <summary>Posición <paramref name="i"/> de <paramref name="n"/>, repartidas entre a y b.</summary>
    /// <remarks>Con <c>n = 1</c> va al centro, no al extremo: es el <c>PosLineal</c> de la macro.</remarks>
    public static double PosLineal(double a, double b, int n, int i) =>
        n <= 1 ? (a + b) / 2 : a + (i * (b - a) / (n - 1));

    /// <summary>
    /// Separación al borde <b>automática</b>, cuando la hoja no la da.
    /// </summary>
    /// <remarks>
    /// A media distancia entre el paño del perfil y el borde de la placa, que es donde cae de forma
    /// natural el ancla. Con topes: nunca menos que el diámetro del agujero —o el ancla quedaría
    /// mordiendo el borde— y nunca tanto que las dos hileras se cruzarían en el centro.
    /// </remarks>
    public static double SepAuto(
        double dimPlaca, double dimPerfil, double dAgujero, double escala)
    {
        var minimo = 0.5 * escala;   // medio centímetro

        var s = dimPerfil > 0 && dimPerfil < dimPlaca
            ? (dimPlaca - dimPerfil) / 4
            : 0.12 * dimPlaca;

        if (s < dAgujero) { s = dAgujero; }
        if (s > (dimPlaca / 2) - minimo) { s = (dimPlaca / 2) - minimo; }
        if (s < minimo) { s = minimo; }

        return s;
    }

    // ======================================================================
    //  LAS DOS TABLAS DE LIBRAMIENTOS
    // ======================================================================

    /// <summary>
    /// Columna <b>J</b>: separación mínima centro a centro entre dos anclas, en mm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El diámetro se redondea al milímetro nominal más cercano y, si cae entre dos renglones, se
    /// usa el <b>inmediato superior</b>. Es el criterio de la macro y es el prudente: quedarse en
    /// el renglón de abajo daría por buena una separación que la tabla no autoriza.
    /// </para>
    /// <para>
    /// Fuera de la tabla se extrapola a <b>tres diámetros</b>, que es conservador y coherente con
    /// la tendencia de los últimos renglones.
    /// </para>
    /// </remarks>
    public static double SeparacionMinimaJmm(double diametroMm)
    {
        var d = (int)Math.Floor(diametroMm + 0.5);

        return d switch
        {
            <= 13 => 40,
            <= 16 => 45,
            <= 19 => 60,
            <= 22 => 65,
            <= 25 => 75,
            <= 29 => 90,
            <= 32 => 95,
            <= 35 => 105,
            <= 38 => 120,
            <= 41 => 120,
            <= 44 => 130,
            <= 48 => 150,
            <= 51 => 150,
            <= 57 => 170,
            <= 64 => 195,
            <= 70 => 210,
            <= 76 => 225,
            <= 89 => 270,
            <= 102 => 300,
            _ => 3.0 * d
        };
    }

    /// <summary>
    /// Columna <b>K</b>: distancia mínima del centro del ancla al borde de la placa, en mm.
    /// </summary>
    /// <remarks>
    /// Mismo criterio de redondeo que <see cref="SeparacionMinimaJmm"/>. Fuera de la tabla se
    /// extrapola con el factor del último renglón, <c>1.8 · D</c>.
    /// </remarks>
    public static double DistanciaMinimaKmm(double diametroMm)
    {
        var d = (int)Math.Floor(diametroMm + 0.5);

        return d switch
        {
            <= 13 => 22,
            <= 16 => 30,
            <= 19 => 32,
            <= 22 => 38,
            <= 25 => 45,
            <= 29 => 51,
            <= 32 => 57,
            <= 35 => 60,
            <= 38 => 65,
            <= 41 => 70,
            <= 44 => 75,
            <= 48 => 85,
            <= 51 => 90,
            <= 57 => 100,
            <= 64 => 110,
            <= 70 => 120,
            <= 76 => 135,
            <= 89 => 155,
            <= 102 => 180,
            _ => 1.8 * d
        };
    }

    /// <summary>Lo que impide dibujar la placa, dicho con nombre y números.</summary>
    /// <remarks>
    /// <b>Se devuelve el motivo, no un <c>bool</c>.</b> Un «no cumple» a secas obliga al usuario a
    /// adivinar qué ancla y por cuánto; con el par de anclas, la distancia disponible y la exigida,
    /// sabe si le sobra un ancla o le falta placa.
    /// </remarks>
    public sealed record Incumplimiento(string Titulo, string Detalle);

    /// <summary>
    /// Comprueba la separación <b>J</b> entre todos los pares de anclas.
    /// </summary>
    /// <remarks>
    /// Con diámetros distintos manda el <b>mayor</b> de los dos valores J: la tabla es por ancla, y
    /// dos anclas vecinas tienen que respetar la más exigente de las dos.
    /// </remarks>
    public static Incumplimiento? RevisarSeparacionJ(
        IReadOnlyList<Ancla> anclas, double escala)
    {
        if (anclas.Count < 2 || escala <= 0)
        {
            return null;
        }

        for (var i = 0; i < anclas.Count - 1; i++)
        {
            var dI = anclas[i].DAncla / escala * 10;

            if (dI <= 0)
            {
                return new Incumplimiento(
                    "Diámetro no válido",
                    $"El diámetro del ancla {i + 1} no es válido.");
            }

            var jI = SeparacionMinimaJmm(dI);

            for (var k = i + 1; k < anclas.Count; k++)
            {
                var dK = anclas[k].DAncla / escala * 10;

                if (dK <= 0)
                {
                    return new Incumplimiento(
                        "Diámetro no válido",
                        $"El diámetro del ancla {k + 1} no es válido.");
                }

                var requerida = Math.Max(jI, SeparacionMinimaJmm(dK));

                var dx = (anclas[i].X - anclas[k].X) / escala * 10;
                var dy = (anclas[i].Y - anclas[k].Y) / escala * 10;
                var distancia = Math.Sqrt((dx * dx) + (dy * dy));

                // La holgura de 0.01 mm es de la macro: evita que un redondeo de coma flotante
                // rechace una separación que está exactamente en el límite.
                if (distancia + 0.01 < requerida)
                {
                    return new Incumplimiento(
                        "Separación mínima de anclas",
                        $"Anclas {i + 1} y {k + 1}:\n" +
                        $"  Diámetros: {dI:0.##} mm y {dK:0.##} mm\n" +
                        $"  Separación disponible: {distancia:0.##} mm\n" +
                        $"  Separación mínima J: {requerida:0.##} mm\n\n" +
                        "Aumenta las dimensiones de la placa, reduce el número de anclas o " +
                        "revisa las separaciones al borde.");
                }
            }
        }

        return null;
    }

    /// <summary>Comprueba la distancia <b>K</b> de cada ancla al borde más cercano.</summary>
    public static Incumplimiento? RevisarDistanciaK(
        IReadOnlyList<Ancla> anclas,
        double x0, double y0, double ancho, double alto, double escala)
    {
        if (escala <= 0)
        {
            return null;
        }

        for (var i = 0; i < anclas.Count; i++)
        {
            var d = anclas[i].DAncla / escala * 10;

            if (d <= 0)
            {
                return new Incumplimiento(
                    "Diámetro no válido",
                    $"El diámetro del ancla {i + 1} no es válido.");
            }

            var requerida = DistanciaMinimaKmm(d);

            var izq = (anclas[i].X - x0) / escala * 10;
            var der = (x0 + ancho - anclas[i].X) / escala * 10;
            var inf = (anclas[i].Y - y0) / escala * 10;
            var sup = (y0 + alto - anclas[i].Y) / escala * 10;

            var menor = izq;
            var borde = "izquierdo";

            if (der < menor) { menor = der; borde = "derecho"; }
            if (inf < menor) { menor = inf; borde = "inferior"; }
            if (sup < menor) { menor = sup; borde = "superior"; }

            if (menor + 0.01 < requerida)
            {
                return new Incumplimiento(
                    "Distancia mínima K al borde",
                    $"Ancla {i + 1}:\n" +
                    $"  Diámetro: {d:0.##} mm\n" +
                    $"  Borde más cercano: {borde}\n" +
                    $"  Distancia disponible: {menor:0.##} mm\n" +
                    $"  Distancia mínima K: {requerida:0.##} mm\n\n" +
                    "Aumenta la dimensión de la placa o ajusta la separación al borde.");
            }
        }

        return null;
    }

    /// <summary>Las coordenadas X distintas de las anclas, ordenadas. Para la cadena de cotas.</summary>
    public static List<double> ValoresUnicosX(IReadOnlyList<Ancla> anclas, double escala) =>
        Unicos(anclas.Select(a => a.X), escala);

    /// <summary>Las coordenadas Y distintas de las anclas, ordenadas.</summary>
    public static List<double> ValoresUnicosY(IReadOnlyList<Ancla> anclas, double escala) =>
        Unicos(anclas.Select(a => a.Y), escala);

    private static List<double> Unicos(IEnumerable<double> valores, double escala)
    {
        var tol = 0.001 * (escala > 0 ? escala : 1);
        var salida = new List<double>();

        foreach (var v in valores)
        {
            if (!salida.Any(x => Math.Abs(x - v) < tol))
            {
                salida.Add(v);
            }
        }

        salida.Sort();

        return salida;
    }
}
