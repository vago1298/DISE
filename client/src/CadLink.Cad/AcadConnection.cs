using System.Runtime.InteropServices;

namespace CadLink.Cad;

/// <summary>
/// Conexión a AutoCAD por COM, equivalente en C# a lo que la macro hace con
/// <c>GetObject(, "AutoCAD.Application")</c> y <c>CreateObject</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema que resuelve esta clase.</b> En .NET Framework existía
/// <c>Marshal.GetActiveObject("AutoCAD.Application")</c>. En .NET Core y de ahí
/// en adelante — incluido .NET 8 — <b>ese método fue eliminado</b>. Sin él no hay
/// forma directa de adjuntarse a una instancia de AutoCAD que ya esté abierta, que
/// es justo lo que necesita esta aplicación.
/// </para>
/// <para>
/// La solución es llamar a la función nativa <c>GetActiveObject</c> de
/// <c>oleaut32.dll</c>, que es lo que hacía internamente el método desaparecido.
/// </para>
/// </remarks>
public static class AcadConnection
{
    private const string ProgId = "AutoCAD.Application";

    // HRESULTs de COM que significan "AutoCAD está ocupado, vuelve a intentar".
    // Son la causa número uno de fallos intermitentes al manejar AutoCAD desde
    // fuera: si el usuario tiene un comando a medias o un diálogo abierto, las
    // llamadas se rechazan.
    private const uint RpcECallRejected = 0x80010001;
    private const uint RpcEServerCallRetryLater = 0x8001010A;

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID,
        out Guid lpclsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    /// <summary>
    /// Se adjunta a la instancia de AutoCAD abierta, o <c>null</c> si no hay ninguna.
    /// </summary>
    public static dynamic? AttachToRunningInstance()
    {
        try
        {
            CLSIDFromProgID(ProgId, out var clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return obj;
        }
        catch (COMException)
        {
            // MK_E_UNAVAILABLE: no hay instancia registrada. Es el caso normal
            // cuando AutoCAD simplemente no está abierto.
            return null;
        }
    }

    /// <summary>
    /// Se adjunta a AutoCAD si está abierto y, si no, lo lanza.
    /// </summary>
    /// <param name="launchIfMissing">
    /// Si es <c>false</c> y AutoCAD no está abierto, se lanza una excepción en
    /// lugar de arrancarlo. Arrancar AutoCAD tarda bastante y consume una
    /// licencia, así que conviene que sea una decisión explícita del usuario.
    /// </param>
    public static dynamic Connect(bool launchIfMissing = true)
    {
        dynamic? running = AttachToRunningInstance();
        if (running is not null)
        {
            return running;
        }

        if (!launchIfMissing)
        {
            throw new AcadNotAvailableException(
                "AutoCAD no está abierto. Ábrelo con el dibujo donde quieres " +
                "trabajar y vuelve a intentar.");
        }

        var type = Type.GetTypeFromProgID(ProgId, throwOnError: false);
        if (type is null)
        {
            throw new AcadNotAvailableException(
                "No se encontró AutoCAD instalado en este equipo. " +
                "Verifica que esté instalado y que se haya abierto al menos una vez.");
        }

        dynamic app = Activator.CreateInstance(type)
                      ?? throw new AcadNotAvailableException("No se pudo iniciar AutoCAD.");

        // Cuerpo con llaves a propósito: con 'app.Visible = true' suelto, el
        // compilador tiene que elegir entre la sobrecarga Func<T> y la Action,
        // y conviene no dejar esa ambigüedad escrita.
        Retry(() => { app.Visible = true; });

        return app;
    }

    /// <summary>
    /// Documento activo, creando uno nuevo si no hay ninguno abierto.
    /// </summary>
    public static dynamic GetOrCreateDocument(dynamic app, bool forceNewDrawing = false)
    {
        if (forceNewDrawing)
        {
            return Retry(() => app.Documents.Add());
        }

        try
        {
            var doc = Retry(() => app.ActiveDocument);
            if (doc is not null)
            {
                return doc;
            }
        }
        catch (COMException)
        {
            // Sin documentos abiertos, ActiveDocument falla.
        }

        return Retry(() => app.Documents.Add());
    }

    /// <summary>
    /// Ejecuta una llamada a AutoCAD reintentando cuando la rechaza por estar ocupado.
    /// </summary>
    /// <remarks>
    /// <b>Envuelve con esto toda llamada COM a AutoCAD.</b> Es el equivalente
    /// disciplinado de los <c>On Error Resume Next</c> de la macro: en lugar de
    /// tragarse el error y seguir con datos incompletos, se distingue el caso
    /// recuperable ("está ocupado") del error real, y solo el primero se reintenta.
    ///
    /// Sin esto, manejar AutoCAD desde otro proceso falla de forma intermitente y
    /// sin patrón aparente, que es lo más difícil de diagnosticar para un usuario.
    /// </remarks>
    public static T Retry<T>(Func<T> action, int attempts = 12, int delayMs = 250)
    {
        COMException? last = null;

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (IsBusy(ex))
            {
                last = ex;
                Thread.Sleep(delayMs);
            }
        }

        throw new AcadBusyException(
            "AutoCAD no respondió después de varios intentos. Suele ser porque hay " +
            "un comando a medias o un cuadro de diálogo abierto: termínalo o " +
            "ciérralo y vuelve a intentar.",
            last);
    }

    /// <summary>Igual que <see cref="Retry{T}"/> para llamadas sin valor de retorno.</summary>
    public static void Retry(Action action, int attempts = 12, int delayMs = 250)
    {
        Retry<object?>(() => { action(); return null; }, attempts, delayMs);
    }

    private static bool IsBusy(COMException ex) => EstaOcupado(ex);

    /// <summary>
    /// Si el error es de los recuperables: <b>AutoCAD estaba ocupado</b>, no que la
    /// llamada estuviera mal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está expuesto porque <see cref="Retry{T}"/> no sirve en un caso: cuando una
    /// llamada se prueba de <b>varias formas en cascada</b>, como los arreglos de
    /// entidades de <c>AcadArreglos</c>. Ahí, tragarse el error y pasar a la forma
    /// siguiente es exactamente lo que NO hay que hacer si el error fue «ocupado»:
    /// se abandona la única vía que esa versión de AutoCAD acepta y las siguientes
    /// fallan por otro motivo, así que el diagnóstico acaba señalando al arreglo
    /// cuando el problema era que AutoCAD estaba a media faena.
    /// </para>
    /// <para>
    /// Fue el error real que vio el usuario al dibujar cuatro secciones:
    /// <c>MoveToTop [AcadEntity tipado] -&gt; RPC_E_CALL_REJECTED</c> y a
    /// continuación las otras dos vías con <c>Invalid object array</c>.
    /// </para>
    /// </remarks>
    public static bool EstaOcupado(Exception ex)
    {
        if (ex is not COMException com)
        {
            return false;
        }

        var hr = (uint)com.HResult;
        return hr is RpcECallRejected or RpcEServerCallRetryLater;
    }
}

/// <summary>AutoCAD no está disponible en este equipo.</summary>
public sealed class AcadNotAvailableException : Exception
{
    public AcadNotAvailableException(string message) : base(message) { }
}

/// <summary>AutoCAD está ocupado y no atendió la llamada.</summary>
public sealed class AcadBusyException : Exception
{
    public AcadBusyException(string message, Exception? inner) : base(message, inner) { }
}
