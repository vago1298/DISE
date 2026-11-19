using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>Prueba de <see cref="GeometriaArmadoLosa"/>: el armado de un tablero de losa.</summary>
internal static class Program
{
    private static int _fallos;

    private static void Comprobar(bool cond, string que, string porque = "")
    {
        Console.WriteLine(cond ? $"  OK    {que}" : $"  FALLA {que}" + (porque.Length > 0 ? $"\n        {porque}" : string.Empty));

        if (!cond)
        {
            _fallos++;
        }
    }

    private static void Cerca(double esperado, double real, string que, double tol = 1e-6) =>
        Comprobar(Math.Abs(esperado - real) <= tol, que, $"esperado {esperado}, salio {real}");

    private static ArmadoLosaCad Losa(TipoLosa tipo = TipoLosa.Entrepiso, BordeLosa izq = BordeLosa.Borde, BordeLosa der = BordeLosa.Continuo) => new()
    {
        Id = "LS-1", Tipo = tipo, ClaroXM = 4.20, ClaroYM = 5.20, EspesorCm = 12, RecubrimientoCm = 2,
        ApoyoAnchoCm = 20, ApoyoPeralteCm = 40, VarX = new VarCad("#3", 0.9525), SepXCm = 20,
        VarY = new VarCad("#3", 0.9525), SepYCm = 20, Izquierda = izq, Derecha = der, EscalaPlano = 50
    };

    private static int Main()
    {
        Console.WriteLine("[1] Bastones y bayonetas a L/4 del claro libre");

        var l = Losa();
        Cerca(4.00, GeometriaArmadoLosa.ClaroLibre(4.20, 20), "el claro libre es de paño a paño: 4.20 - 0.20");
        Cerca(1.00, GeometriaArmadoLosa.CuartoDelClaro(4.20, 20), "L/4 = 1.00 m");

        var f = GeometriaArmadoLosa.Franja(4.20, l, l.VarX, BordeLosa.Borde, BordeLosa.Continuo);
        var corrida = f.Single(v => v.Que == "corridas");
        var bay = f.Single(v => v.Que == "bayonetas");
        var bIzq = f.Single(v => v.Que == "baston izquierdo");
        var bDer = f.Single(v => v.Que == "baston derecho");
        var h = 0.12;
        var zInf = 0.02 + (0.009525 / 2);
        var zSup = h - zInf;

        Comprobar(corrida.Eje.Where(p => Math.Abs(p.Z - zInf) < 1e-9).Count() == 2, "la corrida va abajo en el entrepiso");
        Comprobar(Math.Abs(corrida.Eje[0].S - (-0.10 + 0.02)) < 1e-9 && corrida.Eje[0].Z > zInf,
            "en el borde la corrida llega al paño exterior menos el recubrimiento, con gancho hacia arriba");
        Cerca(4.20, corrida.Eje[^1].S, "en el apoyo continuo llega al eje");

        Cerca(0.10 + 1.00, bay.Eje.First(p => Math.Abs(p.Z - zInf) < 1e-9).S, "la bayoneta baja y llega abajo a L/4 del paño");
        Cerca(4.10 - 1.00, bay.Eje.Last(p => Math.Abs(p.Z - zInf) < 1e-9).S, "y vuelve a subir a L/4 del otro paño");
        Comprobar(Math.Abs(bay.Eje[1].Z - zSup) < 1e-9, "sobre el apoyo la bayoneta va arriba");
        Cerca(4.20 + 0.10 + 1.00, bay.Eje[^1].S, "y sobre el continuo entra L/4 al tablero vecino");

        var arribaIzq = bIzq.Eje.Where(p => p.Z > h / 2).ToList();
        Cerca(0.10 + 1.00, arribaIzq.Max(p => p.S), "el baston entra L/4 desde el paño");
        Comprobar(bIzq.Eje.Count == 3 && bIzq.Eje[0].Z < bIzq.Eje[1].Z, "en el borde el baston lleva gancho hacia abajo");
        Cerca(4.10 - 1.00, bDer.Eje.Min(p => p.S), "el del otro apoyo tambien a L/4 del paño");
        Cerca(4.20 + 0.10 + 1.00, bDer.Eje.Max(p => p.S), "y en el continuo pasa L/4 al vecino");
        Comprobar(bDer.Eje.Count == 2, "sin gancho en el continuo");

        Console.WriteLine("[2] Claro distinto, bastones distintos");
        var largo = GeometriaArmadoLosa.Franja(6.20, l, l.VarX, BordeLosa.Borde, BordeLosa.Borde);
        Cerca(0.10 + 1.50, largo.Single(v => v.Que == "baston izquierdo").Eje.Max(p => p.S), "con 6.20 a ejes el baston es de 1.50");

        Console.WriteLine("[3] Losa de cimentacion: todo al reves");
        var c = Losa(TipoLosa.Cimentacion);
        var fc = GeometriaArmadoLosa.Franja(4.20, c, c.VarX, BordeLosa.Borde, BordeLosa.Continuo);
        Comprobar(fc.Single(v => v.Que == "corridas").Eje.Count(p => Math.Abs(p.Z - zSup) < 1e-9) == 2, "las corridas van ARRIBA");
        var bc = fc.Single(v => v.Que == "bayonetas");
        Comprobar(Math.Abs(bc.Eje[1].Z - zInf) < 1e-9 && bc.Eje.Any(p => Math.Abs(p.Z - zSup) < 1e-9),
            "la bayoneta va abajo en la contratrabe y arriba al centro");
        Comprobar(fc.Single(v => v.Que == "baston izquierdo").Eje.Skip(1).All(p => p.Z < h / 2), "los bastones van abajo (con su gancho hacia arriba en el borde)");

        Console.WriteLine("[4] El dibujo");
        var d = GeometriaArmadoLosa.Dibujo(l);
        Comprobar(d.Avisos.Count == 0 && d.Trazos.Count > 10 && d.Cotas.Count >= 10, "sale planta y dos cortes con sus cotas");
        Comprobar(d.Cotas.Count(k => k.Texto.StartsWith("L/4 = 1.00")) == 2 && d.Cotas.Any(k => k.Texto.StartsWith("L/4 = 1.25")),
            "las cotas dicen el L/4 de cada claro (1.00 en X, 1.25 en Y)");
        Comprobar(d.Textos.Any(t => t.Texto.Contains("@ 40 cm corridas")), "corridas y bayonetas alternadas: @ 2s");
        Comprobar(d.Puntos.Count > 0, "las del otro sentido se ven de punta");

        var mal = new ArmadoLosaCad { Id = "X", ClaroXM = 0.1, ClaroYM = 4, EspesorCm = 3, RecubrimientoCm = 2, ApoyoAnchoCm = 20, VarX = new VarCad("#3", 0.95), VarY = new VarCad("#3", 0.95), SepXCm = 20, SepYCm = 20 };
        Comprobar(GeometriaArmadoLosa.Dibujo(mal).Avisos.Count >= 2, "lo que no cabe se dice y no se dibuja");

        Console.WriteLine(_fallos == 0 ? "\nTODAS LAS COMPROBACIONES PASARON" : $"\n{_fallos} FALLA(S)");
        return _fallos == 0 ? 0 : 1;
    }
}
