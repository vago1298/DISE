// ParameterModifier y BindingFlags: hacen falta para llamar a GetBoundingBox, que
// devuelve sus resultados por referencia y no se puede invocar con 'dynamic'.
using System.Reflection;

// DispatchWrapper: obliga a marshalizar una entidad como VT_DISPATCH dentro de un
// arreglo. Ver ConArregloDeEntidades.
using System.Runtime.InteropServices;

namespace CadLink.Cad;

/// <summary>
/// Dibuja secciones de concreto reforzado en AutoCAD, por COM.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>DibujarSeccionRectangular</c> de la macro. Las fórmulas de geometría
/// se copiaron tal cual: en particular que los radios del estribo se deriven del
/// diámetro de la varilla que abraza, de modo que quede <b>tangente</b> a la
/// varilla de esquina.
/// </para>
/// <para>
/// El <b>hatch de concreto va en dos partes</b>, igual que la macro:
/// </para>
/// <list type="number">
///   <item>Entre la cara del concreto y la frontera exterior del estribo.</item>
///   <item>
///     Dentro de la frontera interior, con las varillas como <b>islas</b> para que
///     el rayado no las cruce.
///   </item>
/// </list>
/// <para>
/// El cuerpo del estribo queda sin hatch: es justamente la franja entre las dos
/// fronteras. Por eso el rayado nunca tapa ni el acero ni el estribo, sin importar
/// el orden de dibujo.
/// </para>
/// </remarks>
public sealed partial class SeccionDrawer
{
    private const double Pi = Math.PI;
    private const double Rt2I = 0.707106781186547;
    private const double Rt2 = 1.41421356237309;

    /// <summary>Tan(90/4 grados): el bulge de un arco de 90 en una polilínea.</summary>
    private const double Bulge90 = 0.414213562373095;

    /// <summary>
    /// Holgura de la frontera del hatch para que las varillas tangentes no la toquen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La macro usa <c>0.000002</c>, dos micras, y con ese valor <b>AutoCAD rechaza
    /// las islas</b> del hatch con <c>0x80200003: Invalid input</c>. El motivo es
    /// geométrico: la varilla de esquina queda a
    /// <c>rec + dEst + d/2</c> del paño, así que su borde cae <b>exactamente</b> sobre
    /// la frontera interior del estribo. Con dos micras de separación, AutoCAD la
    /// considera tocando el contorno y no la admite como isla.
    /// </para>
    /// <para>
    /// Se sube a <b>0.2 mm</b>, cien veces más. No se ve por dos razones: la frontera
    /// es una polilínea <b>temporal</b> que se borra en cuanto el hatch está hecho, y
    /// esos 0.2 mm son el 2% del espesor del estribo, que además queda tapado por el
    /// propio estribo.
    /// </para>
    /// <para>
    /// En la macro el fallo también ocurre, pero pasa inadvertido: se traga el error
    /// con <c>On Error Resume Next</c>, y como el rayado se manda al fondo y la
    /// varilla lleva su relleno encima, la isla que falta no se nota.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Segunda corrección.</b> Se había subido a <c>0.0002</c> (0.2 mm) para que
    /// AutoCAD aceptara las islas, y eso trajo un defecto visible: la frontera del
    /// relleno queda <b>metida</b> 0.2 mm respecto al contorno, así que entre el
    /// relleno sólido y su línea aparece una <b>rendija</b> del color del fondo. En
    /// el dibujo del usuario se veía como un halo blanco alrededor del estribo y de
    /// las varillas: <i>«el hatch sólido del estribo no llega a su línea»</i>.
    /// </para>
    /// <para>
    /// Ahora son <b>20 micras</b>: diez veces la holgura de la macro, que basta para
    /// que la isla no se considere tangente, y cincuenta veces menos que antes, con
    /// lo que la rendija baja a 0.02 mm y deja de verse a cualquier zoom de trabajo.
    /// </para>
    /// <para>
    /// Bajarlo es seguro porque las islas se agregan <b>una por una</b> y la que
    /// AutoCAD rechace se salta sola, sin llevarse el resto del hatch.
    /// </para>
    /// </remarks>
    private const double EpsTangencia = 0.00002;

    /// <summary>Holgura de tangencia, a la escala del dibujo.</summary>
    private double EpsHatch => EpsTangencia * _f;

    /// <summary>Traslape cola-doblez del gancho: 10 micras, como en la macro.</summary>
    private const double SolapeGancho = 0.00001;

    private const string PatronConcreto = "AR-CONC";
    private const string PatronRespaldo = "ANSI31";

    /// <summary>
    /// Escala del AR-CONC. Valor por omisión, ajustable desde la aplicación.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es el mismo 0.0003 de la macro.</b> Estuvo un tiempo en 0.01 porque en las
    /// primeras pruebas el rayado salía microscópico con 0.0003, y 0.01 fue el valor
    /// que se midió bien sobre el dibujo. Al arreglarse lo demás —el patrón, las
    /// fronteras, el orden de dibujo— el 0.0003 volvió a verse bien, y como es el
    /// valor con el que el usuario lleva años trabajando, es el que debe venir puesto.
    /// </para>
    /// <para>
    /// El valor se multiplica por la escala del dibujo, de modo que si se dibuja al
    /// doble de tamaño el rayado crece con la sección en lugar de quedarse diminuto.
    /// </para>
    /// <para>
    /// Sigue siendo <b>ajustable</b> desde la casilla de la aplicación, porque el
    /// tamaño con que AutoCAD dibuja un patrón depende también del dibujo: la variable
    /// <c>MEASUREMENT</c> decide si el patrón se toma de <c>acad.pat</c> o de
    /// <c>acadiso.pat</c>, y entre esos dos archivos hay un factor de 25.4. En una
    /// plantilla distinta el mismo número puede salir mucho más fino.
    /// </para>
    /// </remarks>
    private const double EscalaPatronBase = 0.0003;
    private const int ColorPatron = 251;
    private const int ColorFondo = 9;
    private const int ColorRellenoEstribo = 152;

    /// <summary>Color «por capa». El rotulado va siempre así, como en la macro.</summary>
    private const int PorCapa = 256;

    // ---------- Estilos de texto y de cota ----------
    private const string EstiloTexto = "SECCIONES";
    private const string FuenteTexto = "BAHNSCHRIFT SEMILIGHT";
    private const double AlturaTextoCotas = 0.025;
    private const double FactorAnchoTexto = 1.0;

    private const string EstiloCota = "COTA_ESTRUCTURAL";
    private const string BloqueFlechaCota = "_OPEN90";
    private const int ColorLineaCota = 253;
    private const int ColorExtensionCota = 253;
    private const int ColorTextoCota = 1;

    // ---------- Llamadas (leaders) de los lechos ----------
    private const double AlturaTextoLeader = 0.021;
    private const double LechoSepY = 0.032;
    private const double LechoSepX = 0.045;
    private const double OffsetIntermediaSup = 0.011;
    private const double LineaVerticalDist = 0.025;
    private const double TamFlecha = 0.005;

    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly double _escala;

    /// <summary>Color verdadero negro, cacheado. Ver <see cref="ColorNegro"/>.</summary>
    private object? _negro;
    private bool _negroIntentado;

    /// <summary>
    /// Escala del patrón AR-CONC. Se aplica igual en los dos tipos de sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <b>ajustable</b> porque el mismo 0.0003 de la macro no siempre se ve
    /// igual: el tamaño con que AutoCAD dibuja un patrón depende también del
    /// dibujo, en particular de la variable <c>MEASUREMENT</c>, que decide si los
    /// patrones se toman de <c>acad.pat</c> o de <c>acadiso.pat</c>. Entre esos dos
    /// archivos hay un factor de 25.4, así que en una plantilla distinta el mismo
    /// número puede salir mucho más fino.
    /// </para>
    /// <para>
    /// El valor se multiplica por la escala del dibujo, de modo que si se dibuja al
    /// doble de tamaño el rayado crece con la sección en lugar de quedarse
    /// diminuto. Con la escala normal de 0.01 el resultado es idéntico al de la
    /// macro.
    /// </para>
    /// </remarks>
    public double EscalaHatch { get; set; } = EscalaPatronBase;

    private double EscalaHatchEfectiva => EscalaHatch * _f;

    private readonly List<string> _log = new();

    /// <summary>
    /// Fallos que se toleraron durante el dibujo, con su causa real.
    /// </summary>
    /// <remarks>
    /// <b>Por qué existe.</b> Casi todo el dibujo está envuelto en <c>catch</c> que
    /// dejan continuar, porque un hatch que falla no debe tirar abajo la sección.
    /// Pero esos <c>catch</c> estaban <b>descartando la excepción</b>, y el
    /// resultado fue que los hatches de concreto no aparecían y desde aquí no había
    /// forma de saber por qué: solo se veía el dibujo incompleto. Sin el HRESULT no
    /// se puede distinguir un patrón que no existe de una frontera mal formada o de
    /// una propiedad que esta versión de AutoCAD no acepta.
    /// </remarks>
    /// <summary>
    /// Solo los fallos de verdad. Vacío significa que el dibujo salió completo.
    /// </summary>
    public IReadOnlyList<string> Fallos => _log;

    /// <summary>
    /// Notas informativas: qué vía funcionó, de dónde se cargó la interop.
    /// </summary>
    /// <remarks>
    /// Van <b>separadas de los fallos</b>. Al principio todo iba en la misma lista y
    /// el resultado fue un cuadro de aviso diciendo «hubo 2 fallos» cuando las dos
    /// líneas eran informativas y el dibujo había salido perfecto. Un aviso que
    /// grita cuando no pasa nada enseña a ignorar los avisos.
    /// </remarks>
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

    /// <summary>Fallos y notas juntos, para el informe completo.</summary>
    public IReadOnlyList<string> Diagnostico
    {
        get
        {
            var todo = new List<string>();
            todo.AddRange(Notas);
            todo.AddRange(_log);
            return todo;
        }
    }

    private readonly List<string> _notas = new();

    /// <summary>
    /// Varillas del lecho superior de la sección en curso: centro y radio.
    /// </summary>
    /// <remarks>
    /// Solo los lechos, no las laterales. Es el <c>gVarSupX/Y/R</c> de la macro, y
    /// lo usa el estribo diamante para saber a qué varillas del centro abrazarse.
    /// </remarks>
    private readonly List<(double X, double Y, double R)> _varSup = new();

    /// <summary>Varillas del lecho inferior. Ver <see cref="_varSup"/>.</summary>
    private readonly List<(double X, double Y, double R)> _varInf = new();

    /// <summary>
    /// Varillas <b>laterales</b>, las de los costados.
    /// </summary>
    /// <remarks>
    /// Hacen falta para que el estribo diamante no las atraviese. Sus tramos rectos
    /// bajan por los costados justo por donde van estas varillas, y sin conocerlas la
    /// cinta les pasaba por encima: en el dibujo la diagonal del diamante cruzaba la
    /// varilla por la mitad. Ver <c>RodearLaterales</c>.
    /// </remarks>
    private readonly List<(double X, double Y, double R)> _varLat = new();

    /// <summary>Un tramo recto del estribo principal, con su geometría anotada.</summary>
    /// <remarks>
    /// Se guarda la geometría <b>además</b> de la entidad porque para recortar el
    /// tramo hay que saber por dónde va, y preguntárselo a AutoCAD entidad por
    /// entidad es lento y devuelve los extremos en un orden que no está garantizado.
    /// Aquí se conocen los números exactos con los que se dibujó.
    /// </remarks>
    private sealed class TramoEstribo
    {
        public required object Ent { get; init; }

        /// <summary>Horizontal, o vertical si es <c>false</c>.</summary>
        public required bool Horizontal { get; init; }

        /// <summary>La coordenada constante: la Y si es horizontal, la X si no.</summary>
        public required double Fijo { get; init; }

        /// <summary>Inicio del tramo a lo largo de su eje.</summary>
        public required double A { get; init; }

        /// <summary>Fin del tramo a lo largo de su eje.</summary>
        public required double B { get; init; }
    }

    /// <summary>
    /// Tramos rectos del estribo principal de la sección en curso, para poder
    /// recortarlos donde el diamante pasa por encima.
    /// </summary>
    private readonly List<TramoEstribo> _tramosEstribo = new();

    /// <summary>Trozo más corto que vale la pena dibujar: medio milímetro.</summary>
    private double LargoMinTramo => 0.0005 * _f;

    /// <summary>IDs de las secciones que se saltaron por tener ya su bloque.</summary>
    private readonly List<string> _saltadas = new();

    /// <summary>
    /// Secciones que <b>no</b> se dibujaron porque ya existían en el dibujo.
    /// </summary>
    /// <remarks>
    /// No son fallos, así que no van a la lista de fallos; pero hay que
    /// <b>decírselo al usuario</b>. Si se saltan en silencio, el ingeniero cambia
    /// el armado de una sección en la hoja, vuelve a dibujar, no ve ningún aviso y
    /// se queda creyendo que el plano ya tiene el armado nuevo. Ese es el tipo de
    /// silencio que acaba en obra.
    /// </remarks>
    public IReadOnlyList<string> Saltadas => _saltadas;

    /// <summary>IDs de las secciones que se borraron y se volvieron a dibujar.</summary>
    private readonly List<string> _redibujadas = new();

    /// <summary>Secciones que se redibujaron en el sitio que ya tenían.</summary>
    public IReadOnlyList<string> Redibujadas => _redibujadas;

    /// <summary>
    /// Con esto encendido, la sección que ya existe <b>se rehace</b> en su mismo
    /// sitio en lugar de saltarse.
    /// </summary>
    /// <remarks>
    /// Es el <c>ActualizarSecciones</c> de la macro. Está apagado por omisión, que es
    /// el comportamiento de <c>DibujarSecciones</c>: saltar. Los dos hacen falta y no
    /// son lo mismo. Sin el salto, redibujar tira el acomodo del plano; sin esto, un
    /// cambio de armado no hay forma de llevarlo al plano más que purgando bloques a
    /// mano en AutoCAD, que es justo lo que no debe tener que hacer el usuario.
    /// </remarks>
    public bool Redibujar { get; set; }

    /// <summary>
    /// La última sección dibujada volvió al sitio que ya tenía, así que no ocupa
    /// lugar nuevo en la fila.
    /// </summary>
    public bool UltimaFueASuSitio { get; private set; }

    /// <summary>Anota algo informativo, sin repetirlo.</summary>
    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }

    /// <summary>
    /// Ejecuta una llamada de AutoCAD que recibe un <b>arreglo de entidades</b>,
    /// probando las dos formas de empaquetarlo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Este era el fallo que dejaba el dibujo sin ningún hatch.</b> Todas las
    /// llamadas que reciben entidades en un arreglo —<c>AppendOuterLoop</c>,
    /// <c>AppendInnerLoop</c>, <c>CopyObjects</c>, <c>MoveToTop</c>,
    /// <c>MoveToBottom</c>— fallaban con:
    /// </para>
    /// <code>COMException 0x8021007B: Invalid object array</code>
    /// <para>
    /// El motivo es de marshalling, no de geometría: AutoCAD espera que los
    /// elementos del arreglo lleguen como <c>VT_DISPATCH</c>, y un
    /// <c>object[]</c> de .NET con objetos COM dentro se marshaliza como
    /// <c>VT_UNKNOWN</c>. VBA no tiene el problema porque sus <c>Object</c> ya son
    /// <c>IDispatch</c>. <see cref="DispatchWrapper"/> fuerza esa conversión.
    /// </para>
    /// <para>
    /// Se prueban las dos formas porque no todas las versiones de AutoCAD se
    /// comportan igual, y el diagnóstico anota cuál funcionó.
    /// </para>
    /// </remarks>
    private bool ConArregloDeEntidades(
        string operacion, IReadOnlyList<object> entidades, Action<object> llamada)
    {
        return AcadArreglos.Llamar(operacion, entidades, llamada, Fallo, Nota);
    }

    /// <summary>
    /// Igual, pero para el <b>orden de dibujo</b>: lo que falle va como NOTA, no como
    /// fallo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El orden de dibujo es estético: cambia qué queda encima de qué, no qué hay en el
    /// plano. Reportarlo como fallo hacía que el resumen avisara de que «el dibujo puede
    /// estar incompleto» cuando estaba entero, y eso es peor que no avisar: enseña al
    /// usuario a desconfiar de un mensaje que casi siempre es falsa alarma, y el día que
    /// falte algo de verdad no lo va a creer.
    /// </para>
    /// <para>
    /// La nota sí dice qué se perdió, porque tiene consecuencia visible: sin reordenar, el
    /// rayado puede tapar una varilla o el rótulo quedar debajo del acero.
    /// </para>
    /// </remarks>
    private bool ConArregloParaOrdenar(
        string operacion, IReadOnlyList<object> entidades, Action<object> llamada)
    {
        var reportado = false;

        var ok = AcadArreglos.Llamar(
            operacion, entidades, llamada,
            (op, ex) =>
            {
                if (reportado)
                {
                    return;
                }

                reportado = true;

                Nota(
                    $"{op}: no se pudo reordenar ({ex.GetType().Name}). El dibujo está " +
                    "completo; lo único que puede pasar es que algo quede tapado por " +
                    "encima, como el rayado sobre una varilla.");
            },
            Nota);

        return ok;
    }


    /// <summary>Registra un fallo tolerado, sin repetir el mismo mensaje.</summary>
    private void Fallo(string operacion, Exception ex)
    {
        var e = ex;
        while (e is System.Reflection.TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var detalle = e.GetType().Name;

        if (e is System.Runtime.InteropServices.COMException com)
        {
            detalle += $" 0x{(uint)com.HResult:X8}";
        }

        detalle += ": " + e.Message.Replace(Environment.NewLine, " ").Trim();

        var linea = operacion + " -> " + detalle;

        // Una sección repite las mismas operaciones muchas veces; sin esto el
        // informe saldría con cientos de renglones idénticos.
        if (!_log.Contains(linea))
        {
            _log.Add(linea);
        }
    }

    /// <summary>Factor respecto a la escala base de la macro, 0.01.</summary>
    private readonly double _f;

    public SeccionDrawer(dynamic doc, double escala = 0.01)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);
        _escala = escala <= 0 ? 0.01 : escala;
        _f = _escala / 0.01;

        // Se fuerza la búsqueda de la interop AQUÍ, antes de dibujar, para que su
        // resultado quede en la bitácora aunque después no falle nada.
        _ = AcadInterop.TipoEntidad;
    }

    // ==================================================================
    // Capas y variables de cota
    // ==================================================================

    public void AsegurarCapas(IEnumerable<string> clavesDeVarilla)
    {
        Capa("CONCRETO", 8);
        Capa("ESTRIBOS", 150);
        Capa("TEXTOS", 3);
        Capa("ROTULOS", 3);
        Capa("COTAS", 253);

        var colores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["#2"] = 150, ["#2.5"] = 6, ["#3"] = 132, ["#4"] = 142, ["#5"] = 160,
            ["#6"] = 4, ["#8"] = 1, ["#10"] = 6, ["#12"] = 15
        };

        foreach (var clave in clavesDeVarilla.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(clave))
            {
                Capa("VAR_" + clave, colores.TryGetValue(clave, out var c) ? c : 7);
            }
        }

        AsegurarEstiloTexto();
        ConfigurarCotas();
    }

    /// <summary>
    /// Crea el estilo de texto <c>SECCIONES</c> con la fuente de la macro.
    /// </summary>
    /// <remarks>
    /// <b>Esto faltaba por completo</b> y era la razón principal de que los dibujos
    /// no se parecieran a los de la macro. Sin asignar un estilo, AutoCAD usa el
    /// estilo actual del dibujo, que casi siempre es <c>Standard</c> con la fuente
    /// vectorial <c>txt.shx</c>: cuadrada y muy distinta de la Bahnschrift. Toda la
    /// geometría podía estar bien y el dibujo se veía mal solo por la tipografía.
    /// </remarks>
    private void AsegurarEstiloTexto()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(EstiloTexto);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(EstiloTexto);
                }

                // Los dos false son negrita e itálica; los dos ceros, juego de
                // caracteres y familia, igual que el SetFont de la macro.
                estilo.SetFont(FuenteTexto, false, false, 0, 0);
                estilo.Height = AlturaTextoCotas * _f;
                estilo.Width = FactorAnchoTexto;
            });
        }
        catch (Exception ex)
        {
            // Si la fuente no está instalada, AutoCAD la sustituye y el texto
            // sigue saliendo; solo cambia el tipo de letra.
            Fallo($"Estilo de texto '{EstiloTexto}' con la fuente '{FuenteTexto}'", ex);
        }
    }

    private void Capa(string nombre, int colorAci)
    {
        AcadConnection.Retry(() =>
        {
            dynamic capas = _doc.Layers;
            dynamic capa;
            try
            {
                capa = capas.Item(nombre);
            }
            catch (Exception)
            {
                capa = capas.Add(nombre);
            }

            capa.Color = colorAci;
        });
    }

    /// <summary>
    /// Deja las variables de cota y el estilo <c>COTA_ESTRUCTURAL</c> como la macro.
    /// </summary>
    /// <remarks>
    /// Antes aquí solo se fijaban diez variables y no se creaba ningún estilo de
    /// cota, así que las cotas salían con <b>flechas rellenas</b> en lugar de las
    /// marcas abiertas <c>_OPEN90</c>, sin colores propios y sin líneas de
    /// extensión de longitud fija. Se veían como cotas de AutoCAD recién instalado,
    /// no como las de la macro.
    /// </remarks>
    private void ConfigurarCotas()
    {
        AplicarVariablesDeCota();
        AsegurarEstiloCota();
    }

    /// <summary>
    /// Fija <b>una sola</b> variable de cota, tolerando que esta versión de AutoCAD
    /// la rechace, y dejando dicho en el diagnóstico cuál fue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Este método existe por un error de traducción del VBA.</b> La macro fija
    /// las variables debajo de un <c>On Error Resume Next</c>, y en VBA eso es
    /// tolerancia <b>por instrucción</b>: si <c>DIMBLK</c> no se puede asignar, esa
    /// línea se salta y las veinte siguientes <b>sí se ejecutan</b>.
    /// </para>
    /// <para>
    /// Al portarlo, las veinticinco asignaciones quedaron dentro de un
    /// <b>único</b> <c>try</c>. Un solo rechazo abortaba el bloque completo, así que
    /// <c>DIMEXO</c>, <c>DIMEXE</c>, <c>DIMFXL</c>, <c>DIMFXLON</c>, <c>DIMDEC</c> y
    /// <c>DIMTXSTY</c> nunca se fijaban y el dibujo se quedaba con los valores de la
    /// plantilla del usuario. Como esas plantillas suelen venir de un plano a escala
    /// de impresión, <c>DIMEXO</c> y <c>DIMEXE</c> traían valores cien veces
    /// mayores: de ahí las <b>líneas de extensión enormes</b>.
    /// </para>
    /// <para>
    /// Peor aún, el fallo era <b>invisible</b>: el <c>catch</c> descartaba la
    /// excepción sin registrarla. Ahora cada variable va sola y su rechazo aparece
    /// con nombre y HRESULT en el informe.
    /// </para>
    /// </remarks>
    /// <param name="valores">
    /// El valor, y opcionalmente <b>otras formas del mismo valor</b> por si AutoCAD
    /// rechaza la primera. Se prueban en orden y basta con que una funcione.
    /// </param>
    /// <remarks>
    /// <para>
    /// Las alternativas hacen falta porque el tipo que AutoCAD acepta para una
    /// variable <b>no siempre es el documentado</b>. El caso real fue
    /// <c>DIMDSEP</c>: se documenta como el código ASCII del separador, y con
    /// <c>46</c> el AutoCAD 2026 del usuario contestaba
    /// <c>0x80210066: Error setting system variable</c>. Con el carácter en texto sí
    /// lo acepta.
    /// </para>
    /// <para>
    /// Solo se reporta el fallo si <b>fallan todas</b>. Reportar cada intento
    /// llenaría el aviso de ruido para algo que acabó funcionando.
    /// </para>
    /// </remarks>
    private void Dimvar(string nombre, params object[] valores)
    {
        Exception? ultimo = null;

        foreach (var valor in valores)
        {
            try
            {
                // El cuerpo va entre llaves a propósito: con una expresión, al ser
                // '_doc' dinámico, la lambda podría resolverse al Retry<T> genérico.
                AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
                return;
            }
            catch (Exception ex)
            {
                ultimo = ex;
            }
        }

        if (ultimo is not null)
        {
            var formas = string.Join(" ni ", valores.Select(v => $"'{v}' ({v.GetType().Name})"));
            Fallo($"Variable de cota {nombre}: no acepta {formas}", ultimo);
        }
    }

    /// <summary>Deja en el documento las variables de cota de la macro.</summary>
    /// <remarks>
    /// El <b>orden es deliberado</b>: primero lo que define la geometría de la cota
    /// y al final las flechas. <c>DIMBLK</c> es la asignación con más probabilidad de
    /// ser rechazada, porque depende de que el bloque <c>_OPEN90</c> esté disponible
    /// en el dibujo. Con <see cref="Dimvar"/> el orden ya no cambia el resultado,
    /// pero deja lo delicado donde menos estorba.
    /// </remarks>
    private void AplicarVariablesDeCota()
    {
        Dimvar("DIMSCALE", 1d);

        // Líneas de extensión: separación de la pieza, remate más allá de la línea
        // de cota y LONGITUD FIJA. Estas cuatro son las que se perdían.
        Dimvar("DIMEXO", 0.02 * _f);
        Dimvar("DIMEXE", 0.035 * _f);
        Dimvar("DIMFXL", 0.035 * _f);
        Dimvar("DIMFXLON", 1);
        Dimvar("DIMDLE", 0d);

        // Colores: línea y extensión en 253, el texto en 1
        Dimvar("DIMCLRD", ColorLineaCota);
        Dimvar("DIMCLRE", ColorExtensionCota);
        Dimvar("DIMCLRT", ColorTextoCota);

        Dimvar("DIMTXT", 0.017 * _f);
        Dimvar("DIMGAP", 0.005 * _f);

        // Aquí había un DIMTOFF que AutoCAD rechazaba, y con razón: esa variable
        // NO EXISTE. No hay ninguna DIMTOFF en AutoCAD; las que se parecen son
        // DIMTOFL y DIMTMOVE, que hacen otra cosa. La separación del texto respecto
        // a la línea de cota ya la da DIMGAP, así que no se pierde nada al quitarla.
        // Se colaba porque el error se descartaba en silencio.

        Dimvar("DIMLUNIT", 2);
        Dimvar("DIMDEC", 2);
        Dimvar("DIMZIN", 0);

        // El PUNTO como separador decimal. Se prueba el código ASCII, que es lo
        // documentado, y si no lo acepta, el carácter en texto. Tu AutoCAD 2026
        // rechaza el 46, así que la segunda forma es la que acaba funcionando.
        Dimvar("DIMDSEP", 46, ".");

        Dimvar("DIMTXSTY", EstiloTexto);
        Dimvar("DIMTAD", 0);
        Dimvar("DIMUPT", 0);
        Dimvar("DIMTIH", 1);
        Dimvar("DIMTOH", 1);

        // Flechas: marca abierta a 90, no el triángulo relleno de fábrica.
        // Al final, por ser lo más frágil. Se fijan las tres porque cuál manda
        // depende de DIMSAH, que se deja como lo tenga el dibujo, igual que la macro.
        Dimvar("DIMASZ", 0.02 * _f);
        Dimvar("DIMBLK", BloqueFlechaCota);
        Dimvar("DIMBLK1", BloqueFlechaCota);
        Dimvar("DIMBLK2", BloqueFlechaCota);
    }

    /// <summary>Crea o refresca el estilo de cota <c>COTA_ESTRUCTURAL</c>.</summary>
    private void AsegurarEstiloCota()
    {
        // El estilo se crea DESPUES de fijar las variables, porque CopyFrom copia
        // el estado actual del documento. Al revés saldría un estilo de fábrica.
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(EstiloCota);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(EstiloCota);
                }

                estilo.CopyFrom(_doc);
                _doc.ActiveDimStyle = estilo;
            });
        }
        catch (Exception)
        {
            // Sin estilo propio las cotas usan el activo del dibujo.
        }
    }

    /// <summary>
    /// Asigna <b>una sola</b> propiedad de una cota, tolerando que esta versión de
    /// AutoCAD no la exponga.
    /// </summary>
    /// <remarks>
    /// Va por reflexión y no con <c>dynamic</c> para poder recorrer una lista de
    /// nombres y, sobre todo, para <b>nombrar en el diagnóstico</b> la propiedad que
    /// falló. Con un bloque <c>dynamic</c> compartido, una propiedad ausente
    /// cancelaba todas las siguientes sin decir cuál.
    /// </remarks>
    private void PropCota(object cota, string propiedad, object valor)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                cota.GetType().InvokeMember(
                    propiedad,
                    BindingFlags.SetProperty,
                    binder: null,
                    target: cota,
                    args: new[] { valor });
            });
        }
        catch (Exception ex)
        {
            // ------------------------------------------------------------------
            // «Esa propiedad no existe» NO es un fallo del dibujo
            // ------------------------------------------------------------------
            // Las propiedades de cota no son las mismas en todas las versiones de
            // AutoCAD. Cuatro de las que pide la macro —ExtensionLineOffset,
            // ExtensionLineExtend, ExtLineFixedLen y ExtLineFixedLenSuppress— no
            // están en algunas, y el enlace tardío responde con
            // DISP_E_UNKNOWNNAME (0x80020006).
            //
            // Eso se estaba contando como fallo, y el resultado era que al terminar
            // de dibujar salía «PERO hubo 4 fallo(s) que se toleraron, así que el
            // dibujo puede estar incompleto» en un dibujo que estaba perfecto. Un
            // aviso que se dispara siempre y que no hay forma de atender enseña al
            // usuario a ignorar los avisos, y entonces los de verdad tampoco se
            // leen.
            //
            // Son propiedades de PRESENTACIÓN: afinan cuánto sobresale la línea de
            // extensión. Sin ellas la cota sale igual de correcta, solo con el
            // remate por omisión de la versión. Así que se registran como NOTA.
            if (EsPropiedadInexistente(ex))
            {
                Nota(
                    $"Esta versión de AutoCAD no tiene la propiedad de cota " +
                    $"'{propiedad}', así que se dejó su valor por omisión. Es un " +
                    "detalle de presentación de la línea de extensión: las cotas " +
                    "salen bien.");

                return;
            }

            Fallo($"Cota: propiedad {propiedad}", ex);
        }
    }

    /// <summary>
    /// ¿El error es «esa propiedad no existe» y no un fallo de verdad?
    /// </summary>
    /// <remarks>
    /// Se comprueba por <b>HRESULT</b> y no por el texto del mensaje: el mensaje
    /// viene traducido al idioma de AutoCAD —«Nombre desconocido» en español,
    /// «Unknown name» en inglés— así que buscar en el texto funcionaría en una
    /// instalación y no en la siguiente.
    /// <para>
    /// <c>DISP_E_UNKNOWNNAME</c> es el error del enlace tardío cuando el objeto COM
    /// no expone ese nombre. <c>DISP_E_MEMBERNOTFOUND</c> es su equivalente cuando
    /// el nombre se resuelve pero no como miembro asignable.
    /// </para>
    /// </remarks>
    private static bool EsPropiedadInexistente(Exception ex)
    {
        const uint DispUnknownName = 0x80020006;      // DISP_E_UNKNOWNNAME
        const uint DispMemberNotFound = 0x80020003;   // DISP_E_MEMBERNOTFOUND

        var e = ex;

        // El InvokeMember envuelve la excepción de COM, así que hay que desenvolverla
        // o el HRESULT que se leería sería el del envoltorio.
        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        if (e is COMException com)
        {
            var h = (uint)com.HResult;
            return h == DispUnknownName || h == DispMemberNotFound;
        }

        // Sin COM por medio, la reflexión avisa así de que el miembro no está.
        return e is MissingMemberException;
    }

    /// <summary>Aplica a una cota el estilo, la capa y los ajustes de la macro.</summary>
    /// <remarks>
    /// <para>
    /// <b>El estilo se asigna primero y las propiedades después</b>, y el orden no es
    /// negociable: asignar <c>StyleName</c> vuelca sobre la cota todos los valores
    /// guardados en el estilo, borrando cualquier ajuste previo. Al revés, el formato
    /// se perdería completo.
    /// </para>
    /// <para>
    /// Que se repitan aquí valores que ya están en las variables del documento no es
    /// redundancia: es la <b>última línea de defensa</b>. Si la plantilla del usuario
    /// trae un <c>COTA_ESTRUCTURAL</c> heredado con longitudes de otro plano, estas
    /// asignaciones sobre el objeto mandan sobre el estilo y la cota sale bien de
    /// todas formas.
    /// </para>
    /// </remarks>
    private void FormatearCota(object cota)
    {
        // Primero el estilo, solo. Si esto se mezclara con lo de abajo, un fallo
        // aquí se llevaría por delante todo el formato.
        PropCota(cota, "StyleName", EstiloCota);
        PropCota(cota, "Layer", "COTAS");

        // --- Líneas de extensión ---
        // ExtLineFixedLen sin activar la longitud fija no hace nada: el interruptor
        // es ExtLineFixedLenSuppress, y su nombre engaña, porque a 'true' es cuando
        // la longitud fija QUEDA ACTIVA (equivale a DIMFXLON = 1).
        PropCota(cota, "ExtensionLineOffset", 0.02 * _f);
        PropCota(cota, "ExtensionLineExtend", 0.035 * _f);
        PropCota(cota, "ExtLineFixedLen", 0.035 * _f);
        PropCota(cota, "ExtLineFixedLenSuppress", true);

        // --- Texto y flechas ---
        PropCota(cota, "TextGap", 0.005 * _f);
        PropCota(cota, "TextHeight", 0.017 * _f);
        PropCota(cota, "TextStyle", EstiloTexto);
        PropCota(cota, "ArrowheadSize", 0.02 * _f);
        PropCota(cota, "TextRotation", 0d);

        // Los DOS decimales se fijan también EN LA COTA, no solo con DIMDEC: la
        // variable del documento la pisa el estilo, la propiedad del objeto no.
        PropCota(cota, "PrimaryUnitsPrecision", 2);   // acDimPrecisionTwo

        // El PUNTO como separador decimal, no la coma.
        //
        // No basta con DIMDSEP: esa variable la pisa el estilo de cota del dibujo, y
        // además AutoCAD la inicializa según la CONFIGURACIÓN REGIONAL de Windows.
        // En un Windows en español el separador decimal es la coma, así que en la
        // máquina del usuario las cotas salían "30,00" aunque DIMDSEP se hubiera
        // fijado en 46. Aquí se fija sobre el objeto, que manda sobre las dos cosas.
        //
        // Ojo con el tipo: la VARIABLE DIMDSEP se fija con el código ASCII (46),
        // mientras que la PROPIEDAD del objeto es el carácter en texto (".").
        // Ponerle 46 a la propiedad dejaría la cota con un "46" de separador.
        PropCota(cota, "DecimalSeparator", ".");

        try
        {
            AcadConnection.Retry(() => { ((dynamic)cota).Update(); });
        }
        catch (Exception ex)
        {
            Fallo("Cota: Update", ex);
        }
    }

    // ==================================================================
    // Dibujo de una sección
    // ==================================================================

    /// <returns>Cuántas entidades se crearon. <c>0</c> si la sección se saltó.</returns>
    public int Dibujar(SeccionCad s, double xIzquierda, double yAbajo)
    {
        // La sección que ya es bloque NO se vuelve a dibujar, igual que la macro.
        // La comprobación se repite aquí aunque quien llama ya la haga, porque
        // volver a dibujar una sección existente deja el plano con dos copias
        // encimadas y eso es carísimo de deshacer a mano.
        // Al redibujar, aquí se guarda dónde estaba para devolverla a su sitio.
        double[]? destino = null;

        if (BloqueYaExiste(s.Id))
        {
            if (!Redibujar)
            {
                // Se anota SIEMPRE, sin quitar repetidos: quien llama detecta el
                // salto porque la lista creció, y si aquí se filtraran los
                // repetidos, una segunda fila con el mismo ID pasaría por dibujada y
                // le robaría su sitio en la fila a la siguiente sección. Los
                // repetidos se quitan al mostrarlos, que es donde estorban.
                _saltadas.Add(s.Id);
                return 0;
            }

            // El punto se lee ANTES de borrar. Después ya no hay a quién
            // preguntárselo, y la sección acabaría al final de la fila.
            destino = PuntoDeInsercion(s.Id);

            if (!BorrarSeccion(s.Id))
            {
                // Si no se pudo borrar, dibujar encima dejaría dos copias
                // encimadas. Mejor saltarla y decirlo.
                _saltadas.Add(s.Id);
                return 0;
            }

            _redibujadas.Add(s.Id);
        }

        // Si la sección va a volver a su sitio, NO ocupa lugar en la fila. Quien
        // llama lo necesita saber para no dejar un hueco por cada una que se rehizo.
        UltimaFueASuSitio = destino is not null;

        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);

        // El registro de varillas es POR SECCION: si no se limpia, el estribo
        // diamante de una seccion se abrazaria a las varillas de la anterior.
        _varSup.Clear();
        _varInf.Clear();
        _varLat.Clear();
        _tramosEstribo.Clear();

        // Se escribe con switch y no con una comparacion suelta para que cada
        // tipo diga explicitamente si lleva fondo solido, sin depender de que el
        // Tipo 1 sea el valor por omision. El AR-CONC se dibuja en los DOS tipos;
        // lo que cambia es el fondo solido, el relleno del estribo y el contorno.
        var conFondoSolido = s.Modo switch
        {
            ModoSeccion.Tipo1SinRelleno => false,
            ModoSeccion.Tipo2Rellena => true,
            _ => true
        };

        // ---------- La seccion REDONDA se va por su propio camino ----------
        // No es una variante del rectangulo con un radio: no tiene esquinas, no tiene
        // lechos, el acero transversal es un zuncho y no un estribo, y el hatch se
        // recorta contra coronas y no contra rectangulos redondeados. Intentar que
        // una sola rutina hiciera las dos formas llenaria de 'if (circular)' cada
        // una de las veinte etapas del dibujo rectangular, que es codigo ya probado
        // y no hay ninguna razon para arriesgarlo.
        if (s.Circular)
        {
            return DibujarCircular(s, xIzquierda, yAbajo, inicio, destino, conFondoSolido);
        }

        var b = s.BaseCm * _escala;
        var h = s.AlturaCm * _escala;
        var rec = s.RecubrimientoCm * _escala;
        var gancho = s.GanchoCm * _escala;
        var dEst = s.Estribo.Cm * _escala;

        var dSup = s.Superior.Esquina.Cm * _escala;
        var dInf = s.Inferior.Esquina.Cm * _escala;
        if (dSup <= 0) { dSup = dEst; }
        if (dInf <= 0) { dInf = dEst; }

        // ---------- Concreto ----------
        var plConcreto = Polilinea(new[]
        {
            xIzquierda,     yAbajo,
            xIzquierda + b, yAbajo,
            xIzquierda + b, yAbajo + h,
            xIzquierda,     yAbajo + h
        }, "CONCRETO");

        // ---------- Estribo ----------
        var contorno = new List<object>();
        var ganchoQuads = new List<double[]>();
        var ganchoSectores = new List<double[]>();

        var hayEstribo = dEst > 0 && rec * 2 < b && rec * 2 < h;

        if (hayEstribo)
        {
            EstriboExterior(contorno, xIzquierda, yAbajo, b, h, rec, dEst, dSup, dInf, gancho);
            EstriboInterior(contorno, xIzquierda, yAbajo, b, h, rec, dEst, dSup, dInf, gancho);

            if (gancho > 0)
            {
                Ganchos(contorno, ganchoQuads, ganchoSectores,
                    xIzquierda, yAbajo, b, h, rec, dEst, dSup, gancho);
            }
        }

        // ---------- Varillas ----------
        var circulos = new List<object>();

        var posSup = Lecho(circulos, s.Superior, s, xIzquierda, yAbajo, b, h, rec, dEst, arriba: true);
        var posInf = Lecho(circulos, s.Inferior, s, xIzquierda, yAbajo, b, h, rec, dEst, arriba: false);

        Laterales(circulos, s, xIzquierda, yAbajo, b, h, rec, dEst, dSup, dInf);

        var rellenosVarilla = new List<object>();
        RellenarVarillas(circulos, rellenosVarilla);

        // ---------- Estribo diamante ----------
        // Va DESPUES de las varillas porque se abraza a ellas: necesita saber
        // dónde quedaron las del centro de cada lecho.
        if (s.Diamante)
        {
            EstriboDiamante(s, contorno, xIzquierda, yAbajo, b, h, rec, dEst, conFondoSolido);
        }

        // ---------- Llamadas de los lechos ----------
        LeadersDeLecho(s.Superior, posSup, xIzquierda, arriba: true);
        LeadersDeLecho(s.Inferior, posInf, xIzquierda, arriba: false);

        // ---------- Hatch de concreto, AL FINAL y en dos partes ----------
        if (hayEstribo && plConcreto is not null)
        {
            HatchDeConcreto(
                plConcreto, circulos, ganchoQuads, ganchoSectores,
                xIzquierda, yAbajo, b, h, rec, dEst, dSup, dInf, conFondoSolido);
        }

        // ---------- Contornos en negro, solo en la sección rellena ----------
        if (conFondoSolido)
        {
            foreach (var ent in contorno)
            {
                Negro(ent);
            }

            // Las varillas también llevan el contorno negro. Su relleno sigue con
            // el color de su capa: lo que se pinta de negro es solo el círculo.
            foreach (var circulo in circulos)
            {
                Negro(circulo);
            }
        }

        // ---------- Contorno del estribo al frente ----------
        // Va antes de las cotas para no arrastrarlas en el reordenado.
        EstribosAlFrente(inicio, (int)AcadConnection.Retry(() => (int)_ms.Count));

        // ---------- Varillas al frente, encima del estribo ----------
        // Primero los rellenos y después los círculos, para que el contorno negro
        // no acabe por debajo de su propio relleno. Van después de los estribos a
        // propósito: así el gancho no muerde la varilla de esquina que abraza.
        var varillas = new List<object>(rellenosVarilla);
        varillas.AddRange(circulos);
        AlFrente(varillas);

        // ---------- Cotas ----------
        Cotas(xIzquierda, yAbajo, b, h);

        // ---------- Rotulo ----------
        Rotulo(s, xIzquierda + (b / 2), yAbajo - (0.06 * _f));

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        if (!string.IsNullOrWhiteSpace(s.Id))
        {
            Bloquear(s.Id, inicio, fin, destino);
        }

        return fin - inicio;
    }

    // ==================================================================
    // Hatch de concreto en dos partes
    // ==================================================================

    private void HatchDeConcreto(
        object plConcreto, List<object> circulos,
        List<double[]> ganchoQuads, List<double[]> ganchoSectores,
        double x0, double y0, double b, double h,
        double rec, double dEst, double dSup, double dInf, bool conFondoSolido)
    {
        var temporales = new List<object>();
        var creados = new List<object>();

        try
        {
            // Fronteras del estribo, con las MISMAS formulas con que se dibuja
            var rfInf = dEst + (dInf / 2);
            var rfSup = dEst + (dSup / 2);
            var rInf = dInf / 2;
            var rSup = dSup / 2;

            var x1 = x0 + rec;
            var y1 = y0 + rec;
            var x2 = x0 + b - rec;
            var y2 = y0 + h - rec;

            var bExt = PolyRectFillet(x1, y1, x2, y2, rfInf, rfSup);
            var bInt = PolyRectFillet(
                x1 + dEst - EpsHatch, y1 + dEst - EpsHatch,
                x2 - dEst + EpsHatch, y2 - dEst + EpsHatch,
                rInf, rSup);

            if (bExt is not null) { temporales.Add(bExt); }
            if (bInt is not null) { temporales.Add(bInt); }

            // ---------- PARTE 1: entre la cara del concreto y el estribo ----------
            if (bExt is not null)
            {
                ParteHatch(plConcreto, new List<object> { bExt }, creados, conFondoSolido);
            }

            // ---------- PARTE 2: dentro del estribo, varillas como islas ----------
            if (bInt is not null)
            {
                var islas = new List<object>(circulos);

                // Las colas de los ganchos solo se usan como islas cuando NO hay
                // relleno del estribo. Con relleno, este va encima y las cubre.
                if (!conFondoSolido)
                {
                    foreach (var q in ganchoQuads)
                    {
                        var pl = PolyCerrada(q);
                        if (pl is not null)
                        {
                            islas.Add(pl);
                            temporales.Add(pl);
                        }
                    }
                }

                // ---- El diamante NO puede ser isla de esta región ----
                //
                // La macro lo intenta, y aquí se copió el intento. Es imposible, y se
                // puede demostrar con dos líneas: el diamante se abraza a la varilla
                // central, que está TANGENTE a la cara interior del estribo, así que
                // el borde exterior de su cinta llega hasta
                //
                //     y_varilla + R + dDia  =  cara interior + dDia
                //
                // o sea que SIEMPRE sobresale de esta frontera, tanto como grueso
                // tenga el diamante. Una isla que cruza el contorno no es una isla, y
                // AutoCAD la rechaza con 0x80200003: Invalid input. Cada intento
                // gastaba además las tres vías de marshalling, y de ahí salían los
                // SEIS renglones de fallo que veía el usuario: dos islas imposibles
                // por tres formas de empaquetarlas.
                //
                // En la macro el rechazo pasa inadvertido porque lo traga
                // On Error Resume Next. El resultado es el mismo que aquí: el rayado
                // cruza la banda del diamante. No se nota porque el rayado se manda
                // al fondo y el diamante lleva su propio relleno encima.
                if (_diamExt is not null || _diamInt is not null)
                {
                    Nota(
                        "Estribo diamante: no se usa como isla del rayado. Su cinta " +
                        "sobresale de la cara interior del estribo justo su grueso, " +
                        "así que no es una isla válida y AutoCAD la rechaza. El dibujo " +
                        "no cambia: el rayado va al fondo y el diamante lleva su " +
                        "relleno encima.");
                }

                ParteHatch(bInt, islas, creados, conFondoSolido);
            }

            // ---------- Relleno solido del estribo, solo tipo 1 ----------
            if (conFondoSolido && bExt is not null && bInt is not null)
            {
                RellenoEstribo(bExt, bInt, ganchoQuads, ganchoSectores, creados, temporales);
            }

            // Al fondo, conservando el orden relativo
            AlFondo(creados);

            if (creados.Count == 0)
            {
                _log.Add(
                    "Hatch de concreto: no se creo NINGUN hatch. " +
                    $"Fronteras: exterior={(bExt is null ? "nula" : "ok")}, " +
                    $"interior={(bInt is null ? "nula" : "ok")}.");
            }
        }
        catch (Exception ex)
        {
            // El hatch es decorativo: si algo falla, la geometria ya esta dibujada.
            Fallo("Hatch de concreto", ex);
        }
        finally
        {
            // Las fronteras eran auxiliares. Los hatches son NO asociativos, asi
            // que borrarlas no los afecta.
            foreach (var t in temporales)
            {
                Borrar(t);
            }
        }
    }

    /// <summary>Una parte del hatch: fondo sólido opcional más el patrón AR-CONC.</summary>
    private void ParteHatch(object exterior, List<object> islas, List<object> creados, bool conFondo)
    {
        // El fondo va PRIMERO para que el patron quede encima
        if (conFondo)
        {
            var fondo = Hatch("SOLID", 1, exterior, islas, "CONCRETO", ColorFondo);
            if (fondo is not null)
            {
                creados.Add(fondo);
            }
        }

        // La escala del patrón es la MISMA en los dos tipos de sección: lo único
        // que cambia entre tipo 1 y tipo 2 es el fondo sólido, no el rayado.
        var patron = Hatch(PatronConcreto, EscalaHatchEfectiva, exterior, islas, "CONCRETO", ColorPatron)
                     ?? Hatch(PatronRespaldo, EscalaHatchEfectiva, exterior, islas, "CONCRETO", ColorPatron);

        if (patron is not null)
        {
            creados.Add(patron);
        }
    }

    /// <summary>Relleno sólido del estribo: cuerpo, doblez de los ganchos y colas.</summary>
    private void RellenoEstribo(
        object bExt, object bInt,
        List<double[]> quads, List<double[]> sectores,
        List<object> creados, List<object> temporales)
    {
        // 1) cuerpo: el anillo entre las dos fronteras
        var cuerpo = Hatch("SOLID", 1, bExt, new List<object> { bInt },
            "ESTRIBOS", ColorRellenoEstribo);
        if (cuerpo is not null)
        {
            creados.Add(cuerpo);
        }

        // 2) doblez de cada gancho: sector anular
        foreach (var s in sectores)
        {
            var pl = SectorAnular(s[0], s[1], s[2], s[3], s[4], s[5]);
            if (pl is null)
            {
                continue;
            }

            temporales.Add(pl);
            var hs = Hatch("SOLID", 1, pl, null, "ESTRIBOS", ColorRellenoEstribo);
            if (hs is not null)
            {
                creados.Add(hs);
            }
        }

        // 3) colas: lo que sobresale del estribo
        foreach (var q in quads)
        {
            var pl = PolyCerrada(q);
            if (pl is null)
            {
                continue;
            }

            temporales.Add(pl);
            var hq = Hatch("SOLID", 1, pl, null, "ESTRIBOS", ColorRellenoEstribo);
            if (hq is not null)
            {
                creados.Add(hq);
            }
        }
    }

    private object? Hatch(
        string patron, double escala, object exterior, List<object>? islas,
        string capa, int colorAci)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic h = _ms.AddHatch(0, patron, false);
                h.HatchStyle = 0;                       // acHatchStyleNormal

                var frontera = ConArregloDeEntidades(
                    $"AppendOuterLoop del hatch '{patron}'",
                    new[] { exterior },
                    arr => { h.AppendOuterLoop(arr); });

                if (!frontera)
                {
                    // Se BORRA el hatch fallido, igual que la macro. Si se deja, en
                    // el dibujo queda una entidad degenerada, invisible y sin
                    // extensión, que después hace fallar a GetBoundingBox y con ello
                    // al agrupado en bloques de toda la sección.
                    Borrar((object)h);
                    return null;
                }

                if (islas is not null)
                {
                    var fallidas = 0;

                    for (var i = 0; i < islas.Count; i++)
                    {
                        var isla = islas[i];

                        // Una isla que falla no invalida el resto del hatch. Y se
                        // pasa el número de lazos para no duplicarla: si la llamada
                        // reporta error pero el lazo ya entró, reintentar la
                        // agregaría dos veces y con estilo Normal se anularían.
                        var antes = Lazos(h);

                        var ok = AcadArreglos.Llamar(
                            $"AppendInnerLoop del hatch '{patron}' ({Que(isla)})",
                            new[] { isla },
                            arr => { h.AppendInnerLoop(arr); },
                            Fallo, Nota,
                            yaSurtioEfecto: () =>
                            {
                                var ahora = Lazos(h);
                                return antes >= 0 && ahora > antes;
                            });

                        if (!ok)
                        {
                            fallidas++;
                        }
                    }

                    if (fallidas > 0)
                    {
                        // Se explica la consecuencia real, que es ninguna: el rayado
                        // va al fondo y la varilla lleva su relleno encima, así que
                        // una isla que falta no se ve en el dibujo.
                        Nota(
                            $"Hatch '{patron}': {fallidas} de {islas.Count} islas no " +
                            "entraron. El dibujo no cambia, porque el rayado se manda " +
                            "al fondo y el acero queda encima.");
                    }
                }

                if (!patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    h.PatternScale = escala;
                }

                // Capa y color ANTES del Evaluate y otra vez despues: si el
                // Evaluate deja el objeto en un estado raro, la primera
                // asignacion se pierde en silencio.
                h.Layer = capa;
                h.Color = colorAci;
                h.Evaluate();
                h.Layer = capa;
                h.Color = colorAci;

                return (object?)h;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}' en la capa {capa}", ex);
            return null;
        }
    }

    /// <summary>
    /// Sube al frente el contorno de la capa ESTRIBOS de la sección recién dibujada.
    /// </summary>
    /// <remarks>
    /// Es el <c>EstribosAlFrenteEn</c> de la macro. Hace falta porque el relleno
    /// sólido del estribo se manda al fondo junto con el hatch de concreto, y sin
    /// esto el contorno negro puede quedar por debajo de su propio relleno y
    /// desaparecer a trozos. Los <b>hatches se excluyen</b>: si subiera también el
    /// relleno, taparía las varillas donde el gancho abraza la de esquina, que es
    /// justo lo que advierte la macro con <c>RELLENO_ESTRIBO_AL_FRENTE = False</c>.
    /// </remarks>
    /// <summary>
    /// Sube al frente TODO el rotulado del dibujo: leaders, flechas y textos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el <c>SubirRotulosAlFrente</c> de la macro, y <b>faltaba</b>. Sin él las
    /// varillas y el estribo tapan las flechas y las líneas guía, porque se dibujan
    /// después: en el dibujo se veían las flechitas mordidas por los círculos de las
    /// varillas.
    /// </para>
    /// <para>
    /// Se llama <b>una vez al final</b>, cuando ya están todas las secciones, y es
    /// deliberadamente lo ÚLTIMO en subir: así el rotulado queda por encima incluso
    /// del contorno de los estribos, que también se sube. Recorre el espacio modelo
    /// completo porque el rotulado no entra en los bloques de las secciones y por
    /// tanto vive suelto ahí.
    /// </para>
    /// </remarks>
    /// <summary>
    /// X donde empezar a dibujar: después del último bloque que ya haya en el dibujo.
    /// </summary>
    /// <remarks>
    /// Es el <c>ObtenerPosicionInicialX</c> de la macro, y <b>faltaba</b>. Sin esto,
    /// cada corrida empezaba en <c>x = 0</c> y las secciones nuevas caían encima de
    /// las que ya estaban dibujadas. Se mira el punto de inserción de las
    /// referencias de bloque, igual que la macro, y se deja 0.7 de aire.
    /// </remarks>
    public double PosicionInicialX()
    {
        try
        {
            var maxX = 0d;

            AcadConnection.Retry(() =>
            {
                var total = (int)_ms.Count;

                for (var i = 0; i < total; i++)
                {
                    dynamic ent = _ms.Item(i);

                    string nombre = ent.ObjectName;
                    if (!nombre.Contains("blockreference", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var p = ADobles(ent.InsertionPoint);
                    if (p.Length > 0 && p[0] > maxX)
                    {
                        maxX = p[0];
                    }
                }
            });

            return maxX > 0 ? maxX + 0.7 : 0;
        }
        catch (Exception ex)
        {
            Fallo("Buscar donde empezar a dibujar", ex);
            return 0;
        }
    }

    public void RotulosAlFrente()
    {
        try
        {
            var rotulado = new List<object>();

            AcadConnection.Retry(() =>
            {
                var total = (int)_ms.Count;

                for (var i = 0; i < total; i++)
                {
                    dynamic ent = _ms.Item(i);

                    string capa = ent.Layer;
                    if (string.Equals(capa, "ROTULOS", StringComparison.OrdinalIgnoreCase))
                    {
                        rotulado.Add((object)ent);
                    }
                }
            });

            AlFrente(rotulado);
        }
        catch (Exception ex)
        {
            Fallo("Subir el rotulado al frente", ex);
        }
    }

    private void EstribosAlFrente(int inicio, int fin)
    {
        try
        {
            var contorno = new List<object>();

            AcadConnection.Retry(() =>
            {
                for (var i = inicio; i < fin; i++)
                {
                    dynamic ent = _ms.Item(i);

                    string capa = ent.Layer;
                    if (!string.Equals(capa, "ESTRIBOS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string nombre = ent.ObjectName;
                    if (nombre.Contains("hatch", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    contorno.Add((object)ent);
                }
            });

            AlFrente(contorno);
        }
        catch (Exception)
        {
            // El reordenado es estético.
        }
    }

    private void AlFrente(List<object> objetos)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = _ms.GetExtensionDictionary;
                dynamic tabla;
                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                ConArregloParaOrdenar("MoveToTop", objetos,
                    arr => { tabla.MoveToTop(arr); });
            });
        }
        catch (Exception)
        {
            // Sin reordenar el dibujo sigue completo.
        }
    }

    /// <summary>
    /// Qué es una entidad, para que el diagnóstico diga QUÉ isla falló y no solo que
    /// falló alguna.
    /// </summary>
    private static string Que(object ent)
    {
        try
        {
            dynamic e = ent;
            string nombre = e.ObjectName;
            string capa = e.Layer;

            // AcDbCircle -> Circle, AcDbPolyline -> Polyline
            var corto = nombre.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase)
                ? nombre[4..]
                : nombre;

            return $"{corto} en {capa}";
        }
        catch (Exception)
        {
            return "entidad desconocida";
        }
    }

    /// <summary>
    /// Cuántos lazos tiene el hatch, o <c>-1</c> si no se puede saber.
    /// </summary>
    /// <remarks>
    /// Sirve para detectar si un <c>AppendInnerLoop</c> tuvo efecto aunque reportara
    /// error, y así no reintentarlo y duplicar la isla.
    /// </remarks>
    private static int Lazos(dynamic hatch)
    {
        try
        {
            return (int)hatch.NumberOfLoops;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private void AlFondo(List<object> objetos)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = _ms.GetExtensionDictionary;
                dynamic tabla;
                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                ConArregloParaOrdenar("MoveToBottom", objetos,
                    arr => { tabla.MoveToBottom(arr); });
            });
        }
        catch (Exception)
        {
            // Sin reordenar tampoco se tapa nada: las varillas son islas del hatch.
        }
    }

    // ==================================================================
    // Estribo
    // ==================================================================

    private void EstriboExterior(
        List<object> contorno,
        double x0, double y0, double b, double h, double rec,
        double dEst, double dSup, double dInf, double gancho)
    {
        var rfSup = dEst + (dSup / 2);
        var rfInf = dEst + (dInf / 2);

        var x1 = x0 + rec;
        var y1 = y0 + rec;
        var x2 = x0 + b - rec;
        var y2 = y0 + h - rec;

        Horizontal(contorno, x1 + rfInf, x2 - rfInf, y1);
        Vertical(contorno, y1 + rfInf, y2 - rfSup, x2);
        Horizontal(contorno, x1 + rfSup, x2 - rfSup, y2);
        Vertical(contorno, y1 + rfInf, y2 - rfSup, x1);

        Agregar(contorno, Arco(x2 - rfInf, y1 + rfInf, rfInf, 1.5 * Pi, 2 * Pi));
        Agregar(contorno, Arco(x1 + rfInf, y1 + rfInf, rfInf, Pi, 1.5 * Pi));
        Agregar(contorno, Arco(x1 + rfSup, y2 - rfSup, rfSup, 0.5 * Pi, Pi));

        Agregar(contorno, gancho > 0
            ? Arco(x2 - rfSup, y2 - rfSup, rfSup, 1.75 * Pi, 0.5 * Pi)
            : Arco(x2 - rfSup, y2 - rfSup, rfSup, 0, 0.5 * Pi));
    }

    private void EstriboInterior(
        List<object> contorno,
        double x0, double y0, double b, double h, double rec,
        double dEst, double dSup, double dInf, double gancho)
    {
        var rSup = dSup / 2;
        var rInf = dInf / 2;

        var x1 = x0 + rec + dEst;
        var y1 = y0 + rec + dEst;
        var x2 = x0 + b - rec - dEst;
        var y2 = y0 + h - rec - dEst;

        if (x2 <= x1 || y2 <= y1)
        {
            return;
        }

        var yFinDer = y2 - rSup;
        if (gancho > 0)
        {
            var rOut = rSup + dEst;
            var tCruce = rOut - (Rt2 * rSup);
            if (tCruce >= 0 && tCruce <= gancho)
            {
                var yTrim = y2 - (Rt2 * rOut);
                if (yTrim > y1 + rInf)
                {
                    yFinDer = yTrim;
                }
            }
        }

        Horizontal(contorno, x1 + rInf, x2 - rInf, y1);
        Vertical(contorno, y1 + rInf, yFinDer, x2);
        Horizontal(contorno, x1 + rSup, x2 - rSup, y2);
        Vertical(contorno, y1 + rInf, y2 - rSup, x1);

        Agregar(contorno, Arco(x2 - rInf, y1 + rInf, rInf, 1.5 * Pi, 2 * Pi));
        Agregar(contorno, Arco(x1 + rInf, y1 + rInf, rInf, Pi, 1.5 * Pi));
        Agregar(contorno, Arco(x1 + rSup, y2 - rSup, rSup, 0.5 * Pi, Pi));

        Agregar(contorno, gancho > 0
            ? Arco(x2 - rSup, y2 - rSup, rSup, 1.75 * Pi, 0.75 * Pi)
            : Arco(x2 - rSup, y2 - rSup, rSup, 0, 0.5 * Pi));
    }

    private void Ganchos(
        List<object> contorno, List<double[]> quads, List<double[]> sectores,
        double x0, double y0, double b, double h, double rec,
        double dEst, double dSup, double gancho)
    {
        var rIn = dSup / 2;
        var rOut = rIn + dEst;

        var bx = x0 + b - rec - dEst - rIn;
        var by = y0 + h - rec - dEst - rIn;

        // Doblez: sector anular con los mismos radios y angulos de los arcos
        sectores.Add(new[] { bx, by, rIn, rOut, 1.75 * Pi, 0.75 * Pi });

        const double ux = -Rt2I;
        const double uy = -Rt2I;

        Cola(contorno, quads, bx, by, rIn, rOut, Rt2I, -Rt2I, ux, uy, gancho, false, 0, 0);

        var tCruce = rOut - (Rt2 * rIn);
        var recortar = gancho > 0 && tCruce >= 0 && tCruce <= gancho;

        Cola(contorno, quads, bx, by, rIn, rOut, -Rt2I, Rt2I, ux, uy, gancho,
            recortar, bx + rIn - (Rt2 * rOut), by + rIn);
    }

    private void Cola(
        List<object> contorno, List<double[]> quads,
        double bx, double by, double rIn, double rOut,
        double nx, double ny, double ux, double uy, double largo,
        bool recortar, double xIni, double yIni,
        bool sinLineaInterior = false)
    {
        var piX = bx + (rIn * nx);
        var piY = by + (rIn * ny);
        var poX = bx + (rOut * nx);
        var poY = by + (rOut * ny);

        var qiX = piX + (largo * ux);
        var qiY = piY + (largo * uy);
        var qoX = poX + (largo * ux);
        var qoY = poY + (largo * uy);

        if (recortar)
        {
            poX = xIni;
            poY = yIni;
        }

        // La línea de la cara que da a la varilla puede no dibujarse. La usa el gancho
        // del diamante: allí el doblez se lee como una pieza que pasa POR ENCIMA de la
        // varilla, y esa línea, que nace pegada al acero de la varilla, cortaba el
        // doblez por dentro y rompía esa lectura.
        if (!sinLineaInterior)
        {
            Agregar(contorno, Linea(piX, piY, qiX, qiY, "ESTRIBOS"));
        }

        Agregar(contorno, Linea(poX, poY, qoX, qoY, "ESTRIBOS"));
        Agregar(contorno, Linea(qiX, qiY, qoX, qoY, "ESTRIBOS"));

        // El cuadrilátero para rellenar la cola va INFLADO, no con los cuatro
        // puntos crudos. Antes se guardaban crudos y el resultado eran costuras
        // blancas entre la cola y el doblez, y una cuña sin rellenar en la cola
        // recortada. Son las dos razones que explica la macro:
        //
        //   * Cola NO recortada: arranca justo en el borde radial del sector del
        //     doblez, así que basta un traslape de micras. Alargarla más sacaría
        //     un piquito por fuera de la cara del estribo.
        //   * Cola RECORTADA: su línea exterior arranca sobre la línea interior
        //     del estribo, que no es radial, y entre las dos queda una cuña. Se
        //     alarga hacia atrás el espesor del estribo, que siempre cae dentro
        //     del acero de la esquina y por tanto no se ve.
        var wx = poX - piX;
        var wy = poY - piY;
        var wl = Math.Sqrt((wx * wx) + (wy * wy));

        if (wl < 1e-9)
        {
            return;
        }

        wx /= wl;
        wy /= wl;

        var solape = SolapeGancho * _f;
        var espesor = rOut - rIn;

        var atras = recortar ? (espesor > 0 ? espesor : solape) : solape;
        var adelante = solape;
        var lado = solape;

        quads.Add(new[]
        {
            piX - (ux * atras)    - (wx * lado), piY - (uy * atras)    - (wy * lado),
            qiX + (ux * adelante) - (wx * lado), qiY + (uy * adelante) - (wy * lado),
            qoX + (ux * adelante) + (wx * lado), qoY + (uy * adelante) + (wy * lado),
            poX - (ux * atras)    + (wx * lado), poY - (uy * atras)    + (wy * lado)
        });
    }

    // ==================================================================
    // Varillas
    // ==================================================================

    /// <returns>
    /// Las X de las varillas de esquina y de las intermedias, y la elevación del
    /// lecho. Hacen falta para dibujar las llamadas.
    /// </returns>
    private (double[] Esquina, double[] Intermedia, double Y) Lecho(
        List<object> circulos, LechoCad lecho, SeccionCad s,
        double x0, double y0, double b, double h, double rec, double dEst, bool arriba)
    {
        var p = PosicionesDeLecho(lecho, x0, y0, b, h, rec, dEst, arriba);

        foreach (var x in p.Esquina)
        {
            var rr = lecho.Esquina.Cm * _escala / 2;
            Agregar(circulos, Varilla(x, p.YEsquina, rr, lecho.Esquina.Clave));
            (arriba ? _varSup : _varInf).Add((x, p.YEsquina, rr));
        }

        foreach (var x in p.Intermedia)
        {
            var rr = lecho.Intermedia.Cm * _escala / 2;
            Agregar(circulos, Varilla(x, p.YIntermedia, rr, lecho.Intermedia.Clave));
            (arriba ? _varSup : _varInf).Add((x, p.YIntermedia, rr));
        }

        return (p.Esquina, p.Intermedia, p.YGrupo);
    }

    /// <summary>
    /// <b>Dónde</b> van las varillas de un lecho, sin dibujar nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está separado del dibujo porque hace falta <b>dos veces</b>: al dibujar la
    /// sección, y otra vez al rehacer sus llamadas junto al bloque que el alzado
    /// inserta a su lado. Ver <see cref="LlamadasJuntoAlBloque"/>. Antes el cálculo
    /// estaba dentro de <see cref="Lecho"/>, mezclado con la creación de los círculos,
    /// así que la única forma de recuperar las posiciones era dibujar las varillas otra
    /// vez encima.
    /// </para>
    /// <para>
    /// Devuelve <b>las dos Y</b> además de la del grupo. Cuando la varilla de esquina y
    /// la intermedia son de distinto diámetro, sus centros no están a la misma altura
    /// —el reparto es desde la cara, así que media diferencia de diámetro las
    /// separa— y para dibujarlas hace falta cada una. <c>YGrupo</c> es la que usan las
    /// llamadas, y es la de las intermedias cuando existen, igual que en la macro, donde
    /// <c>ySup</c> termina valiendo la de la última fila dibujada.
    /// </para>
    /// </remarks>
    private (double[] Esquina, double YEsquina, double[] Intermedia, double YIntermedia,
        double YGrupo) PosicionesDeLecho(
        LechoCad lecho,
        double x0, double y0, double b, double h, double rec, double dEst, bool arriba)
    {
        var xsEsquina = Array.Empty<double>();
        var xsIntermedia = Array.Empty<double>();
        var yEsquina = 0d;
        var yIntermedia = 0d;
        var yGrupo = 0d;

        if (lecho.NEsquina > 0 && lecho.Esquina.Existe)
        {
            var d = lecho.Esquina.Cm * _escala;
            var off = rec + dEst + (d / 2);
            yEsquina = arriba ? y0 + h - off : y0 + off;
            yGrupo = yEsquina;

            var xs = new List<double>();

            if (lecho.NEsquina == 1)
            {
                xs.Add(x0 + (b / 2));
            }
            else
            {
                var paso = (b - (2 * off)) / (lecho.NEsquina - 1);
                for (var i = 0; i < lecho.NEsquina; i++)
                {
                    xs.Add(x0 + off + (i * paso));
                }
            }

            xsEsquina = xs.ToArray();
        }

        if (lecho.NIntermedia > 0 && lecho.Intermedia.Existe)
        {
            var d = lecho.Intermedia.Cm * _escala;
            var off = rec + dEst + (d / 2);
            yIntermedia = arriba ? y0 + h - off : y0 + off;
            yGrupo = yIntermedia;

            var xs = new List<double>();

            if (lecho.NIntermedia == 1)
            {
                xs.Add(x0 + (b / 2));
            }
            else
            {
                var xIni = x0 + off;
                var xFin = x0 + b - off;
                var paso = (xFin - xIni) / (lecho.NIntermedia + 1);
                for (var i = 1; i <= lecho.NIntermedia; i++)
                {
                    xs.Add(xIni + (i * paso));
                }
            }

            xsIntermedia = xs.ToArray();
        }

        return (xsEsquina, yEsquina, xsIntermedia, yIntermedia, yGrupo);
    }

    /// <summary>
    /// <b>Dónde</b> van las varillas laterales, sin dibujar nada.
    /// </summary>
    /// <remarks>Separado de <see cref="Laterales"/> por lo mismo que
    /// <see cref="PosicionesDeLecho"/>.</remarks>
    private List<(double XIzq, double XDer, double Y)> PosicionesLaterales(
        SeccionCad s, double x0, double y0, double b, double h,
        double rec, double dEst, double dSup, double dInf)
    {
        var salida = new List<(double XIzq, double XDer, double Y)>();

        if (s.NLateral <= 0 || !s.Lateral.Existe)
        {
            return salida;
        }

        var d = s.Lateral.Cm * _escala;
        var offSup = rec + dEst + (dSup / 2);
        var offInf = rec + dEst + (dInf / 2);
        var offLado = rec + dEst + (d / 2);

        var hueco = h - offSup - offInf;
        var paso = s.NLateral > 1 ? hueco / (s.NLateral + 1) : hueco / 2;

        for (var i = 1; i <= s.NLateral; i++)
        {
            salida.Add((x0 + offLado, x0 + b - offLado, y0 + offInf + (i * paso)));
        }

        return salida;
    }

    /// <summary>
    /// Llamadas de un lecho, con la misma regla de agrupado que la macro.
    /// </summary>
    /// <remarks>
    /// Si las varillas de esquina y las intermedias son del <b>mismo diámetro</b>,
    /// se rotulan juntas en una sola llamada con el total. Si son distintas, van dos
    /// llamadas separadas y escalonadas, para que los textos no se encimen.
    /// </remarks>
    private void LeadersDeLecho(
        LechoCad lecho, (double[] Esquina, double[] Intermedia, double Y) pos,
        double xIzquierda, bool arriba)
    {
        var claveEsq = lecho.Esquina.Clave;
        var claveInt = lecho.Intermedia.Clave;

        var mismoDiametro = string.Equals(claveEsq, claveInt, StringComparison.OrdinalIgnoreCase);

        if (mismoDiametro && pos.Esquina.Length > 0 && pos.Intermedia.Length > 0)
        {
            var todas = pos.Esquina.Concat(pos.Intermedia).ToArray();

            LeaderLecho(
                todas, pos.Y, lecho.NEsquina + lecho.NIntermedia, claveEsq,
                xIzquierda, indiceGrupo: 0, arriba: arriba, esIntermedia: false);

            return;
        }

        if (pos.Esquina.Length > 0)
        {
            LeaderLecho(
                pos.Esquina, pos.Y, lecho.NEsquina, claveEsq,
                xIzquierda, indiceGrupo: 0, arriba: arriba, esIntermedia: false);
        }

        if (pos.Intermedia.Length > 0)
        {
            LeaderLecho(
                pos.Intermedia, pos.Y, lecho.NIntermedia, claveInt,
                xIzquierda, indiceGrupo: 1, arriba: arriba, esIntermedia: true);
        }
    }

    private void Laterales(
        List<object> circulos, SeccionCad s,
        double x0, double y0, double b, double h, double rec, double dEst,
        double dSup, double dInf)
    {
        if (s.NLateral <= 0 || !s.Lateral.Existe)
        {
            return;
        }

        var d = s.Lateral.Cm * _escala;

        foreach (var (xIzq, xDer, y) in
                 PosicionesLaterales(s, x0, y0, b, h, rec, dEst, dSup, dInf))
        {
            Agregar(circulos, Varilla(xIzq, y, d / 2, s.Lateral.Clave));
            Agregar(circulos, Varilla(xDer, y, d / 2, s.Lateral.Clave));

            // Se anotan para que el estribo diamante las rodee en lugar de
            // atravesarlas. Ver RodearLaterales.
            _varLat.Add((xIzq, y, d / 2));
            _varLat.Add((xDer, y, d / 2));

            // Una llamada por varilla, y dice "2 vars." porque rotula la pareja
            // de los dos costados. Es lo que hace la macro.
            LeaderVarilla(xIzq, y, 2, s.Lateral.Clave, x0);
            LeaderVarilla(xDer, y, 2, s.Lateral.Clave, x0);
        }
    }

    private object? Varilla(double cx, double cy, double radio, string clave)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _ms.AddCircle(new[] { cx, cy, 0d }, radio);
                c.Layer = "VAR_" + clave;
                c.Color = PorCapa;
                return (object?)c;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <param name="rellenos">Se le agregan los hatches creados, para reordenarlos.</param>
    private void RellenarVarillas(List<object> circulos, List<object> rellenos)
    {
        foreach (var circulo in circulos)
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic c = circulo;
                    dynamic h = _ms.AddHatch(0, "SOLID", false);

                    var ok = ConArregloDeEntidades(
                        "AppendOuterLoop del relleno de la varilla",
                        new[] { circulo },
                        arr => { h.AppendOuterLoop(arr); });

                    if (!ok)
                    {
                        Borrar((object)h);
                        return;
                    }

                    h.Layer = c.Layer;
                    h.Color = PorCapa;
                    h.Evaluate();
                    rellenos.Add((object)h);
                });
            }
            catch (Exception ex)
            {
                // Estetico: sin relleno la varilla queda como circulo.
                Fallo("Relleno solido de la varilla", ex);
            }
        }
    }

    // ==================================================================
    // Llamadas (leaders) de los lechos
    // ==================================================================

    /// <summary>
    /// Llamada de un lecho: espina horizontal, una línea con flecha por varilla y
    /// el texto <c>N vars. #X C</c>.
    /// </summary>
    /// <param name="xs">Posición X de cada varilla del grupo.</param>
    /// <param name="yVarilla">Elevación del eje de las varillas.</param>
    /// <param name="indiceGrupo">
    /// 0 para el primer grupo del lecho, 1 para el segundo. Separa las llamadas
    /// para que no se encimen cuando el lecho tiene dos diámetros distintos.
    /// </param>
    /// <param name="esIntermedia">
    /// Cambia el sentido de la flecha y sube un poco la llamada. En el lecho
    /// superior las varillas intermedias van al mismo nivel que las de esquina, así
    /// que sin esto las dos llamadas se pisarían.
    /// </param>
    private void LeaderLecho(
        double[] xs, double yVarilla, int cantidad, string diametro,
        double xIzquierdaSeccion, int indiceGrupo, bool arriba, bool esIntermedia)
    {
        if (xs.Length == 0 || cantidad <= 0 || string.IsNullOrWhiteSpace(diametro))
        {
            return;
        }

        var offsetSup = arriba && esIntermedia ? OffsetIntermediaSup * _f : 0;

        var yBase = arriba
            ? yVarilla - (LineaVerticalDist * _f) + (indiceGrupo * LechoSepY * _f) + offsetSup
            : yVarilla - (LineaVerticalDist * _f) - (indiceGrupo * LechoSepY * _f);

        var xTexto = xIzquierdaSeccion - (0.02 * _f) - (indiceGrupo * LechoSepX * _f);

        // Espina: del texto hasta la varilla más a la derecha del grupo
        Rotulado(Linea(xTexto, yBase, xs.Max(), yBase, "ROTULOS"));

        foreach (var x in xs)
        {
            Rotulado(Linea(x, yBase, x, yVarilla, "ROTULOS"));

            // La flecha apunta a la varilla. En las intermedias del lecho superior
            // se voltea, porque la llamada queda por encima de la varilla.
            FlechaTriangular(x, yVarilla, haciaArriba: arriba && esIntermedia);
        }

        TextoLeader(xTexto, yBase, $"{cantidad} vars. {diametro}C");
    }

    /// <summary>
    /// Llamada de una varilla suelta: vertical con flecha, tramo horizontal y texto.
    /// Es la que usa la macro para las varillas laterales.
    /// </summary>
    private void LeaderVarilla(
        double x, double y, int cantidad, string diametro, double xIzquierdaSeccion)
    {
        if (cantidad <= 0 || string.IsNullOrWhiteSpace(diametro))
        {
            return;
        }

        var yAbajo = y - (LineaVerticalDist * _f);
        var xTexto = xIzquierdaSeccion - (0.02 * _f);

        Rotulado(Linea(x, y, x, yAbajo, "ROTULOS"));
        FlechaTriangular(x, y, haciaArriba: false);
        Rotulado(Linea(x, yAbajo, xTexto, yAbajo, "ROTULOS"));

        TextoLeader(xTexto, yAbajo, $"{cantidad} vars. {diametro}C");
    }

    /// <summary>Flecha triangular equilátera, rellena, con el vértice en la varilla.</summary>
    private void FlechaTriangular(double x, double y, bool haciaArriba)
    {
        var lado = TamFlecha * _f * 2;
        var alto = lado * Math.Sqrt(3) / 2;
        var yBase = haciaArriba ? y + alto : y - alto;

        var pl = PolyCerrada(new[]
        {
            x,               y,
            x + (lado / 2),  yBase,
            x - (lado / 2),  yBase
        });

        if (pl is null)
        {
            return;
        }

        Rotulado(pl);

        // Relleno sólido: sin él la flecha se ve hueca
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic h = _ms.AddHatch(0, "SOLID", false);

                var ok = ConArregloDeEntidades(
                    "AppendOuterLoop de la flecha de la llamada",
                    new[] { pl },
                    arr => { h.AppendOuterLoop(arr); });

                if (!ok)
                {
                    Borrar((object)h);
                    return;
                }

                h.Layer = "ROTULOS";
                h.Color = PorCapa;
                h.Evaluate();
            });
        }
        catch (Exception)
        {
            // Sin relleno la flecha queda como contorno.
        }
    }

    /// <param name="haciaLaDerecha">
    /// El texto crece hacia la <b>derecha</b> del punto, así que la línea de llamada
    /// sale por su lado <b>izquierdo</b>.
    /// </param>
    /// <remarks>
    /// El anclaje por omisión es <c>MiddleRight</c>, que es el
    /// <c>ATTACH_MIDDLE_RIGHT</c> de la macro y lo correcto en las llamadas de lecho:
    /// ahí el texto va a la izquierda de la sección y la línea entra por su derecha.
    /// <para>
    /// En la llamada del círculo hace falta lo contrario. Con el anclaje a la derecha
    /// el texto se extendía hacia la izquierda y la línea salía pegada a su última
    /// letra, que es el defecto que se veía en el plano.
    /// </para>
    /// </remarks>
    private void TextoLeader(double x, double y, string texto, bool haciaLaDerecha = false)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { x, y, 0d }, 1.0 * _f, texto);
                mt.Height = AlturaTextoLeader * _f;

                // 4 = acAttachmentPointMiddleLeft, 6 = acAttachmentPointMiddleRight
                mt.AttachmentPoint = haciaLaDerecha ? 4 : 6;
                mt.InsertionPoint = new[] { x, y, 0d };
                mt.Layer = "ROTULOS";
                mt.Color = PorCapa;
                mt.StyleName = EstiloTexto;
                mt.Update();
            });
        }
        catch (Exception)
        {
            // Sin el texto la llamada queda muda, pero el dibujo sigue siendo válido.
        }
    }

    /// <summary>
    /// Deja una entidad de rotulado con capa ROTULOS y todo <b>por capa</b>.
    /// </summary>
    /// <remarks>
    /// Es el <c>AplicarPropiedadesRotulo</c> de la macro. Importa que el color sea
    /// por capa: así la capa ROTULOS se ve verde en el Model y se imprime en negro
    /// sin tocar cada objeto.
    /// </remarks>
    private void Rotulado(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic e = ent;
                e.Layer = "ROTULOS";
                e.Color = PorCapa;
                e.Linetype = "ByLayer";
                e.Lineweight = -1;        // acLnWtByLayer
            });
        }
        catch (Exception)
        {
            // Si no se puede uniformar, la entidad ya está dibujada.
        }
    }

    // ==================================================================
    // Cotas
    // ==================================================================

    private void Cotas(double x0, double y0, double b, double h)
    {
        var off = 3 * 0.02 * _f;

        // Se reaplican las variables y se refresca el estilo ANTES de cada par de
        // cotas, igual que la macro, que llama a ConfigurarVariablesDeCota dentro de
        // ColocarCotasParaConcreto y no una sola vez al principio. No es un adorno:
        // AddDimRotated toma el estilo activo del documento en ese instante, y entre
        // una sección y la siguiente el dibujo pudo cambiarlo.
        ConfigurarCotas();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dh = _ms.AddDimRotated(
                    new[] { x0, y0 + h, 0d },
                    new[] { x0 + b, y0 + h, 0d },
                    new[] { x0 + (b / 2), y0 + h + off, 0d },
                    0d);
                FormatearCota(dh);
            });

            AcadConnection.Retry(() =>
            {
                dynamic dv = _ms.AddDimRotated(
                    new[] { x0 + b, y0, 0d },
                    new[] { x0 + b, y0 + h, 0d },
                    new[] { x0 + b + off, y0 + (h / 2), 0d },
                    Pi / 2);

                // El TextRotation a 0 va DESPUES de formatear: el estilo lo
                // reajusta y si no, el texto de la cota vertical sale girado.
                FormatearCota(dv);
                dv.TextRotation = 0d;
                dv.Update();
            });
        }
        catch (Exception ex)
        {
            // Sin cotas el dibujo sigue siendo valido.
            Fallo("Cotas", ex);
        }
    }

    // ==================================================================
    // Rotulo
    // ==================================================================

    private void Rotulo(SeccionCad s, double xCentro, double yBase)
    {
        var lineas = new List<string>
        {
            s.Elemento.ToUpperInvariant(),
            "\"" + s.Id + "\""
        };

        foreach (var g in VarillasPorDiametro(s))
        {
            lineas.Add($"{g.Value} vars. {g.Key}C");
        }

        var sep = Separaciones(s.Separacion, s.Estribo.Clave);

        if (s.Estribo.Existe)
        {
            // En la seccion redonda el acero transversal NO es un estribo: es un
            // zuncho, y ademas hay que decir si sube en helice o son anillos, porque
            // son dos formas de armar distintas y el fierrero necesita saber cual.
            if (s.Circular)
            {
                var forma = s.ZunchoHelicoidal ? "helicoidal" : "en anillos";
                lineas.Add($"Zuncho {forma} {s.Estribo.Clave} @{sep} cm");
            }
            else
            {
                lineas.Add($"Estr. {s.Estribo.Clave} @{sep} cm");
            }
        }

        // Renglón del estribo diamante, con la MISMA separación que el principal.
        // Faltaba: el diamante se dibujaba pero no se rotulaba, así que el plano no
        // decía qué varilla llevaba.
        //
        // En la seccion circular no aplica: el diamante es un rombo entre las
        // varillas de dos lechos, y en un circulo no hay lechos ni esquinas.
        if (s.Diamante && !s.Circular)
        {
            var clave = s.EstriboDiamanteVar.Existe
                ? s.EstriboDiamanteVar.Clave
                : s.Estribo.Clave;

            if (!string.IsNullOrWhiteSpace(clave))
            {
                lineas.Add($"Est. Diamante {clave} @{sep} cm");
            }
        }

        lineas.Add($"Rec. {s.RecubrimientoCm:0.##} cm");

        if (!string.IsNullOrWhiteSpace(s.Fc))
        {
            // El superindice va literal, como el Chr$(178) de la macro
            lineas.Add($"f'c={s.Fc} kg/cm\u00B2");
        }

        if (!string.IsNullOrWhiteSpace(s.Escala))
        {
            lineas.Add($"Escala 1:{s.Escala}");
        }

        var texto = string.Join("\\P", lineas);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { xCentro, yBase, 0d }, 0.45 * _f, texto);
                mt.Height = 0.03 * _f;
                mt.AttachmentPoint = 2;   // acAttachmentPointTopCenter
                mt.InsertionPoint = new[] { xCentro, yBase, 0d };
                mt.Layer = "ROTULOS";
                mt.Color = PorCapa;
                mt.StyleName = EstiloTexto;
                mt.Update();
            });
        }
        catch (Exception ex)
        {
            // Sin rotulo el dibujo sigue siendo valido.
            Fallo("Rotulo", ex);
        }
    }

    /// <summary>
    /// Da formato a la separación de estribos, que puede venir como una lista tipo
    /// <c>5-10-15</c>.
    /// </summary>
    /// <remarks>
    /// Es el <c>ParsearSeparaciones</c> de la macro. Cada tramo se formatea por
    /// separado, la coma decimal se acepta como punto y se quitan los ceros que no
    /// aportan, para que <c>10.00</c> salga como <c>10</c> y no desalinee el rótulo.
    /// </remarks>
    private static string Separaciones(string? separacion, string claveEstribo)
    {
        var s = (separacion ?? string.Empty).Trim();

        if (s.Length == 0)
        {
            return claveEstribo;
        }

        var partes = s.Replace(',', '.').Split('-');

        for (var i = 0; i < partes.Length; i++)
        {
            var p = partes[i].Trim();

            partes[i] = double.TryParse(
                p, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : p;
        }

        return string.Join("-", partes);
    }

    private static IEnumerable<KeyValuePair<string, int>> VarillasPorDiametro(SeccionCad s)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Sumar(VarCad v, int n)
        {
            if (n <= 0 || !v.Existe || string.IsNullOrWhiteSpace(v.Clave))
            {
                return;
            }

            d[v.Clave] = d.TryGetValue(v.Clave, out var actual) ? actual + n : n;
        }

        if (s.Circular)
        {
            // En la seccion redonda hay UN solo grupo: el circulo de varillas. Sumar
            // aqui los lechos dejaria el rotulo diciendo varillas que no se
            // dibujaron, porque el dibujo circular no los usa.
            Sumar(s.VarTotal, s.NVarTotal);
        }
        else
        {
            Sumar(s.Superior.Esquina, s.Superior.NEsquina);
            Sumar(s.Superior.Intermedia, s.Superior.NIntermedia);
            Sumar(s.Inferior.Esquina, s.Inferior.NEsquina);
            Sumar(s.Inferior.Intermedia, s.Inferior.NIntermedia);
            Sumar(s.Lateral, s.NLateral * 2);
        }

        return d.OrderByDescending(p => Numero(p.Key));

        static double Numero(string clave) =>
            double.TryParse(clave.TrimStart('#'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ==================================================================
    // Bloque
    // ==================================================================

    /// <summary>
    /// ¿La sección ya está dibujada, o sea, ya existe su bloque en el dibujo?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el <c>BloqueYaExiste</c> de la macro, y decide si la sección <b>se salta
    /// por completo</b>. La macro lo hace así por una razón de trabajo real: el
    /// ingeniero llena la hoja, dibuja, <b>mueve las secciones a mano</b> para
    /// acomodarlas en el plano, y luego agrega dos secciones nuevas y vuelve a
    /// dibujar. Si el programa redibujara todo, tiraría el acomodo del plano.
    /// </para>
    /// <para>
    /// Se comprueba contra la <b>tabla de bloques</b> y no contra lo que haya
    /// insertado, a propósito: aunque el usuario borre la sección del dibujo, su
    /// definición sigue ahí y la macro la sigue considerando hecha. Para redibujar
    /// una sección hay que purgar su bloque, igual que en la macro.
    /// </para>
    /// </remarks>
    public bool BloqueYaExiste(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return false;
        }

        try
        {
            return AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Blocks.Item(nombre);
                    return true;
                }
                catch (Exception)
                {
                    // No existe: es el caso normal de una sección nueva.
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            // Si no se puede consultar la tabla, se prefiere DIBUJAR: dejar de
            // dibujar por una duda es peor que dibujar una sección repetida.
            Fallo($"Consultar si el bloque '{nombre}' ya existe", ex);
            return false;
        }
    }

    /// <summary>
    /// Punto donde está insertada la sección en el dibujo, o <c>null</c> si no está.
    /// </summary>
    /// <remarks>
    /// Es la mitad de <c>ActualizarSecciones</c>: la macro <b>guarda el punto de
    /// inserción anterior</b> y redibuja ahí mismo, para que el acomodo que el
    /// ingeniero hizo a mano en el plano no se pierda al actualizar el armado.
    /// </remarks>
    public double[]? PuntoDeInsercion(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<double[]?>(() =>
            {
                var total = (int)_ms.Count;

                for (var i = 0; i < total; i++)
                {
                    dynamic ent = _ms.Item(i);

                    if (!EsReferenciaDe(ent, nombre))
                    {
                        continue;
                    }

                    var p = (double[])ent.InsertionPoint;
                    return new[] { p[0], p[1], p.Length > 2 ? p[2] : 0d };
                }

                return null;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Buscar dónde está insertada la sección '{nombre}'", ex);
            return null;
        }
    }

    /// <summary>¿Es una inserción del bloque <paramref name="nombre"/>?</summary>
    /// <remarks>
    /// Se pregunta primero por <c>ObjectName</c> porque solo las referencias de
    /// bloque tienen <c>Name</c> con ese significado. Preguntar <c>Name</c> a secas
    /// a cualquier entidad lanza excepción en unas y devuelve otra cosa en otras.
    /// </remarks>
    private static bool EsReferenciaDe(dynamic ent, string nombre)
    {
        try
        {
            string clase = ent.ObjectName;

            if (!clase.Contains("BlockReference", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suNombre = ent.Name;
            return string.Equals(suNombre, nombre, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Una entidad que no contesta simplemente no es la que se busca.
            return false;
        }
    }

    /// <summary>
    /// Borra la sección del dibujo: sus inserciones y la definición del bloque.
    /// </summary>
    /// <remarks>
    /// Las inserciones van <b>primero</b>: AutoCAD no deja borrar la definición de un
    /// bloque que todavía tiene referencias, así que al revés fallaría y quedaría un
    /// bloque viejo que haría que la sección se siguiera saltando.
    /// </remarks>
    public bool BorrarSeccion(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return false;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                // Se juntan primero y se borran después: borrar mientras se recorre
                // corre los índices y deja inserciones sin tocar.
                var refs = new List<object>();
                var total = (int)_ms.Count;

                for (var i = 0; i < total; i++)
                {
                    dynamic ent = _ms.Item(i);
                    if (EsReferenciaDe(ent, nombre))
                    {
                        refs.Add((object)ent);
                    }
                }

                foreach (dynamic r in refs)
                {
                    try
                    {
                        r.Delete();
                    }
                    catch (Exception)
                    {
                        // Si una no se puede borrar, se sigue con las demás.
                    }
                }

                dynamic def = _doc.Blocks.Item(nombre);
                def.Delete();
            });

            return true;
        }
        catch (Exception ex)
        {
            Fallo($"Borrar la sección '{nombre}' para redibujarla", ex);
            return false;
        }
    }

    private void Bloquear(string nombre, int inicio, int fin, double[]? destino)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                var objetos = new List<object>();
                double xMin = double.MaxValue, yMin = double.MaxValue;
                double xMax = double.MinValue, yMax = double.MinValue;

                for (var i = inicio; i < fin; i++)
                {
                    dynamic ent = _ms.Item(i);

                    // Las cotas y el rotulado NO entran al bloque, igual que en la
                    // macro. Son dos motivos distintos y los dos importan:
                    //
                    //   * El origen del bloque es el centro de la geometría de la
                    //     sección. Si se midieran también las cotas y el rótulo,
                    //     que sobresalen bastante, el origen quedaría descentrado.
                    //   * Metidos en el bloque, el rótulo y las cotas dejan de
                    //     poder editarse y borrarse por capa.
                    string capa = ent.Layer;
                    if (string.Equals(capa, "COTAS", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(capa, "ROTULOS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    objetos.Add((object)ent);

                    var caja = CajaEnvolvente((object)ent);
                    if (caja is null)
                    {
                        continue;
                    }

                    var mn = caja.Value.Min;
                    var mx = caja.Value.Max;

                    if (mn[0] < xMin) { xMin = mn[0]; }
                    if (mn[1] < yMin) { yMin = mn[1]; }
                    if (mx[0] > xMax) { xMax = mx[0]; }
                    if (mx[1] > yMax) { yMax = mx[1]; }
                }

                if (objetos.Count == 0)
                {
                    return;
                }

                var origen = new[] { (xMin + xMax) / 2, (yMin + yMax) / 2, 0d };

                dynamic bloque = _doc.Blocks.Add(origen, nombre);

                var copiado = ConArregloDeEntidades(
                    $"CopyObjects de la seccion '{nombre}'",
                    objetos,
                    arr => { _doc.CopyObjects(arr, bloque); });

                // Los originales solo se borran si la copia FUNCIONÓ. Borrarlos
                // igualmente dejaría la sección sin dibujar por ningún lado.
                if (!copiado)
                {
                    return;
                }

                // El orden de dibujo hay que rehacerlo DENTRO del bloque. Faltaba, y
                // es un detalle que la macro sí hace: CopyObjects no conserva la
                // tabla de orden del espacio modelo, así que dentro del bloque el
                // hatch de concreto podía acabar ENCIMA del acero, tapándolo. Como
                // la geometría vive en el bloque una vez creado, ordenarlo solo en
                // el espacio modelo no sirve de nada.
                OrdenarDentroDelBloque(bloque);

                foreach (dynamic o in objetos)
                {
                    o.Delete();
                }

                dynamic insercion = _ms.InsertBlock(origen, nombre, 1d, 1d, 1d, 0d);

                // Al redibujar, la sección vuelve AL SITIO QUE TENÍA, no al final de
                // la fila. Es lo que hace ActualizarSecciones en la macro, y es lo
                // que permite acomodar el plano a mano una vez y no perderlo cada
                // vez que cambia un armado.
                if (destino is not null)
                {
                    insercion.InsertionPoint = destino;
                    insercion.Update();
                }
            });
        }
        catch (Exception ex)
        {
            // Si el agrupado falla, la geometria ya quedo dibujada y es usable.
            Fallo($"Agrupar la seccion '{nombre}' en un bloque", ex);
        }
    }

    // ==================================================================
    // Primitivas
    // ==================================================================

    /// <summary>
    /// Rehace el orden de dibujo dentro de la definición del bloque.
    /// </summary>
    /// <remarks>
    /// Se recorre el bloque y se clasifica por lo que es cada entidad, en lugar de
    /// arrastrar las listas del espacio modelo: después de <c>CopyObjects</c> las
    /// entidades del bloque son <b>copias nuevas</b> y las referencias anteriores ya
    /// no sirven para reordenar.
    /// <para>
    /// Queda: hatch de concreto al fondo, y el contorno de los estribos al frente.
    /// Los hatches de la capa ESTRIBOS no suben, por lo mismo que advierte la macro:
    /// el relleno taparía la varilla que el gancho abraza.
    /// </para>
    /// </remarks>
    private void OrdenarDentroDelBloque(object bloque)
    {
        try
        {
            var concreto = new List<object>();
            var contornoEstribo = new List<object>();

            AcadConnection.Retry(() =>
            {
                dynamic bd = bloque;
                var total = (int)bd.Count;

                for (var i = 0; i < total; i++)
                {
                    dynamic ent = bd.Item(i);

                    string capa = ent.Layer;
                    string nombre = ent.ObjectName;
                    var esHatch = nombre.Contains("hatch", StringComparison.OrdinalIgnoreCase);

                    if (esHatch && string.Equals(capa, "CONCRETO", StringComparison.OrdinalIgnoreCase))
                    {
                        concreto.Add((object)ent);
                    }
                    else if (!esHatch && string.Equals(capa, "ESTRIBOS", StringComparison.OrdinalIgnoreCase))
                    {
                        contornoEstribo.Add((object)ent);
                    }
                }
            });

            OrdenarEn(bloque, concreto, alFondo: true);
            OrdenarEn(bloque, contornoEstribo, alFondo: false);
        }
        catch (Exception ex)
        {
            Fallo("Orden de dibujo dentro del bloque", ex);
        }
    }

    /// <summary>Mueve entidades al fondo o al frente dentro de un contenedor.</summary>
    private void OrdenarEn(object contenedor, List<object> objetos, bool alFondo)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = ((dynamic)contenedor).GetExtensionDictionary;
                dynamic tabla;

                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                ConArregloParaOrdenar(
                    alFondo ? "MoveToBottom en el bloque" : "MoveToTop en el bloque",
                    objetos,
                    arr =>
                    {
                        if (alFondo)
                        {
                            tabla.MoveToBottom(arr);
                        }
                        else
                        {
                            tabla.MoveToTop(arr);
                        }
                    });
            });
        }
        catch (Exception ex)
        {
            Fallo("Reordenar dentro del bloque", ex);
        }
    }

    /// <summary>
    /// Caja envolvente de una entidad, o <c>null</c> si no se pudo obtener.
    /// </summary>
    /// <remarks>
    /// <b>No se puede llamar con <c>dynamic</c>.</b> <c>GetBoundingBox</c> devuelve
    /// sus dos resultados en parámetros <b>por referencia</b>, y el enlace dinámico
    /// de C# no los sabe manejar sobre un objeto COM: la llamada revienta. Es el
    /// mismo problema que ya obligó a usar <c>Type.InvokeMember</c> con
    /// <see cref="ParameterModifier"/> para la API de ETABS; aquí se había quedado
    /// escrito con <c>dynamic</c> y el resultado era que <b>fallaba el agrupado en
    /// bloques de todas las secciones</b>, porque el centroide se calcula con esta
    /// caja.
    /// </remarks>
    private (double[] Min, double[] Max)? CajaEnvolvente(object ent)
    {
        try
        {
            var args = new object?[] { null, null };

            var mod = new ParameterModifier(2);
            mod[0] = true;
            mod[1] = true;

            ent.GetType().InvokeMember(
                "GetBoundingBox",
                BindingFlags.InvokeMethod,
                binder: null,
                target: ent,
                args: args,
                modifiers: new[] { mod },
                culture: null,
                namedParameters: null);

            var mn = ADobles(args[0]);
            var mx = ADobles(args[1]);

            if (mn.Length < 2 || mx.Length < 2)
            {
                return null;
            }

            return (mn, mx);
        }
        catch (Exception ex)
        {
            Fallo("Caja envolvente de la entidad (GetBoundingBox)", ex);
            return null;
        }
    }

    private static double[] ADobles(object? v) => v switch
    {
        double[] d => d,
        object[] o => o.Select(x => x is null ? 0d : Convert.ToDouble(x)).ToArray(),
        _ => Array.Empty<double>()
    };

    /// <summary>
    /// Dibuja un tramo <b>horizontal</b> del estribo y lo anota como recortable.
    /// </summary>
    /// <remarks>
    /// Todos los tramos rectos del estribo pasan por aquí para que el recorte bajo el
    /// diamante tenga la lista completa. Si un tramo se dibujara con
    /// <c>Linea</c> a secas, el diamante lo cruzaría sin recortarlo y el defecto
    /// volvería, pero solo en ese lado de la sección: el peor tipo de error, porque
    /// parece aleatorio.
    /// </remarks>
    private void Horizontal(List<object> contorno, double xa, double xb, double y)
    {
        Tramo(contorno, Linea(xa, y, xb, y, "ESTRIBOS"), horizontal: true, y, xa, xb);
    }

    /// <summary>Dibuja un tramo <b>vertical</b> del estribo y lo anota. Ver <see cref="Horizontal"/>.</summary>
    private void Vertical(List<object> contorno, double ya, double yb, double x)
    {
        Tramo(contorno, Linea(x, ya, x, yb, "ESTRIBOS"), horizontal: false, x, ya, yb);
    }

    private void Tramo(
        List<object> contorno, object? ent, bool horizontal,
        double fijo, double a, double b)
    {
        Agregar(contorno, ent);

        if (ent is null)
        {
            return;
        }

        _tramosEstribo.Add(new TramoEstribo
        {
            Ent = ent,
            Horizontal = horizontal,
            Fijo = fijo,

            // Se normaliza el sentido: recortar es más simple si A < B siempre.
            A = Math.Min(a, b),
            B = Math.Max(a, b)
        });
    }

    private static void Agregar(List<object> lista, object? o)
    {
        if (o is not null)
        {
            lista.Add(o);
        }
    }

    private object? Linea(double xa, double ya, double xb, double yb, string capa)
    {
        if (Math.Abs(xb - xa) < 1e-9 && Math.Abs(yb - ya) < 1e-9)
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
        catch (Exception)
        {
            return null;
        }
    }

    private object? Arco(double cx, double cy, double radio, double a0, double a1)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic a = _ms.AddArc(new[] { cx, cy, 0d }, radio, a0, a1);
                a.Layer = "ESTRIBOS";
                a.Color = PorCapa;
                return (object?)a;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private object? Polilinea(double[] puntos, string capa)
    {
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
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Rectángulo cerrado con esquinas redondeadas, como frontera del hatch.</summary>
    private object? PolyRectFillet(
        double x1, double y1, double x2, double y2, double rInf, double rSup)
    {
        if (x2 - x1 <= 0 || y2 - y1 <= 0)
        {
            return null;
        }

        var rMax = 0.49 * Math.Min(x2 - x1, y2 - y1);
        if (rInf > rMax) { rInf = rMax; }
        if (rSup > rMax) { rSup = rMax; }
        if (rInf < 1e-7) { rInf = 1e-7; }
        if (rSup < 1e-7) { rSup = 1e-7; }

        var pts = new[]
        {
            x1 + rInf, y1,
            x2 - rInf, y1,
            x2,        y1 + rInf,
            x2,        y2 - rSup,
            x2 - rSup, y2,
            x1 + rSup, y2,
            x1,        y2 - rSup,
            x1,        y1 + rInf
        };

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(pts);
                pl.Closed = true;
                pl.SetBulge(1, Bulge90);   // esquina inferior derecha
                pl.SetBulge(3, Bulge90);   // superior derecha
                pl.SetBulge(5, Bulge90);   // superior izquierda
                pl.SetBulge(7, Bulge90);   // inferior izquierda
                pl.Layer = "CONCRETO";
                pl.Color = PorCapa;
                pl.Update();
                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Frontera redondeada del estribo (PolyRectFillet)", ex);
            return null;
        }
    }

    /// <summary>Polilínea cerrada desde un arreglo plano x1,y1,x2,y2,...</summary>
    private object? PolyCerrada(double[] pts)
    {
        if (pts.Length < 6)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(pts);
                pl.Closed = true;
                pl.Layer = "ESTRIBOS";
                pl.Color = PorCapa;
                return (object?)pl;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Sector anular cerrado: dos arcos y dos tramos radiales.</summary>
    private object? SectorAnular(
        double cx, double cy, double rIn, double rOut, double a0, double a1)
    {
        if (rIn <= 0 || rOut <= rIn)
        {
            return null;
        }

        var sw = a1 - a0;
        while (sw <= 0) { sw += 2 * Pi; }
        while (sw > 2 * Pi) { sw -= 2 * Pi; }
        if (sw < 1e-6)
        {
            return null;
        }

        var bulge = Math.Tan(sw / 4);

        var pts = new[]
        {
            cx + (rIn * Math.Cos(a0)),  cy + (rIn * Math.Sin(a0)),
            cx + (rIn * Math.Cos(a1)),  cy + (rIn * Math.Sin(a1)),
            cx + (rOut * Math.Cos(a1)), cy + (rOut * Math.Sin(a1)),
            cx + (rOut * Math.Cos(a0)), cy + (rOut * Math.Sin(a0))
        };

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(pts);
                pl.Closed = true;
                pl.SetBulge(0, bulge);    // arco interior
                pl.SetBulge(2, -bulge);   // arco exterior, de regreso
                pl.Layer = "ESTRIBOS";
                pl.Update();
                return (object?)pl;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Color negro real por objeto, sin tocar el color de la capa.</summary>
    /// <summary>
    /// Pone una entidad en negro, con <b>color verdadero</b> RGB(0,0,0).
    /// </summary>
    /// <remarks>
    /// No se usa el índice ACI 7 y es a propósito, igual que en la macro: el ACI 7
    /// se dibuja <b>blanco</b> cuando el fondo del Model es oscuro, que es como
    /// trabaja casi todo el mundo. Con color verdadero se ve negro en los dos
    /// casos. El ACI 7 queda solo como último recurso.
    /// <para>
    /// El objeto de color se crea <b>una sola vez</b> y se reutiliza. Crearlo por
    /// cada línea y arco era lento y fallaba de vez en cuando, y ahí era donde a la
    /// macro se le quedaban secciones con el color de la capa.
    /// </para>
    /// </remarks>
    private void Negro(object ent)
    {
        try
        {
            var negro = ColorNegro(ent);

            AcadConnection.Retry(() =>
            {
                dynamic e = ent;
                e.Lineweight = -1;        // acLnWtByLayer

                if (negro is not null)
                {
                    e.TrueColor = negro;
                    return;
                }

                // Ultimo recurso. Se AVISA porque no es equivalente: el ACI 7 se
                // dibuja BLANCO cuando el fondo del Model es oscuro, que es como
                // trabaja casi todo el mundo, así que el contorno que debía ser
                // negro sale blanco. Antes esto pasaba en silencio.
                e.Color = 7;
                _sinColorVerdadero = true;
            });
        }
        catch (Exception ex)
        {
            Fallo("Contorno del estribo en negro", ex);
            // El contorno queda con el color de su capa.
        }
    }

    /// <summary>
    /// Se tuvo que recurrir al ACI 7 porque no se pudo crear el color verdadero.
    /// </summary>
    private bool _sinColorVerdadero;

    /// <summary>
    /// Avisa, una sola vez, si algún contorno quedó en ACI 7 en lugar de negro.
    /// </summary>
    /// <remarks>
    /// Va como método aparte y no dentro de <see cref="Negro"/> para no repetir el
    /// mismo aviso cientos de veces, una por entidad.
    /// </remarks>
    public void RevisarColorNegro()
    {
        if (_sinColorVerdadero)
        {
            Fallo(
                "Contornos en negro: no se pudo crear el color verdadero RGB(0,0,0), " +
                "así que quedaron en ACI 7. Con el fondo del Model oscuro, el ACI 7 " +
                "se dibuja BLANCO, no negro",
                new InvalidOperationException(
                    "Ni la propiedad TrueColor de una entidad ni ningún ProgID " +
                    "AutoCAD.AcCmColor.NN entregaron un objeto de color."));
        }
    }

    /// <summary>
    /// Objeto de color verdadero negro, creado una vez y reutilizado.
    /// </summary>
    /// <remarks>
    /// <c>AcCmColor</c> lleva número de versión en su ProgID y cambia con cada
    /// AutoCAD, así que se prueba de la más nueva a la más vieja. Es la misma
    /// cascada que hace la macro en <c>NuevoAcCmColor</c>.
    /// </remarks>
    private object? ColorNegro(object? ent = null)
    {
        if (_negro is not null)
        {
            return _negro;
        }

        if (_negroIntentado)
        {
            return null;
        }

        _negroIntentado = true;

        // ---------------------------------------------------------------
        // VIA BUENA: pedirle su propio TrueColor a una entidad ya dibujada.
        // ---------------------------------------------------------------
        // Toda entidad tiene la propiedad TrueColor, y lo que devuelve ES un
        // AcCmColor. Se le cambia el RGB y se reutiliza.
        //
        // Esto sustituye a la cascada de ProgIDs de abajo, que es la que fallaba y
        // dejaba los contornos en ACI 7, o sea BLANCOS sobre fondo oscuro. El
        // ProgID de AcCmColor lleva número de versión y ese número NO es el año:
        // adivinarlo probando del 26 al 15 funciona hasta que deja de funcionar, y
        // en AutoCAD 2026 dejó de funcionar. Preguntárselo a una entidad no depende
        // de ninguna versión, porque no hay ningún nombre que acertar.
        if (ent is not null)
        {
            try
            {
                _negro = AcadConnection.Retry<object?>(() =>
                {
                    dynamic e = ent;
                    dynamic col = e.TrueColor;
                    col.SetRGB(0, 0, 0);
                    return (object?)col;
                });

                if (_negro is not null)
                {
                    Nota("Color negro: se obtuvo del TrueColor de una entidad.");
                    return _negro;
                }
            }
            catch (Exception)
            {
                // Se sigue con la cascada de ProgIDs.
            }
        }

        for (var v = 26; v >= 15; v--)
        {
            try
            {
                dynamic col = _doc.Application.GetInterfaceObject("AutoCAD.AcCmColor." + v);
                col.SetRGB(0, 0, 0);
                _negro = col;
                return _negro;
            }
            catch (Exception)
            {
                // Esa versión no está: se prueba la siguiente.
            }
        }

        try
        {
            dynamic col = _doc.Application.GetInterfaceObject("AutoCAD.AcCmColor");
            col.SetRGB(0, 0, 0);
            _negro = col;
        }
        catch (Exception)
        {
            _negro = null;
        }

        return _negro;
    }

    /// <summary>Borra una entidad auxiliar. Tolera el nulo a propósito.</summary>
    /// <remarks>
    /// Acepta <c>null</c> porque se usa para limpiar cosas que <b>pueden no haberse
    /// creado</b>: si una de las dos cintas del diamante falló, hay que borrar la
    /// otra sin tener que preguntar por cada una. Con <c>object</c> a secas el
    /// compilador avisaba con CS8604 en justo ese caso.
    /// </remarks>
    private void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic e = ent;
                e.Delete();
            });
        }
        catch (Exception)
        {
            // Si no se puede borrar, queda una polilinea auxiliar visible.
        }
    }
}
