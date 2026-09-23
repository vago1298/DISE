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
    /// El aire que se deja <b>entre el detalle de una placa y el de la siguiente</b>, en cm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo pidió el usuario: <i>«que cuando dibuje dos o más haya separación a la derecha del otro
    /// detalle para que no se encimen»</i>. Y se encimaban de verdad: el paso entre placas se medía
    /// con la huella de la <b>planta</b> —la placa o el dado, el que sobresaliera— y el detalle
    /// sigue 60 cm a la derecha con uno o dos cortes, así que los cortes de una caían sobre la
    /// planta de la siguiente. El aire no arreglaba nada porque lo que faltaba era medir.
    /// </para>
    /// <para>
    /// Ahora el paso se mide con <b>lo que de verdad ocupó el detalle</b>, cortes incluidos, y esto
    /// es el aire que se suma a eso.
    /// </para>
    /// </remarks>
    public const double SeparacionEntreDetallesCm = 4.0;

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
    /// <param name="LongAnclaje">
    /// La <b>longitud vertical</b> del ancla: lo que se ahoga en el concreto, medido desde la cara
    /// de abajo de la placa. Celdas <b>E12</b> y <b>E13</b>. <b>Es la que gobierna hasta dónde baja
    /// la barra</b>, y con ella la profundidad del dado.
    /// </param>
    /// <param name="LongAncla">
    /// La longitud <b>total desarrollada</b> del ancla, doblez incluido. Ya no se captura: es el
    /// respaldo para un trabajo guardado que solo la traiga a ella, y solo se usa cuando la
    /// vertical viene en cero.
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
    /// El <b>EJE</b> de la barra, de la tuerca al fondo, abierto. Dos puntos si va recta y
    /// <b>tres</b> si lleva doblez: el tercero es la punta de la pata.
    /// <para>
    /// <b>Ya no se dibuja: se mide.</b> De él salen las cotas, el ahogo y la profundidad del
    /// concreto —que suma medio diámetro justo porque esto es el eje—. Lo que se dibuja es
    /// <see cref="Contorno"/>. Separar las dos cosas es lo que permitió pasar de la barra rellena
    /// al perfil vacío sin tocar una sola cota.
    /// </para>
    /// </param>
    /// <param name="Tuerca">El rectángulo sobre la placa.</param>
    /// <param name="Arandela">La línea que la apoya. Dos puntos.</param>
    /// <param name="Remate">
    /// El travesaño del fondo, <c>null</c> cuando el ancla lleva doblez: ahí lo que ancla es la
    /// pata, y un travesaño además de la pata dibuja un remate que no existe.
    /// </param>
    /// <param name="Ahogo">Cuánto baja de la cara de abajo de la placa, para que el dado la cubra.</param>
    /// <param name="Diametro">
    /// El <b>grueso real</b> de la barra, en unidades de dibujo, para que el vástago se dibuje con
    /// su espesor y no como una línea de eje. Sale del diámetro de la tabla —«3/4"»— convertido a
    /// centímetros en <c>PlacaBaseRow.AFormatoCad</c>.
    /// </param>
    /// <param name="Rosca">
    /// El <b>enroscado</b> que asoma por encima de la tuerca: sus dos flancos, el remate de la
    /// punta y las dos hebras del hilo. Poligonales <b>abiertas</b>, todas de la misma familia
    /// porque se dibujan igual. Vacío si no hay rosca que dibujar.
    /// </param>
    /// <param name="Contorno">
    /// El <b>perfil de la barra</b>, cerrado y vacío: las dos caras separadas un diámetro, con sus
    /// extremos y el codo del doblez <b>redondeado</b> con el radio de doblado. Es lo que se DIBUJA,
    /// y va con sus bulges: sin ellos el codo sale a escuadra.
    /// </param>
    /// <param name="AristasTuerca">
    /// Las dos aristas verticales de la tuerca: es lo que la hace leerse como una <b>tuerca
    /// hexagonal de frente</b> y no como una caja. Dos líneas de dos puntos.
    /// </param>
    public readonly record struct AnclaDeCanto(
        double[] Vastago, double[] Tuerca, double[] Arandela, double[]? Remate, double Ahogo,
        double Diametro, double[][] Rosca, double[][] AristasTuerca, Perfil Contorno)
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
        (double X, double Y) Rotulo,
        double[]? Grout);

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
    /// <param name="grout">
    /// El espesor de la cama de grout entre la placa y el dado, en unidades de dibujo. En
    /// <c>0</c> —la casilla de la hoja en NO— la placa se apoya directamente en el dado, que es
    /// como se dibujaba antes.
    /// </param>
    public static List<Vista> Construir(
        double xInicio, double yPlaca, double escala, double alturaTexto,
        double espesorPlaca, bool conCartabones, Direccion x, Direccion y, double grout = 0)
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
                           espesorPlaca, x, cartX, grout)
                : UnaVista("X-Y", xInicio + (ocupaY / 2), ocupaY, yPlaca, escala, alturaTexto,
                           espesorPlaca, y, cartY, grout);

            if (unica is not null)
            {
                vistas.Add(unica);
            }

            return vistas;
        }

        var vx = UnaVista("X", xInicio + (ocupaX / 2), ocupaX, yPlaca, escala, alturaTexto,
                          espesorPlaca, x, cartX, grout);

        if (vx is not null)
        {
            vistas.Add(vx);
        }

        var xVistaY = xInicio + ocupaX + (SeparacionEntreVistasCm * escala) + (ocupaY / 2);

        var vy = UnaVista("Y", xVistaY, ocupaY, yPlaca, escala, alturaTexto,
                          espesorPlaca, y, cartY, grout);

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
        double espesorPlaca, Direccion d, bool conCartabon, double grout)
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

        // ═════════════════════════════════════════════════════════════════════════════════════
        // LA CAMA DE GROUT, ENTRE LA PLACA Y EL DADO.
        //
        // Se resuelve BAJANDO EL CONCRETO y no subiendo la placa. La placa se queda en yPlaca
        // —que es el nivel de arranque que recibe esta vista, el mismo que usa la planta— y el
        // dado empieza un espesor de grout más abajo. Al revés habría que mover también la
        // columna, los cartabones, la tuerca y la arandela, que cuelgan todos de yArriba, y el
        // nivel de arranque del detalle dejaría de ser el de la planta.
        //
        // Y el ancla ATRAVIESA el grout: su longitud vertical se mide desde la cara de arriba
        // del CONCRETO, que es donde el ancla ancla de verdad. Por eso el grout se le pasa a
        // AnclasDeCanto: lo suma a lo que la barra gasta antes de morder el dado.
        // ═════════════════════════════════════════════════════════════════════════════════════
        var g = Math.Max(0, grout);

        var yDado = yPlaca - g;

        // LAS ANCLAS PRIMERO, porque ahora gobiernan la profundidad del dado. Ver la nota de
        // ProfundidadDelDado: con la longitud total capturada, el ahogo de la hoja puede quedarse
        // corto, y un ancla dibujada asomando por debajo del concreto es un plano que no se puede
        // construir.
        var anclas = AnclasDeCanto(
            xCentro, yPlaca, yArriba, d.AnchoPlaca, d.SepBorde,
            d.LongAnclaje, d.LongAncla, d.DoblezAncla, esp, g, d.DiamAncla, d.CuantasAnclas,
            escala);

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
            Concreto: Caja(xCentro - (anchoConcreto / 2), yDado - profundidad,
                           xCentro + (anchoConcreto / 2), yDado),
            Placa: Caja(xCentro - (d.AnchoPlaca / 2), yPlaca,
                        xCentro + (d.AnchoPlaca / 2), yArriba),
            Columna: Caja(xCentro - (anchoPerfil / 2), yArriba,
                          xCentro + (anchoPerfil / 2), yArriba + alturaColumna),
            Cartabones: cartabones.ToArray(),
            Anclas: anclas.ToArray(),

            // EL ROTULO BAJA A CINCO ALTURAS DE TEXTO. Eran dos, y ahí ya no cabe: debajo del
            // dado va ahora la cota del doblez del ancla, y el rótulo se le montaba encima.
            Rotulo: (xCentro, yDado - profundidad - (5.0 * alturaTexto)),

            // La cama de grout, del ancho del dado: se cuela sobre él y es la cara sobre la que
            // se nivela la placa. Sin grout no hay franja que dibujar, y va en null y no en una
            // caja de altura cero, que en el dibujo serían dos líneas encimadas.
            Grout: g > 0
                ? Caja(xCentro - (anchoConcreto / 2), yDado,
                       xCentro + (anchoConcreto / 2), yPlaca)
                : null);
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
    /// <param name="ahogo">
    /// Celdas E12 y E13: la <b>longitud vertical</b> del ancla, la que manda hasta dónde baja.
    /// </param>
    /// <param name="largoTotal">
    /// La longitud desarrollada del ancla, doblez incluido. Respaldo de los trabajos viejos.
    /// </param>
    /// <param name="doblez">La pata del extremo. Cero = ancla recta.</param>
    /// <param name="espesorPlaca">Para descontar lo que el ancla gasta por encima del concreto.</param>
    /// <param name="grout">
    /// La cama de grout que el ancla atraviesa antes de morder el dado. Se descuenta igual que el
    /// espesor de la placa: la longitud vertical se mide dentro del CONCRETO.
    /// </param>
    public static List<AnclaDeCanto> AnclasDeCanto(
        double xCentro, double yPlaca, double yArriba, double anchoPlaca, double sepBorde,
        double ahogo, double largoTotal, double doblez, double espesorPlaca, double grout,
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
            // La única del centro dobla hacia la derecha: no hay un «hacia dentro» que respetar, ni
            // pareja con la que encimarse, así que tampoco lleva desfase.
            salida.Add(UnAncla(xCentro, yPlaca, yArriba, ahogo, largoTotal, doblez,
                               espesorPlaca, grout, diametro, 0, 1, escala));

            return salida;
        }

        // Y SI LAS DOS PATAS SE ALCANZAN, UNA SE SUBE. Ver DesfaseDeLasPatas.
        var desfase = DesfaseDeLasPatas(
            Math.Max(0, doblez), 2 * desplazamiento, diametro, escala);

        // LAS PATAS APUNTAN HACIA DENTRO, una contra la otra. Es lo que da recubrimiento: las dos
        // anclas van cerca de los cantos de la placa, así que una pata hacia fuera se acerca a la
        // cara del dado y se queda sin concreto que la sujete. Hacia dentro, el doblez muerde el
        // núcleo confinado, y además no puede salirse del dado por mucho que se alargue.
        salida.Add(UnAncla(xCentro - desplazamiento, yPlaca, yArriba, ahogo, largoTotal, doblez,
                           espesorPlaca, grout, diametro, 0, 1, escala));

        salida.Add(UnAncla(xCentro + desplazamiento, yPlaca, yArriba, ahogo, largoTotal, doblez,
                           espesorPlaca, grout, diametro, desfase, -1, escala));

        return salida;
    }

    /// <summary>
    /// Cuánto se <b>sube</b> una de las dos anclas cuando sus patas se encimarían.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos patas doblan hacia dentro, una contra la otra y <b>a la misma altura</b>. Con un
    /// doblez largo —o una placa angosta— la punta de una llega al eje de la otra, y entonces en el
    /// plano las dos se dibujan encimadas: se ve una sola barra en L y no se entiende el detalle.
    /// Ahora que el vástago va con su grueso real, encimadas se leen como una sola pieza maciza.
    /// </para>
    /// <para>
    /// Así que una se sube. Es el mismo criterio que <c>ZapataDrawer.DesfaseDeLosGanchos</c> usa con
    /// los ganchos de arranque del dado, y es también lo que se hace en obra: los dobleces se
    /// alternan para que quepan. La cuenta es la misma —la <b>suma de las dos patas</b> contra el
    /// hueco libre, menos una tolerancia— y el desfase también: <b>dos diámetros</b> más la
    /// tolerancia, que es lo que hace falta para que entre las dos barras se vea concreto.
    /// </para>
    /// <para>
    /// Un ancla recta no lleva pata, así que no puede encimarse con nada y sale en cero.
    /// </para>
    /// </remarks>
    /// <param name="pata">La pata del doblez, en horizontal.</param>
    /// <param name="separacionEjes">De eje a eje de las dos anclas.</param>
    /// <param name="diametro">El grueso de la barra: es la medida del desfase.</param>
    public static double DesfaseDeLasPatas(
        double pata, double separacionEjes, double diametro, double escala)
    {
        if (pata <= 0 || separacionEjes <= 0)
        {
            return 0;
        }

        var tolerancia = 0.5 * escala;
        var d = diametro > 0 ? diametro : 1.0 * escala;

        return (2 * pata) > separacionEjes - tolerancia
            ? (2 * d) + tolerancia
            : 0;
    }

    private static AnclaDeCanto UnAncla(
        double x, double yPlaca, double yArriba, double ahogo, double largoTotal, double doblez,
        double espesorPlaca, double grout, double diametro, double desfase, int sentidoDoblez,
        double escala)
    {
        var d = diametro > 0 ? diametro : 1.0 * escala;

        // Los mínimos son de la macro, y no son estéticos: una tuerca de menos de 1.5 cm y una
        // arandela de menos de 0.5 cm no se distinguen del vástago al plotear a 1:10.
        var anchoTuerca = Math.Max(2.5 * d, 1.5 * escala);
        var altoTuerca = Math.Max(0.75 * d, 0.5 * escala);

        var yPunta = yArriba + altoTuerca;

        // ═════════════════════════════════════════════════════════════════════════════════════
        // LA LONGITUD VERTICAL MANDA, Y EL LARGO TOTAL ES EL RESPALDO.
        //
        // La casilla de la hoja es «Longitud de ancla X vertical»: lo que el ancla se ahoga en el
        // concreto, medido desde la cara de abajo de la placa —E12 y E13 de la macro—. Es la que
        // se captura, así que es la que TIENE que gobernar hasta dónde baja la barra: el fondo
        // queda exactamente a esa distancia de la placa, y el dado baja detrás de él.
        //
        // Era al revés. Mandaba el largo TOTAL desarrollado, y entonces el ahogo capturado a mano
        // acababa siendo un dato que el dibujo recalculaba solo: se escribía 45 y el ancla bajaba
        // otra cosa. Esa casilla ya no está en la hoja —una sola verdad para el largo del ancla—,
        // y aquí el total se queda de respaldo para un trabajo guardado que solo lo traiga a él.
        //
        // El GASTO es lo que el ancla consume por encima del concreto: el espesor de la placa que
        // atraviesa, la cama de grout si la hay, y lo que asoma para la tuerca. Se suma a la
        // vertical porque el vástago se dibuja desde la punta de arriba, no desde la cara de la
        // placa. Con grout, entonces, la barra se alarga lo que mide la cama y sigue ahogándose
        // en el dado la longitud que dice la hoja: es el grout el que no cuenta como anclaje.
        // ═════════════════════════════════════════════════════════════════════════════════════
        var gasto = espesorPlaca + Math.Max(0, grout) + altoTuerca;

        var largoRecto = ahogo > 0
            ? ahogo + gasto
            : largoTotal - Math.Max(0, doblez);

        // Un ancla más corta que lo que gasta atravesando la placa no baja al concreto. En lugar de
        // dibujarla al revés —la punta por encima de la placa— se le deja el mínimo que sí baja.
        if (largoRecto <= gasto)
        {
            largoRecto = gasto + (1.0 * escala);
        }

        // EL DESFASE SUBE ESTA ANCLA, pero nunca tanto que su fondo se meta en la placa: se topa
        // en un centímetro de barra dentro del concreto. Sin el tope, un doblez enorme en una placa
        // angosta subiría el ancla por encima del dado y el desfase crearía otro problema.
        var sube = Math.Max(0, Math.Min(desfase, largoRecto - gasto - (1.0 * escala)));

        var yFondo = yPunta - largoRecto + sube;

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

            // El ahogo se devuelve MEDIDO —y desde la cara de arriba del CONCRETO, no de la
            // placa—, no copiado del dato: si esta ancla se subió por el desfase, el suyo es
            // menor, y así el dado lo calcula la más honda de las dos.
            Ahogo: yPlaca - Math.Max(0, grout) - yFondo,

            // Y el grueso real de la barra viaja con ella: lo pintan el dibujante y la previa.
            Diametro: d,

            // ═════════════════════════════════════════════════════════════════════════════════
            // EL ENROSCADO Y LAS ARISTAS DE LA TUERCA.
            //
            // Los pidió el usuario: «necesito que las anclas las dibuje con su enroscado al inicio
            // y con su tuerca, la tuerca deberá ser adecuada para el tamaño del ancla». La tuerca
            // ya salía a la medida de la barra —2.5 diámetros de ancho por 0.75 de alto—, pero era
            // un rectángulo pelado y el vástago acababa a ras de ella, así que el detalle no
            // enseñaba qué parte del ancla va roscada.
            //
            // LA ROSCA VA POR ENCIMA DE LA TUERCA y no cambia nada de lo de abajo: ni el gasto, ni
            // el fondo, ni el ahogo. Es lo que asoma del perno una vez apretada la tuerca, que es
            // lo que se dibuja en un detalle y lo que se pide en obra. Si entrara en el gasto, el
            // ancla se alargaría por dibujar su rosca, y eso sería el dibujo cambiando el dato.
            // ═════════════════════════════════════════════════════════════════════════════════
            Rosca: Roscar(x, yPunta, d, escala),

            AristasTuerca: AristasDeLaTuerca(
                x, yArriba, yArriba + altoTuerca, anchoTuerca),

            // ═════════════════════════════════════════════════════════════════════════════════
            // EL PERFIL DE LA BARRA, VACÍO Y CON SUS DOS CARAS.
            //
            // Lo pidió el usuario: «las anclas no las hagas con PEDIT, déjalas vacías pero con 2
            // líneas representando su grosor». El grueso se dibujaba con el ANCHO de la polilínea
            // —lo que en AutoCAD se toca con PEDIT—, y eso sale como una barra MACIZA: al plotear
            // es una mancha negra, y encima no se puede rayar ni rotular por dentro.
            //
            // Ahora es el contorno de la pieza: las dos caras a medio diámetro del eje, con los
            // extremos cerrados y el codo del doblez resuelto. Es como se dibuja una barra en un
            // plano de armado, y deja el ancla como UNA entidad que se selecciona entera.
            // ═════════════════════════════════════════════════════════════════════════════════
            Contorno: ContornoDeLaBarra(x, yPunta, yFondo, pata, sentidoDoblez, d));
    }

    /// <summary>
    /// El <b>perfil</b> de la barra: sus dos caras a medio diámetro del eje, cerrado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recta es un rectángulo largo. Con doblez es una <b>L de seis vértices</b>: se baja por la
    /// cara de fuera del codo, se cruza por debajo hasta la punta de la pata, se cierra la punta y
    /// se vuelve por la cara de dentro. Así el codo queda a escuadra por los dos lados, que es como
    /// se dibuja un doblez de 90° en un plano de armado.
    /// </para>
    /// <para>
    /// Las caras se nombran por el codo y no por la mano: la de <b>fuera</b> es la del lado opuesto
    /// al que dobla la pata, y es la que pasa por debajo del eje. Con <c>sentidoDoblez</c> al otro
    /// lado, las dos se cambian el papel, y por eso el signo entra en la cuenta en lugar de haber
    /// dos ramas iguales y espejadas.
    /// </para>
    /// </remarks>
    /// <param name="pata">Lo que mide la pata del doblez. Cero o menos: la barra va recta.</param>
    /// <param name="sentidoDoblez"><c>+1</c> la pata va a la derecha, <c>-1</c> a la izquierda.</param>
    public static Perfil ContornoDeLaBarra(
        double x, double yPunta, double yFondo, double pata, int sentidoDoblez, double diametro)
    {
        var r = diametro / 2;
        var s = sentidoDoblez >= 0 ? 1 : -1;

        if (pata <= 0)
        {
            return new Perfil(
                new[]
                {
                    x - r, yPunta,
                    x + r, yPunta,
                    x + r, yFondo,
                    x - r, yFondo,
                },
                Array.Empty<(int, double)>());
        }

        // La cara de FUERA del codo —la del lado contrario al doblez— es la que pasa por debajo del
        // eje, a medio diámetro; la de DENTRO pasa por encima del fondo del eje.
        var xFuera = x - (s * r);
        var xDentro = x + (s * r);
        var xPunta = x + (s * (pata + r));

        var rc = RadioDelDoblez(pata, Math.Abs(yPunta - yFondo), diametro);

        if (rc <= 0)
        {
            // Sin sitio para el arco, el codo se queda a escuadra. Es lo que hace AutoCAD cuando el
            // radio del FILLET no cabe: no lo redondea a medias.
            return new Perfil(
                new[]
                {
                    xFuera, yPunta,
                    xFuera, yFondo - r,
                    xPunta, yFondo - r,
                    xPunta, yFondo + r,
                    xDentro, yFondo + r,
                    xDentro, yPunta,
                },
                Array.Empty<(int, double)>());
        }

        // ═════════════════════════════════════════════════════════════════════════════════════
        // EL CODO, REDONDEADO. Los dos arcos son CONCÉNTRICOS: el centro está a rc del eje en las
        // dos direcciones, y de ahí salen el radio de fuera —rc + d/2— y el de dentro —rc - d/2—.
        //
        // Concéntricos y no con el mismo radio cada uno, que es lo que haría un FILLET aplicado por
        // separado a cada cara: con radios iguales y centros distintos, el codo engorda un 41 % en
        // la diagonal. Una barra doblada tiene el MISMO grueso en todo su desarrollo.
        // ═════════════════════════════════════════════════════════════════════════════════════
        var yCentro = yFondo + rc;
        var xCentro = x + (s * rc);

        // El arco de fuera gira 90° en el sentido del doblez; el de dentro, al contrario. El signo
        // del bulge es el del giro: positivo antihorario.
        var bulge = BulgeDeCuartoDeVuelta * s;

        return new Perfil(
            new[]
            {
                xFuera, yPunta,
                xFuera, yCentro,            // tangencia del arco de fuera, en el tramo vertical
                xCentro, yFondo - r,        // y en el tramo de la pata
                xPunta, yFondo - r,
                xPunta, yFondo + r,
                xCentro, yFondo + r,        // tangencia del arco de dentro, en la pata
                xDentro, yCentro,           // y en el tramo vertical
                xDentro, yPunta,
            },
            new[]
            {
                // El bulge va en el vértice donde ARRANCA el arco, que es como lo guarda AutoCAD.
                (1, bulge),
                (5, -bulge),
            });
    }

    /// <summary>
    /// El radio del doblez <b>medido al eje</b> de la barra, recortado a lo que cabe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo pidió el usuario: <i>«en la esquina del ancla dale fillete de 5·2.54·diámetro de ancla»</i>.
    /// Ese <c>5 · 2.54 · Ø"</c> son <b>cinco diámetros</b> —2.54 convierte la pulgada a centímetros—,
    /// que es el radio de doblado con el que se dobla un ancla: las normas piden entre tres y seis.
    /// Se toma como radio <b>interior</b>, así que al eje le toca medio diámetro más.
    /// </para>
    /// <para>
    /// <b>Y se recorta a lo que cabe</b>, porque muchas veces no cabe: cinco diámetros de un ancla de
    /// 3/4" son 9.5 cm, y con una pata de 10 cm el arco se comería la pata entera. Sin recorte, el
    /// contorno se cruzaría consigo mismo y el ancla saldría como un nudo. Si no queda ni un cuarto
    /// del radio pedido, se devuelve <c>0</c> y el codo se dibuja a escuadra: es lo que hace AutoCAD
    /// cuando el FILLET no cabe, y es más honesto que un redondeo inventado.
    /// </para>
    /// </remarks>
    public static double RadioDelDoblez(double pata, double tramoRecto, double diametro)
    {
        var d = diametro > 0 ? diametro : 0;

        if (pata <= 0 || d <= 0)
        {
            return 0;
        }

        // Al eje: el interior que se pide más medio diámetro.
        var pedido = (RadioDeDoblezEnDiametros * d) + (d / 2);

        // Lo que cabe: el arco no puede comerse la pata entera ni subir más que el tramo recto. Se
        // deja un diámetro de barra recta a cada lado para que el doblez se vea como un doblez.
        var cabe = Math.Min(pata - d, tramoRecto - d);

        var rc = Math.Min(pedido, cabe);

        return rc >= pedido / 4 ? rc : 0;
    }

    // ======================================================================
    //  EL ENROSCADO DEL ANCLA
    // ======================================================================
    //
    //  Las proporciones van con nombre y en DIÁMETROS, como el resto de la pieza: una rosca
    //  medida en centímetros se vería bien en un ancla de 3/4" y ridícula en una de 2".

    /// <summary>Lo que asoma la rosca por encima de la tuerca, en diámetros.</summary>
    /// <remarks>
    /// Dos diámetros y medio: lo que un perno asoma de su tuerca cuando está bien apretado, con
    /// hilos de sobra para verse. Más corto no se lee como rosca, y más largo parece un ancla que
    /// se quedó sin apretar.
    /// </remarks>
    private const double RoscaEnDiametros = 2.5;

    /// <summary>Y el mínimo absoluto, en cm, para que se vea al plotear a 1:10.</summary>
    /// <remarks>Es el mismo criterio que los mínimos de la tuerca y la arandela.</remarks>
    private const double RoscaMinimaCm = 1.5;

    /// <summary>El paso del hilo, en diámetros.</summary>
    /// <remarks>
    /// <para>
    /// <b>Es el paso de verdad, redondeado.</b> En la serie gruesa —UNC, la de las anclas— el paso
    /// vale entre <c>0.11</c> y <c>0.17</c> diámetros: un 3/8"-16 son 0.167, un 1/2"-13 son 0.154,
    /// un 3/4"-10 son 0.133 y un 2"-4½ son 0.111. Un solo número para todos los diámetros es
    /// entonces una aproximación buena, y 0.15 cae dentro del rango en los diámetros que se usan a
    /// diario.
    /// </para>
    /// <para>
    /// Estuvo en <c>0.5</c> —tres veces más abierto que la rosca real— porque se eligió pensando en
    /// el papel: a 1:10 estos hilos quedan a un décimo de milímetro y se empastan. Y el usuario lo
    /// rechazó a la primera: <i>«no se parece en lo enroscado a la figura 2»</i>. Tenía razón, y su
    /// figura era la de un perno de verdad. Con 0.15 salen <b>diecisiete hilos</b> en la parte que
    /// asoma, que es lo que se ve en su croquis.
    /// </para>
    /// <para>
    /// Lo que se pierde es nitidez al plotear muy reducido; lo que se gana es que el detalle enseñe
    /// un tornillo y no un fuelle. Si algún día hace falta lo otro, este es el número.
    /// </para>
    /// </remarks>
    private const double PasoEnDiametros = 0.15;

    /// <summary>El radio del doblez del ancla, en diámetros y medido al <b>interior</b>.</summary>
    /// <remarks>
    /// <para>
    /// <b>UN diámetro.</b> Se pidió primero como <c>5 · 2.54 · Ø"</c> —cinco diámetros, porque el
    /// 2.54 solo pasa la pulgada a centímetros— y después <i>«quita el 5, solo deja puro
    /// diámetro»</i>. Así que el radio interior del codo es el diámetro de la barra, y al eje le
    /// toca medio diámetro más.
    /// </para>
    /// <para>
    /// Con cinco el codo se comía la pata: en un ancla de 3/4" el radio al eje salía de 10.5 cm y
    /// con una pata de 10 cm no quedaba recta ninguna. Con uno, el radio al eje va de 1.4 cm en un
    /// 3/8" a 7.6 cm en uno de 2", y cabe en cualquier pata de las que se capturan.
    /// </para>
    /// </remarks>
    private const double RadioDeDoblezEnDiametros = 1.0;

    /// <summary>El bulge de un arco de <b>90°</b>: <c>tan(90/4)</c>.</summary>
    /// <remarks>
    /// El bulge de una polilínea es la tangente de un cuarto del ángulo que barre el arco. Para el
    /// codo de 90° del doblez sale <c>tan(22.5°)</c>, y se escribe con su valor y no con una llamada
    /// a <c>Math.Tan</c> para que se pueda comparar de un vistazo con el número que guarda AutoCAD.
    /// </remarks>
    private const double BulgeDeCuartoDeVuelta = 0.41421356237309503;

    /// <summary>Un perfil cerrado con sus arcos: los puntos y el bulge de los que lo llevan.</summary>
    /// <remarks>
    /// Va junto porque no se puede dibujar lo uno sin lo otro: los mismos puntos con los bulges
    /// perdidos dan un codo a escuadra, y es un fallo que no se ve hasta que el plano está impreso.
    /// </remarks>
    public readonly record struct Perfil(double[] Puntos, (int Indice, double Bulge)[] Dobleces);

    /// <summary>Cuántos dientes se dibujan como máximo.</summary>
    /// <remarks>
    /// Tope de seguridad, no un criterio de dibujo: con un diámetro minúsculo y una rosca larga, el
    /// zigzag saldría con miles de vértices y el detalle pesaría más que el plano entero.
    /// </remarks>
    private const int MaxDientes = 60;

    /// <summary>
    /// El enroscado que asoma sobre la tuerca: dos flancos, el remate de la punta y dos hebras.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dos hebras desfasadas medio paso.</b> Una sola da un zigzag, que se lee como un fuelle;
    /// dos cruzadas es como se ve un hilo de verdad —la parte de delante subiendo y la de detrás
    /// bajando— y es lo que enseñó el usuario en su croquis.
    /// </para>
    /// <para>
    /// El vástago macizo <b>acaba</b> en la cara de arriba de la tuerca, así que aquí se dibujan
    /// también los dos flancos: sin ellos la barra se cortaría de golpe y la rosca flotaría encima.
    /// </para>
    /// </remarks>
    /// <param name="yBase">La cara de arriba de la tuerca, donde arranca lo que asoma.</param>
    public static double[][] Roscar(double x, double yBase, double diametro, double escala)
    {
        var d = diametro > 0 ? diametro : 1.0 * escala;

        var largo = Math.Max(RoscaEnDiametros * d, RoscaMinimaCm * escala);
        var paso = Math.Max(PasoEnDiametros * d, 1e-9);

        var dientes = (int)Math.Ceiling(largo / paso);

        if (dientes < 2) { dientes = 2; }
        if (dientes > MaxDientes) { dientes = MaxDientes; }

        // El paso se reparte otra vez con los dientes que caben: así el último hilo acaba EN la
        // punta y no a media altura, que es lo que delata un zigzag dibujado a ojo.
        var h = largo / dientes;

        var izq = x - (d / 2);
        var der = x + (d / 2);
        var yPunta = yBase + largo;

        // ═════════════════════════════════════════════════════════════════════════════════════
        // LAS DOS HEBRAS VAN DESFASADAS MEDIO DIENTE, NO ESPEJADAS.
        //
        // Es la corrección que pidió el usuario mirando los vértices: «checa los puntos en donde
        // salen». Estaban ESPEJADAS —cuando una picaba en el flanco izquierdo, la otra picaba en el
        // derecho A LA MISMA ALTURA—, y eso dibuja una cadena de equis simétricas con todos los
        // vértices alineados por parejas. No es una rosca: es una celosía.
        //
        // En un hilo de verdad los picos de un flanco caen ENTRE los del otro, porque la hebra de
        // detrás va media vuelta retrasada, y media vuelta de hélice es medio paso de altura. Así
        // que la hebra B arranca medio diente más arriba, y de ahí salen las dos cosas que se ven
        // en el croquis: los picos intercalados y los cruces con pendientes distintas.
        //
        // B se queda medio diente corta por arriba, y eso es correcto: la punta la cierra el remate,
        // y un hilo que llegara justo al canto se leería como un corte a ras.
        // ═════════════════════════════════════════════════════════════════════════════════════
        var hebraA = new List<double>();
        var hebraB = new List<double>();

        for (var i = 0; i <= dientes; i++)
        {
            var parIzquierda = i % 2 == 0;

            hebraA.Add(parIzquierda ? izq : der);
            hebraA.Add(yBase + (i * h));

            // El último de A cae en la punta; B, que va medio diente por delante, se para antes.
            if (i < dientes)
            {
                hebraB.Add(parIzquierda ? der : izq);
                hebraB.Add(yBase + (i * h) + (h / 2));
            }
        }

        return new[]
        {
            new[] { izq, yBase, izq, yPunta },
            new[] { der, yBase, der, yPunta },
            new[] { izq, yPunta, der, yPunta },
            hebraA.ToArray(),
            hebraB.ToArray(),
        };
    }

    /// <summary>
    /// Las dos aristas verticales de la tuerca, para que se lea como <b>hexagonal</b>.
    /// </summary>
    /// <remarks>
    /// Una tuerca hexagonal de frente enseña tres caras, y lo que las separa son dos aristas a un
    /// cuarto y a tres cuartos del ancho. Sin ellas el dibujo es una caja, que es lo que había, y
    /// una caja sobre la placa se puede leer como una silleta o un dado de nivelación.
    /// </remarks>
    public static double[][] AristasDeLaTuerca(
        double x, double yAbajo, double yArriba, double anchoTuerca)
    {
        var cuarto = anchoTuerca / 4;

        return new[]
        {
            new[] { x - cuarto, yAbajo, x - cuarto, yArriba },
            new[] { x + cuarto, yAbajo, x + cuarto, yArriba },
        };
    }

    /// <summary>
    /// Hasta dónde baja el dado: <b>lo que haga falta</b> para que el ancla quede dentro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La regla de la macro es «el ahogo más 5 cm, y al menos 20». Se conserva, pero ya no es la
    /// única: se mide además el ancla ya dibujada, porque puede bajar más de lo que dice E12 —un
    /// trabajo viejo que solo trae el largo total, o el mínimo que se le pone a un ancla más corta
    /// que la propia placa—, y ahí la regla de la macro dibujaría la punta <b>asomando por debajo
    /// del dado</b>.
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
            // MEDIO DIÁMETRO MÁS. El vástago se dibuja con su grueso real y sus puntos son el
            // EJE de la barra, así que la cara de abajo queda medio diámetro por debajo del
            // fondo. Con el ancla de 4" del cuadro eso son 5.08 cm —más que la holgura del
            // dado—, o sea que sin sumarlo la barra más gruesa asomaría por debajo del concreto.
            var suyo = a.Ahogo + (a.Diametro / 2);

            if (suyo > pide)
            {
                pide = suyo;
            }
        }

        return Math.Max(pide + (5.0 * escala), 20.0 * escala);
    }

    /// <summary>Un rectángulo como polilínea cerrada, en <b>antihorario</b>.</summary>
    /// <summary>Un rectángulo cerrado, en antihorario.</summary>
    /// <remarks>
    /// Público porque lo usa también el detalle del ancla suelta —<see cref="DetalleDeAncla"/>—, que
    /// dibuja la MISMA tuerca que el corte. Con una copia allí, el día que la tuerca cambie de
    /// proporción el plano enseñaría dos tuercas distintas de la misma pieza.
    /// </remarks>
    public static double[] Caja(double x1, double y1, double x2, double y2) =>
        new[] { x1, y1, x2, y1, x2, y2, x1, y2 };
}
