using System.Globalization;

namespace CadLink.Cad;

// ============================================================================
//  EL ESTILO DE LOS DIBUJOS: LETRAS, COTAS Y COLORES
//
//  Un PERFIL por tipo de dibujo -secciones, zapatas, muros, placa base, planta-, cada uno con
//  sus ajustes. Los valores POR DEFECTO son exactamente los que los dibujantes tenian escritos
//  como constantes: sin tocar nada, todo sale igual que antes. La ventana «Estilo de dibujo» de
//  CadLink cambia los valores; los dibujantes los leen de EstiloDibujo.Actual al dibujar.
//
//  Este archivo no sabe nada de AutoCAD ni de WPF, y solo depende de CapasCad -la tabla de
//  colores de la macro, que es el defecto del perfil comun-. Asi se prueba solo.
// ============================================================================

/// <summary>Que clase de valor guarda un ajuste: decide el editor y la validacion.</summary>
public enum TipoAjuste
{
    /// <summary>El nombre de una fuente de Windows.</summary>
    Fuente,

    /// <summary>Una medida en unidades de dibujo, mayor que cero.</summary>
    Medida,

    /// <summary>Un color ACI de AutoCAD, de 0 a 256.</summary>
    ColorAci,

    /// <summary>El bloque de las marcas de cota: <c>_OPEN90</c>, <c>_OBLIQUE</c>…</summary>
    Marca
}

/// <summary>Un ajuste: su valor y su valor por defecto.</summary>
public sealed class AjusteEstilo
{
    public AjusteEstilo(string clave, string grupo, string nombre, TipoAjuste tipo, string defecto, string ayuda = "")
    {
        Clave = clave;
        Grupo = grupo;
        Nombre = nombre;
        Tipo = tipo;
        Defecto = defecto;
        Ayuda = ayuda;
        Valor = defecto;
    }

    /// <summary>Con lo que lo busca el dibujante: <c>cota.alto</c>, <c>capa.COTAS</c>…</summary>
    public string Clave { get; }

    /// <summary><see cref="EstiloDibujo.GrupoLetras"/>, <see cref="EstiloDibujo.GrupoCotas"/> o <see cref="EstiloDibujo.GrupoColores"/>.</summary>
    public string Grupo { get; }

    /// <summary>Lo que lee el usuario.</summary>
    public string Nombre { get; }

    public TipoAjuste Tipo { get; }

    public string Defecto { get; }

    public string Ayuda { get; }

    public string Valor { get; set; }

    /// <summary>¿El usuario lo cambio?</summary>
    public bool Cambiado => !string.Equals(Normalizar(Valor), Normalizar(Defecto), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Que esta mal en <paramref name="valor"/>, o vacio si sirve. La ventana lo usa antes de
    /// guardar; al cargar el archivo, un valor que no sirve se ignora y queda el defecto.
    /// </summary>
    public string Problema(string? valor)
    {
        var v = (valor ?? string.Empty).Trim();

        switch (Tipo)
        {
            case TipoAjuste.Fuente:
                return v.Length == 0 ? "falta el nombre de la fuente" : string.Empty;

            case TipoAjuste.Medida:
                // El 0 vale solo donde el 0 es el defecto: ahi quiere decir «automatica».
                return EstiloDibujo.LeerNumero(v) is double n && n < 1000
                       && (n > 0 || (n == 0 && EstiloDibujo.LeerNumero(Defecto) == 0))
                    ? string.Empty
                    : "tiene que ser un numero mayor que cero, por ejemplo 0.08";

            case TipoAjuste.ColorAci:
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c >= 0 && c <= 256
                    ? string.Empty
                    : "tiene que ser un color de AutoCAD, de 0 a 256";

            case TipoAjuste.Marca:
                return EstiloDibujo.Marcas.Any(m => string.Equals(m.Bloque, v, StringComparison.OrdinalIgnoreCase))
                    ? string.Empty
                    : "no es una de las marcas de cota de AutoCAD";

            default:
                return string.Empty;
        }
    }

    private string Normalizar(string? s)
    {
        var t = (s ?? string.Empty).Trim();

        // 0.080 y 0.08 son lo mismo; 0,08 tambien.
        return Tipo == TipoAjuste.Medida && EstiloDibujo.LeerNumero(t) is double n
            ? n.ToString("R", CultureInfo.InvariantCulture)
            : t;
    }
}

/// <summary>Los ajustes de un tipo de dibujo.</summary>
public sealed class PerfilEstilo
{
    private readonly List<AjusteEstilo> _ajustes = new();

    public PerfilEstilo(string clave, string nombre, string descripcion)
    {
        Clave = clave;
        Nombre = nombre;
        Descripcion = descripcion;
    }

    public string Clave { get; }

    public string Nombre { get; }

    public string Descripcion { get; }

    public IReadOnlyList<AjusteEstilo> Ajustes => _ajustes;

    internal PerfilEstilo Con(string clave, string grupo, string nombre, TipoAjuste tipo, string defecto, string ayuda = "")
    {
        _ajustes.Add(new AjusteEstilo(clave, grupo, nombre, tipo, defecto, ayuda));
        return this;
    }

    public AjusteEstilo? Buscar(string clave) =>
        _ajustes.FirstOrDefault(a => string.Equals(a.Clave, clave, StringComparison.OrdinalIgnoreCase));

    private AjusteEstilo Ajuste(string clave) =>
        Buscar(clave) ?? throw new KeyNotFoundException($"El estilo '{Clave}' no tiene el ajuste '{clave}'.");

    /// <summary>El valor en texto: una fuente o una marca.</summary>
    public string Texto(string clave)
    {
        var a = Ajuste(clave);
        return a.Problema(a.Valor).Length == 0 ? a.Valor.Trim() : a.Defecto;
    }

    /// <summary>El valor como numero; si lo que hay no sirve, el defecto.</summary>
    public double Numero(string clave)
    {
        var a = Ajuste(clave);
        return a.Problema(a.Valor).Length == 0 && EstiloDibujo.LeerNumero(a.Valor) is double n
            ? n
            : EstiloDibujo.LeerNumero(a.Defecto) ?? 0;
    }

    /// <summary>El valor como color ACI; si lo que hay no sirve, el defecto.</summary>
    public int ColorAci(string clave)
    {
        var a = Ajuste(clave);
        var v = a.Problema(a.Valor).Length == 0 ? a.Valor : a.Defecto;
        return int.Parse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <summary>¿El usuario cambio este ajuste? Si si, el color de la capa se fuerza.</summary>
    public bool Cambiado(string clave) => Buscar(clave)?.Cambiado ?? false;

    /// <summary>Todo de vuelta a los valores por defecto.</summary>
    public void Restaurar()
    {
        foreach (var a in _ajustes)
        {
            a.Valor = a.Defecto;
        }
    }
}

/// <summary>Una marca de cota que se puede elegir.</summary>
public sealed record MarcaCota(string Bloque, string Nombre);

/// <summary>El estilo de todos los dibujos de CadLink.</summary>
public sealed class EstiloDibujo
{
    // ---------- Los perfiles ----------
    public const string Comun = "comun";
    public const string Secciones = "secciones";
    public const string Acero = "acero";
    public const string Zapatas = "zapatas";
    public const string Muros = "muros";
    public const string PlacaBase = "placa";
    public const string PlantaEtabs = "planta";

    // ---------- Los grupos de la ventana ----------
    public const string GrupoLetras = "Letras";
    public const string GrupoCotas = "Cotas";
    public const string GrupoColores = "Colores de capa";

    /// <summary>
    /// El estilo con que se dibuja. Lo pone CadLink al arrancar -con lo guardado- y al aceptar la
    /// ventana; los dibujantes lo leen en cada dibujo.
    /// </summary>
    public static EstiloDibujo Actual { get; set; } = PorDefecto();

    /// <summary>Las marcas de cota de AutoCAD que ofrece la ventana.</summary>
    public static IReadOnlyList<MarcaCota> Marcas { get; } = new[]
    {
        new MarcaCota("_OPEN90", "Abierta a 90°"),
        new MarcaCota("_OBLIQUE", "Oblicua (tick)"),
        new MarcaCota("_ARCHTICK", "Tick de arquitectura"),
        new MarcaCota("_OPEN", "Abierta"),
        new MarcaCota("_OPEN30", "Abierta a 30°"),
        new MarcaCota("_CLOSED", "Cerrada"),
        new MarcaCota("_CLOSEDBLANK", "Cerrada en blanco"),
        new MarcaCota("_DOT", "Punto"),
        new MarcaCota("_DOTSMALL", "Punto chico"),
        new MarcaCota("_DOTBLANK", "Punto en blanco"),
        new MarcaCota("_SMALL", "Punto pequeño en blanco"),
        new MarcaCota("_INTEGRAL", "Integral"),
        new MarcaCota("_NONE", "Sin marca"),
    };

    private readonly List<PerfilEstilo> _perfiles = new();

    public IReadOnlyList<PerfilEstilo> Perfiles => _perfiles;

    public PerfilEstilo Perfil(string clave) =>
        _perfiles.FirstOrDefault(p => p.Clave == clave)
        ?? throw new KeyNotFoundException($"No hay estilo de dibujo '{clave}'.");

    /// <summary>El color de la capa de una varilla -<c>#5</c>- en el perfil comun.</summary>
    public int ColorDeVarilla(string? clave)
    {
        var a = Perfil(Comun).Buscar("capa.VAR_" + (clave ?? string.Empty).Trim());
        return a is null ? -1 : Perfil(Comun).ColorAci(a.Clave);
    }

    /// <summary>
    /// Los valores de fabrica: <b>los que los dibujantes tenian escritos</b>. Cada numero de aqui
    /// es el de la constante que sustituye; si se cambia uno, cambia el dibujo de todos.
    /// </summary>
    public static EstiloDibujo PorDefecto()
    {
        var e = new EstiloDibujo();
        string N(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        string C(int v) => v.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------
        // Las capas que comparten secciones, alzados, zapatas y muros. Una capa es UNA en el
        // dibujo: VAR_#5 no puede ser azul en la seccion y verde en la zapata del mismo plano,
        // asi que su color va aqui, una sola vez.
        // ------------------------------------------------------------------
        var comun = new PerfilEstilo(Comun, "Capas compartidas",
            "Las capas que usan a la vez las secciones, los alzados, las zapatas y los muros: el acero por diametro, el concreto, los estribos y los textos.");

        foreach (var (capa, color) in CapasCad.TablaDeLaMacro)
        {
            var nombre = capa.StartsWith(CapasCad.PrefijoVarilla, StringComparison.Ordinal)
                ? $"Varilla {capa.Substring(CapasCad.PrefijoVarilla.Length)} (capa {capa})"
                : capa == "TEXTOS" ? "Textos (capa TEXTOS, y ROTULOS la sigue)" : $"Capa {capa}";
            comun.Con("capa." + capa, GrupoColores, nombre, TipoAjuste.ColorAci, C(color));
        }

        e._perfiles.Add(comun);

        // ------------------------------------------------------------------
        // Secciones y alzados: SeccionDrawer y AlzadoDrawer. Las medidas son las de la escala
        // base 1:100 de la macro y se multiplican por la escala del dibujo, como antes.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(Secciones, "Secciones y alzados",
                "Hoja «Secciones Concreto» y los alzados. Las medidas son a la escala base de la macro (0.01) y se ajustan solas a la escala del dibujo.")
            .Con("fuente", GrupoLetras, "Fuente del estilo de texto SECCIONES", TipoAjuste.Fuente, "BAHNSCHRIFT SEMILIGHT")
            .Con("alto.estilo", GrupoLetras, "Altura del estilo SECCIONES", TipoAjuste.Medida, N(0.025))
            .Con("alto.llamadas", GrupoLetras, "Altura de las llamadas del acero en la sección", TipoAjuste.Medida, N(0.021))
            .Con("alto.titulo.alzado", GrupoLetras, "Altura del título del alzado", TipoAjuste.Medida, N(0.03))
            .Con("alto.escala.alzado", GrupoLetras, "Altura de la escala del alzado", TipoAjuste.Medida, N(0.0225))
            .Con("alto.rotulos.alzado", GrupoLetras, "Altura de los rótulos del alzado", TipoAjuste.Medida, N(0.025))
            .Con("alto.letra.corte", GrupoLetras, "Altura de la letra del corte (A, A')", TipoAjuste.Medida, N(0.025))
            .Con("alto.rotulo.seccion", GrupoLetras, "Altura del rótulo bajo la sección (ID, f'c, escala)", TipoAjuste.Medida, N(0.03))
            .Con("alto.corte.alzado", GrupoLetras, "Altura del «CORTE A-A'» del alzado", TipoAjuste.Medida, N(0.025))
            .Con("cota.alto", GrupoCotas, "Altura del número de las cotas (COTA_ESTRUCTURAL)", TipoAjuste.Medida, N(0.017))
            .Con("cota.marca.tam", GrupoCotas, "Tamaño de la marca", TipoAjuste.Medida, N(0.02))
            .Con("cota.marca", GrupoCotas, "Tipo de marca", TipoAjuste.Marca, "_OPEN90")
            .Con("cota.color.texto", GrupoCotas, "Color del número", TipoAjuste.ColorAci, C(1))
            .Con("cota.color.lineas", GrupoCotas, "Color de las líneas de cota y de extensión", TipoAjuste.ColorAci, C(253))
            .Con("capa.COTAS", GrupoColores, "Capa COTAS", TipoAjuste.ColorAci, C(253)));

        // ------------------------------------------------------------------
        // Perfiles de acero: la hoja de acero, con su estilo ACERO.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(Acero, "Perfiles de acero",
                "Las secciones de perfiles de acero. Sus cotas usan COTA_ESTRUCTURAL, el de «Secciones y alzados».")
            .Con("fuente", GrupoLetras, "Fuente del estilo de texto ACERO", TipoAjuste.Fuente, "BAHNSCHRIFT SEMILIGHT")
            .Con("factor.rotulo", GrupoLetras, "Tamaño del rótulo del perfil (1 = el de siempre)", TipoAjuste.Medida, N(1),
                "La altura del rótulo sale del tamaño del perfil; este factor la multiplica: 1.5 = 50 % más grande.")
            .Con("factor.cotas", GrupoCotas, "Tamaño del número y de la marca de las cotas (1 = el de siempre)", TipoAjuste.Medida, N(1),
                "Salen del peralte del perfil; este factor los multiplica.")
            .Con("capa.PERFILES", GrupoColores, "Capa PERFILES", TipoAjuste.ColorAci, C(7)));

        // ------------------------------------------------------------------
        // Zapatas aisladas y corridas: ZapataDrawer.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(Zapatas, "Zapatas aisladas y corridas",
                "Hojas de zapatas aisladas y corridas: elevación y planta.")
            .Con("fuente", GrupoLetras, "Fuente de SECCIONES si el dibujo todavía no tiene ese estilo", TipoAjuste.Fuente, "Arial",
                "Si ya se dibujaron secciones, SECCIONES ya existe con la fuente de «Secciones y alzados» y se respeta.")
            .Con("alto.titulo", GrupoLetras, "Altura del título", TipoAjuste.Medida, N(0.07))
            .Con("alto.subtitulo", GrupoLetras, "Altura del subtítulo (ELEVACION, PLANTA)", TipoAjuste.Medida, N(0.05))
            .Con("alto.escala", GrupoLetras, "Altura de la escala", TipoAjuste.Medida, N(0.04))
            .Con("alto.rotulos", GrupoLetras, "Altura de los rótulos del acero", TipoAjuste.Medida, N(0.015))
            .Con("alto.terreno", GrupoLetras, "Altura del texto del terreno y del nivel del terreno", TipoAjuste.Medida, N(0.025))
            .Con("alto.plantilla", GrupoLetras, "Altura del texto de la plantilla", TipoAjuste.Medida, N(0.02))
            .Con("alto.planta.rotulos", GrupoLetras, "Altura de los rótulos de la planta", TipoAjuste.Medida, N(0.03))
            .Con("alto.planta.id", GrupoLetras, "Altura del ID del dado en la planta", TipoAjuste.Medida, N(0.03))
            .Con("cota.alto", GrupoCotas, "Altura del número de las cotas (COTA_ESTRUCTURAL)", TipoAjuste.Medida, N(0.025))
            .Con("cota.marca.tam", GrupoCotas, "Tamaño de la marca", TipoAjuste.Medida, N(0.025))
            .Con("cota.marca", GrupoCotas, "Tipo de marca", TipoAjuste.Marca, "_OPEN90")
            .Con("capa.COTAS", GrupoColores, "Capa COTAS (0 = no se toca)", TipoAjuste.ColorAci, C(0))
            .Con("capa.TERRENO_LINEA", GrupoColores, "Capa TERRENO_LINEA", TipoAjuste.ColorAci, C(140))
            .Con("capa.TERRENO_HATCH", GrupoColores, "Capa TERRENO_HATCH", TipoAjuste.ColorAci, C(8))
            .Con("capa.PLANTILLA", GrupoColores, "Capa PLANTILLA", TipoAjuste.ColorAci, C(8))
            .Con("capa.BLOQUE_DADO", GrupoColores, "Capa BLOQUE_DADO", TipoAjuste.ColorAci, C(7))
            .Con("capa.BLOQUE_ZAPATA", GrupoColores, "Capa BLOQUE_ZAPATA", TipoAjuste.ColorAci, C(7))
            .Con("capa.MURO DE ENRASE", GrupoColores, "Capa MURO DE ENRASE (corridas)", TipoAjuste.ColorAci, C(140)));

        // ------------------------------------------------------------------
        // Muros de contencion: TrazoMuroContencion y ZapataDrawer.Muro. COTA_MC es
        // COTA_ESTRUCTURAL de las zapatas con lo de aqui encima.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(Muros, "Muros de contención",
                "Hoja «Muros de Contención». Las capas son las de las zapatas y las compartidas.")
            .Con("alto.rotulos", GrupoLetras, "Altura de las llamadas del acero", TipoAjuste.Medida, N(0.09))
            .Con("alto.titulo", GrupoLetras, "Altura del título", TipoAjuste.Medida, N(0.15))
            .Con("alto.subtitulo", GrupoLetras, "Altura del subtítulo y las leyendas", TipoAjuste.Medida, N(0.10))
            .Con("cota.alto", GrupoCotas, "Altura del número de las cotas (COTA_MC)", TipoAjuste.Medida, N(0.08))
            .Con("cota.marca.tam", GrupoCotas, "Tamaño de la marca", TipoAjuste.Medida, N(0.025))
            .Con("cota.marca", GrupoCotas, "Tipo de marca", TipoAjuste.Marca, "_OPEN90"));

        // ------------------------------------------------------------------
        // Placa base y simbologia: PlacaBaseDrawer.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(PlacaBase, "Placa base y simbología",
                "Hojas de placa base y de simbología de soldadura.")
            .Con("fuente", GrupoLetras, "Fuente del estilo ACERO_PLACA", TipoAjuste.Fuente, "Bahnschrift Light SemiCondensed")
            .Con("alto.texto", GrupoLetras, "Altura de los textos", TipoAjuste.Medida, N(0.016))
            .Con("cota.alto", GrupoCotas, "Altura del número de las cotas (COTA_ACERO)", TipoAjuste.Medida, N(0.016))
            .Con("cota.marca.mm", GrupoCotas, "Tamaño de la marca, en mm de impresión", TipoAjuste.Medida, N(1.5))
            .Con("cota.marca", GrupoCotas, "Tipo de marca", TipoAjuste.Marca, "_OBLIQUE")
            .Con("cota.color.texto", GrupoCotas, "Color del número", TipoAjuste.ColorAci, C(1))
            .Con("capa.PLACA BASE", GrupoColores, "Capa PLACA BASE", TipoAjuste.ColorAci, C(140))
            .Con("capa.ANCLAS", GrupoColores, "Capa ANCLAS", TipoAjuste.ColorAci, C(1))
            .Con("capa.ROTULOS", GrupoColores, "Capa ROTULOS", TipoAjuste.ColorAci, C(3))
            .Con("capa.COTAS", GrupoColores, "Capa COTAS", TipoAjuste.ColorAci, C(7))
            .Con("capa.CONCRETO", GrupoColores, "Capa CONCRETO", TipoAjuste.ColorAci, C(8))
            .Con("capa.PERFILES", GrupoColores, "Capa PERFILES", TipoAjuste.ColorAci, C(7))
            .Con("capa.CARTABONES", GrupoColores, "Capa CARTABONES", TipoAjuste.ColorAci, C(140))
            .Con("capa.SOLDADURA", GrupoColores, "Capa SOLDADURA", TipoAjuste.ColorAci, C(240))
            .Con("capa.SOLDADURA CARTABON", GrupoColores, "Capa SOLDADURA CARTABON", TipoAjuste.ColorAci, C(210))
            .Con("capa.GROUT", GrupoColores, "Capa GROUT", TipoAjuste.ColorAci, C(30)));

        // ------------------------------------------------------------------
        // Planta estructural: PlantaDrawer. Las claves SON los parametros de su hoja CONFIG
        // -ConfigPlano-, y los defectos los de esa hoja: el dibujante los recibe tal cual.
        // ------------------------------------------------------------------
        e._perfiles.Add(new PerfilEstilo(PlantaEtabs, "Planta estructural (ETABS)",
                "La planta que se dibuja desde el modelo de ETABS. Cada ajuste es un parámetro de su hoja CONFIG.")
            .Con("SEC_NOMBRE_FUENTE", GrupoLetras, "Fuente de los rótulos de sección", TipoAjuste.Fuente, "Bahnschrift")
            .Con("SEC_ALTURA", GrupoLetras, "Altura de los rótulos de sección", TipoAjuste.Medida, "0.12")
            .Con("ALTURA_TEXTO", GrupoLetras, "Altura de las etiquetas", TipoAjuste.Medida, "0.12")
            .Con("ALTURA_TEXTO_SECCION", GrupoLetras, "Altura del texto de las secciones (0 = 0.8 de las etiquetas)", TipoAjuste.Medida, "0")
            .Con("ALTURA_TEXTO_BURBUJA", GrupoLetras, "Altura del número de los ejes (0 = automática)", TipoAjuste.Medida, "0")
            .Con("MURO_CONCRETO_LEYENDA_ALTURA", GrupoLetras, "Altura de la leyenda del muro de concreto", TipoAjuste.Medida, "0.12")
            .Con("CADENA_NOMBRE_FUENTE", GrupoLetras, "Fuente del rótulo de las cadenas", TipoAjuste.Fuente, "Bahnschrift")
            .Con("CADENA_TEXTO_ALTURA", GrupoLetras, "Altura del rótulo de las cadenas", TipoAjuste.Medida, "0.09")
            .Con("LOSA_NOMBRE_FUENTE", GrupoLetras, "Fuente del rótulo de las losas", TipoAjuste.Fuente, "Bahnschrift")
            .Con("LOSA_TEXTO_ALTURA", GrupoLetras, "Altura del rótulo de las losas", TipoAjuste.Medida, "0.072")
            .Con("LOSACERO_TEXTO_ALTURA", GrupoLetras, "Altura del rótulo de la losacero (0 = la de la losa)", TipoAjuste.Medida, "0")
            .Con("ROTULO_NOMBRE_FUENTE", GrupoLetras, "Fuente del rótulo de la planta", TipoAjuste.Fuente, "Haettenschweiler")
            .Con("ROTULO_ALTURA_TITULO", GrupoLetras, "Altura de PLANTA ESTRUCTURAL", TipoAjuste.Medida, "0.52")
            .Con("ROTULO_ALTURA_NIVEL", GrupoLetras, "Altura del renglón del nivel", TipoAjuste.Medida, "0.26")
            .Con("COTA_NOMBRE_FUENTE", GrupoCotas, "Fuente del número de las cotas", TipoAjuste.Fuente, "Century Gothic")
            .Con("COTA_TEXT_HEIGHT", GrupoCotas, "Altura del número de las cotas (COTA_DIM)", TipoAjuste.Medida, "0.1")
            .Con("ALTURA_ESTILO_COTA", GrupoCotas, "Altura del estilo de texto COTA", TipoAjuste.Medida, "0.1")
            .Con("COTA_ARROW_SIZE", GrupoCotas, "Tamaño de la marca", TipoAjuste.Medida, "0.05")
            .Con("COTA_FLECHA", GrupoCotas, "Tipo de marca", TipoAjuste.Marca, "_OBLIQUE")
            .Con("COTA_COLOR_TEXTO", GrupoCotas, "Color del número", TipoAjuste.ColorAci, "1")
            .Con("COLOR_COTAS", GrupoColores, "Capa E-COTAS", TipoAjuste.ColorAci, "8")
            .Con("COLOR_CASTILLO", GrupoColores, "Capa E-CASTILLO", TipoAjuste.ColorAci, "1")
            .Con("COLOR_DALA", GrupoColores, "Capa E-DALA", TipoAjuste.ColorAci, "12")
            .Con("COLOR_ACERO", GrupoColores, "Capa E-ACERO", TipoAjuste.ColorAci, "130")
            .Con("COLOR_MURO_CONCRETO", GrupoColores, "Capa del muro de concreto", TipoAjuste.ColorAci, "4")
            .Con("COLOR_MAMPOSTERIA", GrupoColores, "Capa E-MAMPOSTERIA", TipoAjuste.ColorAci, "30")
            .Con("COLOR_CADENA_DESPLANTE", GrupoColores, "Capa de la cadena de desplante", TipoAjuste.ColorAci, "1")
            .Con("COLOR_ARMADO_LOSA", GrupoColores, "Capa E-ARMADO LOSA", TipoAjuste.ColorAci, "142")
            .Con("COLOR_VOLADO", GrupoColores, "Capa E-VOLADO", TipoAjuste.ColorAci, "252")
            .Con("COLOR_LOSACERO", GrupoColores, "Capa E-LOSACERO", TipoAjuste.ColorAci, "6")
            .Con("COLOR_ESCALERA", GrupoColores, "Capa de la escalera", TipoAjuste.ColorAci, "8")
            .Con("COLOR_VACIO", GrupoColores, "Capa de los vacíos", TipoAjuste.ColorAci, "252")
            .Con("COLOR_PIERS", GrupoColores, "Capa PIERS", TipoAjuste.ColorAci, "7")
            .Con("COLOR_EJES", GrupoColores, "Líneas de eje", TipoAjuste.ColorAci, "8")
            .Con("COLOR_BURBUJA_EJES", GrupoColores, "Círculos de los ejes", TipoAjuste.ColorAci, "4")
            .Con("COLOR_EJES_TEXTO", GrupoColores, "Números de los ejes", TipoAjuste.ColorAci, "6")
            .Con("COLOR_TITULO", GrupoColores, "Capa E-TITULO (0 = negro real)", TipoAjuste.ColorAci, "7"));

        return e;
    }

    /// <summary>Una copia independiente, para que la ventana edite sin tocar el actual.</summary>
    public EstiloDibujo Copia()
    {
        var c = PorDefecto();
        c.Aplicar(ParaGuardar());
        return c;
    }

    /// <summary>Lo que hay que guardar: SOLO lo que difiere del defecto, perfil por perfil.</summary>
    public Dictionary<string, Dictionary<string, string>> ParaGuardar()
    {
        var r = new Dictionary<string, Dictionary<string, string>>();

        foreach (var p in _perfiles)
        {
            var cambios = p.Ajustes.Where(a => a.Cambiado).ToDictionary(a => a.Clave, a => a.Valor.Trim());

            if (cambios.Count > 0)
            {
                r[p.Clave] = cambios;
            }
        }

        return r;
    }

    /// <summary>
    /// Pone lo guardado. Lo que no se reconoce -un perfil o un ajuste que ya no existe- o no
    /// sirve -un color de 900- se ignora y queda el defecto: un archivo viejo o editado a mano
    /// no impide dibujar.
    /// </summary>
    /// <returns>Cuantos valores se pusieron.</returns>
    public int Aplicar(IReadOnlyDictionary<string, Dictionary<string, string>>? guardado)
    {
        var n = 0;

        if (guardado is null)
        {
            return 0;
        }

        foreach (var (clavePerfil, valores) in guardado)
        {
            var p = _perfiles.FirstOrDefault(x => x.Clave == clavePerfil);

            if (p is null || valores is null)
            {
                continue;
            }

            foreach (var (clave, valor) in valores)
            {
                var a = p.Buscar(clave);

                if (a is not null && a.Problema(valor).Length == 0)
                {
                    a.Valor = valor.Trim();
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>Lee un numero con punto o con coma.</summary>
    public static double? LeerNumero(string? s)
    {
        var t = (s ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v)
            ? v
            : null;
    }

    /// <summary>
    /// El RGB de un color ACI, para las muestras de la ventana y las vistas previas. Es la rueda
    /// de AutoCAD: del 10 al 249 el tono va de 15 en 15 grados y la unidad decide el brillo y si
    /// es palido; del 250 al 255, los grises.
    /// </summary>
    public static (byte R, byte G, byte B) Rgb(int aci)
    {
        switch (aci)
        {
            case 1: return (255, 0, 0);
            case 2: return (255, 255, 0);
            case 3: return (0, 255, 0);
            case 4: return (0, 255, 255);
            case 5: return (0, 0, 255);
            case 6: return (255, 0, 255);
            case 7: return (255, 255, 255);
            case 8: return (128, 128, 128);
            case 9: return (192, 192, 192);
            case 250: return (51, 51, 51);
            case 251: return (80, 80, 80);
            case 252: return (105, 105, 105);
            case 253: return (130, 130, 130);
            case 254: return (190, 190, 190);
            case 255: return (255, 255, 255);
        }

        if (aci < 10 || aci > 249)
        {
            // 0 (por bloque) y 256 (por capa): sin color propio.
            return (0, 0, 0);
        }

        var tono = ((aci / 10) - 1) * 15.0;
        var u = aci % 10;
        double[] brillo = { 1.0, 1.0, 0.65, 0.65, 0.5, 0.5, 0.3, 0.3, 0.15, 0.15 };
        var v = brillo[u];
        var s = u % 2 == 0 ? 1.0 : 0.5;

        var c = v * s;
        var x = c * (1 - Math.Abs((tono / 60.0 % 2) - 1));
        var m = v - c;

        var (r, g, b) = tono switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        byte B8(double q) => (byte)Math.Round(Math.Clamp((q + m) * 255, 0, 255));
        return (B8(r), B8(g), B8(b));
    }
}
