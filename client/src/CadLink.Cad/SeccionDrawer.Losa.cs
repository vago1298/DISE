namespace CadLink.Cad;

/// <summary>
/// El armado de una LOSA de entrepiso o de cimentacion: la parte de <see cref="SeccionDrawer"/> que
/// la dibuja.
/// </summary>
/// <remarks>
/// La geometria -planta con las varillas abatidas, corte A-A y corte B-B- sale de
/// <see cref="GeometriaArmadoLosa"/>, sin AutoCAD, y la misma la pinta la vista previa de CadLink.
/// Aqui solo se dibuja, con las capas de siempre (CONCRETO, VAR_#n, COTAS, TEXTOS, ROTULOS), y se
/// agrupa en un bloque <c>LOSA-ID</c> que se puede redibujar en su sitio.
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>Dibuja el tablero y lo agrupa en su bloque. Devuelve cuantas entidades creo.</summary>
    public int DibujarLosa(ArmadoLosaCad l, double xIzquierda, double yArriba)
    {
        double[]? destino = null;
        InicioUltima = -1;
        UltimaFueASuSitio = false;

        var nombre = l.Bloque;

        if (BloqueYaExiste(nombre))
        {
            if (!Redibujar)
            {
                _saltadas.Add(l.Id);
                return 0;
            }

            destino = VolverASuSitio ? PuntoDeInsercion(nombre) : null;

            if (!BorrarSeccion(nombre))
            {
                _saltadas.Add(l.Id);
                return 0;
            }

            _redibujadas.Add(l.Id);
        }

        var dib = GeometriaArmadoLosa.Dibujo(l);

        if (dib.Trazos.Count == 0)
        {
            _log.Add($"Losa '{l.Id}': " + string.Join("; ", dib.Avisos));
            return 0;
        }

        UltimaFueASuSitio = destino is not null;

        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);
        InicioUltima = inicio;

        // El origen del dibujo es el eje de abajo a la izquierda; se corre para que la caja
        // empiece en la X pedida y su borde de arriba quede en la Y pedida.
        var caja = dib.Caja();
        var dx = xIzquierda - (caja.XMin * _f);
        var dy = yArriba - (caja.YMax * _f);

        double X(double x) => dx + (x * _f);
        double Y(double y) => dy + (y * _f);

        foreach (var t in dib.Trazos)
        {
            var pts = t.Puntos.SelectMany(p => new[] { X(p.X), Y(p.Y) }).ToArray();
            var pl = PolilineaDeLosa(pts, t.Capa, t.Cerrado);

            if (pl is not null && t.Ancho > 0)
            {
                AnchoConstante(pl, t.Ancho * _f);
            }
        }

        var circulos = new List<object>();

        foreach (var p in dib.Puntos)
        {
            if (Varilla(X(p.X), Y(p.Y), p.Radio * _f, p.Clave) is { } c)
            {
                circulos.Add(c);
            }
        }

        var rellenos = new List<object>();
        RellenarVarillas(circulos, rellenos);

        foreach (var c in dib.Cotas)
        {
            CotaDeLosa(X(c.X1), Y(c.Y1), X(c.X2), Y(c.Y2), X(c.XTexto), Y(c.YTexto), c.Texto,
                0.0025 * l.EscalaPlano * _f * 0.8);
        }

        foreach (var t in dib.Textos)
        {
            TextoDeLosa(t.Texto, X(t.X), Y(t.Y), t.Alto * _f, t.Anclaje, t.Capa);
        }

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        if (!string.IsNullOrWhiteSpace(l.Id))
        {
            Bloquear(nombre, inicio, fin, destino);
        }

        return fin - inicio;
    }

    /// <summary>Ancho que ocupa el dibujo de la losa, ya a la escala del dibujo: para poner el siguiente al lado.</summary>
    public double AnchoDeLosa(ArmadoLosaCad l)
    {
        var c = GeometriaArmadoLosa.Dibujo(l).Caja();
        return (c.XMax - c.XMin) * _f;
    }

    private object? PolilineaDeLosa(double[] puntos, string capa, bool cerrada)
    {
        if (puntos.Length < 4)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic p = _ms.AddLightWeightPolyline(puntos);
                p.Closed = cerrada;
                p.Layer = capa;
                p.Color = PorCapa;
                return (object?)p;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilinea de la losa en la capa {capa}", ex);
            return null;
        }
    }

    private void CotaDeLosa(double x1, double y1, double x2, double y2, double xt, double yt, string texto, double alto)
    {
        ConfigurarCotas();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic cota = _ms.AddDimAligned(new[] { x1, y1, 0d }, new[] { x2, y2, 0d }, new[] { xt, yt, 0d });

                FormatearCota((object)cota);

                // La escala del plano de losa no es la de las secciones: el texto y las marcas,
                // proporcionales a 1:N.
                PropCota((object)cota, "TextHeight", alto);
                PropCota((object)cota, "ArrowheadSize", alto * 0.8);
                PropCota((object)cota, "ExtensionLineExtend", alto * 0.6);
                PropCota((object)cota, "ExtLineFixedLen", alto * 0.6);
                PropCota((object)cota, "TextOverride", texto);
                cota.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota de la losa", ex);
        }
    }

    private void TextoDeLosa(string contenido, double x, double y, double altura, int anclaje, string capa)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic t = _ms.AddMText(new[] { x, y, 0d }, 0d, contenido);
                t.Layer = capa;
                t.Color = PorCapa;
                t.StyleName = EstiloTexto;
                t.Height = altura;
                t.AttachmentPoint = anclaje;
                t.InsertionPoint = new[] { x, y, 0d };
                t.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo($"Texto de la losa en la capa {capa}", ex);
        }
    }
}
