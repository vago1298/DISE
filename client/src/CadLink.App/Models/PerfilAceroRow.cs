using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// La <b>forma</b> con la que se dibuja un perfil, que no es lo mismo que su familia.
/// </summary>
/// <remarks>
/// <para>
/// <b>Familia y forma son dos cosas distintas y hace falta separarlas.</b> La familia es
/// la lista en la que se busca el perfil y el nombre con el que se rotula: quien pide una
/// <c>IR</c> quiere ver <b>solo</b> las W, y quien pide una <c>IS</c> solo las I soldadas.
/// La forma es la geometría: y ahí resulta que IR, IS, IC y S <b>se dibujan igual</b>,
/// porque las cuatro son un alma con dos patines.
/// </para>
/// <para>
/// Antes esto estaba mezclado: las cuatro se metían en la familia <c>IR</c> «porque son
/// perfiles I», y el resultado era un desplegable de IR con 573 perfiles de cuatro
/// nomenclaturas revueltas, en el que había que ir sorteando IS, IC y S para encontrar una
/// W. Con la separación son cuatro listas y un solo dibujante.
/// </para>
/// </remarks>
public static class FormaPerfil
{
    // Las nueve formas son ALIAS de las del dibujante, no copias. Si fueran dos listas de
    // cadenas independientes, cambiar una y olvidar la otra dejaría al dibujante recibiendo
    // una forma que no reconoce, y el perfil se saltaría con un aviso desconcertante.
    // Siendo alias, el compilador garantiza que dicen lo mismo.

    /// <inheritdoc cref="FormaAcero.I"/>
    public const string I = FormaAcero.I;

    /// <inheritdoc cref="FormaAcero.Te"/>
    public const string Te = FormaAcero.Te;

    /// <inheritdoc cref="FormaAcero.Angulo"/>
    public const string Angulo = FormaAcero.Angulo;

    /// <inheritdoc cref="FormaAcero.Canal"/>
    public const string Canal = FormaAcero.Canal;

    /// <inheritdoc cref="FormaAcero.CanalConLabios"/>
    public const string CanalConLabios = FormaAcero.CanalConLabios;

    /// <inheritdoc cref="FormaAcero.Zeta"/>
    public const string Zeta = FormaAcero.Zeta;

    /// <inheritdoc cref="FormaAcero.TuboRectangular"/>
    public const string TuboRectangular = FormaAcero.TuboRectangular;

    /// <inheritdoc cref="FormaAcero.TuboRedondo"/>
    public const string TuboRedondo = FormaAcero.TuboRedondo;

    /// <inheritdoc cref="FormaAcero.RedondoMacizo"/>
    public const string RedondoMacizo = FormaAcero.RedondoMacizo;

    /// <summary>La forma que le toca a cada familia.</summary>
    public static string DeLaFamilia(string? familia) => (familia ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        FamiliaPerfil.Ir or FamiliaPerfil.Is or FamiliaPerfil.Ic or FamiliaPerfil.S => I,
        FamiliaPerfil.Wt => Te,
        FamiliaPerfil.C => Canal,
        FamiliaPerfil.Cf => CanalConLabios,
        FamiliaPerfil.Zf => Zeta,
        FamiliaPerfil.L => Angulo,
        FamiliaPerfil.Or => TuboRectangular,
        FamiliaPerfil.Oc => TuboRedondo,
        FamiliaPerfil.Os => RedondoMacizo,
        _ => string.Empty
    };

    /// <summary>La forma dicha en castellano, para los avisos y las ayudas.</summary>
    public static string Nombre(string? forma) => FormaAcero.Nombre(forma);
}

/// <summary>
/// Las <b>doce familias</b> de perfil del catálogo IMCA que se saben dibujar.
/// </summary>
/// <remarks>
/// <para>
/// Son las <b>claves internas</b>, y a propósito coinciden con la nomenclatura con la que
/// el propio manual designa cada perfil, que es la que el usuario busca en el desplegable.
/// Las tres únicas que se traducen al rotular son las que ya traducían las macros, porque
/// son nomenclatura americana que en el plano mexicano se escribe de otro modo:
/// <c>W</c> pasa a <c>IR</c>, <c>HSS</c> a <c>OR</c> y <c>PIPE</c> a <c>OC</c>. Ver
/// <see cref="PerfilAceroRow.PerfilRotulo"/>.
/// </para>
/// <para>
/// Cada familia usa unas columnas de dimensiones y deja las demás en blanco, y quién usa
/// qué lo decide la <see cref="FormaPerfil">forma</see>, no la familia: cuatro familias
/// comparten la forma I y por tanto piden las mismas cuatro medidas.
/// </para>
/// </remarks>
public static class FamiliaPerfil
{
    /// <summary>Perfil I laminado, el <c>W</c> del catálogo. Es el más usado.</summary>
    public const string Ir = "IR";

    /// <summary>I soldada de tres placas. Llega a peraltes de casi dos metros.</summary>
    public const string Is = "IS";

    /// <summary>I soldada de sección constante para columna.</summary>
    public const string Ic = "IC";

    /// <summary>Viga estándar americana: patines estrechos y en cuña.</summary>
    public const string S = "S";

    /// <summary>Te estructural, que sale de cortar un perfil I por el alma.</summary>
    public const string Wt = "WT";

    /// <summary>Canal estándar laminada, sin labios.</summary>
    public const string C = "C";

    /// <summary>Ángulo, de alas iguales o desiguales.</summary>
    public const string L = "L";

    /// <summary>Tubo rectangular o cuadrado, el <c>HSS</c>. Esquinas redondeadas.</summary>
    public const string Or = "OR";

    /// <summary>Tubo redondo, el <c>PIPE</c>. Dos circunferencias.</summary>
    public const string Oc = "OC";

    /// <summary>Redondo macizo: la varilla lisa estructural.</summary>
    public const string Os = "OS";

    /// <summary>Canal formada en frío, con labios. Es el «monten».</summary>
    public const string Cf = "CF";

    /// <summary>Zeta formada en frío, con los dos patines de distinto ancho.</summary>
    public const string Zf = "ZF";

    /// <summary>
    /// Las doce, <b>agrupadas por forma</b>: primero las laminadas de alma y patines,
    /// después las formadas en frío, el ángulo y por último los tubos y el macizo.
    /// </summary>
    /// <remarks>
    /// El orden es el del desplegable y también el del acomodo en el dibujo, así que
    /// familias que se parecen quedan en bandas vecinas y se comparan de un vistazo.
    /// </remarks>
    public static readonly string[] Todas =
        { Ir, Is, Ic, S, Wt, C, Cf, Zf, L, Or, Oc, Os };

    /// <summary>
    /// Los prefijos de nombre que delatan una familia, con sus sinónimos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se compara con el <b>prefijo de letras completo</b> del nombre, no con
    /// <c>StartsWith</c>. Es la diferencia entre acertar y no: con <c>StartsWith</c>, un
    /// <c>CF - 3" x 1 1/2"</c> entra por la puerta de la <c>C</c> si esa se prueba antes, y
    /// un <c>WT - 2"</c> por la de la <c>W</c>. Tomando las letras de delante enteras
    /// —<c>CF</c>, <c>WT</c>— cada nombre solo puede caer en su familia y el orden de la
    /// tabla deja de importar.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> PorPrefijo = new(StringComparer.Ordinal)
    {
        // Perfiles I. IPR es como se llama a la IR en algunas obras.
        ["W"] = Ir, ["IR"] = Ir, ["IPR"] = Ir,
        ["IS"] = Is,
        ["IC"] = Ic,
        ["S"] = S,

        // Te y canal laminada. TR y CE son sus nombres mexicanos.
        ["WT"] = Wt, ["TR"] = Wt,
        ["C"] = C, ["CE"] = C,

        // Ángulo. LI de alas iguales, LD de alas desiguales: las dos son la L.
        ["L"] = L, ["LI"] = L, ["LD"] = L,

        // Tubos. PTR es como se pide el tubo rectangular en México.
        ["HSS"] = Or, ["OR"] = Or, ["PTR"] = Or,
        ["PIPE"] = Oc, ["OC"] = Oc, ["TUBO"] = Oc,

        // Redondo macizo. VR de «varilla redonda».
        ["OS"] = Os, ["VR"] = Os,

        // Formados en frío.
        ["CF"] = Cf, ["MONTEN"] = Cf, ["MON"] = Cf,
        ["ZF"] = Zf, ["Z"] = Zf
    };

    /// <summary>
    /// La familia que le corresponde a un nombre de perfil de catálogo.
    /// </summary>
    /// <remarks>
    /// Sirve para no obligar a elegir la familia a mano cuando el nombre ya lo dice: quien
    /// escribe <c>W12X30</c> está capturando un IR y quien escribe <c>HSS6X6X1/4</c> un OR.
    /// Devuelve <c>null</c> si el nombre no lo aclara, y entonces manda la columna.
    /// </remarks>
    public static string? DelNombre(string? perfil)
    {
        var p = (perfil ?? string.Empty).Trim().ToUpperInvariant();

        if (p.Length == 0)
        {
            return null;
        }

        // Las letras de delante, hasta el primer número, espacio o guion.
        var letras = new System.Text.StringBuilder();

        foreach (var ch in p)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            letras.Append(ch);
        }

        return PorPrefijo.TryGetValue(letras.ToString(), out var familia) ? familia : null;
    }

    /// <summary>La forma con la que se dibuja esta familia.</summary>
    public static string Forma(string? familia) => FormaPerfil.DeLaFamilia(familia);

    /// <summary>Familia y forma en una línea, para la ayuda del desplegable.</summary>
    public static string Descripcion(string? familia)
    {
        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        return f switch
        {
            Ir => "IR — perfil I laminado (las W del IMCA)",
            Is => "IS — I soldada de tres placas",
            Ic => "IC — I soldada para columna",
            S => "S — viga estándar americana",
            Wt => "WT — te, que sale de cortar un perfil I",
            C => "C — canal estándar laminada, sin labios",
            Cf => "CF — canal formada en frío, con labios (monten)",
            Zf => "ZF — zeta formada en frío",
            L => "L — ángulo de alas iguales o desiguales",
            Or => "OR — tubo rectangular o cuadrado (HSS)",
            Oc => "OC — tubo redondo (PIPE)",
            Os => "OS — redondo macizo",
            _ => f
        };
    }
}

/// <summary>
/// Una fila de la hoja de <b>secciones de acero</b>: un perfil con sus dimensiones.
/// </summary>
/// <remarks>
/// <para>
/// Port de las cuatro macros de acero —<c>DibujarSeccionIR</c>, <c>DibujarSeccionHSS</c>,
/// <c>DibujarSeccionOC</c> y <c>DibujarSeccionCF</c>—, que en la hoja de Excel viven en
/// <b>cuatro bloques de columnas distintos</b>: D-G para el IR, L-N para el HSS, AJ-AL para
/// el OC y T-W para el CF. Y ampliado con las <b>cinco formas que faltaban</b> —te, ángulo,
/// canal laminada, zeta y redondo macizo—, que son las que dejaban 499 perfiles del manual
/// IMCA fuera del catálogo.
/// </para>
/// <para>
/// <b>Aquí es una sola tabla.</b> Cuatro bloques de columnas separados obligan a saberse de
/// memoria en qué zona de la hoja se captura cada cosa, y dejan el 75 % de la fila en
/// blanco siempre. Con una tabla y una columna de familia, las dimensiones se llaman por lo
/// que son —peralte, ancho, espesores— y cada forma usa las que necesita:
/// </para>
/// <list type="table">
///   <listheader><term>Forma</term><description>Columnas que usa</description></listheader>
///   <item>
///     <term>I, canal</term>
///     <description>Peralte (<c>d</c>), ancho de patín (<c>bf</c>), espesor de alma
///     (<c>tw</c>) y espesor de patín (<c>tf</c>).</description>
///   </item>
///   <item>
///     <term>Te</term>
///     <description>Las mismas cuatro, pero con un solo patín.</description>
///   </item>
///   <item>
///     <term>Ángulo</term>
///     <description>Peralte, que es el <b>ala larga</b>; ancho, que es la <b>corta</b>; y
///     el espesor, que es el mismo en las dos.</description>
///   </item>
///   <item>
///     <term>Tubo rectangular</term>
///     <description>Peralte, ancho y espesor de pared. El radio de esquina no se captura:
///     la macro lo fija en el propio espesor por fuera y en su mitad por dentro.</description>
///   </item>
///   <item>
///     <term>Tubo redondo</term>
///     <description>Peralte, que aquí es el <b>diámetro exterior</b>, y espesor de
///     pared.</description>
///   </item>
///   <item>
///     <term>Redondo macizo</term>
///     <description>Solo el peralte, que es el diámetro. No tiene pared.</description>
///   </item>
///   <item>
///     <term>Canal con labios</term>
///     <description>Peralte, ancho, espesor, <b>labio</b> y <b>radio</b> de doblez.</description>
///   </item>
///   <item>
///     <term>Zeta</term>
///     <description>Peralte, ancho del patín, <b>ancho 2</b> —el patín angosto— espesor y
///     radio.</description>
///   </item>
/// </list>
/// </remarks>
public sealed class PerfilAceroRow : Row
{
    private string _familia = FamiliaPerfil.Ir;

    // El perfil de arranque va con su DESIGNACIÓN DEL MANUAL completa, no abreviada, para
    // que al abrir la celda aparezca marcado en el desplegable en lugar de parecer un
    // perfil escrito a mano que no está en el catálogo.
    private string _perfil = "W - 12'' x 30.04 lb/ft";
    private string _id = "V-1";
    private string _elemento = ElementoViga;
    private string _clasificacion = string.Empty;
    private string _acero = AceroA36;
    private bool _doble;

    private double _peralteCm = 31.3;
    private double _anchoCm = 16.6;
    private double _espesorAlmaCm = 0.67;
    private double _espesorPatinCm = 1.12;
    private double _labioCm;
    private double _radioCm;
    private double _anchoMenorCm;

    private PropiedadesPerfil _propiedades = PropiedadesPerfil.Ninguna;

    /// <summary>Elementos de acero que se rotulan.</summary>
    public const string ElementoViga = "VIGA";

    /// <inheritdoc cref="ElementoViga"/>
    public const string ElementoColumna = "COLUMNA";

    /// <inheritdoc cref="ElementoViga"/>
    public const string ElementoTensor = "TENSOR";

    // LAS DESIGNACIONES VAN COMO LAS ESCRIBE EL CATÁLOGO —«A-572-Gr. 50», no
    // «A-572 GR. 50»—, porque el desplegable se llena del catálogo y una designación que no
    // esté en la lista sale con la celda en blanco. Un proyecto viejo guardado con la
    // escritura de antes se sigue leyendo igual: el ajustador de la celda la reconoce y la
    // guarda con la escritura del catálogo.

    /// <summary>El acero al carbón de siempre, el más disponible.</summary>
    public const string AceroA36 = "A-36";

    /// <summary>El de alta resistencia y baja aleación más usado.</summary>
    public const string AceroA572 = "A-572-Gr. 50";

    /// <summary>El de los perfiles I laminados de hoy.</summary>
    public const string AceroA992 = "A-992";

    /// <summary>
    /// El de los tubos estructurales <b>rectangulares</b>.
    /// </summary>
    /// <remarks>
    /// Lleva apóstrofo, y no es un adorno: en el manual el <c>A-500-Gr. B</c> es el tubo
    /// <b>redondo</b>, con Fy 2955 kg/cm², y el <c>A-500-Gr. B'</c> el <b>rectangular</b>,
    /// con Fy 3235. Es la misma norma con dos Fy según la forma del tubo —42 y 46 ksi—.
    /// </remarks>
    public const string AceroA500Bp = "A-500-Gr. B'";

    /// <summary>El de los tubos redondos de conducción, el <c>PIPE</c>.</summary>
    public const string AceroA53B = "A-53-Gr. B";

    /// <summary>El de la lámina rolada en frío: la canal con labios y la zeta.</summary>
    public const string AceroA1008 = "A-1008-Gr. 50";

    /// <summary>
    /// Los elementos del desplegable.
    /// </summary>
    /// <remarks>
    /// <b>MONTEN</b> y <b>DIAGONAL</b> son los dos que se agregaron: el monten es el
    /// larguero de lámina doblada de la cubierta, que se pide por su nombre y no como
    /// «larguero», y la diagonal es la del contraventeo. Sin ellos había que rotular a mano
    /// dos de los elementos más frecuentes de una nave.
    /// </remarks>
    public static readonly string[] Elementos =
    {
        ElementoViga, ElementoColumna, ElementoTensor, "PUNTAL", "LARGUERO",
        "ATIESADOR", "MONTEN", "DIAGONAL"
    };

    /// <summary>
    /// Los aceros del desplegable: <b>los del catálogo</b>, no una lista escrita aquí.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes eran cinco, escritos a mano y sin más dato que su nombre. Ahora salen de
    /// <c>aceros.csv</c> —treinta y nueve, con su Fy, su Fu y en qué secciones se hace cada
    /// uno—, y por lo mismo que los perfiles: una lista dentro del programa envejece con la
    /// norma y solo se puede corregir recompilando.
    /// </para>
    /// <para>
    /// Es una <b>propiedad</b> y no un campo <c>readonly</c> para que recargar el catálogo se
    /// note: con un campo, la lista se congelaba en el primer arranque.
    /// </para>
    /// </remarks>
    public static string[] Aceros => CatalogoAceros.Nombres;

    /// <summary>
    /// Clasificación del elemento, que en el rótulo va pegada a su nombre.
    /// </summary>
    /// <remarks>
    /// Es la columna D de la fila de información en la macro, y de ahí sale el renglón
    /// «VIGA PRINCIPAL "V-1"». <b>Vale para cualquier elemento</b>, no solo para la viga:
    /// una «DIAGONAL PRINCIPAL» o un «LARGUERO DE BORDE» son cosas que se dicen, y no había
    /// motivo para que el programa las descartara.
    /// </remarks>
    public static readonly string[] Clasificaciones =
        { string.Empty, "PRINCIPAL", "SECUNDARIA", "DE BORDE", "DE PISO", "DE TECHO" };

    /// <summary>
    /// Una fila nueva arranca con un perfil del catálogo, medidas y propiedades incluidas.
    /// </summary>
    /// <remarks>
    /// El perfil de arranque está en el catálogo, así que sus medidas se traen de ahí en
    /// lugar de fiarse de las que hay escritas arriba: si algún día el catálogo corrige un
    /// espesor, la fila nueva sale con el valor corregido y no con el de hace dos versiones.
    /// Y de paso trae sus propiedades, que no se pueden escribir a mano.
    /// </remarks>
    public PerfilAceroRow() => TraerDelCatalogo();

    /// <summary>Familia del perfil. Decide qué lista se ofrece y con qué nombre se rotula.</summary>
    public string Familia
    {
        get => _familia;
        set
        {
            Set(ref _familia, (value ?? string.Empty).Trim().ToUpperInvariant());
            Raise(nameof(Forma));
            Raise(nameof(FormaNombre));
            Raise(nameof(PerfilRotulo));
            Raise(nameof(FaltanDatos));

            // Al cambiar de familia cambia la lista de perfiles que se ofrece, que es lo
            // que hace que no haya que teclear las medidas.
            Raise(nameof(PerfilesDeLaFamilia));

            // Y cambia la RESPUESTA de la disponibilidad, aunque el acero sea el mismo: un
            // A-36 se consigue en canal y no en monten. Sin esto, cambiar de familia dejaba
            // la marca de la familia anterior, que es peor que no tener marca.
            RaiseDelAcero();
        }
    }

    /// <summary>La forma con la que se dibuja esta fila. La decide la familia.</summary>
    public string Forma => FormaPerfil.DeLaFamilia(_familia);

    /// <summary>
    /// La forma dicha en castellano, para verla en la cuadrícula.
    /// </summary>
    /// <remarks>
    /// Se muestra como columna calculada porque es la única manera de que se vea que cuatro
    /// familias distintas se dibujan igual: al poner IS en una fila, ahí aparece «perfil I»,
    /// que es lo que va a salir en el plano. Sin esto, la relación entre familia y forma solo
    /// existiría en el código.
    /// </remarks>
    public string FormaNombre => FormaPerfil.Nombre(Forma);

    /// <summary>
    /// Los perfiles del catálogo de <b>esta</b> familia, para el desplegable de la celda.
    /// </summary>
    /// <remarks>
    /// Es una propiedad de la FILA y no una lista de la columna porque cada fila puede ser
    /// de una familia distinta: la lista de la celda depende de lo que diga su propio
    /// renglón. Una lista por columna solo podría ofrecer todos los perfiles de todas las
    /// familias mezclados, que es justo lo que hace elegir mal.
    /// </remarks>
    public string[] PerfilesDeLaFamilia => CatalogoPerfiles.NombresDe(_familia);

    /// <summary>
    /// Nombre de catálogo del perfil, tal como se captura.
    /// </summary>
    /// <remarks>
    /// Al escribirlo se <b>ajusta la familia sola</b> si el nombre la delata, porque un
    /// <c>HSS</c> dibujado como IR no es un error que se vea venir: sale un perfil I con
    /// las medidas de un tubo. Si el nombre no dice nada, la familia se queda como está.
    /// </remarks>
    public string Perfil
    {
        get => _perfil;
        set
        {
            Set(ref _perfil, value);

            var familia = FamiliaPerfil.DelNombre(_perfil);

            if (familia is not null && familia != _familia)
            {
                Familia = familia;
            }

            Raise(nameof(PerfilRotulo));

            // Y si el perfil está en el catálogo, sus medidas se traen solas.
            TraerDelCatalogo();
        }
    }

    /// <summary>
    /// Si el perfil está en el catálogo, copia sus medidas a la fila.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que evita el error de captura: elegir un perfil de la lista trae su peralte, su
    /// ancho y sus espesores, en lugar de teclear cuatro números que nadie va a revisar.
    /// </para>
    /// <para>
    /// <b>Se escriben las siete medidas, incluidas las que la forma no usa.</b> El catálogo
    /// las trae en cero para lo que no aplica —un tubo redondo no tiene ancho— y copiarlas
    /// tal cual es justamente lo que hace falta: si se dejaran las del perfil anterior, un
    /// OC capturado después de un CF se quedaría con el labio del CF en la celda, y la
    /// columna «Falta» no tendría cómo saber que ese número ya no significa nada.
    /// </para>
    /// <para>
    /// Y si el perfil <b>no</b> está en el catálogo no se borra nada: quien escribe un
    /// perfil a mano es porque va a capturar sus medidas a mano, y limpiárselas sería
    /// pelearse con él.
    /// </para>
    /// </remarks>
    private void TraerDelCatalogo()
    {
        var c = CatalogoPerfiles.Buscar(_familia, _perfil);

        if (c is null)
        {
            // Las MEDIDAS no se tocan —quien escribe un perfil a mano las va a capturar a
            // mano— pero las PROPIEDADES sí se limpian, y esa diferencia importa: dejar las
            // del perfil anterior sería mostrar el Ix de una W12 junto al nombre de un
            // perfil hecho a la medida. Es el peor de los errores posibles en una tabla de
            // perfiles, porque el número es creíble.
            if (_propiedades.Cuantas > 0)
            {
                _propiedades = PropiedadesPerfil.Ninguna;
                Raise(nameof(Propiedades));
            }

            return;
        }

        _peralteCm = c.PeralteCm;
        _anchoCm = c.AnchoCm;
        _espesorAlmaCm = c.EspesorAlmaCm;
        _espesorPatinCm = c.EspesorPatinCm;
        _labioCm = c.LabioCm;
        _radioCm = c.RadioCm;
        _anchoMenorCm = c.AnchoMenorCm;
        _propiedades = c.Props;

        // Se avisa de las siete a la vez, con los campos ya puestos: si se usaran las
        // propiedades una por una, cada asignación dispararía su propio aviso y la columna
        // «Falta» se calcularía siete veces con la fila a medio llenar.
        Raise(nameof(PeralteCm));
        Raise(nameof(AnchoCm));
        Raise(nameof(EspesorAlmaCm));
        Raise(nameof(EspesorPatinCm));
        Raise(nameof(LabioCm));
        Raise(nameof(RadioCm));
        Raise(nameof(AnchoMenorCm));
        Raise(nameof(Propiedades));
        Raise(nameof(FaltanDatos));
    }

    /// <summary>
    /// Las <b>propiedades geométricas</b> del perfil del catálogo: solo para leerlas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No se capturan ni se dibujan: se traen del catálogo con las medidas y se muestran al
    /// final de la cuadrícula, para poder comparar dos perfiles al elegir sin tener que ir al
    /// manual. El dibujante no recibe ninguna.
    /// </para>
    /// <para>
    /// Un perfil escrito a mano, que no está en el catálogo, <b>se queda sin ellas</b>: no
    /// hay de dónde sacarlas y no se calculan. Las celdas salen vacías, que es lo que hay que
    /// decir; inventar un <c>Ix</c> a partir del área sería peor que dejarlo en blanco.
    /// </para>
    /// </remarks>
    public PropiedadesPerfil Propiedades => _propiedades;

    /// <summary>Identificador de la sección. Es el nombre del bloque en AutoCAD.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Tipo de elemento: VIGA, COLUMNA, MONTEN, DIAGONAL…</summary>
    public string Elemento
    {
        get => _elemento;
        set
        {
            Set(ref _elemento, value);
            Raise(nameof(ElementoRotulo));
        }
    }

    /// <summary>Clasificación del elemento, si la lleva.</summary>
    public string Clasificacion
    {
        get => _clasificacion;
        set
        {
            Set(ref _clasificacion, value);
            Raise(nameof(ElementoRotulo));
        }
    }

    /// <summary>
    /// Tipo de acero, para el renglón «ACERO …» del rótulo y para el Fy de la tabla.
    /// </summary>
    /// <remarks>
    /// Al asignarlo se guarda <b>con la escritura del catálogo</b> si el acero está en él,
    /// así que «A-572 GR. 50» —como se escribía antes— se guarda como «A-572-Gr. 50» y el
    /// desplegable lo muestra marcado. Si no está en el catálogo se guarda tal cual: la
    /// celda sigue siendo texto libre, porque nadie tiene por qué esperar a que se actualice
    /// el archivo para rotular un acero que ya compró.
    /// </remarks>
    public string Acero
    {
        get => _acero;
        set
        {
            Set(ref _acero, CatalogoAceros.ComoEnElCatalogo(value));
            RaiseDelAcero();
        }
    }

    /// <summary>
    /// Una copia de esta fila, para el <b>historial de deshacer</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El orden de las asignaciones importa y no es negociable.</b> Primero la familia y el
    /// perfil, y las medidas <i>después</i>: escribir el perfil trae las medidas del catálogo
    /// solo —es lo que evita teclear cuatro números por fila—, así que asignándolas antes las
    /// pisaría el catálogo y una medida que el usuario había ajustado a mano se perdería al
    /// deshacer, que es justo lo contrario de lo que un deshacer tiene que hacer.
    /// </para>
    /// <para>
    /// Las propiedades geométricas no se copian: son del catálogo, no de la fila, y las trae el
    /// propio perfil al asignarlo.
    /// </para>
    /// </remarks>
    public PerfilAceroRow Copia()
    {
        var c = new PerfilAceroRow
        {
            Familia = _familia,
            Perfil = _perfil
        };

        // Y ahora las medidas, que pueden estar ajustadas a mano.
        c.Id = _id;
        c.Elemento = _elemento;
        c.Clasificacion = _clasificacion;
        c.Acero = _acero;
        c.Doble = _doble;
        c.PeralteCm = _peralteCm;
        c.AnchoCm = _anchoCm;
        c.EspesorAlmaCm = _espesorAlmaCm;
        c.EspesorPatinCm = _espesorPatinCm;
        c.LabioCm = _labioCm;
        c.RadioCm = _radioCm;
        c.AnchoMenorCm = _anchoMenorCm;

        return c;
    }

    /// <summary>Si esta fila guarda lo mismo que la otra.</summary>
    /// <remarks>
    /// Se compara lo que se <b>captura</b>, no lo calculado: dos filas con la misma familia,
    /// perfil y medidas son la misma fila, y su forma, su rótulo y sus propiedades salen de
    /// ahí. Sirve para que el historial no apile un paso donde no cambió nada.
    /// </remarks>
    public bool EsIgualA(PerfilAceroRow? o) =>
        o is not null
        && _familia == o._familia
        && _perfil == o._perfil
        && _id == o._id
        && _elemento == o._elemento
        && _clasificacion == o._clasificacion
        && _acero == o._acero
        && _doble == o._doble
        && Math.Abs(_peralteCm - o._peralteCm) < 1e-9
        && Math.Abs(_anchoCm - o._anchoCm) < 1e-9
        && Math.Abs(_espesorAlmaCm - o._espesorAlmaCm) < 1e-9
        && Math.Abs(_espesorPatinCm - o._espesorPatinCm) < 1e-9
        && Math.Abs(_labioCm - o._labioCm) < 1e-9
        && Math.Abs(_radioCm - o._radioCm) < 1e-9
        && Math.Abs(_anchoMenorCm - o._anchoMenorCm) < 1e-9;

    /// <summary>El acero del catálogo que corresponde a esta fila, si está.</summary>
    public AceroCatalogo? AceroInfo => CatalogoAceros.Buscar(_acero);

    /// <summary>Fy del acero, en kg/cm². Vacío si el acero no está en el catálogo.</summary>
    public double? FyKgCm2 => AceroInfo?.FyKgCm2;

    /// <summary>Fu del acero, en kg/cm². No se muestra en la tabla: va en la ayuda.</summary>
    public double? FuKgCm2 => AceroInfo?.FuKgCm2;

    /// <summary>
    /// Si este acero se consigue en <b>esta</b> familia de perfil: <c>SI</c>,
    /// <c>VERIFICAR</c> o <c>NO</c>.
    /// </summary>
    /// <remarks>
    /// Un acero que no está en el catálogo contesta <c>VERIFICAR</c>, no <c>NO</c>: se
    /// escribió a mano y el programa no tiene de dónde saberlo. Marcar en rojo un acero
    /// solo por no estar en el archivo sería inventarse una respuesta.
    /// </remarks>
    public string DisponibilidadAcero =>
        AceroInfo?.DisponibleEn(_familia) ?? AceroCatalogo.Verificar;

    /// <summary>Lo que se lee en la celda de disponibilidad.</summary>
    public string AceroDisponibleLeyenda =>
        AceroInfo?.LeyendaEn(_familia) ?? "Fuera del catálogo";

    /// <summary>Si el manual dice que este acero <b>no se hace</b> en esta familia.</summary>
    /// <remarks>
    /// Es lo que pinta la fila en rojo. Y es lo único que la pinta: el «verificar» se queda
    /// en su celda, porque un aviso que sale en la mitad de las filas no se lee.
    /// </remarks>
    public bool AceroNoDisponible => DisponibilidadAcero == AceroCatalogo.No;

    /// <summary>Si hay que confirmarlo con el proveedor.</summary>
    public bool AceroPorVerificar => DisponibilidadAcero == AceroCatalogo.Verificar;

    /// <summary>El acero con sus dos esfuerzos y su norma mexicana, para el globo de ayuda.</summary>
    public string AceroDetalle => AceroInfo?.Detalle
        ?? $"{_acero}\nNo está en el catálogo de aceros, así que no se puede decir su Fy "
           + "ni si se consigue en este perfil.";

    /// <summary>Avisa de todo lo que depende del acero y de la familia a la vez.</summary>
    private void RaiseDelAcero()
    {
        Raise(nameof(AceroInfo));
        Raise(nameof(FyKgCm2));
        Raise(nameof(FuKgCm2));
        Raise(nameof(DisponibilidadAcero));
        Raise(nameof(AceroDisponibleLeyenda));
        Raise(nameof(AceroNoDisponible));
        Raise(nameof(AceroPorVerificar));
        Raise(nameof(AceroDetalle));
    }

    /// <summary>
    /// Perfil <b>doble</b>: dos perfiles juntos, como en la columna «SI» de la macro.
    /// </summary>
    /// <remarks>
    /// En las formas simétricas los dos van uno al lado del otro. En las que tienen un lado
    /// —la canal con labios, la canal laminada, el ángulo y la te— el segundo va
    /// <b>espejeado</b>, que es como se arma un cajón con dos canales enfrentadas o una
    /// cruz con dos ángulos.
    /// </remarks>
    public bool Doble { get => _doble; set => Set(ref _doble, value); }

    /// <summary>Peralte. En OC y OS es el <b>diámetro</b>; en la L, el <b>ala larga</b>.</summary>
    public double PeralteCm { get => _peralteCm; set { Set(ref _peralteCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Ancho del patín; cara del tubo rectangular; <b>ala corta</b> del ángulo.</summary>
    public double AnchoCm { get => _anchoCm; set { Set(ref _anchoCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Espesor del alma en las laminadas; de pared en los tubos; de lámina en frío.</summary>
    public double EspesorAlmaCm { get => _espesorAlmaCm; set { Set(ref _espesorAlmaCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Espesor del patín. Solo las formas laminadas: I, te y canal.</summary>
    public double EspesorPatinCm { get => _espesorPatinCm; set { Set(ref _espesorPatinCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Largo del labio. Solo la canal con labios.</summary>
    public double LabioCm { get => _labioCm; set { Set(ref _labioCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Radio de doblez exterior. La canal con labios y la zeta.</summary>
    public double RadioCm { get => _radioCm; set { Set(ref _radioCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>
    /// El <b>patín angosto</b> de la zeta. Solo lo usa la ZF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Una zeta de catálogo tiene los dos patines de <b>distinto ancho</b> —60.3 y 54 mm en
    /// la de 2 3/8"— y eso no es una errata del manual: es lo que permite traslapar dos
    /// zetas en el apoyo, porque el patín angosto de una entra dentro del ancho de la otra.
    /// Dibujarla con los dos iguales sería dibujar una zeta que no existe.
    /// </para>
    /// <para>
    /// Si se deja en cero se dibuja simétrica, con los dos patines del ancho de
    /// <see cref="AnchoCm"/>: es lo que hay que hacer con una zeta de fabricación propia.
    /// </para>
    /// </remarks>
    public double AnchoMenorCm { get => _anchoMenorCm; set { Set(ref _anchoMenorCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>
    /// El nombre del perfil <b>como va en el plano</b>, en nomenclatura mexicana.
    /// </summary>
    /// <remarks>
    /// Es la traducción que hacen las macros al rotular: <c>W</c> a <c>IR</c>, <c>HSS</c> a
    /// <c>OR</c>, <c>PIPE</c> a <c>OC</c>, y el <c>#</c> del calibre a <c>CAL </c>. Las
    /// otras nueve familias <b>no se traducen</b>: IS, IC, S, WT, C, L, OS, CF y ZF ya se
    /// designan así en el manual y así se rotulan. Se muestra en la cuadrícula para que se
    /// vea antes de dibujar.
    /// </remarks>
    public string PerfilRotulo => AlRotulo(_familia, _perfil);

    /// <summary>La misma traducción, para poder probarla sin construir una fila.</summary>
    public static string AlRotulo(string? familia, string? perfil)
    {
        var s = (perfil ?? string.Empty).Trim().ToUpperInvariant();

        if (s.Length == 0)
        {
            return string.Empty;
        }

        var f = (familia ?? string.Empty).Trim().ToUpperInvariant();

        // Cada familia traduce SU prefijo, y no los de las otras: así un CF que se llame
        // «CF 6X2 W» no se convierte en «CF 6X2 IR».
        //
        // Y el IR compara el prefijo de letras ENTERO, no con StartsWith: si no, la
        // «WT - 2'' x 6.5 lb/ft» capturada en una fila de IR se rotularía «IRT - 2''».
        if (f == FamiliaPerfil.Ir && PrefijoDeLetras(s) == "W")
        {
            s = "IR" + s.Substring(1);
        }
        else if (f == FamiliaPerfil.Or)
        {
            s = Reemplazar(s, "HSS", "OR");
            s = Reemplazar(s, "PTR", "OR");
        }
        else if (f == FamiliaPerfil.Oc)
        {
            s = Reemplazar(s, "PIPE", "OC");
            s = Reemplazar(s, "TUBO", "OC");
        }

        // El calibre. Va en las doce familias porque el «#14» de una CF y el de una
        // lámina se escriben igual, y en el plano se lee «CAL 14».
        return s.Replace("#", "CAL ");
    }

    /// <summary>Las letras de delante de un nombre, hasta el primer número o espacio.</summary>
    private static string PrefijoDeLetras(string s)
    {
        var letras = new System.Text.StringBuilder();

        foreach (var ch in s)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            letras.Append(ch);
        }

        return letras.ToString();
    }

    private static string Reemplazar(string texto, string viejo, string nuevo) =>
        texto.StartsWith(viejo, StringComparison.Ordinal)
            ? nuevo + texto.Substring(viejo.Length)
            : texto;

    /// <summary>
    /// El primer renglón del rótulo: el elemento con su clasificación y el ID.
    /// </summary>
    /// <remarks>
    /// Port de <c>ConstruirLinea1Rotulo</c>. La macro solo pegaba la clasificación cuando
    /// el elemento era VIGA; aquí se pega a <b>cualquiera</b>, porque una «DIAGONAL
    /// PRINCIPAL» o un «LARGUERO DE BORDE» son cosas que se dicen en un plano, y con la
    /// regla de la macro el usuario elegía la clasificación, la veía en su celda y luego no
    /// aparecía en el dibujo sin que nada le dijera por qué.
    /// </remarks>
    public string ElementoRotulo
    {
        get
        {
            var elem = (_elemento ?? string.Empty).Trim().ToUpperInvariant();
            var clasif = (_clasificacion ?? string.Empty).Trim().ToUpperInvariant();

            if (clasif.Length > 0 && elem.Length > 0)
            {
                return elem + " " + clasif;
            }

            return elem.Length > 0 ? elem : clasif;
        }
    }

    /// <summary>
    /// Qué le falta a esta fila para poder dibujarse, o cadena vacía si no le falta nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se muestra como columna calculada porque cada forma pide unas dimensiones y no
    /// otras, y sin esto la única forma de enterarse sería que el dibujo saliera raro. Es la
    /// misma idea que la cuantía de la hoja de concreto: una columna que se calcula sola y
    /// dice si los datos se sostienen.
    /// </para>
    /// <para>
    /// Lo pide la FORMA, no la familia: las cuatro familias de perfil I piden las mismas
    /// cuatro medidas, y el redondo macizo no pide espesor porque no tiene pared.
    /// </para>
    /// </remarks>
    public string FaltanDatos
    {
        get
        {
            var forma = Forma;

            if (forma.Length == 0)
            {
                return $"la familia «{_familia}» no se reconoce";
            }

            var faltan = new List<string>();

            var esRedondo = forma is FormaPerfil.TuboRedondo or FormaPerfil.RedondoMacizo;

            if (_peralteCm <= 0)
            {
                faltan.Add(esRedondo ? "diametro" : forma == FormaPerfil.Angulo ? "ala larga" : "peralte");
            }

            // El macizo es el único que no lleva espesor: es una barra llena.
            if (forma != FormaPerfil.RedondoMacizo && _espesorAlmaCm <= 0)
            {
                faltan.Add(forma is FormaPerfil.I or FormaPerfil.Te or FormaPerfil.Canal
                    ? "e alma"
                    : "espesor");
            }

            if (!esRedondo && _anchoCm <= 0)
            {
                faltan.Add(forma == FormaPerfil.Angulo ? "ala corta" : "ancho");
            }

            // Los paréntesis del patrón no son de adorno: sin ellos se lee igual, pero al
            // siguiente que agregue una forma a la lista le costará ver dónde acaba el «or»
            // y dónde empieza el «&&».
            if ((forma is FormaPerfil.I or FormaPerfil.Te or FormaPerfil.Canal) &&
                _espesorPatinCm <= 0)
            {
                faltan.Add("e patin");
            }

            if (forma == FormaPerfil.CanalConLabios && _labioCm <= 0)
            {
                faltan.Add("labio");
            }

            // Y las comprobaciones de que el perfil CABE en sí mismo. Sin esto, un espesor
            // mayor que el medio peralte dibuja un perfil con el alma cruzada, que en el
            // plano se ve como un borrón.
            if (faltan.Count == 0)
            {
                var problema = NoCabe(forma);

                if (problema is not null)
                {
                    faltan.Add(problema);
                }
            }

            return faltan.Count == 0 ? string.Empty : string.Join(", ", faltan);
        }
    }

    /// <summary>Lo que hace que el perfil no quepa en sí mismo, o <c>null</c>.</summary>
    private string? NoCabe(string forma) => forma switch
    {
        FormaPerfil.I or FormaPerfil.Canal when 2 * _espesorPatinCm >= _peralteCm =>
            "los dos patines no caben en el peralte",

        FormaPerfil.I or FormaPerfil.Canal when _espesorAlmaCm >= _anchoCm =>
            "el alma es mas ancha que el patin",

        // La te lleva UN patín, así que el que no quepa se comprueba con uno solo.
        FormaPerfil.Te when _espesorPatinCm >= _peralteCm =>
            "el patin no cabe en el peralte",

        FormaPerfil.Te when _espesorAlmaCm >= _anchoCm =>
            "el alma es mas ancha que el patin",

        FormaPerfil.Angulo when _espesorAlmaCm >= _anchoCm =>
            "el espesor se come el ala corta",

        FormaPerfil.Angulo when _anchoCm > _peralteCm =>
            "el ala corta es mas larga que la larga: cambialas",

        FormaPerfil.TuboRectangular when 2 * _espesorAlmaCm >= Math.Min(_peralteCm, _anchoCm) =>
            "la pared no deja hueco interior",

        FormaPerfil.TuboRedondo when 2 * _espesorAlmaCm >= _peralteCm =>
            "la pared no deja hueco interior",

        FormaPerfil.CanalConLabios or FormaPerfil.Zeta when 2 * _espesorAlmaCm >= _peralteCm =>
            "los dos patines no caben en el peralte",

        FormaPerfil.CanalConLabios when _labioCm <= _espesorAlmaCm =>
            "el labio no llega ni al espesor",

        // El ancho 2 es opcional: en cero la zeta sale simétrica. Lo que no puede es ser
        // MAYOR que el ancho, porque entonces el angosto no es el angosto.
        FormaPerfil.Zeta when _anchoMenorCm > _anchoCm =>
            "el ancho 2 es el patin ANGOSTO: no puede pasar del ancho",

        _ => null
    };

    protected override void RaiseCalculadas()
    {
        Raise(nameof(Forma));
        Raise(nameof(FormaNombre));
        Raise(nameof(Propiedades));
        Raise(nameof(PerfilRotulo));
        Raise(nameof(ElementoRotulo));
        Raise(nameof(FaltanDatos));
    }
}
