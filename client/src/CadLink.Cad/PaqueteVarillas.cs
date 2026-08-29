namespace CadLink.Cad;

/// <summary>
/// La regla de los <b>paquetes</b> de varillas en los lechos de esquina.
/// </summary>
/// <remarks>
/// <para>
/// Un lecho de esquina lleva una varilla en cada esquina. Cuando se piden más de dos, no
/// se reparten a lo ancho del lecho —para eso está el lecho <i>intermedio</i>—: se
/// <b>apilan</b> en las esquinas formando paquetes, pegadas unas a otras y hacia el
/// núcleo de la sección.
/// </para>
/// <para>
/// La varilla de fuera de cada paquete es <b>la que da el doblez</b>: es la que el
/// estribo abraza en la esquina, y por eso las demás se cuelgan de ella hacia dentro y no
/// al contrario.
/// </para>
/// <para>
/// Vive en la capa de CAD, sin WPF, porque la usan <b>los dos</b> dibujantes: la vista
/// previa en pantalla y el de AutoCAD. Si cada uno apilara a su manera, el plano
/// dibujaría el paquete en un sitio y la pantalla en otro; y peor, las grapas se guardan
/// por el <b>índice</b> de la varilla dentro del lecho, así que un orden distinto en cada
/// capa las pondría agarradas de varillas distintas sin ningún error que lo delatara.
/// </para>
/// </remarks>
public static class PaqueteVarillas
{
    /// <summary>
    /// ¿Este lecho de esquina va en <b>paquetes</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con una o dos varillas no hay paquete: es una por esquina, el caso de siempre.
    /// </para>
    /// <para>
    /// Con más de dos hace falta que sea <b>par</b>, porque las esquinas son dos y el
    /// armado es simétrico. Un número impar mayor que dos no se puede repartir en dos
    /// paquetes iguales, así que <b>no</b> se apila: se deja el reparto a lo ancho de
    /// antes y la revisión de datos lo señala. Repartir 5 como 3 y 2 dejaría la sección
    /// asimétrica sin que nadie lo hubiera pedido, y quedarse con 4 perdería una varilla
    /// en silencio, que es el tipo de error que este programa existe para no cometer.
    /// </para>
    /// </remarks>
    public static bool EsPaquete(int cantidad) => cantidad > 2 && cantidad % 2 == 0;

    /// <summary>Cuántas varillas lleva cada una de las dos esquinas.</summary>
    public static int PorEsquina(int cantidad) => cantidad / 2;

    /// <summary>
    /// Cuánto se desplaza la varilla <paramref name="k"/> de un paquete respecto de la
    /// que da el doblez.
    /// </summary>
    /// <param name="k">Posición dentro del paquete. La 0 es la del doblez.</param>
    /// <param name="diametro">Diámetro de la varilla, en las mismas unidades que se use.</param>
    /// <param name="arriba">Si el lecho es el superior.</param>
    /// <remarks>
    /// El paso es <b>un diámetro</b> entero: dos varillas pegadas se tocan cuando sus
    /// centros están a un diámetro, o sea tangentes en un solo punto, que es como se
    /// amarra un paquete en obra.
    /// <para>
    /// El signo apunta <b>al núcleo</b>: hacia abajo en el lecho de arriba y hacia arriba
    /// en el de abajo. Siempre hacia dentro, porque hacia fuera el paquete se saldría del
    /// concreto.
    /// </para>
    /// </remarks>
    public static double Desplazamiento(int k, double diametro, bool arriba) =>
        (arriba ? -1.0 : 1.0) * k * diametro;
}
