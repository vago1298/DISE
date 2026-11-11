namespace CadLink.Cad;

// ============================================================================
//  EL DETALLE DE UNA CONEXION DE ACERO, LISTO PARA AUTOCAD
//
//  Las vistas ya vienen proyectadas (las arma el nucleo de CadLink con las piezas de la
//  conexion): aqui solo hay lineas, contornos, circulos, llamadas y cotas en CENTIMETROS, en los
//  ejes del papel de cada vista. El dibujante las escala y las acomoda.
// ============================================================================

/// <summary>En que capa va cada cosa.</summary>
public enum CapaConexion
{
    Perfil,
    Oculto,
    Eje,
    Placa,
    Tornillo,
    Soldadura,
    Cota,
    Texto
}

public sealed record LineaConexion(double X1, double Y1, double X2, double Y2, CapaConexion Capa);

public sealed record ContornoConexion(IReadOnlyList<(double X, double Y)> Puntos, bool Cerrado, CapaConexion Capa, bool Relleno);

public sealed record CirculoConexion(double X, double Y, double R, CapaConexion Capa);

public sealed record LlamadaConexion(double X, double Y, string Texto);

/// <summary>Cota alineada de (X1, Y1) a (X2, Y2), desplazada hacia (Nx, Ny), con su texto.</summary>
public sealed record CotaConexion(double X1, double Y1, double X2, double Y2, double Nx, double Ny, string Texto);

/// <summary>Una vista del detalle, en cm.</summary>
public sealed class VistaConexionCad
{
    public string Titulo { get; init; } = string.Empty;

    public List<LineaConexion> Lineas { get; } = new();

    public List<ContornoConexion> Contornos { get; } = new();

    public List<CirculoConexion> Circulos { get; } = new();

    public List<LlamadaConexion> Llamadas { get; } = new();

    public List<CotaConexion> Cotas { get; } = new();

    public double MinX { get; init; }

    public double MinY { get; init; }

    public double MaxX { get; init; }

    public double MaxY { get; init; }
}

/// <summary>Una conexion para dibujar: su titulo, sus vistas y su cuadro de datos.</summary>
public sealed class ConexionAceroCad
{
    /// <summary>«UNIÓN VIGA-COLUMNA CON PLACAS EN LOS PATINES».</summary>
    public string Titulo { get; init; } = string.Empty;

    /// <summary>La hoja del libro y la identificacion: «UNION VIGA-COLUMNA · UV-1».</summary>
    public string Subtitulo { get; init; } = string.Empty;

    public List<VistaConexionCad> Vistas { get; } = new();

    /// <summary>El «RESUMEN PARA EL DIBUJANTE» de la hoja.</summary>
    public List<string> Renglones { get; } = new();

    public List<string> Notas { get; } = new();
}

/// <summary>Capas, alturas y colores del detalle: del estilo «Conexiones de acero».</summary>
public static class ConexionAceroCapas
{
    private static PerfilEstilo Estilo => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones);

    public const string EstiloTexto = "ACERO_CONEXION";
    public const string EstiloCota = "COTA_CONEXION";

    public static string Fuente => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Texto("fuente");

    public static double AltoTexto => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("alto.texto");

    public static double AltoTitulo => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("alto.titulo");

    public static double AltoVista => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("alto.vista");

    public static double AltoCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.alto");

    public static double TamanoMarcaCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.marca.tam");

    public static string TipoMarca => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Texto("cota.marca");

    public static int ColorNumeroCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).ColorAci("cota.color.texto");

    public static double DesfaseCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.desfase");

    public static double EscalaLineas => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("escala.lineas");

    public static string PatronSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Texto("hatch.soldadura");

    public static double EscalaSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("hatch.soldadura.escala");

    /// <summary>El nombre de la capa de AutoCAD.</summary>
    public static string Nombre(CapaConexion c) => c switch
    {
        CapaConexion.Perfil => "CONEXION PERFIL",
        CapaConexion.Oculto => "CONEXION OCULTO",
        CapaConexion.Eje => "CONEXION EJE",
        CapaConexion.Placa => "CONEXION PLACAS",
        CapaConexion.Tornillo => "CONEXION TORNILLOS",
        CapaConexion.Soldadura => "CONEXION SOLDADURA",
        CapaConexion.Cota => "CONEXION COTAS",
        _ => "CONEXION TEXTOS"
    };

    public static int ColorDeCapa(CapaConexion c) => Estilo.ColorAci("capa." + Nombre(c));

    public static bool ColorElegido(CapaConexion c) => Estilo.Cambiado("capa." + Nombre(c));

    /// <summary>El tipo de linea de la capa: oculta y de eje; las demas, continua.</summary>
    public static string? TipoDeLinea(CapaConexion c) => c switch
    {
        CapaConexion.Oculto => "HIDDEN",
        CapaConexion.Eje => "CENTER",
        _ => null
    };
}
