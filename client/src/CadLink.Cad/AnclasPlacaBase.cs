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
    /// <para>
    /// El reparto es el <b>perimetral</b> de la macro —su <c>MODO_ANCLAS = "PERIMETRAL"</c>— y es el
    /// único: los dos valores de la hoja son TOTALES. Las de X se parten entre la hilera de abajo y
    /// la de arriba, con la impar abajo; las de Y van <b>entre</b> esas dos hileras, así que las
    /// anclas de las esquinas —que son de X— no se cuentan dos veces.
    /// </para>
    /// <para>
    /// Hubo un segundo reparto en malla, capturable por fila. Se quitó: no está en la macro y no
    /// hacía falta. Y no era inocuo, era confuso de la peor manera —con 4 y 4, el perímetro da ocho
    /// anclas y la malla dieciséis— así que una casilla que nadie entendía podía duplicar el número
    /// de anclas del detalle.
    /// </para>
    /// <para>
    /// Se admite <c>0</c> en cualquiera de las dos direcciones: una placa puede llevar anclas solo
    /// en un sentido, y la macro lo permite explícitamente.
    /// </para>
    /// </remarks>
    public static List<Ancla> Construir(
        double x0, double y0, double ancho, double alto,
        int nx, int ny, double sepX, double sepY,
        double dAncX, double dAguX, double dAncY, double dAguY)
    {
        var anclas = new List<Ancla>();

        if (nx < 0) { nx = 0; }
        if (ny < 0) { ny = 0; }

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
    /// El mínimo de la <b>columna K</b> —del ancla al canto de la placa— en unidades de dibujo.
    /// Cero para no aplicarlo.
    /// </param>
    public static double SepAuto(
        double dimPlaca, double dimPerfil, double dAgujero, double escala, double bordeLibre = 0)
    {
        var minimo = 0.5 * escala;   // medio centímetro

        var s = dimPerfil > 0 && dimPerfil < dimPlaca
            ? (dimPlaca - dimPerfil) / 4
            : 0.12 * dimPlaca;

        if (s < dAgujero) { s = dAgujero; }

        // EL MÍNIMO DE LA COLUMNA K MANDA SOBRE EL REPARTO. Es la distancia que la placa necesita
        // del ancla a su canto recortado, así que se aplica DESPUÉS: da igual que la cuenta del
        // sobrante entre placa y patín salga más chica, ese número no es admisible.
        if (bordeLibre > 0 && s < bordeLibre) { s = bordeLibre; }

        // LOS DOS TOPES DE LA PLACA VAN AL FINAL, y siguen ganando: una separación mayor que media
        // placa cruzaría las dos hileras en el centro, y eso no es un detalle apretado, es un
        // detalle imposible. Si el mínimo de K no cabe dentro de la placa, aquí se recorta y quien
        // avisa es RevisarDistanciaK: es la diferencia entre dibujar algo que no cumple y decir
        // que la placa es demasiado chica para ese ancla.
        if (s > (dimPlaca / 2) - minimo) { s = (dimPlaca / 2) - minimo; }
        if (s < minimo) { s = minimo; }

        return s;
    }

    /// <summary>
    /// Ajusta una separación al borde <b>capturada</b> para que cumpla el mínimo de la columna K.
    /// </summary>
    /// <param name="sepPedidaCm">Lo que se capturó, en cm. Cero o menos = automática.</param>
    /// <param name="diamAnclaCm">Diámetro del ancla de esa dirección, en cm.</param>
    /// <param name="dimPlacaCm">La medida de la placa en esa dirección, en cm.</param>
    /// <remarks>
    /// <para>
    /// Devuelve <c>0</c> si lo pedido es cero —seguir en automático— y en los demás casos el
    /// <b>mayor</b> entre lo pedido y el mínimo de K, sin pasarse de lo que cabe en la placa.
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

        var minimoCm = BordeMinimoCm(diamAnclaCm);   // columna K

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
    //  LAS TRES TABLAS DE LIBRAMIENTOS
    // ======================================================================
    //
    //  FUENTE: Hylsa, Estándar de Ingeniería ES-03-001, «LIBRAMIENTOS REQUERIDOS PARA ANCLAS
    //  EN PLACAS BASE», pág. 5 de 5, 30-MAY-80. Su nomenclatura, tal cual:
    //
    //      D  -  DIÁMETRO DEL ANCLA (mm)
    //      J  -  DISTANCIA MÍNIMA ENTRE ANCLAS (mm)
    //      K  -  DISTANCIA MÍNIMA DEL ANCLA AL CANTO RECORTADO DE LA PLACA (mm)
    //      L  -  DISTANCIA MÍNIMA DE COLUMNA/CARTABÓN PARA ATORNILLAR (mm)
    //
    //  El cuadro tiene DIECINUEVE renglones y en el croquis se ve el orden a lo largo de una
    //  línea: canto de la placa → K → ancla → L → paño de la columna. O sea que K y L NO son
    //  dos versiones de lo mismo: K mira al borde de la placa y L mira a la columna, y el
    //  ancla tiene que caber entre las dos.
    //
    //  ─── UNA ADVERTENCIA QUE HAY QUE DEJAR ESCRITA ──────────────────────────────────────
    //  Hubo una vuelta en la que estas tablas se «corrigieron» contra una transcripción a
    //  pulgadas del mismo cuadro, y la transcripción era la que estaba mal: había tirado el
    //  renglón de 48 mm —1 7/8", que existe pero es raro— y al tirarlo corrió los valores de
    //  1 5/8" y 1 3/4" un renglón hacia arriba. El resultado fue endurecer J y K en esos dos
    //  diámetros contra el estándar.
    //
    //  Lo que manda es ESTE cuadro, el de milímetros, con sus diecinueve renglones. Si algún
    //  día aparece otra transcripción que no cuadre, cuéntense los renglones antes de tocar
    //  nada: si no son diecinueve, le falta uno.
    //  ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Columna <b>J</b>: distancia mínima <b>entre anclas</b>, centro a centro, en mm.
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
            <= 13 => 40,     // 1/2"
            <= 16 => 45,     // 5/8"
            <= 19 => 60,     // 3/4"
            <= 22 => 65,     // 7/8"
            <= 25 => 75,     // 1"
            <= 29 => 90,     // 1 1/8"
            <= 32 => 95,     // 1 1/4"
            <= 35 => 105,    // 1 3/8"
            <= 38 => 120,    // 1 1/2"
            <= 41 => 120,    // 1 5/8"
            <= 44 => 130,    // 1 3/4"
            <= 48 => 150,    // 1 7/8"  <- el renglón que la transcripción a pulgadas había tirado
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
    /// Columna <b>K</b>: distancia mínima del ancla al <b>canto recortado de la placa</b>, en mm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la que gobierna la <b>separación al borde</b> de las anclas —lo que se captura en «Sep
    /// borde X cm» y «Sep borde Y cm»—, porque es exactamente esa distancia: del centro del ancla al
    /// canto de la placa.
    /// </para>
    /// <para>
    /// Mismo criterio de redondeo que <see cref="SeparacionMinimaJmm"/>. Fuera de la tabla se
    /// extrapola con el factor del último renglón, <c>1.8 · D</c>.
    /// </para>
    /// </remarks>
    public static double DistanciaMinimaKmm(double diametroMm)
    {
        var d = (int)Math.Floor(diametroMm + 0.5);

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
            <= 41 => 70,     // 1 5/8"
            <= 44 => 75,     // 1 3/4"
            <= 48 => 85,     // 1 7/8"
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
    /// La distancia mínima del ancla al <b>canto de la placa</b>, en centímetros. Es la K.
    /// </summary>
    /// <remarks>
    /// El envoltorio existe para que la hoja y la vista previa no tengan que acordarse de convertir
    /// de milímetros: la tabla trabaja en mm y todo lo demás del programa en cm, y esa conversión
    /// repartida por cuatro sitios es una de las que se hace mal una vez y no se nota.
    /// </remarks>
    public static double BordeMinimoCm(double diametroAnclaCm) =>
        diametroAnclaCm <= 0 ? 0 : DistanciaMinimaKmm(diametroAnclaCm * 10) / 10.0;

    /// <summary>
    /// Columna <b>L</b>: distancia mínima del ancla a la <b>columna o al cartabón</b>, en mm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El estándar la llama «DISTANCIA MÍNIMA DE COLUMNA/CARTABÓN PARA ATORNILLAR», y el nombre
    /// dice para qué es: el espacio que hace falta entre el ancla y el paño de la columna para que
    /// entre la <b>llave</b> y se pueda apretar la tuerca. No es una comprobación de resistencia, es
    /// de montaje, y por eso no se puede deducir de las otras dos.
    /// </para>
    /// <para>
    /// <b>No confundirla con la K.</b> En el croquis del estándar se ve el orden a lo largo de una
    /// línea: canto de la placa → K → ancla → L → paño de la columna. La K mira hacia el borde de la
    /// placa y la L hacia dentro, hacia la columna, y el ancla tiene que caber entre las dos. Los
    /// números tampoco permiten confundirlas en un sentido fijo: en un ancla de 5/8" la K pide 30 mm
    /// y la L 28, y en una de 1 1/2" la K pide 65 y la L 66.
    /// </para>
    /// <para>
    /// Mismo criterio de redondeo que las otras dos: al milímetro nominal más cercano y, entre dos
    /// renglones, el inmediato superior. Fuera de la tabla se extrapola con el factor del último
    /// renglón, <c>1.7 · D</c> —172 mm para un ancla de 102—, que es conservador y coherente con la
    /// tendencia.
    /// </para>
    /// </remarks>
    public static double DistanciaMinimaLmm(double diametroMm)
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
            <= 41 => 71,     // 1 5/8"
            <= 44 => 76,     // 1 3/4"
            <= 48 => 82,     // 1 7/8"
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
    /// La holgura mínima del ancla a la columna o al cartabón, en <b>centímetros</b>. Es la L.
    /// </summary>
    public static double HolguraColumnaMinimaCm(double diametroAnclaCm) =>
        diametroAnclaCm <= 0 ? 0 : DistanciaMinimaLmm(diametroAnclaCm * 10) / 10.0;

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
    /// Comprueba la distancia <b>L</b> de cada ancla al paño de la <b>columna o del cartabón</b>.
    /// </summary>
    /// <param name="contornoColumna">
    /// El contorno del perfil, en unidades de dibujo. Si va vacío no se comprueba nada: sin columna
    /// dibujada no hay a qué medirle la holgura.
    /// </param>
    /// <remarks>
    /// <para>
    /// El estándar la llama «DISTANCIA MÍNIMA DE COLUMNA/CARTABÓN PARA ATORNILLAR»: es el espacio
    /// que hace falta entre el ancla y el paño de la columna para que <b>entre la llave</b> y se
    /// pueda apretar la tuerca. Por eso se mide contra el perfil y no contra la placa.
    /// </para>
    /// <para>
    /// <b>Es otra cosa que la K</b>, aunque las dos sean distancias del ancla a algo. En el croquis
    /// del estándar se ve el orden a lo largo de una línea: canto de la placa → K → ancla → L → paño
    /// de la columna. La K mira hacia fuera y la L hacia dentro, y el ancla tiene que caber entre las
    /// dos. Los números tampoco permiten deducir una de la otra: en un ancla de 5/8" la K pide 30 mm
    /// y la L 28, y en una de 1 1/2" la K pide 65 y la L 66.
    /// </para>
    /// <para>
    /// Se mide al <b>perímetro</b> del perfil, así que un ancla que cayera encima del perfil también
    /// se reporta: su holgura sería la distancia al paño más cercano, y eso es menor que L salvo en
    /// un perfil enorme. No se intenta distinguir dentro de fuera porque un ancla debajo de la
    /// columna es un error de captura, no un caso a resolver.
    /// </para>
    /// </remarks>
    public static Incumplimiento? RevisarHolguraColumnaL(
        IReadOnlyList<Ancla> anclas, double[]? contornoColumna, double escala)
    {
        if (escala <= 0 || contornoColumna is null || contornoColumna.Length < 6)
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

            var requerida = DistanciaMinimaLmm(d);

            var disponible =
                ContornoDesplazado.DistanciaAlContorno(contornoColumna, anclas[i].X, anclas[i].Y)
                / escala * 10;

            // La holgura de 0.01 mm es la de la macro: evita que un redondeo de coma flotante
            // rechace una holgura que está exactamente en el límite.
            if (disponible + 0.01 < requerida)
            {
                return new Incumplimiento(
                    "Holgura mínima a la columna (L)",
                    $"Ancla {i + 1}:\n" +
                    $"  Diámetro: {d:0.##} mm\n" +
                    $"  Holgura al paño de la columna: {disponible:0.##} mm\n" +
                    $"  Holgura mínima L: {requerida:0.##} mm\n\n" +
                    "No cabe la llave para apretar la tuerca. Separa más las anclas del perfil " +
                    "—aumentando\nla separación al borde no, al contrario: acercándolas al borde—, " +
                    "usa una placa mayor\no un perfil menor.");
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
