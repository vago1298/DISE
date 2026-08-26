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
            if (!EsCadena(elementos[i]))
            {
                continue;
            }

            for (var j = 0; j < elementos.Count; j++)
            {
                if (i == j || !EsCadena(elementos[j]))
                {
                    continue;
                }

                // ¿Manda la otra? Está más arriba, o está a la misma altura y llega antes en
                // la lista, que es el desempate estable.
                var mandaLaOtra = Arriba(elementos[j]) > Arriba(elementos[i]) + Nada
                                  || (Math.Abs(Arriba(elementos[j]) - Arriba(elementos[i])) <= Nada
                                      && j < i);

                if (!mandaLaOtra || !SeEnciman(elementos[i], elementos[j], tolM))
                {
                    continue;
                }

                tapadas.Add(elementos[i]);
                break;
            }
        }

        return tapadas;
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
        var (vx, vy, largoB) = Direccion(b);

        if (largoA <= Nada || largoB <= Nada)
        {
            return false;
        }

        // MISMA DIRECCIÓN: el seno del ángulo que forman. Se admiten unos grados, porque un
        // muro dibujado a mano nunca queda exacto.
        if (Math.Abs((ux * vy) - (uy * vx)) > 0.10)
        {
            return false;
        }

        var ox = (a.X1 + a.X2) / 2;
        var oy = (a.Y1 + a.Y2) / 2;

        var px = (b.X1 + b.X2) / 2;
        var py = (b.Y1 + b.Y2) / 2;

        // MISMA LÍNEA: lo que separa a los dos centros, medido de través.
        if (Math.Abs(((py - oy) * ux) - ((px - ox) * uy)) > tolM)
        {
            return false;
        }

        // Y QUE SE ENCIMEN, no que se toquen: el trozo común tiene que medir algo.
        var (a1, a2) = Tramo(a, ux, uy, ox, oy);
        var (b1, b2) = Tramo(b, ux, uy, ox, oy);

        return Math.Min(a2, b2) - Math.Max(a1, b1) > tolM;
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
