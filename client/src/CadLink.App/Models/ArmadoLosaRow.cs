using System.Globalization;
using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Un renglon de la hoja «Losas»: un tablero de losa de entrepiso o de cimentacion, con su armado de
/// corridas, bayonetas y bastones a L/4 del claro.
/// </summary>
/// <remarks>
/// Los claros son de EJE a EJE de los apoyos; el claro libre -el que da el L/4- es ese menos el ancho
/// del apoyo. La separacion es la de la parrilla: corridas y bayonetas se alternan, asi que cada una
/// va al doble. Cada orilla dice si sigue otro tablero (CONTINUO) o ahi se acaba la losa (BORDE).
/// </remarks>
public sealed class ArmadoLosaRow : Row
{
    public const string Entrepiso = "ENTREPISO";
    public const string Cimentacion = "CIMENTACION";
    public const string Continuo = "CONTINUO";
    public const string Borde = "BORDE";

    public static readonly string[] Tipos = { Entrepiso, Cimentacion };

    public static readonly string[] Bordes = { Borde, Continuo };

    private string _id = string.Empty;
    private string _tipo = Entrepiso;
    private double _claroX = 4.0;
    private double _claroY = 5.0;
    private double _espesor = 12;
    private double _rec = 2;
    private double _apoyoAncho = 20;
    private double _apoyoPeralte = 40;
    private string _varX = "#3";
    private double _sepX = 20;
    private string _varY = "#3";
    private double _sepY = 20;
    private string _baston = string.Empty;
    private string _izq = Borde;
    private string _der = Borde;
    private string _abajo = Borde;
    private string _arriba = Borde;
    private string _fc = "250";
    private string _escala = "50";

    public string Id { get => _id; set => Set(ref _id, value ?? string.Empty); }

    /// <summary>ENTREPISO o CIMENTACION. Al cambiarlo, lo que siga con los valores tipicos del otro se cambia a los de este.</summary>
    public string Tipo
    {
        get => _tipo;
        set
        {
            var v = (value ?? string.Empty).Trim().ToUpperInvariant();
            var antes = _tipo;
            Set(ref _tipo, v);

            if (antes == v)
            {
                return;
            }

            // Solo lo que nadie ha tocado: el recubrimiento y el apoyo tipicos de cada una.
            if (v == Cimentacion)
            {
                if (_rec == 2) { RecubrimientoCm = 5; }
                if (_espesor == 12) { EspesorCm = 20; }
                if (_apoyoAncho == 20) { ApoyoAnchoCm = 30; }
                if (_apoyoPeralte == 40) { ApoyoPeralteCm = 80; }
            }
            else if (v == Entrepiso)
            {
                if (_rec == 5) { RecubrimientoCm = 2; }
                if (_espesor == 20) { EspesorCm = 12; }
                if (_apoyoAncho == 30) { ApoyoAnchoCm = 20; }
                if (_apoyoPeralte == 80) { ApoyoPeralteCm = 40; }
            }
        }
    }

    /// <summary>Claro en X, de eje a eje, en metros.</summary>
    public double ClaroXM { get => _claroX; set => Set(ref _claroX, value); }

    public double ClaroYM { get => _claroY; set => Set(ref _claroY, value); }

    public double EspesorCm { get => _espesor; set => Set(ref _espesor, value); }

    public double RecubrimientoCm { get => _rec; set => Set(ref _rec, value); }

    /// <summary>Ancho de la trabe o contratrabe que la apoya, en cm.</summary>
    public double ApoyoAnchoCm { get => _apoyoAncho; set => Set(ref _apoyoAncho, value); }

    public double ApoyoPeralteCm { get => _apoyoPeralte; set => Set(ref _apoyoPeralte, value); }

    public string VarX { get => _varX; set => Set(ref _varX, value ?? string.Empty); }

    /// <summary>Separacion de la parrilla en X, cm.</summary>
    public double SepXCm { get => _sepX; set => Set(ref _sepX, value); }

    public string VarY { get => _varY; set => Set(ref _varY, value ?? string.Empty); }

    public double SepYCm { get => _sepY; set => Set(ref _sepY, value); }

    /// <summary>Vacia = la misma varilla de cada direccion.</summary>
    public string VarBaston { get => _baston; set => Set(ref _baston, value ?? string.Empty); }

    public string BordeIzquierdo { get => _izq; set => Set(ref _izq, Normalizar(value)); }

    public string BordeDerecho { get => _der; set => Set(ref _der, Normalizar(value)); }

    public string BordeAbajo { get => _abajo; set => Set(ref _abajo, Normalizar(value)); }

    public string BordeArriba { get => _arriba; set => Set(ref _arriba, Normalizar(value)); }

    public string Fc { get => _fc; set => Set(ref _fc, value ?? string.Empty); }

    public string Escala { get => _escala; set => Set(ref _escala, value ?? string.Empty); }

    // ---------------- Calculadas ----------------

    /// <summary>L/4 del claro libre en X: lo que entra el baston desde el paño.</summary>
    public double BastonXM => Math.Round(Math.Max(0, GeometriaArmadoLosa.CuartoDelClaro(ClaroXM, ApoyoAnchoCm)), 2);

    public double BastonYM => Math.Round(Math.Max(0, GeometriaArmadoLosa.CuartoDelClaro(ClaroYM, ApoyoAnchoCm)), 2);

    protected override void RaiseCalculadas()
    {
        Raise(nameof(BastonXM));
        Raise(nameof(BastonYM));
    }

    private static string Normalizar(string? v) =>
        (v ?? string.Empty).Trim().ToUpperInvariant().StartsWith("C", StringComparison.Ordinal) ? Continuo : Borde;

    private static VarCad V(string clave) =>
        Varilla.TryDiametroCm(clave, out var cm) ? new VarCad(Varilla.Normalizar(clave), cm) : new VarCad(string.Empty, 0);

    private static BordeLosa B(string v) => v == Continuo ? BordeLosa.Continuo : BordeLosa.Borde;

    public ArmadoLosaCad AFormatoCad() => new()
    {
        Id = Id.Trim(),
        Tipo = Tipo == Cimentacion ? TipoLosa.Cimentacion : TipoLosa.Entrepiso,
        ClaroXM = ClaroXM,
        ClaroYM = ClaroYM,
        EspesorCm = EspesorCm,
        RecubrimientoCm = RecubrimientoCm,
        ApoyoAnchoCm = ApoyoAnchoCm,
        ApoyoPeralteCm = ApoyoPeralteCm,
        VarX = V(VarX),
        SepXCm = SepXCm,
        VarY = V(VarY),
        SepYCm = SepYCm,
        Baston = V(VarBaston),
        Izquierda = B(BordeIzquierdo),
        Derecha = B(BordeDerecho),
        Abajo = B(BordeAbajo),
        Arriba = B(BordeArriba),
        Fc = Fc,
        EscalaPlano = double.TryParse(Escala, NumberStyles.Float, CultureInfo.InvariantCulture, out var e) && e > 0 ? e : 50
    };

    /// <summary>Lo que falta para poder dibujarla. Vacio = se puede.</summary>
    public List<string> Falta()
    {
        var f = new List<string>();

        if (Id.Trim().Length == 0) { f.Add("el ID"); }
        if (!Varilla.TryDiametroCm(VarX, out _)) { f.Add($"la varilla en X ({Varilla.ClavesValidas})"); }
        if (!Varilla.TryDiametroCm(VarY, out _)) { f.Add($"la varilla en Y ({Varilla.ClavesValidas})"); }
        if (VarBaston.Trim().Length > 0 && !Varilla.TryDiametroCm(VarBaston, out _)) { f.Add("una varilla de baston valida"); }
        if (!double.TryParse(Escala, NumberStyles.Float, CultureInfo.InvariantCulture, out var e) || e <= 0) { f.Add("la escala (por ejemplo 50)"); }

        if (f.Count == 0)
        {
            f.AddRange(GeometriaArmadoLosa.Problemas(AFormatoCad()));
        }

        return f;
    }
}
