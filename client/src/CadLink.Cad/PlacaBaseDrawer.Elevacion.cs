namespace CadLink.Cad;

/// <summary>
/// El <b>alzado</b> de la placa base, dibujado a la derecha de la planta.
/// </summary>
/// <remarks>
/// Port de <c>DibujarDetallesElevacion</c> y compañía. El reparto —cuántas vistas, dónde y con qué
/// medidas— lo hace <see cref="ElevacionPlacaBase"/>, que no toca COM y se puede comprobar sin
/// AutoCAD delante. Aquí solo se manda a dibujar lo que esa clase diga.
/// </remarks>
public sealed partial class PlacaBaseDrawer
{
    /// <summary>
    /// Las vistas de canto, con su concreto, su placa, su columna, sus cartabones y sus anclas.
    /// </summary>
    /// <param name="xInicio">El canto derecho de la planta. El alzado arranca 60 cm más allá.</param>
    /// <param name="yPlaca">La cara de abajo de la placa en el alzado.</param>
    /// <remarks>
    /// <para>
    /// <b>Va dentro del bloque</b>, igual que en la macro: se dibuja antes de <c>Bloquear</c> y todo
    /// lo suyo queda en capas de geometría. Lo único que se queda fuera es su rótulo, que va en
    /// ROTULOS y por eso <c>Bloquear</c> lo salta. Así la planta y su alzado se mueven juntos.
    /// </para>
    /// <para>
    /// El alzado <b>no lleva cotas</b>, y eso es de la macro. Ver la nota de la vista previa: lo que
    /// se captura en F18, F19, E12 y E13 sale como geometría y no como número.
    /// </para>
    /// </remarks>
    private void Elevacion(
        PlacaBaseCad p, double xInicio, double yPlaca,
        double b, double h, double dadoX, double dadoY, double pX, double pY,
        double sepX, double sepY, double dAncX, double dAncY)
    {
        if (!p.DibujarElevacion)
        {
            return;
        }

        var vistas = ElevacionPlacaBase.Construir(
            xInicio + (ElevacionPlacaBase.SeparacionDeLaPlantaCm * _escala),
            yPlaca, _escala, _hTxt,
            p.EspesorCm * _escala,
            p.ConCartabones,
            DireccionDeElevacion(p, _escala, b, dadoX, pX, esX: true, sepX, dAncX),
            DireccionDeElevacion(p, _escala, h, dadoY, pY, esX: false, sepY, dAncY));

        foreach (var v in vistas)
        {
            DibujarVistaDeElevacion(v);
        }
    }

    /// <summary>Los datos de una dirección, ya en unidades de dibujo.</summary>
    /// <param name="anchoPlaca">La placa a lo ancho EN ESTA VISTA, ya orientada.</param>
    /// <param name="esX">
    /// <b>Los datos van cruzados igual que en planta.</b> La vista X toma la cantidad de cartabones
    /// de X, su longitud —E19— y su altura —F18—, que son los que en planta salen de las caras Y.
    /// Es la corrección que la propia macro documenta, y cambiarla aquí dejaría el alzado
    /// contradiciendo a la planta.
    /// </param>
    private static ElevacionPlacaBase.Direccion DireccionDeElevacion(
        PlacaBaseCad p, double escala, double anchoPlaca, double anchoDado, double anchoPerfil,
        bool esX, double sepBorde, double diamAncla) =>
        new(
            AnchoPlaca: anchoPlaca,
            AnchoDado: p.DibujarDado ? anchoDado : 0,
            AnchoPerfil: anchoPerfil,
            LongCartabon: (esX ? p.LongCartabonXCm : p.LongCartabonYCm) * escala,
            AltoCartabon: (esX ? p.AltoCartabonXCm : p.AltoCartabonYCm) * escala,
            CuantosCartabones: Math.Max(0, esX ? p.NCartabonesX : p.NCartabonesY),
            LongAnclaje: (esX ? p.LongAnclajeXCm : p.LongAnclajeYCm) * escala,
            LongAncla: (esX ? p.LongAnclaXCm : p.LongAnclaYCm) * escala,
            DoblezAncla: (esX ? p.DoblezAnclaXCm : p.DoblezAnclaYCm) * escala,
            SepBorde: sepBorde,
            DiamAncla: diamAncla,
            CuantasAnclas: Math.Max(0, esX ? p.NAnclasX : p.NAnclasY));

    private void DibujarVistaDeElevacion(ElevacionPlacaBase.Vista v)
    {
        Polilinea(v.Concreto, PlacaBaseCapas.Concreto);

        var placa = Polilinea(v.Placa, PlacaBaseCapas.Placa);

        if (placa is not null)
        {
            // El mismo grueso de línea que la placa en planta: es la misma pieza.
            try
            {
                AcadConnection.Retry(() =>
                {
                    ((dynamic)placa).ConstantWidth = PlacaBaseCapas.AnchoLineaPlaca;
                    ((dynamic)placa).Update();
                });
            }
            catch (Exception ex)
            {
                Fallo("Ancho de la polilínea de la placa en el alzado", ex);
            }
        }

        Polilinea(v.Columna, PlacaBaseCapas.Perfiles);

        foreach (var c in v.Cartabones)
        {
            Polilinea(c, PlacaBaseCapas.Cartabones);
        }

        foreach (var a in v.Anclas)
        {
            // POLILÍNEA ABIERTA y no dos líneas: con doblez el vástago tiene tres puntos, y dos
            // líneas suetas se pueden mover por separado. El ancla es una pieza.
            Polilinea(a.Vastago, PlacaBaseCapas.Anclas, cerrada: false);

            Polilinea(a.Tuerca, PlacaBaseCapas.Anclas);
            Linea(a.Arandela[0], a.Arandela[1], a.Arandela[2], a.Arandela[3], PlacaBaseCapas.Anclas);

            if (a.Remate is { } remate)
            {
                Linea(remate[0], remate[1], remate[2], remate[3], PlacaBaseCapas.Anclas);
            }
        }

        // El identificador SIEMPRE entre comillas, como en la macro: ELEVACION "X".
        Texto("ELEVACION \"" + v.Id + "\"", v.Rotulo.X, v.Rotulo.Y);
    }

    /// <summary>Un TEXT de una línea, centrado en el punto.</summary>
    /// <remarks>
    /// TEXT y no MTEXT: es un renglón suelto y no lleva ningún código de párrafo, así que un MTEXT
    /// solo añadiría el ancho del cuadro y su enganche. Es lo que hace la macro.
    /// </remarks>
    private object? Texto(string s, double x, double y)
    {
        if (s.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _ms.AddText(s, Punto(x, y), _hTxt);

                t.Layer = PlacaBaseCapas.Rotulos;
                t.Color = PorCapa;
                t.StyleName = PlacaBaseCapas.EstiloTexto;

                // 10 = acAlignmentMiddleCenter. Y el punto se reafirma después, porque al cambiar
                // la alineación AutoCAD recoloca el texto respecto al punto anterior.
                t.Alignment = 10;
                t.TextAlignmentPoint = Punto(x, y);

                return (object?)t;
            });
        }
        catch (Exception ex)
        {
            Fallo("Texto del alzado", ex);
            return null;
        }
    }
}
