using System.ComponentModel;
using System.Runtime.CompilerServices;
using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Un <b>bastón</b> de una trabe o contratrabe, como se edita en su cuadro: lecho,
/// ubicación, cuántas varillas, de qué diámetro y a qué distancia del paño.
/// </summary>
/// <remarks>
/// <para>
/// No va en la tabla de secciones, a pedido del usuario: se edita aparte, por fila, como
/// las grapas. Así la tabla no crece cuatro columnas que en la mayoría de los elementos
/// quedarían vacías.
/// </para>
/// <para>
/// Los textos de <see cref="Posicion"/> y <see cref="Ubicacion"/> son los que se ven en el
/// cuadro. Se traducen a los enums de dibujo en <see cref="ACad"/>, en un solo sitio.
/// </para>
/// </remarks>
public sealed class BastonSeccion : INotifyPropertyChanged
{
    public const string TextoSuperior = "Superior";
    public const string TextoInferior = "Inferior";
    public const string TextoMedio = "En medio";

    public const string TextoExtremos = "Ambos extremos";
    public const string TextoIzquierdo = "Extremo izquierdo";
    public const string TextoDerecho = "Extremo derecho";
    public const string TextoCentro = "Centro";

    /// <summary>Las opciones de los dos combos del cuadro.</summary>
    /// <summary>Los dos lechos que admite un bastón.</summary>
    public static IReadOnlyList<string> Posiciones { get; } = new[] { TextoSuperior, TextoInferior };

    /// <summary>La ubicación que le toca a un lecho: ver <see cref="CadLink.Cad.Bastones.UbicacionDe"/>.</summary>
    public static string UbicacionTexto(string posicion, bool contratrabe) =>
        CadLink.Cad.Bastones.UbicacionDe(
            posicion == TextoInferior ? PosicionBaston.Inferior : PosicionBaston.Superior,
            contratrabe) == UbicacionBaston.Extremos ? TextoExtremos : TextoCentro;

    public static IReadOnlyList<string> Ubicaciones { get; } =
        new[] { TextoExtremos, TextoIzquierdo, TextoDerecho, TextoCentro };

    private string _posicion = TextoSuperior;
    private string _ubicacion = TextoExtremos;
    private int _cantidad = 2;
    private string _diametro = "#4";
    private double _distanciaM = 1.0;

    public string Posicion { get => _posicion; set => Set(ref _posicion, value); }

    public string Ubicacion { get => _ubicacion; set => Set(ref _ubicacion, value); }

    public int Cantidad { get => _cantidad; set => Set(ref _cantidad, value); }

    /// <summary>Clave de la varilla, <c>#4</c>.</summary>
    public string Diametro { get => _diametro; set => Set(ref _diametro, value); }

    /// <summary>
    /// La <b>longitud real</b> de la varilla, en metros. El nombre se queda por los archivos
    /// ya guardados. Al centro va la mitad a cada lado del centro de la pieza.
    /// </summary>
    public double DistanciaM { get => _distanciaM; set => Set(ref _distanciaM, value); }

    /// <summary>Copia suelta, para editar en el cuadro sin tocar la fila hasta aceptar.</summary>
    public BastonSeccion Copia() => new()
    {
        Posicion = Posicion,
        Ubicacion = Ubicacion,
        Cantidad = Cantidad,
        Diametro = Diametro,
        DistanciaM = DistanciaM
    };

    /// <summary>Al formato del dibujante, con el diámetro ya en centímetros.</summary>
    /// <remarks>La ubicación la corrige <see cref="CadLink.Cad.Bastones.Normalizar"/> al juntarlos.</remarks>
    public BastonCad ACad() => new BastonCad
    {
        Posicion = Posicion switch
        {
            TextoInferior => PosicionBaston.Inferior,
            TextoMedio => PosicionBaston.Medio,
            _ => PosicionBaston.Superior
        },
        Ubicacion = Ubicacion switch
        {
            TextoIzquierdo => UbicacionBaston.Izquierdo,
            TextoDerecho => UbicacionBaston.Derecho,
            TextoCentro => UbicacionBaston.AlCentro,
            _ => UbicacionBaston.Extremos
        },
        Cantidad = Cantidad,
        Var = Varilla.TryDiametroCm(Diametro, out var cm)
            ? new VarCad(Varilla.Normalizar(Diametro), cm)
            : new VarCad(string.Empty, 0),
        DistanciaM = DistanciaM
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T campo, T valor, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
    }
}
