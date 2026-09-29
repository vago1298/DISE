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

        // ---- Que el commit NO se pueda abortar ----
        //
        // Sin esto, Revit levanta su propio cuadro al confirmar. Los avisos se podrian
        // descartar, pero un error marcado "cannot be ignored" -por ejemplo
        // "Position of end cut planes has resulted in a slanted column without any geometry"-
        // deja el cuadro con el OK apagado y solo Cancel, y al cancelar SE DESHACE TODA LA
        // TRANSACCION. El informe dice que se crearon cuatrocientas cincuenta piezas y en el
        // modelo no aparece ninguna.
        //
        // Con este manejador los avisos se silencian, y el elemento que causa un error se
        // borra para que el resto SI se confirme. Lo que se silencio y lo que se borro se
        // cuenta y se dice en el informe: no se esconde nada, se evita perderlo todo.
        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();

        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

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

            if (manejador.AvisosSilenciados > 0)
            {
                r.Avisos.Add($"Revit levanto {manejador.AvisosSilenciados} aviso(s) al "
                             + "confirmar y se descartaron para no interrumpir la importacion.");
            }

            if (manejador.ElementosBorrados > 0)
            {
                r.Errores.Add($"«Revit»: {manejador.ElementosBorrados} elemento(s) se borraron "
                              + "porque Revit dio un error que no se puede ignorar. "
                              + "El resto se importo igual.");
            }

            foreach (var m in manejador.Motivos.Take(4))
            {
                r.Avisos.Add("Revit dijo: " + m);
            }
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
            // Un nivel sin nombre no deberia existir, pero la API lo declara opcional y un
            // nulo aqui reventaria al indexar el diccionario.
            var nombre = n.Name;

            if (!string.IsNullOrWhiteSpace(nombre))
            {
                porNombre[nombre] = n;
            }
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

        FamilyInstance inst;

        if (b.Clase == ClasePieza.Columna && Colocacion.EsVertical(b))
        {
            // ---- La columna vertical se coloca POR NIVELES, no por su linea ----
            //
            // Pasarle la linea de sus dos extremos usa la API de columna INCLINADA, y de ahi
            // sale el error que Revit marca como imposible de ignorar:
            //
            //   "Position of end cut planes has resulted in a slanted column without any
            //    geometry."
            //
            // Ademas ETABS asigna la columna al nivel al que SUBE, no al que arranca, asi que
            // usando ese nivel como base la columna quedaba con la base por encima de la punta.
            //
            // Asi se modela una columna en Revit a mano: en su punto, con nivel de base y de
            // punta y sus desfases.
            var abajo = Math.Min(b.P1.Z, b.P2.Z);
            var punto = new XYZ(
                Unidades.AInternas(b.P1.X), Unidades.AInternas(b.P1.Y), Unidades.AInternas(abajo));

            inst = doc.Create.NewFamilyInstance(punto, simbolo, nivel, StructuralType.Column);

            AtarAColumna(doc, inst, b, nivel);
        }
        else
        {
            var linea = Line.CreateBound(Unidades.Punto(b.P1), Unidades.Punto(b.P2));

            var comoQue = b.Clase switch
            {
                // Una columna que NO esta a plomo se sigue creando por su linea, porque no hay
                // otra forma de darle su inclinacion. Es el caso donde puede volver a salir el
                // error de los planos de corte, y por eso va dentro del try de cada pieza: si
                // falla, falla ella sola y se dice.
                ClasePieza.Columna => StructuralType.Column,
                ClasePieza.Diagonal => StructuralType.Brace,
                _ => StructuralType.Beam
            };

            inst = doc.Create.NewFamilyInstance(linea, simbolo, nivel, comoQue);
        }

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

        // ---- La losa, conservando su pendiente ----
        //
        // Floor.Create solo acepta un contorno PLANO y paralelo a XY, asi que una losa
        // inclinada no se puede crear de una vez. Pero aplanarla y dejarla asi pierde el dato:
        // una losa de entrepiso con pendiente modelada en el calculo tiene que salir con su
        // pendiente.
        //
        // Se hace en dos pasos: plana en su cota mas baja, y despues cada vertice a la suya con
        // el editor de forma de la losa.
        var forma = Losas.Preparar(p.Vertices);

        if (forma.EnPlanta.Count < 3)
        {
            throw new InvalidOperationException(
                "El contorno se queda en menos de tres vertices distintos.");
        }

        var enPlanta = new List<Curve>();

        for (var i = 0; i < forma.EnPlanta.Count; i++)
        {
            var a = Unidades.Punto(forma.EnPlanta[i]);
            var z = Unidades.Punto(forma.EnPlanta[(i + 1) % forma.EnPlanta.Count]);

            if (a.DistanceTo(z) >= Unidades.AInternas(0.001))
            {
                enPlanta.Add(Line.CreateBound(a, z));
            }
        }

        if (enPlanta.Count < 3)
        {
            throw new InvalidOperationException(
                "El contorno se queda en menos de tres lados al quitar los de largo cero.");
        }

        var lazo = CurveLoop.Create(enPlanta);

        if (forma.EsPlana)
        {
            return Floor.Create(doc, new List<CurveLoop> { lazo }, tipoId, nivel.Id);
        }

        // La FLECHA DE PENDIENTE: una linea horizontal que va del vertice mas bajo hacia la
        // planta del mas alto, mas el angulo en radianes. Es la forma documentada de crear un
        // suelo inclinado, y tiene que ser horizontal: la inclinacion la da el angulo, no la
        // linea.
        var cota = Unidades.AInternas(forma.ZBaseM);

        var flecha = Line.CreateBound(
            new XYZ(Unidades.AInternas(forma.ColaX), Unidades.AInternas(forma.ColaY), cota),
            new XYZ(Unidades.AInternas(forma.PuntaX), Unidades.AInternas(forma.PuntaY), cota));

        return Floor.Create(
            doc, new List<CurveLoop> { lazo }, tipoId, nivel.Id,
            structural: true, flecha, forma.AnguloRad);
    }

    /// <summary>Ata la columna a su nivel de base y su nivel de punta, con sus desfases.</summary>
    /// <remarks>
    /// Es lo que le da altura. Sin esto la columna se queda con la altura por omision del tipo
    /// y no con la del modelo de calculo.
    /// </remarks>
    private static void AtarAColumna(Document doc, FamilyInstance inst, BarraJson b, Level nivel)
    {
        var deRevit = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToList();

        var comoJson = deRevit
            .Select(n => new NivelJson
            {
                Nombre = n.Name ?? string.Empty,
                ElevacionM = Unidades.AMetros(n.Elevation)
            })
            .ToList();

        var donde = Colocacion.De(b.P1.Z, b.P2.Z, comoJson);

        var nBase = deRevit.FirstOrDefault(n => n.Name == donde.NivelBase) ?? nivel;
        var nPunta = deRevit.FirstOrDefault(n => n.Name == donde.NivelPunta) ?? nivel;

        inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.Set(nBase.Id);
        inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)
            ?.Set(Unidades.AInternas(donde.DesfaseBaseM));

        inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(nPunta.Id);
        inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)
            ?.Set(Unidades.AInternas(donde.DesfasePuntaM));
    }

    /// <summary>El contorno de un pano como curvas cerradas, en sus cotas reales.</summary>
    /// <remarks>
    /// Lo usan los MUROS, que se crean con su contorno tal cual. Las losas no pasan por aqui:
    /// necesitan el contorno aplanado al plano de apoyo, y eso lo prepara <c>Losas</c>.
    /// </remarks>
    private static IList<Curve> Contorno(PanoJson p)
    {
        var puntos = p.Vertices.Select(Unidades.Punto).ToList();
        var curvas = new List<Curve>();

        for (var i = 0; i < puntos.Count; i++)
        {
            var a = puntos[i];
            var b = puntos[(i + 1) % puntos.Count];

            // Un lado de largo cero hace que Revit rechace el contorno entero. Los vertices
            // repetidos ya se limpian al exportar, pero un modelo puede traer dos puntos a una
            // decima de milimetro y eso tambien cuenta como cero para Revit.
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
