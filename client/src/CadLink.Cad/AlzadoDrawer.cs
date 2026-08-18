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

        var sec = InsertarSeccion(a.Id, xSec, AlzadoLayout.YBloques);

        // Si el bloque de la sección no existe, la macro supone 0.8 x 0.4 para que
        // los elementos siguientes no se encimen. No es un adorno: sin esto, un ID
        // sin sección arrastra el desorden al resto de la fila.
        var ancho = sec?.Ancho ?? AlzadoLayout.AnchoSeccionSupuesto;
        var tope = sec?.Tope ?? (AlzadoLayout.YBloques + AlzadoLayout.AltoSeccionSupuesto);

        var dosCaras = a.EsVertical
                       && a.BaseCm > 0
                       && Math.Abs(a.BaseCm - a.AlturaCm) > 1e-4;

        var p = AlzadoLayout.Colocar(x0, a.EsVertical, ancho, tope, largo, dosCaras);

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
                // La segunda cara va por encima del paño superior de la primera
                var y2 = y + largo + 0.3;

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

        CapsulasDeEstribo(bloque, centros, y0, y1, rec, dEst, relleno);

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
        VarillaConGanchos(bloque, xa, xb, ycSup, dSup,
            CapaVar(a.Superior.Esquina.Clave), centros, dEst, gSup, hacia: false, relleno);

        VarillaConGanchos(bloque, xaInf, xbInf, ycInf, dInf,
            CapaVar(a.Inferior.Esquina.Clave), centros, dEst, gInf, hacia: true, relleno);

        Intermedias(bloque, a, xa, xb, xaInf, xbInf, ycSup, ycInf,
            dSup, dInf, gSup, gInf, centros, dEst, relleno);

        // ---------- Color y orden ----------
        if (relleno)
        {
            ContornosNegros(bloque, inicio);
        }

        OrdenarRellenos(bloque);

        if (girar)
        {
            Girar90(bloque, inicio);
        }

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
                $"Est. {a.Estribo.Clave} @ {s[i]:0} cm", false);

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

        Cota(xDer, y, xDer, y1, xDer + 0.08, y + (largo / 2), TextoLecho(a.Superior, "Izquierdas"), true);
        Cota(xDer, y, xDer, y1, xDer + 0.16, y + (largo / 2), TextoSimple(a.NLateral * 2, a.Lateral, "Intermedias"), true);
        Cota(xDer, y, xDer, y1, xDer + 0.24, y + (largo / 2), TextoLecho(a.Inferior, "Derechas"), true);

        var s = a.SeparacionesCm;
        var q = new[] { y, y + (largo / 4), y + (3 * largo / 4), y1 };
        var etiquetas = new[] { "L/4", "L/2", "L/4" };

        for (var i = 0; i < 3; i++)
        {
            var medio = (q[i] + q[i + 1]) / 2;

            Cota(xIzq, q[i], xIzq, q[i + 1], xIzq - 0.08, medio,
                $"Est. {a.Estribo.Clave} @ {s[i]:0} cm", true);

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
