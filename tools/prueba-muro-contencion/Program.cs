using CadLink.Cad;

int fallos = 0;

void Vale(string que, bool ok)
{
    Console.WriteLine((ok ? "  OK    " : "  FALLA ") + que);
    if (!ok) { fallos++; }
}

bool Casi(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

VarMuro V(string clave, double cm) => new(clave, cm);

// El muro de la imagen: 4.35 m de pantalla, zapata de 5.00 x 0.65, punta 1.67, espolon.
var armado = new MuroContencionCad
{
    Tipo = MuroContencionCad.ConcretoArmado, Id = "MC-01", Fc = "250",
    AlturaM = 4.35, EspesorZapataM = 0.65, BaseM = 5.00, PuntaM = 1.67,
    CoronaM = 0.30, EspesorPieM = 0.60, RecCm = 5,
    Espolon = true, EspolonDistM = 1.82, EspolonAnchoM = 0.65, EspolonProfM = 0.65,
    VarVertTierra = V("#5", 1.59), SepVertTierraCm = 10,
    VarVertExt = V("#6", 1.91), SepVertExtCm = 33,
    VarHoriz = V("#6", 1.91), SepHorizCm = 33,
    VarEspiga = V("#6", 1.91), SepEspigaCm = 66, LongEspigaM = 1.45,
    VarZapSup = V("#4", 1.27), SepZapSupCm = 30, VarRepSup = V("#4", 1.27), SepRepSupCm = 30,
    VarZapInf = V("#2", 0.64), SepZapInfCm = 20, VarRepInf = V("#2", 0.64), SepRepInfCm = 20,
    AnchoDetalleM = 2.0
};

Console.WriteLine("\n---------- Concreto armado ----------");
Vale("el muro de la imagen esta completo", TrazoMuroContencion.Problemas(armado).Count == 0);
Vale("su talon es lo que sobra: 5.00 - 1.67 - 0.60 = 2.73", Casi(armado.TalonM, 2.73, 1e-9));

var d = TrazoMuroContencion.Dibujar(armado, 0, 0);
Vale("un solo poligono de concreto", d.Concreto.Count == 1);

var conc = d.Concreto[0];
var xMinC = conc.Min(p => p.X);
var yMinC = conc.Min(p => p.Y);
var yMaxC = conc.Max(p => p.Y);
Vale("el espolon baja 0.65 bajo la zapata", Casi(yMinC, -0.65, 1e-9));
Vale("y la corona queda a 0.65 + 4.35 = 5.00", Casi(yMaxC, 5.00, 1e-9));
Vale("la zapata mide 5.00 de ancho", Casi(conc.Max(p => p.X) - xMinC, 5.00, 1e-9));

// La corona: 0.30, con la cara de la tierra vertical.
var corona = conc.Where(p => Casi(p.Y, yMaxC)).Select(p => p.X).OrderBy(x => x).ToList();
Vale("la corona mide 0.30", corona.Count == 2 && Casi(corona[1] - corona[0], 0.30, 1e-9));
Vale("la cara de la tierra es vertical: al pie esta donde en la corona",
    conc.Any(p => Casi(p.X, corona[1]) && Casi(p.Y, 0.65)));

Vale("el detalle de la pantalla va a la IZQUIERDA del corte", d.Lineas.Single().Puntos.Max(p => p.X) < xMinC);

var espigas = d.Varillas.Where(v => v.Oculta).ToList();
Vale("las espigas van a trazos, en el corte y en el detalle", espigas.Count >= 2);
var esp = espigas.First(v => v.Puntos[0].Y < 0);
Vale("la del corte arranca dentro del espolon", esp.Puntos[0].Y > -0.65 && esp.Puntos[0].Y < 0);
Vale("y sube 1.45 sobre la zapata", Casi(esp.Puntos[1].Y, 0.65 + 1.45, 1e-9));

var xTierra = corona[1];
Vale("la espiga del corte queda dentro de la pantalla", esp.Puntos[0].X < xTierra && esp.Puntos[0].X > corona[0] - 0.40);

// Los lazos: el de la zapata y el de la pantalla, cerrados y por dentro del concreto.
var lazos = d.Varillas.Where(v => v.Cerrada).ToList();
Vale("dos lazos cerrados: zapata y pantalla", lazos.Count == 2);
Vale("el de la zapata queda a 5 cm del fondo", Casi(lazos[0].Puntos.Min(p => p.Y), 0.05 + (1.27 / 200), 1e-6));
Vale("el de la pantalla no se sale por la corona", lazos[1].Puntos.Max(p => p.Y) < 5.00 - 0.05 + 1e-9);

var horiz = d.Puntos.Where(p => p.Clave == "#6" && p.Y > 0.65).ToList();
Vale("las horizontales van de 33 en 33 en las dos caras", horiz.Count > 20 && horiz.Count % 2 == 0);
Vale("y no pasan de la corona", horiz.All(p => p.Y < 5.00));

Vale("rotulos con el texto de la imagen", d.Rotulos.Any(r => r.Texto == "Ø#5 @ 10 cm")
    && d.Rotulos.Any(r => r.Texto.Contains("Espigas")));
Vale("el titulo dice el tipo y el ID", d.Textos.Any(t => t.Texto == "MURO DE CONTENCION DE CONCRETO ARMADO \"MC-01\""));
Vale("y el detalle su leyenda", d.Textos.Any(t => t.Texto == "ACERO EN LA PANTALLA"));
Vale("las cotas incluyen el ancho total", d.Cotas.Any(c => !c.Vertical && Casi(c.X2 - c.X1, 5.00, 1e-9)));
Vale("y la altura de la pantalla", d.Cotas.Any(c => c.Vertical && Casi(c.Y2 - c.Y1, 4.35, 1e-9)));

// Los limites contienen todo.
Vale("los limites contienen el concreto, el detalle y los textos",
    d.XMin <= d.Lineas.Single().Puntos.Min(p => p.X) && d.XMax >= conc.Max(p => p.X) && d.YMin < yMinC && d.YMax > yMaxC);

// Colocar: la orilla izquierda va donde se pide.
var mov = TrazoMuroContencion.Dibujar(armado, 10, -20);
Vale("se coloca con su orilla izquierda donde se pide", Casi(mov.XMin, 10, 1e-9));
Vale("y su base en la Y pedida", Casi(mov.Concreto[0].Where(p => p.X > mov.Concreto[0].Min(q => q.X) + 1e-6).Max(p => p.Y) - 0, -20 + 5.00, 1e-9));
Vale("el ancho que ocupa es el de sus limites", Casi(TrazoMuroContencion.Ancho(armado), d.XMax - d.XMin, 1e-9));

// Sin espolon: ni espigas ni espolon.
var sinEsp = new MuroContencionCad
{
    Tipo = armado.Tipo, Id = "MC-02", Fc = "250", AlturaM = 3, EspesorZapataM = 0.5, BaseM = 3,
    PuntaM = 0.8, CoronaM = 0.25, EspesorPieM = 0.4, RecCm = 5, Espolon = false,
    VarVertTierra = V("#4", 1.27), SepVertTierraCm = 20, VarVertExt = V("#4", 1.27), SepVertExtCm = 30,
    VarHoriz = V("#3", 0.95), SepHorizCm = 30, VarZapSup = V("#4", 1.27), SepZapSupCm = 25,
    VarRepSup = V("#3", 0.95), SepRepSupCm = 30, VarZapInf = V("#4", 1.27), SepZapInfCm = 25,
    VarRepInf = V("#3", 0.95), SepRepInfCm = 30
};
var d2 = TrazoMuroContencion.Dibujar(sinEsp, 0, 0);
Vale("sin espolon esta completo sin pedir espigas", TrazoMuroContencion.Problemas(sinEsp).Count == 0);
Vale("sin espolon no hay espigas", !d2.Varillas.Any(v => v.Oculta));
Vale("ni nada bajo la zapata", Casi(d2.Concreto[0].Min(p => p.Y), 0, 1e-12));

// Lo que no cabe se dice.
var malo = new MuroContencionCad
{
    Tipo = armado.Tipo, Id = "MAL", AlturaM = 3, EspesorZapataM = 0.5, BaseM = 1.0, PuntaM = 0.8,
    CoronaM = 0.5, EspesorPieM = 0.4, RecCm = 5, Espolon = true, EspolonDistM = 0.8, EspolonAnchoM = 0.5,
    EspolonProfM = 0.3
};
var pm = TrazoMuroContencion.Problemas(malo);
Vale("dice que punta + pie no cabe", pm.Any(p => p.Contains("no cabe en la zapata")));
Vale("que la corona no puede ser mayor que el pie", pm.Any(p => p.Contains("espesor al pie")));
Vale("que el espolon se sale", pm.Any(p => p.Contains("espolon se sale")));
Vale("y que faltan las varillas", pm.Any(p => p.Contains("falta la varilla")));

// Una separacion absurda no congela el dibujo.
Vale("una separacion de 0.1 cm se topa en 400 varillas", TrazoMuroContencion.Repartir(0, 100, 0.001).Count == 400);

Console.WriteLine("\n---------- Concreto ciclopeo ----------");
var cic = new MuroContencionCad
{
    Tipo = MuroContencionCad.Ciclopeo, Id = "MCC-01", Fc = "150", PiedraPct = "40",
    AlturaM = 2.0, EspesorZapataM = 0.40, CM = 0.30, BM = 0.20, MM = 0.30, EM = 0.60, GM = 0.50, NM = 0.30
};
Vale("el ciclopeo de ejemplo esta completo", TrazoMuroContencion.Problemas(cic).Count == 0);
Vale("su base es M + E + G + N = 1.70", Casi(cic.BaseCiclopeoM, 1.70, 1e-9));

var dc = TrazoMuroContencion.Dibujar(cic, 0, 0);
var pc = dc.Concreto.Single();
Vale("el cuerpo tiene 8 vertices", pc.Count == 8);
var top = pc.Where(p => Casi(p.Y, 2.40)).Select(p => p.X).OrderBy(x => x).ToList();
Vale("la corona mide c + b = 0.50", Casi(top[1] - top[0], 0.50, 1e-9));
var x0c = pc.Min(p => p.X);
Vale("la cara de la tierra esta en M + E + G = 1.40", Casi(top[1] - x0c, 1.40, 1e-9));
Vale("el pie de la cara inclinada esta en M = 0.30", pc.Any(p => Casi(p.X - x0c, 0.30) && Casi(p.Y, 0.40)));
Vale("las cotas llevan sus letras", new[] { "M", "E", "G", "N", "c", "b", "h", "d" }
    .All(l => dc.Textos.Any(t => t.Texto == l)));
Vale("el titulo dice ciclopeo y su piedra", dc.Textos.Any(t => t.Texto.Contains("CICLOPEO \"MCC-01\""))
    && dc.Textos.Any(t => t.Texto.Contains("Piedra 40%")));
Vale("el ciclopeo no lleva acero", dc.Varillas.Count == 0 && dc.Puntos.Count == 0);

var cicMal = new MuroContencionCad
{
    Tipo = MuroContencionCad.Ciclopeo, Id = "X", AlturaM = 2, EspesorZapataM = 0.4,
    CM = 0.6, BM = 0.5, MM = 0.3, EM = 0.3, GM = 0.3, NM = 0.3
};
Vale("una corona mayor que el pie se dice", TrazoMuroContencion.Problemas(cicMal).Any(p => p.Contains("colgada")));

Console.WriteLine();
Console.WriteLine(fallos == 0 ? " TODO CORRECTO" : $" {fallos} FALLO(S)");
return fallos == 0 ? 0 : 1;
