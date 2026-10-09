using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Paso de metros a las unidades internas de Revit.</summary>
/// <remarks>
/// <para>
/// Revit trabaja por dentro en <b>pies decimales</b>, SIEMPRE, sin importar las unidades que
/// muestre el proyecto. Todo lo que entra o sale de la API pasa por aqui.
/// </para>
/// <para>
/// Es el error mas facil de cometer y el mas facil de no ver: un modelo metido en pies como
/// si fueran metros sale con la escala multiplicada por 3.28, y como todo queda proporcionado,
/// en pantalla parece bien hasta que alguien acota.
/// </para>
/// </remarks>
internal static class Unidades
{
    public static double AInternas(double metros) =>
        UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);

    public static double AMetros(double internas) =>
        UnitUtils.ConvertFromInternalUnits(internas, UnitTypeId.Meters);

    public static XYZ Punto(PuntoJson p) =>
        new(AInternas(p.X), AInternas(p.Y), AInternas(p.Z));

    public static PuntoJson DePunto(XYZ p) => new()
    {
        X = AMetros(p.X),
        Y = AMetros(p.Y),
        Z = AMetros(p.Z)
    };
}
