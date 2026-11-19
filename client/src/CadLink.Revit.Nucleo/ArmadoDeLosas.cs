namespace CadLink.Revit.Nucleo;

/// <summary>Una orilla del tablero: si sigue otro tablero y cuanto mide lo que hay debajo.</summary>
/// <param name="Continuo">Del otro lado de la trabe hay otro tablero.</param>
/// <param name="AnchoM">
/// En un borde, el ancho del apoyo que la losa cubre (de su orilla a su paño interior). En un
/// continuo, la holgura entre los dos tableros: el ancho de la trabe.
/// </param>
/// <param name="ClaroVecinoM">El claro del tablero vecino, para meterle L/4. Cero = el mismo de este.</param>
/// <param name="ConBaston">
/// Si este tablero pone el baston de esta orilla. En una orilla continua entre dos tableros que se arman
/// juntos lo pone uno solo, para que no salga doble.
/// </param>
public sealed record ApoyoLosa(bool Continuo, double AnchoM, double ClaroVecinoM = 0, bool ConBaston = true);

/// <summary>Una varilla de la franja: su eje en (S, Z) y si va corrida media separacion.</summary>
/// <param name="Desfase">0 = en la primera posicion; 1 = una separacion despues (las bayonetas, entre las corridas).</param>
public sealed record VarillaDeLosa(string Que, List<(double S, double Z)> Eje, double DiamM, int Desfase);

/// <summary>Una caja en planta, en el marco del tablero (U a lo largo del claro, V a lo ancho), con sus cotas.</summary>
public sealed record CajaPlanta(double U0, double U1, double V0, double V1, double Z0, double Z1);

/// <summary>
/// El armado de una losa en un sentido: <b>corridas</b>, <b>bayonetas</b> y <b>bastones a L/4</b> del
/// claro libre. Es lo mismo que dibuja la pestaña Losas de CadLink, aqui para Revit.
/// </summary>
/// <remarks>
/// <para>
/// <c>S</c> va a lo largo del claro desde el PAÑO del primer apoyo (0) hasta el del otro (Ln); <c>Z</c>
/// desde la cara de abajo de la losa. En metros.
/// </para>
/// <para>
/// A la separacion <c>s</c> de la parrilla se alternan corrida y bayoneta (cada una @ 2s); sobre los
/// apoyos, arriba, bayoneta y baston (el baston @ 2s, en la posicion de la corrida). La bayoneta va
/// arriba sobre el apoyo, baja a 45° llegando abajo a L/4 del paño y vuelve a subir a L/4 del otro. El
/// baston entra L/4 desde el paño. En un borde se anclan con pata de 90° hasta la orilla menos el
/// recubrimiento; en un continuo pasan L/4 al tablero vecino.
/// </para>
/// <para>
/// En cimentacion se pasa <paramref name="zCentro"/> arriba y <paramref name="zApoyo"/> abajo: el
/// terreno empuja hacia arriba y todo se invierte.
/// </para>
/// </remarks>
public static class ArmadoDeLosas
{
    public const double GanchoDiametros = 12;

    /// <summary>Lo minimo que se acerca una orilla para tomarla como vecina, y lo mas que puede medir la trabe.</summary>
    public const double HolguraMaximaM = 1.0;

    public static List<VarillaDeLosa> Franja(
        double lnM, ApoyoLosa ini, ApoyoLosa fin, double zCentro, double zApoyo,
        double diamM, double diamBastonM, double recM)
    {
        var res = new List<VarillaDeLosa>();

        if (lnM <= 0.1 || diamM <= 0)
        {
            return res;
        }

        var cuarto = lnM / 4;
        var run = Math.Abs(zApoyo - zCentro);
        var db = diamBastonM > 0 ? diamBastonM : diamM;
        var signoCentro = Math.Sign(zApoyo - zCentro);   // hacia donde va la pata de lo que esta en zCentro
        var pata = Math.Max(0, Math.Min(GanchoDiametros * diamM, run - diamM));
        var pataB = Math.Max(0, Math.Min(GanchoDiametros * db, run - db));

        // Donde se ancla en cada lado, ya en S.
        double Fuera(ApoyoLosa a, bool inicio)
        {
            var vecino = (a.ClaroVecinoM > 0 ? a.ClaroVecinoM : lnM) / 4;
            var d = a.Continuo ? a.AnchoM + vecino : a.AnchoM - recM;
            return inicio ? -d : lnM + d;
        }

        double Eje(ApoyoLosa a, bool inicio)
        {
            var d = a.Continuo ? a.AnchoM / 2 : a.AnchoM - recM;
            return inicio ? -d : lnM + d;
        }

        // ---- Corrida ----
        var corrida = new List<(double S, double Z)>();
        var c0 = Eje(ini, true);
        var c1 = Eje(fin, false);

        if (!ini.Continuo && pata > 0) { corrida.Add((c0, zCentro + (signoCentro * pata))); }
        corrida.Add((c0, zCentro));
        corrida.Add((c1, zCentro));
        if (!fin.Continuo && pata > 0) { corrida.Add((c1, zCentro + (signoCentro * pata))); }
        res.Add(new VarillaDeLosa("corridas", corrida, diamM, 0));

        // ---- Bayoneta: solo si el claro da para los dos dobleces ----
        if (cuarto > run + 0.02)
        {
            var bay = new List<(double S, double Z)>();
            var b0 = Fuera(ini, true);
            var b1 = Fuera(fin, false);

            if (!ini.Continuo && pata > 0) { bay.Add((b0, zApoyo - (signoCentro * pata))); }
            bay.Add((b0, zApoyo));
            bay.Add((cuarto - run, zApoyo));
            bay.Add((cuarto, zCentro));
            bay.Add((lnM - cuarto, zCentro));
            bay.Add((lnM - cuarto + run, zApoyo));
            bay.Add((b1, zApoyo));
            if (!fin.Continuo && pata > 0) { bay.Add((b1, zApoyo - (signoCentro * pata))); }
            res.Add(new VarillaDeLosa("bayonetas", bay, diamM, 1));
        }

        // ---- Bastones: L/4 desde el paño ----
        if (ini.ConBaston)
        {
            var p = new List<(double S, double Z)>();
            var s0 = Fuera(ini, true);
            if (!ini.Continuo && pataB > 0) { p.Add((s0, zApoyo - (signoCentro * pataB))); }
            p.Add((s0, zApoyo));
            p.Add((cuarto, zApoyo));
            res.Add(new VarillaDeLosa("baston inicio", p, db, 0));
        }

        if (fin.ConBaston)
        {
            var p = new List<(double S, double Z)> { (lnM - cuarto, zApoyo) };
            var s1 = Fuera(fin, false);
            p.Add((s1, zApoyo));
            if (!fin.Continuo && pataB > 0) { p.Add((s1, zApoyo - (signoCentro * pataB))); }
            res.Add(new VarillaDeLosa("baston fin", p, db, 0));
        }

        return res;
    }

    /// <summary>
    /// La orilla del lado de U0 (<paramref name="inicio"/>) o de U1: continua si hay otra losa a menos de
    /// <see cref="HolguraMaximaM"/> del otro lado, a su misma cota y a lo largo de la orilla.
    /// </summary>
    /// <returns>La holgura y el claro del vecino, o null si es borde.</returns>
    public static (double Holgura, double ClaroVecino, int Indice)? Vecino(CajaPlanta yo, IReadOnlyList<CajaPlanta> otras, bool inicio)
    {
        (double, double, int)? mejor = null;
        var largo = yo.V1 - yo.V0;

        for (var i = 0; i < otras.Count; i++)
        {
            var o = otras[i];

            if (Math.Abs(o.Z1 - yo.Z1) > 0.05 || Solape(yo.V0, yo.V1, o.V0, o.V1) < Math.Min(0.3, largo * 0.3))
            {
                continue;
            }

            var holgura = inicio ? yo.U0 - o.U1 : o.U0 - yo.U1;

            if (holgura < -0.02 || holgura > HolguraMaximaM)
            {
                continue;
            }

            if (mejor is null || holgura < mejor.Value.Item1)
            {
                mejor = (Math.Max(0, holgura), o.U1 - o.U0, i);
            }
        }

        return mejor;
    }

    /// <summary>
    /// En un borde: lo que la losa cubre de su apoyo, desde su orilla hasta el paño interior de la trabe o
    /// el muro que corre debajo. Null si no hay ninguno.
    /// </summary>
    public static double? ApoyoDeBorde(CajaPlanta yo, IReadOnlyList<CajaPlanta> apoyos, bool inicio)
    {
        double? res = null;
        var largo = yo.V1 - yo.V0;

        foreach (var a in apoyos)
        {
            // Que toque la losa en altura y vaya a lo largo de la orilla.
            if (a.Z1 < yo.Z0 - 0.05 || a.Z0 > yo.Z1 + 0.05 || Solape(yo.V0, yo.V1, a.V0, a.V1) < Math.Min(0.3, largo * 0.3))
            {
                continue;
            }

            // Que este bajo la orilla: su caja cruza la orilla o empieza en ella, y no es mas ancha que un metro.
            var ancho = a.U1 - a.U0;

            if (ancho <= 0.02 || ancho > HolguraMaximaM)
            {
                continue;
            }

            var orilla = inicio ? yo.U0 : yo.U1;

            if ((inicio && a.U0 > orilla + 0.03) || (!inicio && a.U1 < orilla - 0.03))
            {
                continue;
            }

            var dentro = inicio ? a.U1 - yo.U0 : yo.U1 - a.U0;

            if (dentro > 0.02 && dentro <= HolguraMaximaM)
            {
                res = Math.Max(res ?? 0, dentro);
            }
        }

        return res;
    }

    /// <summary>
    /// Las posiciones a lo ancho de un grupo: desde el recubrimiento, a 2s, con <paramref name="desfase"/>
    /// separaciones de corrimiento. Devuelve la primera y cuantas van.
    /// </summary>
    public static (double Primera, int Cuantas) Reparto(double anchoM, double recM, double diamM, double sepM, int desfase)
    {
        var v0 = recM + (diamM / 2) + (desfase * sepM);
        var v1 = anchoM - recM - (diamM / 2);

        if (sepM <= 0 || v1 < v0)
        {
            return (v0, 0);
        }

        return (v0, (int)Math.Floor(((v1 - v0) / (2 * sepM)) + 1e-9) + 1);
    }

    private static double Solape(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0);
}
