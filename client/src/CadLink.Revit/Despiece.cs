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
        ResultadoArmado r, IReadOnlyList<string>? nombres = null, bool enHoja = true, bool unaVista = false)
    {
        var creadas = new List<View>();

        if (cortes.Count == 0)
        {
            return creadas;
        }

        // Se pidio: todas las secciones en UNA vista, no un corte por cada una.
        if (unaVista)
        {
            if (Juntas(doc, cortes.Select(c => c.Armado).ToList(), r, enHoja) is { } junta)
            {
                creadas.Add(junta);
            }

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

                // Como quedo la vista DE VERDAD: con eso las cotas van arriba y a la derecha, las
                // llamadas a la izquierda y la etiqueta abajo, como en AutoCAD.
                var (der, arr) = ComoSeVe(doc, vista, plan, tipoDeTexto);
                var (enVista, aVista) = PlanDespiece.ALaVista(plan, a, der, arr);
                plan = enVista;

                Llamadas(doc, vista, plan, tipoDeTexto, r);
                Etiqueta(doc, vista, pieza, plan, aVista, r);
                Cotas(doc, vista, pieza, plan, aVista, r);
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

        // El numero del peralte, A LA DERECHA de su linea: centrado en ella pisaba la seccion.
        var lado = Cota(doc, vista, pieza, plan.EjeY, y1, y2,
            plan.Origen + (plan.EjeX * plan.XCotaAltura) - (plan.EjeY * (h / 2)),
            plan.Origen + (plan.EjeX * plan.XCotaAltura) + (plan.EjeY * (h / 2)),
            plan.Origen + (plan.EjeX * (plan.XCotaAltura + PlanDespiece.TextoCotaAltura)));

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
        FamilyInstanceReferenceType de, FamilyInstanceReferenceType a, V3 p1, V3 p2, V3? texto = null)
    {
        var linea = Line.CreateBound(Punto(p1), Punto(p2));

        if (Caras(pieza, eje) is { } caras && Acotar(doc, vista, linea, caras.Min, caras.Max, texto))
        {
            return true;
        }

        try
        {
            var r1 = pieza.GetReferences(de).FirstOrDefault();
            var r2 = pieza.GetReferences(a).FirstOrDefault();

            return r1 is not null && r2 is not null && Acotar(doc, vista, linea, r1, r2, texto);
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
    private static bool Acotar(Document doc, View vista, Line linea, Reference r1, Reference r2, V3? texto)
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
                if (texto is { } t)
                {
                    try
                    {
                        cota.TextPosition = Punto(t);
                    }
                    catch (Exception)
                    {
                        // Si el estilo de cota no deja mover el texto, se queda donde Revit lo pone.
                    }
                }

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

    /// <summary>
    /// Las llamadas de los lechos como en AutoCAD: el texto «2 vars. #3C» a la izquierda de la
    /// seccion, una espina horizontal hasta la ultima varilla del grupo y una linea con FLECHA
    /// RELLENA a cada varilla. Lineas de detalle y regiones rellenas: son del corte, no del modelo.
    /// </summary>
    private static void Llamadas(Document doc, View vista, CorteDeSeccion plan, ElementId tipoDeTexto, ResultadoArmado r)
    {
        XYZ En(double x, double y) => Punto(plan.Origen + (plan.EjeX * x) + (plan.EjeY * y));

        var relleno = TipoDeRellenoSolido(doc);
        var textos = new List<(TextNote Nota, LlamadaDeCorte Llamada)>();
        var sinFlecha = false;

        foreach (var l in plan.Llamadas)
        {
            foreach (var t in l.Lineas)
            {
                // Revit no acepta lineas mas cortas que su tolerancia (~0.8 mm).
                if (Math.Abs(t.X2 - t.X1) + Math.Abs(t.Y2 - t.Y1) < 0.002)
                {
                    continue;
                }

                try
                {
                    doc.Create.NewDetailCurve(vista, Line.CreateBound(En(t.X1, t.Y1), En(t.X2, t.Y2)));
                }
                catch (Exception)
                {
                    // Una linea de menos no quita la llamada.
                }
            }

            foreach (var f in l.Flechas)
            {
                // Rellena con LINEAS, que no apaga la plantilla de vista; la region va encima.
                foreach (var t in f.Relleno())
                {
                    Linea(doc, vista, En(t.X1, t.Y1), En(t.X2, t.Y2));
                }

                var v = f.Triangulo().Select(p => En(p.X, p.Y)).ToList();
                var lados = new List<Curve>
                {
                    Line.CreateBound(v[0], v[1]), Line.CreateBound(v[1], v[2]), Line.CreateBound(v[2], v[0])
                };

                try
                {
                    if (relleno is null)
                    {
                        throw new InvalidOperationException("sin relleno solido");
                    }

                    FilledRegion.Create(doc, relleno, vista.Id, new List<CurveLoop> { CurveLoop.Create(lados) });
                }
                catch (Exception)
                {
                    // Sin relleno, al menos el contorno del triangulo.
                    sinFlecha = true;

                    foreach (var c in lados)
                    {
                        try
                        {
                            doc.Create.NewDetailCurve(vista, c);
                        }
                        catch (Exception)
                        {
                            // Nada.
                        }
                    }
                }
            }

            textos.Add((TextNote.Create(doc, vista.Id, En(l.X, l.Y), l.Texto, tipoDeTexto), l));
        }

        if (sinFlecha)
        {
            r.Avisos.Add("«flechas sin relleno»: el proyecto no tiene un relleno sólido para las flechas de las llamadas");
        }

        // El texto se crea con su esquina en el punto; ya medido, se acomoda como en AutoCAD:
        // su borde derecho al final de la espina y centrado en su altura.
        doc.Regenerate();

        foreach (var (nota, l) in textos)
        {
            if (EnLaVista(nota, vista, plan) is not { } caja)
            {
                continue;
            }

            var dx = (l.X - PlanDespiece.HuecoTexto) - caja.XMax;
            var dy = l.Y - ((caja.YMin + caja.YMax) / 2);
            Mover(doc, nota, plan, dx, dy);
        }
    }

    /// <summary>
    /// Lo que ocupa un elemento en el corte, en metros de la vista -X a la derecha y Y arriba
    /// desde el centro de la seccion-. Null si Revit no lo dice.
    /// </summary>
    private static (double XMin, double XMax, double YMin, double YMax)? EnLaVista(Element e, View vista, CorteDeSeccion plan)
    {
        try
        {
            if (e.get_BoundingBox(vista) is not { } c)
            {
                return null;
            }

            var xs = new List<double>();
            var ys = new List<double>();

            foreach (var x in new[] { c.Min.X, c.Max.X })
            {
                foreach (var y in new[] { c.Min.Y, c.Max.Y })
                {
                    foreach (var z in new[] { c.Min.Z, c.Max.Z })
                    {
                        var p = new V3(Unidades.AMetros(x), Unidades.AMetros(y), Unidades.AMetros(z)) - plan.Origen;
                        xs.Add(p.Punto(plan.EjeX));
                        ys.Add(p.Punto(plan.EjeY));
                    }
                }
            }

            return (xs.Min(), xs.Max(), ys.Min(), ys.Max());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Mover(Document doc, Element e, CorteDeSeccion plan, double dx, double dy)
    {
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-4)
        {
            return;
        }

        try
        {
            ElementTransformUtils.MoveElement(doc, e.Id, Vector((plan.EjeX * Unidades.AInternas(dx)) + (plan.EjeY * Unidades.AInternas(dy))));
        }
        catch (Exception)
        {
            // Se queda donde quedo.
        }
    }

    /// <summary>
    /// El tipo de region rellena para las flechas: uno con relleno SOLIDO; si no hay, se crea
    /// duplicando el primero. Null si el proyecto no tiene ninguno.
    /// </summary>
    private static ElementId? TipoDeRellenoSolido(Document doc)
    {
        try
        {
            var solido = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .OfType<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            if (solido is null)
            {
                return null;
            }

            var tipos = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .OfType<FilledRegionType>()
                .ToList();

            if (tipos.FirstOrDefault(t => t.ForegroundPatternId == solido.Id) is { } ya)
            {
                return ya.Id;
            }

            if (tipos.Count == 0)
            {
                return null;
            }

            var nombre = PlanDespiece.NombreLibre(
                "CadLink - Flecha sólida",
                tipos.Select(t => t.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase));

            if (tipos[0].Duplicate(nombre) is not FilledRegionType nuevo)
            {
                return null;
            }

            nuevo.ForegroundPatternId = solido.Id;
            return nuevo.Id;
        }
        catch (Exception)
        {
            return null;
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
            var tag = IndependentTag.Create(
                doc, vista.Id, new Reference(pieza), false,
                TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, Punto(punto));

            // Donde queda el punto dentro de la etiqueta depende de su familia. Ya medida, se
            // sube para que su borde de ARRIBA quede pegado a la seccion y centrada, como el
            // rotulo de AutoCAD, que va colgado de su centro de arriba.
            doc.Regenerate();

            if (EnLaVista(tag, vista, plan) is { } caja)
            {
                Mover(doc, tag, plan,
                    plan.PuntoDeEtiqueta.X - ((caja.XMin + caja.XMax) / 2),
                    plan.PuntoDeEtiqueta.Y - caja.YMax);
            }
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

    /// <summary>
    /// Hacia donde quedaron DE VERDAD la derecha y el arriba de la vista, medidos con un texto.
    /// </summary>
    /// <remarks>
    /// <c>RightDirection</c> y <c>UpDirection</c> de un corte recien creado dicen lo que se pidio, no
    /// como lo pinta Revit: en los cortes de columna la vista salia girada media vuelta y las cotas
    /// quedaban abajo y a la izquierda aunque esas dos dijeran que estaba bien. Un texto siempre se
    /// pinta derecho y de izquierda a derecha, asi que su direccion es la de la pantalla. Se pone uno
    /// de prueba, se mide y se borra.
    /// </remarks>
    private static (V3 Derecha, V3 Arriba) ComoSeVe(Document doc, View vista, CorteDeSeccion plan, ElementId tipoDeTexto)
    {
        V3 De(XYZ v) => new(v.X, v.Y, v.Z);
        var derecha = De(vista.RightDirection);
        var arriba = De(vista.UpDirection);

        try
        {
            doc.Regenerate();
            var prueba = TextNote.Create(doc, vista.Id, Punto(plan.Origen), "X", tipoDeTexto);
            doc.Regenerate();

            var b = De(prueba.BaseDirection);
            var u = De(prueba.UpDirection);
            doc.Delete(new List<ElementId> { prueba.Id });

            if (b.Largo > 0.5 && u.Largo > 0.5)
            {
                derecha = b;
                arriba = u;
            }
        }
        catch (Exception)
        {
            // Se queda lo que dice la vista.
        }

        return (derecha, arriba);
    }

    /// <summary>Una linea de detalle, si Revit la acepta (no muy corta).</summary>
    private static DetailCurve? Linea(Document doc, View vista, XYZ a, XYZ b)
    {
        if (a.DistanceTo(b) < Unidades.AInternas(0.0008))
        {
            return null;
        }

        try
        {
            return doc.Create.NewDetailCurve(vista, Line.CreateBound(a, b));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// TODAS las secciones en una sola VISTA DE DIBUJO, en renglones, como la fila de secciones de
    /// AutoCAD: el concreto, el estribo, las varillas rellenas, las llamadas con su flecha, las cotas
    /// de base y peralte y el nombre debajo. Se pidio asi porque un corte por seccion llenaba el
    /// navegador de vistas.
    /// </summary>
    /// <remarks>
    /// Todo es dibujo 2D -lineas de detalle, regiones rellenas, textos y cotas entre lineas-, asi que
    /// una plantilla de vista de cortes no le apaga las flechas ni las varillas. Es la foto del
    /// armado: si cambia la seccion, se vuelve a armar y la vista se rehace.
    /// </remarks>
    private static View? Juntas(Document doc, IReadOnlyList<ArmadoJson> armados, ResultadoArmado r, bool enHoja)
    {
        var tipo = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .OfType<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);

        if (tipo is null)
        {
            r.Errores.Add("«despiece»: el proyecto no tiene ningún tipo de vista de dibujo (Drafting View)");
            return null;
        }

        var enUso = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v => v.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        var vista = ViewDrafting.Create(doc, tipo.Id);
        vista.Scale = PlanDespiece.Escala;
        vista.Name = PlanDespiece.NombreLibre("CadLink - Secciones", enUso);

        var tipoDeTexto = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        var relleno = TipoDeRellenoSolido(doc);
        var centros = PlanDespiece.EnUnaVista(armados);
        var textos = new List<(TextNote Nota, double XDer, double YCentro)>();
        var etiquetas = new List<(TextNote Nota, double XCentro, double YArriba)>();

        for (var i = 0; i < armados.Count; i++)
        {
            var a = armados[i];
            var (cx, cy) = centros[i];
            var b = a.BaseCm / 100;
            var h = a.AlturaCm / 100;

            XYZ P(double x, double y) => new(Unidades.AInternas(cx + x), Unidades.AInternas(cy + y), 0);

            try
            {
                // ---- Concreto ----
                var izq = Linea(doc, vista, P(-b / 2, -h / 2), P(-b / 2, h / 2));
                var der = Linea(doc, vista, P(b / 2, -h / 2), P(b / 2, h / 2));
                var abajo = Linea(doc, vista, P(-b / 2, -h / 2), P(b / 2, -h / 2));
                var arriba = Linea(doc, vista, P(-b / 2, h / 2), P(b / 2, h / 2));

                // ---- Estribo: sus dos caras ----
                if (a.DiamEstriboCm > 0)
                {
                    foreach (var d in new[] { a.RecubrimientoCm / 100, (a.RecubrimientoCm + a.DiamEstriboCm) / 100 })
                    {
                        var (x0, x1, y0, y1) = ((-b / 2) + d, (b / 2) - d, (-h / 2) + d, (h / 2) - d);

                        if (x1 > x0 && y1 > y0)
                        {
                            Linea(doc, vista, P(x0, y0), P(x1, y0));
                            Linea(doc, vista, P(x1, y0), P(x1, y1));
                            Linea(doc, vista, P(x1, y1), P(x0, y1));
                            Linea(doc, vista, P(x0, y1), P(x0, y0));
                        }
                    }
                }

                // ---- Varillas, rellenas ----
                foreach (var v in a.Varillas)
                {
                    var c = P((v.XCm / 100) - (b / 2), (v.YCm / 100) - (h / 2));
                    var rad = Unidades.AInternas(v.DiamCm / 200);

                    if (rad <= 0)
                    {
                        continue;
                    }

                    var medio1 = Arc.Create(c, rad, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                    var medio2 = Arc.Create(c, rad, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

                    try
                    {
                        if (relleno is not null)
                        {
                            FilledRegion.Create(doc, relleno, vista.Id, new List<CurveLoop> { CurveLoop.Create(new List<Curve> { medio1, medio2 }) });
                        }
                    }
                    catch (Exception)
                    {
                        // Queda el contorno.
                    }

                    foreach (var arco in new Curve[] { medio1, medio2 })
                    {
                        try
                        {
                            doc.Create.NewDetailCurve(vista, arco);
                        }
                        catch (Exception)
                        {
                            // Nada.
                        }
                    }
                }

                // ---- Llamadas, con la flecha rellena de lineas ----
                foreach (var l in PlanDespiece.Llamadas(a))
                {
                    foreach (var t in l.Lineas)
                    {
                        Linea(doc, vista, P(t.X1, t.Y1), P(t.X2, t.Y2));
                    }

                    foreach (var f in l.Flechas)
                    {
                        foreach (var t in f.Relleno())
                        {
                            Linea(doc, vista, P(t.X1, t.Y1), P(t.X2, t.Y2));
                        }
                    }

                    textos.Add((TextNote.Create(doc, vista.Id, P(l.X, l.Y), l.Texto, tipoDeTexto),
                        cx + l.X - PlanDespiece.HuecoTexto, cy + l.Y));
                }

                // ---- Nombre y medida, debajo ----
                var nombre = $"{a.Id.Trim()}\r{Cm(a.BaseCm)} X {Cm(a.AlturaCm)} CM";
                etiquetas.Add((TextNote.Create(doc, vista.Id, P(0, -(h / 2) - PlanDespiece.AireEtiqueta), nombre, tipoDeTexto),
                    cx, cy - (h / 2) - PlanDespiece.AireEtiqueta));

                // ---- Cotas entre las lineas del concreto ----
                var sinCota = false;

                if (izq is not null && der is not null)
                {
                    var y = (h / 2) + 0.06;
                    sinCota |= !Acotar(doc, vista, Line.CreateBound(P(-b / 2, y), P(b / 2, y)),
                        izq.GeometryCurve.Reference, der.GeometryCurve.Reference, null);
                }

                if (abajo is not null && arriba is not null)
                {
                    var x = (b / 2) + 0.06;
                    sinCota |= !Acotar(doc, vista, Line.CreateBound(P(x, -h / 2), P(x, h / 2)),
                        abajo.GeometryCurve.Reference, arriba.GeometryCurve.Reference, null);
                }

                if (sinCota)
                {
                    r.Avisos.Add($"«sin cotas»: {a.Id} en la vista de secciones; acótala a mano");
                }
            }
            catch (Exception ex)
            {
                r.Errores.Add($"«despiece»: {a.Id}: {ex.Message}");
            }
        }

        // Los textos, ya medidos: el de la llamada con su borde derecho en la espina y centrado en
        // ella; el nombre centrado y colgado de su borde de arriba.
        doc.Regenerate();

        void Acomodar(TextNote nota, Func<(double XMin, double XMax, double YMin, double YMax), (double Dx, double Dy)> cuanto)
        {
            try
            {
                if (nota.get_BoundingBox(vista) is not { } c)
                {
                    return;
                }

                var (dx, dy) = cuanto((Unidades.AMetros(c.Min.X), Unidades.AMetros(c.Max.X), Unidades.AMetros(c.Min.Y), Unidades.AMetros(c.Max.Y)));

                if (Math.Abs(dx) + Math.Abs(dy) > 1e-4)
                {
                    ElementTransformUtils.MoveElement(doc, nota.Id, new XYZ(Unidades.AInternas(dx), Unidades.AInternas(dy), 0));
                }
            }
            catch (Exception)
            {
                // Se queda donde quedo.
            }
        }

        foreach (var (nota, xDer, yCentro) in textos)
        {
            Acomodar(nota, c => (xDer - c.XMax, yCentro - ((c.YMin + c.YMax) / 2)));
        }

        foreach (var (nota, xCentro, yArriba) in etiquetas)
        {
            Acomodar(nota, c => (xCentro - ((c.XMin + c.XMax) / 2), yArriba - c.YMax));
        }

        if (enHoja)
        {
            var xs = centros.Select((c, i) => (c.X - (armados[i].BaseCm / 200) - PlanDespiece.AireIzquierda, c.X + (armados[i].BaseCm / 200) + PlanDespiece.AireDerecha)).ToList();
            var ys = centros.Select((c, i) => (c.Y - (armados[i].AlturaCm / 200) - PlanDespiece.AireAbajo, c.Y + (armados[i].AlturaCm / 200) + PlanDespiece.AireArriba)).ToList();
            var todo = new CorteDeSeccion(vista.Name ?? "CadLink - Secciones", new V3(0, 0, 0), new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1),
                new V3(xs.Min(x => x.Item1), ys.Min(y => y.Item1), 0), new V3(xs.Max(x => x.Item2), ys.Max(y => y.Item2), 0),
                new List<LlamadaDeCorte>(), new V3(0, 0, 0));
            Hoja(doc, new List<(View, CorteDeSeccion)> { (vista, todo) }, r);
        }

        r.Avisos.Add($"«despiece»: {armados.Count} seccion(es) en una sola vista, «{vista.Name}».");
        return vista;
    }

    private static string Cm(double cm) => cm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static XYZ Punto(V3 p) =>
        new(Unidades.AInternas(p.X), Unidades.AInternas(p.Y), Unidades.AInternas(p.Z));

    private static XYZ Vector(V3 v) => new(v.X, v.Y, v.Z);
}
