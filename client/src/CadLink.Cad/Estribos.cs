namespace CadLink.Cad;

/// <summary>
/// Reparto de estribos a lo largo de un elemento, por zonas L/4 - L/2 - L/4.
/// </summary>
/// <remarks>
/// Port de <c>BuildStirrupCenters</c> y sus auxiliares. Se deja en su propia clase,
/// sin nada de COM, porque es aritmética pura y así se puede razonar y comprobar
/// aparte del dibujo.
/// </remarks>
public static class Estribos
{
    /// <summary>Retiro del primer y último estribo respecto al extremo.</summary>
    public const double BordeM = 0.05;

    /// <summary>Separación mínima entre estribos, centro a centro.</summary>
    public const double SepMinimaM = 0.05;

    /// <summary>Cuánto se puede recorrer el estribo de frontera entre zonas.</summary>
    public const double TolTransicionM = 0.06;

    /// <summary>Separación mínima admitida en los datos.</summary>
    public const double SepMinimaDatoM = 0.05;

    /// <summary>
    /// Posiciones de los estribos entre <paramref name="x0"/> y <paramref name="x1"/>.
    /// </summary>
    /// <param name="s1">Separación de la primera zona, L/4, en metros.</param>
    /// <param name="s2">Separación de la zona central, L/2.</param>
    /// <param name="s3">Separación de la última zona, L/4.</param>
    /// <param name="conExtremos">Si se ponen estribos justo en los extremos.</param>
    /// <param name="conFronteras">
    /// Si se pone un estribo en cada frontera entre zonas. Puede omitirse si no cabe
    /// respetando la separación mínima.
    /// </param>
    public static List<double> Centros(
        double x0, double x1, double s1, double s2, double s3,
        bool conExtremos, bool conFronteras)
    {
        var col = new List<double>();

        var ini = x0 + BordeM;
        var fin = x1 - BordeM;
        var largo = fin - ini;

        if (largo <= 0)
        {
            return col;
        }

        if (s1 <= 0) { s1 = 0.15; }
        if (s2 <= 0) { s2 = s1; }
        if (s3 <= 0) { s3 = s1; }

        s1 = Math.Max(s1, SepMinimaDatoM);
        s2 = Math.Max(s2, SepMinimaDatoM);
        s3 = Math.Max(s3, SepMinimaDatoM);

        var variable = Math.Abs(s1 - s2) > 1e-4 || Math.Abs(s2 - s3) > 1e-4;

        if (!variable)
        {
            // Separación única. La macro fuerza un mínimo de 3 estribos SIN volver a
            // mirar la separación resultante, y en un elemento muy corto eso da
            // estribos a 1.67 cm, que no existe en obra. Aquí se recorta el número
            // para que nunca baje del mínimo. En longitudes normales no cambia nada.
            //
            // El +1e-6 es imprescindible y NO es cosmética. En coma flotante
            // 0.90/0.10 da 8.9999999999999982, y el (int) lo truncaba a 8: un dado de
            // 1.00 m a 10 cm salía con 8 huecos de 11.25 cm en lugar de 9 de 10 cm.
            // Se perdía un estribo Y la separación dejaba de ser la de la tabla, que
            // es peor, porque el rótulo seguía diciendo "@10 cm".
            var n = (int)((largo / s1) + 1e-6);
            if (n < 3) { n = 3; }

            var maxPorSeparacion = (int)Math.Floor(largo / SepMinimaM);
            if (maxPorSeparacion >= 1 && n > maxPorSeparacion)
            {
                n = maxPorSeparacion;
            }

            if (n < 1) { n = 1; }

            var paso = largo / n;

            var desde = conExtremos ? 0 : 1;
            var hasta = conExtremos ? n : n - 1;

            for (var i = desde; i <= hasta; i++)
            {
                Unico(col, ini + (i * paso));
            }

            return col;
        }

        var z1 = largo * 0.25;
        var z2 = z1 + (largo * 0.5);

        // Posición del estribo que SIGUE a cada frontera, para dejarle su holgura
        var sig1 = ini + z1 + s2;
        if (z1 + s2 > z2 - 1e-4) { sig1 = ini + z2; }

        var sig2 = ini + z2 + s3;
        if (z2 + s3 > largo - 1e-4) { sig2 = fin; }

        if (conExtremos) { Unico(col, ini); }

        PorSeparacion(col, ini, 0, z1, s1);

        if (conFronteras) { Transicion(col, ini + z1, sig1, fin); }

        PorSeparacion(col, ini, z1, z2, s2);

        if (conFronteras) { Transicion(col, ini + z2, sig2, fin); }

        PorSeparacion(col, ini, z2, largo, s3);

        if (conExtremos) { Unico(col, fin); }

        return col;
    }

    /// <summary>Estribos dentro de una zona, a su separación.</summary>
    /// <remarks>
    /// <para>
    /// <b>Aquí estaba el hueco doble.</b> El filtro era <c>p &lt; hasta - 1e-4</c>, que
    /// descarta el estribo que cae <b>exactamente</b> en la frontera de zona. Y eso
    /// pasa en el caso más común de todos: cuando la zona es múltiplo exacto de la
    /// separación (L/4 con separación redonda, que es como se captura siempre).
    /// </para>
    /// <para>
    /// El efecto en el plano es que la zona pierde su último estribo y la zona
    /// siguiente empieza a una separación de la frontera, así que en la frontera
    /// queda un hueco de <b>dos separaciones</b>. En columna y dado no lo tapaba
    /// nadie, porque <c>Transicion</c> solo corre con <c>conFronteras</c>, que va en
    /// <c>false</c> en el alzado vertical. Dos huecos dobles por elemento: es la
    /// mitad de "no me da todos los estribos".
    /// </para>
    /// <para>
    /// Ahora el extremo de la zona <b>sí</b> se coloca. No hay riesgo de duplicarlo
    /// con el de la zona siguiente ni con <c>Transicion</c>: el primero lo filtra
    /// <see cref="ConSeparacion"/> y el segundo se autodescarta, porque su ventana
    /// <c>[lo,hi]</c> arranca en <c>col[^1] + SepMinimaM</c> y se cierra sola.
    /// </para>
    /// <para>
    /// El <c>+1e-6</c> del conteo es por coma flotante: <c>0.725/0.145</c> da
    /// <c>4.9999…</c> y el <c>(int)</c> lo truncaba a 4, perdiendo otro estribo.
    /// </para>
    /// </remarks>
    private static void PorSeparacion(
        List<double> col, double ini, double desde, double hasta, double sep)
    {
        var n = (int)(((hasta - desde) / sep) + 1e-6);
        if (n < 1) { n = 1; }

        for (var i = 1; i <= n; i++)
        {
            var p = desde + (i * sep);
            if (p <= hasta + 1e-6)
            {
                ConSeparacion(col, ini + p);
            }
        }
    }

    /// <summary>
    /// Estribo de frontera entre zonas.
    /// </summary>
    /// <remarks>
    /// Se coloca lo más cerca posible de su posición nominal, pero conservando la
    /// separación mínima con el estribo anterior y con el siguiente. Puede
    /// recorrerse hasta <see cref="TolTransicionM"/>; si ni así cabe, <b>se omite</b>
    /// y el estribo vecino hace de transición.
    /// </remarks>
    private static void Transicion(
        List<double> col, double nominal, double siguiente, double limSuperior)
    {
        var lo = nominal - TolTransicionM;
        var hi = nominal + TolTransicionM;

        if (col.Count > 0)
        {
            lo = Math.Max(lo, col[^1] + SepMinimaM);
        }

        hi = Math.Min(hi, siguiente - SepMinimaM);
        hi = Math.Min(hi, limSuperior);

        if (lo > hi + 1e-7)
        {
            return;
        }

        col.Add(Math.Clamp(nominal, lo, hi));
    }

    /// <summary>
    /// Agrega un centro solo si no queda pegado al anterior.
    /// </summary>
    /// <remarks>
    /// Dentro de una zona la separación la manda la tabla, así que aquí no se mueve
    /// el estribo: se omite. Mover uno arrastraría a todos los demás.
    /// </remarks>
    private static void ConSeparacion(List<double> col, double valor)
    {
        if (col.Count > 0 && Math.Abs(col[^1] - valor) < SepMinimaM - 1e-7)
        {
            return;
        }

        col.Add(valor);
    }

    private static void Unico(List<double> col, double valor)
    {
        if (col.Count == 0 || Math.Abs(col[^1] - valor) > 1e-4)
        {
            col.Add(valor);
        }
    }

    /// <summary>
    /// Centros de los estribos de un <b>alzado</b>, ya con las reglas del elemento.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe para que el dibujo y la vista previa usen <b>exactamente</b> lo mismo.
    /// Antes cada uno llamaba a <see cref="Centros"/> por su cuenta y aplicaba las
    /// reglas del elemento a mano, y pasó lo que tenía que pasar: la vista previa
    /// quitaba el último estribo de las columnas —como manda el
    /// <c>RemoveLastCenter</c> de la macro— y el dibujo no. La vista previa decía 16
    /// estribos y AutoCAD dibujaba 17.
    /// </para>
    /// <para>
    /// Las dos reglas que dependen del elemento, tal como están en el VBA:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     Los estribos de <b>frontera</b> entre zonas solo se ponen en el alzado
    ///     horizontal: <c>BuildStirrupCenters(..., addBoundaryStirrups:=True)</c> en
    ///     la trabe y <c>False</c> en la columna.
    ///   </item>
    ///   <item>
    ///     En <b>COLUMNA</b> se quita el último estribo. Es literal:
    ///     <c>If TipoElementoTexto(...) = "COLUMNA" Then centers = RemoveLastCenter(centers)</c>.
    ///     No se aplica al dado ni a la trabe.
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="vertical">El alzado va de pie: columna o dado.</param>
    /// <param name="esColumna">
    /// El elemento es COLUMNA. Solo entonces se quita el último estribo.
    /// </param>
    public static List<double> CentrosDeAlzado(
        double largo, double s1, double s2, double s3,
        bool vertical, bool esColumna)
    {
        var centros = Centros(
            0, largo, s1, s2, s3,
            conExtremos: true,
            conFronteras: true);

        if (esColumna && centros.Count > 2)
        {
            // RemoveLastCenter del VBA: en la columna el estribo del extremo superior
            // se quita porque ahí llega el nudo con la trabe y estorba.
            //
            // Se exige Count > 2 para no vaciar el elemento. El VBA hacía
            // 'if Count <= 1 then vaciar', y eso dejaba columnas cortas SIN NI UN
            // ESTRIBO en el plano, sin avisar de nada.
            centros.RemoveAt(centros.Count - 1);
        }

        return centros;
    }

    /// <summary>
    /// Cuántos estribos <b>deberían</b> salir, contando solo por separación.
    /// </summary>
    /// <remarks>
    /// Es el número que hace el usuario a mano cuando dice "faltan estribos". Sirve
    /// para poder <b>comparar</b> contra lo que realmente se colocó y avisar, en vez
    /// de que las omisiones de <see cref="Transicion"/> y <see cref="ConSeparacion"/>
    /// se queden calladas. No manda en el dibujo: solo se informa.
    /// </remarks>
    public static int ConteoNominal(double largo, double s1, double s2, double s3)
    {
        var interior = largo - (2 * BordeM);
        if (interior <= 0)
        {
            return 0;
        }

        if (s1 <= 0) { s1 = 0.15; }
        if (s2 <= 0) { s2 = s1; }
        if (s3 <= 0) { s3 = s1; }

        s1 = Math.Max(s1, SepMinimaDatoM);
        s2 = Math.Max(s2, SepMinimaDatoM);
        s3 = Math.Max(s3, SepMinimaDatoM);

        var variable = Math.Abs(s1 - s2) > 1e-4 || Math.Abs(s2 - s3) > 1e-4;

        if (!variable)
        {
            return (int)((interior / s1) + 1e-6) + 1;
        }

        var n1 = (int)(((interior * 0.25) / s1) + 1e-6);
        var n2 = (int)(((interior * 0.50) / s2) + 1e-6);
        var n3 = (int)(((interior * 0.25) / s3) + 1e-6);

        return n1 + n2 + n3 + 1;
    }

    /// <summary>Longitud del gancho en diámetros, cuando el alzado va tendido.</summary>
    /// <remarks>
    /// Es el <c>HOOK_DIAM_FACTOR</c> del VBA. En la trabe el gancho se mide en
    /// diámetros de la varilla; en la columna se toma tal cual de la columna T de la
    /// hoja. Son dos reglas distintas y hay que respetar las dos.
    /// </remarks>
    public const double FactorGanchoDiametros = 12.0;

    /// <summary>Longitud nominal del gancho, antes de recortarla por la geometría.</summary>
    public static double GanchoNominal(bool vertical, double ganchoM, double dBarraM) =>
        vertical ? ganchoM : FactorGanchoDiametros * dBarraM;

    /// <summary>
    /// Longitud final del gancho: la nominal recortada a lo que cabe.
    /// </summary>
    /// <remarks>
    /// Si no cabe ni un diámetro, el VBA lo pone en <b>cero</b> y no dibuja gancho:
    /// <c>If hookSup &lt; dSup Then hookSup = 0#</c>. Dibujar un gancho más corto que
    /// la propia varilla no representa nada.
    /// </remarks>
    public static double GanchoEfectivo(double nominal, double disponible, double dBarraM)
    {
        var g = Math.Min(nominal, disponible);
        return g < dBarraM ? 0 : g;
    }

    /// <summary>
    /// Longitud del elemento cuando la columna W viene vacía: la que resulta de
    /// acomodar un número entero de estribos en cada zona.
    /// </summary>
    public static double LongitudFlexible(double s1, double s2, double s3, double lInicial = 2.0)
    {
        var interior = lInicial - (2 * BordeM);

        if (s1 <= 0) { s1 = 0.15; }
        if (s2 <= 0) { s2 = s1; }
        if (s3 <= 0) { s3 = s1; }

        s1 = Math.Max(s1, SepMinimaDatoM);
        s2 = Math.Max(s2, SepMinimaDatoM);
        s3 = Math.Max(s3, SepMinimaDatoM);

        var variable = Math.Abs(s1 - s2) > 1e-4 || Math.Abs(s2 - s3) > 1e-4;

        double largo;

        if (variable)
        {
            var n1 = Math.Max(1, (int)((interior / 4) / s1));
            var n2 = Math.Max(1, (int)((interior / 2) / s2));
            var n3 = Math.Max(1, (int)((interior / 4) / s3));

            largo = (2 * BordeM) + (n1 * s1) + (n2 * s2) + (n3 * s3);
        }
        else
        {
            var n = Math.Max(3, (int)(interior / s1));
            largo = (2 * BordeM) + (n * s1);
        }

        return Math.Max(largo, lInicial * 0.8);
    }

    /// <summary>
    /// Longitud de la columna W. La macro interpreta un valor ≥ 20 como centímetros.
    /// </summary>
    public static double LongitudDeColumnaW(double valor)
    {
        if (valor <= 0)
        {
            return 0;
        }

        if (valor >= 20)
        {
            valor /= 100;
        }

        return Math.Max(valor, 0.2);
    }

    /// <summary>
    /// ¿El acero transversal de este elemento es un <b>zuncho</b>, o son <b>estribos</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// La regla es la casilla, y solo la casilla: <b>una sección redonda no lleva zuncho por ser
    /// redonda</b>. Lleva zuncho si se pidió zuncho —la columna «Zuncho helic.» con un SI—, y si
    /// no, lleva estribos normales: anillos cerrados con su gancho, que es como se arma la
    /// mayoría de las columnas y de los dados redondos.
    /// </para>
    /// <para>
    /// Antes esto se decidía con <c>Circular</c> a secas, y el resultado era que un dado redondo
    /// sin la casilla marcada salía rotulado «Zuncho anillos #3 @ 6 cm». <b>El dibujo estaba
    /// bien</b> —eran cápsulas de estribo, no una hélice—, pero el rótulo le decía al fierrero
    /// otra cosa: un zuncho se pide, se dobla y se paga distinto que un estribo. Con la casilla
    /// marcada sí es un zuncho, y entonces el rótulo tiene que decirlo.
    /// </para>
    /// <para>
    /// Vive aquí, y no repetida en cada rótulo, porque la deciden <b>cuatro</b> sitios: el
    /// rótulo del alzado, el texto del acero transversal del alzado, el rótulo de la sección y el
    /// título de la vista previa. Con la regla copiada cuatro veces, arreglar uno dejaba los
    /// otros tres diciendo lo contrario.
    /// </para>
    /// </remarks>
    /// <param name="circular">Si la sección es redonda.</param>
    /// <param name="zunchoHelicoidal">Si se marcó la casilla del zuncho.</param>
    public static bool EsZuncho(bool circular, bool zunchoHelicoidal) =>
        circular && zunchoHelicoidal;
}
