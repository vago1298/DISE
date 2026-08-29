using System;
using System.Collections.Generic;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// De varias cadenas en la misma línea, en planta se dibuja <b>una</b>: la más alta.
/// </summary>
/// <remarks>
/// <para>
/// Un muro de mampostería lleva normalmente <b>tres</b> cadenas a lo largo del mismo paño: la de
/// <b>desplante</b> abajo, la <b>intermedia</b> a media altura y la de <b>cerramiento</b>
/// arriba. Las tres pertenecen al mismo nivel del modelo y las tres ocupan <b>la misma línea en
/// planta</b>, así que se dibujaban las tres, una encima de la otra: tres parejas de líneas
/// pegadas y <b>tres rótulos pisándose</b> en el mismo sitio. En el plano eso no es información,
/// es una mancha.
/// </para>
/// <para>
/// Y en una planta no hay forma de distinguirlas —una planta no tiene alturas—, así que dibujar
/// las tres no aporta nada: se dibuja la de <b>arriba</b>, que es la que se ve al mirar el piso
/// desde arriba, y las de abajo se callan. Es lo que se pidió: «en planta solo muestra la cadena
/// más alta que exista, solo dibuja una».
/// </para>
/// <para>
/// <b>Solo se tapan las que se encima de verdad.</b> Dos cadenas seguidas en la misma línea —una
/// de castillo a castillo y la siguiente de ahí al final del muro— no se tapan: son dos tramos
/// distintos del mismo paño y los dos se dibujan. Solo se calla la que otra <b>más alta</b> le
/// pasa por encima.
/// </para>
/// </remarks>
public static class CadenaMasAlta
{
    /// <summary>Menos que esto no es un largo: es un nudo mal leído.</summary>
    private const double Nada = 1e-9;

    /// <summary>
    /// ¿Es una <b>cadena</b> —dala, cerramiento, desplante o intermedia—?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se mira el <b>tipo</b>, que es lo que sale de las notas de la propiedad, y entran los
    /// cuatro nombres con que puede llegar: <c>DALA</c> y las tres <c>CADENA…</c>. Una TRABE no
    /// entra, y es a propósito: dos trabes a distinta altura sobre la misma línea son dos vigas
    /// de verdad —una de entrepiso y una de azotea— y callar una sería esconder estructura.
    /// </para>
    /// <para>
    /// La cadena es otra cosa: las tres del muro son el <b>mismo</b> elemento repetido a tres
    /// alturas, y en planta las tres son la misma raya.
    /// </para>
    /// </remarks>
    public static bool EsCadena(ElementoPlanta? el)
    {
        if (el is null || el.Clase != ClasePlanta.Trabe)
        {
            return false;
        }

        var t = (el.Tipo ?? string.Empty).Trim();

        return t.StartsWith("CADENA", StringComparison.OrdinalIgnoreCase)
               || t.Equals("DALA", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La cota de arriba de la cadena, que es por donde se comparan.</summary>
    public static double Arriba(ElementoPlanta el) => Math.Max(el.Z1, el.Z2);

    /// <summary>
    /// Las cadenas que <b>no</b> se dibujan porque otra más alta les pasa por encima.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se devuelven las tapadas y no las que quedan, para que quien dibuja siga recorriendo la
    /// planta en su orden y solo pregunte «¿esta se calla?». Así el resumen sigue contando lo
    /// que hay en el modelo y no lo que se dibujó.
    /// </para>
    /// <para>
    /// El desempate, cuando dos cadenas están a la <b>misma altura</b> y se encima: se queda la
    /// primera de la lista. Da igual cuál sea —son la misma raya en planta—, pero tiene que ser
    /// <b>siempre la misma</b>: si dependiera del orden de recorrido, dibujar dos veces la misma
    /// planta podría callar una distinta y el plano cambiaría sin motivo.
    /// </para>
    /// </remarks>
    /// <param name="elementos">Todos los elementos de la planta.</param>
    /// <param name="tolM">
    /// Holgura para tomar dos cadenas como «la misma línea»: es <c>TOLERANCIA_CADENA_CM</c>,
    /// la misma con la que se decide si una cadena va sobre un muro.
    /// </param>
    public static HashSet<ElementoPlanta> Tapadas(
        IReadOnlyList<ElementoPlanta>? elementos, double tolM)
    {
        var tapadas = new HashSet<ElementoPlanta>();

        if (elementos is null || elementos.Count < 2)
        {
            return tapadas;
        }

        for (var i = 0; i < elementos.Count; i++)
        {
            var a = elementos[i];

            if (!EsCadena(a))
            {
                continue;
            }

            var (ux, uy, largoA) = Direccion(a);

            if (largoA <= Nada)
            {
                continue;
            }

            var ox = (a.X1 + a.X2) / 2;
            var oy = (a.Y1 + a.Y2) / 2;

            var (a1, a2) = Tramo(a, ux, uy, ox, oy);

            // ==========================================================================
            //  LO QUE DE VERDAD LA TAPA: LA UNIÓN DE LAS DE ARRIBA
            // ==========================================================================
            //  Aquí estaba el fallo, y se veía en el plano como una cadena que falta. Antes
            //  bastaba que la de arriba la solapara MÁS DE LA HOLGURA —diez centímetros— para
            //  callarla ENTERA. Así que una cadena corta de castillo a castillo, con una de
            //  cerramiento más alta que solo le entraba quince centímetros por la punta,
            //  desaparecía del plano y en su sitio no quedaba nada: la de arriba solo cubría
            //  esos quince centímetros. El hueco medía lo que la cadena menos el solape.
            //
            //  Ahora se junta lo que cubren TODAS las de arriba y se calla solo si entre ellas
            //  la cubren ENTERA. Con eso:
            //
            //    · las tres cadenas del mismo paño —desplante, intermedia y cerramiento— se
            //      siguen callando, que es para lo que se hizo esto: las tres miden lo mismo;
            //    · una cadena que otra solo pisa en parte SE DIBUJA, porque hay tramo suyo que
            //      nadie más está dibujando;
            //    · y si DOS de arriba se reparten cubrirla, también se calla: entre las dos no
            //      dejan ni un pedazo sin dibujar. Por eso es la unión y no cada una por su
            //      cuenta.
            var cubren = new List<(double Desde, double Hasta)>();

            for (var j = 0; j < elementos.Count; j++)
            {
                var b = elementos[j];

                if (i == j || !EsCadena(b))
                {
                    continue;
                }

                // ¿Manda la otra? Está más arriba, o está a la misma altura y llega antes en
                // la lista, que es el desempate estable.
                var mandaLaOtra = Arriba(b) > Arriba(a) + Nada
                                  || (Math.Abs(Arriba(b) - Arriba(a)) <= Nada && j < i);

                if (!mandaLaOtra || !MismaLinea(a, b, ux, uy, ox, oy, tolM))
                {
                    continue;
                }

                var (b1, b2) = Tramo(b, ux, uy, ox, oy);

                var desde = Math.Max(a1, b1);
                var hasta = Math.Min(a2, b2);

                // Que se toquen por la punta no cuenta: dos tramos seguidos del mismo paño se
                // dibujan los dos.
                if (hasta - desde > Nada)
                {
                    cubren.Add((desde, hasta));
                }
            }

            // Sin nadie encima no hay nada que decidir. Y hace falta preguntarlo: en una cadena
            // más corta que la holgura, «cubierto >= largo - holgura» sale cierto con cero
            // cubierto, y se callaría una cadena que nadie tapa.
            if (cubren.Count == 0)
            {
                continue;
            }

            if (LargoCubierto(cubren) >= largoA - tolM)
            {
                tapadas.Add(a);
            }
        }

        return tapadas;
    }

    /// <summary>Cuánto miden los tramos <b>juntos</b>, sin contar dos veces lo que se solapa.</summary>
    /// <remarks>
    /// Se ordenan por su principio y se van fundiendo con el anterior mientras lo toquen. Sin
    /// fundirlos, dos de arriba que se solapan entre ellas sumarían más de lo que cubren y
    /// callarían una cadena que sí tiene tramo libre.
    /// </remarks>
    private static double LargoCubierto(List<(double Desde, double Hasta)> tramos)
    {
        tramos.Sort((p, q) => p.Desde.CompareTo(q.Desde));

        double total = 0;

        var desde = tramos[0].Desde;
        var hasta = tramos[0].Hasta;

        for (var k = 1; k < tramos.Count; k++)
        {
            if (tramos[k].Desde <= hasta)
            {
                hasta = Math.Max(hasta, tramos[k].Hasta);
                continue;
            }

            total += hasta - desde;

            desde = tramos[k].Desde;
            hasta = tramos[k].Hasta;
        }

        return total + (hasta - desde);
    }

    /// <summary>¿Van en la <b>misma dirección</b> y por la <b>misma línea</b>?</summary>
    private static bool MismaLinea(
        ElementoPlanta a, ElementoPlanta b,
        double ux, double uy, double ox, double oy, double tolM)
    {
        var (vx, vy, largoB) = Direccion(b);

        if (largoB <= Nada)
        {
            return false;
        }

        // MISMA DIRECCIÓN: el seno del ángulo que forman. Se admiten unos grados, porque un
        // muro dibujado a mano nunca queda exacto.
        if (Math.Abs((ux * vy) - (uy * vx)) > 0.10)
        {
            return false;
        }

        var px = (b.X1 + b.X2) / 2;
        var py = (b.Y1 + b.Y2) / 2;

        // MISMA LÍNEA: lo que separa a los dos centros, medido de través.
        return Math.Abs(((py - oy) * ux) - ((px - ox) * uy)) <= tolM;
    }

    /// <summary>
    /// ¿Estas dos cadenas son <b>la misma raya</b> en planta?
    /// </summary>
    /// <remarks>
    /// Tres condiciones, y las tres hacen falta: que vayan en la <b>misma dirección</b>, que
    /// estén en la <b>misma línea</b> —lo que separa a sus ejes, medido de través, no llega a la
    /// holgura— y que se <b>encimen de verdad</b> a lo largo de ella. Lo último es lo que
    /// distingue una cadena tapada de dos tramos seguidos del mismo paño: dos que solo se tocan
    /// por la punta no se tapan, y las dos se dibujan.
    /// </remarks>
    public static bool SeEnciman(ElementoPlanta a, ElementoPlanta b, double tolM)
    {
        var (ux, uy, largoA) = Direccion(a);

        if (largoA <= Nada || !MismaLinea(a, b, ux, uy,
                (a.X1 + a.X2) / 2, (a.Y1 + a.Y2) / 2, tolM))
        {
            return false;
        }

        var ox = (a.X1 + a.X2) / 2;
        var oy = (a.Y1 + a.Y2) / 2;

        var (a1, a2) = Tramo(a, ux, uy, ox, oy);
        var (b1, b2) = Tramo(b, ux, uy, ox, oy);

        // LA CUBRE ENTERA, no «se solapan un poco». Esto se corrigió: con «un poco» bastaba
        // para callar la cadena completa, y donde la de arriba no llegaba no quedaba nada
        // dibujado. El hueco medía lo que la cadena menos el solape.
        return Math.Min(a2, b2) - Math.Max(a1, b1) >= largoA - tolM;
    }

    /// <summary>La dirección unitaria de una barra en planta, y su largo.</summary>
    private static (double X, double Y, double Largo) Direccion(ElementoPlanta el)
    {
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        return largo > Nada ? (dx / largo, dy / largo, largo) : (0, 0, 0);
    }

    /// <summary>El tramo que ocupa una barra sobre una línea, de menor a mayor.</summary>
    private static (double A, double B) Tramo(
        ElementoPlanta el, double ux, double uy, double ox, double oy)
    {
        var t1 = ((el.X1 - ox) * ux) + ((el.Y1 - oy) * uy);
        var t2 = ((el.X2 - ox) * ux) + ((el.Y2 - oy) * uy);

        return t1 <= t2 ? (t1, t2) : (t2, t1);
    }
}
