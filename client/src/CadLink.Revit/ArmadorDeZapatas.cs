using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// Pone las parrillas de la cimentacion: abajo y, si se pide, arriba, en las dos direcciones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dos caminos, segun la zapata.</b> Si es una zapata de Revit -familia de cimentacion aislada,
/// cimentacion de muro o losa de cimentacion- se ponen varillas NATIVAS: se cuantifican, se
/// etiquetan y se editan como cualquier armado. Si es una zapata de CadLink, que es un DirectShape,
/// Revit no la admite como anfitrion de armado; ahi las varillas se modelan como solidos con su
/// pata, para que se vean en el modelo y en los cortes, y se avisa que no se cuantifican.
/// </para>
/// <para>
/// La planta se mide del solido: el lado mayor sale de la arista mas larga de su cara de abajo, asi
/// que una zapata girada se arma girada. Volver a armar reemplaza el armado de CadLink.
/// </para>
/// </remarks>
internal static class ArmadorDeZapatas
{
    /// <summary>Una zapata elegida y el armado que le toca. Null = no hay fila para ella.</summary>
    public readonly record struct ZapataElegida(Element Zapata, ArmadoZapataJson? Armado, string Origen);

    public static ResultadoDetalles Armar(Document doc, IReadOnlyList<ZapataElegida> zapatas)
    {
        var r = new ResultadoDetalles();
        var tipos = TiposDeVarilla(doc);
        var categoria = Solidos.Categoria(doc, BuiltInCategory.OST_Rebar, BuiltInCategory.OST_GenericModel);

        using var t = new Transaction(doc, "Armado de cimentacion de CadLink");
        t.Start();

        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            var material = Solidos.Anclas(doc);

            foreach (var (zapata, armado, origen) in zapatas)
            {
                var nombre = $"zapata {zapata.Id.Value}";

                try
                {
                    if (armado is null)
                    {
                        r.Errores.Add($"«{nombre}»: No hay fila de armado para ella: {origen}.");
                        continue;
                    }

                    var marco = Planta(zapata);

                    if (marco is null)
                    {
                        r.Errores.Add($"«{nombre}»: No se pudo medir su planta.");
                        continue;
                    }

                    var m = marco.Value;
                    var capas = ArmadoZapata.Repartir(
                        Unidades.AMetros(m.Mayor), Unidades.AMetros(m.Menor), Unidades.AMetros(m.Alto), armado);

                    if (capas.Count == 0)
                    {
                        r.Errores.Add($"«{nombre}»: El armado no cabe, o la fila no trae varillas.");
                        continue;
                    }

                    var llave = LlaveDetalle.De(ClaseDetalle.ArmadoZapata, zapata.Id.Value);

                    if (Borrar(doc, llave) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var nativa = zapata is not DirectShape
                                 && RebarHostData.GetRebarHostData(zapata) is { } datos && datos.IsValidHost();

                    // Una zapata de Revit que no admite armado no se llena de solidos: casi siempre es
                    // un Floor sin la casilla Structural o con un material que no es de concreto, y
                    // eso se arregla en un clic. Se dice y se salta.
                    if (!nativa && zapata is not DirectShape)
                    {
                        r.Errores.Add($"«{nombre}»: No admite armado. Si es un Floor, marca «Structural» en sus "
                                      + "propiedades y revisa que su material sea de clase concreto.");
                        continue;
                    }

                    if (nativa)
                    {
                        foreach (var c in capas)
                        {
                            var tipo = Tipo(tipos, c.DiametroM);

                            if (tipo is null)
                            {
                                r.Errores.Add($"«{nombre}»: No hay tipo de varilla de {c.DiametroM * 1000:0.#} mm en el proyecto.");
                                continue;
                            }

                            Nativa(doc, zapata, m, c, tipo, llave);
                        }
                    }
                    else
                    {
                        var solidos = new List<GeometryObject>();

                        foreach (var c in capas)
                        {
                            foreach (var p in c.Posiciones)
                            {
                                solidos.AddRange(Barra(m, c, Unidades.AInternas(p), material));
                            }
                        }

                        Solidos.Forma(doc, categoria, solidos, llave,
                            $"CadLink: armado de la zapata, {Resumen(armado)}", string.Empty);

                        r.Avisos.Add($"«{nombre}»: Es una zapata de CadLink (DirectShape), que Revit no acepta como anfitrion de armado: "
                                     + "las varillas van como solidos, se ven pero no se cuantifican.");
                    }

                    r.Hechas++;
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo armar. {e.Message}");
                }
            }

            t.Commit();
        }
        catch (Exception e)
        {
            t.RollBack();
            r.Hechas = 0;
            r.Reemplazadas = 0;
            r.Errores.Add("Se deshizo todo el armado de la cimentacion: " + e.Message);
        }

        return r;
    }

    /// <summary>La planta de la zapata: su esquina, sus dos ejes y sus medidas, en unidades internas.</summary>
    /// <param name="Esquina">La esquina de abajo, al fondo de la zapata.</param>
    /// <param name="Ex">A lo largo del lado mayor.</param>
    public readonly record struct PlantaZapata(XYZ Esquina, XYZ Ex, XYZ Ey, double Mayor, double Menor, double Alto);

    public static PlantaZapata? Planta(Element zapata)
    {
        var puntos = Geometria.PuntosDelSolido(zapata);

        if (puntos.Count < 4)
        {
            return null;
        }

        var z0 = puntos.Min(p => p.Z);
        var z1 = puntos.Max(p => p.Z);

        // El lado mayor: la arista horizontal mas larga entre los puntos del fondo.
        var fondo = puntos.Where(p => p.Z - z0 < 1e-4).ToList();
        XYZ? mejor = null;
        var largo = 0.0;

        for (var i = 0; i < fondo.Count; i++)
        {
            for (var j = i + 1; j < fondo.Count; j++)
            {
                var d = fondo[j] - fondo[i];
                var l = d.GetLength();

                if (l > largo)
                {
                    largo = l;
                    mejor = d;
                }
            }
        }

        // La diagonal tambien sale como «arista»: se toman los ejes de la familia si los hay, y si
        // no, se endereza la diagonal con las aristas del fondo que la acompanan.
        var ex = zapata is FamilyInstance fi && fi.HandOrientation.GetLength() > 0.5
            ? new XYZ(fi.HandOrientation.X, fi.HandOrientation.Y, 0).Normalize()
            : EjeDeArista(fondo) ?? (mejor is null ? XYZ.BasisX : new XYZ(mejor.X, mejor.Y, 0).Normalize());

        var ey = XYZ.BasisZ.CrossProduct(ex).Normalize();

        var u0 = puntos.Min(p => p.DotProduct(ex));
        var u1 = puntos.Max(p => p.DotProduct(ex));
        var v0 = puntos.Min(p => p.DotProduct(ey));
        var v1 = puntos.Max(p => p.DotProduct(ey));

        if (v1 - v0 > u1 - u0)
        {
            // El lado mayor es el de Y: se cambian los ejes para que Ex vaya a lo largo de el.
            (ex, ey) = (ey, ex.Negate());
            (u0, u1, v0, v1) = (puntos.Min(p => p.DotProduct(ex)), puntos.Max(p => p.DotProduct(ex)),
                                puntos.Min(p => p.DotProduct(ey)), puntos.Max(p => p.DotProduct(ey)));
        }

        var esquina = (ex * u0) + (ey * v0) + new XYZ(0, 0, z0);

        return new PlantaZapata(esquina, ex, ey, u1 - u0, v1 - v0, z1 - z0);
    }

    /// <summary>
    /// La direccion de un lado del fondo: de los pares de puntos, el que deja a todos los demas de un
    /// solo lado -una arista del contorno, no una diagonal-.
    /// </summary>
    private static XYZ? EjeDeArista(List<XYZ> fondo)
    {
        XYZ? mejor = null;
        var largo = 0.0;

        for (var i = 0; i < fondo.Count; i++)
        {
            for (var j = i + 1; j < fondo.Count; j++)
            {
                var d = new XYZ(fondo[j].X - fondo[i].X, fondo[j].Y - fondo[i].Y, 0);
                var l = d.GetLength();

                if (l < 1e-6 || l <= largo)
                {
                    continue;
                }

                var n = XYZ.BasisZ.CrossProduct(d).Normalize();
                var lados = fondo.Select(p => (p - fondo[i]).DotProduct(n)).ToList();

                if (lados.All(x => x > -1e-6) || lados.All(x => x < 1e-6))
                {
                    largo = l;
                    mejor = d.Normalize();
                }
            }
        }

        return mejor;
    }

    /// <summary>El eje de una varilla: pata, tramo y pata, en el plano vertical de su direccion.</summary>
    private static List<XYZ> Eje(PlantaZapata m, CapaRepartida c, double posicion)
    {
        var (dir, otra) = c.Larga ? (m.Ex, m.Ey) : (m.Ey, m.Ex);
        var z = new XYZ(0, 0, Unidades.AInternas(c.Z));
        var a = m.Esquina + (otra * posicion) + (dir * Unidades.AInternas(c.Desde)) + z;
        var b = m.Esquina + (otra * posicion) + (dir * Unidades.AInternas(c.Hasta)) + z;

        if (c.Pata <= 0)
        {
            return new List<XYZ> { a, b };
        }

        var pata = new XYZ(0, 0, Unidades.AInternas(c.Pata) * (c.PataArriba ? 1 : -1));

        return new List<XYZ> { a + pata, a, b, b + pata };
    }

    /// <summary>Una capa entera como varillas nativas: la primera y su reparto a lo ancho.</summary>
    private static void Nativa(Document doc, Element host, PlantaZapata m, CapaRepartida c, RebarBarType tipo, string llave)
    {
        var puntos = Eje(m, c, Unidades.AInternas(c.Posiciones[0]));
        var curvas = new List<Curve>();

        for (var i = 1; i < puntos.Count; i++)
        {
            curvas.Add(Line.CreateBound(puntos[i - 1], puntos[i]));
        }

        // La normal es la direccion del reparto: hacia ahi se copian las demas varillas.
        var normal = c.Larga ? m.Ey : m.Ex;

        // POSICIONALES, como todas las llamadas a la Revit API del complemento (validar.py §26).
        var varilla = Rebar.CreateFromCurves(
            doc, RebarStyle.Standard, tipo, null, null, host, normal, curvas,
            RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);

        if (varilla is null)
        {
            return;
        }

        var n = c.Posiciones.Count;

        if (n > 1)
        {
            var paso = Unidades.AInternas((c.Posiciones[n - 1] - c.Posiciones[0]) / (n - 1));
            varilla.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(n, paso, true, true, true);
        }

        varilla.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(llave);
    }

    /// <summary>Una varilla como solidos: un cilindro por tramo.</summary>
    private static IEnumerable<Solid> Barra(PlantaZapata m, CapaRepartida c, double posicion, ElementId material)
    {
        var puntos = Eje(m, c, posicion);
        var radio = Unidades.AInternas(c.DiametroM / 2);

        for (var i = 1; i < puntos.Count; i++)
        {
            var d = puntos[i] - puntos[i - 1];
            var l = d.GetLength();

            if (l > 1e-6)
            {
                yield return Solidos.Cilindro(puntos[i - 1], d.Normalize(), l, radio, material);
            }
        }
    }

    /// <summary>Los tipos de varilla del proyecto con su diametro, en metros.</summary>
    public static List<(RebarBarType Tipo, double DiametroM)> TiposDeVarilla(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).OfType<RebarBarType>()
            .Select(t => (t, Unidades.AMetros(t.BarNominalDiameter)))
            .ToList();

    /// <summary>El tipo de varilla de ese diametro, con un milimetro de holgura.</summary>
    private static RebarBarType? Tipo(List<(RebarBarType Tipo, double DiametroM)> tipos, double diametroM) =>
        tipos.Where(x => Math.Abs(x.DiametroM - diametroM) <= 0.001)
            .OrderBy(x => Math.Abs(x.DiametroM - diametroM))
            .Select(x => x.Tipo)
            .FirstOrDefault();

    /// <summary>Borra el armado que CadLink ya le habia puesto: varillas nativas y solidos.</summary>
    private static int Borrar(Document doc, string llave)
    {
        var ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == llave)
            .Select(e => e.Id)
            .ToList();

        if (ids.Count > 0)
        {
            doc.Delete(ids);
        }

        return ids.Count + Solidos.Borrar(doc, llave);
    }

    public static string Resumen(ArmadoZapataJson a)
    {
        string Capa(CapaZapataJson? c) => c is null || c.SeparacionM <= 0
            ? "-"
            : $"{(c.Varilla.Length > 0 ? c.Varilla : $"Ø{c.DiametroM * 1000:0.#} mm")} @ {c.SeparacionM * 100:0.#} cm";

        var abajo = $"abajo {Capa(a.InfLargas)} largas y {Capa(a.InfCortas)} cortas";

        return a.SupLargas is null && a.SupCortas is null
            ? abajo
            : abajo + $"; arriba {Capa(a.SupLargas)} largas y {Capa(a.SupCortas)} cortas";
    }
}
