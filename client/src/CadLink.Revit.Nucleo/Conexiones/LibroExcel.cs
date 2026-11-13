using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LECTOR MINIMO DE LIBROS DE EXCEL (.xlsx / .xlsm)
//
//  Un .xlsx o .xlsm es un ZIP con XML adentro. Para leer los VALORES de las celdas no hace falta
//  Excel ni ninguna libreria: basta System.IO.Compression y System.Xml.Linq, que vienen con .NET.
//  Asi el mismo codigo corre en CadLink, en el complemento de Revit y en las pruebas de Linux.
//
//  Se lee lo que Excel GUARDO como resultado de cada formula (el <v> de la celda). Las formulas
//  no se evaluan aqui: si el libro se modifico fuera de Excel y no se volvio a guardar, esos
//  resultados pueden estar viejos. Para eso esta RecalcularAlAbrir.
// ============================================================================

/// <summary>Una celda leida: su texto, su numero si lo es, y si viene de una formula.</summary>
public readonly record struct CeldaExcel(string Texto, double? Numero, bool EsFormula);

/// <summary>Una hoja del libro: sus celdas por fila y columna (1 = A).</summary>
public sealed class HojaExcel
{
    private readonly Dictionary<(int Fila, int Columna), CeldaExcel> _celdas;

    internal HojaExcel(string nombre, Dictionary<(int Fila, int Columna), CeldaExcel> celdas)
    {
        Nombre = nombre;
        _celdas = celdas;
        UltimaFila = celdas.Count == 0 ? 0 : celdas.Keys.Max(k => k.Fila);
    }

    public string Nombre { get; }

    public int UltimaFila { get; }

    public CeldaExcel? Celda(int fila, int columna) =>
        _celdas.TryGetValue((fila, columna), out var c) ? c : null;

    public string Texto(int fila, int columna) => Celda(fila, columna)?.Texto ?? string.Empty;

    /// <summary>Todas las celdas, para recorrerlas.</summary>
    public IEnumerable<KeyValuePair<(int Fila, int Columna), CeldaExcel>> Celdas => _celdas;
}

/// <summary>El libro: los nombres de sus hojas, en orden, y cada hoja cuando se pide.</summary>
public sealed class LibroExcel
{
    private readonly Dictionary<string, string> _rutaDeHoja;
    private readonly Dictionary<string, HojaExcel> _leidas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _partes;
    private readonly List<string> _comunes;

    private LibroExcel(List<string> nombres, Dictionary<string, string> rutas, Dictionary<string, byte[]> partes,
        List<string> comunes, bool recalcular)
    {
        Hojas = nombres;
        _rutaDeHoja = rutas;
        _partes = partes;
        _comunes = comunes;
        RecalcularAlAbrir = recalcular;
    }

    /// <summary>Los nombres de las hojas, en el orden de las pestañas.</summary>
    public IReadOnlyList<string> Hojas { get; }

    /// <summary>
    /// El libro pide recalcular al abrirse: lo modifico un programa que no es Excel y los
    /// resultados de las formulas que se guardaron pueden estar viejos.
    /// </summary>
    public bool RecalcularAlAbrir { get; }

    /// <summary>Abre el libro. Se puede leer aunque este abierto en Excel.</summary>
    public static LibroExcel Abrir(string ruta)
    {
        // FileShare.ReadWrite: Excel tiene el archivo abierto con escritura y aun asi se deja leer.
        using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Leer(fs);
    }

    public static LibroExcel Leer(Stream flujo)
    {
        var partes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var zip = new ZipArchive(flujo, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var e in zip.Entries)
            {
                // Solo lo que hace falta: el libro, sus relaciones, los textos y las hojas.
                var n = e.FullName.Replace('\\', '/');

                if (!n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && !n.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!n.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                partes[n] = ms.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("El archivo no es un libro de Excel (.xlsx o .xlsm).");
        }

        if (!partes.TryGetValue("xl/workbook.xml", out var libro))
        {
            throw new InvalidDataException("El archivo no es un libro de Excel: le falta xl/workbook.xml.");
        }

        var wb = XDocument.Parse(System.Text.Encoding.UTF8.GetString(libro));
        XNamespace m = wb.Root!.Name.Namespace;
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var destinos = new Dictionary<string, string>(StringComparer.Ordinal);

        if (partes.TryGetValue("xl/_rels/workbook.xml.rels", out var rels))
        {
            var rd = XDocument.Parse(System.Text.Encoding.UTF8.GetString(rels));

            foreach (var rel in rd.Root!.Elements())
            {
                var id = (string?)rel.Attribute("Id");
                var t = (string?)rel.Attribute("Target");

                if (id is null || t is null)
                {
                    continue;
                }

                t = t.Replace('\\', '/');
                t = t.StartsWith("/", StringComparison.Ordinal) ? t.TrimStart('/') : "xl/" + t;
                destinos[id] = t;
            }
        }

        var nombres = new List<string>();
        var rutas = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var h in wb.Root.Descendants(m + "sheet"))
        {
            var nombre = (string?)h.Attribute("name") ?? string.Empty;
            var id = (string?)h.Attribute(r + "id");

            if (id is not null && destinos.TryGetValue(id, out var ruta))
            {
                nombres.Add(nombre);
                rutas[nombre] = ruta;
            }
        }

        var recalcular = wb.Root.Descendants(m + "calcPr")
            .Any(c => (string?)c.Attribute("fullCalcOnLoad") is "1" or "true");

        var comunes = new List<string>();

        if (partes.TryGetValue("xl/sharedStrings.xml", out var ss))
        {
            var sd = XDocument.Parse(System.Text.Encoding.UTF8.GetString(ss));
            XNamespace sm = sd.Root!.Name.Namespace;

            foreach (var si in sd.Root.Elements(sm + "si"))
            {
                // El texto de un renglon con formato viene partido en <r><t>: se juntan, sin la
                // pronunciacion fonetica (<rPh>) que Excel guarda para el japones.
                comunes.Add(string.Concat(si.Descendants(sm + "t")
                    .Where(t => t.Parent?.Name.LocalName != "rPh")
                    .Select(t => t.Value)));
            }
        }

        return new LibroExcel(nombres, rutas, partes, comunes, recalcular);
    }

    /// <summary>La hoja con ese nombre exacto, o null.</summary>
    public HojaExcel? Hoja(string nombre)
    {
        if (_leidas.TryGetValue(nombre, out var ya))
        {
            return ya;
        }

        if (!_rutaDeHoja.TryGetValue(nombre, out var ruta) || !_partes.TryGetValue(ruta, out var bytes))
        {
            return null;
        }

        var hoja = new HojaExcel(nombre, LeerCeldas(bytes, _comunes));
        _leidas[nombre] = hoja;
        return hoja;
    }

    private static Dictionary<(int Fila, int Columna), CeldaExcel> LeerCeldas(byte[] bytes, List<string> comunes)
    {
        var celdas = new Dictionary<(int Fila, int Columna), CeldaExcel>();
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        XNamespace m = doc.Root!.Name.Namespace;

        foreach (var c in doc.Root.Descendants(m + "c"))
        {
            var refe = (string?)c.Attribute("r");

            if (refe is null || !Referencia(refe, out var fila, out var col))
            {
                continue;
            }

            var tipo = (string?)c.Attribute("t") ?? "n";
            var v = c.Element(m + "v")?.Value;
            var formula = c.Element(m + "f") is not null;
            string texto;
            double? numero = null;

            switch (tipo)
            {
                case "s":
                    texto = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                            && i >= 0 && i < comunes.Count
                        ? comunes[i]
                        : string.Empty;
                    break;
                case "inlineStr":
                    texto = string.Concat(c.Descendants(m + "t").Select(t => t.Value));
                    break;
                case "str":
                case "e":
                    texto = v ?? string.Empty;
                    break;
                case "b":
                    texto = v == "1" ? "VERDADERO" : "FALSO";
                    break;
                default:
                    texto = v ?? string.Empty;

                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        numero = d;
                    }

                    break;
            }

            if (texto.Length == 0 && numero is null && !formula)
            {
                continue;
            }

            celdas[(fila, col)] = new CeldaExcel(texto, numero, formula);
        }

        return celdas;
    }

    /// <summary>«C12» a fila 12, columna 3.</summary>
    public static bool Referencia(string refe, out int fila, out int columna)
    {
        fila = 0;
        columna = 0;
        var i = 0;

        while (i < refe.Length && char.IsLetter(refe[i]))
        {
            columna = (columna * 26) + (char.ToUpperInvariant(refe[i]) - 'A' + 1);
            i++;
        }

        return columna > 0 && i < refe.Length
            && int.TryParse(refe.AsSpan(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out fila);
    }
}
