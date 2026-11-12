namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LAS VISTAS DEL DETALLE: CADA PIEZA PROYECTADA EN EL PAPEL
//
//  Una vista es una proyeccion ortogonal: la derecha y el arriba del papel en los ejes de la
//  conexion. Cada pieza es un prisma, asi que su proyeccion es la envolvente convexa de sus
//  vertices proyectados: la placa de frente sale con su contorno, la de canto como un
//  rectangulo de su espesor, sin casos aparte. Todo sale en METROS; quien dibuja escala.
// ============================================================================

/// <summary>En que capa va cada cosa del detalle.</summary>
public enum CapaDetalle
{
    Perfil,
    PerfilOculto,
    Eje,
    Placa,
    Tornillo,
    Soldadura,
    Cota,
    Texto,

    /// <summary>Los perfiles secundarios (la viga que llega, las diagonales): otra capa y otro color.</summary>
    PerfilSecundario
}

public sealed record Linea2D(double X1, double Y1, double X2, double Y2, CapaDetalle Capa);

public sealed record Poligono2D(IReadOnlyList<(double X, double Y)> Puntos, bool Cerrado, CapaDetalle Capa, bool Relleno);

public sealed record Circulo2D(double X, double Y, double R, CapaDetalle Capa);

/// <summary>Una llamada: de un punto de la pieza a su rotulo.</summary>
/// <param name="Soldadura">Si es de un cordon: se llama con el simbolo de soldadura.</param>
public sealed record Llamada2D(double X, double Y, string Texto, CapaDetalle Capa)
{
    public CordonPieza? Soldadura { get; init; }
}

/// <summary>Una cota alineada de (X1, Y1) a (X2, Y2), desplazada hacia el lado (Nx, Ny).</summary>
public sealed record Cota2D(double X1, double Y1, double X2, double Y2, double Nx, double Ny, string Texto);

/// <summary>Una vista ya proyectada.</summary>
public sealed class Vista2D
{
    public string Titulo { get; init; } = string.Empty;

    public List<Linea2D> Lineas { get; } = new();

    public List<Poligono2D> Poligonos { get; } = new();

    public List<Circulo2D> Circulos { get; } = new();

    public List<Llamada2D> Llamadas { get; } = new();

    public List<Cota2D> Cotas { get; } = new();

    public double MinX { get; private set; } = double.MaxValue;

    public double MinY { get; private set; } = double.MaxValue;

    public double MaxX { get; private set; } = double.MinValue;

    public double MaxY { get; private set; } = double.MinValue;

    public double Ancho => MaxX > MinX ? MaxX - MinX : 0;

    public double Alto => MaxY > MinY ? MaxY - MinY : 0;

    internal void Abarcar(double x, double y)
    {
        MinX = Math.Min(MinX, x);
        MinY = Math.Min(MinY, y);
        MaxX = Math.Max(MaxX, x);
        MaxY = Math.Max(MaxY, y);
    }
}

public static class VistasConexion
{
    /// <summary>Las vistas del modelo, proyectadas.</summary>
    public static List<Vista2D> Proyectar(ModeloConexion m) => m.Vistas.Select(v => Proyectar(m, v)).ToList();

    public static Vista2D Proyectar(ModeloConexion m, VistaPieza vista)
    {
        var der = vista.Derecha.Unitario();
        var arr = vista.Arriba.Unitario();
        var hacia = der.Cruz(arr).Unitario();
        var r = new Vista2D { Titulo = vista.Titulo };

        (double X, double Y) P(V3 p) => (p.Punto(der), p.Punto(arr));

        void Linea(V3 a, V3 b, CapaDetalle capa)
        {
            var (x1, y1) = P(a);
            var (x2, y2) = P(b);

            if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 1e-6)
            {
                return;
            }

            r.Lineas.Add(new Linea2D(x1, y1, x2, y2, capa));
            r.Abarcar(x1, y1);
            r.Abarcar(x2, y2);
        }

        void Envolvente(IEnumerable<V3> puntos, CapaDetalle capa, bool relleno)
        {
            var e = PiezasLocales.Envolvente(puntos.Select(P).Select(q => (Math.Round(q.X, 6), Math.Round(q.Y, 6))));

            if (e.Count < 2)
            {
                return;
            }

            r.Poligonos.Add(new Poligono2D(e.Select(q => (q.U, q.V)).ToList(), e.Count >= 3, capa, relleno));

            foreach (var q in e)
            {
                r.Abarcar(q.U, q.V);
            }
        }

        // ---- Los miembros: de lado sus lineas, de frente su seccion. Los principales (columna,
        // trabe, la viga que soporta) y los secundarios (la viga que llega, las diagonales) van en
        // capas distintas.
        foreach (var mi in m.Miembros)
        {
            var a = (mi.P1 - mi.P0).Unitario();
            var pe = mi.Peralte.Unitario();
            var q = a.Cruz(pe).Unitario();
            var capa = EsSecundario(m, mi) ? CapaDetalle.PerfilSecundario : CapaDetalle.Perfil;

            if (Math.Abs(a.Punto(hacia)) > 0.9)
            {
                Seccion(r, mi, pe, q, P, capa);
                continue;
            }

            // De lado: se ve el peralte si la vista no mira a lo largo de el.
            var vePeralte = Math.Abs(pe.Punto(hacia)) < Math.Abs(q.Punto(hacia));
            var dir = vePeralte ? pe : q;
            var exterior = (vePeralte ? mi.D : mi.B) / 2;
            double interior;
            CapaDetalle capaInterior;

            if (mi.Forma == FormaMiembro.I)
            {
                interior = vePeralte ? exterior - mi.Tf : mi.Tw / 2;
                capaInterior = vePeralte ? capa : CapaDetalle.PerfilOculto;
            }
            else
            {
                interior = exterior - mi.Tw;
                capaInterior = CapaDetalle.PerfilOculto;
            }

            DespatinadoPieza? Despatinado(double s) =>
                mi.Despatinados.FirstOrDefault(c => c.Arriba == (s * pe.Punto(dir) > 0));

            if (vePeralte)
            {
                // De lado se ve el recorte: el patin se corta en escalon.
                var fin = new Dictionary<double, double>();

                foreach (var s in new[] { -1.0, 1.0 })
                {
                    var c = Despatinado(s);
                    var arranque = c is null ? mi.P0 : mi.P0 + (a * c.Largo);
                    Linea(arranque + (dir * (s * exterior)), mi.P1 + (dir * (s * exterior)), capa);

                    if (c is not null)
                    {
                        var abajo = exterior - c.Profundo;
                        Linea(arranque + (dir * (s * exterior)), arranque + (dir * (s * abajo)), capa);
                        Linea(mi.P0 + (dir * (s * abajo)), arranque + (dir * (s * abajo)), capa);
                    }

                    if (interior > 1e-4)
                    {
                        var desde = c is not null && exterior - c.Profundo <= interior + 1e-6 ? arranque : mi.P0;
                        Linea(desde + (dir * (s * interior)), mi.P1 + (dir * (s * interior)), capaInterior);
                    }

                    fin[s] = c is null ? exterior : exterior - c.Profundo;
                }

                Linea(mi.P0 + (dir * fin[1.0]), mi.P0 - (dir * fin[-1.0]), capa);
            }
            else
            {
                // De planta, en lo despatinado ya no hay patin: se ve el canto del alma.
                var c = mi.Despatinados.FirstOrDefault();
                var arranque = c is null ? mi.P0 : mi.P0 + (a * c.Largo);

                foreach (var s in new[] { -1.0, 1.0 })
                {
                    Linea(arranque + (dir * (s * exterior)), mi.P1 + (dir * (s * exterior)), capa);

                    if (interior > 1e-4)
                    {
                        Linea(arranque + (dir * (s * interior)), mi.P1 + (dir * (s * interior)), capaInterior);
                    }

                    if (c is not null && mi.Forma == FormaMiembro.I)
                    {
                        Linea(mi.P0 + (dir * (s * mi.Tw / 2)), arranque + (dir * (s * mi.Tw / 2)), capa);
                    }
                }

                if (c is not null && mi.Forma == FormaMiembro.I)
                {
                    Linea(arranque + (dir * exterior), arranque - (dir * exterior), capa);
                    Linea(mi.P0 + (dir * (mi.Tw / 2)), mi.P0 - (dir * (mi.Tw / 2)), capa);
                }
                else
                {
                    Linea(mi.P0 + (dir * exterior), mi.P0 - (dir * exterior), capa);
                }
            }

            Linea(mi.P1 + (dir * exterior), mi.P1 - (dir * exterior), capa);
            Linea(mi.P0 - (a * 0.05), mi.P1 + (a * 0.05), CapaDetalle.Eje);
        }

        // ---- Lo que solo se dibuja.
        foreach (var t in m.Trazos)
        {
            var puntos = t.Puntos.Select(P).ToList();
            r.Poligonos.Add(new Poligono2D(puntos, t.Cerrado, t.Oculto ? CapaDetalle.PerfilOculto : CapaDetalle.Perfil, false));

            foreach (var (x, y) in puntos)
            {
                r.Abarcar(x, y);
            }
        }

        // ---- Las piezas.
        var rotulos = new Dictionary<string, (double X, double Y, CapaDetalle Capa)>(StringComparer.Ordinal);
        var cordonDe = new Dictionary<string, CordonPieza>(StringComparer.Ordinal);

        void Rotulo(string texto, IEnumerable<V3> puntos, CapaDetalle capa, CordonPieza? cordon = null)
        {
            if (cordon is not null && !cordonDe.ContainsKey(texto))
            {
                cordonDe[texto] = cordon;
            }

            if (string.IsNullOrWhiteSpace(texto) || rotulos.ContainsKey(texto))
            {
                return;
            }

            var lista = puntos.Select(P).ToList();

            if (lista.Count == 0)
            {
                return;
            }

            rotulos[texto] = (lista.Average(q => q.X), lista.Average(q => q.Y), capa);
        }

        foreach (var pl in m.Placas)
        {
            Envolvente(pl.Vertices(), CapaDetalle.Placa, false);
            Rotulo(pl.Rotulo, pl.Vertices(), CapaDetalle.Placa);
        }

        foreach (var c in m.Cordones)
        {
            Envolvente(c.Vertices(), CapaDetalle.Soldadura, true);
            Rotulo(c.Rotulo, c.Vertices(), CapaDetalle.Soldadura, c);
        }

        foreach (var t in m.Tornillos)
        {
            if (Math.Abs(t.Eje.Punto(hacia)) > 0.9)
            {
                var (x, y) = P(t.Desde);
                r.Circulos.Add(new Circulo2D(x, y, t.Diametro / 2, CapaDetalle.Tornillo));
                r.Abarcar(x - (t.Diametro / 2), y - (t.Diametro / 2));
                r.Abarcar(x + (t.Diametro / 2), y + (t.Diametro / 2));
                Rotulo(t.Rotulo, new[] { t.Desde }, CapaDetalle.Tornillo);
                continue;
            }

            // De lado, el cilindro se ve como un rectangulo de su diametro: el ancho va en el papel,
            // perpendicular al eje.
            var ladoVisible = hacia.Cruz(t.Eje).Unitario() * (t.Diametro / 2);
            Envolvente(new[] { t.Desde + ladoVisible, t.Desde - ladoVisible, t.Hasta + ladoVisible, t.Hasta - ladoVisible },
                CapaDetalle.Tornillo, false);
            Linea(t.Desde - (t.Eje * 0.005), t.Hasta + (t.Eje * 0.005), CapaDetalle.Eje);
            Rotulo(t.Rotulo, new[] { t.Desde, t.Hasta }, CapaDetalle.Tornillo);
        }

        foreach (var (texto, (x, y, capa)) in rotulos)
        {
            r.Llamadas.Add(new Llamada2D(x, y, texto, capa) { Soldadura = cordonDe.GetValueOrDefault(texto) });
        }

        // ---- Las cotas que se leen en esta vista: las que no van a lo largo de la mirada.
        foreach (var c in m.Cotas)
        {
            var d = c.B - c.A;

            if (Math.Abs(d.Unitario().Punto(hacia)) > 0.2)
            {
                continue;
            }

            var (x1, y1) = P(c.A);
            var (x2, y2) = P(c.B);
            var (nx, ny) = P(c.Lado);
            var l = Math.Sqrt((nx * nx) + (ny * ny));

            if (l < 0.3 || Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1))) < 1e-4)
            {
                continue;
            }

            r.Cotas.Add(new Cota2D(x1, y1, x2, y2, nx / l, ny / l, c.Texto));
        }

        if (r.MinX > r.MaxX)
        {
            r.Abarcar(0, 0);
        }

        return r;
    }

    /// <summary>
    /// Si el miembro es secundario: la viga que llega a una columna, a una trabe o a la viga que la
    /// soporta, y las diagonales. Los principales son la columna, la trabe y la viga que soporta;
    /// si no hay ninguno (contraventeos, empalmes), las vigas son las principales.
    /// </summary>
    public static bool EsSecundario(ModeloConexion m, MiembroPieza mi)
    {
        static bool Principal(string que) => que is "Columna" or "Trabe" or "Viga que soporta";

        if (Principal(mi.Que))
        {
            return false;
        }

        if (mi.Que.StartsWith("Diagonal", StringComparison.Ordinal))
        {
            return true;
        }

        return m.Miembros.Any(x => Principal(x.Que));
    }

    /// <summary>La seccion de un miembro visto a lo largo de su eje.</summary>
    private static void Seccion(Vista2D r, MiembroPieza mi, V3 pe, V3 q, Func<V3, (double X, double Y)> P, CapaDetalle capa)
    {
        var o = mi.P0;
        var d = mi.D / 2;
        var b = mi.B / 2;

        List<(double, double)> Contorno(IEnumerable<(double S, double T)> local) =>
            local.Select(st => P(o + (pe * st.S) + (q * st.T))).ToList();

        if (mi.Forma == FormaMiembro.I)
        {
            var tf = mi.Tf;
            var tw = mi.Tw / 2;
            var i = Contorno(new[]
            {
                (d, -b), (d, b), (d - tf, b), (d - tf, tw), (-d + tf, tw), (-d + tf, b), (-d, b), (-d, -b),
                (-d + tf, -b), (-d + tf, -tw), (d - tf, -tw), (d - tf, -b)
            });
            r.Poligonos.Add(new Poligono2D(i, true, capa, false));

            foreach (var (x, y) in i)
            {
                r.Abarcar(x, y);
            }

            return;
        }

        var t = mi.Tw;
        var fuera = Contorno(new[] { (d, -b), (d, b), (-d, b), (-d, -b) });
        var dentro = Contorno(new[] { (d - t, -b + t), (d - t, b - t), (-d + t, b - t), (-d + t, -b + t) });
        r.Poligonos.Add(new Poligono2D(fuera, true, capa, false));
        r.Poligonos.Add(new Poligono2D(dentro, true, capa, false));

        foreach (var (x, y) in fuera)
        {
            r.Abarcar(x, y);
        }
    }

    /// <summary>Dos direcciones unitarias perpendiculares al eje y entre si.</summary>
    public static (V3 U, V3 V) Perpendiculares(V3 eje)
    {
        var n = eje.Unitario();
        var aux = Math.Abs(n.Z) < 0.9 ? new V3(0, 0, 1) : new V3(1, 0, 0);
        var u = aux.Cruz(n).Unitario();
        return (u, n.Cruz(u).Unitario());
    }
}
