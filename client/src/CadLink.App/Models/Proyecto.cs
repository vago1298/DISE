using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// Reflection y Globalization: la fila generica se lee por sus propiedades y sus numeros van
// en formato invariante, para que un trabajo guardado con coma decimal se abra donde hay punto.
using System.Globalization;
using System.Reflection;

namespace CadLink.App.Models;

/// <summary>
/// El trabajo completo guardado en un archivo <c>.clk</c>.
/// </summary>
/// <remarks>
/// <para>
/// Lo que pidió el usuario: <i>«habilita una opción de guardar el trabajo actual para
/// que cuando se cierre no vuelvas a hacer todo de nuevo»</i>.
/// </para>
/// <para>
/// Se guarda en <b>JSON</b> y no en un formato binario a propósito. Un archivo binario
/// es más compacto, pero cuando algo sale mal —y en un archivo de proyecto que el
/// usuario guarda durante años, algo acaba saliendo mal— con JSON se abre en un
/// editor y se ve qué tiene dentro. Con binario solo queda adivinar.
/// </para>
/// <para>
/// Lleva <see cref="Version"/> desde el primer día. Cuando el formato cambie hará
/// falta saber de qué versión viene el archivo, y añadir el número después obliga a
/// tratar todo lo anterior como «versión desconocida».
/// </para>
/// </remarks>
public sealed class ProyectoGuardado
{
    /// <summary>Versión del formato del archivo.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Con qué versión de la aplicación se guardó. Solo informativo.</summary>
    public string Aplicacion { get; set; } = string.Empty;

    public DateTime Guardado { get; set; } = DateTime.Now;

    // ---- Solapa ----
    public string Calculista { get; set; } = string.Empty;

    /// <summary>Cedula profesional del calculista. Solo el numero.</summary>
    public string Cedula { get; set; } = string.Empty;
    public string Propietario { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string Obra { get; set; } = string.Empty;
    public string Dibujo { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Escala { get; set; } = "1:50";
    public string Acotacion { get; set; } = "cm";

    // ---- Ajustes de dibujo ----
    public double EscalaDibujo { get; set; } = 0.01;
    public double EscalaHatch { get; set; } = 0.0003;
    public int ModoSeccion { get; set; } = 1;

    /// <summary>
    /// Doblez del gancho de arranque de las zapatas, en <b>diámetros</b>.
    /// </summary>
    /// <remarks>
    /// Es del juego entero, como el modo de sección, así que se guarda aquí y no por fila. Por
    /// omisión los <b>15</b> de la macro: un <c>.clk</c> guardado antes de que existiera esta
    /// casilla se abre con los 15 que tenía, que es lo que se dibujó cuando se guardó.
    /// </remarks>
    public double GanchoZapatasDiametros { get; set; } = 15.0;

    // ---- Contenido ----
    public List<PlanoGuardado> Planos { get; set; } = new();
    public List<SeccionGuardada> Secciones { get; set; } = new();

    /// <summary>Las filas de <b>Secciones Acero</b>.</summary>
    /// <remarks>
    /// Se agregan al final y vacías por omisión, igual que se hizo con la sección circular: un
    /// <c>.clk</c> guardado antes de que existieran estas hojas se sigue abriendo, y sale sin
    /// ellas, que es lo que tenía.
    /// </remarks>
    public List<FilaGuardada> Acero { get; set; } = new();

    /// <summary>Las filas de <b>Zapatas Aisladas</b>.</summary>
    public List<FilaGuardada> Zapatas { get; set; } = new();

    /// <summary>Las filas de la hoja de <b>zapatas corridas</b>.</summary>
    /// <remarks>
    /// En su propia lista y no revueltas con las aisladas: son otra hoja, con otras columnas y
    /// otro dibujante. Van con el mismo mecanismo genérico —<see cref="FilaSerializable"/>—, así
    /// que una columna nueva de esta hoja se guarda sola. Un <c>.clk</c> de antes de esta hoja
    /// llega sin la clave y la lista queda vacía, que es lo correcto: no había zapatas corridas.
    /// </remarks>
    public List<FilaGuardada> ZapatasCorridas { get; set; } = new();

    /// <summary>Las filas de la hoja de <b>placas base</b>.</summary>
    /// <remarks>
    /// Con el mismo mecanismo genérico que las dos hojas de zapatas, y por el mismo motivo: una
    /// columna nueva de esta hoja se guarda sola. Va también en la instantánea del deshacer —que
    /// serializa este mismo objeto—, así que sin esta clave un Ctrl+Z después de capturar una
    /// placa habría <b>borrado la hoja entera</b>, que es la clase de sorpresa que un deshacer no
    /// puede dar.
    /// </remarks>
    public List<FilaGuardada> PlacasBase { get; set; } = new();
}

public sealed class PlanoGuardado
{
    public string Clave { get; set; } = string.Empty;
    public string Contiene { get; set; } = string.Empty;

    /// <summary>La segunda linea del contenido: seccion y detalles.</summary>
    public string Detalle { get; set; } = string.Empty;

    public string Escala { get; set; } = string.Empty;

    /// <summary>
    /// Tamano de hoja y orientacion, para el generador de solapas.
    /// </summary>
    /// <remarks>
    /// <b>Horizontal empieza en <c>true</c></b> y el tamano en blanco a proposito: al abrir un
    /// archivo guardado ANTES de que existieran estas dos columnas, JSON deja el bool en false y
    /// la cadena vacia. Un tamano vacio se corrige solo -el lector le pone ARCH D-, pero una hoja
    /// vertical no se nota hasta que sale el plano. Ver el lector del proyecto.
    /// </remarks>
    public string Tamano { get; set; } = string.Empty;

    public bool Horizontal { get; set; } = true;
}

/// <summary>
/// Un renglón de la tabla de secciones, tal como se guarda.
/// </summary>
/// <remarks>
/// Se guardan <b>solo los datos que el usuario escribió</b>. Las columnas calculadas
/// —total de varillas, área de acero, cuantía— no se guardan: se recalculan al abrir.
/// Guardarlas sería duplicar la verdad, y el día que se corrija una fórmula los
/// archivos viejos seguirían mostrando el número antiguo.
/// </remarks>
public sealed class SeccionGuardada
{
    public string Elemento { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public double BaseCm { get; set; }
    public double AlturaCm { get; set; }

    public int NEsqSup { get; set; }
    public string DiamEsqSup { get; set; } = string.Empty;
    public int NIntSup { get; set; }
    public string DiamIntSup { get; set; } = string.Empty;
    public int NEsqInf { get; set; }
    public string DiamEsqInf { get; set; } = string.Empty;
    public int NIntInf { get; set; }
    public string DiamIntInf { get; set; } = string.Empty;
    public int NInter { get; set; }
    public string DiamInter { get; set; } = string.Empty;

    // ---------------- Sección circular ----------------
    // Se agregan al final y con valor por omisión vacío / cero a propósito: un .clk
    // guardado ANTES de que existiera la sección circular se sigue abriendo, y sus
    // filas salen rectangulares, que es lo que eran. Sin esto, abrir un trabajo
    // viejo fallaría o, peor, lo abriría con datos inventados.
    public string Circular { get; set; } = string.Empty;
    public int NVarTotal { get; set; }
    public string DiamVarTotal { get; set; } = string.Empty;
    public string ZunchoHelicoidal { get; set; } = string.Empty;

    public double RecubrimientoCm { get; set; }
    public string Estribo { get; set; } = string.Empty;
    public string SeparacionCm { get; set; } = string.Empty;
    public string EstriboDiamante { get; set; } = string.Empty;
    public string DiamEstriboDiamante { get; set; } = string.Empty;
    public double GanchoCm { get; set; }
    public string Fc { get; set; } = string.Empty;
    public string Escala { get; set; } = string.Empty;
    public double LongitudM { get; set; }

    // ---------------- Grapas ----------------
    // Al final y con la lista vacía por omisión, por el mismo motivo que la sección
    // circular de arriba: un .clk guardado antes de que existieran las grapas se
    // abre igual y sus secciones salen sin ninguna, que es lo que tenían. La
    // versión del archivo NO sube, porque nada de lo que ya se guardaba cambió de
    // significado.
    public List<GrapaGuardada> Grapas { get; set; } = new();
}

/// <summary>Una grapa, como se guarda en el archivo del proyecto.</summary>
/// <remarks>
/// Se guardan los <b>números</b> del lecho y del índice, y no el
/// <see cref="Models.RefVarilla"/> directamente, para que el archivo no dependa de la
/// forma interna de una estructura de C#: si algún día se le agrega un campo, los
/// proyectos ya guardados siguen abriéndose.
/// </remarks>
public sealed class GrapaGuardada
{
    /// <summary>El lecho de la primera varilla, como número de <c>LechoVarilla</c>.</summary>
    public int LechoA { get; set; }

    public int IndiceA { get; set; }

    /// <summary>El lecho de la segunda varilla.</summary>
    public int LechoB { get; set; }

    public int IndiceB { get; set; }

    /// <summary>Clave de la varilla de la grapa, por ejemplo <c>#3</c>.</summary>
    public string Diametro { get; set; } = string.Empty;
}

/// <summary>Lee y escribe los archivos <c>.clk</c>.</summary>
public static class ArchivoProyecto
{
    public const string Extension = ".clk";

    public const string Filtro =
        "Trabajo de CadLink (*.clk)|*.clk|Todos los archivos (*.*)|*.*";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,

        // Sin esto, un acento en el nombre del propietario se guarda como \u00E1 y el
        // archivo deja de poder leerse a ojo, que es justo la razon de usar JSON.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Escribe el proyecto. Primero a un temporal y luego se cambia por el bueno.
    /// </summary>
    /// <remarks>
    /// <b>El temporal no es paranoia.</b> Si se escribe directamente sobre el archivo
    /// y algo falla a medias —se llena el disco, se corta la luz—, el trabajo anterior
    /// ya está machacado y el nuevo está incompleto: se pierden los dos. Escribiendo
    /// aparte y cambiando al final, el archivo bueno solo desaparece cuando ya existe
    /// su reemplazo completo.
    /// </remarks>
    public static void Guardar(string ruta, ProyectoGuardado p)
    {
        var temporal = ruta + ".tmp";

        File.WriteAllText(temporal, JsonSerializer.Serialize(p, Opciones));

        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }

        File.Move(temporal, ruta);
    }

    /// <summary>Lee el proyecto de un archivo.</summary>
    /// <exception cref="InvalidDataException">El archivo no es un trabajo válido.</exception>
    public static ProyectoGuardado Leer(string ruta)
    {
        var texto = File.ReadAllText(ruta);

        ProyectoGuardado? p;

        try
        {
            p = JsonSerializer.Deserialize<ProyectoGuardado>(texto, Opciones);
        }
        catch (JsonException ex)
        {
            // Se dice QUE archivo y POR QUE: "no se pudo abrir" no ayuda a nadie.
            throw new InvalidDataException(
                $"El archivo '{Path.GetFileName(ruta)}' no parece un trabajo de CadLink. " +
                "Detalle: " + ex.Message, ex);
        }

        if (p is null)
        {
            throw new InvalidDataException(
                $"El archivo '{Path.GetFileName(ruta)}' está vacío o incompleto.");
        }

        if (p.Version > 1)
        {
            throw new InvalidDataException(
                $"El archivo se guardó con una versión más nueva de CadLink " +
                $"(formato {p.Version}). Actualiza la aplicación para abrirlo.");
        }

        return p;
    }
}


/// <summary>
/// Un renglón de una hoja <b>cualquiera</b>, guardado como pares de nombre y valor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué esto existe.</b> Las secciones de concreto se guardan con su propia clase, campo
/// por campo, y eso funcionó mientras hubo una sola hoja. Cuando llegaron la de <b>acero</b> y la
/// de <b>zapatas aisladas</b>, nadie volvió a tocar el archivo del proyecto: guardar el trabajo
/// escribía solo el concreto y las otras dos hojas <b>se perdían</b> —y con ellas se perdía el
/// trabajo de verdad, porque una zapata se captura una vez—. El defecto no era una línea mal
/// puesta: era que el formato obligaba a acordarse de una lista por cada columna nueva.
/// </para>
/// <para>
/// Aquí se guarda lo que el renglón <b>tiene escrito</b>, leyendo sus propiedades: así una columna
/// nueva se guarda sola el día que se agregue, que es justo lo que no pasó. Solo entran las
/// propiedades <b>que se pueden escribir</b> y de tipo simple: las calculadas —«Falta», «Resumen»,
/// los totales— no se guardan, porque se recalculan al abrir y guardarlas sería duplicar la verdad.
/// </para>
/// <para>
/// Los números van en formato <b>invariante</b>, con punto decimal. Un archivo guardado en una
/// máquina con coma decimal se abre igual en otra con punto, que es como se pasan los trabajos
/// entre dos computadoras.
/// </para>
/// </remarks>
public sealed class FilaGuardada
{
    public Dictionary<string, string> Valores { get; set; } = new();
}

/// <summary>Lee y escribe una fila cualquiera como pares de nombre y valor.</summary>
public static class FilaSerializable
{
    /// <summary>Recoge lo que la fila tiene escrito.</summary>
    public static FilaGuardada Leer(object fila)
    {
        var salida = new FilaGuardada();

        foreach (var p in Propiedades(fila.GetType()))
        {
            var v = p.GetValue(fila);

            if (v is null)
            {
                continue;
            }

            salida.Valores[p.Name] = v switch
            {
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                int i => i.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => v.ToString() ?? string.Empty
            };
        }

        return salida;
    }

    /// <summary>
    /// Vuelca los valores guardados en una fila nueva.
    /// </summary>
    /// <remarks>
    /// Lo que no se reconozca <b>se ignora en silencio</b>, y es a propósito: un archivo guardado
    /// con una versión que tenía una columna que ya no existe se sigue abriendo, y una columna
    /// nueva que el archivo no traiga se queda con su valor por omisión. Es lo que permite abrir
    /// trabajos viejos sin convertirlos.
    /// </remarks>
    public static void Aplicar(object fila, FilaGuardada guardada)
    {
        var mapa = Propiedades(fila.GetType())
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (var (nombre, texto) in guardada.Valores)
        {
            if (!mapa.TryGetValue(nombre, out var p))
            {
                continue;
            }

            try
            {
                if (p.PropertyType == typeof(string))
                {
                    p.SetValue(fila, texto);
                }
                else if (p.PropertyType == typeof(double))
                {
                    if (double.TryParse(texto, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var d))
                    {
                        p.SetValue(fila, d);
                    }
                }
                else if (p.PropertyType == typeof(int))
                {
                    if (int.TryParse(texto, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var i))
                    {
                        p.SetValue(fila, i);
                    }
                }
                else if (p.PropertyType == typeof(bool))
                {
                    if (bool.TryParse(texto, out var b))
                    {
                        p.SetValue(fila, b);
                    }
                }
            }
            catch (Exception)
            {
                // Un valor que la fila rechaza no puede tumbar la apertura del trabajo.
            }
        }
    }

    /// <summary>Las propiedades que se guardan: de instancia, escribibles y de tipo simple.</summary>
    private static IEnumerable<PropertyInfo> Propiedades(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => p.PropertyType == typeof(string)
                        || p.PropertyType == typeof(double)
                        || p.PropertyType == typeof(int)
                        || p.PropertyType == typeof(bool))
            .OrderBy(p => p.Name, StringComparer.Ordinal);
}
