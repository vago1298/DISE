using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Lo que se pidio para el muro con malla.</summary>
/// <param name="GruesoDibujoM">
/// Con que grueso se DIBUJA el alambre. El real (3 a 6 mm) no se ve en un isometrico de un muro entero:
/// sale como una raya de un pixel. Para el detalle se dibuja mas grueso; cero = el real.
/// </param>
public sealed record OpcionesMalla(TipoDeMalla Malla, bool DosCaras, AplanadoDeMalla Aplanado, double AplanadoM, bool Isometrico,
    double GruesoDibujoM = 0.012);

/// <summary>
/// Pone la MALLA ELECTROSOLDADA y su aplanado en cualquier muro -tambien de tabique, que Revit no
/// acepta como anfitrion de una Fabric Sheet- y hace el isometrico de ese muro solo.
/// </summary>
/// <remarks>
/// <para>
/// La malla se modela como solidos (un cilindro por alambre) y el aplanado como una placa delgada
/// pegada a la cara, roto en diagonal para que en el detalle se vea el tabique, la malla y el
/// aplanado. Se ven en 3D y en cortes; no se cuantifican como armado. Volver a ponerla en el mismo muro
/// la reemplaza.
/// </para>
/// <para>
/// El isometrico es una vista 3D nueva con la caja de corte al muro, en Realista y a detalle fino, con
/// el muro, su malla y su aplanado aislados. Mira a la cara donde va la malla.
/// </para>
/// </remarks>
internal static class MuroConMalla
{
    public static (ResultadoDetalles R, View3D? Vista) Poner(Document doc, IReadOnlyList<Wall> muros, OpcionesMalla o)
    {
        var r = new ResultadoDetalles();
        View3D? vista = null;
        var aislar = new List<ElementId>();
        BoundingBoxXYZ? total = null;
        XYZ? mira = null;
        XYZ? alo = null;

        using var t = new Transaction(doc, "Muro con malla de CadLink");
        t.Start();

        try
        {
            // DENTRO de la transaccion: si el material no existe se crea, y crear algo fuera de una
            // transaccion es una excepcion que tumbaba el comando entero («Command Failure»).
            var categoria = Solidos.Categoria(doc, BuiltInCategory.OST_GenericModel);
            // Gris claro galvanizado: sobre el tabique rojo y el aplanado gris se distingue.
            var acero = Solidos.MaterialDe(doc, "CadLink - Malla", 225, 228, 232, "galvaniz", "malla");
            var mortero = Solidos.MaterialDe(doc, "CadLink - Aplanado", 186, 184, 176, "aplanado", "mortero", "plaster", "repello");

            foreach (var muro in muros)
            {
                var nombre = $"muro {muro.Id.Value}";

                try
                {
                    if (muro.Location is not LocationCurve lc || lc.Curve is not Line linea)
                    {
                        r.Errores.Add($"«{nombre}»: Muro curvo. Se ponen mallas en muros rectos.");
                        continue;
                    }

                    var p0 = linea.GetEndPoint(0);
                    var p1 = linea.GetEndPoint(1);
                    var dir = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0).Normalize();
                    var normal = XYZ.BasisZ.CrossProduct(dir).Normalize();
                    var puntos = Geometria.PuntosDelSolido(muro);

                    if (puntos.Count < 4)
                    {
                        r.Errores.Add($"«{nombre}»: No se pudo medir el muro.");
                        continue;
                    }

                    // Del SOLIDO: de donde a donde va, sus dos caras y su altura.
                    var s = puntos.Select(q => (q - p0).DotProduct(dir)).ToList();
                    var e = puntos.Select(q => (q - p0).DotProduct(normal)).ToList();
                    var z0 = puntos.Min(q => q.Z);
                    var z1 = puntos.Max(q => q.Z);
                    var largo = s.Max() - s.Min();
                    var alto = z1 - z0;
                    var esquina = new XYZ(p0.X, p0.Y, z0) + (dir * s.Min());

                    var llave = LlaveDetalle.De(ClaseDetalle.MallaMuro, muro.Id.Value);

                    if (Solidos.Borrar(doc, llave) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var solidosMalla = new List<GeometryObject>();
                    var solidosAplanado = new List<GeometryObject>();
                    var (vs, hs) = MallaDeMuro.Alambres(Unidades.AMetros(largo), Unidades.AMetros(alto), o.Malla);
                    var d = Unidades.AInternas(Math.Max(o.Malla.AlambreM, o.GruesoDibujoM));
                    var radio = d / 2;
                    var aplanado = Unidades.AInternas(Math.Max(0.005, o.AplanadoM));

                    // Cada cara: su posicion sobre la normal y hacia donde es "afuera".
                    var caras = new List<(double Cara, double Fuera)> { (e.Max(), 1) };

                    if (o.DosCaras)
                    {
                        caras.Add((e.Min(), -1));
                    }

                    double Ms(double interno) => Unidades.AMetros(interno);

                    // La malla va DENTRO del aplanado: si el alambre dibujado no cabe en la capa (dos
                    // alambres cruzados), se adelgaza para que no asome por fuera del aplanado.
                    if (o.Aplanado != AplanadoDeMalla.Ninguno && 2 * d > aplanado * 0.8)
                    {
                        d = aplanado * 0.4;
                        radio = d / 2;
                    }

                    foreach (var (cara, fuera) in caras)
                    {
                        XYZ En(double sM, double zM, double sobre) =>
                            esquina + (dir * Unidades.AInternas(sM)) + new XYZ(0, 0, Unidades.AInternas(zM))
                            + (normal * (cara + (fuera * sobre)));

                        // LA FORMA DE LA CARA: sus contornos en (S, Z), con los huecos. Sin ella, el
                        // rectangulo del muro, como antes.
                        var lazos = ContornosDeLaCara(muro, normal * fuera, p => (Ms((p - esquina).DotProduct(dir)), Ms(p.Z - esquina.Z)));

                        if (lazos.Count == 0)
                        {
                            lazos.Add(MallaDeMuro.AplanadoCompleto(Ms(largo), Ms(alto)));
                        }

                        // A media capa de aplanado; sin aplanado, pegada a la cara.
                        var centro = o.Aplanado == AplanadoDeMalla.Ninguno ? d : aplanado / 2;
                        var lazosLectura = lazos.Select(l => (IReadOnlyList<(double S, double Z)>)l).ToList();

                        foreach (var x in vs)
                        {
                            foreach (var (z0m, z1m) in MallaDeMuro.DentroDeLaCara(lazosLectura, x, true, MallaDeMuro.LibreM))
                            {
                                solidosMalla.Add(Solidos.Cilindro(En(x, z0m, centro - radio), XYZ.BasisZ,
                                    Unidades.AInternas(z1m - z0m), radio, acero));
                            }
                        }

                        foreach (var z in hs)
                        {
                            // Los horizontales del otro lado de los verticales, tocandolos.
                            foreach (var (s0m, s1m) in MallaDeMuro.DentroDeLaCara(lazosLectura, z, false, MallaDeMuro.LibreM))
                            {
                                solidosMalla.Add(Solidos.Cilindro(En(s0m, z, centro + radio), dir,
                                    Unidades.AInternas(s1m - s0m), radio, acero));
                            }
                        }

                        if (o.Aplanado != AplanadoDeMalla.Ninguno)
                        {
                            var opciones = new SolidOptions(mortero, ElementId.InvalidElementId);

                            // El aplanado con la forma de la cara (y sus huecos)...
                            Solid? capa = null;

                            try
                            {
                                capa = GeometryCreationUtilities.CreateExtrusionGeometry(
                                    lazos.Select(l => Solidos.Poligono(l.Select(q => En(q.S, q.Z, 0)).ToList())).ToList(),
                                    normal * fuera, aplanado, opciones);
                            }
                            catch (Exception)
                            {
                                capa = null;
                            }

                            capa ??= GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { Solidos.Poligono(MallaDeMuro.AplanadoCompleto(Ms(largo), Ms(alto)).Select(q => En(q.S, q.Z, 0)).ToList()) },
                                normal * fuera, aplanado, opciones);

                            // ...y roto: lo que queda dentro de la orilla quebrada.
                            if (o.Aplanado == AplanadoDeMalla.Roto)
                            {
                                var roto = GeometryCreationUtilities.CreateExtrusionGeometry(
                                    new List<CurveLoop> { Solidos.Poligono(MallaDeMuro.AplanadoRoto(Ms(largo), Ms(alto)).Select(q => En(q.S, q.Z, -aplanado)).ToList()) },
                                    normal * fuera, aplanado * 3, opciones);

                                try
                                {
                                    capa = BooleanOperationsUtils.ExecuteBooleanOperation(capa, roto, BooleanOperationsType.Intersect) ?? roto;
                                }
                                catch (Exception)
                                {
                                    capa = roto;
                                }
                            }

                            solidosAplanado.Add(capa);
                        }
                    }

                    var malla = Solidos.Forma(doc, categoria, solidosMalla, llave + "|malla",
                        $"CadLink: malla electrosoldada {o.Malla.Nombre}", string.Empty);
                    aislar.Add(malla.Id);

                    if (solidosAplanado.Count > 0)
                    {
                        var ap = Solidos.Forma(doc, categoria, solidosAplanado, llave + "|aplanado",
                            $"CadLink: aplanado de {o.AplanadoM * 100:0.#} cm sobre la malla", string.Empty);
                        aislar.Add(ap.Id);
                    }

                    aislar.Add(muro.Id);

                    // La caja de todo, para el isometrico, y desde donde se mira: la cara de la malla.
                    // Con los CASTILLOS y las CADENAS que lo confinan: que el detalle se vea completo.
                    var bb = muro.get_BoundingBox(null);

                    void Sumar(BoundingBoxXYZ c)
                    {
                        total ??= new BoundingBoxXYZ { Min = c.Min, Max = c.Max };
                        total.Min = new XYZ(Math.Min(total.Min.X, c.Min.X), Math.Min(total.Min.Y, c.Min.Y), Math.Min(total.Min.Z, c.Min.Z));
                        total.Max = new XYZ(Math.Max(total.Max.X, c.Max.X), Math.Max(total.Max.Y, c.Max.Y), Math.Max(total.Max.Z, c.Max.Z));
                    }

                    if (bb is not null)
                    {
                        Sumar(bb);

                        foreach (var c in Confinamiento(doc, bb))
                        {
                            if (!aislar.Contains(c.Id) && c.get_BoundingBox(null) is { } cb)
                            {
                                aislar.Add(c.Id);
                                Sumar(cb);
                            }
                        }
                    }

                    mira ??= normal;
                    alo ??= dir;

                    r.Avisos.Add($"«{nombre}»: malla {o.Malla.Nombre} ({vs.Count} verticales y {hs.Count} horizontales"
                                 + (o.DosCaras ? " por cara, en las dos caras" : "") + ")"
                                 + (o.GruesoDibujoM > o.Malla.AlambreM ? $", alambre dibujado de {o.GruesoDibujoM * 1000:0.#} mm (real {o.Malla.AlambreM * 1000:0.##} mm)" : string.Empty)
                                 + (o.Aplanado == AplanadoDeMalla.Ninguno ? "." : $" con aplanado de {o.AplanadoM * 100:0.#} cm."));
                    r.Hechas++;
                }
                catch (Exception ex)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo poner la malla. {ex.Message}");
                }
            }

            if (o.Isometrico && total is not null && mira is not null && alo is not null)
            {
                vista = Isometrico(doc, total, mira, alo, aislar, muros.Count == 1 ? muros[0].Id.Value : 0, r);
            }

            t.Commit();
        }
        catch (Exception ex)
        {
            t.RollBack();
            r.Hechas = 0;
            r.Errores.Add("Se deshizo todo: " + ex.Message);
            vista = null;
        }

        return (r, vista);
    }

    /// <summary>
    /// Los castillos (pilares) y las cadenas y trabes (armazon) que tocan el muro: su caja cruza la del
    /// muro, con 5 cm de holgura.
    /// </summary>
    private static List<Element> Confinamiento(Document doc, BoundingBoxXYZ muro)
    {
        var h = Unidades.AInternas(0.05);

        bool Toca(BoundingBoxXYZ c) =>
            c.Min.X <= muro.Max.X + h && c.Max.X >= muro.Min.X - h
            && c.Min.Y <= muro.Max.Y + h && c.Max.Y >= muro.Min.Y - h
            && c.Min.Z <= muro.Max.Z + h && c.Max.Z >= muro.Min.Z - h;

        return new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.Category?.BuiltInCategory is BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_StructuralFraming)
            .Where(e => e.get_BoundingBox(null) is { } c && Toca(c))
            .ToList();
    }

    /// <summary>
    /// Los contornos de la cara del muro que mira hacia <paramref name="haciaFuera"/>, ya en (S, Z) del
    /// muro: el de fuera y los de sus huecos. Vacio si no se encuentra.
    /// </summary>
    private static List<List<(double S, double Z)>> ContornosDeLaCara(
        Wall muro, XYZ haciaFuera, Func<XYZ, (double S, double Z)> enElMuro)
    {
        var res = new List<List<(double S, double Z)>>();

        try
        {
            var geo = muro.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });

            if (geo is null)
            {
                return res;
            }

            PlanarFace? mejor = null;
            var lejos = double.MinValue;

            foreach (var o in geo)
            {
                if (o is not Solid solido || solido.Volume <= 1e-9)
                {
                    continue;
                }

                foreach (var f in solido.Faces)
                {
                    if (f is PlanarFace pf && pf.FaceNormal.DotProduct(haciaFuera) > 0.99)
                    {
                        var d = pf.Origin.DotProduct(haciaFuera);

                        if (d > lejos)
                        {
                            lejos = d;
                            mejor = pf;
                        }
                    }
                }
            }

            if (mejor is null)
            {
                return res;
            }

            foreach (var lazo in mejor.GetEdgesAsCurveLoops())
            {
                var puntos = new List<(double S, double Z)>();

                foreach (var c in lazo)
                {
                    var t = c.Tessellate();

                    // El ultimo punto de cada curva es el primero de la siguiente.
                    for (var i = 0; i < t.Count - 1; i++)
                    {
                        puntos.Add(enElMuro(t[i]));
                    }
                }

                if (puntos.Count >= 3)
                {
                    res.Add(puntos);
                }
            }
        }
        catch (Exception)
        {
            res.Clear();
        }

        return res;
    }

    /// <summary>Una vista 3D solo del muro: caja de corte, Realista, detalle fino y lo demas oculto.</summary>
    private static View3D? Isometrico(
        Document doc, BoundingBoxXYZ caja, XYZ mira, XYZ alo, List<ElementId> aislar, long idMuro, ResultadoDetalles r)
    {
        var tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).OfType<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

        if (tipo is null)
        {
            r.Errores.Add("«isometrico»: el proyecto no tiene tipo de vista 3D.");
            return null;
        }

        var vista = View3D.CreateIsometric(doc, tipo.Id);
        var enUso = new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v => v.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        vista.Name = PlanDespiece.NombreLibre(idMuro > 0 ? $"Isometrico muro con malla {idMuro}" : "Isometrico muros con malla", enUso);
        vista.DetailLevel = ViewDetailLevel.Fine;

        try
        {
            vista.DisplayStyle = DisplayStyle.Realistic;
        }
        catch (Exception)
        {
            // Se queda el estilo de la plantilla.
        }

        var aire = Unidades.AInternas(0.30);
        vista.SetSectionBox(new BoundingBoxXYZ
        {
            Min = new XYZ(caja.Min.X - aire, caja.Min.Y - aire, caja.Min.Z - aire),
            Max = new XYZ(caja.Max.X + aire, caja.Max.Y + aire, caja.Max.Z + aire)
        });

        // Mirando a la cara de la malla, de lado y desde arriba, como en un isometrico.
        var centro = (caja.Min + caja.Max) * 0.5;
        var adelante = ((mira * -1) + (alo * 0.6) + (XYZ.BasisZ * -0.45)).Normalize();
        var arriba = (XYZ.BasisZ - (adelante * XYZ.BasisZ.DotProduct(adelante))).Normalize();
        var ojo = centro - (adelante * Unidades.AInternas(30));

        try
        {
            vista.SetOrientation(new ViewOrientation3D(ojo, arriba, adelante));
        }
        catch (Exception)
        {
            // Se queda la orientacion de omision.
        }

        try
        {
            vista.IsolateElementsTemporary(aislar);
            vista.ConvertTemporaryHideIsolateToPermanent();
        }
        catch (Exception)
        {
            // Con la caja de corte basta.
        }

        return vista;
    }
}
