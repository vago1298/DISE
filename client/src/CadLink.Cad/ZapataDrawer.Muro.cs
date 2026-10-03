namespace CadLink.Cad;

/// <summary>
/// Los <b>muros de contencion</b>: de concreto armado y de concreto ciclopeo.
/// </summary>
/// <remarks>
/// <para>
/// Va como parcial del dibujante de zapatas para reutilizar sus primitivas -capas, hatch,
/// textos, llamadas- en vez de copiar otras ochocientas lineas de COM.
/// </para>
/// <para>
/// La geometria NO se calcula aqui: la da <see cref="TrazoMuroContencion"/>, la misma que pinta
/// la vista previa de CadLink. Este archivo solo la pasa a AutoCAD.
/// </para>
/// </remarks>
public sealed partial class ZapataDrawer
{
    /// <summary>Lo que se dibujo, para el aviso de «Listo».</summary>
    public sealed class ResumenMuros
    {
        public int Armados { get; set; }

        public int Ciclopeos { get; set; }

        public int Varillas { get; set; }

        public int Cotas { get; set; }

        public override string ToString() =>
            (Ciclopeos == 0 ? $"{Armados} muro(s) de concreto armado.\n"
                : Armados == 0 ? $"{Ciclopeos} muro(s) de concreto ciclopeo.\n"
                : $"{Armados} muro(s) de concreto armado y {Ciclopeos} de concreto ciclopeo.\n")
            + $"{Varillas} varilla(s) y {Cotas} cota(s).";
    }

    private const string PatronCiclopeo = "GRAVEL";

    /// <summary>El AR-CONC de un muro: mas grande que el de una zapata, que mide la quinta parte.</summary>
    private const double EscalaConcretoMuro = 0.0015;

    /// <summary>La escala del GRAVEL del ciclopeo, la que pidio el usuario.</summary>
    private const double EscalaCiclopeo = 0.0170;

    /// <summary>El tamano de las puntas de flecha de las cotas del muro.</summary>
    private const double FlechaCotaMuro = 0.08;

    /// <summary>
    /// Dibuja los muros uno a la derecha del otro, en la fila de <see cref="TrazoMuroContencion.YBase"/>.
    /// </summary>
    public ResumenMuros DibujarMuros(IReadOnlyList<MuroContencionCad> muros)
    {
        var r = new ResumenMuros();

        AsegurarCapasBase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();

        _relleno = SeccionRellena;
        _cont = _ms;

        // Cada tipo en su fila: los armados en YBase y los ciclopeos mas abajo.
        var xArmado = 0d;
        var xCiclopeo = 0d;

        foreach (var m in muros)
        {
            try
            {
                var d = m.EsCiclopeo
                    ? TrazoMuroContencion.Dibujar(m, xCiclopeo, TrazoMuroContencion.YBaseCiclopeo)
                    : TrazoMuroContencion.Dibujar(m, xArmado, TrazoMuroContencion.YBase);

                DibujarMuro(m, d, r);

                if (m.EsCiclopeo)
                {
                    r.Ciclopeos++;
                    xCiclopeo = d.XMax + TrazoMuroContencion.SeparacionMuros;
                }
                else
                {
                    r.Armados++;
                    xArmado = d.XMax + TrazoMuroContencion.SeparacionMuros;
                }
            }
            catch (Exception ex)
            {
                // Un muro que falla no se lleva a los demas.
                Fallo($"Muro de contencion '{m.Id}'", ex);
            }
        }

        return r;
    }

    private void DibujarMuro(MuroContencionCad m, DibujoMuro d, ResumenMuros r)
    {
        // ---------- El terreno, debajo de todo ----------
        foreach (var t in d.Terreno)
        {
            HatchPoligono(Plano(t), CapaTerrenoHatch, PatronTerreno, EscalaTerreno, string.Empty, 0);
        }

        // ---------- El concreto: contorno y relleno ----------
        foreach (var c in d.Concreto)
        {
            var pts = Plano(c);

            Polilinea(pts, CapaConcreto, true);

            if (m.EsCiclopeo)
            {
                HatchPoligono(pts, CapaConcreto, PatronCiclopeo, EscalaCiclopeo, string.Empty, 0);
            }
            else if (_relleno)
            {
                HatchPoligono(pts, CapaConcreto, "SOLID", 1, string.Empty, ColorSolidoRelleno);
                HatchPoligono(pts, CapaConcreto, PatronConcreto, EscalaConcretoMuro, string.Empty, ColorPatronRelleno);
            }
            else
            {
                HatchPoligono(pts, CapaConcreto, PatronConcreto, EscalaConcretoMuro, string.Empty, 0);
            }
        }

        foreach (var l in d.Lineas)
        {
            Polilinea(Plano(l.Puntos), CapaConcreto, l.Cerrada);
        }

        // ---------- El acero: con su GROSOR REAL y en la capa de su diametro ----------
        // Cada varilla va en VAR_#n, con el color de la macro para ese diametro, y la polilinea
        // tiene de ancho el diametro de la varilla: una #5 sale de 1.59 cm de gruesa.
        foreach (var v in d.Varillas)
        {
            var capa = CapaVar(v.Clave);
            AsegurarCapaVarilla(capa);

            var pl = Polilinea(Plano(v.Puntos), capa, v.Cerrada);

            Grueso(pl, TrazoMuroContencion.DiametroM(m, v.Clave));

            if (v.Oculta)
            {
                ATrazos(pl);
            }

            r.Varillas++;
        }

        // Las de punta: un circulo del diametro real, relleno del color de su capa.
        foreach (var p in d.Puntos)
        {
            var capa = CapaVar(p.Clave);
            AsegurarCapaVarilla(capa);

            var radio = Math.Max(p.R, 0.003);

            RellenarCirculo(p.X, p.Y, radio, capa, 0);
            Circulo(p.X, p.Y, radio, capa);
            r.Varillas++;
        }

        // ---------- Cotas ----------
        foreach (var c in d.Cotas)
        {
            r.Cotas += CotaMuro(c, m.EsCiclopeo
                ? TrazoMuroContencion.AltoCotaCiclopeo
                : TrazoMuroContencion.AltoCotaArmado);
        }

        // ---------- Llamadas y textos ----------
        foreach (var t in d.Rotulos)
        {
            Rotulo(t);
        }

        // Centrados en (X, Y), como en la vista previa: el Centro de Texto ya es «medio-medio».
        foreach (var t in d.Textos)
        {
            Texto(t.X, t.Y, t.Alto, t.Texto, CapaRotulos, Alineacion.Centro);
        }
    }

    /// <summary>
    /// Una llamada: flecha en la punta, quiebre en el codo y el texto ENCIMA de un hombro
    /// horizontal, alineado con los demas de su columna. Ninguna linea lo atraviesa.
    /// </summary>
    /// <remarks>
    /// El texto se escribe primero y se mide de verdad: el hombro tiene que acabar donde acaba
    /// la palabra, y una llamada a la izquierda tiene que acabar justo en su codo, como las otras
    /// de su columna. La estimacion de la geometria solo queda si AutoCAD no da la medida.
    /// </remarks>
    private void Rotulo(TrazoRotulo t)
    {
        var alto = TrazoMuroContencion.AltoRotulo;
        var aLaIzquierda = t.XHombro < t.XCodo;

        var txt = Texto(t.XTexto, t.YTexto, alto, t.Texto, CapaRotulos, Alineacion.Izquierda);
        var xHombro = t.XHombro;
        var caja = Caja(txt);

        if (caja is not null)
        {
            var (x1, _, x2, _) = caja.Value;

            if (aLaIzquierda)
            {
                // Que acabe a 2 cm del codo, como las demas de su columna.
                var dx = (t.XCodo - 0.02) - x2;
                Mover(txt, dx, 0);
                xHombro = x1 + dx - 0.02;
            }
            else
            {
                xHombro = x2 + 0.02;
            }
        }

        LeaderQuebrado(t.XPunta, t.YPunta, t.XCodo, t.YCodo, xHombro, t.YCodo);
    }

    /// <summary>El grosor real de una varilla: el ancho constante de su polilinea.</summary>
    private void Grueso(object? pl, double ancho)
    {
        if (pl is null || ancho <= 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic p = pl;
                p.ConstantWidth = ancho;
                p.Update();
            });
        }
        catch (Exception)
        {
            Nota("Una varilla del muro no acepto su grosor: salio como linea fina.");
        }
    }

    /// <summary>Una cota del muro, con el alto de numero que le toca.</summary>
    private int CotaMuro(TrazoCota c, double altoNumero)
    {
        if (Math.Abs(c.X2 - c.X1) < 1e-6 && Math.Abs(c.Y2 - c.Y1) < 1e-6)
        {
            return 0;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(
                    new[] { c.X1, c.Y1, 0d }, new[] { c.X2, c.Y2, 0d }, new[] { c.Xt, c.Yt, 0d });

                try
                {
                    d.StyleName = EstiloCota;
                }
                catch (Exception)
                {
                    // Sin el estilo, la cota sale con el activo.
                }

                d.Layer = CapaCotas;

                // El numero, al alto que toca y no al del estilo: con ScaleFactor el alto sale del
                // estilo multiplicado, y el estilo de las zapatas trae letra de 2.5 cm.
                try
                {
                    d.ScaleFactor = 1.0;
                    d.TextHeight = altoNumero;
                    d.ArrowheadSize = FlechaCotaMuro;
                    d.TextGap = altoNumero / 4;
                    d.ExtensionLineExtend = altoNumero / 2;
                }
                catch (Exception)
                {
                    // Se queda con lo del estilo: se lee, pero mas chico.
                }

                if (c.Vertical)
                {
                    d.TextRotation = Math.PI / 2;
                }

                d.Update();
            });

            return 1;
        }
        catch (Exception ex)
        {
            Fallo("Cota del muro", ex);
            return 0;
        }
    }

    /// <summary>La espiga va a trazos: carga el tipo de linea si no esta y se lo pone.</summary>
    private void ATrazos(object? ent)
    {
        if (ent is null)
        {
            return;
        }

        const string tipo = "DASHED";

        try
        {
            AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Linetypes.Item(tipo);
                }
                catch (Exception)
                {
                    try
                    {
                        _doc.Linetypes.Load(tipo, "acad.lin");
                    }
                    catch (Exception)
                    {
                        _doc.Linetypes.Load(tipo, "acadiso.lin");
                    }
                }

                dynamic e = ent;
                e.Linetype = tipo;
                e.LinetypeScale = 0.2;
                e.Update();
            });
        }
        catch (Exception)
        {
            Nota("No se pudo cargar el tipo de linea DASHED: las espigas salen continuas.");
        }
    }

    private static double[] Plano(IEnumerable<(double X, double Y)> pts) =>
        pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();
}
