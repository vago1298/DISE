using CadLink.Cad.PlanoEstructural;

namespace CadLink.Cad;

/// <summary>
/// Dibuja en AutoCAD la planta estructural de un nivel.
/// </summary>
/// <remarks>
/// <para>
/// Es el arranque del plano estructural: lo que en la pestaña «Dibujar planos
/// estructurales» se ve en el lienzo, puesto en AutoCAD con la misma geometría y en
/// <b>metros</b>, que es la unidad en la que ETABS entrega el modelo.
/// </para>
/// <para>
/// <b>Por qué cada cosa se dibuja como se dibuja.</b> Una planta estructural no es
/// el modelo de alambre: es lo que se construye.
/// </para>
/// <list type="bullet">
///   <item>
///     La <b>columna</b> en planta es su sección, un rectángulo del tamaño real
///     centrado en el nudo. Un punto no diría nada.
///   </item>
///   <item>
///     La <b>trabe</b> se dibuja por sus <b>dos paños</b>, separados su ancho real,
///     más el eje a trazos. Una sola línea no permite acotar ni ver los cruces.
///   </item>
///   <item>
///     El <b>muro</b> igual que la trabe, pero con su espesor y en su capa, porque
///     es lo que se replantea primero en obra.
///   </item>
///   <item>
///     La <b>losa</b> es su contorno cerrado, sin relleno: encima van a ir el armado
///     y las cotas, y un relleno los taparía.
///   </item>
/// </list>
/// <para>
/// <b>No se agrupa en un bloque</b>, al contrario que las secciones. Una sección es
/// una pieza de catálogo que se repite y se inserta; una planta es única y sobre ella
/// se sigue trabajando: armado, cotas, ejes, textos. Si llegara como bloque, lo
/// primero que habría que hacer es explotarla. Lo que sí se hace es repartirla en
/// <b>capas por tipo de elemento</b>, que es lo que de verdad se usa para trabajar.
/// </para>
/// <para>
/// <b>Enlace tardío.</b> Como el resto del proyecto, se habla con AutoCAD por COM con
/// <c>dynamic</c>, sin referenciar ninguna DLL de Autodesk, así que el mismo binario
/// sirve para varias versiones de AutoCAD.
/// </para>
/// </remarks>
public sealed partial class PlantaDrawer
{
    private const int PorCapa = 256;

    // ==================================================================================
    //  LAS CAPAS SON LAS DE LA MACRO, NO UNAS PROPIAS
    // ==================================================================================
    //  Antes esto tenía sus propias capas —PLANTA-COLUMNAS, PLANTA-TRABES…— con sus
    //  propios colores, así que el plano salía en unas capas que no eran las suyas y no
    //  encajaba con nada de lo que ya tiene dibujado.
    //
    //  Ahora salen de CapasPlano, que es la tabla de DefinirCapas + CrearCapas: E-CASTILLO,
    //  E-COLUMNA, E-DALA, E-TRABE, E-CONTRATRABE, E-MURO, E-LOSA, E-ACERO, E-EJES, E-TEXTO
    //  y E-TITULO, cada una con SU color. Y la capa de cada elemento se elige como en su
    //  DibujarElemento: por el TIPO —que distingue castillo de columna y dala de trabe— y,
    //  si es un perfil de acero, E-ACERO.
    // ==================================================================================
    private readonly PlanoEstructural.ConfigPlano _cfg = new();
    private readonly PlanoEstructural.CapasPlano _capas;

    /// <summary>La cuenta del ajuste al paño de los castillos y las columnas.</summary>
    private PanoDeApoyo Pano => _pano ??= new PanoDeApoyo(_cfg);

    private PanoDeApoyo? _pano;

    private string CapaEjes => _capas.Prefijo + "EJES";
    private string CapaTextos => _capas.Prefijo + "TEXTO";
    private string CapaRotulo => _capas.Prefijo + "TITULO";

    /// <summary>
    /// La capa que le toca a un elemento: la de su TIPO, o la del acero si es un perfil.
    /// </summary>
    private string CapaDe(ElementoPlanta el)
    {
        if (PlanoEstructural.CapasPlano.EsPerfilAcero(el.Forma))
        {
            return _capas.CapaDeTipo("ACERO");
        }

        var tipo = string.IsNullOrWhiteSpace(el.Tipo)
            ? el.Clase switch
            {
                ClasePlanta.Columna => "COLUMNA",
                ClasePlanta.Trabe => "TRABE",
                ClasePlanta.Muro => "MURO",
                ClasePlanta.Losa => "LOSA",
                _ => "DIAGONAL"
            }
            : el.Tipo;

        return _capas.CapaDeTipo(tipo);
    }

    /// <summary>
    /// La capa de un <b>muro</b>: la de concreto tiene la suya cuando no lleva cadena.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió así: «no me dibujas los muros de concreto cuando no tienen cadena, dibújalos en
    /// una capa —solo si no tienen cadena; si tienen cadena dibuja pura cadena, como en
    /// mampostería—: la capa E-MURO DE CONCRETO».
    /// </para>
    /// <para>
    /// Y es la regla de la mampostería aplicada al concreto: donde hay cadena, la cadena manda
    /// —el muro y su cadena ocupan la misma línea en planta y dibujar los dos deja dos parejas
    /// de rayas pegadas—, y donde no hay cadena el muro es lo único que hay, así que se dibuja.
    /// La diferencia es la <b>capa</b>: un muro de concreto es estructura, se arma y se cuela, y
    /// tiene que poderse revisar sin la mampostería encima.
    /// </para>
    /// </remarks>
    private string CapaDeMuro(ElementoPlanta el, bool tapado)
    {
        if (!tapado
            && _cfg.Bandera("MURO_CONCRETO_CAPA_PROPIA", true)
            && EsMuroDeConcreto(el))
        {
            return _capas.CapaMuroConcreto;
        }

        return CapaDe(el);
    }

    /// <summary>
    /// ¿Es un muro de <b>concreto</b>? Manda la <b>property note</b> de ETABS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió con esas palabras: los muros «que digan en property note CONCRETO». Y hay que
    /// mirar la nota <b>aparte</b>, no basta con reusar <c>el.Material</c>, porque
    /// <c>SeccionesModelo.MaterialDeMuro</c> decide con <b>la nota y el nombre de la sección
    /// juntos</b> y además le da <b>prioridad a la mampostería</b>. Con esa regla, una propiedad
    /// que se llame <c>MURO BLOCK 15</c> pero cuya nota diga <c>CONCRETO</c> sale clasificada
    /// como mampostería, que es justo el caso que se quiere poder resolver escribiendo la nota.
    /// </para>
    /// <para>
    /// Así que si la nota dice concreto, es de concreto y punto. Si la nota no dice nada del
    /// material, se respeta la clasificación general, que sigue siendo la buena para el resto de
    /// los casos.
    /// </para>
    /// <para>
    /// Las palabras son las de la hoja —<c>PALABRAS_CONCRETO</c>—, las mismas que usa el
    /// clasificador, para que no haya dos listas que se puedan desincronizar.
    /// </para>
    /// </remarks>
    /// <summary>
    /// ¿Debajo de este muro <b>no hay nada</b>: ni otro nivel, ni nada?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió con esas palabras: dibujar la línea de la base del muro <b>solo si abajo del muro
    /// no hay nada, ni otro nivel</b>, y que valga <b>para distintos niveles</b>.
    /// </para>
    /// <para>
    /// Y es la regla correcta, no un capricho: la línea de la base es <b>donde el muro apoya</b>.
    /// Si debajo hay otro nivel, el muro no apoya ahí —apoya en la losa o en la trabe de ese
    /// nivel— y dibujarle una base es dibujar un desplante que no existe. Solo el muro que
    /// arranca desde lo más bajo del edificio apoya de verdad, y ese es el que la lleva.
    /// </para>
    /// <para>
    /// <b>Cómo se comprueba.</b> Se mira si existe algún nivel del modelo por <b>debajo</b> de la
    /// base del muro. Se compara contra la lista de niveles y no contra el nivel que se está
    /// dibujando, y eso es justo lo que hace que valga para cualquier planta: en la de cimentación
    /// no habrá ninguno por debajo y el muro llevará su base; en un entrepiso sí lo habrá y no la
    /// llevará, sin necesidad de preguntar en qué planta estamos.
    /// </para>
    /// <para>
    /// La tolerancia evita que un nivel a la misma cota que la base —el propio nivel del muro, o
    /// uno duplicado por redondeo en el modelo— cuente como «hay algo debajo».
    /// </para>
    /// <para>
    /// Si el modelo <b>no trae niveles</b>, se contesta que no hay nada debajo: es lo que pasa en
    /// un modelo de SAP2000 sin pisos, y ahí lo prudente es dibujar la base y que se vea, no
    /// callarse por falta de datos.
    /// </para>
    /// </remarks>
    private bool NadaDebajoDelMuro(ElementoPlanta el, PlantaCad p)
    {
        if (p.Niveles.Count == 0)
        {
            return true;
        }

        var tol = _cfg.Numero("MURO_BASE_TOLERANCIA_CM", 20) / 100;

        // La base del muro: la más baja de sus dos cotas, que el modelo puede traerlas al revés.
        var baseMuro = Math.Min(el.Z1, el.Z2);

        foreach (var (_, z) in p.Niveles)
        {
            if (z < baseMuro - tol)
            {
                return false;
            }
        }

        return true;
    }

    private bool EsMuroDeConcreto(ElementoPlanta el)
    {
        if (_cfg.Bandera("MURO_CONCRETO_POR_NOTA", true)
            && DiceConcreto(el.Notas))
        {
            return true;
        }

        return string.Equals(el.Material, "CONCRETO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>¿Este texto nombra el concreto, con las palabras de la hoja?</summary>
    private bool DiceConcreto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        var t = NormalizarNota(texto);

        var palabras = _cfg.Texto("PALABRAS_CONCRETO", "CONCRETO,CONCRETE,C.A.,REFORZADO")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var palabra in palabras)
        {
            var p = NormalizarNota(palabra);

            if (p.Length > 0 && t.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normaliza una nota igual que <c>EtabsReader.Normalizar</c>: mayúsculas, sin acentos y
    /// <b>sin espacios ni signos</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se duplica la regla aquí porque <c>CadLink.Cad</c> <b>no referencia</b> a
    /// <c>CadLink.Etabs</c> —el dibujante no sabe nada de ETABS y así debe seguir—, pero tiene que
    /// comparar <b>con el mismo criterio</b> que el clasificador. Si una de las dos quitara los
    /// espacios y la otra no, un muro saldría de concreto para una y de otra cosa para la otra, y
    /// eso es exactamente el tipo de diferencia que no se ve hasta que está en el plano.
    /// </para>
    /// <para>
    /// Quitar los espacios importa de verdad: en la hoja las palabras se escriben separadas por
    /// comas y con espacios alrededor, y las notas de ETABS traen texto libre como
    /// <c>«MURO DE CONCRETO f'c=250 kg/cm²»</c>. Comparando en crudo, un espacio de más o un
    /// apóstrofo bastan para no encontrar la palabra.
    /// </para>
    /// </remarks>
    private static string NormalizarNota(string s)
    {
        var t = s.ToUpperInvariant().Trim()
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I')
            .Replace('Ó', 'O').Replace('Ú', 'U').Replace('Ñ', 'N');

        return new string(
            t.Where(c => (c >= 'A' && c <= 'Z') || char.IsAsciiDigit(c) || c == '.').ToArray());
    }

    private const string EstiloTexto = "SECCIONES";

    /// <summary>Ancho por omisión de una trabe cuando el modelo no lo dice, en m.</summary>
    /// <remarks>
    /// Con ancho 0 la trabe se dibujaría como una sola línea y el plano quedaría
    /// mudo. 0.20 m es el ancho mínimo de una trabe real: se dibuja algo con
    /// sentido y se AVISA, en lugar de callar el dato que falta.
    /// </remarks>
    private const double AnchoTrabePorOmision = 0.20;

    /// <summary>Espesor por omisión de un muro cuando el modelo no lo dice, en m.</summary>
    private const double EspesorMuroPorOmision = 0.15;

    /// <summary>Bajo esto un elemento se considera un punto y no se dibuja.</summary>
    private const double LargoMinimo = 1e-4;

    private readonly dynamic _doc;
    private readonly dynamic _ms;

    private readonly List<string> _log = new();
    private readonly List<string> _notas = new();

    /// <summary>
    /// Las cadenas que <b>no</b> se dibujan porque otra más alta va sobre su misma línea.
    /// </summary>
    /// <remarks>
    /// Se calcula una vez por planta, antes de dibujar las barras, y lo miran <b>dos</b> sitios:
    /// la geometría y el <b>rótulo</b>. Si solo lo mirara la geometría, la cadena intermedia no
    /// se dibujaría pero su nombre seguiría escrito en el mismo punto que el de la de arriba,
    /// que es la mitad de lo que se pidió arreglar.
    /// </remarks>
    private HashSet<ElementoPlanta> _cadenasTapadas = new();

    /// <summary>Cuántos nombres de cadena se callaron por caer sobre un castillo de área.</summary>
    private int _rotulosSobreCastillo;

    /// <summary>Cuántos muros de concreto se dibujaron en su capa, para el resumen.</summary>
    private int _murosDeConcreto;

    /// <summary>Muros de concreto dibujados como contorno cerrado con su leyenda dentro.</summary>
    private int _contornosMc;

    /// <summary>
    /// Muros de concreto a los que no les cupo la leyenda.
    /// </summary>
    /// <remarks>
    /// Se cuenta y se dice. Un muro corto sin su «MC» mientras los de al lado sí lo llevan parece
    /// un muro de otro material, y eso en un plano de cimentación se malinterpreta en obra.
    /// </remarks>
    private int _sinLeyendaMc;


    /// <summary>Muros que salieron clasificados como de concreto.</summary>
    /// <summary>
    /// Muros a los que <b>no</b> se les dibujó la base porque debajo hay otro nivel.
    /// </summary>
    /// <remarks>
    /// Se dice, porque es la diferencia entre «no lo dibuja» y «no lo dibuja a propósito»: un muro
    /// de un entrepiso no apoya en la cimentación, así que no lleva base, y quien mire el plano
    /// tiene que poder confirmar que eso es la regla y no un fallo.
    /// </remarks>
    private int _muroConAlgoAbajo;

    /// <summary>Bases de muro de planta baja dibujadas en la cimentación.</summary>
    private int _basesDeMuroDeArriba;

    /// <summary>
    /// Muros de planta baja que <b>no</b> llevan base porque tienen cadena de desplante.
    /// </summary>
    /// <remarks>
    /// Son los de mampostería: apoyan en su cadena, y la cadena ya se dibuja por su cuenta. Se
    /// cuentan para poder distinguir «no se dibujó» de «no se dibuja a propósito».
    /// </remarks>
    private int _muroDeArribaConCadena;

    /// <summary>Muros que salieron clasificados como de concreto.</summary>
    private int _muroConcretoVistos;

    /// <summary>Muros que salieron clasificados como de mampostería.</summary>
    private int _muroMamposteriaVistos;

    /// <summary>Muros sin material reconocido.</summary>
    private int _muroSinMaterial;

    /// <summary>
    /// Las notas distintas de los muros que <b>no</b> salieron de concreto.
    /// </summary>
    /// <remarks>
    /// Es el dato que faltaba para no adivinar. Cuando el usuario dice «no me dibujas la base del
    /// muro de concreto», hay tres causas que desde el plano se ven idénticas —no hay línea— y
    /// esta lista distingue la tercera: que la property note no diga lo que se cree que dice.
    /// </remarks>
    private readonly SortedSet<string> _notasDeMuro =
        new(StringComparer.OrdinalIgnoreCase);

    public PlantaDrawer(dynamic doc)
    {
        _doc = doc;
        _capas = new PlanoEstructural.CapasPlano(_cfg);
        _ms = AcadConnection.Retry(() => doc.ModelSpace);

        // Se toca una vez para que la interop quede cargada antes del primer dibujo,
        // igual que hacen los otros dibujantes.
        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Fallos tolerados: lo que no se pudo dibujar, y por qué.</summary>
    public IReadOnlyList<string> Fallos => _log;

    public IReadOnlyList<string> Notas
    {
        get
        {
            var todo = new List<string>();
            todo.AddRange(AcadInterop.Bitacora);
            todo.AddRange(_notas);
            return todo;
        }
    }

    /// <summary>Cuántos elementos se dibujaron de verdad, por tipo.</summary>
    public sealed class Resumen
    {
        public int Columnas { get; set; }
        public int Trabes { get; set; }
        public int Muros { get; set; }
        public int Losas { get; set; }
        public int Diagonales { get; set; }

        public int Total => Columnas + Trabes + Muros + Losas + Diagonales;

        public override string ToString() =>
            $"{Columnas} columna(s), {Trabes} trabe(s), {Muros} muro(s), " +
            $"{Losas} losa(s), {Diagonales} diagonal(es)";
    }

    // ==================================================================
    // Entrada
    // ==================================================================

    /// <summary>
    /// Dibuja la planta completa y devuelve qué se dibujó.
    /// </summary>
    /// <param name="p">La planta, ya filtrada por nivel y por tipo.</param>
    /// <param name="x0">Desplazamiento en X, para no encimar dos plantas.</param>
    /// <param name="y0">Desplazamiento en Y.</param>
    public Resumen Dibujar(PlantaCad p, double x0 = 0, double y0 = 0)
    {
        var r = new Resumen();

        // Cada planta tiene sus ejes: si se quedara el de la anterior, el rótulo de esta se
        // colgaría de una cuadrícula que está en otro sitio del dibujo.
        _abajoDeLosEjes = null;
        _cadenasTapadas = new HashSet<ElementoPlanta>();
        _rotulosSobreCastillo = 0;
        _murosDeConcreto = 0;
        _contornosMc = 0;
        _sinLeyendaMc = 0;
        _muroConAlgoAbajo = 0;
        _basesDeMuroDeArriba = 0;
        _muroDeArribaConCadena = 0;
        _muroConcretoVistos = 0;
        _muroMamposteriaVistos = 0;
        _muroSinMaterial = 0;
        _notasDeMuro.Clear();

        AsegurarCapas();
        AsegurarEstiloTexto();

        // Los estilos de la macro: TEXTO_SECCIONES, TEXTO_CADENAS, TEXTO_LOSAS, COTA y
        // COTA_DIM. Sin ellos las cotas saldrían con la letra de fábrica de AutoCAD.
        AsegurarEstilosDeLaMacro();

        // ==============================================================================
        //  LA ESCALERA SE APARTA, Y SE APARTA LO PRIMERO DE TODO
        // ==============================================================================
        //  Se pidió tal cual: «nada de losa de escalera en planos, tampoco las que se modelan
        //  como muro, solo dibuja el contorno de las escaleras, puro contorno nada más». El
        //  razonamiento está en EscaleraEnPlanta; en corto: una escalera no es un tablero de
        //  losa —no se arma con parrilla ni se cota como un paño— pero tampoco puede
        //  desaparecer, porque hay que ver dónde está. Su armado va en su detalle aparte.
        //
        //  VA LO PRIMERO, Y ESO IMPORTA DOS VECES:
        //
        //   · Antes de las conversiones de shell que vienen justo debajo. Un peldaño modelado
        //     como shell angosto y corto es exactamente lo que buscan el castillo de muro y la
        //     cadena de muro, así que llegando después convertirían los peldaños en cadenas y
        //     entonces ya no serían muros: se colarían al plano con su capa y su rótulo.
        //
        //   · Antes de las huellas, los tableros, los voladizos y el recuadro del título. Es
        //     lo que hace cierto el «nada más»: ninguna de esas etapas llega a verla.
        _escalerasDeLaPlanta.Clear();

        if (_cfg.Bandera("IGNORAR_LOSA_ESCALERA", true))
        {
            _escalerasDeLaPlanta.AddRange(PlanoEstructural.EscaleraEnPlanta.Apartar(
                p.Elementos,
                _cfg.Texto("PALABRAS_ESCALERA", "ESCALERA,ESCAL,STAIR,RAMPA,RAMP,DESCANSO")));
        }

        // ==============================================================================
        //  EL CASTILLO MODELADO COMO SHELL DE MURO, A CASTILLO
        // ==============================================================================
        //  Se pidió: «los shells de muro que tengan en property note CASTILLO igual hacerlos
        //  bloques y rellenarlos con amarillo como un frame normal, OJO solo si dice CASTILLO».
        //  Un castillo se puede modelar de las dos maneras —frame de 15×15 o shell angosto,
        //  que es lo que sale al dibujarlo junto con su muro— y dibujados como muro salían
        //  como dos rayas, sin bloque y sin relleno: la misma cosa se veía de dos formas.
        //
        //  VA AQUÍ, ANTES DE NADA, porque los apoyos y las huellas se calculan justo abajo: si
        //  la conversión llegara después, los muros morirían en el EJE de este castillo en vez
        //  de en su paño y el contorno de la losa se le metería por dentro.
        //  Y COMPLETO: los pedazos del mismo castillo se unen antes de convertirlo. Un
        //  castillo de shell casi nunca llega de una pieza —el modelador lo dibuja hasta el
        //  antepecho y del dintel arriba, y en planta los dos paneles ocupan el mismo sitio—,
        //  así que salían dos bloques encimados, o el castillo en dos mitades cuando el corte
        //  era a lo largo.
        if (_cfg.Bandera("SHELL_CASTILLO_COMO_COLUMNA", true))
        {
            var deShell = PlanoEstructural.CastilloDeMuro.Normalizar(
                p.Elementos, EspesorMuroPorOmision,
                _cfg.Numero("SHELL_CASTILLO_UNIR_TOL_CM", 2) / 100,
                _cfg.Texto("SHELL_CASTILLO_PREFIJO", "K"),

                // Y HASTA EL PAÑO del muro con el que se cruza. Con la holgura del encuentro
                // que ya usa el recorte de los muros —PANO_TOLERANCIA_CM—, que es la misma
                // pregunta: ¿este elemento llega a aquel?
                _cfg.Bandera("SHELL_CASTILLO_AL_PANO", true)
                    ? _cfg.Numero("PANO_TOLERANCIA_CM", 25) / 100
                    : 0);

            // ==========================================================================
            //  Y LAS CADENAS DE SHELL, IGUAL
            // ==========================================================================
            //  Una cadena también se modela como shell —las INTERMEDIAS casi siempre, porque se
            //  dibujan como un trozo del propio muro— y dibujada como muro no era una cadena para
            //  nada: sin su capa, sin su rótulo, sin su relleno en el corte y sin su bloque. Es
            //  lo que se reportó de la cadena intermedia una y otra vez.
            var cadenasDeShell = PlanoEstructural.CadenaDeMuro.Normalizar(
                p.Elementos, AnchoTrabePorOmision);

            if (cadenasDeShell > 0)
            {
                Nota($"{cadenasDeShell} cadena(s) venían como shell de muro: se dibujaron como " +
                     "cadena —su capa, su rótulo y, en el corte, su relleno y su bloque—.");
            }

            if (deShell > 0)
            {
                Nota($"{deShell} castillo(s) venían como shell de muro con CASTILLO en sus " +
                     "notas: se dibujaron completos y como bloque —relleno amarillo— igual " +
                     "que uno de frame.");
            }
        }

        // ==============================================================================
        //  LO QUE HAY QUE SABER ANTES DE DIBUJAR NADA
        // ==============================================================================
        //  Los APOYOS —las columnas y los castillos— y las HUELLAS de las barras: el
        //  rectángulo que cada muro, trabe o cadena ocupa en planta. De ahí salen tres cosas
        //  que se ven en el plano:
        //    · las líneas del muro mueren en el PAÑO del castillo y no en su eje;
        //    · una VIGA muere en la cara de la viga que cruza, en lugar de pasarle por
        //      encima y dejar una reja de líneas cruzadas en cada nudo;
        //    · el CONTORNO de la losa no se dibuja por dentro del muro ni de la cadena, y
        //      los lados apoyados dicen si el paño está VOLADO.
        var apoyos = p.Elementos.Where(e => e.Clase == ClasePlanta.Columna).ToList();

        var huellas = new List<ElementoPlanta>();

        foreach (var el in p.Elementos)
        {
            if (el.Clase is not (ClasePlanta.Muro or ClasePlanta.Trabe))
            {
                continue;
            }

            var anchoHuella = el.AnchoM > LargoMinimo
                ? el.AnchoM
                : el.Clase == ClasePlanta.Muro ? EspesorMuroPorOmision : AnchoTrabePorOmision;

            huellas.Add(PanoDeApoyo.Huella(el, anchoHuella));
        }

        var cruces = _cfg.Bandera("VIGAS_CORTAR_EN_CRUCES", true) ? huellas : null;

        // ==============================================================================
        //  TODOS LOS VOLADIZOS DE LA PLANTA, ANTES DE DIBUJAR EL PRIMERO
        // ==============================================================================
        //  Hace falta conocerlos TODOS de antemano para no dibujar la orilla que dos
        //  voladizos pegados comparten. Si se fueran descubriendo por el camino, la primera
        //  losa dibujaría su raya —porque aún no sabe de la segunda— y la segunda ya no:
        //  quedaría media junta, que es peor que la junta entera.
        _voladosDeLaPlanta.Clear();

        var palabrasVolado = PalabrasDeVolado();

        foreach (var el in p.Elementos)
        {
            if (el.Clase == ClasePlanta.Losa
                && el.Vertices.Count >= 3
                && LosaEnPlanta.DiceVolado(el.Notas, el.Seccion, palabrasVolado))
            {
                _voladosDeLaPlanta.Add((ClaveDelPano(el), el.Vertices));
            }
        }

        // ==============================================================================
        //  LOS PEDAZOS DE UNA MISMA LOSA, EN UN SOLO TABLERO
        // ==============================================================================
        //  Se pidió: «si tengo varias secciones de losa en un mismo tablero, júntalas para que
        //  solo dé un armado». Y es exactamente lo que hay que hacer, porque esos pedazos NO son
        //  losas distintas: es una sola que el MESH partió —en los nudos de las trabes, en los
        //  ejes, o donde el programa decidió al mallar—. Dibujando cada shell por su cuenta salían
        //  tres armados pequeños dentro del mismo tablero y tres rótulos encimados, o sea la malla
        //  del programa de cálculo copiada al papel.
        //
        //  CON EL LÍMITE QUE SE PIDIÓ: la unión tiene que quedar dentro de los muros, las trabes o
        //  las cadenas que limitan el tablero. Dos pedazos se juntan solo si la orilla que
        //  comparten está LIBRE; si por ahí corre un apoyo son dos tableros, y cada uno lleva su
        //  armado, porque el apoyo interrumpe el claro y ahí cambia el acero.
        //
        //  Y se calculan TODOS antes de dibujar el primer paño, como los voladizos y por lo mismo:
        //  cada pedazo tiene que saber a qué tablero pertenece antes de decidir si le toca dibujar
        //  el armado y el rótulo o callarse.
        _tablerosDeLaPlanta.Clear();

        if (_cfg.Bandera("LOSA_UNIR_TABLEROS", true))
        {
            _tablerosDeLaPlanta.AddRange(TableroDeLosa.Agrupar(
                p.Elementos,
                huellas,
                _cfg.Numero("LOSA_TABLERO_TOL_CM", 5) / 100,
                _cfg.Numero("LOSA_TABLERO_APOYO_CUBRE", 0.5),
                el => FamiliaDeLaLosa(el, huellas)));

            AvisarDeLosTableros();
        }

        // ==============================================================================
        //  SOLO LA CUADRÍCULA, SI ES LO QUE SE PIDIÓ
        // ==============================================================================
        //  Se pidió poder dibujar «solo ejes y cortes sin hacer todo el dibujo de planos», y se
        //  entiende para qué: montar la cuadrícula sobre un plano de arquitectura que ya existe,
        //  o replantear con las cotas de los ejes y nada más.
        //
        //  Se salta el dibujo de los ELEMENTOS y se va derecho a los ejes, que están más abajo.
        //  Los elementos siguen contando para el rectángulo que los ejes cubren y para el paño al
        //  que se corren los de orilla —esos cálculos ya están hechos, arriba—, así que la
        //  cuadrícula sale EN EL MISMO SITIO que saldría con la planta entera dibujada. Eso es lo
        //  que permite dibujar primero los ejes, revisarlos, y dibujar la estructura después
        //  encima sin que nada se mueva.
        if (p.SoloEjes)
        {
            var cajaSola = Envolvente(p);

            DibujarEjesDeLaPlanta(
                p, x0, y0, cajaSola.XMin, cajaSola.YMin, cajaSola.XMax, cajaSola.YMax);

            RotuloDeLaPlanta(p, x0, y0, cajaSola.XMin, cajaSola.YMin, cajaSola.XMax);

            Nota("Se dibujó SOLO la cuadrícula: los ejes con sus burbujas y sus cotas, sin los " +
                 "elementos. La estructura se puede dibujar después encima y caerá en su sitio, " +
                 "porque los ejes se colocan con las medidas de los elementos aunque no se " +
                 "dibujen.");

            return r;
        }

        // Las losas PRIMERO, para que las trabes y las columnas queden encima. En
        // AutoCAD el orden de creación es el orden de dibujo, así que basta con
        // dibujarlas antes; no hace falta tocar el DrawOrder.
        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Losa))
        {
            if (Losa(el, x0, y0, huellas))
            {
                r.Losas++;
            }
        }

        // Las escaleras, de puro contorno: se apartaron al empezar, así que no han pasado por
        // nada de lo de arriba. Se les pasan las huellas para que su línea muera en el PAÑO del
        // muro y no en su eje.
        Escaleras(x0, y0, huellas);

        // Y donde NO hay losa: el hueco del elevador, del ducto o de la doble altura, con su
        // contorno a trazos y su cruz. Después de los paños para que la línea del vacío quede
        // por encima de su achurado.
        Vacios(p, x0, y0);

        // ==============================================================================
        //  EL MURO QUE VA DEBAJO DE UNA CADENA NO SE DIBUJA
        // ==============================================================================
        //  Es MarcarMurosTapados. En el modelo el muro y su cadena de cerramiento ocupan LA
        //  MISMA LÍNEA en planta —la cadena corre encima del muro, de castillo a castillo—,
        //  así que dibujando los dos el plano sale con dos parejas de líneas pegadas: eso
        //  era la raya de más a cada lado de cada cadena.
        //
        //  Se borran las líneas del muro tapado y SOLO se dejan las de los muros que NO
        //  llevan cadena, que son los que hay que ver.
        var ocultarTapados = _cfg.Bandera("OCULTAR_MURO_BAJO_CADENA", true);
        var incluirTrabes = _cfg.Bandera("CADENA_INCLUYE_TRABES", false);
        var tolCadena = _cfg.Numero("TOLERANCIA_CADENA_CM", 10) / 100;
        var traslapeMin = _cfg.Numero("TRASLAPE_MINIMO", 0.8);

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Muro))
        {
            var tramo = Pano.Recortar(el, apoyos, cruces);

            var bajoCadena = MuroBajoCadena.Como(
                el, p.Elementos, incluirTrabes, tolCadena, traslapeMin);

            // El ancho de la cadena que lo tapa, para que el rótulo del PIER se separe de
            // ella y no le caiga encima. Es el eTapaB de la macro.
            _anchoDeLaCadena[el] = bajoCadena.AnchoCadena;

            var tapado = ocultarTapados && bajoCadena.Tapado;

            if (tapado)
            {
                _tapados++;
            }

            // ==============================================================================
            //  EL MURO DE CONCRETO SE DIBUJA SIEMPRE, TAPADO O NO
            // ==============================================================================
            //  Se pidió con esas palabras: «aún no me dibuja la base de los muros de concreto en
            //  la planta de cimentación, la línea debe estar en la capa E-MURO DE CONCRETO pero
            //  haz que aparezca SIEMPRE».
            //
            //  Y esta es la razón por la que no aparecía: en cimentación casi todos los muros
            //  llevan su cadena de desplante encima, así que OCULTAR_MURO_BAJO_CADENA los daba
            //  por tapados y NO SE DIBUJABA NINGUNO. El contorno estaba metido dentro del
            //  if (!tapado), de modo que la regla de la cadena se lo comía antes de empezar.
            //
            //  La regla de la cadena sigue siendo la correcta para el MURO NORMAL: el muro y su
            //  cadena ocupan la misma línea en planta, y dibujar los dos deja dos parejas de
            //  rayas pegadas. Pero la BASE de un muro de concreto no es lo mismo que el muro: es
            //  el desplante que hay que colar, va en su propia capa y tiene que estar en el plano
            //  de cimentación aunque encima lleve una cadena. Por eso va aparte y no comparte el
            //  if, igual que ya hacía la línea de mampostería con MAMPOSTERIA_AUNQUE_TAPADO.
            //
            //  La capa se pide SIN mirar 'tapado' a propósito: CapaDeMuro() devuelve la capa
            //  genérica cuando el muro está tapado, y aquí se quiere E-MURO DE CONCRETO siempre,
            //  que es lo que se pidió explícitamente.
            var esConcreto = EsMuroDeConcreto(el);

            // ==============================================================================
            //  QUÉ CLASIFICACIÓN LE SALIÓ A CADA MURO, Y CON QUÉ NOTA
            // ==============================================================================
            //  Esto existe porque el usuario y yo nos quedamos atascados: él veía que no se
            //  dibujaba la base del muro de concreto y desde el código no había forma de saber si
            //  era porque la regla no se aplicaba, porque la cadena lo tapaba, o porque el muro
            //  NO ESTABA CLASIFICADO COMO CONCRETO. Las tres se ven igual desde el plano: no hay
            //  línea.
            //
            //  Y era la tercera. La pista estaba en otra nota del propio programa —«su línea de
            //  mampostería se dibuja en todos»—, porque esa línea solo se dibuja cuando el
            //  material es MAMPOSTERIA: si sale en todos, ninguno es de concreto.
            //
            //  Así que el resumen dice ahora la cuenta por material Y las notas que llegaron. Con
            //  eso, mirar el aviso una vez sustituye a adivinar.
            if (esConcreto)
            {
                _muroConcretoVistos++;
            }
            else if (string.Equals(el.Material, "MAMPOSTERIA", StringComparison.OrdinalIgnoreCase))
            {
                _muroMamposteriaVistos++;
            }
            else
            {
                _muroSinMaterial++;
            }

            // Las notas distintas que trae el modelo, para poder ver QUÉ dice la property note en
            // lugar de suponerlo. Se guardan pocas: es un aviso, no un volcado.
            if (!esConcreto && _notasDeMuro.Count < 8)
            {
                var nota = (el.Notas ?? string.Empty).Trim();

                _notasDeMuro.Add(nota.Length > 0 ? nota : "(sin nota)");
            }

            // ==============================================================================
            //  LA BASE SE DIBUJA SI DEBAJO DEL MURO NO HAY NADA
            // ==============================================================================
            //  Es el criterio que se pidió: la línea de la base solo cuando abajo no hay nada, ni
            //  otro nivel, y que valga para distintos niveles. La comprobación mira los NIVELES
            //  DEL MODELO, no la planta que se dibuja, así que sale sola en cualquier planta.
            //
            //  SOLO PARA MURO DE CONCRETO. Se pidió expresamente, y es lo correcto: esta línea es
            //  el desplante de un muro que se cuela, y en un plano estructural dibujarla en un
            //  muro de mampostería diría que hay algo que colar donde no lo hay. Un muro de
            //  tabicón apoya en su cadena de desplante, y esa cadena ya se dibuja por su cuenta.
            //
            //  CONSECUENCIA, y hay que tenerla presente: si el modelo no trae muros con CONCRETO
            //  en su property note, aquí no se dibuja NADA. Es lo que pasa con un modelo cuyos
            //  muros dicen TABICON. No es un fallo del dibujo: es que no hay muros de concreto, y
            //  el resumen lo dice con esas palabras y con las notas que llegaron.
            var nadaAbajo = NadaDebajoDelMuro(el, p);

            // ==============================================================================
            //  EL MURO DE CONCRETO ES EL QUE NO LLEVA CADENA DE DESPLANTE
            // ==============================================================================
            //  Se dijo con estas palabras: «como los muros de concreto NO LLEVAN CADENA DE
            //  DESPLANTE, debe verse su cara inferior representada con 2 líneas, que es su grosor,
            //  y con la leyenda MC; cuando no haya muro debajo del muro de concreto».
            //
            //  Ahí está la definición, y es la que faltaba. En este modelo la property note dice
            //  TABICON en los 21 muros, así que atarse a la nota no dibuja NADA —es lo que llevaba
            //  pasando—. Pero el plano sí distingue los dos: el muro de mampostería lleva su
            //  cadena de desplante encima y el de concreto no la lleva, porque se cuela con la
            //  cimentación. O sea que NO TENER CADENA es la señal de que es de concreto.
            //
            //  Y esto invierte lo que yo había hecho: había puesto MURO_CONCRETO_AUNQUE_TAPADO
            //  para dibujarlo AUNQUE tuviera cadena, que es exactamente lo contrario del
            //  criterio. El que tiene cadena NO lleva esta línea: ahí se ve la cadena.
            var sinCadena = !bajoCadena.Tapado;

            var esMuroConcreto = esConcreto
                                 || (sinCadena
                                     && _cfg.Bandera("MURO_SIN_CADENA_ES_CONCRETO", true));

            var baseMc = esMuroConcreto
                         && sinCadena
                         && nadaAbajo
                         && _cfg.Bandera("MURO_CONCRETO_CONTORNO", true)
                         && (!_cfg.Bandera("MURO_CONCRETO_SOLO_CIMENTACION", true)
                             || Rot.EsCimentacion(p.Nivel));

            var dibujado = false;

            if (baseMc)
            {
                var capaConcreto = _cfg.Bandera("MURO_CONCRETO_CAPA_PROPIA", true)
                    ? _capas.CapaMuroConcreto
                    : CapaDe(el);

                var espesorDelMuro = Espesor(el, EspesorMuroPorOmision, "muro");

                // La LEYENDA PRIMERO y las líneas después, por el mismo motivo que en el bucle de
                // los muros de arriba: las dos van en la misma capa, y al subir la capa al frente
                // se conserva el orden de dentro. Con el texto dibujado después quedaba encima y
                // cortaba las líneas.
                LeyendaDeMuro(el, x0, y0, capaConcreto, tramo, espesorDelMuro);

                // DOS LÍNEAS, no un contorno cerrado. Es lo que se pidió —«su cara inferior
                // representada con 2 líneas que es su grosor»— y es lo que Barra() ya hace: los
                // dos paños del muro, separados su espesor. Un contorno cerrado añadiría tapas en
                // los extremos que ahí no van, porque el muro sigue.
                if (Barra(el, x0, y0, capaConcreto, espesorDelMuro, conEje: false, tramo))
                {
                    _contornosMc++;
                    dibujado = true;
                }
            }
            else if (esMuroConcreto && !nadaAbajo)
            {
                // Solo se cuenta el que SÍ es de concreto: es el único al que le tocaría la línea,
                // así que es el único cuya ausencia hay que explicar.
                _muroConAlgoAbajo++;
            }

            // El muro NORMAL: solo si NO está tapado y si la base de concreto no lo dibujó ya. El
            // muro cuenta igual en el resumen aunque esté tapado, porque está en el modelo y su
            // mampostería y su pier sí se dibujan.
            if (!tapado && !dibujado)
            {
                var capaMuro = CapaDeMuro(el, tapado);

                if (!string.Equals(capaMuro, CapaDe(el), StringComparison.OrdinalIgnoreCase))
                {
                    _murosDeConcreto++;
                }

                Barra(el, x0, y0, capaMuro,
                      Espesor(el, EspesorMuroPorOmision, "muro"), conEje: false, tramo);
            }

            r.Muros++;

            // Y si es de BLOCK, su polilínea ancha al centro: es la marca de
            // mampostería, y es lo que distingue de un golpe de vista un muro de block
            // de uno de concreto. Va sobre el tramo YA recortado, así que su separación
            // se mide desde el paño del castillo y no desde el eje.
            //
            // SE DIBUJA AUNQUE EL MURO ESTÉ TAPADO —MAMPOSTERIA_AUNQUE_TAPADO—: si
            // desapareciera con el muro, el plano no diría de qué es la pared.
            if (!tapado || _cfg.Bandera("MAMPOSTERIA_AUNQUE_TAPADO", true))
            {
                LineaDeMamposteria(el, x0, y0, tramo);
            }
        }

        // ==============================================================================
        //  LA BASE DE LOS MUROS DE LA PLANTA BAJA, EN LA CIMENTACIÓN
        // ==============================================================================
        //  Se pidió: «pon las líneas de la base del muro de la planta baja en la cimentación».
        //
        //  Y ESTO ES LO QUE FALTABA, no las reglas de antes. Un muro de planta baja pertenece al
        //  story de planta baja, así que la planta de cimentación NO LO TIENE en p.Elementos: el
        //  bucle de arriba nunca lo vio. Todo lo que corregí antes operaba sobre una lista que no
        //  contenía esos muros, y por eso no se dibujaba nada por más vueltas que diera.
        //
        //  SOLO PARA CONCRETO, y sin preguntarle a ETABS de qué es el muro: se usa la definición
        //  que se dio antes —«los muros de concreto NO LLEVAN CADENA DE DESPLANTE»—. Se mira si en
        //  ESTA planta, la de cimentación, hay una cadena debajo del muro:
        //
        //    · si NO hay cadena  -> es de concreto, se cuela con la cimentación, y lleva su base
        //    · si SÍ hay cadena  -> es de mampostería, apoya en su cadena, y no lleva base
        //
        //  Es el criterio del plano y no el de la nota, que en este modelo dice TABICON en todos.
        //  Y la property note se sigue respetando: si dice CONCRETO, califica igual.
        foreach (var el in p.MurosDeArriba)
        {
            var bajoCadena = MuroBajoCadena.Como(
                el, p.Elementos, incluirTrabes, tolCadena, traslapeMin);

            var esDeConcreto = EsMuroDeConcreto(el)
                               || (!bajoCadena.Tapado
                                   && _cfg.Bandera("MURO_SIN_CADENA_ES_CONCRETO", true));

            if (!esDeConcreto)
            {
                _muroDeArribaConCadena++;
                continue;
            }

            var capaConcreto = _cfg.Bandera("MURO_CONCRETO_CAPA_PROPIA", true)
                ? _capas.CapaMuroConcreto
                : CapaDe(el);

            // El tramo se recorta a los paños de los castillos de ESTA planta, igual que un muro
            // normal: si no, la base se metería dentro del castillo.
            var tramoArriba = Pano.Recortar(el, apoyos, cruces);

            var espesorMuro = Espesor(el, EspesorMuroPorOmision, "muro");

            // ==============================================================================
            //  LA LEYENDA PRIMERO Y LAS LÍNEAS DESPUÉS
            // ==============================================================================
            //  El orden importa y era el fallo. La leyenda va en LA MISMA CAPA que las líneas, y
            //  al subir una capa al frente se conserva el orden relativo de lo que hay dentro. Con
            //  la leyenda dibujada DESPUÉS, quedaba por encima de las líneas y su fondo opaco las
            //  cortaba: en el plano se veían las dos líneas interrumpidas justo en el «MC», que es
            //  exactamente el síntoma reportado.
            //
            //  Dibujando la leyenda ANTES, las líneas nacen después y quedan encima de ella. Así,
            //  cuando la capa sube al frente, las líneas suben por encima del texto y ya no hay
            //  nada que las corte.
            LeyendaDeMuro(el, x0, y0, capaConcreto, tramoArriba, espesorMuro);

            // DOS LÍNEAS, que son su grosor. Es lo que hace Barra().
            if (Barra(el, x0, y0, capaConcreto, espesorMuro, conEje: false, tramoArriba))
            {
                _basesDeMuroDeArriba++;
            }
        }

        // ==============================================================================
        //  DE VARIAS CADENAS EN LA MISMA LÍNEA, SOLO LA MÁS ALTA
        // ==============================================================================
        //  Se pidió: «si hay cadena intermedia abajo no lo muestres en planta; en planta solo
        //  muestra la cadena más alta que exista, solo dibuja una». Un muro de mampostería lleva
        //  tres cadenas sobre el mismo paño —desplante, intermedia y cerramiento—, las tres son
        //  del mismo nivel y las tres ocupan LA MISMA LÍNEA en planta: se dibujaban las tres,
        //  una encima de la otra, con tres rótulos pisándose. Y en una planta no hay forma de
        //  distinguirlas, porque una planta no tiene alturas.
        //
        //  Se dibuja la de arriba, que es la que se ve al mirar el piso desde arriba.
        _cadenasTapadas = _cfg.Bandera("CADENA_SOLO_LA_MAS_ALTA", true)
            ? PlanoEstructural.CadenaMasAlta.Tapadas(p.Elementos, tolCadena)
            : new HashSet<ElementoPlanta>();

        if (_cadenasTapadas.Count > 0)
        {
            Nota($"{_cadenasTapadas.Count} cadena(s) no se dibujaron: sobre su misma línea va " +
                 "otra más alta, y en planta las dos son la misma raya. Se dibuja la de arriba.");
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Trabe))
        {
            // LA QUE TIENE OTRA MÁS ALTA ENCIMA NO SE DIBUJA, pero cuenta en el resumen: está
            // en el modelo, y el resumen dice lo que hay, no lo que se pintó.
            if (_cadenasTapadas.Contains(el))
            {
                r.Trabes++;
                continue;
            }

            // LA CADENA SIN MURO DE PISO A TECHO VA CON OTRA LÍNEA. Es MarcarCadenasSinMuro:
            // una cadena de cerramiento que no lleva su muro completo debajo se marca con
            // ACAD_ISO02W100 para que se vea de un golpe; con muro completo, línea normal.
            var punteada = LineaDeCadenaSinMuro(el, p);

            if (Barra(el, x0, y0, CapaDe(el),
                     Espesor(el, AnchoTrabePorOmision, "trabe"), conEje: true,
                     Pano.Recortar(el, apoyos, cruces), punteada))
            {
                r.Trabes++;
            }
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Diagonal))
        {
            // La diagonal en planta es su proyección: una línea, y a trazos, porque
            // no está en el plano del piso. Dibujarla con paños engañaría.
            if (Linea(el.X1 + x0, el.Y1 + y0, el.X2 + x0, el.Y2 + y0, CapaEjes) is not null)
            {
                r.Diagonales++;
            }
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Columna))
        {
            if (Columna(el, x0, y0))
            {
                r.Columnas++;
            }
        }

        if (p.ConRotulos)
        {
            foreach (var el in p.Elementos)
            {
                Rotulo(el, p, x0, y0, p.AlturaTexto);
            }
        }

        // ---- LO QUE CONVIERTE EL DIBUJO EN UN PLANO -------------------------------
        // El rectángulo de lo dibujado: lo piden los ejes, las cotas y el rótulo, y se
        // calcula UNA vez.
        var caja = Envolvente(p);

        // Los ejes con sus burbujas y las cotas en los cuatro lados.
        DibujarEjesDeLaPlanta(p, x0, y0, caja.XMin, caja.YMin, caja.XMax, caja.YMax);

        // Y el rótulo de dos renglones, debajo de los ejes de abajo.
        RotuloDeLaPlanta(p, x0, y0, caja.XMin, caja.YMin, caja.XMax);

        // UN solo renglón con los que se dibujaron con el espesor de omisión, y otro con
        // los muros que se quedaron sin pier, en lugar de uno por elemento.
        ResumirEspesores();
        ResumirPiers();

        if (_murosDeConcreto > 0)
        {
            Nota($"{_murosDeConcreto} muro(s) de concreto sin cadena se dibujaron en la capa " +
                 $"'{_capas.CapaMuroConcreto}'. Los que llevan cadena no se dibujan: ahí se ve " +
                 "la cadena, como en la mampostería.");
        }

        if (_muroConAlgoAbajo > 0)
        {
            Nota($"A {_muroConAlgoAbajo} muro(s) no se les dibujó la línea de la base porque " +
                 "DEBAJO HAY OTRO NIVEL: ahí el muro no apoya, apoya en la losa o la trabe de ese " +
                 "nivel, así que dibujarle una base sería dibujar un desplante que no existe. " +
                 "Solo la lleva el muro que arranca desde lo más bajo del edificio.");
        }

        if (_contornosMc > 0)
        {
            var leyenda = _cfg.Texto("MURO_CONCRETO_LEYENDA", "MC").Trim();

            Nota($"{_contornosMc} muro(s) DE CONCRETO llevan su cara inferior dibujada con dos " +
                 $"líneas —su grosor— en la capa '{_capas.CapaMuroConcreto}'" +
                 (leyenda.Length > 0 ? $" y con la leyenda '{leyenda}' dentro" : string.Empty) +
                 ". Se toman como de concreto los que NO llevan cadena de desplante, que en el " +
                 "plano es la señal que los distingue, además de los que lo dicen en su property " +
                 "note. Y solo si debajo no hay ningún nivel, porque es ahí donde apoyan.");
        }

        // ==============================================================================
        //  SI NO SALIÓ NI UN MURO DE CONCRETO, SE DICE POR QUÉ
        // ==============================================================================
        //  Callar aquí es lo que hizo perder varias vueltas: el plano sin la línea del muro de
        //  concreto se ve igual si la regla no se aplicó, si la cadena lo tapó o si el muro no
        //  está clasificado como concreto. Se dice la cuenta por material y QUÉ notas llegaron.
        if (_contornosMc == 0
            && (_muroMamposteriaVistos > 0 || _muroSinMaterial > 0))
        {
            var detalle =
                $"Muros vistos: {_muroConcretoVistos} de concreto, " +
                $"{_muroMamposteriaVistos} de mampostería, " +
                $"{_muroSinMaterial} sin material reconocido.";

            if (_muroConcretoVistos == 0)
            {
                detalle +=
                    " NINGUNO salió de concreto, así que no hay base de muro de concreto que " +
                    "dibujar. La property note de ETABS es la que manda, y estas son las que " +
                    "llegaron: " + string.Join(" | ", _notasDeMuro) + ". " +
                    "Para que un muro salga de concreto, su property note tiene que traer una " +
                    "de las palabras de PALABRAS_CONCRETO (" +
                    _cfg.Texto("PALABRAS_CONCRETO", "CONCRETO,CONCRETE,C.A.,REFORZADO") + "). " +
                    "Ojo: si la nota trae además una palabra de mampostería, gana la " +
                    "mampostería; para ese caso está MURO_CONCRETO_POR_NOTA, que hace que la " +
                    "nota mande sobre el nombre de la sección.";
            }

            Nota(detalle);
        }

        if (_basesDeMuroDeArriba > 0)
        {
            var leyendaB = _cfg.Texto("MURO_CONCRETO_LEYENDA", "MC").Trim();

            Nota($"{_basesDeMuroDeArriba} muro(s) DE CONCRETO de la planta baja llevan la línea de " +
                 $"su base dibujada aquí, con dos líneas —su grosor— en la capa " +
                 $"'{_capas.CapaMuroConcreto}'" +
                 (leyendaB.Length > 0 ? $" y con la leyenda '{leyendaB}' dentro" : string.Empty) +
                 ". Se toman como de concreto los que NO llevan cadena de desplante debajo, que es " +
                 "la señal del plano, sin depender de lo que diga su property note.");
        }

        if (_muroDeArribaConCadena > 0)
        {
            Nota($"Otros {_muroDeArribaConCadena} muro(s) de la planta baja NO llevan base porque " +
                 "tienen cadena de desplante debajo: esos son de mampostería, apoyan en su cadena " +
                 "y la cadena ya está dibujada. Si alguno es de concreto, quítale la cadena en el " +
                 "modelo o pon CONCRETO en su property note.");
        }

        if (_tapados > 0)
        {
            Nota($"{_tapados} muro(s) llevan cadena de desplante encima, así que NO se les dibuja " +
                 "la cara inferior: ahí lo que se ve es la cadena. Son los de mampostería. Si " +
                 "alguno de esos es de concreto, quítale la cadena en el modelo o pon CONCRETO en " +
                 "su property note.");
        }

        if (_sinLeyendaMc > 0)
        {
            Nota($"A {_sinLeyendaMc} muro(s) de concreto no les cupo la leyenda: son más cortos " +
                 "que el texto. El contorno sí está. Si los necesitas rotulados, baja " +
                 "MURO_CONCRETO_LEYENDA_ALTURA en la hoja de configuración.");
        }

        if (_rotulosSobreCastillo > 0)
        {
            Nota($"{_rotulosSobreCastillo} nombre(s) de cadena no se escribieron: caían encima " +
                 "de un castillo de área, sobre su relleno y sobre su propio rótulo.");
        }

        return r;
    }

    /// <summary>
    /// Dibuja <b>TODAS las plantas de un jalón</b>, una al lado de otra, como la macro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es como se usa de verdad: un edificio son cinco o seis plantas y se quieren las seis
    /// en el dibujo, no una y volver a pulsar. La macro las reparte con estas reglas, que
    /// son las que se siguen aquí:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     El <b>orden</b> lo dice <c>ORDEN_NIVELES</c>: <c>ASC</c> —el de omisión— pone
    ///     primero el nivel más bajo, así que el juego se lee de izquierda a derecha
    ///     empezando por la cimentación.
    ///   </item>
    ///   <item>
    ///     El <b>paso</b> horizontal es el ancho de la planta más
    ///     <c>SEPARACION_ENTRE_PLANTAS</c> —10 m—, y es el <b>mismo para todas</b>: se toma
    ///     el rectángulo que las envuelve a todas, no el de cada una, para que queden
    ///     alineadas y a la misma distancia. Con el ancho de cada una, dos plantas
    ///     distintas quedarían descuadradas.
    ///   </item>
    ///   <item>
    ///     Todas arrancan en la misma Y —la de <c>OFFSET_Y_INICIAL</c>, 25, si el dibujo
    ///     está vacío, o encima de lo que ya haya—, así que los rótulos quedan en línea.
    ///   </item>
    ///   <item>
    ///     Y <c>PLANTAS_POR_FILA</c> —100— es cuántas caben en una fila antes de bajar a la
    ///     siguiente. Con 100 es lo mismo que decir «todas en una fila».
    ///   </item>
    /// </list>
    /// <para>
    /// Lo que <b>todavía no</b> hace, y es lo que falta para que salga igual que la suya:
    /// los ejes con burbujas y las cotas en los cuatro lados, los bloques de sección
    /// rellenos, el armado de losa y el rótulo de dos renglones con su tipografía. Eso es el
    /// dibujante nuevo, etapas 3 y 4 de <c>docs/plan-port-planos-estructurales.md</c>.
    /// </para>
    /// </remarks>
    /// <param name="plantas">Una por nivel, ya filtradas.</param>
    public Resumen DibujarTodas(IReadOnlyList<PlantaCad> plantas)
    {
        var total = new Resumen();

        if (plantas.Count == 0)
        {
            return total;
        }

        AsegurarCapas();
        AsegurarEstiloTexto();

        // El rectángulo que envuelve a TODAS: el paso tiene que ser uno solo.
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        foreach (var p in plantas)
        {
            foreach (var el in p.Elementos)
            {
                if (el.Vertices.Count > 0)
                {
                    foreach (var v in el.Vertices)
                    {
                        xMin = Math.Min(xMin, v.X); xMax = Math.Max(xMax, v.X);
                        yMin = Math.Min(yMin, v.Y); yMax = Math.Max(yMax, v.Y);
                    }
                }

                xMin = Math.Min(xMin, Math.Min(el.X1, el.X2));
                xMax = Math.Max(xMax, Math.Max(el.X1, el.X2));
                yMin = Math.Min(yMin, Math.Min(el.Y1, el.Y2));
                yMax = Math.Max(yMax, Math.Max(el.Y1, el.Y2));
            }
        }

        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        if (yMax <= yMin)
        {
            yMax = yMin + 1;
        }

        var hueco = _cfg.Numero("SEPARACION_ENTRE_PLANTAS", 10);
        var porFila = (int)_cfg.Numero("PLANTAS_POR_FILA", 100);

        // ==============================================================================
        //  DÓNDE SE PONE EL JUEGO: POR ENCIMA DE LO QUE YA ESTÉ DIBUJADO
        // ==============================================================================
        //  La macro arranca siempre en la Y de OFFSET_Y_INICIAL, y eso está bien cuando se
        //  dibuja en un archivo nuevo. Pero al dibujar sobre un plano que ya tiene cosas
        //  —o al dibujar dos veces— las plantas caían encima de lo anterior.
        //
        //  Así que se mira qué hay ya en el dibujo y el juego se coloca AIRE_SOBRE_LO_
        //  DIBUJADO_M por encima de lo más alto que haya, sea de concreto, de acero o una
        //  anotación.
        //
        //  Y si el dibujo está VACÍO, a la Y de OFFSET_Y_INICIAL —25—, no al origen: el
        //  rótulo de la planta va DEBAJO de las burbujas y de las cotas, así que pegado al
        //  origen se saldría por abajo, a la zona de los negativos.
        // ==============================================================================
        var aire = _cfg.Numero("AIRE_SOBRE_LO_DIBUJADO_M", 5);
        var tope = TopeDeLoDibujado();
        var offsetY = tope is { } t ? t + aire : _cfg.Numero("OFFSET_Y_INICIAL", 25);

        if (porFila < 1)
        {
            porFila = 1;
        }

        var pasoX = (xMax - xMin) + hueco;

        // Y el vertical, con aire para el rótulo de la planta, que va debajo.
        var pasoY = (yMax - yMin) + hueco + (4 * plantas[0].AlturaTexto);

        for (var i = 0; i < plantas.Count; i++)
        {
            var dx = (i % porFila * pasoX) - xMin;
            var dy = (-(i / porFila) * pasoY) - yMin + offsetY;

            var r = Dibujar(plantas[i], dx, dy);

            total.Columnas += r.Columnas;
            total.Trabes += r.Trabes;
            total.Muros += r.Muros;
            total.Losas += r.Losas;
            total.Diagonales += r.Diagonales;
        }

        // ==============================================================================
        //  EL TOPE DEL JUEGO, CALCULADO Y NO PREGUNTADO
        // ==============================================================================
        //  Se apunta aquí la Y más alta a la que llegó el juego de plantas, y se calcula con
        //  aritmética en lugar de preguntárselo a AutoCAD.
        //
        //  Por qué: el corte va SIEMPRE 10 unidades por encima del punto más alto del plano, y
        //  si eso se le pregunta al dibujo —recorriendo las cajas envolventes— hay un caso en
        //  que la pregunta no se puede responder: si esa lectura falla o devuelve vacío, lo
        //  único que queda es el origen, y el corte se iría a Y = 10 mientras las plantas están
        //  a Y = 40. O sea, DEBAJO de la planta, que es justo lo que no puede pasar.
        //
        //  Con este número el corte se coloca aunque COM no responda: se sabe dónde arrancó el
        //  juego —offsetY—, cuánto mide de alto y cuánto le sobresalen por arriba los ejes con
        //  sus burbujas.
        _topeDelJuego = offsetY + (yMax - yMin) + Ejes.SaleEjes() + (2 * Ejes.RadioBurbuja);

        // AL FINAL DE TODO, cuando ya está dibujado el juego entero: las capas de
        // CAPAS_AL_FRENTE encima de lo demás. Antes de terminar no serviría, porque cada
        // planta nueva se dibujaría después.
        TraerCapasAlFrente();

        // Y la capa de las losas apagada, con la de los voladizos encendida.
        ApagarCapasDeLosa();

        if (_volados > 0 || _armadas > 0 || _losacero > 0)
        {
            Nota($"{_volados} paño(s) en voladizo achurados en {_capas.CapaVolado}, " +
                 $"{_armadas} tablero(s) armados y {_losacero} de losacero " +
                 $"({_franjas} franja(s)).");
        }

        if (_tapados > 0)
        {
            Nota($"{_tapados} muro(s) no se dibujaron porque su cadena los tapa " +
                 "(OCULTAR_MURO_BAJO_CADENA). Los muros SIN cadena sí salen, que son los " +
                 "que hay que revisar; su línea de mampostería se dibuja en todos.");
        }

        if (_alFrente > 0)
        {
            // Las de geometría y, encima de ellas, las de texto: son dos pasadas y las dos
            // cuentan en el total.
            var alFrente = _capas.CapasAlFrente()
                .Concat(_capas.CapasDeTextoAlFrente())
                .ToList();

            Nota($"{_alFrente} objeto(s) subidos al frente ({string.Join(" + ", alFrente)}), " +
                 "los rótulos por encima de todo lo demás.");
        }

        return total;
    }

    /// <summary>El rectángulo que envuelve lo dibujado de una planta.</summary>
    /// <remarks>
    /// Cuenta los vértices de los paños y los dos extremos de las barras. Si la planta
    /// llegara vacía devuelve un cuadrado de 1 m: así lo que venga detrás —los ejes, el
    /// rótulo— no tiene que comprobar nada.
    /// </remarks>
    private static (double XMin, double YMin, double XMax, double YMax) Envolvente(PlantaCad p)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        foreach (var el in p.Elementos)
        {
            foreach (var v in el.Vertices)
            {
                xMin = Math.Min(xMin, v.X); xMax = Math.Max(xMax, v.X);
                yMin = Math.Min(yMin, v.Y); yMax = Math.Max(yMax, v.Y);
            }

            xMin = Math.Min(xMin, Math.Min(el.X1, el.X2));
            xMax = Math.Max(xMax, Math.Max(el.X1, el.X2));
            yMin = Math.Min(yMin, Math.Min(el.Y1, el.Y2));
            yMax = Math.Max(yMax, Math.Max(el.Y1, el.Y2));
        }

        if (xMax <= xMin)
        {
            xMin = 0;
            xMax = 1;
        }

        if (yMax <= yMin)
        {
            yMin = 0;
            yMax = 1;
        }

        return (xMin, yMin, xMax, yMax);
    }

    // ==================================================================
    // Cada tipo de elemento
    // ==================================================================

    /// <summary>La columna: su sección real, centrada en el nudo.</summary>
    private bool Columna(ElementoPlanta el, double x0, double y0)
    {
        var b = el.AnchoM;
        var h = el.PeralteM;

        // Sin medidas no se inventa una columna: se avisa y se marca el nudo con una
        // cruz, para que el plano no pierda el punto de apoyo.
        if (b <= LargoMinimo || h <= LargoMinimo)
        {
            _log.Add(
                $"Columna '{el.Etiqueta}' ({el.Seccion}): el modelo no dio sus medidas, " +
                "así que se marcó solo el nudo.");

            var m = 0.10;
            var ok1 = Linea(el.X1 + x0 - m, el.Y1 + y0, el.X1 + x0 + m, el.Y1 + y0,
                            CapaDe(el)) is not null;
            var ok2 = Linea(el.X1 + x0, el.Y1 + y0 - m, el.X1 + x0, el.Y1 + y0 + m,
                            CapaDe(el)) is not null;
            return ok1 || ok2;
        }

        var cx = el.X1 + x0;
        var cy = el.Y1 + y0;

        // PRIMERO, COMO BLOQUE Y RELLENA, que es como lo hace la macro: el bloque se llama
        // como la sección, así que con un BLOCKREPLACE se cambian de golpe todas las
        // columnas de esa sección por el detalle bueno. Si no se puede, se dibuja suelta.
        if (ColumnaComoBloque(el, cx, cy, b, h))
        {
            return true;
        }

        // ---- EL CAMINO SIN BLOQUE: LA SECCIÓN SUELTA, PERO IGUAL DE FIEL -------------
        //  Girada como en el modelo, con SU FORMA y rellena, exactamente como el bloque.
        //  Antes este camino dibujaba un rectángulo derecho y hueco, así que cuando el
        //  bloque no se podía crear el plano salía sin orientación y sin relleno sin decir
        //  por qué.
        var capa = CapaDe(el);

        // La REDONDA no es un polígono: es su circunferencia y, si es tubo, la de dentro.
        if (SeccionEnPlanta.EsRedonda(el.Forma))
        {
            return SeccionRedonda(el, cx, cy, b, capa);
        }

        var local = SeccionEnPlanta.Contorno(el.Forma, b, h, el.PatinM, el.AlmaM, el.ParedM);

        if (local.Length < 6)
        {
            return false;
        }

        var puntos = SeccionEnPlanta.Colocar(local, cx, cy, el.AnguloGrados);

        var pl = PolilineaCerrada(puntos, capa);

        if (pl is null)
        {
            return false;
        }

        // El hueco del cajón: su contorno interior, en la misma capa. Sin él parecería una
        // placa maciza, que es un dato equivocado.
        var hueco = SeccionEnPlanta.Hueco(el.Forma, b, h, el.ParedM);
        object? plHueco = null;

        if (hueco.Length >= 6)
        {
            plHueco = PolilineaCerrada(
                SeccionEnPlanta.Colocar(hueco, cx, cy, el.AnguloGrados), capa);
        }

        if (_cfg.Bandera("RELLENAR_COLUMNAS", true))
        {
            RellenarEnPlanta(pl, plHueco, el, cx, cy, b, h, capa);
        }

        // Las diagonales del recuadro: es la marca de «columna» en un plano estructural, y
        // distingue de un dado o de un hueco a simple vista. Solo en las macizas de
        // concreto: en un perfil de acero la forma ya dice lo que es, y las diagonales
        // taparían el alma y los patines.
        if (!PlanoEstructural.CapasPlano.EsPerfilAcero(el.Forma))
        {
            var esquinas = EsquinasGiradas(cx, cy, b, h, el.AnguloGrados);

            Linea(esquinas[0], esquinas[1], esquinas[4], esquinas[5], capa);
            Linea(esquinas[6], esquinas[7], esquinas[2], esquinas[3], capa);
        }

        return true;
    }

    /// <summary>
    /// La sección <b>redonda</b>: su circunferencia y, si es tubo, la del hueco.
    /// </summary>
    /// <remarks>
    /// Se trata aparte porque no hay polilínea que la describa: una columna circular dibujada
    /// como polígono se ve poligonal al acercar el zoom, y las cotas al centro no cuadran. El
    /// giro no le hace nada —un círculo girado es el mismo círculo—, así que aquí no se aplica.
    /// </remarks>
    private bool SeccionRedonda(ElementoPlanta el, double cx, double cy, double b, string capa)
    {
        var fuera = Circulo(cx, cy, b / 2, capa);

        if (fuera is null)
        {
            return false;
        }

        var ri = SeccionEnPlanta.RadioInterior(el.Forma, b, el.ParedM);

        if (ri > 0)
        {
            Circulo(cx, cy, ri, capa);
        }

        // Un tubo hueco no se rellena de amarillo: se vería macizo, que es justo lo que no
        // es. La circular maciza sí.
        if (_cfg.Bandera("RELLENAR_COLUMNAS", true) && ri <= 0)
        {
            RellenarEnPlanta(fuera, null, el, cx, cy, b, b, capa);
        }

        return true;
    }

    /// <summary>
    /// Las cuatro esquinas de una sección <b>ya girada</b>, en el orden de la polilínea.
    /// </summary>
    /// <remarks>
    /// El giro se hace alrededor del <b>centro</b> de la sección, que es el nudo: es donde
    /// gira de verdad una columna en ETABS. Girar respecto a una esquina la movería de sitio.
    /// </remarks>
    public static double[] EsquinasGiradas(
        double cx, double cy, double b, double h, double grados)
    {
        var a = grados * Math.PI / 180;
        var ca = Math.Cos(a);
        var sa = Math.Sin(a);

        var mb = b / 2;
        var mh = h / 2;

        return new[]
        {
            cx + (-mb * ca) - (-mh * sa), cy + (-mb * sa) + (-mh * ca),
            cx + (mb * ca) - (-mh * sa), cy + (mb * sa) + (-mh * ca),
            cx + (mb * ca) - (mh * sa), cy + (mb * sa) + (mh * ca),
            cx + (-mb * ca) - (mh * sa), cy + (-mb * sa) + (mh * ca)
        };
    }

    /// <summary>
    /// Rellena una sección dibujada <b>en el plano</b>, no dentro de un bloque.
    /// </summary>
    /// <remarks>
    /// Mismo criterio que el relleno del bloque: achurado <c>SOLID</c> del color de
    /// <c>COLOR_RELLENO_BLOQUE</c> —el 2, amarillo— y, si el achurado no se deja, un SOLID de
    /// cuatro puntos, que nunca falla. El color va por objeto para que se vea igual en
    /// cualquiera de las capas de columna.
    /// </remarks>
    private void RellenarEnPlanta(
        object contorno, object? hueco, ElementoPlanta el,
        double cx, double cy, double b, double h, string capa)
    {
        var color = ColorDelRelleno();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic ht = _ms.AddHatch(0, "SOLID", true, 0);
                ht.AppendOuterLoop(new[] { contorno });

                // El hueco del cajón, como lazo INTERIOR: así el achurado deja el hueco
                // vacío en lugar de pintarlo, que es lo que hace que se vea que es un tubo.
                if (hueco is not null)
                {
                    try
                    {
                        ht.AppendInnerLoop(new[] { hueco });
                    }
                    catch (Exception)
                    {
                        // Sin el lazo interior sale macizo: se avisa más abajo.
                    }
                }

                ht.Evaluate();
                ht.Layer = capa;
                ht.Color = color;
            });

            return;
        }
        catch (Exception)
        {
            // Al respaldo: las piezas macizas de las que está hecha la sección.
        }

        // EL RESPALDO. Un SOLID solo cubre un cuadrilátero convexo, y una I no lo es, así
        // que la sección se rellena con las PIEZAS de las que está hecha: los dos patines y
        // el alma, las cuatro paredes del cajón, las dos alas del ángulo. Es lo que salva el
        // relleno cuando el achurado no se deja crear.
        var piezas = SeccionEnPlanta.RectangulosDeRelleno(
            el.Forma, b, h, el.PatinM, el.AlmaM, el.ParedM);

        if (piezas.Count == 0)
        {
            Nota($"La sección '{el.Seccion}' quedó con su contorno pero sin relleno: " +
                 "achúrala con SOLID si la quieres rellena.");
            return;
        }

        foreach (var r in piezas)
        {
            SolidoGirado(r, cx, cy, el.AnguloGrados, capa, color);
        }
    }

    /// <summary>El color del relleno de la hoja, acotado: el 2, amarillo, por omisión.</summary>
    private int ColorDelRelleno()
    {
        var color = (int)_cfg.Numero("COLOR_RELLENO_BLOQUE", 2);

        return color is <= 0 or > 255 ? 2 : color;
    }

    /// <summary>
    /// Un <c>SOLID</c> a partir de un rectángulo en coordenadas de la sección, ya girado.
    /// </summary>
    /// <remarks>
    /// Los cuatro puntos de un SOLID <b>no van en orden alrededor</b>: el tercero y el cuarto
    /// van cruzados. En orden circular sale un moño en lugar de un rectángulo, y es un error
    /// que solo se ve al imprimir.
    /// </remarks>
    private void SolidoGirado(
        double[] rect, double cx, double cy, double grados, string capa, int color)
    {
        if (rect.Length < 4)
        {
            return;
        }

        var p = SeccionEnPlanta.Colocar(
            new[]
            {
                rect[0], rect[1],
                rect[2], rect[1],
                rect[2], rect[3],
                rect[0], rect[3]
            },
            cx, cy, grados);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic sol = _ms.AddSolid(
                    new[] { p[0], p[1], 0d },
                    new[] { p[2], p[3], 0d },
                    new[] { p[6], p[7], 0d },
                    new[] { p[4], p[5], 0d });

                sol.Layer = capa;
                sol.Color = color;
            });
        }
        catch (Exception ex)
        {
            Fallo("Rellenar la sección de una columna", ex);
        }
    }

    /// <summary>
    /// Una barra en planta —trabe o muro— por sus dos paños.
    /// </summary>
    /// <remarks>
    /// Los paños son el eje desplazado media anchura hacia cada lado, en la dirección
    /// <b>perpendicular</b> al eje. Se calcula normalizando el vector del eje y
    /// girándolo 90°: <c>(-dy, dx) / largo</c>. Así funciona con la barra en
    /// cualquier dirección, no solo en las ortogonales, que es lo que hace falta en
    /// una planta con ejes inclinados.
    /// </remarks>
    private bool Barra(
        ElementoPlanta el, double x0, double y0, string capa,
        double ancho, bool conEje, PanoDeApoyo.Tramo? tramo = null,
        (string Tipo, double Escala)? tipoLinea = null)
    {
        // El tramo YA llevado a los paños, si quien llama lo calculó. Sin él, el elemento tal
        // como viene del modelo: de eje a eje.
        var t = tramo ?? new PanoDeApoyo.Tramo(el.X1, el.Y1, el.X2, el.Y2);

        var dx = t.X2 - t.X1;
        var dy = t.Y2 - t.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            _log.Add($"'{el.Etiqueta}': largo nulo en planta, no se dibujó.");
            return false;
        }

        var ax = t.X1 + x0;
        var ay = t.Y1 + y0;
        var bx = t.X2 + x0;
        var by = t.Y2 + y0;

        // Normal unitaria al eje
        var nx = -dy / largo * (ancho / 2);
        var ny = dx / largo * (ancho / 2);

        var p1 = Linea(ax + nx, ay + ny, bx + nx, by + ny, capa);
        var p2 = Linea(ax - nx, ay - ny, bx - nx, by - ny, capa);

        if (p1 is null && p2 is null)
        {
            return false;
        }

        // LA CADENA SIN MURO COMPLETO VA CON OTRA LÍNEA, por objeto y no por capa: en la
        // misma capa E-CADENA conviven las que llevan muro —continuas— y las que no.
        if (tipoLinea is { } lt)
        {
            PonerTipoDeLinea(p1, lt.Tipo, lt.Escala);
            PonerTipoDeLinea(p2, lt.Tipo, lt.Escala);
        }

        // El eje, en su capa aparte: es lo que se acota y lo que se congela cuando
        // el plano se llena. Va a trazos, como marca la convención.
        if (conEje)
        {
            var eje = Linea(ax, ay, bx, by, CapaEjes);
            LineaATrazos(eje);
        }

        return true;
    }

    /// <summary>La leyenda «MC» centrada dentro del muro y girada con él.</summary>
    /// <remarks>
    /// <para>
    /// Va en su propio método porque el muro se dibuja con <see cref="Barra"/> —que es lo que se
    /// pidió, dos líneas y no un contorno cerrado— y <c>Barra</c> es la primitiva que usan todos
    /// los elementos de barra: no puede saber nada de leyendas.
    /// </para>
    /// <para>
    /// Se centra en el <b>medio del tramo ya recortado</b> a los paños de los castillos, no en el
    /// medio del eje del modelo: si el muro se recortó por un lado, el centro del eje ya no es el
    /// centro de lo que se ve.
    /// </para>
    /// </remarks>
    private void LeyendaDeMuro(
        ElementoPlanta el, double x0, double y0, string capa, PanoDeApoyo.Tramo? tramo,
        double espesor = 0)
    {
        var leyenda = _cfg.Texto("MURO_CONCRETO_LEYENDA", "MC").Trim();

        if (leyenda.Length == 0)
        {
            return;
        }

        var t = tramo ?? new PanoDeApoyo.Tramo(el.X1, el.Y1, el.X2, el.Y2);

        var dx = t.X2 - t.X1;
        var dy = t.Y2 - t.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            return;
        }

        var altura = _cfg.Numero("MURO_CONCRETO_LEYENDA_ALTURA", 0.12);

        // ===== LA ALTURA SE AJUSTA AL GROSOR DEL MURO =====
        //
        // El texto va DENTRO del muro, entre sus dos caras, así que no puede ser más alto que el
        // grosor. Con 0.12 fijo y un muro de 15 cm, el «MC» sobresalía por las dos caras y se
        // comía las líneas que se supone que acompaña.
        //
        // Se deja al 70% del grosor: entra con aire y sigue siendo legible.
        if (espesor > 0)
        {
            altura = Math.Min(altura, espesor * 0.7);
        }

        // No se rotula donde no cabe: un «MC» más largo que el muro se sale por los dos lados y se
        // lee peor que no ponerlo. Se mide contra el LARGO, que es la dirección en la que crece el
        // texto una vez girado con el muro.
        if (altura <= 0 || largo < altura * leyenda.Length)
        {
            _sinLeyendaMc++;
            return;
        }

        // ===== SIN FONDO OPACO =====
        //
        // Lo llevaba, y era lo que CORTABA LAS DOS LÍNEAS del muro: el fondo del MTEXT tapa lo que
        // hay detrás, y en el plano se veían las líneas interrumpidas justo en el «MC».
        //
        // No hace falta: el texto va entre las dos caras del muro, y con la altura ajustada al
        // grosor no llega a tocarlas. Y como la leyenda se dibuja ANTES que las líneas, si alguna
        // vez se rozaran, mandan las líneas, que es lo que se pidió.
        //
        // Anclaje 5 es MiddleCenter, así que el punto ES el centro del texto.
        Mtexto(((t.X1 + t.X2) / 2) + x0, ((t.Y1 + t.Y2) / 2) + y0, leyenda, altura, capa,
               AnguloLegible(dx, dy), EstiloSecciones, conFondo: false);
    }

    /// <summary>
    /// La losa: su contorno, su <b>armado</b> y —si está volada— su <b>hatch</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo primero es decidir si el paño está <b>volado</b>, porque de eso depende todo lo
    /// demás: un voladizo lleva su hatch y va en la capa <c>E-VOLADO</c> —la que se queda
    /// encendida cuando <c>E-LOSA</c> se apaga—, y un tablero apoyado lleva su parrilla de
    /// armado en <c>E-ARMADO LOSA</c>.
    /// </para>
    /// <para>
    /// Y el contorno se dibuja <b>solo por fuera del muro y de la cadena</b>. Donde la losa
    /// apoya, su paño y el del muro son la misma línea, así que dibujarlo dejaría una raya en
    /// medio del muro que se lee como una junta que no existe. Para el hatch sí se usa el
    /// contorno completo —un achurado necesita un contorno cerrado—, y la polilínea que sirve
    /// de molde se borra después.
    /// </para>
    /// </remarks>
    private bool Losa(ElementoPlanta el, double x0, double y0,
                      IReadOnlyList<ElementoPlanta> huellas)
    {
        if (el.Vertices.Count < 3)
        {
            _log.Add(
                $"Losa '{el.Etiqueta}': llegó con {el.Vertices.Count} vértice(s), " +
                "hacen falta 3 para cerrar un paño.");
            return false;
        }

        var pts = new double[el.Vertices.Count * 2];

        for (var i = 0; i < el.Vertices.Count; i++)
        {
            pts[2 * i] = el.Vertices[i].X + x0;
            pts[(2 * i) + 1] = el.Vertices[i].Y + y0;
        }

        // ==============================================================================
        //  ¿ES UN VOLADIZO? LO DICE SU NOTA, NO LA GEOMETRÍA
        // ==============================================================================
        //  Se pidió tal cual: el ANSI37 va SOLO en las losas cuya etiqueta de nota diga
        //  VOLADO. Y es lo correcto en un modelo real: el ingeniero sabe cuál es el volado y
        //  lo escribe en la propiedad, mientras que contar lados apoyados se equivoca en
        //  cuanto una cadena viene partida en el modelo, y entonces el achurado aparece donde
        //  no va y falta donde sí.
        //
        //  La cuenta por geometría se queda disponible con VOLADO_POR_NOTA en NO.
        var volada = LosaVolada(el, huellas);

        var capa = volada ? _capas.CapaVolado : CapaDe(el);

        // ==============================================================================
        //  EL VOLADIZO: EL ACHURADO EN TODO EL PAÑO, PERO LA LÍNEA SOLO POR FUERA
        // ==============================================================================
        //  Son dos cosas distintas y por eso van con dos objetos distintos:
        //
        //   * EL MOLDE es la polilínea cerrada del paño COMPLETO. Un achurado necesita un
        //     contorno cerrado para nacer, así que el molde tiene que dar la vuelta entera,
        //     incluso por donde la losa se apoya.
        //
        //   * EL CONTORNO que se queda dibujado es SOLO EL EXTERIOR: se pidió que esa línea
        //     no toque la cadena ni el muro. Y es lo correcto en el plano: por donde la losa
        //     apoya, su paño y el de la cadena son la MISMA línea, así que dibujarla encima
        //     deja una raya en medio de la cadena que se lee como una junta que no existe.
        //     El borde libre —lo que en obra se cimbra— es el único que hay que ver.
        //
        //  Así que se achura el molde y después el molde SE BORRA, quedando el achurado y los
        //  tramos de contorno que caen fuera de los muros. Por eso el hatch se crea no
        //  asociativo: uno asociativo se rehace al borrar su contorno.
        if (volada)
        {
            // ==========================================================================
            //  EL MOLDE, METIDO HASTA EL PAÑO DEL MURO
            // ==========================================================================
            //  En el modelo la losa llega al EJE del muro, porque ahí están los nudos. Pero el
            //  concreto de la losa no llega al eje: llega al PAÑO, y medio espesor antes ya es
            //  muro. Achurando con el contorno del modelo, el rayado se metía por dentro de la
            //  cadena, que es lo que se pidió quitar.
            var alPano = _cfg.Bandera("LOSA_HATCH_AL_PANO", true) && huellas.Count > 0
                ? PanoDeLosa.AlPano(el.Vertices, huellas)
                : el.Vertices;

            var ptsMolde = new double[alPano.Count * 2];

            for (var i = 0; i < alPano.Count; i++)
            {
                ptsMolde[2 * i] = alPano[i].X + x0;
                ptsMolde[(2 * i) + 1] = alPano[i].Y + y0;
            }

            var molde = PolilineaCerrada(ptsMolde, capa);
            var conHatch = false;

            if (_cfg.Bandera("LOSA_HATCH", true))
            {
                // Se le pasan los vértices para el último recurso: si NINGUNA de las tres
                // vías del hatch funciona, el voladizo se raya a mano antes que quedarse sin
                // marcar —y se avisa de que eso no es un hatch—.
                conHatch = HatchSobre(molde, capa,
                                      _cfg.Texto("LOSA_HATCH_PATRON", "ANSI37"),
                                      EscalaDelHatchDeLosa(),
                                      _cfg.Numero("LOSA_HATCH_ANGULO", 45),
                                      alPano, x0, y0);
            }

            // ---- Y LA LÍNEA, SOLO EL CONTORNO EXTERIOR -------------------------------
            var soloFuera = _cfg.Bandera("VOLADO_CONTORNO_FUERA_DE_MUROS", true)
                            && huellas.Count > 0;

            var contorno = molde;

            if (soloFuera)
            {
                var tramos = 0;

                // ======================================================================
                //  DOS VOLADIZOS PEGADOS SON UN SOLO PAÑO
                // ======================================================================
                //  Se pidió: que cuando haya varias losas de volado juntas se vea UN
                //  perímetro y no las divisiones entre ellas. Esa raya del medio es la orilla
                //  que las dos losas comparten, y en la obra NO EXISTE: el concreto es
                //  continuo, y quien lee el plano entiende una junta que nadie va a construir.
                //
                //  Casi siempre son una losa partida en dos por un eje, porque en el modelo
                //  hace falta el nudo. El plano no tiene por qué heredar esa partición.
                var vecinas = _cfg.Bandera("VOLADO_SIN_DIVISIONES", true)
                    ? OtrosVolados(el)
                    : new List<IReadOnlyList<(double X, double Y)>>();

                foreach (var lado in LosaEnPlanta.Lados(el.Vertices))
                {
                    foreach (var t in LosaEnPlanta.TramosFuera(lado, huellas))
                    {
                        // El tramo que comparte con otro voladizo no se dibuja: es interior
                        // al paño de los dos juntos.
                        if (vecinas.Count > 0 && PanoDeLosa.ContornoCompartido(t, vecinas))
                        {
                            continue;
                        }

                        if (Linea(t.X1 + x0, t.Y1 + y0, t.X2 + x0, t.Y2 + y0, capa) is not null)
                        {
                            tramos++;
                        }
                    }
                }

                // El molde ya cumplió su papel. Se borra SIEMPRE que se hayan dibujado los
                // tramos de fuera; si no se dibujó ninguno —un paño metido entre dos cadenas
                // pegadas— se deja el molde, porque si no el achurado se quedaría sin ningún
                // borde y el paño no se entendería.
                //
                //  Y NO SE BORRA si el achurado se quedó ATADO a él. Es el caso raro en que
                //  AutoCAD crea el hatch asociativo y no deja quitarle la asociatividad:
                //  borrar el molde se llevaría el achurado por delante, y entonces el rótulo
                //  aparece sobre una losa sin achurar. Antes que eso, una línea de más por
                //  dentro del muro.
                if (tramos > 0 && !HatchAtadoAlMolde)
                {
                    Borrar(molde);
                    contorno = null;
                }

                // ESTO ES UNA NOTA, NO UN FALLO. Iba a _log, que es la lista de lo que se
                // toleró, así que el diálogo del final decía «hubo 2 avisos que se
                // toleraron» y los enseñaba con su triángulo amarillo... para contar que
                // todo había ido BIEN —«achurado puesto»—. Un aviso que no avisa de nada
                // enseña a desconfiar de los avisos, y el día que falte algo de verdad no se
                // va a creer. Va a las notas, que es donde se cuenta lo que se hizo.
                Nota($"Volado '{el.Etiqueta}': {tramos} tramo(s) de contorno por fuera de " +
                     $"muros y cadenas, achurado {(conHatch ? "puesto" : "NO puesto")}.");
            }

            // ==========================================================================
            //  Y SE DICE DE DÓNDE SALIÓ EL «VOLADO», UNA VEZ POR PROPIEDAD
            // ==========================================================================
            //  Porque cuando el achurado aparece donde no se espera, la causa está SIEMPRE
            //  en el modelo y no en el dibujo, y sin esta nota no hay manera de verlo: las
            //  NOTAS SON DE LA PROPIEDAD, no del paño. Si el voladizo y el entrepiso
            //  comparten la misma propiedad de losa —la misma sección— y en sus notas dice
            //  VOLADO, entonces TODOS los paños de esa sección son voladizo para el
            //  programa, y todos salen achurados. La solución no está aquí: hay que darle al
            //  volado su propia propiedad en ETABS.
            //
            //  Con la nota se lee de un golpe qué sección se tomó por voladizo y por qué
            //  palabra, así que se ve al momento si el que sobra es un paño o toda una
            //  sección.
            AvisarDelVolado(el);

            _volados++;

            // Se dio por dibujado si quedó el achurado o si quedó alguna línea: con el
            // contorno recortado el molde se borra, así que ya no sirve para comprobarlo.
            return conHatch || contorno is not null || soloFuera;
        }

        // ---- LA LOSACERO: FRANJAS CON HATCH FLEX, NO ARMADO DE CONCRETO ---------------
        //  Donde hay vigas de acero la losa es LOSACERO, y no lleva parrilla ni bayoneta:
        //  lleva las franjas de la lámina y su rótulo con el calibre.
        if (_cfg.Bandera("LOSACERO_FRANJAS", true) &&
            LosaEnPlanta.DiceLosacero(
                el.Etiqueta, el.Notas, el.Seccion,
                _cfg.Texto("LOSACERO_PALABRAS", "LOSACERO,DECK,STEEL DECK,LAMINA ACANALADA")))
        {
            return Losacero(el, x0, y0);
        }

        // ---- EL CONTORNO, SOLO POR FUERA DEL MURO Y DE LA CADENA ---------------------
        var fuera = _cfg.Bandera("LOSA_CONTORNO_FUERA_DE_MUROS", true) && huellas.Count > 0;

        var algo = false;

        if (fuera)
        {
            // ==========================================================================
            //  LA RAYA DEL MESH NO SE DIBUJA
            // ==========================================================================
            //  Los pedazos de un mismo tablero comparten orilla, y esa orilla EN LA OBRA NO
            //  EXISTE: el concreto es continuo y ahí no hay junta ni cimbra. Es la misma raya
            //  que ya se quita entre dos voladizos pegados, y por el mismo motivo: quien lee el
            //  plano entiende una junta que nadie va a construir.
            //
            //  Y no se quita a lo bruto: se quita la orilla COMPARTIDA CON SU PROPIO TABLERO. La
            //  que da a otro tablero —la que tiene un apoyo debajo— se dibuja, porque ahí sí
            //  termina el paño.
            var mismoTablero = _cfg.Bandera("LOSA_TABLERO_SIN_LINEA_INTERIOR", true)
                ? OtrosDelTablero(el)
                : new List<IReadOnlyList<(double X, double Y)>>();

            foreach (var lado in LosaEnPlanta.Lados(el.Vertices))
            {
                foreach (var t in LosaEnPlanta.TramosFuera(lado, huellas))
                {
                    if (mismoTablero.Count > 0 && PanoDeLosa.ContornoCompartido(t, mismoTablero))
                    {
                        continue;
                    }

                    algo |= Linea(t.X1 + x0, t.Y1 + y0, t.X2 + x0, t.Y2 + y0, capa) is not null;
                }
            }

            // Un paño que queda ENTERO por dentro de los muros no tiene contorno que
            // dibujar, y eso no es un fallo: es una losa entre dos cadenas pegadas.
            if (!algo)
            {
                algo = true;
            }
        }
        else
        {
            algo = PolilineaCerrada(pts, capa) is not null;
        }

        // ---- Y EL ARMADO, en el tablero apoyado --------------------------------------
        ArmadoDeLosa(el, x0, y0, huellas);

        return algo;
    }

    // =================================================================================
    //  LA ESCALERA: PURO CONTORNO
    // =================================================================================

    /// <summary>Dibuja el <b>contorno</b> de las escaleras apartadas, y nada más.</summary>
    /// <remarks>
    /// <para>
    /// Se pidió: «solo dibuja el contorno de las escaleras, puro contorno nada más». Aquí eso es
    /// literal —una polilínea cerrada por escalera y se acabó—, y lo es porque las escaleras se
    /// <b>apartaron de la lista</b> antes de empezar: el achurado, la parrilla, el rótulo, la
    /// unión de tableros y la línea doble del muro nunca las vieron. El razonamiento completo
    /// está en <see cref="PlanoEstructural.EscaleraEnPlanta"/>.
    /// </para>
    /// <para>
    /// Va en su <b>propia capa</b>, <c>E-ESCALERA</c>, y no en la de la losa. No es orden: la
    /// capa de la losa se deja <b>apagada</b> al terminar —<c>APAGAR_CAPA_LOSA</c>, porque el
    /// contorno de todos los paños llena el plano—, así que metiendo ahí la escalera se apagaría
    /// con ella y no se vería justo lo único que se pidió dibujar.
    /// </para>
    /// </remarks>
    private int Escaleras(double x0, double y0, IReadOnlyList<ElementoPlanta> huellas)
    {
        if (_escalerasDeLaPlanta.Count == 0)
        {
            return 0;
        }

        var capa = _capas.CapaEscalera;
        var dibujadas = 0;

        // ==============================================================================
        //  LA LÍNEA DE LA ESCALERA MUERE EN EL PAÑO DEL MURO, NO EN SU EJE
        // ==============================================================================
        //  Se pidió, y es lo mismo que ya se hace con el molde del achurado de la losa: en el
        //  modelo la escalera llega al EJE del muro, porque ahí están los nudos, pero el
        //  concreto no llega al eje —medio espesor antes ya es muro—. Dibujando el contorno del
        //  modelo, la línea de la escalera se mete por dentro de la cadena y se lee como una
        //  junta que no existe.
        //
        //  Lo hace PanoDeLosa.AlPano, que mete cada lado SOLO por el trozo que tiene muro
        //  debajo. Y aquí importa que sea por tramos: el rellano de una escalera apoya en el
        //  muro por un pedazo de su lado y da al aire por el resto.
        var alPano = _cfg.Bandera("ESCALERA_AL_PANO", true) && huellas.Count > 0;

        foreach (var el in _escalerasDeLaPlanta)
        {
            var contorno = PlanoEstructural.EscaleraEnPlanta.Contorno(el);

            if (contorno.Count < 3)
            {
                continue;
            }

            if (alPano)
            {
                contorno = PanoDeLosa.AlPano(contorno, huellas);
            }

            if (contorno.Count < 3)
            {
                continue;
            }

            var pts = new double[contorno.Count * 2];

            for (var i = 0; i < contorno.Count; i++)
            {
                pts[2 * i] = contorno[i].X + x0;
                pts[(2 * i) + 1] = contorno[i].Y + y0;
            }

            if (PolilineaCerrada(pts, capa) is not null)
            {
                dibujadas++;
            }
        }

        // Se dice CUÁNTAS y POR QUÉ PALABRA. Una losa que se queda en puro contorno sin avisar
        // es un misterio; sabiendo que fue por decir «RAMPA» se corrige el modelo o se quita la
        // palabra de la lista.
        var palabras = _cfg.Texto(
            "PALABRAS_ESCALERA", "ESCALERA,ESCAL,STAIR,RAMPA,RAMP,DESCANSO");

        var porQue = string.Join(", ", _escalerasDeLaPlanta
            .Select(e => PlanoEstructural.EscaleraEnPlanta.PalabraEscalera(
                e.Etiqueta, e.Notas, e.Seccion, palabras))
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()})"));

        var deMuro = _escalerasDeLaPlanta.Count(e => e.Clase == ClasePlanta.Muro);

        Nota($"{dibujadas} escalera(s) se dibujaron SOLO de contorno en la capa {capa} " +
             $"—sin achurado, sin armado y sin rótulo—, por decir {porQue} en su nota, " +
             $"etiqueta o sección." +
             (deMuro > 0 ? $" {deMuro} de ellas venían modeladas como muro." : string.Empty) +
             " Se apaga con IGNORAR_LOSA_ESCALERA en NO.");

        return dibujadas;
    }

    // =================================================================================
    //  EL VACÍO: DONDE NO HAY PISO
    // =================================================================================

    /// <summary>
    /// Marca los <b>vacíos</b> de la planta: contorno a trazos y una <b>cruz</b> dentro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió: «delimita los vacíos con líneas punteadas… de los vértices de donde se forma el
    /// vacío, de ahí salen las líneas para formar la cruz, que hay vacío, o sea no hay piso». Es
    /// la convención de siempre en un plano de losas: el hueco de la escalera, el del elevador o
    /// el del ducto van con su contorno a trazos y una X dentro.
    /// </para>
    /// <para>
    /// Dónde salen los vacíos está razonado en <see cref="PlanoEstructural.VacioEnLosa"/>: no
    /// vienen del modelo, se deducen buscando los <b>agujeros de la unión de los paños</b>. Aquí
    /// solo se pasa a líneas lo que esa cuenta devuelve.
    /// </para>
    /// <para>
    /// <b>Va DESPUÉS de dibujar las losas</b>, y a propósito: en AutoCAD el orden de creación es
    /// el orden de dibujo, así que la línea del vacío queda por encima del achurado de los paños
    /// vecinos y no se pierde debajo.
    /// </para>
    /// <para>
    /// Y el <b>LinetypeScale</b> no es un adorno: el plano va en <b>metros</b>, y el patrón del
    /// DASHDOT mide media unidad de dibujo. A escala 1, medio metro de raya seguida de medio de
    /// espacio en un hueco de 1.20 m se ve como una línea <b>continua</b>. De ahí
    /// <c>VACIO_LTSCALE</c>, con la misma cuenta que la cadena sin muro: 0 = automático = 0.01.
    /// </para>
    /// </remarks>
    private int Vacios(PlantaCad p, double x0, double y0)
    {
        if (!_cfg.Bandera("DIBUJAR_VACIOS", true))
        {
            return 0;
        }

        // Los paños YA desplazados a su sitio en el plano: así lo que devuelve la cuenta se
        // dibuja tal cual, sin volver a sumar el origen en cada punto y cada trazo.
        var contornos = p.Elementos
            .Where(e => e.Clase == ClasePlanta.Losa && e.Vertices.Count >= 3)
            .Select(e => (IReadOnlyList<(double X, double Y)>)e.Vertices)
            .ToList();

        // LA ESCALERA CUENTA COMO PISO. Se apartó de la lista de elementos al empezar, así que
        // hay que volver a meterla AQUÍ: si no, su hueco saldría marcado con la cruz de «no hay
        // piso», y por una escalera sí se pasa. Es el sitio exacto donde la escalera vuelve a
        // ser suelo sin volver a ser un tablero de losa.
        foreach (var el in _escalerasDeLaPlanta)
        {
            var c = PlanoEstructural.EscaleraEnPlanta.Contorno(el);

            if (c.Count >= 3)
            {
                contornos.Add(c);
            }
        }

        var panos = contornos
            .Select(c => (IReadOnlyList<(double X, double Y)>)c
                .Select(v => (v.X + x0, v.Y + y0))
                .ToList())
            .ToList();

        if (panos.Count == 0)
        {
            return 0;
        }

        var tol = _cfg.Numero("VACIO_TOL_CM", 5) / 100;

        // La retícula la ponen los vértices de los paños, así que un nivel con el mallado muy
        // fino puede pedir una gigantesca. Se avisa ANTES de intentarlo, porque si no el usuario
        // solo vería que no salió ningún vacío y no sabría que hay algo que subir.
        var celdas = PlanoEstructural.VacioEnLosa.CeldasQueHacenFalta(panos, tol);

        if (celdas > PlanoEstructural.VacioEnLosa.MaximoDeCeldas)
        {
            Nota($"No se buscaron los vacíos de este nivel: sus {panos.Count} paños piden una " +
                 $"retícula de {celdas:N0} celdas y el tope está en " +
                 $"{PlanoEstructural.VacioEnLosa.MaximoDeCeldas:N0}. El mallado del modelo es " +
                 "muy fino; sube VACIO_TOL_CM —junta los bordes casi iguales— y vuelve a " +
                 "dibujar.");

            return 0;
        }

        List<PlanoEstructural.VacioEnLosa.Vacio> vacios;

        try
        {
            vacios = PlanoEstructural.VacioEnLosa.Detectar(
                panos, tol, _cfg.Numero("VACIO_AREA_MIN_M2", 0.10));
        }
        catch (Exception ex)
        {
            // Un fallo buscando los huecos no puede tumbar la planta entera: el resto del
            // plano ya está dibujado y es bueno.
            Fallo("Buscar los vacíos de la losa", ex);

            return 0;
        }

        if (vacios.Count == 0)
        {
            return 0;
        }

        var capa = _capas.CapaVacio;
        var tipo = _cfg.Texto("LINETYPE_VACIO", "DASHDOT");

        var escalaLt = _cfg.Numero("VACIO_LTSCALE", 0);

        if (escalaLt <= 0)
        {
            escalaLt = 0.01;
        }

        var conCruz = _cfg.Bandera("VACIO_CRUZ", true);

        foreach (var vacio in vacios)
        {
            foreach (var contorno in vacio.Contornos)
            {
                var pts = new double[contorno.Count * 2];

                for (var i = 0; i < contorno.Count; i++)
                {
                    pts[2 * i] = contorno[i].X;
                    pts[(2 * i) + 1] = contorno[i].Y;
                }

                PonerTipoDeLinea(PolilineaCerrada(pts, capa), tipo, escalaLt);
            }

            if (!conCruz)
            {
                continue;
            }

            foreach (var (xa, ya, xb, yb) in vacio.Cruz)
            {
                PonerTipoDeLinea(Linea(xa, ya, xb, yb, capa), tipo, escalaLt);
            }
        }

        var area = vacios.Sum(v => v.Area);

        Nota($"{vacios.Count} vacío(s) marcados —{area:0.00} m² sin losa— con su contorno a " +
             $"trazos y su cruz, en la capa {capa}. Se apaga con DIBUJAR_VACIOS en NO.");

        return vacios.Count;
    }

    /// <summary>
    /// La <b>losacero</b>: sus franjas con hatch <c>FLEX</c> y su rótulo con el calibre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>FranjasLosacero</c> + <c>RotuloLosacero</c>. Una losacero <b>no lleva armado de
    /// concreto</b> —ni parrilla ni bayoneta—: lleva la lámina, y en el plano eso son franjas
    /// en el sentido corto con el patrón <c>FLEX</c>, más el rótulo <c>LOSACERO IMSA CALIBRE
    /// 24</c>, donde el calibre sale de las notas de la sección de ETABS.
    /// </para>
    /// <para>
    /// Todo va en la capa <c>E-LOSACERO</c>, y el rótulo en la de textos con fondo opaco para
    /// que tape el achurado.
    /// </para>
    /// </remarks>
    private bool Losacero(ElementoPlanta el, double x0, double y0)
    {
        var capa = _capas.Prefijo + "LOSACERO";

        var franjas = LosaEnPlanta.Franjas(
            el.Vertices,
            _cfg.Numero("LOSACERO_FRANJA_ANCHO_M", 0.15),
            _cfg.Numero("LOSACERO_FRANJA_SEP_M", 0.8),
            _cfg.Numero("LOSACERO_FRANJA_LARGO_MIN_M", 0.3));

        var ancho = _cfg.Numero("LOSACERO_FRANJA_ANCHO_M", 0.15);
        var patron = _cfg.Texto("LOSACERO_HATCH_PATRON", "FLEX");
        var escala = _cfg.Numero("LOSACERO_HATCH_ESCALA", 0.02);
        var dejarContorno = _cfg.Bandera("LOSACERO_FRANJA_CONTORNO", true);

        var hechas = 0;

        foreach (var f in franjas)
        {
            var horizontal = Math.Abs(f.Y2 - f.Y1) < LargoMinimo;

            // El rectángulo de la franja, del ancho de la lámina.
            var pts = horizontal
                ? new[]
                {
                    f.X1 + x0, f.Y1 + y0 - (ancho / 2),
                    f.X2 + x0, f.Y2 + y0 - (ancho / 2),
                    f.X2 + x0, f.Y2 + y0 + (ancho / 2),
                    f.X1 + x0, f.Y1 + y0 + (ancho / 2)
                }
                : new[]
                {
                    f.X1 + x0 - (ancho / 2), f.Y1 + y0,
                    f.X1 + x0 + (ancho / 2), f.Y1 + y0,
                    f.X2 + x0 + (ancho / 2), f.Y2 + y0,
                    f.X2 + x0 - (ancho / 2), f.Y2 + y0
                };

            var molde = PolilineaCerrada(pts, capa);

            if (molde is null)
            {
                continue;
            }

            // El achurado va con el ángulo de la franja: 0 si corre en X, 90 si en Y.
            HatchSobre(molde, capa, patron, escala, horizontal ? 0 : 90);

            if (!dejarContorno)
            {
                Borrar(molde);
            }

            hechas++;
        }

        _losacero++;
        _franjas += hechas;

        // ---- EL RÓTULO, con el calibre de las notas ---------------------------------
        if (_cfg.Bandera("LOSACERO_TEXTO", true))
        {
            var calibre = LosaEnPlanta.Calibre(el.Notas);

            if (calibre.Length == 0)
            {
                calibre = LosaEnPlanta.Calibre(el.Seccion + " " + el.Etiqueta);
            }

            if (calibre.Length == 0)
            {
                calibre = _cfg.Texto("LOSACERO_CALIBRE_OMISION", "24");
            }

            var texto = _cfg.Texto("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C")
                .Replace("%C", calibre)
                .Replace("%S", el.Seccion)
                .Replace("%E", (el.AnchoM * 100).ToString(
                    "0.#", System.Globalization.CultureInfo.InvariantCulture));

            var cx = ((el.Vertices.Min(v => v.X) + el.Vertices.Max(v => v.X)) / 2) + x0;
            var cy = ((el.Vertices.Min(v => v.Y) + el.Vertices.Max(v => v.Y)) / 2) + y0;

            var alt = _cfg.Numero("LOSACERO_TEXTO_ALTURA", 0);

            Mtexto(cx, cy, texto, alt > 0 ? alt : AlturaLosas(0.072), CapaTextos, 0,
                   EstiloLosas, _cfg.Bandera("LOSACERO_TEXTO_FONDO", true));
        }

        return hechas > 0;
    }

    /// <summary>Cuántas losaceros y cuántas franjas se dibujaron, para el resumen.</summary>
    private int _losacero;

    private int _franjas;

    /// <summary>Cuántos paños salieron volados, para el resumen.</summary>
    private int _volados;

    /// <summary>
    /// La Y más alta a la que llegó el juego de plantas, para colocar el corte encima.
    /// </summary>
    /// <remarks>
    /// Calculada, no preguntada al dibujo: es lo que garantiza que el corte quede arriba
    /// incluso si la lectura de las cajas envolventes falla. Nulo si aún no se ha dibujado
    /// ninguna planta.
    /// </remarks>
    private double? _topeDelJuego;

    /// <summary>Secciones ya avisadas: la nota del voladizo va una vez, no una por paño.</summary>
    private readonly HashSet<string> _voladosAvisados = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Los contornos de los <b>otros</b> voladizos de la planta que se está dibujando.
    /// </summary>
    /// <remarks>
    /// Hacen falta para no dibujar la orilla que un voladizo comparte con otro. Se guardan al
    /// empezar la planta —no se van buscando por cada paño— porque hay que conocerlos TODOS
    /// antes de dibujar el primero: si se fueran descubriendo por el camino, la primera losa
    /// dibujaría su raya y la segunda ya no, y quedaría media junta.
    /// </remarks>
    private readonly List<(string Clave, IReadOnlyList<(double X, double Y)> Vertices)>
        _voladosDeLaPlanta = new();

    /// <summary>
    /// Las <b>escaleras</b> apartadas de la planta que se está dibujando.
    /// </summary>
    /// <remarks>
    /// Se sacan de la lista de elementos lo primero de todo —el razonamiento está en
    /// <see cref="PlanoEstructural.EscaleraEnPlanta"/>— y se guardan aquí para dos cosas: dibujar
    /// su contorno, que es lo único que se pidió de ellas, y contar su superficie como
    /// <b>piso</b> al buscar los vacíos. Sin lo segundo, el hueco de la escalera saldría marcado
    /// con la cruz de «no hay piso», y por la escalera sí se pasa.
    /// </remarks>
    private readonly List<ElementoPlanta> _escalerasDeLaPlanta = new();

    /// <summary>Los contornos de los demás voladizos, sin contar el que se está dibujando.</summary>
    private List<IReadOnlyList<(double X, double Y)>> OtrosVolados(ElementoPlanta el)
    {
        var clave = ClaveDelPano(el);

        return _voladosDeLaPlanta
            .Where(v => !string.Equals(v.Clave, clave, StringComparison.Ordinal))
            .Select(v => v.Vertices)
            .ToList();
    }

    /// <summary>
    /// Los <b>tableros</b> de losa de la planta que se está dibujando: los pedazos del mesh,
    /// juntos.
    /// </summary>
    /// <remarks>
    /// Se calculan al empezar la planta, antes de dibujar el primer paño, por lo mismo que los
    /// voladizos: cada pedazo tiene que saber a qué tablero pertenece <b>antes</b> de decidir si le
    /// toca dibujar el armado y el rótulo o callarse. Descubriéndolos por el camino, el primer
    /// pedazo dibujaría su armado —porque aún no sabe de los otros— y saldría el problema de
    /// siempre, solo que con un armado en lugar de tres.
    /// </remarks>
    private readonly List<TableroDeLosa.Tablero> _tablerosDeLaPlanta = new();

    /// <summary>El tablero al que pertenece este pedazo de losa, o nulo si no se están uniendo.</summary>
    private TableroDeLosa.Tablero? TableroDe(ElementoPlanta el) =>
        _tablerosDeLaPlanta.FirstOrDefault(t => t.Pedazos.Any(q => ReferenceEquals(q, el)));

    /// <summary>Los contornos de los <b>otros pedazos del mismo tablero</b>.</summary>
    /// <remarks>
    /// Son los que dicen cuál de las orillas de este pedazo es una <b>junta del mesh</b> y no un
    /// borde del paño. La orilla que da a otro tablero no está aquí, y por eso sí se dibuja.
    /// </remarks>
    private List<IReadOnlyList<(double X, double Y)>> OtrosDelTablero(ElementoPlanta el)
    {
        var suyo = TableroDe(el);

        if (suyo is null || !suyo.Partido)
        {
            return new List<IReadOnlyList<(double X, double Y)>>();
        }

        return suyo.Pedazos
            .Where(q => !ReferenceEquals(q, el))
            .Select(q => (IReadOnlyList<(double X, double Y)>)q.Vertices)
            .ToList();
    }

    /// <summary>
    /// Con qué se puede juntar este paño: <b>volado</b>, <b>losacero</b> o losa de concreto.
    /// </summary>
    /// <remarks>
    /// Un volado no se junta con un entrepiso aunque se toquen, y una losacero tampoco con una losa
    /// de concreto: son paños distintos, se dibujan distinto —achurado, franjas, armado— y se
    /// rotulan distinto. Juntarlos daría un solo rótulo para dos cosas que no son la misma.
    /// </remarks>
    private string FamiliaDeLaLosa(ElementoPlanta el, IReadOnlyList<ElementoPlanta> huellas)
    {
        if (LosaVolada(el, huellas))
        {
            return "VOLADO";
        }

        if (_cfg.Bandera("LOSACERO_FRANJAS", true)
            && LosaEnPlanta.DiceLosacero(
                el.Etiqueta, el.Notas, el.Seccion,
                _cfg.Texto("LOSACERO_PALABRAS", "LOSACERO,DECK,STEEL DECK,LAMINA ACANALADA")))
        {
            return "LOSACERO";
        }

        return "LOSA";
    }

    /// <summary>
    /// ¿Este paño es un <b>voladizo</b>? Lo dice su nota, no la geometría.
    /// </summary>
    /// <remarks>
    /// Es lo que se pidió y lo correcto en un modelo real: el ingeniero sabe cuál es el volado y lo
    /// escribe en la propiedad, mientras que contar lados apoyados se equivoca en cuanto una cadena
    /// viene partida en el modelo. La cuenta por geometría se queda disponible con
    /// <c>VOLADO_POR_NOTA</c> en NO.
    /// </remarks>
    private bool LosaVolada(ElementoPlanta el, IReadOnlyList<ElementoPlanta> huellas) =>
        _cfg.Bandera("VOLADO_POR_NOTA", true)
            ? LosaEnPlanta.DiceVolado(
                el.Notas, el.Seccion,
                _cfg.Texto("LOSA_PALABRAS_VOLADO", "VOLADO,VOLADIZO,VOLADA,CANTILEVER"))
            : LosaEnPlanta.EsVolada(
                el.Vertices, huellas, _cfg.Numero("LOSA_APOYO_CUBRE", 0.7));

    /// <summary>Se cuenta lo que se juntó, y se avisa si los pedazos no coincidían.</summary>
    /// <remarks>
    /// Lo primero es una NOTA —salió bien—, y lo segundo es un AVISO de verdad: si los pedazos de un
    /// mismo tablero traen espesores distintos, el dibujo tiene que rotular uno solo y el dato del
    /// modelo es dudoso. Callarlo sería esconder que se eligió por el usuario.
    /// </remarks>
    private void AvisarDeLosTableros()
    {
        var partidos = _tablerosDeLaPlanta.Where(t => t.Partido).ToList();

        if (partidos.Count == 0)
        {
            return;
        }

        Nota($"{partidos.Count} tablero(s) de losa venían partidos por el mesh en " +
             $"{partidos.Sum(t => t.Pedazos.Count)} pedazos: se juntaron, y cada tablero lleva UN " +
             "armado y UN rótulo medidos sobre el tablero completo.");

        // Y CADA UNO CON SU MEDIDA Y SU SITIO. Es lo que permite revisar la unión sin abrir el
        // modelo: si un tablero salió más grande de lo que es —dos tableros juntados en uno— se ve
        // en su medida y se sabe dónde está para ir a mirarlo.
        foreach (var t in partidos)
        {
            Nota($"  · Tablero de {t.Ancho:0.00} × {t.Alto:0.00} m en " +
                 $"({t.X0:0.00}, {t.Y0:0.00}): {t.Pedazos.Count} pedazos del mesh.");
        }

        foreach (var t in partidos)
        {
            var espesores = t.Pedazos
                .Where(e => e.AnchoM > LargoMinimo)
                .Select(e => e.AnchoM)
                .ToList();

            if (espesores.Count > 1 && espesores.Max() - espesores.Min() > 0.01)
            {
                _log.Add(
                    $"Tablero de losa '{t.Manda.Etiqueta}': sus {t.Pedazos.Count} pedazos traen " +
                    $"espesores distintos —de {espesores.Min() * 100:0.#} a " +
                    $"{espesores.Max() * 100:0.#} cm—. Se rotuló el del pedazo más grande, " +
                    $"{t.Manda.AnchoM * 100:0.#} cm: revísalo en el modelo.");
            }
        }
    }

    /// <summary>
    /// Una <b>clave</b> para reconocer el paño: su etiqueta y su primer vértice.
    /// </summary>
    /// <remarks>
    /// La etiqueta sola no basta —hay modelos donde dos paños la repiten— y el vértice solo,
    /// tampoco. Juntos identifican el paño sin tener que guardar una referencia, que es lo que
    /// permite comparar contra la lista sin excluirse a sí mismo por error.
    /// </remarks>
    private static string ClaveDelPano(ElementoPlanta el)
    {
        var v = el.Vertices.Count > 0 ? el.Vertices[0] : (X: el.X1, Y: el.Y1);

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{el.Etiqueta}|{v.X:0.###}|{v.Y:0.###}");
    }

    /// <summary>
    /// La escala <b>de verdad</b> del achurado de la losa, la que lo deja <b>visible</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El <c>ANSI37</c> tiene sus líneas a <c>0.125</c> de unidad, así que con la escala de la
    /// macro —<c>0.0475</c>— la separación real queda en <b>5.9 mm</b>. En un tablero de
    /// 6 × 12 m eso son más de dos mil líneas por dirección: no se ve un achurado, se ve un
    /// <b>relleno gris uniforme</b>, y en el color 252 parece una sombra. Es exactamente lo
    /// que salía.
    /// </para>
    /// <para>
    /// Con <c>LOSA_HATCH_ESCALA_AUTO</c> la escala se saca al revés, de la separación que se
    /// quiere <b>ver</b>: <c>escala = separación / 0.125</c>. Con los 25 cm de la hoja sale
    /// escala 2 y el rayado a 45° se distingue, que es lo que tiene que verse en el plano.
    /// </para>
    /// <para>
    /// El valor literal de la macro se queda a un interruptor de distancia: con
    /// <c>LOSA_HATCH_ESCALA_AUTO</c> en NO manda <c>LOSA_HATCH_ESCALA</c> tal cual.
    /// </para>
    /// </remarks>
    private double EscalaDelHatchDeLosa()
    {
        var deLaHoja = _cfg.Numero("LOSA_HATCH_ESCALA", 0.0475);

        if (!_cfg.Bandera("LOSA_HATCH_ESCALA_AUTO", true))
        {
            return deLaHoja;
        }

        return EscalaDeHatch(_cfg.Numero("LOSA_HATCH_SEPARACION_CM", 25) / 100, deLaHoja);
    }

    /// <summary>
    /// La cuenta sola: qué escala hay que darle al patrón para que sus líneas queden a
    /// <paramref name="separacionM"/> metros.
    /// </summary>
    /// <remarks>
    /// <c>static</c> y aparte para poder comprobarla sin AutoCAD. Si la separación no tiene
    /// sentido se devuelve la de la hoja: más vale un achurado apretado que ninguno.
    /// </remarks>
    /// <param name="separacionM">Separación que se quiere ver, en metros.</param>
    /// <param name="escalaHoja">La escala literal de la macro, por si hay que volver a ella.</param>
    public static double EscalaDeHatch(double separacionM, double escalaHoja) =>
        separacionM > 0.005 ? separacionM / 0.125 : escalaHoja;

    /// <summary>
    /// Dice en las notas <b>qué sección</b> se tomó por voladizo y <b>por qué palabra</b>.
    /// </summary>
    /// <remarks>
    /// Una vez por sección, no una por paño. Y es la nota más útil de todas cuando el
    /// achurado sale donde no se espera, porque las <b>notas son de la PROPIEDAD</b>: si el
    /// voladizo y el entrepiso comparten la misma sección de losa y en sus notas dice VOLADO,
    /// todos los paños de esa sección son voladizo para el programa. Eso se arregla en ETABS
    /// —dándole al volado su propia propiedad—, no aquí, y sin esta nota no había forma de
    /// saberlo.
    /// </remarks>
    private void AvisarDelVolado(ElementoPlanta el)
    {
        var seccion = string.IsNullOrWhiteSpace(el.Seccion) ? "(sin sección)" : el.Seccion;

        if (!_voladosAvisados.Add(seccion))
        {
            return;
        }

        var notas = (el.Notas ?? string.Empty).Trim();

        var porque = notas.Length > 0 && LosaEnPlanta.DiceVolado(
                         notas, null, PalabrasDeVolado())
            ? $"sus NOTAS dicen «{notas}»"
            : "su NOMBRE de sección lo dice";

        Nota($"Losa «{seccion}»: VOLADIZO porque {porque}. Va achurada en " +
             $"{_capas.CapaVolado}, sin armado y con el rótulo corto. Ojo: las notas son de " +
             "la PROPIEDAD, así que TODOS los paños de esta sección salen achurados; si " +
             "alguno no es volado, dale su propia propiedad de losa en ETABS.");
    }

    /// <summary>Las palabras que marcan un voladizo: <c>LOSA_PALABRAS_VOLADO</c>.</summary>
    private string PalabrasDeVolado() =>
        _cfg.Texto("LOSA_PALABRAS_VOLADO", "VOLADO,VOLADIZO,VOLADA,CANTILEVER");

    /// <summary>
    /// La <b>parrilla</b> del armado de la losa, recortada al paño.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las varillas van en las dos direcciones a <c>MALLA_SEP_CM</c>, recortadas al contorno
    /// real del paño —no a su rectángulo envolvente— y <b>ajustadas al paño del muro</b>
    /// (<c>MALLA_AL_PANO</c>), así que empiezan y acaban donde empieza el claro y no se meten
    /// dentro de la cadena.
    /// </para>
    /// <para>
    /// Los filtros son los de la hoja, y son los que evitan armar lo que no se arma: una losa
    /// más delgada que <c>ARMADO_LOSA_ESPESOR_MIN_CM</c> —un firme— y un tablero de menos de
    /// <c>ARMADO_LOSA_LADO_MIN_CM</c> por lado no llevan parrilla.
    /// </para>
    /// <para>
    /// Falta la <b>bayoneta</b> de la macro —la polilínea de 6 vértices con sus quiebres a 45°
    /// y sus bastones a L/4— para el tablero apoyado en sus cuatro lados. Esto es la parrilla,
    /// que es el otro armado que ella dibuja.
    /// </para>
    /// </remarks>
    private void ArmadoDeLosa(
        ElementoPlanta el, double x0, double y0, IReadOnlyList<ElementoPlanta> huellas)
    {
        if (!_cfg.Bandera("DIBUJAR_ARMADO_LOSA", true))
        {
            return;
        }

        // ==============================================================================
        //  UN TABLERO, UN ARMADO
        // ==============================================================================
        //  Los pedazos que el mesh partió son UNA losa, así que el armado lo dibuja UNO —el más
        //  grande— y sobre la caja del tablero COMPLETO, que es el claro de verdad. Los demás se
        //  callan: si cada pedazo dibujara el suyo saldrían tres armaditos dentro del mismo
        //  tablero, cada uno con su bayoneta y sus bastones medidos sobre un claro que no existe.
        var tablero = TableroDe(el);

        if (tablero is not null && !tablero.Manejado(el))
        {
            return;
        }

        // Una losa muy delgada es un firme: no se arma con parrilla.
        var espesorMin = _cfg.Numero("ARMADO_LOSA_ESPESOR_MIN_CM", 8) / 100;

        if (el.AnchoM > LargoMinimo && el.AnchoM < espesorMin)
        {
            return;
        }

        var ladoMin = _cfg.Numero("ARMADO_LOSA_LADO_MIN_CM", 50) / 100;

        // La medida es la del TABLERO, no la del pedazo: un tablero mallado en cuadros de 40 cm no
        // se queda sin armado por el mínimo, que es lo que pasaba antes con cada cuadro por su
        // cuenta.
        var ancho = tablero?.Ancho ?? (el.Vertices.Max(v => v.X) - el.Vertices.Min(v => v.X));
        var alto = tablero?.Alto ?? (el.Vertices.Max(v => v.Y) - el.Vertices.Min(v => v.Y));

        if (ancho < ladoMin || alto < ladoMin)
        {
            return;
        }

        var capaArmado = _capas.Prefijo + "ARMADO LOSA";

        // ==============================================================================
        //  EL ARMADO DEL TABLERO: BAYONETA, BASTONES Y CORRIDA
        // ==============================================================================
        //  Es ArmadoDireccionX / ArmadoDireccionY con sus medidas: la bayoneta de seis
        //  vértices con sus quiebres a 45°, los dos bastones de L/4 con su rayita, y la
        //  corrida de lado a lado. Cada varilla, en DOBLE LÍNEA.
        //
        //  El armado se mide sobre el tablero YA LLEVADO AL PAÑO de sus apoyos —es
        //  ARMADO_AL_PANO_CADENA— para que la varilla empiece donde empieza el claro.
        if (_cfg.Bandera("ARMADO_LOSA_BAYONETA", true))
        {
            var margen = _cfg.Numero("ARMADO_LOSA_MARGEN_CM", 0) / 100;

            // La caja del TABLERO: de apoyo a apoyo, cruzando por encima de las juntas del mesh.
            var ax0 = (tablero?.X0 ?? el.Vertices.Min(v => v.X)) + margen;
            var ax1 = (tablero?.X1 ?? el.Vertices.Max(v => v.X)) - margen;
            var ay0 = (tablero?.Y0 ?? el.Vertices.Min(v => v.Y)) + margen;
            var ay1 = (tablero?.Y1 ?? el.Vertices.Max(v => v.Y)) - margen;

            // ==========================================================================
            //  AL PAÑO DEL APOYO, TAMBIÉN DE LA TRABE
            // ==========================================================================
            //  Cada borde se mete medio ancho del apoyo que corra sobre él, y cuenta cualquiera:
            //  muro, cadena o TRABE. Se pidió lo de la trabe y hacía falta: la losa se dibuja
            //  hasta el EJE de la trabe que la sostiene, así que sin correr el borde la varilla
            //  se metía media trabe dentro de ella.
            //
            //  Se pregunta por los cuatro BORDES DE LA CAJA del armado, no por los lados del
            //  polígono del tablero, que es como estaba y por lo que las trabes se quedaban
            //  fuera: la cuenta vieja necesitaba un lado en la coordenada extrema Y alineado con
            //  los ejes al milímetro de millón, y en un tablero que no es un rectángulo perfecto
            //  —o con las coordenadas que trae ETABS, que casi nunca son exactas— ese lado no
            //  aparecía y no se corría nada.
            if (_cfg.Bandera("ARMADO_AL_PANO_CADENA", true) && huellas.Count > 0)
            {
                var tolPano = _cfg.Numero("PANO_TOLERANCIA_CM", 25) / 100;

                // Los cuatro se miden ANTES de mover nada: si se aplicara uno a uno, el segundo
                // se mediría sobre un borde ya corrido y quedaría con el apoyo fuera de la
                // holgura.
                var pIzq = LosaEnPlanta.MedioApoyoEnBorde(
                    new LosaEnPlanta.Segmento(ax0, ay0, ax0, ay1), huellas, tolPano);

                var pDer = LosaEnPlanta.MedioApoyoEnBorde(
                    new LosaEnPlanta.Segmento(ax1, ay0, ax1, ay1), huellas, tolPano);

                var pAbajo = LosaEnPlanta.MedioApoyoEnBorde(
                    new LosaEnPlanta.Segmento(ax0, ay0, ax1, ay0), huellas, tolPano);

                var pArriba = LosaEnPlanta.MedioApoyoEnBorde(
                    new LosaEnPlanta.Segmento(ax0, ay1, ax1, ay1), huellas, tolPano);

                ax0 += pIzq;
                ax1 -= pDer;
                ay0 += pAbajo;
                ay1 -= pArriba;
            }

            var escalaVar = _cfg.Numero("ARMADO_LOSA_ESCALA_VARILLA", 1);

            var trazos = LosaEnPlanta.ArmadoDeTablero(
                ax0, ay0, ax1, ay1,
                _cfg.Bandera("ARMADO_LOSA_DOS_DIRECCIONES", true),
                escalaVar);

            var medio = LosaEnPlanta.MedioDiametroDeVarilla(escalaVar);

            foreach (var t in trazos)
            {
                DibujarTrazoDeArmado(t, x0, y0, medio, capaArmado);
            }

            _armadas++;
        }

        if (!_cfg.Bandera("ARMADO_LOSA_PARRILLA", false))
        {
            return;
        }

        var sep = _cfg.Numero("MALLA_SEP_CM", 15) / 100;

        // La parrilla se traza sobre el CONTORNO de cada pedazo del tablero, no sobre la caja: así
        // no aparece acero donde no hay concreto —un tablero en L tiene un hueco— y el tablero
        // partido queda cubierto entero, no solo el pedazo que manda.
        var barras = new List<LosaEnPlanta.Segmento>();

        foreach (var pedazo in tablero?.Pedazos ?? new List<ElementoPlanta> { el })
        {
            barras.AddRange(LosaEnPlanta.Parrilla(
                pedazo.Vertices,
                sep,
                _cfg.Numero("ARMADO_LOSA_MARGEN_CM", 0) / 100,
                _cfg.Bandera("ARMADO_LOSA_DOS_DIRECCIONES", true),
                (int)_cfg.Numero("MALLA_MAX_LINEAS", 200),
                _cfg.Numero("MALLA_SEGMENTO_MIN_CM", 15) / 100));
        }

        if (barras.Count == 0)
        {
            return;
        }

        var alPano = _cfg.Bandera("MALLA_AL_PANO", true) && huellas.Count > 0;
        var minTramo = _cfg.Numero("MALLA_SEGMENTO_MIN_CM", 15) / 100;

        foreach (var b in barras)
        {
            if (!alPano)
            {
                Linea(b.X1 + x0, b.Y1 + y0, b.X2 + x0, b.Y2 + y0, capaArmado);
                continue;
            }

            // Ajustada al paño: la varilla se parte donde entra en el muro o en la cadena y
            // se dibuja solo el trozo del claro.
            foreach (var t in LosaEnPlanta.TramosFuera(b, huellas, minTramo))
            {
                Linea(t.X1 + x0, t.Y1 + y0, t.X2 + x0, t.Y2 + y0, capaArmado);
            }
        }
    }

    /// <summary>
    /// Dibuja un trazo del armado: en <b>doble línea</b> si es una varilla.
    /// </summary>
    /// <remarks>
    /// La macro dibuja el eje y le hace <c>Offset(±d/2)</c>, con respaldo de copiar y mover.
    /// Aquí se dibujan directamente las dos líneas, desplazadas en la perpendicular al
    /// sentido de la varilla: es el mismo resultado y no depende de que <c>Offset</c>
    /// responda por COM, que es de las llamadas que más fallan.
    /// </remarks>
    private void DibujarTrazoDeArmado(
        LosaEnPlanta.Trazo t, double x0, double y0, double medio, string capa)
    {
        if (t.Puntos.Count < 2)
        {
            return;
        }

        if (!t.Doble || medio <= 0)
        {
            Puntos(t.Puntos, 0, 0);
            return;
        }

        // La varilla corre en X: sus dos líneas se separan en Y, y al revés.
        var dx = t.EnX ? 0 : medio;
        var dy = t.EnX ? medio : 0;

        Puntos(t.Puntos, dx, dy);
        Puntos(t.Puntos, -dx, -dy);

        void Puntos(List<(double X, double Y)> ps, double ox, double oy)
        {
            var arr = new double[ps.Count * 2];

            for (var i = 0; i < ps.Count; i++)
            {
                arr[2 * i] = ps[i].X + x0 + ox;
                arr[(2 * i) + 1] = ps[i].Y + y0 + oy;
            }

            PolilineaAbierta(arr, capa);
        }
    }

    /// <summary>Una polilínea <b>abierta</b>: la bayoneta del armado.</summary>
    private object? PolilineaAbierta(double[] puntos, string capa)
    {
        if (puntos.Length < 4)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic p = _ms.AddLightWeightPolyline(puntos);
                p.Closed = false;
                p.Layer = capa;
                p.Color = PorCapa;
                return (object?)p;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Armado de la losa en la capa '{capa}'", ex);
            return null;
        }
    }

    /// <summary>Cuántos paños se armaron, para el resumen.</summary>
    private int _armadas;

    /// <summary>
    /// El rótulo del elemento, <b>donde lo pone la macro</b> y no todos en el centro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esto es lo que hacía que la planta se leyera como un borrón: todos los rótulos iban
    /// horizontales y al centro del elemento, así que en cada nudo caían encima el de la
    /// columna y el de las cuatro trabes que llegan, y salía «CCK15X2515X25» pisado.
    /// </para>
    /// <para>
    /// La macro los reparte, y por eso su plano se lee:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Columna o castillo</b>: en la <b>esquina superior derecha</b> de la sección,
    ///     separado <c>COLUMNA_TEXTO_SEPARACION_CM</c> —2 cm— y horizontal. Ahí no hay
    ///     nada más, porque el nudo lo ocupa la propia columna.
    ///   </item>
    ///   <item>
    ///     <b>Trabe, cadena o viga</b>: al centro y <b>girado a lo largo de la barra</b>,
    ///     con el ángulo llevado al rango de −90° a 90° para que nunca salga de cabeza.
    ///   </item>
    ///   <item>
    ///     <b>Muro</b>: su pier, girado como el muro y <b>corrido al lado</b> medio espesor
    ///     más <c>PIER_SEPARACION_CM</c>, para que no caiga sobre las dos líneas del paño.
    ///   </item>
    ///   <item><b>Losa</b>: al centro del paño, horizontal.</item>
    /// </list>
    /// </remarks>
    private void Rotulo(ElementoPlanta el, PlantaCad p, double x0, double y0, double altura)
    {
        // El interruptor general de la hoja: DIBUJAR_ETIQUETAS en NO deja la planta muda,
        // que es como se entrega cuando el rotulado se hace aparte.
        if (!_cfg.Bandera("DIBUJAR_ETIQUETAS", true))
        {
            return;
        }

        // QUÉ se rotula: lo que dice la hoja CONFIG. ETIQUETA_ID_COLUMNAS y
        // ETIQUETA_ID_TRABES están en NO, así que de la columna y de la trabe va SOLO la
        // sección; del muro, solo su PIER —no la propiedad, que es la que repetía «MURO
        // TABICON 2 APLANADOS 15 CM» en los 31 muros—; y de la losa, su propiedad.
        var texto = el.Clase switch
        {
            ClasePlanta.Muro => PierDelMuro(el),
            ClasePlanta.Losa => RotuloDeLosa(el),
            _ => string.IsNullOrWhiteSpace(el.Seccion) ? el.Etiqueta : el.Seccion
        };

        if (string.IsNullOrWhiteSpace(texto))
        {
            // UN MURO SIN PIER NO SE ROTULA, pero se cuenta: así el resumen dice por qué
            // faltan rótulos en lugar de dejar pensando que se perdieron. Pasa en los
            // modelos de SAP2000, donde los piers no existen, y en los de ETABS a los que
            // no se les asignó ninguno.
            if (el.Clase == ClasePlanta.Muro)
            {
                _sinPier++;
            }

            return;
        }

        var (cx, cy) = CentroDe(el, x0, y0);

        // ==============================================================================
        //  LA CADENA QUE NO SE DIBUJÓ TAMPOCO SE ROTULA
        // ==============================================================================
        //  Si no, la intermedia desaparecería del dibujo pero su nombre seguiría escrito en el
        //  mismo punto que el de la cadena de arriba, y el rótulo doble era la mitad del
        //  problema.
        if (_cadenasTapadas.Contains(el))
        {
            return;
        }

        // ---- COLUMNA Y CASTILLO: esquina superior derecha ---------------------------
        //  Con el estilo TEXTO_SECCIONES y anclado por su esquina INFERIOR IZQUIERDA
        //  —la alineación 12 de la macro—, así que el texto crece hacia arriba y hacia la
        //  derecha y nunca se mete sobre la sección.
        if (el.Clase == ClasePlanta.Columna)
        {
            var b = el.AnchoM > LargoMinimo ? el.AnchoM : 0.15;
            var h = el.PeralteM > LargoMinimo ? el.PeralteM : b;
            var gap = _cfg.Numero("COLUMNA_TEXTO_SEPARACION_CM", 2) / 100;

            // La esquina de la sección YA GIRADA: en una columna a 30° la esquina no está
            // en (b/2, h/2). Se toma la caja que la envuelve, que es lo que se ve.
            var a = el.AnguloGrados * Math.PI / 180;
            var ca = Math.Abs(Math.Cos(a));
            var sa = Math.Abs(Math.Sin(a));

            var medioX = (b / 2 * ca) + (h / 2 * sa);
            var medioY = (b / 2 * sa) + (h / 2 * ca);

            Mtexto(cx + medioX + gap, cy + medioY + gap, texto, AlturaSecciones(altura),
                   CapaTextos, 0, EstiloSecciones, false, 7);
            return;
        }

        // ---- LOSA: al centro del paño, y DENTRO DE UN BLOQUE -------------------------
        //  Se pidió, y es la misma idea que ya se aplica a las secciones de columna: cada
        //  losa DISTINTA lleva su bloque —ROTULO-LOSA-VOLADO, ROTULO-LOSA-AZOTEA…—, así que
        //  cambiando el bloque una vez se cambian de golpe los veinte rótulos de esa losa.
        //  Escribir «Var. # 3/8 @ 20 cm.» en veinte MTEXT sueltos es veinte veces el mismo
        //  trabajo y diecinueve ocasiones de que uno quede distinto.
        if (el.Clase == ClasePlanta.Losa)
        {
            // ==========================================================================
            //  UN TABLERO, UN RÓTULO
            // ==========================================================================
            //  Los tres textos encimados de «Losa de… cm de espesor… Var. # @… cm.» eran esto:
            //  el rótulo se escribía una vez POR PEDAZO, y los pedazos de un tablero mallado caen
            //  todos dentro del mismo paño. Ahora lo escribe el que manda —el pedazo más grande,
            //  del que salen el espesor y el uso— y va al CENTRO DEL TABLERO, no al de su pedazo.
            var suTablero = TableroDe(el);

            if (suTablero is not null && !suTablero.Manejado(el))
            {
                return;
            }

            if (suTablero is not null)
            {
                cx = suTablero.CentroX + x0;
                cy = suTablero.CentroY + y0;
            }

            var alturaLosa = AlturaLosas(altura);

            if (!RotuloDeLosaComoBloque(el, cx, cy, texto, alturaLosa))
            {
                // Si el bloque no se puede crear —una versión que no deje, un nombre
                // imposible— el rótulo se escribe suelto, como siempre. El plano nunca se
                // queda sin el texto de la losa.
                Mtexto(cx, cy, texto, alturaLosa, CapaTextos, 0, EstiloLosas,
                       _cfg.Bandera("LOSA_TEXTO_FONDO", true));
            }

            return;
        }

        // ---- TRABE, CADENA, VIGA Y MURO: a lo largo de la barra ----------------------
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            Mtexto(cx, cy, texto, AlturaSecciones(altura), CapaTextos, 0, EstiloSecciones);
            return;
        }

        var ang = AnguloLegible(dx, dy);

        // ---- EL MURO: SU PIER, EN LA CAPA PIERS -------------------------------------
        //  Va corrido al lado del muro, no encima, y en su propia capa —PIERS, sin el
        //  prefijo E-, igual que la macro— para poder apagar todos los piers de un clic
        //  sin apagar los rótulos de las secciones.
        if (el.Clase == ClasePlanta.Muro)
        {
            var esp = el.AnchoM > LargoMinimo ? el.AnchoM : EspesorMuroPorOmision;
            var hPier = AlturaSecciones(altura);

            // De qué se separa: de lo más ancho que corra sobre el muro. Su espesor, la
            // línea de mampostería que va a su centro, o LA CADENA QUE LO TAPA. Si solo
            // se contara el espesor, en un muro de 15 con una cadena de 25 el pier caería
            // encima de la cadena. Es el eTapaB de la macro.
            var medio = Math.Max(esp / 2, _cfg.Numero("MAMPOSTERIA_ANCHO", 0.06) / 2);

            if (_anchoDeLaCadena.TryGetValue(el, out var anchoCadena) && anchoCadena > 0)
            {
                medio = Math.Max(medio, anchoCadena / 2);
            }

            var d = medio + (_cfg.Numero("PIER_SEPARACION_CM", 6) / 100) + (hPier * 0.7);

            Mtexto(cx + (-dy / largo * d), cy + (dx / largo * d), texto, hPier,
                   _capas.CapaPiers, ang, EstiloSecciones);
            return;
        }

        // ---- LA TRABE Y LA CADENA: MTEXT CENTRADO, GIRADO Y CON FONDO ----------------
        //  Centrado JUSTO EN MEDIO de la barra —TRABE_ROTULO_CENTRADO en SI—, no corrido a
        //  un lado, y con el fondo opaco puesto: es lo que deja leer la sección encima de
        //  las dos líneas del muro sin cortarlas (CADENA_CORTA_LINEA en NO).
        var esCadena = EsCadena(el);

        var altTrabe = esCadena ? AlturaCadenas(altura) : AlturaSecciones(altura);
        var estilo = esCadena ? EstiloCadenas : EstiloSecciones;
        var fondo = esCadena && _cfg.Bandera("CADENA_TEXTO_FONDO", true);

        // ==============================================================================
        //  EL NOMBRE DE LA CADENA NO SE ESCRIBE ENCIMA DE UN CASTILLO DE ÁREA
        // ==============================================================================
        //  Se pidió, y hay que medir EL TEXTO, no su punto de inserción, que es lo que fallaba
        //  antes: el rótulo es un MTEXT CENTRADO en la barra, así que el texto se extiende a los
        //  dos lados. Una cadena de 60 cm entre dos castillos tiene su centro ENTRE los dos
        //  —fuera de los dos— y «CC 15X25» mide más que la propia cadena: el punto no caía en
        //  ningún castillo y el texto los tapaba igual.
        //
        //  Se mide como el ancho de omisión de AnchoDeTexto —largo por altura por 0.55—, que es
        //  la cuenta que ya se usa aquí cuando AutoCAD no da la caja del texto.
        if (esCadena && !_cfg.Bandera("CADENA_ROTULO_EN_CASTILLO_AREA", false))
        {
            // LOS CASTILLOS DE ÁREA SE MIRAN AQUÍ, EN LA PLANTA QUE SE ESTÁ DIBUJANDO. Antes se
            // guardaban en un campo al convertirlos, y eso es una fuente de error de más: si el
            // orden cambia, o si la conversión no pasó por ahí, el campo llega vacío y la regla
            // no se aplica sin decir nada. La planta siempre está.
            var castillos = p.Elementos.Where(
                e => e.DeShell && e.Clase == ClasePlanta.Columna).ToList();

            // ==========================================================================
            //  DOS REGLAS, Y BASTA CON QUE SE CUMPLA UNA
            // ==========================================================================
            //  1) EL CASTILLO CUBRE A LA CADENA. En el modelo la cadena llega PARTIDA por sus
            //     cruces, así que el pedazo que va sobre el castillo es una cadena propia que
            //     mide lo que el castillo: su rótulo cae justo en medio de él y ahí no cabe
            //     ningún nombre. Esta regla no depende de medir el texto, y es la que resuelve
            //     el caso de la imagen: «CC 15X25» escrito a lo largo del K 15X80.
            //  2) EL TEXTO LO TAPA. Para el resto: una cadena más larga cuyo nombre, por su
            //     tamaño, acaba encima del castillo de todas formas.
            var tolCastillo = _cfg.Numero("TOLERANCIA_CADENA_CM", 10) / 100;

            var medioTexto = texto.Length * altTrabe * 0.55 / 2;
            var ex = dx / largo * medioTexto;
            var ey = dy / largo * medioTexto;

            if (PlanoEstructural.CastilloDeMuro.CubreALaBarra(el, castillos, tolCastillo)
                || PlanoEstructural.CastilloDeMuro.HayCastilloDeAreaBajoElTexto(
                       cx - ex, cy - ey, cx + ex, cy + ey, castillos, altTrabe))
            {
                _rotulosSobreCastillo++;
                return;
            }
        }

        Mtexto(cx, cy, texto, altTrabe, CapaTextos, ang, estilo, fondo);
    }

    /// <summary>
    /// El rótulo de la losa: los <b>cuatro renglones</b> de la hoja, no el nombre de la
    /// sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>LOSA_TEXTO_1</c> a <c>LOSA_TEXTO_4</c>: «Losa de AZOTEA / cm de espesor / Var. #
    /// @ cm. / Ambos sentidos». Antes se rotulaba el nombre de la propiedad de ETABS, que en
    /// el plano no dice nada; esto es lo que se lee en obra.
    /// </para>
    /// <para>
    /// Los renglones se toman <b>tal cual</b>, con sus espacios: los de
    /// <c>LOSA_TEXTO_2</c> —«       cm de espesor»— son el hueco donde va el número, y
    /// recortarlos dejaría el rótulo pegado a la izquierda. Es el mismo criterio de
    /// <c>CfgT</c> en la macro.
    /// </para>
    /// <para>
    /// <c>%U</c> se cambia por el uso —AZOTEA o ENTREPISO, según las palabras de la hoja— y
    /// <c>%E</c> por el espesor real en centímetros, si el modelo lo dio.
    /// </para>
    /// </remarks>
    private string RotuloDeLosa(ElementoPlanta el)
    {
        if (!_cfg.Bandera("ARMADO_LOSA_TEXTO", true))
        {
            return string.Empty;
        }

        // ==============================================================================
        //  LA LOSA DE VOLADO: EL RÓTULO SE QUEDA SOLO CON EL ARMADO
        // ==============================================================================
        //  Se pidió tal cual: cuando la losa diga VOLADO, el rótulo debe decir ÚNICAMENTE
        //
        //        Var. #      @               cm.
        //        Ambos sentidos
        //
        //  o sea los renglones 3 y 4. Los dos primeros —«Losa de VOLADO» y el espesor— no se
        //  escriben, porque en el volado lo que hay que ver es la varilla y su separación; el
        //  nombre y el espesor ya se leen en el resto del plano.
        //
        //  Se reconoce por la NOTA o por la sección, con las mismas palabras que usa el
        //  achurado —LOSA_PALABRAS_VOLADO— para que rótulo y ANSI37 nunca discrepen: si una
        //  losa sale achurada, sale también con el rótulo corto.
        var uso = UsoDeLaLosa(el);

        // ==============================================================================
        //  EL VOLADO: SU NOMBRE SÍ VA, Y EN EL PRIMER RENGLÓN
        // ==============================================================================
        //  Se pidió expresamente: el primer renglón del MTEXT dice «Losa VOLADO». La palabra
        //  sale de las NOTAS de la propiedad de la losa en ETABS —ahí es donde el ingeniero
        //  la escribe— y, si las notas no dicen nada, del nombre de la sección. Es la MISMA
        //  palabra que decide el achurado, así que el rótulo y el ANSI37 nunca se
        //  contradicen: donde dice VOLADO hay hatch, y donde hay hatch dice VOLADO.
        //
        //  Lo que NO lleva es el renglón del espesor. Queda:
        //
        //        Losa VOLADO
        //        Var. #      @               cm.
        //        Ambos sentidos
        var palabraVolado =
            LosaEnPlanta.PalabraVolado(el.Notas, el.Seccion, PalabrasDeVolado());

        // El respaldo: si el nombre que se iba a escribir —el que sale de la sección— ya
        // decía VOLADO, se trata como volado igual. Así la palabra no se cuela nunca por el
        // renglón de la macro.
        if (palabraVolado.Length == 0)
        {
            palabraVolado = LosaEnPlanta.PalabraVolado(uso, null, PalabrasDeVolado());
        }

        var esVolado = palabraVolado.Length > 0;

        var soloArmado = esVolado && _cfg.Bandera("VOLADO_ROTULO_SOLO_ARMADO", true);

        var hoja = new string[4];

        for (var i = 1; i <= 4; i++)
        {
            hoja[i - 1] = _cfg.TextoTalCual($"LOSA_TEXTO_{i}");
        }

        if (esVolado)
        {
            // El primer renglón del volado es el suyo —«Losa %U»—, y el %U es la palabra que
            // trae el modelo.
            var primero = _cfg.TextoTalCual("VOLADO_TEXTO_1");

            hoja[0] = primero.Trim().Length > 0 ? primero : hoja[0];
            uso = palabraVolado;
        }

        var espesor = el.AnchoM > LargoMinimo
            ? (el.AnchoM * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        var texto = ArmarRotuloDeLosa(hoja, soloArmado, uso, espesor);

        return texto.Length > 0
            ? texto
            : (string.IsNullOrWhiteSpace(el.Seccion) ? string.Empty : el.Seccion);
    }

    /// <summary>
    /// Junta los cuatro renglones del rótulo de la losa en un solo <b>MTEXT</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aparte y <c>static</c> para poder comprobarlo sin AutoCAD —está en
    /// <c>tools/prueba-ejes-plano</c>—, que es donde se ve de un golpe que el volado sale con
    /// dos renglones y la losa normal con cuatro.
    /// </para>
    /// <para>
    /// Con <paramref name="soloArmado"/> se salta los dos primeros renglones: es la LOSA DE
    /// VOLADO, donde se pidió que diga únicamente «Var. # @ cm. / Ambos sentidos».
    /// </para>
    /// </remarks>
    /// <param name="hoja">Los cuatro <c>LOSA_TEXTO_n</c>, tal cual, con sus espacios.</param>
    /// <param name="soloArmado"><c>true</c> = solo los renglones 3 y 4.</param>
    /// <param name="uso">Lo que sustituye a <c>%U</c>: AZOTEA, ENTREPISO, VOLADO…</param>
    /// <param name="espesor">Lo que sustituye a <c>%E</c>, en centímetros.</param>
    public static string ArmarRotuloDeLosa(
        IReadOnlyList<string> hoja, bool soloArmado, string uso, string espesor)
    {
        var renglones = new List<string>();

        for (var i = 1; i <= Math.Min(4, hoja.Count); i++)
        {
            // EN EL VOLADO SE SALTA EL RENGLÓN 2, el del espesor, y nada más. El 1 lleva su
            // nombre —«Losa VOLADO»— y el 3 y el 4, la varilla y los sentidos.
            if (soloArmado && i == 2)
            {
                continue;
            }

            var linea = hoja[i - 1] ?? string.Empty;

            // Un renglón vacío en la hoja es un renglón que no se quiere: no se deja el
            // hueco, porque el MTEXT lo enseñaría como una línea en blanco.
            if (linea.Trim().Length > 0)
            {
                renglones.Add(linea.Replace("%U", uso).Replace("%E", espesor));
            }
        }

        // \P es el salto de renglón de un MTEXT.
        return string.Join("\\P", renglones);
    }

    /// <summary>
    /// De qué es la losa: <c>AZOTEA</c> o <c>ENTREPISO</c>, por las palabras de la hoja.
    /// </summary>
    /// <remarks>
    /// Se miran la sección y las notas; si ninguna dice nada, se usa
    /// <c>LOSA_USO_POR_OMISION</c>. La azotea se comprueba <b>primero</b> porque una sección
    /// llamada «LOSA AZOTEA SLAB» contiene las dos palabras y lo que manda es la azotea.
    /// </remarks>
    private string UsoDeLaLosa(ElementoPlanta el)
    {
        // ==============================================================================
        //  EL NOMBRE DE LA SECCIÓN DE ETABS, SIN LA PALABRA «LOSA»
        // ==============================================================================
        //  Se pidió así: el rótulo dice lo que la sección se llama en el programa, quitándole
        //  la palabra LOSA porque el renglón ya la trae —«Losa de %U»—. Con una sección
        //  llamada «LOSA VOLADO» sale «Losa de VOLADO», y con «Losa AZOTEA», «Losa de
        //  AZOTEA», que es justo lo que se ve en su plano.
        //
        //  Y es mejor que la lista de palabras: sirve para cualquier nombre que use —MARQUESINA,
        //  PATIO, TAPANCO— sin tener que apuntarlo en la hoja. Las palabras se quedan como
        //  respaldo para cuando la sección no dice nada aprovechable.
        var deLaSeccion = SinLaPalabraLosa(el.Seccion);

        if (deLaSeccion.Length > 0)
        {
            return deLaSeccion;
        }

        var texto = ((el.Seccion ?? string.Empty) + " " + (el.Notas ?? string.Empty))
            .ToUpperInvariant();

        if (Contiene(_cfg.Texto("LOSA_PALABRAS_AZOTEA", "AZOTEA,CUBIERTA,TECHO,ROOF")))
        {
            return "AZOTEA";
        }

        if (Contiene(_cfg.Texto("LOSA_PALABRAS_ENTREPISO", "ENTREPISO,PISO,FLOOR,SLAB")))
        {
            return "ENTREPISO";
        }

        return _cfg.Texto("LOSA_USO_POR_OMISION", "ENTREPISO").ToUpperInvariant();

        bool Contiene(string palabras)
        {
            foreach (var palabra in palabras.Split(','))
            {
                var p = palabra.Trim().ToUpperInvariant();

                if (p.Length > 0 && texto.Contains(p, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// El nombre de la sección <b>sin la palabra LOSA</b> y en mayúsculas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// «LOSA VOLADO» → <c>VOLADO</c>; «Losa de azotea» → <c>AZOTEA</c>. Se quitan también
    /// <c>SLAB</c> y <c>DECK</c>, que es como ETABS llama a lo mismo, y las palabras de relleno
    /// —<c>DE</c>, <c>DEL</c>— que dejarían el rótulo diciendo «Losa de DE AZOTEA».
    /// </para>
    /// <para>
    /// Si de la sección no queda nada —se llama «LOSA» a secas, o «SLAB1»— se devuelve vacío y
    /// manda la lista de palabras de la hoja. Un rótulo que dijera «Losa de 1» sería peor que
    /// el de omisión.
    /// </para>
    /// </remarks>
    public static string SinLaPalabraLosa(string? seccion)
    {
        if (string.IsNullOrWhiteSpace(seccion))
        {
            return string.Empty;
        }

        var fuera = new[] { "LOSA", "LOSAS", "SLAB", "DECK", "DE", "DEL", "LA" };

        var piezas = seccion
            .ToUpperInvariant()
            .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !fuera.Contains(p))
            .ToList();

        // Un nombre que solo deja números —«SLAB 10»— no dice nada en el rótulo.
        if (piezas.Count == 0 || piezas.All(p => p.All(c => char.IsAsciiDigit(c) || c == ',')))
        {
            return string.Empty;
        }

        return string.Join(" ", piezas);
    }

    /// <summary>
    /// El pier del muro, y <b>nada más</b>: si no tiene pier, no se rotula.
    /// </summary>
    /// <remarks>
    /// Es lo que pidió el usuario y lo que hace la macro. Antes se caía a la etiqueta del
    /// muro —el nombre de su propiedad— y la planta salía con «MURO TABICON 2 APLANADOS 15
    /// CM» escrito 31 veces. Se acepta la etiqueta <b>solo</b> si el lector ya puso ahí el
    /// pier, que es lo que hace con los muros de ETABS y de SAP2000.
    /// </remarks>
    private static string PierDelMuro(ElementoPlanta el) =>
        !string.IsNullOrWhiteSpace(el.Pier) ? el.Pier.Trim() : string.Empty;

    /// <summary>
    /// ¿Es una <b>cadena de cerramiento</b>? Lo dice el prefijo de su sección.
    /// </summary>
    /// <remarks>
    /// <c>CADENA_PREFIJO_SECCION</c> es <c>CC</c>, así que <c>CC15X20</c> es cadena y
    /// <c>T30X60</c> no. También cuenta el tipo si la clasificación ya lo dijo. Importa
    /// porque la cadena lleva <b>otro estilo y otra altura</b> —TEXTO_CADENAS, 0.09— y el
    /// fondo opaco puesto.
    /// </remarks>
    private bool EsCadena(ElementoPlanta el)
    {
        if (el.Tipo.Contains("CADENA", StringComparison.OrdinalIgnoreCase) ||
            el.Tipo.Contains("DALA", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pref = _cfg.Texto("CADENA_PREFIJO_SECCION", "CC");

        return pref.Length > 0 && !string.IsNullOrWhiteSpace(el.Seccion) &&
               el.Seccion.TrimStart().StartsWith(pref, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El ángulo de un rótulo que se lee <b>a lo largo</b> de la barra, sin quedar de cabeza.
    /// </summary>
    /// <remarks>
    /// Es el apaño de la macro: un texto a 135° se lee del revés, así que el ángulo se lleva
    /// al rango de −90° a 90°. Un plano no se gira más de un cuarto de vuelta para leerlo.
    /// </remarks>
    public static double AnguloLegible(double dx, double dy)
    {
        var ang = Math.Atan2(dy, dx) * 180 / Math.PI;

        if (ang > 90)
        {
            ang -= 180;
        }
        else if (ang <= -90)
        {
            ang += 180;
        }

        return ang;
    }

    private string EstiloSecciones => _cfg.Texto("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES");

    private string EstiloCadenas => _cfg.Bandera("CADENA_USAR_ESTILO", true)
        ? _cfg.Texto("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS")
        : EstiloSecciones;

    private string EstiloLosas => _cfg.Bandera("LOSA_USAR_ESTILO", true)
        ? _cfg.Texto("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS")
        : EstiloSecciones;

    /// <summary>
    /// La altura del rótulo de una sección: la de la hoja, no una calculada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Va con la altura FIJA de la hoja —<c>SEC_ALTURA</c>, 0.12— porque es la del estilo
    /// <c>TEXTO_SECCIONES</c>, y un MTEXT <b>obedece al estilo</b> cuando este trae altura
    /// fija: pedirle otra no serviría de nada y el plano saldría con dos criterios.
    /// </para>
    /// <para>
    /// El valor calculado a partir del tamaño de la planta se usa solo como respaldo, si
    /// alguien pone la altura de la hoja en 0.
    /// </para>
    /// </remarks>
    private double AlturaSecciones(double respaldo)
    {
        var h = _cfg.Numero("SEC_ALTURA", 0.12);

        if (h > 0)
        {
            return h;
        }

        h = _cfg.Numero("ALTURA_TEXTO_SECCION", 0);

        return h > 0 ? h : respaldo;
    }

    private double AlturaCadenas(double respaldo)
    {
        var h = _cfg.Numero("CADENA_TEXTO_ALTURA", 0.09);

        if (h > 0)
        {
            return h;
        }

        var factor = _cfg.Numero("CADENA_TEXTO_FACTOR", 0.5);

        return factor > 0 ? AlturaSecciones(respaldo) * factor : respaldo;
    }

    private double AlturaLosas(double respaldo)
    {
        var h = _cfg.Numero("LOSA_TEXTO_ALTURA", 0.072);

        if (h > 0)
        {
            return h;
        }

        var factor = _cfg.Numero("LOSA_TEXTO_FACTOR", 0.5);

        return factor > 0 ? AlturaSecciones(respaldo) * factor : respaldo;
    }

    /// <summary>
    /// Dónde va el rótulo: el centro del paño en una losa, el centro del eje en el
    /// resto.
    /// </summary>
    /// <remarks>
    /// El centro de un paño se toma como la media de sus vértices. No es el
    /// centroide exacto de un polígono irregular, pero para colocar un rótulo dentro
    /// del paño es suficiente, y no falla nunca: la fórmula del centroide se va al
    /// infinito si el área sale cero, que es lo que pasa con un paño degenerado que
    /// ETABS entregue mal.
    /// </remarks>
    private static (double X, double Y) CentroDe(ElementoPlanta el, double x0, double y0)
    {
        if (el.Clase == ClasePlanta.Losa && el.Vertices.Count >= 3)
        {
            return (el.Vertices.Average(v => v.X) + x0,
                    el.Vertices.Average(v => v.Y) + y0);
        }

        return (((el.X1 + el.X2) / 2) + x0, ((el.Y1 + el.Y2) / 2) + y0);
    }

    /// <summary>El rótulo de la planta, debajo y a la izquierda del dibujo.</summary>
    private void TituloDeLaPlanta(PlantaCad p, double x0, double y0)
    {
        var conGeometria = p.Elementos
            .Where(e => e.Clase != ClasePlanta.Losa || e.Vertices.Count >= 3)
            .ToList();

        if (conGeometria.Count == 0)
        {
            return;
        }

        var xMin = double.MaxValue;
        var yMin = double.MaxValue;

        foreach (var el in conGeometria)
        {
            if (el.Vertices.Count >= 3)
            {
                foreach (var v in el.Vertices)
                {
                    xMin = Math.Min(xMin, v.X);
                    yMin = Math.Min(yMin, v.Y);
                }
            }
            else
            {
                xMin = Math.Min(xMin, Math.Min(el.X1, el.X2));
                yMin = Math.Min(yMin, Math.Min(el.Y1, el.Y2));
            }
        }

        var titulo = string.IsNullOrWhiteSpace(p.Nivel)
            ? "PLANTA ESTRUCTURAL"
            : "PLANTA ESTRUCTURAL " + p.Nivel.ToUpperInvariant();

        // Se separa del dibujo lo bastante para no montarse sobre el elemento más
        // bajo, y con el tamaño del título, no del rótulo de un elemento.
        var alto = p.AlturaTexto * 2.2;

        Mtexto(xMin + x0, yMin + y0 - (alto * 2), titulo, alto, CapaRotulo);
    }

    // ==================================================================
    // Primitivas de AutoCAD
    // ==================================================================

    /// <summary>
    /// Crea las capas de la macro con <b>su</b> color y <b>su</b> tipo de línea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son las 21 de <c>CapasPlano</c>, y el color se <b>pone siempre</b>, exista la capa o
    /// no: es lo que hace <c>AsegurarCapa</c> en la macro —<c>Layers.Add</c> devuelve la que
    /// ya está y le asigna el color igual— y es lo que hace falta para que el plano se vea
    /// como el suyo aunque el dibujo traiga esas capas de otro sitio con otro color.
    /// </para>
    /// <para>
    /// El tipo de línea se carga de <c>acad.lin</c> y, si no está, se deja la que tenga: la
    /// capa E-TRABE sin PHANTOM2 se ve continua, que es un detalle; una capa que no se pudo
    /// crear serían elementos perdidos.
    /// </para>
    /// </remarks>
    public void AsegurarCapas()
    {
        foreach (var capa in _capas.Todas)
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic todas = _doc.Layers;
                    dynamic lay;

                    try
                    {
                        lay = todas.Item(capa.Nombre);
                    }
                    catch (Exception)
                    {
                        lay = todas.Add(capa.Nombre);
                    }

                    lay.Color = capa.Color;

                    if (capa.TipoDeLinea.Length > 0 && AsegurarTipoDeLinea(capa.TipoDeLinea))
                    {
                        try
                        {
                            lay.Linetype = capa.TipoDeLinea;
                        }
                        catch (Exception)
                        {
                            // La capa se queda con la línea que tenga: es cosmético.
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Fallo($"Crear la capa '{capa.Nombre}'", ex);
            }
        }
    }

    /// <summary>Carga un tipo de línea si no está en el dibujo.</summary>
    private bool AsegurarTipoDeLinea(string nombre)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Linetypes.Item(nombre);
                    return true;
                }
                catch (Exception)
                {
                    try
                    {
                        _doc.Linetypes.Load(nombre, "acad.lin");
                        return true;
                    }
                    catch (Exception)
                    {
                        _doc.Linetypes.Load(nombre, "acadiso.lin");
                        return true;
                    }
                }
            });
        }
        catch (Exception)
        {
            Nota($"No se pudo cargar el tipo de línea '{nombre}'; la capa se queda con la " +
                 "que tenga.");
            return false;
        }
    }

    /// <summary>El estilo de texto compartido con las secciones y los alzados.</summary>
    private void AsegurarEstiloTexto()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;

                try
                {
                    _ = estilos.Item(EstiloTexto);
                }
                catch (Exception)
                {
                    dynamic nuevo = estilos.Add(EstiloTexto);
                    nuevo.SetFont("Arial", false, false, 0, 0);
                }
            });
        }
        catch (Exception ex)
        {
            // Sin estilo propio los textos salen con el estilo actual del dibujo. Se
            // pierde uniformidad, no el plano: no vale la pena abortar por esto.
            Nota("No se pudo preparar el estilo de texto '" + EstiloTexto +
                 "'; los rótulos usan el estilo actual del dibujo. " + ex.Message);
        }
    }

    private object? Linea(double xa, double ya, double xb, double yb, string capa)
    {
        if (Math.Abs(xb - xa) < 1e-12 && Math.Abs(yb - ya) < 1e-12)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic l = _ms.AddLine(new[] { xa, ya, 0d }, new[] { xb, yb, 0d });
                l.Layer = capa;
                l.Color = PorCapa;
                return (object?)l;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Línea de la planta en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? PolilineaCerrada(double[] puntos, string capa)
    {
        if (puntos.Length < 6)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic p = _ms.AddLightWeightPolyline(puntos);
                p.Closed = true;
                p.Layer = capa;
                p.Color = PorCapa;
                return (object?)p;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilínea de la planta en la capa '{capa}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Un <b>MTEXT</b>, con su estilo, su giro, su anclaje y —si se pide— su fondo opaco.
    /// </summary>
    /// <param name="estilo">
    /// El estilo de texto. En blanco = el de las secciones. <b>Si no está en el dibujo se
    /// crea</b>: es lo que hacía que los rótulos no salieran con la letra de la macro en un
    /// dibujo que no la tuviera.
    /// </param>
    /// <param name="conFondo">
    /// <c>true</c> = <c>BackgroundFill</c>, el fondo opaco que <b>borra lo que tenga
    /// atrás</b>. Es lo que en la macro deja leer el rótulo de la cadena encima de las dos
    /// líneas del muro sin tener que cortarlas (<c>CADENA_CORTA_LINEA</c> = NO).
    /// </param>
    /// <param name="anclaje">
    /// El <c>AttachmentPoint</c>: 5 = MiddleCenter —centrado, el de las trabes—, 7 =
    /// BottomLeft —el de la esquina de la columna, que es la alineación 12 de la macro—.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>El ancho es automático.</b> El MTEXT se crea con un ancho de arranque —con 0 hay
    /// versiones de AutoCAD que crean el objeto y no lo muestran— y acto seguido se le pone
    /// <c>Width = 0</c>, que en AutoCAD significa «sin ancho definido»: la caja se ajusta al
    /// texto y no parte renglones. Es lo que hay que hacer, porque una caja más ancha que el
    /// texto se nota: al centrar por <c>AttachmentPoint</c> se centra <b>la caja</b>, así que
    /// el rótulo se veía gordo y descentrado respecto a la trabe.
    /// </para>
    /// <para>
    /// Si esa versión no acepta el 0, se <b>mide</b> el texto ya dibujado y se le da su ancho
    /// exacto, que es lo mismo por otro camino. Y el anclaje y el punto de inserción se
    /// vuelven a poner <b>después</b> de cambiar el ancho: cambiar la caja mueve el texto.
    /// </para>
    /// <para>
    /// El orden importa: <b>estilo, luego altura</b>. Si el estilo trae altura fija —los de
    /// la macro la traen— manda el estilo y la asignación se ignora; al revés, la altura se
    /// perdería siempre.
    /// </para>
    /// </remarks>
    private object? Mtexto(
        double x, double y, string texto, double altura, string capa,
        double giroGrados = 0, string estilo = "", bool conFondo = false, int anclaje = 5,
        object? dentroDe = null)
    {
        if (string.IsNullOrWhiteSpace(texto) || altura <= 0)
        {
            return null;
        }

        // EL DUEÑO del texto: el espacio modelo, o un BLOQUE cuando el rótulo de la losa se
        // mete en uno. Es un parámetro y no un método aparte a propósito: el MTEXT de un
        // bloque tiene que nacer con el mismo estilo, el mismo ancho automático, el mismo
        // anclaje y el mismo fondo que el de fuera, y duplicar todo eso terminaría con dos
        // rótulos que se ven distinto.
        dynamic duenio = dentroDe ?? _ms;

        var nombreEstilo = estilo.Length > 0 ? estilo : EstiloTexto;

        // ANTES de crear el texto: si el estilo no está en el dibujo, se crea. Así el
        // rótulo sale con la letra que pide la hoja aunque el dibujo venga en blanco.
        AsegurarEstiloDeTexto(nombreEstilo);

        // El ancho de ARRANQUE, solo para que el objeto nazca visible. Enseguida se pone en
        // automático; y si no se puede, este es el que queda, así que se calcula ajustado al
        // texto —0.62 de la altura por letra, que es lo que mide una Bahnschrift— y no
        // holgado, para que el rótulo no salga gordo.
        var letras = texto.Split('\n', '\r').Max(s => s.Length);
        var ancho = Math.Max(1, letras) * altura * 0.62;

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic mt = duenio.AddMText(new[] { x, y, 0d }, ancho, texto);

                try
                {
                    mt.StyleName = nombreEstilo;
                }
                catch (Exception)
                {
                    // Sin el estilo, el texto sale con el del dibujo. No es motivo
                    // para perder el rótulo.
                }

                try
                {
                    mt.Height = altura;
                }
                catch (Exception)
                {
                    // El estilo trae altura fija: manda él, que es lo que quiere la macro.
                }

                // ==================================================================
                //  EL ANCHO, AUTOMÁTICO
                // ==================================================================
                //  Width = 0 es «sin ancho definido»: la caja se ajusta al texto. Es lo que
                //  hay que hacer, porque al centrar se centra LA CAJA, y con una caja más
                //  ancha que el texto el rótulo se veía gordo y corrido respecto a la trabe.
                //
                //  Si esta versión no acepta el 0, se MIDE el texto ya dibujado y se le da
                //  su ancho exacto: el mismo resultado por otro camino.
                AnchoAutomatico(mt, texto, altura);

                // El GIRO va antes de fijar el punto de anclaje: así el texto queda
                // centrado sobre el punto ya girado. Es lo que deja el rótulo de la trabe
                // LEÍDO A LO LARGO de la trabe, como en la macro, en lugar de horizontal y
                // encimado con el de la columna del nudo.
                if (Math.Abs(giroGrados) > 1e-9)
                {
                    try
                    {
                        mt.Rotation = giroGrados * Math.PI / 180;
                    }
                    catch (Exception)
                    {
                        // Sin giro se lee igual, solo que horizontal.
                    }
                }

                // El anclaje y el punto, DESPUÉS del ancho: cambiar la caja mueve el texto,
                // así que ponerlos antes lo dejaría corrido justo lo que la caja cambió.
                try
                {
                    mt.AttachmentPoint = anclaje;
                    mt.InsertionPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Alguna versión no acepta cambiar el punto de anclaje después
                    // de crear el MText. Se deja como salió: el rótulo queda algo
                    // corrido, pero está.
                }

                if (conFondo)
                {
                    try
                    {
                        // El fondo del DIBUJO, que es el que tapa sin pintar un color: es
                        // el «SI = con FONDO, borra lo que tenga atras» de la hoja.
                        mt.BackgroundFill = true;
                    }
                    catch (Exception)
                    {
                        Nota("Tu AutoCAD no aceptó el fondo opaco de los rótulos; si alguno " +
                             "se lee mal encima del muro, ponle máscara a mano.");
                    }
                }

                mt.Layer = capa;
                mt.Color = PorCapa;
                return (object?)mt;
            });
        }
        catch (Exception ex)
        {
            Fallo("Rótulo de la planta", ex);
            return null;
        }
    }

    /// <summary>
    /// Deja el MTEXT con el ancho <b>ajustado al texto</b>, no con una caja de más.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Primero por las buenas: <c>Width = 0</c>, que en AutoCAD es «sin ancho definido» y
    /// deja que la caja siga al texto. Se comprueba que de verdad se quedó en 0, porque hay
    /// versiones que aceptan la asignación y la ignoran.
    /// </para>
    /// <para>
    /// Y si no, por las malas: se <b>mide</b> la caja del texto ya dibujado y se le da ese
    /// ancho más un pelo. Medir es la única forma de saber lo que ocupa —depende de la
    /// fuente—, y es lo que hace la macro para centrar su rótulo. Si tampoco se puede medir
    /// se deja el ancho de arranque, que ya venía calculado a la medida del texto.
    /// </para>
    /// </remarks>
    private void AnchoAutomatico(object? mt, string texto, double altura)
    {
        if (mt is null)
        {
            return;
        }

        try
        {
            ((dynamic)mt).Width = 0d;

            if (Convert.ToDouble(((dynamic)mt).Width) <= 1e-9)
            {
                return;
            }
        }
        catch (Exception)
        {
            // Esta versión no admite el ancho libre: se mide.
        }

        var caja = CajaEnvolvente(mt);

        if (caja is not { } c)
        {
            return;
        }

        var medido = c.Max[0] - c.Min[0];

        if (medido <= 0)
        {
            return;
        }

        try
        {
            // Un pelo de más —una décima de letra— para que el último carácter no se parta
            // por un redondeo de la medida.
            ((dynamic)mt).Width = medido + (altura * 0.1);
        }
        catch (Exception)
        {
            Nota($"No se pudo ajustar el ancho del rótulo «{texto}»; queda con el ancho " +
                 "calculado, que puede verse algo holgado.");
        }
    }

    /// <summary>
    /// Se asegura de que un estilo de texto <b>exista</b>, y si no, lo crea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los cuatro estilos de la macro los crea <c>AsegurarEstilosDeLaMacro</c> al empezar a
    /// dibujar, con su fuente y su altura. Esto es la red de seguridad para cualquier otro
    /// —uno escrito a mano en la hoja CONFIG, por ejemplo—: se crea con la fuente que le
    /// toque según su nombre, o con Arial y altura libre si no se reconoce.
    /// </para>
    /// <para>
    /// Se recuerda lo ya visto para no interrogar al dibujo por cada rótulo: en una planta
    /// con 300 elementos serían 300 vueltas por COM, que es la parte lenta.
    /// </para>
    /// </remarks>
    private void AsegurarEstiloDeTexto(string nombre)
    {
        if (nombre.Length == 0 || !_estilosVistos.Add(nombre))
        {
            return;
        }

        var existe = false;

        try
        {
            existe = AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.TextStyles.Item(nombre);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }
        catch (Exception)
        {
            // Si no se puede preguntar, se intenta crear: crear uno que ya está no rompe.
        }

        if (existe)
        {
            return;
        }

        // La fuente que le toca por nombre. Es la de la hoja para los estilos de la macro,
        // y Arial con altura libre para cualquier otro.
        if (string.Equals(nombre, _cfg.Texto("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES"),
                          StringComparison.OrdinalIgnoreCase))
        {
            EstiloDeTexto(nombre,
                          _cfg.Texto("SEC_NOMBRE_FUENTE", "Bahnschrift"),
                          _cfg.Texto("SEC_FUENTE", "bahnschrift.ttf"),
                          _cfg.Numero("SEC_ALTURA", 0.12), false);
        }
        else if (string.Equals(nombre, _cfg.Texto("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS"),
                               StringComparison.OrdinalIgnoreCase))
        {
            EstiloDeTexto(nombre,
                          _cfg.Texto("CADENA_NOMBRE_FUENTE", "Bahnschrift"),
                          _cfg.Texto("CADENA_FUENTE", "bahnschrift.ttf"),
                          _cfg.Numero("CADENA_TEXTO_ALTURA", 0.09), false);
        }
        else if (string.Equals(nombre, _cfg.Texto("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS"),
                               StringComparison.OrdinalIgnoreCase))
        {
            EstiloDeTexto(nombre,
                          _cfg.Texto("LOSA_NOMBRE_FUENTE", "Bahnschrift"),
                          _cfg.Texto("LOSA_FUENTE", "bahnschrift.ttf"),
                          _cfg.Numero("LOSA_TEXTO_ALTURA", 0.072), false);
        }
        else
        {
            EstiloDeTexto(nombre, "Arial", "arial.ttf", 0, false);
        }
    }

    /// <summary>Estilos por los que ya se preguntó, para no repetir la vuelta por COM.</summary>
    private readonly HashSet<string> _estilosVistos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Le pone a una entidad un tipo de línea <b>por objeto</b>, con su escala.
    /// </summary>
    /// <remarks>
    /// Por objeto y no por capa a propósito: en <c>E-CADENA</c> conviven las cadenas que
    /// llevan muro completo —continuas— y las que no —<c>ACAD_ISO02W100</c>—, así que la capa
    /// no puede decidirlo. La escala también va por objeto porque un tipo de línea pensado
    /// para milímetros necesita 0.01 en un dibujo en metros.
    /// </remarks>
    private void PonerTipoDeLinea(object? ent, string tipo, double escala)
    {
        if (ent is null || tipo.Length == 0 || !AsegurarTipoDeLinea(tipo))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                ((dynamic)ent).Linetype = tipo;

                if (escala > 0)
                {
                    ((dynamic)ent).LinetypeScale = escala;
                }
            });
        }
        catch (Exception)
        {
            Nota($"No se pudo poner el tipo de línea '{tipo}'; esa cadena queda continua.");
        }
    }

    /// <summary>
    /// Pone la línea a trazos, cargando el tipo de línea si hace falta.
    /// </summary>
    /// <remarks>
    /// El tipo de línea vive en un archivo (<c>acad.lin</c>) y puede no estar cargado
    /// en el dibujo. Se intenta cargar y, si no se puede, la línea se queda continua:
    /// un eje continuo es un defecto de presentación, no un plano perdido.
    /// </remarks>
    private void LineaATrazos(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Linetypes.Item("CENTER");
                }
                catch (Exception)
                {
                    _doc.Linetypes.Load("CENTER", "acad.lin");
                }

                ((dynamic)ent).Linetype = "CENTER";
            });
        }
        catch (Exception ex)
        {
            Nota("No se pudo poner el eje a trazos; queda continuo. " + ex.Message);
        }
    }

    private void Fallo(string operacion, Exception ex) =>
        _log.Add($"{operacion}: {ex.Message}");

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }

    /// <summary>
    /// Espesor a usar cuando el modelo no lo dio: el de omisión, <b>sin dar lata</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes salía un aviso <b>por elemento</b>, y en un modelo con 31 muros de tabicón el
    /// resumen eran 31 renglones diciendo lo mismo. La macro no avisa de esto: si
    /// <c>GetWall</c> no da el espesor, <c>PropiedadDeMuro</c> lo saca del nombre y, si de
    /// ahí tampoco sale, usa <c>ESPESOR_MURO_CM</c> —15 cm— y sigue dibujando sin decir
    /// nada.
    /// </para>
    /// <para>
    /// Aquí se hace igual, pero se <b>cuentan</b> y al final se pone <b>un solo</b> renglón
    /// con el total. El dato interesa —un muro dibujado a 15 cm que en realidad mide 20 no
    /// se puede acotar— pero interesa una vez, no treinta y una.
    /// </para>
    /// </remarks>
    private double Espesor(ElementoPlanta el, double porOmision, string que)
    {
        if (el.AnchoM > LargoMinimo)
        {
            return el.AnchoM;
        }

        _sinEspesor++;
        _espesorOmision = porOmision;
        return porOmision;
    }

    /// <summary>Cuántos elementos se dibujaron con el espesor de omisión.</summary>
    private int _sinEspesor;

    private double _espesorOmision;

    /// <summary>Cuántos muros llegaron sin pier, y por tanto sin rótulo.</summary>
    private int _sinPier;

    /// <summary>Cuántos muros no se dibujaron porque su cadena los tapa.</summary>
    private int _tapados;

    /// <summary>
    /// El ancho de la cadena que tapa cada muro: el <c>eTapaB</c> de la macro.
    /// </summary>
    /// <remarks>
    /// Se guarda al dibujar el muro y se usa al rotular su pier, que va después. Es un
    /// diccionario por elemento y no un campo del DTO a propósito: es un dato del
    /// <b>dibujo</b> —depende de qué más haya en la planta— y no del modelo.
    /// </remarks>
    private readonly Dictionary<ElementoPlanta, double> _anchoDeLaCadena = new();

    /// <summary>
    /// El renglón único del resumen. Se llama al terminar de dibujar la planta.
    /// </summary>
    internal void ResumirEspesores()
    {
        if (_sinEspesor == 0)
        {
            return;
        }

        Nota($"{_sinEspesor} elemento(s) sin espesor en el modelo: se dibujaron con " +
             $"{_espesorOmision * 100:0} cm, como hace la macro. Revísalos antes de acotar.");

        _sinEspesor = 0;
    }

    /// <summary>
    /// El renglón de los muros que se quedaron sin rótulo por no tener pier.
    /// </summary>
    /// <remarks>
    /// En el muro se rotula <b>el pier y nada más</b>, que es lo que hace la macro. Así que
    /// si el modelo no los tiene asignados —en SAP2000 los piers no existen— los muros salen
    /// sin rótulo, y eso hay que decirlo una vez: es una decisión del modelo, no un fallo del
    /// dibujo.
    /// </remarks>
    internal void ResumirPiers()
    {
        if (_sinPier == 0)
        {
            return;
        }

        Nota($"{_sinPier} muro(s) sin PIER asignado en el modelo: se dibujaron sin rótulo, " +
             "porque en el muro se rotula el pier y no el nombre de su propiedad. Asígnales " +
             "un pier en el modelo si los quieres rotulados.");

        _sinPier = 0;
    }
}
