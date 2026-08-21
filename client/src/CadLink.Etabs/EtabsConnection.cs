using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CadLink.Etabs;

/// <summary>
/// Conexión a la instancia de ETABS que ya está abierta.
/// </summary>
/// <remarks>
/// <para>
/// Equivale al <c>ConnectToETABS</c> de la macro, pero probando <b>dos vías</b>,
/// porque la que funciona depende de la versión de ETABS:
/// </para>
/// <list type="number">
///   <item>
///     <b>Objeto activo por ProgID</b>, el equivalente exacto de
///     <c>GetObject(, "CSI.ETABS.API.ETABSObject")</c> de VBA.
///   </item>
///   <item>
///     <b>A través del Helper</b>, que es la forma que CSI documenta para las
///     versiones recientes:
///     <c>helper.GetObject("CSI.ETABS.API.ETABSObject")</c>.
///   </item>
/// </list>
/// <para>
/// A propósito <b>no</b> se lanza ETABS si no está abierto: arrancarlo consume una
/// licencia y tarda mucho.
/// </para>
/// </remarks>
public sealed class EtabsConnection : IDisposable
{
    private const int UnidadesKnMC = 6;

    /// <summary>
    /// A qué programa de CSI se conecta.
    /// </summary>
    /// <remarks>
    /// <b>El lector es el mismo para los dos.</b> CSI comparte la OAPI entre ETABS y
    /// SAP2000: la misma interfaz <c>cOAPI</c>, el mismo <c>SapModel</c> y las mismas
    /// llamadas para pisos, marcos y áreas. Lo único que cambia de verdad es el
    /// <b>ProgID</b> con el que se pide el objeto activo, así que no hace falta un lector
    /// aparte: basta decirle a la conexión a quién buscar.
    /// </remarks>
    /// <remarks>
    /// Se llama <c>ProgramaCsi</c> y no <c>Programa</c> porque ya hay una propiedad
    /// <see cref="Programa"/> que guarda el nombre y la versión que reporta el programa
    /// una vez conectado. Son dos cosas distintas: esto es a quién se BUSCA, y aquello es
    /// qué se ENCONTRÓ.
    /// </remarks>
    public enum ProgramaCsi
    {
        /// <summary>ETABS.</summary>
        Etabs,

        /// <summary>SAP2000.</summary>
        Sap2000
    }

    /// <summary>El programa al que se conecta esta instancia.</summary>
    public ProgramaCsi Destino { get; init; } = ProgramaCsi.Etabs;

    /// <summary>Nombre para los mensajes y la bitácora.</summary>
    public string NombreDelDestino => Destino == ProgramaCsi.Sap2000 ? "SAP2000" : "ETABS";

    /// <summary>ProgID del objeto de aplicación, según el destino.</summary>
    /// <remarks>
    /// Son los dos ProgID que registra el instalador de CSI. El de SAP2000 se llama
    /// <c>SapObject</c> y no <c>SAP2000Object</c>, que es el error tipico al escribirlo
    /// de memoria.
    /// </remarks>
    private string ProgIdApp => Destino == ProgramaCsi.Sap2000
        ? "CSI.SAP2000.API.SapObject"
        : "CSI.ETABS.API.ETABSObject";

    /// <summary>ProgIDs de Helper conocidos, del más nuevo al más viejo.</summary>
    /// <remarks>
    /// Cada programa tiene los suyos, y se prueban en ese orden porque el nombre cambió
    /// entre versiones. Si el Helper no aparece, la conexión sigue por el camino del
    /// objeto activo, que no lo necesita.
    /// </remarks>
    /// <summary>
    /// Prefijo de los tipos del ensamblado de la API.
    /// </summary>
    /// <remarks>
    /// <b>Esto era el fallo grande.</b> Los tipos de la interop llevan el nombre del
    /// programa en su espacio de nombres: <c>ETABSv1.Helper</c> en la librería de ETABS y
    /// <c>SAP2000v1.Helper</c> en la de SAP2000. Al pedir el tipo por su nombre en duro,
    /// en la librería de SAP2000 devolvía <c>null</c>, la vía del Helper se caía y todo
    /// terminaba en el camino de respaldo, que fallaba con «Object does not match target
    /// type». Y como el mensaje se armaba con la palabra ETABS, parecía que la lectura se
    /// hubiera ido a ETABS cuando lo que pasaba es que se buscaba un tipo que no existe.
    /// </remarks>
    private string PrefijoTipos => Destino == ProgramaCsi.Sap2000 ? "SAP2000v1" : "ETABSv1";

    private string[] ProgIdsHelper => Destino == ProgramaCsi.Sap2000
        ? new[] { "SAP2000v1.Helper", "CSI.SAP2000.API.Helper", "SAP2000v20.Helper" }
        : new[] { "ETABSv1.Helper", "CSI.ETABS.API.Helper", "ETABS2016.Helper" };

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid lpclsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid, IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    private object? _etabs;
    private object? _sapModel;
    private int? _unidadesOriginales;
    private readonly List<string> _bitacora = new();

    /// <summary>
    /// La interfaz <c>cOAPI</c> del ensamblado, tomada del tipo de retorno de
    /// <c>GetObject</c>. Es la vía buena para llegar a <c>SapModel</c>.
    /// </summary>
    /// <remarks>
    /// <b>Aquí estaba el error de fondo.</b> El objeto que entrega ETABS llega a
    /// .NET como <c>System.__ComObject</c>, un envoltorio opaco: pedirle
    /// <c>GetType().GetProperty("SapModel")</c> devuelve <c>null</c> siempre, porque
    /// el tipo en tiempo de ejecución no declara nada. La propiedad hay que pedirla
    /// a la <b>interfaz que la declara</b>, y entonces el runtime hace por su cuenta
    /// el <c>QueryInterface</c> hacia ella. Sin esta pieza el diagnóstico repetía
    /// «el tipo no expone la propiedad» sin que hubiera nada roto en ETABS.
    /// </remarks>
    private Type? _tipoOapi;

    public object SapModel =>
        _sapModel ?? throw new EtabsException($"No hay conexión con {NombreDelDestino}.");

    public string Programa { get; private set; } = string.Empty;

    public string Modelo { get; private set; } = string.Empty;

    /// <summary>
    /// Qué se intentó y qué pasó en cada intento. Se muestra al usuario cuando la
    /// conexión falla, para no dejarlo adivinando.
    /// </summary>
    public string Diagnostico => string.Join(Environment.NewLine, _bitacora);

    /// <summary>
    /// Se conecta a ETABS probando cada vía hasta que una entregue un
    /// <c>SapModel</c> utilizable.
    /// </summary>
    /// <remarks>
    /// <b>La prueba de que una vía sirve es que entregue el SapModel</b>, no que
    /// devuelva un objeto.
    /// <para>
    /// Antes esto era <c>PorObjetoActivo() ?? PorHelper()</c>, y ahí estaba el
    /// error: la vía del objeto activo <i>sí</i> devuelve un objeto, así que la del
    /// Helper nunca llegaba a probarse. Cuando ese objeto fallaba al pedirle el
    /// SapModel con <c>InvalidCastException</c>, se abandonaba todo teniendo
    /// todavía sin usar la vía que CSI documenta, que es precisamente la del
    /// Helper. Ahora cada candidato se valida y solo se falla si fallan todos.
    /// </para>
    /// </remarks>
    public void Conectar()
    {
        // ANTES de cargar la libreria: la de ETABS y la de SAP2000 son distintas, y los
        // tipos del enlace temprano salen de ella. Sin esto se encontraba el objeto de
        // SAP2000 pero se intentaba castear con las interfaces de ETABS, y fallaba con
        // «Object does not match target type». Ver EtabsAssembly.ParaSap2000.
        EtabsAssembly.ParaSap2000 = Destino == ProgramaCsi.Sap2000;

        _bitacora.Clear();

        var vias = new (string Nombre, Func<object?> Obtener)[]
        {
            // La librería va PRIMERO: es la única vía que de verdad funciona.
            // Las otras dos se conservan como respaldo por si en alguna versión
            // de ETABS el enlace tardío sí alcanza.
            ("Librería ETABSv1.dll", PorEnsamblado),
            ("Helper de CSI", PorHelper),
            ("Objeto activo por ProgID", PorObjetoActivo)
        };

        foreach (var (nombre, obtener) in vias)
        {
            var candidato = obtener();
            if (candidato is null)
            {
                continue;
            }

            var sap = IntentarSapModel(candidato);
            if (sap is not null)
            {
                _etabs = candidato;
                _sapModel = sap;
                _bitacora.Add($"CONECTADO por: {nombre}.");
                break;
            }

            // Este candidato no sirve. Se suelta antes de probar el siguiente,
            // para no dejar referencias COM colgando.
            if (Marshal.IsComObject(candidato))
            {
                try
                {
                    Marshal.FinalReleaseComObject(candidato);
                }
                catch (Exception)
                {
                    // Soltar la referencia es limpieza; si falla no cambia nada.
                }
            }
        }

        if (_sapModel is null)
        {
            // Si ni siquiera se encontró la librería, ese es el problema de fondo y
            // hay que decirlo así, en lugar de mandar a revisar ETABS.
            if (string.IsNullOrEmpty(EtabsAssembly.RutaCargada))
            {
                throw new EtabsException(EtabsAssembly.MensajeNoEncontrada());
            }

            throw new EtabsException(
                $"No pude obtener el modelo de {NombreDelDestino}.\n\n" +
                "La librería de la API sí se cargó:\n  " + EtabsAssembly.RutaCargada + "\n\n" +
                "Revisa que:\n" +
                $"  1. {NombreDelDestino} esté abierto, con un modelo cargado.\n" +
                $"  2. {NombreDelDestino} no tenga ningún cuadro de diálogo esperando " +
                "respuesta.\n" +
                $"  3. {NombreDelDestino} y esta aplicación corran igual: si " +
                $"{NombreDelDestino} está como\n" +
                "     administrador y esta aplicación no, o al revés, no se ven\n" +
                "     entre sí. Ciérralos y abre los dos del mismo modo.\n\n" +
                "Detalle de cada intento:\n" + Diagnostico);
        }

        LeerInfo();
        FijarUnidades();
    }

    /// <summary>
    /// Pide el <c>SapModel</c> a un candidato, probando los tres mecanismos de
    /// enlace tardío. Devuelve <c>null</c> si ninguno funciona.
    /// </summary>
    /// <remarks>
    /// Hacen falta tres porque <c>InvalidCastException: Specified cast is not
    /// valid</c> al pedir <c>SapModel</c> es justo lo que ocurre cuando el objeto
    /// que entrega la ROT no se puede usar por <c>IDispatch</c>: la API de ETABS es
    /// un ensamblado .NET expuesto por COM, y según cómo quede el envoltorio, la
    /// llamada hay que hacerla por reflexión sobre el tipo real o sobre la interfaz
    /// que declara la propiedad, no por <c>IDispatch</c>.
    /// </remarks>
    private object? IntentarSapModel(object candidato)
    {
        // El tipo REAL del candidato se anota siempre. Es el dato que faltaba para
        // entender el diagnóstico: si aquí sale 'System.__ComObject', ya se sabe que
        // ninguna vía basada en GetType() puede funcionar, y no hay que seguir
        // buscando el problema en ETABS.
        _bitacora.Add($"Objeto de {NombreDelDestino}: tipo en ejecución '{candidato.GetType().FullName}'.");

        // 1) LA VIA BUENA: la interfaz cOAPI que declara la propiedad, sacada del
        //    ensamblado. Va primero porque es la única que funciona con el
        //    envoltorio COM, que es el caso normal.
        foreach (var tipo in TiposQueDeclaranSapModel())
        {
            try
            {
                var prop = tipo.GetProperty("SapModel");
                if (prop is null)
                {
                    continue;
                }

                var sap = prop.GetValue(candidato);
                if (sap is not null)
                {
                    _bitacora.Add($"SapModel: obtenido por la interfaz '{tipo.FullName}'.");
                    return sap;
                }

                _bitacora.Add($"SapModel por '{tipo.Name}': devolvió vacío.");
            }
            catch (Exception ex)
            {
                _bitacora.Add($"SapModel por '{tipo.Name}': " + Detalle(ex));
            }
        }

        // 2) IDispatch, que es lo que hace VBA
        try
        {
            var sap = Com.Get(candidato, "SapModel");
            if (sap is not null)
            {
                _bitacora.Add("SapModel: obtenido por IDispatch.");
                return sap;
            }
        }
        catch (Exception ex)
        {
            _bitacora.Add("SapModel por IDispatch: " + Detalle(ex));
        }

        // 3) Reflexión sobre el tipo real del envoltorio
        try
        {
            var prop = candidato.GetType().GetProperty("SapModel");
            var sap = prop?.GetValue(candidato);
            if (sap is not null)
            {
                _bitacora.Add("SapModel: obtenido por reflexión sobre el tipo.");
                return sap;
            }

            _bitacora.Add("SapModel por reflexión: el tipo no expone la propiedad.");
        }
        catch (Exception ex)
        {
            _bitacora.Add("SapModel por reflexión: " + Detalle(ex));
        }

        // 4) Reflexión sobre las interfaces que el propio tipo declara implementar
        foreach (var iface in candidato.GetType().GetInterfaces())
        {
            try
            {
                var prop = iface.GetProperty("SapModel");
                if (prop is null)
                {
                    continue;
                }

                var sap = prop.GetValue(candidato);
                if (sap is not null)
                {
                    _bitacora.Add($"SapModel: obtenido por la interfaz '{iface.Name}'.");
                    return sap;
                }
            }
            catch (Exception ex)
            {
                _bitacora.Add($"SapModel por la interfaz '{iface.Name}': " + Detalle(ex));
            }
        }

        return null;
    }

    /// <summary>
    /// Tipos del ensamblado de ETABS que declaran <c>SapModel</c>, el más probable
    /// primero.
    /// </summary>
    /// <remarks>
    /// Primero el tipo de retorno de <c>GetObject</c>, que es el dato más fiable que
    /// existe: el propio ensamblado dice qué interfaz devuelve. Después, por si esa
    /// vía no se recorrió, cualquier tipo del ensamblado que declare la propiedad.
    /// Se busca <b>por miembro y no por nombre de tipo</b> a propósito: si CSI
    /// renombra <c>cOAPI</c> en una versión futura, esto sigue funcionando.
    /// </remarks>
    private IEnumerable<Type> TiposQueDeclaranSapModel()
    {
        var vistos = new HashSet<Type>();

        if (_tipoOapi is not null && vistos.Add(_tipoOapi))
        {
            yield return _tipoOapi;
        }

        foreach (var t in EtabsAssembly.TiposQueDeclaran("SapModel"))
        {
            if (vistos.Add(t))
            {
                yield return t;
            }
        }
    }

    /// <summary>
    /// Busca un método por nombre en el tipo <b>y en sus interfaces</b>.
    /// </summary>
    /// <remarks>
    /// <b>Por esto fallaba la vía de la librería con «el Helper no tiene
    /// GetObject(string)».</b> <c>Helper</c> implementa <c>cHelper</c>, y cuando una
    /// clase implementa una interfaz de forma <b>explícita</b>, sus métodos no
    /// aparecen al preguntárselos a la clase: hay que preguntárselos a la interfaz.
    /// El método sí existía; se estaba mirando en el sitio equivocado.
    /// <para>
    /// La firma exacta se prueba primero y el nombre después, porque una versión
    /// puede declarar el parámetro de otro modo y aquí lo que importa es encontrar
    /// el método, no validar su firma.
    /// </para>
    /// </remarks>
    private MethodInfo? MetodoDe(Type tipo, string nombre, params Type[] firma)
    {
        MethodInfo? PorNombre(Type t)
        {
            try
            {
                return t.GetMethod(nombre, firma)
                       ?? t.GetMethods().FirstOrDefault(m =>
                              m.Name == nombre &&
                              m.GetParameters().Length == firma.Length);
            }
            catch (Exception)
            {
                // Un tipo que no se puede reflejar simplemente no aporta.
                return null;
            }
        }

        var encontrado = PorNombre(tipo);
        if (encontrado is not null)
        {
            return encontrado;
        }

        foreach (var iface in tipo.GetInterfaces())
        {
            encontrado = PorNombre(iface);
            if (encontrado is not null)
            {
                _bitacora.Add(
                    $"Librería: '{nombre}' se encontró en la interfaz " +
                    $"'{iface.Name}', no en la clase '{tipo.Name}'.");
                return encontrado;
            }
        }

        return null;
    }

    // ==================================================================
    // Estrategias de conexión
    // ==================================================================

    /// <summary>
    /// Vía buena: se carga <c>ETABSv1.dll</c> y se usa su <c>Helper</c>.
    /// </summary>
    /// <remarks>
    /// Al cargar el ensamblado, los objetos que devuelve son tipos .NET de verdad
    /// y la reflexión sobre ellos funciona con normalidad. Es el equivalente de la
    /// <i>referencia</i> a ETABSv1.dll que tiene la macro de VBA, pero resuelta en
    /// tiempo de ejecución, para no atar el binario a una versión de ETABS.
    /// </remarks>
    private object? PorEnsamblado()
    {
        var asm = EtabsAssembly.Cargar();

        // La bitácora de la búsqueda se incorpora a la de la conexión
        foreach (var linea in EtabsAssembly.Bitacora)
        {
            _bitacora.Add(linea);
        }

        if (asm is null)
        {
            _bitacora.Add($"Librería de {NombreDelDestino}: no se encontró.");
            return null;
        }

        Type? tipoHelper;
        try
        {
            tipoHelper = asm.GetType(PrefijoTipos + ".Helper")
                         ?? asm.GetTypes().FirstOrDefault(t =>
                                t.Name == "Helper" && !t.IsInterface);
        }
        catch (Exception ex)
        {
            _bitacora.Add("Librería: no se pudieron leer sus tipos: " + Detalle(ex));
            return null;
        }

        if (tipoHelper is null)
        {
            _bitacora.Add("Librería: no tiene la clase 'Helper'. ¿Es de otro programa?");
            return null;
        }

        object? helper;
        try
        {
            helper = Activator.CreateInstance(tipoHelper);
        }
        catch (Exception ex)
        {
            _bitacora.Add("Librería: no se pudo crear el Helper: " + Detalle(ex));
            return null;
        }

        if (helper is null)
        {
            _bitacora.Add("Librería: el Helper salió vacío.");
            return null;
        }

        // GetObject(progId): se engancha a la instancia que ya está abierta
        try
        {
            var m = MetodoDe(tipoHelper, "GetObject", typeof(string));
            if (m is not null)
            {
                // El tipo de RETORNO es la interfaz cOAPI. Se guarda porque es la
                // única forma fiable de llegar después a SapModel.
                _tipoOapi = m.ReturnType;
                _bitacora.Add($"Librería: GetObject devuelve '{m.ReturnType.FullName}'.");

                var obj = m.Invoke(helper, new object?[] { ProgIdApp });
                if (obj is not null)
                {
                    _bitacora.Add($"Librería: GetObject entregó el objeto de {NombreDelDestino}.");
                    return obj;
                }

                _bitacora.Add("Librería: GetObject devolvió vacío.");
            }
            else
            {
                // Si no se encuentra, se LISTA lo que el Helper sí tiene. Sin esto,
                // el mensaje anterior («no tiene GetObject») dejaba el problema sin
                // salida: no había modo de saber cómo se llama el método de verdad.
                _bitacora.Add(
                    "Librería: no se encontró GetObject(string) ni en el Helper ni en " +
                    "sus interfaces. Métodos disponibles: " + MiembrosDe(tipoHelper));
            }
        }
        catch (Exception ex)
        {
            _bitacora.Add("Librería, GetObject: " + Detalle(ex));
        }

        // GetObjectProcess(progId, pid): hace falta cuando hay VARIAS instancias
        // de ETABS abiertas, porque entonces GetObject no sabe a cuál engancharse.
        try
        {
            var m = MetodoDe(tipoHelper, "GetObjectProcess", typeof(string), typeof(int));
            if (m is null)
            {
                return null;
            }

            _tipoOapi ??= m.ReturnType;

            foreach (var pid in ProcesosEtabs())
            {
                try
                {
                    var obj = m.Invoke(helper, new object?[] { ProgIdApp, pid });
                    if (obj is not null)
                    {
                        _bitacora.Add($"Librería: GetObjectProcess entregó {NombreDelDestino} (pid {pid}).");
                        return obj;
                    }
                }
                catch (Exception ex)
                {
                    _bitacora.Add($"Librería, GetObjectProcess(pid {pid}): " + Detalle(ex));
                }
            }
        }
        catch (Exception ex)
        {
            _bitacora.Add("Librería, GetObjectProcess: " + Detalle(ex));
        }

        return null;
    }

    /// <summary>Identificadores de los procesos de ETABS que están corriendo.</summary>
    private static List<int> ProcesosEtabs()
    {
        var ids = new List<int>();

        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    // El nombre del proceso depende del programa: 'ETABS' o 'SAP2000'.
                    // Se mira la bandera ESTATICA porque este bloque lo es; Conectar la
                    // fija antes de llegar aqui.
                    if (p.ProcessName.Contains(
                            EtabsAssembly.ParaSap2000 ? "sap2000" : "etabs",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(p.Id);
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Sin la lista de procesos solo se pierde este respaldo.
        }

        return ids;
    }

    private object? PorObjetoActivo()
    {
        try
        {
            CLSIDFromProgID(ProgIdApp, out var clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            _bitacora.Add($"Objeto activo '{ProgIdApp}': encontrado.");
            return obj;
        }
        catch (Exception ex)
        {
            _bitacora.Add($"Objeto activo '{ProgIdApp}': {Detalle(ex)}");
            return null;
        }
    }

    /// <summary>Vía Helper, la que documenta CSI para las versiones recientes.</summary>
    private object? PorHelper()
    {
        foreach (var progId in ProgIdsHelper)
        {
            var tipo = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (tipo is null)
            {
                _bitacora.Add($"Helper '{progId}': no está registrado.");
                continue;
            }

            try
            {
                var helper = Activator.CreateInstance(tipo);
                if (helper is null)
                {
                    _bitacora.Add($"Helper '{progId}': no se pudo crear.");
                    continue;
                }

                var obj = Com.Call(helper, "GetObject", new object?[] { ProgIdApp });
                if (obj is not null)
                {
                    _bitacora.Add($"Helper '{progId}': entregó el objeto de {NombreDelDestino}.");
                    return obj;
                }

                _bitacora.Add($"Helper '{progId}': devolvió vacío.");
            }
            catch (Exception ex)
            {
                _bitacora.Add($"Helper '{progId}': {Detalle(ex)}");
            }
        }

        return null;
    }

    /// <summary>
    /// Lista los métodos públicos de un tipo y de sus interfaces, para el diagnóstico.
    /// </summary>
    /// <remarks>
    /// Es para que un fallo de nombre <b>se resuelva en un solo intento</b>. Decirle
    /// al usuario «el Helper no tiene GetObject» no le sirve de nada; enseñarle la
    /// lista de lo que sí tiene convierte el siguiente diagnóstico en la respuesta.
    /// </remarks>
    private static string MiembrosDe(Type tipo)
    {
        try
        {
            var nombres = new SortedSet<string>(StringComparer.Ordinal);

            void Recoger(Type t)
            {
                foreach (var m in t.GetMethods())
                {
                    if (m.IsSpecialName)
                    {
                        continue;
                    }

                    var args = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                    nombres.Add($"{m.Name}({args})");
                }
            }

            Recoger(tipo);

            foreach (var i in tipo.GetInterfaces())
            {
                Recoger(i);
            }

            return nombres.Count == 0 ? "(ninguno)" : string.Join(", ", nombres.Take(40));
        }
        catch (Exception ex)
        {
            return "(no se pudieron leer: " + ex.GetType().Name + ")";
        }
    }

    /// <summary>Mensaje de error útil: tipo, HRESULT y texto, desenvolviendo el interno.</summary>
    private static string Detalle(Exception ex)
    {
        var e = ex;
        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var s = new StringBuilder();
        s.Append(e.GetType().Name);

        if (e is COMException com)
        {
            s.Append($" 0x{(uint)com.HResult:X8}");
        }

        s.Append(": ").Append(e.Message.Replace(Environment.NewLine, " "));
        return s.ToString();
    }

    // ==================================================================
    // Información y unidades
    // ==================================================================

    private void LeerInfo()
    {
        try
        {
            object?[] a = { string.Empty, string.Empty, string.Empty };
            Com.Call(SapModel, "GetProgramInfo", a, 0, 1, 2);
            Programa = $"{a[0]} {a[1]}".Trim();
        }
        catch (Exception ex)
        {
            _bitacora.Add("GetProgramInfo: " + Detalle(ex));
            Programa = $"{NombreDelDestino} (versión no reportada)";
        }

        try
        {
            var r = Com.Call(SapModel, "GetModelFilename", new object?[] { true });
            Modelo = r?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _bitacora.Add("GetModelFilename: " + Detalle(ex));
            Modelo = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(Modelo))
        {
            Modelo = "(modelo sin guardar)";
        }
    }

    /// <summary>
    /// Pone kN·m·C para leer en metros, guardando las unidades del usuario.
    /// </summary>
    /// <remarks>
    /// La macro las cambia y nunca las restaura: el ingeniero deja ETABS en kgf-cm,
    /// corre la macro, y lo encuentra en kN-m. Aquí se devuelven en <see cref="Dispose"/>.
    /// </remarks>
    private void FijarUnidades()
    {
        try
        {
            var actuales = Com.Call(SapModel, "GetPresentUnits", Array.Empty<object?>());
            if (actuales is not null)
            {
                _unidadesOriginales = Convert.ToInt32(actuales);
            }
        }
        catch (Exception)
        {
            _unidadesOriginales = null;
        }

        try
        {
            Com.Call(SapModel, "SetPresentUnits", new object?[] { UnidadesKnMC });
        }
        catch (Exception ex)
        {
            _bitacora.Add("SetPresentUnits: " + Detalle(ex) + " (se leerá con las unidades actuales)");
        }
    }

    public void Dispose()
    {
        if (_sapModel is not null && _unidadesOriginales is not null)
        {
            try
            {
                Com.Call(SapModel, "SetPresentUnits", new object?[] { _unidadesOriginales.Value });
            }
            catch (Exception)
            {
                // No vale la pena tapar el error real con este.
            }
        }

        if (_sapModel is not null && Marshal.IsComObject(_sapModel))
        {
            Marshal.FinalReleaseComObject(_sapModel);
        }

        if (_etabs is not null && Marshal.IsComObject(_etabs))
        {
            Marshal.FinalReleaseComObject(_etabs);
        }

        _sapModel = null;
        _etabs = null;
    }
}
