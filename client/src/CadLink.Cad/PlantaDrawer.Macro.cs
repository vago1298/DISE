using CadLink.Cad.PlanoEstructural;

namespace CadLink.Cad;

/// <summary>
/// Etapa 4 del port: lo que convierte el dibujo de elementos en un <b>plano</b>.
/// </summary>
/// <remarks>
/// <para>
/// Aquí están las piezas de la macro que no dibujan elementos pero son las que hacen que el
/// plano se vea como el suyo: los <b>ejes con sus burbujas</b>, las <b>cotas en los cuatro
/// lados</b>, los <b>estilos de texto y de cota</b>, el <b>rótulo de dos renglones</b>, la
/// <b>línea de mampostería</b> y el <b>orden de dibujo</b>.
/// </para>
/// <para>
/// Va en un archivo aparte de la misma clase —no en otra clase— porque comparte con el resto
/// el documento, las capas y las primitivas: partirlo en dos objetos obligaría a pasarse el
/// <c>ModelSpace</c> y la lista de fallos de uno a otro sin ganar nada.
/// </para>
/// <para>
/// La cuenta de dónde va cada cosa está en <see cref="EjesPlano"/> y en
/// <see cref="RotuloPlanta"/>, que no tocan AutoCAD y tienen prueba propia en
/// <c>tools/prueba-ejes-plano</c>. Aquí queda solo lo que necesita COM.
/// </para>
/// </remarks>
public sealed partial class PlantaDrawer
{
    /// <summary>La cuenta de los ejes, las burbujas y las cotas.</summary>
    private EjesPlano Ejes => _ejes ??= new EjesPlano(_cfg);

    private EjesPlano? _ejes;

    /// <summary>El rótulo de la planta.</summary>
    private RotuloPlanta Rot => _rotulo ??= new RotuloPlanta(_cfg);

    private RotuloPlanta? _rotulo;

    private bool _estilosListos;

    // =================================================================================
    //  ESTILOS DE TEXTO Y DE COTA
    // =================================================================================

    /// <summary>
    /// Crea los estilos de la macro: los cuatro de texto y el de cota.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son los que hacen que el plano se lea como el suyo y no con la letra de fábrica de
    /// AutoCAD:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>TEXTO_SECCIONES</c> —Bahnschrift, 0.12— para las secciones.</item>
    ///   <item><c>TEXTO_CADENAS</c> —0.09— solo para el rótulo de las cadenas.</item>
    ///   <item><c>TEXTO_LOSAS</c> —0.072— solo para el de la losa.</item>
    ///   <item><c>COTA</c> —Century Gothic <b>negrita</b>, 0.1— para las cotas.</item>
    ///   <item><c>HAETTENSCHWEILER</c> —altura libre— para el rótulo de la planta.</item>
    /// </list>
    /// <para>
    /// Los tres primeros llevan <b>altura fija</b>, y eso es a propósito y está en la macro:
    /// con altura fija en el estilo, el texto sale del mismo tamaño aunque quien lo inserte
    /// se equivoque. La contrapartida es que un MTEXT obedece al estilo y no al objeto, y de
    /// ahí viene todo el enredo de <c>LOSA_TEXTO_ALTURA</c> allá.
    /// </para>
    /// <para>
    /// Si una fuente no está instalada se avisa y se sigue: un plano con otra letra se
    /// entrega; uno sin cotas, no.
    /// </para>
    /// </remarks>
    private void AsegurarEstilosDeLaMacro()
    {
        if (_estilosListos)
        {
            return;
        }

        _estilosListos = true;

        // El del rótulo va con altura 0 —libre— porque sus dos renglones miden distinto.
        EstiloDeTexto(Rot.Estilo,
                      _cfg.Texto("ROTULO_NOMBRE_FUENTE", "Haettenschweiler"),
                      _cfg.Texto("ROTULO_FUENTE", "hatten.ttf"), 0, false);

        EstiloDeTexto(_cfg.Texto("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES"),
                      _cfg.Texto("SEC_NOMBRE_FUENTE", "Bahnschrift"),
                      _cfg.Texto("SEC_FUENTE", "bahnschrift.ttf"),
                      _cfg.Numero("SEC_ALTURA", 0.12), false);

        if (_cfg.Bandera("CADENA_USAR_ESTILO", true))
        {
            EstiloDeTexto(_cfg.Texto("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS"),
                          _cfg.Texto("CADENA_NOMBRE_FUENTE", "Bahnschrift"),
                          _cfg.Texto("CADENA_FUENTE", "bahnschrift.ttf"),
                          _cfg.Numero("CADENA_TEXTO_ALTURA", 0.09), false);
        }

        if (_cfg.Bandera("LOSA_USAR_ESTILO", true))
        {
            EstiloDeTexto(_cfg.Texto("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS"),
                          _cfg.Texto("LOSA_NOMBRE_FUENTE", "Bahnschrift"),
                          _cfg.Texto("LOSA_FUENTE", "bahnschrift.ttf"),
                          _cfg.Numero("LOSA_TEXTO_ALTURA", 0.072), false);
        }

        // El de las cotas, en NEGRITA: la negrita solo se puede pedir por el nombre de la
        // fuente, no por el archivo .ttf, y por eso se intenta primero SetFont.
        EstiloDeTexto(_cfg.Texto("ESTILO_TEXTO_COTA", "COTA"),
                      _cfg.Texto("COTA_NOMBRE_FUENTE", "Century Gothic"),
                      _cfg.Texto("FUENTE_COTA", "gothicb.ttf"),
                      _cfg.Numero("ALTURA_ESTILO_COTA", 0.1),
                      _cfg.Bandera("COTA_NEGRITA", true));

        if (_cfg.Bandera("CREAR_ESTILO_COTA", true))
        {
            EstiloDeCota();
        }
    }

    private void EstiloDeTexto(
        string nombre, string fuente, string archivo, double altura, bool negrita)
    {
        if (nombre.Length == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic est;

                try
                {
                    est = estilos.Item(nombre);
                }
                catch (Exception)
                {
                    est = estilos.Add(nombre);
                }

                var puesta = false;

                if (fuente.Length > 0)
                {
                    try
                    {
                        est.SetFont(fuente, negrita, false, 0, 0);
                        puesta = true;
                    }
                    catch (Exception)
                    {
                        puesta = false;
                    }
                }

                if (!puesta && archivo.Length > 0)
                {
                    try
                    {
                        est.fontFile = archivo;
                        puesta = true;
                    }
                    catch (Exception)
                    {
                        puesta = false;
                    }
                }

                if (!puesta)
                {
                    Nota($"No se pudo poner la fuente «{(fuente.Length > 0 ? fuente : archivo)}» " +
                         $"al estilo {nombre}: revisa que esté instalada.");
                }

                try
                {
                    est.Height = altura < 0 ? 0 : altura;
                    est.Width = 1;
                    est.ObliqueAngle = 0;
                }
                catch (Exception)
                {
                    // Alguna versión no deja tocar esto por objeto; el estilo ya existe.
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Crear el estilo de texto '{nombre}'", ex);
        }
    }

    /// <summary>
    /// El estilo de cota <c>COTA_DIM</c>, con las variables de la hoja.
    /// </summary>
    /// <remarks>
    /// Se hace como en la macro: se ponen las variables <c>DIM*</c> del dibujo y el estilo se
    /// crea con <c>CopyFrom(doc)</c>, que se las lleva todas. Es la única forma con la API de
    /// COM: no hay una propiedad por variable en el objeto del estilo.
    /// </remarks>
    private void EstiloDeCota()
    {
        var nombre = _cfg.Texto("ESTILO_COTA", "COTA_DIM");

        if (nombre.Length == 0)
        {
            return;
        }

        var sep = _cfg.Texto("COTA_SEPARADOR_DECIMAL", ".");
        var codigoSep = sep.Length > 0 ? (int)sep[0] : 46;   // 46 = punto

        try
        {
            AcadConnection.Retry(() =>
            {
                void V(string variable, object valor)
                {
                    try
                    {
                        _doc.SetVariable(variable, valor);
                    }
                    catch (Exception)
                    {
                        // Una variable que esta versión no tenga no tira el estilo entero.
                    }
                }

                V("DIMTXSTY", _cfg.Texto("ESTILO_TEXTO_COTA", "COTA"));
                V("DIMTXT", _cfg.Numero("COTA_TEXT_HEIGHT", 0.1));
                V("DIMCLRT", (int)_cfg.Numero("COTA_COLOR_TEXTO", 1));
                V("DIMTFILL", 0);
                V("DIMASZ", _cfg.Numero("COTA_ARROW_SIZE", 0.05));
                V("DIMSAH", 0);

                var flecha = _cfg.Texto("COTA_FLECHA", "_OBLIQUE");
                V("DIMBLK", flecha);
                V("DIMBLK1", flecha);
                V("DIMBLK2", flecha);
                V("DIMLDRBLK", flecha);

                V("DIMCEN", _cfg.Numero("COTA_CENTER_MARK", 0.04));
                V("DIMEXE", _cfg.Numero("COTA_EXT_LINE_EXT", 0));
                V("DIMEXO", _cfg.Numero("COTA_EXT_LINE_OFFSET", 0.5));

                // 0 = el número EN MEDIO de la línea de cota; 1 = encima.
                V("DIMTAD", _cfg.Bandera("COTA_TEXTO_EN_MEDIO", true) ? 0 : 1);
                V("DIMJUST", 0);
                V("DIMGAP", _cfg.Numero("COTA_OFFSET_DIM_LINE", 0.04));
                V("DIMTIH", 1);
                V("DIMTOH", 1);
                V("DIMLUNIT", 2);
                V("DIMDEC", (int)_cfg.Numero("COTA_PRECISION", 3));
                V("DIMDSEP", codigoSep);
                V("DIMRND", 0);
                V("DIMLFAC", 1);
                V("DIMSCALE", _cfg.Numero("COTA_ESCALA_GENERAL", 1));
                V("DIMZIN", 0);
                V("DIMTOL", 0);
                V("DIMLIM", 0);

                dynamic estilos = _doc.DimStyles;
                dynamic est;

                try
                {
                    est = estilos.Item(nombre);
                }
                catch (Exception)
                {
                    est = estilos.Add(nombre);
                }

                est.CopyFrom(_doc);
                _doc.ActiveDimStyle = est;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Crear el estilo de cota '{nombre}'", ex);
        }
    }

    // =================================================================================
    //  LOS EJES, CON SUS BURBUJAS Y SUS COTAS
    // =================================================================================

    /// <summary>
    /// Dibuja la cuadrícula: la línea de cada eje, sus burbujas y las cotas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La extensión de las líneas se toma del <b>rectángulo de la planta</b> unido con el de
    /// los propios ejes: un eje puede quedar fuera de lo dibujado —un voladizo que no llega—
    /// y su línea tiene que cruzar la planta de todos modos.
    /// </para>
    /// <para>
    /// Las burbujas de arriba y de la izquierda arrancan a <c>EJES_INICIO_BURBUJA_M</c>; las
    /// de abajo y la derecha, a <c>EJES_SALE_CORTO_M</c>, que en 0 significa «lo mismo». Con
    /// eso las cuatro filas quedan a la misma distancia, que es como se ve en su plano.
    /// </para>
    /// </remarks>
    private void DibujarEjesDeLaPlanta(
        PlantaCad p, double dx, double dy,
        double xMin, double yMin, double xMax, double yMax)
    {
        if (p.EjesX.Count == 0 && p.EjesY.Count == 0)
        {
            return;
        }

        var capaLinea = _capas.Prefijo + "EJES";
        var capaBur = _capas.Prefijo + "EJES-BURBUJA";
        var capaTxt = _capas.Prefijo + "EJES-TEXTO";

        // El rectángulo, estirado hasta los ejes que se salgan de lo dibujado.
        foreach (var (_, o) in p.EjesX)
        {
            xMin = Math.Min(xMin, o);
            xMax = Math.Max(xMax, o);
        }

        foreach (var (_, o) in p.EjesY)
        {
            yMin = Math.Min(yMin, o);
            yMax = Math.Max(yMax, o);
        }

        var escalaLt = _cfg.Numero("EJES_ESCALA_TIPOLINEA", 1);

        // ---- los verticales -------------------------------------------------------
        foreach (var e in Ejes.Verticales(p.EjesX, yMin, yMax))
        {
            var x = e.Ordenada + dx;
            LineaDeEje(x, e.Desde + dy, x, e.Hasta + dy, capaLinea, escalaLt);

            // La dirección hacia el dibujo: la de abajo mira hacia arriba y al revés.
            Burbuja(x, e.BurbujaA + dy, e.Id, capaBur, capaTxt, 0, 1);
            Burbuja(x, e.BurbujaB + dy, e.Id, capaBur, capaTxt, 0, -1);
        }

        // ---- los horizontales -----------------------------------------------------
        foreach (var e in Ejes.Horizontales(p.EjesY, xMin, xMax))
        {
            var y = e.Ordenada + dy;
            LineaDeEje(e.Desde + dx, y, e.Hasta + dx, y, capaLinea, escalaLt);

            Burbuja(e.BurbujaA + dx, y, e.Id, capaBur, capaTxt, 1, 0);
            Burbuja(e.BurbujaB + dx, y, e.Id, capaBur, capaTxt, -1, 0);
        }

        // ---- y las cotas ----------------------------------------------------------
        var cotas = Ejes.Cotas(
            p.EjesX.Select(e => e.Ordenada).ToList(),
            p.EjesY.Select(e => e.Ordenada).ToList(),
            xMin, yMin, xMax, yMax);

        var capaCotas = _capas.Prefijo + "COTAS";
        var extTotal = _cfg.Numero("COTA_TOTAL_EXT_LINE_EXT", 0);

        foreach (var c in cotas)
        {
            CotaAlineada(c.X1 + dx, c.Y1 + dy, c.X2 + dx, c.Y2 + dy,
                         c.XTexto + dx, c.YTexto + dy, capaCotas,
                         c.EsTotal ? extTotal : -1);
        }
    }

    /// <summary>La línea de un eje, con el <c>LinetypeScale</c> de la hoja.</summary>
    /// <remarks>
    /// El tipo de línea lo pone la <b>capa</b> —DASHDOT—, pero la escala va por objeto: en un
    /// dibujo en metros, un DASHDOT a escala 1 se ve como una línea continua.
    /// </remarks>
    private void LineaDeEje(
        double x1, double y1, double x2, double y2, string capa, double escalaLt)
    {
        var l = Linea(x1, y1, x2, y2, capa);

        if (l is null || escalaLt <= 0 || Math.Abs(escalaLt - 1) < 1e-9)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)l).LinetypeScale = escalaLt; });
        }
        catch (Exception)
        {
            // Sin escala se ve continua: es cosmético.
        }
    }

    /// <summary>
    /// Una burbuja de eje: sus dos círculos, sus rayitas y su nombre.
    /// </summary>
    /// <remarks>
    /// Las <b>rayitas van en la capa de la burbuja</b>, no en la de las líneas de eje. Es un
    /// detalle de la macro —y de la v31, donde se corrigió— y se nota: en la capa de los ejes
    /// saldrían del color tenue de la cuadrícula y la burbuja se vería descosida.
    /// </remarks>
    private void Burbuja(
        double cx, double cy, string texto, string capaBur, string capaTxt,
        double ux, double uy)
    {
        var r = Ejes.RadioBurbuja;

        Circulo(cx, cy, r, capaBur);

        var anillo = Ejes.RadioAnillo();

        if (anillo > 0 && anillo < r)
        {
            Circulo(cx, cy, anillo, capaBur);
        }

        foreach (var (x1, y1, x2, y2) in Ejes.RayitasDeBurbuja(cx, cy, ux, uy))
        {
            Linea(x1, y1, x2, y2, capaBur);
        }

        TextoCentrado(cx, cy, texto, Ejes.AlturaTextoBurbuja(), capaTxt);
    }

    // =================================================================================
    //  EL RÓTULO DE LA PLANTA, DE DOS RENGLONES
    // =================================================================================

    /// <summary>
    /// El rótulo: <c>PLANTA  ESTRUCTURAL</c> y, debajo, el nivel con su escala.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Va <b>debajo de los ejes de abajo</b>, no del dibujo: la cuenta es la punta del eje más
    /// la burbuja y su rayita, más <c>ROTULO_SEPARACION_EJES</c>. Si se midiera desde el
    /// dibujo, el rótulo caería encima de las burbujas.
    /// </para>
    /// <para>
    /// El segundo renglón va alineado a la <b>derecha</b> del primero, con la línea entre los
    /// dos si <c>ROTULO_LINEA</c>. Y para centrarlo hay que <b>medir</b> el primero: se
    /// dibuja, se mide su caja y se mueve, que es exactamente lo que hace la macro, porque el
    /// ancho de un texto depende de la fuente y no se puede calcular.
    /// </para>
    /// </remarks>
    private void RotuloDeLaPlanta(
        PlantaCad p, double dx, double dy, double xMin, double yMin, double xMax)
    {
        var capa = _capas.Prefijo + "TITULO";

        var h1 = Rot.AlturaTitulo;
        var h2 = Rot.AlturaNivel;
        var hayEjes = p.EjesX.Count > 0 || p.EjesY.Count > 0;

        var margen = _cfg.Numero("MARGEN", 3) / 4;
        var x0 = xMin + dx - margen;
        var y0 = yMin + dy - Ejes.AbajoDeEjes(hayEjes) - Rot.SeparacionEjes - h1;

        var s1 = Rot.Titulo;
        var s2 = Rot.RenglonDelNivel(p.Nivel);

        var t1 = TextoEstilo(x0, y0, s1, h1, capa, Rot.Estilo, false);
        var ancho = AnchoDeTexto(t1, s1, h1);

        if (Rot.Centrado)
        {
            var cx = ((xMin + xMax) / 2) + dx;
            x0 = cx - (ancho / 2);
            MoverTexto(t1, x0, y0);
        }

        var xDer = x0 + ancho;

        // La línea entre los dos renglones, del ancho del título.
        var yLinea = y0 - (h1 * 0.22);

        if (Rot.ConLinea)
        {
            Linea(x0, yLinea, xDer, yLinea, capa);
        }

        // El segundo renglón, alineado a la DERECHA: así los dos cierran en la misma
        // vertical aunque el nombre del nivel sea largo.
        TextoEstilo(xDer, yLinea - (h2 * 1.35), s2, h2, capa, Rot.Estilo, true);
    }

    // =================================================================================
    //  LA LÍNEA DE MAMPOSTERÍA
    // =================================================================================

    /// <summary>
    /// La polilínea ancha al centro del muro de <b>block</b>: es la marca de mampostería.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el <c>PEDIT → Width</c> de la macro: una polilínea de dos vértices con
    /// <c>ConstantWidth</c> igual a <c>MAMPOSTERIA_ANCHO</c> —6 cm—, en su capa y su color.
    /// Es lo que distingue de un golpe de vista un muro de block de uno de concreto.
    /// </para>
    /// <para>
    /// Y se <b>separa de los extremos</b> <c>MAMPOSTERIA_GAP_M</c> —5 cm—, pero solo si el
    /// muro mide más de <c>MAMPOSTERIA_GAP_LARGO_MIN_M</c>. Sin esa condición, en un muro
    /// corto los dos huecos se comerían la línea entera.
    /// </para>
    /// </remarks>
    private bool LineaDeMamposteria(ElementoPlanta el, double x0, double y0)
    {
        if (!_cfg.Bandera("MAMPOSTERIA_LINEA", true))
        {
            return false;
        }

        if (!string.Equals(el.Material, "MAMPOSTERIA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < LargoMinimo)
        {
            return false;
        }

        var ancho = _cfg.Numero("MAMPOSTERIA_ANCHO", 0.06);

        if (ancho <= 0)
        {
            return false;
        }

        var gap = _cfg.Numero("MAMPOSTERIA_GAP_M", 0.05);
        var minimo = _cfg.Numero("MAMPOSTERIA_GAP_LARGO_MIN_M", 1);

        var ax = el.X1 + x0;
        var ay = el.Y1 + y0;
        var bx = el.X2 + x0;
        var by = el.Y2 + y0;

        if (gap > 0 && largo > minimo)
        {
            ax += dx / largo * gap;
            ay += dy / largo * gap;
            bx -= dx / largo * gap;
            by -= dy / largo * gap;
        }

        return PolilineaAncha(ax, ay, bx, by, _capas.Prefijo + "MAMPOSTERIA", ancho);
    }

    // =================================================================================
    //  ORDEN DE DIBUJO: LAS CAPAS QUE VAN ENCIMA DE TODO
    // =================================================================================

    /// <summary>
    /// Sube al frente las capas de <c>CAPAS_AL_FRENTE</c>: es <c>TraerCapasAlFrente</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se hace con la <b>tabla de orden de dibujo</b> del espacio modelo —<c>ACAD_SORTENTS</c>
    /// y su <c>MoveToTop</c>—, que es el <i>Bring to Front</i> de verdad. Y antes se pone
    /// <c>SORTENTS = 127</c>, que es lo que hace que AutoCAD respete ese orden en pantalla, al
    /// regenerar y al imprimir.
    /// </para>
    /// <para>
    /// La macro tiene un respaldo que copia y borra las entidades para que la copia quede al
    /// final. Aquí <b>no se hace</b>: eso les cambia el handle y rompe cualquier referencia
    /// externa —xrefs, campos, anotaciones asociativas—. Si la tabla no está disponible se
    /// avisa y el usuario lo resuelve con DRAWORDER, que es un clic.
    /// </para>
    /// </remarks>
    private void TraerCapasAlFrente()
    {
        if (!_cfg.Bandera("TRAER_AL_FRENTE", true))
        {
            return;
        }

        var capas = _capas.CapasAlFrente();

        if (capas.Count == 0)
        {
            return;
        }

        if (_cfg.Bandera("PONER_SORTENTS_127", true))
        {
            try
            {
                AcadConnection.Retry(() => { _doc.SetVariable("SORTENTS", 127); });
            }
            catch (Exception)
            {
                // Sin SORTENTS el orden se guarda igual; solo puede no verse en pantalla.
            }
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                var lista = new List<object>();

                foreach (var ent in _ms)
                {
                    string capa;

                    try
                    {
                        capa = ((dynamic)ent).Layer?.ToString()?.ToUpperInvariant() ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (capa.Length > 0 && capas.Contains(capa))
                    {
                        lista.Add(ent);
                    }
                }

                if (lista.Count == 0)
                {
                    return;
                }

                dynamic dict = _ms.GetExtensionDictionary;
                dynamic tabla;

                try
                {
                    tabla = dict.GetObject("ACAD_SORTENTS");
                }
                catch (Exception)
                {
                    tabla = dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable");
                }

                tabla.MoveToTop(lista.ToArray());
                _alFrente = lista.Count;
            });
        }
        catch (Exception)
        {
            Nota($"No se pudieron subir al frente las capas {string.Join(" + ", capas)}. " +
                 "Hazlo a mano con DRAWORDER (Bring to Front) si hace falta.");
        }
    }

    /// <summary>Cuántos objetos se subieron al frente, para el resumen.</summary>
    private int _alFrente;

    // =================================================================================
    //  PRIMITIVAS QUE SOLO USA ESTA PARTE
    // =================================================================================

    private object? Circulo(double cx, double cy, double r, string capa)
    {
        if (r <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _ms.AddCircle(new[] { cx, cy, 0d }, r);
                c.Layer = capa;
                return c;
            });
        }
        catch (Exception ex)
        {
            Fallo("Dibujar un círculo", ex);
            return null;
        }
    }

    /// <summary>Un texto de una línea, centrado en el punto.</summary>
    private object? TextoCentrado(double x, double y, string texto, double altura, string capa)
    {
        if (string.IsNullOrWhiteSpace(texto) || altura <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _ms.AddText(texto, new[] { x, y, 0d }, altura);
                t.Layer = capa;

                try
                {
                    // 10 = MiddleCenter, el que centra en los dos sentidos.
                    t.Alignment = 10;
                    t.TextAlignmentPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Queda anclado abajo a la izquierda: se lee igual.
                }

                return t;
            });
        }
        catch (Exception ex)
        {
            Fallo("Dibujar un texto", ex);
            return null;
        }
    }

    /// <summary>Un texto con estilo propio, opcionalmente alineado a la derecha.</summary>
    private object? TextoEstilo(
        double x, double y, string texto, double altura, string capa, string estilo,
        bool aDerecha)
    {
        if (string.IsNullOrWhiteSpace(texto) || altura <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _ms.AddText(texto, new[] { x, y, 0d }, altura);
                t.Layer = capa;

                if (estilo.Length > 0)
                {
                    try
                    {
                        t.StyleName = estilo;
                    }
                    catch (Exception)
                    {
                        // Sin el estilo sale con el del dibujo.
                    }
                }

                try
                {
                    // Se repone la altura DESPUÉS del estilo: si el estilo trae altura fija,
                    // manda el estilo, y si no, la que se pidió.
                    t.Height = altura;
                }
                catch (Exception)
                {
                    // Nada que hacer.
                }

                if (aDerecha)
                {
                    try
                    {
                        t.Alignment = 2;                 // 2 = Right
                        t.TextAlignmentPoint = new[] { x, y, 0d };
                    }
                    catch (Exception)
                    {
                        // Queda a la izquierda del punto.
                    }
                }

                return t;
            });
        }
        catch (Exception ex)
        {
            Fallo("Dibujar el rótulo", ex);
            return null;
        }
    }

    /// <summary>
    /// El ancho REAL de un texto ya dibujado; si no se puede medir, se estima.
    /// </summary>
    /// <remarks>
    /// Es el <c>AnchoDeTexto</c> de la macro. La estimación —0.55 de la altura por letra— es
    /// la suya, y es la que se usa cuando la fuente no está instalada y AutoCAD no devuelve
    /// la caja.
    /// </remarks>
    private double AnchoDeTexto(object? texto, string s, double altura)
    {
        var caja = CajaEnvolvente(texto);

        if (caja is { } c && c.Max[0] - c.Min[0] > 0)
        {
            return c.Max[0] - c.Min[0];
        }

        return s.Length * altura * 0.55;
    }

    /// <summary>
    /// La caja de una entidad, por <b>reflexión</b>.
    /// </summary>
    /// <remarks>
    /// <c>GetBoundingBox</c> devuelve sus dos resultados <b>por referencia</b> y el enlace
    /// dinámico no los sabe manejar sobre un objeto COM: llamarlo con <c>dynamic</c> falla.
    /// Está escrito igual en <c>SeccionDrawer</c> y en <c>ZapataDrawer</c>, y las dos veces
    /// se descubrió rompiendo algo.
    /// </remarks>
    private (double[] Min, double[] Max)? CajaEnvolvente(object? ent)
    {
        if (ent is null)
        {
            return null;
        }

        try
        {
            var args = new object?[] { null, null };

            var mod = new System.Reflection.ParameterModifier(2);
            mod[0] = true;
            mod[1] = true;

            ent.GetType().InvokeMember(
                "GetBoundingBox",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: ent,
                args: args,
                modifiers: new[] { mod },
                culture: null,
                namedParameters: null);

            var mn = ADobles(args[0]);
            var mx = ADobles(args[1]);

            return mn.Length < 2 || mx.Length < 2 ? null : (mn, mx);
        }
        catch (Exception)
        {
            // Sin medida se estima el ancho: el rótulo puede quedar algo corrido, pero está.
            return null;
        }
    }

    private static double[] ADobles(object? v) => v switch
    {
        double[] d => d,
        object[] o => o.Select(x => x is null ? 0d : Convert.ToDouble(x)).ToArray(),
        _ => Array.Empty<double>()
    };

    private void MoverTexto(object? texto, double x, double y)
    {
        if (texto is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                ((dynamic)texto).InsertionPoint = new[] { x, y, 0d };
            });
        }
        catch (Exception)
        {
            // Se queda donde estaba: el rótulo sale sin centrar.
        }
    }

    /// <summary>Polilínea de dos vértices con ancho constante: el muro de block.</summary>
    private bool PolilineaAncha(
        double x1, double y1, double x2, double y2, string capa, double ancho)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(new[] { x1, y1, x2, y2 });
                pl.Closed = false;
                pl.Layer = capa;

                try
                {
                    pl.ConstantWidth = ancho;
                }
                catch (Exception)
                {
                    pl.SetWidth(0, ancho, ancho);
                }

                return true;
            });
        }
        catch (Exception ex)
        {
            Fallo("Dibujar la línea de mampostería", ex);
            return false;
        }
    }

    /// <summary>
    /// Una cota alineada, con el estilo de la macro.
    /// </summary>
    /// <param name="extLinea">
    /// Cuánto se pasa la línea de extensión, <b>por objeto</b>. Negativo = la del estilo. Se
    /// usa en la cota TOTAL para que su línea no llegue hasta la burbuja del eje y se vea el
    /// aire que hay entre las dos.
    /// </param>
    private void CotaAlineada(
        double x1, double y1, double x2, double y2, double xt, double yt, string capa,
        double extLinea)
    {
        if (Math.Abs(x2 - x1) < LargoMinimo && Math.Abs(y2 - y1) < LargoMinimo)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(
                    new[] { x1, y1, 0d }, new[] { x2, y2, 0d }, new[] { xt, yt, 0d });

                d.Layer = capa;

                var estilo = _cfg.Texto("ESTILO_COTA", "COTA_DIM");

                if (estilo.Length > 0)
                {
                    try
                    {
                        d.StyleName = estilo;
                    }
                    catch (Exception)
                    {
                        // Sale con el estilo activo.
                    }
                }

                if (_cfg.Bandera("COTA_FORZAR_ALTURA", true))
                {
                    try
                    {
                        d.TextHeight = _cfg.Numero("COTA_TEXT_HEIGHT", 0.1);
                        d.ArrowheadSize = _cfg.Numero("COTA_ARROW_SIZE", 0.05);
                    }
                    catch (Exception)
                    {
                        // Se queda con lo del estilo.
                    }
                }

                if (extLinea >= 0)
                {
                    try
                    {
                        d.ExtensionLineExtend = extLinea;
                    }
                    catch (Exception)
                    {
                        // Alguna versión no lo expone por objeto.
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Fallo("Dibujar una cota de los ejes", ex);
        }
    }
}
