using System.Diagnostics;
using System.Reflection;

namespace CadLink.Etabs;

/// <summary>
/// Localiza y carga <c>ETABSv1.dll</c>, la librería de la API de ETABS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta esta librería.</b> El primer intento fue conectarse solo
/// por enlace tardío, como hace VBA con <c>CreateObject</c>, para no depender de
/// ninguna DLL y que un mismo binario sirviera para cualquier versión de ETABS.
/// <b>No funciona.</b> El diagnóstico en la máquina del usuario lo dejó claro: el
/// objeto de ETABS se obtiene bien, pero al pedirle <c>SapModel</c> por enlace
/// tardío falla siempre, por los tres mecanismos:
/// </para>
/// <code>
/// Objeto activo 'CSI.ETABS.API.ETABSObject': encontrado.
/// SapModel por IDispatch : InvalidCastException: Specified cast is not valid.
/// Helper 'ETABSv1.Helper': entregó el objeto de ETABS.
/// SapModel por IDispatch : MissingMethodException: .ctor
/// </code>
/// <para>
/// La razón es que la API de CSI es un <b>ensamblado .NET</b>, no un servidor COM
/// clásico: sus objetos no exponen <c>IDispatch</c> de forma utilizable, así que no
/// hay forma de recorrerlos a ciegas. Las macros de VBA que sí funcionan lo hacen
/// porque tienen una <b>referencia</b> a <c>ETABSv1.dll</c>, o sea enlace temprano.
/// </para>
/// <para>
/// <b>Cómo se conserva la independencia de versión.</b> En lugar de referenciar la
/// DLL al compilar, que ataría el binario a una versión concreta de ETABS, se carga
/// <b>en tiempo de ejecución</b>. Y se busca primero <b>junto al ETABS que está
/// corriendo</b>, con lo que la librería siempre corresponde exactamente a la
/// versión abierta, sea 19, 20, 21 o 22.
/// </para>
/// </remarks>
public static class EtabsAssembly
{
    /// <summary>
    /// La librería que hay que buscar, según el programa de CSI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto era el fallo de fondo al leer SAP2000.</b> El ProgID de SAP2000 se
    /// encontraba —la bitácora decía <c>Objeto activo 'CSI.SAP2000.API.SapObject':
    /// encontrado</c>— pero luego no se podía sacar el <c>SapModel</c>, y el motivo era
    /// que la librería cargada seguía siendo <c>ETABSv1.dll</c>. Los tipos con los que se
    /// hace el enlace temprano (<c>cOAPI</c>, <c>cSapModel</c>) salen de esa librería, y
    /// los de ETABS <b>no casan</b> con el objeto COM de SAP2000: de ahí los
    /// <c>Object does not match target type</c> y el delator
    /// <c>Object of type 'System.Int32' cannot be converted to type
    /// 'ETABSv1.eSlabTypeX'</c>.
    /// </para>
    /// <para>
    /// O sea que no basta cambiar el ProgID: hay que cargar <b>la librería del programa
    /// al que se habla</b>, y buscarla en SU carpeta de instalación.
    /// </para>
    /// </remarks>
    public static bool ParaSap2000 { get; set; }

    private static string NombreDll => ParaSap2000 ? "SAP2000v1.dll" : "ETABSv1.dll";

    /// <summary>Trozo del nombre de la carpeta de instalación que hay que buscar.</summary>
    private static string CarpetaClave => ParaSap2000 ? "sap2000" : "etabs";

    private static Assembly? _cargado;
    private static bool _cargadoParaSap;
    private static string _rutaCargada = string.Empty;

    /// <summary>Ruta indicada a mano en la configuración. Tiene prioridad.</summary>
    public static string? RutaConfigurada { get; set; }

    /// <summary>De dónde se cargó la librería. Vacío si no se ha cargado.</summary>
    public static string RutaCargada => _rutaCargada;

    /// <summary>Lo que se intentó al buscar la librería, para poder diagnosticar.</summary>
    public static List<string> Bitacora { get; } = new();

    /// <summary>
    /// Carga <c>ETABSv1.dll</c>, o devuelve <c>null</c> si no se encuentra.
    /// </summary>
    public static Assembly? Cargar()
    {
        // La cache se lleva TAMBIEN el programa: si no, leer ETABS y despues SAP2000 en
        // la misma sesion devolvia la libreria de ETABS la segunda vez, que es justo el
        // fallo que se estaba arreglando.
        if (_cargado is not null && _cargadoParaSap == ParaSap2000)
        {
            return _cargado;
        }

        _cargado = null;
        _rutaCargada = string.Empty;

        foreach (var ruta in Candidatas())
        {
            try
            {
                if (!File.Exists(ruta))
                {
                    continue;
                }

                _cargado = Assembly.LoadFrom(ruta);
                _cargadoParaSap = ParaSap2000;
                _rutaCargada = ruta;
                Bitacora.Add(
                    $"Librería de {(ParaSap2000 ? "SAP2000" : "ETABS")} cargada: {ruta}");
                return _cargado;
            }
            catch (Exception ex)
            {
                Bitacora.Add($"No se pudo cargar '{ruta}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Tipos del ensamblado que declaran el miembro indicado, interfaces primero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sirve para llegar a <c>SapModel</c> cuando el objeto de ETABS llega envuelto
    /// como <c>System.__ComObject</c>. Ese envoltorio no declara nada, así que la
    /// propiedad hay que pedirla a la <b>interfaz</b> que la declara y dejar que el
    /// runtime resuelva el <c>QueryInterface</c>.
    /// </para>
    /// <para>
    /// Se busca por <b>miembro</b> y no por el nombre <c>cOAPI</c>: así, si CSI
    /// renombra o reorganiza sus interfaces en una versión futura, esto sigue
    /// encontrando el camino en lugar de romperse.
    /// </para>
    /// <para>
    /// Las interfaces van primero porque son las que el envoltorio COM puede
    /// atender; una clase concreta del ensamblado no le sirve de nada.
    /// </para>
    /// </remarks>
    public static List<Type> TiposQueDeclaran(string miembro)
    {
        var encontrados = new List<Type>();

        var asm = _cargado;
        if (asm is null)
        {
            return encontrados;
        }

        Type[] tipos;
        try
        {
            tipos = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Un ensamblado al que le falta alguna dependencia entrega los tipos que
            // sí cargaron. Se usan esos en lugar de rendirse: lo que hace falta
            // (cOAPI) casi siempre está entre ellos.
            tipos = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            Bitacora.Add(
                $"Librería: algunos tipos no cargaron; se usan los {tipos.Length} que sí.");
        }
        catch (Exception ex)
        {
            Bitacora.Add("Librería: no se pudieron leer sus tipos: " + ex.Message);
            return encontrados;
        }

        foreach (var t in tipos)
        {
            try
            {
                if (t.GetProperty(miembro) is not null || t.GetMethod(miembro) is not null)
                {
                    encontrados.Add(t);
                }
            }
            catch (Exception)
            {
                // Un tipo que no se puede reflejar simplemente no entra a la lista.
            }
        }

        // Las interfaces primero: son las únicas que el envoltorio COM puede atender.
        return encontrados
            .OrderByDescending(t => t.IsInterface)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Rutas donde buscar, de la más fiable a la menos.</summary>
    private static IEnumerable<string> Candidatas()
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? Unica(string? p)
        {
            if (string.IsNullOrWhiteSpace(p) || !vistas.Add(p))
            {
                return null;
            }

            return p;
        }

        // 1) Ruta puesta a mano en cadlink.config.json
        if (!string.IsNullOrWhiteSpace(RutaConfigurada))
        {
            var p = RutaConfigurada.Trim();

            // Se admite tanto la carpeta como el archivo
            if (Directory.Exists(p))
            {
                p = Path.Combine(p, NombreDll);
            }

            var u = Unica(p);
            if (u is not null)
            {
                Bitacora.Add($"Ruta de la configuración: {u}");
                yield return u;
            }
        }

        // 2) Junto al ETABS que está corriendo. Es la mejor: la librería
        //    corresponde EXACTAMENTE a la versión que el usuario tiene abierta.
        foreach (var carpeta in CarpetasDeProcesosEtabs())
        {
            var u = Unica(Path.Combine(carpeta, NombreDll));
            if (u is not null)
            {
                Bitacora.Add($"Carpeta del ETABS en ejecución: {carpeta}");
                yield return u;
            }
        }

        // 3) Junto a esta aplicación, por si se copió la DLL a mano.
        //
        // El 'yield return' NO puede ir dentro de un try que tenga catch (CS1626),
        // así que la parte que puede fallar se resuelve aparte y aquí solo se
        // entrega el resultado.
        var propia = CarpetaPropia();
        if (propia is not null)
        {
            var u = Unica(Path.Combine(propia, NombreDll));
            if (u is not null)
            {
                yield return u;
            }
        }

        // 4) Instalaciones típicas de CSI, de la versión más nueva a la más vieja
        foreach (var p in InstalacionesCsi())
        {
            var u = Unica(p);
            if (u is not null)
            {
                yield return u;
            }
        }
    }

    /// <summary>Carpeta de esta aplicación, o <c>null</c> si no se puede resolver.</summary>
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

    /// <summary>Carpetas de los procesos de ETABS que están corriendo.</summary>
    private static List<string> CarpetasDeProcesosEtabs()
    {
        var carpetas = new List<string>();

        Process[] procesos;
        try
        {
            procesos = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Bitacora.Add("No se pudo listar los procesos: " + ex.Message);
            return carpetas;
        }

        foreach (var p in procesos)
        {
            try
            {
                if (!p.ProcessName.Contains("etabs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // MainModule falla si el proceso es de otra arquitectura o si no
                // hay permisos. En ese caso simplemente se prueban otras rutas.
                var exe = p.MainModule?.FileName;
                var dir = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);

                if (!string.IsNullOrWhiteSpace(dir))
                {
                    carpetas.Add(dir);
                }
            }
            catch (Exception ex)
            {
                Bitacora.Add(
                    $"Proceso '{Nombre(p)}': no se pudo leer su ubicación ({ex.GetType().Name}). " +
                    "Suele ser por permisos: prueba abrir ETABS y esta aplicación igual, " +
                    "los dos como administrador o los dos normales.");
            }
            finally
            {
                p.Dispose();
            }
        }

        return carpetas;

        static string Nombre(Process p)
        {
            try
            {
                return p.ProcessName;
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }

    /// <summary>Carpetas de instalación de CSI, ordenadas de más nueva a más vieja.</summary>
    private static IEnumerable<string> InstalacionesCsi()
    {
        var raices = new List<string>();

        foreach (var v in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            try
            {
                var raiz = Environment.GetFolderPath(v);
                if (!string.IsNullOrWhiteSpace(raiz))
                {
                    raices.Add(Path.Combine(raiz, "Computers and Structures"));
                }
            }
            catch (Exception)
            {
                // Si no se puede resolver la carpeta, se omite.
            }
        }

        foreach (var raiz in raices)
        {
            List<string> subcarpetas;
            try
            {
                if (!Directory.Exists(raiz))
                {
                    continue;
                }

                // Orden descendente: 'ETABS 22' antes que 'ETABS 19'
                subcarpetas = Directory
                    .GetDirectories(raiz)
                    .Where(d => Path.GetFileName(d)
                        .Contains(CarpetaClave, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var d in subcarpetas)
            {
                yield return Path.Combine(d, NombreDll);
            }
        }
    }

    /// <summary>Mensaje para el usuario cuando no se encuentra la librería.</summary>
    public static string MensajeNoEncontrada() =>
        "No encontré la librería de la API de " +
        (ParaSap2000 ? "SAP2000" : "ETABS") + " (" + NombreDll + ").\n\n" +
        "Es la misma que tu macro de Excel tiene como referencia. Sin ella no se\n" +
        "puede leer el modelo: la API de ETABS no se deja usar a ciegas.\n\n" +
        "Busqué:\n" +
        "  - Junto al ETABS que está abierto ahora.\n" +
        "  - En la carpeta de esta aplicación.\n" +
        "  - En Program Files\\Computers and Structures\\ETABS ...\n\n" +
        "Solución: abre ETABS antes de conectar. Si aun así falla, busca\n" +
        NombreDll + " en la carpeta donde instalaste ETABS y escribe esa ruta\n" +
        "en cadlink.config.json, en la clave \"rutaLibreriaEtabs\".\n\n" +
        "Detalle:\n" + string.Join(Environment.NewLine, Bitacora);
}
