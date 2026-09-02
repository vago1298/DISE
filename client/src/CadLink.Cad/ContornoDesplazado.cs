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
