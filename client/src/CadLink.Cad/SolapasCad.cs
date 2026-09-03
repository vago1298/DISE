namespace CadLink.Cad;

/// <summary>
/// Los <b>datos de un plano</b> para su solapa, ya resueltos y listos para rotular.
/// </summary>
/// <remarks>
/// <para>
/// Port de la lectura de celdas de la macro <c>GenerarSolapas</c>. Ahí cada plano era un bloque de
/// 18 filas —C2, C20, C38…— y cada atributo salía de un desplazamiento sobre la fila del título.
/// Aquí no hay hoja de Excel: los datos ya están capturados en la pestaña <b>Proyecto</b>, así que
/// lo que se porta es la <b>traducción</b> de cada dato a su atributo, no la aritmética de celdas.
/// </para>
/// <para>
/// Todo llega <b>ya combinado</b>: la solapa del juego pone lo que es común —calculista, cédula,
/// propietario, ubicación, proyecto, quién dibujó, fecha, acotación— y el plano pone lo suyo —clave,
/// contenido, detalle, escala, número—.
/// </para>
/// </remarks>
public sealed class SolapaCad
{
    /// <summary>El nombre grande del plano. Celda <c>C2</c> de la macro.</summary>
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Tamaño de hoja tal como se eligió: <c>ARCH D</c>, <c>ISO A1</c>…</summary>
    public string Tamano { get; set; } = string.Empty;

    /// <summary>Ancho y alto de la hoja en <b>pulgadas</b>, como los da el dispositivo.</summary>
    public double AnchoPulg { get; set; }
    public double AltoPulg { get; set; }

    /// <summary>La hoja va acostada. Celda <c>C6</c> de la macro.</summary>
    public bool Horizontal { get; set; } = true;

    // ---------- Lo que es del juego ----------
    public string Calculista { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string Propietario { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string Proyecto { get; set; } = string.Empty;
    public string Dibujo { get; set; } = string.Empty;
    public string Fecha { get; set; } = string.Empty;
    public string Acotacion { get; set; } = string.Empty;

    // ---------- Lo que es del plano ----------
    public string Contenido { get; set; } = string.Empty;

    /// <summary>La segunda línea del contenido: «sección y detalles». Columna <c>I</c>.</summary>
    public string Detalle { get; set; } = string.Empty;

    public string Escala { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;

    /// <summary>Número de este plano y cuántos son. Celdas <c>D18</c> y <c>E18</c>.</summary>
    public int Numero { get; set; }
    public int Total { get; set; }

    /// <summary>
    /// Lo que <b>falta capturar</b> para poder generar esta solapa.
    /// </summary>
    /// <remarks>
    /// Es el mismo criterio de la macro: sin nombre en la celda C no se hace la solapa, y sin
    /// medidas se salta el plano avisando. Aquí se dice <b>qué</b> falta, que es lo que permite
    /// corregirlo sin adivinar.
    /// </remarks>
    public List<string> Falta
    {
        get
        {
            var falta = new List<string>();

            if (Titulo.Trim().Length == 0 && Clave.Trim().Length == 0)
            {
                falta.Add("la clave o el titulo del plano");
            }

            if (AnchoPulg <= 0 || AltoPulg <= 0)
            {
                falta.Add("el tamano de la hoja");
            }

            return falta;
        }
    }
}

/// <summary>
/// La lógica de las <b>solapas</b>: qué texto va en cada atributo y qué papel le toca a cada hoja.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>ValorAtributo</c>, <c>SinTitulo</c>, <c>ConCeros</c>, <c>Formatear</c>,
/// <c>SinAcentos</c>, <c>Normaliza</c>, <c>Limpiar</c>, <c>NombreLibre</c>,
/// <c>MedidasDelNombre</c>, <c>ExtraerNumeros</c> y <c>BuscarMedio</c> de la macro
/// <c>GenerarSolapas</c>.
/// </para>
/// <para>
/// Vive <b>aparte del dibujante y sin nada de COM</b>, igual que <see cref="AnclasPlacaBase"/> y
/// <see cref="ElevacionPlacaBase"/>. Aquí el motivo pesa más que en ningún otro sitio: la búsqueda
/// del papel tiene <b>tres estrategias, un desempate por puntos y un último recurso</b>, y cuando se
/// equivoca no falla —AutoCAD deja el papel por omisión, que es Carta vertical, y el plano entero
/// sale descuadrado sin un solo mensaje de error—. Es la causa número uno de que la orientación
/// salga mal, y lo dice la propia macro.
/// </para>
/// </remarks>
public static class Solapas
{
    /// <summary>Títulos profesionales que se quitan del nombre del calculista.</summary>
    /// <remarks>
    /// El cajetín ya trae dibujado el «ING.», así que dejarlo también en el dato imprime
    /// «ING. ING. MIGUEL». Los que llevan punto van <b>primero</b> para que <c>ING.</c> se pruebe
    /// antes que <c>ING</c>: al revés, «ING. MIGUEL» perdería solo las tres letras y quedaría
    /// «. MIGUEL».
    /// </remarks>
    public static readonly string[] TitulosQueSeQuitan =
    {
        "ING.", "ING", "ARQ.", "ARQ", "M.I.", "DR.", "LIC.", "C.",
    };

    /// <summary>Lo que va antes del número de cédula.</summary>
    public const string PrefijoCedula = "CED. PROF. ";

    /// <summary>Dígitos del número de plano: <c>2</c> da <c>01/04</c>.</summary>
    public const int DigitosDelNumero = 2;

    /// <summary>El separador de <c>01/04</c>.</summary>
    public const string SeparadorDelNumero = "/";

    /// <summary>Tolerancia al comparar las medidas del nombre del papel, en mm.</summary>
    public const double ToleranciaMedioMm = 2.0;

    /// <summary>Tolerancia para decir que el papel del layout es el que se pidió, en mm.</summary>
    public const double ToleranciaPapelMm = 6.0;

    /// <summary>
    /// Los atributos que la macro sabe llenar, en el orden en que se documentan.
    /// </summary>
    /// <remarks>
    /// Sirve para <b>reconocer cuál de los bloques del dibujo es el cajetín</b>: el que más
    /// atributos de esta lista tenga. Si algún día se le agrega uno al bloque, va aquí también, o el
    /// reconocimiento lo cuenta de menos.
    /// </remarks>
    public static readonly string[] TagsConocidos =
    {
        "CALCULISTA", "CEDULA", "PROPIETARIO", "UBICACION", "PROYECTO", "CONTENIDO", "DETALLE",
        "DIBUJO", "FECHA", "ESCALA", "ACOTACION", "CLAVE", "NUMERO", "TOTAL", "TITULO", "TAMANO",
    };

    // ======================================================================
    //  QUÉ TEXTO VA EN CADA ATRIBUTO
    // ======================================================================

    /// <summary>
    /// El texto que le toca a un atributo del cajetín, o <c>null</c> si <b>no se toca</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>null</c> y no cadena vacía.</b> Son dos cosas distintas: vacío significa «este dato
    /// está en blanco, bórralo del cajetín», y <c>null</c> significa «este atributo no lo maneja el
    /// programa, déjalo como está». La macro lo distinguía con <c>vbNullChar</c>; devolver vacío en
    /// los dos casos borraría los atributos que el dibujante haya puesto a mano.
    /// </para>
    /// <para>
    /// El texto sale <b>sin formatear</b>: las mayúsculas y los acentos los pone
    /// <see cref="Formatear"/> al escribir, que es el único sitio donde se decide.
    /// </para>
    /// </remarks>
    public static string? TextoDeAtributo(SolapaCad s, string? tag)
    {
        switch ((tag ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "CALCULISTA": return SinTitulo(s.Calculista);

            case "CEDULA":
                // EL PREFIJO SOLO SI HAY NÚMERO. Con la celda vacía, ponerlo igual deja un
                // «CED. PROF.» solo en el cajetín, que se lee como un dato que se perdió.
                return s.Cedula.Trim().Length == 0 ? string.Empty : PrefijoCedula + s.Cedula.Trim();

            case "PROPIETARIO": return s.Propietario;
            case "UBICACION": return s.Ubicacion;
            case "PROYECTO": return s.Proyecto;
            case "CONTENIDO": return s.Contenido;
            case "DETALLE": return s.Detalle;
            case "DIBUJO": return s.Dibujo;
            case "FECHA": return s.Fecha;
            case "ESCALA": return s.Escala;
            case "ACOTACION": return s.Acotacion;
            case "CLAVE": return s.Clave;

            case "NUMERO": return TextoDelNumero(s);

            case "TOTAL": return ConCeros(s.Total, DigitosDelNumero);
            case "TITULO": return s.Titulo;
            case "TAMANO": return s.Tamano;

            default: return null;
        }
    }

    /// <summary>El número del plano: <c>01/04</c>, o <c>01</c> si no se sabe el total.</summary>
    /// <remarks>
    /// El cajetín puede traer el <c>/</c> dibujado y dos recuadros. En ese caso se usa
    /// <c>NUMERO</c> para el primero y <c>TOTAL</c> para el segundo, y este texto sobra: el atributo
    /// <c>TOTAL</c> se llena igual, así que las dos formas del cajetín funcionan sin configurar nada.
    /// </remarks>
    public static string TextoDelNumero(SolapaCad s)
    {
        var n = ConCeros(s.Numero, DigitosDelNumero);

        return s.Total > 0 ? n + SeparadorDelNumero + ConCeros(s.Total, DigitosDelNumero) : n;
    }

    /// <summary>¿Es uno de los atributos que el programa sabe llenar?</summary>
    public static bool EsTagConocido(string? tag)
    {
        var t = (tag ?? string.Empty).Trim();

        foreach (var c in TagsConocidos)
        {
            if (string.Equals(c, t, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ======================================================================
    //  FORMATO DEL TEXTO
    // ======================================================================

    /// <summary>
    /// El formato final de lo que se escribe en AutoCAD: <b>mayúsculas</b>.
    /// </summary>
    /// <param name="quitarAcentos">
    /// Para las fuentes <c>.shx</c> que no dibujan las mayúsculas acentuadas y sacan un cuadrito
    /// donde va la Á. En una TrueType no hace falta.
    /// </param>
    /// <remarks>
    /// <b>Punto único.</b> La regla se aplica al escribir y no al capturar, así que en la aplicación
    /// se sigue escribiendo normal y el plano sale siempre igual. Y con
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/>: en un Windows turco
    /// <c>ToUpper</c> convierte la <c>i</c> en <c>İ</c> y el plano sale con una letra que no existe
    /// en español.
    /// </remarks>
    public static string Formatear(string? s, bool quitarAcentos = false)
    {
        var t = (s ?? string.Empty).Trim().ToUpperInvariant();

        return quitarAcentos ? SinAcentos(t) : t;
    }

    /// <summary>Cambia las vocales acentuadas y la eñe por su letra simple.</summary>
    public static string SinAcentos(string? s)
    {
        const string con = "áéíóúñüÁÉÍÓÚÑÜ";
        const string sin = "aeiounuAEIOUNU";

        var t = new System.Text.StringBuilder((s ?? string.Empty).Length);

        foreach (var c in s ?? string.Empty)
        {
            var i = con.IndexOf(c);

            t.Append(i >= 0 ? sin[i] : c);
        }

        return t.ToString();
    }

    /// <summary>
    /// Quita el título profesional del inicio: <c>Ing. Miguel</c> queda <c>Miguel</c>.
    /// </summary>
    /// <remarks>
    /// Se exige el <b>espacio</b> detrás del título para no morderle la primera palabra a un nombre
    /// que empiece igual: sin él, «Inga Torres» se quedaría en «a Torres».
    /// </remarks>
    public static string SinTitulo(string? s)
    {
        var t = (s ?? string.Empty).Trim();

        foreach (var tok in TitulosQueSeQuitan)
        {
            if (t.Length > tok.Length
                && t.StartsWith(tok, StringComparison.OrdinalIgnoreCase)
                && t[tok.Length] == ' ')
            {
                return t.Substring(tok.Length + 1).Trim();
            }
        }

        return t;
    }

    /// <summary>Ceros a la izquierda: <c>1</c> con dos dígitos es <c>01</c>.</summary>
    public static string ConCeros(int v, int digitos)
    {
        if (v <= 0)
        {
            return string.Empty;
        }

        var s = v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return digitos <= 0 ? s : s.PadLeft(digitos, '0');
    }

    /// <summary>
    /// Minúsculas, sin espacios, guiones ni acentos: para <b>comparar</b> sin fallar.
    /// </summary>
    /// <remarks>
    /// Es lo que permite que <c>"ARCH D Horizontal"</c>, <c>"ARCH-D-H"</c> y
    /// <c>"archdhorizontal"</c> se reconozcan como lo mismo. Incluye el espacio duro
    /// —<c>U+00A0</c>—, que es el que se cuela al pegar desde una lista y no se ve.
    /// </remarks>
    public static string Normaliza(string? s)
    {
        var t = SinAcentos((s ?? string.Empty).Trim().ToLowerInvariant());
        var b = new System.Text.StringBuilder(t.Length);

        foreach (var c in t)
        {
            if (c is ' ' or '\u00A0' or '_' or '-')
            {
                continue;
            }

            b.Append(c);
        }

        return b.ToString();
    }

    // ======================================================================
    //  EL NOMBRE DEL LAYOUT
    // ======================================================================

    /// <summary>
    /// El nombre del layout: la <b>clave</b> del plano, o su título si no tiene clave.
    /// </summary>
    /// <remarks>
    /// La clave primero porque es corta y única —<c>E-01</c>— y en la pestaña del layout se lee de
    /// un vistazo. El título completo cabe, pero deja una fila de pestañas que no se puede recorrer.
    /// </remarks>
    public static string NombreDeLayout(SolapaCad s)
    {
        var clave = s.Clave.Trim();

        return Limpiar(Formatear(clave.Length > 0 ? clave : s.Titulo));
    }

    /// <summary>Quita los caracteres que AutoCAD no acepta en un nombre de layout.</summary>
    /// <remarks>
    /// Se sustituyen por un guion en lugar de borrarse: <c>E-01/02</c> queda <c>E-01-02</c> y no
    /// <c>E-0102</c>, que se lee como otra clave.
    /// </remarks>
    public static string Limpiar(string? s)
    {
        const string malos = "<>/\\\":;?*|,=`";

        var b = new System.Text.StringBuilder();

        foreach (var c in s ?? string.Empty)
        {
            b.Append(malos.IndexOf(c) >= 0 ? '-' : c);
        }

        var t = b.ToString().Trim();

        if (t.Length > 60)
        {
            t = t.Substring(0, 60);
        }

        return t.Length == 0 ? "PLANO" : t;
    }

    /// <summary>
    /// Un nombre de layout que no choque con los que ya hay.
    /// </summary>
    /// <param name="usados">Los nombres que ya existen en el dibujo.</param>
    /// <param name="sobrescribir">
    /// <c>true</c> devuelve el nombre tal cual —quien llama borra el layout que había—.
    /// <c>false</c> le pone un consecutivo.
    /// </param>
    /// <remarks>
    /// La comparación es <b>sin distinguir mayúsculas</b>, que es lo que hace AutoCAD: pedirle un
    /// layout «e-01» cuando ya existe «E-01» no crea uno nuevo, falla.
    /// </remarks>
    public static string NombreLibre(
        string nombre, IEnumerable<string> usados, bool sobrescribir)
    {
        if (sobrescribir)
        {
            return nombre;
        }

        var hay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var u in usados)
        {
            hay.Add(u);
        }

        if (!hay.Contains(nombre))
        {
            return nombre;
        }

        for (var k = 1; k < 1000; k++)
        {
            var prueba = nombre + "-" + k.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!hay.Contains(prueba))
            {
                return prueba;
            }
        }

        return nombre;
    }

    // ======================================================================
    //  LA HOJA
    // ======================================================================

    /// <summary>El ancho y el alto de la hoja en mm, <b>ya orientados</b>.</summary>
    public static (double Ancho, double Alto) HojaOrientada(SolapaCad s)
    {
        var a = s.AnchoPulg * 25.4;
        var b = s.AltoPulg * 25.4;

        return s.Horizontal
            ? (Math.Max(a, b), Math.Min(a, b))
            : (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>El nombre que le tocaría a la configuración de página: <c>ARCH D Horizontal</c>.</summary>
    public static string NombreDeConfigPagina(SolapaCad s) =>
        s.Tamano.Trim() + (s.Horizontal ? " Horizontal" : " Vertical");

    /// <summary>
    /// ¿La configuración de página que se encontró es la de este plano?
    /// </summary>
    /// <remarks>
    /// Acepta las dos formas que la macro documenta —<c>ARCH D Horizontal</c> y <c>ARCH-D-H</c>—
    /// comparando normalizado. Sin esto, una plantilla con las configuraciones nombradas «ARCH-D-H»
    /// no encontraría ninguna y todos los planos caerían a la búsqueda de papel.
    /// </remarks>
    public static bool ConfigPaginaSirve(SolapaCad s, string? nombreConfig)
    {
        var n = Normaliza(nombreConfig);

        if (n.Length == 0 || s.Tamano.Trim().Length == 0)
        {
            return false;
        }

        var largo = Normaliza(s.Tamano + (s.Horizontal ? "Horizontal" : "Vertical"));
        var corto = Normaliza(s.Tamano + (s.Horizontal ? "H" : "V"));

        return n == largo || n == corto;
    }

    /// <summary>¿El papel que tiene el layout mide lo que se pidió?</summary>
    /// <remarks>
    /// Se comparan el <b>lado mayor con el mayor</b> y el menor con el menor, así que da igual la
    /// rotación del ploteo: lo que se comprueba es que sea el mismo pliego, no cómo está puesto.
    /// </remarks>
    public static bool PapelCoincide(double pw, double ph, double w, double h) =>
        Math.Abs(Math.Max(pw, ph) - Math.Max(w, h)) <= ToleranciaPapelMm
        && Math.Abs(Math.Min(pw, ph) - Math.Min(w, h)) <= ToleranciaPapelMm;

    // ======================================================================
    //  EL PAPEL: TRES ESTRATEGIAS Y UN ÚLTIMO RECURSO
    // ======================================================================
    //
    //  ═════════════════════════════════════════════════════════════════════════════════════
    //  SI EL PAPEL NO SE ENCUENTRA, EL PLANO SALE DESCUADRADO Y NADIE AVISA.
    //
    //  Cuando AutoCAD no reconoce el tamaño pedido no da error: deja el papel por omisión
    //  del dispositivo —Carta vertical— y el marco, el cajetín y las cotas se dibujan sobre
    //  una hoja que no es. La propia macro lo dice: es la causa número uno de que la
    //  orientación salga mal.
    //
    //  De ahí las tres estrategias, por prioridad:
    //    1. la medida en el ORDEN pedido      -> no hay que rotar el ploteo
    //    2. la misma medida al revés          -> hay que rotarlo 90°
    //    3. por NOMBRE del tamaño             -> para los personalizados sin medidas
    //  y si ninguna acierta, el pliego más chico donde el plano QUEPA, que descuadra el
    //  margen pero no la orientación.
    //  ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>El papel elegido, y si hay que rotar el ploteo.</summary>
    /// <param name="Rotacion">
    /// <c>0</c> sin rotar, <c>1</c> noventa grados, y <c>-1</c> <b>no tocar la rotación</b>: es la
    /// del acierto por nombre, donde no se sabe cómo está puesto el pliego y forzarlo lo empeoraría.
    /// </param>
    /// <param name="Cabe">
    /// <c>false</c> cuando salió del último recurso: el pliego es más grande que lo pedido, así que
    /// el plano cabe pero no llena la hoja. Quien llama tiene que <b>avisarlo</b>.
    /// </param>
    public readonly record struct PapelElegido(string Nombre, int Rotacion, bool Cabe);

    /// <summary>
    /// Busca en los nombres canónicos del dispositivo el pliego que le toca a este plano.
    /// </summary>
    /// <param name="medios">Los nombres canónicos, tal como los da <c>GetCanonicalMediaNames</c>.</param>
    /// <param name="preferirExpand">
    /// Prefiere los <c>expand</c>, que tienen menos margen no imprimible. Es lo que quiere un plano
    /// que llega hasta el borde.
    /// </param>
    /// <param name="usarFullBleed">
    /// Los <c>full bleed</c> imprimen sin margen. Se <b>penalizan</b> por omisión: casi ningún
    /// plóter puede de verdad, y el resultado es un plano recortado.
    /// </param>
    /// <param name="usarMasGrande">
    /// Si el tamaño exacto no existe, usar el más chico donde quepa. Sin esto el papel se queda en
    /// Carta y todo se descuadra.
    /// </param>
    public static PapelElegido? BuscarPapel(
        IEnumerable<string> medios, SolapaCad s,
        bool preferirExpand = true, bool usarFullBleed = false, bool usarMasGrande = true)
    {
        var (aMm, bMm) = HojaOrientada(s);

        var pedido = Normaliza(s.Tamano);
        var familia = Normaliza(PrimeraPalabra(s.Tamano));

        string? mejor = null;
        var mejorPunto = -1;
        var mejorRot = 0;

        // ---- último recurso, en la misma pasada ----
        string? cabe = null;
        var cabeArea = 0.0;
        var cabeRot = 0;

        foreach (var nm in medios)
        {
            if (string.IsNullOrWhiteSpace(nm))
            {
                continue;
            }

            var nmN = Normaliza(nm);
            var esFull = nmN.Contains("fullbleed");

            if (esFull && !usarFullBleed)
            {
                // Ni como acierto ni como respaldo: un full bleed que el plóter no puede honrar
                // recorta el plano por los cuatro lados.
                continue;
            }

            var punto = -1;
            var rot = 0;
            var tieneMedidas = MedidasDelNombre(nm, out var mw, out var mh);

            if (tieneMedidas)
            {
                if (Cerca(mw, aMm) && Cerca(mh, bMm))
                {
                    punto = 300;
                    rot = 0;
                }
                else if (Cerca(mw, bMm) && Cerca(mh, aMm))
                {
                    punto = 200;
                    rot = 1;
                }
            }

            // ---- 3: por nombre, para los personalizados sin medidas ----
            if (punto < 0 && pedido.Length > 0)
            {
                if (nmN == pedido)
                {
                    punto = 100;
                    rot = -1;
                }
                else if (nmN.StartsWith(pedido, StringComparison.Ordinal)
                         && nmN.Length > pedido.Length && nmN[pedido.Length] == '(')
                {
                    // EL PARÉNTESIS ES OBLIGATORIO. Sin él, «archd» pescaría «archd1», que es otro
                    // pliego: 24x36 contra 26x38. El plano sale en la hoja de al lado.
                    punto = 100;
                    rot = -1;
                }
            }

            if (punto > 0)
            {
                // Misma familia que pide el usuario: ARCH con ARCH, no con ANSI.
                if (familia.Length > 0 && nmN.StartsWith(familia, StringComparison.Ordinal))
                {
                    punto += 5;
                }

                if (esFull)
                {
                    punto += 20;
                }
                else if (nmN.Contains("expand"))
                {
                    punto += preferirExpand ? 10 : -10;
                }

                if (punto > mejorPunto)
                {
                    mejorPunto = punto;
                    mejor = nm;
                    mejorRot = rot;
                }

                continue;
            }

            // ---- el respaldo: el más chico donde QUEPA ----
            if (!usarMasGrande || !tieneMedidas)
            {
                continue;
            }

            var rotCabe = -2;

            if (mw >= aMm - ToleranciaMedioMm && mh >= bMm - ToleranciaMedioMm)
            {
                rotCabe = 0;
            }
            else if (mh >= aMm - ToleranciaMedioMm && mw >= bMm - ToleranciaMedioMm)
            {
                rotCabe = 1;
            }

            if (rotCabe < 0)
            {
                continue;
            }

            var area = mw * mh;

            // Un empujón de nada para que, a igualdad de área, gane la misma familia.
            if (familia.Length > 0 && nmN.StartsWith(familia, StringComparison.Ordinal))
            {
                area *= 0.999;
            }

            if (cabe is null || area < cabeArea)
            {
                cabe = nm;
                cabeArea = area;
                cabeRot = rotCabe;
            }
        }

        if (mejor is not null)
        {
            return new PapelElegido(mejor, mejorRot, Cabe: true);
        }

        return cabe is null ? null : new PapelElegido(cabe, cabeRot, Cabe: false);
    }

    /// <summary>
    /// Las medidas que trae el nombre canónico del papel, <b>en mm</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ARCH_D_(36.00_x_24.00_Inches)</c> da 914.4 × 609.6, y
    /// <c>ISO_A1_(841.00_x_594.00_MM)</c> da 841 × 594.
    /// </para>
    /// <para>
    /// Se leen los números de dentro del <b>último</b> paréntesis, y se toman los <b>dos últimos</b>.
    /// Los dos detalles importan: el nombre del tamaño trae dígitos —<c>D1</c>, <c>E2</c>,
    /// <c>A4</c>— y confundirlos con la medida da un pliego imaginario.
    /// </para>
    /// </remarks>
    public static bool MedidasDelNombre(string? nombre, out double wMm, out double hMm)
    {
        wMm = 0;
        hMm = 0;

        var s = nombre ?? string.Empty;

        var p1 = s.LastIndexOf('(');
        var p2 = s.LastIndexOf(')');

        if (p1 >= 0 && p2 > p1)
        {
            s = s.Substring(p1 + 1, p2 - p1 - 1);
        }

        var esMm = s.IndexOf("MM", StringComparison.OrdinalIgnoreCase) >= 0;

        var nums = ExtraerNumeros(s);

        if (nums.Count < 2)
        {
            return false;
        }

        wMm = nums[nums.Count - 2];
        hMm = nums[nums.Count - 1];

        if (!esMm)
        {
            wMm *= 25.4;
            hMm *= 25.4;
        }

        return wMm > 0 && hMm > 0;
    }

    /// <summary>Todos los números de una cadena.</summary>
    /// <remarks>
    /// Se parsea a mano y con <see cref="System.Globalization.CultureInfo.InvariantCulture"/>: el
    /// nombre canónico siempre trae <b>punto</b> decimal, y en un Windows con coma
    /// <c>double.Parse</c> de la cultura local leería <c>36.00</c> como 3600.
    /// </remarks>
    public static List<double> ExtraerNumeros(string? s)
    {
        var salida = new List<double>();
        var tok = new System.Text.StringBuilder();

        void Cerrar()
        {
            if (tok.Length == 0)
            {
                return;
            }

            if (double.TryParse(
                    tok.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v))
            {
                salida.Add(v);
            }

            tok.Clear();
        }

        foreach (var c in s ?? string.Empty)
        {
            if (char.IsDigit(c) || c == '.')
            {
                tok.Append(c);
            }
            else
            {
                Cerrar();
            }
        }

        Cerrar();

        return salida;
    }

    /// <summary>La primera palabra: <c>ARCH expand D1</c> da <c>ARCH</c>.</summary>
    public static string PrimeraPalabra(string? s)
    {
        var t = (s ?? string.Empty).Trim();
        var p = t.IndexOf(' ');

        return p > 0 ? t.Substring(0, p) : t;
    }

    /// <summary>Dos medidas son la misma dentro de <see cref="ToleranciaMedioMm"/>.</summary>
    public static bool Cerca(double x, double y) => Math.Abs(x - y) <= ToleranciaMedioMm;

    /// <summary>
    /// «ARCH_expand_D1_(26.00_x_38.00_Inches)» queda «ARCH expand D1».
    /// </summary>
    public static string NombreCortoDelPapel(string? canonico)
    {
        var s = canonico ?? string.Empty;
        var p = s.IndexOf('(');

        if (p > 0)
        {
            s = s.Substring(0, p);
        }

        return s.Replace('_', ' ').Trim();
    }

    // ======================================================================
    //  ENCAJAR EL CAJETÍN EN LA HOJA
    // ======================================================================

    /// <summary>
    /// La escala con la que el bloque medido cabe en el área, <b>sin deformarse</b>.
    /// </summary>
    /// <param name="soloReducir">Nunca agrandar: solo reducir si no cabe.</param>
    /// <remarks>
    /// Una sola escala para los dos ejes, y por eso: escalando X y Y por separado el marco encaja
    /// perfecto y los <b>textos del cajetín salen estirados</b>. Un rótulo deformado se ve mal en la
    /// pantalla y se ve peor impreso, y no hay forma de arreglarlo sin volver a generar el plano.
    /// </remarks>
    public static double EscalaParaCaber(
        double anchoBloque, double altoBloque, double anchoArea, double altoArea,
        double margen = 0, bool soloReducir = false)
    {
        if (anchoBloque <= 0 || altoBloque <= 0)
        {
            return 1;
        }

        var s = Math.Min(
            (anchoArea - (2 * margen)) / anchoBloque,
            (altoArea - (2 * margen)) / altoBloque);

        if (s <= 0)
        {
            return 1;
        }

        return soloReducir && s > 1 ? 1 : s;
    }
}
