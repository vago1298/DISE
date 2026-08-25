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

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(fallos == 0 ? " RESULTADO: todo bien" : $" RESULTADO: {fallos} fallaron");
Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
