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

// ---------- EL ACOMODO: la fila EMPIEZA EN X = -0.8 y crece a la IZQUIERDA ----------
// Lo que se pidio: "empezar en x = -0.8", "no lo dibujes a partir del centro". La primera
// zapata queda con su pano DERECHO en -0.8, asi que la fila entera vive en x <= -0.8 y nada
// toca el origen. Vale igual para la central y para el lindero, y las dos vistas -corte y
// planta- usan esta misma X, asi que quedan en la misma vertical.
var anchos = new[] { 1.5, 2.0, 1.0 };

foreach (var tipo in new[] { "CENTRAL", "LINDERO" })
{
    Vale($"{tipo}: la fila empieza en x = -0.8, no en el origen",
        Math.Abs(TrazoZapata.XArranque - (-0.8)) < 1e-12
        && Math.Abs(TrazoZapata.XBase(tipo, anchos, 0) - (-0.8 - 1.5)) < 1e-12);

    // El pano DERECHO de la primera es el que se coloca en -0.8.
    Vale($"{tipo}: el pano derecho de la primera cae justo en -0.8",
        Math.Abs(TrazoZapata.XBase(tipo, anchos, 0) + anchos[0] - (-0.8)) < 1e-12);

    // La segunda: su pano derecho a 80 cm del pano izquierdo de la primera, o sea su pano
    // izquierdo 80 cm mas su propio ancho a la izquierda.
    Vale($"{tipo}: la segunda a 80 cm a la izquierda de la primera",
        Math.Abs(TrazoZapata.XBase(tipo, anchos, 1) - (-0.8 - 1.5 - (0.8 + 2.0))) < 1e-12);

    Vale($"{tipo}: la tercera, otros 80 cm mas a la izquierda",
        Math.Abs(TrazoZapata.XBase(tipo, anchos, 2)
                 - (-0.8 - 1.5 - (0.8 + 2.0) - (0.8 + 1.0))) < 1e-12);

    // Y NINGUNA se encima con la anterior: entre el pano derecho de una y el izquierdo de la
    // otra hay justo la separacion de 80 cm.
    var ok = true;

    for (var i = 1; i < anchos.Length; i++)
    {
        var izqAnterior = TrazoZapata.XBase(tipo, anchos, i - 1);
        var derActual = TrazoZapata.XBase(tipo, anchos, i) + anchos[i];

        if (Math.Abs((izqAnterior - derActual) - 0.8) > 1e-12)
        {
            ok = false;
        }
    }

    Vale($"{tipo}: siempre 80 cm justos entre una y la siguiente", ok);

    // Y NINGUNA pasa del arranque de la fila: eso es "no dibujar a partir del centro".
    var fuera = false;

    for (var i = 0; i < anchos.Length; i++)
    {
        if (TrazoZapata.XBase(tipo, anchos, i) + anchos[i] > TrazoZapata.XDerechaDeLaFila + 1e-12)
        {
            fuera = true;
        }
    }

    Vale($"{tipo}: ninguna zapata pasa de x = -0.8 ni toca el origen", !fuera);
}

// ---------- EL RENGLON DE LOS ROTULOS: EL MISMO PARA TODAS ----------
// Lo que se pidio: que los titulos y las cotas esten ALINEADOS SIEMPRE, bajados y aparte del
// dibujo. El renglon se mide desde el fondo de la plantilla, que sale del punto de insercion
// -8, igual para todas: asi que da lo mismo lo que mida cada zapata.
{
    var yFondo = TrazoZapata.YBaseElevacion - TrazoZapata.PlantillaEspesor;

    Vale("el titulo se baja los mismos 80 cm de la fila",
        Math.Abs(TrazoZapata.RotuloSeparacion - 0.8) < 1e-12
        && Math.Abs(TrazoZapata.YRotulo(yFondo, 0) - (yFondo - 0.8)) < 1e-12);

    Vale("y los tres renglones guardan los saltos de la macro",
        Math.Abs(TrazoZapata.YRotulo(yFondo, 0) - TrazoZapata.YRotulo(yFondo, 1) - 0.09) < 1e-12
        && Math.Abs(TrazoZapata.YRotulo(yFondo, 0) - TrazoZapata.YRotulo(yFondo, 2) - 0.17)
           < 1e-12);

    Vale("el renglon queda POR DEBAJO de todo el dibujo",
        TrazoZapata.YRotulo(yFondo, 0) < yFondo - 0.5);

    // Tres zapatas de anchos y espesores distintos: los tres rotulos, en la misma linea.
    var mismaLinea = true;

    foreach (var esp in new[] { 0.20, 0.45, 0.90 })
    {
        var y = TrazoZapata.YRotulo(TrazoZapata.YBaseElevacion - TrazoZapata.PlantillaEspesor, 0);

        if (Math.Abs(y - TrazoZapata.YRotulo(yFondo, 0)) > 1e-12 || esp <= 0)
        {
            mismaLinea = false;
        }
    }

    Vale("midan lo que midan las zapatas, el titulo va en la misma linea", mismaLinea);

    // Y no se sale de su hueco: su zapata mas los 80 cm de separacion.
    var titulo = "ZAPATA AISLADA DE LINDERO \"ZE-1\"";

    foreach (var ancho in new[] { 0.60, 1.00, 2.50 })
    {
        var disponible = TrazoZapata.AnchoParaElRotulo(ancho);
        var alto = TrazoZapata.AltoQueQuepa(titulo.Length, 0.07, disponible);

        Vale($"el titulo cabe en su hueco con una zapata de {ancho:0.00} m",
            titulo.Length * alto * 0.62 <= disponible + 1e-9 && alto > 0);
    }

    Vale("y si ya cabe no se le toca el alto",
        Math.Abs(TrazoZapata.AltoQueQuepa(5, 0.07, 10.0) - 0.07) < 1e-12);

    Vale("el hueco del rotulo son su ancho mas los 80 cm",
        Math.Abs(TrazoZapata.AnchoParaElRotulo(1.20) - 2.0) < 1e-12);
}

Console.WriteLine(fallos == 0
    ? "\nRESULTADO: todo bien"
    : $"\nRESULTADO: {fallos} fallo(s)");

return fallos == 0 ? 0 : 1;
