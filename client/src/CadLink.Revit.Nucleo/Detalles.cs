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

    /// <summary>
    /// El radio del codo, AL EJE de la barra: el de <c>ElevacionPlacaBase.RadioDelDoblez</c>.
    /// Cero = no cabe o el archivo es de antes, y Revit pone uno que quepa.
    /// </summary>
    public double RadioDobleM { get; set; }

    /// <summary>Hacia donde apunta la pata, en los ejes del detalle: hacia el centro de la placa. Unitario.</summary>
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

    /// <summary>Las parrillas de la hoja. Null = la fila no trae armado.</summary>
    public ArmadoZapataJson? Armado { get; set; }
}

/// <summary>Una capa de varillas de una parrilla: su varilla, su diametro y su separacion.</summary>
public sealed class CapaZapataJson
{
    /// <summary>Como se escribio en la hoja: «#4». Solo para el informe.</summary>
    public string Varilla { get; set; } = string.Empty;

    public double DiametroM { get; set; }

    public double SeparacionM { get; set; }
}

/// <summary>
/// Las parrillas de una zapata. «Largas» corren a lo largo del lado MAYOR de la zapata y «Cortas» a
/// lo largo del menor: asi no importa como este girada en Revit ni como se llamen sus lados.
/// </summary>
public sealed class ArmadoZapataJson
{
    public CapaZapataJson? InfLargas { get; set; }

    public CapaZapataJson? InfCortas { get; set; }

    /// <summary>La parrilla de arriba. Null = sin doble parrilla.</summary>
    public CapaZapataJson? SupLargas { get; set; }

    public CapaZapataJson? SupCortas { get; set; }

    public double RecM { get; set; } = 0.05;

    /// <summary>Las varillas doblan en sus puntas: las de abajo hacia arriba y las de arriba hacia abajo.</summary>
    public bool Ganchos { get; set; } = true;
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

    /// <summary>Las parrillas de la hoja. Null = la fila no trae armado.</summary>
    public ArmadoZapataJson? Armado { get; set; }
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
/// <remarks>
/// <c>Soldadura</c> es TODO lo de un extremo de viga -el filete, las placas de la conexion a
/// momento o la placa embebida en el muro-, para que cambiar de una a otra quite la anterior.
/// <c>SoldaduraColumna</c> es el filete de la base o de la cabeza de una columna.
/// </remarks>
public enum ClaseDetalle { Placa, Zapata, Corrida, Soldadura, SoldaduraColumna, Encamisado, PlacaViga, ArmadoZapata }

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

    /// <summary>
    /// Las tres cotas de la placa segun vaya EMBEBIDA o SOBRE su apoyo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Embebida</b>: la cara de arriba de la placa a paño del arranque de la columna. Si la
    /// columna arranca en la cara del dado, la placa y su grout quedan metidos en el concreto.
    /// </para>
    /// <para>
    /// <b>Sobre el apoyo</b>: el grout se apoya en la cara de arriba del dado o del muro, y la placa
    /// encima. Es lo que se construye; la columna, entonces, arranca en la cara de la placa.
    /// Sin apoyo encontrado se toma el arranque de la columna como cara del concreto.
    /// </para>
    /// </remarks>
    public static CotasPlaca Cotas(PlacaBaseJson p, double baseColumnaM, double? apoyoM, bool sobreElApoyo)
    {
        var grout = Math.Max(0, p.GroutM);

        if (!sobreElApoyo)
        {
            return new CotasPlaca(baseColumnaM - p.EspesorM - grout, baseColumnaM - p.EspesorM, baseColumnaM);
        }

        var concreto = apoyoM ?? baseColumnaM;

        return new CotasPlaca(concreto, concreto + grout, concreto + grout + p.EspesorM);
    }

    /// <summary>
    /// La placa de APOYO de una viga sobre un muro o un cabezal: su cara de arriba en el patin de
    /// abajo de la viga, que no se mueve.
    /// </summary>
    /// <remarks>
    /// Si entre la placa y el concreto queda hueco, el grout lo llena -aunque mida mas que el de la
    /// hoja-. Si la viga ya esta apoyada en el concreto, la placa y su grout se meten en el.
    /// </remarks>
    public static CotasPlaca CotasDeApoyoDeViga(PlacaBaseJson p, double fondoVigaM, double caraConcretoM)
    {
        var caraInf = fondoVigaM - p.EspesorM;
        var conGrout = caraInf - Math.Max(0, p.GroutM);
        var fondo = caraConcretoM < conGrout ? caraConcretoM : conGrout;

        return new CotasPlaca(fondo, caraInf, fondoVigaM);
    }

    /// <summary>
    /// La placa de apoyo que sigue a la viga, cuando no viene de la hoja: horizontal, a lo largo de
    /// la viga, del ancho del patin mas 8 cm por lado para que las anclas queden POR FUERA de el, con
    /// grout de 2.5 cm, el patin soldado y cuatro anclas rectas.
    /// </summary>
    public static PlacaBaseJson DeApoyo(double anchoPatinM, double soldaduraM, OpcionesConexion o)
    {
        var d = o.PernoM > 0 ? o.PernoM : 0.019;
        var e = 0.019;
        var p = new PlacaBaseJson
        {
            Marca = "apoyo",
            LadoXM = 0.25,
            LadoYM = anchoPatinM + 0.16,
            EspesorM = e,
            GroutM = 0.025,
            SoldaduraM = soldaduraM
        };

        foreach (var x in new[] { -1.0, 1.0 })
        {
            foreach (var y in new[] { -1.0, 1.0 })
            {
                p.Anclas.Add(new AnclaJson
                {
                    X = x * ((p.LadoXM / 2) - 0.05),
                    Y = y * ((anchoPatinM / 2) + 0.04),
                    DiametroM = d,
                    AgujeroM = d + 0.003,
                    TuercaM = 1.6 * d,
                    TuercaAltoM = 0.8 * d,
                    PuntaM = e + (0.8 * d) + 0.01,
                    FondoM = -Math.Max(0.15, o.PernoLargoM)
                });
            }
        }

        return p;
    }

    /// <summary>
    /// ¿Se apoya la viga en ese concreto, o llega a su costado? Se apoya si la cara de arriba del
    /// concreto esta bajo el patin de abajo -hasta 30 cm, lo que puede llenar un grout grueso o un
    /// dado- o apenas por encima, porque la viga se modelo metida.
    /// </summary>
    public static bool SeApoya(double fondoVigaM, double caraConcretoM) =>
        caraConcretoM <= fondoVigaM + 0.05 && caraConcretoM >= fondoVigaM - 0.30;

    /// <summary>
    /// La cara en que se apoya la viga sobre un concreto que va de <paramref name="minM"/> a
    /// <paramref name="maxM"/>, o null si no se apoya.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SOLO cuando la cara de arriba del concreto queda bajo el patin -hasta 30 cm-: ahi la viga va
    /// ENCIMA y la placa es horizontal.
    /// </para>
    /// <para>
    /// Si el concreto SUBE por encima del patin, la viga llega a su COSTADO -un muro, un cabezal o una
    /// columna modelados hasta el nivel de arriba- y la placa va VERTICAL, embebida a paño de su cara.
    /// Hubo una version que en ese caso ponia la placa horizontal bajo la viga: quedaba en el aire,
    /// junto al muro, con las anclas colgando.
    /// </para>
    /// </remarks>
    public static double? CaraDeApoyo(double fondoVigaM, double minM, double maxM) =>
        SeApoya(fondoVigaM, maxM) && minM < maxM ? maxM : null;

    /// <summary>¿El concreto sube por encima del patin de abajo? Entonces la viga llega a su costado.</summary>
    public static bool LlegaAlCostado(double fondoVigaM, double minM, double maxM) =>
        minM < fondoVigaM + 0.05 && maxM > fondoVigaM + 0.05;

    /// <summary>
    /// Cuanto se encima la viga sobre el apoyo, a lo largo de su eje, para que cuente como apoyo y no
    /// como una viga que llega a la cara del muro: 3 cm.
    /// </summary>
    public const double EncimeMinimoM = 0.03;

    /// <summary>
    /// De los apoyos que hay bajo la columna, la cara de arriba que le toca, o null si ninguno.
    /// </summary>
    /// <remarks>
    /// Cuenta un apoyo cuya cara de arriba quede cerca del arranque: hasta 30 cm por debajo -la
    /// placa que ya se puso subio la columna- y hasta 60 cm por encima -una columna ahogada en el
    /// dado-. Mas lejos es otra cosa: la losa del nivel de abajo o el muro de al lado.
    /// </remarks>
    public static double? CaraDelApoyo(IEnumerable<double> carasDeArriba, double baseColumnaM)
    {
        double? mejor = null;

        foreach (var z in carasDeArriba)
        {
            if (z >= baseColumnaM - 0.30 && z <= baseColumnaM + 0.60 && (mejor is null || z > mejor))
            {
                mejor = z;
            }
        }

        return mejor;
    }

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

/// <summary>Las cotas de la placa base, de abajo hacia arriba.</summary>
public readonly record struct CotasPlaca(double FondoGroutM, double CaraInferiorM, double CaraSuperiorM);

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

/// <summary>Las medidas de un perfil I leidas de su contorno: peralte, patin y espesores.</summary>
/// <param name="CU">Donde queda el centro del perfil, en U, respecto al cero de las medidas.</param>
/// <param name="CV">Lo mismo en V.</param>
public readonly record struct MedidasI(double D, double Bf, double Tf, double Tw, double CU = 0, double CV = 0);

/// <summary>Una caja en los ejes del extremo de la viga, en metros.</summary>
/// <remarks>
/// <c>U</c> va de lado, <c>V</c> hacia arriba y <c>A</c> a lo largo de la viga, desde la cara de la
/// columna -o del muro- hacia dentro de la viga. El cero de U y V es el centro del perfil.
/// </remarks>
public readonly record struct CajaLocal(
    string Que, double U0, double U1, double V0, double V1, double A0, double A1);

/// <summary>Un tornillo atravesado a lo largo de U, en los ejes del extremo.</summary>
public readonly record struct TornilloLocal(double V, double A, double U0, double U1, double DiametroM);

/// <summary>Un ancla de la placa embebida en el muro: sale de la cara de atras hacia dentro.</summary>
public readonly record struct PernoLocal(double U, double V, double DiametroM, double LargoM);

/// <summary>Las medidas de las conexiones, con los valores usuales por omision.</summary>
public sealed class OpcionesConexion
{
    /// <summary>Placas de los patines: espesor, y cuanto entran a lo largo de la viga.</summary>
    public double PlacaPatinEspesorM { get; set; } = 0.019;

    public double PlacaPatinLargoM { get; set; } = 0.30;

    /// <summary>Ancho de las placas de los patines. Cero = el ancho del patin de la viga.</summary>
    public double PlacaPatinAnchoM { get; set; }

    /// <summary>Filete de las placas de los patines a la columna. Cero = el filete general.</summary>
    public double SoldaduraPatinM { get; set; }

    /// <summary>Placa de cortante: espesor y largo a lo largo de la viga.</summary>
    public double PlacaCortanteEspesorM { get; set; } = 0.0095;

    public double PlacaCortanteLargoM { get; set; } = 0.12;

    /// <summary>
    /// Alto de la placa de cortante. Cero = el que cabe entre los patines con 2.5 cm de holgura.
    /// Nunca mas alto que eso: si no, chocaria con los patines.
    /// </summary>
    public double PlacaCortanteAltoM { get; set; }

    /// <summary>Filete de la placa de cortante a la columna. Cero = el filete general.</summary>
    public double SoldaduraCortanteM { get; set; }

    /// <summary>
    /// La placa de cortante SOLDADA al alma de la viga, sin tornillos. Apagado: atornillada.
    /// </summary>
    public bool CortanteSoldada { get; set; }

    /// <summary>Placa de cortante en LAS DOS caras del alma. Apagado: en una sola.</summary>
    public bool CortanteDosCaras { get; set; }

    /// <summary>Filete de la placa de cortante al alma, todo alrededor. Cero = el filete general.</summary>
    public double SoldaduraAlmaM { get; set; }

    /// <summary>Tornillos de la placa de cortante: diametro y separacion vertical (3").</summary>
    public double TornilloM { get; set; } = 0.019;

    public double SeparacionTornillosM { get; set; } = 0.075;

    /// <summary>Placa embebida en el muro: espesor y lo que sobresale del perfil por cada lado.</summary>
    public double PlacaMuroEspesorM { get; set; } = 0.0127;

    /// <remarks>8 cm: las anclas, a 4 cm del borde, quedan fuera del perfil y su tuerca no choca con el.</remarks>
    public double PlacaMuroSobranteM { get; set; } = 0.08;

    /// <summary>Anclas de la placa embebida: diametro y largo.</summary>
    public double PernoM { get; set; } = 0.019;

    public double PernoLargoM { get; set; } = 0.15;
}

/// <summary>Las cuentas de las conexiones de vigas, sin nada de Revit.</summary>
public static class Conexion
{
    /// <summary>
    /// Mide un perfil I de su contorno: los niveles de sus aristas horizontales y verticales.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las aristas horizontales de una I estan en cuatro alturas: las dos caras de fuera de los
    /// patines y las dos de dentro. Las verticales, en cuatro posiciones: los dos cantos de los
    /// patines y las dos caras del alma. Los arcos de los filetes no cuentan, asi que da igual que
    /// la familia los tenga.
    /// </para>
    /// <para>
    /// Se mide del dibujo y no de los parametros del tipo porque cada familia los llama distinto
    /// -d, Peralte, Altura- y en cada idioma otro.
    /// </para>
    /// </remarks>
    /// <returns>Null si no es una I: un cajon tiene solo dos alturas en su contorno de fuera.</returns>
    public static MedidasI? MedirI(IEnumerable<double> alturasHorizontales, IEnumerable<double> posicionesVerticales)
    {
        var v = Distintos(alturasHorizontales);
        var u = Distintos(posicionesVerticales);

        if (v.Count != 4 || u.Count != 4)
        {
            return null;
        }

        var d = v[3] - v[0];
        var tf = v[1] - v[0];
        var bf = u[3] - u[0];
        var tw = u[2] - u[1];

        if (d <= 0 || tf <= 0 || bf <= 0 || tw <= 0 || (2 * tf) >= d || tw >= bf)
        {
            return null;
        }

        return new MedidasI(d, bf, tf, tw, (u[0] + u[3]) / 2, (v[0] + v[3]) / 2);
    }

    private static List<double> Distintos(IEnumerable<double> valores)
    {
        var res = new List<double>();

        foreach (var x in valores.OrderBy(x => x))
        {
            if (res.Count == 0 || x - res[^1] > 0.0005)
            {
                res.Add(x);
            }
        }

        return res;
    }

    /// <summary>
    /// La conexion a MOMENTO de una IR a una columna: placa en cada patin y placa de cortante al alma.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las placas de los patines van por FUERA -encima del patin de arriba y debajo del de abajo-,
    /// del ancho del patin y desde la cara de la columna. La de cortante va a un lado del alma,
    /// entre los dos patines con 2.5 cm de holgura en cada uno, con una hilera de tornillos a 3" que
    /// atraviesa la placa y el alma.
    /// </para>
    /// <para>
    /// Las soldaduras de las tres placas a la columna se ponen aparte, alrededor de la cara de cada
    /// placa que toca la columna.
    /// </para>
    /// </remarks>
    /// <param name="conPatines">False: solo la placa de cortante, la conexion a CORTANTE.</param>
    public static (List<CajaLocal> Placas, List<TornilloLocal> Tornillos) Momento(
        MedidasI p, OpcionesConexion o, bool conPatines = true)
    {
        var placas = new List<CajaLocal>();
        var tornillos = new List<TornilloLocal>();

        var mitadD = p.D / 2;
        var mitadBf = (o.PlacaPatinAnchoM > 0 ? o.PlacaPatinAnchoM : p.Bf) / 2;

        if (conPatines)
        {
            placas.Add(new CajaLocal("placa del patin superior", -mitadBf, mitadBf,
                mitadD, mitadD + o.PlacaPatinEspesorM, 0, o.PlacaPatinLargoM));
            placas.Add(new CajaLocal("placa del patin inferior", -mitadBf, mitadBf,
                -mitadD - o.PlacaPatinEspesorM, -mitadD, 0, o.PlacaPatinLargoM));
        }

        var holgura = 0.025;
        var cabe = p.D - (2 * p.Tf) - (2 * holgura);
        var alto = o.PlacaCortanteAltoM > 0 ? Math.Min(o.PlacaCortanteAltoM, cabe) : cabe;

        if (alto <= 0.02 || o.PlacaCortanteLargoM <= 0)
        {
            return (placas, tornillos);
        }

        var u0 = p.Tw / 2;
        var u1 = u0 + o.PlacaCortanteEspesorM;
        placas.Add(new CajaLocal("placa de cortante", u0, u1, -alto / 2, alto / 2, 0, o.PlacaCortanteLargoM));

        // La de la otra cara, en espejo: el alma queda en medio de las dos.
        if (o.CortanteDosCaras)
        {
            placas.Add(new CajaLocal("placa de cortante", -u1, -u0, -alto / 2, alto / 2, 0, o.PlacaCortanteLargoM));
        }

        // Soldada al alma: sin tornillos. El filete lo pone la capa de Revit, alrededor de la placa.
        if (o.CortanteSoldada)
        {
            return (placas, tornillos);
        }

        // La hilera de tornillos, centrada en la placa, a 3" y a 4 cm de los bordes.
        var borde = 0.04;
        var libre = alto - (2 * borde);
        var n = libre <= 0 ? 1 : 1 + (int)Math.Floor((libre / o.SeparacionTornillosM) + 1e-9);
        var a = Math.Max(o.PlacaCortanteLargoM / 2, Math.Min(o.PlacaCortanteLargoM - borde, 0.06));
        var largoTornillo = 0.02;

        for (var i = 0; i < n; i++)
        {
            var v = n == 1 ? 0 : (-(n - 1) * o.SeparacionTornillosM / 2) + (i * o.SeparacionTornillosM);
            var desde = o.CortanteDosCaras ? -u1 : -u0;
            tornillos.Add(new TornilloLocal(v, a, desde - largoTornillo, u1 + largoTornillo, o.TornilloM));
        }

        return (placas, tornillos);
    }

    /// <summary>
    /// La placa embebida en un muro de concreto, donde llega la viga, y sus anclas.
    /// </summary>
    /// <remarks>
    /// La placa queda a paño de la cara del muro y se mete en el su espesor. Sobresale del perfil lo
    /// que diga <see cref="OpcionesConexion.PlacaMuroSobranteM"/> por cada lado, y lleva un ancla
    /// en cada esquina, a 4 cm de los bordes, mas las hileras que hagan falta para no pasar de
    /// 25 cm entre anclas.
    /// </remarks>
    /// <param name="u0">El paño del perfil, en los ejes del extremo.</param>
    public static (CajaLocal Placa, List<PernoLocal> Pernos) PlacaEnMuro(
        double u0, double u1, double v0, double v1, OpcionesConexion o)
    {
        var s = o.PlacaMuroSobranteM;
        var placa = new CajaLocal("placa embebida", u0 - s, u1 + s, v0 - s, v1 + s, -o.PlacaMuroEspesorM, 0);

        var borde = 0.04;
        var pernos = new List<PernoLocal>();

        var columnas = Hileras(placa.U1 - placa.U0 - (2 * borde));
        var filas = Hileras(placa.V1 - placa.V0 - (2 * borde));

        // SOLO EN EL PERIMETRO de la placa: las de dentro caerian detras de la viga y su tuerca
        // chocaria con ella o con su soldadura.
        for (var i = 0; i < columnas; i++)
        {
            for (var j = 0; j < filas; j++)
            {
                if (i > 0 && i < columnas - 1 && j > 0 && j < filas - 1)
                {
                    continue;
                }

                pernos.Add(new PernoLocal(
                    Reparto(placa.U0 + borde, placa.U1 - borde, columnas, i),
                    Reparto(placa.V0 + borde, placa.V1 - borde, filas, j),
                    o.PernoM, o.PernoLargoM));
            }
        }

        return (placa, pernos);
    }

    /// <summary>
    /// La placa VERTICAL hecha con la placa de la hoja de CadLink: sus medidas, su espesor y SUS
    /// anclas, tal cual vienen en el archivo -cuantas y donde-. La X del detalle va de lado (U) y la
    /// Y hacia arriba (V); con <paramref name="girar"/>, al reves. Centrada en el perfil.
    /// </summary>
    /// <remarks>
    /// Cada ancla se mete en el concreto lo que baja en el alzado de AutoCAD -su fondo bajo la cara
    /// de la placa-, con un minimo de 10 cm.
    /// </remarks>
    public static (CajaLocal Placa, List<PernoLocal> Pernos) PlacaEnMuroDeArchivo(PlacaBaseJson p, bool girar)
    {
        var lu = girar ? p.LadoYM : p.LadoXM;
        var lv = girar ? p.LadoXM : p.LadoYM;
        var placa = new CajaLocal("placa embebida", -lu / 2, lu / 2, -lv / 2, lv / 2, -p.EspesorM, 0);

        var pernos = p.Anclas.Select(a => new PernoLocal(
                girar ? a.Y : a.X,
                girar ? a.X : a.Y,
                a.DiametroM > 0 ? a.DiametroM : 0.019,
                Math.Max(0.10, -a.FondoM)))
            .ToList();

        return (placa, pernos);
    }

    /// <summary>Cuantas anclas caen detras del perfil -con media tuerca de holgura- y chocarian con el.</summary>
    public static int DetrasDelPerfil(IEnumerable<PernoLocal> pernos, double u0, double u1, double v0, double v1) =>
        pernos.Count(q =>
        {
            var h = 0.8 * q.DiametroM;
            return q.U > u0 - h && q.U < u1 + h && q.V > v0 - h && q.V < v1 + h;
        });

    private static int Hileras(double libre) =>
        libre <= 0 ? 1 : Math.Max(2, 1 + (int)Math.Ceiling((libre / 0.25) - 1e-9));

    private static double Reparto(double a, double b, int n, int i) =>
        n <= 1 ? (a + b) / 2 : a + (i * (b - a) / (n - 1));

    /// <summary>Lo que se acepta entre la punta de la viga y la cara del concreto que tiene enfrente.</summary>
    public const double HuecoMaximoM = 0.15;

    /// <summary>
    /// ¿El concreto queda ENFRENTE de la punta de la viga? Y si si, a cuanto de su cara.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los rangos van en los ejes del extremo: A a lo largo de la viga -cero en la cara de su punta,
    /// positivo hacia dentro de la viga-, y U y V en el plano de la cara.
    /// </para>
    /// <para>
    /// Enfrente quiere decir: su cara mas cercana a no mas de <see cref="HuecoMaximoM"/> por delante
    /// -o un poco metida, si la viga se modelo entrando en el-, que no corra a lo largo de la viga
    /// -una losa encima o un muro de al lado tocan la cara pero no estan enfrente- y que se encime
    /// con la cara de la punta al menos 2 cm en las dos direcciones, no solo en una orilla.
    /// </para>
    /// </remarks>
    /// <returns>El hueco en metros -cero o negativo si la viga entra en el-, o null si no esta enfrente.</returns>
    public static double? Enfrente(
        double a0, double a1, double u0, double u1, double v0, double v1,
        double caraU0, double caraU1, double caraV0, double caraV1)
    {
        var hueco = -a1;

        if (hueco > HuecoMaximoM || a1 > 0.60 || a0 > -0.01)
        {
            return null;
        }

        var solapeU = Math.Min(u1, caraU1) - Math.Max(u0, caraU0);
        var solapeV = Math.Min(v1, caraV1) - Math.Max(v0, caraV0);

        return solapeU >= 0.02 && solapeV >= 0.02 ? hueco : null;
    }

    /// <summary>¿Que extremo de la viga, de <paramref name="p0"/> a <paramref name="p1"/>, queda mas cerca del clic?</summary>
    public static int ExtremoMasCercano(V3 p0, V3 p1, V3 clic) =>
        (clic - p0).Largo <= (clic - p1).Largo ? 0 : 1;
}

/// <summary>Como se encamisa la trabe.</summary>
public enum TipoEncamisado
{
    /// <summary>Placas corridas en las caras elegidas, con tornillos pasantes.</summary>
    Placas,

    /// <summary>Angulos en las esquinas y soleras -cinchos- a cada tanto, rodeando la trabe.</summary>
    AngulosYSoleras
}

/// <summary>Las medidas del encamisado, con los valores usuales por omision.</summary>
public sealed class OpcionesEncamisado
{
    public TipoEncamisado Tipo { get; set; } = TipoEncamisado.Placas;

    public bool Fondo { get; set; } = true;

    public bool Laterales { get; set; } = true;

    /// <summary>
    /// La cara de arriba. Apagada por omision: casi siempre la trabe tiene la losa encima.
    /// </summary>
    public bool Arriba { get; set; }

    /// <summary>Espesor de las placas o de los angulos: 3/8".</summary>
    public double EspesorM { get; set; } = 0.0095;

    /// <summary>
    /// La capa de adhesivo epoxico o grout entre el concreto y el acero. Cero = el acero pegado.
    /// </summary>
    public double AdhesivoM { get; set; } = 0.003;

    /// <summary>Lado de los angulos: 3".</summary>
    public double LadoAnguloM { get; set; } = 0.076;

    /// <summary>Soleras: ancho a lo largo de la trabe y espesor (2" x 1/4").</summary>
    public double AnchoSoleraM { get; set; } = 0.051;

    public double EspesorSoleraM { get; set; } = 0.0064;

    /// <summary>Entre soleras, o entre tornillos, a lo largo de la trabe.</summary>
    public double SeparacionM { get; set; } = 0.30;

    /// <summary>Tornillos pasantes de las placas laterales. Cero = sin tornillos.</summary>
    public double TornilloM { get; set; } = 0.016;

    /// <summary>Lo que el acero se queda antes de la cara de la columna, en cada punta.</summary>
    public double LibreEnPuntasM { get; set; }

    /// <summary>Filas de tornillos en las caras laterales. Cero = las que quepan a 20 cm.</summary>
    public int FilasLaterales { get; set; }

    /// <summary>Las filas en tresbolillo: cada fila corrida media separacion de la anterior.</summary>
    public bool Tresbolillo { get; set; } = true;

    /// <summary>Anclas al concreto de las placas de abajo y de arriba (1/2"). Cero = sin anclas.</summary>
    public double AnclaM { get; set; } = 0.0127;

    /// <summary>Lo que el ancla se mete en el concreto.</summary>
    public double AnclaEmpotreM { get; set; } = 0.10;
}

/// <summary>
/// Un tornillo o ancla del encamisado, en los ejes de la trabe.
/// </summary>
/// <param name="U">Arranque: la punta que asoma por fuera de la placa.</param>
/// <param name="DU">Direccion, unitaria: hacia dentro de la trabe.</param>
/// <param name="Asoma">De la punta a la cara de fuera de la placa: ahi va la tuerca.</param>
/// <param name="Pasante">Atraviesa la trabe y lleva tuerca tambien en la otra punta.</param>
public readonly record struct PernoEncamisado(
    double U, double V, double A, double DU, double DV, double LargoM, double DiametroM,
    double Asoma, bool Pasante);

/// <summary>
/// El encamisado de una trabe de concreto con acero, sin nada de Revit.
/// </summary>
/// <remarks>
/// <para>
/// Las cajas van en los ejes de la trabe: <c>U</c> de lado -cero al centro-, <c>V</c> hacia arriba
/// -cero en la cara de ABAJO del concreto- y <c>A</c> a lo largo, de cara a cara de columna.
/// </para>
/// <para>
/// Las piezas que van por fuera abrazan a las de dentro: la placa del fondo es tan ancha como la
/// trabe mas las dos laterales, asi que las esquinas cierran; y las soleras van por fuera de los
/// angulos.
/// </para>
/// </remarks>
public static class Encamisado
{
    public const string Adhesivo = "adhesivo";

    public static (List<CajaLocal> Piezas, List<PernoEncamisado> Tornillos) Piezas(
        double baseM, double altoM, double largoM, OpcionesEncamisado o)
    {
        var piezas = new List<CajaLocal>();
        var tornillos = new List<PernoEncamisado>();

        var b = baseM / 2;
        var h = altoM;
        var g = Math.Max(0, o.AdhesivoM);
        var t = Math.Max(0, o.EspesorM);
        var a0 = Math.Max(0, o.LibreEnPuntasM);
        var a1 = largoM - a0;

        if (a1 - a0 <= 0.01 || baseM <= 0 || altoM <= 0 || t <= 0)
        {
            return (piezas, tornillos);
        }

        // ---- El adhesivo: una capa en cada cara cubierta ----
        if (g > 0)
        {
            if (o.Fondo) { piezas.Add(new CajaLocal(Adhesivo, -b, b, -g, 0, a0, a1)); }
            if (o.Arriba) { piezas.Add(new CajaLocal(Adhesivo, -b, b, h, h + g, a0, a1)); }

            if (o.Laterales)
            {
                piezas.Add(new CajaLocal(Adhesivo, -b - g, -b, 0, h, a0, a1));
                piezas.Add(new CajaLocal(Adhesivo, b, b + g, 0, h, a0, a1));
            }
        }

        if (o.Tipo == TipoEncamisado.Placas)
        {
            // Las laterales bajan y suben hasta cubrir el canto de las de abajo y arriba.
            var ancho = o.Laterales ? b + g + t : b;
            var vAbajo = o.Fondo ? -g - t : 0;
            var vArriba = o.Arriba ? h + g + t : h;

            if (o.Fondo) { piezas.Add(new CajaLocal("placa del fondo", -ancho, ancho, -g - t, -g, a0, a1)); }
            if (o.Arriba) { piezas.Add(new CajaLocal("placa de arriba", -ancho, ancho, h + g, h + g + t, a0, a1)); }

            if (o.Laterales)
            {
                piezas.Add(new CajaLocal("placa lateral", -b - g - t, -b - g, vAbajo, vArriba, a0, a1));
                piezas.Add(new CajaLocal("placa lateral", b + g, b + g + t, vAbajo, vArriba, a0, a1));

                // Tornillos PASANTES de lado a lado, en varias filas y en tresbolillo.
                if (o.TornilloM > 0 && o.SeparacionM > 0)
                {
                    var asoma = 0.025;
                    var mitad = b + g + t + asoma;
                    var filas = Filas(0, h, 0.075, 0.20, o.FilasLaterales, 2);

                    for (var k = 0; k < filas.Count; k++)
                    {
                        foreach (var a in AlLargo(a0, a1, o.SeparacionM, o.Tresbolillo && k % 2 == 1))
                        {
                            tornillos.Add(new PernoEncamisado(-mitad, filas[k], a, 1, 0, 2 * mitad, o.TornilloM, asoma, true));
                        }
                    }
                }
            }

            // ANCLAS AL CONCRETO en las placas de abajo y de arriba: no pueden ser pasantes -del
            // otro lado esta la losa o no hay acceso-, asi que se meten en el concreto lo que diga
            // el empotre. Filas a lo ancho, en tresbolillo con las de al lado.
            if (o.AnclaM > 0 && o.SeparacionM > 0)
            {
                var asoma = 0.015;
                var largo = asoma + t + g + Math.Max(0.02, o.AnclaEmpotreM);
                var filas = Filas(-b, b, 0.05, 0.15, 0, 1);

                void Cara(double vFuera, double dv)
                {
                    for (var k = 0; k < filas.Count; k++)
                    {
                        foreach (var a in AlLargo(a0, a1, o.SeparacionM, o.Tresbolillo && k % 2 == 1))
                        {
                            tornillos.Add(new PernoEncamisado(filas[k], vFuera - (dv * asoma), a, 0, dv, largo, o.AnclaM, asoma, false));
                        }
                    }
                }

                if (o.Fondo) { Cara(-g - t, 1); }
                if (o.Arriba) { Cara(h + g + t, -1); }
            }

            return (piezas, tornillos);
        }

        // ---- Angulos en las esquinas ----
        var lado = Math.Max(o.LadoAnguloM, 2 * t);
        var x = b + g;            // cara de dentro del angulo, de lado
        var abajo = -g;           // cara de dentro, abajo
        var arriba = h + g;       // cara de dentro, arriba

        foreach (var s in new[] { -1.0, 1.0 })
        {
            // Abajo: ala horizontal bajo el fondo y ala vertical en la cara lateral.
            piezas.Add(Caja("angulo inferior", s, x - lado + t, x + t, abajo - t, abajo, a0, a1));
            piezas.Add(Caja("angulo inferior", s, x, x + t, abajo, abajo - t + lado, a0, a1));

            if (o.Arriba)
            {
                piezas.Add(Caja("angulo superior", s, x - lado + t, x + t, arriba, arriba + t, a0, a1));
                piezas.Add(Caja("angulo superior", s, x, x + t, arriba + t - lado, arriba, a0, a1));
            }
        }

        // ---- Soleras: cinchos en U -o cerrados con la cara de arriba- por fuera de los angulos ----
        var ts = Math.Max(0, o.EspesorSoleraM);
        var w = Math.Max(0, o.AnchoSoleraM);

        if (ts > 0 && w > 0 && o.SeparacionM > 0)
        {
            var xs = x + t;
            var vAbajo = abajo - t - ts;
            var vArriba = o.Arriba ? arriba + t + ts : h;

            foreach (var a in Repartir(a0 + (w / 2), a1 - (w / 2), o.SeparacionM))
            {
                var c0 = a - (w / 2);
                var c1 = a + (w / 2);

                piezas.Add(new CajaLocal("solera", -xs - ts, xs + ts, vAbajo, abajo - t, c0, c1));
                piezas.Add(new CajaLocal("solera", -xs - ts, -xs, abajo - t, vArriba, c0, c1));
                piezas.Add(new CajaLocal("solera", xs, xs + ts, abajo - t, vArriba, c0, c1));

                if (o.Arriba)
                {
                    piezas.Add(new CajaLocal("solera", -xs - ts, xs + ts, arriba + t, vArriba, c0, c1));
                }
            }
        }

        return (piezas, tornillos);
    }

    /// <summary>
    /// Las filas de una cara, de <paramref name="desde"/> a <paramref name="hasta"/>, dejando
    /// <paramref name="borde"/> en cada orilla y sin pasar de <paramref name="maxSep"/> entre filas.
    /// </summary>
    public static List<double> Filas(double desde, double hasta, double borde, double maxSep, int pedidas, int minimo)
    {
        var a = desde + borde;
        var b = hasta - borde;

        if (b - a <= 0.01)
        {
            return new List<double> { (desde + hasta) / 2 };
        }

        var n = pedidas > 0 ? pedidas : Math.Max(minimo, 1 + (int)Math.Ceiling(((b - a) / maxSep) - 1e-9));

        if (n == 1)
        {
            return new List<double> { (a + b) / 2 };
        }

        return Enumerable.Range(0, n).Select(i => a + (i * (b - a) / (n - 1))).ToList();
    }

    /// <summary>
    /// Las posiciones a lo largo de una fila. En tresbolillo, a media separacion de las de la fila
    /// de al lado: en los huecos que esas dejan.
    /// </summary>
    public static List<double> AlLargo(double a0, double a1, double separacion, bool corrida)
    {
        var base_ = Repartir(a0 + 0.05, a1 - 0.05, separacion);

        if (!corrida || base_.Count < 2)
        {
            return base_;
        }

        return base_.Zip(base_.Skip(1), (x, y) => (x + y) / 2).ToList();
    }

    /// <summary>Una caja de un lado: con <paramref name="lado"/> negativo, el espejo en U.</summary>
    private static CajaLocal Caja(string que, double lado, double u0, double u1, double v0, double v1, double a0, double a1) =>
        lado > 0
            ? new CajaLocal(que, u0, u1, v0, v1, a0, a1)
            : new CajaLocal(que, -u1, -u0, v0, v1, a0, a1);

    /// <summary>
    /// Posiciones a lo largo de la trabe a la separacion pedida, centradas: el primero y el ultimo
    /// quedan a la misma distancia de las puntas.
    /// </summary>
    public static List<double> Repartir(double a0, double a1, double separacion)
    {
        var res = new List<double>();
        var largo = a1 - a0;

        if (largo < 0 || separacion <= 0)
        {
            return res;
        }

        var n = 1 + (int)Math.Floor((largo / separacion) + 1e-9);
        var sobra = largo - ((n - 1) * separacion);

        for (var i = 0; i < n; i++)
        {
            res.Add(a0 + (sobra / 2) + (i * separacion));
        }

        return res;
    }

    /// <summary>Lo que falta para poder encamisar. Vacio = se puede.</summary>
    public static List<string> Falta(OpcionesEncamisado o)
    {
        var f = new List<string>();

        if (!o.Fondo && !o.Laterales && !o.Arriba) { f.Add("al menos una cara"); }
        if (o.EspesorM <= 0) { f.Add("el espesor de las placas o los angulos"); }

        if (o.Tipo == TipoEncamisado.AngulosYSoleras && !o.Laterales)
        {
            f.Add("las caras laterales: los angulos y las soleras las abrazan");
        }

        return f;
    }
}

/// <summary>Una capa de varillas de la parrilla, ya repartida, en los ejes de la zapata.</summary>
/// <param name="Larga">Corre a lo largo del lado mayor.</param>
/// <param name="Posiciones">Donde va cada varilla, a lo largo del OTRO lado, desde su orilla.</param>
/// <param name="Z">La altura de su eje, desde el fondo de la zapata.</param>
/// <param name="Desde">Donde empieza y termina, a lo largo de su lado.</param>
/// <param name="Pata">Lo que mide la pata de cada punta. Cero = recta.</param>
/// <param name="PataArriba">Las de abajo doblan hacia arriba; las de arriba, hacia abajo.</param>
public sealed record CapaRepartida(
    string Que, bool Larga, List<double> Posiciones, double Z, double Desde, double Hasta,
    double Pata, bool PataArriba, double DiametroM, double SeparacionM);

/// <summary>Con cuantas parrillas se arma.</summary>
public enum ModoParrillas
{
    /// <summary>Lo que diga la fila: doble si trae la de arriba.</summary>
    ComoLaHoja,

    /// <summary>Doble siempre: si la fila no trae la de arriba, se repite la de abajo.</summary>
    Doble,

    /// <summary>Solo la de abajo, aunque la fila traiga la de arriba.</summary>
    Una
}

/// <summary>Las parrillas de una zapata, sin nada de Revit.</summary>
public static class ArmadoZapata
{
    public static bool EsDoble(ArmadoZapataJson a) => a.SupLargas is not null || a.SupCortas is not null;

    /// <summary>El armado con las parrillas pedidas. No toca el original: se comparte entre zapatas.</summary>
    public static ArmadoZapataJson ConParrillas(ArmadoZapataJson a, ModoParrillas modo)
    {
        var copia = new ArmadoZapataJson
        {
            InfLargas = a.InfLargas,
            InfCortas = a.InfCortas,
            SupLargas = a.SupLargas,
            SupCortas = a.SupCortas,
            RecM = a.RecM,
            Ganchos = a.Ganchos
        };

        if (modo == ModoParrillas.Una)
        {
            copia.SupLargas = null;
            copia.SupCortas = null;
        }
        else if (modo == ModoParrillas.Doble && !EsDoble(a))
        {
            copia.SupLargas = a.InfLargas;
            copia.SupCortas = a.InfCortas;
        }

        return copia;
    }

    /// <summary>
    /// Reparte las capas en una zapata de <paramref name="mayor"/> por <paramref name="menor"/> y
    /// <paramref name="alto"/>. Abajo, las largas en el lecho de abajo y las cortas encima; arriba, al
    /// reves: las cortas en el lecho de arriba y las largas debajo de ellas.
    /// </summary>
    public static List<CapaRepartida> Repartir(double mayor, double menor, double alto, ArmadoZapataJson a)
    {
        var res = new List<CapaRepartida>();
        var rec = Math.Max(0, a.RecM);

        double D(CapaZapataJson? c) => c is null || c.SeparacionM <= 0 ? 0 : Math.Max(0, c.DiametroM);

        var dInfL = D(a.InfLargas);
        var dInfC = D(a.InfCortas);
        var dSupL = D(a.SupLargas);
        var dSupC = D(a.SupCortas);

        // La pata llega hasta la otra parrilla, o hasta el recubrimiento de arriba si no hay.
        var libre = alto - (2 * rec);

        void Capa(string que, CapaZapataJson? c, bool larga, double z, bool arriba, double estorbo)
        {
            if (c is null || c.SeparacionM <= 0 || c.DiametroM <= 0)
            {
                return;
            }

            var d = c.DiametroM;
            var suLado = larga ? mayor : menor;
            var otroLado = larga ? menor : mayor;
            var desde = rec + (d / 2);
            var hasta = suLado - rec - (d / 2);

            if (hasta - desde <= 0.05 || otroLado - (2 * rec) - d <= 0)
            {
                return;
            }

            var pata = a.Ganchos ? Math.Max(0, libre - estorbo - d) : 0;

            res.Add(new CapaRepartida(que, larga, Posiciones(otroLado, rec, d, c.SeparacionM), z, desde, hasta,
                pata, arriba, d, c.SeparacionM));
        }

        Capa("parrilla inferior, varillas largas", a.InfLargas, true, rec + (dInfL / 2), true, dSupC + dSupL);
        Capa("parrilla inferior, varillas cortas", a.InfCortas, false, rec + dInfL + (dInfC / 2), true, dSupC + dSupL);
        Capa("parrilla superior, varillas cortas", a.SupCortas, false, alto - rec - (dSupC / 2), false, dInfL + dInfC);
        Capa("parrilla superior, varillas largas", a.SupLargas, true, alto - rec - dSupC - (dSupL / 2), false, dInfL + dInfC);

        return res;
    }

    /// <summary>
    /// Las posiciones a lo ancho de un lado: de recubrimiento a recubrimiento, a la separacion pedida
    /// o menos, repartidas parejo -la primera y la ultima justo en el recubrimiento-.
    /// </summary>
    public static List<double> Posiciones(double lado, double rec, double d, double sep)
    {
        var a = rec + (d / 2);
        var b = lado - rec - (d / 2);

        if (b <= a)
        {
            return new List<double> { lado / 2 };
        }

        var n = 1 + (int)Math.Ceiling(((b - a) / sep) - 1e-9);
        n = Math.Max(2, n);

        return Enumerable.Range(0, n).Select(i => a + (i * (b - a) / (n - 1))).ToList();
    }

    /// <summary>La separacion «15» o «15-20» de la hoja, en metros: el primer numero.</summary>
    public static double Separacion(string? texto)
    {
        var t = (texto ?? string.Empty).Trim();
        var i = 0;

        while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.' || t[i] == ','))
        {
            i++;
        }

        return double.TryParse(t[..i].Replace(',', '.'), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var cm) && cm > 0
            ? cm / 100
            : 0;
    }
}

