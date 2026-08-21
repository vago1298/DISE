// Prueba de verdad de TrazoDiamante: se EJECUTA contra el CadLink.Cad compilado, no es un
// port. Comprueba lo que un port no puede comprobar, que es que el codigo compilado hace lo
// que se cree.
using System;
using System.Collections.Generic;
using System.Linq;
using CadLink.Cad;

static class Prueba
{
    static int fallos;

    static void Check(string nombre, bool cond, string detalle = "")
    {
        Console.WriteLine($"  {(cond ? "OK  " : "FALLA")}  {nombre}"
            + (!cond && detalle.Length > 0 ? $"   [{detalle}]" : ""));

        if (!cond)
        {
            fallos++;
        }
    }

    static List<(double X, double Y, double R)> Lecho(
        double b, double y, int n, double off, double r)
    {
        var l = new List<(double X, double Y, double R)>();

        if (n == 1)
        {
            l.Add((b / 2, y, r));
            return l;
        }

        var paso = (b - (2 * off)) / (n - 1);

        for (var i = 0; i < n; i++)
        {
            l.Add((off + (i * paso), y, r));
        }

        return l;
    }

    static int Main()
    {
        // Una columna de 40x40 con 3 varillas por lecho y 1 lateral por costado, del #4, con
        // estribo del #3. Es el armado del ejemplo del programa.
        const double b = 40, h = 40, rec = 4, dEst = 0.953, dVar = 1.27;

        var off = rec + dEst + (dVar / 2);

        var sup = Lecho(b, h - off, 3, off, dVar / 2);
        var inf = Lecho(b, off, 3, off, dVar / 2);

        var lat = new List<(double X, double Y, double R)>
        {
            (off, h / 2, dVar / 2),
            (b - off, h / 2, dVar / 2)
        };

        var notas = new List<string>();

        var centros = TrazoDiamante.Centros(
            rec, rec, b - rec, h - rec, dEst, sup, inf, lat, notas);

        Check("el recorrido se arma", centros is not null);

        if (centros is null)
        {
            return 1;
        }

        Console.WriteLine($"\n    {centros.Count} circulos en el recorrido:");

        foreach (var c in centros)
        {
            Console.WriteLine($"       ({c.X,6:F3}, {c.Y,6:F3})  r = {c.R:F3}");
        }

        foreach (var n in notas)
        {
            Console.WriteLine($"    nota: {n}");
        }

        // 1. El recorrido tiene que ser ANTIHORARIO y sin cruces, o la cinta sale hecha un
        //    nudo. Se mide con el area con signo del poligono de los centros.
        var area = 0.0;

        for (var i = 0; i < centros.Count; i++)
        {
            var j = (i + 1) % centros.Count;
            area += (centros[i].X * centros[j].Y) - (centros[j].X * centros[i].Y);
        }

        Console.WriteLine($"\n    area con signo del recorrido: {area / 2:F3} cm2");

        Check("el recorrido va en sentido antihorario", area > 0, $"{area / 2:F3}");

        // 2. El diamante se abraza a las varillas CENTRALES: la de arriba, la de abajo y las
        //    dos laterales. Con tres por lecho, la central esta en el eje.
        var enElEje = centros.Count(c => Math.Abs(c.X - (b / 2)) < 0.01);

        Check("abraza la varilla central de arriba y la de abajo", enElEje == 2,
            $"{enElEje} circulos en el eje");

        var aMediaAltura = centros.Count(c => Math.Abs(c.Y - (h / 2)) < 0.01);

        Check("y las dos laterales, a media altura", aMediaAltura == 2,
            $"{aMediaAltura} a media altura");

        // 3. Las dos cintas: la interior y la exterior separadas el diametro.
        var interior = TrazoDiamante.Cinta(centros, 0);
        var exterior = TrazoDiamante.Cinta(centros, dEst);

        Check("las dos cintas se calculan",
            interior is not null && exterior is not null);

        if (interior is null || exterior is null)
        {
            return 1;
        }

        Check("las dos tienen dos vertices por circulo",
            interior.Value.Pts.Length == 4 * centros.Count
            && exterior.Value.Pts.Length == 4 * centros.Count,
            $"{interior.Value.Pts.Length / 2} y {exterior.Value.Pts.Length / 2} vertices");

        // 4. LO QUE DE VERDAD IMPORTA DE LA CINTA: es TANGENTE a cada circulo. Cada vertice
        //    esta a exactamente R del centro de su circulo, y a R + extra en la exterior.
        var peorInt = 0.0;
        var peorExt = 0.0;

        for (var i = 0; i < centros.Count; i++)
        {
            var c = centros[i];

            foreach (var k in new[] { 2 * i, (2 * i) + 1 })
            {
                var dxi = interior.Value.Pts[2 * k] - c.X;
                var dyi = interior.Value.Pts[(2 * k) + 1] - c.Y;
                peorInt = Math.Max(peorInt, Math.Abs(Math.Sqrt((dxi * dxi) + (dyi * dyi)) - c.R));

                var dxe = exterior.Value.Pts[2 * k] - c.X;
                var dye = exterior.Value.Pts[(2 * k) + 1] - c.Y;
                peorExt = Math.Max(
                    peorExt, Math.Abs(Math.Sqrt((dxe * dxe) + (dye * dye)) - (c.R + dEst)));
            }
        }

        Console.WriteLine($"\n    tangencia: interior {peorInt:E3} cm, exterior {peorExt:E3} cm");

        Check("la cinta interior es tangente a cada circulo", peorInt < 1e-9, $"{peorInt:E3}");
        Check("y la exterior, al circulo engrosado", peorExt < 1e-9, $"{peorExt:E3}");

        // 5. EL MUESTREO, que es el codigo nuevo: convierte los bulges en puntos para el
        //    lienzo de la vista previa. Un arco mal muestreado se ve al revés o cortando.
        var puntos = TrazoDiamante.Muestrear(
            interior.Value.Pts, interior.Value.Bulges, 12);

        Console.WriteLine($"    el muestreo da {puntos.Count} puntos de "
            + $"{interior.Value.Pts.Length / 2} vertices");

        Check("el muestreo da mas puntos que vertices",
            puntos.Count > interior.Value.Pts.Length / 2, $"{puntos.Count}");

        // Todos los vertices originales tienen que estar entre los puntos: el muestreo AÑADE
        // puntos en los arcos, no mueve los que ya habia.
        var faltan = 0;

        for (var v = 0; v < interior.Value.Pts.Length / 2; v++)
        {
            var vx = interior.Value.Pts[2 * v];
            var vy = interior.Value.Pts[(2 * v) + 1];

            if (!puntos.Any(p => Math.Abs(p.X - vx) < 1e-9 && Math.Abs(p.Y - vy) < 1e-9))
            {
                faltan++;
            }
        }

        Check("y conserva todos los vertices de la cinta", faltan == 0, $"faltan {faltan}");

        // LA COMPROBACION DEL ARCO: cada punto que el muestreo añade tiene que caer sobre el
        // circulo de su doblez, o sea a R del centro. Si el centro del arco se calculara mal
        // -el error clasico de esta cuenta, con el barrido de mas de media vuelta- los
        // puntos se irian del circulo y el doblez saldria volteado.
        var peorArco = 0.0;

        foreach (var p in puntos)
        {
            var d = centros.Min(c =>
                Math.Abs(Math.Sqrt(((p.X - c.X) * (p.X - c.X)) + ((p.Y - c.Y) * (p.Y - c.Y)))
                         - c.R));

            peorArco = Math.Max(peorArco, d);
        }

        Console.WriteLine($"    el punto muestreado que mas se sale de su doblez: "
            + $"{peorArco:E3} cm");

        Check("cada punto del muestreo cae sobre el arco de su doblez",
            peorArco < 1e-9, $"{peorArco:E3} cm");

        // Y el poligono muestreado tiene que seguir siendo antihorario y de area parecida a
        // la de la cinta: si un arco saliera volteado, el area se desplomaria.
        var areaM = 0.0;

        for (var i = 0; i < puntos.Count; i++)
        {
            var j = (i + 1) % puntos.Count;
            areaM += (puntos[i].X * puntos[j].Y) - (puntos[j].X * puntos[i].Y);
        }

        Console.WriteLine($"    area del muestreo: {areaM / 2:F3} cm2");

        Check("el muestreo sigue siendo antihorario", areaM > 0, $"{areaM / 2:F3}");
        Check("y encierra mas que el poligono de los centros",
            areaM / 2 > area / 2, $"{areaM / 2:F3} contra {area / 2:F3}");

        // 6. Y un caso que tiene que decir NO: sin diametro no hay diamante.
        Check("sin diametro no se arma recorrido",
            TrazoDiamante.Centros(rec, rec, b - rec, h - rec, 0, sup, inf, lat) is null);
        Check("ni con el nucleo al reves",
            TrazoDiamante.Centros(b - rec, h - rec, rec, rec, dEst, sup, inf, lat) is null);

        Console.WriteLine();

        if (fallos > 0)
        {
            Console.WriteLine($" {fallos} PROBLEMA(S).");
            return 1;
        }

        Console.WriteLine(" Todo correcto.");
        return 0;
    }
}
