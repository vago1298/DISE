namespace CadLink.Cad;

/// <summary>
/// El <b>alzado</b> de la placa base: la vista de canto que acompaña a la planta.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>DibujarDetallesElevacion</c>, <c>AnchoOcupadoElevacion</c>,
/// <c>DibujarElevacionDireccion</c>, <c>DibujarCartabonElevacion</c>,
/// <c>DibujarAnclasElevacion</c> y <c>DibujarAnclaElevacionIndividual</c> de la macro
/// <c>DibujarPlacaBase_BloqueXX</c>.
/// </para>
/// <para>
/// La planta no dice tres cosas que sí se capturan en la hoja: <b>cuánto se ahoga el ancla</b>
/// —E12 y E13—, <b>cuánto sube el cartabón</b> —F18 y F19— y el <b>espesor de la placa</b>. En
/// planta las tres se ven de canto, o sea que no se ven. El alzado existe para eso.
/// </para>
/// <para>
/// Vive <b>aparte del dibujante y sin nada de COM</b>, igual que <see cref="AnclasPlacaBase"/> y
/// <see cref="CartabonesPlacaBase"/>. Aquí el motivo es el de siempre: es lo único que se puede
/// comprobar sin AutoCAD delante, y el alzado tiene ocho respaldos —«si esto viene en cero, usa
/// aquello»— que es justo la clase de cuenta que sale mal en silencio.
/// </para>
/// </remarks>
public static class ElevacionPlacaBase
{
    /// <summary>A cuánto de la planta arranca el alzado, en cm. <c>SEP_ELEVACION_CM</c>.</summary>
    public const double SeparacionDeLaPlantaCm = 60.0;

    /// <summary>Entre la vista X y la vista Y, en cm.</summary>
    public const double SeparacionEntreVistasCm = 20.0;

    /// <summary>
    /// El chaflán del rincón exterior de arriba del cartabón, en cm. <c>CORTE_CARTABON_CM</c>.
    /// </summary>
    /// <remarks>
    /// Se recorta lo mismo en las dos direcciones, así que la arista queda a <b>45°</b>. Y por eso
    /// el recorte se limita con un solo número: limitando cada dirección por su cuenta el chaflán
    /// dejaría de ser de 45° sin que nada avisara.
    /// </remarks>
    public const double CorteDelCartabonCm = 3.0;

    /// <summary>
    /// Los datos de <b>una</b> dirección: todo lo que su vista necesita.
    /// </summary>
    /// <param name="AnchoPlaca">Lo que mide la placa a lo ancho <b>en esta vista</b>.</param>
    /// <param name="AnchoDado">El dado a lo ancho. Cero = sin dado.</param>
    /// <param name="AnchoPerfil">La columna a lo ancho, de donde arrancan los cartabones.</param>
    /// <param name="LongCartabon">Lo que sobresale el cartabón, en horizontal.</param>
    /// <param name="AltoCartabon">Lo que sube el cartabón. Celdas <b>F18</b> y <b>F19</b>.</param>
    /// <param name="LongAnclaje">Lo que se ahoga el ancla. Celdas <b>E12</b> y <b>E13</b>.</param>
    /// <param name="LongAncla">
    /// La longitud <b>total desarrollada</b> del ancla: lo que se corta y se pide, doblez incluido.
    /// Cero = se deduce del ahogo, que es lo que se dibujaba antes.
    /// </param>
    /// <param name="DoblezAncla">
    /// La <b>pata</b> del doblez del extremo, en horizontal. Cero = ancla recta con su travesaño.
    /// </param>
    /// <param name="SepBorde">Del ancla al canto de la placa, para colocarla en el alzado.</param>
    /// <remarks>Todo en <b>unidades de dibujo</b>, ya orientado y a escala.</remarks>
    public readonly record struct Direccion(
        double AnchoPlaca, double AnchoDado, double AnchoPerfil,
        double LongCartabon, double AltoCartabon, int CuantosCartabones,
        double LongAnclaje, double LongAncla, double DoblezAncla,
        double SepBorde, double DiamAncla, int CuantasAnclas);

    /// <summary>Un ancla vista de canto: vástago, tuerca, arandela y remate o doblez.</summary>
    /// <param name="Vastago">
    /// La barra, de la tuerca al fondo, <b>abierta</b>. Dos puntos si va recta y <b>tres</b> si
    /// lleva doblez: el tercero es la punta de la pata.
    /// </param>
    /// <param name="Tuerca">El rectángulo sobre la placa.</param>
    /// <param name="Arandela">La línea que la apoya. Dos puntos.</param>
    /// <param name="Remate">
    /// El travesaño del fondo, <c>null</c> cuando el ancla lleva doblez: ahí lo que ancla es la
    /// pata, y un travesaño además de la pata dibuja un remate que no existe.
    /// </param>
    /// <param name="Ahogo">Cuánto baja de la cara de abajo de la placa, para que el dado la cubra.</param>
    public readonly record struct AnclaDeCanto(
        double[] Vastago, double[] Tuerca, double[] Arandela, double[]? Remate, double Ahogo)
    {
        /// <summary>¿Lleva doblez en el extremo?</summary>
        public bool ConDoblez => Vastago.Length >= 6;
    }

    /// <summary>Una vista de alzado, completa y lista para dibujar.</summary>
    /// <param name="Id">
    /// <c>"X"</c>, <c>"Y"</c> o <c>"X-Y"</c>, y va entre comillas en el rótulo. La placa cuadrada
    /// lleva una sola vista, porque las dos serían el mismo dibujo.
    /// </param>
    public sealed record Vista(
        string Id,
        double XCentro,
        double Ancho,
        double[] Concreto,
        double[] Placa,
        double[] Columna,
        double[][] Cartabones,
        AnclaDeCanto[] Anclas,
        (double X, double Y) Rotulo);

    /// <summary>
    /// Las vistas de alzado, colocadas a la derecha de la planta.
    /// </summary>
    /// <param name="xInicio">Dónde empieza el alzado: el canto derecho de la planta más 60 cm.</param>
    /// <param name="yPlaca">La cara <b>de abajo</b> de la placa, que es el nivel de arranque.</param>
    /// <param name="escala">Cuántas unidades de dibujo mide un centímetro.</param>
    /// <param name="alturaTexto">Para separar el rótulo del concreto.</param>
    /// <remarks>
    /// <para>
    /// <b>Una vista si la placa es cuadrada, dos si no.</b> Es de la macro, y tiene sentido: en una
    /// placa cuadrada las dos vistas saldrían del mismo ancho y el plano llevaría dos dibujos
    /// iguales con dos rótulos distintos.
    /// </para>
    /// <para>
    /// Y en ese caso <b>manda la dirección X</b>, salvo que X no tenga nada que enseñar —ni
    /// cartabones, ni longitud de anclaje, ni anclas— y entonces se enseña la Y. Ver la nota de
    /// <see cref="Construir"/> sobre lo que esto se lleva por delante.
    /// </para>
    /// </remarks>
    public static List<Vista> Construir(
        double xInicio, double yPlaca, double escala, double alturaTexto,
        double espesorPlaca, bool conCartabones, Direccion x, Direccion y)
    {
        var vistas = new List<Vista>();

        if (escala <= 0)
        {
            return vistas;
        }

        var cartX = LlevaCartabon(conCartabones, x);
        var cartY = LlevaCartabon(conCartabones, y);

        var ocupaX = AnchoOcupado(x, cartX, escala);
        var ocupaY = AnchoOcupado(y, cartY, escala);

        // Cuadrada al centímetro: la tolerancia es de la macro, y a esta escala un milímetro de
        // diferencia entre el largo y el ancho es una placa cuadrada mal capturada, no dos vistas.
        var cuadrada = Math.Abs(x.AnchoPlaca - y.AnchoPlaca) <= 0.01 * escala;

        if (cuadrada)
        {
            var usarX = cartX || x.LongAnclaje > 0 || x.CuantasAnclas > 0;

            var unica = usarX
                ? UnaVista("X-Y", xInicio + (ocupaX / 2), ocupaX, yPlaca, escala, alturaTexto,
                           espesorPlaca, x, cartX)
                : UnaVista("X-Y", xInicio + (ocupaY / 2), ocupaY, yPlaca, escala, alturaTexto,
                           espesorPlaca, y, cartY);

            if (unica is not null)
            {
                vistas.Add(unica);
            }

            return vistas;
        }

        var vx = UnaVista("X", xInicio + (ocupaX / 2), ocupaX, yPlaca, escala, alturaTexto,
                          espesorPlaca, x, cartX);

        if (vx is not null)
        {
            vistas.Add(vx);
        }

        var xVistaY = xInicio + ocupaX + (SeparacionEntreVistasCm * escala) + (ocupaY / 2);

        var vy = UnaVista("Y", xVistaY, ocupaY, yPlaca, escala, alturaTexto,
                          espesorPlaca, y, cartY);

        if (vy is not null)
        {
            vistas.Add(vy);
        }

        return vistas;
    }

    /// <summary>¿Esta dirección enseña cartabones en el alzado?</summary>
    /// <remarks>
    /// Hace falta la <b>altura</b> además de la cantidad y la longitud: un cartabón con altura cero
    /// en el alzado es una línea, y una línea suelta al lado de la columna parece un error del
    /// dibujo y no un dato que falta en la hoja.
    /// </remarks>
    public static bool LlevaCartabon(bool conCartabones, Direccion d) =>
        conCartabones && d.CuantosCartabones > 0 && d.LongCartabon > 0 && d.AltoCartabon > 0;

    /// <summary>Lo que ocupa una vista a lo ancho, para no encimarla con la siguiente.</summary>
    /// <remarks>
    /// El que sobresalga: la placa, el dado, o la columna con sus dos cartabones. Con los
    /// cartabones apagados no se cuentan, y ahí está bien: no se dibujan.
    /// </remarks>
    public static double AnchoOcupado(Direccion d, bool conCartabon, double escala)
    {
        var r = d.AnchoPlaca;

        if (d.AnchoDado > r)
        {
            r = d.AnchoDado;
        }

        if (conCartabon && d.AnchoPerfil + (2 * d.LongCartabon) > r)
        {
            r = d.AnchoPerfil + (2 * d.LongCartabon);
        }

        return r <= 0 ? 20.0 * escala : r;
    }

    // ======================================================================
    //  UNA VISTA
    // ======================================================================
    //
    //  Los respaldos de abajo son de la macro, uno por uno. Son ocho, y todos dicen lo mismo:
    //  «si esa celda viene en cero, dibuja algo razonable». Están aquí y no en el dibujante
    //  porque son la parte del alzado que se puede equivocar sin que se note —un alzado
    //  dibujado con un respaldo se ve igual de terminado que uno dibujado con el dato— y aquí
    //  se pueden comprobar sin AutoCAD delante.

    private static Vista? UnaVista(
        string id, double xCentro, double ancho, double yPlaca, double escala, double alturaTexto,
        double espesorPlaca, Direccion d, bool conCartabon)
    {
        if (d.AnchoPlaca <= 0)
        {
            return null;
        }

        // Sin espesor capturado, 1 cm. Es de la macro; una placa de espesor nulo en el alzado son
        // dos líneas encimadas.
        var esp = espesorPlaca > 0 ? espesorPlaca : 1.0 * escala;

        // La columna: sin medida, el 40 % de la placa, y nunca más del 90 %.
        var anchoPerfil = d.AnchoPerfil > 0 ? d.AnchoPerfil : 0.4 * d.AnchoPlaca;

        if (anchoPerfil > 0.9 * d.AnchoPlaca)
        {
            anchoPerfil = 0.9 * d.AnchoPlaca;
        }

        // El concreto: el dado si lo hay, y si no la placa más 10 cm. Y NUNCA más angosto que la
        // placa, porque el alzado dibuja la placa apoyada encima.
        var anchoConcreto = d.AnchoDado > 0 ? d.AnchoDado : d.AnchoPlaca + (10.0 * escala);

        if (anchoConcreto < d.AnchoPlaca)
        {
            anchoConcreto = d.AnchoPlaca;
        }

        // La columna sube 10 cm más que el cartabón, y al menos 20: si acabara en el cartabón, la
        // pieza se leería como el final de la columna.
        var alturaColumna = Math.Max(d.AltoCartabon + (10.0 * escala), 20.0 * escala);

        var yArriba = yPlaca + esp;

        // LAS ANCLAS PRIMERO, porque ahora gobiernan la profundidad del dado. Ver la nota de
        // ProfundidadDelDado: con la longitud total capturada, el ahogo de la hoja puede quedarse
        // corto, y un ancla dibujada asomando por debajo del concreto es un plano que no se puede
        // construir.
        var anclas = AnclasDeCanto(
            xCentro, yPlaca, yArriba, d.AnchoPlaca, d.SepBorde,
            d.LongAnclaje, d.LongAncla, d.DoblezAncla, esp, d.DiamAncla, d.CuantasAnclas, escala);

        var profundidad = ProfundidadDelDado(d.LongAnclaje, anclas, escala);

        var cartabones = new List<double[]>();

        if (conCartabon)
        {
            var izq = CartabonDeCanto(
                xCentro - (anchoPerfil / 2), yArriba, d.LongCartabon, d.AltoCartabon, -1, escala);

            var der = CartabonDeCanto(
                xCentro + (anchoPerfil / 2), yArriba, d.LongCartabon, d.AltoCartabon, 1, escala);

            if (izq is not null) { cartabones.Add(izq); }
            if (der is not null) { cartabones.Add(der); }
        }

        return new Vista(
            Id: id,
            XCentro: xCentro,
            Ancho: ancho,
            Concreto: Caja(xCentro - (anchoConcreto / 2), yPlaca - profundidad,
                           xCentro + (anchoConcreto / 2), yPlaca),
            Placa: Caja(xCentro - (d.AnchoPlaca / 2), yPlaca,
                        xCentro + (d.AnchoPlaca / 2), yArriba),
            Columna: Caja(xCentro - (anchoPerfil / 2), yArriba,
                          xCentro + (anchoPerfil / 2), yArriba + alturaColumna),
            Cartabones: cartabones.ToArray(),
            Anclas: anclas.ToArray(),
            Rotulo: (xCentro, yPlaca - profundidad - (2.0 * alturaTexto)));
    }

    /// <summary>
    /// El cartabón visto de canto: el rincón exterior de arriba lleva un chaflán a <b>45°</b>.
    /// </summary>
    /// <param name="sentido"><c>+1</c> hacia la derecha, <c>−1</c> hacia la izquierda.</param>
    /// <remarks>
    /// El chaflán es de taller: la punta viva de un atiesador es una concentración de esfuerzo y
    /// además estorba para soldar el rincón. Se recorta lo mismo en las dos direcciones, y por eso
    /// el límite es un solo número aplicado a las dos: limitando cada una por su cuenta la arista
    /// dejaría de estar a 45° sin que nada avisara.
    /// </remarks>
    public static double[]? CartabonDeCanto(
        double xPanoColumna, double yBase, double largo, double alto, int sentido, double escala)
    {
        if (largo <= 0 || alto <= 0)
        {
            return null;
        }

        var corte = CorteDelCartabonCm * escala;

        if (corte > 0.45 * largo) { corte = 0.45 * largo; }
        if (corte > 0.45 * alto) { corte = 0.45 * alto; }

        var xFuera = xPanoColumna + (sentido * largo);

        return new[]
        {
            xPanoColumna, yBase,
            xFuera, yBase,
            xFuera, yBase + alto - corte,
            xFuera - (sentido * corte), yBase + alto,
            xPanoColumna, yBase + alto,
        };
    }

    /// <summary>
    /// Las anclas del alzado: <b>una</b> al centro, o <b>dos</b> a los extremos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es de la macro y es una decisión de dibujo, no un descuido: el alzado enseña <b>cómo</b> se
    /// ahoga el ancla, y para eso bastan las dos de los extremos. Cuántas hay lo dice la planta,
    /// que las tiene todas, y el rótulo, que las cuenta.
    /// </para>
    /// <para>
    /// La posición sale de la separación al borde de la planta, así que las dos vistas coinciden.
    /// Si esa separación no cabe, el 35 % del semiancho: pasa con una placa muy chica.
    /// </para>
    /// </remarks>
    /// <param name="ahogo">Celdas E12 y E13. Se usa cuando no hay longitud total capturada.</param>
    /// <param name="largoTotal">La longitud desarrollada del ancla, doblez incluido.</param>
    /// <param name="doblez">La pata del extremo. Cero = ancla recta.</param>
    /// <param name="espesorPlaca">Para descontar lo que el ancla gasta por encima del concreto.</param>
    public static List<AnclaDeCanto> AnclasDeCanto(
        double xCentro, double yPlaca, double yArriba, double anchoPlaca, double sepBorde,
        double ahogo, double largoTotal, double doblez, double espesorPlaca,
        double diametro, int cuantas, double escala)
    {
        var salida = new List<AnclaDeCanto>();

        // Sin ancla que dibujar si no hay ni ahogo ni longitud: es lo que hace la macro cuando E12
        // viene en cero, y el alzado sale con su dado y sin anclas.
        if (cuantas <= 0 || (ahogo <= 0 && largoTotal <= 0))
        {
            return salida;
        }

        var desplazamiento = (anchoPlaca / 2) - sepBorde;

        if (desplazamiento <= 0)
        {
            desplazamiento = 0.35 * anchoPlaca;
        }

        if (cuantas == 1)
        {
            // La única del centro dobla hacia la derecha: no hay un «hacia dentro» que respetar.
            salida.Add(UnAncla(xCentro, yPlaca, yArriba, ahogo, largoTotal, doblez,
                               espesorPlaca, diametro, 1, escala));

            return salida;
        }

        // LAS PATAS APUNTAN HACIA DENTRO, una contra la otra. Es lo que da recubrimiento: las dos
        // anclas van cerca de los cantos de la placa, así que una pata hacia fuera se acerca a la
        // cara del dado y se queda sin concreto que la sujete. Hacia dentro, el doblez muerde el
        // núcleo confinado, y además no puede salirse del dado por mucho que se alargue.
        salida.Add(UnAncla(xCentro - desplazamiento, yPlaca, yArriba, ahogo, largoTotal, doblez,
                           espesorPlaca, diametro, 1, escala));

        salida.Add(UnAncla(xCentro + desplazamiento, yPlaca, yArriba, ahogo, largoTotal, doblez,
                           espesorPlaca, diametro, -1, escala));

        return salida;
    }

    private static AnclaDeCanto UnAncla(
        double x, double yPlaca, double yArriba, double ahogo, double largoTotal, double doblez,
        double espesorPlaca, double diametro, int sentidoDoblez, double escala)
    {
        var d = diametro > 0 ? diametro : 1.0 * escala;

        // Los mínimos son de la macro, y no son estéticos: una tuerca de menos de 1.5 cm y una
        // arandela de menos de 0.5 cm no se distinguen del vástago al plotear a 1:10.
        var anchoTuerca = Math.Max(2.5 * d, 1.5 * escala);
        var altoTuerca = Math.Max(0.75 * d, 0.5 * escala);

        var yPunta = yArriba + altoTuerca;

        // ═════════════════════════════════════════════════════════════════════════════════════
        // LA LONGITUD TOTAL MANDA, Y EL AHOGO ES EL RESPALDO.
        //
        // «Longitud del ancla» es lo que se corta y se pide en el taller, doblez incluido. El
        // ahogo —E12 y E13 de la macro— es la consecuencia: lo que queda dentro del concreto
        // una vez descontado lo que el ancla gasta atravesando la placa y saliendo a la tuerca.
        //
        // Con las dos capturadas pueden contradecirse, y de las dos la que se puede verificar
        // en el taller es la longitud. Así que se dibuja con ella, y el ahogo se usa cuando
        // viene en cero: es exactamente lo que se dibujaba antes de que existiera esta columna.
        // ═════════════════════════════════════════════════════════════════════════════════════
        var gasto = espesorPlaca + altoTuerca;

        var largoRecto = largoTotal > 0
            ? largoTotal - Math.Max(0, doblez)
            : ahogo + gasto;

        // Un ancla más corta que lo que gasta atravesando la placa no baja al concreto. En lugar de
        // dibujarla al revés —la punta por encima de la placa— se le deja el mínimo que sí baja.
        if (largoRecto <= gasto)
        {
            largoRecto = gasto + (1.0 * escala);
        }

        var yFondo = yPunta - largoRecto;

        var pata = Math.Max(0, doblez);

        var vastago = pata > 0
            ? new[] { x, yPunta, x, yFondo, x + (sentidoDoblez * pata), yFondo }
            : new[] { x, yPunta, x, yFondo };

        return new AnclaDeCanto(
            Vastago: vastago,
            Tuerca: Caja(x - (anchoTuerca / 2), yArriba, x + (anchoTuerca / 2), yArriba + altoTuerca),
            Arandela: new[] { x - anchoTuerca, yArriba, x + anchoTuerca, yArriba },

            // EL TRAVESAÑO SOLO SI NO HAY DOBLEZ. Con pata, lo que ancla es la pata, y dibujar
            // además un travesaño pone en el plano un remate que la pieza no lleva.
            Remate: pata > 0
                ? null
                : new[] { x - (anchoTuerca / 2), yFondo, x + (anchoTuerca / 2), yFondo },

            Ahogo: yPlaca - yFondo);
    }

    /// <summary>
    /// Hasta dónde baja el dado: <b>lo que haga falta</b> para que el ancla quede dentro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La regla de la macro es «el ahogo más 5 cm, y al menos 20». Se conserva, pero ya no es la
    /// única: con la longitud total capturada, el ancla puede bajar más de lo que dice E12, y ahí la
    /// regla de la macro dibujaría la punta <b>asomando por debajo del dado</b>.
    /// </para>
    /// <para>
    /// Así que el dado baja lo que pida el ancla más honda, con los mismos 5 cm de holgura. Un dado
    /// más profundo de lo capturado es un dato que se puede discutir; un ancla fuera del concreto es
    /// un plano que no se puede construir.
    /// </para>
    /// </remarks>
    public static double ProfundidadDelDado(
        double ahogo, IEnumerable<AnclaDeCanto> anclas, double escala)
    {
        var pide = ahogo;

        foreach (var a in anclas)
        {
            if (a.Ahogo > pide)
            {
                pide = a.Ahogo;
            }
        }

        return Math.Max(pide + (5.0 * escala), 20.0 * escala);
    }

    /// <summary>Un rectángulo como polilínea cerrada, en <b>antihorario</b>.</summary>
    private static double[] Caja(double x1, double y1, double x2, double y2) =>
        new[] { x1, y1, x2, y1, x2, y2, x1, y2 };
}
