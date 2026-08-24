namespace CadLink.Etabs;

/// <summary>Clasificación de un elemento leído del modelo.</summary>
public enum ClaseElemento
{
    Columna,
    Trabe,
    Diagonal,
    Muro,
    Losa
}

/// <summary>
/// Un elemento del modelo de ETABS, ya clasificado.
/// </summary>
/// <remarks>
/// La macro guarda esto en 56 arreglos paralelos con <c>ReDim Preserve</c>. Aquí es
/// una clase y una lista: desaparecen ~200 líneas de <c>ReDim</c> y con ellas el
/// riesgo de agregar un campo y olvidarlo en uno de los dos bloques, que es un
/// error que solo aparece cuando el modelo pasa de 2.000 elementos, es decir en el
/// proyecto grande del cliente y no en las pruebas.
/// </remarks>
public sealed class ElementoEtabs
{
    public ClaseElemento Clase { get; set; }

    /// <summary>Nivel al que pertenece.</summary>
    public string Story { get; set; } = string.Empty;

    /// <summary>Etiqueta de ETABS, o el nombre único si no tiene etiqueta.</summary>
    public string Etiqueta { get; set; } = string.Empty;

    /// <summary>Nombre de la sección o propiedad asignada.</summary>
    public string Seccion { get; set; } = string.Empty;

    /// <summary>
    /// Las <b>notas</b> de la propiedad de ETABS, con su material pegado detrás.
    /// </summary>
    /// <remarks>
    /// Es de donde la macro saca dos cosas que no están en ningún otro sitio: el
    /// <b>material del muro</b> —busca las palabras de <c>PALABRAS_MAMPOSTERIA</c> y
    /// <c>PALABRAS_CONCRETO</c> en las notas y en el nombre— y el <b>calibre</b> de la
    /// losacero, que es el último número de las notas.
    /// </remarks>
    public string Notas { get; set; } = string.Empty;

    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double Z1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double Z2 { get; set; }

    /// <summary>Ancho o espesor, en metros.</summary>
    public double AnchoM { get; set; }

    /// <summary>Peralte, en metros.</summary>
    public double PeralteM { get; set; }

    /// <summary>RECT, CIRC, I, TUBO, PIPE, C, T, L.</summary>
    public string Forma { get; set; } = "RECT";

    /// <summary>
    /// El <b>material</b> que la propiedad de ETABS o de SAP2000 tiene asignado:
    /// <c>CONC</c>, <c>A992Fy50</c>, <c>MAMPOSTERIA</c>, lo que sea.
    /// </summary>
    /// <remarks>
    /// Lo devuelve la misma llamada que da las medidas —<c>GetRectangle</c>,
    /// <c>GetISection</c>, <c>GetWall</c>…— en el parámetro <c>MatProp</c>, y antes se
    /// tiraba. Es el dato que hacía que la columna MATERIAL de la tabla de secciones
    /// saliera en blanco en todo menos en los muros de mampostería.
    /// </remarks>
    public string Material { get; set; } = string.Empty;

    /// <summary>Espesor del patín, en metros. Solo en perfiles I, C y T.</summary>
    /// <remarks>
    /// Hace falta para dibujar el perfil DE VERDAD. Con solo el ancho y el peralte lo
    /// único que se puede dibujar es una caja, que es lo que se veía antes.
    /// </remarks>
    public double PatinM { get; set; }

    /// <summary>Espesor del alma, en metros. Solo en perfiles I, C y T.</summary>
    public double AlmaM { get; set; }

    /// <summary>Espesor de pared, en metros. Solo en tubos y cajones.</summary>
    public double ParedM { get; set; }

    /// <summary>Vértices en planta, solo para losas y muros.</summary>
    public List<(double X, double Y)> Vertices { get; } = new();

    /// <summary>
    /// Vértices con su elevación, solo para losas y muros.
    /// </summary>
    /// <remarks>
    /// Se guarda además de <see cref="Vertices"/> porque en un muro la Z de cada
    /// vértice es imprescindible: proyectado solo en planta, un muro se aplasta
    /// contra una línea y en la vista 3D no se podría dibujar su paño.
    /// </remarks>
    public List<(double X, double Y, double Z)> Vertices3D { get; } = new();

    public double LargoM =>
        Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)) + ((Z2 - Z1) * (Z2 - Z1)));

    /// <summary>
    /// Área del paño en m², la de verdad: la del <b>plano del elemento</b>, no su
    /// proyección en planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se calcula con el método de Newell: la suma de los productos cruzados de los
    /// vértices consecutivos da un vector perpendicular al polígono cuya longitud es el
    /// doble del área. Sirve para las losas y para los muros con la misma fórmula, y eso
    /// es lo importante: un <b>muro es vertical</b>, así que su proyección en planta es una
    /// línea de área cero, y la fórmula del área en planta —la de siempre, con las X y las
    /// Y— daría 0 en los 31 muros del modelo.
    /// </para>
    /// <para>
    /// Vale para cualquier polígono plano, cóncavo incluido, y no depende de en qué orden
    /// vengan los vértices porque se toma el valor absoluto.
    /// </para>
    /// </remarks>
    public double AreaM2
    {
        get
        {
            if (Vertices3D.Count < 3)
            {
                return 0;
            }

            double nx = 0, ny = 0, nz = 0;

            for (var i = 0; i < Vertices3D.Count; i++)
            {
                var a = Vertices3D[i];
                var b = Vertices3D[(i + 1) % Vertices3D.Count];

                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }

            return Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz)) / 2;
        }
    }

    /// <summary>Descripción corta para mostrar en una cuadrícula.</summary>
    public string Dimensiones =>
        AnchoM > 0 && PeralteM > 0
            ? $"{PeralteM * 100:N0} x {AnchoM * 100:N0} cm"
            : AnchoM > 0
                ? $"e = {AnchoM * 100:N0} cm"
                : "—";
}

/// <summary>Un nivel del modelo.</summary>
public sealed class NivelEtabs
{
    public string Nombre { get; set; } = string.Empty;

    public double ElevacionM { get; set; }

    public double AlturaM { get; set; }
}

/// <summary>
/// Resultado completo de leer el modelo.
/// </summary>
public sealed class ModeloEtabs
{
    public string Programa { get; set; } = string.Empty;

    public string Archivo { get; set; } = string.Empty;

    public List<NivelEtabs> Niveles { get; } = new();

    public List<ElementoEtabs> Elementos { get; } = new();

    /// <summary>Avisos no fatales: cosas que no se pudieron leer.</summary>
    public List<string> Avisos { get; } = new();

    /// <summary>
    /// La <b>cuadrícula de ejes</b>, si el programa la dio; nulo si no se pudo leer.
    /// </summary>
    /// <remarks>
    /// En nulo <b>no</b> significa «sin ejes»: quien dibuja los deduce de las columnas y los
    /// muros con <see cref="EjesModelo.DesdeGeometria"/>, que es el respaldo de la macro.
    /// </remarks>
    public EjesModelo? Ejes { get; set; }

    public int Puntos { get; set; }

    public int Frames { get; set; }

    public int Areas { get; set; }

    public int Contar(ClaseElemento c) => Elementos.Count(e => e.Clase == c);

    /// <summary>
    /// Los niveles <b>que tienen elementos</b>, ordenados por su elevación.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el <c>StoriesDesdeElementos</c> + <c>OrdenarStories</c> de la macro, y hace falta
    /// por una razón concreta: <b>la BASE</b>. La lista de pisos que devuelve la API
    /// —<c>GetStories</c>— <b>no incluye el nivel base</b>, pero en el modelo sí hay
    /// elementos con <c>Story = "Base"</c>: las cadenas de desplante de la cimentación. Con
    /// la lista de la API a secas, esa planta no se dibujaba nunca.
    /// </para>
    /// <para>
    /// La elevación sale de la lista de la API cuando el nivel está ahí, y si no —la base—
    /// se toma la <b>Z más alta de sus elementos que no sean losa</b>, que es exactamente lo
    /// que hace la macro. Así la base queda en su sitio, debajo de todo.
    /// </para>
    /// <para>
    /// Los niveles que están en la lista de la API pero <b>sin un solo elemento</b> se
    /// quedan fuera: un hueco en la fila de plantas se ve como un error de dibujo.
    /// </para>
    /// </remarks>
    /// <param name="ascendente">
    /// <c>true</c> —el <c>ORDEN_NIVELES = ASC</c> de la hoja CONFIG— pone primero el nivel
    /// más bajo, o sea la cimentación.
    /// </param>
    public List<NivelEtabs> NivelesConElementos(bool ascendente = true)
    {
        var nombres = Elementos
            .Select(e => e.Story.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var salida = new List<NivelEtabs>();

        foreach (var nombre in nombres)
        {
            var dela = Niveles.FirstOrDefault(
                n => string.Equals(n.Nombre, nombre, StringComparison.OrdinalIgnoreCase));

            if (dela is not null)
            {
                salida.Add(dela);
                continue;
            }

            // No está en la lista de la API: es la BASE, o un nivel que la API no expuso.
            // Su cota es la Z más alta de sus elementos, sin contar las losas.
            var zs = Elementos
                .Where(e => string.Equals(e.Story.Trim(), nombre, StringComparison.OrdinalIgnoreCase)
                            && e.Clase != ClaseElemento.Losa)
                .Select(e => Math.Max(e.Z1, e.Z2))
                .ToList();

            salida.Add(new NivelEtabs
            {
                Nombre = nombre,
                ElevacionM = zs.Count > 0 ? zs.Max() : 0
            });
        }

        return ascendente
            ? salida.OrderBy(n => n.ElevacionM).ToList()
            : salida.OrderByDescending(n => n.ElevacionM).ToList();
    }

    /// <summary>Resumen para mostrarle al usuario.</summary>
    public string Resumen()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"Programa : {Programa}");
        s.AppendLine($"Modelo   : {Archivo}");
        s.AppendLine();
        s.AppendLine($"Se leyeron:      {Puntos} puntos, {Frames} frames, {Areas} áreas");
        s.AppendLine($"Niveles  : {Niveles.Count}");
        s.AppendLine();
        s.AppendLine($"  Columnas   : {Contar(ClaseElemento.Columna)}");
        s.AppendLine($"  Trabes     : {Contar(ClaseElemento.Trabe)}");
        s.AppendLine($"  Diagonales : {Contar(ClaseElemento.Diagonal)}");
        s.AppendLine($"  Muros      : {Contar(ClaseElemento.Muro)}");
        s.AppendLine($"  Losas      : {Contar(ClaseElemento.Losa)}");

        if (Avisos.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("Avisos:");
            foreach (var a in Avisos.Distinct().Take(12))
            {
                s.AppendLine("  - " + a);
            }
        }

        return s.ToString();
    }
}
