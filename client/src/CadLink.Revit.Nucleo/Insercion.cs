namespace CadLink.Revit.Nucleo;

/// <summary>Contra que cara de la seccion se mide la linea que exporta el calculo.</summary>
public enum CaraDeInsercion
{
    /// <summary>La linea pasa por la cara de ABAJO: la pieza queda por encima.</summary>
    Abajo,

    /// <summary>La linea pasa por el centro: la pieza queda repartida.</summary>
    Centro,

    /// <summary>La linea pasa por la cara de ARRIBA: la pieza cuelga por debajo.</summary>
    Arriba
}

/// <summary>
/// El punto de insercion de ETABS, traducido a lo que Revit entiende.
/// </summary>
/// <remarks>
/// <para>
/// ETABS no coloca una viga por su centroide: la coloca por su <b>punto cardinal</b>, que dice
/// que punto de la seccion va sobre la linea de los nudos. Son once, en una cuadricula de tres
/// por tres mas el centroide y el centro de cortante:
/// </para>
/// <code>
///   7 8 9   arriba      (7 izquierda, 8 centro, 9 derecha)
///   4 5 6   medio
///   1 2 3   abajo
///   10 centroide, 11 centro de cortante
/// </code>
/// <para>
/// Esto hacia falta porque el complemento tenia el <b>8 escrito a mano</b> para todas las
/// trabes. El 8 -arriba al centro- es el habitual de una cadena, pero <b>el de omision de ETABS
/// es el 10</b>, el centroide. Con el 10, la linea que se exporta ya pasa por el centro de la
/// seccion, asi que forzar «arriba» sube la pieza media seccion o una entera, segun donde tenga
/// el origen la familia. Ese es el alzado que no se iba.
/// </para>
/// <para>
/// <b>Solo se traduce la parte VERTICAL.</b> El lector de ETABS ya suma el corrimiento en planta
/// a las coordenadas de la barra -de modo que la X y la Y que llegan son las del centro de la
/// seccion- y deja la Z sin tocar a proposito, porque mover la elevacion cambiaria el nivel al
/// que se reparte la pieza. Asi que a lo ancho la seccion va SIEMPRE centrada, y lo unico que
/// depende del punto cardinal es contra que cara se mide la altura.
/// </para>
/// </remarks>
public static class Insercion
{
    /// <summary>El punto cardinal de omision de ETABS: el centroide.</summary>
    public const int Centroide = 10;

    /// <summary>Contra que cara se mide la linea, segun el punto cardinal.</summary>
    /// <remarks>
    /// La fila de la cuadricula es lo unico que importa: 1, 2 y 3 van abajo; 4, 5 y 6 al medio;
    /// 7, 8 y 9 arriba. El centroide y el centro de cortante -10 y 11- y cualquier valor raro se
    /// tratan como el centro, que es lo que son.
    /// </remarks>
    public static CaraDeInsercion Cara(int puntoCardinal) => puntoCardinal switch
    {
        1 or 2 or 3 => CaraDeInsercion.Abajo,
        4 or 5 or 6 => CaraDeInsercion.Centro,
        7 or 8 or 9 => CaraDeInsercion.Arriba,
        _ => CaraDeInsercion.Centro
    };

    /// <summary>
    /// La cota de la cara INFERIOR de la pieza, conocida la de su linea y su peralte.
    /// </summary>
    /// <remarks>
    /// Es lo que necesita el ajuste de muros: el muro tiene que morir donde empieza su cadena, y
    /// donde empieza depende del punto cardinal. Con el 8 la cadena cuelga entera bajo la cota
    /// del piso; con el 10 solo cuelga la mitad; con el 2 no cuelga nada y se apoya encima.
    /// </remarks>
    public static double CaraInferior(double zLinea, double peralteM, int puntoCardinal) =>
        Cara(puntoCardinal) switch
        {
            CaraDeInsercion.Arriba => zLinea - peralteM,
            CaraDeInsercion.Centro => zLinea - (peralteM / 2.0),
            _ => zLinea
        };

    /// <summary>Cuanto cuelga la pieza por debajo de su linea.</summary>
    public static double CuelgaM(double peralteM, int puntoCardinal) =>
        Cara(puntoCardinal) switch
        {
            CaraDeInsercion.Arriba => peralteM,
            CaraDeInsercion.Centro => peralteM / 2.0,
            _ => 0
        };
}
