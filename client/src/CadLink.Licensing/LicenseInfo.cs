namespace CadLink.Licensing;

/// <summary>
/// Estado de la licencia tal como lo consume la interfaz de usuario.
/// </summary>
public sealed record LicenseInfo
{
    public required LicenseState State { get; init; }

    public LicenseTier Tier { get; init; } = LicenseTier.Unknown;

    /// <summary>Nombre que se muestra en el splash.</summary>
    public string Organization { get; init; } = string.Empty;

    /// <summary>Fin de la suscripción. <c>null</c> en el tier interno (no expira).</summary>
    public DateTimeOffset? LicenseExpiresAt { get; init; }

    /// <summary>Expiración del token. Obliga a reconectar con el servidor.</summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }

    /// <summary>Días de gracia sin conexión que quedan. Solo aplica en <see cref="LicenseState.Grace"/>.</summary>
    public int GraceDaysRemaining { get; init; }

    /// <summary>Módulos habilitados para este tier.</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>Mensaje para el usuario. En estados de error explica qué hacer.</summary>
    public string Message { get; init; } = string.Empty;

    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>La aplicación puede usarse.</summary>
    public bool IsUsable => State is LicenseState.Valid or LicenseState.Grace;

    /// <summary>Días restantes de suscripción, o <c>null</c> si no expira.</summary>
    public int? DaysRemaining =>
        LicenseExpiresAt is null
            ? null
            : Math.Max(0, (int)Math.Ceiling((LicenseExpiresAt.Value - DateTimeOffset.UtcNow).TotalDays));

    public bool HasFeature(string feature) =>
        Features.Contains(feature, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Línea de estado que se pinta en el splash y en la barra inferior.
    /// Distinta por tier: el trabajador ve el nombre de la empresa, el cliente
    /// externo ve la vigencia de su suscripción.
    /// </summary>
    public string StatusLine => State switch
    {
        LicenseState.Grace =>
            $"Sin conexión — {GraceDaysRemaining} día(s) antes de requerir validación",

        LicenseState.Valid => Tier switch
        {
            // Sin nombre de empresa NO se pone el guion: quedaba un
            // "Licencia interna — " colgando.
            LicenseTier.Internal when string.IsNullOrWhiteSpace(Organization) =>
                "Licencia interna",
            LicenseTier.Internal => $"Licencia interna — {Organization}",
            LicenseTier.Commercial when LicenseExpiresAt is not null =>
                $"Suscripción activa hasta {LicenseExpiresAt.Value.ToLocalTime():dd/MM/yyyy}",
            LicenseTier.Commercial => "Suscripción activa",
            LicenseTier.Trial => $"Versión de prueba — {DaysRemaining ?? 0} día(s) restantes",
            _ => "Licencia válida"
        },

        LicenseState.NeedsActivation => "Se requiere activación",
        LicenseState.Expired => "Suscripción vencida",
        LicenseState.Revoked => "Equipo dado de baja",
        LicenseState.Tampered => "Validación en línea requerida",
        _ => "No se pudo verificar la licencia"
    };

    public static LicenseInfo Failure(LicenseState state, string message, string fingerprint = "") =>
        new()
        {
            State = state,
            Message = message,
            Fingerprint = fingerprint
        };
}
