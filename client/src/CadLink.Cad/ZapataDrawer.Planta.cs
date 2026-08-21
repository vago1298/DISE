// ParameterModifier y BindingFlags: GetBoundingBox devuelve sus dos resultados POR
// REFERENCIA y no se puede invocar con 'dynamic' sobre un objeto COM: la llamada revienta.
// Es la misma lección que ya está escrita en SeccionDrawer.CajaEnvolvente, y aquí importa
// el doble, porque de esa caja dependen la colocación del dado y el arranque de los leaders.
using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// La <b>vista en planta</b> de la zapata y las primitivas de AutoCAD del dibujante.
/// </summary>
/// <remarks>
/// Va en un archivo aparte porque son dos cosas distintas: la planta es un port completo
/// —<c>DibujarPlantaZapataAislada</c> y <c>DibujarPlantaZapataLindero</c>— y las primitivas son el
/// trato con COM, que no cambia nunca. Con las dos en un solo archivo, la elevación no se
/// encontraba.
/// </remarks>
public sealed partial class ZapataDrawer
{
    /// <summary>Cota del ancho, por debajo del paño inferior de la planta.</summary>
    private const double PlantaCotaOffset = 0.12;

    /// <summary>Cota del dado, a la derecha del paño derecho.</summary>
    private const double PlantaCotaOffsetDado = 0.1;

    /// <summary>
    /// Cota del largo de la zapata, a la <b>izquierda</b> del paño izquierdo.
    /// </summary>
    /// <remarks>
    /// Los 0.12 de la macro. El largo del dado va a la derecha, a 0.10, y así las dos cotas
    /// verticales de la planta no comparten lado ni se montan.
    /// </remarks>
    private const double PlantaCotaOffsetLargo = 0.12;
    private const double PlantaMinBarra = 0.03;
    private const double PlantaMinSeg = 0.004;
    private const double PlantaHuecoMargen = 0.003;
    private const double PlantaAltoIdDado = 0.03;
    private const double PlantaAltoMtexto = 0.03;
    private const double PlantaBreaklineAncho = 0.001;
    private const int PlantaBreaklineColor = 250;

    // Posición de los rótulos de parrilla, en fracción de ancho y largo.
    private const double PlantaRotInfFx = 0.62;
    private const double PlantaRotInfFy = 0.22;
    private const double PlantaRotSupFx = 0.33;
    private const double PlantaRotSupFy = 0.72;

    // ------------------------------------------------------------------
    // El hueco del dado en la planta. Cuadrado o CIRCULAR: con el dado redondo, las varillas
    // de la malla tienen que llegar hasta la circunferencia, cada una a su corte.
    // ------------------------------------------------------------------
    private bool _huecoCircular;
    private double _hcx;
    private double _hcy;
    private double _hr;

    /// <summary>
    /// Port de <c>DibujarPlantaZapataAislada</c> / <c>DibujarPlantaZapataLindero</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El paño, el dado —<b>centrado</b> en la central y pegado al <b>paño derecho</b> en el
    /// lindero—, las dos mallas y sus cotas. La malla que corre en Y pasa completa y la que corre
    /// en X se corta en cada cruce: así se ve cuál va arriba, que es lo que el fierrero necesita.
    /// </para>
    /// <para>
    /// <b>Primero todos los rellenos y después todos los contornos</b> (las dos fases de la macro
    /// V8). Dibujando cada varilla completa, el relleno de la siguiente se comía el contorno de la
    /// anterior y las varillas salían con las líneas cortadas.
    /// </para>
    /// </remarks>
    private void Planta(ZapataCad z, TrazoZapata.Acomodo a, Resumen r)
    {
        var lindero = TrazoZapata.EsLindero(z.Tipo);

        var xIzq = a.XBase;
        var xDer = a.XDer;
        var yBot = a.YPlanta;
        var yTop = yBot + z.LargoM;
        var xCen = (xIzq + xDer) / 2;
        var yCen = (yBot + yTop) / 2;

        var ancho = z.AnchoM;
        var largo = z.LargoM;
        var rec = z.RecM;

        if (ancho <= 0 || largo <= 0)
        {
            return;
        }

        var hayDoble = z.DobleParrilla && Diam(z.VarSup) > 0;

        // ------------------------------------------------------------------
        // LA PLANTA VA EN SU PROPIO BLOQUE, y dentro va solo el DIBUJO: el paño de la zapata,
        // sus varillas y el dado. Los rótulos y las cotas se quedan FUERA, en el modelo.
        //
        // Es lo que se pidió y es lo correcto: la planta se mueve, se copia y se inserta de una
        // pieza en el juego de planos, mientras que una cota tiene que poder editarse y seguir
        // midiendo, y un rótulo tiene que poder moverse solo. Metidos en el bloque habría que
        // explotarlo para tocar una cota, y explotarlo es perder el bloque.
        // ------------------------------------------------------------------
        var nombrePlanta = string.Empty;
        var plantaEnBloque = false;

        if (ZapataComoBloque)
        {
            nombrePlanta = NombreBloqueLibre(
                (z.Id ?? string.Empty).Trim().Length == 0 ? "PLANTA" : z.Id!.Trim() + "-PLANTA");

            var blk = CrearBloqueVacio(nombrePlanta, xIzq, yBot);

            if (blk is not null)
            {
                _cont = blk;
                plantaEnBloque = true;
            }
        }

        // El concreto de la planta solo se rellena en modo 1, igual que la macro.
        if (_relleno)
        {
            HatchConcreto(xIzq, yBot, ancho, largo, CapaConcreto);
        }

        Rectangulo(xIzq, yBot, xDer, yTop, CapaConcreto);

        // ---------- El dado ----------
        var wDado = z.AnchoDadoCm * TrazoZapata.EscalaElevacion;

        var (dx1, dy1, dx2, dy2) = TrazoZapata.HuecoDelDado(z, xIzq, yBot);

        // El hueco de recorte: círculo si el dado es redondo, rectángulo si no. El margen es el
        // mismo PLANTA_HUECO_MARGEN de la macro: el recorte va un pelo por fuera del dado para
        // que la varilla no acabe pegada al contorno.
        _huecoCircular = z.DadoCircular && wDado > 0;
        _hcx = (dx1 + dx2) / 2;
        _hcy = yCen;
        _hr = (wDado / 2) + PlantaHuecoMargen;

        var insertado = false;
        var id = (z.IdDado ?? string.Empty).Trim();

        if (id.Length > 0)
        {
            // En la central el bloque se centra en el centro del hueco; en el lindero se pega al
            // paño derecho, que es lo que hace InsertarBloqueDerecha.
            insertado = InsertarBloque(id, (dx1 + dx2) / 2, yCen, CapaBloqueDado,
                alinearDerechaEn: lindero ? xDer : (double?)null);
        }

        if (insertado)
        {
            r.DadosInsertados++;
        }
        else
        {
            if (wDado > 0)
            {
                if (z.DadoCircular)
                {
                    // El dado redondo se dibuja redondo, y su relleno también: es lo que se ve en
                    // el plano y es el contorno hasta el que llegan las varillas.
                    HatchCirculo(_hcx, _hcy, wDado / 2, CapaConcreto);
                    Circulo(_hcx, _hcy, wDado / 2, CapaConcreto);
                }
                else
                {
                    HatchConcreto(dx1, dy1, dx2 - dx1, dy2 - dy1, CapaConcreto);
                    Rectangulo(dx1, dy1, dx2, dy2, CapaConcreto);
                }

                if (id.Length > 0)
                {
                    var alto = PlantaAltoIdDado;
                    var estimado = id.Length * alto * 0.7;

                    if (estimado > wDado * 0.8)
                    {
                        alto *= wDado * 0.8 / estimado;
                    }

                    Mtexto((dx1 + dx2) / 2, (dy1 + dy2) / 2, id, alto, CapaRotulos, !_relleno);
                }
            }

            r.DadosDeRespaldo++;

            if (id.Length == 0)
            {
                Nota($"Zapata '{z.Id}': no tiene ID de dado, así que en la planta se dibujó su "
                     + "rectángulo. Elige el dado en la celda y vuelve a dibujar.");
            }
            else if (_dadosQueFaltan.Add(id))
            {
                Nota($"El bloque del dado «{id}» no está en el dibujo: en la planta se dibujó un "
                     + "rectángulo de su tamaño. Dibuja primero la sección del dado en la hoja de "
                     + "concreto y vuelve a dibujar la zapata.");
            }
        }

        // El hueco de recorte es un poco mayor que el bloque; las cotas miden el bloque exacto.
        var hx1 = dx1 - PlantaHuecoMargen;
        var hx2 = dx2 + PlantaHuecoMargen;
        var hy1 = dy1 - PlantaHuecoMargen;
        var hy2 = dy2 + PlantaHuecoMargen;

        // ---------- Las mallas ----------
        if (hayDoble)
        {
            for (var fase = 1; fase <= 2; fase++)
            {
                Malla(xIzq, yBot, ancho, largo, rec, z.VarInf, z.SepInf, z.VarInfTrans,
                    z.SepInfTrans, ladoInferior: true, conDiagonal: true,
                    hx1, hy1, hx2, hy2, fase);
                Malla(xIzq, yBot, ancho, largo, rec, z.VarSup, z.SepSup, z.VarSupTrans,
                    z.SepSupTrans, ladoInferior: false, conDiagonal: true,
                    hx1, hy1, hx2, hy2, fase);
            }

            LineaDeRoturaEntre(xIzq, yBot, xDer, yTop);
        }
        else
        {
            for (var fase = 1; fase <= 2; fase++)
            {
                Malla(xIzq, yBot, ancho, largo, rec, z.VarInf, z.SepInf, z.VarInfTrans,
                    z.SepInfTrans, ladoInferior: true, conDiagonal: false,
                    hx1, hy1, hx2, hy2, fase);
            }
        }

        // ------------------------------------------------------------------
        // Se cierra el bloque: lo que sigue -cotas y rótulos- va en el MODELO.
        // ------------------------------------------------------------------
        _cont = _ms;

        if (plantaEnBloque && InsertarBloquePropio(nombrePlanta, xIzq, yBot, CapaBloqueZapata))
        {
            r.Bloques++;
        }

        // ---------- Rótulos de las mallas ----------
        if (hayDoble)
        {
            RotuloMalla(
                xIzq + (ancho * PlantaRotInfFx), yBot + (largo * PlantaRotInfFy),
                "PARRILLA INFERIOR\n" + VarSep(z.VarInf, z.SepInf, string.Empty)
                + "\n" + VarSep(z.VarInfTrans, z.SepInfTrans, string.Empty),
                xIzq + (ancho * 0.86), yBot + (largo * 0.28),
                xIzq + (ancho * 0.9), yBot + (largo * 0.14));

            RotuloMalla(
                xIzq + (ancho * PlantaRotSupFx), yBot + (largo * PlantaRotSupFy),
                "PARRILLA SUPERIOR\n" + VarSep(z.VarSup, z.SepSup, string.Empty)
                + "\n" + VarSep(z.VarSupTrans, z.SepSupTrans, string.Empty),
                xIzq + (ancho * 0.14), yBot + (largo * 0.72),
                xIzq + (ancho * 0.1), yBot + (largo * 0.86));
        }
        else
        {
            RotuloMalla(
                xIzq + (ancho * PlantaRotInfFx), yBot + (largo * PlantaRotInfFy),
                VarSep(z.VarInf, z.SepInf, string.Empty)
                + "\n" + VarSep(z.VarInfTrans, z.SepInfTrans, string.Empty),
                xIzq + (ancho * 0.86), yBot + (largo * 0.28),
                xIzq + (ancho * 0.9), yBot + (largo * 0.14));
        }

        // ---------- Cotas: LAS DE LA MACRO, en su sitio ----------
        // El ancho del dado arriba, el de la zapata abajo, el largo de la zapata a la izquierda y
        // el del dado a la derecha. Cada medida pegada al paño que le toca, que es donde se lee.
        r.Cotas += Cota(dx1, yTop + PlantaCotaOffsetDado, dx2, yTop + PlantaCotaOffsetDado,
            (dx1 + dx2) / 2, yTop + PlantaCotaOffsetDado, false, false);

        r.Cotas += Cota(xIzq, yBot - PlantaCotaOffset, xDer, yBot - PlantaCotaOffset,
            xCen, yBot - PlantaCotaOffset, false, false);

        // El largo del DADO a la derecha, a 0.10; el de la ZAPATA a la IZQUIERDA, a 0.12, que es
        // como lo pone la macro. Los dos por el mismo lado se montaban.
        r.Cotas += Cota(xDer + PlantaCotaOffsetDado, dy1, xDer + PlantaCotaOffsetDado, dy2,
            xDer + PlantaCotaOffsetDado, (dy1 + dy2) / 2, true, false);

        r.Cotas += Cota(xIzq - PlantaCotaOffsetLargo, yBot, xIzq - PlantaCotaOffsetLargo, yTop,
            xIzq - PlantaCotaOffsetLargo, yCen, true, false);

        // Y los dos renglones del rótulo, a los 0.24 y 0.33 de la macro por debajo del paño
        // inferior y CENTRADOS en el eje de la planta, con el mismo encogido del corte para que un
        // título largo no se meta en el de la planta de al lado.
        var yTitulo = TrazoZapata.YRotuloPlanta(yBot, 0);
        var yEscala = TrazoZapata.YRotuloPlanta(yBot, 2);
        var anchoRotulo = TrazoZapata.AnchoParaElRotulo(ancho);

        var titulo = $"VISTA EN PLANTA \"{z.Id}\"";
        var escala = $"Rec. {rec * 100:0.#} cm    Escala 1:10";

        Texto(xCen, yTitulo,
            TrazoZapata.AltoQueQuepa(titulo.Length, AltoTitulo, anchoRotulo,
                TrazoZapata.FactorLetraTitulo),
            titulo, CapaRotulos, alineacion: Alineacion.Centro);

        Texto(xCen, yEscala,
            TrazoZapata.AltoQueQuepa(escala.Length, AltoEscala, anchoRotulo,
                TrazoZapata.FactorLetraTitulo),
            escala, CapaRotulos, alineacion: Alineacion.Centro);
    }

    /// <summary>
    /// Port de <c>DibujarMallaPlanta</c>: una malla, con sus recortes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La familia que corre en <b>Y</b> pasa completa —queda encima— y solo se parte en el hueco
    /// del dado. La que corre en <b>X</b> se corta en cada cruce con la anterior y también en el
    /// hueco. Los cortes se ordenan antes de recorrer, que es lo que permite que un tramo caiga
    /// entre dos cruces sin dibujarse dos veces.
    /// </para>
    /// <para>
    /// Con doble parrilla, la <b>diagonal</b> parte la planta: la inferior se dibuja en un
    /// triángulo y la superior en el otro, y cada varilla que la cruza se corta en diagonal. Así
    /// las dos parrillas se acotan por separado sin dibujar dos plantas.
    /// </para>
    /// </remarks>
    private void Malla(
        double xIzq, double yBot, double ancho, double largo, double rec,
        string? varX, string? sepX, string? varY, string? sepY,
        bool ladoInferior, bool conDiagonal,
        double hx1, double hy1, double hx2, double hy2, int fase)
    {
        var dX = Diam(varX);
        var dY = Diam(varY);

        if (dX <= 0)
        {
            return;
        }

        var capaX = CapaVar(varX);
        var capaY = CapaVar(varY);

        AsegurarCapaVarilla(capaX);
        AsegurarCapaVarilla(capaY);

        var rX = dX / 2;
        var rY = dY / 2;

        var sX = TrazoZapata.SeparacionM(sepX);
        var sY = TrazoZapata.SeparacionM(sepY);

        var xIni = xIzq + rec;
        var xFin = xIzq + ancho - rec;
        var yIni = yBot + rec;
        var yFin = yBot + largo - rec;

        if (xFin <= xIni || yFin <= yIni)
        {
            return;
        }

        var hayHueco = hx2 > hx1 && hy2 > hy1;

        var posY = TrazoZapata.Posiciones(xIni + rY, xFin - rY, sY);
        var posX = TrazoZapata.Posiciones(yIni + rX, yFin - rX, sX);

        if (posY.Length == 0 || posX.Length == 0)
        {
            return;
        }

        // ---- familia que corre en Y ----
        foreach (var x in posY)
        {
            var cortaAb = conDiagonal && !ladoInferior;
            var cortaAr = conDiagonal && ladoInferior;

            BarraYConHueco(x, yIni, yFin, rY, capaY, cortaAb, cortaAr,
                xIzq, yBot, ancho, largo, hx1, hy1, hx2, hy2, fase);
        }

        // ---- familia que corre en X ----
        var m = ancho / largo;

        foreach (var y in posX)
        {
            var xa = xIni;
            var xb = xFin;
            var cortaIzq = false;
            var cortaDer = false;
            var dibujar = true;

            if (conDiagonal)
            {
                var xdb = xIzq + ((y - rX - yBot) * m);
                var xdt = xIzq + ((y + rX - yBot) * m);

                if (ladoInferior)
                {
                    if (xdt >= xb - PlantaMinBarra)
                    {
                        dibujar = false;
                    }
                    else
                    {
                        if (xdt > xa)
                        {
                            xa = xdt;
                        }

                        cortaIzq = true;
                    }
                }
                else
                {
                    if (xdb <= xa + PlantaMinBarra)
                    {
                        dibujar = false;
                    }
                    else
                    {
                        if (xdb < xb)
                        {
                            xb = xdb;
                        }

                        cortaDer = true;
                    }
                }
            }

            if (!dibujar)
            {
                continue;
            }

            var cortes = new List<(double A, double B)>();

            foreach (var x in posY)
            {
                cortes.Add((x - rY, x + rY));
            }

            if (hayHueco || _huecoCircular)
            {
                var corte = CorteDelHueco(y, enX: true, rX, hx1, hy1, hx2, hy2);

                if (corte is not null)
                {
                    cortes.Add(corte.Value);
                }
            }

            cortes.Sort((p, q) => p.A.CompareTo(q.A));

            var desde = xa;

            foreach (var (ca, cb) in cortes)
            {
                var a = ca;
                var b = cb;

                if (b > xa && a < xb)
                {
                    if (a > desde)
                    {
                        if (a > xb)
                        {
                            a = xb;
                        }

                        var esIni = Math.Abs(desde - xa) < 1e-6;

                        SegBandaX(y, desde, a, rX, capaX, cortaIzq && esIni, false,
                            xIzq, yBot, ancho, largo, esIni && !cortaIzq, false, fase);
                    }

                    if (b > desde)
                    {
                        desde = b;
                    }
                }
            }

            if (xb > desde)
            {
                var esIni = Math.Abs(desde - xa) < 1e-6;

                SegBandaX(y, desde, xb, rX, capaX, cortaIzq && esIni, cortaDer,
                    xIzq, yBot, ancho, largo, esIni && !cortaIzq, !cortaDer, fase);
            }
        }
    }

    /// <summary>Port de <c>EmitirBarraYConHueco</c>.</summary>
    private void BarraYConHueco(
        double x, double ya, double yb, double r, string capa, bool cortaAb, bool cortaAr,
        double xIzq, double yBot, double ancho, double largo,
        double hx1, double hy1, double hx2, double hy2, int fase)
    {
        if (ancho <= 0)
        {
            return;
        }

        var m = largo / ancho;
        var yd = yBot + ((x - xIzq) * m);

        var yBotEf = cortaAb ? yd : ya;
        var yTopEf = cortaAr ? yd : yb;

        yBotEf = Math.Max(yBotEf, ya);
        yTopEf = Math.Min(yTopEf, yb);

        if (yTopEf - yBotEf <= PlantaMinSeg)
        {
            return;
        }

        // El corte de ESTA varilla: la cuerda del círculo a su X, o el rectángulo del hueco.
        var corte = CorteDelHueco(x, enX: false, r, hx1, hy1, hx2, hy2);

        if (corte is null || yTopEf <= corte.Value.A || yBotEf >= corte.Value.B)
        {
            SegBandaY(x, ya, yb, r, capa, cortaAb, cortaAr, xIzq, yBot, ancho, largo,
                !cortaAb, !cortaAr, fase);
            return;
        }

        var cy1 = corte.Value.A;
        var cy2 = corte.Value.B;

        // Tramo por debajo del hueco.
        var y1 = yBotEf;
        var y2 = Math.Min(yTopEf, cy1);

        if (y2 - y1 > PlantaMinSeg)
        {
            var cAb = cortaAb && Math.Abs(y1 - yd) < 1e-6;
            var cAr = cortaAr && Math.Abs(y2 - yd) < 1e-6;

            // Solo se tapa el extremo que es un extremo DE VERDAD de la varilla: en el corte del
            // hueco y en el de la diagonal no va tapa. Son los tapAb / tapAr de la macro.
            var tapAb = Math.Abs(y1 - ya) < 1e-6 && !cAb;
            var tapAr = Math.Abs(y2 - yb) < 1e-6 && !cAr;

            SegBandaY(x, y1, y2, r, capa, cAb, cAr, xIzq, yBot, ancho, largo,
                tapAb, tapAr, fase);
        }

        // Tramo por arriba del hueco.
        y1 = Math.Max(yBotEf, cy2);
        y2 = yTopEf;

        if (y2 - y1 > PlantaMinSeg)
        {
            var cAb = cortaAb && Math.Abs(y1 - yd) < 1e-6;
            var cAr = cortaAr && Math.Abs(y2 - yd) < 1e-6;

            // Solo se tapa el extremo que es un extremo DE VERDAD de la varilla: en el corte del
            // hueco y en el de la diagonal no va tapa. Son los tapAb / tapAr de la macro.
            var tapAb = Math.Abs(y1 - ya) < 1e-6 && !cAb;
            var tapAr = Math.Abs(y2 - yb) < 1e-6 && !cAr;

            SegBandaY(x, y1, y2, r, capa, cAb, cAr, xIzq, yBot, ancho, largo,
                tapAb, tapAr, fase);
        }
    }

    /// <summary>
    /// El tramo que el hueco del dado le come a una varilla, o <c>null</c> si no la toca.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con el dado <b>cuadrado</b> el corte es el rectángulo del hueco, igual para todas las
    /// varillas que lo cruzan. Con el dado <b>redondo</b> cada varilla tiene <b>su</b> corte: la
    /// media cuerda de la circunferencia a su altura, <c>√(r² − d²)</c>. Eso es lo que hace que
    /// las varillas lleguen hasta el contorno circular y no se queden todas en el mismo cuadrado.
    /// </para>
    /// <para>
    /// Se tiene en cuenta el <b>radio de la propia varilla</b>: la que solo roza la circunferencia
    /// con el canto no se corta, porque en la obra pasa por fuera.
    /// </para>
    /// </remarks>
    private (double A, double B)? CorteDelHueco(
        double coordenada, bool enX, double rBar,
        double hx1, double hy1, double hx2, double hy2)
    {
        if (_huecoCircular)
        {
            var d = coordenada - (enX ? _hcy : _hcx);
            var dentro = (_hr * _hr) - (d * d);

            if (dentro <= 0)
            {
                return null;
            }

            var media = Math.Sqrt(dentro);
            var centro = enX ? _hcx : _hcy;

            return (centro - media, centro + media);
        }

        if (hx2 <= hx1 || hy2 <= hy1)
        {
            return null;
        }

        if (enX)
        {
            return coordenada + rBar > hy1 && coordenada - rBar < hy2 ? (hx1, hx2) : null;
        }

        return coordenada + rBar > hx1 && coordenada - rBar < hx2 ? (hy1, hy2) : null;
    }

    /// <summary>Port de <c>DibujarSegBandaX</c>: un tramo de varilla en X, con sus dos caras.</summary>
    private void SegBandaX(
        double y, double xa, double xb, double r, string capa,
        bool cortaIzqDiag, bool cortaDerDiag,
        double xIzq, double yBot, double ancho, double largo,
        bool taparIzq, bool taparDer, int fase)
    {
        if (ancho <= 0 || largo <= 0)
        {
            return;
        }

        var m = ancho / largo;
        var xdb = xIzq + ((y - r - yBot) * m);
        var xdt = xIzq + ((y + r - yBot) * m);

        var xaB = xa;
        var xaT = xa;
        var xbB = xb;
        var xbT = xb;

        if (cortaIzqDiag)
        {
            if (xb - xdt <= PlantaMinBarra)
            {
                return;
            }

            xaB = xdb;
            xaT = xdt;
        }
        else if (cortaDerDiag)
        {
            if (xdb - xa <= PlantaMinBarra)
            {
                return;
            }

            xbB = xdb;
            xbT = xdt;
        }
        else if (xb - xa <= PlantaMinSeg)
        {
            return;
        }

        if (fase != 2)
        {
            if (_relleno)
            {
                RellenarQuad(xaB, y - r, xbB, y - r, xbT, y + r, xaT, y + r, capa, 0);
            }

            if (fase == 1)
            {
                return;
            }
        }

        Var(Linea(xaB, y - r, xbB, y - r, capa));
        Var(Linea(xaT, y + r, xbT, y + r, capa));

        if (cortaIzqDiag)
        {
            Var(Linea(xaB, y - r, xaT, y + r, capa));
        }
        else if (taparIzq)
        {
            Var(Linea(xa, y - r, xa, y + r, capa));
        }

        if (cortaDerDiag)
        {
            Var(Linea(xbB, y - r, xbT, y + r, capa));
        }
        else if (taparDer)
        {
            Var(Linea(xb, y - r, xb, y + r, capa));
        }
    }

    /// <summary>Port de <c>DibujarSegBandaY</c>.</summary>
    private void SegBandaY(
        double x, double ya, double yb, double r, string capa,
        bool cortaAbDiag, bool cortaArDiag,
        double xIzq, double yBot, double ancho, double largo,
        bool taparAb, bool taparAr, int fase)
    {
        if (ancho <= 0 || largo <= 0)
        {
            return;
        }

        var m = largo / ancho;
        var ydl = yBot + ((x - r - xIzq) * m);
        var ydr = yBot + ((x + r - xIzq) * m);

        var yaL = ya;
        var yaR = ya;
        var ybL = yb;
        var ybR = yb;

        if (cortaArDiag)
        {
            if (ydl - ya <= PlantaMinBarra)
            {
                return;
            }

            ybL = ydl;
            ybR = ydr;
        }
        else if (cortaAbDiag)
        {
            if (yb - ydr <= PlantaMinBarra)
            {
                return;
            }

            yaL = ydl;
            yaR = ydr;
        }
        else if (yb - ya <= PlantaMinSeg)
        {
            return;
        }

        if (fase != 2)
        {
            if (_relleno)
            {
                RellenarQuad(x - r, yaL, x - r, ybL, x + r, ybR, x + r, yaR, capa, 0);
            }

            if (fase == 1)
            {
                return;
            }
        }

        Var(Linea(x - r, yaL, x - r, ybL, capa));
        Var(Linea(x + r, yaR, x + r, ybR, capa));

        if (cortaAbDiag)
        {
            Var(Linea(x - r, yaL, x + r, yaR, capa));
        }
        else if (taparAb)
        {
            Var(Linea(x - r, ya, x + r, ya, capa));
        }

        if (cortaArDiag)
        {
            Var(Linea(x - r, ybL, x + r, ybR, capa));
        }
        else if (taparAr)
        {
            Var(Linea(x - r, yb, x + r, yb, capa));
        }
    }

    /// <summary>Port de <c>RotularMallaPlanta</c>: el texto con sus dos leaders.</summary>
    private void RotuloMalla(
        double xt, double yt, string texto,
        double tipXx, double tipXy, double tipYx, double tipYy)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        // Con la sección rellena el rótulo va SIN máscara, para que se vea el hatch por detrás.
        var mt = Mtexto(xt, yt, texto, PlantaAltoMtexto, CapaRotulos, conFondo: !_relleno);

        if (mt is null)
        {
            return;
        }

        var caja = Caja(mt);

        var xa = xt;
        var ya = yt;
        var xb = xt;
        var yb = yt;

        if (caja is not null)
        {
            xa = tipXx >= xt ? caja.Value.X2 : caja.Value.X1;
            ya = tipXy >= yt ? caja.Value.Y2 : caja.Value.Y1;
            xb = tipYx >= xt ? caja.Value.X2 : caja.Value.X1;
            yb = tipYy >= yt ? caja.Value.Y2 : caja.Value.Y1;
        }

        Leader(tipXx, tipXy, xa, ya);
        Leader(tipYx, tipYy, xb, yb);
    }

    /// <summary>Port de <c>DibujarBreakLineEntre</c>: la línea de rotura de la diagonal.</summary>
    private void LineaDeRoturaEntre(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var l = Math.Sqrt((dx * dx) + (dy * dy));

        if (l <= 1e-6)
        {
            return;
        }

        var ux = dx / l;
        var uy = dy / l;
        var px = -uy;
        var py = ux;
        var xm = (x1 + x2) / 2;
        var ym = (y1 + y2) / 2;

        var ext = l * 0.02;
        var paso = l * 0.05;
        var amp = l * 0.04;

        var pl = Polilinea(
            new[]
            {
                x1 - (ux * ext), y1 - (uy * ext),
                xm - (ux * 1.5 * paso), ym - (uy * 1.5 * paso),
                xm - (ux * 0.5 * paso) + (px * amp), ym - (uy * 0.5 * paso) + (py * amp),
                xm + (ux * 0.5 * paso) - (px * amp), ym + (uy * 0.5 * paso) - (py * amp),
                xm + (ux * 1.5 * paso), ym + (uy * 1.5 * paso),
                x2 + (ux * ext), y2 + (uy * ext)
            },
            CapaConcreto, cerrada: false);

        if (_relleno)
        {
            Grosor(pl, PlantaBreaklineAncho);
            Color(pl, PlantaBreaklineColor);
        }
    }

    // ======================================================================
    // Coordenadas: la rotación de 90° del elemento vertical
    // ======================================================================

    /// <summary>X global de un punto local. Con la rotación activa gira 90° sobre el origen.</summary>
    private double GX(double x, double y) => _rot ? _rx0 - (y - _ry0) : x;

    /// <summary>Y global de un punto local.</summary>
    private double GY(double x, double y) => _rot ? _ry0 + (x - _rx0) : y;

    /// <summary>Ángulo global de un arco: la rotación le suma 90°.</summary>
    private double GA(double a) => _rot ? a + (Math.PI / 2) : a;

    // ======================================================================
    // Varillas: capas, diámetros y etiquetas
    // ======================================================================

    /// <summary>El diámetro de una varilla en metros, o 0 si la celda está vacía.</summary>
    private double Diam(string? clave)
    {
        var cm = _diametroCm(clave);

        return cm > 0 ? cm / 100.0 : 0;
    }

    /// <summary>Port de <c>NormalizeDiaLabel</c>: «4» y «#4» son la misma varilla.</summary>
    private static string Etiqueta(string? clave)
    {
        var t = (clave ?? string.Empty).Trim().ToUpperInvariant();

        if (t.Length == 0)
        {
            return string.Empty;
        }

        return t.Contains('#', StringComparison.Ordinal) ? t : "#" + t;
    }

    private static bool MismoDiametro(string? a, string? b)
    {
        var x = Etiqueta(a);
        var y = Etiqueta(b);

        return x.Length > 0 && y.Length > 0 && x == y;
    }

    /// <summary>Port de <c>VarLayerName</c>: cada diámetro en su capa, <c>VAR_#4</c>.</summary>
    /// <remarks>
    /// Es lo que permite apagar un diámetro entero en el plano, y es como quedan las secciones y
    /// los alzados: si la zapata metiera su acero en una capa propia, el mismo #4 estaría en dos
    /// capas distintas del mismo dibujo.
    /// </remarks>
    private static string CapaVar(string? clave)
    {
        var e = Etiqueta(clave);

        return e.Length == 0 ? "VAR_#3" : "VAR_" + e;
    }

    private void AsegurarCapaVarilla(string capa)
    {
        if (!_capas.Add(capa))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic todas = _doc.Layers;

                try
                {
                    _ = todas.Item(capa);
                }
                catch (Exception)
                {
                    _ = todas.Add(capa);
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Crear la capa '{capa}'", ex);
        }
    }

    /// <summary>
    /// Le pone el contorno negro a una varilla cuando la sección va rellena.
    /// </summary>
    /// <remarks>
    /// Port de <c>AplicarContornoVarilla</c>. Con el relleno puesto, el contorno del color de la
    /// capa se pierde dentro del relleno: en negro —ACI 250, que se ve negro en el Model y imprime
    /// negro— la varilla vuelve a leerse.
    /// </remarks>
    private void Var(object? ent)
    {
        if (_relleno)
        {
            Negro(ent);
        }
    }

    private void Negro(object? ent) => Color(ent, ColorContornoNegro);

    // ======================================================================
    // Primitivas de AutoCAD
    // ======================================================================

    /// <summary>Crea las capas de la macro si no existen. Nunca cambia las que ya hay.</summary>
    public void AsegurarCapasBase()
    {
        var capas = new (string Nombre, int Color)[]
        {
            (CapaConcreto, 0),
            (CapaEstribos, 0),
            (CapaCotas, 0),
            (CapaRotulos, 3),
            (CapaLeader, 3),
            (CapaTerreno, 140),
            (CapaTerrenoHatch, 8),
            (CapaPlantilla, 8),
            (CapaBloqueDado, 7),
            (CapaBloqueZapata, 7)
        };

        foreach (var (nombre, color) in capas)
        {
            if (!_capas.Add(nombre))
            {
                continue;
            }

            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic todas = _doc.Layers;

                    try
                    {
                        // Si ya existe se deja como está: son las capas del usuario.
                        _ = todas.Item(nombre);
                    }
                    catch (Exception)
                    {
                        dynamic nueva = todas.Add(nombre);

                        if (color > 0)
                        {
                            nueva.Color = color;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Fallo($"Crear la capa '{nombre}'", ex);
            }
        }
    }

    private void AsegurarEstiloTexto()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;

                try
                {
                    _ = estilos.Item(EstiloTexto);
                }
                catch (Exception)
                {
                    dynamic nuevo = estilos.Add(EstiloTexto);
                    nuevo.SetFont("Arial", false, false, 0, 0);
                }
            });
        }
        catch (Exception ex)
        {
            Nota($"No se pudo preparar el estilo de texto '{EstiloTexto}'. " + ex.Message);
        }
    }

    /// <summary>
    /// Port de <c>AsegurarEstiloCota</c>, con las <b>variables de cota</b> antes de crearlo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aquí estaba el defecto de las cotas gigantes.</b> Al reescribir el dibujante dejé solo
    /// la parte que busca o crea el estilo <c>COTA_ESTRUCTURAL</c>, y un estilo que se crea sin
    /// más se crea con las variables <b>que tenga el dibujo</b>: en un plano nuevo eso es texto
    /// de 0.18 y flechas de 0.18, o sea 18 cm de texto al lado de una zapata de un metro. Las
    /// cotas salían enormes y descuadradas, y no era que estuvieran mal medidas.
    /// </para>
    /// <para>
    /// Se fijan primero las variables y <b>después</b> se crea el estilo, porque el estilo copia
    /// el estado del documento. Al revés no sirve de nada. Los valores son los de un plano a
    /// <b>1:10</b>, que es la escala en la que se dibuja la zapata.
    /// </para>
    /// </remarks>
    private void AsegurarEstiloCota()
    {
        // Geometría de la cota.
        Dimvar("DIMSCALE", 1d);
        Dimvar("DIMTXT", 0.025);      // alto del número
        Dimvar("DIMASZ", 0.025);      // tamaño de la marca
        Dimvar("DIMEXO", 0.02);       // separación de la pieza
        Dimvar("DIMEXE", 0.035);      // remate de la línea de extensión
        Dimvar("DIMGAP", 0.008);      // hueco alrededor del número
        Dimvar("DIMDLE", 0d);

        // Metros con dos decimales, que es como se lee un plano de cimentación.
        Dimvar("DIMLUNIT", 2);
        Dimvar("DIMDEC", 2);
        Dimvar("DIMZIN", 0);

        // Marcas abiertas en lugar de flechas rellenas. DIMSAH va primero: dice que las dos
        // puntas usan el mismo bloque, y con DIMSAH en 1 la asignación de DIMBLK se rechaza.
        Dimvar("DIMSAH", 0);
        Dimvar("DIMBLK", "_OPEN90");

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(EstiloCota);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(EstiloCota);
                }

                // CopyFrom copia el estado ACTUAL del documento, que es el que se acaba de
                // fijar. Es lo que hace que el estilo tenga las medidas del plano y no las de
                // fábrica, tanto si ya existía como si se acaba de crear.
                estilo.CopyFrom(_doc);
                _doc.ActiveDimStyle = estilo;
            });
        }
        catch (Exception)
        {
            Nota($"No se pudo dejar activo el estilo de cota '{EstiloCota}'; las cotas de la "
                 + "zapata usan el estilo activo del dibujo.");
        }
    }

    /// <summary>Fija una variable de cota, tolerando que esta versión no la acepte.</summary>
    /// <remarks>
    /// El cuerpo va entre llaves a propósito: con una expresión, al ser <c>_doc</c> dinámico, la
    /// lambda podría resolverse al <c>Retry&lt;T&gt;</c> genérico.
    /// </remarks>
    private void Dimvar(string nombre, object valor)
    {
        try
        {
            AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
        }
        catch (Exception)
        {
            Nota($"La variable de cota {nombre} no aceptó '{valor}'; esa cota sale con lo que "
                 + "tenga el dibujo.");
        }
    }

    private object? Linea(double xa, double ya, double xb, double yb, string capa)
    {
        if (Math.Abs(xb - xa) < 1e-9 && Math.Abs(yb - ya) < 1e-9)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic l = _cont.AddLine(
                    new[] { GX(xa, ya), GY(xa, ya), 0d },
                    new[] { GX(xb, yb), GY(xb, yb), 0d });

                l.Layer = capa;
                return (object?)l;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Línea de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Arco(double cx, double cy, double radio, double a0, double a1, string capa)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic arc = _cont.AddArc(
                    new[] { GX(cx, cy), GY(cx, cy), 0d }, radio, GA(a0), GA(a1));

                arc.Layer = capa;
                return (object?)arc;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Arco de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Polilinea(double[] puntos, string capa, bool cerrada)
    {
        if (puntos.Length < 4)
        {
            return null;
        }

        var mapa = new double[puntos.Length];

        for (var i = 0; i < puntos.Length; i += 2)
        {
            mapa[i] = GX(puntos[i], puntos[i + 1]);
            mapa[i + 1] = GY(puntos[i], puntos[i + 1]);
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic p = _cont.AddLightWeightPolyline(mapa);
                p.Closed = cerrada;
                p.Layer = capa;
                return (object?)p;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Polilínea de la zapata en la capa '{capa}'", ex);
            return null;
        }
    }

    private object? Rectangulo(double x1, double y1, double x2, double y2, string capa)
    {
        var xa = Math.Min(x1, x2);
        var xb = Math.Max(x1, x2);
        var ya = Math.Min(y1, y2);
        var yb = Math.Max(y1, y2);

        if (xb - xa < 1e-9 || yb - ya < 1e-9)
        {
            return null;
        }

        return Polilinea(new[] { xa, ya, xb, ya, xb, yb, xa, yb }, capa, cerrada: true);
    }

    private object? Circulo(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic c = _cont.AddCircle(new[] { GX(cx, cy), GY(cx, cy), 0d }, radio);
                c.Layer = capa;
                return (object?)c;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Varilla vista de punta en la capa '{capa}'", ex);
            return null;
        }
    }

    /// <summary>Port de <c>DibujarCirculoRelleno</c>: relleno primero y contorno encima.</summary>
    private void CirculoRelleno(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return;
        }

        if (_relleno)
        {
            RellenarCirculo(cx, cy, radio, capa, 0);
        }

        Var(Circulo(cx, cy, radio, capa));
    }

    /// <summary>Port de <c>DibujarHatchRect</c>: rellena un rectángulo y borra su frontera.</summary>
    /// <remarks>
    /// La polilínea del contorno es <b>temporal</b>: se borra en cuanto el hatch está evaluado,
    /// porque sus lados coinciden con líneas que ya están dibujadas. Y por eso el hatch va
    /// <b>no asociativo</b>: uno asociativo se iría con su frontera.
    /// </remarks>
    private object? HatchRect(
        double x, double y, double w, double h, string capa,
        string patron, double escala, string transparencia, int colorAci)
    {
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        var borde = Rectangulo(x, y, x + w, y + h, capa);

        if (borde is null)
        {
            return null;
        }

        var h1 = Hatch(borde, patron, escala, capa, colorAci);

        if (h1 is null && !patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
        {
            Nota($"El patrón '{patron}' no se pudo usar; se rellenó con '{PatronRespaldo}'.");
            h1 = Hatch(borde, PatronRespaldo, escala, capa, colorAci);
        }

        if (transparencia.Length > 0)
        {
            Transparencia(h1, transparencia);
        }

        Borrar(borde);

        return h1;
    }

    /// <summary>Rellena un círculo con el patrón del concreto, según el modo.</summary>
    private void HatchCirculo(double cx, double cy, double radio, string capa)
    {
        if (radio <= 0)
        {
            return;
        }

        if (_relleno)
        {
            RellenarCirculo(cx, cy, radio, capa, ColorSolidoRelleno);
            HatchCirculoPatron(cx, cy, radio, capa, EscalaConcretoRelleno, ColorPatronRelleno);
            return;
        }

        HatchCirculoPatron(cx, cy, radio, capa, EscalaConcretoNormal, 0);
    }

    private void HatchCirculoPatron(
        double cx, double cy, double radio, string capa, double escala, int colorAci)
    {
        var borde = Circulo(cx, cy, radio, capa);

        if (borde is null)
        {
            return;
        }

        var h = Hatch(borde, PatronConcreto, escala, capa, colorAci);

        if (h is null)
        {
            _ = Hatch(borde, PatronRespaldo, escala, capa, colorAci);
        }

        // El círculo del borde SÍ se queda: es el contorno del dado en planta.
    }

    private object? Hatch(object borde, string patron, double escala, string capa, int colorAci)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic h = _cont.AddHatch(0, patron, false);
                h.HatchStyle = 0;

                var ok = AcadArreglos.Llamar(
                    $"AppendOuterLoop del hatch '{patron}' de la zapata",
                    new[] { borde },
                    arr => { h.AppendOuterLoop(arr); },
                    Fallo, Nota);

                if (!ok)
                {
                    Borrar((object)h);
                    return null;
                }

                if (!patron.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    h.PatternScale = escala;
                }

                h.Layer = capa;

                if (colorAci > 0)
                {
                    h.Color = colorAci;
                }

                h.Evaluate();

                h.Layer = capa;

                if (colorAci > 0)
                {
                    h.Color = colorAci;
                }

                return (object?)h;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}' de la zapata", ex);
            return null;
        }
    }

    /// <summary>Port de <c>RellenarPoligonoSolido</c> para un cuadrilátero.</summary>
    /// <remarks>
    /// Los rellenos del acero se hacen <b>solo</b> con cuadriláteros, triángulos y círculos, que
    /// son las fronteras que AutoCAD nunca rechaza. Con la cápsula o el gancho completos como
    /// frontera, el hatch fallaba y el acero salía hueco: es lo que la macro resolvió en su V5.
    /// </remarks>
    private void RellenarQuad(
        double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4,
        string capa, int colorAci)
    {
        var borde = Polilinea(new[] { x1, y1, x2, y2, x3, y3, x4, y4 }, capa, cerrada: true);

        if (borde is null)
        {
            return;
        }

        var h = Hatch(borde, "SOLID", 1, capa, colorAci);

        Borrar(borde);

        if (h is null)
        {
            Nota("AutoCAD rechazó un relleno de acero; esa varilla queda dibujada hueca.");
        }
    }

    private void RellenarTriangulo(
        double x1, double y1, double x2, double y2, double x3, double y3, string capa)
    {
        var borde = Polilinea(new[] { x1, y1, x2, y2, x3, y3 }, capa, cerrada: true);

        if (borde is null)
        {
            return;
        }

        _ = Hatch(borde, "SOLID", 1, capa, 0);

        Borrar(borde);
    }

    private void RellenarCirculo(double cx, double cy, double radio, string capa, int colorAci)
    {
        var borde = Circulo(cx, cy, radio, capa);

        if (borde is null)
        {
            return;
        }

        _ = Hatch(borde, "SOLID", 1, capa, colorAci);

        Borrar(borde);
    }

    /// <summary>Port de <c>RellenarBandaSegmentada</c>: el relleno se corta como el contorno.</summary>
    private void RellenarBandaSegmentada(
        double yc, double r, string capa, double[] centros, double rGap,
        double xIni, double xFin)
    {
        if (xFin <= xIni + 1e-7)
        {
            return;
        }

        var desde = xIni;

        foreach (var c in centros)
        {
            var a = c - rGap;
            var b = c + rGap;

            if (a > xFin)
            {
                break;
            }

            if (b > desde)
            {
                if (a > desde)
                {
                    var hasta = Math.Min(a, xFin);
                    RellenarQuad(desde, yc - r, hasta, yc - r, hasta, yc + r, desde, yc + r,
                        capa, 0);
                }

                desde = b;
            }
        }

        if (xFin > desde + 1e-7)
        {
            RellenarQuad(desde, yc - r, xFin, yc - r, xFin, yc + r, desde, yc + r, capa, 0);
        }
    }

    /// <summary>Port de <c>RellenarGanchoLSolido</c>: el doblez en abanico.</summary>
    private void RellenarGanchoL(
        double x0, double yc, double r, double dBar, double hook, string capa,
        double sx, double sy)
    {
        if (dBar <= 0 || hook <= 0)
        {
            return;
        }

        var n = SegmentosArcoRelleno;
        var pi = Math.PI;

        var c1x = x0 + (sx * dBar);
        var c1y = yc + (sy * r);
        var c2x = x0 + (sx * (dBar + r));
        var c2y = yc + (sy * dBar);

        var angA = sx > 0 ? pi : 0;
        var angB = sy > 0 ? -pi / 2 : pi / 2;

        var xOut = new double[n + 1];
        var yOut = new double[n + 1];
        var xIn = new double[n + 1];
        var yIn = new double[n + 1];

        PuntosArco(xOut, yOut, c1x, c1y, dBar, angA, angB, n);
        PuntosArco(xIn, yIn, c2x, c2y, r, angA, angB, n);

        for (var i = 0; i < n; i++)
        {
            RellenarQuad(xOut[i], yOut[i], xOut[i + 1], yOut[i + 1],
                xIn[i + 1], yIn[i + 1], xIn[i], yIn[i], capa, 0);
        }

        // El arranque de la pata y la cuña donde el doblez se junta con el tramo recto: son los
        // dos triángulos que la macro agregó porque quedaban «picos» sin rellenar.
        RellenarTriangulo(x0, yc + (sy * r), x0, yc + (sy * dBar),
            x0 + (sx * dBar), yc + (sy * dBar), capa);

        RellenarTriangulo(x0 + (sx * dBar), yc - (sy * r),
            x0 + (sx * (dBar + r)), yc - (sy * r),
            x0 + (sx * (dBar + r)), yc + (sy * r), capa);

        var yTip = yc + (sy * (r + hook));

        RellenarQuad(x0, yc + (sy * dBar), x0 + (sx * dBar), yc + (sy * dBar),
            x0 + (sx * dBar), yTip, x0, yTip, capa, 0);
    }

    /// <summary>Port de <c>RellenarGanchoParrillaSolido</c>: sector anular más pata.</summary>
    private void RellenarGanchoParrilla(
        double yBarra, double diam, double longGancho, string capa,
        double xCara, double si, double sl)
    {
        if (diam <= 0 || longGancho <= 0)
        {
            return;
        }

        var n = SegmentosArcoRelleno;
        var pi = Math.PI;

        var r = diam / 2;
        var radioInt = diam / 2;
        var radioExt = diam + (diam / 2);

        var cx = xCara + (si * diam);
        var cy = yBarra + (sl * diam);

        var ang1 = sl > 0 ? -pi / 2 : pi / 2;
        var ang2 = si > 0 ? pi : 0;

        var xOut = new double[n + 1];
        var yOut = new double[n + 1];
        var xIn = new double[n + 1];
        var yIn = new double[n + 1];

        PuntosArco(xOut, yOut, cx, cy, radioExt, ang1, ang2, n);
        PuntosArco(xIn, yIn, cx, cy, radioInt, ang1, ang2, n);

        for (var i = 0; i < n; i++)
        {
            RellenarQuad(xOut[i], yOut[i], xOut[i + 1], yOut[i + 1],
                xIn[i + 1], yIn[i + 1], xIn[i], yIn[i], capa, 0);
        }

        var y1 = cy;
        var y2 = cy + (sl * longGancho);

        RellenarQuad(xCara - r, Math.Min(y1, y2), xCara + r, Math.Min(y1, y2),
            xCara + r, Math.Max(y1, y2), xCara - r, Math.Max(y1, y2), capa, 0);
    }

    /// <summary>Port de <c>PuntosArco</c>: siempre el barrido corto entre los dos ángulos.</summary>
    private static void PuntosArco(
        double[] xs, double[] ys, double cx, double cy, double radio,
        double a1, double a2, int n)
    {
        var pi = Math.PI;
        var d = a2 - a1;

        while (d > pi)
        {
            d -= 2 * pi;
        }

        while (d <= -pi)
        {
            d += 2 * pi;
        }

        for (var i = 0; i <= n; i++)
        {
            var a = a1 + (d * ((double)i / n));

            xs[i] = cx + (radio * Math.Cos(a));
            ys[i] = cy + (radio * Math.Sin(a));
        }
    }

    /// <summary>Una cota alineada, con el estilo de la macro. Devuelve 1 si se puso.</summary>
    private int Cota(
        double x1, double y1, double x2, double y2, double xt, double yt,
        bool vertical, bool dentro)
    {
        if (Math.Abs(x2 - x1) < 1e-6 && Math.Abs(y2 - y1) < 1e-6)
        {
            return 0;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(
                    new[] { x1, y1, 0d }, new[] { x2, y2, 0d }, new[] { xt, yt, 0d });

                try
                {
                    d.StyleName = EstiloCota;
                }
                catch (Exception)
                {
                    // Sin el estilo, la cota sale con el activo. No es motivo para perderla.
                }

                // La capa se asigna DESPUÉS del estilo: al revés, el estilo podía dejarla en otra
                // capa. Es el arreglo de la V4 de la macro.
                d.Layer = CapaCotas;

                if (dentro)
                {
                    // DIMTIX + DIMTOFL + DIMTMOVE: el número EN MEDIO y las flechas afuera. Es lo
                    // que hace legible la cota de 5 cm de la plantilla.
                    d.TextInside = true;
                    d.ForceLineInside = true;
                    d.TextMovement = 0;
                }

                if (vertical)
                {
                    d.TextRotation = Math.PI / 2;
                }

                d.Update();
            });

            return 1;
        }
        catch (Exception ex)
        {
            Fallo("Cota de la zapata", ex);
            return 0;
        }
    }

    /// <summary>De dónde se cuelga un texto de una línea.</summary>
    /// <remarks>
    /// Son los dos casos del <c>centrado As Boolean</c> de <c>AgregarTexto</c> en las macros:
    /// <b>Centro</b> para los títulos y los rótulos de las varillas —el punto que se pasa es el
    /// eje del dibujo y el renglón crece parejo hacia los dos lados—, e <b>Izquierda</b> para
    /// los textos que van pegados a algo, como el del hueco del cimiento.
    /// No hay <c>Derecha</c>: alinear los rótulos al paño derecho fue un invento mío del turno
    /// pasado y lo que consiguió fue que el título de una zapata angosta se saliera por el otro
    /// lado. Las macros centran, y centrado se queda.
    /// </remarks>
    /// <summary>
    /// Las dos alineaciones que usa la macro. No hay una tercera a propósito.
    /// </summary>
    /// <remarks>
    /// Se probó alinear el rótulo al paño derecho y quedó peor: el texto se despegaba de su dibujo.
    /// El encimado de los títulos, que era lo que se quería arreglar con eso, se arregla con el
    /// ancho de letra —<see cref="TrazoZapata.FactorLetraTitulo"/>—, no con la alineación.
    /// </remarks>
    private enum Alineacion
    {
        Izquierda,
        Centro
    }

    /// <summary>Un texto de una línea. Port de <c>AgregarTexto</c>, con su alineación.</summary>
    private object? Texto(
        double x, double y, double alto, string texto, string capa, Alineacion alineacion)
    {
        if (string.IsNullOrWhiteSpace(texto) || alto <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _cont.AddText(texto, new[] { x, y, 0d }, alto);
                t.Layer = capa;

                if (alineacion == Alineacion.Centro)
                {
                    try
                    {
                        // 4 = acAlignmentMiddle, que es el centrado que usa la macro.
                        t.HorizontalAlignment = 4;
                        t.VerticalAlignment = 2;
                        t.TextAlignmentPoint = new[] { x, y, 0d };
                    }
                    catch (Exception)
                    {
                        // Alguna versión no acepta la alineación después de crear el texto.
                    }
                }

                return (object?)t;
            });
        }
        catch (Exception ex)
        {
            Fallo("Texto de la zapata", ex);
            return null;
        }
    }

    /// <summary>Port de <c>CrearMTextoCentradoMascara</c> y de <c>CrearMText</c>.</summary>
    private object? Mtexto(
        double x, double y, string texto, double alto, string capa, bool conFondo,
        int anclaje = AnclajeCentro)
    {
        if (string.IsNullOrWhiteSpace(texto) || alto <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic mt = _cont.AddMText(new[] { x, y, 0d }, 0d, texto);
                mt.Layer = capa;
                mt.Height = alto;

                try
                {
                    mt.Width = 0;

                    // 4 = MiddleLeft (crece a la derecha), 5 = MiddleCenter,
                    // 6 = MiddleRight (crece a la izquierda). Es el mismo juego de anclajes
                    // con el que las macros reparten los rótulos de las dos parrillas.
                    mt.AttachmentPoint = anclaje;
                    mt.InsertionPoint = new[] { x, y, 0d };
                }
                catch (Exception)
                {
                    // Sin anclaje centrado el rótulo queda corrido, pero está.
                }

                try
                {
                    if (conFondo)
                    {
                        mt.BackgroundFill = true;
                        mt.BackgroundScaleFactor = 1.1;
                        mt.UseBackgroundColor = true;
                        mt.BackgroundColor = 7;
                    }
                    else
                    {
                        mt.UseBackgroundColor = false;
                        mt.BackgroundFill = false;
                    }
                }
                catch (Exception)
                {
                    // La máscara es presentación: si no se puede, el texto queda igual.
                }

                mt.Update();

                return (object?)mt;
            });
        }
        catch (Exception ex)
        {
            Fallo("Rótulo de la zapata", ex);
            return null;
        }
    }

    /// <summary>Port de <c>AgregarLeaderRecto</c>: la línea y su flecha rellena.</summary>
    private void Leader(double xPunta, double yPunta, double xAnclaje, double yAnclaje)
    {
        var dx = xPunta - xAnclaje;
        var dy = yPunta - yAnclaje;
        var l = Math.Sqrt((dx * dx) + (dy * dy));

        if (l <= 1e-6)
        {
            return;
        }

        Linea(xAnclaje, yAnclaje, xPunta, yPunta, CapaLeader);

        var ux = dx / l;
        var uy = dy / l;
        var px = -uy;
        var py = ux;

        var bx = xPunta - (ux * LargoFlecha);
        var by = yPunta - (uy * LargoFlecha);

        var borde = Polilinea(
            new[]
            {
                xPunta, yPunta,
                bx + (px * AnchoFlecha), by + (py * AnchoFlecha),
                bx - (px * AnchoFlecha), by - (py * AnchoFlecha)
            },
            CapaLeader, cerrada: true);

        if (borde is not null)
        {
            // La flecha SÍ conserva su frontera: es parte del dibujo, no un borde temporal.
            _ = Hatch(borde, "SOLID", 1, CapaLeader, 0);
        }
    }

    // ======================================================================
    // Bloques
    // ======================================================================

    /// <summary>Port de <c>NombreBloqueLibre</c>: id, id-2, id-3…</summary>
    private string NombreBloqueLibre(string? id)
    {
        var n = (id ?? string.Empty).Trim();

        foreach (var c in new[] { '"', '<', '>', '/', '\\', ':', ';', '?', '*', '|', ',', '=', '`' })
        {
            n = n.Replace(c.ToString(), string.Empty, StringComparison.Ordinal);
        }

        if (n.Length == 0)
        {
            n = "ZAPATA";
        }

        for (var k = 1; k <= 500; k++)
        {
            var prueba = k == 1 ? n : $"{n}-{k}";

            if (!ExisteBloque(prueba))
            {
                return prueba;
            }
        }

        return $"{n}-{DateTime.Now:HHmmss}";
    }

    private bool ExisteBloque(string nombre)
    {
        try
        {
            AcadConnection.Retry(() => { _ = _doc.Blocks.Item(nombre); });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Port de <c>CrearBloqueVacio</c>: se dibuja dentro con coordenadas absolutas.</summary>
    private object? CrearBloqueVacio(string nombre, double x, double y)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
                (object?)_doc.Blocks.Add(new[] { x, y, 0d }, nombre));
        }
        catch (Exception ex)
        {
            Nota($"No se pudo crear el bloque '{nombre}' de la zapata; se dibuja directo en el "
                 + "modelo. " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Inserta un bloque <b>propio</b> —de los que crea este dibujante— en su sitio, sin tocarlo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AQUÍ ESTABA EL DESFASE DE TODO EL DIBUJO. La sección y la planta se estaban insertando con
    /// <see cref="InsertarBloque"/>, que <b>recoloca el bloque por el centro de su caja</b>. Esa
    /// rutina es para el bloque del <b>dado</b>, que viene de otro dibujo y cuyo punto base no se
    /// conoce; aplicada a un bloque propio arrastra el dibujo entero.
    /// </para>
    /// <para>
    /// La cuenta, con una zapata de 1.00 × 0.30 y 1.05 de desplante: la elevación va de
    /// <c>y = −8.05</c> —el fondo de la plantilla— a <c>−6.2</c>, así que el centro de su caja está
    /// en <c>−7.12</c>; al forzar ese centro al punto de inserción <c>−8.00</c>, la geometría
    /// <b>bajaba 88 cm</b>. En X, con el centro en <c>xBase + 0.5</c>, se corría <b>50 cm a la
    /// izquierda</b>. Las cotas y los rótulos, que se dibujan fuera del bloque, se quedaban en su
    /// sitio: de ahí que salieran despegados de la cimentación, y de ahí el «las cotas no están a
    /// la altura de la sección».
    /// </para>
    /// <para>
    /// Un bloque propio no necesita nada de eso: se crea con su punto base en <c>(x, y)</c> y su
    /// geometría se dibuja dentro en coordenadas <b>absolutas</b>, así que insertándolo en ese
    /// mismo punto cae exactamente donde se dibujó. Es lo que hacen las dos macros.
    /// </para>
    /// </remarks>
    private bool InsertarBloquePropio(string nombre, double x, double y, string capa)
    {
        if (!ExisteBloque(nombre))
        {
            return false;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic r = _cont.InsertBlock(new[] { x, y, 0d }, nombre, 1d, 1d, 1d, 0d);
                r.Layer = capa;
                r.Update();
            });

            return true;
        }
        catch (Exception ex)
        {
            Fallo($"Insertar el bloque '{nombre}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Inserta un bloque <b>ajeno</b> que ya exista en el dibujo, recolocándolo por el centro de su
    /// caja. Con <paramref name="alinearDerechaEn"/> lo pega a esa X.
    /// </summary>
    /// <remarks>
    /// <b>Solo para el bloque del DADO</b>, que lo dibujó alguien más y cuyo punto base puede estar
    /// en cualquier parte. Para los bloques propios —la sección y la planta— va
    /// <see cref="InsertarBloquePropio"/>: recolocar uno de esos por su centro mueve el dibujo
    /// entero y lo despega de sus cotas.
    /// </remarks>
    /// <remarks>
    /// Port de <c>InsertarBloqueCentroide</c> y de <c>InsertarBloqueDerecha</c>: se inserta, se
    /// mide su caja y se <b>recoloca</b>, porque el punto de inserción de un bloque no tiene por
    /// qué ser su centro. Sin ese paso, el dado sale corrido de la zapata.
    /// </remarks>
    private bool InsertarBloque(
        string nombre, double x, double y, string capa, double? alinearDerechaEn = null)
    {
        if (!ExisteBloque(nombre))
        {
            return false;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic r = _cont.InsertBlock(new[] { x, y, 0d }, nombre, 1d, 1d, 1d, 0d);
                r.Layer = capa;
                r.Update();

                var caja = CajaEnvolvente((object)r);

                if (caja is null)
                {
                    // Sin caja no se puede recolocar: se queda donde entró.
                    return;
                }

                var mn = caja.Value.Min;
                var mx = caja.Value.Max;

                var dx = alinearDerechaEn is null
                    ? x - ((mn[0] + mx[0]) / 2)
                    : alinearDerechaEn.Value - mx[0];

                var dy = y - ((mn[1] + mx[1]) / 2);

                if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
                {
                    r.Move(new[] { 0d, 0d, 0d }, new[] { dx, dy, 0d });
                    r.Update();
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            Fallo($"Insertar el bloque '{nombre}'", ex);
            return false;
        }
    }

    // ======================================================================
    // Utilidades de entidad
    // ======================================================================

    /// <summary>
    /// La caja envolvente de una entidad, o <c>null</c> si no se pudo obtener.
    /// </summary>
    /// <remarks>
    /// Va por <b>reflexión</b> y no con <c>dynamic</c> a propósito: <c>GetBoundingBox</c> devuelve
    /// sus dos resultados por referencia y el enlace dinámico no los sabe manejar sobre un objeto
    /// COM. Está escrito igual en <c>SeccionDrawer.CajaEnvolvente</c>, donde llamarlo con
    /// <c>dynamic</c> rompía el agrupado en bloques de todas las secciones.
    /// </remarks>
    private (double X1, double Y1, double X2, double Y2)? Caja(object? ent)
    {
        if (ent is null)
        {
            return null;
        }

        var caja = CajaEnvolvente(ent);

        return caja is null
            ? null
            : (caja.Value.Min[0], caja.Value.Min[1], caja.Value.Max[0], caja.Value.Max[1]);
    }

    private (double[] Min, double[] Max)? CajaEnvolvente(object ent)
    {
        try
        {
            var args = new object?[] { null, null };

            var mod = new ParameterModifier(2);
            mod[0] = true;
            mod[1] = true;

            ent.GetType().InvokeMember(
                "GetBoundingBox",
                BindingFlags.InvokeMethod,
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
        catch (Exception ex)
        {
            Nota("No se pudo medir una entidad de la zapata (GetBoundingBox): "
                 + "el rótulo o el dado pueden quedar corridos. " + ex.Message);
            return null;
        }
    }

    private static double[] ADobles(object? v) => v switch
    {
        double[] d => d,
        object[] o => o.Select(x => x is null ? 0d : Convert.ToDouble(x)).ToArray(),
        _ => Array.Empty<double>()
    };

    private void Mover(object? ent, double dx, double dy)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                ((dynamic)ent).Move(new[] { 0d, 0d, 0d }, new[] { dx, dy, 0d });
                ((dynamic)ent).Update();
            });
        }
        catch (Exception)
        {
            // Si no se puede mover, el rótulo queda un poco corrido.
        }
    }

    private void Color(object? ent, int colorAci)
    {
        if (ent is null || colorAci <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).Color = colorAci; });
        }
        catch (Exception)
        {
            Nota("No se pudo dar color a una entidad de la zapata; queda con el de su capa.");
        }
    }

    private void Grosor(object? ent, double grosor)
    {
        if (ent is null || grosor <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).ConstantWidth = grosor; });
        }
        catch (Exception)
        {
            // Sin grosor la línea se queda fina: es presentación.
        }
    }

    private void Transparencia(object? ent, string valor)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).EntityTransparency = valor; });
        }
        catch (Exception)
        {
            Nota("No se pudo dar transparencia al relleno del terreno; queda opaco. Si tapa el "
                 + "acero, apaga la capa " + CapaTerrenoHatch + ".");
        }
    }

    private void Borrar(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() => { ((dynamic)ent).Delete(); });
        }
        catch (Exception)
        {
            // Queda una entidad de más. No vale un aviso.
        }
    }

    private void Fallo(string operacion, Exception ex) =>
        _log.Add($"{operacion}: {ex.Message}");

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }
}
