namespace CadLink.Etabs;

/// <summary>
/// La tabla de <b>secciones usadas en el modelo</b>: la hoja <c>SECCIONES</c> de la macro.
/// </summary>
/// <remarks>
/// <para>
/// Es el port de <c>VolcarSecciones</c>, con sus mismas diez columnas y su mismo orden:
/// primero por tipo de elemento —castillos, columnas, dalas, trabes, contratrabes,
/// diagonales, muros y losas— y dentro de cada tipo por nombre de sección.
/// </para>
/// <para>
/// Para qué sirve: es el <b>inventario</b> del modelo. Dice qué secciones de ETABS o de
/// SAP2000 se están usando de verdad, cuántas veces y en qué niveles, así que de un golpe
/// de vista se ve si quedó una sección de prueba suelta, si hay dos que hacen lo mismo o si
/// falta capturar una en las hojas de concreto y de acero.
/// </para>
/// <para>
/// Y de paso es la primera pieza de la <b>capa 2</b> del port —la clasificación—, que no
/// toca ni ETABS ni AutoCAD: <see cref="ClasificaTipo"/> es el <c>ClasificaTipo</c> de la
/// macro y <see cref="MaterialDeMuro"/> es su <c>MaterialDeMuro</c>.
/// </para>
/// </remarks>
public static class SeccionesModelo
{
    /// <summary>
    /// Los umbrales y las listas de palabras con que se clasifica. Son los de la hoja
    /// CONFIG, con sus valores de omisión.
    /// </summary>
    /// <param name="CastilloLadoMaxCm">
    /// <c>CASTILLO_LADO_MAX_CM</c>: una columna con los dos lados menores o iguales a esto
    /// es un CASTILLO.
    /// </param>
    /// <param name="DalaPeralteMaxCm">
    /// <c>DALA_PERALTE_MAX_CM</c>: una trabe con peralte menor o igual a esto es una DALA.
    /// </param>
    /// <param name="PalabrasMamposteria">
    /// <c>PALABRAS_MAMPOSTERIA</c>: si alguna aparece en las notas o en el nombre del
    /// muro, el muro es de mampostería.
    /// </param>
    /// <param name="PalabrasConcreto"><c>PALABRAS_CONCRETO</c>, igual.</param>
    /// <param name="IncluyeLosas"><c>TABLA_INCLUYE_LOSAS</c>.</param>
    public sealed record Opciones(
        double CastilloLadoMaxCm = 20,
        double DalaPeralteMaxCm = 25,
        string PalabrasMamposteria =
            "TABIQUE,TABICON,BLOCK,BLOQUE,MAMPOSTERIA,LADRILLO,ADOBE",
        string PalabrasConcreto = "CONCRETO,CONCRETE,C.A.,REFORZADO",
        bool IncluyeLosas = true);

    /// <summary>Un renglón de la tabla, con las columnas de la hoja SECCIONES.</summary>
    public sealed class Fila
    {
        /// <summary>CASTILLO, COLUMNA, DALA, TRABE, CONTRATRABE, DIAGONAL, MURO o LOSA.</summary>
        public string Tipo { get; set; } = string.Empty;

        /// <summary>El nombre de la sección tal como está en el modelo.</summary>
        public string Seccion { get; set; } = string.Empty;

        /// <summary>RECT, CIRC, I, TUBO, PIPE, C, T, L, AREA; con «(ACERO)» si es perfil.</summary>
        public string Forma { get; set; } = string.Empty;

        /// <summary>MAMPOSTERIA o CONCRETO en los muros; en blanco si no se sabe.</summary>
        public string Material { get; set; } = string.Empty;

        /// <summary>T3, el peralte, en centímetros. En muros y losas va en blanco.</summary>
        public double? PeralteCm { get; set; }

        /// <summary>T2: el ancho, o el espesor si es muro o losa, en centímetros.</summary>
        public double? AnchoCm { get; set; }

        /// <summary>Espesor del patín en centímetros, solo en perfiles.</summary>
        public double? PatinCm { get; set; }

        /// <summary>Espesor del alma en centímetros, solo en perfiles.</summary>
        public double? AlmaCm { get; set; }

        /// <summary>Cuántos elementos del modelo la usan.</summary>
        public int Cantidad { get; set; }

        /// <summary>
        /// Longitud TOTAL de los elementos tipo <b>frame</b> con esa sección, en metros.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Es la suma del largo REAL de cada barra —en tres dimensiones—, no de su
        /// proyección en planta: en una diagonal o en una rampa las dos cosas no son lo
        /// mismo, y lo que se compra es el largo real.
        /// </para>
        /// <para>
        /// En muros y losas va en blanco: ahí lo que se mide es el área.
        /// </para>
        /// </remarks>
        public double? LongitudTotalM { get; set; }

        /// <summary>
        /// Área TOTAL de los <b>shell</b> —muros y losas— con esa propiedad, en m².
        /// </summary>
        /// <remarks>
        /// Es el área del paño de verdad, la del plano del elemento. En un muro, que es
        /// vertical, el área en planta sería cero: ver <c>ElementoEtabs.AreaM2</c>.
        /// </remarks>
        public double? AreaTotalM2 { get; set; }

        /// <summary>En qué niveles aparece, separados por coma.</summary>
        public string Niveles { get; set; } = string.Empty;
    }

    /// <summary>Arma la tabla a partir del modelo leído.</summary>
    public static List<Fila> Construir(ModeloEtabs modelo, Opciones? op = null)
    {
        op ??= new Opciones();

        var filas = new List<Fila>();
        var niveles = new Dictionary<Fila, List<string>>();

        foreach (var e in modelo.Elementos)
        {
            var esMuroOLosa = e.Clase is ClaseElemento.Muro or ClaseElemento.Losa;

            // T2 y T3 tal como los devolvió la API. En la columna van al revés que en la
            // viga, y el lector ya los guardó así; aquí se deshace ese cambio para que la
            // tabla diga lo mismo que la de la macro.
            var t2 = e.Clase == ClaseElemento.Columna ? e.PeralteM : e.AnchoM;
            var t3 = e.Clase == ClaseElemento.Columna ? e.AnchoM : e.PeralteM;

            var tipo = ClasificaTipo(e.Clase, e.Seccion, t2, t3, op);
            if (tipo == "LOSA" && !op.IncluyeLosas)
            {
                continue;
            }

            var seccion = e.Seccion.Trim();
            if (seccion.Length == 0)
            {
                seccion = "(sin nombre)";
            }

            var fila = filas.FirstOrDefault(
                f => f.Tipo == tipo && string.Equals(f.Seccion, seccion, StringComparison.OrdinalIgnoreCase));

            if (fila is null)
            {
                fila = new Fila
                {
                    Tipo = tipo,
                    Seccion = seccion,
                    Forma = e.Forma + (EsPerfilAcero(e.Forma) ? " (ACERO)" : string.Empty),
                    Material = Material(e, op),

                    // En muros y losas no hay peralte: lo que se reporta es el ESPESOR, y
                    // va en la columna del ancho. Es como lo escribe la macro.
                    PeralteCm = esMuroOLosa ? null : Cm(t3),
                    AnchoCm = esMuroOLosa ? Cm(e.AnchoM) : Cm(t2),
                    PatinCm = Cm(e.PatinM),
                    AlmaCm = Cm(e.AlmaM)
                };

                filas.Add(fila);
                niveles[fila] = new List<string>();
            }

            fila.Cantidad++;

            // Los frames suman LARGO y los shell suman AREA. Un elemento no puede aportar
            // a las dos: o es una barra o es un paño.
            if (esMuroOLosa)
            {
                fila.AreaTotalM2 = Math.Round((fila.AreaTotalM2 ?? 0) + e.AreaM2, 3);
            }
            else
            {
                fila.LongitudTotalM = Math.Round((fila.LongitudTotalM ?? 0) + e.LargoM, 3);
            }

            var story = e.Story.Trim();
            if (story.Length > 0 && !niveles[fila].Contains(story, StringComparer.OrdinalIgnoreCase))
            {
                niveles[fila].Add(story);
            }
        }

        foreach (var f in filas)
        {
            f.Niveles = string.Join(",", niveles[f]);
        }

        // El orden de la macro: por tipo de elemento y, dentro del tipo, por nombre.
        return filas
            .OrderBy(f => OrdenDeTipo(f.Tipo))
            .ThenBy(f => f.Seccion, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// El material que se muestra: el <b>nombre del material</b> del modelo y, en los
    /// muros, además de qué están hechos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes esta columna solo se llenaba en los muros de mampostería, y en todo lo demás
    /// salía en blanco aunque el modelo <b>sí</b> trae el dato: el material de la propiedad
    /// —CONC, A992Fy50— lo devuelve la misma llamada que da las medidas, y se estaba
    /// tirando.
    /// </para>
    /// <para>
    /// En un muro se muestran las dos cosas cuando no dicen lo mismo:
    /// <c>MAMPOSTERIA (MUR-TABICON)</c>. La clasificación de la macro es la que manda para
    /// dibujar, y el nombre del material es el que permite comprobarla contra el modelo.
    /// </para>
    /// </remarks>
    private static string Material(ElementoEtabs e, Opciones op)
    {
        var delModelo = e.Material.Trim();

        if (e.Clase != ClaseElemento.Muro)
        {
            return delModelo;
        }

        var clasificado = MaterialDeMuro(e.Seccion, e.Notas, op);

        if (clasificado.Length == 0)
        {
            return delModelo;
        }

        if (delModelo.Length == 0 ||
            EtabsReader.Normalizar(delModelo) == EtabsReader.Normalizar(clasificado))
        {
            return clasificado;
        }

        return $"{clasificado} ({delModelo})";
    }

    /// <summary>Centímetros con un decimal, o <c>null</c> si no hay dato.</summary>
    private static double? Cm(double metros) =>
        metros > 0 ? Math.Round(metros * 100, 2) : null;

    /// <summary>
    /// CASTILLO / COLUMNA, DALA / TRABE / CONTRATRABE, DIAGONAL, MURO o LOSA. Es el
    /// <c>ClasificaTipo</c> de la macro.
    /// </summary>
    /// <remarks>
    /// El orden de las preguntas importa y es el suyo: primero lo que diga el
    /// <b>nombre</b> de la sección —una sección que se llama CASTILLO es un castillo mida
    /// lo que mida— y solo después la medida. Así una columna de 25×25 que el ingeniero
    /// llamó «CASTILLO ESQUINA» no se cuenta como columna.
    /// </remarks>
    public static string ClasificaTipo(
        ClaseElemento clase, string seccion, double anchoT2M, double peralteT3M,
        Opciones? op = null)
    {
        op ??= new Opciones();
        var t = EtabsReader.Normalizar(seccion);

        if (clase == ClaseElemento.Columna)
        {
            if (t.Contains("CASTILLO", StringComparison.Ordinal) ||
                t.Contains("CAST", StringComparison.Ordinal))
            {
                return "CASTILLO";
            }

            var lado = op.CastilloLadoMaxCm / 100;
            return anchoT2M > 0 && peralteT3M > 0 && anchoT2M <= lado && peralteT3M <= lado
                ? "CASTILLO"
                : "COLUMNA";
        }

        if (clase == ClaseElemento.Trabe)
        {
            if (t.Contains("CONTRATRABE", StringComparison.Ordinal))
            {
                return "CONTRATRABE";
            }

            if (t.Contains("DALA", StringComparison.Ordinal) ||
                t.Contains("CERRAMIENTO", StringComparison.Ordinal))
            {
                return "DALA";
            }

            return peralteT3M > 0 && peralteT3M <= op.DalaPeralteMaxCm / 100 ? "DALA" : "TRABE";
        }

        return clase switch
        {
            ClaseElemento.Diagonal => "DIAGONAL",
            ClaseElemento.Muro => "MURO",
            _ => "LOSA"
        };
    }

    /// <summary>
    /// MAMPOSTERIA o CONCRETO por las palabras de las notas y del nombre, o cadena vacía
    /// si el modelo no lo dice. Es el <c>MaterialDeMuro</c> de la macro.
    /// </summary>
    /// <remarks>
    /// La mampostería se pregunta <b>primero</b>, como allá: una propiedad que se llame
    /// «MURO TABICON CONFINADO CON CASTILLOS DE CONCRETO» trae las dos palabras, y lo que
    /// manda es de qué está hecho el muro.
    /// </remarks>
    public static string MaterialDeMuro(string seccion, string notas, Opciones? op = null)
    {
        op ??= new Opciones();
        var t = EtabsReader.Normalizar(notas + " " + seccion);

        if (t.Length == 0)
        {
            return string.Empty;
        }

        if (Alguna(t, op.PalabrasMamposteria))
        {
            return "MAMPOSTERIA";
        }

        return Alguna(t, op.PalabrasConcreto) ? "CONCRETO" : string.Empty;
    }

    private static bool Alguna(string textoNormalizado, string lista)
    {
        foreach (var pieza in lista.Split(','))
        {
            var p = EtabsReader.Normalizar(pieza);
            if (p.Length > 0 && textoNormalizado.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>¿Es un perfil de acero? Es el <c>EsPerfilAcero</c> de la macro.</summary>
    /// <remarks>
    /// El <c>CAJON</c> entra también: en la macro es la forma <c>TUBO</c> —el
    /// <c>GetTube</c>— y va a la capa del acero como cualquier otro perfil.
    /// </remarks>
    public static bool EsPerfilAcero(string forma) =>
        forma is "I" or "TUBO" or "CAJON" or "PIPE" or "C" or "T" or "L";

    /// <summary>El orden en que la macro ordena los tipos en la hoja SECCIONES.</summary>
    public static int OrdenDeTipo(string tipo) => tipo switch
    {
        "CASTILLO" => 1,
        "COLUMNA" => 2,
        "DALA" => 3,
        "TRABE" => 4,
        "CONTRATRABE" => 5,
        "DIAGONAL" => 6,
        "MURO" => 7,
        "LOSA" => 8,
        _ => 9
    };
}
