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

        AsegurarCapas();
        AsegurarEstiloTexto();

        // Los estilos de la macro: TEXTO_SECCIONES, TEXTO_CADENAS, TEXTO_LOSAS, COTA y
        // COTA_DIM. Sin ellos las cotas saldrían con la letra de fábrica de AutoCAD.
        AsegurarEstilosDeLaMacro();

        // Las losas PRIMERO, para que las trabes y las columnas queden encima. En
        // AutoCAD el orden de creación es el orden de dibujo, así que basta con
        // dibujarlas antes; no hace falta tocar el DrawOrder.
        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Losa))
        {
            if (Losa(el, x0, y0))
            {
                r.Losas++;
            }
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Muro))
        {
            if (Barra(el, x0, y0, CapaDe(el),
                     Espesor(el, EspesorMuroPorOmision, "muro"), conEje: false))
            {
                r.Muros++;

                // Y si es de BLOCK, su polilínea ancha al centro: es la marca de
                // mampostería, y es lo que distingue de un golpe de vista un muro de block
                // de uno de concreto.
                LineaDeMamposteria(el, x0, y0);
            }
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Trabe))
        {
            if (Barra(el, x0, y0, CapaDe(el),
                     Espesor(el, AnchoTrabePorOmision, "trabe"), conEje: true))
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
                Rotulo(el, x0, y0, p.AlturaTexto);
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

        // UN solo renglón con los que se dibujaron con el espesor de omisión, en lugar de
        // uno por elemento.
        ResumirEspesores();

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
    ///     <c>SEPARACION_ENTRE_PLANTAS</c> —5 m—, y es el <b>mismo para todas</b>: se toma
    ///     el rectángulo que las envuelve a todas, no el de cada una, para que queden
    ///     alineadas y a la misma distancia. Con el ancho de cada una, dos plantas
    ///     distintas quedarían descuadradas.
    ///   </item>
    ///   <item>
    ///     Todas arrancan en la misma Y, la de <c>OFFSET_Y_INICIAL</c> —15—, así que los
    ///     rótulos quedan en línea.
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

        var hueco = _cfg.Numero("SEPARACION_ENTRE_PLANTAS", 5);
        var offsetY = _cfg.Numero("OFFSET_Y_INICIAL", 15);
        var porFila = (int)_cfg.Numero("PLANTAS_POR_FILA", 100);

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

        // AL FINAL DE TODO, cuando ya está dibujado el juego entero: las capas de
        // CAPAS_AL_FRENTE encima de lo demás. Antes de terminar no serviría, porque cada
        // planta nueva se dibujaría después.
        TraerCapasAlFrente();

        if (_alFrente > 0)
        {
            Nota($"{_alFrente} objeto(s) subidos al frente " +
                 $"({string.Join(" + ", _capas.CapasAlFrente())}).");
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

        var pl = PolilineaCerrada(
            new[]
            {
                cx - (b / 2), cy - (h / 2),
                cx + (b / 2), cy - (h / 2),
                cx + (b / 2), cy + (h / 2),
                cx - (b / 2), cy + (h / 2)
            },
            CapaDe(el));

        if (pl is null)
        {
            return false;
        }

        // Las diagonales del recuadro: es la marca de «columna» en un plano
        // estructural, y distingue de un dado o de un hueco a simple vista.
        Linea(cx - (b / 2), cy - (h / 2), cx + (b / 2), cy + (h / 2), CapaDe(el));
        Linea(cx - (b / 2), cy + (h / 2), cx + (b / 2), cy - (h / 2), CapaDe(el));

        return true;
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
        double ancho, bool conEje)
    {
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            _log.Add($"'{el.Etiqueta}': largo nulo en planta, no se dibujó.");
            return false;
        }

        var ax = el.X1 + x0;
        var ay = el.Y1 + y0;
        var bx = el.X2 + x0;
        var by = el.Y2 + y0;

        // Normal unitaria al eje
        var nx = -dy / largo * (ancho / 2);
        var ny = dx / largo * (ancho / 2);

        var p1 = Linea(ax + nx, ay + ny, bx + nx, by + ny, capa);
        var p2 = Linea(ax - nx, ay - ny, bx - nx, by - ny, capa);

        if (p1 is null && p2 is null)
        {
            return false;
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

    /// <summary>La losa: el contorno del paño, cerrado y sin relleno.</summary>
    private bool Losa(ElementoPlanta el, double x0, double y0)
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

        return PolilineaCerrada(pts, CapaDe(el)) is not null;
    }

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
    private void Rotulo(ElementoPlanta el, double x0, double y0, double altura)
    {
        // QUÉ se rotula: lo que dice la hoja CONFIG. ETIQUETA_ID_COLUMNAS y
        // ETIQUETA_ID_TRABES están en NO, así que de la columna y de la trabe va SOLO la
        // sección; del muro, solo su PIER —no la propiedad, que es la que repetía «MURO
        // TABICON 2 APLANADOS 15 CM» en los 31 muros—; y de la losa, su propiedad.
        var texto = el.Clase switch
        {
            ClasePlanta.Muro => el.Etiqueta,
            ClasePlanta.Losa => el.Seccion,
            _ => string.IsNullOrWhiteSpace(el.Seccion) ? el.Etiqueta : el.Seccion
        };

        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        var (cx, cy) = CentroDe(el, x0, y0);

        // ---- COLUMNA Y CASTILLO: esquina superior derecha ---------------------------
        if (el.Clase == ClasePlanta.Columna)
        {
            var b = el.AnchoM > LargoMinimo ? el.AnchoM : 0.15;
            var h = el.PeralteM > LargoMinimo ? el.PeralteM : b;
            var gap = _cfg.Numero("COLUMNA_TEXTO_SEPARACION_CM", 2) / 100;

            Mtexto(cx + (b / 2) + gap + (altura * 2), cy + (h / 2) + gap + (altura / 2),
                   texto, altura, CapaTextos);
            return;
        }

        // ---- LOSA: al centro del paño ------------------------------------------------
        if (el.Clase == ClasePlanta.Losa)
        {
            Mtexto(cx, cy, texto, altura, CapaTextos);
            return;
        }

        // ---- TRABE, CADENA, VIGA Y MURO: a lo largo de la barra ----------------------
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            Mtexto(cx, cy, texto, altura, CapaTextos);
            return;
        }

        var ang = Math.Atan2(dy, dx) * 180 / Math.PI;

        // El mismo apaño de la macro: un texto a 135° se lee de cabeza, así que el ángulo
        // se lleva al rango de −90° a 90°.
        if (ang > 90)
        {
            ang -= 180;
        }
        else if (ang <= -90)
        {
            ang += 180;
        }

        var px = cx;
        var py = cy;

        if (el.Clase == ClasePlanta.Muro)
        {
            // Corrido al lado, en la perpendicular al muro: medio espesor más la
            // separación de la hoja, más media letra para que no roce la línea.
            var esp = el.AnchoM > LargoMinimo ? el.AnchoM : 0.15;
            var d = (esp / 2) + (_cfg.Numero("PIER_SEPARACION_CM", 6) / 100) + (altura * 0.7);

            px += -dy / largo * d;
            py += dx / largo * d;
        }

        Mtexto(px, py, texto, altura, CapaTextos, ang);
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

    private object? Mtexto(
        double x, double y, string texto, double altura, string capa,
        double giroGrados = 0)
    {
        if (string.IsNullOrWhiteSpace(texto) || altura <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic mt = _ms.AddMText(new[] { x, y, 0d }, 0d, texto);
                mt.Height = altura;

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

                // 5 = MiddleCenter. Centrado sobre el punto, que es el centro del
                // elemento: así el rótulo no se va hacia un lado en una trabe corta.
                try
                {
                    mt.AttachmentPoint = 5;
                    mt.InsertionPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Alguna versión no acepta cambiar el punto de anclaje después
                    // de crear el MText. Se deja como salió: el rótulo queda algo
                    // corrido, pero está.
                }

                try
                {
                    mt.StyleName = EstiloTexto;
                }
                catch (Exception)
                {
                    // Sin el estilo, el texto sale con el del dibujo. No es motivo
                    // para perder el rótulo.
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
}
