namespace CadLink.Revit.Nucleo;

// ============================================================================
//  EL DESPIECE EN REVIT: LO QUE SE DECIDE SIN REVIT
//
//  Para cada seccion de CadLink que se armo:
//    * las PROPIEDADES DE TIPO que se escriben en su tipo de Revit -de ahi las lee la
//      etiqueta, asi que para cambiar lo que dice el plano se editan las propiedades de tipo-;
//    * el CORTE: donde va la vista de seccion, que encuadra y donde caen las llamadas de los
//      lechos («2 vars. #3C»), igual que en el plano de AutoCAD;
//    * y como se acomodan los cortes en la hoja, en renglones, como la fila de AutoCAD.
//
//  Metros en el modelo; en la hoja, metros de PAPEL.
// ============================================================================

/// <summary>Lo que se escribe en las propiedades de tipo de Revit.</summary>
/// <param name="CodigoDeMontaje">Las varillas, como en el rotulo de AutoCAD: <c>4 vars. #3C</c>.</param>
/// <param name="NotaClave">Siempre <c>CONCRETO</c>.</param>
/// <param name="Modelo">Las medidas: <c>15 X 30 CM</c>.</param>
/// <param name="Descripcion">El ID de la seccion en CadLink: <c>T-04</c>.</param>
/// <param name="MarcaDeTipo">El elemento: <c>TRABE</c>, <c>CASTILLO</c>, <c>COLUMNA</c>…</param>
/// <param name="ComentariosDeTipo">El estribo: <c>Estr. #3C @15 cm</c>, para que la etiqueta lo pueda leer.</param>
public sealed record PropiedadesDeTipo(
    string CodigoDeMontaje, string NotaClave, string Modelo, string Descripcion, string MarcaDeTipo,
    string ComentariosDeTipo = "");

/// <summary>Una llamada de lecho en el corte, en metros de la vista: X a la derecha, Y arriba.</summary>
public sealed record LlamadaDeCorte(string Texto, double X, double Y);

/// <summary>
/// La vista de corte de una seccion: su sistema -origen y direcciones, en el modelo- y su caja
/// en coordenadas de la vista. La X de la vista es la base de la seccion y la Y su peralte, asi
/// que se ve como en el plano de AutoCAD.
/// </summary>
/// <param name="YCotaBase">A que altura de la vista va la cota de la base: encima de la seccion.</param>
/// <param name="XCotaAltura">Y la del peralte: a la derecha, porque a la izquierda van las llamadas.</param>
public sealed record CorteDeSeccion(
    string Nombre, V3 Origen, V3 EjeX, V3 EjeY, V3 EjeZ,
    V3 Min, V3 Max, List<LlamadaDeCorte> Llamadas, V3 PuntoDeEtiqueta,
    double YCotaBase = 0, double XCotaAltura = 0);

public static class PlanDespiece
{
    /// <summary>La escala de los cortes: 1:10, la del plano de AutoCAD.</summary>
    public const int Escala = 10;

    /// <summary>Aire alrededor de la seccion en el corte, en m: llamadas a la izquierda y etiqueta abajo.</summary>
    public const double AireIzquierda = 0.32;
    public const double AireDerecha = 0.10;
    public const double AireArriba = 0.10;
    public const double AireAbajo = 0.40;

    /// <summary>Lo profundo que mira el corte, para que salga al menos un estribo detras.</summary>
    public const double Profundidad = 0.30;

    public static PropiedadesDeTipo Propiedades(ArmadoJson a)
    {
        var varillas = a.Rotulo.Where(l => l.Contains(" vars. ", StringComparison.Ordinal)).ToList();

        // Archivos sin rotulo: se cuentan las varillas como en el rotulo.
        if (varillas.Count == 0)
        {
            varillas = a.Varillas
                .GroupBy(v => v.Clave)
                .OrderByDescending(g => g.First().DiamCm)
                .Select(g => $"{g.Count()} vars. {g.Key}C")
                .ToList();
        }

        var elemento = a.Elemento.Trim().Length > 0 ? a.Elemento.Trim().ToUpperInvariant() : a.Tipo.ToUpperInvariant();

        var estribo = a.Rotulo.FirstOrDefault(l =>
            l.StartsWith("Estr.", StringComparison.Ordinal) || l.StartsWith("Zuncho", StringComparison.Ordinal))
            ?? EstriboComoAutoCad(a);

        return new PropiedadesDeTipo(
            string.Join(" + ", varillas),
            "CONCRETO",
            $"{Cm(a.BaseCm)} X {Cm(a.AlturaCm)} CM",
            a.Id.Trim(),
            elemento,
            estribo);
    }

    /// <summary>
    /// El corte de una pieza ya armada: a media longitud en la trabe, a media altura en la
    /// columna, mirando la seccion como en el plano.
    /// </summary>
    public static CorteDeSeccion Corte(ArmadoJson a, MarcoPieza m)
    {
        var b = a.BaseCm / 100;
        var h = a.AlturaCm / 100;

        var origen = m.En(a.BaseCm / 2, a.AlturaCm / 2, m.LargoM / 2);
        var z = m.Ex.Cruz(m.Ey).Unitario();

        var nombre = a.EsHorizontal
            ? $"Corte {a.Id.Trim()} - {m.LargoM:0.00}m"
            : $"Corte {a.Id.Trim()}";

        var min = new V3(-(b / 2) - AireIzquierda, -(h / 2) - AireAbajo, -Profundidad);
        var max = new V3((b / 2) + AireDerecha, (h / 2) + AireArriba, 0);

        return new CorteDeSeccion(
            nombre, origen, m.Ex, m.Ey, z, min, max,
            Llamadas(a),
            new V3(0, -(h / 2) - 0.08, 0),
            (h / 2) + 0.06,
            (b / 2) + 0.06);
    }

    /// <summary>
    /// Las llamadas de los lechos, a la izquierda de la seccion: una por renglon de varillas
    /// del mismo diametro, con su cantidad -«2 vars. #3C»-, a la altura de su renglon.
    /// </summary>
    public static List<LlamadaDeCorte> Llamadas(ArmadoJson a)
    {
        var b = a.BaseCm / 100;
        var h = a.AlturaCm / 100;

        return a.Varillas
            .GroupBy(v => (Y: Math.Round(v.YCm, 1), v.Clave))
            .OrderByDescending(g => g.Key.Y)
            .Select(g => new LlamadaDeCorte(
                $"{g.Count()} vars. {g.Key.Clave}C",
                -(b / 2) - AireIzquierda + 0.02,
                (g.Key.Y / 100) - (h / 2) + 0.015))
            .ToList();
    }

    /// <summary>
    /// Donde va cada corte, en metros de papel desde la esquina de abajo a la izquierda de su
    /// hoja: en renglones de izquierda a derecha, empezando arriba, y cuando la hoja se llena,
    /// en la siguiente. Devuelve la hoja -0, 1, 2...- y el CENTRO de cada corte, que es lo que
    /// pide Revit para colocar la vista.
    /// </summary>
    /// <param name="tamanos">Lo que mide cada corte en el papel: ancho y alto.</param>
    public static List<(int Hoja, double X, double Y)> Acomodo(
        IReadOnlyList<(double Ancho, double Alto)> tamanos, double anchoHoja, double altoHoja,
        double margen = 0.03, double aire = 0.01)
    {
        var res = new List<(int, double, double)>();
        var hoja = 0;
        var x = margen;
        var arriba = altoHoja - margen;
        var altoRenglon = 0.0;
        var enLaHoja = 0;

        foreach (var (w, hh) in tamanos)
        {
            // No cabe en el renglon: al siguiente. El primero de un renglon va siempre.
            if (x > margen && x + w > anchoHoja - margen)
            {
                x = margen;
                arriba -= altoRenglon + aire;
                altoRenglon = 0;
            }

            // No cabe en la hoja: a la siguiente. El primero de una hoja va siempre.
            if (enLaHoja > 0 && arriba - hh < margen)
            {
                hoja++;
                x = margen;
                arriba = altoHoja - margen;
                altoRenglon = 0;
                enLaHoja = 0;
            }

            res.Add((hoja, x + (w / 2), arriba - (hh / 2)));
            x += w + aire;
            altoRenglon = Math.Max(altoRenglon, hh);
            enLaHoja++;
        }

        return res;
    }

    /// <summary>Lo que mide el corte en el papel, a su escala.</summary>
    public static (double Ancho, double Alto) EnPapel(CorteDeSeccion c) =>
        ((c.Max.X - c.Min.X) / Escala, (c.Max.Y - c.Min.Y) / Escala);

    /// <summary>
    /// El nombre de un corte nuevo que no choque con las vistas que ya hay: «Corte T-01», y si
    /// existe, «Corte T-01 (2)», «(3)»…
    /// </summary>
    public static string NombreLibre(string deseado, ICollection<string> existentes)
    {
        var nombre = deseado.Trim().Length == 0 ? "Corte" : deseado.Trim();
        var res = nombre;

        for (var i = 2; existentes.Contains(res); i++)
        {
            res = $"{nombre} ({i})";
        }

        return res;
    }

    /// <summary>
    /// El renglon del estribo como lo rotula AutoCAD -«Estr. #3C @15 cm», o «@10-15-10 cm» si
    /// las zonas van distintas-, para archivos cuyo rotulo no lo trae.
    /// </summary>
    public static string EstriboComoAutoCad(ArmadoJson a)
    {
        var clave = a.ClaveEstribo.Trim();

        if (clave.Length == 0 || a.SeparacionesCm is not { Count: > 0 } sep)
        {
            return string.Empty;
        }

        var texto = sep.Distinct().Count() == 1
            ? Cm(sep[0])
            : string.Join("-", sep.Select(Cm));

        return $"Estr. {clave}C @{texto} cm";
    }

    private static string Cm(double v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
