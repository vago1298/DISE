using System.Runtime.InteropServices;

namespace CadLink.Cad;

/// <summary>
/// Llamadas de AutoCAD que reciben un <b>arreglo de entidades</b>.
/// </summary>
/// <remarks>
/// <para>
/// Vive aparte porque la usan varios dibujantes y porque resuelve un problema que
/// costó encontrar: <c>AppendOuterLoop</c>, <c>AppendInnerLoop</c>,
/// <c>CopyObjects</c>, <c>MoveToTop</c> y <c>MoveToBottom</c> fallaban con
/// </para>
/// <code>COMException 0x8021007B: Invalid object array</code>
/// <para>
/// por el <b>tipo de elemento del SAFEARRAY</b>. En VBA, <c>Dim v() As Object</c>
/// produce un SAFEARRAY de <c>VT_DISPATCH</c>; un <c>object[]</c> de .NET produce
/// uno de <c>VT_VARIANT</c> y AutoCAD lo rechaza. Envolver los elementos en
/// <c>DispatchWrapper</c> no basta, porque el tipo del arreglo no cambia. Lo único
/// que funciona es un arreglo <b>tipado</b> con <c>AcadEntity</c>, que se obtiene
/// de la interop cargada en tiempo de ejecución.
/// </para>
/// <para>
/// Se conservan las tres formas en cascada: si en alguna versión de AutoCAD la
/// interop no está, todavía queda la posibilidad de que una de las otras pase.
/// </para>
/// </remarks>
internal static class AcadArreglos
{
    /// <param name="fallo">Registra un fallo tolerado.</param>
    /// <param name="nota">Registra qué vía funcionó.</param>
    /// <param name="yaSurtioEfecto">
    /// Comprueba si la llamada <b>ya tuvo efecto</b> aunque haya reportado error. Si
    /// devuelve <c>true</c>, la cascada se detiene.
    /// </param>
    /// <remarks>
    /// El <paramref name="yaSurtioEfecto"/> existe por una advertencia explícita de
    /// la macro sobre <c>AppendInnerLoop</c>: si se reintenta una llamada que en
    /// realidad sí agregó la isla, la isla queda <b>duplicada</b>, y con el estilo
    /// Normal del hatch dos islas iguales se anulan entre sí. El resultado sería una
    /// varilla rayada por encima. Con esta comprobación, un fallo que en realidad
    /// funcionó no se reintenta.
    /// </remarks>
    public static bool Llamar(
        string operacion,
        IReadOnlyList<object> entidades,
        Action<object> llamada,
        Action<string, Exception> fallo,
        Action<string> nota,
        Func<bool>? yaSurtioEfecto = null)
    {
        if (entidades.Count == 0)
        {
            return true;
        }

        bool Surtio(string via)
        {
            if (yaSurtioEfecto?.Invoke() != true)
            {
                return false;
            }

            nota($"{operacion}: reportó error por {via} pero SÍ tuvo efecto; " +
                 "no se reintenta para no duplicarlo.");
            return true;
        }

        // 1) Arreglo TIPADO con AcadEntity: SAFEARRAY de VT_DISPATCH
        var tipado = AcadInterop.ArregloTipado(entidades);
        if (tipado is not null)
        {
            try
            {
                llamada(tipado);
                nota("Arreglos de entidades: se usa el tipo AcadEntity de la interop.");
                return true;
            }
            catch (Exception ex)
            {
                fallo(operacion + " [AcadEntity tipado]", ex);

                if (Surtio("el arreglo tipado"))
                {
                    return true;
                }
            }
        }

        // 2) VT_DISPATCH elemento a elemento
        try
        {
            llamada(entidades.Select(e => (object)new DispatchWrapper(e)).ToArray());
            nota(operacion + ": funcionó con DispatchWrapper.");
            return true;
        }
        catch (Exception ex)
        {
            fallo(operacion + " [DispatchWrapper]", ex);

            if (Surtio("DispatchWrapper"))
            {
                return true;
            }
        }

        // 3) El arreglo tal cual
        try
        {
            llamada(entidades.ToArray());
            nota(operacion + ": funcionó pasando el arreglo sin envolver.");
            return true;
        }
        catch (Exception ex)
        {
            fallo(operacion + " [arreglo directo]", ex);
            return false;
        }
    }
}
