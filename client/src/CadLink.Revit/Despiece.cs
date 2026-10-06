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
    /// Las dos cotas del corte: la base encima de la seccion y el peralte a su derecha. Se acotan
    /// los planos de referencia de la familia -izquierda y derecha, arriba y abajo-, asi que la
    /// cota se mueve con la pieza si cambia de tipo.
    /// </summary>
    private static void Cotas(
        Document doc, View vista, FamilyInstance pieza, CorteDeSeccion plan, ArmadoJson a, ResultadoArmado r)
    {
        // En la trabe la base va entre Left y Right y el peralte entre Top y Bottom; en la
        // columna, que se corta en planta, el peralte va entre Front y Back.
        var (y1, y2) = a.EsHorizontal
            ? (FamilyInstanceReferenceType.Bottom, FamilyInstanceReferenceType.Top)
            : (FamilyInstanceReferenceType.Front, FamilyInstanceReferenceType.Back);

        var b = a.BaseCm / 100;
        var h = a.AlturaCm / 100;

        var arriba = Cota(doc, vista, pieza,
            FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right,
            plan.Origen + (plan.EjeY * plan.YCotaBase) - (plan.EjeX * (b / 2)),
            plan.Origen + (plan.EjeY * plan.YCotaBase) + (plan.EjeX * (b / 2)));

        var lado = Cota(doc, vista, pieza, y1, y2,
            plan.Origen + (plan.EjeX * plan.XCotaAltura) - (plan.EjeY * (h / 2)),
            plan.Origen + (plan.EjeX * plan.XCotaAltura) + (plan.EjeY * (h / 2)));

        if (!arriba || !lado)
        {
            r.Avisos.Add($"«sin cotas»: {plan.Nombre}. La familia no tiene planos de referencia "
                         + "izquierda/derecha y arriba/abajo con los que acotar; acótala a mano");
        }
    }

    private static bool Cota(
        Document doc, View vista, FamilyInstance pieza,
        FamilyInstanceReferenceType de, FamilyInstanceReferenceType a, V3 p1, V3 p2)
    {
        try
        {
            var r1 = pieza.GetReferences(de).FirstOrDefault();
            var r2 = pieza.GetReferences(a).FirstOrDefault();

            if (r1 is null || r2 is null)
            {
                return false;
            }

            var refs = new ReferenceArray();
            refs.Append(r1);
            refs.Append(r2);

            return doc.Create.NewDimension(vista, Line.CreateBound(Punto(p1), Punto(p2)), refs) is not null;
        }
        catch (Exception)
        {
            return false;
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
