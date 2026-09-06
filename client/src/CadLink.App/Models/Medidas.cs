namespace CadLink.App.Models;

/// <summary>
/// Cuántos <b>decimales</b> admite una celda de medida.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dos decimales en todas las medidas, y tres en las longitudes de las elevaciones.</b> Es
/// lo que se pidió, y aquí está en un solo sitio para que el número de decimales que se
/// GUARDA sea el mismo que la celda ENSEÑA. Si no, la celda diría 1.23 y el plano se dibujaría
/// con 1.234: una diferencia de cuatro décimas de milímetro que no se ve en la hoja pero sí
/// sale en la cota.
/// </para>
/// <para>
/// El redondeo se aplica en <c>Row.Set</c>, que es por donde pasan TODAS las propiedades de
/// todas las filas, así que no hay forma de olvidarse de una.
/// </para>
/// <para>
/// <b>Las tres excepciones.</b> El largo del alzado lleva tres decimales porque es la medida
/// en metros de la que salen las elevaciones, y ahí el milímetro se captura. Los dos espesores
/// de los perfiles de acero también, y por un motivo distinto: salen del catálogo, donde un
/// alma de 1/4" son 0.635 cm exactos. Redondearlos a 0.64 cambiaría el perfil, y además la
/// fila dejaría de coincidir con el catálogo del que se eligió.
/// </para>
/// </remarks>
public static class Medidas
{
    /// <summary>Los decimales de una medida cualquiera.</summary>
    public const int Decimales = 2;

    /// <summary>Los de las longitudes de las elevaciones, donde se captura el milímetro.</summary>
    public const int DecimalesDeLongitud = 3;

    /// <summary>
    /// Las propiedades que admiten tres decimales. Cada una con su motivo, arriba.
    /// </summary>
    /// <remarks>
    /// Van por <c>nameof</c> y no como texto: si algún día se renombra la propiedad, el
    /// compilador obliga a renombrarla aquí también en lugar de dejar la excepción muerta.
    /// </remarks>
    private static readonly HashSet<string> ConTresDecimales = new(StringComparer.Ordinal)
    {
        nameof(SeccionConcretoRow.LongitudM),
        nameof(PerfilAceroRow.EspesorAlmaCm),
        nameof(PerfilAceroRow.EspesorPatinCm),
    };

    /// <summary>Los decimales que admite una propiedad.</summary>
    public static int DecimalesDe(string? propiedad) =>
        propiedad is not null && ConTresDecimales.Contains(propiedad)
            ? DecimalesDeLongitud
            : Decimales;

    /// <summary>
    /// El valor con los decimales que admite su celda.
    /// </summary>
    /// <remarks>
    /// <b>Se redondea al alza en el medio</b> -0.125 a 0.13- y no al par, que es lo que hace
    /// <c>Math.Round</c> por omisión y deja 0.12. En una medida el redondeo al par sorprende:
    /// el usuario teclea y ve otra cosa sin explicación.
    /// </remarks>
    public static double Redondear(double valor, string? propiedad)
    {
        if (double.IsNaN(valor) || double.IsInfinity(valor))
        {
            return valor;
        }

        return Math.Round(valor, DecimalesDe(propiedad), MidpointRounding.AwayFromZero);
    }
}
