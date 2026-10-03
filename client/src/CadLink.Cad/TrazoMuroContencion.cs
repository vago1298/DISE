using System.Globalization;

namespace CadLink.Cad;

// ============================================================================
//  LA GEOMETRIA DE LOS MUROS DE CONTENCION, SIN AUTOCAD
//
//  Una sola cuenta para la vista previa de CadLink y para el dibujo de AutoCAD: los dos piden
//  aqui el DibujoMuro y lo pintan cada uno con sus primitivas. Asi lo que se ve en la pantalla
//  es lo que sale en el plano.
//
//  Unidades: METROS de dibujo, 1 unidad = 1 m. El origen (x0, y0) es la esquina de abajo a la
//  izquierda de la zapata, en la orilla de la punta.
// ============================================================================

/// <summary>Una poligonal del dibujo.</summary>
/// <param name="Clave">La varilla, si es acero; vacia si es concreto o una linea.</param>
/// <param name="Oculta">A trazos: las espigas.</param>
public sealed record TrazoPoli(List<(double X, double Y)> Puntos, bool Cerrada, string Clave, bool Oculta);

/// <summary>Una varilla vista de punta.</summary>
public readonly record struct TrazoPunto(double X, double Y, double R, string Clave);

/// <summary>Una cota alineada: de (X1,Y1) a (X2,Y2), con el texto en (Xt,Yt).</summary>
public readonly record struct TrazoCota(double X1, double Y1, double X2, double Y2, double Xt, double Yt, bool Vertical);

/// <summary>
/// Una llamada: flecha en la punta, quiebre en el codo y un HOMBRO horizontal de (XCodo, YCodo)
/// a (XHombro, YCodo) con el texto ENCIMA, arrancando en (XTexto, YTexto) -su linea base-.
/// </summary>
/// <remarks>
/// Asi se rotula en un plano: la linea de la flecha llega al hombro y el texto va sobre el, sin
/// que ninguna linea lo atraviese. Antes la flecha llegaba a media altura del texto y lo tachaba.
/// </remarks>
public sealed record TrazoRotulo(
    string Texto, double XPunta, double YPunta, double XCodo, double YCodo, double XHombro,
    double XTexto, double YTexto);

/// <summary>Un texto suelto, centrado en X.</summary>
public sealed record TrazoTexto(string Texto, double X, double Y, double Alto);

/// <summary>Todo lo que lleva el dibujo de un muro.</summary>
public sealed class DibujoMuro
{
    /// <summary>El concreto: un poligono que se rellena.</summary>
    public List<List<(double X, double Y)>> Concreto { get; } = new();

    /// <summary>El terreno: poligonos que se rellenan con tierra.</summary>
    public List<List<(double X, double Y)>> Terreno { get; } = new();

    /// <summary>Contornos sin relleno: el del detalle «acero en la pantalla».</summary>
    public List<TrazoPoli> Lineas { get; } = new();

    public List<TrazoPoli> Varillas { get; } = new();

    public List<TrazoPunto> Puntos { get; } = new();

    public List<TrazoCota> Cotas { get; } = new();

    public List<TrazoRotulo> Rotulos { get; } = new();

    public List<TrazoTexto> Textos { get; } = new();

    public double XMin { get; set; }

    public double XMax { get; set; }

    public double YMin { get; set; }

    public double YMax { get; set; }
}

/// <summary>Las cuentas de los muros de contencion.</summary>
public static class TrazoMuroContencion
{
    /// <summary>Donde se apoya la fila de muros en AutoCAD: lejos de lo demas.</summary>
    public const double YBase = -20.0;

    /// <summary>
    /// La fila de los ciclopeos, mas abajo: cada pestaña se dibuja por su lado, y asi dibujar
    /// los ciclopeos despues de los armados no los encima.
    /// </summary>
    public const double YBaseCiclopeo = -34.0;

    /// <summary>Aire entre un muro y el siguiente.</summary>
    public const double SeparacionMuros = 2.5;

    /// <summary>Aire entre el detalle de la pantalla y el corte.</summary>
    public const double AireDetalle = 2.2;

    /// <summary>Lo que se aparta una cota de lo que mide.</summary>
    public const double SepCota = 0.45;

    public const double AltoRotulo = 0.09;
    public const double AltoTitulo = 0.15;
    public const double AltoSubtitulo = 0.10;

    /// <summary>
    /// Ancho de una letra en relacion a su alto, con holgura: el hombro de la llamada tiene que
    /// cubrir el texto entero, y quedarse corto es peor que pasarse.
    /// </summary>
    public const double FactorLetra = 0.85;

    /// <summary>Lo que se levanta el texto sobre el hombro de su llamada.</summary>
    public const double AireTexto = 0.03;

    /// <summary>
    /// El alto de los numeros de las cotas del muro de concreto armado. Mas grande que el de los
    /// textos: un muro de 5 m se dibuja para leerse a 1:50.
    /// </summary>
    public const double AltoCotaArmado = 0.15;

    /// <summary>El del ciclopeo, que suele medir la mitad.</summary>
    public const double AltoCotaCiclopeo = 0.09;

    /// <summary>Lo ancho que sale un texto, para su hombro.</summary>
    public static double AnchoTexto(string texto, double alto) => texto.Length * alto * FactorLetra;

    /// <summary>
    /// El diametro REAL, en m, de la varilla de esa clave en ese muro: es el grosor con que se
    /// dibuja. 0 si el muro no la lleva.
    /// </summary>
    public static double DiametroM(MuroContencionCad m, string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            return 0;
        }

        foreach (var v in new[]
                 {
                     m.VarVertTierra, m.VarVertExt, m.VarHoriz, m.VarEspiga,
                     m.VarZapSup, m.VarRepSup, m.VarZapInf, m.VarRepInf
                 })
        {
            if (v.Existe && string.Equals(v.Clave.Trim(), clave.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return v.M;
            }
        }

        return 0;
    }

    /// <summary>Una llamada con su texto a la DERECHA del codo.</summary>
    public static TrazoRotulo LlamadaDerecha(string texto, double xp, double yp, double xCodo, double yCodo)
    {
        var w = AnchoTexto(texto, AltoRotulo);
        return new TrazoRotulo(texto, xp, yp, xCodo, yCodo, xCodo + w + 0.04, xCodo + 0.02, yCodo + AireTexto);
    }

    /// <summary>Una llamada con su texto a la IZQUIERDA del codo, que acaba en el.</summary>
    public static TrazoRotulo LlamadaIzquierda(string texto, double xp, double yp, double xCodo, double yCodo)
    {
        var w = AnchoTexto(texto, AltoRotulo);
        return new TrazoRotulo(texto, xp, yp, xCodo, yCodo, xCodo - w - 0.04, xCodo - w - 0.02, yCodo + AireTexto);
    }

    /// <summary>Espesor de las franjas de terreno.</summary>
    public const double FranjaTerreno = 0.15;

    /// <summary>Lo que se aparta la bota del acero del lecho de abajo de la zapata.</summary>
    private const double PieDelAcero = 0.05;

    // ======================================================================
    //  Revisar
    // ======================================================================

    /// <summary>Lo que impide dibujarlo, dicho en palabras. Vacio si se puede.</summary>
    public static List<string> Problemas(MuroContencionCad m)
    {
        var p = new List<string>();

        if (string.IsNullOrWhiteSpace(m.Id)) { p.Add("falta el ID"); }
        if (m.AlturaM <= 0) { p.Add("la altura tiene que ser mayor que cero"); }
        if (m.EspesorZapataM <= 0) { p.Add("el espesor de la zapata tiene que ser mayor que cero"); }

        if (m.EsCiclopeo)
        {
            if (m.CM < 0 || m.BM < 0 || m.MM < 0 || m.EM < 0 || m.GM < 0 || m.NM < 0)
            {
                p.Add("ninguna medida puede ser negativa");
            }

            if (m.CM + m.BM <= 0) { p.Add("la corona (c + b) tiene que ser mayor que cero"); }

            if (m.CM + m.BM > m.EM + m.GM + 1e-9)
            {
                p.Add($"la corona c + b = {F(m.CM + m.BM)} m es mayor que el pie E + G = {F(m.EM + m.GM)} m: la cara quedaria colgada");
            }

            return p;
        }

        if (m.BaseM <= 0) { p.Add("el ancho de la zapata tiene que ser mayor que cero"); }
        if (m.CoronaM <= 0) { p.Add("el espesor de la corona tiene que ser mayor que cero"); }
        if (m.EspesorPieM < m.CoronaM) { p.Add("el espesor al pie no puede ser menor que el de la corona"); }
        if (m.PuntaM < 0) { p.Add("la punta no puede ser negativa"); }

        if (m.TalonM < -1e-9)
        {
            p.Add($"punta + espesor al pie = {F(m.PuntaM + m.EspesorPieM)} m no cabe en la zapata de {F(m.BaseM)} m");
        }

        var rec = m.RecCm / 100;

        if (rec <= 0 || 2 * rec >= Math.Min(m.EspesorZapataM, m.CoronaM))
        {
            p.Add("el recubrimiento no cabe en la zapata o en la corona");
        }

        if (m.Espolon)
        {
            if (m.EspolonAnchoM <= 0 || m.EspolonProfM <= 0) { p.Add("el espolon necesita ancho y profundidad"); }

            if (m.EspolonDistM < 0 || m.EspolonDistM + m.EspolonAnchoM > m.BaseM + 1e-9)
            {
                p.Add("el espolon se sale de la zapata");
            }
        }

        void Var(string que, VarMuro v, double sepCm)
        {
            if (!v.Existe) { p.Add($"falta la varilla de {que}"); }
            else if (sepCm <= 0) { p.Add($"falta la separacion de {que}"); }
        }

        Var("la vertical de la cara de la tierra", m.VarVertTierra, m.SepVertTierraCm);
        Var("la vertical de la cara exterior", m.VarVertExt, m.SepVertExtCm);
        Var("la horizontal de la pantalla", m.VarHoriz, m.SepHorizCm);
        Var("el lecho superior de la zapata", m.VarZapSup, m.SepZapSupCm);
        Var("el lecho inferior de la zapata", m.VarZapInf, m.SepZapInfCm);
        Var("la reparticion de arriba de la zapata", m.VarRepSup, m.SepRepSupCm);
        Var("la reparticion de abajo de la zapata", m.VarRepInf, m.SepRepInfCm);

        if (m.Espolon)
        {
            Var("las espigas", m.VarEspiga, m.SepEspigaCm);

            if (m.LongEspigaM <= 0 || m.LongEspigaM > m.AlturaM)
            {
                p.Add("lo que suben las espigas tiene que estar entre cero y la altura de la pantalla");
            }
        }

        if (m.AnchoDetalleM <= 0) { p.Add("el ancho del detalle de la pantalla tiene que ser mayor que cero"); }

        return p;
    }

    // ======================================================================
    //  Dibujar
    // ======================================================================

    /// <summary>
    /// El dibujo completo de un muro, con su orilla izquierda -la del detalle de la pantalla
    /// en el concreto armado- en <paramref name="xIzquierda"/>.
    /// </summary>
    public static DibujoMuro Dibujar(MuroContencionCad m, double xIzquierda, double yBase)
    {
        var crudo = m.EsCiclopeo ? Ciclopeo(m, 0, 0) : Armado(m, 0, 0);

        return Mover(crudo, xIzquierda - crudo.XMin, yBase);
    }

    /// <summary>Lo que ocupa a lo ancho, para acomodar el siguiente.</summary>
    public static double Ancho(MuroContencionCad m)
    {
        var d = m.EsCiclopeo ? Ciclopeo(m, 0, 0) : Armado(m, 0, 0);
        return d.XMax - d.XMin;
    }

    /// <summary>El texto de una varilla: <c>Ø#5 @ 10 cm</c>.</summary>
    public static string TextoVarilla(VarMuro v, double sepCm) =>
        $"Ø{v.Clave} @ {sepCm.ToString("0.#", CultureInfo.InvariantCulture)} cm";

    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    // ----------------------------------------------------------------------
    //  Concreto armado
    // ----------------------------------------------------------------------

    private static DibujoMuro Armado(MuroContencionCad m, double x0, double y0)
    {
        var d = new DibujoMuro();

        var B = m.BaseM;
        var P = m.PuntaM;
        var eB = m.EspesorPieM;
        var c = m.CoronaM;
        var H = m.AlturaM;
        var dz = m.EspesorZapataM;
        var r = m.RecCm / 100;

        var xTierra = x0 + P + eB;               // la cara de la tierra, vertical
        var yZap = y0 + dz;                      // la cara de arriba de la zapata
        var yCorona = yZap + H;

        // La cara exterior, de (P, dz) a (P + eB - c, dz + H).
        double XExterior(double y) => x0 + P + ((eB - c) * (y - yZap) / H);

        var ed = m.Espolon ? m.EspolonDistM : 0;
        var ea = m.Espolon ? m.EspolonAnchoM : 0;
        var ep = m.Espolon ? m.EspolonProfM : 0;

        // ---------- El concreto: un solo poligono ----------
        var conc = new List<(double, double)> { (x0, y0) };

        if (m.Espolon)
        {
            conc.Add((x0 + ed, y0));
            conc.Add((x0 + ed, y0 - ep));
            conc.Add((x0 + ed + ea, y0 - ep));
            conc.Add((x0 + ed + ea, y0));
        }

        conc.Add((x0 + B, y0));
        conc.Add((x0 + B, yZap));
        conc.Add((xTierra, yZap));
        conc.Add((xTierra, yCorona));
        conc.Add((xTierra - c, yCorona));
        conc.Add((x0 + P, yZap));
        conc.Add((x0, yZap));
        d.Concreto.Add(conc);

        // ---------- El terreno ----------
        var f = FranjaTerreno;

        // El relleno detras del muro, a la altura de la corona.
        d.Terreno.Add(Rect(xTierra, yCorona, xTierra + Math.Max(1.2, m.TalonM + 0.3), yCorona + f));

        // El suelo bajo la zapata, a los dos lados del espolon.
        if (m.Espolon)
        {
            d.Terreno.Add(Rect(x0 - 0.3, y0 - f, x0 + ed - f, y0));
            d.Terreno.Add(Rect(x0 + ed + ea + f, y0 - f, x0 + B + 0.3, y0));
            d.Terreno.Add(Rect(x0 + ed - f, y0 - ep - f, x0 + ed, y0));
            d.Terreno.Add(Rect(x0 + ed + ea, y0 - ep - f, x0 + ed + ea + f, y0));
            d.Terreno.Add(Rect(x0 + ed - f, y0 - ep - f, x0 + ed + ea + f, y0 - ep));
        }
        else
        {
            d.Terreno.Add(Rect(x0 - 0.3, y0 - f, x0 + B + 0.3, y0));
        }

        // ---------- El acero de la zapata: un lecho arriba y otro abajo ----------
        // Cada lecho es su propia varilla, con su diametro: asi se dibuja con su grosor real y en
        // la capa de su diametro. Las patas del de arriba bajan y las del de abajo suben, sin
        // tocarse.
        var dZs = m.VarZapSup.M;
        var dZi = m.VarZapInf.M;
        var aZ = r + (Math.Max(dZs, dZi) / 2);
        var aS = r + (dZs / 2);
        var aI = r + (dZi / 2);
        var hueco = (yZap - y0) - (2 * r);
        var pataS = Math.Min(12 * dZs, 0.42 * hueco);
        var pataI = Math.Min(12 * dZi, 0.42 * hueco);

        d.Varillas.Add(new TrazoPoli(
            Redondear(new List<(double, double)>
            {
                (x0 + aS, yZap - aS - pataS), (x0 + aS, yZap - aS),
                (x0 + B - aS, yZap - aS), (x0 + B - aS, yZap - aS - pataS)
            }, 3 * dZs, false),
            false, m.VarZapSup.Clave, false));

        d.Varillas.Add(new TrazoPoli(
            Redondear(new List<(double, double)>
            {
                (x0 + aI, y0 + aI + pataI), (x0 + aI, y0 + aI),
                (x0 + B - aI, y0 + aI), (x0 + B - aI, y0 + aI + pataI)
            }, 3 * dZi, false),
            false, m.VarZapInf.Clave, false));

        // La reparticion: de punta, pegada por dentro de cada lecho.
        var dRs = m.VarRepSup.M;
        var dRi = m.VarRepInf.M;
        var yRepSup = yZap - aS - (dZs / 2) - (dRs / 2);
        var yRepInf = y0 + aI + (dZi / 2) + (dRi / 2);

        foreach (var x in Repartir(x0 + aZ + 0.05, x0 + B - aZ - 0.05, m.SepRepSupCm / 100))
        {
            d.Puntos.Add(new TrazoPunto(x, yRepSup, dRs / 2, m.VarRepSup.Clave));
        }

        foreach (var x in Repartir(x0 + aZ + 0.05, x0 + B - aZ - 0.05, m.SepRepInfCm / 100))
        {
            d.Puntos.Add(new TrazoPunto(x, yRepInf, dRi / 2, m.VarRepInf.Clave));
        }

        // ---------- El acero de la pantalla: un lazo de las dos caras ----------
        var dVt = m.VarVertTierra.M;
        var dVe = m.VarVertExt.M;
        var dH = m.VarHoriz.M;

        var cos = H / Math.Sqrt((H * H) + ((eB - c) * (eB - c)));
        var desdeExterior = (r + (dVe / 2)) / cos;

        var xBarTierra = xTierra - r - (dVt / 2);
        double XBarExterior(double y) => XExterior(y) + desdeExterior;

        var yArriba = yCorona - r - (Math.Max(dVt, dVe) / 2);
        var yAbajo = y0 + aZ + dZi + dRi + PieDelAcero;

        // Una varilla por cara, cada una con SU diametro. La de la tierra cruza la corona por
        // arriba; la exterior dobla por debajo de ella. Las dos llevan su bota en la zapata: la de
        // la tierra hacia el talon y la exterior hacia la punta.
        var yT = yCorona - r - (dVt / 2);
        var yE = yT - (dVt / 2) - (dVe / 2);
        var bota = 0.30;

        d.Varillas.Add(new TrazoPoli(
            Redondear(new List<(double, double)>
            {
                (Math.Min(xBarTierra + bota, x0 + B - r), yAbajo), (xBarTierra, yAbajo),
                (xBarTierra, yT), (XBarExterior(yT), yT)
            }, 3 * dVt, false),
            false, m.VarVertTierra.Clave, false));

        d.Varillas.Add(new TrazoPoli(
            Redondear(new List<(double, double)>
            {
                (Math.Max(XBarExterior(yAbajo) - bota, x0 + r), yAbajo), (XBarExterior(yAbajo), yAbajo),
                (XBarExterior(yE), yE), (xBarTierra - (dVt / 2) - (dVe / 2), yE)
            }, 3 * dVe, false),
            false, m.VarVertExt.Clave, false));

        // Las horizontales, de punta, por dentro de las verticales de las dos caras.
        var paso = m.SepHorizCm / 100;
        var ysH = Repartir(yZap + paso, yArriba - (paso / 3), paso);

        foreach (var y in ysH)
        {
            d.Puntos.Add(new TrazoPunto(xBarTierra - (dVt / 2) - (dH / 2), y, dH / 2, m.VarHoriz.Clave));
            d.Puntos.Add(new TrazoPunto(XBarExterior(y) + (dVe / 2) + (dH / 2), y, dH / 2, m.VarHoriz.Clave));
        }

        // ---------- El espolon y sus espigas ----------
        var xEspiga = 0d;

        if (m.Espolon)
        {
            // En el centro del espolon, metida en la pantalla si el espolon se sale de ella.
            var xMinEsp = XBarExterior(yZap) + 0.05;
            var xMaxEsp = xBarTierra - 0.05;
            xEspiga = Math.Clamp(x0 + ed + (ea / 2), Math.Min(xMinEsp, xMaxEsp), Math.Max(xMinEsp, xMaxEsp));

            d.Varillas.Add(new TrazoPoli(
                new List<(double, double)> { (xEspiga, y0 - ep + r), (xEspiga, yZap + m.LongEspigaM) },
                false, m.VarEspiga.Clave, true));
        }

        // ---------- Las llamadas ----------

        void Derecha(string texto, double xp, double yp, double xTexto, double yTexto) =>
            d.Rotulos.Add(LlamadaDerecha(texto, xp, yp, xTexto - 0.08, yTexto));

        void Izquierda(string texto, double xp, double yp, double xFinTexto, double yTexto) =>
            d.Rotulos.Add(LlamadaIzquierda(texto, xp, yp, xFinTexto + 0.08, yTexto));

        var xDer = xTierra + 0.55;

        Derecha(TextoVarilla(m.VarVertTierra, m.SepVertTierraCm),
            xBarTierra, yZap + (0.70 * H), xDer, yZap + (0.74 * H));

        Derecha(TextoVarilla(m.VarHoriz, m.SepHorizCm),
            xBarTierra - (dVt / 2) - (dH / 2), Cerca(ysH, yZap + (0.45 * H)), xDer, yZap + (0.50 * H));

        // Las dos de la cara exterior acaban en la MISMA columna, la de la mas baja: la cara es
        // inclinada y, si cada una se apartara de ella lo mismo, saldrian escalonadas.
        var xFinIzq = XExterior(yZap + (0.40 * H)) - 0.35;

        Izquierda(TextoVarilla(m.VarVertExt, m.SepVertExtCm),
            XBarExterior(yZap + (0.60 * H)), yZap + (0.60 * H),
            xFinIzq, yZap + (0.66 * H));

        Izquierda(TextoVarilla(m.VarHoriz, m.SepHorizCm),
            XBarExterior(Cerca(ysH, yZap + (0.35 * H))) + (dVe / 2) + (dH / 2), Cerca(ysH, yZap + (0.35 * H)),
            xFinIzq, yZap + (0.40 * H));

        if (m.Espolon)
        {
            Derecha("Espigas " + TextoVarilla(m.VarEspiga, m.SepEspigaCm),
                xEspiga, yZap + (m.LongEspigaM * 0.6), xDer, yZap + (0.22 * H));
        }

        // La zapata: arriba sobre el talon, abajo bajo la punta. Las dos de arriba en la misma
        // columna, y las flechas por la izquierda de esa columna: la de la de arriba baja por
        // fuera del texto de la de abajo, sin tacharlo.
        var xTalon = xTierra + (m.TalonM / 2);
        var xZap = xTalon + 0.40;

        Derecha(TextoVarilla(m.VarZapSup, m.SepZapSupCm),
            xTalon, yZap - aS, xZap, yZap + 0.62);

        Derecha(TextoVarilla(m.VarRepSup, m.SepRepSupCm),
            Cerca(d.Puntos.Where(p => Math.Abs(p.Y - yRepSup) < 1e-9).Select(p => p.X).ToList(), xTalon + 0.25),
            yRepSup, xZap, yZap + 0.30);

        Izquierda(TextoVarilla(m.VarZapInf, m.SepZapInfCm),
            x0 + (P / 2), y0 + aI, x0 - 0.15, y0 - 0.30);

        Izquierda(TextoVarilla(m.VarRepInf, m.SepRepInfCm),
            Cerca(d.Puntos.Where(p => Math.Abs(p.Y - yRepInf) < 1e-9).Select(p => p.X).ToList(), x0 + (P * 0.3)),
            yRepInf, x0 - 0.15, y0 + (dz / 2));

        // ---------- Las cotas ----------
        var s = SepCota;
        var xCotaDer = x0 + B + s;

        d.Cotas.Add(new TrazoCota(x0 + B, yZap, x0 + B, yCorona, xCotaDer, (yZap + yCorona) / 2, true));
        d.Cotas.Add(new TrazoCota(x0 + B, y0, x0 + B, yZap, xCotaDer, (y0 + yZap) / 2, true));

        if (m.Espolon)
        {
            d.Cotas.Add(new TrazoCota(x0 + ed + ea, y0 - ep, x0 + ed + ea, y0,
                x0 + ed + ea + s, y0 - (ep / 2), true));
        }

        // Arriba de la zapata: punta, pie de la pantalla y talon, en cadena.
        var yCadena = yZap + s;
        d.Cotas.Add(new TrazoCota(x0, yZap, x0 + P, yZap, x0 + (P / 2), yCadena, false));
        d.Cotas.Add(new TrazoCota(x0 + P, yZap, xTierra, yZap, x0 + P + (eB / 2), yCadena + 0.25, false));

        if (m.TalonM > 1e-6)
        {
            d.Cotas.Add(new TrazoCota(xTierra, yZap, x0 + B, yZap, xTierra + (m.TalonM / 2), yCadena + 0.9, false));
        }

        // La corona.
        d.Cotas.Add(new TrazoCota(xTierra - c, yCorona, xTierra, yCorona, xTierra - (c / 2), yCorona + s, false));

        // Abajo: el espolon y el ancho total.
        var yFondo = y0 - ep;

        if (m.Espolon)
        {
            d.Cotas.Add(new TrazoCota(x0, yFondo, x0 + ed, yFondo, x0 + (ed / 2), yFondo - s, false));
            d.Cotas.Add(new TrazoCota(x0 + ed, yFondo, x0 + ed + ea, yFondo, x0 + ed + (ea / 2), yFondo - s, false));
        }

        d.Cotas.Add(new TrazoCota(x0, yFondo, x0 + B, yFondo, x0 + (B / 2), yFondo - (2.2 * s), false));

        // Lo que suben las espigas.
        if (m.Espolon)
        {
            d.Cotas.Add(new TrazoCota(xTierra, yZap, xTierra, yZap + m.LongEspigaM,
                xTierra + 0.25, yZap + (m.LongEspigaM / 2), true));
        }

        // ---------- El detalle «acero en la pantalla» ----------
        DetalleDePantalla(d, m, x0 - AireDetalle - m.AnchoDetalleM, yZap);

        // ---------- El rotulo ----------
        var yMinCuerpo = yFondo - (2.2 * s) - 0.2;
        Rotulo(d, m, "MURO DE CONTENCION DE CONCRETO ARMADO", x0 + (B / 2), yMinCuerpo,
            $"f'c = {m.Fc} kg/cm²     Rec. {m.RecCm.ToString("0.#", CultureInfo.InvariantCulture)} cm     Escala 1:{m.Escala}");

        Limites(d, yMinCuerpo - (2 * AltoTitulo) - 0.4, yCorona + FranjaTerreno + 0.6);

        return d;
    }

    /// <summary>El alzado de un tramo de pantalla con su malla y sus espigas, como la imagen.</summary>
    private static void DetalleDePantalla(DibujoMuro d, MuroContencionCad m, double xe, double yZap)
    {
        var W = m.AnchoDetalleM;
        var H = m.AlturaM;
        var r = m.RecCm / 100;
        var yTop = yZap + H;

        d.Lineas.Add(new TrazoPoli(Rect(xe, yZap, xe + W, yTop), true, string.Empty, false));

        // Las verticales de la cara de la tierra.
        var xsV = Repartir(xe + r, xe + W - r, m.SepVertTierraCm / 100);

        foreach (var x in xsV)
        {
            d.Varillas.Add(new TrazoPoli(new List<(double, double)> { (x, yZap + 0.02), (x, yTop - r) },
                false, m.VarVertTierra.Clave, false));
        }

        // Las horizontales.
        var ysH = Repartir(yZap + (m.SepHorizCm / 100), yTop - r, m.SepHorizCm / 100);

        foreach (var y in ysH)
        {
            d.Varillas.Add(new TrazoPoli(new List<(double, double)> { (xe + 0.02, y), (xe + W - 0.02, y) },
                false, m.VarHoriz.Clave, false));
        }

        // Las espigas, a trazos, entre las verticales.
        var xsE = new List<double>();

        if (m.Espolon)
        {
            var sE = m.SepEspigaCm / 100;
            xsE = Repartir(xe + r + (sE / 2), xe + W - r, sE);

            foreach (var x in xsE)
            {
                d.Varillas.Add(new TrazoPoli(new List<(double, double)> { (x, yZap), (x, yZap + m.LongEspigaM) },
                    false, m.VarEspiga.Clave, true));
            }

            // Lo que suben, al lado.
            d.Cotas.Add(new TrazoCota(xe + W, yZap, xe + W, yZap + m.LongEspigaM,
                xe + W + 0.25, yZap + (m.LongEspigaM / 2), true));
        }

        // Las llamadas, a la derecha del detalle.
        var xT = xe + W + 0.45;

        void Llamada(string texto, double xp, double yp, double yT) =>
            d.Rotulos.Add(LlamadaDerecha(texto, xp, yp, xT - 0.08, yT));

        Llamada(TextoVarilla(m.VarHoriz, m.SepHorizCm),
            xe + (0.75 * W), Cerca(ysH, yZap + (0.85 * H)), yZap + (0.92 * H));

        Llamada(TextoVarilla(m.VarVertTierra, m.SepVertTierraCm),
            Cerca(xsV, xe + (0.85 * W)), yZap + (0.78 * H), yZap + (0.80 * H));

        if (m.Espolon && xsE.Count > 0)
        {
            Llamada(TextoVarilla(m.VarEspiga, m.SepEspigaCm),
                xsE[^1], yZap + (m.LongEspigaM * 0.85), yZap + (0.68 * H));
        }

        d.Textos.Add(new TrazoTexto("ACERO EN LA PANTALLA", xe + (W / 2), yTop + 0.35, AltoSubtitulo));

        if (m.Espolon)
        {
            d.Textos.Add(new TrazoTexto("Las espigas solo son necesarias cuando hay espolon.",
                xe + (W / 2), yZap - 0.35, AltoRotulo));
        }
    }

    // ----------------------------------------------------------------------
    //  Ciclopeo
    // ----------------------------------------------------------------------

    private static DibujoMuro Ciclopeo(MuroContencionCad m, double x0, double y0)
    {
        var d = new DibujoMuro();

        var B = m.BaseCiclopeoM;
        var dz = m.EspesorZapataM;
        var h = m.AlturaM;
        var yZap = y0 + dz;
        var yTop = yZap + h;

        var xTierra = x0 + m.MM + m.EM + m.GM;     // cara de la tierra, vertical
        var xPie = x0 + m.MM;                      // pie de la cara inclinada
        var xCoronaIzq = xTierra - (m.CM + m.BM);

        d.Concreto.Add(new List<(double, double)>
        {
            (x0, y0), (x0 + B, y0), (x0 + B, yZap), (xTierra, yZap),
            (xTierra, yTop), (xCoronaIzq, yTop), (xPie, yZap), (x0, yZap)
        });

        var f = FranjaTerreno;
        d.Terreno.Add(Rect(x0 - 0.3, y0 - f, x0 + B + 0.3, y0));
        d.Terreno.Add(Rect(xTierra, yTop, xTierra + Math.Max(1.0, m.NM + 0.4), yTop + f));

        // ---------- Las cotas, con las letras de su dibujo ----------
        var s = SepCota;

        // Abajo, en cadena: M, E, G, N, y el total.
        var yC = y0 - f - s;
        var x = x0;

        foreach (var (largo, letra) in new[] { (m.MM, "M"), (m.EM, "E"), (m.GM, "G"), (m.NM, "N") })
        {
            if (largo > 1e-6)
            {
                d.Cotas.Add(new TrazoCota(x, y0, x + largo, y0, x + (largo / 2), yC, false));
                d.Textos.Add(new TrazoTexto(letra, x + (largo / 2), yC - 0.28, AltoRotulo));
            }

            x += largo;
        }

        d.Cotas.Add(new TrazoCota(x0, y0, x0 + B, y0, x0 + (B / 2), yC - 0.6, false));

        // Arriba: c y b.
        var yTopC = yTop + f + s;

        if (m.CM > 1e-6)
        {
            d.Cotas.Add(new TrazoCota(xCoronaIzq, yTop, xCoronaIzq + m.CM, yTop, xCoronaIzq + (m.CM / 2), yTopC, false));
            d.Textos.Add(new TrazoTexto("c", xCoronaIzq + (m.CM / 2), yTopC + 0.22, AltoRotulo));
        }

        if (m.BM > 1e-6)
        {
            d.Cotas.Add(new TrazoCota(xTierra - m.BM, yTop, xTierra, yTop, xTierra - (m.BM / 2), yTopC, false));
            d.Textos.Add(new TrazoTexto("b", xTierra - (m.BM / 2), yTopC + 0.22, AltoRotulo));
        }

        // A la izquierda: h y d.
        var xIzq = x0 - s;
        d.Cotas.Add(new TrazoCota(x0, yZap, x0, yTop, xIzq, (yZap + yTop) / 2, true));
        d.Cotas.Add(new TrazoCota(x0, y0, x0, yZap, xIzq, (y0 + yZap) / 2, true));
        d.Textos.Add(new TrazoTexto("h", xIzq - 0.3, (yZap + yTop) / 2, AltoRotulo));
        d.Textos.Add(new TrazoTexto("d", xIzq - 0.3, (y0 + yZap) / 2, AltoRotulo));

        var piedra = string.IsNullOrWhiteSpace(m.PiedraPct) ? string.Empty : $"     Piedra {m.PiedraPct.Trim().TrimEnd('%')}%";
        var yMin = yC - 0.6 - 0.25;

        Rotulo(d, m, "MURO DE CONTENCION DE CONCRETO CICLOPEO", x0 + (B / 2), yMin,
            $"f'c = {m.Fc} kg/cm²{piedra}     Escala 1:{m.Escala}");

        Limites(d, yMin - (2 * AltoTitulo) - 0.4, yTopC + 0.5);

        return d;
    }

    // ----------------------------------------------------------------------
    //  Ayudas
    // ----------------------------------------------------------------------

    private static void Rotulo(DibujoMuro d, MuroContencionCad m, string que, double x, double y, string datos)
    {
        d.Textos.Add(new TrazoTexto($"{que} \"{m.Id.Trim()}\"", x, y - AltoTitulo, AltoTitulo));
        d.Textos.Add(new TrazoTexto(datos, x, y - (2 * AltoTitulo) - 0.1, AltoSubtitulo));
    }

    /// <summary>Los limites del dibujo, contando lo que sobresale: textos y cotas.</summary>
    private static void Limites(DibujoMuro d, double yMin, double yMax)
    {
        var xs = new List<double>();

        foreach (var p in d.Concreto.Concat(d.Terreno)) { xs.AddRange(p.Select(q => q.X)); }
        foreach (var l in d.Lineas) { xs.AddRange(l.Puntos.Select(q => q.X)); }
        foreach (var c in d.Cotas) { xs.Add(c.Xt + 0.3); }
        foreach (var r in d.Rotulos)
        {
            xs.Add(Math.Min(r.XCodo, r.XHombro));
            xs.Add(Math.Max(r.XCodo, r.XHombro));
        }

        // Y los textos centrados: el titulo de un muro angosto es mas ancho que el muro, y sin
        // contarlo se encimaba con el titulo del siguiente.
        foreach (var t in d.Textos)
        {
            var w = AnchoTexto(t.Texto, t.Alto);
            xs.Add(t.X - (w / 2));
            xs.Add(t.X + (w / 2));
        }

        d.XMin = xs.Min() - 0.2;
        d.XMax = xs.Max() + 0.2;
        d.YMin = yMin;
        d.YMax = yMax;
    }

    private static DibujoMuro Mover(DibujoMuro o, double dx, double dy)
    {
        (double X, double Y) M((double X, double Y) p) => (p.X + dx, p.Y + dy);

        var d = new DibujoMuro
        {
            XMin = o.XMin + dx, XMax = o.XMax + dx, YMin = o.YMin + dy, YMax = o.YMax + dy
        };

        d.Concreto.AddRange(o.Concreto.Select(p => p.Select(M).ToList()));
        d.Terreno.AddRange(o.Terreno.Select(p => p.Select(M).ToList()));
        d.Lineas.AddRange(o.Lineas.Select(l => l with { Puntos = l.Puntos.Select(M).ToList() }));
        d.Varillas.AddRange(o.Varillas.Select(l => l with { Puntos = l.Puntos.Select(M).ToList() }));
        d.Puntos.AddRange(o.Puntos.Select(p => p with { X = p.X + dx, Y = p.Y + dy }));
        d.Cotas.AddRange(o.Cotas.Select(c => new TrazoCota(
            c.X1 + dx, c.Y1 + dy, c.X2 + dx, c.Y2 + dy, c.Xt + dx, c.Yt + dy, c.Vertical)));
        d.Rotulos.AddRange(o.Rotulos.Select(r => r with
        {
            XPunta = r.XPunta + dx, YPunta = r.YPunta + dy, XCodo = r.XCodo + dx,
            YCodo = r.YCodo + dy, XHombro = r.XHombro + dx, XTexto = r.XTexto + dx, YTexto = r.YTexto + dy
        }));
        d.Textos.AddRange(o.Textos.Select(t => t with { X = t.X + dx, Y = t.Y + dy }));

        return d;
    }

    private static List<(double X, double Y)> Rect(double x1, double y1, double x2, double y2) =>
        new() { (x1, y1), (x2, y1), (x2, y2), (x1, y2) };

    /// <summary>Puntos de <paramref name="desde"/> a <paramref name="hasta"/> cada <paramref name="paso"/>.</summary>
    /// <remarks>Se topa en 400 para que una separacion mal capturada -1 cm en 5 m- no congele el dibujo.</remarks>
    public static List<double> Repartir(double desde, double hasta, double paso)
    {
        var res = new List<double>();

        if (paso <= 0 || hasta < desde)
        {
            return res;
        }

        for (var v = desde; v <= hasta + 1e-9 && res.Count < 400; v += paso)
        {
            res.Add(v);
        }

        return res;
    }

    private static double Cerca(List<double> valores, double objetivo) =>
        valores.Count == 0 ? objetivo : valores.OrderBy(v => Math.Abs(v - objetivo)).First();

    /// <summary>
    /// Redondea las esquinas de una poligonal con arcos de <paramref name="radio"/>, muestreados.
    /// </summary>
    /// <remarks>El acero no se dobla en pico: el lazo de la zapata y el de la pantalla llevan sus dobleces.</remarks>
    public static List<(double X, double Y)> Redondear(List<(double X, double Y)> esquinas, double radio, bool cerrada)
    {
        var n = esquinas.Count;

        if (radio <= 0 || n < 3)
        {
            return esquinas;
        }

        var res = new List<(double X, double Y)>();

        for (var i = 0; i < n; i++)
        {
            if (!cerrada && (i == 0 || i == n - 1))
            {
                res.Add(esquinas[i]);
                continue;
            }

            var a = esquinas[(i - 1 + n) % n];
            var p = esquinas[i];
            var b = esquinas[(i + 1) % n];

            var (u1x, u1y, l1) = Dir(p, a);
            var (u2x, u2y, l2) = Dir(p, b);

            var rr = Math.Min(radio, Math.Min(l1, l2) / 2);
            var p1 = (X: p.X + (u1x * rr), Y: p.Y + (u1y * rr));
            var p2 = (X: p.X + (u2x * rr), Y: p.Y + (u2y * rr));

            // Bezier cuadratica con el vertice como control: se ve como el doblez y no
            // necesita saber el angulo.
            for (var k = 0; k <= 6; k++)
            {
                var t = k / 6.0;
                var w0 = (1 - t) * (1 - t);
                var w1 = 2 * (1 - t) * t;
                var w2 = t * t;
                res.Add(((w0 * p1.X) + (w1 * p.X) + (w2 * p2.X), (w0 * p1.Y) + (w1 * p.Y) + (w2 * p2.Y)));
            }
        }

        return res;
    }

    private static (double Ux, double Uy, double L) Dir((double X, double Y) desde, (double X, double Y) hacia)
    {
        var dx = hacia.X - desde.X;
        var dy = hacia.Y - desde.Y;
        var l = Math.Sqrt((dx * dx) + (dy * dy));
        return l < 1e-12 ? (0, 0, 0) : (dx / l, dy / l, l);
    }
}
