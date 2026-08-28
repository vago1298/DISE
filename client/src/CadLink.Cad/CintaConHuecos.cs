namespace CadLink.Cad;

/// <summary>
/// Abre <b>huecos</b> en la cinta cerrada del estribo diamante, para que lo que le pasa
/// por encima se vea pasar por encima.
/// </summary>
/// <remarks>
/// <para>
/// La cinta del diamante es <b>una sola polilínea cerrada con bulges</b>: dos vértices por
/// círculo abrazado —la llegada y la salida de la tangente común—, con el arco del doblez
/// entre los dos y el tramo recto de la diagonal hasta el círculo siguiente. Eso significa
/// que <b>no se puede borrar un trozo</b>: hay que volver a montarla partida.
/// </para>
/// <para>
/// Por eso no sirve la maquinaria de recorte del estribo rectangular. <c>TramoEstribo</c>
/// solo sabe partir rectas horizontales o verticales, porque son las únicas que dibuja; las
/// diagonales del rombo no son ni una ni otra, y sus dobleces son arcos.
/// </para>
/// <para>
/// <b>Los arcos se recortan también, y no es un extra.</b> La primera versión de esto solo
/// abría hueco en los tramos rectos, con el argumento de que lo que se pidió recortar era
/// «la línea diagonal». La prueba de <c>tools/prueba-cinta-huecos</c> lo tumbó: en un
/// armado real la grapa cruza la cinta <b>justo en los dobleces</b>, y es lógico —una grapa
/// se amarra a varillas, y los dobleces del diamante están precisamente en las varillas que
/// abraza—. Recortar solo las rectas dejaba cuatro vértices metidos dentro del acero de la
/// grapa. Así que partir el arco y recalcular su bulge era el caso principal, no el raro.
/// </para>
/// <para>
/// <b>Vive fuera de <c>SeccionDrawer</c> y sin tocar COM a propósito</b>, igual que
/// <see cref="TrazoDiamante"/> y <see cref="TrazoGrapa"/>. Dos razones, y las dos han
/// costado errores antes en este proyecto: la vista previa necesita abrir los mismos huecos
/// que AutoCAD —si los calculara aparte, la pantalla enseñaría un diamante y el plano
/// otro—, y una geometría enredada con llamadas COM no se puede probar.
/// </para>
/// <para>
/// <b>Convenio de índices</b>, que es lo que más confunde al leer esto: con
/// <c>m = pts.Length / 2</c> vértices, el <b>segmento</b> <c>v</c> va del vértice <c>v</c>
/// al <c>v + 1</c> (dando la vuelta), y su curvatura es <c>bulges[v]</c>. Los segmentos de
/// índice <b>par</b> son los arcos de los dobleces y los de índice <b>impar</b> son los
/// tramos rectos de las diagonales. Un hueco se identifica por su segmento y por dónde
/// empieza y acaba a lo largo de él, de 0 a 1.
/// </para>
/// </remarks>
public static class CintaConHuecos
{
    /// <summary>Un hueco: en qué segmento y de dónde a dónde a lo largo de él.</summary>
    /// <param name="Segmento">Índice del segmento, del vértice a su siguiente.</param>
    /// <param name="S0">Dónde empieza el hueco, de 0 a 1 a lo largo del segmento.</param>
    /// <param name="S1">Dónde acaba.</param>
    public readonly record struct Hueco(int Segmento, double S0, double S1);

    /// <summary>Un trozo de la cinta que sobrevive, listo para dibujarse abierto.</summary>
    public readonly record struct Trozo(double[] Pts, double[] Bulges);

    /// <summary>
    /// Un segmento de la cinta, con su geometría de arco ya resuelta.
    /// </summary>
    /// <remarks>
    /// El bulge de AutoCAD es <c>tan(barrido / 4)</c>, con signo: positivo es en sentido
    /// contrario a las agujas del reloj. De ahí sale todo lo demás —radio, centro y ángulo
    /// de arranque—, y se resuelve <b>una sola vez por segmento</b> porque hace falta en
    /// cuatro sitios distintos y recalcularlo en cada uno es como se acaba con un signo
    /// distinto en cada cuenta.
    /// </remarks>
    private readonly record struct Seg(
        double Ax, double Ay, double Bx, double By,
        bool EsArco, double Cx, double Cy, double R,
        double AngA, double Theta, double Cuerda);

    private static Seg Segmento(double[] pts, double[] bulges, int v, int m)
    {
        var w = (v + 1) % m;

        var ax = pts[2 * v];
        var ay = pts[(2 * v) + 1];
        var bx = pts[2 * w];
        var by = pts[(2 * w) + 1];

        var dx = bx - ax;
        var dy = by - ay;
        var cuerda = Math.Sqrt((dx * dx) + (dy * dy));

        var bulge = v < bulges.Length ? bulges[v] : 0;

        if (Math.Abs(bulge) <= 1e-12 || cuerda < 1e-12)
        {
            return new Seg(ax, ay, bx, by, false, 0, 0, 0, 0, 0, cuerda);
        }

        // Barrido con signo, y de ahí el radio y el centro.
        var theta = 4 * Math.Atan(bulge);
        var medio = theta / 2;

        var seno = Math.Sin(medio);

        if (Math.Abs(seno) < 1e-12)
        {
            return new Seg(ax, ay, bx, by, false, 0, 0, 0, 0, 0, cuerda);
        }

        // Radio CON SIGNO: así el centro sale del lado correcto sin ningún apaño por
        // casos. Fue justo lo que se hizo mal una vez en TrazoDiamante.Muestrear, que
        // usaba el radio en valor absoluto más un arreglo para el lado, y los puntos se
        // salían del doblez hasta 0.74 cm.
        var rConSigno = cuerda / (2 * seno);
        var h = rConSigno * Math.Cos(medio);

        // La normal IZQUIERDA de la cuerda.
        var nx = -dy / cuerda;
        var ny = dx / cuerda;

        var cx = ((ax + bx) / 2) + (nx * h);
        var cy = ((ay + by) / 2) + (ny * h);

        return new Seg(
            ax, ay, bx, by, true, cx, cy, Math.Abs(rConSigno),
            Math.Atan2(ay - cy, ax - cx), theta, cuerda);
    }

    /// <summary>Un punto del segmento, con <paramref name="s"/> de 0 a 1.</summary>
    private static (double X, double Y) Punto(in Seg seg, double s)
    {
        if (!seg.EsArco)
        {
            return (seg.Ax + (s * (seg.Bx - seg.Ax)), seg.Ay + (s * (seg.By - seg.Ay)));
        }

        var ang = seg.AngA + (s * seg.Theta);

        return (seg.Cx + (seg.R * Math.Cos(ang)), seg.Cy + (seg.R * Math.Sin(ang)));
    }

    /// <summary>El bulge del pedazo de <paramref name="s0"/> a <paramref name="s1"/>.</summary>
    /// <remarks>
    /// Un pedazo de arco barre la parte proporcional del barrido total, así que su bulge es
    /// <c>tan(barrido · (s1 − s0) / 4)</c>. Esto es lo que deja el doblez con la misma
    /// curvatura al partirlo: si se conservara el bulge entero, el pedazo que sobrevive
    /// saldría abombado.
    /// </remarks>
    private static double BulgeParcial(in Seg seg, double s0, double s1) =>
        seg.EsArco ? Math.Tan(seg.Theta * (s1 - s0) / 4) : 0;

    /// <summary>Largo del pedazo de <paramref name="s0"/> a <paramref name="s1"/>.</summary>
    private static double Largo(in Seg seg, double s0, double s1) =>
        seg.EsArco
            ? seg.R * Math.Abs(seg.Theta) * (s1 - s0)
            : seg.Cuerda * (s1 - s0);

    /// <summary>
    /// Qué parte del segmento <b>recto</b> <c>a→b</c> cae dentro del polígono.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mismo método que ya usa el recorte del estribo rectangular
    /// —<c>SeccionDrawer.DentroDelPoligono</c>—, pero <b>sin suponer que el segmento es
    /// horizontal o vertical</b>. Ahí se podía resolver el cruce con una regla de tres
    /// porque una de las dos coordenadas era constante; aquí hay que cruzar dos segmentos
    /// cualesquiera, así que se usa el producto cruzado.
    /// </para>
    /// <para>
    /// Se corta el segmento en todos los puntos donde cruza un lado del polígono y se
    /// decide trozo a trozo <b>probando el punto medio</b>. No es un detalle de estilo:
    /// sostiene el resultado aunque el polígono sea cóncavo, y el contorno de una grapa lo
    /// es —tiene la escotadura entre las dos colas—. Un método de «recortar contra
    /// semiplanos» daría ahí un resultado equivocado sin avisar.
    /// </para>
    /// </remarks>
    public static List<(double S0, double S1)> DentroDelPoligono(
        double ax, double ay, double bx, double by, double[] poligono, double minimo)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        var seg = new Seg(ax, ay, bx, by, false, 0, 0, 0, 0, 0, largo);

        return DentroDelPoligono(seg, poligono, minimo);
    }

    private static List<(double S0, double S1)> DentroDelPoligono(
        in Seg seg, double[] poligono, double minimo)
    {
        var dentro = new List<(double S0, double S1)>();

        var n = poligono.Length / 2;

        if (n < 3 || seg.Cuerda < 1e-12)
        {
            return dentro;
        }

        var cortes = new List<double> { 0.0, 1.0 };

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var px = poligono[2 * i];
            var py = poligono[(2 * i) + 1];
            var qx = poligono[2 * j];
            var qy = poligono[(2 * j) + 1];

            if (seg.EsArco)
            {
                CortesDelArco(seg, px, py, qx, qy, cortes);
            }
            else
            {
                CorteDeLaRecta(seg, px, py, qx, qy, cortes);
            }
        }

        cortes.Sort();

        for (var k = 0; k + 1 < cortes.Count; k++)
        {
            var medio = (cortes[k] + cortes[k + 1]) / 2;

            var (mx, my) = Punto(seg, medio);

            if (!PuntoEnPoligono(mx, my, poligono))
            {
                continue;
            }

            if (Largo(seg, cortes[k], cortes[k + 1]) > minimo)
            {
                dentro.Add((cortes[k], cortes[k + 1]));
            }
        }

        return dentro;
    }

    /// <summary>Cruce de un segmento recto con un lado del polígono.</summary>
    private static void CorteDeLaRecta(
        in Seg seg, double px, double py, double qx, double qy, List<double> cortes)
    {
        var dx = seg.Bx - seg.Ax;
        var dy = seg.By - seg.Ay;

        var ex = qx - px;
        var ey = qy - py;

        var cruz = (dx * ey) - (dy * ex);

        if (Math.Abs(cruz) < 1e-15)
        {
            // Lado paralelo al segmento: no aporta ningún corte.
            return;
        }

        var rx = px - seg.Ax;
        var ry = py - seg.Ay;

        var s = ((rx * ey) - (ry * ex)) / cruz;
        var t = ((rx * dy) - (ry * dx)) / cruz;

        if (t < 0 || t > 1 || s <= 0 || s >= 1)
        {
            return;
        }

        cortes.Add(s);
    }

    /// <summary>
    /// Cruces de un <b>arco</b> con un lado del polígono.
    /// </summary>
    /// <remarks>
    /// Se corta el lado contra la <b>circunferencia completa</b> —una cuadrática— y de las
    /// dos soluciones se queda con las que caen dentro del lado y <b>dentro del barrido del
    /// arco</b>. Mirar solo la circunferencia daría cortes en la parte del círculo por la
    /// que el doblez no pasa.
    /// </remarks>
    private static void CortesDelArco(
        in Seg seg, double px, double py, double qx, double qy, List<double> cortes)
    {
        var ex = qx - px;
        var ey = qy - py;

        var a = (ex * ex) + (ey * ey);

        if (a < 1e-18)
        {
            return;
        }

        var fx = px - seg.Cx;
        var fy = py - seg.Cy;

        var b = 2 * ((fx * ex) + (fy * ey));
        var c = (fx * fx) + (fy * fy) - (seg.R * seg.R);

        var disc = (b * b) - (4 * a * c);

        if (disc <= 0)
        {
            return;
        }

        var raiz = Math.Sqrt(disc);

        foreach (var t in new[] { (-b - raiz) / (2 * a), (-b + raiz) / (2 * a) })
        {
            if (t < 0 || t > 1)
            {
                continue;
            }

            var x = px + (t * ex);
            var y = py + (t * ey);

            var s = ParametroEnElArco(seg, x, y);

            if (s > 0 && s < 1)
            {
                cortes.Add(s);
            }
        }
    }

    /// <summary>De un punto de la circunferencia al parámetro 0..1 del arco.</summary>
    private static double ParametroEnElArco(in Seg seg, double x, double y)
    {
        if (Math.Abs(seg.Theta) < 1e-15)
        {
            return -1;
        }

        var d = Math.Atan2(y - seg.Cy, x - seg.Cx) - seg.AngA;

        // El desfase se lleva al sentido en que barre el arco: hacia delante si el
        // barrido es positivo y hacia atrás si es negativo. Sin esto, un corte que cae
        // pasado el origen de ángulos sale con el parámetro de la vuelta contraria.
        if (seg.Theta > 0)
        {
            while (d < 0)
            {
                d += 2 * Math.PI;
            }

            while (d >= 2 * Math.PI)
            {
                d -= 2 * Math.PI;
            }
        }
        else
        {
            while (d > 0)
            {
                d -= 2 * Math.PI;
            }

            while (d <= -2 * Math.PI)
            {
                d += 2 * Math.PI;
            }
        }

        return d / seg.Theta;
    }

    /// <summary>¿El punto está dentro del polígono? Por conteo de cruces.</summary>
    public static bool PuntoEnPoligono(double px, double py, double[] poligono)
    {
        var n = poligono.Length / 2;
        var dentro = false;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var xi = poligono[2 * i];
            var yi = poligono[(2 * i) + 1];
            var xj = poligono[2 * j];
            var yj = poligono[(2 * j) + 1];

            if ((yi > py) != (yj > py) &&
                px < (((xj - xi) * (py - yi) / (yj - yi)) + xi))
            {
                dentro = !dentro;
            }
        }

        return dentro;
    }

    /// <summary>
    /// Los huecos que unos polígonos abren en la cinta, rectas <b>y</b> arcos.
    /// </summary>
    public static List<Hueco> Huecos(
        double[] pts,
        double[] bulges,
        IReadOnlyList<double[]> poligonos,
        double minimo)
    {
        var huecos = new List<Hueco>();
        var m = pts.Length / 2;

        if (m < 3 || bulges.Length < m || poligonos.Count == 0)
        {
            return huecos;
        }

        for (var v = 0; v < m; v++)
        {
            var seg = Segmento(pts, bulges, v, m);

            foreach (var poly in poligonos)
            {
                foreach (var (s0, s1) in DentroDelPoligono(seg, poly, minimo))
                {
                    huecos.Add(new Hueco(v, s0, s1));
                }
            }
        }

        return huecos;
    }

    /// <summary>
    /// Vuelve a montar la cinta cerrada en <b>trozos abiertos</b>, saltándose los huecos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La idea.</b> Los huecos se colocan sobre el anillo en una sola coordenada
    /// continua —<c>segmento + s</c>, que va de 0 a <c>m</c> y da la vuelta—, se ordenan y
    /// se fusionan los que se solapan. Lo que sobrevive es lo que queda <b>entre</b> el
    /// final de un hueco y el principio del siguiente, dando la vuelta. Con un solo hueco
    /// sale un solo trozo que recorre la cinta casi entera, que es justo lo que hacía la
    /// versión anterior a mano para el gancho; con varios salen varios, sin ningún caso
    /// especial.
    /// </para>
    /// <para>
    /// <b>Los arcos que sobreviven enteros conservan su bulge</b>, y los que se parten
    /// reciben el bulge del barrido que les queda. Así el doblez del rombo no se deforma en
    /// ningún caso: ni cuando el hueco lo esquiva ni cuando lo corta por la mitad.
    /// </para>
    /// <para>
    /// <b>El seguro.</b> Si los huecos suman más de <paramref name="fraccionMax"/> del
    /// contorno, no se devuelve nada y el que llama deja la cinta cerrada como estaba. Una
    /// cuenta equivocada tiene que salir como una línea cruzada, que se ve y se reporta, y
    /// no como media diagonal borrada, que parece un dibujo correcto.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Los trozos que sobreviven, o <c>null</c> si no hay que tocar nada —sin huecos— o si
    /// el seguro salta.
    /// </returns>
    public static List<Trozo>? Abrir(
        double[] pts,
        double[] bulges,
        IEnumerable<Hueco> huecos,
        double minimo,
        double fraccionMax)
    {
        var m = pts.Length / 2;

        if (m < 3 || bulges.Length < m)
        {
            return null;
        }

        var segs = new Seg[m];

        for (var v = 0; v < m; v++)
        {
            segs[v] = Segmento(pts, bulges, v, m);
        }

        // ---------- Los huecos, sobre el anillo y fusionados ----------
        var rango = new List<(double Ini, double Fin)>();

        foreach (var hueco in huecos)
        {
            if (hueco.Segmento < 0 || hueco.Segmento >= m)
            {
                continue;
            }

            var s0 = Math.Clamp(Math.Min(hueco.S0, hueco.S1), 0.0, 1.0);
            var s1 = Math.Clamp(Math.Max(hueco.S0, hueco.S1), 0.0, 1.0);

            if (s1 - s0 <= 0)
            {
                continue;
            }

            rango.Add((hueco.Segmento + s0, hueco.Segmento + s1));
        }

        if (rango.Count == 0)
        {
            return null;
        }

        rango.Sort((p, q) => p.Ini.CompareTo(q.Ini));

        var union = new List<(double Ini, double Fin)> { rango[0] };

        for (var i = 1; i < rango.Count; i++)
        {
            var ultimo = union[^1];

            if (rango[i].Ini <= ultimo.Fin + 1e-12)
            {
                if (rango[i].Fin > ultimo.Fin)
                {
                    union[^1] = (ultimo.Ini, rango[i].Fin);
                }
            }
            else
            {
                union.Add(rango[i]);
            }
        }

        // El primero y el último pueden tocarse dando la vuelta por el vértice 0.
        if (union.Count > 1
            && union[^1].Fin >= m - 1e-12
            && union[0].Ini <= 1e-12)
        {
            union[0] = (union[^1].Ini - m, union[0].Fin);
            union.RemoveAt(union.Count - 1);
        }

        // ---------- El seguro ----------
        var contorno = 0.0;

        for (var v = 0; v < m; v++)
        {
            contorno += Largo(segs[v], 0, 1);
        }

        if (contorno < 1e-12)
        {
            return null;
        }

        var tapado = union.Sum(u => LargoEnElAnillo(segs, u.Ini, u.Fin, m));

        if (tapado > fraccionMax * contorno)
        {
            return null;
        }

        // ---------- Los trozos que sobreviven ----------
        var trozos = new List<Trozo>();

        for (var i = 0; i < union.Count; i++)
        {
            var trozo = Armar(
                segs, bulges, m,
                union[i].Fin,
                union[(i + 1) % union.Count].Ini,
                minimo);

            if (trozo is not null)
            {
                trozos.Add(trozo.Value);
            }
        }

        return trozos.Count > 0 ? trozos : null;
    }

    /// <summary>Arma un trozo abierto, del final de un hueco al principio del siguiente.</summary>
    private static Trozo? Armar(
        Seg[] segs, double[] bulges, int m, double desde, double hasta, double minimo)
    {
        var recorrido = hasta - desde;

        while (recorrido <= 0)
        {
            recorrido += m;
        }

        // ---------- Los extremos, normalizados ----------
        //
        // Un extremo que cae EXACTAMENTE en un vértice se puede escribir de dos maneras
        // —final del segmento anterior o principio del siguiente— y las dos dan el mismo
        // punto pero NO el mismo bulge. Se normalizan para que el arranque quede siempre
        // en [0,1) y el final en (0,1]; con eso el último vértice del recorrido es siempre
        // el segmento del final, y no hace falta ningún caso aparte.
        var segIni = (int)Math.Floor(desde);
        var sIni = desde - segIni;

        if (sIni > 1 - 1e-12)
        {
            segIni++;
            sIni = 0;
        }

        var fin = desde + recorrido;

        var segFin = (int)Math.Floor(fin);
        var sFin = fin - segFin;

        if (sFin < 1e-12)
        {
            segFin--;
            sFin = 1;
        }

        var vIni = ((segIni % m) + m) % m;
        var vFin = ((segFin % m) + m) % m;

        var puntos = new List<double>();
        var bul = new List<double>();

        var (ax, ay) = Punto(segs[vIni], sIni);
        puntos.Add(ax);
        puntos.Add(ay);

        // Los vértices enteros que quedan entre el arranque y el final.
        var vertices = new List<int>();

        for (var t = segIni + 1; t <= segFin; t++)
        {
            vertices.Add(((t % m) + m) % m);
        }

        if (vertices.Count == 0)
        {
            // El trozo entero cae dentro de un solo segmento: dos puntos y su bulge.
            bul.Add(BulgeParcial(segs[vIni], sIni, sFin));
        }
        else
        {
            // Del arranque al primer vértice: la cola del segmento donde empieza.
            bul.Add(BulgeParcial(segs[vIni], sIni, 1));

            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];

                puntos.Add(segs[v].Ax);
                puntos.Add(segs[v].Ay);

                bul.Add(i == vertices.Count - 1
                    // Del último vértice al final: la cabeza de su segmento.
                    ? BulgeParcial(segs[vFin], 0, sFin)
                    // De un vértice al siguiente: el segmento entero, tal cual.
                    : bulges[v]);
            }
        }

        var (bx, by) = Punto(segs[vFin], sFin);
        puntos.Add(bx);
        puntos.Add(by);

        // El último punto no tiene segmento que le siga.
        bul.Add(0);

        if (LargoEnElAnillo(segs, desde, hasta, m) <= minimo)
        {
            // Un trozo más corto que el mínimo no se dibuja: sería una entidad invisible
            // que solo estorba al editar el plano.
            return null;
        }

        return new Trozo(puntos.ToArray(), bul.ToArray());
    }

    /// <summary>Largo de un tramo del anillo, entre dos posiciones continuas.</summary>
    /// <remarks>
    /// Se avanza cortando en cada frontera de segmento: sumar de golpe mezclaría segmentos
    /// de largos distintos —y arcos con rectas— en una sola regla de tres.
    /// </remarks>
    private static double LargoEnElAnillo(Seg[] segs, double ini, double fin, int m)
    {
        var recorrido = fin - ini;

        while (recorrido <= 0)
        {
            recorrido += m;
        }

        var total = 0.0;
        var paso = 0.0;

        while (paso < recorrido - 1e-12)
        {
            var pos = ini + paso;

            while (pos < 0)
            {
                pos += m;
            }

            var v = (((int)Math.Floor(pos) % m) + m) % m;
            var dentro = pos - Math.Floor(pos);

            var queda = Math.Min(1 - dentro, recorrido - paso);

            if (queda <= 1e-15)
            {
                break;
            }

            total += Largo(segs[v], dentro, dentro + queda);
            paso += queda;
        }

        return total;
    }
}
