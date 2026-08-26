namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// Las capas del plano estructural, <b>las de la macro y con sus colores</b>.
/// </summary>
/// <remarks>
/// <para>
/// Es la suma de <c>DefinirCapas</c> —las diez capas por tipo de elemento— y de las que
/// <c>CrearCapas</c> agrega aparte: texto, título, ejes, burbujas, armado de losa,
/// mampostería, cadena de desplante, piers, losacero y cotas.
/// </para>
/// <para>
/// <b>Ningún color se inventa ni se cambia.</b> Los que en la macro están escritos en el
/// código —MURO 6, COLUMNA 1, TRABE 3, CONTRATRABE 2, LOSA 8, DIAGONAL 30, OTROS 7,
/// TEXTO 7— están aquí con ese número; los que salen de la hoja CONFIG se leen de la hoja
/// —<c>COLOR_CASTILLO</c>, <c>COLOR_DALA</c>, <c>COLOR_ACERO</c>, <c>COLOR_ARMADO_LOSA</c>,
/// <c>COLOR_MAMPOSTERIA</c>, <c>COLOR_CADENA_DESPLANTE</c>, <c>COLOR_PIERS</c>,
/// <c>COLOR_LOSACERO</c>, <c>COLOR_COTAS</c>, <c>COLOR_EJES</c>,
/// <c>COLOR_BURBUJA_EJES</c>, <c>COLOR_EJES_TEXTO</c>, <c>COLOR_TITULO</c>— con los mismos
/// topes que <c>LeerConfig</c>: fuera del 1 al 255 se regresa al valor de la macro.
/// </para>
/// <para>
/// <b>El prefijo.</b> Todas llevan el <c>PREFIJO_CAPAS</c> —<c>E-</c>— menos una: la de los
/// <b>piers</b>, que en la macro es <c>PIERS</c> a secas. Ese detalle importa porque
/// <c>BorrarCapasGeneradas</c> borra por prefijo y tiene que acordarse de PIERS aparte.
/// </para>
/// </remarks>
public sealed class CapasPlano
{
    /// <summary>Una capa: su nombre completo, su color y su tipo de línea.</summary>
    /// <param name="Tipo">
    /// El tipo de elemento que va en ella —MURO, COLUMNA, CASTILLO…— o cadena vacía si es
    /// una capa de servicio, como las de texto o las de ejes.
    /// </param>
    /// <param name="Nombre">El nombre completo, ya con el prefijo.</param>
    /// <param name="Color">El índice de color de AutoCAD, el de la macro.</param>
    /// <param name="TipoDeLinea">El tipo de línea, o vacío para no tocar el del dibujo.</param>
    public sealed record Capa(string Tipo, string Nombre, int Color, string TipoDeLinea);

    private readonly ConfigPlano _cfg;

    /// <summary>El <c>PREFIJO_CAPAS</c> de la hoja: <c>E-</c>.</summary>
    public string Prefijo { get; }

    /// <summary>La tabla completa, en el orden en que la macro crea las capas.</summary>
    public IReadOnlyList<Capa> Todas { get; }

    public CapasPlano(ConfigPlano cfg)
    {
        _cfg = cfg;
        Prefijo = cfg.Texto("PREFIJO_CAPAS", "E-");

        var t = new List<Capa>();

        // ---- DefinirCapas: una capa por tipo de elemento ----------------------------
        // Los colores que aquí van escritos son los de la macro, no una preferencia:
        // en DefinirCapas están así, con el número a la vista.
        t.Add(PorTipo("MURO", 6));
        t.Add(PorTipo("COLUMNA", 1));
        t.Add(PorTipo("CASTILLO", Color("COLOR_CASTILLO", 1)));
        t.Add(PorTipo("TRABE", 3, cfg.Texto("LINETYPE_TRABE", "PHANTOM2")));
        t.Add(PorTipo("CONTRATRABE", 2));
        // LA DALA SE LLAMA E-CADENA. El tipo sigue siendo DALA —es lo que devuelve
        // ClasificaTipo y lo que dice la hoja CONFIG— pero la CAPA se llama como se le llama
        // en obra a la pieza: cadena. Se pidió expresamente, y el nombre se puede volver a
        // cambiar desde la hoja con CAPA_DALA sin tocar el código.
        t.Add(new Capa("DALA", Prefijo + _cfg.Texto("CAPA_DALA", "CADENA"),
                       Color("COLOR_DALA", 12), string.Empty));
        t.Add(PorTipo("LOSA", 8));
        t.Add(PorTipo("DIAGONAL", 30));
        t.Add(PorTipo("OTROS", 7));

        // El ACERO se define en DefinirCapas sin tipo de línea, pero CrearCapas se lo
        // pone después con LINETYPE_ACERO —vacío por omisión, o sea «no toques la que ya
        // tenga el dibujo»—. Aquí va una sola vez, ya con ese dato.
        t.Add(PorTipo("ACERO", Color("COLOR_ACERO", 130), cfg.Texto("LINETYPE_ACERO")));

        // ---- CrearCapas: las de servicio -------------------------------------------
        t.Add(Servicio("TEXTO", 7));
        t.Add(Servicio("TITULO", Color("COLOR_TITULO", 7, minimo: 0)));
        t.Add(Servicio("EJES", Color("COLOR_EJES", 8), cfg.Texto("LINETYPE_EJES", "DASHDOT")));
        t.Add(Servicio("EJES-BURBUJA", Color("COLOR_BURBUJA_EJES", 4)));
        t.Add(Servicio("EJES-TEXTO", Color("COLOR_EJES_TEXTO", 6)));
        t.Add(Servicio("ARMADO LOSA", Color("COLOR_ARMADO_LOSA", 142)));
        t.Add(Servicio("MAMPOSTERIA", Color("COLOR_MAMPOSTERIA", 30)));

        // La cadena de desplante de la planta de cimentación. Va SIN tipo de línea a
        // propósito: en ese nivel nunca se usa la punteada de «cadena sin muro abajo».
        t.Add(new Capa(string.Empty, CapaCadenaDesplante, Color("COLOR_CADENA_DESPLANTE", 1),
                       string.Empty));

        // Y la de los piers, la única sin prefijo.
        t.Add(new Capa(string.Empty, CapaPiers, Color("COLOR_PIERS", 7), string.Empty));

        // LA LOSA EN VOLADIZO, EN SU PROPIA CAPA. En la macro el volado va en la capa del
        // armado; aquí se pidió aparte —E-VOLADO— para poder apagar E-LOSA y quedarse solo
        // con los volados, que es lo que se revisa en obra.
        t.Add(new Capa(string.Empty, CapaVolado, Color("COLOR_VOLADO", 4), string.Empty));

        t.Add(Servicio("LOSACERO", Color("COLOR_LOSACERO", 6)));
        t.Add(Servicio("COTAS", Color("COLOR_COTAS", 8)));

        Todas = t;
    }

    private Capa PorTipo(string tipo, int color, string tipoDeLinea = "") =>
        new(tipo, Prefijo + tipo, color, tipoDeLinea);

    private Capa Servicio(string nombre, int color, string tipoDeLinea = "") =>
        new(string.Empty, Prefijo + nombre, color, tipoDeLinea);

    /// <summary>
    /// Un color de la hoja, con el tope de <c>LeerConfig</c>: fuera de rango se regresa al
    /// de la macro.
    /// </summary>
    /// <remarks>
    /// El mínimo es 1 en todos menos en <c>COLOR_TITULO</c>, que acepta el 0 porque ahí el
    /// 0 significa «negro de verdad», el que la macro pone con <c>trueColor</c>.
    /// </remarks>
    private int Color(string parametro, int omision, int minimo = 1)
    {
        var c = (int)_cfg.Numero(parametro, omision);
        return c < minimo || c > 255 ? omision : c;
    }

    /// <summary>
    /// Nombre completo de la capa de la cadena de desplante: <c>E-CADENA DESPLANTE</c>.
    /// </summary>
    /// <remarks>
    /// Se le pone el prefijo <b>solo si no lo trae ya</b>, igual que
    /// <c>CapaCadenaDesplante</c> de la macro: así el usuario puede escribir en la hoja
    /// <c>CADENA DESPLANTE</c> o <c>E-CADENA DESPLANTE</c> y las dos formas valen.
    /// </remarks>
    public string CapaCadenaDesplante
    {
        get
        {
            var s = _cfg.Texto("CAPA_CADENA_DESPLANTE", "CADENA DESPLANTE");
            if (s.Length == 0)
            {
                s = "CADENA DESPLANTE";
            }

            if (Prefijo.Length > 0 &&
                !s.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase))
            {
                s = Prefijo + s;
            }

            return s;
        }
    }

    /// <summary>La capa de la losa en voladizo: <c>E-VOLADO</c>.</summary>
    public string CapaVolado
    {
        get
        {
            var s = _cfg.Texto("CAPA_VOLADO", "VOLADO");

            if (s.Length == 0)
            {
                s = "VOLADO";
            }

            return Prefijo.Length > 0 &&
                   !s.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase)
                ? Prefijo + s
                : s;
        }
    }

    /// <summary>
    /// Las capas que se dejan <b>apagadas</b> al terminar el dibujo.
    /// </summary>
    /// <remarks>
    /// Solo <c>E-LOSA</c>, y con <c>APAGAR_CAPA_LOSA</c>. Se pidió así: el contorno de todos
    /// los paños llena el plano y estorba para revisar, mientras que <b>E-VOLADO se queda
    /// encendida</b>, que es la que interesa ver. Apagada y no congelada, para que el usuario
    /// la encienda con un clic sin regenerar.
    /// </remarks>
    /// <summary>
    /// Las capas que se mandan al <b>fondo</b>: la losa, su armado y el voladizo.
    /// </summary>
    /// <remarks>
    /// Es la otra mitad del orden de dibujo, y la que faltaba: da igual cuántas veces se suba
    /// la cadena al frente si el achurado del voladizo y la rejilla del armado se dibujaron
    /// después. <b>Bajar lo de abajo</b> es tan válido como subir lo de arriba, y haciendo las
    /// dos cosas el resultado se ve aunque una de ellas no llegue a aplicarse.
    /// </remarks>
    public IReadOnlyList<string> CapasAlFondo() =>
        ListaConPrefijo(_cfg.Texto("CAPAS_AL_FONDO", "LOSA,ARMADO LOSA,VOLADO,LOSACERO,EJES"));

    public IReadOnlyList<string> CapasApagadas() =>
        _cfg.Bandera("APAGAR_CAPA_LOSA", true)
            ? new[] { CapaDeTipo("LOSA") }
            : Array.Empty<string>();

    /// <summary>La capa del pier de los muros: <c>PIERS</c>, sin prefijo.</summary>
    public string CapaPiers
    {
        get
        {
            var s = _cfg.Texto("CAPA_PIERS", "PIERS");
            return s.Length == 0 ? "PIERS" : s;
        }
    }

    /// <summary>
    /// La capa que le toca a un tipo de elemento; <c>E-OTROS</c> si no está en la tabla.
    /// </summary>
    /// <remarks>Es el <c>CapaDeTipo</c> de la macro, con la misma salida por omisión.</remarks>
    public string CapaDeTipo(string tipo)
    {
        // ==============================================================================
        //  LAS TRES CADENAS VAN A LAS CAPAS DE LAS CADENAS
        // ==============================================================================
        //  Desde que el tipo sale de las notas de la propiedad, una cadena puede llegar aquí
        //  como CADENA DE CERRAMIENTO, CADENA DE DESPLANTE o CADENA INTERMEDIA. Ninguno de
        //  esos nombres es el de una capa —las capas son E-CADENA y E-CADENA DESPLANTE— así
        //  que sin esta traducción las tres se irían a E-OTROS, que es peor que antes: se
        //  dibujarían, pero en una capa que nadie mira.
        //
        //  La de DESPLANTE tiene capa propia porque en el plano de cimentación se distingue
        //  del resto; las otras dos van a la de las cadenas, que es donde van las dalas.
        var t = (tipo ?? string.Empty).Trim();

        if (t.StartsWith("CADENA", StringComparison.OrdinalIgnoreCase))
        {
            return t.Contains("DESPLANTE", StringComparison.OrdinalIgnoreCase)
                ? CapaCadenaDesplante
                : CapaDeTipo("DALA");
        }

        // ==============================================================================
        //  EL CABEZAL VA CON LAS TRABES
        // ==============================================================================
        //  Se pidió leer CABEZAL de las notas, y como tipo ya sale: la tabla de secciones lo
        //  dice y el plano lo distingue. Pero CABEZAL no es el nombre de ninguna capa, así que
        //  sin esta traducción se iría a E-OTROS —se dibujaría, pero en una capa que nadie
        //  mira—, que es lo mismo que les pasaba a las tres cadenas.
        //
        //  Va a la de las TRABES porque un cabezal es eso, una viga: la que cierra un vano o la
        //  que reparte sobre los apoyos. Con eso hereda su color, su PHANTOM2 y su sitio en
        //  CAPAS_AL_FRENTE. Si algún día quiere su propia capa, se añade a la tabla con su
        //  color y esta traducción sobra.
        if (t.Equals("CABEZAL", StringComparison.OrdinalIgnoreCase))
        {
            return CapaDeTipo("TRABE");
        }

        foreach (var c in Todas)
        {
            if (c.Tipo.Length > 0 && string.Equals(c.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            {
                return c.Nombre;
            }
        }

        return Prefijo + "OTROS";
    }

    /// <summary>
    /// Las capas que van <b>encima de todo</b> al terminar el dibujo, ya con su prefijo.
    /// </summary>
    /// <remarks>
    /// Es <c>ListaDeCapasAlFrente</c>: <c>CAPAS_AL_FRENTE</c> es una lista separada por
    /// comas —DALA, CADENA DESPLANTE, TRABE y ACERO— y a cada nombre se le pone el prefijo
    /// si no lo trae. Si la lista se queda vacía, la macro deja al menos <c>DALA</c>.
    /// </remarks>
    public IReadOnlyList<string> CapasAlFrente()
    {
        var salida = ListaConPrefijo(
            _cfg.Texto("CAPAS_AL_FRENTE", "DALA,CADENA DESPLANTE,TRABE,ACERO"));

        if (salida.Count == 0)
        {
            // El respaldo de la macro: al menos la de las dalas, que ahora se llama E-CADENA.
            salida.Add(CapaDeTipo("DALA").ToUpperInvariant());
        }

        return salida;
    }

    /// <summary>
    /// Las capas de <b>TEXTO</b> que van encima de todo, <i>después</i> de la geometría.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Van en una lista aparte, y en una <b>segunda pasada</b> del orden de dibujo, porque el
    /// orden importa: si los rótulos se subieran junto con las trabes y las dalas, unas veces
    /// quedarían encima y otras debajo, según en qué orden los encontrara el recorrido del
    /// dibujo. Subiendo primero la geometría y luego los textos, los textos quedan
    /// <b>siempre</b> arriba, que es lo que se pidió: los rótulos se leen aunque les pase por
    /// debajo un muro o una parrilla.
    /// </para>
    /// <para>
    /// <c>PIERS</c> se queda <b>sin prefijo</b>, como en la macro: es la única capa generada
    /// que no lo lleva, y ponerle <c>E-</c> dejaba los piers fuera del orden de dibujo sin
    /// que se notara por qué.
    /// </para>
    /// <para>
    /// <b>Va vacía por omisión</b>, y es a propósito: el MTEXT tiene que quedar encima de la
    /// polilínea de mampostería —para eso lleva fondo— pero <b>debajo</b> de las líneas de la
    /// cadena y del acero. Eso sale solo con el orden en que se dibuja, así que subir también
    /// el texto lo dejaba encima de las líneas, que es lo contrario de lo que se pidió.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CapasDeTextoAlFrente() =>
        ListaConPrefijo(_cfg.Texto("CAPAS_TEXTO_AL_FRENTE", string.Empty));

    /// <summary>
    /// Parte una lista de la hoja por comas y le pone el prefijo a lo que le toca.
    /// </summary>
    /// <remarks>
    /// La excepción es la capa de los <b>piers</b>: se escribe tal cual, porque en la macro
    /// es <c>PIERS</c> a secas. Lo mismo valdría para cualquier otro nombre que el usuario ya
    /// escriba con su prefijo.
    /// </remarks>
    private List<string> ListaConPrefijo(string lista)
    {
        var salida = new List<string>();
        var piers = CapaPiers.Trim().ToUpperInvariant();
        var pref = Prefijo.ToUpperInvariant();

        foreach (var pieza in lista.Split(','))
        {
            var s = pieza.Trim().ToUpperInvariant();

            if (s.Length == 0)
            {
                continue;
            }

            if (pref.Length > 0 && s != piers &&
                !s.StartsWith(pref, StringComparison.Ordinal))
            {
                s = pref + s;
            }

            if (!salida.Contains(s))
            {
                salida.Add(s);
            }
        }

        return salida;
    }

    /// <summary>
    /// ¿Es un perfil de acero? Es el <c>EsPerfilAcero</c> de la macro, y decide una cosa:
    /// que el elemento se vaya a la capa <c>E-ACERO</c> en lugar de a la de su tipo.
    /// </summary>
    /// <remarks>
    /// Está aquí y también en <c>CadLink.Etabs.SeccionesModelo</c>, y el duplicado es a
    /// propósito: los dos proyectos son independientes —ninguno referencia al otro— y este
    /// es el único sitio del dibujante donde hace falta. Son seis nombres de forma; darles
    /// un proyecto común para compartirlos costaría más de lo que ahorra.
    /// </remarks>
    public static bool EsPerfilAcero(string forma) =>
        forma is "I" or "TUBO" or "CAJON" or "PIPE" or "C" or "T" or "L";

    /// <summary>
    /// ¿Esta capa la generó el plano? Es la regla de <c>BorrarCapasGeneradas</c>.
    /// </summary>
    /// <remarks>
    /// Todo lo que lleve el prefijo, más <c>PIERS</c> y la de la cadena de desplante, que
    /// pueden no llevarlo. <b>Ojo:</b> la macro borra por capa y eso se lleva de paso lo
    /// que el usuario haya puesto a mano ahí; cuando se porte el borrado hay que marcar lo
    /// generado con XData propio, como dice <c>docs/macro-plantas-etabs.md</c> §5.4.
    /// </remarks>
    public bool EsCapaGenerada(string capa)
    {
        var l = capa.Trim().ToUpperInvariant();
        if (l.Length == 0)
        {
            return false;
        }

        if (Prefijo.Length > 0 &&
            l.StartsWith(Prefijo.ToUpperInvariant(), StringComparison.Ordinal))
        {
            return true;
        }

        return l == CapaPiers.ToUpperInvariant()
            || l == CapaCadenaDesplante.ToUpperInvariant();
    }
}
