using System.Globalization;
using System.Text;
using CadLink.Ifc;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba del exportador de IFC.
/// </summary>
/// <remarks>
/// Lo que mas se comprueba es la COLOCACION de cada pieza, porque es donde un error no se
/// ve: un IFC con las piezas mal puestas abre igual de bien que uno correcto, y el error
/// aparece en Revit como un modelo "parecido" al bueno. Asi que no se mira el archivo, se
/// reconstruye la terna de cada colocacion y se comprueba que el solido empieza y acaba
/// donde dice el modelo.
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

    private static void Casi(string nombre, double dio, double esperado, double tol = 1e-9) =>
        Check(nombre, Math.Abs(dio - esperado) <= tol,
            $"esperaba {esperado} y dio {dio}");

    private static void CasiVec(string nombre, double[] dio, double[] esperado, double tol = 1e-9)
    {
        var ok = dio.Length == esperado.Length;

        for (var i = 0; ok && i < dio.Length; i++)
        {
            ok = Math.Abs(dio[i] - esperado[i]) <= tol;
        }

        Check(nombre, ok, $"esperaba ({Junta(esperado)}) y dio ({Junta(dio)})");
    }

    private static string Junta(double[] v) =>
        string.Join(", ", v.Select(x => x.ToString("0.######", CultureInfo.InvariantCulture)));

    // ==================================================================
    //  Un lector minimo de STEP, para poder afirmar sobre el archivo
    // ==================================================================

    private sealed record Ent(int Id, string Tipo, List<string> Args);

    private static Dictionary<int, Ent> Leer(string texto)
    {
        var salida = new Dictionary<int, Ent>();
        var datos = texto.Split("DATA;", 2)[1].Split("ENDSEC;", 2)[0];

        foreach (var bruto in Partir(datos))
        {
            var linea = bruto.Trim();

            if (!linea.StartsWith('#'))
            {
                continue;
            }

            var igual = linea.IndexOf('=');
            var abre = linea.IndexOf('(', igual);

            if (igual < 0 || abre < 0 || !linea.EndsWith(')'))
            {
                continue;
            }

            var id = int.Parse(linea[1..igual], CultureInfo.InvariantCulture);
            var tipo = linea[(igual + 1)..abre].Trim();
            var args = Argumentos(linea[(abre + 1)..^1]);

            salida[id] = new Ent(id, tipo, args);
        }

        return salida;
    }

    /// <summary>Parte el cuerpo en entidades, respetando los apostrofos.</summary>
    private static IEnumerable<string> Partir(string cuerpo)
    {
        var sb = new StringBuilder();
        var enTexto = false;

        for (var i = 0; i < cuerpo.Length; i++)
        {
            var c = cuerpo[i];

            if (c == '\'')
            {
                // Dos apostrofos seguidos son un apostrofo dentro del texto, no el cierre.
                if (enTexto && i + 1 < cuerpo.Length && cuerpo[i + 1] == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }

                enTexto = !enTexto;
                sb.Append(c);
                continue;
            }

            if (c == ';' && !enTexto)
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    /// <summary>Separa los argumentos de primer nivel.</summary>
    private static List<string> Argumentos(string dentro)
    {
        var salida = new List<string>();
        var sb = new StringBuilder();
        var hondo = 0;
        var enTexto = false;

        for (var i = 0; i < dentro.Length; i++)
        {
            var c = dentro[i];

            if (c == '\'')
            {
                if (enTexto && i + 1 < dentro.Length && dentro[i + 1] == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }

                enTexto = !enTexto;
                sb.Append(c);
                continue;
            }

            if (!enTexto)
            {
                if (c == '(')
                {
                    hondo++;
                }
                else if (c == ')')
                {
                    hondo--;
                }
                else if (c == ',' && hondo == 0)
                {
                    salida.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
            }

            sb.Append(c);
        }

        if (sb.Length > 0 || salida.Count > 0)
        {
            salida.Add(sb.ToString().Trim());
        }

        return salida;
    }

    private static int Refe(string arg) =>
        int.Parse(arg.TrimStart('#'), CultureInfo.InvariantCulture);

    private static double[] Tripleta(Dictionary<int, Ent> m, string arg)
    {
        var e = m[Refe(arg)];
        var lista = Argumentos(e.Args[0].Trim('(', ')'));

        return lista.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    }

    private static IEnumerable<Ent> Todas(Dictionary<int, Ent> m, string tipo) =>
        m.Values.Where(e => e.Tipo == tipo);

    // ==================================================================
    //  Modelo de prueba
    // ==================================================================

    private static ModeloIfc ModeloDePrueba()
    {
        var m = new ModeloIfc { Programa = "ETABS", Archivo = "ejemplo.EDB", Obra = "Edificio ÑOÑO" };

        m.Niveles.Add(new NivelIfc { Nombre = "Base", ElevacionM = 0 });
        m.Niveles.Add(new NivelIfc { Nombre = "Story1", ElevacionM = 3 });
        m.Niveles.Add(new NivelIfc { Nombre = "Story2", ElevacionM = 6 });

        // Una columna de 3 m, de Base a Story1, con la seccion de 30 x 60.
        m.Barras.Add(new BarraIfc
        {
            Etiqueta = "C1",
            Clase = ClaseIfc.Columna,
            Nivel = "Story1",
            X1 = 0, Y1 = 0, Z1 = 0,
            X2 = 0, Y2 = 0, Z2 = 3,
            E1 = new double[] { 0, 0, 1 },
            E2 = new double[] { 1, 0, 0 },
            E3 = new double[] { 0, 1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "C 30x60",
                Forma = FormaIfc.Rectangulo,
                AnchoM = 0.30,
                PeralteM = 0.60,
                Material = "CONC"
            }
        });

        // Una trabe de 5 m sobre el eje X, a la cota de Story1, con perfil I.
        m.Barras.Add(new BarraIfc
        {
            Etiqueta = "T1",
            Clase = ClaseIfc.Trabe,
            Nivel = "Story1",
            X1 = 0, Y1 = 0, Z1 = 3,
            X2 = 5, Y2 = 0, Z2 = 3,
            E1 = new double[] { 1, 0, 0 },
            E2 = new double[] { 0, 0, 1 },
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "IPR 356x171",
                Forma = FormaIfc.PerfilI,
                AnchoM = 0.171,
                PeralteM = 0.356,
                PatinM = 0.0113,
                AlmaM = 0.0069,
                Material = "A992Fy50"
            }
        });

        // Una diagonal, para que salga IfcMember con .BRACE.
        m.Barras.Add(new BarraIfc
        {
            Etiqueta = "D1",
            Clase = ClaseIfc.Diagonal,
            Nivel = "Story1",
            X1 = 0, Y1 = 0, Z1 = 0,
            X2 = 3, Y2 = 0, Z2 = 3,
            E1 = Unit(3, 0, 3),
            E2 = Unit(-3, 0, 3),
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "OC 6",
                Forma = FormaIfc.Tubo,
                AnchoM = 0.168,
                PeralteM = 0.168,
                ParedM = 0.0071,
                Material = "A500"
            }
        });

        // Un angulo, que es el caso donde el espejo se notaria.
        m.Barras.Add(new BarraIfc
        {
            Etiqueta = "L1",
            Clase = ClaseIfc.Trabe,
            Nivel = "Story2",
            X1 = 0, Y1 = 2, Z1 = 6,
            X2 = 4, Y2 = 2, Z2 = 6,
            E1 = new double[] { 1, 0, 0 },
            E2 = new double[] { 0, 0, 1 },
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "LI 102x102x9.5",
                Forma = FormaIfc.PerfilL,
                AnchoM = 0.102,
                PeralteM = 0.102,
                AlmaM = 0.0095,
                Material = "A36"
            }
        });

        // Una losa cuadrada de 5 x 5 a la cota de Story1.
        var losa = new PanoIfc
        {
            Etiqueta = "L-1",
            Clase = ClaseIfc.Losa,
            Nivel = "Story1",
            Seccion = "LOSA 12",
            EspesorM = 0.12,
            Material = "CONC"
        };
        losa.Vertices.Add((0, 0, 3));
        losa.Vertices.Add((5, 0, 3));
        losa.Vertices.Add((5, 5, 3));
        losa.Vertices.Add((0, 5, 3));
        losa.Vertices.Add((0, 0, 3));   // repetido a proposito: debe limpiarse
        m.Panos.Add(losa);

        // Un muro de 3 m de alto en el plano XZ.
        var muro = new PanoIfc
        {
            Etiqueta = "M-1",
            Clase = ClaseIfc.Muro,
            Nivel = "Story1",
            Seccion = "MURO 20",
            EspesorM = 0.20,
            Material = "CONC"
        };
        muro.Vertices.Add((0, 0, 0));
        muro.Vertices.Add((5, 0, 0));
        muro.Vertices.Add((5, 0, 3));
        muro.Vertices.Add((0, 0, 3));
        m.Panos.Add(muro);

        return m;
    }

    private static double[] Unit(double x, double y, double z)
    {
        var l = Math.Sqrt((x * x) + (y * y) + (z * z));

        return new[] { x / l, y / l, z / l };
    }

    // ==================================================================
    //  main
    // ==================================================================

    private static int Main()
    {
        Console.WriteLine("======================================================");
        Console.WriteLine(" Prueba del exportador de IFC");
        Console.WriteLine("======================================================");

        GuidDeIfc();
        Reales();
        Cadenas();
        Perfiles();
        var texto = Estructura();
        Colocaciones(texto);
        Panos(texto);
        Estabilidad();
        Degenerados();
        Guardar(texto);

        Console.WriteLine();
        Console.WriteLine("======================================================");

        if (Fallos.Count > 0)
        {
            Console.WriteLine($" FALLARON {Fallos.Count}:");

            foreach (var f in Fallos)
            {
                Console.WriteLine("   - " + f);
            }

            Console.WriteLine("======================================================");

            return 1;
        }

        Console.WriteLine(" TODAS LAS COMPROBACIONES PASARON");
        Console.WriteLine("======================================================");

        return 0;
    }

    // ------------------------------------------------------------------
    private static void GuidDeIfc()
    {
        Console.WriteLine("\n[1] GlobalId de IFC");

        var uno = Guid.Parse("3c5e1f34-9a7b-4d2e-8f10-2b6c9d4e5a71");
        var comprimido = IfcGuid.De(uno);

        Check("mide 22 caracteres", comprimido.Length == 22, comprimido);
        Check("solo usa el alfabeto de IFC",
            comprimido.All(c => IfcGuid.Alfabeto.Contains(c)), comprimido);
        Check("el ida y vuelta devuelve el mismo Guid",
            IfcGuid.ADesde(comprimido) == uno,
            $"{uno} -> {comprimido} -> {IfcGuid.ADesde(comprimido)}");

        // El ida y vuelta es la prueba de verdad: un reparto de bits equivocado da una
        // cadena de aspecto perfectamente normal.
        var rnd = new Random(1234);
        var todos = true;

        for (var i = 0; i < 2000; i++)
        {
            var b = new byte[16];
            rnd.NextBytes(b);
            var g = new Guid(b);

            if (IfcGuid.ADesde(IfcGuid.De(g)) != g)
            {
                todos = false;
                break;
            }
        }

        Check("2000 Guid al azar sobreviven el ida y vuelta", todos);

        var a = IfcGuid.Estable("barra|Columna|C1|Story1");
        var b2 = IfcGuid.Estable("barra|Columna|C1|Story1");
        var c = IfcGuid.Estable("barra|Columna|C2|Story1");

        Check("Estable da lo mismo con la misma clave", a == b2, $"{a} vs {b2}");
        Check("y algo distinto con otra clave", a != c, $"{a} vs {c}");
        Check("Estable tambien mide 22", a.Length == 22, a);
    }

    // ------------------------------------------------------------------
    private static void Reales()
    {
        Console.WriteLine("\n[2] Reales de STEP");

        Check("el cero es '0.'", EscritorPaso.Real(0) == "0.", EscritorPaso.Real(0));
        Check("un entero lleva punto", EscritorPaso.Real(3) == "3.", EscritorPaso.Real(3));
        Check("negativo", EscritorPaso.Real(-2) == "-2.", EscritorPaso.Real(-2));
        Check("decimal", EscritorPaso.Real(0.5) == "0.5", EscritorPaso.Real(0.5));
        Check("decimal largo", EscritorPaso.Real(0.0069) == "0.0069", EscritorPaso.Real(0.0069));
        Check("grande", EscritorPaso.Real(1234567.891) == "1234567.891",
            EscritorPaso.Real(1234567.891));

        // Lo importante: NUNCA notacion cientifica y NUNCA coma. Con coma decimal el
        // argumento se partiria en dos y la entidad quedaria corrupta.
        var todos = true;
        var rnd = new Random(7);

        for (var i = 0; i < 5000; i++)
        {
            var v = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-6, 7));
            var t = EscritorPaso.Real(v);

            if (t.Contains(',') || t.Contains('E') || t.Contains('e') || !t.Contains('.'))
            {
                todos = false;
                Console.WriteLine("        sospechoso: " + t);
                break;
            }
        }

        Check("5000 reales al azar: sin coma, sin exponente y con punto", todos);

        var reventó = false;

        try
        {
            EscritorPaso.Real(double.NaN);
        }
        catch (ArgumentException)
        {
            reventó = true;
        }

        Check("un NaN se rechaza en vez de escribirse", reventó);
    }

    // ------------------------------------------------------------------
    private static void Cadenas()
    {
        Console.WriteLine("\n[3] Cadenas de STEP y acentos");

        Check("ASCII pasa tal cual",
            EscritorPaso.Cadena("C 30x60") == "'C 30x60'", EscritorPaso.Cadena("C 30x60"));

        Check("el apostrofo se duplica",
            EscritorPaso.Cadena("VIGA 12'") == "'VIGA 12'''",
            EscritorPaso.Cadena("VIGA 12'"));

        Check("la barra invertida se dobla",
            EscritorPaso.Cadena(@"C:\obra") == @"'C:\\obra'", EscritorPaso.Cadena(@"C:\obra"));

        // La Ñ es U+00D1 y la Ó es U+00D3. Deben ir en UNA sola secuencia, no en dos.
        Check("los acentos van en \\X2\\ y agrupados",
            EscritorPaso.Cadena("ÑÓ") == @"'\X2\00D100D3\X0\'", EscritorPaso.Cadena("ÑÓ"));

        Check("el texto mezclado abre y cierra la secuencia donde toca",
            EscritorPaso.Cadena("CAÑON 1") == @"'CA\X2\00D1\X0\ON 1'",
            EscritorPaso.Cadena("CAÑON 1"));

        Check("vacia", EscritorPaso.Cadena("") == "''", EscritorPaso.Cadena(""));
    }

    // ------------------------------------------------------------------
    private static void Perfiles()
    {
        Console.WriteLine("\n[4] Cada forma con su entidad de perfil");

        var esperado = new (FormaIfc Forma, string Entidad)[]
        {
            (FormaIfc.Rectangulo, "IFCRECTANGLEPROFILEDEF"),
            (FormaIfc.Circulo, "IFCCIRCLEPROFILEDEF"),
            (FormaIfc.Tubo, "IFCCIRCLEHOLLOWPROFILEDEF"),
            (FormaIfc.Cajon, "IFCRECTANGLEHOLLOWPROFILEDEF"),
            (FormaIfc.PerfilI, "IFCISHAPEPROFILEDEF"),
            (FormaIfc.PerfilC, "IFCUSHAPEPROFILEDEF"),
            (FormaIfc.PerfilT, "IFCTSHAPEPROFILEDEF"),
            (FormaIfc.PerfilL, "IFCLSHAPEPROFILEDEF")
        };

        foreach (var (forma, ent) in esperado)
        {
            var m = new ModeloIfc();
            m.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
            m.Barras.Add(new BarraIfc
            {
                Etiqueta = "X",
                Nivel = "N",
                X1 = 0, Y1 = 0, Z1 = 0,
                X2 = 2, Y2 = 0, Z2 = 0,
                E1 = new double[] { 1, 0, 0 },
                E2 = new double[] { 0, 0, 1 },
                E3 = new double[] { 0, -1, 0 },
                Seccion = new SeccionIfc
                {
                    Nombre = "S",
                    Forma = forma,
                    AnchoM = 0.2,
                    PeralteM = 0.4,
                    PatinM = 0.02,
                    AlmaM = 0.01,
                    ParedM = 0.008
                }
            });

            var (t, _) = ExportadorIfc.Generar(m, "x.ifc", DateTimeOffset.UnixEpoch);

            Check($"{forma} -> {ent}", t.Contains("=" + ent + "("),
                "no aparece en el archivo");
        }

        // Un canal NO debe salir como IfcCShapeProfileDef: esa es la C doblada en frio, de
        // espesor unico, y perderia la distincion entre alma y patin.
        var canal = new ModeloIfc();
        canal.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
        canal.Barras.Add(new BarraIfc
        {
            Etiqueta = "CA",
            Nivel = "N",
            X1 = 0, Y1 = 0, Z1 = 0, X2 = 1, Y2 = 0, Z2 = 0,
            E1 = new double[] { 1, 0, 0 },
            E2 = new double[] { 0, 0, 1 },
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "CE 6", Forma = FormaIfc.PerfilC,
                AnchoM = 0.05, PeralteM = 0.15, PatinM = 0.008, AlmaM = 0.005
            }
        });

        var (tc, _) = ExportadorIfc.Generar(canal, "c.ifc", DateTimeOffset.UnixEpoch);
        Check("el canal no usa IfcCShapeProfileDef",
            !tc.Contains("IFCCSHAPEPROFILEDEF("));

        // Un espesor imposible se recorta y se avisa, en vez de salir invalido.
        var gordo = new ModeloIfc();
        gordo.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
        gordo.Barras.Add(new BarraIfc
        {
            Etiqueta = "G",
            Nivel = "N",
            X1 = 0, Y1 = 0, Z1 = 0, X2 = 1, Y2 = 0, Z2 = 0,
            E1 = new double[] { 1, 0, 0 },
            E2 = new double[] { 0, 0, 1 },
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc
            {
                Nombre = "MALA", Forma = FormaIfc.PerfilI,
                AnchoM = 0.20, PeralteM = 0.20,
                PatinM = 0.30,   // mas grueso que la seccion entera
                AlmaM = 0.25
            }
        });

        var (_, rg) = ExportadorIfc.Generar(gordo, "g.ifc", DateTimeOffset.UnixEpoch);

        Check("un patin imposible se recorta y deja aviso",
            rg.Avisos.Any(a => a.Contains("patin")),
            string.Join(" | ", rg.Avisos));
    }

    // ------------------------------------------------------------------
    private static string Estructura()
    {
        Console.WriteLine("\n[5] Estructura del archivo");

        var (texto, r) = ExportadorIfc.Generar(
            ModeloDePrueba(), "modelo-de-prueba.ifc", DateTimeOffset.UnixEpoch);

        Check("abre con ISO-10303-21", texto.StartsWith("ISO-10303-21;"));
        Check("cierra con END-ISO-10303-21", texto.TrimEnd().EndsWith("END-ISO-10303-21;"));
        Check("declara el esquema IFC4", texto.Contains("FILE_SCHEMA(('IFC4'))"));
        Check("tiene HEADER y DATA", texto.Contains("HEADER;") && texto.Contains("DATA;"));

        var m = Leer(texto);

        Check("hay un solo IfcProject", Todas(m, "IFCPROJECT").Count() == 1);
        Check("hay un solo IfcSite", Todas(m, "IFCSITE").Count() == 1);
        Check("hay un solo IfcBuilding", Todas(m, "IFCBUILDING").Count() == 1);
        Check("hay tres niveles", Todas(m, "IFCBUILDINGSTOREY").Count() == 3,
            Todas(m, "IFCBUILDINGSTOREY").Count().ToString());

        Check("la columna es IfcColumn", Todas(m, "IFCCOLUMN").Count() == 1);
        Check("las trabes son IfcBeam", Todas(m, "IFCBEAM").Count() == 2,
            Todas(m, "IFCBEAM").Count().ToString());
        Check("la diagonal es IfcMember", Todas(m, "IFCMEMBER").Count() == 1);
        Check("la losa es IfcSlab", Todas(m, "IFCSLAB").Count() == 1);
        Check("el muro es IfcWall", Todas(m, "IFCWALL").Count() == 1);

        Check("la diagonal lleva .BRACE.",
            Todas(m, "IFCMEMBER").First().Args[8] == ".BRACE.",
            Todas(m, "IFCMEMBER").First().Args[8]);

        Check("el muro lleva .SHEAR.",
            Todas(m, "IFCWALL").First().Args[8] == ".SHEAR.",
            Todas(m, "IFCWALL").First().Args[8]);

        // Los TIPOS son lo que en Revit se convierte en un tipo por seccion.
        Check("hay un IfcColumnType", Todas(m, "IFCCOLUMNTYPE").Count() == 1);
        Check("hay dos IfcBeamType, uno por seccion", Todas(m, "IFCBEAMTYPE").Count() == 2,
            Todas(m, "IFCBEAMTYPE").Count().ToString());
        Check("hay un IfcMemberType", Todas(m, "IFCMEMBERTYPE").Count() == 1);
        Check("hay un IfcSlabType", Todas(m, "IFCSLABTYPE").Count() == 1);
        Check("hay un IfcWallType", Todas(m, "IFCWALLTYPE").Count() == 1);

        Check("cada tipo se liga con IfcRelDefinesByType",
            Todas(m, "IFCRELDEFINESBYTYPE").Count() == 6,
            Todas(m, "IFCRELDEFINESBYTYPE").Count().ToString());

        Check("las piezas se cuelgan de su nivel",
            Todas(m, "IFCRELCONTAINEDINSPATIALSTRUCTURE").Any());

        // La jerarquia espacial se DECLARA, no se deduce. Sin estas tres relaciones el
        // archivo es correcto y esta desconectado: en Revit aparece geometria sin niveles.
        // Faltaban, y lo encontro tools/verificar_ifc.py; se comprueba aqui tambien para
        // que no vuelva a colarse.
        var agrega = Todas(m, "IFCRELAGGREGATES").ToList();

        Check("hay tres IfcRelAggregates", agrega.Count == 3, agrega.Count.ToString());

        var proyecto = Todas(m, "IFCPROJECT").First();
        var sitio = Todas(m, "IFCSITE").First();
        var edificio = Todas(m, "IFCBUILDING").First();

        Check("el proyecto agrega el terreno",
            agrega.Any(a => Refe(a.Args[4]) == proyecto.Id
                            && a.Args[5].Contains("#" + sitio.Id)));

        Check("el terreno agrega el edificio",
            agrega.Any(a => Refe(a.Args[4]) == sitio.Id
                            && a.Args[5].Contains("#" + edificio.Id)));

        var deNiveles = agrega.First(a => Refe(a.Args[4]) == edificio.Id);
        var cuantos = Argumentos(deNiveles.Args[5].Trim('(', ')')).Count;

        Check("el edificio agrega los tres niveles", cuantos == 3, cuantos.ToString());

        Check("el nombre de la seccion va en ObjectType, que es de donde Revit toma el tipo",
            Todas(m, "IFCCOLUMN").First().Args[4] == "'C 30x60'",
            Todas(m, "IFCCOLUMN").First().Args[4]);

        Check("la seccion viaja como perfil con material",
            Todas(m, "IFCMATERIALPROFILESET").Any() && Todas(m, "IFCMATERIALPROFILE").Any());

        Check("hay juego de propiedades con la seccion",
            texto.Contains("'CadLink_Seccion'"));

        // La obra lleva Ñ: tiene que haber sobrevivido escapada.
        Check("el nombre de la obra con Ñ va escapado",
            texto.Contains(@"\X2\00D1"), "no aparece la secuencia de la Ñ");

        Check("el resumen cuenta bien",
            r.Columnas == 1 && r.Trabes == 2 && r.Diagonales == 1
            && r.Losas == 1 && r.Muros == 1 && r.Niveles == 3,
            $"col={r.Columnas} trabes={r.Trabes} diag={r.Diagonales} "
            + $"losas={r.Losas} muros={r.Muros} niveles={r.Niveles}");

        Check("el total es seis piezas", r.Total == 6, r.Total.ToString());

        return texto;
    }

    // ------------------------------------------------------------------
    private static void Colocaciones(string texto)
    {
        Console.WriteLine("\n[6] Donde queda cada pieza (lo que no se ve en el archivo)");

        var m = Leer(texto);

        // ---- La columna ----
        var col = Todas(m, "IFCCOLUMN").First();
        var (origenCol, ejeCol, refCol, largoCol) = Extrusion(m, col);

        Casi("la columna mide 3 m", largoCol, 3);

        // Cuelga de Story1, que esta a 3, asi que su origen local en Z es 3 - 3 = 0.
        CasiVec("arranca en el extremo J, en coordenadas del nivel",
            origenCol, new double[] { 0, 0, 0 });

        // Axis = -E1
        CasiVec("el eje de extrusion es -E1", ejeCol, new double[] { 0, 0, -1 });
        CasiVec("la referencia es E3", refCol, new double[] { 0, 1, 0 });

        // Y AQUI lo que importa: la Y de la colocacion tiene que ser E2, no -E2.
        var yCol = Cruz(ejeCol, refCol);
        CasiVec("la Y de la colocacion es E2, o sea el peralte NO sale espejeado",
            yCol, new double[] { 1, 0, 0 });

        // Y el solido tiene que acabar donde empieza la barra.
        var finCol = Suma(origenCol, Escala(ejeCol, largoCol));
        CasiVec("extruyendo se llega al extremo I", finCol, new double[] { 0, 0, -3 });

        // En globales: el nivel esta a 3, asi que el extremo I queda en Z=0. Correcto.
        Casi("en globales el extremo I esta en Z = 0", finCol[2] + 3, 0);

        // ---- La trabe ----
        var trabes = Todas(m, "IFCBEAM").ToList();
        var t1 = trabes.First(e => e.Args[2] == "'T1'");
        var (origenT, ejeT, refT, largoT) = Extrusion(m, t1);

        Casi("la trabe mide 5 m", largoT, 5);
        CasiVec("arranca en su extremo J", origenT, new double[] { 5, 0, 0 });
        CasiVec("se extruye hacia -X", ejeT, new double[] { -1, 0, 0 });

        var yT = Cruz(ejeT, refT);
        CasiVec("y su peralte queda vertical, como en el modelo", yT, new double[] { 0, 0, 1 });

        var finT = Suma(origenT, Escala(ejeT, largoT));
        CasiVec("llega al extremo I", finT, new double[] { 0, 0, 0 });

        // ---- El angulo, que es donde el espejo se veria ----
        var l1 = trabes.First(e => e.Args[2] == "'L1'");
        var (_, ejeL, refL, _) = Extrusion(m, l1);
        var yL = Cruz(ejeL, refL);

        CasiVec("el angulo tambien conserva su eje 2", yL, new double[] { 0, 0, 1 });

        Check("la terna del angulo es derecha",
            EsDerecha(refL, yL, ejeL),
            $"X=({Junta(refL)}) Y=({Junta(yL)}) Z=({Junta(ejeL)})");

        // ---- La diagonal ----
        var diag = Todas(m, "IFCMEMBER").First();
        var (origenD, ejeD, refD, largoD) = Extrusion(m, diag);

        Casi("la diagonal mide 3*raiz(2)", largoD, Math.Sqrt(18), 1e-9);

        var finD = Suma(origenD, Escala(ejeD, largoD));

        // Arranca en (3,0,3) global -> local (3,0,0) porque Story1 esta a 3.
        CasiVec("arranca en su extremo J", origenD, new double[] { 3, 0, 0 });
        CasiVec("y llega al I", finD, new double[] { 0, 0, -3 });

        Check("la terna de la diagonal es derecha",
            EsDerecha(refD, Cruz(ejeD, refD), ejeD));
    }

    /// <summary>Saca de un elemento el origen, el eje, la referencia y el largo de su extrusion.</summary>
    private static (double[] Origen, double[] Eje, double[] Ref, double Largo) Extrusion(
        Dictionary<int, Ent> m, Ent elemento)
    {
        // elemento.Args[6] = Representation -> IfcProductDefinitionShape
        var forma = m[Refe(elemento.Args[6])];
        var rep = m[Refe(Argumentos(forma.Args[2].Trim('(', ')'))[0])];
        var solido = m[Refe(Argumentos(rep.Args[3].Trim('(', ')'))[0])];

        var pos = m[Refe(solido.Args[1])];
        var origen = Tripleta(m, pos.Args[0]);
        var eje = Tripleta(m, pos.Args[1]);
        var refe = Tripleta(m, pos.Args[2]);
        var largo = double.Parse(solido.Args[3], CultureInfo.InvariantCulture);

        return (origen, eje, refe, largo);
    }

    private static double[] Cruz(double[] a, double[] b) => new[]
    {
        (a[1] * b[2]) - (a[2] * b[1]),
        (a[2] * b[0]) - (a[0] * b[2]),
        (a[0] * b[1]) - (a[1] * b[0])
    };

    private static double[] Suma(double[] a, double[] b) =>
        new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };

    private static double[] Escala(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };

    private static bool EsDerecha(double[] x, double[] y, double[] z)
    {
        var c = Cruz(x, y);

        return Math.Abs(c[0] - z[0]) < 1e-9
               && Math.Abs(c[1] - z[1]) < 1e-9
               && Math.Abs(c[2] - z[2]) < 1e-9;
    }

    // ------------------------------------------------------------------
    private static void Panos(string texto)
    {
        Console.WriteLine("\n[7] Muros y losas");

        var m = Leer(texto);

        var losa = Todas(m, "IFCSLAB").First();
        var (origenL, ejeL, _, espesorL) = Extrusion(m, losa);

        Casi("la losa tiene 12 cm de espesor", espesorL, 0.12);
        CasiVec("y su normal apunta hacia arriba", ejeL, new double[] { 0, 0, 1 });

        // La losa CUELGA del contorno: arranca 12 cm por debajo. El contorno esta en Z=3
        // global, el nivel tambien en 3, asi que en local el contorno esta en 0 y el
        // arranque en -0.12.
        Casi("cuelga por debajo de su contorno", origenL[2], -0.12);

        var muro = Todas(m, "IFCWALL").First();
        var (origenM, ejeM, _, espesorM) = Extrusion(m, muro);

        Casi("el muro tiene 20 cm", espesorM, 0.20);
        Check("la normal del muro es horizontal", Math.Abs(ejeM[2]) < 1e-9, Junta(ejeM));

        // El muro se reparte a los dos lados de su plano medio: el plano esta en Y=0, asi
        // que el arranque queda a 10 cm de un lado.
        Casi("se reparte a los dos lados del plano medio",
            Math.Abs(origenM[1]), 0.10);

        // El contorno tiene que estar CERRADO: la primera y la ultima referencia iguales.
        var lineas = Todas(m, "IFCPOLYLINE").ToList();
        Check("hay dos contornos, uno por pano", lineas.Count == 2, lineas.Count.ToString());

        var cerrados = lineas.All(l =>
        {
            var pts = Argumentos(l.Args[0].Trim('(', ')'));

            return pts.Count >= 4 && pts[0] == pts[^1];
        });

        Check("los contornos se cierran repitiendo su primer punto", cerrados);

        // El vertice repetido que traia la losa no debe haber dejado un lado de largo cero.
        var losaLinea = lineas.First(l =>
            Argumentos(l.Args[0].Trim('(', ')')).Count == 5);

        Check("el vertice repetido de la losa se limpio (5 puntos = 4 mas el cierre)",
            Argumentos(losaLinea.Args[0].Trim('(', ')')).Count == 5);
    }

    // ------------------------------------------------------------------
    private static void Estabilidad()
    {
        Console.WriteLine("\n[8] Dos corridas del mismo modelo dan lo mismo");

        var (a, _) = ExportadorIfc.Generar(ModeloDePrueba(), "x.ifc", DateTimeOffset.UnixEpoch);
        var (b, _) = ExportadorIfc.Generar(ModeloDePrueba(), "x.ifc", DateTimeOffset.UnixEpoch);

        Check("el archivo es byte a byte el mismo", a == b);

        // Y los GlobalId no cambian entre corridas, que es lo que permite reimportar sin
        // duplicar todo.
        var m1 = Leer(a);
        var m2 = Leer(b);

        var g1 = Todas(m1, "IFCCOLUMN").First().Args[0];
        var g2 = Todas(m2, "IFCCOLUMN").First().Args[0];

        Check("el GlobalId de la columna se repite", g1 == g2, $"{g1} vs {g2}");
    }

    // ------------------------------------------------------------------
    private static void Degenerados()
    {
        Console.WriteLine("\n[9] Casos degenerados: avisan y no revientan");

        // Barra de largo cero
        var m1 = new ModeloIfc();
        m1.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
        m1.Barras.Add(new BarraIfc
        {
            Etiqueta = "NADA", Nivel = "N",
            X1 = 1, Y1 = 1, Z1 = 1, X2 = 1, Y2 = 1, Z2 = 1,
            Seccion = new SeccionIfc { Nombre = "S", AnchoM = 0.2, PeralteM = 0.2 }
        });

        var (_, r1) = ExportadorIfc.Generar(m1, "a.ifc", DateTimeOffset.UnixEpoch);

        Check("una barra de largo cero no se exporta", r1.Total == 0, r1.Total.ToString());
        Check("y deja aviso", r1.Avisos.Any(a => a.Contains("mismo punto")),
            string.Join(" | ", r1.Avisos));

        // Modelo sin niveles
        var m2 = new ModeloIfc();
        m2.Barras.Add(new BarraIfc
        {
            Etiqueta = "B", Nivel = "",
            X1 = 0, Y1 = 0, Z1 = 0, X2 = 0, Y2 = 0, Z2 = 3,
            E1 = new double[] { 0, 0, 1 },
            E2 = new double[] { 1, 0, 0 },
            E3 = new double[] { 0, 1, 0 },
            Seccion = new SeccionIfc { Nombre = "S", AnchoM = 0.2, PeralteM = 0.2 }
        });

        var (t2, r2) = ExportadorIfc.Generar(m2, "b.ifc", DateTimeOffset.UnixEpoch);

        Check("sin niveles se inventa uno", r2.Niveles == 1, r2.Niveles.ToString());
        Check("y la pieza se exporta igual", r2.Total == 1, r2.Total.ToString());
        Check("el archivo sigue siendo valido", t2.Contains("IFCBUILDINGSTOREY"));

        // Ejes locales basura: deben rehacerse
        var m3 = new ModeloIfc();
        m3.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
        m3.Barras.Add(new BarraIfc
        {
            Etiqueta = "TORCIDA", Nivel = "N",
            X1 = 0, Y1 = 0, Z1 = 0, X2 = 4, Y2 = 0, Z2 = 0,
            E1 = new double[] { 0, 0, 1 },     // no va a lo largo de la barra
            E2 = new double[] { 1, 0, 0 },
            E3 = new double[] { 0, 1, 0 },
            Seccion = new SeccionIfc { Nombre = "S", AnchoM = 0.2, PeralteM = 0.4 }
        });

        var (t3, r3) = ExportadorIfc.Generar(m3, "c.ifc", DateTimeOffset.UnixEpoch);

        Check("unos ejes que no son de la barra se rehacen",
            r3.Avisos.Any(a => a.Contains("terna")), string.Join(" | ", r3.Avisos));
        Check("y la pieza se exporta", r3.Total == 1);

        var m3l = Leer(t3);
        var (_, eje3, ref3, largo3) = Extrusion(m3l, Todas(m3l, "IFCBEAM").First());

        Casi("con el largo correcto", largo3, 4);
        Check("y con terna derecha", EsDerecha(ref3, Cruz(eje3, ref3), eje3));

        // Pano con todos los vertices en linea
        var m4 = new ModeloIfc();
        m4.Niveles.Add(new NivelIfc { Nombre = "N", ElevacionM = 0 });
        var plano = new PanoIfc { Etiqueta = "PLANO", Clase = ClaseIfc.Losa, Nivel = "N", EspesorM = 0.1 };
        plano.Vertices.Add((0, 0, 0));
        plano.Vertices.Add((1, 0, 0));
        plano.Vertices.Add((2, 0, 0));
        m4.Panos.Add(plano);

        var (_, r4) = ExportadorIfc.Generar(m4, "d.ifc", DateTimeOffset.UnixEpoch);

        Check("un pano de area cero no se exporta", r4.Total == 0);
        Check("y deja aviso", r4.Avisos.Any(a => a.Contains("area cero")),
            string.Join(" | ", r4.Avisos));

        // Nivel que no existe: se cuelga del mas cercano
        var m5 = new ModeloIfc();
        m5.Niveles.Add(new NivelIfc { Nombre = "Base", ElevacionM = 0 });
        m5.Niveles.Add(new NivelIfc { Nombre = "Alto", ElevacionM = 10 });
        m5.Barras.Add(new BarraIfc
        {
            Etiqueta = "H", Nivel = "NO EXISTE",
            X1 = 0, Y1 = 0, Z1 = 9.5, X2 = 2, Y2 = 0, Z2 = 9.5,
            E1 = new double[] { 1, 0, 0 },
            E2 = new double[] { 0, 0, 1 },
            E3 = new double[] { 0, -1, 0 },
            Seccion = new SeccionIfc { Nombre = "S", AnchoM = 0.2, PeralteM = 0.3 }
        });

        var (_, r5) = ExportadorIfc.Generar(m5, "e.ifc", DateTimeOffset.UnixEpoch);

        Check("un nivel desconocido se resuelve por cota",
            r5.Avisos.Any(a => a.Contains("Alto")), string.Join(" | ", r5.Avisos));
        Check("y la pieza no se pierde", r5.Total == 1);
    }

    // ------------------------------------------------------------------
    private static void Guardar(string texto)
    {
        Console.WriteLine("\n[10] Se deja el archivo para verificar_ifc.py");

        var carpeta = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "salida");
        carpeta = Path.GetFullPath(carpeta);
        Directory.CreateDirectory(carpeta);

        var ruta = Path.Combine(carpeta, "modelo-de-prueba.ifc");
        File.WriteAllText(ruta, texto, new UTF8Encoding(false));

        Check("el archivo se escribio", File.Exists(ruta), ruta);
        Console.WriteLine("        " + ruta);

        // Tambien se comprueba que AArchivo hace lo mismo que Generar.
        var ruta2 = Path.Combine(carpeta, "por-aarchivo.ifc");
        var resumen = ExportadorIfc.AArchivo(ModeloDePrueba(), ruta2, DateTimeOffset.UnixEpoch);

        Check("AArchivo tambien escribe y cuenta igual", resumen.Total == 6,
            resumen.Total.ToString());

        // Y que no mete BOM: un BOM antes de ISO-10303-21 hace que algunos lectores no
        // reconozcan el archivo.
        var crudo = File.ReadAllBytes(ruta2);

        Check("sin BOM al principio",
            !(crudo.Length > 2 && crudo[0] == 0xEF && crudo[1] == 0xBB && crudo[2] == 0xBF));
    }
}
