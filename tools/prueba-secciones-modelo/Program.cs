using CadLink.Etabs;

// =====================================================================================
//  PRUEBA EJECUTABLE DE LA TABLA DE SECCIONES DEL MODELO (la hoja SECCIONES)
// =====================================================================================
//  Comprueba el port de VolcarSecciones, ClasificaTipo, MaterialDeMuro y OrdenDeTipo, y
//  el respaldo del espesor por el NOMBRE de la propiedad (DimsDesdeNombre), que es lo que
//  quita los 31 avisos de «el modelo no dio su ancho».
//
//      cd tools/prueba-secciones-modelo
//      dotnet run
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

void Check(string que, bool ok)
{
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}");
    if (!ok)
    {
        fallos++;
    }
}

Console.WriteLine("=====================================================================");
Console.WriteLine(" CLASIFICACION DEL TIPO   (ClasificaTipo de la macro)");
Console.WriteLine("=====================================================================");

string Tipo(ClaseElemento c, string sec, double t2, double t3) =>
    SeccionesModelo.ClasificaTipo(c, sec, t2, t3);

// CASTILLO_LADO_MAX_CM = 20: los dos lados menores o iguales
Igual("una columna de 15x15 es CASTILLO", "CASTILLO",
      Tipo(ClaseElemento.Columna, "K 15X15", 0.15, 0.15));
Igual("una de 15x25 ya es COLUMNA", "COLUMNA",
      Tipo(ClaseElemento.Columna, "K 15X25", 0.15, 0.25));
Igual("una de 30x60, COLUMNA", "COLUMNA",
      Tipo(ClaseElemento.Columna, "C 30X60", 0.30, 0.60));
Igual("y la que se LLAMA castillo lo es aunque mida 25x25", "CASTILLO",
      Tipo(ClaseElemento.Columna, "CASTILLO ESQUINA 25X25", 0.25, 0.25));
Igual("una columna sin medidas se queda en COLUMNA", "COLUMNA",
      Tipo(ClaseElemento.Columna, "SIN DATOS", 0, 0));

// DALA_PERALTE_MAX_CM = 25
Igual("una trabe de peralte 20 es DALA", "DALA",
      Tipo(ClaseElemento.Trabe, "CC 15X20", 0.15, 0.20));
Igual("la cadena de cerramiento CC 15X25 también", "DALA",
      Tipo(ClaseElemento.Trabe, "CC 15X25", 0.15, 0.25));
Igual("una de peralte 30 es TRABE", "TRABE",
      Tipo(ClaseElemento.Trabe, "T 15X30", 0.15, 0.30));
Igual("la que dice CONTRATRABE es CONTRATRABE", "CONTRATRABE",
      Tipo(ClaseElemento.Trabe, "CONTRATRABE 30X60", 0.30, 0.60));
Igual("la que dice CERRAMIENTO es DALA aunque tenga peralte", "DALA",
      Tipo(ClaseElemento.Trabe, "CERRAMIENTO 15X40", 0.15, 0.40));
Igual("y las áreas y diagonales, lo suyo", "MURO",
      Tipo(ClaseElemento.Muro, "MURO TABICON", 0.15, 0));
Igual("losa", "LOSA", Tipo(ClaseElemento.Losa, "LOSA AZOTEA", 0, 0));
Igual("diagonal", "DIAGONAL", Tipo(ClaseElemento.Diagonal, "IPR 8", 0.20, 0.20));

Console.WriteLine();
Console.WriteLine(" MATERIAL DEL MURO   (MaterialDeMuro de la macro)");

Igual("las palabras de mampostería, en las notas", "MAMPOSTERIA",
      SeccionesModelo.MaterialDeMuro("W2", "MURO TABICON 2 APLANADOS 15 CM"));
Igual("y en el nombre", "MAMPOSTERIA",
      SeccionesModelo.MaterialDeMuro("MURO DE BLOCK 15", string.Empty));
Igual("las de concreto", "CONCRETO",
      SeccionesModelo.MaterialDeMuro("W1", "MURO DE CONCRETO REFORZADO"));
Igual("si no dice nada, se queda en blanco", string.Empty,
      SeccionesModelo.MaterialDeMuro("W9", "PROPIEDAD 9"));
Igual("y manda la mampostería cuando aparecen las dos", "MAMPOSTERIA",
      SeccionesModelo.MaterialDeMuro("MURO TABICON CONFINADO", "CASTILLOS DE CONCRETO"));

Console.WriteLine();
Console.WriteLine(" EL ESPESOR POR EL NOMBRE   (DimsDesdeNombre de la macro)");
Console.WriteLine("   es lo que quita los 31 avisos de «el modelo no dio su ancho»");

Igual("«MURO 20 CM» son 0.20 m", 0.20, EtabsReader.EspesorDesdeNombre("MURO 20 CM"));
Igual("«MURO 15» son 0.15 m", 0.15, EtabsReader.EspesorDesdeNombre("MURO 15"));
Igual("«30X60» toma el primero: 0.30", 0.30, EtabsReader.EspesorDesdeNombre("30X60"));
Igual("«MURO TABICON 2 APLANADOS 15 CM» da 2.15, o sea que se descarta",
      2.15, EtabsReader.EspesorDesdeNombre("MURO TABICON 2 APLANADOS 15 CM"));
Igual("y un nombre sin números da 0", 0d, EtabsReader.EspesorDesdeNombre("MURO"));
Igual("Normalizar quita acentos, espacios y lo demás", "MUROCANON",
      EtabsReader.Normalizar("Muro cañón"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LA TABLA COMPLETA   (VolcarSecciones)");
Console.WriteLine("=====================================================================");

var m = new ModeloEtabs();

void Agrega(ClaseElemento c, string story, string sec, double ancho, double peralte,
            string forma = "RECT", string notas = "", double patin = 0, double alma = 0,
            string material = "", double largo = 0, double lado = 0, double alto = 0)
{
    var e = new ElementoEtabs
    {
        Clase = c,
        Story = story,
        Seccion = sec,
        AnchoM = ancho,
        PeralteM = peralte,
        Forma = forma,
        Notas = notas,
        PatinM = patin,
        AlmaM = alma,
        Material = material
    };

    // Una barra con largo: se pone el otro extremo a esa distancia en X.
    if (largo > 0)
    {
        e.X2 = largo;
    }

    // Un paño: cuadrado de «lado» en planta si es losa, o vertical de «lado» x «alto»
    // si es muro. Así el área sale de la geometría, como en el modelo de verdad.
    if (lado > 0)
    {
        if (alto > 0)
        {
            e.Vertices3D.Add((0, 0, 0));
            e.Vertices3D.Add((lado, 0, 0));
            e.Vertices3D.Add((lado, 0, alto));
            e.Vertices3D.Add((0, 0, alto));
            e.X2 = lado;
        }
        else
        {
            e.Vertices3D.Add((0, 0, 0));
            e.Vertices3D.Add((lado, 0, 0));
            e.Vertices3D.Add((lado, lado, 0));
            e.Vertices3D.Add((0, lado, 0));
        }

        foreach (var v in e.Vertices3D)
        {
            e.Vertices.Add((v.X, v.Y));
        }
    }

    m.Elementos.Add(e);
}

// Ojo: en la COLUMNA el lector guarda AnchoM = T3 y PeralteM = T2, al revés que en la
// viga. Es la regla de la macro y la tabla tiene que deshacerla.
Agrega(ClaseElemento.Columna, "Story1", "K 15X15", 0.15, 0.15, material: "CONC", largo: 3);
Agrega(ClaseElemento.Columna, "Story1", "K 15X15", 0.15, 0.15, material: "CONC", largo: 3);
Agrega(ClaseElemento.Columna, "Story2", "K 15X15", 0.15, 0.15, material: "CONC", largo: 3);
Agrega(ClaseElemento.Columna, "Story1", "C 30X60", 0.30, 0.60, material: "CONC", largo: 3);
Agrega(ClaseElemento.Trabe, "Story1", "CC 15X25", 0.15, 0.25, material: "CONC", largo: 4);
Agrega(ClaseElemento.Trabe, "Story1", "T 15X30", 0.15, 0.30, material: "CONC", largo: 5);
Agrega(ClaseElemento.Trabe, "Story1", "IPR 10X4", 0.10, 0.35, "I", string.Empty, 0.008, 0.006,
       material: "A992Fy50", largo: 6);
Agrega(ClaseElemento.Muro, "Story1", "W2", 0.15, 0, "AREA",
       "MURO TABICON 2 APLANADOS 15 CM", material: "MUR-TABICON", lado: 4, alto: 2.5);
Agrega(ClaseElemento.Muro, "Story2", "W2", 0.15, 0, "AREA",
       "MURO TABICON 2 APLANADOS 15 CM", material: "MUR-TABICON", lado: 4, alto: 2.5);
Agrega(ClaseElemento.Losa, "Story1", "LOSA AZOTEA", 0.10, 0, "AREA", material: "CONC",
       lado: 5);

var t = SeccionesModelo.Construir(m);

foreach (var f in t)
{
    Console.WriteLine($"        {f.Tipo,-12} {f.Seccion,-14} {f.Forma,-12} {f.Material,-12}" +
                      $" T3 {f.PeralteCm,6} T2 {f.AnchoCm,6}  x{f.Cantidad}  [{f.Niveles}]");
}

Console.WriteLine();
Igual("salen 7 secciones distintas", 7, t.Count);
Igual("y el orden es el de la macro: castillos, columnas, dalas, trabes…",
      "CASTILLO, COLUMNA, DALA, TRABE, TRABE, MURO, LOSA",
      string.Join(", ", t.Select(f => f.Tipo)));

var cast = t[0];
Igual("el castillo cuenta sus tres apariciones", 3, cast.Cantidad);
Igual("y dice en qué niveles está", "Story1,Story2", cast.Niveles);
Igual("con su T3 en cm", 15d, cast.PeralteCm);
Igual("y su T2 en cm", 15d, cast.AnchoCm);

var col = t.First(f => f.Tipo == "COLUMNA");
Igual("la columna 30x60: T3 es el peralte", 30d, col.PeralteCm);
Igual("y T2 el ancho", 60d, col.AnchoCm);

// Un perfil de 35 cm de peralte es TRABE. Con 25 o menos seria DALA: es la regla de
// DALA_PERALTE_MAX_CM y vale igual para el acero, tal como en la macro.
var ipr = t.First(f => f.Seccion == "IPR 10X4");
Igual("una viga de acero de 35 cm de peralte es TRABE", "TRABE", ipr.Tipo);
Igual("el perfil de acero se marca como tal", "I (ACERO)", ipr.Forma);
Igual("con su patín en cm", 0.8, ipr.PatinCm);
Igual("y su alma", 0.6, ipr.AlmaCm);

var muro = t.First(f => f.Tipo == "MURO");
// El muro dice las dos cosas: lo que la macro clasifica y el nombre del material del
// modelo, que es con lo que se comprueba. Si coincidieran, saldría solo una.
Igual("el muro trae su material de las notas y el del modelo",
      "MAMPOSTERIA (MUR-TABICON)", muro.Material);
Igual("en el muro no hay peralte", null, muro.PeralteCm);
Igual("y el espesor va en la columna del ancho", 15d, muro.AnchoCm);
Igual("y cuenta los dos paños", 2, muro.Cantidad);

Console.WriteLine();
Console.WriteLine(" LO QUE HAY DE CADA COSA: longitudes de los frames y areas de los shell");

Igual("el castillo suma sus 3 x 3.00 m", 9d, cast.LongitudTotalM);
Igual("y no tiene area, que es una barra", null, cast.AreaTotalM2);

var cc = t.First(f => f.Seccion == "CC 15X25");
Igual("la cadena, sus 4.00 m", 4d, cc.LongitudTotalM);

Igual("el muro suma el area de sus DOS paños de 4.00 x 2.50", 20d, muro.AreaTotalM2);
Igual("y no tiene longitud, que es un paño", null, muro.LongitudTotalM);

var losa = t.First(f => f.Tipo == "LOSA");
Igual("la losa de 5.00 x 5.00 son 25 m2", 25d, losa.AreaTotalM2);

Console.WriteLine();
Console.WriteLine(" EL MATERIAL, EN TODOS Y NO SOLO EN LOS MUROS");

Igual("el castillo trae el material del modelo", "CONC", cast.Material);
Igual("la viga de acero, el suyo", "A992Fy50", ipr.Material);
Igual("y el muro dice las dos cosas cuando no coinciden",
      "MAMPOSTERIA (MUR-TABICON)", muro.Material);

var sinLosas = SeccionesModelo.Construir(m, new SeccionesModelo.Opciones(IncluyeLosas: false));
Check("con TABLA_INCLUYE_LOSAS en NO, las losas no salen",
      sinLosas.All(f => f.Tipo != "LOSA") && sinLosas.Count == t.Count - 1);

var vacio = SeccionesModelo.Construir(new ModeloEtabs());
Check("un modelo vacío da una tabla vacía, sin reventar", vacio.Count == 0);

var sinNombre = new ModeloEtabs();
sinNombre.Elementos.Add(new ElementoEtabs
{
    Clase = ClaseElemento.Trabe, Story = "Story1", Seccion = "   ", PeralteM = 0.60
});
Igual("una sección sin nombre se muestra como (sin nombre)", "(sin nombre)",
      SeccionesModelo.Construir(sinNombre)[0].Seccion);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LOS NIVELES QUE SE DIBUJAN, CON LA BASE   (StoriesDesdeElementos)");
Console.WriteLine("=====================================================================");

// El caso real: la API devuelve Story1 y Story2, pero NO la Base, y en el modelo hay
// cadenas de desplante con Story = «Base». Antes esa planta no se dibujaba nunca.
var conBase = new ModeloEtabs();
conBase.Niveles.Add(new NivelEtabs { Nombre = "Story1", ElevacionM = 2.7 });
conBase.Niveles.Add(new NivelEtabs { Nombre = "Story2", ElevacionM = 5.4 });
conBase.Niveles.Add(new NivelEtabs { Nombre = "Story3", ElevacionM = 8.1 });   // sin elementos

conBase.Elementos.Add(new ElementoEtabs
{
    Clase = ClaseElemento.Trabe, Story = "Base", Seccion = "CD 15X25",
    Z1 = -0.30, Z2 = -0.30
});
conBase.Elementos.Add(new ElementoEtabs
{
    Clase = ClaseElemento.Trabe, Story = "Story2", Seccion = "T 15X30", Z1 = 5.4, Z2 = 5.4
});
conBase.Elementos.Add(new ElementoEtabs
{
    Clase = ClaseElemento.Columna, Story = "Story1", Seccion = "K 15X15", Z1 = 0, Z2 = 2.7
});

var niveles = conBase.NivelesConElementos();

Igual("la BASE entra aunque la API no la devuelva, y va primero",
      "Base, Story1, Story2", string.Join(", ", niveles.Select(n => n.Nombre)));
Igual("su cota sale de sus propios elementos", -0.30, niveles[0].ElevacionM);
Igual("los niveles SIN elementos se quedan fuera", 3, niveles.Count);
Igual("y al revés, para la lista de elegir a mano", "Story2, Story1, Base",
      string.Join(", ", conBase.NivelesConElementos(ascendente: false).Select(n => n.Nombre)));

var soloBase = new ModeloEtabs();
soloBase.Elementos.Add(new ElementoEtabs
{
    Clase = ClaseElemento.Trabe, Story = "Base", Seccion = "CD 15X25"
});
Igual("un modelo que solo tiene la base también se dibuja", 1,
      soloBase.NivelesConElementos().Count);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL ESPESOR DE LA LOSA: EL DEL MODELO, Y SI NO, UNO CON SENTIDO");
Console.WriteLine("=====================================================================");

// El respaldo del NOMBRE de la propiedad, que es el de la macro: «LOSA 10» da 0.10.
Cerca("«LOSA 10» da 10 cm", 0.10, EtabsReader.EspesorDesdeNombre("LOSA 10"));
Cerca("«MURO 15 CM» da 15 cm", 0.15, EtabsReader.EspesorDesdeNombre("MURO 15 CM"));
// Y «LOSA VOLADO» no trae numero: de ahi no sale espesor, y por eso el rotulo salia con
// el hueco vacio y la extruida dibujaba la losa plana.
Cerca("«LOSA VOLADO» no da espesor", 0, EtabsReader.EspesorDesdeNombre("LOSA VOLADO"));
Cerca("ni «LOSA ENTREPISO»", 0, EtabsReader.EspesorDesdeNombre("LOSA ENTREPISO"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL TIPO, POR LAS NOTAS DE LA PROPIEDAD");
Console.WriteLine("=====================================================================");

// Es la respuesta a «como puedo hacer que los clasifiques como tipos»: con las NOTAS de
// la propiedad. Si en las notas dice CASTILLO, es CASTILLO, y no hay mas que discutir.
Igual("CASTILLO en las notas manda", "CASTILLO",
      SeccionesModelo.TipoDeLasNotas("CASTILLO"));
Igual("COLUMNA en las notas manda", "COLUMNA",
      SeccionesModelo.TipoDeLasNotas("columna de concreto"));
Igual("TRABE en las notas manda", "TRABE", SeccionesModelo.TipoDeLasNotas("TRABE"));
// EL CABEZAL, que se pidio leer igual que los demas. Va ANTES que TRABE y que VIGA en la lista:
// una nota que diga «CABEZAL DE TRABE» es un cabezal, no una trabe.
Igual("CABEZAL en las notas sale como CABEZAL", "CABEZAL",
      SeccionesModelo.TipoDeLasNotas("CABEZAL"));
Igual("en minusculas igual", "CABEZAL", SeccionesModelo.TipoDeLasNotas("cabezal de concreto"));
Igual("y manda sobre TRABE si estan las dos", "CABEZAL",
      SeccionesModelo.TipoDeLasNotas("CABEZAL DE TRABE"));
// LAS TRES CADENAS, CADA UNA CON SU NOMBRE: son tres cosas distintas en obra -la de
// desplante va sobre la cimentacion, la intermedia a media altura del muro y la de
// cerramiento arriba- y fundirlas en «DALA» es perder el dato que las distingue.
Igual("la de CERRAMIENTO sale con su nombre", "CADENA DE CERRAMIENTO",
      SeccionesModelo.TipoDeLasNotas("CADENA DE CERRAMIENTO"));
Igual("la de DESPLANTE, tambien", "CADENA DE DESPLANTE",
      SeccionesModelo.TipoDeLasNotas("cadena de desplante"));
Igual("y la INTERMEDIA", "CADENA INTERMEDIA",
      SeccionesModelo.TipoDeLasNotas("CADENA INTERMEDIA"));
// Y las tres van CON las dalas en el orden de la tabla: se leen juntas.
Igual("la de cerramiento se ordena con las dalas",
      SeccionesModelo.OrdenDeTipo("DALA"),
      SeccionesModelo.OrdenDeTipo("CADENA DE CERRAMIENTO"));
Igual("y la de desplante tambien",
      SeccionesModelo.OrdenDeTipo("DALA"),
      SeccionesModelo.OrdenDeTipo("CADENA DE DESPLANTE"));
// Una cadena a secas, sin decir de que tipo, sigue siendo DALA.
Igual("«CADENA» a secas es DALA", "DALA", SeccionesModelo.TipoDeLasNotas("CADENA"));
Igual("y sin notas no dice nada", "", SeccionesModelo.TipoDeLasNotas(""));

// EL ORDEN IMPORTA, y no es alfabetico: lo mas especifico primero. «CONTRATRABE»
// contiene la palabra TRABE, asi que preguntando por TRABE antes, todas las
// contratrabes saldrian mal.
Igual("CONTRATRABE no se confunde con TRABE", "CONTRATRABE",
      SeccionesModelo.TipoDeLasNotas("CONTRATRABE DE LIGA"));
Igual("ni CASTILLO con COLUMNA", "CASTILLO",
      SeccionesModelo.TipoDeLasNotas("CASTILLO AHOGADO EN COLUMNA"));

// Y ASI SE ARREGLA EL CASO DE LA TABLA: «K 15X23.5» mide mas de 20 cm de un lado, asi
// que POR MEDIDAS sale COLUMNA aunque en obra sea un castillo.
Igual("por medidas, la de 15x23.5 sale COLUMNA", "COLUMNA",
      SeccionesModelo.ClasificaTipo(ClaseElemento.Columna, "K 15X23.5", 0.15, 0.235));
Igual("y con CASTILLO en sus notas, CASTILLO", "CASTILLO",
      SeccionesModelo.ClasificaTipo(
          ClaseElemento.Columna, "K 15X23.5", 0.15, 0.235, null, "CASTILLO"));

// Lo que las notas no digan se sigue clasificando como antes.
Igual("sin notas, la de 15x15 sigue siendo CASTILLO por medidas", "CASTILLO",
      SeccionesModelo.ClasificaTipo(ClaseElemento.Columna, "K 15X15", 0.15, 0.15, null, ""));
Igual("y una nota que no habla de tipos no estorba", "COLUMNA",
      SeccionesModelo.ClasificaTipo(
          ClaseElemento.Columna, "C 30X60", 0.30, 0.60, null, "f'c = 250"));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL PUNTO DE INSERCION: POR ESTO LA BARRA APARECE MOVIDA");
Console.WriteLine("=====================================================================");

void Cerca(string que, double esperado, double real, double tol = 1e-9)
{
    var ok = Math.Abs(esperado - real) <= tol;
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}" +
                      (ok ? string.Empty : $"   esperado <{esperado}>, salio <{real}>"));
    if (!ok)
    {
        fallos++;
    }
}

// ---- LOS EJES LOCALES, que son la base de todo -----------------------------------
// TRABE a lo largo del +X: el 1 va a lo largo, el 2 es VERTICAL (+Z) y el 3 queda
// horizontal y perpendicular. Es la convencion de CSI, y es la que explica que en una
// trabe el offset del eje 3 la mueva en planta y el del eje 2 solo la suba o la baje.
var (t1, t2v, t3v) = PuntoDeInsercion.Ejes(false, 1, 0, 0);

Cerca("trabe en +X: el eje 1 va a lo largo", 1, t1[0]);
Cerca("el eje 2 es VERTICAL", 1, t2v[2]);
Cerca("y no tiene componente en planta, en X", 0, t2v[0]);
Cerca("ni en Y", 0, t2v[1]);
Cerca("el eje 3 es horizontal y perpendicular: -Y", -1, t3v[1]);
Cerca("sin componente vertical", 0, t3v[2]);

// COLUMNA: el 1 hacia arriba, el 2 al +X y el 3 al +Y. Los DOS ejes de la seccion son
// horizontales, asi que cualquier offset la mueve en planta.
var (c1, c2v, c3v) = PuntoDeInsercion.Ejes(true, 0, 0, 0);

Cerca("columna: el eje 1 va hacia arriba", 1, c1[2]);
Cerca("el eje 2 al +X", 1, c2v[0]);
Cerca("y el eje 3 al +Y", 1, c3v[1]);

// Con giro de ejes locales, el 2 y el 3 giran alrededor del 1.
var (_, g2, g3) = PuntoDeInsercion.Ejes(true, 0, 0, 90);

Cerca("una columna girada 90: el eje 2 pasa al +Y", 1, g2[1], 1e-12);
Cerca("y el eje 3 al -X", -1, g3[0], 1e-12);

// ---- LOS OFFSETS DE NUDO, que es lo que trae el modelo ---------------------------
// El caso de la pantalla: offsets en el eje 3 de -0.025 en los dos extremos, en LOCALES.
// EL MOVIMIENTO CON SU Z, que es lo que se ve en la vista extruida. El punto cardinal de una
// trabe es casi siempre el 8 -arriba al centro-: su CARA DE ARRIBA va a la cota de la linea, asi
// que el centro de la seccion BAJA medio peralte y la trabe cuelga del piso. Dibujandola centrada,
// medio peralte quedaba POR ENCIMA de la losa.
var (_, _, dzArriba) = PuntoDeInsercion.Movimiento(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0, 0 }, enLocales: true,
    puntoCardinal: 8, dim2: 0.40, dim3: 0.20);

Cerca("con el punto 8 la trabe de 40 baja 20 cm", -0.20, dzArriba);

// Con el centroide no se mueve nada: es el de omision.
var (_, _, dzCentro) = PuntoDeInsercion.Movimiento(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0, 0 }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.40, dim3: 0.20);

Cerca("y con el centroide se queda donde esta", 0, dzCentro);

// El punto 2 -abajo al centro- la sube: la cara de abajo queda en la linea.
var (_, _, dzAbajo) = PuntoDeInsercion.Movimiento(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0, 0 }, enLocales: true,
    puntoCardinal: 2, dim2: 0.40, dim3: 0.20);

Cerca("con el punto 2 sube 20 cm", 0.20, dzAbajo);

// Y en una COLUMNA el eje 2 es horizontal, asi que el punto cardinal no la mueve en Z.
var (_, _, dzColumna) = PuntoDeInsercion.Movimiento(
    vertical: true, ux: 0, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0, 0 }, enLocales: true,
    puntoCardinal: 8, dim2: 0.60, dim3: 0.20);

Cerca("en una columna el punto cardinal no mueve la Z", 0, dzColumna);

// Un offset de nudo en Z SI la mueve, y llega en la tercera componente.
var (_, _, dzOffset) = PuntoDeInsercion.Movimiento(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, -0.05, 0 }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.40, dim3: 0.20);

Cerca("y el offset del nudo en el eje 2 baja lo suyo", -0.05, dzOffset);

var (dxTrabe, dyTrabe) = PuntoDeInsercion.EnPlanta(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0d, -0.025 }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.25, dim3: 0.15);

Cerca("trabe en +X con offset 3 = -0.025: NO se mueve en X", 0, dxTrabe);
Cerca("y se mueve 2.5 cm en Y, que es lo que se ve corrido", 0.025, dyTrabe);

// El mismo offset, pero en el eje 2: en una trabe eso es VERTICAL, no se ve en planta.
var (dx2, dy2) = PuntoDeInsercion.EnPlanta(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, -0.025, 0d }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.25, dim3: 0.15);

Cerca("el offset del eje 2 en una trabe no mueve la planta, en X", 0, dx2);
Cerca("ni en Y: solo la sube o la baja", 0, dy2);

// En una COLUMNA el mismo offset SI mueve la planta: sus dos ejes son horizontales.
var (dxCol, dyCol) = PuntoDeInsercion.EnPlanta(
    vertical: true, ux: 0, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0.10, -0.025 }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.15, dim3: 0.15);

Cerca("columna: el offset del eje 2 la mueve en X", 0.10, dxCol);
Cerca("y el del eje 3, en Y", -0.025, dyCol);

// En GLOBALES los offsets ya vienen en X, Y y Z: se toman tal cual.
var (dxG, dyG) = PuntoDeInsercion.EnPlanta(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0.05, -0.03, 0.99 }, enLocales: false,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.25, dim3: 0.15);

Cerca("en globales el offset X se toma tal cual", 0.05, dxG);
Cerca("y el Y tambien", -0.03, dyG);

// ---- EL PUNTO CARDINAL ------------------------------------------------------------
// El 10 es el centroide, el de omision: no corre nada, y es el que trae su modelo.
Igual("el centroide no corre la seccion", (0d, 0d),
      PuntoDeInsercion.PorPuntoCardinal(10, 0.25, 0.15));
Igual("ni el centro de cortante", (0d, 0d),
      PuntoDeInsercion.PorPuntoCardinal(11, 0.25, 0.15));
Igual("ni el centro de la cuadricula, el 5", (0d, 0d),
      PuntoDeInsercion.PorPuntoCardinal(5, 0.25, 0.15));

// El 8 es «arriba al centro»: la cara de arriba queda en la linea y el centro BAJA
// medio peralte. Es lo tipico de una trabe a paño de losa.
var (d2Arriba, d3Arriba) = PuntoDeInsercion.PorPuntoCardinal(8, 0.25, 0.15);

Cerca("el punto 8 baja el centro medio peralte", -0.125, d2Arriba);
Cerca("y no lo corre de lado", 0, d3Arriba);

// El 1 es «abajo a la izquierda»: la esquina queda en la linea y el centro se va
// arriba y a la derecha.
var (d2Esq, d3Esq) = PuntoDeInsercion.PorPuntoCardinal(1, 0.25, 0.15);

Cerca("el punto 1 sube el centro medio peralte", 0.125, d2Esq);
Cerca("y lo corre medio ancho", 0.075, d3Esq);

// El espejo respecto del eje 2 invierte el lado.
var (_, d3Espejo) = PuntoDeInsercion.PorPuntoCardinal(1, 0.25, 0.15, espejo2: true);

Cerca("con espejo en el eje 2 el lado se invierte", -0.075, d3Espejo);

// Y en una COLUMNA el punto cardinal mueve la planta en las dos direcciones: es el
// caso de la columna de esquina alineada al paño de la fachada.
var (dxEsq, dyEsq) = PuntoDeInsercion.EnPlanta(
    vertical: true, ux: 0, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0d, 0d }, enLocales: true,
    puntoCardinal: 1, dim2: 0.30, dim3: 0.20);

Cerca("columna con el punto 1: media seccion en X", 0.15, dxEsq);
Cerca("y media en Y", 0.10, dyEsq);

// Una barra sin punto de insercion no se mueve, que es el caso de casi todo el modelo.
var (dxNada, dyNada) = PuntoDeInsercion.EnPlanta(
    vertical: false, ux: 1, uy: 0, anguloGrados: 0,
    offset: new[] { 0d, 0d, 0d }, enLocales: true,
    puntoCardinal: PuntoDeInsercion.Centroide, dim2: 0.25, dim3: 0.15);

Cerca("sin offsets y con el centroide no se mueve nada, en X", 0, dxNada);
Cerca("ni en Y", 0, dyNada);

// Y el elemento lo dice: ConPuntoDeInsercion es lo que se mira en el diagnostico.
var movida = new ElementoEtabs { MovidoYI = 0.025, MovidoYJ = 0.025 };
var quieta = new ElementoEtabs();

Check("la barra movida se reconoce", movida.ConPuntoDeInsercion);
Check("y la que no se movio, tambien", !quieta.ConPuntoDeInsercion);
Igual("el punto cardinal de omision es el centroide", 10, quieta.PuntoCardinal);

// =====================================================================================
//  EL PRETIL, AL NIVEL QUE LO SOSTIENE: MURO, CASTILLOS Y CADENA DE REMATE
// =====================================================================================
//  Se reporto: «arriba del pasillo va un pretil de 1 m que se debe ver en el piso de cada
//  uno, pero donde van pretiles no hay nada y los estas colocando un nivel arriba». Y
//  despues: «ya pones el muro en su nivel, pero tambien te faltan las columnas y vigas que
//  estan a 1 m del piso terminado».
//
//  Un pretil no es solo su muro: lleva sus CASTILLOS -columnas cortas- y su CADENA DE
//  REMATE -una viga a un metro del piso-. Las tres se iban al mismo sitio equivocado porque
//  ETABS asigna cada pieza al piso de su cota MAS ALTA.
//
//  Y LA MITAD DE ESTA PRUEBA son las piezas que NO se deben mover, una por una, porque se
//  pidio expresamente «no quiero que mueva todos los muros, solo los pretiles».
Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" EL PRETIL: MURO, CASTILLOS Y CADENA, AL NIVEL QUE LOS SOSTIENE");
Console.WriteLine("=====================================================================");

// Un edificio de tres niveles con entrepiso de 2.80: losas a 0, 2.8, 5.6 y 8.4.
NivelEtabs Nivel(string nombre, double elev, double alto) =>
    new() { Nombre = nombre, ElevacionM = elev, AlturaM = alto };

// Un PANEL de muro: shell con sus vertices 3D.
ElementoEtabs Panel(string story, double zAbajo, double zArriba, double x = 0)
{
    var e = new ElementoEtabs
    {
        Clase = ClaseElemento.Muro, Story = story, Forma = "AREA",
        X1 = x, Y1 = 0, X2 = x + 4, Y2 = 0, AnchoM = 0.15,
        Z1 = zAbajo, Z2 = zArriba
    };

    e.Vertices3D.Add((x, 0, zAbajo));
    e.Vertices3D.Add((x + 4, 0, zAbajo));
    e.Vertices3D.Add((x + 4, 0, zArriba));
    e.Vertices3D.Add((x, 0, zArriba));

    return e;
}

// Una BARRA: columna si sube, viga si es plana. Sin vertices 3D, como llegan del lector.
ElementoEtabs Barra(ClaseElemento clase, string story,
                    double zAbajo, double zArriba, double x = 0, double largo = 0)
{
    return new ElementoEtabs
    {
        Clase = clase, Story = story,
        X1 = x, Y1 = 0, Z1 = zAbajo,
        X2 = x + largo, Y2 = 0, Z2 = zArriba,
        AnchoM = 0.15, PeralteM = 0.25
    };
}

ElementoEtabs Castillo(string story, double zAbajo, double zArriba, double x = 0) =>
    Barra(ClaseElemento.Columna, story, zAbajo, zArriba, x);

ElementoEtabs Viga(string story, double z, double x = 0) =>
    Barra(ClaseElemento.Trabe, story, z, z, x, largo: 4);

var n1 = Nivel("Story1", 2.8, 2.8);
var n2 = Nivel("Story2", 5.6, 2.8);
var n3 = Nivel("Story3", 8.4, 2.8);

// El modelo con el que se preguntan los casos de una en una. Hace falta porque la condicion
// de «no continua hacia arriba» mira el resto del modelo.
var solo = new ModeloEtabs();

bool EsPretil(ElementoEtabs el, NivelEtabs suyo, double losaAbajo,
              double alturaMax = Pretil.AlturaMaximaM)
{
    var uno = new ModeloEtabs();
    uno.Elementos.Add(el);
    return Pretil.EsDelPretil(uno, el, suyo, losaAbajo, Pretil.ToleranciaM, alturaMax);
}

// ---- LAS TRES PIEZAS DEL PRETIL: se paran en la losa del 2 (5.6) y no llegan a la del 3.
Check("el MURO del pretil, de 1 m, si es del pretil",
      EsPretil(Panel("Story3", 5.6, 6.6), n3, 5.6));
Check("el CASTILLO del pretil tambien",
      EsPretil(Castillo("Story3", 5.6, 6.6), n3, 5.6));
Check("y la CADENA DE REMATE, que flota a 1 m y no se apoya en la losa",
      EsPretil(Viga("Story3", 6.6), n3, 5.6));

// La cadena es EL caso que forzo a cambiar la regla: no se apoya en la losa, asi que medir
// «se para en la losa de abajo» la dejaba fuera. Lo que se mide es su altura SOBRE la losa.
Check("una cadena a 1.4 m sigue siendo del pretil",
      EsPretil(Viga("Story3", 7.0), n3, 5.6));
Check("pero una a 2 m ya no: pasa del tope de altura",
      !EsPretil(Viga("Story3", 7.6), n3, 5.6));

// ---- LO QUE **NO** SE DEBE MOVER ----

Check("un MURO COMPLETO de piso a techo NO se mueve",
      !EsPretil(Panel("Story3", 5.6, 8.4), n3, 5.6));
Check("una COLUMNA normal de piso a piso NO se mueve",
      !EsPretil(Castillo("Story3", 5.6, 8.4), n3, 5.6));
Check("una VIGA a la altura de su losa NO se mueve",
      !EsPretil(Viga("Story3", 8.4), n3, 5.6));
Check("una CADENA DE CERRAMIENTO, que va en la losa, tampoco",
      !EsPretil(Viga("Story3", 8.35), n3, 5.6));
Check("un DINTEL NO se mueve: su tapa es la losa",
      !EsPretil(Panel("Story3", 7.7, 8.4), n3, 5.6));
Check("el panel de encima de un antepecho NO se mueve",
      !EsPretil(Panel("Story3", 6.6, 8.4), n3, 5.6));
Check("un muro de dos pisos de corrido NO se mueve",
      !EsPretil(Panel("Story3", 2.8, 8.4), n3, 5.6));
Check("una LOSA nunca se mueve: una losa a media altura es un entrepiso",
      !EsPretil(Barra(ClaseElemento.Losa, "Story3", 5.6, 6.6), n3, 5.6));
Check("y una DIAGONAL tampoco",
      !EsPretil(Barra(ClaseElemento.Diagonal, "Story3", 5.6, 6.6, largo: 3), n3, 5.6));
Check("un muro de 1.9 m sobre la losa no se mueve: pasa del tope",
      !EsPretil(Panel("Story3", 5.6, 7.5), n3, 5.6));
Check("pero subiendo el tope a 2.0 si, o sea que el tope es lo que lo frena",
      EsPretil(Panel("Story3", 5.6, 7.5), n3, 5.6, alturaMax: 2.0));

// ---- LA CONDICION 3: NO CONTINUA HACIA ARRIBA ----
//  Es la que salva al ANTEPECHO DE VENTANA y a la COLUMNA PARTIDA EN DOS. Las dos se
//  parecen muchisimo a un pretil: son bajas y no llegan a la losa. La diferencia es que
//  llevan algo encima que SI llega.
var conAntepecho = new ModeloEtabs();

var antepecho = Panel("Story3", 5.6, 6.6);
var sobreAntepecho = Panel("Story3", 6.6, 8.4);

conAntepecho.Elementos.Add(antepecho);
conAntepecho.Elementos.Add(sobreAntepecho);

Check("con pared encima que llega a la losa, el antepecho NO es un pretil",
      !Pretil.EsDelPretil(conAntepecho, antepecho, n3, 5.6));
Check("y se sabe porque continua hacia arriba",
      Pretil.ContinuaArriba(conAntepecho, antepecho));

// SIN esa pared encima, el mismo panel SI es un pretil. Asi se ve que lo que decide es la
// pieza de arriba y no otra cosa del panel.
var sinNadaEncima = new ModeloEtabs();
var pretilSuelto = Panel("Story3", 5.6, 6.6);

sinNadaEncima.Elementos.Add(pretilSuelto);

Check("quitando la pared de encima, el mismo panel SI es un pretil",
      Pretil.EsDelPretil(sinNadaEncima, pretilSuelto, n3, 5.6));
Check("y no continua hacia arriba", !Pretil.ContinuaArriba(sinNadaEncima, pretilSuelto));

// UNA COLUMNA PARTIDA EN DOS por el modelador, casi siempre porque ahi llega una cadena.
var partida = new ModeloEtabs();

var mitadBaja = Castillo("Story3", 5.6, 6.6);
var mitadAlta = Castillo("Story3", 6.6, 8.4);

partida.Elementos.Add(mitadBaja);
partida.Elementos.Add(mitadAlta);

Check("la mitad de abajo de una columna partida NO es un pretil",
      !Pretil.EsDelPretil(partida, mitadBaja, n3, 5.6));

// Y LA CADENA DE REMATE DEL PROPIO PRETIL no cuenta como continuacion del castillo: es
// plana. Sin esto, el castillo del pretil se quedaria arriba por culpa de su propia cadena.
var pretilCompleto = new ModeloEtabs();

var muroP = Panel("Story3", 5.6, 6.6);
var castilloP = Castillo("Story3", 5.6, 6.6);
var cadenaP = Viga("Story3", 6.6);

pretilCompleto.Elementos.Add(muroP);
pretilCompleto.Elementos.Add(castilloP);
pretilCompleto.Elementos.Add(cadenaP);

Check("la cadena de remate NO cuenta como continuacion del castillo",
      !Pretil.ContinuaArriba(pretilCompleto, castilloP));
Check("asi que el castillo del pretil si se mueve",
      Pretil.EsDelPretil(pretilCompleto, castilloP, n3, 5.6));

// ---- AHORA SOBRE EL MODELO ENTERO, CON EL CASO REPORTADO ----
var mod = new ModeloEtabs();

mod.Niveles.Add(n1);
mod.Niveles.Add(n2);
mod.Niveles.Add(n3);

// El pretil del pasillo del nivel 3, completo: muro, dos castillos y su cadena.
var pMuro = Panel("Story3", 5.6, 6.6, x: 20);
var pCast1 = Castillo("Story3", 5.6, 6.6, x: 20);
var pCast2 = Castillo("Story3", 5.6, 6.6, x: 24);
var pCadena = Viga("Story3", 6.6, x: 20);

// Y el del pasillo del nivel 2.
var qMuro = Panel("Story2", 2.8, 3.8, x: 20);
var qCadena = Viga("Story2", 3.8, x: 20);

// La estructura de verdad de cada nivel, que NO se puede mover.
var muro3 = Panel("Story3", 5.6, 8.4);
var col3 = Castillo("Story3", 5.6, 8.4, x: 8);
var viga3 = Viga("Story3", 8.4, x: 8);
var muro2 = Panel("Story2", 2.8, 5.6);
var col2 = Castillo("Story2", 2.8, 5.6, x: 8);

foreach (var e in new[]
         {
             pMuro, pCast1, pCast2, pCadena, qMuro, qCadena,
             muro3, col3, viga3, muro2, col2
         })
{
    mod.Elementos.Add(e);
}

var movidos = Pretil.Bajar(mod);

Igual("se bajaron las SEIS piezas de pretil y nada mas", 6, movidos.Count);

Igual("el muro del pretil del 3 pasa al Story2", "Story2", pMuro.Story);
Igual("su primer castillo tambien", "Story2", pCast1.Story);
Igual("y el segundo", "Story2", pCast2.Story);
Igual("y su cadena de remate", "Story2", pCadena.Story);
Igual("el muro del pretil del 2 pasa al Story1", "Story1", qMuro.Story);
Igual("y su cadena", "Story1", qCadena.Story);

Igual("EL MURO COMPLETO DEL 3 NO SE MOVIO", "Story3", muro3.Story);
Igual("LA COLUMNA DEL 3 NO SE MOVIO", "Story3", col3.Story);
Igual("LA VIGA DEL 3 NO SE MOVIO", "Story3", viga3.Story);
Igual("EL MURO COMPLETO DEL 2 NO SE MOVIO", "Story2", muro2.Story);
Igual("LA COLUMNA DEL 2 NO SE MOVIO", "Story2", col2.Story);

// El aviso dice de donde a donde y de que clase, que es lo que hace falta para revisarlo.
var avisoPretil = Pretil.Aviso(movidos);

Check("el aviso menciona los dos niveles de destino",
      avisoPretil.Contains("Story1") && avisoPretil.Contains("Story2"));
Check("y distingue muros de castillos y de cadenas",
      avisoPretil.Contains("muro") && avisoPretil.Contains("castillo")
      && avisoPretil.Contains("cadena"));
Igual("sin nada movido no hay aviso", "", Pretil.Aviso(new List<Pretil.Bajado>()));

// ---- IDEMPOTENTE: aplicarlo dos veces no baja el pretil dos pisos ----
// Es importante de verdad: el modelo se lee mas de una vez en la sesion, y un pretil que
// fuera bajando un piso en cada lectura acabaria en la cimentacion.
var segunda = Pretil.Bajar(mod);

Igual("aplicado dos veces no mueve nada mas", 0, segunda.Count);
Igual("y el muro se queda donde lo dejo la primera vez", "Story2", pMuro.Story);
Igual("y la cadena tambien", "Story2", pCadena.Story);

// ---- SIN NIVEL DE DESTINO no se adivina: se queda donde estaba ----
var raro = new ModeloEtabs();

raro.Niveles.Add(n3);

var sinDestino = Panel("Story3", 5.6, 6.6);

raro.Elementos.Add(sinDestino);

Igual("con un solo nivel no se mueve nada", 0, Pretil.Bajar(raro).Count);
Igual("y se queda en su nivel", "Story3", sinDestino.Story);

// ---- LA TOLERANCIA: la losa nunca cae exacta en el modelo ----
Check("un pretil 3 cm por encima de la losa sigue siendo pretil",
      EsPretil(Panel("Story3", 5.63, 6.6), n3, 5.6));
Check("y 3 cm por debajo tambien",
      EsPretil(Panel("Story3", 5.57, 6.6), n3, 5.6));

// ---- LAS COTAS SALEN DE LOS VERTICES 3D, NO DE Z1/Z2 ----
// Z1/Z2 los pone el lector al minimo y al maximo, pero un panel que llegue por otro camino
// podria traerlos como las cotas de dos vertices cualesquiera, y saldria de altura cero.
var conVertices = Panel("Story3", 5.6, 6.6);
conVertices.Z1 = 6.6;
conVertices.Z2 = 6.6;

var cotas = Pretil.CotasDe(conVertices);

Igual("la cota de abajo sale de los vertices 3D", 5.6, cotas.Abajo);
Igual("y la de arriba tambien", 6.6, cotas.Arriba);

// Una BARRA no trae vertices 3D: sus cotas son las de sus dos nudos.
Igual("una barra saca la cota de abajo de Z1", 5.6,
      Pretil.CotasDe(Castillo("Story3", 5.6, 6.6)).Abajo);
Igual("y la de arriba de Z2", 6.6,
      Pretil.CotasDe(Castillo("Story3", 5.6, 6.6)).Arriba);

// ---- QUE CLASES SE BAJAN ----
Check("se bajan los muros", Pretil.ClaseQueSeBaja(ClaseElemento.Muro));
Check("las columnas", Pretil.ClaseQueSeBaja(ClaseElemento.Columna));
Check("y las trabes", Pretil.ClaseQueSeBaja(ClaseElemento.Trabe));
Check("las losas NO", !Pretil.ClaseQueSeBaja(ClaseElemento.Losa));
Check("y las diagonales NO", !Pretil.ClaseQueSeBaja(ClaseElemento.Diagonal));

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(fallos == 0 ? " RESULTADO: todo bien" : $" RESULTADO: {fallos} fallaron");
Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
