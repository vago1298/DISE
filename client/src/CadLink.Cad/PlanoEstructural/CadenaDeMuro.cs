using System;
using System.Collections.Generic;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// La <b>cadena modelada como shell de muro</b>: se dibuja como cadena, no como muro.
/// </summary>
/// <remarks>
/// <para>
/// Es el hermano de <see cref="CastilloDeMuro"/> y sale del mismo sitio: en ETABS una cadena se
/// puede modelar como <b>frame</b> —una barra con su sección— o como un <b>shell</b>, que es lo que
/// pasa cuando se dibuja junto con el muro del que forma parte. Y las cadenas
/// <b>INTERMEDIAS</b> —las que confinan los vanos de puertas y ventanas, o las que rematan un
/// antepecho— casi siempre se modelan así, porque se dibujan como un trozo del propio muro.
/// </para>
/// <para>
/// Dibujada como muro, la cadena <b>no era una cadena para nada</b>: no llevaba su relleno en el
/// corte, no llevaba su bloque, no iba a la capa de las cadenas y en el alzado se leía como un paño
/// de mampostería. Era justo lo que se reportó una y otra vez de la cadena intermedia: «no la
/// rellenas ni le haces bloque».
/// </para>
/// <para>
/// Aquí el shell se convierte en una <b>barra</b> —clase trabe—, y a partir de ahí lo dibuja el
/// mismo camino que una cadena de frame: su capa <c>E-CADENA</c>, su rótulo, su relleno morado
/// cuando el corte la cruza y su bloque de la cara que llega.
/// </para>
/// <para>
/// <b>Solo si las notas lo dicen.</b> La palabra la pone el modelo —CADENA DE CERRAMIENTO, CADENA
/// INTERMEDIA, CADENA DE DESPLANTE o DALA— y es la única condición: no se mira el tamaño. Un shell
/// bajito puede ser un antepecho o un pretil, y convertirlo «porque mide 25 cm» sería inventar una
/// cadena que el ingeniero no puso.
/// </para>
/// </remarks>
public static class CadenaDeMuro
{
    /// <summary>Menos que esto no es una medida: es un shell degenerado.</summary>
    private const double Nada = 1e-9;

    /// <summary>
    /// ¿Este muro es en realidad una <b>cadena</b>?
    /// </summary>
    /// <remarks>
    /// Se pregunta por el <see cref="ElementoPlanta.Tipo"/> —que la ventana ya clasificó con las
    /// notas de la propiedad— y, de respaldo, por las <b>notas</b> tal cual. El nombre de la
    /// sección no cuenta, igual que en el castillo: una propiedad de muro llamada «MURO CON CADENA»
    /// es un muro.
    /// </remarks>
    public static bool Dice(ElementoPlanta? el)
    {
        if (el is null || el.Clase != ClasePlanta.Muro)
        {
            return false;
        }

        // Un shell que dice CASTILLO es un castillo, y ese tiene su propia conversión. Si las
        // notas dijeran las dos cosas, manda el castillo: es la pieza vertical.
        if (CastilloDeMuro.Dice(el))
        {
            return false;
        }

        return DicenLasNotas(el.Tipo, el.Notas);
    }

    /// <summary>¿El tipo o las notas hablan de una <b>cadena</b> o de una <b>dala</b>?</summary>
    /// <remarks>
    /// Está aparte para que lo pueda preguntar también la <b>ventana</b>: la casilla que decide si
    /// un elemento se dibuja va por clase, y un shell que es una cadena tiene que seguir a la
    /// casilla de las <b>trabes</b>, no a la de los muros. Si no, quien apaga los muros para ver
    /// solo la estructura perdería las cadenas.
    /// </remarks>
    public static bool DicenLasNotas(string? tipo, string? notas)
    {
        var t = (tipo ?? string.Empty).Trim();

        if (t.StartsWith("CADENA", StringComparison.OrdinalIgnoreCase)
            || t.Equals("DALA", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var n = (notas ?? string.Empty).ToUpperInvariant();

        return n.Contains("CADENA", StringComparison.Ordinal)
               || n.Contains("DALA", StringComparison.Ordinal);
    }

    /// <summary>
    /// El mismo elemento, pero ya como <b>cadena</b>: una barra con su sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La traducción es la que dicta la geometría de un shell vertical:
    /// </para>
    /// <list type="bullet">
    ///   <item>su <b>largo en planta</b> es el recorrido de la cadena, y se conserva tal cual;</item>
    ///   <item>el <b>espesor</b> del muro es el <b>ancho</b> de la sección;</item>
    ///   <item>y su <b>alto</b> —lo que mide en Z— es el <b>peralte</b>.</item>
    /// </list>
    /// <para>
    /// La cota se pone <b>arriba</b>, en su cara superior, porque una barra cuelga de su cota: es
    /// como se dibuja una cadena en el corte y como se reparte por niveles. Con la cota abajo, la
    /// cadena de cerramiento aparecería un peralte por encima del techo.
    /// </para>
    /// <para>
    /// Se devuelve un elemento <b>nuevo</b>: el que llega no se toca, para que el visor y la tabla
    /// sigan diciendo lo que el modelo dice.
    /// </para>
    /// </remarks>
    /// <param name="muro">El shell, tal como llegó del modelo.</param>
    /// <param name="peraltePorOmision">
    /// Peralte en metros para cuando el shell no dice su alto. Sin él la cadena quedaría de peralte
    /// cero, o sea sin dibujar.
    /// </param>
    public static ElementoPlanta Como(ElementoPlanta muro, double peraltePorOmision = 0.20)
    {
        var zAbajo = Math.Min(muro.Z1, muro.Z2);
        var zArriba = Math.Max(muro.Z1, muro.Z2);

        var alto = zArriba - zAbajo;

        var peralte = alto > Nada ? alto : peraltePorOmision;

        return new ElementoPlanta
        {
            Clase = ClasePlanta.Trabe,

            // El tipo se conserva: de él salen la capa —E-CADENA o E-CADENA DESPLANTE— y el color
            // del relleno en el corte. Si llegó en blanco se le pone el genérico.
            Tipo = string.IsNullOrWhiteSpace(muro.Tipo) ? "DALA" : muro.Tipo,
            Forma = "RECT",

            Etiqueta = muro.Etiqueta,
            Seccion = muro.Seccion,
            Notas = muro.Notas,
            Material = muro.Material,

            X1 = muro.X1,
            Y1 = muro.Y1,
            X2 = muro.X2,
            Y2 = muro.Y2,

            // ARRIBA las dos: una barra cuelga de su cota. Con la cota abajo, la cadena de
            // cerramiento saldría un peralte por encima del techo.
            Z1 = zArriba,
            Z2 = zArriba,

            AnchoM = muro.AnchoM,
            PeralteM = peralte,

            // Una cadena de shell lleva su muro debajo por definición —es un trozo de ese muro—,
            // así que su línea va continua y no a trazos.
            MuroDePisoATecho = true,

            DeShell = true
        };
    }

    /// <summary>
    /// Cambia <b>en la lista</b> los shells que dicen cadena por su cadena, y dice cuántos.
    /// </summary>
    /// <remarks>
    /// Va <b>después</b> de convertir los castillos y antes de dibujar: de estas cadenas dependen el
    /// muro que se tapa debajo, el paño al que muere el armado de la losa y, en el corte, la altura
    /// del muro que remata.
    /// </remarks>
    /// <returns>Cuántas se convirtieron, para la bitácora.</returns>
    public static int Normalizar(
        IList<ElementoPlanta>? elementos, double peraltePorOmision = 0.20)
    {
        if (elementos is null)
        {
            return 0;
        }

        var cuantas = 0;

        for (var i = 0; i < elementos.Count; i++)
        {
            if (!Dice(elementos[i]))
            {
                continue;
            }

            elementos[i] = Como(elementos[i], peraltePorOmision);
            cuantas++;
        }

        return cuantas;
    }
}
