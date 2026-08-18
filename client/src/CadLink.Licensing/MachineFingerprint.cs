using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace CadLink.Licensing;

/// <summary>
/// Calcula un identificador estable del equipo a partir de su hardware.
/// </summary>
/// <remarks>
/// Se combinan varios componentes y se ignoran los que no estén disponibles:
/// muchas máquinas virtuales y equipos OEM no reportan número de serie de placa
/// base. Con al menos dos componentes presentes la huella es suficientemente única.
///
/// Si el usuario cambia hardware mayor (placa base o disco de sistema) la huella
/// cambia y el equipo debe reactivarse. Es poco frecuente y aceptable, pero el
/// flujo de reactivación debe ser fácil para no molestar a tus propios trabajadores.
/// </remarks>
public static class MachineFingerprint
{
    private static readonly Lazy<string> _cached = new(ComputeCore, isThreadSafe: true);

    /// <summary>
    /// Huella del equipo: SHA-256 en hexadecimal minúsculas (64 caracteres).
    /// El cálculo consulta WMI, así que se memoriza para no repetirlo.
    /// </summary>
    public static string Value => _cached.Value;

    /// <summary>
    /// Versión legible para mostrar al usuario en la pantalla de activación,
    /// de modo que pueda dictártela o copiarla sin equivocarse.
    /// </summary>
    public static string ToDisplayGroups(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(fingerprint.Length + fingerprint.Length / 4);
        for (var i = 0; i < fingerprint.Length; i++)
        {
            if (i > 0 && i % 8 == 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToUpperInvariant(fingerprint[i]));
        }

        return sb.ToString();
    }

    private static string ComputeCore()
    {
        var parts = new List<string>(4);

        Add(parts, "MG", ReadMachineGuid());
        Add(parts, "BB", QueryWmi("Win32_BaseBoard", "SerialNumber"));
        Add(parts, "CPU", QueryWmi("Win32_Processor", "ProcessorId"));
        Add(parts, "HD", ReadSystemDiskSerial());

        if (parts.Count == 0)
        {
            // Último recurso: no debería ocurrir en Windows real, pero si ocurre
            // es mejor una huella débil que una excepción al arrancar.
            parts.Add("FALLBACK:" + Environment.MachineName);
        }

        var material = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Add(List<string> parts, string label, string? value)
    {
        var cleaned = Sanitize(value);
        if (cleaned is not null)
        {
            parts.Add($"{label}:{cleaned}");
        }
    }

    /// <summary>
    /// Descarta valores basura que los fabricantes dejan en el firmware. Si no se
    /// filtran, miles de equipos distintos producirían la misma huella.
    /// </summary>
    private static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();

        // Algunos BIOS rellenan con ceros, espacios o literales de relleno.
        var junk = new[]
        {
            "to be filled by o.e.m.",
            "to be filled by oem",
            "default string",
            "none",
            "n/a",
            "not applicable",
            "not specified",
            "system serial number",
            "0",
            "00000000",
            "filled by oem",
            "invalid"
        };

        if (junk.Contains(value.ToLowerInvariant()))
        {
            return null;
        }

        // Cadenas de un solo carácter repetido (000000, ffffff, ......) no aportan nada.
        if (value.Length > 1 && value.Distinct().Count() == 1)
        {
            return null;
        }

        return value;
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            // Registry64 explícito: en un proceso de 32 bits la vista por defecto
            // se redirige a Wow6432Node y la clave no aparece.
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? QueryWmi(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT {property} FROM {wmiClass}");
            foreach (var item in searcher.Get())
            {
                using var mo = (ManagementObject)item;
                var value = mo[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (ManagementException)
        {
            // WMI puede estar deshabilitado o corrupto. Seguimos con los demás componentes.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>
    /// Serial del disco físico que contiene el directorio de Windows.
    /// Tomar "el disco 0" a ciegas es frágil en equipos con varios discos, porque
    /// el orden puede cambiar al conectar una unidad externa.
    /// </summary>
    private static string? ReadSystemDiskSerial()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
            if (string.IsNullOrEmpty(systemDrive))
            {
                return null;
            }

            // Partición que corresponde a la letra del sistema
            using var partSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} " +
                "WHERE AssocClass = Win32_LogicalDiskToPartition");

            foreach (var partition in partSearcher.Get())
            {
                using var part = (ManagementObject)partition;
                var partitionId = part["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(partitionId))
                {
                    continue;
                }

                // Disco físico que contiene esa partición
                using var diskSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} " +
                    "WHERE AssocClass = Win32_DiskDriveToDiskPartition");

                foreach (var disk in diskSearcher.Get())
                {
                    using var d = (ManagementObject)disk;
                    var serial = d["SerialNumber"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial))
                    {
                        return serial;
                    }
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
