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
public sealed class PlantaDrawer
{
    private const int PorCapa = 256;

    // Capas de la planta. El prefijo las deja juntas en el administrador de capas y
    // evita pisar una capa del usuario que se llamara "MUROS" o "LOSAS".
    private const string CapaColumnas = "PLANTA-COLUMNAS";
    private const string CapaTrabes = "PLANTA-TRABES";
    private const string CapaMuros = "PLANTA-MUROS";
    private const string CapaLosas = "PLANTA-LOSAS";
    private const string CapaEjes = "PLANTA-EJES";
    private const string CapaTextos = "PLANTA-TEXTOS";
    private const string CapaRotulo = "PLANTA-ROTULO";

    // Colores ACI. Los mismos criterios del visor en pantalla, para que el plano se
    // parezca a la vista previa y nadie se pregunte si dibujó otra cosa.
    private const int ColorColumna = 1;    // rojo
    private const int ColorTrabe = 5;      // azul
    private const int ColorMuro = 3;       // verde
    private const int ColorLosa = 8;       // gris
    private const int ColorEje = 253;      // gris claro
    private const int ColorTexto = 7;      // blanco/negro segun el fondo

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
            if (Barra(el, x0, y0, CapaMuros,
                     Espesor(el, EspesorMuroPorOmision, "muro"), conEje: false))
            {
                r.Muros++;
            }
        }

        foreach (var el in p.Elementos.Where(e => e.Clase == ClasePlanta.Trabe))
        {
            if (Barra(el, x0, y0, CapaTrabes,
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

        TituloDeLaPlanta(p, x0, y0);

        // UN solo renglón con los que se dibujaron con el espesor de omisión, en lugar de
        // uno por elemento.
        ResumirEspesores();

        return r;
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
                            CapaColumnas) is not null;
            var ok2 = Linea(el.X1 + x0, el.Y1 + y0 - m, el.X1 + x0, el.Y1 + y0 + m,
                            CapaColumnas) is not null;
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
            CapaColumnas);

        if (pl is null)
        {
            return false;
        }

        // Las diagonales del recuadro: es la marca de «columna» en un plano
        // estructural, y distingue de un dado o de un hueco a simple vista.
        Linea(cx - (b / 2), cy - (h / 2), cx + (b / 2), cy + (h / 2), CapaColumnas);
        Linea(cx - (b / 2), cy + (h / 2), cx + (b / 2), cy - (h / 2), CapaColumnas);

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

        return PolilineaCerrada(pts, CapaLosas) is not null;
    }

    /// <summary>Etiqueta y sección del elemento, en el centro de su eje.</summary>
    private void Rotulo(ElementoPlanta el, double x0, double y0, double altura)
    {
        if (string.IsNullOrWhiteSpace(el.Etiqueta) && string.IsNullOrWhiteSpace(el.Seccion))
        {
            return;
        }

        var (cx, cy) = CentroDe(el, x0, y0);

        // ==============================================================================
        //  QUÉ SE ROTULA: LO QUE DICE LA HOJA CONFIG DE LA MACRO
        // ==============================================================================
        //  Antes salían la ETIQUETA y la SECCIÓN de todos los elementos, uno encima de
        //  otro: en una planta con 30 columnas, 43 trabes y 31 muros el dibujo se volvía
        //  ilegible, con los textos pisándose.
        //
        //  La macro rotula MUCHO menos, y por eso su plano se lee:
        //
        //      ETIQUETA_ID_COLUMNAS  = NO    ->  de la columna, solo la SECCIÓN
        //      ETIQUETA_SEC_COLUMNAS = SI
        //      ETIQUETA_ID_TRABES    = NO    ->  de la trabe, solo la SECCIÓN
        //      ETIQUETA_SEC_TRABES   = SI
        //
        //  y del MURO solo su PIER —la etiqueta—, nunca la propiedad, que es la que
        //  llenaba la planta de «MURO TABICON 2 APLANADOS 15 CM» repetido 31 veces.
        //
        //  Esto es un arreglo del dibujante de hoy, para que mientras se porta el de la
        //  macro el plano se pueda leer. El acomodo fino de cada rótulo —al costado de la
        //  trabe, en la esquina de la columna, al centro de la cadena— viene con él.
        // ==============================================================================
        var texto = el.Clase switch
        {
            // El muro: su PIER, y nada más. Si no tiene pier asignado, no se rotula.
            ClasePlanta.Muro => el.Etiqueta,

            // La losa: su nombre de propiedad, que es lo que dice de qué losa se trata.
            ClasePlanta.Losa => el.Seccion,

            // Columnas, trabes y diagonales: la SECCIÓN, sin el ID.
            _ => string.IsNullOrWhiteSpace(el.Seccion) ? el.Etiqueta : el.Seccion
        };

        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        Mtexto(cx, cy, texto, altura, CapaTextos);
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

    /// <summary>Crea las capas de la planta si no existen. Nunca cambia las que ya hay.</summary>
    public void AsegurarCapas()
    {
        var capas = new (string Nombre, int Color)[]
        {
            (CapaColumnas, ColorColumna),
            (CapaTrabes, ColorTrabe),
            (CapaMuros, ColorMuro),
            (CapaLosas, ColorLosa),
            (CapaEjes, ColorEje),
            (CapaTextos, ColorTexto),
            (CapaRotulo, ColorTexto)
        };

        foreach (var (nombre, color) in capas)
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic todas = _doc.Layers;

                    try
                    {
                        // Si ya existe se deja EXACTAMENTE como está: puede que el
                        // usuario le haya puesto su color y su grosor de pluma.
                        _ = todas.Item(nombre);
                    }
                    catch (Exception)
                    {
                        dynamic nueva = todas.Add(nombre);
                        nueva.Color = color;
                    }
                });
            }
            catch (Exception ex)
            {
                Fallo($"Crear la capa '{nombre}'", ex);
            }
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
        double x, double y, string texto, double altura, string capa)
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
