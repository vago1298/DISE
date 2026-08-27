using System;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>achurado de la mampostería</b> en el corte: uno para el tabique y otro para el tabicón.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió, y es lo que hace que un corte se lea como un corte de albañilería: el área de los
/// muros de <b>mampostería</b> lleva su patrón, y así se distingue de un muro de <b>concreto</b>,
/// que va limpio. En un plano estructural esa diferencia es de obra: uno se levanta con piezas y
/// mortero y el otro se cimbra y se cuela.
/// </para>
/// <para>
/// Y son <b>dos</b> patrones, no uno, porque las piezas no son iguales:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Tabique</b> y <b>adobe</b>: <c>AR-BRSTD</c> a escala <c>0.0010</c>. Es el aparejo de
///     ladrillo, con su traba.
///   </item>
///   <item>
///     <b>Tabicón</b> y <b>tabique ligero</b>: <c>AR-B816</c> a escala <c>0.0005</c>. Es el bloque,
///     que es más grande y va con otra junta.
///   </item>
/// </list>
/// <para>
/// De <b>las notas de la propiedad</b>, que es donde el ingeniero escribe de qué es el muro, y del
/// nombre de la sección como respaldo. Las dos escalas son diminutas porque estos patrones de
/// AutoCAD están pensados para dibujar en <b>pulgadas</b> y aquí se dibuja en metros: a escala 1 el
/// achurado sale como una mancha negra.
/// </para>
/// </remarks>
public static class HatchDeMamposteria
{
    /// <summary>Lo que hace falta para achurar: el patrón, su escala y su color.</summary>
    public sealed record Achurado(string Patron, double Escala, int Color);

    /// <summary>
    /// El achurado que le toca a un muro, o <c>null</c> si <b>no es de mampostería</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El orden de las preguntas importa y no es un detalle: <b>«TABIQUE LIGERO» contiene la
    /// palabra «TABIQUE»</b>, así que preguntando primero por el tabique, el ligero saldría con el
    /// aparejo de ladrillo. Se pregunta de lo más específico a lo más general, igual que en la
    /// clasificación de las cadenas.
    /// </para>
    /// <para>
    /// Un muro de <b>concreto</b> devuelve <c>null</c> y se queda sin achurado, que es lo que se
    /// pidió: en el corte se lee por su paño.
    /// </para>
    /// </remarks>
    /// <param name="notas">Las notas de la propiedad del muro.</param>
    /// <param name="seccion">El nombre de la propiedad, de respaldo.</param>
    /// <param name="patronTabique">Patrón del tabique y del adobe.</param>
    /// <param name="escalaTabique">Su escala.</param>
    /// <param name="patronTabicon">Patrón del tabicón y del tabique ligero.</param>
    /// <param name="escalaTabicon">Su escala.</param>
    /// <param name="color">El color de los dos: el 12 de la hoja.</param>
    public static Achurado? Para(
        string? notas, string? seccion,
        string patronTabique = "AR-BRSTD", double escalaTabique = 0.0010,
        string patronTabicon = "AR-B816", double escalaTabicon = 0.0005,
        int color = 12)
    {
        var texto = Normalizar(notas) + " " + Normalizar(seccion);

        if (texto.Trim().Length == 0)
        {
            return null;
        }

        // EL BLOQUE PRIMERO: «TABIQUE LIGERO» contiene «TABIQUE», así que preguntando al revés el
        // ligero saldría con el aparejo de ladrillo, que es el de la pieza maciza.
        if (texto.Contains("TABICON", StringComparison.Ordinal)
            || texto.Contains("TABIQUE LIGERO", StringComparison.Ordinal)
            || texto.Contains("BLOQUE LIGERO", StringComparison.Ordinal))
        {
            return Hecho(patronTabicon, escalaTabicon, color, "AR-B816", 0.0005);
        }

        if (texto.Contains("TABIQUE", StringComparison.Ordinal)
            || texto.Contains("ADOBE", StringComparison.Ordinal)
            || texto.Contains("LADRILLO", StringComparison.Ordinal))
        {
            return Hecho(patronTabique, escalaTabique, color, "AR-BRSTD", 0.0010);
        }

        return null;
    }

    /// <summary>El achurado, con el respaldo si la hoja trae algo sin sentido.</summary>
    /// <remarks>
    /// Un patrón vacío o una escala de cero no achuran nada —AutoCAD rechaza el hatch— y el muro se
    /// quedaría sin su patrón sin que nadie sepa por qué. Se vuelve al valor de la hoja original.
    /// </remarks>
    private static Achurado Hecho(
        string patron, double escala, int color, string patronPorOmision, double escalaPorOmision)
    {
        var p = (patron ?? string.Empty).Trim();

        return new Achurado(
            p.Length > 0 ? p : patronPorOmision,
            escala > 0 ? escala : escalaPorOmision,
            color is > 0 and <= 255 ? color : 12);
    }

    /// <summary>
    /// A mayúsculas y <b>sin acentos</b>: «TABICÓN» y «TABICON» son la misma palabra.
    /// </summary>
    /// <remarks>
    /// Se escribe de las dos formas según quién teclee las notas y con qué teclado, y un muro que se
    /// queda sin achurado por una tilde es de las cosas que nadie encuentra mirando el plano.
    /// </remarks>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(texto.Length);

        foreach (var c in texto.ToUpperInvariant())
        {
            sb.Append(c switch
            {
                'Á' => 'A',
                'É' => 'E',
                'Í' => 'I',
                'Ó' => 'O',
                'Ú' => 'U',
                'Ü' => 'U',
                'Ñ' => 'N',
                _ => c
            });
        }

        return sb.ToString();
    }
}
