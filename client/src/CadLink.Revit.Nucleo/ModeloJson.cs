using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadLink.Revit.Nucleo;

/// <summary>Que es cada pieza. Espejo de <c>ClaseElemento</c> del lector de ETABS.</summary>
public enum ClasePieza
{
    Columna,
    Trabe,
    Diagonal,
    Muro,
    Losa
}

/// <summary>La forma de la seccion transversal.</summary>
public enum FormaSeccion
{
    Rectangulo,
    Circulo,
    PerfilI,
    PerfilC,
    PerfilT,
    PerfilL,
    Tubo,
    Cajon,

    /// <summary>Muros y losas: no tienen perfil, tienen espesor.</summary>
    Pano
}

/// <summary>Un punto en metros.</summary>
public sealed class PuntoJson
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }
}

/// <summary>Un nivel del modelo.</summary>
public sealed class NivelJson
{
    public string Nombre { get; set; } = string.Empty;

    public double ElevacionM { get; set; }
}

/// <summary>La seccion de una pieza, con sus medidas en metros.</summary>
public sealed class SeccionJson
{
    public string Nombre { get; set; } = string.Empty;

    public FormaSeccion Forma { get; set; } = FormaSeccion.Rectangulo;

    /// <summary>Medida sobre el eje local 3.</summary>
    public double AnchoM { get; set; }

    /// <summary>Medida sobre el eje local 2.</summary>
    public double PeralteM { get; set; }

    public double PatinM { get; set; }

    public double AlmaM { get; set; }

    public double ParedM { get; set; }

    /// <summary>Espesor, solo en muros y losas.</summary>
    public double EspesorM { get; set; }

    public string Material { get; set; } = string.Empty;

    /// <summary>Las notas de la propiedad: dicen si es CASTILLO, COLUMNA, DALA, TRABE...</summary>
    public string Notas { get; set; } = string.Empty;
}

/// <summary>Una barra: columna, trabe o diagonal.</summary>
public sealed class BarraJson
{
    public string Etiqueta { get; set; } = string.Empty;

    public ClasePieza Clase { get; set; } = ClasePieza.Trabe;

    public string Nivel { get; set; } = string.Empty;

    public PuntoJson P1 { get; set; } = new();

    public PuntoJson P2 { get; set; } = new();

    /// <summary>
    /// El giro de la seccion alrededor del eje de la barra, en grados.
    /// </summary>
    /// <remarks>
    /// Va el ANGULO y no los tres vectores de los ejes locales, al contrario que en el
    /// exportador de IFC. El motivo es que Revit no quiere una terna: quiere el parametro
    /// <c>Cross-Section Rotation</c> de la pieza, que es exactamente este angulo. Pasar la
    /// terna obligaria al complemento a deshacerla para volver a sacar el angulo.
    /// </remarks>
    public double AnguloGrados { get; set; }

    public SeccionJson Seccion { get; set; } = new();
}

/// <summary>Un pano: muro o losa. Un poligono con espesor.</summary>
public sealed class PanoJson
{
    public string Etiqueta { get; set; } = string.Empty;

    public ClasePieza Clase { get; set; } = ClasePieza.Losa;

    public string Nivel { get; set; } = string.Empty;

    public SeccionJson Seccion { get; set; } = new();

    /// <summary>El contorno, en orden y sin repetir el primero al final. En metros.</summary>
    public List<PuntoJson> Vertices { get; set; } = new();
}

/// <summary>
/// El modelo tal como viaja de CadLink al complemento de Revit.
/// </summary>
/// <remarks>
/// <para>
/// Es un <b>contrato</b> entre dos programas que se instalan por separado: CadLink escribe
/// el archivo y el complemento lo lee dentro de Revit, quiza semanas despues y quiza con
/// versiones distintas. Por eso lleva <see cref="Version"/> desde el principio y por eso
/// sus tipos son planos y con propiedades de lectura y escritura, en vez de reutilizar
/// <c>ModeloIfc</c>.
/// </para>
/// <para>
/// Reutilizar <c>ModeloIfc</c> habria ahorrado codigo y habria sido un error: es el modelo
/// interno del exportador de IFC, cambia cuando al IFC le hace falta, y lleva cosas que a
/// Revit no le sirven -la terna de ejes ya calculada- mientras le falta la que si -el
/// angulo de giro-. Atar el formato de intercambio a un detalle interno de otro modulo es
/// como se rompen los archivos ya repartidos.
/// </para>
/// </remarks>
public sealed class ModeloJson
{
    /// <summary>La version del formato. Se sube cuando un cambio no es compatible.</summary>
    public const int VersionActual = 1;

    public int Version { get; set; } = VersionActual;

    /// <summary>De donde salio: "ETABS" o "SAP2000".</summary>
    public string Programa { get; set; } = string.Empty;

    public string Archivo { get; set; } = string.Empty;

    public string Obra { get; set; } = string.Empty;

    /// <summary>Cuando se exporto, en ISO 8601.</summary>
    public string Exportado { get; set; } = string.Empty;

    public List<NivelJson> Niveles { get; set; } = new();

    public List<BarraJson> Barras { get; set; } = new();

    public List<PanoJson> Panos { get; set; } = new();

    public int Piezas => Barras.Count + Panos.Count;
}

/// <summary>Leer y escribir el archivo de intercambio.</summary>
public static class ArchivoModelo
{
    /// <summary>La extension del archivo.</summary>
    public const string Extension = ".cadlink-modelo.json";

    /// <summary>Para el cuadro de abrir del complemento.</summary>
    public const string Filtro = "Modelo de CadLink (*.cadlink-modelo.json)|*.cadlink-modelo.json";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,

        // Sin esto, System.Text.Json escapa los acentos a \u00d1 y el archivo se vuelve
        // ilegible para una persona. Es un archivo que alguien va a abrir para entender por
        // que una pieza no salio, asi que tiene que poder leerse.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // Las enumeraciones van por NOMBRE y no por numero: un 3 en el archivo no dice nada,
        // y si alguien reordena el enum, los archivos viejos pasarian a significar otra cosa
        // sin que nada fallara.
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Convierte el modelo en texto JSON.</summary>
    public static string ATexto(ModeloJson modelo)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        return JsonSerializer.Serialize(modelo, Opciones);
    }

    /// <summary>Lee el modelo de un texto JSON.</summary>
    /// <exception cref="InvalidDataException">Si el archivo no sirve o es de otra version.</exception>
    public static ModeloJson DeTexto(string texto)
    {
        ModeloJson? m;

        try
        {
            m = JsonSerializer.Deserialize<ModeloJson>(texto, Opciones);
        }
        catch (JsonException e)
        {
            // Se dice QUE esta mal y donde, no "archivo invalido": el usuario no puede
            // hacer nada con eso.
            throw new InvalidDataException(
                "El archivo del modelo no es un JSON valido: " + e.Message, e);
        }

        if (m is null)
        {
            throw new InvalidDataException("El archivo del modelo esta vacio.");
        }

        if (m.Version > ModeloJson.VersionActual)
        {
            // Se rechaza lo MAS NUEVO, no lo mas viejo. Un archivo de una version futura
            // puede traer campos cuyo significado este complemento no conoce, y leerlo a
            // medias produciria un modelo mal puesto en silencio.
            throw new InvalidDataException(
                $"El archivo es de la version {m.Version} y este complemento entiende hasta "
                + $"la {ModeloJson.VersionActual}. Actualiza el complemento de Revit.");
        }

        // Las listas pueden llegar nulas si el JSON las trae como null en vez de [].
        m.Niveles ??= new List<NivelJson>();
        m.Barras ??= new List<BarraJson>();
        m.Panos ??= new List<PanoJson>();

        return m;
    }

    /// <summary>Escribe el archivo.</summary>
    public static void Guardar(ModeloJson modelo, string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            throw new ArgumentException("Falta la ruta.", nameof(ruta));
        }

        // Se escribe en un temporal y se cambia al final, como hace el .clk de la
        // aplicacion: si algo falla a medias, el archivo anterior sigue entero en vez de
        // quedar truncado.
        var temporal = ruta + ".tmp";

        File.WriteAllText(temporal, ATexto(modelo), new System.Text.UTF8Encoding(false));

        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }

        File.Move(temporal, ruta);
    }

    /// <summary>Lee el archivo.</summary>
    public static ModeloJson Leer(string ruta)
    {
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException("No existe el archivo del modelo: " + ruta, ruta);
        }

        return DeTexto(File.ReadAllText(ruta));
    }
}
