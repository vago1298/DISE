using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  EL ARMADO POR TIPO DE REVIT
//
//  En Revit el armado se pone eligiendo una pieza ya dibujada. Con cien trabes iguales eso es
//  elegir cien veces. Aqui se hace por TIPO: el complemento lista los tipos de columna y de
//  trabe que hay en el proyecto -con cuantas piezas tiene cada uno-, a cada tipo se le asigna
//  una seccion de la tabla de CadLink, y se arman TODAS sus piezas de un jalon.
//
//  Las secciones llegan en un archivo propio, el .cadlink-armado.json, que CadLink escribe
//  desde la hoja de secciones de concreto con el boton «Armado para Revit». No hace falta
//  haber modelado en Revit desde ETABS: sirve para cualquier proyecto.
// ============================================================================

/// <summary>El archivo con las secciones de la tabla, para armar en Revit por tipo.</summary>
public sealed class ArchivoArmadoJson
{
    public const int VersionActual = 1;

    public int Version { get; set; } = VersionActual;

    public string Aplicacion { get; set; } = string.Empty;

    public DateTime Fecha { get; set; }

    /// <summary>De donde salio: el trabajo de CadLink.</summary>
    public string Origen { get; set; } = string.Empty;

    public List<ArmadoJson> Armados { get; set; } = new();
}

/// <summary>Leer y escribir el <c>.cadlink-armado.json</c>.</summary>
public static class ArchivoArmado
{
    public const string Extension = ".cadlink-armado.json";

    public const string Filtro = "Secciones de CadLink (*.cadlink-armado.json)|*.cadlink-armado.json";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ATexto(ArchivoArmadoJson a) =>
        JsonSerializer.Serialize(a ?? throw new ArgumentNullException(nameof(a)), Opciones);

    /// <exception cref="InvalidDataException">Si el archivo no sirve o es de otra version.</exception>
    public static ArchivoArmadoJson DeTexto(string texto)
    {
        ArchivoArmadoJson? a;

        try
        {
            a = JsonSerializer.Deserialize<ArchivoArmadoJson>(texto, Opciones);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("El archivo de secciones no es un JSON valido: " + e.Message, e);
        }

        if (a is null)
        {
            throw new InvalidDataException("El archivo de secciones esta vacio.");
        }

        if (a.Version > ArchivoArmadoJson.VersionActual)
        {
            throw new InvalidDataException(
                $"El archivo es de la version {a.Version} y este complemento entiende hasta la "
                + $"{ArchivoArmadoJson.VersionActual}. Actualiza el complemento de Revit.");
        }

        a.Armados ??= new List<ArmadoJson>();

        foreach (var x in a.Armados)
        {
            x.Varillas ??= new List<VarillaJson>();
            x.Bastones ??= new List<BastonRecetaJson>();
        }

        return a;
    }

    public static void Guardar(ArchivoArmadoJson a, string ruta)
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

    public static ArchivoArmadoJson Leer(string ruta)
    {
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException("No existe el archivo de secciones: " + ruta, ruta);
        }

        return DeTexto(File.ReadAllText(ruta));
    }
}

/// <summary>Un tipo de Revit con sus piezas, como lo cuenta el complemento.</summary>
public sealed class TipoArmable
{
    /// <summary>El Id del tipo en Revit.</summary>
    public long Id { get; set; }

    /// <summary><see cref="ClasePieza.Columna"/> o <see cref="ClasePieza.Trabe"/>.</summary>
    public ClasePieza Clase { get; set; }

    public string Familia { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    /// <summary>Cuantas piezas de este tipo hay en el proyecto.</summary>
    public int Piezas { get; set; }

    /// <summary>De esas, cuantas admite Revit como anfitrion de armado: las de concreto.</summary>
    public int DeConcreto { get; set; }

    /// <summary>Cuantas ya tienen armado puesto por CadLink.</summary>
    public int YaArmadas { get; set; }

    public double? AnchoM { get; set; }

    public double? PeralteM { get; set; }

    public string Nombre => Familia.Length == 0 ? Tipo : $"{Familia} : {Tipo}";
}

/// <summary>Una fila del cuadro: un tipo de Revit y la seccion de CadLink que lo arma.</summary>
public sealed class FilaArmadoTipo : Avisador
{
    /// <summary>La opcion de no armar este tipo.</summary>
    public const string SinArmar = "(no armar)";

    private readonly IReadOnlyDictionary<string, ArmadoJson> _armados;
    private string _seccion = SinArmar;
    private bool _armar;

    internal FilaArmadoTipo(TipoArmable tipo, IReadOnlyList<ArmadoJson> compatibles)
    {
        Tipo = tipo;
        _armados = compatibles.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        Secciones = new ObservableCollection<string>(
            new[] { SinArmar }.Concat(compatibles.Select(a => a.Id)));
    }

    public TipoArmable Tipo { get; }

    public string Categoria => Tipo.Clase == ClasePieza.Columna ? "Columna" : "Trabe";

    public string Familia => Tipo.Familia;

    public string NombreTipo => Tipo.Tipo;

    public int Piezas => Tipo.Piezas;

    public int DeConcreto => Tipo.DeConcreto;

    public int YaArmadas => Tipo.YaArmadas;

    public string Medidas => Tipo.AnchoM is double b && Tipo.PeralteM is double h
        ? $"{b * 100:0.#} x {h * 100:0.#}"
        : "?";

    /// <summary>Las secciones de la tabla que pueden armar este tipo, y la de no armarlo.</summary>
    public ObservableCollection<string> Secciones { get; }

    /// <summary>Como se eligio la seccion: por nombre, por medidas, o a mano.</summary>
    public string Sugerencia { get; private set; } = string.Empty;

    /// <summary>La seccion de CadLink elegida, o <see cref="SinArmar"/>.</summary>
    public string Seccion
    {
        get => _seccion;
        set
        {
            var v = Secciones.Contains(value) ? value : SinArmar;

            if (v == _seccion)
            {
                return;
            }

            _seccion = v;
            Sugerencia = string.Empty;
            _armar = v != SinArmar && DeConcreto > 0;
            Aviso();
            Aviso(nameof(Armar));
            Aviso(nameof(Sugerencia));
            Aviso(nameof(Diagnostico));
            Aviso(nameof(Coherente));
        }
    }

    /// <summary>Si se arma al aceptar. Solo se puede con una seccion elegida.</summary>
    public bool Armar
    {
        get => _armar;
        set
        {
            var v = value && Armado is not null && DeConcreto > 0;

            if (v == _armar)
            {
                return;
            }

            _armar = v;
            Aviso();
        }
    }

    /// <summary>La seccion elegida, o null.</summary>
    public ArmadoJson? Armado => _armados.TryGetValue(_seccion, out var a) ? a : null;

    /// <summary>Si las medidas del tipo cuadran con las de la seccion elegida.</summary>
    public bool Coherente => Armado is null || MedidasCuadran(Armado) != false;

    /// <summary>Lo que conviene saber de la fila antes de armar.</summary>
    public string Diagnostico
    {
        get
        {
            var partes = new List<string>();

            if (DeConcreto == 0)
            {
                partes.Add("ninguna pieza es de concreto: Revit no las deja armar");
            }
            else if (DeConcreto < Piezas)
            {
                partes.Add($"{Piezas - DeConcreto} de {Piezas} no son de concreto y se saltan");
            }

            if (Armado is { } a)
            {
                if (MedidasCuadran(a) == false)
                {
                    partes.Add($"OJO: «{a.Id}» mide {a.BaseCm:0.#} x {a.AlturaCm:0.#} y el tipo {Medidas}");
                }

                if (YaArmadas > 0)
                {
                    partes.Add($"{YaArmadas} ya armadas: se rehacen");
                }
            }

            if (Sugerencia.Length > 0)
            {
                partes.Insert(0, Sugerencia);
            }

            return string.Join(" · ", partes);
        }
    }

    /// <summary>Con la tolerancia del emparejador: medio centimetro. Null si no se sabe.</summary>
    private bool? MedidasCuadran(ArmadoJson a)
    {
        if (Tipo.AnchoM is not double b || Tipo.PeralteM is not double h)
        {
            return null;
        }

        bool Mide(double x, double y) =>
            Math.Abs(a.BaseCm - (x * 100)) <= 0.5 && Math.Abs(a.AlturaCm - (y * 100)) <= 0.5;

        return Mide(b, h) || (Tipo.Clase == ClasePieza.Columna && Mide(h, b));
    }

    internal void Sugerir(EmparejarArmado.Resultado r)
    {
        if (r.Armado is null)
        {
            return;
        }

        Seccion = r.Armado.Id;
        Sugerencia = r.Por == "nombre" ? "sugerida por su nombre" : "sugerida por sus medidas";
        Aviso(nameof(Sugerencia));
        Aviso(nameof(Diagnostico));
    }
}

/// <summary>El cuadro «Armar por tipo»: todos los tipos de columna y trabe del proyecto.</summary>
public sealed class VistaArmadoPorTipo : Avisador
{
    public VistaArmadoPorTipo(IEnumerable<TipoArmable> tipos, IReadOnlyList<ArmadoJson> armados)
    {
        // Solo las secciones que se pueden armar: rectangulares, con receta y con varillas.
        Armados = armados.Where(a => a.TieneReceta && a.BaseCm > 0 && a.AlturaCm > 0).ToList();

        foreach (var t in tipos
                     .Where(t => (t.Clase is ClasePieza.Columna or ClasePieza.Trabe) && t.Piezas > 0)
                     .OrderBy(t => t.Clase)
                     .ThenBy(t => t.Familia, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(t => t.Tipo, StringComparer.CurrentCultureIgnoreCase))
        {
            var compatibles = Armados.Where(a => Compatible(a, t.Clase)).ToList();
            var f = new FilaArmadoTipo(t, compatibles);

            // Lo mas parecido de una vez: por el nombre del tipo y, si no, por sus medidas.
            f.Sugerir(EmparejarArmado.Buscar(
                t.Tipo, t.Clase, t.AnchoM ?? 0, t.PeralteM ?? 0, compatibles));

            f.PropertyChanged += (_, _) => Aviso(nameof(Resumen));
            Filas.Add(f);
        }
    }

    public IReadOnlyList<ArmadoJson> Armados { get; }

    public ObservableCollection<FilaArmadoTipo> Filas { get; } = new();

    /// <summary>Las filas que se van a armar.</summary>
    public IEnumerable<FilaArmadoTipo> AArmar => Filas.Where(f => f.Armar && f.Armado is not null);

    public int PiezasAArmar => AArmar.Sum(f => f.DeConcreto);

    public string Resumen
    {
        get
        {
            var tipos = AArmar.Count();
            return tipos == 0
                ? "Elige la seccion de CadLink de cada tipo que quieras armar."
                : $"Se van a armar {PiezasAArmar} pieza(s) de {tipos} tipo(s).";
        }
    }

    /// <summary>Marca o desmarca todas las filas que tienen seccion.</summary>
    public void MarcarTodas(bool armar)
    {
        foreach (var f in Filas)
        {
            f.Armar = armar;
        }
    }

    private static bool Compatible(ArmadoJson a, ClasePieza clase) => clase switch
    {
        ClasePieza.Trabe => a.Tipo is "Trabe" or "Contratrabe",
        ClasePieza.Columna => a.Tipo is "Columna" or "Dado",
        _ => false
    };
}
