namespace CadLink.Cad;

/// <summary>
/// El <b>contorno</b> de un perfil de acero: puntos y dobleces, sin AutoCAD de por medio.
/// </summary>
/// <remarks>
/// <para>
/// Es geometría pura y por eso vive aparte del dibujante. <b>Existe para que la vista previa
/// de la pantalla y el dibujo de AutoCAD salgan del mismo cálculo.</b> Con los vértices
/// dentro del dibujante, la vista previa tendría que repetirlos, y una vista previa que
/// calcula la forma por su cuenta puede enseñar algo distinto de lo que se va a dibujar
/// —que es exactamente lo que una vista previa no puede hacer—.
/// </para>
/// <para>
/// Devuelve las nueve formas en <b>coordenadas de dibujo ya escaladas</b>, con el borde
/// izquierdo del perfil en <c>x0</c> y su paño inferior en <c>y0</c>, que es como las
/// coloca el dibujante.
/// </para>
/// </remarks>
public static class TrazoAcero
{
    /// <summary>Un contorno cerrado: los puntos y el bulge de los vértices que lo tienen.</summary>
    /// <param name="Puntos">Plano: <c>x1,y1,x2,y2…</c>, como los quiere AutoCAD.</param>
    /// <param name="Dobleces">Índice de vértice y bulge. Vacío si el contorno va en pico.</param>
    public sealed record Contorno(double[] Puntos, (int Indice, double Bulge)[] Dobleces);

    /// <summary>Una circunferencia, para las dos formas redondas.</summary>
    public sealed record Circulo(double Cx, double Cy, double R);

    /// <summary>
    /// Todo lo que hay que trazar de un perfil.
    /// </summary>
    /// <remarks>
    /// Las nueve formas caben en estas cuatro piezas y ninguna usa más de dos: las siete
    /// poligonales llevan solo <see cref="Exterior"/>, salvo el tubo rectangular que además
    /// lleva su hueco en <see cref="Interior"/>; el tubo redondo lleva las dos
    /// circunferencias y el macizo solo la de fuera.
    /// </remarks>
    public sealed record Trazo(
        Contorno? Exterior = null,
        Contorno? Interior = null,
        Circulo? CircExterior = null,
        Circulo? CircInterior = null);

    /// <summary>Tan(90/4 grados): el bulge de un arco de 90 en una polilínea.</summary>
    private const double Bulge90 = 0.414213562373095;

    /// <summary>
    /// El trazo de un perfil, con su borde izquierdo en <paramref name="x0"/> y su paño
    /// inferior en <paramref name="y0"/>.
    /// </summary>
    /// <param name="p">El perfil, con las medidas en centímetros.</param>
    /// <param name="x0">Borde izquierdo, en unidades de dibujo.</param>
    /// <param name="y0">Paño inferior, en unidades de dibujo.</param>
    /// <param name="escala">Cuántas unidades de dibujo mide un centímetro.</param>
    /// <param name="espejo">
    /// Espejea las formas que tienen un lado —canal, canal con labios, zeta y ángulo—, que
    /// es como se dibuja el segundo perfil de una pareja.
    /// </param>
    /// <returns>El trazo, o <c>null</c> si las medidas no dan para dibujar nada.</returns>
    public static Trazo? De(
        PerfilAceroCad p, double x0, double y0, double escala, bool espejo = false)
    {
        if (p.PeralteCm <= 0 || escala <= 0)
        {
            return null;
        }

        var h = p.PeralteCm * escala;
        var b = p.AnchoCm * escala;
        var t = p.EspesorCm * escala;
        var tf = p.EspesorPatinCm * escala;
        var labio = p.LabioCm * escala;
        var radio = p.RadioCm * escala;
        var bMenor = p.PatinAngostoCm * escala;

        // El alto y el ancho del DIBUJO, que en el tubo rectangular no son el peralte y el
        // ancho capturados: el tubo se dibuja de pie, con su lado mayor en vertical.
        var alto = p.AltoDibujoCm * escala;
        var uno = p.AnchoDeUnoCm * escala;

        // Un milímetro de holgura para las medidas que no dan. Es el mismo criterio del
        // dibujante: antes de dibujar un perfil imposible, se dibuja el mínimo que se
        // sostiene, y la columna «Falta» de la hoja ya avisó de que los datos no cuadran.
        var eps = 0.1 * escala;

        return p.Forma switch
        {
            FormaAcero.I => new Trazo(
                Exterior: PerfilI(x0 + (uno / 2), y0, h, b, t, tf)),

            FormaAcero.Te => new Trazo(
                Exterior: PerfilTe(x0 + (uno / 2), y0, h, b, t, tf)),

            FormaAcero.Canal => new Trazo(
                Exterior: PerfilCanal(x0, y0, h, b, t, tf, espejo)),

            FormaAcero.Angulo => new Trazo(
                Exterior: PerfilAngulo(x0, y0, h, b, t, espejo)),

            FormaAcero.CanalConLabios => new Trazo(
                Exterior: PerfilCf(x0, y0, h, b, t, labio, radio, espejo, eps)),

            FormaAcero.Zeta => new Trazo(
                Exterior: PerfilZeta(x0, y0, h, b, bMenor, t, radio, espejo, eps)),

            FormaAcero.TuboRectangular => TuboRectangular(x0, y0, uno, alto, t),

            FormaAcero.TuboRedondo => new Trazo(
                CircExterior: new Circulo(x0 + (h / 2), y0 + (h / 2), h / 2),
                CircInterior: (h / 2) - t > 0
                    ? new Circulo(x0 + (h / 2), y0 + (h / 2), (h / 2) - t)
                    : null),

            FormaAcero.RedondoMacizo => new Trazo(
                CircExterior: new Circulo(x0 + (h / 2), y0 + (h / 2), h / 2)),

            _ => null
        };
    }

    // ==================================================================
    //  Las siete formas poligonales
    // ==================================================================

    /// <summary>El perfil I, de doce vértices. Port de <c>DibujarPerfilW</c>.</summary>
    /// <remarks>
    /// Los doce puntos van en el orden de la macro, empezando por el patín inferior derecho
    /// y girando en sentido antihorario. No lleva curvas de acuerdo entre alma y patín: la
    /// macro tampoco, y a la escala de un plano estructural no se distinguirían.
    /// </remarks>
    private static Contorno PerfilI(
        double cx, double cy, double d, double bf, double tw, double tf) =>
        EnPico(new[]
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
        });

    /// <summary>La te: un patín arriba y el alma colgando. Ocho vértices.</summary>
    /// <remarks>
    /// El peralte es el <b>total</b>, del canto del patín a la punta del alma, que es la
    /// columna <c>d</c> del manual y no la <c>h</c>, que solo mide el alma libre. Y el patín
    /// va <b>arriba</b>, que es la posición en la que se usa como cuerda de armadura y la de
    /// la mitad de un perfil I partido, que es de donde sale.
    /// </remarks>
    private static Contorno PerfilTe(
        double cx, double cy, double d, double bf, double tw, double tf) =>
        EnPico(new[]
        {
            cx + (tw / 2), cy,
            cx + (tw / 2), cy + d - tf,
            cx + (bf / 2), cy + d - tf,
            cx + (bf / 2), cy + d,
            cx - (bf / 2), cy + d,
            cx - (bf / 2), cy + d - tf,
            cx - (tw / 2), cy + d - tf,
            cx - (tw / 2), cy
        });

    /// <summary>La canal laminada: alma a un lado y dos patines, sin labios.</summary>
    /// <remarks>
    /// No se puede dibujar con la canal con labios aunque las dos se llamen «canal»: la CF
    /// es lámina doblada de espesor único, con labios y radios, y la C es laminada, con el
    /// alma y los patines de <b>distinto espesor</b> y sin nada doblado. Los patines van de
    /// espesor constante: el manual da un solo <c>tf</c>, que es el medio, y la cuña real
    /// del patín laminado no está en los datos.
    /// </remarks>
    private static Contorno PerfilCanal(
        double xIzq, double y0, double d, double bf, double tw, double tf, bool espejo)
    {
        var s = espejo ? -1.0 : 1.0;

        // Con espejo el alma se va al otro extremo del hueco, para que dos canales queden
        // enfrentadas formando un cajón, que es como se arman.
        var xAlma = espejo ? xIzq + bf : xIzq;

        return EnPico(new[]
        {
            xAlma, y0,
            xAlma, y0 + d,
            xAlma + (s * bf), y0 + d,
            xAlma + (s * bf), y0 + d - tf,
            xAlma + (s * tw), y0 + d - tf,
            xAlma + (s * tw), y0 + tf,
            xAlma + (s * bf), y0 + tf,
            xAlma + (s * bf), y0
        });
    }

    /// <summary>El ángulo: dos alas en escuadra del mismo espesor. Seis vértices.</summary>
    /// <remarks>
    /// Va <b>en pico</b>, sin el acuerdo del talón ni las puntas redondeadas, y no es una
    /// simplificación gratuita: el manual IMCA <b>no da ningún radio para el ángulo</b>. Sus
    /// 143 filas tienen todas las columnas de geometría en «-» y las medidas están solo en
    /// la designación. Inventar un radio sería dibujar un dato que nadie dio.
    /// </remarks>
    private static Contorno PerfilAngulo(
        double xIzq, double y0, double alaLarga, double alaCorta, double t, bool espejo)
    {
        var s = espejo ? -1.0 : 1.0;

        // El talón se va al otro extremo del hueco al espejear, para que dos ángulos queden
        // espalda contra espalda, que es como se arma un doble ángulo.
        var xTalon = espejo ? xIzq + alaCorta : xIzq;

        return EnPico(new[]
        {
            xTalon, y0,
            xTalon + (s * alaCorta), y0,
            xTalon + (s * alaCorta), y0 + t,
            xTalon + (s * t), y0 + t,
            xTalon + (s * t), y0 + alaLarga,
            xTalon, y0 + alaLarga
        });
    }

    /// <summary>
    /// La canal con labios, con sus ocho dobleces. Port de <c>CrearCFReal</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El radio exterior es el capturado y el interior su mitad, cada uno recortado a lo que
    /// cabe, igual que la macro: en el exterior manda el menor entre medio ancho, el labio y
    /// medio peralte, y en el interior lo mismo descontando el espesor.
    /// </para>
    /// <para>
    /// <b>Es UNA polilínea donde la macro dibuja dos cosas.</b> La macro traza el contorno
    /// como veinticuatro líneas y arcos sueltos, los une con <c>JoinEntities</c> y además
    /// construye una segunda polilínea con bulges para el rayado: dos entidades con
    /// exactamente la misma geometría, una encima de la otra. Con los bulges basta una.
    /// </para>
    /// </remarks>
    private static Contorno PerfilCf(
        double xWeb, double y0, double h, double b, double t, double lip, double ri,
        bool espejo, double eps)
    {
        var s = espejo ? -1.0 : 1.0;

        if (lip <= t) { lip = t + eps; }
        if (b <= 2 * t) { b = (2 * t) + eps; }
        if (h <= 2 * t) { h = (2 * t) + eps; }
        if (ri < 0) { ri = 0; }

        // Con espejo el alma se va al otro extremo del hueco.
        if (espejo) { xWeb += b; }

        // Radio EXTERIOR: el capturado, recortado a lo que cabe.
        var rExt = Math.Max(0, Math.Min(ri, Math.Min(b / 2, Math.Min(lip, h / 2))));

        // Radio INTERIOR: la mitad, recortada por su cuenta. No es rExt - t: la macro lo
        // fija en ri/2, y con eso el doblez interior sale más cerrado que el exterior.
        var rIntMax = Math.Min((b - t) / 2, Math.Min((h - (2 * t)) / 2, lip - t));
        var rInt = Math.Max(0, Math.Min(ri / 2, rIntMax));

        var xWebOut = xWeb;
        var xWebIn = xWeb + (s * t);
        var xFlangeOut = xWeb + (s * b);
        var xFlangeIn = xFlangeOut - (s * t);
        var yb = y0;
        var yt = y0 + h;

        if (rExt <= 0 && rInt <= 0)
        {
            // Sin radios: doce vértices en pico, el caso que la macro dibuja con líneas.
            return EnPico(new[]
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

        // Los ocho dobleces, cada uno con su centro. El bulge sale del barrido real entre
        // los dos vértices vistos desde el centro, así que el espejo se resuelve solo: al
        // invertir s, los barridos cambian de signo y los arcos también.
        var centros = new (int Indice, double Cx, double Cy, int A, int B)[]
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

        return new Contorno(pts, BulgesDesdeCentros(pts, centros));
    }

    /// <summary>
    /// La zeta: el alma vertical con un patín a cada lado, y sus cuatro dobleces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los <b>dos patines son de distinto ancho</b> —60.3 y 54 mm en la de 2 3/8"— y eso no
    /// es una errata del manual: es lo que permite traslapar dos zetas en el apoyo, porque el
    /// patín angosto de una entra dentro del ancho de la otra. El patín <b>ancho va arriba</b>
    /// y el angosto abajo, que es la posición de montaje.
    /// </para>
    /// <para>
    /// El radio interior es el exterior <b>menos el espesor</b>, y eso es geometría y no
    /// convención: una zeta es una lámina de espesor único doblada dos veces, así que en cada
    /// doblez la cara de dentro y la de fuera son dos arcos <b>concéntricos</b> separados
    /// exactamente el espesor.
    /// </para>
    /// <para>
    /// <b>Y ojo con los dos dobleces interiores:</b> sus centros caen fuera del acero y a
    /// distinto lado del alma, el de arriba por la derecha y el de abajo por la izquierda,
    /// porque los dos patines salen a lados contrarios. Con los dos al mismo lado, el
    /// contorno de abajo se devuelve sobre sí mismo y la polilínea se cruza: el rayado sale
    /// por fuera del perfil.
    /// </para>
    /// </remarks>
    private static Contorno PerfilZeta(
        double xIzq, double y0, double h, double bAncho, double bAngosto, double t,
        double ri, bool espejo, double eps)
    {
        if (t <= 0) { t = eps; }
        if (bAngosto <= t) { bAngosto = bAncho; }
        if (h <= 2 * t) { h = (2 * t) + eps; }
        if (ri < 0) { ri = 0; }

        // El ancho total: los dos patines menos el espesor del alma, que comparten.
        var w = bAncho + bAngosto - t;

        var rExt = Math.Max(0, Math.Min(
            ri, Math.Min(Math.Min(bAncho, bAngosto) / 2, (h - (2 * t)) / 2)));

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
            return EnPico(new[]
            {
                X(xIzq), y0,
                X(xAlmaDer), y0,
                X(xAlmaDer), yt - t,
                X(xTope), yt - t,
                X(xTope), yt,
                X(xAlmaIzq), yt,
                X(xAlmaIzq), y0 + t,
                X(xIzq), y0 + t
            });
        }

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

        var centros = new (int Indice, double Cx, double Cy, int A, int B)[]
        {
            (1, X(xAlmaDer - rExt), y0 + rExt, 1, 2),
            (3, X(xAlmaDer + rInt), yt - t - rInt, 3, 4),
            (7, X(xAlmaIzq + rExt), yt - rExt, 7, 8),
            (9, X(xAlmaIzq - rInt), y0 + t + rInt, 9, 10)
        };

        return new Contorno(pts, BulgesDesdeCentros(pts, centros));
    }

    /// <summary>
    /// El tubo rectangular: dos rectángulos redondeados, el de fuera y su hueco.
    /// </summary>
    /// <remarks>
    /// Port de <c>DibujarPerfilHSS</c>. Los radios no se capturan: el exterior es el propio
    /// espesor y el interior su mitad, como en la macro, y los dos se recortan si no caben.
    /// Si la pared se come el hueco, el tubo sale macizo.
    /// </remarks>
    private static Trazo TuboRectangular(
        double x0, double y0, double bHss, double hHss, double tHss)
    {
        var rOut = Math.Min(tHss, Math.Min(bHss, hHss) / 2);

        var exterior = RectanguloRedondeado(x0, y0, x0 + bHss, y0 + hHss, rOut);

        var bInt = bHss - (2 * tHss);
        var hInt = hHss - (2 * tHss);

        Contorno? interior = null;

        if (bInt > 0 && hInt > 0)
        {
            var rIn = Math.Min(tHss / 2, Math.Min(bInt, hInt) / 2);

            interior = RectanguloRedondeado(
                x0 + tHss, y0 + tHss, x0 + bHss - tHss, y0 + hHss - tHss, rIn);
        }

        return new Trazo(Exterior: exterior, Interior: interior);
    }

    // ==================================================================
    //  Auxiliares
    // ==================================================================

    /// <summary>Un contorno sin dobleces.</summary>
    private static Contorno EnPico(double[] pts) =>
        new(pts, Array.Empty<(int, double)>());

    /// <summary>
    /// Un rectángulo con las cuatro esquinas redondeadas. Port de
    /// <c>CrearRectanguloRedondeado</c>.
    /// </summary>
    private static Contorno? RectanguloRedondeado(
        double x0, double y0, double x1, double y1, double r)
    {
        if (x1 - x0 <= 0 || y1 - y0 <= 0)
        {
            return null;
        }

        if (r <= 1e-7)
        {
            return EnPico(new[] { x0, y0, x1, y0, x1, y1, x0, y1 });
        }

        return new Contorno(
            new[]
            {
                x0 + r, y0,
                x1 - r, y0,
                x1, y0 + r,
                x1, y1 - r,
                x1 - r, y1,
                x0 + r, y1,
                x0, y1 - r,
                x0, y0 + r
            },
            new[] { (1, Bulge90), (3, Bulge90), (5, Bulge90), (7, Bulge90) });
    }

    /// <summary>Los bulges de una lista de dobleces, cada uno visto desde su centro.</summary>
    private static (int, double)[] BulgesDesdeCentros(
        double[] pts, (int Indice, double Cx, double Cy, int A, int B)[] centros)
    {
        var bulges = new (int, double)[centros.Length];

        for (var i = 0; i < centros.Length; i++)
        {
            var (indice, cx, cy, a, b) = centros[i];

            bulges[i] = (indice, BulgeDesdeCentro(
                cx, cy,
                pts[2 * a], pts[(2 * a) + 1],
                pts[2 * b], pts[(2 * b) + 1]));
        }

        return bulges;
    }

    /// <summary>El bulge de un arco visto desde su centro.</summary>
    /// <remarks>
    /// Port de <c>BulgeDesdeCentro</c>: es la tangente de la cuarta parte del barrido, con el
    /// barrido normalizado a media vuelta para cada lado. Así el signo sale solo y los arcos
    /// del perfil espejeado giran al revés sin tener que decírselo.
    /// </remarks>
    public static double BulgeDesdeCentro(
        double cx, double cy, double xa, double ya, double xb, double yb)
    {
        var aa = Math.Atan2(ya - cy, xa - cx);
        var ab = Math.Atan2(yb - cy, xb - cx);

        var barrido = ab - aa;

        while (barrido > Math.PI)
        {
            barrido -= 2 * Math.PI;
        }

        while (barrido <= -Math.PI)
        {
            barrido += 2 * Math.PI;
        }

        return Math.Tan(barrido / 4);
    }

    /// <summary>
    /// El contorno convertido en <b>puntos sueltos</b>, con cada arco muestreado.
    /// </summary>
    /// <remarks>
    /// Lo usa la vista previa de la pantalla, que dibuja sobre un lienzo sin arcos. El
    /// dibujante de AutoCAD no lo necesita: allí los bulges van tal cual en la polilínea.
    /// </remarks>
    /// <param name="porArco">Tramos con los que se aproxima cada arco.</param>
    public static List<(double X, double Y)> Muestrear(Contorno c, int porArco = 12)
    {
        var salida = new List<(double X, double Y)>();
        var n = c.Puntos.Length / 2;

        var deVertice = new Dictionary<int, double>();

        foreach (var (indice, bulge) in c.Dobleces)
        {
            deVertice[indice] = bulge;
        }

        for (var i = 0; i < n; i++)
        {
            var px = c.Puntos[2 * i];
            var py = c.Puntos[(2 * i) + 1];

            salida.Add((px, py));

            if (!deVertice.TryGetValue(i, out var b) || Math.Abs(b) < 1e-15)
            {
                continue;
            }

            var j = (i + 1) % n;
            var qx = c.Puntos[2 * j];
            var qy = c.Puntos[(2 * j) + 1];

            var cuerda = Math.Sqrt(((qx - px) * (qx - px)) + ((qy - py) * (qy - py)));

            if (cuerda < 1e-15)
            {
                continue;
            }

            // Del bulge salen el barrido y el radio; del radio y la cuerda, el centro.
            var barrido = 4 * Math.Atan(b);
            var r = cuerda / (2 * Math.Sin(Math.Abs(barrido) / 2));

            var mx = (px + qx) / 2;
            var my = (py + qy) / 2;
            var dx = (qx - px) / cuerda;
            var dy = (qy - py) / cuerda;

            var altura = Math.Sqrt(Math.Max((r * r) - ((cuerda / 2) * (cuerda / 2)), 0));
            var signo = barrido > 0 ? 1.0 : -1.0;

            var ccx = mx - (signo * altura * dy);
            var ccy = my + (signo * altura * dx);

            var a0 = Math.Atan2(py - ccy, px - ccx);

            for (var k = 1; k < porArco; k++)
            {
                var a = a0 + (barrido * k / porArco);

                salida.Add((ccx + (r * Math.Cos(a)), ccy + (r * Math.Sin(a))));
            }
        }

        return salida;
    }
}
