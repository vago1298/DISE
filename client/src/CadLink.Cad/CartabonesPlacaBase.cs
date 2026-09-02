namespace CadLink.Cad;

/// <summary>
/// El reparto de los <b>cartabones</b> —los atiesadores— de una placa base, visto en planta.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>DibujarCartabones</c> y <c>PosicionCartabon</c> de la macro
/// <c>DibujarPlacaBase_BloqueXX</c>.
/// </para>
/// <para>
/// Vive <b>aparte del dibujante y sin nada de COM</b>, igual que <see cref="AnclasPlacaBase"/> y
/// <see cref="Estribos"/>. Aquí el motivo es concreto y no una manía de orden: esta geometría la
/// necesitan <b>dos</b> —el dibujante, para mandarla a AutoCAD, y la vista previa de la hoja, para
/// enseñarla en pantalla—. Estando dentro del dibujante y mezclada con las llamadas a
/// <c>AddLightWeightPolyline</c>, la previa no podía usarla y habría tenido que reimplementar el
/// reparto: dos juegos de la misma cuenta, y la previa enseñando unos cartabones y el plano otros.
/// </para>
/// <para>
/// Y de paso se puede comprobar sin AutoCAD delante, que es lo único que se puede hacer aquí.
/// </para>
/// </remarks>
public static class CartabonesPlacaBase
{
    /// <summary>
    /// La fracción de la cara del perfil sobre la que se reparten.
    /// </summary>
    /// <remarks>
    /// El 60 % es de la macro, y tiene sentido: un cartabón en el extremo del patín cae donde el
    /// perfil ya no tiene alma que lo respalde, así que la carga que recoge no tiene por dónde
    /// bajar.
    /// </remarks>
    public const double FraccionDeLaCara = 0.6;

    /// <summary>Un cartabón visto en planta: el rectángulo que se dibuja.</summary>
    /// <param name="X1">Esquina, en unidades de dibujo.</param>
    /// <param name="Y1">Esquina, en unidades de dibujo.</param>
    /// <param name="X2">Esquina opuesta.</param>
    /// <param name="Y2">Esquina opuesta.</param>
    /// <param name="EsX">
    /// Sale de una cara <b>Y</b> del perfil y por tanto lo gobiernan los datos de X. Ver la nota
    /// del cruce en <see cref="Construir"/>. Decide con qué espesor se rotula.
    /// </param>
    public readonly record struct Cartabon(double X1, double Y1, double X2, double Y2, bool EsX);

    /// <summary>
    /// Coloca los cartabones alrededor del perfil, en coordenadas del dibujo.
    /// </summary>
    /// <param name="p">La placa, con sus cantidades, espesores y longitudes.</param>
    /// <param name="xc">Centro del perfil, en unidades de dibujo.</param>
    /// <param name="yc">Centro del perfil, en unidades de dibujo.</param>
    /// <param name="pX">Ancho del perfil ya orientado, en unidades de dibujo.</param>
    /// <param name="pY">Alto del perfil ya orientado, en unidades de dibujo.</param>
    /// <param name="escala">Cuántas unidades de dibujo mide un centímetro.</param>
    /// <remarks>
    /// <para>
    /// La cantidad de cada dirección es el <b>total</b>, y se reparte mitad y mitad entre las dos
    /// caras opuestas, con la impar en la cara positiva. Es el mismo criterio que las anclas.
    /// </para>
    /// <para>
    /// <b>Y van cruzados a propósito:</b> los datos de X —cantidad, espesor y longitud— dibujan los
    /// cartabones que salen de las caras <b>Y</b>, y los de Y salen de las caras <b>X</b>. Es la
    /// corrección que la propia macro documenta: la hoja maneja la longitud en el sentido opuesto
    /// al espesor visto en planta. Intercambiarlos dibuja los cartabones con la longitud del otro
    /// sentido, y eso no se ve en la tabla: se ve en el plano, y solo si se mide.
    /// </para>
    /// </remarks>
    /// <param name="contorno">
    /// El paño del perfil, ya girado y en unidades de dibujo. Con él, cada cartabón arranca del
    /// <b>acero que de verdad tiene al lado</b> en lugar del rectángulo envolvente. En <c>null</c> se
    /// usa el envolvente, que es lo que se hacía antes: sirve de respaldo cuando no hay perfil
    /// dibujado —una placa sin columna— y ahí el envolvente es lo único que hay.
    /// </param>
    public static List<Cartabon> Construir(
        PlacaBaseCad p, double xc, double yc, double pX, double pY, double escala,
        double[]? contorno = null)
    {
        var salida = new List<Cartabon>();

        if (!p.ConCartabones || escala <= 0)
        {
            return salida;
        }

        var espX = p.EspCartabonXCm * escala;
        var espY = p.EspCartabonYCm * escala;
        var largoX = p.LongCartabonXCm * escala;
        var largoY = p.LongCartabonYCm * escala;

        // Sin espesor o sin longitud no hay cartabón que dibujar. La macro pone la cantidad en cero
        // en ese caso, en lugar de dibujar una placa de grueso nulo: una polilínea de ancho cero se
        // ve como una línea suelta y parece un error del dibujo, no un dato que falta.
        var nX = espX > 0 && largoX > 0 ? Math.Max(0, p.NCartabonesX) : 0;
        var nY = espY > 0 && largoY > 0 ? Math.Max(0, p.NCartabonesY) : 0;

        // ---------- Cartabones X: placas verticales, desde las caras +Y y -Y ----------
        for (var lado = 0; lado <= 1; lado++)
        {
            var cuantos = lado == 0 ? (nX + 1) / 2 : nX / 2;

            for (var i = 1; i <= cuantos; i++)
            {
                var x = Posicion(xc, pX, i, cuantos);

                if (lado == 0)
                {
                    var cara = CaraDeArriba(contorno, x, yc + (pY / 2));

                    salida.Add(new Cartabon(
                        x - (espX / 2), cara, x + (espX / 2), cara + largoX, EsX: true));
                }
                else
                {
                    var cara = CaraDeAbajo(contorno, x, yc - (pY / 2));

                    salida.Add(new Cartabon(
                        x - (espX / 2), cara - largoX, x + (espX / 2), cara, EsX: true));
                }
            }
        }

        // ---------- Cartabones Y: placas horizontales, desde las caras +X y -X ----------
        for (var lado = 0; lado <= 1; lado++)
        {
            var cuantos = lado == 0 ? (nY + 1) / 2 : nY / 2;

            for (var i = 1; i <= cuantos; i++)
            {
                var y = Posicion(yc, pY, i, cuantos);

                if (lado == 0)
                {
                    var cara = CaraDerecha(contorno, y, xc + (pX / 2));

                    salida.Add(new Cartabon(
                        cara, y - (espY / 2), cara + largoY, y + (espY / 2), EsX: false));
                }
                else
                {
                    var cara = CaraIzquierda(contorno, y, xc - (pX / 2));

                    salida.Add(new Cartabon(
                        cara - largoY, y - (espY / 2), cara, y + (espY / 2), EsX: false));
                }
            }
        }

        return salida;
    }

    // ======================================================================
    //  DE DÓNDE ARRANCA CADA CARTABÓN
    // ======================================================================
    //
    //  ═════════════════════════════════════════════════════════════════════════════════════
    //  EL CARTABÓN ARRANCA DEL ACERO, NO DEL RECTÁNGULO ENVOLVENTE.
    //
    //  Antes se usaba el envolvente del perfil, y en un perfil I eso deja el cartabón del eje
    //  Y flotando en el aire: se colocaba a la altura del centro pero arrancando en la punta
    //  del patín, y a media altura el patín no está —está el hueco entre los dos—. En el plano
    //  se veía una placa suelta al lado de la columna, sin nada que la uniera.
    //
    //  Con un rayo contra el contorno, a media altura de una I lo que se encuentra es el
    //  ALMA, que es donde el cartabón se suelda. Y no hay que preguntarle la forma a nadie:
    //  sale de la geometría, así que vale igual para la te, la canal, el ángulo o el tubo.
    //  ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>El paño derecho del perfil a la altura <paramref name="y"/>.</summary>
    /// <param name="respaldo">El del envolvente, para cuando no hay contorno o el rayo no cruza.</param>
    private static double CaraDerecha(double[]? contorno, double y, double respaldo) =>
        ContornoDesplazado.CruceHorizontal(contorno, y, 1) ?? respaldo;

    /// <summary>El paño izquierdo del perfil a la altura <paramref name="y"/>.</summary>
    private static double CaraIzquierda(double[]? contorno, double y, double respaldo) =>
        ContornoDesplazado.CruceHorizontal(contorno, y, -1) ?? respaldo;

    /// <summary>El paño de arriba del perfil en la abscisa <paramref name="x"/>.</summary>
    private static double CaraDeArriba(double[]? contorno, double x, double respaldo) =>
        ContornoDesplazado.CruceVertical(contorno, x, 1) ?? respaldo;

    /// <summary>El paño de abajo del perfil en la abscisa <paramref name="x"/>.</summary>
    private static double CaraDeAbajo(double[]? contorno, double x, double respaldo) =>
        ContornoDesplazado.CruceVertical(contorno, x, -1) ?? respaldo;

    /// <summary>
    /// Reparte los cartabones sobre el <see cref="FraccionDeLaCara">60 % central</see> de la cara.
    /// </summary>
    /// <remarks>
    /// Con uno solo va al centro, que es donde está el alma.
    /// </remarks>
    public static double Posicion(double centro, double dimension, int indice, int cuantos)
    {
        if (cuantos <= 1 || dimension <= 0)
        {
            return centro;
        }

        var tramo = FraccionDeLaCara * dimension;

        return centro - (tramo / 2) + ((indice - 1) * tramo / (cuantos - 1));
    }

    /// <summary>
    /// Cuántos cartabones salen en total, <b>ya repartidos</b>.
    /// </summary>
    /// <remarks>
    /// Se contesta con la misma regla que usa <see cref="Construir"/> —incluido el descarte por
    /// espesor o longitud en cero— para que la tabla diga el número que de verdad se va a dibujar.
    /// Contestar <c>nx + ny</c> a secas prometería cartabones que el dibujo no pone.
    /// </remarks>
    public static int Cuantos(PlacaBaseCad p)
    {
        if (!p.ConCartabones)
        {
            return 0;
        }

        var nX = p.EspCartabonXCm > 0 && p.LongCartabonXCm > 0 ? Math.Max(0, p.NCartabonesX) : 0;
        var nY = p.EspCartabonYCm > 0 && p.LongCartabonYCm > 0 ? Math.Max(0, p.NCartabonesY) : 0;

        return nX + nY;
    }
}
