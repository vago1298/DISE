using System.Runtime.InteropServices;

namespace CadLink.Etabs;

/// <summary>
/// Lee el modelo abierto en ETABS: niveles, puntos, frames y áreas.
/// </summary>
/// <remarks>
/// <para>
/// Es el port de <c>LeerModelo</c> de la macro. Se conservan el orden de lectura y
/// la clasificación, pero con dos diferencias importantes:
/// </para>
/// <list type="number">
///   <item>
///     Cada fallo se <b>registra</b> en <see cref="ModeloEtabs.Avisos"/> en lugar
///     de tragarse con <c>On Error Resume Next</c>. La macro documenta ella misma
///     el síntoma de esa práctica: <i>"ahí era donde algunas secciones se quedaban
///     con el color de la capa"</i>.
///   </item>
///   <item>
///     Si a un frame le falta un punto extremo, <b>se reporta y se descarta</b>.
///     La macro dejaba ese extremo en (0,0,0), o sea en el origen del modelo, lo
///     que dibuja una línea que cruza toda la planta hacia la esquina sin ningún
///     aviso.
///   </item>
/// </list>
/// </remarks>
public static class EtabsReader
{
    /// <summary>Tolerancia para decidir si un frame es vertical u horizontal, en metros.</summary>
    private const double Tol = 0.001;

    public static ModeloEtabs Leer(EtabsConnection cx)
    {
        var m = new ModeloEtabs
        {
            Programa = cx.Programa,
            Archivo = cx.Modelo
        };

        // Se limpia para que la bitácora sea la de ESTA lectura y no arrastre la
        // anterior, que confundiría más de lo que ayuda.
        Com.Bitacora.Clear();

        var puntos = LeerPuntos(cx, m);
        LeerNiveles(cx, m);
        LeerFrames(cx, m, puntos);
        LeerAreas(cx, m, puntos);

        // El detalle REAL de cada miembro se adjunta siempre que algo saliera mal.
        // Los avisos por sí solos ("no se pudieron leer los puntos") no distinguen
        // un ETABS sin modelo de un miembro que no se encuentra, y esa diferencia es
        // justo la que hace falta para arreglarlo.
        var nadaLeido = m.Puntos == 0 && m.Frames == 0 && m.Areas == 0;

        if (m.Avisos.Count > 0 || nadaLeido)
        {
            if (nadaLeido)
            {
                m.Avisos.Add(
                    "ETABS entregó el modelo pero no se pudo leer NADA de él. Abajo, " +
                    "por qué falló cada miembro.");
            }

            m.Avisos.Add("--- Detalle por miembro ---");

            foreach (var linea in Com.Bitacora)
            {
                m.Avisos.Add(linea);
            }

            if (!string.IsNullOrEmpty(EtabsAssembly.RutaCargada))
            {
                m.Avisos.Add("Librería usada: " + EtabsAssembly.RutaCargada);
            }
            else
            {
                m.Avisos.Add(
                    "NO se cargó ETABSv1.dll. Sin ella no hay forma de llegar a los " +
                    "miembros del modelo: el envoltorio COM no los expone.");
            }
        }

        return m;
    }

    // ==================================================================
    // Puntos
    // ==================================================================

    private static Dictionary<string, (double X, double Y, double Z)> LeerPuntos(
        EtabsConnection cx, ModeloEtabs m)
    {
        var puntos = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);

        try
        {
            var pointObj = Com.Get(cx.SapModel, "PointObj");

            object?[] a = { 0, null };
            Com.Call(pointObj, "GetNameList", a, 0, 1);

            var nombres = Com.AsStrings(a[1]);
            m.Puntos = nombres.Length;

            foreach (var nombre in nombres)
            {
                object?[ ] c = { nombre, 0d, 0d, 0d };
                try
                {
                    Com.Call(pointObj, "GetCoordCartesian", c, 1, 2, 3);
                    puntos[nombre] = (
                        Convert.ToDouble(c[1]),
                        Convert.ToDouble(c[2]),
                        Convert.ToDouble(c[3]));
                }
                catch (Exception ex) when (EsFalloCom(ex))
                {
                    m.Avisos.Add($"No se pudo leer la coordenada del punto '{nombre}'.");
                }
            }
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudieron leer los puntos del modelo.");
        }

        return puntos;
    }

    // ==================================================================
    // Niveles
    // ==================================================================

    private static void LeerNiveles(EtabsConnection cx, ModeloEtabs m)
    {
        object? story = Com.TryGet(cx.SapModel, "Story");
        if (story is null)
        {
            // SAP2000 NO tiene pisos: es lo normal, no un defecto de version.
            m.Avisos.Add(
                "El modelo no expone el objeto Story, así que no hay niveles. En "
                + "SAP2000 es lo normal: los pisos son un concepto de ETABS.");
            return;
        }

        // Primero GetStories_2, que trae la elevación de la base.
        try
        {
            object?[] a = { 0d, 0, null, null, null, null, null, null, null, null };
            if (Com.CallRet(story, "GetStories_2", a, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9) == 0)
            {
                Agregar(m, Com.AsStrings(a[2]), Com.AsDoubles(a[3]), Com.AsDoubles(a[4]));
                return;
            }
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            // Esta versión no tiene GetStories_2. Se prueba la anterior.
        }

        try
        {
            object?[] a = { 0, null, null, null, null, null, null, null };
            if (Com.CallRet(story, "GetStories", a, 0, 1, 2, 3, 4, 5, 6, 7) == 0)
            {
                Agregar(m, Com.AsStrings(a[1]), Com.AsDoubles(a[2]), Com.AsDoubles(a[3]));
                return;
            }
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudieron leer los niveles del modelo.");
        }

        static void Agregar(ModeloEtabs m, string[] nombres, double[] elev, double[] alt)
        {
            for (var i = 0; i < nombres.Length; i++)
            {
                m.Niveles.Add(new NivelEtabs
                {
                    Nombre = nombres[i],
                    ElevacionM = i < elev.Length ? elev[i] : 0,
                    AlturaM = i < alt.Length ? alt[i] : 0
                });
            }
        }
    }

    // ==================================================================
    // Frames: columnas, trabes y diagonales
    // ==================================================================

    private static void LeerFrames(
        EtabsConnection cx, ModeloEtabs m,
        Dictionary<string, (double X, double Y, double Z)> puntos)
    {
        object frameObj;
        object? propFrame;

        try
        {
            frameObj = Com.Get(cx.SapModel, "FrameObj");
            propFrame = Com.TryGet(cx.SapModel, "PropFrame");
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo acceder a los frames del modelo.");
            return;
        }

        string[] nombres, etiquetas, niveles;

        try
        {
            (nombres, etiquetas, niveles) = ListaDeNombres(frameObj, m, "frames");
            m.Frames = nombres.Length;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo obtener la lista de frames.");
            return;
        }

        var cacheSecciones = new Dictionary<string, Dims>(
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < nombres.Length; i++)
        {
            var nombre = nombres[i];

            string p1 = string.Empty, p2 = string.Empty, seccion = string.Empty;

            try
            {
                object?[] a = { nombre, string.Empty, string.Empty };
                Com.Call(frameObj, "GetPoints", a, 1, 2);
                p1 = a[1]?.ToString() ?? string.Empty;
                p2 = a[2]?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                m.Avisos.Add($"Frame '{nombre}': no se pudieron leer sus extremos.");
                continue;
            }

            // AQUI ESTA LA CORRECCION al bug de la macro: se exigen LOS DOS
            // extremos. Si falta uno, el elemento se descarta y se avisa, en
            // lugar de dejarlo apuntando al origen del modelo.
            if (!puntos.TryGetValue(p1, out var c1) || !puntos.TryGetValue(p2, out var c2))
            {
                var etiqueta = i < etiquetas.Length && etiquetas[i].Length > 0 ? etiquetas[i] : nombre;
                m.Avisos.Add(
                    $"Frame '{etiqueta}' descartado: falta la coordenada de un extremo. " +
                    "En la macro este caso dibujaba una línea hacia el origen.");
                continue;
            }

            try
            {
                object?[] a = { nombre, string.Empty, string.Empty };
                Com.Call(frameObj, "GetSection", a, 1, 2);
                seccion = a[1]?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                // Sin sección no se puede dimensionar, pero el elemento sí existe.
            }

            var e = new ElementoEtabs
            {
                Story = i < niveles.Length ? niveles[i] : string.Empty,
                Etiqueta = i < etiquetas.Length && etiquetas[i].Length > 0 ? etiquetas[i] : nombre,
                Seccion = seccion,
                X1 = c1.X, Y1 = c1.Y, Z1 = c1.Z,
                X2 = c2.X, Y2 = c2.Y, Z2 = c2.Z
            };

            // Misma clasificación de la macro
            if (Math.Abs(e.X1 - e.X2) < Tol && Math.Abs(e.Y1 - e.Y2) < Tol)
            {
                e.Clase = ClaseElemento.Columna;
            }
            else if (Math.Abs(e.Z1 - e.Z2) < Tol)
            {
                e.Clase = ClaseElemento.Trabe;
            }
            else
            {
                e.Clase = ClaseElemento.Diagonal;
            }

            if (propFrame is not null && seccion.Length > 0)
            {
                var dims = DimensionesSeccion(propFrame, seccion, cacheSecciones, m);
                e.Forma = dims.Forma;
                e.Material = dims.Material;

                // Los espesores, que son lo que permite dibujar el perfil de verdad en
                // lugar de una caja.
                e.PatinM = dims.Patin;
                e.AlmaM = dims.Alma;
                e.ParedM = dims.Pared;

                // En la columna el ancho se mide sobre el eje 3, al contrario que
                // en la viga. Es la misma regla de la macro.
                if (e.Clase == ClaseElemento.Columna)
                {
                    e.AnchoM = dims.T3;
                    e.PeralteM = dims.T2;
                }
                else
                {
                    e.AnchoM = dims.T2;
                    e.PeralteM = dims.T3;
                }
            }

            m.Elementos.Add(e);
        }
    }

    /// <summary>
    /// Dimensiones de una sección, probando cada forma en cascada, igual que
    /// <c>DimsDeSeccion</c> de la macro.
    /// </summary>
    private static Dims DimensionesSeccion(
        object propFrame, string seccion,
        Dictionary<string, Dims> cache,
        ModeloEtabs m)
    {
        if (cache.TryGetValue(seccion, out var guardada))
        {
            return guardada;
        }

        // Se PREGUNTA la forma en vez de tantear. Antes se probaba rectángulo, círculo y
        // perfil I por turnos, y todo lo demás —ángulos, tubos, canales, que es de lo que
        // está hecha una armadura metálica— caía al respaldo y salía como caja.
        var r = PorForma(propFrame, seccion) ?? new Dims(0, 0, "RECT", 0, 0, 0);

        if (r.T2 == 0 && r.T3 == 0)
        {
            m.Avisos.Add($"Sin dimensiones para la sección '{seccion}'.");
        }

        cache[seccion] = r;
        return r;
    }

    private static Dims? LeerRectangulo(object propFrame, string seccion)
    {
        try
        {
            object?[] a = { seccion, string.Empty, string.Empty, 0d, 0d, 0, string.Empty, string.Empty };
            if (Com.CallRet(propFrame, "GetRectangle", a, 1, 2, 3, 4, 5, 6, 7) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);
            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "RECT", 0, 0, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    private static Dims? LeerCirculo(object propFrame, string seccion)
    {
        try
        {
            object?[] a = { seccion, string.Empty, string.Empty, 0d, 0, string.Empty, string.Empty };
            if (Com.CallRet(propFrame, "GetCircle", a, 1, 2, 3, 4, 5, 6) != 0)
            {
                return null;
            }

            var d = Convert.ToDouble(a[3]);
            return d > 0 ? new Dims(d, d, "CIRC", 0, 0, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    private static Dims? LeerPerfilI(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty,
                0d, 0d, 0d, 0d, 0d, 0d,
                0, string.Empty, string.Empty
            };

            if (Com.CallRet(propFrame, "GetISection", a, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);

            // a[5] y a[6] son el patin y el alma: ya venian por referencia, solo que no
            // se guardaban, y son justo lo que hace falta para dibujar la I.
            var tf = Convert.ToDouble(a[5]);
            var tw = Convert.ToDouble(a[6]);

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "I", tf, tw, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    // ==================================================================
    // Áreas: muros y losas
    // ==================================================================

    private static void LeerAreas(
        EtabsConnection cx, ModeloEtabs m,
        Dictionary<string, (double X, double Y, double Z)> puntos)
    {
        object areaObj;
        object? propArea;

        try
        {
            areaObj = Com.Get(cx.SapModel, "AreaObj");
            propArea = Com.TryGet(cx.SapModel, "PropArea");
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo acceder a las áreas del modelo.");
            return;
        }

        string[] nombres, etiquetas, niveles;

        try
        {
            (nombres, etiquetas, niveles) = ListaDeNombres(areaObj, m, "áreas");
            m.Areas = nombres.Length;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo obtener la lista de áreas.");
            return;
        }

        var cachePropiedad =
            new Dictionary<string, (double EspesorM, string Notas, string Material)>(
                StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < nombres.Length; i++)
        {
            var nombre = nombres[i];
            string[] vertices;

            try
            {
                object?[] a = { nombre, 0, null };
                Com.Call(areaObj, "GetPoints", a, 1, 2);
                vertices = Com.AsStrings(a[2]);
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                m.Avisos.Add($"Área '{nombre}': no se pudieron leer sus vértices.");
                continue;
            }

            if (vertices.Length < 3)
            {
                continue;
            }

            var coords = new List<(double X, double Y, double Z)>();
            var faltan = false;
            foreach (var v in vertices)
            {
                if (puntos.TryGetValue(v, out var c))
                {
                    coords.Add(c);
                }
                else
                {
                    faltan = true;
                }
            }

            if (faltan || coords.Count < 3)
            {
                m.Avisos.Add($"Área '{nombre}' descartada: le faltan coordenadas de vértices.");
                continue;
            }

            var seccion = string.Empty;
            try
            {
                object?[] a = { nombre, string.Empty };
                Com.Call(areaObj, "GetProperty", a, 1);
                seccion = a[1]?.ToString() ?? string.Empty;
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                // El área existe aunque no se sepa su propiedad.
            }

            var zMin = coords.Min(c => c.Z);
            var zMax = coords.Max(c => c.Z);
            var esVertical = zMax - zMin > 0.05;   // mismo criterio de la macro

            var e = new ElementoEtabs
            {
                Clase = esVertical ? ClaseElemento.Muro : ClaseElemento.Losa,
                Story = i < niveles.Length ? niveles[i] : string.Empty,
                Etiqueta = i < etiquetas.Length && etiquetas[i].Length > 0 ? etiquetas[i] : nombre,
                Seccion = seccion,
                Forma = "AREA"
            };

            foreach (var c in coords)
            {
                e.Vertices.Add((c.X, c.Y));
                e.Vertices3D.Add((c.X, c.Y, c.Z));
            }

            if (esVertical)
            {
                // Los dos vértices más separados en planta definen la línea del muro
                var (ia, ib) = MasSeparados(coords);
                e.X1 = coords[ia].X; e.Y1 = coords[ia].Y; e.Z1 = coords[ia].Z;
                e.X2 = coords[ib].X; e.Y2 = coords[ib].Y; e.Z2 = coords[ib].Z;
            }
            else
            {
                e.X1 = coords.Min(c => c.X); e.Y1 = coords.Min(c => c.Y);
                e.X2 = coords.Max(c => c.X); e.Y2 = coords.Max(c => c.Y);
                e.Z1 = zMin; e.Z2 = zMax;
            }

            if (propArea is not null && seccion.Length > 0)
            {
                var prop = Propiedad(propArea, seccion, esVertical, cachePropiedad);
                e.AnchoM = prop.EspesorM;
                e.Notas = prop.Notas;
                e.Material = prop.Material;
            }

            m.Elementos.Add(e);
        }
    }

    /// <summary>
    /// El <b>espesor y las notas</b> de una propiedad de área, como los lee la macro en
    /// <c>PropiedadDeMuro</c> y <c>PropiedadDeLosa</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las <b>notas</b> hacen falta y no son un extra: son de donde la macro saca el
    /// material del muro —las palabras de <c>PALABRAS_MAMPOSTERIA</c> y
    /// <c>PALABRAS_CONCRETO</c> se buscan en las notas y en el nombre— y el <b>calibre</b>
    /// de la losacero. Sin ellas, un muro de tabicón no se puede distinguir de uno de
    /// concreto.
    /// </para>
    /// <para>
    /// <b>Y si la API no da el espesor</b>, se saca del NOMBRE, que es lo que hace la
    /// macro con <c>DimsDesdeNombre</c>: en una propiedad que se llama «MURO 20 CM» el
    /// espesor está a la vista. Antes se caía directo al valor de omisión y en un modelo
    /// con 31 muros salían 31 avisos.
    /// </para>
    /// </remarks>
    private static (double EspesorM, string Notas, string Material) Propiedad(
        object propArea, string seccion, bool esMuro,
        Dictionary<string, (double EspesorM, string Notas, string Material)> cache)
    {
        if (cache.TryGetValue(seccion, out var ya))
        {
            return ya;
        }

        var metodo = esMuro ? "GetWall" : "GetSlab";
        var valor = 0d;
        var notas = string.Empty;
        var material = string.Empty;

        try
        {
            object?[] a = { seccion, 0, 0, string.Empty, 0d, 0, string.Empty, string.Empty };
            if (Com.CallRet(propArea, metodo, a, 1, 2, 3, 4, 5, 6, 7) == 0)
            {
                valor = Convert.ToDouble(a[4]);

                // Aquí el MatProp va en la posición 3, no en la 2: la firma de GetWall y
                // GetSlab lleva antes el tipo y el comportamiento del shell.
                material = (a[3]?.ToString() ?? string.Empty).Trim();

                // notas + material, como los junta la macro: nts & " " & mat
                notas = ((a[6]?.ToString() ?? string.Empty) + " " + material).Trim();
            }
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            valor = 0;
        }

        // El respaldo de la macro: el espesor que traiga el NOMBRE de la propiedad, y solo
        // si sale un valor con sentido —menos de un metro—. Si no, se queda en 0 y el
        // dibujante aplica ESPESOR_MURO_CM.
        if (valor <= 0)
        {
            var delNombre = EspesorDesdeNombre(seccion);
            if (delNombre > 0 && delNombre < 1)
            {
                valor = delNombre;
            }
        }

        var r = (valor, notas, material);
        cache[seccion] = r;
        return r;
    }

    /// <summary>
    /// El espesor que trae el <b>nombre</b> de la propiedad, en metros. Es el
    /// <c>DimsDesdeNombre</c> de la macro.
    /// </summary>
    /// <remarks>
    /// Con la misma cuenta, hasta en lo raro: si el nombre trae una <c>X</c> se toman los
    /// dos números que la rodean —<c>30X60</c>—, y si no, <b>todas</b> las cifras del texto
    /// seguidas, y el resultado se divide entre 100. Así «MURO 20 CM» da 0.20, y
    /// «MURO TABICON 2 APLANADOS 15 CM» da 2.15, que al pasar del metro se descarta y deja
    /// el valor de omisión. Suena tosco y lo es, pero es <b>exactamente</b> lo que hace la
    /// macro, y cambiarlo aquí haría que el plano saliera distinto del suyo.
    /// </remarks>
    public static double EspesorDesdeNombre(string nombre)
    {
        var t = Normalizar(nombre);
        var x = t.IndexOf('X', StringComparison.Ordinal);

        if (x < 1)
        {
            var todas = new string(t.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
            return Valor(todas) / 100;
        }

        var izq = string.Empty;
        for (var i = x - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(t[i]) && t[i] != '.')
            {
                break;
            }

            izq = t[i] + izq;
        }

        return Valor(izq) / 100;

        static double Valor(string s) =>
            double.TryParse(s.Trim('.'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : 0;
    }

    /// <summary>
    /// Deja el texto en mayúsculas, sin acentos y solo con letras, cifras y punto. Es el
    /// <c>Norm</c> de la macro, y es la base de todas sus comparaciones por palabra.
    /// </summary>
    public static string Normalizar(string s)
    {
        var t = s.ToUpperInvariant().Trim()
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I')
            .Replace('Ó', 'O').Replace('Ú', 'U').Replace('Ñ', 'N');

        return new string(t.Where(c => (c >= 'A' && c <= 'Z') || char.IsAsciiDigit(c) || c == '.')
                           .ToArray());
    }

    private static (int A, int B) MasSeparados(List<(double X, double Y, double Z)> p)
    {
        int a = 0, b = 0;
        var max = -1d;

        for (var i = 0; i < p.Count - 1; i++)
        {
            for (var j = i + 1; j < p.Count; j++)
            {
                var d = ((p[i].X - p[j].X) * (p[i].X - p[j].X)) +
                        ((p[i].Y - p[j].Y) * (p[i].Y - p[j].Y));
                if (d > max)
                {
                    max = d;
                    a = i;
                    b = j;
                }
            }
        }

        return (a, b);
    }

    /// <summary>
    /// Distingue un fallo de COM o de la API de un error de programación. Solo los
    /// primeros se toleran; un bug propio debe salir a la superficie.
    /// </summary>
    private static bool EsFalloCom(Exception ex) =>
        ex is COMException
            or MissingMemberException
            or System.Reflection.TargetInvocationException
            or InvalidCastException
            or NullReferenceException
            or FormatException
            or OverflowException;
    /// <summary>
    /// La lista de nombres de un objeto del modelo, con sus etiquetas y niveles si los hay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aquí se separan ETABS y SAP2000, y era el motivo de que SAP2000 leyera 0 frames
    /// y 0 áreas.</b> Se usaba <c>GetLabelNameList</c>, que devuelve nombre + etiqueta +
    /// piso de una vez. Pero eso es de <b>ETABS</b>: la etiqueta y el piso son conceptos
    /// suyos, y SAP2000 no tiene ese método. Al fallar, el lector se rendía y devolvía
    /// cero, aunque el modelo tuviera cientos de barras.
    /// </para>
    /// <para>
    /// SAP2000 sí tiene <c>GetNameList</c>, que devuelve solo los nombres. Es el mismo
    /// que ya se usaba para los puntos, <b>y por eso los puntos sí se leían</b>: 232
    /// puntos y 0 frames en el mismo modelo era la pista de que el problema no era la
    /// conexión sino el método.
    /// </para>
    /// <para>
    /// Que el nivel quede vacío no se calla: significa que el modelo se ve en 3D pero no
    /// se agrupa por pisos, y eso se avisa.
    /// </para>
    /// </remarks>
    private static (string[] Nombres, string[] Etiquetas, string[] Niveles) ListaDeNombres(
        object? obj, ModeloEtabs m, string queEs)
    {
        if (obj is null)
        {
            return (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        // 1) El camino de ETABS: nombre, etiqueta y piso de una vez.
        try
        {
            object?[] a = { 0, null, null, null };
            Com.Call(obj, "GetLabelNameList", a, 0, 1, 2, 3);

            return (Com.AsStrings(a[1]), Com.AsStrings(a[2]), Com.AsStrings(a[3]));
        }
        catch (Exception)
        {
            // No está: casi seguro que es SAP2000. Se sigue por el camino común.
        }

        // 2) El camino común, que es el que tiene SAP2000.
        object?[] b = { 0, null };
        Com.Call(obj, "GetNameList", b, 0, 1);

        var nombres = Com.AsStrings(b[1]);
        var vacios = new string[nombres.Length];

        for (var i = 0; i < vacios.Length; i++)
        {
            vacios[i] = string.Empty;
        }

        m.Avisos.Add(
            $"Los {queEs} se leyeron sin etiqueta ni nivel: este modelo no expone " +
            "'GetLabelNameList', que es de ETABS. Se ven en 3D, pero no se agrupan " +
            "por piso.");

        return (nombres, vacios, vacios);
    }

    /// <summary>
    /// Dimensiones de una sección de barra, con lo que hace falta para dibujar su perfil.
    /// </summary>
    /// <param name="T2">Peralte, en metros.</param>
    /// <param name="T3">Ancho, en metros.</param>
    /// <param name="Forma">RECT, CIRC, I, C, L, TUBO o CAJON.</param>
    /// <param name="Patin">Espesor del patín. Cero si la forma no lo tiene.</param>
    /// <param name="Alma">Espesor del alma. Cero si la forma no lo tiene.</param>
    /// <param name="Pared">Espesor de pared de un tubo o cajón.</param>
    /// <param name="Material">
    /// El material que la propiedad tiene asignado en el modelo: CONC, A992Fy50, el que
    /// sea. Lo devuelve la misma llamada que las medidas, y antes se tiraba.
    /// </param>
    private sealed record Dims(
        double T2, double T3, string Forma, double Patin, double Alma, double Pared,
        string Material = "");

    /// <summary>
    /// Pregunta a SAP2000 <b>qué forma es</b> y llama al lector que le toca.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetTypeOAPI</c> devuelve el tipo de sección, así que no hay que ir probando
    /// getters a ver cuál responde. El tanteo tenía dos problemas: gastaba una llamada COM
    /// por intento fallido, y sobre todo <b>solo cubría tres formas</b>. Una armadura
    /// metálica está hecha de ángulos, tubos y canales, y todos ellos caían al respaldo y
    /// se dibujaban como una caja.
    /// </para>
    /// <para>
    /// Si <c>GetTypeOAPI</c> no está —versiones viejas— se cae al tanteo de siempre, que
    /// para rectángulo, círculo y I sigue funcionando.
    /// </para>
    /// </remarks>
    /// <summary>
    /// El <b>material</b> de la propiedad: la posición 2 del arreglo de la llamada, que en
    /// todos los <c>Get…</c> de sección es <c>MatProp</c>.
    /// </summary>
    private static string Material(object?[] a) =>
        a.Length > 2 ? (a[2]?.ToString() ?? string.Empty).Trim() : string.Empty;

    private static Dims? PorForma(object propFrame, string seccion)
    {
        var tipo = -1;

        try
        {
            object?[] a = { seccion, 0 };

            if (Com.CallRet(propFrame, "GetTypeOAPI", a, 1) == 0)
            {
                tipo = Convert.ToInt32(a[1]);
            }
        }
        catch (Exception)
        {
            tipo = -1;
        }

        // Los valores del enum eFramePropType de CSI. Solo se listan los que se dibujan;
        // el resto cae al tanteo.
        var porTipo = tipo switch
        {
            1 => LeerPerfilI(propFrame, seccion),      // SECTION_I
            2 => LeerCanal(propFrame, seccion),        // SECTION_CHANNEL
            3 => LeerTe(propFrame, seccion),           // SECTION_T
            4 => LeerAngulo(propFrame, seccion),       // SECTION_ANGLE
            6 => LeerCajon(propFrame, seccion),        // SECTION_BOX
            7 => LeerTubo(propFrame, seccion),         // SECTION_PIPE
            8 => LeerRectangulo(propFrame, seccion),   // SECTION_RECTANGULAR
            9 => LeerCirculo(propFrame, seccion),      // SECTION_CIRCLE
            _ => null
        };

        if (porTipo is not null)
        {
            return porTipo;
        }

        // Respaldo: el tanteo de siempre.
        return LeerRectangulo(propFrame, seccion)
               ?? LeerCirculo(propFrame, seccion)
               ?? LeerPerfilI(propFrame, seccion)
               ?? LeerTubo(propFrame, seccion)
               ?? LeerCajon(propFrame, seccion)
               ?? LeerAngulo(propFrame, seccion)
               ?? LeerCanal(propFrame, seccion);
    }

    /// <summary>Tubo redondo: <c>GetPipe(Name, File, Mat, T3, Tw, ...)</c>.</summary>
    private static Dims? LeerTubo(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty, 0d, 0d, 0, string.Empty, string.Empty
            };

            if (Com.CallRet(propFrame, "GetPipe", a, 1, 2, 3, 4, 5, 6, 7) != 0)
            {
                return null;
            }

            var d = Convert.ToDouble(a[3]);
            var tw = Convert.ToDouble(a[4]);

            return d > 0 ? new Dims(d, d, "TUBO", 0, 0, tw, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    /// <summary>Cajón: <c>GetTube(Name, File, Mat, T3, T2, Tf, Tw, ...)</c>.</summary>
    private static Dims? LeerCajon(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty, 0d, 0d, 0d, 0d, 0, string.Empty,
                string.Empty
            };

            if (Com.CallRet(propFrame, "GetTube", a, 1, 2, 3, 4, 5, 6, 7, 8, 9) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);
            var tf = Convert.ToDouble(a[5]);

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "CAJON", 0, 0, tf, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    /// <summary>Ángulo: <c>GetAngle(Name, File, Mat, T3, T2, Tf, Tw, ...)</c>.</summary>
    private static Dims? LeerAngulo(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty, 0d, 0d, 0d, 0d, 0, string.Empty,
                string.Empty
            };

            if (Com.CallRet(propFrame, "GetAngle", a, 1, 2, 3, 4, 5, 6, 7, 8, 9) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);
            var tf = Convert.ToDouble(a[5]);
            var tw = Convert.ToDouble(a[6]);

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "L", tf, tw, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    /// <summary>Canal: <c>GetChannel(Name, File, Mat, T3, T2, Tf, Tw, ...)</c>.</summary>
    private static Dims? LeerCanal(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty, 0d, 0d, 0d, 0d, 0, string.Empty,
                string.Empty
            };

            if (Com.CallRet(propFrame, "GetChannel", a, 1, 2, 3, 4, 5, 6, 7, 8, 9) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);
            var tf = Convert.ToDouble(a[5]);
            var tw = Convert.ToDouble(a[6]);

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "C", tf, tw, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    /// <summary>Te: <c>GetTee(Name, File, Mat, T3, T2, Tf, Tw, ...)</c>.</summary>
    private static Dims? LeerTe(object propFrame, string seccion)
    {
        try
        {
            object?[] a =
            {
                seccion, string.Empty, string.Empty, 0d, 0d, 0d, 0d, 0, string.Empty,
                string.Empty
            };

            if (Com.CallRet(propFrame, "GetTee", a, 1, 2, 3, 4, 5, 6, 7, 8, 9) != 0)
            {
                return null;
            }

            var t3 = Convert.ToDouble(a[3]);
            var t2 = Convert.ToDouble(a[4]);
            var tf = Convert.ToDouble(a[5]);
            var tw = Convert.ToDouble(a[6]);

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "T", tf, tw, 0, Material(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

}
