using CadLink.Revit.Nucleo;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba del nucleo del complemento de Revit.
/// </summary>
/// <remarks>
/// El complemento decide cosas que despues son muy caras de deshacer: con que familia se
/// modela cada seccion, y que piezas se crean, se cambian o se dejan en paz. Todo eso se
/// decide aqui, sin Revit, y por eso se puede comprobar de verdad.
/// </remarks>
internal static class Programa
{
    private static readonly List<string> Fallos = new();

    private static void Check(string nombre, bool ok, string detalle = "")
    {
        Console.WriteLine((ok ? "  OK    " : "  FALLA ") + nombre
                          + (ok || detalle.Length == 0 ? "" : "  -> " + detalle));

        if (!ok)
        {
            Fallos.Add(nombre);
        }
    }

    private static void Igual(string nombre, object? dio, object? esperado) =>
        Check(nombre, Equals(dio, esperado), $"esperaba «{esperado}» y dio «{dio}»");

    // ==================================================================
    //  Cosas de ejemplo
    // ==================================================================

    private static SeccionJson Rect(string nombre, double anchoCm, double peralteCm,
        string material = "CONC") => new()
    {
        Nombre = nombre,
        Forma = FormaSeccion.Rectangulo,
        AnchoM = anchoCm / 100.0,
        PeralteM = peralteCm / 100.0,
        Material = material
    };

    private static SeccionJson PerfilI(string nombre, double anchoCm, double peralteCm) => new()
    {
        Nombre = nombre,
        Forma = FormaSeccion.PerfilI,
        AnchoM = anchoCm / 100.0,
        PeralteM = peralteCm / 100.0,
        PatinM = 0.011,
        AlmaM = 0.007,
        Material = "A992Fy50"
    };

    private static PuntoJson P(double x, double y, double z) => new() { X = x, Y = y, Z = z };

    private static ModeloJson ModeloDePrueba()
    {
        var m = new ModeloJson
        {
            Programa = "ETABS",
            Archivo = "ejemplo.EDB",
            Obra = "Edificio ÑOÑO",
            Exportado = "2026-09-28T12:00:00"
        };

        m.Niveles.Add(new NivelJson { Nombre = "Base", ElevacionM = 0 });
        m.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });

        // Dos columnas de la MISMA seccion, en niveles distintos.
        m.Barras.Add(new BarraJson
        {
            Etiqueta = "C1", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            Seccion = Rect("K 30X60", 30, 60)
        });
        m.Barras.Add(new BarraJson
        {
            Etiqueta = "C2", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(5, 0, 0), P2 = P(5, 0, 3),
            Seccion = Rect("K 30X60", 30, 60)
        });

        // Una trabe con la MISMA propiedad que las columnas: misma seccion, otra clase.
        m.Barras.Add(new BarraJson
        {
            Etiqueta = "T1", Clase = ClasePieza.Trabe, Nivel = "Story1",
            P1 = P(0, 0, 3), P2 = P(5, 0, 3),
            Seccion = Rect("K 30X60", 30, 60)
        });

        // Una trabe de acero.
        m.Barras.Add(new BarraJson
        {
            Etiqueta = "T2", Clase = ClasePieza.Trabe, Nivel = "Story1",
            P1 = P(0, 5, 3), P2 = P(5, 5, 3),
            Seccion = PerfilI("IPR 356X171", 17.1, 35.6)
        });

        // Una diagonal.
        m.Barras.Add(new BarraJson
        {
            Etiqueta = "D1", Clase = ClasePieza.Diagonal, Nivel = "Story1",
            P1 = P(0, 0, 0), P2 = P(5, 0, 3),
            Seccion = Rect("DIAG 20X20", 20, 20)
        });

        // Un muro y una losa.
        var muro = new PanoJson
        {
            Etiqueta = "M1", Clase = ClasePieza.Muro, Nivel = "Story1",
            Seccion = new SeccionJson
            {
                Nombre = "MURO 20", Forma = FormaSeccion.Pano, EspesorM = 0.20, Material = "CONC"
            }
        };
        muro.Vertices.Add(P(0, 0, 0));
        muro.Vertices.Add(P(5, 0, 0));
        muro.Vertices.Add(P(5, 0, 3));
        muro.Vertices.Add(P(0, 0, 3));
        m.Panos.Add(muro);

        var losa = new PanoJson
        {
            Etiqueta = "L1", Clase = ClasePieza.Losa, Nivel = "Story1",
            Seccion = new SeccionJson
            {
                Nombre = "LOSA 12", Forma = FormaSeccion.Pano, EspesorM = 0.12, Material = "CONC"
            }
        };
        losa.Vertices.Add(P(0, 0, 3));
        losa.Vertices.Add(P(5, 0, 3));
        losa.Vertices.Add(P(5, 5, 3));
        losa.Vertices.Add(P(0, 5, 3));
        m.Panos.Add(losa);

        return m;
    }

    /// <summary>Un catalogo de Revit parecido al de una plantilla de verdad.</summary>
    private static CatalogoRevit CatalogoDePrueba()
    {
        var c = new CatalogoRevit();
        var id = 1000L;

        void T(CategoriaRevit cat, string fam, string tipo, double? a, double? p,
               double? e = null, FormaSeccion? forma = null, bool sistema = false)
        {
            c.Tipos.Add(new TipoRevit
            {
                Categoria = cat, Familia = fam, Tipo = tipo,
                AnchoM = a, PeralteM = p, EspesorM = e, Forma = forma,
                EsSistema = sistema, Id = id++
            });
        }

        // Columnas de hormigon
        T(CategoriaRevit.ColumnaEstructural, "Hormigón-Rectangular-Pilar", "300 x 450", 0.30, 0.45);
        T(CategoriaRevit.ColumnaEstructural, "Hormigón-Rectangular-Pilar", "300 x 600", 0.30, 0.60);
        T(CategoriaRevit.ColumnaEstructural, "Hormigón-Rectangular-Pilar", "450 x 450", 0.45, 0.45);
        T(CategoriaRevit.ColumnaEstructural, "Hormigón-Redondo-Pilar", "300 mm", 0.30, 0.30);
        T(CategoriaRevit.ColumnaEstructural, "H_Perfiles de ala ancha-Pilar", "HE100A", 0.10, 0.096);

        // Vigas
        T(CategoriaRevit.Estructura, "Hormigón-Viga rectangular", "300 x 600", 0.30, 0.60);
        T(CategoriaRevit.Estructura, "Hormigón-Viga rectangular", "200 x 400", 0.20, 0.40);
        T(CategoriaRevit.Estructura, "H_Perfiles de ala ancha", "HE100A", 0.10, 0.096);
        T(CategoriaRevit.Estructura, "H_Perfiles de ala ancha", "IPR 356X171", 0.171, 0.356);
        T(CategoriaRevit.Estructura, "Canal_C", "CE 150", 0.05, 0.15);

        // Muros y pisos: familias de sistema
        T(CategoriaRevit.Muro, "Muro básico", "Hormigón 200 mm", null, null, 0.20,
            null, sistema: true);
        T(CategoriaRevit.Muro, "Muro básico", "Hormigón 300 mm", null, null, 0.30,
            null, sistema: true);
        T(CategoriaRevit.Piso, "Suelo", "Losa 120 mm", null, null, 0.12, null, sistema: true);
        T(CategoriaRevit.Piso, "Suelo", "Losa 200 mm", null, null, 0.20, null, sistema: true);

        c.Niveles.Add(new NivelJson { Nombre = "Nivel 1", ElevacionM = 0 });
        c.Niveles.Add(new NivelJson { Nombre = "Nivel 2", ElevacionM = 3 });

        return c;
    }

    // ==================================================================
    private static int Main()
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" Prueba del nucleo del complemento de Revit");
        Console.WriteLine("============================================================");

        Json();
        InventarioDeSecciones();
        FormasPorNombre();
        Emparejar();
        MapeoYArchivo();
        PlanDeModelado();
        NivelesResueltos();
        Cuadro();

        Console.WriteLine();
        Console.WriteLine("============================================================");

        if (Fallos.Count > 0)
        {
            Console.WriteLine($" FALLARON {Fallos.Count}:");

            foreach (var f in Fallos)
            {
                Console.WriteLine("   - " + f);
            }

            Console.WriteLine("============================================================");

            return 1;
        }

        Console.WriteLine(" TODAS LAS COMPROBACIONES PASARON");
        Console.WriteLine("============================================================");

        return 0;
    }

    // ------------------------------------------------------------------
    private static void Json()
    {
        Console.WriteLine("\n[1] El archivo de intercambio");

        var m = ModeloDePrueba();
        var texto = ArchivoModelo.ATexto(m);

        Check("los acentos se escriben legibles, no como \\u00d1",
            texto.Contains("ÑOÑO") && !texto.Contains("\\u00d1"),
            texto.Contains("\\u00d1") ? "salio escapado" : "no aparece la obra");

        Check("las enumeraciones van por nombre, no por numero",
            texto.Contains("\"Columna\"") && !texto.Contains("\"Clase\": 0"));

        var v = ArchivoModelo.DeTexto(texto);

        Igual("vuelven las mismas barras", v.Barras.Count, m.Barras.Count);
        Igual("vuelven los mismos panos", v.Panos.Count, m.Panos.Count);
        Igual("vuelven los mismos niveles", v.Niveles.Count, m.Niveles.Count);
        Igual("la obra con Ñ sobrevive", v.Obra, "Edificio ÑOÑO");
        Igual("la clase sobrevive", v.Barras[0].Clase, ClasePieza.Columna);
        Igual("la forma sobrevive", v.Barras[3].Seccion.Forma, FormaSeccion.PerfilI);
        Igual("las coordenadas sobreviven", v.Barras[0].P2.Z, 3.0);
        Igual("los vertices del muro sobreviven", v.Panos[0].Vertices.Count, 4);
        Igual("la cuenta de piezas", v.Piezas, 7);

        // Una version futura se rechaza, y con un mensaje que dice que hacer.
        var futuro = texto.Replace("\"Version\": 1", "\"Version\": 99");
        var rechazo = string.Empty;

        try
        {
            ArchivoModelo.DeTexto(futuro);
        }
        catch (InvalidDataException e)
        {
            rechazo = e.Message;
        }

        Check("un archivo de version futura se rechaza", rechazo.Length > 0);
        Check("y el mensaje dice que actualizar el complemento",
            rechazo.Contains("Actualiza el complemento"), rechazo);

        // Un JSON roto da un mensaje util, no una excepcion cruda.
        var roto = string.Empty;

        try
        {
            ArchivoModelo.DeTexto("{ esto no es json ");
        }
        catch (InvalidDataException e)
        {
            roto = e.Message;
        }

        Check("un JSON roto se explica", roto.Contains("no es un JSON valido"), roto);

        // Y el ida y vuelta por disco, con el temporal.
        var tmp = Path.Combine(Path.GetTempPath(), "cadlink-prueba" + ArchivoModelo.Extension);
        ArchivoModelo.Guardar(m, tmp);

        Check("se escribe el archivo", File.Exists(tmp));
        Check("y no queda el temporal", !File.Exists(tmp + ".tmp"));

        var leido = ArchivoModelo.Leer(tmp);
        Igual("lo leido del disco cuadra", leido.Piezas, 7);

        File.Delete(tmp);
    }

    // ------------------------------------------------------------------
    private static void InventarioDeSecciones()
    {
        Console.WriteLine("\n[2] Inventario de secciones");

        var inv = Inventario.De(ModeloDePrueba());

        // K 30X60 sale DOS veces: una como columna y otra como trabe. Son dos filas porque
        // van a categorias distintas de Revit.
        var k = inv.Where(s => s.Seccion.Nombre == "K 30X60").ToList();

        Igual("la misma propiedad usada como columna y trabe da dos filas", k.Count, 2);
        Check("una es columna y la otra trabe",
            k.Any(s => s.Clase == ClasePieza.Columna) && k.Any(s => s.Clase == ClasePieza.Trabe));

        var kCol = k.First(s => s.Clase == ClasePieza.Columna);
        Igual("la columna cuenta sus dos piezas", kCol.Cuantas, 2);
        Igual("la trabe cuenta una", k.First(s => s.Clase == ClasePieza.Trabe).Cuantas, 1);

        Igual("la etiqueta lleva la clase entre parentesis", kCol.Etiqueta, "K 30X60 (columna)");
        Igual("las medidas en centimetros", kCol.Medidas, "30 x 60 cm");
        Igual("la categoria de una columna", kCol.Categoria, CategoriaRevit.ColumnaEstructural);

        var muro = inv.First(s => s.Clase == ClasePieza.Muro);
        Igual("un muro ensena su espesor", muro.Medidas, "e = 20 cm");
        Igual("y va a la categoria de muros", muro.Categoria, CategoriaRevit.Muro);

        Igual("en total hay seis secciones distintas", inv.Count, 6);

        // Dos propiedades con el mismo nombre y distinto peralte NO se funden.
        var m2 = new ModeloJson();
        m2.Niveles.Add(new NivelJson { Nombre = "N", ElevacionM = 0 });
        m2.Barras.Add(new BarraJson
        {
            Etiqueta = "A", Clase = ClasePieza.Trabe, Nivel = "N",
            P1 = P(0, 0, 0), P2 = P(1, 0, 0), Seccion = Rect("V", 20, 40)
        });
        m2.Barras.Add(new BarraJson
        {
            Etiqueta = "B", Clase = ClasePieza.Trabe, Nivel = "N",
            P1 = P(0, 1, 0), P2 = P(1, 1, 0), Seccion = Rect("V", 20, 50)
        });

        Igual("dos propiedades con el mismo nombre y otra medida no se funden",
            Inventario.De(m2).Count, 2);

        // El orden es estable: dos corridas dan lo mismo.
        var a = string.Join(",", Inventario.De(ModeloDePrueba()).Select(s => s.Clave));
        var b = string.Join(",", Inventario.De(ModeloDePrueba()).Select(s => s.Clave));
        Check("el orden del inventario es estable", a == b);
    }

    // ------------------------------------------------------------------
    private static void FormasPorNombre()
    {
        Console.WriteLine("\n[3] Deducir la forma del nombre de una familia");

        var casos = new (string Nombre, FormaSeccion? Esperado)[]
        {
            ("Hormigón-Rectangular-Pilar", FormaSeccion.Rectangulo),
            ("Hormigón-Viga rectangular", FormaSeccion.Rectangulo),
            ("Concreto rectangular", FormaSeccion.Rectangulo),
            ("Hormigón-Redondo-Pilar", FormaSeccion.Circulo),
            ("Columna circular", FormaSeccion.Circulo),
            ("H_Perfiles de ala ancha", FormaSeccion.PerfilI),
            ("W-Wide Flange", FormaSeccion.PerfilI),
            ("IPR 356X171", FormaSeccion.PerfilI),
            ("HE100A", FormaSeccion.PerfilI),
            ("W18X50", FormaSeccion.PerfilI),
            ("HSS3X2X1/4", FormaSeccion.Cajon),
            ("PTR 100x100", FormaSeccion.Cajon),
            ("Tubo rectangular 100", FormaSeccion.Cajon),
            ("Tubo redondo 168", FormaSeccion.Tubo),
            ("Pipe 6 in", FormaSeccion.Tubo),
            ("Canal_C", FormaSeccion.PerfilC),
            ("CE 150", FormaSeccion.PerfilC),
            ("Ángulo de lados iguales", FormaSeccion.PerfilL),
            ("LI 102x102x9.5", FormaSeccion.PerfilL),
            ("Tee estructural", FormaSeccion.PerfilT),
            ("Mi familia rarísima", null),
            ("", null),
            (null!, null)
        };

        foreach (var (nombre, esperado) in casos)
        {
            var dio = Sugeridor.FormaPorNombre(nombre);
            Check($"«{nombre}» -> {esperado?.ToString() ?? "no se sabe"}", dio == esperado,
                "dio " + (dio?.ToString() ?? "null"));
        }

        // Lo especifico gana a lo general: "tubular rectangular" contiene "rectangular".
        Igual("«Tubular rectangular» es cajon, no rectangulo",
            Sugeridor.FormaPorNombre("Tubular rectangular"), FormaSeccion.Cajon);

        // Una sigla corta no debe saltar dentro de otra palabra.
        Igual("«Hormigón» no se confunde con un perfil HE",
            Sugeridor.FormaPorNombre("Hormigón"), FormaSeccion.Rectangulo);

        Console.WriteLine("\n[3a] Sacar las medidas del nombre de un tipo de Revit");

        // Con unidad escrita o con numeros que solo pueden ser milimetros: se contesta.
        Igual("«300 x 450» son milimetros", MedidasPorNombre.Dos("300 x 450"), (0.300, 0.450));
        Igual("«300x600» tambien", MedidasPorNombre.Dos("300x600"), (0.300, 0.600));
        Igual("«300 × 450» con la x de multiplicar",
            MedidasPorNombre.Dos("300 × 450"), (0.300, 0.450));
        Igual("«20 x 40 cm» respeta la unidad", MedidasPorNombre.Dos("20 x 40 cm"), (0.20, 0.40));
        Igual("«0.3 x 0.6 m» tambien", MedidasPorNombre.Dos("0.3 x 0.6 m"), (0.3, 0.6));
        Igual("«Hormigón 200 mm» da un espesor", MedidasPorNombre.Una("Hormigón 200 mm"), 0.200);
        Igual("«Losa 120 mm» tambien", MedidasPorNombre.Una("Losa 120 mm"), 0.120);

        // En la duda, NO se adivina.
        Igual("«12 x 18» sin unidad no se adivina", MedidasPorNombre.Dos("12 x 18"), null);
        Igual("«HSS3X2X1/4» en pulgadas no se adivina",
            MedidasPorNombre.Dos("HSS3X2X1/4"), null);
        Igual("«W18X50» no son medidas en mm", MedidasPorNombre.Dos("W18X50"), null);
        Igual("un nombre sin numeros no da nada", MedidasPorNombre.Dos("Genérico"), null);
        Igual("ni un nombre vacio", MedidasPorNombre.Dos(""), null);
        Igual("ni nulo", MedidasPorNombre.Dos(null), null);

        // Un nombre con dos medidas NO es un espesor.
        Igual("«300 x 450» no es un espesor", MedidasPorNombre.Una("300 x 450"), null);

        Console.WriteLine("\n[3b] Interpretar la forma que REVIT declara del perfil");

        // Estos son los nombres de StructuralSectionShape, que es lo fiable: Revit sabe de
        // verdad que forma tiene el perfil. Llegan como texto para no escribir en el codigo
        // el nombre de ningun miembro del enum, que cambia entre versiones.
        var deRevit = new (string Nombre, FormaSeccion? Esperado)[]
        {
            ("RectangularBar", FormaSeccion.Rectangulo),
            ("RoundBar", FormaSeccion.Circulo),
            ("IWideFlange", FormaSeccion.PerfilI),
            ("IParallelFlange", FormaSeccion.PerfilI),
            ("IWelded", FormaSeccion.PerfilI),
            ("ISplitTee", FormaSeccion.PerfilT),
            ("CChannel", FormaSeccion.PerfilC),
            ("CParallelFlange", FormaSeccion.PerfilC),
            ("LAngle", FormaSeccion.PerfilL),
            ("PipeStandard", FormaSeccion.Tubo),
            ("RoundHSS", FormaSeccion.Tubo),
            ("RectangleHSS", FormaSeccion.Cajon),
            ("RectangularHSS", FormaSeccion.Cajon),
            ("SquareHSS", FormaSeccion.Cajon),
            ("Rectangle HSS", FormaSeccion.Cajon),
            ("rectangle_hss", FormaSeccion.Cajon),
            ("NotDefined", null),
            ("Other", null),
            ("AlgoQueNoExiste", null),
            ("", null),
            (null!, null)
        };

        foreach (var (nombre, esperado) in deRevit)
        {
            var dio = FormasDeRevit.Interpretar(nombre);
            Check($"Revit dice «{nombre}» -> {esperado?.ToString() ?? "no se sabe"}",
                dio == esperado, "dio " + (dio?.ToString() ?? "null"));
        }

        // Las dos trampas del orden, explicitas.
        Igual("ISplitTee es una te aunque empiece por I",
            FormasDeRevit.Interpretar("ISplitTee"), FormaSeccion.PerfilT);
        Igual("RectangleHSS es un cajon aunque diga Rectangle",
            FormasDeRevit.Interpretar("RectangleHSS"), FormaSeccion.Cajon);
    }

    // ------------------------------------------------------------------
    private static void Emparejar()
    {
        Console.WriteLine("\n[4] Emparejar secciones con las familias que hay en Revit");

        var cat = CatalogoDePrueba();
        var inv = Inventario.De(ModeloDePrueba());

        var kCol = inv.First(s => s.Seccion.Nombre == "K 30X60" && s.Clase == ClasePieza.Columna);
        var sCol = Sugeridor.Para(kCol, cat);

        Check("una columna de 30x60 encuentra tipo", sCol.Tipo is not null, sCol.Porque);
        Igual("y es el de 300 x 600", sCol.Tipo?.Tipo, "300 x 600");
        Igual("de la familia de pilares rectangulares",
            sCol.Tipo?.Familia, "Hormigón-Rectangular-Pilar");
        Check("la sugerencia es buena", sCol.EsBuena, "puntos=" + sCol.Puntos);
        Check("y explica por que", sCol.Porque.Length > 0, sCol.Porque);

        // La MISMA seccion como trabe tiene que ir a una familia de vigas, no de pilares.
        var kTrabe = inv.First(s => s.Seccion.Nombre == "K 30X60" && s.Clase == ClasePieza.Trabe);
        var sTrabe = Sugeridor.Para(kTrabe, cat);

        Igual("la misma seccion como trabe va a la categoria Estructura",
            sTrabe.Tipo?.Categoria, CategoriaRevit.Estructura);
        Igual("y a la familia de vigas", sTrabe.Tipo?.Familia, "Hormigón-Viga rectangular");

        // Un perfil de acero por nombre exacto.
        var ipr = inv.First(s => s.Seccion.Nombre == "IPR 356X171");
        var sIpr = Sugeridor.Para(ipr, cat);

        Igual("un IPR encuentra su tipo por nombre y medidas", sIpr.Tipo?.Tipo, "IPR 356X171");
        Check("la sugerencia del IPR es buena", sIpr.EsBuena, "puntos=" + sIpr.Puntos);

        // Un muro por espesor.
        var muro = inv.First(s => s.Clase == ClasePieza.Muro);
        var sMuro = Sugeridor.Para(muro, cat);

        Igual("un muro de 20 cm encuentra el de Hormigón 200 mm",
            sMuro.Tipo?.Tipo, "Hormigón 200 mm");
        Check("y es familia de sistema", sMuro.Tipo?.EsSistema == true);

        var losa = inv.First(s => s.Clase == ClasePieza.Losa);
        Igual("una losa de 12 cm encuentra la de 120 mm",
            Sugeridor.Para(losa, cat).Tipo?.Tipo, "Losa 120 mm");

        // Las medidas al reves tambien valen: 60x30 debe encontrar el tipo 300x600.
        var alReves = new SeccionDelModelo
        {
            Clave = "x", Clase = ClasePieza.Columna,
            Seccion = Rect("K 60X30", 60, 30)
        };

        Igual("una seccion con las medidas invertidas encuentra el mismo tipo",
            Sugeridor.Para(alReves, cat).Tipo?.Tipo, "300 x 600");

        // Una forma que no existe en el catalogo no debe emparejarse con otra distinta.
        var canalCol = new SeccionDelModelo
        {
            Clave = "y", Clase = ClasePieza.Columna,
            Seccion = new SeccionJson
            {
                Nombre = "CANAL", Forma = FormaSeccion.PerfilC,
                AnchoM = 0.05, PeralteM = 0.15
            }
        };

        var sCanal = Sugeridor.Para(canalCol, cat);
        Check("un canal como columna no se acepta como rectangular ni como I",
            !sCanal.EsBuena, $"propuso {sCanal.Tipo?.NombreCompleto} con {sCanal.Puntos}");

        // Sin candidatos en la categoria: lo dice, no revienta.
        var vacio = new CatalogoRevit();
        var sVacio = Sugeridor.Para(kCol, vacio);

        Check("sin familias cargadas no propone nada", sVacio.Tipo is null);
        Check("y explica que hay que cargar una familia",
            sVacio.Porque.Contains("Carga una familia"), sVacio.Porque);

        // Determinismo: la misma entrada, la misma salida.
        var uno = Sugeridor.Para(kCol, CatalogoDePrueba()).Tipo?.NombreCompleto;
        var dos = Sugeridor.Para(kCol, CatalogoDePrueba()).Tipo?.NombreCompleto;
        Igual("el emparejador es determinista", uno, dos);

        // Y una forma medio parecida: un cajon puede caer en un rectangulo del mismo tamano.
        Check("un cajon y un rectangulo se consideran parientes",
            Sugeridor.MismaFamiliaDeForma(FormaSeccion.Cajon, FormaSeccion.Rectangulo));
        Check("un tubo y un circulo tambien",
            Sugeridor.MismaFamiliaDeForma(FormaSeccion.Tubo, FormaSeccion.Circulo));
        Check("pero una I y un angulo no",
            !Sugeridor.MismaFamiliaDeForma(FormaSeccion.PerfilI, FormaSeccion.PerfilL));
    }

    // ------------------------------------------------------------------
    private static void MapeoYArchivo()
    {
        Console.WriteLine("\n[5] El mapeo y su archivo");

        var cat = CatalogoDePrueba();
        var inv = Inventario.De(ModeloDePrueba());
        var kCol = inv.First(s => s.Clase == ClasePieza.Columna);

        var m = new Mapeo { Obra = "Edificio ÑOÑO" };
        var tipo = cat.DeCategoria(CategoriaRevit.ColumnaEstructural)
                      .First(t => t.Tipo == "300 x 600");

        m.Poner(kCol, tipo);

        Check("el mapeo recuerda la eleccion", m.Tiene(kCol.Clave));
        Igual("y devuelve el tipo", m.TipoDe(kCol, cat)?.Tipo, "300 x 600");
        Igual("una clave que no esta no tiene tipo", m.TipoDe(
            inv.First(s => s.Clase == ClasePieza.Losa), cat), null);

        var texto = ArchivoMapeo.ATexto(m.AGuardado());
        var vuelta = Mapeo.DeGuardado(ArchivoMapeo.DeTexto(texto));

        Igual("el mapeo sobrevive el ida y vuelta", vuelta.TipoDe(kCol, cat)?.Tipo, "300 x 600");
        Igual("y la obra tambien", vuelta.Obra, "Edificio ÑOÑO");

        // Dos guardados del mismo mapeo dan el mismo texto salvo la fecha: se comprueba que
        // las filas van ordenadas.
        var m2 = Mapeo.DeGuardado(ArchivoMapeo.DeTexto(texto));
        var t1 = string.Join(",", m.AGuardado().Filas.Select(f => f.Clave));
        var t2 = string.Join(",", m2.AGuardado().Filas.Select(f => f.Clave));
        Igual("las filas se guardan ordenadas", t1, t2);

        // Reatar con un catalogo donde el tipo ya no existe.
        var flaco = new CatalogoRevit();
        flaco.Tipos.Add(new TipoRevit
        {
            Categoria = CategoriaRevit.ColumnaEstructural,
            Familia = "Otra familia", Tipo = "Otro tipo", Id = 5
        });

        var perdidas = Mapeo.DeGuardado(ArchivoMapeo.DeTexto(texto)).Reatar(flaco);

        Igual("un tipo que ya no existe se reporta como perdido", perdidas.Count, 1);
        Igual("y dice cual era", perdidas[0].Tipo, "300 x 600");

        // Reatar contra un catalogo con los MISMOS nombres pero otros id: debe funcionar,
        // porque un mapeo se reusa en otro proyecto y ahi los id son distintos.
        var otro = CatalogoDePrueba();

        foreach (var t in otro.Tipos)
        {
            t.Id += 9000;
        }

        var reatado = Mapeo.DeGuardado(ArchivoMapeo.DeTexto(texto));
        var perdidas2 = reatado.Reatar(otro);

        Igual("con otros id pero los mismos nombres no se pierde nada", perdidas2.Count, 0);
        Check("y el id se actualiza al del proyecto nuevo",
            reatado.De(kCol.Clave)!.TipoId > 9000,
            reatado.De(kCol.Clave)!.TipoId.ToString());

        // La ruta del mapeo, con la extension doble del modelo.
        Igual("la ruta del mapeo quita la extension doble",
            Path.GetFileName(ArchivoMapeo.RutaPara("/obra/Torre" + ArchivoModelo.Extension)),
            "Torre" + ArchivoMapeo.Extension);

        // Si no hay archivo, se devuelve un mapeo vacio y no un error.
        var sinArchivo = ArchivoMapeo.LeerOVacio(
            Path.Combine(Path.GetTempPath(), "no-existe" + ArchivoMapeo.Extension));

        Igual("no tener mapeo guardado no es un error", sinArchivo.Cuantas, 0);
    }

    // ------------------------------------------------------------------
    private static void PlanDeModelado()
    {
        Console.WriteLine("\n[6] El plan: que crear, que actualizar, que dejar");

        var cat = CatalogoDePrueba();
        var modelo = ModeloDePrueba();
        var vista = new VistaMapeo(modelo, cat);
        vista.AceptarLoMasParecido();
        var mapeo = vista.AMapeo();

        // Primera importacion: no hay nada en Revit.
        var plan1 = Planificador.Armar(modelo, mapeo, cat, null, Modo.ModelarNuevos);

        Igual("la primera vez se crean todas las piezas mapeadas",
            plan1.Crear, plan1.Pasos.Count(p => p.Accion != Accion.SinMapeo));
        Igual("no hay nada que actualizar", plan1.Actualizar, 0);
        Igual("ni nada que sobre", plan1.Sobra, 0);
        Check("hay trabajo", plan1.HayTrabajo);
        Check("el resumen se entiende", plan1.Resumen().Contains("crear"), plan1.Resumen());

        // Segunda importacion: ya estan todas, con el tipo correcto.
        var existentes = plan1.Pasos
            .Where(p => p.Accion == Accion.Crear)
            .Select((p, i) => new PiezaExistente
            {
                Id = 5000 + i,
                Llave = p.Llave,
                TipoId = p.Tipo!.Id,
                P1 = p.Barra?.P1,
                P2 = p.Barra?.P2
            })
            .ToList();

        var plan2 = Planificador.Armar(modelo, mapeo, cat, existentes, Modo.ModelarNuevos);

        Igual("la segunda vez no se crea nada", plan2.Crear, 0);
        Igual("y todo se deja igual", plan2.DejarIgual, existentes.Count);
        Check("no hay trabajo", !plan2.HayTrabajo);

        // Cambio de seccion: se re-tipa, pero SOLO en modo actualizar.
        var cambiado = existentes.Select(e => new PiezaExistente
        {
            Id = e.Id, Llave = e.Llave, TipoId = 1, P1 = e.P1, P2 = e.P2
        }).ToList();

        var planNuevos = Planificador.Armar(modelo, mapeo, cat, cambiado, Modo.ModelarNuevos);
        Igual("en modo «modelar nuevos» lo que ya esta NO se toca", planNuevos.Actualizar, 0);
        Check("pero se dice que habia cambios",
            planNuevos.Pasos.Any(p => p.Motivo.Contains("solo modelar nuevos")));

        var planActualiza = Planificador.Armar(modelo, mapeo, cat, cambiado, Modo.ActualizarExistentes);
        Igual("en modo «actualizar existentes» se re-tipan todas",
            planActualiza.Actualizar, cambiado.Count);
        Igual("y no se crea nada nuevo", planActualiza.Crear, 0);
        Check("el motivo dice que cambia el tipo",
            planActualiza.Pasos.Any(p => p.Motivo.Contains("cambia el tipo")));

        // Una pieza movida en el modelo de calculo.
        var movida = existentes.Select(e => new PiezaExistente
        {
            Id = e.Id, Llave = e.Llave, TipoId = e.TipoId,
            P1 = e.P1 is null ? null : P(e.P1.X + 1, e.P1.Y, e.P1.Z),
            P2 = e.P2
        }).ToList();

        var planMovida = Planificador.Armar(modelo, mapeo, cat, movida, Modo.ActualizarExistentes);
        Check("una pieza movida se detecta",
            planMovida.Pasos.Any(p => p.Motivo.Contains("se movio")),
            "actualizar=" + planMovida.Actualizar);

        // Un movimiento por debajo de la tolerancia NO cuenta.
        var casi = existentes.Select(e => new PiezaExistente
        {
            Id = e.Id, Llave = e.Llave, TipoId = e.TipoId,
            P1 = e.P1 is null ? null : P(e.P1.X + 0.001, e.P1.Y, e.P1.Z),
            P2 = e.P2
        }).ToList();

        Igual("un milimetro no cuenta como movimiento",
            Planificador.Armar(modelo, mapeo, cat, casi, Modo.ActualizarExistentes).Actualizar, 0);

        // Una pieza que ya no esta en el modelo: sobra, y se informa sin borrar.
        var sobra = existentes.ToList();
        sobra.Add(new PiezaExistente
        {
            Id = 9999, Llave = Llave.De(ClasePieza.Columna, "BORRADA", "Story1"), TipoId = 1
        });

        var planSobra = Planificador.Armar(modelo, mapeo, cat, sobra, Modo.ModelarNuevos);
        Igual("una pieza que ya no esta en el modelo se reporta", planSobra.Sobra, 1);
        Check("con su id, para poder buscarla",
            planSobra.Pasos.Any(p => p.Accion == Accion.Sobra && p.IdExistente == 9999));

        // Piezas ajenas: si el comentario no es nuestro, se ignora.
        var ajena = new List<PiezaExistente>
        {
            new() { Id = 1, Llave = "Puesta a mano por el arquitecto", TipoId = 1 }
        };

        Igual("una pieza sin marca de CadLink no se toca",
            Planificador.Armar(modelo, mapeo, cat, ajena, Modo.ModelarNuevos).Sobra, 0);

        // Sin mapeo: la seccion se salta y se dice.
        var vacio = new Mapeo();
        var planSinMapeo = Planificador.Armar(modelo, vacio, cat, null, Modo.ModelarNuevos);

        Igual("sin mapeo no se crea nada", planSinMapeo.Crear, 0);
        Igual("y todas las piezas quedan como sin mapeo", planSinMapeo.SinMapeo, modelo.Piezas);

        // Dos piezas con la misma etiqueta y nivel: se avisa.
        var repe = ModeloDePrueba();
        repe.Barras.Add(new BarraJson
        {
            Etiqueta = "C1", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(9, 9, 0), P2 = P(9, 9, 3), Seccion = Rect("K 30X60", 30, 60)
        });

        var planRepe = Planificador.Armar(repe, mapeo, cat, null, Modo.ModelarNuevos);
        Check("dos piezas con la misma etiqueta y nivel dan aviso",
            planRepe.Avisos.Any(a => a.Contains("misma etiqueta")),
            string.Join(" | ", planRepe.Avisos));

        // La llave lleva el nivel: "C1" en dos niveles son dos piezas.
        Check("la llave distingue el mismo nombre en dos niveles",
            Llave.De(ClasePieza.Columna, "C1", "Story1")
            != Llave.De(ClasePieza.Columna, "C1", "Story2"));

        Check("y se reconoce como nuestra",
            Llave.EsNuestra(Llave.De(ClasePieza.Columna, "C1", "Story1")));
        Check("mientras un comentario cualquiera no",
            !Llave.EsNuestra("revisar con el calculista"));
    }

    // ------------------------------------------------------------------
    private static void NivelesResueltos()
    {
        Console.WriteLine("\n[7] Emparejar niveles con los de Revit");

        var modelo = ModeloDePrueba();   // Base en 0, Story1 en 3
        var cat = CatalogoDePrueba();    // Nivel 1 en 0, Nivel 2 en 3

        var n = Niveles.Resolver(modelo, cat);

        Igual("Base cae en Nivel 1 por su cota", n.Nombre("Base"), "Nivel 1");
        Igual("Story1 cae en Nivel 2 por su cota", n.Nombre("Story1"), "Nivel 2");
        Igual("no falta ninguno", n.Faltan.Count, 0);
        Check("la explicacion ensena las parejas",
            n.Explicacion().Contains("Base -> Nivel 1"), n.Explicacion());

        // Con el mismo nombre, manda el nombre y no la cota.
        var cat2 = new CatalogoRevit();
        cat2.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 99 });

        Igual("si el nombre coincide, gana el nombre", Niveles.Resolver(modelo, cat2).Nombre("Story1"),
            "Story1");

        // Un nivel sin pareja se reporta.
        var cat3 = new CatalogoRevit();
        cat3.Niveles.Add(new NivelJson { Nombre = "Solo uno", ElevacionM = 0 });

        var n3 = Niveles.Resolver(modelo, cat3);
        Igual("el nivel sin pareja se reporta", n3.Faltan.Count, 1);
        Igual("y es el que falta", n3.Faltan[0].Nombre, "Story1");

        // La tolerancia de 10 cm.
        var cat4 = new CatalogoRevit();
        cat4.Niveles.Add(new NivelJson { Nombre = "Casi", ElevacionM = 3.05 });
        Igual("5 cm de diferencia se consideran el mismo nivel",
            Niveles.Resolver(modelo, cat4).Nombre("Story1"), "Casi");

        var cat5 = new CatalogoRevit();
        cat5.Niveles.Add(new NivelJson { Nombre = "Lejos", ElevacionM = 3.5 });

        var n5 = Niveles.Resolver(modelo, cat5);

        // Se comprueba que Story1 queda sin pareja, no el total: en este catalogo Base
        // tampoco empareja -su cota es 0 y el unico nivel esta a 3.5-, asi que faltan los
        // dos. Contar el total habria hecho pasar la prueba por el motivo equivocado.
        Check("50 cm ya no se considera el mismo nivel",
            n5.Faltan.Any(f => f.Nombre == "Story1"),
            "faltan: " + string.Join(", ", n5.Faltan.Select(f => f.Nombre)));

        Igual("y Story1 se queda con su propio nombre", n5.Nombre("Story1"), "Story1");
    }

    // ------------------------------------------------------------------
    private static void Cuadro()
    {
        Console.WriteLine("\n[8] El comportamiento del cuadro de mapeo");

        var cat = CatalogoDePrueba();
        var modelo = ModeloDePrueba();
        var v = new VistaMapeo(modelo, cat);

        Igual("hay una fila por seccion", v.Total, 6);
        Check("las que tienen sugerencia buena vienen puestas", v.Mapeadas > 0,
            v.Mapeadas.ToString());
        Check("y se marcan como sugerencia", v.Sugeridas > 0, v.Sugeridas.ToString());
        Check("el resumen se entiende", v.Resumen().Contains("seccion(es) con tipo elegido"),
            v.Resumen());

        var fila = v.Filas.First(f => f.Seccion.Seccion.Nombre == "K 30X60"
                                      && f.Seccion.Clase == ClasePieza.Columna);

        Igual("la fila trae las familias de SU categoria",
            fila.Familias.Count, cat.FamiliasDe(CategoriaRevit.ColumnaEstructural).Count);
        Check("la familia elegida esta entre las ofrecidas",
            fila.Familias.Contains(fila.Familia), fila.Familia);
        Check("los tipos son los de esa familia",
            fila.Tipos.All(t => t.Familia == fila.Familia));
        Check("hay explicacion de la sugerencia", fila.Sugerido.Length > 0, fila.Sugerido);

        // Cambiar de familia debe repoblar los tipos y descartar el que no pertenece.
        var otra = fila.Familias.First(f => f != fila.Familia);
        fila.Familia = otra;

        Check("al cambiar de familia se repueblan los tipos",
            fila.Tipos.All(t => t.Familia == otra), otra);
        Check("y el tipo de la familia anterior se descarta", fila.Tipo is null,
            fila.Tipo?.NombreCompleto ?? "null");
        Check("asi que la fila queda sin mapear", !fila.Mapeada);

        // Elegir a mano deja de ser sugerencia.
        fila.Tipo = fila.Tipos.First();
        Check("elegir a mano marca la fila como revisada", !fila.EsSugerencia);
        Check("y vuelve a estar mapeada", fila.Mapeada);

        // Una fila sin sugerencia buena se queda vacia pero con la pista puesta.
        var canal = new ModeloJson();
        canal.Niveles.Add(new NivelJson { Nombre = "N", ElevacionM = 0 });
        canal.Barras.Add(new BarraJson
        {
            Etiqueta = "X", Clase = ClasePieza.Columna, Nivel = "N",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            Seccion = new SeccionJson
            {
                Nombre = "CANAL RARO", Forma = FormaSeccion.PerfilC,
                AnchoM = 0.05, PeralteM = 0.15
            }
        });

        var vc = new VistaMapeo(canal, cat);

        Igual("una seccion sin buen candidato se deja sin mapear", vc.SinMapear, 1);
        Check("pero se dice lo mas parecido",
            vc.Filas[0].Sugerido.Contains("no convence")
            || vc.Filas[0].Sugerido.Contains("lo mas parecido"),
            vc.Filas[0].Sugerido);
        Check("y no se puede modelar nada", !vc.PuedeModelar);

        // Aceptar lo mas parecido es un acto explicito.
        var puestas = vc.AceptarLoMasParecido();
        Igual("aceptar lo mas parecido la pone", puestas, 1);
        Check("y ya se puede modelar", vc.PuedeModelar);

        // Un mapeo guardado MANDA sobre la sugerencia.
        var guardado = new Mapeo();
        var kCol = Inventario.De(modelo).First(s => s.Clase == ClasePieza.Columna);
        var elegido = cat.DeCategoria(CategoriaRevit.ColumnaEstructural)
                         .First(t => t.Tipo == "450 x 450");
        guardado.Poner(kCol, elegido);

        var v2 = new VistaMapeo(modelo, cat, guardado);
        var fila2 = v2.Filas.First(f => f.Seccion.Clave == kCol.Clave);

        Igual("lo guardado manda sobre lo sugerido", fila2.Tipo?.Tipo, "450 x 450");
        Check("y no se marca como sugerencia", !fila2.EsSugerencia);
        Check("se dice que viene de una importacion anterior",
            fila2.Sugerido.Contains("importacion anterior"), fila2.Sugerido);

        // Un mapeo guardado que apunta a un tipo que ya no existe se reporta.
        var flaco = new CatalogoRevit();
        flaco.Tipos.Add(new TipoRevit
        {
            Categoria = CategoriaRevit.ColumnaEstructural,
            Familia = "Hormigón-Rectangular-Pilar", Tipo = "300 x 600",
            AnchoM = 0.30, PeralteM = 0.60, Id = 1
        });

        var guardado2 = new Mapeo();
        guardado2.Poner(kCol, elegido);   // 450 x 450, que no esta en el catalogo flaco

        var v3 = new VistaMapeo(modelo, flaco, guardado2);
        Check("un tipo guardado que ya no existe se reporta",
            v3.PerdidasDelMapeo.Count == 1, string.Join(" | ", v3.PerdidasDelMapeo));

        // El mapeo que sale del cuadro sirve para el plan.
        var v4 = new VistaMapeo(modelo, cat);
        v4.AceptarLoMasParecido();
        var m4 = v4.AMapeo();

        Igual("el mapeo del cuadro tiene una fila por seccion mapeada", m4.Cuantas, v4.Mapeadas);
        Igual("y la obra viaja con el", m4.Obra, "Edificio ÑOÑO");
    }
}
