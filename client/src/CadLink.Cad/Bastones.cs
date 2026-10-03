namespace CadLink.Cad;

/// <summary>En qué lecho va el bastón.</summary>
public enum PosicionBaston
{
    /// <summary>Arriba, junto al lecho superior. Típico en los apoyos.</summary>
    Superior,

    /// <summary>Abajo, junto al lecho inferior. Típico al centro del claro.</summary>
    Inferior,

    /// <summary>A media altura, como las intermedias. Va recto, sin gancho.</summary>
    Medio
}

/// <summary>En qué parte de la trabe va el bastón.</summary>
public enum UbicacionBaston
{
    /// <summary>Uno en cada extremo, cada uno desde su paño hacia dentro.</summary>
    Extremos,

    /// <summary>Solo en el extremo izquierdo.</summary>
    Izquierdo,

    /// <summary>Solo en el extremo derecho.</summary>
    Derecho,

    /// <summary>Al centro: empieza a la distancia dada de cada paño.</summary>
    AlCentro
}

/// <summary>
/// Un <b>bastón</b>: varillas adicionales que no corren de paño a paño, como las recibe el
/// dibujante, con el diámetro ya resuelto.
/// </summary>
/// <remarks>
/// <para>
/// <b>La distancia se mide desde los paños</b>, que es como se pidió y como se acota en
/// obra:
/// </para>
/// <list type="bullet">
///   <item>En un extremo, es lo que mide el bastón: del paño hacia dentro.</item>
///   <item>Al centro, es donde empieza: el bastón va de esa distancia de un paño a la
///   misma del otro.</item>
/// </list>
/// <para>
/// Solo los llevan <b>trabes y contratrabes</b>. Quien arma los datos los deja vacíos en
/// columnas y dados.
/// </para>
/// </remarks>
public sealed class BastonCad
{
    public PosicionBaston Posicion { get; set; } = PosicionBaston.Superior;

    public UbicacionBaston Ubicacion { get; set; } = UbicacionBaston.Extremos;

    /// <summary>Cuántas varillas lleva el bastón.</summary>
    public int Cantidad { get; set; }

    public VarCad Var { get; set; }

    /// <summary>Distancia desde el paño, en metros.</summary>
    public double DistanciaM { get; set; }
}

/// <summary>
/// Las reglas de los bastones, sin AutoCAD: dónde van a lo largo de la trabe, si los cruza
/// el corte A-A' y cómo se rotulan.
/// </summary>
/// <remarks>
/// Viven aquí y no en el dibujante para que el alzado, el corte y la vista previa usen
/// la misma cuenta: si el alzado dibujara un bastón que el corte no muestra, el plano se
/// contradiría consigo mismo.
/// </remarks>
public static class Bastones
{
    /// <summary>
    /// Cuántos bastones admite una pieza: <b>dos</b>, uno arriba y otro abajo. Lo pidió el
    /// usuario: la ubicación de cada uno la da <see cref="UbicacionDe"/>.
    /// </summary>
    public const int Maximo = 2;

    /// <summary>
    /// La altura del bastón de en medio, medida desde la cara de abajo del núcleo
    /// (<paramref name="yBot"/>) hasta la de arriba (<paramref name="yTop"/>).
    /// </summary>
    /// <remarks>
    /// A media altura, salvo que haya un número <b>impar</b> de intermedias: entonces una
    /// de ellas cae justo ahí, y el bastón <b>baja</b> medio paso para no quedar encima de
    /// ella. Baja y no sube: lo pidió el usuario, el de en medio va en la parte de abajo.
    /// Es la misma cuenta en el alzado y en el corte.
    /// </remarks>
    public static double YMedio(double yBot, double yTop, int nIntermedias)
    {
        var medio = (yBot + yTop) / 2;

        if (nIntermedias > 0 && nIntermedias % 2 == 1)
        {
            medio -= (yTop - yBot) / (nIntermedias + 1) / 2;
        }

        return medio;
    }

    /// <summary>
    /// Lo que puede medir el gancho de un bastón de arriba o de abajo <b>sin chocar</b> con
    /// otro bastón: todas las medidas son huecos libres, hacia dentro de la pieza, desde la
    /// cara del bastón.
    /// </summary>
    /// <param name="hastaLechoOpuesto">Hasta la cara de la corrida del otro lecho.</param>
    /// <param name="hastaMedio">Hasta el bastón de en medio, si lo hay en esa punta.</param>
    /// <param name="hastaBastonOpuesto">
    /// Hasta el bastón del otro lecho, si lo hay en esa punta. Su gancho viene de frente,
    /// así que el hueco se reparte a medias.
    /// </param>
    /// <param name="holgura">Lo que se deja libre entre la punta y lo que tiene enfrente.</param>
    public static double LibreParaGancho(
        double hastaLechoOpuesto, double? hastaMedio, double? hastaBastonOpuesto, double holgura)
    {
        var libre = hastaLechoOpuesto;

        if (hastaBastonOpuesto is double o)
        {
            libre = Math.Min(libre, (o - holgura) / 2);
        }

        if (hastaMedio is double m)
        {
            libre = Math.Min(libre, m - holgura);
        }

        return Math.Max(0, libre);
    }

    /// <summary>
    /// ¿Llega el bastón a la punta izquierda (o derecha) de la pieza, donde está el gancho
    /// de otro? Hace falta para que solo se tope el gancho contra lo que de verdad pasa por
    /// ahí: un bastón de centro que empieza a L/4 no estorba al gancho del paño.
    /// </summary>
    /// <param name="xGancho">La X del gancho, en metros desde el paño izquierdo.</param>
    public static bool PasaPor(BastonCad b, double largo, double xGancho, double margen) =>
        Tramos(b, largo).Any(t => t.Ini <= xGancho + margen && t.Fin >= xGancho - margen);

    /// <summary>Separación libre entre el lecho y la cama de bastones: 2.5 cm.</summary>
    public const double SeparacionCamaCm = 2.5;

    /// <summary>Un tramo de bastón a lo largo de la pieza, en metros desde el paño izquierdo.</summary>
    /// <param name="GanchoIzq">Si lleva gancho en su punta izquierda, la que llega al paño.</param>
    /// <param name="GanchoDer">Si lleva gancho en su punta derecha.</param>
    public readonly record struct Tramo(double Ini, double Fin, bool GanchoIzq, bool GanchoDer)
    {
        public double Largo => Fin - Ini;
    }

    /// <summary>
    /// La ubicación que le toca a cada lecho, que <b>no se elige</b>: lo pidió el usuario.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>Trabe:</b> arriba en ambos extremos (momento negativo en los apoyos) y
    ///   abajo al centro (positivo en el claro).</item>
    ///   <item><b>Contratrabe:</b> al revés, porque trabaja invertida: abajo en ambos
    ///   extremos y arriba al centro.</item>
    /// </list>
    /// </remarks>
    public static UbicacionBaston UbicacionDe(PosicionBaston p, bool contratrabe)
    {
        var enExtremos = contratrabe ? PosicionBaston.Inferior : PosicionBaston.Superior;
        return p == enExtremos ? UbicacionBaston.Extremos : UbicacionBaston.AlCentro;
    }

    /// <summary>
    /// Aplica la regla: dos bastones como mucho, uno arriba y otro abajo, con la ubicación
    /// de <see cref="UbicacionDe"/>. Lo que llegue de otra forma —un trabajo viejo con el
    /// de «en medio»— se descarta.
    /// </summary>
    public static List<BastonCad> Normalizar(IEnumerable<BastonCad> bastones, bool contratrabe)
    {
        var res = new List<BastonCad>();

        foreach (var b in bastones)
        {
            if (b.Posicion == PosicionBaston.Medio || res.Any(o => o.Posicion == b.Posicion))
            {
                continue;
            }

            b.Ubicacion = UbicacionDe(b.Posicion, contratrabe);
            res.Add(b);
        }

        return res;
    }

    /// <summary>¿Se puede dibujar? Varillas, diámetro y distancia válidos.</summary>
    public static bool EsValido(BastonCad b) =>
        b.Cantidad > 0 && b.Var.Existe && b.DistanciaM > 0;

    /// <summary>
    /// Los tramos del bastón en una pieza de <paramref name="largo"/> metros.
    /// </summary>
    /// <remarks>
    /// Los de extremo llevan gancho en la punta del paño, que es la que se ancla en el
    /// apoyo; el del centro va recto. Un bastón que no cabe —de extremo más largo que la
    /// mitad, o de centro que empezaría pasado el medio— se recorta a lo que cabe o no
    /// devuelve nada: dos bastones de extremo encimados al centro no son un armado.
    /// </remarks>
    public static List<Tramo> Tramos(BastonCad b, double largo)
    {
        var res = new List<Tramo>();

        if (!EsValido(b) || largo <= 0)
        {
            return res;
        }

        var d = b.DistanciaM;

        switch (b.Ubicacion)
        {
            case UbicacionBaston.Extremos:
            {
                var l = Math.Min(d, largo / 2);
                res.Add(new Tramo(0, l, true, false));
                res.Add(new Tramo(largo - l, largo, false, true));
                break;
            }

            case UbicacionBaston.Izquierdo:
                res.Add(new Tramo(0, Math.Min(d, largo), true, false));
                break;

            case UbicacionBaston.Derecho:
                res.Add(new Tramo(Math.Max(0, largo - d), largo, false, true));
                break;

            case UbicacionBaston.AlCentro:
                if (2 * d < largo)
                {
                    res.Add(new Tramo(d, largo - d, false, false));
                }

                break;
        }

        return res;
    }

    /// <summary>¿El corte, a <paramref name="xCorte"/> metros del paño izquierdo, cruza el bastón?</summary>
    public static bool CruzaElCorte(BastonCad b, double largo, double xCorte) =>
        Tramos(b, largo).Any(t => xCorte >= t.Ini - 1e-9 && xCorte <= t.Fin + 1e-9);

    /// <summary>
    /// Los bastones que se ven en el <c>CORTE A-A'</c>: los que cruza la línea de corte.
    /// </summary>
    /// <remarks>
    /// El corte está donde lo pone <see cref="AlzadoLayout.PosicionCorte"/>, el mismo sitio
    /// de la línea A-A' del alzado. Por eso un bastón de extremo más corto que L/4 + 5 cm no
    /// sale en el corte: la línea pasa más allá de su punta.
    /// </remarks>
    public static List<BastonCad> EnElCorte(IEnumerable<BastonCad> bastones, double largo)
    {
        var xCorte = AlzadoLayout.PosicionCorte(largo);
        return bastones.Where(b => CruzaElCorte(b, largo, xCorte)).ToList();
    }

    /// <summary>El rótulo del bastón: <c>2 Var. (Bastones) #4C</c>.</summary>
    public static string Texto(BastonCad b) =>
        $"{b.Cantidad} Var. (Bastones) {b.Var.Clave}C";

    /// <summary>
    /// Las X de las varillas de una cama de bastones en la sección, repartidas entre las
    /// dos esquinas del lecho.
    /// </summary>
    /// <param name="xIni">Centro de la varilla más a la izquierda que cabe.</param>
    /// <param name="xFin">Centro de la más a la derecha.</param>
    public static List<double> XsEnCama(int cantidad, double xIni, double xFin)
    {
        var xs = new List<double>();

        if (cantidad <= 0)
        {
            return xs;
        }

        if (cantidad == 1 || xFin <= xIni)
        {
            xs.Add((xIni + xFin) / 2);
            return xs;
        }

        var paso = (xFin - xIni) / (cantidad - 1);
        for (var i = 0; i < cantidad; i++)
        {
            xs.Add(xIni + (i * paso));
        }

        return xs;
    }
}
