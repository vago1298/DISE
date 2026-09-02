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

    // ======================================================================
    //  HACIA DÓNDE SALE CADA CARTABÓN
    // ======================================================================
    //
    //  Y de paso, cuántos giros de 90° hay que darle al marco local para llegar ahí. Es lo que
    //  permite que la boca de pescado se calcule UNA vez, mirando hacia +X, y las otras tres
    //  direcciones salgan girando. Cuatro bloques con sus signos escritos a mano es donde se
    //  esconde un error que en el plano se ve como un cartabón montado sobre el tubo.

    private const int Derecha = 0;
    private const int Arriba = 1;
    private const int Izquierda = 2;
    private const int Abajo = 3;

    /// <summary>Un cartabón visto en planta: la polilínea cerrada que se dibuja.</summary>
    /// <param name="Puntos">
    /// Plano y cerrado: <c>x1,y1,x2,y2…</c>, como los quiere AutoCAD, y en sentido
    /// <b>antihorario</b>.
    /// </param>
    /// <param name="Dobleces">
    /// Los bulges de los vértices que llevan arco. Solo la <b>boca de pescado</b> trae uno; un
    /// cartabón contra un perfil recto no lleva ninguno y aquí viene <c>null</c>.
    /// </param>
    /// <param name="EsX">
    /// Sale de una cara <b>Y</b> del perfil y por tanto lo gobiernan los datos de X. Ver la nota
    /// del cruce en <see cref="Construir"/>. Decide con qué espesor se rotula.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Puntos, y no cuatro esquinas.</b> Mientras el cartabón fue siempre un rectángulo, dos
    /// esquinas opuestas bastaban. La boca de pescado —el recorte que lo ajusta a una columna
    /// redonda— es un <b>arco</b>, y un rectángulo no puede describirlo: con cuatro números el
    /// cartabón seguiría arrancando en la tangente del tubo y dejando la media luna de hueco que
    /// hay que rellenar de soldadura.
    /// </para>
    /// <para>
    /// Las esquinas siguen a mano en <see cref="X1"/>…<see cref="Y2"/>, que ahora son el
    /// <b>envolvente</b> y no los datos de origen. A los leaders y al encuadre de la previa les
    /// basta con eso, así que no hubo que tocarlos.
    /// </para>
    /// </remarks>
    public readonly record struct Cartabon(
        double[] Puntos, (int Indice, double Bulge)[]? Dobleces, bool EsX)
    {
        /// <summary>La X menor del envolvente.</summary>
        public double X1 => Extremo(0, menor: true);

        /// <summary>La Y menor del envolvente.</summary>
        public double Y1 => Extremo(1, menor: true);

        /// <summary>La X mayor del envolvente.</summary>
        public double X2 => Extremo(0, menor: false);

        /// <summary>La Y mayor del envolvente.</summary>
        public double Y2 => Extremo(1, menor: false);

        /// <summary>¿Lleva boca de pescado?</summary>
        public bool ConBoca => Dobleces is { Length: > 0 };

        /// <summary>El cartabón recto de siempre, por sus dos esquinas opuestas.</summary>
        /// <remarks>
        /// Los cuatro vértices salen en <b>antihorario</b>, igual que los de la boca de pescado:
        /// así el desplazamiento hacia fuera del contorno —el de la soldadura— no tiene que
        /// preguntarse el sentido de cada cartabón.
        /// </remarks>
        public static Cartabon Recto(double x1, double y1, double x2, double y2, bool esX) =>
            new(new[] { x1, y1, x2, y1, x2, y2, x1, y2 }, null, esX);

        /// <remarks>
        /// El arco de la boca <b>muerde hacia dentro</b> del cartabón, así que nunca se sale del
        /// envolvente de los vértices y no hay que tenerlo en cuenta aquí.
        /// </remarks>
        private double Extremo(int eje, bool menor)
        {
            if (Puntos is null || Puntos.Length < eje + 1)
            {
                return 0;
            }

            var r = Puntos[eje];

            for (var i = eje; i < Puntos.Length; i += 2)
            {
                if (menor ? Puntos[i] < r : Puntos[i] > r)
                {
                    r = Puntos[i];
                }
            }

            return r;
        }
    }

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
    /// <b>acero que de verdad tiene al lado</b> en lugar del rectángulo envolvente, y si la columna
    /// es <b>redonda</b> se le recorta la boca de pescado. En <c>null</c> se usa el envolvente, que
    /// es lo que se hacía antes: sirve de respaldo cuando no hay perfil dibujado —una placa sin
    /// columna— y ahí el envolvente es lo único que hay.
    /// </param>
    public static List<Cartabon> Construir(
        PlacaBaseCad p, double xc, double yc, double pX, double pY, double escala,
        ContornoDeColumna? contorno = null)
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

        // El paño poligonal sirve para el rayo, y la circunferencia para la boca de pescado. Son
        // excluyentes: PanoDeLaColumna entrega puntos O círculo, nunca los dos.
        var puntos = contorno?.Puntos;
        var circulo = contorno?.Circulo;

        // ---------- Cartabones X: placas verticales, desde las caras +Y y -Y ----------
        for (var lado = 0; lado <= 1; lado++)
        {
            var cuantos = lado == 0 ? (nX + 1) / 2 : nX / 2;

            for (var i = 1; i <= cuantos; i++)
            {
                var x = Posicion(xc, pX, i, cuantos);

                salida.Add(lado == 0
                    ? Uno(Arriba, xc, yc, x, CaraDeArriba(puntos, x, yc + (pY / 2)),
                          espX, largoX, circulo, esX: true)
                    : Uno(Abajo, xc, yc, x, CaraDeAbajo(puntos, x, yc - (pY / 2)),
                          espX, largoX, circulo, esX: true));
            }
        }

        // ---------- Cartabones Y: placas horizontales, desde las caras +X y -X ----------
        for (var lado = 0; lado <= 1; lado++)
        {
            var cuantos = lado == 0 ? (nY + 1) / 2 : nY / 2;

            for (var i = 1; i <= cuantos; i++)
            {
                var y = Posicion(yc, pY, i, cuantos);

                salida.Add(lado == 0
                    ? Uno(Derecha, xc, yc, y, CaraDerecha(puntos, y, xc + (pX / 2)),
                          espY, largoY, circulo, esX: false)
                    : Uno(Izquierda, xc, yc, y, CaraIzquierda(puntos, y, xc - (pX / 2)),
                          espY, largoY, circulo, esX: false));
            }
        }

        return salida;
    }

    /// <summary>Un cartabón: con boca de pescado si la columna es redonda, y recto si no.</summary>
    /// <param name="direccion">
    /// <see cref="Derecha"/>, <see cref="Arriba"/>, <see cref="Izquierda"/> o <see cref="Abajo"/>.
    /// </param>
    /// <param name="centro">
    /// Dónde cae el eje del cartabón sobre la cara: la <b>X</b> para los que salen hacia arriba o
    /// hacia abajo, y la <b>Y</b> para los que salen a los lados.
    /// </param>
    /// <param name="cara">De dónde arranca cuando NO lleva boca: el paño que encontró el rayo.</param>
    private static Cartabon Uno(
        int direccion, double xc, double yc, double centro, double cara,
        double esp, double largo, (double Cx, double Cy, double R)? circulo, bool esX)
    {
        var boca = BocaDePescado(direccion, xc, yc, centro, esp, largo, circulo, esX);

        if (boca is not null)
        {
            return boca.Value;
        }

        var medio = esp / 2;

        return direccion switch
        {
            Derecha => Cartabon.Recto(cara, centro - medio, cara + largo, centro + medio, esX),
            Arriba => Cartabon.Recto(centro - medio, cara, centro + medio, cara + largo, esX),
            Izquierda => Cartabon.Recto(cara - largo, centro - medio, cara, centro + medio, esX),
            _ => Cartabon.Recto(centro - medio, cara - largo, centro + medio, cara, esX),
        };
    }

    // ======================================================================
    //  LA BOCA DE PESCADO
    // ======================================================================
    //
    //  ═════════════════════════════════════════════════════════════════════════════════════
    //  CONTRA UN TUBO REDONDO, EL CARTABÓN RECTO NO SE PEGA: SE TOCA EN UN PUNTO.
    //
    //  El rayo del paño no sirve aquí —una columna redonda no tiene contorno poligonal, así que
    //  se caía al rectángulo envolvente— y el envolvente de un círculo es su tangente: el
    //  cartabón arrancaba en el punto más saliente del tubo y dejaba, a cada lado de su
    //  espesor, una media luna de hueco. En el taller eso se resuelve recortando el canto con
    //  la curva del tubo, y es lo que se llama boca de pescado.
    //
    //  Se calcula UNA vez, en un marco local que mira hacia +X, y las otras tres direcciones
    //  salen girando 90° alrededor del centro del círculo. Escribir los cuatro casos a mano es
    //  escribir cuatro veces los mismos senos y cosenos con signos distintos, y el que se
    //  equivoque no falla: dibuja un cartabón montado dentro del tubo, que parece correcto
    //  hasta que alguien lo mide.
    //  ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>El cartabón recortado a la curva del tubo, o <c>null</c> si no procede.</summary>
    /// <remarks>
    /// <para>
    /// En el marco local el círculo está en el origen y el cartabón sale hacia <b>+X</b>, con su eje
    /// en <c>y = t</c>. Sus dos cantos largos —<c>t ± esp/2</c>— cortan la circunferencia en dos
    /// abscisas <b>distintas</b> salvo que el cartabón esté centrado, y de ahí sale el arco.
    /// </para>
    /// <para>
    /// <b>La longitud se mide desde el paño del tubo en el eje del cartabón</b>, que es donde se
    /// acota y donde el dibujante espera verla. Medirla desde el canto más corto haría que dos
    /// cartabones con la misma longitud capturada salieran de distinto largo según lo descentrados
    /// que estuvieran.
    /// </para>
    /// </remarks>
    private static Cartabon? BocaDePescado(
        int direccion, double xc, double yc, double centro,
        double esp, double largo, (double Cx, double Cy, double R)? circulo, bool esX)
    {
        if (circulo is null || esp <= 0 || largo <= 0)
        {
            return null;
        }

        var (cx, cy, r) = circulo.Value;

        if (r <= 0)
        {
            return null;
        }

        // El centro del perfil y el del círculo tienen que ser el mismo: el círculo lo entrega
        // PanoDeLaColumna centrado en el perfil. Si no lo fueran, el desplazamiento del eje que se
        // calcula abajo estaría medido desde el sitio equivocado y la boca saldría descentrada.
        if (Math.Abs(cx - xc) > 1e-6 || Math.Abs(cy - yc) > 1e-6)
        {
            return null;
        }

        // El desplazamiento del eje del cartabón respecto al centro del círculo, YA EN EL MARCO
        // LOCAL. Es la única cuenta que depende de la dirección.
        var t = direccion switch
        {
            Derecha => centro - cy,
            Arriba => cx - centro,
            Izquierda => cy - centro,
            _ => centro - cx,
        };

        var yAlto = t + (esp / 2);
        var yBajo = t - (esp / 2);

        // LOS DOS CANTOS TIENEN QUE CRUZAR EL CÍRCULO. Si uno se sale, no hay boca que recortar: el
        // cartabón pasaría por fuera del tubo y el arco se saldría de su propio canto. Pasa con un
        // cartabón muy descentrado o con un tubo muy chico, y ahí lo correcto es dejarlo recto
        // arrancando del envolvente —tangente al tubo—, que es lo que se hacía antes.
        if (Math.Abs(yAlto) >= r - ContornoDesplazado.Tolerancia
            || Math.Abs(yBajo) >= r - ContornoDesplazado.Tolerancia)
        {
            return null;
        }

        var xAlto = Math.Sqrt((r * r) - (yAlto * yAlto));
        var xBajo = Math.Sqrt((r * r) - (yBajo * yBajo));

        var xPano = Math.Sqrt((r * r) - (t * t));
        var xLejos = xPano + largo;

        // Y la punta libre tiene que quedar MÁS ALLÁ de los dos arranques, o la polilínea se cruza
        // sola y el achurado de su soldadura sale por dentro. Pasa con un cartabón corto y muy
        // descentrado, donde el canto de dentro arranca más lejos que la punta.
        if (xLejos <= Math.Max(xAlto, xBajo) + ContornoDesplazado.Tolerancia)
        {
            return null;
        }

        // Antihorario: se recorre el canto de abajo hacia fuera, se sube por la punta, se vuelve por
        // el canto de arriba, y el ARCO cierra bajando pegado a la circunferencia.
        var locales = new[]
        {
            xBajo, yBajo,
            xLejos, yBajo,
            xLejos, yAlto,
            xAlto, yAlto,
        };

        // El bulge del tramo 3→0, el que sigue al tubo. Va del canto de arriba al de abajo, o sea
        // que recorre la circunferencia en sentido HORARIO alrededor de su centro: por eso el
        // barrido sale negativo, y por eso el arco muerde hacia DENTRO del cartabón en lugar de
        // abombarse contra el tubo. Un bulge positivo aquí metería el cartabón en la columna.
        var barrido = Math.Atan2(yBajo, xBajo) - Math.Atan2(yAlto, xAlto);
        var dobleces = new[] { (3, Math.Tan(barrido / 4)) };

        // Del marco local al dibujo: un giro de 90° por cada cuarto de vuelta. Los bulges no se
        // tocan —girar un arco no le cambia el ángulo que barre— igual que en HaciaFuera.
        var puntos = new double[locales.Length];

        for (var i = 0; i < locales.Length; i += 2)
        {
            var (x, y) = (cx + locales[i], cy + locales[i + 1]);

            for (var giro = 0; giro < direccion; giro++)
            {
                (x, y) = ContornoDesplazado.Girar90Punto(x, y, cx, cy);
            }

            puntos[i] = x;
            puntos[i + 1] = y;
        }

        return new Cartabon(puntos, dobleces, esX);
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
