using System.Collections.ObjectModel;
using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Una placa base de la hoja, con las mismas celdas que la macro leía de Excel.
/// </summary>
/// <remarks>
/// <para>
/// Cada propiedad lleva en su comentario <b>la celda de la que salía</b> en la hoja
/// <c>DibujarPlacaBase_BloqueXX</c>. Eso permite comparar la captura contra la hoja de siempre sin
/// tener que reconstruir de memoria qué era cada cosa, y es lo que hace que un dato que se
/// interpretaba al revés —como la longitud de los cartabones— se pueda encontrar.
/// </para>
/// <para>
/// <b>Las unidades se capturan como en la hoja</b>: la placa en centímetros y las anclas, agujeros,
/// espesores y soldadura en <b>pulgadas</b>, admitiendo fracciones —<c>5/8</c>, <c>1 1/4</c>—.
/// Convertir aquí a centímetros obligaría al usuario a hacer la cuenta, que es justo lo que la hoja
/// le ahorraba. La conversión vive en <see cref="AFormatoCad"/>, en un solo sitio.
/// </para>
/// </remarks>
public sealed class PlacaBaseRow : Row
{
    private string _marca = "PB-1";
    private double _largoCm = 40;
    private double _anchoCm = 40;
    private string _espesor = "1";
    private string _aceroPlaca = "A-36";
    private string _idDado = string.Empty;
    private double _dadoXCm;
    private double _dadoYCm;
    private bool _dadoCircular;
    private string _familia = FamiliaPerfil.Ir;
    private string _seccion = string.Empty;
    private int _nAnclasX = 4;
    private int _nAnclasY;
    private double _sepBordeXCm;
    private double _sepBordeYCm;
    private string _diamAnclaX = "3/4";
    private string _diamAnclaY = "3/4";
    private string _electrodo = "E70XX";
    private string _soldadura = "1/4";
    private int _nCartabonesX;
    private int _nCartabonesY;
    private string _soldaduraCartabon = "3/16";
    private string _espCartabonX = "1/2";
    private string _espCartabonY = "1/2";
    private double _longCartabonXCm = 15;
    private double _longCartabonYCm = 15;

    // LO QUE SOLO SE VE EN EL ALZADO. En planta el cartabon y el ancla se ven de canto, asi que ni
    // la altura del uno ni el ahogo del otro aparecen por ningun lado: son los datos que la macro
    // lee de F18, F19, E12 y E13 y que solo el alzado dibuja.
    private double _altoCartabonXCm = 20;
    private double _altoCartabonYCm = 20;
    private double _longAnclajeXCm = 30;
    private double _longAnclajeYCm = 30;

    // La longitud TOTAL del ancla, en CERO y sin casilla en la hoja: la que manda es la vertical de
    // arriba, y esta se quedo como respaldo de los trabajos guardados que solo la traigan a ella.
    // Nacia en 45, y con la regla nueva eso seria un dato fantasma que nadie puede ver ni cambiar.
    private double _longAnclaXCm;
    private double _longAnclaYCm;

    // El doblez convierte el ancla en una L y le quita el travesano del extremo.
    private double _doblezAnclaXCm = 10;
    private double _doblezAnclaYCm = 10;
    private bool _conCartabones;

    // LA CAMA DE GROUT. En NO de fabrica: el detalle sale como siempre, con la placa apoyada
    // directamente en el dado. Los 2.5 cm son el espesor usual de una cama nivelante.
    private string _tipoPlaca = TipoBase;
    private string _grout = "NO";
    private double _espesorGroutCm = 2.5;

    private double _escala = 10;
    private bool _girarPlaca90 = true;

    /// <summary>Celda <b>E2</b>: la marca de la placa. Va al rótulo.</summary>
    public string Marca { get => _marca; set => Set(ref _marca, value); }

    /// <summary>Celda <b>C5</b>: largo de la placa, en cm.</summary>
    public double LargoCm { get => _largoCm; set => Set(ref _largoCm, value); }

    /// <summary>Celda <b>C6</b>: ancho de la placa, en cm.</summary>
    public double AnchoCm { get => _anchoCm; set => Set(ref _anchoCm, value); }

    /// <summary>Celda <b>E6</b>: espesor de la placa, en pulgadas. Admite fracción.</summary>
    public string Espesor { get => _espesor; set => Set(ref _espesor, value); }

    /// <summary>Celda <b>E5</b>: tipo de acero de la placa.</summary>
    public string AceroPlaca { get => _aceroPlaca; set => Set(ref _aceroPlaca, value); }

    // ======================================================================
    //  QUÉ CLASE DE PLACA ES
    // ======================================================================

    /// <summary>
    /// <b>PLACA BASE</b> —se apoya en un dado— o <b>PLACA A MURO</b> —en una cadena o una trabe—.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo pidió el usuario después de un caso real: había puesto <c>CC-1</c> —una cadena de
    /// cerramiento— en el ID del apoyo, y la hoja solo buscaba <b>dados</b>. La referencia no
    /// encontraba nada, no decía nada, y la placa se quedaba con el apoyo en cero: el detalle iba a
    /// salir sin concreto y sin dónde ahogar las anclas.
    /// </para>
    /// <para>
    /// Con <b>PLACA A MURO</b> el desplegable del ID ofrece las piezas <b>horizontales</b> de la
    /// hoja de concreto —trabes, contratrabes y las tres cadenas— en lugar de los dados, y de ahí
    /// salen las medidas del apoyo igual que salían las del dado.
    /// </para>
    /// <para>
    /// <b>Lo que NO cambia es la geometría del detalle.</b> La placa se sigue dibujando apoyada
    /// sobre su pieza de concreto, con las anclas ahogadas en ella, porque es el mismo detalle: una
    /// placa sobre una cadena de cerramiento se dibuja como una placa sobre un dado, solo que el
    /// concreto de abajo es la cadena. Lo que cambia es de dónde salen las medidas, qué ofrece el
    /// desplegable y cómo se rotula. Si algún día hace falta el otro caso —la placa atornillada a
    /// la <i>cara</i> vertical del muro, con las anclas horizontales— ese sí es un detalle nuevo.
    /// </para>
    /// </remarks>
    public string TipoPlaca
    {
        get => _tipoPlaca;
        set
        {
            Set(ref _tipoPlaca, (value ?? string.Empty).Trim().ToUpperInvariant());

            // La lista del desplegable del ID depende de esto, y el texto de la columna del apoyo
            // también: sin estos avisos, al cambiar el tipo la celda seguiría ofreciendo dados.
            Raise(nameof(ApoyosDisponibles));
            Raise(nameof(EsPlacaAMuro));
        }
    }

    /// <summary>Los dos tipos, para el desplegable de la celda.</summary>
    public static string[] TiposDePlaca => new[] { TipoBase, TipoMuro };

    /// <summary>El valor de <see cref="TipoPlaca"/> de una placa que se apoya en un dado.</summary>
    /// <remarks>
    /// Es el <b>valor por omisión</b>, y lo es porque es el caso normal: una placa base. Así una
    /// hoja capturada antes de que esto existiera —donde el archivo no trae la columna— sigue
    /// leyéndose como lo que era.
    /// </remarks>
    public const string TipoBase = "PLACA BASE";

    /// <summary>El valor de <see cref="TipoPlaca"/> de una placa sobre una cadena o una trabe.</summary>
    public const string TipoMuro = "PLACA A MURO";

    /// <summary>¿Es de las que se apoyan en una cadena o una trabe?</summary>
    public bool EsPlacaAMuro =>
        _tipoPlaca.Equals(TipoMuro, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ID del <b>apoyo</b> de la hoja de secciones de concreto, del que salen sus medidas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La macro pedía el dado a mano, en dos celdas —D7 y E7—, y eso es capturar dos veces el mismo
    /// dato: el dado ya está en la hoja de concreto, con su armado y su recubrimiento, porque es
    /// una sección que se dibuja por su cuenta. De los dos sitios el segundo es el que se
    /// equivoca, y no se ve: una placa que dice 50×50 sobre un dado que se armó de 45 sale con la
    /// placa volando 2.5 cm por cada lado y nada en la tabla lo delata.
    /// </para>
    /// <para>
    /// Al elegirlo, <see cref="DadoXCm"/>, <see cref="DadoYCm"/> y <see cref="DadoCircular"/> se
    /// llenan solos y <b>se vuelven a poner al día</b> si esa sección cambia. Las celdas siguen
    /// siendo editables para un caso a mano, pero lo escrito a mano no gana contra la sección: la
    /// referencia es la sección.
    /// </para>
    /// <para>
    /// En blanco se queda como estaba la macro: el dado se captura a mano en las dos celdas.
    /// </para>
    /// </remarks>
    public string IdDado
    {
        get => _idDado;
        set => Set(ref _idDado, ZapataAisladaRow.SoloElId(value));
    }

    /// <summary>
    /// Los dados capturados en la hoja de concreto, para el desplegable de la celda.
    /// </summary>
    /// <remarks>
    /// <b>Es la lista de la hoja de zapatas, la misma.</b> La mantiene al día
    /// <c>ActualizarDadosDisponibles</c> en cada cambio, y compartirla es lo que garantiza que las
    /// dos hojas ofrezcan exactamente los mismos dados: con una lista propia habría dos sitios que
    /// recorren la hoja de concreto buscando dados, y el día que cambie el criterio —hoy es
    /// «DADO» y «DADO CIRCULAR»— uno de los dos se quedaría corto sin que nada avisara.
    /// </remarks>
    public static ObservableCollection<string> DadosDisponibles => ZapataAisladaRow.DadosDisponibles;

    /// <summary>
    /// Las <b>cadenas y trabes</b> capturadas en la hoja de concreto, para una placa a muro.
    /// </summary>
    /// <remarks>
    /// La mantiene <c>ActualizarApoyosDeMuroDisponibles</c> con el mismo criterio que la de dados
    /// —<c>EsApoyoDeMuro</c>—, y es estática por lo mismo: la lista es del programa, no de la fila.
    /// </remarks>
    public static ObservableCollection<string> ApoyosDeMuroDisponibles { get; } = new();

    /// <summary>
    /// Lo que ofrece el desplegable del ID en <b>esta</b> fila, según su tipo de placa.
    /// </summary>
    /// <remarks>
    /// Es una propiedad de la FILA y no una lista de la columna, por lo mismo que los perfiles de
    /// la familia: cada fila puede ser de un tipo distinto, así que la lista de la celda depende de
    /// su propio renglón. Una lista por columna solo podría ofrecer dados y cadenas mezclados, que
    /// es justo lo que hace que se elija el que no toca.
    /// </remarks>
    public ObservableCollection<string> ApoyosDisponibles =>
        EsPlacaAMuro ? ApoyosDeMuroDisponibles : DadosDisponibles;

    /// <summary>Celda <b>D7</b>: dado de concreto en X, en cm. Cero = sin dado.</summary>
    /// <remarks>Si es redondo, es su <b>diámetro</b>.</remarks>
    public double DadoXCm { get => _dadoXCm; set => Set(ref _dadoXCm, value); }

    /// <summary>Celda <b>E7</b>: dado de concreto en Y, en cm.</summary>
    public double DadoYCm { get => _dadoYCm; set => Set(ref _dadoYCm, value); }

    /// <summary>El dado es <b>redondo</b>. Lo dice su sección, no se captura aquí.</summary>
    public bool DadoCircular { get => _dadoCircular; set => Set(ref _dadoCircular, value); }

    /// <summary>Celda <b>C8</b>: familia del perfil de la columna.</summary>
    /// <remarks>
    /// Se guarda en MAYÚSCULAS porque con eso se busca en el catálogo: escribir <c>ir</c> en la
    /// celda dejaría la lista de perfiles vacía sin que nada explicara por qué.
    /// </remarks>
    public string Familia
    {
        get => _familia;
        set
        {
            Set(ref _familia, (value ?? string.Empty).Trim().ToUpperInvariant());

            // Al cambiar de familia cambia la lista de perfiles que ofrece la celda de al lado.
            Raise(nameof(PerfilesDeLaFamilia));
        }
    }

    /// <summary>
    /// Los perfiles del catálogo de <b>esta</b> familia, para el desplegable de la celda.
    /// </summary>
    /// <remarks>
    /// Es una propiedad de la FILA y no una lista de la columna, por lo mismo que en la hoja de
    /// acero: cada fila puede ser de otra familia, así que la lista de la celda depende de su
    /// propio renglón. Una lista por columna solo podría ofrecer todos los perfiles mezclados.
    /// </remarks>
    public string[] PerfilesDeLaFamilia => CatalogoPerfiles.NombresDe(_familia);

    // ======================================================================
    //  LAS LISTAS DE LAS CELDAS EN FRACCIONES
    // ======================================================================

    /// <summary>Los diámetros de ancla usuales, en fracciones de pulgada.</summary>
    /// <remarks>
    /// <para>
    /// Son <b>propiedades de la fila</b> y no listas puestas desde el código en la columna, porque
    /// esas celdas son desplegables <c>IsEditable</c>: se puede teclear una medida que no esté en
    /// la lista, y un <c>DataGridComboBoxColumn</c> con <c>SelectedItemBinding</c> descarta lo
    /// escrito si no coincide con una entrada. Ligadas a la fila, la lista es un atajo y no un
    /// límite.
    /// </para>
    /// <para>
    /// Y son <c>static readonly</c> por dentro: la misma tabla en memoria para todas las filas.
    /// </para>
    /// </remarks>
    public string[] DiametrosAncla => _diametrosAncla;

    /// <summary>Los espesores de placa y de cartabón usuales, en pulgadas.</summary>
    public string[] EspesoresPlaca => _espesoresPlaca;

    /// <summary>Los espesores de soldadura, con el vacío al principio: vacío = sin soldadura.</summary>
    public string[] EspesoresSoldadura => _espesoresSoldadura;

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  LOS DIECINUEVE DIÁMETROS DEL CUADRO —más el de 3/8"—, en orden y con su equivalente
    //  en mm.
    //
    //  Del 1/2" para arriba son exactamente los renglones del cuadro Hylsa ES-03-001 del que
    //  salen J, K y L. Antes la lista tenía OCHO, y le faltaban los once de arriba: un ancla
    //  de 2" había que teclearla a mano. Y peor, faltaba justo el tramo donde el cuadro se
    //  pone exigente —una de 4" pide 300 mm entre anclas— así que lo que no estaba a un clic
    //  era lo que más cuidado necesita.
    //
    //  ─── EL 3/8" NO ESTÁ EN EL CUADRO, Y ESTÁ AQUÍ A PROPÓSITO ──────────────────────────
    //  Lo pidió el usuario, y es un ancla que se usa: placas base ligeras de marquesina, de
    //  poste, de equipo. Pero el cuadro EMPIEZA en 13 mm —1/2"— y no tiene renglón para los
    //  9.53 mm del 3/8".
    //
    //  Y no se le inventa uno. Lo que hacen las tres tablas es el criterio del propio
    //  estándar: un diámetro que no tiene renglón se resuelve por el INMEDIATO SUPERIOR, y
    //  por debajo del primero el inmediato superior es el primero. O sea que un ancla de
    //  3/8" se libra con los números del 1/2" —J=40, K=22, L=23 mm—, que es EXIGIRLE MÁS de
    //  lo que necesitaría, nunca menos: el plano cumple de sobra. Inventar el renglón que
    //  falta sería lo contrario: aflojarlo con números que el estándar no firma.
    //
    //  La regla que sigue en pie —y que la comprobación vigila— es que aquí no entre un
    //  diámetro que caiga ENTRE dos renglones del cuadro: ese sí engaña, porque el usuario
    //  leería la fila de su ancla y estaría leyendo la de otra sin que nada se lo diga. Por
    //  debajo del primer renglón no hay ambigüedad posible, y el globo de la celda lo dice.
    // ═══════════════════════════════════════════════════════════════════════════════════════
    private static readonly string[] _diametrosAncla =
    {
        "3/8",      // 10 mm - fuera del cuadro: se libra con el renglon de media pulgada
        "1/2",      // 13 mm
        "5/8",      // 16 mm
        "3/4",      // 19 mm
        "7/8",      // 22 mm
        "1",        // 25 mm
        "1 1/8",    // 29 mm
        "1 1/4",    // 32 mm
        "1 3/8",    // 35 mm
        "1 1/2",    // 38 mm
        "1 5/8",    // 41 mm
        "1 3/4",    // 44 mm
        "1 7/8",    // 48 mm
        "2",        // 51 mm
        "2 1/4",    // 57 mm
        "2 1/2",    // 64 mm
        "2 3/4",    // 70 mm
        "3",        // 76 mm
        "3 1/2",    // 89 mm
        "4"         // 102 mm
    };

    private static readonly string[] _espesoresPlaca =
        { "1/4", "5/16", "3/8", "1/2", "5/8", "3/4", "1", "1 1/4", "1 1/2", "2" };

    // LA LISTA DE SOLDADURA, COMPLETA. Antes iba de 3/16 a 1/2, que deja fuera los dos extremos que
    // sí se usan: el filete de 1/8 de una placa delgada y los de 5/8 en adelante de una columna de
    // varias toneladas. La celda es editable, así que faltar en la lista no impedía capturarlos,
    // pero obligaba a teclear a mano lo que debería estar a un clic.
    private static readonly string[] _espesoresSoldadura =
    {
        string.Empty,
        "1/8", "3/16", "1/4", "5/16", "3/8", "7/16", "1/2", "9/16", "5/8", "3/4", "7/8", "1"
    };

    /// <summary>Celda <b>C9</b>: designación del perfil, del catálogo.</summary>
    public string Seccion { get => _seccion; set => Set(ref _seccion, value); }

    /// <summary>
    /// Celda <b>C11</b>: número de anclas en X. Se reparten a lo <b>ancho</b>, en horizontal.
    /// </summary>
    /// <remarks>
    /// Ojo con el cruce de la hoja, que la macro documenta: la fila 10 es «N. de Anclas (y)» y la
    /// 11 es «N. de Anclas (X)». Aquí el nombre dice la dirección, no la fila.
    /// </remarks>
    public int NAnclasX { get => _nAnclasX; set => Set(ref _nAnclasX, value); }

    /// <summary>Celda <b>C10</b>: número de anclas en Y. Se reparten a lo <b>alto</b>.</summary>
    public int NAnclasY { get => _nAnclasY; set => Set(ref _nAnclasY, value); }

    /// <summary>
    /// Celda <b>E11</b>: separación al borde en X, en cm. Cero = automática.
    /// </summary>
    /// <remarks>
    /// <b>Se corrige sola si no llega al mínimo de la columna K</b> —la distancia del ancla al canto
    /// recortado de la placa— para el diámetro de su ancla. Ver <see cref="AjustarSeparacionesAlBorde"/>.
    /// </remarks>
    public double SepBordeXCm { get => _sepBordeXCm; set => Set(ref _sepBordeXCm, value); }

    /// <summary>Celda <b>E10</b>: separación al borde en Y, en cm. Cero = automática. Se corrige sola.</summary>
    public double SepBordeYCm { get => _sepBordeYCm; set => Set(ref _sepBordeYCm, value); }

    /// <summary>
    /// Sube las separaciones al borde capturadas hasta el mínimo de la <b>columna K</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La llama la hoja <b>al salir de la celda</b>, no en el <c>set</c>. Corrigiendo en el <c>set</c>
    /// —que con <c>UpdateSourceTrigger=PropertyChanged</c> se dispara en cada tecla— escribir «5»
    /// para llegar a «50» se convertiría en un forcejeo: la celda subiría el 5 al mínimo antes de
    /// que se llegue a teclear el 0. Al salir de la celda el usuario ya terminó de escribir.
    /// </para>
    /// <para>
    /// Una separación en <b>cero</b> se deja en cero: cero significa «calcúlala», y el cálculo
    /// automático ya aplica el mismo mínimo por su cuenta.
    /// </para>
    /// <para>
    /// Devuelve <c>true</c> si movió algo, para que la hoja pueda decirlo en lugar de cambiar un
    /// número delante del usuario sin explicación.
    /// </para>
    /// </remarks>
    public bool AjustarSeparacionesAlBorde()
    {
        var p = AFormatoCad();

        var x = AnclasPlacaBase.SepBordeAjustada(SepBordeXCm, p.DiamAnclaXCm, p.AnchoDibujoCm);
        var y = AnclasPlacaBase.SepBordeAjustada(SepBordeYCm, p.DiamAnclaYCm, p.AltoDibujoCm);

        var movio = false;

        if (Math.Abs(x - SepBordeXCm) > 1e-9)
        {
            SepBordeXCm = x;
            movio = true;
        }

        if (Math.Abs(y - SepBordeYCm) > 1e-9)
        {
            SepBordeYCm = y;
            movio = true;
        }

        return movio;
    }

    /// <summary>
    /// La distancia mínima al <b>canto de la placa</b> que exige la columna K, en cm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se ven en la tabla porque son los números que explican dos cosas que si no pasan sin decir
    /// por qué: que una separación al borde se corrigió sola —eso es la <b>K</b>— y que el detalle
    /// se negó a dibujarse porque no cabe la llave —eso es la <b>L</b>—. Sin ellos, la celda cambia
    /// de valor o el botón no hace nada, y el usuario no tiene de dónde sacar el motivo.
    /// </para>
    /// <para>
    /// Van los dos juntos y no en columnas separadas porque se leen juntos: son las dos paredes
    /// entre las que tiene que caber el ancla.
    /// </para>
    /// </remarks>
    public string BordeMinimo
    {
        get
        {
            var p = AFormatoCad();

            var k = Math.Max(
                AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaXCm),
                AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaYCm));

            var l = Math.Max(
                AnclasPlacaBase.HolguraColumnaMinimaCm(p.DiamAnclaXCm),
                AnclasPlacaBase.HolguraColumnaMinimaCm(p.DiamAnclaYCm));

            if (k <= 0 && l <= 0)
            {
                return string.Empty;
            }

            // Se dice el MAYOR de las dos direcciones: es el que manda, y con dos anclas de distinto
            // diámetro cuatro números en una celda no se leen.
            return $"K {k:0.#} · L {l:0.#} cm";
        }
    }

    /// <summary>
    /// La separación al borde que se va a <b>usar de verdad</b>, ya ajustada, en cm.
    /// </summary>
    /// <remarks>
    /// Hace visible el número final, que es distinto de lo capturado en dos casos: cuando la celda
    /// va en cero —automática— y cuando lo capturado no llegaba al mínimo de K. En los dos, sin
    /// esta columna el usuario ve una cosa en la celda y el plano sale con otra.
    /// </remarks>
    public string SepBordeUsada
    {
        get
        {
            var p = AFormatoCad();

            if (p.AnchoDibujoCm <= 0 || p.AltoDibujoCm <= 0)
            {
                return string.Empty;
            }

            var dAguX = p.DiamAgujeroXCm > 0 ? p.DiamAgujeroXCm : p.DiamAnclaXCm + (2.54 / 16);
            var dAguY = p.DiamAgujeroYCm > 0 ? p.DiamAgujeroYCm : p.DiamAnclaYCm + (2.54 / 16);

            var x = AnclasPlacaBase.SepBordeAjustada(p.SepBordeXCm, p.DiamAnclaXCm, p.AnchoDibujoCm);
            var y = AnclasPlacaBase.SepBordeAjustada(p.SepBordeYCm, p.DiamAnclaYCm, p.AltoDibujoCm);

            // Y si va en cero, la automática: la MISMA llamada que hace el dibujante, con el mismo
            // mínimo de K. Repetir aquí una versión simplificada sería enseñar un número que el
            // plano no usa.
            if (x <= 0)
            {
                x = AnclasPlacaBase.SepAuto(
                    p.AnchoDibujoCm, p.PerfilXDibujoCm, dAguX, 1,
                    AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaXCm));
            }

            if (y <= 0)
            {
                y = AnclasPlacaBase.SepAuto(
                    p.AltoDibujoCm, p.PerfilYDibujoCm, dAguY, 1,
                    AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaYCm));
            }

            return $"{x:0.#} / {y:0.#} cm";
        }
    }

    /// <summary>Celda <b>C14</b>: diámetro de las anclas en X, en pulgadas.</summary>
    /// <remarks>Al cambiarlo, el <b>agujero</b> se recalcula solo. Ver <see cref="DiamAgujeroX"/>.</remarks>
    public string DiamAnclaX { get => _diamAnclaX; set => Set(ref _diamAnclaX, value); }

    /// <summary>Celda <b>C15</b>: diámetro de las anclas en Y, en pulgadas.</summary>
    public string DiamAnclaY { get => _diamAnclaY; set => Set(ref _diamAnclaY, value); }

    /// <summary>
    /// Celda <b>E14</b>: diámetro del agujero en X. Es el ancla <b>más 1/16"</b>, y no se captura.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es una columna calculada, no un dato.</b> El agujero de una placa base es siempre el ancla
    /// más 1/16" de holgura, así que no hay nada que decidir: dejarlo capturable era ofrecer una
    /// decisión que no existe, y con ella la posibilidad de que el agujero y su ancla dejaran de
    /// corresponder sin que nada lo dijera.
    /// </para>
    /// <para>
    /// Antes esta cuenta se hacía por dentro y al dibujar, con la celda en blanco. Ahora se ve el
    /// número, que es lo que permite cotejarlo con el plano en lugar de fiarse.
    /// </para>
    /// <para>
    /// Al ser de solo lectura tampoco se guarda en el <c>.clk</c> —<c>FilaSerializable</c> solo
    /// guarda propiedades que se pueden escribir— y eso es lo correcto: se recalcula al abrir a
    /// partir del ancla, así que no puede llegar viejo.
    /// </para>
    /// </remarks>
    public string DiamAgujeroX => AgujeroAutomatico(_diamAnclaX);

    /// <summary>Celda <b>E15</b>: diámetro del agujero en Y. Ver la nota de X.</summary>
    public string DiamAgujeroY => AgujeroAutomatico(_diamAnclaY);

    /// <summary>
    /// El agujero que le toca a un ancla: su diámetro <b>más 1/16"</b>, en fracción.
    /// </summary>
    /// <remarks>
    /// Devuelve vacío si el ancla no se entiende o es cero, porque entonces no hay agujero que
    /// proponer: escribir «1/16» ahí sería inventarse un dato.
    /// </remarks>
    public static string AgujeroAutomatico(string? ancla)
    {
        var d = Pulgadas(ancla);

        return d <= 0 ? string.Empty : ComoFraccion(d + (1.0 / 16.0));
    }

    /// <summary>
    /// Escribe unas pulgadas como <b>fracción</b>: <c>13/16</c>, <c>1 1/16</c>, <c>1 3/16</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el camino de vuelta de <see cref="Pulgadas"/>, y hace falta por lo mismo que ella: en el
    /// taller los diámetros se piden en fracciones. Un agujero que en el plano dijera <c>0.8125"</c>
    /// obligaría a traducir en obra, que es justo lo que la hoja ahorra.
    /// </para>
    /// <para>
    /// Se prueba en dieciseisavos, luego en treintaidosavos y luego en sesentaicuatroavos, y se
    /// reduce. Las medidas de taller caen todas ahí: un ancla de 3/4 más 1/16 son 13/16 exactos. Si
    /// el número no es una fracción de esas —porque alguien capturó un decimal cualquiera— se
    /// escribe como decimal antes que redondear en silencio a la fracción de al lado.
    /// </para>
    /// </remarks>
    public static string ComoFraccion(double pulgadas)
    {
        if (pulgadas <= 0)
        {
            return string.Empty;
        }

        foreach (var den in new[] { 16, 32, 64 })
        {
            var exacto = pulgadas * den;
            var redondo = Math.Round(exacto);

            if (Math.Abs(exacto - redondo) > 1e-6 || redondo <= 0)
            {
                continue;
            }

            var n = (long)redondo;
            var d = (long)den;

            var g = Mcd(n, d);

            n /= g;
            d /= g;

            var entero = n / d;
            var resto = n % d;

            if (resto == 0)
            {
                return entero.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return entero == 0 ? $"{resto}/{d}" : $"{entero} {resto}/{d}";
        }

        return pulgadas.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long Mcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a == 0 ? 1 : a;
    }

    /// <summary>Celda <b>C16</b>: electrodo. Se rotula con el sufijo <c>XX</c>.</summary>
    public string Electrodo { get => _electrodo; set => Set(ref _electrodo, value); }

    /// <summary>Celda <b>C17</b>: espesor de soldadura, en pulgadas. Cero = sin soldadura.</summary>
    public string Soldadura { get => _soldadura; set => Set(ref _soldadura, value); }

    /// <summary>Celda <b>C18</b>: cantidad TOTAL de cartabones en X.</summary>
    public int NCartabonesX { get => _nCartabonesX; set => Set(ref _nCartabonesX, value); }

    /// <summary>Celda <b>C19</b>: cantidad TOTAL de cartabones en Y.</summary>
    public int NCartabonesY { get => _nCartabonesY; set => Set(ref _nCartabonesY, value); }

    /// <summary>
    /// Espesor del filete que une los <b>cartabones</b>, en pulgadas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es un dato aparte del de la columna, y no por simetría: el cartabón es una placa más delgada
    /// que el perfil, así que su filete casi nunca es el mismo. Con un solo espesor para las dos
    /// soldaduras, una de las dos sale mal en el plano y no se ve —los dos rayados se parecen—.
    /// </para>
    /// <para>
    /// En blanco o en cero, los cartabones se dibujan sin soldadura.
    /// </para>
    /// </remarks>
    public string SoldaduraCartabon { get => _soldaduraCartabon; set => Set(ref _soldaduraCartabon, value); }

    /// <summary>Celda <b>C20</b>: espesor de los cartabones X, en pulgadas.</summary>
    public string EspCartabonX { get => _espCartabonX; set => Set(ref _espCartabonX, value); }

    /// <summary>Celda <b>C21</b>: espesor de los cartabones Y, en pulgadas.</summary>
    public string EspCartabonY { get => _espCartabonY; set => Set(ref _espCartabonY, value); }

    /// <summary>
    /// Celda <b>E19</b>: longitud de los cartabones X, en cm.
    /// </summary>
    /// <remarks>
    /// <b>E19 para X y E18 para Y no es una errata.</b> Es la corrección que la propia macro
    /// documenta: la hoja maneja las longitudes en el sentido opuesto al espesor visto en planta.
    /// Intercambiarlas dibuja los cartabones con la longitud del otro sentido.
    /// </remarks>
    public double LongCartabonXCm { get => _longCartabonXCm; set => Set(ref _longCartabonXCm, value); }

    /// <summary>Celda <b>E18</b>: longitud de los cartabones Y, en cm. Ver la nota de X.</summary>
    public double LongCartabonYCm { get => _longCartabonYCm; set => Set(ref _longCartabonYCm, value); }

    /// <summary>
    /// Celda <b>F18</b>: lo que <b>sube</b> el cartabon X en el alzado, en cm.
    /// </summary>
    /// <remarks>
    /// Va cruzada igual que la longitud: F18 es la altura de los cartabones <b>X</b>, que en planta
    /// son los que salen de las caras Y. Es la correccion que la propia macro documenta.
    /// En cero, el cartabon no sale en el alzado.
    /// </remarks>
    public double AltoCartabonXCm { get => _altoCartabonXCm; set => Set(ref _altoCartabonXCm, value); }

    /// <summary>Celda <b>F19</b>: lo que sube el cartabon Y en el alzado, en cm.</summary>
    public double AltoCartabonYCm { get => _altoCartabonYCm; set => Set(ref _altoCartabonYCm, value); }

    /// <summary>
    /// Celda <b>E12</b>: lo que se <b>ahoga</b> el ancla X en el concreto, en cm.
    /// </summary>
    /// <remarks>
    /// Solo se ve en el alzado, y ademas gobierna la profundidad del dado que se dibuja ahi: el
    /// concreto baja 5 cm mas que el ancla, para que la punta quede dentro. En cero, el alzado sale
    /// sin anclas.
    /// </remarks>
    public double LongAnclajeXCm { get => _longAnclajeXCm; set => Set(ref _longAnclajeXCm, value); }

    /// <summary>Celda <b>E13</b>: lo que se ahoga el ancla Y en el concreto, en cm.</summary>
    public double LongAnclajeYCm { get => _longAnclajeYCm; set => Set(ref _longAnclajeYCm, value); }

    /// <summary>
    /// La longitud <b>total desarrollada</b> del ancla X, en cm: lo que se corta, doblez incluido.
    /// </summary>
    /// <remarks>
    /// <b>Manda sobre el ahogo.</b> Con las dos capturadas pueden contradecirse, y de las dos la que
    /// se puede verificar con una cinta en el taller es esta. En cero se usa el ahogo, que es lo que
    /// se dibujaba antes. Y si la longitud pide mas hondo de lo que dice el ahogo, el dado del alzado
    /// baja lo que haga falta: un ancla asomando por debajo del concreto no se puede construir.
    /// </remarks>
    public double LongAnclaXCm { get => _longAnclaXCm; set => Set(ref _longAnclaXCm, value); }

    /// <summary>La longitud total del ancla Y, en cm. Ver la nota de X.</summary>
    public double LongAnclaYCm { get => _longAnclaYCm; set => Set(ref _longAnclaYCm, value); }

    /// <summary>
    /// La <b>pata</b> del doblez del extremo del ancla X, en cm. Cero = ancla recta.
    /// </summary>
    /// <remarks>
    /// Va <b>dentro</b> de la longitud total: una ancla de 45 con doblez de 10 lleva 35 de tramo
    /// recto. Las dos patas apuntan una contra la otra, hacia dentro, que es donde tienen
    /// recubrimiento. Con doblez desaparece el travesano del extremo: lo que ancla es la pata.
    /// </remarks>
    public double DoblezAnclaXCm { get => _doblezAnclaXCm; set => Set(ref _doblezAnclaXCm, value); }

    /// <summary>La pata del doblez del ancla Y, en cm. Ver la nota de X.</summary>
    public double DoblezAnclaYCm { get => _doblezAnclaYCm; set => Set(ref _doblezAnclaYCm, value); }

    /// <summary>Celda <b>F6</b>: dibujar los cartabones.</summary>
    public bool ConCartabones { get => _conCartabones; set => Set(ref _conCartabones, value); }

    // ======================================================================
    //  LA CAMA DE GROUT
    // ======================================================================

    /// <summary>Las dos respuestas de la casilla del grout.</summary>
    /// <remarks>
    /// La misma lista de las hojas de zapatas —«SI» y «NO»—, para que la casilla se conteste igual
    /// en todo el programa. Está una sola vez, en <see cref="ZapataAisladaRow.SiNo"/>.
    /// </remarks>
    public static string[] SiNo => ZapataAisladaRow.SiNo;

    /// <summary>
    /// <b>GROUT</b>: ¿va la placa sobre una cama de mortero de relleno?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Texto y no casilla de palomita, como las de zapatas: en un cuadro de placas se dicta y se
    /// revisa leyendo «SI» o «NO», y con una palomita hay que fijarse en si está marcada.
    /// </para>
    /// <para>
    /// En <b>NO</b> —lo que trae de fábrica— el detalle no cambia en nada: la placa se apoya
    /// directamente en el dado, que es como se dibujaba antes de que existiera esta casilla.
    /// </para>
    /// </remarks>
    public string Grout
    {
        get => _grout;
        set
        {
            Set(ref _grout, value);

            // La celda del espesor se enciende y se apaga con esta, así que su bandera tiene que
            // avisar. Sin este Raise, la celda de al lado se queda apagada aunque aquí diga SI.
            // De «Falta» ya se encarga RaiseCalculadas, por donde pasa todo Set.
            Raise(nameof(EsGrout));
        }
    }

    /// <summary>¿La casilla del grout dice SI?</summary>
    /// <remarks>
    /// De aquí cuelga el <c>IsEnabled</c> de la celda del espesor —ver <c>CeldaSoloGrout</c>— y el
    /// dibujo del corte.
    /// </remarks>
    public bool EsGrout =>
        (_grout ?? string.Empty).Trim().Equals("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El espesor de la cama de grout, en <b>centímetros</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En centímetros y no en pulgadas, a diferencia de la placa y de las anclas: una cama de grout
    /// se especifica en centímetros —2, 2.5, 3— y no en fracciones de pulgada.
    /// </para>
    /// <para>
    /// Solo se lee con la casilla en SI. Los 2.5 cm de arranque son el espesor usual de una cama
    /// nivelante; se cambia renglón por renglón.
    /// </para>
    /// </remarks>
    public double EspesorGroutCm
    {
        get => _espesorGroutCm;
        set => Set(ref _espesorGroutCm, value);
    }

    /// <summary>Escala del detalle, para el rótulo.</summary>
    public double Escala { get => _escala; set => Set(ref _escala, value); }

    /// <summary>Gira 90° la placa. El dado gira con ella; las anclas y el rótulo, no.</summary>
    public bool GirarPlaca90 { get => _girarPlaca90; set => Set(ref _girarPlaca90, value); }


    // ======================================================================
    //  COLUMNAS CALCULADAS
    // ======================================================================

    /// <summary>La forma que le toca a la familia elegida.</summary>
    public string Forma => FormaPerfil.DeLaFamilia(Familia);

    /// <summary>
    /// Las medidas que se trajeron del catálogo para la sección elegida. <b>Se ven en la tabla</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esta columna existe para que el dato deje de ser invisible. El perfil no se captura: se
    /// elige un nombre y sus medidas se buscan en el catálogo. Cuando esa búsqueda no encuentra
    /// nada —un nombre con un espacio de más, una familia que no corresponde— la fila se ve
    /// completa y el detalle sale sin la columna, sin que nada lo hubiera dicho antes.
    /// </para>
    /// <para>
    /// Con el peralte y el ancho a la vista, un perfil que no se encontró se nota de un golpe.
    /// </para>
    /// </remarks>
    public string MedidasPerfil
    {
        get
        {
            if (Seccion.Trim().Length == 0)
            {
                return "sin perfil";
            }

            var c = Catalogo();

            if (c is null)
            {
                return "NO ESTA EN EL CATALOGO";
            }

            return $"d={c.PeralteCm:0.##} b={c.AnchoCm:0.##} cm";
        }
    }

    /// <summary>
    /// De dónde salen las medidas del apoyo, y si sobresale de la placa. <b>Se ve en la tabla.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Por el mismo motivo que <see cref="MedidasPerfil"/>: cuando un dato se trae de otra hoja, lo
    /// que hay que hacer visible es <b>si de verdad se trajo</b>. Un ID escrito con un guion de más
    /// no encuentra la sección, las celdas se quedan con lo que hubiera, y la fila se ve completa.
    /// </para>
    /// <para>
    /// <b>Y LO PRIMERO ES SI EL ID EXISTE.</b> Paso de verdad: una placa con <c>CC-1</c> en el ID
    /// —una cadena de cerramiento— cuando la hoja solo buscaba dados. La referencia no encontraba
    /// nada y salía en silencio, así que la celda decía «rectangular, de «CC-1»» con las medidas de
    /// otro dado que traía de antes, y la de al lado, capturada después, se quedó en cero. Dos filas
    /// con el mismo ID mostrando cosas distintas y nada que lo explicara.
    /// </para>
    /// </remarks>
    public string ReferenciaApoyo
    {
        get
        {
            // ¿ESTÁ CAPTURADO? Se pregunta ANTES que nada, porque un ID que no existe hace falsas
            // todas las demás respuestas: las medidas que se vean serán de otra cosa o de nada.
            var id = IdDado.Trim();

            if (id.Length > 0 && !ApoyosDisponibles.Contains(id))
            {
                return EsPlacaAMuro
                    ? $"«{id}» NO ES UNA CADENA NI TRABE DE LA HOJA DE CONCRETO"
                    : $"«{id}» NO ES UN DADO DE LA HOJA DE CONCRETO";
            }

            // EL REDONDO SE MIDE CON UNA SOLA MEDIDA, igual que en AFormatoCad. Exigiendo las dos,
            // un dado circular al que solo se le puso el diámetro se leería aquí como «sin dado»
            // mientras el dibujo lo pone: la tabla diciendo una cosa y el plano otra.
            var dadoY = DadoCircular ? DadoXCm : DadoYCm;

            if (DadoXCm <= 0 || dadoY <= 0)
            {
                return EsPlacaAMuro ? "sin apoyo" : "sin dado";
            }

            var forma = DadoCircular ? "redondo" : "rectangular";

            var origen = IdDado.Trim().Length > 0 ? $"de «{IdDado.Trim()}»" : "a mano";

            // Y SI SOBRESALE, que es lo que decide si el rayado de concreto se ve: un dado igual o
            // menor que la placa no deja franja que rayar, y el detalle sale sin concreto a la
            // vista sin que nada lo hubiera dicho.
            var vuela = DadoCircular
                ? DadoXCm > Math.Sqrt((AnchoDibujoCm * AnchoDibujoCm) + (AltoDibujoCm * AltoDibujoCm))
                : DadoXCm > AnchoDibujoCm && dadoY > AltoDibujoCm;

            return $"{forma}, {origen}" + (vuela ? string.Empty : " · NO SOBRESALE");
        }
    }

    /// <summary>El ancho de la placa ya orientada, para poder comparar el dado contra ella.</summary>
    private double AnchoDibujoCm => GirarPlaca90 ? LargoCm : AnchoCm;

    /// <summary>El alto de la placa ya orientada.</summary>
    private double AltoDibujoCm => GirarPlaca90 ? AnchoCm : LargoCm;

    /// <summary>Cuántos cartabones se van a dibujar de verdad.</summary>
    /// <remarks>
    /// Se contesta con la <b>misma</b> regla que el dibujo, incluido el descarte por espesor o
    /// longitud en cero: prometer en la tabla seis cartabones y dibujar cuatro es peor que no decir
    /// nada. Y con la casilla apagada son cero, aunque las cantidades sigan capturadas.
    /// </remarks>
    public int TotalCartabones => CartabonesPlacaBase.Cuantos(AFormatoCad());

    /// <summary>Cuántas anclas salen en total, ya repartidas.</summary>
    /// <remarks>
    /// Los dos números de la hoja son <b>totales</b>: las de X se parten entre la hilera de abajo y
    /// la de arriba, y las de Y van <b>entre</b> esas dos hileras, así que las esquinas no se
    /// cuentan dos veces y el total es la suma de los dos. Es el reparto perimetral de la macro, y
    /// el único.
    /// </remarks>
    public int TotalAnclas => Math.Max(0, NAnclasX) + Math.Max(0, NAnclasY);

    /// <summary>
    /// Los libramientos J y K, ya comprobados. Vacío = cumple.
    /// </summary>
    /// <remarks>
    /// <b>Se comprueba en la tabla y no solo al dibujar.</b> Es la diferencia entre enterarse de que
    /// las anclas no caben mientras se captura y enterarse cuando el botón se niega a dibujar. Es la
    /// misma idea que la columna «Falta» del resto de las hojas.
    /// </remarks>
    public string Libramientos => RevisarLibramientos()?.Titulo ?? string.Empty;

    /// <summary>
    /// El <b>detalle</b> del libramiento que no cumple: los números y qué hacer. Vacío = cumple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En la celda de la tabla solo cabe el titular —«Holgura mínima a la columna (L)»—, y con el
    /// titular solo no se puede corregir nada: dice QUÉ pasa y no de qué ancla, ni cuánta holgura
    /// hay, ni cuánta se pide. Esto es lo que sale en el aviso de antes de dibujar, que es donde el
    /// usuario está parado cuando necesita los números.
    /// </para>
    /// <para>
    /// Y lleva al final <b>el sitio que hay entre el canto de la placa y el paño del perfil</b>,
    /// que es el dato que dice si el problema se arregla moviendo las anclas o solo con una placa
    /// mayor. Sin él, «faltan 20 mm de holgura» no distingue un detalle apretado de uno imposible.
    /// </para>
    /// </remarks>
    public string LibramientoDetalle
    {
        get
        {
            var falla = RevisarLibramientos();

            if (falla is null)
            {
                return string.Empty;
            }

            var sitio = SitioEntreElCantoYElPerfil();

            return sitio.Length == 0 ? falla.Detalle : falla.Detalle + "\n\n" + sitio;
        }
    }

    /// <summary>
    /// Cuánto hay —y cuánto pide el cuadro— entre el canto de la placa y el paño del perfil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos paredes entre las que tiene que caber el ancla, dichas como una resta: de un lado el
    /// sobrante real de la placa, del otro <c>K + L</c>, que es lo que el cuadro exige a un ancla
    /// alineada con el perfil. Si el sobrante no llega, no es que las anclas estén mal puestas: no
    /// hay dónde ponerlas, y lo que hace falta es una placa mayor o un perfil menor.
    /// </para>
    /// <para>
    /// Se dice «alineada con el perfil» porque es la única posición en la que las dos distancias
    /// caen sobre la misma línea. Las anclas de las <b>esquinas</b> —las de las hileras de X con
    /// pocas anclas— miden su holgura en diagonal y por eso ganan algo: pueden cumplir con un
    /// sobrante algo menor. Es una referencia para entender el aviso, no la comprobación: la que
    /// manda es la que mide ancla por ancla contra el contorno de verdad.
    /// </para>
    /// </remarks>
    private string SitioEntreElCantoYElPerfil()
    {
        var p = AFormatoCad();

        var b = p.AnchoDibujoCm;
        var h = p.AltoDibujoCm;
        var px = p.PerfilXDibujoCm;
        var py = p.PerfilYDibujoCm;

        if (b <= 0 || h <= 0 || px <= 0 || py <= 0)
        {
            return string.Empty;
        }

        var hayX = (b - px) / 2;
        var hayY = (h - py) / 2;

        var pideX = AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaXCm)
                    + AnclasPlacaBase.HolguraColumnaMinimaCm(p.DiamAnclaXCm);

        var pideY = AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaYCm)
                    + AnclasPlacaBase.HolguraColumnaMinimaCm(p.DiamAnclaYCm);

        return
            $"Entre el canto de la placa y el paño del perfil hay {hayX:0.#} cm en X y " +
            $"{hayY:0.#} cm en Y.\n" +
            $"El cuadro pide K + L = {pideX:0.#} cm en X y {pideY:0.#} cm en Y para un ancla " +
            "alineada con el perfil\n(las de esquina ganan algo por la diagonal). Si no llega, " +
            "hace falta placa mayor o perfil menor.";
    }

    /// <summary>
    /// La comprobación de los libramientos, en <b>un solo sitio</b>. <c>null</c> = la placa cumple.
    /// </summary>
    /// <remarks>
    /// La hacen tres: la celda «Libramientos» —que enseña el titular—, el aviso de antes de dibujar
    /// —que enseña el detalle— y la vista previa. Con el cálculo repetido, la tabla podría decir que
    /// una placa cumple y el botón negarse a dibujarla.
    /// </remarks>
    private AnclasPlacaBase.Incumplimiento? RevisarLibramientos()
    {
        var p = AFormatoCad();

        // Con la fila incompleta no se dice nada: la columna «Falta» ya está diciendo lo que
        // hay, y añadir «las anclas no caben» a una placa sin medidas es ruido.
        if (p.Falta.Count > 0 || !p.ValidarSeparacionAnclas)
        {
            return null;
        }

        // Se mide en centímetros: da igual la escala del dibujo, porque las tablas J y K
        // trabajan en milímetros y la conversión es interna.
        var b = p.AnchoDibujoCm;
        var h = p.AltoDibujoCm;

        var dAncX = p.DiamAnclaXCm;
        var dAncY = p.DiamAnclaYCm;

        var dAguX = p.DiamAgujeroXCm > 0 ? p.DiamAgujeroXCm : dAncX + (2.54 / 16);
        var dAguY = p.DiamAgujeroYCm > 0 ? p.DiamAgujeroYCm : dAncY + (2.54 / 16);

        // EL PERFIL Y LA DISTANCIA K ENTRAN EN LA CUENTA, igual que en el dibujante. La
        // separación automática reparte el sobrante entre la placa y el patín, así que sin el
        // perfil esta columna usaría un 12 % del ancho y el dibujante otra cosa: la tabla diría
        // que la placa cumple y el botón se negaría a dibujarla, sin nada que explicara la
        // diferencia. Y el ajuste al mínimo de K, por lo mismo.
        var sepX = AnclasPlacaBase.SepBordeAjustada(p.SepBordeXCm, dAncX, b);
        var sepY = AnclasPlacaBase.SepBordeAjustada(p.SepBordeYCm, dAncY, h);

        if (sepX <= 0)
        {
            sepX = AnclasPlacaBase.SepAuto(
                b, p.PerfilXDibujoCm, dAguX, 1, AnclasPlacaBase.BordeMinimoCm(dAncX));
        }

        if (sepY <= 0)
        {
            sepY = AnclasPlacaBase.SepAuto(
                h, p.PerfilYDibujoCm, dAguY, 1, AnclasPlacaBase.BordeMinimoCm(dAncY));
        }

        var anclas = AnclasPlacaBase.Construir(
            0, 0, b, h, p.NAnclasX, p.NAnclasY, sepX, sepY,
            dAncX, dAguX, dAncY, dAguY);

        // LAS TRES COLUMNAS DEL CUADRO, y cada una mide LO SUYO:
        //   J - la distancia entre anclas
        //   K - la del ancla al canto recortado de la placa
        //   L - la del ancla al paño de la COLUMNA, para que entre la llave
        //
        // La L no es «la K con otro nombre»: en el croquis del estándar el orden es canto de la
        // placa -> K -> ancla -> L -> paño de la columna, así que una mira hacia fuera y la otra
        // hacia dentro. Los números tampoco dejan deducir una de la otra: en un ancla de 5/8" la
        // K pide 30 mm y la L 28, y en una de 1 1/2" la K pide 65 y la L 66.
        // En la celda solo cabe el titular; el detalle completo —con los números— sale en el
        // aviso de antes de dibujar, por LibramientoDetalle.
        return AnclasPlacaBase.RevisarSeparacionJ(anclas, 1)
               ?? AnclasPlacaBase.RevisarDistanciaK(anclas, 0, 0, b, h, 1)
               ?? AnclasPlacaBase.RevisarHolguraColumnaL(
                      anclas, p.PanoDeLaColumna(b / 2, h / 2, 1)?.Puntos, 1);
    }

    /// <summary>Qué falta para poder dibujar. Vacío = se puede.</summary>
    public string Falta
    {
        get
        {
            var falta = new List<string>(AFormatoCad().Falta);

            var libramiento = Libramientos;

            if (libramiento.Length > 0)
            {
                falta.Add(libramiento.ToLowerInvariant());
            }

            return falta.Count == 0 ? string.Empty : string.Join("; ", falta);
        }
    }

    /// <summary>Refresca las columnas calculadas cuando cambia cualquier dato.</summary>
    protected override void RaiseCalculadas()
    {
        // EL CACHE DEL PERFIL SE TIRA ANTES DE AVISAR. Si se avisara primero, las celdas se
        // repintarían leyendo todavía el perfil viejo, y cambiar de sección dejaría en pantalla
        // las medidas de la anterior.
        _catalogo = null;
        _claveCatalogo = null;

        Raise(nameof(Forma));
        Raise(nameof(MedidasPerfil));
        Raise(nameof(ReferenciaApoyo));

        // El tipo no cambia solo, pero la LISTA que ofrece el desplegable del ID depende de el y
        // sus elementos SI cambian: al capturar una cadena nueva en la hoja de concreto, la celda
        // de esta fila tiene que ofrecerla. Ver ApoyosDisponibles.
        Raise(nameof(ApoyosDisponibles));
        Raise(nameof(TotalAnclas));
        Raise(nameof(TotalCartabones));

        // Los agujeros son calculados desde su ancla, así que se avisa de ellos aquí: sin esto, la
        // celda seguiría enseñando el agujero del ancla anterior.
        Raise(nameof(DiamAgujeroX));
        Raise(nameof(DiamAgujeroY));

        Raise(nameof(BordeMinimo));
        Raise(nameof(SepBordeUsada));
        Raise(nameof(Libramientos));

        // El detalle va JUNTO con su titular: es el globo de esa misma celda, y sin avisar de él
        // el globo se quedaría con los números de la captura anterior.
        Raise(nameof(LibramientoDetalle));
        Raise(nameof(Falta));
    }

    // ======================================================================
    //  A LO QUE ENTIENDE EL DIBUJANTE
    // ======================================================================

    /// <summary>
    /// Convierte la fila en el objeto que dibuja <c>CadLink.Cad</c>, ya <b>todo en centímetros</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí es donde viven las conversiones de la macro, y en un solo sitio a propósito: las anclas,
    /// los agujeros, los espesores y la soldadura se capturan en <b>pulgadas</b> y se pasan a
    /// centímetros; la placa y los cartabones ya vienen en centímetros.
    /// </para>
    /// <para>
    /// Los diámetros se guardan <b>dos veces</b>: el número convertido, para la geometría, y el
    /// texto tal como se escribió, para el rótulo. Un plano que pide anclas de <c>0.75"</c> en lugar
    /// de <c>3/4"</c> obliga a traducir en obra.
    /// </para>
    /// </remarks>
    public PlacaBaseCad AFormatoCad()
    {
        var perfil = BuscarPerfil();

        return new PlacaBaseCad
        {
            Marca = Marca,
            LargoCm = LargoCm,
            AnchoCm = AnchoCm,
            Espesor = Espesor,
            AceroPlaca = AceroPlaca,

            DadoXCm = DadoXCm,

            // EL DADO REDONDO ES UN DIÁMETRO, uno solo. Se copia en las dos direcciones para que
            // las cotas y el encuadre del detalle midan lo mismo por los dos lados: con un Y
            // distinto, el dibujo cotaría un diámetro vertical que el círculo no tiene.
            DadoYCm = DadoCircular ? DadoXCm : DadoYCm,
            DadoCircular = DadoCircular,

            // Para el titulo del detalle y el nombre del bloque del corte. La geometria no cambia:
            // ver PlacaBaseCad.EsPlacaAMuro.
            EsPlacaAMuro = EsPlacaAMuro,

            Familia = Familia,
            Seccion = Seccion,
            Perfil = perfil,

            NAnclasX = Math.Max(0, NAnclasX),
            NAnclasY = Math.Max(0, NAnclasY),

            SepBordeXCm = SepBordeXCm,
            SepBordeYCm = SepBordeYCm,

            // Pulgadas -> centímetros.
            DiamAnclaXCm = Pulgadas(DiamAnclaX) * 2.54,
            DiamAnclaYCm = Pulgadas(DiamAnclaY) * 2.54,
            DiamAgujeroXCm = Pulgadas(DiamAgujeroX) * 2.54,
            DiamAgujeroYCm = Pulgadas(DiamAgujeroY) * 2.54,

            // Y el texto tal cual, para el rótulo.
            TextoDiamAnclaX = DiamAnclaX,
            TextoDiamAnclaY = DiamAnclaY,
            TextoDiamAgujeroX = DiamAgujeroX,
            TextoDiamAgujeroY = DiamAgujeroY,

            EspesorCm = Pulgadas(Espesor) * 2.54,

            Electrodo = Electrodo,
            SoldaduraCm = Pulgadas(Soldadura) * 2.54,
            TextoSoldadura = Soldadura,

            SoldaduraCartabonCm = Pulgadas(SoldaduraCartabon) * 2.54,
            TextoSoldaduraCartabon = SoldaduraCartabon,

            NCartabonesX = Math.Max(0, NCartabonesX),
            NCartabonesY = Math.Max(0, NCartabonesY),
            EspCartabonXCm = Pulgadas(EspCartabonX) * 2.54,
            EspCartabonYCm = Pulgadas(EspCartabonY) * 2.54,
            TextoEspCartabonX = EspCartabonX,
            TextoEspCartabonY = EspCartabonY,
            LongCartabonXCm = LongCartabonXCm,
            LongCartabonYCm = LongCartabonYCm,
            ConCartabones = ConCartabones,

            AltoCartabonXCm = AltoCartabonXCm,
            AltoCartabonYCm = AltoCartabonYCm,
            LongAnclajeXCm = LongAnclajeXCm,
            LongAnclajeYCm = LongAnclajeYCm,
            LongAnclaXCm = LongAnclaXCm,
            LongAnclaYCm = LongAnclaYCm,
            DoblezAnclaXCm = DoblezAnclaXCm,
            DoblezAnclaYCm = DoblezAnclaYCm,

            // LA CAMA DE GROUT. El «SI» de la casilla se traduce aquí a un booleano: al dibujante
            // no le importa cómo se contestó la celda, solo si la lleva o no.
            ConGrout = EsGrout,
            EspesorGroutCm = EspesorGroutCm,

            Escala = Escala > 0 ? Escala : 10,
            GirarPlaca90 = GirarPlaca90
        };
    }

    /// <summary>El renglón del catálogo, buscado una sola vez por cada cambio.</summary>
    /// <remarks>
    /// <para>
    /// <b>Se guarda a propósito.</b> <see cref="CatalogoPerfiles.Buscar"/> recorre el catálogo
    /// entero —más de tres mil renglones— y a esta búsqueda la llaman, por cada tecla que se
    /// escribe en la fila, las columnas «Medidas», «Libramientos» y «Falta», cada una a través de
    /// su propio <see cref="AFormatoCad"/>. Sin guardar el resultado, escribir en una celda
    /// significa recorrer el catálogo media docena de veces por pulsación, y la tabla se arrastra.
    /// </para>
    /// <para>
    /// El guardado se tira en <see cref="RaiseCalculadas"/>, que es justo cuando algo cambió.
    /// </para>
    /// </remarks>
    private PerfilCatalogo? Catalogo()
    {
        var clave = _familia + "\u0001" + _seccion;

        if (_claveCatalogo == clave)
        {
            return _catalogo;
        }

        _catalogo = CatalogoPerfiles.Buscar(_familia, _seccion);
        _claveCatalogo = clave;

        return _catalogo;
    }

    private PerfilCatalogo? _catalogo;
    private string? _claveCatalogo;

    /// <summary>La geometría del perfil, buscada en el catálogo por familia y designación.</summary>
    private PerfilAceroCad? BuscarPerfil()
    {
        if (Seccion.Trim().Length == 0)
        {
            return null;
        }

        var c = Catalogo();

        if (c is null)
        {
            return null;
        }

        // Las dimensiones vienen del catálogo, que ya las trae en centímetros. La macro las leía de
        // la hoja IMCA5 en milímetros y las convertía; aquí el catálogo hizo ya ese trabajo, así que
        // no se vuelve a convertir: hacerlo dividiría las medidas por diez sin que nada avisara.
        return new PerfilAceroCad
        {
            Familia = Familia,
            Forma = Forma,
            Perfil = Seccion,
            Acero = AceroPlaca,
            PeralteCm = c.PeralteCm,
            AnchoCm = c.AnchoCm,
            EspesorCm = c.EspesorAlmaCm,
            EspesorPatinCm = c.EspesorPatinCm,
            LabioCm = c.LabioCm,
            RadioCm = c.RadioCm,
            AnchoMenorCm = c.AnchoMenorCm
        };
    }

    /// <summary>
    /// Lee un valor en pulgadas admitiendo <b>fracciones</b>: <c>5/8</c>, <c>1 1/4</c>, <c>1-1/2</c>.
    /// </summary>
    /// <remarks>
    /// Es el <c>ValorFraccion</c> de la macro. Hace falta porque en el taller los diámetros se
    /// piden en fracciones, no en decimales, y obligar a escribir <c>0.625</c> es pedirle al
    /// usuario que haga una división que el programa puede hacer.
    /// </remarks>
    public static double Pulgadas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return 0;
        }

        // Se quitan las comillas de pulgada, los guiones de «1-1/2» y el espacio duro que a veces
        // llega al pegar desde Excel. La coma se acepta como punto decimal.
        var limpio = texto
            .Replace("\"", " ")
            .Replace("-", " ")
            .Replace('\u00A0', ' ')
            .Replace(',', '.');

        var total = 0.0;
        var algo = false;

        foreach (var pieza in limpio.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = Token(pieza);

            if (v > 0)
            {
                total += v;
                algo = true;
            }
        }

        return algo ? total : 0;
    }

    /// <summary>Un trozo: o una fracción <c>a/b</c>, o un número.</summary>
    private static double Token(string s)
    {
        var t = s.Trim();

        if (t.Length == 0)
        {
            return 0;
        }

        if (t.Contains('/'))
        {
            var partes = t.Split('/');

            if (partes.Length == 2
                && double.TryParse(partes[0], System.Globalization.NumberStyles.Any,
                                   System.Globalization.CultureInfo.InvariantCulture, out var a)
                && double.TryParse(partes[1], System.Globalization.NumberStyles.Any,
                                   System.Globalization.CultureInfo.InvariantCulture, out var b)
                && Math.Abs(b) > 1e-9)
            {
                return a / b;
            }

            return 0;
        }

        return double.TryParse(t, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }
}
