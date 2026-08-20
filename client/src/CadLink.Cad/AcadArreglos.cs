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

        // Cada vía se intenta VARIAS VECES si el error es «AutoCAD ocupado».
        //
        // Sin esto, un rechazo pasajero hacía abandonar la vía buena y pasar a las
        // siguientes, que en AutoCAD 2026 fallan siempre por el tipo del arreglo. El
        // usuario veía tres fallos seguidos —el primero RPC_E_CALL_REJECTED y los otros
        // dos «Invalid object array»— y el diagnóstico apuntaba al arreglo cuando el
        // problema era que AutoCAD estaba a media faena.
        //
        // Devuelve true si la llamada pasó -o si ya había surtido efecto-, y false si hay
        // que probar la vía siguiente.
        bool Intentar(Func<object> construir, string via, string exito)
        {
            for (var intento = 1; intento <= IntentosPorOcupado; intento++)
            {
                try
                {
                    llamada(construir());
                    nota(exito);
                    return true;
                }
                catch (Exception ex) when (AcadConnection.EstaOcupado(ex)
                                          && intento < IntentosPorOcupado)
                {
                    // Ocupado: se espera y se reintenta LA MISMA vía. No se registra
                    // como fallo, porque no lo es todavía.
                    Thread.Sleep(EsperaMs);
                }
                catch (Exception ex)
                {
                    fallo($"{operacion} [{via}]", ex);
                    return Surtio(via);
                }
            }

            return false;
        }

        // 1) Arreglo TIPADO con AcadEntity: SAFEARRAY de VT_DISPATCH
        var tipado = AcadInterop.ArregloTipado(entidades);

        if (tipado is not null
            && Intentar(
                () => tipado,
                "AcadEntity tipado",
                "Arreglos de entidades: se usa el tipo AcadEntity de la interop."))
        {
            return true;
        }

        // 2) VT_DISPATCH elemento a elemento
        if (Intentar(
                () => entidades.Select(e => (object)new DispatchWrapper(e)).ToArray(),
                "DispatchWrapper",
                operacion + ": funcionó con DispatchWrapper."))
        {
            return true;
        }

        // 3) El arreglo tal cual
        return Intentar(
            () => entidades.ToArray(),
            "arreglo directo",
            operacion + ": funcionó pasando el arreglo sin envolver.");
    }

    /// <summary>Cuántas veces se reintenta una vía cuando AutoCAD está ocupado.</summary>
    /// <remarks>
    /// Los mismos doce intentos cada 250 ms que usa <see cref="AcadConnection.Retry{T}"/>:
    /// tres segundos de paciencia, que es lo que tarda AutoCAD en soltar un comando a
    /// medias o en acabar de regenerar un dibujo grande.
    /// </remarks>
    private const int IntentosPorOcupado = 12;

    /// <summary>Espera entre reintentos, en milisegundos.</summary>
    private const int EsperaMs = 250;
}
