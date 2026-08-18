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
            var n = (int)(largo / s1);
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
    private static void PorSeparacion(
        List<double> col, double ini, double desde, double hasta, double sep)
    {
        var n = (int)((hasta - desde) / sep);
        if (n < 1) { n = 1; }

        for (var i = 1; i <= n; i++)
        {
            var p = desde + (i * sep);
            if (p < hasta - 1e-4)
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
            conExtremos: false,
            conFronteras: !vertical);

        if (esColumna)
        {
            // RemoveLastCenter del VBA: con un solo estribo la macro devuelve un
            // arreglo VACIO, no lo deja. Se copia ese comportamiento.
            if (centros.Count <= 1)
            {
                centros.Clear();
            }
            else
            {
                centros.RemoveAt(centros.Count - 1);
            }
        }

        return centros;
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
}
