namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// Dónde van los <b>ejes, las burbujas y las cotas</b>: la cuenta de la macro, sin AutoCAD.
/// </summary>
/// <remarks>
/// <para>
/// Es la parte de <c>DibujarEjes</c>, <c>AcotarEjes</c>, <c>SaleEjes</c>,
/// <c>SaleEjesCorto</c> y <c>AbajoDeEjes</c> que es <b>pura aritmética</b>. Está aparte del
/// dibujante a propósito: así se puede comprobar contra los números de la macro sin abrir
/// AutoCAD —está en <c>tools/prueba-ejes-plano</c>— y el dibujante se queda con lo que de
/// verdad necesita COM.
/// </para>
/// <para>
/// Los dos números que más se han peleado en la macro, y que aquí son <b>independientes</b>
/// como allá:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>EJES_INICIO_BURBUJA_M</c> = 2.00 manda <b>solo</b> dónde arranca la burbuja, que
///     es donde termina la línea de eje.
///   </item>
///   <item>
///     <c>COTAS_SEPARACION</c> = 0.75 y <c>COTAS_SEPARACION_TOTAL</c> = 1.17 mandan
///     <b>solo</b> las cotas. Mover uno no mueve al otro; con esos valores quedan 0.83 de
///     aire entre la cota total y la burbuja.
///   </item>
/// </list>
/// </remarks>
public sealed class EjesPlano
{
    private readonly ConfigPlano _cfg;

    public EjesPlano(ConfigPlano cfg) => _cfg = cfg;

    /// <summary>Un eje ya colocado: su nombre, su coordenada y sus dos puntas.</summary>
    /// <param name="Id">Lo que dice la burbuja.</param>
    /// <param name="Ordenada">Su coordenada: la X si es vertical, la Y si es horizontal.</param>
    /// <param name="Desde">Donde arranca la línea del eje.</param>
    /// <param name="Hasta">Donde termina.</param>
    /// <param name="BurbujaA">Centro de la burbuja del lado de <paramref name="Desde"/>.</param>
    /// <param name="BurbujaB">Centro de la del otro lado.</param>
    public sealed record EjeColocado(
        string Id, double Ordenada, double Desde, double Hasta, double BurbujaA, double BurbujaB);

    /// <summary>Una cota: de dónde a dónde, y dónde va su número.</summary>
    /// <param name="EsTotal">
    /// La del ancho total lleva la línea de extensión <b>corta</b>, para no llegar hasta la
    /// burbuja.
    /// </param>
    public sealed record Cota(
        double X1, double Y1, double X2, double Y2, double XTexto, double YTexto, bool EsTotal);

    /// <summary>Radio de la burbuja: <c>RADIO_BURBUJA</c>.</summary>
    public double RadioBurbuja => Positivo(_cfg.Numero("RADIO_BURBUJA", 0.35), 0.35);

    /// <summary>
    /// A qué distancia de la planta <b>arranca la burbuja</b>, arriba y a la izquierda.
    /// </summary>
    /// <remarks>
    /// Es <c>SaleEjes</c>. Con <c>EJES_INICIO_BURBUJA_M</c> mayor que cero, ese valor manda y
    /// se acabó: es lo que hace que las burbujas no se muevan al tocar las cotas. Con 0 se
    /// vuelve a la cuenta vieja, la que las ataba a la cota total.
    /// </remarks>
    public double SaleEjes()
    {
        var inicio = _cfg.Numero("EJES_INICIO_BURBUJA_M", 2);

        if (inicio > 0)
        {
            return inicio;
        }

        var s = _cfg.Numero("EJES_SOBRESALEN", 1.15);

        if (_cfg.Bandera("ACOTAR_EJES", true) && _cfg.Bandera("COTAS_EMPUJAR_EJES", true))
        {
            var minimo = SeparacionTotal() + _cfg.Numero("EJES_HOLGURA_COTA_M", 0.15);
            if (s < minimo)
            {
                s = minimo;
            }
        }

        return s;
    }

    /// <summary>Lo mismo por la <b>derecha y por abajo</b>: es <c>SaleEjesCorto</c>.</summary>
    /// <remarks>
    /// Con <c>EJES_SALE_CORTO_M</c> y <c>EJES_RECORTE_M</c> en 0 —como están— los cuatro
    /// lados salen lo mismo, mandados por un solo número.
    /// </remarks>
    public double SaleEjesCorto()
    {
        var propio = _cfg.Numero("EJES_SALE_CORTO_M", 0);

        if (propio > 0)
        {
            return propio;
        }

        var recorte = _cfg.Numero("EJES_RECORTE_M", 0);
        var s = recorte > 0 ? SaleEjes() - recorte : SaleEjes();

        return s < 0 ? 0 : s;
    }

    /// <summary>
    /// Cuánto baja de verdad lo que se dibuja debajo de la planta: es <c>AbajoDeEjes</c>.
    /// </summary>
    /// <remarks>
    /// La punta del eje de abajo <b>más</b> la burbuja y su rayita. Con esto el rótulo de la
    /// planta se coloca justo debajo y no flotando a dos metros.
    /// </remarks>
    public double AbajoDeEjes(bool hayEjes)
    {
        var s = SaleEjesCorto();

        if (!hayEjes)
        {
            return s;
        }

        s += RadioBurbuja * 2;

        if (_cfg.Bandera("BURBUJA_CRUZ", true))
        {
            s += RadioBurbuja * Positivo(_cfg.Numero("BURBUJA_CRUZ_LARGO", 0.9), 0.9);
        }

        return s;
    }

    /// <summary>La primera cadena de cotas: <c>COTAS_SEPARACION</c>.</summary>
    public double Separacion() => Positivo(_cfg.Numero("COTAS_SEPARACION", 0.75), 0.75);

    /// <summary>La cota del ancho total: <c>COTAS_SEPARACION_TOTAL</c>.</summary>
    /// <remarks>
    /// Con el tope de la macro: si quedara por debajo de la primera cadena, se sube a
    /// <c>0.75 + 0.42</c>, que son los 1.17 de siempre.
    /// </remarks>
    public double SeparacionTotal()
    {
        var s = _cfg.Numero("COTAS_SEPARACION_TOTAL", 1.17);
        var primera = Separacion();

        return s <= primera ? primera + 0.42 : s;
    }

    /// <summary>
    /// Coloca los ejes <b>verticales</b> —los de la cuadrícula en X— sobre la planta.
    /// </summary>
    /// <param name="ejes">Nombre y coordenada de cada uno.</param>
    /// <param name="yMin">Borde inferior de lo dibujado.</param>
    /// <param name="yMax">Borde superior.</param>
    public List<EjeColocado> Verticales(
        IReadOnlyList<(string Id, double Ordenada)> ejes, double yMin, double yMax)
    {
        var sale = SaleEjes();
        var corto = SaleEjesCorto();
        var r = RadioBurbuja;
        var salida = new List<EjeColocado>();

        foreach (var (id, x) in ejes)
        {
            // Abajo el recortado y arriba el largo, como en la macro: arriba es donde van
            // las cotas.
            var abajo = yMin - corto;
            var arriba = yMax + sale;

            salida.Add(new EjeColocado(id, x, abajo, arriba, abajo - r, arriba + r));
        }

        return salida;
    }

    /// <summary>Y los <b>horizontales</b>: el largo va a la izquierda.</summary>
    public List<EjeColocado> Horizontales(
        IReadOnlyList<(string Id, double Ordenada)> ejes, double xMin, double xMax)
    {
        var sale = SaleEjes();
        var corto = SaleEjesCorto();
        var r = RadioBurbuja;
        var salida = new List<EjeColocado>();

        foreach (var (id, y) in ejes)
        {
            var izq = xMin - sale;
            var der = xMax + corto;

            salida.Add(new EjeColocado(id, y, izq, der, izq - r, der + r));
        }

        return salida;
    }

    /// <summary>
    /// Las cotas de los ejes, en <b>los cuatro lados</b>: es <c>AcotarEjes</c> con
    /// <c>CotasEnX</c> y <c>CotasEnY</c>.
    /// </summary>
    /// <remarks>
    /// Cada lado se prende por separado con <c>COTAS_ARRIBA</c>, <c>COTAS_ABAJO</c>,
    /// <c>COTAS_IZQUIERDA</c> y <c>COTAS_DERECHA</c>. La cadena eje a eje necesita 2 ejes y
    /// la del ancho total, 3: con solo dos, la total y la cadena serían la misma línea
    /// dibujada dos veces.
    /// </remarks>
    public List<Cota> Cotas(
        IReadOnlyList<double> ejesX, IReadOnlyList<double> ejesY,
        double xMin, double yMin, double xMax, double yMax)
    {
        var cotas = new List<Cota>();

        if (!_cfg.Bandera("ACOTAR_EJES", true))
        {
            return cotas;
        }

        var off = Separacion();
        var off2 = SeparacionTotal();
        var total = _cfg.Bandera("COTA_TOTAL", true);

        if (ejesX.Count >= 2)
        {
            if (_cfg.Bandera("COTAS_ARRIBA", true))
            {
                EnX(cotas, ejesX, yMax, +1, off, off2, total);
            }

            if (_cfg.Bandera("COTAS_ABAJO", true))
            {
                EnX(cotas, ejesX, yMin, -1, off, off2, total);
            }
        }

        if (ejesY.Count >= 2)
        {
            if (_cfg.Bandera("COTAS_IZQUIERDA", true))
            {
                EnY(cotas, ejesY, xMin, -1, off, off2, total);
            }

            if (_cfg.Bandera("COTAS_DERECHA", true))
            {
                EnY(cotas, ejesY, xMax, +1, off, off2, total);
            }
        }

        return cotas;
    }

    private static void EnX(
        List<Cota> cotas, IReadOnlyList<double> ejes, double yBase, double signo,
        double off, double off2, bool total)
    {
        for (var i = 0; i < ejes.Count - 1; i++)
        {
            var yT = yBase + (signo * off);
            cotas.Add(new Cota(ejes[i], yBase, ejes[i + 1], yBase,
                               (ejes[i] + ejes[i + 1]) / 2, yT, false));
        }

        if (total && ejes.Count >= 3)
        {
            var yT = yBase + (signo * off2);
            cotas.Add(new Cota(ejes[0], yBase, ejes[^1], yBase,
                               (ejes[0] + ejes[^1]) / 2, yT, true));
        }
    }

    private static void EnY(
        List<Cota> cotas, IReadOnlyList<double> ejes, double xBase, double signo,
        double off, double off2, bool total)
    {
        for (var i = 0; i < ejes.Count - 1; i++)
        {
            var xT = xBase + (signo * off);
            cotas.Add(new Cota(xBase, ejes[i], xBase, ejes[i + 1],
                               xT, (ejes[i] + ejes[i + 1]) / 2, false));
        }

        if (total && ejes.Count >= 3)
        {
            var xT = xBase + (signo * off2);
            cotas.Add(new Cota(xBase, ejes[0], xBase, ejes[^1],
                               xT, (ejes[0] + ejes[^1]) / 2, true));
        }
    }

    /// <summary>
    /// Las rayitas de la burbuja: <b>tres</b>, o cuatro con
    /// <c>BURBUJA_CRUZ_4_LINEAS</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="ux"/>, <paramref name="uy"/> es la dirección que va <b>hacia el
    /// dibujo</b>. La cuarta rayita es la que apunta hacia allá, y por eso es opcional: en
    /// una burbuja pegada a la planta se mete en el dibujo.
    /// </remarks>
    public List<(double X1, double Y1, double X2, double Y2)> RayitasDeBurbuja(
        double cx, double cy, double ux, double uy)
    {
        var salida = new List<(double, double, double, double)>();

        if (!_cfg.Bandera("BURBUJA_CRUZ", true))
        {
            return salida;
        }

        var r = RadioBurbuja;
        var largo = r * Positivo(_cfg.Numero("BURBUJA_CRUZ_LARGO", 0.9), 0.9);

        if (largo <= 0)
        {
            return salida;
        }

        var n = Math.Sqrt((ux * ux) + (uy * uy));

        if (n < 1e-9)
        {
            ux = 0;
            uy = 1;
        }
        else
        {
            ux /= n;
            uy /= n;
        }

        // La perpendicular, para las dos de los costados.
        var px = -uy;
        var py = ux;

        salida.Add((cx - (ux * r), cy - (uy * r), cx - (ux * (r + largo)), cy - (uy * (r + largo))));
        salida.Add((cx + (px * r), cy + (py * r), cx + (px * (r + largo)), cy + (py * (r + largo))));
        salida.Add((cx - (px * r), cy - (py * r), cx - (px * (r + largo)), cy - (py * (r + largo))));

        if (_cfg.Bandera("BURBUJA_CRUZ_4_LINEAS", true))
        {
            salida.Add((cx + (ux * r), cy + (uy * r),
                        cx + (ux * (r + largo)), cy + (uy * (r + largo))));
        }

        return salida;
    }

    /// <summary>El anillo interior, o 0 si <c>BURBUJA_DOBLE</c> está en NO.</summary>
    public double RadioAnillo()
    {
        if (!_cfg.Bandera("BURBUJA_DOBLE", true))
        {
            return 0;
        }

        var f = _cfg.Numero("BURBUJA_ANILLO", 0.82);

        if (f <= 0 || f >= 1)
        {
            f = 0.82;
        }

        return RadioBurbuja * f;
    }

    /// <summary>Altura del texto de la burbuja: <c>ALTURA_TEXTO_BURBUJA</c>, o automática.</summary>
    public double AlturaTextoBurbuja()
    {
        var alt = _cfg.Numero("ALTURA_TEXTO_BURBUJA", 0);

        if (alt > 0)
        {
            return alt;
        }

        var anillo = RadioAnillo();
        return anillo > 0 ? anillo * 1.2 : RadioBurbuja * 1.1;
    }

    // =================================================================================
    //  EL PRIMER Y EL ÚLTIMO EJE, AL PAÑO EXTERIOR DEL MURO
    // =================================================================================

    /// <summary>¿Se corren los ejes de orilla al paño? Es <c>EJES_EXTREMOS_AL_PANO</c>.</summary>
    public bool ExtremosAlPano => _cfg.Bandera("EJES_EXTREMOS_AL_PANO", true);

    /// <summary>
    /// Holgura para dar por bueno que un muro <b>corre sobre</b> un eje, en metros.
    /// </summary>
    /// <remarks>
    /// <c>EJES_PANO_TOL_CM</c> son 25 cm, y son necesarios: en ETABS el muro se modela en su
    /// eje medio, pero un muro de fachada se suele dibujar con su paño interior en el eje de
    /// la cuadrícula, así que su línea media queda a un palmo del eje. Con una tolerancia
    /// pequeña no se encontraría ningún muro y los ejes se quedarían sin correr.
    /// </remarks>
    public double ToleranciaPano => _cfg.Numero("EJES_PANO_TOL_CM", 25) / 100;

    /// <summary>
    /// Holgura para dar dos ejes por <b>el mismo</b>: <c>EJES_UNIR_TOL_CM</c>, 1 cm.
    /// </summary>
    public double ToleranciaUnirEjes => _cfg.Numero("EJES_UNIR_TOL_CM", 1) / 100;

    /// <summary>
    /// Quita los ejes <b>repetidos</b>: los que están a la misma coordenada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió tal cual —«solo es una, porque en unos ejes lo duplicas»— y el que sobra no
    /// se ve como un eje de más: se ve como una <b>línea más gruesa y más oscura</b> que las
    /// demás, porque son dos líneas encima de la otra, con dos burbujas superpuestas y dos
    /// cotas iguales pisándose.
    /// </para>
    /// <para>
    /// Y viene del modelo, no de un fallo de aquí: en ETABS la cuadrícula suele traer el eje
    /// <b>declarado dos veces</b> —una en el sistema principal y otra como secundario, o el
    /// mismo eje repetido al haber copiado la planta— y <c>GetGridSys_2</c> devuelve todas
    /// las líneas que hay declaradas, no las distintas.
    /// </para>
    /// <para>
    /// Del repetido <b>se guarda el primero</b>, que es el que trae el nombre bueno: si un
    /// eje viene como «2» y como «Grid2», la burbuja debe decir 2. Y se compara con
    /// <see cref="ToleranciaUnirEjes"/>, 1 cm, porque dos ejes de verdad nunca están a menos
    /// de un centímetro —serían la misma línea en el papel— pero un nudo movido por redondeo
    /// sí puede dar 4.999 y 5.000.
    /// </para>
    /// </remarks>
    public List<(string Id, double Ordenada)> SinRepetidos(
        IReadOnlyList<(string Id, double Ordenada)> ejes)
        => SinRepetidos(ejes, ToleranciaUnirEjes);

    /// <summary>El mismo, con la holgura a mano: es el que se comprueba en las pruebas.</summary>
    public static List<(string Id, double Ordenada)> SinRepetidos(
        IReadOnlyList<(string Id, double Ordenada)> ejes, double tolM)
    {
        var salida = new List<(string Id, double Ordenada)>();

        if (ejes.Count == 0)
        {
            return salida;
        }

        // Con holgura 0 o negativa no se une nada: se devuelve tal cual llegó.
        if (tolM <= 0)
        {
            return ejes.ToList();
        }

        foreach (var eje in ejes)
        {
            var repetido = false;

            foreach (var ya in salida)
            {
                if (Math.Abs(ya.Ordenada - eje.Ordenada) < tolM)
                {
                    repetido = true;
                    break;
                }
            }

            if (!repetido)
            {
                salida.Add(eje);
            }
        }

        return salida;
    }

    /// <summary>
    /// Corre el <b>primer y el último</b> eje al <b>paño exterior</b> del muro que lleven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>AjustarEjesExtremosAlPano</c>. En un plano de arquitectura las cotas de orilla
    /// se dan <b>al paño</b> —es lo que se replantea en obra, el borde de la construcción—
    /// mientras que las interiores se dan <b>eje a eje</b>. Así que solo se mueven el
    /// primero y el último de cada dirección: los de en medio se quedan en su eje.
    /// </para>
    /// <para>
    /// Lo que se mueve es el eje <b>completo</b>: su línea, sus dos burbujas y las cotas que
    /// arrancan de él, porque todo eso se calcula después a partir de esta ordenada. Y se
    /// devuelve una <b>lista nueva</b>, no se toca la que llega: dibujar dos veces la misma
    /// planta correría los ejes dos veces.
    /// </para>
    /// <para>
    /// Si sobre el eje no hay ningún muro ni ninguna trabe, se queda donde estaba. Un eje
    /// corrido «por si acaso» descuadraría la cota total.
    /// </para>
    /// </remarks>
    /// <param name="verticales">
    /// <c>true</c> = los ejes de las letras, los que van en X y se mueven a izquierda y
    /// derecha; <c>false</c> = los de los números.
    /// </param>
    public List<(string Id, double Ordenada)> AlPanoExterior(
        IReadOnlyList<(string Id, double Ordenada)> ejes,
        bool verticales,
        IReadOnlyList<ElementoPlanta> elementos)
    {
        var salida = ejes.ToList();

        if (!ExtremosAlPano || salida.Count < 2 || elementos.Count == 0)
        {
            return salida;
        }

        // Cuáles son los de orilla: se buscan por su coordenada y no por su posición en la
        // lista, porque la cuadrícula puede llegar sin ordenar.
        var iMin = 0;
        var iMax = 0;

        for (var i = 1; i < salida.Count; i++)
        {
            if (salida[i].Ordenada < salida[iMin].Ordenada)
            {
                iMin = i;
            }

            if (salida[i].Ordenada > salida[iMax].Ordenada)
            {
                iMax = i;
            }
        }

        if (iMin == iMax)
        {
            return salida;
        }

        var medioMin = MedioAnchoSobreEje(salida[iMin].Ordenada, verticales, elementos);
        var medioMax = MedioAnchoSobreEje(salida[iMax].Ordenada, verticales, elementos);

        if (medioMin > 0)
        {
            salida[iMin] = (salida[iMin].Id, salida[iMin].Ordenada - medioMin);
        }

        if (medioMax > 0)
        {
            salida[iMax] = (salida[iMax].Id, salida[iMax].Ordenada + medioMax);
        }

        return salida;
    }

    /// <summary>
    /// <b>Medio ancho</b> del elemento más grueso que corre sobre un eje, en metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>MedioAnchoSobreEje</c>, y tiene dos reglas de la macro que importan:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Manda el muro sobre la trabe.</b> Si por el eje van las dos cosas —lo normal:
    ///     una cadena de cerramiento sobre el muro de block—, el paño lo da el MURO, que es
    ///     el que se ve en el plano y el que se replantea. La trabe solo se usa cuando no
    ///     hay muro.
    ///   </item>
    ///   <item>
    ///     Entre varios muros sobre el mismo eje se toma el <b>más grueso</b>: la cota tiene
    ///     que llegar al paño más saliente, no a uno cualquiera.
    ///   </item>
    /// </list>
    /// <para>
    /// El elemento tiene que <b>correr a lo largo</b> del eje, no cruzarlo: se comprueba que
    /// sus dos extremos estén sobre él y que tenga largo en la otra dirección. Sin eso, un
    /// muro perpendicular que pasa por el eje daría un paño que no existe.
    /// </para>
    /// </remarks>
    public double MedioAnchoSobreEje(
        double ordenada, bool vertical, IReadOnlyList<ElementoPlanta> elementos)
    {
        var tol = ToleranciaPano;

        if (tol <= 0)
        {
            tol = 0.25;
        }

        double deMuro = 0;
        double deTrabe = 0;

        foreach (var el in elementos)
        {
            if (el.Clase != ClasePlanta.Muro && el.Clase != ClasePlanta.Trabe)
            {
                continue;
            }

            // Sobre el eje con sus DOS extremos, y con largo en la otra dirección: así un
            // muro que solo lo cruza no cuenta.
            var sobre = vertical
                ? Math.Abs(el.X1 - ordenada) <= tol && Math.Abs(el.X2 - ordenada) <= tol &&
                  Math.Abs(el.Y2 - el.Y1) > tol
                : Math.Abs(el.Y1 - ordenada) <= tol && Math.Abs(el.Y2 - ordenada) <= tol &&
                  Math.Abs(el.X2 - el.X1) > tol;

            if (!sobre)
            {
                continue;
            }

            var ancho = el.AnchoM > 0
                ? el.AnchoM
                : el.Clase == ClasePlanta.Muro
                    ? _cfg.Numero("ESPESOR_MURO_CM", 15) / 100
                    : 0.20;

            if (el.Clase == ClasePlanta.Muro)
            {
                deMuro = Math.Max(deMuro, ancho / 2);
            }
            else
            {
                deTrabe = Math.Max(deTrabe, ancho / 2);
            }
        }

        return deMuro > 0 ? deMuro : deTrabe;
    }

    private static double Positivo(double v, double omision) => v > 0 ? v : omision;
}
