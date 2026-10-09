using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// Crea en Revit los tipos de trabe y columna de las secciones de CadLink que el proyecto no
/// tiene: duplica un tipo de la familia de concreto que ya se usa y le cambia la base y el peralte.
/// </summary>
/// <remarks>
/// Cuales faltan y como se llaman lo decide <see cref="TiposNuevos"/>, en el nucleo, con pruebas.
/// Aqui solo se toca Revit, con argumentos posicionales y sin miembros de enum escritos.
/// </remarks>
internal static class CreadorDeTipos
{
    /// <summary>Todos los tipos de columna y de trabe cargados, tengan piezas o no.</summary>
    public static List<TipoExistente> Existentes(Document doc)
    {
        var res = new List<TipoExistente>();

        foreach (var (s, clase) in Simbolos(doc))
        {
            var (b, h) = LectorDeCatalogo.Medidas(s);
            res.Add(new TipoExistente(clase, s.FamilyName ?? string.Empty, s.Name ?? string.Empty, b, h,
                Despiece.DescripcionDe(s)));
        }

        return res;
    }

    /// <summary>Crea los tipos que faltan, en una transaccion. Devuelve los nombres creados.</summary>
    public static List<string> Crear(Document doc, IReadOnlyList<TipoPorCrear> faltan, ResultadoArmado r)
    {
        var creados = new List<string>();

        if (faltan.Count == 0)
        {
            return creados;
        }

        var simbolos = Simbolos(doc);
        var usos = Usos(doc);

        using var t = new Transaction(doc, "Tipos nuevos de CadLink");
        t.Start();

        try
        {
            foreach (var f in faltan)
            {
                var plantilla = Plantilla(simbolos, usos, f.Clase);

                if (plantilla is null)
                {
                    r.Errores.Add($"«tipo nuevo»: {f.Nombre}. No hay ninguna familia de "
                                  + (f.Clase == ClasePieza.Trabe ? "armazón estructural" : "pilar estructural")
                                  + " con parámetros de base y peralte (b y h) para duplicar; carga una de concreto rectangular");
                    continue;
                }

                var enLaFamilia = simbolos
                    .Where(x => x.Simbolo.FamilyName == plantilla.FamilyName)
                    .Select(x => x.Simbolo.Name ?? string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var nombre = PlanDespiece.NombreLibre(f.Nombre, enLaFamilia);

                try
                {
                    if (plantilla.Duplicate(nombre) is not FamilySymbol nuevo)
                    {
                        r.Errores.Add($"«tipo nuevo»: {nombre}. Revit no lo pudo duplicar");
                        continue;
                    }

                    var bien = Medida(nuevo, LectorDeCatalogo.NombresDeAncho, f.BaseCm)
                               & Medida(nuevo, LectorDeCatalogo.NombresDePeralte, f.AlturaCm);

                    if (!bien)
                    {
                        r.Avisos.Add($"«tipo nuevo»: {nombre}. No se pudo escribir su base o su peralte; revísalo");
                    }

                    // Con una sola seccion de esa medida, el tipo ya sabe cual es: Descripcion = ID.
                    if (f.Unica is { } a)
                    {
                        Despiece.Propiedades(doc, nuevo.Id, a, r);
                    }

                    simbolos.Add((nuevo, f.Clase));
                    creados.Add($"{plantilla.FamilyName} : {nombre}");
                }
                catch (Exception ex)
                {
                    r.Errores.Add($"«tipo nuevo»: {nombre}: {ex.Message}");
                }
            }

            t.Commit();
        }
        catch (Exception e)
        {
            t.RollBack();
            creados.Clear();
            r.Errores.Add("No se crearon los tipos: " + e.Message);
        }

        return creados;
    }

    private static List<(FamilySymbol Simbolo, ClasePieza Clase)> Simbolos(Document doc)
    {
        var res = new List<(FamilySymbol, ClasePieza)>();

        foreach (var (cat, clase) in new[]
                 {
                     (BuiltInCategory.OST_StructuralColumns, ClasePieza.Columna),
                     (BuiltInCategory.OST_StructuralFraming, ClasePieza.Trabe)
                 })
        {
            foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(cat))
            {
                if (e is FamilySymbol s)
                {
                    res.Add((s, clase));
                }
            }
        }

        return res;
    }

    /// <summary>Cuantas piezas hay dibujadas de cada familia: la que mas se usa es la de la oficina.</summary>
    private static Dictionary<string, int> Usos(Document doc)
    {
        var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming })
        {
            foreach (var e in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
            {
                if (doc.GetElement(e.GetTypeId()) is FamilySymbol s && s.FamilyName is { } fam)
                {
                    res[fam] = res.TryGetValue(fam, out var n) ? n + 1 : 1;
                }
            }
        }

        return res;
    }

    /// <summary>
    /// El tipo a duplicar: de la clase pedida, con base y peralte ESCRIBIBLES -una familia de
    /// acero con bf y d de solo lectura no sirve-, prefiriendo la familia de concreto y, entre
    /// ellas, la que mas piezas tiene en el proyecto.
    /// </summary>
    private static FamilySymbol? Plantilla(
        List<(FamilySymbol Simbolo, ClasePieza Clase)> simbolos, Dictionary<string, int> usos, ClasePieza clase) =>
        simbolos
            .Where(x => x.Clase == clase
                        && Escribible(x.Simbolo, LectorDeCatalogo.NombresDeAncho) is not null
                        && Escribible(x.Simbolo, LectorDeCatalogo.NombresDePeralte) is not null)
            .Select(x => x.Simbolo)
            .OrderByDescending(s => DeConcreto(s.FamilyName))
            .ThenByDescending(s => usos.TryGetValue(s.FamilyName ?? string.Empty, out var n) ? n : 0)
            .FirstOrDefault();

    private static bool DeConcreto(string? familia)
    {
        var f = (familia ?? string.Empty).ToUpperInvariant();
        return f.Contains("CONCRET") || f.Contains("HORMIG") || f.Contains("RECTANG");
    }

    private static Parameter? Escribible(FamilySymbol s, string[] nombres)
    {
        foreach (var n in nombres)
        {
            if (s.LookupParameter(n) is { } p && p.StorageType == StorageType.Double && !p.IsReadOnly)
            {
                return p;
            }
        }

        return null;
    }

    private static bool Medida(FamilySymbol s, string[] nombres, double cm)
    {
        try
        {
            return Escribible(s, nombres) is { } p && p.Set(Unidades.AInternas(cm / 100));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
