using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadLink.Revit.Nucleo;

/// <summary>Lo que se eligio para una seccion.</summary>
public sealed class FilaMapeo
{
    /// <summary>La clave de la seccion, la de <see cref="Inventario.Clave"/>.</summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>El nombre de la seccion. No se usa para nada: es para poder leer el archivo.</summary>
    public string Seccion { get; set; } = string.Empty;

    public ClasePieza Clase { get; set; }

    /// <summary>
    /// La categoria de Revit ELEGIDA, que no tiene que ser la que le tocaria por su clase.
    /// </summary>
    /// <remarks>
    /// Se guarda porque el usuario puede cambiarla en el cuadro. Un paño que ETABS trae como
    /// muro puede querer modelarse como suelo -pasa cuando la propiedad esta mal clasificada
    /// en el calculo-, y esa decision tiene que sobrevivir a la siguiente importacion.
    /// Deducirla de la clase, como se hacia antes, borraba la eleccion en silencio.
    /// </remarks>
    public CategoriaRevit Categoria { get; set; }

    public string Familia { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// El <c>ElementId</c> del tipo, si se conocia al guardar.
    /// </summary>
    /// <remarks>
    /// Es una PISTA, no la verdad. Los ElementId no sobreviven a cambiar de proyecto ni a
    /// recargar una familia, asi que al abrir el mapeo se busca primero por nombre y el id
    /// solo se usa para desempatar. Guardar solo el id haria que el mapeo dejara de servir
    /// en cuanto se abre en otro modelo, que es justo cuando mas se agradece tenerlo.
    /// </remarks>
    public long TipoId { get; set; }
}

/// <summary>El mapeo completo, tal como se guarda en disco.</summary>
public sealed class MapeoGuardado
{
    public const int VersionActual = 1;

    public int Version { get; set; } = VersionActual;

    public string Obra { get; set; } = string.Empty;

    /// <summary>Cuando se guardo, en ISO 8601.</summary>
    public string Guardado { get; set; } = string.Empty;

    public List<FilaMapeo> Filas { get; set; } = new();
}

/// <summary>
/// El mapeo de secciones a tipos de Revit, en memoria.
/// </summary>
/// <remarks>
/// Existe para que el cuadro no se tenga que llenar dos veces. Un modelo se reimporta
/// muchas veces mientras el calculo cambia, y volver a elegir quince familias en cada vuelta
/// es lo que hace que la gente deje de usar la herramienta.
/// </remarks>
public sealed class Mapeo
{
    private readonly Dictionary<string, FilaMapeo> _filas = new(StringComparer.Ordinal);

    public string Obra { get; set; } = string.Empty;

    public int Cuantas => _filas.Count;

    public IEnumerable<FilaMapeo> Filas => _filas.Values;

    public FilaMapeo? De(string clave) =>
        _filas.TryGetValue(clave ?? string.Empty, out var f) ? f : null;

    /// <summary>Si la seccion ya tiene un tipo elegido.</summary>
    public bool Tiene(string? clave) =>
        _filas.TryGetValue(clave ?? string.Empty, out var f)
        && !string.IsNullOrWhiteSpace(f.Tipo);

    public void Poner(SeccionDelModelo seccion, TipoRevit tipo)
    {
        if (seccion is null)
        {
            throw new ArgumentNullException(nameof(seccion));
        }

        if (tipo is null)
        {
            throw new ArgumentNullException(nameof(tipo));
        }

        _filas[seccion.Clave] = new FilaMapeo
        {
            Clave = seccion.Clave,
            Seccion = seccion.Seccion.Nombre,
            Clase = seccion.Clase,

            // La del TIPO, no la que le tocaria por su clase: es la que el usuario eligio.
            Categoria = tipo.Categoria,
            Familia = tipo.Familia,
            Tipo = tipo.Tipo,
            TipoId = tipo.Id
        };
    }

    public void Quitar(string clave) => _filas.Remove(clave ?? string.Empty);

    /// <summary>
    /// Vuelve a atar cada fila guardada con un tipo que EXISTA ahora en el proyecto.
    /// </summary>
    /// <remarks>
    /// Se busca por nombre de familia y de tipo, no por id, porque un mapeo se guarda para
    /// reusarlo en otro modelo y los ElementId no significan lo mismo en dos proyectos. Lo
    /// que ya no exista se devuelve como perdido, para poder decirselo al usuario en vez de
    /// dejar la fila vacia sin explicacion.
    /// </remarks>
    public List<FilaMapeo> Reatar(CatalogoRevit catalogo)
    {
        var perdidas = new List<FilaMapeo>();

        foreach (var f in _filas.Values.ToList())
        {
            var t = catalogo.PorNombre(f.Categoria, f.Familia, f.Tipo);

            if (t is null)
            {
                perdidas.Add(f);
                _filas.Remove(f.Clave);
                continue;
            }

            f.TipoId = t.Id;
        }

        return perdidas;
    }

    /// <summary>El tipo elegido para una seccion, buscado en el catalogo de ahora.</summary>
    public TipoRevit? TipoDe(SeccionDelModelo seccion, CatalogoRevit catalogo)
    {
        var f = De(seccion.Clave);

        if (f is null)
        {
            return null;
        }

        // Por la categoria GUARDADA. Usar la de la seccion perderia la eleccion del usuario
        // cuando cambio de categoria en el cuadro.
        return catalogo.PorNombre(f.Categoria, f.Familia, f.Tipo);
    }

    public MapeoGuardado AGuardado() => new()
    {
        Version = MapeoGuardado.VersionActual,
        Obra = Obra,
        Guardado = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),

        // Ordenado para que dos guardados del mismo mapeo den el mismo archivo y se pueda
        // comparar con git o a ojo.
        Filas = _filas.Values.OrderBy(f => f.Clave, StringComparer.Ordinal).ToList()
    };

    public static Mapeo DeGuardado(MapeoGuardado g)
    {
        var m = new Mapeo { Obra = g.Obra ?? string.Empty };

        foreach (var f in g.Filas ?? new List<FilaMapeo>())
        {
            if (!string.IsNullOrWhiteSpace(f.Clave))
            {
                m._filas[f.Clave] = f;
            }
        }

        return m;
    }
}

/// <summary>Leer y escribir el archivo del mapeo.</summary>
public static class ArchivoMapeo
{
    public const string Extension = ".cadlink-mapeo.json";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ATexto(MapeoGuardado g) => JsonSerializer.Serialize(g, Opciones);

    public static MapeoGuardado DeTexto(string texto)
    {
        MapeoGuardado? g;

        try
        {
            g = JsonSerializer.Deserialize<MapeoGuardado>(texto, Opciones);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                "El archivo del mapeo no es un JSON valido: " + e.Message, e);
        }

        if (g is null)
        {
            throw new InvalidDataException("El archivo del mapeo esta vacio.");
        }

        g.Filas ??= new List<FilaMapeo>();

        return g;
    }

    /// <summary>La ruta del mapeo que le corresponde a un modelo.</summary>
    /// <remarks>
    /// Va al lado del modelo y con su nombre, no en una carpeta de configuracion del usuario:
    /// el mapeo es de ESTA obra, y si el modelo se copia a otra maquina el mapeo tiene que
    /// viajar con el.
    /// </remarks>
    public static string RutaPara(string rutaModelo)
    {
        var carpeta = Path.GetDirectoryName(rutaModelo) ?? string.Empty;
        var nombre = Path.GetFileName(rutaModelo);

        // El modelo acaba en '.cadlink-modelo.json', que son DOS extensiones para
        // Path.ChangeExtension: se quita la terminacion completa a mano.
        if (nombre.EndsWith(ArchivoModelo.Extension, StringComparison.OrdinalIgnoreCase))
        {
            nombre = nombre[..^ArchivoModelo.Extension.Length];
        }
        else
        {
            nombre = Path.GetFileNameWithoutExtension(nombre);
        }

        return Path.Combine(carpeta, nombre + Extension);
    }

    public static void Guardar(Mapeo mapeo, string ruta)
    {
        var temporal = ruta + ".tmp";

        File.WriteAllText(temporal, ATexto(mapeo.AGuardado()), new System.Text.UTF8Encoding(false));

        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }

        File.Move(temporal, ruta);
    }

    /// <summary>Lee el mapeo, o devuelve uno vacio si no hay archivo.</summary>
    /// <remarks>
    /// No tener mapeo guardado es lo normal la primera vez, asi que no es un error.
    /// </remarks>
    public static Mapeo LeerOVacio(string ruta)
    {
        if (!File.Exists(ruta))
        {
            return new Mapeo();
        }

        return Mapeo.DeGuardado(DeTexto(File.ReadAllText(ruta)));
    }
}
