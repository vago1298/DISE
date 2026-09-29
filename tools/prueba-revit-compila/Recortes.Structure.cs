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
