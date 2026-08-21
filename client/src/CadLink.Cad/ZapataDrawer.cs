namespace CadLink.Cad;

/// <summary>
/// Dibuja en AutoCAD las <b>zapatas aisladas</b>: su corte y su planta, como las macros
/// <c>ZAPATA AISLADA CENTRAL V2</c> y <c>ZAPATA AISLADA LINDERO V1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Es el otro lado de la pestaña de zapatas: lo que la vista previa enseña en el lienzo, puesto
/// en el dibujo. <b>Toda la geometría y todas las distancias salen de <see cref="TrazoZapata"/></b>,
/// que es la misma clase que usa la vista previa. Eso no es un adorno de arquitectura: es la
/// única forma de que lo que se revisó antes de pulsar el botón sea de verdad lo que aparece en
/// el plano. Si el dibujante recalculara por su cuenta dónde cuelga la planta o cómo se reparten
/// los estribos, la previa sería una promesa y no una revisión.
/// </para>
/// <para>
/// <b>Unidades.</b> Se dibuja en <b>metros</b>, 1:1 en el modelo, igual que las macros: la zapata
/// y sus alturas ya vienen en metros y lo que viene en centímetros —el dado, la columna, los
/// recubrimientos— se pasa a metros con <see cref="TrazoZapata.EscalaElevacion"/>. Lo único que
/// depende de la escala del plano es el <i>tamaño del aparato</i>: textos y cotas, que se
/// multiplican por <see cref="_f"/>.
/// </para>
/// <para>
/// <b>Qué se dibuja de cada zapata.</b> Del corte: la plantilla de concreto simple, el cuerpo de
/// la zapata, el dado hasta el desplante, la columna cuando es de concreto —cortada con su línea
/// de rotura, porque sigue hacia arriba fuera del dibujo—, el nivel del terreno con su relleno,
/// las parrillas con sus ganchos y sus transversales vistas de punta, los estribos del dado en
/// cápsula y las cotas en cadena. De la planta: el paño, las mallas recortadas en el hueco del
/// dado, el dado insertado <b>como bloque</b> buscándolo por su ID, y sus cotas.
/// </para>
/// <para>
/// <b>El dado se inserta, no se copia.</b> El dado ya está dibujado en la hoja de secciones de
/// concreto con su armado y su nombre de bloque; en la planta de la zapata se inserta ese bloque.
/// Si no está en el dibujo se pone un rectángulo con su tamaño y <b>se avisa</b>: es mejor un
/// hueco honesto y un aviso que un dado inventado que no cuadre con el de la otra hoja.
/// </para>
/// <para>
/// <b>Enlace tardío.</b> Como todos los dibujantes del proyecto, se habla con AutoCAD por COM con
/// <c>dynamic</c>: ni una referencia a las DLL de Autodesk, así que el mismo binario sirve de
/// AutoCAD 2021 a 2026. Y como todos, <b>tolera fallos</b>: lo que no se pudo dibujar se apunta
/// en <see cref="Fallos"/> y el resto del dibujo sigue. Un hatch que no se pudo evaluar no vale
/// perder la zapata entera.
/// </para>
/// </remarks>
public sealed class ZapataDrawer
{
    private const int PorCapa = 256;

    // ------------------------------------------------------------------
    // Capas: las mismas de las macros
    // ------------------------------------------------------------------
    private const string CapaConcreto = "CONCRETO";
    private const string CapaEstribos = "ESTRIBOS";
    private const string CapaRotulos = "ROTULOS";
    private const string CapaCotas = "COTAS";
    private const string CapaTerrenoLinea = "TERRENO_LINEA";
    private const string CapaTerrenoHatch = "TERRENO_HATCH";
    private const string CapaPlantilla = "PLANTILLA";
    private const string CapaBloqueDado = "BLOQUE_DADO";

    /// <summary>
    /// Capa de las parrillas y las mallas.
    /// </summary>
    /// <remarks>
    /// No está en la lista de la macro —allí el acero de la zapata comparte capa con los
    /// estribos—, y se separó a propósito: la malla en planta es lo que más ensucia la vista
    /// cuando se está acotando, y con su propia capa se apaga sola sin perder los estribos del
    /// dado. Es un cambio de presentación, no de geometría.
    /// </remarks>
    private const string CapaParrilla = "PARRILLA";

    // Colores ACI.
    private const int ColorConcreto = 4;      // cian: el contorno de concreto
    private const int ColorAcero = 1;         // rojo: parrillas y mallas
    private const int ColorAceroSup = 6;      // magenta: la parrilla de arriba, para distinguirlas
    private const int ColorEstribo = 2;       // amarillo
    private const int ColorPlantilla = 8;     // gris oscuro
    private const int ColorTerreno = 34;      // tierra
    private const int ColorPatronConcreto = 251;
    private const int ColorRotulo = 7;

    private const string PatronConcreto = "AR-CONC";
    private const string PatronRespaldo = "ANSI31";
    private const string PatronTerreno = "EARTH";

    private const string EstiloTexto = "SECCIONES";
    private const string EstiloCota = "COTA_ESTRUCTURAL";

    /// <summary>Alto del renglón del título del dibujo, en metros de modelo a escala 1:10.</summary>
    private const double AlturaTitulo = 0.045;

    /// <summary>Alto del renglón de «Rec. / f'c / Escala».</summary>
    private const double AlturaTexto = 0.03;

    /// <summary>Cuánto se prolonga la línea del terreno a cada lado de la zapata, en m.</summary>
    private const double TerrenoVuelo = 0.5;

    /// <summary>La columna del dado se dibuja cortada a esta altura, en m.</summary>
    /// <remarks>
    /// Es el <c>0.8 * 8 / 9</c> de la macro: la columna sigue hacia arriba, así que se dibuja un
    /// tramo y se corta. Los ocho novenos son lo que deja sitio para la línea de rotura sin que
    /// el tramo dibujado parezca la columna completa.
    /// </remarks>
    private const double AlturaColumna = 0.8 * 8.0 / 9.0;

    private readonly dynamic _doc;
    private readonly dynamic _ms;

    /// <summary>Escala del plano —10 es 1:10— solo para el tamaño de textos y cotas.</summary>
    private readonly double _f;

    /// <summary>El catálogo de varillas, que vive en la ventana y no aquí.</summary>
    /// <remarks>
    /// Se recibe en el constructor en lugar de traer una segunda tabla de diámetros. Una copia
    /// aquí sería una tabla que hay que acordarse de actualizar dos veces: el día que se agregue
    /// una varilla del #14, la celda la ofrecería y el plano la dibujaría con diámetro cero.
    /// </remarks>
    private readonly Func<string?, double> _diametroCm;

    private readonly List<string> _log = new();
    private readonly List<string> _notas = new();

    /// <summary>Bloques de dado que se buscaron y no estaban, para no avisar dos veces.</summary>
    private readonly HashSet<string> _dadosQueFaltan = new(StringComparer.OrdinalIgnoreCase);

    public ZapataDrawer(dynamic doc, Func<string?, double> diametroCm, double escalaPlano = 10)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);
        _diametroCm = diametroCm;
        _f = escalaPlano <= 0 ? 1 : escalaPlano / 10.0;

        // Se toca una vez para que la interop quede cargada antes del primer dibujo, igual que
        // hacen los otros dibujantes.
        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Escala del patrón de concreto. A la vista del usuario porque depende del plano.</summary>
    public double EscalaHatchConcreto { get; set; } = 0.005;

    /// <summary>Escala del patrón del terreno.</summary>
    public double EscalaHatchTerreno { get; set; } = 0.01;

    /// <summary>Si se rellenan de negro las varillas vistas de punta.</summary>
    public bool RellenarVarillas { get; set; } = true;

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

    /// <summary>Qué se dibujó de verdad.</summary>
    public sealed class Resumen
    {
        public int Zapatas { get; set; }
        public int Parrillas { get; set; }
        public int Estribos { get; set; }
        public int Varillas { get; set; }
        public int Cotas { get; set; }
        public int DadosInsertados { get; set; }
        public int DadosDeRespaldo { get; set; }

        public override string ToString() =>
            $"{Zapatas} zapata(s), {Parrillas} parrilla(s), {Estribos} estribo(s), " +
            $"{Varillas} varilla(s) de malla, {Cotas} cota(s)";
    }

    // ======================================================================
    // Entrada
    // ======================================================================

    /// <summary>
    /// Dibuja todas las zapatas, cada una en su sitio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El acomodo <b>no se decide aquí</b>: se pide a <see cref="TrazoZapata.XBase"/>, que es
    /// quien sabe que las centrales crecen a la derecha con un metro de separación y que el
    /// lindero arranca en −3 y crece a la izquierda con 80 cm. Son dos reglas distintas, una por
    /// macro, y la vista previa usa esta misma para enseñar la zapata en su posición.
    /// </para>
    /// <para>
    /// Lo común —capas, estilo de texto y variables de cota— se prepara <b>una sola vez</b> antes
    /// del bucle: son operaciones que tocan el documento entero y repetirlas por zapata solo
    /// añadiría viajes por COM.
    /// </para>
    /// </remarks>
    public Resumen DibujarTodas(IReadOnlyList<ZapataCad> zapatas)
    {
        var r = new Resumen();

        AsegurarCapas();
        AsegurarEstiloTexto();
        ConfigurarCotas();

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
                // Una zapata que se cae no se lleva a las demás: se apunta y se sigue con la
                // siguiente. Es lo que hace la hoja de secciones con un elemento imposible.
                Fallo($"Zapata '{z.Id}'", ex);
            }
        }

        return r;
    }

    /// <summary>Dibuja una zapata en la X que se le diga. Devuelve dónde acaba.</summary>
    public double Dibujar(ZapataCad z, double xBase)
    {
        AsegurarCapas();
        AsegurarEstiloTexto();
        ConfigurarCotas();

        var r = new Resumen();
        Dibujar(z, xBase, r);

        return xBase + z.AnchoM;
    }

    private void Dibujar(ZapataCad z, double xBase, Resumen r)
    {
        var a = TrazoZapata.Colocar(z, xBase);

        Elevacion(z, a, r);
        Planta(z, a, r);
    }

    // ======================================================================
    // El corte
    // ======================================================================

    /// <summary>El corte de la zapata, de abajo arriba: como se construye.</summary>
    /// <remarks>
    /// El orden importa dos veces. Primero porque en AutoCAD el orden de creación es el orden de
    /// dibujo, así que los rellenos van antes que los contornos y el acero al final, encima de
    /// todo. Y segundo porque es el orden de la obra —plantilla, zapata, dado, columna—, que es
    /// como se lee un corte.
    /// </remarks>
    private void Elevacion(ZapataCad z, TrazoZapata.Acomodo a, Resumen r)
    {
        // ---------- El terreno, lo primero: queda debajo de todo ----------
        RellenoDeTerreno(z, a);

        Linea(a.XBase - TerrenoVuelo, a.YTerreno, a.XDer + TerrenoVuelo, a.YTerreno,
            CapaTerrenoLinea);

        // ---------- La plantilla de concreto simple ----------
        var plantilla = Rectangulo(a.XBase, a.YPlantillaBot, a.XDer, a.YZapBot, CapaPlantilla);

        HatchDe(plantilla, PatronConcreto, EscalaHatchConcreto, CapaPlantilla, ColorPlantilla);

        // ---------- El cuerpo de la zapata y el dado ----------
        var cuerpo = Rectangulo(a.XBase, a.YZapBot, a.XDer, a.YZapTop, CapaConcreto);

        HatchDe(cuerpo, PatronConcreto, EscalaHatchConcreto, CapaConcreto, ColorPatronConcreto);

        if (a.YDadoTop > a.YZapTop + 1e-6 && a.XDadoDer > a.XDadoIzq + 1e-6)
        {
            var dado = Rectangulo(a.XDadoIzq, a.YZapTop, a.XDadoDer, a.YDadoTop, CapaConcreto);

            HatchDe(dado, PatronConcreto, EscalaHatchConcreto, CapaConcreto, ColorPatronConcreto);
        }

        // ---------- La columna, cuando es de concreto ----------
        // Con columna de acero no se dibuja: ahí va una placa base y sus anclas, que son otro
        // detalle y no esta vista. Se avisa para que nadie crea que se olvidó.
        if (z.ColumnaDeConcreto && a.XColDer > a.XColIzq + 1e-6)
        {
            ColumnaCortada(a);
        }
        else if (!z.ColumnaDeConcreto)
        {
            Nota($"Zapata '{z.Id}': la columna es de acero, así que en el corte se dibujó solo el "
                 + "dado. La placa base y las anclas son otro detalle y no salen de esta hoja.");
        }

        // ---------- El acero ----------
        if (Parrilla(z, a, superior: false, r))
        {
            r.Parrillas++;
        }

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup) && Parrilla(z, a, superior: true, r))
        {
            r.Parrillas++;
        }

        EstribosDelDado(z, a, r);

        // ---------- Cotas y rótulos ----------
        CotasDelCorte(a, r);
        RotuloDelCorte(z, a);
    }

    /// <summary>El relleno de tierra a los lados del dado, entre la zapata y el desplante.</summary>
    /// <remarks>
    /// Es el relleno que se compacta encima de la zapata, y en el corte es lo que explica por qué
    /// la zapata está enterrada. Van dos paños, uno a cada lado del dado; en el lindero el
    /// derecho no existe porque el dado llega al paño. Con transparencia, para que no tape el
    /// acero: es un fondo, no una pieza.
    /// </remarks>
    private void RellenoDeTerreno(ZapataCad z, TrazoZapata.Acomodo a)
    {
        if (a.YTerreno <= a.YZapTop + 1e-6)
        {
            // Desplante igual o menor que el espesor: no hay relleno que dibujar. No es un
            // error, es una zapata a flor de tierra.
            return;
        }

        // Sin la eñe en el nombre: el codigo se compila en Windows con otra pagina de
        // codigos y un identificador acentuado es un riesgo que no aporta nada.
        var panos = new (double X1, double X2)[]
        {
            (a.XBase, a.XDadoIzq),
            (a.XDadoDer, a.XDer)
        };

        foreach (var (x1, x2) in panos)
        {
            if (x2 <= x1 + 1e-6)
            {
                continue;
            }

            var borde = Rectangulo(x1, a.YZapTop, x2, a.YTerreno, CapaTerrenoHatch);

            var h = HatchDe(borde, PatronTerreno, EscalaHatchTerreno, CapaTerrenoHatch, ColorTerreno);

            Transparencia(h, 45);

            // El rectángulo del relleno era solo la frontera del hatch: sus lados coinciden con
            // el paño de la zapata y con el terreno, así que dejarlo dibujaría esas líneas dos
            // veces —y en la capa del relleno, donde nadie las buscaría—.
            Borrar(borde);
        }
    }

    /// <summary>La columna: sus dos paños y la línea de rotura que dice que sigue.</summary>
    private void ColumnaCortada(TrazoZapata.Acomodo a)
    {
        var yTope = a.YDadoTop + AlturaColumna;

        Linea(a.XColIzq, a.YDadoTop, a.XColIzq, yTope, CapaConcreto);
        Linea(a.XColDer, a.YDadoTop, a.XColDer, yTope, CapaConcreto);

        LineaDeRotura(a.XColIzq, a.XColDer, yTope);
    }

    /// <summary>
    /// La línea de rotura del remate de la columna: el zigzag de siempre.
    /// </summary>
    /// <remarks>
    /// Una línea recta ahí arriba se leería como el final de la columna, y la columna no acaba:
    /// sigue hasta el siguiente nivel. El zigzag es la convención que dice «esto está cortado».
    /// </remarks>
    private void LineaDeRotura(double x1, double x2, double y)
    {
        var ancho = x2 - x1;

        if (ancho <= 1e-6)
        {
            return;
        }

        var pico = Math.Min(0.04, ancho / 4);

        Polilinea(
            new[]
            {
                x1, y,
                x1 + (ancho * 0.35), y,
                x1 + (ancho * 0.45), y + pico,
                x1 + (ancho * 0.55), y - pico,
                x1 + (ancho * 0.65), y,
                x2, y
            },
            CapaConcreto, cerrada: false);
    }

    /// <summary>Una parrilla en el corte: la barra con sus ganchos y las transversales.</summary>
    /// <remarks>
    /// La barra que corre lleva gancho en los dos extremos y dobla <b>hacia dentro</b> de la
    /// zapata —abajo en la superior, arriba en la inferior—, que es como se arma. Las
    /// transversales se ven de punta, y se rellenan: un círculo hueco de 1 cm en un plano a 1:10
    /// se confunde con un punto de cota.
    /// </remarks>
    private bool Parrilla(ZapataCad z, TrazoZapata.Acomodo a, bool superior, Resumen r)
    {
        var varBarra = superior ? z.VarSup : z.VarInf;
        var varTrans = superior ? z.VarSupTrans : z.VarInfTrans;
        var sepTrans = superior ? z.SepSupTrans : z.SepInfTrans;

        var dBarraCm = _diametroCm(varBarra);

        if (dBarraCm <= 0)
        {
            Nota($"Zapata '{z.Id}': la parrilla {(superior ? "superior" : "inferior")} no tiene "
                 + "varilla capturada, así que no se dibujó.");
            return false;
        }

        var diam = dBarraCm / 100.0;
        var diamT = Math.Max(_diametroCm(varTrans), 0) / 100.0;

        var p = TrazoZapata.ParrillaEnAlzado(
            a.XBase, a.YZapBot, z.AnchoM, z.EspesorM, z.RecM, diam, diamT,
            TrazoZapata.SeparacionM(sepTrans), superior);

        var color = superior ? ColorAceroSup : ColorAcero;

        var yTip = superior
            ? p.YBarra - TrazoZapata.GanchoParrilla
            : p.YBarra + TrazoZapata.GanchoParrilla;

        // Una sola polilínea: gancho, barra, gancho. Así el acero se selecciona de una pieza,
        // que es como se edita en el plano.
        var barra = Polilinea(
            new[]
            {
                p.XCaraIzq, yTip,
                p.XCaraIzq, p.YBarra,
                p.XCaraDer, p.YBarra,
                p.XCaraDer, yTip
            },
            CapaParrilla, cerrada: false);

        Color(barra, color);
        Grosor(barra, diam);

        foreach (var x in p.Circulos)
        {
            var c = Circulo(x, p.YCirculos, Math.Max(p.DiamCirculos / 2, 0.003), CapaParrilla);

            Color(c, color);

            if (RellenarVarillas && c is not null)
            {
                var h = HatchDe(c, "SOLID", 1, CapaParrilla, color);

                if (h is null)
                {
                    // Sin relleno el círculo sigue estando: es acero visible igual.
                    Nota($"Zapata '{z.Id}': no se pudo rellenar una varilla transversal; queda "
                         + "dibujada hueca.");
                }
            }

            r.Varillas++;
        }

        return true;
    }

    /// <summary>
    /// Los estribos del dado, en cápsula y en las posiciones que reparte la macro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El dado se reparte con la celda de separación —<c>9-18-9</c> o una sola— y los centros se
    /// miden a lo largo del dado, o sea en Y desde el fondo de la zapata. Se saltan los primeros,
    /// que es donde está la parrilla: dos con doble parrilla y uno con una sola.
    /// </para>
    /// <para>
    /// Y se dibujan en <b>cápsula</b>, no como una raya: un estribo del #3 visto de canto tiene
    /// casi un centímetro de canto y sus esquinas son dobleces, no picos. Es lo que hace la macro
    /// y es lo que permite ver, en el plano impreso, dónde acaba el estribo y dónde el
    /// recubrimiento.
    /// </para>
    /// </remarks>
    private void EstribosDelDado(ZapataCad z, TrazoZapata.Acomodo a, Resumen r)
    {
        var tramos = TrazoZapata.TramosCm(z.SepEstriboDado);

        var centros = TrazoZapata.CentrosEstribos(
            z.ProfundidadM, tramos[0], tramos[1], tramos[2],
            TrazoZapata.EstriboRetiroBorde, TrazoZapata.EstriboRetiroBorde);

        if (centros.Length == 0)
        {
            Nota($"Zapata '{z.Id}': con la separación «{z.SepEstriboDado}» y {z.ProfundidadM:0.##} m "
                 + "de desplante no cabe ningún estribo en el dado. Revisa la celda.");
            return;
        }

        TrazoZapata.Sobresalir(centros);

        centros = TrazoZapata.QuitarPrimeros(centros, z.DobleParrilla ? 2 : 1);

        var recDado = z.RecDadoCm * TrazoZapata.EscalaElevacion;

        var x1 = a.XDadoIzq + recDado;
        var x2 = a.XDadoDer - recDado;

        if (x2 <= x1)
        {
            Fallo($"Estribos del dado de '{z.Id}'",
                new InvalidOperationException(
                    $"el recubrimiento de {z.RecDadoCm:0.#} cm se come el ancho del dado "
                    + $"({z.AnchoDadoCm:0.#} cm)"));
            return;
        }

        var dEstribo = Math.Max(_diametroCm(z.EstriboDado), 0.95) / 100.0;

        foreach (var c in centros)
        {
            var y = a.YZapBot + c;

            if (y < a.YZapBot || y > a.YDadoTop)
            {
                // Fuera del dado. Puede pasar con la protrusión del último: no es un fallo.
                continue;
            }

            Capsula(x1, x2, y, dEstribo);

            r.Estribos++;
        }
    }

    /// <summary>Una cápsula horizontal: dos rectas y los dos dobleces.</summary>
    private void Capsula(double x1, double x2, double y, double espesor)
    {
        var rad = espesor / 2;

        if (x2 - x1 <= espesor)
        {
            // Tan corta que sería solo los dos dobleces: se dibuja la raya y ya.
            Color(Linea(x1, y, x2, y, CapaEstribos), ColorEstribo);
            return;
        }

        Color(Linea(x1 + rad, y - rad, x2 - rad, y - rad, CapaEstribos), ColorEstribo);
        Color(Linea(x1 + rad, y + rad, x2 - rad, y + rad, CapaEstribos), ColorEstribo);

        Color(Arco(x1 + rad, y, rad, Math.PI / 2, 3 * Math.PI / 2, CapaEstribos), ColorEstribo);
        Color(Arco(x2 - rad, y, rad, 3 * Math.PI / 2, Math.PI / 2, CapaEstribos), ColorEstribo);
    }

    /// <summary>
    /// Las cotas del corte: la cadena de abajo y las verticales de la izquierda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son las mismas que pone la macro y en el mismo sitio, porque una cota fuera de su sitio se
    /// vuelve a poner a mano: abajo el ancho del dado con sus dos vuelos y el ancho total en un
    /// segundo renglón; a la izquierda, escalonadas, la plantilla, el espesor de la zapata, lo que
    /// hay de la zapata al terreno y el total.
    /// </para>
    /// <para>
    /// La de la plantilla —5 cm— va con el número <b>forzado dentro</b>: es la que AutoCAD sacaría
    /// fuera con una flecha, y en un plano de zapata eso queda encima de la propia plantilla.
    /// </para>
    /// </remarks>
    private void CotasDelCorte(TrazoZapata.Acomodo a, Resumen r)
    {
        var yCad = a.YZapBot - (0.14 * _f);
        var yTot = a.YZapBot - (0.24 * _f);

        if (a.XDadoIzq > a.XBase + 0.001)
        {
            r.Cotas += Cota(a.XBase, a.YZapBot, a.XDadoIzq, a.YZapBot,
                (a.XBase + a.XDadoIzq) / 2, yCad, vertical: false, dentro: true);
        }

        r.Cotas += Cota(a.XDadoIzq, a.YZapBot, a.XDadoDer, a.YZapBot,
            (a.XDadoIzq + a.XDadoDer) / 2, yCad, vertical: false, dentro: true);

        if (a.XDer > a.XDadoDer + 0.001)
        {
            r.Cotas += Cota(a.XDadoDer, a.YZapBot, a.XDer, a.YZapBot,
                (a.XDadoDer + a.XDer) / 2, yCad, vertical: false, dentro: true);
        }

        r.Cotas += Cota(a.XBase, a.YZapBot, a.XDer, a.YZapBot,
            (a.XBase + a.XDer) / 2, yTot, vertical: false, dentro: false);

        var x1 = a.XBase - (0.08 * _f);
        var x2 = a.XBase - (0.20 * _f);

        r.Cotas += Cota(a.XBase, a.YPlantillaBot, a.XBase, a.YZapBot,
            x1, (a.YPlantillaBot + a.YZapBot) / 2, vertical: true, dentro: true);

        r.Cotas += Cota(a.XBase, a.YZapBot, a.XBase, a.YZapTop,
            x1, (a.YZapBot + a.YZapTop) / 2, vertical: true, dentro: true);

        if (a.YTerreno > a.YZapTop + 0.001)
        {
            r.Cotas += Cota(a.XBase, a.YZapTop, a.XBase, a.YTerreno,
                x1, (a.YZapTop + a.YTerreno) / 2, vertical: true, dentro: true);
        }

        r.Cotas += Cota(a.XBase, a.YPlantillaBot, a.XBase, a.YTerreno,
            x2, (a.YPlantillaBot + a.YTerreno) / 2, vertical: true, dentro: false);
    }

    /// <summary>El rótulo del corte: el nombre de la zapata y su renglón de datos.</summary>
    /// <remarks>
    /// El segundo renglón va justo en <see cref="TrazoZapata.RotuloEscalaOffset"/> por debajo del
    /// fondo de la zapata, y eso <b>no es decoración</b>: es el renglón del que
    /// <see cref="TrazoZapata.Colocar"/> cuelga la planta. Moverlo de ahí descuadra la separación
    /// entre las dos vistas.
    /// </remarks>
    private void RotuloDelCorte(ZapataCad z, TrazoZapata.Acomodo a)
    {
        var xCen = (a.XBase + a.XDer) / 2;

        var titulo = string.IsNullOrWhiteSpace(z.Id)
            ? "ZAPATA AISLADA"
            : $"ZAPATA {z.Id.ToUpperInvariant()}";

        if (TrazoZapata.EsLindero(z.Tipo))
        {
            titulo += " (LINDERO)";
        }

        Texto(xCen, a.YZapBot - (0.36 * _f), titulo, AlturaTitulo * _f, CapaRotulos);

        var partes = new List<string>
        {
            $"Rec. {z.RecM * 100:0.#} cm"
        };

        if (!string.IsNullOrWhiteSpace(z.Fc))
        {
            partes.Add($"f'c = {z.Fc}");
        }

        partes.Add($"Escala 1:{10 * _f:0.#}");

        Texto(xCen, a.YZapBot - TrazoZapata.RotuloEscalaOffset, string.Join("    ·    ", partes),
            AlturaTexto * _f, CapaRotulos);
    }

    // ======================================================================
    // La planta
    // ======================================================================

    /// <summary>La planta, colgada de la vista de corte donde diga el acomodo.</summary>
    private void Planta(ZapataCad z, TrazoZapata.Acomodo a, Resumen r)
    {
        var yBot = a.YPlanta;
        var yTop = a.YPlanta + z.LargoM;

        var (hx1, hy1, hx2, hy2) = TrazoZapata.HuecoDelDado(z, a.XBase, yBot);

        // Primero las mallas y encima el dado: en planta el dado tapa la malla, no al contrario.
        Malla(z, a, yBot, yTop, z.VarInf, z.SepInf, z.VarInfTrans, z.SepInfTrans,
            ColorAcero, hx1, hy1, hx2, hy2, r);

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup))
        {
            Malla(z, a, yBot, yTop, z.VarSup, z.SepSup, z.VarSupTrans, z.SepSupTrans,
                ColorAceroSup, hx1, hy1, hx2, hy2, r);

            // La diagonal de rotura: es lo que en el plano separa la parrilla de arriba de la de
            // abajo, para poder acotar cada una sin dibujar dos plantas.
            Linea(a.XBase, yBot, a.XDer, yTop, CapaParrilla);
        }

        Rectangulo(a.XBase, yBot, a.XDer, yTop, CapaConcreto);

        DadoEnPlanta(z, hx1, hy1, hx2, hy2, r);

        // ---------- Cotas ----------
        r.Cotas += Cota(a.XBase, yBot, a.XDer, yBot,
            (a.XBase + a.XDer) / 2, yBot - (0.12 * _f), vertical: false, dentro: false);

        r.Cotas += Cota(a.XBase, yBot, a.XBase, yTop,
            a.XBase - (0.12 * _f), (yBot + yTop) / 2, vertical: true, dentro: false);

        r.Cotas += Cota(hx1, yTop, hx2, yTop,
            (hx1 + hx2) / 2, yTop + (0.10 * _f), vertical: false, dentro: true);

        r.Cotas += Cota(a.XDer, hy1, a.XDer, hy2,
            a.XDer + (0.10 * _f), (hy1 + hy2) / 2, vertical: true, dentro: true);

        // ---------- Rótulo ----------
        var xCen = (a.XBase + a.XDer) / 2;

        Texto(xCen, yBot - (0.26 * _f), "PLANTA", AlturaTitulo * _f, CapaRotulos);
        Texto(xCen, yBot - (0.26 * _f) - (AlturaTitulo * _f * 1.6),
            $"Escala 1:{10 * _f:0.#}", AlturaTexto * _f, CapaRotulos);
    }

    /// <summary>El dado en planta: su bloque, o un rectángulo con un aviso.</summary>
    /// <remarks>
    /// Se inserta el bloque del dado que ya dibujó la hoja de secciones de concreto, buscándolo
    /// por su ID, que es el nombre del bloque. Es lo que hace la macro y es lo correcto: el dado
    /// se dibuja una vez y se inserta donde haga falta, así que corregir su armado corrige todas
    /// las zapatas. Si el bloque no está en el dibujo se pone un rectángulo del tamaño del dado y
    /// se avisa: hace de hueco reservado y deja claro que falta insertar el dado de verdad.
    /// </remarks>
    private void DadoEnPlanta(ZapataCad z, double hx1, double hy1, double hx2, double hy2, Resumen r)
    {
        var id = (z.IdDado ?? string.Empty).Trim();

        var xCen = (hx1 + hx2) / 2;
        var yCen = (hy1 + hy2) / 2;

        if (id.Length > 0 && InsertarBloque(id, xCen, yCen))
        {
            r.DadosInsertados++;
            return;
        }

        Rectangulo(hx1, hy1, hx2, hy2, CapaBloqueDado);

        r.DadosDeRespaldo++;

        if (id.Length == 0)
        {
            Nota($"Zapata '{z.Id}': no tiene ID de dado, así que en la planta se puso el "
                 + "rectángulo del dado. Elige el dado en la celda y vuelve a dibujar.");
        }
        else if (_dadosQueFaltan.Add(id))
        {
            Nota($"El bloque del dado «{id}» no está en el dibujo: en la planta de la zapata se "
                 + "puso un rectángulo de su tamaño. Dibuja primero la sección del dado en la "
                 + "hoja de concreto y vuelve a dibujar la zapata.");
        }
    }

    /// <summary>
    /// Una malla en planta, recortada en el hueco del dado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las varillas que corren en X se reparten a lo largo de Y y al contrario, que es lo que hace
    /// <c>DibujarMallaPlanta</c> con <c>PosicionesConSeparacion</c>.
    /// </para>
    /// <para>
    /// <b>El recorte en el hueco del dado</b> es lo que hace que la planta se pueda leer: por
    /// debajo del dado la malla sigue, pero en el dibujo el dado va encima, así que las varillas
    /// que lo cruzan se parten en dos tramos y se dejan sus dos puntas a la vista. Dibujarlas
    /// enteras las haría pasar por delante del dado; no dibujarlas diría que la malla se
    /// interrumpe, que es un error de armado.
    /// </para>
    /// </remarks>
    private void Malla(
        ZapataCad z, TrazoZapata.Acomodo a, double yBot, double yTop,
        string? varX, string? sepX, string? varY, string? sepY, int color,
        double hx1, double hy1, double hx2, double hy2, Resumen r)
    {
        var dxCm = _diametroCm(varX);

        if (dxCm <= 0)
        {
            return;
        }

        var dyCm = Math.Max(_diametroCm(varY), 0);

        var rX = dxCm / 200.0;
        var rY = dyCm / 200.0;

        var xIni = a.XBase + z.RecM;
        var xFin = a.XDer - z.RecM;
        var yIni = yBot + z.RecM;
        var yFin = yTop - z.RecM;

        if (xFin <= xIni || yFin <= yIni)
        {
            Fallo($"Malla de '{z.Id}'",
                new InvalidOperationException(
                    $"el recubrimiento de {z.RecM * 100:0.#} cm no cabe en la zapata"));
            return;
        }

        foreach (var y in TrazoZapata.Posiciones(yIni + rX, yFin - rX, TrazoZapata.SeparacionM(sepX)))
        {
            if (y > hy1 && y < hy2)
            {
                r.Varillas += Color(Linea(xIni, y, hx1, y, CapaParrilla), color) ? 1 : 0;
                r.Varillas += Color(Linea(hx2, y, xFin, y, CapaParrilla), color) ? 1 : 0;
                continue;
            }

            r.Varillas += Color(Linea(xIni, y, xFin, y, CapaParrilla), color) ? 1 : 0;
        }

        foreach (var x in TrazoZapata.Posiciones(xIni + rY, xFin - rY, TrazoZapata.SeparacionM(sepY)))
        {
            if (x > hx1 && x < hx2)
            {
                r.Varillas += Color(Linea(x, yIni, x, hy1, CapaParrilla), color) ? 1 : 0;
                r.Varillas += Color(Linea(x, hy2, x, yFin, CapaParrilla), color) ? 1 : 0;
                continue;
            }

            r.Varillas += Color(Linea(x, yIni, x, yFin, CapaParrilla), color) ? 1 : 0;
        }
    }

    // ======================================================================
    // Primitivas de AutoCAD
    // ======================================================================

    /// <summary>Crea las capas de la zapata si no existen. Nunca cambia las que ya hay.</summary>
    public void AsegurarCapas()
    {
        var capas = new (string Nombre, int Color)[]
        {
            (CapaConcreto, ColorConcreto),
            (CapaEstribos, ColorEstribo),
            (CapaParrilla, ColorAcero),
            (CapaPlantilla, ColorPlantilla),
            (CapaTerrenoLinea, ColorTerreno),
            (CapaTerrenoHatch, ColorTerreno),
            (CapaBloqueDado, ColorConcreto),
            (CapaCotas, ColorRotulo),
            (CapaRotulos, ColorRotulo)
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
                        // Si ya existe se deja EXACTAMENTE como está: el usuario pudo darle su
                        // color y su grosor de pluma, y son sus capas de siempre.
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
            Nota($"No se pudo preparar el estilo de texto '{EstiloTexto}'; los rótulos de la "
                 + "zapata usan el estilo actual del dibujo. " + ex.Message);
        }
    }

    /// <summary>
    /// Deja las variables de cota y el estilo <c>COTA_ESTRUCTURAL</c> como las macros.
    /// </summary>
    /// <remarks>
    /// Es el mismo estilo que usan las secciones y los alzados, a propósito: en un plano con
    /// secciones, alzados y zapatas, tres estilos de cota distintos se ven como tres personas
    /// dibujando. Si el estilo ya existe se refresca desde el documento, que es como lo hace la
    /// hoja de secciones.
    /// </remarks>
    private void ConfigurarCotas()
    {
        Dimvar("DIMSCALE", 1d);
        Dimvar("DIMEXO", 0.02 * _f);
        Dimvar("DIMEXE", 0.035 * _f);
        Dimvar("DIMDLE", 0d);
        Dimvar("DIMTXT", 0.025 * _f);
        Dimvar("DIMASZ", 0.025 * _f);
        Dimvar("DIMGAP", 0.008 * _f);

        // Cotas en metros con dos decimales, que es como se lee un plano de cimentación.
        Dimvar("DIMLUNIT", 2);
        Dimvar("DIMDEC", 2);
        Dimvar("DIMZIN", 0);

        // Marcas abiertas en lugar de flechas rellenas, como la macro. DIMSAH va PRIMERO: dice
        // que las dos puntas usan el mismo bloque, y con DIMSAH en 1 la asignacion de DIMBLK se
        // rechaza. Al reves salia un aviso por cada dibujo para algo que no fallaba de verdad.
        Dimvar("DIMSAH", 0);
        Dimvar("DIMBLK", "_OPEN90");

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
            // Sin estilo propio las cotas usan el activo del dibujo: se pierde uniformidad, no
            // las cotas.
            Nota($"No se pudo preparar el estilo de cota '{EstiloCota}'; las cotas de la zapata "
                 + "usan el estilo activo del dibujo.");
        }
    }

    /// <summary>Fija una variable de cota, tolerando que esta versión no la acepte.</summary>
    private void Dimvar(string nombre, object valor)
    {
        try
        {
            // El cuerpo va entre llaves a propósito: con una expresión, al ser '_doc' dinámico,
            // la lambda podría resolverse al Retry<T> genérico.
            AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
        }
        catch (Exception ex)
        {
            Nota($"La variable de cota {nombre} no aceptó '{valor}'; la cota sale con lo que "
                 + "tenga el dibujo. " + ex.Message);
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
        catch (Exception ex)
        {
            Fallo($"Línea de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Arco(double cx, double cy, double radio, double a0, double a1, string capa)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic arc = _ms.AddArc(new[] { cx, cy, 0d }, radio, a0, a1);
                arc.Layer = capa;
                arc.Color = PorCapa;
                return (object?)arc;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Arco de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Circulo(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _ms.AddCircle(new[] { cx, cy, 0d }, radio);
                c.Layer = capa;
                c.Color = PorCapa;
                return (object?)c;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Varilla vista de punta en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Polilinea(double[] puntos, string capa, bool cerrada)
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
                p.Closed = cerrada;
                p.Layer = capa;
                p.Color = PorCapa;
                return (object?)p;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilínea de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    /// <summary>Un rectángulo por sus dos esquinas, como polilínea cerrada.</summary>
    /// <remarks>
    /// Cerrada y de una pieza porque así sirve de <b>frontera de hatch</b> y porque en el plano se
    /// selecciona el paño completo con un clic. Cuatro líneas suministrarían el mismo dibujo y
    /// ningún hatch.
    /// </remarks>
    private object? Rectangulo(double x1, double y1, double x2, double y2, string capa)
    {
        var xa = Math.Min(x1, x2);
        var xb = Math.Max(x1, x2);
        var ya = Math.Min(y1, y2);
        var yb = Math.Max(y1, y2);

        if (xb - xa < 1e-9 || yb - ya < 1e-9)
        {
            return null;
        }

        return Polilinea(new[] { xa, ya, xb, ya, xb, yb, xa, yb }, capa, cerrada: true);
    }

    /// <summary>Rellena una frontera cerrada. Devuelve el hatch, o <c>null</c> si no se pudo.</summary>
    /// <remarks>
    /// Si el patrón no está disponible se reintenta con <c>ANSI31</c>, que viene en cualquier
    /// AutoCAD: un rayado distinto se entiende igual, un paño sin relleno no.
    /// </remarks>
    private object? HatchDe(object? borde, string patron, double escala, string capa, int colorAci)
    {
        if (borde is null)
        {
            return null;
        }

        var h = Hatch(borde, patron, escala, capa, colorAci);

        if (h is null && !patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase)
                      && !patron.Equals(PatronRespaldo, StringComparison.OrdinalIgnoreCase))
        {
            Nota($"El patrón '{patron}' no se pudo usar; se rellenó con '{PatronRespaldo}'.");
            h = Hatch(borde, PatronRespaldo, escala, capa, colorAci);
        }

        return h;
    }

    private object? Hatch(object borde, string patron, double escala, string capa, int colorAci)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                // NO asociativo -el 'false'-, y es importante: el relleno del terreno borra su
                // frontera en cuanto esta rellenado, porque esa frontera coincide con lineas que
                // ya estan dibujadas. Un hatch asociativo se iria con ella. Es tambien lo que
                // hacen los dibujantes de secciones y de alzados.
                dynamic h = _ms.AddHatch(0, patron, false);
                h.HatchStyle = 0;

                var ok = AcadArreglos.Llamar(
                    $"AppendOuterLoop del hatch '{patron}' de la zapata",
                    new[] { borde },
                    arr => { h.AppendOuterLoop(arr); },
                    Fallo, Nota);

                if (!ok)
                {
                    // Un hatch sin frontera es una entidad degenerada: se borra para que no rompa
                    // después el cálculo de extensiones ni el ZoomExtents.
                    Borrar((object)h);
                    return null;
                }

                if (!patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    h.PatternScale = escala;
                }

                h.Layer = capa;
                h.Color = colorAci;
                h.Evaluate();

                // Se repite después de Evaluate: alguna versión reevalúa el hatch y le devuelve
                // el color de la capa.
                h.Layer = capa;
                h.Color = colorAci;

                return (object?)h;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}' de la zapata", ex);
            return null;
        }
    }

    /// <summary>Una cota alineada. Devuelve 1 si se puso, 0 si no, para el resumen.</summary>
    private int Cota(
        double x1, double y1, double x2, double y2,
        double xt, double yt, bool vertical, bool dentro)
    {
        if (Math.Abs(x2 - x1) < 1e-6 && Math.Abs(y2 - y1) < 1e-6)
        {
            return 0;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(
                    new[] { x1, y1, 0d }, new[] { x2, y2, 0d }, new[] { xt, yt, 0d });

                try
                {
                    d.StyleName = EstiloCota;
                }
                catch (Exception)
                {
                    // Sin el estilo, la cota sale con el activo. No es motivo para perderla.
                }

                d.Layer = CapaCotas;

                if (dentro)
                {
                    // El número DENTRO de la cota: es lo que la macro resuelve con DIMTIX, y sin
                    // eso las cotas cortas —la plantilla de 5 cm— sacan el número con una flecha
                    // que acaba encima del dibujo.
                    d.TextInside = true;
                    d.TextInsideAlign = true;
                }

                if (vertical)
                {
                    d.TextRotation = Math.PI / 2;
                }

                d.Update();
            });

            return 1;
        }
        catch (Exception ex)
        {
            Fallo("Cota de la zapata", ex);
            return 0;
        }
    }

    private object? Texto(double x, double y, string texto, double altura, string capa)
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

                try
                {
                    // 5 = MiddleCenter: el rótulo queda centrado en el eje de la zapata, que es
                    // el punto que se le pasa.
                    mt.AttachmentPoint = 5;
                    mt.InsertionPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Alguna versión no deja mover el anclaje después de crear el MText: el
                    // rótulo queda algo corrido, pero está.
                }

                try
                {
                    mt.StyleName = EstiloTexto;
                }
                catch (Exception)
                {
                    // Sin el estilo, sale con el del dibujo.
                }

                mt.Layer = capa;
                mt.Color = PorCapa;
                return (object?)mt;
            });
        }
        catch (Exception ex)
        {
            Fallo("Rótulo de la zapata", ex);
            return null;
        }
    }

    /// <summary>Inserta un bloque que ya exista en el dibujo. <c>false</c> si no está.</summary>
    private bool InsertarBloque(string nombre, double x, double y)
    {
        try
        {
            AcadConnection.Retry(() => { _ = _doc.Blocks.Item(nombre); });
        }
        catch (Exception)
        {
            // No está: quien llama decide qué poner en su lugar. No es un fallo del dibujo.
            return false;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic r = _ms.InsertBlock(new[] { x, y, 0d }, nombre, 1d, 1d, 1d, 0d);
                r.Layer = CapaBloqueDado;
            });

            return true;
        }
        catch (Exception ex)
        {
            Fallo($"Insertar el bloque del dado '{nombre}'", ex);
            return false;
        }
    }

    /// <summary>Le pone color a una entidad. Devuelve si la entidad existía.</summary>
    private bool Color(object? ent, int colorAci)
    {
        if (ent is null)
        {
            return false;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).Color = colorAci; });
        }
        catch (Exception ex)
        {
            Nota("No se pudo dar color a una entidad de la zapata; queda con el de su capa. "
                 + ex.Message);
        }

        return true;
    }

    /// <summary>
    /// Le da a la polilínea del acero el grosor de la varilla.
    /// </summary>
    /// <remarks>
    /// Con el grosor real, la parrilla del #6 se ve más gruesa que la del #4 en el plano impreso,
    /// que es información: se distingue el armado de un trazo cualquiera. Si la versión no acepta
    /// el grosor constante, la línea se queda fina y el dibujo sigue siendo correcto.
    /// </remarks>
    private void Grosor(object? ent, double grosor)
    {
        if (ent is null || grosor <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).ConstantWidth = grosor; });
        }
        catch (Exception)
        {
            Nota("No se pudo dar grosor a una varilla; queda dibujada con línea fina.");
        }
    }

    /// <summary>Transparencia de 0 a 90, para el relleno del terreno.</summary>
    private void Transparencia(object? ent, int valor)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).EntityTransparency = valor.ToString(); });
        }
        catch (Exception)
        {
            Nota("No se pudo dar transparencia al relleno del terreno; queda opaco. Si tapa el "
                 + "acero, apaga la capa " + CapaTerrenoHatch + ".");
        }
    }

    private void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).Delete(); });
        }
        catch (Exception)
        {
            // Si no se puede borrar, se queda una entidad de más. No vale un aviso.
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
}
