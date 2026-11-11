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
    /// Dibuja el detalle con su esquina de arriba a la izquierda en (<paramref name="x0"/>,
    /// <paramref name="y0"/>), en unidades del dibujo. Devuelve cuantas entidades se crearon.
    /// </summary>
    public int Dibujar(ConexionAceroCad c, double x0, double y0)
    {
        var antes = Contar();
        var h = HTexto;
        var hTitulo = ConexionAceroCapas.AltoTitulo * _f;
        var hVista = ConexionAceroCapas.AltoVista * _f;
        var margen = 6 * h;

        Texto(c.Titulo, x0, y0, hTitulo, CapaConexion.Texto);
        Texto(c.Subtitulo, x0, y0 - (1.6 * hTitulo), h, CapaConexion.Texto);

        var arriba = y0 - (1.6 * hTitulo) - (4 * h);
        var x = x0;
        var abajo = arriba;

        foreach (var v in c.Vistas)
        {
            var (ancho, alto) = Vista(v, x, arriba, hVista);
            x += ancho + margen;
            abajo = Math.Min(abajo, arriba - alto);
        }

        // El cuadro de datos y las notas, bajo las vistas.
        var y = abajo - (3 * h);

        if (c.Renglones.Count > 0)
        {
            Texto("DATOS DE LA HOJA", x0, y, hVista, CapaConexion.Texto);
            y -= 2 * hVista;

            foreach (var r in c.Renglones)
            {
                Texto(r, x0, y, h, CapaConexion.Texto);
                y -= 1.7 * h;
            }
        }

        if (c.Notas.Count > 0)
        {
            y -= h;
            Texto("NOTAS", x0, y, hVista, CapaConexion.Texto);
            y -= 2 * hVista;

            for (var i = 0; i < c.Notas.Count; i++)
            {
                Texto($"{i + 1}. {c.Notas[i]}", x0, y, h, CapaConexion.Texto);
                y -= 1.7 * h;
            }
        }

        var largoTextos = c.Renglones.Concat(c.Notas).Select(t => t.Length).DefaultIfEmpty(0).Max() * 0.62 * h;
        UltimoAncho = Math.Max(x - x0 - margen, Math.Max(largoTextos, c.Titulo.Length * 0.62 * hTitulo));
        UltimoAlto = y0 - y;
        return Contar() - antes;
    }

    /// <summary>Lo que midio a lo alto el ultimo detalle.</summary>
    public double UltimoAlto { get; private set; }

    /// <summary>Una vista con su esquina de arriba a la izquierda en (x, y). Devuelve lo que ocupo.</summary>
    private (double Ancho, double Alto) Vista(VistaConexionCad v, double x, double y, double hVista)
    {
        var h = HTexto;
        var desfase = ConexionAceroCapas.DesfaseCota * _f;

        // De cm del papel de la vista a unidades del dibujo, con su esquina en (x + holgura, y).
        var holgura = 4 * h;
        double X(double cx) => x + holgura + ((cx - v.MinX) * _escala);
        double Y(double cy) => y - holgura - ((v.MaxY - cy) * _escala);

        var anchoGeo = (v.MaxX - v.MinX) * _escala;
        var altoGeo = (v.MaxY - v.MinY) * _escala;

        foreach (var l in v.Lineas)
        {
            Linea(X(l.X1), Y(l.Y1), X(l.X2), Y(l.Y2), l.Capa);
        }

        foreach (var p in v.Contornos)
        {
            var puntos = p.Puntos.Select(q => (X(q.X), Y(q.Y))).ToList();
            var pl = Polilinea(puntos, p.Cerrado, p.Capa);

            if (p.Relleno && pl is not null && p.Cerrado && puntos.Count >= 3)
            {
                Rellenar(pl, p.Capa);
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

        // Las llamadas: una columna de textos a la derecha de la vista, de arriba abajo, cada una
        // con su linea al punto de la pieza.
        var xTexto = x + holgura + anchoGeo + (6 * h);
        var paso = 2.2 * h;
        var orden = v.Llamadas.OrderByDescending(l => l.Y).ToList();
        var yTexto = y - holgura - Math.Max(0, (altoGeo - (orden.Count * paso)) / 2);
        var anchoTextos = 0.0;

        foreach (var l in orden)
        {
            var px = X(l.X);
            var py = Y(l.Y);
            Linea(px, py, xTexto - h, yTexto, CapaConexion.Texto);
            Linea(xTexto - h, yTexto, xTexto - (0.3 * h), yTexto, CapaConexion.Texto);
            Circulo(px, py, 0.25 * h, CapaConexion.Texto);
            Texto(l.Texto, xTexto, yTexto + (0.5 * h), h, CapaConexion.Texto);
            anchoTextos = Math.Max(anchoTextos, l.Texto.Length * 0.62 * h);
            yTexto -= paso;
        }

        var alto = Math.Max(altoGeo + (2 * holgura), y - yTexto);
        Texto(v.Titulo, x + holgura, y - alto - (0.5 * hVista), hVista, CapaConexion.Texto);
        var ancho = (2 * holgura) + anchoGeo + (orden.Count > 0 ? (6 * h) + anchoTextos : 0);
        return (ancho, alto + (2.5 * hVista));
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

    private void Rellenar(object contorno, CapaConexion capa)
    {
        var patron = ConexionAceroCapas.PatronSoldadura;

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic hh = _ms.AddHatch(0, patron, false);

                var ok = AcadArreglos.Llamar("AppendOuterLoop de la soldadura", new[] { contorno },
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
                    hh.PatternScale = ConexionAceroCapas.EscalaSoldadura * _f;
                }

                hh.Layer = ConexionAceroCapas.Nombre(capa);
                hh.Evaluate();
            });
        }
        catch (Exception ex)
        {
            Fallo("Relleno de la soldadura", ex);
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

    /// <summary>Texto de una linea, con su esquina de arriba a la izquierda en (x, y).</summary>
    private void Texto(string texto, double x, double y, double alto, CapaConexion capa)
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
                t.AttachmentPoint = 1;
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
