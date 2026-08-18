using System.Net.Http;

namespace CadLink.Licensing;

/// <summary>
/// Punto de entrada del licenciamiento. Orquesta cache, verificación y servidor.
/// </summary>
/// <remarks>
/// Toda la política de arranque vive aquí:
/// <list type="bullet">
///   <item>Con token vigente en cache, arranca sin tocar la red.</item>
///   <item>Si el token está por vencer, renueva en línea.</item>
///   <item>Sin conexión, respeta el periodo de gracia antes de bloquear.</item>
///   <item>Sin cache, intenta activación silenciosa (PCs del dominio).</item>
/// </list>
/// </remarks>
public sealed class LicenseService : IDisposable
{
    private readonly LicensingOptions _options;
    private readonly LicenseCache _cache;
    private readonly LicenseApiClient _api;

    public LicenseService(LicensingOptions? options = null, HttpMessageHandler? handler = null)
    {
        _options = options ?? new LicensingOptions();
        _cache = new LicenseCache(_options);
        _api = new LicenseApiClient(_options, handler);
    }

    /// <summary>Huella de este equipo, para mostrarla en la pantalla de activación.</summary>
    public string Fingerprint => MachineFingerprint.Value;

    /// <summary>Huella formateada en grupos, más fácil de dictar o copiar.</summary>
    public string FingerprintDisplay => MachineFingerprint.ToDisplayGroups(Fingerprint);

    /// <summary>
    /// Evalúa el estado de la licencia. Es lo que llama el splash al arrancar.
    /// Nunca lanza excepciones: todo error se traduce a un <see cref="LicenseInfo"/>.
    /// </summary>
    public async Task<LicenseInfo> EvaluateAsync(CancellationToken ct = default)
    {
        try
        {
            return await EvaluateCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return LicenseInfo.Failure(
                LicenseState.Error,
                "No se pudo verificar la licencia: " + ex.Message,
                Fingerprint);
        }
    }

    private async Task<LicenseInfo> EvaluateCoreAsync(CancellationToken ct)
    {
        var fingerprint = Fingerprint;
        var envelope = _cache.Load();

        // Primer arranque, o cache ilegible/borrado.
        if (envelope is null)
        {
            return await ActivateAsync(licenseKey: null, ct).ConfigureAwait(false);
        }

        LicenseClaims claims;
        try
        {
            claims = LicenseTokenVerifier.Verify(envelope.Token, fingerprint);
        }
        catch (LicenseVerificationException ex)
        {
            // Token alterado, firmado con otra llave, o de otro equipo.
            // No se confía en el cache: se exige validación en línea.
            return await ReactivateOrFailAsync(
                LicenseState.Tampered,
                "la licencia guardada en este equipo ya no es válida (" + ex.Message + ")",
                ct).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;

        // Reloj del sistema atrasado respecto a lo ya observado: intento de estirar
        // la gracia. Se ignora el cache y se obliga a validar contra el servidor.
        if (now < envelope.LastSeenUtc - _options.ClockSkewTolerance)
        {
            return await ReactivateOrFailAsync(
                LicenseState.Tampered,
                "el reloj del equipo está atrasado respecto al último uso registrado, " +
                "así que hay que corregir la fecha y validar en línea",
                ct).ConfigureAwait(false);
        }

        _cache.TouchLastSeen(envelope);

        // Suscripción vencida según el propio token. Puede que el cliente ya pagó,
        // así que vale la pena preguntarle al servidor antes de bloquear.
        if (claims.LicenseExpiresAt is not null && now > claims.LicenseExpiresAt)
        {
            return await RenewOrDegradeAsync(claims, now, ct).ConfigureAwait(false);
        }

        // Token cómodamente vigente: arranque sin red.
        var renewThreshold = claims.ExpiresAt - TimeSpan.FromDays(_options.RenewBeforeDays);
        if (now < renewThreshold)
        {
            return Build(claims, LicenseState.Valid);
        }

        // Token por vencer o ya vencido: hay que intentar renovar.
        return await RenewOrDegradeAsync(claims, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Intenta renovar en línea. Si no hay red, degrada al mejor estado posible
    /// según lo que el token en cache todavía permite.
    /// </summary>
    private async Task<LicenseInfo> RenewOrDegradeAsync(
        LicenseClaims claims, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var response = await _api.RenewAsync(ct).ConfigureAwait(false);
            var fresh = Persist(response.Token);
            return Build(fresh, LicenseState.Valid);
        }
        catch (LicenseServerException ex) when (ex.IsPaymentRequired)
        {
            return LicenseInfo.Failure(LicenseState.Expired, ex.Message, Fingerprint) with
            {
                Tier = claims.Tier,
                Organization = claims.Organization,
                LicenseExpiresAt = claims.LicenseExpiresAt
            };
        }
        catch (LicenseServerException ex) when (ex.IsForbidden)
        {
            // Equipo revocado: se borra el cache para que no siga arrancando.
            _cache.Clear();
            return LicenseInfo.Failure(LicenseState.Revoked, ex.Message, Fingerprint);
        }
        catch (LicenseServerException ex) when (ex.IsNotFound)
        {
            // El servidor no conoce este equipo (base restaurada, por ejemplo).
            return await ActivateAsync(licenseKey: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            return DegradeOffline(claims, now);
        }
    }

    /// <summary>
    /// Decide qué hacer sin conexión: seguir normal, entrar en gracia, o bloquear.
    /// </summary>
    private LicenseInfo DegradeOffline(LicenseClaims claims, DateTimeOffset now)
    {
        // La suscripción venció: la gracia no aplica a algo que ya no está pagado.
        if (claims.LicenseExpiresAt is not null && now > claims.LicenseExpiresAt)
        {
            return LicenseInfo.Failure(
                LicenseState.Expired,
                "La suscripción venció. Conéctate a internet después de renovar el pago.",
                Fingerprint) with { Tier = claims.Tier, LicenseExpiresAt = claims.LicenseExpiresAt };
        }

        // Token aún vigente: no pasa nada, se renovará en el siguiente arranque.
        if (now <= claims.ExpiresAt)
        {
            return Build(claims, LicenseState.Valid);
        }

        // Token vencido pero dentro del periodo de gracia sin conexión.
        var graceEnd = claims.ExpiresAt + TimeSpan.FromDays(claims.GraceDays);
        if (now <= graceEnd)
        {
            var remaining = Math.Max(0, (int)Math.Ceiling((graceEnd - now).TotalDays));
            return Build(claims, LicenseState.Grace) with { GraceDaysRemaining = remaining };
        }

        return LicenseInfo.Failure(
            LicenseState.NeedsActivation,
            "La licencia necesita validarse en línea. Conecta el equipo a internet y vuelve a abrir la aplicación.",
            Fingerprint) with { Tier = claims.Tier };
    }

    /// <summary>
    /// Activa este equipo. Con <paramref name="licenseKey"/> en <c>null</c> el
    /// servidor resuelve el tier solo: las PCs del dominio quedan INTERNAL sin
    /// que el trabajador tenga que escribir nada.
    /// </summary>
    public async Task<LicenseInfo> ActivateAsync(string? licenseKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _api.ActivateAsync(licenseKey, ct).ConfigureAwait(false);
            var claims = Persist(response.Token);
            return Build(claims, LicenseState.Valid);
        }
        catch (LicenseVerificationException ex)
        {
            // El servidor respondió, pero el token no verifica: llaves desalineadas
            // entre servidor y cliente. Es un error de despliegue, no del usuario.
            return LicenseInfo.Failure(
                LicenseState.Error,
                "El servidor emitió un token que este programa no puede verificar. " +
                "Avisa a soporte: posible desalineación de llaves. (" + ex.Message + ")",
                Fingerprint);
        }
        catch (LicenseServerException ex) when (ex.IsPaymentRequired)
        {
            return LicenseInfo.Failure(LicenseState.Expired, ex.Message, Fingerprint);
        }
        catch (LicenseServerException ex) when (ex.IsForbidden)
        {
            return LicenseInfo.Failure(LicenseState.Revoked, ex.Message, Fingerprint);
        }
        catch (LicenseServerException ex)
        {
            // 404 clave inexistente, 409 asientos agotados, 422 datos inválidos...
            return LicenseInfo.Failure(LicenseState.NeedsActivation, ex.Message, Fingerprint);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            return LicenseInfo.Failure(
                LicenseState.NeedsActivation,
                MensajeSinServidor(),
                Fingerprint);
        }
    }

    /// <summary>
    /// Explica que no se pudo contactar al servidor, con el consejo que
    /// corresponde según dónde esté.
    /// </summary>
    /// <remarks>
    /// Con el servidor en <c>localhost</c>, decir «revisa tu conexión a internet»
    /// manda al usuario a buscar en el lugar equivocado: no falta internet, falta
    /// encender el servidor. Se distinguen los dos casos.
    /// </remarks>
    private string MensajeSinServidor()
    {
        var url = _options.ServerUrl ?? string.Empty;

        var esLocal =
            url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("127.0.0.1", StringComparison.Ordinal) ||
            url.Contains("::1", StringComparison.Ordinal);

        if (esLocal)
        {
            return
                "No se pudo contactar al servidor de licencias en " + url + ". " +
                "El servidor está en este mismo equipo y parece apagado: " +
                "ejecuta 2-iniciar-servidor.bat, dejalo abierto, y vuelve a " +
                "intentar la activación.";
        }

        return
            "No se pudo contactar al servidor de licencias en " + url + ". " +
            "Verifica tu conexión a internet.";
    }

    /// <summary>
    /// El cache dejó de ser confiable: se intenta una activación en línea y, si no
    /// se puede, se explica por qué.
    /// </summary>
    /// <param name="motivo">
    /// Por qué se desconfía del cache, en minúsculas y sin punto final, para
    /// encajar en la frase que se le muestra al usuario.
    /// </param>
    /// <remarks>
    /// <b>Se informa el error de la activación, no el del cache.</b> Antes esta
    /// función devolvía siempre el mensaje del cache y eso resultaba engañoso: con
    /// el servidor de licencias apagado, la ventana decía «la firma del token no es
    /// válida», que suena a instalación corrupta o a llaves mal generadas, y
    /// escondía el único problema accionable, que era que el servidor no estaba
    /// encendido. El motivo del cache se conserva, pero como contexto.
    /// </remarks>
    private async Task<LicenseInfo> ReactivateOrFailAsync(
        LicenseState fallbackState, string motivo, CancellationToken ct)
    {
        var result = await ActivateAsync(licenseKey: null, ct).ConfigureAwait(false);

        if (result.IsUsable)
        {
            return result;
        }

        // La activación trae el motivo concreto y accionable: servidor apagado, sin
        // red, suscripción vencida, equipo dado de baja. Ese es el que se muestra.
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result with
            {
                Message = result.Message +
                          " Hay que reactivar en línea porque " + motivo + "."
            };
        }

        return LicenseInfo.Failure(
            fallbackState,
            "Hay que reactivar en línea porque " + motivo + ".",
            Fingerprint);
    }

    /// <summary>Verifica el token recibido y lo guarda solo si es legítimo.</summary>
    private LicenseClaims Persist(string token)
    {
        // Verificar ANTES de guardar: nunca se persiste un token no validado.
        var claims = LicenseTokenVerifier.Verify(token, Fingerprint);

        _cache.Save(new CacheEnvelope
        {
            Token = token,
            Fingerprint = Fingerprint,
            LastSeenUtc = DateTimeOffset.UtcNow
        });

        return claims;
    }

    private LicenseInfo Build(LicenseClaims claims, LicenseState state) => new()
    {
        State = state,
        Tier = claims.Tier,
        Organization = claims.Organization,
        LicenseExpiresAt = claims.LicenseExpiresAt,
        TokenExpiresAt = claims.ExpiresAt,
        Features = claims.Features,
        Fingerprint = Fingerprint
    };

    /// <summary>
    /// Distingue "no hay red" de un error de programación. Solo los fallos de red
    /// activan el periodo de gracia; un bug no debe abrir la puerta.
    /// </summary>
    private static bool IsNetworkFailure(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            or System.Net.Sockets.SocketException;

    /// <summary>Borra la licencia local. Útil para liberar un equipo antes de reasignarlo.</summary>
    public void Deactivate() => _cache.Clear();

    public void Dispose() => _api.Dispose();
}
