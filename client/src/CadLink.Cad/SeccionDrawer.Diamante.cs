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
    /// <summary>Radio del doblez de la esquina, como fracción del diámetro.</summary>
    private const double DiaFactorRadioEsquina = 0.5;

    /// <summary>Tolerancia para dar una varilla por «centrada», en radios.</summary>
    private const double DiaTolCentroFactor = 0.5;

    /// <summary>Holgura entre el diamante y la varilla que abraza.</summary>
    private const double DiaHolguraGancho = 0.0;

    /// <summary>Contornos del diamante de la sección en curso, para las islas.</summary>
    private object? _diamExt;
    private object? _diamInt;

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

        // ---------- Radios de los dobleces laterales ----------
        var rEsqInt = DiaFactorRadioEsquina * dDia;
        if (rEsqInt < dDia * 0.25)
        {
            rEsqInt = dDia * 0.25;
        }

        var rEsqExt = rEsqInt + dDia;

        // En una sección estrecha el doblez no puede ser mayor que la sección:
        // se recorta, pero solo si queda un radio con sentido.
        var rMaxLat = 0.35 * (x2 - x1);
        if (rEsqExt > rMaxLat && rMaxLat > dDia * 1.3)
        {
            rEsqExt = rMaxLat;
            rEsqInt = rEsqExt - dDia;
        }

        // ---------- A qué varillas se abraza ----------
        var selSup = VarillasDelCentro(_varSup, cx);
        var selInf = VarillasDelCentro(_varInf, cx);

        // Orden del recorrido: derecha, arriba, izquierda, abajo. Tiene que ser
        // ANTIHORARIO y sin cruces, o la cinta sale hecha un nudo.
        var centros = new List<(double X, double Y, double R)>();
        centros.AddRange(DoblezLateral(derecha: true, cx, cy, x1, x2, rEsqExt, rEsqInt));

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
            var (a, bb) = selSup[0].X > selSup[1].X ? (selSup[0], selSup[1]) : (selSup[1], selSup[0]);
            centros.Add(ConHolgura(a));
            centros.Add(ConHolgura(bb));
        }

        centros.AddRange(DoblezLateral(derecha: false, cx, cy, x1, x2, rEsqExt, rEsqInt));

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
            var (a, bb) = selInf[0].X < selInf[1].X ? (selInf[0], selInf[1]) : (selInf[1], selInf[0]);
            centros.Add(ConHolgura(a));
            centros.Add(ConHolgura(bb));
        }

        if (centros.Count < 3)
        {
            return;
        }

        // ---------- Que la cinta rodee las varillas laterales ----------
        centros = RodearLaterales(centros, dDia);

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

    private static (double X, double Y, double R) ConHolgura((double X, double Y, double R) v) =>
        (v.X, v.Y, v.R + DiaHolguraGancho);

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
    private List<(double X, double Y, double R)> DoblezLateral(
        bool derecha, double cx, double cy, double x1, double x2,
        double rEsqExt, double rEsqInt)
    {
        var ficticio = new List<(double X, double Y, double R)>
        {
            derecha ? (x2 - rEsqExt, cy, rEsqInt) : (x1 + rEsqExt, cy, rEsqInt)
        };

        // Las varillas de ESE costado, y de ese costado solo: mezclarlas haría que el
        // doblez de la derecha se fuera a abrazar una varilla de la izquierda.
        var delLado = _varLat
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
    private List<(double X, double Y, double R)> RodearLaterales(
        List<(double X, double Y, double R)> centros, double dDia)
    {
        if (_varLat.Count == 0)
        {
            return centros;
        }

        var actual = centros;

        // Las que ya están en el recorrido no se vuelven a meter.
        var puestas = new List<(double X, double Y, double R)>();

        for (var pasada = 0; pasada < PasadasRodeo; pasada++)
        {
            var interior = GeometriaCinta(actual, 0);
            var exterior = GeometriaCinta(actual, dDia);

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

                foreach (var v in _varLat)
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
        if (GeometriaCinta(actual, 0) is null || GeometriaCinta(actual, dDia) is null)
        {
            Nota(
                "Estribo diamante: no se pudo hacer que rodeara las varillas " +
                "laterales, así que se dibuja como en la macro, cruzándolas.");

            return centros;
        }

        if (actual.Count > centros.Count)
        {
            Nota(
                $"Estribo diamante: rodea {actual.Count - centros.Count} varilla(s) " +
                "lateral(es) que quedaban en su camino. Esto no lo hace la macro: ahí " +
                "el diamante las atraviesa.");
        }

        return actual;
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
    private static List<(double X, double Y, double R)> VarillasDelCentro(
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

        var tol = Math.Max(DiaTolCentroFactor * varillas[mejor].R, 1e-6);

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
    private static (double[] Pts, double[] Bulges)? GeometriaCinta(
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
                barrido += 2 * Pi;
            }

            while (barrido >= 2 * Pi)
            {
                barrido -= 2 * Pi;
            }

            bulges[2 * i] = Math.Tan(barrido / 4);
            bulges[(2 * i) + 1] = 0;
        }

        return (pts, bulges);
    }

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

        // El borde EXTERIOR, para alargar hasta él la línea de la cola de arriba.
        var geoExt = GeometriaCinta(centros, dDia);

        // Las dos colas, con la Cola del estribo rectangular.
        //
        // SIN la línea interior, la que nace pegada a la varilla: va fuera por lo mismo
        // que el arco interior, porque el doblez pasa POR ENCIMA de la varilla y su cara de
        // dentro es la circunferencia de la varilla, que ya está dibujada.
        //
        // Y las dos líneas exteriores se tratan DISTINTO, porque el gancho de arriba es el
        // que se dibuja encima:
        //
        //   * la de ARRIBA se ALARGA hacia atrás hasta el borde exterior de la cinta, para
        //     que muera sobre la línea del diamante en vez de nacer en el aire;
        //   * la de ABAJO se RECORTA donde sale del acero de la cinta, porque por ese lado
        //     el gancho pasa por debajo.
        //
        // Es la misma asimetría del estribo rectangular: una cola entera y la otra
        // recortada contra el estribo.
        foreach (var (nx, ny, arriba) in
            new[] { (n1X, n1Y, true), (n2X, n2Y, false) })
        {
            var poX = barra.X + (rOut * nx);
            var poY = barra.Y + (rOut * ny);

            (double X, double Y)? arranque = null;

            if (iBarra >= 0)
            {
                arranque = arriba
                    ? (geoExt is not null
                        ? AlcanceConLaCinta(
                            geoExt.Value.Pts, centros, iBarra, nx, ny, poX, poY, ux, uy,
                            2 * dDia)
                        : null)
                    : (geoInt is not null
                        ? SalidaDelAceroDelDiamante(
                            geoInt.Value.Pts, centros, iBarra, nx, ny, poX, poY, ux, uy,
                            gancho)
                        : null);
            }

            Cola(contorno, quads, barra.X, barra.Y, rIn, rOut, nx, ny, ux, uy, gancho,
                arranque is not null, arranque?.X ?? 0, arranque?.Y ?? 0,
                sinLineaInterior: true);
        }

        // ------------------------------------------------------------------
        // Y se abre la línea interior de la cinta por donde el brazo le pasa por encima
        // ------------------------------------------------------------------
        // Solo la de ARRIBA, que es el gancho que va encima. La de abajo pasa por debajo de
        // la cinta y por eso su línea exterior se recortó en lugar de abrir nada.
        var cintaAbierta = AbrirCintaBajoLaCola(
            centros, iBarra, n1X, n1Y, ux, uy, rIn, rOut, gancho, conFondoSolido);

        if (cintaAbierta is not null)
        {
            // La vieja se borra AHORA, no antes: hacía de isla del relleno del diamante,
            // que ya está hecho, y los hatches no son asociativos.
            Borrar(_diamInt);
            _diamInt = cintaAbierta;
        }

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
    /// Hasta dónde hay que <b>alargar hacia atrás</b> la línea exterior de una cola para
    /// que llegue al borde exterior de la cinta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La cola arranca en la perpendicular a la varilla, y ahí no hay ninguna línea: el
    /// borde exterior de la cinta pasa un poco más atrás, porque deja de abrazar la varilla
    /// antes de llegar a esa perpendicular. Resultado: la línea de la cola nacía en el
    /// aire, a media banda del diamante.
    /// </para>
    /// <para>
    /// Se alarga hacia atrás hasta el borde exterior de la cinta, que es el mismo tramo
    /// recto con el que <see cref="GeometriaCinta"/> la dibuja. Así la línea del gancho
    /// <b>muere sobre la línea del diamante</b> y el contorno se lee seguido.
    /// </para>
    /// <para>
    /// Solo hacia ATRÁS y con tope. Si el cruce cayera hacia delante, o más atrás que
    /// <paramref name="maxAtras"/>, o fuera del tramo recto, no se alarga: los números no
    /// describirían este vértice, y una línea larga de más se ve peor que una corta.
    /// </para>
    /// </remarks>
    private static (double X, double Y)? AlcanceConLaCinta(
        double[] pts, List<(double X, double Y, double R)> centros, int iBarra,
        double nx, double ny, double px, double py, double ux, double uy,
        double maxAtras)
    {
        var tramo = TramoDeLaCinta(pts, centros, iBarra, nx, ny);

        if (tramo is null)
        {
            return null;
        }

        var (a, b, _) = tramo.Value;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        var cruz = (ux * dy) - (uy * dx);

        if (Math.Abs(cruz) < 1e-12)
        {
            return null;
        }

        var rx = a.X - px;
        var ry = a.Y - py;

        var t = ((rx * dy) - (ry * dx)) / cruz;
        var sTramo = ((rx * uy) - (ry * ux)) / cruz;

        // t NEGATIVO es hacia atrás, que es lo que se busca aquí.
        if (t >= -1e-12 || t < -maxAtras || sTramo < -1e-9 || sTramo > 1 + 1e-9)
        {
            return null;
        }

        return (px + (t * ux), py + (t * uy));
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
    /// <returns>La cinta interior nueva, o <c>null</c> si se deja la de antes.</returns>
    private object? AbrirCintaBajoLaCola(
        List<(double X, double Y, double R)> centros, int iBarra,
        double nx, double ny, double ux, double uy,
        double rIn, double rOut, double largo, bool conFondoSolido)
    {
        var geo = GeometriaCinta(centros, 0);

        if (geo is null || iBarra < 0 || iBarra >= centros.Count)
        {
            return null;
        }

        var pts = geo.Value.Pts;
        var bulges = geo.Value.Bulges;

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

        if (s1 - s0 > FraccionMaxHuecoCinta)
        {
            Nota(
                "Estribo diamante: no se abrió la línea interior de la cinta bajo el " +
                $"gancho porque el hueco calculado se comía el {100 * (s1 - s0):0} % de " +
                "la diagonal. El dibujo queda completo, con la línea cruzando el gancho.");
            return null;
        }

        // ---------- La cinta, otra vez, abierta por el hueco ----------
        var m = 2 * centros.Count;

        var nuevos = new List<double>();
        var nuevosBulges = new List<double> { 0 };

        // Empieza donde ACABA el hueco…
        nuevos.Add(a.X + (s1 * (b.X - a.X)));
        nuevos.Add(a.Y + (s1 * (b.Y - a.Y)));

        // …da la vuelta entera por los vértices de siempre…
        for (var k = 1; k <= m; k++)
        {
            var v = (verticeA + k) % m;

            nuevos.Add(pts[2 * v]);
            nuevos.Add(pts[(2 * v) + 1]);
            nuevosBulges.Add(bulges[v]);
        }

        // …y termina donde el hueco EMPIEZA.
        nuevos.Add(a.X + (s0 * (b.X - a.X)));
        nuevos.Add(a.Y + (s0 * (b.Y - a.Y)));
        nuevosBulges.Add(0);

        var abierta = PolilineaAbierta(nuevos.ToArray(), nuevosBulges.ToArray());

        if (abierta is null)
        {
            return null;
        }

        if (conFondoSolido)
        {
            Negro(abierta);
        }

        AlFrente(new List<object> { abierta });

        return abierta;
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
