namespace CadLink.Etabs;

/// <summary>
/// El <b>contorno 2D</b> de una sección de barra, en coordenadas de su propia sección.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué vive aquí y no en el visor.</b> Este contorno hace falta en dos sitios muy
/// distintos: la vista extruida de la ventana, que lo pinta en WPF, y el dibujo 3D en
/// AutoCAD, que lo extruye a lo largo de cada barra por COM. Si cada uno tuviera su copia
/// de la geometría, el visor y el plano acabarían mostrando perfiles distintos, que es el
/// tipo de desajuste más difícil de notar y más caro de arreglar. Así que la geometría se
/// calcula <b>una vez</b>, sin nada de WPF ni de COM, y los dos consumen lo mismo.
/// </para>
/// <para>
/// <b>El sistema de coordenadas.</b> El contorno se devuelve centrado en el origen, con
/// <c>x</c> a lo ancho (eje 3 de la sección) y <c>y</c> a lo alto (eje 2). Quien lo use lo
/// coloca y lo orienta; aquí no se sabe nada de la posición de la barra.
/// </para>
/// <para>
/// <b>Los perfiles van cerrados y en un solo lazo</b>, incluso los que tienen hueco. Un
/// tubo se aproxima por su contorno exterior: el hueco no se ve desde fuera en una vista
/// extruida ni en un sólido, y meter lazos interiores obligaría a que los dos
/// consumidores supieran resolver islas. Si algún día hace falta el hueco de verdad, el
/// sitio de añadirlo es este, no los dos consumidores.
/// </para>
/// </remarks>
public static class Perfil2D
{
    /// <summary>Puntos de un contorno, en metros y relativos al centro de la sección.</summary>
    /// <remarks>Van en orden, y el último cierra con el primero sin repetirlo.</remarks>
    public sealed record Contorno(double[] X, double[] Y)
    {
        /// <summary>Cuántos vértices tiene.</summary>
        public int N => X.Length;
    }

    /// <summary>
    /// Hasta dónde puede llegar un espesor respecto de la medida total.
    /// </summary>
    /// <remarks>
    /// <b>Los topes no son cosmética.</b> Con un recorte a la medida entera —o a la
    /// mitad, en el patín— los vértices del alma y los del patín acaban <b>coincidiendo</b>
    /// y el contorno se cruza consigo mismo. Y un contorno que se cruza da un sólido
    /// inválido en AutoCAD, no un perfil feo. Pasa con una sección mal capturada, que es
    /// justo el caso en el que no se quiere que el dibujo reviente. Se deja un 45&#160;%,
    /// que sigue permitiendo perfiles muy gruesos y garantiza que el alma nunca alcance al
    /// patín.
    /// </remarks>
    private const double FraccionMaxima = 0.45;

    /// <summary>Recorta un espesor, y lo repone si viene en cero o negativo.</summary>
    private static double Tope(double valor, double maximo) =>
        valor <= 0 ? maximo * 0.2 : Math.Min(valor, maximo);

    /// <summary>Lados con que se aproxima un círculo o un tubo.</summary>
    /// <remarks>
    /// Veinticuatro es el equilibrio de siempre: a esa cuenta el error de la cuerda contra
    /// el arco es del 0.86&#160;% del radio, invisible en un perfil de acero, y mantiene la
    /// malla ligera cuando el modelo trae cientos de barras.
    /// </remarks>
    public const int LadosDelCirculo = 24;

    /// <summary>
    /// El contorno que le toca a una forma.
    /// </summary>
    /// <param name="forma">RECT, CIRC, I, C, T, L, TUBO o CAJON.</param>
    /// <param name="ancho">Ancho, eje 3, en metros.</param>
    /// <param name="alto">Peralte, eje 2, en metros.</param>
    /// <param name="patin">Espesor del patín. Si va en cero se estima.</param>
    /// <param name="alma">Espesor del alma. Si va en cero se estima.</param>
    /// <param name="pared">Espesor de pared del tubo o cajón. Si va en cero se estima.</param>
    /// <remarks>
    /// Si la forma no se reconoce se devuelve el rectángulo. Es lo que hacía el programa
    /// con TODO antes de esto, así que en el peor caso se queda como estaba y nunca se
    /// queda sin dibujar.
    /// </remarks>
    public static Contorno De(
        string? forma, double ancho, double alto,
        double patin = 0, double alma = 0, double pared = 0)
    {
        var b = ancho > 1e-6 ? ancho : 0.12;
        var h = alto > 1e-6 ? alto : 0.12;

        // Los espesores que no vengan se estiman con proporciones de perfil laminado
        // corriente. Es mejor que dibujar una caja: la silueta se reconoce igual.
        var tf = patin > 1e-6 ? patin : h * 0.08;
        var tw = alma > 1e-6 ? alma : b * 0.06;
        var tp = pared > 1e-6 ? pared : Math.Min(b, h) * 0.06;

        return (forma ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "CIRC" => Circulo(b / 2),
            "TUBO" => Circulo(b / 2),
            "I" => PerfilI(b, h, tf, tw),
            "T" => PerfilT(b, h, tf, tw),
            "C" => Canal(b, h, tf, tw),
            "L" => Angulo(b, h, tf, tw),
            "CAJON" => Rectangulo(b, h),
            _ => Rectangulo(b, h)
        };
    }

    /// <summary>Rectángulo centrado.</summary>
    public static Contorno Rectangulo(double b, double h)
    {
        var x = b / 2;
        var y = h / 2;

        return new Contorno(
            new[] { -x, x, x, -x },
            new[] { -y, -y, y, y });
    }

    /// <summary>Círculo aproximado por un polígono.</summary>
    public static Contorno Circulo(double r)
    {
        var x = new double[LadosDelCirculo];
        var y = new double[LadosDelCirculo];

        for (var i = 0; i < LadosDelCirculo; i++)
        {
            var a = 2 * Math.PI * i / LadosDelCirculo;
            x[i] = r * Math.Cos(a);
            y[i] = r * Math.Sin(a);
        }

        return new Contorno(x, y);
    }

    /// <summary>
    /// Perfil <b>I</b>: doce vértices, recorridos en un solo lazo.
    /// </summary>
    /// <remarks>
    /// Se arranca en la esquina inferior izquierda del patín de abajo y se va en sentido
    /// antihorario. Los espesores se recortan si no caben —un patín no puede valer más que
    /// medio peralte— porque una sección mal capturada no debe reventar el dibujo.
    /// </remarks>
    public static Contorno PerfilI(double b, double h, double tf, double tw)
    {
        var x = b / 2;
        var y = h / 2;

        var f = Tope(tf, h * FraccionMaxima);
        var w = Tope(tw, b * FraccionMaxima) / 2;

        return new Contorno(
            new[] { -x, x, x, w, w, x, x, -x, -x, -w, -w, -x },
            new[] { -y, -y, -y + f, -y + f, y - f, y - f, y, y, y - f, y - f, -y + f, -y + f });
    }

    /// <summary>Perfil <b>T</b>: patín arriba y alma colgando.</summary>
    /// <remarks>
    /// El recorrido arranca en el pie del alma y sube por su derecha hasta el patín, lo
    /// rodea, y baja por la izquierda del alma. Hay que respetar ese orden: una primera
    /// versión mezclaba los vértices del patín con los del alma y el contorno se cruzaba
    /// consigo mismo. No se ve a simple vista en el código, pero un polígono que se cruza
    /// da un sólido inválido en AutoCAD y un relleno con agujeros en el visor. Lo cazó la
    /// comprobación de cruces entre lados de
    /// <c>tools/verificar_perfiles.py</c>.
    /// </remarks>
    public static Contorno PerfilT(double b, double h, double tf, double tw)
    {
        var x = b / 2;
        var y = h / 2;

        var f = Tope(tf, h * FraccionMaxima);
        var w = Tope(tw, b * FraccionMaxima) / 2;

        return new Contorno(
            new[] { -w, w, w, x, x, -x, -x, -w },
            new[] { -y, -y, y - f, y - f, y, y, y - f, y - f });
    }

    /// <summary>Canal <b>C</b>: alma a la izquierda y los dos patines hacia la derecha.</summary>
    public static Contorno Canal(double b, double h, double tf, double tw)
    {
        var x = b / 2;
        var y = h / 2;

        var f = Tope(tf, h * FraccionMaxima);
        var w = Tope(tw, b * FraccionMaxima);

        return new Contorno(
            new[] { -x, x, x, -x + w, -x + w, x, x, -x },
            new[] { -y, -y, -y + f, -y + f, y - f, y - f, y, y });
    }

    /// <summary>Ángulo <b>L</b>: dos alas en escuadra.</summary>
    /// <remarks>
    /// El vértice va abajo a la izquierda, que es como se dibuja un ángulo suelto. Aquí no
    /// se sabe cómo está girado en la estructura: eso lo pone quien lo coloca.
    /// </remarks>
    public static Contorno Angulo(double b, double h, double tf, double tw)
    {
        var x = b / 2;
        var y = h / 2;

        var ef = Tope(tf, h * FraccionMaxima);
        var ew = Tope(tw, b * FraccionMaxima);

        return new Contorno(
            new[] { -x, x, x, -x + ew, -x + ew, -x },
            new[] { -y, -y, -y + ef, -y + ef, y, y });
    }
}
