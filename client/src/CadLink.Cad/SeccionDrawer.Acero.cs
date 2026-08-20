namespace CadLink.Cad;

/// <summary>
/// Secciones de <b>acero</b>: la parte de <see cref="SeccionDrawer"/> que dibuja los
/// perfiles IR, OR, OC y CF.
/// </summary>
/// <remarks>
/// <para>
/// Port de las cuatro macros de la hoja de acero: <c>DibujarSeccionIR</c>,
/// <c>DibujarSeccionHSS</c>, <c>DibujarSeccionOC</c> y <c>DibujarSeccionCF</c>.
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
/// Lo que sí es propio de cada familia se conserva tal cual: la geometría, los patrones y
/// colores de rayado, qué cotas lleva y la altura del rótulo.
/// </para>
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>Capa de los perfiles de acero, la de las macros.</summary>
    private const string CapaPerfiles = "PERFILES";

    // El bulge del cuarto de círculo -el BULGE_90 de la macro del HSS- ya existe en la
    // parte principal de la clase, con el mismo número de quince cifras: Bulge90.

    /// <summary>Peralte, en pulgadas, a partir del cual el tubo rectangular se rellena.</summary>
    private const double PeralteLimitePulg = 5.0;

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

        AsegurarEstiloTexto();
        ConfigurarCotas();
    }

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

        var inicio = (int)_ms.Count;

        var h = p.PeralteCm * _escala;
        var b = p.AnchoCm * _escala;
        var t = p.EspesorCm * _escala;
        var tf = p.EspesorPatinCm * _escala;
        var labio = p.LabioCm * _escala;
        var radio = p.RadioCm * _escala;

        // El ancho del hueco y el centro del PRIMER perfil. En el OC el ancho es el
        // diámetro, que viene en el peralte.
        var uno = p.Familia == "OC" ? h : b;
        var centro = xIzquierda + (p.AnchoDibujoCm * _escala / 2);

        switch (p.Familia)
        {
            case "IR":
                PerfilIr(xIzquierda + (uno / 2), yAbajo, h, b, t, tf);

                if (p.Doble)
                {
                    PerfilIr(xIzquierda + uno + (uno / 2), yAbajo, h, b, t, tf);
                }

                CotasIr(xIzquierda + (uno / 2), yAbajo, h, b, t, tf, p.Doble);
                break;

            case "OR":
                // El peralte es el lado MAYOR: un tubo capturado como 10x20 y otro como
                // 20x10 son el mismo tubo, y en el plano se dibuja de pie.
                var bOr = Math.Min(b, h);
                var hOr = Math.Max(b, h);

                PerfilOr(xIzquierda + (bOr / 2), yAbajo, bOr, hOr, t, p);

                if (p.Doble)
                {
                    PerfilOr(xIzquierda + bOr + (bOr / 2), yAbajo, bOr, hOr, t, p);
                }

                CotasOr(xIzquierda + (bOr / 2), yAbajo, bOr, hOr, t, p.Doble);
                break;

            case "OC":
                var rExt = h / 2;
                var cyOc = yAbajo + rExt;

                PerfilOc(xIzquierda + rExt, cyOc, rExt, rExt - t);

                if (p.Doble)
                {
                    PerfilOc(xIzquierda + h + rExt, cyOc, rExt, rExt - t);
                }

                CotasOc(xIzquierda + rExt, cyOc, rExt, p.EspesorCm, p.Doble);
                break;

            case "CF":
                // El primero con el alma a la IZQUIERDA. El segundo, si es doble, va
                // ESPEJEADO con su alma a la derecha del todo: las dos canales quedan
                // enfrentadas formando un cajón, que es como se arman.
                PerfilCf(xIzquierda, yAbajo, h, b, t, labio, radio, false);

                if (p.Doble)
                {
                    PerfilCf(xIzquierda + (2 * b), yAbajo, h, b, t, labio, radio, true);
                }

                CotasCf(xIzquierda, yAbajo, h, b, t, labio, p.Doble);
                break;

            default:
                Nota(
                    $"Sección de acero '{p.Id}': la familia '{p.Familia}' no se reconoce. " +
                    "Las que se dibujan son IR, OR, OC y CF.");
                return 0;
        }

        RotuloAcero(p, centro, yAbajo - (0.06 * _f));

        var fin = (int)_ms.Count;

        Bloquear(p.Id, inicio, fin, destino);

        return fin - inicio;
    }

    // ==================================================================
    //  IR: el perfil I laminado
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
    private void PerfilIr(double cx, double cy, double d, double bf, double tw, double tf)
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

        var pl = Polilinea(pts, CapaPerfiles);

        if (pl is null)
        {
            Nota("Perfil IR: no se pudo crear el contorno.");
            return;
        }

        // El ancho constante del PEDIT de la macro: engrosa la línea del perfil para que
        // se lea como acero y no como una línea de construcción.
        AnchoConstante(pl, 0.001 * _f);

        Hatch("ANSI32", 0.0009 * _f, pl, null, CapaPerfiles, 252);
    }

    private void CotasIr(
        double cx, double cy, double d, double bf, double tw, double tf, bool doble)
    {
        var gap = 0.06 * _f;

        // Ancho del patín, arriba de cada perfil
        CotaAcero(cx - (bf / 2), cy + d, cx + (bf / 2), cy + d, cx, cy + d + gap);

        var cxUlt = cx;

        if (doble)
        {
            var cx2 = cx + bf;
            cxUlt = cx2;

            CotaAcero(cx2 - (bf / 2), cy + d, cx2 + (bf / 2), cy + d, cx2, cy + d + gap);

            // Y el ancho TOTAL de los dos, más arriba
            CotaAcero(
                cx - (bf / 2), cy + d, cx2 + (bf / 2), cy + d,
                cx + (bf / 2), cy + d + gap + (0.05 * _f));
        }

        // Peralte, a la derecha del último
        CotaAcero(
            cxUlt + (bf / 2), cy, cxUlt + (bf / 2), cy + d,
            cxUlt + (bf / 2) + gap, cy + (d / 2));

        // Espesor del patín, a la izquierda
        CotaAcero(
            cx - (bf / 2), cy + d, cx - (bf / 2), cy + d - tf,
            cx - (bf / 2) - gap, cy + d - (tf / 2));

        // Espesor del alma, a media altura
        CotaAcero(cx - (tw / 2), cy + (d / 2), cx + (tw / 2), cy + (d / 2), cx, cy + (d / 2) - gap);

        if (doble)
        {
            var cx2 = cx + bf;
            CotaAcero(
                cx2 - (tw / 2), cy + (d / 2), cx2 + (tw / 2), cy + (d / 2),
                cx2, cy + (d / 2) - gap);
        }
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
    /// <para>
    /// <b>El rayado depende del peralte</b>, y esa es una decisión de la macro que se
    /// conserva: por debajo de 5 pulgadas el tubo se raya fino con fondo cian, y de ahí
    /// para arriba se rellena sólido con un rayado más abierto encima. Un tubo pequeño
    /// relleno sale como un manchón negro en el plano.
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

        // El peralte en pulgadas, con la misma tolerancia de la macro para que un 5"
        // nominal no caiga del lado equivocado por un redondeo.
        var peralteIn = p.PeralteCm / 2.54;
        var menorDe5 = peralteIn < PeralteLimitePulg - 0.01;

        if (!menorDe5)
        {
            // Los colores son los de las CONSTANTES de la macro (141 y 144). Sus
            // comentarios dicen 94 y 80, que es lo que tuvo alguna vez; manda la
            // constante, que es lo que se ejecuta.
            Hatch("SOLID", 1, exterior, islas, CapaPerfiles, 141);
        }

        var trama = Hatch(
            "ANSI31",
            (menorDe5 ? 0.001 : 0.002) * _f,
            exterior, islas, CapaPerfiles,
            menorDe5 ? 142 : 144);

        if (menorDe5 && trama is not null)
        {
            FondoDelHatch(trama, 4);
        }
    }

    private void CotasOr(
        double cx, double cy, double bHss, double hHss, double tHss, bool doble)
    {
        var gap = 0.06 * _f;

        CotaAcero(cx - (bHss / 2), cy + hHss, cx + (bHss / 2), cy + hHss, cx, cy + hHss + gap);

        var cxUlt = cx;

        if (doble)
        {
            var cx2 = cx + bHss;
            cxUlt = cx2;

            CotaAcero(
                cx2 - (bHss / 2), cy + hHss, cx2 + (bHss / 2), cy + hHss,
                cx2, cy + hHss + gap);

            CotaAcero(
                cx - (bHss / 2), cy + hHss, cx2 + (bHss / 2), cy + hHss,
                cx + (bHss / 2), cy + hHss + gap + (0.05 * _f));

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
    //  OC: el tubo redondo
    // ==================================================================

    /// <summary>Las dos circunferencias del tubo redondo, con su corona rayada.</summary>
    /// <remarks>
    /// Port de <c>DibujarPerfilOC</c> y <c>AplicarHatchOC</c>. Si el espesor se come el
    /// radio, la circunferencia interior no se dibuja y el tubo sale macizo, que es lo que
    /// hace la macro cuando <c>radioInt</c> queda en cero.
    /// </remarks>
    private void PerfilOc(double cx, double cy, double rExt, double rInt)
    {
        var exterior = Circulo(cx, cy, rExt, CapaPerfiles);

        if (exterior is null)
        {
            Nota("Perfil OC: no se pudo crear la circunferencia exterior.");
            return;
        }

        var interior = rInt > 0 ? Circulo(cx, cy, rInt, CapaPerfiles) : null;
        var islas = interior is null ? null : new List<object> { interior };

        Hatch("SOLID", 1, exterior, islas, CapaPerfiles, 162);
        Hatch("ANSI31", 0.002 * _f, exterior, islas, CapaPerfiles, 162);
    }

    private void CotasOc(double cx, double cy, double rExt, double espesorCm, bool doble)
    {
        var gap = 0.06 * _f;

        CotaAcero(cx - rExt, cy + rExt, cx + rExt, cy + rExt, cx, cy + rExt + gap);

        var cxUlt = cx;

        if (doble)
        {
            var cx2 = cx + (2 * rExt);
            cxUlt = cx2;

            CotaAcero(
                cx - rExt, cy + rExt, cx2 + rExt, cy + rExt,
                (cx + cx2) / 2, cy + rExt + gap + (0.05 * _f));
        }

        // El espesor va como TEXTO y no como cota: en un tubo redondo la pared no tiene
        // dos caras paralelas que acotar de frente, así que la macro escribe «e=…».
        TextoAcero(
            $"e={espesorCm:0.000} cm",
            cxUlt + rExt + gap, cy,
            0.018 * _f,
            "COTAS");
    }

    // ==================================================================
    //  CF: la canal formada en frío
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
        bool espejo)
    {
        var s = espejo ? -1.0 : 1.0;

        if (lip <= t) { lip = t + (0.001 * _f); }
        if (b <= 2 * t) { b = (2 * t) + (0.001 * _f); }
        if (h <= 2 * t) { h = (2 * t) + (0.001 * _f); }
        if (ri < 0) { ri = 0; }

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

        object? pl;

        if (rExt <= 0 && rInt <= 0)
        {
            // Sin radios: doce vértices en pico, el caso que la macro dibuja con líneas.
            pl = PolyCerrada(new[]
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
            });
        }
        else
        {
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

            var lista = new List<(int, double)>();

            foreach (var (indice, cx, cy, a, bb) in bulges)
            {
                lista.Add((indice, BulgeDesdeCentro(
                    cx, cy,
                    pts[2 * a], pts[(2 * a) + 1],
                    pts[2 * bb], pts[(2 * bb) + 1])));
            }

            pl = PolilineaConBulges(pts, lista, CapaPerfiles);
        }

        if (pl is null)
        {
            Nota("Perfil CF: no se pudo crear el contorno.");
            return;
        }

        Hatch("SOLID", 1, pl, null, CapaPerfiles, 4);
        Hatch("ANSI31", 0.0008 * _f, pl, null, CapaPerfiles, 142);
    }

    private void CotasCf(
        double xIzq, double yBase, double h, double b, double t, double lip, bool doble)
    {
        var gap = 0.08 * _f;
        var anchoTotal = doble ? 2 * b : b;

        // Peralte, a la derecha de todo
        CotaAcero(
            xIzq + anchoTotal, yBase, xIzq + anchoTotal, yBase + h,
            xIzq + anchoTotal + gap, yBase + (h / 2));

        // Ancho del patín, arriba
        CotaAcero(
            xIzq, yBase + h + (0.02 * _f), xIzq + b, yBase + h + (0.02 * _f),
            xIzq + (b / 2), yBase + h + gap);

        // Largo del labio, junto al labio
        CotaAcero(
            xIzq + b - (0.05 * _f), yBase + h - lip, xIzq + b - (0.05 * _f), yBase + h,
            xIzq + b - (0.12 * _f), yBase + h - (lip / 2));

        // Espesor del alma, abajo a la izquierda
        CotaAcero(
            xIzq + (0.02 * _f), yBase, xIzq + (0.02 * _f), yBase + t,
            xIzq - gap, yBase + (t / 2));
    }

    // ==================================================================
    //  Rótulo
    // ==================================================================

    /// <summary>
    /// Los cuatro renglones del rótulo, con la altura de letra de cada familia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Port de los cuatro <c>AgregarRotulo…</c>, que dicen lo mismo con alturas distintas:
    /// 0.03 el IR, 0.02 el OC, 0.022 el CF y el OR 0.02 o 0.03 según si su primer número
    /// pasa de 6. Esa última regla se conserva porque tiene sentido: el rótulo se centra
    /// bajo el perfil y en un tubo de 4 pulgadas un texto grande sobresale por los lados.
    /// </para>
    /// <para>
    /// El salto de renglón va como <c>\P</c>, el de MText, no como salto de línea real: es
    /// lo que hacen tres de las cuatro macros y lo que ya usa el rótulo del concreto.
    /// </para>
    /// </remarks>
    private void RotuloAcero(PerfilAceroCad p, double xCentro, double yBase)
    {
        var etiqueta = p.Doble ? "PERFIL DOBLE: " : "PERFIL: ";

        var lineas = new List<string>
        {
            $"{p.Elemento.ToUpperInvariant()} \"{p.Id}\"",
            etiqueta + p.Perfil.ToUpperInvariant(),
            "ACERO " + p.Acero.ToUpperInvariant(),
            $"Acot. cm    Esc. {p.EscalaRotulo}"
        };

        var altura = p.Familia switch
        {
            "IR" => 0.03,
            "CF" => 0.022,
            "OR" => PrimerNumero(p.Perfil) is > 0 and <= 6 ? 0.02 : 0.03,
            _ => 0.02
        };

        TextoAcero(string.Join("\\P", lineas), xCentro, yBase, altura * _f, "ROTULOS");
    }

    /// <summary>El primer número que aparece en el nombre del perfil, o cero.</summary>
    /// <remarks>
    /// Port de <c>ExtraerPrimerNumero</c>. Sirve para decidir la altura del rótulo del OR:
    /// en <c>OR6X6X1/4</c> el primer número es el 6.
    /// </remarks>
    private static double PrimerNumero(string? texto)
    {
        var s = (texto ?? string.Empty).Trim();
        var acumulado = new System.Text.StringBuilder();
        var empezo = false;

        foreach (var ch in s)
        {
            if (char.IsDigit(ch) || ch == '.')
            {
                acumulado.Append(ch);
                empezo = true;
            }
            else if (empezo)
            {
                break;
            }
        }

        return double.TryParse(
            acumulado.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v)
            ? v
            : 0;
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
    private void TextoAcero(string contenido, double x, double y, double altura, string capa)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic t = _ms.AddMText(new[] { x, y, 0d }, 0.7 * _f, contenido);
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
            return PolyCerrada(new[] { x0, y0, x1, y0, x1, y1, x0, y1 });
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

    /// <summary>Una circunferencia.</summary>
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
            Fallo($"Circunferencia de radio {radio:0.###} en la capa {capa}", ex);
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
            "Rayado de acero: no se pudo poner el color de fondo. El tubo queda con el " +
            "rayado pero sin el fondo cian, que es solo decorativo.");
    }
}
