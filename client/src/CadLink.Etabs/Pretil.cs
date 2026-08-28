namespace CadLink.Etabs;

/// <summary>
/// El <b>pretil</b>: el muro bajo que se apoya en una losa y <b>no llega a la de arriba</b>.
/// </summary>
/// <remarks>
/// <para>
/// Se reportó así: «tengo en el segundo y tercer nivel un pasillo que sobresale, y arriba de ese
/// pasillo va un pretil de 1 m de altura que se debe ver en el piso de cada uno, pero al dibujar
/// las plantas estructurales donde van pretiles no hay nada y los estás colocando un nivel
/// arriba». Las dos mitades del síntoma son la misma cosa.
/// </para>
///
/// <para><b>POR QUÉ PASA, QUE NO ES UN FALLO DE ETABS</b></para>
/// <para>
/// ETABS asigna cada shell al <b>piso de su cota más alta</b>: un panel que vive entre el nivel 2
/// y el nivel 3 es del Story3, porque su tapa está en el Story3. Para un muro normal eso es lo
/// correcto y es lo que se quiere. Pero un pretil <b>no llega</b> al nivel de arriba: se para en
/// la losa del 2 y se queda a un metro. Su cota más alta sigue cayendo en el tramo del Story3, así
/// que ETABS lo mete ahí, y el plano del nivel 3 sale con un pretil que en la obra está un piso
/// más abajo, mientras que en el plano del nivel 2 —donde de verdad está— no hay nada.
/// </para>
/// <para>
/// El propio usuario lo describió exacto: «en ETABS se ven desde el nivel de arriba pero se ve
/// como al fondo, no que sea parte de ese nivel». Eso es porque está abajo.
/// </para>
///
/// <para><b>LA REGLA, Y POR QUÉ NO MUEVE LOS DEMÁS MUROS</b></para>
/// <para>
/// Se pidió expresamente que esto <b>no mueva todos los muros, solo los pretiles</b>. La regla son
/// dos condiciones que se tienen que dar <b>las dos</b>:
/// </para>
/// <list type="number">
///   <item>
///     <b>Se para en la losa de abajo.</b> Su cota inferior coincide con el nivel inferior de su
///     propio piso. O sea que está apoyado en ese piso, no colgado.
///   </item>
///   <item>
///     <b>No llega a su propia losa.</b> Su cota superior se queda por debajo de la elevación del
///     piso al que ETABS lo asignó. O sea que no sostiene nada.
///   </item>
/// </list>
/// <para>
/// Con eso, cada tipo de panel cae donde debe y <b>ninguno se mueve por error</b>:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Un muro completo</b> de piso a techo falla la segunda: su tapa <i>es</i> la elevación
///     de su piso. No se toca, que es lo que se pidió.
///   </item>
///   <item>
///     <b>Un dintel</b> —el panel que va de encima de la puerta a la losa— falla la primera: no
///     se para en la losa de abajo, arranca a dos metros. No se toca.
///   </item>
///   <item>
///     <b>El panel de encima de un antepecho</b> falla la primera por lo mismo. No se toca.
///   </item>
///   <item>
///     <b>Un castillo o un muro dibujado de corrido por dos pisos</b> falla las dos. No se toca.
///   </item>
///   <item>
///     <b>Un pretil o un antepecho de ventana</b> cumple las dos, y bajarlo es lo correcto para
///     los dos: los dos se paran en esa losa y ninguno sostiene la de arriba, así que los dos
///     pertenecen al plano de ese piso.
///   </item>
/// </list>
/// <para>
/// Y encima hay un <b>tope de altura</b>, que no hace falta para que la regla sea correcta pero sí
/// para que sea prudente: se pidió «solo los pretiles», y con el tope un muro alto que casualmente
/// se quede corto no se mueve sin que alguien lo decida.
/// </para>
///
/// <para><b>DÓNDE SE APLICA, Y POR QUÉ AHÍ</b></para>
/// <para>
/// Reescribiendo el <c>Story</c> del elemento, una sola vez, <b>antes de que nadie filtre por
/// nivel</b>. Y eso importa porque hay <b>dos</b> filtros por nivel independientes: el que arma la
/// planta para AutoCAD y el del lienzo de la vista previa. Arreglando solo uno, el plano y lo que
/// se ve en pantalla dirían cosas distintas. Hay precedente de reescribir <c>Story</c> así en
/// <see cref="ModeloEtabs.NivelesDesdeZ"/>, que es lo que hace con los modelos de SAP2000.
/// </para>
/// <para>
/// Y es <b>idempotente</b>: aplicado dos veces no baja el pretil dos pisos. Después de bajarlo, su
/// cota inferior ya coincide con la elevación de su nuevo piso y no con la del piso de abajo, así
/// que falla la primera condición y se queda quieto.
/// </para>
/// </remarks>
public static class Pretil
{
    /// <summary>Un pretil que se bajó de piso, para poder avisar de qué se movió.</summary>
    /// <param name="Muro">El panel.</param>
    /// <param name="DeNivel">El piso al que ETABS lo tenía asignado.</param>
    /// <param name="ANivel">El piso al que se pasó: el que lo sostiene.</param>
    /// <param name="AlturaM">Su altura, en metros.</param>
    public sealed record Bajado(ElementoEtabs Muro, string DeNivel, string ANivel, double AlturaM);

    /// <summary>Cuánto se admite de desajuste al comparar cotas, en metros.</summary>
    public const double ToleranciaM = 0.20;

    /// <summary>
    /// Fracción de la altura de entrepiso por debajo de la cual el muro <b>no llega al techo</b>.
    /// </summary>
    /// <remarks>
    /// Es el mismo 0.75 de <c>MURO_FRACCION_ENTREPISO</c>, que ya se usa para decidir si un shell
    /// cubre un entrepiso. Se repite aquí como valor por omisión porque este proyecto no ve la
    /// configuración del plano —vive en CadLink.Cad— y quien llama puede pasar el de la hoja.
    /// </remarks>
    public const double FraccionDeEntrepiso = 0.75;

    /// <summary>Altura máxima, en metros, para tomar un muro bajo como pretil.</summary>
    /// <remarks>
    /// Un pretil de azotea o de pasillo va entre 0.90 y 1.20 m. 1.50 deja margen sin empezar a
    /// mover medios muros. No hace falta para que la regla sea correcta: es la prudencia que se
    /// pidió con «solo los pretiles».
    /// </remarks>
    public const double AlturaMaximaM = 1.50;

    /// <summary>La cota de abajo y la de arriba del panel, de sus vértices si los trae.</summary>
    /// <remarks>
    /// Se prefieren los vértices 3D a <c>Z1</c>/<c>Z2</c> porque son el dato de origen; el lector
    /// ya pone <c>Z1</c>/<c>Z2</c> a ese mínimo y máximo, pero un panel que llegue de otro camino
    /// podría traerlos como las cotas de dos vértices cualesquiera, y entonces un muro saldría de
    /// altura cero.
    /// </remarks>
    public static (double Abajo, double Arriba) CotasDe(ElementoEtabs el)
    {
        if (el.Vertices3D.Count > 0)
        {
            return (el.Vertices3D.Min(v => v.Z), el.Vertices3D.Max(v => v.Z));
        }

        return (Math.Min(el.Z1, el.Z2), Math.Max(el.Z1, el.Z2));
    }

    /// <summary>
    /// ¿Este muro es un <b>pretil</b> del piso de abajo en lugar de un muro del suyo?
    /// </summary>
    /// <param name="el">El panel.</param>
    /// <param name="suyo">El nivel al que ETABS lo asignó, con su elevación y su entrepiso.</param>
    /// <param name="fraccion">Ver <see cref="FraccionDeEntrepiso"/>.</param>
    /// <param name="tolM">Ver <see cref="ToleranciaM"/>.</param>
    /// <param name="alturaMaxM">Ver <see cref="AlturaMaximaM"/>.</param>
    public static bool EsPretil(
        ElementoEtabs el,
        NivelEtabs suyo,
        double fraccion = FraccionDeEntrepiso,
        double tolM = ToleranciaM,
        double alturaMaxM = AlturaMaximaM)
    {
        // Solo muros. Una columna corta o una trabe no son un pretil, y moverlas de piso sería
        // cambiar la estructura de sitio.
        if (el.Clase != ClaseElemento.Muro)
        {
            return false;
        }

        // Sin altura de entrepiso no se puede decir si llega al techo o no, así que no se toca.
        // Pasa en los niveles deducidos de las cotas, donde la altura puede venir en cero.
        if (suyo.AlturaM <= 0.1)
        {
            return false;
        }

        var (abajo, arriba) = CotasDe(el);

        var alto = arriba - abajo;

        if (alto <= 0.01 || alto > alturaMaxM)
        {
            return false;
        }

        // ---- 1) SE PARA EN LA LOSA DE ABAJO ----
        //
        // Esto es lo que distingue un pretil de un DINTEL. Los dos son panelitos que no llegan a
        // la losa de arriba, pero el dintel arranca a dos metros del suelo y el pretil se apoya.
        if (Math.Abs(abajo - (suyo.ElevacionM - suyo.AlturaM)) > tolM)
        {
            return false;
        }

        // ---- 2) NO LLEGA A SU PROPIA LOSA ----
        //
        // Esto es lo que salva a los MUROS COMPLETOS, que es lo que se pidió expresamente: la
        // tapa de un muro de piso a techo ES la elevación de su piso, así que aquí se va.
        if (suyo.ElevacionM - arriba <= tolM)
        {
            return false;
        }

        // Y la comprobación de sobra: que además sea bajo respecto a su entrepiso. Con las dos
        // de arriba ya está decidido, pero esto ata el caso raro de un entrepiso pequeñísimo,
        // donde un metro sí sería casi todo el muro.
        return alto < suyo.AlturaM * fraccion;
    }

    /// <summary>
    /// Baja al piso que los sostiene los pretiles que ETABS dejó un nivel arriba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devuelve los que se movieron para poder avisar. No mueve nada si no encuentra un nivel cuya
    /// elevación coincida con la base del pretil: antes que adivinar un destino, se queda como
    /// estaba, que es un defecto conocido y no una pieza en un sitio inventado.
    /// </para>
    /// <para>
    /// Los niveles se toman de <see cref="ModeloEtabs.NivelesConElementos"/> y no de
    /// <c>Niveles</c> a secas porque la API de ETABS <b>no devuelve la base</b>, y un pretil
    /// apoyado en la losa de planta baja tiene que poder bajar hasta ella.
    /// </para>
    /// </remarks>
    public static List<Bajado> Bajar(
        ModeloEtabs m,
        double fraccion = FraccionDeEntrepiso,
        double tolM = ToleranciaM,
        double alturaMaxM = AlturaMaximaM)
    {
        var movidos = new List<Bajado>();

        // El nivel de cada elemento: el de la API si está, y si no el deducido de las cotas, que
        // es el único camino por el que aparece la BASE.
        var niveles = m.NivelesConElementos(ascendente: true);

        foreach (var n in m.Niveles)
        {
            if (!niveles.Any(x => string.Equals(x.Nombre, n.Nombre, StringComparison.OrdinalIgnoreCase)))
            {
                niveles.Add(n);
            }
        }

        if (niveles.Count < 2)
        {
            return movidos;
        }

        NivelEtabs? Nivel(string nombre) => niveles.FirstOrDefault(
            n => string.Equals(n.Nombre.Trim(), nombre.Trim(), StringComparison.OrdinalIgnoreCase));

        foreach (var el in m.Elementos)
        {
            if (el.Clase != ClaseElemento.Muro || el.Story.Trim().Length == 0)
            {
                continue;
            }

            var suyo = Nivel(el.Story);

            if (suyo is null || !EsPretil(el, suyo, fraccion, tolM, alturaMaxM))
            {
                continue;
            }

            var (abajo, arriba) = CotasDe(el);

            // El destino es el nivel cuya losa lo sostiene, buscado por ELEVACIÓN y no cogiendo
            // «el anterior de la lista»: si un nivel intermedio no tiene elementos, el anterior
            // de la lista no es el que está debajo.
            var destino = niveles
                .Where(n => Math.Abs(n.ElevacionM - abajo) <= tolM)
                .OrderBy(n => Math.Abs(n.ElevacionM - abajo))
                .FirstOrDefault();

            if (destino is null
                || string.Equals(destino.Nombre.Trim(), el.Story.Trim(),
                                 StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            movidos.Add(new Bajado(el, el.Story, destino.Nombre, arriba - abajo));

            el.Story = destino.Nombre;
        }

        return movidos;
    }

    /// <summary>Un aviso legible de lo que se movió, o cadena vacía si no se movió nada.</summary>
    /// <remarks>
    /// Se agrupa por el par de niveles porque un pretil llega partido en muchos paneles —el mesh
    /// lo corta en cada nudo— y un aviso por panel serían cuarenta renglones iguales.
    /// </remarks>
    public static string Aviso(List<Bajado> movidos)
    {
        if (movidos.Count == 0)
        {
            return string.Empty;
        }

        var porNiveles = movidos
            .GroupBy(b => (b.DeNivel, b.ANivel))
            .OrderBy(g => g.Key.ANivel, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
                $"{g.Count()} de {g.Key.DeNivel} a {g.Key.ANivel} " +
                $"(altura {g.Min(b => b.AlturaM):0.00}–{g.Max(b => b.AlturaM):0.00} m)");

        return $"{movidos.Count} panel(es) de PRETIL se pasaron al nivel que los sostiene: " +
               string.Join("; ", porNiveles) +
               ". ETABS los asigna al piso de su cota más alta, así que un pretil que no llega " +
               "a la losa de arriba quedaba dibujado un nivel por encima de donde está.";
    }
}
