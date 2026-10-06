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
