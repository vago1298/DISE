using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// Recorre el proyecto de Revit y apunta QUE FAMILIAS Y TIPOS hay cargados de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Es la respuesta a "el complemento debe conectarse a Revit y verificar que familias tengo
/// creadas": no hay ninguna lista de familias escrita en el codigo. Lo que se ofrece en el
/// cuadro de mapeo es exactamente lo que este metodo encuentra en el documento abierto.
/// </para>
/// <para>
/// De cada tipo se intenta averiguar su forma y sus medidas, en este orden de fiabilidad:
/// </para>
/// <list type="number">
///   <item>la <b>seccion estructural</b> que declara Revit, que es el dato bueno;</item>
///   <item>los <b>parametros</b> del tipo -b, h, d, bf...-;</item>
///   <item>y si no, el <b>nombre</b> del tipo, que en las plantillas suele ser sus medidas.</item>
/// </list>
/// <para>
/// Lo que no se consigue averiguar se deja en <c>null</c>, que el emparejador trata como "no
/// se sabe". No se rellena con valores inventados.
/// </para>
/// </remarks>
internal static class LectorDeCatalogo
{
    /// <summary>Nombres de parametro con que las familias guardan el ancho.</summary>
    /// <remarks>
    /// No hay un parametro estandar para esto en una familia cargable, asi que se prueban los
    /// nombres que usan las plantillas de Autodesk y el contenido estructural: "b" en
    /// hormigon, "bf" en perfiles de acero.
    /// </remarks>
    private static readonly string[] NombresDeAncho =
        { "b", "bf", "Width", "Ancho", "Anchura", "Base" };

    private static readonly string[] NombresDePeralte =
        { "h", "d", "Height", "Depth", "Peralte", "Canto", "Altura" };

    public static CatalogoRevit Leer(Document doc)
    {
        var c = new CatalogoRevit();

        Cargables(doc, c, BuiltInCategory.OST_StructuralColumns,
            CategoriaRevit.ColumnaEstructural);

        Cargables(doc, c, BuiltInCategory.OST_StructuralFraming,
            CategoriaRevit.Estructura);

        Muros(doc, c);
        Pisos(doc, c);
        Niveles(doc, c);

        return c;
    }

    private static void Cargables(
        Document doc, CatalogoRevit catalogo, BuiltInCategory categoria, CategoriaRevit destino)
    {
        var encontrados = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(categoria)
            .Cast<FamilySymbol>();

        foreach (var s in encontrados)
        {
            var t = new TipoRevit
            {
                Categoria = destino,
                Familia = s.FamilyName ?? string.Empty,
                Tipo = s.Name ?? string.Empty,
                EsSistema = false,
                Id = s.Id.Value
            };

            // 1. La seccion estructural. Se pasa el NOMBRE de la forma como texto, no el
            //    enum: la traduccion vive en el nucleo, donde tiene pruebas, y asi no se
            //    escribe aqui el nombre de ningun miembro de un enum que cambia entre
            //    versiones de Revit.
            try
            {
                var seccion = s.GetStructuralSection();

                if (seccion is not null)
                {
                    t.Forma = FormasDeRevit.Interpretar(seccion.StructuralSectionShape.ToString());
                }
            }
            catch (Exception)
            {
                // Hay familias sin seccion estructural y algunas lanzan al preguntar. No es
                // un problema: se queda sin forma y el emparejador la deduce del nombre.
            }

            // 2. Los parametros del tipo.
            t.AnchoM = Longitud(s, NombresDeAncho);
            t.PeralteM = Longitud(s, NombresDePeralte);

            // 3. Y como ultimo recurso, el nombre.
            if (t.AnchoM is null || t.PeralteM is null)
            {
                var delNombre = MedidasPorNombre.Dos(t.Tipo);

                if (delNombre is not null)
                {
                    t.AnchoM ??= delNombre.Value.AnchoM;
                    t.PeralteM ??= delNombre.Value.PeralteM;
                }
            }

            catalogo.Tipos.Add(t);
        }
    }

    private static void Muros(Document doc, CatalogoRevit catalogo)
    {
        var tipos = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>();

        foreach (var w in tipos)
        {
            // Solo los muros BASICOS. Los cortina y los apilados no se pueden crear a
            // partir de un contorno con un espesor, y ofrecerlos en el cuadro seria ofrecer
            // algo que despues falla al modelar.
            if (w.Kind != WallKind.Basic)
            {
                continue;
            }

            double? espesor = null;

            try
            {
                if (w.Width > 0)
                {
                    espesor = Unidades.AMetros(w.Width);
                }
            }
            catch (Exception)
            {
                // Algunos tipos no contestan al ancho. Se deja sin espesor.
            }

            catalogo.Tipos.Add(new TipoRevit
            {
                Categoria = CategoriaRevit.Muro,
                Familia = w.FamilyName ?? "Muro básico",
                Tipo = w.Name ?? string.Empty,
                EsSistema = true,
                Id = w.Id.Value,
                EspesorM = espesor ?? MedidasPorNombre.Una(w.Name)
            });
        }
    }

    private static void Pisos(Document doc, CatalogoRevit catalogo)
    {
        var tipos = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>();

        foreach (var f in tipos)
        {
            double? espesor = null;

            try
            {
                var estructura = f.GetCompoundStructure();

                if (estructura is not null && estructura.GetWidth() > 0)
                {
                    espesor = Unidades.AMetros(estructura.GetWidth());
                }
            }
            catch (Exception)
            {
                // Idem: si no contesta, se deja sin espesor.
            }

            catalogo.Tipos.Add(new TipoRevit
            {
                Categoria = CategoriaRevit.Piso,
                Familia = f.FamilyName ?? "Suelo",
                Tipo = f.Name ?? string.Empty,
                EsSistema = true,
                Id = f.Id.Value,
                EspesorM = espesor ?? MedidasPorNombre.Una(f.Name)
            });
        }
    }

    private static void Niveles(Document doc, CatalogoRevit catalogo)
    {
        var niveles = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(n => n.Elevation);

        foreach (var n in niveles)
        {
            catalogo.Niveles.Add(new NivelJson
            {
                Nombre = n.Name ?? string.Empty,
                ElevacionM = Unidades.AMetros(n.Elevation)
            });
        }
    }

    /// <summary>Una longitud de los parametros del tipo, en metros, o <c>null</c>.</summary>
    /// <summary>
    /// La base y el peralte de un tipo, en metros: por sus parametros y, si no, por su nombre.
    /// Es lo mismo que hace <see cref="Cargables"/>; lo usa tambien «Armar por tipo».
    /// </summary>
    public static (double? AnchoM, double? PeralteM) Medidas(FamilySymbol s)
    {
        var ancho = Longitud(s, NombresDeAncho);
        var peralte = Longitud(s, NombresDePeralte);

        if (ancho is null || peralte is null)
        {
            var delNombre = MedidasPorNombre.Dos(s.Name);

            if (delNombre is not null)
            {
                ancho ??= delNombre.Value.AnchoM;
                peralte ??= delNombre.Value.PeralteM;
            }
        }

        return (ancho, peralte);
    }

    private static double? Longitud(FamilySymbol s, string[] nombres)
    {
        foreach (var n in nombres)
        {
            var p = s.LookupParameter(n);

            if (p is null || p.StorageType != StorageType.Double)
            {
                continue;
            }

            var v = p.AsDouble();

            if (v > 0)
            {
                return Unidades.AMetros(v);
            }
        }

        return null;
    }

    /// <summary>Las piezas que CadLink ya puso en este proyecto.</summary>
    /// <remarks>
    /// Se reconocen por la marca en su parametro de comentarios. Lo que no lleve esa marca no
    /// se toca nunca: puede ser trabajo hecho a mano.
    /// </remarks>
    public static List<PiezaExistente> Existentes(Document doc)
    {
        var salida = new List<PiezaExistente>();

        var categorias = new[]
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors
        };

        foreach (var cat in categorias)
        {
            var elementos = new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType();

            foreach (var e in elementos)
            {
                var comentario = e
                    .get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?
                    .AsString();

                if (!Llave.EsNuestra(comentario))
                {
                    continue;
                }

                var pieza = new PiezaExistente
                {
                    Id = e.Id.Value,
                    Llave = comentario!,
                    TipoId = e.GetTypeId().Value
                };

                // La posicion, para poder detectar que la pieza se movio en el calculo. Solo
                // la tienen las que se colocaron sobre una linea.
                if (e.Location is LocationCurve lc && lc.Curve is Line linea)
                {
                    pieza.P1 = Unidades.DePunto(linea.GetEndPoint(0));
                    pieza.P2 = Unidades.DePunto(linea.GetEndPoint(1));
                }

                salida.Add(pieza);
            }
        }

        return salida;
    }
}
