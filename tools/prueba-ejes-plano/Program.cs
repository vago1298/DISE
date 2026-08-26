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
Console.WriteLine(" EL EJE DE ORILLA CON SOLO UN CASTILLO: EL PANO DEL K 15X15");
Console.WriteLine("=====================================================================");

// LO QUE SE REPORTO: "el ultimo eje de izquierda a derecha no esta poniendo la cota a
// pano". En el eje de orilla muchas veces NO corre ningun muro a lo largo: solo esta el
// castillo -un K 15X15- con los muros y las cadenas LLEGANDO a el en perpendicular. Una
// columna en planta es un PUNTO, asi que la comprobacion de los dos extremos no le servia,
// se devolvia cero, el eje no se corria y la cota de orilla quedaba A EJE.
var soloCastillo = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
        AnchoM = 0.15, PeralteM = 0.15
    },
    // La cadena LLEGA al castillo en perpendicular: no corre sobre el eje, y por eso no
    // daba pano. Sigue sin darlo, y esta bien: el pano lo da el castillo.
    new() { Clase = ClasePlanta.Trabe, X1 = 9, Y1 = 0, X2 = 12, Y2 = 0, AnchoM = 0.15 }
};

Cerca("un castillo de 15x15 sobre el eje da su medio pano", 0.075,
      ejes.MedioAnchoSobreEje(12, vertical: true, soloCastillo));

var conCastillo = ejes.AlPanoExterior(
    new List<(string Id, double Ordenada)> { ("10", 10.575), ("11", 12) },
    verticales: true, soloCastillo);

Cerca("y el ultimo eje SE CORRE al pano, 7.5 cm hacia afuera", 12.075,
      conCastillo[1].Ordenada);
Igual("con su burbuja intacta", "11", conCastillo[1].Id);

// EL GIRO CUENTA: un castillo de 15x40 girado 90 grados saca pano a 20 cm del eje, no a
// 7.5. Se mide con la caja que ENVUELVE a la seccion girada, igual que su rotulo.
var castilloGirado = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
        AnchoM = 0.15, PeralteM = 0.40, AnguloGrados = 90
    }
};
Cerca("un castillo de 15x40 girado 90 da 20 cm de pano en X", 0.20,
      ejes.MedioAnchoSobreEje(12, vertical: true, castilloGirado));
Cerca("y el mismo, sin girar, da 7.5", 0.075,
      ejes.MedioAnchoSobreEje(
          12, vertical: true,
          new List<ElementoPlanta>
          {
              new()
              {
                  Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
                  AnchoM = 0.15, PeralteM = 0.40
              }
          }));

// SIN MEDIDAS: el respaldo son los 15x15 del castillo de siempre, el mismo que en planta.
Cerca("un castillo sin medidas cae en los 15x15 de siempre", 0.075,
      ejes.MedioAnchoSobreEje(
          12, vertical: true,
          new List<ElementoPlanta>
          {
              new() { Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0 }
          }));

// EL ORDEN DE LA OBRA: manda el MURO, luego la TRABE, y el castillo solo cuando no hay ni
// una ni otra. Un muro de 30 sobre el eje deja al castillo de 15 sin voz.
var muroYCastillo = new List<ElementoPlanta>
{
    new() { Clase = ClasePlanta.Muro, X1 = 12, Y1 = 0, X2 = 12, Y2 = 6, AnchoM = 0.30 },
    new()
    {
        Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
        AnchoM = 0.15, PeralteM = 0.15
    }
};
Cerca("con muro sobre el eje manda el muro, no el castillo", 0.15,
      ejes.MedioAnchoSobreEje(12, vertical: true, muroYCastillo));

var trabeYCastillo = new List<ElementoPlanta>
{
    new() { Clase = ClasePlanta.Trabe, X1 = 12, Y1 = 0, X2 = 12, Y2 = 6, AnchoM = 0.25 },
    new()
    {
        Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
        AnchoM = 0.15, PeralteM = 0.15
    }
};
Cerca("y sin muro manda la trabe que corre sobre el eje", 0.125,
      ejes.MedioAnchoSobreEje(12, vertical: true, trabeYCastillo));

// UN CASTILLO QUE NO ESTA EN EL EJE no da pano: correr el eje por una columna que esta a
// metro y medio descuadraria la cota total.
Cerca("un castillo lejos del eje no da pano", 0,
      ejes.MedioAnchoSobreEje(12, vertical: true,
          new List<ElementoPlanta>
          {
              new()
              {
                  Clase = ClasePlanta.Columna, X1 = 10.5, Y1 = 0, X2 = 10.5, Y2 = 0,
                  AnchoM = 0.15, PeralteM = 0.15
              }
          }));

// Y en la otra direccion, el eje de los numeros: se mide el PERALTE, no el ancho.
Cerca("en un eje horizontal el castillo mide su peralte", 0.20,
      ejes.MedioAnchoSobreEje(0, vertical: false,
          new List<ElementoPlanta>
          {
              new()
              {
                  Clase = ClasePlanta.Columna, X1 = 12, Y1 = 0, X2 = 12, Y2 = 0,
                  AnchoM = 0.15, PeralteM = 0.40
              }
          }));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" UN EJE, UNA LINEA: LOS EJES REPETIDOS");
Console.WriteLine("=====================================================================");

// La cuadricula del modelo trae el mismo eje declarado dos veces -una en el sistema
// principal y otra como secundario- y salian DOS lineas encima de la otra, con dos
// burbujas superpuestas y dos cotas pisandose. En el plano eso se ve como un eje MAS
// GRUESO que los demas, que es lo que se reporto.
var conRepes = new List<(string Id, double Ordenada)>
{
    ("1", 0.0),
    ("2", 3.0),
    ("Grid2", 3.0),        // el mismo eje, con otro nombre
    ("3", 3.0005),         // medio milimetro: redondeo, es el mismo
    ("4", 6.0)
};

var unicos = EjesPlano.SinRepetidos(conRepes, 0.01);

Igual("de cinco ejes declarados quedan tres distintos", 3, unicos.Count);
Igual("del repetido se guarda el PRIMERO, que trae el nombre bueno", "2", unicos[1].Id);
Cerca("y su ordenada", 3.0, unicos[1].Ordenada);
Igual("el ultimo sigue estando", "4", unicos[2].Id);

Cerca("la tolerancia de la hoja es 1 cm", 0.01, ejes.ToleranciaUnirEjes);
Igual("dos ejes de verdad a 2 cm NO se unen", 2,
      EjesPlano.SinRepetidos(
          new List<(string, double)> { ("A", 0), ("B", 0.02) }, 0.01).Count);

// Con 0 no se une nada: es la salida de emergencia si alguien tiene dos ejes pegados a
// proposito.
var sinUnir = new ConfigPlano();
sinUnir.Aplicar(new Dictionary<string, string> { ["EJES_UNIR_TOL_CM"] = "0" });
Igual("con EJES_UNIR_TOL_CM en 0 se dibujan los dos", 5,
      new EjesPlano(sinUnir).SinRepetidos(conRepes).Count);

Igual("una lista vacia no revienta", 0,
      EjesPlano.SinRepetidos(new List<(string, double)>(), 0.01).Count);

// Y la capa de los ejes se manda al FONDO -Send to Back-, de ULTIMA, que es lo que la
// deja debajo de la losa y de su armado.
var alFondo = new CapasPlano(cfg).CapasAlFondo();
Check("E-EJES esta entre las capas que se mandan al fondo", alFondo.Contains("E-EJES"));
Igual("y va de ULTIMA, asi que queda abajo de todas", "E-EJES", alFondo[alFondo.Count - 1]);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL ROTULO DE LA LOSA, Y EL CORTO DEL VOLADO");
Console.WriteLine("=====================================================================");

var hoja = new[]
{
    "Losa de %U",
    "  %E   cm de espesor",
    "Var. #      @               cm.",
    "Ambos sentidos"
};

var completo = PlantaDrawer.ArmarRotuloDeLosa(hoja, soloArmado: false, "ENTREPISO", "10");

Igual("la losa normal lleva los cuatro renglones", 4,
      completo.Split("\\P").Length);
Check("con el uso en el primero", completo.StartsWith("Losa de ENTREPISO"));
Check("y el espesor en el segundo", completo.Contains("10   cm de espesor"));

// EL VOLADO: su NOMBRE en el primer renglon -«Losa VOLADO»- y sin el renglon del
// espesor. Se pidio asi, y la palabra sale de las NOTAS de la propiedad en ETABS.
var hojaVolado = new[]
{
    "Losa %U",                            // VOLADO_TEXTO_1, sin el «de»
    "  %E   cm de espesor",
    "Var. #      @               cm.",
    "Ambos sentidos"
};

var rotuloCorto = PlantaDrawer.ArmarRotuloDeLosa(
    hojaVolado, soloArmado: true, "VOLADO", "10");

Igual("el volado lleva TRES renglones", 3, rotuloCorto.Split("\\P").Length);
Igual("el nombre va en el primero, y los otros dos son el armado",
      "Losa VOLADO\\PVar. #      @               cm.\\PAmbos sentidos", rotuloCorto);
Check("dice «Losa VOLADO»", rotuloCorto.StartsWith("Losa VOLADO"));
Check("y NO lleva el renglon del espesor", !rotuloCorto.Contains("espesor"));

// El volado se reconoce con las MISMAS palabras que el achurado ANSI37: si una losa sale
// achurada, sale tambien con el rotulo corto.
var palabras = cfg.Texto("LOSA_PALABRAS_VOLADO", "VOLADO,VOLADIZO,VOLADA,CANTILEVER");

Check("la nota VOLADO lo dice", LosaEnPlanta.DiceVolado("VOLADO", "Losa 10", palabras));

// Y CUAL es la palabra, porque esa es la que se rotula. Las NOTAS primero: es donde el
// ingeniero lo escribe -el campo de notas de la propiedad de la losa en ETABS-.
Igual("la palabra sale de las NOTAS, aunque la seccion no diga nada", "VOLADO",
      LosaEnPlanta.PalabraVolado("VOLADO", "LOSA 10", palabras));
Igual("manda la nota sobre el nombre de la seccion", "VOLADIZO",
      LosaEnPlanta.PalabraVolado("VOLADIZO", "LOSA VOLADO", palabras));
Igual("sin notas, se usa el nombre de la seccion", "VOLADO",
      LosaEnPlanta.PalabraVolado(null, "LOSA VOLADO", palabras));
Igual("y una losa normal no da palabra", "",
      LosaEnPlanta.PalabraVolado("ENTREPISO", "LOSA ENTREPISO", palabras));
Check("y la seccion LOSA VOLADO tambien",
      LosaEnPlanta.DiceVolado(null, "LOSA VOLADO", palabras));
Check("una losa de ENTREPISO no",
      !LosaEnPlanta.DiceVolado("ENTREPISO", "LOSA ENTREPISO", palabras));
// LA BANDERA VA EN **NO**: se pidio que el rotulo del volado lleve tambien el renglon del
// espesor, en el segundo, y la varilla en el tercero. O sea los cuatro renglones.
Check("el rotulo del volado NO se salta ningun renglon",
      !cfg.Bandera("VOLADO_ROTULO_SOLO_ARMADO", true));

// Y EL NOMBRE «VOLADO» NO SE ESCRIBE NUNCA: el nombre que se iba a poner sale de la
// seccion, asi que si esa seccion dice VOLADO el rotulo se acorta antes de escribirlo.
Igual("de «LOSA VOLADO» el nombre seria VOLADO", "VOLADO",
      PlantaDrawer.SinLaPalabraLosa("LOSA VOLADO"));
Check("y ese nombre se reconoce como volado, asi que el rotulo se acorta",
      LosaEnPlanta.DiceVolado(PlantaDrawer.SinLaPalabraLosa("LOSA VOLADO"), null, palabras));

// EL PRIMER RENGLON DEL VOLADO LLEVA EL «de»: «Losa de VOLADO», como se pidio.
Igual("el primer renglon del volado es «Losa de %U»", "Losa de %U",
      cfg.TextoTalCual("VOLADO_TEXTO_1"));
Igual("y con la palabra del modelo queda «Losa de VOLADO»",
      "Losa de VOLADO",
      PlantaDrawer.ArmarRotuloDeLosa(
          new[] { cfg.TextoTalCual("VOLADO_TEXTO_1"), "  %E  cm de espesor", "", "" },
          soloArmado: true, "VOLADO", "10"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL Z-BUFFER: POR ESO LA LOSA SE VEIA CORTADA");
Console.WriteLine("=====================================================================");

// El caso que se veia en la vista extruida: DOS CARAS QUE SE ATRAVIESAN. Ordenar por la
// profundidad MEDIA de cada cara no tiene solucion correcta, porque cada una esta delante
// en una mitad; el algoritmo del pintor tiene que elegir una entera y de ahi que la losa
// saliera cortada por el muro.
var rz = new RasterZ(40, 40);
rz.Limpiar(0);

// Cara A: horizontal, inclinada en profundidad -de z=0 a la izquierda a z=20 a la derecha-.
rz.Triangulo(0, 0, 0, 40, 0, 20, 40, 40, 20, unchecked((int)0xFFAAAAAA));
rz.Triangulo(0, 0, 0, 40, 40, 20, 0, 40, 0, unchecked((int)0xFFAAAAAA));

// Cara B: la que la cruza, inclinada al contrario -z=20 a la izquierda y z=0 a la derecha-.
rz.Triangulo(0, 0, 20, 40, 0, 0, 40, 40, 0, unchecked((int)0xFF0000FF));
rz.Triangulo(0, 0, 20, 40, 40, 0, 0, 40, 20, unchecked((int)0xFF0000FF));

// A la IZQUIERDA gana la primera -ahi esta mas cerca- y a la DERECHA la segunda: las dos
// se ven, cada una en su mitad, que es lo que el orden por caras no puede hacer.
Igual("a la izquierda queda la cara que ahi esta mas cerca",
      unchecked((int)0xFFAAAAAA), rz.PixelEn(4, 20));
Igual("y a la derecha, la otra", unchecked((int)0xFF0000FF), rz.PixelEn(36, 20));

// Y la profundidad guardada es la del que gano, no la media de nadie.
Cerca("la profundidad del pixel es la de la cara que gano", 2, rz.ProfundidadEn(4, 20), 0.6);

// LO DE SIEMPRE, comprobado: lo que esta detras NO tapa lo de delante, sin importar en
// que orden se pinte.
var orden = new RasterZ(10, 10);
orden.Limpiar(0);
orden.Triangulo(0, 0, 1, 10, 0, 1, 10, 10, 1, unchecked((int)0xFF112233));   // cerca
orden.Triangulo(0, 0, 9, 10, 0, 9, 10, 10, 9, unchecked((int)0xFF445566));   // lejos, DESPUES

Igual("lo de detras no tapa lo de delante aunque se pinte despues",
      unchecked((int)0xFF112233), orden.PixelEn(8, 2));

// Y al reves: lo de delante SI tapa lo de detras.
var tapa = new RasterZ(10, 10);
tapa.Limpiar(0);
tapa.Triangulo(0, 0, 9, 10, 0, 9, 10, 10, 9, unchecked((int)0xFF445566));
tapa.Triangulo(0, 0, 1, 10, 0, 1, 10, 10, 1, unchecked((int)0xFF112233));

Igual("y lo de delante si tapa lo de detras",
      unchecked((int)0xFF112233), tapa.PixelEn(8, 2));

// El fondo se queda donde no se pinta nada.
Igual("fuera de los triangulos queda el fondo", 0, tapa.PixelEn(1, 8));
Cerca("y su profundidad es el infinito", RasterZ.Lejos, tapa.ProfundidadEn(1, 8), 1);

// Un triangulo degenerado no revienta ni pinta nada.
var deg = new RasterZ(8, 8);
deg.Limpiar(7);
deg.Triangulo(0, 0, 1, 4, 4, 1, 8, 8, 1, 99);
Igual("un triangulo sin area no pinta", 7, deg.PixelEn(4, 4));

// LAS ARISTAS quedan DELANTE de su propia cara: sin el sesgo, la mitad de sus pixeles
// perderia el desempate contra la cara y el contorno saldria a puntos.
var ar = new RasterZ(20, 20);
ar.Limpiar(0);
ar.Triangulo(0, 0, 5, 20, 0, 5, 20, 20, 5, 1000);
ar.Linea(0, 0, 5, 20, 20, 5, 2000);

Check("la arista se ve sobre su cara", ar.PixelEn(10, 10) == 2000);

// Fuera del buffer no se pinta ni se cae.
var borde = new RasterZ(5, 5);
borde.Limpiar(0);
borde.Triangulo(-50, -50, 1, -40, -50, 1, -40, -40, 1, 123);
Igual("lo que cae fuera del lienzo no pinta", 0, borde.PixelEn(0, 0));
Igual("y preguntar fuera del lienzo no revienta", 0, borde.PixelEn(99, 99));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL PAÑO DE LA LOSA Y LOS VOLADOS PEGADOS");
Console.WriteLine("=====================================================================");

// Una losa de 4x3 con una cadena de 15 cm debajo de su orilla de abajo -y = 0-. El
// concreto de la losa no llega al EJE de la cadena: llega a su PAÑO, medio espesor
// mas adentro.
var losaCuadro = new List<(double X, double Y)> { (0, 0), (4, 0), (4, 3), (0, 3) };

var cadenaAbajo = new List<ElementoPlanta>
{
    PanoDeApoyo.Huella(
        new ElementoPlanta
        {
            Clase = ClasePlanta.Trabe, X1 = 0, Y1 = 0, X2 = 4, Y2 = 0, AnchoM = 0.15
        },
        0.15)
};

Cerca("la cadena de 15 mete el pano 7.5 cm", 0.075,
      PanoDeLosa.MedioAnchoDelMuro((0, 0), (4, 0), cadenaAbajo));
Cerca("y un lado sin cadena no se mete nada", 0,
      PanoDeLosa.MedioAnchoDelMuro((0, 3), (4, 3), cadenaAbajo));

var panoLosa = PanoDeLosa.AlPano(losaCuadro, cadenaAbajo);

Igual("el pano tiene los mismos cuatro vertices", 4, panoLosa.Count);
Cerca("la orilla con cadena sube al pano: y = 0.075", 0.075, panoLosa.Min(v => v.Y));
Cerca("la de arriba se queda donde estaba", 3, panoLosa.Max(v => v.Y));
Cerca("y los lados sin cadena no se mueven, en X", 0, panoLosa.Min(v => v.X));
Cerca("ni el otro", 4, panoLosa.Max(v => v.X));

// UNA CADENA PERPENDICULAR que solo toca la orilla NO mete el pano: no esta debajo de
// esa orilla.
var cadenaCruza = new List<ElementoPlanta>
{
    PanoDeApoyo.Huella(
        new ElementoPlanta
        {
            Clase = ClasePlanta.Trabe, X1 = 2, Y1 = -1, X2 = 2, Y2 = 4, AnchoM = 0.15
        },
        0.15)
};

Cerca("una cadena perpendicular no mete el pano", 0,
      PanoDeLosa.MedioAnchoDelMuro((0, 0), (4, 0), cadenaCruza));

// DOS VOLADOS PEGADOS: la orilla que comparten no se dibuja, para que se vea un solo
// perimetro. Es casi siempre una losa partida en dos por un eje.
var vecina = new List<(double X, double Y)> { (4, 0), (8, 0), (8, 3), (4, 3) };
var vecinas = new List<IReadOnlyList<(double X, double Y)>> { vecina };

Check("la orilla compartida con el otro volado se reconoce",
      PanoDeLosa.ContornoCompartido(new LosaEnPlanta.Segmento(4, 0, 4, 3), vecinas));
Check("y la del borde libre, no",
      !PanoDeLosa.ContornoCompartido(new LosaEnPlanta.Segmento(0, 0, 0, 3), vecinas));
// Aunque los vertices no coincidan: la vecina puede estar partida de otra forma.
Check("se reconoce por la orilla, no por los vertices",
      PanoDeLosa.ContornoCompartido(new LosaEnPlanta.Segmento(4, 0.5, 4, 2), vecinas));
Check("sin vecinas no hay nada compartido",
      !PanoDeLosa.ContornoCompartido(
          new LosaEnPlanta.Segmento(4, 0, 4, 3),
          new List<IReadOnlyList<(double X, double Y)>>()));

Check("la hoja pide un solo perimetro", cfg.Bandera("VOLADO_SIN_DIVISIONES", false));
Check("y el hatch al pano", cfg.Bandera("LOSA_HATCH_AL_PANO", false));

// EL ROTULO DEL VOLADO LLEVA LOS CUATRO RENGLONES: se pidio el espesor en el segundo y
// la varilla en el tercero.
Check("el volado ya no se salta el renglon del espesor",
      !cfg.Bandera("VOLADO_ROTULO_SOLO_ARMADO", true));

var rotuloVolado = PlantaDrawer.ArmarRotuloDeLosa(
    new[]
    {
        cfg.TextoTalCual("VOLADO_TEXTO_1"),
        "       cm de espesor",
        "Var. #      @               cm.",
        "Ambos sentidos"
    },
    soloArmado: false, "VOLADO", "10");

Igual("el volado lleva CUATRO renglones", 4, rotuloVolado.Split("\\P").Length);
Check("el primero es «Losa de VOLADO»", rotuloVolado.StartsWith("Losa de VOLADO"));
Check("el segundo, el espesor", rotuloVolado.Split("\\P")[1].Contains("cm de espesor"));
Check("el tercero, la varilla", rotuloVolado.Split("\\P")[2].Contains("Var. #"));
Igual("y el cuarto, los sentidos", "Ambos sentidos", rotuloVolado.Split("\\P")[3]);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL CORTE POR UN EJE: EL ALZADO DEL MODELO");
Console.WriteLine("=====================================================================");

// Un modelo de juguete: una columna de 15x15 que sube 2.7 m sobre el eje X = 5, una
// trabe que corre A LO LARGO del corte y otra que lo CRUZA.
var enElEje = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, Etiqueta = "K1", Seccion = "K 15X15",
        X1 = 5, Y1 = 0, X2 = 5, Y2 = 0, Z1 = 0, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.15
    },
    new()
    {
        // A lo largo del corte: va en Y, y el corte es por un eje en X.
        Clase = ClasePlanta.Trabe, Etiqueta = "T1", Seccion = "CC 15X25",
        X1 = 5, Y1 = 0, X2 = 5, Y2 = 4, Z1 = 2.7, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.25
    },
    new()
    {
        // Cruza el corte: va en X, asi que solo asoma de canto.
        Clase = ClasePlanta.Trabe, Etiqueta = "T2", Seccion = "CC 15X25",
        X1 = 4, Y1 = 2, X2 = 6, Y2 = 2, Z1 = 2.7, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.25
    },
    new()
    {
        // Y una que no toca el corte: esta a 10 m.
        Clase = ClasePlanta.Trabe, Etiqueta = "T3", Seccion = "CC 15X25",
        X1 = 15, Y1 = 0, X2 = 15, Y2 = 4, Z1 = 2.7, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.25
    }
};

Check("la columna del eje entra en el corte",
      CorteEnAlzado.Entra(enElEje[0], enX: true, ordenada: 5, espesorM: 0.6));
Check("la trabe que corre por el eje, tambien",
      CorteEnAlzado.Entra(enElEje[1], enX: true, ordenada: 5, espesorM: 0.6));
Check("la que lo CRUZA tambien entra: en el corte se ve su costado",
      CorteEnAlzado.Entra(enElEje[2], enX: true, ordenada: 5, espesorM: 0.6));
Check("y la que esta a diez metros, no",
      !CorteEnAlzado.Entra(enElEje[3], enX: true, ordenada: 5, espesorM: 0.6));

// Sin el fondo, para comprobar primero solo lo que corta.
var piezas = CorteEnAlzado.Piezas(
    enElEje, enX: true, ordenada: 5, espesorM: 0.6, verElFondo: false);

Igual("del corte salen TRES piezas", 3, piezas.Count);

var col = piezas.First(p => p.Etiqueta == "K1");
Cerca("la columna se ve de nudo a nudo: 2.7 m de alto", 2.7, col.Alto);
Cerca("y del ancho de su seccion", 0.15, col.Ancho);
Cerca("arrancando en su cota", 0, col.Z);

var aLoLargo = piezas.First(p => p.Etiqueta == "T1");
// 4 m de nudo a nudo MAS la mitad del castillo que hay en un extremo: la trabe se dibuja
// completa, hasta la CARA de su apoyo, no hasta su eje. En el otro extremo no hay nada, asi
// que ahi no se le suma.
Cerca("la trabe se ve entera: 4 m mas medio castillo", 4 + 0.075, aLoLargo.Ancho);
Cerca("con su peralte", 0.25, aLoLargo.Alto);
// Cuelga DEBAJO de la cota de su eje, que es donde esta el concreto.
Cerca("y colgando debajo de su eje", 2.7 - 0.25, aLoLargo.Z);

var deCanto = piezas.First(p => p.Etiqueta == "T2");
Cerca("la que cruza se ve solo de canto", 0.15, deCanto.Ancho);
Cerca("con su peralte igual", 0.25, deCanto.Alto);

// UN MURO se ve como el paño que es: de su cota mas baja a la mas alta.
var muroDelCorte = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Etiqueta = "M1", Seccion = "MURO 15",
    X1 = 5, Y1 = 0, X2 = 5, Y2 = 3, Z1 = 0, Z2 = 2.7, AnchoM = 0.15
};

var pMuro = CorteEnAlzado.Piezas(
    new List<ElementoPlanta> { muroDelCorte }, enX: true, ordenada: 5, espesorM: 0.6);

Igual("el muro da una pieza", 1, pMuro.Count);
Cerca("de 3 m de largo", 3, pMuro[0].Ancho);
Cerca("y 2.7 de alto", 2.7, pMuro[0].Alto);

// LA LOSA no da pieza: en un corte se ve como una linea, y esa la pone el dibujante
// junto a la cota del nivel.
var losa = new ElementoPlanta { Clase = ClasePlanta.Losa, Etiqueta = "L1" };
losa.Vertices.Add((4, 0));
losa.Vertices.Add((6, 0));
losa.Vertices.Add((6, 4));
losa.Vertices.Add((4, 4));

// LA LOSA SI SE DIBUJA: en la extruida se veia y en el plano no aparecia. En un corte es
// una franja horizontal de su espesor, colgada de la cota de su paño, y es lo que da la
// lectura de los entrepisos.
losa.Z1 = 2.7;
losa.Z2 = 2.7;
losa.AnchoM = 0.10;

var pLosa = CorteEnAlzado.Piezas(
    new List<ElementoPlanta> { losa }, enX: true, ordenada: 5, espesorM: 0.6,
    verElFondo: false);

Igual("la losa da su franja en el corte", 1, pLosa.Count);
Cerca("del espesor de la losa", 0.10, pLosa[0].Alto);

// Y SI EL MODELO NO DIO EL ESPESOR, NO SE INVENTA: la losa sale como una LINEA -alto 0-.
// Esto es un plano: la franja se mide y se acota, asi que un espesor puesto a dedo no es una
// aproximacion, es un dato falso que alguien va a construir.
var losaSinEspesor = new ElementoPlanta
{
    Clase = ClasePlanta.Losa, Etiqueta = "L2", Seccion = "LOSA VOLADO",
    Z1 = 2.7, Z2 = 2.7, AnchoM = 0
};
losaSinEspesor.Vertices.Add((4, 0));
losaSinEspesor.Vertices.Add((6, 0));
losaSinEspesor.Vertices.Add((6, 4));
losaSinEspesor.Vertices.Add((4, 4));

var pSin = CorteEnAlzado.Piezas(
    new List<ElementoPlanta> { losaSinEspesor }, enX: true, ordenada: 5, espesorM: 0.6,
    verElFondo: false);

Igual("la losa sin espesor tambien sale", 1, pSin.Count);
Cerca("pero con alto CERO: es una linea, no una franja inventada", 0, pSin[0].Alto);
Cerca("y a la cota de su paño", 2.7, pSin[0].Z);
Cerca("colgada de la cota de su paño", 2.7 - 0.10, pLosa[0].Z);
Cerca("y a lo largo de lo que cruza el corte", 4, pLosa[0].Ancho);

// LO QUE SE VE AL FONDO tambien se dibuja: un corte es una VISTA, no solo la rebanada.
var colDetras = new ElementoPlanta
{
    Clase = ClasePlanta.Columna, Etiqueta = "K9", Seccion = "K 15X15",
    X1 = 9, Y1 = 0, X2 = 9, Y2 = 0, Z1 = 0, Z2 = 2.7, AnchoM = 0.15, PeralteM = 0.15
};

Check("una columna a 4 m detras del corte se ve al fondo",
      CorteEnAlzado.AlFondo(colDetras, enX: true, ordenada: 5, espesorM: 0.6));
Check("y una que esta DELANTE no se ve",
      !CorteEnAlzado.AlFondo(
          new ElementoPlanta
          {
              Clase = ClasePlanta.Columna, X1 = 1, Y1 = 0, X2 = 1, Y2 = 0,
              Z1 = 0, Z2 = 2.7, AnchoM = 0.15, PeralteM = 0.15
          },
          enX: true, ordenada: 5, espesorM: 0.6));

var conFondo = CorteEnAlzado.Piezas(
    new List<ElementoPlanta> { enElEje[0], colDetras }, enX: true, ordenada: 5,
    espesorM: 0.6, verElFondo: true);

Igual("con el fondo salen las dos columnas", 2, conFondo.Count);
Check("la del eje va marcada como cortada", conFondo.Any(q => q.Cortada));
Check("y la de atras como fondo", conFondo.Any(q => !q.Cortada));

Igual("sin el fondo sale solo la del eje", 1,
      CorteEnAlzado.Piezas(
          new List<ElementoPlanta> { enElEje[0], colDetras }, enX: true, ordenada: 5,
          espesorM: 0.6, verElFondo: false).Count);

// LA TRABE SE DIBUJA COMPLETA, no de eje a eje: el concreto llega a la CARA de sus
// apoyos, no a su eje. A cada extremo se le suma la mitad del apoyo que hay ahi.
var conApoyos = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Trabe, Etiqueta = "CC1", Seccion = "CC 15X25",
        X1 = 5, Y1 = 0, X2 = 5, Y2 = 4, Z1 = 2.7, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.25
    },
    new()
    {
        Clase = ClasePlanta.Columna, Etiqueta = "K1", Seccion = "K 15X15",
        X1 = 5, Y1 = 0, X2 = 5, Y2 = 0, Z1 = 0, Z2 = 2.7,
        AnchoM = 0.15, PeralteM = 0.15
    },
    new()
    {
        Clase = ClasePlanta.Columna, Etiqueta = "K2", Seccion = "K 20X20",
        X1 = 5, Y1 = 4, X2 = 5, Y2 = 4, Z1 = 0, Z2 = 2.7,
        AnchoM = 0.20, PeralteM = 0.20
    }
};

var pTrabe = CorteEnAlzado.Piezas(
    conApoyos, enX: true, ordenada: 5, espesorM: 0.6, verElFondo: false)
    .First(q => q.Etiqueta == "CC1");

// 4 m de eje a eje + 7.5 cm del castillo de 15 + 10 cm del de 20.
Cerca("la trabe llega a la cara de sus dos apoyos", 4 + 0.075 + 0.10, pTrabe.Ancho);
Cerca("y arranca media seccion antes de su nudo", -0.075, pTrabe.X);

// Y en un extremo SIN apoyo no se le suma nada: ahi la trabe termina de verdad.
Cerca("medio apoyo donde no hay nada es cero", 0,
      CorteEnAlzado.MedioApoyoEn(conApoyos[0], enX: true, donde: 9, todos: conApoyos));
Cerca("y donde hay un castillo de 15, 7.5 cm", 0.075,
      CorteEnAlzado.MedioApoyoEn(conApoyos[0], enX: true, donde: 0, todos: conApoyos));

// LA REBANADA NO PUEDE SER CERO: en un modelo real el muro se modela en su linea media y
// el eje pasa por su paño, asi que un corte de espesor cero se quedaria vacio.
Check("con espesor 0 se usa el minimo, no se queda vacio",
      CorteEnAlzado.Entra(
          new ElementoPlanta
          {
              Clase = ClasePlanta.Columna, X1 = 5.02, Y1 = 0, X2 = 5.02, Y2 = 0,
              Z1 = 0, Z2 = 2.7, AnchoM = 0.15, PeralteM = 0.15
          },
          enX: true, ordenada: 5, espesorM: 0));

// Y LA HOJA trae el corte a 10 m de la planta, como se pidio.
Cerca("el corte va a 10 m de la planta", 10, cfg.Numero("CORTE_SEPARACION_M", 0));
Check("y se dibuja por omision", cfg.Bandera("CORTE_DIBUJAR", false));
Igual("con su rotulo", "CORTE  POR  EL  EJE  %E", cfg.TextoTalCual("CORTE_ROTULO"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LA ESCALA DEL ACHURADO, LA QUE LO DEJA VISIBLE");
Console.WriteLine("=====================================================================");

// El ANSI37 tiene sus lineas a 0.125 de unidad. Con la escala de la macro -0.0475- la
// separacion real queda en 5.9 MILIMETROS: en un tablero de 6 x 12 m son mas de dos mil
// lineas y no se ve un achurado, se ve una MANCHA GRIS uniforme. Es lo que salia.
Cerca("con la escala de la macro las lineas quedan a 5.9 mm", 0.0059375,
      0.0475 * 0.125, 1e-9);

// LA ESCALA QUE MANDA ES LA DE LA MACRO: 0.0475, y el automatico va APAGADO. Lo que hacia
// que el achurado no se viera no era la escala -era el color 252 sobre fondo oscuro y que el
// hatch no llegaba a crearse-.
Cerca("la escala del ANSI37 es la de la macro", 0.0475,
      cfg.Numero("LOSA_HATCH_ESCALA", 0));
Check("y el automatico va apagado", !cfg.Bandera("LOSA_HATCH_ESCALA_AUTO", true));
Cerca("el color del achurado es el 142, por objeto", 142,
      cfg.Numero("LOSA_HATCH_COLOR", 0));

// Al reves: de la separacion que se quiere VER se saca la escala.
Cerca("para ver 25 cm de separacion, escala 2", 2.0,
      PlantaDrawer.EscalaDeHatch(0.25, 0.0475));
Cerca("para 12.5 cm, escala 1", 1.0, PlantaDrawer.EscalaDeHatch(0.125, 0.0475));
Cerca("y una separacion sin sentido regresa a la de la macro", 0.0475,
      PlantaDrawer.EscalaDeHatch(0, 0.0475));

Cerca("la hoja pide 25 cm", 25, cfg.Numero("LOSA_HATCH_SEPARACION_CM", 0));
Check("el automatico esta disponible pero apagado",
      !cfg.Bandera("LOSA_HATCH_ESCALA_AUTO", false)
      && ConfigPlano.PorOmision.Any(r => r.Parametro == "LOSA_HATCH_ESCALA_AUTO"));
Igual("el patron sigue siendo el ANSI37 de la macro", "ANSI37",
      cfg.Texto("LOSA_HATCH_PATRON", ""));
Cerca("y el angulo, 45 grados", 45, cfg.Numero("LOSA_HATCH_ANGULO", 0));

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
Console.WriteLine(" LA LOSA: APOYOS, VOLADO, PARRILLA Y CONTORNO");
Console.WriteLine("=====================================================================");

// Un tablero de 5 x 4 con cadenas de 15 cm en sus cuatro lados.
var tablero = new List<(double X, double Y)> { (0, 0), (5, 0), (5, 4), (0, 4) };

static ElementoPlanta Cadena(double x1, double y1, double x2, double y2, double b = 0.15) =>
    PanoDeApoyo.Huella(
        new ElementoPlanta
        {
            Clase = ClasePlanta.Trabe, Tipo = "DALA",
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, AnchoM = b
        },
        b);

var cuatroLados = new List<ElementoPlanta>
{
    Cadena(0, 0, 5, 0), Cadena(5, 0, 5, 4), Cadena(5, 4, 0, 4), Cadena(0, 4, 0, 0)
};

Igual("un tablero de cuatro esquinas tiene cuatro lados", 4,
      LosaEnPlanta.Lados(tablero).Count);

var apoyados = LosaEnPlanta.LadosApoyados(tablero, cuatroLados);
Igual("y con cadena en las cuatro, los cuatro apoyados", 4, apoyados.Count(a => a));
Check("asi que NO esta volado",
      !LosaEnPlanta.EsVolada(tablero, cuatroLados));

// UN SOLO LADO APOYADO = VOLADO. Es la regla del calculo: trabaja en cantilever y su
// acero va ARRIBA, asi que en el plano lleva su hatch y su capa propia.
var unLado = new List<ElementoPlanta> { Cadena(0, 0, 5, 0) };
Igual("con una sola cadena, un lado apoyado", 1,
      LosaEnPlanta.LadosApoyados(tablero, unLado).Count(a => a));
Check("y el pano SI esta volado", LosaEnPlanta.EsVolada(tablero, unLado));
Check("un pano sin ningun apoyo tambien se marca volado",
      LosaEnPlanta.EsVolada(tablero, new List<ElementoPlanta>()));

// LA UNION Y NO LA SUMA: dos cadenas que se traslapan cubren su tramo UNA vez. Si se
// sumaran, un lado con dos cadenitas encimadas pasaria del 100 %.
var traslapadas = new List<ElementoPlanta> { Cadena(0, 0, 3, 0), Cadena(2, 0, 5, 0) };
Cerca("dos cadenas traslapadas cubren el lado una sola vez", 1.0,
      LosaEnPlanta.FraccionApoyada(LosaEnPlanta.Lados(tablero)[0], traslapadas), 1e-9);

// Media cadena, medio lado. Con LOSA_APOYO_CUBRE = 0.7 ese lado NO cuenta como apoyado.
Cerca("media cadena apoya medio lado", 0.5,
      LosaEnPlanta.FraccionApoyada(
          LosaEnPlanta.Lados(tablero)[0],
          new List<ElementoPlanta> { Cadena(0, 0, 2.5, 0) }), 1e-9);

Console.WriteLine();
Console.WriteLine(" La parrilla del armado, recortada al pano");

// Un tablero de 3 x 2 con varillas a 50 cm: 5 en un sentido -x = 0.5 a 2.5- y 3 en el
// otro -y = 0.5, 1.0, 1.5-.
var chico = new List<(double X, double Y)> { (0, 0), (3, 0), (3, 2), (0, 2) };
var parrilla = LosaEnPlanta.Parrilla(chico, 0.5, minTramo: 0.05);

Igual("la parrilla de un tablero de 3 x 2 a 50 cm lleva 8 varillas", 8, parrilla.Count);
Cerca("las de un sentido miden el ancho del tablero", 2,
      parrilla.Where(b => Math.Abs(b.X1 - b.X2) < 1e-9).Max(b => b.Largo), 1e-9);
Cerca("y las del otro, el largo", 3,
      parrilla.Where(b => Math.Abs(b.Y1 - b.Y2) < 1e-9).Max(b => b.Largo), 1e-9);

Igual("en una sola direccion va la mitad", 3,
      LosaEnPlanta.Parrilla(chico, 0.5, dosDirecciones: false, minTramo: 0.05).Count);
Igual("y el tope de MALLA_MAX_LINEAS se respeta", 2 * 2,
      LosaEnPlanta.Parrilla(chico, 0.5, maxLineas: 2, minTramo: 0.05).Count);

// LA REGLA SEMIABIERTA: un vertice que cae JUSTO en la linea de la parrilla cuenta UNA
// vez. Sin eso, las parejas se descuadran a partir de ese vertice y media parrilla sale
// fuera de la losa. Se prueba con una L, que es donde se nota.
var ele = new List<(double X, double Y)>
{
    (0, 0), (4, 0), (4, 2), (2, 2), (2, 4), (0, 4)
};

// x = 2 es justo la vertical del quiebre. La regla semiabierta cuenta el vertice UNA
// vez, asi que ahi la losa va de y = 0 a y = 2 -la pierna de arriba empieza a la
// izquierda de esa linea-. Es el comportamiento que se quiere: si el vertice contara dos
// veces, las parejas se descuadrarian y la varilla saldria hasta y = 4 por un sitio donde
// no hay losa.
var enLaEsquina = LosaEnPlanta.Cortes(ele, 2, true);
Igual("en la vertical del quiebre, UN tramo y no dos", 1, enLaEsquina.Count);
Cerca("que arranca en 0", 0, enLaEsquina[0].A, 1e-9);
Cerca("y llega al quiebre, no mas alla", 2, enLaEsquina[0].B, 1e-9);

// Y a la IZQUIERDA del quiebre la losa llega hasta arriba, a y = 4.
var antesDelQuiebre = LosaEnPlanta.Cortes(ele, 1, true);
Igual("a la izquierda del quiebre, un tramo", 1, antesDelQuiebre.Count);
Cerca("que llega hasta arriba", 4, antesDelQuiebre[0].B, 1e-9);

// A la derecha del quiebre la losa solo llega a y = 2.
var pasadoElQuiebre = LosaEnPlanta.Cortes(ele, 3, true);
Igual("pasado el quiebre, tambien un tramo", 1, pasadoElQuiebre.Count);
Cerca("pero solo hasta 2", 2, pasadoElQuiebre[0].B, 1e-9);

// Todas las varillas de la L caen DENTRO de la losa: ninguna se sale de y > 2 a la
// derecha de x = 2.
var enL = LosaEnPlanta.Parrilla(ele, 0.5, minTramo: 0.05);
Check("ninguna varilla de la L se sale del pano",
      enL.All(b => b.X1 <= 2.0001 || Math.Max(b.Y1, b.Y2) <= 2.0001));

Console.WriteLine();
Console.WriteLine(" La bayoneta, y el volado que se reconoce por su NOTA");

// EL ARMADO DEL TABLERO: bayoneta, dos bastones con su rayita, y la corrida. Son las
// medidas de la macro: varilla de 1.57 cm, bayoneta separada 1.57, bastones a 2.87 y
// corrida a 3.44. Por direccion salen SEIS trazos -bayoneta, 2 bastones, 2 rayitas y
// corrida-, o sea 12 en las dos direcciones.
var armado = LosaEnPlanta.ArmadoDeTablero(0, 0, 4, 3);

Igual("el armado de un tablero lleva 12 trazos en las dos direcciones", 12, armado.Count);
Igual("y 6 si va en una sola direccion", 6,
      LosaEnPlanta.ArmadoDeTablero(0, 0, 4, 3, dosDirecciones: false).Count);

// LA BAYONETA es el primero: seis vertices, de lado a lado del tablero.
var bay = armado[0];
Igual("la bayoneta tiene SEIS vertices, como en la macro", 6, bay.Puntos.Count);
Check("y va en doble linea", bay.Doble);
Cerca("arranca en el borde del tablero", 0, bay.Puntos[0].X, 1e-12);
Cerca("y termina en el otro", 4, bay.Puntos[5].X, 1e-12);

// Sale arriba del centro, baja al medio del claro y vuelve a subir: el salto es 2 x 1.57 cm
// y los quiebres van a 45 grados, o sea que avanzan lo mismo a lo largo que de lado.
Cerca("sale por encima del centro del tablero", 1.5 + 0.0157, bay.Puntos[0].Y, 1e-12);
Cerca("baja al centro del claro", 1.5 - 0.0157, bay.Puntos[2].Y, 1e-12);
Cerca("el quiebre es a 45 grados", 2 * 0.0157,
      bay.Puntos[2].X - bay.Puntos[1].X, 1e-12);
Cerca("el primer quiebre cae a un cuarto del claro", 1.0,
      bay.Puntos[2].X, 1e-12);

// LOS BASTONES: de L/4 desde cada apoyo, y por encima de la bayoneta.
Cerca("el baston arranca en el apoyo", 0, armado[1].Puntos[0].X, 1e-12);
Cerca("y mide un cuarto del claro", 1.0, armado[1].Puntos[1].X, 1e-12);
Check("el baston va en doble linea", armado[1].Doble);
Check("y su rayita de la punta, en linea sencilla", !armado[3].Doble);

// LA CORRIDA: de lado a lado, por debajo del centro.
var corrida = armado[5];
Cerca("la corrida va de lado a lado", 0, corrida.Puntos[0].X, 1e-12);
Cerca("hasta el otro extremo", 4, corrida.Puntos[1].X, 1e-12);
Check("la corrida queda por debajo de la bayoneta",
      corrida.Puntos[0].Y < bay.Puntos[2].Y);

Cerca("y la doble linea se separa medio diametro", 0.0157 / 2,
      LosaEnPlanta.MedioDiametroDeVarilla(), 1e-12);

Console.WriteLine();
Console.WriteLine(" La losacero: franjas en el sentido corto y el calibre de las notas");

// Un tablero de 6 x 3: la franja va en el sentido CORTO -la Y-, y se repiten a lo largo
// de la X cada 80 cm.
var deck = new List<(double X, double Y)> { (0, 0), (6, 0), (6, 3), (0, 3) };
var franjas = LosaEnPlanta.Franjas(deck);

Check("caben varias franjas", franjas.Count >= 6);
Check("y todas corren en el sentido corto",
      franjas.All(f => Math.Abs(f.X1 - f.X2) < 1e-9));
Cerca("cada franja cruza el claro corto", 3, franjas[0].Largo, 1e-9);

// El calibre: el numero que sigue a CAL, y si no hay, el ULTIMO numero.
Igual("LOSACERO CAL 24 da 24", "24", LosaEnPlanta.Calibre("LOSACERO CAL 24"));
Igual("CALIBRE 22 tambien", "22", LosaEnPlanta.Calibre("Losacero calibre 22"));
Igual("y si no dice CAL, el ultimo numero", "25", LosaEnPlanta.Calibre("DECK 25"));
// OJO, y es el comportamiento REAL de la macro: al normalizar se quitan los espacios, asi
// que dos numeros seguidos se pegan. «DECK 4 25» da 425, no 25. Se deja escrito.
Igual("dos numeros con espacio se pegan, como en la macro", "425",
      LosaEnPlanta.Calibre("DECK 4 25"));
Igual("sin numeros, vacio", "", LosaEnPlanta.Calibre("LOSACERO IMSA"));

Check("una losa que dice DECK es losacero",
      LosaEnPlanta.DiceLosacero("L1", "DECK 4", "", "LOSACERO,DECK"));
Check("y una de concreto no lo es",
      !LosaEnPlanta.DiceLosacero("L2", "LOSA AZOTEA", "SLAB10", "LOSACERO,DECK"));

// EL VOLADO, POR SU NOTA. Es lo que se pidio: el ANSI37 solo donde la nota diga VOLADO.
const string palabrasVolado = "VOLADO,VOLADIZO,VOLADA,CANTILEVER";

Check("una losa cuya NOTA dice VOLADO es volado",
      LosaEnPlanta.DiceVolado("LOSA EN VOLADO", "L10", palabrasVolado));
Check("y tambien si lo dice su seccion",
      LosaEnPlanta.DiceVolado("", "LOSA VOLADIZO 10", palabrasVolado));
Check("no importan las mayusculas",
      LosaEnPlanta.DiceVolado("losa volada de acceso", "", palabrasVolado));
Check("una losa de azotea normal NO es volado",
      !LosaEnPlanta.DiceVolado("LOSA DE AZOTEA", "LOSA 10", palabrasVolado));
Check("y sin nota ni seccion, tampoco",
      !LosaEnPlanta.DiceVolado(null, null, palabrasVolado));

Console.WriteLine();
Console.WriteLine(" El muro que va debajo de una cadena no se dibuja");

// Un muro de 5 m con su cadena de cerramiento encima, de castillo a castillo: en el modelo
// los dos ocupan la MISMA linea en planta, asi que dibujando ambos salen dos parejas de
// lineas pegadas. Eso es lo que se ve como una raya de mas a cada lado de la cadena.
var muroConCadena = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, X1 = 0, Y1 = 0, X2 = 5, Y2 = 0, AnchoM = 0.15
};

static ElementoPlanta Dala(double x1, double y1, double x2, double y2, double b = 0.25) =>
    new()
    {
        Clase = ClasePlanta.Trabe, Tipo = "DALA", Seccion = "CC15X25",
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, AnchoM = b
    };

var conCadena = new List<ElementoPlanta> { muroConCadena, Dala(0, 0, 5, 0) };
var tapado = MuroBajoCadena.Como(muroConCadena, conCadena);

Check("el muro con su cadena encima queda TAPADO", tapado.Tapado);
Cerca("cubierto al 100 %", 1.0, tapado.Cobertura, 1e-9);
Cerca("y se recuerda el ancho de la cadena, para separar el pier", 0.25,
      tapado.AnchoCadena, 1e-12);

// EL MURO SIN CADENA SI SE DIBUJA: es el que hay que revisar.
var muroSolo = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, X1 = 0, Y1 = 3, X2 = 5, Y2 = 3, AnchoM = 0.15
};
Check("un muro SIN cadena no esta tapado",
      !MuroBajoCadena.Como(muroSolo, new List<ElementoPlanta> { muroSolo, Dala(0, 0, 5, 0) })
        .Tapado);

// Media cadena no tapa: con TRASLAPE_MINIMO = 0.8 hace falta el 80 % del largo.
var mitad = MuroBajoCadena.Como(
    muroConCadena, new List<ElementoPlanta> { muroConCadena, Dala(0, 0, 2.5, 0) });
Cerca("media cadena cubre la mitad", 0.5, mitad.Cobertura, 1e-9);
Check("y con la mitad NO se da por tapado", !mitad.Tapado);

// LA UNION Y NO LA SUMA: dos cadenas traslapadas cubren su tramo una sola vez.
var dosCadenas = MuroBajoCadena.Como(
    muroConCadena,
    new List<ElementoPlanta> { muroConCadena, Dala(0, 0, 3, 0), Dala(2, 0, 5, 0) });
Cerca("dos cadenas traslapadas cubren el muro una sola vez", 1.0,
      dosCadenas.Cobertura, 1e-9);

// Una cadena PERPENDICULAR no tapa nada, y una TRABE tampoco -salvo que se pida-.
Check("una cadena perpendicular no tapa el muro",
      !MuroBajoCadena.Como(
          muroConCadena,
          new List<ElementoPlanta> { muroConCadena, Dala(2.5, -2, 2.5, 2) }).Tapado);

var trabe = new ElementoPlanta
{
    Clase = ClasePlanta.Trabe, Tipo = "TRABE", X1 = 0, Y1 = 0, X2 = 5, Y2 = 0, AnchoM = 0.30
};
Check("una TRABE no tapa el muro por omision",
      !MuroBajoCadena.Como(muroConCadena, new List<ElementoPlanta> { muroConCadena, trabe })
        .Tapado);
Check("pero si CADENA_INCLUYE_TRABES esta en SI, si",
      MuroBajoCadena.Como(
          muroConCadena, new List<ElementoPlanta> { muroConCadena, trabe },
          incluirTrabes: true).Tapado);

Console.WriteLine();
Console.WriteLine(" El nombre de la losa: el de la seccion, sin la palabra LOSA");

Igual("«LOSA VOLADO» se rotula VOLADO", "VOLADO",
      PlantaDrawer.SinLaPalabraLosa("LOSA VOLADO"));
Igual("«Losa de AZOTEA» se rotula AZOTEA", "AZOTEA",
      PlantaDrawer.SinLaPalabraLosa("Losa de AZOTEA"));
Igual("«LOSA ENTREPISO» se rotula ENTREPISO", "ENTREPISO",
      PlantaDrawer.SinLaPalabraLosa("LOSA ENTREPISO"));
Igual("y se conserva lo que diga, sea lo que sea", "MARQUESINA",
      PlantaDrawer.SinLaPalabraLosa("Losa Marquesina"));
// Si de la seccion no queda nada aprovechable, manda la lista de palabras de la hoja.
Igual("«LOSA» a secas no dice nada", "", PlantaDrawer.SinLaPalabraLosa("LOSA"));
Igual("ni «SLAB 10»", "", PlantaDrawer.SinLaPalabraLosa("SLAB 10"));
Igual("ni una seccion vacia", "", PlantaDrawer.SinLaPalabraLosa(null));

Console.WriteLine();
Console.WriteLine(" El contorno, solo por fuera del muro o la cadena");

// Un lado de losa que corre sobre una cadena: por dentro de la cadena NO se dibuja.
var sobreLaCadena = new LosaEnPlanta.Segmento(0, 0, 5, 0);
var fueraDeTodo = LosaEnPlanta.TramosFuera(
    sobreLaCadena, new List<ElementoPlanta> { Cadena(1, 0, 4, 0) });

Igual("el lado se parte en dos: antes y despues de la cadena", 2, fueraDeTodo.Count);
Cerca("el primero llega al pano de la cadena", 1, fueraDeTodo[0].X2, 1e-9);
Cerca("y el segundo arranca en el otro pano", 4, fueraDeTodo[1].X1, 1e-9);

// Un lado que va ENTERO por dentro del muro no se dibuja: ahi la losa apoya, y una linea
// en medio del muro se lee como una junta que no existe.
Igual("un lado entero dentro del muro no se dibuja", 0,
      LosaEnPlanta.TramosFuera(
          new LosaEnPlanta.Segmento(1, 0, 4, 0),
          new List<ElementoPlanta> { Cadena(0, 0, 5, 0) }).Count);

Igual("y sin muros, el lado va completo", 1,
      LosaEnPlanta.TramosFuera(sobreLaCadena, new List<ElementoPlanta>()).Count);

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
// A -0.50 DE LOS EJES, la de la hoja de la macro. Lo que estaba mal no era la distancia, sino
// DESDE DONDE se medía: desde la caja de los elementos y no desde los ejes, y por eso los
// rotulos de un juego de plantas salian escalonados.
Cerca("y a medio metro de los ejes", 0.5, rot.SeparacionEjes);

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
Console.WriteLine(" EL CASTILLO MODELADO COMO SHELL DE MURO");
Console.WriteLine("=====================================================================");

// SE PIDIO: «los shells de muro que tengan en property note CASTILLO igual hacerlos bloques
// y rellenarlos con amarillo como un frame normal, OJO solo si dice CASTILLO». Un castillo se
// puede modelar como frame de 15x15 o como shell angosto -lo que sale al dibujarlo junto con
// su muro- y dibujado como muro salia como dos rayas, sin bloque y sin relleno.
var shellCastillo = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Tipo = "CASTILLO", Notas = "CASTILLO AHOGADO EN MURO",
    Etiqueta = "45", Seccion = "MURO 15", X1 = 12, Y1 = 0, X2 = 12, Y2 = 0.15,
    AnchoM = 0.15, Z1 = 0, Z2 = 2.5
};

Check("un shell de muro con CASTILLO en sus notas es un castillo",
      CastilloDeMuro.Dice(shellCastillo));
Check("y basta el TIPO que ya clasifico la ventana",
      CastilloDeMuro.Dice(new ElementoPlanta { Clase = ClasePlanta.Muro, Tipo = "CASTILLO" }));

// OJO SOLO SI DICE CASTILLO: un muro cualquiera sigue siendo un muro.
Check("un muro normal NO se convierte",
      !CastilloDeMuro.Dice(new ElementoPlanta
      {
          Clase = ClasePlanta.Muro, Tipo = "MURO", Notas = "MURO DE BLOCK 15", AnchoM = 0.15
      }));
// El NOMBRE DE LA SECCION no cuenta: se pidio la property note, y una propiedad de muro
// llamada «MURO CON CASTILLOS 15» es un muro, no un castillo.
Check("el nombre de la seccion no convierte a nadie",
      !CastilloDeMuro.Dice(new ElementoPlanta
      {
          Clase = ClasePlanta.Muro, Tipo = "MURO", Seccion = "MURO CON CASTILLOS 15"
      }));
// Y solo los MUROS: una losa o una trabe con esa nota se quedan como estan.
Check("una losa con CASTILLO en sus notas no es un castillo",
      !CastilloDeMuro.Dice(new ElementoPlanta
      {
          Clase = ClasePlanta.Losa, Notas = "CASTILLO", Tipo = "LOSA"
      }));

// LA CONVERSION: el segmento con espesor se vuelve una SECCION EN UN PUNTO, girada en la
// direccion del shell. Es la misma cuenta de PanoDeApoyo.Huella, la que ya mide panos.
var comoCastillo = CastilloDeMuro.Como(shellCastillo, 0.15);

Igual("se dibuja como COLUMNA, que es el camino del bloque y el relleno",
      ClasePlanta.Columna, comoCastillo.Clase);
Igual("con el tipo CASTILLO, que es lo que lo manda a la capa E-CASTILLO",
      "CASTILLO", comoCastillo.Tipo);
Igual("y forma RECT: la del shell es AREA, que no describe ninguna seccion",
      "RECT", comoCastillo.Forma);
Cerca("su centro es el punto medio del shell, en X", 12, comoCastillo.X1);
Cerca("y en Y", 0.075, comoCastillo.Y1);
Cerca("su ancho es el LARGO del shell", 0.15, comoCastillo.AnchoM);
Cerca("su peralte es el ESPESOR del muro", 0.15, comoCastillo.PeralteM);
Cerca("y su giro, la direccion del shell", 90, comoCastillo.AnguloGrados);
// SU NOMBRE ES SU MEDIDA. La seccion de un shell es la propiedad del MURO -«MURO 15», que no
// dice nada de este castillo- y su etiqueta es el PIER, que en SAP2000 NO EXISTE: el castillo
// salia sin rotulo y con el nombre de un muro. Se nombra con lo que lo describe: el espesor por
// el largo, en centimetros.
Igual("se rotula con su medida en planta", "K 15X15", comoCastillo.Etiqueta);
Igual("y la seccion dice lo mismo, que es de donde sale el nombre del BLOQUE",
      "K 15X15", comoCastillo.Seccion);
Cerca("y las cotas, que es lo que el corte necesita", 2.5, comoCastillo.Z2);

// Sin espesor en el modelo, el mismo respaldo que usa el dibujante para un muro.
Cerca("sin espesor cae en el respaldo de 15 cm", 0.15,
      CastilloDeMuro.Como(
          new ElementoPlanta
          {
              Clase = ClasePlanta.Muro, Tipo = "CASTILLO", X1 = 0, Y1 = 0, X2 = 0, Y2 = 0.20
          },
          0.15).PeralteM);

// Un shell de un solo punto no tiene direccion ni largo: sale cuadrado, en lugar de no
// dibujarse.
Cerca("un shell degenerado sale cuadrado del espesor", 0.15,
      CastilloDeMuro.Como(
          new ElementoPlanta
          {
              Clase = ClasePlanta.Muro, Tipo = "CASTILLO", AnchoM = 0.15
          },
          0.15).AnchoM);

// NORMALIZAR: cambia en la lista los que dicen CASTILLO y deja el resto igual.
var mezcla = new List<ElementoPlanta>
{
    shellCastillo,
    new()
    {
        Clase = ClasePlanta.Muro, Tipo = "MURO", Notas = "MURO DE BLOCK",
        X1 = 0, Y1 = 0, X2 = 4, Y2 = 0, AnchoM = 0.15
    }
};

Igual("de dos shells se convierte UNO", 1, CastilloDeMuro.Normalizar(mezcla, 0.15));
Igual("el castillo queda como columna", ClasePlanta.Columna, mezcla[0].Clase);
Igual("y el muro sigue siendo muro", ClasePlanta.Muro, mezcla[1].Clase);
// IDEMPOTENTE: dibujar dos veces la misma planta no vuelve a convertir ni desplaza nada.
Igual("a la segunda pasada ya no queda nada por convertir", 0,
      CastilloDeMuro.Normalizar(mezcla, 0.15));
// La lista que llega al metodo Como NO se toca: el visor y la tabla siguen viendo el shell.
Igual("el elemento original se queda como muro", ClasePlanta.Muro, shellCastillo.Clase);

// Y AHORA CUENTA COMO APOYO: es lo que hace que los muros mueran en su paño y que el eje de
// orilla se corra a su paño, igual que con un castillo de frame.
Cerca("un castillo de shell tambien corre el eje de orilla a su paño", 0.075,
      ejes.MedioAnchoSobreEje(12, vertical: true, new List<ElementoPlanta> { mezcla[0] }));

// LOS DECIMALES, CUANDO HACEN FALTA: un castillo de 23.5 cm es «K 15X23.5», no «K 15X24». Y con
// PUNTO decimal siempre, porque este texto acaba siendo un nombre de bloque de AutoCAD: con la
// coma de la configuracion regional, la misma medida daria DOS bloques distintos.
Igual("un castillo de 23.5 cm se nombra con su decimal", "K 15X23.5",
      CastilloDeMuro.Nombre("K", 0.15, 0.235));
Igual("y uno redondo no lleva ceros de relleno", "K 15X20",
      CastilloDeMuro.Nombre("K", 0.15, 0.20));
Igual("el prefijo se recorta y lleva UN espacio", "K 15X20",
      CastilloDeMuro.Nombre("K ", 0.15, 0.20));
Igual("y sin prefijo queda la medida sola", "15X20",
      CastilloDeMuro.Nombre("", 0.15, 0.20));
// El prefijo sale de la hoja: quien nombre sus castillos «C» los tendra como «C 15X20».
Igual("y se puede cambiar en la hoja CONFIG", "C 15X20",
      CastilloDeMuro.Nombre("C", 0.15, 0.20));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" Y COMPLETO: LOS PEDAZOS DEL MISMO CASTILLO, UNO SOLO");
Console.WriteLine("=====================================================================");

// SE PIDIO: «cuando un shell diga castillo en property notes debes ponerlo COMPLETO como
// bloque». Un castillo de shell casi nunca llega de una pieza, y se parte de dos maneras:
//
//  1) A LO ALTO -lo mas comun-: el modelador lo dibuja en dos paneles, uno hasta el antepecho
//     y otro del dintel arriba. En planta los dos ocupan EXACTAMENTE el mismo sitio, asi que
//     salian DOS BLOQUES ENCIMADOS y, en el corte, dos castillos cortos en vez de uno de piso
//     a techo.
//  2) A LO LARGO: dos paneles seguidos sobre la misma linea, y el castillo salia en dos
//     mitades.
ElementoPlanta PanelCastillo(double x1, double y1, double x2, double y2,
                     double z1, double z2, double esp = 0.15, string etiqueta = "") =>
    new()
    {
        Clase = ClasePlanta.Muro, Tipo = "CASTILLO", Notas = "CASTILLO",
        Seccion = "MURO 15", Etiqueta = etiqueta,
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Z1 = z1, Z2 = z2, AnchoM = esp
    };

// 1) PARTIDO A LO ALTO: mismo sitio en planta, uno de 0 a 1.0 y otro de 1.0 a 2.5.
var castilloAlto = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.15, 0, 1.0),
    PanelCastillo(12, 0, 12, 0.15, 1.0, 2.5, etiqueta: "C7")
};

Igual("dos paneles en el mismo sitio son UN castillo", 1,
      CastilloDeMuro.Normalizar(castilloAlto, 0.15));
Igual("y en la lista queda uno solo, no dos bloques encimados", 1, castilloAlto.Count);
Cerca("con el largo del castillo", 0.15, castilloAlto[0].AnchoM);
Cerca("y de cota va del mas bajo...", 0, castilloAlto[0].Z1);
Cerca("...al mas alto, para que el corte lo saque de una pieza", 2.5, castilloAlto[0].Z2);
// Y el castillo unido tambien se rotula con SU medida, la de los dos juntos.
Igual("el castillo unido se rotula con su medida", "K 15X15", castilloAlto[0].Etiqueta);

// 2) PARTIDO A LO LARGO: dos paneles seguidos de 30 cm dan un castillo de 60.
var castilloLargo = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.30, 0, 2.5),
    PanelCastillo(12, 0.30, 12, 0.60, 0, 2.5)
};

Igual("dos paneles seguidos son UN castillo", 1,
      CastilloDeMuro.Normalizar(castilloLargo, 0.15));
Cerca("con el largo de los dos juntos", 0.60, castilloLargo[0].AnchoM);
Cerca("y su centro a la mitad de todo", 0.30, castilloLargo[0].Y1);

// EL ESPESOR, EL MAYOR: el paño tiene que llegar al mas saliente.
var castilloDosEspesores = new List<ElementoPlanta>
{
    PanelCastillo(0, 0, 0, 0.15, 0, 1.0, esp: 0.15),
    PanelCastillo(0, 0, 0, 0.15, 1.0, 2.5, esp: 0.20)
};
CastilloDeMuro.Normalizar(castilloDosEspesores, 0.15);
Cerca("de dos espesores manda el mayor", 0.20, castilloDosEspesores[0].PeralteM);

// LO QUE NO SE UNE. Dos castillos DISTINTOS siguen siendo dos: uno en cada esquina de un vano
// de metro y medio, y dos castillosParalelos a los dos lados de un muro.
var castillosLejanos = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.15, 0, 2.5),
    PanelCastillo(12, 1.5, 12, 1.65, 0, 2.5)
};
Igual("dos castillos separados metro y medio son DOS", 2,
      CastilloDeMuro.Normalizar(castillosLejanos, 0.15));
Igual("y los dos siguen en la lista", 2, castillosLejanos.Count);

var castillosParalelos = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.15, 0, 2.5),
    PanelCastillo(12.15, 0, 12.15, 0.15, 0, 2.5)
};
Igual("dos castillos paralelos a 15 cm tampoco se unen", 2,
      CastilloDeMuro.Normalizar(castillosParalelos, 0.15));

// Y en cruz: uno en X y otro en Y que se cruzan no son el mismo castillo, porque no van en la
// misma direccion.
var castillosEnCruz = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.15, 0, 2.5),
    PanelCastillo(11.95, 0.075, 12.10, 0.075, 0, 2.5)
};
Igual("uno en X y otro en Y no son el mismo castillo", 2,
      CastilloDeMuro.Normalizar(castillosEnCruz, 0.15));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL CASTILLO DE AREA, HASTA EL PANO DEL MURO QUE SE CRUZA");
Console.WriteLine("=====================================================================");

// SE PIDIO: «cuando sea area un castillo debes sumarle la mitad del espesor de la seccion en el
// lado donde se intersecta con otro muro modelado, para que llegue al pano y no se corte antes».
// En el modelo LOS MUROS SE DIBUJAN POR SU EJE, asi que el shell del castillo se traza hasta la
// LINEA del muro con el que se topa: en el plano el castillo se quedaba a media pared -el pano
// del muro seguia mas alla de el- y parecia cortado.
//
// El castillo va en Y, de 0 a 0.235, y en su punta de abajo lo cruza un muro de 15 que corre en
// X con su eje en y = 0: sus dos caras estan en -0.075 y +0.075.
var castilloAlPano = PanelCastillo(12, 0, 12, 0.235, 0, 2.5);

var muroQueCruza = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Tipo = "MURO", Notas = "MURO DE BLOCK",
    X1 = 12, Y1 = 0, X2 = 15, Y2 = 0, AnchoM = 0.15
};

var estirado = CastilloDeMuro.AlPanoDeLosMuros(
    castilloAlPano, new List<ElementoPlanta> { muroQueCruza }, 0.15, 0.25);

Cerca("el castillo baja hasta la cara de mas alla del muro", -0.075, estirado.Y1);
Cerca("y por arriba no se toca, que ahi no hay nada", 0.235, estirado.Y2);

// SI YA LLEGABA AL PANO no se alarga: sumar medio espesor a ciegas lo pasaria de largo.
var yaLlegaba = PanelCastillo(12, -0.075, 12, 0.235, 0, 2.5);
Cerca("el que ya llegaba al pano se queda como esta", -0.075,
      CastilloDeMuro.AlPanoDeLosMuros(
          yaLlegaba, new List<ElementoPlanta> { muroQueCruza }, 0.15, 0.25).Y1);

// UN MURO QUE CORRE EN LA MISMA DIRECCION no cuenta: no se cruzan en un punto, se acompañan, y
// ahi no hay pano que alcanzar.
var muroALoLargo = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Tipo = "MURO", X1 = 12, Y1 = 0, X2 = 12, Y2 = 3, AnchoM = 0.15
};
Cerca("un muro en la misma direccion no lo alarga", 0,
      CastilloDeMuro.AlPanoDeLosMuros(
          castilloAlPano, new List<ElementoPlanta> { muroALoLargo }, 0.15, 0.25).Y1);

// UN MURO QUE CRUZA POR EL MEDIO tampoco: el castillo no tiene que llegar a ningun sitio.
var muroPorElMedio = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Tipo = "MURO", X1 = 11, Y1 = 1.5, X2 = 15, Y2 = 1.5, AnchoM = 0.15
};
Cerca("un muro que cruza lejos de las puntas no lo alarga", 0,
      CastilloDeMuro.AlPanoDeLosMuros(
          PanelCastillo(12, 0, 12, 3, 0, 2.5),
          new List<ElementoPlanta> { muroPorElMedio }, 0.15, 0.25).Y1);

// UN MURO QUE NO LLEGA HASTA EL CASTILLO tampoco: su eje se cruzaria con el del castillo en su
// prolongacion, pero ahi no hay muro.
var muroQueNoLlega = new ElementoPlanta
{
    Clase = ClasePlanta.Muro, Tipo = "MURO", X1 = 14, Y1 = 0, X2 = 15, Y2 = 0, AnchoM = 0.15
};
Cerca("un muro que se queda a dos metros no lo alarga", 0,
      CastilloDeMuro.AlPanoDeLosMuros(
          castilloAlPano, new List<ElementoPlanta> { muroQueNoLlega }, 0.15, 0.25).Y1);

// Y OTRO CASTILLO DE AREA tampoco: entre dos castillos no hay pano que alcanzar.
Cerca("otro castillo de area no lo alarga", 0,
      CastilloDeMuro.AlPanoDeLosMuros(
          castilloAlPano,
          new List<ElementoPlanta> { PanelCastillo(11.8, 0, 12.2, 0, 0, 2.5) },
          0.15, 0.25).Y1);

// DE PUNTA A PUNTA: un castillo que muere en un muro por cada lado se alarga por los dos.
var entreDosMuros = new List<ElementoPlanta>
{
    muroQueCruza,
    new()
    {
        Clase = ClasePlanta.Muro, Tipo = "MURO",
        X1 = 12, Y1 = 0.235, X2 = 15, Y2 = 0.235, AnchoM = 0.20
    }
};
var porLosDos = CastilloDeMuro.AlPanoDeLosMuros(castilloAlPano, entreDosMuros, 0.15, 0.25);
Cerca("por abajo, medio muro de 15", -0.075, porLosDos.Y1);
Cerca("y por arriba, medio muro de 20", 0.235 + 0.10, porLosDos.Y2);

// Y TODO JUNTO: al normalizar, el castillo sale con la medida ESTIRADA, y su nombre dice esa
// medida -la que se dibuja- porque el nombre del bloque tiene que describir al bloque.
var conPano = new List<ElementoPlanta> { PanelCastillo(12, 0, 12, 0.235, 0, 2.5), muroQueCruza };
CastilloDeMuro.Normalizar(conPano, 0.15, 0.02, "K", 0.25);

Cerca("el castillo normalizado mide lo estirado", 0.31, conPano[0].AnchoM);
Igual("y su nombre lo dice, que es lo que se dibuja", "K 15X31", conPano[0].Seccion);

// Con la holgura en 0 -SHELL_CASTILLO_AL_PANO en NO- no se alarga nada.
var sinPanoLista = new List<ElementoPlanta>
{
    PanelCastillo(12, 0, 12, 0.235, 0, 2.5), muroQueCruza
};
CastilloDeMuro.Normalizar(sinPanoLista, 0.15, 0.02, "K", 0);
Cerca("y apagado se queda con su medida de modelo", 0.235, sinPanoLista[0].AnchoM);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" DE VARIAS CADENAS EN LA MISMA LINEA, SOLO LA MAS ALTA");
Console.WriteLine("=====================================================================");

// SE PIDIO: «si hay cadena intermedia abajo no lo muestres en planta, en planta solo muestra la
// cadena mas alta que exista, solo dibuja una». Un muro de mamposteria lleva TRES cadenas sobre
// el mismo paño -desplante abajo, intermedia a media altura y cerramiento arriba-: las tres son
// del mismo nivel y las tres ocupan LA MISMA LINEA en planta, asi que se dibujaban las tres, una
// encima de la otra, con tres rotulos pisandose. Y en una planta no hay forma de distinguirlas,
// porque una planta no tiene alturas.
ElementoPlanta CadenaEnLinea(string tipo, double z, double x1 = 0, double x2 = 4,
                      double y = 0, double ancho = 0.15) =>
    new()
    {
        Clase = ClasePlanta.Trabe, Tipo = tipo, Seccion = "CC 15X25",
        X1 = x1, Y1 = y, X2 = x2, Y2 = y, Z1 = z, Z2 = z, AnchoM = ancho, PeralteM = 0.25
    };

var lasTres = new List<ElementoPlanta>
{
    CadenaEnLinea("CADENA DE DESPLANTE", 0),
    CadenaEnLinea("CADENA INTERMEDIA", 1.2),
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5)
};

var tapadas = CadenaMasAlta.Tapadas(lasTres, 0.10);

Igual("de las tres cadenas del muro se callan dos", 2, tapadas.Count);
Check("la de desplante no se dibuja", tapadas.Contains(lasTres[0]));
Check("la intermedia tampoco", tapadas.Contains(lasTres[1]));
Check("y la de cerramiento, que es la mas alta, si", !tapadas.Contains(lasTres[2]));

// DOS TRAMOS SEGUIDOS del mismo paño NO se tapan: son dos cadenas distintas -una de castillo a
// castillo y la siguiente de ahi al final- y las dos se dibujan.
var dosTramos = new List<ElementoPlanta>
{
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5, x1: 0, x2: 4),
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5, x1: 4, x2: 8)
};
Igual("dos tramos seguidos se dibujan los dos", 0, CadenaMasAlta.Tapadas(dosTramos, 0.10).Count);

// DOS CADENAS EN PAREDES DISTINTAS tampoco: estan a metros una de otra.
var dosParedes = new List<ElementoPlanta>
{
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5, y: 0),
    CadenaEnLinea("CADENA INTERMEDIA", 1.2, y: 3)
};
Igual("cadenas de paredes distintas no se tapan", 0,
      CadenaMasAlta.Tapadas(dosParedes, 0.10).Count);

// Y UNA CADENA QUE CRUZA no tapa a la que corre: no van en la misma direccion.
var enCruzCadenas = new List<ElementoPlanta>
{
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5, x1: 0, x2: 4, y: 0),
    new()
    {
        Clase = ClasePlanta.Trabe, Tipo = "CADENA INTERMEDIA", Seccion = "CC 15X25",
        X1 = 2, Y1 = -2, X2 = 2, Y2 = 2, Z1 = 1.2, Z2 = 1.2, AnchoM = 0.15, PeralteM = 0.25
    }
};
Igual("una cadena que cruza no tapa a la que corre", 0,
      CadenaMasAlta.Tapadas(enCruzCadenas, 0.10).Count);

// LAS TRABES NO ENTRAN, y es a proposito: dos trabes a distinta altura sobre la misma linea son
// dos vigas de verdad -una de entrepiso y una de azotea- y callar una seria esconder estructura.
Check("una trabe no es una cadena", !CadenaMasAlta.EsCadena(CadenaEnLinea("TRABE", 2.5)));
Check("una dala si", CadenaMasAlta.EsCadena(CadenaEnLinea("DALA", 2.5)));
Check("y las tres cadenas tambien",
      CadenaMasAlta.EsCadena(CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5))
      && CadenaMasAlta.EsCadena(CadenaEnLinea("CADENA INTERMEDIA", 1.2))
      && CadenaMasAlta.EsCadena(CadenaEnLinea("CADENA DE DESPLANTE", 0)));

var dosTrabes = new List<ElementoPlanta> { CadenaEnLinea("TRABE", 2.5), CadenaEnLinea("TRABE", 5) };
Igual("dos trabes encimadas se dibujan las dos", 0,
      CadenaMasAlta.Tapadas(dosTrabes, 0.10).Count);

// A LA MISMA ALTURA se queda la PRIMERA, siempre la misma: si dependiera del orden de recorrido,
// dibujar dos veces la planta podria callar una distinta y el plano cambiaria sin motivo.
var mismaAltura = new List<ElementoPlanta>
{
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5),
    CadenaEnLinea("CADENA DE CERRAMIENTO", 2.5)
};
var tapadasIguales = CadenaMasAlta.Tapadas(mismaAltura, 0.10);
Igual("de dos cadenas iguales encimadas se calla una", 1, tapadasIguales.Count);
Check("y la que se queda es la primera", tapadasIguales.Contains(mismaAltura[1]));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL NOMBRE DE LA CADENA NO VA ENCIMA DE UN CASTILLO DE AREA");
Console.WriteLine("=====================================================================");

// SE PIDIO: «cuando tenga un area sea castillo, no coloques el nombre de la cadena». La cadena
// que muere en un castillo de area es corta -a veces mide lo que el castillo- y su rotulo va al
// CENTRO de la barra: ese centro cae DENTRO del castillo, asi que el nombre de la cadena acababa
// escrito sobre el amarillo y encima del rotulo del propio castillo.
var castilloDeArea = new List<ElementoPlanta> { PanelCastillo(5, 0, 5, 0.20, 0, 2.5) };
CastilloDeMuro.Normalizar(castilloDeArea, 0.15);

Check("el castillo de area queda marcado como tal", castilloDeArea[0].DeShell);
Check("y un punto en su centro cae dentro",
      CastilloDeMuro.HayCastilloDeAreaEn(5, 0.10, castilloDeArea));
Check("uno a medio metro, no",
      !CastilloDeMuro.HayCastilloDeAreaEn(5.5, 0.10, castilloDeArea));

// UN CASTILLO DE FRAME NO CUENTA: ahi el nombre de la cadena nunca ha estorbado, y callarlo
// seria quitar un dato que si se lee.
var deFrame = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, Tipo = "CASTILLO", Seccion = "K 15X15",
        X1 = 5, Y1 = 0.10, X2 = 5, Y2 = 0.10, AnchoM = 0.15, PeralteM = 0.15
    }
};
Check("un castillo de frame no calla a nadie",
      !CastilloDeMuro.HayCastilloDeAreaEn(5, 0.10, deFrame));

// LO QUE FALLABA: MIRAR EL PUNTO Y NO EL TEXTO. El rotulo de una cadena es un MTEXT CENTRADO en
// la barra, asi que el texto se extiende a los dos lados de su punto de insercion. Una cadena de
// 45 cm entre dos castillos tiene su centro ENTRE los dos -fuera de los dos- y su nombre,
// «CC 15X25», mide mas que la propia cadena: el punto no caia dentro de ningun castillo y el
// texto los tapaba igual. Eso es lo que se seguia viendo en el plano.
var dosCastillos = new List<ElementoPlanta>
{
    PanelCastillo(5, 0, 5, 0.20, 0, 2.5),
    PanelCastillo(5.6, 0, 5.6, 0.20, 0, 2.5)
};
CastilloDeMuro.Normalizar(dosCastillos, 0.15);

// El centro de la cadena que va de un castillo al otro: entre los dos, y en ninguno.
Check("el punto del rotulo NO cae en ningun castillo -por eso no bastaba-",
      !CastilloDeMuro.HayCastilloDeAreaEn(5.3, 0.10, dosCastillos));
// Pero su texto, de un metro, pasa por encima de los dos.
Check("y su texto SI pasa por encima",
      CastilloDeMuro.HayCastilloDeAreaBajoElTexto(4.8, 0.10, 5.8, 0.10, dosCastillos));

// Una cadena larga tiene su nombre lejos de los castillos: ese si se escribe.
Check("el nombre de una cadena larga no estorba",
      !CastilloDeMuro.HayCastilloDeAreaBajoElTexto(7.5, 0.10, 8.5, 0.10, dosCastillos));

// Y el texto que ACABA en el castillo tambien cuenta, no solo el que lo cruza.
Check("un texto que acaba en el castillo tambien lo tapa",
      CastilloDeMuro.HayCastilloDeAreaBajoElTexto(6.2, 0.10, 5.65, 0.10, dosCastillos));

// Se pregunta cada cinco centimetros, asi que un castillo de quince no se cuela entre dos
// preguntas: el texto de dos metros pasa por encima del que esta en medio.
Check("un castillo de 15 no se cuela entre dos preguntas",
      CastilloDeMuro.HayCastilloDeAreaBajoElTexto(4, 0.10, 6, 0.10, dosCastillos));

// LA REGLA QUE NO DEPENDE DEL TEXTO: EL CASTILLO CUBRE A LA CADENA. En el modelo la cadena llega
// PARTIDA por sus cruces, asi que el pedazo que va sobre el castillo es una cadena propia: mide lo
// que el castillo, su rotulo va al centro de ese pedazo -o sea justo en medio del castillo- y ahi
// no cabe ningun nombre. Es el caso de la imagen: «CC 15X25» escrito a lo largo del K 15X80.
var castilloDe80 = new List<ElementoPlanta> { PanelCastillo(5, 0, 5, 0.80, 0, 2.5) };
CastilloDeMuro.Normalizar(castilloDe80, 0.15);

Igual("el castillo de area mide lo que su shell", "K 15X80", castilloDe80[0].Seccion);

// La cadena que corre A LO LARGO del castillo, del mismo largo que el: cubierta.
var cadenaSobreElCastillo = new ElementoPlanta
{
    Clase = ClasePlanta.Trabe, Tipo = "CADENA DE CERRAMIENTO", Seccion = "CC 15X25",
    X1 = 5, Y1 = 0, X2 = 5, Y2 = 0.80, Z1 = 2.5, Z2 = 2.5, AnchoM = 0.15, PeralteM = 0.25
};
Check("la cadena que va sobre el castillo no se rotula",
      CastilloDeMuro.CubreALaBarra(cadenaSobreElCastillo, castilloDe80, 0.10));

// La cadena que LLEGA DE LADO y muere en el castillo: solo lo toca, y si se rotula.
var cadenaDeLado = new ElementoPlanta
{
    Clase = ClasePlanta.Trabe, Tipo = "CADENA DE CERRAMIENTO", Seccion = "CC 15X25",
    X1 = 5, Y1 = 0.40, X2 = 8, Y2 = 0.40, Z1 = 2.5, Z2 = 2.5, AnchoM = 0.15, PeralteM = 0.25
};
Check("la que llega de lado si se rotula",
      !CastilloDeMuro.CubreALaBarra(cadenaDeLado, castilloDe80, 0.10));

// Y una cadena LARGA en la misma linea tampoco se calla: el castillo cubre 80 cm de sus cuatro
// metros, y su nombre cabe de sobra fuera de el.
var cadenaDeCuatroMetros = new ElementoPlanta
{
    Clase = ClasePlanta.Trabe, Tipo = "CADENA DE CERRAMIENTO", Seccion = "CC 15X25",
    X1 = 5, Y1 = 0, X2 = 5, Y2 = 4, Z1 = 2.5, Z2 = 2.5, AnchoM = 0.15, PeralteM = 0.25
};
Check("una cadena de cuatro metros no se calla por 80 cm de castillo",
      !CastilloDeMuro.CubreALaBarra(cadenaDeCuatroMetros, castilloDe80, 0.10));

// Un castillo de FRAME no calla a nadie, tampoco con esta regla.
var castilloFrameDe80 = new List<ElementoPlanta>
{
    new()
    {
        Clase = ClasePlanta.Columna, Tipo = "CASTILLO", Seccion = "K 15X80",
        X1 = 5, Y1 = 0.40, X2 = 5, Y2 = 0.40,
        AnchoM = 0.80, PeralteM = 0.15, AnguloGrados = 90
    }
};
Check("un castillo de frame no calla a la cadena",
      !CastilloDeMuro.CubreALaBarra(cadenaSobreElCastillo, castilloFrameDe80, 0.10));

// UN ROTULO NO ES UNA RAYA, ES UNA CAJA: alto por largo, y con el fondo opaco todavia un poco
// mas. Un texto que pasa justo al lado del castillo lo tapa con media letra, y preguntando solo
// por su linea del centro se escapaba. Con el alto se detecta.
var castilloParaCaja = new List<ElementoPlanta> { PanelCastillo(5, 0, 5, 0.20, 0, 2.5) };
CastilloDeMuro.Normalizar(castilloParaCaja, 0.15);

Check("un texto que pasa al lado, sin alto, no se detecta",
      !CastilloDeMuro.HayCastilloDeAreaBajoElTexto(4.5, 0.32, 5.5, 0.32, castilloParaCaja));
Check("pero con su alto si, porque su caja lo tapa",
      CastilloDeMuro.HayCastilloDeAreaBajoElTexto(
          4.5, 0.32, 5.5, 0.32, castilloParaCaja, altoTexto: 0.25));

// Y EL GIRO CUENTA: en un castillo a 45 grados la caja recta diria que si donde no lo hay.
var giradoArea = new List<ElementoPlanta> { PanelCastillo(0, 0, 0.60, 0.60, 0, 2.5) };
CastilloDeMuro.Normalizar(giradoArea, 0.15);
Check("el centro del castillo girado cae dentro",
      CastilloDeMuro.HayCastilloDeAreaEn(0.30, 0.30, giradoArea));
Check("y su esquina recta, fuera",
      !CastilloDeMuro.HayCastilloDeAreaEn(0.60, 0.05, giradoArea));

// EL ALTO DEL AREA, QUE ES LO QUE LO HACIA DESAPARECER DEL CORTE. Las cotas de un muro salian
// de los dos vertices MAS SEPARADOS EN PLANTA, y esos pueden ser los dos de ABAJO -depende del
// orden en que ETABS devuelva las esquinas-: entonces Z1 y Z2 valian LO MISMO, el alto era
// CERO y en el corte no se dibujaba nada. Ahora el lector manda la cota mas baja y la mas alta
// del paño, y el castillo de area se ve.
var castilloEnCorte = new List<ElementoPlanta>
{
    PanelCastillo(5, 0, 5, 0.15, 0, 2.7)
};
CastilloDeMuro.Normalizar(castilloEnCorte, 0.15);

var pCastillo = CorteEnAlzado.Piezas(castilloEnCorte, enX: true, ordenada: 5, espesorM: 0.6);

Igual("el castillo de area da su pieza en el corte", 1, pCastillo.Count);
Cerca("con el alto que trae el area", 2.7, pCastillo[0].Alto);
Igual("y como columna, que es lo que se rellena", ClasePlanta.Columna, pCastillo[0].Clase);
Check("y CORTADA, que es lo que decide el relleno", pCastillo[0].Cortada);

// Y con las dos cotas iguales -lo que llegaba antes- no habia nada que dibujar. Se deja
// escrito para que se vea de dónde venia el problema.
var castilloSinAlto = new List<ElementoPlanta>
{
    PanelCastillo(5, 0, 5, 0.15, 0, 0)
};
CastilloDeMuro.Normalizar(castilloSinAlto, 0.15);

Igual("con Z1 = Z2 no se dibujaba: eso era el castillo invisible", 0,
      CorteEnAlzado.Piezas(castilloSinAlto, enX: true, ordenada: 5, espesorM: 0.6).Count);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(fallos == 0 ? " RESULTADO: todo bien" : $" RESULTADO: {fallos} fallaron");
Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
