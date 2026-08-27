using System;
using System.Collections.Generic;
using System.Linq;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>tablero</b> de losa: los pedazos que el mallado partió, juntos otra vez.
/// </summary>
/// <remarks>
/// <para>
/// En el modelo una losa casi nunca llega de una pieza. El <b>mesh</b> la parte —en los nudos de
/// las trabes, en los ejes, o donde el programa decidió al mallar— y lo que en la obra es
/// <b>un solo tablero de concreto</b> llega al dibujo como tres o cuatro shells. Dibujando cada
/// shell por su cuenta salían tres armados pequeños dentro del mismo tablero y tres rótulos
/// «Losa de… cm de espesor… Var. # @… cm.» encimados, y eso no es un plano: es la malla del
/// programa de cálculo copiada al papel.
/// </para>
/// <para>
/// <b>Un tablero es un armado y un rótulo.</b> Así que los pedazos se juntan antes de dibujar y el
/// armado se traza sobre la caja del tablero <b>completo</b>, que es el claro de verdad: el que se
/// mide de apoyo a apoyo y el que decide la varilla.
/// </para>
/// <para>
/// <b>Y la frontera manda.</b> Se pidió expresamente: la unión tiene que quedar <b>dentro de los
/// límites de los muros, las trabes o las cadenas que lo limitan</b>. Dos pedazos se juntan solo si
/// la orilla que comparten está <b>libre</b>; si por esa orilla corre un apoyo, son <b>dos
/// tableros</b> y cada uno lleva su armado, porque el apoyo interrumpe el claro y ahí cambia el
/// acero. Esa es la diferencia entre juntar lo que el mesh partió —que es lo que se pide— y juntar
/// dos tableros distintos, que sería un error de cálculo dibujado.
/// </para>
/// <para>
/// <b>No se toca la geometría de nadie.</b> Los pedazos siguen en la planta con su contorno y su
/// hatch: lo único que se decide aquí es <b>quién manda</b> en el tablero —el pedazo más grande— y
/// <b>qué caja</b> ocupa entre todos. Es a propósito: un tablero en L no se puede representar con
/// un rectángulo sin dibujar concreto donde no hay, así que se juntan para el armado y el rótulo, y
/// las líneas siguen siendo las de cada paño.
/// </para>
/// </remarks>
public static class TableroDeLosa
{
    private const double Nada = 1e-9;

    /// <summary>Un tablero: sus pedazos, quién manda, su caja y dónde va el rótulo.</summary>
    /// <param name="Pedazos">Los shells que lo forman. Uno solo si la losa no venía partida.</param>
    /// <param name="Manda">
    /// El pedazo <b>más grande</b>. De él salen el espesor, la sección y el uso que se rotulan, y
    /// es el único que dibuja el armado y el texto del tablero.
    /// </param>
    /// <param name="CentroX">Donde va el rótulo: dentro del tablero, no en el aire.</param>
    public sealed record Tablero(
        List<ElementoPlanta> Pedazos,
        ElementoPlanta Manda,
        double X0,
        double Y0,
        double X1,
        double Y1,
        double CentroX,
        double CentroY)
    {
        /// <summary>¿El mesh lo había partido?</summary>
        public bool Partido => Pedazos.Count > 1;

        public double Ancho => X1 - X0;

        public double Alto => Y1 - Y0;

        /// <summary>¿Este pedazo es el que manda —el que dibuja armado y rótulo—?</summary>
        public bool Manejado(ElementoPlanta el) => ReferenceEquals(Manda, el);
    }

    /// <summary>
    /// Junta los pedazos de losa en <b>tableros</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Agrupación <b>transitiva</b>, y con fusión de grupos: si un pedazo resulta vecino de dos
    /// grupos que ya existían, los dos son el mismo tablero y se unen. Sin la fusión, una losa
    /// mallada en nueve cuadros se descubre en zigzag y quedaban dos o tres tableros donde hay uno.
    /// </para>
    /// <para>
    /// El orden de los pedazos no cambia el resultado, y llamarlo dos veces da lo mismo: no toca la
    /// lista de elementos.
    /// </para>
    /// </remarks>
    /// <param name="elementos">Todos los elementos de la planta. Se miran solo las losas.</param>
    /// <param name="huellas">Los apoyos en planta: muros, cadenas y trabes.</param>
    /// <param name="tolM">Holgura para tomar dos pedazos como pegados.</param>
    /// <param name="tolApoyoM">Holgura para tomar un apoyo como que corre sobre la frontera.</param>
    /// <param name="cubre">Qué parte de la frontera tiene que recorrer el apoyo para separarla.</param>
    /// <param name="familia">
    /// Con qué se puede juntar cada pedazo. Un <b>volado</b> no se junta con un entrepiso ni una
    /// <b>losacero</b> con una losa de concreto: son paños distintos, con dibujo distinto y con
    /// rótulo distinto, aunque se toquen.
    /// </param>
    public static List<Tablero> Agrupar(
        IReadOnlyList<ElementoPlanta>? elementos,
        IReadOnlyList<ElementoPlanta>? huellas,
        double tolM = 0.05,
        double tolApoyoM = 0.25,
        double cubre = 0.7,
        Func<ElementoPlanta, string>? familia = null)
    {
        var salida = new List<Tablero>();

        if (elementos is null)
        {
            return salida;
        }

        var apoyos = huellas ?? new List<ElementoPlanta>();

        var losas = elementos
            .Where(e => e.Clase == ClasePlanta.Losa && e.Vertices.Count >= 3)
            .ToList();

        var grupos = new List<List<ElementoPlanta>>();

        foreach (var el in losas)
        {
            var suyos = grupos
                .Where(g => g.Any(o => MismaFamilia(familia, o, el)
                                       && MismoTablero(o, el, apoyos, tolM, tolApoyoM, cubre)))
                .ToList();

            if (suyos.Count == 0)
            {
                grupos.Add(new List<ElementoPlanta> { el });
                continue;
            }

            suyos[0].Add(el);

            // Y los demás grupos vecinos son el MISMO tablero: se le meten dentro.
            for (var k = 1; k < suyos.Count; k++)
            {
                suyos[0].AddRange(suyos[k]);
                grupos.Remove(suyos[k]);
            }
        }

        foreach (var g in grupos)
        {
            salida.Add(Armar(g));
        }

        return salida;
    }

    /// <summary>
    /// ¿Estos dos pedazos son el <b>mismo tablero</b>?
    /// </summary>
    /// <remarks>
    /// Dos condiciones, y las dos hacen falta: que <b>compartan orilla</b> —si no se tocan, no hay
    /// nada que juntar— y que por esa orilla <b>no corra un apoyo</b>. Lo segundo es lo que se
    /// pidió: la unión tiene que quedar dentro de los límites de los muros, las trabes o las
    /// cadenas. Un tablero termina donde apoya.
    /// </remarks>
    public static bool MismoTablero(
        ElementoPlanta a,
        ElementoPlanta b,
        IReadOnlyList<ElementoPlanta> huellas,
        double tolM = 0.05,
        double tolApoyoM = 0.25,
        double cubre = 0.7)
    {
        if (ReferenceEquals(a, b))
        {
            return false;
        }

        return Frontera(a, b, tolM) is { } f && !HayApoyoEnLaFrontera(f, huellas, tolApoyoM, cubre);
    }

    /// <summary>
    /// La <b>orilla que dos pedazos comparten</b>, o nulo si no se tocan.
    /// </summary>
    /// <remarks>
    /// Se busca el par de lados <b>paralelos, sobre la misma línea y solapados</b>, y se devuelve el
    /// trozo común más largo. No se comparan vértices: el mesh de ETABS los reparte con las
    /// coordenadas que salen del cálculo y dos pedazos pegados casi nunca traen el vértice exacto.
    /// </remarks>
    public static LosaEnPlanta.Segmento? Frontera(
        ElementoPlanta a, ElementoPlanta b, double tolM = 0.05)
    {
        LosaEnPlanta.Segmento? mejor = null;
        double masLargo = 0;

        foreach (var la in LosaEnPlanta.Lados(a.Vertices))
        {
            foreach (var lb in LosaEnPlanta.Lados(b.Vertices))
            {
                if (Comun(la, lb, tolM) is { } t && t.Largo > masLargo)
                {
                    mejor = t;
                    masLargo = t.Largo;
                }
            }
        }

        return mejor;
    }

    /// <summary>
    /// ¿Corre un <b>apoyo</b> a lo largo de esta frontera?
    /// </summary>
    /// <remarks>
    /// Es la misma pregunta que hace el armado para llegar al paño —<see
    /// cref="LosaEnPlanta.MedioApoyoEnBorde"/>—, y por eso se reusa: un apoyo cuenta si va
    /// <b>paralelo</b> a la frontera, <b>sobre su línea</b> y la <b>recorre</b>. Una trabe que solo
    /// la cruza de través no separa nada: pasa por encima de la losa continua.
    /// </remarks>
    public static bool HayApoyoEnLaFrontera(
        LosaEnPlanta.Segmento frontera,
        IReadOnlyList<ElementoPlanta> huellas,
        double tolM = 0.25,
        double cubre = 0.7) =>
        LosaEnPlanta.MedioApoyoEnBorde(frontera, huellas, tolM, cubre) > 0;

    /// <summary>El <b>área</b> del paño, por la fórmula del zapatero.</summary>
    public static double Area(IReadOnlyList<(double X, double Y)>? v)
    {
        if (v is null || v.Count < 3)
        {
            return 0;
        }

        double doble = 0;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            doble += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(doble) / 2;
    }

    /// <summary>¿El punto cae <b>dentro</b> del paño? Contando cruces con un rayo.</summary>
    public static bool Dentro(IReadOnlyList<(double X, double Y)>? v, double x, double y)
    {
        if (v is null || v.Count < 3)
        {
            return false;
        }

        var dentro = false;

        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];

            if ((a.Y > y) == (b.Y > y))
            {
                continue;
            }

            var corte = a.X + ((y - a.Y) / (b.Y - a.Y) * (b.X - a.X));

            if (x < corte)
            {
                dentro = !dentro;
            }
        }

        return dentro;
    }

    /// <summary>Arma el tablero de un grupo: quién manda, su caja y dónde va el rótulo.</summary>
    private static Tablero Armar(List<ElementoPlanta> g)
    {
        // QUIÉN MANDA: el pedazo MÁS GRANDE. De él salen el espesor y el uso que se rotulan, y es
        // lo honesto cuando el mesh reparte propiedades distintas entre los pedazos de un mismo
        // tablero: manda la que cubre el tablero, no la primera que se leyó.
        var manda = g[0];
        var mayor = Area(manda.Vertices);

        foreach (var el in g.Skip(1))
        {
            var a = Area(el.Vertices);

            if (a > mayor)
            {
                mayor = a;
                manda = el;
            }
        }

        var x0 = g.Min(e => e.Vertices.Min(v => v.X));
        var x1 = g.Max(e => e.Vertices.Max(v => v.X));
        var y0 = g.Min(e => e.Vertices.Min(v => v.Y));
        var y1 = g.Max(e => e.Vertices.Max(v => v.Y));

        // EL RÓTULO, DENTRO DEL TABLERO. El centro de la caja es lo que se quiere —queda centrado
        // entre los pedazos—, pero en un tablero en L ese punto cae en el hueco, o sea encima de
        // otra cosa del plano. Cuando eso pasa, se rotula en el centro del pedazo que manda, que
        // siempre es concreto de verdad.
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;

        if (!g.Any(e => Dentro(e.Vertices, cx, cy)))
        {
            cx = manda.Vertices.Average(v => v.X);
            cy = manda.Vertices.Average(v => v.Y);
        }

        return new Tablero(g, manda, x0, y0, x1, y1, cx, cy);
    }

    /// <summary>¿Los dos pedazos son del mismo tipo de paño?</summary>
    private static bool MismaFamilia(
        Func<ElementoPlanta, string>? familia, ElementoPlanta a, ElementoPlanta b) =>
        familia is null
        || string.Equals(familia(a), familia(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>El trozo que dos lados tienen en común, o nulo si no van por la misma línea.</summary>
    private static LosaEnPlanta.Segmento? Comun(
        LosaEnPlanta.Segmento a, LosaEnPlanta.Segmento b, double tol)
    {
        var largo = a.Largo;

        if (largo < Nada || b.Largo < Nada)
        {
            return null;
        }

        var ux = (a.X2 - a.X1) / largo;
        var uy = (a.Y2 - a.Y1) / largo;

        var lb = b.Largo;
        var vx = (b.X2 - b.X1) / lb;
        var vy = (b.Y2 - b.Y1) / lb;

        // PARALELOS: si se cruzan, no comparten orilla.
        if (Math.Abs((ux * vy) - (uy * vx)) > 0.10)
        {
            return null;
        }

        // Y SOBRE LA MISMA LÍNEA: las dos puntas del otro lado, medidas de través.
        var f1 = (-uy * (b.X1 - a.X1)) + (ux * (b.Y1 - a.Y1));
        var f2 = (-uy * (b.X2 - a.X1)) + (ux * (b.Y2 - a.Y1));

        if (Math.Abs(f1) > tol || Math.Abs(f2) > tol)
        {
            return null;
        }

        // LO QUE SE SOLAPAN, proyectando las puntas del otro lado sobre este.
        var t1 = ((b.X1 - a.X1) * ux) + ((b.Y1 - a.Y1) * uy);
        var t2 = ((b.X2 - a.X1) * ux) + ((b.Y2 - a.Y1) * uy);

        if (t2 < t1)
        {
            (t1, t2) = (t2, t1);
        }

        var desde = Math.Max(0, t1);
        var hasta = Math.Min(largo, t2);

        // Tocarse en una ESQUINA no es compartir orilla: dos tableros en diagonal se tocan en un
        // punto y no son el mismo paño.
        if (hasta - desde <= tol)
        {
            return null;
        }

        return new LosaEnPlanta.Segmento(
            a.X1 + (ux * desde), a.Y1 + (uy * desde),
            a.X1 + (ux * hasta), a.Y1 + (uy * hasta));
    }
}
