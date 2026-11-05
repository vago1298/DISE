// Recortes de las PLACAS BASE, las ZAPATAS y las SOLDADURAS. Lo de siempre: esto NO es la Revit
// API, son declaraciones para poder compilar Solidos.cs y Detallador.cs aqui. Ver Recortes.cs.
//
// SIN COMPROBAR contra la DLL de Revit 2025/2026: se escribio de la documentacion de la API.
// Si el complemento no compila alla, lo primero que hay que mirar es esto:
//   DirectShape.CreateElement(Document, ElementId), IsValidCategoryId(ElementId, Document),
//     ApplicationId, ApplicationDataId y SetShape(IList<GeometryObject>)
//   GeometryCreationUtilities.CreateExtrusionGeometry(IList<CurveLoop>, XYZ, double, SolidOptions)
//   GeometryCreationUtilities.CreateBlendGeometry(CurveLoop, CurveLoop, ICollection<VertexPair>, SolidOptions)
//   BooleanOperationsUtils.ExecuteBooleanOperation(Solid, Solid, BooleanOperationsType)
//   Material.Create(Document, string) -> ElementId, y Material.Color

namespace Autodesk.Revit.DB;

public sealed class DirectShape : Element
{
    public static DirectShape CreateElement(Document doc, ElementId categoria) => new();

    public static bool IsValidCategoryId(ElementId categoria, Document doc) => true;

    public string ApplicationId { get; set; } = string.Empty;

    public string ApplicationDataId { get; set; } = string.Empty;

    public void SetShape(IList<GeometryObject> forma) { }
}

public sealed class SolidOptions
{
    public SolidOptions(ElementId material, ElementId estiloGrafico) { }
}

public sealed class VertexPair { }

public static class GeometryCreationUtilities
{
    public static Solid CreateExtrusionGeometry(
        IList<CurveLoop> perfil, XYZ direccion, double distancia, SolidOptions opciones) => new();

    public static Solid CreateBlendGeometry(
        CurveLoop primero, CurveLoop segundo, ICollection<VertexPair>? pares, SolidOptions opciones) => new();
}

public enum BooleanOperationsType { Union, Difference, Intersect }

public static class BooleanOperationsUtils
{
    public static Solid ExecuteBooleanOperation(Solid a, Solid b, BooleanOperationsType tipo) => a;
}

public sealed class Color
{
    public Color(byte rojo, byte verde, byte azul) { }
}

public sealed class Material : Element
{
    public static ElementId Create(Document doc, string nombre) => new(0L);

    public Color Color { get; set; } = new(0, 0, 0);
}
