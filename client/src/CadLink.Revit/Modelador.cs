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

    /// <summary>Rejillas de eje creadas.</summary>
    public int EjesCreados { get; set; }

    public int NivelesCreados { get; set; }

    /// <summary>Lo que fallo, pieza por pieza y con el motivo.</summary>
    public List<string> Errores { get; } = new();

    public List<string> Avisos { get; } = new();

    /// <summary>Muros llevados al paño de las columnas modeladas, y puntas sin columna.</summary>
    internal int MurosAPano { get; set; }

    internal int PuntasSinColumna { get; set; }

    /// <summary>Las vigas creadas o actualizadas, para dejarlas en su nivel al final.</summary>
    internal List<(FamilyInstance Viga, BarraJson Barra)> Vigas { get; } = new();
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

            CrearEjes(doc, plan, r);

            // LAS BARRAS PRIMERO y despues los paños: los muros se llevan al paño de las
            // columnas que Revit YA dibujo, medidas, no de las que se suponen.
            List<AjusteDeMuros.HuellaColumna>? huellas = null;

            foreach (var paso in plan.Pasos.OrderBy(p => p.Pano is null ? 0 : 1))
            {
                if (paso.Pano is { Clase: ClasePieza.Muro } muro && paso.Accion == Accion.Crear
                    && paso.Tipo?.Categoria == CategoriaRevit.Muro)
                {
                    huellas ??= Huellas(doc);
                    APanoDeColumnas(muro, huellas, r);
                }

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

            // ---- Las vigas en su nivel, con el desfase Z en cero ----
            //
            // Otra vez al final, ya con todo regenerado: las uniones con columnas y muros que
            // hace Revit al regenerar pueden volver a tocar la viga. Y se comprueba: si alguna
            // se queda con desfase, se dice como error, que es lo unico que sale en pantalla.
            if (r.Vigas.Count > 0)
            {
                doc.Regenerate();

                foreach (var (viga, barra) in r.Vigas)
                {
                    Nivelar(doc, viga, barra);
                }

                doc.Regenerate();

                var conDesfase = r.Vigas.Count(v => v.Viga.IsValidObject && Math.Abs(DesfaseZ(v.Viga)) > 1e-4);

                if (conDesfase > 0)
                {
                    r.Errores.Add($"«desfase Z»: {conDesfase} viga(s) se quedaron con Valor de desfase Z "
                                  + "distinto de cero; su familia no deja cambiarlo");
                }
            }

            if (r.MurosAPano + r.PuntasSinColumna > 0)
            {
                r.Avisos.Add($"{r.MurosAPano} muro(s) se llevaron al paño de las columnas modeladas"
                             + (r.PuntasSinColumna > 0 ? $"; {r.PuntasSinColumna} punta(s) de muro no tienen columna y se quedaron en su sitio" : string.Empty));
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

    // ==================================================================
    //  Ejes
    // ==================================================================

    /// <summary>Crea la malla de ejes del modelo, con sus nombres.</summary>
    /// <remarks>
    /// <para>
    /// Los ejes llegan YA COLOCADOS: los extremos corridos a paño y los interiores sobre el
    /// eje, decidido en el nucleo con el mismo criterio que el plano de AutoCAD. Aqui solo se
    /// dibujan, que es la parte que necesita Revit.
    /// </para>
    /// <para>
    /// Un eje cuyo nombre ya existe en el proyecto NO se vuelve a crear. Es lo que permite
    /// reimportar sin acabar con seis rejillas llamadas «1» encima unas de otras; Revit ademas
    /// no admite dos con el mismo nombre.
    /// </para>
    /// </remarks>
    private static void CrearEjes(Document doc, Plan plan, ResultadoModelado r)
    {
        var c = plan.Cuadricula;

        if (c is null || !c.Hay)
        {
            return;
        }

        var yaEstan = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            if (!string.IsNullOrWhiteSpace(g.Name))
            {
                yaEstan.Add(g.Name!);
            }
        }

        // Hasta donde llega cada linea. Se mira la geometria del modelo y tambien los propios
        // ejes, para que un eje que cae fuera del edificio salga con su linea igual de larga.
        var xs = new List<double>();
        var ys = new List<double>();

        var modelo = plan.Modelo;

        if (modelo is null)
        {
            return;
        }

        foreach (var b in modelo.Barras)
        {
            xs.Add(b.P1.X);
            xs.Add(b.P2.X);
            ys.Add(b.P1.Y);
            ys.Add(b.P2.Y);
        }

        foreach (var p in modelo.Panos)
        {
            foreach (var v in p.Vertices)
            {
                xs.Add(v.X);
                ys.Add(v.Y);
            }
        }

        xs.AddRange(c.X.Select(e => e.Ordenada));
        ys.AddRange(c.Y.Select(e => e.Ordenada));

        if (xs.Count == 0 || ys.Count == 0)
        {
            return;
        }

        // Lo que sobresale la linea del edificio, para que la burbuja no caiga encima.
        const double margenM = 2.0;

        var xMin = xs.Min() - margenM;
        var xMax = xs.Max() + margenM;
        var yMin = ys.Min() - margenM;
        var yMax = ys.Max() + margenM;

        // A cota cero: Grid.Create pide la linea en un plano horizontal.
        XYZ P(double x, double y) =>
            new(Unidades.AInternas(x), Unidades.AInternas(y), 0);

        var nuevas = new List<Grid>();

        void Uno(EjeJson e, XYZ a, XYZ z)
        {
            var id = (e.Id ?? string.Empty).Trim();

            if (id.Length > 0 && yaEstan.Contains(id))
            {
                return;
            }

            try
            {
                var rejilla = Grid.Create(doc, Line.CreateBound(a, z));

                nuevas.Add(rejilla);

                if (id.Length > 0)
                {
                    try
                    {
                        rejilla.Name = id;
                        yaEstan.Add(id);
                    }
                    catch (Exception)
                    {
                        // El nombre lo puede rechazar Revit. La rejilla ya esta, que es lo que
                        // importa; se queda con el nombre que le puso Revit.
                        r.Avisos.Add($"«eje {id}»: la rejilla se creo pero no se le pudo poner "
                                     + "ese nombre");
                    }
                }

                r.EjesCreados++;
            }
            catch (Exception ex)
            {
                r.Errores.Add($"«eje {id}»: no se pudo crear: {ex.Message}");
            }
        }

        foreach (var e in c.X)
        {
            Uno(e, P(e.Ordenada, yMin), P(e.Ordenada, yMax));
        }

        foreach (var e in c.Y)
        {
            Uno(e, P(xMin, e.Ordenada), P(xMax, e.Ordenada));
        }

        BurbujasEnLosCuatroLados(doc, nuevas, r);
    }

    /// <summary>Pone la burbuja de cada eje en SUS DOS EXTREMOS.</summary>
    /// <remarks>
    /// <para>
    /// Con la burbuja en un solo extremo, el plano queda rotulado por dos lados. Con los dos
    /// extremos de los ejes verticales y de los horizontales, queda rotulado por los
    /// <b>cuatro</b>, que es como se entrega un plano estructural: se puede leer la retícula
    /// desde cualquier borde de la hoja sin cruzar el dibujo con la vista.
    /// </para>
    /// <para>
    /// La visibilidad de la burbuja es <b>por vista</b>, no del eje, asi que hay que ponerla en
    /// cada vista de planta. Se hace en todas las que no sean plantilla.
    /// </para>
    /// </remarks>
    private static void BurbujasEnLosCuatroLados(
        Document doc, List<Grid> rejillas, ResultadoModelado r)
    {
        if (rejillas.Count == 0)
        {
            return;
        }

        // EL TIPO DE EJE, PRIMERO. Encender la burbuja vista por vista no basto en el proyecto
        // del usuario: los ejes salieron con la burbuja en UN extremo. «Símbolo de vista en
        // planta extremo 1 / 2 (predeterminado)» es del tipo y vale en todas las vistas, las de
        // ahora y las que se creen despues.
        foreach (var idTipo in rejillas.Select(g => g.GetTypeId()).Distinct())
        {
            if (doc.GetElement(idTipo) is not ElementType tipo)
            {
                continue;
            }

            foreach (var (interno, nombres) in new[]
                     {
                         ("GRID_BUBBLE_END_1", new[] { "Plan View Symbols End 1 (Default)",
                             "Símbolos de vista de plano en extremo 1 (predeterminado)",
                             "Símbolo de vista de plano extremo 1 (predeterminado)" }),
                         ("GRID_BUBBLE_END_2", new[] { "Plan View Symbols End 2 (Default)",
                             "Símbolos de vista de plano en extremo 2 (predeterminado)",
                             "Símbolo de vista de plano extremo 2 (predeterminado)" })
                     })
            {
                try
                {
                    var p = Despiece.Parametro(tipo, new[] { interno }, nombres);

                    if (p is null || p.IsReadOnly || !p.Set(1))
                    {
                        r.Errores.Add($"«ejes»: no se pudo encender la burbuja en los dos extremos del tipo «{tipo.Name}»");
                    }
                }
                catch (Exception ex)
                {
                    r.Errores.Add($"«ejes»: tipo «{tipo.Name}»: {ex.Message}");
                }
            }
        }

        // Una sola vez: hasta que Revit no regenera, un eje recien creado no esta en las vistas.
        doc.Regenerate();

        var plantas = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(v => !v.IsTemplate)
            .ToList();

        if (plantas.Count == 0)
        {
            return;
        }

        // Y en cada vista, por si alguna tiene la burbuja cambiada a mano.
        foreach (var rejilla in rejillas)
        {
            foreach (var vista in plantas)
            {
                try
                {
                    rejilla.ShowBubbleInView(DatumEnds.End0, vista);
                    rejilla.ShowBubbleInView(DatumEnds.End1, vista);
                }
                catch (Exception)
                {
                    // Hay vistas donde el eje no llega a verse, y Revit no deja tocar su
                    // burbuja. Con el tipo ya encendido no es un problema.
                }
            }
        }
    }

    /// <summary>
    /// Lo que ocupa cada columna del proyecto -las de CadLink y las que ya estaban-, medido sobre
    /// su solido, en metros.
    /// </summary>
    private static List<AjusteDeMuros.HuellaColumna> Huellas(Document doc)
    {
        var res = new List<AjusteDeMuros.HuellaColumna>();

        // Las columnas recien creadas no tienen geometria hasta que Revit regenera.
        doc.Regenerate();

        foreach (var e in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType())
        {
            var puntos = Geometria.PuntosDelSolido(e);

            if (puntos.Count == 0)
            {
                continue;
            }

            res.Add(new AjusteDeMuros.HuellaColumna(
                puntos.Select(p => (Unidades.AMetros(p.X), Unidades.AMetros(p.Y))).ToList(),
                Unidades.AMetros(puntos.Min(p => p.Z)),
                Unidades.AMetros(puntos.Max(p => p.Z))));
        }

        return res;
    }

    /// <summary>El muro, al paño de las columnas que tiene en sus puntas. Se cambia en su sitio.</summary>
    private static void APanoDeColumnas(PanoJson muro, List<AjusteDeMuros.HuellaColumna> huellas, ResultadoModelado r)
    {
        var a = AjusteDeMuros.APanoDeColumnas(muro.Vertices, huellas);

        if (a is null)
        {
            return;
        }

        r.PuntasSinColumna += 2 - a.Puntas;

        if (a.Cambio)
        {
            muro.Vertices.Clear();
            muro.Vertices.AddRange(a.Contorno);
            r.MurosAPano++;
        }
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
                ? CrearBarra(doc, paso, nivel, tipoId, r)
                : CrearPano(doc, paso, NivelDePano(doc, paso, nivel), tipoId);

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

    private static Element? CrearBarra(
        Document doc, Paso paso, Level nivel, ElementId tipoId, ResultadoModelado r)
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

            // ---- Y AHORA SE GIRA, DE VERDAD ----
            //
            // Aqui estaba el fallo de los castillos. Mas abajo se escribia
            // STRUCTURAL_BEND_DIR_ANGLE -"Rotacion de la seccion"-, pero ese parametro es de
            // las piezas que Revit define por una CURVA: vigas, arriostres y columnas
            // inclinadas. Una columna a plomo colocada por punto y niveles no lo tiene, asi que
            // get_Parameter devolvia null, el «?.» se lo tragaba y no pasaba nada: ni giro, ni
            // excepcion, ni aviso. Por eso las piezas inclinadas salian bien y los castillos a
            // plomo -que son casi todos- salian girados.
            //
            // A una columna colocada por punto se la gira como en AutoCAD: rotando la pieza
            // alrededor de su eje vertical.
            var giro = Orientacion.GiroRad(b, paso.Tipo);

            if (Orientacion.Vale(giro))
            {
                var eje = Line.CreateBound(punto, punto + XYZ.BasisZ);

                ElementTransformUtils.RotateElement(doc, inst.Id, eje, giro);
            }

            return inst;
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

        // El giro de la seccion, SOLO para las piezas que Revit define por una curva: vigas,
        // arriostres y columnas inclinadas. Son las unicas que tienen este parametro. La
        // columna a plomo se gira arriba, rotando la pieza, porque para ella esto es un no-op.
        var giroCurva = Orientacion.GiroRad(b, paso.Tipo);

        if (Orientacion.Vale(giroCurva))
        {
            inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)?.Set(giroCurva);
        }

        JustificarComoEnEtabs(inst, b);

        if (b.Clase == ClasePieza.Trabe)
        {
            Nivelar(doc, inst, b);
            r.Vigas.Add((inst, b));
        }

        return inst;
    }

    /// <summary>
    /// La trabe como se modela a mano: en su nivel, con su cara de arriba en la cota de la linea
    /// y el <b>Valor de desfase Z en cero</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidio: «a todas las vigas les das ese offset en z que no debe ir, deben ir normales».
    /// Las trabes salian con un desfase Z (0.60 m en el modelo del usuario), y con la
    /// justificacion arriba eso las dejaba fuera de su sitio y con un dato que nadie pone.
    /// </para>
    /// <para>
    /// Se ata al nivel MAS CERCANO a su linea -ETABS la asigna a otro piso a veces- y la altura
    /// se le da con los desfases de nivel de sus dos puntas, que es lo normal en Revit. Por
    /// nombre en texto, como las propiedades de tipo.
    /// </para>
    /// </remarks>
    private static void Nivelar(Document doc, FamilyInstance inst, BarraJson b)
    {
        // CADA PARAMETRO EN SU PROPIO TRY. Aqui estaba el «aun pones ese desfase»: el Nivel de
        // referencia de una viga ya colocada es de SOLO LECTURA en Revit, su Set lanzaba, el try
        // unico se lo tragaba y el Valor de desfase Z -que iba despues- no se llegaba a escribir.
        Level? nivel = null;

        try
        {
            var deRevit = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var comoJson = deRevit
                .Select(n => new NivelJson { Nombre = n.Name ?? string.Empty, ElevacionM = Unidades.AMetros(n.Elevation) })
                .ToList();

            var (nombre, _) = Colocacion.NivelDePano(Math.Min(b.P1.Z, b.P2.Z), comoJson);
            nivel = deRevit.FirstOrDefault(n => n.Name == nombre);
        }
        catch (Exception)
        {
            // Sin niveles que leer se deja el que le puso Revit.
        }

        if (nivel is not null)
        {
            try
            {
                if (Despiece.Parametro(inst, new[] { "INSTANCE_REFERENCE_LEVEL_PARAM" },
                        new[] { "Nivel de referencia", "Reference Level" }) is { IsReadOnly: false } pn)
                {
                    pn.Set(nivel.Id);
                }
            }
            catch (Exception)
            {
                // Se queda en el nivel con que se creo, que es el del piso de ETABS.
            }
        }

        // El desfase Z, SIEMPRE en cero: el uniforme y el de cada punta, por si la familia tiene
        // la justificacion yz independiente. Y la justificacion yz, uniforme.
        Poner(inst, "YZ_JUSTIFICATION", new[] { "Justificación yz", "yz Justification" }, 0, entero: true);
        Poner(inst, "Z_OFFSET_VALUE", new[] { "Valor de desfase Z", "z Offset Value", "Z Offset Value" }, 0);
        Poner(inst, "START_Z_OFFSET_VALUE", new[] { "Valor de desfase Z inicial", "Start z Offset Value" }, 0);
        Poner(inst, "END_Z_OFFSET_VALUE", new[] { "Valor de desfase Z final", "End z Offset Value" }, 0);

        // La altura, con los desfases de nivel de las puntas, medidos contra el nivel que de
        // verdad tiene la viga. Lo que esta a menos de 2 cm del nivel, EN el nivel.
        var elev = 0.0;

        try
        {
            var idNivel = Despiece.Parametro(inst, new[] { "INSTANCE_REFERENCE_LEVEL_PARAM" },
                new[] { "Nivel de referencia", "Reference Level" })?.AsElementId();
            var nivelReal = (idNivel is null ? null : doc.GetElement(idNivel) as Level) ?? nivel;

            if (nivelReal is null)
            {
                return;
            }

            elev = Unidades.AMetros(nivelReal.Elevation);
        }
        catch (Exception)
        {
            return;
        }

        Poner(inst, "STRUCTURAL_BEAM_END0_ELEVATION",
            new[] { "Desfase de nivel inicial", "Start Level Offset" }, AlNivel(b.P1.Z - elev));
        Poner(inst, "STRUCTURAL_BEAM_END1_ELEVATION",
            new[] { "Desfase de nivel final", "End Level Offset" }, AlNivel(b.P2.Z - elev));
    }

    /// <summary>Un desfase de menos de 2 cm es ruido del calculo: la viga va en el nivel.</summary>
    internal static double AlNivel(double desfaseM) => Math.Abs(desfaseM) < 0.02 ? 0 : desfaseM;

    /// <summary>Lo que quedo en el Valor de desfase Z, en m; 0 si la familia no lo tiene.</summary>
    private static double DesfaseZ(FamilyInstance inst)
    {
        try
        {
            var p = Despiece.Parametro(inst, new[] { "Z_OFFSET_VALUE" }, new[] { "Valor de desfase Z", "z Offset Value" });
            return p is not null && p.StorageType == StorageType.Double ? Unidades.AMetros(p.AsDouble()) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void Poner(Element e, string interno, string[] nombres, double metros, bool entero = false)
    {
        try
        {
            var p = Despiece.Parametro(e, new[] { interno }, nombres);

            if (p is null || p.IsReadOnly)
            {
                return;
            }

            if (entero && p.StorageType == StorageType.Integer)
            {
                p.Set((int)metros);
            }
            else if (!entero && p.StorageType == StorageType.Double)
            {
                p.Set(Unidades.AInternas(metros));
            }
        }
        catch (Exception)
        {
            // Uno que no se deja no impide los demas.
        }
    }

    /// <summary>
    /// Coloca la seccion contra la MISMA cara que el punto de insercion de ETABS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aqui estaba el alzado que no se iba. El complemento tenia el punto cardinal <b>8</b>
    /// -arriba al centro- escrito a mano para todas las trabes. El 8 es el habitual de una
    /// cadena, pero <b>el de omision de ETABS es el 10</b>, el centroide: con el, la linea que
    /// llega ya pasa por el centro de la seccion, y forzar «arriba» sube la pieza. De ahi que
    /// las cadenas subieran un peralte y que no hubiera forma de bajarlas tocando el desfase.
    /// </para>
    /// <para>
    /// Ahora se lee el punto cardinal del modelo y se traduce. A lo ANCHO la seccion va siempre
    /// centrada, porque el lector de ETABS ya sumo el corrimiento en planta a la X y la Y; lo
    /// unico que depende del punto cardinal es contra que cara se mide la altura.
    /// </para>
    /// <para>
    /// No se toca ningun desfase de nivel: la cota de la linea es la que trae el calculo y la
    /// cara de la seccion la pone la justificacion. Forzar el desfase a cero movia la pieza.
    /// </para>
    /// </remarks>
    private static void JustificarComoEnEtabs(FamilyInstance inst, BarraJson b)
    {
        try
        {
            // UNA CADENA O TRABE CUELGA SIEMPRE BAJO EL NIVEL. No se deduce del punto de
            // insercion de ETABS, y ese fue el error de tres vueltas: el punto de omision de
            // ETABS es el centroide -para calcular da igual donde este la seccion respecto del
            // eje-, asi que respetarlo al pie de la letra dejaba la trabe repartida, media por
            // encima del nivel. Correcto para el calculo, y no es lo que se construye.
            //
            // El resto de las barras si respetan su punto de insercion: una diagonal o una
            // columna inclinada no tienen "arriba" que valga.
            var z = b.Clase == ClasePieza.Trabe
                ? ZJustification.Top
                : Insercion.Cara(b.PuntoCardinal) switch
                {
                    CaraDeInsercion.Arriba => ZJustification.Top,
                    CaraDeInsercion.Abajo => ZJustification.Bottom,
                    _ => ZJustification.Center
                };

            // A lo ancho, centrada: el corrimiento en planta ya viene en las coordenadas, y
            // volverlo a aplicar aqui lo contaria dos veces.
            inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION)
                ?.Set((int)YJustification.Center);

            inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.Set((int)z);

            // Con "yz Justification" en Independent los de arriba se ignoran y manda cada
            // extremo por su cuenta, asi que se escriben tambien.
            inst.get_Parameter(BuiltInParameter.START_Z_JUSTIFICATION)?.Set((int)z);
            inst.get_Parameter(BuiltInParameter.END_Z_JUSTIFICATION)?.Set((int)z);
        }
        catch (Exception)
        {
            // Hay familias que no exponen la justificacion. Se queda como la ponga Revit.
        }
    }

    /// <summary>El nivel al que se ata un paño: el de su BASE, no el que le asigna ETABS.</summary>
    /// <remarks>
    /// ETABS asigna un area a la planta de su parte de ARRIBA, asi que un muro de planta baja
    /// viene con el nivel de la planta primera. Atandolo a ese nivel, el muro no sale en la
    /// vista de planta baja aunque su geometria este en el sitio correcto. Es la misma
    /// correccion que <see cref="AtarAColumna"/> hace para las columnas.
    /// </remarks>
    private static Level NivelDePano(Document doc, Paso paso, Level porOmision)
    {
        var p = paso.Pano;

        if (p is null || p.Vertices.Count == 0)
        {
            return porOmision;
        }

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

        var donde = Colocacion.NivelDePano(p.Vertices.Min(v => v.Z), comoJson);

        if (donde.Nombre.Length == 0)
        {
            return porOmision;
        }

        return deRevit.FirstOrDefault(n =>
            string.Equals(n.Name, donde.Nombre, StringComparison.CurrentCultureIgnoreCase))
            ?? porOmision;
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
            // Los argumentos van POSICIONALES, nunca con nombre. El nombre del parametro es
            // parte de la firma y aqui no hay Revit con el que comprobarlo: si se escribe
            // "structural: true" y en la DLL real ese parametro se llama de otra forma, no
            // compila en la maquina del usuario aunque compile en el arnes. El ultimo bool de
            // esta sobrecarga es el de estructural.
            return Wall.Create(doc, curvas, tipoId, nivel.Id, true);
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

        // Posicionales por el mismo motivo que en el muro. El orden de esta sobrecarga es
        // documento, contornos, tipo, nivel, estructural, flecha de pendiente y angulo.
        return Floor.Create(
            doc, new List<CurveLoop> { lazo }, tipoId, nivel.Id,
            true, flecha, forma.AnguloRad);
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

            if (paso.Barra is { Clase: ClasePieza.Trabe } barra && e is FamilyInstance viga)
            {
                r.Vigas.Add((viga, barra));
            }

            r.Actualizadas++;
        }
        catch (Exception e)
        {
            r.Errores.Add($"«{paso.Llave}»: {e.Message}");
        }
    }
}
