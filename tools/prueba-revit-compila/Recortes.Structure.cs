// Recortes del espacio Autodesk.Revit.DB.Structure. Van en archivo aparte porque C# no
// permite mezclar en el mismo archivo un namespace con punto y coma y otro con llaves.
//
// Lo de siempre: esto NO es la Revit API, son declaraciones para poder compilar. Ver
// Recortes.cs para el porque.

namespace Autodesk.Revit.DB.Structure;

public enum StructuralType { NonStructural, Beam, Column, Brace, Footing }

public sealed class StructuralSection
{
    /// <summary>
    /// El complemento solo usa su ToString(), a proposito, para no escribir en el codigo el
    /// nombre de ningun miembro de un enum que cambia entre versiones de Revit.
    /// </summary>
    public object StructuralSectionShape => string.Empty;
}


// La justificacion de una viga. Comprobado en la Revit API 2026 contra dos fuentes:
//
//   - el exportador de IFC de Autodesk -Revit.IFC.Export, FamilyInstanceExporter- lee
//     BuiltInParameter.Y_JUSTIFICATION y Z_JUSTIFICATION con AsInteger() y compara con
//     (int)YJustification.X / (int)ZJustification.X para deducir el punto cardinal;
//   - rhino.inside-revit los ESCRIBE en una viga recien creada.
//
// De ahi sale tambien la equivalencia que usa el complemento: YJustification.Origin junto con
// ZJustification.Top es el punto cardinal 8, "top center", que es el punto de insercion por
// omision de una viga en ETABS.
public enum YJustification { Left, Origin, Center, Right }

public enum ZJustification { Bottom, Origin, Center, Top }
