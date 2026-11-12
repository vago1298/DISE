using System.Globalization;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  EL CATALOGO DE PERFILES DEL PROPIO LIBRO: SU HOJA IMCA5
//
//  Hay hojas que no traen todas las medidas de un perfil (la conexion a cortante no da el grueso
//  del patin de la viga que soporta). El libro las tiene en su hoja IMCA5 -la misma de donde salen
//  sus formulas-, asi que de ahi se completan, sin adivinar.
// ============================================================================

public static class CatalogoDelLibro
{
    /// <summary>Las columnas de la hoja IMCA5 (1 = A).</summary>
    private const int ColNombre = 2, ColD = 3, ColTdist = 5, ColTw = 27, ColBf = 28, ColTf = 29;

    /// <summary>
    /// Busca perfiles por su nombre en la hoja IMCA5 del libro; null si el libro no la trae. Las
    /// medidas salen en cm (la hoja las da en mm; la pared de diseño de los tubos, en cm).
    /// </summary>
    public static Func<string, PerfilDeCatalogo?> De(LibroExcel libro)
    {
        var tabla = new Dictionary<string, PerfilDeCatalogo>(StringComparer.OrdinalIgnoreCase);
        var hoja = libro.Hojas.FirstOrDefault(n => n.Trim().Equals("IMCA5", StringComparison.OrdinalIgnoreCase)) is { } n0
            ? libro.Hoja(n0)
            : null;

        if (hoja is not null)
        {
            double N(int f, int c) => hoja.Celda(f, c)?.Numero ?? double.NaN;

            for (var f = 1; f <= hoja.UltimaFila; f++)
            {
                var nombre = hoja.Texto(f, ColNombre).Trim();

                if (nombre.Length == 0 || tabla.ContainsKey(nombre))
                {
                    continue;
                }

                if (nombre.StartsWith("HSS", StringComparison.OrdinalIgnoreCase))
                {
                    // «HSS - 5" x 5/16"» (cuadrado) o «HSS - 8" x 4" x 1/2"»: las caras, del nombre.
                    var medidas = Pulgadas(nombre);
                    var pared = N(f, ColTdist);

                    if (medidas.Count >= 2 && pared > 0)
                    {
                        var alto = medidas[0] * 2.54;
                        var ancho = (medidas.Count >= 3 ? medidas[1] : medidas[0]) * 2.54;
                        var t = pared / 0.93;
                        tabla[nombre] = new PerfilDeCatalogo(alto, ancho, t, t, 0, true);
                    }

                    continue;
                }

                var d = N(f, ColD);
                var bf = N(f, ColBf);
                var tf = N(f, ColTf);
                var tw = N(f, ColTw);

                if (d > 0 && bf > 0 && tf > 0 && tw > 0)
                {
                    tabla[nombre] = new PerfilDeCatalogo(d / 10, bf / 10, tf / 10, tw / 10, 0, false);
                }
            }
        }

        return nombre => tabla.TryGetValue((nombre ?? string.Empty).Trim(), out var p) ? p : null;
    }

    /// <summary>Las medidas en pulgadas de un nombre: «5"», «4 1/2"», «5/16"».</summary>
    private static List<double> Pulgadas(string nombre)
    {
        var lista = new List<double>();

        foreach (Match m in Regex.Matches(nombre, @"(\d+)\s+(\d+)/(\d+)|(\d+)/(\d+)|(\d+(?:\.\d+)?)"))
        {
            if (m.Groups[1].Success)
            {
                lista.Add(D(m.Groups[1].Value) + (D(m.Groups[2].Value) / D(m.Groups[3].Value)));
            }
            else if (m.Groups[4].Success)
            {
                lista.Add(D(m.Groups[4].Value) / D(m.Groups[5].Value));
            }
            else
            {
                lista.Add(D(m.Groups[6].Value));
            }
        }

        return lista;

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }
}
