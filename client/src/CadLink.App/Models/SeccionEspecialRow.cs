using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Un renglon de la hoja «Formas especiales»: un castillo, columna, cabezal o dado en L, en T o con
/// el contorno que se escriba, con su armado.
/// </summary>
/// <remarks>
/// <para>
/// Las medidas siguen el croquis de la hoja: B y H son la caja; en la L, T1 es el ala de abajo y T2
/// la de la izquierda; en la T, T1 es el patin de arriba y T2 el alma, centrada. Una forma
/// IRREGULAR se escribe por sus vertices, «x,y; x,y; …», en cm.
/// </para>
/// <para>
/// El armado se captura como se arma una forma asi: una varilla en cada esquina -tambien en las de
/// dentro de la L o la T- y, a lo largo de cada cara, las intermedias que hagan falta para no pasar
/// de la separacion maxima.
/// </para>
/// </remarks>
public sealed class SeccionEspecialRow : Row
{
    public static readonly string[] Elementos = { "CASTILLO", "COLUMNA", "CABEZAL", "DADO" };

    private string _elemento = "COLUMNA";
    private string _id = string.Empty;
    private string _forma = FormaEspecial.L;
    private double _bCm = 60;
    private double _hCm = 60;
    private double _t1Cm = 20;
    private double _t2Cm = 20;
    private string _vertices = string.Empty;
    private double _recCm = 4;
    private string _varEsquina = "#5";
    private string _varIntermedia = "#4";
    private double _sepMaxCm = 15;
    private string _estribo = "#3";
    private string _sepEstribo = "10-15-20";
    private string _fc = "250";
    private string _escala = "20";

    public string Elemento { get => _elemento; set => Set(ref _elemento, (value ?? string.Empty).Trim().ToUpperInvariant()); }

    public string Id { get => _id; set => Set(ref _id, value ?? string.Empty); }

    /// <summary>L, T o IRREGULAR.</summary>
    public string Forma { get => _forma; set => Set(ref _forma, (value ?? string.Empty).Trim().ToUpperInvariant()); }

    public double BCm { get => _bCm; set => Set(ref _bCm, value); }

    public double HCm { get => _hCm; set => Set(ref _hCm, value); }

    public double T1Cm { get => _t1Cm; set => Set(ref _t1Cm, value); }

    public double T2Cm { get => _t2Cm; set => Set(ref _t2Cm, value); }

    /// <summary>Solo IRREGULAR: «x,y; x,y; …» en cm.</summary>
    public string Vertices { get => _vertices; set => Set(ref _vertices, value ?? string.Empty); }

    public double RecubrimientoCm { get => _recCm; set => Set(ref _recCm, value); }

    public string VarEsquina { get => _varEsquina; set => Set(ref _varEsquina, value ?? string.Empty); }

    /// <summary>Vacia = la misma de las esquinas.</summary>
    public string VarIntermedia { get => _varIntermedia; set => Set(ref _varIntermedia, value ?? string.Empty); }

    /// <summary>Lo mas que puede haber entre varillas a lo largo de una cara. Cero = solo esquinas.</summary>
    public double SepMaxCm { get => _sepMaxCm; set => Set(ref _sepMaxCm, value); }

    public string Estribo { get => _estribo; set => Set(ref _estribo, value ?? string.Empty); }

    public string SepEstribo { get => _sepEstribo; set => Set(ref _sepEstribo, value ?? string.Empty); }

    public string Fc { get => _fc; set => Set(ref _fc, value ?? string.Empty); }

    public string Escala { get => _escala; set => Set(ref _escala, value ?? string.Empty); }

    // ---------------- Calculadas ----------------

    /// <summary>El contorno de la forma, o vacio si sus medidas no la forman.</summary>
    public List<(double X, double Y)> Contorno => GeometriaEspecial.Contorno(Forma, BCm, HCm, T1Cm, T2Cm, Vertices);

    public double AreaCm2 => Math.Round(Math.Abs(GeometriaEspecial.Area(Contorno)), 1);

    public int TotalVarillas => GeometriaEspecial.Varillas(AFormatoCad()).Count;

    public double AreaAceroCm2 =>
        Math.Round(GeometriaEspecial.Varillas(AFormatoCad()).Sum(v => Math.PI * v.DiamCm * v.DiamCm / 4), 2);

    public double CuantiaPorcentaje => AreaCm2 <= 0 ? 0 : Math.Round(AreaAceroCm2 / AreaCm2 * 100, 3);

    protected override void RaiseCalculadas()
    {
        Raise(nameof(AreaCm2));
        Raise(nameof(TotalVarillas));
        Raise(nameof(AreaAceroCm2));
        Raise(nameof(CuantiaPorcentaje));
    }

    /// <summary>Como se escribio la forma, para el rotulo.</summary>
    public string Descripcion => Forma switch
    {
        FormaEspecial.L => $"L {BCm:0.#}x{HCm:0.#} cm, alas de {T1Cm:0.#} y {T2Cm:0.#} cm",
        FormaEspecial.T => $"T {BCm:0.#}x{HCm:0.#} cm, patin {T1Cm:0.#} y alma {T2Cm:0.#} cm",
        _ => $"Forma especial de {Contorno.Count} lados"
    };

    public SeccionEspecialCad AFormatoCad(ModoSeccion modo = ModoSeccion.Tipo2Rellena)
    {
        static VarCad V(string clave)
        {
            return Varilla.TryDiametroCm(clave, out var cm) ? new VarCad(Varilla.Normalizar(clave), cm) : new VarCad(string.Empty, 0);
        }

        return new SeccionEspecialCad
        {
            Id = Id.Trim(),
            Elemento = Elemento,
            Contorno = Contorno,
            Descripcion = Descripcion,
            RecubrimientoCm = RecubrimientoCm,
            Estribo = V(Estribo),
            SeparacionEstribo = SepEstribo,
            Esquina = V(VarEsquina),
            Intermedia = V(VarIntermedia),
            SepMaxCm = SepMaxCm,
            Fc = Fc,
            Escala = Escala,
            Modo = modo
        };
    }

    /// <summary>Lo que falta para poder dibujarla. Vacio = se puede.</summary>
    public List<string> Falta()
    {
        var f = new List<string>();

        if (Id.Trim().Length == 0) { f.Add("el ID"); }

        if (Contorno.Count == 0)
        {
            f.Add(Forma == FormaEspecial.Irregular
                ? "vertices que formen un contorno sin cruces (x,y; x,y; …)"
                : "medidas que formen la " + Forma + ": las alas menores que el total");
        }

        if (!Varilla.TryDiametroCm(VarEsquina, out _)) { f.Add($"la varilla de esquina ({Varilla.ClavesValidas})"); }
        if (VarIntermedia.Trim().Length > 0 && !Varilla.TryDiametroCm(VarIntermedia, out _)) { f.Add("una varilla intermedia valida"); }
        if (Estribo.Trim().Length > 0 && !Varilla.TryDiametroCm(Estribo, out _)) { f.Add("un estribo valido"); }

        if (f.Count == 0)
        {
            var s = AFormatoCad();

            if (GeometriaEspecial.HaciaDentro(s.Contorno, s.RecubrimientoCm + s.Estribo.Cm) is null)
            {
                f.Add("un recubrimiento que quepa: la forma es muy delgada");
            }
            else if (GeometriaEspecial.Varillas(s) is var v && (v.Count == 0 || GeometriaEspecial.HuecoMinimo(v) < 0))
            {
                f.Add("varillas que quepan sin encimarse");
            }
        }

        return f;
    }
}
