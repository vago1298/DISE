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
    /// <param name="bordeLibre">
    /// El <b>borde libre</b> mínimo de la tabla L, en unidades de dibujo. Cero para no aplicarlo.
    /// </param>
    public static double SepAuto(
        double dimPlaca, double dimPerfil, double dAgujero, double escala, double bordeLibre = 0)
    {
        var minimo = 0.5 * escala;   // medio centímetro

        var s = dimPerfil > 0 && dimPerfil < dimPlaca
            ? (dimPlaca - dimPerfil) / 4
            : 0.12 * dimPlaca;

        if (s < dAgujero) { s = dAgujero; }

        // EL BORDE LIBRE DE LA TABLA L MANDA SOBRE LO DEMÁS. Es lo que impide que el ancla se
        // desconche el borde del concreto, así que se aplica DESPUÉS del reparto: da igual que la
        // cuenta del sobrante entre placa y patín salga más chica, ese número no es admisible.
        if (bordeLibre > 0 && s < bordeLibre) { s = bordeLibre; }

        // LOS DOS TOPES DE LA PLACA VAN AL FINAL, y siguen ganando: una separación mayor que media
        // placa cruzaría las dos hileras en el centro, y eso no es un detalle apretado, es un
        // detalle imposible. Si el borde libre no cabe dentro de la placa, aquí se recorta y quien
        // avisa es RevisarBordeLibreL: es la diferencia entre dibujar algo que no cumple y decir
        // que la placa es demasiado chica para ese ancla.
        if (s > (dimPlaca / 2) - minimo) { s = (dimPlaca / 2) - minimo; }
        if (s < minimo) { s = minimo; }

        return s;
    }

    /// <summary>
    /// Ajusta una separación al borde <b>capturada</b> para que cumpla el borde libre de la tabla L.
    /// </summary>
    /// <param name="sepPedidaCm">Lo que se capturó, en cm. Cero o menos = automática.</param>
    /// <param name="diamAnclaCm">Diámetro del ancla de esa dirección, en cm.</param>
    /// <param name="dimPlacaCm">La medida de la placa en esa dirección, en cm.</param>
    /// <remarks>
    /// <para>
    /// Devuelve <c>0</c> si lo pedido es cero —seguir en automático— y en los demás casos el
    /// <b>mayor</b> entre lo pedido y el borde libre, sin pasarse de lo que cabe en la placa.
    /// </para>
    /// <para>
    /// Vive aquí y no en la hoja porque la usan los dos: la celda, para corregirse sola al salir de
    /// ella, y el dibujante, para que lo que se dibuje cumpla aunque el ancla se cambie después de
    /// haber capturado la separación. Escrita en dos sitios, ese segundo caso se olvidaría.
    /// </para>
    /// </remarks>
    public static double SepBordeAjustada(
        double sepPedidaCm, double diamAnclaCm, double dimPlacaCm)
    {
        if (sepPedidaCm <= 0)
        {
            return 0;
        }

        var minimoCm = BordeLibreMinimoCm(diamAnclaCm);

        var s = Math.Max(sepPedidaCm, minimoCm);

        // Sin pasarse de media placa menos medio centímetro, que es el tope de SepAuto: pasado eso
        // las dos hileras se cruzan.
        if (dimPlacaCm > 0)
        {
            var tope = (dimPlacaCm / 2) - 0.5;

            if (tope > 0 && s > tope)
            {
                s = tope;
            }
        }

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

        // ═══════════════════════════════════════════════════════════════════════════════════
        //  CORREGIDA CONTRA LA TABLA. Esta columna tenía DOS renglones mal, y no era un
        //  redondeo: era un CORRIMIENTO. La versión anterior repetía el 120 del 1 1/2" en el
        //  1 5/8" y arrastraba los dos siguientes hacia arriba, además de meter un renglón de
        //  48 mm que en la tabla no existe:
        //
        //      D           antes    tabla
        //      1 5/8"  41   120  ->  130
        //      1 3/4"  44   130  ->  150
        //      (48 mm)      150      no existe
        //
        //  Un ancla de 1 3/4" pasaba la revisión con 130 mm de separación cuando la tabla pide
        //  150: veinte milímetros de menos, y eso NO se ve en el dibujo. Se ve en obra.
        // ═══════════════════════════════════════════════════════════════════════════════════
        return d switch
        {
            <= 13 => 40,     // 1/2"
            <= 16 => 45,     // 5/8"
            <= 19 => 60,     // 3/4"
            <= 22 => 65,     // 7/8"
            <= 25 => 75,     // 1"
            <= 29 => 90,     // 1 1/8"
            <= 32 => 95,     // 1 1/4"
            <= 35 => 105,    // 1 3/8"
            <= 38 => 120,    // 1 1/2"
            <= 41 => 130,    // 1 5/8"
            <= 44 => 150,    // 1 3/4"
            <= 51 => 150,    // 2"
            <= 57 => 170,    // 2 1/4"
            <= 64 => 195,    // 2 1/2"
            <= 70 => 210,    // 2 3/4"
            <= 76 => 225,    // 3"
            <= 89 => 270,    // 3 1/2"
            <= 102 => 300,   // 4"
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

        // CORREGIDA CONTRA LA TABLA, con el mismo corrimiento que tenía la J y en los mismos dos
        // renglones: el 1 5/8" decía 70 mm y son 75, y el 1 3/4" decía 75 y son 85. El renglón de
        // 48 mm tampoco existe en la tabla.
        return d switch
        {
            <= 13 => 22,     // 1/2"
            <= 16 => 30,     // 5/8"
            <= 19 => 32,     // 3/4"
            <= 22 => 38,     // 7/8"
            <= 25 => 45,     // 1"
            <= 29 => 51,     // 1 1/8"
            <= 32 => 57,     // 1 1/4"
            <= 35 => 60,     // 1 3/8"
            <= 38 => 65,     // 1 1/2"
            <= 41 => 75,     // 1 5/8"
            <= 44 => 85,     // 1 3/4"
            <= 51 => 90,     // 2"
            <= 57 => 100,    // 2 1/4"
            <= 64 => 110,    // 2 1/2"
            <= 70 => 120,    // 2 3/4"
            <= 76 => 135,    // 3"
            <= 89 => 155,    // 3 1/2"
            <= 102 => 180,   // 4"
            _ => 1.8 * d
        };
    }

    /// <summary>
    /// Columna <b>L</b>: <b>borde libre</b> mínimo del centro del ancla, en mm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la <b>tercera</b> columna del cuadro de libramientos, y faltaba: el port se quedó con la
    /// J y la K. Es la que gobierna la <b>separación al borde</b> de las anclas —la que se captura
    /// en «Sep borde X cm» y «Sep borde Y cm»—, así que sin ella esas dos celdas aceptaban
    /// cualquier número.
    /// </para>
    /// <para>
    /// Mismo criterio de redondeo que las otras dos: al milímetro nominal más cercano y, entre dos
    /// renglones, el inmediato superior. Fuera de la tabla se extrapola con el factor del último
    /// renglón, <c>1.7 · D</c> —172 mm para un ancla de 102—, que es conservador y coherente con la
    /// tendencia.
    /// </para>
    /// </remarks>
    public static double BordeLibreMinimoLmm(double diametroMm)
    {
        var d = (int)Math.Floor(diametroMm + 0.5);

        return d switch
        {
            <= 13 => 23,     // 1/2"
            <= 16 => 28,     // 5/8"
            <= 19 => 34,     // 3/4"
            <= 22 => 37,     // 7/8"
            <= 25 => 44,     // 1"
            <= 29 => 49,     // 1 1/8"
            <= 32 => 55,     // 1 1/4"
            <= 35 => 60,     // 1 3/8"
            <= 38 => 66,     // 1 1/2"
            <= 41 => 76,     // 1 5/8"
            <= 44 => 82,     // 1 3/4"
            <= 51 => 87,     // 2"
            <= 57 => 97,     // 2 1/4"
            <= 64 => 107,    // 2 1/2"
            <= 70 => 118,    // 2 3/4"
            <= 76 => 130,    // 3"
            <= 89 => 150,    // 3 1/2"
            <= 102 => 172,   // 4"
            _ => 1.7 * d
        };
    }

    /// <summary>
    /// El <b>borde libre</b> mínimo que le toca a un ancla, en <b>centímetros</b>.
    /// </summary>
    /// <remarks>
    /// El envoltorio existe para que la hoja y la vista previa no tengan que acordarse de convertir
    /// de milímetros: la tabla trabaja en mm y todo lo demás del programa en cm, y esa conversión
    /// repartida por cuatro sitios es una de las que se hace mal una vez y no se nota.
    /// </remarks>
    public static double BordeLibreMinimoCm(double diametroAnclaCm) =>
        diametroAnclaCm <= 0 ? 0 : BordeLibreMinimoLmm(diametroAnclaCm * 10) / 10.0;

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

    /// <summary>
    /// Comprueba el <b>borde libre L</b> de cada ancla al borde más cercano de la placa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la tercera columna del cuadro y hace falta comprobarla aparte de la K: no son la misma
    /// distancia ni una es siempre mayor que la otra —en un ancla de 5/8" la K pide 30 mm y la L
    /// 28, y en una de 1 1/2" la K pide 65 y la L 66—, así que quedarse con una de las dos deja
    /// pasar los casos en los que manda la otra.
    /// </para>
    /// <para>
    /// Aquí solo se llega cuando la separación al borde <b>no pudo</b> ajustarse: la placa es
    /// demasiado chica para ese ancla. Es la diferencia entre dibujar algo que no cumple y decirlo.
    /// </para>
    /// </remarks>
    public static Incumplimiento? RevisarBordeLibreL(
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

            var requerido = BordeLibreMinimoLmm(d);

            var izq = (anclas[i].X - x0) / escala * 10;
            var der = (x0 + ancho - anclas[i].X) / escala * 10;
            var inf = (anclas[i].Y - y0) / escala * 10;
            var sup = (y0 + alto - anclas[i].Y) / escala * 10;

            var menor = izq;
            var borde = "izquierdo";

            if (der < menor) { menor = der; borde = "derecho"; }
            if (inf < menor) { menor = inf; borde = "inferior"; }
            if (sup < menor) { menor = sup; borde = "superior"; }

            if (menor + 0.01 < requerido)
            {
                return new Incumplimiento(
                    "Borde libre mínimo L",
                    $"Ancla {i + 1}:\n" +
                    $"  Diámetro: {d:0.##} mm\n" +
                    $"  Borde más cercano: {borde}\n" +
                    $"  Borde libre disponible: {menor:0.##} mm\n" +
                    $"  Borde libre mínimo L: {requerido:0.##} mm\n\n" +
                    "La separación al borde ya se ajustó al máximo que cabe en esta placa, así " +
                    "que\nla placa es demasiado chica para un ancla de ese diámetro: agrándala o " +
                    "usa\nun ancla menor.");
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
