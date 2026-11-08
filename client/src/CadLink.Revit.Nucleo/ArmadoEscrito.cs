namespace CadLink.Revit.Nucleo;

/// <summary>
/// El armado de una columna o una trabe ESCRITO en Revit, sin la hoja de CadLink.
/// </summary>
/// <remarks>
/// Las varillas se nombran por el TIPO de varilla de Revit elegido en el cuadro: con ese nombre el
/// complemento sabe que tipo poner en cada una, igual que con la tabla de armaduras del armado por
/// tipo. Todo en centimetros dentro de la seccion, como <see cref="ArmadoJson"/>.
/// </remarks>
public sealed class OpcionesArmadoEscrito
{
    public double RecubrimientoCm { get; set; } = 4;

    /// <summary>El tipo de varilla del estribo y su diametro.</summary>
    public string Estribo { get; set; } = string.Empty;

    public double DiamEstriboCm { get; set; } = 0.95;

    /// <summary>La separacion de los estribos en los extremos -L/4 de cada lado- y al centro.</summary>
    public double SepExtremosCm { get; set; } = 10;

    public double SepCentroCm { get; set; } = 20;

    // ---- Trabe ----

    public int NSuperior { get; set; } = 2;

    public string VarSuperior { get; set; } = string.Empty;

    public double DiamSuperiorCm { get; set; } = 1.59;

    public int NInferior { get; set; } = 3;

    public string VarInferior { get; set; } = string.Empty;

    public double DiamInferiorCm { get; set; } = 1.59;

    /// <summary>Laterales por cada cara. Cero = sin laterales.</summary>
    public int NLateral { get; set; }

    public string VarLateral { get; set; } = string.Empty;

    public double DiamLateralCm { get; set; } = 1.27;

    // ---- Columna ----

    /// <summary>Varillas en cada cara a lo largo de su base -lado X-, contando las esquinas.</summary>
    public int NCaraX { get; set; } = 3;

    /// <summary>Varillas en cada cara a lo largo de su peralte -lado Y-, contando las esquinas.</summary>
    public int NCaraY { get; set; } = 3;

    public string VarColumna { get; set; } = string.Empty;

    public double DiamColumnaCm { get; set; } = 1.91;
}

/// <summary>Arma un <see cref="ArmadoJson"/> con receta a partir de lo escrito en el cuadro.</summary>
public static class ArmadoEscrito
{
    /// <summary>La seccion de una TRABE de <paramref name="baseCm"/> por <paramref name="alturaCm"/>.</summary>
    public static ArmadoJson Trabe(string id, double baseCm, double alturaCm, OpcionesArmadoEscrito o)
    {
        var a = Base(id, "Trabe", "TRABE", baseCm, alturaCm, o);
        var de = o.DiamEstriboCm;

        double C(double d) => o.RecubrimientoCm + de + (d / 2);

        var ySup = alturaCm - C(o.DiamSuperiorCm);
        var yInf = C(o.DiamInferiorCm);

        Fila(a, o.VarSuperior, o.DiamSuperiorCm, Math.Max(2, o.NSuperior), C(o.DiamSuperiorCm), baseCm - C(o.DiamSuperiorCm), ySup, "Superior");
        Fila(a, o.VarInferior, o.DiamInferiorCm, Math.Max(2, o.NInferior), C(o.DiamInferiorCm), baseCm - C(o.DiamInferiorCm), yInf, "Inferior");

        // Las laterales, repartidas parejo entre el lecho de abajo y el de arriba, en las dos caras.
        if (o.NLateral > 0)
        {
            var x0 = C(o.DiamLateralCm);
            var x1 = baseCm - C(o.DiamLateralCm);

            for (var i = 1; i <= o.NLateral; i++)
            {
                var y = yInf + (i * (ySup - yInf) / (o.NLateral + 1));
                a.Varillas.Add(Var(o.VarLateral, o.DiamLateralCm, x0, y, "Lateral"));
                a.Varillas.Add(Var(o.VarLateral, o.DiamLateralCm, x1, y, "Lateral"));
            }
        }

        a.Rotulo = new List<string>
        {
            "TRABE", $"«{id}»",
            $"{o.NSuperior} {o.VarSuperior} arriba, {o.NInferior} {o.VarInferior} abajo"
                + (o.NLateral > 0 ? $", {2 * o.NLateral} {o.VarLateral} laterales" : string.Empty),
            $"E. {o.Estribo} @ {o.SepExtremosCm:0.#}-{o.SepCentroCm:0.#} cm",
            $"Rec. {o.RecubrimientoCm:0.#} cm"
        };

        return a;
    }

    /// <summary>La seccion de una COLUMNA de <paramref name="baseCm"/> -X- por <paramref name="alturaCm"/> -Y-.</summary>
    public static ArmadoJson Columna(string id, double baseCm, double alturaCm, OpcionesArmadoEscrito o)
    {
        var a = Base(id, "Columna", "COLUMNA", baseCm, alturaCm, o);
        var d = o.DiamColumnaCm;
        var c = o.RecubrimientoCm + o.DiamEstriboCm + (d / 2);
        var nx = Math.Max(2, o.NCaraX);
        var ny = Math.Max(2, o.NCaraY);

        // Abajo y arriba, con las esquinas; a los lados solo las intermedias, para no repetir esquinas.
        Fila(a, o.VarColumna, d, nx, c, baseCm - c, c, "Inferior");
        Fila(a, o.VarColumna, d, nx, c, baseCm - c, alturaCm - c, "Superior");

        for (var j = 1; j < ny - 1; j++)
        {
            var y = c + (j * (alturaCm - (2 * c)) / (ny - 1));
            a.Varillas.Add(Var(o.VarColumna, d, c, y, "Lateral"));
            a.Varillas.Add(Var(o.VarColumna, d, baseCm - c, y, "Lateral"));
        }

        a.Rotulo = new List<string>
        {
            "COLUMNA", $"«{id}»", $"{Total(nx, ny)} {o.VarColumna}",
            $"E. {o.Estribo} @ {o.SepExtremosCm:0.#}-{o.SepCentroCm:0.#} cm",
            $"Rec. {o.RecubrimientoCm:0.#} cm"
        };

        return a;
    }

    /// <summary>Cuantas varillas lleva la columna: el perimetro sin contar dos veces las esquinas.</summary>
    public static int Total(int nx, int ny) => (2 * Math.Max(2, nx)) + (2 * Math.Max(0, Math.Max(2, ny) - 2));

    /// <summary>Lo que falta para poder armar. Vacio = se puede.</summary>
    public static List<string> Falta(OpcionesArmadoEscrito o, bool trabes, bool columnas)
    {
        var f = new List<string>();

        if (o.Estribo.Trim().Length == 0) { f.Add("el estribo"); }
        if (o.SepExtremosCm < 3 || o.SepCentroCm < 3) { f.Add("separaciones de estribo de al menos 3 cm"); }
        if (trabes && (o.VarSuperior.Length == 0 || o.VarInferior.Length == 0)) { f.Add("las varillas de la trabe"); }
        if (columnas && o.VarColumna.Length == 0) { f.Add("la varilla de la columna"); }

        return f;
    }

    private static ArmadoJson Base(string id, string tipo, string elemento, double b, double h, OpcionesArmadoEscrito o) => new()
    {
        Id = id,
        Tipo = tipo,
        Elemento = elemento,
        BaseCm = b,
        AlturaCm = h,
        RecubrimientoCm = o.RecubrimientoCm,
        ClaveEstribo = o.Estribo,
        DiamEstriboCm = o.DiamEstriboCm,

        // Las tres zonas del alzado: L/4, L/2 y L/4, como las de la hoja de CadLink.
        SeparacionesCm = new List<double> { o.SepExtremosCm, o.SepCentroCm, o.SepExtremosCm }
    };

    private static void Fila(ArmadoJson a, string clave, double d, int n, double x0, double x1, double y, string lecho)
    {
        for (var i = 0; i < n; i++)
        {
            a.Varillas.Add(Var(clave, d, n == 1 ? (x0 + x1) / 2 : x0 + (i * (x1 - x0) / (n - 1)), y, lecho));
        }
    }

    private static VarillaJson Var(string clave, double d, double x, double y, string lecho) => new()
    {
        Clave = clave,
        DiamCm = d,
        XCm = x,
        YCm = y,
        Lecho = lecho
    };
}
