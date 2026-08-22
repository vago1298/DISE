namespace CadLink.Cad;

/// <summary>
/// Dibuja en AutoCAD las <b>zapatas aisladas</b>: port de las macros
/// <c>ZAPATA AISLADA CENTRAL V2</c> y <c>ZAPATA AISLADA LINDERO V1</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto es un port, no un dibujo parecido.</b> La versión anterior de este archivo dibujaba
/// «una zapata»: contorno, parrillas y estribos. Le faltaba casi todo lo que hace que el plano
/// sirva —el acero de arranque del dado y de la columna, los ganchos de 15 diámetros, la unión
/// dado-columna, el bloque de la zapata, los rótulos con leader, el texto de la plantilla y el
/// modo de relleno— y lo que sí dibujaba no estaba donde la macro lo pone. Ahora cada rutina de
/// aquí es la traducción de una rutina del VBA y lleva su nombre en el comentario, para poder
/// cotejarlas una por una.
/// </para>
/// <para>
/// <b>Qué se dibuja dentro del bloque y qué fuera.</b> Igual que la macro: la geometría de la
/// elevación —concreto, plantilla, parrillas, dado, columna— se mete en un <b>bloque</b> con el
/// nombre de la zapata (<c>ZAPATA_COMO_BLOQUE</c>) y se inserta en la capa <c>BLOQUE_ZAPATA</c>.
/// Fuera del bloque quedan el terreno, las cotas, los rótulos, el texto de la plantilla y la
/// vista en planta. No es un capricho: así el plano se mueve de una pieza y las cotas siguen
/// siendo editables.
/// </para>
/// <para>
/// <b>El elemento vertical se dibuja tumbado y se rota 90°</b>, como en el VBA
/// (<c>DrawVerticalElementFromAlzados</c> + <c>RotateEntitiesRange90KeepBase</c>). Aquí no se
/// rotan entidades una por una: se rota <b>cada punto</b> al dibujarlo, con
/// <see cref="GX"/>/<see cref="GY"/>/<see cref="GA"/>. El cálculo queda idéntico al de la macro
/// —se puede leer al lado— y no hay que recorrer el dibujo después.
/// </para>
/// <para>
/// <b>Modo de relleno.</b> Es la celda <c>B3</c>/<c>S3</c> de la macro: <c>1</c> rellena la
/// sección entera —SOLID color 9 más AR-CONC a 0.0003 color 251, las varillas con el color de su
/// capa y contorno ACI 250, y los estribos en color 152— y <c>2</c> la deja como siempre, con el
/// AR-CONC a 0.0005 por capa. En la hoja es la columna «Relleno», con SI y NO.
/// </para>
/// <para>
/// <b>Enlace tardío.</b> Como el resto del proyecto: COM con <c>dynamic</c>, sin referencias a
/// DLL de Autodesk, y tolerando fallos —lo que no se pudo dibujar se apunta en
/// <see cref="Fallos"/> y el dibujo sigue—.
/// </para>
/// </remarks>
public sealed partial class ZapataDrawer
{
    private const int PorCapa = 256;

    // ==================================================================
    // Constantes de las macros. El nombre del VBA va en el comentario.
    // ==================================================================

    /// <summary><c>ARCOFFSET</c>: lo que la cápsula del estribo se sale del recubrimiento.</summary>
    private const double ArcOffset = 0.0039;

    /// <summary><c>ESTRIBOS_DADO_OFFSET_FIN</c>.</summary>
    private const double EstribosDadoOffsetFin = 0.02;

    /// <summary><c>ESTRIBOS_DADO_HOLGURA_TOPE</c>.</summary>
    private const double EstribosDadoHolguraTope = 0.002;

    /// <summary><c>ESTRIBOS_DADO_HOLGURA_GANCHO</c>.</summary>
    private const double EstribosDadoHolguraGancho = 0.004;

    /// <summary><c>HOLGURA_INTERMEDIAS_GANCHO</c>.</summary>
    private const double HolguraIntermediasGancho = 0.006;

    /// <summary><c>MIN_BARRA_RECTA_DADO</c>.</summary>
    private const double MinBarraRectaDado = 0.15;

    /// <summary><c>RELACION_DESPLAZAMIENTO</c>: alto de la zona de dobleces por su desplazamiento.</summary>
    private const double RelacionDesplazamiento = 6.0;

    /// <summary><c>DESPLAZAMIENTO_MAX</c>: más que esto y no hay unión que dibujar.</summary>
    private const double DesplazamientoMax = 0.12;

    /// <summary><c>COLUMNA_FRACCION_CORTE</c>: la columna se dibuja hasta 8/9 y se corta.</summary>
    private const double ColumnaFraccionCorte = 8.0 / 9.0;

    /// <summary><c>alturaColumnaRep</c>: el tramo de columna que se representa, en m.</summary>
    private const double AlturaColumnaRep = 0.8;

    /// <summary>Gancho de remate de las barras del dado y de la columna: la macro pasa 0.12.</summary>
    private const double GanchoRemate = 0.12;

    /// <summary>Gancho de las parrillas de la zapata: la macro pasa 0.03.</summary>
    private const double GanchoParrilla = 0.03;

    // ---- Cotas y rótulos: TODO cuelga de la ESQUINA INFERIOR DERECHA ----
    //
    // Las distancias viven en TrazoZapata, en un solo bloque y medidas todas a esa esquina, para
    // que no vuelva a pasar lo de antes: tres anclas distintas -paño izquierdo, desplante y fondo
    // de la plantilla- y el rótulo además centrado en el eje. Con eso, cambiar el ancho de una
    // zapata movía cada anotación en una dirección distinta.
    private const double CotaOffsetVert1 = TrazoZapata.AnotacionCotaVert1;
    private const double CotaOffsetVert2 = TrazoZapata.AnotacionCotaVert2;
    private const double CotaOffsetCadena = TrazoZapata.AnotacionCadena;
    private const double CotaOffsetTotal = TrazoZapata.AnotacionTotal;

    /// <summary>Separación de la cota de la pata del gancho respecto de la pata.</summary>
    /// <remarks>
    /// Los 6 cm de la macro, y ahí se quedan. Bajarlas a un renglón propio por debajo de la cota
    /// total las dejaba lejos de lo que miden y con las líneas de extensión cruzando la zapata
    /// entera.
    /// </remarks>
    private const double CotaDoblezOffset = TrazoZapata.AnotacionGancho;

    // Los offsets de rótulo de la macro -0.32, 0.41 y 0.49 desde el fondo de la zapata- YA NO
    // ESTÁN: a esa distancia el rótulo cae sobre el dibujo y sobre las cotas de anchos. Ahora el
    // renglón lo da TrazoZapata.YRotulo, 80 cm por debajo del fondo de la plantilla, igual para
    // todas las zapatas. Los saltos entre los tres renglones sí son los de la macro
    // (TrazoZapata.RotuloSalto1 y RotuloSalto2).
    private const double AltoTitulo = 0.07;
    private const double AltoSubtitulo = 0.05;
    private const double AltoEscala = 0.04;
    private const double AltoTerreno = 0.025;
    private const double AltoMtexto = 0.015;
    private const double AltoPlantilla = 0.02;
    private const double LargoFlecha = 0.014;
    private const double AnchoFlecha = 0.0042;
    private const double RotuloVertGapLeader = 0.06;

    // ---- LOS DESPLAZAMIENTOS DE LOS RÓTULOS DE PARRILLA, TAL CUAL LAS MACROS ----
    // Son números que el usuario ajustó a mano en AutoCAD hasta que los rótulos quedaron en su
    // sitio, así que se copian con su nombre y su valor y no se «redondean».
    private const double AnchoMtexto = 0.38;                            // ANCHO_MTEXT
    private const double DesplazamientoVertical = 0.0175;
    private const double DesplazamientoInferiorX = -0.4818;
    private const double DesplazamientoAmbosSentidos = -0.2;
    private const double DesplazamientoInferiorAdicional = 0.15;
    private const double DesplazamientoAmbosInferiorX = 0.09;
    private const double DesplazamientoYAmbosAnclaje = -0.024;
    private const double DesplazamientoYAmbosTexto = -0.011;
    private const double DesplazamientoInferiorSuperiorAdicional = 0.0988;
    private const double DesplazamientoParrillaInfCentrar = 0.2;
    private const double SeparacionPuntasParrillaInf = 0.15;
    private const double FraccionMaxPuntaBarraInf = 0.32;
    private const double SeparacionMinPuntas = 0.06;

    // Rótulo de la parrilla superior en el LINDERO: centrado sobre el lomo de la zapata.
    private const double LinderoRotuloSupDy = 0.23;                     // LINDERO_ROTULO_SUP_DY
    private const double LinderoRotSupFxBarra = 0.32;
    private const double LinderoRotSupFxCirc = 0.66;
    private const double LinderoRotSupGapX = 0.03;

    // Anclajes de MText de AutoCAD: 4 = MiddleLeft, 5 = MiddleCenter, 6 = MiddleRight.
    private const int AnclajeIzquierda = 4;
    private const int AnclajeCentro = 5;
    private const int AnclajeDerecha = 6;

    /// <summary><c>LINDERO_ROTULO_ELEM_DX</c>: en el lindero los rótulos van a la izquierda.</summary>
    private const double LinderoRotuloElemDx = 0.3;

    /// <summary>Vuelo de la línea del terreno: la macro dibuja de xBase−0.2 a xDer+0.2.</summary>
    private const double TerrenoVuelo = 0.2;

    // Hatches
    private const string PatronConcreto = "AR-CONC";
    private const string PatronRespaldo = "ANSI31";
    private const string PatronTerreno = "EARTH";
    private const double EscalaConcretoNormal = 0.0005;   // HATCH_ESCALA_CONCRETO
    private const double EscalaConcretoRelleno = 0.0003;  // RELLENO_HATCH_ESCALA
    private const double EscalaTerreno = 0.01;            // HATCH_ESCALA_TERRENO
    private const string TranspTerreno = "45";            // HATCH_TRANSP_TERRENO
    private const int ColorSolidoRelleno = 9;             // RELLENO_COLOR_SOLIDO
    private const int ColorPatronRelleno = 251;           // RELLENO_COLOR_CONCRETO
    private const int ColorEstriboRelleno = 152;          // RELLENO_COLOR_ESTRIBO
    private const int ColorContornoNegro = 250;           // RELLENO_CONTORNO_ACI
    private const int SegmentosArcoRelleno = 12;          // RELLENO_ARCO_SEGMENTOS

    /// <summary>Texto de la plantilla, palabra por palabra como en la macro.</summary>
    private const string TextoPlantilla = "Plantilla de concreto simple f'c: 100 kg/cm\u00B2";

    // Capas
    private const string CapaConcreto = "CONCRETO";
    private const string CapaEstribos = "ESTRIBOS";
    private const string CapaRotulos = "ROTULOS";
    private const string CapaCotas = "COTAS";
    private const string CapaLeader = "LEADER";
    private const string CapaTerreno = "TERRENO_LINEA";
    private const string CapaTerrenoHatch = "TERRENO_HATCH";
    private const string CapaPlantilla = "PLANTILLA";
    private const string CapaBloqueDado = "BLOQUE_DADO";
    private const string CapaBloqueZapata = "BLOQUE_ZAPATA";

    private const string EstiloTexto = "SECCIONES";
    private const string EstiloCota = "COTA_ESTRUCTURAL";

    private readonly dynamic _doc;
    private readonly dynamic _ms;

    /// <summary>Contenedor donde se está dibujando: el bloque de la zapata o el modelo.</summary>
    private dynamic _cont;

    private readonly Func<string?, double> _diametroCm;

    private readonly List<string> _log = new();
    private readonly List<string> _notas = new();
    private readonly HashSet<string> _capas = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dadosQueFaltan = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Modo de relleno de la sección en curso: la celda B3 de la macro.</summary>
    private bool _relleno;

    /// <summary>
    /// Los estribos de la zapata en curso, para subirlos al frente al final.
    /// </summary>
    /// <remarks>
    /// El estribo se dibuja antes que las varillas longitudinales, así que sin esto queda por debajo
    /// de ellas y en la zona de dobleces —donde todo se cruza— desaparece. Se sube con la tabla
    /// <c>ACAD_SORTENTS</c>, el mismo <i>bring to front</i> que se usa a mano en AutoCAD.
    /// </remarks>
    private readonly List<object> _estribos = new();

    // Rotación de 90° del elemento vertical.
    private bool _rot;
    private double _rx0;
    private double _ry0;

    public ZapataDrawer(dynamic doc, Func<string?, double> diametroCm)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);
        _cont = _ms;
        _diametroCm = diametroCm;

        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>
    /// Dibujar <b>todas</b> las secciones rellenas: es el «tipo 2» de la hoja.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la celda <c>B3</c>/<c>S3</c> de la macro, pero <b>una sola vez para el juego entero</b> y
    /// no por zapata. Va aquí y no en <see cref="ZapataCad"/> a propósito: el tipo de sección es
    /// una decisión de <b>presentación del plano</b> —o se entrega relleno o se sigue
    /// trabajando— y tenerla en cada renglón invitaba a un plano con la mitad de las zapatas
    /// rellenas y la otra mitad no, que no es un plano, son dos.
    /// </para>
    /// <para>
    /// Con <c>true</c>: fondo SOLID color 9, AR-CONC a 0.0003 en color 251, las varillas rellenas
    /// con el color de su capa y su contorno en negro, y los estribos en 152. Con <c>false</c>: el
    /// AR-CONC a 0.0005 y todo por capa.
    /// </para>
    /// </remarks>
    public bool SeccionRellena { get; set; }

    /// <summary>Meter la elevación en un bloque con el nombre de la zapata.</summary>
    /// <remarks><c>ZAPATA_COMO_BLOQUE</c>. En <c>false</c> se dibuja directo en el modelo.</remarks>
    public bool ZapataComoBloque { get; set; } = true;

    /// <summary>Fallos tolerados: lo que no se pudo dibujar, y por qué.</summary>
    public IReadOnlyList<string> Fallos => _log;

    /// <summary>Avisos que no son fallos pero hay que leer antes de imprimir.</summary>
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

    /// <summary>Qué se dibujó.</summary>
    public sealed class Resumen
    {
        public int Zapatas { get; set; }
        public int Rellenas { get; set; }
        public int Bloques { get; set; }
        public int DadosInsertados { get; set; }
        public int DadosDeRespaldo { get; set; }
        public int Estribos { get; set; }
        public int Cotas { get; set; }

        public override string ToString() =>
            $"{Zapatas} zapata(s), {Rellenas} con la sección rellena, {Bloques} en bloque, " +
            $"{Estribos} estribo(s), {Cotas} cota(s)";
    }

    // ======================================================================
    // Entrada
    // ======================================================================

    /// <summary>
    /// Dibuja todas las zapatas, cada una donde diga <see cref="TrazoZapata.XBase"/>.
    /// </summary>
    /// <remarks>
    /// El acomodo no se decide aquí: las centrales crecen a la derecha con
    /// <c>SEPARACION_SECCIONES = 1</c> m y el lindero arranca en −3 y crece a la izquierda con
    /// 0.8 m. Es la misma función que usa la vista previa.
    /// </remarks>
    public Resumen DibujarTodas(IReadOnlyList<ZapataCad> zapatas)
    {
        var r = new Resumen();

        AsegurarCapasBase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();

        var anchos = zapatas.Select(z => z.AnchoM).ToList();

        for (var i = 0; i < zapatas.Count; i++)
        {
            var z = zapatas[i];

            try
            {
                Dibujar(z, TrazoZapata.XBase(z.Tipo, anchos, i), r);
                r.Zapatas++;
            }
            catch (Exception ex)
            {
                Fallo($"Zapata '{z.Id}'", ex);
            }
        }

        return r;
    }

    /// <summary>Dibuja una zapata en la X que se le diga.</summary>
    public double Dibujar(ZapataCad z, double xBase)
    {
        AsegurarCapasBase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();

        Dibujar(z, xBase, new Resumen());

        return xBase + z.AnchoM;
    }

    // ======================================================================
    // Port de DibujarUnaZapataAislada / DibujarUnaZapataLindero
    // ======================================================================

    /// <summary>
    /// Una zapata completa: elevación en bloque, terreno, cotas, rótulos y planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos macros son <b>la misma</b> hasta el acomodo del dado: en la central va centrado y
    /// los rótulos del dado y de la columna salen a la derecha; en el lindero el dado va pegado al
    /// paño derecho —ese es el lindero—, los dos ganchos de arranque doblan a la izquierda
    /// (<c>DADO_GANCHOS_AMBOS_IZQUIERDA</c>), los rótulos salen a la izquierda y se saltan dos
    /// estribos del dado en lugar de uno. Por eso está en una sola función con esas ramas, y no
    /// en dos copias que se van separando.
    /// </para>
    /// <para>
    /// <b>El orden es el de la macro</b>, y es lo que hace que el dibujo se vea bien: primero los
    /// rellenos, después los contornos del concreto, luego la plantilla, luego el acero, y al
    /// final —ya fuera del bloque— el texto de la plantilla, los rótulos y las cotas.
    /// </para>
    /// </remarks>
    private void Dibujar(ZapataCad z, double xBase, Resumen r)
    {
        _relleno = SeccionRellena;

        if (_relleno)
        {
            r.Rellenas++;
        }

        var lindero = TrazoZapata.EsLindero(z.Tipo);

        var a = TrazoZapata.Colocar(z, xBase);

        var rec = z.RecM;
        var recDadoM = z.RecDadoCm * TrazoZapata.EscalaElevacion;
        var recColM = z.RecColumnaCm * TrazoZapata.EscalaElevacion;

        var anchoZapata = z.AnchoM;
        var xExtremoDer = a.XDer;
        var xCentro = xBase + (anchoZapata / 2);

        var yZapBot = a.YZapBot;
        var yZapTop = a.YZapTop;
        var yTerreno = a.YTerreno;
        var yDadoTop = a.YDadoTop;

        // alturaDadoRep = profundidad. El dado llega al desplante.
        var alturaDadoRep = z.ProfundidadM;

        var dSupDado = Diam(z.VarDadoSup);
        var dInfDado = Diam(z.VarDadoInf);
        var dEstDado = Diam(z.EstriboDado);
        var dMaxDado = Math.Max(dSupDado, dInfDado);

        // SI NO SE DIJO EL DIÁMETRO DE LAS INTERMEDIAS, SE USA EL DE LAS DE ESQUINA. Es el respaldo
        // de la macro, y FALTABA:
        //     If Len(NormalizeDiaLabel(txtIntDado)) = 0 Then txtIntDado = txtAA7
        // Sin él, una sección que declara intermedias pero no su diámetro —pasa cuando las lleva en
        // los lechos y no en la casilla de intermedias— deja el dado SIN VARILLAS INTERIORES: el
        // conteo dice que hay, el diámetro sale 0 y no se dibuja ninguna. Y el rótulo también las
        // perdía, porque se arma con el mismo dato.
        var diaIntDado = Diam(z.VarIntDado) > 0 ? z.VarIntDado : z.VarDadoSup;
        var dIntDado = Diam(diaIntDado);

        // El mismo respaldo para la columna:
        //     If Len(NormalizeDiaLabel(txtIntCol)) = 0 Then txtIntCol = txtAA5
        var diaIntCol = Diam(z.VarIntColumna) > 0 ? z.VarIntColumna : z.VarColSup;

        var dSupCol = Diam(z.VarColSup);
        var dInfCol = Diam(z.VarColInf);

        // subirGanchos = diámetro de la barra de la parrilla + el de su transversal: es lo que
        // levanta el arranque del dado para que su gancho no caiga encima de la parrilla.
        var subirGanchoDado = Diam(z.VarInf) + Diam(z.VarInfTrans);

        // ---------- El contenedor: bloque de la zapata ----------
        var nombreBloque = string.Empty;
        var usaBloque = false;

        _cont = _ms;

        if (ZapataComoBloque)
        {
            nombreBloque = NombreBloqueLibre(z.Id);

            var blk = CrearBloqueVacio(nombreBloque, xBase, yZapBot);

            if (blk is not null)
            {
                _cont = blk;
                usaBloque = true;
            }
        }

        // ---------- El terreno: FUERA del bloque ----------
        var antes = _cont;
        _cont = _ms;

        HatchTerreno(xBase, xExtremoDer, a.XDadoIzq, a.XDadoDer, yZapTop, yTerreno);
        Linea(xBase - TerrenoVuelo, yTerreno, xExtremoDer + TerrenoVuelo, yTerreno, CapaTerreno);
        Texto(xBase, yTerreno + 0.03, AltoTerreno, "Nivel del terreno", CapaRotulos,
            alineacion: Alineacion.Izquierda);

        _cont = antes;

        // ---------- 1) RELLENOS ----------
        HatchConcreto(xBase, yZapBot, anchoZapata, z.EspesorM, CapaConcreto);
        HatchConcreto(a.XDadoIzq, yZapTop, a.XDadoDer - a.XDadoIzq, yDadoTop - yZapTop, CapaConcreto);

        if (z.ColumnaDeConcreto)
        {
            HatchConcreto(a.XColIzq, yDadoTop, a.XColDer - a.XColIzq,
                AlturaColumnaRep * ColumnaFraccionCorte, CapaConcreto);
        }

        // ---------- 2) CONTORNO DEL CONCRETO ----------
        // Port de DibujarContornoZapataConDado: el lomo de la zapata NO se dibuja por debajo del
        // dado. Una línea ahí sería un plano equivocado: el dado y la zapata se cuelan juntos.
        ContornoZapataConDado(xBase, yZapBot, xExtremoDer, yZapTop, a.XDadoIzq, a.XDadoDer);

        // ---------- 3) PLANTILLA (solo la geometría) ----------
        PlantillaConcretoSimple(xBase, yZapBot, anchoZapata, TrazoZapata.PlantillaEspesor);

        // ---------- 4) ACERO DE LA ZAPATA ----------
        ParrillaZapata(xBase, yZapBot, anchoZapata, z.EspesorM, rec,
            z.VarInf, z.VarInfTrans, z.SepInfTrans, superior: false);

        if (z.DobleParrilla && Diam(z.VarSup) > 0)
        {
            ParrillaZapata(xBase, yZapBot, anchoZapata, z.EspesorM, rec,
                z.VarSup, z.VarSupTrans, z.SepSupTrans, superior: true);
        }

        // ---------- 5) EL DADO Y LA COLUMNA ----------
        var esquinasIguales =
            z.ColumnaDeConcreto
            && MismoDiametro(z.VarDadoSup, z.VarColSup)
            && MismoDiametro(z.VarDadoInf, z.VarColInf);

        var intermediasIguales = z.NIntDado > 0 && z.NIntColumna > 0;

        // Las varillas de cada elemento, en las MISMAS posiciones en las que el alzado las dibuja.
        var barrasDado = BarrasDelElemento(
            a.XDadoDer, a.XDadoDer - a.XDadoIzq, recDadoM, dSupDado, dInfDado, z.NIntDado);

        var barrasCol = BarrasDelElemento(
            a.XColDer, a.XColDer - a.XColIzq, recColM, dSupCol, dInfCol, z.NIntColumna);

        var union = PrepararUnion(
            barrasDado, barrasCol,
            dSupDado, dInfDado, dIntDado,
            z.VarDadoSup, z.VarDadoInf, diaIntDado,
            esquinasIguales, intermediasIguales);

        // EL DOBLEZ VA A 1:6 Y NO SE NEGOCIA. Lo resuelve TrazoZapata.Desplazamiento: el alto es
        // seis veces el corrimiento y lo que se acomoda es DÓNDE queda ese doblez. Si no cabe en el
        // dado, acaba dentro de la columna; y si no cabe ni así, no se dibuja y se avisa.
        //
        // Antes el alto se recortaba a lo que quedaba libre en el dado, y el doblez salía más
        // parado que el detalle: en el dibujo que se revisó, 1:3.
        var trans = TrazoZapata.Desplazamiento(union.DxMax, yZapTop, yDadoTop, recDadoM);

        var yZonaBot = trans.Cabe ? trans.YZonaBot : yDadoTop - recDadoM;
        var yDiagTop = trans.Cabe ? trans.YDiagTop : yDadoTop;

        // La varilla se mete un recubrimiento en la columna, para que se vea la continuidad. Es el
        // yZonaTop de la macro y NO más: por encima de ahí las varillas son las de la columna, que
        // dibuja su propio elemento. Pasarse de ese punto es dibujar dos veces la misma varilla.
        var yZonaTop = yDadoTop + recColM;

        // Se dibuja la transición solo si el doblez cabe a 1:6 DENTRO del dado. Si no, las varillas
        // del dado se quedan rectas y su gancho de remate se conserva, como cuando no hay unión.
        //
        // Y una condición más, que es la que evitaba el dibujo duplicado: al dado se le recortan
        // las varillas justo en yZonaBot para que la unión siga desde ahí. Si ese recorte fuera tan
        // grande que no dejara barra donde recortar, ElementoVertical lo IGNORA y dibuja la varilla
        // completa; entonces la unión la volvía a dibujar encima y salían las dos.
        var topeBarrasDado = yDadoTop - recDadoM;

        // La misma cuenta que hace ElementoVertical para decidir hasta dónde puede recortar: el
        // arranque de la varilla del dado es el desplante + su recubrimiento + lo que se le sube el
        // gancho, y hay que dejarle 2 cm. Le faltaba el recubrimiento y por eso había un margen en
        // el que la unión se dibujaba pero el recorte no llegaba a aplicarse.
        var recorteCabe = yZonaBot > yZapBot + recDadoM + subirGanchoDado + 0.02;

        var aplicarUnion = union.Activa && trans.Cabe && recorteCabe;

        var recorteDado = 0.0;

        if (aplicarUnion)
        {
            recorteDado = Math.Max((yDadoTop - recDadoM) - yZonaBot, 0);

        }
        else if (union.Activa)
        {
            // No cabe: las varillas del dado se quedan RECTAS y la columna se traslapa aparte. Es
            // mejor eso que un doblez más parado que el detalle, o metido encima de las varillas de
            // la columna, que alguien podría armar así en obra.
            Nota($"Zapata '{z.Id}': el desplazamiento de {union.DxMax:0.###} m pediría "
                 + $"{trans.Alto:0.###} m de doblez a 1:6 y en el dado solo hay "
                 + $"{Math.Max(topeBarrasDado - (yZapBot + z.EspesorM + TrazoZapata.MinBarraRectaDado), 0):0.###} m, "
                 + "así que las varillas del dado se dejan rectas y la columna se traslapa "
                 + "aparte. Sube el dado o acerca los anchos del dado y de la columna.");
        }

        // offEstribosFin del dado: con columna de concreto, 2 cm; con columna de acero hay que
        // dejar sitio al gancho de remate, que en ese caso dobla hacia afuera.
        double offEstFinDado;

        if (z.ColumnaDeConcreto)
        {
            offEstFinDado = EstribosDadoOffsetFin;
        }
        else
        {
            offEstFinDado = recDadoM + dMaxDado + (dEstDado / 2)
                            + TrazoZapata.EstriboSobresale + EstribosDadoHolguraGancho;

            var maxOff = (alturaDadoRep - z.EspesorM) * 0.5;

            if (maxOff > 0 && offEstFinDado > maxOff)
            {
                offEstFinDado = maxOff;
            }
        }

        // DADO_ESTRIBOS_OMITIR_*: el lindero se salta dos con doble parrilla, la central uno.
        var omitirEstribos = z.DobleParrilla
            ? (lindero ? 2 : 1)
            : 1;

        // Dónde empieza el acero del dado, para poder barrer después SOLO lo que se dibuje de aquí
        // en adelante: la parrilla de la zapata también está en capas VAR_ y no se toca.
        var idxAntesDado = CuentaDelContenedor();

        r.Estribos += ElementoVertical(
            x0: a.XDadoDer, y0: yZapBot, largo: alturaDadoRep,
            anchoCm: z.AnchoDadoCm, recCm: z.RecDadoCm,
            diaSup: z.VarDadoSup, diaInf: z.VarDadoInf,
            nInt: z.NIntDado, diaInt: diaIntDado,
            estrDia: z.EstriboDado, espStr: z.SepEstriboDado,
            gancho: GanchoRemate, esDado: true, subirGanchos: subirGanchoDado,
            gancho12D: true, recorteConcIni: z.EspesorM, fracCorte: 0,
            estrOmitirIni: omitirEstribos, omitGanchoIni: false,
            omitGanchoFin: aplicarUnion, ganchoIniAfuera: z.ColumnaDeConcreto ? 0 : 1,
            recorteBarrasFin: recorteDado, offEstribosFin: offEstFinDado,
            // ganchosAmbosIzq va SIEMPRE en false, también en el lindero. La regla es el TIPO
            // DE COLUMNA y nada más: con columna de concreto las dos patas doblan hacia ADENTRO
            // del núcleo -que es donde hay concreto que las reciba- y con columna de acero una
            // adentro y otra afuera. La macro V1 del lindero las mandaba las dos a la izquierda
            // por el paño del lindero, y eso dejaba una pata saliéndose del dado.
            estribosAlTope: z.ColumnaDeConcreto, ganchosAmbosIzq: false,
            // CON UNIÓN, LAS VARILLAS DEL DADO ACABAN EXACTAMENTE DONDE ARRANCAN LOS DOBLECES.
            // No basta con pedir el recorte: por debajo se le restan holguras y márgenes y quedaba
            // un tramo recto asomando justo donde empieza el 1:6.
            topeBarras: aplicarUnion ? yZonaBot : null);

        if (z.ColumnaDeConcreto)
        {
            r.Estribos += ElementoVertical(
                x0: a.XColDer, y0: yDadoTop, largo: AlturaColumnaRep,
                anchoCm: z.AnchoColumnaCm, recCm: z.RecColumnaCm,
                diaSup: z.VarColSup, diaInf: z.VarColInf,
                nInt: z.NIntColumna, diaInt: diaIntCol,
                estrDia: z.EstriboColumna, espStr: z.SepEstriboColumna,
                gancho: GanchoRemate, esDado: false, subirGanchos: 0,
                gancho12D: false, recorteConcIni: 0, fracCorte: ColumnaFraccionCorte,
                estrOmitirIni: -1, omitGanchoIni: aplicarUnion,
                omitGanchoFin: false, ganchoIniAfuera: -1,
                recorteBarrasFin: 0, offEstribosFin: -1,
                estribosAlTope: false, ganchosAmbosIzq: false);

            if (aplicarUnion)
            {
                // PRIMERO SE BARRE LA ZONA y después se dibujan los dobleces, igual que el VBA: así
                // no queda ni un tramo recto de las varillas del dado dentro del 1:6, venga de la
                // holgura que venga.
                if (idxAntesDado >= 0)
                {
                    RecortarVerticalesEnLaZona(
                        idxAntesDado, yZonaBot, yZonaTop, a.XDadoIzq, a.XDadoDer);
                }

                // El doblez acaba en yDiagTop -el 1:6- y de ahí la varilla sigue vertical.
                DibujarUnion(union, yZonaBot, yDiagTop, yZonaTop);


                if (union.SinPareja > 0)
                {
                    Nota($"Zapata '{z.Id}': {union.SinPareja} varilla(s) del dado no tienen pareja "
                         + "en la columna, así que no llevan doblez y acaban en el dado. Si tienen "
                         + "que subir, dale a la columna las mismas varillas intermedias.");
                }
            }
        }

        // ---------- LOS ESTRIBOS, AL FRENTE ----------
        // Lo último de la geometría, y por eso va aquí y no junto a donde se dibujan: el orden lo
        // decide el final, cuando ya están todas las varillas. Es el «draw order → bring to front»
        // de AutoCAD, con la misma tabla ACAD_SORTENTS que usa el alzado.
        AlFrente(_cont, _estribos);
        _estribos.Clear();

        // ---------- Se inserta el bloque de la zapata ----------
        _cont = _ms;

        if (usaBloque)
        {
            // En su sitio y sin recolocar: el bloque se creó con su punto base en (xBase, yZapBot)
            // y su geometría está en coordenadas absolutas.
            if (InsertarBloquePropio(nombreBloque, xBase, yZapBot, CapaBloqueZapata))
            {
                r.Bloques++;
            }
        }

        // ---------- Texto de la plantilla: DESPUÉS del bloque ----------
        // Va aquí y no dentro: con la sección rellena, el SOLID del bloque lo taparía.
        PlantillaTexto(xBase, yZapBot, anchoZapata, TrazoZapata.PlantillaEspesor);

        // ---------- Rótulos con leader del dado y de la columna ----------
        RotuloDelDado(z, a, lindero, diaIntDado);

        if (z.ColumnaDeConcreto)
        {
            RotuloDeLaColumna(z, a, lindero, diaIntCol);
        }

        // ---------- Cotas de los dobleces del gancho de arranque ----------
        var desfaseInf = DesfaseDeLosGanchos(z, dSupDado, dInfDado, dMaxDado, recDadoM);

        CotasDoblezGanchos(a.XDadoIzq, a.XDadoDer, yZapBot, recDadoM, subirGanchoDado,
            dSupDado, dInfDado, CotaDoblezOffset, !z.ColumnaDeConcreto, desfaseInf,
            ambosIzquierda: false, r);

        // ---------- Rótulos de las parrillas ----------
        RotuloParrillaInferior(xBase, yZapBot, anchoZapata, rec,
            z.VarInf, z.SepInf, z.VarInfTrans, z.SepInfTrans);

        if (z.DobleParrilla && Diam(z.VarSup) > 0)
        {
            if (lindero)
            {
                RotuloParrillaSuperiorLindero(xBase, yZapBot, anchoZapata, z.EspesorM, rec,
                    z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans);
            }
            else
            {
                RotuloParrillaSuperiorCentral(xBase, yZapBot, anchoZapata, z.EspesorM, rec,
                    z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans);
            }
        }

        // ---------- Cotas de anchos y verticales ----------
        CotasAnchos(xBase, xExtremoDer, a.XDadoIzq, a.XDadoDer, yZapBot, r);
        CotasVerticales(xBase, yZapBot, yZapTop, yTerreno, r);

        // ---------- Rótulo de la sección ----------
        var titulo = lindero
            ? $"ZAPATA AISLADA DE LINDERO \"{z.Id}\""
            : $"ZAPATA AISLADA CENTRAL \"{z.Id}\"";

        // EL RÓTULO, DONDE LO PONE LA MACRO: a 0.32, 0.41 y 0.49 por debajo del desplante y
        // CENTRADO en el eje de la zapata. Nada de bajarlo a un renglón propio ni de alinearlo al
        // paño derecho: las dos cosas se probaron y las dos dejaron el rótulo despegado de su
        // dibujo.
        //
        // Lo único que se le añade a la macro es el encogido: si el renglón no cabe en el hueco
        // que le toca —su zapata más los 80 cm de la fila— se le baja el alto. Y se mide con el
        // ancho de letra REAL del dibujo (FactorLetraTitulo), que es lo que faltaba: con el 0.62
        // de la macro el título nunca se encogía y por eso se encimaba con el de al lado.
        var yTitulo = TrazoZapata.YRotulo(yZapBot, 0);
        var ySubtitulo = TrazoZapata.YRotulo(yZapBot, 1);
        var yEscala = TrazoZapata.YRotulo(yZapBot, 2);
        var anchoRotulo = TrazoZapata.AnchoParaElRotulo(anchoZapata);

        Texto(xCentro, yTitulo,
            TrazoZapata.AltoQueQuepa(titulo.Length, AltoTitulo, anchoRotulo,
                TrazoZapata.FactorLetraTitulo),
            titulo, CapaRotulos, alineacion: Alineacion.Centro);

        Texto(xCentro, ySubtitulo,
            TrazoZapata.AltoQueQuepa("ELEVACION".Length, AltoSubtitulo, anchoRotulo,
                TrazoZapata.FactorLetraTitulo),
            "ELEVACION", CapaRotulos, alineacion: Alineacion.Centro);

        var fc = string.IsNullOrWhiteSpace(z.Fc) ? string.Empty : $"    f'c = {z.Fc.Trim()} kg/cm\u00B2";
        var escala = $"Rec. {z.RecM * 100:0.#} cm{fc}    Escala 1:10";

        Texto(xCentro, yEscala,
            TrazoZapata.AltoQueQuepa(escala.Length, AltoEscala, anchoRotulo,
                TrazoZapata.FactorLetraTitulo),
            escala, CapaRotulos, alineacion: Alineacion.Centro);

        // ---------- La planta ----------
        Planta(z, a, r);
    }

    /// <summary>
    /// Desfase vertical entre los dos ganchos de arranque del dado.
    /// </summary>
    /// <remarks>
    /// Port de <c>DesfaseGanchosInternos</c> y <c>DesfaseGanchosMismoLado</c>. Si las dos patas se
    /// alcanzarían dentro del dado, una se sube: en el plano se verían encimadas y en la obra no
    /// caben. En el lindero las dos doblan al mismo lado, así que la regla es la otra.
    /// </remarks>
    private double DesfaseDeLosGanchos(
        ZapataCad z, double dSup, double dInf, double dMax, double recDadoM)
    {
        var w = z.AnchoDadoCm * TrazoZapata.EscalaElevacion;
        var interior = w - (2 * recDadoM);

        if (!z.ColumnaDeConcreto)
        {
            // Los ganchos doblan hacia AFUERA: no se pueden encimar.
            return 0;
        }

        var suma = (TrazoZapata.FactorGanchoAbajo * dSup) + (TrazoZapata.FactorGanchoAbajo * dInf);

        return suma > interior - 0.02 ? (2 * dMax) + 0.005 : 0;
    }

    // ======================================================================
    // El elemento vertical: dado y columna
    // ======================================================================

    /// <summary>
    /// Port de <c>DrawVerticalElementFromAlzados</c>: el dado o la columna, con su acero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se calcula <b>tumbado</b> —el largo en X y el ancho en Y, como en el VBA— y se rota 90°
    /// sobre <paramref name="x0"/>, <paramref name="y0"/> al dibujar cada punto. Después de rotar,
    /// la barra «superior» local es la del paño <b>izquierdo</b> y la «inferior» la del paño
    /// <b>derecho</b>, que en el lindero es el lindero mismo.
    /// </para>
    /// <para>
    /// Lo que dibuja, en este orden: las dos caras del concreto y su tapa, los estribos en
    /// cápsula, las dos barras de esquina con su gancho de 15 diámetros abajo y su remate arriba,
    /// y las intermedias cortadas en cada estribo. Con <paramref name="fracCorte"/> el elemento se
    /// corta a esa fracción y se le pone la línea de rotura: es la columna, que sigue hacia
    /// arriba.
    /// </para>
    /// </remarks>
    /// <returns>Cuántos estribos se dibujaron.</returns>
    private int ElementoVertical(
        double x0, double y0, double largo, double anchoCm, double recCm,
        string? diaSup, string? diaInf, int nInt, string? diaInt,
        string? estrDia, string? espStr, double gancho, bool esDado, double subirGanchos,
        bool gancho12D, double recorteConcIni, double fracCorte,
        int estrOmitirIni, bool omitGanchoIni, bool omitGanchoFin, int ganchoIniAfuera,
        double recorteBarrasFin, double offEstribosFin, bool estribosAlTope, bool ganchosAmbosIzq,
        double? topeBarras = null)
    {
        var w = anchoCm * TrazoZapata.EscalaElevacion;
        var recM = recCm * TrazoZapata.EscalaElevacion;

        if (w <= 0 || largo <= 0)
        {
            return 0;
        }

        var xh0 = x0;
        var yh0 = y0;
        var xh1 = xh0 + largo;
        var yh1 = yh0 + w;

        // A partir de aquí TODO va en coordenadas locales y se rota al dibujar.
        _rot = true;
        _rx0 = x0;
        _ry0 = y0;

        try
        {
            var gIniAfuera = ganchoIniAfuera < 0 ? esDado : ganchoIniAfuera != 0;

            var hayCorte = fracCorte > 0 && fracCorte < 1;
            var xCut = hayCorte ? xh0 + (largo * fracCorte) : xh1;
            var xFinElem = xCut;

            if (recorteConcIni < 0)
            {
                recorteConcIni = 0;
            }

            var xc0 = xh0 + recorteConcIni;

            if (xc0 > xFinElem - 0.01)
            {
                xc0 = xh0;
            }

            // Las dos caras del concreto. El arranque se recorta el espesor de la zapata: ahí el
            // dado y la zapata son la misma pieza y la línea sobraría.
            Linea(xc0, yh1, xFinElem, yh1, CapaConcreto);
            Linea(xc0, yh0, xFinElem, yh0, CapaConcreto);

            if (Math.Abs(xc0 - xh0) < 1e-6)
            {
                Linea(xc0, yh0, xc0, yh1, CapaConcreto);
            }

            if (!hayCorte)
            {
                Linea(xh1, yh0, xh1, yh1, CapaConcreto);
            }

            var dSup = Diam(diaSup);
            var dInf = Diam(diaInf);
            var dInt = Diam(diaInt);
            var dE = Diam(estrDia);
            var dMaxCara = Math.Max(dSup, dInf);

            var ycSup = yh1 - recM - (dSup / 2);
            var ycInf = yh0 + recM + (dInf / 2);

            var tramos = TrazoZapata.TramosCm(espStr);

            // offEstribosFin: negativo = el de siempre (STIRRUP_EDGE_OFFSET).
            var offFin = offEstribosFin < 0 ? TrazoZapata.EstriboRetiroBorde : offEstribosFin;
            var forzarFin = false;

            if (esDado && !hayCorte && estribosAlTope)
            {
                forzarFin = true;

                var offMin = TrazoZapata.EstriboSobresale + (dE / 2) + EstribosDadoHolguraTope;

                if (offFin < offMin)
                {
                    offFin = offMin;
                }
            }

            double[] centros;

            if (esDado)
            {
                centros = TrazoZapata.CentrosEstribos(
                    largo, tramos[0], tramos[1], tramos[2],
                    TrazoZapata.EstriboRetiroBorde, offFin, forzarFin);
            }
            else
            {
                // La columna va con separación ÚNICA, la más cerrada de la celda: es lo que hace
                // BuildStirrupCentersUniforme con SeparacionMinima.
                centros = TrazoZapata.CentrosUniformes(
                    largo, TrazoZapata.SeparacionMinimaCm(tramos),
                    TrazoZapata.EstriboRetiroBorde, offFin);
            }

            // Los centros vienen medidos desde 0: se corren al sitio del elemento.
            for (var i = 0; i < centros.Length; i++)
            {
                centros[i] += xh0;
            }

            TrazoZapata.Sobresalir(centros);

            if (esDado)
            {
                var n = estrOmitirIni < 0 ? 0 : estrOmitirIni;
                centros = TrazoZapata.QuitarPrimeros(centros, n);
            }

            if (hayCorte)
            {
                centros = centros.Where(c => c <= xCut - dE).ToArray();
            }

            CapsulasDeEstribo(centros, yh0, yh1, recM, dE);

            var xa = xh0 + recM;
            var xb = xh1 - recM;
            var xaBot = xa + subirGanchos;

            if (xaBot > xb - 0.01)
            {
                xaBot = xa;
            }

            double xbBar;

            if (hayCorte)
            {
                xbBar = xCut;
            }
            else
            {
                xbBar = xb;

                // EL RECORTE SE APLICA SIEMPRE, RECORTADO SI HACE FALTA, PERO NUNCA SE IGNORA.
                // Antes, si el recorte no dejaba al menos 2 cm de barra, se descartaba entero y la
                // varilla salía COMPLETA. Y cuando el recorte viene de la zona de dobleces, eso
                // significa la varilla completa MÁS el doblez encima: la misma varilla dos veces.
                // Ahora, si no cabe entero, se aplica lo que quepa; y si no cabe nada, el que pidió
                // el recorte se enterará porque la barra llega hasta arriba, no porque aparezca
                // duplicada.
                if (recorteBarrasFin > 0)
                {
                    var maximo = Math.Max(xb - (xaBot + 0.02), 0);
                    xbBar = xb - Math.Min(recorteBarrasFin, maximo);
                }

                // Y SI SE PIDIÓ UN TOPE EXACTO, MANDA ESE. Es el caso del dado cuando lleva la zona
                // de dobleces: sus varillas tienen que acabar JUSTO donde arrancan los dobleces, sin
                // los 2 cm de margen ni las holguras del gancho que se aplican más abajo. Cualquier
                // milímetro de más es un tramo recto asomando por debajo del 1:6.
                if (topeBarras is { } tope)
                {
                    xbBar = x0 + (tope - y0);
                }
            }

            var gAbSup = gancho12D ? TrazoZapata.FactorGanchoAbajo * dSup : gancho;
            var gAbInf = gancho12D ? TrazoZapata.FactorGanchoAbajo * dInf : gancho;

            // Hacia dónde dobla cada gancho de arranque. bendUp local = izquierda global.
            bool bendIniSup;
            bool bendIniInf;

            // LA REGLA, en una línea: con columna de CONCRETO las dos patas doblan hacia
            // adentro del núcleo (gIniAfuera = false → la del paño izquierdo dobla a la derecha y
            // la del derecho a la izquierda); con columna de ACERO, una adentro y otra afuera.
            if (ganchosAmbosIzq)
            {
                bendIniSup = true;
                bendIniInf = true;
            }
            else
            {
                bendIniSup = gIniAfuera;
                bendIniInf = !gIniAfuera;
            }

            var desfaseIni = 0.0;

            if (!omitGanchoIni && gancho > 0)
            {
                var interior = w - (2 * recM);

                if (ganchosAmbosIzq)
                {
                    desfaseIni = gAbInf > interior - dMaxCara - 0.005
                        ? (2 * dMaxCara) + 0.005
                        : 0;
                }
                else if (!gIniAfuera)
                {
                    desfaseIni = gAbSup + gAbInf > interior - 0.02
                        ? (2 * dMaxCara) + 0.005
                        : 0;
                }
            }

            var xaBotInf = xaBot + desfaseIni;

            if (xaBotInf > xbBar - 0.01)
            {
                xaBotInf = xaBot;
            }

            var hookIniSup = 0.0;
            var hookIniInf = 0.0;
            var hookFinSup = 0.0;
            var hookFinInf = 0.0;

            if (gancho > 0)
            {
                if (!omitGanchoIni)
                {
                    hookIniSup = gAbSup;
                    hookIniInf = gAbInf;
                }

                if (!hayCorte && !omitGanchoFin)
                {
                    hookFinSup = gancho;
                    hookFinInf = gancho;
                }
            }

            // Barra del paño izquierdo global.
            BarraConGanchos(xaBot, xbBar, ycSup, dSup, CapaVar(diaSup), centros, dE,
                hookIniSup, bendIniSup, hookFinSup, false, false, false);

            // Barra del paño derecho global (en el lindero, el lindero).
            BarraConGanchos(xaBotInf, xbBar, ycInf, dInf, CapaVar(diaInf), centros, dE,
                hookIniInf, bendIniInf, hookFinInf, false, true, false);

            // Intermedias: rectas, cortadas en cada estribo.
            if (nInt > 0 && dInt > 0)
            {
                var xaIntBase = Math.Max(xaBot, xaBotInf);
                var xaInt = xaIntBase;
                var xbInt = xbBar;

                if (!omitGanchoIni && gancho > 0 && (!gIniAfuera || ganchosAmbosIzq))
                {
                    xaInt = xaIntBase + dMaxCara + HolguraIntermediasGancho;
                }

                if (!hayCorte && !omitGanchoFin && topeBarras is null)
                {
                    xbInt = xbBar - dMaxCara - HolguraIntermediasGancho;
                }

                if (xbInt <= xaInt + 0.02)
                {
                    xaInt = xaBot;
                    xbInt = xbBar;
                }

                var cerrarIzq = !omitGanchoIni;
                var cerrarDer = !hayCorte && !omitGanchoFin;

                if (esDado && !hayCorte && omitGanchoFin)
                {
                    xbInt = xbBar;
                    cerrarDer = false;
                }

                var yTopL = ycSup - (dSup / 2);
                var yBotL = ycInf + (dInf / 2);

                if (nInt == 1)
                {
                    BarraRectaSegmentada(xaInt, xbInt, (yTopL + yBotL) / 2, dInt, CapaVar(diaInt),
                        centros, dE, cerrarIzq, cerrarDer);
                }
                else
                {
                    var paso = (yTopL - yBotL) / (nInt + 1);

                    for (var k = 1; k <= nInt; k++)
                    {
                        BarraRectaSegmentada(xaInt, xbInt, yBotL + (paso * k), dInt,
                            CapaVar(diaInt), centros, dE, cerrarIzq, cerrarDer);
                    }
                }
            }

            if (hayCorte)
            {
                LineaDeRotura(xCut, yh0, yh1);
            }

            return centros.Length;
        }
        finally
        {
            _rot = false;
        }
    }

    /// <summary>Port de <c>DrawStirrupsCapsulesFront</c>: los estribos vistos de frente.</summary>
    /// <remarks>
    /// Cada estribo es una <b>cápsula</b>: dos caras rectas y dos semicírculos, que es la
    /// proyección de un estribo cerrado visto de canto. Las caras van al recubrimiento más
    /// <c>ARCOFFSET</c>, que es lo que hace que asome del acero que abraza.
    /// </remarks>
    private void CapsulasDeEstribo(
        double[] centros, double y0, double y1, double rec, double dE)
    {
        if (dE <= 0 || centros.Length == 0)
        {
            return;
        }

        var r = dE / 2;
        var yTop = y1 - rec + ArcOffset;
        var yBot = y0 + rec - ArcOffset;

        if (yTop <= yBot)
        {
            return;
        }

        foreach (var xc in centros)
        {
            // Relleno primero: así el contorno queda encima.
            if (_relleno)
            {
                RellenarQuad(xc - r, yBot, xc + r, yBot, xc + r, yTop, xc - r, yTop,
                    CapaEstribos, ColorEstriboRelleno);
                RellenarCirculo(xc, yTop, r, CapaEstribos, ColorEstriboRelleno);
                RellenarCirculo(xc, yBot, r, CapaEstribos, ColorEstriboRelleno);
            }

            var e1 = Linea(xc - r, yBot, xc - r, yTop, CapaEstribos);
            var e2 = Linea(xc + r, yBot, xc + r, yTop, CapaEstribos);

            // Las dos puntas redondeadas. En locales el arco de arriba va de 0 a π.
            var a1 = Arco(xc, yTop, r, 0, Math.PI, CapaEstribos);
            var a2 = Arco(xc, yBot, r, Math.PI, 2 * Math.PI, CapaEstribos);

            if (_relleno)
            {
                Negro(e1);
                Negro(e2);
                Negro(a1);
                Negro(a2);
            }

            // Se apuntan para subirlos al frente al final. El estribo es lo que amarra el nudo y en
            // el plano tiene que leerse por encima de las varillas: se dibuja antes que ellas, así
            // que sin esto queda tapado justo donde más se cruzan, en la zona de dobleces.
            Apuntar(_estribos, e1);
            Apuntar(_estribos, e2);
            Apuntar(_estribos, a1);
            Apuntar(_estribos, a2);
        }
    }

    /// <summary>
    /// Port de <c>DibujarBarraGanchosRapido</c>: una barra con gancho en L en cada extremo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La barra son sus <b>dos caras</b>, cortadas en cada estribo que la cruza —eso es lo que
    /// hace que el acero se lea en el plano— y cada gancho son dos arcos concéntricos al doblez
    /// más la pata recta. El radio de doblez es un diámetro, como en la obra.
    /// </para>
    /// <para>
    /// Los ganchos solo se dibujan si <b>caben</b>: con una barra más corta que dos dobleces, la
    /// macro los omite en lugar de dibujar un nudo.
    /// </para>
    /// </remarks>
    private void BarraConGanchos(
        double xL, double xR, double yc, double diaBar, string capa,
        double[] centros, double dE,
        double hookIzq, bool bendUpIzq, double hookDer, bool bendUpDer,
        bool cerrarIzq, bool cerrarDer)
    {
        if (diaBar <= 0 || xR <= xL + 1e-7)
        {
            return;
        }

        AsegurarCapaVarilla(capa);

        var r = diaBar / 2;
        var dBar = diaBar;
        var rGap = dE / 2;

        var yTop = yc + r;
        var yBot = yc - r;

        var cabe = xR - dBar - r > xL + dBar + r;
        var hayIzq = hookIzq > r && cabe;
        var hayDer = hookDer > r && cabe;

        var xTopIni = xL;
        var xBotIni = xL;
        var xTopFin = xR;
        var xBotFin = xR;

        if (hayIzq)
        {
            if (bendUpIzq)
            {
                xBotIni = xL + dBar;
                xTopIni = xL + dBar + r;
            }
            else
            {
                xTopIni = xL + dBar;
                xBotIni = xL + dBar + r;
            }
        }

        if (hayDer)
        {
            if (bendUpDer)
            {
                xBotFin = xR - dBar;
                xTopFin = xR - dBar - r;
            }
            else
            {
                xTopFin = xR - dBar;
                xBotFin = xR - dBar - r;
            }
        }

        if (_relleno)
        {
            RellenarBandaSegmentada(yc, r, capa, centros, rGap,
                Math.Max(xTopIni, xBotIni), Math.Min(xTopFin, xBotFin));

            if (hayIzq)
            {
                RellenarGanchoL(xL, yc, r, dBar, hookIzq, capa, 1, bendUpIzq ? 1 : -1);
            }

            if (hayDer)
            {
                RellenarGanchoL(xR, yc, r, dBar, hookDer, capa, -1, bendUpDer ? 1 : -1);
            }
        }

        CaraSegmentada(yTop, capa, centros, rGap, xTopIni, xTopFin);
        CaraSegmentada(yBot, capa, centros, rGap, xBotIni, xBotFin);

        var pi = Math.PI;

        // ---- extremo izquierdo ----
        if (hayIzq)
        {
            if (bendUpIzq)
            {
                Var(Arco(xL + dBar, yc + r, dBar, pi, 1.5 * pi, capa));
                Var(Arco(xL + dBar + r, yc + dBar, r, pi, 1.5 * pi, capa));

                var yTip = yc + r + hookIzq;

                Var(Linea(xL, yc + r, xL, yTip, capa));
                Var(Linea(xL + dBar, yc + dBar, xL + dBar, yTip, capa));
                Var(Linea(xL, yTip, xL + dBar, yTip, capa));
            }
            else
            {
                Var(Arco(xL + dBar, yc - r, dBar, pi / 2, pi, capa));
                Var(Arco(xL + dBar + r, yc - dBar, r, pi / 2, pi, capa));

                var yTip = yc - r - hookIzq;

                Var(Linea(xL, yc - r, xL, yTip, capa));
                Var(Linea(xL + dBar, yc - dBar, xL + dBar, yTip, capa));
                Var(Linea(xL, yTip, xL + dBar, yTip, capa));
            }
        }
        else if (cerrarIzq)
        {
            Var(Linea(xL, yBot, xL, yTop, capa));
        }

        // ---- extremo derecho ----
        if (hayDer)
        {
            if (bendUpDer)
            {
                Var(Arco(xR - dBar, yc + r, dBar, 1.5 * pi, 2 * pi, capa));
                Var(Arco(xR - dBar - r, yc + dBar, r, 1.5 * pi, 2 * pi, capa));

                var yTip = yc + r + hookDer;

                Var(Linea(xR, yc + r, xR, yTip, capa));
                Var(Linea(xR - dBar, yc + dBar, xR - dBar, yTip, capa));
                Var(Linea(xR, yTip, xR - dBar, yTip, capa));
            }
            else
            {
                Var(Arco(xR - dBar, yc - r, dBar, 0, pi / 2, capa));
                Var(Arco(xR - dBar - r, yc - dBar, r, 0, pi / 2, capa));

                var yTip = yc - r - hookDer;

                Var(Linea(xR, yc - r, xR, yTip, capa));
                Var(Linea(xR - dBar, yc - dBar, xR - dBar, yTip, capa));
                Var(Linea(xR, yTip, xR - dBar, yTip, capa));
            }
        }
        else if (cerrarDer)
        {
            Var(Linea(xR, yBot, xR, yTop, capa));
        }
    }

    /// <summary>Port de <c>DibujarCaraSegmentada</c>: una cara cortada en cada estribo.</summary>
    private void CaraSegmentada(
        double yLinea, string capa, double[] centros, double rGap, double xIni, double xFin)
    {
        if (xFin <= xIni + 1e-7)
        {
            return;
        }

        var desde = xIni;

        foreach (var c in centros)
        {
            var a = c - rGap;
            var b = c + rGap;

            if (a > xFin)
            {
                break;
            }

            if (b > desde)
            {
                if (a > desde)
                {
                    Var(Linea(desde, yLinea, Math.Min(a, xFin), yLinea, capa));
                }

                desde = b;
            }
        }

        if (xFin > desde + 1e-7)
        {
            Var(Linea(desde, yLinea, xFin, yLinea, capa));
        }
    }

    /// <summary>Port de <c>DrawBarLineTrimWithOffset</c>: una intermedia, con sus dos caras.</summary>
    private void BarraRectaSegmentada(
        double xa, double xb, double y, double diaBar, string capa,
        double[] centros, double dE, bool cerrarIzq, bool cerrarDer)
    {
        if (diaBar <= 0 || xb <= xa)
        {
            return;
        }

        AsegurarCapaVarilla(capa);

        var rGap = dE / 2;
        var r = diaBar / 2;
        var desde = xa;
        var primero = true;

        foreach (var c in centros)
        {
            var a = Math.Max(xa, c - rGap);
            var b = Math.Min(xb, c + rGap);

            if (a > desde)
            {
                Tramo(y, r, capa, desde, a, primero && cerrarIzq, false);
                primero = false;
            }

            if (b > desde)
            {
                desde = b;
            }
        }

        if (xb > desde)
        {
            Tramo(y, r, capa, desde, xb, false, cerrarDer);
        }
    }

    /// <summary>Port de <c>DrawTwoOffsetSegment</c>.</summary>
    private void Tramo(
        double y, double r, string capa, double xa, double xb, bool cerrarIzq, bool cerrarDer)
    {
        if (xb - xa <= 1e-7)
        {
            return;
        }

        if (_relleno)
        {
            RellenarQuad(xa, y - r, xb, y - r, xb, y + r, xa, y + r, capa, 0);
        }

        Var(Linea(xa, y + r, xb, y + r, capa));
        Var(Linea(xa, y - r, xb, y - r, capa));

        if (cerrarIzq)
        {
            Var(Linea(xa, y + r, xa, y - r, capa));
        }

        if (cerrarDer)
        {
            Var(Linea(xb, y + r, xb, y - r, capa));
        }
    }

    /// <summary>Port de <c>DibujarBreakLine</c>: el zigzag del corte de la columna.</summary>
    private void LineaDeRotura(double xCut, double y0, double y1)
    {
        var w = y1 - y0;

        if (w <= 0)
        {
            return;
        }

        var ext = w * 0.06;
        var paso = w * 0.1;
        var amp = w * 0.2;
        var yc = (y0 + y1) / 2;

        Polilinea(
            new[]
            {
                xCut, y0 - ext,
                xCut, yc - (1.5 * paso),
                xCut + amp, yc - (0.5 * paso),
                xCut - amp, yc + (0.5 * paso),
                xCut, yc + (1.5 * paso),
                xCut, y1 + ext
            },
            CapaConcreto, cerrada: false);
    }

    // ======================================================================
    // La parrilla de la zapata
    // ======================================================================

    /// <summary>Port de <c>DibujarParrillaZapata</c>.</summary>
    /// <remarks>
    /// La barra que corre, con su gancho a cada lado, y las transversales vistas de punta: dos en
    /// las caras y las demás repartidas con su separación. La barra de la parrilla superior lleva
    /// el gancho <b>hacia abajo</b> y la inferior hacia arriba, que es como se arma.
    /// </remarks>
    private void ParrillaZapata(
        double xBase, double yZapBot, double anchoZapata, double espZapata, double rec,
        string? numVar, string? numVarCirculos, string? sepCirculosTxt, bool superior)
    {
        var diam = Diam(numVar);

        if (diam <= 0)
        {
            Nota($"La parrilla {(superior ? "superior" : "inferior")} no tiene varilla capturada: "
                 + "no se dibujó.");
            return;
        }

        var capaVar = CapaVar(numVar);
        var capaCirc = CapaVar(numVarCirculos);

        AsegurarCapaVarilla(capaVar);
        AsegurarCapaVarilla(capaCirc);

        var diamCirc = Diam(numVarCirculos);
        var sepCirc = TrazoZapata.SeparacionM(sepCirculosTxt);

        double yBarra;
        double yCirculos;

        if (superior)
        {
            yBarra = yZapBot + espZapata - rec - (diam / 2);
            yCirculos = yBarra - (diam / 2) - (diamCirc / 2);
        }
        else
        {
            yBarra = yZapBot + rec + (diam / 2);
            yCirculos = yBarra + (diam / 2) + (diamCirc / 2);
        }

        var xCaraIzq = xBase + rec;
        var xCaraDer = xBase + anchoZapata - rec;

        BarraLongitudinalUnica(yBarra, diam, GanchoParrilla, capaVar, superior, xCaraIzq, xCaraDer);

        if (diamCirc <= 0)
        {
            return;
        }

        var xCircIzq = xCaraIzq + (diam / 2) + (diamCirc / 2);
        var xCircDer = xCaraDer - (diam / 2) - (diamCirc / 2);
        var tol = sepCirc * 0.2;

        CirculoRelleno(xCircIzq, yCirculos, diamCirc / 2, capaCirc);
        CirculoRelleno(xCircDer, yCirculos, diamCirc / 2, capaCirc);

        var x = xCircIzq + sepCirc;

        while (x < xCircDer - tol)
        {
            CirculoRelleno(x, yCirculos, diamCirc / 2, capaCirc);
            x += sepCirc;
        }
    }

    /// <summary>Port de <c>DibujarBarraLongitudinalUnica</c>.</summary>
    private void BarraLongitudinalUnica(
        double yBarra, double diam, double longGancho, string capa, bool superior,
        double xCaraIzq, double xCaraDer)
    {
        var r = diam / 2;
        var radioCentro = diam;   // (diam/2 + 1.5*diam) / 2

        var xIni = xCaraIzq + radioCentro;
        var xFin = xCaraDer - radioCentro;

        if (xFin > xIni)
        {
            if (_relleno)
            {
                RellenarQuad(xIni, yBarra - r, xFin, yBarra - r, xFin, yBarra + r, xIni,
                    yBarra + r, capa, 0);
            }

            Var(Linea(xIni, yBarra - r, xFin, yBarra - r, capa));
            Var(Linea(xIni, yBarra + r, xFin, yBarra + r, capa));
        }

        GanchoContinuo(yBarra, diam, longGancho, capa, izquierdo: true, superior, xCaraIzq);
        GanchoContinuo(yBarra, diam, longGancho, capa, izquierdo: false, superior, xCaraDer);
    }

    /// <summary>Port de <c>DibujarGanchoContinuoLimpio</c>: el gancho de la parrilla.</summary>
    /// <remarks>
    /// Es un sector anular —radio interior medio diámetro, exterior diámetro y medio— más la pata
    /// recta, tapada solo en la punta. Se dibuja el <b>arco exterior</b> y las dos caras de la
    /// pata: así el doblez se ve continuo con el tramo recto y no aparece la línea de más que
    /// tenía la versión anterior.
    /// </remarks>
    private void GanchoContinuo(
        double yBarra, double diam, double longGancho, string capa,
        bool izquierdo, bool superior, double xCaraExterior)
    {
        if (diam <= 0 || longGancho <= 0)
        {
            return;
        }

        var radioInt = diam / 2;
        var radioExt = diam + (diam / 2);
        var radioCentro = (radioInt + radioExt) / 2;
        var r = diam / 2;

        double cx;
        double xPata;

        if (izquierdo)
        {
            cx = xCaraExterior + radioCentro;
            xPata = xCaraExterior + radioExt - (diam / 2) - diam;
        }
        else
        {
            cx = xCaraExterior - radioCentro;
            xPata = xCaraExterior - radioExt + (diam / 2) + diam;
        }

        double cy;
        double yPata1;
        double yPata2;

        if (superior)
        {
            cy = yBarra - radioCentro;
            yPata1 = cy;
            yPata2 = cy - longGancho;
        }
        else
        {
            cy = yBarra + radioCentro;
            yPata1 = cy;
            yPata2 = cy + longGancho;
        }

        var pi = Math.PI;
        double angIni;
        double angFin;

        if (izquierdo)
        {
            angIni = superior ? pi / 2 : pi;
            angFin = superior ? pi : 3 * pi / 2;
        }
        else
        {
            angIni = superior ? 0 : 3 * pi / 2;
            angFin = superior ? pi / 2 : 2 * pi;
        }

        if (_relleno)
        {
            RellenarGanchoParrilla(yBarra, diam, longGancho, capa, xCaraExterior,
                izquierdo ? 1 : -1, superior ? -1 : 1);
        }

        Var(Arco(cx, cy, radioExt, angIni, angFin, capa));

        Var(Linea(xPata - r, yPata1, xPata - r, yPata2, capa));
        Var(Linea(xPata + r, yPata1, xPata + r, yPata2, capa));
        Var(Linea(xPata - r, yPata2, xPata + r, yPata2, capa));
    }

    // ======================================================================
    // La unión dado - columna
    // ======================================================================

    /// <summary>Lo que hace falta para dibujar los dobleces de la unión.</summary>
    private sealed class Union
    {
        public bool Activa { get; set; }

        /// <summary>
        /// El corrimiento más grande de todas las varillas emparejadas.
        /// </summary>
        /// <remarks>
        /// Se guarda el corrimiento y <b>no</b> el alto del doblez: el alto lo resuelve
        /// <see cref="TrazoZapata.Desplazamiento"/> a 1:6, y así no hay dos sitios donde se pueda
        /// recortar. Antes se guardaba el alto ya calculado y después se recortaba aquí, que es
        /// como el doblez acababa más parado que el detalle.
        /// </remarks>
        public double DxMax { get; set; }

        public List<(double X1, double X2, double Dia, string Capa)> Dobleces { get; } = new();

        /// <summary>
        /// Cuántas varillas del dado se quedaron <b>sin pareja</b> en la columna.
        /// </summary>
        /// <remarks>
        /// Solo para avisar. Antes se dibujaban rectas hasta el tope del dado —así lo hace la
        /// macro— y en el plano quedaban unos tramos de varilla que no van a ningún lado, entre
        /// los dobleces. Ya no se dibujan: la varilla acaba donde el dado la corta.
        /// </remarks>
        public int SinPareja { get; set; }
    }

    /// <summary>Port de <c>PrepararUnionDadoColumna</c>.</summary>
    /// <remarks>
    /// <para>
    /// Empareja cada barra del dado con la de la columna que le toca y calcula el
    /// <b>desplazamiento</b> de cada una. Solo se hace si las esquinas son del mismo diámetro: si
    /// no, no es una barra que sigue, son dos barras distintas y hay que traslaparlas, que es otro
    /// detalle.
    /// </para>
    /// <para>
    /// El emparejamiento de las intermedias es <b>uno a uno y por cercanía</b>, y eso importa: con
    /// un emparejamiento en orden, dos intermedias podían salir cruzadas en el plano.
    /// </para>
    /// </remarks>
    private Union PrepararUnion(
        TrazoZapata.BarrasElemento dado, TrazoZapata.BarrasElemento columna,
        double dSupD, double dInfD, double dIntD,
        string? diaSupD, string? diaInfD, string? diaIntD,
        bool esquinasIguales, bool intermediasIguales)
    {
        var u = new Union();

        if (!esquinasIguales)
        {
            return u;
        }

        var xEsqIzqD = dado.Izq;
        var xEsqDerD = dado.Der;
        var xIntD = dado.Intermedias;

        var xEsqIzqC = columna.Izq;
        var xEsqDerC = columna.Der;
        var xIntC = columna.Intermedias;

        if (Math.Abs(xEsqIzqC - xEsqIzqD) > TrazoZapata.DesplazamientoMax
            || Math.Abs(xEsqDerC - xEsqDerD) > TrazoZapata.DesplazamientoMax)
        {
            return u;
        }

        u.Dobleces.Add((xEsqIzqD, xEsqIzqC, dSupD, CapaVar(diaSupD)));
        u.Dobleces.Add((xEsqDerD, xEsqDerC, dInfD, CapaVar(diaInfD)));

        // EMPAREJADO EN ORDEN, NO POR CERCANÍA. Las dos listas se ordenan y se emparejan la 1ª con
        // la 1ª, la 2ª con la 2ª: así el orden se conserva y DOS BARRAS NO PUEDEN CRUZARSE, porque
        // para cruzarse tendrían que cambiar de orden entre el dado y la columna.
        //
        // El emparejado por cercanía —el de la macro y el que estaba aquí— elige en cada vuelta el
        // mejor par disponible, y eso SÍ cruza: si la 1ª del dado queda más cerca de la 2ª de la
        // columna, se lleva esa, y a la 2ª del dado le toca la 1ª. Es lo que se veía en el dibujo,
        // dos aspas en el arranque del dado.
        var ordD = xIntD.OrderBy(x => x).ToList();
        var ordC = xIntC.OrderBy(x => x).ToList();

        var pares = intermediasIguales ? Math.Min(ordD.Count, ordC.Count) : 0;

        for (var k = 0; k < pares; k++)
        {
            u.Dobleces.Add((ordD[k], ordC[k], dIntD, CapaVar(diaIntD)));
        }

        // Las del dado que se quedaron sin pareja NO SE DIBUJAN en la zona: acaban donde el dado
        // las corta. La macro las seguía recta hasta el tope del dado y eso dejaba, entre los
        // dobleces, tramos de varilla que no van a ningún lado.
        u.SinPareja = Math.Max(ordD.Count - pares, 0);

        u.DxMax = u.Dobleces.Count == 0
            ? 0
            : u.Dobleces.Max(d => Math.Abs(d.X2 - d.X1));

        u.Activa = true;

        return u;
    }

    /// <summary>
    /// Las varillas de un elemento vertical, <b>donde el alzado las dibuja</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Redondo o cuadrado, se usa el <b>mismo</b> reparto que <see cref="ElementoVertical"/>: las
    /// dos de los paños y las intermedias repartidas entre ellas. Y tiene que ser el mismo, porque
    /// si la unión partiera de otras posiciones —por ejemplo de la proyección real de las varillas
    /// de un círculo— los dobleces <b>no arrancarían encima de las varillas</b> que ya están
    /// dibujadas: se verían despegados.
    /// </para>
    /// <para>
    /// Con esto el caso que se pidió sale solo: dado y columna circulares con las <b>mismas</b>
    /// varillas dan el mismo número de posiciones en los dos, se emparejan una a una en orden, y
    /// cada una se corre en proporción a su sitio. Todas cruzan la misma zona, así que el nudo se ve
    /// <b>parejo</b>.
    /// </para>
    /// <para>
    /// Cuando el alzado dibuje las varillas de un elemento redondo en su <b>proyección real</b> —hoy
    /// las reparte a lo ancho, como en el cuadrado— hay que cambiar las dos cosas a la vez, aquí y
    /// en <see cref="ElementoVertical"/>, o volverán a no coincidir.
    /// </para>
    /// </remarks>
    private static TrazoZapata.BarrasElemento BarrasDelElemento(
        double xCaraDer, double w, double recM, double dSup, double dInf, int nInt) =>
        TrazoZapata.BarrasRectangulares(xCaraDer, w, recM, dSup, dInf, nInt);

    /// <summary>
    /// Port de <c>DibujarUnionDadoColumna</c>: cada varilla con <b>su</b> doblez a 1:6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EL TEOREMA: <b>hay UNA zona de doblez y la comparten todas las varillas</b>. Empieza en
    /// <paramref name="yZonaBot"/> y acaba en la junta, y cada varilla la cruza entera, desde donde
    /// está en el dado hasta donde le toca en la columna.
    /// </para>
    /// <para>
    /// El 1:6 no se aplica varilla por varilla: fija el <b>alto de la zona</b>, y lo fija la que más
    /// se corre. Esa sale exactamente a 1:6 y todas las demás, que se corren menos en la misma
    /// altura, salen <b>más tendidas</b> —1:8, 1:12— que es del lado seguro. Por eso el nudo se ve
    /// <b>parejo</b>: las varillas quedan paralelas y sin quiebres.
    /// </para>
    /// <para>
    /// Darle a cada varilla su propio doblez corto de 6·dx —que fue mi turno anterior— cumple el
    /// 1:6 en cada una por separado, pero deja cada quiebre a una altura distinta y el arranque sale
    /// desparejo, con unas varillas rectas y otras torcidas. No es lo que hace la macro ni lo que se
    /// arma en obra: los dobleces de un nudo se hacen todos en la misma zona.
    /// </para>
    /// <para>
    /// Se ve mejor con el caso que lo destapó: dado y columna <b>circulares con las mismas
    /// varillas</b>. Cada una se corre lo que dice su posición en el círculo —la del paño casi nada,
    /// la del otro extremo el doble— y con la zona compartida todas cruzan en paralelo, que es
    /// exactamente el plano de la macro.
    /// </para>
    /// </remarks>
    /// <param name="yZonaBot">Donde arranca la zona de doblez, la misma para todas.</param>
    /// <param name="yDiagTop">Donde acaba, en la junta con la columna.</param>
    /// <param name="yZonaTop">Hasta donde sigue la varilla, ya dentro de la columna.</param>
    private void DibujarUnion(Union u, double yZonaBot, double yDiagTop, double yZonaTop)
    {
        // EN LA ZONA SOLO VAN LOS DOBLECES. Las varillas del dado que no tienen pareja en la
        // columna ya no se dibujan aquí: la macro las seguía rectas hasta el tope del dado y en el
        // plano quedaban tramos de varilla entre los dobleces que no van a ningún lado.
        foreach (var (x1, x2, dia, capa) in u.Dobleces)
        {
            // La zona es la MISMA para todas: de yZonaBot a yDiagTop. La que más se corre va a 1:6
            // y las otras, más tendidas.
            DesplazamientoVarilla(x1, x2, yZonaBot, yZonaBot, yDiagTop, yZonaTop, dia, capa);
        }
    }

    /// <summary>
    /// Port de <c>DibujarDesplazamientoVarilla</c>: recta, el doblez a 1:6, y recta otra vez.
    /// </summary>
    /// <remarks>
    /// Hasta tres tramos: sube derecha hasta <paramref name="yDiagBot"/>, se corre de lado hasta
    /// <paramref name="yDiagTop"/> y sigue derecha hasta arriba. La unión la llama con
    /// <c>yDiagBot == yBot</c>, así que el primer tramo <b>no existe</b> y la varilla es diagonal
    /// desde el arranque de la zona, como en la macro; el parámetro se conserva porque el tramo
    /// recto de abajo sí hace falta si algún día se acota el doblez por otra regla. Con
    /// <c>x1 == x2</c> sale una varilla recta, que es lo correcto cuando no hay nada que correr.
    /// </remarks>
    private void DesplazamientoVarilla(
        double x1, double x2, double yBot, double yDiagBot, double yDiagTop, double yTop,
        double dia, string capa)
    {
        if (dia <= 0 || yTop <= yBot)
        {
            return;
        }

        AsegurarCapaVarilla(capa);

        var r = dia / 2;

        var yd1 = Math.Clamp(yDiagBot, yBot, yTop);
        var yd2 = Math.Clamp(yDiagTop, yd1, yTop);

        if (_relleno)
        {
            if (yd1 > yBot)
            {
                RellenarQuad(x1 - r, yBot, x1 - r, yd1, x1 + r, yd1, x1 + r, yBot, capa, 0);
            }

            if (yd2 > yd1)
            {
                RellenarQuad(x1 - r, yd1, x2 - r, yd2, x2 + r, yd2, x1 + r, yd1, capa, 0);
            }

            if (yTop > yd2)
            {
                RellenarQuad(x2 - r, yd2, x2 - r, yTop, x2 + r, yTop, x2 + r, yd2, capa, 0);
            }
        }

        foreach (var s in new[] { -1.0, 1.0 })
        {
            var pts = new List<double> { x1 + (s * r), yBot };

            // El vértice del arranque solo se pone si hay tramo recto abajo: repetir el punto
            // dejaría un segmento de longitud cero en la polilínea.
            if (yd1 > yBot + 1e-9)
            {
                pts.Add(x1 + (s * r));
                pts.Add(yd1);
            }

            pts.Add(x2 + (s * r));
            pts.Add(yd2);

            if (yTop > yd2 + 1e-9)
            {
                pts.Add(x2 + (s * r));
                pts.Add(yTop);
            }

            Var(Polilinea(pts.ToArray(), capa, cerrada: false));
        }
    }

    /// <summary>
    /// Port de <c>RecortarVerticalesZonaDobleces</c>: <b>limpia la zona de dobleces</b> antes de
    /// dibujar la transición.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ESTA RUTINA FALTABA, y es la que cierra el problema de las varillas rectas dentro del
    /// 1:6. El dado dibuja sus varillas y se les pide que acaben en el arranque de la zona, pero
    /// ese recorte pasa por media docena de holguras y márgenes —el gancho, el remate, los 2 cm
    /// mínimos— y cualquiera de ellos deja un pedazo asomando. La macro no confía en el recorte:
    /// después de dibujar el dado <b>barre la zona</b> y quita lo que haya quedado dentro.
    /// </para>
    /// <para>
    /// Se llama <b>antes</b> de dibujar los dobleces, igual que en el VBA, así que lo único que
    /// puede encontrar son restos: las varillas de la transición todavía no existen.
    /// </para>
    /// <para>
    /// Lo que barre son las capas <c>VAR_*</c> dentro de los paños del dado. Los estribos —capa
    /// <c>ESTRIBOS</c>— no se tocan: esos SÍ van en la zona, son los que amarran el nudo. Y de cada
    /// resto: si empieza dentro de la zona se borra entero, y si viene de más abajo se recorta al
    /// arranque de la zona, que es donde tiene que acabar.
    /// </para>
    /// </remarks>
    /// <param name="desde">Índice del contenedor antes de dibujar el dado.</param>
    private void RecortarVerticalesEnLaZona(
        int desde, double yZonaBot, double yZonaTop, double xIzq, double xDer)
    {
        if (yZonaTop <= yZonaBot + TrimTolVertical)
        {
            return;
        }

        var restos = new List<(object Ent, bool EsLinea, double[] Min, double[] Max, string Capa)>();

        try
        {
            AcadConnection.Retry(() =>
            {
                restos.Clear();

                var total = (int)((dynamic)_cont).Count;

                for (var i = Math.Max(desde, 0); i < total; i++)
                {
                    dynamic ent = ((dynamic)_cont).Item(i);

                    string capa = ent.Layer;

                    if (!capa.StartsWith("VAR_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string tipo = ent.ObjectName;

                    var esLinea = tipo.Contains("Line", StringComparison.OrdinalIgnoreCase);
                    var esRelleno = tipo.Contains("Hatch", StringComparison.OrdinalIgnoreCase);

                    if (!esLinea && !esRelleno)
                    {
                        continue;
                    }

                    var caja = CajaEnvolvente((object)ent);

                    if (caja is null)
                    {
                        continue;
                    }

                    restos.Add(((object)ent, esLinea, caja.Value.Min, caja.Value.Max, capa));
                }
            });
        }
        catch (Exception ex)
        {
            Fallo("Barrer la zona de dobleces", ex);
            return;
        }

        var borradas = 0;

        foreach (var (ent, esLinea, mn, mx, capa) in restos)
        {
            var xm = (mn[0] + mx[0]) / 2;

            // Solo lo que está dentro de los paños del dado.
            if (xm < xIzq - 0.02 || xm > xDer + 0.02)
            {
                continue;
            }

            // Y solo lo que se mete en la zona.
            if (mx[1] <= yZonaBot + TrimTolVertical || mn[1] >= yZonaTop - TrimTolVertical)
            {
                continue;
            }

            var desdeAbajo = mn[1] < yZonaBot - TrimTolVertical;

            Borrar(ent);
            borradas++;

            if (!desdeAbajo)
            {
                continue;
            }

            // Venía de más abajo: se rehace solo el tramo que queda por debajo de la zona.
            if (esLinea)
            {
                Var(Linea(xm, mn[1], xm, yZonaBot, capa));
            }
            else if (_relleno)
            {
                RellenarQuad(mn[0], mn[1], mx[0], mn[1], mx[0], yZonaBot, mn[0], yZonaBot, capa, 0);
            }
        }

        if (borradas > 0)
        {
            Nota($"Zona de dobleces: se quitaron {borradas} resto(s) de varilla que quedaban "
                 + "dentro del 1:6.");
        }
    }

    /// <summary>Cuántas entidades tiene el contenedor en curso.</summary>
    private int CuentaDelContenedor()
    {
        try
        {
            return AcadConnection.Retry(() => (int)((dynamic)_cont).Count);
        }
        catch (Exception ex)
        {
            Nota("No se pudo contar el contenido del bloque de la zapata: " + ex.Message);
            return -1;
        }
    }

    /// <summary>Tolerancia para decidir si una varilla se mete en la zona: <c>TRIM_TOL_VERTICAL</c>.</summary>
    private const double TrimTolVertical = 0.0006;

    /// <summary>Apunta una entidad para reordenarla después, si se creó.</summary>
    private static void Apuntar(List<object> lista, object? ent)
    {
        if (ent is not null)
        {
            lista.Add(ent);
        }
    }

    /// <summary>
    /// Sube entidades al frente: el <b>bring to front</b> de AutoCAD.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con la tabla <c>ACAD_SORTENTS</c> del contenedor, igual que
    /// <c>AlzadoDrawer.AlFrente</c>. Funciona dentro de un bloque porque la tabla vive en el
    /// diccionario de extensión del bloque, no en el del modelo.
    /// </para>
    /// <para>
    /// Las llamadas van por <see cref="AcadArreglos"/>: <c>MoveToTop</c> recibe un arreglo de
    /// entidades y esa es una de las llamadas que revienta con <c>dynamic</c> si no se le pasa el
    /// arreglo con el tipo que espera.
    /// </para>
    /// </remarks>
    private void AlFrente(object cont, List<object> objetos)
    {
        if (objetos.Count == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic dict = ((dynamic)cont).GetExtensionDictionary;
                dynamic tabla;

                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                AcadArreglos.Llamar("MoveToTop de la zapata", objetos,
                    arr => { tabla.MoveToTop(arr); },
                    (op, ex) => Fallo(op, ex), Nota);
            });
        }
        catch (Exception ex)
        {
            Fallo("Orden de dibujo de la zapata", ex);
        }
    }

    // El port de DibujarBarraVerticalBanda YA NO ESTÁ. Era la varilla recta que la macro dibujaba
    // en la zona de dobleces para las del dado sin pareja en la columna, y en el plano quedaban
    // tramos de varilla entre los dobleces que no van a ningún lado. Se quitó el dibujo y con él su
    // única rutina, en lugar de dejarla sin usar: una rutina que no se llama vuelve sola.

    // ======================================================================
    // Concreto, plantilla y terreno
    // ======================================================================

    /// <summary>Port de <c>DibujarContornoZapataConDado</c>.</summary>
    private void ContornoZapataConDado(
        double xIzq, double yBot, double xDer, double yTop, double xDadoIzq, double xDadoDer)
    {
        const double tol = 1e-6;

        Linea(xIzq, yBot, xDer, yBot, CapaConcreto);
        Linea(xIzq, yBot, xIzq, yTop, CapaConcreto);
        Linea(xDer, yBot, xDer, yTop, CapaConcreto);

        if (xDadoDer <= xDadoIzq)
        {
            Linea(xIzq, yTop, xDer, yTop, CapaConcreto);
            return;
        }

        if (xDadoIzq <= xIzq + tol && xDadoDer >= xDer - tol)
        {
            return;
        }

        if (xDadoIzq > xIzq + tol)
        {
            Linea(xIzq, yTop, xDadoIzq, yTop, CapaConcreto);
        }

        if (xDadoDer < xDer - tol)
        {
            Linea(xDadoDer, yTop, xDer, yTop, CapaConcreto);
        }
    }

    /// <summary>Port de <c>DibujarPlantillaConcretoSimple</c>: solo la geometría.</summary>
    private void PlantillaConcretoSimple(
        double xIzq, double yZapBot, double ancho, double espesor)
    {
        if (ancho <= 0 || espesor <= 0)
        {
            return;
        }

        var xDer = xIzq + ancho;
        var yBot = yZapBot - espesor;

        HatchConcreto(xIzq, yBot, ancho, espesor, CapaPlantilla);

        Linea(xIzq, yBot, xDer, yBot, CapaPlantilla);
        Linea(xIzq, yBot, xIzq, yZapBot, CapaPlantilla);
        Linea(xDer, yBot, xDer, yZapBot, CapaPlantilla);
    }

    /// <summary>Port de <c>DibujarPlantillaTexto</c>: el texto, ya fuera del bloque.</summary>
    /// <remarks>
    /// El alto se ajusta para que quepa en los 5 cm de la plantilla y en el ancho de la zapata, y
    /// va <b>sin máscara de fondo</b> para que se vea el rayado por detrás. Los dos ajustes son de
    /// la macro: sin ellos, en una zapata angosta el texto se sale del dibujo.
    /// </remarks>
    private void PlantillaTexto(double xIzq, double yZapBot, double ancho, double espesor)
    {
        if (ancho <= 0 || espesor <= 0)
        {
            return;
        }

        var yBot = yZapBot - espesor;
        var alto = AltoPlantilla;

        if (alto > espesor * 0.5)
        {
            alto = espesor * 0.5;
        }

        var anchoEstimado = TextoPlantilla.Length * alto * 0.62;

        if (anchoEstimado > ancho * 0.92)
        {
            alto *= ancho * 0.92 / anchoEstimado;
        }

        if (alto <= 0)
        {
            return;
        }

        Mtexto(xIzq + (ancho / 2), yBot + (espesor / 2), TextoPlantilla, alto, CapaRotulos,
            conFondo: false);
    }

    /// <summary>Port de <c>DibujarHatchTerreno</c>: el relleno de tierra a los lados del dado.</summary>
    private void HatchTerreno(
        double xBase, double xDer, double xDadoIzq, double xDadoDer, double yDesde, double yHasta)
    {
        var h = yHasta - yDesde;

        if (h <= 0)
        {
            return;
        }

        if (xDadoIzq > xBase + 0.001)
        {
            HatchRect(xBase, yDesde, xDadoIzq - xBase, h, CapaTerrenoHatch,
                PatronTerreno, EscalaTerreno, TranspTerreno, 0);
        }

        if (xDer > xDadoDer + 0.001)
        {
            HatchRect(xDadoDer, yDesde, xDer - xDadoDer, h, CapaTerrenoHatch,
                PatronTerreno, EscalaTerreno, TranspTerreno, 0);
        }
    }

    /// <summary>Port de <c>DibujarHatchConcretoRect</c>: respeta el modo de relleno.</summary>
    private void HatchConcreto(double x, double y, double w, double h, string capa)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (_relleno)
        {
            HatchRect(x, y, w, h, capa, "SOLID", 1, string.Empty, ColorSolidoRelleno);
            HatchRect(x, y, w, h, capa, PatronConcreto, EscalaConcretoRelleno, string.Empty,
                ColorPatronRelleno);
            return;
        }

        HatchRect(x, y, w, h, capa, PatronConcreto, EscalaConcretoNormal, string.Empty, 0);
    }

    // ======================================================================
    // Cotas y rótulos de la elevación
    // ======================================================================

    /// <summary>Port de <c>CotasAnchosZapataYDado</c>: la cadena de abajo y el total.</summary>
    private void CotasAnchos(
        double xBase, double xDer, double xDadoIzq, double xDadoDer, double yZapBot, Resumen r)
    {
        var yCad = yZapBot - CotaOffsetCadena;
        var yTot = yZapBot - CotaOffsetTotal;

        if (xDadoIzq > xBase + 0.001)
        {
            r.Cotas += Cota(xBase, yCad, xDadoIzq, yCad, (xBase + xDadoIzq) / 2, yCad, false, false);
        }

        if (xDadoDer > xDadoIzq + 0.001)
        {
            r.Cotas += Cota(xDadoIzq, yCad, xDadoDer, yCad, (xDadoIzq + xDadoDer) / 2, yCad,
                false, false);
        }

        if (xDer > xDadoDer + 0.001)
        {
            r.Cotas += Cota(xDadoDer, yCad, xDer, yCad, (xDadoDer + xDer) / 2, yCad, false, false);
        }

        r.Cotas += Cota(xBase, yTot, xDer, yTot, (xBase + xDer) / 2, yTot, false, false);
    }

    /// <summary>Las cuatro verticales de la izquierda, con la plantilla incluida.</summary>
    /// <remarks>
    /// La de la plantilla —5 cm— va con el <b>texto adentro</b>: es la que AutoCAD sacaría con una
    /// flecha y acabaría encima del dibujo. Y la total arranca del <b>fondo de la plantilla</b>,
    /// no del de la zapata, que es lo que hay que replantear en obra.
    /// </remarks>
    private void CotasVerticales(
        double xBase, double yZapBot, double yZapTop, double yTerreno, Resumen r)
    {
        // A LA IZQUIERDA DEL PAÑO IZQUIERDO, a 0.08 y 0.16, donde las pone la macro
        // (COTA_OFFSET_VERT_1 y _2, medidos desde xBase) y donde tienen que estar: pegadas a la
        // SECCIÓN DE CIMENTACIÓN, que es lo que miden. Pasarlas al paño derecho las dejaba al otro
        // lado del dado, a media altura y lejos de lo medido. Se probó dos veces; las dos, peor.
        var x1 = xBase - CotaOffsetVert1;
        var x2 = xBase - CotaOffsetVert2;
        var yPlantillaBot = yZapBot - TrazoZapata.PlantillaEspesor;

        r.Cotas += Cota(x1, yPlantillaBot, x1, yZapBot, x1, (yPlantillaBot + yZapBot) / 2,
            true, dentro: true);
        r.Cotas += Cota(x1, yZapBot, x1, yZapTop, x1, (yZapBot + yZapTop) / 2, true, false);

        if (yTerreno > yZapTop + 0.001)
        {
            r.Cotas += Cota(x1, yZapTop, x1, yTerreno, x1, (yZapTop + yTerreno) / 2, true, false);
        }

        r.Cotas += Cota(x2, yPlantillaBot, x2, yTerreno, x2, (yPlantillaBot + yTerreno) / 2,
            true, false);
    }

    /// <summary>Port de <c>CotasDoblezGanchosDado</c>: los 15 diámetros de cada pata.</summary>
    /// <remarks>
    /// Cada una a 6 cm de su pata, como la macro: la de arriba por debajo de su pata y la de abajo
    /// por encima de la suya, así que no comparten renglón aunque las dos patas se cruzen.
    /// </remarks>
    private void CotasDoblezGanchos(
        double xDadoIzq, double xDadoDer, double yZapBot, double recDadoM, double subirGanchos,
        double dSup, double dInf, double offset, bool haciaAfuera, double desfaseInf,
        bool ambosIzquierda, Resumen r)
    {
        var yPataSup = yZapBot + recDadoM + subirGanchos + (dSup / 2);
        var yPataInf = yZapBot + recDadoM + subirGanchos + desfaseInf + (dInf / 2);

        var xIzq1 = xDadoIzq + recDadoM;
        var xDer1 = xDadoDer - recDadoM;

        double xIzq2;
        double xDer2;

        if (ambosIzquierda)
        {
            xIzq2 = xIzq1 - (TrazoZapata.FactorGanchoAbajo * dSup);
            xDer2 = xDer1 - (TrazoZapata.FactorGanchoAbajo * dInf);
        }
        else if (haciaAfuera)
        {
            xIzq2 = xIzq1 - (TrazoZapata.FactorGanchoAbajo * dSup);
            xDer2 = xDer1 + (TrazoZapata.FactorGanchoAbajo * dInf);
        }
        else
        {
            xIzq2 = xIzq1 + (TrazoZapata.FactorGanchoAbajo * dSup);
            xDer2 = xDer1 - (TrazoZapata.FactorGanchoAbajo * dInf);
        }

        r.Cotas += Cota(xIzq2, yPataSup, xIzq1, yPataSup, (xIzq2 + xIzq1) / 2,
            yPataSup - offset, false, false);
        r.Cotas += Cota(xDer2, yPataInf, xDer1, yPataInf, (xDer2 + xDer1) / 2,
            yPataInf + offset, false, false);
    }

    /// <summary>Port de <c>TextoRotuloElementoVertical</c>: el rótulo del dado o de la columna.</summary>
    private string TextoElemento(
        string elemento, string? id, string? diaSup, string? diaInf, int nInt, string? diaInt,
        string? estrDia, string? sep, int nSup, int nInf, int nIntTotal)
    {
        var titulo = elemento.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(id))
        {
            titulo += $" \"{id!.Trim()}\"";
        }

        var lineas = new List<string> { titulo };

        var barras = TextoBarrasLongitudinales(
            nSup, diaSup, nInf, diaInf, nIntTotal, nInt, diaInt);

        if (barras.Length > 0)
        {
            lineas.Add(barras);
        }

        if (!string.IsNullOrWhiteSpace(estrDia))
        {
            var s = (sep ?? string.Empty)
                .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Trim();

            lineas.Add(s.Length == 0
                ? $"EST {Etiqueta(estrDia)}"
                : $"EST {Etiqueta(estrDia)} @ {s} cm");
        }

        return string.Join("\n", lineas);
    }

    /// <summary>
    /// Port de <c>TextoBarrasLongitudinales</c>: el renglón de varillas del rótulo,
    /// <b>sumando las que son del mismo diámetro</b>.
    /// </summary>
    /// <remarks>
    /// Esto es lo que hacía la macro y lo que a mí me faltaba: yo escribía «VAR #4 + 7 VAR #4»
    /// —el primer término sin conteo y los dos sin juntarse—, y lo que debe decir es
    /// «16 VAR #4». La macro mete los tres términos (paño superior, paño inferior e intermedias)
    /// en una lista, y cuando dos caen en el mismo diámetro suma sus conteos en un solo término.
    /// </remarks>
    private string TextoBarrasLongitudinales(
        int nSup, string? diaSup, int nInf, string? diaInf,
        int nIntTotal, int nIntDibujadas, string? diaInt)
    {
        // Si no hay conteos —una fila vieja, guardada antes de que se referenciaran— se escriben
        // los diámetros sin número, que es lo que se hacía hasta ahora: mejor eso que un rótulo
        // vacío o un conteo inventado.
        var hayConteos = nSup > 0 || nInf > 0 || nIntTotal > 0;

        if (!hayConteos)
        {
            var sinConteo = new List<string>();

            if (!string.IsNullOrWhiteSpace(diaSup))
            {
                sinConteo.Add($"VAR {Etiqueta(diaSup)}");
            }

            if (!string.IsNullOrWhiteSpace(diaInf) && !MismoDiametro(diaSup, diaInf))
            {
                sinConteo.Add($"VAR {Etiqueta(diaInf)}");
            }

            if (nIntDibujadas > 0 && !string.IsNullOrWhiteSpace(diaInt))
            {
                sinConteo.Add($"{nIntDibujadas} VAR {Etiqueta(diaInt)}");
            }

            return string.Join(" + ", sinConteo);
        }

        var etiquetas = new List<string>();
        var conteos = new List<int>();

        void Agregar(int n, string? clave)
        {
            var tag = Etiqueta(clave);

            if (n <= 0 || tag.Length == 0)
            {
                return;
            }

            var k = etiquetas.IndexOf(tag);

            if (k >= 0)
            {
                conteos[k] += n;
                return;
            }

            etiquetas.Add(tag);
            conteos.Add(n);
        }

        Agregar(nSup, diaSup);
        Agregar(nInf, diaInf);
        Agregar(nIntTotal, diaInt);

        var terminos = new List<string>();

        for (var i = 0; i < etiquetas.Count; i++)
        {
            terminos.Add($"{conteos[i]} VAR {etiquetas[i]}");
        }

        return string.Join(" + ", terminos);
    }

    /// <param name="diaInt">
    /// Diámetro de las intermedias <b>ya resuelto</b>, con el respaldo de la macro aplicado.
    /// </param>
    private void RotuloDelDado(ZapataCad z, TrazoZapata.Acomodo a, bool lindero, string? diaInt)
    {
        if (a.YDadoTop <= a.YZapTop + 0.02)
        {
            return;
        }

        var y = (a.YZapTop + a.YDadoTop) / 2;

        // Con el mismo respaldo de diámetro que el dibujo, para que el rótulo no diga una cosa y el
        // dibujo otra.
        var texto = TextoElemento("DADO", z.IdDado, z.VarDadoSup, z.VarDadoInf,
            z.NIntDado, diaInt, z.EstriboDado, z.SepEstriboDado,
            z.NVarDadoSup, z.NVarDadoInf, z.NVarIntDadoTotal);

        if (lindero)
        {
            // El dado está al paño derecho: su rótulo sale a la IZQUIERDA.
            RotuloConLeader(a.XDadoIzq, y, a.XDadoIzq - LinderoRotuloElemDx, y, texto, true);
        }
        else
        {
            RotuloConLeader(a.XDadoDer, y, (a.XDadoDer + a.XDer) / 2, y, texto, false);
        }
    }

    /// <param name="diaInt">Diámetro de las intermedias ya resuelto.</param>
    private void RotuloDeLaColumna(
        ZapataCad z, TrazoZapata.Acomodo a, bool lindero, string? diaInt)
    {
        var y = a.YDadoTop + (AlturaColumnaRep * ColumnaFraccionCorte / 2);

        var texto = TextoElemento("COLUMNA", z.IdColumna, z.VarColSup, z.VarColInf,
            z.NIntColumna, diaInt, z.EstriboColumna, z.SepEstriboColumna,
            z.NVarColSup, z.NVarColInf, z.NVarIntColumnaTotal);

        if (lindero)
        {
            RotuloConLeader(a.XColIzq, y, a.XColIzq - LinderoRotuloElemDx, y, texto, true);
        }
        else
        {
            RotuloConLeader(a.XColDer, y, (a.XColDer + a.XDer) / 2, y, texto, false);
        }
    }

    /// <summary>Port de <c>RotularElementoVerticalLeader</c>.</summary>
    /// <remarks>
    /// El texto se coloca y <b>después</b> se mira dónde acabó su caja para sacar el leader del
    /// borde que toca, corriéndolo si se acercó demasiado a la pieza. Sin eso, la flecha sale de
    /// media palabra.
    /// </remarks>
    private void RotuloConLeader(
        double xPunta, double yPunta, double xTexto, double yTexto, string texto,
        bool haciaIzquierda)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        var mt = Mtexto(xTexto, yTexto, texto, AltoMtexto, CapaRotulos, conFondo: true);

        if (mt is null)
        {
            return;
        }

        var caja = Caja(mt);
        var xAnclaje = xTexto;

        if (caja is not null)
        {
            xAnclaje = haciaIzquierda ? caja.Value.X2 : caja.Value.X1;
        }

        var dx = 0.0;

        if (haciaIzquierda)
        {
            if (xAnclaje > xPunta - RotuloVertGapLeader)
            {
                dx = xPunta - RotuloVertGapLeader - xAnclaje;
            }
        }
        else
        {
            if (xAnclaje < xPunta + RotuloVertGapLeader)
            {
                dx = xPunta + RotuloVertGapLeader - xAnclaje;
            }
        }

        if (Math.Abs(dx) > 1e-6)
        {
            Mover(mt, dx, 0);
            xAnclaje += dx;
        }

        Leader(xPunta, yPunta, xAnclaje, yTexto);
    }

    /// <summary>
    /// Port de <c>RotularParrillaInferiorZA</c>: los rótulos de la parrilla de abajo.
    /// </summary>
    /// <remarks>
    /// LA POSICIÓN ES LA DE LA MACRO, con sus sumas y restas tal cual:
    /// <c>xTexto = (xBase - 0.18) + 0.272 - 0.11 + 0.2</c> y
    /// <c>yTexto = (yZapBot + 0.1) + 0.4164 - 0.16</c>. Se ven raras porque salieron de mover el
    /// rótulo a mano en AutoCAD hasta dejarlo en su sitio, pero es justo lo que hay que copiar:
    /// el turno pasado yo lo puse en <c>yZapBot - 0.10</c>, o sea 46 cm más abajo, y ahí es donde
    /// se le encimaba a la cota de la cadena (0.14), a la del total (0.22) y al título (0.32).
    /// Y va <b>sin el renglón «PARRILLA INFERIOR»</b>: en la elevación las dos macros solo
    /// escriben la varilla y su separación —el título únicamente aparece en la planta—, y ese
    /// renglón de más era lo que ensanchaba el rótulo hasta chocar con el de al lado.
    /// </remarks>
    private void RotuloParrillaInferior(
        double xBase, double yZapBot, double anchoZapata, double rec,
        string? varBarra, string? sepBarra, string? varCirc, string? sepCirc)
    {
        var dBarra = Diam(varBarra);

        if (dBarra <= 0)
        {
            return;
        }

        var dCirc = Diam(varCirc);
        var sepCircM = TrazoZapata.SeparacionM(sepCirc);

        var yBarra = yZapBot + rec + (dBarra / 2);
        var yCirc = yBarra + (dBarra / 2) + (dCirc / 2);

        var xCaraIzq = xBase + rec;
        var xCaraDer = xBase + anchoZapata - rec;
        var xCircIzq = xCaraIzq + (dBarra / 2) + (dCirc / 2);

        // Las dos puntas: la del círculo en la segunda varilla transversal y la de la barra un
        // poco más adentro, sin pasarse del 32 % del ancho ni pegarse a la otra punta.
        var xPuntaCirc = xCircIzq + sepCircM;
        var xPuntaBarra = xPuntaCirc + SeparacionPuntasParrillaInf;
        var xPuntaBarraMax = xCaraIzq + ((xCaraDer - xCaraIzq) * FraccionMaxPuntaBarraInf);
        var xPuntaBarraMin = xPuntaCirc + SeparacionMinPuntas;

        if (xPuntaBarra > xPuntaBarraMax)
        {
            xPuntaBarra = xPuntaBarraMax;
        }

        if (xPuntaBarra < xPuntaBarraMin)
        {
            xPuntaBarra = xPuntaBarraMin;
        }

        var xTexto = xBase - 0.18 + 0.272 - 0.11 + DesplazamientoParrillaInfCentrar;
        var yTexto = yZapBot + 0.1 + 0.4164 - 0.16;

        var textoBarra = VarSep(varBarra, sepBarra, "INFERIOR");
        var textoCirc = VarSep(varCirc, sepCirc, "SUPERIOR");

        if (textoBarra.Length == 0 && textoCirc.Length == 0)
        {
            return;
        }

        if (ParrillasIguales(varBarra, sepBarra, varCirc, sepCirc))
        {
            // Un solo rótulo centrado, con los dos leaders colgados de sus costados.
            var xCentroMt = xTexto + (AnchoMtexto / 2)
                            + DesplazamientoAmbosSentidos + DesplazamientoAmbosInferiorX;
            var yAmbos = yTexto + DesplazamientoYAmbosTexto;

            var mtAmbos = Mtexto(xCentroMt, yAmbos, TextoAmbosSentidos(varBarra, sepBarra),
                AltoMtexto, CapaRotulos, conFondo: true, anclaje: AnclajeCentro);

            if (mtAmbos is null)
            {
                return;
            }

            var cajaAmbos = Caja(mtAmbos);
            var yAnclaje = yAmbos + DesplazamientoYAmbosAnclaje;

            Leader(xPuntaCirc, yCirc, cajaAmbos?.X1 ?? xCentroMt, yAnclaje);
            Leader(xPuntaBarra, yBarra, cajaAmbos?.X2 ?? xCentroMt, yAnclaje);

            return;
        }

        // Dos rótulos: el de la parrilla de arriba un renglón más alto y creciendo a la derecha,
        // el de la de abajo un renglón más bajo y creciendo a la izquierda.
        var ySuperior = yTexto + DesplazamientoVertical;
        var yInferior = yTexto - DesplazamientoVertical;
        var xTextoDer = xTexto + AnchoMtexto
                        + DesplazamientoInferiorX + DesplazamientoInferiorAdicional;

        if (textoCirc.Length > 0)
        {
            var mtCirc = Mtexto(xTexto, ySuperior, textoCirc, AltoMtexto, CapaRotulos,
                conFondo: true, anclaje: AnclajeIzquierda);

            if (mtCirc is not null)
            {
                Leader(xPuntaCirc, yCirc, Caja(mtCirc)?.X1 ?? xTexto, ySuperior);
            }
        }

        if (textoBarra.Length > 0)
        {
            var mtBarra = Mtexto(xTextoDer, yInferior, textoBarra, AltoMtexto, CapaRotulos,
                conFondo: true, anclaje: AnclajeDerecha);

            if (mtBarra is not null)
            {
                Leader(xPuntaBarra, yBarra, Caja(mtBarra)?.X2 ?? xTextoDer, yInferior);
            }
        }
    }

    /// <summary>
    /// Port de <c>RotularParrillaSuperiorZA</c> de la macro CENTRAL: el rótulo sale del paño
    /// derecho de la zapata, arriba del lomo.
    /// </summary>
    /// <remarks>
    /// Otra vez los números de la macro: <c>xTexto = xBase + ancho + 0.16 - 0.4302</c> y
    /// <c>yTexto = yZapTop + 0.02 + 0.2908 - 0.16</c>. Mi versión anterior lo ponía en
    /// <c>xBase + ancho + 0.10</c>, es decir 37 cm más a la derecha, fuera del dibujo y encima de
    /// la zapata siguiente.
    /// </remarks>
    private void RotuloParrillaSuperiorCentral(
        double xBase, double yZapBot, double anchoZapata, double espZapata, double rec,
        string? varBarra, string? sepBarra, string? varCirc, string? sepCirc)
    {
        var dBarra = Diam(varBarra);

        if (dBarra <= 0)
        {
            return;
        }

        var dCirc = Diam(varCirc);
        var yZapTop = yZapBot + espZapata;
        var yBarra = yZapTop - rec - (dBarra / 2);
        var yCirc = yBarra - (dBarra / 2) - (dCirc / 2);

        var xCaraDer = xBase + anchoZapata - rec;
        var xCircDer = xCaraDer - (dBarra / 2) - (dCirc / 2);
        var xPuntaBarra = xBase + anchoZapata - 0.18;

        var xTexto = xBase + anchoZapata + 0.16 - 0.4302;
        var yTexto = yZapTop + 0.02 + 0.2908 - 0.16;

        var textoBarra = VarSep(varBarra, sepBarra, "SUPERIOR");
        var textoCirc = VarSep(varCirc, sepCirc, "INFERIOR");

        if (textoBarra.Length == 0 && textoCirc.Length == 0)
        {
            return;
        }

        if (ParrillasIguales(varBarra, sepBarra, varCirc, sepCirc))
        {
            var xCentroMt = xTexto + (AnchoMtexto / 2) + DesplazamientoAmbosSentidos;
            var yAmbos = yTexto + DesplazamientoYAmbosTexto;

            var mtAmbos = Mtexto(xCentroMt, yAmbos, TextoAmbosSentidos(varBarra, sepBarra),
                AltoMtexto, CapaRotulos, conFondo: true, anclaje: AnclajeCentro);

            if (mtAmbos is null)
            {
                return;
            }

            var cajaAmbos = Caja(mtAmbos);
            var yAnclaje = yAmbos + DesplazamientoYAmbosAnclaje;

            Leader(xPuntaBarra, yBarra, cajaAmbos?.X1 ?? xCentroMt, yAnclaje);
            Leader(xCircDer, yCirc, cajaAmbos?.X2 ?? xCentroMt, yAnclaje);

            return;
        }

        var ySuperior = yTexto + DesplazamientoVertical;
        var yInferior = yTexto - DesplazamientoVertical;
        var xTextoDer = xTexto + AnchoMtexto
                        + DesplazamientoInferiorX + DesplazamientoInferiorSuperiorAdicional;

        if (textoBarra.Length > 0)
        {
            var mtBarra = Mtexto(xTexto, ySuperior, textoBarra, AltoMtexto, CapaRotulos,
                conFondo: true, anclaje: AnclajeIzquierda);

            if (mtBarra is not null)
            {
                Leader(xPuntaBarra, yBarra, Caja(mtBarra)?.X1 ?? xTexto, ySuperior);
            }
        }

        if (textoCirc.Length > 0)
        {
            var mtCirc = Mtexto(xTextoDer, yInferior, textoCirc, AltoMtexto, CapaRotulos,
                conFondo: true, anclaje: AnclajeDerecha);

            if (mtCirc is not null)
            {
                Leader(xCircDer, yCirc, Caja(mtCirc)?.X2 ?? xTextoDer, yInferior);
            }
        }
    }

    /// <summary>
    /// Port de <c>RotularParrillaSuperiorZALindero</c>: en el lindero va <b>centrado</b> sobre el
    /// lomo de la zapata, porque el rótulo del dado ya ocupa la izquierda.
    /// </summary>
    private void RotuloParrillaSuperiorLindero(
        double xBase, double yZapBot, double anchoZapata, double espZapata, double rec,
        string? varBarra, string? sepBarra, string? varCirc, string? sepCirc)
    {
        var dBarra = Diam(varBarra);

        if (dBarra <= 0)
        {
            return;
        }

        var dCirc = Diam(varCirc);
        var sepCircM = TrazoZapata.SeparacionM(sepCirc);
        var yZapTop = yZapBot + espZapata;
        var yBarra = yZapTop - rec - (dBarra / 2);
        var yCirc = yBarra - (dBarra / 2) - (dCirc / 2);

        var xCircIzq = xBase + rec + (dBarra / 2) + (dCirc / 2);
        var xCircDer = xBase + anchoZapata - rec - (dBarra / 2) - (dCirc / 2);

        var xCentro = xBase + (anchoZapata / 2);
        var yTexto = yZapTop + LinderoRotuloSupDy;

        // La punta de la barra a la izquierda del eje y la del círculo a la derecha, para que los
        // dos leaders no se crucen. La del círculo se pega a UNA VARILLA DE VERDAD, no a un punto
        // cualquiera: es lo que hace CirculoMasCercano en la macro.
        var xPuntaBarra = xBase + (anchoZapata * LinderoRotSupFxBarra);
        var xPuntaCirc = CirculoMasCercano(
            xCircIzq, xCircDer, sepCircM, xBase + (anchoZapata * LinderoRotSupFxCirc));

        var textoBarra = VarSep(varBarra, sepBarra, "SUPERIOR");
        var textoCirc = VarSep(varCirc, sepCirc, "INFERIOR");

        if (textoBarra.Length == 0 && textoCirc.Length == 0)
        {
            return;
        }

        if (ParrillasIguales(varBarra, sepBarra, varCirc, sepCirc))
        {
            var mtAmbos = Mtexto(xCentro, yTexto, TextoAmbosSentidos(varBarra, sepBarra),
                AltoMtexto, CapaRotulos, conFondo: true, anclaje: AnclajeCentro);

            if (mtAmbos is null)
            {
                return;
            }

            var cajaAmbos = Caja(mtAmbos);
            var yAnclaje = yTexto + DesplazamientoYAmbosAnclaje;

            Leader(xPuntaBarra, yBarra, cajaAmbos?.X1 ?? xCentro, yAnclaje);
            Leader(xPuntaCirc, yCirc, cajaAmbos?.X2 ?? xCentro, yAnclaje);

            return;
        }

        var ySuperior = yTexto + DesplazamientoVertical;
        var yInferior = yTexto - DesplazamientoVertical;

        if (textoBarra.Length > 0)
        {
            // Crece hacia la izquierda desde el eje.
            var mtBarra = Mtexto(xCentro - LinderoRotSupGapX, ySuperior, textoBarra, AltoMtexto,
                CapaRotulos, conFondo: true, anclaje: AnclajeDerecha);

            if (mtBarra is not null)
            {
                Leader(xPuntaBarra, yBarra, Caja(mtBarra)?.X1 ?? xCentro, ySuperior);
            }
        }

        if (textoCirc.Length > 0)
        {
            // Y este hacia la derecha.
            var mtCirc = Mtexto(xCentro + LinderoRotSupGapX, yInferior, textoCirc, AltoMtexto,
                CapaRotulos, conFondo: true, anclaje: AnclajeIzquierda);

            if (mtCirc is not null)
            {
                Leader(xPuntaCirc, yCirc, Caja(mtCirc)?.X2 ?? xCentro, yInferior);
            }
        }
    }

    /// <summary>
    /// Port de <c>CirculoMasCercano</c>: el centro de la varilla transversal más cercana a
    /// <paramref name="xObjetivo"/>, para que la flecha del leader caiga sobre una varilla.
    /// </summary>
    private static double CirculoMasCercano(
        double xIni, double xFin, double sep, double xObjetivo)
    {
        if (sep <= 0 || xFin <= xIni)
        {
            return xObjetivo;
        }

        var k = (long)((xObjetivo - xIni) / sep);

        if (k < 0)
        {
            k = 0;
        }

        var x = xIni + (k * sep);

        return x > xFin ? xFin : x;
    }

    /// <summary>
    /// Las dos parrillas son la misma varilla a la misma separación (el «AMBOS SENTIDOS»).
    /// </summary>
    private bool ParrillasIguales(
        string? varBarra, string? sepBarra, string? varCirc, string? sepCirc)
        => MismoDiametro(varBarra, varCirc)
           && TrazoZapata.SeparacionM(sepBarra, -1) == TrazoZapata.SeparacionM(sepCirc, -2);

    /// <summary>El texto de las dos parrillas cuando son iguales.</summary>
    private string TextoAmbosSentidos(string? varBarra, string? sepBarra)
        => $"{VarSep(varBarra, sepBarra, string.Empty)}\nAMBOS SENTIDOS";

    private string VarSep(string? clave, string? sep, string sufijo)
    {
        var d = Etiqueta(clave);

        if (d.Length == 0)
        {
            return string.Empty;
        }

        var t = $"VAR {d}";

        if (sufijo.Length > 0)
        {
            t += $" {sufijo}";
        }

        var s = TrazoZapata.SeparacionM(sep, -1);

        if (s > 0)
        {
            t += $" @ {s * 100:0.#} cm";
        }

        return t;
    }
}
