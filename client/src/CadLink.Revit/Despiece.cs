using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// El <b>despiece en Revit</b>: las propiedades de tipo que lee la etiqueta, y un corte por
/// seccion armada, con sus llamadas de lecho y su etiqueta, acomodados en una hoja.
/// </summary>
/// <remarks>
/// <para>
/// <b>La etiqueta se edita desde las propiedades de tipo.</b> En el tipo se escriben: Codigo de
/// montaje = las varillas («4 vars. #3C»), Nota clave = CONCRETO, Modelo = «15 X 30 CM»,
/// Descripcion = el ID de CadLink y Marca de tipo = el elemento. La etiqueta de la familia los
/// lee, asi que cambiar lo que dice el plano es cambiar el tipo.
/// </para>
/// <para>
/// Lo que se decide -donde va el corte, que encuadra, donde caen las llamadas, como se acomodan
/// en la hoja- vive en el nucleo, <see cref="PlanDespiece"/>, y tiene pruebas. Aqui solo se llama
/// a la API, y TODAS las llamadas van con argumentos posicionales (validar.py §26).
/// </para>
/// </remarks>
internal static class Despiece
{
    /// <summary>El nombre de la hoja del despiece.</summary>
    public const string NombreHoja = "DESPIECE DE SECCIONES - CadLink";

    /// <summary>Escribe las propiedades de tipo de la seccion. Lo que no se pudo, al informe.</summary>
    public static void Propiedades(Document doc, ElementId tipo, ArmadoJson a, ResultadoArmado r)
    {
        if (doc.GetElement(tipo) is not ElementType t)
        {
            return;
        }

        var p = PlanDespiece.Propiedades(a);

        // POR NOMBRE DE TEXTO, no con el miembro del enum escrito: el del Codigo de montaje no
        // se llama igual en todas las versiones -el miembro UNIFORMAT_CODE del enum
        // no compila-, y un nombre que no existe tiraba la compilacion entera. Asi, si no esta,
        // se busca el parametro por su nombre en ingles y en espanol.
        Poner(t, new[] { "UNIFORMAT_CODE", "ASSEMBLY_CODE" },
            new[] { "Código de montaje", "Codigo de montaje", "Assembly Code" }, p.CodigoDeMontaje, "Código de montaje", r);
        Poner(t, new[] { "KEYNOTE_PARAM" }, new[] { "Nota clave", "Keynote" }, p.NotaClave, "Nota clave", r);
        Poner(t, new[] { "ALL_MODEL_MODEL" }, new[] { "Modelo", "Model" }, p.Modelo, "Modelo", r);
        Poner(t, new[] { "ALL_MODEL_DESCRIPTION" }, new[] { "Descripción", "Descripcion", "Description" },
            p.Descripcion, "Descripción", r);
        Poner(t, new[] { "ALL_MODEL_TYPE_MARK" }, new[] { "Marca de tipo", "Type Mark" }, p.MarcaDeTipo, "Marca de tipo", r);

        // El estribo, para que la etiqueta tenga de donde leerlo: «Estr. #3C @15 cm».
        if (p.ComentariosDeTipo.Length > 0)
        {
            Poner(t, new[] { "ALL_MODEL_TYPE_COMMENTS" }, new[] { "Comentarios de tipo", "Type Comments" },
                p.ComentariosDeTipo, "Comentarios de tipo", r);
        }
    }

    /// <summary>Lo que dice la Descripcion de un tipo: el ID de CadLink, si ya se armo.</summary>
    public static string? DescripcionDe(Element tipo)
    {
        try
        {
            return Parametro(tipo, new[] { "ALL_MODEL_DESCRIPTION" },
                new[] { "Descripción", "Descripcion", "Description" })?.AsString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>El parametro: por el nombre del BuiltInParameter, y si no, por como se llama.</summary>
    internal static Parameter? Parametro(Element e, string[] internos, string[] nombres)
    {
        foreach (var n in internos)
        {
            if (Enum.TryParse<BuiltInParameter>(n, out var bip) && e.get_Parameter(bip) is { } p)
            {
                return p;
            }
        }

        foreach (var n in nombres)
        {
            if (e.LookupParameter(n) is { } p)
            {
                return p;
            }
        }

        return null;
    }

    private static void Poner(
        Element e, string[] internos, string[] nombres, string valor, string nombre, ResultadoArmado r)
    {
        try
        {
            var p = Parametro(e, internos, nombres);

            if (p is null || p.IsReadOnly || !p.Set(valor))
            {
                r.Avisos.Add($"«propiedad de tipo»: {nombre} de «{e.Name}» no se pudo escribir");
            }
        }
        catch (Exception ex)
        {
            r.Avisos.Add($"«propiedad de tipo»: {nombre} de «{e.Name}»: {ex.Message}");
        }
    }

    /// <summary>
    /// Un corte por seccion, con sus llamadas y su etiqueta, todos en una hoja. Va dentro de la
    /// MISMA transaccion del armado: un Ctrl+Z deshace todo.
    /// </summary>
    /// <param name="cortes">Cada seccion con la pieza que la representa.</param>
    /// <param name="nombres">El nombre de cada corte, si se eligio; si no, el del plan.</param>
    /// <param name="enHoja">Ponerlos en la hoja del despiece. Sin hoja, solo las vistas.</param>
    /// <returns>Las vistas creadas.</returns>
    public static List<View> Crear(
        Document doc, IReadOnlyList<(ArmadoJson Armado, FamilyInstance Pieza, MarcoPieza Marco)> cortes,
        ResultadoArmado r, IReadOnlyList<string>? nombres = null, bool enHoja = true)
    {
        var creadas = new List<View>();

        if (cortes.Count == 0)
        {
            return creadas;
        }

        var tipoDeCorte = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .OfType<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);

        if (tipoDeCorte is null)
        {
            r.Errores.Add("«despiece»: el proyecto no tiene ningún tipo de vista de sección");
            return creadas;
        }

        var tipoDeTexto = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        var enUso = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v => v.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        // Revit necesita la geometria del armado ya calculada para dibujarlo en las vistas.
        doc.Regenerate();

        var hechas = new List<(View Vista, CorteDeSeccion Plan)>();

        for (var i = 0; i < cortes.Count; i++)
        {
            var (a, pieza, marco) = cortes[i];
            var plan = PlanDespiece.Corte(a, marco);

            if (nombres is not null && i < nombres.Count && nombres[i].Trim().Length > 0)
            {
                plan = plan with { Nombre = nombres[i].Trim() };
            }

            try
            {
                var vista = Vista(doc, tipoDeCorte, plan, enUso);
                Llamadas(doc, vista, plan, tipoDeTexto);
                Etiqueta(doc, vista, pieza, plan, a, r);
                Cotas(doc, vista, pieza, plan, a, r);
                hechas.Add((vista, plan));
                creadas.Add(vista);
            }
            catch (Exception ex)
            {
                r.Errores.Add($"«despiece»: {plan.Nombre}: {ex.Message}");
            }
        }

        if (hechas.Count > 0 && enHoja)
        {
            Hoja(doc, hechas, r);
            r.Avisos.Add($"«despiece»: {hechas.Count} corte(s) en la hoja «{NombreHoja}»");
        }

        return creadas;
    }

    /// <summary>
    /// Las dos cotas del corte: la base encima de la seccion y el peralte a su derecha.
    /// </summary>
    /// <remarks>
    /// Se acotan las CARAS de la pieza que se ven en el corte: las dos verticales para la base y
    /// la de arriba y la de abajo para el peralte. Los planos de referencia de la familia
    /// -Left/Right, Top/Bottom- fallaron en la familia del usuario («la familia no tiene planos
    /// de referencia» y «hay referencias de cota que no son validas»): no todas los tienen y no
    /// todos se ven en un corte. Quedan solo como respaldo.
    /// </remarks>
    private static void Cotas(
        Document doc, View vista, FamilyInstance pieza, CorteDeSeccion plan, ArmadoJson a, ResultadoArmado r)
    {
        var (y1, y2) = a.EsHorizontal
            ? (FamilyInstanceReferenceType.Bottom, FamilyInstanceReferenceType.Top)
            : (FamilyInstanceReferenceType.Front, FamilyInstanceReferenceType.Back);

        var b = a.BaseCm / 100;
        var h = a.AlturaCm / 100;

        var arriba = Cota(doc, vista, pieza, plan.EjeX,
            FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right,
            plan.Origen + (plan.EjeY * plan.YCotaBase) - (plan.EjeX * (b / 2)),
            plan.Origen + (plan.EjeY * plan.YCotaBase) + (plan.EjeX * (b / 2)));

        var lado = Cota(doc, vista, pieza, plan.EjeY, y1, y2,
            plan.Origen + (plan.EjeX * plan.XCotaAltura) - (plan.EjeY * (h / 2)),
            plan.Origen + (plan.EjeX * plan.XCotaAltura) + (plan.EjeY * (h / 2)));

        if (!arriba || !lado)
        {
            r.Avisos.Add($"«sin cotas»: {plan.Nombre}. Revit no dejó acotar "
                         + (!arriba && !lado ? "la base ni el peralte" : !arriba ? "la base" : "el peralte")
                         + "; acótalo a mano");
        }
    }

    /// <summary>Una cota: primero entre las caras de la pieza, y si no, entre sus planos de referencia.</summary>
    private static bool Cota(
        Document doc, View vista, FamilyInstance pieza, V3 eje,
        FamilyInstanceReferenceType de, FamilyInstanceReferenceType a, V3 p1, V3 p2)
    {
        var linea = Line.CreateBound(Punto(p1), Punto(p2));

        if (Caras(pieza, eje) is { } caras && Acotar(doc, vista, linea, caras.Min, caras.Max))
        {
            return true;
        }

        try
        {
            var r1 = pieza.GetReferences(de).FirstOrDefault();
            var r2 = pieza.GetReferences(a).FirstOrDefault();

            return r1 is not null && r2 is not null && Acotar(doc, vista, linea, r1, r2);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// La cota entre dos referencias. Se crea en una SUBTRANSACCION: si Revit la da por mala
    /// -«hay una o varias referencias de cota que no son validas»-, se deshace sin dejar el
    /// aviso en el informe y se prueba la otra forma.
    /// </summary>
    private static bool Acotar(Document doc, View vista, Line linea, Reference r1, Reference r2)
    {
        using var sub = new SubTransaction(doc);
        sub.Start();

        try
        {
            var refs = new ReferenceArray();
            refs.Append(r1);
            refs.Append(r2);

            var cota = doc.Create.NewDimension(vista, linea, refs);
            doc.Regenerate();

            if (cota is not null && cota.IsValidObject && (cota.Value ?? 0) > 1e-6)
            {
                sub.Commit();
                return true;
            }
        }
        catch (Exception)
        {
            // Se deshace abajo.
        }

        sub.RollBack();
        return false;
    }

    /// <summary>
    /// Las dos caras planas de la pieza perpendiculares a <paramref name="eje"/> que estan mas
    /// lejos una de otra: los dos lados de la seccion. Con <c>ComputeReferences</c> para que se
    /// puedan acotar.
    /// </summary>
    private static (Reference Min, Reference Max)? Caras(FamilyInstance pieza, V3 eje)
    {
        try
        {
            var dir = Vector(eje);
            // Sin la vista en las opciones: el corte acaba de crearse y su geometria aun no se ha
            // generado. Las referencias de la pieza sirven igual en cualquier vista.
            var opciones = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var geo = pieza.get_Geometry(opciones);

            if (geo is null)
            {
                return null;
            }

            var caras = new List<(Reference Ref, double Pos)>();

            void DeSolidos(IEnumerable<GeometryObject> objetos, Transform t)
            {
                foreach (var o in objetos)
                {
                    if (o is not Solid solido || solido.Faces.Size == 0)
                    {
                        continue;
                    }

                    foreach (var f in solido.Faces)
                    {
                        if (f is not PlanarFace pf || pf.Reference is null)
                        {
                            continue;
                        }

                        var normal = t.OfVector(pf.FaceNormal);

                        if (Math.Abs(Math.Abs(normal.DotProduct(dir)) - 1) < 1e-6)
                        {
                            caras.Add((pf.Reference, t.OfPoint(pf.Origin).DotProduct(dir)));
                        }
                    }
                }
            }

            foreach (var o in geo)
            {
                if (o is GeometryInstance gi)
                {
                    // Las caras del SIMBOLO: sus referencias son las que Revit deja acotar en la
                    // pieza. Las de GetInstanceGeometry suelen salir sin referencia.
                    DeSolidos(gi.GetSymbolGeometry(), gi.Transform);
                }
                else
                {
                    DeSolidos(new[] { o }, Transform.Identity);
                }
            }

            if (caras.Count < 2)
            {
                return null;
            }

            var min = caras.MinBy(c => c.Pos);
            var max = caras.MaxBy(c => c.Pos);

            return max.Pos - min.Pos > 1e-6 ? (min.Ref, max.Ref) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ViewSection Vista(
        Document doc, ViewFamilyType tipo, CorteDeSeccion plan, HashSet<string> nombres)
    {
        var t = Transform.Identity;
        t.Origin = Punto(plan.Origen);
        t.BasisX = Vector(plan.EjeX);
        t.BasisY = Vector(plan.EjeY);
        t.BasisZ = Vector(plan.EjeZ);

        var caja = new BoundingBoxXYZ();
        caja.Transform = t;
        caja.Min = Punto(plan.Min);
        caja.Max = Punto(plan.Max);

        var vista = ViewSection.CreateSection(doc, tipo.Id, caja);

        // QUE NO SALGA EN ESPEJO. El corte tiene que verse como el plano de AutoCAD -la base de la
        // seccion de izquierda a derecha, el gancho del estribo arriba a la DERECHA-. Si Revit
        // pone la derecha de la vista al reves de la X pedida, se rehace girado media vuelta
        // sobre la vertical: misma seccion, vista desde el otro lado.
        if (vista.RightDirection.DotProduct(Vector(plan.EjeX)) < 0)
        {
            doc.Delete(new List<ElementId> { vista.Id });

            var g = Transform.Identity;
            g.Origin = t.Origin;
            g.BasisX = Vector(plan.EjeX * -1);
            g.BasisY = t.BasisY;
            g.BasisZ = Vector(plan.EjeZ * -1);

            caja = new BoundingBoxXYZ();
            caja.Transform = g;
            caja.Min = Punto(new V3(-plan.Max.X, plan.Min.Y, plan.Min.Z));
            caja.Max = Punto(new V3(-plan.Min.X, plan.Max.Y, plan.Max.Z));

            vista = ViewSection.CreateSection(doc, tipo.Id, caja);
        }

        vista.Scale = PlanDespiece.Escala;
        vista.DetailLevel = ViewDetailLevel.Fine;
        vista.CropBoxActive = true;
        vista.CropBoxVisible = false;

        // El nombre tiene que ser unico en el proyecto: si ya hay uno, «(2)», «(3)»...
        var nombre = PlanDespiece.NombreLibre(plan.Nombre, nombres);

        vista.Name = nombre;
        nombres.Add(nombre);

        return vista;
    }

    /// <summary>Las llamadas de los lechos, «2 vars. #3C», como textos en el plano del corte.</summary>
    private static void Llamadas(Document doc, View vista, CorteDeSeccion plan, ElementId tipoDeTexto)
    {
        foreach (var l in plan.Llamadas)
        {
            var enModelo = plan.Origen + (plan.EjeX * l.X) + (plan.EjeY * l.Y);
            TextNote.Create(doc, vista.Id, Punto(enModelo), l.Texto, tipoDeTexto);
        }
    }

    /// <summary>
    /// La etiqueta de la pieza, debajo de la seccion. Es la etiqueta de la categoria cargada en
    /// el proyecto: lee las propiedades de tipo, asi que se edita desde ahi.
    /// </summary>
    private static void Etiqueta(
        Document doc, View vista, FamilyInstance pieza, CorteDeSeccion plan, ArmadoJson a, ResultadoArmado r)
    {
        var punto = plan.Origen + (plan.EjeX * plan.PuntoDeEtiqueta.X) + (plan.EjeY * plan.PuntoDeEtiqueta.Y);

        try
        {
            IndependentTag.Create(
                doc, vista.Id, new Reference(pieza), false,
                TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, Punto(punto));
        }
        catch (Exception)
        {
            r.Avisos.Add($"«sin etiqueta»: {a.Id}. Carga en el proyecto una etiqueta de "
                         + (a.EsHorizontal ? "armazón estructural" : "pilar estructural")
                         + " que lea la Marca de tipo, la Descripción y el Código de montaje");
        }
    }

    /// <summary>
    /// Las hojas con los cortes, en renglones, como la fila de secciones de AutoCAD. Cuando una
    /// se llena, se crea la siguiente.
    /// </summary>
    private static void Hoja(Document doc, List<(View Vista, CorteDeSeccion Plan)> hechas, ResultadoArmado r)
    {
        var cajetin = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .FirstElementId();

        var hojas = new List<ViewSheet>();

        ViewSheet Nueva()
        {
            var h = ViewSheet.Create(doc, cajetin);
            h.Name = hojas.Count == 0 ? NombreHoja : $"{NombreHoja} ({hojas.Count + 1})";
            hojas.Add(h);
            return h;
        }

        var primera = Nueva();

        // Lo que mide la hoja, de su cajetin; sin cajetin, un A1.
        var ancho = 0.841;
        var alto = 0.594;

        try
        {
            var o = primera.Outline;
            var w = Unidades.AMetros(o.Max.U - o.Min.U);
            var h = Unidades.AMetros(o.Max.V - o.Min.V);

            if (w > 0.1 && h > 0.1)
            {
                ancho = w;
                alto = h;
            }
        }
        catch (Exception)
        {
            // Se queda el A1.
        }

        var lugares = PlanDespiece.Acomodo(hechas.Select(x => PlanDespiece.EnPapel(x.Plan)).ToList(), ancho, alto);

        for (var i = 0; i < hechas.Count; i++)
        {
            var (n, x, y) = lugares[i];

            while (hojas.Count <= n)
            {
                Nueva();
            }

            var hoja = hojas[n];

            if (!Viewport.CanAddViewToSheet(doc, hoja.Id, hechas[i].Vista.Id))
            {
                r.Avisos.Add($"«despiece»: {hechas[i].Plan.Nombre} no se pudo poner en la hoja");
                continue;
            }

            Viewport.Create(doc, hoja.Id, hechas[i].Vista.Id,
                new XYZ(Unidades.AInternas(x), Unidades.AInternas(y), 0));
        }
    }

    private static XYZ Punto(V3 p) =>
        new(Unidades.AInternas(p.X), Unidades.AInternas(p.Y), Unidades.AInternas(p.Z));

    private static XYZ Vector(V3 v) => new(v.X, v.Y, v.Z);
}
