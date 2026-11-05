using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  PLACAS BASE, CONEXIONES Y CIMENTACIONES EN REVIT
//
//  Las tablas de CadLink de placas base y de zapatas describen TIPOS, no piezas colocadas: una
//  fila es «la placa PB-1», no «la placa de la columna del eje 3-B». Por eso en Revit se trabaja
//  como con el armado por tipo: se ELIGEN las columnas -o los muros- en el modelo, se escoge la
//  fila de la tabla, y el complemento la pone debajo de cada una.
//
//  La posicion, la orientacion y la cota salen de la pieza que ya esta en Revit. Lo que viaja
//  en el archivo son las medidas, YA RESUELTAS en CadLink con las mismas cuentas del dibujo de
//  AutoCAD: el reparto de las anclas, sus libramientos, el ahogo y la tuerca. Asi el modelo y el
//  plano no pueden contar dos historias distintas.
//
//  Todo en METROS, como el resto del nucleo.
// ============================================================================

/// <summary>El archivo con las placas base y las zapatas de CadLink, para ponerlas en Revit.</summary>
public sealed class ArchivoDetallesJson
{
    public const int VersionActual = 1;

    public int Version { get; set; } = VersionActual;

    public string Aplicacion { get; set; } = string.Empty;

    public DateTime Fecha { get; set; }

    /// <summary>De donde salio: el trabajo de CadLink.</summary>
    public string Origen { get; set; } = string.Empty;

    public List<PlacaBaseJson> Placas { get; set; } = new();

    public List<ZapataAisladaJson> Zapatas { get; set; } = new();

    public List<ZapataCorridaJson> Corridas { get; set; } = new();

    /// <summary>Lo que CadLink no pudo mandar, para decirlo tambien en Revit.</summary>
    public List<string> Avisos { get; set; } = new();
}

/// <summary>
/// Una placa base, con sus medidas YA ORIENTADAS como en el detalle de AutoCAD.
/// </summary>
/// <remarks>
/// <para>
/// «X» y «Y» son las del detalle: X es la horizontal de la planta dibujada y Y la vertical. Las
/// anclas vienen colocadas en esos mismos ejes, medidas desde el CENTRO de la placa.
/// </para>
/// <para>
/// En Revit, la X del detalle se hace coincidir con el lado de la columna cuyo paño mide lo que
/// <see cref="PerfilXM"/>. Asi la placa queda girada respecto a la columna igual que en el plano,
/// la haya colocado como la haya colocado quien modelo.
/// </para>
/// </remarks>
public sealed class PlacaBaseJson
{
    public string Marca { get; set; } = string.Empty;

    /// <summary>La seccion de la columna, solo informativa: «IR 305x59.8».</summary>
    public string Seccion { get; set; } = string.Empty;

    public string Acero { get; set; } = string.Empty;

    /// <summary>La placa a lo largo de la X del detalle.</summary>
    public double LadoXM { get; set; }

    /// <summary>La placa a lo largo de la Y del detalle.</summary>
    public double LadoYM { get; set; }

    public double EspesorM { get; set; }

    /// <summary>El paño de la columna a lo largo de la X del detalle. Cero si no se sabe.</summary>
    public double PerfilXM { get; set; }

    /// <summary>El paño de la columna a lo largo de la Y del detalle. Cero si no se sabe.</summary>
    public double PerfilYM { get; set; }

    /// <summary>La cama de grout bajo la placa. Cero = sin grout.</summary>
    public double GroutM { get; set; }

    /// <summary>El filete de la columna a la placa, todo alrededor. Cero = sin soldadura.</summary>
    public double SoldaduraM { get; set; }

    public string Electrodo { get; set; } = string.Empty;

    /// <summary>Cuantos cartabones lleva. No se modelan todavia: solo se avisa.</summary>
    public int Cartabones { get; set; }

    public List<AnclaJson> Anclas { get; set; } = new();
}

/// <summary>Un ancla de la placa, con la geometria del alzado de AutoCAD.</summary>
/// <remarks>
/// Las cotas verticales se miden desde la cara de ABAJO de la placa, positivas hacia arriba: la
/// punta queda por encima -atraviesa la placa y la tuerca- y el fondo por debajo, ahogado en el
/// dado.
/// </remarks>
public sealed class AnclaJson
{
    /// <summary>Desde el centro de la placa, en la X del detalle.</summary>
    public double X { get; set; }

    /// <summary>Desde el centro de la placa, en la Y del detalle.</summary>
    public double Y { get; set; }

    public double DiametroM { get; set; }

    /// <summary>El barreno de la placa. Cero = el diametro del ancla.</summary>
    public double AgujeroM { get; set; }

    /// <summary>La punta, sobre la cara de abajo de la placa. Positiva.</summary>
    public double PuntaM { get; set; }

    /// <summary>El fondo, bajo la cara de abajo de la placa. Negativo.</summary>
    public double FondoM { get; set; }

    /// <summary>La pata del doblez. Cero = ancla recta.</summary>
    public double PataM { get; set; }

    /// <summary>Hacia donde apunta la pata, en los ejes del detalle. Unitario.</summary>
    public double PataX { get; set; }

    public double PataY { get; set; }

    /// <summary>La tuerca: entre caras y de alto.</summary>
    public double TuercaM { get; set; }

    public double TuercaAltoM { get; set; }
}

/// <summary>Una zapata aislada, con su dado.</summary>
public sealed class ZapataAisladaJson
{
    public string Id { get; set; } = string.Empty;

    /// <summary>CENTRAL o LINDERO.</summary>
    public string Tipo { get; set; } = "CENTRAL";

    /// <summary>A lo largo de la X de la columna -su HandOrientation-.</summary>
    public double AnchoM { get; set; }

    /// <summary>A lo largo de la Y de la columna -su FacingOrientation-.</summary>
    public double LargoM { get; set; }

    public double EspesorM { get; set; }

    /// <summary>
    /// La profundidad de desplante: del fondo de la zapata al arranque de la columna, que es
    /// donde termina el dado. Es la misma cuenta que el corte de AutoCAD.
    /// </summary>
    public double ProfundidadM { get; set; }

    /// <summary>El dado. Cero = sin dado.</summary>
    public double DadoXM { get; set; }

    public double DadoYM { get; set; }

    public bool DadoCircular { get; set; }

    public string Fc { get; set; } = string.Empty;
}

/// <summary>Una zapata corrida, con su contratrabe o su muro de enrase.</summary>
public sealed class ZapataCorridaJson
{
    public string Id { get; set; } = string.Empty;

    /// <summary>CENTRAL o LINDERO.</summary>
    public string Tipo { get; set; } = "CENTRAL";

    public double AnchoM { get; set; }

    public double EspesorM { get; set; }

    /// <summary>Del fondo de la zapata a la base del muro.</summary>
    public double ProfundidadM { get; set; }

    /// <summary>La seccion de la contratrabe. Vacio = sin contratrabe.</summary>
    public string Contratrabe { get; set; } = string.Empty;

    public double ContratrabeAnchoM { get; set; }

    public double ContratrabeAltoM { get; set; }

    /// <summary>
    /// El muro de enrase, de la zapata a la base del muro, cuando no hay contratrabe. Cero = no
    /// se modela.
    /// </summary>
    public double EnraseM { get; set; }

    public string Fc { get; set; } = string.Empty;
}

/// <summary>Leer y escribir el <c>.cadlink-detalles.json</c>.</summary>
public static class ArchivoDetalles
{
    public const string Extension = ".cadlink-detalles.json";

    public const string Filtro =
        "Placas y cimentaciones de CadLink (*.cadlink-detalles.json)|*.cadlink-detalles.json";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ATexto(ArchivoDetallesJson a) =>
        JsonSerializer.Serialize(a ?? throw new ArgumentNullException(nameof(a)), Opciones);

    /// <exception cref="InvalidDataException">Si el archivo no sirve o es de otra version.</exception>
    public static ArchivoDetallesJson DeTexto(string texto)
    {
        ArchivoDetallesJson? a;

        try
        {
            a = JsonSerializer.Deserialize<ArchivoDetallesJson>(texto, Opciones);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                "El archivo de placas y cimentaciones no es un JSON valido: " + e.Message, e);
        }

        if (a is null)
        {
            throw new InvalidDataException("El archivo de placas y cimentaciones esta vacio.");
        }

        if (a.Version > ArchivoDetallesJson.VersionActual)
        {
            throw new InvalidDataException(
                $"El archivo es de la version {a.Version} y este complemento entiende hasta la "
                + $"{ArchivoDetallesJson.VersionActual}. Actualiza el complemento de Revit.");
        }

        a.Placas ??= new List<PlacaBaseJson>();
        a.Zapatas ??= new List<ZapataAisladaJson>();
        a.Corridas ??= new List<ZapataCorridaJson>();
        a.Avisos ??= new List<string>();

        foreach (var p in a.Placas)
        {
            p.Anclas ??= new List<AnclaJson>();
            p.Marca ??= string.Empty;
        }

        foreach (var z in a.Zapatas)
        {
            z.Id ??= string.Empty;
            z.Tipo ??= "CENTRAL";
        }

        foreach (var c in a.Corridas)
        {
            c.Id ??= string.Empty;
            c.Tipo ??= "CENTRAL";
            c.Contratrabe ??= string.Empty;
        }

        return a;
    }

    public static void Guardar(ArchivoDetallesJson a, string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            throw new ArgumentException("Falta la ruta.", nameof(ruta));
        }

        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, ATexto(a), new System.Text.UTF8Encoding(false));

        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }

        File.Move(temporal, ruta);
    }

    public static ArchivoDetallesJson Leer(string ruta)
    {
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException("No existe el archivo de placas y cimentaciones: " + ruta, ruta);
        }

        return DeTexto(File.ReadAllText(ruta));
    }
}

/// <summary>Lo que se pone en Revit: una pieza de CadLink sobre una pieza del modelo.</summary>
public enum ClaseDetalle { Placa, Zapata, Corrida, Soldadura }

/// <summary>
/// La llave de lo que CadLink pone sobre una pieza del modelo: <c>CadLink|Placa|123456</c>.
/// </summary>
/// <remarks>
/// <para>
/// Va en el <c>ApplicationDataId</c> de cada forma. Con ella se encuentra lo que ya se habia
/// puesto sobre la MISMA pieza para cambiarlo en lugar de duplicarlo: volver a poner la placa de
/// una columna reemplaza la anterior, no la encima.
/// </para>
/// <para>
/// Las soldaduras de una viga llevan ademas el extremo -<c>|0</c> o <c>|1</c>-, para que soldar
/// solo un lado no borre el del otro.
/// </para>
/// </remarks>
public static class LlaveDetalle
{
    public const string Aplicacion = "CadLink";

    public static string De(ClaseDetalle clase, long idPieza, int? extremo = null) =>
        extremo is null
            ? $"{Aplicacion}|{clase}|{idPieza}"
            : $"{Aplicacion}|{clase}|{idPieza}|{extremo}";
}

/// <summary>Las cotas de una zapata aislada, de abajo hacia arriba.</summary>
/// <param name="PlantillaM">El fondo de la plantilla.</param>
/// <param name="DesplanteM">El fondo de la zapata.</param>
/// <param name="TopeZapataM">La cara de arriba de la zapata, donde arranca el dado.</param>
/// <param name="TopeDadoM">La cara de arriba del dado: donde se apoya la columna o su placa.</param>
public readonly record struct CotasZapata(
    double PlantillaM, double DesplanteM, double TopeZapataM, double TopeDadoM)
{
    public double AltoDadoM => TopeDadoM - TopeZapataM;
}

/// <summary>Las cotas de una zapata corrida, de abajo hacia arriba.</summary>
/// <param name="TopeContratrabeM">
/// Donde termina la contratrabe. Igual a <paramref name="TopeZapataM"/> si no lleva.
/// </param>
/// <param name="BaseMuroM">La base del muro de Revit: hasta ahi sube el enrase.</param>
public readonly record struct CotasCorrida(
    double PlantillaM, double DesplanteM, double TopeZapataM, double TopeContratrabeM,
    double BaseMuroM);

/// <summary>Las cuentas de la cimentacion, sin nada de Revit.</summary>
public static class Cimentacion
{
    /// <summary>La plantilla de concreto pobre, por omision. Es la de los cortes de AutoCAD.</summary>
    public const double PlantillaPorOmisionM = 0.05;

    /// <summary>Lo que la plantilla sobresale de la zapata por cada lado.</summary>
    public const double SalientePlantillaM = 0.05;

    public static bool EsLindero(string? tipo) =>
        string.Equals((tipo ?? string.Empty).Trim(), "LINDERO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Las cotas de la zapata a partir de donde termina el dado.
    /// </summary>
    /// <remarks>
    /// Es la cuenta del corte de AutoCAD: <c>TrazoZapata.Colocar</c> sube el dado desde el fondo de
    /// la zapata hasta la profundidad de desplante. Aqui se parte del otro extremo -el arranque de
    /// la columna, que es lo que se conoce en Revit- y se baja la profundidad.
    /// </remarks>
    /// <param name="topeDadoM">
    /// El arranque de la columna o, si tiene placa base de CadLink, el fondo de su grout.
    /// </param>
    public static CotasZapata Cotas(ZapataAisladaJson z, double topeDadoM, double plantillaM)
    {
        if (z is null)
        {
            throw new ArgumentNullException(nameof(z));
        }

        var desplante = topeDadoM - Math.Max(z.ProfundidadM, z.EspesorM);
        var topeZapata = desplante + z.EspesorM;

        return new CotasZapata(desplante - Math.Max(0, plantillaM), desplante, topeZapata, topeDadoM);
    }

    /// <summary>Las cotas de la zapata corrida bajo un muro cuya base esta en <paramref name="baseMuroM"/>.</summary>
    public static CotasCorrida Cotas(ZapataCorridaJson z, double baseMuroM, double plantillaM)
    {
        if (z is null)
        {
            throw new ArgumentNullException(nameof(z));
        }

        var desplante = baseMuroM - Math.Max(z.ProfundidadM, z.EspesorM);
        var topeZapata = desplante + z.EspesorM;

        var topeContratrabe = LlevaContratrabe(z)
            ? topeZapata + z.ContratrabeAltoM
            : topeZapata;

        return new CotasCorrida(
            desplante - Math.Max(0, plantillaM), desplante, topeZapata, topeContratrabe, baseMuroM);
    }

    public static bool LlevaContratrabe(ZapataCorridaJson z) =>
        z.Contratrabe.Trim().Length > 0 && z.ContratrabeAnchoM > 0 && z.ContratrabeAltoM > 0;

    /// <summary>
    /// Cuanto se corre el centro de la zapata corrida hacia la cara EXTERIOR del muro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Central: nada, la zapata va centrada en el muro.
    /// </para>
    /// <para>
    /// Lindero: el paño exterior de la zapata es el paño exterior del muro -por ese lado esta el
    /// vecino y no hay donde salirse-, asi que la zapata crece toda hacia dentro. El resultado es
    /// negativo cuando la zapata es mas ancha que el muro, que es lo normal.
    /// </para>
    /// </remarks>
    public static double DesfaseHaciaFuera(string? tipo, double anchoZapataM, double anchoMuroM) =>
        EsLindero(tipo) ? (anchoMuroM / 2) - (anchoZapataM / 2) : 0;

    /// <summary>Lo que falta para poder ponerla. Vacio = se puede.</summary>
    public static List<string> Falta(ZapataAisladaJson z)
    {
        var f = new List<string>();

        if (z.AnchoM <= 0) { f.Add("el ancho"); }
        if (z.LargoM <= 0) { f.Add("el largo"); }
        if (z.EspesorM <= 0) { f.Add("el espesor"); }

        if (z.DadoXM > 0 && (z.DadoXM > z.AnchoM || (z.DadoYM > 0 && z.DadoYM > z.LargoM)))
        {
            f.Add("un dado que quepa en la zapata");
        }

        return f;
    }

    public static List<string> Falta(ZapataCorridaJson z)
    {
        var f = new List<string>();

        if (z.AnchoM <= 0) { f.Add("el ancho"); }
        if (z.EspesorM <= 0) { f.Add("el espesor"); }

        if (z.Contratrabe.Trim().Length > 0 && (z.ContratrabeAnchoM <= 0 || z.ContratrabeAltoM <= 0))
        {
            f.Add($"las medidas de la contratrabe «{z.Contratrabe}» en la hoja de secciones");
        }

        return f;
    }
}

/// <summary>Las cuentas de la placa base, sin nada de Revit.</summary>
public static class PlacaBase
{
    /// <summary>
    /// ¿La X del detalle va a lo largo de la Y de la columna?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se mide el paño de la columna YA MODELADA en sus dos direcciones -la X y la Y de la
    /// familia-, y se compara con el paño que trae el detalle. Gana la asignacion que menos se
    /// equivoca. Asi no importa si en Revit la columna se coloco girada 90° ni de que familia sea.
    /// </para>
    /// <para>
    /// Si el detalle no trae el perfil, o la columna es cuadrada o redonda y las dos asignaciones
    /// empatan, se queda como esta: la X del detalle con la X de la columna.
    /// </para>
    /// </remarks>
    public static bool Intercambiar(double perfilXM, double perfilYM, double columnaXM, double columnaYM)
    {
        if (perfilXM <= 0 || perfilYM <= 0 || columnaXM <= 0 || columnaYM <= 0)
        {
            return false;
        }

        var derecho = Math.Abs(perfilXM - columnaXM) + Math.Abs(perfilYM - columnaYM);
        var girado = Math.Abs(perfilXM - columnaYM) + Math.Abs(perfilYM - columnaXM);

        // Un milimetro de tolerancia: dos perfiles que solo difieren en el redondeo no giran.
        return girado + 0.001 < derecho;
    }

    /// <summary>La cara de abajo de la placa, con la columna arrancando en <paramref name="baseColumnaM"/>.</summary>
    public static double CaraInferior(PlacaBaseJson p, double baseColumnaM) => baseColumnaM - p.EspesorM;

    /// <summary>Donde termina el grout por abajo: ahi arranca el dado.</summary>
    public static double FondoDelGrout(PlacaBaseJson p, double baseColumnaM) =>
        CaraInferior(p, baseColumnaM) - Math.Max(0, p.GroutM);

    /// <summary>Lo que falta para poder ponerla. Vacio = se puede.</summary>
    public static List<string> Falta(PlacaBaseJson p)
    {
        var f = new List<string>();

        if (p.LadoXM <= 0 || p.LadoYM <= 0) { f.Add("las medidas de la placa"); }
        if (p.EspesorM <= 0) { f.Add("el espesor de la placa"); }

        foreach (var a in p.Anclas)
        {
            if (Math.Abs(a.X) > (p.LadoXM / 2) || Math.Abs(a.Y) > (p.LadoYM / 2))
            {
                f.Add("anclas dentro de la placa");
                break;
            }
        }

        return f;
    }
}

/// <summary>Las cuentas del cordon de soldadura, sin nada de Revit.</summary>
public static class Cordon
{
    /// <summary>El filete de una conexion cuando no se dice otro: 6 mm, el minimo usual.</summary>
    public const double PorOmisionM = 0.006;

    /// <summary>
    /// ¿La cara de la pieza es la de su extremo? Su normal va a lo largo del eje.
    /// </summary>
    public static bool EsCaraDeExtremo(V3 normal, V3 eje) =>
        Math.Abs(normal.Unitario().Punto(eje.Unitario())) > 0.999;

    /// <summary>
    /// De dos contornos desplazados -uno hacia cada lado-, el que va hacia FUERA es el mas largo.
    /// </summary>
    /// <remarks>
    /// Revit desplaza un contorno hacia un lado u otro segun el sentido en que este recorrido, y
    /// el sentido de las aristas de una cara depende de la cara. En vez de adivinarlo, se
    /// desplaza hacia los dos y se queda el que crecio.
    /// </remarks>
    public static bool ElPrimeroEsElDeFuera(double largoPrimero, double largoSegundo) =>
        largoPrimero >= largoSegundo;
}
