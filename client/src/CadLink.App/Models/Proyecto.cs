using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // ---- Contenido ----
    public List<PlanoGuardado> Planos { get; set; } = new();
    public List<SeccionGuardada> Secciones { get; set; } = new();
}

public sealed class PlanoGuardado
{
    public string Clave { get; set; } = string.Empty;
    public string Contiene { get; set; } = string.Empty;
    public string Escala { get; set; } = string.Empty;
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

    public double RecubrimientoCm { get; set; }
    public string Estribo { get; set; } = string.Empty;
    public string SeparacionCm { get; set; } = string.Empty;
    public string EstriboDiamante { get; set; } = string.Empty;
    public string DiamEstriboDiamante { get; set; } = string.Empty;
    public double GanchoCm { get; set; }
    public string Fc { get; set; } = string.Empty;
    public string Escala { get; set; } = string.Empty;
    public double LongitudM { get; set; }
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
