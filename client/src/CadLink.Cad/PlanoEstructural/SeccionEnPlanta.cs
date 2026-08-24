namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>contorno de una sección vista en planta</b>: la I, la C, la te, el ángulo, el cajón
/// y el tubo, con sus espesores de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Es lo que hace que un plano con estructura metálica se lea. Antes <b>todo</b> lo que no
/// era redondo salía como un rectángulo, así que una IR de 25×15 y un cajón de 25×15 se
/// dibujaban igual, y en el plano no había forma de distinguir una columna de acero de un
/// dado de concreto. En un plano estructural la sección <b>es</b> la información.
/// </para>
/// <para>
/// <b>Los ejes.</b> El contorno se devuelve centrado en el origen, con la medida
/// <c>b</c> sobre la <b>X</b> y la <c>h</c> sobre la <b>Y</b>, que es como el dibujante
/// coloca las secciones: <c>b</c> es <c>AnchoM</c> —el T3 de ETABS en una columna— y
/// <c>h</c> es <c>PeralteM</c> —su T2—. El giro del elemento se aplica <b>después</b>, con
/// <see cref="Colocar"/>, porque dentro de un bloque la geometría va derecha y el giro va en
/// la inserción.
/// </para>
/// <para>
/// <b>Sin espesores no hay perfil.</b> Si el modelo no dio el patín o el alma —pasa con las
/// secciones «auto select» y con las importadas a medias— se devuelve el rectángulo. Es
/// mejor una caja honesta que una I inventada con espesores a ojo, que se acotaría mal.
/// </para>
/// <para>
/// Está aparte del dibujante y sin AutoCAD a propósito: son polígonos, se comprueban con
/// aritmética en <c>tools/prueba-ejes-plano</c> y el dibujante solo los pasa a una polilínea.
/// </para>
/// </remarks>
public static class SeccionEnPlanta
{
    /// <summary>Bajo esto una medida se considera cero.</summary>
    private const double Nada = 1e-6;

    /// <summary>¿Esta forma se dibuja con <b>dos circunferencias</b>?</summary>
    /// <remarks>
    /// El tubo redondo —<c>TUBO</c>, el <c>GetPipe</c> de ETABS— no es un polígono: son la
    /// circunferencia de fuera y la de dentro. Se trata aparte en el dibujante.
    /// </remarks>
    public static bool EsRedonda(string forma) =>
        forma is "CIRC" or "TUBO" or "PIPE";

    /// <summary>¿Lleva <b>hueco</b> dentro, y por tanto un contorno interior?</summary>
    public static bool EsHueca(string forma) =>
        forma is "CAJON" or "TUBO" or "PIPE";

    /// <summary>
    /// El contorno de la sección, centrado en el origen: pares X, Y seguidos.
    /// </summary>
    /// <param name="forma">RECT, I, C, T, L, CAJON… lo que da el lector.</param>
    /// <param name="b">La medida sobre X, en metros. En una columna, su T3.</param>
    /// <param name="h">La medida sobre Y, en metros. En una columna, su T2.</param>
    /// <param name="patin">Espesor del patín —<c>Tf</c>—, en metros.</param>
    /// <param name="alma">Espesor del alma —<c>Tw</c>—, en metros.</param>
    /// <param name="pared">Espesor de la pared del cajón o del tubo, en metros.</param>
    /// <remarks>
    /// Devuelve el <b>rectángulo</b> —cuatro vértices— para RECT, para lo que no se reconozca
    /// y para cualquier perfil al que le falten los espesores. Nunca devuelve vacío en una
    /// forma poligonal: una columna sin contorno sería una columna que no se dibuja.
    /// </remarks>
    public static double[] Contorno(
        string forma, double b, double h, double patin, double alma, double pared = 0)
    {
        if (b <= Nada || h <= Nada)
        {
            return Array.Empty<double>();
        }

        var f = (forma ?? string.Empty).Trim().ToUpperInvariant();

        // El tubo redondo y el círculo no son polígonos: los dibuja el dibujante con
        // AddCircle, y aquí no hay contorno que dar.
        if (EsRedonda(f))
        {
            return Array.Empty<double>();
        }

        var tf = patin;
        var tw = alma;

        return f switch
        {
            "I" => PerfilI(b, h, tf, tw),
            "C" => Canal(b, h, tf, tw),
            "T" => Te(b, h, tf, tw),
            "L" => Angulo(b, h, tf, tw),
            // El cajón es el rectángulo de fuera; su hueco va aparte, en Hueco().
            "CAJON" => Rectangulo(b, h),
            _ => Rectangulo(b, h)
        };
    }

    /// <summary>
    /// El contorno <b>interior</b> —el hueco— de un cajón. Vacío en lo demás.
    /// </summary>
    /// <remarks>
    /// Va aparte del contorno de fuera porque son dos polilíneas y dos lazos del achurado:
    /// el de fuera y el de dentro. Un cajón dibujado sin su hueco parece una placa maciza y
    /// pesa cuatro veces más en el cálculo mental de quien lee el plano.
    /// </remarks>
    public static double[] Hueco(string forma, double b, double h, double pared)
    {
        var f = (forma ?? string.Empty).Trim().ToUpperInvariant();

        if (f != "CAJON" || pared <= Nada)
        {
            return Array.Empty<double>();
        }

        var bi = b - (2 * pared);
        var hi = h - (2 * pared);

        // Una pared que se come la sección entera: se dibuja macizo, que es lo que es.
        return bi <= Nada || hi <= Nada ? Array.Empty<double>() : Rectangulo(bi, hi);
    }

    /// <summary>
    /// El radio interior de un <b>tubo redondo</b>; 0 si es macizo o no se sabe.
    /// </summary>
    public static double RadioInterior(string forma, double b, double pared)
    {
        var f = (forma ?? string.Empty).Trim().ToUpperInvariant();

        if (f is not ("TUBO" or "PIPE") || pared <= Nada)
        {
            return 0;
        }

        var r = (b / 2) - pared;

        return r <= Nada ? 0 : r;
    }

    /// <summary>
    /// Los <b>rectángulos macizos</b> en los que se descompone la sección, para rellenarla.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el respaldo del achurado, y hace falta: un <c>AddHatch</c> dentro de una
    /// <i>definición de bloque</i> falla en varias versiones de AutoCAD y la columna se queda
    /// hueca. Un SOLID de cuatro puntos siempre se puede crear, pero solo cubre un
    /// cuadrilátero <b>convexo</b>, y una I no lo es. Así que se parte en las piezas de las
    /// que está hecha: los dos patines y el alma.
    /// </para>
    /// <para>
    /// Cada rectángulo llega como <c>x1, y1, x2, y2</c> —esquina inferior izquierda y
    /// superior derecha— en el mismo sistema centrado que el contorno.
    /// </para>
    /// </remarks>
    public static List<double[]> RectangulosDeRelleno(
        string forma, double b, double h, double patin, double alma, double pared = 0)
    {
        var salida = new List<double[]>();

        if (b <= Nada || h <= Nada)
        {
            return salida;
        }

        var f = (forma ?? string.Empty).Trim().ToUpperInvariant();

        var mb = b / 2;
        var mh = h / 2;

        var tf = patin;
        var tw = alma;

        switch (f)
        {
            case "I" when Valen(b, h, tf, tw):
                // Los dos patines, de lado a lado, y el alma entre ellos.
                salida.Add(new[] { -mb, -mh, -mb + tf, mh });
                salida.Add(new[] { mb - tf, -mh, mb, mh });
                salida.Add(new[] { -mb + tf, -tw / 2, mb - tf, tw / 2 });
                break;

            case "C" when Valen(b, h, tf, tw):
                // El alma corrida y los dos patines saliendo de ella.
                salida.Add(new[] { -mb, -mh, mb, -mh + tw });
                salida.Add(new[] { -mb, -mh + tw, -mb + tf, mh });
                salida.Add(new[] { mb - tf, -mh + tw, mb, mh });
                break;

            case "T" when Valen(b, h, tf, tw):
                salida.Add(new[] { -mb, -mh, -mb + tf, mh });
                salida.Add(new[] { -mb + tf, -tw / 2, mb, tw / 2 });
                break;

            case "L" when ValenAlas(b, h, tf, tw):
                // La pierna larga sobre X con su espesor Tw, y la otra con su Tf.
                salida.Add(new[] { -mb, -mh, mb, -mh + tw });
                salida.Add(new[] { -mb, -mh + tw, -mb + tf, mh });
                break;

            case "CAJON" when pared > Nada && b - (2 * pared) > Nada && h - (2 * pared) > Nada:
                // Las cuatro paredes: las dos largas completas y las dos cortas entre ellas.
                salida.Add(new[] { -mb, -mh, mb, -mh + pared });
                salida.Add(new[] { -mb, mh - pared, mb, mh });
                salida.Add(new[] { -mb, -mh + pared, -mb + pared, mh - pared });
                salida.Add(new[] { mb - pared, -mh + pared, mb, mh - pared });
                break;

            case "CIRC":
            case "TUBO":
            case "PIPE":
                // Redondas: no hay rectángulo que valga, se quedan con su achurado o huecas.
                break;

            default:
                salida.Add(new[] { -mb, -mh, mb, mh });
                break;
        }

        return salida;
    }

    /// <summary>
    /// Gira un contorno y lo <b>lleva a su sitio</b>: es lo que se dibuja en el plano.
    /// </summary>
    /// <remarks>
    /// El giro es alrededor del <b>centro de la sección</b> —el nudo—, que es donde gira de
    /// verdad una columna en ETABS. Solo se usa cuando la sección se dibuja suelta: dentro de
    /// un bloque la geometría va derecha y el giro va en la inserción, para que un
    /// <c>BLOCKREPLACE</c> conserve la orientación de cada columna.
    /// </remarks>
    public static double[] Colocar(double[] contorno, double cx, double cy, double grados)
    {
        var salida = new double[contorno.Length];

        var a = grados * Math.PI / 180;
        var ca = Math.Cos(a);
        var sa = Math.Sin(a);

        for (var i = 0; i + 1 < contorno.Length; i += 2)
        {
            var x = contorno[i];
            var y = contorno[i + 1];

            salida[i] = cx + (x * ca) - (y * sa);
            salida[i + 1] = cy + (x * sa) + (y * ca);
        }

        return salida;
    }

    // =================================================================================
    //  CADA FORMA
    // =================================================================================

    /// <summary>El rectángulo de siempre: cuatro vértices, en sentido antihorario.</summary>
    public static double[] Rectangulo(double b, double h)
    {
        var mb = b / 2;
        var mh = h / 2;

        return new[] { -mb, -mh, mb, -mh, mb, mh, -mb, mh };
    }

    /// <summary>
    /// La <b>I</b>: doce vértices —dos patines y el alma—.
    /// </summary>
    /// <remarks>
    /// Los patines son perpendiculares a la medida <c>b</c>, que en una columna es su
    /// peralte: es como se ve una IR en planta, con los dos patines a la vista y el alma
    /// uniéndolos por el centro.
    /// </remarks>
    private static double[] PerfilI(double b, double h, double tf, double tw)
    {
        if (!Valen(b, h, tf, tw))
        {
            return Rectangulo(b, h);
        }

        var mb = b / 2;
        var mh = h / 2;
        var mw = tw / 2;

        var xi = -mb + tf;   // cara interior del patín de la izquierda
        var xd = mb - tf;    // la del de la derecha

        return new[]
        {
            -mb, -mh,
            xi, -mh,
            xi, -mw,
            xd, -mw,
            xd, -mh,
            mb, -mh,
            mb, mh,
            xd, mh,
            xd, mw,
            xi, mw,
            xi, mh,
            -mb, mh
        };
    }

    /// <summary>
    /// La <b>canal</b>: ocho vértices, el alma a un lado y los dos patines saliendo de ella.
    /// </summary>
    private static double[] Canal(double b, double h, double tf, double tw)
    {
        if (!Valen(b, h, tf, tw))
        {
            return Rectangulo(b, h);
        }

        var mb = b / 2;
        var mh = h / 2;

        var ya = -mh + tw;   // cara interior del alma
        var xi = -mb + tf;
        var xd = mb - tf;

        return new[]
        {
            -mb, -mh,
            mb, -mh,
            mb, mh,
            xd, mh,
            xd, ya,
            xi, ya,
            xi, mh,
            -mb, mh
        };
    }

    /// <summary>La <b>te</b>: ocho vértices, un patín y el alma colgando de él.</summary>
    private static double[] Te(double b, double h, double tf, double tw)
    {
        if (!Valen(b, h, tf, tw))
        {
            return Rectangulo(b, h);
        }

        var mb = b / 2;
        var mh = h / 2;
        var mw = tw / 2;

        var xi = -mb + tf;

        return new[]
        {
            -mb, -mh,
            xi, -mh,
            xi, -mw,
            mb, -mw,
            mb, mw,
            xi, mw,
            xi, mh,
            -mb, mh
        };
    }

    /// <summary>El <b>ángulo</b>: seis vértices, sus dos alas en escuadra.</summary>
    /// <remarks>
    /// Cada ala lleva <b>su</b> espesor y no el del otro: en <c>GetAngle</c>, <c>Tw</c> es el
    /// de la pierna que mide <c>T3</c> —la que aquí va sobre la X— y <c>Tf</c> el de la que
    /// mide <c>T2</c>. En un ángulo de alas iguales da lo mismo, pero en uno de alas
    /// desiguales cruzarlos deja el dibujo con los espesores al revés.
    /// </remarks>
    private static double[] Angulo(double b, double h, double tf, double tw)
    {
        if (!ValenAlas(b, h, tf, tw))
        {
            return Rectangulo(b, h);
        }

        var mb = b / 2;
        var mh = h / 2;

        return new[]
        {
            -mb, -mh,
            mb, -mh,
            mb, -mh + tw,
            -mb + tf, -mh + tw,
            -mb + tf, mh,
            -mb, mh
        };
    }

    /// <summary>
    /// ¿Los espesores dan para dibujar el perfil, o hay que caer al rectángulo?
    /// </summary>
    /// <remarks>
    /// Se piden los dos mayores que cero y <b>menores que la mitad</b> de su medida. Un patín
    /// que se come media sección no es un perfil: es un dato malo, y con él la polilínea
    /// saldría con los vértices cruzados —un moño— en lugar de una I.
    /// </remarks>
    private static bool Valen(double b, double h, double tf, double tw) =>
        tf > Nada && tw > Nada && tf < b / 2 && tw < h;

    /// <summary>
    /// Lo mismo para el <b>ángulo</b>, donde cada espesor cruza la sección entera.
    /// </summary>
    /// <remarks>
    /// En una I el patín tiene que caber dos veces en el peralte; en un ángulo hay <b>una</b>
    /// ala de cada lado, así que basta con que cada espesor sea menor que la medida que
    /// cruza. Con la regla de la I, un ángulo de 5×5 cm con alas de 6 mm se dibujaría como
    /// una caja.
    /// </remarks>
    private static bool ValenAlas(double b, double h, double tf, double tw) =>
        tf > Nada && tw > Nada && tf < b && tw < h;
}
