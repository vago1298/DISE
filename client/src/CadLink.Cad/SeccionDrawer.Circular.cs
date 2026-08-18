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
///     <b>No hay gancho sísmico en la esquina.</b> Un zuncho circular no tiene
///     esquinas donde doblar: se traslapa. La columna T se ignora aquí, y el rótulo
///     no la menciona.
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

        var l1 = Linea(destino.X, destino.Y + rVar, xCodo, yCodo, "ROTULOS");
        var l2 = Linea(xCodo, yCodo, xCodo + (0.05 * _f), yCodo, "ROTULOS");

        Rotulado(l1);
        Rotulado(l2);

        FlechaTriangular(destino.X, destino.Y + rVar, haciaArriba: false);
        TextoLeader(xCodo + (0.06 * _f), yCodo, texto);
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
    /// </remarks>
    private void CotaDelDiametro(double cx, double cy, double r)
    {
        ConfigurarCotas();

        try
        {
            AcadConnection.Retry(() =>
            {
                // Los dos extremos de un diámetro a 45°, para que la cota no se
                // monte sobre la llamada de las varillas, que sale por arriba.
                var a = Pi / 4;

                dynamic dd = _ms.AddDimDiametric(
                    new[] { cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a)), 0d },
                    new[] { cx - (r * Math.Cos(a)), cy - (r * Math.Sin(a)), 0d },
                    0.5 * r);

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
                dynamic dh = _ms.AddDimRotated(
                    new[] { cx - r, cy, 0d },
                    new[] { cx + r, cy, 0d },
                    new[] { cx, cy + r + (3 * 0.02 * _f), 0d },
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
}
