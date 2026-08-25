namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// La hoja <b>CONFIG</b> de la macro de planos estructurales, tal cual, dentro de la
/// aplicación.
/// </summary>
/// <remarks>
/// <para>
/// La macro guarda sus ~260 parámetros en una hoja de Excel con tres columnas
/// —parámetro, valor, descripción— que ella misma crea (<c>CrearHojaConfig</c>) y va
/// actualizando por número de versión (<c>MigrarConfig</c> y los parches sellados con
/// <c>VERSION_PARCHE</c>). Aquí <b>no hay hoja de Excel</b>: el valor de omisión de cada
/// parámetro está en la tabla de esta clase —copiada renglón por renglón de
/// <c>CrearHojaConfig</c>, con su descripción— y lo que el usuario cambie se guarda en un
/// archivo JSON al lado del proyecto.
/// </para>
/// <para>
/// <b>Los nombres son los de la macro, letra por letra.</b> No se traducen ni se
/// «mejoran»: quien ya conoce su hoja CONFIG tiene que reconocer cada renglón, y el
/// verificador <c>tools/verificar_config_plano.py</c> compara esta tabla contra los
/// valores de la macro. Por eso tampoco se corrigen los espacios de más de
/// <c>LOSA_TEXTO_2</c> ni el doble espacio de <c>PLANTA  ESTRUCTURAL</c>: son parte del
/// dato.
/// </para>
/// <para>
/// <b>Lectura tipada, con las mismas reglas.</b> <see cref="Texto"/> = <c>CfgS</c>
/// (recorta espacios y cae al valor de omisión si está en blanco), <see cref="TextoTalCual"/>
/// = <c>CfgT</c> (<b>no</b> recorta: es la que leen los renglones del rótulo de la losa),
/// <see cref="Numero"/> = <c>CfgD</c> (acepta la coma como separador decimal) y
/// <see cref="Bandera"/> = <c>CfgB</c> (SI/NO, y también TRUE, VERDADERO, 1, X, YES).
/// </para>
/// <para>
/// <b>Las migraciones se conservan.</b> <see cref="VersionConfig"/> y
/// <see cref="VersionParche"/> son los mismos números —29 y 50—, y la idea de los parches
/// idempotentes sellados por versión se mantiene: un parche pisa el valor una sola vez y
/// después el usuario manda. Eso está en <see cref="Aplicar"/>.
/// </para>
/// </remarks>
public sealed class ConfigPlano
{
    /// <summary>Versión de la hoja CONFIG. Es el <c>VER_CFG</c> de la macro.</summary>
    public const double VersionConfig = 29;

    /// <summary>Último parche aplicado. Es el <c>VERSION_PARCHE</c> de la macro.</summary>
    public const double VersionParche = 50;

    /// <summary>Un renglón de la hoja: parámetro, valor y descripción.</summary>
    /// <param name="Parametro">El nombre, el de la macro.</param>
    /// <param name="Valor">El valor de omisión, como texto, igual que en la celda.</param>
    /// <param name="Descripcion">La tercera columna de la hoja.</param>
    public sealed record Renglon(string Parametro, string Valor, string Descripcion);

    /// <summary>
    /// Los valores que el usuario cambió. Lo que no esté aquí sale de
    /// <see cref="PorOmision"/>.
    /// </summary>
    private readonly Dictionary<string, string> _puestos =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Índice de la tabla de omisión, para no recorrerla en cada consulta.</summary>
    /// <remarks>
    /// Va <b>perezoso</b> y no como campo estático a secas: los campos estáticos se
    /// inicializan en el orden en que están escritos, y la tabla está al final del archivo
    /// —donde se lee— así que un índice escrito aquí arriba se armaría cuando
    /// <see cref="PorOmision"/> todavía es nulo.
    /// </remarks>
    private static Dictionary<string, Renglon> Indice => _indice ??=
        PorOmision.ToDictionary(r => r.Parametro, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, Renglon>? _indice;

    // =================================================================================
    //  LECTURA TIPADA
    // =================================================================================

    /// <summary>El valor crudo, o <c>null</c> si el parámetro no existe.</summary>
    public string? Crudo(string parametro)
    {
        if (_puestos.TryGetValue(parametro, out var puesto))
        {
            return puesto;
        }

        return Indice.TryGetValue(parametro, out var r) ? r.Valor : null;
    }

    /// <summary>
    /// Texto <b>recortado</b>. Es el <c>CfgS</c> de la macro: si la celda está en blanco
    /// se usa el valor de omisión.
    /// </summary>
    public string Texto(string parametro, string omision = "")
    {
        var s = Crudo(parametro);
        return string.IsNullOrWhiteSpace(s) ? omision : s.Trim();
    }

    /// <summary>
    /// Texto <b>sin recortar</b>. Es el <c>CfgT</c> de la macro, y existe por los
    /// renglones del rótulo de la losa: <c>LOSA_TEXTO_2</c> vale
    /// <c>"       cm de espesor"</c> y esos espacios son los que dejan el hueco donde va
    /// el número; recortarlos cambia el dibujo.
    /// </summary>
    public string TextoTalCual(string parametro, string omision = "")
    {
        var s = Crudo(parametro);
        return string.IsNullOrWhiteSpace(s) ? omision : s;
    }

    /// <summary>
    /// Número. Es el <c>CfgD</c> de la macro: cambia la coma por punto y lee lo que se
    /// pueda del principio del texto, como el <c>Val</c> de VBA.
    /// </summary>
    public double Numero(string parametro, double omision = 0)
    {
        var s = Crudo(parametro);
        return string.IsNullOrWhiteSpace(s) ? omision : ValDeVba(s);
    }

    /// <summary>
    /// Sí o no. Es el <c>CfgB</c> de la macro, con la misma lista de palabras: cualquier
    /// otra cosa deja el valor de omisión, que es lo que evita que una celda mal escrita
    /// apague una opción sin avisar.
    /// </summary>
    public bool Bandera(string parametro, bool omision = false)
    {
        var s = (Crudo(parametro) ?? string.Empty).Trim().ToUpperInvariant();

        return s switch
        {
            "SI" or "SÍ" or "TRUE" or "VERDADERO" or "1" or "X" or "YES" => true,
            "NO" or "FALSE" or "FALSO" or "0" => false,
            _ => omision
        };
    }

    /// <summary>
    /// El <c>Val</c> de VBA: lee el número del <b>principio</b> del texto y para en
    /// cuanto encuentra algo que no encaja. <c>"0.5 m"</c> son 0.5 y <c>"m 0.5"</c> es 0.
    /// </summary>
    /// <remarks>
    /// Se escribe a mano y no con <c>double.TryParse</c> porque VBA no falla nunca aquí:
    /// devuelve 0. Un <c>TryParse</c> sobre <c>"0.5 m"</c> devuelve falso, y entonces el
    /// parámetro se iría al valor de omisión en lugar de al 0.5 que el usuario escribió.
    /// </remarks>
    internal static double ValDeVba(string texto)
    {
        var t = texto.Trim().Replace(',', '.');

        var i = 0;
        if (i < t.Length && (t[i] == '+' || t[i] == '-'))
        {
            i++;
        }

        var punto = false;
        while (i < t.Length && (char.IsAsciiDigit(t[i]) || (t[i] == '.' && !punto)))
        {
            if (t[i] == '.')
            {
                punto = true;
            }

            i++;
        }

        var n = t[..i].TrimEnd('.');
        return double.TryParse(n, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }

    // =================================================================================
    //  ESCRITURA Y MIGRACIÓN
    // =================================================================================

    /// <summary>
    /// Pone un valor <b>pisando</b> el que hubiera. Es el <c>PonCfg</c> de la macro, y
    /// como allá se usa dentro de los parches.
    /// </summary>
    public void Poner(string parametro, string valor) => _puestos[parametro] = valor;

    /// <summary>
    /// Pone un valor <b>solo si el parámetro no está</b>. Es el <c>PonCfgSiFalta</c>: así
    /// una versión nueva agrega sus renglones sin tocar lo que el usuario ya ajustó.
    /// </summary>
    public void PonerSiFalta(string parametro, string valor)
    {
        if (!_puestos.ContainsKey(parametro))
        {
            _puestos[parametro] = valor;
        }
    }

    /// <summary>
    /// Aplica los valores guardados de un archivo, y <b>sella la versión</b>.
    /// </summary>
    /// <remarks>
    /// Es la mitad que importa de <c>MigrarConfig</c> y de los parches: un parche se
    /// aplica una sola vez porque, en cuanto corre, deja <c>VERSION_PARCHE</c> en su
    /// número; a partir de ahí el valor es del usuario. Aquí no hace falta la lista de
    /// parches de la v30 a la v50 —la tabla de omisión ya trae el resultado de todos—,
    /// pero el mecanismo se conserva para las versiones que vengan.
    /// </remarks>
    public void Aplicar(IReadOnlyDictionary<string, string> guardados)
    {
        foreach (var (k, v) in guardados)
        {
            _puestos[k] = v;
        }

        // Un archivo de una versión anterior se sube a esta, igual que la macro sube la
        // hoja: los renglones que falten salen de la tabla de omisión.
        if (Numero("VERSION_CONFIG") < VersionConfig)
        {
            Poner("VERSION_CONFIG", VersionConfig.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        if (Numero("VERSION_PARCHE") < VersionParche)
        {
            Poner("VERSION_PARCHE", VersionParche.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Lo que hay que guardar: <b>solo lo que el usuario cambió</b> respecto de la tabla.
    /// </summary>
    /// <remarks>
    /// Guardar los 260 renglones tendría el mismo problema que la hoja de Excel: un valor
    /// nuevo de la aplicación no entraría nunca, porque el archivo ya lo trae escrito con
    /// el valor viejo.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ParaGuardar()
    {
        var salida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in _puestos)
        {
            if (!Indice.TryGetValue(k, out var r) || !string.Equals(r.Valor, v, StringComparison.Ordinal))
            {
                salida[k] = v;
            }
        }

        return salida;
    }

    /// <summary>Los renglones, con el valor que se está usando: para mostrar la hoja.</summary>
    public IEnumerable<Renglon> Renglones() =>
        PorOmision.Select(r => new Renglon(r.Parametro, Crudo(r.Parametro) ?? r.Valor, r.Descripcion));

    // =================================================================================
    //  LA TABLA, RENGLÓN POR RENGLÓN, COMO LA ESCRIBE CrearHojaConfig
    // =================================================================================
    //  El orden es el de la macro, incluidos los renglones que allá están fuera de sitio
    //  (VERSION_PARCHE en medio de los rótulos de las cadenas, por ejemplo). Se respeta a
    //  propósito: es el orden en que el usuario los tiene en su hoja y los va a buscar.
    //
    //  Los «<<<» de algunas descripciones son los que la macro pone para señalar lo que
    //  cambió en la última versión. Se dejan.

    private static Renglon P(string parametro, string valor, string descripcion) =>
        new(parametro, valor, descripcion);

    /// <summary>La hoja CONFIG de omisión, con los valores de la v29 / parche 50.</summary>
    public static readonly IReadOnlyList<Renglon> PorOmision = new[]
    {
        P("VERSION_CONFIG", "29", "NO BORRAR: version de esta hoja CONFIG"),
        // 25 y no los 15 de la hoja: se pidió expresamente que el juego arranque en Y = 25
        // cuando el dibujo está vacío, en lugar de pegado al origen. Con 15 el rótulo de la
        // planta —que va DEBAJO de las burbujas y las cotas— se salía por abajo del origen.
        P("OFFSET_Y_INICIAL", "25", "LA PLANTA SE DIBUJA A PARTIR DE ESTA Y DEL ORIGEN"),

        // AÑADIDO, no está en la hoja de la macro. Allá la planta arranca siempre en la Y
        // de OFFSET_Y_INICIAL, así que dibujar dos veces encima del mismo plano las
        // encimaba. Aquí se mira QUÉ HAY YA DIBUJADO y la planta se pone este aire por
        // encima de lo más alto; si el dibujo está vacío, va al origen.
        P("AIRE_SOBRE_LO_DIBUJADO_M", "5",
          "La planta se dibuja a esta altura por encima de lo mas alto que ya haya en el " +
          "dibujo. Si el dibujo esta vacio, en el origen"),
        P("FACTOR_UNIDADES", "1", "ETABS se lee en metros. 1 = dibujar en m, 100 = en cm"),
        P("PREFIJO_CAPAS", "E-", "Prefijo de las capas creadas"),
        P("ALTURA_TEXTO", "0.12", "Altura de las etiquetas"),
        P("DIBUJAR_ETIQUETAS", "SI", "Interruptor general de los rotulos"),
        P("ETIQUETA_ID_COLUMNAS", "NO", "NO = sin C23, C24..."),
        P("ETIQUETA_SEC_COLUMNAS", "SI", "SI = rotular la SECCION de columnas y castillos"),
        P("ETIQUETA_ID_TRABES", "NO", "El ID de las vigas no se rotula"),
        P("ETIQUETA_SEC_TRABES", "SI", "Rotular la SECCION de las vigas"),
        P("TRABE_ROTULO_CENTRADO", "SI", "SI = LA SECCION DE LA TRABE / VIGA DE ACERO VA JUSTO EN MEDIO"),
        P("ETIQUETA_TRABES_PREFIJO_SECCION", "T", "Solo vigas cuya seccion empieza asi"),
        P("ALTURA_TEXTO_SECCION", "0", "0 = automatica (0.8 de ALTURA_TEXTO)"),
        P("PIER_SEPARACION_CM", "6", "Separacion extra del PIER"),

        // AÑADIDO, no está en la hoja de la macro: allá la capa de las dalas es E-DALA a
        // secas. Se pidió que se llame E-CADENA, que es como se le llama a la pieza en obra.
        // El TIPO sigue siendo DALA; esto solo cambia el nombre de la capa.
        P("CAPA_DALA", "CADENA", "NOMBRE DE LA CAPA DE LAS DALAS (con prefijo: E-CADENA)"),
        P("DOBLE_LINEA", "SI", "Muros y trabes a espesor real"),
        P("RELLENAR_COLUMNAS", "SI", "Achurado solido en columnas y perfiles"),
        P("COLOR_RELLENO_BLOQUE", "2", "COLOR DEL RELLENO DE COLUMNAS Y CASTILLOS (2 = amarillo)"),
        P("REDEFINIR_BLOQUES", "SI", "SI = volver a armar los bloques que ya existian"),
        P("BLOQUE_SUFIJO_ROTACION", "NO", "NO = un solo bloque por seccion"),
        P("BLOQUE_ROTACION_EXTRA_GRADOS", "0", "DESFASE DEL BLOQUE: PRUEBA 0, 90 O -90"),
        P("CIMENTACION_STORIES", "BASE,CIMENTACION,FOUNDATION", "NOMBRES DE STORY QUE SON LA BASE"),
        P("CAPA_CADENA_DESPLANTE", "CADENA DESPLANTE", "CAPA DE LA CADENA DE DESPLANTE (con prefijo: E-CADENA DESPLANTE)"),
        P("COLOR_CADENA_DESPLANTE", "1", "Color de esa capa"),
        P("CIMENTACION_SIN_PUNTEADA", "SI", "SI = EN LA CIMENTACION NUNCA VA PUNTEADA"),
        P("CIMENTACION_ROTULAR_CADENA", "SI", "SI = ROTULAR LA SECCION DE ETABS AL CENTRO DE LA CADENA"),
        P("CIMENTACION_SIN_ROTULO_TRABES", "SI", "SI = SIN NOMBRES NI SECCIONES DE TRABES EN ESE NIVEL"),
        P("CIMENTACION_DIBUJA_COLUMNAS", "SI", "SI = CASTILLOS Y COLUMNAS QUE DESPLANTAN EN LA BASE"),
        P("CIMENTACION_COLUMNA_TOL_CM", "20", "Holgura en Z para saber que desplanta en la base"),
        P("ROTULO_NOMBRE_CIMENTACION", "CIMENTACION", "LO QUE DICE EL ROTULO CUANDO EL NIVEL ES LA BASE"),
        P("DIBUJAR_LOSAS", "SI", "Dibujar el contorno de las losas"),
        P("IGNORAR_LOSA_ESCALERA", "SI", "SI = las losas de escalera NO se dibujan"),
        P("PALABRAS_ESCALERA", "ESCALERA,ESCAL,STAIR,RAMPA,RAMP,DESCANSO", "Palabras que identifican escaleras"),
        P("PLANTAS_POR_FILA", "100", "100 = todas en una fila hacia la derecha"),
        // 10.00 y no los 5.00 de la hoja: se pidió expresamente, y con 5 las burbujas y las
        // cotas de una planta quedaban a un palmo de las de la siguiente.
        P("SEPARACION_ENTRE_PLANTAS", "10", "10.00 m A LA DERECHA ENTRE PLANTAS"),
        P("SEPARACION_CUENTA_EJES", "SI", "SI = la separacion cuenta ejes, burbujas y cotas"),
        P("ORDEN_NIVELES", "ASC", "ASC = Story1 primero (izquierda a derecha)"),
        P("MARGEN", "3", "Margen del titulo de la planta"),
        P("SEPARACION_X", "0", "0 = automatica; si no, paso horizontal fijo"),
        P("SEPARACION_Y", "0", "0 = automatica; si no, paso vertical fijo"),
        P("NIVELES_A_DIBUJAR", "TODOS", "TODOS o lista: Base,Story1,Story2"),
        P("CASTILLO_LADO_MAX_CM", "20", "Columnas con ambos lados <= a esto = CASTILLO"),
        P("DALA_PERALTE_MAX_CM", "25", "Trabes con peralte <= a esto = DALA"),
        P("ESPESOR_MURO_CM", "15", "Espesor si ETABS no lo entrega"),
        P("MAMPOSTERIA_LINEA", "SI", "SI = polilinea ancha al centro del muro"),
        P("MAMPOSTERIA_ANCHO", "0.06", "GLOBAL WIDTH de esa polilinea"),
        P("COLOR_MAMPOSTERIA", "30", "Color de la capa E-MAMPOSTERIA"),
        P("PALABRAS_MAMPOSTERIA", "TABIQUE,TABICON,BLOCK,BLOQUE,MAMPOSTERIA,LADRILLO,ADOBE", "NOTAS del muro = mamposteria"),
        P("PALABRAS_CONCRETO", "CONCRETO,CONCRETE,C.A.,REFORZADO", "NOTAS del muro = concreto"),
        P("MAMPOSTERIA_AUNQUE_TAPADO", "SI", "SI = dibujarla aunque el muro este bajo la cadena"),
        P("DINTEL_SIN_LINEA", "SI", "SI = dinteles / pretiles sin linea"),
        P("MURO_ALTURA_MIN_M", "1.5", "Altura minima para tomarlo como muro completo"),
        P("MURO_FRACCION_ENTREPISO", "0.75", "Fraccion de la altura de entrepiso"),
        P("PRENDER_LWDISPLAY", "SI", "SI = prender LWDISPLAY"),
        P("MUROS_AL_PANO", "SI", "SI = el muro termina en el PANO del frame"),
        P("TRABES_AL_PANO", "SI", "SI = las trabes tambien se recortan"),
        P("PANO_TOLERANCIA_CM", "25", "Holgura del encuentro con el frame"),
        P("PANO_RECORTE_MAX", "0.4", "Fraccion maxima que se recorta por lado"),
        P("PANO_BUSCAR_OTRO_NIVEL", "SI", "SI = buscar el frame en otros niveles"),
        P("OCULTAR_MURO_BAJO_CADENA", "SI", "SI = no dibujar el muro bajo la dala"),
        P("CADENA_INCLUYE_TRABES", "NO", "SI = las trabes tambien tapan al muro"),
        P("TRASLAPE_MINIMO", "0.8", "Fraccion del muro cubierta por la cadena"),
        P("TOLERANCIA_CADENA_CM", "10", "Desviacion permitida de la cadena"),
        P("ACOTAR_EJES", "SI", "SI = acotar los ejes (interruptor general)"),
        P("COTAS_ARRIBA", "SI", "SI = cotas ARRIBA de la planta"),
        P("COTAS_ABAJO", "SI", "<<< SI = cotas ABAJO de la planta"),
        P("COTAS_IZQUIERDA", "SI", "SI = cotas a la IZQUIERDA de la planta"),
        P("COTAS_DERECHA", "SI", "<<< SI = cotas a la DERECHA de la planta"),
        P("COTAS_SEPARACION", "0.75", "SEPARACION DE LA PRIMERA CADENA DE COTAS (arriba y a la izquierda)"),
        P("COTAS_SEPARACION_TOTAL", "1.17", "<<< SOLO LA COTA DEL ANCHO TOTAL (1.17).  NO mueve las burbujas"),
        P("EJES_INICIO_BURBUJA_M", "2", "<<< LAS BURBUJAS DE LOS CUATRO LADOS, A ESTA SEPARACION (2.00 m).  NO lo mueven las cotas"),
        P("COTA_TOTAL_EXT_LINE_EXT", "0", "<<< La cota total, igual que el estilo (0 = no se pasa nada)"),
        P("EJES_HOLGURA_COTA_M", "0.15", "Aire entre la cota total y la punta del eje (solo si el de arriba es 0)"),
        P("EJES_SALE_CORTO_M", "0", "0 = la DERECHA y el ABAJO salen igual que arriba y la izquierda"),
        P("COTA_TOTAL", "SI", "SI = agregar la cota total"),
        P("COTAS_EMPUJAR_EJES", "SI", "SI = ejes y burbujas afuera de las cotas"),
        P("COLOR_COTAS", "8", "COLOR DE LA CAPA E-COTAS"),
        P("ESTILO_COTA", "COTA_DIM", "Estilo de cota que se usa (y que se crea)"),
        P("CREAR_ESTILO_COTA", "SI", "SI = crear / actualizar el estilo COTA_DIM"),
        P("ESTILO_TEXTO_COTA", "COTA", "Nombre del estilo de TEXTO de las cotas"),
        P("COTA_NOMBRE_FUENTE", "Century Gothic", "FUENTE DEL ESTILO DE TEXTO DE LAS COTAS"),
        P("COTA_NEGRITA", "SI", "SI = ESE ESTILO VA EN NEGRITA"),
        P("FUENTE_COTA", "gothicb.ttf", "Respaldo si no esta la fuente: Century Gothic Bold"),
        P("ALTURA_ESTILO_COTA", "0.1", "Altura del estilo de TEXTO de la cota"),
        P("COTA_TEXT_HEIGHT", "0.1", "ALTURA DEL TEXTO DE LAS COTAS"),
        P("COTA_FLECHA", "_OBLIQUE", "Flecha de las cotas (1a, 2a y leader)"),
        P("COTA_ARROW_SIZE", "0.05", "Arrow size del estilo de cota"),
        P("COTA_OFFSET_DIM_LINE", "0.04", "Offset from dim line"),
        P("COTA_CENTER_MARK", "0.04", "Center mark MARK"),
        P("COTA_EXT_LINE_EXT", "0", "<<< Ext line ext de las cotas (0.0000)"),
        P("COTA_EXT_LINE_OFFSET", "0.5", "<<< Ext line offset de las cotas (0.5000)"),
        P("COTA_TEXTO_EN_MEDIO", "SI", "SI = el numero va EN MEDIO de la linea de cota"),
        P("COTA_PRECISION", "3", "PRECISION DE LAS COTAS (3 = 0.000)"),
        P("COTA_COLOR_TEXTO", "1", "Color del texto de la cota (1 = rojo)"),
        P("COTA_FORZAR_ALTURA", "SI", "SI = forzar la altura del texto en cada cota"),
        P("COTA_SEPARADOR_DECIMAL", ".", "SEPARADOR DECIMAL DE LAS COTAS: PUNTO"),
        P("COTA_ESCALA_GENERAL", "1", "DIMSCALE del estilo (subelo si quieres todo mas grande)"),
        P("DIBUJAR_ARMADO_LOSA", "SI", "SI = dibujar el armado de las losas"),
        P("LOSA_APOYO_4_LADOS", "SI", "SI = con 4 lados va el armado completo"),
        P("LOSA_APOYO_CUBRE", "0.7", "Fraccion del lado que debe estar apoyada"),
        P("LOSA_APOYO_TOL_CM", "25", "Holgura perpendicular del apoyo"),
        P("ARMADO_AL_PANO_CADENA", "SI", "SI = en los bordes el armado llega al paño de la cadena"),
        P("ARMADO_PANO_SIEMPRE", "SI", "SI = SIEMPRE AL PANO, TAMBIEN EN LOS APOYOS INTERMEDIOS (NUNCA A LA MITAD)"),
        P("LOSA_VECINA_DELTA_CM", "20", "Cuanto se asoma para buscar losa vecina"),
        P("MALLA_SEP_CM", "15", "SEPARACION DE LAS VARILLAS DE LA PARRILLA (cm)"),
        P("MALLA_DOS_DIRECCIONES", "SI", "SI = parrilla en las DOS direcciones"),
        P("MALLA_VARILLA_EXTREMOS", "SI", "SI = varillas en los extremos para CERRAR la parrilla"),
        P("MALLA_ENCIMA", "X", "Direccion que va ENCIMA en los cruces (X o Y)"),
        P("MALLA_TRIM_HOLGURA", "0", "0 = la varilla llega HASTA la otra varilla"),
        P("MALLA_AL_PANO", "SI", "SI = la parrilla se ajusta al PANO de la cadena o muro"),
        P("MALLA_PANO_PRIMERO", "SI", "SI = al PRIMER pano (no al eje ni al de afuera)"),
        P("MALLA_PANO_MAX_CM", "40", "Tope de ese ajuste al pano"),
        P("MALLA_DOBLE_LINEA", "SI", "SI = varillas en doble linea"),
        P("MALLA_MAX_LINEAS", "200", "Tope de varillas por direccion (valvula de escape)"),
        P("MALLA_RECORTAR_CONTORNO", "SI", "SI = la parrilla se recorta al contorno REAL de la losa"),
        P("MALLA_SEGMENTO_MIN_CM", "15", "Tramos de varilla mas cortos que esto no se dibujan"),
        P("ARMADO_LOSA_SOLO_SLAB", "SI", "SI = solo SLAB (el DECK no se arma)"),
        P("ARMADO_LOSA_ESPESOR_MIN_CM", "8", "Losas mas delgadas no se arman"),
        P("ARMADO_LOSA_DOS_DIRECCIONES", "SI", "SI = armado en las dos direcciones"),
        P("ARMADO_LOSA_MARGEN_CM", "0", "Margen del armado contra el borde"),
        P("ARMADO_LOSA_ESCALA_VARILLA", "1", "Multiplica el grosor de la varilla"),
        P("ARMADO_LOSA_FILETE", "SI", "SI = redondear los quiebres de la bayoneta"),

        // AÑADIDOS: cual de los dos armados se dibuja. La PARRILLA en NO porque llenaba de
        // rejilla todos los tableros, y lo que va en un tablero apoyado es la BAYONETA.
        P("ARMADO_LOSA_BAYONETA", "SI",
          "<<< SI = EN EL TABLERO APOYADO VA LA BAYONETA (la varilla con sus quiebres)"),
        P("ARMADO_LOSA_PARRILLA", "NO",
          "NO = sin rejilla de varillas en los tableros (llena el plano)"),
        P("ARMADO_LOSA_LADO_MIN_CM", "50", "Tableros mas chicos no se arman"),
        P("ARMADO_LOSA_TEXTO", "SI", "Rotular seccion y espesor de la losa"),
        P("COLOR_ARMADO_LOSA", "142", "Color de la capa E-ARMADO LOSA"),
        P("OCULTAR_CAPA_LOSA", "SI", "SI = apagar solo la capa E-LOSA"),
        P("LOSA_HATCH", "SI", "SI = en la losa de un sentido va HATCH, no varillas"),
        P("LOSA_HATCH_PATRON", "ANSI37", "PATRON DEL HATCH DE LA LOSA"),
        P("LOSA_HATCH_ESCALA", "0.0475", "ESCALA DE ESE HATCH (la de la macro)"),

        // EL COLOR DEL ACHURADO, POR OBJETO: el 142, que es el que se pidió y el mismo del
        // armado de la losa. Va por OBJETO y no por capa a propósito, porque la capa
        // E-VOLADO se pidió en 252 —el gris del contorno— y el achurado tiene que verse: con
        // los dos datos juntos, el borde del voladizo va en 252 y su rayado en 142.
        //
        //   0 = por capa, que era como estaba antes.
        P("LOSA_HATCH_COLOR", "142",
          "<<< COLOR DEL ACHURADO DE LA LOSA, POR OBJETO (142; 0 = el de la capa)"),

        // AÑADIDO, Y ES LO QUE HACÍA QUE EL ACHURADO SE VIERA COMO UNA MANCHA GRIS.
        //
        //   El ANSI37 tiene sus líneas a 0.125 de unidad. Con la escala de la macro, 0.0475,
        //   la separación real queda en 0.125 x 0.0475 = 0.0059 m, o sea SEIS MILÍMETROS.
        //   En un tablero de 6 x 12 m eso son más de dos mil líneas, y a 1/75 quedan a
        //   0.08 mm unas de otras: no se ve un achurado, se ve un relleno gris uniforme.
        //   Y encima en color 252, que es gris oscuro, así que parece una sombra.
        //
        //   Con LOSA_HATCH_ESCALA_AUTO la escala se calcula de la separación que se quiera
        //   ver: escala = separacion / 0.125. Con 25 cm sale escala 2, y entonces sí se
        //   distingue el rayado a 45 grados, que es lo que tiene que verse.
        //   Y VA EN **NO**, porque la escala buena es la de la macro: 0.0475. Se pidió ese
        //   valor y ese valor manda. El automático se queda por si algún día se dibuja en
        //   otras unidades, pero apagado: el achurado que se veía como una mancha gris no era
        //   por la escala, era porque el color 252 sobre fondo oscuro no se ve —ahora va en
        //   142— y porque el hatch no llegaba a crearse.
        P("LOSA_HATCH_ESCALA_AUTO", "NO",
          "NO = manda LOSA_HATCH_ESCALA, la escala de la macro (0.0475)"),
        P("LOSA_HATCH_SEPARACION_CM", "25",
          "<<< SEPARACION REAL ENTRE LINEAS DEL ACHURADO, EN CM (25 = se ve bien a 1/75)"),
        P("LOSA_HATCH_ANGULO", "45", "ANGULO DEL HATCH (45 grados)"),
        P("LOSA_TEXTO_BLOQUE", "SI", "SI = MTEXT dentro de un BLOQUE por uso de losa"),
        P("LOSA_TEXTO_BLOQUE_PREFIJO", "TEXTO LOSA ", "Prefijo del nombre de esos bloques"),

        // AÑADIDO: EL CORTE POR UN EJE, dibujado al lado de la planta. Se pidió que se
        // dibuje el que se haya elegido en la pestaña del modelo, a 10 m de la planta.
        // Juntos se leen: la planta da los espesores y las distancias entre ejes, y el
        // corte, las alturas que la planta no puede dar.
        P("CORTE_DIBUJAR", "SI",
          "<<< SI = SE DIBUJA EL CORTE ELEGIDO AL LADO DE LA PLANTA ESTRUCTURAL"),
        P("CORTE_SEPARACION_M", "10",
          "<<< A CUANTOS METROS DE LA PLANTA SE PONE EL CORTE (a su derecha)"),
        P("CORTE_ESPESOR_CM", "60",
          "REBANADA QUE ENTRA EN EL CORTE: 0 dejaria el corte vacio en un modelo real"),
        P("CORTE_ROTULO", "CORTE  POR  EL  EJE  %E", "Rotulo del corte (%E = nombre del eje)"),
        P("CORTE_ROTULO_ABAJO_M", "1.2", "Cuanto baja el rotulo del corte"),
        P("CORTE_NIVEL_VUELA_M", "0.6", "Cuanto sale la linea de nivel por los lados"),
        P("LOSA_TEXTO_REDEFINIR", "SI", "SI = VOLVER A ARMAR EL BLOQUE (hace falta para ver la altura nueva; ponlo en NO cuando ya te guste)"),
        P("LOSA_PALABRAS_AZOTEA", "AZOTEA,CUBIERTA,TECHO,ROOF", "Palabras de la seccion = AZOTEA"),
        P("LOSA_PALABRAS_ENTREPISO", "ENTREPISO,PISO,FLOOR,SLAB", "Palabras de la seccion = ENTREPISO"),
        P("LOSA_USO_POR_OMISION", "ENTREPISO", "Uso cuando la seccion no dice nada"),
        P("LOSA_TEXTO_ANCHO", "0", "Ancho del MTEXT (0 = sin ajuste de linea)"),
        P("LOSA_USAR_ESTILO", "SI", "SI = EL MTEXT DE LA LOSA EN SU PROPIO ESTILO"),
        P("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS", "ESTILO SOLO PARA EL ROTULO DE LA LOSA"),
        P("LOSA_NOMBRE_FUENTE", "Bahnschrift", "Nombre de la fuente de ese estilo"),
        P("LOSA_FUENTE", "bahnschrift.ttf", "Archivo de la fuente (respaldo)"),
        P("LOSA_TEXTO_ALTURA", "0.072", "<<< AQUI CAMBIAS LA ALTURA DEL ROTULO DE LA LOSA (0 = usar el factor)"),
        P("LOSA_TEXTO_FORZAR_ALTURA", "SI", "SI = SI CAMBIAS LA ALTURA, EL BLOQUE SE REARMA AUNQUE REDEFINIR ESTE EN NO"),
        P("LOSA_TEXTO_FACTOR", "0.5", "0.5 = LA MITAD DE LA ALTURA DEL TEXTO DE SECCIONES"),
        P("LOSA_TEXTO_INTERLINEA", "1.45", "Separacion entre renglones, en alturas de texto"),
        P("CADENA_ROTULAR", "SI", "SI = rotular la seccion de las cadenas de cerramiento"),
        P("CADENA_PREFIJO_SECCION", "CC", "Solo las secciones que empiezan asi"),
        P("CADENA_CORTA_LINEA", "NO", "NO = la linea del muro va COMPLETA (el texto lleva fondo)"),
        P("CADENA_HUECO_MARGEN_CM", "2", "Margen del hueco (solo si se corta)"),
        P("CADENA_TEXTO_MTEXT", "SI", "SI = el rotulo de la cadena es un MTEXT"),
        P("CADENA_TEXTO_FONDO", "SI", "SI = con FONDO, borra lo que tenga atras"),
        P("CADENA_USAR_ESTILO", "SI", "SI = EL ROTULO DE LAS CADENAS EN SU PROPIO ESTILO"),
        P("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS", "ESTILO SOLO PARA EL ROTULO DE LAS CADENAS"),
        P("CADENA_NOMBRE_FUENTE", "Bahnschrift", "Nombre de la fuente de ese estilo"),
        P("CADENA_FUENTE", "bahnschrift.ttf", "Archivo de la fuente (respaldo)"),
        P("CADENA_TEXTO_FACTOR", "0.5", "0.5 = LA MITAD DE LA ALTURA DEL TEXTO DE SECCIONES"),
        P("CADENA_TEXTO_ALTURA", "0.09", "<<< ALTURA DEL ROTULO DE LAS CADENAS (0 = usar el factor)"),
        P("MTEXT_ANCHO_AUTOMATICO", "SI", "SI = la caja del MTEXT se ajusta sola al texto"),
        P("COLUMNA_TEXTO_ESQUINA", "SI", "SI = la seccion va en la ESQUINA SUPERIOR DERECHA"),
        P("COLUMNA_TEXTO_SEPARACION_CM", "2", "Separacion de ese texto a la esquina"),
        P("VERSION_PARCHE", "50", "NO BORRAR: ultimo parche aplicado"),
        P("LOSACERO_FRANJAS", "SI", "SI = LOSACERO CON FRANJAS DE HATCH FLEX"),
        P("LOSACERO_PALABRAS", "LOSACERO,DECK,STEEL DECK,LAMINA ACANALADA", "PALABRAS DE LA ETIQUETA = LOSACERO"),
        P("LOSACERO_HATCH_PATRON", "FLEX", "PATRON DE LAS FRANJAS"),
        P("LOSACERO_HATCH_ESCALA", "0.02", "<<< ESCALA DEL HATCH FLEX DE LA LOSACERO (0.02)"),
        P("LOSACERO_FRANJA_ANCHO_M", "0.15", "<<< ANCHO DE LA FRANJA DONDE VA EL HATCH (0.15)"),
        P("LOSACERO_TEXTO", "SI", "<<< MTEXT AL MEDIO DE LA LOSACERO, COMO BLOQUE"),
        P("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C", "<<< %C = ULTIMO NUMERO DE LAS NOTAS DE ETABS (LOSACERO CAL 24 -> 24)"),
        P("LOSACERO_TEXTO_BLOQUE_PREFIJO", "TEXTO LOSACERO ", "Prefijo del bloque: TEXTO LOSACERO CAL 24"),
        P("LOSACERO_TEXTO_REDEFINIR", "NO", "NO = si el bloque ya existe se respeta como lo editaste"),
        P("LOSACERO_CALIBRE_OMISION", "24", "Calibre si las notas de ETABS no traen numero"),
        P("LOSACERO_TEXTO_ALTURA", "0", "Altura de ese rotulo (0 = la del rotulo de losa)"),
        P("LOSACERO_TEXTO_FONDO", "SI", "SI = con fondo, tapa el hatch de las franjas"),
        P("PANO_ALMA_W", "SI", "<<< SI = SOLO EN LAS COLUMNAS W LA LINEA LLEGA AL ALMA (a las vigas, al pano)"),
        P("LOSACERO_FRANJA_SEP_M", "0.8", "De centro a centro entre franjas"),
        P("LOSACERO_FRANJA_LARGO_MIN_M", "0.3", "Franjas mas cortas no se dibujan"),
        P("LOSACERO_FRANJA_CONTORNO", "SI", "SI = dejar las 2 lineas de la franja"),
        P("COLOR_LOSACERO", "6", "Color de la capa E-LOSACERO"),
        P("LOSACERO_ROTULO", "NO", "SI = ponerle tambien el rotulo de losa"),
        P("CAPA_PIERS", "PIERS", "CAPA APARTE PARA EL PIER DE LOS MUROS"),
        P("COLOR_PIERS", "7", "Color de la capa PIERS"),
        P("MAMPOSTERIA_GAP_M", "0.05", "SEPARACION DE LA POLILINEA AL INICIO Y FIN DEL MURO"),
        P("MAMPOSTERIA_GAP_LARGO_MIN_M", "1", "SOLO SI EL MURO MIDE MAS QUE ESTO"),
        P("LOSA_HATCH_SOLO_VOLADO", "SI", "SI = EL HATCH VA SOLO EN LA LOSA VOLADA"),

        // AÑADIDOS de la losa en voladizo y del contorno. La macro dibuja el hatch del volado
        // en la capa del armado; se pidió que el volado tenga CAPA PROPIA, que la de la losa
        // se APAGUE y que el contorno no se meta dentro del muro ni de la cadena.
        P("CAPA_VOLADO", "VOLADO", "CAPA DE LA LOSA EN VOLADIZO (con prefijo: E-VOLADO)"),

        // EL HATCH DEL VOLADO VA POR LA NOTA, no por la geometria: se pidio expresamente que
        // el ANSI37 salga SOLO en las losas cuya nota o seccion diga VOLADO. Las palabras son
        // las de LOSA_PALABRAS_VOLADO, que ya venia en la hoja.
        P("VOLADO_POR_NOTA", "SI",
          "<<< SI = EL VOLADO SE RECONOCE POR SU NOTA; NO = por sus lados apoyados"),
        P("COLOR_VOLADO", "252", "COLOR DE LA CAPA E-VOLADO (252, como se pidio)"),
        P("APAGAR_CAPA_LOSA", "SI",
          "SI = la capa E-LOSA se deja APAGADA y E-VOLADO encendida"),
        P("LOSA_CONTORNO_FUERA_DE_MUROS", "SI",
          "SI = el contorno de la losa NO se dibuja dentro del muro ni de la cadena"),

        // LO MISMO PARA EL VOLADO, que antes se dibujaba completo a propósito. Se pidió que
        // esa línea sea SOLO EL CONTORNO EXTERIOR y que no toque la cadena ni el muro. La
        // polilínea cerrada se sigue creando, pero solo como MOLDE del achurado, y se borra.
        P("VOLADO_CONTORNO_FUERA_DE_MUROS", "SI",
          "<<< SI = LA LINEA DEL VOLADO ES SOLO EL CONTORNO EXTERIOR, NO TOCA CADENA NI MURO"),

        // DOS VOLADIZOS PEGADOS SON UN SOLO PAÑO. La raya del medio es la orilla que las dos
        // losas comparten, y en la obra no existe: el concreto es continuo. Casi siempre son
        // una losa partida en dos por un eje, porque en el modelo hace falta el nudo.
        P("VOLADO_SIN_DIVISIONES", "SI",
          "<<< SI = VARIOS VOLADOS JUNTOS SE DIBUJAN CON UN SOLO PERIMETRO, SIN LA RAYA DEL MEDIO"),

        // EL TERCER INTENTO DEL ACHURADO: el comando -HATCH de AutoCAD. Lo que sale por aquí
        // es un HATCH auténtico, con su patrón, no una imitación con líneas.
        P("LOSA_HATCH_POR_COMANDO", "SI",
          "<<< SI = si la API no crea el hatch, se manda el comando -HATCH (sigue siendo hatch)"),
        P("VIGAS_CORTAR_EN_CRUCES", "SI",
          "SI = la viga muere en la CARA de la viga que cruza, no le pasa por encima"),
        P("CIMENTACION_SIN_MUROS_SIN_COLUMNAS", "SI",
          "SI = en la base, sin muros no se dibujan columnas ni castillos"),
        P("LOSA_PALABRAS_VOLADO", "VOLADO,VOLADIZO,VOLADA,CANTILEVER", "PALABRAS DE LAS NOTAS = LOSA VOLADA"),
        P("PANO_SOLAPE_CM", "0", "<<< 0 = LA LINEA TERMINA EXACTAMENTE EN EL PANO DEL ELEMENTO"),
        P("PANO_ALMA_W_MODO", "ALMA", "<<< COLUMNA W: entra entre patines -> CARA DEL ALMA; por el patin -> al CENTRO.  (ALMA / CENTRO / PATIN)"),
        P("EJES_EXTREMOS_AL_PANO", "SI", "SI = 1er Y ULTIMO EJE AL PANO EXTERIOR DEL MURO"),
        P("EJES_PANO_TOL_CM", "25", "Tolerancia para hallar el muro sobre el eje"),

        // AÑADIDO: un eje, UNA línea. La cuadrícula del modelo trae ejes declarados dos
        // veces y salían dos líneas encima de la otra —se ve como un eje más grueso—, con
        // dos burbujas y dos cotas pisándose. 0 = no se une nada.
        P("EJES_UNIR_TOL_CM", "1",
          "<<< DOS EJES A MENOS DE ESTO SON EL MISMO: SE DIBUJA UNA SOLA LINEA (0 = no unir)"),
        P("ROTULO_SEPARACION_EJES", "0.5", "AIRE ENTRE LOS EJES DE ABAJO Y EL ROTULO (m)"),
        P("PANO_ALARGAR_MAX_CM", "150", "<<< CUANTO SE ALARGA LA VIGA QUE QUEDO CORTA EN EL MODELO (1.50 m)"),
        P("PANO_BUSCA_CM", "150", "<<< Radio de busqueda del elemento al que hay que llegar (1.50 m)"),

        // AÑADIDO: el interruptor de todo el ajuste al paño. En NO, las lineas del muro y de
        // la trabe llegan al EJE del castillo, como salian antes.
        P("LINEAS_AL_PANO", "SI",
          "SI = LAS LINEAS DEL MURO Y DE LA TRABE MUEREN EN EL PANO DEL CASTILLO, COLUMNA O " +
          "PERFIL, no en su eje"),
        P("BURBUJA_CRUZ_4_LINEAS", "SI", "SI = 4 RAYITAS EN LA BURBUJA, TODAS DE SU COLOR"),
        P("EJES_RECORTE_M", "0", "0 = no se le quita nada al eje por la derecha ni por abajo"),
        P("CADENA_SIN_MURO_MARCAR", "SI", "SI = cadena sin muro de piso a techo con otra linea"),
        P("CADENA_SIN_MURO_LINETYPE", "ACAD_ISO02W100", "TIPO DE LINEA DE ESAS CADENAS"),
        P("CADENA_SIN_MURO_LTSCALE", "0", "0 = automatico (0.01 si el dibujo va en metros)"),
        P("CADENA_SIN_MURO_CUBRE", "0.5", "Fraccion con muro abajo para NO marcarla"),
        P("LOSA_HATCH_AL_PANO", "SI", "SI = EL HATCH LLEGA AL PANO DE LA CADENA, NO A LA MITAD"),
        P("TRAER_AL_FRENTE", "SI", "SI = subir CAPAS_AL_FRENTE encima de todo (Bring to Front)"),
        // CADENA y no DALA: la capa de las dalas se llama E-CADENA —ver CAPA_DALA—, así que
        // aquí tiene que ir con ese nombre o no se subiría al frente.
        P("CAPAS_AL_FRENTE", "CADENA,CADENA DESPLANTE,TRABE,ACERO", "<<< CAPAS ENCIMA DE TODO (incluye ACERO: las vigas de acero al frente)"),

        // AÑADIDO, no está en la hoja de la macro. Los ROTULOS tienen que quedar encima de
        // todo, y en una SEGUNDA pasada del orden de dibujo: subidos junto con las trabes y
        // las dalas, unas veces quedaban encima y otras debajo. PIERS va sin el prefijo E-,
        // como en la macro.
        // VACÍO A PROPÓSITO, y esto es importante: el MTEXT NO va al frente.
        //
        //   El orden que se quiere, de atrás hacia adelante, es
        //         losa y armado  ->  polilínea de MAMPOSTERÍA  ->  MTEXT  ->  LÍNEAS
        //   o sea: el rótulo tapa la polilínea ancha del muro —para eso lleva fondo— pero
        //   las líneas de la cadena y del acero le pasan POR ENCIMA.
        //
        //   Eso sale solo: el MTEXT se dibuja después de la mampostería, y al final se
        //   suben al frente únicamente las capas de CAPAS_AL_FRENTE. Subir también el texto
        //   —como se hacía— lo dejaba encima de las líneas, que es justo lo que no se quería.
        //   Si alguien quiere el texto arriba de todo, aquí se escribe TEXTO,PIERS.
        P("CAPAS_TEXTO_AL_FRENTE", "",
          "VACIO = el MTEXT queda encima de la mamposteria pero DEBAJO de las lineas"),

        // AÑADIDO: la otra mitad del orden de dibujo. La losa y su armado, AL FONDO. Da igual
        // cuantas veces se suba la cadena si el achurado y la rejilla se dibujaron despues.
        // EJES va AL FINAL de la lista, y el sitio importa: las capas se van bajando UNA
        // POR UNA, así que la ÚLTIMA que se baja es la que queda más abajo de todas. Se
        // pidió que las líneas de los ejes quedaran en DRAW ORDER -> SEND TO BACK, o sea
        // debajo de la losa, del armado y de todo lo demás; por eso se baja de última.
        P("CAPAS_AL_FONDO", "LOSA,ARMADO LOSA,VOLADO,LOSACERO,EJES",
          "CAPAS AL FONDO, EN ORDEN; LA ULTIMA QUEDA ABAJO DE TODAS (EJES = Send to Back)"),
        P("PONER_SORTENTS_127", "SI", "SI = SORTENTS = 127 para que se respete el draw order"),

        // AÑADIDO: el respaldo del orden de dibujo. Si la tabla ACAD_SORTENTS no se deja
        // usar, se manda el DRAWORDER de verdad -el mismo que se usa a mano- por comando.
        P("DRAWORDER_POR_COMANDO", "SI",
          "SI = si la tabla de orden no se deja, se manda DRAWORDER -> Front por comando"),
        P("CADENA_SIN_TAPA", "SI", "SI = las cadenas sin tapadera en los extremos"),
        P("TRABE_SIN_TAPA", "SI", "SI = TODAS LAS TRABES SIN TAPADERA (solo las 2 lineas)"),
        P("ARMADO_PANO_PRIMERO", "SI", "SI = en las losas de borde el armado termina en el pano INTERIOR"),
        P("LOSA_TEXTO_1", "Losa de %U", "Renglon 1 (%U = AZOTEA / ENTREPISO)"),
        P("LOSA_TEXTO_2", "       cm de espesor", "Renglon 2 (%E = espesor real)"),
        P("LOSA_TEXTO_3", "Var. #      @               cm.", "Renglon 3"),
        P("LOSA_TEXTO_4", "Ambos sentidos", "Renglon 4"),

        // AÑADIDO: en la LOSA DE VOLADO el rótulo se queda SOLO con el armado. Se pidió que
        // ahí diga únicamente
        //         Var. #      @               cm.
        //         Ambos sentidos
        // o sea los renglones 3 y 4; los renglones 1 y 2 —"Losa de ..." y el espesor— no se
        // escriben. En las demás losas (ENTREPISO, AZOTEA, etc.) el rótulo sigue completo.
        // EN **NO**: se pidió que el rótulo del volado lleve TAMBIÉN el renglón del espesor,
        // en el segundo, y que la varilla baje al tercero. O sea los cuatro renglones, con el
        // primero suyo —«Losa de VOLADO»—:
        //
        //         Losa de VOLADO
        //                cm de espesor
        //         Var. #      @      cm.
        //         Ambos sentidos
        P("VOLADO_ROTULO_SOLO_ARMADO", "NO",
          "NO = el rotulo del volado lleva los CUATRO renglones, con el espesor en el 2o"),

        // EL PRIMER RENGLÓN DEL VOLADO. Se pidió que el nombre SÍ vaya, y en el primer
        // renglón: «Losa VOLADO», con el nombre que salga de la sección o de las notas de
        // ETABS. Es un renglón aparte del LOSA_TEXTO_1 de la macro —«Losa de %U»— porque ahí
        // se quiere sin el «de».
        P("VOLADO_TEXTO_1", "Losa de %U",
          "<<< PRIMER RENGLON DE LA LOSA DE VOLADO (%U = lo que diga su nota: VOLADO)"),
        P("LOSA_TEXTO_FONDO", "SI", "SI = hueco en el hatch atras del texto"),
        P("LOSA_TEXTO_COLOR", "0", "0 = el color de la capa"),
        P("LOSA_HATCH_OFFSET_CM", "0", "Cuanto se mete el contorno antes de achurar"),
        P("LOSA_HATCH_DEJAR_CONTORNO", "NO", "NO = se borra el contorno auxiliar"),
        P("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES", "Estilo de texto de los rotulos de seccion"),
        P("SEC_NOMBRE_FUENTE", "Bahnschrift", "Nombre de la fuente de ese estilo"),
        P("SEC_FUENTE", "bahnschrift.ttf", "Archivo de la fuente (respaldo)"),
        P("SEC_ALTURA", "0.12", "ALTURA DEL ESTILO TEXTO_SECCIONES (v35: 50% MAS GRANDE)"),
        P("SECCIONES_USAR_ESTILO", "SI", "SI = los rotulos de seccion usan ese estilo"),
        P("COLOR_TITULO", "7", "COLOR DE LA CAPA E-TITULO (7 = negro; 0 = negro real)"),
        P("ROTULO_CENTRADO", "SI", "SI = el rotulo va CENTRADO a la mitad de la planta"),
        P("ROTULO_TITULO", "PLANTA  ESTRUCTURAL", "Primer renglon del rotulo de la planta"),
        P("ROTULO_ALTURA_TITULO", "0.52", "ALTURA DE PLANTA  ESTRUCTURAL"),
        P("ROTULO_ALTURA_NIVEL", "0.26", "ALTURA DEL RENGLON DEL NIVEL"),
        P("ROTULO_ESTILO_TEXTO", "HAETTENSCHWEILER", "Estilo de texto del rotulo"),
        P("ROTULO_NOMBRE_FUENTE", "Haettenschweiler", "Nombre de la fuente del rotulo"),
        P("ROTULO_FUENTE", "hatten.ttf", "Archivo de la fuente (respaldo)"),
        P("ROTULO_ESCALA", "esc. 1/75", "Texto de escala que se pega al nivel"),
        P("ROTULO_LINEA", "SI", "SI = linea entre los dos renglones"),
        P("ROTULO_NIVELES",
          "PLANTA BAJA,PRIMER NIVEL,SEGUNDO NIVEL,TERCER NIVEL,CUARTO NIVEL," +
          "QUINTO NIVEL,SEXTO NIVEL,SEPTIMO NIVEL,OCTAVO NIVEL,NOVENO NIVEL," +
          "DECIMO NIVEL,DECIMO PRIMER NIVEL,DECIMO SEGUNDO NIVEL," +
          "DECIMO TERCER NIVEL,DECIMO CUARTO NIVEL,DECIMO QUINTO NIVEL",
          "Story1 = el primero de la lista, Story2 = el segundo, etc."),
        P("COLOR_CASTILLO", "1", "Color de E-CASTILLO (1 = rojo)"),
        P("COLOR_DALA", "12", "Color de E-DALA (12)"),
        P("COLOR_ACERO", "130", "<<< COLOR DE LA CAPA E-ACERO (130)"),
        P("ACERO_LINEA_BYLAYER", "SI", "<<< SI = LAS VIGAS DE ACERO CON TIPO DE LINEA BYLAYER (manda la capa)"),
        P("LINETYPE_ACERO", "Continuous", "<<< LAS LINEAS DE E-ACERO, CONTINUAS (se pidio asi)"),
        P("LINETYPE_TRABE", "PHANTOM2", "Tipo de linea de E-TRABE"),
        P("ESCALA_TIPOLINEA", "0", "0 = no tocar el LTSCALE"),
        P("TABLA_DE_SECCIONES", "SI", "SI = escribir la hoja SECCIONES"),
        P("TABLA_INCLUYE_LOSAS", "SI", "SI = incluir losas en la tabla"),
        P("USAR_MATRIZ_EJES", "SI", "SI = usar la matriz de ejes de ETABS"),
        P("ROTACION_SIGNO", "1", "Dejar en 1"),
        P("ROTACION_OFFSET_GRADOS", "0", "Dejar en 0"),
        P("GIRAR_SECCIONES_90", "NO", "DEJAR EN NO"),
        P("COLUMNAS_COMO_BLOQUE", "SI", "SI = insertar las secciones como BLOQUE"),
        P("BLOQUE_NOMBRE_SECCION", "SI", "SI = el bloque se llama IGUAL que la seccion de ETABS"),
        P("BLOQUE_PREFIJO", "", "Prefijo opcional del nombre del bloque (vacio = ninguno)"),
        P("DIBUJAR_EJES", "SI", "Dibujar la cuadricula con burbujas"),
        P("LINETYPE_EJES", "DASHDOT", "Tipo de linea de la capa E-EJES"),
        P("EJES_ESCALA_TIPOLINEA", "1", "LinetypeScale de las lineas de eje (1 = no tocar)"),
        P("EJES_SOBRESALEN", "1.15", "Solo se usa si EJES_INICIO_BURBUJA_M = 0"),
        P("RADIO_BURBUJA", "0.35", "Radio del circulo exterior"),
        P("BURBUJA_DOBLE", "SI", "SI = dos circulos concentricos"),
        P("BURBUJA_ANILLO", "0.82", "Radio interior / exterior"),
        P("BURBUJA_CRUZ", "SI", "SI = rayitas en cruz"),
        P("BURBUJA_CRUZ_LARGO", "0.9", "Largo de cada rayita"),
        P("ALTURA_TEXTO_BURBUJA", "0", "0 = automatica"),
        P("BURBUJAS", "AMBOS", "AMBOS, INICIO o FIN"),
        P("COLOR_EJES", "8", "Color de las lineas de eje (gris tenue)"),
        P("COLOR_BURBUJA_EJES", "4", "Color de los circulos (cian)"),
        P("COLOR_EJES_TEXTO", "6", "Color de los numeros de las burbujas"),
        P("BORRAR_ANTES", "SI", "Borrar lo dibujado antes con el prefijo"),
        P("DIBUJAR_EN_NUEVO_DIBUJO", "NO", "SI = crear un DWG nuevo"),
        P("VOLCAR_A_EXCEL", "SI", "Escribir la hoja MODELO_ETABS")
    };
}
