using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadLink.Licensing;

/// <summary>
/// Contenido persistido del cache de licencia.
/// </summary>
internal sealed class CacheEnvelope
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Instante más avanzado que la aplicación ha observado. Es la defensa contra
    /// atrasar el reloj del sistema para estirar el periodo de gracia.
    /// </summary>
    [JsonPropertyName("last_seen_utc")]
    public DateTimeOffset LastSeenUtc { get; set; }

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>
/// Guarda el token en <c>%LOCALAPPDATA%\{app}\license.dat</c>, cifrado con DPAPI
/// en alcance de usuario.
/// </summary>
/// <remarks>
/// DPAPI con <see cref="DataProtectionScope.CurrentUser"/> hace que el archivo sea
/// ilegible en otra máquina o bajo otro usuario de Windows, así que copiarlo no
/// sirve de nada. Es una capa adicional: la defensa principal sigue siendo que el
/// token está firmado y atado a la huella del equipo.
/// </remarks>
public sealed class LicenseCache
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("CadLink.Licensing.v1");

    private readonly string _filePath;

    public LicenseCache(LicensingOptions options)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            options.AppFolderName);
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "license.dat");
    }

    public string FilePath => _filePath;

    public bool Exists => File.Exists(_filePath);

    internal CacheEnvelope? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(_filePath);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<CacheEnvelope>(plain);
        }
        catch (CryptographicException)
        {
            // Archivo de otro usuario u otra máquina, o alterado. Se descarta.
            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal void Save(CacheEnvelope envelope)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);

        // Escritura atómica: si el proceso muere a medio guardar, no queremos
        // dejar un license.dat truncado que obligue a reactivar.
        var temp = _filePath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, _filePath, overwrite: true);
    }

    /// <summary>
    /// Avanza la marca de tiempo observada. Solo hacia adelante, nunca hacia atrás.
    /// </summary>
    internal void TouchLastSeen(CacheEnvelope envelope)
    {
        var now = DateTimeOffset.UtcNow;
        if (now > envelope.LastSeenUtc)
        {
            envelope.LastSeenUtc = now;
            Save(envelope);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se puede borrar, la validación en línea corregirá el estado.
        }
    }
}
