using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Lo que salio de modelar.</summary>
public sealed class ResultadoModelado
{
    public int Creadas { get; set; }

    public int Actualizadas { get; set; }

    public int Saltadas { get; set; }

    public int NivelesCreados { get; set; }

    /// <summary>Lo que fallo, pieza por pieza y con el motivo.</summary>
    public List<string> Errores { get; } = new();

    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Ejecuta el plan: crea y actualiza los elementos de Revit.
/// </summary>
/// <remarks>
/// <para>
/// Todo va en UNA transaccion, para que un solo Ctrl+Z deshaga la importacion entera. Si se
/// abriera una por pieza, deshacer una importacion de tres mil elementos serian tres mil
/// Ctrl+Z.
/// </para>
/// <para>
/// Y cada pieza va en su propio try: una seccion que Revit rechace -por un tipo que no admite
/// esa geometria, por ejemplo- no debe tirar las otras dos mil novecientas. Se apunta el
/// motivo y se sigue.
/// </para>
/// </remarks>
internal static class Modelador
{
    public static ResultadoModelado Ejecutar(Document doc, Plan plan)
    {
        var r = new ResultadoModelado();

        r.Avisos.AddRange(plan.Avisos);

        using var t = new Transaction(doc, "Importar modelo de CadLink");
        t.Start();

        try
        {
            var niveles = CrearNivelesQueFaltan(doc, plan, r);

            foreach (var paso in plan.Pasos)
            {
                switch (paso.Accion)
                {
                    case Accion.Crear:
                        Crear(doc, paso, niveles, r);
                        break;

                    case Accion.Actualizar:
                        Actualizar(doc, paso, r);
                        break;

                    default:
                        // DejarIgual, SinMapeo y Sobra no tocan nada.
                        r.Saltadas++;
                        break;
                }
            }

            t.Commit();
        }
        catch (Exception e)
        {
            // Si algo revienta fuera del try de cada pieza, se deshace TODO. Es preferible a
            // dejar el modelo a medio importar sin saber por donde se quedo.
            t.RollBack();

            r.Errores.Add("Se deshizo la importacion completa por un fallo general: " + e.Message);
        }

        return r;
    }

    // ==================================================================
    //  Niveles
    // ==================================================================

    private static Dictionary<string, Level> CrearNivelesQueFaltan(
        Document doc, Plan plan, ResultadoModelado r)
    {
        var porNombre = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);

        foreach (var n in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
        {
            porNombre[n.Name] = n;
        }

        foreach (var falta in plan.NivelesQueFaltan)
        {
            if (porNombre.ContainsKey(falta.Nombre))
            {
                continue;
            }

            try
            {
                var nuevo = Level.Create(doc, Unidades.AInternas(falta.ElevacionM));

                // Ponerle el nombre puede fallar si ya existe uno asi con otra cota. No es
                // grave: el nivel ya esta creado a la cota correcta, que es lo que importa
                // para colgar las piezas.
                try
                {
                    nuevo.Name = falta.Nombre;
                }
                catch (Exception)
                {
                    r.Avisos.Add(
                        $"El nivel a {Niveles.Cm(falta.ElevacionM)} cm se creo, pero no se le "
                        + $"pudo poner el nombre «{falta.Nombre}»: ya hay otro asi.");
                }

                porNombre[falta.Nombre] = nuevo;
                r.NivelesCreados++;
            }
            catch (Exception e)
            {
                r.Errores.Add($"No se pudo crear el nivel «{falta.Nombre}»: {e.Message}");
            }
        }

        return porNombre;
    }

    private static Level? Nivel(Dictionary<string, Level> niveles, string nombre) =>
        niveles.TryGetValue(nombre ?? string.Empty, out var n) ? n : null;

    // ==================================================================
    //  Crear
    // ==================================================================

    private static void Crear(
        Document doc, Paso paso, Dictionary<string, Level> niveles, ResultadoModelado r)
    {
        try
        {
            var nivel = Nivel(niveles, paso.NivelRevit);

            if (nivel is null)
            {
                r.Errores.Add($"«{paso.Llave}»: no se encontro el nivel «{paso.NivelRevit}».");

                return;
            }

            var tipoId = new ElementId(paso.Tipo!.Id);

            Element? hecho = paso.Barra is not null
                ? CrearBarra(doc, paso, nivel, tipoId)
                : CrearPano(doc, paso, nivel, tipoId);

            if (hecho is null)
            {
                r.Errores.Add($"«{paso.Llave}»: Revit no devolvio ningun elemento.");

                return;
            }

            // La marca, que es como se reconoce la pieza la proxima vez.
            hecho.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(paso.Llave);

            r.Creadas++;
        }
        catch (Exception e)
        {
            r.Errores.Add($"«{paso.Llave}»: {e.Message}");
        }
    }

    private static Element? CrearBarra(Document doc, Paso paso, Level nivel, ElementId tipoId)
    {
        var b = paso.Barra!;

        if (doc.GetElement(tipoId) is not FamilySymbol simbolo)
        {
            throw new InvalidOperationException(
                "El tipo elegido ya no existe en el proyecto.");
        }

        // Un tipo sin activar no se puede colocar, y el mensaje que da Revit no lo dice.
        if (!simbolo.IsActive)
        {
            simbolo.Activate();
            doc.Regenerate();
        }

        var linea = Line.CreateBound(Unidades.Punto(b.P1), Unidades.Punto(b.P2));

        var comoQue = b.Clase switch
        {
            ClasePieza.Columna => StructuralType.Column,
            ClasePieza.Diagonal => StructuralType.Brace,
            _ => StructuralType.Beam
        };

        var inst = doc.Create.NewFamilyInstance(linea, simbolo, nivel, comoQue);

        // El giro de la seccion. En Revit es el parametro "Rotacion de la seccion", en
        // RADIANES; en el modelo viene en grados.
        if (Math.Abs(b.AnguloGrados) > 1e-9)
        {
            inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)
                ?.Set(b.AnguloGrados * Math.PI / 180.0);
        }

        return inst;
    }

    private static Element? CrearPano(Document doc, Paso paso, Level nivel, ElementId tipoId)
    {
        var p = paso.Pano!;

        if (p.Vertices.Count < 3)
        {
            throw new InvalidOperationException(
                "El contorno tiene menos de tres vertices.");
        }

        var curvas = Contorno(p);

        // Se mira la categoria del TIPO ELEGIDO, no la clase que trae el modelo. El usuario
        // puede cambiar la categoria en el cuadro -un paño que ETABS trae como muro puede
        // tener que ser un suelo-, y si aqui se decidiera por la clase, esa eleccion se
        // ignoraria y se llamaria a la API equivocada.
        if (paso.Tipo!.Categoria == CategoriaRevit.Muro)
        {
            // Wall.Create con el CONTORNO, no con una linea y una altura: un pano de muro de
            // ETABS puede ser cualquier poligono, y con la version de linea mas altura se
            // perderia su forma en cuanto no sea un rectangulo.
            return Wall.Create(doc, curvas, tipoId, nivel.Id, structural: true);
        }

        if (paso.Tipo.Categoria != CategoriaRevit.Piso)
        {
            // Una categoria que no sabe hacer un paño. Se dice en vez de dejar que Revit
            // conteste algo incomprensible.
            throw new InvalidOperationException(
                "El tipo elegido es de " + Categorias.Nombre(paso.Tipo.Categoria).ToLowerInvariant()
                + ", y un paño solo se puede modelar como muro o como suelo. "
                + "Cambia la categoria de esta seccion en el cuadro de mapeo.");
        }

        var lazo = CurveLoop.Create(curvas);

        return Floor.Create(doc, new List<CurveLoop> { lazo }, tipoId, nivel.Id);
    }

    /// <summary>El contorno del pano como curvas cerradas.</summary>
    private static IList<Curve> Contorno(PanoJson p)
    {
        var puntos = p.Vertices.Select(Unidades.Punto).ToList();
        var curvas = new List<Curve>();

        for (var i = 0; i < puntos.Count; i++)
        {
            var a = puntos[i];
            var b = puntos[(i + 1) % puntos.Count];

            // Un lado de largo cero hace que Revit rechace el contorno entero. Los vertices
            // repetidos ya se limpian al exportar, pero un modelo puede traer dos puntos a
            // una decima de milimetro y eso tambien cuenta como cero para Revit.
            if (a.DistanceTo(b) < Unidades.AInternas(0.001))
            {
                continue;
            }

            curvas.Add(Line.CreateBound(a, b));
        }

        if (curvas.Count < 3)
        {
            throw new InvalidOperationException(
                "El contorno se queda en menos de tres lados al quitar los de largo cero.");
        }

        return curvas;
    }

    // ==================================================================
    //  Actualizar
    // ==================================================================

    private static void Actualizar(Document doc, Paso paso, ResultadoModelado r)
    {
        try
        {
            var e = doc.GetElement(new ElementId(paso.IdExistente));

            if (e is null)
            {
                r.Errores.Add($"«{paso.Llave}»: la pieza ya no esta en el proyecto.");

                return;
            }

            var tipoId = new ElementId(paso.Tipo!.Id);

            if (e.GetTypeId() != tipoId)
            {
                if (doc.GetElement(tipoId) is FamilySymbol s && !s.IsActive)
                {
                    s.Activate();
                    doc.Regenerate();
                }

                e.ChangeTypeId(tipoId);
            }

            // Recolocar, solo si la pieza esta sobre una linea y el modelo la movio.
            if (paso.Barra is not null && e.Location is LocationCurve lc)
            {
                var nueva = Line.CreateBound(
                    Unidades.Punto(paso.Barra.P1), Unidades.Punto(paso.Barra.P2));

                // Si Revit no deja mover la pieza -porque esta unida a otra, por ejemplo- se
                // deja donde esta y se dice. Forzarlo rompe las uniones.
                try
                {
                    lc.Curve = nueva;
                }
                catch (Exception)
                {
                    r.Avisos.Add(
                        $"«{paso.Llave}» cambio de sitio en el calculo, pero Revit no dejo "
                        + "moverla. Revisa si esta unida a otra pieza.");
                }
            }

            r.Actualizadas++;
        }
        catch (Exception e)
        {
            r.Errores.Add($"«{paso.Llave}»: {e.Message}");
        }
    }
}
