namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// Lleva las líneas del muro y de la trabe <b>al paño</b> del castillo, la columna o el
/// perfil de acero en el que mueren, en lugar de hasta su eje.
/// </summary>
/// <remarks>
/// <para>
/// Es <c>RecortarAlPano</c> de la macro, y es lo que hace que el plano se vea construido. En
/// el modelo el muro llega al <b>nudo</b> —al centro de la columna—, porque ahí es donde se
/// une el elemento. Pero en el plano las dos líneas del muro dibujadas hasta el centro se
/// meten dentro de la sección de la columna y la cruzan, y en obra el muro no está ahí: el
/// muro <b>empieza en el paño</b> del castillo. Así que hay que recortarlo.
/// </para>
/// <para>
/// <b>Y también alargarlo</b>, que es la otra mitad del asunto y la parte que se olvida: si
/// en el modelo el muro se quedó corto —no llega hasta el nudo—, en el plano queda un hueco
/// entre el muro y el castillo. La misma cuenta lo resuelve: el recorte sale <b>negativo</b>
/// y el extremo se mueve hacia afuera hasta tocar el paño. Es el detalle elegante de su
/// macro, y aquí está igual.
/// </para>
/// <para>
/// <b>Cómo se calcula.</b> Se lanza un rayo desde el extremo del muro hacia adentro del muro
/// y se mira dónde <b>sale del material</b> de la columna, midiendo contra las piezas de las
/// que está hecha la sección —el rectángulo del castillo, o los dos patines y el alma de una
/// W—. Con eso los dos casos finos de la macro salen solos, sin tener que distinguirlos:
/// </para>
/// <list type="bullet">
///   <item>
///     El muro que llega <b>por el patín</b> de una columna W sale por la <b>cara exterior
///     del patín</b>: el rayo recorre el alma y sigue por el patín, que es material
///     contiguo.
///   </item>
///   <item>
///     El que <b>entra entre los patines</b> se para en la <b>cara del alma</b>, que es lo
///     primero que encuentra. Es el caso que la macro trata aparte con
///     <c>PANO_ALMA_W</c>, y aquí es la misma cuenta.
///   </item>
/// </list>
/// <para>
/// No toca AutoCAD: es geometría, y tiene su prueba en <c>tools/prueba-ejes-plano</c>.
/// </para>
/// </remarks>
public sealed class PanoDeApoyo
{
    private const double Nada = 1e-9;

    private readonly ConfigPlano _cfg;

    public PanoDeApoyo(ConfigPlano cfg) => _cfg = cfg;

    /// <summary>¿Se llevan las líneas al paño? Es <c>LINEAS_AL_PANO</c>.</summary>
    public bool Activo => _cfg.Bandera("LINEAS_AL_PANO", true);

    /// <summary>
    /// Radio de búsqueda del elemento al que hay que llegar: <c>PANO_BUSCA_CM</c>, 1.50 m.
    /// </summary>
    /// <remarks>
    /// Es generoso a propósito, y hace falta para el caso del muro que quedó corto en el
    /// modelo: si solo se mirara un palmo alrededor del extremo, ese muro no encontraría su
    /// castillo y el hueco se quedaría en el plano.
    /// </remarks>
    public double RadioBusqueda => Positivo(_cfg.Numero("PANO_BUSCA_CM", 150) / 100, 1.5);

    /// <summary>
    /// Cuánto se mete la línea <b>dentro</b> del elemento: <c>PANO_SOLAPE_CM</c>, 0.
    /// </summary>
    /// <remarks>
    /// En 0 —como está en la hoja— la línea termina <b>exactamente</b> en el paño. Se deja
    /// configurable porque hay quien prefiere un centímetro de solape para que en la
    /// impresión no se vea una raya blanca entre el muro y el castillo.
    /// </remarks>
    public double Solape => _cfg.Numero("PANO_SOLAPE_CM", 0) / 100;

    /// <summary>Cuánto se alarga como máximo un muro que quedó corto: 1.50 m.</summary>
    public double AlargarMax => Positivo(_cfg.Numero("PANO_ALARGAR_MAX_CM", 150) / 100, 1.5);

    /// <summary>
    /// Fracción máxima que se recorta <b>por lado</b>: <c>PANO_RECORTE_MAX</c>, 0.4.
    /// </summary>
    /// <remarks>
    /// El tope existe para que un dato raro no se coma el muro. Si por lo que sea la cuenta
    /// pide recortar más del 40 % de un lado, se deja el muro como estaba: es mejor un muro
    /// que llega al eje que un muro que desaparece.
    /// </remarks>
    public double RecorteMax
    {
        get
        {
            var f = _cfg.Numero("PANO_RECORTE_MAX", 0.4);
            return f is > 0 and < 1 ? f : 0.4;
        }
    }

    /// <summary>
    /// ¿Se entra hasta el <b>alma</b> de las columnas W? Es <c>PANO_ALMA_W</c>.
    /// </summary>
    /// <remarks>
    /// En SI —como está en la hoja— la sección se mide por sus piezas, así que el muro que
    /// entra entre los patines llega al alma. En NO se mide por el rectángulo que envuelve al
    /// perfil, y entonces se para en la punta del patín.
    /// </remarks>
    public bool HastaElAlma => _cfg.Bandera("PANO_ALMA_W", true);

    /// <summary>El tramo de un elemento, ya llevado a los paños.</summary>
    public readonly record struct Tramo(double X1, double Y1, double X2, double Y2)
    {
        public double Largo =>
            Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
    }

    /// <summary>
    /// Recorta —o alarga— los dos extremos de un elemento hasta el paño de sus apoyos.
    /// </summary>
    /// <param name="el">El muro, la trabe o la cadena.</param>
    /// <param name="apoyos">
    /// Los elementos a los que hay que llegar: las <b>columnas y los castillos</b> de la
    /// planta, sean de concreto o perfiles de acero. Se pasan todos y aquí se busca; los que
    /// no son verticales se ignoran.
    /// </param>
    /// <remarks>
    /// Si algo no cuadra —no hay apoyo cerca, la cuenta pide comerse el muro, el elemento no
    /// tiene largo— se devuelve el tramo <b>tal como llegó</b>. Recortar mal un muro se ve
    /// mucho más que no recortarlo.
    /// </remarks>
    /// <param name="cruces">
    /// Las <b>huellas de las otras barras</b> —vigas, cadenas, muros—, hechas con
    /// <see cref="Huella"/>. Son las que hacen que una <b>viga muera en la cara de la viga
    /// que cruza</b> en lugar de pasarle por encima, que es lo que se veía como una reja de
    /// líneas cruzadas en cada nudo. Se miran <b>después</b> de las columnas: si el extremo
    /// muere en un castillo, manda el castillo.
    /// </param>
    public Tramo Recortar(
        ElementoPlanta el,
        IReadOnlyList<ElementoPlanta> apoyos,
        IReadOnlyList<ElementoPlanta>? cruces = null)
    {
        var tal = new Tramo(el.X1, el.Y1, el.X2, el.Y2);

        if (!Activo || (apoyos.Count == 0 && (cruces is null || cruces.Count == 0)))
        {
            return tal;
        }

        var largo = tal.Largo;

        if (largo < 1e-4)
        {
            return tal;
        }

        var ux = (el.X2 - el.X1) / largo;
        var uy = (el.Y2 - el.Y1) / largo;

        // Cada extremo mira hacia ADENTRO del muro: el rayo sale del extremo y recorre el
        // muro, así que lo que encuentra es el paño por el que el muro sale de la columna.
        var ta = Avance(el, el.X1, el.Y1, ux, uy, apoyos, cruces, largo);
        var tb = Avance(el, el.X2, el.Y2, -ux, -uy, apoyos, cruces, largo);

        var nuevo = new Tramo(
            el.X1 + (ux * ta), el.Y1 + (uy * ta),
            el.X2 - (ux * tb), el.Y2 - (uy * tb));

        // Que no se cruce ni se quede en nada: con los dos recortes al tope se iría el 80 %,
        // y un muro de 10 cm entre dos castillos no dice nada en el plano.
        return nuevo.Largo < Math.Max(0.02, largo * 0.1) ? tal : nuevo;
    }

    /// <summary>
    /// Cuánto hay que mover un extremo: positivo recorta, negativo alarga.
    /// </summary>
    private double Avance(
        ElementoPlanta el, double px, double py, double dx, double dy,
        IReadOnlyList<ElementoPlanta> apoyos, IReadOnlyList<ElementoPlanta>? cruces,
        double largo)
    {
        // PRIMERO LAS COLUMNAS: si el extremo muere en un castillo, manda el castillo. Solo
        // si ahí no hay nada se mira la viga o el muro que cruza.
        var t = MejorSalida(px, py, dx, dy, apoyos, null);

        t ??= MejorSalida(px, py, dx, dy, cruces, el);

        if (t is not { } avance)
        {
            return 0;
        }

        if (avance > 0)
        {
            avance -= Solape;

            // El tope: si la cuenta pide recortar más de PANO_RECORTE_MAX de este lado, no se
            // recorta NADA por aquí. Y no se recorta «hasta el tope», que dejaría un tocón:
            // pasarse del tope significa que ese muro no va de paño a paño de esos apoyos, y
            // entonces lo correcto es dejarlo como venía.
            return avance <= 0 || avance > largo * RecorteMax ? 0 : avance;
        }

        // Alargue: el muro se quedó corto en el modelo y se estira hasta tocar el paño, pero
        // solo hasta PANO_ALARGAR_MAX_CM. Más allá, lo que hay no es un muro corto: es otra
        // cosa, y estirarlo metro y medio sería inventar.
        return -avance > AlargarMax ? 0 : avance;
    }

    /// <summary>
    /// El paño <b>más cercano</b> al extremo entre todos los candidatos.
    /// </summary>
    /// <param name="propio">
    /// El elemento que se está recortando, cuando los candidatos son huellas de otras barras.
    /// Sirve para dos descartes imprescindibles: no medirse contra <b>sí mismo</b>, y no
    /// medirse contra una barra <b>paralela</b>. Sin el segundo, dos muros seguidos en línea
    /// se recortarían el uno al otro medio muro, porque el extremo de cada uno cae dentro de
    /// la huella del vecino.
    /// </param>
    /// <remarks>
    /// Se elige por la <b>distancia al paño</b> —el menor <c>|t|</c>— y no por la distancia al
    /// centro del candidato: la huella de una viga de seis metros tiene su centro lejísimos
    /// del nudo, así que por centro nunca se elegiría. Y se descarta lo que quede a más de
    /// <see cref="RadioBusqueda"/>, que es el filtro de la macro.
    /// </remarks>
    private double? MejorSalida(
        double px, double py, double dx, double dy,
        IReadOnlyList<ElementoPlanta>? candidatos, ElementoPlanta? propio)
    {
        if (candidatos is null || candidatos.Count == 0)
        {
            return null;
        }

        double? mejor = null;
        var radio = RadioBusqueda;

        foreach (var c in candidatos)
        {
            if (c.Clase != ClasePlanta.Columna)
            {
                continue;
            }

            if (propio is not null && !EsTransversal(propio, c))
            {
                continue;
            }

            var t = SalidaDelMaterial(c, px, py, dx, dy, HastaElAlma);

            if (t is not { } v || Math.Abs(v) > radio)
            {
                continue;
            }

            if (mejor is null || Math.Abs(v) < Math.Abs(mejor.Value))
            {
                mejor = v;
            }
        }

        return mejor;
    }

    /// <summary>
    /// ¿La huella <b>cruza</b> al elemento, o va en su misma dirección?
    /// </summary>
    /// <remarks>
    /// Solo se recorta contra lo que cruza. Dos barras en línea —dos tramos del mismo muro,
    /// una cadena partida en el modelo— no se recortan entre sí: se tocan por la punta, y
    /// medirlas una contra otra dejaría cada tramo la mitad. El corte es a 20°, que distingue
    /// de sobra un cruce de una continuación, incluso con ejes inclinados.
    /// </remarks>
    private static bool EsTransversal(ElementoPlanta el, ElementoPlanta huella)
    {
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < Nada)
        {
            return false;
        }

        var a = huella.AnguloGrados * Math.PI / 180;
        var hx = Math.Cos(a);
        var hy = Math.Sin(a);

        // El seno del ángulo entre las dos direcciones, con el producto cruzado.
        var seno = Math.Abs(((dx / largo) * hy) - ((dy / largo) * hx));

        return seno > 0.342;   // sen 20°
    }

    /// <summary>
    /// Dónde <b>sale del material</b> del apoyo un rayo que arranca en un punto.
    /// </summary>
    /// <returns>
    /// El parámetro del rayo —en metros— donde deja el material, o <c>null</c> si el rayo no
    /// da con el apoyo o si el apoyo le queda <b>delante</b> (un castillo intermedio por el
    /// que el muro pasa de largo: ese no recorta nada).
    /// </returns>
    /// <remarks>
    /// <para>
    /// La sección se mide por <b>las piezas de las que está hecha</b> —el rectángulo del
    /// castillo, o los dos patines y el alma de una W—, y los tramos que el rayo pasa por
    /// dentro de cada pieza se <b>unen</b>. Es lo que hace que salgan solos los dos casos de
    /// la columna W: el alma y el patín se tocan, así que un rayo que va por el alma hacia el
    /// patín ve un tramo seguido y sale por la cara de fuera del patín; y un rayo que entra
    /// entre los patines solo ve el alma y sale por su cara.
    /// </para>
    /// <para>
    /// Tres casos, y el tercero es el que evita un destrozo:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     El extremo está <b>dentro</b> del apoyo —lo normal, porque el modelo lo pone en el
    ///     nudo—: se devuelve por dónde sale. Positivo: recorta.
    ///   </item>
    ///   <item>
    ///     El apoyo queda <b>detrás</b> del extremo: el muro se quedó corto. Se devuelve
    ///     negativo, y el extremo se estira hasta el paño.
    ///   </item>
    ///   <item>
    ///     El apoyo queda <b>delante</b>, dentro del muro: es un castillo intermedio. Se
    ///     devuelve <c>null</c> y no se toca nada. Sin esta salida, un muro largo con un
    ///     castillo a un metro de la punta se recortaría hasta él.
    ///   </item>
    /// </list>
    /// </remarks>
    public static double? SalidaDelMaterial(
        ElementoPlanta apoyo, double px, double py, double dx, double dy, bool porPiezas = true)
    {
        var unidos = Intervalos(apoyo, px, py, dx, dy, porPiezas);

        if (unidos.Count == 0)
        {
            return null;
        }

        // 1) El extremo está DENTRO: se sale por el final de ese tramo.
        foreach (var t in unidos)
        {
            if (t.A <= 1e-9 && t.B >= -1e-9)
            {
                return t.B;
            }
        }

        // 2) El apoyo queda DETRÁS: se alarga hasta su cara, la más cercana al extremo.
        double? atras = null;

        foreach (var t in unidos)
        {
            if (t.B < 0 && (atras is null || t.B > atras))
            {
                atras = t.B;
            }
        }

        // 3) Y si queda delante, no se toca: es un castillo intermedio.
        return atras;
    }

    /// <summary>
    /// Los tramos —ya unidos— en que un rayo va <b>por dentro del material</b> de un apoyo.
    /// </summary>
    /// <remarks>
    /// Es la cuenta que comparten el ajuste al paño y el recorte del contorno de la losa: uno
    /// pregunta por dónde <b>sale</b> y el otro por qué trozos <b>quitar</b>. Los tramos se
    /// unen cuando se tocan, que es lo que hace que el alma y el patín de una W cuenten como
    /// una sola pieza de acero y no como dos.
    /// </remarks>
    public static List<(double A, double B)> Intervalos(
        ElementoPlanta apoyo, double px, double py, double dx, double dy,
        bool porPiezas = true)
    {
        var vacio = new List<(double A, double B)>();

        var b = apoyo.AnchoM;
        var h = apoyo.PeralteM;

        if (b <= Nada || h <= Nada)
        {
            return vacio;
        }

        // Al sistema de la sección: el giro de la columna se deshace, y así todo se mide con
        // rectángulos rectos, que es lo único que hay que saber intersecar.
        var a = apoyo.AnguloGrados * Math.PI / 180;
        var ca = Math.Cos(a);
        var sa = Math.Sin(a);

        var rx = px - apoyo.X1;
        var ry = py - apoyo.Y1;

        var ox = (rx * ca) + (ry * sa);
        var oy = (-rx * sa) + (ry * ca);

        var vx = (dx * ca) + (dy * sa);
        var vy = (-dx * sa) + (dy * ca);

        var tramos = new List<(double A, double B)>();

        if (SeccionEnPlanta.EsRedonda(apoyo.Forma))
        {
            // La redonda, por la ecuación de segundo grado. Se mide MACIZA aunque sea un
            // tubo: un muro que muere en una columna redonda se para en la circunferencia de
            // fuera, no en la pared de dentro.
            var t = Circulo(ox, oy, vx, vy, b / 2);

            if (t is { } c)
            {
                tramos.Add(c);
            }
        }
        else
        {
            var piezas = porPiezas
                ? SeccionEnPlanta.RectangulosDeRelleno(
                    apoyo.Forma, b, h, apoyo.PatinM, apoyo.AlmaM, apoyo.ParedM)
                : new List<double[]> { new[] { -b / 2, -h / 2, b / 2, h / 2 } };

            if (piezas.Count == 0)
            {
                piezas = new List<double[]> { new[] { -b / 2, -h / 2, b / 2, h / 2 } };
            }

            foreach (var r in piezas)
            {
                var t = Rectangulo(ox, oy, vx, vy, r);

                if (t is { } c)
                {
                    tramos.Add(c);
                }
            }
        }

        if (tramos.Count == 0)
        {
            return vacio;
        }

        // Se unen los que se tocan: el alma y el patín comparten su cara, y si no se unieran
        // el rayo «saldría» del alma en un sitio donde sigue habiendo acero.
        tramos.Sort((p, q) => p.A.CompareTo(q.A));

        var unidos = new List<(double A, double B)> { tramos[0] };

        foreach (var t in tramos.Skip(1))
        {
            var ultimo = unidos[^1];

            if (t.A <= ultimo.B + 1e-9)
            {
                unidos[^1] = (ultimo.A, Math.Max(ultimo.B, t.B));
            }
            else
            {
                unidos.Add(t);
            }
        }

        return unidos;
    }

    /// <summary>
    /// La <b>huella</b> de una barra —muro, trabe o cadena— como si fuera una sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el truco que permite tratar de una sola forma los dos recortes que hacen falta: un
    /// muro que muere en un castillo y una <b>viga que muere en otra viga</b>. La barra se
    /// convierte en un rectángulo largo —su largo por su ancho, girado en su dirección— y a
    /// partir de ahí es la misma cuenta del rayo.
    /// </para>
    /// <para>
    /// Se devuelve como <see cref="ElementoPlanta"/> de clase columna para poder reusar la
    /// cuenta tal cual. No se dibuja: solo se mide con ella.
    /// </para>
    /// </remarks>
    public static ElementoPlanta Huella(ElementoPlanta barra, double ancho)
    {
        var dx = barra.X2 - barra.X1;
        var dy = barra.Y2 - barra.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        return new ElementoPlanta
        {
            Clase = ClasePlanta.Columna,
            Forma = "RECT",
            Etiqueta = barra.Etiqueta,
            X1 = (barra.X1 + barra.X2) / 2,
            Y1 = (barra.Y1 + barra.Y2) / 2,
            X2 = (barra.X1 + barra.X2) / 2,
            Y2 = (barra.Y1 + barra.Y2) / 2,
            AnchoM = largo,
            PeralteM = ancho,
            AnguloGrados = largo < 1e-9 ? 0 : Math.Atan2(dy, dx) * 180 / Math.PI
        };
    }

    /// <summary>
    /// El tramo en que un rayo cruza un rectángulo recto, por el <i>slab method</i>.
    /// </summary>
    /// <remarks>
    /// Se corta el rayo con las dos franjas —la de las X y la de las Y— y se toma lo que
    /// tienen en común. Es el algoritmo de la macro y el de cualquier trazador de rayos: sin
    /// casos especiales, y funciona igual si el rayo entra por un lado, por una esquina o
    /// paralelo a un lado.
    /// </remarks>
    private static (double A, double B)? Rectangulo(
        double ox, double oy, double vx, double vy, double[] r)
    {
        var tMin = double.NegativeInfinity;
        var tMax = double.PositiveInfinity;

        if (!Franja(ox, vx, r[0], r[2], ref tMin, ref tMax) ||
            !Franja(oy, vy, r[1], r[3], ref tMin, ref tMax))
        {
            return null;
        }

        return tMin > tMax ? null : (tMin, tMax);
    }

    private static bool Franja(
        double o, double v, double min, double max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(v) < Nada)
        {
            // Paralelo a la franja: o va por dentro todo el rato, o no la toca nunca.
            return o >= min - Nada && o <= max + Nada;
        }

        var t1 = (min - o) / v;
        var t2 = (max - o) / v;

        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);

        return true;
    }

    /// <summary>El tramo en que un rayo cruza una circunferencia.</summary>
    private static (double A, double B)? Circulo(
        double ox, double oy, double vx, double vy, double radio)
    {
        var a = (vx * vx) + (vy * vy);

        if (a < Nada)
        {
            return null;
        }

        var b = 2 * ((ox * vx) + (oy * vy));
        var c = (ox * ox) + (oy * oy) - (radio * radio);

        var disc = (b * b) - (4 * a * c);

        if (disc < 0)
        {
            return null;
        }

        var raiz = Math.Sqrt(disc);

        return ((-b - raiz) / (2 * a), (-b + raiz) / (2 * a));
    }

    private static double Positivo(double v, double omision) => v > 0 ? v : omision;
}
