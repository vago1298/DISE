// Recortes de las VISTAS del despiece. Lo de siempre: esto NO es la Revit API, son
// declaraciones para poder compilar Despiece.cs aqui. Ver Recortes.cs para el porque.
//
// SIN COMPROBAR contra la DLL de Revit 2025/2026: se escribio de la documentacion de la API.
// Si el complemento no compila alla, lo primero que hay que mirar es esto:
//   ViewSection.CreateSection(Document, ElementId, BoundingBoxXYZ)
//   TextNote.Create(Document, ElementId, XYZ, string, ElementId)
//   IndependentTag.Create(Document, ElementId, Reference, bool, TagMode, TagOrientation, XYZ)
//   ViewSheet.Create(Document, ElementId)
//   Viewport.Create(Document, ElementId, ElementId, XYZ) y Viewport.CanAddViewToSheet(...)

namespace Autodesk.Revit.DB;

public enum ViewFamily { Section, Detail, FloorPlan, Elevation }

public enum ViewDetailLevel { Coarse, Medium, Fine }

public enum ElementTypeGroup { TextNoteType }

public enum TagMode { TM_ADDBY_CATEGORY, TM_ADDBY_MULTICATEGORY }

public enum TagOrientation { Horizontal, Vertical }

public sealed class ViewFamilyType : ElementType
{
    public ViewFamily ViewFamily => ViewFamily.Section;
}

public sealed class Transform
{
    public static Transform Identity => new();

    public XYZ Origin { get; set; } = new(0, 0, 0);

    public XYZ BasisX { get; set; } = new(1, 0, 0);

    public XYZ BasisY { get; set; } = new(0, 1, 0);

    public XYZ BasisZ { get; set; } = new(0, 0, 1);

    /// <summary>Revit API: Transform.OfVector y OfPoint.</summary>
    public XYZ OfVector(XYZ v) => v;

    public XYZ OfPoint(XYZ p) => p;
}

public sealed class ViewSection : View
{
    public static ViewSection CreateSection(Document doc, ElementId tipo, BoundingBoxXYZ caja) => new();
}

public sealed class TextNote : Element
{
    public static TextNote Create(Document doc, ElementId vista, XYZ punto, string texto, ElementId tipo) => new();
}

public sealed class Reference
{
    public Reference(Element e) { }
}

public sealed class IndependentTag : Element
{
    public static IndependentTag Create(
        Document doc, ElementId vista, Reference referencia, bool conGuia,
        TagMode modo, TagOrientation orientacion, XYZ punto) => new();
}

public sealed class UV
{
    public double U => 0;

    public double V => 0;
}

public sealed class BoundingBoxUV
{
    public UV Min => new();

    public UV Max => new();
}

public sealed class ViewSheet : View
{
    public static ViewSheet Create(Document doc, ElementId cajetin) => new();
}

public sealed class Viewport : Element
{
    public static Viewport Create(Document doc, ElementId hoja, ElementId vista, XYZ punto) => new();

    public static bool CanAddViewToSheet(Document doc, ElementId hoja, ElementId vista) => true;
}

// Las COTAS del corte. Sin comprobar contra la DLL:
//   FamilyInstance.GetReferences(FamilyInstanceReferenceType) -> IList<Reference>
//   doc.Create.NewDimension(View, Line, ReferenceArray) -> Dimension
public enum FamilyInstanceReferenceType { Left, CenterLeftRight, Right, Front, CenterFrontBack, Back, Bottom, CenterElevation, Top }

public sealed class ReferenceArray
{
    public void Append(Reference r) { }
}

/// <summary>Revit API: Dimension.Value es double? -null en las de varios tramos-.</summary>
public sealed class Dimension : Element
{
    public double? Value => 0;
}

/// <summary>Revit API: SubTransaction(Document), Start, Commit, RollBack. Dentro de una Transaction.</summary>
public sealed class SubTransaction : IDisposable
{
    public SubTransaction(Document doc) { }

    public void Start() { }

    public void Commit() { }

    public void RollBack() { }

    public void Dispose() { }
}

/// <summary>Revit API: Options.ComputeReferences, DetailLevel, View.</summary>
public sealed class Options
{
    public bool ComputeReferences { get; set; }

    public ViewDetailLevel DetailLevel { get; set; }

    public View? View { get; set; }
}

public abstract class GeometryObject { }

/// <summary>Revit API: GeometryElement es IEnumerable&lt;GeometryObject&gt;.</summary>
public sealed class GeometryElement : GeometryObject, IEnumerable<GeometryObject>
{
    public IEnumerator<GeometryObject> GetEnumerator() => new List<GeometryObject>().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Revit API: GeometryInstance.GetSymbolGeometry() y Transform.</summary>
public sealed class GeometryInstance : GeometryObject
{
    public Transform Transform => Transform.Identity;

    public GeometryElement GetSymbolGeometry() => new();
}

public abstract class Face
{
    public Reference? Reference => null;
}

/// <summary>Revit API: PlanarFace.FaceNormal y Origin.</summary>
public sealed class PlanarFace : Face
{
    public XYZ FaceNormal => new(0, 0, 1);

    public XYZ Origin => new(0, 0, 0);
}

/// <summary>Revit API: FaceArray.Size y se recorre con foreach.</summary>
public sealed class FaceArray : IEnumerable<Face>
{
    public int Size => 0;

    public IEnumerator<Face> GetEnumerator() => new List<Face>().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class Solid : GeometryObject
{
    public FaceArray Faces => new();
}
