// Prueba ejecutable de los lectores de celda de la zapata. Ver Prueba.csproj.
//
// Cada comprobacion dice QUE se espera y por que, para que el dia que una falle se sepa
// si lo que hay que arreglar es el codigo o la expectativa.

using CadLink.Cad;

int fallos = 0;

void Vale(string que, bool ok)
{
    Console.WriteLine((ok ? "  OK    " : "  FALLA ") + que);
    if (!ok) fallos++;
}

// ---------- SeparacionM ----------
Vale("20 -> 0.20 m", Math.Abs(TrazoZapata.SeparacionM("20") - 0.20) < 1e-12);
Vale("«20 cm» -> 0.20 m", Math.Abs(TrazoZapata.SeparacionM("20 cm") - 0.20) < 1e-12);
Vale("«@15» -> 0.15 m", Math.Abs(TrazoZapata.SeparacionM("@15") - 0.15) < 1e-12);
Vale("«12,5» -> 0.125 m", Math.Abs(TrazoZapata.SeparacionM("12,5") - 0.125) < 1e-12);
Vale("vacio -> 0.12 m", Math.Abs(TrazoZapata.SeparacionM("") - 0.12) < 1e-12);
Vale("null -> 0.12 m", Math.Abs(TrazoZapata.SeparacionM(null) - 0.12) < 1e-12);
Vale("«0» -> 0.12 m (no cero)", Math.Abs(TrazoZapata.SeparacionM("0") - 0.12) < 1e-12);
Vale("«hola» -> 0.12 m", Math.Abs(TrazoZapata.SeparacionM("hola") - 0.12) < 1e-12);

// ---------- TramosCm ----------
var t = TrazoZapata.TramosCm("9-18-9");
Vale("9-18-9 -> 9,18,9", t[0] == 9 && t[1] == 18 && t[2] == 9);
t = TrazoZapata.TramosCm("15");
Vale("15 -> 15,0,0 (separacion unica)", t[0] == 15 && t[1] == 0 && t[2] == 0);
t = TrazoZapata.TramosCm("10-20");
Vale("10-20 -> 10,20,0", t[0] == 10 && t[1] == 20 && t[2] == 0);
t = TrazoZapata.TramosCm("");
Vale("vacio -> 15,0,0 (por omision)", t[0] == 15 && t[1] == 0 && t[2] == 0);
t = TrazoZapata.TramosCm(" 6 - 12 - 6 ");
Vale("con espacios -> 6,12,6", t[0] == 6 && t[1] == 12 && t[2] == 6);
t = TrazoZapata.TramosCm("6-12-6-8");
Vale("cuatro tramos: se toman los tres primeros", t[0] == 6 && t[1] == 12 && t[2] == 6);

// ---------- Las cinco de la lista corta reparten estribos de verdad ----------
foreach (var sep in new[] { "6-12-6", "7-14-7", "8-16-8", "9-18-9", "10-20-10", "15", "20" })
{
    var tr = TrazoZapata.TramosCm(sep);
    var c = TrazoZapata.CentrosEstribos(
        1.20, tr[0], tr[1], tr[2],
        TrazoZapata.EstriboRetiroBorde, TrazoZapata.EstriboRetiroBorde);

    var ordenados = true;
    for (var i = 1; i < c.Length; i++)
    {
        if (c[i] <= c[i - 1]) ordenados = false;
    }

    Vale($"«{sep}» en un dado de 1.20 m: {c.Length} estribos, en orden y dentro",
        c.Length > 2 && ordenados && c[0] >= 0 && c[^1] <= 1.20 + 1e-9);
}

// ---------- El acomodo de las dos macros sigue igual ----------
var anchos = new[] { 1.5, 2.0 };
Vale("central: la primera en x=0", Math.Abs(TrazoZapata.XBase("CENTRAL", anchos, 0)) < 1e-12);
Vale("central: la segunda a 1 m de la primera",
    Math.Abs(TrazoZapata.XBase("CENTRAL", anchos, 1) - (1.5 + 1.0)) < 1e-12);
Vale("lindero: la primera en x=-3",
    Math.Abs(TrazoZapata.XBase("LINDERO", anchos, 0) + 3.0) < 1e-12);

Console.WriteLine(fallos == 0
    ? "\nRESULTADO: todo bien"
    : $"\nRESULTADO: {fallos} fallo(s)");

return fallos == 0 ? 0 : 1;
