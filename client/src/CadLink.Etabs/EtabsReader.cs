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
            m.Avisos.Add("Esta versión de ETABS no expone el objeto Story.");
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
            object?[] a = { 0, null, null, null };
            Com.Call(frameObj, "GetLabelNameList", a, 0, 1, 2, 3);
            nombres = Com.AsStrings(a[1]);
            etiquetas = Com.AsStrings(a[2]);
            niveles = Com.AsStrings(a[3]);
            m.Frames = nombres.Length;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo obtener la lista de frames.");
            return;
        }

        var cacheSecciones = new Dictionary<string, (double T2, double T3, string Forma)>(
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
    private static (double T2, double T3, string Forma) DimensionesSeccion(
        object propFrame, string seccion,
        Dictionary<string, (double T2, double T3, string Forma)> cache,
        ModeloEtabs m)
    {
        if (cache.TryGetValue(seccion, out var guardada))
        {
            return guardada;
        }

        var r = LeerRectangulo(propFrame, seccion)
                ?? LeerCirculo(propFrame, seccion)
                ?? LeerPerfilI(propFrame, seccion)
                ?? (0, 0, "RECT");

        if (r.Item1 == 0 && r.Item2 == 0)
        {
            m.Avisos.Add($"Sin dimensiones para la sección '{seccion}'.");
        }

        cache[seccion] = r;
        return r;
    }

    private static (double, double, string)? LeerRectangulo(object propFrame, string seccion)
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
            return t2 > 0 && t3 > 0 ? (t2, t3, "RECT") : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    private static (double, double, string)? LeerCirculo(object propFrame, string seccion)
    {
        try
        {
            object?[] a = { seccion, string.Empty, string.Empty, 0d, 0, string.Empty, string.Empty };
            if (Com.CallRet(propFrame, "GetCircle", a, 1, 2, 3, 4, 5, 6) != 0)
            {
                return null;
            }

            var d = Convert.ToDouble(a[3]);
            return d > 0 ? (d, d, "CIRC") : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

    private static (double, double, string)? LeerPerfilI(object propFrame, string seccion)
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
            return t2 > 0 && t3 > 0 ? (t2, t3, "I") : null;
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
            object?[] a = { 0, null, null, null };
            Com.Call(areaObj, "GetLabelNameList", a, 0, 1, 2, 3);
            nombres = Com.AsStrings(a[1]);
            etiquetas = Com.AsStrings(a[2]);
            niveles = Com.AsStrings(a[3]);
            m.Areas = nombres.Length;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            m.Avisos.Add("No se pudo obtener la lista de áreas.");
            return;
        }

        var cacheEspesor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

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
                e.AnchoM = Espesor(propArea, seccion, esVertical, cacheEspesor);
            }

            m.Elementos.Add(e);
        }
    }

    private static double Espesor(
        object propArea, string seccion, bool esMuro, Dictionary<string, double> cache)
    {
        if (cache.TryGetValue(seccion, out var e))
        {
            return e;
        }

        var metodo = esMuro ? "GetWall" : "GetSlab";
        var valor = 0d;

        try
        {
            object?[] a = { seccion, 0, 0, string.Empty, 0d, 0, string.Empty, string.Empty };
            if (Com.CallRet(propArea, metodo, a, 1, 2, 3, 4, 5, 6, 7) == 0)
            {
                valor = Convert.ToDouble(a[4]);
            }
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            valor = 0;
        }

        cache[seccion] = valor;
        return valor;
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
}
