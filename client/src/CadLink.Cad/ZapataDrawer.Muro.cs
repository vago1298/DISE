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
            $"{Armados} muro(s) de concreto armado y {Ciclopeos} de concreto ciclopeo.\n"
            + $"{Varillas} varilla(s) y {Cotas} cota(s).";
    }

    private const string CapaMuroAcero = "ACERO MURO";
    private const string PatronCiclopeo = "GRAVEL";

    /// <summary>El AR-CONC de un muro: mas grande que el de una zapata, que mide la quinta parte.</summary>
    private const double EscalaConcretoMuro = 0.0015;

    /// <summary>Las piedras del ciclopeo: unos 15 cm.</summary>
    private const double EscalaCiclopeo = 0.15;

    /// <summary>
    /// Las cotas de un muro, al triple que las de una zapata: un muro de 5 m con cotas de 2.5 cm
    /// de letra no se lee en un plano a 1:50.
    /// </summary>
    private const double EscalaCotasMuro = 3.0;

    /// <summary>
    /// Dibuja los muros uno a la derecha del otro, en la fila de <see cref="TrazoMuroContencion.YBase"/>.
    /// </summary>
    public ResumenMuros DibujarMuros(IReadOnlyList<MuroContencionCad> muros)
    {
        var r = new ResumenMuros();

        AsegurarCapasBase();
        AsegurarEstiloTexto();
        AsegurarEstiloCota();

        if (_capas.Add(CapaMuroAcero))
        {
            CrearCapa(CapaMuroAcero, 1, false);
        }

        _relleno = SeccionRellena;
        _cont = _ms;

        var x = 0d;

        foreach (var m in muros)
        {
            try
            {
                var d = TrazoMuroContencion.Dibujar(m, x, TrazoMuroContencion.YBase);

                DibujarMuro(m, d, r);

                if (m.EsCiclopeo) { r.Ciclopeos++; } else { r.Armados++; }

                x = d.XMax + TrazoMuroContencion.SeparacionMuros;
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

        // ---------- El acero ----------
        foreach (var v in d.Varillas)
        {
            var pl = Polilinea(Plano(v.Puntos), CapaMuroAcero, v.Cerrada);

            if (v.Oculta)
            {
                ATrazos(pl);
            }

            r.Varillas++;
        }

        foreach (var p in d.Puntos)
        {
            CirculoRelleno(p.X, p.Y, Math.Max(p.R, 0.004), CapaMuroAcero);
            r.Varillas++;
        }

        // ---------- Cotas ----------
        foreach (var c in d.Cotas)
        {
            r.Cotas += CotaMuro(c);
        }

        // ---------- Llamadas y textos ----------
        foreach (var t in d.Rotulos)
        {
            LeaderQuebrado(t.XPunta, t.YPunta, t.XCodo, t.YCodo, t.XTexto, t.YTexto);
            Texto(t.XTexto, t.YTexto - (TrazoMuroContencion.AltoRotulo / 2),
                TrazoMuroContencion.AltoRotulo, t.Texto, CapaRotulos, Alineacion.Izquierda);
        }

        foreach (var t in d.Textos)
        {
            Texto(t.X, t.Y - (t.Alto / 2), t.Alto, t.Texto, CapaRotulos, Alineacion.Centro);
        }
    }

    /// <summary>Una cota del muro, con su escala propia.</summary>
    private int CotaMuro(TrazoCota c)
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

                try
                {
                    d.ScaleFactor = EscalaCotasMuro;
                }
                catch (Exception)
                {
                    // Con la escala del estilo se sigue leyendo; solo sale mas chica.
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
