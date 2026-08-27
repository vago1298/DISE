namespace CadLink.App.Models;

/// <summary>
/// A qué <b>grupo</b> de varillas pertenece una varilla de la sección.
/// </summary>
/// <remarks>
/// <para>
/// Los valores son los cinco repartos que calcula la vista previa: los cuatro lechos
/// —esquina e intermedio, arriba y abajo— y las laterales, que salen de un reparto
/// aparte porque van en los dos costados a la vez.
/// </para>
/// <para>
/// <b>Los números son fijos y no se deben reordenar.</b> Se guardan tal cual en el
/// archivo del proyecto, así que cambiar el orden movería las grapas de sitio en los
/// proyectos ya guardados.
/// </para>
/// </remarks>
public enum LechoVarilla
{
    EsquinaSuperior = 0,
    IntermediaSuperior = 1,
    EsquinaInferior = 2,
    IntermediaInferior = 3,

    /// <summary>
    /// Las laterales de los <b>dos</b> costados, en el orden en que las reparte la
    /// sección: izquierda y derecha alternadas, de abajo hacia arriba.
    /// </summary>
    Lateral = 4
}

/// <summary>
/// Señala <b>una</b> varilla de la sección: su grupo y su posición dentro del grupo.
/// </summary>
/// <remarks>
/// <para>
/// Se guarda así, y <b>no</b> como coordenadas ni como un índice global, a propósito.
/// </para>
/// <para>
/// Con coordenadas, cambiar la base, el peralte o el recubrimiento dejaría la grapa
/// colgada en el aire, porque las varillas se recalculan pero el punto guardado no.
/// Con un índice global sobre la lista de todas las varillas, subir de 3 a 4 las
/// varillas del lecho superior correría la numeración de todas las de abajo y las
/// grapas saltarían a varillas que no son.
/// </para>
/// <para>
/// Guardando grupo e índice, la grapa sigue significando lo mismo —«la tercera del
/// lecho inferior»— aunque la sección cambie de tamaño, y si ese lecho se queda con
/// menos varillas de las que hacen falta, la grapa se descarta sola en lugar de
/// señalar a otra.
/// </para>
/// </remarks>
/// <param name="Lecho">El grupo de varillas.</param>
/// <param name="Indice">La posición dentro del grupo, empezando en 0.</param>
public readonly record struct RefVarilla(LechoVarilla Lecho, int Indice);

/// <summary>
/// Una <b>grapa</b>: el estribo suplementario que une dos varillas longitudinales,
/// con un gancho en cada punta.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que en obra se llama grapa y en los planos aparece como estribo
/// suplementario o <i>crosstie</i>: una varilla recta con un doblez en cada extremo
/// que se agarra de dos varillas del armado para arriostrarlas.
/// </para>
/// <para>
/// El diámetro se guarda como la <b>clave</b> de la varilla —<c>#3</c>, <c>#4</c>…— y
/// no en centímetros, igual que todos los demás diámetros del programa, para que haya
/// una sola tabla de conversión: <see cref="Varilla.TryDiametroCm"/>.
/// </para>
/// </remarks>
public sealed class GrapaSeccion
{
    /// <summary>Una de las dos varillas que la grapa agarra.</summary>
    public RefVarilla A { get; init; }

    /// <summary>La otra.</summary>
    public RefVarilla B { get; init; }

    /// <summary>Clave de la varilla con la que se arma la grapa.</summary>
    public string Diametro { get; set; } = "#3";

    /// <summary>
    /// ¿Esta grapa une justo esas dos varillas, en cualquier orden?
    /// </summary>
    /// <remarks>
    /// Sin orden a propósito: una grapa entre la varilla 1 y la 5 es la MISMA que
    /// entre la 5 y la 1. Es lo que permite que volver a marcar el mismo par la
    /// quite, en vez de acabar con dos grapas encimadas que se ven como una sola y
    /// no hay manera de borrar.
    /// </remarks>
    public bool Une(RefVarilla x, RefVarilla y) =>
        (A.Equals(x) && B.Equals(y)) || (A.Equals(y) && B.Equals(x));
}
