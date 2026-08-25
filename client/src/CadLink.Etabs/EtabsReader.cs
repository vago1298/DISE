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
        LeerEjes(cx, m);

        // ==============================================================================
        //  SI NO HAY PISOS, LOS NIVELES SALEN DE LA ALTURA EN Z
        // ==============================================================================
        //  SAP2000 no tiene stories: son un concepto de ETABS. Sin esto, un modelo de SAP
        //  llegaba con TODOS los elementos en un solo nivel sin nombre, así que el juego de
        //  plantas era una sola planta con el edificio entero encimado.
        //
        //  Va DESPUÉS de leer los elementos porque se deduce de sus cotas, y cada elemento se
        //  queda con el nombre del nivel que le toca, así que de aquí para adelante todo
        //  —plantas, filtros, rótulos— funciona igual que con ETABS.
        if (m.Niveles.Count == 0)
        {
            m.NivelesDesdeZ();
        }

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
    // La cuadricula de ejes
    // ==================================================================

    /// <summary>
    /// La <b>cuadrícula</b> del modelo, la que lleva burbuja y cota en el plano.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>LeerEjes</c> de la macro: se pide el primer sistema de cuadrícula y se leen sus
    /// ordenadas en X y en Y con <c>GetGridSys_2</c>.
    /// </para>
    /// <para>
    /// <b>Todo va envuelto y sin dar guerra si falla</b>, y no por prudencia excesiva:
    /// <c>GetGridSys_2</c> no existe en todas las versiones —la macro lleva un comentario
    /// avisando de que hay que comentar esa línea si el ETABS del cliente no lo tiene— y en
    /// SAP2000 la cuadrícula puede estar definida de otra manera. Si no se puede leer, se
    /// deja en nulo y quien dibuja usa <c>EjesModelo.DesdeGeometria</c>, que deduce los ejes
    /// de las columnas y los muros. El plano sale con ejes de las dos formas.
    /// </para>
    /// </remarks>
    private static void LeerEjes(EtabsConnection cx, ModeloEtabs m)
    {
        // ==============================================================================
        //  AQUI NO SE PUEDE TIRAR LA LECTURA ENTERA. Se probó con Com.Get y con el filtro
        //  de fallos de COM, y ROMPIÓ SAP2000: al pedir «GridSys» salta una excepción propia
        //  —«devolvió vacío al pedir GridSys»— que no es un fallo de COM, así que subía y se
        //  llevaba por delante el modelo completo. El usuario veía «No se pudo leer el
        //  modelo» con SAP2000 y el de ETABS sí.
        //
        //  Los ejes son OPCIONALES: si no se pueden leer se deducen de las columnas y los
        //  muros. Así que aquí se traga CUALQUIER excepción, y punto.
        // ==============================================================================
        object? gridSys;

        try
        {
            gridSys = Com.TryGet(cx.SapModel, "GridSys");
        }
        catch (Exception)
        {
            gridSys = null;
        }

        if (gridSys is null)
        {
            return;
        }

        // ==============================================================================
        //  TODOS LOS NOMBRES POSIBLES, Y NO SOLO EL PRIMERO
        // ==============================================================================
        //  Aquí estaba el motivo de que en SAP2000 salieran ejes DEDUCIDOS —16 números y 26
        //  letras— en lugar de los 3 y 6 que tiene el modelo: la cuadrícula no se leía y se
        //  caía al respaldo por geometría.
        //
        //  El sistema de ejes de SAP2000 se llama «GLOBAL», y aquí, si GetNameList no
        //  respondía, se probaba «G1», que es el nombre de omisión de ETABS. Con el nombre
        //  equivocado la llamada devuelve error y no hay ejes, aunque estén ahí.
        //
        //  Así que se prueban TODOS los nombres que dé el modelo y, detrás, los tres que se
        //  usan por convención: GLOBAL —SAP2000—, G1 —ETABS— y el vacío, que en algunas
        //  versiones significa «el sistema activo».
        var nombres = new List<string>();

        try
        {
            object?[] a = { 0, null };
            if (Com.CallRet(gridSys, "GetNameList", a, 0, 1) == 0)
            {
                nombres.AddRange(
                    Com.AsStrings(a[1]).Where(n => n.Trim().Length > 0).Select(n => n.Trim()));
            }
        }
        catch (Exception)
        {
            // Sin lista se prueban los de convención, que es lo que hay.
        }

        foreach (var porOmision in new[] { "GLOBAL", "G1", string.Empty })
        {
            if (!nombres.Contains(porOmision, StringComparer.OrdinalIgnoreCase))
            {
                nombres.Add(porOmision);
            }
        }

        foreach (var nombre in nombres)
        {
            if (LeerCuadricula(gridSys, m, nombre))
            {
                return;
            }
        }

        m.Avisos.Add(
            "No se pudo leer la cuadrícula del modelo —se probó con " +
            string.Join(", ", nombres.Select(n => n.Length == 0 ? "(sistema activo)" : n)) +
            "—, así que los ejes se DEDUCEN de la geometría y saldrán más de los que tiene " +
            "el modelo.");
    }

    /// <summary>
    /// Intenta leer la cuadrícula de <b>un</b> sistema de ejes, con las dos firmas.
    /// </summary>
    /// <remarks>
    /// <c>GetGridSys_2</c> es la de ETABS y <c>GetGridSysCartesian</c> la de SAP2000, que
    /// además trae los ejes en Z. Se prueban las dos con el mismo nombre porque lo que cambia
    /// entre versiones no es solo el programa: hay versiones de ETABS que solo tienen la
    /// segunda.
    /// </remarks>
    private static bool LeerCuadricula(object gridSys, ModeloEtabs m, string nombre)
    {

        try
        {
            // La firma es larga: nombre, origen X, origen Y, giro, tipo, cuántos en X,
            // cuántos en Y, y luego los arreglos de IDs, ordenadas, visibles y burbujas.
            object?[] a =
            {
                nombre, 0d, 0d, 0d, string.Empty, 0, 0,
                null, null, null, null, null, null, null, null
            };

            if (Com.CallRet(gridSys, "GetGridSys_2", a,
                            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14) == 0)
            {
                var ejes = new EjesModelo
                {
                    OrigenX = Convert.ToDouble(a[1]),
                    OrigenY = Convert.ToDouble(a[2]),
                    RotacionGrados = Convert.ToDouble(a[3]),
                    Origen = $"cuadrícula del modelo «{nombre}»"
                };

                // Los VISIBLES van en 11 y 12: un eje apagado en el modelo no se dibuja.
                Cargar(ejes.X, Com.AsStrings(a[7]), Com.AsDoubles(a[9]), Banderas(a[11]), m);
                Cargar(ejes.Y, Com.AsStrings(a[8]), Com.AsDoubles(a[10]), Banderas(a[12]), m);

                if (ejes.Hay)
                {
                    m.Ejes = ejes;

                    m.Avisos.Add(
                        $"Ejes leídos del modelo: sistema «{nombre}», " +
                        $"{ejes.X.Count} en X y {ejes.Y.Count} en Y.");

                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Esta versión no tiene GetGridSys_2: se prueba la de SAP2000.
        }

        // ==============================================================================
        //  Y AHORA LA DE SAP2000, QUE ES OTRA FUNCIÓN
        // ==============================================================================
        //  GetGridSys_2 es de ETABS. SAP2000 tiene su cuadrícula en GetGridSysCartesian, que
        //  además trae los ejes en Z. Sin esta segunda pasada, en SAP2000 NUNCA se leía la
        //  cuadrícula del modelo y el plano salía con ejes DEDUCIDOS: uno por cada quiebre de
        //  muro, o sea muchos más de los que el modelo tiene.
        //
        //  El usuario lo dijo tal cual: en SAP hay que respetar los ejes que trae el modelo y
        //  no poner de más.
        try
        {
            // nombre, Xo, Yo, Rz, cuántos en X, en Y y en Z, y luego los arreglos de IDs,
            // ordenadas, visibles y burbujas de las tres direcciones.
            object?[] a =
            {
                nombre, 0d, 0d, 0d, 0, 0, 0,
                null, null, null, null, null, null,
                null, null, null, null, null, null
            };

            if (Com.CallRet(gridSys, "GetGridSysCartesian", a,
                            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
                            13, 14, 15, 16, 17, 18) != 0)
            {
                return false;
            }

            var ejes = new EjesModelo
            {
                OrigenX = Convert.ToDouble(a[1]),
                OrigenY = Convert.ToDouble(a[2]),
                RotacionGrados = Convert.ToDouble(a[3]),
                Origen = $"cuadrícula del modelo «{nombre}»"
            };

            // Los IDs van en 7, 8 y 9 —X, Y y Z—, las ordenadas en 10, 11 y 12, y los
            // VISIBLES en 13, 14 y 15. La de Z no se usa: son los niveles, y esos ya se leen
            // aparte.
            //
            //  Y AQUÍ ESTABA EL PROBLEMA DE «SAP ME GENERA MÁS EJES DE LOS QUE TENGO»: se
            //  leían TODAS las líneas de la cuadrícula, y en SAP2000 es de lo más normal tener
            //  líneas OCULTAS —se apagan con la casilla de visibilidad en cuanto se usan para
            //  construir algo y ya no hacen falta—. Esas líneas siguen en el modelo, así que
            //  la API las devuelve; lo que no hay que hacer es dibujarlas.
            Cargar(ejes.X, Com.AsStrings(a[7]), Com.AsDoubles(a[10]), Banderas(a[13]), m);
            Cargar(ejes.Y, Com.AsStrings(a[8]), Com.AsDoubles(a[11]), Banderas(a[14]), m);

            if (ejes.Hay)
            {
                m.Ejes = ejes;

                m.Avisos.Add(
                    $"Ejes leídos del modelo: sistema «{nombre}», " +
                    $"{ejes.X.Count} en X y {ejes.Y.Count} en Y.");

                return true;
            }
        }
        catch (Exception)
        {
            // Ni una ni otra con este nombre: se prueba el siguiente y, si ninguno responde,
            // se deducen de las columnas y el plano sale con sus ejes igual.
        }

        return false;

        static void Cargar(
            List<EjesModelo.Eje> destino, string[] ids, double[] ords,
            bool[] visibles, ModeloEtabs modelo)
        {
            // ==========================================================================
            //  LOS EJES OCULTOS DEL MODELO NO SE DIBUJAN
            // ==========================================================================
            //  La cuadrícula guarda TODAS las líneas que se han declarado, visibles o no, y la
            //  API las devuelve todas. Un eje apagado en el modelo no es un eje del plano: es
            //  una línea de apoyo que sirvió para construir y que su autor decidió esconder.
            //
            //  Con una salvaguarda: si el arreglo de visibles no cuadra —o dice que NINGUNO se
            //  ve— no se filtra nada. Un plano con todos sus ejes de más es un problema; un
            //  plano SIN ejes es peor, y ese caso no se puede distinguir de un dato mal leído.
            var mirarVisibles = visibles.Length >= ords.Length && visibles.Any(v => v);
            var ocultos = 0;

            for (var i = 0; i < ords.Length; i++)
            {
                if (mirarVisibles && !visibles[i])
                {
                    ocultos++;
                }
            }

            if (ocultos > 0)
            {
                modelo.Avisos.Add(
                    $"{ocultos} eje(s) de la cuadrícula están OCULTOS en el modelo y no se " +
                    "dibujan. Si los quieres en el plano, enciéndelos en el programa.");
            }

            // ==========================================================================
            //  UN EJE, UNA LÍNEA: FUERA LOS REPETIDOS
            // ==========================================================================
            //  La cuadrícula del modelo trae, con más frecuencia de la que parece, el mismo
            //  eje DECLARADO DOS VECES: una en el sistema principal y otra como secundario,
            //  o repetido al haber copiado la planta. GetGridSys_2 devuelve todas las líneas
            //  declaradas, no las distintas, así que sin este filtro se dibujan dos líneas
            //  exactamente encima de la otra, con dos burbujas superpuestas y dos cotas
            //  iguales. En el plano eso no se ve como un eje de más: se ve como un eje MÁS
            //  GRUESO Y MÁS OSCURO que los demás, que es justo lo que se reportó.
            //
            //  Un centímetro de holgura. Dos ejes de verdad nunca están a menos de eso —en
            //  el papel serían la misma línea— pero una ordenada guardada como 4.9999 y otra
            //  como 5.0 sí pasan si se comparan exactas.
            //
            //  Se guarda el PRIMERO, que es el que trae el nombre bueno.
            const double tol = 0.01;

            for (var i = 0; i < ords.Length; i++)
            {
                // El eje apagado en el modelo se salta: no es un eje del plano.
                if (mirarVisibles && !visibles[i])
                {
                    continue;
                }

                var repetido = false;

                foreach (var ya in destino)
                {
                    if (Math.Abs(ya.Ordenada - ords[i]) < tol)
                    {
                        repetido = true;
                        break;
                    }
                }

                if (repetido)
                {
                    continue;
                }

                var id = i < ids.Length && ids[i].Trim().Length > 0
                    ? ids[i].Trim()
                    : (i + 1).ToString();

                destino.Add(new EjesModelo.Eje(id, ords[i]));
            }

            destino.Sort((p, q) => p.Ordenada.CompareTo(q.Ordenada));
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

            // EL GIRO DE LA SECCION. Es lo que hace que una columna de 20x60 se vea de
            // 20x60 y no de 60x20, y con el se inserta el bloque en la orientación que
            // tiene en el modelo. La macro lo lee igual, con GetLocalAxes.
            try
            {
                object?[] a = { nombre, 0d, false };

                if (Com.CallRet(frameObj, "GetLocalAxes", a, 1, 2) == 0)
                {
                    e.AnguloGrados = Convert.ToDouble(a[1]);
                }
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                // Sin ángulo la sección sale derecha, como antes.
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

                // LAS NOTAS DE LA PROPIEDAD, que antes se tiraban en las columnas y las
                // trabes. Son las que dicen si «K 15X23.5» es un CASTILLO o una COLUMNA, y
                // eso no se puede sacar del nombre ni de las medidas sin equivocarse.
                e.Notas = dims.Notas;

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

            // ==========================================================================
            //  EL PUNTO DE INSERCIÓN: POR ESTO EL ELEMENTO APARECE MOVIDO
            // ==========================================================================
            //  Va AL FINAL, después de las dimensiones, porque el corrimiento del punto
            //  cardinal se mide con el ancho y el peralte de la sección.
            //
            //  En el modelo la barra se calcula sobre la línea que une sus dos nudos, pero
            //  la pieza que se construye —y la que hay que dibujar— está donde la ponen su
            //  punto cardinal y sus offsets de nudo. Es el «Frame Assignment - Insertion
            //  Point» de ETABS, y sin leerlo el plano sale con las barras en el eje del nudo
            //  mientras que en la pantalla de ETABS se ven corridas.
            LeerPuntoDeInsercion(frameObj, nombre, e, m);

            m.Elementos.Add(e);
        }
    }

    /// <summary>
    /// Las <b>banderas de visibilidad</b> de la cuadrícula, tal como las devuelva la API.
    /// </summary>
    /// <remarks>
    /// Se toleran las tres formas en que CSI puede devolverlas —booleanos, números o textos
    /// tipo <c>Yes</c>/<c>True</c>—, porque cambia entre versiones y entre ETABS y SAP2000. Lo
    /// que no se puede hacer es suponer una sola forma: si la conversión falla, el eje se da
    /// por VISIBLE, que es el lado seguro —se dibuja de más, no de menos—.
    /// </remarks>
    private static bool[] Banderas(object? v)
    {
        if (v is null)
        {
            return Array.Empty<bool>();
        }

        if (v is bool[] bs)
        {
            return bs;
        }

        if (v is not System.Collections.IEnumerable lista)
        {
            return Array.Empty<bool>();
        }

        var salida = new List<bool>();

        foreach (var x in lista)
        {
            salida.Add(x switch
            {
                bool b => b,
                null => true,
                _ => Verdadero(x.ToString())
            });
        }

        return salida.ToArray();
    }

    /// <summary>¿Ese texto dice que sí? <c>True</c>, <c>Yes</c>, <c>1</c>, <c>Si</c>…</summary>
    private static bool Verdadero(string? t)
    {
        var s = (t ?? string.Empty).Trim().ToUpperInvariant();

        // Vacío = visible: más vale un eje de más que un plano sin ejes.
        return s.Length == 0
               || s is "TRUE" or "YES" or "SI" or "SÍ" or "1" or "-1" or "VERDADERO" or "V";
    }

    /// <summary>
    /// Si el punto de inserción se <b>aplica</b> a la geometría. Válvula de escape.
    /// </summary>
    /// <remarks>
    /// En <c>false</c> las barras se quedan en la línea de sus nudos, como salían antes de
    /// leer el punto de inserción. Está para poder comparar los dos planos si alguna vez un
    /// modelo trae los offsets al revés de lo esperado.
    /// </remarks>
    public static bool AplicarPuntosDeInsercion { get; set; } = true;

    /// <summary>
    /// Lee el <b>punto de inserción</b> del marco y <b>mueve</b> sus extremos en planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La llamada tiene dos firmas según la versión —<c>GetInsertionPoint_1</c> en las nuevas,
    /// con el espejo respecto del eje 3, y <c>GetInsertionPoint</c> en las viejas, sin él— así
    /// que se prueban las dos en cascada, igual que se hace con la cuadrícula. Si ninguna
    /// responde no pasa nada: la barra se queda en la línea de sus nudos, que es como salía
    /// antes, y no se avisa por cada elemento.
    /// </para>
    /// <para>
    /// <b>Solo se mueve en planta</b>, la Z no se toca. En planta la elevación no se ve, y
    /// mover la de una trabe 2.5 cm podría cambiarle el nivel al que se asigna: se arreglaría
    /// algo que no se nota y se rompería algo que sí.
    /// </para>
    /// </remarks>
    private static void LeerPuntoDeInsercion(
        object frameObj, string nombre, ElementoEtabs e, ModeloEtabs m)
    {
        if (!AplicarPuntosDeInsercion)
        {
            return;
        }

        var punto = PuntoDeInsercion.Centroide;
        var espejo2 = false;
        var espejo3 = false;
        double[] offI = { 0, 0, 0 };
        double[] offJ = { 0, 0, 0 };
        var sistema = "Local";
        var leido = false;

        // 1) La firma nueva: nombre, punto cardinal, espejo 2, espejo 3, transformar la
        //    rigidez, offsets de I, offsets de J y el sistema de coordenadas.
        try
        {
            object?[] a =
            {
                nombre, 0, false, false, false, null, null, string.Empty
            };

            if (Com.CallRet(frameObj, "GetInsertionPoint_1", a, 1, 2, 3, 4, 5, 6, 7) == 0)
            {
                punto = Convert.ToInt32(a[1]);
                espejo2 = Convert.ToBoolean(a[2]);
                espejo3 = Convert.ToBoolean(a[3]);
                offI = Com.AsDoubles(a[5]);
                offJ = Com.AsDoubles(a[6]);
                sistema = a[7]?.ToString() ?? "Local";
                leido = true;
            }
        }
        catch (Exception)
        {
            // Esta versión no la tiene: se prueba la otra firma.
        }

        // 2) La firma vieja, sin el espejo respecto del eje 3.
        if (!leido)
        {
            try
            {
                object?[] a = { nombre, 0, false, false, null, null, string.Empty };

                if (Com.CallRet(frameObj, "GetInsertionPoint", a, 1, 2, 3, 4, 5, 6) == 0)
                {
                    punto = Convert.ToInt32(a[1]);
                    espejo2 = Convert.ToBoolean(a[2]);
                    offI = Com.AsDoubles(a[4]);
                    offJ = Com.AsDoubles(a[5]);
                    sistema = a[6]?.ToString() ?? "Local";
                    leido = true;
                }
            }
            catch (Exception)
            {
                // Ni una ni otra: se queda en la línea de los nudos, como antes.
            }
        }

        if (!leido)
        {
            return;
        }

        e.PuntoCardinal = punto;
        e.Espejo2 = espejo2;
        e.Espejo3 = espejo3;

        // «Local» —o «Locales», según el idioma— significa ejes 1, 2 y 3; cualquier otra cosa
        // es el sistema global, y entonces los offsets ya vienen en X, Y y Z.
        var enLocales = sistema.Trim().StartsWith("Local", StringComparison.OrdinalIgnoreCase);

        var vertical = e.Clase == ClaseElemento.Columna;

        // t3 se mide sobre el eje local 2 y t2 sobre el 3. El lector guarda el ancho y el
        // peralte cambiados entre columna y trabe justamente por eso, así que aquí se
        // deshace ese cambio para volver a t3 y t2.
        var dim2 = vertical ? e.AnchoM : e.PeralteM;    // t3
        var dim3 = vertical ? e.PeralteM : e.AnchoM;    // t2

        var (dxi, dyi) = PuntoDeInsercion.EnPlanta(
            vertical, e.X2 - e.X1, e.Y2 - e.Y1, e.AnguloGrados,
            offI, enLocales, punto, dim2, dim3, espejo2, espejo3);

        var (dxj, dyj) = PuntoDeInsercion.EnPlanta(
            vertical, e.X2 - e.X1, e.Y2 - e.Y1, e.AnguloGrados,
            offJ, enLocales, punto, dim2, dim3, espejo2, espejo3);

        e.MovidoXI = dxi;
        e.MovidoYI = dyi;
        e.MovidoXJ = dxj;
        e.MovidoYJ = dyj;

        if (!e.ConPuntoDeInsercion)
        {
            return;
        }

        e.X1 += dxi;
        e.Y1 += dyi;
        e.X2 += dxj;
        e.Y2 += dyj;

        m.ConPuntoDeInsercion++;
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
            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "RECT", 0, 0, 0, Material(a), NotasDe(a)) : null;
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
            return d > 0 ? new Dims(d, d, "CIRC", 0, 0, 0, Material(a), NotasDe(a)) : null;
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

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "I", tf, tw, 0, Material(a), NotasDe(a)) : null;
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

        // Las secciones que ya se avisaron por no traer espesor: el aviso va una vez por
        // propiedad, no una por paño.
        var sinEspesor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // EL PIER DEL MURO. Es lo que se rotula en el plano, y por eso se pide aquí y
            // no en una pasada aparte: la macro hace lo mismo y usa el pier COMO etiqueta
            // del muro. Un muro sin pier no lleva rótulo, que es mejor que rotular el
            // nombre de la propiedad 31 veces.
            var pier = string.Empty;

            if (esVertical)
            {
                try
                {
                    object?[] a = { nombre, string.Empty };
                    Com.Call(areaObj, "GetPier", a, 1);
                    pier = a[1]?.ToString()?.Trim() ?? string.Empty;

                    if (pier.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        pier = string.Empty;
                    }
                }
                catch (Exception ex) when (EsFalloCom(ex))
                {
                    // Un modelo sin piers asignados: el muro se queda sin rótulo.
                }
            }

            var e = new ElementoEtabs
            {
                Clase = esVertical ? ClaseElemento.Muro : ClaseElemento.Losa,
                Story = i < niveles.Length ? niveles[i] : string.Empty,

                // En el MURO la etiqueta ES el pier, como en la macro. En la losa, la suya.
                Etiqueta = esVertical
                    ? pier
                    : (i < etiquetas.Length && etiquetas[i].Length > 0 ? etiquetas[i] : nombre),

                Pier = pier,
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

                // EL ESPESOR QUE NO VINO SE APUNTA, una vez por sección. Es el dato que hace
                // que el rótulo del plano salga con el hueco vacío en «    cm de espesor» y
                // que la vista extruida tenga que inventarse un espesor para no dibujar la
                // losa plana. Se dice UNA vez por propiedad y no una por paño: en un modelo
                // con 40 losas de la misma sección, 40 avisos iguales no informan, tapan.
                if (prop.EspesorM <= 0 && sinEspesor.Add(seccion))
                {
                    m.Avisos.Add(
                        $"La propiedad '{seccion}' no dio su espesor. Se dibuja con el de " +
                        "omisión; ponlo en ETABS para que el plano lo acote de verdad.");
                }
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

        // ==============================================================================
        //  EL DECK PRIMERO, COMO EN LA MACRO
        // ==============================================================================
        //  Su PropiedadDeLosa prueba GetDeck ANTES de GetSlab, y el orden importa: una
        //  losacero es un DECK y su propiedad NO responde a GetSlab, así que preguntando al
        //  revés se quedaba sin espesor y sin notas —y sin saber que era losacero—.
        //
        //  Cuando es un deck se le añade la palabra DECK a las notas: es lo que después
        //  reconoce el dibujante para poner las franjas de losacero en lugar del armado de
        //  concreto, igual que hace EsLosacero allá con la etiqueta y las notas.
        var metodo = esMuro ? "GetWall" : "GetSlab";
        var valor = 0d;
        var notas = string.Empty;
        var material = string.Empty;

        if (!esMuro)
        {
            try
            {
                object?[] d = { seccion, 0, 0, string.Empty, 0d, 0, string.Empty, string.Empty };

                if (Com.CallRet(propArea, "GetDeck", d, 1, 2, 3, 4, 5, 6, 7) == 0)
                {
                    valor = Convert.ToDouble(d[4]);
                    material = (d[3]?.ToString() ?? string.Empty).Trim();

                    notas = ("DECK " + (d[6]?.ToString() ?? string.Empty) + " " + material)
                        .Trim();

                    var listo = (valor, notas, material);
                    cache[seccion] = listo;
                    return listo;
                }
            }
            catch (Exception ex) when (EsFalloCom(ex))
            {
                // No es un deck: se sigue con GetSlab, que es el caso normal.
            }
        }

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

        // ==============================================================================
        //  LA LOSA ALIGERADA Y LA RETICULAR, QUE NO RESPONDEN A GetSlab
        // ==============================================================================
        //  Una losa nervada —ribbed— o reticular —waffle— es una propiedad de losa distinta,
        //  y a GetSlab le devuelve espesor 0 o le falla. Aquí es donde se perdía el espesor de
        //  las losas del modelo: sin él, el rótulo del plano sale con el hueco vacío en
        //  «     cm de espesor» y la vista extruida dibuja la losa PLANA, como una hoja.
        //
        //  De las dos se toma el PERALTE TOTAL —OverallDepth—, que es el espesor con el que
        //  se dibuja y se acota: el de la capa de compresión sola no dice lo que mide la losa.
        if (!esMuro && valor <= 0)
        {
            foreach (var metodoLosa in new[] { "GetSlabRibbed", "GetSlabWaffle" })
            {
                try
                {
                    object?[] a = { seccion, 0d, 0d, 0d, 0d, 0d, 0 };

                    if (Com.CallRet(propArea, metodoLosa, a, 1, 2, 3, 4, 5, 6) == 0)
                    {
                        var total = Convert.ToDouble(a[1]);

                        if (total > 0)
                        {
                            valor = total;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // No es de ese tipo: se prueba el siguiente y, al final, el nombre.
                }
            }
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
        string Material = "", string Notas = "");

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

    /// <summary>
    /// Las <b>notas</b> de la propiedad de sección: el penúltimo dato de la llamada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Todos los <c>Get…</c> de sección de CSI terminan igual: <c>…, Color, Notes, GUID</c>.
    /// Así que las notas son la penúltima posición del arreglo, y con eso se leen sin tener
    /// que conocer la firma de cada forma.
    /// </para>
    /// <para>
    /// Y hacen falta: es donde el ingeniero escribe <b>qué es</b> la pieza —CASTILLO, COLUMNA,
    /// TRABE— y eso es lo que se pidió para clasificar la tabla de secciones. El nombre de la
    /// sección cambia de obra en obra y las medidas se equivocan en los casos de frontera
    /// —una de 15×23.5 pasa de 20 cm y por medidas sale COLUMNA aunque en obra sea un
    /// castillo—. Antes solo se leían las notas de los MUROS y las LOSAS; las de las columnas
    /// y las trabes se tiraban.
    /// </para>
    /// </remarks>
    private static string NotasDe(object?[] a) =>
        a.Length > 2 ? (a[^2]?.ToString() ?? string.Empty).Trim() : string.Empty;

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

            return d > 0 ? new Dims(d, d, "TUBO", 0, 0, tw, Material(a), NotasDe(a)) : null;
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

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "CAJON", 0, 0, tf, Material(a), NotasDe(a)) : null;
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

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "L", tf, tw, 0, Material(a), NotasDe(a)) : null;
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

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "C", tf, tw, 0, Material(a), NotasDe(a)) : null;
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

            return t2 > 0 && t3 > 0 ? new Dims(t2, t3, "T", tf, tw, 0, Material(a), NotasDe(a)) : null;
        }
        catch (Exception ex) when (EsFalloCom(ex))
        {
            return null;
        }
    }

}
