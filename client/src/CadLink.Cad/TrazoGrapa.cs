namespace CadLink.Cad;

/// <summary>
/// El trazo de una <b>grapa</b>: el estribo suplementario que une dos varillas
/// longitudinales, con un gancho tipo C en cada punta.
/// </summary>
/// <remarks>
/// <para>
/// Vive en <c>CadLink.Cad</c> y no en la aplicación, y no usa WPF, por el mismo motivo
/// que <see cref="TrazoDiamante"/> y <see cref="TrazoZapata"/>: la geometría la
/// necesitan <b>dos</b> dibujantes —la vista previa en pantalla y el de AutoCAD— y
/// calcularla dos veces es la manera de acabar enseñando en pantalla una grapa que no
/// es la que se dibuja en el plano.
/// </para>
/// <para>
/// Hoy solo la usa la vista previa. Está aquí para que el día que las grapas se manden
/// a AutoCAD no haya que reescribir la geometría, solo llamarla.
/// </para>
/// <para>
/// <b>La forma.</b> Una grapa es una varilla recta con un doblez en cada extremo. El
/// tramo recto va <i>tangente</i> a las dos varillas por el mismo lado, y en cada punta
/// da media vuelta alrededor de su varilla y remata con una cola paralela al tramo
/// recto, apuntando hacia dentro. Eso es el gancho «tipo C»: cuerpo por un lado y los
/// dos dobleces curvando hacia el otro.
/// </para>
/// </remarks>
public static class TrazoGrapa
{
    /// <summary>Cuántos tramos rectos se usan para cada medio doblez.</summary>
    /// <remarks>
    /// Los arcos se muestrean porque un lienzo de WPF no tiene arcos ni <i>bulges</i>,
    /// igual que en <c>TrazoDiamante.Muestrear</c>. Con 18 tramos el doblez de una
    /// varilla del #3 ya se ve redondo a cualquier zoom razonable.
    /// </remarks>
    public const int TramosPorDoblez = 18;

    /// <summary>
    /// Hasta qué parte del tramo recto puede medir cada cola.
    /// </summary>
    /// <remarks>
    /// Las dos colas apuntan una hacia la otra. Sin tope, con dos varillas cercanas y
    /// un gancho largo se cruzarían y la grapa se vería como un nudo. Al 40 % cada una
    /// siempre queda un hueco entre las puntas.
    /// </remarks>
    private const double FraccionMaximaDeCola = 0.40;

    /// <summary>
    /// Distancia entre las dos varillas que la grapa agarra.
    /// </summary>
    /// <remarks>
    /// Es lo que decide <b>quién pasa por encima</b> cuando dos grapas se cruzan. Ver
    /// <see cref="ClaveDeOrden"/>.
    /// </remarks>
    public static double Largo(double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>¿La grapa va más tumbada que parada?</summary>
    public static bool EsHorizontal(double ax, double ay, double bx, double by) =>
        Math.Abs(bx - ax) >= Math.Abs(by - ay);

    /// <summary>
    /// La clave con la que se ordenan las grapas para dibujarlas: <b>primero las que van
    /// debajo</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La regla es que <b>la más larga va debajo y la más corta encima</b>. Tiene sentido
    /// de armado: la grapa corta es la que se mete al final, cuando la larga ya está
    /// colocada, así que queda por delante.
    /// </para>
    /// <para>
    /// El desempate: si las dos miden lo mismo —una sección cuadrada con una grapa
    /// horizontal y otra vertical—, <b>la horizontal va encima</b>. Sin una regla fija,
    /// el resultado dependería del orden en que se hubieran ido marcando las grapas, y la
    /// misma sección se dibujaría distinta en dos proyectos.
    /// </para>
    /// <para>
    /// Vive aquí, y no en cada dibujante, porque los dos tienen que ordenar igual: si la
    /// pantalla pusiera una encima y el plano la otra, la vista previa estaría mintiendo.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Dos números para ordenar de menor a mayor. El primero es el largo en negativo —así
    /// la más larga sale primero— y el segundo desempata dejando la horizontal al final.
    /// </returns>
    public static (double Primero, int Segundo) ClaveDeOrden(
        double ax, double ay, double bx, double by) =>
        (-Largo(ax, ay, bx, by), EsHorizontal(ax, ay, bx, by) ? 1 : 0);

    /// <summary>
    /// El <b>contorno cerrado</b> de la grapa, en centímetros.
    /// </summary>
    /// <param name="ax">X del centro de la primera varilla.</param>
    /// <param name="ay">Y del centro de la primera varilla.</param>
    /// <param name="ra">Radio de la primera varilla.</param>
    /// <param name="bx">X del centro de la segunda varilla.</param>
    /// <param name="by">Y del centro de la segunda varilla.</param>
    /// <param name="rb">Radio de la segunda varilla.</param>
    /// <param name="dGrapa">Diámetro de la varilla con que se arma la grapa.</param>
    /// <param name="colaCm">Largo que se pide para cada cola, antes del tope.</param>
    /// <returns>
    /// El contorno, empezando y acabando en el mismo punto, o <c>null</c> si los datos
    /// no dan una grapa dibujable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Devuelve <b>un solo contorno cerrado</b> —no dos líneas paralelas— porque así
    /// sirve para las dos cosas que hacen falta: pintarlo relleno o hueco según el
    /// estilo de la sección, y registrarlo como <i>isla</i> del achurado de concreto
    /// cuando esto llegue a AutoCAD.
    /// </para>
    /// <para>
    /// <b>Por qué el tramo recto no va del centro de una varilla al de la otra.</b> La
    /// grapa se agarra por fuera, así que su eje es la <i>tangente común</i> a las dos
    /// varillas engordadas medio diámetro de grapa. Cuando las dos varillas son del
    /// mismo calibre esa tangente sale paralela a la línea de centros, pero con
    /// calibres distintos va ligeramente inclinada, y es la inclinación que hace que el
    /// doblez encaje sin escalón con el tramo recto. Se calcula con
    /// <c>beta = -asin((R2 - R1) / L)</c>, que es la condición de tangencia.
    /// </para>
    /// </remarks>
    public static List<(double X, double Y)>? Contorno(
        double ax, double ay, double ra,
        double bx, double by, double rb,
        double dGrapa, double colaCm)
    {
        if (dGrapa <= 0 || ra <= 0 || rb <= 0)
        {
            return null;
        }

        var lx = bx - ax;
        var ly = by - ay;
        var largoEntreCentros = Math.Sqrt((lx * lx) + (ly * ly));

        // Dos varillas en el mismo sitio, o tan juntas que la grapa no tiene por dónde
        // pasar: no hay grapa que dibujar.
        if (largoEntreCentros < 1e-9)
        {
            return null;
        }

        var ux = lx / largoEntreCentros;
        var uy = ly / largoEntreCentros;

        // La normal a la línea de centros, girada +90°.
        var nx = -uy;
        var ny = ux;

        // Los radios del EJE de la grapa alrededor de cada varilla: la varilla más
        // medio diámetro de grapa.
        var r1 = ra + (dGrapa / 2);
        var r2 = rb + (dGrapa / 2);

        // Condición de tangencia. Si las varillas están tan cerca que una queda dentro
        // de la otra engordada, no hay tangente común y la grapa no existe.
        var seno = (r2 - r1) / largoEntreCentros;

        if (seno <= -1 || seno >= 1)
        {
            return null;
        }

        var beta = -Math.Asin(seno);
        var cosB = Math.Cos(beta);
        var senB = Math.Sin(beta);

        // m: del centro de cada varilla hacia su punto de tangencia. Es la normal
        // inclinada lo que pide la tangencia.
        var mx = (cosB * nx) + (senB * ux);
        var my = (cosB * ny) + (senB * uy);

        var thetaM = Math.Atan2(my, mx);

        // Los dos puntos de tangencia del eje, y de ahí la dirección del tramo recto.
        var paX = ax + (r1 * mx);
        var paY = ay + (r1 * my);
        var pbX = bx + (r2 * mx);
        var pbY = by + (r2 * my);

        var cx = pbX - paX;
        var cy = pbY - paY;
        var largoRecto = Math.Sqrt((cx * cx) + (cy * cy));

        if (largoRecto < 1e-9)
        {
            return null;
        }

        // Las colas van paralelas al tramo recto, no a la línea de centros: con
        // calibres distintos no son lo mismo, y usar la línea de centros dejaría un
        // codo visible donde el doblez entrega a la cola.
        var dx = cx / largoRecto;
        var dy = cy / largoRecto;

        var cola = Math.Max(0, Math.Min(colaCm, largoRecto * FraccionMaximaDeCola));

        // Los cuatro círculos del contorno: interior y exterior de cada doblez.
        var raIn = ra;
        var raOut = ra + dGrapa;
        var rbIn = rb;
        var rbOut = rb + dGrapa;

        var salida = new List<(double X, double Y)>();

        // Puntos de arranque y remate de cada doblez, en el lado opuesto al tramo recto.
        var thetaAFin = thetaM + Math.PI;
        var thetaBFin = thetaM - Math.PI;

        (double X, double Y) EnA(double r, double t) =>
            (ax + (r * Math.Cos(t)), ay + (r * Math.Sin(t)));

        (double X, double Y) EnB(double r, double t) =>
            (bx + (r * Math.Cos(t)), by + (r * Math.Sin(t)));

        void Arco((double X, double Y) centro, double radio, double desde, double hasta)
        {
            for (var k = 0; k <= TramosPorDoblez; k++)
            {
                var t = desde + ((hasta - desde) * k / TramosPorDoblez);
                salida.Add((centro.X + (radio * Math.Cos(t)), centro.Y + (radio * Math.Sin(t))));
            }
        }

        var centroA = (X: ax, Y: ay);
        var centroB = (X: bx, Y: by);

        // ---- Se recorre el contorno completo, de una sola vuelta ----

        // 1) Canto exterior de la cola de A, de la punta hacia el doblez.
        var aOutFin = EnA(raOut, thetaAFin);
        salida.Add((aOutFin.X + (cola * dx), aOutFin.Y + (cola * dy)));

        // 2) Doblez de A por fuera. Va de vuelta hacia el tramo recto, pasando por la
        //    cara de atrás de la varilla, la que da la espalda a la otra.
        Arco(centroA, raOut, thetaAFin, thetaM);

        // 3) Tramo recto por fuera.
        var bOutIni = EnB(rbOut, thetaM);
        salida.Add(bOutIni);

        // 4) Doblez de B por fuera, también por su cara de atrás.
        Arco(centroB, rbOut, thetaM, thetaBFin);

        // 5) Canto exterior de la cola de B, hacia su punta.
        var bOutFin = EnB(rbOut, thetaBFin);
        salida.Add((bOutFin.X - (cola * dx), bOutFin.Y - (cola * dy)));

        // 6) La punta de la cola de B: cruza el grueso de la varilla.
        var bInFin = EnB(rbIn, thetaBFin);
        salida.Add((bInFin.X - (cola * dx), bInFin.Y - (cola * dy)));

        // 7) Canto interior de la cola de B, de vuelta al doblez.
        salida.Add(bInFin);

        // 8) Doblez de B por dentro, deshaciendo el camino.
        Arco(centroB, rbIn, thetaBFin, thetaM);

        // 9) Tramo recto por dentro.
        var aInIni = EnA(raIn, thetaM);
        salida.Add(aInIni);

        // 10) Doblez de A por dentro.
        Arco(centroA, raIn, thetaM, thetaAFin);

        // 11) Canto interior de la cola de A, hacia su punta.
        var aInFin = EnA(raIn, thetaAFin);
        salida.Add((aInFin.X + (cola * dx), aInFin.Y + (cola * dy)));

        // 12) La punta de la cola de A cierra el contorno contra el punto 1.
        salida.Add((aOutFin.X + (cola * dx), aOutFin.Y + (cola * dy)));

        return salida;
    }
}
