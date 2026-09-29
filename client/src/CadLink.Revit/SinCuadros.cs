using Autodesk.Revit.DB;

namespace CadLink.Revit;

/// <summary>
/// Evita que Revit levante su propio cuadro al confirmar la importacion.
/// </summary>
/// <remarks>
/// <para>
/// Sin esto, al confirmar la transaccion Revit abre su cuadro de avisos. Los avisos se podrian
/// descartar, pero un error marcado <b>"cannot be ignored"</b> -por ejemplo
/// <i>"Position of end cut planes has resulted in a slanted column without any geometry"</i>-
/// deja ese cuadro con el OK apagado y solo el boton Cancel. Y al cancelar <b>se deshace la
/// transaccion entera</b>.
/// </para>
/// <para>
/// El resultado es el peor posible: el informe dice que se crearon cuatrocientas cincuenta
/// piezas, y en el modelo no hay ninguna. Una pieza mala tira las otras cuatrocientas
/// cuarenta y nueve.
/// </para>
/// <para>
/// Aqui los avisos se descartan y el elemento que causa un error se borra, de modo que el resto
/// SI se confirma. No se esconde: lo silenciado y lo borrado se cuenta y aparece en el informe.
/// Es la diferencia entre perder una pieza y perderlas todas.
/// </para>
/// </remarks>
internal sealed class SinCuadros : IFailuresPreprocessor
{
    /// <summary>Cuantos avisos se descartaron.</summary>
    public int AvisosSilenciados { get; private set; }

    /// <summary>Cuantos elementos se borraron por dar un error que no se puede ignorar.</summary>
    public int ElementosBorrados { get; private set; }

    /// <summary>Los textos distintos que dijo Revit, para poder ensenarlos.</summary>
    public List<string> Motivos { get; } = new();

    public FailureProcessingResult PreprocessFailures(FailuresAccessor acceso)
    {
        var huboAlgo = false;

        foreach (var m in acceso.GetFailureMessages())
        {
            var severidad = m.GetSeverity();

            Apuntar(m);

            if (severidad == FailureSeverity.Warning)
            {
                acceso.DeleteWarning(m);
                AvisosSilenciados++;
                huboAlgo = true;

                continue;
            }

            // Un error. Primero se intenta la resolucion que Revit proponga; si no propone
            // ninguna, se borran los elementos que lo causan.
            if (m.HasResolutions())
            {
                acceso.ResolveFailure(m);
                huboAlgo = true;

                continue;
            }

            var culpables = m.GetFailingElementIds()?.ToList() ?? new List<ElementId>();

            if (culpables.Count > 0)
            {
                acceso.DeleteElements(culpables);
                ElementosBorrados += culpables.Count;
                huboAlgo = true;
            }
        }

        // ProceedWithCommit solo si se resolvio algo; si no, Continue, que deja seguir el curso
        // normal. Devolver siempre ProceedWithCommit puede meter a Revit en un bucle cuando el
        // fallo no se ha resuelto de verdad.
        return huboAlgo
            ? FailureProcessingResult.ProceedWithCommit
            : FailureProcessingResult.Continue;
    }

    private void Apuntar(FailureMessageAccessor m)
    {
        // Se guardan los textos DISTINTOS, no uno por pieza: cuarenta y seis avisos iguales
        // llenarian el informe sin decir nada nuevo.
        if (Motivos.Count >= 8)
        {
            return;
        }

        string texto;

        try
        {
            texto = m.GetDescriptionText() ?? string.Empty;
        }
        catch (Exception)
        {
            return;
        }

        texto = texto.Trim();

        if (texto.Length > 0 && !Motivos.Contains(texto, StringComparer.OrdinalIgnoreCase))
        {
            Motivos.Add(texto);
        }
    }
}
