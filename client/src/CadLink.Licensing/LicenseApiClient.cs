using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CadLink.Licensing;

/// <summary>Respuesta del servidor al activar o renovar.</summary>
internal sealed record TokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("tier")]
    public string Tier { get; init; } = string.Empty;

    [JsonPropertyName("org")]
    public string Org { get; init; } = string.Empty;
}

/// <summary>Error devuelto por el servidor con un código HTTP significativo.</summary>
public sealed class LicenseServerException : Exception
{
    public LicenseServerException(HttpStatusCode statusCode, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>La suscripción o la prueba terminaron (HTTP 402).</summary>
    public bool IsPaymentRequired => StatusCode == HttpStatusCode.PaymentRequired;

    /// <summary>El equipo fue dado de baja o requiere clave (HTTP 403).</summary>
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;

    /// <summary>El equipo no está registrado (HTTP 404).</summary>
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>Se agotaron los asientos de la licencia (HTTP 409).</summary>
    public bool IsSeatsExceeded => StatusCode == HttpStatusCode.Conflict;
}

/// <summary>
/// Cliente HTTP del servidor de licencias.
/// </summary>
/// <remarks>
/// <b>Interno a propósito.</b> Quien use esta librería debe pasar por
/// <see cref="LicenseService"/>, que es el que aplica la política completa: cache,
/// verificación de firma, periodo de gracia y renovación. Usar este cliente
/// directamente permitiría, por ejemplo, guardar un token sin verificarlo.
///
/// Además resuelve un error de compilación real: siendo público, sus métodos
/// devolvían <c>Task&lt;TokenResponse&gt;</c> con <c>TokenResponse</c> interno, y C#
/// no permite que un miembro público exponga un tipo menos accesible (CS0050).
/// </remarks>
internal sealed class LicenseApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly LicensingOptions _options;

    public LicenseApiClient(LicensingOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
        _http.Timeout = options.NetworkTimeout;
        _http.DefaultRequestHeaders.Add("User-Agent", $"CadLink/{options.AppVersion}");
    }

    /// <summary>
    /// Activa este equipo. Sin <paramref name="licenseKey"/>, el servidor decide el
    /// tier por el SID de dominio (interno) o entrega una prueba.
    /// </summary>
    public async Task<TokenResponse> ActivateAsync(string? licenseKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string?>
        {
            ["fingerprint"] = MachineFingerprint.Value,
            ["hostname"] = SafeHostName(),
            ["os_user"] = SafeUserName(),
            ["domain_sid"] = DomainInfo.GetDomainSid(),
            ["app_version"] = _options.AppVersion,
            ["license_key"] = string.IsNullOrWhiteSpace(licenseKey) ? null : licenseKey.Trim()
        };

        return await PostAsync("v1/activate", body, ct).ConfigureAwait(false);
    }

    /// <summary>Renueva el token de un equipo ya registrado.</summary>
    public async Task<TokenResponse> RenewAsync(CancellationToken ct = default)
    {
        var body = new Dictionary<string, string?>
        {
            ["fingerprint"] = MachineFingerprint.Value,
            ["app_version"] = _options.AppVersion
        };

        return await PostAsync("v1/renew", body, ct).ConfigureAwait(false);
    }

    private async Task<TokenResponse> PostAsync(
        string path, Dictionary<string, string?> body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadDetailAsync(response, ct).ConfigureAwait(false);
            throw new LicenseServerException(response.StatusCode, detail);
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(ct)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new LicenseServerException(
                response.StatusCode, "El servidor no devolvió un token válido.");
        }

        return payload;
    }

    /// <summary>
    /// Extrae el campo <c>detail</c> que usa FastAPI para los errores, de modo que
    /// el mensaje que ve el usuario sea el que escribiste en el servidor.
    /// </summary>
    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var doc = await response.Content
                .ReadFromJsonAsync<Dictionary<string, object>>(ct)
                .ConfigureAwait(false);

            if (doc is not null && doc.TryGetValue("detail", out var detail))
            {
                var text = detail?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            // Respuesta no JSON (por ejemplo, una página de error del proxy).
        }

        return $"El servidor respondió {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private static string SafeHostName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return "desconocido";
        }
    }

    private static string SafeUserName()
    {
        try
        {
            return $@"{Environment.UserDomainName}\{Environment.UserName}";
        }
        catch (InvalidOperationException)
        {
            return "desconocido";
        }
    }

    public void Dispose() => _http.Dispose();
}
