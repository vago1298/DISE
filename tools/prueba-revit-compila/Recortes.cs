// ============================================================================
//  RECORTES DE LA REVIT API, SOLO PARA COMPILAR
//
//  Esto NO es la Revit API. Son declaraciones minimas de los tipos y miembros que usa
//  CadLink.Revit, escritas aqui para poder COMPILAR esos archivos en una maquina sin Revit
//  instalado.
//
//  QUE ATRAPA Y QUE NO
//
//  Atrapa: nombres mal escritos, argumentos de mas o de menos, tipos que no cuadran, usings
//  que faltan, y cualquier incoherencia interna del codigo del complemento. Eso es la mayoria
//  de los errores de compilacion, y son los que obligaban a descubrir el fallo en la maquina
//  del usuario de uno en uno.
//
//  NO atrapa: que una firma de aqui no coincida con la de la Revit API de verdad. Si aqui se
//  declara un miembro que no existe, el codigo compilara contra este recorte y fallara alla.
//  Por eso cada miembro lleva anotada la version en que se comprobo y de donde.
//
//  ESTE ARCHIVO ES LA LISTA DE TODO LO QUE EL COMPLEMENTO SUPONE DE REVIT. Leerlo es la forma
//  mas rapida de revisar esas suposiciones sin leer el complemento entero.
//
//  Ya sirvio: la primera version del complemento usaba Floor.SlabShapeEditor, que en Revit
//  2026 no existe -da CS1061- porque el miembro cambio de sitio al reorganizarse la jerarquia
//  de suelos y toposolidos. Se cambio por la sobrecarga de Floor.Create con flecha de
//  pendiente, que si esta documentada para 2026.
// ============================================================================

using System.Collections;

namespace Autodesk.Revit.DB;

public sealed class ElementId
{
    public ElementId(long id) => Value = id;

    public long Value { get; }

    public static bool operator ==(ElementId? a, ElementId? b) => Equals(a?.Value, b?.Value);

    public static bool operator !=(ElementId? a, ElementId? b) => !(a == b);

    public override bool Equals(object? o) => o is ElementId e && e.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();
}

public sealed class XYZ
{
    public XYZ(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public double DistanceTo(XYZ otro) => 0;
}

public abstract class Curve
{
    public XYZ GetEndPoint(int i) => new(0, 0, 0);
}

public sealed class Line : Curve
{
    public static Line CreateBound(XYZ a, XYZ b) => new();
}

public sealed class CurveLoop
{
    public static CurveLoop Create(IList<Curve> curvas) => new();
}

public enum StorageType { None, Integer, Double, String, ElementId }

public sealed class Parameter
{
    public StorageType StorageType => StorageType.Double;

    public double AsDouble() => 0;

    public string? AsString() => null;

    public bool Set(double v) => true;

    public bool Set(ElementId v) => true;

    public bool Set(string v) => true;
}

public abstract class Location { }

public sealed class LocationCurve : Location
{
    public Curve Curve { get; set; } = new Line();
}

public abstract class Element
{
    public ElementId Id => new(0);

    public string? Name { get; set; }

    public Location? Location => null;

    public Parameter? get_Parameter(BuiltInParameter p) => new();

    public Parameter? LookupParameter(string nombre) => new();

    public ElementId GetTypeId() => new(0);

    public void ChangeTypeId(ElementId tipo) { }
}

public abstract class ElementType : Element
{
    public string? FamilyName => null;
}

public sealed class Level : Element
{
    public double Elevation => 0;

    public static Level Create(Document doc, double elevacion) => new();
}

public sealed class FamilySymbol : ElementType
{
    public bool IsActive => false;

    public void Activate() { }

    /// <summary>Comprobado en la Revit API 2026: FamilySymbol.GetStructuralSection().</summary>
    public Structure.StructuralSection? GetStructuralSection() => null;
}

public sealed class FamilyInstance : Element { }

public enum WallKind { Basic, Curtain, Stacked, Unknown }

public sealed class WallType : ElementType
{
    public WallKind Kind => WallKind.Basic;

    public double Width => 0;
}

public sealed class Wall : Element
{
    /// <summary>
    /// Comprobado en la Revit API 2026: Wall.Create con CONTORNO, no con linea y altura.
    /// Es la que permite un pano de cualquier forma.
    /// </summary>
    public static Wall Create(
        Document doc, IList<Curve> profile, ElementId wallTypeId, ElementId levelId,
        bool structural) => new();
}

public sealed class CompoundStructure
{
    public double GetWidth() => 0;
}

public sealed class FloorType : ElementType
{
    public CompoundStructure? GetCompoundStructure() => null;
}

public sealed class Floor : Element
{
    /// <summary>Comprobado en la Revit API 2026: la sobrecarga sin pendiente.</summary>
    public static Floor Create(
        Document doc, IList<CurveLoop> profile, ElementId floorTypeId, ElementId levelId) =>
        new();

    /// <summary>
    /// Comprobado en la Revit API 2026: la sobrecarga con FLECHA DE PENDIENTE.
    /// El slopeArrow tiene que ser HORIZONTAL y el slope va en RADIANES; con slopeArrow nulo
    /// sale un suelo horizontal.
    /// </summary>
    public static Floor Create(
        Document doc, IList<CurveLoop> profile, ElementId floorTypeId, ElementId levelId,
        bool structural, Line? slopeArrow, double slope) => new();
}

public sealed class Document
{
    public bool IsFamilyDocument => false;

    public Creation Create => new();

    public Element? GetElement(ElementId id) => null;

    public void Regenerate() { }
}

public sealed class Creation
{
    public FamilyInstance NewFamilyInstance(
        Curve curva, FamilySymbol simbolo, Level nivel,
        Structure.StructuralType tipo) => new();

    public FamilyInstance NewFamilyInstance(
        XYZ punto, FamilySymbol simbolo, Level nivel,
        Structure.StructuralType tipo) => new();
}

public sealed class FilteredElementCollector : IEnumerable<Element>
{
    public FilteredElementCollector(Document doc) { }

    public FilteredElementCollector OfClass(Type t) => this;

    public FilteredElementCollector OfCategory(BuiltInCategory c) => this;

    public FilteredElementCollector WhereElementIsNotElementType() => this;

    public IEnumerator<Element> GetEnumerator() => new List<Element>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public enum BuiltInCategory
{
    OST_StructuralColumns,
    OST_StructuralFraming,
    OST_Walls,
    OST_Floors
}

public enum BuiltInParameter
{
    ALL_MODEL_INSTANCE_COMMENTS,
    STRUCTURAL_BEND_DIR_ANGLE,
    FAMILY_BASE_LEVEL_PARAM,
    FAMILY_BASE_LEVEL_OFFSET_PARAM,
    FAMILY_TOP_LEVEL_PARAM,
    FAMILY_TOP_LEVEL_OFFSET_PARAM
}

public static class UnitUtils
{
    public static double ConvertToInternalUnits(double v, ForgeTypeId unidad) => v;

    public static double ConvertFromInternalUnits(double v, ForgeTypeId unidad) => v;
}

public sealed class ForgeTypeId { }

public static class UnitTypeId
{
    public static ForgeTypeId Meters => new();
}

// ---------- Transacciones y manejo de fallos ----------

public sealed class Transaction : IDisposable
{
    public Transaction(Document doc, string nombre) { }

    public void Start() { }

    public void Commit() { }

    public void RollBack() { }

    public FailureHandlingOptions GetFailureHandlingOptions() => new();

    public void SetFailureHandlingOptions(FailureHandlingOptions o) { }

    public void Dispose() { }
}

public sealed class FailureHandlingOptions
{
    public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor p) => this;

    public FailureHandlingOptions SetClearAfterRollback(bool v) => this;
}

public enum FailureSeverity { None, Warning, Error, DocumentCorruption }

public enum FailureProcessingResult
{
    Continue,
    ProceedWithCommit,
    ProceedWithRollBack,
    WaitForUserInput
}

public sealed class FailureMessageAccessor
{
    public FailureSeverity GetSeverity() => FailureSeverity.Warning;

    public string? GetDescriptionText() => null;

    public bool HasResolutions() => false;

    public ICollection<ElementId>? GetFailingElementIds() => null;
}

public sealed class FailuresAccessor
{
    public IList<FailureMessageAccessor> GetFailureMessages() => new List<FailureMessageAccessor>();

    public void DeleteWarning(FailureMessageAccessor m) { }

    public void ResolveFailure(FailureMessageAccessor m) { }

    public void DeleteElements(ICollection<ElementId> ids) { }
}

public interface IFailuresPreprocessor
{
    FailureProcessingResult PreprocessFailures(FailuresAccessor acceso);
}
