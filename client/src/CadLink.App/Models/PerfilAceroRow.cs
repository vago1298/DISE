namespace CadLink.App.Models;

/// <summary>
/// Las cuatro familias de perfil que dibujan las macros de acero.
/// </summary>
/// <remarks>
/// <para>
/// Son las <b>claves internas</b>, y a propósito coinciden con la nomenclatura mexicana
/// con la que se rotula el plano, no con la americana con la que vienen los catálogos. Las
/// macros hacen esa traducción al rotular —<c>W</c> pasa a <c>IR</c>, <c>HSS</c> a
/// <c>OR</c>, <c>PIPE</c> a <c>OC</c>— y aquí se conserva igual, en
/// <see cref="PerfilAceroRow.PerfilRotulo"/>.
/// </para>
/// <para>
/// Cada familia usa unas columnas de dimensiones y deja las demás en blanco. No hay una
/// tabla de columnas por familia porque la cuadrícula es una sola: lo que hay es una
/// <see cref="PerfilAceroRow.FaltanDatos"/> que dice, para la familia elegida, qué falta.
/// </para>
/// </remarks>
public static class FamiliaPerfil
{
    /// <summary>Perfil I laminado, el <c>W</c> de los catálogos. Alma y dos patines.</summary>
    public const string Ir = "IR";

    /// <summary>Tubo rectangular o cuadrado, el <c>HSS</c>. Esquinas redondeadas.</summary>
    public const string Or = "OR";

    /// <summary>Tubo redondo, el <c>PIPE</c>. Dos circunferencias.</summary>
    public const string Oc = "OC";

    /// <summary>Canal formado en frío, con labios. Es el que lleva radios de doblez.</summary>
    public const string Cf = "CF";

    /// <summary>Las cuatro, en el orden en que se usan.</summary>
    public static readonly string[] Todas = { Ir, Or, Oc, Cf };

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

        // HSS antes que nada porque contiene una S y podría confundirse con otros; y
        // PIPE antes que IR porque la palabra PIPE no lleva W ni HSS.
        if (p.StartsWith("HSS", StringComparison.Ordinal) ||
            p.StartsWith("OR", StringComparison.Ordinal) ||
            p.StartsWith("PTR", StringComparison.Ordinal))
        {
            return Or;
        }

        if (p.StartsWith("PIPE", StringComparison.Ordinal) ||
            p.StartsWith("OC", StringComparison.Ordinal) ||
            p.StartsWith("TUBO", StringComparison.Ordinal))
        {
            return Oc;
        }

        if (p.StartsWith("CF", StringComparison.Ordinal) ||
            p.StartsWith("CANAL", StringComparison.Ordinal) ||
            p.StartsWith("MONTEN", StringComparison.Ordinal))
        {
            return Cf;
        }

        if (p.StartsWith("W", StringComparison.Ordinal) ||
            p.StartsWith("IR", StringComparison.Ordinal) ||
            p.StartsWith("IPR", StringComparison.Ordinal))
        {
            return Ir;
        }

        return null;
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
/// el OC y T-W para el CF.
/// </para>
/// <para>
/// <b>Aquí es una sola tabla.</b> Cuatro bloques de columnas separados obligan a saberse de
/// memoria en qué zona de la hoja se captura cada cosa, y dejan el 75 % de la fila en
/// blanco siempre. Con una tabla y una columna de familia, las dimensiones se llaman por lo
/// que son —peralte, ancho, espesores— y cada familia usa las que necesita:
/// </para>
/// <list type="table">
///   <listheader><term>Familia</term><description>Columnas que usa</description></listheader>
///   <item>
///     <term>IR</term>
///     <description>Peralte (<c>d</c>), ancho de patín (<c>bf</c>), espesor de alma
///     (<c>tw</c>) y espesor de patín (<c>tf</c>).</description>
///   </item>
///   <item>
///     <term>OR</term>
///     <description>Peralte, ancho y espesor de pared. El radio de esquina no se captura:
///     la macro lo fija en el propio espesor por fuera y en su mitad por dentro.</description>
///   </item>
///   <item>
///     <term>OC</term>
///     <description>Peralte, que aquí es el <b>diámetro exterior</b>, y espesor de
///     pared.</description>
///   </item>
///   <item>
///     <term>CF</term>
///     <description>Peralte, ancho, espesor, <b>labio</b> y <b>radio</b> de doblez, que son
///     las dos que solo usa esta familia.</description>
///   </item>
/// </list>
/// </remarks>
public sealed class PerfilAceroRow : Row
{
    private string _familia = FamiliaPerfil.Ir;
    private string _perfil = "W12X30";
    private string _id = "V-1";
    private string _elemento = ElementoViga;
    private string _clasificacion = string.Empty;
    private string _acero = AceroA36;
    private bool _doble;

    private double _peralteCm = 30.3;
    private double _anchoCm = 16.5;
    private double _espesorAlmaCm = 0.65;
    private double _espesorPatinCm = 1.1;
    private double _labioCm;
    private double _radioCm;

    /// <summary>Elementos de acero que se rotulan, en el orden de las macros.</summary>
    public const string ElementoViga = "VIGA";

    /// <inheritdoc cref="ElementoViga"/>
    public const string ElementoColumna = "COLUMNA";

    /// <inheritdoc cref="ElementoViga"/>
    public const string ElementoTensor = "TENSOR";

    /// <summary>Los aceros que salen en el rótulo. Es texto libre, no una lista cerrada.</summary>
    public const string AceroA36 = "A-36";

    /// <inheritdoc cref="AceroA36"/>
    public const string AceroA572 = "A-572 GR. 50";

    /// <summary>El de los perfiles I laminados de hoy.</summary>
    public const string AceroA992 = "A-992";

    /// <summary>El de los tubos estructurales rectangulares.</summary>
    public const string AceroA500B = "A-500 GR. B";

    /// <summary>El de los tubos redondos.</summary>
    public const string AceroA53B = "A-53 GR. B";

    /// <summary>Los elementos del desplegable.</summary>
    public static readonly string[] Elementos =
        { ElementoViga, ElementoColumna, ElementoTensor, "PUNTAL", "LARGUERO", "ATIESADOR" };

    /// <summary>
    /// Los aceros del desplegable, cada uno junto a la familia que lo usa.
    /// </summary>
    public static readonly string[] Aceros =
        { AceroA36, AceroA572, AceroA992, AceroA500B, AceroA53B };

    /// <summary>
    /// Clasificación de la viga, que en el rótulo va pegada a la palabra VIGA.
    /// </summary>
    /// <remarks>
    /// Solo la usa el IR, y solo cuando el elemento es VIGA: es la columna D de la fila de
    /// información en la macro, y de ahí sale el renglón «VIGA PRINCIPAL "V-1"».
    /// </remarks>
    public static readonly string[] Clasificaciones =
        { string.Empty, "PRINCIPAL", "SECUNDARIA", "DE BORDE", "DE PISO", "DE TECHO" };

    /// <summary>Familia del perfil. Decide qué se dibuja y qué columnas se usan.</summary>
    public string Familia
    {
        get => _familia;
        set
        {
            Set(ref _familia, (value ?? string.Empty).Trim().ToUpperInvariant());
            Raise(nameof(PerfilRotulo));
            Raise(nameof(FaltanDatos));
        }
    }

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
        }
    }

    /// <summary>Identificador de la sección. Es el nombre del bloque en AutoCAD.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Tipo de elemento: VIGA, COLUMNA, TENSOR…</summary>
    public string Elemento
    {
        get => _elemento;
        set
        {
            Set(ref _elemento, value);
            Raise(nameof(ElementoRotulo));
        }
    }

    /// <summary>Clasificación de la viga, si la lleva.</summary>
    public string Clasificacion
    {
        get => _clasificacion;
        set
        {
            Set(ref _clasificacion, value);
            Raise(nameof(ElementoRotulo));
        }
    }

    /// <summary>Tipo de acero, para el renglón «ACERO …» del rótulo.</summary>
    public string Acero { get => _acero; set => Set(ref _acero, value); }

    /// <summary>
    /// Perfil <b>doble</b>: dos perfiles juntos, como en la columna «SI» de la macro.
    /// </summary>
    /// <remarks>
    /// En el IR, el OR y el OC los dos van uno al lado del otro. En el CF el segundo va
    /// <b>espejeado</b>, que es como se arma un cajón con dos canales enfrentadas.
    /// </remarks>
    public bool Doble { get => _doble; set => Set(ref _doble, value); }

    /// <summary>Peralte. En el OC es el <b>diámetro exterior</b>.</summary>
    public double PeralteCm { get => _peralteCm; set { Set(ref _peralteCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Ancho del patín en el IR y el CF, ancho de la cara en el OR.</summary>
    public double AnchoCm { get => _anchoCm; set { Set(ref _anchoCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Espesor del alma en el IR; espesor de pared en el OR, el OC y el CF.</summary>
    public double EspesorAlmaCm { get => _espesorAlmaCm; set { Set(ref _espesorAlmaCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Espesor del patín. Solo lo usa el IR.</summary>
    public double EspesorPatinCm { get => _espesorPatinCm; set { Set(ref _espesorPatinCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Largo del labio. Solo lo usa el CF.</summary>
    public double LabioCm { get => _labioCm; set { Set(ref _labioCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>Radio de doblez exterior. Solo lo usa el CF.</summary>
    public double RadioCm { get => _radioCm; set { Set(ref _radioCm, value); Raise(nameof(FaltanDatos)); } }

    /// <summary>
    /// El nombre del perfil <b>como va en el plano</b>, en nomenclatura mexicana.
    /// </summary>
    /// <remarks>
    /// Es la traducción que hacen las macros al rotular: <c>W</c> a <c>IR</c>, <c>HSS</c> a
    /// <c>OR</c>, <c>PIPE</c> a <c>OC</c>, y en el CF el <c>#</c> del calibre a
    /// <c>CAL </c>. Se muestra en la cuadrícula para que se vea antes de dibujar.
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
        if (f == FamiliaPerfil.Ir && s.StartsWith("W", StringComparison.Ordinal))
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

        // El calibre. Va en las cuatro familias porque el «#14» de un CF y el de una
        // lámina se escriben igual, y en el plano se lee «CAL 14».
        return s.Replace("#", "CAL ");
    }

    private static string Reemplazar(string texto, string viejo, string nuevo) =>
        texto.StartsWith(viejo, StringComparison.Ordinal)
            ? nuevo + texto.Substring(viejo.Length)
            : texto;

    /// <summary>
    /// El primer renglón del rótulo: el elemento con su clasificación y el ID.
    /// </summary>
    /// <remarks>
    /// Port de <c>ConstruirLinea1Rotulo</c>. La clasificación solo se pega cuando el
    /// elemento es VIGA, que es lo que hace la macro: una «COLUMNA SECUNDARIA» no
    /// significa nada.
    /// </remarks>
    public string ElementoRotulo
    {
        get
        {
            var elem = (_elemento ?? string.Empty).Trim().ToUpperInvariant();
            var clasif = (_clasificacion ?? string.Empty).Trim().ToUpperInvariant();

            if (elem == ElementoViga && clasif.Length > 0)
            {
                return elem + " " + clasif;
            }

            return elem;
        }
    }

    /// <summary>
    /// Qué le falta a esta fila para poder dibujarse, o cadena vacía si no le falta nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se muestra como columna calculada porque cada familia pide unas dimensiones y no
    /// otras, y sin esto la única forma de enterarse sería que el dibujo saliera raro. Es la
    /// misma idea que la cuantía de la hoja de concreto: una columna que se calcula sola y
    /// dice si los datos se sostienen.
    /// </para>
    /// <para>
    /// El OC no pide ancho porque es redondo; el OR no pide espesor de patín porque su pared
    /// es una sola; y el radio del CF <b>sí</b> puede ser cero: un canal doblado en pico es
    /// raro pero se dibuja.
    /// </para>
    /// </remarks>
    public string FaltanDatos
    {
        get
        {
            var faltan = new List<string>();

            if (_peralteCm <= 0)
            {
                faltan.Add(_familia == FamiliaPerfil.Oc ? "diametro" : "peralte");
            }

            if (_espesorAlmaCm <= 0)
            {
                faltan.Add(_familia == FamiliaPerfil.Ir ? "e alma" : "espesor");
            }

            if (_familia != FamiliaPerfil.Oc && _anchoCm <= 0)
            {
                faltan.Add("ancho");
            }

            if (_familia == FamiliaPerfil.Ir && _espesorPatinCm <= 0)
            {
                faltan.Add("e patin");
            }

            if (_familia == FamiliaPerfil.Cf && _labioCm <= 0)
            {
                faltan.Add("labio");
            }

            // Y las comprobaciones de que el perfil CABE en sí mismo. Sin esto, un espesor
            // mayor que el medio peralte dibuja un perfil con el alma cruzada, que en el
            // plano se ve como un borrón.
            if (faltan.Count == 0)
            {
                switch (_familia)
                {
                    case FamiliaPerfil.Ir when 2 * _espesorPatinCm >= _peralteCm:
                        faltan.Add("los dos patines no caben en el peralte");
                        break;

                    case FamiliaPerfil.Ir when _espesorAlmaCm >= _anchoCm:
                        faltan.Add("el alma es mas ancha que el patin");
                        break;

                    case FamiliaPerfil.Or when 2 * _espesorAlmaCm >= Math.Min(_peralteCm, _anchoCm):
                        faltan.Add("la pared no deja hueco interior");
                        break;

                    case FamiliaPerfil.Oc when 2 * _espesorAlmaCm >= _peralteCm:
                        faltan.Add("la pared no deja hueco interior");
                        break;

                    case FamiliaPerfil.Cf when 2 * _espesorAlmaCm >= _peralteCm:
                        faltan.Add("los dos patines no caben en el peralte");
                        break;

                    case FamiliaPerfil.Cf when _labioCm <= _espesorAlmaCm:
                        faltan.Add("el labio no llega ni al espesor");
                        break;
                }
            }

            return faltan.Count == 0 ? string.Empty : string.Join(", ", faltan);
        }
    }

    protected override void RaiseCalculadas()
    {
        Raise(nameof(PerfilRotulo));
        Raise(nameof(ElementoRotulo));
        Raise(nameof(FaltanDatos));
    }
}
