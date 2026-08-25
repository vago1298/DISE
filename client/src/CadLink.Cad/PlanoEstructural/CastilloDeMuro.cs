using System;
using System.Collections.Generic;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>castillo modelado como shell de muro</b>: se dibuja como un castillo, no como muro.
/// </summary>
/// <remarks>
/// <para>
/// En ETABS un castillo se puede modelar de dos maneras, y las dos son legítimas: como
/// <b>frame</b> —una barra con su sección de 15×15— o como un <b>shell de muro</b> muy
/// angosto, que es lo que sale cuando el castillo se dibuja junto con el muro del que forma
/// parte. El modelo lo distingue con las <b>notas de la propiedad</b>: ahí dice CASTILLO.
/// </para>
/// <para>
/// Dibujados como muro salían como <b>dos líneas</b> —el muro se dibuja por sus dos paños, sin
/// bloque y sin relleno—, así que en el plano un castillo de shell y uno de frame se veían
/// distintos <b>siendo la misma cosa</b>: uno amarillo y en un bloque que un
/// <c>BLOCKREPLACE</c> cambia por el detalle bueno, y el otro un par de rayas.
/// </para>
/// <para>
/// Aquí el shell se convierte en un elemento de clase <b>columna</b>, y a partir de ahí lo
/// dibuja el mismo camino que un castillo de frame: bloque con el nombre de la sección,
/// relleno <c>SOLID</c> amarillo dentro del bloque, capa <c>E-CASTILLO</c>, rótulo en su
/// esquina, y —esto también cuenta— pasa a ser un <b>apoyo</b>, así que los muros mueren en su
/// paño, el contorno de la losa lo respeta y el eje de orilla se corre a su paño.
/// </para>
/// <para>
/// <b>Solo si dice CASTILLO.</b> Es la única condición, y es a propósito: no se mira el largo
/// ni el espesor. Un shell angosto puede ser un pedazo de muro entre dos vanos, y convertirlo
/// «porque mide 15 cm» sería inventar un castillo que el ingeniero no puso. Si en las notas
/// dice CASTILLO, es un castillo; si no dice nada, es un muro.
/// </para>
/// </remarks>
public static class CastilloDeMuro
{
    /// <summary>La palabra que lo declara, y el tipo que sale de ella.</summary>
    public const string Palabra = "CASTILLO";

    /// <summary>Un largo por debajo de esto no es un elemento: es un shell degenerado.</summary>
    private const double Nada = 1e-9;

    /// <summary>
    /// ¿Este muro es en realidad un <b>castillo</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pregunta por el <see cref="ElementoPlanta.Tipo"/> —que la ventana ya clasificó con
    /// <c>ClasificaTipo</c>, y en un muro ese tipo <b>solo</b> puede salir de las notas— y, de
    /// respaldo, por las <b>notas</b> tal cual, para cuando el elemento llega de otro sitio y
    /// el tipo viene en blanco.
    /// </para>
    /// <para>
    /// El <b>nombre de la sección no se mira</b>: se pidió que fuera la property note, y una
    /// propiedad de muro llamada «MURO CASTILLO 15» es un muro con castillos, no un castillo.
    /// </para>
    /// </remarks>
    public static bool Dice(ElementoPlanta? el)
    {
        if (el is null || el.Clase != ClasePlanta.Muro)
        {
            return false;
        }

        return DicenLasNotas(el.Tipo, el.Notas);
    }

    /// <summary>
    /// ¿El tipo o las notas dicen <b>CASTILLO</b>?
    /// </summary>
    /// <remarks>
    /// Está aparte para que lo pueda preguntar también la <b>ventana</b>: la casilla que decide
    /// si un elemento se dibuja va por clase, y un shell que es un castillo tiene que seguir a
    /// la casilla de las <b>columnas</b>, no a la de los muros. Si no, quien apaga los muros
    /// para ver solo la estructura de castillos los perdería todos.
    /// </remarks>
    public static bool DicenLasNotas(string? tipo, string? notas)
    {
        if (string.Equals(tipo?.Trim(), Palabra, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (notas ?? string.Empty)
            .ToUpperInvariant()
            .Contains(Palabra, StringComparison.Ordinal);
    }

    /// <summary>
    /// El mismo elemento, pero ya como <b>castillo</b>: una sección en su centro, girada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El shell en planta es un <b>segmento con espesor</b>, y un castillo es una
    /// <b>sección en un punto</b>. La traducción es la de <c>PanoDeApoyo.Huella</c>, que ya se
    /// usa para medir paños, y aquí sirve para dibujar: el centro de la sección es el
    /// <b>punto medio</b> del segmento, su ancho es el <b>largo</b> del shell, su peralte es
    /// el <b>espesor</b> del muro y su giro es la <b>dirección</b> del segmento. Así un
    /// castillo de 15×40 modelado de lado sale de lado, como en el modelo.
    /// </para>
    /// <para>
    /// La forma se fija en <c>RECT</c>: la del shell es <c>AREA</c>, que no describe ninguna
    /// sección, y un castillo es un rectángulo. Lo demás —etiqueta, sección, notas, cotas— se
    /// copia tal cual, porque es lo que se rotula y lo que el corte necesita para saber de
    /// dónde a dónde va.
    /// </para>
    /// <para>
    /// Se devuelve un elemento <b>nuevo</b>: el que llega no se toca, para que la planta que
    /// el visor o la tabla tengan en la mano siga diciendo lo que el modelo dice.
    /// </para>
    /// </remarks>
    /// <param name="muro">El shell, tal como llegó del modelo.</param>
    /// <param name="espesorPorOmision">
    /// Espesor en metros para cuando el modelo no dio el del muro. Es el mismo respaldo que
    /// usa el dibujante para un muro cualquiera, y sin él el castillo quedaría de peralte cero
    /// —o sea, sin dibujar—.
    /// </param>
    public static ElementoPlanta Como(ElementoPlanta muro, double espesorPorOmision)
    {
        var dx = muro.X2 - muro.X1;
        var dy = muro.Y2 - muro.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        var espesor = muro.AnchoM > Nada ? muro.AnchoM : espesorPorOmision;

        // Un shell de un solo punto no tiene dirección ni largo: se le da el espesor en las
        // dos, que es un castillo cuadrado, en lugar de dejarlo sin dibujar.
        var b = largo > Nada ? largo : espesor;

        var cx = (muro.X1 + muro.X2) / 2;
        var cy = (muro.Y1 + muro.Y2) / 2;

        return new ElementoPlanta
        {
            Clase = ClasePlanta.Columna,
            Tipo = Palabra,
            Forma = "RECT",

            Etiqueta = muro.Etiqueta,
            Seccion = muro.Seccion,
            Notas = muro.Notas,
            Material = muro.Material,

            // El PIER no se copia: es el rótulo del muro, y en un castillo lo que se rotula es
            // su etiqueta y su sección, igual que en uno de frame.
            X1 = cx,
            Y1 = cy,
            X2 = cx,
            Y2 = cy,

            // Las cotas SÍ, tal cual: en el corte el castillo va de su desplante a su
            // cerramiento, y esa altura es la del shell.
            Z1 = muro.Z1,
            Z2 = muro.Z2,

            AnchoM = b,
            PeralteM = espesor,
            AnguloGrados = largo > Nada ? Math.Atan2(dy, dx) * 180 / Math.PI : 0
        };
    }

    /// <summary>
    /// Cambia <b>en la lista</b> los shells que dicen CASTILLO por su castillo, y dice cuántos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se hace <b>antes de dibujar nada</b>, y por un motivo que se ve en el plano: los apoyos
    /// y las huellas se calculan al principio, así que si la conversión llegara después, los
    /// muros y las cadenas morirían en el <b>eje</b> de este castillo en lugar de en su paño, y
    /// el contorno de la losa se metería por dentro de él.
    /// </para>
    /// <para>
    /// Es <b>idempotente</b>: al segundo paso ya no queda ningún muro que decir CASTILLO, así
    /// que dibujar dos veces la misma planta no duplica ni desplaza nada.
    /// </para>
    /// </remarks>
    /// <returns>Cuántos se convirtieron, para la bitácora.</returns>
    public static int Normalizar(IList<ElementoPlanta>? elementos, double espesorPorOmision)
    {
        if (elementos is null)
        {
            return 0;
        }

        var cuantos = 0;

        for (var i = 0; i < elementos.Count; i++)
        {
            if (!Dice(elementos[i]))
            {
                continue;
            }

            elementos[i] = Como(elementos[i], espesorPorOmision);
            cuantos++;
        }

        return cuantos;
    }
}
