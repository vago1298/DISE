using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Lo que se pidio para el muro con malla.</summary>
public sealed record OpcionesMalla(TipoDeMalla Malla, bool DosCaras, AplanadoDeMalla Aplanado, double AplanadoM, bool Isometrico);

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
            var acero = Solidos.Anclas(doc);
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
                    var d = Unidades.AInternas(o.Malla.AlambreM);
                    var radio = d / 2;
                    var aplanado = Unidades.AInternas(Math.Max(0.005, o.AplanadoM));

                    // Cada cara: su posicion sobre la normal y hacia donde es "afuera".
                    var caras = new List<(double Cara, double Fuera)> { (e.Max(), 1) };

                    if (o.DosCaras)
                    {
                        caras.Add((e.Min(), -1));
                    }

                    foreach (var (cara, fuera) in caras)
                    {
                        // La malla a media capa de aplanado: separada de la cara un tercio del espesor.
                        var hueco = Math.Max(d, aplanado / 3);
                        XYZ En(double sM, double zM, double sobre) =>
                            esquina + (dir * Unidades.AInternas(sM)) + new XYZ(0, 0, Unidades.AInternas(zM))
                            + (normal * (cara + (fuera * sobre)));

                        foreach (var x in vs)
                        {
                            solidosMalla.Add(Solidos.Cilindro(En(x, MallaDeMuro.LibreM, hueco + radio), XYZ.BasisZ,
                                alto - Unidades.AInternas(2 * MallaDeMuro.LibreM), radio, acero));
                        }

                        foreach (var z in hs)
                        {
                            // Los horizontales del otro lado de los verticales, tocandolos.
                            solidosMalla.Add(Solidos.Cilindro(En(MallaDeMuro.LibreM, z, hueco + radio + d), dir,
                                largo - Unidades.AInternas(2 * MallaDeMuro.LibreM), radio, acero));
                        }

                        if (o.Aplanado != AplanadoDeMalla.Ninguno)
                        {
                            var contorno = (o.Aplanado == AplanadoDeMalla.Roto
                                    ? MallaDeMuro.AplanadoRoto(Unidades.AMetros(largo), Unidades.AMetros(alto))
                                    : MallaDeMuro.AplanadoCompleto(Unidades.AMetros(largo), Unidades.AMetros(alto)))
                                .Select(p => En(p.S, p.Z, 0))
                                .ToList();

                            solidosAplanado.Add(GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { Solidos.Poligono(contorno) }, normal * fuera, aplanado,
                                new SolidOptions(mortero, ElementId.InvalidElementId)));
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
                    var bb = muro.get_BoundingBox(null);

                    if (bb is not null)
                    {
                        total ??= new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
                        total.Min = new XYZ(Math.Min(total.Min.X, bb.Min.X), Math.Min(total.Min.Y, bb.Min.Y), Math.Min(total.Min.Z, bb.Min.Z));
                        total.Max = new XYZ(Math.Max(total.Max.X, bb.Max.X), Math.Max(total.Max.Y, bb.Max.Y), Math.Max(total.Max.Z, bb.Max.Z));
                    }

                    mira ??= normal;
                    alo ??= dir;

                    r.Avisos.Add($"«{nombre}»: malla {o.Malla.Nombre} ({vs.Count} verticales y {hs.Count} horizontales"
                                 + (o.DosCaras ? " por cara, en las dos caras" : "") + ")"
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
