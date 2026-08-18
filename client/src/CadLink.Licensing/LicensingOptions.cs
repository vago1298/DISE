namespace CadLink.Licensing;

/// <summary>
/// Configuración del cliente de licenciamiento.
/// </summary>
public sealed class LicensingOptions
{
    /// <summary>
    /// URL base del servidor de licencias. DEBE ser HTTPS en producción: sin TLS
    /// cualquiera en la red puede interceptar y suplantar las respuestas.
    /// </summary>
    public string ServerUrl { get; init; } = "https://licencias.miempresa.com";

    /// <summary>Versión que se reporta al servidor, útil para saber qué usan tus clientes.</summary>
    public string AppVersion { get; init; } = "1.0.0";

    /// <summary>Nombre de la carpeta bajo %LOCALAPPDATA% donde vive el cache.</summary>
    public string AppFolderName { get; init; } = "CadLink";

    /// <summary>
    /// Cuántos días antes de que expire el token se intenta renovar en segundo plano.
    /// Un margen amplio evita que un equipo que se va a obra por dos semanas
    /// regrese con la licencia ya vencida.
    /// </summary>
    public int RenewBeforeDays { get; init; } = 5;

    /// <summary>
    /// Timeout de red. Corto a propósito: el arranque no puede quedarse colgado
    /// esperando a un servidor caído si ya hay un token válido en cache.
    /// </summary>
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tolerancia de desfase de reloj hacia atrás antes de considerarlo manipulación.
    /// Cubre ajustes legítimos de NTP y cambios de zona horaria.
    /// </summary>
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromHours(26);
}
