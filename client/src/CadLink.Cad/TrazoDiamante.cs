namespace CadLink.Cad;

/// <summary>
/// La <b>geometría del estribo diamante</b>: el recorrido de círculos que abraza y la
/// cinta tangente a ellos. Sin AutoCAD y sin WPF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué está aquí y no dentro del dibujante.</b> Este cálculo lo necesitan ahora
/// <b>dos</b> programas: el dibujante de AutoCAD y la <b>vista previa</b> de la pestaña de
/// concreto. Y no puede haber dos copias. Una vista previa que calcula el rombo por su
/// cuenta puede acabar enseñando un diamante que no es el que se va a dibujar —otro
/// vértice, otra varilla abrazada— y entonces no sirve para lo único que sirve una vista
/// previa, que es confiar en ella. Es el mismo motivo por el que existe
/// <see cref="TrazoAcero"/>.
/// </para>
/// <para>
/// <b>Qué es un diamante.</b> No es un rombo geométrico: es una <b>cinta cerrada tangente
/// a una serie de círculos</b> —las varillas centrales de arriba y de abajo, y los dos
/// dobleces de los costados— recorridos en sentido antihorario. Así el estribo abraza de
/// verdad las varillas que sujeta, con los dobleces redondeados y no en pico, que es como
/// se arma en obra.
/// </para>
/// <para>
/// Todo va en <b>unidades de dibujo</b>, las que se le pasen: si entran centímetros salen
/// centímetros, y si entran píxeles salen píxeles. La clase no sabe de escalas.
/// </para>
/// </remarks>
public static class TrazoDiamante
{
    /// <summary>Radio del doblez de la esquina, como fracción del diámetro.</summary>
    public const double FactorRadioEsquina = 0.5;

    /// <summary>Tolerancia para dar una varilla por «centrada», en radios.</summary>
    public const double TolCentroFactor = 0.5;

    /// <summary>Holgura entre el diamante y la varilla que abraza.</summary>
    public const double HolguraGancho = 0.0;

    /// <summary>
    /// El <b>recorrido de círculos</b> que abraza el diamante, en orden antihorario.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El orden es <b>derecha, arriba, izquierda, abajo</b>, y tiene que ser antihorario y
    /// sin cruces o la cinta sale hecha un nudo.
    /// </para>
    /// <para>
    /// En cada vértice se abraza <b>una</b> varilla si hay alguna en el eje y <b>dos</b> si
    /// el eje cae entre dos, que es lo que pasa con un número par: ahí el vértice sale
    /// plano y centrado, como se arma. Si no hay varilla en ese sitio se usa un círculo
    /// <b>ficticio</b> a <c>rEsqExt</c> del paño, que es lo único que hacía la macro.
    /// </para>
    /// <para>
    /// Y al final se meten en el recorrido las varillas laterales que la cinta
    /// atravesaría: el estribo no cruza el acero, lo rodea.
    /// </para>
    /// </remarks>
    /// <param name="x1">Paño izquierdo del núcleo, ya descontado el recubrimiento.</param>
    /// <param name="y1">Paño inferior del núcleo.</param>
    /// <param name="x2">Paño derecho del núcleo.</param>
    /// <param name="y2">Paño superior del núcleo.</param>
    /// <param name="dDia">Diámetro de la varilla del diamante.</param>
    /// <param name="varSup">Varillas del lecho superior, con su radio.</param>
    /// <param name="varInf">Varillas del lecho inferior.</param>
    /// <param name="varLat">Varillas laterales, las de los costados.</param>
    /// <returns>
    /// El recorrido, o <c>null</c> si con esos datos no hay diamante que dibujar: sin
    /// diámetro, con el núcleo al revés o con menos de tres círculos.
    /// </returns>
    public static List<(double X, double Y, double R)>? Centros(
        double x1, double y1, double x2, double y2, double dDia,
        List<(double X, double Y, double R)> varSup,
        List<(double X, double Y, double R)> varInf,
        List<(double X, double Y, double R)> varLat,
        List<string>? notas = null)
    {
        if (dDia <= 0 || x2 <= x1 || y2 <= y1)
        {
            return null;
        }

        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;

        // ---------- Radios de los dobleces laterales ----------
        var rEsqInt = FactorRadioEsquina * dDia;

        if (rEsqInt < dDia * 0.25)
        {
            rEsqInt = dDia * 0.25;
        }

        var rEsqExt = rEsqInt + dDia;

        // En una sección estrecha el doblez no puede ser mayor que la sección: se recorta,
        // pero solo si queda un radio con sentido.
        var rMaxLat = 0.35 * (x2 - x1);

        if (rEsqExt > rMaxLat && rMaxLat > dDia * 1.3)
        {
            rEsqExt = rMaxLat;
            rEsqInt = rEsqExt - dDia;
        }

        // ---------- A qué varillas se abraza ----------
        var selSup = VarillasDelCentro(varSup, cx);
        var selInf = VarillasDelCentro(varInf, cx);

        var centros = new List<(double X, double Y, double R)>();

        centros.AddRange(
            DoblezLateral(true, cx, cy, x1, x2, rEsqExt, rEsqInt, varLat));

        if (selSup.Count == 0)
        {
            centros.Add((cx, y2 - rEsqExt, rEsqInt));
        }
        else if (selSup.Count == 1)
        {
            centros.Add(ConHolgura(selSup[0]));
        }
        else
        {
            // Arriba se recorre de derecha a izquierda
            var (a, bb) = selSup[0].X > selSup[1].X
                ? (selSup[0], selSup[1])
                : (selSup[1], selSup[0]);

            centros.Add(ConHolgura(a));
            centros.Add(ConHolgura(bb));
        }

        centros.AddRange(
            DoblezLateral(false, cx, cy, x1, x2, rEsqExt, rEsqInt, varLat));

        if (selInf.Count == 0)
        {
            centros.Add((cx, y1 + rEsqExt, rEsqInt));
        }
        else if (selInf.Count == 1)
        {
            centros.Add(ConHolgura(selInf[0]));
        }
        else
        {
            // Abajo se recorre de izquierda a derecha, para cerrar el circuito
            var (a, bb) = selInf[0].X < selInf[1].X
                ? (selInf[0], selInf[1])
                : (selInf[1], selInf[0]);

            centros.Add(ConHolgura(a));
            centros.Add(ConHolgura(bb));
        }

        if (centros.Count < 3)
        {
            return null;
        }

        // ---------- Que la cinta rodee las varillas laterales ----------
        return RodearLaterales(centros, dDia, varLat, notas);
    }

    /// <summary>
    /// La cinta convertida en <b>puntos</b>, para quien no sepa dibujar arcos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La cinta es una polilínea con <i>bulges</i>: cada doblez es un arco, no una esquina.
    /// AutoCAD los dibuja tal cual, pero un lienzo de WPF no tiene bulges, así que cada
    /// arco se parte en tramos rectos.
    /// </para>
    /// <para>
    /// <b>El bulge es <c>tan(barrido / 4)</c></b>, así que el barrido se recupera con
    /// <c>4·atan(bulge)</c>, y el centro del arco sale de la cuerda y de ese barrido. Es la
    /// misma cuenta que hace <see cref="TrazoAcero.Muestrear"/>, y por el mismo motivo.
    /// </para>
    /// </remarks>
    /// <param name="porArco">Tramos por arco. Doce ya no se distinguen en pantalla.</param>
    public static List<(double X, double Y)> Muestrear(
        double[] pts, double[] bulges, int porArco = 12)
    {
        var salida = new List<(double X, double Y)>();

        var n = pts.Length / 2;

        if (n < 2)
        {
            return salida;
        }

        var tramos = Math.Max(2, porArco);

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var ax = pts[2 * i];
            var ay = pts[(2 * i) + 1];
            var bx = pts[2 * j];
            var by = pts[(2 * j) + 1];

            salida.Add((ax, ay));

            var bulge = i < bulges.Length ? bulges[i] : 0;

            if (Math.Abs(bulge) < 1e-12)
            {
                // Tramo recto: con el punto de arranque basta, el siguiente lo pone la
                // vuelta siguiente del bucle.
                continue;
            }

            // ---------- El arco ----------
            // barrido = 4·atan(bulge), CON SIGNO: positivo es antihorario.
            var barrido = 4 * Math.Atan(bulge);

            var cuerdaX = bx - ax;
            var cuerdaY = by - ay;
            var cuerda = Math.Sqrt((cuerdaX * cuerdaX) + (cuerdaY * cuerdaY));

            if (cuerda < 1e-12 || Math.Abs(Math.Sin(barrido / 2)) < 1e-12)
            {
                continue;
            }

            // El radio, TAMBIÉN CON SIGNO: sale negativo cuando el barrido lo es.
            //
            // Y ese signo es justo lo que coloca el centro del lado que toca. Con el radio
            // en valor absoluto hay que decidir a mano a qué lado de la cuerda cae el
            // centro, y ese es el error clásico de esta cuenta: se acierta con los arcos de
            // menos de media vuelta y se falla con los cerrados, que salen volteados. Con
            // el seno y el coseno del medio barrido se resuelve solo, porque el coseno ya
            // cambia de signo pasada la media vuelta.
            var radio = cuerda / (2 * Math.Sin(barrido / 2));

            // Normal IZQUIERDA de la cuerda.
            var nx = -cuerdaY / cuerda;
            var ny = cuerdaX / cuerda;

            var mx = (ax + bx) / 2;
            var my = (ay + by) / 2;

            var d = radio * Math.Cos(barrido / 2);

            var ccx = mx + (nx * d);
            var ccy = my + (ny * d);

            // El radio para dibujar es el de verdad, o sea la distancia del centro al
            // arranque. Se mide en lugar de usar |radio| por si la cuerda venía con ruido.
            var rDib = Math.Sqrt(((ax - ccx) * (ax - ccx)) + ((ay - ccy) * (ay - ccy)));

            var a0 = Math.Atan2(ay - ccy, ax - ccx);

            for (var k = 1; k < tramos; k++)
            {
                var a = a0 + (barrido * k / tramos);

                salida.Add((ccx + (rDib * Math.Cos(a)), ccy + (rDib * Math.Sin(a))));
            }
        }

        return salida;
    }

    /// <summary>Extremos del tramo recto que va del círculo <paramref name="i"/> al <paramref name="j"/>.</summary>
    /// <remarks>
    /// Los vértices van de dos en dos por círculo: el primero es donde <b>llega</b> la
    /// tangente anterior y el segundo donde <b>sale</b> la siguiente. Así que el tramo
    /// recto va del segundo vértice del círculo i al primero del j.
    /// </remarks>
    private static ((double X, double Y) A, (double X, double Y) B) TramoRecto(
        double[] pts, int i, int j)
    {
        return ((pts[(4 * i) + 2], pts[(4 * i) + 3]),
                (pts[4 * j], pts[(4 * j) + 1]));
    }

    private static bool YaEsta(
        (double X, double Y, double R) v, List<(double X, double Y, double R)> lista)
    {
        // Se compara por posición y no por identidad: el recorrido guarda copias con
        // la holgura ya sumada al radio, así que el radio no sirve para comparar.
        return lista.Any(c => Math.Abs(c.X - v.X) < 1e-9 && Math.Abs(c.Y - v.Y) < 1e-9);
    }

    /// <summary>Distancia del centro de una varilla a un segmento.</summary>
    private static double DistanciaASegmento(
        (double X, double Y, double R) v, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var largo2 = (dx * dx) + (dy * dy);

        if (largo2 < 1e-18)
        {
            return Math.Sqrt(((v.X - a.X) * (v.X - a.X)) + ((v.Y - a.Y) * (v.Y - a.Y)));
        }

        // Se recorta al segmento: sin el Clamp se mediría a la RECTA, y una varilla
        // que está más allá del extremo del tramo saldría como atravesada.
        var t = Math.Clamp((((v.X - a.X) * dx) + ((v.Y - a.Y) * dy)) / largo2, 0, 1);

        var px = a.X + (t * dx);
        var py = a.Y + (t * dy);

        return Math.Sqrt(((v.X - px) * (v.X - px)) + ((v.Y - py) * (v.Y - py)));
    }

    /// <summary>Cuánto se ha avanzado por el tramo, de 0 en A a 1 en B.</summary>
    private static double Avance(
        (double X, double Y, double R) v, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var largo2 = (dx * dx) + (dy * dy);

        return largo2 < 1e-18
            ? 0
            : (((v.X - a.X) * dx) + ((v.Y - a.Y) * dy)) / largo2;
    }

    private static (double X, double Y, double R) ConHolgura((double X, double Y, double R) v) =>
        (v.X, v.Y, v.R + HolguraGancho);

    /// <summary>
    /// El doblez de un costado: la <b>varilla lateral</b> si la hay, o un círculo
    /// ficticio si no.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto es la corrección de fondo, y no está en la macro.</b> Allí el doblez
    /// lateral es siempre un círculo ficticio colocado a
    /// <c>rEsqExt</c> del paño, sin mirar si en ese sitio hay una varilla. Y en un
    /// armado normal la hay: la varilla lateral va justo ahí, a media altura del
    /// costado. El resultado era que la cinta pasaba <b>por encima de la varilla</b>,
    /// y en el dibujo la diagonal del diamante la cortaba por la mitad.
    /// </para>
    /// <para>
    /// La solución no es esquivarla: es <b>abrazarla</b>. El doblez del diamante tiene
    /// que doblar sobre esa varilla, que es lo que se hace en obra y lo que pidió el
    /// usuario: que siga su circunferencia. Así que si hay una varilla lateral en ese
    /// costado, ella <i>es</i> el doblez, con su radio real.
    /// </para>
    /// <para>
    /// Se toma la más cercana a media altura porque es la que marca el vértice del
    /// rombo. Las demás varillas del costado, si la cinta las cruzara al ir de este
    /// doblez a la varilla central, las recoge después
    /// <see cref="RodearLaterales"/>.
    /// </para>
    /// </remarks>
    private static List<(double X, double Y, double R)> DoblezLateral(
        bool derecha, double cx, double cy, double x1, double x2,
        double rEsqExt, double rEsqInt,
        List<(double X, double Y, double R)> varLat)
    {
        var ficticio = new List<(double X, double Y, double R)>
        {
            derecha ? (x2 - rEsqExt, cy, rEsqInt) : (x1 + rEsqExt, cy, rEsqInt)
        };

        // Las varillas de ESE costado, y de ese costado solo: mezclarlas haría que el
        // doblez de la derecha se fuera a abrazar una varilla de la izquierda.
        var delLado = varLat
            .Where(v => derecha ? v.X > cx : v.X < cx)
            .ToList();

        if (delLado.Count == 0)
        {
            return ficticio;
        }

        // ------------------------------------------------------------------
        // Una varilla si hay alguna en el eje; DOS si el eje cae entre dos.
        // ------------------------------------------------------------------
        // Es la misma regla que ya se usaba arriba y abajo (VarillasDelCentro), y
        // ahora también a los lados. Con un número PAR de varillas laterales no hay
        // ninguna a media altura, y forzar el doblez sobre la más cercana dejaba el
        // vértice del rombo descentrado y la otra varilla fuera, atravesada por la
        // cinta. Doblando sobre las dos más juntas el vértice sale plano y centrado,
        // que es como se arma.
        //
        // El recorrido tiene que seguir siendo antihorario, así que en el costado
        // DERECHO se va de abajo hacia arriba y en el IZQUIERDO al contrario.
        var seleccion = VarillasDelCentro(
            delLado.Select(v => (v.X, Y: v.Y, v.R)).ToList(),
            cy,
            porY: true);

        if (seleccion.Count == 0)
        {
            return ficticio;
        }

        var orden = derecha
            ? seleccion.OrderBy(v => v.Y).ToList()
            : seleccion.OrderByDescending(v => v.Y).ToList();

        return orden.Select(ConHolgura).ToList();
    }

    /// <summary>Cuántas pasadas se dan buscando varillas que la cinta atraviese.</summary>
    /// <remarks>
    /// Hacen falta varias porque rodear una varilla <b>empuja la cinta hacia fuera</b>
    /// en ese tramo, y la cinta empujada puede acabar cruzando otra varilla que antes
    /// no tocaba. Con una sola pasada quedarían cruces. El tope existe para que un
    /// caso raro no se quede dando vueltas: tres pasadas resuelven cualquier armado
    /// real, porque cada costado no lleva más de dos o tres varillas laterales.
    /// </remarks>
    private const int PasadasRodeo = 4;

    /// <summary>
    /// Mete en el recorrido de la cinta las varillas laterales que atravesaría.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto no está en la macro.</b> Ahí la cinta va del doblez lateral a la
    /// varilla central en línea recta, y si por ese camino hay una varilla lateral, le
    /// pasa por encima: en el dibujo la diagonal del diamante corta la varilla por la
    /// mitad. En obra eso no puede ser, porque el estribo no atraviesa el acero: lo
    /// rodea.
    /// </para>
    /// <para>
    /// La corrección es tratar esas varillas como <b>un círculo más del recorrido</b>.
    /// La cinta ya sabe abrazar una serie de círculos con dobleces redondeados —es
    /// para lo que existe—, así que basta insertarlas en el sitio correcto del
    /// recorrido y sale sola tangente a ellas.
    /// </para>
    /// <para>
    /// El orden importa y no es negociable: el recorrido tiene que seguir siendo
    /// antihorario y sin cruces, o la cinta sale hecha un nudo. Cada varilla se
    /// inserta <b>en el tramo que atraviesa</b> y, si hay varias en el mismo tramo, en
    /// el orden en que se las encuentra al recorrerlo.
    /// </para>
    /// <para>
    /// Si al final la cinta no se puede construir, se devuelve el recorrido de
    /// partida. Es mejor un diamante que cruza una varilla —lo que hacía antes— que
    /// ningún diamante.
    /// </para>
    /// </remarks>
    private static List<(double X, double Y, double R)> RodearLaterales(
        List<(double X, double Y, double R)> centros, double dDia,
        List<(double X, double Y, double R)> varLat, List<string>? notas)
    {
        if (varLat.Count == 0)
        {
            return centros;
        }

        var actual = centros;

        // Las que ya están en el recorrido no se vuelven a meter.
        var puestas = new List<(double X, double Y, double R)>();

        for (var pasada = 0; pasada < PasadasRodeo; pasada++)
        {
            var interior = Cinta(actual, 0);
            var exterior = Cinta(actual, dDia);

            if (interior is null || exterior is null)
            {
                break;
            }

            var siguiente = new List<(double X, double Y, double R)>();
            var metidas = 0;

            for (var i = 0; i < actual.Count; i++)
            {
                siguiente.Add(actual[i]);

                var j = (i + 1) % actual.Count;

                // Tramo recto que sale del círculo i y llega al i+1, por dentro y
                // por fuera de la cinta.
                var (ai, bi) = TramoRecto(interior.Value.Pts, i, j);
                var (ae, be) = TramoRecto(exterior.Value.Pts, i, j);

                var candidatas = new List<(double T, (double X, double Y, double R) V)>();

                foreach (var v in varLat)
                {
                    if (YaEsta(v, actual) || YaEsta(v, puestas))
                    {
                        continue;
                    }

                    // Se mira contra las DOS fronteras: la cinta atraviesa la varilla
                    // tanto si la corta su borde interior como el exterior.
                    if (DistanciaASegmento(v, ai, bi) >= v.R &&
                        DistanciaASegmento(v, ae, be) >= v.R)
                    {
                        continue;
                    }

                    candidatas.Add((Avance(v, ai, bi), v));
                }

                // En el orden en que se encuentran al recorrer el tramo.
                foreach (var (_, v) in candidatas.OrderBy(c => c.T))
                {
                    siguiente.Add(ConHolgura(v));
                    puestas.Add(v);
                    metidas++;
                }
            }

            if (metidas == 0)
            {
                // Ninguna varilla queda atravesada: ya está bien.
                return actual;
            }

            actual = siguiente;
        }

        // Se comprueba que el recorrido nuevo dé una cinta válida ANTES de quedárselo.
        if (Cinta(actual, 0) is null || Cinta(actual, dDia) is null)
        {
            notas?.Add(
                "Estribo diamante: no se pudo hacer que rodeara las varillas " +
                "laterales, así que se dibuja como en la macro, cruzándolas.");

            return centros;
        }

        if (actual.Count > centros.Count)
        {
            notas?.Add(
                $"Estribo diamante: rodea {actual.Count - centros.Count} varilla(s) " +
                "lateral(es) que quedaban en su camino. Esto no lo hace la macro: ahí " +
                "el diamante las atraviesa.");
        }

        return actual;
    }

    /// <summary>
    /// Elige las varillas de un lecho a las que se abraza el diamante.
    /// </summary>
    /// <remarks>
    /// Port de <c>SeleccionaVarillasCentro</c>. Si hay una varilla prácticamente en
    /// el eje, el diamante se abraza a <b>esa sola</b> y el vértice queda en punta.
    /// Si el eje cae entre dos, se abraza a <b>las dos</b> y el vértice sale plano,
    /// que es lo correcto cuando el número de varillas es par.
    /// </remarks>
    /// <param name="porY">
    /// Mide sobre la Y en lugar de la X. Sirve para los costados, donde las varillas
    /// se reparten en vertical y el eje que importa es el horizontal.
    /// </param>
    public static List<(double X, double Y, double R)> VarillasDelCentro(
        List<(double X, double Y, double R)> varillas, double cx, bool porY = false)
    {
        var vacio = new List<(double X, double Y, double R)>();

        if (varillas.Count == 0)
        {
            return vacio;
        }

        double Coord((double X, double Y, double R) v) => porY ? v.Y : v.X;

        // La más cercana al eje
        var mejor = 0;
        var dMejor = double.MaxValue;

        for (var i = 0; i < varillas.Count; i++)
        {
            var d = Math.Abs(Coord(varillas[i]) - cx);
            if (d < dMejor)
            {
                dMejor = d;
                mejor = i;
            }
        }

        var tol = Math.Max(TolCentroFactor * varillas[mejor].R, 1e-6);

        if (dMejor <= tol)
        {
            return new List<(double X, double Y, double R)> { varillas[mejor] };
        }

        // El eje cae entre dos: se toman la más cercana por cada lado.
        //
        // OJO CON EL 'Coord'. Aquí estaba el defecto que reportó el usuario: este
        // bloque leía 'varillas[i].X' a pelo, en lugar de la coordenada que toca. En
        // los lechos de arriba y abajo da igual, porque ahí el eje ES la X. Pero en
        // los COSTADOS se llama con porY: true y las varillas se reparten en
        // vertical: todas tienen prácticamente la misma X, así que comparar la X
        // contra una 'cx' que en realidad era la Y del centro no separaba nada.
        // Resultado: en un costado con número PAR de varillas —ninguna a media
        // altura— no se encontraba pareja y el doblez se iba al círculo ficticio o a
        // una sola varilla descentrada, con la otra atravesada por la cinta. Con
        // Coord() la regla de «las dos más centradas» vale igual para arriba, abajo,
        // izquierda y derecha.
        var izq = -1;
        var der = -1;
        var dIzq = double.MaxValue;
        var dDer = double.MaxValue;

        for (var i = 0; i < varillas.Count; i++)
        {
            var c = Coord(varillas[i]);

            if (c < cx)
            {
                if (cx - c < dIzq)
                {
                    dIzq = cx - c;
                    izq = i;
                }
            }
            else if (c - cx < dDer)
            {
                dDer = c - cx;
                der = i;
            }
        }

        if (izq >= 0 && der >= 0)
        {
            return new List<(double X, double Y, double R)> { varillas[izq], varillas[der] };
        }

        var uno = izq >= 0 ? izq : der;
        return uno >= 0
            ? new List<(double X, double Y, double R)> { varillas[uno] }
            : vacio;
    }

    /// <summary>
    /// Cinta cerrada <b>tangente</b> a una serie de círculos, con los dobleces
    /// redondeados.
    /// </summary>
    /// <param name="centros">Círculos a abrazar, en orden antihorario.</param>
    /// <param name="extra">
    /// Cuánto engrosar cada radio. Con <c>0</c> sale la cinta interior; con el
    /// diámetro del estribo, la exterior.
    /// </param>
    /// <remarks>
    /// Port de <c>CrearCintaConFillet</c>. La geometría, para cada par de círculos
    /// consecutivos:
    /// <list type="number">
    ///   <item>
    ///     Se busca la <b>tangente exterior común</b>. Su dirección normal se
    ///     obtiene girando el vector que une los centros un ángulo cuyo coseno es
    ///     <c>(r1 - r2) / d</c>: así la recta toca ambos círculos aunque tengan
    ///     radios distintos, que es el caso cuando el diamante abraza una varilla
    ///     del #3 y un doblez lateral de otro tamaño.
    ///   </item>
    ///   <item>
    ///     Cada círculo aporta <b>dos vértices</b>, donde llega la tangente
    ///     anterior y donde sale la siguiente, unidos por un arco. El arco se
    ///     expresa como <i>bulge</i> de la polilínea: <c>tan(barrido / 4)</c>.
    ///   </item>
    /// </list>
    /// </remarks>
    /// <summary>
    /// Vértices y curvaturas de la cinta, <b>solo el cálculo</b>, sin dibujar nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está separado del dibujo porque lo necesitan <b>dos</b> sitios: el que dibuja
    /// la cinta y el que recorta el estribo por debajo de ella. Y tienen que usar
    /// exactamente los mismos números, o el corte no caería sobre la línea dibujada
    /// sino un poco antes o un poco después, que es justo el defecto que se estaba
    /// arreglando.
    /// </para>
    /// <para>
    /// Los puntos salen en orden, dos por círculo: donde llega la tangente anterior y
    /// donde sale la siguiente. El arreglo son parejas X,Y seguidas.
    /// </para>
    /// </remarks>
    public static (double[] Pts, double[] Bulges)? Cinta(
        List<(double X, double Y, double R)> centros, double extra)
    {
        var n = centros.Count;
        if (n < 3)
        {
            return null;
        }

        var r = new double[n];
        for (var i = 0; i < n; i++)
        {
            r[i] = centros[i].R + extra;
            if (r[i] <= 0)
            {
                return null;
            }
        }

        // Normal de la tangente que sale del círculo i hacia el i+1
        var mx = new double[n];
        var my = new double[n];

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var dx = centros[j].X - centros[i].X;
            var dy = centros[j].Y - centros[i].Y;
            var d = Math.Sqrt((dx * dx) + (dy * dy));

            // Dos círculos en el mismo sitio: no hay tangente que valga
            if (d < 1e-7)
            {
                return null;
            }

            var ux = dx / d;
            var uy = dy / d;

            var cc = (r[i] - r[j]) / d;

            // Si un círculo cabe dentro del otro no existe tangente exterior. Se
            // recorta en lugar de propagar un NaN, que dibujaría basura.
            cc = Math.Clamp(cc, -0.999999, 0.999999);

            var ss = Math.Sqrt(1 - (cc * cc));

            mx[i] = (cc * ux) + (ss * uy);
            my[i] = (cc * uy) - (ss * ux);
        }

        // Dos vértices por círculo: llegada de la tangente previa y salida de la
        // siguiente. El bulge del arco va en el índice par.
        var pts = new double[4 * n];
        var bulges = new double[2 * n];

        for (var i = 0; i < n; i++)
        {
            var previo = (i + n - 1) % n;

            pts[(4 * i) + 0] = centros[i].X + (r[i] * mx[previo]);
            pts[(4 * i) + 1] = centros[i].Y + (r[i] * my[previo]);
            pts[(4 * i) + 2] = centros[i].X + (r[i] * mx[i]);
            pts[(4 * i) + 3] = centros[i].Y + (r[i] * my[i]);

            var aEntra = Math.Atan2(my[previo], mx[previo]);
            var aSale = Math.Atan2(my[i], mx[i]);

            var barrido = aSale - aEntra;
            while (barrido < 0)
            {
                barrido += 2 * Math.PI;
            }

            while (barrido >= 2 * Math.PI)
            {
                barrido -= 2 * Math.PI;
            }

            bulges[2 * i] = Math.Tan(barrido / 4);
            bulges[(2 * i) + 1] = 0;
        }

        return (pts, bulges);
    }
}
