using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>El armado de un tablero, ya en metros: el de cada sentido del proyecto (X y Y).</summary>
public sealed record ArmadoDeTablero(
    bool? Cimentacion, double RecInfM, double RecSupM,
    RebarBarType VarX, double SepXM, RebarBarType VarY, double SepYM, RebarBarType? Baston, string Origen);

/// <summary>
/// Arma las losas elegidas -de entrepiso o de cimentacion- con <b>corridas, bayonetas y bastones a L/4</b>
/// del claro libre, cada TABLERO por separado y con varillas nativas.
/// </summary>
/// <remarks>
/// <para>
/// Cada losa de Revit es un tablero. Su planta se mide del solido; en cada orilla se mira que hay:
/// <b>otra losa</b> a su misma cota del otro lado de una trabe (orilla CONTINUA: bayoneta y baston pasan
/// L/4 al vecino), o la trabe o el muro de <b>borde</b> que la losa cubre (la varilla se ancla con pata
/// de 90° en la orilla menos el recubrimiento). El claro libre es de paño a paño de los apoyos.
/// </para>
/// <para>
/// En una orilla continua entre dos losas que se arman juntas, el baston lo pone UNA (la del lado
/// izquierdo o de abajo), de L/4 de un claro a L/4 del otro, para que no salga doble.
/// </para>
/// <para>
/// Las varillas cortas van por fuera (la cama de abajo y la de arriba); las largas, por dentro. Volver a
/// armar una losa reemplaza su armado de CadLink, pero solo si salio el nuevo.
/// </para>
/// </remarks>
internal static class ArmadorDeLosas
{
    public static ResultadoDetalles Armar(Document doc, IReadOnlyList<(Element Losa, ArmadoDeTablero? Armado)> losas)
    {
        var r = new ResultadoDetalles();

        // Todas las losas y los apoyos del proyecto, una vez: con ellos se sabe que hay en cada orilla.
        var todas = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.Category?.BuiltInCategory is BuiltInCategory.OST_Floors or BuiltInCategory.OST_StructuralFoundation)
            .ToList();
        var apoyos = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.Category?.BuiltInCategory is BuiltInCategory.OST_StructuralFraming or BuiltInCategory.OST_Walls)
            .ToList();
        var elegidas = losas.Select(l => l.Losa.Id.Value).ToHashSet();

        using var t = new Transaction(doc, "Armado de losas de CadLink");
        ElementId categoria = ElementId.InvalidElementId;
        ElementId material = ElementId.InvalidElementId;
        t.Start();

        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            categoria = Solidos.Categoria(doc, BuiltInCategory.OST_Rebar, BuiltInCategory.OST_GenericModel);
            material = Solidos.Anclas(doc);

            foreach (var (losa, armado) in losas)
            {
                var nombre = $"losa {losa.Id.Value}";

                try
                {
                    if (armado is null)
                    {
                        r.Errores.Add($"«{nombre}»: No hay fila de armado para ella.");
                        continue;
                    }

                    // ¿REVIT LA ACEPTA COMO ANFITRION? Primero se marca Structural por si acaso. Si aun asi
                    // no -pasa con losas de entrepiso estructurales y de concreto: divididas en partes, de un
                    // tipo cuya capa de estructura no es la que Revit toma, etc.- YA NO SE SALTA: el armado
                    // se modela como solidos, que se ven en 3D y en los cortes, y se dice.
                    var nativa = EsAnfitrion(losa);

                    if (!nativa)
                    {
                        try
                        {
                            var p = losa.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);

                            if (p is not null && !p.IsReadOnly && p.AsInteger() == 0)
                            {
                                p.Set(1);
                                doc.Regenerate();
                                nativa = EsAnfitrion(losa);
                            }
                        }
                        catch (Exception)
                        {
                            // Se sigue con solidos.
                        }
                    }

                    var solidos = new List<GeometryObject>();

                    if (ArmadorDeZapatas.Planta(losa) is not { } m)
                    {
                        r.Errores.Add($"«{nombre}»: No se pudo medir su planta.");
                        continue;
                    }

                    // EL ESPESOR, DEL PARAMETRO DE LA LOSA. Medido de las aristas del solido salia 0 en
                    // las losas del usuario («con 0 cm de espesor no caben las dos camas»): la planta se
                    // lee bien de ahi, pero la altura no. Se toma «Thickness» y, si no lo trae, la caja.
                    m = ConSuEspesor(losa, m);

                    var cimentacion = armado.Cimentacion
                                      ?? losa.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation;
                    var llave = LlaveDetalle.De(ClaseDetalle.ArmadoLosa, losa.Id.Value);
                    var viejas = Suyas(doc, llave);
                    var hechas = 0;
                    var notas = new List<string>();

                    // Ex es el lado mayor y Ey el menor. Las varillas del CORTO (que corren en Ey) van por fuera.
                    var largo = Unidades.AMetros(m.Mayor);
                    var ancho = Unidades.AMetros(m.Menor);
                    var h = Unidades.AMetros(m.Alto);

                    // Cada sentido del proyecto con su fila: X es el que mas se parece al eje X de Revit.
                    var exEsX = Math.Abs(m.Ex.X) >= Math.Abs(m.Ex.Y);
                    var (varLargo, sepLargo) = exEsX ? (armado.VarX, armado.SepXM) : (armado.VarY, armado.SepYM);
                    var (varCorto, sepCorto) = exEsX ? (armado.VarY, armado.SepYM) : (armado.VarX, armado.SepXM);
                    var dL = varLargo.BarNominalDiameter;
                    var dC = varCorto.BarNominalDiameter;
                    var dLm = Unidades.AMetros(dL);
                    var dCm = Unidades.AMetros(dC);

                    foreach (var corto in new[] { true, false })
                    {
                        var tipo = corto ? varCorto : varLargo;
                        var sep = corto ? sepCorto : sepLargo;
                        var d = corto ? dCm : dLm;
                        var otra = corto ? 0 : dCm;

                        // El marco de la franja: U a lo largo de la varilla, V a lo ancho.
                        var (dirU, dirV, lU, lV) = corto ? (m.Ey, m.Ex, ancho, largo) : (m.Ex, m.Ey, largo, ancho);
                        var origen = m.Esquina;

                        // Las camas, por el eje de la varilla.
                        var zInf = armado.RecInfM + otra + (d / 2);
                        var zSup = h - armado.RecSupM - otra - (d / 2);

                        if (zSup - zInf < d)
                        {
                            r.Errores.Add($"«{nombre}»: con {h * 100:0.#} cm de espesor y {armado.RecInfM * 100:0.#} + "
                                          + $"{armado.RecSupM * 100:0.#} cm de recubrimiento no caben las dos camas.");
                            continue;
                        }

                        var (zCentro, zApoyo) = cimentacion ? (zSup, zInf) : (zInf, zSup);

                        // La propia, con sus medidas de verdad; las demas, con su caja.
                        var yo = new CajaPlanta(0, lU, 0, lV, 0, h);

                        var cajasLosas = todas.Where(e => e.Id != losa.Id)
                            .Select(e => (E: e, C: Caja(e, origen, dirU, dirV)))
                            .Where(x => x.C is not null)
                            .ToList();
                        var cajasApoyos = apoyos.Select(e => Caja(e, origen, dirU, dirV)).OfType<CajaPlanta>().ToList();
                        var listaLosas = cajasLosas.Select(x => x.C!).ToList();

                        ApoyoLosa Orilla(bool inicio, out string que)
                        {
                            if (ArmadoDeLosas.Vecino(yo, listaLosas, inicio) is { } v)
                            {
                                var vecinaElegida = elegidas.Contains(cajasLosas[v.Indice].E.Id.Value);
                                que = $"continua ({v.Holgura * 100:0} cm de trabe)";

                                // Entre dos que se arman juntas, el baston lo pone la de la orilla de FIN.
                                return new ApoyoLosa(true, v.Holgura, v.ClaroVecino, !(vecinaElegida && inicio));
                            }

                            var b = ArmadoDeLosas.ApoyoDeBorde(yo, cajasApoyos, inicio) ?? 0;
                            que = b > 0 ? $"borde (apoyo de {b * 100:0} cm)" : "borde libre";
                            return new ApoyoLosa(false, b);
                        }

                        var ini = Orilla(true, out var queIni);
                        var fin = Orilla(false, out var queFin);

                        // Lo que hay de paño a paño: en un borde la losa cubre su apoyo; en un continuo ya
                        // llega a su paño.
                        var pIni = ini.Continuo ? 0 : ini.AnchoM;
                        var ln = lU - pIni - (fin.Continuo ? 0 : fin.AnchoM);

                        var dBaston = armado.Baston is { } vb ? Unidades.AMetros(vb.BarNominalDiameter) : d;
                        var varillas = ArmadoDeLosas.Franja(ln, ini, fin, zCentro, zApoyo, d, dBaston,
                            Math.Max(armado.RecInfM, armado.RecSupM));

                        notas.Add($"{(corto ? "corto" : "largo")}: Ln = {ln:0.00} m, L/4 = {ln / 4:0.00} m, "
                                  + $"{queIni} / {queFin}, {tipo.Name} @ {sep * 200:0.#} cm corridas y bayonetas");

                        foreach (var v in varillas)
                        {
                            var vTipo = v.Que.StartsWith("baston", StringComparison.Ordinal) ? armado.Baston ?? tipo : tipo;
                            var (primera, cuantas) = ArmadoDeLosas.Reparto(lV, armado.RecInfM, v.DiamM, sep, v.Desfase);

                            if (cuantas == 0)
                            {
                                continue;
                            }

                            XYZ P(double s, double z) =>
                                origen + (dirU * Unidades.AInternas(pIni + s)) + (dirV * Unidades.AInternas(primera))
                                + new XYZ(0, 0, Unidades.AInternas(z));

                            var curvas = new List<Curve>();

                            for (var i = 1; i < v.Eje.Count; i++)
                            {
                                var a = P(v.Eje[i - 1].S, v.Eje[i - 1].Z);
                                var b = P(v.Eje[i].S, v.Eje[i].Z);

                                if (a.DistanceTo(b) > Unidades.AInternas(0.005))
                                {
                                    curvas.Add(Line.CreateBound(a, b));
                                }
                            }

                            if (curvas.Count == 0)
                            {
                                continue;
                            }

                            if (!nativa)
                            {
                                // Como solidos: un cilindro por tramo y por cada posicion del reparto.
                                for (var k = 0; k < cuantas; k++)
                                {
                                    var corre = dirV * Unidades.AInternas(2 * sep * k);

                                    foreach (var c in curvas)
                                    {
                                        var a = c.GetEndPoint(0) + corre;
                                        var b = c.GetEndPoint(1) + corre;
                                        var largoC = a.DistanceTo(b);

                                        if (largoC > 1e-6)
                                        {
                                            solidos.Add(Solidos.Cilindro(a, (b - a).Normalize(), largoC,
                                                Unidades.AInternas(v.DiamM / 2), material));
                                        }
                                    }
                                }

                                hechas++;
                                continue;
                            }

                            try
                            {
                                // POSICIONALES, como todas las llamadas a la Revit API del complemento.
                                var varilla = Rebar.CreateFromCurves(
                                    doc, RebarStyle.Standard, vTipo, null, null, losa, dirV, curvas,
                                    RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);

                                if (varilla is null)
                                {
                                    continue;
                                }

                                if (cuantas > 1)
                                {
                                    varilla.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(
                                        cuantas, Unidades.AInternas(2 * sep), true, true, true);
                                }

                                varilla.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(llave);
                                hechas++;
                            }
                            catch (Exception e)
                            {
                                r.Errores.Add($"«{nombre}»: {v.Que} en el sentido {(corto ? "corto" : "largo")}: {e.Message}");
                            }
                        }
                    }

                    if (hechas == 0)
                    {
                        r.Errores.Add($"«{nombre}»: No salio ninguna varilla"
                                      + (viejas.Count > 0 ? "; se dejo el armado que tenia." : "."));
                        continue;
                    }

                    var habia = Solidos.Borrar(doc, llave);

                    if (viejas.Count > 0)
                    {
                        doc.Delete(viejas);
                    }

                    if (viejas.Count + habia > 0)
                    {
                        r.Reemplazadas++;
                    }

                    if (!nativa)
                    {
                        Solidos.Forma(doc, categoria, solidos, llave, "CadLink: armado de la losa (solidos)", string.Empty);
                        r.Avisos.Add($"«{nombre}»: Revit no la acepta como anfitrion de armado aunque sea estructural; "
                                     + "el armado va como SOLIDOS: se ve en 3D y en cortes, pero no se cuantifica. "
                                     + "Suele pasar si la losa esta dividida en partes (Parts) o si su tipo no tiene la "
                                     + "capa de estructura en el nucleo (Core Boundary).");
                    }

                    r.Avisos.Add($"«{nombre}»: losa de {(cimentacion ? "CIMENTACION" : "ENTREPISO")} ({armado.Origen}). "
                                 + string.Join("; ", notas) + ".");
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
            r.Errores.Add("Se deshizo todo el armado de las losas: " + e.Message);
        }

        return r;
    }

    /// <summary>
    /// La planta con el espesor de verdad: el parametro Thickness del Floor; si no, la altura de su caja;
    /// si no, lo medido. La cara de abajo se pone a ese espesor bajo la cara de arriba, que es la del nivel.
    /// </summary>
    private static ArmadorDeZapatas.PlantaZapata ConSuEspesor(Element losa, ArmadorDeZapatas.PlantaZapata m)
    {
        var minimo = Unidades.AInternas(0.03);
        var bb = losa.get_BoundingBox(null);
        var arriba = bb is not null ? bb.Max.Z : m.Esquina.Z + m.Alto;
        double espesor;

        try
        {
            espesor = losa.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0;
        }
        catch (Exception)
        {
            espesor = 0;
        }

        if (espesor < minimo && m.Alto >= minimo)
        {
            espesor = m.Alto;
        }

        if (espesor < minimo && bb is not null)
        {
            espesor = bb.Max.Z - bb.Min.Z;
        }

        if (espesor < minimo)
        {
            return m;
        }

        return m with { Alto = espesor, Esquina = new XYZ(m.Esquina.X, m.Esquina.Y, arriba - espesor) };
    }

    private static bool EsAnfitrion(Element losa)
    {
        try
        {
            return RebarHostData.GetRebarHostData(losa) is { } d && d.IsValidHost();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Las varillas que CadLink ya le habia puesto a ESTA losa.</summary>
    private static List<ElementId> Suyas(Document doc, string llave) =>
        new FilteredElementCollector(doc).OfClass(typeof(Rebar))
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == llave)
            .Select(e => e.Id)
            .ToList();

    /// <summary>La caja de una pieza en el marco del tablero, en metros. Con su caja de Revit: basta para orillas rectas.</summary>
    private static CajaPlanta? Caja(Element e, XYZ origen, XYZ dirU, XYZ dirV)
    {
        var bb = e.get_BoundingBox(null);

        if (bb is null)
        {
            return null;
        }

        var us = new List<double>();
        var vs = new List<double>();

        foreach (var x in new[] { bb.Min.X, bb.Max.X })
        {
            foreach (var y in new[] { bb.Min.Y, bb.Max.Y })
            {
                var p = new XYZ(x, y, origen.Z) - origen;
                us.Add(Unidades.AMetros(p.DotProduct(dirU)));
                vs.Add(Unidades.AMetros(p.DotProduct(dirV)));
            }
        }

        return new CajaPlanta(us.Min(), us.Max(), vs.Min(), vs.Max(),
            Unidades.AMetros(bb.Min.Z - origen.Z), Unidades.AMetros(bb.Max.Z - origen.Z));
    }
}
