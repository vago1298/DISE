using System.Diagnostics;
using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// Localiza y carga la librería de interoperabilidad de AutoCAD, solo para obtener
/// el tipo <c>AcadEntity</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Para qué hace falta.</b> Todas las llamadas de AutoCAD que reciben un
/// <b>arreglo de entidades</b> —<c>AppendOuterLoop</c>, <c>AppendInnerLoop</c>,
/// <c>CopyObjects</c>, <c>MoveToTop</c>, <c>MoveToBottom</c>— fallaban con:
/// </para>
/// <code>COMException 0x8021007B: Invalid object array</code>
/// <para>
/// El motivo es el <b>tipo de elemento del SAFEARRAY</b>, no el contenido. En VBA,
/// <c>Dim v(0 To 0) As Object</c> produce un SAFEARRAY de <c>VT_DISPATCH</c>, que es
/// lo que AutoCAD acepta. Un <c>object[]</c> de .NET produce un SAFEARRAY de
/// <c>VT_VARIANT</c>, y AutoCAD lo rechaza. Envolver cada elemento en
/// <c>DispatchWrapper</c> <b>no arregla nada</b>, porque el tipo del arreglo sigue
/// siendo VARIANT: se probó y falla igual.
/// </para>
/// <para>
/// La única forma de que el marshaller genere un SAFEARRAY de <c>VT_DISPATCH</c> es
/// que el arreglo esté <b>tipado</b> con una interfaz COM. Ese tipo,
/// <c>AcadEntity</c>, vive en la interop de AutoCAD, así que se carga en tiempo de
/// ejecución, buscándola junto al AutoCAD que está corriendo. Así no se ata el
/// binario a una versión, igual que se hizo con la librería de ETABS.
/// </para>
/// </remarks>
public static class AcadInterop
{
    private static readonly string[] Nombres =
    {
        "Autodesk.AutoCAD.Interop.Common.dll",
        "Autodesk.AutoCAD.Interop.dll"
    };

    private static bool _intentado;
    private static Type? _tipoEntidad;

    /// <summary>Lo que se intentó al buscar la interop, para poder diagnosticar.</summary>
    public static List<string> Bitacora { get; } = new();

    /// <summary>
    /// Tipo <c>AcadEntity</c>, o <c>null</c> si no se pudo cargar la interop.
    /// </summary>
    public static Type? TipoEntidad
    {
        get
        {
            if (!_intentado)
            {
                _intentado = true;
                _tipoEntidad = Cargar();
            }

            return _tipoEntidad;
        }
    }

    /// <summary>
    /// Construye un arreglo <b>tipado</b> de entidades, que se marshaliza como
    /// SAFEARRAY de <c>VT_DISPATCH</c>.
    /// </summary>
    /// <returns><c>null</c> si no hay interop o si algún elemento no encaja.</returns>
    public static Array? ArregloTipado(IReadOnlyList<object> entidades)
    {
        var tipo = TipoEntidad;
        if (tipo is null || entidades.Count == 0)
        {
            return null;
        }

        try
        {
            var arr = Array.CreateInstance(tipo, entidades.Count);

            for (var i = 0; i < entidades.Count; i++)
            {
                // SetValue hace la conversión: sobre un objeto COM equivale a un
                // QueryInterface por AcadEntity. Si la entidad no lo implementa,
                // lanza y se usa otra estrategia.
                arr.SetValue(entidades[i], i);
            }

            return arr;
        }
        catch (Exception ex)
        {
            Bitacora.Add($"No se pudo armar el arreglo tipado: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static Type? Cargar()
    {
        foreach (var ruta in Candidatas())
        {
            try
            {
                if (!File.Exists(ruta))
                {
                    continue;
                }

                var asm = Assembly.LoadFrom(ruta);

                var tipo = asm.GetType("Autodesk.AutoCAD.Interop.Common.AcadEntity")
                           ?? asm.GetTypes().FirstOrDefault(t =>
                                  t.Name == "AcadEntity" && t.IsInterface);

                if (tipo is not null)
                {
                    Bitacora.Add($"Interop de AutoCAD cargada: {ruta}");
                    return tipo;
                }

                Bitacora.Add($"'{ruta}' no expone AcadEntity.");
            }
            catch (Exception ex)
            {
                Bitacora.Add($"No se pudo cargar '{ruta}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        Bitacora.Add(
            "No se encontró la interop de AutoCAD. Se seguirá intentando con " +
            "arreglos sin tipar, que en algunas versiones no funcionan.");

        return null;
    }

    private static IEnumerable<string> Candidatas()
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var carpetas = new List<string>();

        // 1) Junto al AutoCAD que está corriendo: la versión correcta, sin adivinar
        foreach (var carpeta in CarpetasDeAutoCad())
        {
            carpetas.Add(carpeta);
        }

        // 2) Carpeta compartida de Autodesk
        foreach (var v in new[]
                 {
                     Environment.SpecialFolder.CommonProgramFiles,
                     Environment.SpecialFolder.CommonProgramFilesX86
                 })
        {
            var raiz = Ruta(v);
            if (raiz is not null)
            {
                carpetas.Add(Path.Combine(raiz, "Autodesk Shared"));
            }
        }

        // 3) La carpeta de esta aplicación, por si se copiaron a mano
        var propia = CarpetaPropia();
        if (propia is not null)
        {
            carpetas.Add(propia);
        }

        foreach (var carpeta in carpetas)
        {
            foreach (var nombre in Nombres)
            {
                var p = Path.Combine(carpeta, nombre);
                if (vistas.Add(p))
                {
                    yield return p;
                }
            }
        }
    }

    private static string? Ruta(Environment.SpecialFolder f)
    {
        try
        {
            var p = Environment.GetFolderPath(f);
            return string.IsNullOrWhiteSpace(p) ? null : p;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? CarpetaPropia()
    {
        try
        {
            var d = AppContext.BaseDirectory;
            return string.IsNullOrWhiteSpace(d) ? null : d;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> CarpetasDeAutoCad()
    {
        var carpetas = new List<string>();

        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!p.ProcessName.Contains("acad", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var exe = p.MainModule?.FileName;
                    var dir = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);

                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        carpetas.Add(dir);
                    }
                }
                catch (Exception)
                {
                    // MainModule falla por permisos o por arquitectura distinta.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Sin lista de procesos se prueban las otras rutas.
        }

        return carpetas;
    }
}
