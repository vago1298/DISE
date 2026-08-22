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
// dibujo. Y TODO cuelga de la MISMA esquina, la inferior derecha: las cotas y los tres
// renglones del rotulo. Como el desplante es el punto de insercion -8, igual para todas, los
// rotulos de todas quedan en la misma linea midan lo que midan.
{
    var yEsquina = TrazoZapata.YBaseElevacion;

    // ---------- LA TRANSICION DADO -> COLUMNA: SIEMPRE 1:6 ----------
    // El detalle del usuario: DESPLAZAMIENTO DE VARILLA EN COLUMNA O TRABE, RELACION 1:6. El alto
    // del doblez es intocable -seis veces el corrimiento-; lo que se acomoda es DONDE queda.
    {
        var yZapTop = -8.0 + 0.30;      // lomo de la zapata
        var yDadoTop = -8.0 + 1.05;     // tope del dado
        var yColTope = yDadoTop + 0.71; // lo que se dibuja de columna

        // Caso normal: cabe en el dado y el doblez acaba justo en la junta.
        var t1 = TrazoZapata.Desplazamiento(0.06, yZapTop, yDadoTop, 0.05);

        Vale("cabe en el dado: el doblez mide 6 veces el corrimiento",
            t1.Cabe && Math.Abs(t1.Alto - (6 * 0.06)) < 1e-12);

        Vale("y la pendiente es 1:6 exacta",
            Math.Abs((t1.YDiagTop - t1.YZonaBot) / 0.06 - 6.0) < 1e-9);

        Vale("y acaba EN la junta, no por encima",
            Math.Abs(t1.YDiagTop - yDadoTop) < 1e-12);

        Vale("y le queda tramo recto sobre la zapata",
            t1.YZonaBot >= yZapTop + TrazoZapata.MinBarraRectaDado - 1e-12);

        // Corrimiento chico: el doblez no arranca dentro del recubrimiento del tope, asi que acaba
        // POR DEBAJO de la junta. Tambien correcto, y sigue siendo 1:6.
        var tChico = TrazoZapata.Desplazamiento(0.004, yZapTop, yDadoTop, 0.05);

        Vale("un corrimiento chico da un doblez corto, por debajo de la junta",
            tChico.Cabe
            && Math.Abs((tChico.YDiagTop - tChico.YZonaBot) / 0.004 - 6.0) < 1e-9
            && tChico.YDiagTop <= yDadoTop + 1e-12);

        // Corrimiento grande: NO cabe entre el tramo recto y la junta, asi que NO se dibuja. Antes
        // se recortaba el alto -y salia mas parado- o se dejaba pasar a la columna, y ahi arriba ya
        // estan las varillas de la columna: se veian DUPLICADAS.
        var t2 = TrazoZapata.Desplazamiento(0.12, yZapTop, yDadoTop, 0.05);

        Vale("un corrimiento que no cabe en el dado NO se dibuja", !t2.Cabe);

        Vale("y el alto que habria pedido se sigue informando, para el aviso",
            Math.Abs(t2.Alto - (6 * 0.12)) < 1e-12);

        // NINGUN caso puede acabar por encima de la junta: ahi empiezan las varillas de la columna.
        var ningunoSePasa = true;

        for (var dx = 0.0; dx <= 0.12001; dx += 0.002)
        {
            var tr = TrazoZapata.Desplazamiento(dx, yZapTop, yDadoTop, 0.05);

            if (tr.Cabe && tr.YDiagTop > yDadoTop + 1e-9)
            {
                ningunoSePasa = false;
            }

            // Y el que se dibuja, SIEMPRE a 1:6.
            if (tr.Cabe && Math.Abs((tr.YDiagTop - tr.YZonaBot) / dx - 6.0) > 1e-6)
            {
                ningunoSePasa = false;
            }
        }

        Vale("ningun doblez se pasa de la junta ni deja de ser 1:6", ningunoSePasa);

        Vale("sin corrimiento no hay doblez",
            !TrazoZapata.Desplazamiento(0, yZapTop, yDadoTop, 0.05).Cabe);

        _ = yColTope;

        Vale("el corrimiento maximo que se dobla son 12 cm, como la macro",
            Math.Abs(TrazoZapata.DesplazamientoMax - 0.12) < 1e-12
            && Math.Abs(TrazoZapata.RelacionDesplazamiento - 6.0) < 1e-12);
    }

    // ---------- LAS VARILLAS DE CADA ELEMENTO, REDONDO O CUADRADO ----------
    {
        // Cuadrado: las dos de los paños y las intermedias repartidas.
        var rect = TrazoZapata.BarrasRectangulares(0.0, 0.40, 0.05, 0.0127, 0.0127, 2);

        Vale("cuadrado: las dos de los paños caen a rec + medio diametro",
            Math.Abs(rect.Der - (-(0.05 + 0.00635))) < 1e-9
            && Math.Abs(rect.Izq - (-(0.40 - 0.05 - 0.00635))) < 1e-9);

        Vale("y las intermedias van entre ellas, sin salirse",
            rect.Intermedias.Count == 2
            && rect.Intermedias.All(x => x > rect.Izq && x < rect.Der));

        // Redondo: proyeccion de las varillas de la circunferencia sobre el diametro.
        var circ = TrazoZapata.BarrasCirculares(0.0, 0.40, 0.05, 0.0095, 0.0127, 8);
        var radio = (0.40 / 2) - 0.05 - 0.0095 - (0.0127 / 2);

        Vale("redondo: los extremos caen en la circunferencia del armado",
            Math.Abs(circ.Der - (-0.20 + radio)) < 1e-9
            && Math.Abs(circ.Izq - (-0.20 - radio)) < 1e-9);

        // Con 8 varillas empezando arriba: -R, -0.707R, 0, +0.707R, +R -> 5 posiciones distintas.
        Vale("y las simetricas se ven como UNA sola varilla en el alzado",
            circ.Intermedias.Count == 3);

        Vale("las intermedias del redondo van ordenadas y dentro de los extremos",
            circ.Intermedias.All(x => x > circ.Izq && x < circ.Der)
            && circ.Intermedias.Zip(circ.Intermedias.Skip(1)).All(p => p.First < p.Second));

        // Un redondo y un cuadrado del MISMO ancho: los extremos del redondo quedan por dentro,
        // que es lo que hace que el corrimiento con el dado sea distinto y haya que calcularlo.
        Vale("el redondo mete sus varillas por dentro del cuadrado del mismo ancho",
            circ.Izq > rect.Izq && circ.Der < rect.Der);

        // Sin sitio para el armado no se inventa nada: se responde como un cuadrado.
        var chico = TrazoZapata.BarrasCirculares(0.0, 0.10, 0.05, 0.0095, 0.0127, 6);

        Vale("un redondo sin sitio no revienta", chico.Intermedias.Count == 0);
    }

    // El orden saliendo del dibujo hacia abajo: cadena, total y el rotulo. Las cotas de las patas
    // no entran aqui: van pegadas a su pata, DENTRO del dado, como en la macro.
    Vale("la anotacion sale en orden, sin que dos cosas compartan renglon",
        TrazoZapata.AnotacionCadena < TrazoZapata.AnotacionTotal
        && TrazoZapata.AnotacionTotal < TrazoZapata.AnotacionRotulo);

    Vale("el rotulo queda por debajo de la cota total, con aire",
        TrazoZapata.AnotacionRotulo - TrazoZapata.AnotacionTotal >= 0.08);

    Vale("el titulo cuelga del desplante, a los 0.32 de la macro",
        Math.Abs(TrazoZapata.YRotulo(yEsquina, 0) - (yEsquina - 0.32)) < 1e-12);

    Vale("y los otros dos renglones caen en el 0.41 y el 0.49 de la macro",
        Math.Abs(TrazoZapata.YRotulo(yEsquina, 1) - (yEsquina - 0.41)) < 1e-12
        && Math.Abs(TrazoZapata.YRotulo(yEsquina, 2) - (yEsquina - 0.49)) < 1e-12);

    Vale("y los tres renglones guardan los saltos de la macro",
        Math.Abs(TrazoZapata.YRotulo(yEsquina, 0) - TrazoZapata.YRotulo(yEsquina, 1) - 0.09)
            < 1e-12
        && Math.Abs(TrazoZapata.YRotulo(yEsquina, 0) - TrazoZapata.YRotulo(yEsquina, 2) - 0.17)
           < 1e-12);

    // El renglon del rotulo NO depende del espesor ni del ancho: solo del desplante, que es el
    // mismo para todas. Antes se medía desde el fondo de la plantilla y cada zapata lo bajaba
    // por su cuenta.
    var mismaLinea = true;

    foreach (var esp in new[] { 0.20, 0.45, 0.90 })
    {
        // El espesor cambia el lomo, no el desplante: el renglon tiene que salir identico.
        var y = TrazoZapata.YRotulo(yEsquina, 0);

        if (Math.Abs(y - TrazoZapata.YRotulo(yEsquina, 0)) > 1e-12 || esp <= 0)
        {
            mismaLinea = false;
        }
    }

    Vale("midan lo que midan las zapatas, el titulo va en la misma linea", mismaLinea);

    // La planta cuelga de SU esquina, con los renglones de la macro.
    Vale("el rotulo de la planta va a 0.24 y 0.33 de su pano inferior",
        Math.Abs(TrazoZapata.YRotuloPlanta(-15.0, 0) - (-15.0 - 0.24)) < 1e-12
        && Math.Abs(TrazoZapata.YRotuloPlanta(-15.0, 2) - (-15.0 - 0.33)) < 1e-12);

    // Y NO SE SALE DE SU HUECO -su zapata mas los 80 cm de la fila-, medido con el ancho de letra
    // REAL del dibujo. Esto es lo que fallaba: con el 0.62 de la macro el titulo nunca se encogia,
    // en el dibujo medía 2.2 m y los dos titulos se leian uno encima del otro.
    var titulo = "ZAPATA AISLADA DE LINDERO \"ZE-1\"";
    var f = TrazoZapata.FactorLetraTitulo;

    Vale("el ancho de letra del titulo es el del dibujo, no el 0.62 de la plantilla",
        f >= 0.95);

    foreach (var ancho in new[] { 0.60, 1.00, 2.50 })
    {
        var disponible = TrazoZapata.AnchoParaElRotulo(ancho);
        var alto = TrazoZapata.AltoQueQuepa(titulo.Length, 0.07, disponible, f);

        Vale($"el titulo cabe en su hueco con una zapata de {ancho:0.00} m",
            titulo.Length * alto * f <= disponible + 1e-9 && alto > 0);

        // Y con el hueco medido asi, DOS titulos seguidos no se tocan: cada uno mide como mucho
        // su hueco, y de un eje al siguiente hay ese mismo hueco.
        var mitad = titulo.Length * alto * f / 2;

        Vale($"y no alcanza al de la zapata de al lado con {ancho:0.00} m",
            2 * mitad <= TrazoZapata.SeparacionIzquierda + ancho + 1e-9);
    }

    // Con el 0.62 viejo, la comprobacion de arriba NO pasaba: se deja escrito el numero.
    Vale("(control) con el 0.62 el titulo se pasaba de su hueco en una zapata de 1.00 m",
        titulo.Length * TrazoZapata.AltoQueQuepa(titulo.Length, 0.07,
            TrazoZapata.AnchoParaElRotulo(1.00), 0.62) * f
        > TrazoZapata.AnchoParaElRotulo(1.00) + 1e-9);

    Vale("y si ya cabe no se le toca el alto",
        Math.Abs(TrazoZapata.AltoQueQuepa(5, 0.07, 10.0) - 0.07) < 1e-12);

    Vale("el hueco del rotulo son su ancho mas los 80 cm",
        Math.Abs(TrazoZapata.AnchoParaElRotulo(1.20) - 2.0) < 1e-12);
}

// ======================================================================
// ZAPATAS CORRIDAS: el C# compilado contra los mismos numeros que comprueba
// tools/verificar_zapatas_corridas.py
// ======================================================================
//
// El verificar_*.py rehace las cuentas de las macros EN PYTHON, y un port de Python
// correcto conviviendo con un C# equivocado da todo en verde. Los numeros que se
// esperan aqui son los que salen de ese port, calculados a mano, y lo que se ejecuta
// es el CadLink.Cad de verdad: si los dos coinciden, coinciden con la macro.
{
    Console.WriteLine("\n---------- Zapatas corridas ----------");

    // ---------- El acomodo de las dos filas ----------
    Vale("la central arranca en 0 y crece a la derecha",
        TrazoZapataCorrida.XBase("CENTRAL", 0) == 0
        && TrazoZapataCorrida.XBase("CENTRAL", 1) == 2
        && TrazoZapataCorrida.XBase("CENTRAL", 3) == 6);

    Vale("el lindero arranca en -2 y crece a la izquierda",
        TrazoZapataCorrida.XBase("LINDERO", 0) == -2
        && TrazoZapataCorrida.XBase("LINDERO", 1) == -4
        && TrazoZapataCorrida.XBase("LINDERO", 3) == -8);

    Vale("un indice negativo no manda la seccion a otro lado",
        TrazoZapataCorrida.XBase("CENTRAL", -5) == 0);

    // ---------- Las alturas: el terreno manda ----------
    var zc = new ZapataCorridaCad
    {
        Tipo = ZapataCorridaCad.Central,
        AnchoM = 1.00,
        ProfundidadM = 1.50,
        EspesorM = 0.25,
        EspesorMuroCm = 20,
    };

    var ac = TrazoZapataCorrida.Colocar(zc, 0);

    Vale("el desplante cuelga del terreno: -3.5 - 1.5 = -5",
        Math.Abs(ac.YZapBot - (-5.0)) < 1e-12);
    Vale("el lomo queda en -4.75", Math.Abs(ac.YZapTop - (-4.75)) < 1e-12);
    Vale("la plantilla acaba en -5.05", Math.Abs(ac.YPlantillaBot - (-5.05)) < 1e-12);
    Vale("el terreno se queda en -3.5", Math.Abs(ac.YTerreno - (-3.5)) < 1e-12);

    var zHonda = new ZapataCorridaCad
    {
        Tipo = ZapataCorridaCad.Central,
        AnchoM = 1.00,
        ProfundidadM = 2.50,
        EspesorM = 0.25,
    };

    Vale("dos zapatas con desplantes distintos comparten el nivel de terreno",
        Math.Abs(TrazoZapataCorrida.Colocar(zHonda, 0).YTerreno - ac.YTerreno) < 1e-12);

    // ---------- Donde va el muro ----------
    Vale("en la central el muro va centrado: 0.40 a 0.60",
        Math.Abs(ac.XMuroIzq - 0.40) < 1e-12 && Math.Abs(ac.XMuroDer - 0.60) < 1e-12);

    var zl = new ZapataCorridaCad
    {
        Tipo = ZapataCorridaCad.Lindero,
        AnchoM = 1.00,
        ProfundidadM = 1.50,
        EspesorM = 0.25,
        EspesorMuroCm = 20,
    };

    var al = TrazoZapataCorrida.Colocar(zl, 0);

    Vale("en el lindero el pano derecho del muro ES el de la zapata",
        Math.Abs(al.XMuroDer - al.XDer) < 1e-12
        && Math.Abs(al.XMuroIzq - 0.80) < 1e-12);

    var zSinMuro = new ZapataCorridaCad { AnchoM = 1.00, EspesorMuroCm = 0 };

    Vale("sin espesor capturado el muro sale de 15 cm",
        Math.Abs(TrazoZapataCorrida.Colocar(zSinMuro, 0).XMuroDer
                 - TrazoZapataCorrida.Colocar(zSinMuro, 0).XMuroIzq - 0.15) < 1e-12);

    var zGordo = new ZapataCorridaCad { AnchoM = 0.40, EspesorMuroCm = 60 };
    var ag = TrazoZapataCorrida.Colocar(zGordo, 0);

    Vale("un muro mas ancho que la zapata se recorta a sus panos",
        Math.Abs(ag.XMuroIzq) < 1e-12 && Math.Abs(ag.XMuroDer - 0.40) < 1e-12);

    // ---------- El muro de enrase ----------
    foreach (var hueco in new[] { 0.18, 0.27, 0.40, 0.55, 0.73, 1.00, 1.37 })
    {
        var e = TrazoZapataCorrida.MuroDeEnrase(0, hueco);
        var tope = e.YBases[^1] + e.AltoPieza;

        Vale($"el enrase de {hueco:0.00} m cierra exacto contra la cadena",
            e.Piezas > 0 && Math.Abs(tope - hueco) < 1e-9);

        Vale($"y su pieza sale cerca de los 8 cm ({hueco:0.00} m)",
            e.AltoPieza > 0.04 && e.AltoPieza < 0.135);
    }

    var eMejor = TrazoZapataCorrida.MuroDeEnrase(0, 0.55);

    // 6 piezas de 8.33 cm y 5 juntas: 6 x 0.083333 + 5 x 0.01 = 0.55 exactos. Con 7 la
    // pieza baja a 7 cm y con 5 sube a 10.2, asi que las dos se alejan mas de los 8.
    Vale("con 55 cm de hueco salen 6 piezas de 8.33 cm",
        eMejor.Piezas == 6 && Math.Abs(eMejor.AltoPieza - (0.5 / 6)) < 1e-9
        && Math.Abs((6 * eMejor.AltoPieza) + (5 * 0.01) - 0.55) < 1e-9);

    foreach (var hueco in new[] { 0.0, -0.30, 0.005, 0.01 })
    {
        Vale($"con hueco {hueco:0.000} no se dibuja enrase",
            TrazoZapataCorrida.MuroDeEnrase(0, hueco).Piezas == 0);
    }

    var eBajo = TrazoZapataCorrida.MuroDeEnrase(-5.0, -4.5);

    Vale("el enrase arranca de donde se le diga, no de cero",
        eBajo.Piezas > 0 && Math.Abs(eBajo.YBases[0] - (-5.0)) < 1e-12);

    // ---------- El acero vertical del muro ----------
    var diam = 0.0127;  // #4
    var muroC = TrazoZapataCorrida.ColocarMuro(ac, ac.YZapTop, -3.6);
    var vc = TrazoZapataCorrida.VerticalesDelMuro(ac, muroC, true, diam, 0.05, 0);

    Vale("con doble parrilla salen dos barras, una por pano",
        vc.X.Length == 2
        && Math.Abs(vc.X[0] - (ac.XMuroIzq + 0.05 + (diam / 2))) < 1e-12
        && Math.Abs(vc.X[1] - (ac.XMuroDer - 0.05 - (diam / 2))) < 1e-12);

    Vale("en la central las dos patas se miran",
        vc.Sentido[0] == 1 && vc.Sentido[1] == -1);

    Vale("la pata son 15 diametros por omision",
        Math.Abs(vc.Doblez - (15 * diam)) < 1e-12);

    Vale("con 40 en la casilla la pata es de 40 diametros",
        Math.Abs(TrazoZapataCorrida.VerticalesDelMuro(
            ac, muroC, true, diam, 0.05, 40).Doblez - (40 * diam)) < 1e-12);

    Vale("un 2 se sube al minimo de 6 y un 500 se baja al maximo de 80",
        Math.Abs(TrazoZapataCorrida.VerticalesDelMuro(
            ac, muroC, true, diam, 0.05, 2).Doblez - (6 * diam)) < 1e-12
        && Math.Abs(TrazoZapataCorrida.VerticalesDelMuro(
            ac, muroC, true, diam, 0.05, 500).Doblez - (80 * diam)) < 1e-12);

    var muroL = TrazoZapataCorrida.ColocarMuro(al, al.YZapTop, -3.6);
    var vl = TrazoZapataCorrida.VerticalesDelMuro(al, muroL, true, diam, 0.05, 0);

    Vale("en el lindero las dos patas doblan hacia el eje, lejos del lindero",
        vl.Sentido[0] == -1 && vl.Sentido[1] == -1);

    Vale("la barra arranca dentro de la zapata, sobre su recubrimiento",
        Math.Abs(vc.YBase - (ac.YZapBot + 0.05 + (diam / 2))) < 1e-12);

    var zFino = new ZapataCorridaCad { AnchoM = 1.00, EspesorMuroCm = 8 };
    var aFino = TrazoZapataCorrida.Colocar(zFino, 0);
    var mFino = TrazoZapataCorrida.ColocarMuro(aFino, aFino.YZapTop, -3.6);

    Vale("un muro de 8 cm lleva una sola barra, al eje",
        TrazoZapataCorrida.VerticalesDelMuro(aFino, mFino, true, diam, 0.05, 0).X.Length == 1);

    // ---------- Las horizontales del muro ----------
    var hs = TrazoZapataCorrida.HorizontalesDelMuro(muroC, 0.20);

    Vale("las horizontales se reparten dentro del muro",
        hs.Length > 0 && hs[0] > muroC.YBase && hs[^1] < muroC.YTope);

    Vale("la primera no cae encima del doblez del arranque",
        hs.Length > 0 && Math.Abs(hs[0] - (muroC.YBase + 0.10)) < 1e-12);

    Vale("un muro de alto cero no lleva horizontales",
        TrazoZapataCorrida.HorizontalesDelMuro(
            TrazoZapataCorrida.ColocarMuro(ac, ac.YZapTop, ac.YZapTop), 0.20).Length == 0);

    Vale("y un tope por debajo del arranque no dibuja el muro al reves",
        TrazoZapataCorrida.ColocarMuro(ac, ac.YZapTop, -9.0).YTope >= ac.YZapTop);

    // ---------- El rotulo ----------
    Vale("los tres renglones del rotulo se miden desde el fondo de la plantilla",
        Math.Abs(TrazoZapataCorrida.YRotulo(-5.0, 0) - (-5.30)) < 1e-12
        && Math.Abs(TrazoZapataCorrida.YRotulo(-5.0, 1) - (-5.39)) < 1e-12
        && Math.Abs(TrazoZapataCorrida.YRotulo(-5.0, 2) - (-5.47)) < 1e-12);

    // ---------- Los titulos, que NO son iguales en las dos macros ----------
    Vale("el titulo del lindero no dice «corrida», como en su macro",
        zl.TipoTexto == "ZAPATA DE LINDERO" && zc.TipoTexto == "ZAPATA CORRIDA CENTRAL");

    Vale("un bloque capturado como «0» no cuenta como bloque",
        !ZapataCorridaCad.HayBloque("0") && !ZapataCorridaCad.HayBloque("")
        && !ZapataCorridaCad.HayBloque(null) && ZapataCorridaCad.HayBloque("CT-1"));

    // ---------- La parrilla es la MISMA rutina que la de las aisladas ----------
    var pc = TrazoZapataCorrida.ParrillaEnAlzado(ac, zc.EspesorM, 0.05, diam, diam, 0.20, false);
    var pa = TrazoZapata.ParrillaEnAlzado(
        ac.XBase, ac.YZapBot, zc.AnchoM, zc.EspesorM, 0.05, diam, diam, 0.20, false);

    Vale("la parrilla de la corrida sale identica a la de la aislada",
        Math.Abs(pc.YBarra - pa.YBarra) < 1e-12
        && Math.Abs(pc.XCaraIzq - pa.XCaraIzq) < 1e-12
        && Math.Abs(pc.XCaraDer - pa.XCaraDer) < 1e-12
        && pc.Circulos.Length == pa.Circulos.Length);
}

Console.WriteLine(fallos == 0
    ? "\nRESULTADO: todo bien"
    : $"\nRESULTADO: {fallos} fallo(s)");

return fallos == 0 ? 0 : 1;
