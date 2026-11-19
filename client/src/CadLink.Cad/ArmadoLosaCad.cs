namespace CadLink.Cad;

/// <summary>De entrepiso (carga hacia abajo) o de cimentacion (la reaccion del terreno la empuja hacia arriba).</summary>
public enum TipoLosa
{
    Entrepiso,
    Cimentacion
}

/// <summary>Lo que hay en una orilla del tablero: otro tablero (continuo) o nada (borde).</summary>
public enum BordeLosa
{
    Continuo,
    Borde
}

/// <summary>Un tablero de losa con su armado, como lo manda la hoja de CadLink. Medidas en metros y cm.</summary>
public sealed class ArmadoLosaCad
{
    public string Id { get; init; } = string.Empty;

    public TipoLosa Tipo { get; init; } = TipoLosa.Entrepiso;

    /// <summary>Claro en X, de eje a eje de los apoyos, en metros.</summary>
    public double ClaroXM { get; init; }

    /// <summary>Claro en Y, de eje a eje, en metros.</summary>
    public double ClaroYM { get; init; }

    public double EspesorCm { get; init; }

    public double RecubrimientoCm { get; init; }

    /// <summary>Ancho de la trabe o contratrabe de apoyo, en cm. El claro libre es el de ejes menos esto.</summary>
    public double ApoyoAnchoCm { get; init; }

    /// <summary>Peralte total del apoyo, en cm. Solo se dibuja.</summary>
    public double ApoyoPeralteCm { get; init; }

    /// <summary>Las varillas que corren en X y su separacion de parrilla.</summary>
    public VarCad VarX { get; init; }

    public double SepXCm { get; init; }

    public VarCad VarY { get; init; }

    public double SepYCm { get; init; }

    /// <summary>Varilla de los bastones. Sin ella, la misma de cada direccion.</summary>
    public VarCad Baston { get; init; }

    public BordeLosa Izquierda { get; init; } = BordeLosa.Borde;

    public BordeLosa Derecha { get; init; } = BordeLosa.Borde;

    public BordeLosa Abajo { get; init; } = BordeLosa.Borde;

    public BordeLosa Arriba { get; init; } = BordeLosa.Borde;

    public string Fc { get; init; } = string.Empty;

    /// <summary>Escala del plano, 1:N. Con ella se dan los altos de texto.</summary>
    public double EscalaPlano { get; init; } = 50;

    public string Nombre => Tipo == TipoLosa.Cimentacion ? "LOSA DE CIMENTACION" : "LOSA DE ENTREPISO";

    /// <summary>El nombre del bloque en AutoCAD: no choca con el de una seccion del mismo ID.</summary>
    public string Bloque => "LOSA-" + Id.Trim();
}

/// <summary>Una polilinea abierta o cerrada. <see cref="Ancho"/> mayor que cero es una varilla con su grueso.</summary>
public sealed record TrazoLosa(List<(double X, double Y)> Puntos, string Capa, double Ancho = 0, bool Cerrado = false);

/// <summary>Un texto. <see cref="Anclaje"/> es el AttachmentPoint de AutoCAD: 1 arriba-izq, 2 arriba-centro, 5 centro, 7 abajo-izq, 8 abajo-centro.</summary>
public sealed record TextoLosa(double X, double Y, string Texto, double Alto, int Anclaje = 7, string Capa = "TEXTOS");

/// <summary>Una cota alineada con su texto puesto.</summary>
public sealed record CotaLosa(double X1, double Y1, double X2, double Y2, double XTexto, double YTexto, string Texto);

/// <summary>Una varilla vista de punta.</summary>
public sealed record PuntoLosa(double X, double Y, double Radio, string Clave);

/// <summary>Todo lo que se dibuja de un tablero, en metros, con el origen abajo a la izquierda de la planta.</summary>
public sealed class DibujoLosa
{
    public List<TrazoLosa> Trazos { get; } = new();

    public List<TextoLosa> Textos { get; } = new();

    public List<CotaLosa> Cotas { get; } = new();

    public List<PuntoLosa> Puntos { get; } = new();

    public List<string> Avisos { get; } = new();

    public (double XMin, double YMin, double XMax, double YMax) Caja()
    {
        var xs = Trazos.SelectMany(t => t.Puntos.Select(p => p.X)).Concat(Textos.Select(t => t.X)).ToList();
        var ys = Trazos.SelectMany(t => t.Puntos.Select(p => p.Y)).Concat(Textos.Select(t => t.Y)).ToList();

        return xs.Count == 0 ? (0, 0, 0, 0) : (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}

/// <summary>Una varilla de la franja, a lo largo del claro: su eje y sus ganchos ya puestos.</summary>
public sealed record VarillaDeFranja(string Que, List<(double S, double Z)> Eje, VarCad Var);

/// <summary>
/// El armado de una losa en un sentido: <b>corridas</b>, <b>bayonetas</b> y <b>bastones a L/4</b> del claro.
/// </summary>
/// <remarks>
/// <para>
/// La parrilla va a la separacion <c>s</c> de la hoja, alternando: abajo al centro del claro, una
/// corrida y una bayoneta (corridas @ 2s y bayonetas @ 2s). Sobre los apoyos, arriba, una bayoneta
/// y un baston (bastones @ 2s). Asi el acero queda a <c>s</c> donde trabaja.
/// </para>
/// <para>
/// <b>L/4 es del claro LIBRE</b>, de paño a paño del apoyo, y se mide desde el paño: es lo que
/// sube la bayoneta y lo que entra el baston en el claro. Sobre un apoyo continuo el baston y la
/// bayoneta entran tambien L/4 al tablero vecino (se supone de claro parecido). En un borde se
/// anclan con gancho de 90° hasta el paño exterior menos el recubrimiento.
/// </para>
/// <para>
/// En la losa de <b>cimentacion</b> todo se invierte, porque el terreno empuja hacia arriba: las
/// corridas van arriba, la bayoneta va arriba al centro y abajo sobre las contratrabes, y los
/// bastones van abajo.
/// </para>
/// <para>
/// Coordenadas: <c>S</c> a lo largo del claro desde el eje del primer apoyo, <c>Z</c> desde la cara
/// inferior de la losa. En metros.
/// </para>
/// </remarks>
public static class GeometriaArmadoLosa
{
    /// <summary>Gancho de 90° en diametros.</summary>
    public const double GanchoDiametros = 12;

    /// <summary>Claro libre: de paño a paño.</summary>
    public static double ClaroLibre(double claroEjesM, double apoyoAnchoCm) => claroEjesM - (apoyoAnchoCm / 100);

    /// <summary>Lo que entra el baston y donde dobla la bayoneta, desde el paño.</summary>
    public static double CuartoDelClaro(double claroEjesM, double apoyoAnchoCm) => ClaroLibre(claroEjesM, apoyoAnchoCm) / 4;

    /// <summary>Las varillas de una franja.</summary>
    public static List<VarillaDeFranja> Franja(
        double claroEjesM, ArmadoLosaCad l, VarCad var, BordeLosa inicio, BordeLosa fin)
    {
        var res = new List<VarillaDeFranja>();
        var h = l.EspesorCm / 100;
        var rec = l.RecubrimientoCm / 100;
        var b = l.ApoyoAnchoCm / 100;
        var d = var.Cm / 100;
        var L = claroEjesM;
        var ln = L - b;

        if (ln <= 0 || h <= (2 * rec) + (2 * d) || !var.Existe)
        {
            return res;
        }

        var cuarto = ln / 4;
        var pIni = b / 2;
        var pFin = L - (b / 2);

        // Las dos camas, por el eje de la varilla: la de la cara que trabaja al centro y la otra.
        var zA = rec + (d / 2);            // la del lado de la carga: abajo en entrepiso
        var zB = h - rec - (d / 2);        // la del otro lado
        var inv = l.Tipo == TipoLosa.Cimentacion;

        // Al centro del claro el acero va del lado en tension; sobre los apoyos, del otro.
        var zCentro = inv ? zB : zA;
        var zApoyo = inv ? zA : zB;
        var run = Math.Abs(zB - zA);       // dobleces a 45°
        // El gancho no pasa de la otra cama: se queda un diametro antes.
        var gancho = Math.Min(GanchoDiametros * d, h - (2 * rec) - (2 * d));

        // Hacia donde va el gancho en un borde: hacia la otra cama.
        double Hacia(double z) => z > h / 2 ? -1 : 1;

        // ---- Corrida: la cama del centro, de lado a lado ----
        var corrida = new List<(double S, double Z)>();
        var sC0 = inicio == BordeLosa.Borde ? -(b / 2) + rec : 0;
        var sC1 = fin == BordeLosa.Borde ? L + (b / 2) - rec : L;

        if (inicio == BordeLosa.Borde)
        {
            corrida.Add((sC0, zCentro + (Hacia(zCentro) * gancho)));
        }

        corrida.Add((sC0, zCentro));
        corrida.Add((sC1, zCentro));

        if (fin == BordeLosa.Borde)
        {
            corrida.Add((sC1, zCentro + (Hacia(zCentro) * gancho)));
        }

        res.Add(new VarillaDeFranja("corridas", corrida, var));

        // ---- Bayoneta: en el apoyo arriba, dobla a L/4 del paño y baja al centro ----
        var bay = new List<(double S, double Z)>();
        var sB0 = inicio == BordeLosa.Borde ? -(b / 2) + rec : -(b / 2) - cuarto;
        var sB1 = fin == BordeLosa.Borde ? L + (b / 2) - rec : L + (b / 2) + cuarto;

        if (inicio == BordeLosa.Borde)
        {
            bay.Add((sB0, zApoyo + (Hacia(zApoyo) * gancho)));
        }

        bay.Add((sB0, zApoyo));
        bay.Add((pIni + cuarto - run, zApoyo));
        bay.Add((pIni + cuarto, zCentro));
        bay.Add((pFin - cuarto, zCentro));
        bay.Add((pFin - cuarto + run, zApoyo));
        bay.Add((sB1, zApoyo));

        if (fin == BordeLosa.Borde)
        {
            bay.Add((sB1, zApoyo + (Hacia(zApoyo) * gancho)));
        }

        res.Add(new VarillaDeFranja("bayonetas", bay, var));

        // ---- Bastones: sobre cada apoyo, L/4 del paño hacia el claro (y hacia el vecino si sigue) ----
        var vb = l.Baston.Existe ? l.Baston : var;
        var db = vb.Cm / 100;

        // Se dibujan un diametro hacia dentro de la cama: en la losa van entre las bayonetas, a la
        // misma altura, y encimados no se distinguirian en el corte.
        var zBaston = zApoyo + (zApoyo > h / 2 ? -(d + db) / 2 : (d + db) / 2);
        var ganchoB = Math.Min(GanchoDiametros * db, h - (2 * rec) - (2 * db));

        List<(double S, double Z)> Baston(double pano, double haciaClaro, BordeLosa borde, double ejeApoyo)
        {
            var p = new List<(double S, double Z)>();
            var dentro = pano + (haciaClaro * cuarto);
            var fuera = borde == BordeLosa.Borde
                ? ejeApoyo - (haciaClaro * ((b / 2) - rec))
                : ejeApoyo - (haciaClaro * ((b / 2) + cuarto));

            if (borde == BordeLosa.Borde)
            {
                p.Add((fuera, zBaston + (Hacia(zBaston) * ganchoB)));
            }

            p.Add((fuera, zBaston));
            p.Add((dentro, zBaston));

            return p;
        }

        res.Add(new VarillaDeFranja("baston izquierdo", Baston(pIni, 1, inicio, 0), vb));
        res.Add(new VarillaDeFranja("baston derecho", Baston(pFin, -1, fin, L), vb));

        return res;
    }

    /// <summary>Lo que no deja dibujar el tablero. Vacio si se puede.</summary>
    public static List<string> Problemas(ArmadoLosaCad l)
    {
        var p = new List<string>();
        var b = l.ApoyoAnchoCm / 100;

        if (l.ClaroXM <= b || l.ClaroYM <= b)
        {
            p.Add("los claros tienen que ser mayores que el ancho del apoyo");
        }

        foreach (var (v, que) in new[] { (l.VarX, "X"), (l.VarY, "Y") })
        {
            if (!v.Existe)
            {
                p.Add($"la varilla en {que}");
            }
            else if (l.EspesorCm <= (2 * l.RecubrimientoCm) + (2 * v.Cm))
            {
                p.Add($"espesor: con {l.RecubrimientoCm:0.#} cm de recubrimiento no caben las dos camas de {v.Clave}");
            }
        }

        if (l.SepXCm <= 0 || l.SepYCm <= 0)
        {
            p.Add("la separacion en X y en Y");
        }

        return p;
    }

    /// <summary>
    /// La planta del tablero con sus varillas abatidas y los dos cortes, en metros.
    /// </summary>
    /// <remarks>
    /// La planta va arriba, con el origen en el eje de abajo a la izquierda. Debajo, el corte A-A
    /// (las varillas en X) y el corte B-B (las de Y). Los altos de texto salen de la escala del plano:
    /// 2.5 mm los rotulos y 3.5 mm el titulo, ya en el papel.
    /// </remarks>
    public static DibujoLosa Dibujo(ArmadoLosaCad l)
    {
        var dib = new DibujoLosa();
        var problemas = Problemas(l);

        if (problemas.Count > 0)
        {
            dib.Avisos.AddRange(problemas);
            return dib;
        }

        var escala = l.EscalaPlano > 0 ? l.EscalaPlano : 50;
        var alto = 0.0025 * escala;
        var altoTitulo = 0.0035 * escala;
        var Lx = l.ClaroXM;
        var Ly = l.ClaroYM;
        var b = l.ApoyoAnchoCm / 100;
        var h = l.EspesorCm / 100;

        // ================= PLANTA =================
        Rect(dib, -b / 2, -b / 2, Lx + (b / 2), Ly + (b / 2), "CONCRETO");

        // Los paños de los apoyos, a trazos de linea oculta en la capa de concreto.
        Rect(dib, b / 2, b / 2, Lx - (b / 2), Ly - (b / 2), "CONCRETO");

        // Las varillas ABATIDAS: la franja en X se pinta acostada sobre la planta, a un tercio de
        // la altura, con su forma de verdad; la de Y, de pie a un tercio del ancho.
        var yFranjaX = Ly * 0.38;
        var xFranjaY = Lx * 0.38;
        var exageracion = Math.Max(1, 0.25 / Math.Max(h, 0.05));

        foreach (var v in Franja(Lx, l, l.VarX, l.Izquierda, l.Derecha))
        {
            dib.Trazos.Add(new TrazoLosa(
                v.Eje.Select(p => (p.S, yFranjaX + (p.Z * exageracion))).ToList(), Capa(v.Var), 0));
        }

        foreach (var v in Franja(Ly, l, l.VarY, l.Abajo, l.Arriba))
        {
            dib.Trazos.Add(new TrazoLosa(
                v.Eje.Select(p => (xFranjaY - (p.Z * exageracion), p.S)).ToList(), Capa(v.Var), 0));
        }

        var notasX = Notas(l, l.VarX, l.SepXCm, Lx);
        var notasY = Notas(l, l.VarY, l.SepYCm, Ly);

        dib.Textos.Add(new TextoLosa(xFranjaY + (b / 2) + (alto * 2), yFranjaX + (h * exageracion) + (alto * 1.5),
            "EN X:\\P" + string.Join("\\P", notasX), alto, 7));
        dib.Textos.Add(new TextoLosa(xFranjaY + (alto * 1.5), Ly * 0.80,
            "EN Y:\\P" + string.Join("\\P", notasY), alto, 1));

        dib.Cotas.Add(new CotaLosa(0, Ly + (b / 2), Lx, Ly + (b / 2), Lx / 2, Ly + (b / 2) + (alto * 3), $"{Lx:0.00} m a ejes"));
        dib.Cotas.Add(new CotaLosa(-(b / 2), 0, -(b / 2), Ly, -(b / 2) - (alto * 3), Ly / 2, $"{Ly:0.00} m a ejes"));

        foreach (var (texto, x, y, anclaje) in new[]
                 {
                     (Borde(l.Izquierda), -(b / 2) - (alto * 6), Ly * 0.12, 9),
                     (Borde(l.Derecha), Lx + (b / 2) + (alto * 0.8), Ly * 0.12, 7),
                     (Borde(l.Abajo), Lx * 0.70, -(b / 2) - (alto * 0.8), 2),
                     (Borde(l.Arriba), Lx * 0.70, Ly + (b / 2) + (alto * 0.8), 8)
                 })
        {
            dib.Textos.Add(new TextoLosa(x, y, texto, alto * 0.8, anclaje));
        }

        dib.Textos.Add(new TextoLosa(Lx / 2, Ly + (b / 2) + (alto * 6.5),
            $"PLANTA DE {l.Nombre} \"{l.Id}\"", altoTitulo, 8, "ROTULOS"));

        // ================= CORTES =================
        // Cada corte por debajo del anterior: lo que sube de la losa (apoyo, cotas y notas) y lo que
        // baja (apoyo, cota del claro y titulo).
        var pa = Math.Max(l.ApoyoPeralteCm / 100, h);
        var inv = l.Tipo == TipoLosa.Cimentacion;
        var sube = (inv ? pa : h) + (alto * 11);
        var baja = (inv ? 0 : pa - h) + (alto * 9);

        var techo = -(b / 2) - (alto * 6);
        var yA = techo - sube;
        Corte(dib, l, Lx, l.VarX, l.SepXCm, l.VarY, l.SepYCm, l.Izquierda, l.Derecha, yA, alto, "CORTE A-A (varillas en X)");
        var yB = yA - baja - (alto * 6) - sube;
        Corte(dib, l, Ly, l.VarY, l.SepYCm, l.VarX, l.SepXCm, l.Abajo, l.Arriba, yB, alto, "CORTE B-B (varillas en Y)");

        return dib;
    }

    /// <summary>Lo que dice cada grupo de varillas, con su separacion de verdad (2s) y el largo del baston.</summary>
    public static List<string> Notas(ArmadoLosaCad l, VarCad var, double sepCm, double claroEjesM)
    {
        var vb = l.Baston.Existe ? l.Baston : var;
        var cuarto = CuartoDelClaro(claroEjesM, l.ApoyoAnchoCm);
        var s2 = sepCm * 2;
        var lado = l.Tipo == TipoLosa.Cimentacion ? "lecho superior" : "lecho inferior";

        return new List<string>
        {
            $"Var. {var.Clave} @ {s2:0.#} cm corridas ({lado})",
            $"Var. {var.Clave} @ {s2:0.#} cm bayonetas, doblez a L/4 = {cuarto:0.00} m del paño",
            $"Bastones {vb.Clave} @ {s2:0.#} cm, L/4 = {cuarto:0.00} m del paño"
        };
    }

    private static string Borde(BordeLosa b) => b == BordeLosa.Borde ? "BORDE" : "CONTINUO";

    private static string Capa(VarCad v) => CapasCad.PrefijoVarilla + v.Clave;

    private static void Rect(DibujoLosa d, double x0, double y0, double x1, double y1, string capa) =>
        d.Trazos.Add(new TrazoLosa(new List<(double, double)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, capa, 0, true));

    /// <summary>Un corte: el concreto, sus apoyos, las varillas con su grueso, las de punta, cotas y notas.</summary>
    private static void Corte(
        DibujoLosa dib, ArmadoLosaCad l, double L, VarCad var, double sepCm, VarCad otra, double sepOtraCm,
        BordeLosa inicio, BordeLosa fin, double yLosa, double alto, string titulo)
    {
        var h = l.EspesorCm / 100;
        var b = l.ApoyoAnchoCm / 100;
        var pa = Math.Max(l.ApoyoPeralteCm / 100, h);
        var inv = l.Tipo == TipoLosa.Cimentacion;
        var ln = L - b;
        var cuarto = ln / 4;
        var rec = l.RecubrimientoCm / 100;

        // Lo que se ve del tablero vecino en un apoyo continuo: L/4 y un poco.
        var sMin = inicio == BordeLosa.Continuo ? -(b / 2) - cuarto - 0.15 : -(b / 2);
        var sMax = fin == BordeLosa.Continuo ? L + (b / 2) + cuarto + 0.15 : L + (b / 2);

        // La losa.
        Rect(dib, sMin, yLosa, sMax, yLosa + h, "CONCRETO");

        // Los apoyos: la trabe cuelga en el entrepiso; la contratrabe sube en la cimentacion.
        foreach (var eje in new[] { 0.0, L })
        {
            var z0 = inv ? h : -(pa - h);
            var z1 = inv ? pa : 0;
            Rect(dib, eje - (b / 2), yLosa + z0, eje + (b / 2), yLosa + z1, "CONCRETO");
        }

        // Las varillas, con su diametro de verdad.
        foreach (var v in Franja(L, l, var, inicio, fin))
        {
            dib.Trazos.Add(new TrazoLosa(v.Eje.Select(p => (p.S, yLosa + p.Z)).ToList(), Capa(v.Var), v.Var.Cm / 100));
        }

        // Las del otro sentido, de punta, en su cama: pegadas por dentro a las de este.
        if (otra.Existe && sepOtraCm > 0)
        {
            var d = var.Cm / 100;
            var r = otra.Cm / 200;
            var zIn = rec + d + r;
            var zSu = h - rec - d - r;
            var paso = sepOtraCm / 100;

            for (var s = (b / 2) + (paso / 2); s < L - (b / 2); s += paso)
            {
                dib.Puntos.Add(new PuntoLosa(s, yLosa + zIn, r, otra.Clave));
                dib.Puntos.Add(new PuntoLosa(s, yLosa + zSu, r, otra.Clave));
            }
        }

        // Cotas: el claro libre, los dos L/4 desde el paño y el espesor.
        var abajo = yLosa + (inv ? 0 : -(pa - h)) - (alto * 2.5);
        var arriba = yLosa + (inv ? pa : h) + (alto * 2.5);

        dib.Cotas.Add(new CotaLosa(b / 2, abajo + (alto * 2), L - (b / 2), abajo + (alto * 2), L / 2, abajo, $"Ln = {ln:0.00} m"));
        dib.Cotas.Add(new CotaLosa(b / 2, arriba - (alto * 2), (b / 2) + cuarto, arriba - (alto * 2), (b / 2) + (cuarto / 2), arriba, $"L/4 = {cuarto:0.00}"));
        dib.Cotas.Add(new CotaLosa(L - (b / 2) - cuarto, arriba - (alto * 2), L - (b / 2), arriba - (alto * 2), L - (b / 2) - (cuarto / 2), arriba, $"L/4 = {cuarto:0.00}"));
        dib.Cotas.Add(new CotaLosa(sMax, yLosa, sMax, yLosa + h, sMax + (alto * 2.5), yLosa + (h / 2), $"h = {l.EspesorCm:0.#} cm"));

        var notas = Notas(l, var, sepCm, L);
        dib.Textos.Add(new TextoLosa(L / 2, arriba + (alto * 2.5), string.Join("\\P", notas), alto, 8));

        var pie = abajo - (alto * 1.5);
        dib.Textos.Add(new TextoLosa(L / 2, pie, titulo
            + $"\\P{l.Nombre} \"{l.Id}\" · h = {l.EspesorCm:0.#} cm · rec. {l.RecubrimientoCm:0.#} cm"
            + (l.Fc.Trim().Length > 0 ? $" · f'c = {l.Fc.Trim()} kg/cm²" : string.Empty)
            + $" · Esc. 1:{l.EscalaPlano:0}", alto * 1.2, 2, "ROTULOS"));
    }
}
