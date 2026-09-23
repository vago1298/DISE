namespace CadLink.Cad;

/// <summary>
/// Los <b>símbolos de soldadura</b> del estándar, dibujados con geometría pura.
/// </summary>
/// <remarks>
/// <para>
/// Es la simbología de la AWS A2.4 —la que va en el cuadro de notas de cualquier plano de
/// estructura metálica—: el leader con su flecha, la línea de referencia, el símbolo del tipo de
/// soldadura encima, los modificadores (todo alrededor, de campo) y la cola con el proceso.
/// </para>
/// <para>
/// <b>Sin COM</b>, igual que <see cref="ElevacionPlacaBase"/> y por el mismo motivo: aquí está lo
/// que se puede equivocar sin que se note —las proporciones del símbolo y dónde cae cada pieza— y
/// así se puede comprobar sin AutoCAD delante. El dibujante solo traduce puntos a entidades.
/// </para>
/// <para>
/// <b>Todo se mide en alturas de texto.</b> No hay un solo número en centímetros: el símbolo
/// completo se define como múltiplos de la altura del texto que lo acompaña, que es como está
/// definido en el estándar y lo que hace que la simbología se vea igual a 1:10 que a 1:25. Quien
/// dibuje a otra escala pasa otra altura y todo crece con ella.
/// </para>
/// </remarks>
public static class SimbolosSoldadura
{
    /// <summary>Los cinco tipos de la simbología.</summary>
    public enum Tipo
    {
        /// <summary>Soldadura de filete: el triángulo, el símbolo más usado de todos.</summary>
        Filete,

        /// <summary>A todo alrededor de la pieza: el círculo en el codo del leader.</summary>
        TodoAlrededor,

        /// <summary>De campo —hecha en obra, no en taller—: la bandera.</summary>
        DeCampo,

        /// <summary>Bisel simple: el chaflán de la preparación, con su ángulo.</summary>
        BiselSimple,

        /// <summary>Filete en los dos lados del elemento: dos triángulos, uno por lado.</summary>
        FileteAmbosLados,
    }

    /// <summary>Un texto del dibujo, con su punto, su altura y su anclaje.</summary>
    /// <param name="Anclaje">
    /// El <c>Alignment</c> de un <b>TEXT</b> de AutoCAD: <c>9</c> = izquierda a media altura,
    /// <c>10</c> = centrado y <c>11</c> = derecha.
    /// <para>
    /// <b>Ojo con confundirlo con el del MTEXT</b>, que numera lo mismo del 4 al 6: ahí está el
    /// error de bulto que deja los textos descolocados sin que nada falle. Estos son los del TEXT
    /// porque la simbología se dibuja con TEXT —renglones sueltos, sin códigos de párrafo—.
    /// </para>
    /// </param>
    public readonly record struct TextoDwg(string S, double X, double Y, double Altura, int Anclaje);

    /// <summary>
    /// Un renglón de la simbología: un símbolo completo más el nombre de lo que significa.
    /// </summary>
    /// <param name="Leader">
    /// Los tres puntos del leader: la <b>punta de la flecha</b>, el <b>codo</b> y el final de la
    /// línea de referencia. La flecha la pone el dibujante sobre el primer tramo.
    /// </param>
    /// <param name="Abiertas">Poligonales abiertas: el símbolo, la cola y el asta de la bandera.</param>
    /// <param name="Cerradas">Los triángulos del filete, que van cerrados y sin relleno.</param>
    /// <param name="Rellenas">Lo que va <b>relleno</b>: la bandera de la soldadura de campo.</param>
    /// <param name="Circulo">El círculo de «todo alrededor», en el codo. <c>null</c> si no lleva.</param>
    public sealed record Renglon(
        Tipo Tipo,
        double[] Leader,
        List<double[]> Abiertas,
        List<double[]> Cerradas,
        List<double[]> Rellenas,
        (double X, double Y, double R)? Circulo,
        List<TextoDwg> Textos);

    /// <summary>La simbología completa: su título y sus renglones.</summary>
    /// <param name="Ancho">Lo que ocupa a lo ancho, para encuadrarla o dejarle sitio.</param>
    /// <param name="Alto">Y a lo alto, del título al último renglón.</param>
    public sealed record Legenda(
        TextoDwg Titulo, List<Renglon> Renglones, double Ancho, double Alto);

    // ======================================================================
    //  LAS PROPORCIONES, TODAS EN ALTURAS DE TEXTO
    // ======================================================================
    //
    //  Salen del estándar y de medir el cuadro de un plano real. Están con nombre y no
    //  escritas en medio de las cuentas para que se puedan ajustar de una vez: cambiar
    //  LadoSimbolo aquí cambia los cinco renglones y su vista previa a la vez.

    /// <summary>Lo que avanza el tramo inclinado del leader, en horizontal y en vertical.</summary>
    /// <remarks>Los dos iguales: el tramo va a <b>45°</b>, como en el estándar.</remarks>
    private const double TramoFlecha = 2.0;

    /// <summary>Largo de la línea de referencia, la horizontal.</summary>
    private const double LineaReferencia = 7.0;

    /// <summary>El cateto del triángulo del filete.</summary>
    private const double LadoSimbolo = 1.3;

    /// <summary>Dónde se planta el símbolo sobre la línea de referencia, desde el codo.</summary>
    private const double SimboloDesdeCodo = 2.2;

    /// <summary>Alto del asta de la bandera de campo.</summary>
    private const double AstaBandera = 1.8;

    /// <summary>Y el vuelo de la bandera, lo que sobresale del asta.</summary>
    private const double VueloBandera = 1.0;

    /// <summary>Radio del círculo de «todo alrededor».</summary>
    private const double RadioCirculo = 0.55;

    /// <summary>Lo que se abre la cola, en largo y en semialtura.</summary>
    private const double LargoCola = 0.85;
    private const double SemiAltoCola = 0.55;

    /// <summary>Altura del texto del proceso, el que va en la cola.</summary>
    private const double AlturaCola = 0.62;

    /// <summary>Desde el final de la línea de referencia hasta el nombre del símbolo.</summary>
    private const double NombreDesdeLinea = 4.2;

    /// <summary>Separación entre un renglón y el siguiente.</summary>
    private const double SeparacionRenglones = 4.6;

    /// <summary>Y del título al primer renglón.</summary>
    private const double TituloSobreElPrimero = 3.4;

    /// <summary>El texto que va en la cola de todos los renglones.</summary>
    /// <remarks>
    /// <b>Dice «TIPO» y no un proceso concreto</b> —E70XX, GMAW— porque esto es la leyenda: lo que
    /// enseña es <i>dónde</i> se escribe el proceso, no cuál se usa. En el detalle real ahí va el
    /// electrodo de la hoja.
    /// </remarks>
    public const string TextoDeLaCola = "TIPO";

    /// <summary>
    /// La simbología completa, con el <b>título</b> en el punto que se le pase.
    /// </summary>
    /// <param name="x">La esquina izquierda: por ahí pasan las puntas de las flechas.</param>
    /// <param name="y">El renglón del título. Los símbolos cuelgan hacia abajo.</param>
    /// <param name="h">La altura del texto de los nombres. Todo lo demás sale de ella.</param>
    /// <param name="titulo">El encabezado del cuadro.</param>
    public static Legenda Construir(
        double x, double y, double h, string titulo = "SIMBOLOGIA DE SOLDADURA")
    {
        var renglones = new List<Renglon>();

        if (h <= 0)
        {
            return new Legenda(new TextoDwg(titulo, x, y, 1, 9), renglones, 0, 0);
        }

        // Los cinco, en el orden de la leyenda: primero el filete —el que se usa siempre—, luego
        // sus dos modificadores, después el bisel y al final el de los dos lados.
        var tipos = new[]
        {
            Tipo.Filete,
            Tipo.TodoAlrededor,
            Tipo.DeCampo,
            Tipo.BiselSimple,
            Tipo.FileteAmbosLados,
        };

        var yRenglon = y - (TituloSobreElPrimero * h);

        foreach (var t in tipos)
        {
            renglones.Add(UnRenglon(t, x, yRenglon, h));
            yRenglon -= SeparacionRenglones * h;
        }

        // Lo que ocupa: del arranque de la flecha al final del nombre más largo. El ancho del texto
        // se estima con 0.62 alturas por letra, que es lo que mide una mayúscula de la fuente
        // condensada de esta macro. No hay forma de medirlo sin AutoCAD, y para dejar sitio basta.
        var letras = 0;

        foreach (var t in tipos)
        {
            letras = Math.Max(letras, Nombre(t).Length);
        }

        var ancho = ((TramoFlecha + LineaReferencia + NombreDesdeLinea) * h)
                    + (letras * 0.62 * h);

        var alto = (TituloSobreElPrimero * h) + (SeparacionRenglones * h * (tipos.Length - 1))
                   + (LadoSimbolo * h);

        // Anclaje 9 = izquierda a media altura, para que el título arranque a plomo con las flechas.
        return new Legenda(
            new TextoDwg(titulo, x, y, 1.15 * h, 9), renglones, ancho, alto);
    }

    /// <summary>El nombre de cada símbolo, tal como va en el cuadro.</summary>
    public static string Nombre(Tipo t) => t switch
    {
        Tipo.Filete => "SOLDADURA DE FILETE",
        Tipo.TodoAlrededor => "SOLDADURA A TODO ALREDEDOR DE LA PIEZA",
        Tipo.DeCampo => "SOLDADURA DE CAMPO",
        Tipo.BiselSimple => "SOLDADURA DE BISEL SIMPLE",
        Tipo.FileteAmbosLados => "SOLDADURA DE FILETE EN AMBOS LADOS DEL ELEMENTO",
        _ => string.Empty,
    };

    /// <summary>
    /// Un símbolo completo, con su leader, su cola y su nombre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La <b>punta de la flecha</b> queda en <c>(x, y)</c> y el símbolo se construye hacia arriba y
    /// a la derecha, que es como se dibuja a mano: se apunta a la junta y el símbolo sale de ahí.
    /// </para>
    /// <para>
    /// El símbolo va <b>encima</b> de la línea de referencia, y eso no es estético: en el estándar,
    /// encima de la línea significa «del otro lado de la junta» y debajo «del lado de la flecha».
    /// Un filete de un solo lado se dibuja de UN lado de la línea, y el de los dos lados lleva los
    /// dos triángulos, que es justo lo que distingue a esos dos renglones de la leyenda.
    /// </para>
    /// </remarks>
    public static Renglon UnRenglon(Tipo t, double x, double y, double h)
    {
        var abiertas = new List<double[]>();
        var cerradas = new List<double[]>();
        var rellenas = new List<double[]>();
        var textos = new List<TextoDwg>();

        (double X, double Y, double R)? circulo = null;

        // ---------- El leader: la flecha a 45° y la línea de referencia ----------
        var xCodo = x + (TramoFlecha * h);
        var yRef = y + (TramoFlecha * h);
        var xFin = xCodo + (LineaReferencia * h);

        var leader = new[] { x, y, xCodo, yRef, xFin, yRef };

        // ---------- El símbolo, plantado sobre la línea ----------
        var xs = xCodo + (SimboloDesdeCodo * h);
        var lado = LadoSimbolo * h;

        switch (t)
        {
            case Tipo.Filete:
            case Tipo.TodoAlrededor:

                // EL TRIÁNGULO DEL FILETE, con el cateto vertical A LA IZQUIERDA. Es la convención
                // del estándar y no da lo mismo: con la hipotenusa del otro lado el símbolo se
                // confunde con el del bisel.
                cerradas.Add(Triangulo(xs, yRef, lado, arriba: true));
                break;

            case Tipo.FileteAmbosLados:

                // Los dos triángulos comparten el arranque, uno hacia arriba y otro hacia abajo:
                // el de arriba es el otro lado de la junta y el de abajo el lado de la flecha.
                cerradas.Add(Triangulo(xs, yRef, lado, arriba: true));
                cerradas.Add(Triangulo(xs, yRef, lado, arriba: false));
                break;

            case Tipo.DeCampo:

                // LA BANDERA: un asta vertical y el triángulo relleno colgando a la derecha. Va
                // rellena a propósito —así lo pide el estándar— y es lo que distingue de un
                // vistazo lo que se suelda en obra de lo que llega soldado de taller.
                abiertas.Add(new[] { xs, yRef, xs, yRef + (AstaBandera * h) });

                rellenas.Add(new[]
                {
                    xs, yRef + (AstaBandera * h),
                    xs + (VueloBandera * h), yRef + ((AstaBandera - 0.35) * h),
                    xs, yRef + ((AstaBandera - 0.7) * h),
                });

                // Y con su filete: una soldadura de campo TAMBIÉN es de algún tipo. Sin el
                // triángulo, el renglón enseñaría la bandera como si fuera un símbolo completo.
                cerradas.Add(Triangulo(xs + (VueloBandera * h) + (0.5 * h), yRef, lado,
                                       arriba: true));
                break;

            case Tipo.BiselSimple:

                // EL BISEL: la línea inclinada de la preparación, y su ángulo acotado debajo de la
                // línea de referencia. El ángulo va fuera del símbolo porque es un dato del
                // chaflán, no parte del símbolo.
                abiertas.Add(new[] { xs, yRef, xs + (0.85 * lado), yRef + (1.25 * lado) });

                textos.Add(new TextoDwg(
                    "45%%d", xs - (0.2 * h), yRef - (1.15 * h), 0.72 * h, 11));
                break;
        }

        if (t == Tipo.TodoAlrededor)
        {
            // EL CÍRCULO VA EN EL CODO, no en medio de la línea: es la posición del estándar y lo
            // que hace que se lea «esta soldadura da la vuelta a toda la pieza».
            circulo = (xCodo, yRef, RadioCirculo * h);
        }

        // ---------- La cola, con el proceso ----------
        // Se abre HACIA LA DERECHA desde el final de la línea, y el texto va dentro. Sin la cola no
        // hay dónde escribir el electrodo, que es la mitad de la información de una soldadura.
        abiertas.Add(new[]
        {
            xFin + (LargoCola * h), yRef + (SemiAltoCola * h),
            xFin, yRef,
            xFin + (LargoCola * h), yRef - (SemiAltoCola * h),
        });

        textos.Add(new TextoDwg(
            TextoDeLaCola, xFin + ((LargoCola + 0.25) * h), yRef, AlturaCola * h, 9));

        // ---------- Y el nombre, a la derecha de todo ----------
        textos.Add(new TextoDwg(
            Nombre(t), xFin + (NombreDesdeLinea * h), yRef, h, 9));

        return new Renglon(t, leader, abiertas, cerradas, rellenas, circulo, textos);
    }

    /// <summary>
    /// El símbolo <b>de un detalle real</b>: la flecha apunta a la junta y el símbolo cuelga a la
    /// izquierda, con su tamaño y su electrodo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo pidió el usuario: <i>«cuando la soldadura sea en todo el contorno debe colocar ese detalle
    /// de línea»</i>, señalando el renglón de <see cref="Tipo.TodoAlrededor"/> del cuadro de
    /// simbología. El detalle de la placa escribía solo el texto —«SOLDADURA CON E70XX DE 3/16" DE
    /// ESP.»— y en un plano de estructura eso se dice con el <b>símbolo</b>: el que lo lee busca el
    /// círculo en el codo, no una frase.
    /// </para>
    /// <para>
    /// Es <see cref="UnRenglon"/> mirando al otro lado. En el cuadro la línea de referencia crece a
    /// la derecha porque el nombre va detrás; en el detalle la pieza está a la derecha y la flecha
    /// apunta hacia ella, así que la línea de referencia y la cola caen a la <b>izquierda</b>. Las
    /// proporciones son las mismas —las constantes de arriba—, para que el símbolo del detalle y el
    /// del cuadro que lo explica se vean iguales.
    /// </para>
    /// <para>
    /// <b>El triángulo no se voltea.</b> Su cateto vertical sigue a la izquierda y la hipotenusa a
    /// la derecha, como en el estándar: el símbolo del filete se lee igual venga la línea de donde
    /// venga, y espejado se confundiría con el del bisel.
    /// </para>
    /// <para>
    /// El leader lleva <b>tres tramos</b> y no dos: sube desde la punta, cruza en horizontal por
    /// encima y baja al codo. Es lo que ya hacía el leader de este detalle, y no es decoración: por
    /// ahí abajo pasan las dos cadenas de cotas, y una diagonal recta les cruza los números.
    /// </para>
    /// </remarks>
    /// <param name="xPunta">Dónde muerde la flecha: el eje de la franja de soldadura.</param>
    /// <param name="yPunta">Su altura. En un perfil I NO es el centro de la pieza: ahí hay aire.</param>
    /// <param name="xCodo">Donde arranca la línea de referencia, a la izquierda de la pieza.</param>
    /// <param name="yRef">La altura de la línea de referencia.</param>
    /// <param name="despeje">Cuánto sube el tramo de paso sobre la punta, para librar las cotas.</param>
    /// <param name="tamano">
    /// El tamaño del filete, a la izquierda del triángulo, donde lo pone el estándar. Vacío si no se
    /// capturó: mejor sin número que con uno inventado.
    /// </param>
    /// <param name="cola">El electrodo, dentro de la cola. Vacío = sin cola.</param>
    public static Renglon Enlazado(
        Tipo t, double xPunta, double yPunta, double xCodo, double yRef, double h,
        double despeje, string tamano, string cola)
    {
        var abiertas = new List<double[]>();
        var cerradas = new List<double[]>();
        var textos = new List<TextoDwg>();

        (double X, double Y, double R)? circulo = null;

        // ---------- El leader: sube, cruza y baja al codo ----------
        var yPaso = yPunta + despeje;
        var dx = xCodo - xPunta;

        var leader = new[]
        {
            xPunta, yPunta,
            xPunta + (0.18 * dx), yPaso,
            xCodo - (0.18 * dx), yPaso,
            xCodo, yRef,
        };

        // ---------- La línea de referencia, hacia la izquierda ----------
        var xFin = xCodo - (LineaReferencia * h);

        abiertas.Add(new[] { xCodo, yRef, xFin, yRef });

        // ---------- El filete, plantado sobre la línea ----------
        var lado = LadoSimbolo * h;
        var xs = xCodo - (SimboloDesdeCodo * h) - lado;

        cerradas.Add(Triangulo(xs, yRef, lado, arriba: true));

        if (tamano.Trim().Length > 0)
        {
            // A LA IZQUIERDA DEL TRIÁNGULO Y DEL MISMO LADO DE LA LÍNEA, que es donde el estándar
            // pone el tamaño del filete. Anclaje 11 = a la derecha y a media altura, así el número
            // crece hacia la izquierda y nunca se mete en el símbolo.
            textos.Add(new TextoDwg(
                tamano.Trim(), xs - (0.4 * h), yRef + (0.55 * lado), 0.8 * h, 11));
        }

        // ---------- El círculo de «todo alrededor», en el codo ----------
        if (t == Tipo.TodoAlrededor)
        {
            circulo = (xCodo, yRef, RadioCirculo * h);
        }

        // ---------- La cola, con el electrodo ----------
        if (cola.Trim().Length > 0)
        {
            abiertas.Add(new[]
            {
                xFin - (LargoCola * h), yRef + (SemiAltoCola * h),
                xFin, yRef,
                xFin - (LargoCola * h), yRef - (SemiAltoCola * h),
            });

            textos.Add(new TextoDwg(
                cola.Trim(), xFin - ((LargoCola + 0.25) * h), yRef, AlturaCola * h, 11));
        }

        return new Renglon(t, leader, abiertas, cerradas, new List<double[]>(), circulo, textos);
    }

    // IzquierdaDelSimbolo SE QUITÓ. Servía para colocar la frase «SOLDADURA CON E70XX DE 3/16" DE
    // ESP.» justo antes de la cola, y esa frase ya no se escribe: el símbolo dice el tamaño y el
    // electrodo en el sitio donde el estándar manda buscarlos. Sin ella, esta cuenta no la usaba
    // nadie, y una función pública sin llamadores es una invitación a usarla mal.
    //
    // El ancho que ocupa el símbolo sigue contado para el reparto de placas, pero no desde aquí:
    // lo apuntan los propios helpers de dibujo cuando lo dibujan. Ver PlacaBaseDrawer.Apuntar.

    /// <summary>
    /// El triángulo del filete: cateto vertical a la izquierda y base sobre la línea.
    /// </summary>
    /// <param name="arriba">
    /// <c>true</c> lo pone encima de la línea de referencia y <c>false</c> debajo. Ver la nota de
    /// <see cref="UnRenglon"/> sobre lo que significa cada lado.
    /// </param>
    private static double[] Triangulo(double x, double yRef, double lado, bool arriba)
    {
        var alto = arriba ? lado : -lado;

        return new[]
        {
            x, yRef,
            x, yRef + alto,
            x + lado, yRef,
        };
    }
}
