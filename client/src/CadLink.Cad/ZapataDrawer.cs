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

    // Cotas y rótulos
    private const double CotaOffsetVert1 = 0.08;
    private const double CotaOffsetVert2 = 0.16;
    private const double CotaOffsetCadena = 0.14;
    private const double CotaOffsetTotal = 0.22;
    private const double CotaDoblezOffset = 0.06;
    private const double RotuloTituloOffset = 0.32;
    private const double RotuloSubtituloOffset = 0.41;

    /// <summary><c>ROTULO_ESCALA_OFFSET</c>. De este renglón cuelga la planta.</summary>
    private const double RotuloEscalaOffset = 0.49;

    private const double AltoTitulo = 0.07;
    private const double AltoSubtitulo = 0.05;
    private const double AltoEscala = 0.04;
    private const double AltoTerreno = 0.025;
    private const double AltoMtexto = 0.015;
    private const double AltoPlantilla = 0.02;
    private const double LargoFlecha = 0.014;
    private const double AnchoFlecha = 0.0042;
    private const double RotuloVertGapLeader = 0.06;

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
        var dIntDado = Diam(z.VarIntDado);
        var dEstDado = Diam(z.EstriboDado);
        var dMaxDado = Math.Max(dSupDado, dInfDado);

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

        var union = PrepararUnion(
            a.XDadoDer, a.XDadoDer - a.XDadoIzq, recDadoM, dSupDado, dInfDado, z.NIntDado,
            z.VarIntDado, dIntDado, z.VarDadoSup, z.VarDadoInf,
            a.XColDer, a.XColDer - a.XColIzq, recColM, dSupCol, dInfCol, z.NIntColumna,
            esquinasIguales, intermediasIguales);

        // Alto de la zona de dobleces, recortado para que quede barra recta en el dado.
        var hMaxZona = (yDadoTop - recDadoM) - (yZapTop + MinBarraRectaDado);

        if (hMaxZona < 0)
        {
            hMaxZona = 0;
        }

        var hZona = Math.Min(union.Alto, hMaxZona);

        if (hZona < 0)
        {
            hZona = 0;
        }

        var yZonaTop = yDadoTop + recColM;
        var yZonaBot = yDadoTop - hZona;

        if (yZonaBot > yDadoTop - recDadoM)
        {
            yZonaBot = yDadoTop - recDadoM;
        }

        var recorteDado = 0.0;

        if (union.Activa)
        {
            recorteDado = Math.Max((yDadoTop - recDadoM) - yZonaBot, 0);

            // EL TRASLAPE VA A 1:6 —la RELACION_DESPLAZAMIENTO de la macro—: la zona de dobleces
            // mide seis veces lo que la barra se corre de lado. Si el dado es tan bajo que no
            // caben esos seis, la zona se recorta y el doblez sale MAS PARADO que 1:6, así que se
            // dice: un doblez más parado de lo que manda el reglamento no se arregla dibujándolo
            // bonito, se arregla subiendo el dado o bajando el desplazamiento.
            if (union.Alto > hZona + 1e-6)
            {
                Nota($"Zapata '{z.Id}': el traslape del dado con la columna necesita "
                     + $"{union.Alto:0.###} m para quedar a 1:6 y en el dado solo caben "
                     + $"{hZona:0.###} m, así que el doblez queda más parado. Sube el dado o "
                     + "reduce la diferencia entre el ancho del dado y el de la columna.");
            }
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

        r.Estribos += ElementoVertical(
            x0: a.XDadoDer, y0: yZapBot, largo: alturaDadoRep,
            anchoCm: z.AnchoDadoCm, recCm: z.RecDadoCm,
            diaSup: z.VarDadoSup, diaInf: z.VarDadoInf,
            nInt: z.NIntDado, diaInt: z.VarIntDado,
            estrDia: z.EstriboDado, espStr: z.SepEstriboDado,
            gancho: GanchoRemate, esDado: true, subirGanchos: subirGanchoDado,
            gancho12D: true, recorteConcIni: z.EspesorM, fracCorte: 0,
            estrOmitirIni: omitirEstribos, omitGanchoIni: false,
            omitGanchoFin: union.Activa, ganchoIniAfuera: z.ColumnaDeConcreto ? 0 : 1,
            recorteBarrasFin: recorteDado, offEstribosFin: offEstFinDado,
            // ganchosAmbosIzq va SIEMPRE en false, también en el lindero. La regla es el TIPO
            // DE COLUMNA y nada más: con columna de concreto las dos patas doblan hacia ADENTRO
            // del núcleo -que es donde hay concreto que las reciba- y con columna de acero una
            // adentro y otra afuera. La macro V1 del lindero las mandaba las dos a la izquierda
            // por el paño del lindero, y eso dejaba una pata saliéndose del dado.
            estribosAlTope: z.ColumnaDeConcreto, ganchosAmbosIzq: false);

        if (z.ColumnaDeConcreto)
        {
            r.Estribos += ElementoVertical(
                x0: a.XColDer, y0: yDadoTop, largo: AlturaColumnaRep,
                anchoCm: z.AnchoColumnaCm, recCm: z.RecColumnaCm,
                diaSup: z.VarColSup, diaInf: z.VarColInf,
                nInt: z.NIntColumna, diaInt: z.VarIntColumna,
                estrDia: z.EstriboColumna, espStr: z.SepEstriboColumna,
                gancho: GanchoRemate, esDado: false, subirGanchos: 0,
                gancho12D: false, recorteConcIni: 0, fracCorte: ColumnaFraccionCorte,
                estrOmitirIni: -1, omitGanchoIni: union.Activa,
                omitGanchoFin: false, ganchoIniAfuera: -1,
                recorteBarrasFin: 0, offEstribosFin: -1,
                estribosAlTope: false, ganchosAmbosIzq: false);

            if (union.Activa)
            {
                DibujarUnion(union, yZonaBot, yDadoTop, yZonaTop, yDadoTop - recDadoM);
            }
        }

        // ---------- Se inserta el bloque de la zapata ----------
        _cont = _ms;

        if (usaBloque)
        {
            if (InsertarBloque(nombreBloque, xBase, yZapBot, CapaBloqueZapata))
            {
                r.Bloques++;
            }
        }

        // ---------- Texto de la plantilla: DESPUÉS del bloque ----------
        // Va aquí y no dentro: con la sección rellena, el SOLID del bloque lo taparía.
        PlantillaTexto(xBase, yZapBot, anchoZapata, TrazoZapata.PlantillaEspesor);

        // ---------- Rótulos con leader del dado y de la columna ----------
        RotuloDelDado(z, a, lindero);

        if (z.ColumnaDeConcreto)
        {
            RotuloDeLaColumna(z, a, lindero);
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
        CotasVerticales(xExtremoDer, yZapBot, yZapTop, yTerreno, r);

        // ---------- Rótulo de la sección ----------
        var titulo = lindero
            ? $"ZAPATA AISLADA DE LINDERO \"{z.Id}\""
            : $"ZAPATA AISLADA CENTRAL \"{z.Id}\"";

        // LOS TRES RENGLONES DEL RÓTULO SE CUELGAN DEL PUNTO INFERIOR DERECHO de la zapata y
        // crecen hacia la izquierda, sobre su propio dibujo. Centrados en el eje —como estaban—
        // un título largo se salía por los dos lados y se metía en la zapata de al lado; con el
        // extremo derecho fijo, cada rótulo se queda con su zapata y se mueve con ella.
        Texto(xExtremoDer, yZapBot - RotuloTituloOffset, AltoTitulo, titulo, CapaRotulos,
            alineacion: Alineacion.Derecha);
        Texto(xExtremoDer, yZapBot - RotuloSubtituloOffset, AltoSubtitulo, "ELEVACION",
            CapaRotulos, alineacion: Alineacion.Derecha);

        var fc = string.IsNullOrWhiteSpace(z.Fc) ? string.Empty : $"    f'c = {z.Fc.Trim()} kg/cm\u00B2";

        Texto(xExtremoDer, yZapBot - RotuloEscalaOffset, AltoEscala,
            $"Rec. {z.RecM * 100:0.#} cm{fc}    Escala 1:10", CapaRotulos,
            alineacion: Alineacion.Derecha);

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
        double recorteBarrasFin, double offEstribosFin, bool estribosAlTope, bool ganchosAmbosIzq)
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

                if (recorteBarrasFin > 0 && xb - recorteBarrasFin > xaBot + 0.02)
                {
                    xbBar = xb - recorteBarrasFin;
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

                if (!hayCorte && !omitGanchoFin)
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
        public double Alto { get; set; }
        public List<(double X1, double X2, double Dia, string Capa)> Dobleces { get; } = new();
        public List<(double X, double Dia, string Capa)> Rectas { get; } = new();
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
        double xDadoCaraDer, double wDado, double recDadoM, double dSupD, double dInfD,
        int nIntD, string? diaIntD, double dIntD, string? diaSupD, string? diaInfD,
        double xColCaraDer, double wCol, double recColM, double dSupC, double dInfC, int nIntC,
        bool esquinasIguales, bool intermediasIguales)
    {
        var u = new Union();

        if (!esquinasIguales)
        {
            return u;
        }

        var (xEsqIzqD, xEsqDerD, xIntD) =
            PosicionesBarras(xDadoCaraDer, wDado, recDadoM, dSupD, dInfD, nIntD);
        var (xEsqIzqC, xEsqDerC, xIntC) =
            PosicionesBarras(xColCaraDer, wCol, recColM, dSupC, dInfC, nIntC);

        if (Math.Abs(xEsqIzqC - xEsqIzqD) > DesplazamientoMax
            || Math.Abs(xEsqDerC - xEsqDerD) > DesplazamientoMax)
        {
            return u;
        }

        u.Dobleces.Add((xEsqIzqD, xEsqIzqC, dSupD, CapaVar(diaSupD)));
        u.Dobleces.Add((xEsqDerD, xEsqDerC, dInfD, CapaVar(diaInfD)));

        var usadoD = new bool[xIntD.Count];
        var usadoC = new bool[xIntC.Count];

        if (intermediasIguales && xIntD.Count > 0 && xIntC.Count > 0)
        {
            var pares = Math.Min(xIntD.Count, xIntC.Count);

            for (var p = 0; p < pares; p++)
            {
                var mejorD = -1;
                var mejorC = -1;
                var mejor = double.MaxValue;

                for (var k = 0; k < xIntD.Count; k++)
                {
                    if (usadoD[k])
                    {
                        continue;
                    }

                    for (var j = 0; j < xIntC.Count; j++)
                    {
                        if (usadoC[j])
                        {
                            continue;
                        }

                        var d = Math.Abs(xIntD[k] - xIntC[j]);

                        if (d < mejor)
                        {
                            mejor = d;
                            mejorD = k;
                            mejorC = j;
                        }
                    }
                }

                if (mejorD < 0)
                {
                    break;
                }

                usadoD[mejorD] = true;
                usadoC[mejorC] = true;

                u.Dobleces.Add((xIntD[mejorD], xIntC[mejorC], dIntD, CapaVar(diaIntD)));
            }
        }

        for (var k = 0; k < xIntD.Count; k++)
        {
            if (!usadoD[k])
            {
                u.Rectas.Add((xIntD[k], dIntD, CapaVar(diaIntD)));
            }
        }

        var dxMax = u.Dobleces.Count == 0
            ? 0
            : u.Dobleces.Max(d => Math.Abs(d.X2 - d.X1));

        u.Alto = RelacionDesplazamiento * dxMax;
        u.Activa = true;

        return u;
    }

    /// <summary>Port de <c>PosicionesBarrasElemento</c>, ya en coordenadas globales.</summary>
    private static (double Izq, double Der, List<double> Intermedias) PosicionesBarras(
        double xCaraDer, double w, double recM, double dSup, double dInf, int nInt)
    {
        var izq = xCaraDer - (w - recM - (dSup / 2));
        var der = xCaraDer - (recM + (dInf / 2));

        var lista = new List<double>();

        if (nInt <= 0)
        {
            return (izq, der, lista);
        }

        var yBot = recM + dInf;
        var yTop = w - recM - dSup;

        if (yTop <= yBot)
        {
            return (izq, der, lista);
        }

        var paso = (yTop - yBot) / (nInt + 1);

        for (var k = 1; k <= nInt; k++)
        {
            lista.Add(xCaraDer - (yBot + (paso * k)));
        }

        return (izq, der, lista);
    }

    /// <summary>Port de <c>DibujarUnionDadoColumna</c>.</summary>
    private void DibujarUnion(
        Union u, double yZonaBot, double yJunta, double yZonaTop, double yTopRectas)
    {
        foreach (var (x1, x2, dia, capa) in u.Dobleces)
        {
            DesplazamientoVarilla(x1, x2, yZonaBot, yJunta, yZonaTop, dia, capa);
        }

        foreach (var (x, dia, capa) in u.Rectas)
        {
            BarraVerticalBanda(x, yZonaBot, yTopRectas, dia, capa, false, true);
        }
    }

    /// <summary>Port de <c>DibujarDesplazamientoVarilla</c>: la barra que se corre y sigue.</summary>
    private void DesplazamientoVarilla(
        double x1, double x2, double yBot, double yDiagTop, double yTop, double dia, string capa)
    {
        if (dia <= 0 || yTop <= yBot)
        {
            return;
        }

        AsegurarCapaVarilla(capa);

        var r = dia / 2;
        var yt = Math.Clamp(yDiagTop, yBot, yTop);

        if (_relleno)
        {
            RellenarQuad(x1 - r, yBot, x2 - r, yt, x2 + r, yt, x1 + r, yBot, capa, 0);
            RellenarQuad(x2 - r, yt, x2 - r, yTop, x2 + r, yTop, x2 + r, yt, capa, 0);
        }

        foreach (var s in new[] { -1.0, 1.0 })
        {
            Var(Polilinea(
                new[] { x1 + (s * r), yBot, x2 + (s * r), yt, x2 + (s * r), yTop },
                capa, cerrada: false));
        }
    }

    /// <summary>Port de <c>DibujarBarraVerticalBanda</c>.</summary>
    private void BarraVerticalBanda(
        double x, double yBot, double yTop, double dia, string capa,
        bool taparAbajo, bool taparArriba)
    {
        if (dia <= 0 || yTop <= yBot)
        {
            return;
        }

        AsegurarCapaVarilla(capa);

        var r = dia / 2;

        if (_relleno)
        {
            RellenarQuad(x - r, yBot, x + r, yBot, x + r, yTop, x - r, yTop, capa, 0);
        }

        Var(Linea(x - r, yBot, x - r, yTop, capa));
        Var(Linea(x + r, yBot, x + r, yTop, capa));

        if (taparAbajo)
        {
            Var(Linea(x - r, yBot, x + r, yBot, capa));
        }

        if (taparArriba)
        {
            Var(Linea(x - r, yTop, x + r, yTop, capa));
        }
    }

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
        double xDer, double yZapBot, double yZapTop, double yTerreno, Resumen r)
    {
        // Colgadas del PAÑO DERECHO y hacia la derecha, no del izquierdo: es el hueco de 80 cm
        // que la fila deja entre una zapata y la anterior, y así las cotas viajan con SU zapata y
        // no se meten en la de al lado. En la macro iban a la izquierda porque las centrales
        // crecían hacia la derecha; con la fila creciendo a la izquierda, ese lado ya está
        // ocupado por la zapata siguiente.
        var x1 = xDer + CotaOffsetVert1;
        var x2 = xDer + CotaOffsetVert2;
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

    /// <summary>Port de <c>TextoRotuloElementoVertical</c>.</summary>
    private string TextoElemento(
        string elemento, string? id, string? diaSup, string? diaInf, int nInt, string? diaInt,
        string? estrDia, string? sep)
    {
        var titulo = elemento.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(id))
        {
            titulo += $" \"{id!.Trim()}\"";
        }

        var lineas = new List<string> { titulo };

        var barras = new List<string>();

        if (!string.IsNullOrWhiteSpace(diaSup))
        {
            barras.Add($"VAR {Etiqueta(diaSup)}");
        }

        if (!string.IsNullOrWhiteSpace(diaInf) && !MismoDiametro(diaSup, diaInf))
        {
            barras.Add($"VAR {Etiqueta(diaInf)}");
        }

        if (nInt > 0 && !string.IsNullOrWhiteSpace(diaInt))
        {
            barras.Add($"{nInt} VAR {Etiqueta(diaInt)}");
        }

        if (barras.Count > 0)
        {
            lineas.Add(string.Join(" + ", barras));
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

    private void RotuloDelDado(ZapataCad z, TrazoZapata.Acomodo a, bool lindero)
    {
        if (a.YDadoTop <= a.YZapTop + 0.02)
        {
            return;
        }

        var y = (a.YZapTop + a.YDadoTop) / 2;

        var texto = TextoElemento("DADO", z.IdDado, z.VarDadoSup, z.VarDadoInf,
            z.NIntDado, z.VarIntDado, z.EstriboDado, z.SepEstriboDado);

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

    private void RotuloDeLaColumna(ZapataCad z, TrazoZapata.Acomodo a, bool lindero)
    {
        var y = a.YDadoTop + (AlturaColumnaRep * ColumnaFraccionCorte / 2);

        var texto = TextoElemento("COLUMNA", z.IdColumna, z.VarColSup, z.VarColInf,
            z.NIntColumna, z.VarIntColumna, z.EstriboColumna, z.SepEstriboColumna);

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

    /// <summary>Port de <c>RotularParrillaInferiorZA</c>, con sus dos leaders.</summary>
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

        var xPuntaCirc = xCircIzq + sepCircM;
        var xPuntaBarra = Math.Clamp(
            xPuntaCirc + 0.15,
            xPuntaCirc + 0.06,
            xCaraIzq + ((xCaraDer - xCaraIzq) * 0.32));

        var texto = TextoDeParrilla("PARRILLA INFERIOR", varBarra, sepBarra, varCirc, sepCirc,
            "INFERIOR", "SUPERIOR");

        var xTexto = xBase + 0.18;
        var yTexto = yZapBot - 0.10;

        var mt = Mtexto(xTexto, yTexto, texto, AltoMtexto, CapaRotulos, conFondo: true);

        if (mt is null)
        {
            return;
        }

        var caja = Caja(mt);

        Leader(xPuntaCirc, yCirc, caja?.X1 ?? xTexto, yTexto);
        Leader(xPuntaBarra, yBarra, caja?.X2 ?? xTexto, yTexto);
    }

    /// <summary>El rótulo de la parrilla superior de la central: sale a la derecha.</summary>
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

        var texto = TextoDeParrilla("PARRILLA SUPERIOR", varBarra, sepBarra, varCirc, sepCirc,
            "SUPERIOR", "INFERIOR");

        var mt = Mtexto(xBase + anchoZapata + 0.10, yZapTop + 0.16, texto, AltoMtexto,
            CapaRotulos, conFondo: true);

        if (mt is null)
        {
            return;
        }

        var caja = Caja(mt);
        var x = caja?.X1 ?? xBase + anchoZapata;

        Leader(xPuntaBarra, yBarra, x, yZapTop + 0.16);
        Leader(xCircDer, yCirc, x, yZapTop + 0.16);
    }

    /// <summary>
    /// Port de <c>RotularParrillaSuperiorZALindero</c>: va <b>centrado</b> sobre la zapata.
    /// </summary>
    /// <remarks>
    /// En el lindero el rótulo del dado ya ocupa la izquierda, así que el de la parrilla superior
    /// se centra sobre el lomo: es lo que evita el amontonamiento que tenía la macro V1.
    /// </remarks>
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
        var yZapTop = yZapBot + espZapata;
        var yBarra = yZapTop - rec - (dBarra / 2);
        var yCirc = yBarra - (dBarra / 2) - (dCirc / 2);

        var xCentro = xBase + (anchoZapata / 2);
        var yTexto = yZapTop + 0.23;

        var texto = TextoDeParrilla("PARRILLA SUPERIOR", varBarra, sepBarra, varCirc, sepCirc,
            "SUPERIOR", "INFERIOR");

        var mt = Mtexto(xCentro, yTexto, texto, AltoMtexto, CapaRotulos, conFondo: true);

        if (mt is null)
        {
            return;
        }

        var caja = Caja(mt);

        Leader(xBase + (anchoZapata * 0.32), yBarra, caja?.X1 ?? xCentro, yTexto);
        Leader(xBase + (anchoZapata * 0.66), yCirc, caja?.X2 ?? xCentro, yTexto);
    }

    /// <summary>Port de <c>TextoVarSep</c>, con el «AMBOS SENTIDOS» de la macro.</summary>
    private string TextoDeParrilla(
        string titulo, string? varBarra, string? sepBarra, string? varCirc, string? sepCirc,
        string sufijoBarra, string sufijoCirc)
    {
        var iguales = MismoDiametro(varBarra, varCirc)
                      && TrazoZapata.SeparacionM(sepBarra, -1)
                         == TrazoZapata.SeparacionM(sepCirc, -2);

        if (iguales)
        {
            return $"{titulo}\n{VarSep(varBarra, sepBarra, string.Empty)}\nAMBOS SENTIDOS";
        }

        var lineas = new List<string> { titulo };

        var a = VarSep(varBarra, sepBarra, sufijoBarra);
        var b = VarSep(varCirc, sepCirc, sufijoCirc);

        if (a.Length > 0)
        {
            lineas.Add(a);
        }

        if (b.Length > 0)
        {
            lineas.Add(b);
        }

        return string.Join("\n", lineas);
    }

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
