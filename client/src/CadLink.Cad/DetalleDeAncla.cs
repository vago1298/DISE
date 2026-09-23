namespace CadLink.Cad;

/// <summary>
/// El detalle de <b>una ancla sola</b>, acotada: el que va al final de los cortes.
/// </summary>
/// <remarks>
/// <para>
/// Lo pidió el usuario: <i>«hazme aparte un detalle de la pura ancla, así como la imagen, pero con
/// los datos correspondientes; ese detalle ponlo al final de los cortes»</i>.
/// </para>
/// <para>
/// Y hace falta por algo concreto: en el corte de la placa el ancla sale <b>enterrada</b> —el
/// concreto rayado por detrás, la placa cruzándola, la tuerca pegada a otras piezas— así que ni se
/// lee el doblez ni se puede acotar sin amontonar cotas sobre las del dado. Suelta y a un lado, la
/// misma barra se deja acotar entera: diámetro, longitud vertical, pata, rosca y largo desarrollado.
/// </para>
/// <para>
/// <b>Es la MISMA geometría del corte</b>, no un dibujo nuevo: la barra sale de
/// <see cref="ElevacionPlacaBase.ContornoDeLaBarra"/>, la rosca de <see cref="ElevacionPlacaBase"/>
/// y la tuerca de su misma cuenta. Si el ancla cambia en el corte, cambia aquí, y es lo que evita
/// el detalle que enseña una pieza que el plano no dibuja.
/// </para>
/// <para>
/// Geometría pura, sin COM, como <see cref="ElevacionPlacaBase"/> y <see cref="AnclasPlacaBase"/>:
/// se puede comprobar sin AutoCAD delante.
/// </para>
/// </remarks>
public static class DetalleDeAncla
{
    /// <summary>A cuánto del último corte arranca el detalle, en cm.</summary>
    /// <remarks>
    /// El mismo aire que hay entre la planta y el primer corte
    /// —<see cref="ElevacionPlacaBase.SeparacionDeLaPlantaCm"/>—: el detalle se lee como una vista
    /// más de la misma fila, y las vistas de una fila se separan igual.
    /// </remarks>
    public const double SeparacionDelUltimoCorteCm = 60.0;

    /// <summary>Lo que se aparta la cota del canto de la pieza, en alturas de texto.</summary>
    private const double CotaDesdeLaPieza = 3.0;

    /// <summary>Y la segunda cota, más afuera, para que no se monte con la primera.</summary>
    private const double SegundaCota = 6.0;

    /// <summary>
    /// Una cota del detalle: de dónde a dónde, y por dónde pasa su línea.
    /// </summary>
    /// <param name="Vertical">
    /// <c>true</c> la cota mide en vertical y su línea va a <see cref="Ref"/> en X;
    /// <c>false</c> mide en horizontal y su línea va a <see cref="Ref"/> en Y.
    /// </param>
    /// <param name="Desde">El primer punto medido, en el eje que mide.</param>
    /// <param name="Hasta">El segundo.</param>
    /// <param name="Origen">La coordenada del otro eje donde nacen las líneas de extensión.</param>
    /// <param name="Ref">Por dónde pasa la línea de cota.</param>
    public readonly record struct Cota(
        bool Vertical, double Desde, double Hasta, double Origen, double Ref);

    /// <summary>El detalle completo, en coordenadas de dibujo.</summary>
    /// <param name="Barra">El perfil de la barra, cerrado y vacío, con el codo redondeado.</param>
    /// <param name="Tuerca">Su rectángulo.</param>
    /// <param name="AristasTuerca">Las dos aristas del hexágono.</param>
    /// <param name="Rosca">Los flancos, la punta y las dos hebras del hilo.</param>
    /// <param name="Cotas">Lo que se acota: diámetro, vertical, pata y rosca.</param>
    /// <param name="Rotulo">Dónde arranca el rótulo, bajo la pieza.</param>
    /// <param name="Renglones">Los renglones del rótulo, de arriba abajo.</param>
    /// <param name="Ancho">Lo que ocupa a lo ancho, cotas incluidas, para el reparto.</param>
    public sealed record Detalle(
        ElevacionPlacaBase.Perfil Barra,
        double[] Tuerca,
        double[][] AristasTuerca,
        double[][] Rosca,
        List<Cota> Cotas,
        (double X, double Y) Rotulo,
        List<string> Renglones,
        double Ancho);

    /// <summary>
    /// Arma el detalle de un ancla, <b>ya acotado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La barra se dibuja <b>recta y de pie</b>, con su pata si la lleva: es la posición en la que
    /// se fabrica y en la que se pide, y la que deja las cuatro cotas sin cruzarse.
    /// </para>
    /// <para>
    /// <b>Las cotas salen de la geometría, no de los datos.</b> La vertical se mide sobre el eje
    /// dibujado y no se copia de la celda, así que si el desfase subió esta ancla o el grout la
    /// alargó, la cota lo dice. Es la diferencia entre un detalle y una ficha.
    /// </para>
    /// </remarks>
    /// <param name="x">Dónde va el eje de la barra.</param>
    /// <param name="yBaseTuerca">La cara de abajo de la tuerca: el nivel de arranque.</param>
    /// <param name="ahogo">La longitud vertical capturada, en unidades de dibujo.</param>
    /// <param name="doblez">La pata. Cero: la barra va recta.</param>
    /// <param name="diametro">El grueso de la barra, en unidades de dibujo.</param>
    /// <param name="textoDiametro">El diámetro tal como se capturó —<c>3/8"</c>—, para el rótulo.</param>
    /// <param name="marca">La marca de la placa, para saber de qué detalle es esta ancla.</param>
    public static Detalle? Construir(
        double x, double yBaseTuerca, double ahogo, double doblez, double diametro,
        string textoDiametro, string marca, double escala, double alturaTexto)
    {
        if (escala <= 0 || ahogo <= 0)
        {
            return null;
        }

        var d = diametro > 0 ? diametro : 1.0 * escala;

        // La tuerca, con la misma cuenta que en el corte: 2.5 diámetros de ancho por 0.75 de alto,
        // con los mínimos para que se vea al plotear.
        var anchoTuerca = Math.Max(2.5 * d, 1.5 * escala);
        var altoTuerca = Math.Max(0.75 * d, 0.5 * escala);

        var yPunta = yBaseTuerca + altoTuerca;
        var yFondo = yBaseTuerca - ahogo;

        var pata = Math.Max(0, doblez);

        var barra = ElevacionPlacaBase.ContornoDeLaBarra(x, yPunta, yFondo, pata, 1, d);
        var rosca = ElevacionPlacaBase.Roscar(x, yPunta, d, escala);

        var yPuntaRosca = rosca[0][3];

        // ---------- Las cotas ----------
        var o1 = CotaDesdeLaPieza * alturaTexto;
        var o2 = SegundaCota * alturaTexto;

        var xIzq = Math.Min(x - (anchoTuerca / 2), x - (d / 2));
        var xDer = Math.Max(x + (anchoTuerca / 2), x + (d / 2));

        if (pata > 0)
        {
            xDer = Math.Max(xDer, x + pata + (d / 2));
        }

        var cotas = new List<Cota>
        {
            // La longitud vertical: de la cara de abajo de la tuerca al fondo de la barra. Es la
            // que se captura, y va a la izquierda porque es la que se busca primero.
            new(Vertical: true, Desde: yFondo, Hasta: yBaseTuerca, Origen: xIzq, Ref: xIzq - o2),

            // Lo que asoma roscado, en la misma vertical pero más adentro: se leen en cadena.
            new(Vertical: true, Desde: yPunta, Hasta: yPuntaRosca, Origen: xIzq, Ref: xIzq - o1),

            // Y el diámetro, en horizontal sobre la punta: es la medida que identifica la barra.
            new(Vertical: false, Desde: x - (d / 2), Hasta: x + (d / 2), Origen: yPuntaRosca,
                Ref: yPuntaRosca + o1),
        };

        if (pata > 0)
        {
            // La pata, abajo y medida sobre su propio eje: es la cota que en el corte de la placa no
            // cabe, porque ahí la pata queda dentro del concreto rayado.
            cotas.Add(new Cota(
                Vertical: false, Desde: x, Hasta: x + pata, Origen: yFondo, Ref: yFondo - o1));
        }

        // ---------- El rótulo ----------
        var renglones = new List<string>
        {
            "DETALLE DE ANCLA" + (marca.Trim().Length > 0 ? " " + marca.Trim() : string.Empty),
        };

        var diam = textoDiametro.Trim();

        if (diam.Length > 0)
        {
            renglones.Add("ANCLA " + (diam.EndsWith("\"", StringComparison.Ordinal) ? diam : diam + "\""));
        }

        renglones.Add($"LONG. VERTICAL {Numero(ahogo / escala)} cm");

        if (pata > 0)
        {
            renglones.Add($"DOBLEZ {Numero(pata / escala)} cm");

            // EL LARGO DESARROLLADO ES LA SUMA, y va dicho: es el que se pide al proveedor y el que
            // nadie quiere calcular a mano sobre el plano.
            renglones.Add($"DESARROLLO {Numero((ahogo + altoTuerca + pata) / escala)} cm");
        }
        else
        {
            renglones.Add($"DESARROLLO {Numero((ahogo + altoTuerca) / escala)} cm");
        }

        var yRotulo = yFondo - o2 - (2.0 * alturaTexto);

        // Lo que ocupa: de la cota de la izquierda al canto derecho de la pieza. Lo usa el dibujante
        // para saber dónde acaba el detalle, y con él el reparto de la placa siguiente.
        var ancho = xDer - (xIzq - o2);

        return new Detalle(
            Barra: barra,
            Tuerca: ElevacionPlacaBase.Caja(
                x - (anchoTuerca / 2), yBaseTuerca, x + (anchoTuerca / 2), yPunta),
            AristasTuerca: ElevacionPlacaBase.AristasDeLaTuerca(
                x, yBaseTuerca, yPunta, anchoTuerca),
            Rosca: rosca,
            Cotas: cotas,
            Rotulo: (x, yRotulo),
            Renglones: renglones,
            Ancho: ancho);
    }

    /// <summary>Un número como va en el plano: sin decimales de más.</summary>
    private static string Numero(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.005
            ? Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
