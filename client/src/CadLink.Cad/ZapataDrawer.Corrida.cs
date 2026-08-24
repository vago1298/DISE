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
        // A los dos lados de lo que sobresale, y CEÑIDO A SU FORMA. La macro central lo parte en
        // bandas horizontales para rodear cada obstáculo, y eso es exactamente lo que hace falta
        // aquí: la contratrabe es más ancha que el muro, así que un relleno de un solo paño recto
        // —el de antes— dejaba el bloque de la contratrabe metido en la tierra, asomando por
        // encima del rayado. Ahora se le pasa la caja de CADA pieza y el contorno sale en
        // escalera, ajustándose solo a la que sea más ancha en cada altura.
        var obstaculos = new List<ObstaculoTerreno>();

        if (hayCt)
        {
            obstaculos.Add(new ObstaculoTerreno(a.YZapBot, yCtTop, xCtIzq, xCtDer));
        }

        if (muroConcreto is not null)
        {
            var m = muroConcreto.Value;
            obstaculos.Add(new ObstaculoTerreno(m.YBase, m.YTope, m.XIzq, m.XDer));
        }

        if (enrase is not null && enrase.Value.Piezas > 0)
        {
            var e = enrase.Value;

            obstaculos.Add(new ObstaculoTerreno(
                e.YBases[0], e.YBases[^1] + e.AltoPieza, e.XIzq, e.XIzq + e.Ancho));
        }

        if (hayCadena)
        {
            obstaculos.Add(new ObstaculoTerreno(yCadenaBot, a.YTerreno, xCadIzq, xCadDer));
        }

        HatchTerrenoCorrida(xBase, a.XDer, a.YZapTop, a.YTerreno, obstaculos);

        Linea(xBase, a.YTerreno, a.XDer, a.YTerreno, CapaTerreno);

        // ---------- El texto de la plantilla ----------
        PlantillaTextoCorrida(xBase, a.YZapBot, ancho);

        // ---------- El rótulo del nivel de terreno ----------
        var (xNivel, yNivel) = TrazoZapataCorrida.PosicionTextoNivel(a);

        Mtexto(xNivel, yNivel, TextoNivelTerreno,
            TrazoZapataCorrida.AltoTextoNivel, CapaRotulos, conFondo: true);

        // ---------- LOS RÓTULOS DE LAS PARRILLAS, CON SUS LEADERS ----------
        //
        // Cada parrilla lleva DOS rótulos y no uno: el de la varilla de flexión -la que se ve de
        // canto- a la mitad del tramo izquierdo, y el de la de temperatura -la que se ve de punta-
        // a la mitad del lado derecho. Ver RotulosDeParrillaCorrida.
        //
        // El tope de la contratrabe se aplica ahí mismo: la punta de cada flecha se queda entre
        // las caras de su acero, así que nunca acaba debajo del bloque.
        //
        // La parrilla de abajo escribe PRIMERO y devuelve la caja de sus renglones. La de arriba la
        // recibe y se acomoda con ella: se sube por encima del texto y corre su carril a un lado,
        // que es lo que impide que con doble parrilla los cuatro rótulos y sus cuatro leaders se
        // monten unos sobre otros.
        // Y los dos topes: el paño de lo que hay en medio a la altura de los rótulos, que es la
        // contratrabe cuando sobresale y el muro cuando no. De ahí sale «la mitad de cada lado».
        var xTopeIzq = Math.Min(a.XMuroIzq, hayCt ? xCtIzq : a.XMuroIzq);
        var xTopeDer = Math.Max(a.XMuroDer, hayCt ? xCtDer : a.XMuroDer);

        var huellaInf = RotulosDeParrillaCorrida(
            z, a, rec, z.VarInf, z.SepInf, z.VarInfTrans, z.SepInfTrans, superior: false,
            abajo: default, xTopeIzq, xTopeDer);

        if (z.DobleParrilla && Diam(z.VarSup) > 0)
        {
            RotulosDeParrillaCorrida(
                z, a, rec, z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans, superior: true,
                abajo: huellaInf, xTopeIzq, xTopeDer);
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

            // Y EL CODO. Faltaba, y se veía: el tramo recto y la pata salían macizos y la
            // esquina hueca, con el rayado del concreto por dentro de la varilla. Es la zona
            // curva, así que no se puede rellenar con un rectángulo: se aproxima el sector entre
            // los dos arcos, que es lo que hace la macro de lindero con RellenarCodoVarilla.
            RellenarCodoDeVarilla(cxOut, cyOut, rOut, cxIn, cyIn, rIn, s, capa);
        }

        Var(Linea(xDentro, b.YTop, xDentro, cyIn, capa));
        Var(Linea(xFuera, b.YTop, xFuera, cyOut, capa));

        // El codo: los dos arcos de 90°. Con el sentido a la izquierda van del tercer al cuarto
        // cuadrante, y a la derecha del segundo al tercero, que es lo que hacen las macros.
        Var(Arco(cxIn, cyIn, rIn, AnguloCodo(s, false), AnguloCodo(s, true), capa));
        Var(Arco(cxOut, cyOut, rOut, AnguloCodo(s, false), AnguloCodo(s, true), capa));

        // La pata: sus dos caras, cada una desde el final de su arco, y su remate.
        Var(Linea(cxIn, b.YEsquina + mitad, xFin, b.YEsquina + mitad, capa));
        Var(Linea(cxOut, b.YEsquina - mitad, xFin, b.YEsquina - mitad, capa));
        Var(Linea(xFin, b.YEsquina - mitad, xFin, b.YEsquina + mitad, capa));
    }

    /// <summary>
    /// El ángulo de arranque o de cierre del arco del codo, según hacia dónde dobla.
    /// </summary>
    /// <remarks>
    /// Doblando a la <b>izquierda</b> el codo barre del tercer al cuarto cuadrante —270° a 360°—
    /// y a la derecha del segundo al tercero —180° a 270°—. Son los mismos cuadrantes de las dos
    /// macros, y están en un método para que el contorno y el relleno usen exactamente los mismos.
    /// </remarks>
    private static double AnguloCodo(int sentido, bool cierre) =>
        sentido < 0
            ? (cierre ? 2 * Math.PI : 3 * Math.PI / 2)
            : (cierre ? 3 * Math.PI / 2 : Math.PI);

    /// <summary>
    /// Rellena el <b>codo</b> de una varilla: la zona entre sus dos arcos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No se puede rellenar con un rectángulo —es una esquina curva— ni con un sector anular
    /// —los dos arcos <b>no son concéntricos</b>, y eso es de las macros: la cara de dentro del
    /// codo tiene su centro y su radio, y la de fuera los suyos—. Así que se sigue la frontera de
    /// verdad: el arco exterior de ida y el interior de vuelta, con lo que el relleno cae
    /// exactamente donde está la varilla dibujada.
    /// </para>
    /// <para>
    /// Diez segmentos por arco. A escala 1:10, un codo de una varilla del #4 mide 3 mm en el
    /// papel: con diez tramos la curva ya no se distingue de un arco, y el hatch entra sin
    /// rechazos.
    /// </para>
    /// </remarks>
    private void RellenarCodoDeVarilla(
        double cxOut, double cyOut, double rOut,
        double cxIn, double cyIn, double rIn,
        int sentido, string capa)
    {
        const int segmentos = 10;

        if (rOut <= 0 || rIn < 0)
        {
            return;
        }

        var a0 = AnguloCodo(sentido, false);
        var a1 = AnguloCodo(sentido, true);

        var pts = new List<double>();

        for (var i = 0; i <= segmentos; i++)
        {
            var a = a0 + ((a1 - a0) * i / segmentos);

            pts.Add(cxOut + (rOut * Math.Cos(a)));
            pts.Add(cyOut + (rOut * Math.Sin(a)));
        }

        for (var i = segmentos; i >= 0; i--)
        {
            var a = a0 + ((a1 - a0) * i / segmentos);

            pts.Add(cxIn + (rIn * Math.Cos(a)));
            pts.Add(cyIn + (rIn * Math.Sin(a)));
        }

        var borde = Polilinea(pts.ToArray(), capa, cerrada: true);

        if (borde is null)
        {
            return;
        }

        _ = Hatch(borde, "SOLID", 1, capa, 256);

        Borrar(borde);
    }

    // ======================================================================
    // El terreno, ceñido a lo que sobresale
    // ======================================================================

    /// <summary>Una pieza que se come el terreno: su altura y sus dos paños.</summary>
    /// <remarks>
    /// Son las cuatro que pueden sobresalir de la zapata, y ninguna tiene por qué medir lo mismo
    /// que las otras: la <b>contratrabe</b>, el <b>muro de concreto</b>, la <b>hilada de enrase</b>
    /// y la <b>cadena de desplante</b>. Las tres últimas salen de un bloque o de un reparto, así
    /// que su ancho no se sabe hasta que están puestas.
    /// </remarks>
    private readonly record struct ObstaculoTerreno(
        double YBot, double YTop, double XIzq, double XDer);

    /// <summary>
    /// El relleno de tierra de una corrida: dos contornos en <b>escalera</b>, uno por lado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La altura entre el lomo de la zapata y el nivel de terreno se parte en <b>bandas</b> por
    /// cada arranque y cada remate de pieza —es lo que hace la macro central—, y en cada banda el
    /// relleno se detiene en el paño de la pieza más ancha que haya <b>a esa altura</b>. Con eso el
    /// borde del terreno se ajusta solo: si la contratrabe sobresale del muro, la tierra se retira
    /// en la banda de la contratrabe y vuelve a cerrarse encima, contra el paño del muro.
    /// </para>
    /// <para>
    /// Las bandas no se rellenan por separado: se cosen en <b>un solo contorno</b> por lado, porque
    /// dos hatches apilados cortan el rayado en la junta —cada uno arranca su patrón por su
    /// cuenta— y se ve la costura.
    /// </para>
    /// <para>
    /// Si una banda no tiene ninguna pieza —puede pasar entre el remate del enrase y el terreno
    /// cuando la cadena no se encontró— hereda el paño de la banda de al lado, y así el pasillo del
    /// muro se queda limpio en lugar de rellenarse de tierra a media altura.
    /// </para>
    /// </remarks>
    private void HatchTerrenoCorrida(
        double xBase, double xDer, double yDesde, double yHasta,
        List<ObstaculoTerreno> obstaculos)
    {
        if (yHasta - yDesde <= 1e-6 || xDer - xBase <= 1e-6)
        {
            return;
        }

        // ---------- Las bandas ----------
        var cortes = new List<double> { yDesde, yHasta };

        foreach (var o in obstaculos)
        {
            if (o.YBot > yDesde + 1e-6 && o.YBot < yHasta - 1e-6)
            {
                cortes.Add(o.YBot);
            }

            if (o.YTop > yDesde + 1e-6 && o.YTop < yHasta - 1e-6)
            {
                cortes.Add(o.YTop);
            }
        }

        cortes.Sort();

        // ---------- El paño de cada banda ----------
        var ys = new List<double>();
        var izq = new List<double>();
        var der = new List<double>();

        for (var i = 0; i < cortes.Count - 1; i++)
        {
            var yBot = cortes[i];
            var yTop = cortes[i + 1];

            if (yTop - yBot <= 1e-6)
            {
                continue;
            }

            var medio = (yBot + yTop) / 2;

            var xi = double.NaN;
            var xd = double.NaN;

            foreach (var o in obstaculos)
            {
                if (o.YBot > medio || o.YTop < medio)
                {
                    continue;
                }

                xi = double.IsNaN(xi) ? o.XIzq : Math.Min(xi, o.XIzq);
                xd = double.IsNaN(xd) ? o.XDer : Math.Max(xd, o.XDer);
            }

            ys.Add(yBot);
            izq.Add(xi);
            der.Add(xd);
        }

        if (ys.Count == 0)
        {
            return;
        }

        ys.Add(yHasta);

        // Las bandas sin pieza heredan de su vecina: primero hacia arriba, después hacia abajo.
        for (var i = 0; i < izq.Count; i++)
        {
            if (double.IsNaN(izq[i]) && i > 0)
            {
                izq[i] = izq[i - 1];
                der[i] = der[i - 1];
            }
        }

        for (var i = izq.Count - 1; i >= 0; i--)
        {
            if (double.IsNaN(izq[i]) && i < izq.Count - 1)
            {
                izq[i] = izq[i + 1];
                der[i] = der[i + 1];
            }
        }

        for (var i = 0; i < izq.Count; i++)
        {
            if (double.IsNaN(izq[i]))
            {
                // Ninguna pieza en toda la altura: el terreno es un rectángulo de lado a lado.
                HatchRect(xBase, yDesde, xDer - xBase, yHasta - yDesde, CapaTerrenoHatch,
                    PatronTerreno, EscalaTerreno, TranspTerreno, 0);

                return;
            }

            izq[i] = Math.Clamp(izq[i], xBase, xDer);
            der[i] = Math.Clamp(der[i], xBase, xDer);
        }

        HatchEscaleraTerreno(xBase, ys, izq, aLaDerecha: true);
        HatchEscaleraTerreno(xDer, ys, der, aLaDerecha: false);
    }

    /// <summary>Un lado del terreno: el paño recto del extremo y la escalera de las piezas.</summary>
    /// <param name="xPano">El extremo de la zapata por ese lado, que es el paño recto.</param>
    /// <param name="ys">Los cortes de las bandas, de abajo arriba: uno más que anchos.</param>
    /// <param name="xs">Dónde se detiene el relleno en cada banda.</param>
    /// <param name="aLaDerecha">
    /// <c>true</c> para el relleno de la <b>izquierda</b>, que crece hacia la derecha hasta la
    /// escalera; <c>false</c> para el de la derecha.
    /// </param>
    private void HatchEscaleraTerreno(
        double xPano, List<double> ys, List<double> xs, bool aLaDerecha)
    {
        // ¿Queda algo que rellenar por este lado? Con la pieza pegada al paño, no.
        var hay = false;

        foreach (var x in xs)
        {
            if (aLaDerecha ? x > xPano + 1e-4 : x < xPano - 1e-4)
            {
                hay = true;
                break;
            }
        }

        if (!hay)
        {
            return;
        }

        var pts = new List<double> { xPano, ys[0] };

        for (var i = 0; i < xs.Count; i++)
        {
            // Un peldaño solo cuando el paño cambia: los vértices repetidos hacen que AutoCAD
            // rechace el contorno del hatch.
            if (pts.Count == 2 || Math.Abs(xs[i] - pts[^2]) > 1e-6)
            {
                pts.Add(xs[i]);
                pts.Add(ys[i]);
            }

            pts.Add(xs[i]);
            pts.Add(ys[i + 1]);
        }

        pts.Add(xPano);
        pts.Add(ys[^1]);

        HatchPoligono(pts.ToArray(), CapaTerrenoHatch,
            PatronTerreno, EscalaTerreno, TranspTerreno, 0);
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
    private const double AnchoRotuloCadena = 0.26;          // anchoCad, pedido a 26 cm
    private const double AnchoRotuloMuroCentral = 0.32;     // ANCHO_MTEXT_MURO_CONC
    private const double AnchoRotuloMuroLindero = 0.25;     // anchoMTextMuro del lindero

    /// <summary>Texto del rótulo del muro de enrase, palabra por palabra.</summary>
    private const string TextoRotuloEnrase = "MURO DE ENRASE DE BLOCK DE CEMENTO";

    /// <summary>Cuánto se separa del bloque el rótulo de la contratrabe, en horizontal.</summary>
    private const double RotuloContratrabeDx = 0.12;

    /// <summary>
    /// Y lo que se corre <b>a la izquierda</b> ese rótulo, para pegarlo más al bloque: 6 cm.
    /// </summary>
    /// <remarks>
    /// Se pidió así, mirando la sección central: con los 12 cm pelados el renglón quedaba
    /// despegado del bloque y la flecha salía larguísima. Es un corrimiento en X, no un cambio de
    /// separación, así que se aplica igual en la central y en la de lindero.
    /// </remarks>
    private const double RotuloContratrabeCorrimiento = 0.06;

    /// <summary>Y cuánto sube por encima de su esquina superior.</summary>
    private const double RotuloContratrabeDy = 0.10;

    /// <summary>
    /// Lo que se despega de la cadena de desplante su rótulo: <b>5 cm</b>.
    /// </summary>
    /// <remarks>
    /// Se pidió que estuviera <b>siempre</b> despegado, y por eso se mide desde el paño de la
    /// cadena y no desde el eje de la sección: así la separación es la misma con una cadena de 15
    /// cm y con una de 40, y el renglón nunca acaba encima del bloque.
    /// </remarks>
    private const double RotuloCadenaSeparacion = 0.05;

    /// <summary>
    /// Lo que sube el rótulo de una parrilla <b>sobre el lomo de la zapata</b>: 10 cm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se mide desde el <b>paño de arriba del concreto</b> y no desde la varilla que señala, y eso
    /// es lo que arregla dos cosas de golpe. La primera: el renglón <b>nunca cae dentro de la
    /// sección</b>. Midiéndolo desde la varilla, una zapata de 50 cm de espesor dejaba el rótulo de
    /// la parrilla inferior enterrado en el concreto, encima del rayado y debajo del acero. La
    /// segunda: con <b>doble parrilla</b> el rótulo se sube <b>solo</b>, porque el acero de arriba
    /// también está por debajo de ese paño.
    /// </para>
    /// </remarks>
    private const double RotuloParrillaDy = 0.10;

    /// <summary>Aire entre el renglón de una parrilla y el de la de arriba.</summary>
    private const double RotuloParrillaAire = 0.03;

    /// <summary>
    /// Ancho de renglón del rótulo de parrilla: <b>22 cm</b>, o sea <b>dos renglones</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En una sola línea el rótulo salía de 30 cm —<c>VAR #4C @ 20 cm INFERIOR</c>— y en una zapata
    /// de 80 con la contratrabe de 30 no cabe: el renglón de la parrilla de abajo se metía dentro
    /// del bloque de la contratrabe. Con el ancho puesto es un <b>MText de dos renglones</b>, la
    /// varilla arriba y su palabra debajo, y ocupa la mitad de largo.
    /// </para>
    /// <para>
    /// El corte no se deja al reparto automático: va escrito con un salto de línea, para que la
    /// palabra del lecho —o el <c>AMBOS SENTIDOS</c>— caiga <b>siempre</b> en el segundo renglón y
    /// no según lo que mida el número de varilla.
    /// </para>
    /// </remarks>
    private const double AnchoRotuloParrilla = 0.22;

    /// <summary>
    /// Holgura que se le deja al leader de la parrilla superior para pasar por fuera del rótulo de
    /// la inferior.
    /// </summary>
    private const double RotuloParrillaHolgura = 0.02;

    /// <summary>Dónde quedaron los renglones de una parrilla, para que la de arriba los esquive.</summary>
    /// <remarks>
    /// La parrilla superior no puede colocarse a ciegas: necesita saber qué caja ocupa cada renglón
    /// de la inferior para subirse por encima y para bajar su leader por <b>fuera</b> del texto, no
    /// por encima. Y esa caja solo se conoce después de escribir el MText, midiéndolo.
    /// </remarks>
    private readonly record struct HuellaRotulos(
        (double X1, double Y1, double X2, double Y2)? Izq,
        (double X1, double Y1, double X2, double Y2)? Der)
    {
        /// <summary>El borde de arriba más alto de los dos renglones.</summary>
        public double Techo =>
            Math.Max(Izq?.Y2 ?? double.NegativeInfinity, Der?.Y2 ?? double.NegativeInfinity);
    }

    /// <summary>
    /// Los <b>dos</b> rótulos de una parrilla de zapata corrida: flexión y temperatura.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En una zapata corrida las dos varillas de una parrilla no hacen el mismo trabajo, y por eso
    /// no se rotulan juntas: la que se ve <b>de canto</b> —la que cruza la zapata de lado a lado—
    /// es la de <b>flexión</b>, y las que se ven <b>de punta</b> son las de <b>temperatura</b>, que
    /// corren a lo largo del muro. Un solo rótulo de «AMBOS SENTIDOS» deja al armador sin saber
    /// cuál es cuál.
    /// </para>
    /// <para>
    /// <b>Dónde va cada uno.</b> El de flexión, a la mitad del tramo <b>izquierdo</b>, con la
    /// flecha en la varilla horizontal justo ahí. El de temperatura, a la mitad del <b>lado
    /// derecho</b>, con la flecha en la varilla de punta más cercana. Así los dos leaders salen a
    /// lados contrarios del muro y no se cruzan.
    /// </para>
    /// <para>
    /// Los dos textos llevan la <b>C</b> de corrugada detrás del número —<c>VAR #4C @ 20 cm</c>—,
    /// que es como se especifica en el plano.
    /// </para>
    /// </remarks>
    private HuellaRotulos RotulosDeParrillaCorrida(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, double rec,
        string? varBarra, string? sepBarra, string? varCirc, string? sepCirc, bool superior,
        HuellaRotulos abajo, double xTopeIzq, double xTopeDer)
    {
        var diam = Diam(varBarra);

        if (diam <= 0)
        {
            return default;
        }

        var diamC = Diam(varCirc);

        if (diamC <= 0)
        {
            diamC = diam;
        }

        var p = TrazoZapataCorrida.ParrillaEnAlzado(
            a, z.EspesorM, rec, diam, diamC, TrazoZapata.SeparacionM(sepCirc), superior);

        var ancho = a.XDer - a.XBase;

        // ---------- LA ALTURA: 10 CM SOBRE EL LOMO, Y POR ENCIMA DE LO YA ESCRITO ----------
        //
        // Se mide desde el paño de arriba del concreto, así que el renglón está SIEMPRE fuera de
        // la sección, con el espesor que sea. Y si la parrilla de abajo ya escribió, este se sube
        // por encima de su renglón más alto: es lo que evita que con doble parrilla los cuatro
        // rótulos se apilen en el mismo sitio.
        var yTexto = a.YZapTop + RotuloParrillaDy;

        var techo = abajo.Techo;

        if (!double.IsNegativeInfinity(techo))
        {
            yTexto = Math.Max(yTexto, techo + RotuloParrillaAire + AltoMtexto);
        }

        // ---------- UN SOLO RÓTULO CUANDO EL ARMADO ES EL MISMO ----------
        //
        // Misma varilla y misma separación en los dos sentidos: sobra rotularlo dos veces, y se
        // escribe como se especifica en el plano, «AMBOS SENTIDOS». Va en el tramo izquierdo, con
        // la flecha en la varilla de flexión, que es la que se puede señalar sin ambigüedad.
        if (MismoArmado(varBarra, sepBarra, varCirc, sepCirc))
        {
            var xUno = CarrilDeFlexion(a, p, diam, xTopeIzq, abajo.Izq);

            var cajaUno = RotuloDeParrilla(
                xUno, p.YBarra, yTexto,
                TextoParrillaCorrida(varBarra, sepBarra, SufijoAmbosSentidos));

            return new HuellaRotulos(cajaUno, null);
        }

        // ---------- FLEXIÓN: la varilla de canto, a la mitad del tramo izquierdo ----------
        //
        // LA PALABRA DEL LECHO SE VOLTEA EN LA PARRILLA DE ARRIBA. En la de abajo la varilla de
        // flexión es la que se apoya en el recubrimiento y la de temperatura descansa encima; en la
        // de arriba es al revés, porque la de flexión se amarra por el lomo. Se pidió expresamente,
        // y así cada renglón dice el lecho en el que de verdad va su varilla.
        var cajaIzq = RotuloDeParrilla(
            CarrilDeFlexion(a, p, diam, xTopeIzq, abajo.Izq), p.YBarra, yTexto,
            TextoParrillaCorrida(
                varBarra, sepBarra, superior ? SufijoLechoSuperior : SufijoLechoInferior));

        // ---------- TEMPERATURA: la de punta, a la mitad del lado derecho ----------
        var xTemp = CirculoLibre(
            p.Circulos,
            MitadDelLado(xTopeDer, a.XDer, haciaDerecha: true),
            abajo.Der,
            LimiteDelRotulo(xTopeDer, haciaDerecha: true));

        if (double.IsNaN(xTemp))
        {
            return new HuellaRotulos(cajaIzq, null);
        }

        var cajaDer = RotuloDeParrilla(
            xTemp, p.YCirculos, yTexto,
            TextoParrillaCorrida(
                varCirc, sepCirc, superior ? SufijoLechoInferior : SufijoLechoSuperior));

        return new HuellaRotulos(cajaIzq, cajaDer);
    }

    /// <summary>Palabra del acero que va en el lecho de <b>abajo</b>: el de flexión.</summary>
    private const string SufijoLechoInferior = "INFERIOR";

    /// <summary>Y del que se apoya <b>encima</b> de él: el de temperatura.</summary>
    private const string SufijoLechoSuperior = "SUPERIOR";

    /// <summary>Lo que se escribe cuando los dos sentidos llevan el mismo armado.</summary>
    private const string SufijoAmbosSentidos = "AMBOS SENTIDOS";

    /// <summary>El texto de una varilla con la <b>C</b> de corrugada detrás del número.</summary>
    /// <remarks>
    /// <c>VAR #4C @ 20 cm INFERIOR</c> para la varilla de flexión —la del lecho de abajo— y
    /// <c>SUPERIOR</c> para la de temperatura, que se apoya encima de ella. Cuando las dos llevan
    /// la misma varilla y la misma separación se rotulan de una vez, con <c>AMBOS SENTIDOS</c>.
    /// </remarks>
    private string TextoParrillaCorrida(string? varilla, string? sep, string sufijo)
    {
        var etiqueta = Etiqueta(varilla);

        if (etiqueta.Length == 0)
        {
            return string.Empty;
        }

        // El salto de línea va ESCRITO: la palabra del lecho -o el «AMBOS SENTIDOS»- cae siempre
        // en el segundo renglón, y no donde la deje el reparto automático del ancho.
        return $"VAR {etiqueta}C @ {SepTexto(sep)} cm\n{sufijo}";
    }

    /// <summary>¿Los dos sentidos de una parrilla llevan el mismo armado?</summary>
    /// <remarks>
    /// Se comparan la varilla <b>y</b> la separación, no solo la separación: dos varillas
    /// distintas a los mismos 20 cm no son «ambos sentidos», y meterlas en un solo renglón dejaría
    /// una de las dos sin especificar.
    /// </remarks>
    private bool MismoArmado(string? varA, string? sepA, string? varB, string? sepB)
    {
        var etA = Etiqueta(varA);
        var etB = Etiqueta(varB);

        if (etA.Length == 0 || etB.Length == 0)
        {
            return false;
        }

        return etA.Equals(etB, StringComparison.OrdinalIgnoreCase)
            && SepTexto(sepA).Equals(SepTexto(sepB), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El carril por el que baja el leader de la varilla de <b>flexión</b>.
    /// </summary>
    /// <remarks>
    /// A la mitad del lado izquierdo <b>libre</b>, corrido a la izquierda si por ahí ya pasa el
    /// rótulo de la parrilla de abajo, y con la punta siempre entre las dos caras de la barra: la
    /// varilla de flexión es una línea continua, así que la flecha se puede pegar a cualquier punto
    /// de ella y el leader se queda vertical, sin cruzar nada.
    /// </remarks>
    private static double CarrilDeFlexion(
        TrazoZapataCorrida.Acomodo a, TrazoZapata.Parrilla p, double diam, double xTopeIzq,
        (double X1, double Y1, double X2, double Y2)? estorbo)
    {
        var x = CarrilLibre(
            MitadDelLado(xTopeIzq, a.XBase, haciaDerecha: false), estorbo, haciaDerecha: false);

        return Math.Clamp(x, p.XCaraIzq + (diam / 2), p.XCaraDer - (diam / 2));
    }

    /// <summary>
    /// El centro del renglón: <b>a la mitad del lado libre</b>, y sin tocar el bloque del centro.
    /// </summary>
    /// <remarks>
    /// «Cada lado» es el tramo que va del paño de la zapata al paño de lo que hay en medio —la
    /// contratrabe si sobresale, y si no el muro—, no la cuarta parte del ancho. Con la contratrabe
    /// de 30 en una zapata de 80, la cuarta parte del ancho caía a 20 cm del paño y el renglón se
    /// metía en el bloque; la mitad del lado libre cae a 12.5 y queda centrado en el volado, que es
    /// lo que se pidió. Y si el volado es más estrecho que el propio renglón, se corre hacia fuera
    /// hasta que quepa: antes se sale de la zapata que taparse con la contratrabe.
    /// </remarks>
    private static double MitadDelLado(double xTope, double xExtremo, bool haciaDerecha)
    {
        var medio = (xTope + xExtremo) / 2;
        var limite = LimiteDelRotulo(xTope, haciaDerecha);

        return haciaDerecha ? Math.Max(medio, limite) : Math.Min(medio, limite);
    }

    /// <summary>Lo más cerca del bloque que puede ir el centro del renglón sin tocarlo.</summary>
    private static double LimiteDelRotulo(double xTope, bool haciaDerecha) =>
        haciaDerecha
            ? xTope + RotuloParrillaHolgura + (AnchoRotuloParrilla / 2)
            : xTope - RotuloParrillaHolgura - (AnchoRotuloParrilla / 2);

    /// <summary>Corre una X hasta salirse de la caja que le estorba, por el lado que se pida.</summary>
    private static double CarrilLibre(
        double x, (double X1, double Y1, double X2, double Y2)? estorbo, bool haciaDerecha)
    {
        if (estorbo is null)
        {
            return x;
        }

        var c = estorbo.Value;

        if (x < c.X1 - 1e-9 || x > c.X2 + 1e-9)
        {
            return x;
        }

        return haciaDerecha
            ? c.X2 + RotuloParrillaHolgura
            : c.X1 - RotuloParrillaHolgura;
    }

    /// <summary>La varilla de punta más cercana a una X que además no quede tapada.</summary>
    /// <remarks>
    /// Se prefiere una varilla que esté <b>a la derecha</b> del rótulo de la parrilla de abajo: así
    /// el leader de la de arriba baja por fuera de ese renglón en lugar de atravesarlo. Si no hay
    /// ninguna se vuelve a la más cercana, que es mejor que no rotular.
    /// </remarks>
    private static double CirculoLibre(
        double[] circulos, double x, (double X1, double Y1, double X2, double Y2)? estorbo,
        double xMinimo)
    {
        // Dos condiciones, y la segunda vale también sin parrilla debajo: la varilla tiene que
        // estar lo bastante a la derecha para que el renglón no se meta en la contratrabe.
        var limite = estorbo is null
            ? xMinimo
            : Math.Max(xMinimo, estorbo.Value.X2 + RotuloParrillaHolgura);

        var libres = 0;

        foreach (var c in circulos)
        {
            if (c > limite)
            {
                libres++;
            }
        }

        if (libres == 0)
        {
            return CirculoMasCercano(circulos, x);
        }

        var solo = new double[libres];
        var i = 0;

        foreach (var c in circulos)
        {
            if (c > limite)
            {
                solo[i++] = c;
            }
        }

        return CirculoMasCercano(solo, x);
    }

    /// <summary>La varilla de punta más cercana a una X, o <c>NaN</c> si no hay ninguna.</summary>
    /// <remarks>
    /// La flecha se pega a una varilla <b>de verdad</b> y no a un punto cualquiera del reparto: es
    /// lo que hace <c>CirculoMasCercano</c> en las macros, y es lo que evita que el rótulo señale
    /// un hueco entre dos varillas.
    /// </remarks>
    private static double CirculoMasCercano(double[] circulos, double x)
    {
        if (circulos.Length == 0)
        {
            return double.NaN;
        }

        var mejor = circulos[0];

        foreach (var c in circulos)
        {
            if (Math.Abs(c - x) < Math.Abs(mejor - x))
            {
                mejor = c;
            }
        }

        return mejor;
    }

    /// <summary>
    /// Un rótulo de parrilla <b>centrado sobre su carril</b>, con el leader bajando recto por él.
    /// </summary>
    /// <remarks>
    /// El texto se centra en el mismo X al que apunta la flecha, así que la línea sale del medio de
    /// su borde inferior y baja <b>vertical</b> hasta la varilla. Dos rótulos apilados con carriles
    /// distintos dan dos leaders paralelos, y dos leaders paralelos no se cruzan nunca: es la forma
    /// más simple de cumplir lo que se pidió para la doble parrilla.
    /// </remarks>
    /// <returns>La caja del renglón escrito, para que el de arriba lo pueda esquivar.</returns>
    private (double X1, double Y1, double X2, double Y2)? RotuloDeParrilla(
        double xCarril, double yPunta, double yTexto, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        // Con ancho de renglón, que es lo que lo parte en dos líneas y lo deja corto.
        var mt = MtextoAncho(xCarril, yTexto, texto, AnchoRotuloParrilla, AnclajeCentro);

        var caja = Caja(mt);

        // El leader arranca del borde de ABAJO del renglón, en su punto medio.
        var ySalida = caja?.Y1 ?? yTexto;

        Leader(xCarril, yPunta, xCarril, ySalida);

        return caja;
    }

    /// <summary>Lo que se despega del muro de enrase su rótulo, siempre por la derecha.</summary>
    /// <remarks>
    /// Se pidió <b>6 cm y siempre a la derecha</b> de la hilada. Antes eran 10 cm en la central y,
    /// en el lindero, 30 cm por la <b>izquierda</b> —que es lo que hace su macro, porque a la
    /// derecha del lindero está la colindancia—. Ahora los dos lo sacan por el mismo lado y a la
    /// misma distancia; si en el lindero ese renglón acaba estorbando a la sección de al lado, es
    /// una línea volver a colgarlo por la izquierda.
    /// </remarks>
    private const double RotuloEnraseSeparacion = 0.06;

    /// <summary>Y lo que se despega del muro de concreto el suyo, en la central: 6 cm.</summary>
    private const double RotuloMuroSeparacion = 0.06;

    /// <summary>
    /// El rótulo del <b>muro de enrase</b>, con su leader al centro de la hilada.
    /// </summary>
    /// <remarks>
    /// Siempre por la <b>derecha</b> de la hilada y despegado
    /// <see cref="RotuloEnraseSeparacion"/>, en la central y en el lindero.
    /// </remarks>
    private void RotuloDelEnrase(
        TrazoZapataCorrida.Acomodo a, bool lindero, TrazoZapataCorrida.Enrase e)
    {
        var yTop = e.YBases[^1] + e.AltoPieza;
        var yBot = e.YBases[0];

        var xCentro = e.XIzq + (e.Ancho / 2);
        var yCentro = (yBot + yTop) / 2;

        var yTexto = yTop - 0.08;

        // SIEMPRE A LA DERECHA DE LA HILADA, Y A 6 CM DE SU PAÑO. Se mide desde el paño derecho del
        // enrase y no desde el eje de la sección, así que la separación es la misma con una hilada
        // de 15 cm y con una de 40, y el renglón nunca acaba tocando el block.
        var xTexto = e.XIzq + e.Ancho + RotuloEnraseSeparacion;

        MtextoAncho(xTexto, yTexto, TextoRotuloEnrase, AnchoRotuloEnrase, AnclajeIzquierda);

        Leader(xCentro, yCentro, xTexto, yTexto);
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

        // LA FLECHA VA A LA ESQUINA SUPERIOR DERECHA DEL BLOQUE.
        // Antes apuntaba al centro de la contratrabe, y ahí la flecha se pierde entre su acero y
        // el hatch del concreto: en la esquina se ve de una vez a qué elemento señala, y además
        // deja de cruzar la sección para llegar al centro.
        var xPunta = xCtDer;
        var yPunta = yCtTop;

        // El renglón se cuelga por ARRIBA de esa esquina, del lado donde hay sitio: a la derecha
        // en la central —ahí solo hay terreno— y a la izquierda en el lindero, donde el paño
        // derecho ES el lindero y no queda hueco.
        var yTexto = yCtTop + RotuloContratrabeDy;

        var xIns = (lindero
            ? xCtIzq - RotuloContratrabeDx
            : xCtDer + RotuloContratrabeDx) - RotuloContratrabeCorrimiento;

        var anclaje = lindero ? AnclajeDerecha : AnclajeIzquierda;

        MtextoAncho(xIns, yTexto, texto, AnchoRotuloContratrabe, anclaje);

        Leader(xPunta, yPunta, xIns, yTexto);
    }

    /// <summary>El rótulo de la <b>cadena de desplante</b>, con su leader.</summary>
    private void RotuloDeLaCadena(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a, bool lindero,
        double xCadIzq, double xCadDer, double yCadBot)
    {
        var texto = $"CADENA DE DESPLANTE \"{(z.IdCadena ?? string.Empty).Trim()}\"";

        var xCentroCad = (xCadIzq + xCadDer) / 2;
        var yCentroCad = (yCadBot + a.YTerreno) / 2;

        // EL TEXTO SIEMPRE DESPEGADO DE LA CADENA: 5 cm desde su paño izquierdo, y no a una
        // distancia fija desde el eje de la sección. Con la distancia fija, una cadena ancha o una
        // zapata angosta dejaban el renglón tocando el bloque —o encima—, y el leader no se veía.
        var xIns = xCadIzq - RotuloCadenaSeparacion;

        MtextoAncho(xIns, yCentroCad, texto, AnchoRotuloCadena, AnclajeDerecha);

        Leader(xCadIzq, yCentroCad, xIns, yCentroCad);
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

        // EN LA CENTRAL, SIEMPRE 6 CM DEL PAÑO DEL MURO. Se pidió así: antes eran 7 cm heredados de
        // la macro —«+0.12 − 0.05»—, que con el muro estrecho dejaban el renglón despegado y con el
        // muro ancho casi encima. Medido desde el paño, la separación es la misma con cualquier
        // espesor. El lindero se queda como su macro, colgado por la izquierda, porque a su derecha
        // está la colindancia.
        var xTexto = lindero
            ? m.XIzq - 0.27
            : m.XDer + RotuloMuroSeparacion;

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
