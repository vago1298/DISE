namespace CadLink.Revit.Nucleo;

// ============================================================================
//  DEL ARMADO DE LA TABLA A VARILLAS EN EL ESPACIO
//
//  Todo lo que se puede decidir sin Revit: los puntos de cada varilla, hacia donde dobla su
//  gancho y como se agrupan los estribos en juegos. El complemento solo convierte esto en
//  llamadas a la Revit API (Armador.cs).
//
//  Unidades: METROS en todo este archivo, en coordenadas del modelo. La conversion a pies
//  -las unidades internas de Revit- la hace el complemento, en Unidades.cs.
// ============================================================================

/// <summary>Un vector o un punto en metros.</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public double Punto(V3 b) => (X * b.X) + (Y * b.Y) + (Z * b.Z);

    public V3 Cruz(V3 b) => new((Y * b.Z) - (Z * b.Y), (Z * b.X) - (X * b.Z), (X * b.Y) - (Y * b.X));

    public double Largo => Math.Sqrt(Punto(this));

    public V3 Unitario()
    {
        var l = Largo;
        return l < 1e-12 ? this : this * (1 / l);
    }

    public static V3 Arriba => new(0, 0, 1);
}

/// <summary>
/// Donde esta la pieza: la esquina de su seccion y sus tres direcciones.
/// </summary>
/// <param name="Origen">
/// La esquina de abajo a la izquierda de la seccion en el ARRANQUE de la pieza. Es el (0, 0)
/// de las coordenadas de la tabla.
/// </param>
/// <param name="Eje">A lo largo de la pieza, unitario.</param>
/// <param name="Ex">Hacia donde crece la X de la seccion -su base-, unitario.</param>
/// <param name="Ey">Hacia donde crece la Y -su peralte-, unitario.</param>
/// <param name="LargoM">Lo que mide la pieza a lo largo de <paramref name="Eje"/>.</param>
public sealed record MarcoPieza(V3 Origen, V3 Eje, V3 Ex, V3 Ey, double LargoM)
{
    /// <summary>Un punto de la seccion, en cm, a <paramref name="sM"/> metros del arranque.</summary>
    public V3 En(double xCm, double yCm, double sM) =>
        Origen + (Ex * (xCm / 100)) + (Ey * (yCm / 100)) + (Eje * sM);

    /// <summary>
    /// El marco recortado a lo que mide la pieza DE VERDAD a lo largo de su eje: de cara a cara.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es el «armadura totalmente fuera de su anfitrion».</b> La linea de una trabe se dibuja
    /// de centro a centro de columna, pero Revit recorta su solido en la CARA de la columna. Con
    /// el largo de la linea, el primer y el ultimo estribo -a 5 cm de la punta- caian DENTRO
    /// de la columna: fuera de la trabe. Y las corridas, los bastones y las zonas de estribos
    /// se median desde el centro de la columna y no desde su paño, como en el alzado.
    /// </para>
    /// <para>
    /// Con los puntos del solido proyectados en el eje se saca donde empieza y acaba la pieza.
    /// Sin puntos, o si dan algo absurdo, se queda como estaba.
    /// </para>
    /// </remarks>
    public MarcoPieza AlLargoDe(IEnumerable<V3> puntosDelSolido)
    {
        var s = puntosDelSolido.Select(p => (p - Origen).Punto(Eje)).ToList();

        if (s.Count < 2)
        {
            return this;
        }

        var (min, max) = (s.Min(), s.Max());

        // Una pieza de menos de 5 cm, o que se sale mas de un metro de su linea, no es una
        // trabe de verdad: mejor la linea.
        if (max - min < 0.05 || min < -1 || max > LargoM + 1)
        {
            return this;
        }

        return this with { Origen = Origen + (Eje * min), LargoM = max - min };
    }

    /// <summary>
    /// El marco de una trabe <b>horizontal</b>: su linea en planta, y la cara de abajo.
    /// </summary>
    /// <param name="p1">Arranque de su linea de colocacion.</param>
    /// <param name="p2">Final.</param>
    /// <param name="zCaraInferior">La cota de su cara de abajo.</param>
    /// <param name="baseCm">El ancho, para centrar la seccion en la linea.</param>
    /// <remarks>
    /// La X de la seccion va a la IZQUIERDA del eje mirando del arranque al final, y la Y hacia
    /// arriba. Con secciones simetricas da igual el sentido, que es lo normal en una trabe.
    /// </remarks>
    public static MarcoPieza DeTrabe(V3 p1, V3 p2, double zCaraInferior, double baseCm)
    {
        var plano = new V3(p2.X - p1.X, p2.Y - p1.Y, 0);
        var eje = plano.Unitario();
        var ex = V3.Arriba.Cruz(eje).Unitario();
        var ancho = baseCm / 100;

        var origen = new V3(p1.X, p1.Y, zCaraInferior) - (ex * (ancho / 2));

        return new MarcoPieza(origen, eje, ex, V3.Arriba, plano.Largo);
    }

    /// <summary>El marco de una columna vertical: su centro, sus caras y sus dos lados.</summary>
    /// <param name="centro">El centro de la seccion en planta, a cualquier cota.</param>
    /// <param name="ladoX">Hacia donde va su base -el lado X de la familia-.</param>
    /// <param name="ladoY">Hacia donde va su peralte.</param>
    public static MarcoPieza DeColumna(
        V3 centro, V3 ladoX, V3 ladoY, double zBase, double zTope, double baseCm, double alturaCm)
    {
        var ex = new V3(ladoX.X, ladoX.Y, 0).Unitario();
        var ey = new V3(ladoY.X, ladoY.Y, 0).Unitario();

        var origen = new V3(centro.X, centro.Y, zBase)
                     - (ex * (baseCm / 200)) - (ey * (alturaCm / 200));

        return new MarcoPieza(origen, V3.Arriba, ex, ey, zTope - zBase);
    }
}

/// <summary>Que clase de varilla es, para elegir el estilo de Revit.</summary>
public enum EstiloVarilla
{
    /// <summary>Longitudinal: corrida, lateral o baston.</summary>
    Longitudinal,

    /// <summary>Estribo.</summary>
    Estribo
}

/// <summary>El gancho que lleva cada punta.</summary>
public enum GanchoVarilla
{
    Ninguno,

    /// <summary>90°, de 12 diametros: el de las corridas y los bastones.</summary>
    De90,

    /// <summary>135°: el sismico del estribo.</summary>
    De135
}

/// <summary>
/// Hacia donde dobla un gancho, como lo pide Revit: a la izquierda o a la derecha de la
/// varilla mirandola con su normal hacia uno.
/// </summary>
public enum LadoGancho
{
    Izquierda,
    Derecha
}

/// <summary>Una varilla lista para crear en Revit.</summary>
/// <param name="Puntos">Su eje, como poligonal: dos puntos una recta, cinco un estribo.</param>
/// <param name="Normal">
/// La normal del plano de la varilla. En las longitudinales, la X de la seccion; en los
/// estribos, el eje de la pieza, que es tambien hacia donde se reparte el juego.
/// </param>
/// <param name="Cuenta">Cuantas iguales lleva el juego; 1 si va sola.</param>
/// <param name="PasoM">La separacion del juego.</param>
/// <param name="Que">Para el informe: <c>corrida</c>, <c>baston</c>, <c>estribo</c>.</param>
public sealed record VarillaArmada(
    string Clave, double DiamM, EstiloVarilla Estilo, List<V3> Puntos,
    GanchoVarilla GanchoIni, GanchoVarilla GanchoFin, LadoGancho LadoIni, LadoGancho LadoFin,
    V3 Normal, int Cuenta, double PasoM, string Que);

/// <summary>Un juego de estribos iguales a la misma separacion.</summary>
public readonly record struct JuegoDeEstribos(double InicioM, int Cuenta, double PasoM);

/// <summary>
/// Arma las varillas de una pieza a partir de su armado y de donde esta.
/// </summary>
public static class PlanDeArmado
{
    /// <summary>
    /// Junta los estribos en juegos de separacion constante, que es como los reparte Revit:
    /// un juego por zona en vez de cien estribos sueltos.
    /// </summary>
    /// <remarks>
    /// Se corta un juego en cuanto la separacion cambia mas de 1 mm. Un estribo suelto -el de
    /// una frontera entre zonas, por ejemplo- queda como juego de uno.
    /// </remarks>
    public static List<JuegoDeEstribos> Juegos(IReadOnlyList<double> centros)
    {
        var res = new List<JuegoDeEstribos>();
        var orden = centros.OrderBy(c => c).ToList();
        var i = 0;

        while (i < orden.Count)
        {
            if (i == orden.Count - 1)
            {
                res.Add(new JuegoDeEstribos(orden[i], 1, 0));
                break;
            }

            var paso = orden[i + 1] - orden[i];
            var j = i + 1;

            while (j + 1 < orden.Count && Math.Abs((orden[j + 1] - orden[j]) - paso) <= 0.001)
            {
                j++;
            }

            res.Add(new JuegoDeEstribos(orden[i], j - i + 1, paso));
            i = j + 1;
        }

        return res;
    }

    /// <summary>
    /// De que lado queda el gancho para Revit, a partir de hacia donde tiene que doblar.
    /// </summary>
    /// <param name="normal">La normal de la varilla.</param>
    /// <param name="tangente">La direccion de la varilla en esa punta, del arranque al final.</param>
    /// <param name="haciaDonde">Hacia donde tiene que ir el gancho.</param>
    /// <remarks>
    /// <b>Es la suposicion mas delicada de todo el armado</b>, porque no se ha podido comprobar
    /// en Revit: la izquierda se toma como <c>normal x tangente</c>. Si en la primera prueba los
    /// ganchos salen hacia fuera de la pieza, se invierte aqui y en ningun otro sitio.
    /// </remarks>
    public static LadoGancho Lado(V3 normal, V3 tangente, V3 haciaDonde) =>
        normal.Cruz(tangente).Punto(haciaDonde) >= 0 ? LadoGancho.Izquierda : LadoGancho.Derecha;

    /// <summary>
    /// Cuanto se mete hacia dentro cada varilla de ESQUINA para quedar asentada en el doblez del
    /// estribo, en cm por cada lado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En el plano de AutoCAD el estribo da la vuelta ABRAZANDO la varilla de la esquina. En
    /// Revit el doblez del estribo lo pone el tipo de armadura -su «Diametro de curvatura de
    /// estribo/tirante», 4 db por norma- y es mas abierto que la varilla: con la varilla puesta
    /// como si la esquina fuera en escuadra, quedaba un hueco entre la varilla y el doblez y
    /// el estribo no la abrazaba.
    /// </para>
    /// <para>
    /// Asentada, la varilla toca el doblez por dentro: su centro va sobre la diagonal, a
    /// <c>R − r</c> del centro del doblez. Medido desde las caras interiores del estribo eso da
    /// <c>R − (R − r)/√2</c>, en lugar de <c>r</c>. Con un doblez que no pasa de la varilla no se
    /// mueve nada.
    /// </para>
    /// </remarks>
    /// <param name="radioInteriorCm">El radio interior del doblez del estribo.</param>
    /// <param name="radioVarillaCm">El radio de la varilla de la esquina.</param>
    public static double AsientoEnElDoblez(double radioInteriorCm, double radioVarillaCm) =>
        radioInteriorCm > radioVarillaCm
            ? (radioInteriorCm - radioVarillaCm) * (1 - (1 / Math.Sqrt(2)))
            : 0;

    /// <summary>Todas las varillas de una pieza.</summary>
    /// <param name="radioInteriorEstriboCm">
    /// El radio interior del doblez del estribo en Revit -medio «Diametro de curvatura de
    /// estribo/tirante» de su tipo de armadura-. Con el, las varillas de esquina se asientan en
    /// el doblez, como en el plano de AutoCAD. Cero: como estan en la tabla.
    /// </param>
    public static List<VarillaArmada> Armar(
        ArmadoJson a, ArmadoBarraJson ab, MarcoPieza m, double radioInteriorEstriboCm = 0)
    {
        var res = new List<VarillaArmada>();
        var rec = a.RecubrimientoCm / 100;
        var horizontal = a.EsHorizontal;

        // Las esquinas: la X mas chica o mas grande Y la Y mas chica o mas grande de todas.
        var xs = a.Varillas.Select(v => v.XCm).DefaultIfEmpty().ToList();
        var ys = a.Varillas.Select(v => v.YCm).DefaultIfEmpty().ToList();
        var (xMin, xMax, yMin, yMax) = (xs.Min(), xs.Max(), ys.Min(), ys.Max());

        // ---------- Las longitudinales ----------
        //
        // De recubrimiento a recubrimiento a lo largo de la pieza. En la trabe las de los lechos
        // de arriba y de abajo llevan su gancho de 90° en las dos puntas, doblado hacia dentro,
        // como en el alzado de CadLink; las laterales van rectas. En la columna, todas rectas.
        foreach (var v in a.Varillas)
        {
            var (x, y) = (v.XCm, v.YCm);

            var izq = Math.Abs(x - xMin) < 0.5;
            var der = Math.Abs(x - xMax) < 0.5;
            var aba = Math.Abs(y - yMin) < 0.5;
            var arr = Math.Abs(y - yMax) < 0.5;

            if (a.DiamEstriboCm > 0 && (izq || der) && (aba || arr) && xMax - xMin > 1 && yMax - yMin > 1)
            {
                var d = AsientoEnElDoblez(radioInteriorEstriboCm, v.DiamCm / 2);
                x += izq ? d : -d;
                y += aba ? d : -d;
            }

            var p1 = m.En(x, y, rec);
            var p2 = m.En(x, y, m.LargoM - rec);

            var conGancho = horizontal && (v.Lecho is "Superior" or "Inferior");
            var hacia = v.Lecho == "Superior" ? m.Ey * -1 : m.Ey;

            res.Add(Longitudinal(v.Clave, v.DiamCm, p1, p2, conGancho, conGancho, hacia, m.Ex,
                v.Lecho == "Lateral" ? "lateral" : "corrida"));
        }

        // ---------- Los bastones ----------
        //
        // Ya vienen colocados: CadLink calculo sus tramos con la longitud de esta pieza, con la
        // misma cuenta del alzado.
        foreach (var t in ab.Bastones)
        {
            var hacia = t.Lecho == "Superior" ? m.Ey * -1 : m.Ey;

            foreach (var x in t.XsCm)
            {
                res.Add(Longitudinal(t.Clave, t.DiamCm,
                    m.En(x, t.YCm, t.IniM), m.En(x, t.YCm, t.FinM),
                    t.GanchoIni, t.GanchoFin, hacia, m.Ex, "baston"));
            }
        }

        // ---------- Los estribos, en juegos ----------
        //
        // Por el eje del alambre: medio diametro por dentro del recubrimiento. Arranca y acaba
        // en la esquina de arriba a la derecha, que es donde CadLink pone el gancho sismico, y
        // da la vuelta por arriba, la izquierda y abajo.
        var de = a.DiamEstriboCm;

        if (de > 0 && ab.EstribosM.Count > 0)
        {
            var c = a.RecubrimientoCm + (de / 2);
            var x1 = c;
            var x2 = a.BaseCm - c;
            var y1 = c;
            var y2 = a.AlturaCm - c;

            if (x2 > x1 && y2 > y1)
            {
                foreach (var j in Juegos(ab.EstribosM))
                {
                    var s = j.InicioM;

                    var puntos = new List<V3>
                    {
                        m.En(x2, y2, s), m.En(x1, y2, s), m.En(x1, y1, s),
                        m.En(x2, y1, s), m.En(x2, y2, s)
                    };

                    // El gancho de 135° va hacia el centro de la seccion, en las dos puntas.
                    var centro = m.En(a.BaseCm / 2, a.AlturaCm / 2, s);
                    var haciaDentro = (centro - puntos[0]).Unitario();

                    var tIni = (puntos[1] - puntos[0]).Unitario();
                    var tFin = (puntos[4] - puntos[3]).Unitario();

                    res.Add(new VarillaArmada(
                        a.ClaveEstribo, de / 100, EstiloVarilla.Estribo, puntos,
                        GanchoVarilla.De135, GanchoVarilla.De135,
                        Lado(m.Eje, tIni, haciaDentro), Lado(m.Eje, tFin, haciaDentro),
                        m.Eje, j.Cuenta, j.PasoM, "estribo"));
                }
            }
        }

        return res;
    }

    private static VarillaArmada Longitudinal(
        string clave, double diamCm, V3 p1, V3 p2, bool ganchoIni, bool ganchoFin,
        V3 hacia, V3 normal, string que)
    {
        var t = (p2 - p1).Unitario();
        var lado = Lado(normal, t, hacia);

        return new VarillaArmada(
            clave, diamCm / 100, EstiloVarilla.Longitudinal, new List<V3> { p1, p2 },
            ganchoIni ? GanchoVarilla.De90 : GanchoVarilla.Ninguno,
            ganchoFin ? GanchoVarilla.De90 : GanchoVarilla.Ninguno,
            lado, lado, normal, 1, 0, que);
    }
}
