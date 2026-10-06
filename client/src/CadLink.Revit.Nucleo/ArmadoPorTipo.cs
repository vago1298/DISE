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

/// <summary>Lo elegido en la tabla de armaduras, recordado para la proxima vez.</summary>
public static class ArchivoVarillas
{
    /// <summary>En la carpeta del usuario: vale para todos sus proyectos.</summary>
    public static string Ruta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CadLink", "armaduras-revit.json");

    public static Dictionary<string, string> Leer(string? ruta = null)
    {
        try
        {
            var r = ruta ?? Ruta;
            return File.Exists(r)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(r)) ?? new()
                : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Junta lo nuevo con lo que ya habia. Si no se puede guardar, no pasa nada.</summary>
    public static void Guardar(IReadOnlyDictionary<string, string> elecciones, string? ruta = null)
    {
        try
        {
            var r = ruta ?? Ruta;
            var todo = Leer(r);

            foreach (var (k, v) in elecciones)
            {
                todo[k] = v;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(r)!);
            File.WriteAllText(r, JsonSerializer.Serialize(todo, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Es una comodidad: armar ya se hizo.
        }
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

/// <summary>Un tipo de armadura -<c>RebarBarType</c>- del proyecto de Revit.</summary>
public sealed class TipoDeVarillaRevit
{
    public long Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Diametro nominal, en m.</summary>
    public double DiamM { get; set; }
}

/// <summary>
/// Una fila de la tabla de armaduras: con que tipo de armadura de Revit se pone la varilla
/// <see cref="Clave"/> cuando es <see cref="Uso"/> de una <see cref="Pieza"/>.
/// </summary>
/// <remarks>
/// En una oficina el mismo #4 tiene varios tipos de armadura -«VAR #4C TRABES», «VAR #4C
/// COLUMNAS/CASTILLOS», «VAR #4C BASTON»…- para que cada uso salga en su capa y su tabla de
/// cuantificacion. Por eso se elige por pieza y uso, no solo por diametro.
/// </remarks>
public sealed class FilaVarilla : Avisador
{
    /// <summary>La opcion de que el complemento elija por diametro, o cree el tipo.</summary>
    public const string Automatico = "(automático: por diámetro)";

    private readonly IReadOnlyList<TipoDeVarillaRevit> _tipos;
    private string _elegido = Automatico;

    internal FilaVarilla(string pieza, string uso, string clave, double diamCm,
        IReadOnlyList<TipoDeVarillaRevit> tipos)
    {
        Pieza = pieza;
        Uso = uso;
        Clave = clave;
        DiamCm = diamCm;
        _tipos = tipos;
        Opciones = new ObservableCollection<string>(new[] { Automatico }.Concat(tipos.Select(t => t.Nombre)));
    }

    /// <summary><c>Trabe</c> o <c>Columna</c>.</summary>
    public string Pieza { get; }

    /// <summary><c>Corrida</c>, <c>Baston</c> o <c>Estribo</c>.</summary>
    public string Uso { get; }

    public string Clave { get; }

    public double DiamCm { get; }

    public string Descripcion => Uso switch
    {
        "Estribo" => $"Estribos {Clave} de {(Pieza == "Trabe" ? "trabes" : "columnas")}",
        "Baston" => $"Bastones {Clave} de trabes",
        _ => $"Corridas y laterales {Clave} de {(Pieza == "Trabe" ? "trabes" : "columnas")}"
    };

    /// <summary>La llave con la que se recuerda la eleccion.</summary>
    public string Llave => $"{Pieza}|{Uso}|{Clave}";

    public ObservableCollection<string> Opciones { get; }

    /// <summary>Si la puso el complemento -por nombre, o la de la vez pasada- y no la persona.</summary>
    public string Origen { get; private set; } = string.Empty;

    public string Elegido
    {
        get => _elegido;
        set
        {
            var v = Opciones.Contains(value) ? value : Automatico;

            if (v == _elegido)
            {
                return;
            }

            _elegido = v;
            Origen = string.Empty;
            Aviso();
            Aviso(nameof(Origen));
        }
    }

    /// <summary>El Id del tipo de armadura elegido, o null para el automatico.</summary>
    public long? IdElegido => _tipos.FirstOrDefault(t => t.Nombre == _elegido)?.Id;

    internal void Poner(string? nombre, string origen)
    {
        if (nombre is null || !Opciones.Contains(nombre))
        {
            return;
        }

        Elegido = nombre;
        Origen = origen;
        Aviso(nameof(Origen));
    }
}

/// <summary>
/// El tipo de armadura de Revit que mejor le va a una varilla, por su NOMBRE: el numero de la
/// varilla exacto (#2 no es #2.5) y las palabras de su uso y de su pieza.
/// </summary>
public static class SugerirVarilla
{
    public static TipoDeVarillaRevit? Mejor(
        string clave, double diamCm, string pieza, string uso, IReadOnlyList<TipoDeVarillaRevit> tipos)
    {
        var numero = Numero(clave);

        var candidatos = tipos.Where(t => numero is not null && Numero(t.Nombre) == numero).ToList();

        if (candidatos.Count == 0 && diamCm > 0)
        {
            // Sin el numero en el nombre, los que tengan su diametro.
            candidatos = tipos.Where(t => Math.Abs((t.DiamM * 100) - diamCm) <= 0.05).ToList();
        }

        return candidatos
            .Select(t => (t, p: Puntos(Sin(t.Nombre), pieza, uso)))
            .OrderByDescending(x => x.p)
            .ThenBy(x => x.t.Nombre.Length)
            .Select(x => x.t)
            .FirstOrDefault();
    }

    private static int Puntos(string n, string pieza, string uso)
    {
        var p = 0;
        var estribo = n.Contains("ESTRIB");
        var baston = n.Contains("BASTON");

        p += uso switch
        {
            "Estribo" => estribo ? 10 : -10,
            "Baston" => baston ? 10 : estribo ? -10 : -4,
            _ => estribo || baston || n.Contains("GRAPA") ? -10 : 0
        };

        // Lo que es de otros elementos.
        foreach (var otro in new[] { "LOSA", "MURO", "ZAPATA", "GRAPA" })
        {
            if (n.Contains(otro) && !(uso == "Corrida" && otro == "GRAPA"))
            {
                p -= 6;
            }
        }

        var deTrabe = n.Contains("TRABE") || n.Contains("CADENA") || n.Contains("VIGA");
        var deColumna = n.Contains("COLUMNA") || n.Contains("CASTILLO");

        if (pieza == "Trabe")
        {
            p += deTrabe ? 5 : deColumna ? -3 : 0;
        }
        else
        {
            p += deColumna ? 5 : deTrabe ? -3 : 0;
        }

        return p;
    }

    /// <summary>El numero de la varilla en un texto: «VAR #2.5C» da 2.5; «#4» da 4.</summary>
    private static string? Numero(string? texto)
    {
        var m = System.Text.RegularExpressions.Regex.Match(texto ?? string.Empty, @"#\s*(\d+(?:[.,]\d+)?)");
        return m.Success ? m.Groups[1].Value.Replace(',', '.') : null;
    }

    /// <summary>A mayusculas y sin acentos.</summary>
    private static string Sin(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
}

/// <summary>El cuadro «Armar por tipo»: todos los tipos de columna y trabe del proyecto.</summary>
public sealed class VistaArmadoPorTipo : Avisador
{
    private readonly IReadOnlyList<TipoDeVarillaRevit> _tiposDeVarilla;
    private readonly IReadOnlyDictionary<string, string> _recordadas;
    private readonly Dictionary<string, FilaVarilla> _todasLasVarillas = new();

    public VistaArmadoPorTipo(IEnumerable<TipoArmable> tipos, IReadOnlyList<ArmadoJson> armados)
        : this(tipos, armados, Array.Empty<TipoDeVarillaRevit>(), null)
    {
    }

    /// <param name="tiposDeVarilla">Los tipos de armadura del proyecto.</param>
    /// <param name="recordadas">Lo que se eligio la vez pasada, por <see cref="FilaVarilla.Llave"/>.</param>
    public VistaArmadoPorTipo(
        IEnumerable<TipoArmable> tipos, IReadOnlyList<ArmadoJson> armados,
        IReadOnlyList<TipoDeVarillaRevit> tiposDeVarilla, IReadOnlyDictionary<string, string>? recordadas)
    {
        _tiposDeVarilla = tiposDeVarilla.OrderBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
        _recordadas = recordadas ?? new Dictionary<string, string>();

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

            f.PropertyChanged += (_, _) =>
            {
                ActualizarVarillas();
                Aviso(nameof(Resumen));
            };
            Filas.Add(f);
        }

        ActualizarVarillas();
    }

    /// <summary>
    /// Las varillas que llevan las secciones marcadas, una fila por pieza, uso y clave: lo que
    /// hay que decidir con que tipo de armadura de Revit va.
    /// </summary>
    public ObservableCollection<FilaVarilla> Varillas { get; } = new();

    /// <summary>Lo elegido en la tabla de armaduras, para recordarlo la proxima vez.</summary>
    public Dictionary<string, string> Elecciones() =>
        _todasLasVarillas.Values
            .Where(v => v.Elegido != FilaVarilla.Automatico)
            .ToDictionary(v => v.Llave, v => v.Elegido);

    /// <summary>
    /// El tipo de armadura elegido para esta varilla de esta seccion, o null para que el
    /// complemento lo busque por diametro.
    /// </summary>
    public long? IdDeVarilla(ArmadoJson a, VarillaArmada v)
    {
        var uso = v.Que switch { "estribo" => "Estribo", "baston" => "Baston", _ => "Corrida" };
        var pieza = a.EsHorizontal ? "Trabe" : "Columna";

        return _todasLasVarillas.TryGetValue($"{pieza}|{uso}|{v.Clave}", out var f) ? f.IdElegido : null;
    }

    private void ActualizarVarillas()
    {
        var hacen = new Dictionary<string, (string Pieza, string Uso, string Clave, double Diam)>();

        void Agregar(string pieza, string uso, string clave, double diam)
        {
            if (clave.Length > 0)
            {
                hacen.TryAdd($"{pieza}|{uso}|{clave}", (pieza, uso, clave, diam));
            }
        }

        foreach (var a in AArmar.Select(f => f.Armado!).Distinct())
        {
            var pieza = a.EsHorizontal ? "Trabe" : "Columna";

            foreach (var v in a.Varillas)
            {
                Agregar(pieza, "Corrida", v.Clave, v.DiamCm);
            }

            foreach (var b in a.Bastones)
            {
                Agregar(pieza, "Baston", b.Clave, b.DiamCm);
            }

            Agregar(pieza, "Estribo", a.ClaveEstribo, a.DiamEstriboCm);
        }

        var orden = hacen.Values
            .OrderBy(x => x.Pieza == "Trabe" ? 0 : 1)
            .ThenBy(x => x.Uso == "Corrida" ? 0 : x.Uso == "Baston" ? 1 : 2)
            .ThenBy(x => x.Diam)
            .ToList();

        var nuevas = new List<FilaVarilla>();

        foreach (var x in orden)
        {
            var llave = $"{x.Pieza}|{x.Uso}|{x.Clave}";

            if (!_todasLasVarillas.TryGetValue(llave, out var f))
            {
                // Se crea una vez y se guarda: si la seccion se desmarca y se vuelve a marcar,
                // la eleccion de la persona sigue ahi.
                f = new FilaVarilla(x.Pieza, x.Uso, x.Clave, x.Diam, _tiposDeVarilla);

                if (_recordadas.TryGetValue(llave, out var antes) && f.Opciones.Contains(antes))
                {
                    f.Poner(antes, "la de la vez pasada");
                }
                else
                {
                    f.Poner(SugerirVarilla.Mejor(x.Clave, x.Diam, x.Pieza, x.Uso, _tiposDeVarilla)?.Nombre,
                        "sugerida por su nombre");
                }

                _todasLasVarillas[llave] = f;
            }

            nuevas.Add(f);
        }

        if (nuevas.SequenceEqual(Varillas))
        {
            return;
        }

        Varillas.Clear();

        foreach (var f in nuevas)
        {
            Varillas.Add(f);
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
