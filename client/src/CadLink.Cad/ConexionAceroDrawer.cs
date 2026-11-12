using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// Dibuja en AutoCAD el detalle de una conexion de acero leida del libro de Excel: su titulo, sus
/// vistas una junto a otra con sus llamadas y cotas, y abajo el cuadro de datos de la hoja.
/// </summary>
/// <remarks>
/// <para>
/// Tamaño real, como la placa base: <paramref name="escala"/> son unidades del dibujo por cm
/// (0.01 = el dibujo en metros). Los textos salen de «Estilo de dibujo» y estan pensados para
/// imprimir a 1:10; si el dibujo no esta en metros se escalan en la misma proporcion.
/// </para>
/// <para>
/// Como los demas dibujantes, cada llamada a AutoCAD va con su reintento y su try: una entidad que
/// AutoCAD rechaza se anota en <see cref="Fallos"/> y el detalle sigue.
/// </para>
/// </remarks>
public sealed class ConexionAceroDrawer
{
    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly double _escala;
    private readonly double _f;
    private readonly List<string> _fallos = new();
    private readonly List<string> _notas = new();

    public ConexionAceroDrawer(dynamic doc, double escala = 0.01)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => (object)doc.ModelSpace);
        _escala = escala > 0 ? escala : 0.01;
        _f = _escala / 0.01;
    }

    public IReadOnlyList<string> Fallos => _fallos;

    public IReadOnlyList<string> Notas => _notas;

    /// <summary>Lo que midio a lo ancho el ultimo detalle, en unidades del dibujo.</summary>
    public double UltimoAncho { get; private set; }

    private double HTexto => ConexionAceroCapas.AltoTexto * _f;

    // =====================================================================
    //  Capas y estilos
    // =====================================================================

    public void AsegurarCapas()
    {
        foreach (var c in Enum.GetValues<CapaConexion>())
        {
            Capa(c);
        }

        EstiloDeTexto();
        EstiloDeCota();
    }

    private void Capa(CapaConexion c)
    {
        var nombre = ConexionAceroCapas.Nombre(c);
        var color = ConexionAceroCapas.ColorDeCapa(c);
        var tipo = ConexionAceroCapas.TipoDeLinea(c);

        if (tipo is not null)
        {
            TipoDeLinea(tipo);
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic capas = _doc.Layers;
                dynamic capa;
                var nueva = false;

                try
                {
                    capa = capas.Item(nombre);
                }
                catch (Exception)
                {
                    capa = capas.Add(nombre);
                    nueva = true;
                }

                // Una capa nueva, o una cuyo color eligio el usuario en «Estilo de dibujo».
                if (nueva || ConexionAceroCapas.ColorElegido(c))
                {
                    capa.Color = color;
                }

                if (nueva && tipo is not null)
                {
                    try
                    {
                        capa.Linetype = tipo;
                    }
                    catch (Exception)
                    {
                        // Sin el tipo de linea cargado, la capa queda continua.
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Capa '{nombre}'", ex);
        }
    }

    private void TipoDeLinea(string nombre)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Linetypes.Item(nombre);
                }
                catch (Exception)
                {
                    try
                    {
                        _doc.Linetypes.Load(nombre, "acad.lin");
                    }
                    catch (Exception)
                    {
                        _doc.Linetypes.Load(nombre, "acadiso.lin");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Nota($"No se pudo cargar el tipo de linea {nombre}: las lineas ocultas o de eje salen continuas. ({ex.Message})");
        }
    }

    private void EstiloDeTexto()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(ConexionAceroCapas.EstiloTexto);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(ConexionAceroCapas.EstiloTexto);
                }

                estilo.SetFont(ConexionAceroCapas.Fuente, false, false, 0, 34);
                estilo.Width = 1.0;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Estilo de texto '{ConexionAceroCapas.EstiloTexto}'", ex);
        }
    }

    private void Variable(string nombre, object valor)
    {
        try
        {
            AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
        }
        catch (Exception ex)
        {
            Fallo($"Variable de cota {nombre}", ex);
        }
    }

    private void EstiloDeCota()
    {
        var alto = ConexionAceroCapas.AltoCota * _f;
        var marca = ConexionAceroCapas.TamanoMarcaCota * _f;
        var tipoMarca = ConexionAceroCapas.TipoMarca;

        Variable("DIMSCALE", 1.0);
        Variable("DIMTXT", alto);
        Variable("DIMASZ", marca);
        Variable("DIMEXE", marca);
        Variable("DIMEXO", marca / 2);
        Variable("DIMGAP", alto / 3);
        Variable("DIMTAD", 1);
        Variable("DIMTIH", 0);
        Variable("DIMTOH", 0);
        Variable("DIMATFIT", 3);
        Variable("DIMTOFL", 1);
        Variable("DIMCLRT", ConexionAceroCapas.ColorNumeroCota);
        Variable("DIMTXSTY", ConexionAceroCapas.EstiloTexto);
        Variable("DIMSAH", 1);
        Variable("DIMBLK", tipoMarca);
        Variable("DIMBLK1", tipoMarca);
        Variable("DIMBLK2", tipoMarca);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(ConexionAceroCapas.EstiloCota);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(ConexionAceroCapas.EstiloCota);
                }

                estilo.CopyFrom(_doc);
                _doc.ActiveDimStyle = estilo;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Estilo de cota '{ConexionAceroCapas.EstiloCota}'", ex);
        }
    }

    // =====================================================================
    //  El detalle
    // =====================================================================

    /// <summary>
    /// El acomodo de una vista: las llamadas se reparten en DOS columnas, la de la izquierda para
    /// las piezas de la mitad izquierda y la de la derecha para las otras. Asi las lineas no cruzan
    /// todo el detalle ni se enciman.
    /// </summary>
    private sealed record Acomodo(
        double Holgura, double AnchoGeo, double AltoGeo, double AnchoIzq, double AnchoDer,
        List<LlamadaConexion> Izquierda, List<LlamadaConexion> Derecha, double AltoColumnas)
    {
        public double Ancho => AnchoIzq + (2 * Holgura) + AnchoGeo + AnchoDer;
    }

    private double HSimbolo => ConexionAceroCapas.AltoSimbolo * _f;

    private Acomodo Acomodar(VistaConexionCad v)
    {
        var h = HTexto;
        var centro = (v.MinX + v.MaxX) / 2;
        var izq = v.Llamadas.Where(l => l.X < centro).OrderByDescending(l => l.Y).ToList();
        var der = v.Llamadas.Where(l => l.X >= centro).OrderByDescending(l => l.Y).ToList();

        // Que ninguna columna se cargue de mas: si una tiene el doble que la otra, pasan las mas cercanas al centro.
        while (izq.Count > der.Count + 2)
        {
            var l = izq.OrderByDescending(q => q.X).First();
            izq.Remove(l);
            der.Add(l);
        }

        while (der.Count > izq.Count + 2)
        {
            var l = der.OrderBy(q => q.X).First();
            der.Remove(l);
            izq.Add(l);
        }

        izq = izq.OrderByDescending(l => l.Y).ToList();
        der = der.OrderByDescending(l => l.Y).ToList();

        double Ancho(List<LlamadaConexion> lista) => lista.Count == 0
            ? 0
            : (4 * h) + lista.Max(l => l.Simbolo is null
                ? AnchoDeTexto(l.Texto, h)
                : SimbolosSoldadura.LargoApuntado(HSimbolo, l.Electrodo) + (0.6 * h) + AnchoDeTexto(l.Descripcion, h));

        double Alto(List<LlamadaConexion> lista) => lista.Sum(l => Paso(l));

        return new Acomodo(2 * h, (v.MaxX - v.MinX) * _escala, (v.MaxY - v.MinY) * _escala, Ancho(izq), Ancho(der),
            izq, der, Math.Max(Alto(izq), Alto(der)));
    }

    /// <summary>De renglon a renglon: el simbolo de soldadura pide mas que un texto.</summary>
    private double Paso(LlamadaConexion l) => l.Simbolo is null
        ? 1.9 * HTexto
        : Math.Max(1.9 * HTexto, SimbolosSoldadura.AltoApuntado(HSimbolo) + (0.5 * HTexto));

    private static double AnchoDeTexto(string texto, double h) => texto.Length * 0.62 * h;

    /// <summary>Lo que va a medir el detalle a lo ancho, en unidades del dibujo, sin dibujarlo.</summary>
    public double Medir(ConexionAceroCad c)
    {
        var h = HTexto;
        var margen = 4 * h;
        var vistas = c.Vistas.Select(Acomodar).Sum(a => a.Ancho + margen) - (c.Vistas.Count > 0 ? margen : 0);
        return Math.Max(vistas, AnchoDeTexto(c.Titulo, ConexionAceroCapas.AltoTitulo * _f));
    }

    /// <summary>
    /// Dibuja el detalle con su esquina de arriba a la izquierda en (<paramref name="x0"/>,
    /// <paramref name="y0"/>), en unidades del dibujo. Devuelve cuantas entidades se crearon.
    /// </summary>
    /// <remarks>
    /// El titulo, el nombre de cada vista y el cuadro de datos van CENTRADOS en el detalle.
    /// </remarks>
    public int Dibujar(ConexionAceroCad c, double x0, double y0)
    {
        var antes = Contar();
        var h = HTexto;
        var hTitulo = ConexionAceroCapas.AltoTitulo * _f;
        var hVista = ConexionAceroCapas.AltoVista * _f;
        var margen = 4 * h;
        var ancho = Medir(c);
        var centro = x0 + (ancho / 2);

        Texto(c.Titulo, centro, y0, hTitulo, CapaConexion.Texto, anclaje: 2);
        Texto(c.Subtitulo, centro, y0 - (1.7 * hTitulo), 0.8 * h, CapaConexion.Texto, anclaje: 2);

        var arriba = y0 - (1.7 * hTitulo) - (3 * h);
        var x = x0;
        var abajo = arriba;

        foreach (var v in c.Vistas)
        {
            var (anchoVista, alto) = Vista(v, x, arriba, hVista);
            x += anchoVista + margen;
            abajo = Math.Min(abajo, arriba - alto);
        }

        // El cuadro de datos y las notas, centrados bajo las vistas: un MTEXT por bloque, con sus
        // renglones centrados, para que se lean como una tabla y se muevan de una pieza.
        var y = abajo - (2 * h);
        var anchoBloque = Math.Min(Math.Max(ancho, 30 * h),
            Math.Max(c.Renglones.Concat(c.Notas).Select(t => AnchoDeTexto(t, h)).DefaultIfEmpty(0).Max() * 1.05, 30 * h));

        if (c.Renglones.Count > 0)
        {
            Texto("DATOS DE LA HOJA", centro, y, hVista, CapaConexion.Texto, anclaje: 2);
            y -= 1.8 * hVista;
            Bloque(c.Renglones, centro, y, h, anchoBloque);
            y -= Renglones(c.Renglones, h, anchoBloque) * 1.7 * h + h;
        }

        if (c.Notas.Count > 0)
        {
            Texto("NOTAS", centro, y, hVista, CapaConexion.Texto, anclaje: 2);
            y -= 1.8 * hVista;
            var notas = c.Notas.Select((n, i) => $"{i + 1}. {n}").ToList();
            Bloque(notas, centro, y, h, anchoBloque);
            y -= Renglones(notas, h, anchoBloque) * 1.7 * h;
        }

        UltimoAncho = Math.Max(ancho, anchoBloque);
        UltimoAlto = y0 - y;
        return Contar() - antes;
    }

    private static double Renglones(IEnumerable<string> textos, double h, double ancho) =>
        textos.Sum(t => Math.Ceiling(Math.Max(1, AnchoDeTexto(t, h) / ancho)));

    /// <summary>Lo que midio a lo alto el ultimo detalle.</summary>
    public double UltimoAlto { get; private set; }

    /// <summary>Una vista con su esquina de arriba a la izquierda en (x, y). Devuelve lo que ocupo.</summary>
    private (double Ancho, double Alto) Vista(VistaConexionCad v, double x, double y, double hVista)
    {
        var h = HTexto;
        var desfase = ConexionAceroCapas.DesfaseCota * _f;
        var a = Acomodar(v);
        var alto = Math.Max(a.AltoGeo, a.AltoColumnas) + (2 * a.Holgura);

        // El dibujo, centrado a lo alto en lo que ocupan sus llamadas.
        var xGeo = x + a.AnchoIzq + a.Holgura;
        var yGeo = y - ((alto - a.AltoGeo) / 2);
        double X(double cx) => xGeo + ((cx - v.MinX) * _escala);
        double Y(double cy) => yGeo - ((v.MaxY - cy) * _escala);

        foreach (var l in v.Lineas)
        {
            Linea(X(l.X1), Y(l.Y1), X(l.X2), Y(l.Y2), l.Capa);
        }

        foreach (var p in v.Contornos)
        {
            var puntos = p.Puntos.Select(q => (X(q.X), Y(q.Y))).ToList();
            var pl = Polilinea(puntos, p.Cerrado, p.Capa);

            // La soldadura se rellena con el hatch de la placa base, sobre su contorno.
            if (p.Relleno && pl is not null && p.Cerrado && puntos.Count >= 3)
            {
                Rellenar(pl, p.Capa, ConexionAceroCapas.PatronSoldadura, ConexionAceroCapas.EscalaSoldadura * _f);
            }
        }

        foreach (var c in v.Circulos)
        {
            Circulo(X(c.X), Y(c.Y), c.R * _escala, c.Capa);
        }

        foreach (var c in v.Cotas)
        {
            Cota(X(c.X1), Y(c.Y1), X(c.X2), Y(c.Y2), c.Nx * desfase, c.Ny * desfase, c.Texto);
        }

        // Las dos columnas de llamadas, centradas a lo alto en el dibujo.
        void Columna(List<LlamadaConexion> lista, double xCodo, bool derecha)
        {
            var total = lista.Sum(Paso);
            var yl = y - ((alto - total) / 2) - (Paso(lista.FirstOrDefault() ?? new LlamadaConexion(0, 0, "")) / 2);

            foreach (var l in lista)
            {
                var px = X(l.X);
                var py = Y(l.Y);

                if (l.Simbolo is { } tipo)
                {
                    var hs = HSimbolo;
                    var r = SimbolosSoldadura.Apuntado(tipo, px, py, xCodo, yl, hs, l.Tamano, l.Electrodo, derecha);
                    DibujarSimbolo(r);
                    var largo = SimbolosSoldadura.LargoApuntado(hs, l.Electrodo) + (0.6 * h);
                    Texto(l.Descripcion, derecha ? xCodo + largo : xCodo - largo, yl, h, CapaConexion.Texto, anclaje: derecha ? 4 : 6);
                }
                else
                {
                    var xt = derecha ? xCodo + (0.4 * h) : xCodo - (0.4 * h);
                    Linea(px, py, xCodo, yl, CapaConexion.Texto);
                    Circulo(px, py, 0.2 * h, CapaConexion.Texto);
                    Texto(l.Texto, xt, yl, h, CapaConexion.Texto, anclaje: derecha ? 4 : 6);
                }

                yl -= Paso(l);
            }
        }

        Columna(a.Izquierda, x + a.AnchoIzq - (2 * h), derecha: false);
        Columna(a.Derecha, xGeo + a.AnchoGeo + a.Holgura + (2 * h), derecha: true);

        Texto(v.Titulo, xGeo + (a.AnchoGeo / 2), y - alto - (0.4 * hVista), hVista, CapaConexion.Texto, anclaje: 2);
        return (a.Ancho, alto + (2.2 * hVista));
    }

    /// <summary>Un renglon de la simbologia: leader, simbolo, rellenos, circulo y textos.</summary>
    private void DibujarSimbolo(SimbolosSoldadura.Renglon r)
    {
        var capa = CapaConexion.Soldadura;
        Polilinea(Pares(r.Leader), false, capa);

        foreach (var a in r.Abiertas)
        {
            Polilinea(Pares(a), false, capa);
        }

        foreach (var c in r.Cerradas)
        {
            Polilinea(Pares(c), true, capa);
        }

        foreach (var f in r.Rellenas)
        {
            if (Polilinea(Pares(f), true, capa) is { } pl)
            {
                // Las flechas y la bandera van llenas: el SOLID de siempre.
                Rellenar(pl, capa, "SOLID", 1.0);
            }
        }

        if (r.Circulo is { } c0)
        {
            Circulo(c0.X, c0.Y, c0.R, capa);
        }

        foreach (var t in r.Textos)
        {
            // Los anclajes del TEXT de la simbologia, como MTEXT: 9 izquierda, 10 centro, 11 derecha.
            var anclaje = t.Anclaje switch { 10 => 5, 11 => 6, _ => 4 };
            Texto(t.S, t.X, t.Y, t.Altura, CapaConexion.Texto, anclaje);
        }
    }

    private static List<(double X, double Y)> Pares(double[] a)
    {
        var l = new List<(double, double)>();

        for (var i = 0; i + 1 < a.Length; i += 2)
        {
            l.Add((a[i], a[i + 1]));
        }

        return l;
    }

    // =====================================================================
    //  Entidades
    // =====================================================================

    private static double[] P(double x, double y) => new[] { x, y, 0.0 };

    private int Contar()
    {
        try
        {
            return AcadConnection.Retry(() => (int)_ms.Count);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void Capear(dynamic e, CapaConexion capa)
    {
        e.Layer = ConexionAceroCapas.Nombre(capa);

        if (ConexionAceroCapas.TipoDeLinea(capa) is not null)
        {
            e.LinetypeScale = ConexionAceroCapas.EscalaLineas * _f;
        }
    }

    private void Linea(double x1, double y1, double x2, double y2, CapaConexion capa)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = _ms.AddLine(P(x1, y1), P(x2, y2));
                Capear(l, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Linea", ex);
        }
    }

    private object? Polilinea(IReadOnlyList<(double X, double Y)> puntos, bool cerrada, CapaConexion capa)
    {
        if (puntos.Count < 2)
        {
            return null;
        }

        var arreglo = new double[puntos.Count * 2];

        for (var i = 0; i < puntos.Count; i++)
        {
            arreglo[2 * i] = puntos[i].X;
            arreglo[(2 * i) + 1] = puntos[i].Y;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(arreglo);
                pl.Closed = cerrada && puntos.Count >= 3;
                Capear(pl, capa);
                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Polilinea", ex);
            return null;
        }
    }

    private void Rellenar(object contorno, CapaConexion capa, string patron, double escala)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic hh = _ms.AddHatch(0, patron, false);

                var ok = AcadArreglos.Llamar($"AppendOuterLoop del hatch '{patron}'", new[] { contorno },
                    arr => { hh.AppendOuterLoop(arr); }, Fallo, Nota);

                if (!ok)
                {
                    try
                    {
                        hh.Delete();
                    }
                    catch (Exception)
                    {
                        // Ya no estaba.
                    }

                    return;
                }

                if (!string.Equals(patron, "SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    hh.PatternScale = escala;
                }

                hh.Layer = ConexionAceroCapas.Nombre(capa);
                hh.Evaluate();
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}'", ex);
        }
    }

    private void Circulo(double x, double y, double r, CapaConexion capa)
    {
        if (r <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic c = _ms.AddCircle(P(x, y), r);
                Capear(c, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Circulo", ex);
        }
    }

    /// <summary>
    /// Un MTEXT en (x, y) con su punto de anclaje: 1 arriba a la izquierda, 2 arriba al centro, 4 al
    /// medio a la izquierda, 5 al centro, 6 al medio a la derecha.
    /// </summary>
    private void Texto(string texto, double x, double y, double alto, CapaConexion capa, int anclaje = 1)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic t = _ms.AddMText(P(x, y), 0.0, Escapar(texto));
                t.AttachmentPoint = anclaje;
                t.InsertionPoint = P(x, y);
                t.StyleName = ConexionAceroCapas.EstiloTexto;
                t.Height = alto;
                Capear(t, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Texto", ex);
        }
    }

    /// <summary>
    /// Varios renglones en UN MTEXT de <paramref name="ancho"/>, JUSTIFICADO: cada renglon es un
    /// parrafo (\P) y el texto se reparte a los dos margenes.
    /// </summary>
    private void Bloque(IReadOnlyList<string> renglones, double x, double y, double alto, double ancho)
    {
        if (renglones.Count == 0)
        {
            return;
        }

        var contenido = "\\pqc;" + string.Join("\\P", renglones.Select(Escapar));

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic t = _ms.AddMText(P(x, y), ancho, contenido);
                t.AttachmentPoint = 2;
                t.InsertionPoint = P(x, y);
                t.StyleName = ConexionAceroCapas.EstiloTexto;
                t.Height = alto;
                Capear(t, CapaConexion.Texto);
            });
        }
        catch (Exception ex)
        {
            Fallo("Cuadro de datos", ex);
        }
    }

    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

    private void Cota(double x1, double y1, double x2, double y2, double dx, double dy, string texto)
    {
        if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 1e-9)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(P(x1, y1), P(x2, y2), P(((x1 + x2) / 2) + dx, ((y1 + y2) / 2) + dy));
                d.Layer = ConexionAceroCapas.Nombre(CapaConexion.Cota);
                d.StyleName = ConexionAceroCapas.EstiloCota;

                // El numero es el de la hoja, en cm: no depende de las unidades del dibujo.
                d.TextOverride = texto;
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota", ex);
        }
    }

    // =====================================================================

    private void Fallo(string operacion, Exception ex)
    {
        var e = ex;

        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var linea = operacion + " -> " + e.GetType().Name + ": " + e.Message.Replace(Environment.NewLine, " ").Trim();

        if (!_fallos.Contains(linea))
        {
            _fallos.Add(linea);
        }
    }

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }
}
