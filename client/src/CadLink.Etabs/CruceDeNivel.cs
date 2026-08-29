namespace CadLink.Etabs;

/// <summary>
/// Qué pieza <b>cruza</b> un nivel: el corte a la cota del piso y lo que hay debajo.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió así: «haz el corte al nivel story y todo lo que haya debajo de ese nivel se dibuja en
/// ese story». Eso es exactamente la convención de un plano estructural: la planta de un nivel es
/// un corte a su cota, y lo que se ve es lo que hay <b>entre ese nivel y el de abajo</b>.
/// </para>
///
/// <para><b>QUÉ FALLABA</b></para>
/// <para>
/// La planta se armaba <b>solo</b> con las piezas que ETABS tenía asignadas a ese story. Y ETABS
/// asigna cada pieza al piso de su <b>cota más alta</b>, así que un muro o un castillo dibujado
/// <b>de corrido por dos niveles</b> es de un solo story —el de arriba— y desaparecía del plano de
/// abajo, aunque en el nivel de abajo ese muro esté ahí y haya que verlo.
/// </para>
/// <para>
/// Ya estaba resuelto para <b>un</b> caso, y con este mismo razonamiento escrito: los castillos de
/// shell se recogían de otros niveles midiendo lo que cubren del entrepiso, y el comentario decía
/// «un castillo de tres niveles lo cubre entero en los tres y sale en las tres plantas —que es lo
/// correcto, en las tres hay castillo—». Lo que faltaba era que eso valiera para <b>todo</b> y no
/// solo para los castillos.
/// </para>
///
/// <para><b>LA MEDIDA: CUÁNTO CUBRE DEL ENTREPISO</b></para>
/// <para>
/// No vale preguntar «¿toca este nivel?», porque entonces una pieza que solo <b>asoma</b> un
/// centímetro por debajo del piso saldría dibujada en él, y la misma pieza saldría en dos plantas
/// por un asomo. Lo que se mide es cuánto del entrepiso cubre de verdad, y se exige una
/// <b>fracción</b> de él. Con 0.75, una pieza tiene que subir tres cuartas partes del nivel para
/// contar en él.
/// </para>
/// <para>
/// Y esa misma fracción es la que hace que el <b>pretil no se duplique</b>. Un pretil de un metro
/// en un entrepiso de 2.80 cubre 1.00 de 2.10 exigidos, así que no aparece en la planta de arriba;
/// aparece solo en la de la losa sobre la que se para, que es donde lo puso <see cref="Pretil"/>.
/// Las dos reglas encajan sin tener que conocerse.
/// </para>
///
/// <para><b>QUÉ CLASES CRUZAN</b></para>
/// <para>
/// Solo lo que tiene <b>altura</b>: muros, columnas y diagonales. Una <b>viga</b> y una <b>losa</b>
/// están a una sola cota, así que no cubren nada de ningún entrepiso —su cuenta daría cero— y
/// pertenecen al nivel que ETABS ya les da. Dejarlas fuera no cambia el resultado; lo que hace es
/// que quede dicho, en lugar de depender de que la aritmética dé cero.
/// </para>
/// </remarks>
public static class CruceDeNivel
{
    /// <summary>Fracción del entrepiso que hay que cubrir para contar en el nivel.</summary>
    /// <remarks>
    /// Es el mismo 0.75 de <c>MURO_FRACCION_ENTREPISO</c>, que ya se usaba para recoger los
    /// castillos de otros niveles. Se repite aquí como valor por omisión porque este proyecto no ve
    /// la configuración del plano —vive en CadLink.Cad— y quien llama puede pasar el de la hoja.
    /// </remarks>
    public const double FraccionPorOmision = 0.75;

    /// <summary>Las clases que pueden cruzar un nivel: las que tienen altura.</summary>
    public static bool ClaseQueCruza(ClaseElemento clase) =>
        clase is ClaseElemento.Muro or ClaseElemento.Columna or ClaseElemento.Diagonal;

    /// <summary>
    /// Cuánto del tramo <paramref name="zBaja"/>–<paramref name="zAlta"/> cubre la pieza.
    /// </summary>
    /// <remarks>
    /// El solape de dos intervalos. Sale <b>cero o negativo</b> cuando la pieza está entera fuera
    /// del tramo, y ahí no hay que sumar nada: el signo ya lo dice.
    /// </remarks>
    public static double Cubre(ElementoEtabs el, double zBaja, double zAlta)
    {
        var (abajo, arriba) = Pretil.CotasDe(el);

        return Math.Min(arriba, zAlta) - Math.Max(abajo, zBaja);
    }

    /// <summary>
    /// ¿Esta pieza hay que dibujarla en este nivel <b>aunque ETABS la tenga en otro</b>?
    /// </summary>
    /// <param name="el">La pieza.</param>
    /// <param name="n">El nivel, con su cota y su altura de entrepiso.</param>
    /// <param name="fraccion">Ver <see cref="FraccionPorOmision"/>.</param>
    public static bool CruzaBastante(
        ElementoEtabs el, NivelEtabs n, double fraccion = FraccionPorOmision)
    {
        if (!ClaseQueCruza(el.Clase))
        {
            return false;
        }

        // Sin la altura del entrepiso no hay tramo que comparar, y adivinarlo metería piezas de
        // otros pisos en la planta.
        if (n.AlturaM <= 0)
        {
            return false;
        }

        var f = fraccion is > 0 and <= 1 ? fraccion : FraccionPorOmision;

        var zAlta = n.ElevacionM;
        var zBaja = zAlta - n.AlturaM;

        return Cubre(el, zBaja, zAlta) >= n.AlturaM * f;
    }

    /// <summary>Las cotas recortadas al entrepiso, para el corte.</summary>
    /// <remarks>
    /// Una pieza de tres niveles se dibuja en las tres plantas, pero en cada una el <b>corte</b>
    /// tiene que verla del alto de <i>ese</i> entrepiso y no de los tres; si no, el alzado saldría
    /// con la pieza saliéndose del nivel.
    /// </remarks>
    public static (double Z1, double Z2) RecortadaAlNivel(ElementoEtabs el, NivelEtabs n)
    {
        var (abajo, arriba) = Pretil.CotasDe(el);

        var zAlta = n.ElevacionM;
        var zBaja = zAlta - n.AlturaM;

        return (Math.Max(abajo, zBaja), Math.Min(arriba, zAlta));
    }
}
