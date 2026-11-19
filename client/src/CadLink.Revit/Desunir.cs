using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CadLink.Revit;

/// <summary>Lo que salio de desunir.</summary>
public sealed class ResultadoDesunir
{
    public int Vigas { get; set; }

    public int Muros { get; set; }

    /// <summary>Pares de piezas que estaban unidas con Join Geometry y se separaron.</summary>
    public int Uniones { get; set; }

    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Quita las uniones de las piezas para que se puedan mover sueltas: las de las puntas de vigas y
/// muros, y las de geometria -Join Geometry- entre cualquier par.
/// </summary>
/// <remarks>
/// <para>
/// <b>Primero se congela el largo de cada viga.</b> Revit recorta la viga en la cara de la columna
/// mientras su punta esta unida; al quitar esa union la viga crece hasta la punta de su linea, que
/// llega al CENTRO de la columna. El modelo cambiaria de forma y el armado -que se mide de cara a
/// cara- quedaria metido en las columnas. Asi que la linea se lleva antes a lo que la viga mide de
/// verdad, y la forma no cambia.
/// </para>
/// <para>
/// No usa transaccion propia: va dentro de la del que llama, para que el mismo Ctrl+Z lo deshaga.
/// </para>
/// </remarks>
internal static class Desunir
{
    public static ResultadoDesunir Piezas(Document doc, IEnumerable<Element> piezas)
    {
        var r = new ResultadoDesunir();
        var lista = piezas.Where(e => e is not null && e.IsValidObject).ToList();
        var ids = lista.Select(e => e.Id).ToHashSet();

        // ---- 1. Las vigas: su largo de cara a cara, y despues sus puntas sueltas ----
        foreach (var e in lista.OfType<FamilyInstance>())
        {
            if (e.Category?.BuiltInCategory != BuiltInCategory.OST_StructuralFraming
                || e.Location is not LocationCurve lc || lc.Curve is not Line linea)
            {
                continue;
            }

            try
            {
                var ya = !StructuralFramingUtils.IsJoinAllowedAtEnd(e, 0)
                         && !StructuralFramingUtils.IsJoinAllowedAtEnd(e, 1);

                if (ya)
                {
                    continue;
                }

                var p0 = linea.GetEndPoint(0);
                var p1 = linea.GetEndPoint(1);
                var largo = p0.DistanceTo(p1);
                var dir = (p1 - p0).Normalize();
                var puntos = Geometria.PuntosDelSolido(e);

                StructuralFramingUtils.DisallowJoinAtEnd(e, 0);
                StructuralFramingUtils.DisallowJoinAtEnd(e, 1);

                if (puntos.Count >= 2 && largo > 1e-6)
                {
                    var t0 = puntos.Min(q => (q - p0).DotProduct(dir));
                    var t1 = puntos.Max(q => (q - p0).DotProduct(dir));

                    // Solo si lo medido es mas corto que la linea y tiene sentido: una viga con
                    // extension o con su solido raro se deja como esta.
                    t0 = Math.Max(0, t0);
                    t1 = Math.Min(largo, t1);

                    if (t1 - t0 > Unidades.AInternas(0.05) && (t0 > 1e-6 || t1 < largo - 1e-6))
                    {
                        lc.Curve = Line.CreateBound(p0 + (dir * t0), p0 + (dir * t1));
                    }
                }

                r.Vigas++;
            }
            catch (Exception ex)
            {
                r.Avisos.Add($"«viga {e.Id.Value}»: No se pudo soltar. {ex.Message}");
            }
        }

        // ---- 2. Los muros: sus dos puntas sin union ----
        foreach (var w in lista.OfType<Wall>())
        {
            try
            {
                var algo = false;

                for (var punta = 0; punta <= 1; punta++)
                {
                    if (WallUtils.IsWallJoinAllowedAtEnd(w, punta))
                    {
                        WallUtils.DisallowWallJoinAtEnd(w, punta);
                        algo = true;
                    }
                }

                if (algo)
                {
                    r.Muros++;
                }
            }
            catch (Exception ex)
            {
                r.Avisos.Add($"«muro {w.Id.Value}»: No se pudo soltar. {ex.Message}");
            }
        }

        // ---- 3. Join Geometry: cada par una sola vez ----
        var hechos = new HashSet<(long, long)>();

        foreach (var e in lista)
        {
            ICollection<ElementId> unidos;

            try
            {
                unidos = JoinGeometryUtils.GetJoinedElements(doc, e);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var otroId in unidos)
            {
                var par = e.Id.Value < otroId.Value ? (e.Id.Value, otroId.Value) : (otroId.Value, e.Id.Value);

                if (!hechos.Add(par) || doc.GetElement(otroId) is not { } otro)
                {
                    continue;
                }

                try
                {
                    JoinGeometryUtils.UnjoinGeometry(doc, e, otro);
                    r.Uniones++;
                }
                catch (Exception)
                {
                    // Unas uniones las rehace Revit solo (columna con viga): no es un error.
                }
            }
        }

        return r;
    }

    public static string Texto(ResultadoDesunir r) =>
        $"Se soltaron {r.Vigas} viga(s) y {r.Muros} muro(s), y se separaron {r.Uniones} union(es) de geometria.";
}
