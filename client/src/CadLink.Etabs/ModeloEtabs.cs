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
    /// El <b>giro del eje local 2</b> de la sección, en grados. Solo en marcos.
    /// </summary>
    /// <remarks>
    /// Es el ángulo que devuelve <c>GetLocalAxes</c>, y es <b>lo que decide cómo se ve la
    /// columna en planta</b>: una columna de 20×60 girada 90° es una de 60×20. Sin este dato
    /// todas salían derechas y el plano no coincidía con el modelo.
    /// </remarks>
    public double AnguloGrados { get; set; }

    /// <summary>
    /// La etiqueta de <b>PIER</b> del muro: <c>M1</c>, <c>M2X</c>… Vacío si no tiene.
    /// </summary>
    /// <remarks>
    /// Es lo que la macro rotula en el muro —no el nombre de la propiedad, que es lo que
    /// llenaba la planta de «MURO TABICON 2 APLANADOS 15 CM» repetido—. Un muro sin pier
    /// asignado no se rotula, igual que allá.
    /// </remarks>
    public string Pier { get; set; } = string.Empty;

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

    /// <summary>
    /// Reparte los elementos en niveles <b>por su cota Z</b>, para SAP2000.
    /// </summary>
    /// <param name="tolM">
    /// Dos cotas más juntas que esto son el mismo nivel. 20 cm es lo razonable: un nudo
    /// modelado dos centímetros más abajo no es otro piso, y dos niveles reales nunca están a
    /// menos de un palmo.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>SAP2000 no tiene pisos</b>: los <i>stories</i> son un concepto de ETABS. Así que en
    /// SAP el modelo llegaba con todos los elementos en un solo nivel sin nombre, y el juego de
    /// plantas salía de una sola planta con el edificio entero encimado. Aquí los niveles se
    /// deducen de <b>la altura en Z</b>, que es como se leen esos modelos a mano.
    /// </para>
    /// <para>
    /// Cada elemento va al nivel de su <b>cota más alta</b>, y eso es lo que hace que
    /// coincida con ETABS: allá una columna que va del suelo al primer piso pertenece al
    /// piso de <i>arriba</i>, no al de abajo. Con la misma regla, las cadenas de desplante
    /// —que están abajo del todo— caen en su propio nivel, y ese es la <b>BASE</b>.
    /// </para>
    /// <para>
    /// Los nombres se ponen como los de ETABS —<c>BASE</c>, <c>N1</c>, <c>N2</c>…— y no con la
    /// cota, a propósito: así el rótulo de la planta sigue diciendo CIMENTACION, PLANTA BAJA y
    /// PRIMER NIVEL sin tener que tratar SAP como un caso aparte.
    /// </para>
    /// </remarks>
    public void NivelesDesdeZ(double tolM = 0.20)
    {
        if (Elementos.Count == 0)
        {
            return;
        }

        // La cota de cada elemento: la más alta que tenga.
        static double Cota(ElementoEtabs e) =>
            e.Vertices3D.Count > 0
                ? e.Vertices3D.Max(v => v.Z)
                : Math.Max(e.Z1, e.Z2);

        var cotas = new List<double>();

        foreach (var e in Elementos)
        {
            var z = Cota(e);

            if (!cotas.Any(c => Math.Abs(c - z) <= tolM))
            {
                cotas.Add(z);
            }
        }

        if (cotas.Count == 0)
        {
            return;
        }

        cotas.Sort();

        Niveles.Clear();

        for (var i = 0; i < cotas.Count; i++)
        {
            // El más bajo es la BASE —ahí están las cadenas de desplante—, pero solo si hay
            // más de uno: en un modelo de un solo nivel, ese nivel no es una cimentación.
            var nombre = i == 0 && cotas.Count > 1 ? "Base" : $"N{i}";

            Niveles.Add(new NivelEtabs
            {
                Nombre = nombre,
                ElevacionM = cotas[i],
                AlturaM = i == 0 ? 0 : cotas[i] - cotas[i - 1]
            });
        }

        foreach (var e in Elementos)
        {
            var z = Cota(e);

            var nivel = Niveles
                .OrderBy(n => Math.Abs(n.ElevacionM - z))
                .First();

            e.Story = nivel.Nombre;
        }

        Avisos.Add(
            $"El modelo no trae pisos —en SAP2000 es lo normal—, así que los {Niveles.Count} " +
            "niveles se deducen de la altura en Z: " +
            string.Join(", ", Niveles.Select(n => $"{n.Nombre} a {n.ElevacionM:0.00} m")) + ".");
    }

    /// <summary>
    /// ¿Esta cadena lleva debajo un <b>muro de piso a techo</b>?
    /// </summary>
    /// <param name="cadena">La cadena o dala de cerramiento.</param>
    /// <param name="cubre">
    /// Fracción de la cadena que tiene que llevar muro para darla por buena:
    /// <c>CADENA_SIN_MURO_CUBRE</c>, 0.5.
    /// </param>
    /// <param name="tolM">Holgura perpendicular y en Z, en metros.</param>
    /// <remarks>
    /// <para>
    /// Es <c>MarcarCadenasSinMuro</c>, y de esto depende cómo se dibuja: la cadena que
    /// <b>no</b> lleva su muro completo sale a trazos —<c>ACAD_ISO02W100</c>— y la que sí, con
    /// línea normal. Vive aquí y no en el dibujante porque hay que mirar <b>el nivel de
    /// abajo</b> del modelo, y el dibujante solo ve una planta.
    /// </para>
    /// <para>
    /// Un muro cuenta si está <b>debajo</b> de la cadena —su cota alta llega a la de la
    /// cadena—, si corre <b>a lo largo</b> de ella y si es de <b>piso a techo</b>: su altura
    /// tiene que ser al menos el 80 % de lo que hay entre los dos niveles. Un antepecho de
    /// ventana de 90 cm no sostiene una cadena de cerramiento, y por eso no vale.
    /// </para>
    /// </remarks>
    public bool MuroDePisoATechoBajo(
        ElementoEtabs cadena, double cubre = 0.5, double tolM = 0.25)
    {
        var dx = cadena.X2 - cadena.X1;
        var dy = cadena.Y2 - cadena.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-4)
        {
            return false;
        }

        var zCadena = Math.Max(cadena.Z1, cadena.Z2);

        // La altura del nivel de la cadena, para saber qué es «de piso a techo».
        var altura = Niveles
            .Where(n => Math.Abs(n.ElevacionM - zCadena) <= tolM)
            .Select(n => n.AlturaM)
            .FirstOrDefault();

        var ux = dx / largo;
        var uy = dy / largo;

        var tramos = new List<(double A, double B)>();

        foreach (var m in Elementos)
        {
            if (m.Clase != ClaseElemento.Muro)
            {
                continue;
            }

            var zAlta = m.Vertices3D.Count > 0
                ? m.Vertices3D.Max(v => v.Z)
                : Math.Max(m.Z1, m.Z2);

            var zBaja = m.Vertices3D.Count > 0
                ? m.Vertices3D.Min(v => v.Z)
                : Math.Min(m.Z1, m.Z2);

            // Debajo de la cadena, tocándola.
            if (Math.Abs(zAlta - zCadena) > tolM)
            {
                continue;
            }

            // De piso a techo: al menos el 80 % de la altura del nivel. Sin altura conocida
            // se acepta, que es lo prudente: es lo normal en un modelo sin pisos.
            if (altura > 0.1 && zAlta - zBaja < altura * 0.8)
            {
                continue;
            }

            // Y a lo largo de la cadena: se proyectan sus dos extremos y se comprueba que no
            // se salga de lado más de la holgura.
            var (a, da) = Proyecta(m.X1, m.Y1);
            var (b, db) = Proyecta(m.X2, m.Y2);

            if (da > tolM || db > tolM)
            {
                continue;
            }

            var t1 = Math.Max(0, Math.Min(a, b));
            var t2 = Math.Min(largo, Math.Max(a, b));

            if (t2 > t1)
            {
                tramos.Add((t1, t2));
            }
        }

        if (tramos.Count == 0)
        {
            return false;
        }

        // La UNIÓN, no la suma: dos muros que se traslapan en un nudo cubren su tramo una
        // sola vez, y sumándolos se pasaría del 100 %.
        tramos.Sort((p, q) => p.A.CompareTo(q.A));

        var unidos = new List<(double A, double B)> { tramos[0] };

        foreach (var t in tramos.Skip(1))
        {
            var ultimo = unidos[^1];

            if (t.A <= ultimo.B + 1e-9)
            {
                unidos[^1] = (ultimo.A, Math.Max(ultimo.B, t.B));
            }
            else
            {
                unidos.Add(t);
            }
        }

        return unidos.Sum(t => t.B - t.A) / largo >= cubre;

        (double T, double D) Proyecta(double x, double y)
        {
            var vx = x - cadena.X1;
            var vy = y - cadena.Y1;

            var t = (vx * ux) + (vy * uy);

            return (t, Math.Abs((vx * uy) - (vy * ux)));
        }
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
