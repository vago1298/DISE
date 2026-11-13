namespace CadLink.Revit.Nucleo;

/// <summary>
/// La hoja PLACA BASE del libro de acero, en las medidas de la hoja Placa Base de CadLink (cm).
/// </summary>
/// <remarks>
/// En el libro, N va en la direccion del momento -el peralte de la columna- y B a lo ancho del
/// patin. Las anclas van en dos lineas extremas a ±f del eje de la columna, con ny anclas cada una
/// a sy, mas las intermedias de los bordes si nx &gt; 2.
/// </remarks>
public sealed record DatosPlacaBase(
    string Marca, string TipoColumna, string Perfil, string Acero,
    double NCm, double BCm, double EspesorCm,
    double DadoNCm, double DadoBCm,
    int AnclasN, int AnclasB, double FCm, double SepNCm, double SepBCm,
    double DiametroAnclaCm, double AgujeroCm, double AnclajeCm, bool Gancho, double GanchoCm,
    double GroutCm, double SoldaduraCm, string Electrodo,
    double PeralteCm, double AnchoPatinCm)
{
    /// <summary>Del ancla extrema al borde de la placa en N.</summary>
    public double BordeNCm => (NCm / 2) - FCm;

    /// <summary>Del ancla extrema al borde de la placa en B.</summary>
    public double BordeBCm => (BCm - (Math.Max(AnclasB - 1, 0) * SepBCm)) / 2;

    /// <summary>Las anclas, desde el centro de la placa: X a lo largo de N, Y a lo largo de B.</summary>
    public List<(double XCm, double YCm)> Anclas()
    {
        var lista = new List<(double, double)>();
        var ny = Math.Max(AnclasB, 1);
        var nx = Math.Max(AnclasN, 2);
        var ly = (ny - 1) * SepBCm;

        // Las dos lineas extremas, a ±f.
        foreach (var x in new[] { -FCm, FCm })
        {
            for (var j = 0; j < ny; j++)
            {
                lista.Add((x, (-ly / 2) + (j * SepBCm)));
            }
        }

        // Las intermedias de los bordes largos, si hay mas de dos por borde.
        if (nx > 2 && ny > 1)
        {
            var paso = SepNCm > 0 ? SepNCm : 2 * FCm / (nx - 1);

            for (var i = 1; i < nx - 1; i++)
            {
                var x = -FCm + (i * paso);
                lista.Add((x, -ly / 2));
                lista.Add((x, ly / 2));
            }
        }

        return lista;
    }
}

public static class PlacaBaseDelLibro
{
    public static DatosPlacaBase Datos(HojaLeida h)
    {
        var anc = h.Opcion("Anc");

        return new DatosPlacaBase(
            Marca: h.Id,
            TipoColumna: h.Opcion("Tipo") == "HSS" ? "HSS" : "W",
            Perfil: h.Txt("PerfC"),
            Acero: h.Txt("AcP"),
            NCm: h.Num("N"),
            BCm: h.Num("Bp"),
            EspesorCm: h.Num("tp"),
            DadoNCm: h.Num("Nd"),
            DadoBCm: h.Num("Bd"),
            AnclasN: (int)Math.Round(h.Num("nx", 2)),
            AnclasB: (int)Math.Round(h.Num("nt", 2)),
            FCm: h.Num("f"),
            SepNCm: h.Num("sx"),
            SepBCm: h.Num("s"),
            DiametroAnclaCm: h.Num("do"),
            AgujeroCm: h.Num("Hol"),
            AnclajeCm: h.Num("hef"),
            Gancho: anc == "GANCHO",
            GanchoCm: anc == "GANCHO" ? h.Num("eh") : 0,
            GroutCm: h.Si("Agr") ? h.Num("gg") : 0,
            SoldaduraCm: h.Num("wc"),
            Electrodo: h.Txt("Elec"),
            PeralteCm: h.Num("dc"),
            AnchoPatinCm: h.Num("bfc"));
    }

    /// <summary>La placa base en el formato que modela Revit.</summary>
    public static PlacaBaseJson AJson(HojaLeida h)
    {
        var d = Datos(h);
        var placa = new PlacaBaseJson
        {
            Marca = d.Marca,
            Seccion = d.Perfil,
            Acero = d.Acero,
            LadoXM = d.NCm / 100,
            LadoYM = d.BCm / 100,
            EspesorM = d.EspesorCm / 100,
            PerfilXM = d.PeralteCm / 100,
            PerfilYM = d.AnchoPatinCm / 100,
            GroutM = d.GroutCm / 100,
            SoldaduraM = d.SoldaduraCm / 100,
            Electrodo = d.Electrodo
        };

        var dia = d.DiametroAnclaCm / 100;
        var tuerca = 1.5 * dia;

        foreach (var (x, y) in d.Anclas())
        {
            // La pata del gancho apunta hacia el centro de la placa, a lo largo de N.
            var hacia = x > 0 ? -1.0 : 1.0;

            placa.Anclas.Add(new AnclaJson
            {
                X = x / 100,
                Y = y / 100,
                DiametroM = dia,
                AgujeroM = d.AgujeroCm > 0 ? d.AgujeroCm / 100 : dia + 0.0016,
                PuntaM = (d.EspesorCm / 100) + dia + tuerca,
                FondoM = -d.AnclajeCm / 100,
                PataM = d.GanchoCm / 100,
                PataX = d.Gancho ? hacia : 0,
                PataY = 0,
                TuercaM = tuerca,
                TuercaAltoM = dia
            });
        }

        return placa;
    }
}
