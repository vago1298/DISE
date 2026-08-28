namespace CadLink.Etabs;

/// <summary>
/// El <b>pretil</b> y todo lo que lo acompaña: lo que se para sobre una losa y <b>no llega a la
/// de arriba</b>.
/// </summary>
/// <remarks>
/// <para>
/// Se reportó así: «tengo en el segundo y tercer nivel un pasillo que sobresale, y arriba de ese
/// pasillo va un pretil de 1 m de altura que se debe ver en el piso de cada uno, pero al dibujar
/// las plantas estructurales donde van pretiles no hay nada y los estás colocando un nivel
/// arriba». Y después: «ya pones el muro en su nivel, pero también te faltan las columnas y vigas
/// que están a 1 m del piso terminado».
/// </para>
/// <para>
/// Son la misma cosa. Un pretil no es solo su muro: lleva sus <b>castillos</b> —columnas cortas
/// dentro del muro— y su <b>cadena de remate</b> —una viga a un metro del piso—. Las tres piezas
/// se van al mismo sitio equivocado por el mismo motivo.
/// </para>
///
/// <para><b>POR QUÉ PASA, QUE NO ES UN FALLO DE ETABS</b></para>
/// <para>
/// ETABS asigna cada pieza al <b>piso de su cota más alta</b>: lo que vive entre el nivel 2 y el
/// nivel 3 es del Story3, porque su tapa está en el tramo del Story3. Para un muro o una columna
/// normales eso es lo correcto y es lo que se quiere. Pero el pretil <b>no llega</b> al nivel de
/// arriba: se para en la losa del 2 y se queda a un metro. Su cota más alta sigue cayendo en el
/// tramo del Story3, así que ETABS lo mete ahí, y el plano del nivel 3 sale con un pretil que en
/// la obra está un piso más abajo, mientras que en el plano del nivel 2 —donde de verdad está— no
/// hay nada.
/// </para>
/// <para>
/// El propio usuario lo describió exacto: «en ETABS se ven desde el nivel de arriba pero se ve
/// como al fondo, no que sea parte de ese nivel». Eso es porque está abajo.
/// </para>
///
/// <para><b>LA REGLA</b></para>
/// <para>
/// Se pidió expresamente que esto <b>no mueva todos los muros, solo los pretiles</b>, así que la
/// regla está escrita para dejar quieto todo lo demás. Son tres condiciones, y las tres tienen
/// que darse:
/// </para>
/// <list type="number">
///   <item>
///     <b>No llega a su propia losa.</b> Su cota superior se queda por debajo de la elevación del
///     piso al que ETABS lo asignó, o sea que no sostiene nada de ese piso. <b>Esta es la que hace
///     casi todo el trabajo</b>, y es lo que salva a todo lo que sí llega.
///   </item>
///   <item>
///     <b>Se queda bajo respecto a la losa sobre la que se para.</b> Su cota superior está a menos
///     de <see cref="AlturaMaximaM"/> de esa losa. Un pretil, sus castillos y su cadena están a un
///     metro; medio muro, no.
///   </item>
///   <item>
///     <b>No continúa hacia arriba</b> —solo para muros y columnas—. Si en el mismo sitio en
///     planta hay otra pieza que arranca justo donde esta acaba, entonces esto no es un pretil: es
///     el <b>trozo de abajo</b> de algo más alto, y ese algo sí llega a la losa. Es lo que
///     distingue un pretil de un antepecho de ventana con su panel encima, y de una columna que el
///     modelador partió en dos.
///   </item>
/// </list>
/// <para>
/// Con eso, cada pieza cae donde debe y <b>ninguna se mueve por error</b>:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Un muro completo</b> de piso a techo falla la 1: su tapa <i>es</i> la elevación de su
///     piso. No se toca, que es lo que se pidió.
///   </item>
///   <item>
///     <b>Una columna normal</b> de piso a piso falla la 1, por lo mismo. No se toca.
///   </item>
///   <item>
///     <b>Una viga o una cadena de cerramiento</b> están <i>a la altura</i> de la losa de su piso,
///     así que fallan la 1. No se tocan.
///   </item>
///   <item>
///     <b>Un dintel</b> —el panel de encima de la puerta a la losa— falla la 1: su tapa es la
///     losa. No se toca.
///   </item>
///   <item>
///     <b>Un antepecho de ventana</b> con su panel encima falla la 3: hay pared arriba que sí llega
///     a la losa. No se toca.
///   </item>
///   <item>
///     <b>Una columna partida en dos</b> por el modelador falla la 3: su mitad de arriba arranca
///     donde acaba esta. No se toca, así que la columna no se queda repartida entre dos plantas.
///   </item>
///   <item>
///     <b>Un muro o un castillo de corrido por dos pisos</b> fallan la 1 y la 2.
///   </item>
///   <item>
///     <b>El pretil, sus castillos y su cadena de remate</b> cumplen las tres. Y bajarlos es lo
///     correcto: se paran en esa losa, no sostienen la de arriba y en la obra se construyen con
///     ese piso.
///   </item>
/// </list>
///
/// <para><b>DÓNDE SE APLICA, Y POR QUÉ AHÍ</b></para>
/// <para>
/// Reescribiendo el <c>Story</c> de la pieza, una sola vez, <b>antes de que nadie filtre por
/// nivel</b>. Y eso importa porque hay <b>dos</b> filtros por nivel independientes: el que arma la
/// planta para AutoCAD y el del lienzo de la vista previa. Arreglando solo uno, el plano y lo que
/// se ve en pantalla dirían cosas distintas. Hay precedente de reescribir <c>Story</c> así en
/// <see cref="ModeloEtabs.NivelesDesdeZ"/>, que es lo que hace con los modelos de SAP2000.
/// </para>
/// <para>
/// Y es <b>idempotente</b>: aplicado dos veces no baja el pretil dos pisos. Después de bajarlo, su
/// tapa ya no queda por debajo de la elevación de su nuevo piso —queda por encima, porque el pretil
/// sobresale de esa losa—, así que falla la primera condición y se queda quieto.
/// </para>
/// </remarks>
public static class Pretil
{
    /// <summary>Una pieza que se bajó de piso, para poder avisar de qué se movió.</summary>
    /// <param name="Pieza">El muro, la columna o la viga.</param>
    /// <param name="DeNivel">El piso al que ETABS la tenía asignada.</param>
    /// <param name="ANivel">El piso al que se pasó: el que la sostiene.</param>
    /// <param name="SobreLaLosaM">A qué altura queda su tapa sobre esa losa, en metros.</param>
    public sealed record Bajado(
        ElementoEtabs Pieza, string DeNivel, string ANivel, double SobreLaLosaM);

    /// <summary>Cuánto se admite de desajuste al comparar cotas, en metros.</summary>
    public const double ToleranciaM = 0.20;

    /// <summary>Cuánto se admite de desajuste al comparar posiciones en planta, en metros.</summary>
    /// <remarks>
    /// Solo se usa para saber si una pieza <b>continúa hacia arriba</b>. Se compara el punto medio
    /// en planta, que en un muro o una columna partidos por el modelador coincide.
    /// </remarks>
    public const double ToleranciaEnPlantaM = 0.10;

    /// <summary>Altura máxima sobre la losa, en metros, para tomar la pieza como pretil.</summary>
    /// <remarks>
    /// Un pretil de azotea o de pasillo va entre 0.90 y 1.20 m, y su cadena de remate va en la
    /// tapa. 1.50 deja margen sin empezar a mover medios muros. No hace falta para que la regla sea
    /// correcta: es la prudencia que se pidió con «solo los pretiles».
    /// </remarks>
    public const double AlturaMaximaM = 1.50;

    /// <summary>Las clases que se bajan: muros, columnas y vigas.</summary>
    /// <remarks>
    /// Las tres piezas de un pretil: el muro, sus castillos y su cadena de remate. Una <b>losa</b>
    /// no entra —una losa a media altura es un entrepiso y tiene su propio nivel— y una
    /// <b>diagonal</b> tampoco, porque no se ha visto ninguna formando parte de un pretil y mover
    /// un contraviento de piso sería cambiar la estructura de sitio.
    /// </remarks>
    public static bool ClaseQueSeBaja(ClaseElemento clase) =>
        clase is ClaseElemento.Muro or ClaseElemento.Columna or ClaseElemento.Trabe;

    /// <summary>La cota de abajo y la de arriba de la pieza, de sus vértices si los trae.</summary>
    /// <remarks>
    /// Se prefieren los vértices 3D a <c>Z1</c>/<c>Z2</c> porque son el dato de origen; el lector
    /// ya pone <c>Z1</c>/<c>Z2</c> a ese mínimo y máximo para los shells, pero un panel que llegue
    /// de otro camino podría traerlos como las cotas de dos vértices cualesquiera, y entonces un
    /// muro saldría de altura cero. Las barras no traen vértices 3D y se resuelven con
    /// <c>Z1</c>/<c>Z2</c>, que en una barra son las cotas de sus dos nudos.
    /// </remarks>
    public static (double Abajo, double Arriba) CotasDe(ElementoEtabs el)
    {
        if (el.Vertices3D.Count > 0)
        {
            return (el.Vertices3D.Min(v => v.Z), el.Vertices3D.Max(v => v.Z));
        }

        return (Math.Min(el.Z1, el.Z2), Math.Max(el.Z1, el.Z2));
    }

    /// <summary>El punto medio en planta de la pieza.</summary>
    private static (double X, double Y) EnPlanta(ElementoEtabs el) =>
        ((el.X1 + el.X2) / 2, (el.Y1 + el.Y2) / 2);

    /// <summary>
    /// ¿Hay otra pieza en el mismo sitio que <b>arranca donde esta acaba</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la condición 3, y es la que distingue un pretil de dos cosas que se le parecen mucho:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     un <b>antepecho de ventana</b>, que lleva encima el panel que sí llega a la losa;
    ///   </item>
    ///   <item>
    ///     una <b>columna partida en dos</b> por el modelador, casi siempre porque ahí llega una
    ///     cadena intermedia.
    ///   </item>
    /// </list>
    /// <para>
    /// En los dos casos el trozo de abajo <b>no</b> es un pretil: es el pie de algo que sí sostiene
    /// la losa de arriba. Bajarlo dejaría la pieza repartida entre dos plantas.
    /// </para>
    /// <para>
    /// Solo se pregunta por piezas de la <b>misma clase</b>: encima de un castillo de pretil puede
    /// pasar una viga, y eso no convierte al castillo en el pie de nada.
    /// </para>
    /// </remarks>
    public static bool ContinuaArriba(
        ModeloEtabs m, ElementoEtabs el, double tolM = ToleranciaM,
        double tolPlantaM = ToleranciaEnPlantaM)
    {
        var (_, arriba) = CotasDe(el);
        var (x, y) = EnPlanta(el);

        foreach (var otro in m.Elementos)
        {
            if (ReferenceEquals(otro, el) || otro.Clase != el.Clase)
            {
                continue;
            }

            var (abajoOtro, arribaOtro) = CotasDe(otro);

            // Que ARRANQUE donde esta acaba. Y que suba de verdad: una pieza plana apoyada en la
            // tapa —la cadena de remate del propio pretil— no es una continuación.
            if (Math.Abs(abajoOtro - arriba) > tolM || arribaOtro - abajoOtro <= tolM)
            {
                continue;
            }

            var (xo, yo) = EnPlanta(otro);

            if (Math.Abs(xo - x) <= tolPlantaM && Math.Abs(yo - y) <= tolPlantaM)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ¿Esta pieza es del <b>pretil</b> del piso de abajo en lugar de ser del suyo?
    /// </summary>
    /// <param name="m">El modelo, para saber si la pieza continúa hacia arriba.</param>
    /// <param name="el">La pieza.</param>
    /// <param name="suyo">El nivel al que ETABS la asignó, con su elevación y su entrepiso.</param>
    /// <param name="losaDeAbajo">La elevación de la losa sobre la que se para.</param>
    /// <param name="tolM">Ver <see cref="ToleranciaM"/>.</param>
    /// <param name="alturaMaxM">Ver <see cref="AlturaMaximaM"/>.</param>
    public static bool EsDelPretil(
        ModeloEtabs m,
        ElementoEtabs el,
        NivelEtabs suyo,
        double losaDeAbajo,
        double tolM = ToleranciaM,
        double alturaMaxM = AlturaMaximaM)
    {
        if (!ClaseQueSeBaja(el.Clase))
        {
            return false;
        }

        var (abajo, arriba) = CotasDe(el);

        // ---- 1) NO LLEGA A SU PROPIA LOSA ----
        //
        // Es la condición que hace casi todo el trabajo, y la que salva a los MUROS COMPLETOS,
        // a las COLUMNAS de piso a piso, a las VIGAS y a los DINTELES: la tapa de todos ellos ES
        // la elevación de su piso, así que aquí se van.
        if (suyo.ElevacionM - arriba <= tolM)
        {
            return false;
        }

        // ---- 2) SE QUEDA BAJO RESPECTO A LA LOSA QUE LA SOSTIENE ----
        //
        // Se mide desde la losa de abajo y no desde el pie de la pieza, y a propósito: la cadena
        // de remate de un pretil no se apoya en la losa —flota a un metro—, así que medir su
        // propia altura no diría nada. Lo que importa es a qué altura del piso está.
        var sobreLaLosa = arriba - losaDeAbajo;

        if (sobreLaLosa < -tolM || sobreLaLosa > alturaMaxM)
        {
            return false;
        }

        // ---- 3) NO CONTINÚA HACIA ARRIBA ----
        //
        // Solo tiene sentido en lo que se apila: un muro con su antepecho, una columna partida.
        // Una viga no lleva otra viga encima.
        if (el.Clase is ClaseElemento.Muro or ClaseElemento.Columna
            && ContinuaArriba(m, el, tolM))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Baja al piso que las sostiene las piezas de pretil que ETABS dejó un nivel arriba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devuelve las que se movieron para poder avisar. No mueve nada si no encuentra un nivel por
    /// debajo: antes que adivinar un destino, se queda como estaba, que es un defecto conocido y no
    /// una pieza en un sitio inventado.
    /// </para>
    /// <para>
    /// Los niveles se toman de <see cref="ModeloEtabs.NivelesConElementos"/> y no de
    /// <c>Niveles</c> a secas porque la API de ETABS <b>no devuelve la base</b>, y un pretil
    /// apoyado en la losa de planta baja tiene que poder bajar hasta ella.
    /// </para>
    /// </remarks>
    public static List<Bajado> Bajar(
        ModeloEtabs m,
        double tolM = ToleranciaM,
        double alturaMaxM = AlturaMaximaM)
    {
        var movidos = new List<Bajado>();

        var niveles = TodosLosNiveles(m);

        if (niveles.Count < 2)
        {
            return movidos;
        }

        NivelEtabs? Nivel(string nombre) => niveles.FirstOrDefault(
            n => string.Equals(n.Nombre.Trim(), nombre.Trim(), StringComparison.OrdinalIgnoreCase));

        // Se recorre una COPIA porque ContinuaArriba mira la lista entera, y así lo que ve no
        // depende de cuántas piezas se hayan movido ya. Sin esto, el resultado cambiaría con el
        // orden de la lista.
        foreach (var el in m.Elementos.ToList())
        {
            if (!ClaseQueSeBaja(el.Clase) || el.Story.Trim().Length == 0)
            {
                continue;
            }

            var suyo = Nivel(el.Story);

            if (suyo is null)
            {
                continue;
            }

            var (abajo, arriba) = CotasDe(el);

            // El destino es el nivel cuya losa la sostiene: el más alto que quede por debajo de
            // su pie. Se busca por ELEVACIÓN y no cogiendo «el anterior de la lista», porque si un
            // nivel intermedio no tiene elementos el anterior de la lista no es el que está debajo.
            var destino = niveles
                .Where(n => n.ElevacionM <= abajo + tolM
                            && n.ElevacionM < suyo.ElevacionM - tolM)
                .OrderByDescending(n => n.ElevacionM)
                .FirstOrDefault();

            if (destino is null
                || string.Equals(destino.Nombre.Trim(), el.Story.Trim(),
                                 StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!EsDelPretil(m, el, suyo, destino.ElevacionM, tolM, alturaMaxM))
            {
                continue;
            }

            movidos.Add(new Bajado(el, el.Story, destino.Nombre, arriba - destino.ElevacionM));

            el.Story = destino.Nombre;
        }

        return movidos;
    }

    /// <summary>Todos los niveles del modelo, incluida la base que la API no devuelve.</summary>
    private static List<NivelEtabs> TodosLosNiveles(ModeloEtabs m)
    {
        var niveles = m.NivelesConElementos(ascendente: true);

        foreach (var n in m.Niveles)
        {
            if (!niveles.Any(
                    x => string.Equals(x.Nombre, n.Nombre, StringComparison.OrdinalIgnoreCase)))
            {
                niveles.Add(n);
            }
        }

        return niveles.OrderBy(n => n.ElevacionM).ToList();
    }

    /// <summary>Un aviso legible de lo que se movió, o cadena vacía si no se movió nada.</summary>
    /// <remarks>
    /// Se agrupa por el par de niveles y se dice de qué clase son las piezas, porque un pretil
    /// llega partido en muchas —el mesh corta el muro en cada nudo, y van sus castillos y su
    /// cadena—, y un aviso por pieza serían cuarenta renglones iguales.
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
            {
                var clases = string.Join(", ", g
                    .GroupBy(b => b.Pieza.Clase)
                    .OrderByDescending(c => c.Count())
                    .Select(c => $"{c.Count()} {Nombre(c.Key)}"));

                return $"de {g.Key.DeNivel} a {g.Key.ANivel}: {clases} " +
                       $"(hasta {g.Max(b => b.SobreLaLosaM):0.00} m sobre la losa)";
            });

        return $"{movidos.Count} pieza(s) de PRETIL se pasaron al nivel que las sostiene — " +
               string.Join("; ", porNiveles) +
               ". ETABS asigna cada pieza al piso de su cota más alta, así que un pretil que no " +
               "llega a la losa de arriba, con sus castillos y su cadena de remate, quedaba " +
               "dibujado un nivel por encima de donde está.";
    }

    private static string Nombre(ClaseElemento clase) => clase switch
    {
        ClaseElemento.Muro => "muro(s)",
        ClaseElemento.Columna => "castillo(s) o columna(s)",
        ClaseElemento.Trabe => "viga(s) o cadena(s)",
        _ => clase.ToString()
    };
}
