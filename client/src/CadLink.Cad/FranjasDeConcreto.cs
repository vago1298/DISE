namespace CadLink.Cad;

/// <summary>
/// Las <b>bandas de concreto que se ven alrededor de la placa</b>, para poder rayarlas.
/// </summary>
/// <remarks>
/// <para>
/// En la planta de una placa base, el concreto de abajo —el dado, o la cadena o la trabe de una
/// placa a muro— se raya <b>solo donde sobresale</b>: debajo de la placa lo que se ve es la placa.
/// Lo normal es resolverlo con el contorno del concreto rayado y el de la placa como <b>isla</b>,
/// que es una sola entidad y es lo que se hacía.
/// </para>
/// <para>
/// <b>Pero la isla exige que la placa quepa ENTERA dentro del concreto.</b> Y no siempre cabe: una
/// cadena de 25 × 15 bajo una placa de 18 × 15 sobresale en X y en Y mide <i>lo mismo</i> que la
/// placa, así que los dos contornos se tocan. Un contorno interior que toca el exterior no delimita
/// un área: AutoCAD o falla o raya de más. Esto fue un caso real —«aplica el hatch a la cadena o
/// trabe»— y lo que pasaba es que no se rayaba nada, porque la condición pedía que el concreto fuera
/// mayor en las <b>dos</b> direcciones.
/// </para>
/// <para>
/// La salida es partir la parte visible en <b>bandas</b> —izquierda, derecha, abajo y arriba— y
/// rayar cada una por su cuenta, sin islas. Las bandas no se solapan entre sí: las laterales se
/// llevan toda la altura del concreto y las de arriba y abajo solo el tramo central, que es el que
/// las laterales no cogieron.
/// </para>
/// <para>
/// Es aritmética pura y vive aparte del dibujante, como <see cref="ElevacionPlacaBase"/> y
/// <see cref="AnclasPlacaBase"/>: es la parte que decide qué se ve y qué no, y así se puede
/// comprobar sin AutoCAD delante.
/// </para>
/// </remarks>
public static class FranjasDeConcreto
{
    /// <summary>
    /// Las bandas del rectángulo de concreto que <b>no</b> tapa la placa.
    /// </summary>
    /// <param name="cx1">Concreto: canto izquierdo.</param>
    /// <param name="cy1">Concreto: canto de abajo.</param>
    /// <param name="cx2">Concreto: canto derecho.</param>
    /// <param name="cy2">Concreto: canto de arriba.</param>
    /// <param name="px1">Placa: canto izquierdo.</param>
    /// <param name="py1">Placa: canto de abajo.</param>
    /// <param name="px2">Placa: canto derecho.</param>
    /// <param name="py2">Placa: canto de arriba.</param>
    /// <param name="tolerancia">
    /// Por debajo de esto una banda no se devuelve. Una banda de un milímetro no se ve en el plano
    /// y sí da un hatch degenerado, que es de los que AutoCAD rechaza con un error que no dice nada.
    /// </param>
    /// <returns>
    /// Cada banda como los <b>cuatro vértices</b> de su rectángulo, en el formato que espera una
    /// polilínea: <c>x1 y1 x2 y1 x2 y2 x1 y2</c>. Vacío si no se ve nada de concreto.
    /// </returns>
    public static List<double[]> Alrededor(
        double cx1, double cy1, double cx2, double cy2,
        double px1, double py1, double px2, double py2,
        double tolerancia = 1e-6)
    {
        var salida = new List<double[]>();

        // Sin concreto no hay nada que rayar.
        if (cx2 - cx1 <= tolerancia || cy2 - cy1 <= tolerancia)
        {
            return salida;
        }

        // ¿Se tocan la placa y el concreto? Si NO se solapan —una placa girada y desplazada, o un
        // dato a medio capturar— la parte visible es el concreto entero, y se devuelve de una pieza
        // en lugar de partirlo en bandas que se repetirían.
        var solapan = px2 > cx1 + tolerancia && px1 < cx2 - tolerancia
                      && py2 > cy1 + tolerancia && py1 < cy2 - tolerancia;

        if (!solapan)
        {
            salida.Add(Caja(cx1, cy1, cx2, cy2));
            return salida;
        }

        // LAS LATERALES SE LLEVAN TODA LA ALTURA, y las de arriba y abajo solo el tramo central:
        // así ninguna banda pisa a otra. Dos hatches encimados se ven el doble de densos y, peor,
        // al seleccionar uno queda el otro debajo.
        var xIzq = Math.Max(cx1, Math.Min(px1, cx2));
        var xDer = Math.Min(cx2, Math.Max(px2, cx1));

        if (xIzq - cx1 > tolerancia)
        {
            salida.Add(Caja(cx1, cy1, xIzq, cy2));
        }

        if (cx2 - xDer > tolerancia)
        {
            salida.Add(Caja(xDer, cy1, cx2, cy2));
        }

        var yAbajo = Math.Max(cy1, Math.Min(py1, cy2));
        var yArriba = Math.Min(cy2, Math.Max(py2, cy1));

        if (xDer - xIzq > tolerancia)
        {
            if (yAbajo - cy1 > tolerancia)
            {
                salida.Add(Caja(xIzq, cy1, xDer, yAbajo));
            }

            if (cy2 - yArriba > tolerancia)
            {
                salida.Add(Caja(xIzq, yArriba, xDer, cy2));
            }
        }

        return salida;
    }

    private static double[] Caja(double x1, double y1, double x2, double y2) =>
        new[] { x1, y1, x2, y1, x2, y2, x1, y2 };
}
