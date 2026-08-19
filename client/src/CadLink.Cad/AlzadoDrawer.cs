using System.Globalization;
using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// Dibuja alzados de trabes, contratrabes, columnas y dados en AutoCAD.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>ALZADOS V2</c>. El inventario completo de rutinas y su estado está en
/// <c>docs/inventario-macro-alzados.md</c>.
/// </para>
/// <para>
/// Dos ideas del original que conviene tener presentes al leer esto:
/// </para>
/// <list type="number">
///   <item>
///     El <b>alzado vertical se dibuja horizontal y se gira 90°</b>. Así una sola
///     rutina sirve para trabes y columnas, y las fórmulas no se duplican.
///   </item>
///   <item>
///     La <b>geometría vive dentro de un bloque</b> y las cotas fuera, en el espacio
///     modelo. Por eso el giro se aplica a la definición del bloque y no a las
///     anotaciones.
///   </item>
/// </list>
/// </remarks>
public sealed class AlzadoDrawer
{
    private const string PatronConcreto = "AR-CONC";
    private const string PatronRespaldo = "ANSI31";
    private const int ColorPatron = 251;
    private const int ColorFondo = 9;
    private const int ColorRellenoEstribo = 152;
    private const int PorCapa = 256;
    private const int ColorVerde = 3;

    private const string EstiloTexto = "SECCIONES";

    /// <summary>Cuánto sobresale la cápsula del estribo respecto al recubrimiento.</summary>
    private const double ArcOffset = 0.0039;

    private const double HookDiamFactor = 12.0;
    private const double HookClearH = 0.015;
    private const double HookClearV = 0.01;
    private const double HookClearEst = 0.005;

    private const double AlturaTitulo = 0.03;
    private const double AlturaEscala = 0.0225;

    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly double _escala;
    private readonly double _f;

    private readonly List<string> _log = new();
    private readonly List<string> _notas = new();

    /// <summary>Nombres de bloque ya usados en esta corrida.</summary>
    private readonly HashSet<string> _nombres = new(StringComparer.OrdinalIgnoreCase);

    // Rellenos del alzado en curso, para dejarlos en su orden de dibujo
    private readonly List<object> _hatchConcreto = new();
    private readonly List<object> _fillVarillas = new();
    private readonly List<object> _fillEstribos = new();

    /// <summary>
    /// La polilínea del zuncho helicoidal macizo del alzado en curso, si lo hay.
    /// </summary>
    /// <remarks>
    /// Se guarda para poder <b>devolverle su color</b> después de
    /// <see cref="ContornosNegros"/>. Ese método repinta de negro todo lo que no sea un
    /// hatch, y el zuncho macizo es una polilínea con ancho, no un hatch: por eso salía
    /// negro en el plano por más que <see cref="HeliceMaciza"/> le pusiera el ACI 152.
    /// No se puede arreglar excluyéndolo dentro de <c>ContornosNegros</c>, porque ahí
    /// solo se ve el tipo de entidad y hay muchas otras polilíneas que sí deben pasar
    /// a negro.
    /// </remarks>
    private object? _zunchoMacizo;

    public AlzadoDrawer(dynamic doc, double escala = 0.01)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);
        _escala = escala <= 0 ? 0.01 : escala;
        _f = _escala / 0.01;

        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Escala del patrón AR-CONC.</summary>
    public double EscalaHatch { get; set; } = 0.01;

    /// <summary>
    /// Aviso de que se acaba de insertar el bloque de una sección, con
    /// <c>(id, x, y)</c> de su esquina inferior izquierda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe para que las <b>llamadas de las varillas</b> se rehagan junto al bloque
    /// insertado. No viajan dentro de él —<c>SeccionDrawer.Bloquear</c> deja fuera las
    /// capas <c>COTAS</c> y <c>ROTULOS</c> a propósito— así que el corte que se pone al
    /// lado del alzado llegaba pelado. Ver
    /// <c>SeccionDrawer.LlamadasJuntoAlBloque</c>.
    /// </para>
    /// <para>
    /// <b>Es un aviso y no una llamada directa</b> para no meter aquí una dependencia
    /// de <c>SeccionCad</c> ni de <c>SeccionDrawer</c>. Este dibujante sabe
    /// <i>dónde</i> quedó el bloque, pero no sabe dibujar llamadas; quien sí sabe es el
    /// de secciones. Con el aviso, cada uno se queda con lo suyo y no hace falta un
    /// tercer mapeador de la hoja al formato de la sección.
    /// </para>
    /// </remarks>
    public Action<string, double, double>? TrasInsertarSeccion { get; set; }

    /// <summary>
    /// Alto de la sección más alta ya dibujada, en <b>metros de dibujo</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que separa la fila de alzados de la fila de secciones. Se pone <b>antes</b>
    /// de dibujar, y con ello los bloques y los alzados quedan a
    /// <see cref="AlzadoLayout.AireSobreSecciones"/> metros por encima de la sección
    /// más alta en lugar de en la cota fija Y=2 de la macro.
    /// </para>
    /// <para>
    /// En cero se comporta exactamente como la macro. Así, quien no lo ponga sigue
    /// obteniendo el resultado de siempre.
    /// </para>
    /// </remarks>
    public double AltoMaximoSeccion { get; set; }

    /// <summary>Y en la que se apoya toda la fila de alzados.</summary>
    /// <remarks>
    /// Se calcula cada vez a partir de <see cref="AltoMaximoSeccion"/> y no se guarda
    /// en un campo: si se cacheara, cambiar el alto después de dibujar el primer
    /// elemento dejaría la fila partida en dos alturas.
    /// </remarks>
    private double YDeLaFila => AlzadoLayout.YArranque(AltoMaximoSeccion);

    public IReadOnlyList<string> Fallos => _log;

    public IReadOnlyList<string> Notas
    {
        get
        {
            var todo = new List<string>();
            todo.AddRange(AcadInterop.Bitacora);
            todo.AddRange(_notas);
            return todo;
        }
    }

    // ==================================================================
    // Entrada
    // ==================================================================

    /// <summary>
    /// Dibuja el alzado de un elemento y devuelve el ancho total que ocupó.
    /// </summary>
    /// <param name="x">Borde izquierdo donde empieza.</param>
    /// <param name="y">Cota de arranque, la misma para todos los elementos.</param>
    public double Dibujar(AlzadoCad a, double x, double y)
    {
        var largo = LargoDe(a);

        if (largo <= 0)
        {
            _log.Add($"Alzado '{a.Id}': longitud no válida.");
            return 0;
        }

        return a.EsVertical
            ? DibujarVertical(a, x, y, largo)
            : DibujarHorizontal(a, x, y, largo);
    }

    /// <summary>
    /// Dibuja un elemento completo: <b>su sección al costado</b> y su alzado, en el
    /// sitio que les toca. Devuelve la X donde arranca el elemento siguiente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto es lo que faltaba.</b> Antes solo se dibujaba el alzado y quien
    /// llamaba avanzaba la X a su manera. La macro hace tres cosas más, y las tres se
    /// notan en el plano:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Inserta el <b>bloque de la sección</b> —el <c>CORTE A-A'</c>— junto al
    ///     alzado: a su izquierda en la trabe, debajo en la columna.
    ///   </item>
    ///   <item>
    ///     Lo apoya en <c>Y_BLOQUES</c> por su <b>paño inferior</b>, no por su punto
    ///     de inserción. Muchos bloques traen el punto base en el centroide, y por eso
    ///     una columna de 50 cm insertada en Y=2 aparecía en 1.75.
    ///   </item>
    ///   <item>
    ///     Avanza la fila con las separaciones de la macro, que dependen del tipo de
    ///     elemento. Ver <see cref="AlzadoLayout"/>.
    ///   </item>
    /// </list>
    /// </remarks>
    public double DibujarElemento(AlzadoCad a, double x0)
    {
        var largo = LargoDe(a);

        if (largo <= 0)
        {
            _log.Add($"Alzado '{a.Id}': longitud no válida.");
            return x0;
        }

        // La X de la sección se pide a AlzadoLayout para no repetir aquí el
        // MARGEN_COL de la columna.
        var xSec = AlzadoLayout.XSeccion(x0, a.EsVertical);

        var y = YDeLaFila;

        var sec = InsertarSeccion(a.Id, xSec, y);

        // Si el bloque de la sección no existe, la macro supone 0.8 x 0.4 para que
        // los elementos siguientes no se encimen. No es un adorno: sin esto, un ID
        // sin sección arrastra el desorden al resto de la fila.
        var ancho = sec?.Ancho ?? AlzadoLayout.AnchoSeccionSupuesto;
        var tope = sec?.Tope ?? (y + AlzadoLayout.AltoSeccionSupuesto);

        // El rótulo del elemento cuelga del bloque de la SECCION, no del alzado, y va
        // UNO por elemento aunque el elemento lleve dos alzados. Ver RotuloDelElemento.
        RotuloDelElemento(a, xSec, y, ancho);

        // Una columna RECTANGULAR lleva dos alzados cuando no es cuadrada, uno por
        // cada cara. Una columna REDONDA no: se ve igual desde cualquier lado, así
        // que el segundo alzado sería una copia exacta del primero ocupando plano.
        var dosCaras = a.EsVertical
                       && !a.Circular
                       && a.BaseCm > 0
                       && Math.Abs(a.BaseCm - a.AlturaCm) > 1e-4;

        var p = AlzadoLayout.Colocar(x0, a.EsVertical, ancho, tope, largo, dosCaras, y);

        if (a.EsVertical)
        {
            DibujarVertical(a, p.XAlzado, p.YAlzado, largo);
        }
        else
        {
            DibujarHorizontal(a, p.XAlzado, p.YAlzado, largo);
        }

        return p.XSiguiente;
    }

    /// <summary>Ancho y paño superior de una sección ya insertada.</summary>
    public sealed class SeccionPuesta
    {
        public required double Ancho { get; init; }
        public required double Tope { get; init; }
    }

    /// <summary>
    /// Inserta el bloque de la sección, lo apoya en <paramref name="y"/> y lo rotula
    /// <c>CORTE A-A'</c>.
    /// </summary>
    /// <returns>Sus medidas, o <c>null</c> si el bloque no existe en el dibujo.</returns>
    /// <remarks>
    /// El bloque se coloca en dos pasos, igual que la macro: primero se inserta y
    /// después se <b>mueve</b> para que su borde izquierdo caiga en
    /// <paramref name="x"/> y su paño inferior en <paramref name="y"/>. No se puede
    /// hacer de una vez porque hasta que no está insertado no se sabe dónde tiene el
    /// punto base, y en la mayoría de los bloques de sección está en el centroide.
    /// </remarks>
    public SeccionPuesta? InsertarSeccion(string id, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<SeccionPuesta?>(() =>
            {
                object? br;

                try
                {
                    br = (object?)_ms.InsertBlock(new[] { x, y, 0d }, id, 1d, 1d, 1d, 0d);
                }
                catch (Exception)
                {
                    // El bloque no existe: la sección no se ha dibujado todavía.
                    _notas.Add(
                        $"Alzado '{id}': no hay bloque de sección con ese nombre, así que " +
                        "el alzado va sin su CORTE A-A'. Dibuja primero las secciones.");
                    return null;
                }

                if (br is null)
                {
                    return null;
                }

                var caja = Caja(br);
                if (caja is null)
                {
                    return null;
                }

                var mn = caja.Value.Min;
                var mx = caja.Value.Max;

                // Borde izquierdo en x y paño INFERIOR en y.
                Mover(br, x - mn[0], y - mn[1]);

                var ancho = mx[0] - mn[0];
                var alto = mx[1] - mn[1];

                RotuloCorte(x + (ancho / 2), y + alto);

                // Y aquí se avisa de que el bloque ya está en su sitio, para que quien
                // sepa hacerlo le vuelva a poner sus llamadas. Va DESPUÉS del Mover:
                // antes, la esquina del bloque todavía no está en (x, y).
                try
                {
                    TrasInsertarSeccion?.Invoke(id, x, y);
                }
                catch (Exception ex)
                {
                    // Las llamadas son rotulado: el corte ya está insertado y medido.
                    Fallo($"Llamadas del corte '{id}'", ex);
                }

                return new SeccionPuesta { Ancho = ancho, Tope = y + alto };
            });
        }
        catch (Exception ex)
        {
            Fallo($"Insertar la sección '{id}' junto a su alzado", ex);
            return null;
        }
    }

    /// <summary>Mueve una entidad. Es el <c>br.Move</c> de la macro.</summary>
    private static void Mover(object ent, double dx, double dy)
    {
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return;
        }

        dynamic e = ent;
        e.Move(new[] { 0d, 0d, 0d }, new[] { dx, dy, 0d });
        e.Update();
    }

    /// <summary>El <c>CORTE A-A'</c> encima del bloque de la sección.</summary>
    private void RotuloCorte(double xCentro, double yTope)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                var punto = new[] { xCentro, yTope + (0.15 * _f), 0d };

                dynamic t = _ms.AddText("CORTE A-A'", punto, 0.025 * _f);
                t.StyleName = "SECCIONES";
                t.Alignment = 10;              // acAlignmentBottomCenter
                t.TextAlignmentPoint = punto;
                t.Layer = "ROTULOS";
                t.Color = 3;                   // verde, como la macro
                t.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo("Rótulo CORTE A-A' de la sección", ex);
        }
    }

    /// <summary>Longitud del elemento: la columna W o, si viene vacía, la calculada.</summary>
    private static double LargoDe(AlzadoCad a)
    {
        if (a.LongitudM > 0)
        {
            return a.LongitudM;
        }

        // Un dado, si no se indica, mide 1 m; una columna 3 m. Es lo que hace la macro.
        if (a.EsVertical)
        {
            return a.Tipo == TipoElemento.Dado ? 1.0 : 3.0;
        }

        var s = a.SeparacionesCm;
        return Estribos.LongitudFlexible(s[0] / 100, s[1] / 100, s[2] / 100);
    }

    // ==================================================================
    // Alzado horizontal: trabes y contratrabes
    // ==================================================================

    private double DibujarHorizontal(AlzadoCad a, double x, double y, double largo)
    {
        var nombre = NombreUnico("ALZ-" + a.Id);

        var bloque = DefinicionDeBloque(nombre);
        if (bloque is null)
        {
            return 0;
        }

        var alto = a.AlturaCm * _escala;

        var geo = Geometria(bloque, a, largo, alto, girar: false);

        InsertarBloque(nombre, x, y);
        AnotarHorizontal(a, x, y, largo, alto, geo);

        return largo + 0.16;
    }

    // ==================================================================
    // Alzado vertical: columnas y dados
    // ==================================================================

    private double DibujarVertical(AlzadoCad a, double x, double y, double largo)
    {
        // Una columna rectangular lleva DOS alzados, uno por cara: el de ancho igual
        // a la base y el de ancho igual al peralte.
        var ancho1 = a.BaseCm > 0 ? a.BaseCm : a.AlturaCm;
        var ancho2 = a.BaseCm > 0 && Math.Abs(a.BaseCm - a.AlturaCm) > 1e-4 ? a.AlturaCm : 0;

        var nombre1 = NombreUnico((ancho2 > 0 ? "ALZX-" : "ALZ-") + a.Id);

        var b1 = DefinicionDeBloque(nombre1);
        if (b1 is null)
        {
            return 0;
        }

        var geo1 = Geometria(b1, a, largo, ancho1 * _escala, girar: true);
        InsertarBloque(nombre1, x, y);
        AnotarVertical(a, x, y, largo, ancho1 * _escala, geo1, conRotulo: true);

        if (ancho2 > 0)
        {
            var nombre2 = NombreUnico("ALZY-" + a.Id);
            var b2 = DefinicionDeBloque(nombre2);

            if (b2 is not null)
            {
                // La segunda cara va por encima del paño superior de la primera. El
                // cálculo vive en el layout, que es quien reserva el sitio.
                var y2 = AlzadoLayout.YSegundaCara(y, largo);

                var geo2 = Geometria(b2, a, largo, ancho2 * _escala, girar: true);
                InsertarBloque(nombre2, x, y2);
                AnotarVertical(a, x, y2, largo, ancho2 * _escala, geo2, conRotulo: true);
            }
        }

        return 0.24 + 0.09 + 0.1;
    }

    // ==================================================================
    // Geometría, dentro de la definición del bloque
    // ==================================================================

    /// <summary>Datos de la geometría que después necesitan las cotas.</summary>
    private sealed class Geo
    {
        public double DSup;
        public double DInf;
        public double YcSup;
        public double YcInf;
        public double Xa;
        public double Xb;
        public double XaInf;
        public double XbInf;
        public double GanchoSup;
        public double GanchoInf;
        public List<double> Centros = new();
    }

    /// <summary>
    /// Dibuja la geometría del alzado en la definición del bloque, con la esquina
    /// inferior izquierda en el origen.
    /// </summary>
    /// <param name="girar">
    /// Si al final se gira 90° alrededor del origen, que es lo que convierte el
    /// alzado horizontal en vertical.
    /// </param>
    private Geo Geometria(object bloque, AlzadoCad a, double largo, double ancho, bool girar)
    {
        _hatchConcreto.Clear();
        _fillVarillas.Clear();
        _fillEstribos.Clear();
        _zunchoMacizo = null;

        var relleno = a.Modo == ModoSeccion.Tipo2Rellena;

        var inicio = (int)AcadConnection.Retry(() => (int)((dynamic)bloque).Count);

        var x0 = 0d;
        var y0 = 0d;
        var x1 = largo;
        var y1 = ancho;
        var rec = a.RecubrimientoCm * _escala;

        // ---------- Concreto ----------
        var plConc = RectCerrado(bloque, x0, y0, x1, y1, "CONCRETO");
        HatchDeConcreto(bloque, plConc, relleno);

        var dSup = a.Superior.Esquina.Cm * _escala;
        var dInf = a.Inferior.Esquina.Cm * _escala;
        if (dSup <= 0) { dSup = 0.0095 * _f; }
        if (dInf <= 0) { dInf = 0.0095 * _f; }

        var ycSup = y1 - rec - (dSup / 2);
        var ycInf = y0 + rec + (dInf / 2);

        // ---------- Estribos ----------
        var s = a.SeparacionesCm;

        // En el alzado horizontal se ponen estribos en las fronteras de zona; en el
        // vertical no. Es la diferencia entre las dos llamadas de la macro.
        // Una sola función para el dibujo y para la vista previa: si cada uno
        // aplicara las reglas del elemento por su cuenta, tarde o temprano una de
        // las dos se queda atrás y el usuario ve un número de estribos en la
        // pantalla y otro en AutoCAD.
        var centros = Estribos.CentrosDeAlzado(
            x1 - x0, s[0] / 100, s[1] / 100, s[2] / 100,
            vertical: girar,
            esColumna: a.Tipo == TipoElemento.Columna);

        // CentrosDeAlzado trabaja desde 0; aquí se corren al sitio del dibujo.
        for (var i = 0; i < centros.Count; i++)
        {
            centros[i] += x0;
        }

        var dEst = a.EstriboDibujo.Cm * _escala;
        if (dEst <= 0) { dEst = 0.0095 * _f; }

        // ---------- Zuncho helicoidal, o estribos/anillos ----------
        // La hélice NO es una cápsula repetida: es una sola pieza continua, y en el
        // alzado se ve como un resorte. Lo elige el usuario, no el programa.
        //
        // Se muestrea UNA vez y se reutiliza: la usan el dibujo del zuncho y, después,
        // el recorte de las varillas que pasan por detrás. Si cada uno la calculara por
        // su cuenta, los cortes caerían donde no está dibujada.
        Helice? helice = null;

        if (a.Circular && a.ZunchoHelicoidal)
        {
            helice = MuestrearHelice(a, x0, x1, y0, y1, rec, dEst);

            if (helice is not null)
            {
                HeliceDelZuncho(bloque, a, helice, dEst, relleno);
            }
        }
        else
        {
            // El anillo de un zuncho normal se proyecta EXACTAMENTE como la cápsula
            // del estribo rectangular: un rectángulo de ancho igual al diámetro del
            // anillo y alto igual al de la barra. Así que aquí no hace falta
            // geometría nueva, y se reutiliza la que ya está probada.
            CapsulasDeEstribo(bloque, centros, y0, y1, rec, dEst, relleno);
        }

        // ---------- Corrección de los ejes cuando la sección es CIRCULAR ----------
        // En la redonda las varillas no van pegadas al recubrimiento: van sobre el
        // círculo de paso, que además está dentro del zuncho. Así que hay que restar
        // el diámetro del zuncho, y usar la varilla del círculo y no la de un lecho
        // que en circular está vacío. Sin esto las varillas del alzado saldrían un
        // diámetro de zuncho más afuera que en la sección, y las dos vistas del mismo
        // elemento no coincidirían.
        if (a.Circular)
        {
            var dv = a.VarTotal.Cm * _escala;

            if (dv > 0)
            {
                dSup = dv;
                dInf = dv;
            }

            ycSup = y1 - rec - dEst - (dSup / 2);
            ycInf = y0 + rec + dEst + (dInf / 2);
        }

        var xa = x0 + rec;
        var xb = x1 - rec;

        // ---------- Ganchos ----------
        double gSup = 0, gInf = 0;

        if (a.GanchoCm > 0)
        {
            // En la trabe el gancho es 12 diámetros; en la columna, el valor de la
            // columna T. Son las dos ramas de la macro, y viven en Estribos para que
            // la vista previa dibuje el gancho con la MISMA regla.
            gSup = Estribos.GanchoEfectivo(
                Estribos.GanchoNominal(girar, a.GanchoCm * _escala, dSup),
                ycSup - (dSup / 2) - (y0 + rec),
                dSup);

            gInf = Estribos.GanchoEfectivo(
                Estribos.GanchoNominal(girar, a.GanchoCm * _escala, dInf),
                y1 - rec - (ycInf + (dInf / 2)),
                dInf);
        }

        var xaInf = xa;
        var xbInf = xb;

        // Si los dos ganchos se cruzan, el inferior se recorre para no chocar, y se
        // corre además para no caer sobre un estribo.
        if (gSup > 0 && gInf > 0)
        {
            var puntaSup = ycSup - (dSup / 2) - gSup;
            var puntaInf = ycInf + (dInf / 2) + gInf;

            if (puntaInf > puntaSup - HookClearV)
            {
                var baseL = xa + dSup + HookClearH;
                var baseR = xb - dSup - HookClearH;
                var limL = xa + ((xb - xa) * 0.1);
                var limR = xb - ((xb - xa) * 0.1);

                var candL = CorrerADerecha(baseL, dInf, centros, dEst / 2, HookClearEst);
                if (candL > limL) { candL = baseL; }

                var candR = CorrerAIzquierda(baseR, dInf, centros, dEst / 2, HookClearEst);
                if (candR < limR) { candR = baseR; }

                xaInf = candL;
                xbInf = candR;
            }
        }

        // ---------- Varillas ----------
        if (a.Circular)
        {
            // En la redonda el armado NO está por lechos, así que las tres llamadas
            // de abajo no dibujarían nada: sus lechos vienen vacíos. Las varillas se
            // proyectan desde el círculo.
            VarillasCirculares(bloque, a, xa, xb, xaInf, xbInf,
                (y0 + y1) / 2, ycSup, ycInf, dSup, gSup, gInf, centros, dEst, relleno,
                helice);
        }
        else
        {
            VarillaConGanchos(bloque, xa, xb, ycSup, dSup,
                CapaVar(a.Superior.Esquina.Clave), centros, dEst, gSup, hacia: false, relleno);

            VarillaConGanchos(bloque, xaInf, xbInf, ycInf, dInf,
                CapaVar(a.Inferior.Esquina.Clave), centros, dEst, gInf, hacia: true, relleno);

            Intermedias(bloque, a, xa, xb, xaInf, xbInf, ycSup, ycInf,
                dSup, dInf, gSup, gInf, centros, dEst, relleno);
        }

        // ---------- Color y orden ----------
        if (relleno)
        {
            ContornosNegros(bloque, inicio);

            // OJO AL ORDEN: va DESPUÉS de ContornosNegros a propósito. El zuncho macizo
            // es una polilínea con ancho, no un hatch, así que el repintado lo deja
            // negro; aquí se le devuelve su color de estribo.
            ColorDelZuncho();
        }

        OrdenarRellenos(bloque);

        // El zuncho helicoidal, al FRENTE, y solo en la sección rellena.
        //
        // Va DESPUÉS de OrdenarRellenos porque ese método manda al fondo los rellenos
        // de estribo, y el zuncho macizo es una polilínea con ancho que cuenta como
        // relleno: si se subiera antes, OrdenarRellenos lo volvería a hundir.
        //
        // Solo en la rellena porque es donde estorba: ahí el concreto y las varillas
        // llevan hatch sólido, y la hélice queda tapada por ellos justo en los tramos
        // en que pasa por delante. En la sección de contorno no hay nada opaco que la
        // tape, así que subirla no cambiaría nada.
        if (relleno)
        {
            ZunchoAlFrente(bloque);
        }

        if (girar)
        {
            Girar90(bloque, inicio);
        }

        // El rótulo NO se dibuja aquí: va en el espacio modelo, debajo del bloque ya
        // insertado. Ver RotuloDelElemento.

        return new Geo
        {
            DSup = dSup, DInf = dInf,
            YcSup = ycSup, YcInf = ycInf,
            Xa = xa, Xb = xb, XaInf = xaInf, XbInf = xbInf,
            GanchoSup = gSup, GanchoInf = gInf,
            Centros = centros
        };
    }

    // ==================================================================
    // Rótulo del bloque de alzado
    // ==================================================================

    /// <summary>Separación entre el alzado y su rótulo. Es el <c>ROTULO_GAP</c>.</summary>
    private const double RotuloGap = 0.05;

    /// <summary>
    /// Los renglones del rótulo del alzado: qué elemento es y con qué va armado.
    /// </summary>
    /// <remarks>
    /// Está separado de su colocación porque el texto es el mismo en el alzado
    /// horizontal y en el vertical, y lo único que cambia es dónde se escribe.
    /// </remarks>
    private List<string> LineasDelRotulo(AlzadoCad a)
    {
        var lineas = new List<string>();

        if (!string.IsNullOrWhiteSpace(a.Elemento))
        {
            // Ya viene con el nombre de rótulo: una columna redonda dice COLUMNA.
            lineas.Add(a.Elemento.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(a.Id))
        {
            lineas.Add("\"" + a.Id + "\"");
        }

        // Armado longitudinal, con el mismo texto que las cotas del alzado
        if (a.Circular)
        {
            var t = TextoCirculo(a);
            if (t != "---") { lineas.Add(t); }
        }
        else
        {
            var sup = TextoLecho(a.Superior, "Superiores");
            var inf = TextoLecho(a.Inferior, "Inferiores");
            var lat = TextoSimple(a.NLateral * 2, a.Lateral, "Intermedias");

            if (sup != "---") { lineas.Add(sup); }
            if (lat != "---") { lineas.Add(lat); }
            if (inf != "---") { lineas.Add(inf); }
        }

        // Acero transversal. Se usa la separación TAL COMO se capturó —«10-20-20»— y no
        // solo la de una zona, porque el rótulo describe el elemento entero.
        if (a.Estribo.Existe)
        {
            var sep = string.IsNullOrWhiteSpace(a.Separacion)
                ? a.SeparacionesCm[0].ToString("0", CultureInfo.InvariantCulture)
                : a.Separacion.Trim();

            lineas.Add(a.Circular
                ? $"Zuncho {(a.ZunchoHelicoidal ? "helic." : "anillos")} " +
                  $"{Etiqueta(a.Estribo.Clave)} @ {sep} cm"
                : $"Est. {Etiqueta(a.Estribo.Clave)} @ {sep} cm");
        }

        lineas.Add($"Rec. {a.RecubrimientoCm:0.#} cm");

        if (!string.IsNullOrWhiteSpace(a.Fc))
        {
            lineas.Add($"f'c={a.Fc} kg/cm\u00B2");
        }

        if (!string.IsNullOrWhiteSpace(a.Escala))
        {
            lineas.Add($"Escala 1:{a.Escala}");
        }

        return lineas;
    }

    /// <summary>
    /// El rótulo del elemento, en el <b>espacio modelo</b> y colgando del
    /// <b>bloque de la sección</b>, que es el que se inserta a un lado o debajo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aquí hubo dos malentendidos seguidos, y conviene dejarlos escritos.</b>
    /// </para>
    /// <para>
    /// Primero el rótulo se metía <b>dentro</b> de la definición del bloque de alzado.
    /// Eso lo dibujaba en coordenadas del bloque, así que caía pegado al pie de la
    /// geometría, por encima de las cotas que el espacio modelo pone después, y en el
    /// alzado vertical el giro de 90° lo dejaba tumbado.
    /// </para>
    /// <para>
    /// Al sacarlo se colgó del <b>bloque del alzado</b>, porque «el bloque insertado»
    /// se leyó como ése. Tampoco era: en el módulo de alzados se insertan <b>dos</b>
    /// bloques, el del alzado y el de la sección, y el que el usuario quería es el de
    /// la <b>sección</b> —el del <c>CORTE A-A'</c>—, con el rótulo debajo de él. Lo
    /// aclaró con dos capturas: el rótulo va bajo el corte, a la izquierda, y no
    /// centrado bajo el alzado.
    /// </para>
    /// <para>
    /// Y tiene sentido de plano: el rótulo describe el <b>elemento</b>, no una de sus
    /// vistas. Debajo del corte hay <b>uno solo</b> por elemento, aunque una columna
    /// rectangular lleve dos alzados; colgado del alzado salían dos rótulos iguales.
    /// Además el pie del alzado ya está ocupado por sus cotas de estribos, las
    /// etiquetas de zona, el título y la escala, y el rótulo tenía que bajar mucho para
    /// esquivarlos.
    /// </para>
    /// </remarks>
    /// <param name="xSeccion">Borde izquierdo del bloque de la sección.</param>
    /// <param name="yAbajo">Paño inferior del bloque de la sección.</param>
    /// <param name="anchoSeccion">Ancho medido del bloque de la sección.</param>
    private void RotuloDelElemento(
        AlzadoCad a, double xSeccion, double yAbajo, double anchoSeccion)
    {
        var lineas = LineasDelRotulo(a);

        if (lineas.Count == 0)
        {
            return;
        }

        // Centrado bajo el bloque de la SECCIÓN, y colgando de su paño inferior. Es el
        // mismo sitio en los dos tipos de elemento, porque la sección se apoya en la Y
        // de la fila tanto en la trabe como en la columna.
        var xCentro = xSeccion + (anchoSeccion / 2);
        var yPie = yAbajo - (RotuloGap * _f);

        TextoRotulo(xCentro, yPie, string.Join("\\P", lineas));
    }

    /// <summary>
    /// Hasta dónde baja lo que se dibuja <b>debajo del alzado horizontal</b>.
    /// </summary>
    /// <remarks>
    /// La escala es lo más bajo: el título va en <c>y − 0.23</c>, la escala 0.064 por
    /// debajo, y su propio texto baja otros 0.0225, así que el pie queda en
    /// <c>y − 0.3165</c>. Se redondea a 0.36 para dejar aire entre la escala y el
    /// rótulo. Si se toca <see cref="Titulo"/>, hay que revisar este número.
    /// </remarks>
    private const double PieAnotacionHorizontal = 0.36;

    /// <summary>Altura del texto del rótulo del alzado.</summary>
    /// <remarks>
    /// El mismo <c>H_TX_ROTULO</c> de la macro, para que el rótulo del alzado y el de la
    /// sección se lean del mismo tamaño cuando quedan uno al lado del otro.
    /// </remarks>
    private const double AlturaRotulo = 0.025;

    /// <summary>
    /// El texto del rótulo, en la capa ROTULOS y anclado por <b>arriba y al centro</b>.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="Texto"/> por tres cosas: la capa —ROTULOS, no TEXTOS—, el
    /// anclaje, que aquí tiene que ser <c>TopCenter</c> para que el bloque de renglones
    /// cuelgue centrado bajo el alzado, y que la <c>InsertionPoint</c> se vuelve a
    /// escribir después de fijar el anclaje: AutoCAD recoloca el MText al cambiarle el
    /// <c>AttachmentPoint</c>, y sin repetirla el rótulo se desplaza.
    /// </remarks>
    private void TextoRotulo(double x, double y, string texto)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { x, y, 0d }, 0d, texto);
                mt.StyleName = EstiloTexto;
                mt.Height = AlturaRotulo * _f;
                mt.AttachmentPoint = 2;            // 2 = TopCenter
                mt.InsertionPoint = new[] { x, y, 0d };
                mt.Width = 0;
                mt.Layer = "ROTULOS";
                mt.Color = ColorVerde;
                mt.Update();
            });
        }
        catch (Exception ex)
        {
            // Sin rotulo el alzado sigue siendo valido.
            Fallo("Rótulo del alzado", ex);
        }
    }

    // ==================================================================
    // Varillas de la columna circular en alzado
    // ==================================================================

    /// <summary>
    /// Las varillas del círculo, <b>proyectadas</b> sobre el plano del alzado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Una varilla que en la sección está en el ángulo <c>α</c> se ve en el alzado a
    /// una distancia <c>r·cos(α)</c> del eje. Eso hace que las <b>parejas simétricas
    /// se proyecten en el mismo sitio</b>: de 8 varillas se ven 5 posiciones
    /// distintas, y de 4 se ven 3. Comprobado en
    /// <c>tools/verificar_seccion_circular.py</c>.
    /// </para>
    /// <para>
    /// Por eso se quitan las repetidas. Dibujarlas todas pondría dos varillas
    /// exactamente encima de otra: en pantalla no se nota, pero el archivo lleva
    /// entidades duplicadas y al editar una queda la de debajo, que es el tipo de
    /// defecto que aparece meses después.
    /// </para>
    /// <para>
    /// Las dos varillas de los extremos llevan el gancho, como las de esquina de una
    /// columna rectangular. Las de en medio no: quedan dentro y su gancho chocaría con
    /// los otros dos.
    /// </para>
    /// </remarks>
    /// <param name="helice">
    /// La hélice del zuncho, o <c>null</c> si el zuncho va en anillos. Cuando la hay, es
    /// ella la que decide dónde se recorta cada varilla.
    /// </param>
    private void VarillasCirculares(
        object bloque, AlzadoCad a,
        double xa, double xb, double xaInf, double xbInf,
        double yMedio, double ycSup, double ycInf, double dVar,
        double gSup, double gInf, List<double> centros, double dEst, bool relleno,
        Helice? helice)
    {
        if (a.NVarTotal <= 0 || dVar <= 0)
        {
            return;
        }

        // El radio del círculo de paso sale de los ejes que ya se corrigieron
        var rPaso = ycSup - yMedio;

        if (rPaso <= 0)
        {
            return;
        }

        var capa = CapaVar(a.VarTotal.Existe ? a.VarTotal.Clave : a.EstriboDibujo.Clave);

        // Posiciones proyectadas, sin repetidas
        var ys = new List<double>();

        for (var i = 0; i < a.NVarTotal; i++)
        {
            var ang = (Math.PI / 2) + (i * 2 * Math.PI / a.NVarTotal);
            var y = yMedio + (rPaso * Math.Cos(ang));

            // La tolerancia es un décimo del diámetro de la varilla: por debajo de
            // eso las dos varillas se dibujarían pisándose y no aportan nada.
            if (!ys.Any(v => Math.Abs(v - y) < dVar * 0.1))
            {
                ys.Add(y);
            }
        }

        ys.Sort();

        Nota(
            $"Alzado '{a.Id}': las {a.NVarTotal} varillas del círculo se ven en " +
            $"{ys.Count} posición(es) distinta(s) del alzado; las parejas simétricas se " +
            "proyectan una sobre otra.");

        var recortes = 0;

        for (var i = 0; i < ys.Count; i++)
        {
            var esPrimera = i == 0;
            var esUltima = i == ys.Count - 1;

            // Solo las dos de los extremos llevan gancho
            var gancho = esUltima ? gSup : esPrimera ? gInf : 0;

            // Y solo la de abajo usa el arranque corrido para no chocar con el otro
            var xIzq = esPrimera ? xaInf : xa;
            var xDer = esPrimera ? xbInf : xb;

            // ---------- Dónde se recorta esta varilla ----------
            // Con zuncho HELICOIDAL no hay anillos: lo que tapa la varilla son los
            // pasos del zuncho por delante de ella, y caen en un sitio distinto para
            // cada varilla. Pasarle los centros de los anillos, que es lo que se hacía
            // antes, recortaba donde no hay nada y dejaba sin recortar donde sí lo hay:
            // en el plano las varillas cruzaban la hélice de lado a lado.
            var cortes = helice is null
                ? centros
                : CrucesFrontales(helice, ys[i]);

            if (helice is not null)
            {
                recortes += cortes.Count;
            }

            VarillaConGanchos(
                bloque, xIzq, xDer, ys[i], dVar, capa, cortes, dEst, gancho,
                hacia: esPrimera, relleno);
        }

        if (helice is not null)
        {
            Nota(
                $"Alzado '{a.Id}': las varillas se recortaron en {recortes} paso(s) del " +
                "zuncho por delante de ellas. Los pasos por detrás no se recortan: ahí " +
                "es la varilla la que tapa al zuncho.");
        }
    }

    // ==================================================================
    // Zuncho helicoidal
    // ==================================================================

    /// <summary>Puntos por vuelta como mínimo, aunque la cuenta pida menos.</summary>
    private const int MinPuntosPorVuelta = 24;

    /// <summary>Puntos por vuelta como máximo, por si la cuenta se dispara.</summary>
    private const int MaxPuntosPorVuelta = 180;

    /// <summary>
    /// Cuánto se admite que la polilínea se aparte de la hélice real, en fracción del
    /// <b>diámetro del zuncho</b>.
    /// </summary>
    /// <remarks>
    /// Se mide contra el diámetro de la barra y no en milímetros absolutos porque es
    /// lo que decide si el defecto <b>se ve</b>: una desviación de medio milímetro es
    /// invisible en una barra del #8 y un escalón en una del #2.
    /// </remarks>
    private const double FlechaMaximaFraccion = 0.02;

    /// <summary>
    /// Cuántos puntos por vuelta hacen falta para que la hélice <b>no salga en picos</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El problema.</b> Con un número fijo de puntos por vuelta, la cresta del seno
    /// se muestrea demasiado grueso: ahí la curva gira sobre un radio pequeñísimo y la
    /// cuerda se la salta, así que el resorte sale con vértices en punta en lugar de
    /// curvas. Con 24 puntos la desviación en la cresta es el <b>18.5 %</b> del
    /// diámetro de la barra, que es perfectamente visible.
    /// </para>
    /// <para>
    /// <b>La cuenta.</b> La proyección es <c>y = A·sen(k·x)</c> con <c>A</c> el radio
    /// del eje del zuncho y <c>k = 2π/paso</c>. En la cresta el radio de curvatura es
    /// <c>1/(A·k²)</c>, y la flecha de una cuerda <c>c</c> sobre un arco de radio
    /// <c>R</c> es <c>c²/(8R)</c>. Con muestreo uniforme <c>c ≈ paso/N</c>, y al
    /// sustituir se cancela el paso:
    /// </para>
    /// <code>
    /// flecha = (paso/N)² · A · (2π/paso)² / 8 = A · π² / (2·N²)
    /// </code>
    /// <para>
    /// O sea que la flecha <b>no depende del paso</b>, solo del radio y del número de
    /// puntos. Pidiendo que sea como mucho una fracción <c>f</c> del diámetro:
    /// </para>
    /// <code>
    /// N ≥ π · √( A / (2·f·d) )
    /// </code>
    /// <para>
    /// Con un zuncho del #3 en una columna de 50 cm salen 73 puntos por vuelta, y la
    /// desviación baja del 18.5 % al 2 %. Comprobado en
    /// <c>tools/verificar_seccion_circular.py</c>.
    /// </para>
    /// </remarks>
    private static int PuntosPorVuelta(double rEje, double dZun)
    {
        if (rEje <= 0 || dZun <= 0)
        {
            return MinPuntosPorVuelta;
        }

        var n = (int)Math.Ceiling(
            Math.PI * Math.Sqrt(rEje / (2 * FlechaMaximaFraccion * dZun)));

        return Math.Clamp(n, MinPuntosPorVuelta, MaxPuntosPorVuelta);
    }

    /// <summary>Tope de puntos de la polilínea de la hélice.</summary>
    /// <remarks>
    /// El tope está para que una separación capturada por error —5 mm, por ejemplo— no
    /// genere una polilínea de cien mil vértices que deje AutoCAD inservible. Se subió
    /// de 4 000 a 12 000 al hacer el muestreo adaptativo: con 73 puntos por vuelta, el
    /// tope viejo recortaba la resolución en cuanto la columna pasaba de 55 vueltas y
    /// devolvía los picos justo en los elementos largos.
    /// </remarks>
    private const int MaxPuntosHelice = 12000;

    /// <summary>
    /// El zuncho helicoidal en alzado: la <b>proyección de la hélice</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La geometría, que es exacta y no una aproximación.</b> Una hélice de radio
    /// <c>r</c> y paso <c>p</c> alrededor del eje del elemento se proyecta sobre el
    /// plano del alzado como <c>desplazamiento = r · sen(2π·x/p)</c>: un seno de
    /// amplitud <c>r</c> y periodo <c>p</c>. Eso es la proyección, no un parecido.
    /// </para>
    /// <para>
    /// <b>El grosor también es exacto.</b> La barra tiene diámetro <c>d</c>, así que
    /// su superficie exterior es una hélice de radio <c>r + d/2</c> y la interior una
    /// de radio <c>r − d/2</c>. Se dibujan las dos, con la <b>misma fase</b> y
    /// amplitudes distintas. Ojo con la tentación de dibujar un solo seno y
    /// desplazarlo: eso daría un grosor constante medido en vertical, y el grosor real
    /// se estrecha donde la hélice cruza el eje, porque ahí la barra se ve de perfil.
    /// </para>
    /// <para>
    /// <b>El paso no es constante.</b> La tabla de separaciones da tres zonas
    /// L/4-L/2-L/4, y en una columna real el zuncho va más cerrado en los extremos.
    /// Así que en lugar de un seno de periodo fijo se acumula la <b>fase</b>:
    /// <c>Δφ = 2π·Δx / paso(x)</c>, con el paso de la zona en que cae cada tramo. Con
    /// separación única sale exactamente el seno de siempre.
    /// </para>
    /// </remarks>
    /// <param name="dZun">
    /// Diámetro del zuncho, en metros de dibujo. Sale de la <b>columna Estribo de la
    /// tabla</b>: es el grosor real de la barra, no un grosor de línea.
    /// </param>
    /// <param name="relleno">
    /// En modo sección rellena, el cuerpo del zuncho se rellena de color como los
    /// estribos. En modo sin relleno queda solo el contorno.
    /// </param>
    /// <summary>
    /// El recorrido de la hélice, muestreado. Lo comparten el dibujo y los recortes.
    /// </summary>
    /// <remarks>
    /// Vive aparte porque lo usan <b>dos</b> cosas: dibujar el zuncho y decidir dónde
    /// se recortan las varillas que pasan por detrás. Tienen que salir de la MISMA
    /// muestra, o los cortes caerían en un sitio y la hélice se dibujaría en otro.
    /// </remarks>
    private sealed class Helice
    {
        /// <summary>Radio del eje de la barra, desde el eje del elemento.</summary>
        public required double REje { get; init; }

        /// <summary>Elevación del eje del elemento.</summary>
        public required double YMedio { get; init; }

        public required double[] X { get; init; }

        /// <summary>Seno de la fase: el desplazamiento, en fracción del radio.</summary>
        public required double[] Sen { get; init; }

        /// <summary>
        /// Coseno de la fase: la <b>profundidad</b>. Positivo = la barra pasa por
        /// DELANTE del elemento, hacia quien mira.
        /// </summary>
        public required double[] Cos { get; init; }

        public required double Vueltas { get; init; }
        public required double P1 { get; init; }
        public required double P2 { get; init; }
        public required double P3 { get; init; }
    }

    /// <summary>Muestrea la hélice del zuncho, o <c>null</c> si no cabe.</summary>
    private Helice? MuestrearHelice(
        AlzadoCad a, double x0, double x1, double y0, double y1, double rec, double dZun)
    {
        var largo = x1 - x0;

        if (largo <= 0 || dZun <= 0)
        {
            return null;
        }

        // Radio del EJE de la barra del zuncho, medido desde el eje del elemento
        var rExt = ((y1 - y0) / 2) - rec;
        var rEje = rExt - (dZun / 2);

        if (rEje <= 0)
        {
            _log.Add(
                $"Alzado '{a.Id}': con diámetro {a.DiametroCm:0.#} cm y recubrimiento " +
                $"{a.RecubrimientoCm:0.#} cm no queda sitio para el zuncho helicoidal.");
            return null;
        }

        // Separaciones de las tres zonas, en metros de dibujo
        var s = a.SeparacionesCm;
        var p1 = Paso(s.Length > 0 ? s[0] : 0);
        var p2 = Paso(s.Length > 1 ? s[1] : 0, p1);
        var p3 = Paso(s.Length > 2 ? s[2] : 0, p1);

        // Fronteras de las zonas L/4 - L/2 - L/4
        var z1 = x0 + (largo * 0.25);
        var z2 = x0 + (largo * 0.75);

        double PasoEn(double x) => x < z1 ? p1 : x < z2 ? p2 : p3;

        // Vueltas totales: la integral de dx/paso(x) sobre el elemento
        var vueltas = ((z1 - x0) / p1) + ((z2 - z1) / p2) + ((x1 - z2) / p3);

        if (vueltas <= 0)
        {
            return null;
        }

        // Los puntos por vuelta se calculan a partir del radio y del calibre, para que
        // la cresta del seno no salga en pico. Ver PuntosPorVuelta.
        var porVuelta = PuntosPorVuelta(rEje, dZun);

        var n = (int)Math.Ceiling(vueltas * porVuelta);
        if (n < 8) { n = 8; }

        if (n > MaxPuntosHelice)
        {
            _log.Add(
                $"Alzado '{a.Id}': el zuncho helicoidal daría {vueltas:0} vueltas y una " +
                $"polilínea de más de {MaxPuntosHelice} puntos. Se dibujó con la " +
                "resolución máxima; revisa la separación capturada.");
            n = MaxPuntosHelice;
        }

        var dx = largo / n;

        var xs = new double[n + 1];
        var sen = new double[n + 1];
        var cos = new double[n + 1];

        var fase = 0d;

        for (var i = 0; i <= n; i++)
        {
            var x = x0 + (i * dx);

            if (i > 0)
            {
                // La fase se acumula con el paso de la zona donde cae el tramo. Se
                // evalúa en el punto MEDIO del tramo: en la frontera de zonas, tomar
                // el extremo daría medio tramo con el paso equivocado.
                fase += 2 * Math.PI * dx / PasoEn(x - (dx / 2));
            }

            xs[i] = x;
            sen[i] = Math.Sin(fase);
            cos[i] = Math.Cos(fase);
        }

        return new Helice
        {
            REje = rEje,
            YMedio = (y0 + y1) / 2,
            X = xs, Sen = sen, Cos = cos,
            Vueltas = vueltas, P1 = p1, P2 = p2, P3 = p3
        };
    }

    private void HeliceDelZuncho(
        object bloque, AlzadoCad a, Helice h, double dZun, bool relleno)
    {
        var vueltas = h.Vueltas;
        var p1 = h.P1;
        var p2 = h.P2;
        var p3 = h.P3;

        // ------------------------------------------------------------------
        // El EJE de la hélice, y el grosor por ANCHO DE POLILÍNEA
        // ------------------------------------------------------------------
        // Aquí hubo que descartar dos caminos, y conviene dejarlo escrito porque los
        // dos parecen correctos hasta que se hacen los números:
        //
        //   1. Dibujar las DOS CARAS, exterior e interior, como un contorno cerrado y
        //      rellenarlo con un hatch. NO FUNCIONA: las caras son R·sen(f) y r·sen(f)
        //      con R > r, así que donde sen(f) es negativo la exterior queda POR DEBAJO
        //      de la interior. La banda se cruza en cada paso por el eje —60 veces en
        //      una columna de 3 m con paso de 10 cm— el polígono no es simple y el área
        //      con signo sale EXACTAMENTE CERO. El hatch saldría vacío o corrupto.
        //
        //   2. Desplazar el eje ±d/2 por su NORMAL, que sí es la silueta geométrica
        //      correcta. Tampoco se puede rellenar: la pendiente del seno llega a 12.9
        //      (unos 86°) y su radio de curvatura en las crestas baja a 1.2 mm, menos
        //      que el medio diámetro de la barra (4.75 mm). La banda se cruza otra vez,
        //      ahora en las crestas.
        //
        // Los dos fallos los encontró tools/verificar_seccion_circular.py, no la
        // lectura del código.
        //
        // La vía que sí funciona para el zuncho MACIZO es la idiomática de AutoCAD:
        // UNA polilínea del eje con ANCHO CONSTANTE igual al diámetro del zuncho.
        // AutoCAD la dibuja como una banda maciza de ese ancho real en unidades de
        // dibujo, resuelve él las uniones entre tramos, y no hay ninguna frontera que
        // cerrar. Es una sola entidad y el grosor es el de la tabla.
        //
        // Pero eso vale SOLO para la sección rellena: una polilínea con ancho se
        // dibuja siempre maciza, no hay versión «solo contorno». En la sección SIN
        // relleno el acero va en contorno, como todo lo demás del dibujo, así que ahí
        // se dibujan las dos CARAS como polilíneas abiertas sin ancho. Abiertas no da
        // ningún problema: el problema de las dos caras era cerrarlas para rellenar,
        // y aquí no se rellena nada.
        if (relleno)
        {
            HeliceMaciza(bloque, a, h, dZun);
        }
        else
        {
            HeliceEnContorno(bloque, a, h, dZun);
        }

        Nota(
            $"Alzado '{a.Id}': zuncho HELICOIDAL de {dZun / _escala:0.##} cm, " +
            $"{vueltas:0.#} vuelta(s) con paso " +
            $"{p1 / _escala:0.#}-{p2 / _escala:0.#}-{p3 / _escala:0.#} cm. " +
            "Si lo querías en anillos, quita el SI de la columna «Zuncho helic.».");
    }

    /// <summary>
    /// Dónde el zuncho pasa <b>por delante</b> de una varilla y por tanto la tapa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué solo los de delante.</b> El zuncho cruza la posición proyectada de
    /// una varilla <b>dos veces por vuelta</b>: una con la barra hacia el observador y
    /// otra con la barra al otro lado del elemento. Solo la primera la tapa; en la
    /// segunda es la varilla la que tapa al zuncho. Recortar en todos los cruces
    /// partiría la varilla en el doble de trozos de los que toca, y encima dejaría
    /// huecos donde debería verse entera.
    /// </para>
    /// <para>
    /// El criterio es el <b>coseno de la fase</b>, que es la profundidad de la hélice:
    /// positivo significa hacia quien mira. Comprobado con números para 8 varillas: de
    /// los 60 cruces de una columna de 3 m con paso de 10 cm, 30 son por delante.
    /// </para>
    /// <para>
    /// Los cruces se buscan sobre la <b>misma muestra</b> con la que se dibuja la
    /// hélice, detectando el cambio de signo de <c>(r·sen φ − y)</c>. Así el corte cae
    /// exactamente donde está la línea dibujada y no un poco antes.
    /// </para>
    /// </remarks>
    /// <param name="yBarra">Elevación de la varilla en el alzado.</param>
    private static List<double> CrucesFrontales(Helice h, double yBarra)
    {
        var cruces = new List<double>();

        // Desplazamiento de la varilla respecto al eje del elemento
        var objetivo = yBarra - h.YMedio;

        if (Math.Abs(objetivo) > h.REje)
        {
            // La varilla está más afuera que el zuncho: nunca se cruzan.
            return cruces;
        }

        for (var i = 0; i + 1 < h.X.Length; i++)
        {
            var d0 = (h.REje * h.Sen[i]) - objetivo;
            var d1 = (h.REje * h.Sen[i + 1]) - objetivo;

            // Cambio de signo = hay un cruce en este tramo
            if (d0 == 0 || (d0 < 0) == (d1 < 0))
            {
                continue;
            }

            // Interpolación lineal dentro del tramo
            var t = d0 / (d0 - d1);
            var x = h.X[i] + (t * (h.X[i + 1] - h.X[i]));

            // La profundidad en el punto del cruce. Se interpola igual que la X para
            // no equivocarse justo en los tramos donde el coseno cambia de signo.
            var c = h.Cos[i] + (t * (h.Cos[i + 1] - h.Cos[i]));

            if (c > 0)
            {
                cruces.Add(x);
            }
        }

        cruces.Sort();
        return cruces;
    }

    /// <summary>
    /// El zuncho <b>macizo</b>: una polilínea del eje con el ancho de la barra.
    /// </summary>
    /// <remarks>
    /// Solo para la sección rellena. Se pinta del mismo color que el cuerpo de los
    /// estribos (ACI 152): es el mismo acero transversal y tiene que leerse igual.
    /// </remarks>
    private void HeliceMaciza(object bloque, AlzadoCad a, Helice h, double dZun)
    {
        var n = h.X.Length - 1;
        var pts = new double[(n + 1) * 2];

        for (var i = 0; i <= n; i++)
        {
            pts[2 * i] = h.X[i];
            pts[(2 * i) + 1] = h.YMedio + (h.REje * h.Sen[i]);
        }

        var pl = Poli(bloque, pts, "ESTRIBOS", cerrada: false, bulges: null);

        if (pl is null)
        {
            _log.Add($"Alzado '{a.Id}': no se pudo dibujar el zuncho helicoidal.");
            return;
        }

        if (!AnchoDePolilinea(pl, dZun))
        {
            Nota(
                $"Alzado '{a.Id}': no se pudo dar grosor al zuncho helicoidal, así que " +
                "queda como una línea. Es el aspecto clásico de un zuncho en un plano, " +
                "pero no muestra el diámetro de la barra.");
        }

        // Se guarda para repintarlo DESPUÉS de ContornosNegros, que si no se lo lleva
        // por delante. Ver _zunchoMacizo.
        _zunchoMacizo = pl;

        ColorDelZuncho();
    }

    /// <summary>
    /// Sube el zuncho helicoidal macizo al frente del orden de dibujo.
    /// </summary>
    /// <remarks>
    /// La hélice es lo único del alzado que <b>pasa por delante y por detrás</b> de las
    /// varillas a lo largo de su recorrido, y en la sección rellena las varillas y el
    /// concreto son opacos, así que la tapaban por tramos y el resorte se leía a trozos.
    /// Subirla entera al frente es lo que se hace a mano en AutoCAD con
    /// <i>bring to front</i>.
    /// <para>
    /// No falsea el dibujo: las varillas ya se <b>recortan</b> donde el zuncho pasa por
    /// delante —ver <c>CrucesFrontales</c>— así que en los tramos en que debería ganar la
    /// varilla no hay zuncho que la tape.
    /// </para>
    /// </remarks>
    private void ZunchoAlFrente(object bloque)
    {
        if (_zunchoMacizo is null)
        {
            return;
        }

        AlFrente(bloque, new List<object> { _zunchoMacizo });
    }

    /// <summary>
    /// Le pone al zuncho macizo su color de estribo (ACI 152).
    /// </summary>
    /// <remarks>
    /// Se llama <b>dos veces</b>: al dibujarlo y otra vez después de
    /// <see cref="ContornosNegros"/>. La primera para que el color esté puesto aunque el
    /// repintado falle, y la segunda porque el repintado lo deja negro. Es idempotente,
    /// así que llamarla dos veces no cuesta nada.
    /// </remarks>
    private void ColorDelZuncho()
    {
        if (_zunchoMacizo is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                ((dynamic)_zunchoMacizo).Color = ColorRellenoEstribo;
            });
        }
        catch (Exception ex)
        {
            Fallo("Color del zuncho helicoidal", ex);
        }
    }

    /// <summary>
    /// El zuncho <b>en contorno</b>: la silueta de la barra, con su ancho real.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Para la sección sin relleno, donde todo el acero va en contorno. La barra tiene
    /// que <b>leerse con el ancho de la varilla</b> en todo su recorrido, igual que
    /// cualquier otra barra del dibujo, que se dibuja con sus dos caras.
    /// </para>
    /// <para>
    /// <b>Por qué no valen las amplitudes r ± d/2.</b> Es lo que se hacía antes: dos
    /// senos de amplitud <c>r + d/2</c> y <c>r − d/2</c>. Parece razonable —son las
    /// proyecciones de las hélices exterior e interior de la barra— pero esas dos curvas
    /// <b>no son la silueta</b>. Las dos valen cero donde <c>sen φ = 0</c>, así que el
    /// zuncho se <b>estrangula hasta 0 mm</b> en cada cruce por el eje: sesenta veces en
    /// una columna de 3 m con paso de 10 cm.
    /// </para>
    /// <para>
    /// Y estrangularse ahí es justo lo contrario de lo que toca. La profundidad de la
    /// hélice va con <c>cos φ</c>, de modo que en el cruce por el eje
    /// (<c>φ = 0</c>) su velocidad en profundidad es <c>−r·sen φ = 0</c>: la barra se
    /// mueve <b>dentro</b> del plano del dibujo y se ve en toda su anchura, no de
    /// perfil. El ancho aparente es <c>d</c> a lo largo de toda la vuelta.
    /// </para>
    /// <para>
    /// <b>Lo que sí es la silueta.</b> Desplazar el eje proyectado <c>± d/2</c> por su
    /// <b>normal</b>. Da ancho constante <c>d</c>, que es lo correcto. El problema
    /// conocido de este camino es que en las crestas el radio de curvatura baja a 1.2 mm,
    /// menos que el medio diámetro de la barra (4.76 mm), y la curva desplazada
    /// <b>se riza</b>. Por eso no se puede rellenar, y por eso se descartó para el
    /// zuncho macizo.
    /// </para>
    /// <para>
    /// Pero aquí no hay que rellenar nada, así que el rizo se puede quitar: es un tramo
    /// en el que la curva desplazada <b>retrocede en X</b>, y colapsarlo dejando la X
    /// que no retroceda equivale a quedarse con la envolvente exterior, que es la
    /// silueta de verdad de un tubo. Ver <see cref="SinRizos"/>.
    /// </para>
    /// </remarks>
    private void HeliceEnContorno(object bloque, AlzadoCad a, Helice h, double dZun)
    {
        var n = h.X.Length - 1;
        var w = dZun / 2;

        // El eje proyectado
        var xc = new double[n + 1];
        var yc = new double[n + 1];

        for (var i = 0; i <= n; i++)
        {
            xc[i] = h.X[i];
            yc[i] = h.YMedio + (h.REje * h.Sen[i]);
        }

        // Las dos caras: el eje desplazado +-d/2 por su normal.
        //
        // La tangente se saca por diferencias centradas sobre los propios puntos
        // muestreados, en vez de derivando el seno a mano. Así no hay que volver a
        // deducir en qué zona de paso cae cada punto —la fase ya viene acumulada con el
        // paso correcto— y el muestreo es lo bastante fino para que la diferencia
        // centrada sea precisa.
        var caraA = new double[(n + 1) * 2];
        var caraB = new double[(n + 1) * 2];

        for (var i = 0; i <= n; i++)
        {
            var iAnt = i > 0 ? i - 1 : i;
            var iSig = i < n ? i + 1 : i;

            var tx = xc[iSig] - xc[iAnt];
            var ty = yc[iSig] - yc[iAnt];

            var m = Math.Sqrt((tx * tx) + (ty * ty));

            if (m <= 0)
            {
                tx = 1;
                ty = 0;
                m = 1;
            }

            // Normal unitaria: la tangente girada 90°
            var nx = -ty / m;
            var ny = tx / m;

            caraA[2 * i] = xc[i] + (w * nx);
            caraA[(2 * i) + 1] = yc[i] + (w * ny);

            caraB[2 * i] = xc[i] - (w * nx);
            caraB[(2 * i) + 1] = yc[i] - (w * ny);
        }

        // Las tapas se sacan de las caras SIN recortar, para que sigan estando en los
        // extremos exactos del eje aunque el filtro de rizos quite puntos.
        var tapaIni = new[] { caraA[0], caraA[1], caraB[0], caraB[1] };
        var tapaFin = new[]
        {
            caraA[2 * n], caraA[(2 * n) + 1],
            caraB[2 * n], caraB[(2 * n) + 1]
        };

        caraA = SinRizos(caraA);
        caraB = SinRizos(caraB);

        var dibujadas = 0;

        foreach (var cara in new[] { caraA, caraB })
        {
            if (Poli(bloque, cara, "ESTRIBOS", cerrada: false, bulges: null) is not null)
            {
                dibujadas++;
            }
        }

        if (dibujadas == 0)
        {
            _log.Add($"Alzado '{a.Id}': no se pudo dibujar el zuncho helicoidal.");
            return;
        }

        if (dibujadas == 1)
        {
            Nota(
                $"Alzado '{a.Id}': el zuncho helicoidal salió con una sola cara, así " +
                "que no se le ve el grosor de la barra.");
            return;
        }

        // Las TAPAS de los extremos: sin ellas las dos caras quedan como dos curvas
        // sueltas que arrancan y mueren en el aire. Con ellas el zuncho se lee como una
        // barra con su ancho, igual que el resto del acero en contorno.
        foreach (var t in new[] { tapaIni, tapaFin })
        {
            Linea(bloque, t[0], t[1], t[2], t[3], "ESTRIBOS");
        }
    }

    /// <summary>
    /// Colapsa los <b>rizos</b> de una curva desplazada, dejándola sin retrocesos en X.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Al desplazar una curva por su normal una distancia mayor que su radio de
    /// curvatura, el lado cóncavo se pasa de largo y forma un rizo: un lacito que en el
    /// plano se ve como un nudo en cada cresta del zuncho.
    /// </para>
    /// <para>
    /// La silueta que se busca es la <b>envolvente exterior</b>, o sea la curva
    /// desplazada quitándole esos rizos. El rizo se reconoce porque la curva
    /// <b>retrocede en X</b>, así que se <b>descartan</b> los puntos que retroceden y la
    /// cresta queda rematada por la cuerda entre los dos lados del rizo: un plano de
    /// unos milímetros en lugar de un nudo.
    /// </para>
    /// <para>
    /// <b>Se descartan y no se aplastan</b>, y la diferencia importa. Aplastarlos
    /// —dejarles la Y y subirles la X hasta la del anterior— parece más suave, pero
    /// mueve los puntos respecto del eje y el ancho de la barra deja de ser <c>d</c>:
    /// medido, se queda en 6.3 mm de los 9.5 que debería. Descartándolos, todos los
    /// puntos que sobreviven conservan su desplazamiento exacto y el ancho es
    /// <b>exactamente</b> el de la varilla.
    /// </para>
    /// <para>
    /// Los extremos no se tocan nunca: son los que llevan las tapas.
    /// </para>
    /// </remarks>
    private static double[] SinRizos(double[] pts)
    {
        var n = pts.Length / 2;

        if (n < 3)
        {
            return pts;
        }

        var salida = new List<double>(pts.Length) { pts[0], pts[1] };

        var xUltima = pts[0];

        for (var i = 1; i < n - 1; i++)
        {
            if (pts[2 * i] < xUltima)
            {
                continue;
            }

            xUltima = pts[2 * i];
            salida.Add(pts[2 * i]);
            salida.Add(pts[(2 * i) + 1]);
        }

        // El último punto entra siempre, aunque retroceda: lleva la tapa del extremo.
        salida.Add(pts[2 * (n - 1)]);
        salida.Add(pts[(2 * (n - 1)) + 1]);

        return salida.ToArray();
    }

    /// <summary>
    /// Da a una polilínea un <b>ancho real</b> en unidades de dibujo.
    /// </summary>
    /// <remarks>
    /// Es la forma que tiene AutoCAD de dibujar una barra continua con su grosor: la
    /// polilínea se rellena sola, sin hatch y sin frontera cerrada. Se intenta primero
    /// <c>ConstantWidth</c>, que es lo que corresponde a una <c>LightWeightPolyline</c>;
    /// si esta versión no lo acepta se cae a poner el ancho vértice por vértice con
    /// <c>SetWidth</c>, que es la vía antigua.
    /// </remarks>
    /// <returns><c>true</c> si quedó con grosor.</returns>
    private bool AnchoDePolilinea(object pl, double ancho)
    {
        if (ancho <= 0)
        {
            return false;
        }

        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic p = pl;

                try
                {
                    p.ConstantWidth = ancho;
                    return true;
                }
                catch (Exception)
                {
                    // Vía antigua: el ancho se pone en cada tramo. Se recorre hasta
                    // Count - 2 porque el ancho es del SEGMENTO, no del vértice, y una
                    // polilínea abierta de n vértices tiene n - 1 segmentos.
                    var n = (int)p.Coordinates.Length / 2;

                    for (var i = 0; i < n - 1; i++)
                    {
                        p.SetWidth(i, ancho, ancho);
                    }

                    return true;
                }
            });
        }
        catch (Exception ex)
        {
            Fallo("Ancho de la polilínea del zuncho", ex);
            return false;
        }
    }

    /// <summary>Separación en metros de dibujo, con respaldo.</summary>
    private double Paso(double cm, double respaldo = 0)
    {
        if (cm > 0)
        {
            return cm / 100 * _f;
        }

        return respaldo > 0 ? respaldo : 0.15 * _f;
    }

    // ==================================================================
    // Estribos como cápsulas
    // ==================================================================

    /// <summary>
    /// El estribo en alzado se ve como una <b>cápsula</b>: un rectángulo estrecho con
    /// un semicírculo arriba y otro abajo.
    /// </summary>
    /// <remarks>
    /// Los semicírculos salen de una polilínea cerrada con <c>bulge = -1</c> en los
    /// lados horizontales, que es exactamente medio arco. Es más barato que dos arcos
    /// y dos líneas, y queda una sola entidad que se puede rellenar.
    /// </remarks>
    private void CapsulasDeEstribo(
        object bloque, List<double> centros,
        double y0, double y1, double rec, double dEst, bool relleno)
    {
        if (dEst <= 0 || centros.Count == 0)
        {
            return;
        }

        var r = dEst / 2;
        var yArriba = y1 - rec + ArcOffset;
        var yAbajo = y0 + rec - ArcOffset;

        foreach (var xc in centros)
        {
            var pl = Poli(bloque, new[]
            {
                xc - r, yArriba,
                xc + r, yArriba,
                xc + r, yAbajo,
                xc - r, yAbajo
            }, "ESTRIBOS", cerrada: true, bulges: new (int, double)[] { (0, -1), (2, -1) });

            if (pl is null)
            {
                continue;
            }

            if (relleno)
            {
                var h = Hatch(bloque, "SOLID", 1, pl, "ESTRIBOS", ColorRellenoEstribo);
                if (h is not null)
                {
                    _fillEstribos.Add(h);
                }
            }
        }
    }

    // ==================================================================
    // Varillas
    // ==================================================================

    /// <summary>
    /// Varilla en alzado: una banda de un diámetro de ancho, con dobleces de 90° en
    /// los extremos si lleva gancho.
    /// </summary>
    /// <param name="hacia">Hacia dónde dobla el gancho: <c>true</c> arriba.</param>
    private void VarillaConGanchos(
        object bloque, double xL, double xR, double yc, double dBar, string capa,
        List<double> centros, double dEst, double gancho, bool hacia, bool relleno)
    {
        if (dBar <= 0 || xR <= xL + 1e-6)
        {
            return;
        }

        var r = dBar / 2;
        var hueco = dEst / 2;

        var conGancho = gancho > r && xR - dBar - r > xL + dBar + r;

        // Relleno sólido de la varilla, con el color de su capa. El contorno cerrado
        // es temporal: el hatch no es asociativo, así que se puede borrar.
        if (relleno)
        {
            var borde = BordeDeVarilla(bloque, xL, xR, yc, dBar, conGancho ? gancho : 0, hacia);

            if (borde is not null)
            {
                var h = Hatch(bloque, "SOLID", 1, borde, capa, PorCapa);
                if (h is not null)
                {
                    _fillVarillas.Add(h);
                }

                Borrar(borde);
            }
        }

        if (!conGancho)
        {
            CaraSegmentada(bloque, yc + r, capa, centros, hueco, xL, xR);
            CaraSegmentada(bloque, yc - r, capa, centros, hueco, xL, xR);
            Linea(bloque, xL, yc - r, xL, yc + r, capa);
            Linea(bloque, xR, yc - r, xR, yc + r, capa);
            return;
        }

        var u = hacia ? 1d : -1d;
        var yExterior = yc - (u * r);
        var yInterior = yc + (u * r);
        var yPunta = yc + (u * (r + gancho));

        CaraSegmentada(bloque, yExterior, capa, centros, hueco, xL + dBar, xR - dBar);
        CaraSegmentada(bloque, yInterior, capa, centros, hueco, xL + dBar + r, xR - dBar - r);

        var pi = Math.PI;

        if (hacia)
        {
            Arco(bloque, xL + dBar, yc + r, dBar, pi, 1.5 * pi, capa);
            Arco(bloque, xL + dBar + r, yc + dBar, r, pi, 1.5 * pi, capa);
            Arco(bloque, xR - dBar, yc + r, dBar, 1.5 * pi, 2 * pi, capa);
            Arco(bloque, xR - dBar - r, yc + dBar, r, 1.5 * pi, 2 * pi, capa);
        }
        else
        {
            Arco(bloque, xL + dBar, yc - r, dBar, pi / 2, pi, capa);
            Arco(bloque, xL + dBar + r, yc - dBar, r, pi / 2, pi, capa);
            Arco(bloque, xR - dBar, yc - r, dBar, 0, pi / 2, capa);
            Arco(bloque, xR - dBar - r, yc - dBar, r, 0, pi / 2, capa);
        }

        Linea(bloque, xL, yc + (u * r), xL, yPunta, capa);
        Linea(bloque, xL + dBar, yc + (u * dBar), xL + dBar, yPunta, capa);
        Linea(bloque, xL, yPunta, xL + dBar, yPunta, capa);

        Linea(bloque, xR, yc + (u * r), xR, yPunta, capa);
        Linea(bloque, xR - dBar, yc + (u * dBar), xR - dBar, yPunta, capa);
        Linea(bloque, xR, yPunta, xR - dBar, yPunta, capa);
    }

    /// <summary>
    /// Dibuja la cara de una varilla <b>cortándola donde cruza un estribo</b>.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que el alzado se lea: la varilla pasa por detrás del estribo, y
    /// dejar la línea entera haría creer que pasa por delante.
    /// </remarks>
    private void CaraSegmentada(
        object bloque, double y, string capa, List<double> centros,
        double hueco, double xIni, double xFin)
    {
        if (xFin <= xIni + 1e-7)
        {
            return;
        }

        var desde = xIni;

        foreach (var c in centros)
        {
            var a = c - hueco;
            var b = c + hueco;

            if (a > xFin)
            {
                break;
            }

            if (b > desde)
            {
                if (a > desde)
                {
                    Linea(bloque, desde, y, Math.Min(a, xFin), y, capa);
                }

                desde = b;
            }
        }

        if (xFin > desde + 1e-7)
        {
            Linea(bloque, desde, y, xFin, y, capa);
        }
    }

    /// <summary>Contorno cerrado de la varilla, para su relleno sólido.</summary>
    private object? BordeDeVarilla(
        object bloque, double xL, double xR, double yc, double dBar, double gancho, bool hacia)
    {
        var r = dBar / 2;
        var conGancho = gancho > r && xR - dBar - r > xL + dBar + r;

        if (!conGancho)
        {
            return Poli(bloque, new[]
            {
                xL, yc - r,
                xR, yc - r,
                xR, yc + r,
                xL, yc + r
            }, "CONCRETO", cerrada: true, bulges: null);
        }

        var u = hacia ? 1d : -1d;
        var yPunta = yc + (u * (r + gancho));
        const double b90 = 0.414213562373095;

        return Poli(bloque, new[]
        {
            xL,             yc + (u * r),
            xL,             yPunta,
            xL + dBar,      yPunta,
            xL + dBar,      yc + (u * dBar),
            xL + dBar + r,  yc + (u * r),
            xR - dBar - r,  yc + (u * r),
            xR - dBar,      yc + (u * dBar),
            xR - dBar,      yPunta,
            xR,             yPunta,
            xR,             yc + (u * r),
            xR - dBar,      yc - (u * r),
            xL + dBar,      yc - (u * r)
        }, "CONCRETO", cerrada: true, bulges: new (int, double)[]
        {
            (3, b90 * u), (5, b90 * u), (9, -b90 * u), (11, -b90 * u)
        });
    }

    private void Intermedias(
        object bloque, AlzadoCad a, double xa, double xb, double xaInf, double xbInf,
        double ycSup, double ycInf, double dSup, double dInf,
        double gSup, double gInf, List<double> centros, double dEst, bool relleno)
    {
        var n = a.NLateral;
        if (n <= 0 || !a.Lateral.Existe)
        {
            return;
        }

        var dInt = a.Lateral.Cm * _escala;
        var yTop = ycSup - (dSup / 2);
        var yBot = ycInf + (dInf / 2);

        // Las intermedias terminan ANTES de los ganchos, para no atravesarlos
        var xIni = xa;
        var xFin = xb;

        if (gSup > 0)
        {
            xIni = Math.Max(xIni, xa + dSup + HookClearH);
            xFin = Math.Min(xFin, xb - dSup - HookClearH);
        }

        if (gInf > 0)
        {
            xIni = Math.Max(xIni, xaInf + dInf + HookClearH);
            xFin = Math.Min(xFin, xbInf - dInf - HookClearH);
        }

        if (xFin <= xIni + dInt)
        {
            return;
        }

        var capa = CapaVar(a.Lateral.Clave);

        if (n == 1)
        {
            VarillaConGanchos(bloque, xIni, xFin, (yTop + yBot) / 2, dInt, capa,
                centros, dEst, 0, hacia: true, relleno);
            return;
        }

        var paso = (yTop - yBot) / (n + 1);

        for (var k = 1; k <= n; k++)
        {
            VarillaConGanchos(bloque, xIni, xFin, yBot + (paso * k), dInt, capa,
                centros, dEst, 0, hacia: true, relleno);
        }
    }

    private static double CorrerADerecha(
        double x, double ancho, List<double> centros, double hueco, double holgura)
    {
        foreach (var c in centros)
        {
            if (x <= c + hueco + holgura && x + ancho >= c - hueco - holgura)
            {
                x = c + hueco + holgura;
            }
        }

        return x;
    }

    private static double CorrerAIzquierda(
        double x, double ancho, List<double> centros, double hueco, double holgura)
    {
        for (var i = centros.Count - 1; i >= 0; i--)
        {
            var c = centros[i];
            if (x >= c - hueco - holgura && x - ancho <= c + hueco + holgura)
            {
                x = c - hueco - holgura;
            }
        }

        return x;
    }

    private static string CapaVar(string clave) =>
        string.IsNullOrWhiteSpace(clave) ? "ESTRIBOS" : "VAR_" + clave.Replace(" ", string.Empty);

    // ==================================================================
    // Hatch de concreto
    // ==================================================================

    private void HatchDeConcreto(object bloque, object? plBorde, bool relleno)
    {
        if (plBorde is null)
        {
            return;
        }

        // El fondo va primero para que el patrón quede encima
        if (relleno)
        {
            var fondo = Hatch(bloque, "SOLID", 1, plBorde, "CONCRETO", ColorFondo);
            if (fondo is not null)
            {
                _hatchConcreto.Add(fondo);
            }
        }

        var patron = Hatch(bloque, PatronConcreto, EscalaHatch * _f, plBorde, "CONCRETO", ColorPatron)
                     ?? Hatch(bloque, PatronRespaldo, EscalaHatch * _f, plBorde, "CONCRETO", ColorPatron);

        if (patron is not null)
        {
            _hatchConcreto.Add(patron);
        }
    }

    // ==================================================================
    // Orden de dibujo y color
    // ==================================================================

    /// <summary>
    /// Deja los rellenos en su orden: contornos arriba, luego estribos, luego
    /// varillas, y el concreto al fondo.
    /// </summary>
    private void OrdenarRellenos(object bloque)
    {
        AlFondo(bloque, _fillEstribos);
        AlFondo(bloque, _fillVarillas);
        AlFondo(bloque, _hatchConcreto);
    }

    /// <summary>Pone en negro todo lo que no sea hatch.</summary>
    private void ContornosNegros(object bloque, int inicio)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic bd = bloque;
                var total = (int)bd.Count;

                for (var i = inicio; i < total; i++)
                {
                    dynamic ent = bd.Item(i);

                    string nombre = ent.ObjectName;
                    if (nombre.Contains("hatch", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // El color se resuelve AQUI DENTRO, no antes del bucle, porque
                    // hace falta una entidad para pedirle su TrueColor y antes del
                    // bucle todavía no hay ninguna. No cuesta nada repetirlo:
                    // ColorNegro guarda el resultado y solo trabaja la primera vez.
                    var negro = ColorNegro((object)ent);

                    if (negro is not null)
                    {
                        try
                        {
                            ent.TrueColor = negro;
                            continue;
                        }
                        catch (Exception)
                        {
                            // Se cae al índice ACI de respaldo.
                        }
                    }

                    ent.Color = 7;
                }
            });
        }
        catch (Exception ex)
        {
            Fallo("Contornos del alzado en negro", ex);
        }
    }

    private object? _negro;
    private bool _negroIntentado;

    /// <summary>
    /// Objeto de color verdadero negro. Ver la explicación larga en
    /// <c>SeccionDrawer.ColorNegro</c>: la vía buena es el <c>TrueColor</c> de una
    /// entidad ya dibujada, porque el ProgID <c>AcCmColor</c> lleva un número de
    /// versión que no es el año y en AutoCAD 2026 ya no se acierta adivinándolo.
    /// Cuando falla, el contorno cae a ACI 7, que sobre fondo oscuro sale
    /// <b>blanco</b> en lugar de negro.
    /// </summary>
    private object? ColorNegro(object? ent = null)
    {
        if (_negro is not null || _negroIntentado)
        {
            return _negro;
        }

        _negroIntentado = true;

        if (ent is not null)
        {
            try
            {
                _negro = AcadConnection.Retry<object?>(() =>
                {
                    dynamic e = ent;
                    dynamic col = e.TrueColor;
                    col.SetRGB(0, 0, 0);
                    return (object?)col;
                });

                if (_negro is not null)
                {
                    return _negro;
                }
            }
            catch (Exception)
            {
                // Se sigue con la cascada de ProgIDs.
            }
        }

        for (var v = 26; v >= 15; v--)
        {
            try
            {
                dynamic col = _doc.Application.GetInterfaceObject("AutoCAD.AcCmColor." + v);
                col.SetRGB(0, 0, 0);
                _negro = col;
                return _negro;
            }
            catch (Exception)
            {
                // Esa versión no está.
            }
        }

        return null;
    }

    /// <summary>
    /// Gira 90° todo lo dibujado desde <paramref name="inicio"/>, alrededor del
    /// origen del bloque. Es lo que convierte el alzado horizontal en vertical.
    /// </summary>
    private void Girar90(object bloque, int inicio)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic bd = bloque;
                var total = (int)bd.Count;
                var origen = new[] { 0d, 0d, 0d };

                for (var i = inicio; i < total; i++)
                {
                    dynamic ent = bd.Item(i);
                    ent.Rotate(origen, Math.PI / 2);
                    ent.Update();
                }
            });
        }
        catch (Exception ex)
        {
            Fallo("Girar el alzado vertical", ex);
        }
    }

    // ==================================================================
    // Cotas y títulos
    // ==================================================================

    private void AnotarHorizontal(
        AlzadoCad a, double x, double y, double largo, double alto, Geo geo)
    {
        var x1 = x + largo;
        var y1 = y + alto;

        CotasDeGancho(x, y, x1, geo);

        // Armado, arriba
        Cota(x, y1, x1, y1, x + (largo / 2), y1 + 0.08, TextoLecho(a.Inferior, "Inferiores"), false);
        Cota(x, y1, x1, y1, x + (largo / 2), y1 + 0.16, TextoSimple(a.NLateral * 2, a.Lateral, "Intermedias"), false);
        Cota(x, y1, x1, y1, x + (largo / 2), y1 + 0.24, TextoLecho(a.Superior, "Superiores"), false);

        if (a.LongitudM > 0)
        {
            Cota(x, y1, x1, y1, x + (largo / 2), y1 + 0.32, string.Empty, false);
        }

        // Estribos y zonas, abajo
        var yEst = y - 0.05;
        var yZona = yEst - 0.1;
        var s = a.SeparacionesCm;

        var q = new[] { x, x + (largo / 4), x + (3 * largo / 4), x1 };
        var etiquetas = new[] { "L/4", "L/2", "L/4" };

        for (var i = 0; i < 3; i++)
        {
            var medio = (q[i] + q[i + 1]) / 2;

            Cota(q[i], y, q[i + 1], y, medio, yEst,
                TextoTransversal(a, s[i]), false);

            Cota(q[i], y, q[i + 1], y, medio, yZona, etiquetas[i], false);
        }

        Titulo(a, x, y, largo);
    }

    /// <summary>
    /// Cotas que miden el <b>doblez de los ganchos</b>, al lado derecho del alzado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Faltaban por completo: el gancho se dibujaba pero no se acotaba, así que del
    /// plano no salía la medida con la que se corta y se dobla la varilla en obra. Es
    /// justo el dato que el fierrero necesita.
    /// </para>
    /// <para>
    /// Dos reglas de la macro que no son obvias y hay que respetar:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     El gancho superior <b>solo se acota si las varillas de esquina superior e
    ///     inferior son de distinto diámetro</b>. Si son iguales, los dos ganchos
    ///     miden lo mismo —los dos son 12 diámetros— y la segunda cota diría
    ///     exactamente lo mismo que la primera. Acotar dos veces el mismo número
    ///     ensucia el plano sin añadir información.
    ///   </item>
    ///   <item>
    ///     Cuando se acotan los dos, el de arriba se aparta a
    ///     <c>HOOK_DIM_OFF_2</c> para no montarse sobre el de abajo; si el de abajo
    ///     no está, se queda en <c>HOOK_DIM_OFF_1</c>, pegado al alzado.
    ///   </item>
    /// </list>
    /// <para>
    /// Cada cota se mide sobre la varilla en la que va el gancho, no sobre la cara del
    /// concreto: la inferior en <c>XbInf</c> y la superior en <c>Xb</c>, que no
    /// coinciden cuando los dos ganchos se cruzan y el inferior se tuvo que recorrer.
    /// </para>
    /// <para>
    /// El alzado <b>vertical no lleva estas cotas</b>, igual que en la macro: ahí el
    /// gancho es el valor de la columna T de la hoja, un número que el usuario ya
    /// escribió, y aparece en el rótulo.
    /// </para>
    /// </remarks>
    private void CotasDeGancho(double x, double y, double x1, Geo geo)
    {
        // Diámetros distintos arriba y abajo: entonces los ganchos miden distinto.
        var esquinasDiferentes = Math.Abs(geo.DSup - geo.DInf) > 1e-6;

        if (geo.GanchoInf > 0)
        {
            // El gancho inferior dobla hacia ARRIBA: de la cara superior de la
            // varilla inferior hacia el interior de la pieza.
            var xb = x + geo.XbInf;
            var yA = y + geo.YcInf + (geo.DInf / 2);
            var yB = yA + geo.GanchoInf;

            Cota(xb, yA, xb, yB, x1 + (HookDimOff1 * _f), (yA + yB) / 2, string.Empty, true);
        }

        if (geo.GanchoSup > 0 && esquinasDiferentes)
        {
            // El superior dobla hacia ABAJO.
            var xb = x + geo.Xb;
            var yC = y + geo.YcSup - (geo.DSup / 2);
            var yD = yC - geo.GanchoSup;

            var xCota = geo.GanchoInf > 0
                ? x1 + (HookDimOff2 * _f)
                : x1 + (HookDimOff1 * _f);

            Cota(xb, yD, xb, yC, xCota, (yC + yD) / 2, string.Empty, true);
        }
    }

    /// <summary>Separación de la primera cota de gancho: <c>HOOK_DIM_OFF_1</c>.</summary>
    private const double HookDimOff1 = 0.06;

    /// <summary>Separación de la segunda, cuando hay dos: <c>HOOK_DIM_OFF_2</c>.</summary>
    private const double HookDimOff2 = 0.14;

    private void AnotarVertical(
        AlzadoCad a, double x, double y, double largo, double ancho, Geo geo, bool conRotulo)
    {
        // El bloque insertado ocupa de x-ancho a x, y de y a y+largo
        var xDer = x;
        var xIzq = x - ancho;
        var y1 = y + largo;

        // ---------- Textos del armado longitudinal ----------
        if (a.Circular)
        {
            // En la columna redonda NO hay lechos, así que los tres textos de siempre
            // leerían grupos vacíos y saldrían como «---»: el alzado quedaba SIN
            // rótulo de armado, que es lo que faltaba.
            //
            // Va UNA sola cota, con el total, porque en un círculo todas las varillas
            // son el mismo grupo. Y no dice «Izquierdas» ni «Derechas»: en un círculo
            // eso no significa nada.
            Cota(xDer, y, xDer, y1, xDer + 0.08, y + (largo / 2),
                TextoCirculo(a), true);
        }
        else
        {
            Cota(xDer, y, xDer, y1, xDer + 0.08, y + (largo / 2), TextoLecho(a.Superior, "Izquierdas"), true);
            Cota(xDer, y, xDer, y1, xDer + 0.16, y + (largo / 2), TextoSimple(a.NLateral * 2, a.Lateral, "Intermedias"), true);
            Cota(xDer, y, xDer, y1, xDer + 0.24, y + (largo / 2), TextoLecho(a.Inferior, "Derechas"), true);
        }

        var s = a.SeparacionesCm;
        var q = new[] { y, y + (largo / 4), y + (3 * largo / 4), y1 };
        var etiquetas = new[] { "L/4", "L/2", "L/4" };

        for (var i = 0; i < 3; i++)
        {
            var medio = (q[i] + q[i + 1]) / 2;

            Cota(xIzq, q[i], xIzq, q[i + 1], xIzq - 0.08, medio,
                TextoTransversal(a, s[i]), true);

            Cota(xIzq, q[i], xIzq, q[i + 1], xIzq - 0.18, medio, etiquetas[i], true);
        }

        if (a.LongitudM > 0)
        {
            Cota(xIzq, y, xIzq, y1, xIzq - 0.28, y + (largo / 2), string.Empty, true);
        }

        if (conRotulo)
        {
            TituloVertical(a, xDer + 0.24 + 0.09, y1);
        }
    }

    private void Cota(
        double x1, double y1, double x2, double y2,
        double xt, double yt, string texto, bool vertical)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(
                    new[] { x1, y1, 0d }, new[] { x2, y2, 0d }, new[] { xt, yt, 0d });

                d.StyleName = "COTA_ESTRUCTURAL";
                d.Layer = "COTAS";
                d.TextInsideAlign = true;
                d.TextInside = true;
                d.TextOverride = texto;

                if (vertical)
                {
                    d.TextRotation = Math.PI / 2;
                }

                d.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota del alzado", ex);
        }
    }

    private void Titulo(AlzadoCad a, double x, double y, double largo)
    {
        var esContratrabe = a.Tipo == TipoElemento.Contratrabe;

        var yTitulo = y - 0.23;
        var yEscala = yTitulo - 0.064;

        var xDer = esContratrabe ? x + largo : x + largo - 0.76;
        var xTitulo = esContratrabe ? xDer - 0.92 : xDer;
        var xEscala = esContratrabe ? xDer - 0.16 : xDer + 0.59;

        Texto(xTitulo, yTitulo,
            $"DETALLE DE ALZADO DE {a.TipoTexto} \"{a.Id}\"", AlturaTitulo, 3);

        var escala = string.IsNullOrWhiteSpace(a.Escala) ? "10" : a.Escala;
        Texto(xEscala, yEscala, "Escala 1:" + escala, AlturaEscala, 3);
    }

    private void TituloVertical(AlzadoCad a, double x, double y)
    {
        var xFin = TextoGirado(x, y,
            $"DETALLE DE ALZADO DE {a.TipoTexto} \"{a.Id}\"", AlturaTitulo);

        var escala = string.IsNullOrWhiteSpace(a.Escala) ? "10" : a.Escala;
        TextoGirado(xFin + 0.01, y, "Escala 1:" + escala, AlturaEscala);
    }

    /// <param name="anclaje">3 = arriba a la derecha, como en la macro.</param>
    private void Texto(double x, double y, string texto, double alto, int anclaje)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { x, y, 0d }, 0d, texto);
                mt.StyleName = EstiloTexto;
                mt.Height = alto * _f;
                mt.AttachmentPoint = anclaje;
                mt.Width = 0;
                mt.Layer = "TEXTOS";
                mt.Color = ColorVerde;
                mt.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo("Titulo del alzado", ex);
        }
    }

    /// <summary>
    /// Texto girado 90°, colocado por su borde izquierdo y superior.
    /// </summary>
    /// <returns>La X del borde derecho, para encadenar el siguiente texto.</returns>
    private double TextoGirado(double x, double y, string texto, double alto)
    {
        try
        {
            return (double)AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { x, y, 0d }, 0d, texto);
                mt.StyleName = EstiloTexto;
                mt.Height = alto * _f;
                mt.Width = 0;
                mt.AttachmentPoint = 5;             // centro
                mt.Rotation = Math.PI / 2;
                mt.Layer = "TEXTOS";
                mt.Color = ColorVerde;
                mt.Update();

                // Se recoloca por caja envolvente para que el borde caiga exacto
                var caja = Caja((object)mt);
                if (caja is not null)
                {
                    mt.Move(
                        new[] { 0d, 0d, 0d },
                        new[] { x - caja.Value.Min[0], y - caja.Value.Max[1], 0d });
                    mt.Update();

                    var final = Caja((object)mt);
                    if (final is not null)
                    {
                        return final.Value.Max[0];
                    }
                }

                return x;
            });
        }
        catch (Exception ex)
        {
            Fallo("Titulo girado del alzado", ex);
            return x;
        }
    }

    private static string TextoLecho(LechoCad l, string posicion)
    {
        var total = l.NEsquina + l.NIntermedia;
        if (total <= 0)
        {
            return "---";
        }

        var d1 = Etiqueta(l.Esquina.Clave);
        var d2 = Etiqueta(l.Intermedia.Clave);

        if (l.NIntermedia <= 0 || string.Equals(d1, d2, StringComparison.OrdinalIgnoreCase))
        {
            var d = string.IsNullOrEmpty(d1) ? d2 : d1;
            return total == 1
                ? $"1 Varilla {posicion} {d}"
                : $"{total} Varillas {posicion} {d}";
        }

        var p1 = l.NEsquina == 1 ? $"1 Varilla {d1}" : $"{l.NEsquina} Varillas {d1}";
        var p2 = l.NIntermedia == 1 ? $"1 Varilla {d2}" : $"{l.NIntermedia} Varillas {d2}";

        return $"{p1} + {p2} {posicion}";
    }

    /// <summary>
    /// Texto del armado longitudinal de una columna <b>circular</b>.
    /// </summary>
    /// <remarks>
    /// Sin posición: en un círculo todas las varillas son el mismo grupo y no hay
    /// «izquierdas» ni «derechas» que distinguir.
    /// </remarks>
    private static string TextoCirculo(AlzadoCad a)
    {
        var d = Etiqueta(a.VarTotal.Existe ? a.VarTotal.Clave : a.EstriboDibujo.Clave);

        if (a.NVarTotal <= 0 || string.IsNullOrEmpty(d))
        {
            return "---";
        }

        return a.NVarTotal == 1
            ? $"1 Varilla {d}"
            : $"{a.NVarTotal} Varillas {d}";
    }

    /// <summary>
    /// Texto del acero transversal: <b>estribo</b> en la rectangular y <b>zuncho</b>
    /// en la circular.
    /// </summary>
    /// <remarks>
    /// No es un detalle de redacción. Un estribo y un zuncho se piden, se doblan y se
    /// colocan distinto, y en la circular además hay que decir si sube en hélice o son
    /// anillos: es lo que el fierrero necesita para armarlo. Con el texto «Est. #3 @ 10
    /// cm» en una columna redonda, el plano no dice cuál de las dos cosas es.
    /// </remarks>
    private static string TextoTransversal(AlzadoCad a, double separacionCm)
    {
        var clave = a.Estribo.Clave;

        if (!a.Circular)
        {
            return $"Est. {clave} @ {separacionCm:0} cm";
        }

        var forma = a.ZunchoHelicoidal ? "helic." : "anillos";

        return $"Zuncho {forma} {clave} @ {separacionCm:0} cm";
    }

    private static string TextoSimple(int n, VarCad v, string posicion)
    {
        var d = Etiqueta(v.Clave);
        if (n <= 0 || string.IsNullOrEmpty(d))
        {
            return "---";
        }

        var pos = posicion == "Intermedias" && n == 1 ? "Intermedia" : posicion;

        return n == 1 ? $"1 Varilla {pos} {d}" : $"{n} Varillas {pos} {d}";
    }

    /// <summary>
    /// Etiqueta del diámetro como la escribe la macro: con <c>#</c> y con <c>C</c>
    /// final cuando el número es mayor que 2.
    /// </summary>
    private static string Etiqueta(string clave)
    {
        var t = (clave ?? string.Empty).Trim().ToUpperInvariant();

        if (t.Length == 0)
        {
            return string.Empty;
        }

        if (!t.Contains('#'))
        {
            t = "#" + t;
        }

        var numero = t.Replace("#", string.Empty).Replace(",", ".");

        if (double.TryParse(numero, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v > 2 && !t.EndsWith("C", StringComparison.Ordinal))
        {
            t += "C";
        }

        return t;
    }

    // ==================================================================
    // Bloques
    // ==================================================================

    private string NombreUnico(string baseName)
    {
        var limpio = baseName;

        foreach (var c in new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' })
        {
            limpio = limpio.Replace(c, '_');
        }

        limpio = limpio.Trim();
        if (limpio.Length == 0)
        {
            limpio = "ALZ";
        }

        var cand = limpio;
        var k = 1;

        while (_nombres.Contains(cand))
        {
            k++;
            cand = limpio + "_" + k;
        }

        _nombres.Add(cand);
        return cand;
    }

    /// <summary>Definición del bloque, vaciada si ya existía.</summary>
    private object? DefinicionDeBloque(string nombre)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic bloques = _doc.Blocks;

                try
                {
                    dynamic existente = bloques.Item(nombre);

                    for (var i = (int)existente.Count - 1; i >= 0; i--)
                    {
                        existente.Item(i).Delete();
                    }

                    return (object?)existente;
                }
                catch (Exception)
                {
                    // No existía: se crea.
                }

                return (object?)bloques.Add(new[] { 0d, 0d, 0d }, nombre);
            });
        }
        catch (Exception ex)
        {
            Fallo($"Definicion del bloque de alzado '{nombre}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Crea las capas que necesita el alzado.
    /// </summary>
    /// <remarks>
    /// <b>La capa ALZADOS hay que crearla aquí.</b> Al principio se reutilizaba
    /// <c>AsegurarCapas</c> del dibujante de secciones, que no la crea, y asignar una
    /// capa inexistente hace que AutoCAD responda
    /// <c>0x80200014: Key not found</c> al insertar el bloque. El mensaje engaña,
    /// porque el bloque sí existía: lo que faltaba era la capa.
    /// </remarks>
    public void AsegurarCapas()
    {
        foreach (var capa in new[] { "ALZADOS", "CONCRETO", "ESTRIBOS", "TEXTOS", "ROTULOS", "COTAS" })
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic capas = _doc.Layers;

                    try
                    {
                        _ = capas.Item(capa);
                    }
                    catch (Exception)
                    {
                        capas.Add(capa);
                    }
                });
            }
            catch (Exception ex)
            {
                Fallo($"Crear la capa '{capa}'", ex);
            }
        }
    }

    private void InsertarBloque(string nombre, double x, double y)
    {
        object? br = null;

        try
        {
            br = AcadConnection.Retry<object?>(() =>
                (object?)_ms.InsertBlock(new[] { x, y, 0d }, nombre, 1d, 1d, 1d, 0d));
        }
        catch (Exception ex)
        {
            Fallo($"Insertar el bloque de alzado '{nombre}'", ex);
            return;
        }

        if (br is null)
        {
            return;
        }

        // La capa va en su PROPIO try. Si se pone junto a la inserción, un problema
        // de capa se reporta como si el bloque no existiera, que es justo lo que
        // hizo perder tiempo la primera vez.
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic r = br;
                r.Layer = "ALZADOS";
                r.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo($"Poner el bloque '{nombre}' en la capa ALZADOS", ex);
        }
    }

    // ==================================================================
    // Primitivas
    // ==================================================================

    private object? RectCerrado(
        object cont, double xa, double ya, double xb, double yb, string capa) =>
        Poli(cont, new[] { xa, ya, xb, ya, xb, yb, xa, yb }, capa, cerrada: true, bulges: null);

    private object? Poli(
        object cont, double[] pts, string capa, bool cerrada, (int, double)[]? bulges)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = ((dynamic)cont).AddLightWeightPolyline(pts);
                pl.Closed = cerrada;
                pl.Layer = capa;

                if (bulges is not null)
                {
                    foreach (var (i, b) in bulges)
                    {
                        pl.SetBulge(i, b);
                    }
                }

                pl.Update();
                pl.Color = PorCapa;

                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Polilinea del alzado", ex);
            return null;
        }
    }

    private void Linea(object cont, double xa, double ya, double xb, double yb, string capa)
    {
        if (Math.Abs(xb - xa) < 1e-7 && Math.Abs(yb - ya) < 1e-7)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = ((dynamic)cont).AddLine(new[] { xa, ya, 0d }, new[] { xb, yb, 0d });
                l.Layer = capa;
                l.Color = PorCapa;
            });
        }
        catch (Exception ex)
        {
            Fallo("Linea del alzado", ex);
        }
    }

    private void Arco(
        object cont, double cx, double cy, double radio, double a0, double a1, string capa)
    {
        if (radio <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic arc = ((dynamic)cont).AddArc(new[] { cx, cy, 0d }, radio, a0, a1);
                arc.Layer = capa;
                arc.Color = PorCapa;
            });
        }
        catch (Exception ex)
        {
            Fallo("Arco del alzado", ex);
        }
    }

    private object? Hatch(
        object cont, string patron, double escala, object borde, string capa, int colorAci)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic h = ((dynamic)cont).AddHatch(0, patron, false);
                h.HatchStyle = 0;

                var ok = AcadArreglos.Llamar(
                    $"AppendOuterLoop del hatch '{patron}' del alzado",
                    new[] { borde },
                    arr => { h.AppendOuterLoop(arr); },
                    Fallo, Nota);

                if (!ok)
                {
                    // Un hatch sin frontera es una entidad degenerada: se borra para
                    // que no rompa después el cálculo de extensiones.
                    Borrar((object)h);
                    return null;
                }

                if (!patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    h.PatternScale = escala;
                }

                h.Layer = capa;
                h.Color = colorAci;
                h.Evaluate();
                h.Layer = capa;
                h.Color = colorAci;

                return (object?)h;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}' del alzado", ex);
            return null;
        }
    }

    /// <summary>
    /// Sube entidades al <b>frente</b> del orden de dibujo del bloque.
    /// </summary>
    /// <remarks>
    /// El simétrico de <see cref="AlFondo"/>, con la misma tabla <c>ACAD_SORTENTS</c>.
    /// Lo usa el zuncho helicoidal macizo: ver <see cref="ZunchoAlFrente"/>.
    /// </remarks>
    private void AlFrente(object cont, List<object> objetos)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = ((dynamic)cont).GetExtensionDictionary;
                dynamic tabla;

                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                AcadArreglos.Llamar("MoveToTop del alzado", objetos,
                    arr => { tabla.MoveToTop(arr); }, Fallo, Nota);
            });
        }
        catch (Exception ex)
        {
            Fallo("Orden de dibujo del alzado", ex);
        }
    }

    private void AlFondo(object cont, List<object> objetos)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = ((dynamic)cont).GetExtensionDictionary;
                dynamic tabla;

                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                AcadArreglos.Llamar("MoveToBottom del alzado", objetos,
                    arr => { tabla.MoveToBottom(arr); }, Fallo, Nota);
            });
        }
        catch (Exception ex)
        {
            Fallo("Orden de dibujo del alzado", ex);
        }
    }

    /// <summary>Caja envolvente. No se puede pedir con <c>dynamic</c>: va por reflexión.</summary>
    private (double[] Min, double[] Max)? Caja(object ent)
    {
        try
        {
            var args = new object?[] { null, null };

            var mod = new ParameterModifier(2);
            mod[0] = true;
            mod[1] = true;

            ent.GetType().InvokeMember(
                "GetBoundingBox", BindingFlags.InvokeMethod,
                null, ent, args, new[] { mod }, null, null);

            var mn = ADobles(args[0]);
            var mx = ADobles(args[1]);

            return mn.Length >= 2 && mx.Length >= 2 ? (mn, mx) : null;
        }
        catch (Exception ex)
        {
            Fallo("Caja envolvente en el alzado", ex);
            return null;
        }
    }

    private static double[] ADobles(object? v) => v switch
    {
        double[] d => d,
        object[] o => o.Select(x => x is null ? 0d : Convert.ToDouble(x)).ToArray(),
        _ => Array.Empty<double>()
    };

    private void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).Delete(); });
        }
        catch (Exception)
        {
            // Un temporal que no se borra solo deja una polilínea de más.
        }
    }

    private void Fallo(string operacion, Exception ex)
    {
        var e = ex;
        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var detalle = e.GetType().Name;

        if (e is System.Runtime.InteropServices.COMException com)
        {
            detalle += $" 0x{(uint)com.HResult:X8}";
        }

        detalle += ": " + e.Message.Replace(Environment.NewLine, " ").Trim();

        var linea = operacion + " -> " + detalle;

        if (!_log.Contains(linea))
        {
            _log.Add(linea);
        }
    }

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }
}
