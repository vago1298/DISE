// Recortes del ARMADO: Autodesk.Revit.DB.Structure. Lo de siempre: esto NO es la Revit API,
// son declaraciones para poder compilar Armador.cs aqui. Ver Recortes.cs para el porque.
//
// Todo lo de este archivo esta SIN COMPROBAR contra la DLL de Revit 2025/2026: se escribio de
// la documentacion de la API (RevitAPI.chm, revitapidocs.com). Es la lista de lo que el armado
// supone de Revit, y lo primero que hay que mirar si el complemento no compila alla.

namespace Autodesk.Revit.DB.Structure;

/// <summary>Standard para las longitudinales, StirrupTie para los estribos.</summary>
public enum RebarStyle { Standard, StirrupTie }

/// <summary>De que lado de la varilla dobla el gancho, mirando con la normal hacia uno.</summary>
public enum RebarHookOrientation { Left, Right }

/// <summary>Un tipo de varilla. BarNominalDiameter y BarModelDiameter existen desde 2023.</summary>
public sealed class RebarBarType : ElementType
{
    public static RebarBarType Create(Document doc) => new();

    public double BarNominalDiameter { get; set; }

    public double BarModelDiameter { get; set; }
}

/// <summary>Un tipo de gancho: RebarHookType.Create(Document, angulo en radianes, multiplo).</summary>
public sealed class RebarHookType : ElementType
{
    public static RebarHookType Create(Document doc, double angulo, double multiplo) => new();

    public RebarStyle Style { get; set; }
}

/// <summary>RebarHostData.GetRebarHostData(Element) e IsValidHost().</summary>
public sealed class RebarHostData
{
    public static RebarHostData? GetRebarHostData(Element anfitrion) => new();

    public bool IsValidHost() => true;
}

/// <summary>El reparto de un juego de varillas.</summary>
public sealed class RebarShapeDrivenAccessor
{
    public void SetLayoutAsNumberWithSpacing(
        int cuantas, double separacion, bool haciaLaNormal, bool conLaPrimera, bool conLaUltima) { }
}

/// <summary>
/// Una varilla. Rebar.CreateFromCurves con la firma de doce argumentos: documento, estilo, tipo,
/// gancho de arranque, gancho final, anfitrion, normal, curvas, las dos orientaciones,
/// usar forma existente, crear forma nueva.
/// </summary>
public sealed class Rebar : Element
{
    public static Rebar CreateFromCurves(
        Document doc, RebarStyle estilo, RebarBarType tipo,
        RebarHookType? ganchoIni, RebarHookType? ganchoFin, Element anfitrion, XYZ normal,
        IList<Curve> curvas, RebarHookOrientation orientIni, RebarHookOrientation orientFin,
        bool usarFormaExistente, bool crearFormaNueva) => new();

    public RebarShapeDrivenAccessor GetShapeDrivenAccessor() => new();

    // Los ganchos por punta y el eje, para el armado de muros con ganchos distintos. SIN COMPROBAR:
    //   Rebar.SetHookTypeId(int, ElementId), GetHookOrientation(int), SetHookOrientation(int, ...)
    //   Rebar.GetCenterlineCurves(bool, bool, bool, MultiplanarOption, int)
    public void SetHookTypeId(int extremo, ElementId gancho) { }

    public RebarHookOrientation GetHookOrientation(int extremo) => RebarHookOrientation.Left;

    public void SetHookOrientation(int extremo, RebarHookOrientation o) { }

    public IList<Curve> GetCenterlineCurves(
        bool ajustarCruces, bool sinGanchos, bool sinDobleces, MultiplanarOption opcion, int barra) => new List<Curve>();
}

public enum MultiplanarOption { IncludeOnlyPlanarCurves, IncludeAllMultiplanarCurves }

// El ARMADO DE MUROS. SIN COMPROBAR contra la DLL: de la documentacion de la API.
//   AreaReinforcement.Create(Document, Element anfitrion, IList<Curve> contorno, XYZ direccionMayor,
//       ElementId tipoDeArea, ElementId tipoDeVarilla, ElementId tipoDeGancho)
//   Y los BuiltInParameter REBAR_SYSTEM_* de las cuatro capas, en Recortes.cs.
public sealed class AreaReinforcementType : ElementType { }

public sealed class AreaReinforcement : Element
{
    public static AreaReinforcement Create(
        Document doc, Element anfitrion, IList<Curve> contorno, XYZ direccionMayor,
        ElementId tipoDeArea, ElementId tipoDeVarilla, ElementId tipoDeGancho) => new();

    /// <summary>Revit API: AreaReinforcement.RemoveAreaReinforcementSystem(Document, AreaReinforcement). SIN COMPROBAR.</summary>
    public static IList<ElementId> RemoveAreaReinforcementSystem(Document doc, AreaReinforcement area) => new List<ElementId>();
}
