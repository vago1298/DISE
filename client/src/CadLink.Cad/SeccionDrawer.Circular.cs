namespace CadLink.Cad;

/// <summary>
/// Sección de concreto <b>circular</b>: la parte de <see cref="SeccionDrawer"/> que
/// dibuja columnas redondas con zuncho.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto no está en la macro.</b> La hoja original solo sabe de secciones
/// rectangulares, y el usuario pidió poder marcar una fila como circular sin que las
/// demás cambien. Así que aquí no hay port: hay geometría nueva, y por eso está
/// comprobada aparte, número a número, en
/// <c>tools/verificar_seccion_circular.py</c>.
/// </para>
/// <para>
/// <b>Las cuatro diferencias de fondo con la sección rectangular</b>, que son la
/// razón de que esto viva en su propio archivo y no como una rama dentro de
/// <c>Dibujar</c>:
/// </para>
/// <list type="number">
///   <item>
///     <b>No hay lechos.</b> El acero longitudinal se reparte por igual en un solo
///     <i>círculo de paso</i>, así que se captura como un total. Un lecho superior y
///     otro inferior no significan nada en un círculo, y repartir N varillas entre
///     dos lechos no tiene una respuesta única.
///   </item>
///   <item>
///     <b>No es un estribo, es un zuncho.</b> En la sección se ve igual —una corona—,
///     pero en el alzado sube en hélice o en anillos, y eso lo decide el usuario.
///   </item>
///   <item>
///     <b>El gancho sísmico no va en una esquina, va sobre una varilla.</b> Aquí no
///     hay esquinas donde doblar, y durante un tiempo eso se tomó como que un zuncho
///     circular no lleva gancho y la columna T se podía ignorar. Es falso: lo que
///     ancla un zuncho —igual que un estribo— es el <b>doblez a 135° alrededor de una
///     varilla longitudinal</b> con la cola metida en el núcleo, y la esquina del
///     rectángulo solo era el sitio donde esa varilla estaba. En el círculo la varilla
///     está en el círculo de paso, así que el gancho se dibuja ahí. Ver
///     <see cref="GanchoDelZuncho"/>.
///   </item>
///   <item>
///     <b>El hatch se recorta contra coronas</b>, no contra rectángulos con
///     esquinas redondeadas. Un círculo es una frontera de hatch perfectamente
///     válida en AutoCAD, así que sale más simple que en el rectángulo.
///   </item>
/// </list>
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>
    /// Ángulo de arranque del reparto de varillas: <b>arriba</b>.
    /// </summary>
    /// <remarks>
    /// Arrancar en las 12 en punto y girar en sentido antihorario hace que con 4
    /// varillas queden a las 12, 3, 6 y 9, que es como se arma y como se espera ver
    /// en el plano. Arrancando en el eje X quedarían giradas 45° y el plano se vería
    /// torcido sin motivo.
    /// </remarks>
    private const double AnguloPrimeraVarilla = Pi / 2;

    /// <summary>Mínimo de varillas para que el reparto circular tenga sentido.</summary>
    private const int MinVarillasCirculo = 3;

    /// <summary>
    /// Dibuja una sección circular completa y la agrupa en su bloque.
    /// </summary>
    /// <param name="inicio">Índice de entidad desde el que empezó esta sección.</param>
    /// <param name="destino">Dónde devolverla si se está redibujando.</param>
    /// <returns>Cuántas entidades se crearon.</returns>
    private int DibujarCircular(
        SeccionCad s, double xIzquierda, double yAbajo,
        int inicio, double[]? destino, bool conFondoSolido)
    {
        var d = s.DiametroCm * _escala;
        var r = d / 2;
        var rec = s.RecubrimientoCm * _escala;
        var dZun = s.Estribo.Cm * _escala;

        if (r <= 0)
        {
            _log.Add($"Sección circular '{s.Id}': el diámetro no es válido.");
            return 0;
        }

        // El centro. Se recibe la esquina inferior izquierda del hueco que ocupa la
        // sección, igual que en la rectangular, para que la fila de secciones se
        // acomode con la MISMA aritmética y no haya dos formas de avanzar la X.
        var cx = xIzquierda + r;
        var cy = yAbajo + r;

        // ---------- Concreto ----------
        var plConcreto = CirculoEn(cx, cy, r, "CONCRETO");

        // ---------- Zuncho ----------
        var contorno = new List<object>();

        var rZunExt = r - rec;
        var rZunInt = rZunExt - dZun;

        var hayZuncho = dZun > 0 && rZunInt > 0;

        object? zunExt = null;
        object? zunInt = null;

        if (hayZuncho)
        {
            zunExt = CirculoEn(cx, cy, rZunExt, "ESTRIBOS");
            zunInt = CirculoEn(cx, cy, rZunInt, "ESTRIBOS");

            if (zunExt is not null) { contorno.Add(zunExt); }
            if (zunInt is not null) { contorno.Add(zunInt); }

            // Relleno del cuerpo del zuncho: la corona entre los dos círculos. Se
            // hace con el círculo interior como ISLA, que es lo que deja la banda de
            // acero en lugar de un disco macizo.
            if (conFondoSolido && zunExt is not null && zunInt is not null)
            {
                var relleno = Hatch(
                    "SOLID", 1, zunExt, new List<object> { zunInt },
                    "ESTRIBOS", ColorRellenoEstribo);

                if (relleno is not null)
                {
                    AlFondo(new List<object> { relleno });
                }
            }
        }
        else if (dZun > 0)
        {
            // Se avisa y se sigue: una sección sin zuncho es un dibujo incompleto,
            // pero es mejor que ninguna sección y sin explicación.
            _log.Add(
                $"Sección circular '{s.Id}': con diámetro {s.DiametroCm:0.#} cm, " +
                $"recubrimiento {s.RecubrimientoCm:0.#} cm y zuncho {s.Estribo.Clave} " +
                "no queda sitio para el zuncho, así que no se dibujó.");
        }

        // ---------- Varillas ----------
        var circulos = new List<object>();
        var posiciones = PosicionesCirculares(s, cx, cy, r, rec, dZun);

        var claveVar = s.VarTotal.Existe ? s.VarTotal.Clave : s.Estribo.Clave;
        var rVar = s.VarTotal.Existe
            ? s.VarTotal.Cm * _escala / 2
            : s.Estribo.Cm * _escala / 2;

        foreach (var (vx, vy) in posiciones)
        {
            var c = Varilla(vx, vy, rVar, claveVar);

            if (c is not null)
            {
                circulos.Add(c);

                // Se registran igual que en la rectangular. Hoy solo las usa el
                // estribo diamante, que en circular no aplica, pero dejarlas vacías
                // haría que una sección circular seguida de una rectangular con
                // diamante se abrazara a las varillas de la anterior.
                _varSup.Add((vx, vy, rVar));
            }
        }

        var rellenosVarilla = new List<object>();
        RellenarVarillas(circulos, rellenosVarilla);

        // ---------- Gancho sísmico del zuncho ----------
        // Va DESPUÉS de las varillas porque se abraza a una de ellas, exactamente por
        // el mismo motivo que el estribo diamante en la rectangular: necesita saber
        // dónde quedaron.
        var ganchoQuads = new List<double[]>();
        var ganchoSectores = new List<double[]>();

        if (hayZuncho)
        {
            GanchoDelZuncho(
                s, contorno, ganchoQuads, ganchoSectores,
                cx, cy, posiciones, rVar, dZun, rZunInt);
        }

        // ---------- Hatch de concreto, en dos partes ----------
        // Igual que en la rectangular: primero el recubrimiento, entre la cara del
        // concreto y el zuncho, y después el núcleo, con las varillas como islas. Se
        // parte en dos por la misma razón: un solo hatch con el zuncho como isla
        // dejaría el interior sin rayar.
        if (plConcreto is not null)
        {
            var creados = new List<object>();

            if (hayZuncho && zunExt is not null && zunInt is not null)
            {
                ParteHatch(plConcreto, new List<object> { zunExt }, creados, conFondoSolido);
                ParteHatch(zunInt, circulos, creados, conFondoSolido);
            }
            else
            {
                // Sin zuncho, el disco entero con las varillas como islas
                ParteHatch(plConcreto, circulos, creados, conFondoSolido);
            }

            if (creados.Count > 0)
            {
                AlFondo(creados);
            }
        }

        // ---------- Relleno del gancho ----------
        // El cuerpo del zuncho ya se rellenó arriba con la corona. El gancho sobresale
        // de ella —el doblez rodea la varilla y la cola entra al núcleo—, así que sus
        // dos piezas se rellenan aparte, igual que hace RellenoEstribo en la
        // rectangular.
        if (conFondoSolido && (ganchoSectores.Count > 0 || ganchoQuads.Count > 0))
        {
            RellenoDelGancho(ganchoQuads, ganchoSectores);
        }

        // ---------- Contornos en negro, solo en la sección rellena ----------
        if (conFondoSolido)
        {
            if (plConcreto is not null)
            {
                Negro(plConcreto);
            }

            foreach (var ent in contorno)
            {
                Negro(ent);
            }

            foreach (var circulo in circulos)
            {
                Negro(circulo);
            }
        }

        // ---------- Zuncho al frente, y las varillas encima ----------
        if (contorno.Count > 0)
        {
            AlFrente(contorno);
        }

        var varillas = new List<object>(rellenosVarilla);
        varillas.AddRange(circulos);
        AlFrente(varillas);

        // ---------- Llamada del círculo de varillas ----------
        LlamadaDelCirculo(s, cx, cy, posiciones, rVar);

        // ---------- Cota del diámetro ----------
        CotaDelDiametro(cx, cy, r);

        // ---------- Rótulo ----------
        // El mismo de siempre: ya sabe que en circular hay un solo grupo de varillas
        // y que el transversal es un zuncho.
        Rotulo(s, cx, yAbajo - (0.06 * _f));

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        if (!string.IsNullOrWhiteSpace(s.Id))
        {
            Bloquear(s.Id, inicio, fin, destino);
        }

        return fin - inicio;
    }

    /// <summary>
    /// Centros de las varillas del círculo de paso, repartidas por igual.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El <b>círculo de paso</b> es donde van los <i>centros</i>, y su radio sale de
    /// restar, desde el borde del concreto: el recubrimiento, el diámetro del zuncho
    /// y el <b>radio</b> de la varilla. Ese último medio diámetro es el que se
    /// olvida, y olvidarlo deja la varilla mordiendo el recubrimiento.
    /// </para>
    /// <para>
    /// Comprobado en <c>tools/verificar_seccion_circular.py</c>: con D=50, rec=4,
    /// zuncho #3 y varilla #8 el radio de paso sale 0.1878 m y el borde exterior de
    /// la varilla queda justo en el límite interior del zuncho.
    /// </para>
    /// </remarks>
    private List<(double X, double Y)> PosicionesCirculares(
        SeccionCad s, double cx, double cy, double r, double rec, double dZun)
    {
        var salida = new List<(double X, double Y)>();

        var n = s.NVarTotal;

        if (n <= 0)
        {
            return salida;
        }

        if (n < MinVarillasCirculo)
        {
            _log.Add(
                $"Sección circular '{s.Id}': {n} varilla(s) no forman un círculo. " +
                $"Se dibujan igual, pero el mínimo práctico es {MinVarillasCirculo}.");
        }

        var dVar = s.VarTotal.Existe ? s.VarTotal.Cm * _escala : dZun;
        var rPaso = r - rec - dZun - (dVar / 2);

        if (rPaso <= 0)
        {
            _log.Add(
                $"Sección circular '{s.Id}': no queda sitio para las varillas dentro " +
                "del zuncho, así que no se dibujaron.");
            return salida;
        }

        // Aviso de traslape. No se deja de dibujar: el usuario tiene que VER que se
        // pisan, y ya se le avisó en «Revisar datos» antes de llegar aquí.
        var cuerda = 2 * rPaso * Math.Sin(Pi / n);

        if (cuerda < dVar)
        {
            _log.Add(
                $"Sección circular '{s.Id}': las {n} varillas se traslapan " +
                $"{(dVar - cuerda) / _escala:0.#} cm. Se dibujaron igual, para que se " +
                "vea el problema.");
        }

        for (var i = 0; i < n; i++)
        {
            var a = AnguloPrimeraVarilla + (i * 2 * Pi / n);
            salida.Add((cx + (rPaso * Math.Cos(a)), cy + (rPaso * Math.Sin(a))));
        }

        return salida;
    }

    /// <summary>
    /// Llamada del círculo de varillas: una flecha a una varilla y el texto
    /// <c>N vars. #X C</c>.
    /// </summary>
    /// <remarks>
    /// Es <b>una sola</b>, no una por varilla. En un círculo todas las varillas son
    /// el mismo grupo, con el mismo diámetro y la misma función, así que N llamadas
    /// repetirían N veces el mismo texto y taparían la sección. Se apunta a la
    /// varilla de arriba, que es la que queda libre de la cota del diámetro.
    /// </remarks>
    private void LlamadaDelCirculo(
        SeccionCad s, double cx, double cy,
        List<(double X, double Y)> posiciones, double rVar)
    {
        if (posiciones.Count == 0)
        {
            return;
        }

        var clave = s.VarTotal.Existe ? s.VarTotal.Clave : s.Estribo.Clave;

        if (string.IsNullOrWhiteSpace(clave))
        {
            return;
        }

        // La varilla más alta: es la primera del reparto, pero se busca por Y para
        // no depender de eso.
        var destino = posiciones[0];

        foreach (var p in posiciones)
        {
            if (p.Y > destino.Y)
            {
                destino = p;
            }
        }

        var texto = $"{posiciones.Count} vars. {clave}C";

        // La llamada sale hacia arriba y a la derecha, hasta fuera del círculo.
        var xCodo = cx + (1.15 * (destino.X - cx)) + (0.10 * _f);
        var yCodo = destino.Y + (0.08 * _f);

        // El tramo horizontal del codo: la repisa sobre la que se apoya el texto.
        var xRepisa = xCodo + (0.05 * _f);

        var l1 = Linea(destino.X, destino.Y + rVar, xCodo, yCodo, "ROTULOS");
        var l2 = Linea(xCodo, yCodo, xRepisa, yCodo, "ROTULOS");

        Rotulado(l1);
        Rotulado(l2);

        FlechaTriangular(destino.X, destino.Y + rVar, haciaArriba: false);

        // El texto arranca DONDE TERMINA la repisa y crece hacia la derecha, así que
        // la línea le sale por el lado IZQUIERDO. Antes se anclaba por la derecha y
        // el texto se extendía hacia atrás sobre su propia línea de llamada: en el
        // plano la línea parecía salir de la última letra.
        TextoLeader(xRepisa + (0.01 * _f), yCodo, texto, haciaLaDerecha: true);
    }

    /// <summary>
    /// Cota del diámetro de la sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se intenta primero con <c>AddDimDiametric</c>, que es la cota que corresponde
    /// a un círculo y sale rotulada con el símbolo Ø. Si esta versión de AutoCAD no
    /// la acepta, se cae a una cota lineal sobre el diámetro horizontal: en un
    /// círculo mide exactamente lo mismo, solo se lee sin la Ø.
    /// </para>
    /// <para>
    /// El respaldo no es paranoia: <c>AddDimDiametric</c> pide el punto de la cuerda
    /// y el opuesto <b>sobre la circunferencia</b>, y su comportamiento con la
    /// longitud del director cambia entre versiones.
    /// </para>
    /// <para>
    /// <b>Dónde se coloca, y por qué ahí.</b> Alrededor de la sección hay tres cosas
    /// que se reparten el sitio, y cada una ocupa un cuadrante:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     La <b>llamada de las varillas</b> sale de la varilla más alta hacia
    ///     <b>arriba y a la derecha</b>. Ver <see cref="LlamadaDelCirculo"/>.
    ///   </item>
    ///   <item>El <b>rótulo</b> de la sección va <b>abajo y centrado</b>.</item>
    ///   <item>
    ///     Así que a esta cota le toca <b>arriba y a la IZQUIERDA</b>, que es el
    ///     cuadrante que queda libre.
    ///   </item>
    /// </list>
    /// <para>
    /// Antes usaba 45°, o sea arriba y a la derecha, el mismo cuadrante que la llamada
    /// de las varillas, y el «Ø 50» acababa escrito encima del «8 vars. #8C». La
    /// separación no se arregla empujando la cota unos centímetros: se arregla
    /// mandándola a otro cuadrante, que es lo único que garantiza que no se encimen
    /// aunque cambie el diámetro o el número de varillas.
    /// </para>
    /// </remarks>
    private void CotaDelDiametro(double cx, double cy, double r)
    {
        ConfigurarCotas();

        // Arriba y a la IZQUIERDA. Ver el porqué en el comentario del método.
        const double AnguloCota = 3 * Pi / 4;

        // El director, más largo que el radio: saca el texto fuera del círculo y de
        // paso lo aleja del rótulo de abajo.
        var director = 0.65 * r;

        try
        {
            AcadConnection.Retry(() =>
            {
                // AddDimDiametric pone el texto del lado del PRIMER punto, al final
                // del director. Por eso el primer punto es el del cuadrante libre.
                dynamic dd = _ms.AddDimDiametric(
                    new[]
                    {
                        cx + (r * Math.Cos(AnguloCota)),
                        cy + (r * Math.Sin(AnguloCota)),
                        0d
                    },
                    new[]
                    {
                        cx - (r * Math.Cos(AnguloCota)),
                        cy - (r * Math.Sin(AnguloCota)),
                        0d
                    },
                    director);

                FormatearCota(dd);
            });

            return;
        }
        catch (Exception ex)
        {
            Nota(
                "No se pudo poner la cota de diámetro de la sección circular; se usa " +
                "una cota lineal sobre el diámetro. " + ex.Message);
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                // El respaldo va por ENCIMA de la llamada de las varillas, no entre
                // ella y el círculo. La llamada sube 0.08 desde la varilla más alta,
                // que está dentro del zuncho, así que 0.22 sobre el paño del concreto
                // la deja despejada con holgura.
                dynamic dh = _ms.AddDimRotated(
                    new[] { cx - r, cy, 0d },
                    new[] { cx + r, cy, 0d },
                    new[] { cx, cy + r + (0.22 * _f), 0d },
                    0d);

                FormatearCota(dh);
            });
        }
        catch (Exception ex)
        {
            // Sin cota el dibujo sigue siendo valido.
            Fallo("Cota del diámetro de la sección circular", ex);
        }
    }

    /// <summary>Un círculo en la capa que se le diga.</summary>
    /// <remarks>
    /// No se reutiliza <see cref="Varilla"/> porque ese fuerza la capa
    /// <c>VAR_&lt;clave&gt;</c>, que es justo lo que aquí no se quiere: el concreto va
    /// en CONCRETO y el zuncho en ESTRIBOS.
    /// </remarks>
    private object? CirculoEn(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _ms.AddCircle(new[] { cx, cy, 0d }, radio);
                c.Layer = capa;
                c.Color = PorCapa;
                return (object?)c;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Círculo de la sección en la capa '{capa}'", ex);
            return null;
        }
    }

    // ==================================================================
    //  Gancho sísmico del zuncho
    // ==================================================================
    //
    // Va al final del archivo, y no junto al dibujo del zuncho, porque necesita las
    // varillas ya colocadas.

    /// <summary>
    /// El <b>gancho sísmico</b> del zuncho: doblez a 135° sobre una varilla y cola
    /// hacia el núcleo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es el mismo gancho del estribo rectangular</b>, y por eso reutiliza su
    /// <see cref="Cola"/> en lugar de repetir la geometría. Lo que cambia es solo
    /// dónde está la varilla que se abraza: en el rectángulo es la de la esquina, y en
    /// el círculo es una del círculo de paso.
    /// </para>
    /// <para>
    /// <b>El doblez sale tangente sin que haya que forzarlo</b>, y esa es la
    /// comprobación de que el planteamiento es correcto. El eje del zuncho rodeando la
    /// varilla queda a <c>rVar + dZun/2</c> de su centro; y la distancia entre el
    /// centro de la varilla y el eje del zuncho es
    /// <c>rEje − rPaso = (r − rec − dZun/2) − (r − rec − dZun − dVar/2)</c>, que
    /// simplificando es <c>dVar/2 + dZun/2</c>. Los dos números son <b>el mismo</b>,
    /// así que el zuncho envuelve la varilla sin escalón. Comprobado al bit en
    /// <c>tools/verificar_seccion_circular.py</c>.
    /// </para>
    /// <para>
    /// Y por lo mismo la cara exterior del doblez queda tangente al círculo exterior
    /// del zuncho: <c>rPaso + rVar + dZun = r − rec = rZunExt</c>. O sea que el gancho
    /// <b>no sobresale</b> del zuncho por fuera, ni muerde el recubrimiento.
    /// </para>
    /// <para>
    /// <b>Las direcciones se deducen, no se escriben a mano.</b> En la rectangular
    /// están puestas como constantes —cola <c>(−1/√2, −1/√2)</c> y normales
    /// <c>±(1/√2, −1/√2)</c>— porque allí la esquina siempre es la misma. Aquí la
    /// varilla puede estar en cualquier ángulo, así que:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     La cola apunta al núcleo: se toma el radio <b>hacia dentro</b> y se gira
    ///     45°, que es lo que hace que el gancho entre en diagonal y no de plano. Así
    ///     el producto escalar con el radio interior vale siempre <c>cos 45°</c>, o
    ///     sea que la cola <b>nunca</b> puede apuntar hacia fuera.
    ///   </item>
    ///   <item>
    ///     Las dos normales de arranque son las <b>perpendiculares a la cola</b>, igual
    ///     que en la rectangular.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Y girar el radio 45° es girar el avance 135°</b>, que es la definición del
    /// gancho. Sale gratis: el radio ya está a 90° de la tangente, así que 90 + 45 son
    /// los 135 de la norma. Comprobado para todos los ángulos de varilla.
    /// </para>
    /// <para>
    /// <b>Cuidado con una comparación que parece obvia y es falsa.</b> Uno esperaría
    /// que esta regla diera los mismos números que la rectangular, y no los da: allí
    /// el estribo corre <b>paralelo a la cara</b> y aquí el zuncho corre <b>tangente</b>,
    /// así que la dirección de avance es distinta y la cola también. Lo que comparten
    /// es el ángulo, no las constantes. Si se aplica «gira el avance 135° hacia el
    /// núcleo» a la pata superior del estribo rectangular, que corre en <c>+x</c>, sale
    /// su <c>(−1/√2, −1/√2)</c> exacto; es esa comprobación, y no la de los números
    /// sueltos, la que está en el script.
    /// </para>
    /// <para>
    /// <b>Cuántas colas.</b> Dos si el zuncho va en <b>anillos</b>, porque cada anillo
    /// es cerrado y sus dos extremos se juntan sobre la misma varilla, igual que en el
    /// estribo rectangular. Una sola si va en <b>hélice</b>: una espiral es una sola
    /// barra continua y solo tiene un arranque, así que dibujar dos ganchos diría que
    /// hay dos barras donde hay una.
    /// </para>
    /// </remarks>
    /// <param name="contorno">Donde se acumulan las líneas, para pintarlas de negro.</param>
    /// <param name="quads">Cuadriláteros de las colas, para rellenarlas.</param>
    /// <param name="sectores">Sectores anulares de los dobleces, para rellenarlos.</param>
    /// <param name="rVar">El <b>radio</b> de la varilla, no su diámetro.</param>
    /// <param name="rZunInt">Radio interior del zuncho, para saber dónde está el núcleo.</param>
    private void GanchoDelZuncho(
        SeccionCad s, List<object> contorno,
        List<double[]> quads, List<double[]> sectores,
        double cx, double cy, List<(double X, double Y)> posiciones,
        double rVar, double dZun, double rZunInt)
    {
        var gancho = s.GanchoCm * _escala;

        // Mismo criterio que la sección rectangular: la columna T tal cual, sin el
        // 12·db, que es regla del alzado. Cero significa sin gancho.
        if (gancho <= 0)
        {
            return;
        }

        if (posiciones.Count == 0)
        {
            // Sin varillas no hay de dónde agarrarse. Un zuncho sin nada que abrazar
            // no se ancla, y dibujar el doblez en el aire sería mentir.
            _log.Add(
                $"Sección circular '{s.Id}': el zuncho no lleva gancho porque no hay " +
                "varillas longitudinales a las que agarrarlo.");
            return;
        }

        // La varilla de ABAJO. La llamada de varillas apunta a la de arriba
        // (LlamadaDelCirculo), así que poniendo el gancho en la de abajo la flecha y el
        // doblez no se pisan.
        var barra = posiciones[0];

        foreach (var p in posiciones)
        {
            if (p.Y < barra.Y)
            {
                barra = p;
            }
        }

        var bx = barra.X;
        var by = barra.Y;

        // Radio HACIA DENTRO, normalizado. Se saca del vector varilla->centro en vez
        // del ángulo, que ahorra un atan2 y un par de trigonométricas.
        var rx = cx - bx;
        var ry = cy - by;
        var rl = Math.Sqrt((rx * rx) + (ry * ry));

        if (rl < 1e-9)
        {
            // La varilla está en el centro: no hay dirección «hacia dentro».
            return;
        }

        rx /= rl;
        ry /= rl;

        // La cola: el radio interior girado 45°.
        var ux = (rx - ry) * Rt2I;
        var uy = (rx + ry) * Rt2I;

        // Las normales de arranque: las perpendiculares a la cola.
        var n1X = -uy;
        var n1Y = ux;
        var n2X = uy;
        var n2Y = -ux;

        var rIn = rVar;
        var rOut = rVar + dZun;

        // La cola no puede pasarse del núcleo. Apunta hacia dentro, así que cuanto más
        // larga más se acerca al centro… hasta que lo cruza y empieza a salir por el
        // otro lado. El tope es la proyección del vector arranque->centro sobre la
        // propia cola, que es justo donde la punta queda lo más cerca posible del eje.
        var piX = bx + (rIn * n1X);
        var piY = by + (rIn * n1Y);

        var tope = ((cx - piX) * ux) + ((cy - piY) * uy);

        if (tope > 0 && gancho > tope)
        {
            _log.Add(
                $"Sección circular '{s.Id}': el gancho de {s.GanchoCm:0.#} cm no cabe " +
                $"en el núcleo y se recortó a {tope / _escala:0.#} cm. Con un diámetro " +
                $"de {s.DiametroCm:0.#} cm la cola llegaría al otro lado.");
            gancho = tope;
        }

        // ------------------------------------------------------------------
        // El DOBLEZ que abraza la varilla
        // ------------------------------------------------------------------
        // Media corona, del arranque de una cola al de la otra. El barrido va en sentido
        // antihorario de n1 a n2, y su punto medio cae en -u, o sea en el lado OPUESTO a
        // las colas: el doblez rodea la cara de atrás de la varilla y las colas salen
        // por delante, que es como se dobla de verdad.
        var a1 = Math.Atan2(n1Y, n1X);

        // Para el RELLENO de la sección tipo 2.
        sectores.Add(new[] { bx, by, rIn, rOut, a1, a1 + Pi });

        // ------------------------------------------------------------------
        // Y los dos ARCOS del contorno, cada uno con su propio recorte
        // ------------------------------------------------------------------
        // Aquí estaba el defecto que se veía en el plano: una línea cruzando por dentro
        // de la banda del zuncho, que delataba que el gancho es una pieza pegada encima
        // en lugar de una continuación del zuncho. El estribo rectangular no tiene ese
        // problema porque allí el doblez lo dibuja el estribo mismo, con los arcos de esa
        // esquina barridos de más y su línea interior recortada; el zuncho circular es un
        // círculo completo y no puede abrirse, porque además hace de frontera del hatch
        // de concreto.
        //
        // La solución es la de siempre en dibujo técnico: **no dibujar lo que queda
        // tapado**. Y los dos arcos NO están en la misma situación, que es lo que había
        // que mirar:
        //
        //   * El arco INTERIOR, de radio rVar, es TANGENTE al borde del núcleo. Sale de
        //     la propia aritmética: rPaso + rVar = r − rec − dZun = rZunInt, exacto. Así
        //     que cae entero dentro del núcleo, no tapa nada y se dibuja COMPLETO.
        //
        //   * El arco EXTERIOR, de radio rVar + dZun, llega hasta rZunExt —otra igualdad
        //     exacta— o sea que ATRAVIESA la banda de lado a lado. Su tramo dentro de la
        //     banda es el que sobra, y con los números de una columna de 50 cm son 116°
        //     centrados en la dirección que va del centro de la sección a la varilla.
        //
        // Comprobado al bit en tools/verificar_seccion_circular.py.
        //
        // ------------------------------------------------------------------
        // Y LOS DOS ARRANCAN EN LA TANGENCIA, no en el borde de la cola
        // ------------------------------------------------------------------
        // Este fue el ultimo ajuste, y es el que hace que el gancho se lea como una
        // CONTINUACION del zuncho y no como una pieza pegada:
        //
        //   * El arco EXTERIOR sigue hasta hacerse TANGENTE a la banda. La tangencia cae
        //     exactamente en la direccion centro->varilla, porque rPaso + rOut = rZunExt:
        //     ahi el doblez roza el pano exterior del zuncho y los dos se funden. Antes lo
        //     cortaba donde entraba en la banda, y quedaba un tajo plano a media vuelta.
        //
        //   * El arco INTERIOR se recorta SOLO por ese lado, el derecho. Su otro extremo
        //     llega hasta donde sale la segunda cola, sin tocar.
        //
        // O sea que el barrido de los dos va de la tangencia a la salida de la cola, y no
        // del borde de una cola al de la otra como estaba antes.
        var aTangente = Math.Atan2(ry * -1, rx * -1);

        Agregar(contorno, Arco(bx, by, rIn, aTangente, a1 + Pi));
        Agregar(contorno, Arco(bx, by, rOut, aTangente, a1 + Pi));

        // ------------------------------------------------------------------
        // Las DOS colas, una a cada lado de la varilla
        // ------------------------------------------------------------------
        // Van siempre las dos, igual que en el estribo rectangular. Durante un tiempo
        // aquí se dibujaba UNA sola cuando el zuncho era helicoidal, con el argumento de
        // que una espiral es una barra continua con un solo arranque. Es cierto como
        // descripción de la barra, pero <b>no es el detalle que se dibuja</b>: el remate
        // de un zuncho —espiral o anillo— se representa con sus dos ganchos, uno encima
        // del otro y con el de dentro recortado, y así se lee en el plano tanto en la
        // sección de contorno como en la rellena.
        foreach (var (nx, ny) in new[] { (n1X, n1Y), (n2X, n2Y) })
        {
            // El RECORTE. La cara exterior de la cola arranca en el borde radial del
            // doblez, y ese punto puede caer dentro de la banda del zuncho: entonces la
            // cola y el zuncho se solapan y entre las dos caras queda una cuña. El
            // rectangular resuelve lo mismo arrancando la cara exterior sobre la línea
            // interior del estribo; aquí esa línea es el círculo interior del zuncho.
            var poX = bx + (rOut * nx);
            var poY = by + (rOut * ny);

            var recorte = CruceConElNucleo(poX, poY, ux, uy, cx, cy, rZunInt, gancho);

            Cola(contorno, quads, bx, by, rIn, rOut, nx, ny, ux, uy, gancho,
                recorte is not null, recorte?.X ?? 0, recorte?.Y ?? 0);
        }

        // El núcleo solo se usa para el aviso: si la varilla que se abraza estuviera
        // fuera del zuncho, el gancho no tendría sentido.
        if (rl > rZunInt + rOut)
        {
            _log.Add(
                $"Sección circular '{s.Id}': la varilla del gancho queda muy adentro " +
                "del núcleo. Revisa el recubrimiento y el diámetro del zuncho.");
        }
    }

    /// <summary>
    /// Dónde la cara exterior de una cola <b>entra en el núcleo</b>, o <c>null</c> si no
    /// lo hace dentro de su longitud.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el equivalente circular del recorte del estribo rectangular. Allí la cara
    /// exterior de la segunda cola arranca sobre la <b>línea interior recta</b> del
    /// estribo; aquí esa frontera es el <b>círculo interior</b> del zuncho, así que hay
    /// que resolver la intersección de una recta con una circunferencia en lugar de
    /// leer una coordenada.
    /// </para>
    /// <para>
    /// Se busca el cruce <b>hacia delante</b>: la cola nace en la banda del zuncho y va
    /// hacia el núcleo, así que el corte que interesa es el primero con
    /// <c>t &gt;= 0</c>. Si sale de la longitud del gancho, no hay recorte: la cola es
    /// tan corta que muere dentro de la banda.
    /// </para>
    /// </remarks>
    private static (double X, double Y)? CruceConElNucleo(
        double px, double py, double ux, double uy,
        double cx, double cy, double radio, double largo)
    {
        if (radio <= 0)
        {
            return null;
        }

        // |p + t*u - c|^2 = radio^2, con u unitario, asi que a = 1.
        var dx = px - cx;
        var dy = py - cy;

        var b = 2 * ((dx * ux) + (dy * uy));
        var c = (dx * dx) + (dy * dy) - (radio * radio);

        var disc = (b * b) - (4 * c);

        if (disc < 0)
        {
            // La cola no llega a cruzar el circulo del nucleo en ninguna direccion.
            return null;
        }

        var raiz = Math.Sqrt(disc);

        // Las dos soluciones, de menor a mayor.
        var t1 = (-b - raiz) / 2;
        var t2 = (-b + raiz) / 2;

        var t = t1 >= 0 ? t1 : t2;

        if (t < 0 || t > largo)
        {
            return null;
        }

        return (px + (t * ux), py + (t * uy));
    }

    /// <summary>
    /// Rellena las dos piezas del gancho: el doblez y las colas.
    /// </summary>
    /// <remarks>
    /// Es el paso 2 y 3 de <see cref="RellenoEstribo"/>, sin el paso 1: el cuerpo del
    /// zuncho circular ya se rellenó con la corona entre los dos círculos, que es una
    /// frontera mucho más simple que la del estribo rectangular.
    /// <para>
    /// Las polilíneas de frontera se <b>borran</b> al terminar. Los hatches son no
    /// asociativos, así que no les afecta, y si se dejaran quedarían dos contornos
    /// sueltos encima del acero.
    /// </para>
    /// </remarks>
    private void RellenoDelGancho(List<double[]> quads, List<double[]> sectores)
    {
        var creados = new List<object>();
        var temporales = new List<object>();

        try
        {
            foreach (var s in sectores)
            {
                var pl = SectorAnular(s[0], s[1], s[2], s[3], s[4], s[5]);

                if (pl is null)
                {
                    continue;
                }

                temporales.Add(pl);

                var hs = Hatch("SOLID", 1, pl, null, "ESTRIBOS", ColorRellenoEstribo);

                if (hs is not null)
                {
                    creados.Add(hs);
                }
            }

            foreach (var q in quads)
            {
                var pl = PolyCerrada(q);

                if (pl is null)
                {
                    continue;
                }

                temporales.Add(pl);

                var hq = Hatch("SOLID", 1, pl, null, "ESTRIBOS", ColorRellenoEstribo);

                if (hq is not null)
                {
                    creados.Add(hq);
                }
            }

            if (creados.Count > 0)
            {
                AlFondo(creados);
            }
        }
        catch (Exception ex)
        {
            // El relleno es decorativo: el contorno del gancho ya está dibujado.
            Fallo("Relleno del gancho del zuncho", ex);
        }
        finally
        {
            foreach (var t in temporales)
            {
                Borrar(t);
            }
        }
    }

    // ==================================================================
    //  Llamadas junto al bloque de sección que inserta el alzado
    // ==================================================================

    /// <summary>
    /// Rehace las <b>llamadas de las varillas</b> junto a un bloque de sección ya
    /// insertado, por ejemplo el que el alzado pone a un lado o debajo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué hace falta.</b> Las llamadas no viajan dentro del bloque, y es a
    /// propósito: <see cref="Bloquear"/> salta todo lo que esté en las capas
    /// <c>COTAS</c> y <c>ROTULOS</c>, porque si entraran, el origen del bloque —que es
    /// el centro de su caja envolvente— quedaría descentrado, y además dejarían de
    /// poder editarse por capa. Así que se quedan sueltas en el espacio modelo, donde se
    /// dibujó la sección.
    /// </para>
    /// <para>
    /// La consecuencia es la que se ve en el plano: en la fila de secciones las
    /// llamadas están, y el bloque que el alzado inserta a su lado llega
    /// <b>pelado</b> —solo concreto, acero y rayado— con nada más que su
    /// <c>CORTE A-A'</c>. El usuario lo pidió al revés: que el bloque insertado lleve
    /// sus llamadas.
    /// </para>
    /// <para>
    /// <b>Y no se arregla metiéndolas en el bloque</b>, por las dos razones de arriba.
    /// Se arregla <b>volviéndolas a dibujar</b> en el espacio modelo junto al bloque
    /// insertado, que es exactamente lo que ya hacen el <c>CORTE A-A'</c> y el rótulo
    /// del alzado.
    /// </para>
    /// <para>
    /// <b>Las coordenadas salen gratis.</b> La caja del bloque solo contiene geometría
    /// —el rotulado y las cotas se quedaron fuera— y su polilínea de concreto es la más
    /// externa, así que después de que el alzado lo apoya por su paño inferior
    /// izquierdo, la esquina del concreto cae <b>exactamente</b> en
    /// <paramref name="xIzquierda"/>, <paramref name="yAbajo"/>. Son los mismos dos
    /// números que recibió <c>Dibujar</c> cuando dibujó la sección original, así que las
    /// fórmulas no cambian: se reutilizan tal cual.
    /// </para>
    /// </remarks>
    /// <param name="xIzquierda">Borde izquierdo del concreto del bloque insertado.</param>
    /// <param name="yAbajo">Paño inferior del concreto del bloque insertado.</param>
    public void LlamadasJuntoAlBloque(SeccionCad s, double xIzquierda, double yAbajo)
    {
        try
        {
            if (s.Circular)
            {
                LlamadasCirculoJuntoAlBloque(s, xIzquierda, yAbajo);
                return;
            }

            var b = s.BaseCm * _escala;
            var h = s.AlturaCm * _escala;

            if (b <= 0 || h <= 0)
            {
                return;
            }

            var rec = s.RecubrimientoCm * _escala;
            var dEst = s.Estribo.Cm * _escala;

            var dSup = s.Superior.Esquina.Cm * _escala;
            var dInf = s.Inferior.Esquina.Cm * _escala;
            if (dSup <= 0) { dSup = dEst; }
            if (dInf <= 0) { dInf = dEst; }

            // Las MISMAS posiciones que se usaron al dibujar la sección, sacadas del
            // cálculo puro para no volver a dibujar las varillas encima.
            var pSup = PosicionesDeLecho(s.Superior, xIzquierda, yAbajo, b, h, rec, dEst, true);
            var pInf = PosicionesDeLecho(s.Inferior, xIzquierda, yAbajo, b, h, rec, dEst, false);

            LeadersDeLecho(s.Superior, (pSup.Esquina, pSup.Intermedia, pSup.YGrupo),
                xIzquierda, arriba: true);
            LeadersDeLecho(s.Inferior, (pInf.Esquina, pInf.Intermedia, pInf.YGrupo),
                xIzquierda, arriba: false);

            if (s.NLateral > 0 && s.Lateral.Existe)
            {
                foreach (var (xIzq, xDer, y) in
                         PosicionesLaterales(s, xIzquierda, yAbajo, b, h, rec, dEst, dSup, dInf))
                {
                    LeaderVarilla(xIzq, y, 2, s.Lateral.Clave, xIzquierda);
                    LeaderVarilla(xDer, y, 2, s.Lateral.Clave, xIzquierda);
                }
            }
        }
        catch (Exception ex)
        {
            // Las llamadas son rotulado: si fallan, el corte sigue dibujado y medido.
            Fallo($"Llamadas del corte '{s.Id}' junto a su alzado", ex);
        }
    }

    /// <summary>La misma cosa para el corte circular: una sola llamada.</summary>
    /// <remarks>
    /// Aquí no hay lechos, así que se reutiliza <see cref="LlamadaDelCirculo"/> tal
    /// cual. El centro sale de la esquina igual que en <see cref="DibujarCircular"/>:
    /// <c>cx = xIzquierda + r</c>, <c>cy = yAbajo + r</c>.
    /// </remarks>
    private void LlamadasCirculoJuntoAlBloque(
        SeccionCad s, double xIzquierda, double yAbajo)
    {
        var r = s.DiametroCm * _escala / 2;

        if (r <= 0)
        {
            return;
        }

        var rec = s.RecubrimientoCm * _escala;
        var dZun = s.Estribo.Cm * _escala;

        var cx = xIzquierda + r;
        var cy = yAbajo + r;

        var posiciones = PosicionesCirculares(s, cx, cy, r, rec, dZun);

        if (posiciones.Count == 0)
        {
            return;
        }

        var rVar = s.VarTotal.Existe
            ? s.VarTotal.Cm * _escala / 2
            : s.Estribo.Cm * _escala / 2;

        LlamadaDelCirculo(s, cx, cy, posiciones, rVar);
    }

}
