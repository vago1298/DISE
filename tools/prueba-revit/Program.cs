using System.Globalization;
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
internal static partial class Programa
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

    /// <summary>Compara dos numeros con tolerancia.</summary>
    /// <remarks>
    /// Hace falta para la geometria: recortar un muro al pano de un castillo es una cuenta con
    /// senos y cosenos, y comparar por igualdad exacta fallaria por el ultimo bit.
    /// </remarks>
    private static void Casi(string nombre, double dio, double esperado, double tol = 1e-9) =>
        Check(nombre, Math.Abs(dio - esperado) <= tol, $"esperaba {esperado} y dio {dio}");

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

    /// <summary>El trozo i de un muro mallado en seis: todos comparten pier.</summary>
    private static List<PuntoJson> Trozo(int i) => new()
    {
        P(i, 0, 0), P(i + 1, 0, 0), P(i + 1, 0, 3), P(i, 0, 3)
    };

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
        GiroDeLaSeccion();
        MuroAlPanoDeLoModelado();
        NombresBonitosDeNivel();
        MallaDeEjes();
        Armado();
        ArmadoPorTipo();
        DespieceEnRevit();

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

        // Una version futura se rechaza, y con un mensaje que dice que hacer. La version de
        // partida se lee de la constante y no se escribe a mano, para que subirla no rompa esta
        // prueba por un motivo que no tiene nada que ver con lo que comprueba.
        var futuro = texto.Replace(
            "\"Version\": " + ModeloJson.VersionActual.ToString(CultureInfo.InvariantCulture),
            "\"Version\": 99");

        Check("la sustitucion de version encontro su sitio", futuro != texto);
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

        Console.WriteLine("\n[2b] Muro o losa: la nota de la propiedad y la geometria");

        // Un contorno HORIZONTAL a la cota 3, como una losa.
        var horizontal = new List<PuntoJson>
            { P(0, 0, 3), P(5, 0, 3), P(5, 5, 3), P(0, 5, 3) };

        // Un contorno VERTICAL en el plano XZ, como un muro.
        var vertical = new List<PuntoJson>
            { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) };

        // El caso que se reporto: la nota dice LOSA pero el lector la trajo como MURO,
        // porque el lector decide solo por geometria. Tiene que salir LOSA.
        var r1 = ClasePano.De(horizontal, "LOSA", ClasePieza.Muro);
        Igual("nota LOSA y contorno horizontal, aunque el lector diga muro -> LOSA",
            r1.Clase, ClasePieza.Losa);
        Check("y sin aviso, porque nota y geometria coinciden", r1.Aviso is null, r1.Aviso ?? "");

        // LOSACERO cuenta como losa.
        Igual("LOSACERO tambien es losa",
            ClasePano.De(horizontal, "LOSACERO", ClasePieza.Muro).Clase, ClasePieza.Losa);

        // Un muro de verdad sigue siendo muro.
        Igual("nota MURO y contorno vertical -> MURO",
            ClasePano.De(vertical, "MURO", ClasePieza.Muro).Clase, ClasePieza.Muro);

        // La geometria manda cuando es clara, porque es la que decide si la llamada a Revit
        // puede funcionar: no existe un suelo vertical.
        var r2 = ClasePano.De(vertical, "LOSA", ClasePieza.Losa);
        Igual("nota LOSA pero contorno vertical -> MURO, porque no hay suelos verticales",
            r2.Clase, ClasePieza.Muro);
        Check("y se avisa de la contradiccion", r2.Aviso is not null, "no aviso");

        var r3 = ClasePano.De(horizontal, "MURO", ClasePieza.Muro);
        Igual("nota MURO pero contorno horizontal -> LOSA", r3.Clase, ClasePieza.Losa);
        Check("y tambien se avisa", r3.Aviso is not null, "no aviso");

        // Sin notas, decide la geometria.
        Igual("sin notas, un contorno horizontal es losa",
            ClasePano.De(horizontal, "", ClasePieza.Muro).Clase, ClasePieza.Losa);
        Igual("sin notas, un contorno vertical es muro",
            ClasePano.De(vertical, "", ClasePieza.Losa).Clase, ClasePieza.Muro);

        // Inclinado de verdad: la geometria no decide y hablan las notas.
        var rampa = new List<PuntoJson>
            { P(0, 0, 0), P(5, 0, 0), P(5, 5, 4), P(0, 5, 4) };   // unos 39 grados

        var incl = ClasePano.VerticalidadDe(rampa);
        Check("la rampa de prueba queda en la zona ambigua",
            incl is not null && Math.Abs(incl.Value) < ClasePano.CosHorizontal
                             && Math.Abs(incl.Value) > ClasePano.CosVertical,
            "verticalidad=" + incl);

        Igual("inclinado con nota de losa -> losa",
            ClasePano.De(rampa, "LOSA", ClasePieza.Muro).Clase, ClasePieza.Losa);
        Igual("inclinado con nota de muro -> muro",
            ClasePano.De(rampa, "MURO", ClasePieza.Losa).Clase, ClasePieza.Muro);
        Igual("inclinado y sin notas, se respeta lo del modelo",
            ClasePano.De(rampa, "", ClasePieza.Muro).Clase, ClasePieza.Muro);

        // Degenerados: no revientan.
        Igual("sin contorno deciden las notas",
            ClasePano.De(null, "LOSA", ClasePieza.Muro).Clase, ClasePieza.Losa);
        Igual("con dos vertices deciden las notas",
            ClasePano.De(new List<PuntoJson> { P(0, 0, 0), P(1, 0, 0) }, "MURO",
                ClasePieza.Losa).Clase, ClasePieza.Muro);
        Igual("un contorno en linea no da verticalidad",
            ClasePano.VerticalidadDe(new List<PuntoJson>
                { P(0, 0, 0), P(1, 0, 0), P(2, 0, 0) }), null);

        // Una losa a la cota cero, que es el caso de una cimentacion.
        Igual("una losa en la cota cero sigue siendo losa",
            ClasePano.De(new List<PuntoJson> { P(0, 0, 0), P(4, 0, 0), P(4, 4, 0), P(0, 4, 0) },
                "LOSA", ClasePieza.Muro).Clase, ClasePieza.Losa);

        // Y el orden de los vertices no debe cambiar la decision.
        var alReves = new List<PuntoJson>(horizontal);
        alReves.Reverse();
        Igual("el giro del contorno no cambia la clase",
            ClasePano.De(alReves, "LOSA", ClasePieza.Muro).Clase, ClasePieza.Losa);

        Console.WriteLine("\n[2d] Etiquetas: un modelo sin piers no puede colapsar los muros");

        // El caso del informe: el lector pone el PIER como etiqueta del muro, y sin piers
        // asignados todos quedan con la etiqueta vacia. La llave sale «CadLink|Muro||Story1»
        // para todos, y el planificador modela uno y descarta el resto.
        var m1 = new List<PuntoJson> { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) };
        var m2 = new List<PuntoJson> { P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3) };

        var e1 = Etiquetas.Estable("", m1);
        var e2 = Etiquetas.Estable("", m2);

        Check("sin etiqueta se deriva una de la posicion", e1.Length > 1, e1);
        Check("y dos muros distintos dan etiquetas distintas", e1 != e2, $"{e1} vs {e2}");
        Igual("la misma pieza da siempre la misma etiqueta", Etiquetas.Estable("", m1), e1);

        // Estable aunque se reordenen los vertices: eso permite reimportar y ACTUALIZAR.
        var revuelto = new List<PuntoJson> { m1[2], m1[3], m1[0], m1[1] };
        Igual("el orden de los vertices no cambia la etiqueta",
            Etiquetas.Estable("", revuelto), e1);

        Igual("si el modelo SI trae etiqueta, se respeta", Etiquetas.Estable("M1", m1), "M1");
        Check("un reajuste de milimetros no cambia la etiqueta",
            Etiquetas.Estable("", new List<PuntoJson>
            {
                P(0.001, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3)
            }) == e1, "cambio");

        // Y las llaves resultantes ya no chocan.
        Check("las llaves de los dos muros son distintas",
            Llave.De(ClasePieza.Muro, e1, "Story1") != Llave.De(ClasePieza.Muro, e2, "Story1"));

        // ---- Unica: para un paño no basta con respetar la etiqueta del modelo ----
        //
        // El lector pone el PIER como etiqueta del muro, y un pier agrupa varios paños. Dos
        // trozos del mismo muro con pier P1 tienen la MISMA etiqueta del modelo, asi que
        // Estable los deja iguales y sus llaves chocan. Ese es el fallo que dejaba la planta
        // baja sin muros.
        Igual("Estable respeta el pier y por eso NO distingue dos trozos",
            Etiquetas.Estable("P1", m1), Etiquetas.Estable("P1", m2));

        var u1 = Etiquetas.Unica("P1", m1);
        var u2 = Etiquetas.Unica("P1", m2);

        Check("Unica SI los distingue", u1 != u2, $"{u1} vs {u2}");
        Check("y conserva el pier delante, para que la marca se pueda leer",
            u1.StartsWith("P1", StringComparison.Ordinal), u1);
        Igual("es estable entre exportaciones", Etiquetas.Unica("P1", m1), u1);
        Igual("y no depende del orden de los vertices", Etiquetas.Unica("P1", revuelto), u1);
        Check("sin etiqueta se comporta como Estable",
            Etiquetas.Unica("", m1) == Etiquetas.Estable("", m1));
        Check("un reajuste de milimetros tampoco la cambia",
            Etiquetas.Unica("P1", new List<PuntoJson>
            {
                P(0.001, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3)
            }) == u1, "cambio");

        Check("las llaves de los dos trozos con el mismo pier ya no chocan",
            Llave.De(ClasePieza.Muro, u1, "Story1") != Llave.De(ClasePieza.Muro, u2, "Story1"));

        Console.WriteLine("\n[2e] Contorno de losa: plano y paralelo a XY");

        // El error del informe: "each curve loop is not planar; or each curve loop is not in a
        // plane parallel to the horizontal(XY) plane".
        var torcida = new List<PuntoJson>
            { P(0, 0, 3), P(5, 0, 3.004), P(5, 5, 3), P(0, 5, 2.998) };

        var (plana, z, desv) = Contornos.AHorizontal(torcida);

        Check("todos los vertices quedan a la misma cota",
            plana.All(p => Math.Abs(p.Z - plana[0].Z) < 1e-12), "siguen distintos");
        Casi("se toma la cota mas alta, que es la cara superior", z, 3.004);
        Casi("y se reporta cuanto se movio", desv, 0.006, 1e-9);
        Igual("no se pierde ningun vertice", plana.Count, 4);
        Check("la planta no se toca",
            Math.Abs(plana[1].X - 5) < 1e-12 && Math.Abs(plana[2].Y - 5) < 1e-12);

        // Al aplanar pueden aparecer repetidos: dos vertices que solo diferian en Z.
        var conRepetido = new List<PuntoJson>
            { P(0, 0, 3), P(5, 0, 3.01), P(5, 0, 3), P(5, 5, 3), P(0, 5, 3) };

        var (ap, _, _) = Contornos.AHorizontal(conRepetido);
        Igual("aplanar junta los que solo diferian en Z",
            Contornos.SinRepetidos(ap).Count, 4);

        Igual("un contorno vacio no revienta", Contornos.AHorizontal(null).Contorno.Count, 0);

        Console.WriteLine("\n[2f] Muros: bajo la cadena y al pano de los castillos");

        // Un muro de 5 m entre dos castillos de 15x15, con una cadena de 40 de peralte encima.
        var mm = new ModeloJson();
        mm.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });

        var elMuro = new PanoJson
        {
            Etiqueta = "MURO-A", Clase = ClasePieza.Muro, Nivel = "Story1",
            Seccion = new SeccionJson { Nombre = "MURO 15", Forma = FormaSeccion.Pano, EspesorM = 0.15 }
        };
        elMuro.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) });
        mm.Panos.Add(elMuro);

        // La cadena de cerramiento, encima y en la misma direccion. Con punto cardinal 8
        // -arriba al centro-, que es como se modela una cadena: su cara de ARRIBA va a la cota
        // del piso y cuelga el peralte entero por debajo.
        mm.Barras.Add(new BarraJson
        {
            Etiqueta = "CAD-1", Clase = ClasePieza.Trabe, Nivel = "Story1",
            P1 = P(0, 0, 3), P2 = P(5, 0, 3), PuntoCardinal = 8,
            Seccion = Rect("CADENA 15X40", 15, 40)
        });

        // Los dos castillos, en las puntas.
        mm.Barras.Add(new BarraJson
        {
            Etiqueta = "K-1", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3), Seccion = Rect("K 15X15", 15, 15)
        });
        mm.Barras.Add(new BarraJson
        {
            Etiqueta = "K-2", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(5, 0, 0), P2 = P(5, 0, 3), Seccion = Rect("K 15X15", 15, 15)
        });

        var aj = AjusteDeMuros.Ajustar(elMuro, mm);

        Check("el muro se reconoce como rectangulo vertical", aj.Nota is null, aj.Nota ?? "");
        Casi("baja 40 cm, el peralte de la cadena", aj.BajoM, 0.40);
        Casi("y se recorta 7.5 cm por punta, medio castillo", aj.RecortadoM, 0.15);

        var zs = aj.Contorno.Select(p => p.Z).Distinct().OrderBy(v => v).ToList();
        Igual("el contorno sigue teniendo dos cotas", zs.Count, 2);
        Casi("arranca en la base", zs[0], 0);
        Casi("y muere bajo la cadena, en 2.60", zs[1], 2.60);

        var xs = aj.Contorno.Select(p => p.X).Distinct().OrderBy(v => v).ToList();
        Casi("empieza al pano del primer castillo", xs[0], 0.075);
        Casi("y acaba al pano del segundo", xs[1], 4.925);

        // Sin cadena encima, no se baja.
        var sinCadena = new ModeloJson();
        sinCadena.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });
        var m3 = new PanoJson { Etiqueta = "M", Clase = ClasePieza.Muro, Nivel = "Story1" };
        m3.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) });
        sinCadena.Panos.Add(m3);

        Casi("sin cadena encima no se baja nada",
            AjusteDeMuros.Ajustar(m3, sinCadena).BajoM, 0);

        // Una trabe PERPENDICULAR no cuenta: cruza el muro, no corre encima.
        var perpend = new ModeloJson();
        perpend.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });
        var m4 = new PanoJson { Etiqueta = "M", Clase = ClasePieza.Muro, Nivel = "Story1" };
        m4.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) });
        perpend.Panos.Add(m4);
        perpend.Barras.Add(new BarraJson
        {
            Etiqueta = "T", Clase = ClasePieza.Trabe, Nivel = "Story1",
            P1 = P(2.5, -3, 3), P2 = P(2.5, 3, 3), Seccion = Rect("T 20X50", 20, 50)
        });

        Casi("una trabe perpendicular no baja el muro",
            AjusteDeMuros.Ajustar(m4, perpend).BajoM, 0);

        // Un castillo de OTRO piso no recorta.
        var otroPiso = new ModeloJson();
        otroPiso.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });
        var m5 = new PanoJson { Etiqueta = "M", Clase = ClasePieza.Muro, Nivel = "Story1" };
        m5.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) });
        otroPiso.Panos.Add(m5);
        otroPiso.Barras.Add(new BarraJson
        {
            Etiqueta = "K-arriba", Clase = ClasePieza.Columna, Nivel = "Story2",
            P1 = P(0, 0, 6), P2 = P(0, 0, 9), Seccion = Rect("K 15X15", 15, 15)
        });

        Casi("un castillo de otro piso no recorta", AjusteDeMuros.Ajustar(m5, otroPiso).RecortadoM, 0);

        // Un pano de forma libre se deja en paz, con nota.
        var libre = new PanoJson { Etiqueta = "HASTIAL", Clase = ClasePieza.Muro, Nivel = "Story1" };
        libre.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(2.5, 0, 4) });
        var ajLibre = AjusteDeMuros.Ajustar(libre, mm);

        Check("un pano triangular no se toca", ajLibre.Nota is not null);
        Igual("y conserva sus vertices", ajLibre.Contorno.Count, 3);

        // Una columna rectangular girada se proyecta bien sobre la direccion del muro.
        var girada = new BarraJson
        {
            Etiqueta = "C", Clase = ClasePieza.Columna,
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            AnguloGrados = 90,
            Seccion = Rect("C 20X60", 20, 60)
        };

        // A 90 grados, el eje local 2 apunta a +Y, asi que sobre la direccion X del muro se
        // proyecta el ANCHO -eje 3-, que son 20 cm: medio son 10.
        Casi("una columna girada 90 grados proyecta su otra medida",
            AjusteDeMuros.MedioAncho(girada, 1, 0), 0.10);

        var sinGirar = new BarraJson
        {
            Etiqueta = "C", Clase = ClasePieza.Columna,
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            Seccion = Rect("C 20X60", 20, 60)
        };

        Casi("y sin girar, la suya", AjusteDeMuros.MedioAncho(sinGirar, 1, 0), 0.30);

        // Y el pase completo sobre el modelo deja resumen.
        var resumen = AjusteDeMuros.AplicarATodos(mm);
        Check("el pase completo informa de lo que hizo",
            resumen.Any(a => a.Contains("recortaron")) && resumen.Any(a => a.Contains("bajaron")),
            string.Join(" | ", resumen));

        Casi("y el muro quedo ya ajustado en el modelo",
            elMuro.Vertices.Max(p => p.Z), 2.60);

        // ---- EL PUNTO DE INSERCION MANDA EN CUANTO SE BAJA EL MURO ----
        //
        // Esto es lo que hacia que la cadena quedara alzada y el muro no muriera bajo ella. Lo
        // que cuelga una trabe por debajo de su linea NO es su peralte: depende de su punto
        // cardinal. El complemento tenia el 8 escrito a mano, y el de OMISION de ETABS es el 10.
        Igual("el punto 8 mide contra la cara de arriba",
            Insercion.Cara(8), CaraDeInsercion.Arriba);
        Igual("el 10 -el de omision de ETABS- contra el centro",
            Insercion.Cara(10), CaraDeInsercion.Centro);
        Igual("el 5 tambien", Insercion.Cara(5), CaraDeInsercion.Centro);
        Igual("el 11, centro de cortante, tambien",
            Insercion.Cara(11), CaraDeInsercion.Centro);
        Igual("y el 2 contra la cara de abajo", Insercion.Cara(2), CaraDeInsercion.Abajo);
        Igual("un valor raro se trata como el centro",
            Insercion.Cara(99), CaraDeInsercion.Centro);

        Casi("con el 8 cuelga el peralte entero", Insercion.CuelgaM(0.40, 8), 0.40);
        Casi("con el 10 cuelga la mitad", Insercion.CuelgaM(0.40, 10), 0.20);
        Casi("y con el 2 no cuelga nada", Insercion.CuelgaM(0.40, 2), 0);

        Casi("la cara inferior con el 8", Insercion.CaraInferior(3, 0.40, 8), 2.60);
        Casi("con el 10", Insercion.CaraInferior(3, 0.40, 10), 2.80);
        Casi("y con el 2", Insercion.CaraInferior(3, 0.40, 2), 3.0);

        // ---- PERO EL MURO Y LA CADENA TIENEN QUE ESTAR DE ACUERDO ----
        //
        // El modelador cuelga SIEMPRE la cadena su peralte entero bajo el nivel, porque es lo que
        // se construye: la cadena corona el muro y el piso se apoya encima. Respetar el punto de
        // insercion al pie de la letra dejaba la trabe repartida -media por encima del nivel-,
        // que es correcto para el calculo y no es lo que se construye.
        //
        // Asi que el ajuste del muro tiene que usar LA MISMA regla. Si cada uno supone otra cosa,
        // aparece un hueco entre el muro y su cadena, que es el sintoma que se reporto.
        Casi("la cadena cuelga su peralte entero, pase lo que pase",
            CadenaBajoElNivel.CuelgaM(0.40), 0.40);
        Casi("y su cara inferior sale de ahi",
            CadenaBajoElNivel.CaraInferior(3, 0.40), 2.60);
        var conCentroide = new ModeloJson();
        conCentroide.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 3 });

        var muroC = new PanoJson
        {
            Etiqueta = "MURO-C", Clase = ClasePieza.Muro, Nivel = "Story1",
            Seccion = new SeccionJson
            {
                Nombre = "MURO 15", Forma = FormaSeccion.Pano, EspesorM = 0.15
            }
        };

        muroC.Vertices.AddRange(new[] { P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3) });
        conCentroide.Panos.Add(muroC);

        conCentroide.Barras.Add(new BarraJson
        {
            Etiqueta = "CAD-C", Clase = ClasePieza.Trabe, Nivel = "Story1",
            P1 = P(0, 0, 3), P2 = P(5, 0, 3), PuntoCardinal = 10,
            Seccion = Rect("CADENA 15X40", 15, 40)
        });

        var ajC = AjusteDeMuros.Ajustar(muroC, conCentroide);

        Casi("el muro baja la cadena entera aunque su punto cardinal sea el centroide",
            ajC.BajoM, 0.40);

        // Y no cambia si el punto cardinal es otro: la regla es de obra, no del calculo. Esto es
        // lo que garantiza que el muro muera exactamente en la cara inferior de su cadena.
        foreach (var punto in new[] { 2, 5, 8, 10, 11 })
        {
            conCentroide.Barras[0].PuntoCardinal = punto;

            Casi($"con el punto {punto} el muro baja lo mismo",
                AjusteDeMuros.Ajustar(muroC, conCentroide).BajoM, 0.40);
        }

        // Y el punto cardinal viaja por el archivo. Sigue haciendo falta para las piezas que NO
        // son trabes -una diagonal no tiene "arriba" que valga- y para poder decirlo en el
        // informe, asi que se comprueba que sobrevive.
        conCentroide.Barras[0].PuntoCardinal = 2;

        var idaC = ArchivoModelo.ATexto(conCentroide);
        var vueltaC = ArchivoModelo.DeTexto(idaC);

        Igual("el punto cardinal sobrevive el ida y vuelta por disco",
            vueltaC.Barras[0].PuntoCardinal, 2);

        // Un archivo de una version anterior no lo trae, y entonces tiene que quedar en el
        // centroide, que es el de omision de ETABS: nunca en el 8, que era la suposicion mala.
        var sinCardinal = ArchivoModelo.DeTexto(
            idaC.Replace("\"PuntoCardinal\":", "\"PuntoCardinalX\":"));

        Igual("y un archivo sin el queda en el centroide, no en el 8",
            sinCardinal.Barras[0].PuntoCardinal, Insercion.Centroide);

        Console.WriteLine("\n[2g] Columnas: entre que niveles van");

        // El error que Revit marca como imposible de ignorar: "Position of end cut planes has
        // resulted in a slanted column without any geometry". Sale de crear la columna con la
        // API de columna INCLINADA. La salida es colocarla por niveles.
        var nivs = new List<NivelJson>
        {
            new() { Nombre = "Planta baja", ElevacionM = 0 },
            new() { Nombre = "Nv-02", ElevacionM = 2.78 },
            new() { Nombre = "Nv-03", ElevacionM = 5.56 }
        };

        var col = Colocacion.De(0, 2.78, nivs);

        Igual("la base cae en Planta baja", col.NivelBase, "Planta baja");
        Casi("sin desfase", col.DesfaseBaseM, 0);
        Igual("la punta cae en Nv-02", col.NivelPunta, "Nv-02");
        Casi("y tampoco se desfasa", col.DesfasePuntaM, 0);

        // ETABS asigna la columna al nivel al que SUBE. Si se usara ese como base, la columna
        // quedaria con la base por encima de la punta y sin geometria.
        var alReves2 = Colocacion.De(2.78, 0, nivs);
        Igual("da igual el orden en que vengan las cotas: la base es la de abajo",
            alReves2.NivelBase, "Planta baja");
        Igual("y la punta la de arriba", alReves2.NivelPunta, "Nv-02");

        // Una columna que no arranca en un nivel exacto se ata con desfase.
        var conDesfase = Colocacion.De(0.5, 2.78, nivs);
        Igual("una columna que arranca a media altura se ata al nivel mas cercano",
            conDesfase.NivelBase, "Planta baja");
        Casi("con su desfase", conDesfase.DesfaseBaseM, 0.5);

        // Vertical o inclinada.
        Check("una columna a plomo se reconoce como vertical",
            Colocacion.EsVertical(new BarraJson
            {
                P1 = P(1, 1, 0), P2 = P(1, 1, 3), Clase = ClasePieza.Columna
            }));

        Check("3 mm fuera de plomo sigue siendo vertical",
            Colocacion.EsVertical(new BarraJson
            {
                P1 = P(1, 1, 0), P2 = P(1.003, 1, 3), Clase = ClasePieza.Columna
            }),
            "un modelo real no tiene columnas perfectas al milimetro");

        Check("una diagonal de verdad no es vertical",
            !Colocacion.EsVertical(new BarraJson
            {
                P1 = P(0, 0, 0), P2 = P(3, 0, 3), Clase = ClasePieza.Diagonal
            }));

        Check("una barra horizontal tampoco",
            !Colocacion.EsVertical(new BarraJson
            {
                P1 = P(0, 0, 3), P2 = P(5, 0, 3), Clase = ClasePieza.Trabe
            }));

        Igual("sin niveles no se inventa ninguno",
            Colocacion.De(0, 3, null).NivelBase, "");

        // ---- El nivel de un PAÑO: el de su base, no el de la planta que le da ETABS ----
        //
        // ETABS asigna un area a la planta de su parte de ARRIBA, asi que un muro de planta
        // baja llega con el nivel de la planta primera. Atado a ese nivel, el muro no sale en
        // la vista de planta baja aunque su geometria este bien. Es la misma correccion que ya
        // se hacia para las columnas, que hasta ahora los paños no tenian.
        var nivsPB = new List<NivelJson>
        {
            new() { Nombre = "Base", ElevacionM = 0 },
            new() { Nombre = "Story1", ElevacionM = 3 },
            new() { Nombre = "Story2", ElevacionM = 6 }
        };

        var muroPB = Colocacion.NivelDePano(0, nivsPB);

        Igual("un muro que arranca en 0 va al nivel de abajo, no al de arriba",
            muroPB.Nombre, "Base");
        Casi("y sin desfase", muroPB.DesfaseM, 0);

        var muroP1 = Colocacion.NivelDePano(3, nivsPB);
        Igual("el de la planta siguiente va a Story1", muroP1.Nombre, "Story1");

        var muroRaro = Colocacion.NivelDePano(3.2, nivsPB);
        Igual("uno que arranca algo mas arriba se ata al mas cercano", muroRaro.Nombre, "Story1");
        Casi("con el desfase que le falta", muroRaro.DesfaseM, 0.2);

        Igual("sin niveles no se inventa ninguno para el paño",
            Colocacion.NivelDePano(0, null).Nombre, "");

        Console.WriteLine("\n[2h] Losas inclinadas: se conservan con su pendiente");

        // Lo que se pidio: una losa de entrepiso inclinada modelada en el calculo debe salir
        // inclinada, no aplanada.
        var rampaLosa = new List<PuntoJson>
            { P(0, 0, 3), P(6, 0, 3), P(6, 5, 4), P(0, 5, 4) };   // 1 m de desnivel

        var forma = Losas.Preparar(rampaLosa);

        Casi("se apoya en la cota mas baja", forma.ZBaseM, 3);
        Casi("y el desnivel es de un metro", forma.DesnivelM, 1);
        Check("no es plana", !forma.EsPlana);
        Check("el contorno de apoyo es horizontal",
            forma.EnPlanta.All(p => Math.Abs(p.Z - 3) < 1e-12));

        // La flecha de pendiente: arranca en el vertice MAS BAJO -que esta sobre el contorno,
        // como pide Revit- y apunta a la planta del mas alto.
        Casi("la flecha arranca en el vertice mas bajo, en X", forma.ColaX, 0);
        Casi("y en Y", forma.ColaY, 0);
        Casi("y apunta a la planta del mas alto, en X", forma.PuntaX, 6);
        Casi("y en Y", forma.PuntaY, 5);

        // El angulo: sube 1 m en la carrera entre esos dos puntos.
        var carrera = Math.Sqrt((6 * 6) + (5 * 5));
        Casi("el angulo es el del desnivel sobre la carrera",
            forma.AnguloRad, Math.Atan2(1, carrera), 1e-12);
        Check("y es un angulo razonable, no un absurdo",
            forma.AnguloRad > 0 && forma.AnguloRad < Math.PI / 4,
            forma.AnguloRad.ToString("0.0000"));

        // Una rampa PLANA de verdad: la flecha la reproduce exacta.
        var rampaPlana = new List<PuntoJson>
            { P(0, 0, 3), P(6, 0, 3), P(6, 5, 3), P(0, 5, 3) };
        rampaPlana[1] = P(6, 0, 3.6);
        rampaPlana[2] = P(6, 5, 3.6);

        var fPlana = Losas.Preparar(rampaPlana);
        Casi("una rampa plana no se desvia del plano de la flecha",
            Losas.DesviacionDelPlano(rampaPlana, fPlana), 0, 1e-9);

        // Una losa ALABEADA no se puede reproducir con una flecha, y hay que saberlo.
        var alabeada = new List<PuntoJson>
            { P(0, 0, 3), P(6, 0, 3.5), P(6, 5, 4), P(0, 5, 3.9) };

        var fAlabeada = Losas.Preparar(alabeada);
        Check("una losa alabeada se detecta como aproximacion",
            Losas.DesviacionDelPlano(alabeada, fAlabeada) > 0.01,
            "desviacion=" + Losas.DesviacionDelPlano(alabeada, fAlabeada));

        // Una losa plana de verdad no lleva flecha: crearla con una la inclinaria por ruido.
        var planaDeVerdad = Losas.Preparar(new List<PuntoJson>
            { P(0, 0, 3), P(5, 0, 3.001), P(5, 5, 3), P(0, 5, 3) });

        Check("un milimetro de ruido no cuenta como inclinacion", planaDeVerdad.EsPlana,
            "desnivel=" + planaDeVerdad.DesnivelM);
        Casi("y no se calcula angulo", planaDeVerdad.AnguloRad, 0);

        // Dos vertices uno encima del otro no dan pendiente expresable.
        var sinCarrera = Losas.Preparar(new List<PuntoJson>
            { P(0, 0, 3), P(0, 0, 5), P(1, 0, 3) });

        Check("sin carrera en planta no se inventa una flecha", sinCarrera.EsPlana,
            "angulo=" + sinCarrera.AnguloRad);

        Igual("una losa sin vertices no revienta", Losas.Preparar(null).EnPlanta.Count, 0);

        Console.WriteLine("\n[2c] Agrupar los errores por causa");

        // El caso real: una importacion que falla por UN motivo que afecta a cientos de
        // piezas. Un informe que solo diga "372" no permite arreglar nada.
        var muchos = new List<string>();

        for (var i = 0; i < 372; i++)
        {
            muchos.Add($"«CadLink|Losa|L{i}|Story1»: el tipo elegido no admite este contorno");
        }

        muchos.Add("«CadLink|Muro|M1|Story1»: otra cosa distinta");

        var g = Agrupador.Agrupar(muchos);

        Igual("372 errores iguales y uno distinto dan DOS motivos", g.Count, 2);
        Igual("el motivo mas frecuente va primero", g[0].Cuantas, 372);
        Igual("y dice cual es", g[0].Motivo, "el tipo elegido no admite este contorno");
        Igual("guarda tres ejemplos", g[0].Ejemplos.Count, 3);
        Check("y los ejemplos son las piezas, sin el motivo",
            g[0].Ejemplos[0].Contains("L0"), g[0].Ejemplos[0]);
        Igual("el segundo motivo cuenta uno", g[1].Cuantas, 1);

        var texto = Agrupador.Texto(muchos);
        Check("el informe dice el total", texto.Contains("373"), texto.Split('\n')[0]);
        Check("y dice cuantas por motivo", texto.Contains("372 x"), texto);

        // Un mensaje sin el separador se agrupa por el texto completo, sin adivinar.
        var sueltos = Agrupador.Agrupar(new[] { "fallo general", "fallo general", "otro" });
        Igual("los mensajes sin pieza tambien se agrupan", sueltos.Count, 2);
        Igual("y cuentan bien", sueltos[0].Cuantas, 2);
        Igual("sin inventar ejemplos", sueltos[0].Ejemplos.Count, 0);

        Igual("sin errores no hay informe", Agrupador.Texto(null), "");
        Igual("ni con una lista vacia", Agrupador.Texto(new string[0]), "");
        Igual("los vacios se descartan",
            Agrupador.Agrupar(new[] { "", "  ", "de verdad" }).Count, 1);

        // Determinista: dos corridas dan el mismo informe.
        Igual("el informe es determinista", Agrupador.Texto(muchos), Agrupador.Texto(muchos));

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
            planRepe.Avisos.Any(a => a.Contains("mas de una pieza con esta etiqueta")),
            string.Join(" | ", planRepe.Avisos));

        // Y LAS DOS SE MODELAN. Antes la segunda desaparecia del plan entera: no contaba ni
        // como creada ni como saltada, solo dejaba un aviso. Asi es como una planta podia
        // quedarse sin modelar mientras el informe decia que todo habia ido bien.
        var basePlan = Planificador.Armar(ModeloDePrueba(), mapeo, cat, null, Modo.ModelarNuevos);

        Igual("la pieza repetida NO se pierde, se desempata por su posicion",
            planRepe.Pasos.Count, basePlan.Pasos.Count + 1);
        Igual("y se cuenta como desempatada", planRepe.Desempatadas, 1);
        Igual("sin descartar ninguna", planRepe.Duplicadas, 0);
        Check("las llaves resultantes son distintas",
            planRepe.Pasos.Select(p => p.Llave).Distinct().Count() == planRepe.Pasos.Count);

        // Solo se descarta lo que es la MISMA pieza en el MISMO sitio, que si es un duplicado.
        var igualito = ModeloDePrueba();
        var clon = igualito.Barras[0];

        igualito.Barras.Add(new BarraJson
        {
            Etiqueta = clon.Etiqueta, Clase = clon.Clase, Nivel = clon.Nivel,
            P1 = P(clon.P1.X, clon.P1.Y, clon.P1.Z),
            P2 = P(clon.P2.X, clon.P2.Y, clon.P2.Z),
            Seccion = clon.Seccion
        });

        var planClon = Planificador.Armar(igualito, mapeo, cat, null, Modo.ModelarNuevos);

        Igual("una pieza identica en el mismo sitio si se descarta", planClon.Duplicadas, 1);
        Igual("y no se desempata", planClon.Desempatadas, 0);
        Igual("asi que el plan no crece", planClon.Pasos.Count, basePlan.Pasos.Count);

        // ---- El caso del usuario: un muro mallado con el MISMO PIER ----
        //
        // El lector pone el pier como etiqueta del muro, y un pier no identifica un paño:
        // identifica un grupo. Seis trozos de muro de planta baja con pier P1 daban seis veces
        // la llave «CadLink|Muro|P1|Story1» y se modelaba UNO. El sintoma reportado fue
        // exactamente ese: la planta baja sin nada, mientras columnas y trabes salian bien.
        var mallado = ModeloDePrueba();
        var cuantosAntes = mallado.Panos.Count;

        for (var i = 0; i < 6; i++)
        {
            var trozo = new PanoJson
            {
                Etiqueta = Etiquetas.Unica("P1", Trozo(i)),
                Clase = ClasePieza.Muro,
                Nivel = "Story1",
                Seccion = new SeccionJson
                {
                    Nombre = "MURO20", Forma = FormaSeccion.Pano, EspesorM = 0.20
                }
            };

            trozo.Vertices.AddRange(Trozo(i));
            mallado.Panos.Add(trozo);
        }

        var planMallado = Planificador.Armar(mallado, mapeo, cat, null, Modo.ModelarNuevos);

        Igual("los seis trozos de muro con el mismo pier dan seis pasos",
            planMallado.Pasos.Count, basePlan.Pasos.Count + 6);
        Igual("y no hubo que desempatar ninguno: la etiqueta ya los distingue",
            planMallado.Desempatadas, 0);
        Igual("ni se descarto ninguno", planMallado.Duplicadas, 0);
        Igual("el modelo tenia los paños que se le pusieron",
            mallado.Panos.Count, cuantosAntes + 6);

        // Y con el etiquetado VIEJO -Estable, que respeta el pier y por tanto deja los seis
        // trozos con la misma etiqueta- tampoco se pierde ninguno, porque ahora el planificador
        // desempata por posicion en vez de descartar. Son dos defensas independientes: si un
        // modelo exportado con una version anterior se reimporta, sigue saliendo completo.
        var viejo = ModeloDePrueba();

        for (var i = 0; i < 6; i++)
        {
            var trozo = new PanoJson
            {
                Etiqueta = Etiquetas.Estable("P1", Trozo(i)),
                Clase = ClasePieza.Muro,
                Nivel = "Story1",
                Seccion = new SeccionJson
                {
                    Nombre = "MURO20", Forma = FormaSeccion.Pano, EspesorM = 0.20
                }
            };

            trozo.Vertices.AddRange(Trozo(i));
            viejo.Panos.Add(trozo);
        }

        Igual("con el etiquetado viejo los seis trozos comparten etiqueta",
            viejo.Panos.TakeLast(6).Select(p => p.Etiqueta).Distinct().Count(), 1);

        var planViejo = Planificador.Armar(viejo, mapeo, cat, null, Modo.ModelarNuevos);

        Igual("y aun asi se modelan los seis",
            planViejo.Pasos.Count, basePlan.Pasos.Count + 6);
        Igual("desempatando cinco", planViejo.Desempatadas, 5);
        Igual("sin descartar ninguno", planViejo.Duplicadas, 0);
        Check("con llaves todas distintas",
            planViejo.Pasos.Select(p => p.Llave).Distinct().Count() == planViejo.Pasos.Count);

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

        Check("y la fila NO queda incoherente: nunca se ensena familia A con tipos de la B",
            fila.Coherente, fila.Diagnostico);
        Igual("asi que el cuadro no reporta ninguna incoherencia", v.Incoherentes, 0);
        Check("el diagnostico dice la familia y cuantos tipos se ofrecen",
            fila.Diagnostico.Contains(otra) && fila.Diagnostico.Contains("tipo(s) ofrecidos"),
            fila.Diagnostico);

        // Elegir a mano deja de ser sugerencia.
        fila.Tipo = fila.Tipos.First();
        Check("elegir a mano marca la fila como revisada", !fila.EsSugerencia);
        Check("y vuelve a estar mapeada", fila.Mapeada);
        Check("y sigue siendo coherente", fila.Coherente, fila.Diagnostico);

        // ---- El caso que reporto el usuario: acero HSS junto a hormigon rectangular ----
        //
        // Con una columna HSS y otra de hormigon en el mismo cuadro, elegir la familia HSS
        // ensenaba los tipos del hormigon. El nucleo tiene que ofrecer SOLO los de la familia
        // elegida, y ninguno de la otra.
        var catMixto = new CatalogoRevit();

        foreach (var t in new[] { "300 x 450", "450 x 600", "600 x 750", "C-01 40X40" })
        {
            catMixto.Tipos.Add(new TipoRevit
            {
                Categoria = CategoriaRevit.ColumnaEstructural,
                Familia = "Hormigón-Rectangular-Pilar", Tipo = t,
                Id = catMixto.Tipos.Count + 1, Forma = FormaSeccion.Rectangulo
            });
        }

        foreach (var t in new[] { "HSS3x2x1/4", "HSS4x4x1/4", "HSS6x6x3/8" })
        {
            catMixto.Tipos.Add(new TipoRevit
            {
                Categoria = CategoriaRevit.ColumnaEstructural,
                Familia = "HSS-Hollow Structural Section-Column", Tipo = t,
                Id = catMixto.Tipos.Count + 1, Forma = FormaSeccion.Cajon
            });
        }

        catMixto.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 0 });

        var mixto = new ModeloJson();
        mixto.Niveles.Add(new NivelJson { Nombre = "Story1", ElevacionM = 0 });
        mixto.Barras.Add(new BarraJson
        {
            Etiqueta = "C1", Clase = ClasePieza.Columna, Nivel = "Story1",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            Seccion = new SeccionJson
            {
                Nombre = "HSS3X2X1/4", Forma = FormaSeccion.Cajon,
                AnchoM = 0.051, PeralteM = 0.076
            }
        });

        var vMix = new VistaMapeo(mixto, catMixto);
        var fHss = vMix.Filas.Single();

        Igual("las dos familias de columnas se ofrecen", fHss.Familias.Count, 2);

        fHss.Familia = "HSS-Hollow Structural Section-Column";

        Igual("al elegir HSS se ofrecen sus 3 tipos", fHss.Tipos.Count, 3);
        Check("y NINGUNO es de hormigón rectangular",
            fHss.Tipos.All(t => t.Familia == "HSS-Hollow Structural Section-Column"),
            string.Join(", ", fHss.Tipos.Select(t => t.NombreCompleto)));
        Check("en concreto no aparece 300 x 450",
            fHss.Tipos.All(t => t.Tipo != "300 x 450"));

        fHss.Familia = "Hormigón-Rectangular-Pilar";

        Igual("y al volver a hormigón se ofrecen sus 4", fHss.Tipos.Count, 4);
        Check("sin ninguno de acero", fHss.Tipos.All(t => t.Tipo != "HSS4x4x1/4"));
        Igual("el cuadro nunca queda incoherente", vMix.Incoherentes, 0);

        // ---- Las categorias se leen, no salen como nombre del enum ----
        Igual("hay una opcion por categoria",
            fHss.CategoriasOpciones.Count, Enum.GetValues<CategoriaRevit>().Length);
        Check("y se leen en castellano, no 'ColumnaEstructural'",
            fHss.CategoriasOpciones.Any(o => o.Nombre == "Pilares estructurales")
            && fHss.CategoriasOpciones.All(o => !o.Nombre.Contains("Estructural")),
            string.Join(", ", fHss.CategoriasOpciones.Select(o => o.Nombre)));
        Check("cada opcion arrastra su valor del enum",
            fHss.CategoriasOpciones.Select(o => o.Valor)
                .SequenceEqual(Enum.GetValues<CategoriaRevit>()));

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

        // ---- La categoria se puede CAMBIAR: no la impone la clase de ETABS ----
        var vCat = new VistaMapeo(modelo, cat);
        var filaMuro = vCat.Filas.First(f => f.Seccion.Clase == ClasePieza.Muro);

        Igual("un muro arranca en la categoria de muros",
            filaMuro.Categoria, CategoriaRevit.Muro);
        Check("pero se ofrecen TODAS las categorias, no solo la suya",
            filaMuro.Categorias.Count == Enum.GetValues<CategoriaRevit>().Length,
            filaMuro.Categorias.Count.ToString());
        Check("y se sabe que no se ha cambiado", !filaMuro.CategoriaCambiada);

        // El caso que se pidio: un paño que ETABS trae como muro y hay que modelar como suelo.
        filaMuro.Categoria = CategoriaRevit.Piso;

        Igual("al cambiar a suelos, la categoria queda cambiada",
            filaMuro.Categoria, CategoriaRevit.Piso);
        Check("se marca como cambiada a mano", filaMuro.CategoriaCambiada);
        Check("las familias son ahora las de suelos",
            filaMuro.Familias.SequenceEqual(cat.FamiliasDe(CategoriaRevit.Piso)),
            string.Join(", ", filaMuro.Familias));
        Check("y el tipo de muro que estaba puesto se descarto", filaMuro.Tipo is null,
            filaMuro.Tipo?.NombreCompleto ?? "null");

        // Y se puede elegir un tipo de suelo para ese muro.
        filaMuro.Familia = filaMuro.Familias.First();
        filaMuro.Tipo = filaMuro.Tipos.First();

        Check("se puede poner un tipo de suelo en una seccion de muro", filaMuro.Mapeada);
        Igual("y el tipo elegido es de suelos",
            filaMuro.Tipo!.Categoria, CategoriaRevit.Piso);

        // La eleccion tiene que SOBREVIVIR al guardado: es lo que se rompia al deducir la
        // categoria de la clase.
        var mapCat = vCat.AMapeo();
        var filaGuardada = mapCat.De(filaMuro.Seccion.Clave)!;

        Igual("el mapeo guarda la categoria elegida, no la de la clase",
            filaGuardada.Categoria, CategoriaRevit.Piso);

        var textoCat = ArchivoMapeo.ATexto(mapCat.AGuardado());
        var vueltaCat = Mapeo.DeGuardado(ArchivoMapeo.DeTexto(textoCat));

        Igual("y sobrevive el ida y vuelta por disco",
            vueltaCat.TipoDe(filaMuro.Seccion, cat)?.Categoria, CategoriaRevit.Piso);

        var vCat2 = new VistaMapeo(modelo, cat, vueltaCat);
        var filaMuro2 = vCat2.Filas.First(f => f.Seccion.Clave == filaMuro.Seccion.Clave);

        Igual("al reabrir el cuadro, la categoria cambiada vuelve puesta",
            filaMuro2.Categoria, CategoriaRevit.Piso);
        Check("y sigue marcada como cambiada", filaMuro2.CategoriaCambiada);

        // Volver a la categoria original tambien funciona.
        filaMuro.Categoria = CategoriaRevit.Muro;
        Check("se puede volver a la categoria de origen", !filaMuro.CategoriaCambiada);
        Check("y las familias vuelven a ser las de muros",
            filaMuro.Familias.SequenceEqual(cat.FamiliasDe(CategoriaRevit.Muro)));

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


// ==========================================================================
//  Lo que se pidio en la vuelta de los castillos, los niveles y los ejes
// ==========================================================================

internal static partial class Programa
{
    // ------------------------------------------------------------------
    private static void GiroDeLaSeccion()
    {
        Console.WriteLine("\n[10] El giro de la seccion: los castillos como en ETABS");

        SeccionJson S(double a, double p) => new()
        {
            Nombre = "K", Forma = FormaSeccion.Rectangulo, AnchoM = a, PeralteM = p
        };

        TipoRevit T(double a, double p) => new()
        {
            Categoria = CategoriaRevit.ColumnaEstructural,
            Familia = "F", Tipo = "T", AnchoM = a, PeralteM = p, Id = 1
        };

        // Una seccion cuadrada no se gira: el cuarto de vuelta no se notaria y mover piezas
        // sin motivo solo crea ruido en el modelo.
        Check("una seccion cuadrada no se considera girada",
            !Orientacion.TipoGirado(S(0.20, 0.20), T(0.20, 0.20)));
        Check("ni aunque el tipo sea de otra medida",
            !Orientacion.TipoGirado(S(0.20, 0.20), T(0.30, 0.30)));

        // El caso que importa: el emparejador acepta un tipo con las medidas al reves.
        Check("un tipo con las medidas al reves SI esta girado",
            Orientacion.TipoGirado(S(0.15, 0.25), T(0.25, 0.15)));
        Check("y uno con las medidas en el mismo orden NO",
            !Orientacion.TipoGirado(S(0.15, 0.25), T(0.15, 0.25)));

        // Sin las cuatro medidas no se adivina: girar a ciegas es peor que no girar.
        Check("sin medidas en el tipo no se gira",
            !Orientacion.TipoGirado(S(0.15, 0.25), T(0, 0)));
        Check("sin seccion tampoco", !Orientacion.TipoGirado(null, T(0.25, 0.15)));
        Check("sin tipo tampoco", !Orientacion.TipoGirado(S(0.15, 0.25), null));

        // El giro total: el del modelo, mas el cuarto de vuelta si hace falta.
        BarraJson B(double grados) => new()
        {
            Etiqueta = "C", Clase = ClasePieza.Columna, Nivel = "N",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3),
            AnguloGrados = grados, Seccion = S(0.15, 0.25)
        };

        Casi("el giro del modelo pasa a radianes",
            Orientacion.GiroRad(B(90), T(0.15, 0.25)), Math.PI / 2, 1e-9);

        Casi("sin giro en el modelo y sin cambio de orden, no se gira",
            Orientacion.GiroRad(B(0), T(0.15, 0.25)), 0, 1e-9);

        Casi("un tipo al reves aporta un cuarto de vuelta",
            Orientacion.GiroRad(B(0), T(0.25, 0.15)), Math.PI / 2, 1e-9);

        Casi("y los dos se suman",
            Orientacion.GiroRad(B(90), T(0.25, 0.15)), Math.PI, 1e-9);

        Check("un giro de cero no vale la pena aplicarlo", !Orientacion.Vale(0));
        Check("uno de noventa grados si", Orientacion.Vale(Math.PI / 2));
        Check("sin barra no hay giro", Orientacion.GiroRad(null, T(0.25, 0.15)) == 0);
    }

    // ------------------------------------------------------------------
    private static void MuroAlPanoDeLoModelado()
    {
        Console.WriteLine("\n[13] El muro se ajusta a lo MODELADO, no a la seccion del calculo");

        // Un muro de 4 m entre dos castillos de 15x15 en el calculo, con una cadena de 20 de
        // peralte encima. Pero el usuario mapea los castillos a un tipo de 30x30 y la cadena a
        // uno de 40 de peralte: en Revit las piezas miden ESO, y el muro tiene que morir en el
        // pano de esas, no de las del calculo.
        var m = new ModeloJson();
        m.Niveles.Add(new NivelJson { Nombre = "N", ElevacionM = 0 });
        m.Niveles.Add(new NivelJson { Nombre = "N2", ElevacionM = 3 });

        void Castillo(double x)
        {
            m.Barras.Add(new BarraJson
            {
                Etiqueta = "K" + x, Clase = ClasePieza.Columna, Nivel = "N2",
                P1 = P(x, 0, 0), P2 = P(x, 0, 3), Seccion = Rect("K 15X15", 15, 15)
            });
        }

        Castillo(0);
        Castillo(4);

        m.Barras.Add(new BarraJson
        {
            Etiqueta = "CC1", Clase = ClasePieza.Trabe, Nivel = "N2",
            P1 = P(0, 0, 3), P2 = P(4, 0, 3), PuntoCardinal = 8,
            Seccion = Rect("CC 15X20", 15, 20)
        });

        var muro = new PanoJson
        {
            Etiqueta = "M1", Clase = ClasePieza.Muro, Nivel = "N2",
            Seccion = new SeccionJson
            {
                Nombre = "MURO15", Forma = FormaSeccion.Pano, EspesorM = 0.15
            }
        };

        muro.Vertices.AddRange(new[] { P(0, 0, 0), P(4, 0, 0), P(4, 0, 3), P(0, 0, 3) });
        m.Panos.Add(muro);

        // ---- Primero, con las medidas del calculo ----
        var conCalculo = AjusteDeMuros.Ajustar(muro, m);
        var altoCalculo = conCalculo.Contorno.Max(v => v.Z);
        var largoCalculo = conCalculo.Contorno.Max(v => v.X) - conCalculo.Contorno.Min(v => v.X);

        Casi("con la seccion del calculo el muro baja el peralte de 20", altoCalculo, 2.80, 1e-9);
        Casi("y se recorta medio castillo de 15 por punta", largoCalculo, 4 - 0.15, 1e-9);

        // ---- Y ahora con las medidas de los TIPOS elegidos ----
        MedidasModeladas medidor = b => b.Clase == ClasePieza.Columna
            ? (0.30, 0.30)
            : (0.15, 0.40);

        var conTipos = AjusteDeMuros.Ajustar(muro, m, null, medidor);
        var altoTipos = conTipos.Contorno.Max(v => v.Z);
        var largoTipos = conTipos.Contorno.Max(v => v.X) - conTipos.Contorno.Min(v => v.X);

        Casi("con el tipo elegido baja el peralte de 40, que es el que se va a modelar",
            altoTipos, 2.60, 1e-9);
        Casi("y se recorta medio castillo de 30 por punta", largoTipos, 4 - 0.30, 1e-9);

        Check("asi que el ajuste SI depende de las medidas modeladas",
            Math.Abs(altoCalculo - altoTipos) > 0.1 && Math.Abs(largoCalculo - largoTipos) > 0.1,
            "si no, el muro se recorta al pano de una pieza que no existe");

        // Un medidor que no sabe -sin tipo elegido- tiene que dejarlo como el calculo.
        MedidasModeladas nose = _ => null;

        var conNada = AjusteDeMuros.Ajustar(muro, m, null, nose);

        Casi("sin saber las medidas se usan las del calculo",
            conNada.Contorno.Max(v => v.Z), altoCalculo, 1e-9);

        // Y el medidor de verdad: sale del mapeo, y respeta el giro del tipo.
        var cat = CatalogoDePrueba();
        var vista = new VistaMapeo(m, cat);
        vista.AceptarLoMasParecido();
        var real = Orientacion.Medidor(m, vista.AMapeo(), cat);

        var deCastillo = real(m.Barras[0]);

        Check("el medidor del mapeo devuelve medidas para una pieza mapeada",
            deCastillo is not null,
            "sin esto el ajuste volveria a usar la seccion del calculo");

        if (deCastillo is not null)
        {
            Check("y son las del tipo, no las del calculo",
                deCastillo.Value.AnchoM > 0 && deCastillo.Value.PeralteM > 0,
                $"{deCastillo.Value.AnchoM} x {deCastillo.Value.PeralteM}");
        }
    }

    // ------------------------------------------------------------------
    private static void NombresBonitosDeNivel()
    {
        Console.WriteLine("\n[11] Los niveles, con nombre de plano y no de ETABS");

        var m = new ModeloJson();

        void N(string nombre, double z) =>
            m.Niveles.Add(new NivelJson { Nombre = nombre, ElevacionM = z });

        // A proposito EN DESORDEN, y con los numeros de ETABS mintiendo: Story3 esta debajo de
        // Story2. ETABS lista las plantas de arriba abajo, asi que esto pasa de verdad, y
        // numerar por el nombre daria los niveles al reves.
        N("Story2", 5.78);
        N("Base", -0.60);
        N("Story3", 2.89);
        N("Story1", 0.0);

        m.Barras.Add(new BarraJson
        {
            Etiqueta = "C1", Clase = ClasePieza.Columna, Nivel = "Story3",
            P1 = P(0, 0, 0), P2 = P(0, 0, 3), Seccion = Rect("K 15X15", 15, 15)
        });

        var pano = new PanoJson
        {
            Etiqueta = "M1", Clase = ClasePieza.Muro, Nivel = "Story1",
            Seccion = new SeccionJson { Nombre = "M", Forma = FormaSeccion.Pano, EspesorM = 0.15 }
        };

        pano.Vertices.AddRange(new[] { P(0, 0, 0), P(3, 0, 0), P(3, 0, 3), P(0, 0, 3) });
        m.Panos.Add(pano);

        var mapa = NombresDeNivel.Aplicar(m);

        string De(double z) => m.Niveles.First(n => Math.Abs(n.ElevacionM - z) < 1e-9).Nombre;

        Igual("la cota cero es la planta baja", De(0), "Planta baja +0.00");
        Igual("bajo cero es la cimentacion", De(-0.60), "Cimentacion");
        Igual("el primero por encima es el Nvl-01", De(2.89), "Nvl-01 + 2.89");
        Igual("y el siguiente el Nvl-02", De(5.78), "Nvl-02 + 5.78");

        Check("la numeracion va por COTA, no por el numero de ETABS",
            De(2.89).StartsWith("Nvl-01", StringComparison.Ordinal),
            "Story3 esta debajo de Story2, asi que le toca el 01");

        // Y lo que hace que el modelo siga funcionando: las piezas se renombran con ellos.
        Igual("la columna apunta al nombre nuevo", m.Barras[0].Nivel, "Nvl-01 + 2.89");
        Igual("y el muro tambien", m.Panos[0].Nivel, "Planta baja +0.00");

        Check("todos los nombres son distintos",
            m.Niveles.Select(n => n.Nombre).Distinct().Count() == m.Niveles.Count);

        Igual("el mapa dice de donde viene cada uno", mapa["Story1"], "Planta baja +0.00");

        // Dos cimentaciones: los nombres no pueden repetirse, porque Revit rechaza el segundo
        // nivel y con el se pierden todas las piezas que cuelgan de el.
        var dos = new ModeloJson();
        dos.Niveles.Add(new NivelJson { Nombre = "Base", ElevacionM = -0.60 });
        dos.Niveles.Add(new NivelJson { Nombre = "Zapatas", ElevacionM = -1.20 });
        NombresDeNivel.Aplicar(dos);

        Check("con dos niveles bajo cero los dos nombres siguen siendo distintos",
            dos.Niveles[0].Nombre != dos.Niveles[1].Nombre,
            string.Join(" | ", dos.Niveles.Select(n => n.Nombre)));

        // La mas profunda se queda con «Cimentacion» a secas, que es la que lo es de verdad, y
        // la otra lleva su cota pegada para distinguirse.
        Igual("la mas profunda es la cimentacion a secas",
            dos.Niveles.First(n => Math.Abs(n.ElevacionM + 1.20) < 1e-9).Nombre, "Cimentacion");

        Check("y la otra lleva su cota para distinguirse",
            dos.Niveles.First(n => Math.Abs(n.ElevacionM + 0.60) < 1e-9)
                .Nombre.Contains("0.60", StringComparison.Ordinal),
            string.Join(" | ", dos.Niveles.Select(n => n.Nombre)));

        Igual("la cota se rotula con signo y dos decimales",
            NombresDeNivel.Cota(2.891), "+ 2.89");
        Igual("y en negativo tambien", NombresDeNivel.Cota(-0.6), "- 0.60");

        Check("un modelo sin niveles no revienta", NombresDeNivel.Aplicar(new ModeloJson()).Count == 0);
        Check("ni uno nulo", NombresDeNivel.Aplicar(null).Count == 0);
    }

    // ------------------------------------------------------------------
    private static void MallaDeEjes()
    {
        Console.WriteLine("\n[12] La malla de ejes: extremos a paño, interior al eje");

        // Un modelo con muros de 15 cm sobre los ejes X=0 y X=6, y nada sobre el X=3.
        var m = new ModeloJson();
        m.Niveles.Add(new NivelJson { Nombre = "N", ElevacionM = 0 });

        void Muro(double x)
        {
            var p = new PanoJson
            {
                Etiqueta = "M" + x, Clase = ClasePieza.Muro, Nivel = "N",
                Seccion = new SeccionJson
                {
                    Nombre = "M15", Forma = FormaSeccion.Pano, EspesorM = 0.15
                }
            };

            p.Vertices.AddRange(new[] { P(x, 0, 0), P(x, 8, 0), P(x, 8, 3), P(x, 0, 3) });
            m.Panos.Add(p);
        }

        Muro(0);
        Muro(6);

        var cruda = new CuadriculaJson();
        cruda.X.Add(new EjeJson { Id = "1", Ordenada = 0 });
        cruda.X.Add(new EjeJson { Id = "2", Ordenada = 3 });
        cruda.X.Add(new EjeJson { Id = "3", Ordenada = 6 });
        cruda.Y.Add(new EjeJson { Id = "A", Ordenada = 0 });
        cruda.Y.Add(new EjeJson { Id = "B", Ordenada = 8 });

        var puesta = Cuadriculas.Colocar(cruda, m);

        double X(string id) => puesta.X.First(e => e.Id == id).Ordenada;

        Casi("el eje extremo se corre medio espesor hacia fuera", X("1"), -0.075, 1e-9);
        Casi("el del otro extremo, hacia el otro lado", X("3"), 6.075, 1e-9);
        Casi("y el interior NO se mueve", X("2"), 3, 1e-9);

        Check("no se pierde ningun eje", puesta.Cuantos == 5, puesta.Cuantos.ToString());
        Check("y la cuadricula dice que la hay", puesta.Hay);

        // Los de Y no tienen muro a lo largo, asi que se quedan donde estan: los muros de este
        // modelo corren en Y, o sea que CRUZAN los ejes horizontales y no los definen.
        double Y(string id) => puesta.Y.First(e => e.Id == id).Ordenada;

        Casi("un eje sin pieza a lo largo se queda en su sitio", Y("A"), 0, 1e-9);
        Casi("el otro tambien", Y("B"), 8, 1e-9);

        // Ejes repetidos: se quedan con el primero, que trae el nombre bueno.
        var repe = new CuadriculaJson();
        repe.X.Add(new EjeJson { Id = "1", Ordenada = 0 });
        repe.X.Add(new EjeJson { Id = "1-bis", Ordenada = 0.005 });
        repe.X.Add(new EjeJson { Id = "2", Ordenada = 4 });

        var limpia = Cuadriculas.Colocar(repe, m);

        Igual("dos ejes a menos de un centimetro son el mismo", limpia.X.Count, 2);
        Check("y se queda el primero, que trae el nombre bueno",
            limpia.X.Any(e => e.Id == "1") && limpia.X.All(e => e.Id != "1-bis"),
            string.Join(", ", limpia.X.Select(e => e.Id)));

        // Sin modelo no se puede medir el paño, pero los ejes no se pierden.
        var sinModelo = Cuadriculas.Colocar(cruda, null);
        Igual("sin modelo los ejes siguen saliendo", sinModelo.Cuantos, 5);
        Casi("aunque sin correr los extremos",
            sinModelo.X.First(e => e.Id == "1").Ordenada, 0, 1e-9);

        Check("una cuadricula nula da una vacia", !Cuadriculas.Colocar(null, m).Hay);

        // ---- Y viaja por el archivo ----
        m.Cuadricula = puesta;
        var ida = ArchivoModelo.ATexto(m);
        var vuelta = ArchivoModelo.DeTexto(ida);

        Check("la cuadricula sobrevive el ida y vuelta por disco",
            vuelta.Cuadricula is not null && vuelta.Cuadricula.Cuantos == 5,
            vuelta.Cuadricula?.Cuantos.ToString() ?? "null");

        Casi("con las ordenadas ya corridas",
            vuelta.Cuadricula!.X.First(e => e.Id == "1").Ordenada, -0.075, 1e-9);

        // Un archivo de la version anterior no trae cuadricula, y tiene que seguir leyendose:
        // lo que no se puede es que el complemento se niegue a abrir lo que ya esta repartido.
        var vieja = ida
            .Replace("\"Version\": " + ModeloJson.VersionActual.ToString(CultureInfo.InvariantCulture),
                     "\"Version\": 1");

        var deAntes = ArchivoModelo.DeTexto(vieja);

        Igual("un archivo de la version 1 se sigue leyendo", deAntes.Version, 1);

        // Y una cuadricula explicitamente nula no revienta al leerla.
        var sinMalla = ArchivoModelo.DeTexto(ida.Replace("\"Cuadricula\":", "\"CuadriculaX\":"));
        Check("un archivo sin cuadricula se lee sin reventar", sinMalla.Cuadricula is null);

        // Un solo eje en una direccion no se corre: no hay extremo que distinguir.
        var uno = new CuadriculaJson();
        uno.X.Add(new EjeJson { Id = "1", Ordenada = 0 });

        Casi("con un solo eje no se mueve nada",
            Cuadriculas.Colocar(uno, m).X[0].Ordenada, 0, 1e-9);

        // Y el ancho manda: una columna gruesa sobre el eje corre menos que un muro, porque el
        // muro tiene preferencia por ser lo que define el paño.
        var conTrabe = new ModeloJson();
        conTrabe.Niveles.Add(new NivelJson { Nombre = "N", ElevacionM = 0 });
        conTrabe.Barras.Add(new BarraJson
        {
            Etiqueta = "T1", Clase = ClasePieza.Trabe, Nivel = "N",
            P1 = P(0, 0, 3), P2 = P(0, 8, 3),
            Seccion = Rect("T 30X60", 30, 60)
        });
        conTrabe.Barras.Add(new BarraJson
        {
            Etiqueta = "T2", Clase = ClasePieza.Trabe, Nivel = "N",
            P1 = P(6, 0, 3), P2 = P(6, 8, 3),
            Seccion = Rect("T 30X60", 30, 60)
        });

        var conT = Cuadriculas.Colocar(cruda, conTrabe);

        Casi("sin muro, el paño lo marca la trabe",
            conT.X.First(e => e.Id == "1").Ordenada, -0.15, 1e-9);
    }

    // ==================================================================
    //  [13] El armado: de la tabla de CadLink a varillas de Revit
    // ==================================================================
    private static void DespieceEnRevit()
    {
        Console.WriteLine();
        Console.WriteLine("[15] El despiece en Revit: propiedades de tipo, cortes y hoja");

        var k01 = new ArmadoJson
        {
            Id = "K-01", Tipo = "Columna", Elemento = "CASTILLO", BaseCm = 15, AlturaCm = 15,
            Rotulo = new List<string> { "CASTILLO", "\"K-01\"", "4 vars. #3C", "Estr. #3C @15 cm", "Rec. 2 cm" }
        };
        foreach (var (x, y) in new[] { (3.0, 3.0), (12.0, 3.0), (3.0, 12.0), (12.0, 12.0) })
        {
            k01.Varillas.Add(new VarillaJson { Clave = "#3", DiamCm = 0.95, XCm = x, YCm = y });
        }

        var p = PlanDespiece.Propiedades(k01);
        Igual("codigo de montaje = las varillas del rotulo", p.CodigoDeMontaje, "4 vars. #3C");
        Igual("nota clave = CONCRETO", p.NotaClave, "CONCRETO");
        Igual("modelo = sus medidas en cm", p.Modelo, "15 X 15 CM");
        Igual("descripcion = el ID de CadLink", p.Descripcion, "K-01");
        Igual("marca de tipo = el elemento", p.MarcaDeTipo, "CASTILLO");

        var t04 = new ArmadoJson { Id = "T-04", Tipo = "Trabe", BaseCm = 15, AlturaCm = 30 };
        t04.Varillas.Add(new VarillaJson { Clave = "#4", DiamCm = 1.27, XCm = 3, YCm = 3 });
        t04.Varillas.Add(new VarillaJson { Clave = "#4", DiamCm = 1.27, XCm = 12, YCm = 3 });
        t04.Varillas.Add(new VarillaJson { Clave = "#3", DiamCm = 0.95, XCm = 3, YCm = 27 });
        Igual("sin rotulo se cuentan las varillas, de la mas gruesa a la mas delgada",
            PlanDespiece.Propiedades(t04).CodigoDeMontaje, "2 vars. #4C + 1 vars. #3C");
        Igual("y sin elemento, el tipo", PlanDespiece.Propiedades(t04).MarcaDeTipo, "TRABE");

        // ---------- El corte de una trabe ----------
        var marco = MarcoPieza.DeTrabe(new V3(0, 0, 3), new V3(6, 0, 3), 2.7, 15);
        var c = PlanDespiece.Corte(t04, marco);
        Igual("el corte se llama como en el plano", c.Nombre, "Corte T-04 - 6.00m");
        Casi("va a media longitud", c.Origen.X, 3.0);
        Casi("y a media altura de la seccion", c.Origen.Z, 2.85);
        Check("mira a lo largo de la trabe", Math.Abs(Math.Abs(c.EjeZ.X) - 1) < 1e-9);
        Check("con la Y de la vista hacia arriba", Math.Abs(c.EjeY.Z - 1) < 1e-9);
        Check("el sistema es de mano derecha: X por Y da Z",
            (c.EjeX.Cruz(c.EjeY) - c.EjeZ).Largo < 1e-9);
        Check("la caja deja aire a la izquierda para las llamadas y abajo para la etiqueta",
            c.Min.X < -0.075 - 0.3 && c.Min.Y < -0.15 - 0.3 && c.Max.Z == 0 && c.Min.Z < 0);
        Igual("una llamada por renglon y diametro", c.Llamadas.Count, 2);
        Check("la de arriba primero, con su cantidad",
            c.Llamadas[0].Texto == "1 vars. #3C" && c.Llamadas[1].Texto == "2 vars. #4C");
        Check("a la izquierda de la seccion y a la altura de su renglon",
            c.Llamadas.All(l => l.X < -0.075) && c.Llamadas[0].Y > 0 && c.Llamadas[1].Y < 0);
        Check("la etiqueta, debajo de la seccion", c.PuntoDeEtiqueta.Y < -0.15);

        var cc = PlanDespiece.Corte(k01,
            MarcoPieza.DeColumna(new V3(1, 1, 0), new V3(1, 0, 0), new V3(0, 1, 0), 0, 3, 15, 15));
        Igual("la columna se corta sin longitud en el nombre", cc.Nombre, "Corte K-01");
        Check("y se mira desde arriba, como su seccion", Math.Abs(Math.Abs(cc.EjeZ.Z) - 1) < 1e-9);
        Casi("a media altura", cc.Origen.Z, 1.5);

        // ---------- La hoja ----------
        var tam = PlanDespiece.EnPapel(c);
        Casi("a 1:10 el corte mide en el papel la decima parte", tam.Ancho, (c.Max.X - c.Min.X) / 10);
        var centros = PlanDespiece.Acomodo(Enumerable.Repeat((0.1, 0.12), 9).ToList(), 0.5, 0.4);
        Check("los cortes van en renglones de izquierda a derecha", centros[1].X > centros[0].X && Math.Abs(centros[1].Y - centros[0].Y) < 1e-12);
        Check("y al llenarse uno, bajan al siguiente", centros[4].Y < centros[0].Y && Math.Abs(centros[4].X - centros[0].X) < 1e-12);
        Check("ninguno se sale de su hoja", centros.All(q => q.X + 0.05 <= 0.5 && q.X - 0.05 >= 0 && q.Y - 0.06 >= 0));
        Check("caben ocho en la primera hoja y el noveno pasa a la segunda",
            centros.Take(8).All(q => q.Hoja == 0) && centros[8].Hoja == 1);
        Check("y en la segunda empieza arriba a la izquierda",
            Math.Abs(centros[8].X - centros[0].X) < 1e-12 && Math.Abs(centros[8].Y - centros[0].Y) < 1e-12);

        // ---------- Las varillas de esquina, asentadas en el doblez del estribo ----------
        // #2.5 con 31.8 mm de doblez: radio interior 1.59 cm; la #3 de la esquina, radio 0.475 cm.
        var asiento = PlanDespiece.Escala > 0 ? PlanDeArmado.AsientoEnElDoblez(1.59, 0.475) : 0;
        Casi("la varilla de esquina se mete (R - r)(1 - 1/raiz 2) para tocar el doblez",
            asiento, (1.59 - 0.475) * (1 - (1 / Math.Sqrt(2))), 1e-12);
        Igual("con un doblez que no pasa de la varilla no se mueve", PlanDeArmado.AsientoEnElDoblez(0.3, 0.475), 0.0);

        var k = new ArmadoJson
        {
            Id = "K", Tipo = "Columna", BaseCm = 15, AlturaCm = 20, RecubrimientoCm = 2, DiamEstriboCm = 0.79
        };
        foreach (var (x, y) in new[] { (3.27, 3.27), (11.73, 3.27), (3.27, 10.0), (11.73, 10.0), (3.27, 16.73), (11.73, 16.73) })
        {
            k.Varillas.Add(new VarillaJson { Clave = "#3", DiamCm = 0.95, XCm = x, YCm = y });
        }
        var mk = MarcoPieza.DeColumna(new V3(0, 0, 0), new V3(1, 0, 0), new V3(0, 1, 0), 0, 3, 15, 20);
        var sin = PlanDeArmado.Armar(k, new ArmadoBarraJson(), mk);
        var con = PlanDeArmado.Armar(k, new ArmadoBarraJson(), mk, 1.59);
        double Xde(VarillaArmada v) => v.Puntos[0].X * 100 + 7.5;
        double Yde(VarillaArmada v) => v.Puntos[0].Y * 100 + 10;
        Check("sin radio, las varillas quedan donde las pone la tabla", Math.Abs(Xde(sin[0]) - 3.27) < 1e-9);
        Check("con el doblez de Revit, la de esquina se asienta hacia dentro en X y en Y",
            Math.Abs(Xde(con[0]) - (3.27 + asiento)) < 1e-9 && Math.Abs(Yde(con[0]) - (3.27 + asiento)) < 1e-9);
        Check("y la de la otra esquina, hacia su lado", Math.Abs(Xde(con[5]) - (11.73 - asiento)) < 1e-9
            && Math.Abs(Yde(con[5]) - (16.73 - asiento)) < 1e-9);
        Check("las de en medio de los costados no se mueven", Math.Abs(Yde(con[2]) - 10.0) < 1e-9
            && Math.Abs(Xde(con[2]) - 3.27) < 1e-9);

        // ---------- La misma seccion para todas las de su medida ----------
        var s1530 = new ArmadoJson { Id = "T-04", Tipo = "Trabe", BaseCm = 15, AlturaCm = 30, SeparacionesCm = new() { 15, 15, 15 } };
        var otra = new ArmadoJson { Id = "T-09", Tipo = "Trabe", BaseCm = 15, AlturaCm = 30, SeparacionesCm = new() { 10, 20, 10 } };
        var tipos = new List<TipoArmable>
        {
            new() { Id = 1, Clase = ClasePieza.Trabe, Familia = "Viga", Tipo = "V 15x30 A", Piezas = 5, DeConcreto = 5, AnchoM = 0.15, PeralteM = 0.30 },
            new() { Id = 2, Clase = ClasePieza.Trabe, Familia = "Viga", Tipo = "V 15x30 B", Piezas = 7, DeConcreto = 7, AnchoM = 0.15, PeralteM = 0.30 },
            new() { Id = 3, Clase = ClasePieza.Trabe, Familia = "Viga", Tipo = "V 20x40", Piezas = 2, DeConcreto = 2, AnchoM = 0.20, PeralteM = 0.40 },
            new() { Id = 4, Clase = ClasePieza.Columna, Familia = "Col", Tipo = "C 15x30", Piezas = 2, DeConcreto = 2, AnchoM = 0.15, PeralteM = 0.30 }
        };
        var v = new VistaArmadoPorTipo(tipos, new List<ArmadoJson> { s1530, otra });
        var a = v.Filas.Single(f => f.Tipo.Id == 1);
        var b = v.Filas.Single(f => f.Tipo.Id == 2);
        Check("dos filas de 15x30 sin nombre conocido no se sugieren: hay dos secciones con esa medida",
            a.Seccion == FilaArmadoTipo.SinArmar && b.Seccion == FilaArmadoTipo.SinArmar);
        a.Seccion = "T-09";
        Check("al elegir la de un tipo, la toman todos los de la misma medida",
            b.Seccion == "T-09" && b.Armar && b.Sugerencia.Contains("misma sección"));
        Check("los de otra medida no", v.Filas.Single(f => f.Tipo.Id == 3).Seccion == FilaArmadoTipo.SinArmar);
        Check("ni una columna de la misma medida", v.Filas.Single(f => f.Tipo.Id == 4).Seccion == FilaArmadoTipo.SinArmar);
        b.Seccion = "T-04";
        a.Seccion = FilaArmadoTipo.SinArmar;
        Check("lo que la persona eligio a mano no se pisa", b.Seccion == "T-04");
        Check("los ajustes del despiece vienen encendidos", v.EscribirPropiedades && v.CrearDespiece);
    }

    private static void ArmadoPorTipo()
    {
        Console.WriteLine();
        Console.WriteLine("[14] Armar por tipo de Revit: la receta y el cuadro");

        ArmadoJson Fila(string id, string tipo, double b, double h) => new()
        {
            Id = id, Tipo = tipo, BaseCm = b, AlturaCm = h, RecubrimientoCm = 2.5,
            ClaveEstribo = "#3", DiamEstriboCm = 0.95,
            SeparacionesCm = new List<double> { 10, 20, 10 }
        };

        var t01 = Fila("T-01", "Trabe", 25, 50);
        t01.Varillas.Add(new VarillaJson { Clave = "#5", DiamCm = 1.59, XCm = 4.2, YCm = 45.8, Lecho = "Superior" });
        t01.QuitarEstribosExtremos = true;
        t01.MargenBastonesM = 0.05;
        t01.Bastones.Add(new BastonRecetaJson
        {
            Clave = "#5", DiamCm = 1.59, Lecho = "Superior", XsCm = new List<double> { 8, 17 },
            YCm = 43, Ubicacion = "Extremos", DistanciaM = 1.2
        });
        var t02 = Fila("T-02", "Trabe", 30, 60);
        var c01 = Fila("C-01", "Columna", 40, 50);
        var vieja = new ArmadoJson { Id = "V-9", Tipo = "Trabe", BaseCm = 30, AlturaCm = 60 };

        // ---------- La receta, con la longitud de la pieza de Revit ----------
        var ab = RecetaArmado.ParaLargo(t01, 6.0);
        Check("la receta da estribos para la longitud real", ab.EstribosM.Count > 10);
        Check("y todos caen dentro de la pieza", ab.EstribosM.All(x => x > 0 && x < 6.0));
        Check("los estribos son los del alzado, sin los dos de los extremos por los bastones",
            ab.EstribosM.Count == new List<double>(RecetaArmado.ParaLargo(t02, 6.0).EstribosM).Count - 2);
        Igual("baston de extremos: dos tramos", ab.Bastones.Count, 2);
        Casi("el primero arranca en el margen", ab.Bastones[0].IniM, 0.05);
        Casi("y mide su distancia", ab.Bastones[0].FinM, 1.25);
        Casi("el segundo acaba en el margen del otro lado", ab.Bastones[1].FinM, 5.95);
        Check("con sus dos varillas en la cama", ab.Bastones.All(t => t.XsCm.Count == 2 && t.GanchoIni && t.GanchoFin));

        var corta = RecetaArmado.Tramos("Extremos", 1.2, 2.0, 0.05);
        Casi("en una trabe corta los de extremo no pasan de la mitad util", corta[0].Fin, 0.05 + 0.95);
        Casi("al centro, centrado", RecetaArmado.Tramos("AlCentro", 2, 6, 0.05)[0].Ini, 2.0);
        Igual("sin distancia no hay baston", RecetaArmado.Tramos("Extremos", 0, 6, 0.05).Count, 0);

        var col = RecetaArmado.ParaLargo(c01, 3.0);
        var colComoTrabe = RecetaArmado.ParaLargo(Fila("X", "Dado", 40, 50), 3.0);
        Check("la columna pierde el ultimo estribo, como en el alzado; el dado no",
            col.EstribosM.Count == colComoTrabe.EstribosM.Count - 1);

        Igual("una fila sin receta no da estribos", RecetaArmado.ParaLargo(vieja, 6).EstribosM.Count, 0);

        // ---------- El archivo ----------
        var archivo = new ArchivoArmadoJson { Aplicacion = "CadLink 1.1.0", Armados = { t01, c01 } };
        var leido = ArchivoArmado.DeTexto(ArchivoArmado.ATexto(archivo));
        Check("el archivo de secciones va y vuelve con su receta",
            leido.Armados.Count == 2 && leido.Armados[0].TieneReceta
            && leido.Armados[0].Bastones.Count == 1 && leido.Armados[0].Bastones[0].Ubicacion == "Extremos");

        var futuro = false;
        try { ArchivoArmado.DeTexto("{\"Version\": 99}"); }
        catch (InvalidDataException) { futuro = true; }
        Check("un archivo de una version futura se rechaza", futuro);

        // ---------- El cuadro ----------
        var tipos = new List<TipoArmable>
        {
            new() { Id = 1, Clase = ClasePieza.Trabe, Familia = "Viga-Concreto", Tipo = "T-01", Piezas = 40, DeConcreto = 40, AnchoM = 0.25, PeralteM = 0.5 },
            new() { Id = 2, Clase = ClasePieza.Trabe, Familia = "Viga-Concreto", Tipo = "30 x 60cm", Piezas = 12, DeConcreto = 12, AnchoM = 0.3, PeralteM = 0.6, YaArmadas = 3 },
            new() { Id = 3, Clase = ClasePieza.Columna, Familia = "Columna-Concreto", Tipo = "50 x 40cm", Piezas = 20, DeConcreto = 20, AnchoM = 0.5, PeralteM = 0.4 },
            new() { Id = 4, Clase = ClasePieza.Trabe, Familia = "W-Wide Flange", Tipo = "W12X26", Piezas = 8, DeConcreto = 0, AnchoM = 0.17, PeralteM = 0.31 },
            new() { Id = 5, Clase = ClasePieza.Trabe, Familia = "Viga", Tipo = "Sin usar", Piezas = 0 }
        };

        var vista = new VistaArmadoPorTipo(tipos, new List<ArmadoJson> { t01, t02, c01, vieja });

        Igual("salen los tipos con piezas, uno por fila", vista.Filas.Count, 4);
        Check("las columnas primero", vista.Filas[0].Categoria == "Columna");
        Check("la seccion sin receta no se ofrece", vista.Filas.All(f => !f.Secciones.Contains("V-9")));

        var fT01 = vista.Filas.Single(f => f.Tipo.Id == 1);
        Check("el tipo que se llama como la fila sale sugerido por nombre y marcado",
            fT01.Seccion == "T-01" && fT01.Armar && fT01.Sugerencia.Contains("nombre"));

        var f3060 = vista.Filas.Single(f => f.Tipo.Id == 2);
        Check("el que no se llama como ninguna, por sus medidas",
            f3060.Seccion == "T-02" && f3060.Sugerencia.Contains("medidas"));
        Check("y avisa que ya tiene armado y se rehace", f3060.Diagnostico.Contains("se rehacen"));

        var fCol = vista.Filas.Single(f => f.Tipo.Id == 3);
        Check("la columna girada tambien se reconoce por medidas", fCol.Seccion == "C-01" && fCol.Coherente);
        Check("a una columna no se le ofrecen trabes", !fCol.Secciones.Contains("T-01"));

        var fAcero = vista.Filas.Single(f => f.Tipo.Id == 4);
        fAcero.Seccion = "T-02";
        Check("el acero no se puede marcar para armar", !fAcero.Armar);
        Check("y se dice por que", fAcero.Diagnostico.Contains("no son de concreto") || fAcero.Diagnostico.Contains("de concreto"));

        fT01.Seccion = "T-02";
        Check("elegir una seccion que no mide lo mismo avisa", !fT01.Coherente && fT01.Diagnostico.Contains("OJO"));

        Igual("se arman las piezas de concreto de los tipos marcados", vista.PiezasAArmar, 40 + 12 + 20);

        fT01.Seccion = FilaArmadoTipo.SinArmar;
        Check("con «no armar» la fila se desmarca", !fT01.Armar && fT01.Armado is null);
        Check("y el resumen lo cuenta", vista.Resumen.Contains("32 pieza(s)"));

        vista.MarcarTodas(false);
        Igual("desmarcar todas", vista.PiezasAArmar, 0);
        vista.MarcarTodas(true);
        Igual("y volver a marcar solo las que tienen seccion y son de concreto", vista.PiezasAArmar, 32);
        // ---------- Los tipos de armadura: los de la oficina ----------
        var nombres = new[]
        {
            "VAR #2 ESTRIBOS COLUMNAS/CASTILLOS", "VAR #2 ESTRIBOS TRABES/CADENAS",
            "VAR #2.5C ESTRIBOS COLUMNAS/CASTILLOS", "VAR #2.5C ESTRIBOS TRABES/CADENAS",
            "VAR #3C BASTON", "VAR #3C COLUMNAS/CASTILLOS", "VAR #3C ESTRIBOS COLUMNAS/CASTILLOS",
            "VAR #3C ESTRIBOS TRABES/CADENAS", "VAR #3C GRAPAS", "VAR #3C LOSAS", "VAR #3C MUROS CONC/MAMP",
            "VAR #3C TRABES", "VAR #3C ZAPATA CORRIDA/AISLADA", "VAR #4C BASTON", "VAR #4C COLUMNAS/CASTILLOS",
            "VAR #4C ESTRIBOS COLUMNAS/CASTILLOS", "VAR #4C ESTRIBOS TRABES", "VAR #4C MUROS CONC/MAMP",
            "VAR #4C TRABES", "VAR #4C ZAPATAS CORRIDA/AISLADA", "VAR #5C BASTON", "VAR #5C COLUMNAS/CASTILLOS",
            "VAR #5C MUROS DE CONCRETO", "VAR #5C TRABES", "VAR #6C BASTON", "VAR #6C COLUMNAS",
            "VAR #6C TRABES", "VAR #8C COLUMNAS", "VAR #8C TRABES"
        };
        var deRevit = nombres.Select((n, i) => new TipoDeVarillaRevit { Id = 100 + i, Nombre = n }).ToList();

        string? Mejor(string clave, string pieza, string uso) =>
            SugerirVarilla.Mejor(clave, 0, pieza, uso, deRevit)?.Nombre;

        Igual("corrida #4 de trabe", Mejor("#4", "Trabe", "Corrida"), "VAR #4C TRABES");
        Igual("corrida #4 de columna", Mejor("#4", "Columna", "Corrida"), "VAR #4C COLUMNAS/CASTILLOS");
        Igual("baston #5", Mejor("#5", "Trabe", "Baston"), "VAR #5C BASTON");
        Igual("estribo #3 de trabe", Mejor("#3", "Trabe", "Estribo"), "VAR #3C ESTRIBOS TRABES/CADENAS");
        Igual("estribo #3 de columna", Mejor("#3", "Columna", "Estribo"), "VAR #3C ESTRIBOS COLUMNAS/CASTILLOS");
        Igual("el #2.5 no es el #2", Mejor("#2.5", "Trabe", "Estribo"), "VAR #2.5C ESTRIBOS TRABES/CADENAS");
        Igual("y el #2 no es el #2.5", Mejor("#2", "Columna", "Estribo"), "VAR #2 ESTRIBOS COLUMNAS/CASTILLOS");
        Igual("corrida #6 de columna", Mejor("#6", "Columna", "Corrida"), "VAR #6C COLUMNAS");
        Igual("sin tipo de ese numero, nada", Mejor("#10", "Trabe", "Corrida"), null);

        var conVarillas = new VistaArmadoPorTipo(tipos, new List<ArmadoJson> { t01, t02, c01 }, deRevit,
            new Dictionary<string, string> { ["Trabe|Corrida|#5"] = "VAR #5C MUROS DE CONCRETO" });
        var claves = conVarillas.Varillas.Select(v => v.Llave).ToList();
        Check("la tabla de armaduras sale de las secciones marcadas",
            claves.Contains("Trabe|Corrida|#5") && claves.Contains("Trabe|Baston|#5")
            && claves.Contains("Trabe|Estribo|#3") && claves.Contains("Columna|Estribo|#3"));
        var corrida5 = conVarillas.Varillas.Single(v => v.Llave == "Trabe|Corrida|#5");
        Check("y respeta lo que se eligio la vez pasada", corrida5.Elegido == "VAR #5C MUROS DE CONCRETO"
            && corrida5.Origen.Contains("vez pasada"));
        Check("lo demas sale sugerido por su nombre",
            conVarillas.Varillas.Single(v => v.Llave == "Trabe|Baston|#5").Elegido == "VAR #5C BASTON");

        var est = new VarillaArmada("#3", 0.0095, EstiloVarilla.Estribo, new List<V3>(),
            GanchoVarilla.De135, GanchoVarilla.De135, LadoGancho.Izquierda, LadoGancho.Izquierda,
            new V3(1, 0, 0), 1, 0, "estribo");
        Igual("el armador recibe el tipo elegido para el estribo de la trabe",
            conVarillas.IdDeVarilla(t01, est), deRevit.Single(t => t.Nombre == "VAR #3C ESTRIBOS TRABES/CADENAS").Id);

        corrida5.Elegido = FilaVarilla.Automatico;
        Check("en automatico no se fuerza ningun tipo",
            conVarillas.IdDeVarilla(t01, est with { Que = "corrida", Clave = "#5" }) is null);
        Check("y lo automatico no se recuerda", !conVarillas.Elecciones().ContainsKey("Trabe|Corrida|#5")
            && conVarillas.Elecciones().ContainsKey("Trabe|Baston|#5"));

        conVarillas.Filas.Single(f => f.Tipo.Id == 3).Seccion = FilaArmadoTipo.SinArmar;
        Check("al desmarcar la columna se van sus varillas de la tabla",
            !conVarillas.Varillas.Any(v => v.Pieza == "Columna"));
    }

    private static void Armado()
    {
        Console.WriteLine();
        Console.WriteLine("[13] El armado: de la tabla de CadLink a varillas de Revit");

        var t01 = new ArmadoJson
        {
            Id = "T-01", Tipo = "Trabe", BaseCm = 20, AlturaCm = 40, RecubrimientoCm = 2.5,
            ClaveEstribo = "#3", DiamEstriboCm = 0.95
        };
        t01.Varillas.Add(new VarillaJson { Clave = "#5", DiamCm = 1.59, XCm = 4.25, YCm = 35.75, Lecho = "Superior" });
        t01.Varillas.Add(new VarillaJson { Clave = "#5", DiamCm = 1.59, XCm = 15.75, YCm = 4.25, Lecho = "Inferior" });
        t01.Varillas.Add(new VarillaJson { Clave = "#3", DiamCm = 0.95, XCm = 4.0, YCm = 20, Lecho = "Lateral" });

        var c01 = new ArmadoJson { Id = "C-01", Tipo = "Columna", BaseCm = 40, AlturaCm = 40, RecubrimientoCm = 3 };
        var t02 = new ArmadoJson { Id = "T-02", Tipo = "Trabe", BaseCm = 30, AlturaCm = 60 };
        var t03 = new ArmadoJson { Id = "T-03", Tipo = "Trabe", BaseCm = 30, AlturaCm = 60 };
        var armados = new List<ArmadoJson> { t01, c01, t02, t03 };

        // ---------- Emparejar la seccion de ETABS con la fila ----------
        var porNombre = EmparejarArmado.Buscar("t 01", ClasePieza.Trabe, 0.2, 0.4, armados);
        Check("por nombre, sin mayusculas, espacios ni guiones", porNombre.Armado == t01 && porNombre.Por == "nombre");

        var porMedidas = EmparejarArmado.Buscar("V20X40", ClasePieza.Trabe, 0.2, 0.4, armados);
        Check("si no hay nombre, la unica fila con sus medidas", porMedidas.Armado == t01 && porMedidas.Por == "medidas");

        var ambigua = EmparejarArmado.Buscar("V30X60", ClasePieza.Trabe, 0.3, 0.6, armados);
        Check("dos filas con las mismas medidas: no se elige ninguna, y se dice cuales",
            ambigua.Armado is null && ambigua.Motivo.Contains("T-02") && ambigua.Motivo.Contains("T-03"));

        Check("una columna no se arma con una fila de trabe",
            EmparejarArmado.Buscar("T-01", ClasePieza.Columna, 0.2, 0.4, armados).Armado is null);

        Check("ni una losa con nada",
            EmparejarArmado.Buscar("T-01", ClasePieza.Losa, 0.2, 0.4, armados).Armado is null);

        // ---------- Los estribos en juegos ----------
        var juegos = PlanDeArmado.Juegos(new[] { 0.05, 0.10, 0.15, 0.20, 0.40, 0.60, 0.65, 0.70 });
        Igual("tres juegos: 5 cm, 20 cm, 5 cm", juegos.Count, 3);
        Check("el primero, de 4 a 5 cm", juegos[0].Cuenta == 4 && Math.Abs(juegos[0].PasoM - 0.05) < 1e-9);
        Check("el de en medio, de 20 cm; la frontera no se duplica",
            juegos[1].Cuenta == 2 && Math.Abs(juegos[1].InicioM - 0.40) < 1e-9 && Math.Abs(juegos[1].PasoM - 0.20) < 1e-9);
        Check("y el ultimo juego cierra en el ultimo estribo", juegos[2].Cuenta == 2 && Math.Abs(juegos[2].InicioM - 0.65) < 1e-9);
        Igual("un estribo solo es un juego de uno", PlanDeArmado.Juegos(new[] { 1.0 }).Single().Cuenta, 1);
        Igual("sin estribos no hay juegos", PlanDeArmado.Juegos(Array.Empty<double>()).Count, 0);
        Igual("los centros desordenados se ordenan", PlanDeArmado.Juegos(new[] { 0.2, 0.1, 0.3 }).Single().Cuenta, 3);

        // ---------- El marco de una trabe y sus varillas ----------
        var marco = MarcoPieza.DeTrabe(new V3(0, 0, 3), new V3(6, 0, 3), 2.6, 20);
        Casi("la trabe mide 6 m", marco.LargoM, 6);
        Casi("su seccion se centra en la linea", marco.En(10, 0, 0).Y, 0, 1e-12);
        Casi("y se apoya en la cara de abajo", marco.En(0, 0, 0).Z, 2.6, 1e-12);

        var ab = new ArmadoBarraJson { Id = "T-01", LargoM = 6, EstribosM = new List<double> { 0.05, 0.10, 0.15, 3.0 } };
        ab.Bastones.Add(new TramoBastonJson
        {
            Clave = "#4", DiamCm = 1.27, Lecho = "Superior", XsCm = new List<double> { 4, 16 }, YCm = 33,
            IniM = 0.05, FinM = 1.55, GanchoIni = true, GanchoFin = true
        });

        var v = PlanDeArmado.Armar(t01, ab, marco);
        var corridas = v.Where(x => x.Que == "corrida").ToList();

        Igual("dos corridas", corridas.Count, 2);
        Casi("la corrida va de recubrimiento a recubrimiento", corridas[0].Puntos[0].X, 0.025, 1e-12);
        Casi("hasta el otro extremo", corridas[0].Puntos[1].X, 5.975, 1e-12);
        Check("las corridas de la trabe llevan gancho de 90 en las dos puntas",
            corridas.All(c => c.GanchoIni == GanchoVarilla.De90 && c.GanchoFin == GanchoVarilla.De90));
        Check("la lateral va recta", v.Single(x => x.Que == "lateral").GanchoIni == GanchoVarilla.Ninguno);

        // El gancho dobla HACIA DENTRO: arriba hacia abajo, abajo hacia arriba. Deben quedar en
        // lados opuestos, porque la normal y la direccion son las mismas.
        Check("los ganchos de arriba y de abajo doblan hacia lados opuestos",
            corridas[0].LadoIni != corridas[1].LadoIni);

        var bast = v.Where(x => x.Que == "baston").ToList();
        Igual("un baston de dos varillas son dos varillas", bast.Count, 2);
        Check("con su tramo y sus ganchos", Math.Abs(bast[0].Puntos[0].X - 0.05) < 1e-12
            && Math.Abs(bast[0].Puntos[1].X - 1.55) < 1e-12 && bast[0].GanchoFin == GanchoVarilla.De90);
        Check("y doblan hacia el mismo lado que la corrida de su lecho", bast[0].LadoIni == corridas[0].LadoIni);

        var est = v.Where(x => x.Que == "estribo").ToList();
        Igual("los cuatro estribos son dos juegos", est.Count, 2);
        Check("el primer juego lleva los tres de 5 cm", est[0].Cuenta == 3 && Math.Abs(est[0].PasoM - 0.05) < 1e-9);
        Igual("el estribo es una vuelta cerrada de cuatro lados", est[0].Puntos.Count, 5);
        Check("que arranca y acaba en la misma esquina", est[0].Puntos[0] == est[0].Puntos[4]);
        Casi("por el eje del alambre: medio diametro por dentro del recubrimiento",
            est[0].Puntos[0].Z, 2.6 + ((40 - 2.5 - 0.475) / 100), 1e-12);
        Check("con sus dos ganchos de 135, hacia el mismo lado",
            est[0].GanchoIni == GanchoVarilla.De135 && est[0].LadoIni == est[0].LadoFin);
        Check("y su normal es el eje de la pieza, hacia donde se reparte",
            Math.Abs(est[0].Normal.X - 1) < 1e-12);

        // ---------- El lado del gancho ----------
        var arriba = new V3(0, 0, 1);
        Check("el lado es izquierda o derecha segun la normal",
            PlanDeArmado.Lado(new V3(0, 1, 0), new V3(1, 0, 0), arriba * -1)
            != PlanDeArmado.Lado(new V3(0, 1, 0), new V3(1, 0, 0), arriba));

        // ---------- Una columna ----------
        var col = MarcoPieza.DeColumna(new V3(5, 5, 0), new V3(1, 0, 0), new V3(0, 1, 0), 0, 3, 40, 40);
        Casi("la columna mide su altura", col.LargoM, 3);
        Casi("y su esquina queda a medio lado del centro", col.Origen.X, 4.8, 1e-12);
        c01.Varillas.Add(new VarillaJson { Clave = "#6", DiamCm = 1.9, XCm = 5, YCm = 5, Lecho = "Superior" });
        var vc = PlanDeArmado.Armar(c01, new ArmadoBarraJson(), col);
        Check("en la columna las varillas van rectas y verticales",
            vc.Single().GanchoIni == GanchoVarilla.Ninguno && Math.Abs(vc.Single().Puntos[1].Z - 2.97) < 1e-12);

        // ---------- El archivo ----------
        var m = new ModeloJson();
        m.Barras.Add(new BarraJson { Etiqueta = "B1", Clase = ClasePieza.Trabe, Armado = ab });
        m.Armados.Add(t01);
        var vuelta = ArchivoModelo.DeTexto(ArchivoModelo.ATexto(m));
        Igual("el archivo es de la version 4", vuelta.Version, 4);
        Check("y el armado sobrevive al ida y vuelta",
            vuelta.Armados.Single().Varillas.Count == 3
            && vuelta.Barras.Single().Armado!.Bastones.Single().XsCm.Count == 2);
        Check("un archivo de la 3, sin armado, se sigue leyendo",
            ArchivoModelo.DeTexto("{\"Version\":3,\"Barras\":[{\"Etiqueta\":\"B1\"}]}").Barras.Single().Armado is null);
    }
}
