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

    // Los hatch del muro, del estilo «Muros de contención». Por defecto: GRAVEL a 0.017 -la
    // escala que pidio el usuario- en el ciclopeo, AR-CONC a 0.0015 en el armado y EARTH a 0.01.
    private static string PatronCiclopeo => EstiloMuros.Texto("hatch.ciclopeo");
    private static double EscalaCiclopeo => EstiloMuros.Numero("hatch.ciclopeo.escala");
    private static string PatronConcretoMuro => EstiloMuros.Texto("hatch.concreto");
    private static double EscalaConcretoMuro => EstiloMuros.Numero("hatch.concreto.escala");
    private static string PatronTerrenoMuro => EstiloMuros.Texto("hatch.terreno");
    private static double EscalaTerrenoMuro => EstiloMuros.Numero("hatch.terreno.escala");

    private static PerfilEstilo EstiloMuros => EstiloDibujo.Actual.Perfil(EstiloDibujo.Muros);

    /// <summary>
    /// El estilo de cota de los muros de contencion: IGUAL EN TODO a COTA_ESTRUCTURAL, menos el
    /// alto del numero, que es mas grande.
    /// </summary>
    private const string EstiloCotaMuro = "COTA_MC";

    /// <summary>
    /// Dibuja los muros uno a la derecha del otro, en la fila de <see cref="TrazoMuroContencion.YBase"/>.
    /// </summary>
    public ResumenMuros DibujarMuros(IReadOnlyList<MuroContencionCad> muros)
    {
        var r = new ResumenMuros();

        AsegurarCapasBase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();
        AsegurarEstiloCotaMuro();

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
            HatchPoligono(Plano(t), CapaTerrenoHatch, PatronTerrenoMuro, EscalaTerrenoMuro, string.Empty, 0);
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
                HatchPoligono(pts, CapaConcreto, PatronConcretoMuro, EscalaConcretoMuro, string.Empty, ColorPatronRelleno);
            }
            else
            {
                HatchPoligono(pts, CapaConcreto, PatronConcretoMuro, EscalaConcretoMuro, string.Empty, 0);
            }
        }

        foreach (var l in d.Lineas)
        {
            Polilinea(Plano(l.Puntos), CapaConcreto, l.Cerrada);
        }

        // ---------- El acero: con su DIAMETRO REAL, a dos lineas ----------
        // Cada varilla va en VAR_#n, con el color de la macro para ese diametro, y se dibuja
        // con sus DOS caras a medio diametro del eje -una #5 mide 1.59 cm de cara a cara-. Sin
        // ancho de polilinea: el grosor es geometria de verdad, se acota y se ve igual a
        // cualquier escala de impresion.
        foreach (var v in d.Varillas)
        {
            var capa = CapaVar(v.Clave);
            AsegurarCapaVarilla(capa);

            var contorno = TrazoMuroContencion.ContornoVarilla(
                v.Puntos, TrazoMuroContencion.DiametroM(m, v.Clave));

            var pl = Polilinea(Plano(contorno), capa, contorno.Count > v.Puntos.Count);

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
            r.Cotas += CotaMuro(c, EstiloCotaMuro);
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

    /// <summary>
    /// Crea <c>COTA_MC</c>: las MISMAS variables que <c>COTA_ESTRUCTURAL</c> -huecos, unidades,
    /// decimales- con el alto del numero y la marca del estilo «Muros de contención» (por
    /// defecto 0.08, y la marca de las zapatas). Despues deja las variables y el
    /// estilo activo como estaban, para que las zapatas sigan con el suyo.
    /// </summary>
    private void AsegurarEstiloCotaMuro()
    {
        // AsegurarEstiloCota ya fijo todas las variables de COTA_ESTRUCTURAL: aqui se cambia el
        // alto del numero -y la marca, si en «Estilo de dibujo» se eligio otra- y se copia ese
        // estado al estilo nuevo.
        Dimvar("DIMTXT", TrazoMuroContencion.AltoCotaMuro);
        Dimvar("DIMASZ", TrazoMuroContencion.MarcaCotaMuro);
        Dimvar("DIMSAH", 0);
        Dimvar("DIMBLK", TrazoMuroContencion.BloqueMarcaMuro);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(EstiloCotaMuro);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(EstiloCotaMuro);
                }

                estilo.CopyFrom(_doc);
            });
        }
        catch (Exception)
        {
            Nota($"No se pudo crear el estilo de cota '{EstiloCotaMuro}'; las cotas del muro usan "
                 + $"'{EstiloCota}'.");
        }

        // Y todo de vuelta a COTA_ESTRUCTURAL.
        AsegurarEstiloCota();
    }

    /// <summary>Una cota del muro, en su estilo y SIN cambios encima: todo lo da el estilo.</summary>
    private int CotaMuro(TrazoCota c, string estiloCota)
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
                    d.StyleName = estiloCota;
                }
                catch (Exception)
                {
                    // Sin el estilo, la cota sale con el activo.
                }

                d.Layer = CapaCotas;

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
