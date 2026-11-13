using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

// ============================================================================
//  LAS HOJAS DEL LIBRO DE ACERO (NTC-Acero 2023) Y SUS DATOS
//
//  Cada hoja del libro lleva un dato por fila: la etiqueta en la columna B y el valor en la C. El
//  usuario copia hojas -«PLACA GUSSET 1», «PLACA GUSSET 2»- y las hojas crecen con cada version
//  del libro, asi que las filas NO son fijas. Por eso se lee por ETIQUETA, en el orden de la hoja:
//  dos datos con la misma etiqueta -el Ry de la viga y el de la columna- se distinguen porque
//  aparecen en ese orden.
// ============================================================================

/// <summary>
/// Un dato de una hoja: su nombre corto, la etiqueta de la columna B, su valor de fabrica, sus
/// opciones («A|B|C»; «@IR», «@OR», «@IS», «@L», «@ACEROS» son listas del catalogo) y su unidad.
/// </summary>
public sealed record DatoDelLibro(string Nombre, string Etiqueta, bool EsCaptura, string? Defecto,
    string? Opciones = null, string Unidad = "")
{
    /// <summary>La etiqueta sin el «=» del final, para mostrarla.</summary>
    public string Descripcion => Etiqueta.TrimEnd('=', ' ');
}

/// <summary>Un tipo de hoja del libro: su codigo, su nombre, su titulo y sus datos en orden.</summary>
public sealed record HojaDelLibro(string Codigo, string Hoja, string Titulo, IReadOnlyList<DatoDelLibro> Datos);

public static partial class EtiquetasExcel
{
    /// <summary>El tipo de hoja por su codigo («UV», «GP»…), o null.</summary>
    public static HojaDelLibro? DeCodigo(string codigo) =>
        Hojas.FirstOrDefault(h => h.Codigo == codigo);

    /// <summary>
    /// Que tipo de hoja es: por su nombre sin el numero de la copia («PLACA GUSSET 2» → PLACA
    /// GUSSET) y, si el usuario la renombro, por el titulo de su primer renglon.
    /// </summary>
    public static HojaDelLibro? DeLaHoja(string nombre, string? titulo)
    {
        var baseDelNombre = SinCopia(nombre);

        var porNombre = Hojas.FirstOrDefault(h => string.Equals(Normalizar(h.Hoja), baseDelNombre, StringComparison.Ordinal));

        if (porNombre is not null)
        {
            return porNombre;
        }

        if (!string.IsNullOrWhiteSpace(titulo))
        {
            var t = Normalizar(titulo);
            return Hojas.FirstOrDefault(h => string.Equals(Normalizar(h.Titulo), t, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// El nombre de la hoja sin lo que le pone el usuario o Excel a la copia: «PLACA GUSSET 1»,
    /// «PLACA GUSSET (2)», «PLACA GUSSET - copia».
    /// </summary>
    public static string SinCopia(string nombre)
    {
        var n = Normalizar(nombre);
        n = Regex.Replace(n, @"\s*-\s*copia(\s*\(\d+\))?$", string.Empty);
        n = Regex.Replace(n, @"\s*\(\d+\)$", string.Empty);
        n = Regex.Replace(n, @"\s+\d+$", string.Empty);
        return n.Trim();
    }

    /// <summary>Minusculas, sin acentos, espacios sencillos, sin el «=» del final.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var d = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espacio = false;

        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                espacio = sb.Length > 0;
                continue;
            }

            if (espacio)
            {
                sb.Append(' ');
                espacio = false;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        var s = sb.ToString();
        return s.EndsWith("=", StringComparison.Ordinal) ? s.TrimEnd('=').TrimEnd() : s;
    }
}

/// <summary>Los datos que se leyeron de UNA hoja del libro.</summary>
public sealed class HojaLeida
{
    private readonly Dictionary<string, CeldaExcel> _valores = new(StringComparer.Ordinal);

    public HojaLeida(HojaDelLibro tipo, string hoja)
    {
        Tipo = tipo;
        Hoja = hoja;
    }

    public HojaDelLibro Tipo { get; }

    public string Codigo => Tipo.Codigo;

    /// <summary>El nombre de la pestaña, tal cual: «PLACA GUSSET 2».</summary>
    public string Hoja { get; }

    /// <summary>Los datos de la tabla que no se encontraron en la hoja.</summary>
    public List<string> Faltan { get; } = new();

    /// <summary>Cuantas revisiones dicen NO CUMPLE (o algo que no es CUMPLE, N/A ni REQUIERE ATIESADOR).</summary>
    public int NoCumplen { get; set; }

    /// <summary>Cuantas dicen REQUIERE ATIESADOR.</summary>
    public int RequierenAtiesador { get; set; }

    /// <summary>Todos los datos capturables valen lo de fabrica: la hoja trae el ejemplo.</summary>
    public bool DeEjemplo { get; set; }

    /// <summary>
    /// Las revisiones que no dicen CUMPLE ni N/A, con lo que dicen: «Alma de la viga: V ≤ RN —
    /// NO CUMPLE». Asi se puede ver en el libro CUAL es, y no solo cuantas.
    /// </summary>
    public List<string> Fallas { get; } = new();

    /// <summary>Revisiones cuyo estado es un error de Excel (#N/A, #¡VALOR!): no es que no cumplan.</summary>
    public int ConError { get; set; }

    /// <summary>
    /// Los datos que se pidieron con <see cref="Num"/> o <see cref="Txt"/>: con esto se sabe que
    /// datos usa cada tipo de conexion para capturarlos a mano.
    /// </summary>
    public HashSet<string> Pedidos { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Una hoja armada con valores capturados a mano (sin libro): los numeros con punto decimal.
    /// </summary>
    public static HojaLeida DeValores(HojaDelLibro tipo, string hoja, IReadOnlyDictionary<string, string> valores)
    {
        var h = new HojaLeida(tipo, hoja);

        foreach (var (nombre, texto) in valores)
        {
            var t = (texto ?? string.Empty).Trim();
            double? n = double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            h.Poner(nombre, new CeldaExcel(t, n, false));
        }

        return h;
    }

    public IReadOnlyDictionary<string, CeldaExcel> Valores => _valores;

    public void Poner(string nombre, CeldaExcel c) => _valores[nombre] = c;

    /// <summary>Pone un numero si la hoja no lo trae (los calculados, al capturar a mano).</summary>
    public void SiFalta(string nombre, double valor)
    {
        if (!_valores.TryGetValue(nombre, out var c) || (c.Numero is null && c.Texto.Length == 0))
        {
            _valores[nombre] = new CeldaExcel(valor.ToString("R", CultureInfo.InvariantCulture), valor, true);
        }
    }

    /// <summary>Pone un texto si la hoja no lo trae.</summary>
    public void SiFalta(string nombre, string valor)
    {
        if (!_valores.TryGetValue(nombre, out var c) || c.Texto.Length == 0)
        {
            _valores[nombre] = new CeldaExcel(valor, null, true);
        }
    }

    public bool Tiene(string nombre) => _valores.ContainsKey(nombre);

    /// <summary>El numero, o <paramref name="siNoHay"/> si falta o no es numero.</summary>
    public double Num(string nombre, double siNoHay = 0)
    {
        Pedidos.Add(nombre);

        if (!_valores.TryGetValue(nombre, out var c))
        {
            return siNoHay;
        }

        if (c.Numero is double d && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            return d;
        }

        return double.TryParse(c.Texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
            ? t
            : siNoHay;
    }

    /// <summary>El texto, sin espacios de mas. Vacio si falta.</summary>
    public string Txt(string nombre)
    {
        Pedidos.Add(nombre);
        return _valores.TryGetValue(nombre, out var c) ? c.Texto.Trim() : string.Empty;
    }

    /// <summary>El texto en mayusculas sin acentos, para comparar opciones («PENETRACION»).</summary>
    public string Opcion(string nombre) => EtiquetasExcel.Normalizar(Txt(nombre)).ToUpperInvariant();

    public bool Si(string nombre) => Opcion(nombre) == "SI";

    /// <summary>La identificacion del elemento; si esta vacia, el nombre de la hoja.</summary>
    public string Id => Txt("ID") is { Length: > 0 } id ? id : Hoja.Trim();
}

/// <summary>Lee las hojas del libro de acero que CadLink sabe leer.</summary>
public static class LectorDelLibro
{
    /// <summary>
    /// Todas las hojas reconocidas, en el orden de las pestañas. Las de puro calculo -vigas,
    /// vibraciones- tambien salen: quien llama decide que hacer con ellas.
    /// </summary>
    public static List<HojaLeida> Leer(LibroExcel libro)
    {
        var hojas = new List<HojaLeida>();

        foreach (var nombre in libro.Hojas)
        {
            var hoja = libro.Hoja(nombre);

            if (hoja is null)
            {
                continue;
            }

            var titulo = TituloDe(hoja);
            var tipo = EtiquetasExcel.DeLaHoja(nombre, titulo);

            if (tipo is null)
            {
                continue;
            }

            hojas.Add(LeerHoja(hoja, tipo));
        }

        return hojas;
    }

    /// <summary>El titulo: el primer texto largo de los primeros renglones.</summary>
    private static string? TituloDe(HojaExcel hoja)
    {
        for (var f = 1; f <= 3; f++)
        {
            for (var c = 1; c <= 3; c++)
            {
                var t = hoja.Texto(f, c);

                if (t.Length > 12)
                {
                    return t;
                }
            }
        }

        return null;
    }

    /// <summary>Los datos de una hoja, buscados por etiqueta en el orden de la hoja.</summary>
    public static HojaLeida LeerHoja(HojaExcel hoja, HojaDelLibro tipo)
    {
        var leida = new HojaLeida(tipo, hoja.Nombre);

        // Las etiquetas de la columna B, en orden de fila.
        var filas = new List<(int Fila, string Etiqueta)>();

        for (var f = 1; f <= hoja.UltimaFila; f++)
        {
            var t = hoja.Texto(f, 2);

            if (t.Length > 0)
            {
                filas.Add((f, EtiquetasExcel.Normalizar(t)));
            }
        }

        var desde = 0;

        foreach (var dato in tipo.Datos)
        {
            var buscada = EtiquetasExcel.Normalizar(dato.Etiqueta);
            var i = Buscar(filas, buscada, desde, exacta: true);

            if (i < 0)
            {
                // Una etiqueta de otra version de la hoja: «Grueso de la placa, tp» contra
                // «Grueso de la placa». Se busca por el principio, sin el simbolo.
                var sinSimbolo = SinSimbolo(buscada);
                i = Buscar(filas, sinSimbolo, desde, exacta: false);

                if (i < 0)
                {
                    i = Buscar(filas, sinSimbolo, 0, exacta: false);
                }
            }

            if (i < 0)
            {
                leida.Faltan.Add(dato.Nombre);
                continue;
            }

            desde = Math.Max(desde, i + 1);
            var c = hoja.Celda(filas[i].Fila, 3);

            if (c is { } celda)
            {
                leida.Poner(dato.Nombre, celda);
            }
            else
            {
                leida.Poner(dato.Nombre, new CeldaExcel(string.Empty, null, false));
            }
        }

        // Las revisiones: «CUMPLE», «NO CUMPLE», «N/A», «REQUIERE ATIESADOR» en la columna D.
        foreach (var (f, etiqueta) in filas)
        {
            if (!etiqueta.StartsWith("revision", StringComparison.Ordinal))
            {
                continue;
            }

            var crudo = hoja.Texto(f, 4).Trim();
            var estado = EtiquetasExcel.Normalizar(crudo).ToUpperInvariant();

            if (estado.Length == 0 || estado == "CUMPLE" || estado == "N/A" || estado == "-")
            {
                continue;
            }

            var cual = hoja.Texto(f, 2).Trim();
            cual = cual.StartsWith("Revisión:", StringComparison.OrdinalIgnoreCase) ? cual["Revisión:".Length..].Trim() : cual;

            if (estado.StartsWith("#", StringComparison.Ordinal))
            {
                // Un error de Excel en la formula del estado: no es una revision que no cumple.
                leida.ConError++;
                leida.Fallas.Add($"{cual} — error de Excel {crudo}");
            }
            else if (estado == "REQUIERE ATIESADOR")
            {
                leida.RequierenAtiesador++;
            }
            else
            {
                leida.NoCumplen++;
                leida.Fallas.Add($"{cual} — {crudo}");
            }
        }

        leida.DeEjemplo = EsDeEjemplo(leida);
        return leida;
    }

    private static int Buscar(List<(int Fila, string Etiqueta)> filas, string buscada, int desde, bool exacta)
    {
        if (buscada.Length == 0)
        {
            return -1;
        }

        for (var i = Math.Max(0, desde); i < filas.Count; i++)
        {
            var e = filas[i].Etiqueta;

            if (exacta ? e == buscada : e.StartsWith(buscada, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>«grueso de la placa, tp» → «grueso de la placa».</summary>
    private static string SinSimbolo(string etiqueta)
    {
        var coma = etiqueta.LastIndexOf(", ", StringComparison.Ordinal);
        return coma > 8 ? etiqueta[..coma] : etiqueta;
    }

    /// <summary>
    /// Todos los datos capturables valen lo de fabrica. Una hoja que nunca se lleno trae el
    /// ejemplo del libro, y dibujarla seria dibujar algo que no es del proyecto.
    /// </summary>
    private static bool EsDeEjemplo(HojaLeida h)
    {
        var comparados = 0;

        foreach (var d in h.Tipo.Datos)
        {
            if (!d.EsCaptura || d.Defecto is null || !h.Tiene(d.Nombre) || d.Nombre == "ID")
            {
                continue;
            }

            comparados++;
            var c = h.Valores[d.Nombre];

            if (double.TryParse(d.Defecto, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                if (c.Numero is not double v || Math.Abs(v - n) > 1e-6 * Math.Max(1, Math.Abs(n)))
                {
                    return false;
                }
            }
            else if (!string.Equals(EtiquetasExcel.Normalizar(c.Texto), EtiquetasExcel.Normalizar(d.Defecto), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return comparados > 3;
    }
}
