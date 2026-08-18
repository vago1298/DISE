namespace CadLink.Licensing;

/// <summary>
/// Nivel de licencia asignado por el servidor. Un solo binario atiende a todos.
/// </summary>
public enum LicenseTier
{
    /// <summary>Sin determinar todavía.</summary>
    Unknown = 0,

    /// <summary>PC de un trabajador de la oficina. Gratis, sin fecha de fin.</summary>
    Internal = 1,

    /// <summary>Cliente externo con suscripción de paga.</summary>
    Commercial = 2,

    /// <summary>Prueba gratuita por tiempo limitado.</summary>
    Trial = 3
}

/// <summary>
/// Resultado de evaluar la licencia al arrancar.
/// </summary>
public enum LicenseState
{
    /// <summary>Licencia vigente. La aplicación arranca normal.</summary>
    Valid = 0,

    /// <summary>
    /// El token expiró pero seguimos dentro del periodo de gracia sin conexión.
    /// La aplicación funciona y muestra un aviso con los días restantes.
    /// </summary>
    Grace = 1,

    /// <summary>No hay licencia local. Hay que pasar por la pantalla de activación.</summary>
    NeedsActivation = 2,

    /// <summary>Suscripción o prueba terminada. Requiere pago.</summary>
    Expired = 3,

    /// <summary>El servidor dio de baja este equipo.</summary>
    Revoked = 4,

    /// <summary>
    /// El cache fue alterado, la firma no cuadra, o el reloj del sistema
    /// retrocedió. Se exige validación en línea.
    /// </summary>
    Tampered = 5,

    /// <summary>Error inesperado al evaluar. Se trata como bloqueante.</summary>
    Error = 6
}
