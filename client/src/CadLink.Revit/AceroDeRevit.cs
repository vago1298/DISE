#if ACERO_REVIT
// Los mismos usings que las muestras SampleCommandsSteelElements del SDK de Revit 2025.
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.CADLink.Database;
using Autodesk.SteelConnectionsDB;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;
using AsPlane = Autodesk.AdvanceSteel.Geometry.Plane;
using Point3d = Autodesk.AdvanceSteel.Geometry.Point3d;
using Vector3d = Autodesk.AdvanceSteel.Geometry.Vector3d;
#endif
using Autodesk.Revit.DB;

namespace CadLink.Revit;

/// <summary>Un ancla para el acero de Revit: su eje en la cara de arriba de la placa, diametro y largo en m.</summary>
internal sealed record AnclaNativa(XYZ EnLaCara, double DiametroM, double LargoM);

/// <summary>
/// Una placa para el acero de Revit (Structure > Connection: Plate, Anchors). En unidades internas: el
/// centro de su cara de abajo, sus dos ejes, sus lados y su espesor.
/// </summary>
/// <param name="Normal">Hacia donde crece el espesor: arriba en una placa base, hacia la viga en una embebida.</param>
internal sealed record PlacaNativa(string Nombre, XYZ CentroAbajo, XYZ Ex, XYZ Ey, double LadoX, double LadoY, double Espesor,
    List<AnclaNativa> Anclas, XYZ? Normal = null)
{
    public XYZ N => Normal ?? XYZ.BasisZ;
}

/// <summary>
/// Las placas y anclas como ELEMENTOS DE ACERO DE REVIT -los mismos de Structure > Connection > Plate y
/// Anchors-, no como formas fijas: se seleccionan, se filtran por su categoria (Structural Plates,
/// Structural Anchors), se editan en Properties y salen en las tablas.
/// </summary>
/// <remarks>
/// <para>
/// Se hacen con la API de Advance Steel que trae Revit (la de Steel Connections), dentro de una
/// <c>FabricationTransaction</c>, que es la que pide esa API y no puede ir dentro de otra transaccion: por
/// eso va DESPUES de la del boton, y son dos Ctrl+Z.
/// </para>
/// <para>
/// Solo se compila si al instalar se encontro Steel Connections (ACERO_REVIT). Sin el, el boton sigue
/// con las formas de siempre y lo dice. Las medidas de Advance Steel van en mm.
/// </para>
/// </remarks>
internal static class AceroDeRevit
{
#if ACERO_REVIT
    public static bool Disponible => true;
#else
    public static bool Disponible => false;
#endif

    public static void Placas(Document doc, IReadOnlyList<PlacaNativa> placas, ResultadoDetalles r)
    {
        if (placas.Count == 0)
        {
            return;
        }

#if ACERO_REVIT
        try
        {
            Crear(doc, placas, r);
        }
        catch (Exception e)
        {
            // Un FileNotFound aqui es que Steel Connections no esta cargado en Revit.
            r.Errores.Add("«acero de Revit»: No se pudieron crear las placas y anclas nativas: " + e.Message
                          + ". Revisa que el complemento Steel Connections este activo (Add-Ins).");
        }
#else
        r.Errores.Add("«acero de Revit»: Este complemento se compilo sin Steel Connections, asi que no puede crear "
                      + "placas y anclas nativas. Instala/activa Steel Connections for Revit y vuelve a correr "
                      + "7-instalar-plugin-revit.bat.");
#endif
    }

#if ACERO_REVIT
    private const double Mm = 304.8;

    private static Point3d P(XYZ p) => new(p.X * Mm, p.Y * Mm, p.Z * Mm);

    private static Vector3d V(XYZ v) => new(v.X, v.Y, v.Z);

    private static void Crear(Document doc, IReadOnlyList<PlacaNativa> placas, ResultadoDetalles r)
    {
        using var t = new FabricationTransaction(doc, false, "Placas y anclas de acero de Revit (CadLink)");

        foreach (var p in placas)
        {
            // No se encima con la que ya se puso: esa se edita en Properties o se borra a mano.
            if (YaHay(doc, p.CentroAbajo + (p.N * (p.Espesor / 2)), p.Espesor))
            {
                r.Avisos.Add($"«{p.Nombre}»: ya tiene una placa de acero de Revit; no se puso otra. Editala en Properties o borrala y vuelve a ponerla.");
                continue;
            }

            try
            {
                // La placa, como poligono en el plano de su eje medio y centrada en el (Portioning 0.5).
                var medio = p.CentroAbajo + (p.N * (p.Espesor / 2));
                var hx = p.Ex * (p.LadoX / 2);
                var hy = p.Ey * (p.LadoY / 2);
                var esquinas = new[] { medio - hx - hy, medio + hx - hy, medio + hx + hy, medio - hx + hy }.Select(P).ToArray();

                var placa = new Plate(new AsPlane(P(medio), V(p.N)), esquinas, p.Espesor * Mm);
                placa.Portioning = 0.5;
                placa.WriteToDb();

                var hechas = 0;

                foreach (var a in p.Anclas)
                {
                    try
                    {
                        // Una por punto, con Nx = Ny = 1: asi sale exactamente donde va cada una, tambien en
                        // las placas con anclas solo en el perimetro.
                        var q = P(a.EnLaCara);
                        var ancla = new AnchorPattern(q, q, V(p.Ex), V(p.Ey))
                        {
                            Nx = 1,
                            Ny = 1
                        };

                        // El diametro y el largo, si la norma del proyecto los tiene; si no, los de omision.
                        try { ancla.ScrewDiameter = a.DiametroM * 1000; } catch (Exception) { }
                        try { if (a.LargoM > 0) { ancla.ScrewLength = a.LargoM * 1000; } } catch (Exception) { }

                        ancla.WriteToDb();
                        ancla.Connect(new FilerObject[] { placa }, AtomicElement.eAssemblyLocation.kOnSite);
                        hechas++;
                    }
                    catch (Exception e)
                    {
                        r.Errores.Add($"«{p.Nombre}»: un ancla de acero de Revit no se pudo crear: {e.Message}");
                    }
                }

                r.Avisos.Add($"«{p.Nombre}»: placa y {hechas} ancla(s) como ACERO DE REVIT (Structural Plates / Anchors): "
                             + "editables en Properties. Si el ancla sale hacia arriba, seleccionala y voltea su direccion.");
            }
            catch (Exception e)
            {
                r.Errores.Add($"«{p.Nombre}»: la placa de acero de Revit no se pudo crear: {e.Message}");
            }
        }

        t.Commit();
    }
#endif

    /// <summary>¿Ya hay una placa de acero de Revit en este lugar? Para no ponerla dos veces.</summary>
    public static bool YaHay(Document doc, XYZ centro, double radio)
    {
        try
        {
            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructConnectionPlates)
                .WhereElementIsNotElementType()
                .Any(e => e.get_BoundingBox(null) is { } c
                          && c.Min.X - radio <= centro.X && centro.X <= c.Max.X + radio
                          && c.Min.Y - radio <= centro.Y && centro.Y <= c.Max.Y + radio
                          && c.Min.Z - radio <= centro.Z && centro.Z <= c.Max.Z + radio);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
