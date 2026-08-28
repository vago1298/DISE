namespace CadLink.Cad;

/// <summary>
/// Estribo diamante: la parte de <see cref="SeccionDrawer"/> que dibuja el rombo
/// interior, columnas R y S de la hoja.
/// </summary>
/// <remarks>
/// Port de <c>DibujarEstriboDiamante</c> y <c>CrearCintaConFillet</c> de la macro.
/// <para>
/// La idea del original: el diamante no es un rombo geométrico, es una <b>cinta
/// cerrada tangente a cuatro grupos de círculos</b> —la varilla central de arriba,
/// la de abajo, y dos círculos ficticios a izquierda y derecha—. Así el estribo
/// abraza de verdad las varillas que sujeta, con los dobleces redondeados y no en
/// pico, que es como se arma en obra.
/// </para>
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>Contornos del diamante de la sección en curso, para las islas.</summary>
    /// <remarks>
    /// Quedan en <c>null</c> cuando una grapa o el gancho les abren huecos: entonces la
    /// cinta se sustituye por sus trozos. Para saber si la sección lleva diamante está
    /// <see cref="_hayDiamante"/>, que no depende de eso.
    /// </remarks>
    private object? _diamExt;
    private object? _diamInt;

    /// <summary>Si la sección en curso llegó a dibujar el diamante.</summary>
    private bool _hayDiamante;

    /// <summary>
    /// El hueco que el gancho del diamante abre en su propia cinta interior.
    /// </summary>
    /// <remarks>
    /// Se calcula al dibujar el gancho pero <b>no se aplica ahí</b>: se junta con los que
    /// abren las grapas y se rearma la cinta UNA sola vez. Abrirla dos veces obligaría a
    /// partir una polilínea ya abierta, que es un caso distinto y peor.
    /// </remarks>
    private CintaConHuecos.Hueco? _huecoDelGancho;

    /// <summary>
    /// Dibuja el estribo diamante y lo deja listo para usarse como isla del hatch.
    /// </summary>
    private void EstriboDiamante(
        SeccionCad s, List<object> contorno,
        double x0, double y0, double b, double h, double rec,
        double dEstPrincipal, bool conFondoSolido)
    {
        _diamExt = null;
        _diamInt = null;

        var dDia = s.EstriboDiamanteVar.Cm * _escala;

        // Sin diámetro propio se usa el del estribo principal, como la macro
        if (dDia <= 0)
        {
            dDia = dEstPrincipal;
        }

        if (dDia <= 0)
        {
            return;
        }

        var x1 = x0 + rec;
        var y1 = y0 + rec;
        var x2 = x0 + b - rec;
        var y2 = y0 + h - rec;

        if (x2 <= x1 || y2 <= y1)
        {
            return;
        }

        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;

        // ---------- El recorrido de círculos que abraza ----------
        // La geometría vive en TrazoDiamante, que no sabe de AutoCAD: los radios de los
        // dobleces, a qué varillas se abraza cada vértice y las laterales que hay que
        // rodear. Está fuera de aquí porque la VISTA PREVIA de la pestaña de concreto
        // necesita exactamente el mismo recorrido, y dos copias de un cálculo acaban
        // enseñando dos diamantes distintos.
        // Las notas se le pasan en una lista, porque TrazoDiamante no puede escribir en el
        // registro del dibujante: no sabe que existe. Y hay dos que hay que decir —cuántas
        // varillas laterales acabó rodeando, y si no pudo—, así que se recogen y se pasan
        // al registro con Nota, que es la que quita las repetidas.
        var notas = new List<string>();

        var centros = TrazoDiamante.Centros(
            x1, y1, x2, y2, dDia, _varSup, _varInf, _varLat, notas);

        foreach (var n in notas)
        {
            Nota(n);
        }

        if (centros is null)
        {
            _log.Add(
                "Estribo diamante: no se pudo armar el recorrido de círculos del rombo. " +
                $"Núcleo de {(x2 - x1) / _escala:0.#} x {(y2 - y1) / _escala:0.#} cm.");
            return;
        }

        // ---------- Las dos cintas ----------
        var interior = CintaTangente(centros, 0);
        var exterior = CintaTangente(centros, dDia);

        if (interior is null || exterior is null)
        {
            _log.Add(
                "Estribo diamante: no se pudo construir la cinta tangente. " +
                $"Circulos: {centros.Count}.");

            Borrar(interior);
            Borrar(exterior);
            return;
        }

        _diamInt = interior;
        _diamExt = exterior;
        _hayDiamante = true;

        // ---------- Relleno y color ----------
        if (conFondoSolido)
        {
            // Relleno sólido del cuerpo del diamante, con la cinta interior como
            // isla: queda la banda del acero, no un rombo macizo.
            var relleno = Hatch(
                "SOLID", 1, exterior, new List<object> { interior },
                "ESTRIBOS", ColorRellenoEstribo);

            if (relleno is not null)
            {
                // Al fondo, junto con los demás rellenos, para no tapar el acero
                AlFondo(new List<object> { relleno });
            }

            Negro(interior);
            Negro(exterior);
        }

        AlFrente(new List<object> { interior, exterior });

        // ---------- Gancho sísmico del diamante ----------
        // Va al FINAL, cuando la cinta ya está dibujada y coloreada: el gancho es una
        // pieza aparte que se le añade, igual que en el zuncho circular.
        GanchoDelDiamante(s, contorno, centros, cx, cy, dDia, conFondoSolido);

        // ---------- Las cintas, abiertas por donde algo les pasa por encima ----------
        // El gancho apuntó su hueco al dibujarse y las grapas dejaron sus contornos, así
        // que aquí se juntan todos y las dos cintas se rearman UNA vez.
        RearmarLasCintas(centros, dDia, conFondoSolido);

        // Y AL FINAL, con la cinta ya hecha: se abre el estribo principal por donde
        // el diamante pasa por encima. Va aquí y no antes porque solo tiene sentido
        // si la cinta se construyó de verdad; si hubiera fallado, recortar el
        // estribo lo dejaría abierto sin nada que tape el hueco.
        RecortarEstriboBajoDiamante(contorno, centros, dDia);
    }

    /// <summary>
    /// Abre el estribo principal por donde el diamante pasa por encima.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Qué se ve sin esto.</b> El diamante se dobla sobre la varilla central y en
    /// ese doblez monta sobre el estribo principal. Pero la línea horizontal del
    /// estribo seguía dibujada de lado a lado, así que <b>cruzaba el diamante por
    /// dentro</b>: en el plano parecía que la varilla del estribo atravesaba el
    /// diamante, en lugar de pasar por debajo. Es el <c>TrimEstriboBajoDiamante</c>
    /// de la macro.
    /// </para>
    /// <para>
    /// <b>Por qué no se resuelve con el orden de dibujo.</b> Poner el relleno del
    /// diamante encima taparía la línea, sí, pero solo en la sección <i>rellena</i>.
    /// En la sección tipo 1 no hay relleno que tape nada y el defecto seguiría. Hay
    /// que abrir el hueco de verdad.
    /// </para>
    /// <para>
    /// <b>Cómo se decide qué se tapa, y por qué es seguro.</b> No hay ninguna prueba
    /// de «está dentro del polígono»: eso es justo lo que no se quería, porque
    /// equivocarse borraría el estribo. Lo que se hace es geometría cerrada. El borde
    /// exterior de la cinta, alrededor de cada círculo que abraza, es un arco de
    /// radio <c>R + dDia</c> centrado en ese círculo. Un tramo recto a distancia
    /// perpendicular <c>p</c> de ese centro queda tapado en un ancho de
    /// <c>±√(radio² − p²)</c>. Es una fórmula, no una estimación.
    /// </para>
    /// <para>
    /// Y encima lleva tres seguros: el tramo original <b>solo se borra si los trozos
    /// nuevos ya se dibujaron</b>; si el hueco saliera mayor que
    /// <see cref="FraccionMaxRecorte"/> del tramo se deja el tramo intacto y se
    /// avisa, porque eso significaría que la cuenta está mal; y solo se tocan las
    /// entidades que este dibujo creó, nunca lo que hubiera antes en el plano.
    /// </para>
    /// </remarks>
    private void RecortarEstriboBajoDiamante(
        List<object> contorno,
        List<(double X, double Y, double R)> centros,
        double dDia)
    {
        // Se recorre una copia: la lista se modifica dentro del bucle.
        foreach (var tramo in _tramosEstribo.ToList())
        {
            var largo = tramo.B - tramo.A;
            if (largo <= LargoMinTramo)
            {
                continue;
            }

            var tapado = TramoTapadoPorLaCinta(tramo, centros, dDia, LargoMinTramo);
            if (tapado.Count == 0)
            {
                continue;
            }

            var suma = tapado.Sum(i => i.Fin - i.Ini);

            // El seguro contra una cuenta equivocada. El caso real tapa menos del
            // 15% del tramo; si sale mucho más, algo está mal y es mejor un dibujo
            // con la línea cruzada que un estribo borrado.
            if (suma > FraccionMaxRecorte * largo)
            {
                Nota(
                    "Estribo diamante: no se recortó un tramo del estribo porque el " +
                    $"hueco calculado tapaba el {100 * suma / largo:0} % del tramo. " +
                    "El dibujo queda completo, con la línea del estribo cruzando el " +
                    "diamante.");
                continue;
            }

            // Lo que queda del tramo: los huecos en negativo.
            var trozos = new List<(double A, double B)>();
            var cursor = tramo.A;

            foreach (var (ini, fin) in tapado)
            {
                if (ini > cursor)
                {
                    trozos.Add((cursor, ini));
                }

                cursor = Math.Max(cursor, fin);
            }

            if (cursor < tramo.B)
            {
                trozos.Add((cursor, tramo.B));
            }

            // PRIMERO se dibujan los trozos nuevos...
            var nuevos = new List<object>();

            foreach (var (a, bb) in trozos)
            {
                if (bb - a < LargoMinTramo)
                {
                    continue;
                }

                var linea = tramo.Horizontal
                    ? Linea(a, tramo.Fijo, bb, tramo.Fijo, "ESTRIBOS")
                    : Linea(tramo.Fijo, a, tramo.Fijo, bb, "ESTRIBOS");

                if (linea is not null)
                {
                    nuevos.Add(linea);
                }
            }

            // ...y el original se borra SOLO si los trozos se crearon. Al revés, un
            // fallo al dibujar dejaría el estribo abierto de lado a lado.
            if (nuevos.Count == 0)
            {
                Nota(
                    "Estribo diamante: no se pudo redibujar un tramo recortado del " +
                    "estribo, así que se dejó el tramo entero.");
                continue;
            }

            Borrar(tramo.Ent);
            contorno.Remove(tramo.Ent);
            contorno.AddRange(nuevos);
            _tramosEstribo.Remove(tramo);
        }
    }

    /// <summary>
    /// Partes del tramo que caen <b>dentro</b> del polígono de puntos de tangencia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se corta el tramo en los puntos donde cruza los lados del polígono y se
    /// decide, trozo a trozo, si está dentro o fuera <b>probando el punto medio</b>.
    /// Así se sostiene aunque el polígono no sea convexo, que puede pasar si el
    /// diamante abraza dos varillas muy juntas: un método basado en «recortar contra
    /// semiplanos» daría un resultado silenciosamente equivocado en ese caso.
    /// </para>
    /// <para>
    /// El tramo es siempre horizontal o vertical, así que el cruce con cada lado sale
    /// de una regla de tres, sin resolver ningún sistema.
    /// </para>
    /// </remarks>
    private static List<(double Ini, double Fin)> DentroDelPoligono(
        TramoEstribo tramo, double[] pts)
    {
        var trozos = new List<(double Ini, double Fin)>();

        // pts son parejas X,Y seguidas: dos vértices por círculo.
        var n = pts.Length / 2;
        if (n < 3)
        {
            return trozos;
        }

        var cortes = new List<double> { tramo.A, tramo.B };

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var ax = pts[2 * i];
            var ay = pts[(2 * i) + 1];
            var bx = pts[2 * j];
            var by = pts[(2 * j) + 1];

            // Se cruza el lado con la recta del tramo. 's' recorre el lado.
            var de = tramo.Horizontal ? by - ay : bx - ax;

            if (Math.Abs(de) < 1e-15)
            {
                // Lado paralelo al tramo: no aporta ningún corte.
                continue;
            }

            var s = ((tramo.Horizontal ? tramo.Fijo - ay : tramo.Fijo - ax)) / de;

            if (s < 0 || s > 1)
            {
                continue;
            }

            var donde = tramo.Horizontal
                ? ax + (s * (bx - ax))
                : ay + (s * (by - ay));

            if (donde > tramo.A && donde < tramo.B)
            {
                cortes.Add(donde);
            }
        }

        cortes.Sort();

        for (var k = 0; k + 1 < cortes.Count; k++)
        {
            var medio = (cortes[k] + cortes[k + 1]) / 2;

            var px = tramo.Horizontal ? medio : tramo.Fijo;
            var py = tramo.Horizontal ? tramo.Fijo : medio;

            if (PuntoEnPoligono(px, py, pts))
            {
                trozos.Add((cortes[k], cortes[k + 1]));
            }
        }

        return trozos;
    }

    /// <summary>¿El punto está dentro del polígono? Por conteo de cruces.</summary>
    private static bool PuntoEnPoligono(double px, double py, double[] pts)
    {
        var n = pts.Length / 2;
        var dentro = false;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var xi = pts[2 * i];
            var yi = pts[(2 * i) + 1];
            var xj = pts[2 * j];
            var yj = pts[(2 * j) + 1];

            if ((yi > py) != (yj > py) &&
                px < (((xj - xi) * (py - yi) / (yj - yi)) + xi))
            {
                dentro = !dentro;
            }
        }

        return dentro;
    }

    /// <summary>Fracción del tramo que como máximo se acepta recortar.</summary>
    /// <remarks>
    /// El caso real ronda el 10%. El 60% es holgado a propósito: no está para
    /// afinar nada, está para que una cuenta equivocada no borre el estribo.
    /// </remarks>
    private const double FraccionMaxRecorte = 0.6;

    /// <summary>
    /// Intervalos del tramo que quedan bajo la cinta del diamante, ya fusionados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La región que encierra el borde exterior de la cinta es <b>exactamente</b> la
    /// unión de dos cosas:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Los <b>discos</b> de radio <c>R + dDia</c> centrados en cada círculo que
    ///     el diamante abraza. Cubren los dobleces.
    ///   </item>
    ///   <item>
    ///     El <b>polígono</b> que pasa por los puntos de tangencia. Cubre los tramos
    ///     rectos entre doblez y doblez.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Aquí estaba el defecto.</b> La primera versión solo miraba los discos, y
    /// con eso el corte funcionaba donde el diamante se dobla, pero <b>no donde el
    /// diamante va recto</b>. Las diagonales del diamante cruzan las líneas
    /// verticales del estribo lejos de cualquier doblez, así que ahí no se cortaba
    /// nada: la línea del estribo seguía entera atravesando la diagonal, y encima el
    /// trozo recortado por el disco quedaba terminando en el aire. Es exactamente lo
    /// que el usuario reportó: <i>«aún hay líneas que no se cortan en la
    /// intersección del estribo de diamante»</i>.
    /// </para>
    /// <para>
    /// El polígono se saca de <see cref="GeometriaCinta"/>, la misma función que
    /// dibuja la cinta. Usar los mismos números es lo que garantiza que el corte
    /// caiga <b>sobre</b> la línea dibujada y no un poco antes.
    /// </para>
    /// </remarks>
    private static List<(double Ini, double Fin)> TramoTapadoPorLaCinta(
        TramoEstribo tramo,
        List<(double X, double Y, double R)> centros,
        double dDia,
        double minimo)
    {
        var brutos = new List<(double Ini, double Fin)>();

        // ---- Los tramos RECTOS de la cinta, por el polígono de tangencias ----
        var geo = GeometriaCinta(centros, dDia);

        if (geo is not null)
        {
            foreach (var (ini, fin) in DentroDelPoligono(tramo, geo.Value.Pts))
            {
                if (fin - ini > minimo)
                {
                    brutos.Add((ini, fin));
                }
            }
        }

        foreach (var c in centros)
        {
            var radio = c.R + dDia;

            // Distancia del tramo al centro, medida perpendicular al tramo
            var perp = tramo.Horizontal ? tramo.Fijo - c.Y : tramo.Fijo - c.X;

            var w2 = (radio * radio) - (perp * perp);
            if (w2 <= 0)
            {
                // El tramo pasa fuera de la cinta por este círculo
                continue;
            }

            var w = Math.Sqrt(w2);
            var centro = tramo.Horizontal ? c.X : c.Y;

            var ini = Math.Max(tramo.A, centro - w);
            var fin = Math.Min(tramo.B, centro + w);

            // Un hueco más angosto que el mínimo NO cuenta, y este filtro no es un
            // detalle: cuando el diamante es del mismo calibre que el estribo, su
            // cinta queda EXACTAMENTE tangente a la línea exterior. En números
            // reales w2 sale 0; en coma flotante sale 1e-19, así que se colaba un
            // hueco de ancho cero y el tramo se borraba para redibujarlo partido en
            // tres trozos pegados. El dibujo se veía igual, pero la línea del
            // estribo quedaba troceada y al editarla en AutoCAD aparecía el
            // desastre. Lo encontró la comprobación numérica en Python, no la
            // lectura del código.
            if (fin - ini > minimo)
            {
                brutos.Add((ini, fin));
            }
        }

        if (brutos.Count == 0)
        {
            return brutos;
        }

        // Se fusionan los que se solapan, para no cortar dos veces en el mismo sitio.
        // Aquí es imprescindible: un disco y un tramo recto se solapan casi siempre,
        // porque comparten el punto de tangencia.
        brutos.Sort((p, q) => p.Ini.CompareTo(q.Ini));

        var union = new List<(double Ini, double Fin)> { brutos[0] };

        for (var i = 1; i < brutos.Count; i++)
        {
            var ultimo = union[^1];

            if (brutos[i].Ini <= ultimo.Fin)
            {
                if (brutos[i].Fin > ultimo.Fin)
                {
                    union[^1] = (ultimo.Ini, brutos[i].Fin);
                }
            }
            else
            {
                union.Add(brutos[i]);
            }
        }

        return union;
    }




    /// <summary>
    /// Elige las varillas de un lecho a las que se abraza el diamante. Vive en
    /// <see cref="TrazoDiamante.VarillasDelCentro"/>.
    /// </summary>
    private static List<(double X, double Y, double R)> VarillasDelCentro(
        List<(double X, double Y, double R)> varillas, double cx, bool porY = false) =>
        TrazoDiamante.VarillasDelCentro(varillas, cx, porY);

    /// <summary>
    /// Vértices y curvaturas de la cinta. <b>El cálculo vive en
    /// <see cref="TrazoDiamante.Cinta"/></b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se quedó aquí como atajo porque lo llaman cinco sitios de este archivo —la cinta, el
    /// recorte del estribo, el hueco bajo el gancho, la salida del acero y el arco del
    /// doblez— y todos tienen que usar exactamente los mismos números, o el corte no caería
    /// sobre la línea dibujada.
    /// </para>
    /// <para>
    /// El cálculo se sacó a <see cref="TrazoDiamante"/> porque ahora lo necesita también la
    /// <b>vista previa</b> de la pestaña de concreto, que no puede tener su propia copia:
    /// dos copias de una geometría acaban enseñando dos diamantes distintos.
    /// </para>
    /// </remarks>
    private static (double[] Pts, double[] Bulges)? GeometriaCinta(
        List<(double X, double Y, double R)> centros, double extra) =>
        TrazoDiamante.Cinta(centros, extra);

    private object? CintaTangente(List<(double X, double Y, double R)> centros, double extra)
    {
        var geo = GeometriaCinta(centros, extra);
        if (geo is null)
        {
            return null;
        }

        var pts = geo.Value.Pts;
        var bulges = geo.Value.Bulges;

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(pts);
                pl.Closed = true;
                pl.Layer = "ESTRIBOS";

                for (var i = 0; i < bulges.Length; i++)
                {
                    pl.SetBulge(i, bulges[i]);
                }

                pl.Update();

                // El color va al FINAL, después de los bulges y del Update: es el
                // orden que la macro dejó anotado como necesario.
                pl.Color = PorCapa;

                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Cinta tangente del estribo diamante", ex);
            return null;
        }
    }

    /// <summary>
    /// El <b>gancho sísmico del diamante</b>, en el vértice izquierdo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un diamante es un estribo cerrado, así que sus dos extremos se juntan en algún
    /// sitio y ahí van sus ganchos, igual que el estribo rectangular los lleva en una
    /// esquina. Se ponen en el <b>vértice izquierdo</b> porque es donde el rectangular NO
    /// tiene el suyo —el suyo está arriba a la derecha— y así los dos ganchos de la sección
    /// no se montan uno encima del otro.
    /// </para>
    /// <para>
    /// <b>Se engancha a la varilla que el diamante ya abraza ahí.</b> Eso es lo que hace
    /// que el gancho sea real: un gancho sísmico rodea una varilla longitudinal, no dobla
    /// en el aire. Si en ese costado no hay varilla, el vértice del diamante es un doblez
    /// ficticio sobre una posición calculada y <b>no se dibuja gancho</b>: no habría de qué
    /// agarrarlo.
    /// </para>
    /// <para>
    /// <b>Reutiliza lo que ya existe:</b> <see cref="Cola"/> del estribo rectangular para
    /// las dos colas, y <see cref="RellenoDelGancho"/> del zuncho circular para el relleno
    /// del doblez y de las colas. La geometría del gancho se escribió una vez y este es el
    /// tercer sitio que la usa.
    /// </para>
    /// <para>
    /// <b>Las colas apuntan al núcleo</b>, o sea del vértice izquierdo hacia el centro de
    /// la sección, que es la misma regla del estribo rectangular: allí las colas van por la
    /// diagonal de la esquina, que es justamente el radio hacia el núcleo.
    /// </para>
    /// <para>
    /// <b>Aquí NO se gira 45° el radio.</b> Ese giro es del zuncho circular y solo tiene
    /// sentido ahí: en el zuncho el acero llega al doblez en dirección tangente, o sea
    /// perpendicular al radio, así que girar el radio 45° es lo mismo que doblar el acero
    /// 135°. En el diamante el acero llega por la diagonal del rombo, no en tangente, y ese
    /// giro de 45° dejaba la cola <b>exactamente encima de la propia diagonal del
    /// diamante</b>: en una sección cuadrada las dos diagonales que salen del vértice van a
    /// ±45° del eje, así que la cola girada 45° caía sobre una de ellas y el gancho se veía
    /// como una prolongación del estribo en vez de un gancho metido en el concreto.
    /// </para>
    /// <para>
    /// El radio sin girar es además la dirección más segura: cae en la <b>bisectriz</b> del
    /// vértice, o sea lo más lejos posible de las dos diagonales, así que la cola nunca se
    /// monta sobre el acero del diamante, sea la sección cuadrada, alta o achatada.
    /// </para>
    /// </remarks>
    private void GanchoDelDiamante(
        SeccionCad s, List<object> contorno,
        List<(double X, double Y, double R)> centros,
        double cx, double cy, double dDia, bool conFondoSolido)
    {
        var gancho = s.GanchoCm * _escala;

        if (gancho <= 0)
        {
            return;
        }

        // Las varillas del costado IZQUIERDO, que es donde va el gancho.
        var delLado = _varLat.Where(v => v.X < cx).ToList();

        if (delLado.Count == 0)
        {
            // Sin varilla en ese costado el vértice es un doblez ficticio: no hay de qué
            // agarrar el gancho, así que no se dibuja. Se dice, porque el usuario pidió el
            // gancho y conviene que sepa por qué no está.
            _log.Add(
                $"Sección '{s.Id}': el diamante no lleva gancho porque no hay varillas " +
                "laterales en el costado izquierdo a las que agarrarlo.");
            return;
        }

        // La MISMA regla que usa el vértice: una varilla si hay alguna a media altura, y
        // las dos más juntas si el eje cae entre dos.
        var seleccion = VarillasDelCentro(delLado, cy, porY: true);

        if (seleccion.Count == 0)
        {
            return;
        }

        // Con dos, el gancho va en la de ARRIBA: el recorrido del costado izquierdo va de
        // arriba hacia abajo, así que ahí es donde el acero llega al vértice.
        var barra = seleccion.OrderByDescending(v => v.Y).First();

        var rIn = barra.R;
        var rOut = rIn + dDia;

        // La cola apunta AL NÚCLEO: del vértice izquierdo hacia el centro, o sea +X.
        // Sin girar 45°, que es lo del zuncho circular y aquí caía sobre la diagonal
        // del propio diamante.
        var ux = cx - barra.X;
        var uy = cy - barra.Y;
        var ul = Math.Sqrt((ux * ux) + (uy * uy));

        if (ul < 1e-9)
        {
            return;
        }

        ux /= ul;
        uy /= ul;

        // Las normales de arranque: las perpendiculares a la cola.
        var n1X = -uy;
        var n1Y = ux;
        var n2X = uy;
        var n2Y = -ux;

        // El tope: la cola apunta al núcleo, así que cuanto más larga más se acerca al
        // centro. Se recorta donde queda lo más cerca posible, para que no lo cruce y
        // salga por el otro lado.
        var piX = barra.X + (rIn * n1X);
        var piY = barra.Y + (rIn * n1Y);

        var tope = ((cx - piX) * ux) + ((cy - piY) * uy);

        if (tope > 0 && gancho > tope)
        {
            _log.Add(
                $"Sección '{s.Id}': el gancho del diamante de {s.GanchoCm:0.#} cm no cabe " +
                $"y se recortó a {tope / _escala:0.#} cm.");
            gancho = tope;
        }

        var quads = new List<double[]>();
        var sectores = new List<double[]>();

        // El doblez: media corona alrededor de la varilla, del arranque de una cola al de
        // la otra. Su punto medio cae en -u, o sea en el lado OPUESTO a las colas.
        var a1 = Math.Atan2(n1Y, n1X);
        sectores.Add(new[] { barra.X, barra.Y, rIn, rOut, a1, a1 + Pi });

        // ------------------------------------------------------------------
        // Del doblez NO se dibuja NINGÚN arco
        // ------------------------------------------------------------------
        // Ni el interior ni el exterior, y cada uno por su razón:
        //
        //   * El INTERIOR tiene el centro y el radio de la varilla, o sea que ES su
        //     circunferencia, que ya está trazada.
        //   * El EXTERIOR va a rOut, igual que el borde exterior de la cinta. Entre los
        //     dos puntos donde la cinta abraza la varilla es la MISMA curva, ya trazada; y
        //     fuera de ellos se mete dentro del acero de la cinta —en la cuña que la cinta
        //     forma al doblar en el vértice—, así que pinta una raya por dentro del
        //     relleno. Medido: de 0.84 a 3.63 cm de raya según la sección.
        //
        // Se probó a dibujar solo esos dos pedazos de fuera, para cerrar el contorno de la
        // cola contra la cinta. Empalmaban al bit —los tres tramos sumaban la media vuelta
        // exacta— pero el usuario los rechazó: por muy bien empalmados que estén, son
        // rayas cruzando el relleno, y lo que tiene que verse ahí es el hatch limpio.
        //
        // El estribo rectangular hace lo mismo desde el principio: su gancho no traza el
        // arco del doblez, lo trae el contorno del propio estribo.
        var iBarra = centros.FindIndex(
            c => Math.Abs(c.X - barra.X) < 1e-9 && Math.Abs(c.Y - barra.Y) < 1e-9);

        // El borde INTERIOR de la cinta: los mismos números con los que se dibujó.
        var geoInt = GeometriaCinta(centros, 0);

        // El borde EXTERIOR, para saber dónde acaba el arco del doblez.
        var geoExt = GeometriaCinta(centros, dDia);

        // ------------------------------------------------------------------
        // ARRIBA DE LA VARILLA el contorno es CURVO: es el arco del doblez
        // ------------------------------------------------------------------
        // Y hay que seguir el recorrido del acero para verlo. El extremo que llega por la
        // diagonal de ABAJO envuelve la varilla 135° -de la tangencia de abajo, pasando por
        // la izquierda, hasta arriba- y sale como la cola de ARRIBA. O sea que el acero que
        // hay encima de la varilla es ESE doblez, y su borde exterior es un arco, no una
        // recta. Comprobado con la tangente: en cada punto del contacto, la tangente del
        // arco coincide con la dirección de avance del acero.
        //
        // El arco va del arranque de la cola a la tangencia de la cinta, «hasta donde
        // llegue»: ahí se funde con el borde exterior de la diagonal y de ahí en adelante ya
        // está dibujado. Y la cola arranca justo donde el arco acaba, tangente a él, así que
        // el contorno sale seguido y sin esquinas.
        //
        // Va SOLO arriba. Abajo el doblez que hay es el del otro extremo, que pasa por
        // DEBAJO de la diagonal, y por eso ese lado se recorta en lugar de dibujarse.
        if (iBarra >= 0 && geoExt is not null)
        {
            var tramoExt = TramoDeLaCinta(geoExt.Value.Pts, centros, iBarra, n1X, n1Y);

            if (tramoExt is not null)
            {
                var (ext1, ext2, _) = tramoExt.Value;

                // La tangencia es el extremo del tramo que está SOBRE la varilla.
                var d1 = ((ext1.X - barra.X) * (ext1.X - barra.X))
                    + ((ext1.Y - barra.Y) * (ext1.Y - barra.Y));
                var d2 = ((ext2.X - barra.X) * (ext2.X - barra.X))
                    + ((ext2.Y - barra.Y) * (ext2.Y - barra.Y));

                var tang = d1 <= d2 ? ext1 : ext2;

                ArcoDelDoblez(
                    contorno, barra.X, barra.Y, rOut,
                    a1, Math.Atan2(tang.Y - barra.Y, tang.X - barra.X));
            }
        }

        // Las dos colas, con la Cola del estribo rectangular, ENTERAS.
        //
        // Hubo una versión que les quitaba la línea interior —la que nace pegada a la
        // varilla—, con el argumento de que el doblez pasa por encima de la varilla y su
        // cara de dentro es la circunferencia de la varilla, que ya está dibujada. Se
        // quitó a pedido del usuario: son las DOS líneas que le faltaban al gancho, una por
        // cola, y el estribo va como estaba.
        //
        // Y las dos líneas exteriores se tratan DISTINTO, porque el gancho de arriba es el
        // que se dibuja encima:
        //
        //   * la de ARRIBA se dibuja ENTERA, desde el arranque en la perpendicular, porque
        //     justo ahí acaba el arco del doblez y las dos se empalman tangentes;
        //   * la de ABAJO se RECORTA donde sale del acero de la cinta, porque por ese lado
        //     el gancho pasa por debajo de la diagonal.
        //
        // Es la misma asimetría del estribo rectangular: una cola entera y la otra
        // recortada contra el estribo.
        //
        // Y la de arriba NO se alarga hacia atrás. Se probó, para que muriera sobre la línea
        // del diamante, y estaba mal por dos cosas: el contorno de ahí es CURVO, es el arco
        // del doblez, no una recta; y alargar la cola alargaba también su relleno hacia
        // atrás —Cola infla el cuadrilátero el espesor del estribo cuando le pasan un
        // arranque distinto—, así que el relleno se salía del diamante por arriba a la
        // izquierda. Era el «hatch que sale».
        foreach (var (nx, ny, arriba) in
            new[] { (n1X, n1Y, true), (n2X, n2Y, false) })
        {
            var poX = barra.X + (rOut * nx);
            var poY = barra.Y + (rOut * ny);

            (double X, double Y)? arranque = null;

            if (iBarra >= 0 && !arriba && geoInt is not null)
            {
                arranque = SalidaDelAceroDelDiamante(
                    geoInt.Value.Pts, centros, iBarra, nx, ny, poX, poY, ux, uy, gancho);
            }

            Cola(contorno, quads, barra.X, barra.Y, rIn, rOut, nx, ny, ux, uy, gancho,
                arranque is not null, arranque?.X ?? 0, arranque?.Y ?? 0);
        }

        // ------------------------------------------------------------------
        // Y LA LÍNEA DE LA CINTA QUE PASA ARRIBA DE LA VARILLA SE CORTA CON EL GANCHO
        // ------------------------------------------------------------------
        // Es lo que se pidió: «borra la línea que está arriba de la varilla, que se corte
        // con el ancho del gancho de arriba». Sin esto, la línea interior de la cinta sigue
        // dibujada de punta a punta y ATRAVIESA el brazo del gancho por dentro, así que en
        // el plano parece que la diagonal del rombo corta el gancho en lugar de pasarle por
        // debajo.
        //
        // Solo la de ARRIBA, que es el gancho que va encima. La de abajo pasa por debajo de
        // la cinta, y por eso lo que se recortó de ese lado fue la cola, no la cinta.
        //
        // OJO CON LO QUE ESTO NO ES: no le quita ninguna línea al gancho. Las dos colas
        // siguen con sus tres líneas cada una. Lo que se abre es un hueco del ancho del
        // brazo en la línea del DIAMANTE, que es la que pasa por debajo.
        // El hueco se APUNTA, no se abre todavía. Lo abre RearmarLasCintas junto con los
        // que abren las grapas, en una sola pasada: si se abriera aquí, la cinta quedaría
        // ya partida y luego habría que volver a partir una polilínea abierta, que es un
        // caso distinto y con más aristas.
        _huecoDelGancho = HuecoDelGanchoEnLaCinta(
            centros, iBarra, n1X, n1Y, ux, uy, rIn, rOut, gancho);

        if (conFondoSolido && (sectores.Count > 0 || quads.Count > 0))
        {
            RellenoDelGancho(quads, sectores);
        }
    }

    /// <summary>
    /// El tramo recto de la cinta que llega o sale de la varilla <b>por un lado</b>.
    /// </summary>
    /// <remarks>
    /// La cinta toca cada varilla en dos puntos: por donde llega la diagonal anterior y
    /// por donde sale la siguiente. Cuál de las dos es «la de arriba» depende del costado y
    /// del sentido del recorrido, así que no se da por sabido: se decide comparando con la
    /// normal que se pide.
    /// </remarks>
    /// <returns>
    /// Los dos extremos del tramo y el <b>índice del vértice</b> donde empieza, que es lo
    /// que hace falta para volver a montar la cinta con un hueco.
    /// </returns>
    private static ((double X, double Y) A, (double X, double Y) B, int VerticeA)?
        TramoDeLaCinta(
            double[] pts, List<(double X, double Y, double R)> centros, int iBarra,
            double nx, double ny)
    {
        var n = centros.Count;

        if (n < 3 || pts.Length < 4 * n || iBarra < 0 || iBarra >= n)
        {
            return null;
        }

        var c = centros[iBarra];

        if (c.R <= 0)
        {
            return null;
        }

        var llegaX = pts[4 * iBarra];
        var llegaY = pts[(4 * iBarra) + 1];
        var saleX = pts[(4 * iBarra) + 2];
        var saleY = pts[(4 * iBarra) + 3];

        var ladoLlega = ((llegaX - c.X) * nx) + ((llegaY - c.Y) * ny);
        var ladoSale = ((saleX - c.X) * nx) + ((saleY - c.Y) * ny);

        if (ladoLlega >= ladoSale)
        {
            // El de LLEGADA: viene del círculo anterior y muere en la tangencia. Su
            // vértice de arranque es la salida del círculo anterior.
            var previo = ((iBarra - 1) % n + n) % n;

            return ((pts[(4 * previo) + 2], pts[(4 * previo) + 3]),
                    (llegaX, llegaY),
                    (2 * previo) + 1);
        }

        // El de SALIDA: arranca en la tangencia y se va al círculo siguiente.
        var siguiente = (iBarra + 1) % n;

        return ((saleX, saleY),
                (pts[4 * siguiente], pts[(4 * siguiente) + 1]),
                (2 * iBarra) + 1);
    }

    /// <summary>
    /// El arco del doblez que se ve <b>encima de la varilla</b>, hasta la cinta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lleva una <b>guardia</b>: el arco tiene que barrer menos de media vuelta. Por
    /// geometría barre lo que separa la cola de la diagonal, que en cualquier sección real
    /// va de unos 10° a 70°; si sale más, los ángulos no describen este vértice —una sección
    /// absurda, un recorrido raro— y entonces <b>no se dibuja nada</b>. Un arco de más daría
    /// media vuelta alrededor de la varilla y se vería peor que la falta del pedazo.
    /// </para>
    /// </remarks>
    private void ArcoDelDoblez(
        List<object> contorno, double bx, double by, double r, double aIni, double aFin)
    {
        var barrido = aFin - aIni;

        while (barrido < 0)
        {
            barrido += 2 * Pi;
        }

        while (barrido >= 2 * Pi)
        {
            barrido -= 2 * Pi;
        }

        if (barrido < 1e-9)
        {
            // La cola arranca justo en la tangencia: no falta ningún pedazo de arco.
            return;
        }

        if (barrido > Pi / 2)
        {
            _log.Add(
                "Estribo diamante: no se dibujó el arco del gancho encima de la varilla " +
                $"porque barría {barrido * 180 / Pi:0.#}°, más de lo que puede separar una " +
                "cola de la diagonal del rombo.");
            return;
        }

        Agregar(contorno, Arco(bx, by, r, aIni, aFin));
    }

    /// <summary>Fracción del tramo de la cinta que como máximo se acepta abrir.</summary>
    /// <remarks>
    /// El hueco real es del ancho de la cola, unos milímetros sobre una diagonal de
    /// decenas de centímetros: en los armados probados no pasa del 15 % del tramo. El 50 %
    /// no está para afinar nada, está para que una cuenta equivocada no borre media
    /// diagonal del diamante.
    /// </remarks>
    private const double FraccionMaxHuecoCinta = 0.5;

    /// <summary>
    /// Abre la línea interior de la cinta <b>por donde la cola le pasa por encima</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto no le quita nada al gancho.</b> Conviene decirlo primero, porque la petición
    /// suena a lo contrario: lo que se corta es la línea del <i>diamante</i> —la que pasa
    /// arriba de la varilla y por debajo del brazo—, con un hueco del ancho del brazo. Las
    /// dos colas siguen dibujándose con sus <b>tres líneas</b> cada una.
    /// </para>
    /// <para>
    /// <b>Qué se ve sin esto.</b> El brazo del gancho sale por encima de la varilla y cruza
    /// la diagonal del diamante. Pero la línea interior de la cinta seguía dibujada de
    /// punta a punta, así que <b>atravesaba el brazo por dentro</b>: en el plano parecía que
    /// la diagonal cortaba el gancho, en lugar de pasar por debajo. Es el mismo defecto que
    /// <see cref="RecortarEstriboBajoDiamante"/> arregla para el estribo principal, y la
    /// misma solución: abrir el hueco de verdad, no taparlo con el orden de dibujo, porque
    /// en la sección de contorno no hay relleno que tape nada.
    /// </para>
    /// <para>
    /// <b>Cómo se decide qué se abre.</b> Sin ninguna prueba de «está dentro»: se recorta el
    /// tramo recto de la cinta contra el <b>rectángulo de la cola</b>, que son cuatro
    /// semiplanos —las dos caras, el arranque en la varilla y la punta—. Geometría cerrada,
    /// no una estimación.
    /// </para>
    /// <para>
    /// <b>Y se vuelve a montar la cinta entera, con el hueco.</b> No se puede borrar un
    /// trozo de una polilínea, así que se construye otra ABIERTA: empieza donde acaba el
    /// hueco, da la vuelta completa por todos los vértices y termina donde el hueco empieza.
    /// Los arcos de los dobleces se conservan tal cual, con sus mismos bulges, porque son
    /// los mismos vértices; lo único que cambia es por dónde se corta. Y la nueva sustituye
    /// a la vieja solo si se creó: al revés, un fallo dejaría la cinta sin línea interior.
    /// </para>
    /// <para>
    /// Se puede borrar la vieja sin miedo aunque haya servido de isla del relleno: los
    /// hatches de AutoCAD no son asociativos. El rayado del concreto no la usa —la cinta del
    /// diamante no es una isla válida, está explicado en el hatch— así que no queda nadie
    /// que dependa de ella.
    /// </para>
    /// </remarks>
    /// <returns>El hueco que hay que abrir, o <c>null</c> si el gancho no tapa nada.</returns>
    private CintaConHuecos.Hueco? HuecoDelGanchoEnLaCinta(
        List<(double X, double Y, double R)> centros, int iBarra,
        double nx, double ny, double ux, double uy,
        double rIn, double rOut, double largo)
    {
        var geo = GeometriaCinta(centros, 0);

        if (geo is null || iBarra < 0 || iBarra >= centros.Count)
        {
            return null;
        }

        var pts = geo.Value.Pts;

        var tramo = TramoDeLaCinta(pts, centros, iBarra, nx, ny);

        if (tramo is null)
        {
            return null;
        }

        var (a, b, verticeA) = tramo.Value;

        var c = centros[iBarra];

        // ---------- Qué tapa el gancho, que son DOS piezas ----------
        // Y hay que mirar las dos, no solo la cola. La primera versión de esto solo
        // recortaba contra el rectángulo de la cola, y con eso el hueco empezaba en la
        // perpendicular a la varilla en vez de en la tangencia: quedaba un rabito de línea
        // justo encima de la varilla, que es lo que el usuario seguía viendo. Y en una
        // columna alta, con la diagonal muy empinada, la cola no llega a cruzar el tramo y
        // no se abría NADA, aunque el doblez lo tapara igual.
        var pieza1 = RecorteDeLaCola(a, b, c, nx, ny, ux, uy, rIn, rOut, largo);
        var pieza2 = RecorteDelDoblez(a, b, c, ux, uy, rOut);

        if (pieza1 is null && pieza2 is null)
        {
            // El gancho no tapa nada de este tramo: no hay hueco que abrir.
            return null;
        }

        // Las dos piezas se tocan por la perpendicular a la varilla —una está a un lado y
        // la otra al otro—, así que su unión es un solo hueco seguido.
        var s0 = Math.Min(pieza1?.S0 ?? double.MaxValue, pieza2?.S0 ?? double.MaxValue);
        var s1 = Math.Max(pieza1?.S1 ?? double.MinValue, pieza2?.S1 ?? double.MinValue);

        var largoTramo = Math.Sqrt(
            ((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

        if (largoTramo < 1e-9 || (s1 - s0) * largoTramo < LargoMinTramo)
        {
            return null;
        }

        // El seguro de la fracción ya no se comprueba aquí: lo hace CintaConHuecos.Abrir
        // sobre el TOTAL de los huecos, que es donde tiene sentido. Comprobarlo también
        // aquí, hueco a hueco, dejaría pasar el caso de varios huecos pequeños que juntos
        // se comen la cinta.
        return new CintaConHuecos.Hueco(verticeA, s0, s1);
    }

    /// <summary>
    /// Vuelve a montar las dos cintas del diamante con los <b>huecos</b> de todo lo que
    /// les pasa por encima.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Qué se ve sin esto.</b> Una grapa se coloca por fuera y tiene que verse pasar por
    /// delante, igual que ya pasa con el estribo rectangular. Pero el diamante se dibuja
    /// <b>después</b> de las grapas y nada le abría la cinta, así que sus dos líneas
    /// cruzaban por encima de cualquier grapa que les pasara: el dibujo se leía al revés,
    /// con el diamante montado sobre la grapa.
    /// </para>
    /// <para>
    /// <b>Por qué no se arregla con el orden de dibujo.</b> Por lo mismo que en
    /// <see cref="RecortarEstriboBajoDiamante"/> y en <c>RecortarEstriboBajoGrapas</c>:
    /// <c>EstribosAlFrente</c> sube al frente TODO lo que está en la capa <c>ESTRIBOS</c>
    /// —la grapa y también la cinta—, y en la sección de contorno no hay ningún relleno que
    /// pudiera tapar nada. Hay que abrir el hueco de verdad.
    /// </para>
    /// <para>
    /// <b>Se abren las DOS cintas, no solo la interior.</b> La grapa pasa por encima del
    /// diamante entero, así que si solo se abriera la interior, la línea exterior seguiría
    /// cruzando la grapa y el defecto se vería igual, nada más que corrido un grueso de
    /// estribo. La del gancho sí es solo la interior, y eso no cambia: el brazo del gancho
    /// sale por encima de la varilla y solo cruza esa.
    /// </para>
    /// <para>
    /// La cuenta vive en <see cref="CintaConHuecos"/>, fuera de aquí y sin COM, para poder
    /// probarla: es lo que hace <c>tools/prueba-cinta-huecos</c>. Y ahí se descubrió que la
    /// grapa cruza la cinta <b>justo en los dobleces</b> y no en las diagonales —lógico,
    /// porque los dobleces están en las varillas que el diamante abraza y la grapa se
    /// amarra a varillas—, así que hubo que partir arcos y no solo rectas.
    /// </para>
    /// </remarks>
    private void RearmarLasCintas(
        List<(double X, double Y, double R)> centros, double dDia, bool conFondoSolido)
    {
        RearmarUnaCinta(ref _diamInt, centros, 0, _huecoDelGancho, conFondoSolido, "interior");
        RearmarUnaCinta(ref _diamExt, centros, dDia, null, conFondoSolido, "exterior");
    }

    /// <summary>Rearma una de las dos cintas, si algo le abre hueco.</summary>
    private void RearmarUnaCinta(
        ref object? cinta,
        List<(double X, double Y, double R)> centros,
        double extra,
        CintaConHuecos.Hueco? huecoDelGancho,
        bool conFondoSolido,
        string cual)
    {
        if (cinta is null)
        {
            return;
        }

        var geo = GeometriaCinta(centros, extra);

        if (geo is null)
        {
            return;
        }

        var pts = geo.Value.Pts;
        var bulges = geo.Value.Bulges;

        var huecos = new List<CintaConHuecos.Hueco>();

        if (huecoDelGancho is not null)
        {
            huecos.Add(huecoDelGancho.Value);
        }

        if (_contornosDeGrapa.Count > 0)
        {
            huecos.AddRange(
                CintaConHuecos.Huecos(pts, bulges, _contornosDeGrapa, LargoMinTramo));
        }

        if (huecos.Count == 0)
        {
            return;
        }

        var trozos = CintaConHuecos.Abrir(
            pts, bulges, huecos, LargoMinTramo, FraccionMaxHuecoCinta);

        if (trozos is null)
        {
            Nota(
                $"Estribo diamante: no se abrió la línea {cual} de la cinta porque los " +
                "huecos calculados se comían más de la mitad del contorno. El dibujo " +
                "queda completo, con la línea cruzando lo que le pasa por encima.");
            return;
        }

        // PRIMERO se dibujan los trozos nuevos…
        var nuevos = new List<object>();

        foreach (var trozo in trozos)
        {
            var pl = PolilineaAbierta(trozo.Pts, trozo.Bulges);

            if (pl is not null)
            {
                nuevos.Add(pl);
            }
        }

        // …y la vieja se borra SOLO si alguno se creó. Al revés, un fallo al dibujar
        // dejaría el diamante sin esa línea entera. Es la misma regla que siguen los otros
        // dos recortes de la sección.
        if (nuevos.Count == 0)
        {
            Nota(
                $"Estribo diamante: no se pudieron dibujar los trozos de la línea {cual} " +
                "de la cinta, así que se dejó entera.");
            return;
        }

        // La vieja se puede borrar aunque haya hecho de isla del relleno: ese relleno ya
        // está hecho y los hatches de AutoCAD no son asociativos.
        Borrar(cinta);
        cinta = null;

        if (conFondoSolido)
        {
            foreach (var e in nuevos)
            {
                Negro(e);
            }
        }

        AlFrente(nuevos);
    }

    /// <summary>
    /// Qué parte del tramo tapa <b>la cola</b>: recorte contra su rectángulo.
    /// </summary>
    /// <remarks>
    /// Cuatro semiplanos —las dos caras, el arranque en la varilla y la punta—, cada uno
    /// «este producto escalar no pasa de aquí». Es el recorte de Liang-Barsky de toda la
    /// vida: geometría cerrada, sin ninguna prueba de «está dentro».
    /// </remarks>
    private static (double S0, double S1)? RecorteDeLaCola(
        (double X, double Y) a, (double X, double Y) b,
        (double X, double Y, double R) c,
        double nx, double ny, double ux, double uy,
        double rIn, double rOut, double largo)
    {
        var piX = c.X + (rIn * nx);
        var piY = c.Y + (rIn * ny);

        var poX = c.X + (rOut * nx);
        var poY = c.Y + (rOut * ny);

        var qiX = piX + (largo * ux);
        var qiY = piY + (largo * uy);

        var lados = new[]
        {
            // Por dentro de la cara interior y de la exterior
            (Nx: -nx, Ny: -ny, Tope: -((piX * nx) + (piY * ny))),
            (Nx: nx, Ny: ny, Tope: (poX * nx) + (poY * ny)),

            // Del arranque en la varilla a la punta
            (Nx: -ux, Ny: -uy, Tope: -((piX * ux) + (piY * uy))),
            (Nx: ux, Ny: uy, Tope: (qiX * ux) + (qiY * uy))
        };

        var s0 = 0.0;
        var s1 = 1.0;

        foreach (var lado in lados)
        {
            var pa = (a.X * lado.Nx) + (a.Y * lado.Ny) - lado.Tope;
            var pb = (b.X * lado.Nx) + (b.Y * lado.Ny) - lado.Tope;

            var de = pb - pa;

            if (Math.Abs(de) < 1e-15)
            {
                // Tramo paralelo a este lado: o entra entero o no entra nada.
                if (pa > 0)
                {
                    return null;
                }

                continue;
            }

            var corte = -pa / de;

            if (de > 0)
            {
                s1 = Math.Min(s1, corte);
            }
            else
            {
                s0 = Math.Max(s0, corte);
            }

            if (s0 >= s1)
            {
                return null;
            }
        }

        return (s0, s1);
    }

    /// <summary>
    /// Qué parte del tramo tapa <b>el doblez</b>: la media corona que rodea la varilla.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El doblez ocupa la corona de <c>rIn</c> a <c>rOut</c> alrededor de la varilla, media
    /// vuelta, la mitad OPUESTA a las colas. Así que el recorte es doble: dentro del disco
    /// de radio <c>rOut</c> y del lado de la media vuelta, que es el semiplano
    /// <c>(P − C)·u ≤ 0</c>.
    /// </para>
    /// <para>
    /// El radio interior no hace falta mirarlo: el borde interior de la cinta es
    /// <b>tangente</b> a la varilla, así que nunca se mete dentro de ella. Y si en algún
    /// armado se metiera, ese trozo lo tapa la varilla, que se dibuja encima.
    /// </para>
    /// </remarks>
    private static (double S0, double S1)? RecorteDelDoblez(
        (double X, double Y) a, (double X, double Y) b,
        (double X, double Y, double R) c,
        double ux, double uy, double rOut)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        var largo2 = (dx * dx) + (dy * dy);

        if (largo2 < 1e-18)
        {
            return null;
        }

        // |a + s·d − C|² = rOut², en s.
        var fx = a.X - c.X;
        var fy = a.Y - c.Y;

        var bb = 2 * ((fx * dx) + (fy * dy));
        var cc = (fx * fx) + (fy * fy) - (rOut * rOut);

        var disc = (bb * bb) - (4 * largo2 * cc);

        if (disc <= 0)
        {
            // El tramo no llega a entrar en el doblez.
            return null;
        }

        var raiz = Math.Sqrt(disc);

        var s0 = Math.Max(0, (-bb - raiz) / (2 * largo2));
        var s1 = Math.Min(1, (-bb + raiz) / (2 * largo2));

        if (s0 >= s1)
        {
            return null;
        }

        // Y del lado de la media vuelta: (P − C)·u ≤ 0.
        var pa = (fx * ux) + (fy * uy);
        var pb = ((b.X - c.X) * ux) + ((b.Y - c.Y) * uy);

        var de = pb - pa;

        if (Math.Abs(de) < 1e-15)
        {
            if (pa > 0)
            {
                return null;
            }
        }
        else
        {
            var corte = -pa / de;

            if (de > 0)
            {
                s1 = Math.Min(s1, corte);
            }
            else
            {
                s0 = Math.Max(s0, corte);
            }
        }

        return s0 < s1 ? (s0, s1) : null;
    }

    /// <summary>La cinta interior, abierta: misma polilínea con arcos, pero sin cerrar.</summary>
    private object? PolilineaAbierta(double[] pts, double[] bulges)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(pts);
                pl.Closed = false;
                pl.Layer = "ESTRIBOS";

                for (var i = 0; i < bulges.Length && i < pts.Length / 2; i++)
                {
                    pl.SetBulge(i, bulges[i]);
                }

                pl.Update();

                // El color al FINAL, después de los bulges y del Update, igual que en
                // CintaTangente: es el orden que la macro dejó anotado como necesario.
                pl.Color = PorCapa;

                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Cinta interior del diamante, abierta bajo el gancho", ex);
            return null;
        }
    }

    /// <summary>
    /// Dónde sale del acero del diamante la línea exterior de una cola del gancho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El problema.</b> La cola arranca en el borde exterior del doblez, o sea a
    /// <c>rOut</c> de la varilla, y de ahí sale recta hacia el núcleo. Pero justo ahí la
    /// cinta del diamante <b>le pasa por encima</b>: la diagonal del rombo llega al
    /// vértice por ese mismo sitio. Así que el primer trocito de esa línea queda dentro
    /// del acero del diamante, y dibujarlo pinta una raya por dentro del relleno.
    /// </para>
    /// <para>
    /// <b>La solución.</b> La línea empieza donde <b>sale</b> de ese acero, o sea donde
    /// cruza el borde interior de la cinta. Y ese borde no se estima: es el tramo recto
    /// que <see cref="GeometriaCinta"/> calcula para dibujar la cinta, con los mismos
    /// números, así que el recorte cae <i>sobre</i> la línea dibujada y no un poco antes
    /// ni un poco después.
    /// </para>
    /// <para>
    /// Es el mismo recorte que ya hacía el gancho del estribo rectangular, donde la línea
    /// exterior de la segunda cola arranca sobre la línea interior del estribo. De hecho
    /// se le pasa a la misma <see cref="Cola"/>, por los mismos parámetros.
    /// </para>
    /// <para>
    /// Cada cola se recorta con <b>su</b> diagonal: la de arriba con la que llega y la de
    /// abajo con la que sale. Se distinguen por el lado, comparando la normal del punto de
    /// tangencia con la de la cola.
    /// </para>
    /// <returns>
    /// El punto de salida, o <c>null</c> si no hay recorte que hacer: si las dos rectas
    /// son paralelas, si el cruce cae fuera de la cola o si cae fuera del tramo recto de
    /// la cinta. En todos esos casos se dibuja la cola entera, que es lo que se hacía
    /// antes; equivocar el recorte sería peor.
    /// </returns>
    /// </remarks>
    private static (double X, double Y)? SalidaDelAceroDelDiamante(
        double[] pts, List<(double X, double Y, double R)> centros, int iBarra,
        double nx, double ny, double px, double py, double ux, double uy, double largo)
    {
        // El tramo recto de la cinta de ESE lado, el mismo que sirve para alargar la otra
        // cola: una sola función decide de qué lado se mira.
        var tramo = TramoDeLaCinta(pts, centros, iBarra, nx, ny);

        if (tramo is null)
        {
            return null;
        }

        var (a, b, _) = tramo.Value;

        var ax = a.X;
        var ay = a.Y;

        var dx = b.X - ax;
        var dy = b.Y - ay;

        // Producto cruzado de la dirección de la cola con la del tramo. Cero es que van
        // paralelas: no se cruzan y no hay nada que recortar.
        var cruz = (ux * dy) - (uy * dx);

        if (Math.Abs(cruz) < 1e-12)
        {
            return null;
        }

        var rx = ax - px;
        var ry = ay - py;

        var t = ((rx * dy) - (ry * dx)) / cruz;
        var sTramo = ((rx * uy) - (ry * ux)) / cruz;

        // El cruce tiene que caer DENTRO de la cola y DENTRO del tramo recto de la cinta.
        // Si no, o la cinta no le pasa por encima o la cuenta no describe este caso, y en
        // los dos la cola entera es la respuesta correcta.
        if (t <= 1e-12 || t >= largo || sTramo < -1e-9 || sTramo > 1 + 1e-9)
        {
            return null;
        }

        return (px + (t * ux), py + (t * uy));
    }
}
