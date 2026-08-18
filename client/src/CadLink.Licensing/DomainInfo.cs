using System.Management;
using System.Security.Principal;

namespace CadLink.Licensing;

/// <summary>
/// Obtiene el SID del dominio de Active Directory al que está unido el equipo.
/// Es lo que permite que las PCs de la oficina se activen solas, sin que el
/// trabajador tenga que escribir ninguna clave.
/// </summary>
/// <remarks>
/// Se usa el SID y no el nombre del dominio porque el nombre es trivial de
/// falsificar: cualquiera levanta una máquina virtual con un dominio llamado
/// "MIEMPRESA.local". El SID se genera al crear el dominio y no es adivinable.
///
/// Aun así, el SID NO es una credencial criptográfica. Es un buen discriminante
/// para automatizar el alta, no una prueba de identidad infalsificable. El
/// control real lo tienes en la lista de equipos del panel de administración.
/// </remarks>
public static class DomainInfo
{
    /// <summary>
    /// SID del dominio, con formato S-1-5-21-x-y-z, o <c>null</c> si el equipo
    /// no está unido a un dominio o no se pudo determinar.
    /// </summary>
    public static string? GetDomainSid()
    {
        if (!IsDomainJoined())
        {
            return null;
        }

        // Vía 1: el SID del usuario actual. Si es un usuario de dominio, su
        // AccountDomainSid ES el SID del dominio. No requiere consultar al DC,
        // así que funciona incluso sin conectividad al controlador.
        var fromUser = FromCurrentUser();
        if (fromUser is not null)
        {
            return fromUser;
        }

        // Vía 2: traducir un grupo bien conocido del dominio. Cubre el caso de
        // una sesión iniciada con cuenta local en un equipo sí unido al dominio.
        return FromWellKnownGroup();
    }

    private static bool IsDomainJoined()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PartOfDomain FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get())
            {
                using var mo = (ManagementObject)item;
                if (mo["PartOfDomain"] is bool partOfDomain)
                {
                    return partOfDomain;
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        // Heurística de respaldo: en un equipo sin dominio, UserDomainName
        // coincide con el nombre de la máquina.
        return !string.Equals(
            Environment.UserDomainName,
            Environment.MachineName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? FromCurrentUser()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var domainSid = identity.User?.AccountDomainSid;
            if (domainSid is null)
            {
                return null;
            }

            // Si la sesión es de una cuenta local, AccountDomainSid devuelve el SID
            // de la máquina, no del dominio. Se descarta comparando con el nombre.
            var isLocalAccount = string.Equals(
                Environment.UserDomainName,
                Environment.MachineName,
                StringComparison.OrdinalIgnoreCase);

            return isLocalAccount ? null : domainSid.Value;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FromWellKnownGroup()
    {
        try
        {
            var domain = Environment.UserDomainName;
            if (string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // "Domain Users" existe en todo dominio de Active Directory.
            var account = new NTAccount(domain, "Domain Users");
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            return sid.AccountDomainSid?.Value;
        }
        catch (IdentityNotMappedException)
        {
            // Nombre de grupo no resuelto: dominio no alcanzable o localización
            // distinta del nombre del grupo.
            return null;
        }
        catch (SystemException)
        {
            // Translate() lanza SystemException ante fallos de resolución.
            return null;
        }
    }
}
