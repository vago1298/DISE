namespace CadLink.Cad;

/// <summary>
/// Desplaza un contorno cerrado <b>hacia fuera</b>, paralelo a sí mismo.
/// </summary>
/// <remarks>
/// <para>
/// Existe para la <b>soldadura</b> de la placa base: el achurado del filete es la franja entre el
/// paño del perfil y ese mismo paño corrido hacia fuera el espesor de la soldadura. Y tiene que
/// seguir el contorno <b>de verdad</b>: la primera versión usaba el rectángulo envolvente del perfil
/// crecido el espesor, y en un perfil I eso no es una franja, es la caja entera rellena de rayado
/// con la I como isla. Se ve en el dibujo y no se parece a una soldadura.
/// </para>
/// <para>
/// Vive <b>aparte del dibujante y sin nada de COM</b>, igual que <see cref="AnclasPlacaBase"/> y
/// <see cref="CartabonesPlacaBase"/>. Aquí importa doble: es geometría con signos y casos, es lo
/// único de este arreglo que se puede comprobar sin AutoCAD delante, y la alternativa —el
/// <c>Offset</c> de AutoCAD que usaba la macro— obliga a crear una copia temporal del perfil, medir
/// los dos resultados, quedarse con el que creció y borrar el otro, un baile que depende de que
/// <c>Offset</c> se comporte igual en todas las versiones y que no se puede probar aquí.
/// </para>
/// </remarks>
public static class ContornoDesplazado
{
    /// <summary>Por debajo de esto, dos puntos son el mismo y una arista no tiene dirección.</summary>
    public const double Tolerancia = 1e-9;

    /// <summary>Gira 90° un arreglo plano de puntos alrededor de <c>(xc, yc)</c>.</summary>
    /// <remarks>
    /// El giro de la macro es <c>xd = xc - y ; yd = yc + x</c> sobre las coordenadas locales, que es
    /// un giro de +90°. Aquí los puntos vienen ya en coordenadas del dibujo, así que primero se
    /// llevan al centro, se giran y se devuelven.
    /// </remarks>
    public static double[] Girar90(double[] puntos, double xc, double yc)
    {
        var salida = new double[puntos.Length];

        for (var i = 0; i + 1 < puntos.Length; i += 2)
        {
            var (x, y) = Girar90Punto(puntos[i], puntos[i + 1], xc, yc);

            salida[i] = x;
            salida[i + 1] = y;
        }

        return salida;
    }

    /// <summary>Gira 90° un punto alrededor de <c>(xc, yc)</c>.</summary>
    public static (double X, double Y) Girar90Punto(double x, double y, double xc, double yc) =>
        (xc - (y - yc), yc + (x - xc));

    /// <summary>
    /// El área con <b>signo</b> del contorno: positiva si va antihorario.
    /// </summary>
    /// <remarks>
    /// Es lo que dice de qué lado está el «fuera», y por eso se calcula en lugar de suponerlo:
    /// <c>TrazoAcero</c> entrega unas formas en un sentido y otras en el contrario —el ángulo y la
    /// canal se espejean para dibujar el segundo perfil de una pareja— así que dar por hecho un
    /// sentido desplazaría la mitad de las formas hacia <b>dentro</b>, y el achurado saldría en el
    /// lado equivocado sin que nada avisara.
    /// </remarks>
    public static double AreaConSigno(double[]? puntos)
    {
        if (puntos is null || puntos.Length < 6 || puntos.Length % 2 != 0)
        {
            return 0;
        }

        var n = puntos.Length / 2;
        var suma = 0.0;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            suma += (puntos[2 * i] * puntos[(2 * j) + 1])
                    - (puntos[2 * j] * puntos[(2 * i) + 1]);
        }

        return suma / 2;
    }

    /// <summary>
    /// El mismo contorno con cada vértice corrido <paramref name="t"/> hacia fuera.
    /// </summary>
    /// <param name="puntos">Plano y cerrado: <c>x1,y1,x2,y2…</c>, como los quiere AutoCAD.</param>
    /// <param name="t">Cuánto se separa, en unidades de dibujo. Cero devuelve una copia.</param>
    /// <returns>Los puntos desplazados, o <c>null</c> si el contorno no da para desplazarse.</returns>
    /// <remarks>
    /// <para>
    /// Cada vértice sale de <b>cruzar sus dos aristas ya desplazadas</b>. Corriendo el vértice a lo
    /// largo de la bisectriz —que es el atajo obvio— la franja sale más angosta en las esquinas, y en
    /// una esquina de 90° un 30 % más angosta: la soldadura se vería adelgazar justo donde más
    /// material hay. Cruzando las aristas, el ancho de la franja es el mismo en todo el perímetro,
    /// que es lo que es un filete.
    /// </para>
    /// <para>
    /// <b>Los bulges no se tocan</b>, y no es un descuido: desplazar un arco hacia fuera le cambia el
    /// radio pero <b>no el ángulo que barre</b>, y el bulge de una polilínea es
    /// <c>tan(ángulo / 4)</c>. Así que el mismo bulge sobre los vértices nuevos describe el arco
    /// correcto. Vale para las esquinas redondeadas del tubo y para los dobleces de la canal y la
    /// zeta formadas en frío.
    /// </para>
    /// </remarks>
    public static double[]? HaciaFuera(double[]? puntos, double t)
    {
        if (puntos is null || puntos.Length < 6 || puntos.Length % 2 != 0)
        {
            return null;
        }

        if (Math.Abs(t) <= Tolerancia)
        {
            return (double[])puntos.Clone();
        }

        var area = AreaConSigno(puntos);

        if (Math.Abs(area) <= Tolerancia)
        {
            // Un contorno de área nula no tiene dentro ni fuera: no hay hacia dónde desplazarlo.
            return null;
        }

        var n = puntos.Length / 2;

        // Antihorario: el interior queda a la IZQUIERDA de cada arista, así que la normal hacia
        // fuera de una dirección (dx, dy) es (dy, -dx). Horario, la contraria.
        var sentido = area > 0 ? 1.0 : -1.0;

        var dx = new double[n];
        var dy = new double[n];
        var sirve = new bool[n];
        var utiles = 0;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var ax = puntos[2 * j] - puntos[2 * i];
            var ay = puntos[(2 * j) + 1] - puntos[(2 * i) + 1];

            var largo = Math.Sqrt((ax * ax) + (ay * ay));

            if (largo <= Tolerancia)
            {
                // Dos puntos repetidos. No se descarta el vértice: eso correría los índices y los
                // bulges dejarían de apuntar a su arco. Se marca y se usa la dirección de la arista
                // vecina, que es la que de verdad manda ahí.
                continue;
            }

            dx[i] = ax / largo;
            dy[i] = ay / largo;
            sirve[i] = true;
            utiles++;
        }

        if (utiles < 2)
        {
            return null;
        }

        var salida = new double[puntos.Length];

        for (var i = 0; i < n; i++)
        {
            var entra = AristaHaciaAtras(sirve, n, i);
            var sale = AristaHaciaAdelante(sirve, n, i);

            // Las dos aristas, corridas hacia fuera, pasan por estos dos puntos.
            var a1x = puntos[2 * i] + (t * sentido * dy[entra]);
            var a1y = puntos[(2 * i) + 1] - (t * sentido * dx[entra]);

            var a2x = puntos[2 * i] + (t * sentido * dy[sale]);
            var a2y = puntos[(2 * i) + 1] - (t * sentido * dx[sale]);

            var cruz = (dx[entra] * dy[sale]) - (dy[entra] * dx[sale]);

            if (Math.Abs(cruz) <= 1e-7)
            {
                // Las dos aristas son paralelas: el vértice solo se traslada. Pasa de verdad, en los
                // vértices que TrazoAcero pone a mitad de un lado recto.
                salida[2 * i] = a1x;
                salida[(2 * i) + 1] = a1y;
                continue;
            }

            var u = (((a2x - a1x) * dy[sale]) - ((a2y - a1y) * dx[sale])) / cruz;

            salida[2 * i] = a1x + (u * dx[entra]);
            salida[(2 * i) + 1] = a1y + (u * dy[entra]);
        }

        return salida;
    }

    /// <summary>
    /// Un punto del contorno por su <b>lado izquierdo</b>, para que una flecha pueda apuntarle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No basta con la X más chica.</b> Eso da una coordenada del contorno y ninguna Y, y tomar
    /// el centro vertical de la pieza deja la flecha en el aire: en un perfil I la X más chica es la
    /// punta del patín, y a media altura por ahí no pasa el contorno —está el hueco entre los dos
    /// patines—. Es exactamente lo que hacía que la flecha de la soldadura señalara a nada.
    /// </para>
    /// <para>
    /// Así que se busca la <b>arista</b> de más a la izquierda —la más larga de las que están al
    /// mínimo de X— y se devuelve su punto medio. En un perfil I eso cae en el canto del patín, que
    /// es contorno de verdad; en un ángulo o una canal, en el canto del alma. Si el mínimo de X se da
    /// en un solo vértice, se devuelve ese vértice.
    /// </para>
    /// </remarks>
    public static (double X, double Y) PuntoIzquierdo(double[]? puntos)
    {
        if (puntos is null || puntos.Length < 4 || puntos.Length % 2 != 0)
        {
            return (0, 0);
        }

        var n = puntos.Length / 2;

        var minX = double.MaxValue;
        var maxX = double.MinValue;

        for (var i = 0; i < n; i++)
        {
            if (puntos[2 * i] < minX) { minX = puntos[2 * i]; }
            if (puntos[2 * i] > maxX) { maxX = puntos[2 * i]; }
        }

        // La holgura es RELATIVA al tamaño de la pieza: en unidades de dibujo un perfil puede medir
        // 0.2, y una holgura fija de un milímetro se comería medio contorno.
        var holgura = 1e-9 + (1e-7 * Math.Max(1e-9, maxX - minX));

        var mejorLargo = -1.0;
        var mejorX = minX;
        var mejorY = puntos[1];

        // Primero, la arista COMPLETA que esté al mínimo de X: sus dos extremos ahí.
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            if (puntos[2 * i] > minX + holgura || puntos[2 * j] > minX + holgura)
            {
                continue;
            }

            var largo = Math.Abs(puntos[(2 * j) + 1] - puntos[(2 * i) + 1]);

            if (largo > mejorLargo)
            {
                mejorLargo = largo;
                mejorX = (puntos[2 * i] + puntos[2 * j]) / 2;
                mejorY = (puntos[(2 * i) + 1] + puntos[(2 * j) + 1]) / 2;
            }
        }

        if (mejorLargo > holgura)
        {
            return (mejorX, mejorY);
        }

        // No hay arista vertical en el mínimo —una punta—: se devuelve el vértice.
        for (var i = 0; i < n; i++)
        {
            if (puntos[2 * i] <= minX + holgura)
            {
                return (puntos[2 * i], puntos[(2 * i) + 1]);
            }
        }

        return (minX, mejorY);
    }

    /// <summary>
    /// Hasta dónde llega el contorno <b>a la altura</b> <paramref name="y"/>, en horizontal.
    /// </summary>
    /// <param name="lado">Positivo, la X mayor —el paño derecho—. Negativo, la X menor.</param>
    /// <returns>La X del paño, o <c>null</c> si a esa altura el contorno no pasa.</returns>
    /// <remarks>
    /// <para>
    /// Es un <b>rayo horizontal</b> contra el contorno, y existe para pegar los cartabones al acero
    /// de verdad. Sin él se usaba el rectángulo envolvente del perfil, y en un perfil I eso pone el
    /// cartabón del eje Y a la altura del centro pero arrancando en la <b>punta del patín</b>: a
    /// media altura el patín no está —está el hueco entre los dos— así que el cartabón salía
    /// flotando en el aire, sin nada que lo uniera a la columna.
    /// </para>
    /// <para>
    /// Con el rayo, a media altura de una I lo que se encuentra es el <b>alma</b>, que es donde el
    /// cartabón se suelda. Y no hay que preguntarle nada a la forma: sale de la geometría, así que
    /// vale igual para la te, la canal, el ángulo o el tubo.
    /// </para>
    /// <para>
    /// <b>Los arcos se tratan como cuerdas.</b> En las formas con dobleces —la canal y la zeta en
    /// frío, las esquinas del tubo— el paño calculado queda un pelo por dentro del real, o sea que
    /// el cartabón monta unas décimas de milímetro sobre el acero en lugar de quedar separado. De
    /// los dos errores posibles es el inofensivo: van soldados.
    /// </para>
    /// </remarks>
    public static double? CruceHorizontal(double[]? puntos, double y, int lado)
    {
        if (puntos is null || puntos.Length < 6 || puntos.Length % 2 != 0)
        {
            return null;
        }

        var n = puntos.Length / 2;
        double? mejor = null;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var y1 = puntos[(2 * i) + 1];
            var y2 = puntos[(2 * j) + 1];
            var x1 = puntos[2 * i];
            var x2 = puntos[2 * j];

            if (y < Math.Min(y1, y2) - Tolerancia || y > Math.Max(y1, y2) + Tolerancia)
            {
                continue;
            }

            if (Math.Abs(y2 - y1) <= Tolerancia)
            {
                // Arista horizontal justo a esa altura: sus dos extremos cuentan.
                mejor = Extremo(mejor, x1, lado);
                mejor = Extremo(mejor, x2, lado);
                continue;
            }

            var t = (y - y1) / (y2 - y1);

            mejor = Extremo(mejor, x1 + (t * (x2 - x1)), lado);
        }

        return mejor;
    }

    /// <summary>
    /// Hasta dónde llega el contorno <b>en la abscisa</b> <paramref name="x"/>, en vertical.
    /// </summary>
    /// <param name="lado">Positivo, la Y mayor —el paño de arriba—. Negativo, la Y menor.</param>
    /// <remarks>La hermana de <see cref="CruceHorizontal"/>, con los ejes cambiados.</remarks>
    public static double? CruceVertical(double[]? puntos, double x, int lado)
    {
        if (puntos is null || puntos.Length < 6 || puntos.Length % 2 != 0)
        {
            return null;
        }

        var n = puntos.Length / 2;
        double? mejor = null;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var x1 = puntos[2 * i];
            var x2 = puntos[2 * j];
            var y1 = puntos[(2 * i) + 1];
            var y2 = puntos[(2 * j) + 1];

            if (x < Math.Min(x1, x2) - Tolerancia || x > Math.Max(x1, x2) + Tolerancia)
            {
                continue;
            }

            if (Math.Abs(x2 - x1) <= Tolerancia)
            {
                mejor = Extremo(mejor, y1, lado);
                mejor = Extremo(mejor, y2, lado);
                continue;
            }

            var t = (x - x1) / (x2 - x1);

            mejor = Extremo(mejor, y1 + (t * (y2 - y1)), lado);
        }

        return mejor;
    }

    /// <summary>El mayor o el menor de los dos, según el lado que se pida.</summary>
    private static double Extremo(double? actual, double candidato, int lado)
    {
        if (actual is null)
        {
            return candidato;
        }

        return lado >= 0 ? Math.Max(actual.Value, candidato) : Math.Min(actual.Value, candidato);
    }

    /// <summary>
    /// La distancia de un punto al <b>contorno</b>, en las mismas unidades que los puntos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la distancia al <b>perímetro</b>, no al interior: un punto dentro del contorno devuelve su
    /// distancia a la arista más cercana, con signo positivo igual que uno de fuera. Sirve así porque
    /// lo que se mide con ella es la <b>holgura de la llave</b> —la columna L del estándar— y esa
    /// holgura es contra el paño de la columna, esté el ancla del lado que esté.
    /// </para>
    /// <para>
    /// Se mide al <b>segmento</b> y no al vértice más cercano. Con los vértices, un ancla frente a la
    /// mitad de un patín largo daría una distancia enorme —la de la punta del patín— y pasaría una
    /// holgura que no existe.
    /// </para>
    /// </remarks>
    public static double DistanciaAlContorno(double[]? puntos, double x, double y)
    {
        if (puntos is null || puntos.Length < 4 || puntos.Length % 2 != 0)
        {
            return double.MaxValue;
        }

        var n = puntos.Length / 2;
        var menor = double.MaxValue;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var d = DistanciaAlSegmento(
                x, y,
                puntos[2 * i], puntos[(2 * i) + 1],
                puntos[2 * j], puntos[(2 * j) + 1]);

            if (d < menor)
            {
                menor = d;
            }
        }

        return menor;
    }

    /// <summary>La distancia de un punto a un segmento, no a la recta que lo contiene.</summary>
    private static double DistanciaAlSegmento(
        double x, double y, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;

        var largo2 = (dx * dx) + (dy * dy);

        if (largo2 <= Tolerancia)
        {
            // Segmento de largo cero: es un punto.
            return Math.Sqrt(((x - x1) * (x - x1)) + ((y - y1) * (y - y1)));
        }

        // Dónde cae la proyección, acotada a los extremos: eso es lo que la hace distancia al
        // SEGMENTO y no a la recta.
        var u = (((x - x1) * dx) + ((y - y1) * dy)) / largo2;

        if (u < 0) { u = 0; }
        if (u > 1) { u = 1; }

        var px = x1 + (u * dx);
        var py = y1 + (u * dy);

        return Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
    }

    /// <summary>La arista que <b>llega</b> al vértice, saltando las de largo cero.</summary>
    private static int AristaHaciaAtras(bool[] sirve, int n, int vertice)
    {
        for (var k = 1; k <= n; k++)
        {
            var i = ((vertice - k) % n + n) % n;

            if (sirve[i])
            {
                return i;
            }
        }

        return vertice;
    }

    /// <summary>La arista que <b>sale</b> del vértice, saltando las de largo cero.</summary>
    private static int AristaHaciaAdelante(bool[] sirve, int n, int vertice)
    {
        for (var k = 0; k < n; k++)
        {
            var i = (vertice + k) % n;

            if (sirve[i])
            {
                return i;
            }
        }

        return vertice;
    }
}
