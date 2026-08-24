using CadLink.Cad;
using CadLink.Cad.PlanoEstructural;

// =====================================================================================
//  PRUEBA EJECUTABLE DE LA ETAPA 4: EJES, BURBUJAS, COTAS Y ROTULO DE LA PLANTA
// =====================================================================================
//  Se comprueban los numeros con los que se ha peleado la macro version tras version:
//  la separacion de las burbujas -EJES_INICIO_BURBUJA_M = 2.00- y la de las cotas
//  -0.75 la cadena y 1.17 la total-, que son INDEPENDIENTES; los cuatro lados de cotas;
//  las rayitas de la burbuja; y el nombre del nivel, que dice CIMENTACION en la base y
//  PLANTA BAJA en Story1.
// =====================================================================================

var fallos = 0;

void Igual(string que, object? esperado, object? real)
{
    var ok = Equals(esperado, real);
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}" +
                      (ok ? string.Empty : $"   esperado <{esperado}>, salió <{real}>"));
    if (!ok)
    {
        fallos++;
    }
}

void Cerca(string que, double esperado, double real, double tol = 1e-9)
{
    var ok = Math.Abs(esperado - real) <= tol;
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}" +
                      (ok ? string.Empty : $"   esperado <{esperado}>, salió <{real}>"));
    if (!ok)
    {
        fallos++;
    }
}

void Check(string que, bool ok)
{
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}");
    if (!ok)
    {
        fallos++;
    }
}

var cfg = new ConfigPlano();
var ejes = new EjesPlano(cfg);

Console.WriteLine("=====================================================================");
Console.WriteLine(" DONDE ARRANCAN LAS BURBUJAS Y DONDE VAN LAS COTAS");
Console.WriteLine("=====================================================================");

Cerca("las burbujas arrancan a 2.00 de la planta (EJES_INICIO_BURBUJA_M)", 2.0, ejes.SaleEjes());
Cerca("y por la derecha y abajo, lo mismo: EJES_SALE_CORTO_M en 0", 2.0, ejes.SaleEjesCorto());
Cerca("la primera cadena de cotas, a 0.75", 0.75, ejes.Separacion());
Cerca("la cota del ancho total, a 1.17", 1.17, ejes.SeparacionTotal());
Cerca("así que entre la cota total y la burbuja quedan 0.83 de aire",
      0.83, ejes.SaleEjes() - ejes.SeparacionTotal(), 1e-9);
Cerca("el radio de la burbuja es 0.35", 0.35, ejes.RadioBurbuja);
Cerca("y su anillo interior, 0.82 de eso", 0.35 * 0.82, ejes.RadioAnillo());

// LO QUE SE PELEÓ EN LA MACRO DE LA v43 A LA v49: que mover las cotas NO mueva las
// burbujas. Son dos parámetros distintos y aquí se comprueba que siguen independientes.
var mueveCotas = new ConfigPlano();
mueveCotas.Aplicar(new Dictionary<string, string> { ["COTAS_SEPARACION_TOTAL"] = "3" });
var ej2 = new EjesPlano(mueveCotas);
Cerca("mover la cota total NO mueve las burbujas", 2.0, ej2.SaleEjes());
Cerca("y la cota total sí se mueve", 3.0, ej2.SeparacionTotal());

var sinInicio = new ConfigPlano();
sinInicio.Aplicar(new Dictionary<string, string> { ["EJES_INICIO_BURBUJA_M"] = "0" });
var ej3 = new EjesPlano(sinInicio);
Cerca("con EJES_INICIO_BURBUJA_M en 0 se vuelve a la cuenta vieja: 1.17 + 0.15",
      1.32, ej3.SaleEjes());

var corto = new ConfigPlano();
corto.Aplicar(new Dictionary<string, string> { ["EJES_RECORTE_M"] = "0.5" });
Cerca("y EJES_RECORTE_M acorta la derecha y el abajo", 1.5,
      new EjesPlano(corto).SaleEjesCorto());

Console.WriteLine();
Console.WriteLine(" Cuánto baja lo que va debajo de la planta (AbajoDeEjes)");

// punta del eje 2.00 + la burbuja 2 x 0.35 + su rayita 0.35 x 0.9
Cerca("la punta, la burbuja y su rayita", 2.0 + 0.7 + 0.315, ejes.AbajoDeEjes(true));
Cerca("y sin ejes, solo la punta", 2.0, ejes.AbajoDeEjes(false));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LOS EJES, COLOCADOS SOBRE UNA PLANTA DE 12 x 9");
Console.WriteLine("=====================================================================");

var enX = new List<(string, double)> { ("1", 0), ("2", 4), ("3", 8), ("4", 12) };
var enY = new List<(string, double)> { ("A", 0), ("B", 4.5), ("C", 9) };

var verticales = ejes.Verticales(enX, 0, 9);
var horizontales = ejes.Horizontales(enY, 0, 12);

Igual("cuatro ejes verticales", 4, verticales.Count);
Cerca("el eje 1 arranca 2.00 debajo de la planta", -2.0, verticales[0].Desde);
Cerca("y termina 2.00 encima", 11.0, verticales[0].Hasta);
Cerca("la burbuja de abajo, un radio más allá", -2.35, verticales[0].BurbujaA);
Cerca("la de arriba, igual", 11.35, verticales[0].BurbujaB);

Igual("tres horizontales", 3, horizontales.Count);
Cerca("el eje A arranca 2.00 a la izquierda", -2.0, horizontales[0].Desde);
Cerca("y termina 2.00 a la derecha", 14.0, horizontales[0].Hasta);

Console.WriteLine();
Console.WriteLine(" LAS COTAS, EN LOS CUATRO LADOS");

var cotas = ejes.Cotas(enX.Select(e => e.Item2).ToList(), enY.Select(e => e.Item2).ToList(),
                       0, 0, 12, 9);

// 4 ejes en X -> 3 tramos + 1 total, por 2 lados = 8
// 3 ejes en Y -> 2 tramos + 1 total, por 2 lados = 6
Igual("salen 14 cotas: cadena y total, arriba, abajo, izquierda y derecha", 14, cotas.Count);
Igual("cuatro de ellas son la del ancho total", 4, cotas.Count(c => c.EsTotal));

var arriba = cotas.Where(c => !c.EsTotal && c.Y1 == 9).ToList();
Igual("los tres tramos de arriba", 3, arriba.Count);
Cerca("y su número va a 0.75 encima de la planta", 9.75, arriba[0].YTexto);

var totalArriba = cotas.First(c => c.EsTotal && c.Y1 == 9);
Cerca("la total de arriba mide de eje a eje: 0 a 12", 0, totalArriba.X1);
Cerca("hasta el 12", 12, totalArriba.X2);
Cerca("y su número va a 1.17", 10.17, totalArriba.YTexto);

var abajo = cotas.Where(c => !c.EsTotal && c.Y1 == 0 && c.X1 != c.X2).ToList();
Cerca("las de abajo van al otro lado: -0.75", -0.75, abajo[0].YTexto);

var izq = cotas.Where(c => !c.EsTotal && c.X1 == 0 && c.Y1 != c.Y2).ToList();
Igual("los dos tramos de la izquierda", 2, izq.Count);
Cerca("y su número, a -0.75", -0.75, izq[0].XTexto);

var der = cotas.Where(c => !c.EsTotal && c.X1 == 12 && c.Y1 != c.Y2).ToList();
Cerca("las de la derecha, a +0.75", 12.75, der[0].XTexto);

// Los lados se apagan por separado, y con dos ejes no hay cota total: seria la misma
// linea que la cadena, dibujada dos veces.
var soloArriba = new ConfigPlano();
soloArriba.Aplicar(new Dictionary<string, string>
{
    ["COTAS_ABAJO"] = "NO",
    ["COTAS_IZQUIERDA"] = "NO",
    ["COTAS_DERECHA"] = "NO"
});
Igual("se pueden apagar tres lados", 4,
      new EjesPlano(soloArriba).Cotas(enX.Select(e => e.Item2).ToList(),
                                      enY.Select(e => e.Item2).ToList(),
                                      0, 0, 12, 9).Count);

var dos = new List<double> { 0, 5 };
Igual("con dos ejes no hay cota total", 0,
      ejes.Cotas(dos, new List<double>(), 0, 0, 5, 5).Count(c => c.EsTotal));

var uno = new List<double> { 0 };
Igual("y con uno no hay ninguna", 0, ejes.Cotas(uno, uno, 0, 0, 5, 5).Count);

var sinCotas = new ConfigPlano();
sinCotas.Aplicar(new Dictionary<string, string> { ["ACOTAR_EJES"] = "NO" });
Igual("ACOTAR_EJES en NO las quita todas", 0,
      new EjesPlano(sinCotas).Cotas(enX.Select(e => e.Item2).ToList(),
                                    enY.Select(e => e.Item2).ToList(), 0, 0, 12, 9).Count);

Console.WriteLine();
Console.WriteLine(" LAS RAYITAS DE LA BURBUJA");

var rayitas = ejes.RayitasDeBurbuja(0, 0, 0, 1);
Igual("cuatro rayitas con BURBUJA_CRUZ_4_LINEAS", 4, rayitas.Count);

var tres = new ConfigPlano();
tres.Aplicar(new Dictionary<string, string> { ["BURBUJA_CRUZ_4_LINEAS"] = "NO" });
Igual("tres si se apaga la cuarta", 3,
      new EjesPlano(tres).RayitasDeBurbuja(0, 0, 0, 1).Count);

var nada = new ConfigPlano();
nada.Aplicar(new Dictionary<string, string> { ["BURBUJA_CRUZ"] = "NO" });
Igual("y ninguna sin BURBUJA_CRUZ", 0,
      new EjesPlano(nada).RayitasDeBurbuja(0, 0, 0, 1).Count);

// La primera es la que se aleja del dibujo: con la burbuja abajo de la planta -mirando
// hacia arriba- esa rayita baja.
Cerca("la rayita de fuera arranca en la orilla del círculo", -0.35, rayitas[0].Y1);
Cerca("y llega a 0.35 + 0.315", -0.665, rayitas[0].Y2);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL PRIMER Y EL ULTIMO EJE, AL PANO EXTERIOR DEL MURO");
Console.WriteLine("=====================================================================");

// Una planta de 10 x 8 con muros de 15 cm en las cuatro orillas, una trabe de 30 cm sobre
// el eje B -el de en medio- y otra trabe de 40 cm encimada al muro del eje C.
var muros = new List<ElementoPlanta>
{
    // Los tres verticales: A (x=0), C (x=10) y uno interior en B (x=5) que es TRABE
    new() { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 0, Y2 = 8, AnchoM = 0.15 },
    new() { Clase = ClasePlanta.Muro, X1 = 10, Y1 = 0, X2 = 10, Y2 = 8, AnchoM = 0.15 },
    new() { Clase = ClasePlanta.Trabe, X1 = 10, Y1 = 0, X2 = 10, Y2 = 8, AnchoM = 0.40 },
    new() { Clase = ClasePlanta.Trabe, X1 = 5, Y1 = 0, X2 = 5, Y2 = 8, AnchoM = 0.30 },

    // Y los horizontales: 1 (y=0) con muro de 20, 2 (y=8) solo con una trabe de 25
    new() { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 10, Y2 = 0, AnchoM = 0.20 },
    new() { Clase = ClasePlanta.Trabe, X1 = 0, Y1 = 8, X2 = 10, Y2 = 8, AnchoM = 0.25 }
};

var ejesX = new List<(string Id, double Ordenada)> { ("A", 0), ("B", 5), ("C", 10) };
var ejesY = new List<(string Id, double Ordenada)> { ("1", 0), ("2", 8) };

var alPanoX = ejes.AlPanoExterior(ejesX, verticales: true, muros);

Cerca("el eje A se corre medio espesor a la IZQUIERDA", -0.075, alPanoX[0].Ordenada);
Cerca("el B, el de en medio, NO se mueve", 5, alPanoX[1].Ordenada);
Cerca("y el C, medio espesor a la DERECHA", 10.075, alPanoX[2].Ordenada);
Igual("los nombres de las burbujas se conservan", "A", alPanoX[0].Id);

// MANDA EL MURO: sobre el eje C hay un muro de 15 y una trabe de 40, y el paño lo tiene
// que dar el MURO -0.075-, no la trabe -0.20-. Es lo que se ve en el plano.
Check("sobre el eje C manda el muro y no la trabe de 40",
      Math.Abs(alPanoX[2].Ordenada - 10.075) < 1e-9);

Cerca("y medio ancho sobre el eje C es el del muro", 0.075,
      ejes.MedioAnchoSobreEje(10, vertical: true, muros));

// La lista que llega NO se toca: dibujar dos veces no corre los ejes dos veces.
Cerca("la lista original se queda intacta", 0, ejesX[0].Ordenada);

var alPanoY = ejes.AlPanoExterior(ejesY, verticales: false, muros);

Cerca("el eje 1 baja medio muro de 20", -0.10, alPanoY[0].Ordenada);
// En el eje 2 no hay muro, solo una trabe: se usa la trabe, que es el respaldo.
Cerca("el eje 2 sube medio ancho de la trabe, que es lo unico que hay", 8.125,
      alPanoY[1].Ordenada);

// Un muro que solo CRUZA el eje no cuenta: si contara, el eje se correria por un pano
// que no existe.
var cruzando = new List<ElementoPlanta>
{
    new() { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 4, X2 = 10, Y2 = 4, AnchoM = 0.30 }
};
Cerca("un muro perpendicular que cruza el eje no da pano", 0,
      ejes.MedioAnchoSobreEje(0, vertical: true, cruzando));

var sinPano = new ConfigPlano();
sinPano.Aplicar(new Dictionary<string, string> { ["EJES_EXTREMOS_AL_PANO"] = "NO" });
Cerca("con EJES_EXTREMOS_AL_PANO en NO nada se mueve", 0,
      new EjesPlano(sinPano).AlPanoExterior(ejesX, true, muros)[0].Ordenada);

Cerca("la tolerancia son los 25 cm de la hoja", 0.25, ejes.ToleranciaPano);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LA SECCION DE LA COLUMNA, GIRADA COMO EN EL MODELO");
Console.WriteLine("=====================================================================");

// Sin giro: las cuatro esquinas de una 20 x 60 centrada en el origen.
var derecha = PlantaDrawer.EsquinasGiradas(0, 0, 0.20, 0.60, 0);
Cerca("sin giro, la primera esquina es (-0.10, -0.30)", -0.10, derecha[0]);
Cerca("y su Y", -0.30, derecha[1]);

// A 90 grados la 20 x 60 se ve como una 60 x 20, que es lo que ensena ETABS.
var girada = PlantaDrawer.EsquinasGiradas(0, 0, 0.20, 0.60, 90);
var anchoGirado = girada.Where((_, i) => i % 2 == 0).Max() -
                  girada.Where((_, i) => i % 2 == 0).Min();
var altoGirado = girada.Where((_, i) => i % 2 == 1).Max() -
                 girada.Where((_, i) => i % 2 == 1).Min();

Cerca("a 90 grados mide 0.60 de ancho", 0.60, anchoGirado, 1e-9);
Cerca("y 0.20 de alto", 0.20, altoGirado, 1e-9);

// El centro no se mueve: la columna gira sobre su nudo.
Cerca("el centro sigue en el nudo, en X", 0,
      girada.Where((_, i) => i % 2 == 0).Sum() / 4, 1e-9);
Cerca("y en Y", 0, girada.Where((_, i) => i % 2 == 1).Sum() / 4, 1e-9);

var movida = PlantaDrawer.EsquinasGiradas(3, 7, 0.30, 0.30, 45);
Cerca("una girada 45 sigue centrada en su nudo, en X", 3,
      movida.Where((_, i) => i % 2 == 0).Sum() / 4, 1e-9);
Cerca("y en Y", 7, movida.Where((_, i) => i % 2 == 1).Sum() / 4, 1e-9);

Console.WriteLine();
Console.WriteLine(" El angulo del rotulo, que nunca se lee de cabeza");

Cerca("una trabe horizontal, 0", 0, PlantaDrawer.AnguloLegible(1, 0));
Cerca("una vertical, 90", 90, PlantaDrawer.AnguloLegible(0, 1));
Cerca("a 135 se rota a -45", -45, PlantaDrawer.AnguloLegible(-1, 1), 1e-9);
Cerca("y una dibujada de derecha a izquierda, 0", 0, PlantaDrawer.AnguloLegible(-1, 0));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LA SECCION DE ACERO, DIBUJADA COMO ES");
Console.WriteLine("=====================================================================");

// El area de un poligono por la formula del zapatero: sirve para comprobar que el
// contorno es el que toca sin escribir los 24 numeros de la I a mano.
static double Area(double[] p)
{
    double a = 0;
    var n = p.Length / 2;

    for (var i = 0; i < n; i++)
    {
        var j = (i + 1) % n;
        a += (p[2 * i] * p[(2 * j) + 1]) - (p[2 * j] * p[(2 * i) + 1]);
    }

    return Math.Abs(a) / 2;
}

static double Ancho(double[] p) =>
    p.Where((_, i) => i % 2 == 0).Max() - p.Where((_, i) => i % 2 == 0).Min();

static double Alto(double[] p) =>
    p.Where((_, i) => i % 2 == 1).Max() - p.Where((_, i) => i % 2 == 1).Min();

// Una IR de 25 de peralte por 15 de patin, patin de 1 cm y alma de 0.6 cm.
const double bI = 0.25, hI = 0.15, tfI = 0.01, twI = 0.006;
var perfilI = SeccionEnPlanta.Contorno("I", bI, hI, tfI, twI);

Igual("la I tiene 12 vertices", 24, perfilI.Length);
Cerca("y mide el peralte de la seccion", bI, Ancho(perfilI), 1e-12);
Cerca("y el ancho del patin", hI, Alto(perfilI), 1e-12);
// Area = los dos patines mas el alma entre ellos.
Cerca("su area es la de dos patines y un alma",
      (2 * tfI * hI) + ((bI - (2 * tfI)) * twI), Area(perfilI), 1e-12);

var canal = SeccionEnPlanta.Contorno("C", 0.20, 0.08, 0.01, 0.006);
Igual("la canal tiene 8 vertices", 16, canal.Length);
Cerca("y su area es el alma mas los dos patines",
      (0.20 * 0.006) + (2 * 0.01 * (0.08 - 0.006)), Area(canal), 1e-12);

var te = SeccionEnPlanta.Contorno("T", 0.20, 0.10, 0.012, 0.008);
Igual("la te tiene 8 vertices", 16, te.Length);
Cerca("y su area es el patin mas el alma",
      (0.012 * 0.10) + ((0.20 - 0.012) * 0.008), Area(te), 1e-12);

// El angulo: cada ala con SU espesor. Tw es el de la pierna que mide T3 -la X-.
var angulo = SeccionEnPlanta.Contorno("L", 0.10, 0.075, 0.008, 0.006);
Igual("el angulo tiene 6 vertices", 12, angulo.Length);
Cerca("y su area es la de sus dos alas",
      (0.10 * 0.006) + (0.008 * (0.075 - 0.006)), Area(angulo), 1e-12);

// SIN ESPESORES NO HAY PERFIL: se cae al rectangulo, que es honesto. Una I inventada
// con espesores a ojo se acotaria mal.
Igual("sin espesores, la I se dibuja como caja",
      8, SeccionEnPlanta.Contorno("I", 0.25, 0.15, 0, 0).Length);
Igual("y con un patin que se come media seccion, tambien",
      8, SeccionEnPlanta.Contorno("I", 0.25, 0.15, 0.20, 0.006).Length);
Igual("el RECT de siempre sigue siendo un rectangulo",
      8, SeccionEnPlanta.Contorno("RECT", 0.20, 0.60, 0, 0).Length);
Igual("y una forma que no se reconoce, tambien",
      8, SeccionEnPlanta.Contorno("LO_QUE_SEA", 0.20, 0.60, 0.01, 0.01).Length);

Console.WriteLine();
Console.WriteLine(" El cajon y el tubo, con su hueco");

var cajon = SeccionEnPlanta.Contorno("CAJON", 0.20, 0.20, 0, 0, 0.008);
var dentro = SeccionEnPlanta.Hueco("CAJON", 0.20, 0.20, 0.008);

Igual("el cajon es el rectangulo de fuera", 8, cajon.Length);
Igual("y su hueco, otro rectangulo", 8, dentro.Length);
Cerca("el hueco mide la seccion menos dos paredes", 0.20 - 0.016, Ancho(dentro), 1e-12);
Igual("una seccion maciza no tiene hueco",
      0, SeccionEnPlanta.Hueco("RECT", 0.20, 0.20, 0.008).Length);

Check("el tubo y el circulo son redondos",
      SeccionEnPlanta.EsRedonda("TUBO") && SeccionEnPlanta.EsRedonda("CIRC")
      && !SeccionEnPlanta.EsRedonda("I"));
Igual("una redonda no da contorno de poligono",
      0, SeccionEnPlanta.Contorno("TUBO", 0.20, 0.20, 0, 0, 0.006).Length);
Cerca("el radio interior del tubo es el de fuera menos la pared", 0.094,
      SeccionEnPlanta.RadioInterior("TUBO", 0.20, 0.006), 1e-12);
Cerca("y una circular maciza no tiene radio interior", 0,
      SeccionEnPlanta.RadioInterior("CIRC", 0.20, 0.006));

Console.WriteLine();
Console.WriteLine(" El relleno de respaldo: las piezas de la seccion");

// Un SOLID solo cubre un cuadrilatero CONVEXO, y una I no lo es: se rellena con sus
// piezas. La suma de sus areas tiene que ser la del perfil.
var piezasI = SeccionEnPlanta.RectangulosDeRelleno("I", bI, hI, tfI, twI);
Igual("la I se rellena con tres piezas", 3, piezasI.Count);
Cerca("y sus areas suman la del perfil", Area(perfilI),
      piezasI.Sum(r => Math.Abs((r[2] - r[0]) * (r[3] - r[1]))), 1e-12);

var piezasCajon = SeccionEnPlanta.RectangulosDeRelleno("CAJON", 0.20, 0.20, 0, 0, 0.008);
Igual("el cajon, con sus cuatro paredes", 4, piezasCajon.Count);
Cerca("y sin pisar el hueco", Area(cajon) - Area(dentro),
      piezasCajon.Sum(r => Math.Abs((r[2] - r[0]) * (r[3] - r[1]))), 1e-12);

Igual("una redonda no tiene piezas: se queda con su achurado",
      0, SeccionEnPlanta.RectangulosDeRelleno("TUBO", 0.20, 0.20, 0, 0, 0.006).Count);
Igual("y una caja es una sola pieza",
      1, SeccionEnPlanta.RectangulosDeRelleno("RECT", 0.20, 0.60, 0, 0).Count);

Console.WriteLine();
Console.WriteLine(" Colocar: girar sobre el nudo y llevar a su sitio");

var colocada = SeccionEnPlanta.Colocar(perfilI, 5, 3, 90);
Cerca("girada 90 grados, el peralte pasa a la Y", bI, Alto(colocada), 1e-12);
Cerca("y el patin a la X", hI, Ancho(colocada), 1e-12);
Cerca("el area no cambia al girar", Area(perfilI), Area(colocada), 1e-12);
Cerca("y queda centrada en el nudo, en X", 5,
      (colocada.Where((_, i) => i % 2 == 0).Max() +
       colocada.Where((_, i) => i % 2 == 0).Min()) / 2, 1e-12);
Cerca("y en Y", 3,
      (colocada.Where((_, i) => i % 2 == 1).Max() +
       colocada.Where((_, i) => i % 2 == 1).Min()) / 2, 1e-12);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LAS LINEAS MUEREN EN EL PANO DEL CASTILLO, NO EN SU EJE");
Console.WriteLine("=====================================================================");

var pano = new PanoDeApoyo(cfg);

// Dos castillos de 15 x 15 en (0,0) y en (5,0), y un muro de eje a eje entre ellos, que es
// como lo entrega el modelo.
static ElementoPlanta Castillo(double x, double y, double b, double h, double giro = 0) =>
    new()
    {
        Clase = ClasePlanta.Columna, Tipo = "CASTILLO", Forma = "RECT",
        X1 = x, Y1 = y, X2 = x, Y2 = y,
        AnchoM = b, PeralteM = h, AnguloGrados = giro
    };

var castillos = new List<ElementoPlanta>
{
    Castillo(0, 0, 0.15, 0.15),
    Castillo(5, 0, 0.15, 0.15)
};

var muro = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 5, Y2 = 0, AnchoM = 0.15
};

var t1 = pano.Recortar(muro, castillos);

Cerca("el muro arranca en el pano del castillo, no en su eje", 0.075, t1.X1, 1e-12);
Cerca("y termina en el pano del otro", 4.925, t1.X2, 1e-12);
Cerca("sin moverse de su linea", 0, t1.Y1);
Cerca("asi que mide el claro entre castillos", 4.85, t1.Largo, 1e-12);

// EL MURO QUE QUEDO CORTO EN EL MODELO: la misma cuenta lo ALARGA hasta el pano, que es la
// otra mitad del asunto. Sin esto queda un hueco entre el muro y el castillo.
var muroCorto = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, X1 = 0.40, Y1 = 0, X2 = 4.60, Y2 = 0, AnchoM = 0.15
};

var t2 = pano.Recortar(muroCorto, castillos);

Cerca("el muro corto se alarga hasta el pano", 0.075, t2.X1, 1e-12);
Cerca("y por el otro lado tambien", 4.925, t2.X2, 1e-12);

// UN CASTILLO INTERMEDIO NO RECORTA NADA: si contara, un muro largo con un castillo a un
// metro de la punta se quedaria cortado por la mitad.
var enMedio = new List<ElementoPlanta> { Castillo(1, 0, 0.15, 0.15) };
var t3 = pano.Recortar(
    new ElementoPlanta { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 6, Y2 = 0 },
    enMedio);

Cerca("un castillo por el que el muro pasa de largo no lo recorta", 0, t3.X1);
Cerca("ni por el otro extremo", 6, t3.X2);

// El castillo GIRADO: el pano se mide sobre la seccion girada, no sobre la caja.
var girado = new List<ElementoPlanta> { Castillo(0, 0, 0.20, 0.20, 45) };
var t4 = pano.Recortar(
    new ElementoPlanta { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 4, Y2 = 0 },
    girado);

// A 45 grados, la esquina queda a 0.10*raiz(2) del centro sobre el eje X.
Cerca("en un castillo girado 45 el pano queda en la esquina", 0.10 * Math.Sqrt(2),
      t4.X1, 1e-12);

// La columna REDONDA, por su circunferencia.
var redonda = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, Forma = "CIRC", X1 = 0, Y1 = 0, X2 = 0, Y2 = 0,
        AnchoM = 0.30, PeralteM = 0.30
    }
};
Cerca("en una columna redonda, al radio", 0.15,
      pano.Recortar(
          new ElementoPlanta { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 4, Y2 = 0 },
          redonda).X1, 1e-12);

Console.WriteLine();
Console.WriteLine(" La columna W: entre los patines llega al alma; por el patin, a su cara");

var columnaW = new ElementoPlanta
{
    Clase = ClasePlanta.Columna, Forma = "I",
    X1 = 0, Y1 = 0, X2 = 0, Y2 = 0,
    AnchoM = 0.25, PeralteM = 0.15, PatinM = 0.01, AlmaM = 0.006
};

// El muro que llega POR EL PATIN -a lo largo del peralte- sale por la cara de fuera del
// patin: el rayo recorre el alma y sigue por el patin, que es material seguido.
Cerca("por el patin, al pano del patin", 0.125,
      PanoDeApoyo.SalidaDelMaterial(columnaW, 0, 0, 1, 0) ?? -99, 1e-12);

// El que ENTRA ENTRE LOS PATINES se para en la CARA DEL ALMA, que es lo primero que
// encuentra. Es el caso fino que la macro trata aparte con PANO_ALMA_W.
Cerca("entre los patines, a la cara del alma", 0.003,
      PanoDeApoyo.SalidaDelMaterial(columnaW, 0, 0, 0, 1) ?? -99, 1e-12);

// Con PANO_ALMA_W en NO se mide por la caja que envuelve al perfil: punta del patin.
Cerca("y con PANO_ALMA_W en NO, a la punta del patin", 0.075,
      PanoDeApoyo.SalidaDelMaterial(columnaW, 0, 0, 0, 1, porPiezas: false) ?? -99, 1e-12);

Console.WriteLine();
Console.WriteLine(" Los topes, para que un dato raro no se coma el muro");

// Un muro de 30 cm entre dos castillos de 60: el recorte se pasaria del 40 % por lado, asi
// que se deja como estaba. Mejor un muro que llega al eje que un muro que desaparece.
var grandes = new List<ElementoPlanta>
{
    Castillo(0, 0, 0.60, 0.60), Castillo(0.30, 0, 0.60, 0.60)
};
var t5 = pano.Recortar(
    new ElementoPlanta { Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 0.30, Y2 = 0 },
    grandes);
Cerca("un muro muy corto entre castillos grandes se deja como estaba", 0, t5.X1);
Cerca("y su otro extremo tambien", 0.30, t5.X2, 1e-12);

// Y el muro que quedo corto de MAS de 1.50 m no se estira: eso ya no es un muro corto.
var muyCorto = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, X1 = 2, Y1 = 0, X2 = 3, Y2 = 0, AnchoM = 0.15
};
Cerca("un hueco de mas de 1.50 m no se estira", 2,
      pano.Recortar(muyCorto, castillos).X1);

Cerca("el radio de busqueda son los 1.50 m de la hoja", 1.5, pano.RadioBusqueda);
Cerca("el solape es 0: la linea termina EXACTAMENTE en el pano", 0, pano.Solape);

var sinPanoCfg = new ConfigPlano();
sinPanoCfg.Aplicar(new Dictionary<string, string> { ["LINEAS_AL_PANO"] = "NO" });
Cerca("con LINEAS_AL_PANO en NO, las lineas vuelven al eje", 0,
      new PanoDeApoyo(sinPanoCfg).Recortar(muro, castillos).X1);

var conSolape = new ConfigPlano();
conSolape.Aplicar(new Dictionary<string, string> { ["PANO_SOLAPE_CM"] = "1" });
Cerca("y con solape de 1 cm, la linea se mete ese centimetro", 0.065,
      new PanoDeApoyo(conSolape).Recortar(muro, castillos).X1, 1e-12);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL ROTULO DE LA PLANTA");
Console.WriteLine("=====================================================================");

var rot = new RotuloPlanta(cfg);

Igual("el título", "PLANTA  ESTRUCTURAL", rot.Titulo);
Cerca("con altura 0.52", 0.52, rot.AlturaTitulo);
Cerca("y el segundo renglón, 0.26", 0.26, rot.AlturaNivel);
Igual("en el estilo HAETTENSCHWEILER", "HAETTENSCHWEILER", rot.Estilo);
Check("centrado y con su línea", rot.Centrado && rot.ConLinea);
Cerca("y a 0.5 de los ejes", 0.5, rot.SeparacionEjes);

Igual("la BASE se rotula CIMENTACION", "CIMENTACION", rot.NombreDeNivel("Base"));
Igual("Story1 es la PLANTA BAJA", "PLANTA BAJA", rot.NombreDeNivel("Story1"));
Igual("Story2, el PRIMER NIVEL", "PRIMER NIVEL", rot.NombreDeNivel("Story2"));
Igual("Story5, el CUARTO NIVEL", "CUARTO NIVEL", rot.NombreDeNivel("Story5"));
Igual("y con la escala detrás", "PLANTA BAJA esc. 1/75", rot.RenglonDelNivel("Story1"));
Igual("un nombre que no se reconoce sale tal cual", "AZOTEA", rot.NombreDeNivel("Azotea"));

// El detalle de EsCimentacion: comparacion EXACTA, no «contiene».
Check("Base es la cimentación", rot.EsCimentacion("base") && rot.EsCimentacion("BASE"));
Check("pero Basement y Base2 NO",
      !rot.EsCimentacion("Basement") && !rot.EsCimentacion("Base2"));

Igual("el número se lee del FINAL del nombre", 12, RotuloPlanta.NumeroDeStory("N 12"));
Igual("Story3 es el 3", 3, RotuloPlanta.NumeroDeStory("Story3"));
// Y este es el comportamiento REAL de la macro, que conviene tener escrito: lee de atrás
// hacia adelante SALTANDO las letras del final, así que «2do Piso» da 2 —no 0— y «Nivel 3
// Azotea» da 3. Se para en el primer no-dígito DESPUÉS de haber encontrado cifras, y por
// eso «N 12 bis» da 12 y no 121.
Igual("salta las letras del final: «2do Piso» da 2", 2, RotuloPlanta.NumeroDeStory("2do Piso"));
Igual("y para en el primer hueco: «N 12 bis» da 12", 12, RotuloPlanta.NumeroDeStory("N 12 bis"));
Igual("sin ningún número, 0", 0, RotuloPlanta.NumeroDeStory("Azotea"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(fallos == 0 ? " RESULTADO: todo bien" : $" RESULTADO: {fallos} fallaron");
Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
