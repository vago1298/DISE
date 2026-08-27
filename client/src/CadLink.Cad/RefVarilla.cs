namespace CadLink.Cad;

/// <summary>
/// A qué <b>grupo</b> de varillas pertenece una varilla de la sección.
/// </summary>
/// <remarks>
/// <para>
/// Los valores son los cinco repartos con los que se arma una sección: los cuatro
/// lechos —esquina e intermedio, arriba y abajo— y las laterales, que van en un reparto
/// aparte porque salen en los dos costados a la vez.
/// </para>
/// <para>
/// <b>Los números son fijos y no se deben reordenar.</b> Se guardan tal cual en el
/// archivo del proyecto, así que cambiar el orden movería las grapas de sitio en los
/// proyectos ya guardados.
/// </para>
/// <para>
/// <b>Vive en la capa de CAD, y no en la aplicación, a propósito.</b> Lo necesitan los
/// dos lados: la vista previa, que traduce un clic a una varilla, y el dibujante de
/// AutoCAD, que traduce esa misma señal a un círculo del plano. Con una copia en cada
/// proyecto, el día que alguien agregara un grupo en un lado y no en el otro las grapas
/// se dibujarían en el plano agarradas de varillas distintas de las que se ven en
/// pantalla, y sin ningún error que lo delatara.
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
