namespace CadLink.Cad;

/// <summary>
/// El <b>eje</b> del estribo rectangular: el recorrido que sigue la varilla, con sus
/// dobleces redondeados y su gancho sísmico.
/// </summary>
/// <remarks>
/// <para>
/// <b>Para qué hace falta.</b> El dibujo del corte no traza el eje: traza las dos caras del
/// estribo, la de dentro y la de fuera, como dos rectángulos redondeados concéntricos. Eso
/// está bien en un corte, donde el estribo se ve de canto. Pero la vista en 3D dibuja cada
/// tramo como una <b>barra con grueso</b>, y para eso hace falta el eje, no las caras: si se
/// dibujara una cara con grueso, el estribo saldría corrido medio diámetro.
/// </para>
/// <para>
/// <b>Los radios salen del corte, no de aquí.</b> En <c>EstriboExterior</c> el radio de la
/// cara de fuera es <c>dEst + dVar/2</c> y el de la cara de dentro <c>dVar/2</c>, las dos
/// con el mismo centro —el de la varilla de la esquina—. El eje va justo en medio, así que
/// su radio es <c>(dEst + dVar) / 2</c>. No es una elección: es la consecuencia de que el
/// doblez del estribo <b>envuelve la varilla de la esquina</b>, que es lo que se hace en
/// obra y lo que ya dibuja el corte.
/// </para>
/// <para>
/// <b>El gancho son dos colas paralelas, no una.</b> Los dos extremos del estribo se juntan
/// en la esquina y los dos doblan 135° hacia el núcleo. Uno llega subiendo por el costado y
/// da la vuelta por encima; el otro llega por arriba y da la vuelta por el costado. Sus
/// puntos de salida quedan <b>diametralmente opuestos</b> en el doblez —a 135° y a −45°—,
/// así que las dos colas salen paralelas y separadas un diámetro de doblez. Es la razón de
/// que un gancho sísmico se vea como dos rayas y no como una.
/// </para>
/// <para>
/// Vive aquí, sin COM ni WPF, por lo mismo que <see cref="TrazoDiamante"/> y
/// <see cref="TrazoGrapa"/>: lo necesita la vista previa y se puede probar. La prueba está
/// en <c>tools/prueba-trazo-estribo</c>.
/// </para>
/// </remarks>
public static class TrazoEstribo
{
    /// <summary>En cuántos tramos rectos se parte cada doblez.</summary>
    /// <remarks>
    /// Seis por cuadrante es de sobra para que un doblez no se vea facetado a los tamaños
    /// de la vista previa, y multiplica por poco el número de figuras. El contorno de la
    /// grapa usa dieciocho por doblez, pero ahí el doblez ES la pieza; aquí un estribo lleva
    /// cuatro y se repite en cada posición de la tabla.
    /// </remarks>
    public const int TramosPorDoblez = 6;

    /// <summary>El recorrido del estribo: su cuerpo y las colas del gancho.</summary>
    /// <param name="Cuerpo">
    /// El recorrido de la varilla. <b>Abierto</b> cuando lleva gancho —empieza y acaba en la
    /// esquina del gancho, que es donde se juntan los dos extremos— y cerrado cuando no.
    /// </param>
    /// <param name="Colas">Las dos colas del gancho, cada una con su doblez. Vacío si no lleva.</param>
    /// <param name="Cerrado">Si <paramref name="Cuerpo"/> se cierra sobre sí mismo.</param>
    public readonly record struct Trazo(
        List<(double X, double Y)> Cuerpo,
        List<List<(double X, double Y)>> Colas,
        bool Cerrado);

    /// <summary>
    /// El eje del estribo, con el gancho en la esquina <b>superior derecha</b>.
    /// </summary>
    /// <param name="x1">Izquierda del rectángulo del EJE.</param>
    /// <param name="y1">Abajo del rectángulo del EJE.</param>
    /// <param name="x2">Derecha del rectángulo del EJE.</param>
    /// <param name="y2">Arriba del rectángulo del EJE.</param>
    /// <param name="rSup">Radio del eje en los dobleces de arriba.</param>
    /// <param name="rInf">Radio del eje en los dobleces de abajo.</param>
    /// <param name="gancho">Largo recto de cada cola. Cero o menos, sin gancho.</param>
    /// <remarks>
    /// La esquina del gancho es la de arriba a la derecha porque es la que usa el dibujo del
    /// corte, y las dos vistas tienen que enseñar el estribo con el gancho en el mismo sitio.
    /// </remarks>
    /// <returns>El recorrido, o <c>null</c> si el rectángulo no da para un estribo.</returns>
    public static Trazo? Eje(
        double x1, double y1, double x2, double y2,
        double rSup, double rInf, double gancho,
        int tramosPorDoblez = TramosPorDoblez)
    {
        var ancho = x2 - x1;
        var alto = y2 - y1;

        if (ancho <= 0 || alto <= 0)
        {
            return null;
        }

        // Los radios no pueden pasar de la mitad del lado más corto: con más, los dos
        // dobleces de un lado se solaparían y el recorrido se cruzaría consigo mismo. Es el
        // mismo tope que pone el dibujo del corte antes de trazar los fillets.
        var rMax = Math.Min(ancho, alto) / 2;

        rSup = Math.Clamp(rSup, 0, rMax);
        rInf = Math.Clamp(rInf, 0, rMax);

        var tramos = Math.Max(1, tramosPorDoblez);

        // El centro del doblez de la esquina del gancho, que es el de la varilla que
        // envuelve.
        var cgX = x2 - rSup;
        var cgY = y2 - rSup;

        var cuerpo = new List<(double X, double Y)>();

        // Se arranca en la tangencia de ARRIBA de la esquina del gancho y se recorre en
        // sentido contrario a las agujas del reloj, para acabar en su tangencia del costado
        // DERECHO. Así el hueco que queda entre el principio y el fin es exactamente donde
        // van los dos dobleces del gancho.
        cuerpo.Add((cgX, y2));

        // Arriba, hacia la izquierda.
        cuerpo.Add((x1 + rSup, y2));
        Arco(cuerpo, x1 + rSup, y2 - rSup, rSup, 0.5 * Math.PI, Math.PI, tramos);

        // Costado izquierdo, bajando.
        cuerpo.Add((x1, y1 + rInf));
        Arco(cuerpo, x1 + rInf, y1 + rInf, rInf, Math.PI, 1.5 * Math.PI, tramos);

        // Abajo, hacia la derecha.
        cuerpo.Add((x2 - rInf, y1));
        Arco(cuerpo, x2 - rInf, y1 + rInf, rInf, 1.5 * Math.PI, 2 * Math.PI, tramos);

        // Costado derecho, subiendo hasta la tangencia de la esquina del gancho.
        cuerpo.Add((x2, cgY));

        var colas = new List<List<(double X, double Y)>>();

        if (gancho <= 0 || rSup <= 0)
        {
            // Sin gancho la esquina se cierra con su doblez normal de 90°.
            Arco(cuerpo, cgX, cgY, rSup, 0, 0.5 * Math.PI, tramos);

            return new Trazo(Limpiar(cuerpo), colas, true);
        }

        // ---------- Las dos colas ----------
        //
        // La cola apunta al NÚCLEO, o sea por la diagonal de la esquina hacia dentro: desde
        // la esquina de arriba a la derecha, eso es 225°. Es la misma dirección que usa el
        // gancho del corte.
        const double haciaElNucleo = 1.25 * Math.PI;

        var ux = Math.Cos(haciaElNucleo);
        var uy = Math.Sin(haciaElNucleo);

        // El largo se recorta para que la cola no cruce el estribo y salga por el otro
        // lado. La diagonal del núcleo es el tope natural.
        var tope = 0.9 * Math.Sqrt((ancho * ancho) + (alto * alto)) / 2;
        var largo = Math.Min(gancho, tope);

        // Extremo que llega SUBIENDO por el costado: entra al doblez en 0° y barre 135° en
        // sentido antihorario.
        colas.Add(Cola(cgX, cgY, rSup, 0, 0.75 * Math.PI, largo, ux, uy, tramos));

        // Extremo que llega por ARRIBA: entra en 90° y barre 135° en sentido horario, así
        // que sale por el punto opuesto del doblez y su cola queda paralela a la otra.
        colas.Add(Cola(cgX, cgY, rSup, 0.5 * Math.PI, -0.75 * Math.PI, largo, ux, uy, tramos));

        return new Trazo(Limpiar(cuerpo), colas, false);
    }

    /// <summary>
    /// El eje de un <b>gancho sísmico que envuelve una barra</b>, suelto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el gancho como pieza aparte, no el del estribo rectangular: lo usan el <b>diamante</b>
    /// —que cierra sus dos extremos en el vértice izquierdo, agarrado a la varilla lateral que
    /// abraza ahí— y cualquier otro remate que envuelva una barra.
    /// </para>
    /// <para>
    /// <b>Las reglas salen de <c>GanchoDelDiamante</c></b>, en el dibujante de AutoCAD, que es
    /// donde estaban escritas: el doblez es <b>media vuelta</b> alrededor de la barra y su punto
    /// medio cae en el lado <b>opuesto</b> a las colas, o sea que el acero pasa por detrás de la
    /// barra y sale hacia el núcleo. Las dos colas salen <b>paralelas</b> y separadas el
    /// diámetro del doblez, que es lo que hace que un gancho se vea como dos rayas.
    /// </para>
    /// <para>
    /// Está aquí porque la geometría del dibujante está enredada con llamadas COM y no se puede
    /// llamar desde la vista previa. Sacarla es lo que permite que el 3D enseñe el mismo gancho
    /// que el plano en lugar de una copia que se pueda desviar.
    /// </para>
    /// </remarks>
    /// <param name="bx">X del centro de la barra que envuelve.</param>
    /// <param name="by">Y del centro de la barra que envuelve.</param>
    /// <param name="rEje">
    /// Radio del <b>eje</b> del gancho alrededor de esa barra: el radio de la barra más medio
    /// diámetro del acero del gancho.
    /// </param>
    /// <param name="ux">Hacia dónde apuntan las colas, unitario. Normalmente al núcleo.</param>
    /// <param name="uy">Hacia dónde apuntan las colas, unitario.</param>
    /// <param name="barrido">Cuánto envuelve el doblez, en radianes. Media vuelta es <c>π</c>.</param>
    /// <param name="cola">Largo recto de cada cola.</param>
    /// <returns>El recorrido abierto, de la punta de una cola a la de la otra.</returns>
    public static List<(double X, double Y)>? GanchoAlrededorDeBarra(
        double bx, double by, double rEje,
        double ux, double uy, double barrido, double cola,
        int tramosPorDoblez = TramosPorDoblez)
    {
        var largoU = Math.Sqrt((ux * ux) + (uy * uy));

        if (rEje <= 0 || cola < 0 || largoU < 1e-9 || Math.Abs(barrido) < 1e-9)
        {
            return null;
        }

        ux /= largoU;
        uy /= largoU;

        // Las dos normales a la dirección de las colas: por ahí arranca y remata el doblez.
        var n1X = -uy;
        var n1Y = ux;

        var a1 = Math.Atan2(n1Y, n1X);

        var puntos = new List<(double X, double Y)>();

        // La punta de la primera cola, hacia fuera del doblez.
        puntos.Add((bx + (rEje * n1X) + (cola * ux), by + (rEje * n1Y) + (cola * uy)));

        // El doblez, que arranca justo donde acaba esa cola.
        Arco(puntos, bx, by, rEje, a1, a1 + barrido, Math.Max(1, tramosPorDoblez));

        // Y la punta de la segunda, que sale del final del doblez en la misma dirección.
        var fin = puntos[^1];

        puntos.Add((fin.X + (cola * ux), fin.Y + (cola * uy)));

        return puntos;
    }

    /// <summary>Una cola del gancho: su doblez y el tramo recto.</summary>
    private static List<(double X, double Y)> Cola(
        double cx, double cy, double r,
        double aIni, double barrido, double largo,
        double ux, double uy, int tramos)
    {
        var puntos = new List<(double X, double Y)>();

        Arco(puntos, cx, cy, r, aIni, aIni + barrido, tramos);

        var fin = puntos[^1];

        puntos.Add((fin.X + (largo * ux), fin.Y + (largo * uy)));

        return puntos;
    }

    /// <summary>Añade un arco muestreado, del ángulo <paramref name="a0"/> al a1.</summary>
    /// <remarks>
    /// El número de tramos se reparte por el barrido de verdad, no fijo: un doblez de 135°
    /// necesita la mitad más de tramos que uno de 90° para verse igual de redondo.
    /// </remarks>
    private static void Arco(
        List<(double X, double Y)> puntos,
        double cx, double cy, double r, double a0, double a1, int tramos)
    {
        if (r <= 0)
        {
            puntos.Add((cx, cy));
            return;
        }

        var cuantos = Math.Max(
            1, (int)Math.Ceiling(tramos * Math.Abs(a1 - a0) / (0.5 * Math.PI)));

        for (var i = 0; i <= cuantos; i++)
        {
            var a = a0 + ((a1 - a0) * i / cuantos);

            puntos.Add((cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a))));
        }
    }

    /// <summary>Quita los puntos repetidos seguidos.</summary>
    /// <remarks>
    /// Salen solos donde una recta empalma con un doblez: la tangencia se añade una vez al
    /// terminar el lado y otra al arrancar el arco. Dos puntos iguales seguidos darían una
    /// barra de largo cero, que no se ve pero se cuenta.
    /// </remarks>
    private static List<(double X, double Y)> Limpiar(List<(double X, double Y)> puntos)
    {
        var limpio = new List<(double X, double Y)>();

        foreach (var p in puntos)
        {
            if (limpio.Count > 0
                && Math.Abs(limpio[^1].X - p.X) < 1e-9
                && Math.Abs(limpio[^1].Y - p.Y) < 1e-9)
            {
                continue;
            }

            limpio.Add(p);
        }

        return limpio;
    }
}
