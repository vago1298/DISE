using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// Pone el Area Reinforcement en todos los muros de concreto elegidos: verticales y horizontales,
/// en una o dos caras.
/// </summary>
/// <remarks>
/// <para>
/// Se usa Area Reinforcement y no varillas sueltas a proposito: es lo que se pone a mano, se
/// edita con su contorno, recorta solo en puertas y ventanas, y cada capa se cambia despues desde
/// Propiedades como cualquier otro.
/// </para>
/// <para>
/// El contorno va en el plano del muro, de punta a punta de su linea y de su base a su corona,
/// medidas de la caja del muro. Volver a armar un muro borra el armado que CadLink le habia
/// puesto -lo reconoce por su comentario- y deja el puesto a mano.
/// </para>
/// </remarks>
internal static class ArmadorDeMuros
{
    public static ResultadoDetalles Armar(Document doc, IReadOnlyList<Wall> muros, OpcionesArmadoMuro o)
    {
        var r = new ResultadoDetalles();

        var tipoArea = new FilteredElementCollector(doc).OfClass(typeof(AreaReinforcementType)).FirstElementId();
        var varillas = Varillas(doc);

        if (tipoArea == ElementId.InvalidElementId)
        {
            r.Errores.Add("El proyecto no tiene ningun tipo de Area Reinforcement. Carga la plantilla estructural.");
            return r;
        }

        if (!varillas.TryGetValue(o.VarillaVertical, out var vertical) || !varillas.TryGetValue(o.VarillaHorizontal, out var horizontal))
        {
            r.Errores.Add("No se encontro el tipo de varilla elegido en el proyecto.");
            return r;
        }

        using var t = new Transaction(doc, "Armado de muros de CadLink");
        t.Start();

        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            // Los ganchos se buscan por su nombre -«CadLink 90° - 40d»- y se crean si no estan.
            var inferior = Gancho(doc, o.GanchoInferior);
            var superior = Gancho(doc, o.GanchoSuperior);

            foreach (var muro in muros)
            {
                var nombre = $"muro {muro.Id.Value}";

                try
                {
                    var datos = RebarHostData.GetRebarHostData(muro);

                    if (datos is null || !datos.IsValidHost())
                    {
                        r.Errores.Add($"«{nombre}»: No admite armado. Tiene que ser estructural y de un material de concreto.");
                        continue;
                    }

                    if (muro.Location is not LocationCurve lc || lc.Curve is not Line linea)
                    {
                        r.Errores.Add($"«{nombre}»: Muro curvo. Se arman muros rectos.");
                        continue;
                    }

                    var caja = muro.get_BoundingBox(null);

                    if (caja is null)
                    {
                        r.Errores.Add($"«{nombre}»: Sin geometria.");
                        continue;
                    }

                    var p0 = linea.GetEndPoint(0);
                    var p1 = linea.GetEndPoint(1);
                    var dir = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    var largo = dir.GetLength();
                    dir = dir.Normalize();

                    var esquinas = ArmadoDeMuros.Contorno(
                        Unidades.AMetros(largo), Unidades.AMetros(caja.Min.Z), Unidades.AMetros(caja.Max.Z), o.LibreM);

                    if (esquinas.Count == 0)
                    {
                        r.Errores.Add($"«{nombre}»: Es demasiado chico para armarlo.");
                        continue;
                    }

                    var puntos = esquinas
                        .Select(e => new XYZ(p0.X, p0.Y, 0) + (dir * Unidades.AInternas(e.S)) + new XYZ(0, 0, Unidades.AInternas(e.Z)))
                        .ToList();

                    var curvas = new List<Curve>();

                    for (var i = 0; i < puntos.Count; i++)
                    {
                        curvas.Add(Line.CreateBound(puntos[i], puntos[(i + 1) % puntos.Count]));
                    }

                    var reemplazadas = BorrarLasDeCadLink(doc, muro.Id);

                    // MAJOR VERTICAL: la direccion mayor es la de las barras verticales. POSICIONALES,
                    // como todas las llamadas a la Revit API del complemento (validar.py §26).
                    var area = AreaReinforcement.Create(doc, muro, curvas, XYZ.BasisZ, tipoArea, vertical, inferior);

                    Capa(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_1, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_1,
                        BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_1, true, vertical, o.SeparacionVerticalM);
                    Capa(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_TOP_DIR_2, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_2,
                        BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_2, true, horizontal, o.SeparacionHorizontalM);
                    Capa(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_1, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_1,
                        BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_1, o.DosParrillas, vertical, o.SeparacionVerticalM);
                    Capa(area, BuiltInParameter.REBAR_SYSTEM_ACTIVE_BOTTOM_DIR_2, BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_2,
                        BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_2, o.DosParrillas, horizontal, o.SeparacionHorizontalM);

                    // Las horizontales sin gancho: el gancho elegido es solo de las verticales.
                    area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_TOP_DIR_2)?.Set(ElementId.InvalidElementId);
                    area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_BOTTOM_DIR_2)?.Set(ElementId.InvalidElementId);

                    area.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(ArmadoDeMuros.Llave(muro.Id.Value));

                    // GANCHOS DISTINTOS EN CADA PUNTA: se suelta el Area Reinforcement en varillas y a
                    // cada vertical se le pone el de arriba en su punta de arriba. La de abajo ya trae
                    // el inferior, con la orientacion del sistema -hacia dentro-, y la de arriba copia
                    // esa orientacion.
                    if (!o.MismosGanchos)
                    {
                        doc.Regenerate();
                        var sueltas = AreaReinforcement.RemoveAreaReinforcementSystem(doc, area);
                        var llave = ArmadoDeMuros.Llave(muro.Id.Value);

                        foreach (var id in sueltas)
                        {
                            if (doc.GetElement(id) is not Rebar varilla)
                            {
                                continue;
                            }

                            varilla.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(llave);

                            var eje = varilla.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);

                            if (eje.Count == 0)
                            {
                                continue;
                            }

                            var a = eje[0].GetEndPoint(0);
                            var b = eje[eje.Count - 1].GetEndPoint(1);
                            var enPlanta = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

                            if (!ArmadoDeMuros.EsVertical(b.Z - a.Z, enPlanta))
                            {
                                continue;
                            }

                            var abajo = ArmadoDeMuros.ExtremoDeAbajo(a.Z, b.Z);
                            var arriba = 1 - abajo;

                            varilla.SetHookTypeId(abajo, inferior);
                            varilla.SetHookTypeId(arriba, superior);
                            varilla.SetHookOrientation(arriba, varilla.GetHookOrientation(abajo));
                        }

                        r.Avisos.Add($"«{nombre}»: Lleva ganchos distintos arriba y abajo, asi que va en varillas sueltas "
                                     + "y no como Area Reinforcement: ya no se edita con su contorno.");
                    }

                    if (reemplazadas > 0)
                    {
                        r.Reemplazadas++;
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
            r.Errores.Add("Se deshizo todo el armado de muros: " + e.Message);
        }

        return r;
    }

    private static void Capa(
        Element area, BuiltInParameter activa, BuiltInParameter tipo, BuiltInParameter separacion,
        bool encendida, ElementId varilla, double separacionM)
    {
        area.get_Parameter(activa)?.Set(encendida ? 1 : 0);

        if (encendida)
        {
            area.get_Parameter(tipo)?.Set(varilla);
            area.get_Parameter(separacion)?.Set(Unidades.AInternas(separacionM));
        }
    }

    /// <summary>Los tipos de varilla del proyecto, por nombre.</summary>
    public static Dictionary<string, ElementId> Varillas(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<Element>()
            .GroupBy(e => e.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// El tipo de gancho de Revit para ese angulo y ese largo en diametros: el de CadLink con ese
    /// nombre, o uno nuevo. El largo es el multiplicador de la pata, asi que se ajusta solo a cada
    /// diametro de varilla.
    /// </summary>
    private static ElementId Gancho(Document doc, GanchoMuro g)
    {
        if (g.Ninguno)
        {
            return ElementId.InvalidElementId;
        }

        foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)))
        {
            if (string.Equals(e.Name, g.Nombre, StringComparison.OrdinalIgnoreCase))
            {
                return e.Id;
            }
        }

        var nuevo = RebarHookType.Create(doc, g.AnguloGrados * Math.PI / 180, g.Diametros);
        nuevo.Name = g.Nombre;
        nuevo.Style = RebarStyle.Standard;

        return nuevo.Id;
    }

    /// <summary>Borra el Area Reinforcement que CadLink ya le habia puesto a este muro.</summary>
    private static int BorrarLasDeCadLink(Document doc, ElementId muro)
    {
        var llave = ArmadoDeMuros.Llave(muro.Value);

        // El Area Reinforcement y, si se solto en varillas por llevar ganchos distintos, sus varillas.
        var ids = new FilteredElementCollector(doc).OfClass(typeof(AreaReinforcement))
            .Concat(new FilteredElementCollector(doc).OfClass(typeof(Rebar)))
            .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == llave)
            .Select(e => e.Id)
            .ToList();

        if (ids.Count > 0)
        {
            doc.Delete(ids);
        }

        return ids.Count;
    }
}
