using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CadLink.Licensing;

/// <summary>
/// Claims del token, ya verificados.
/// </summary>
public sealed record LicenseClaims
{
    public required string Subject { get; init; }
    public required LicenseTier Tier { get; init; }
    public required string Organization { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LicenseExpiresAt { get; init; }
    public int GraceDays { get; init; }
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
}

public sealed class LicenseVerificationException : Exception
{
    public LicenseVerificationException(string message) : base(message) { }
}

/// <summary>
/// Verifica tokens JWT firmados con RS256 usando la llave pública embebida.
/// </summary>
/// <remarks>
/// Se implementa a mano en lugar de usar una librería de JWT para no arrastrar
/// dependencias grandes a un ensamblado que conviene mantener pequeño y ofuscable.
/// El alcance es deliberadamente estrecho: <b>solo</b> se acepta RS256.
/// </remarks>
public static class LicenseTokenVerifier
{
    private const string ExpectedIssuer = "cadlink-license-server";

    /// <summary>
    /// Verifica firma y claims. Lanza <see cref="LicenseVerificationException"/>
    /// si algo no cuadra.
    /// </summary>
    /// <param name="token">JWT compacto recibido del servidor.</param>
    /// <param name="expectedSubject">
    /// Huella de ESTE equipo. Es la defensa contra copiar el archivo de licencia
    /// a otra máquina: el token está atado a un equipo concreto.
    /// </param>
    public static LicenseClaims Verify(string token, string expectedSubject)
    {
        EmbeddedPublicKey.EnsureConfigured();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new LicenseVerificationException("Token vacío.");
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new LicenseVerificationException("Formato de token inválido.");
        }

        var header = ParseJson(parts[0]);
        var algorithm = GetString(header, "alg");

        // Rechazo explícito de cualquier algoritmo distinto de RS256. Aceptar
        // "none" o un HMAC aquí sería la vulnerabilidad clásica de JWT: permitiría
        // al atacante firmar sus propios tokens.
        if (!string.Equals(algorithm, "RS256", StringComparison.Ordinal))
        {
            throw new LicenseVerificationException($"Algoritmo no permitido: {algorithm}");
        }

        VerifySignature(parts[0], parts[1], parts[2]);

        var payload = ParseJson(parts[1]);

        var issuer = GetString(payload, "iss");
        if (!string.Equals(issuer, ExpectedIssuer, StringComparison.Ordinal))
        {
            throw new LicenseVerificationException("Emisor no reconocido.");
        }

        var subject = GetString(payload, "sub")
            ?? throw new LicenseVerificationException("Token sin sujeto.");

        if (!string.Equals(subject, expectedSubject, StringComparison.OrdinalIgnoreCase))
        {
            throw new LicenseVerificationException(
                "El token pertenece a otro equipo. Se requiere activación en este equipo.");
        }

        var tierText = GetString(payload, "tier");
        var tier = tierText switch
        {
            "INTERNAL" => LicenseTier.Internal,
            "COMMERCIAL" => LicenseTier.Commercial,
            "TRIAL" => LicenseTier.Trial,
            _ => throw new LicenseVerificationException($"Tier desconocido: {tierText}")
        };

        // El '?? throw' ya desenvuelve el nullable: 'iat' y 'exp' son
        // DateTimeOffset, no DateTimeOffset?. Por eso NO llevan .Value abajo.
        var iat = GetUnixTime(payload, "iat")
            ?? throw new LicenseVerificationException("Token sin fecha de emisión.");
        var exp = GetUnixTime(payload, "exp")
            ?? throw new LicenseVerificationException("Token sin expiración.");

        return new LicenseClaims
        {
            Subject = subject,
            Tier = tier,
            Organization = GetString(payload, "org") ?? string.Empty,
            IssuedAt = iat,
            ExpiresAt = exp,
            LicenseExpiresAt = GetUnixTime(payload, "license_expires_at"),
            GraceDays = GetInt(payload, "grace_days") ?? 0,
            Features = GetStringArray(payload, "features")
        };
    }

    private static void VerifySignature(string encodedHeader, string encodedPayload, string encodedSignature)
    {
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        var signature = Base64UrlDecode(encodedSignature);

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(EmbeddedPublicKey.Pem);
        }
        catch (ArgumentException ex)
        {
            throw new LicenseVerificationException(
                "La llave pública embebida no es válida: " + ex.Message);
        }

        var ok = rsa.VerifyData(
            signingInput,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        if (!ok)
        {
            throw new LicenseVerificationException("La firma del token no es válida.");
        }
    }

    private static JsonElement ParseJson(string base64Url)
    {
        try
        {
            var json = Base64UrlDecode(base64Url);
            using var doc = JsonDocument.Parse(json);
            // Clone(): el JsonDocument se libera al salir, el elemento debe sobrevivir.
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new LicenseVerificationException("No se pudo leer el contenido del token.");
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Longitud base64url inválida.");
        }

        return Convert.FromBase64String(s);
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static DateTimeOffset? GetUnixTime(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(v.GetInt64());
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (s is not null)
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }
}
