namespace CadLink.Cad;

/// <summary>
/// Crea un <b>layout por plano</b> con su papel, su cajetín y sus atributos rellenados.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>GenerarSolapas</c>. Lo que decide —qué texto va en cada atributo, qué papel le toca a
/// cada hoja, cómo se llama el layout— lo hace <see cref="Solapas"/>, que no toca COM. Aquí queda
/// solo el diálogo con AutoCAD.
/// </para>
/// <para>
/// El modo es el <b>CENTRADO</b> de la macro, que es el que ella misma recomienda: se inserta el
/// bloque, se <b>mide</b> con <c>GetBoundingBox</c>, se escala sobre su propio centro y se lleva ese
/// centro al centro del área imprimible. No supone nada del punto base del bloque, así que funciona
/// esté donde esté. Los otros cuatro modos de la macro —MARCO, ESTIRAR, UNIFORME, DINÁMICO— no se
/// portaron: ver la nota de <see cref="Solapas.EscalaParaCaber"/> y el resumen del commit.
/// </para>
/// </remarks>
public sealed class SolapasDrawer
{
    private readonly dynamic _doc;

    /// <summary>Lo que pasó, plano por plano. Es lo que se le enseña al usuario al terminar.</summary>
    public List<string> Notas { get; } = new();

    /// <summary>El primer layout que se creó, para dejarlo a la vista al acabar.</summary>
    public string PrimerLayout { get; private set; } = string.Empty;

    public SolapasDrawer(dynamic documento) => _doc = documento;

    /// <summary>El nombre del bloque del cajetín que se va a usar.</summary>
    public string Cajetin { get; set; } = "CAJETIN";

    /// <summary>El dispositivo de ploteo. Del él sale la lista de papeles.</summary>
    public string Dispositivo { get; set; } = "DWG To PDF.pc3";

    /// <summary>Reemplaza el layout si ya existe, en lugar de crear uno con consecutivo.</summary>
    public bool Sobrescribir { get; set; } = true;

    /// <summary>Margen libre alrededor del cajetín al encajarlo, en mm.</summary>
    public double Margen { get; set; }

    // La lista de papeles depende SOLO del dispositivo, y pedirla consulta el driver: es lo más
    // lento de toda la corrida. Se pide una vez.
    private List<string>? _papeles;

    /// <summary>
    /// Busca en el dibujo el bloque que <b>más atributos del cajetín</b> tenga.
    /// </summary>
    /// <remarks>
    /// Port de <c>BuscarCajetinAuto</c>. Existe porque el nombre del bloque no se puede dar por
    /// sabido: cada despacho llama al suyo de una manera. Se piden <b>al menos tres</b> atributos
    /// conocidos para no confundirlo con un bloque de cotas o una marca de nivel, que también
    /// llevan atributos.
    /// </remarks>
    public string? BuscarCajetin(out int cuantos)
    {
        var mejor = 0;
        string? nombre = null;

        try
        {
            AcadConnection.Retry(() =>
            {
                foreach (dynamic blk in _doc.Blocks)
                {
                    string n = blk.Name;

                    if (n.StartsWith("*", StringComparison.Ordinal) || (bool)blk.IsLayout)
                    {
                        continue;
                    }

                    var k = 0;

                    foreach (dynamic ent in blk)
                    {
                        if ((string)ent.ObjectName == "AcDbAttributeDefinition"
                            && Solapas.EsTagConocido((string)ent.TagString))
                        {
                            k++;
                        }
                    }

                    if (k > mejor)
                    {
                        mejor = k;
                        nombre = n;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add("No se pudo revisar los bloques del dibujo: " + ex.Message);
        }

        cuantos = mejor;

        return mejor >= 3 ? nombre : null;
    }

    /// <summary>¿El dibujo ya tiene la definición de este bloque?</summary>
    public bool ExisteBloque(string nombre)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                foreach (dynamic blk in _doc.Blocks)
                {
                    if (string.Equals((string)blk.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// El layout de un plano, con su papel, su cajetín y sus atributos.
    /// </summary>
    /// <returns>El nombre del layout, o cadena vacía si no se pudo crear.</returns>
    public string Dibujar(SolapaCad s)
    {
        if (s.Falta.Count > 0)
        {
            Notas.Add($"{Etiqueta(s)}: falta {string.Join("; ", s.Falta)}. No se generó.");

            return string.Empty;
        }

        var nombre = Solapas.NombreLibre(Solapas.NombreDeLayout(s), NombresDeLayout(), Sobrescribir);

        // OBJECT Y NO DYNAMIC, y esto no es estilo. Un argumento dynamic vuelve DINÁMICA LA
        // LLAMADA ENTERA, así que el resultado deja de ser el tipo declarado y pasa a ser dynamic:
        // AreaImprimible ya no devolvía su tupla y «var (x0, y0, x1, y1) = area» no compilaba
        // —CS8133, no se pueden deconstruir los objetos dinámicos—. El dynamic se queda DENTRO de
        // cada método, que es donde hace falta, y las fronteras van tipadas. Es lo que ya hacían
        // los otros dibujantes de este proyecto.
        object? lay = CrearLayout(nombre);

        if (lay is null)
        {
            return string.Empty;
        }

        if (PrimerLayout.Length == 0)
        {
            PrimerLayout = nombre;
        }

        var (w, h) = Solapas.HojaOrientada(s);

        // ---------- EL PAPEL ----------
        var papel = AsignarPapel(lay, s, w, h);

        // ---------- EL ÁREA DONDE SE DIBUJA ----------
        // El área imprimible real, que es el recuadro de rayitas que se ve en pantalla. Si no se
        // puede leer, la medida teórica desde el origen.
        var area = AreaImprimible(lay) ?? (0.0, 0.0, w, h);

        var (x0, y0, x1, y1) = area;

        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;

        // ---------- EL CAJETÍN ----------
        // Se dibuja en lay.Block —el espacio papel de ESTE layout— y no en doc.PaperSpace: el
        // segundo depende de cuál esté activo en AutoCAD, así que el cajetín podía acabar en el
        // layout anterior.
        object? br = Insertar(lay, cx, cy);

        var nAtt = 0;
        var escala = 1.0;

        if (br is not null)
        {
            escala = EncajarYCentrar(br, cx, cy, x1 - x0, y1 - y0);
            nAtt = RellenarAtributos(br, s);
        }

        Notas.Add(
            $"{nombre}: {s.Tamano} {(s.Horizontal ? "horizontal" : "vertical")} " +
            $"({w:0}x{h:0} mm), papel {(papel.Length == 0 ? "NO ASIGNADO" : papel)}, " +
            $"{nAtt} atributos" +
            (Math.Abs(escala - 1) > 1e-6 ? $", escala {escala:0.0000}" : string.Empty));

        return nombre;
    }

    // ======================================================================
    //  EL PAPEL
    // ======================================================================

    /// <summary>
    /// Le pone el papel al layout: primero por <b>configuración de página</b>, y si no, buscándolo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La configuración de página va primero porque ahí el usuario controla dispositivo, tabla de
    /// plumillas, márgenes y escala <b>desde AutoCAD</b>, que es donde sabe hacerlo. Pero no se
    /// confía a ciegas: se comprueba que de verdad traiga el papel que se pidió. Una configuración
    /// creada antes con el papel equivocado es peor que ninguna, porque parece correcta.
    /// </para>
    /// <para>
    /// Y si el pliego exacto no existe en el dispositivo, se avisa. Es lo que la propia macro llama
    /// la causa número uno de que la orientación salga mal: AutoCAD no da error, deja Carta vertical
    /// y el plano entero sale descuadrado.
    /// </para>
    /// </remarks>
    private string AsignarPapel(object lay, SolapaCad s, double w, double h)
    {
        var porConfig = AplicarConfigDePagina(lay, s);

        if (porConfig.Length > 0 && PapelDelLayoutCoincide(lay, w, h))
        {
            return "[config] " + porConfig;
        }

        var elegido = Solapas.BuscarPapel(Papeles(lay), s);

        if (elegido is null)
        {
            Notas.Add(
                $"{Etiqueta(s)}: el tamaño «{s.Tamano}» ({w:0}x{h:0} mm) no existe en " +
                $"{Dispositivo}, así que AutoCAD va a dejar el papel por omisión y la hoja se va " +
                "a ver descuadrada. Créalo como tamaño personalizado en PLOTTERMANAGER.");

            return string.Empty;
        }

        var p = elegido.Value;

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                l.CanonicalMediaName = p.Nombre;
                l.PaperUnits = 1;                // acMillimeters
                l.PlotType = 1;                  // acExtents
                l.UseStandardScale = false;
                l.SetCustomScale(1, 1);
                l.CenterPlot = true;

                if (p.Rotacion >= 0)
                {
                    l.PlotRotation = p.Rotacion;
                }

                l.RefreshPlotDeviceInfo();
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"{Etiqueta(s)}: no se pudo asignar el papel «{p.Nombre}»: {ex.Message}");

            return string.Empty;
        }

        if (!p.Cabe)
        {
            Notas.Add(
                $"{Etiqueta(s)}: el tamaño «{s.Tamano}» no existe en {Dispositivo}. Se usó el " +
                $"pliego más chico donde cabe, «{Solapas.NombreCortoDelPapel(p.Nombre)}», así que " +
                "el plano va a quedar con más margen del previsto.");
        }

        return Solapas.NombreCortoDelPapel(p.Nombre);
    }

    private string AplicarConfigDePagina(object lay, SolapaCad s)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                foreach (dynamic cfg in _doc.PlotConfigurations)
                {
                    if (!Solapas.ConfigPaginaSirve(s, (string)cfg.Name))
                    {
                        continue;
                    }

                    l.CopyFrom(cfg);

                    return (string)cfg.Name;
                }

                return string.Empty;
            });
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private bool PapelDelLayoutCoincide(object lay, double w, double h)
    {
        var papel = MedidaDelPapel(lay);

        return papel is not null && Solapas.PapelCoincide(papel.Value.Ancho, papel.Value.Alto, w, h);
    }

    /// <summary>La medida del papel del layout, en mm.</summary>
    private (double Ancho, double Alto)? MedidaDelPapel(object lay)
    {
        try
        {
            return AcadConnection.Retry<(double, double)?>(() =>
            {
                var r = PorReferencia(lay, "GetPaperSize");

                if (r is null)
                {
                    return null;
                }

                var pw = Numero(r[0]) ?? 0;
                var ph = Numero(r[1]) ?? 0;

                if (pw <= 0 || ph <= 0)
                {
                    return null;
                }

                // 0 = pulgadas, 1 = mm. Sin convertir, una plantilla en pulgadas hace que todas las
                // comparaciones de medida fallen y ningún papel «coincide».
                var k = (int)((dynamic)lay).PaperUnits == 0 ? 25.4 : 1.0;

                return (pw * k, ph * k);
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ======================================================================
    //  LAS LLAMADAS COM QUE DEVUELVEN POR REFERENCIA
    // ======================================================================
    //
    //  GetPaperSize y GetPaperMargins entregan sus resultados en parámetros de SALIDA, y por
    //  enlace tardío eso no se puede pedir con «dynamic» y «out»: hay que llamar por
    //  reflexión y marcar los dos argumentos como por referencia. Es exactamente lo que ya
    //  hacía este proyecto para GetBoundingBox —ver Caja, más abajo—, así que aquí se usa la
    //  misma vía en lugar de abrir una segunda.

    /// <summary>Invoca un método COM con <b>dos argumentos de salida</b> y devuelve los dos.</summary>
    private static object?[]? PorReferencia(object objeto, string metodo)
    {
        try
        {
            var args = new object?[] { null, null };

            objeto.GetType().InvokeMember(
                metodo,
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: objeto, args: args,
                modifiers: new[]
                {
                    new System.Reflection.ParameterModifier(2) { [0] = true, [1] = true },
                },
                culture: null, namedParameters: null);

            return args;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Un número que llegó como variante de COM, o <c>null</c> si no lo era.</summary>
    private static double? Numero(object? v)
    {
        try
        {
            return v is null
                ? null
                : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<string> Papeles(object lay)
    {
        if (_papeles is not null)
        {
            return _papeles;
        }

        var salida = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                l.ConfigName = Dispositivo;
                l.RefreshPlotDeviceInfo();

                salida.Clear();

                // DOS FORMAS, y la macro las prueba igual: según la versión de AutoCAD,
                // GetCanonicalMediaNames devuelve la lista o la entrega en un parámetro de salida.
                // Con una sola, en la mitad de las versiones no hay lista y ningún papel se
                // encuentra: justo el fallo que deja el plano en Carta vertical sin avisar.
                object? nombres = null;

                try
                {
                    nombres = l.GetCanonicalMediaNames();
                }
                catch (Exception)
                {
                    nombres = null;
                }

                var sirve = nombres is System.Collections.IEnumerable and not string;

                if (!sirve)
                {
                    var args = new object?[] { null };

                    try
                    {
                        lay.GetType().InvokeMember(
                            "GetCanonicalMediaNames",
                            System.Reflection.BindingFlags.InvokeMethod,
                            binder: null, target: lay, args: args,
                            modifiers: new[]
                            {
                                new System.Reflection.ParameterModifier(1) { [0] = true },
                            },
                            culture: null, namedParameters: null);

                        nombres = args[0];
                    }
                    catch (Exception)
                    {
                        // Se queda con lo que trajera la primera forma.
                    }
                }

                if (nombres is System.Collections.IEnumerable lista and not string)
                {
                    foreach (var n in lista)
                    {
                        var t = Convert.ToString(
                            n, System.Globalization.CultureInfo.InvariantCulture);

                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            salida.Add(t);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add(
                $"No se pudo leer la lista de papeles de {Dispositivo}: {ex.Message}. " +
                "Revisa que ese dispositivo exista en PLOTTERMANAGER.");
        }

        _papeles = salida;

        return salida;
    }

    /// <summary>
    /// El área <b>imprimible</b> del layout: el recuadro de rayitas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se calcula del tamaño de papel menos los márgenes del dispositivo, y no de
    /// <c>LIMMIN</c>/<c>LIMMAX</c>. La macro usaba las dos fuentes y validaba una contra la otra, y
    /// documenta por qué: un layout <b>recién creado</b> todavía no tiene sus límites calculados y
    /// <c>LIMMIN</c> devuelve los del layout anterior, así que la solapa se centraba sobre el área de
    /// otra hoja.
    /// </para>
    /// <para>
    /// Aquí se usa solo la fuente fiable. Da la medida pero no la posición, así que el área se toma
    /// desde el origen: para <b>centrar</b> el cajetín eso es exactamente igual de bueno, y no
    /// depende de activar el layout ni de regenerar el dibujo.
    /// </para>
    /// </remarks>
    private (double X0, double Y0, double X1, double Y1)? AreaImprimible(object lay)
    {
        try
        {
            return AcadConnection.Retry<(double, double, double, double)?>(() =>
            {
                var papel = PorReferencia(lay, "GetPaperSize");

                if (papel is null)
                {
                    return null;
                }

                var pw = Numero(papel[0]) ?? 0;
                var ph = Numero(papel[1]) ?? 0;

                if (pw <= 0 || ph <= 0)
                {
                    return null;
                }

                // GetPaperMargins entrega DOS PUNTOS por referencia: el de abajo-izquierda y el de
                // arriba-derecha. Si no se pueden leer, se usa el papel entero: es un área un pelo
                // mayor, y centrar en ella deja el cajetín igual de centrado.
                var m = ComoNumeros(PorReferencia(lay, "GetPaperMargins"));

                var mIzq = m.Count > 0 ? m[0] : 0;
                var mInf = m.Count > 1 ? m[1] : 0;
                var mDer = m.Count > 3 ? m[3] : 0;
                var mSup = m.Count > 4 ? m[4] : 0;

                dynamic l = lay;

                var k = (int)l.PaperUnits == 0 ? 25.4 : 1.0;

                var w = (pw - (mIzq + mDer)) * k;
                var h = (ph - (mInf + mSup)) * k;

                // La hoja girada intercambia los lados del área imprimible.
                var rot = (int)l.PlotRotation;

                if (rot == 1 || rot == 3)
                {
                    (w, h) = (h, w);
                }

                return w > 1 && h > 1 ? (0.0, 0.0, w, h) : null;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Aplana en una lista de números lo que devolvió una llamada por referencia.
    /// </summary>
    /// <remarks>
    /// Los dos puntos de <c>GetPaperMargins</c> llegan como dos arreglos de tres, así que hay que
    /// aplanar: quedan <c>[izq, inf, 0, der, sup, 0]</c>. Lo que no sea número se salta en lugar de
    /// reventar, porque de esto solo se saca un margen y sin él se usa el papel entero.
    /// </remarks>
    private static List<double> ComoNumeros(object?[]? args)
    {
        var salida = new List<double>();

        if (args is null)
        {
            return salida;
        }

        foreach (var v in args)
        {
            if (v is System.Collections.IEnumerable anidada and not string)
            {
                foreach (var y in anidada)
                {
                    if (Numero(y) is { } n)
                    {
                        salida.Add(n);
                    }
                }
            }
            else if (Numero(v) is { } m)
            {
                salida.Add(m);
            }
        }

        return salida;
    }

    // ======================================================================
    //  EL LAYOUT Y EL BLOQUE
    // ======================================================================

    private List<string> NombresDeLayout()
    {
        var salida = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                salida.Clear();

                foreach (dynamic lo in _doc.Layouts)
                {
                    salida.Add((string)lo.Name);
                }
            });
        }
        catch (Exception)
        {
            // Sin la lista, NombreLibre no puede evitar el choque. Se sigue: Layouts.Add da su
            // propio error y el plano se reporta como no generado.
        }

        return salida;
    }

    private object? CrearLayout(string nombre)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                if (Sobrescribir)
                {
                    foreach (dynamic lo in _doc.Layouts)
                    {
                        if (string.Equals((string)lo.Name, nombre, StringComparison.OrdinalIgnoreCase))
                        {
                            lo.Delete();
                            break;
                        }
                    }
                }

                return (object?)_doc.Layouts.Add(nombre);
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo crear el layout «{nombre}»: {ex.Message}");

            return null;
        }
    }

    private object? Insertar(object lay, double x, double y)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
                (object?)((dynamic)lay).Block.InsertBlock(
                    new[] { x, y, 0.0 }, Cajetin, 1.0, 1.0, 1.0, 0.0));
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo insertar el cajetín «{Cajetin}»: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Mide el bloque insertado, lo escala sin deformarlo y lleva su centro al del área.
    /// </summary>
    /// <remarks>
    /// Se mide con <c>GetBoundingBox</c>, o sea la medida <b>real de lo que se dibujó</b>. Por eso
    /// funciona sin importar dónde esté el punto base del bloque —al centro, en una esquina o fuera
    /// del dibujo— ni para qué tamaño de hoja se dibujó originalmente. Es lo que la macro llama
    /// «una sola ruta, sin suposiciones».
    /// </remarks>
    private double EncajarYCentrar(object br, double cx, double cy, double w, double h)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic b = br;

                var caja = Caja(br);

                if (caja is null)
                {
                    return 1.0;
                }

                var (bx0, by0, bx1, by1) = caja.Value;

                var s = Solapas.EscalaParaCaber(bx1 - bx0, by1 - by0, w, h, Margen);

                if (Math.Abs(s - 1) > 5e-4)
                {
                    // La base del escalado es el centro de la propia caja, así que el bloque no se
                    // mueve de sitio al escalar y el centrado de abajo es una sola resta.
                    b.ScaleEntity(
                        new[] { (bx0 + bx1) / 2, (by0 + by1) / 2, 0.0 }, s);

                    caja = Caja(br);

                    if (caja is null)
                    {
                        return s;
                    }

                    (bx0, by0, bx1, by1) = caja.Value;
                }

                b.Move(
                    new[] { 0.0, 0.0, 0.0 },
                    new[] { cx - ((bx0 + bx1) / 2), cy - ((by0 + by1) / 2), 0.0 });

                return s;
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo encajar el cajetín en la hoja: {ex.Message}");

            return 1;
        }
    }

    private static (double X0, double Y0, double X1, double Y1)? Caja(object ent)
    {
        try
        {
            object? min = null;
            object? max = null;

            var args = new object?[] { min, max };

            ent.GetType().InvokeMember(
                "GetBoundingBox",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: ent, args: args,
                modifiers: new[]
                {
                    new System.Reflection.ParameterModifier(2) { [0] = true, [1] = true },
                },
                culture: null, namedParameters: null);

            if (args[0] is not double[] a || args[1] is not double[] b)
            {
                return null;
            }

            return (a[0], a[1], b[0], b[1]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Escribe los datos en los atributos del cajetín. Devuelve cuántos se llenaron.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los atributos que <see cref="Solapas.TextoDeAtributo"/> no reconoce <b>no se tocan</b>: puede
    /// haber en el cajetín datos que el dibujante pone a mano, y borrárselos en cada corrida sería
    /// peor que no llenar nada.
    /// </para>
    /// <para>
    /// Y a los que no están alineados a la izquierda se les reasigna el punto de alineación. Es de la
    /// macro y es un defecto conocido de COM: al cambiar el texto de un atributo centrado o alineado
    /// a la derecha, AutoCAD no recalcula su posición y el rótulo queda descolocado dentro de su
    /// recuadro. Reasignar el punto lo obliga.
    /// </para>
    /// </remarks>
    private int RellenarAtributos(object br, SolapaCad s)
    {
        var n = 0;

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic b = br;

                n = 0;

                if (!(bool)b.HasAttributes)
                {
                    return;
                }

                foreach (dynamic att in b.GetAttributes())
                {
                    var texto = Solapas.TextoDeAtributo(s, (string)att.TagString);

                    if (texto is null)
                    {
                        continue;
                    }

                    att.TextString = Solapas.Formatear(texto);

                    if ((int)att.Alignment != 0)
                    {
                        att.TextAlignmentPoint = att.TextAlignmentPoint;
                    }

                    n++;
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"{Etiqueta(s)}: no se pudieron llenar todos los atributos: {ex.Message}");
        }

        return n;
    }

    /// <summary>Deja a la vista el primer layout generado.</summary>
    public void MostrarPrimerLayout()
    {
        if (PrimerLayout.Length == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                _doc.ActiveSpace = 0;                  // acPaperSpace
                _doc.ActiveLayout = _doc.Layouts.Item(PrimerLayout);
                _doc.Regen(1);                         // acAllViewports
            });
        }
        catch (Exception)
        {
            // Que no se vea el layout no invalida nada de lo dibujado.
        }
    }

    private static string Etiqueta(SolapaCad s)
    {
        var clave = s.Clave.Trim();

        if (clave.Length > 0)
        {
            return clave;
        }

        var titulo = s.Titulo.Trim();

        return titulo.Length > 0 ? titulo : "plano sin clave";
    }
}
