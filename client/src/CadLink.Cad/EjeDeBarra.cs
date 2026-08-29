namespace CadLink.Cad;

/// <summary>
/// El <b>eje</b> de una barra de acero en el espacio: lo que hace falta para barrerla.
/// </summary>
/// <remarks>
/// <para>
/// Es la aritmética de <see cref="Jaula3dDrawer"/>, separada a propósito. El dibujante habla con
/// AutoCAD por COM y no se puede probar aquí; esto sí, y es donde está lo que puede salir mal de
/// verdad: la <b>orientación del perfil</b>. Barrer un círculo por un camino solo da una varilla
/// redonda si el círculo arranca <b>perpendicular</b> al camino. Si arranca torcido, la varilla
/// sale con la sección elíptica y más gruesa de lo que dice la tabla.
/// </para>
/// <para>
/// Y no es un detalle teórico: los estribos van en un plano horizontal —su tangente inicial es
/// horizontal— y las varillas longitudinales suben —su tangente es vertical—. Un círculo creado en
/// el plano XY, que es lo que da AutoCAD por omisión, está bien para la varilla y <b>mal girado
/// noventa grados</b> para el estribo.
/// </para>
/// </remarks>
public static class EjeDeBarra
{
    private const double Nada = 1e-9;

    /// <summary>
    /// Quita los puntos <b>repetidos seguidos</b> del recorrido.
    /// </summary>
    /// <remarks>
    /// Salen solos donde una recta empalma con un doblez, y hay que quitarlos: dos puntos iguales
    /// no tienen dirección, así que la tangente ahí es indefinida y AutoCAD rechaza el camino o lo
    /// barre en un pico. Es la misma limpieza que hace <see cref="TuboDeMalla"/> antes de generar
    /// sus anillos, y por el mismo motivo.
    /// </remarks>
    public static List<(double X, double Y, double Z)> Limpio(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double tol = 1e-7)
    {
        var salida = new List<(double X, double Y, double Z)>();

        if (eje is null)
        {
            return salida;
        }

        foreach (var p in eje)
        {
            if (salida.Count > 0 && Distancia(salida[^1], p) <= tol)
            {
                continue;
            }

            salida.Add(p);
        }

        return salida;
    }

    /// <summary>
    /// La <b>tangente</b> al principio del recorrido, unitaria. Nula si no hay recorrido.
    /// </summary>
    /// <remarks>
    /// Es la normal que hay que darle al círculo del perfil para que arranque perpendicular al
    /// camino. Se toma del <b>primer tramo con largo</b> y no del primer par de puntos a secas:
    /// con un punto repetido al principio —que pasa— el primer par daría dirección cero y el
    /// círculo se quedaría en el plano de AutoCAD, o sea mal girado.
    /// </remarks>
    public static (double X, double Y, double Z) TangenteInicial(
        IReadOnlyList<(double X, double Y, double Z)>? eje)
    {
        if (eje is null || eje.Count < 2)
        {
            return (0, 0, 0);
        }

        for (var i = 1; i < eje.Count; i++)
        {
            var dx = eje[i].X - eje[0].X;
            var dy = eje[i].Y - eje[0].Y;
            var dz = eje[i].Z - eje[0].Z;

            var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (largo > Nada)
            {
                return (dx / largo, dy / largo, dz / largo);
            }
        }

        return (0, 0, 0);
    }

    /// <summary>El largo total del recorrido, sumando tramo a tramo.</summary>
    /// <remarks>
    /// Sirve para descartar lo que no da para una varilla y para poder decir cuánto acero se
    /// dibujó, que es un número que el usuario puede comparar con su tabla.
    /// </remarks>
    public static double Largo(IReadOnlyList<(double X, double Y, double Z)>? eje)
    {
        if (eje is null || eje.Count < 2)
        {
            return 0;
        }

        double total = 0;

        for (var i = 1; i < eje.Count; i++)
        {
            total += Distancia(eje[i - 1], eje[i]);
        }

        return total;
    }

    /// <summary>¿El recorrido <b>vuelve a su principio</b>?</summary>
    /// <remarks>
    /// Un estribo es cerrado y una varilla no. Importa para el camino que se le da a AutoCAD: uno
    /// cerrado tiene que cerrarse de verdad, o el barrido deja una muesca en la esquina donde
    /// empezó.
    /// </remarks>
    public static bool Cerrado(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double tol = 1e-6) =>
        eje is not null && eje.Count > 2 && Distancia(eje[0], eje[^1]) <= tol;

    /// <summary>El recorrido en la tira plana de tres en tres que espera AutoCAD.</summary>
    /// <remarks>
    /// AutoCAD recibe los vértices de una polilínea 3D como un solo arreglo de dobles
    /// —x, y, z, x, y, z…—, no como una lista de puntos. Se arma aquí para que el dibujante no
    /// tenga que hacer aritmética de índices con COM de por medio.
    /// </remarks>
    public static double[] Tira(IReadOnlyList<(double X, double Y, double Z)> eje)
    {
        var tira = new double[eje.Count * 3];

        for (var i = 0; i < eje.Count; i++)
        {
            tira[3 * i] = eje[i].X;
            tira[(3 * i) + 1] = eje[i].Y;
            tira[(3 * i) + 2] = eje[i].Z;
        }

        return tira;
    }

    /// <summary>
    /// El mismo recorrido con <b>menos vértices</b>, quitando los que casi no doblan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La vista previa dibuja cada doblez con <b>catorce muestras</b>, porque en pantalla se ve
    /// la curva y catorce segmentos ya parecen redondos. Pero en AutoCAD cada tramo del eje es un
    /// sólido, y un estribo tiene cuatro esquinas más dos ganchos: seis dobleces por catorce son
    /// <b>ochenta y cuatro sólidos por estribo</b>. Con treinta estribos son dos mil quinientos.
    /// Eso no es un dibujo, es un dibujo que no se puede abrir.
    /// </para>
    /// <para>
    /// Así que antes de dibujar se quitan los vértices que no aportan. Y se quitan por
    /// <b>cuánto se separaría la varilla de su sitio</b>, no por cuántos grados dobla: se guarda
    /// el vértice que más se aleja de la recta que une los dos que ya están guardados, y se repite
    /// mientras alguno se aleje más de <paramref name="tolerancia"/>. Es Douglas-Peucker.
    /// </para>
    /// <para>
    /// <b>Por qué por distancia y no por ángulo.</b> Porque la tolerancia pasa a ser una medida
    /// que se puede razonar: «que la varilla no se salga de su eje más de tanto». Y porque se
    /// <b>adapta sola</b>, que es lo que hacía falta para los ganchos. Un gancho es un doblez muy
    /// cerrado en un radio pequeño, y una regla de grados le daba los mismos tramos que a la
    /// esquina ancha de un estribo: el gancho salía con esquinas y la esquina, de sobra. Por
    /// distancia, el doblez cerrado recibe más tramos y el ancho menos, sin decirle nada.
    /// </para>
    /// <para>
    /// <b>Y las esquinas vivas no se pueden perder</b>, que es de lo que está hecho un estribo.
    /// No se pierden por construcción: en una esquina de noventa grados el vértice está lejísimos
    /// de la recta que une sus vecinos, así que siempre es el primero que se guarda.
    /// </para>
    /// <para>
    /// <b>Los extremos no se tocan nunca.</b> El primer y el último punto siempre se guardan: son
    /// dónde arranca y dónde acaba la varilla, y el usuario los va a medir contra su tabla. Se
    /// acorta por dentro, no por las puntas. Y por lo mismo un recorrido cerrado —un estribo—
    /// sigue cerrado al salir, porque su último punto es su primero.
    /// </para>
    /// </remarks>
    /// <param name="tolerancia">
    /// Cuánto se permite que la varilla se separe de su eje, <b>en las unidades del eje</b>. Cero
    /// o menos deja el recorrido tal cual.
    /// </param>
    public static List<(double X, double Y, double Z)> Simplificado(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double tolerancia)
    {
        var limpio = Limpio(eje);

        if (limpio.Count < 3 || tolerancia <= 0)
        {
            return limpio;
        }

        var guardar = new bool[limpio.Count];

        // Las dos puntas, siempre.
        guardar[0] = true;
        guardar[^1] = true;

        Partir(limpio, 0, limpio.Count - 1, tolerancia, guardar);

        var salida = new List<(double X, double Y, double Z)>();

        for (var i = 0; i < limpio.Count; i++)
        {
            if (guardar[i])
            {
                salida.Add(limpio[i]);
            }
        }

        return salida;
    }

    /// <summary>
    /// El paso de Douglas-Peucker: guarda el vértice que más se separa de la cuerda y repite a los
    /// dos lados.
    /// </summary>
    /// <remarks>
    /// La recursión no se dispara: cada llamada parte el trozo en dos por su peor vértice, así que
    /// la profundidad es la del árbol de particiones, y un eje de armado trae unos cientos de
    /// puntos. Se sale en cuanto ningún vértice del trozo se separa más de la tolerancia, que es
    /// lo que da la garantía: <b>ningún punto del original queda a más de la tolerancia</b> del
    /// recorrido que sale.
    /// </remarks>
    private static void Partir(
        List<(double X, double Y, double Z)> p, int i, int j, double tol, bool[] guardar)
    {
        if (j <= i + 1)
        {
            return;
        }

        var peor = -1;
        var dPeor = 0d;

        for (var k = i + 1; k < j; k++)
        {
            var d = AlSegmento(p[k], p[i], p[j]);

            if (d > dPeor)
            {
                dPeor = d;
                peor = k;
            }
        }

        if (peor < 0 || dPeor <= tol)
        {
            return;
        }

        guardar[peor] = true;

        Partir(p, i, peor, tol, guardar);
        Partir(p, peor, j, tol, guardar);
    }

    /// <summary>Distancia de un punto al <b>segmento</b> a–b, no a su recta.</summary>
    /// <remarks>
    /// Al segmento y no a la recta infinita, y hay un caso donde importa de verdad: en un
    /// recorrido <b>cerrado</b> el primer punto y el último son el mismo, así que la primera
    /// cuerda tiene largo cero. Contra una recta infinita eso no está definido; contra un
    /// segmento degenerado la distancia es, sin más, la distancia a ese punto, y entonces el
    /// reparto arranca por el vértice más lejano y sigue solo.
    /// </remarks>
    private static double AlSegmento(
        (double X, double Y, double Z) p,
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var vz = b.Z - a.Z;

        var largo2 = (vx * vx) + (vy * vy) + (vz * vz);

        if (largo2 <= Nada)
        {
            return Distancia(p, a);
        }

        var t = (((p.X - a.X) * vx) + ((p.Y - a.Y) * vy) + ((p.Z - a.Z) * vz)) / largo2;

        t = Math.Clamp(t, 0d, 1d);

        return Distancia(p, (a.X + (vx * t), a.Y + (vy * t), a.Z + (vz * t)));
    }

    /// <summary>
    /// El recorrido partido en <b>tramos</b>, cada uno alargado en las uniones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada tramo se va a dibujar como un cilindro suelto, y dos cilindros que se tocan justo en
    /// la punta dejan una <b>muesca en la parte de fuera del doblez</b>: la esquina queda comida.
    /// Se arregla alargando cada tramo <paramref name="alargue"/> por su propio eje, de modo que
    /// los dos cilindros se solapen y el doblez quede lleno. Con el <b>radio de la varilla</b>
    /// como alargue basta para cualquier doblez de armado.
    /// </para>
    /// <para>
    /// <b>Pero solo por dentro.</b> Alargar también las dos puntas libres haría la varilla más
    /// larga que la de la tabla, y eso es un error que el usuario ve en cuanto acota. Así que se
    /// alarga en las uniones y <b>no</b> en el principio ni en el final. En un recorrido cerrado
    /// —un estribo— no hay puntas libres: la unión del principio con el final también es unión, y
    /// también se alarga.
    /// </para>
    /// </remarks>
    public static List<((double X, double Y, double Z) A, (double X, double Y, double Z) B)> Tramos(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double alargue = 0)
    {
        var salida =
            new List<((double X, double Y, double Z) A, (double X, double Y, double Z) B)>();

        if (eje is null || eje.Count < 2)
        {
            return salida;
        }

        var cerrado = Cerrado(eje);

        var ultimo = eje.Count - 1;

        for (var i = 1; i <= ultimo; i++)
        {
            var a = eje[i - 1];
            var b = eje[i];

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var dz = b.Z - a.Z;

            var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (largo <= Nada)
            {
                continue;
            }

            var ux = dx / largo;
            var uy = dy / largo;
            var uz = dz / largo;

            // Hacia atras solo si antes hay otro tramo; hacia delante solo si viene otro.
            var atras = (i - 1 > 0 || cerrado) ? alargue : 0;
            var delante = (i < ultimo || cerrado) ? alargue : 0;

            salida.Add((
                (a.X - (ux * atras), a.Y - (uy * atras), a.Z - (uz * atras)),
                (b.X + (ux * delante), b.Y + (uy * delante), b.Z + (uz * delante))));
        }

        return salida;
    }

    /// <summary>
    /// La matriz 4×4 que lleva un cilindro <b>hecho en el origen y de pie</b> al tramo
    /// <paramref name="a"/>–<paramref name="b"/>. Nula si el tramo no tiene largo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AutoCAD solo sabe hacer cilindros <b>verticales y centrados</b> en el punto que se le dice.
    /// Para poner uno a lo largo de un tramo cualquiera se hace en el origen y se transforma. La
    /// traslación es el <b>punto medio</b> del tramo, no su principio, precisamente porque el
    /// cilindro nace centrado: de <c>−largo/2</c> a <c>+largo/2</c> en su Z.
    /// </para>
    /// <para>
    /// Las tres primeras columnas son un marco ortonormal cuya tercera, <c>w</c>, es la dirección
    /// del tramo. Las otras dos <b>da igual cómo salgan</b> —un círculo es igual por donde se lo
    /// mire, así que girar el cilindro sobre su eje no cambia nada— y por eso aquí no hay el
    /// cuidado que sí hace falta en <see cref="Modelo3dDrawer"/>, donde el perfil es una viga con
    /// su alma y tiene que quedar de pie.
    /// </para>
    /// <para>
    /// <b>El marco queda derecho, no espejado.</b> Se toma <c>u</c> perpendicular a la dirección y
    /// luego <c>v = w × u</c>, y entonces <c>u × v = w</c> sale por identidad. Un marco espejado
    /// tiene determinante negativo y AutoCAD lo aplicaría volteando el sólido.
    /// </para>
    /// <para>
    /// Y la perpendicular de arranque se elige mirando <b>cuál de los ejes del mundo es menos
    /// paralelo</b> al tramo. Tomar siempre la vertical se anularía en cada varilla longitudinal,
    /// que son todas verticales: no es un caso raro que se pueda ignorar, es la mitad del acero.
    /// </para>
    /// </remarks>
    public static double[,]? MatrizDeTramo(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;

        var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (largo <= Nada)
        {
            return null;
        }

        var w = new[] { dx / largo, dy / largo, dz / largo };

        // El eje del mundo menos paralelo al tramo: asi el producto vectorial nunca se anula.
        var h = Math.Abs(w[2]) < 0.9
            ? new[] { 0d, 0d, 1d }
            : new[] { 1d, 0d, 0d };

        var u = Cruz(h, w);

        var n = Math.Sqrt((u[0] * u[0]) + (u[1] * u[1]) + (u[2] * u[2]));

        u = new[] { u[0] / n, u[1] / n, u[2] / n };

        // v = w x u, y entonces u x v = w: el marco queda derecho.
        var v = Cruz(w, u);

        // Por FILAS, como la quiere AutoCAD, y con el PUNTO MEDIO en la cuarta columna.
        return new[,]
        {
            { u[0], v[0], w[0], (a.X + b.X) / 2 },
            { u[1], v[1], w[1], (a.Y + b.Y) / 2 },
            { u[2], v[2], w[2], (a.Z + b.Z) / 2 },
            { 0d,   0d,   0d,   1d              }
        };
    }

    private static double[] Cruz(double[] p, double[] q) =>
        new[]
        {
            (p[1] * q[2]) - (p[2] * q[1]),
            (p[2] * q[0]) - (p[0] * q[2]),
            (p[0] * q[1]) - (p[1] * q[0])
        };

    private static double Distancia(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
