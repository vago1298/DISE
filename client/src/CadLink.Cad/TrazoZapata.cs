namespace CadLink.Cad;

/// <summary>
/// Los datos de una <b>zapata aislada</b>, en las unidades en que se capturan.
/// </summary>
/// <remarks>
/// Las medidas de la zapata van en <b>metros</b> —es como las lee la macro de la hoja, con
/// <c>ValorCeldaM</c>— y las del dado y la columna en <b>centímetros</b>, porque la macro las
/// multiplica por <c>SCALEELEVATION = 0.01</c> al dibujarlas. Se respetan las dos unidades a
/// propósito: cambiarlas aquí obligaría a revisar cada fórmula portada para ver si el factor
/// sigue estando donde debe.
/// </remarks>
public sealed class ZapataCad
{
    /// <summary>Zapata aislada <b>central</b>: el dado va centrado.</summary>
    public const string Central = "CENTRAL";

    /// <summary>Zapata aislada <b>de lindero</b>: el dado va pegado al paño derecho.</summary>
    public const string Lindero = "LINDERO";

    /// <summary>Los dos tipos, en el orden del desplegable.</summary>
    public static readonly string[] Tipos = { Central, Lindero };

    /// <summary>Nombre de la sección. Es la celda <c>G1</c> / <c>X1</c> de la macro.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary><see cref="Central"/> o <see cref="Lindero"/>.</summary>
    public string Tipo { get; init; } = Central;

    /// <summary>Ancho de la zapata, en metros. <c>E4</c> / <c>V4</c>.</summary>
    public double AnchoM { get; init; }

    /// <summary>Largo de la zapata en planta, en metros. <c>E5</c> / <c>V5</c>.</summary>
    public double LargoM { get; init; }

    /// <summary>Profundidad de desplante, en metros. <c>E6</c> / <c>V6</c>.</summary>
    public double ProfundidadM { get; init; }

    /// <summary>Espesor de la zapata, en metros. <c>E7</c> / <c>V7</c>.</summary>
    public double EspesorM { get; init; }

    /// <summary>Recubrimiento de la zapata, en metros. La macro lo fija en 0.05.</summary>
    public double RecM { get; init; } = 0.05;

    /// <summary>Ancho del dado, en <b>centímetros</b>. <c>G8</c> / <c>X8</c>.</summary>
    public double AnchoDadoCm { get; init; }

    /// <summary>Ancho de la columna, en <b>centímetros</b>. <c>G6</c> / <c>X6</c>.</summary>
    public double AnchoColumnaCm { get; init; }

    /// <summary>Recubrimiento del dado, en centímetros. <c>N8</c> / <c>AE8</c>.</summary>
    public double RecDadoCm { get; init; } = 5;

    /// <summary>Recubrimiento de la columna, en centímetros. <c>N6</c> / <c>AE6</c>.</summary>
    public double RecColumnaCm { get; init; } = 5;

    /// <summary>Si la columna que desplanta es de <b>concreto</b>. <c>H4</c> / <c>Y4</c>.</summary>
    /// <remarks>
    /// Manda dos cosas: si se dibuja la columna encima del dado, y hacia dónde doblan los
    /// ganchos de arranque del dado. Con columna de acero los ganchos van hacia <b>afuera</b>
    /// —no hay columna de concreto que los reciba— y con columna de concreto hacia adentro.
    /// </remarks>
    public bool ColumnaDeConcreto { get; init; }

    /// <summary>Si lleva <b>doble parrilla</b>. <c>H9</c> / <c>Y9</c>.</summary>
    public bool DobleParrilla { get; init; }

    /// <summary>Varilla de la parrilla inferior, la que corre a lo largo. <c>C9</c>.</summary>
    public string VarInf { get; init; } = "#4";

    /// <summary>Su separación, en cm de texto. <c>E9</c>.</summary>
    public string SepInf { get; init; } = "15";

    /// <summary>Varilla transversal de la parrilla inferior. <c>C11</c>.</summary>
    public string VarInfTrans { get; init; } = "#4";

    /// <summary>Su separación. <c>E11</c>.</summary>
    public string SepInfTrans { get; init; } = "15";

    /// <summary>Varilla de la parrilla superior. <c>C13</c>.</summary>
    public string VarSup { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E13</c>.</summary>
    public string SepSup { get; init; } = string.Empty;

    /// <summary>Varilla transversal de la parrilla superior. <c>C15</c>.</summary>
    public string VarSupTrans { get; init; } = string.Empty;

    /// <summary>Su separación. <c>E15</c>.</summary>
    public string SepSupTrans { get; init; } = string.Empty;

    /// <summary>Estribo del dado. <c>O7</c> / <c>AF7</c>.</summary>
    public string EstriboDado { get; init; } = "#3";

    /// <summary>Separación de los estribos del dado, del tipo <c>10-15-20</c>. <c>O8</c>.</summary>
    public string SepEstriboDado { get; init; } = "15";

    /// <summary>Varilla de arranque del dado, paño superior en el sistema local. <c>J7</c>.</summary>
    public string VarDadoSup { get; init; } = "#4";

    /// <summary>La del otro paño. <c>J8</c>.</summary>
    public string VarDadoInf { get; init; } = "#4";

    /// <summary>Cuántas intermedias lleva el dado. <c>K7</c>.</summary>
    public int NIntDado { get; init; }

    /// <summary>Diámetro de las intermedias del dado. <c>L7</c>.</summary>
    public string VarIntDado { get; init; } = string.Empty;

    /// <summary>Largo del gancho de arranque, en metros. La macro lo fija en 0.12.</summary>
    public double GanchoM { get; init; } = 0.12;
}

/// <summary>
/// La <b>geometría de una zapata aislada</b>, portada de las macros. Sin AutoCAD y sin WPF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué está aquí.</b> Es el mismo motivo de <see cref="TrazoAcero"/> y de
/// <see cref="TrazoDiamante"/>: este cálculo lo necesitan dos programas —el dibujante de
/// AutoCAD y la vista previa de la pestaña— y no puede haber dos copias. Una vista previa que
/// coloca la planta a otra distancia, o que reparte los estribos de otra manera, enseña un
/// dibujo que no es el que se va a generar.
/// </para>
/// <para>
/// <b>Las distancias son las de las macros, no unas parecidas.</b> Están todas como constantes
/// con el nombre que tenían en el VBA, para poder cotejarlas una por una:
/// </para>
/// <list type="bullet">
/// <item><b>Central</b>: las secciones crecen hacia la <b>derecha</b>, separadas
/// <c>SEPARACION_SECCIONES = 1</c> m más el ancho de la zapata; el dado va <b>centrado</b>; y
/// la planta se cuelga de la vista de corte, con su borde superior a
/// <c>PLANTA_OFFSET_Y = −3</c> del renglón más bajo del rótulo.</item>
/// <item><b>Lindero</b>: la primera zapata arranca en <c>(−3, −8)</c> y las siguientes crecen
/// hacia la <b>izquierda</b>, separadas <c>0.8</c> m; el dado va pegado al <b>paño derecho</b>
/// —ese es el lindero— y la planta arranca en <c>−15</c>, o más abajo si la zapata es tan larga
/// que se encimaría, dejando <c>PLANTA_SEPARACION_MIN = 1.2</c> m de holgura.</item>
/// </list>
/// </remarks>
public static class TrazoZapata
{
    /// <summary>De centímetros de captura a metros de dibujo. El <c>SCALEELEVATION</c>.</summary>
    public const double EscalaElevacion = 0.01;

    /// <summary>Espesor de la plantilla de concreto simple, en metros.</summary>
    public const double PlantillaEspesor = 0.05;

    /// <summary>Separación entre secciones de la <b>central</b>, en metros.</summary>
    public const double SeparacionCentral = 1.0;

    /// <summary>Separación entre secciones del <b>lindero</b>, en metros.</summary>
    /// <remarks>
    /// En la macro está escrita en negativo —<c>SEPARACION_SECCIONES = −0.8</c>— porque las
    /// secciones se acomodan hacia la izquierda, y la macro usa su valor absoluto. Aquí va en
    /// positivo y el sentido lo pone <see cref="XBase"/>, que es donde se decide.
    /// </remarks>
    public const double SeparacionLindero = 0.8;

    /// <summary>Origen de la primera zapata de lindero: <c>ELEVACION_X_BASE</c>.</summary>
    public const double LinderoXBase = -3.0;

    /// <summary>Y de desplante. <c>ELEVACION_Y_BASE</c> en lindero, <c>yBase</c> en central.</summary>
    public const double YBaseElevacion = -8.0;

    /// <summary>Cuánto baja la planta respecto de la vista de corte, en la central.</summary>
    public const double PlantaOffsetY = -3.0;

    /// <summary>Y de arranque de la planta en el lindero.</summary>
    public const double PlantaYBaseLindero = -15.0;

    /// <summary>Holgura mínima entre el rótulo de la elevación y la planta, en lindero.</summary>
    public const double PlantaSeparacionMin = 1.2;

    /// <summary>Renglón más bajo de la elevación: <c>ROTULO_ESCALA_OFFSET</c>.</summary>
    public const double RotuloEscalaOffset = 0.49;

    /// <summary>Offset de la cota del dado en planta.</summary>
    public const double PlantaCotaOffsetDado = 0.1;

    /// <summary>Largo del gancho de las parrillas, en metros. La macro pasa 0.03.</summary>
    public const double GanchoParrilla = 0.03;

    /// <summary>Factor del gancho de arranque del dado: <c>FACTOR_GANCHO_ABAJO</c>.</summary>
    public const double FactorGanchoAbajo = 15.0;

    /// <summary>Separación mínima de estribos, en metros.</summary>
    public const double SepEstriboMinima = 0.05;

    /// <summary>Retiro del primer y último estribo: <c>STIRRUP_EDGE_OFFSET</c>.</summary>
    public const double EstriboRetiroBorde = 0.05;

    /// <summary>Lo que sobresale la cápsula del estribo: <c>CAPSULE_PROTRUSION</c>.</summary>
    public const double EstriboSobresale = 0.0086;

    /// <summary>Cierre de la última varilla de la malla: <c>PLANTA_FRACCION_CIERRE</c>.</summary>
    public const double PlantaFraccionCierre = 0.3;

    // ======================================================================
    // El acomodo
    // ======================================================================

    /// <summary>
    /// El <b>paño izquierdo</b> de la zapata número <paramref name="indice"/>, en metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí está la diferencia de acomodo entre las dos macros, y es de sentido, no de número:
    /// </para>
    /// <list type="bullet">
    /// <item>La <b>central</b> arranca en 0 y crece hacia la derecha: cada zapata se pone a un
    /// metro del borde derecho de la anterior. La macro lo hace acumulando
    /// <c>xBase = xBase + anchoZapata + SEPARACION_SECCIONES</c>, así que el sitio de una
    /// depende de los anchos de <b>todas</b> las de antes.</item>
    /// <item>La de <b>lindero</b> arranca en −3 y crece hacia la izquierda: se le resta la
    /// separación y el ancho de la zapata que se va a dibujar. Ojo con el detalle: se resta el
    /// ancho de la <b>nueva</b>, no el de la anterior, porque lo que se coloca es su paño
    /// izquierdo.</item>
    /// </list>
    /// </remarks>
    /// <param name="anchos">Los anchos, en metros, en el orden de la tabla.</param>
    /// <param name="indice">Cuál de ellas.</param>
    public static double XBase(string tipo, IReadOnlyList<double> anchos, int indice)
    {
        if (indice <= 0)
        {
            return EsLindero(tipo) ? LinderoXBase : 0.0;
        }

        if (EsLindero(tipo))
        {
            var x = LinderoXBase;

            for (var i = 1; i <= indice; i++)
            {
                x -= SeparacionLindero + Ancho(anchos, i);
            }

            return x;
        }

        var acumulado = 0.0;

        for (var i = 0; i < indice; i++)
        {
            acumulado += Ancho(anchos, i) + SeparacionCentral;
        }

        return acumulado;
    }

    /// <summary>Si el tipo es el de lindero.</summary>
    public static bool EsLindero(string? tipo) =>
        (tipo ?? string.Empty).Trim().Equals(ZapataCad.Lindero, StringComparison.OrdinalIgnoreCase);

    private static double Ancho(IReadOnlyList<double> anchos, int i)
    {
        // La macro usa 1 m cuando el ancho no es válido, para que una fila incompleta no
        // amontone todas las demás en el mismo sitio.
        var a = i >= 0 && i < anchos.Count ? anchos[i] : 0.0;

        return a > 0 ? a : 1.0;
    }

    /// <summary>
    /// Las alturas y los paños de una zapata, ya colocada.
    /// </summary>
    /// <param name="XBase">Paño izquierdo.</param>
    /// <param name="XDer">Paño derecho.</param>
    /// <param name="YZapBot">Desplante: cara inferior de la zapata.</param>
    /// <param name="YZapTop">Lomo de la zapata.</param>
    /// <param name="YTerreno">Nivel del terreno.</param>
    /// <param name="YDadoTop">Donde acaba el dado y arranca la columna.</param>
    /// <param name="XDadoIzq">Paño izquierdo del dado.</param>
    /// <param name="XDadoDer">Paño derecho del dado.</param>
    /// <param name="XColIzq">Paño izquierdo de la columna.</param>
    /// <param name="XColDer">Paño derecho de la columna.</param>
    /// <param name="YPlantillaBot">Fondo de la plantilla de concreto simple.</param>
    /// <param name="YPlanta">Borde inferior de la vista en planta.</param>
    public readonly record struct Acomodo(
        double XBase,
        double XDer,
        double YZapBot,
        double YZapTop,
        double YTerreno,
        double YDadoTop,
        double XDadoIzq,
        double XDadoDer,
        double XColIzq,
        double XColDer,
        double YPlantillaBot,
        double YPlanta);

    /// <summary>
    /// Coloca una zapata: sus alturas, sus paños y de dónde cuelga la planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El dado.</b> En la central va centrado en el ancho de la zapata; en el lindero, su
    /// paño derecho <b>es</b> el paño derecho de la zapata, que es lo que significa lindero: por
    /// ese lado no hay dónde salirse. La columna se coloca igual que el dado.
    /// </para>
    /// <para>
    /// <b>La planta.</b> En la central se cuelga de la vista de corte: su borde superior queda a
    /// tres metros por debajo del renglón más bajo del rótulo —el de «Rec. / Escala»—, así que
    /// la planta baja sola cuando el rótulo baja. En el lindero arranca en −15, y si la zapata
    /// es tan larga que se encimaría con el rótulo, se baja todavía más para dejar 1.2 m de
    /// holgura. Las dos reglas son de las macros y no son intercambiables.
    /// </para>
    /// </remarks>
    public static Acomodo Colocar(ZapataCad z, double xBase)
    {
        var yZapBot = YBaseElevacion;
        var yZapTop = yZapBot + z.EspesorM;
        var yTerreno = yZapBot + z.ProfundidadM;

        // El dado llega hasta la profundidad de desplante: alturaDadoRep = profundidad.
        var yDadoTop = yZapBot + z.ProfundidadM;

        var xDer = xBase + z.AnchoM;

        var wDado = z.AnchoDadoCm * EscalaElevacion;
        var wCol = z.AnchoColumnaCm * EscalaElevacion;

        // Los cuatro paños, declarados uno por uno: una declaracion multiple aqui es mas
        // corta, pero el analizador de tools/validar.py no la entiende y reporta los cuatro
        // como no declarados. Vale mas que la comprobacion pueda leer el codigo.
        var xDadoIzq = 0.0;
        var xDadoDer = 0.0;
        var xColIzq = 0.0;
        var xColDer = 0.0;

        if (EsLindero(z.Tipo))
        {
            xDadoDer = xDer;
            xDadoIzq = xDadoDer - wDado;
            xColDer = xDer;
            xColIzq = xColDer - wCol;

            // La macro los recorta al paño izquierdo: un dado más ancho que la zapata no
            // puede salirse por el otro lado.
            if (xDadoIzq < xBase)
            {
                xDadoIzq = xBase;
            }

            if (xColIzq < xBase)
            {
                xColIzq = xBase;
            }
        }
        else
        {
            var xCentro = xBase + (z.AnchoM / 2);

            xDadoIzq = xCentro - (wDado / 2);
            xDadoDer = xCentro + (wDado / 2);
            xColIzq = xCentro - (wCol / 2);
            xColDer = xCentro + (wCol / 2);
        }

        var yPlanta = EsLindero(z.Tipo)
            ? YPlantaLindero(yZapBot, z.LargoM)
            : YPlantaCentral(yZapBot, z.LargoM);

        return new Acomodo(
            xBase, xDer, yZapBot, yZapTop, yTerreno, yDadoTop,
            xDadoIzq, xDadoDer, xColIzq, xColDer,
            yZapBot - PlantillaEspesor, yPlanta);
    }

    /// <summary>Port de <c>YBasePlanta</c>: la planta cuelga de la vista de corte.</summary>
    public static double YPlantaCentral(double yZapBot, double largoM)
    {
        var yFondoCorte = yZapBot - RotuloEscalaOffset;
        var yTopePlanta = yFondoCorte + PlantaOffsetY;

        return yTopePlanta - largoM;
    }

    /// <summary>
    /// La planta del lindero: en −15, o más abajo si la zapata es larga.
    /// </summary>
    public static double YPlantaLindero(double yZapBot, double largoM)
    {
        var y = yZapBot - RotuloEscalaOffset - PlantaSeparacionMin - largoM - PlantaCotaOffsetDado;

        return y > PlantaYBaseLindero ? PlantaYBaseLindero : y;
    }

    // ======================================================================
    // Las parrillas en la elevación
    // ======================================================================

    /// <summary>
    /// Una parrilla vista de canto: la barra que corre y los círculos de la transversal.
    /// </summary>
    /// <param name="YBarra">Eje de la barra que corre a lo largo.</param>
    /// <param name="YCirculos">Eje de las varillas transversales, vistas de punta.</param>
    /// <param name="XCaraIzq">Cara exterior del gancho izquierdo.</param>
    /// <param name="XCaraDer">La del derecho.</param>
    /// <param name="Diam">Diámetro de la barra que corre.</param>
    /// <param name="DiamCirculos">Diámetro de la transversal.</param>
    /// <param name="Circulos">Centros en X de las transversales.</param>
    public readonly record struct Parrilla(
        double YBarra,
        double YCirculos,
        double XCaraIzq,
        double XCaraDer,
        double Diam,
        double DiamCirculos,
        double[] Circulos);

    /// <summary>
    /// Port de <c>DibujarParrillaZapata</c>: dónde va cada cosa de una parrilla.
    /// </summary>
    /// <remarks>
    /// La barra que corre se apoya en el recubrimiento y las transversales van <b>por dentro</b>
    /// de ella —arriba en la parrilla inferior y abajo en la superior—, que es el orden real de
    /// armado. Los círculos arrancan a medio diámetro de la cara del gancho y se reparten con su
    /// separación; el último se pone solo si cabe.
    /// </remarks>
    public static Parrilla ParrillaEnAlzado(
        double xBase, double yZapBot, double anchoM, double espesorM, double recM,
        double diam, double diamCirculos, double sepCirculosM, bool superior)
    {
        var yBarra = superior
            ? yZapBot + espesorM - recM - (diam / 2)
            : yZapBot + recM + (diam / 2);

        var yCirculos = superior
            ? yBarra - (diam / 2) - (diamCirculos / 2)
            : yBarra + (diam / 2) + (diamCirculos / 2);

        var xCaraIzq = xBase + recM;
        var xCaraDer = xBase + anchoM - recM;

        var sep = sepCirculosM > 0 ? sepCirculosM : 0.12;

        var xIzq = xCaraIzq + (diam / 2) + (diamCirculos / 2);
        var xDer = xCaraDer - (diam / 2) - (diamCirculos / 2);

        var circulos = new List<double>();

        if (xDer > xIzq)
        {
            circulos.Add(xIzq);

            // La tolerancia es la de la macro: un 20 % de la separación. Sin ella, el
            // último círculo puede caer pegado al del extremo.
            var tol = sep * 0.2;
            var x = xIzq + sep;

            while (x < xDer - tol)
            {
                circulos.Add(x);
                x += sep;
            }

            circulos.Add(xDer);
        }

        return new Parrilla(
            yBarra, yCirculos, xCaraIzq, xCaraDer, diam, diamCirculos, circulos.ToArray());
    }

    // ======================================================================
    // Los estribos del dado
    // ======================================================================

    /// <summary>
    /// Port de <c>BuildStirrupCenters</c>: los centros de los estribos del dado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con separación <b>variable</b> —una celda del tipo <c>10-15-20</c>— el elemento se parte
    /// en tres zonas de 25 %, 50 % y 25 %, que es el confinamiento de los extremos. Con
    /// separación única se reparten por igual, con un mínimo de tres.
    /// </para>
    /// <para>
    /// Devuelve las posiciones medidas <b>a lo largo del dado</b>, desde su arranque en la
    /// zapata: el dibujante las rota 90° con el resto del elemento, igual que la macro.
    /// </para>
    /// </remarks>
    public static double[] CentrosEstribos(
        double largo, double s1Cm, double s2Cm, double s3Cm, double offIni, double offFin)
    {
        var centros = new List<double>();

        var iniInterior = offIni;
        var finInterior = largo - offFin;
        var largoInterior = finInterior - iniInterior;

        if (largoInterior <= 0)
        {
            return centros.ToArray();
        }

        var s1 = Math.Max(s1Cm / 100.0, SepEstriboMinima);
        var s2 = s2Cm > 0 ? Math.Max(s2Cm / 100.0, SepEstriboMinima) : s1;
        var s3 = s3Cm > 0 ? Math.Max(s3Cm / 100.0, SepEstriboMinima) : s1;

        var variable = Math.Abs(s1 - s2) > 1e-4 || Math.Abs(s2 - s3) > 1e-4;

        void Agregar(double v)
        {
            if (centros.Count == 0 || Math.Abs(centros[^1] - v) > 1e-4)
            {
                centros.Add(v);
            }
        }

        void PorSeparacion(double desde, double hasta, double sep)
        {
            var n = (int)((hasta - desde) / sep);

            if (n < 1)
            {
                n = 1;
            }

            for (var i = 1; i <= n; i++)
            {
                var pos = desde + (i * sep);

                if (pos < hasta - 1e-4)
                {
                    Agregar(iniInterior + pos);
                }
            }
        }

        if (variable)
        {
            var zona1 = largoInterior * 0.25;
            var zona2 = zona1 + (largoInterior * 0.5);

            PorSeparacion(0, zona1, s1);
            PorSeparacion(zona1, zona2, s2);
            PorSeparacion(zona2, largoInterior, s3);
        }
        else
        {
            var n = (int)(largoInterior / s1);

            if (n < 3)
            {
                n = 3;
            }

            var paso = largoInterior / n;

            for (var i = 1; i <= n - 1; i++)
            {
                Agregar(iniInterior + (i * paso));
            }
        }

        return centros.ToArray();
    }

    /// <summary>
    /// Port de <c>ApplyCapsuleProtrusion</c>: el primero y el último salen un poco.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que la cápsula del estribo de los extremos asome del acero que abraza, y
    /// no un adorno: sin eso, el estribo del extremo queda escondido detrás de la barra.
    /// </remarks>
    public static void Sobresalir(double[] centros)
    {
        if (centros.Length == 0)
        {
            return;
        }

        centros[0] -= EstriboSobresale;
        centros[^1] += EstriboSobresale;
    }

    /// <summary>Port de <c>QuitarPrimerosEstribos</c>.</summary>
    /// <remarks>
    /// El dado se salta los primeros porque ahí está la parrilla de la zapata: dibujarlos
    /// pondría estribos encima del acero de la parrilla. Son dos con doble parrilla y uno con
    /// una sola, que es lo que la macro decide con <c>DADO_ESTRIBOS_OMITIR_*</c>.
    /// </remarks>
    public static double[] QuitarPrimeros(double[] centros, int n) =>
        n <= 0 || centros.Length <= n ? (n > 0 ? Array.Empty<double>() : centros) : centros[n..];

    // ======================================================================
    // La malla en planta
    // ======================================================================

    /// <summary>
    /// Port de <c>PosicionesConSeparacion</c>: dónde va cada varilla de la malla.
    /// </summary>
    /// <remarks>
    /// La última se agrega solo si el hueco que queda pasa del 30 % de la separación —el
    /// <c>PLANTA_FRACCION_CIERRE</c>—. Sin ese tope, en un ancho que no es múltiplo de la
    /// separación aparece una varilla pegada a la anterior, que en el plano se lee como un error
    /// de armado.
    /// </remarks>
    public static double[] Posiciones(double ini, double fin, double sep)
    {
        var salida = new List<double>();

        if (fin <= ini || sep <= 0)
        {
            return salida.ToArray();
        }

        var p = ini;
        var ultima = ini;

        while (p <= fin + 1e-4)
        {
            salida.Add(p);
            ultima = p;
            p += sep;
        }

        if (fin - ultima > sep * PlantaFraccionCierre)
        {
            salida.Add(fin);
        }

        return salida.ToArray();
    }

    /// <summary>
    /// El hueco del dado en planta, que es donde se recortan las varillas.
    /// </summary>
    /// <remarks>
    /// En la central el dado va centrado y en el lindero pegado al paño derecho, igual que en la
    /// elevación: la planta y el corte tienen que contar la misma historia.
    /// </remarks>
    public static (double X1, double Y1, double X2, double Y2) HuecoDelDado(
        ZapataCad z, double xBase, double yPlanta)
    {
        var wDado = z.AnchoDadoCm * EscalaElevacion;

        var yCen = yPlanta + (z.LargoM / 2);

        var y1 = yCen - (wDado / 2);
        var y2 = yCen + (wDado / 2);

        if (EsLindero(z.Tipo))
        {
            var xDer = xBase + z.AnchoM;

            return (xDer - wDado, y1, xDer, y2);
        }

        var xCen = xBase + (z.AnchoM / 2);

        return (xCen - (wDado / 2), y1, xCen + (wDado / 2), y2);
    }
}
