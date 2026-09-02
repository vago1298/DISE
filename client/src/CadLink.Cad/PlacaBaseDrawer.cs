using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// Dibuja el detalle de una <b>placa base</b> en AutoCAD, por COM.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>DibujarPlacaBase_BloqueXX</c>. Respeta sus capas, sus colores, sus patrones de
/// achurado y sus estilos, que están recogidos en <see cref="PlacaBaseCapas"/>.
/// </para>
/// <para>
/// <b>El perfil de la columna no se traza aquí.</b> Se pide a <see cref="TrazoAcero"/>, que ya
/// tenía portadas las nueve formas del manual IMCA con la misma geometría que la macro dibujaba a
/// mano. Duplicarla habría dejado dos juegos de fórmulas para el mismo perfil, y el día que una se
/// corrigiera la otra seguiría mal.
/// </para>
/// <para>
/// <b>Qué queda fuera del bloque.</b> Igual que en la macro: las cotas y todo lo de la capa
/// ROTULOS —los rótulos, los leaders y sus flechas— se quedan sueltos en el espacio modelo. El
/// bloque agrupa solo la geometría, para que el detalle se pueda mover de una pieza sin arrastrar
/// las cotas.
/// </para>
/// </remarks>
public sealed partial class PlacaBaseDrawer
{
    private const double Pi = Math.PI;

    /// <summary>Color «por capa».</summary>
    private const int PorCapa = 256;

    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly double _escala;

    /// <summary>Altura de texto en unidades de dibujo.</summary>
    private readonly double _hTxt;

    /// <summary>Tamaño de flecha en unidades de dibujo.</summary>
    private readonly double _hFle;

    private readonly List<string> _log = new();
    private readonly List<string> _notas = new();

    /// <summary>El estilo que de verdad se usó en el MTEXT del rótulo.</summary>
    private string _estiloRotulo = PlacaBaseCapas.EstiloTexto;

    public IReadOnlyList<string> Fallos => _log;
    public IReadOnlyList<string> Notas => _notas;

    /// <summary>El nombre del bloque que se creó, para poder decírselo al usuario.</summary>
    public string UltimoBloque { get; private set; } = string.Empty;

    /// <param name="escala">Cuántas unidades de dibujo mide un centímetro. 0.01 = dibujo en metros.</param>
    public PlacaBaseDrawer(dynamic doc, double escala = 0.01)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);
        _escala = escala <= 0 ? 0.01 : escala;

        // La altura del texto es FIJA, la del estilo ACERO_PLACA. La macro solo la calcula por
        // milímetros ploteados si esa constante viniera en cero.
        _hTxt = PlacaBaseCapas.AlturaTextoDwg;
        _hFle = PlacaBaseCapas.AltoFlechaMm / 10.0 * 10.0 * _escala;
    }

    private void Fallo(string operacion, Exception ex)
    {
        var e = ex;

        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var detalle = e.GetType().Name;

        if (e is System.Runtime.InteropServices.COMException com)
        {
            detalle += $" 0x{(uint)com.HResult:X8}";
        }

        var linea = operacion + " -> " + detalle + ": " +
                    e.Message.Replace(Environment.NewLine, " ").Trim();

        if (!_log.Contains(linea))
        {
            _log.Add(linea);
        }
    }

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }

    // ======================================================================
    //  CAPAS Y ESTILOS
    // ======================================================================

    /// <summary>Crea las capas de la macro con sus colores.</summary>
    /// <remarks>
    /// <b>Solo la placa y la soldadura fuerzan su color.</b> Es lo que hace la macro con su
    /// parámetro <c>forzarColor</c>, y tiene sentido: esas dos son de esta macro, mientras que
    /// CONCRETO, PERFILES, ROTULOS o COTAS suelen venir ya de la plantilla del usuario, y pisarles
    /// el color le desharía su configuración cada vez que dibuja una placa.
    /// </remarks>
    public void AsegurarCapas()
    {
        Capa(PlacaBaseCapas.Placa, PlacaBaseCapas.ColorPlaca, forzar: true);
        Capa(PlacaBaseCapas.Anclas, PlacaBaseCapas.ColorAnclas, forzar: false);
        Capa(PlacaBaseCapas.Rotulos, PlacaBaseCapas.ColorRotulos, forzar: false);
        Capa(PlacaBaseCapas.Cotas, PlacaBaseCapas.ColorCotas, forzar: false);
        Capa(PlacaBaseCapas.Concreto, PlacaBaseCapas.ColorConcreto, forzar: false);
        Capa(PlacaBaseCapas.Perfiles, PlacaBaseCapas.ColorPerfiles, forzar: false);
        Capa(PlacaBaseCapas.Cartabones, PlacaBaseCapas.ColorCartabones, forzar: false);
        Capa(PlacaBaseCapas.Soldadura, PlacaBaseCapas.ColorSoldadura, forzar: true);

        // La de los cartabones FUERZA su color, igual que la del perfil: las dos son de esta macro
        // —no vienen de la plantilla del usuario— y el morado es lo que las distingue en el detalle.
        Capa(PlacaBaseCapas.SoldaduraCartabon, PlacaBaseCapas.ColorSoldaduraCartabon, forzar: true);

        AsegurarEstiloTexto();
        AsegurarEstiloCota();
    }

    private void Capa(string nombre, int color, bool forzar)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic capas = _doc.Layers;
                dynamic capa;
                var nueva = false;

                try
                {
                    capa = capas.Item(nombre);
                }
                catch (Exception)
                {
                    capa = capas.Add(nombre);
                    nueva = true;
                }

                if (nueva || forzar)
                {
                    capa.Color = color;
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Capa '{nombre}'", ex);
        }
    }

    /// <summary>Crea <c>ACERO_PLACA</c> y decide con qué estilo va el rótulo.</summary>
    private void AsegurarEstiloTexto()
    {
        // El rótulo usa SECCIONES solo si de verdad está en el dibujo. Si no, cae a ACERO_PLACA:
        // es lo que hace la macro, y así el rótulo sale con letra aunque la plantilla no traiga su
        // estilo.
        _estiloRotulo = ExisteEstiloTexto(PlacaBaseCapas.EstiloRotulo)
            ? PlacaBaseCapas.EstiloRotulo
            : PlacaBaseCapas.EstiloTexto;

        if (_estiloRotulo != PlacaBaseCapas.EstiloRotulo)
        {
            Nota($"No se encontró el estilo de texto '{PlacaBaseCapas.EstiloRotulo}', así que el " +
                 $"rótulo se dibujó con '{PlacaBaseCapas.EstiloTexto}'.");
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(PlacaBaseCapas.EstiloTexto);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(PlacaBaseCapas.EstiloTexto);
                }

                // Los dos false son negrita e itálica; el 0 y el 34 son juego de caracteres y
                // familia, igual que el SetFont de la macro.
                estilo.SetFont(PlacaBaseCapas.FuenteTexto, false, false, 0, 34);
                estilo.Height = PlacaBaseCapas.AlturaTextoDwg;
                estilo.Width = 1.0;
                estilo.ObliqueAngle = 0.0;
            });
        }
        catch (Exception ex)
        {
            // Si la fuente no está instalada, AutoCAD la sustituye y el texto sale igual.
            Fallo($"Estilo de texto '{PlacaBaseCapas.EstiloTexto}'", ex);
        }
    }

    private bool ExisteEstiloTexto(string nombre)
    {
        try
        {
            return AcadConnection.Retry(() =>
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
            return false;
        }
    }

    /// <summary>
    /// Fija <b>una sola</b> variable de cota, tolerando que esta versión la rechace.
    /// </summary>
    /// <remarks>
    /// Una por una, y no todas dentro del mismo <c>try</c>. En VBA el <c>On Error Resume Next</c>
    /// es tolerancia <b>por instrucción</b>: si una variable no se puede asignar, las siguientes sí
    /// se ejecutan. Metiéndolas en un bloque compartido, un solo rechazo se llevaría por delante
    /// todas las de después y el estilo saldría con los valores de la plantilla.
    /// </remarks>
    private void Dimvar(string nombre, params object[] valores)
    {
        Exception? ultimo = null;

        foreach (var valor in valores)
        {
            try
            {
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
            Fallo($"Variable de cota {nombre}", ultimo);
        }
    }

    /// <summary>Crea o refresca <c>COTA_ACERO</c> con los valores de la macro.</summary>
    private void AsegurarEstiloCota()
    {
        Dimvar("DIMSCALE", 1.0);
        Dimvar("DIMTXT", _hTxt);
        Dimvar("DIMASZ", _hFle);

        // Las dos líneas de extensión. Son las que la macro fuerza SIEMPRE, aunque el estilo ya
        // exista en la plantilla, porque una plantilla heredada de un plano a escala de impresión
        // las trae cien veces mayores y las cotas salen con unos remates enormes.
        Dimvar("DIMEXE", PlacaBaseCapas.DimExe);
        Dimvar("DIMEXO", PlacaBaseCapas.DimExo);

        Dimvar("DIMGAP", 0.08 * 10.0 * _escala);
        Dimvar("DIMDLI", 0.25 * 10.0 * _escala);
        Dimvar("DIMTAD", 1);
        Dimvar("DIMTIH", 0);
        Dimvar("DIMTOH", 0);
        Dimvar("DIMJUST", 0);
        Dimvar("DIMATFIT", 3);
        Dimvar("DIMTOFL", 1);
        Dimvar("DIMLUNIT", 2);
        Dimvar("DIMDEC", 2);
        Dimvar("DIMZIN", 0);

        // El texto de las cotas se lee siempre en CENTÍMETROS, aunque el dibujo esté en metros.
        Dimvar("DIMLFAC", 1.0 / _escala);
        Dimvar("DIMCLRT", 1);
        Dimvar("DIMTFILL", 0);
        Dimvar("DIMTXSTY", PlacaBaseCapas.EstiloTexto);

        // El punto como separador decimal. Se prueba el código ASCII, que es lo documentado, y si
        // AutoCAD no lo acepta, el carácter en texto.
        Dimvar("DIMDSEP", 46, ".");

        // Las flechas: la marca oblicua. Se fijan las tres porque cuál manda depende de DIMSAH.
        Dimvar("DIMSAH", 1);
        Dimvar("DIMBLK", PlacaBaseCapas.FlechaCota);
        Dimvar("DIMBLK1", PlacaBaseCapas.FlechaCota);
        Dimvar("DIMBLK2", PlacaBaseCapas.FlechaCota);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(PlacaBaseCapas.EstiloCota);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(PlacaBaseCapas.EstiloCota);
                }

                // CopyFrom copia el estado ACTUAL del documento, así que va después de fijar las
                // variables. Al revés saldría un estilo de fábrica.
                estilo.CopyFrom(_doc);
                _doc.ActiveDimStyle = estilo;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Estilo de cota '{PlacaBaseCapas.EstiloCota}'", ex);
        }
    }

    // ======================================================================
    //  EL DETALLE
    // ======================================================================

    /// <summary>Dibuja el detalle completo. Devuelve cuántas entidades se crearon.</summary>
    public int Dibujar(PlacaBaseCad p)
    {
        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);

        var x0 = p.InsercionX;
        var y0 = p.InsercionY;

        // Las medidas YA orientadas: si la placa se gira, se intercambian ancho y largo.
        var b = p.AnchoDibujoCm * _escala;
        var h = p.AltoDibujoCm * _escala;

        var dadoX = p.DadoXDibujoCm * _escala;
        var dadoY = p.DadoYDibujoCm * _escala;

        var dAncX = p.DiamAnclaXCm * _escala;
        var dAncY = p.DiamAnclaYCm * _escala;

        // Sin diámetro de agujero, el ancla más 1/16" de holgura. Es el respaldo de la macro.
        var dAguX = p.DiamAgujeroXCm > 0
            ? p.DiamAgujeroXCm * _escala
            : dAncX + (1.0 / 16.0 * 2.54 * _escala);

        var dAguY = p.DiamAgujeroYCm > 0
            ? p.DiamAgujeroYCm * _escala
            : dAncY + (1.0 / 16.0 * 2.54 * _escala);

        // ---------- El perfil, para poder medir la separación al borde ----------
        var (pX, pY) = MedidasDelPerfil(p);

        // El paño de la columna, ya girado. Se saca ANTES de dibujar nada porque la revisión de la
        // columna L mide contra él y tiene que poder negarse a dibujar.
        var panoColumna = p.PanoDeLaColumna(x0 + (b / 2), y0 + (h / 2), _escala);

        // ---------- Separación al borde, respetando la DISTANCIA K del cuadro ----------
        // K es la distancia mínima del ancla al canto recortado de la placa, que es exactamente lo
        // que se captura en «Sep borde». Se ajusta aquí y no solo en la celda de la hoja: la
        // separación se pudo capturar ANTES de cambiar el diámetro del ancla, y en ese caso la celda
        // quedó con un número que ya no cumple. Ajustando también al dibujar, el plano cumple siempre.
        var sepX = AnclasPlacaBase.SepBordeAjustada(p.SepBordeXCm, p.DiamAnclaXCm, p.AnchoDibujoCm);
        var sepY = AnclasPlacaBase.SepBordeAjustada(p.SepBordeYCm, p.DiamAnclaYCm, p.AltoDibujoCm);

        sepX = sepX > 0
            ? sepX * _escala
            : AnclasPlacaBase.SepAuto(
                b, pX, dAguX, _escala, AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaXCm) * _escala);

        sepY = sepY > 0
            ? sepY * _escala
            : AnclasPlacaBase.SepAuto(
                h, pY, dAguY, _escala, AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaYCm) * _escala);

        // ---------- Las anclas ----------
        var anclas = AnclasPlacaBase.Construir(
            x0, y0, b, h, p.NAnclasX, p.NAnclasY, sepX, sepY,
            dAncX, dAguX, dAncY, dAguY);

        // ---------- Los libramientos, ANTES de dibujar ----------
        if (p.ValidarSeparacionAnclas)
        {
            var falla = AnclasPlacaBase.RevisarSeparacionJ(anclas, _escala)
                        ?? AnclasPlacaBase.RevisarDistanciaK(anclas, x0, y0, b, h, _escala)
                        ?? AnclasPlacaBase.RevisarHolguraColumnaL(
                               anclas, panoColumna?.Puntos, _escala);

            if (falla is not null)
            {
                // NO SE DIBUJA NADA. Es lo que hace la macro, y es lo correcto: un detalle con las
                // anclas más juntas de lo que la tabla permite no es un detalle a medias, es un
                // detalle que no se puede construir, y dibujarlo lo pone en camino a obra.
                _log.Add(falla.Titulo + ": " + falla.Detalle);
                return 0;
            }
        }

        var yTop = y0 + h;
        var yBot = y0;
        var xLef = x0;
        var xRig = x0 + b;

        // ---------- El dado, en la capa CONCRETO ----------
        object? contornoDado = null;

        if (p.DibujarDado && dadoX > 0 && dadoY > 0)
        {
            var dx0 = x0 + ((b - dadoX) / 2);
            var dy0 = y0 + ((h - dadoY) / 2);

            // EL DADO REDONDO SE DIBUJA REDONDO. Viene de la hoja de secciones de concreto, y ahí
            // un DADO CIRCULAR es otra forma: dibujarlo cuadrado pondría en el plano un dado que no
            // es el que se armó, con el mismo ID en el rótulo.
            contornoDado = p.DadoCircular
                ? Circulo(x0 + (b / 2), y0 + (h / 2), dadoX, PlacaBaseCapas.Concreto)
                : Rectangulo(dx0, dy0, dx0 + dadoX, dy0 + dadoY, PlacaBaseCapas.Concreto);

            if (dy0 + dadoY > yTop) { yTop = dy0 + dadoY; }
            if (dy0 < yBot) { yBot = dy0; }
            if (dx0 < xLef) { xLef = dx0; }
            if (dx0 + dadoX > xRig) { xRig = dx0 + dadoX; }
        }

        // ---------- La placa ----------
        var contornoPlaca = Rectangulo(x0, y0, x0 + b, y0 + h, PlacaBaseCapas.Placa);

        if (contornoPlaca is not null)
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    ((dynamic)contornoPlaca).ConstantWidth = PlacaBaseCapas.AnchoLineaPlaca;
                    ((dynamic)contornoPlaca).Update();
                });
            }
            catch (Exception ex)
            {
                Fallo("Ancho de la polilínea de la placa", ex);
            }
        }

        // ---------- El rayado del dado, SOLO en la franja que sobresale ----------
        // La placa entra como isla, así que lo que queda bajo la placa no se raya: ahí lo que se
        // ve es la placa, no el concreto.
        // LA PLACA TIENE QUE CABER ENTERA DENTRO DEL DADO para poder entrar como isla. Si los dos
        // contornos se cruzan, el rayado no es «la franja que sobresale»: son dos bordes que se
        // cortan, y AutoCAD o falla o raya de más.
        //
        // En el dado rectangular basta comparar lado a lado. En el REDONDO hay que medir la
        // DIAGONAL de la placa: un dado de 50 cm de diámetro sobresale por el centro de los lados
        // de una placa de 40x40 y sin embargo sus esquinas —que están a 56.6 cm— quedan FUERA del
        // círculo. Comparando solo los lados, ese caso pasaría el filtro y el rayado saldría mal.
        var placaCabeEnElDado = p.DadoCircular
            ? dadoX > Math.Sqrt((b * b) + (h * h)) + 1e-6
            : dadoX > b + 1e-6 && dadoY > h + 1e-6;

        if (p.DibujarHatchDado && contornoDado is not null && contornoPlaca is not null
            && placaCabeEnElDado)
        {
            var hatch = Hatch(
                PlacaBaseCapas.PatronDado, PlacaBaseCapas.EscalaHatchDado,
                contornoDado, new List<object> { contornoPlaca },
                PlacaBaseCapas.Concreto, PorCapa);

            if (hatch is not null)
            {
                AlFondo(new List<object> { hatch });
            }
        }
        else if (p.DibujarHatchDado && p.DadoCircular && contornoDado is not null
                 && dadoX > 0 && dadoX <= Math.Sqrt((b * b) + (h * h)))
        {
            // SE DICE POR QUÉ NO SE RAYÓ. Un dado redondo sin rayado y sin explicación se lee como
            // un fallo del programa; lo que pasa es que las esquinas de la placa se salen del
            // círculo, y eso es un dato del detalle que conviene mirar.
            Nota($"El dado redondo de {dadoX / _escala:0.#} cm no se rayó: las esquinas de la " +
                 $"placa ({Math.Sqrt((b * b) + (h * h)) / _escala:0.#} cm de diagonal) se salen " +
                 "del círculo, así que el contorno de la placa no puede entrar como isla.");
        }

        // ---------- El perfil de la columna ----------
        var perfil = new List<object>();

        if (p.DibujarPerfil && p.Perfil is not null)
        {
            perfil = DibujarPerfil(p, x0 + (b / 2), y0 + (h / 2));

            if (perfil.Count > 0 && EsFormaI(p.Perfil.Forma))
            {
                // Las familias con forma de I llevan contorno ancho y su propio rayado.
                AcabadoPerfilI(perfil);
            }

            // La soldadura: la franja entre el paño del perfil y ese mismo paño corrido hacia fuera.
            if (p.DibujarSoldadura && p.SoldaduraCm > 0 && perfil.Count > 0)
            {
                Soldadura(p, perfil, panoColumna, x0 + (b / 2), y0 + (h / 2), xLef);
            }
        }

        // ---------- Los cartabones ----------
        var repartoCartabones = Cartabones(
            p, x0 + (b / 2), y0 + (h / 2), pX, pY, panoColumna);

        // ---------- Las anclas: dos círculos cada una ----------
        var nAncX = 0;
        var nAncY = 0;

        foreach (var a in anclas)
        {
            // El agujero va en la capa de la PLACA: es un hueco en la placa, no el ancla.
            Circulo(a.X, a.Y, a.DAgujero, PlacaBaseCapas.Placa);
            Circulo(a.X, a.Y, a.DAncla, PlacaBaseCapas.Anclas);

            if (a.EsX) { nAncX++; } else { nAncY++; }
        }

        // ---------- El bloque, ANTES de las cotas y los rótulos ----------
        // Se forma con lo que hay entre 'inicio' y aquí, que es solo geometría. Las cotas y los
        // rótulos se dibujan después y por eso se quedan fuera, igual que en la macro.
        var finGeometria = (int)AcadConnection.Retry(() => (int)_ms.Count);

        UltimoBloque = Bloquear(p.Seccion, inicio, finGeometria, x0, y0);

        // ---------- Las cotas ----------
        var o1 = 2.0 * _hTxt;
        var o2 = o1 + (2.5 * _hTxt);

        CadenaH(AnclasPlacaBase.ValoresUnicosX(anclas, _escala), x0 + b, y0 + h, yTop + o1);
        CotaH(x0, x0 + b, y0 + h, yTop + o2);

        CadenaV(AnclasPlacaBase.ValoresUnicosY(anclas, _escala), y0 + h, x0, xLef - o1);
        CotaV(y0, y0 + h, x0, xLef - o2);

        if (p.DibujarDado && dadoX > 0 && dadoY > 0)
        {
            // Estas dos son las únicas con las líneas de extensión hacia la placa.
            CotaH(x0 + ((b - dadoX) / 2), x0 + ((b + dadoX) / 2), yBot, yBot - o1, haciaObjeto: true);
            CotaV(y0 + ((h - dadoY) / 2), y0 + ((h + dadoY) / 2), xRig, xRig + o1, haciaObjeto: true);
        }

        // ---------- Los leaders ----------
        if (p.DibujarLeaders && anclas.Count > 0)
        {
            LeadersDeAnclas(p, anclas, xRig, y0 + (h / 2));
        }

        // CON EL REPARTO Y NO CON LAS ENTIDADES: para cuando esto corre, Bloquear ya copió la
        // geometría a la definición del bloque y borró las originales, así que preguntarle a una
        // polilínea dónde está devuelve nada y la flecha se iba al origen del dibujo.
        if (p.DibujarLeaders && repartoCartabones.Count > 0)
        {
            LeadersDeCartabones(p, repartoCartabones, xLef);
        }

        // ---------- El rótulo ----------
        var yr = yBot - o2 - (2.0 * _hTxt) + p.SubirRotulo - (p.BajarRotuloCm * _escala);

        Rotulo(p, anclas, nAncX, nAncY, x0 + (b / 2), yr);

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        return fin - inicio;
    }

    /// <summary>
    /// Las medidas del perfil <b>ya orientadas</b>, en unidades de dibujo.
    /// </summary>
    /// <remarks>
    /// El giro y la orientación los decide <see cref="PlacaBaseCad"/>, no este dibujante: la
    /// columna «Libramientos» de la tabla necesita la misma cuenta para avisar mientras se
    /// captura, y con la cuenta escrita dos veces la tabla y el dibujo pueden discrepar.
    /// </remarks>
    private (double X, double Y) MedidasDelPerfil(PlacaBaseCad p) =>
        (p.PerfilXDibujoCm * _escala, p.PerfilYDibujoCm * _escala);

    /// <summary>¿Se gira este perfil? Lo dice <see cref="PlacaBaseCad.GiraElPerfil"/>.</summary>
    private static bool GirarEstePerfil(PlacaBaseCad p) => p.GiraElPerfil;

    /// <summary>Si la forma es de las que se dibujan como perfil I.</summary>
    private static bool EsFormaI(string? forma) =>
        string.Equals(forma, FormaAcero.I, StringComparison.OrdinalIgnoreCase);

    // ======================================================================
    //  PRIMITIVAS
    // ======================================================================

    private static double[] Punto(double x, double y) => new[] { x, y, 0.0 };

    private object? Rectangulo(double x1, double y1, double x2, double y2, string capa) =>
        Polilinea(new[] { x1, y1, x2, y1, x2, y2, x1, y2 }, capa);

    private object? Polilinea(double[] puntos, string capa,
                              (int Indice, double Bulge)[]? dobleces = null)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(puntos);
                pl.Closed = true;
                pl.Layer = capa;
                pl.Color = PorCapa;

                if (dobleces is not null)
                {
                    foreach (var (i, bulge) in dobleces)
                    {
                        pl.SetBulge(i, bulge);
                    }
                }

                pl.Update();

                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilínea en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Circulo(double cx, double cy, double diametro, string capa)
    {
        if (diametro <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _ms.AddCircle(Punto(cx, cy), diametro / 2);
                c.Layer = capa;
                c.Color = PorCapa;
                return (object?)c;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Círculo en la capa '{capa}'", ex);
            return null;
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
                dynamic l = _ms.AddLine(Punto(xa, ya), Punto(xb, yb));
                l.Layer = capa;
                l.Color = PorCapa;
                return (object?)l;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Línea en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Hatch(string patron, double escala, object exterior,
                          List<object>? islas, string capa, int color)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic hh = _ms.AddHatch(0, patron, false);
                hh.HatchStyle = 0;

                var ok = AcadArreglos.Llamar(
                    $"AppendOuterLoop del hatch '{patron}'",
                    new[] { exterior },
                    arr => { hh.AppendOuterLoop(arr); },
                    Fallo, Nota);

                if (!ok)
                {
                    Borrar((object)hh);
                    return null;
                }

                if (islas is not null)
                {
                    foreach (var isla in islas)
                    {
                        AcadArreglos.Llamar(
                            $"AppendInnerLoop del hatch '{patron}'",
                            new[] { isla },
                            arr => { hh.AppendInnerLoop(arr); },
                            Fallo, Nota);
                    }
                }

                hh.PatternScale = escala;
                hh.PatternAngle = 0.0;
                hh.Layer = capa;
                hh.Color = color;
                hh.Evaluate();
                hh.Layer = capa;
                hh.Color = color;

                return (object?)hh;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}' en la capa '{capa}'", ex);
            return null;
        }
    }

    private static void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            ((dynamic)ent).Delete();
        }
        catch (Exception)
        {
            // Ya no existía.
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

                AcadArreglos.Llamar("MoveToBottom", objetos,
                    arr => { tabla.MoveToBottom(arr); }, Fallo, Nota);
            });
        }
        catch (Exception)
        {
            // El reordenado es estético.
        }
    }
}
