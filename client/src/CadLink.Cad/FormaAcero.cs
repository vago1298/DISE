namespace CadLink.Cad;

/// <summary>
/// Las <b>nueve formas</b> de perfil de acero que sabe dibujar <see cref="SeccionDrawer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>La forma no es la familia, y separarlas es lo que arregla el desplegable.</b> La
/// familia es la lista en la que el usuario busca su perfil y el nombre con el que se
/// rotula; la forma es la geometría que se traza. Cuatro familias del manual IMCA —IR, IS,
/// IC y S— comparten la forma <see cref="I"/>, y con ellas mezcladas en una sola familia el
/// desplegable de la IR ofrecía 573 perfiles de cuatro nomenclaturas distintas.
/// </para>
/// <para>
/// Estas constantes viven en el proyecto de dibujo, y no en el de la interfaz, porque son
/// vocabulario del dibujante: quien decide qué forma le toca a cada familia es la interfaz,
/// pero <b>qué formas existen</b> lo decide quien las traza. Así no hay dos listas de
/// cadenas que se puedan desincronizar.
/// </para>
/// <para>
/// <b>La forma decide también con qué rayado se dibuja</b>, y no la familia: las cuatro
/// macros de acero dejaron un rayado por familia, y las cinco formas que no tenían macro
/// toman el de la macro cuyo <i>material</i> comparten. Ver
/// <c>SeccionDrawer.RayarPerfil</c>.
/// </para>
/// </remarks>
public static class FormaAcero
{
    /// <summary>Alma y dos patines: la W, la I soldada, la IC y la S.</summary>
    public const string I = "I";

    /// <summary>Medio perfil I: patín arriba y alma colgando. La WT.</summary>
    public const string Te = "TE";

    /// <summary>Dos alas en escuadra, iguales o desiguales. La L.</summary>
    public const string Angulo = "ANGULO";

    /// <summary>Canal laminada: alma y dos patines, <b>sin labios</b>. La C.</summary>
    public const string Canal = "CANAL";

    /// <summary>Canal formada en frío, <b>con labios</b> y radios de doblez. La CF.</summary>
    public const string CanalConLabios = "CANAL_LABIOS";

    /// <summary>Zeta formada en frío: un patín a cada lado del alma. La ZF.</summary>
    public const string Zeta = "ZETA";

    /// <summary>Tubo rectangular o cuadrado, con esquinas redondeadas. El OR.</summary>
    public const string TuboRectangular = "TUBO_RECT";

    /// <summary>Tubo redondo: dos circunferencias. El OC.</summary>
    public const string TuboRedondo = "TUBO_REDONDO";

    /// <summary>Varilla redonda maciza: una circunferencia rellena. El OS.</summary>
    public const string RedondoMacizo = "REDONDO_MACIZO";

    /// <summary>Las nueve, en el orden en que están declaradas.</summary>
    public static readonly string[] Todas =
    {
        I, Te, Angulo, Canal, CanalConLabios, Zeta,
        TuboRectangular, TuboRedondo, RedondoMacizo
    };

    /// <summary>La forma dicha en castellano, para los avisos y las ayudas.</summary>
    public static string Nombre(string? forma) => forma switch
    {
        I => "perfil I",
        Te => "te",
        Angulo => "ángulo",
        Canal => "canal laminada",
        CanalConLabios => "canal con labios",
        Zeta => "zeta",
        TuboRectangular => "tubo rectangular",
        TuboRedondo => "tubo redondo",
        RedondoMacizo => "redondo macizo",
        _ => "desconocida"
    };
}
