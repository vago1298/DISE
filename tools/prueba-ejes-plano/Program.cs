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
