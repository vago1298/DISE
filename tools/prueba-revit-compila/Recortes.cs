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
//  LOS NOMBRES DE LOS PARAMETROS DE AQUI NO SON LOS DE REVIT. El nombre de un parametro solo
//  se puede leer de la DLL real, y aqui no hay ninguna. Por eso los parametros de este archivo
//  van EN ESPAÑOL a proposito: asi ningun argumento con nombre que se escriba en el
//  complemento puede coincidir por casualidad con el recorte y pasar la compilacion de aqui
//  para fallar en la maquina del usuario. En el complemento los argumentos van posicionales, y
//  validar.py §26 lo comprueba.
//
//  Ya sirvio de aviso: el complemento llamaba a Floor.Create con "structural: true" y en la
//  Revit API 2026 ese parametro no se llama asi -da CS1739-, pero el recorte tambien lo
//  llamaba "structural" y por eso compilaba aqui.
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

    public static ElementId InvalidElementId => new(-1);

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

    /// <summary>El vector unitario vertical. Con el se arma el eje de giro de una columna.</summary>
    public static XYZ BasisZ => new(0, 0, 1);

    public double DistanceTo(XYZ otro) => 0;

    public static XYZ operator +(XYZ a, XYZ b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Revit API: XYZ.DotProduct(XYZ).</summary>
    public double DotProduct(XYZ otro) => 0;
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

    public bool IsReadOnly => false;

    public double AsDouble() => 0;

    public string? AsString() => null;

    /// <summary>Revit API: Parameter.AsElementId().</summary>
    public ElementId AsElementId() => ElementId.InvalidElementId;

    public bool Set(double v) => true;

    public bool Set(int v) => true;

    public bool Set(ElementId v) => true;

    public bool Set(string v) => true;
}

public abstract class Location { }

public sealed class LocationCurve : Location
{
    public Curve Curve { get; set; } = new Line();
}

/// <summary>
/// La ubicacion de una columna colocada por punto. Revit API 2025/2026: LocationPoint.Point.
/// La usa el armado para el centro de la columna.
/// </summary>
public sealed class LocationPoint : Location
{
    public XYZ Point => new(0, 0, 0);
}

/// <summary>
/// La caja de un elemento. Revit API 2025/2026: Element.get_BoundingBox(View) devuelve un
/// BoundingBoxXYZ con Min y Max en coordenadas del modelo cuando la vista es null.
/// </summary>
public sealed class BoundingBoxXYZ
{
    public XYZ Min { get; set; } = new(0, 0, 0);

    public XYZ Max { get; set; } = new(0, 0, 0);

    /// <summary>El sistema de la caja: con el se orienta la vista de corte.</summary>
    public Transform Transform { get; set; } = Transform.Identity;
}

public abstract class Element
{
    public ElementId Id => new(0);

    public string? Name { get; set; }

    public Location? Location => null;

    public Parameter? get_Parameter(BuiltInParameter p) => new();

    public Parameter? LookupParameter(string nombre) => new();

    /// <summary>Revit API 2025/2026: Element.get_BoundingBox(View); con null, la del modelo.</summary>
    public BoundingBoxXYZ? get_BoundingBox(View? vista) => new();

    public ElementId GetTypeId() => new(0);

    public void ChangeTypeId(ElementId tipo) { }

    /// <summary>Revit API: Element.IsValidObject y get_Geometry(Options).</summary>
    public bool IsValidObject => true;

    public GeometryElement? get_Geometry(Options o) => new();

}

/// <summary>Una vista. El complemento la usa para encender las burbujas de los ejes.</summary>
public class View : Element
{
    public bool IsTemplate => false;

    // Lo que el despiece le pone a su corte. Sin comprobar contra la DLL.
    public int Scale { get; set; }

    public ViewDetailLevel DetailLevel { get; set; }

    public bool CropBoxActive { get; set; }

    public bool CropBoxVisible { get; set; }

    /// <summary>Revit API: View.RightDirection, la derecha de la vista en el modelo.</summary>
    public XYZ RightDirection => new(1, 0, 0);

    /// <summary>El contorno de la hoja, en pies de papel.</summary>
    public BoundingBoxUV Outline => new();
}

public abstract class ElementType : Element
{
    public string? FamilyName => null;

    /// <summary>Revit API: ElementType.Duplicate(string) devuelve el ElementType nuevo.</summary>
    public ElementType Duplicate(string nombre) => new FamilySymbol();
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

/// <summary>
/// Revit API 2025/2026: FamilyInstance.HandOrientation y FacingOrientation son las direcciones
/// X e Y de la familia colocada. El armado las usa para los lados de la columna.
/// </summary>
public sealed class FamilyInstance : Element
{
    public XYZ HandOrientation => new(1, 0, 0);

    public XYZ FacingOrientation => new(0, 1, 0);

    /// <summary>Los planos de referencia de la familia, para acotar.</summary>
    public IList<Reference> GetReferences(FamilyInstanceReferenceType tipo) => new List<Reference>();
}

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
        Document doc, IList<Curve> contorno, ElementId tipoDeMuro, ElementId nivel,
        bool estructural) => new();

    /// <summary>
    /// Revit API: Wall.Create(Document, Curve, ElementId tipo, ElementId nivel, double altura,
    /// double desfase, bool voltear, bool estructural). El muro recto con su altura y su base.
    /// </summary>
    public static Wall Create(
        Document doc, Curve linea, ElementId tipoDeMuro, ElementId nivel, double altura,
        double desfase, bool voltear, bool estructural) => new();
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
        Document doc, IList<CurveLoop> contorno, ElementId tipoDeSuelo, ElementId nivel) =>
        new();

    /// <summary>
    /// Comprobado en la Revit API 2026: la sobrecarga con FLECHA DE PENDIENTE.
    /// El slopeArrow tiene que ser HORIZONTAL y el slope va en RADIANES; con slopeArrow nulo
    /// sale un suelo horizontal.
    /// </summary>
    public static Floor Create(
        Document doc, IList<CurveLoop> contorno, ElementId tipoDeSuelo, ElementId nivel,
        bool estructural, Line? flechaDePendiente, double pendienteRad) => new();
}

public sealed class Document
{
    public bool IsFamilyDocument => false;

    public Creation Create => new();

    public Element? GetElement(ElementId id) => null;

    public void Regenerate() { }

    /// <summary>Revit API: Document.GetDefaultElementTypeId(ElementTypeGroup). Para el tipo de texto.</summary>
    public ElementId GetDefaultElementTypeId(ElementTypeGroup grupo) => new(0);

    /// <summary>Revit API 2025/2026: Document.Delete(ICollection&lt;ElementId&gt;).</summary>
    public ICollection<ElementId> Delete(ICollection<ElementId> ids) => ids;
}

public sealed class Creation
{
    /// <summary>Revit API: Creation.Document.NewDimension(View, Line, ReferenceArray).</summary>
    public Dimension NewDimension(View vista, Line linea, ReferenceArray referencias) => new();

    /// <summary>Revit API: Creation.Document.NewDetailCurve(View, Curve). Una linea de detalle.</summary>
    public DetailCurve NewDetailCurve(View vista, Curve curva) => new();

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

    public FilteredElementCollector WhereElementIsElementType() => this;

    public ElementId FirstElementId() => ElementId.InvalidElementId;

    public IEnumerator<Element> GetEnumerator() => new List<Element>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public enum BuiltInCategory
{
    OST_StructuralColumns,
    OST_StructuralFraming,
    OST_Walls,
    OST_Floors,

    // El cajetin de las hojas del despiece.
    OST_TitleBlocks
}

public enum BuiltInParameter
{
    ALL_MODEL_INSTANCE_COMMENTS,
    STRUCTURAL_BEND_DIR_ANGLE,
    FAMILY_BASE_LEVEL_PARAM,
    FAMILY_BASE_LEVEL_OFFSET_PARAM,
    FAMILY_TOP_LEVEL_PARAM,
    FAMILY_TOP_LEVEL_OFFSET_PARAM,

    // La justificacion de una viga. Comprobada en la Revit API 2026 contra el exportador de
    // IFC de Autodesk, que los lee con AsInteger(), y contra rhino.inside-revit, que los
    // escribe en una viga recien creada. Ver Recortes.Structure.cs.
    Y_JUSTIFICATION,
    Z_JUSTIFICATION,

    // Los de cada extremo, para cuando "yz Justification" esta en Independent y los de arriba
    // se ignoran. Confirmados en la misma lista generada para 2026.
    START_Z_JUSTIFICATION,
    END_Z_JUSTIFICATION,

    // El desfase de nivel de cada extremo de una viga. Confirmados en la misma lista de 2026:
    // STRUCTURAL_BEAM_END0_ELEVATION y STRUCTURAL_BEAM_END1_ELEVATION.
    STRUCTURAL_BEAM_END0_ELEVATION,
    STRUCTURAL_BEAM_END1_ELEVATION,

    // Las propiedades de TIPO que escribe el despiece: Nota clave, Modelo, Descripcion y Marca
    // de tipo. COMPROBADAS: compilaron contra la DLL de Revit del usuario. El Codigo de montaje NO
    // va aqui: UNIFORMAT_CODE no existe en su Revit, y Despiece.cs lo busca por nombre en texto.
    KEYNOTE_PARAM,
    ALL_MODEL_MODEL,
    ALL_MODEL_DESCRIPTION,
    ALL_MODEL_TYPE_MARK
}

/// <summary>
/// Girar elementos. Comprobado en la Revit API 2026: es con lo que se gira una columna
/// colocada por punto, que no tiene parametro de rotacion de seccion.
/// </summary>
public static class ElementTransformUtils
{
    public static void RotateElement(Document doc, ElementId id, Line eje, double angulo) { }

    /// <summary>Revit API: ElementTransformUtils.MoveElement(Document, ElementId, XYZ).</summary>
    public static void MoveElement(Document doc, ElementId id, XYZ desplazamiento) { }
}

/// <summary>
/// Una rejilla de eje. Comprobado en la Revit API 2026: <c>Grid.Create(Document, Line)</c> es
/// la forma documentada de crear un eje recto, y es la que usa el propio SDK de Autodesk en su
/// ejemplo GridCreation. La linea tiene que estar en un plano horizontal.
/// </summary>
public sealed class Grid : DatumPlane
{
    public static Grid Create(Document doc, Line linea) => new();
}

/// <summary>
/// Un plano de referencia: de aqui heredan los ejes y los niveles la visibilidad de su
/// burbuja. Comprobado en la Revit API 2026: ShowBubbleInView(DatumEnds, View) es lo que usan
/// el SDK de Autodesk y rhino.inside-revit para encender la burbuja de un extremo.
/// </summary>
public abstract class DatumPlane : Element
{
    public void ShowBubbleInView(DatumEnds extremo, View vista) { }

    public void HideBubbleInView(DatumEnds extremo, View vista) { }

    public bool IsBubbleVisibleInView(DatumEnds extremo, View vista) => false;
}

/// <summary>Los dos extremos de un eje o un nivel.</summary>
public enum DatumEnds { End0, End1 }

/// <summary>Una vista de planta.</summary>
public sealed class ViewPlan : View { }

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
