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
            string forma = "RECT", string notas = "", double patin = 0, double alma = 0)
{
    m.Elementos.Add(new ElementoEtabs
    {
        Clase = c,
        Story = story,
        Seccion = sec,
        AnchoM = ancho,
        PeralteM = peralte,
        Forma = forma,
        Notas = notas,
        PatinM = patin,
        AlmaM = alma
    });
}

// Ojo: en la COLUMNA el lector guarda AnchoM = T3 y PeralteM = T2, al revés que en la
// viga. Es la regla de la macro y la tabla tiene que deshacerla.
Agrega(ClaseElemento.Columna, "Story1", "K 15X15", 0.15, 0.15);
Agrega(ClaseElemento.Columna, "Story1", "K 15X15", 0.15, 0.15);
Agrega(ClaseElemento.Columna, "Story2", "K 15X15", 0.15, 0.15);
Agrega(ClaseElemento.Columna, "Story1", "C 30X60", 0.30, 0.60);
Agrega(ClaseElemento.Trabe, "Story1", "CC 15X25", 0.15, 0.25);
Agrega(ClaseElemento.Trabe, "Story1", "T 15X30", 0.15, 0.30);
Agrega(ClaseElemento.Trabe, "Story1", "IPR 10X4", 0.10, 0.35, "I", string.Empty, 0.008, 0.006);
Agrega(ClaseElemento.Muro, "Story1", "W2", 0.15, 0, "AREA",
       "MURO TABICON 2 APLANADOS 15 CM");
Agrega(ClaseElemento.Muro, "Story2", "W2", 0.15, 0, "AREA",
       "MURO TABICON 2 APLANADOS 15 CM");
Agrega(ClaseElemento.Losa, "Story1", "LOSA AZOTEA", 0.10, 0, "AREA");

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
Igual("el muro trae su material de las notas", "MAMPOSTERIA", muro.Material);
Igual("en el muro no hay peralte", null, muro.PeralteCm);
Igual("y el espesor va en la columna del ancho", 15d, muro.AnchoCm);
Igual("y cuenta los dos paños", 2, muro.Cantidad);

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
Console.WriteLine(fallos == 0 ? " RESULTADO: todo bien" : $" RESULTADO: {fallos} fallaron");
Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
