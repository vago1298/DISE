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
    Texto,

    /// <summary>Los perfiles secundarios: la viga que llega, las diagonales.</summary>
    PerfilSecundario
}

public sealed record LineaConexion(double X1, double Y1, double X2, double Y2, CapaConexion Capa);

public sealed record ContornoConexion(IReadOnlyList<(double X, double Y)> Puntos, bool Cerrado, CapaConexion Capa, bool Relleno);

public sealed record CirculoConexion(double X, double Y, double R, CapaConexion Capa);

/// <summary>Una llamada de la vista: texto, o el símbolo de soldadura si es de un cordón.</summary>
public sealed record LlamadaConexion(double X, double Y, string Texto)
{
    /// <summary>El símbolo de la simbología de soldadura; null si no es una soldadura.</summary>
    public SimbolosSoldadura.Tipo? Simbolo { get; init; }

    /// <summary>El tamaño junto al símbolo: «3/8"».</summary>
    public string Tamano { get; init; } = string.Empty;

    /// <summary>El electrodo, en la cola.</summary>
    public string Electrodo { get; init; } = string.Empty;

    /// <summary>Lo que se escribe después de la cola.</summary>
    public string Descripcion { get; init; } = string.Empty;
}

/// <summary>Que clase de trazo grabo el dibujante en su modo de vista previa.</summary>
public enum TipoTrazo
{
    Linea,
    Polilinea,
    Relleno,
    Circulo,
    Texto,
    Cota
}

/// <summary>
/// Un trazo del detalle tal como iria a AutoCAD, en unidades del dibujo: lo que graba
/// <see cref="ConexionAceroDrawer.ParaVistaPrevia"/> en lugar de mandarlo por COM. Con esto la
/// vista previa de CadLink pinta EXACTAMENTE lo que se dibujaria.
/// </summary>
/// <param name="Puntos">x, y, x, y… Linea: 2 puntos. Cota: los 2 puntos y el de la linea de cota.</param>
/// <param name="Texto">El texto (renglones separados con «\n»), el patron del relleno o el numero de la cota.</param>
/// <param name="Anclaje">El punto de anclaje del MTEXT: 1 arriba izq., 2 arriba centro, 4 medio izq., 5 centro, 6 medio der.</param>
/// <param name="Color">El color ACI propio (el relleno de la soldadura); 0 = el de la capa.</param>
/// <param name="Transparencia">La transparencia del relleno, de 0 a 90.</param>
/// <param name="Grueso">El ancho global de la polilinea (PEDIT Width).</param>
public sealed record TrazoConexion(
    TipoTrazo Tipo, CapaConexion Capa, double[] Puntos, bool Cerrado = false, double Radio = 0,
    string Texto = "", double Alto = 0, int Anclaje = 1, int Color = 0, double Transparencia = 0, double Grueso = 0);

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

    /// <summary>La altura de texto con que se arma la simbologia de soldadura: sus proporciones van en ella.</summary>
    public static double AltoSimbolo => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("alto.simbolo");

    /// <summary>El largo de la punta de flecha de las llamadas de soldadura.</summary>
    public static double LargoFlecha => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("flecha.largo");

    /// <summary>El radio del punto donde muerde la llamada de texto.</summary>
    public static double RadioPunto => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("llamada.punto");

    /// <summary>Cuantas letras caben en un renglon de llamada antes de partirlo en dos.</summary>
    public static double LetrasPorRenglon => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("llamada.letras");

    /// <summary>La separacion entre un detalle y el siguiente, y entre sus vistas.</summary>
    public static double SeparacionDetalles => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("separacion.detalles");

    public static double AltoVista => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("alto.vista");

    public static double AltoCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.alto");

    public static double TamanoMarcaCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.marca.tam");

    public static string TipoMarca => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Texto("cota.marca");

    public static int ColorNumeroCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).ColorAci("cota.color.texto");

    public static double DesfaseCota => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("cota.desfase");

    public static double EscalaLineas => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("escala.lineas");

    /// <summary>El relleno de la soldadura: SOLID, de color 9 y con 70 de transparencia.</summary>
    public static string PatronSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Texto("soldadura.hatch");

    public static double EscalaSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("soldadura.hatch.escala");

    public static int ColorSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).ColorAci("soldadura.color");

    public static double TransparenciaSoldadura => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("soldadura.transparencia");

    /// <summary>El ancho global (PEDIT Width) de las lineas de los perfiles.</summary>
    public static double GruesoDePerfil => EstiloDibujo.Actual.Perfil(EstiloDibujo.Conexiones).Numero("perfil.ancho");

    /// <summary>Si la capa es la de un perfil (principal o secundario): esas llevan ancho.</summary>
    public static bool EsPerfil(CapaConexion c) => c is CapaConexion.Perfil or CapaConexion.PerfilSecundario;

    /// <summary>El nombre de la capa de AutoCAD.</summary>
    public static string Nombre(CapaConexion c) => c switch
    {
        CapaConexion.Perfil => "CONEXION PERFIL",
        CapaConexion.PerfilSecundario => "CONEXION PERFIL SECUNDARIO",
        CapaConexion.Oculto => "CONEXION OCULTO",
        CapaConexion.Eje => "CONEXION EJE",
        CapaConexion.Placa => "CONEXION PLACAS",
        CapaConexion.Tornillo => "CONEXION TORNILLOS",
        CapaConexion.Soldadura => "CONEXION SOLDADURA",
        CapaConexion.Cota => "CONEXION COTAS",
        // Los textos van en ROTULOS, como todo el rotulado de CadLink: verde en pantalla y, con el
        // estilo de trazado o el override del layout que la pone en negro, negros al imprimir.
        _ => "ROTULOS"
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
