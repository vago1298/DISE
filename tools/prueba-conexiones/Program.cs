using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CadLink.Revit.Nucleo;

namespace CadLink.Pruebas;

internal static class Program
{
    private static readonly List<string> Fallos = new();
    private static int _bien;

    private static void Check(string que, bool ok, string detalle = "")
    {
        if (ok)
        {
            _bien++;
            Console.WriteLine("  OK    " + que);
        }
        else
        {
            Fallos.Add(que + (detalle.Length > 0 ? " — " + detalle : string.Empty));
            Console.WriteLine("  FALLA " + que + (detalle.Length > 0 ? " — " + detalle : string.Empty));
        }
    }

    private static bool Casi(double a, double b, double tol = 1e-6) => Math.Abs(a - b) <= tol * Math.Max(1, Math.Abs(b));

    // ==================================================================
    //  Un libro de Excel de prueba, armado a mano: el mismo ZIP que guarda Excel
    // ==================================================================

    private sealed class Hoja
    {
        public string Nombre = string.Empty;
        public readonly List<(int Fila, int Col, object Valor, string Modo)> Celdas = new();

        public void Poner(int fila, int col, object valor, string modo = "") => Celdas.Add((fila, col, valor, modo));
    }

    private static string Col(int c)
    {
        var s = string.Empty;

        while (c > 0)
        {
            var r = (c - 1) % 26;
            s = (char)('A' + r) + s;
            c = (c - 1) / 26;
        }

        return s;
    }

    private static string X(string s) => System.Security.SecurityElement.Escape(s) ?? string.Empty;

    private static MemoryStream Libro(IEnumerable<Hoja> hojas, bool recalcular = false)
    {
        var ms = new MemoryStream();
        var comunes = new List<string>();
        var lista = hojas.ToList();

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Escribir(string ruta, string texto)
            {
                using var w = new StreamWriter(zip.CreateEntry(ruta).Open(), new UTF8Encoding(false));
                w.Write(texto);
            }

            var wb = new StringBuilder("<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var rels = new StringBuilder("<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

            for (var i = 0; i < lista.Count; i++)
            {
                wb.Append($"<sheet name=\"{X(lista[i].Nombre)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");

                var sh = new StringBuilder("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

                foreach (var fila in lista[i].Celdas.GroupBy(c => c.Fila).OrderBy(g => g.Key))
                {
                    sh.Append($"<row r=\"{fila.Key}\">");

                    foreach (var c in fila.OrderBy(c => c.Col))
                    {
                        var refe = Col(c.Col) + c.Fila;

                        if (c.Valor is double d)
                        {
                            sh.Append(c.Modo == "f"
                                ? $"<c r=\"{refe}\"><f>1+1</f><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>"
                                : $"<c r=\"{refe}\"><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        }
                        else
                        {
                            var s = c.Valor.ToString() ?? string.Empty;

                            switch (c.Modo)
                            {
                                case "i":
                                    sh.Append($"<c r=\"{refe}\" t=\"inlineStr\"><is><t>{X(s)}</t></is></c>");
                                    break;
                                case "f":
                                    sh.Append($"<c r=\"{refe}\" t=\"str\"><f>A1</f><v>{X(s)}</v></c>");
                                    break;
                                default:
                                    comunes.Add(s);
                                    sh.Append($"<c r=\"{refe}\" t=\"s\"><v>{comunes.Count - 1}</v></c>");
                                    break;
                            }
                        }
                    }

                    sh.Append("</row>");
                }

                sh.Append("</sheetData></worksheet>");
                Escribir($"xl/worksheets/sheet{i + 1}.xml", sh.ToString());
            }

            wb.Append("</sheets>");
            wb.Append(recalcular ? "<calcPr calcId=\"1\" fullCalcOnLoad=\"1\"/>" : "<calcPr calcId=\"1\"/>");
            wb.Append("</workbook>");
            rels.Append("</Relationships>");
            Escribir("xl/workbook.xml", wb.ToString());
            Escribir("xl/_rels/workbook.xml.rels", rels.ToString());

            var ss = new StringBuilder("<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            foreach (var s in comunes)
            {
                // Uno partido en dos <r>, como los que tienen formato.
                ss.Append(s.Length > 6
                    ? $"<si><r><t>{X(s[..3])}</t></r><r><t xml:space=\"preserve\">{X(s[3..])}</t></r></si>"
                    : $"<si><t>{X(s)}</t></si>");
            }

            ss.Append("</sst>");
            Escribir("xl/sharedStrings.xml", ss.ToString());
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>Una hoja del tipo dado con sus datos de fabrica, mas los que se pidan.</summary>
    private static Hoja HojaDe(string codigo, string nombre, Dictionary<string, object>? valores = null, int saltoCada = 7)
    {
        var tipo = EtiquetasExcel.DeCodigo(codigo)!;
        var h = new Hoja { Nombre = nombre };
        h.Poner(1, 2, tipo.Titulo);
        var fila = 4;
        var i = 0;

        foreach (var d in tipo.Datos)
        {
            // Renglones de mas, para que las filas no coincidan con las de la hoja original.
            if (++i % saltoCada == 0)
            {
                h.Poner(fila++, 2, "Revisión: algo que se revisa");
                h.Poner(fila - 1, 4, "CUMPLE", "f");
            }

            h.Poner(fila, 2, d.Etiqueta, i % 3 == 0 ? "i" : string.Empty);
            object? v = null;

            if (valores is not null && valores.TryGetValue(d.Nombre, out var dado))
            {
                v = dado;
            }
            else if (d.EsCaptura && d.Defecto is not null)
            {
                v = double.TryParse(d.Defecto, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : d.Defecto;
            }

            if (v is not null)
            {
                h.Poner(fila, 3, v is int k ? (double)k : v, d.EsCaptura ? (v is string ? "i" : string.Empty) : "f");
            }

            fila++;
        }

        return h;
    }

    private static HojaLeida Leer(Hoja h)
    {
        using var ms = Libro(new[] { h });
        var libro = LibroExcel.Leer(ms);
        return LectorDelLibro.Leer(libro).Single();
    }

    private static bool Finitos(ModeloConexion m)
    {
        static bool F(V3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        return m.Placas.All(p => p.Vertices().All(F) && double.IsFinite(p.Espesor))
               && m.Cordones.All(c => c.Vertices().All(F))
               && m.Tornillos.All(t => F(t.Desde) && F(t.Hasta))
               && m.Miembros.All(x => F(x.P0) && F(x.P1));
    }

    private static bool VistasBien(ModeloConexion m)
    {
        foreach (var v in VistasConexion.Proyectar(m))
        {
            if (!(double.IsFinite(v.MinX) && double.IsFinite(v.MaxX) && v.Ancho > 0.01 && v.Alto > 0.01 && v.Ancho < 20 && v.Alto < 20))
            {
                return false;
            }

            if (v.Poligonos.Any(p => p.Puntos.Any(q => !double.IsFinite(q.X) || !double.IsFinite(q.Y))))
            {
                return false;
            }
        }

        return m.Vistas.Count > 0;
    }

    // ==================================================================
    private static void ElLector()
    {
        Console.WriteLine("\n[1] El lector del libro");

        var uv = HojaDe("UV", "UNION VIGA-COLUMNA", new Dictionary<string, object>
        {
            ["ID"] = "UV-7 eje C",
            ["Ryv"] = 1.1,
            ["Ryc"] = 1.25,
            ["tps"] = 3.18
        });
        var h = Leer(uv);
        Check("reconoce la hoja por su nombre", h.Codigo == "UV");
        Check("lee el texto (compartido partido en <r>)", h.Id == "UV-7 eje C", h.Id);
        Check("lee los numeros aunque las filas se movieron", Casi(h.Num("tps"), 3.18), h.Num("tps").ToString(CultureInfo.InvariantCulture));
        Check("dos datos con la misma etiqueta salen en su orden (Ry de viga y de columna)",
            Casi(h.Num("Ryv"), 1.1) && Casi(h.Num("Ryc"), 1.25), $"{h.Num("Ryv")} / {h.Num("Ryc")}");
        Check("lee el resultado guardado de una formula (Ry es calculado)", h.Valores["Ryv"].EsFormula);
        Check("no le falta ningun dato", h.Faltan.Count == 0, string.Join(", ", h.Faltan.Take(5)));
        Check("con un dato cambiado ya no es de ejemplo", !h.DeEjemplo);

        var ejemplo = Leer(HojaDe("UV", "UNION VIGA-COLUMNA"));
        Check("con los datos de fabrica es la hoja de ejemplo", ejemplo.DeEjemplo);

        var copia = HojaDe("GP", "PLACA GUSSET 2");
        var copia2 = HojaDe("CE", "CONTRAVENTEO EXCENTRICO (3)");
        var renombrada = HojaDe("MW", "MI CRUCE");
        var no = new Hoja { Nombre = "ETABS" };
        no.Poner(1, 2, "ELEMENTOS MECÁNICOS");

        using var ms = Libro(new[] { copia, copia2, renombrada, no }, recalcular: true);
        var libro = LibroExcel.Leer(ms);
        var leidas = LectorDelLibro.Leer(libro);
        Check("las copias de hojas se reconocen («PLACA GUSSET 2», «… (3)»)",
            leidas.Count == 3 && leidas[0].Codigo == "GP" && leidas[1].Codigo == "CE", string.Join(",", leidas.Select(l => l.Codigo)));
        Check("una hoja renombrada se reconoce por su titulo", leidas.Count == 3 && leidas[2].Codigo == "MW");
        Check("las hojas que no son del libro se ignoran", leidas.All(l => l.Hoja != "ETABS"));
        Check("se avisa que el libro pide recalcular", libro.RecalcularAlAbrir);

        var conNoCumple = HojaDe("SC", "CONEXION CORTANTE");
        conNoCumple.Poner(900, 2, "Revisión: la placa aguanta");
        conNoCumple.Poner(900, 4, "NO CUMPLE", "f");
        conNoCumple.Poner(901, 2, "Revisión: alma de la columna");
        conNoCumple.Poner(901, 4, "REQUIERE ATIESADOR", "f");
        var sc = Leer(conNoCumple);
        Check("cuenta las revisiones que no cumplen", sc.NoCumplen == 1 && sc.RequierenAtiesador == 1, $"{sc.NoCumplen}/{sc.RequierenAtiesador}");

        Check("pulgadas: 3.18 cm = 1 1/4\"", TextoMedida.Pulgadas(3.18) == "1 1/4\"", TextoMedida.Pulgadas(3.18));
        Check("pulgadas: 0.95 cm = 3/8\"", TextoMedida.Pulgadas(0.95) == "3/8\"", TextoMedida.Pulgadas(0.95));
        Check("pulgadas: 2.54 cm = 1\"", TextoMedida.Pulgadas(2.54) == "1\"", TextoMedida.Pulgadas(2.54));
        Check("sin copia: «PLACA GUSSET 1» → placa gusset", EtiquetasExcel.SinCopia("PLACA GUSSET 1") == "placa gusset");
    }

    // ==================================================================
    private static ModeloConexion? Armar(string codigo, Dictionary<string, object> valores, string nombre = "")
    {
        var h = Leer(HojaDe(codigo, nombre.Length > 0 ? nombre : EtiquetasExcel.DeCodigo(codigo)!.Hoja, valores));
        var m = ConexionesDelLibro.Construir(h, out var porQue);

        if (m is null)
        {
            Console.WriteLine("        (no se armo: " + porQue + ")");
        }

        return m;
    }

    private static void LasPiezas()
    {
        Console.WriteLine("\n[2] Las piezas de cada conexion");

        // Calculados que da el libro y que la hoja de prueba no trae.
        var perfilV = new Dictionary<string, object>
        {
            ["db"] = 47.3, ["bfb"] = 28.2, ["tfb"] = 2.21, ["twb"] = 1.36, ["hb"] = 40.7,
            ["dc"] = 57.2, ["bfc"] = 33.4, ["tfc"] = 4.06, ["twc"] = 2.29, ["hc"] = 46.0, ["kc"] = 5.6
        };

        // ---- Union viga-columna, soldada.
        var uv = new Dictionary<string, object>(perfilV)
        {
            ["Tcp"] = "SOLDADA", ["Atr"] = "NO", ["tps"] = 3.18, ["bps"] = 31.0, ["Lps"] = 80.0,
            ["tpi"] = 3.18, ["bpi"] = 31.0, ["Lpi"] = 80.0, ["Lw"] = 60.0, ["wfp"] = 1.27,
            ["Tsp"] = "SOLDADA", ["tsp"] = 1.27, ["hsp"] = 29.0, ["bsp"] = 17.0, ["Lhw"] = 15.0, ["wsw"] = 0.95,
            ["bpc"] = 14.5, ["tpc"] = 3.18, ["cpc"] = 2.5, ["wpc"] = 0.95, ["tdp"] = 2.22
        };
        var m = Armar("UV", uv);
        Check("UV: se arma", m is not null);

        if (m is not null)
        {
            var sup = m.Placas.First(p => p.Rotulo.StartsWith("Placa superior", StringComparison.Ordinal));
            Check("UV: placa superior de 1 1/4\" y 80 cm", Casi(sup.Espesor, 0.0318) && Casi(sup.Contorno.Max(c => c.U), 0.80), sup.Rotulo);
            Check("UV: la placa superior va sobre el patín (Y = d/2)", Casi(sup.Origen.Y, 0.473 / 2));
            Check("UV: 8 placas de continuidad (2 por lado, arriba y abajo)", m.Placas.Count(p => p.Que == "Placa de continuidad") == 4,
                m.Placas.Count(p => p.Que == "Placa de continuidad").ToString(CultureInfo.InvariantCulture));
            Check("UV: placa adosada", m.Placas.Any(p => p.Que == "Placa adosada"));
            Check("UV: soldada no lleva tornillos", m.Tornillos.Count == 0);
            Check("UV: la placa angosta lleva sus filetes en los bordes de la placa",
                m.Cordones.Count(c => c.Rotulo.Contains("placa-patín", StringComparison.Ordinal)) == 4);
            Check("UV: todo finito", Finitos(m));
            Check("UV: las vistas se proyectan", VistasBien(m));
        }

        // ---- Atornillada.
        var uvt = new Dictionary<string, object>(uv) { ["Tcp"] = "ATORNILLADA", ["Atr"] = "SI", ["nf"] = 4.0, ["s"] = 7.5, ["g"] = 11.5, ["S1"] = 6.5, ["dbt"] = 2.22, ["Tsp"] = "ATORNILLADA", ["nv"] = 4.0, ["sv"] = 7.5, ["ev"] = 6.0, ["dv"] = 2.22 };
        m = Armar("UV", uvt);
        Check("UV atornillada: 2 placas × 4 filas × 2 + 4 del alma = 20 tornillos", m is not null && m.Tornillos.Count == 20,
            m?.Tornillos.Count.ToString(CultureInfo.InvariantCulture) ?? "-");

        // ---- WUF-W, RBS y placa de extremo.
        m = Armar("WF", new Dictionary<string, object>(perfilV));
        Check("WF: patines a penetración y placa de cortante", m is not null && m.Cordones.Count(c => c.Penetracion) >= 2 && m.Placas.Any(p => p.Que == "Placa de cortante"));
        Check("WF: vistas", m is not null && VistasBien(m));
        m = Armar("RB", new Dictionary<string, object>(perfilV));
        Check("RB: el recorte se dibuja en los dos bordes", m is not null && m.Trazos.Count(t => t.Que == "Recorte RBS") == 2);
        Check("RB: vistas", m is not null && VistasBien(m));
        m = Armar("EP", new Dictionary<string, object>(perfilV) { ["Cfg"] = "4ES", ["hst"] = 9.6, ["Lst"] = 16.6 });
        Check("EP 4ES: 8 tornillos y 2 atiesadores", m is not null && m.Tornillos.Count == 8 && m.Placas.Count(p => p.Que == "Atiesador de la placa") == 2,
            $"{m?.Tornillos.Count}/{m?.Placas.Count(p => p.Que == "Atiesador de la placa")}");
        m = Armar("EP", new Dictionary<string, object>(perfilV) { ["Cfg"] = "8ES" });
        Check("EP 8ES: 16 tornillos", m is not null && m.Tornillos.Count == 16, m?.Tornillos.Count.ToString(CultureInfo.InvariantCulture) ?? "-");

        // ---- Cortante, soldada y atornillada.
        var sc = new Dictionary<string, object>
        {
            ["ds"] = 30.5, ["bfs"] = 10.1, ["tfs"] = 0.57, ["tws"] = 0.56, ["dp"] = 47.3, ["bfp"] = 28.2, ["twp"] = 1.36,
            ["dce"] = 3.4, ["Union"] = "SOLDADA", ["tpl"] = 1.27, ["hpl"] = 14.0, ["bpl"] = 10.0, ["g0"] = 1.27, ["ytop"] = 6.0, ["wsc"] = 0.95, ["ws2"] = 0.64, ["Lhs"] = 8.0
        };
        m = Armar("SC", sc);
        Check("SC: placa, filetes a la que soporta y C a la soportada", m is not null && m.Placas.Count == 1 && m.Cordones.Count == 5, $"{m?.Placas.Count}/{m?.Cordones.Count}");
        Check("SC: el centro del perfil se encuentra con el despatinado", m is not null && Casi(m.CaraArribaM, (0.305 / 2) - 0.034));
        Check("SC: vistas", m is not null && VistasBien(m));
        m = Armar("SC", new Dictionary<string, object>(sc) { ["Union"] = "ATORNILLADA", ["nv"] = 3.0, ["sv"] = 7.5, ["Lev"] = 3.75, ["aa"] = 7.5, ["dbt"] = 1.905 });
        Check("SC atornillada: 3 tornillos", m is not null && m.Tornillos.Count == 3);

        // ---- Viga-viga soldada: cruce, empalme y continuidad.
        var mw = new Dictionary<string, object>
        {
            ["d"] = 47.3, ["bf"] = 28.2, ["tf"] = 2.21, ["tw"] = 1.36, ["dt"] = 47.3, ["bft"] = 28.2, ["tft"] = 2.21, ["twt"] = 1.36,
            ["Tipo"] = "CRUCE", ["ccop"] = 13.42, ["hwx"] = 38.1, ["UwX"] = "FILETE", ["wwx"] = 0.64, ["Pen"] = "NO", ["PenW"] = "NO", ["Lp"] = 51.3
        };
        m = Armar("MW", mw);
        Check("MW cruce: 2 penetraciones y 2 filetes del alma", m is not null && m.Cordones.Count(c => c.Penetracion) == 2 && m.Cordones.Count(c => !c.Penetracion) == 2);
        Check("MW cruce: la lengüeta del alma solo se dibuja", m is not null && m.Placas.All(p => p.SoloDibujo) && m.PiezasDeRevit == 4);
        Check("MW cruce: vistas", m is not null && VistasBien(m));
        m = Armar("MW", new Dictionary<string, object>(mw) { ["Tipo"] = "EMPALME", ["Pen"] = "NO", ["PenW"] = "NO" });
        Check("MW empalme: cubreplacas y placas de alma", m is not null && m.Placas.Count(p => p.Que == "Cubreplaca") == 2 && m.Placas.Any(p => p.Que == "Placa de alma"));
        Check("MW empalme: vistas", m is not null && VistasBien(m));
        m = Armar("MW", new Dictionary<string, object>(mw) { ["Tipo"] = "CONTINUIDAD" });
        Check("MW continuidad: placa de continuidad y placa de cortante", m is not null && m.Placas.Any(p => p.Que == "Placa de continuidad") && m.Placas.Any(p => p.Que == "Placa de cortante"));

        // ---- Placa gusset.
        var gp = new Dictionary<string, object>
        {
            ["tp"] = 1.91, ["th"] = 41.6, ["eb"] = 23.65, ["dvb"] = 47.3, ["ec"] = 0.0, ["Lgc"] = 50.0, ["Lgb"] = 85.0,
            ["Lw"] = 40.0, ["w"] = 0.95, ["bws"] = 20.3, ["tm"] = 1.18, ["wb"] = 0.95, ["wc"] = 0.95, ["dlin"] = 5.5, ["nw"] = 4.0,
            ["dcc"] = 57.2, ["tfb"] = 2.21, ["twb"] = 1.36, ["tfc"] = 4.06, ["twc"] = 2.29
        };
        m = Armar("GP", gp, "PLACA GUSSET 1");
        Check("GP: se arma", m is not null);

        if (m is not null)
        {
            var c = m.Placas.Single().Contorno;
            Check("GP: el contorno toca la esquina viga-columna", c.Any(q => Casi(q.U, 0) && Casi(q.V, 0.2365)));
            Check("GP: y llega a Lgc sobre la columna", c.Any(q => Casi(q.U, 0) && Casi(q.V, 0.2365 + 0.50)));
            Check("GP: 4 cordones a la diagonal mas viga y columna por las dos caras (8)", m.Cordones.Count == 8, m.Cordones.Count.ToString(CultureInfo.InvariantCulture));
            var diag = m.Miembros.Single(x => x.Que == "Diagonal");
            var dir = (diag.P1 - diag.P0).Unitario();
            Check("GP: la diagonal sale a 41.6° de la vertical", Casi(Math.Atan2(dir.X, dir.Y) * 180 / Math.PI, 41.6, 1e-3));
            Check("GP: el tubo libra la viga", diag.P0.Y - (0.203 / 2 * Math.Sin(41.6 * Math.PI / 180)) >= 0.2365 + 0.025 - 1e-6);
            Check("GP: vistas", VistasBien(m));
        }

        // ---- Contraventeo excentrico.
        var ce = new Dictionary<string, object>
        {
            ["Lb"] = 633.0, ["e"] = 65.0, ["alfa"] = 48.4, ["d"] = 47.3, ["bf"] = 28.2, ["tf"] = 2.21, ["tw"] = 1.36, ["kdv"] = 3.3,
            ["bse"] = 13.0, ["tse"] = 1.11, ["bsi"] = 13.0, ["tsi"] = 1.43, ["ws"] = 0.64, ["cls"] = 2.5, ["Lado"] = "UN LADO",
            ["Req"] = "SI", ["nint"] = 1.0, ["Tip"] = "CORTO", ["tg"] = 2.22, ["Lgi"] = 55.0, ["xc"] = 27.5, ["xin"] = 0.0,
            ["Lwg"] = 35.0, ["wg"] = 0.95, ["bcn"] = 20.3, ["tb"] = 1.18, ["nwc"] = 4.0, ["Twi"] = "PENETRACION"
        };
        m = Armar("CE", ce, "CONTRAVENTEO EXCENTRICO 1");
        Check("CE: 4 atiesadores de extremo + 1 intermedio de un lado", m is not null && m.Placas.Count(p => p.Que == "Atiesador") == 5,
            m?.Placas.Count(p => p.Que == "Atiesador").ToString(CultureInfo.InvariantCulture) ?? "-");
        Check("CE: dos placas de diagonal", m is not null && m.Placas.Count(p => p.Que == "Placa de la diagonal") == 2);
        Check("CE: los atiesadores de extremo van en ±e/2", m is not null && m.Placas.Where(p => p.Que == "Atiesador").Select(p => Math.Round(p.Origen.X + (p.Espesor / 2), 4)).Distinct().Count() == 3);
        Check("CE: vistas", m is not null && VistasBien(m));

        // ---- X en un entrepiso.
        var cx = new Dictionary<string, object>
        {
            ["Conf"] = "1 PISO", ["Cruce"] = "PLACA", ["alfa"] = 38.2, ["Din"] = 12.7, ["tw_"] = 0.74, ["tp"] = 0.95,
            ["Lw"] = 55.0, ["w"] = 0.5, ["nr"] = 2.0, ["br"] = 7.5, ["trf"] = 1.59, ["dlin"] = 6.0, ["nw"] = 4.0
        };
        m = Armar("CX", cx);
        Check("CX placa: una placa del cruce, 4 diagonales y 8 refuerzos", m is not null && m.Placas.Count(p => p.Que == "Placa del cruce") == 1
            && m.Miembros.Count == 4 && m.Placas.Count(p => p.Que == "Placa de refuerzo") == 8);
        Check("CX placa: 16 cordones", m is not null && m.Cordones.Count == 16, m?.Cordones.Count.ToString(CultureInfo.InvariantCulture) ?? "-");
        Check("CX: vistas", m is not null && VistasBien(m));
        m = Armar("CX", new Dictionary<string, object>(cx) { ["Cruce"] = "EMPALMADA", ["bsp"] = 8.0, ["tsp"] = 1.27, ["Ls"] = 40.0, ["wsp"] = 0.95, ["g0"] = 1.27 });
        Check("CX empalmada: dos cubreplacas", m is not null && m.Placas.Count(p => p.Que == "Cubreplaca") == 2);
        m = Armar("CX", new Dictionary<string, object>(cx) { ["Conf"] = "2 NIVELES" });
        Check("CX en dos niveles: todavia no, y se dice por que", m is null);

        // ---- Placa base.
        var bp = new Dictionary<string, object>
        {
            ["N"] = 75.0, ["Bp"] = 45.0, ["tp"] = 3.49, ["nx"] = 2.0, ["nt"] = 2.0, ["f"] = 33.0, ["s"] = 37.0, ["do"] = 1.91,
            ["Hol"] = 3.33, ["hef"] = 40.0, ["Anc"] = "GANCHO", ["eh"] = 7.6, ["Agr"] = "SI", ["gg"] = 2.5, ["wc"] = 0.95, ["dc"] = 57.2, ["bfc"] = 33.4
        };
        m = Armar("BP", bp);
        Check("BP: 4 anclas en las esquinas", m?.PlacaBase is { } pb && pb.Anclas.Count == 4
            && pb.Anclas.All(a => Casi(Math.Abs(a.X), 0.33) && Casi(Math.Abs(a.Y), 0.185)));
        Check("BP: las patas del gancho apuntan hacia dentro", m?.PlacaBase is { } pb2 && pb2.Anclas.All(a => Math.Sign(a.PataX) == -Math.Sign(a.X) && a.PataM > 0));
        m = Armar("BP", new Dictionary<string, object>(bp) { ["nx"] = 4.0, ["nt"] = 4.0, ["s"] = 12.3, ["sx"] = 22.0 });
        Check("BP 4×4: 12 anclas en el perimetro", m?.PlacaBase is { } pb3 && pb3.Anclas.Count == 12, m?.PlacaBase?.Anclas.Count.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    // ==================================================================
    private static void UnLibroDeVerdad(string ruta, string? salida)
    {
        Console.WriteLine($"\n[3] El libro {Path.GetFileName(ruta)}");
        var libro = LibroExcel.Abrir(ruta);
        var lista = ConexionesDelLibro.Leer(libro);
        Check("se leyo al menos una conexion", lista.Count > 0);

        foreach (var c in lista)
        {
            var m = c.Modelo;
            Console.WriteLine($"        {c.Hoja.Hoja,-28} {c.Hoja.Codigo}  {(m is null ? "— " + c.PorQueNo : $"{m.Placas.Count} placas, {m.Cordones.Count} cordones, {m.Tornillos.Count} tornillos")}"
                + (c.Hoja.DeEjemplo ? "  [ejemplo]" : string.Empty) + (c.Hoja.NoCumplen > 0 ? $"  [{c.Hoja.NoCumplen} no cumplen]" : string.Empty)
                + (c.Hoja.Faltan.Count > 0 ? $"  faltan {c.Hoja.Faltan.Count}" : string.Empty));

            if (m is not null && m.PlacaBase is null)
            {
                Check($"{c.Hoja.Hoja}: finito y con vistas", Finitos(m) && VistasBien(m));
            }
        }

        if (salida is null)
        {
            return;
        }

        Directory.CreateDirectory(salida);
        var opciones = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true };

        foreach (var c in lista.Where(c => c.Modelo is { PlacaBase: null }))
        {
            var vistas = VistasConexion.Proyectar(c.Modelo!);
            var archivo = Path.Combine(salida, c.Hoja.Hoja.Trim().Replace(' ', '_') + ".json");
            File.WriteAllText(archivo, JsonSerializer.Serialize(new
            {
                c.Modelo!.Titulo, c.Modelo.Id, c.Modelo.Renglones, c.Modelo.Avisos,
                Vistas = vistas.Select(v => new
                {
                    v.Titulo, v.MinX, v.MinY, v.MaxX, v.MaxY,
                    Lineas = v.Lineas.Select(l => new[] { l.X1, l.Y1, l.X2, l.Y2, (double)l.Capa }),
                    Poligonos = v.Poligonos.Select(p => new { P = p.Puntos.Select(q => new[] { q.X, q.Y }), p.Cerrado, Capa = (int)p.Capa, p.Relleno }),
                    Circulos = v.Circulos.Select(q => new[] { q.X, q.Y, q.R }),
                    Llamadas = v.Llamadas.Select(q => new { q.X, q.Y, q.Texto }),
                    Cotas = v.Cotas.Select(q => new { q.X1, q.Y1, q.X2, q.Y2, q.Nx, q.Ny, q.Texto })
                })
            }, opciones));
        }

        Console.WriteLine("        vistas escritas en " + salida);
    }

    private static int Main(string[] args)
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" Prueba de las conexiones de acero leidas del libro de Excel");
        Console.WriteLine("============================================================");

        ElLector();
        LasPiezas();

        var ruta = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("CADLINK_LIBRO_ACERO");

        if (!string.IsNullOrEmpty(ruta) && File.Exists(ruta))
        {
            UnLibroDeVerdad(ruta, args.Length > 1 ? args[1] : null);
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");

        if (Fallos.Count > 0)
        {
            Console.WriteLine($" FALLARON {Fallos.Count} de {Fallos.Count + _bien}:");
            Fallos.ForEach(f => Console.WriteLine("   - " + f));
            return 1;
        }

        Console.WriteLine($" TODAS LAS COMPROBACIONES PASARON ({_bien})");
        return 0;
    }
}
