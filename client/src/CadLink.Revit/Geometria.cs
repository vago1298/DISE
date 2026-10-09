using Autodesk.Revit.DB;

namespace CadLink.Revit;

/// <summary>Lo que se mide de la geometria que Revit ya dibujo.</summary>
internal static class Geometria
{
    /// <summary>
    /// Los vertices del solido de la pieza tal como la dibuja Revit: ya recortada en las caras
    /// de las columnas. Vacio si no se puede leer.
    /// </summary>
    public static List<XYZ> PuntosDelSolido(Element inst)
    {
        var res = new List<XYZ>();

        try
        {
            var geo = inst.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });

            if (geo is null)
            {
                return res;
            }

            void DeSolidos(IEnumerable<GeometryObject> objetos)
            {
                foreach (var o in objetos)
                {
                    if (o is Solid solido && solido.Volume > 1e-9)
                    {
                        foreach (var e in solido.Edges)
                        {
                            if (e is Edge arista)
                            {
                                res.AddRange(arista.Tessellate());
                            }
                        }
                    }
                }
            }

            foreach (var o in geo)
            {
                if (o is GeometryInstance gi)
                {
                    // Aqui si la geometria de la INSTANCIA: hacen falta coordenadas del modelo.
                    DeSolidos(gi.GetInstanceGeometry());
                }
                else
                {
                    DeSolidos(new[] { o });
                }
            }
        }
        catch (Exception)
        {
            res.Clear();
        }

        return res;
    }
}
