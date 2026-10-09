using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Un renglon de la hoja de <b>muros de contencion de concreto armado</b>: los datos del dibujo
/// del muro en voladizo con su espolon.
/// </summary>
/// <remarks>
/// Medidas en metros y separaciones del acero en centimetros, como se leen en el plano. Los
/// diametros se guardan como clave -<c>#5</c>- igual que en las demas hojas, y se resuelven a
/// centimetros en <see cref="AFormatoCad"/>, el unico sitio donde la fila pasa a geometria.
/// </remarks>
public sealed class MuroArmadoRow : Row
{
    private string _id = string.Empty;
    private double _alturaM = 4.35;
    private double _espesorZapataM = 0.65;
    private double _baseM = 5.00;
    private double _puntaM = 1.67;
    private double _coronaM = 0.30;
    private double _espesorPieM = 0.60;
    private double _recCm = 5;
    private string _espolon = "SI";
    private double _espolonDistM = 1.82;
    private double _espolonAnchoM = 0.65;
    private double _espolonProfM = 0.65;
    private string _varVertTierra = "#5";
    private double _sepVertTierraCm = 10;
    private string _varVertExt = "#6";
    private double _sepVertExtCm = 33;
    private string _varHoriz = "#6";
    private double _sepHorizCm = 33;
    private string _varEspiga = "#6";
    private double _sepEspigaCm = 66;
    private double _longEspigaM = 1.45;
    private string _varZapSup = "#4";
    private double _sepZapSupCm = 30;
    private string _varRepSup = "#4";
    private double _sepRepSupCm = 30;
    private string _varZapInf = "#4";
    private double _sepZapInfCm = 20;
    private string _varRepInf = "#4";
    private double _sepRepInfCm = 20;
    private double _anchoDetalleM = 2.0;
    private string _fc = "250";
    private string _escala = "50";

    public static string[] SiNo => ZapataAisladaRow.SiNo;

    public string Id { get => _id; set { Set(ref _id, value); Avisar(); } }

    public double AlturaM { get => _alturaM; set { Set(ref _alturaM, value); Avisar(); } }

    public double EspesorZapataM { get => _espesorZapataM; set { Set(ref _espesorZapataM, value); Avisar(); } }

    public double BaseM { get => _baseM; set { Set(ref _baseM, value); Avisar(); } }

    public double PuntaM { get => _puntaM; set { Set(ref _puntaM, value); Avisar(); } }

    public double CoronaM { get => _coronaM; set { Set(ref _coronaM, value); Avisar(); } }

    public double EspesorPieM { get => _espesorPieM; set { Set(ref _espesorPieM, value); Avisar(); } }

    /// <summary>Lo que sobra de la zapata detras de la pantalla. Calculado: no se captura.</summary>
    public double TalonM => Math.Round(BaseM - PuntaM - EspesorPieM, 2);

    public double RecCm { get => _recCm; set { Set(ref _recCm, value); Avisar(); } }

    public string Espolon
    {
        get => _espolon;
        set
        {
            Set(ref _espolon, (value ?? string.Empty).Trim().ToUpperInvariant());
            Raise(nameof(LlevaEspolon));
            Avisar();
        }
    }

    public bool LlevaEspolon => (_espolon ?? string.Empty).StartsWith("SI", StringComparison.OrdinalIgnoreCase);

    public double EspolonDistM { get => _espolonDistM; set { Set(ref _espolonDistM, value); Avisar(); } }

    public double EspolonAnchoM { get => _espolonAnchoM; set { Set(ref _espolonAnchoM, value); Avisar(); } }

    public double EspolonProfM { get => _espolonProfM; set { Set(ref _espolonProfM, value); Avisar(); } }

    public string VarVertTierra { get => _varVertTierra; set { Set(ref _varVertTierra, value); Avisar(); } }

    public double SepVertTierraCm { get => _sepVertTierraCm; set { Set(ref _sepVertTierraCm, value); Avisar(); } }

    public string VarVertExt { get => _varVertExt; set { Set(ref _varVertExt, value); Avisar(); } }

    public double SepVertExtCm { get => _sepVertExtCm; set { Set(ref _sepVertExtCm, value); Avisar(); } }

    public string VarHoriz { get => _varHoriz; set { Set(ref _varHoriz, value); Avisar(); } }

    public double SepHorizCm { get => _sepHorizCm; set { Set(ref _sepHorizCm, value); Avisar(); } }

    public string VarEspiga { get => _varEspiga; set { Set(ref _varEspiga, value); Avisar(); } }

    public double SepEspigaCm { get => _sepEspigaCm; set { Set(ref _sepEspigaCm, value); Avisar(); } }

    public double LongEspigaM { get => _longEspigaM; set { Set(ref _longEspigaM, value); Avisar(); } }

    public string VarZapSup { get => _varZapSup; set { Set(ref _varZapSup, value); Avisar(); } }

    public double SepZapSupCm { get => _sepZapSupCm; set { Set(ref _sepZapSupCm, value); Avisar(); } }

    public string VarRepSup { get => _varRepSup; set { Set(ref _varRepSup, value); Avisar(); } }

    public double SepRepSupCm { get => _sepRepSupCm; set { Set(ref _sepRepSupCm, value); Avisar(); } }

    public string VarZapInf { get => _varZapInf; set { Set(ref _varZapInf, value); Avisar(); } }

    public double SepZapInfCm { get => _sepZapInfCm; set { Set(ref _sepZapInfCm, value); Avisar(); } }

    public string VarRepInf { get => _varRepInf; set { Set(ref _varRepInf, value); Avisar(); } }

    public double SepRepInfCm { get => _sepRepInfCm; set { Set(ref _sepRepInfCm, value); Avisar(); } }

    /// <summary>Lo ancho del detalle «acero en la pantalla», en metros de muro.</summary>
    public double AnchoDetalleM { get => _anchoDetalleM; set { Set(ref _anchoDetalleM, value); Avisar(); } }

    public string Fc { get => _fc; set { Set(ref _fc, value); Avisar(); } }

    public string Escala { get => _escala; set { Set(ref _escala, value); Avisar(); } }

    /// <summary>Lo que impide dibujarlo. Vacio si esta completo.</summary>
    public string Falta => string.Join(", ", TrazoMuroContencion.Problemas(AFormatoCad()));

    private void Avisar()
    {
        Raise(nameof(TalonM));
        Raise(nameof(Falta));
    }

    /// <summary>La fila como la dibuja AutoCAD y la vista previa.</summary>
    public MuroContencionCad AFormatoCad() => new()
    {
        Tipo = MuroContencionCad.ConcretoArmado,
        Id = Id ?? string.Empty,
        AlturaM = AlturaM,
        EspesorZapataM = EspesorZapataM,
        BaseM = BaseM,
        PuntaM = PuntaM,
        CoronaM = CoronaM,
        EspesorPieM = EspesorPieM,
        RecCm = RecCm,
        Espolon = LlevaEspolon,
        EspolonDistM = EspolonDistM,
        EspolonAnchoM = EspolonAnchoM,
        EspolonProfM = EspolonProfM,
        VarVertTierra = V(VarVertTierra),
        SepVertTierraCm = SepVertTierraCm,
        VarVertExt = V(VarVertExt),
        SepVertExtCm = SepVertExtCm,
        VarHoriz = V(VarHoriz),
        SepHorizCm = SepHorizCm,
        VarEspiga = V(VarEspiga),
        SepEspigaCm = SepEspigaCm,
        LongEspigaM = LongEspigaM,
        VarZapSup = V(VarZapSup),
        SepZapSupCm = SepZapSupCm,
        VarRepSup = V(VarRepSup),
        SepRepSupCm = SepRepSupCm,
        VarZapInf = V(VarZapInf),
        SepZapInfCm = SepZapInfCm,
        VarRepInf = V(VarRepInf),
        SepRepInfCm = SepRepInfCm,
        AnchoDetalleM = AnchoDetalleM,
        Fc = Fc ?? string.Empty,
        Escala = string.IsNullOrWhiteSpace(Escala) ? "50" : Escala.Trim()
    };

    /// <summary>La varilla con su diametro; vacia si la clave no se reconoce.</summary>
    internal static VarMuro V(string? clave) =>
        Varilla.TryDiametroCm(clave, out var cm)
            ? new VarMuro(Varilla.Normalizar(clave!), cm)
            : new VarMuro(string.Empty, 0);
}

/// <summary>
/// Un renglon de la hoja de <b>muros de contencion de concreto ciclopeo</b>, con las letras de
/// su dibujo: h, d, c, b, M, E, G y N.
/// </summary>
public sealed class MuroCiclopeoRow : Row
{
    private string _id = string.Empty;
    private double _h = 2.0;
    private double _d = 0.40;
    private double _c = 0.30;
    private double _b = 0.20;
    private double _m = 0.30;
    private double _e = 0.60;
    private double _g = 0.50;
    private double _n = 0.30;
    private string _fc = "150";
    private string _piedra = "40";
    private string _escala = "50";

    public string Id { get => _id; set { Set(ref _id, value); Avisar(); } }

    /// <summary>h: altura del cuerpo sobre la zapata.</summary>
    public double HM { get => _h; set { Set(ref _h, value); Avisar(); } }

    /// <summary>d: espesor de la zapata.</summary>
    public double DM { get => _d; set { Set(ref _d, value); Avisar(); } }

    /// <summary>c: corona del lado de la cara inclinada.</summary>
    public double CM { get => _c; set { Set(ref _c, value); Avisar(); } }

    /// <summary>b: corona del lado de la tierra.</summary>
    public double BM { get => _b; set { Set(ref _b, value); Avisar(); } }

    /// <summary>M: punta.</summary>
    public double MM { get => _m; set { Set(ref _m, value); Avisar(); } }

    /// <summary>E: lo que avanza la cara inclinada.</summary>
    public double EM { get => _e; set { Set(ref _e, value); Avisar(); } }

    /// <summary>G: del fin de la inclinacion a la cara de la tierra.</summary>
    public double GM { get => _g; set { Set(ref _g, value); Avisar(); } }

    /// <summary>N: talon.</summary>
    public double NM { get => _n; set { Set(ref _n, value); Avisar(); } }

    /// <summary>Ancho total de la base. Calculado: M + E + G + N.</summary>
    public double BaseM => Math.Round(MM + EM + GM + NM, 2);

    public string Fc { get => _fc; set { Set(ref _fc, value); Avisar(); } }

    /// <summary>Porcentaje de piedra, para el rotulo.</summary>
    public string PiedraPct { get => _piedra; set { Set(ref _piedra, value); Avisar(); } }

    public string Escala { get => _escala; set { Set(ref _escala, value); Avisar(); } }

    public string Falta => string.Join(", ", TrazoMuroContencion.Problemas(AFormatoCad()));

    private void Avisar()
    {
        Raise(nameof(BaseM));
        Raise(nameof(Falta));
    }

    public MuroContencionCad AFormatoCad() => new()
    {
        Tipo = MuroContencionCad.Ciclopeo,
        Id = Id ?? string.Empty,
        AlturaM = HM,
        EspesorZapataM = DM,
        CM = CM,
        BM = BM,
        MM = MM,
        EM = EM,
        GM = GM,
        NM = NM,
        Fc = Fc ?? string.Empty,
        PiedraPct = PiedraPct ?? string.Empty,
        Escala = string.IsNullOrWhiteSpace(Escala) ? "50" : Escala.Trim()
    };
}
