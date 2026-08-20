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

/// <summary>
/// El <b>color de cada familia</b> de perfil, y la capa en la que se dibuja.
/// </summary>
/// <remarks>
/// <para>
/// <b>Un color por familia, y por capa, no por objeto.</b> Antes las cuatro familias
/// portadas se dibujaban todas en la capa <c>PERFILES</c>, así que en el plano las secciones
/// de acero salían todas del mismo color y solo se distinguían por su forma. Con doce
/// familias eso ya no vale: una IR y una IS tienen la misma forma, y si además tienen el
/// mismo color no hay manera de saber cuál es cuál sin leer el rótulo.
/// </para>
/// <para>
/// El color se pone en la <b>capa</b> y los objetos van «por capa», que es como se hace en
/// AutoCAD: así el usuario puede apagar todas las zetas de un clic, cambiarles el color a
/// todas a la vez o dejarlas fuera de la impresión, cosas que con el color pegado a cada
/// objeto no se pueden hacer. La capa <c>PERFILES</c> se sigue creando: es la que usaban las
/// macros, y los dibujos que ya existen la tienen.
/// </para>
/// <para>
/// Los índices están tomados de la <b>rueda de color ACI</b> de a 20 en 20 —cada familia en
/// un tono claramente distinto del de sus vecinas— y de cada tono se usan dos: el saturado
/// para las líneas y el rayado, y uno oscuro para el relleno macizo. Un relleno del mismo
/// color que su rayado deja el rayado invisible, que es justo lo que le pasaba al tubo
/// redondo: rellenaba con SOLID en 162 y rayaba con ANSI31 <b>también</b> en 162.
/// </para>
/// </remarks>
public static class ColorAcero
{
    /// <summary>La capa de las macros. Se sigue creando por compatibilidad.</summary>
    public const string CapaBase = "PERFILES";

    /// <summary>El color de la trama y de las líneas de cada familia.</summary>
    /// <remarks>
    /// El 30 y el 250 no se usan: el 30 es casi el naranja de las cotas y el 250 es gris
    /// oscuro, que sobre fondo negro no se ve.
    /// </remarks>
    public static int Lineas(string? familia) => (familia ?? string.Empty).ToUpperInvariant() switch
    {
        "IR" => 40,    // ámbar
        "IS" => 50,    // amarillo
        "IC" => 70,    // verde amarillo
        "S" => 90,     // verde
        "WT" => 110,   // verde cian
        "C" => 130,    // cian
        "CF" => 150,   // azul cian
        "ZF" => 170,   // azul
        "L" => 190,    // azul violeta
        "OR" => 210,   // magenta
        "OC" => 230,   // rosa
        "OS" => 20,    // naranja
        _ => 7         // blanco, el de la capa PERFILES de las macros
    };

    /// <summary>
    /// El color del <b>relleno macizo</b>: el mismo tono, seis pasos más oscuro.
    /// </summary>
    /// <remarks>
    /// En la rueda ACI cada tono ocupa diez índices y los pares van del saturado al muy
    /// oscuro, así que sumar seis da el mismo color bastante más apagado. Es lo que hace que
    /// el rayado que va encima se siga leyendo.
    /// </remarks>
    public static int Relleno(string? familia)
    {
        var linea = Lineas(familia);

        return linea == 7 ? 8 : linea + 6;
    }

    /// <summary>
    /// El color <b>pálido</b> del tono, para el fondo del rayado de los perfiles chicos.
    /// </summary>
    /// <remarks>
    /// Los índices <b>impares</b> de cada tono son sus versiones pálidas, y el <c>+1</c> es
    /// el más claro de todos. El fondo pálido es lo que hace que un tubo de dos pulgadas se
    /// lea como lleno sin tener que rellenarlo de macizo, que a ese tamaño lo convertiría en
    /// un manchón. Es la misma idea del fondo cian que le ponía la macro del HSS, pero del
    /// color de su familia, y con el rayado saturado encima quedando más oscuro que el
    /// fondo, que es lo que lo hace legible.
    /// </remarks>
    public static int Fondo(string? familia)
    {
        var linea = Lineas(familia);

        return linea == 7 ? 254 : linea + 1;
    }

    /// <summary>La capa de una familia: <c>PERFILES-IR</c>, <c>PERFILES-ZF</c>…</summary>
    public static string Capa(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return f.Length == 0 ? CapaBase : CapaBase + "-" + f;
    }
}
