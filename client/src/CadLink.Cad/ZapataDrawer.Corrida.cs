namespace CadLink.Cad;

/// <summary>
/// El dibujante de las <b>zapatas corridas</b>: port de <c>ZAPATA CORRIDA CENTRAL V2</c> y
/// <c>ZAPATA CORRIDA LINDERO V2</c>.
/// </summary>
/// <remarks>
/// <para>
/// Va como parcial de <see cref="ZapataDrawer"/> y no como clase aparte a propósito: las corridas
/// necesitan <b>lo mismo</b> que las aisladas para hablar con AutoCAD —las líneas, los hatches,
/// las cotas, los textos, el reintento cuando el programa está ocupado, el bloque propio, el orden
/// de dibujo— y eso son ochocientas líneas de COM ya probadas contra el AutoCAD del usuario.
/// Copiarlas para esta hoja habría duplicado justo la parte que más cuesta arreglar dos veces.
/// </para>
/// <para>
/// Lo que sí es de esta hoja está aquí y solo aquí: el acomodo de la fila, la contratrabe y la
/// cadena como bloque, el muro —de mampostería con su hilada de enrase, o de concreto con su
/// acero—, el hatch de terreno a los lados y la anotación.
/// </para>
/// <para>
/// <b>Ni una medida se calcula aquí.</b> Todas salen de <see cref="TrazoZapataCorrida"/>, que es
/// la misma clase que dibuja la vista previa de la ventana. Es la lección de las aisladas: cuando
/// el dibujante hacía sus cuentas y la previa las suyas, el plano y la pantalla no coincidían.
/// </para>
/// </remarks>
public sealed partial class ZapataDrawer
{
    // ======================================================================
    // Constantes propias de las macros de corrida
    // ======================================================================

    /// <summary>Capa del muro de enrase, con el color de la macro.</summary>
    private const string CapaMuroEnrase = "MURO DE ENRASE";

    /// <summary>Color de la capa del muro de enrase: el 140 de las dos macros.</summary>
    private const int ColorMuroEnrase = 140;

    /// <summary>Nombre del bloque de la zapata de lindero: <c>ZAPATA_LINDERO_</c> + ID.</summary>
    private const string PrefijoBloqueLindero = "ZAPATA_LINDERO_";

    /// <summary>Texto de la plantilla, palabra por palabra como en las dos macros.</summary>
    private const string TextoPlantillaCorrida =
        "Plantilla de concreto simple   F'c: 100 kg/cm\u00B2";

    /// <summary>El rótulo del nivel de terreno.</summary>
    private const string TextoNivelTerreno = "Nivel del terreno";

    // ======================================================================
    // El resumen
    // ======================================================================

    /// <summary>Lo que se dibujó, para decírselo al usuario.</summary>
    public sealed class ResumenCorrida
    {
        /// <summary>Zapatas corridas dibujadas.</summary>
        public int Zapatas { get; set; }

        /// <summary>Cuántas salieron con la sección rellena.</summary>
        public int Rellenas { get; set; }

        /// <summary>Bloques de zapata creados.</summary>
        public int Bloques { get; set; }

        /// <summary>Contratrabes insertadas como bloque.</summary>
        public int Contratrabes { get; set; }

        /// <summary>Cadenas de desplante insertadas como bloque.</summary>
        public int Cadenas { get; set; }

        /// <summary>Piezas de muro de enrase dibujadas.</summary>
        public int PiezasDeEnrase { get; set; }

        /// <summary>Muros de concreto dibujados.</summary>
        public int MurosDeConcreto { get; set; }

        /// <summary>Cotas puestas.</summary>
        public int Cotas { get; set; }

        /// <inheritdoc />
        public override string ToString() =>
            $"Zapatas corridas: {Zapatas}   ·   con relleno: {Rellenas}   ·   "
            + $"bloques: {Bloques}" + Environment.NewLine
            + $"Contratrabes insertadas: {Contratrabes}   ·   "
            + $"cadenas de desplante: {Cadenas}" + Environment.NewLine
            + $"Muros de concreto: {MurosDeConcreto}   ·   "
            + $"piezas de muro de enrase: {PiezasDeEnrase}   ·   cotas: {Cotas}";
    }

    // ======================================================================
    // El punto de entrada
    // ======================================================================

    /// <summary>
    /// Dibuja todas las zapatas corridas, cada una en la X que le toca.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos familias llevan <b>su propio contador</b>: la central crece hacia la derecha desde
    /// el origen y el lindero hacia la izquierda desde <c>−2</c>, así que mezclarlas en un solo
    /// índice dejaría huecos en las dos filas y podría encimarlas.
    /// </para>
    /// <para>
    /// Un fallo en una zapata <b>no aborta el juego</b>: se apunta y se sigue con la siguiente,
    /// igual que en las aisladas. Un plano con nueve zapatas de diez es más útil que ninguno, y el
    /// aviso dice cuál falta.
    /// </para>
    /// </remarks>
    public ResumenCorrida DibujarCorridas(IReadOnlyList<ZapataCorridaCad> zapatas)
    {
        var r = new ResumenCorrida();

        if (zapatas.Count == 0)
        {
            return r;
        }

        AsegurarCapasBase();
        AsegurarCapaDelEnrase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();

        var iCentral = 0;
        var iLindero = 0;

        foreach (var z in zapatas)
        {
            var lindero = TrazoZapataCorrida.EsLindero(z.Tipo);
            var indice = lindero ? iLindero++ : iCentral++;

            try
            {
                var xBase = TrazoZapataCorrida.XBase(z.Tipo, indice, z.AnchoM);

                DibujarCorrida(z, xBase, r);

                r.Zapatas++;
            }
            catch (Exception ex)
            {
                Fallo($"Zapata corrida '{z.Id}'", ex);
            }
        }

        return r;
    }

    /// <summary>Crea la capa del muro de enrase si no existe, con el color de la macro.</summary>
    /// <remarks>
    /// Aparte de <c>AsegurarCapasBase</c> porque solo hace falta en esta hoja: una zapata aislada
    /// no lleva muro de enrase, y crear capas que nadie usa deja el dibujo del usuario con basura.
    /// Si la capa ya existe se deja como está: es la del usuario.
    /// </remarks>
    private void AsegurarCapaDelEnrase()
    {
        if (!_capas.Add(CapaMuroEnrase))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic todas = _doc.Layers;

                try
                {
                    _ = todas.Item(CapaMuroEnrase);
                }
                catch (Exception)
                {
                    dynamic nueva = todas.Add(CapaMuroEnrase);
                    nueva.Color = ColorMuroEnrase;
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Crear la capa '{CapaMuroEnrase}'", ex);
        }
    }

    /// <summary>Dibuja una zapata corrida en <paramref name="xBase"/>.</summary>
    /// <remarks>
    /// El orden es el de las macros, y ese orden importa: primero la <b>contratrabe</b> —porque su
    /// caja manda en el hatch de la zapata y en el hueco de su línea superior—, después el
    /// concreto, luego el acero, y al final —ya <b>fuera</b> del bloque— el texto de la plantilla,
    /// el hatch de terreno, los rótulos y las cotas. El texto de la plantilla va fuera porque con
    /// la sección rellena el sólido del bloque lo taparía.
    /// </remarks>
    private void DibujarCorrida(ZapataCorridaCad z, double xBase, ResumenCorrida r)
    {
        _relleno = SeccionRellena;

        if (_relleno)
        {
            r.Rellenas++;
        }

        var lindero = TrazoZapataCorrida.EsLindero(z.Tipo);

        var a = TrazoZapataCorrida.Colocar(z, xBase);

        var rec = z.RecM > 0 ? z.RecM : TrazoZapataCorrida.RecPorOmision;
        var ancho = z.AnchoM;

        // ---------- El bloque propio de la zapata ----------
        var nombreBloque = string.Empty;
        var usaBloque = false;

        if (ZapataComoBloque)
        {
            nombreBloque = NombreBloqueLibre(
                lindero ? PrefijoBloqueLindero + (z.Id ?? string.Empty).Trim() : z.Id);

            var blk = CrearBloqueVacio(nombreBloque, xBase, a.YZapBot);

            if (blk is not null)
            {
                _cont = blk;
                usaBloque = true;
            }
        }

        // ---------- La plantilla de concreto simple ----------
        PlantillaConcretoSimple(xBase, a.YZapBot, ancho, TrazoZapataCorrida.PlantillaEspesor);

        // ---------- LA CONTRATRABE, PRIMERO ----------
        //
        // Se inserta antes de dibujar la zapata porque su caja decide tres cosas: dónde NO va el
        // hatch de concreto, dónde se interrumpe la línea superior de la zapata, y de dónde
        // arranca el muro. Es el mismo orden de las dos macros.
        //
        // SE APOYA EN yZapBot, QUE ES EL PAÑO DE ARRIBA DE LA PLANTILLA. Aquí estaba un error del
        // port: se estaba apoyando en el LOMO de la zapata, y así la contratrabe salía flotando
        // encima en lugar de arrancar del desplante y atravesar el espesor. Las dos macros lo
        // dicen igual —la central con
        //     InsertarBloqueAlineado ..., xCentro, yZapBot, True
        // y la de lindero con
        //     InsertarBloqueCT_EsquinaInferiorDerecha ..., xBase + anchoZapata, yZapBot
        // — y de ahí sale que la contratrabe se cuele por dentro de la zapata, que es lo que hace
        // que su línea superior se interrumpa.
        var xCtIzq = a.XMuroIzq;
        var xCtDer = a.XMuroDer;
        var yCtTop = a.YZapTop + TrazoZapataCorrida.ContratrabeAltoPorOmision;
        var hayCt = false;

        if (z.HayContratrabe)
        {
            // Central: centrada en el eje. Lindero: por su esquina inferior DERECHA, que es el
            // paño del lindero. Las dos, apoyadas en el paño de arriba de la plantilla.
            var caja = InsertarBloqueApoyado(
                z.IdContratrabe, CapaConcreto,
                lindero ? a.XDer : a.XCentro, a.YZapBot, lindero);

            if (caja is not null)
            {
                xCtIzq = caja.Value.X1;
                xCtDer = caja.Value.X2;
                yCtTop = caja.Value.Y2;
                hayCt = true;
            }
            else
            {
                Nota($"Zapata corrida '{z.Id}': no se encontró el bloque de la contratrabe "
                     + $"'{z.IdContratrabe}', así que la sección sale sin ella. Dibújala una vez "
                     + "en AutoCAD con ese nombre y vuelve a generar.");
            }
        }

        if (hayCt)
        {
            r.Contratrabes++;
        }

        // ¿La contratrabe se mete en el espesor de la zapata?
        var ctCruza = hayCt && yCtTop > a.YZapTop + 1e-4
            && xCtIzq > xBase + 1e-4 && xCtIzq < a.XDer - 1e-4;

        // ---------- El concreto de la zapata ----------
        if (ctCruza)
        {
            if (xCtIzq - xBase > 0.001)
            {
                HatchConcreto(xBase, a.YZapBot, xCtIzq - xBase, z.EspesorM, CapaConcreto);
            }

            if (a.XDer - xCtDer > 0.001)
            {
                HatchConcreto(xCtDer, a.YZapBot, a.XDer - xCtDer, z.EspesorM, CapaConcreto);
            }
        }
        else
        {
            HatchConcreto(xBase, a.YZapBot, ancho, z.EspesorM, CapaConcreto);
        }

        // ---------- El contorno, con su línea superior interrumpida ----------
        //
        // El hueco lo abre la contratrabe y, en el muro de concreto, también el propio muro: por
        // ahí el concreto es continuo y una línea ahí sería una junta que no existe.
        var hayHueco = ctCruza;
        var xHuecoIzq = xCtIzq;
        var xHuecoDer = xCtDer;

        if (z.MuroEsConcreto)
        {
            if (!hayHueco)
            {
                hayHueco = true;
                xHuecoIzq = a.XMuroIzq;
                xHuecoDer = a.XMuroDer;
            }
            else
            {
                xHuecoIzq = Math.Min(xHuecoIzq, a.XMuroIzq);
                xHuecoDer = Math.Max(xHuecoDer, a.XMuroDer);
            }
        }

        ContornoCorrida(xBase, a.YZapBot, ancho, z.EspesorM, hayHueco, xHuecoIzq, xHuecoDer);

        // ---------- Las parrillas ----------
        ParrillaZapata(
            xBase, a.YZapBot, ancho, z.EspesorM, rec,
            z.VarInf, z.VarInfTrans, z.SepInfTrans, superior: false);

        if (z.DobleParrilla)
        {
            ParrillaZapata(
                xBase, a.YZapBot, ancho, z.EspesorM, rec,
                z.VarSup, z.VarSupTrans, z.SepSupTrans, superior: true);
        }

        // ---------- El muro ----------
        var yCadenaBot = a.YTerreno;
        var xCadIzq = a.XMuroIzq;
        var xCadDer = a.XMuroDer;
        var hayCadena = false;

        TrazoZapataCorrida.Muro? muroConcreto = null;
        TrazoZapataCorrida.EjesAcero ejesMuro = default;
        TrazoZapataCorrida.Enrase? enrase = null;
        var barrasMuro = Array.Empty<TrazoZapataCorrida.VarillaMuro>();
        var diamBarrasMuro = 0.0;

        if (z.MuroEsConcreto)
        {
            var hecho = MuroDeConcreto(z, a, lindero, rec, hayCt ? yCtTop : a.YZapTop, r);

            if (hecho is not null)
            {
                muroConcreto = hecho.Value.Muro;
                ejesMuro = hecho.Value.Ejes;
                barrasMuro = hecho.Value.Barras;
                diamBarrasMuro = hecho.Value.DiamMuro;
            }
        }
        else
        {
            // Mampostería: la cadena de desplante primero, porque su fondo es el tope del enrase
            // y su ancho es el que se enrasa.
            if (z.HayCadena)
            {
                var caja = InsertarBloqueColgado(
                    z.IdCadena, CapaConcreto,
                    lindero ? a.XDer : a.XCentro, a.YTerreno, lindero);

                if (caja is not null)
                {
                    xCadIzq = caja.Value.X1;
                    xCadDer = caja.Value.X2;
                    yCadenaBot = caja.Value.Y1;
                    hayCadena = true;
                    r.Cadenas++;
                }
                else
                {
                    Nota($"Zapata corrida '{z.Id}': no se encontró el bloque de la cadena de "
                         + $"desplante '{z.IdCadena}'. El muro de enrase remata en el nivel de "
                         + "terreno.");
                }
            }

            if (!hayCadena)
            {
                // Lo que suponen las macros cuando no hay bloque que medir.
                yCadenaBot = a.YTerreno - TrazoZapataCorrida.CadenaAltoPorOmision;
            }

            enrase = MuroDeEnrase(
                z, a, hayCt ? yCtTop : a.YZapTop, yCadenaBot, xCadIzq, xCadDer, r);
        }

        // ---------- Se inserta el bloque de la zapata ----------
        _cont = _ms;

        if (usaBloque && InsertarBloquePropio(nombreBloque, xBase, a.YZapBot, CapaBloqueZapata))
        {
            r.Bloques++;
        }

        // ---------- El terreno, ya fuera del bloque ----------
        //
        // A los dos lados del muro, que es lo que hace la macro de lindero. La central lo parte en
        // bandas para rodear cada obstáculo; aquí se rodea el muro, que es el obstáculo que
        // siempre está, y la contratrabe cuando sobresale del muro.
        var xObsIzq = Math.Min(a.XMuroIzq, hayCt ? xCtIzq : a.XMuroIzq);
        var xObsDer = Math.Max(a.XMuroDer, hayCt ? xCtDer : a.XMuroDer);

        HatchTerreno(xBase, a.XDer, xObsIzq, xObsDer, a.YZapTop, a.YTerreno);

        Linea(xBase, a.YTerreno, a.XDer, a.YTerreno, CapaTerreno);

        // ---------- El texto de la plantilla ----------
        PlantillaTextoCorrida(xBase, a.YZapBot, ancho);

        // ---------- El rótulo del nivel de terreno ----------
        var (xNivel, yNivel) = TrazoZapataCorrida.PosicionTextoNivel(a);

        Mtexto(xNivel, yNivel, TextoNivelTerreno,
            TrazoZapataCorrida.AltoTextoNivel, CapaRotulos, conFondo: true);

        // ---------- LOS RÓTULOS DE LAS PARRILLAS, CON SUS LEADERS ----------
        //
        // Son los MISMOS que las aisladas: las cuatro macros arman el texto igual —«VAR #4 @ 15 cm
        // / AMBOS SENTIDOS» cuando las dos varillas coinciden, y dos renglones de SUPERIOR e
        // INFERIOR cuando no— y los cuelgan de las mismas distancias. Por eso se llaman las
        // rutinas que ya estaban y no se escriben otras: un rótulo con dos versiones acaba con dos
        // planos distintos.
        // El TOPE de las flechas: si hay contratrabe, ninguna punta entra en su huella. Es el
        // recorte de franja de las dos macros de corrida —«zonaR = xCTL − 0.02»—, y hace falta
        // porque una flecha debajo de la contratrabe no dice a qué varilla apunta.
        double? topePuntas = ctCruza ? xCtIzq - 0.02 : null;

        RotuloParrillaInferior(xBase, a.YZapBot, ancho, rec,
            z.VarInf, z.SepInf, z.VarInfTrans, z.SepInfTrans, topePuntas);

        if (z.DobleParrilla && Diam(z.VarSup) > 0)
        {
            if (lindero)
            {
                RotuloParrillaSuperiorLindero(xBase, a.YZapBot, ancho, z.EspesorM, rec,
                    z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans);
            }
            else
            {
                RotuloParrillaSuperiorCentral(xBase, a.YZapBot, ancho, z.EspesorM, rec,
                    z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans);
            }
        }

        // ---------- LOS RÓTULOS PROPIOS DE ESTA HOJA ----------
        if (hayCt)
        {
            RotuloDeLaContratrabe(z, a, lindero, xCtIzq, xCtDer, a.YZapBot, yCtTop);
        }

        if (enrase is not null && enrase.Value.Piezas > 0)
        {
            RotuloDelEnrase(a, lindero, enrase.Value);
        }

        if (hayCadena)
        {
            RotuloDeLaCadena(z, a, lindero, xCadIzq, xCadDer, yCadenaBot);
        }

        if (muroConcreto is not null)
        {
            RotuloDelMuroDeConcreto(z, a, lindero, muroConcreto.Value, ejesMuro);
        }

        // Las cotas de las patas del muro, ya fuera del bloque.
        CotasDeLasPatasDelMuro(a, lindero, barrasMuro, diamBarrasMuro, rec, r);

        // ---------- Las cotas y el rótulo ----------
        CotasDeLaCorrida(z, a, hayCt, xCtIzq, xCtDer, lindero, r);

        RotuloDeLaCorrida(z, a, lindero);
    }

    // ======================================================================
    // El contorno
    // ======================================================================

    /// <summary>
    /// Port de <c>DibujarContornoZapata</c>: el rectángulo con la línea de arriba interrumpida.
    /// </summary>
    private void ContornoCorrida(
        double x, double y, double w, double h,
        bool hayHueco, double xHuecoIzq, double xHuecoDer)
    {
        var yTop = y + h;
        var xDer = x + w;

        Linea(x, y, xDer, y, CapaConcreto);
        Linea(x, y, x, yTop, CapaConcreto);
        Linea(xDer, y, xDer, yTop, CapaConcreto);

        if (!hayHueco)
        {
            Linea(x, yTop, xDer, yTop, CapaConcreto);
            return;
        }

        var a = Math.Max(xHuecoIzq, x);
        var b = Math.Min(xHuecoDer, xDer);

        if (a > x + 1e-6)
        {
            Linea(x, yTop, a, yTop, CapaConcreto);
        }

        if (b < xDer - 1e-6)
        {
            Linea(b, yTop, xDer, yTop, CapaConcreto);
        }
    }

    // ======================================================================
    // El muro de enrase
    // ======================================================================

    /// <summary>
    /// Port de <c>DibujarMuroEnraseElevacion</c>: la hilada de piezas con sus juntas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El reparto lo hace <see cref="TrazoZapataCorrida.MuroDeEnrase"/>, que es el mismo que ve la
    /// vista previa. Aquí solo se pinta, y en el orden que manda la macro cuando la sección va
    /// rellena: <b>primero todos los rellenos</b> —piezas en el 253 y juntas en el 252— y
    /// <b>después todos los contornos</b>, que además se mandan al frente con la tabla de orden de
    /// dibujo. Al revés, cada relleno tapaba la línea de la pieza de abajo.
    /// </para>
    /// <para>
    /// Y se pinta en el orden de <b>creación</b>, no solo con el orden de dibujo: el muro se
    /// dibuja dentro de un bloque, y el orden de dibujo no viaja con el bloque; el de creación sí.
    /// </para>
    /// </remarks>
    private TrazoZapataCorrida.Enrase? MuroDeEnrase(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a,
        double yBase, double yTope, double xCadIzq, double xCadDer, ResumenCorrida r)
    {
        // El ancho del enrase es el de la CADENA cuando la hay: de ahí le viene el nombre.
        var xIzq = xCadIzq;
        var ancho = xCadDer - xCadIzq;

        if (ancho <= 0)
        {
            xIzq = a.XMuroIzq;
            ancho = a.XMuroDer - a.XMuroIzq;
        }

        var e = TrazoZapataCorrida.MuroDeEnrase(xIzq, ancho, yBase, yTope);

        if (e.Piezas == 0)
        {
            if (yTope - yBase > 1e-4)
            {
                Nota($"Zapata corrida '{z.Id}': entre la contratrabe y la cadena quedan "
                     + $"{(yTope - yBase) * 100:0.#} cm, menos de los 2 cm que la macro pide para "
                     + "dibujar el muro de enrase.");
            }

            return null;
        }

        var contornos = new List<object>();

        // ---- Pasada 1: los rellenos ----
        if (_relleno)
        {
            foreach (var yb in e.YBases)
            {
                HatchRect(e.XIzq, yb, e.Ancho, e.AltoPieza, CapaMuroEnrase,
                    "SOLID", 1, string.Empty, TrazoZapataCorrida.EnraseColorPieza);
            }

            for (var i = 0; i < e.Piezas - 1; i++)
            {
                var yJunta = e.YBases[i] + e.AltoPieza;

                var xj = e.XIzq + TrazoZapataCorrida.EnraseDesfaseLado;
                var wj = e.Ancho - (2 * TrazoZapataCorrida.EnraseDesfaseLado);

                if (wj > 0)
                {
                    HatchRect(xj, yJunta, wj, e.Junta, CapaMuroEnrase,
                        "SOLID", 1, string.Empty, TrazoZapataCorrida.EnraseColorJunta);
                }
            }
        }

        // ---- Pasada 2: los contornos ----
        foreach (var yb in e.YBases)
        {
            var pieza = Rectangulo(e.XIzq, yb, e.XIzq + e.Ancho, yb + e.AltoPieza, CapaMuroEnrase);

            Var(pieza);
            Apuntar(contornos, pieza);

            r.PiezasDeEnrase++;
        }

        for (var i = 0; i < e.Piezas - 1; i++)
        {
            var yJ1 = e.YBases[i] + e.AltoPieza;
            var yJ2 = yJ1 + e.Junta;

            var xj1 = e.XIzq + TrazoZapataCorrida.EnraseDesfaseLado;
            var xj2 = e.XIzq + e.Ancho - TrazoZapataCorrida.EnraseDesfaseLado;

            var l1 = Linea(xj1, yJ1, xj1, yJ2, CapaMuroEnrase);
            var l2 = Linea(xj2, yJ1, xj2, yJ2, CapaMuroEnrase);

            Var(l1);
            Var(l2);
            Apuntar(contornos, l1);
            Apuntar(contornos, l2);
        }

        if (_relleno)
        {
            AlFrente(_cont, contornos);
        }

        return e;
    }

    // ======================================================================
    // El muro de concreto
    // ======================================================================

    /// <summary>
    /// El muro de concreto: su relleno, su contorno, sus varillas de punta y su acero con pata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos macros hacen lo mismo hasta el arranque del acero, y ahí se separan: la
    /// <b>central</b> dobla cada varilla hacia <b>su</b> lado y las dos a la misma altura; la de
    /// <b>lindero</b> las dobla las dos a la <b>izquierda</b> y a <b>dos alturas</b>, porque por la
    /// derecha está el lindero y dos patas al mismo nivel se montarían una sobre otra. Esa
    /// diferencia la resuelve <see cref="TrazoZapataCorrida"/>, no este método.
    /// </para>
    /// </remarks>
    private (TrazoZapataCorrida.Muro Muro,
             TrazoZapataCorrida.EjesAcero Ejes,
             TrazoZapataCorrida.VarillaMuro[] Barras,
             double DiamMuro)? MuroDeConcreto(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero,
        double rec, double yContratrabeTop, ResumenCorrida r)
    {
        var m = TrazoZapataCorrida.ColocarMuro(a, yContratrabeTop, a.YTerreno);

        var altura = m.YTope - m.YBase;

        if (altura <= 0)
        {
            Nota($"Zapata corrida '{z.Id}': el muro de concreto sale de altura cero o negativa "
                 + "—la contratrabe llega al nivel de terreno—, así que no se dibujó.");
            return null;
        }

        var ancho = m.XDer - m.XIzq;

        // El relleno primero y el contorno después, para que la línea quede encima.
        // El AR-CONC del muro va con SU escala: la macro usa 0.05, cien veces la de la zapata.
        if (_relleno)
        {
            HatchConcreto(m.XIzq, m.YBase, ancho, altura, CapaConcreto);
        }
        else
        {
            HatchRect(m.XIzq, m.YBase, ancho, altura, CapaConcreto,
                PatronConcreto, TrazoZapataCorrida.ConcretoEscalaMuro, string.Empty, 0);
        }

        Rectangulo(m.XIzq, m.YBase, m.XDer, m.YTope, CapaConcreto);

        r.MurosDeConcreto++;

        var diamMuro = Diam(z.VarMuro);

        if (diamMuro <= 0)
        {
            Nota($"Zapata corrida '{z.Id}': el muro de concreto no tiene varilla capturada, así "
                 + "que sale sin acero.");
            return (m, TrazoZapataCorrida.EjesDelAcero(m, z.MuroDobleParrilla),
                Array.Empty<TrazoZapataCorrida.VarillaMuro>(), 0);
        }

        var capa = CapaVar(z.VarMuro);

        AsegurarCapaVarilla(capa);

        var ejes = TrazoZapataCorrida.EjesDelAcero(m, z.MuroDobleParrilla);

        if (z.MuroDobleParrilla && !ejes.Doble)
        {
            Nota($"Zapata corrida '{z.Id}': el muro de {ancho * 100:0.#} cm es demasiado delgado "
                 + "para doble parrilla, así que el acero va al centro. Es lo que hace la macro.");
        }

        // ---------- Las varillas que se ven de punta ----------
        var ys = TrazoZapataCorrida.CirculosDelMuro(
            m, a.YTerreno, diamMuro, TrazoZapata.SeparacionM(z.SepMuroVert));

        foreach (var y in ys)
        {
            HatchCirculoVarilla(ejes.X1, y, diamMuro / 2, capa);

            if (ejes.Doble)
            {
                HatchCirculoVarilla(ejes.X2, y, diamMuro / 2, capa);
            }
        }

        // ---------- El acero vertical con su pata ----------
        var diamInf = Diam(z.VarInf);
        var diamInfT = Diam(z.VarInfTrans);

        if (diamInf <= 0)
        {
            Nota($"Zapata corrida '{z.Id}': sin la varilla de la parrilla inferior no se puede "
                 + "colocar el arranque del muro, así que no lleva patas.");
            return (m, ejes, Array.Empty<TrazoZapataCorrida.VarillaMuro>(), diamMuro);
        }

        if (diamInfT <= 0)
        {
            diamInfT = diamInf;
        }

        var p = TrazoZapataCorrida.ParrillaEnAlzado(
            a, z.EspesorM, rec, diamInf, diamInfT,
            TrazoZapata.SeparacionM(z.SepInfTrans), superior: false);

        var yPata = TrazoZapataCorrida.YDeLaPata(
            p.YBarra, diamInf, p.YCirculos, diamInfT, diamMuro, lindero);

        var desplazamiento = TrazoZapataCorrida.DesplazamientoDelMuro(Diam(z.SepMuroHoriz));

        var barras = lindero
            ? TrazoZapataCorrida.VerticalesLindero(
                a, ejes, yPata, diamMuro, desplazamiento, rec, FactorGanchoDiametros)
            : TrazoZapataCorrida.VerticalesCentral(
                ejes, a.YTerreno, yPata, diamMuro, desplazamiento, FactorGanchoDiametros);

        foreach (var b in barras)
        {
            VarillaDelMuro(b, diamMuro, capa, lindero);
        }

        // Las COTAS de las patas NO se dibujan aquí: irían dentro del bloque de la zapata, y en
        // las dos macros van al espacio modelo —la central llama a AcotarDoblesMuro con acMsp—.
        // Se devuelven las barras y las acota quien ya cerró el bloque.
        return (m, ejes, barras, diamMuro);
    }

    /// <summary>
    /// Una varilla vertical del muro: sus dos caras y la pata, con su doblez.
    /// </summary>
    /// <remarks>
    /// Se dibuja con el <b>ancho de la varilla</b> —dos caras y no una línea— porque es lo que
    /// hacen las dos macros: en una sección a escala 1:10 una varilla del #4 se ve, y el armador
    /// distingue el arranque del muro del acero de la parrilla por su grosor.
    /// </remarks>
    private void VarillaDelMuro(
        TrazoZapataCorrida.VarillaMuro b, double diam, string capa, bool lindero)
    {
        var mitad = diam / 2;
        var xFin = b.XFinDoblez;
        var s = b.Sentido;

        // LOS RADIOS DEL CODO NO SON LOS MISMOS EN LAS DOS MACROS, y de ahí sale que el doblez del
        // lindero se vea más abierto: la central usa radio interior de medio diámetro y exterior
        // de uno —r = Ø/2, rIn = r/2, rOut = r—, y la de lindero interior de un diámetro y
        // exterior de dos. Se respetan los dos.
        var rIn = lindero ? diam : diam / 4;
        var rOut = lindero ? 2 * diam : diam / 2;

        // Los centros de los dos arcos, tal como los calculan las macros.
        var cxIn = b.X + (s * (mitad + rIn));
        var cyIn = b.YEsquina + mitad + rIn;

        var cxOut = b.X - (s * (mitad - rOut));
        var cyOut = b.YEsquina - mitad + rOut;

        // Las caras del tramo recto acaban donde empieza cada arco, no a la misma altura: es lo
        // que hace que la ele se lea como una varilla doblada y no como dos piezas cruzadas.
        var xDentro = b.X + (s * mitad);
        var xFuera = b.X - (s * mitad);

        // En modo RELLENO, el interior de la varilla se pinta con el color de su capa. En modo
        // normal NO: la macro solo rellena con B3 = 1, y una varilla maciza sobre el hatch del
        // concreto tapa el rayado.
        if (_relleno)
        {
            RellenarTramoDeVarilla(
                Math.Min(xFuera, xDentro), Math.Max(xFuera, xDentro),
                cyIn, b.YTop, capa);

            RellenarTramoDeVarilla(
                Math.Min(xFin, cxIn), Math.Max(xFin, cxIn),
                b.YEsquina - mitad, b.YEsquina + mitad, capa);
        }

        Var(Linea(xDentro, b.YTop, xDentro, cyIn, capa));
        Var(Linea(xFuera, b.YTop, xFuera, cyOut, capa));

        // El codo: los dos arcos de 90°. Con el sentido a la izquierda van del tercer al cuarto
        // cuadrante, y a la derecha del segundo al tercero, que es lo que hacen las macros.
        var a0 = s < 0 ? 3 * Math.PI / 2 : Math.PI;
        var a1 = s < 0 ? 2 * Math.PI : 3 * Math.PI / 2;

        Var(Arco(cxIn, cyIn, rIn, a0, a1, capa));
        Var(Arco(cxOut, cyOut, rOut, a0, a1, capa));

        // La pata: sus dos caras, cada una desde el final de su arco, y su remate.
        Var(Linea(cxIn, b.YEsquina + mitad, xFin, b.YEsquina + mitad, capa));
        Var(Linea(cxOut, b.YEsquina - mitad, xFin, b.YEsquina - mitad, capa));
        Var(Linea(xFin, b.YEsquina - mitad, xFin, b.YEsquina + mitad, capa));
    }

    /// <summary>Rellena un tramo de varilla con el color de su capa.</summary>
    /// <remarks>
    /// El <c>256</c> es el <b>ByLayer</b> de AutoCAD: el relleno toma el color de la capa de la
    /// varilla —<c>VAR_#4</c>— en lugar de uno escrito aquí, que es lo que permite apagar un
    /// diámetro entero desde el administrador de capas y que el relleno se vaya con él.
    /// </remarks>
    private void RellenarTramoDeVarilla(
        double xIzq, double xDer, double yBot, double yTop, string capa)
    {
        var w = xDer - xIzq;
        var h = yTop - yBot;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        HatchRect(xIzq, yBot, w, h, capa, "SOLID", 1, string.Empty, 256);
    }

    /// <summary>Una varilla vista de punta, rellena con el color de su capa.</summary>
    private void HatchCirculoVarilla(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return;
        }

        var c = Circulo(cx, cy, radio, capa);

        Var(c);

        // El relleno, SOLO con la seccion rellena. Se pidio expresamente, y coincide con lo que
        // hacen las macros con B3 = 1: en modo normal la varilla va hueca, con su contorno, y el
        // rayado del concreto se sigue viendo por detras.
        if (_relleno)
        {
            RellenarCirculo(cx, cy, radio, capa, 256);
        }
    }

    // ======================================================================
    // Los bloques de la contratrabe y de la cadena
    // ======================================================================

    /// <summary>
    /// Inserta un bloque <b>apoyado</b> en una Y, y devuelve su caja ya colocada.
    /// </summary>
    /// <remarks>
    /// Port de <c>InsertarBloqueAlineado</c> con <c>alinearInferior = True</c> y de
    /// <c>InsertarBloqueCT_EsquinaInferiorDerecha</c>: la contratrabe se apoya en el lomo de la
    /// zapata, centrada en el eje si la zapata es central y por su <b>esquina inferior derecha</b>
    /// si es de lindero, porque ese paño es el lindero.
    /// </remarks>
    private (double X1, double Y1, double X2, double Y2)? InsertarBloqueApoyado(
        string nombre, string capa, double xObjetivo, double yObjetivo, bool porLaDerecha) =>
        InsertarBloqueYMedir(nombre, capa, xObjetivo, yObjetivo, porLaDerecha, apoyado: true);

    /// <summary>
    /// Inserta un bloque <b>colgado</b> de una Y —su cara de arriba en ella— y devuelve su caja.
    /// </summary>
    /// <remarks>
    /// Port de <c>InsertarBloqueAlineado</c> con <c>alinearInferior = False</c>: la cadena de
    /// desplante cuelga del nivel de terreno.
    /// </remarks>
    private (double X1, double Y1, double X2, double Y2)? InsertarBloqueColgado(
        string nombre, string capa, double xObjetivo, double yObjetivo, bool porLaDerecha) =>
        InsertarBloqueYMedir(nombre, capa, xObjetivo, yObjetivo, porLaDerecha, apoyado: false);

    /// <summary>
    /// Inserta un bloque ajeno, lo recoloca por la esquina que toca y devuelve su caja.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el patrón de las macros: se inserta, se <b>mide</b> con su caja envolvente y se mueve,
    /// porque el punto de inserción de un bloque ajeno puede estar en cualquier parte. Sin medirlo,
    /// la contratrabe sale flotando o enterrada en la zapata.
    /// </para>
    /// <para>
    /// Devuelve la caja <b>ya movida</b>, que es lo que necesita el resto del dibujo: el hatch de
    /// la zapata, el hueco de su línea superior y el ancho del muro de enrase salen de ahí.
    /// </para>
    /// </remarks>
    private (double X1, double Y1, double X2, double Y2)? InsertarBloqueYMedir(
        string nombre, string capa, double xObjetivo, double yObjetivo,
        bool porLaDerecha, bool apoyado)
    {
        var n = (nombre ?? string.Empty).Trim();

        if (!ZapataCorridaCad.HayBloque(n) || !ExisteBloque(n))
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<(double, double, double, double)?>(() =>
            {
                dynamic br = _cont.InsertBlock(
                    new[] { 0d, 0d, 0d }, n, 1d, 1d, 1d, 0d);

                br.Layer = capa;
                br.Update();

                var caja = Caja((object)br);

                if (caja is null)
                {
                    // Sin caja no se puede recolocar. Se queda en el origen y se avisa: es mejor
                    // ver el bloque fuera de sitio que no verlo y creer que no existe.
                    Nota($"No se pudo medir el bloque '{n}': quedó en el origen del dibujo.");
                    return null;
                }

                var c = caja.Value;

                var dx = porLaDerecha
                    ? xObjetivo - c.X2
                    : xObjetivo - ((c.X1 + c.X2) / 2);

                var dy = apoyado
                    ? yObjetivo - c.Y1
                    : yObjetivo - c.Y2;

                if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
                {
                    br.Move(new[] { 0d, 0d, 0d }, new[] { dx, dy, 0d });
                    br.Update();
                }

                return (c.X1 + dx, c.Y1 + dy, c.X2 + dx, c.Y2 + dy);
            });
        }
        catch (Exception ex)
        {
            Fallo($"Insertar el bloque '{n}' de la zapata corrida", ex);
            return null;
        }
    }

    // ======================================================================
    // La anotación
    // ======================================================================

    // ======================================================================
    // LOS RÓTULOS CON LEADER DE ESTA HOJA
    // ======================================================================
    //
    // Los cuatro que las macros de corrida tienen y las de aislada no: el muro de enrase, la
    // contratrabe, la cadena de desplante y el muro de concreto. Cada uno con las distancias de
    // SU macro, que no son las mismas en la central y en la de lindero: en la central los rótulos
    // salen hacia la izquierda del eje —hay sitio, la fila crece a la derecha— y en la de lindero
    // se cuelgan del paño del muro, porque a su derecha está el lindero y a su izquierda va la
    // siguiente zapata.

    /// <summary>Anchos de renglón de cada rótulo, en metros. Los de las macros.</summary>
    private const double AnchoRotuloEnrase = 0.26;          // ANCHO_MTEXT_ENRASE
    private const double AnchoRotuloContratrabe = 0.23;     // anchoCT
    private const double AnchoRotuloCadena = 0.32;          // anchoCad
    private const double AnchoRotuloMuroCentral = 0.32;     // ANCHO_MTEXT_MURO_CONC
    private const double AnchoRotuloMuroLindero = 0.25;     // anchoMTextMuro del lindero

    /// <summary>Texto del rótulo del muro de enrase, palabra por palabra.</summary>
    private const string TextoRotuloEnrase = "MURO DE ENRASE DE BLOCK DE CEMENTO";

    /// <summary>
    /// El rótulo del <b>muro de enrase</b>, con su leader al centro de la hilada.
    /// </summary>
    /// <remarks>
    /// La central lo saca por la <b>derecha</b> de la hilada, a 10 cm, y el lindero por la
    /// <b>izquierda</b>, a 30 cm: en el lindero la hilada está pegada al paño derecho de la zapata
    /// y por ese lado no hay dónde poner un renglón.
    /// </remarks>
    private void RotuloDelEnrase(
        TrazoZapataCorrida.Acomodo a, bool lindero, TrazoZapataCorrida.Enrase e)
    {
        var yTop = e.YBases[^1] + e.AltoPieza;
        var yBot = e.YBases[0];

        var xCentro = e.XIzq + (e.Ancho / 2);
        var yCentro = (yBot + yTop) / 2;

        var yTexto = yTop - 0.08;

        if (lindero)
        {
            var xTexto = e.XIzq - 0.3;

            var mt = MtextoAncho(xTexto, yTexto, TextoRotuloEnrase, AnchoRotuloEnrase,
                AnclajeCentro);

            // El leader sale del borde DERECHO del rótulo, que es el lado que mira al muro.
            var caja = Caja(mt);

            Leader(xCentro, yCentro,
                caja?.X2 ?? xTexto,
                caja is null ? yTexto : (caja.Value.Y1 + caja.Value.Y2) / 2);

            return;
        }

        var xTextoCentral = e.XIzq + e.Ancho + 0.1;

        MtextoAncho(xTextoCentral, yTexto, TextoRotuloEnrase, AnchoRotuloEnrase, AnclajeIzquierda);

        Leader(xCentro, yCentro, xTextoCentral, yTexto);
    }

    /// <summary>El rótulo de la <b>contratrabe</b>, con su leader.</summary>
    /// <remarks>
    /// Central: <c>xCentro − 0.62</c> y 30 cm por encima del centro de la contratrabe, con la
    /// punta en su centro. Lindero: <c>xCentroMuro − 0.75</c> y 14 cm por encima de su lomo, con la
    /// punta 4 cm por debajo del lomo —así la flecha entra en la contratrabe y no en el muro—.
    /// </remarks>
    private void RotuloDeLaContratrabe(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero,
        double xCtIzq, double xCtDer, double yCtBot, double yCtTop)
    {
        var texto = $"CONTRATRABE \"{(z.IdContratrabe ?? string.Empty).Trim()}\"";

        var xCentroCt = (xCtIzq + xCtDer) / 2;
        var yCentroCt = (yCtBot + yCtTop) / 2;

        var xTexto = lindero
            ? a.XCentroMuro - 0.75
            : a.XCentro - 0.62;

        var yTexto = lindero
            ? yCtTop + 0.14
            : yCentroCt + 0.3;

        // El anclaje va a la DERECHA del renglón —crece hacia la izquierda—, así que el punto de
        // inserción es el borde derecho: xTexto + su ancho.
        var xIns = xTexto + AnchoRotuloContratrabe;

        MtextoAncho(xIns, yTexto, texto, AnchoRotuloContratrabe, AnclajeDerecha);

        var yPunta = lindero ? yCtTop - 0.04 : yCentroCt;

        Leader(xCentroCt, yPunta, xIns, yTexto);
    }

    /// <summary>El rótulo de la <b>cadena de desplante</b>, con su leader.</summary>
    private void RotuloDeLaCadena(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero,
        double xCadIzq, double xCadDer, double yCadBot)
    {
        var texto = $"CADENA DE DESPLANTE \"{(z.IdCadena ?? string.Empty).Trim()}\"";

        var xCentroCad = (xCadIzq + xCadDer) / 2;
        var yCentroCad = (yCadBot + a.YTerreno) / 2;

        var xTexto = lindero
            ? a.XCentroMuro - 0.85
            : a.XCentro - 0.78;

        var xIns = xTexto + AnchoRotuloCadena;

        MtextoAncho(xIns, yCentroCad, texto, AnchoRotuloCadena, AnclajeDerecha);

        Leader(xCentroCad, yCentroCad, xIns, yCentroCad);
    }

    /// <summary>
    /// El rótulo del <b>muro de concreto</b>: cuatro renglones con su espesor y su armado.
    /// </summary>
    /// <remarks>
    /// El texto es el de las macros, con sus abreviaturas y su punto final: <c>HORIZ.</c> y
    /// <c>VERT.</c>, y el último renglón dice si el acero va en los dos paños o al centro. La
    /// punta del leader se pega a la <b>varilla</b> —la del paño derecho si hay dos— a un 55 % de
    /// la altura del muro, que es donde no choca con los círculos.
    /// </remarks>
    private void RotuloDelMuroDeConcreto(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero,
        TrazoZapataCorrida.Muro m, TrazoZapataCorrida.EjesAcero ejes)
    {
        var espesorCm = (m.XDer - m.XIzq) * 100;

        var texto =
            $"MURO DE CONCRETO e={espesorCm:0.#} cm\n"
            + $"VAR {Etiqueta(z.VarMuro)} @ {SepTexto(z.SepMuroHoriz)} cm HORIZ.\n"
            + $"Y @ {SepTexto(z.SepMuroVert)} cm VERT.\n"
            + (ejes.Doble ? "DOBLE PARRILLA" : "PARRILLA AL CENTRO");

        var xTexto = lindero
            ? m.XIzq - 0.27
            : m.XDer + 0.12 - 0.05;

        var yTexto = a.YTerreno - 0.1;

        var ancho = lindero ? AnchoRotuloMuroLindero : AnchoRotuloMuroCentral;
        var anclaje = lindero ? AnclajeCentro : AnclajeIzquierda;

        MtextoAncho(xTexto, yTexto, texto, ancho, anclaje);

        var xPunta = ejes.Doble ? ejes.X2 : ejes.X1;
        var yPunta = m.YBase + ((m.YTope - m.YBase) * 0.55);

        Leader(xPunta, yPunta, xTexto, yTexto);
    }

    /// <summary>
    /// Un <b>MText con ancho de renglón</b>, para los rótulos que se parten en varias líneas.
    /// </summary>
    /// <remarks>
    /// <see cref="Mtexto"/> pone <c>Width = 0</c>, que es lo que hace falta para un rótulo de una
    /// línea: así no se corta nunca. Pero los cuatro rótulos de esta hoja llevan ancho en las
    /// macros —<c>MURO DE ENRASE DE BLOCK DE CEMENTO</c> en 26 cm son tres renglones— y sin él
    /// saldrían en una tira larguísima que cruza la zapata de al lado.
    /// </remarks>
    private object? MtextoAncho(
        double x, double y, string texto, double ancho, int anclaje)
    {
        if (string.IsNullOrWhiteSpace(texto) || ancho <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic mt = _cont.AddMText(new[] { x, y, 0d }, ancho, texto);
                mt.Layer = CapaRotulos;
                mt.Height = AltoMtexto;

                try
                {
                    mt.Width = ancho;
                    mt.AttachmentPoint = anclaje;
                    mt.InsertionPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Sin anclaje el rótulo queda corrido, pero está.
                }

                try
                {
                    // La máscara de fondo es lo que evita que el hatch del terreno se lea por
                    // detrás de las letras. Es el ConfigurarFondoMText de las macros.
                    mt.BackgroundFill = true;
                    mt.BackgroundScaleFactor = 1.15;
                    mt.UseBackgroundColor = true;
                    mt.BackgroundColor = 7;
                }
                catch (Exception)
                {
                    // Presentación: si no se puede, el texto queda igual.
                }

                mt.Update();

                return (object?)mt;
            });
        }
        catch (Exception ex)
        {
            Fallo("Rótulo de la zapata corrida", ex);
            return null;
        }
    }

    /// <summary>La separación tal como la escriben las macros en el rótulo: sin decimales de más.</summary>
    /// <remarks>
    /// Port de <c>LimpiarSeparacion</c>: de la celda sale el número y se escribe entero cuando lo
    /// es —«20», no «20.0»—, porque es lo que dice el plano.
    /// </remarks>
    private static string SepTexto(string? celda)
    {
        var m = TrazoZapata.SeparacionM(celda, 0) * 100;

        return m <= 0
            ? (celda ?? string.Empty).Trim()
            : m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>El texto de la plantilla, centrado en su franja.</summary>
    /// <remarks>
    /// Va <b>fuera</b> del bloque, como en las aisladas: con la sección rellena, el sólido del
    /// bloque lo taparía.
    /// </remarks>
    private void PlantillaTextoCorrida(double xIzq, double yZapBot, double ancho)
    {
        var esp = TrazoZapataCorrida.PlantillaEspesor;

        Texto(
            xIzq + (ancho / 2), yZapBot - (esp / 2),
            TrazoZapataCorrida.AltoTextoPlantilla, TextoPlantillaCorrida,
            CapaRotulos, Alineacion.Centro);
    }

    /// <summary>
    /// Las cotas de la sección: los anchos por debajo de la plantilla y las alturas a la izquierda.
    /// </summary>
    /// <remarks>
    /// Son las ocho de la macro central y las siete de la de lindero, con sus offsets: el ancho
    /// total abajo, los anchos parciales que parte la contratrabe justo encima, y a la izquierda la
    /// altura total —del terreno al fondo de la plantilla— más las tres parciales. La de la
    /// <b>plantilla</b> lleva el número <b>adentro</b>: son 5 cm, y AutoCAD la sacaría con una
    /// flecha encima del dibujo.
    /// </remarks>
    private void CotasDeLaCorrida(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a,
        bool hayCt, double xCtIzq, double xCtDer, bool lindero, ResumenCorrida r)
    {
        var yFondo = a.YPlantillaBot;

        // ---------- Los anchos ----------
        var yTotal = yFondo - TrazoZapataCorrida.CotaAnchoTotal;

        r.Cotas += Cota(
            a.XBase, yTotal, a.XDer, yTotal, a.XCentro, yTotal,
            vertical: false, dentro: false);

        if (hayCt)
        {
            var yParcial = yFondo - TrazoZapataCorrida.CotaAnchosParciales;

            if (xCtIzq - a.XBase > 0.01)
            {
                r.Cotas += Cota(
                    a.XBase, yParcial, xCtIzq, yParcial, (a.XBase + xCtIzq) / 2, yParcial,
                    vertical: false, dentro: false);
            }

            if (xCtDer - xCtIzq > 0.01)
            {
                r.Cotas += Cota(
                    xCtIzq, yParcial, xCtDer, yParcial, (xCtIzq + xCtDer) / 2, yParcial,
                    vertical: false, dentro: false);
            }

            // La central lleva además el tramo de la derecha. La de lindero NO: ahí la
            // contratrabe llega al paño derecho y esa cota sería de cero.
            if (!lindero && a.XDer - xCtDer > 0.01)
            {
                r.Cotas += Cota(
                    xCtDer, yParcial, a.XDer, yParcial, (xCtDer + a.XDer) / 2, yParcial,
                    vertical: false, dentro: false);
            }
        }

        // ---------- Las alturas ----------
        var xTotal = a.XBase - TrazoZapataCorrida.CotaAlturaTotal;
        var xParcial = a.XBase - TrazoZapataCorrida.CotaAlturasParciales;

        r.Cotas += Cota(
            xTotal, a.YTerreno, xTotal, yFondo, xTotal, (a.YTerreno + yFondo) / 2,
            vertical: true, dentro: false);

        r.Cotas += Cota(
            xParcial, a.YZapBot, xParcial, a.YZapTop, xParcial, (a.YZapBot + a.YZapTop) / 2,
            vertical: true, dentro: false);

        r.Cotas += Cota(
            xParcial, a.YZapTop, xParcial, a.YTerreno, xParcial, (a.YZapTop + a.YTerreno) / 2,
            vertical: true, dentro: false);

        r.Cotas += Cota(
            xParcial, yFondo, xParcial, a.YZapBot, xParcial, (yFondo + a.YZapBot) / 2,
            vertical: true, dentro: true);
    }

    /// <summary>
    /// Las cotas de las <b>patas</b> del muro de concreto, ya fuera del bloque.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Van aquí y no junto al acero porque en las dos macros se dibujan en el <b>espacio
    /// modelo</b>: la central llama a <c>AcotarDoblesMuro acMsp, …</c> y la de lindero acota desde
    /// <c>DibujarVarillaVerticalConDoblezIzq</c>, que también recibe <c>acMsp</c>. Dentro del
    /// bloque quedarían pegadas a la geometría y no se podrían mover en el plano.
    /// </para>
    /// <para>
    /// <b>El offset no es el mismo para las dos patas del lindero.</b> La de abajo se acota a un
    /// 45 % de la separación entre dobleces y la de arriba a 2.2 cm: si las dos llevaran el mismo,
    /// los dos números saldrían uno encima del otro, que es justo lo que la macro evita separando
    /// los dobleces. La central acota las suyas a 4.5 cm, las dos a la misma altura, porque están
    /// a lados contrarios.
    /// </para>
    /// </remarks>
    private void CotasDeLasPatasDelMuro(
        TrazoZapataCorrida.Acomodo a, bool lindero,
        TrazoZapataCorrida.VarillaMuro[] barras, double diamMuro, double rec, ResumenCorrida r)
    {
        if (barras.Length == 0 || diamMuro <= 0)
        {
            return;
        }

        // La separación con la que se repartieron los dos dobleces del lindero: la misma cuenta
        // que hizo la geometría, para que la cota de abajo caiga a su 45 %.
        var sep = lindero
            ? TrazoZapataCorrida.SepDeLosDobleces(a, barras[0].YEsquina, diamMuro, rec)
            : 0;

        for (var i = 0; i < barras.Length; i++)
        {
            var b = barras[i];

            var offset = lindero
                ? (i == 0
                    ? sep * TrazoZapataCorrida.CotaDoblezLinderoFraccion
                    : TrazoZapataCorrida.CotaDoblezLindero)
                : TrazoZapataCorrida.CotaDoblezCentral;

            var xIzq = Math.Min(b.X, b.XFinDoblez);
            var xDer = Math.Max(b.X, b.XFinDoblez);

            var yCota = b.YEsquina + (diamMuro / 2);

            r.Cotas += Cota(
                xIzq, yCota, xDer, yCota,
                (xIzq + xDer) / 2, yCota + offset,
                vertical: false, dentro: false);
        }
    }

    /// <summary>Los tres renglones del rótulo, centrados en el eje de la sección.</summary>
    /// <remarks>
    /// Ojo con el título del lindero: dice <c>ZAPATA DE LINDERO</c> y <b>no</b> «corrida». Así está
    /// en su macro, y así se queda: es el texto que ya está en los planos entregados.
    /// </remarks>
    private void RotuloDeLaCorrida(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero)
    {
        var id = (z.Id ?? string.Empty).Trim();

        var titulo = id.Length > 0
            ? $"{z.TipoTexto} \"{id}\""
            : z.TipoTexto;

        Texto(a.XCentro, TrazoZapataCorrida.YRotulo(a.YZapBot, 0),
            TrazoZapataCorrida.RotuloAltoTitulo, titulo, CapaRotulos, Alineacion.Centro);

        Texto(a.XCentro, TrazoZapataCorrida.YRotulo(a.YZapBot, 1),
            TrazoZapataCorrida.RotuloAltoElevacion, "ELEVACION", CapaRotulos, Alineacion.Centro);

        var rec = (z.RecM > 0 ? z.RecM : TrazoZapataCorrida.RecPorOmision) * 100;

        // El mismo renglón que las aisladas, con el f'c delante como en las macros de corrida.
        var fc = string.IsNullOrWhiteSpace(z.Fc)
            ? string.Empty
            : $"f'c= {z.Fc.Trim()} kg/cm\u00B2    ";

        var escala = $"{fc}Rec. {rec:0.#}cm    Escala 1:10";

        Texto(a.XCentro, TrazoZapataCorrida.YRotulo(a.YZapBot, 2),
            TrazoZapataCorrida.RotuloAltoEscala, escala, CapaRotulos, Alineacion.Centro);
    }
}
