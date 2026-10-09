namespace CadLink.Revit.Nucleo;

/// <summary>Un motivo de fallo y cuantas piezas cayeron en el.</summary>
public sealed record Grupo(string Motivo, int Cuantas, List<string> Ejemplos);

/// <summary>
/// Agrupa los errores por su CAUSA, para que el informe diga algo util.
/// </summary>
/// <remarks>
/// <para>
/// Cuando una importacion falla, casi nunca falla por trescientos motivos distintos: falla
/// por UNO que afecta a trescientas piezas. Un informe que dice "no se pudieron modelar 372"
/// y ensena las ocho primeras no permite arreglar nada, porque las ocho primeras dicen lo
/// mismo y no se ve que son todas iguales.
/// </para>
/// <para>
/// Agrupado, ese mismo informe dice "372 piezas: el tipo elegido no admite este contorno", y
/// entonces ya se sabe que hay que mirar.
/// </para>
/// </remarks>
public static class Agrupador
{
    /// <summary>Cuantas piezas de ejemplo se guardan por motivo.</summary>
    public const int EjemplosPorMotivo = 3;

    /// <summary>
    /// Agrupa mensajes con la forma <c>«llave»: motivo</c>.
    /// </summary>
    /// <remarks>
    /// Se corta por el primer <c>»: </c> para separar la pieza del motivo. Un mensaje que no
    /// lleve ese separador se agrupa por el texto completo, que es lo correcto: no se sabe que
    /// parte es la pieza, asi que no se adivina.
    /// </remarks>
    public static List<Grupo> Agrupar(IEnumerable<string>? errores)
    {
        var mapa = new Dictionary<string, Grupo>(StringComparer.Ordinal);
        var orden = new List<string>();

        foreach (var e in errores ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(e))
            {
                continue;
            }

            var (pieza, motivo) = Partir(e);

            if (!mapa.TryGetValue(motivo, out var g))
            {
                g = new Grupo(motivo, 0, new List<string>());
                mapa[motivo] = g;
                orden.Add(motivo);
            }

            var nuevo = g with { Cuantas = g.Cuantas + 1 };

            if (pieza.Length > 0 && nuevo.Ejemplos.Count < EjemplosPorMotivo)
            {
                nuevo.Ejemplos.Add(pieza);
            }

            mapa[motivo] = nuevo;
        }

        // Del motivo que mas piezas afecta al que menos, y a igualdad por el orden en que
        // aparecieron, para que dos corridas del mismo modelo den el mismo informe.
        return orden
            .Select(m => mapa[m])
            .OrderByDescending(g => g.Cuantas)
            .ThenBy(g => orden.IndexOf(g.Motivo))
            .ToList();
    }

    private static (string Pieza, string Motivo) Partir(string mensaje)
    {
        const string sep = "»: ";
        var i = mensaje.IndexOf(sep, StringComparison.Ordinal);

        if (i < 0)
        {
            return (string.Empty, mensaje.Trim());
        }

        var pieza = mensaje[..(i + 1)].Trim().Trim('«', '»');
        var motivo = mensaje[(i + sep.Length)..].Trim();

        return (pieza, motivo.Length == 0 ? mensaje.Trim() : motivo);
    }

    /// <summary>El informe de los errores, ya agrupado y listo para ensenar.</summary>
    /// <param name="errores">Los mensajes, con la forma <c>«llave»: motivo</c>.</param>
    /// <param name="motivosMaximos">Cuantos motivos distintos se ensenan.</param>
    /// <param name="encabezado">
    /// Como empieza el informe. Con <c>null</c> se usa el de los errores. Los AVISOS necesitan
    /// otro: agruparlos igual es util, pero decir de ellos "no se pudieron modelar" seria
    /// mentira, porque un aviso no impide modelar.
    /// </param>
    public static string Texto(
        IEnumerable<string>? errores, int motivosMaximos = 5, string? encabezado = null)
    {
        var grupos = Agrupar(errores);

        if (grupos.Count == 0)
        {
            return string.Empty;
        }

        var total = grupos.Sum(g => g.Cuantas);
        var sb = new System.Text.StringBuilder();

        sb.Append(encabezado ?? "No se pudieron modelar")
          .Append(' ').Append(total).Append(" pieza(s), por ")
          .Append(grupos.Count).AppendLine(grupos.Count == 1 ? " motivo:" : " motivos:");

        foreach (var g in grupos.Take(motivosMaximos))
        {
            sb.AppendLine();
            sb.Append("  ").Append(g.Cuantas).Append(" x  ").AppendLine(g.Motivo);

            if (g.Ejemplos.Count > 0)
            {
                sb.Append("      por ejemplo: ").AppendLine(string.Join(", ", g.Ejemplos));
            }
        }

        if (grupos.Count > motivosMaximos)
        {
            sb.AppendLine();
            sb.Append("  ... y ").Append(grupos.Count - motivosMaximos)
              .AppendLine(" motivo(s) mas.");
        }

        return sb.ToString().TrimEnd();
    }
}
