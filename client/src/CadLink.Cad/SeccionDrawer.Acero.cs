namespace CadLink.Cad;

/// <summary>
/// Secciones de <b>acero</b>: la parte de <see cref="SeccionDrawer"/> que dibuja las nueve
/// formas de perfil de las doce familias del catálogo.
/// </summary>
/// <remarks>
/// <para>
/// Port de las cuatro macros de la hoja de acero —<c>DibujarSeccionIR</c>,
/// <c>DibujarSeccionHSS</c>, <c>DibujarSeccionOC</c> y <c>DibujarSeccionCF</c>— <b>y de las
/// cinco formas que no tenían macro</b>: la te, el ángulo, la canal laminada, la zeta y el
/// redondo macizo. Esas cinco eran las que dejaban 499 perfiles del manual IMCA fuera del
/// catálogo, y no por falta de datos: por falta de quien los dibujara.
/// </para>
/// <para>
/// <b>Va aquí, y no en una clase aparte, para no duplicar la mitad del programa.</b> Las
/// cuatro macros repiten cada una sus capas, su estilo de texto, su formato de cota, su
/// sanitizado de nombres y su creación de bloques —el mismo código cuatro veces, con
/// pequeñas diferencias que son erratas más que decisiones—. En el port eso ya existe una
/// sola vez para el concreto: <see cref="Hatch"/>, <see cref="FormatearCota"/>,
/// <see cref="Bloquear"/>, <see cref="Capa"/> y compañía. El acero los reusa.
/// </para>
/// <para>
/// <b>Se dibuja por FORMA, no por familia.</b> Cuatro familias comparten la forma del perfil
/// I, así que el trazo se escribe una vez. Y el <b>rayado</b> también va por forma: cada una
/// lleva el de la macro que le corresponde, y las cinco que no tenían macro toman el de la
/// macro cuyo material comparten. Ver <see cref="RayarPerfil"/>.
/// </para>
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>
    /// Estilo de texto de las <b>cotas</b> del acero, el que crean las cuatro macros.
    /// </summary>
    /// <remarks>
    /// Las cuatro lo crean con <c>AsegurarTextStyleAcero</c> y se lo ponen a cada cota con
    /// <c>dimObj.TextStyle = "ACERO"</c>. Es distinto del de los rótulos —esos van con
    /// <c>SECCIONES</c>— y por eso hacen falta los dos.
    /// </remarks>
    private const string EstiloTextoAcero = "ACERO";

    /// <summary>Capa de los perfiles de acero, la de las cuatro macros.</summary>
    /// <remarks>
    /// <b>Una sola capa para las doce familias</b>, como en las macros. Se probó a darle una
    /// capa y un color a cada familia, y se quitó: el plano deja de parecerse al que ya se
    /// venía haciendo, y las cuatro familias portadas tienen cada una su propio rayado, que
    /// es lo que de verdad las distingue.
    /// </remarks>
    private const string CapaPerfiles = "PERFILES";

    /// <summary>Peralte, en pulgadas, a partir del cual el tubo rectangular se rellena.</summary>
    /// <remarks>
    /// Es de la macro del HSS, y <b>solo la afecta a ella</b>: por debajo de cinco pulgadas
    /// el tubo se raya fino con fondo cian, y de ahí para arriba se rellena sólido con un
    /// rayado más abierto encima.
    /// </remarks>
    private const double PeralteLimitePulg = 5.0;

    /// <summary>Un centímetro, en unidades de dibujo. Todo el acero se mide con esto.</summary>
    /// <remarks>
    /// El dibujo va en metros, así que un centímetro es <c>0.01</c> multiplicado por el
    /// factor de escala. Tenerlo con nombre evita el <c>0.01 * _f</c> repetido cuarenta
    /// veces y, sobre todo, deja claro que los números de abajo son centímetros de verdad.
    /// </remarks>
    private double Cm => 0.01 * _f;

    // ------------------------------------------------------------------
    //  El estado del perfil que se está dibujando
    // ------------------------------------------------------------------
    // Son campos y no parámetros porque los usan las quince funciones de cota de cada
    // forma, y pasarlos uno por uno convertiría cada firma en una lista de ocho
    // argumentos que nadie lee. Los fija PrepararAcero y valen durante UN perfil.

    /// <summary>Separación entre el perfil y sus cotas, proporcional al perfil.</summary>
    private double _gapAcero;

    /// <summary>Tamaño de la flecha de cota, proporcional al perfil.</summary>
    private double _flechaAcero;

    /// <summary>Altura del texto de cota, proporcional al perfil.</summary>
    private double _textoCotaAcero;

    /// <summary>Separación de la línea de extensión respecto de la pieza.</summary>
    private double _extOffsetAcero;

    /// <summary>Remate de la línea de extensión más allá de la línea de cota.</summary>
    private double _extExtiendeAcero;

    /// <summary>
    /// Deja el dibujo listo para las secciones de acero: capas, texto y cotas.
    /// </summary>
    /// <remarks>
    /// Las cuatro macros crean <c>PERFILES</c>, <c>COTAS</c> y <c>ROTULOS</c>, cada una a su
    /// manera y con un color distinto para la misma capa. Aquí se crean una vez con el
    /// blanco 7 que usan las tres macros que sí lo fijan.
    /// </remarks>
    public void AsegurarCapasAcero()
    {
        Capa(CapaPerfiles, 7);
        Capa("COTAS", 253);
        Capa("ROTULOS", 3);

        // Los DOS estilos de texto, que es lo que faltaba. Las macros usan uno para los
        // rótulos —SECCIONES— y otro para las cotas —ACERO—, y se lo ponen a cada cota a
        // mano. Sin crear el segundo, las cotas se quedaban con el de los rótulos.
        AsegurarEstiloTexto();
        AsegurarEstiloAcero();

        ConfigurarCotas();
    }

    /// <summary>
    /// Crea el estilo de texto <c>ACERO</c> de las cotas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Las cuatro macros no se ponen de acuerdo en cómo es este estilo</b>, así que hay
    /// que elegir, y conviene decir por qué:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Macro</term><description>Cómo lo crea</description></listheader>
    ///   <item><term>IR (V2)</term><description><c>BAHNSCHRIFT SEMILIGHT</c>, altura
    ///   <b>0.015</b></description></item>
    ///   <item><term>CF, OC, OR</term><description><c>arial.ttf</c>, altura
    ///   <b>0</b></description></item>
    /// </list>
    /// <para>
    /// <b>La fuente se toma de la IR</b>, que es la única en V2 y además la que coincide con
    /// el estilo de los rótulos y con el de las secciones de concreto: tres familias con
    /// Arial y una con Bahnschrift dejarían el mismo plano con dos tipografías en las cotas
    /// según de qué perfil sea cada una.
    /// </para>
    /// <para>
    /// <b>Y la altura se toma de las otras tres, que la dejan en cero</b>, y esto no es un
    /// término medio: es que el cero hace falta. Un estilo con altura fija <i>manda sobre
    /// la del texto</i>, así que con el 0.015 de la IR ninguna cota podría cambiarla, y las
    /// cuatro macros —la IR incluida— le fijan la altura a cada cota una por una con
    /// <c>TextHeight = 0.015</c>. O sea que la IR se contradice a sí misma: pone una altura
    /// fija en el estilo y luego la asigna por objeto. Con altura 0 las dos cosas encajan, y
    /// además es lo que permite que aquí la altura salga <b>proporcional al perfil</b>.
    /// </para>
    /// </remarks>
    private void AsegurarEstiloAcero()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(EstiloTextoAcero);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(EstiloTextoAcero);
                }

                estilo.SetFont(FuenteTexto, false, false, 0, 0);

                // Altura 0 = variable. Ver el remarks: es lo que permite que cada cota
                // fije la suya, que es lo que hacen las cuatro macros.
                estilo.Height = 0d;
                estilo.Width = FactorAnchoTexto;
            });
        }
        catch (Exception ex)
        {
            // Si la fuente no está instalada AutoCAD la sustituye y el texto sale igual;
            // solo cambia el tipo de letra.
            Fallo($"Estilo de texto '{EstiloTextoAcero}' con la fuente '{FuenteTexto}'", ex);
        }
    }

    /// <summary>
    /// Deja fijado todo lo que depende del <b>tamaño y la familia</b> de un perfil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El aparato de cota tiene que ser proporcional al perfil, y antes no lo era.</b> Los
    /// valores heredados del concreto —flecha de 2 cm, líneas de extensión de 3.5 cm, texto
    /// de 1.5 cm— se eligieron para secciones de 30 por 60, donde son un 5 % de la pieza. El
    /// catálogo de acero, en cambio, va de un redondo de 3/4" a una IS de 1.90 m: con el
    /// aparato fijo, una flecha de 2 cm sobre un ángulo de 1.9 cm es <b>más grande que el
    /// perfil</b> y la cota tapa lo que pretende medir.
    /// </para>
    /// <para>
    /// Las proporciones están puestas para que un perfil de 30 cm salga <b>exactamente igual
    /// que antes</b>: 30 cm entre 15 son los 2 cm de flecha de siempre. Lo que cambia es que
    /// de ahí para abajo el aparato se encoge con la pieza, y los topes evitan los dos
    /// extremos: que una IS de dos metros salga con flechas de 13 cm y que un redondo salga
    /// con cotas ilegibles.
    /// </para>
    /// <para>
    /// <b>El rayado no se toca:</b> cada forma lleva la separación y los colores de su
    /// macro, fijos. Un rayado con separación fija da la misma densidad en el papel para
    /// cualquier tamaño de perfil, que es lo que tiene que hacer un patrón de sombreado.
    /// </para>
    /// </remarks>
    private void PrepararAcero(PerfilAceroCad p)
    {
        // La referencia es el PERALTE, no el ancho ni la diagonal: es la medida con la que
        // se nombra el perfil y la que el ojo usa para juzgar su tamaño.
        var referencia = p.PeralteCm * _escala;

        _gapAcero = Acotar(referencia / 5, 0.8 * Cm, 6 * Cm);
        _flechaAcero = Acotar(referencia / 15, 0.4 * Cm, 2 * Cm);
        _textoCotaAcero = Acotar(referencia / 10, 0.4 * Cm, 1.5 * Cm);
        _extOffsetAcero = Acotar(referencia / 15, 0.3 * Cm, 2 * Cm);
        _extExtiendeAcero = Acotar(referencia / 8, 0.5 * Cm, 3.5 * Cm);
    }

    /// <summary>Un valor metido entre dos topes.</summary>
    private static double Acotar(double valor, double minimo, double maximo) =>
        valor < minimo ? minimo : valor > maximo ? maximo : valor;

    /// <summary>
    /// Dibuja un perfil de acero con sus cotas, su rótulo y su bloque.
    /// </summary>
    /// <remarks>
    /// Mismo contrato que <see cref="Dibujar"/>: si el bloque ya existe se salta —o se
    /// rehace en su sitio si <see cref="Redibujar"/> está encendido— y devuelve cuántas
    /// entidades se crearon.
    /// </remarks>
    /// <param name="p">El perfil, con las medidas en centímetros.</param>
    /// <param name="xIzquierda">Borde izquierdo del hueco donde entra el dibujo.</param>
    /// <param name="yAbajo">Base del perfil.</param>
    public int DibujarAcero(PerfilAceroCad p, double xIzquierda, double yAbajo)
    {
        UltimaFueASuSitio = false;

        if (!FormaAcero.Todas.Contains(p.Forma))
        {
            Nota(
                $"Sección de acero '{p.Id}': la forma '{p.Forma}' de la familia " +
                $"'{p.Familia}' no se reconoce, así que no se dibujó.");
            return 0;
        }

        double[]? destino = null;

        if (BloqueYaExiste(p.Id))
        {
            if (!Redibujar)
            {
                _saltadas.Add(p.Id);
                return 0;
            }

            destino = PuntoDeInsercion(p.Id);

            if (!BorrarSeccion(p.Id))
            {
                _saltadas.Add(p.Id);
                return 0;
            }

            _redibujadas.Add(p.Id);
            UltimaFueASuSitio = destino is not null;
        }

        PrepararAcero(p);

        var inicio = (int)_ms.Count;

        var h = p.PeralteCm * _escala;
        var b = p.AnchoCm * _escala;
        var t = p.EspesorCm * _escala;
        var tf = p.EspesorPatinCm * _escala;
        var labio = p.LabioCm * _escala;
        var radio = p.RadioCm * _escala;
        var bMenor = p.PatinAngostoCm * _escala;

        // El alto del DIBUJO, que en el tubo rectangular es el lado mayor aunque se haya
        // capturado al revés. En las otras ocho formas es el peralte y punto.
        var alto = p.AltoDibujoCm * _escala;

        // El hueco de UN perfil y el centro del dibujo completo. El doble ocupa dos huecos
        // pegados, así que el segundo empieza justo donde acaba el primero.
        var uno = p.AnchoDeUnoCm * _escala;
        var centro = xIzquierda + (p.AnchoDibujoCm * _escala / 2);
        var cuantos = p.Doble ? 2 : 1;

        for (var i = 0; i < cuantos; i++)
        {
            var x = xIzquierda + (i * uno);

            // El segundo va ESPEJEADO en las formas que tienen un lado: dos canales
            // enfrentadas forman un cajón y dos ángulos una cruz, que es como se arman.
            // En las simétricas el espejo no cambia nada, así que da igual.
            var espejo = i == 1;

            switch (p.Forma)
            {
                case FormaAcero.I:
                    PerfilI(x + (uno / 2), yAbajo, h, b, t, tf, p);
                    break;

                case FormaAcero.Te:
                    PerfilTe(x + (uno / 2), yAbajo, h, b, t, tf, p);
                    break;

                case FormaAcero.Canal:
                    PerfilCanal(x, yAbajo, h, b, t, tf, espejo, p);
                    break;

                case FormaAcero.CanalConLabios:
                    PerfilCf(x, yAbajo, h, b, t, labio, radio, espejo, p);
                    break;

                case FormaAcero.Zeta:
                    PerfilZeta(x, yAbajo, h, b, bMenor, t, radio, espejo, p);
                    break;

                case FormaAcero.Angulo:
                    PerfilAngulo(x, yAbajo, h, b, t, espejo, p);
                    break;

                case FormaAcero.TuboRectangular:
                    PerfilOr(x + (uno / 2), yAbajo, uno, alto, t, p);
                    break;

                case FormaAcero.TuboRedondo:
                    PerfilOc(x + (h / 2), yAbajo + (h / 2), h / 2, (h / 2) - t, p);
                    break;

                case FormaAcero.RedondoMacizo:
                    PerfilOs(x + (h / 2), yAbajo + (h / 2), h / 2, p);
                    break;
            }
        }

        // Las cotas van APARTE del trazo y se dibujan UNA VEZ para el conjunto, no una por
        // perfil: en un doble, acotar dos veces el mismo peralte solo ensucia.
        switch (p.Forma)
        {
            case FormaAcero.I:
                CotasI(xIzquierda, yAbajo, h, b, t, tf, p.Doble, false);
                break;

            case FormaAcero.Te:
                CotasI(xIzquierda, yAbajo, h, b, t, tf, p.Doble, true);
                break;

            case FormaAcero.Canal:
                CotasCanal(xIzquierda, yAbajo, h, b, t, tf, p.Doble);
                break;

            case FormaAcero.CanalConLabios:
                CotasCf(xIzquierda, yAbajo, h, b, t, labio, p.Doble);
                break;

            case FormaAcero.Zeta:
                CotasZeta(xIzquierda, yAbajo, h, b, bMenor, t, p.Doble);
                break;

            case FormaAcero.Angulo:
                CotasAngulo(xIzquierda, yAbajo, h, b, t, p.Doble);
                break;

            case FormaAcero.TuboRectangular:
                CotasOr(xIzquierda, yAbajo, uno, alto, t, p.Doble);
                break;

            case FormaAcero.TuboRedondo:
                CotasOc(xIzquierda, yAbajo, h, p.EspesorCm, p.Doble);
                break;

            case FormaAcero.RedondoMacizo:
                CotasOs(xIzquierda, yAbajo, h, p.PeralteCm, p.Doble);
                break;
        }

        RotuloAcero(p, centro, yAbajo - _gapAcero);

        var fin = (int)_ms.Count;

        Bloquear(p.Id, inicio, fin, destino);

        return fin - inicio;
    }

    // ==================================================================
    //  Forma I: el perfil laminado de alma y dos patines (IR, IS, IC, S)
    // ==================================================================

    /// <summary>
    /// El contorno del perfil I, de doce vértices, con su rayado.
    /// </summary>
    /// <remarks>
    /// Port de <c>DibujarPerfilW</c>. Los doce puntos van en el mismo orden que la macro,
    /// empezando por el patín inferior derecho y girando en sentido antihorario. No lleva
    /// curvas de acuerdo entre alma y patín: la macro tampoco, y a la escala de un plano
    /// estructural no se distinguirían.
    /// </remarks>
    private void PerfilI(
        double cx, double cy, double d, double bf, double tw, double tf,
        PerfilAceroCad p)
    {
        var pts = new[]
        {
            cx + (bf / 2), cy,
            cx + (bf / 2), cy + tf,
            cx + (tw / 2), cy + tf,
            cx + (tw / 2), cy + d - tf,
            cx + (bf / 2), cy + d - tf,
            cx + (bf / 2), cy + d,
            cx - (bf / 2), cy + d,
            cx - (bf / 2), cy + d - tf,
            cx - (tw / 2), cy + d - tf,
            cx - (tw / 2), cy + tf,
            cx - (bf / 2), cy + tf,
            cx - (bf / 2), cy
        };

        TrazarPerfil(pts, p, "perfil I");
    }

    /// <summary>
    /// El contorno de la te: un patín arriba y el alma colgando.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No tiene macro: es una de las cinco formas nuevas. Son ocho vértices, y la te se
    /// dibuja <b>con el patín arriba</b>, que es la posición en la que se usa como cuerda
    /// superior de armadura y como la mitad de un perfil I partido, que es de donde sale.
    /// </para>
    /// <para>
    /// El peralte es el <b>total</b>, del canto del patín a la punta del alma, que es la
    /// columna <c>d</c> del manual y no la <c>h</c>, que solo mide el alma libre.
    /// </para>
    /// </remarks>
    private void PerfilTe(
        double cx, double cy, double d, double bf, double tw, double tf,
        PerfilAceroCad p)
    {
        var pts = new[]
        {
            cx + (tw / 2), cy,
            cx + (tw / 2), cy + d - tf,
            cx + (bf / 2), cy + d - tf,
            cx + (bf / 2), cy + d,
            cx - (bf / 2), cy + d,
            cx - (bf / 2), cy + d - tf,
            cx - (tw / 2), cy + d - tf,
            cx - (tw / 2), cy
        };

        TrazarPerfil(pts, p, "te");
    }

    /// <summary>
    /// Las cotas de la forma I y de la te, que son las mismas cuatro.
    /// </summary>
    /// <remarks>
    /// La única diferencia es dónde va la cota del espesor del alma: en el perfil I a media
    /// altura, donde el alma está sola; en la te, más abajo, porque a media altura de una te
    /// el alma también está sola pero el patín ya no estorba y la cota se lee mejor pegada a
    /// la punta.
    /// </remarks>
    private void CotasI(
        double xIzq, double cy, double d, double bf, double tw, double tf,
        bool doble, bool esTe)
    {
        var gap = _gapAcero;
        var cx = xIzq + (bf / 2);
        var cxUlt = doble ? cx + bf : cx;

        // Ancho del patín, arriba de cada perfil
        CotaAcero(cx - (bf / 2), cy + d, cx + (bf / 2), cy + d, cx, cy + d + gap);

        if (doble)
        {
            var cx2 = cx + bf;

            CotaAcero(cx2 - (bf / 2), cy + d, cx2 + (bf / 2), cy + d, cx2, cy + d + gap);

            // Y el ancho TOTAL de los dos, más arriba
            CotaAcero(
                cx - (bf / 2), cy + d, cx2 + (bf / 2), cy + d,
                cx + (bf / 2), cy + d + gap + _textoCotaAcero + _flechaAcero);
        }

        // Peralte, a la derecha del último
        CotaAcero(
            cxUlt + (bf / 2), cy, cxUlt + (bf / 2), cy + d,
            cxUlt + (bf / 2) + gap, cy + (d / 2));

        // Espesor del patín, a la izquierda
        CotaAcero(
            cx - (bf / 2), cy + d, cx - (bf / 2), cy + d - tf,
            cx - (bf / 2) - gap, cy + d - (tf / 2));

        // Espesor del alma
        var yAlma = esTe ? cy + ((d - tf) / 4) : cy + (d / 2);

        CotaAcero(cx - (tw / 2), yAlma, cx + (tw / 2), yAlma, cx, yAlma - gap);

        if (doble)
        {
            var cx2 = cx + bf;
            CotaAcero(cx2 - (tw / 2), yAlma, cx2 + (tw / 2), yAlma, cx2, yAlma - gap);
        }
    }

    // ==================================================================
    //  Canal laminada: la C, sin labios
    // ==================================================================

    /// <summary>
    /// La canal estándar: alma a un lado y dos patines, <b>sin labios</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No tiene macro, y no se puede dibujar con la de la CF aunque las dos se llamen
    /// «canal»: la CF es lámina doblada de espesor único, con labios y radios de doblez,
    /// mientras que la C es laminada, con el alma y los patines de <b>distinto espesor</b> y
    /// sin nada doblado. Son ocho vértices en pico.
    /// </para>
    /// <para>
    /// Los patines se dibujan de espesor constante. El manual da un solo <c>tf</c> por
    /// perfil, que es el espesor medio: la cuña real del patín laminado no está en los datos
    /// y a escala de plano no se vería.
    /// </para>
    /// </remarks>
    private void PerfilCanal(
        double xIzq, double y0, double d, double bf, double tw, double tf, bool espejo,
        PerfilAceroCad p)
    {
        var ancho = bf;
        var s = espejo ? -1.0 : 1.0;

        // Con espejo el alma se va al otro extremo del hueco, para que dos canales queden
        // enfrentadas formando un cajón.
        var xAlma = espejo ? xIzq + ancho : xIzq;

        var pts = new[]
        {
            xAlma, y0,
            xAlma, y0 + d,
            xAlma + (s * bf), y0 + d,
            xAlma + (s * bf), y0 + d - tf,
            xAlma + (s * tw), y0 + d - tf,
            xAlma + (s * tw), y0 + tf,
            xAlma + (s * bf), y0 + tf,
            xAlma + (s * bf), y0
        };

        TrazarPerfil(pts, p, "canal laminada");
    }

    private void CotasCanal(
        double xIzq, double y0, double d, double bf, double tw, double tf, bool doble)
    {
        var gap = _gapAcero;
        var anchoTotal = doble ? 2 * bf : bf;

        // Peralte, a la derecha de todo
        CotaAcero(
            xIzq + anchoTotal, y0, xIzq + anchoTotal, y0 + d,
            xIzq + anchoTotal + gap, y0 + (d / 2));

        // Ancho del patín, arriba del primero
        CotaAcero(xIzq, y0 + d, xIzq + bf, y0 + d, xIzq + (bf / 2), y0 + d + gap);

        if (doble)
        {
            CotaAcero(
                xIzq, y0 + d, xIzq + anchoTotal, y0 + d,
                xIzq + (anchoTotal / 2), y0 + d + gap + _textoCotaAcero + _flechaAcero);
        }

        // Espesor del patín, arriba a la izquierda. Se mide en la cara del alma, donde el
        // patín tiene su espesor completo.
        CotaAcero(
            xIzq, y0 + d, xIzq, y0 + d - tf,
            xIzq - gap, y0 + d - (tf / 2));

        // Espesor del alma, con el texto DENTRO DEL HUECO de la canal.
        //
        // NINGUNA COTA PUEDE LLEVAR SU TEXTO POR DEBAJO DE LA BASE, y ese es el motivo de
        // esta posición: debajo de la base va el rótulo —cuatro renglones y hasta un metro
        // de ancho, centrado— así que un número ahí acaba encima de él. El hueco entre los
        // dos patines, en cambio, está vacío por definición y es donde se pondría a mano.
        CotaAcero(
            xIzq, y0 + (d / 2), xIzq + tw, y0 + (d / 2),
            xIzq + tw + gap, y0 + (d / 2));
    }

    // ==================================================================
    //  Ángulo: la L, de alas iguales o desiguales
    // ==================================================================

    /// <summary>
    /// El ángulo: dos alas en escuadra del mismo espesor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No tiene macro. Son seis vértices, con el <b>talón abajo a la izquierda</b> y las dos
    /// alas subiendo y saliendo de ahí, que es como se dibuja un ángulo en un plano.
    /// </para>
    /// <para>
    /// <b>Va en pico, sin el acuerdo del talón ni las puntas redondeadas</b>, y esto no es
    /// una simplificación gratuita: el manual IMCA <b>no da ningún radio para el ángulo</b>.
    /// Las 144 filas de la familia L tienen todas las columnas de geometría en «-» —ni
    /// peralte, ni espesor, ni radio— y las medidas están únicamente en la designación. Con
    /// las alas y el espesor leídos del nombre se dibuja lo que la hoja sabe; inventar un
    /// radio sería dibujar un dato que nadie dio.
    /// </para>
    /// </remarks>
    private void PerfilAngulo(
        double xIzq, double y0, double alaLarga, double alaCorta, double t, bool espejo,
        PerfilAceroCad p)
    {
        var s = espejo ? -1.0 : 1.0;

        // El talón se va al otro extremo del hueco al espejear, para que dos ángulos
        // queden espalda contra espalda, que es como se arma un doble ángulo.
        var xTalon = espejo ? xIzq + alaCorta : xIzq;

        var pts = new[]
        {
            xTalon, y0,
            xTalon + (s * alaCorta), y0,
            xTalon + (s * alaCorta), y0 + t,
            xTalon + (s * t), y0 + t,
            xTalon + (s * t), y0 + alaLarga,
            xTalon, y0 + alaLarga
        };

        TrazarPerfil(pts, p, "ángulo");
    }

    private void CotasAngulo(
        double xIzq, double y0, double alaLarga, double alaCorta, double t, bool doble)
    {
        var gap = _gapAcero;
        var anchoTotal = doble ? 2 * alaCorta : alaCorta;

        // Ala larga, a la derecha de todo
        CotaAcero(
            xIzq + anchoTotal, y0, xIzq + anchoTotal, y0 + alaLarga,
            xIzq + anchoTotal + gap, y0 + (alaLarga / 2));

        // Ala corta, con el texto DENTRO DE LA ESCUADRA.
        //
        // No va debajo de la base porque ahí va el rótulo: cuatro renglones centrados y de
        // hasta un metro de ancho, así que un número debajo de un ángulo de 7 cm cae justo
        // encima de su primer renglón. El hueco de la escuadra está vacío y es donde se
        // pondría a mano.
        CotaAcero(
            xIzq, y0, xIzq + alaCorta, y0,
            xIzq + (alaCorta / 2), y0 + t + gap);

        // Espesor, arriba en el ala vertical. Es el mismo en las dos alas, así que se acota
        // una sola vez: acotarlo dos veces diría que pueden ser distintos, y no pueden.
        CotaAcero(
            xIzq, y0 + alaLarga, xIzq + t, y0 + alaLarga,
            xIzq + (t / 2), y0 + alaLarga + gap);
    }

    // ==================================================================
    //  OR: el tubo rectangular
    // ==================================================================

    /// <summary>
    /// Los dos rectángulos redondeados del tubo, con el rayado que le toca al peralte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Port de <c>DibujarPerfilHSS</c> y <c>AplicarHatchHSS</c>. Los radios no se capturan:
    /// el exterior es el propio espesor y el interior su mitad, como en la macro, y los dos
    /// se recortan si no caben.
    /// </para>
    /// </remarks>
    private void PerfilOr(
        double cx, double cy, double bHss, double hHss, double tHss, PerfilAceroCad p)
    {
        var rOut = Math.Min(tHss, Math.Min(bHss, hHss) / 2);

        var x0 = cx - (bHss / 2);
        var x1 = cx + (bHss / 2);
        var y0 = cy;
        var y1 = cy + hHss;

        var exterior = RectanguloRedondeado(x0, y0, x1, y1, rOut);

        if (exterior is null)
        {
            Nota("Perfil OR: no se pudo crear el contorno exterior.");
            return;
        }

        // El tubo NO lleva ancho constante: su macro no hace PEDIT, solo la del IR lo hace.
        // Ver PeditDeLaForma.

        var bInt = bHss - (2 * tHss);
        var hInt = hHss - (2 * tHss);

        object? interior = null;

        if (bInt > 0 && hInt > 0)
        {
            var rIn = Math.Min(tHss / 2, Math.Min(bInt, hInt) / 2);

            interior = RectanguloRedondeado(
                x0 + tHss, y0 + tHss, x1 - tHss, y1 - tHss, rIn);
        }

        var islas = interior is null ? null : new List<object> { interior };

        RayarPerfil(exterior, islas, p);
    }

    private void CotasOr(
        double xIzq, double cy, double bHss, double hHss, double tHss, bool doble)
    {
        var gap = _gapAcero;
        var cx = xIzq + (bHss / 2);
        var cxUlt = doble ? cx + bHss : cx;

        CotaAcero(cx - (bHss / 2), cy + hHss, cx + (bHss / 2), cy + hHss, cx, cy + hHss + gap);

        if (doble)
        {
            var cx2 = cx + bHss;

            CotaAcero(
                cx2 - (bHss / 2), cy + hHss, cx2 + (bHss / 2), cy + hHss,
                cx2, cy + hHss + gap);

            CotaAcero(
                cx - (bHss / 2), cy + hHss, cx2 + (bHss / 2), cy + hHss,
                cx + (bHss / 2), cy + hHss + gap + _textoCotaAcero + _flechaAcero);

            CotaAcero(
                cx2 - (bHss / 2), cy + hHss, cx2 - (bHss / 2), cy + hHss - tHss,
                cx2 - (bHss / 2) - gap, cy + hHss - (tHss / 2));
        }

        CotaAcero(
            cxUlt + (bHss / 2), cy, cxUlt + (bHss / 2), cy + hHss,
            cxUlt + (bHss / 2) + gap, cy + (hHss / 2));

        CotaAcero(
            cx - (bHss / 2), cy + hHss, cx - (bHss / 2), cy + hHss - tHss,
            cx - (bHss / 2) - gap, cy + hHss - (tHss / 2));
    }

    // ==================================================================
    //  OC y OS: el tubo redondo y el redondo macizo
    // ==================================================================

    /// <summary>Las dos circunferencias del tubo redondo, con su corona rayada.</summary>
    /// <remarks>
    /// <para>
    /// Port de <c>DibujarPerfilOC</c> y <c>AplicarHatchOC</c>. Si el espesor se come el
    /// radio, la circunferencia interior no se dibuja y el tubo sale macizo, que es lo que
    /// hace la macro cuando <c>radioInt</c> queda en cero.
    /// </para>
    /// <para>
    /// <b>El rayado de esta macro es invisible</b>, y se conserva así: rellena con
    /// <c>SOLID</c> en el color 162 y raya con <c>ANSI31</c> <b>también</b> en 162, así que
    /// las líneas del rayado no se distinguen del fondo y el tubo sale como un anillo liso.
    /// Está apuntado en <c>docs/macros-acero.md</c>: es una decisión de dibujo y con cambiar
    /// el color de las líneas se arregla, pero no se toca por cuenta propia.
    /// </para>
    /// </remarks>
    private void PerfilOc(
        double cx, double cy, double rExt, double rInt, PerfilAceroCad p)
    {
        var exterior = Circulo(cx, cy, rExt);

        if (exterior is null)
        {
            Nota("Perfil OC: no se pudo crear la circunferencia exterior.");
            return;
        }

        var interior = rInt > 0 ? Circulo(cx, cy, rInt) : null;
        var islas = interior is null ? null : new List<object> { interior };

        RayarPerfil(exterior, islas, p);
    }

    private void CotasOc(double xIzq, double yAbajo, double d, double espesorCm, bool doble)
    {
        var gap = _gapAcero;
        var rExt = d / 2;
        var cx = xIzq + rExt;
        var cy = yAbajo + rExt;
        var cxUlt = doble ? cx + d : cx;

        CotaAcero(cx - rExt, cy + rExt, cx + rExt, cy + rExt, cx, cy + rExt + gap);

        if (doble)
        {
            var cx2 = cx + d;

            CotaAcero(
                cx - rExt, cy + rExt, cx2 + rExt, cy + rExt,
                (cx + cx2) / 2, cy + rExt + gap + _textoCotaAcero + _flechaAcero);
        }

        // El espesor va como TEXTO y no como cota: en un tubo redondo la pared no tiene
        // dos caras paralelas que acotar de frente, así que la macro escribe «e=…».
        TextoAcero(
            $"e={espesorCm:0.000} cm",
            cxUlt + rExt + gap, cy,
            _textoCotaAcero,
            "COTAS");
    }

    /// <summary>La circunferencia del redondo macizo.</summary>
    /// <remarks>
    /// No tiene macro. Es la forma más simple de las nueve —una circunferencia rellena— y la
    /// que faltaba para poder dibujar los tensores y las contravientos, que se piden como
    /// varilla lisa y hasta ahora había que capturar como un tubo de pared falsa.
    /// </remarks>
    private void PerfilOs(double cx, double cy, double r, PerfilAceroCad p)
    {
        var contorno = Circulo(cx, cy, r);

        if (contorno is null)
        {
            Nota("Perfil OS: no se pudo crear la circunferencia.");
            return;
        }

        RayarPerfil(contorno, null, p);
    }

    /// <summary>
    /// La medida del redondo macizo, que va como <b>texto y no como cota</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la única forma que no lleva ni una sola cota, y es a propósito. Un redondo macizo
    /// tiene <b>una</b> dimensión, el diámetro, y el catálogo del IMCA va del de 1/4" al de
    /// 4": para casi todos ellos el aparato de una cota —dos líneas de extensión, dos flechas
    /// y el número en medio— mide más que la varilla que pretende medir, y el resultado es un
    /// nudo de líneas del que no se lee nada. Escrito al lado con el símbolo de diámetro se
    /// lee a cualquier tamaño y no tapa el dibujo.
    /// </para>
    /// <para>
    /// El <c>%%C</c> es el código de AutoCAD para el símbolo Ø: se escribe así, y no con el
    /// carácter, porque el símbolo depende de que la fuente lo tenga y el código lo resuelve
    /// AutoCAD.
    /// </para>
    /// </remarks>
    private void CotasOs(double xIzq, double yAbajo, double d, double diametroCm, bool doble)
    {
        var gap = _gapAcero;
        var r = d / 2;
        var cy = yAbajo + r;
        var xDerecha = xIzq + (doble ? 2 * d : d);

        TextoAcero(
            $"%%C{diametroCm:0.00} cm",
            xDerecha + gap, cy,
            _textoCotaAcero,
            "COTAS");
    }

    // ==================================================================
    //  CF: la canal formada en frío, con labios
    // ==================================================================

    /// <summary>
    /// La canal con labios, sus radios de doblez y su rayado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Port de <c>CrearCFReal</c> y <c>CrearPolilineaHatchCF</c>. El radio exterior es el
    /// capturado y el interior su mitad, cada uno recortado a lo que cabe, igual que la
    /// macro: en el exterior manda el menor entre medio ancho, el labio y medio peralte, y
    /// en el interior lo mismo descontando el espesor.
    /// </para>
    /// <para>
    /// <b>Aquí se dibuja UNA polilínea donde la macro dibuja dos cosas.</b> La macro traza
    /// el contorno como veinticuatro líneas y arcos sueltos, los une con <c>JoinEntities</c>
    /// y además construye una segunda polilínea con bulges para el hatch: dos entidades con
    /// exactamente la misma geometría, una encima de la otra. Con los bulges basta una, que
    /// hace de contorno y de frontera del rayado.
    /// </para>
    /// </remarks>
    private void PerfilCf(
        double xWeb, double y0, double h, double b, double t, double lip, double ri,
        bool espejo, PerfilAceroCad p)
    {
        var s = espejo ? -1.0 : 1.0;

        if (lip <= t) { lip = t + (0.1 * Cm); }
        if (b <= 2 * t) { b = (2 * t) + (0.1 * Cm); }
        if (h <= 2 * t) { h = (2 * t) + (0.1 * Cm); }
        if (ri < 0) { ri = 0; }

        // Con espejo el alma se va al otro extremo del hueco, para que dos canales queden
        // enfrentadas formando un cajón, que es como se arman.
        if (espejo) { xWeb += b; }

        // Radio EXTERIOR: el capturado, recortado a lo que cabe.
        var rExt = Math.Min(ri, Math.Min(b / 2, Math.Min(lip, h / 2)));
        if (rExt < 0) { rExt = 0; }

        // Radio INTERIOR: la mitad, recortada por su cuenta. No es rExt - t: la macro lo
        // fija en ri/2, y con eso el doblez interior sale más cerrado que el exterior.
        var rIntMax = Math.Min((b - t) / 2, Math.Min((h - (2 * t)) / 2, lip - t));
        var rInt = Math.Min(ri / 2, rIntMax);
        if (rInt < 0) { rInt = 0; }

        var xWebOut = xWeb;
        var xWebIn = xWeb + (s * t);
        var xFlangeOut = xWeb + (s * b);
        var xFlangeIn = xFlangeOut - (s * t);
        var yb = y0;
        var yt = y0 + h;

        if (rExt <= 0 && rInt <= 0)
        {
            // Sin radios: doce vértices en pico, el caso que la macro dibuja con líneas.
            TrazarPerfil(
                new[]
                {
                    xWebOut, yb,
                    xWebOut, yt,
                    xFlangeOut, yt,
                    xFlangeOut, yt - lip,
                    xFlangeIn, yt - lip,
                    xFlangeIn, yt - t,
                    xWebIn, yt - t,
                    xWebIn, yb + t,
                    xFlangeIn, yb + t,
                    xFlangeIn, yb + lip,
                    xFlangeOut, yb + lip,
                    xFlangeOut, yb
                },
                p, "canal con labios");

            return;
        }

        var pts = new[]
        {
            xWebOut, yb + rExt,
            xWebOut, yt - rExt,
            xWebOut + (s * rExt), yt,
            xFlangeOut - (s * rExt), yt,
            xFlangeOut, yt - rExt,
            xFlangeOut, yt - lip,
            xFlangeIn, yt - lip,
            xFlangeIn, yt - t - rInt,
            xFlangeIn - (s * rInt), yt - t,
            xWebIn + (s * rInt), yt - t,
            xWebIn, yt - t - rInt,
            xWebIn, yb + t + rInt,
            xWebIn + (s * rInt), yb + t,
            xFlangeIn - (s * rInt), yb + t,
            xFlangeIn, yb + t + rInt,
            xFlangeIn, yb + lip,
            xFlangeOut, yb + lip,
            xFlangeOut, yb + rExt,
            xFlangeOut - (s * rExt), yb,
            xWebOut + (s * rExt), yb
        };

        // Los ocho dobleces, cada uno con su centro. El bulge sale del barrido real
        // entre los dos vértices vistos desde el centro, así que el espejo se resuelve
        // solo: al invertir s, los barridos cambian de signo y los arcos también.
        var bulges = new (int Indice, double Cx, double Cy, int A, int B)[]
        {
            (1, xWebOut + (s * rExt), yt - rExt, 1, 2),
            (3, xFlangeOut - (s * rExt), yt - rExt, 3, 4),
            (7, xFlangeIn - (s * rInt), yt - t - rInt, 7, 8),
            (9, xWebIn + (s * rInt), yt - t - rInt, 9, 10),
            (11, xWebIn + (s * rInt), yb + t + rInt, 11, 12),
            (13, xFlangeIn - (s * rInt), yb + t + rInt, 13, 14),
            (17, xFlangeOut - (s * rExt), yb + rExt, 17, 18),
            (19, xWebOut + (s * rExt), yb + rExt, 19, 0)
        };

        TrazarPerfilConDobleces(pts, bulges, p, "canal con labios");
    }

    private void CotasCf(
        double xIzq, double yBase, double h, double b, double t, double lip, bool doble)
    {
        var gap = _gapAcero;
        var anchoTotal = doble ? 2 * b : b;

        // Peralte, a la derecha de todo
        CotaAcero(
            xIzq + anchoTotal, yBase, xIzq + anchoTotal, yBase + h,
            xIzq + anchoTotal + gap, yBase + (h / 2));

        // Ancho del patín, arriba
        CotaAcero(
            xIzq, yBase + h, xIzq + b, yBase + h,
            xIzq + (b / 2), yBase + h + gap);

        // Largo del labio, medido en la cara del alma y con el texto a su izquierda. Es la
        // colocación de la macro, que acotaba el labio sobre una vertical junto al alma y
        // dejaba el número fuera del perfil por ese lado; lo único que cambia es que los
        // desplazamientos ya no son 5 y 12 cm fijos, sino proporcionales al perfil.
        CotaAcero(
            xIzq, yBase + h - lip, xIzq, yBase + h,
            xIzq - gap, yBase + h - (lip / 2));

        // Espesor de la lámina, medido en el patín inferior y con el texto abajo a la
        // izquierda. NO va debajo de la base: ahí va el rótulo.
        CotaAcero(
            xIzq, yBase, xIzq, yBase + t,
            xIzq - gap, yBase + (t / 2));
    }

    // ==================================================================
    //  ZF: la zeta formada en frío
    // ==================================================================

    /// <summary>
    /// La zeta: el alma vertical con un patín a cada lado, y sus cuatro dobleces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No tiene macro. Los <b>dos patines son de distinto ancho</b> —60.3 y 54 mm en la de
    /// 2 3/8"— y eso no es una errata del manual: es lo que permite traslapar dos zetas en el
    /// apoyo, porque el patín angosto de una entra dentro del ancho de la otra. Dibujarla
    /// simétrica sería dibujar una zeta que no se fabrica. El ancho angosto se puede dejar en
    /// cero, y entonces sí sale simétrica, que es lo que hay que hacer con una zeta de
    /// fabricación propia.
    /// </para>
    /// <para>
    /// El patín <b>ancho va arriba</b> y el angosto abajo, que es la posición de montaje: la
    /// zeta se apoya por el patín angosto y el ancho recibe la lámina de cubierta.
    /// </para>
    /// <para>
    /// Lleva <b>cuatro dobleces</b>, dos exteriores y dos interiores, con la misma regla que
    /// la CF: el radio capturado por fuera y su mitad por dentro. Los radios reales de una
    /// zeta son de 4.76 mm, así que a escala de plano apenas se ven, pero sin ellos las
    /// esquinas salen en pico y una lámina doblada en pico no existe.
    /// </para>
    /// </remarks>
    private void PerfilZeta(
        double xIzq, double y0, double h, double bAncho, double bAngosto, double t,
        double ri, bool espejo, PerfilAceroCad p)
    {
        if (t <= 0) { t = 0.1 * Cm; }
        if (bAngosto <= t) { bAngosto = bAncho; }
        if (h <= 2 * t) { h = (2 * t) + (0.1 * Cm); }
        if (ri < 0) { ri = 0; }

        // El ancho total: los dos patines menos el espesor del alma, que comparten.
        var w = bAncho + bAngosto - t;

        // Radio EXTERIOR: el capturado, recortado por el medio patín más corto y por el
        // medio alma libre.
        var rExt = Math.Min(ri, Math.Min(Math.Min(bAncho, bAngosto) / 2, (h - (2 * t)) / 2));
        if (rExt < 0) { rExt = 0; }

        // Radio INTERIOR: el exterior MENOS EL ESPESOR, y esto no es una convención sino
        // geometría. Una zeta es una lámina de espesor único doblada dos veces, así que en
        // cada doblez la cara de dentro y la de fuera son dos arcos CONCÉNTRICOS separados
        // exactamente el espesor. Con cualquier otra relación la lámina saldría más gorda o
        // más delgada en la esquina que en el tramo recto.
        //
        // La CF, que es un port de macro, usa en cambio la mitad del radio capturado, y ahí
        // se respeta lo que hace el original. Aquí no hay original al que ser fiel, así que
        // se dibuja lo que es. Si el radio no llega ni al espesor, el doblez interior sale
        // en pico, que es lo que pasa de verdad en una esquina prensada.
        var rInt = Math.Max(0, rExt - t);

        // Sin espejo: el patín ancho sale a la DERECHA por arriba y el angosto a la
        // IZQUIERDA por abajo. Espejeada, al contrario.
        double X(double x) => espejo ? (2 * xIzq) + w - x : x;

        var xAlmaIzq = xIzq + bAngosto - t;   // cara izquierda del alma
        var xAlmaDer = xIzq + bAngosto;       // cara derecha del alma
        var xTope = xAlmaIzq + bAncho;        // punta del patín ancho
        var yt = y0 + h;

        if (rExt <= 0 && rInt <= 0)
        {
            TrazarPerfil(
                new[]
                {
                    X(xIzq), y0,
                    X(xAlmaDer), y0,
                    X(xAlmaDer), yt - t,
                    X(xTope), yt - t,
                    X(xTope), yt,
                    X(xAlmaIzq), yt,
                    X(xAlmaIzq), y0 + t,
                    X(xIzq), y0 + t
                },
                p, "zeta");

            return;
        }

        // Los cuatro dobleces, y OJO CON LOS DOS INTERIORES: sus centros caen FUERA del
        // acero, en el hueco de la esquina, y a distinto lado del alma cada uno. El de
        // arriba se sale por la derecha del alma —a xAlmaDer + rInt— y el de abajo por la
        // IZQUIERDA —a xAlmaIzq − rInt—, porque los dos patines salen a lados contrarios.
        // Con los dos al mismo lado, el contorno de abajo se devolvía sobre sí mismo y la
        // polilínea se cruzaba: el rayado salía por fuera del perfil.
        var pts = new[]
        {
            X(xIzq), y0,
            X(xAlmaDer - rExt), y0,
            X(xAlmaDer), y0 + rExt,
            X(xAlmaDer), yt - t - rInt,
            X(xAlmaDer + rInt), yt - t,
            X(xTope), yt - t,
            X(xTope), yt,
            X(xAlmaIzq + rExt), yt,
            X(xAlmaIzq), yt - rExt,
            X(xAlmaIzq), y0 + t + rInt,
            X(xAlmaIzq - rInt), y0 + t,
            X(xIzq), y0 + t
        };

        // Los centros también se espejean, y el bulge sale del barrido visto desde el
        // centro, así que el signo se resuelve solo.
        var bulges = new (int Indice, double Cx, double Cy, int A, int B)[]
        {
            (1, X(xAlmaDer - rExt), y0 + rExt, 1, 2),
            (3, X(xAlmaDer + rInt), yt - t - rInt, 3, 4),
            (7, X(xAlmaIzq + rExt), yt - rExt, 7, 8),
            (9, X(xAlmaIzq - rInt), y0 + t + rInt, 9, 10)
        };

        TrazarPerfilConDobleces(pts, bulges, p, "zeta");
    }

    private void CotasZeta(
        double xIzq, double y0, double h, double bAncho, double bAngosto, double t,
        bool doble)
    {
        var gap = _gapAcero;
        var w = bAncho + bAngosto - t;
        var anchoTotal = doble ? 2 * w : w;
        var xAlmaIzq = xIzq + bAngosto - t;

        // Peralte, a la derecha de todo
        CotaAcero(
            xIzq + anchoTotal, y0, xIzq + anchoTotal, y0 + h,
            xIzq + anchoTotal + gap, y0 + (h / 2));

        // Patín ANCHO, arriba: va del alma a la punta
        CotaAcero(
            xAlmaIzq, y0 + h, xAlmaIzq + bAncho, y0 + h,
            xAlmaIzq + (bAncho / 2), y0 + h + gap);

        // Patín ANGOSTO. Los dos se acotan porque son distintos, y ver los dos números
        // juntos es lo que hace evidente que la zeta no es simétrica.
        //
        // El texto va ENCIMA del patín angosto, no debajo, porque debajo de la base va el
        // rótulo. Encima del patín angosto y a la izquierda del alma no hay nada: es el
        // hueco que deja la zeta por ese lado.
        CotaAcero(
            xIzq, y0, xIzq + bAngosto, y0,
            xIzq + (bAngosto / 2), y0 + t + gap);

        // Espesor de la lámina, medido en el patín ancho y con el texto arriba a la
        // izquierda, que es la otra zona vacía de la zeta.
        CotaAcero(
            xAlmaIzq, y0 + h, xAlmaIzq, y0 + h - t,
            xAlmaIzq - gap, y0 + h - (t / 2));
    }

    // ==================================================================
    //  Trazo y rayado, comunes a las nueve formas
    // ==================================================================

    /// <summary>
    /// Traza un contorno en pico y lo raya con lo que le toca a su forma.
    /// </summary>
    /// <remarks>
    /// Las nueve formas acaban aquí o en <see cref="TrazarPerfilConDobleces"/>: el trazo es
    /// el mismo para todas, y lo único que cambia de una a otra es el rayado, que decide
    /// <see cref="RayarPerfil"/>.
    /// </remarks>
    private void TrazarPerfil(double[] pts, PerfilAceroCad p, string queEs)
    {
        var pl = Polilinea(pts, CapaPerfiles);

        if (pl is null)
        {
            Nota($"Perfil de acero ({queEs}): no se pudo crear el contorno.");
            return;
        }

        PeditDeLaForma(pl, p);
        RayarPerfil(pl, null, p);
    }

    /// <summary>Lo mismo, con dobleces: la canal con labios y la zeta.</summary>
    private void TrazarPerfilConDobleces(
        double[] pts,
        (int Indice, double Cx, double Cy, int A, int B)[] dobleces,
        PerfilAceroCad p,
        string queEs)
    {
        var lista = new List<(int, double)>();

        foreach (var (indice, cx, cy, a, b) in dobleces)
        {
            lista.Add((indice, BulgeDesdeCentro(
                cx, cy,
                pts[2 * a], pts[(2 * a) + 1],
                pts[2 * b], pts[(2 * b) + 1])));
        }

        var pl = PolilineaConBulges(pts, lista, CapaPerfiles);

        if (pl is null)
        {
            Nota($"Perfil de acero ({queEs}): no se pudo crear el contorno.");
            return;
        }

        PeditDeLaForma(pl, p);
        RayarPerfil(pl, null, p);
    }

    /// <summary>
    /// El <c>PEDIT &gt; Width</c> del contorno, <b>el de la macro de cada forma</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// De las cuatro macros, <b>solo la del IR engruesa el contorno</b>: le pone 0.001 de
    /// ancho constante para que la sección se lea como acero y no como una línea de
    /// construcción. La del HSS, la del OC y la del CF lo dejan con línea fina.
    /// </para>
    /// <para>
    /// Aquí se conserva exactamente así, y las formas nuevas siguen a la macro cuyo material
    /// comparten: la te, la canal laminada y el ángulo son perfiles <b>laminados</b> como el
    /// IR y llevan su ancho; la zeta es lámina doblada como el CF y no lo lleva; los redondos
    /// van como el OC. Se probó a ponérselo a las nueve y se quitó: cambia el aspecto de las
    /// tres familias que ya se venían dibujando.
    /// </para>
    /// </remarks>
    private void PeditDeLaForma(object pl, PerfilAceroCad p)
    {
        if (p.Forma is FormaAcero.I or FormaAcero.Te or FormaAcero.Canal
            or FormaAcero.Angulo)
        {
            AnchoConstante(pl, 0.1 * Cm);
        }
    }

    /// <summary>
    /// El rayado de un perfil: <b>el de la macro que le corresponde a su forma</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los patrones, las escalas y los colores son los que dejaron las cuatro macros de
    /// acero, uno por uno. No hay un color por familia: se probó y se quitó, porque el plano
    /// dejaba de parecerse al que ya se venía haciendo. Lo que distingue una familia de otra
    /// en el dibujo es su rayado, que es como estaba.
    /// </para>
    /// <para>
    /// <b>Las cinco formas que no tenían macro toman la de su material</b>, que es la
    /// asignación que no inventa nada:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Forma</term><description>Rayado que usa</description></listheader>
    ///   <item><term>I, te, canal laminada, ángulo</term><description>el del <b>IR</b>:
    ///   <c>ANSI32</c> a 0.0009 en color 252. Las cuatro son perfil laminado.</description></item>
    ///   <item><term>canal con labios, zeta</term><description>el del <b>CF</b>: fondo sólido
    ///   4 y <c>ANSI31</c> a 0.0008 en 142. Las dos son lámina doblada en frío.</description></item>
    ///   <item><term>tubo redondo, redondo macizo</term><description>el del <b>OC</b>:
    ///   <c>SOLID</c> y <c>ANSI31</c>, los dos en 162.</description></item>
    ///   <item><term>tubo rectangular</term><description>el del <b>HSS</b>, con su corte de
    ///   las cinco pulgadas.</description></item>
    /// </list>
    /// <para>
    /// <b>Ojo con el del OC:</b> el relleno y el rayado van del mismo color 162, así que las
    /// líneas del rayado no se distinguen del fondo y el tubo se ve macizo. Es lo que hace la
    /// macro y se conserva; está apuntado en <c>docs/macros-acero.md</c> por si algún día se
    /// quiere cambiar el color de las líneas.
    /// </para>
    /// </remarks>
    private void RayarPerfil(object contorno, List<object>? islas, PerfilAceroCad p)
    {
        switch (p.Forma)
        {
            // Laminados: el rayado de la macro del IR.
            case FormaAcero.I:
            case FormaAcero.Te:
            case FormaAcero.Canal:
            case FormaAcero.Angulo:
                Hatch("ANSI32", 0.0009 * _f, contorno, islas, CapaPerfiles, 252);
                break;

            // Formados en frío: el de la macro del CF.
            case FormaAcero.CanalConLabios:
            case FormaAcero.Zeta:
                Hatch("SOLID", 1, contorno, islas, CapaPerfiles, 4);
                Hatch("ANSI31", 0.0008 * _f, contorno, islas, CapaPerfiles, 142);
                break;

            // Redondos: el de la macro del OC.
            case FormaAcero.TuboRedondo:
            case FormaAcero.RedondoMacizo:
                Hatch("SOLID", 1, contorno, islas, CapaPerfiles, 162);
                Hatch("ANSI31", 0.002 * _f, contorno, islas, CapaPerfiles, 162);
                break;

            // Tubo rectangular: el de la macro del HSS, con su corte de las 5 pulgadas.
            //
            // El peralte en pulgadas, con la misma tolerancia de la macro para que un 5"
            // nominal no caiga del lado equivocado por un redondeo. Los colores son los de
            // sus CONSTANTES (141 y 144); sus comentarios dicen 94 y 80, que es lo que tuvo
            // alguna vez, y manda la constante, que es lo que se ejecuta.
            case FormaAcero.TuboRectangular:
                var menorDe5 = p.PeralteCm / 2.54 < PeralteLimitePulg - 0.01;

                if (!menorDe5)
                {
                    Hatch("SOLID", 1, contorno, islas, CapaPerfiles, 141);
                }

                var trama = Hatch(
                    "ANSI31",
                    (menorDe5 ? 0.001 : 0.002) * _f,
                    contorno, islas, CapaPerfiles,
                    menorDe5 ? 142 : 144);

                if (menorDe5 && trama is not null)
                {
                    FondoDelHatch(trama, 4);
                }

                break;
        }
    }

    // ==================================================================
    //  Rótulo
    // ==================================================================

    /// <summary>
    /// Los cuatro renglones del rótulo, con la altura y el ancho de caja que le tocan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Port de los cuatro <c>AgregarRotulo…</c>, que dicen lo mismo con alturas distintas:
    /// 0.03 el IR, 0.02 el OC, 0.022 el CF y el OR 0.02 o 0.03 según si su primer número
    /// pasa de 6. <b>Esa última regla es la que se generalizó</b>, porque es la única de las
    /// cuatro que tiene un motivo: el rótulo se centra bajo el perfil, así que en un tubo de
    /// cuatro pulgadas un texto grande sobresale por los lados. Ahora la altura sale del
    /// peralte para las doce familias, y da los mismos números que las macros donde ellas
    /// los daban. Ver <see cref="PerfilAceroCad.AlturaRotuloCm"/>.
    /// </para>
    /// <para>
    /// El ancho de la caja <b>se calcula del renglón más largo</b> en lugar de ser un número
    /// fijo. Las macros lo dejaban en 0.7, salvo la del tubo redondo que lo subía a 2.5 «para
    /// que el renglón del perfil no se parta en dos»: era un parche a mano del mismo problema.
    /// Y con el catálogo del IMCA el problema es peor, porque hay nombres como
    /// «IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm» que en una caja de 0.7 se parten en tres.
    /// </para>
    /// <para>
    /// El salto de renglón va como <c>\P</c>, el de MText, no como salto de línea real: es
    /// lo que hacen tres de las cuatro macros y lo que ya usa el rótulo del concreto.
    /// </para>
    /// </remarks>
    private void RotuloAcero(PerfilAceroCad p, double xCentro, double yBase)
    {
        TextoAcero(
            string.Join("\\P", p.LineasRotulo),
            xCentro,
            yBase,
            p.AlturaRotuloCm * _escala,
            "ROTULOS",
            p.AnchoRotuloCm * _escala);
    }

    // ==================================================================
    //  Auxiliares de dibujo que solo usa el acero
    // ==================================================================

    /// <summary>Una cota entre dos puntos, con el texto donde se le diga.</summary>
    /// <remarks>
    /// <para>
    /// Usa <c>AddDimAligned</c>, como las macros de acero, y no el <c>AddDimRotated</c> del
    /// concreto: aquí hay cotas de espesor de pocos milímetros junto a cotas de peralte, y
    /// la alineada coloca el texto donde se le pide sin tener que decirle el ángulo.
    /// </para>
    /// <para>
    /// <b>El factor de escala lineal es imprescindible.</b> El dibujo está en metros —un
    /// peralte de 30 cm mide 0.30 unidades— así que sin él la cota diría «0.30» en un plano
    /// rotulado «Acot. cm». Las cuatro macros lo fijan en 100, que es exactamente
    /// <c>1/escala</c>: se calcula así para que siga valiendo si algún día se dibuja a otra
    /// escala.
    /// </para>
    /// <para>
    /// <b>Y el aparato de la cota se reajusta al tamaño del perfil</b>, encima de lo que deja
    /// <see cref="FormatearCota"/>, que trae las medidas del concreto. Ver
    /// <see cref="PrepararAcero"/>: sin esto, un ángulo de 1.9 cm salía con flechas de 2 cm.
    /// </para>
    /// </remarks>
    private void CotaAcero(
        double x1, double y1, double x2, double y2, double xTexto, double yTexto)
    {
        // El estilo activo se reaplica antes de cada cota, por lo mismo que en el
        // concreto: AddDimAligned copia el estado del documento en ese instante.
        ConfigurarCotas();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic cota = _ms.AddDimAligned(
                    new[] { x1, y1, 0d },
                    new[] { x2, y2, 0d },
                    new[] { xTexto, yTexto, 0d });

                FormatearCota((object)cota);

                // Lo que las cuatro macros le ponen a cada cota, y que FormatearCota no
                // sabe porque es del concreto: su propio estilo de texto y su altura.
                PropCota((object)cota, "TextStyle", EstiloTextoAcero);

                // El aparato, proporcional al perfil. Va DESPUÉS de FormatearCota porque
                // es quien tiene que mandar: FormatearCota deja los valores del concreto.
                PropCota((object)cota, "TextHeight", _textoCotaAcero);
                PropCota((object)cota, "ArrowheadSize", _flechaAcero);
                PropCota((object)cota, "ExtensionLineOffset", _extOffsetAcero);
                PropCota((object)cota, "ExtensionLineExtend", _extExtiendeAcero);
                PropCota((object)cota, "ExtLineFixedLen", _extExtiendeAcero);
                PropCota((object)cota, "TextGap", _textoCotaAcero / 3);

                PropCota((object)cota, "LinearScaleFactor", 1 / _escala);
                PropCota((object)cota, "TextRotation", 0d);

                cota.Update();
            });
        }
        catch (Exception ex)
        {
            // Sin una cota el dibujo sigue sirviendo.
            Fallo("Cota de perfil de acero", ex);
        }
    }

    /// <summary>Un MText centrado por arriba, como los rótulos de las macros.</summary>
    /// <param name="anchoCaja">
    /// Ancho de la caja del MText. Si no se dice, el 0.7 de las macros.
    /// </param>
    private void TextoAcero(
        string contenido, double x, double y, double altura, string capa,
        double anchoCaja = 0)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                var caja = anchoCaja > 0 ? anchoCaja : 70 * Cm;

                dynamic t = _ms.AddMText(new[] { x, y, 0d }, caja, contenido);
                t.Layer = capa;
                t.Color = PorCapa;
                t.StyleName = EstiloTexto;
                t.Height = altura;

                // 2 = arriba y centrado. Se vuelve a poner el punto de inserción porque
                // cambiar el AttachmentPoint mueve el texto, igual que en la macro.
                t.AttachmentPoint = 2;
                t.InsertionPoint = new[] { x, y, 0d };
                t.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo($"Texto de acero en la capa {capa}", ex);
        }
    }

    /// <summary>Un rectángulo con las cuatro esquinas redondeadas, en una polilínea.</summary>
    /// <remarks>
    /// Port de <c>CrearRectanguloRedondeado</c>. Con radio cero sale el rectángulo de cuatro
    /// vértices; con radio, ocho vértices y cuatro bulges de un cuarto de círculo.
    /// </remarks>
    private object? RectanguloRedondeado(
        double x0, double y0, double x1, double y1, double r)
    {
        if (x1 - x0 <= 0 || y1 - y0 <= 0)
        {
            return null;
        }

        if (r <= 1e-7)
        {
            return Polilinea(new[] { x0, y0, x1, y0, x1, y1, x0, y1 }, CapaPerfiles);
        }

        var pts = new[]
        {
            x0 + r, y0,
            x1 - r, y0,
            x1, y0 + r,
            x1, y1 - r,
            x1 - r, y1,
            x0 + r, y1,
            x0, y1 - r,
            x0, y0 + r
        };

        return PolilineaConBulges(
            pts,
            new List<(int, double)> { (1, Bulge90), (3, Bulge90), (5, Bulge90), (7, Bulge90) },
            CapaPerfiles);
    }

    /// <summary>Una polilínea cerrada con bulges en los vértices que se le digan.</summary>
    private object? PolilineaConBulges(
        double[] puntos, List<(int Indice, double Valor)> bulges, string capa)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(puntos);
                pl.Closed = true;
                pl.Layer = capa;

                foreach (var (indice, valor) in bulges)
                {
                    pl.SetBulge(indice, valor);
                }

                // El Update va DESPUÉS de los bulges y el color al final, que es el orden
                // que la macro dejó anotado como necesario para que el color agarre.
                pl.Update();
                pl.Color = PorCapa;

                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilínea con dobleces en la capa {capa}", ex);
            return null;
        }
    }

    /// <summary>El bulge de un arco visto desde su centro.</summary>
    /// <remarks>
    /// Port de <c>BulgeDesdeCentro</c>: es la tangente de la cuarta parte del barrido, con
    /// el barrido normalizado a media vuelta para cada lado. Así el signo sale solo y los
    /// arcos del perfil espejeado giran al revés sin tener que decírselo.
    /// </remarks>
    private static double BulgeDesdeCentro(
        double cx, double cy, double xa, double ya, double xb, double yb)
    {
        var aa = Math.Atan2(ya - cy, xa - cx);
        var ab = Math.Atan2(yb - cy, xb - cx);

        var barrido = ab - aa;

        while (barrido > Pi)
        {
            barrido -= 2 * Pi;
        }

        while (barrido <= -Pi)
        {
            barrido += 2 * Pi;
        }

        return Math.Tan(barrido / 4);
    }

    /// <summary>Una circunferencia, en la capa de la familia que se dibuja.</summary>
    private object? Circulo(double cx, double cy, double radio)
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
                c.Layer = CapaPerfiles;
                c.Color = PorCapa;
                return (object?)c;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Circunferencia de radio {radio:0.###} en la capa {CapaPerfiles}", ex);
            return null;
        }
    }

    /// <summary>El ancho constante de la polilínea, el <c>PEDIT &gt; Width</c> de la macro.</summary>
    private void AnchoConstante(object pl, double ancho)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic p = pl;
                p.ConstantWidth = ancho;

                // -1 es ByLayer: el grosor de trazo lo manda la capa, como en la macro.
                p.LineWeight = -1;
                p.Update();
            });
        }
        catch (Exception)
        {
            // El ancho es cosmético: si esta versión de AutoCAD no lo acepta, el perfil
            // sale con línea fina y no se pierde información.
        }
    }

    /// <summary>
    /// El color de <b>fondo</b> de un rayado, que en AutoCAD no es un número sino un objeto.
    /// </summary>
    /// <remarks>
    /// Port de <c>AplicarFondoHatch</c> y <c>CrearColorACI</c>. El objeto de color se pide
    /// por <c>GetInterfaceObject</c> y su ProgID lleva la versión de AutoCAD pegada, así que
    /// hay que ir probando de la más nueva a la más vieja hasta que una responda. Si ninguna
    /// lo hace, el rayado se queda sin fondo y ya: es decoración.
    /// </remarks>
    private void FondoDelHatch(object hatch, int aci)
    {
        var versiones = new[]
        {
            "26", "25", "24", "23", "22", "21", "20", "19", "18", "17", "16", string.Empty
        };

        foreach (var v in versiones)
        {
            try
            {
                var progId = v.Length == 0
                    ? "AutoCAD.AcCmColor"
                    : "AutoCAD.AcCmColor." + v;

                var puesto = AcadConnection.Retry(() =>
                {
                    dynamic color = _doc.Application.GetInterfaceObject(progId);
                    color.ColorIndex = aci;

                    dynamic h = hatch;
                    h.BackgroundColor = color;
                    h.Update();

                    return true;
                });

                if (puesto)
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Esa versión no está: se prueba la siguiente.
            }
        }

        Nota(
            "Rayado de acero: no se pudo poner el color de fondo. El perfil queda con el " +
            "rayado pero sin el fondo tenue, que es solo decorativo.");
    }
}
