using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Lo que salio de poner placas, zapatas o soldaduras.</summary>
public sealed class ResultadoDetalles
{
    /// <summary>Piezas del modelo que recibieron su detalle.</summary>
    public int Hechas { get; set; }

    /// <summary>De esas, las que ya tenian uno de CadLink y se les cambio.</summary>
    public int Reemplazadas { get; set; }

    public List<string> Avisos { get; } = new();

    public List<string> Errores { get; } = new();
}

/// <summary>Como se conecta un extremo de viga que llega a una columna.</summary>
public enum TipoConexion
{
    /// <summary>Filete todo alrededor del perfil, sobre la cara de la columna.</summary>
    Filete,

    /// <summary>IR a IR a momento: placa en cada patin y placa de cortante atornillada al alma.</summary>
    Momento
}

/// <summary>Que extremos de la columna se sueldan.</summary>
public enum ExtremosColumna { Base, Cabeza, Ambos }

/// <summary>Las opciones del cuadro de cada boton.</summary>
public sealed class OpcionesDetalle
{
    /// <summary>Girar la placa 90° sobre la columna, por si la medida no decide bien.</summary>
    public bool Girar { get; set; }

    /// <summary>
    /// La placa SOBRE el dado o el muro, con su grout. Apagado: embebida, con su cara de arriba a
    /// paño del arranque de la columna.
    /// </summary>
    public bool SobreElApoyo { get; set; } = true;

    /// <summary>Con la placa sobre el apoyo, subir el arranque de la columna a la cara de la placa.</summary>
    public bool SubirColumna { get; set; } = true;

    /// <summary>Soldar la columna a la placa base, todo alrededor.</summary>
    public bool SoldarColumna { get; set; } = true;

    /// <summary>
    /// Contraplaca: una segunda placa al FONDO de las anclas, ahogada en el dado, con su tuerca
    /// abajo. Con ella las anclas van rectas: la contraplaca hace lo que haria el gancho.
    /// </summary>
    public bool Contraplaca { get; set; }

    /// <summary>La plantilla bajo las zapatas, en metros. Cero = sin plantilla.</summary>
    public double PlantillaM { get; set; } = Cimentacion.PlantillaPorOmisionM;

    /// <summary>El filete de las conexiones, en metros.</summary>
    public double SoldaduraM { get; set; } = Cordon.PorOmisionM;

    /// <summary>Conectar solo los extremos de viga que llegan a una columna o a un muro.</summary>
    public bool SoloEnApoyos { get; set; } = true;

    /// <summary>En un extremo que llega a un muro, la placa embebida con sus anclas.</summary>
    public bool PlacaEnMuro { get; set; } = true;

    public TipoConexion Conexion { get; set; } = TipoConexion.Filete;

    public ExtremosColumna ExtremosColumna { get; set; } = ExtremosColumna.Base;

    /// <summary>Las medidas de las placas y los tornillos de las conexiones.</summary>
    public OpcionesConexion MedidasDeConexion { get; set; } = new();
}

/// <summary>
/// Pone en Revit las placas base, las zapatas y los cordones de soldadura de CadLink.
/// </summary>
/// <remarks>
/// <para>
/// Una sola transaccion por boton, como la importacion y el armado: un solo Ctrl+Z deshace todo
/// lo que se puso. Y con <see cref="SinCuadros"/>, para que cien avisos de Revit no se conviertan
/// en cien cuadros que cerrar.
/// </para>
/// <para>
/// Cada pieza se calcula ENTERA antes de crear nada. Si una falla a medias -un booleano que
/// Revit no resuelve, una columna sin cara de abajo-, esa pieza no deja restos y las demas
/// siguen.
/// </para>
/// </remarks>
internal static class Detallador
{
    // ======================================================================
    //  PLACAS BASE
    // ======================================================================

    public static ResultadoDetalles Placas(
        Document doc, IReadOnlyList<FamilyInstance> columnas, PlacaBaseJson placa, OpcionesDetalle o)
    {
        return EnTransaccion(doc, "Placas base de CadLink", r =>
        {
            var acero = Solidos.Acero(doc);
            var anclas = Solidos.Anclas(doc);
            var soldadura = Solidos.Soldadura(doc);
            var grout = Solidos.Grout(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructConnections, BuiltInCategory.OST_GenericModel);
            var apoyos = o.SobreElApoyo ? Apoyos(doc) : new List<(ElementId Id, BoundingBoxXYZ Caja)>();

            if (placa.Cartabones > 0)
            {
                r.Avisos.Add($"«placa {placa.Marca}»: Lleva {placa.Cartabones} cartabon(es); todavia no se "
                             + "modelan en Revit, estan solo en el detalle de AutoCAD.");
            }

            foreach (var col in columnas)
            {
                var nombre = $"columna {col.Id.Value}";

                // LA PLACA BASE ES DE LA COLUMNA DE ACERO. Un dado modelado como columna de
                // concreto cae en la misma categoria, y al elegir con ventana se lleva su propia
                // placa en el fondo: una segunda placa que nadie pidio.
                if (EsDeConcreto(col))
                {
                    r.Avisos.Add($"«{nombre}»: Es de concreto (un dado o una columna); la placa va en la de acero que se apoya en ella.");
                    continue;
                }

                try
                {
                    var marco = MarcoDeColumna(col, abajo: true);

                    if (marco is null)
                    {
                        r.Errores.Add($"«{nombre}»: Sin cara de abajo. No se encontro el arranque de la columna.");
                        continue;
                    }

                    var m = marco.Value;
                    var (ex, ey) = EjesDeLaPlaca(placa, m, o.Girar);

                    // ---- Las cotas: embebida, o sobre el dado o el muro que tiene debajo ----
                    double? apoyo = null;

                    if (o.SobreElApoyo)
                    {
                        var caras = apoyos
                            .Where(a => a.Id != col.Id && Contiene(a.Caja, m.Centro, Unidades.AInternas(0.01)))
                            .Select(a => Unidades.AMetros(a.Caja.Max.Z));
                        apoyo = PlacaBase.CaraDelApoyo(caras, Unidades.AMetros(m.BaseZ));

                        if (apoyo is null)
                        {
                            r.Avisos.Add($"«{nombre}»: No tiene dado ni muro debajo; la placa se apoyo en el arranque de la columna.");
                        }
                    }

                    var cotas = PlacaBase.Cotas(placa, Unidades.AMetros(m.BaseZ), apoyo, o.SobreElApoyo);
                    var fondoGrout = Unidades.AInternas(cotas.FondoGroutM);
                    var caraInf = Unidades.AInternas(cotas.CaraInferiorM);
                    var caraSup = Unidades.AInternas(cotas.CaraSuperiorM);
                    var centro = new XYZ(m.Centro.X, m.Centro.Y, caraInf);

                    var planta = Solidos.Rectangulo(
                        centro, ex, ey, Unidades.AInternas(placa.LadoXM), Unidades.AInternas(placa.LadoYM));

                    // ---- La placa, con sus barrenos ----
                    var solidoPlaca = Solidos.Prisma(planta, caraInf, caraSup, acero);
                    var piezasAncla = new List<GeometryObject>();
                    var espesor = caraSup - caraInf;

                    // ---- La contraplaca, al fondo de las anclas: la placa misma, con su tuerca abajo ----
                    Solid? contraplaca = null;
                    var fondoAnclas = placa.Anclas
                        .Where(a => a.PuntaM - a.FondoM > 0.001)
                        .Select(a => caraInf + Unidades.AInternas(a.FondoM))
                        .DefaultIfEmpty(double.NaN)
                        .Max();
                    var tuercaAbajo = Unidades.AInternas(placa.Anclas.Select(a => a.TuercaAltoM).DefaultIfEmpty(0).Max());
                    var caraContra = fondoAnclas + tuercaAbajo;

                    if (o.Contraplaca && !double.IsNaN(fondoAnclas))
                    {
                        contraplaca = Solidos.Prisma(planta, caraContra, caraContra + espesor, acero);
                    }

                    foreach (var a in placa.Anclas)
                    {
                        var eje = centro + (ex * Unidades.AInternas(a.X)) + (ey * Unidades.AInternas(a.Y));
                        var radio = Unidades.AInternas(a.DiametroM / 2);
                        var radioBarreno = Unidades.AInternas((a.AgujeroM > 0 ? a.AgujeroM : a.DiametroM) / 2);

                        // El barreno atraviesa la placa con holgura arriba y abajo, para que la
                        // resta no deje una pelicula de espesor cero.
                        var holgura = Unidades.AInternas(0.002);
                        var barreno = Solidos.Cilindro(
                            new XYZ(eje.X, eje.Y, caraInf - holgura), XYZ.BasisZ,
                            (caraSup - caraInf) + (2 * holgura), radioBarreno, acero);
                        solidoPlaca = Solidos.Restar(solidoPlaca, barreno);

                        if (contraplaca is not null)
                        {
                            contraplaca = Solidos.Restar(contraplaca, Solidos.Cilindro(
                                new XYZ(eje.X, eje.Y, caraContra - holgura), XYZ.BasisZ,
                                espesor + (2 * holgura), radioBarreno, acero));
                        }

                        // Sin alzado -ni ahogo ni longitud en la hoja- el detalle de AutoCAD no
                        // dibuja el vastago, y aqui tampoco se modela: queda el barreno.
                        if (radio <= 0 || a.PuntaM - a.FondoM <= 0.001)
                        {
                            continue;
                        }

                        // ---- El ancla con su codo y su pata, HACIA EL CENTRO de la placa ----
                        //
                        // Hacia el centro y no a lo largo de su hilera: asi la pata se mete en el
                        // nucleo del dado y nunca se sale por una de sus caras. Con contraplaca va
                        // recta: la contraplaca ya ancla.
                        var punta = new XYZ(eje.X, eje.Y, caraInf + Unidades.AInternas(a.PuntaM));
                        var hacia = (ex * -a.X) + (ey * -a.Y);
                        hacia = hacia.GetLength() < 1e-9
                            ? ((ex * a.PataX) + (ey * a.PataY)).GetLength() < 1e-9 ? ex : ((ex * a.PataX) + (ey * a.PataY)).Normalize()
                            : hacia.Normalize();
                        var pata = contraplaca is null ? Unidades.AInternas(a.PataM) : 0;

                        foreach (var pieza in Solidos.AnclaDoblada(
                                     punta, caraInf + Unidades.AInternas(a.FondoM), hacia,
                                     pata, Unidades.AInternas(a.RadioDobleM), radio, anclas))
                        {
                            piezasAncla.Add(pieza);
                        }

                        // La tuerca de abajo de la contraplaca, colgando de su cara de abajo.
                        if (contraplaca is not null && a.TuercaM > 0 && a.TuercaAltoM > 0)
                        {
                            piezasAncla.Add(Solidos.Tuerca(
                                new XYZ(eje.X, eje.Y, caraContra), XYZ.BasisZ.Negate(), ex,
                                Unidades.AInternas(a.TuercaM), Unidades.AInternas(a.TuercaAltoM), radio, anclas));
                        }

                        // ---- La tuerca sobre la placa, con su chaflan y su barreno ----
                        if (a.TuercaM > 0 && a.TuercaAltoM > 0)
                        {
                            piezasAncla.Add(Solidos.Tuerca(
                                new XYZ(eje.X, eje.Y, caraSup), XYZ.BasisZ, ex, Unidades.AInternas(a.TuercaM),
                                Unidades.AInternas(a.TuercaAltoM), radio, anclas));
                        }
                    }

                    // ---- El grout, de la placa al dado ----
                    Solid? solidoGrout = placa.GroutM > 0
                        ? Solidos.Prisma(planta, fondoGrout, caraInf, grout)
                        : null;

                    // ---- La soldadura de la columna, todo alrededor, sobre la cara de la placa ----
                    Solid? filete = null;

                    if (o.SoldarColumna && placa.SoldaduraM > 0)
                    {
                        var contorno = CurveLoop.CreateViaTransform(
                            m.Contorno, Transform.CreateTranslation(new XYZ(0, 0, caraSup - m.BaseZ)));

                        filete = Solidos.Filete(
                            contorno, XYZ.BasisZ, Unidades.AInternas(placa.SoldaduraM), soldadura, out var cuadrado);

                        if (cuadrado)
                        {
                            r.Avisos.Add($"«{nombre}»: Soldadura de seccion cuadrada. Revit no pudo hacer el "
                                         + "triangulo del filete con el contorno de la columna.");
                        }
                    }

                    // ---- Ya todo calculado: se cambia lo anterior y se crea ----
                    var llave = LlaveDetalle.De(ClaseDetalle.Placa, col.Id.Value);

                    if (Solidos.Borrar(doc, llave) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var marca = placa.Marca;
                    var texto = $"CadLink: placa base {placa.Marca}";

                    Solidos.Forma(doc, categoria, new List<GeometryObject> { solidoPlaca }, llave,
                        $"{texto} {TextoDeMedidas(placa)}", marca);

                    // Las anclas y la contraplaca van como PARTE de la llave: se borran con la placa,
                    // pero no cuentan para saber donde termina el dado.
                    var llaveAnclas = llave + "|anclas";

                    if (piezasAncla.Count > 0)
                    {
                        Solidos.Forma(doc, categoria, piezasAncla, llaveAnclas,
                            $"{texto}, {placa.Anclas.Count} anclas", marca);
                    }

                    if (solidoGrout is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { solidoGrout }, llave,
                            $"{texto}, grout {Cm(placa.GroutM)} cm", marca);
                    }

                    if (contraplaca is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { contraplaca }, llaveAnclas,
                            $"{texto}, contraplaca", marca);
                    }

                    if (filete is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { filete }, llave,
                            $"{texto}, filete {Mm(placa.SoldaduraM)} mm todo alrededor {placa.Electrodo}".TrimEnd(),
                            marca);
                    }

                    // ---- Sobre el apoyo: la columna arranca en la cara de la placa ----
                    var subir = caraSup - m.BaseZ;

                    if (o.SobreElApoyo && o.SubirColumna && Math.Abs(subir) > Unidades.AInternas(0.0005))
                    {
                        var desfase = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);

                        if (desfase is not null && !desfase.IsReadOnly)
                        {
                            desfase.Set(desfase.AsDouble() + subir);
                        }
                        else
                        {
                            r.Avisos.Add($"«{nombre}»: No se pudo subir su arranque a la cara de la placa; la columna atraviesa la placa.");
                        }
                    }

                    r.Hechas++;
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo poner la placa. {e.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Lo que puede tener una columna debajo: dados, columnas de concreto, muros, zapatas y losas.
    /// </summary>
    /// <remarks>
    /// Se compara por la caja de cada uno, que basta para saber que esta debajo y a que altura
    /// termina. Las formas de CadLink de la categoria de conexiones -las placas ya puestas- no
    /// estan en estas categorias, asi que no se toman por apoyo.
    /// </remarks>
    private static List<(ElementId Id, BoundingBoxXYZ Caja)> Apoyos(Document doc)
    {
        var res = new List<(ElementId, BoundingBoxXYZ)>();

        foreach (var c in new[]
                 {
                     BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Walls,
                     BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Floors
                 })
        {
            foreach (var e in new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType())
            {
                if (e.get_BoundingBox(null) is { } caja)
                {
                    res.Add((e.Id, caja));
                }
            }
        }

        return res;
    }

    /// <summary>¿Es una columna de concreto -un dado modelado como columna-?</summary>
    public static bool EsDeConcreto(FamilyInstance col) =>
        col.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.Concrete
        || col.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.PrecastConcrete;

    /// <summary>¿El punto cae dentro de la caja en planta?</summary>
    private static bool Contiene(BoundingBoxXYZ c, XYZ p, double holgura) =>
        p.X >= c.Min.X - holgura && p.X <= c.Max.X + holgura
        && p.Y >= c.Min.Y - holgura && p.Y <= c.Max.Y + holgura;

    /// <summary>
    /// Los ejes de la placa en el modelo: la X y la Y del detalle de AutoCAD.
    /// </summary>
    private static (XYZ Ex, XYZ Ey) EjesDeLaPlaca(PlacaBaseJson p, MarcoColumna m, bool girar)
    {
        var intercambiar = PlacaBase.Intercambiar(
            p.PerfilXM, p.PerfilYM, Unidades.AMetros(m.LadoX), Unidades.AMetros(m.LadoY));

        if (girar)
        {
            intercambiar = !intercambiar;
        }

        return intercambiar ? (m.Ey, m.Ex.Negate()) : (m.Ex, m.Ey);
    }

    // ======================================================================
    //  ZAPATAS AISLADAS
    // ======================================================================

    public static ResultadoDetalles Zapatas(
        Document doc, IReadOnlyList<FamilyInstance> columnas, ZapataAisladaJson z, OpcionesDetalle o)
    {
        return EnTransaccion(doc, "Zapatas de CadLink", r =>
        {
            var concreto = Solidos.Concreto(doc);
            var plantilla = Solidos.Plantilla(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_GenericModel);

            if (Cimentacion.EsLindero(z.Tipo))
            {
                r.Avisos.Add($"«zapata {z.Id}»: Es de lindero y se puso centrada en la columna; muevela "
                             + "hacia el lindero con Mover, porque desde la columna no se sabe de que lado esta.");
            }

            foreach (var col in columnas)
            {
                var nombre = $"columna {col.Id.Value}";

                try
                {
                    var marco = MarcoDeColumna(col);

                    if (marco is null)
                    {
                        r.Errores.Add($"«{nombre}»: Sin cara de abajo. No se encontro el arranque de la columna.");
                        continue;
                    }

                    var m = marco.Value;

                    // SI LA COLUMNA YA TIENE PLACA BASE DE CADLINK, el dado termina bajo su grout,
                    // no en el arranque de la columna: si no, el dado se meteria en la placa.
                    var fondoPlaca = Solidos.FondoDe(doc, LlaveDetalle.De(ClaseDetalle.Placa, col.Id.Value));
                    var tope = Unidades.AMetros(fondoPlaca ?? m.BaseZ);

                    var c = Cimentacion.Cotas(z, tope, o.PlantillaM);
                    var centro = new XYZ(m.Centro.X, m.Centro.Y, 0);

                    var zapata = Solidos.Prisma(
                        Solidos.Rectangulo(centro, m.Ex, m.Ey, Unidades.AInternas(z.AnchoM), Unidades.AInternas(z.LargoM)),
                        Unidades.AInternas(c.DesplanteM), Unidades.AInternas(c.TopeZapataM), concreto);

                    Solid? dado = null;

                    if (z.DadoXM > 0 && c.AltoDadoM > 0.001)
                    {
                        var z0 = Unidades.AInternas(c.TopeZapataM);
                        var alto = Unidades.AInternas(c.AltoDadoM);

                        dado = z.DadoCircular
                            ? Solidos.Cilindro(new XYZ(centro.X, centro.Y, z0), XYZ.BasisZ, alto,
                                Unidades.AInternas(z.DadoXM / 2), concreto)
                            : Solidos.Prisma(
                                Solidos.Rectangulo(centro, m.Ex, m.Ey, Unidades.AInternas(z.DadoXM),
                                    Unidades.AInternas(z.DadoYM > 0 ? z.DadoYM : z.DadoXM)),
                                z0, z0 + alto, concreto);
                    }

                    Solid? solidoPlantilla = null;

                    if (o.PlantillaM > 0)
                    {
                        var s = 2 * Cimentacion.SalientePlantillaM;
                        solidoPlantilla = Solidos.Prisma(
                            Solidos.Rectangulo(centro, m.Ex, m.Ey, Unidades.AInternas(z.AnchoM + s),
                                Unidades.AInternas(z.LargoM + s)),
                            Unidades.AInternas(c.PlantillaM), Unidades.AInternas(c.DesplanteM), plantilla);
                    }

                    var llave = LlaveDetalle.De(ClaseDetalle.Zapata, col.Id.Value);

                    if (Solidos.Borrar(doc, llave) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var texto = $"CadLink: zapata {z.Id}";

                    Solidos.Forma(doc, categoria, new List<GeometryObject> { zapata }, llave,
                        $"{texto} {Cm(z.AnchoM)}x{Cm(z.LargoM)}x{Cm(z.EspesorM)} cm{Fc(z.Fc)}", z.Id);

                    if (dado is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { dado }, llave,
                            $"{texto}, dado de {Cm(c.AltoDadoM)} cm de alto", z.Id);
                    }

                    if (solidoPlantilla is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { solidoPlantilla }, llave,
                            $"{texto}, plantilla de {Cm(o.PlantillaM)} cm", z.Id);
                    }

                    r.Hechas++;
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo poner la zapata. {e.Message}");
                }
            }
        });
    }

    // ======================================================================
    //  ZAPATAS CORRIDAS
    // ======================================================================

    public static ResultadoDetalles Corridas(
        Document doc, IReadOnlyList<Wall> muros, ZapataCorridaJson z, OpcionesDetalle o)
    {
        return EnTransaccion(doc, "Zapatas corridas de CadLink", r =>
        {
            var concreto = Solidos.Concreto(doc);
            var plantilla = Solidos.Plantilla(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_GenericModel);

            foreach (var muro in muros)
            {
                var nombre = $"muro {muro.Id.Value}";

                try
                {
                    if (muro.Location is not LocationCurve lc || lc.Curve is not Line linea)
                    {
                        r.Errores.Add($"«{nombre}»: Muro curvo. La zapata corrida solo se pone bajo muros rectos.");
                        continue;
                    }

                    var caja = muro.get_BoundingBox(null);

                    if (caja is null)
                    {
                        r.Errores.Add($"«{nombre}»: Sin geometria.");
                        continue;
                    }

                    var a0 = linea.GetEndPoint(0);
                    var a1 = linea.GetEndPoint(1);
                    var eje = new XYZ(a1.X - a0.X, a1.Y - a0.Y, 0).Normalize();
                    var fuera = new XYZ(muro.Orientation.X, muro.Orientation.Y, 0).Normalize();
                    var anchoMuro = muro.Width;

                    // La linea de ubicacion puede ir en cualquier cara del muro: se pasa al EJE.
                    var aEje = fuera * CorrimientoAlEje(muro, anchoMuro);
                    var p0 = new XYZ(a0.X, a0.Y, 0) + aEje;
                    var p1 = new XYZ(a1.X, a1.Y, 0) + aEje;

                    var c = Cimentacion.Cotas(z, Unidades.AMetros(caja.Min.Z), o.PlantillaM);
                    var anchoMuroM = Unidades.AMetros(anchoMuro);

                    // Las tres franjas -zapata, contratrabe o enrase, plantilla- se alargan medio
                    // ancho de zapata por cada punta: asi en las esquinas las de los dos muros se
                    // encuentran y no queda una muesca sin cimentar.
                    var alargar = Unidades.AInternas(z.AnchoM / 2);

                    List<XYZ> Franja(double anchoM, double extraM) => FranjaDeMuro(
                        p0, p1, eje, fuera,
                        Unidades.AInternas(anchoM + extraM),
                        Unidades.AInternas(Cimentacion.DesfaseHaciaFuera(z.Tipo, anchoM, anchoMuroM)),
                        alargar + Unidades.AInternas(extraM / 2));

                    var zapata = Solidos.Prisma(Franja(z.AnchoM, 0),
                        Unidades.AInternas(c.DesplanteM), Unidades.AInternas(c.TopeZapataM), concreto);

                    Solid? encima = null;
                    var queEncima = string.Empty;

                    if (Cimentacion.LlevaContratrabe(z))
                    {
                        encima = Solidos.Prisma(Franja(z.ContratrabeAnchoM, 0),
                            Unidades.AInternas(c.TopeZapataM), Unidades.AInternas(c.TopeContratrabeM), concreto);
                        queEncima = $"contratrabe {z.Contratrabe}";

                        var diferencia = c.TopeContratrabeM - c.BaseMuroM;

                        if (Math.Abs(diferencia) > 0.005)
                        {
                            r.Avisos.Add(diferencia > 0
                                ? $"«{nombre}»: Contratrabe mas alta que la base del muro. Sube {Cm(diferencia)} cm de mas."
                                : $"«{nombre}»: Hueco bajo el muro. Quedan {Cm(-diferencia)} cm entre la contratrabe y el muro.");
                        }
                    }
                    else if (z.EnraseM > 0 && c.BaseMuroM - c.TopeZapataM > 0.005)
                    {
                        encima = Solidos.Prisma(Franja(z.EnraseM, 0),
                            Unidades.AInternas(c.TopeZapataM), Unidades.AInternas(c.BaseMuroM), concreto);
                        queEncima = $"enrase de {Cm(z.EnraseM)} cm";
                    }

                    Solid? solidoPlantilla = o.PlantillaM > 0
                        ? Solidos.Prisma(Franja(z.AnchoM, 2 * Cimentacion.SalientePlantillaM),
                            Unidades.AInternas(c.PlantillaM), Unidades.AInternas(c.DesplanteM), plantilla)
                        : null;

                    var llave = LlaveDetalle.De(ClaseDetalle.Corrida, muro.Id.Value);

                    if (Solidos.Borrar(doc, llave) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var texto = $"CadLink: zapata corrida {z.Id}";

                    Solidos.Forma(doc, categoria, new List<GeometryObject> { zapata }, llave,
                        $"{texto} de {Cm(z.AnchoM)}x{Cm(z.EspesorM)} cm{Fc(z.Fc)}", z.Id);

                    if (encima is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { encima }, llave,
                            $"{texto}, {queEncima}", z.Id);
                    }

                    if (solidoPlantilla is not null)
                    {
                        Solidos.Forma(doc, categoria, new List<GeometryObject> { solidoPlantilla }, llave,
                            $"{texto}, plantilla de {Cm(o.PlantillaM)} cm", z.Id);
                    }

                    r.Hechas++;
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo poner la zapata corrida. {e.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Cuanto hay que correr la linea de ubicacion del muro, hacia fuera, para llegar a su eje.
    /// </summary>
    /// <remarks>
    /// <c>WALL_KEY_REF_PARAM</c>: 0 eje del muro, 1 eje del nucleo, 2 y 4 caras exteriores, 3 y 5
    /// caras interiores. Las del nucleo se tratan como las de acabado: la diferencia es el
    /// aplanado, centimetros que no cambian la zapata.
    /// </remarks>
    private static double CorrimientoAlEje(Wall muro, double ancho)
    {
        var p = muro.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
        var linea = p?.AsInteger() ?? 0;

        return linea switch
        {
            2 or 4 => -ancho / 2,
            3 or 5 => ancho / 2,
            _ => 0
        };
    }

    /// <summary>El rectangulo en planta de una franja a lo largo del muro.</summary>
    private static List<XYZ> FranjaDeMuro(
        XYZ p0, XYZ p1, XYZ eje, XYZ fuera, double ancho, double desfase, double alargar)
    {
        var a = p0 - (eje * alargar) + (fuera * desfase);
        var b = p1 + (eje * alargar) + (fuera * desfase);
        var h = fuera * (ancho / 2);

        return new List<XYZ> { a - h, b - h, b + h, a + h };
    }

    // ======================================================================
    //  SOLDADURA DE VIGAS
    // ======================================================================

    /// <summary>Una viga y, si se eligio un solo extremo, el punto donde se dio clic.</summary>
    public readonly record struct VigaElegida(FamilyInstance Viga, XYZ? Clic);

    /// <summary>
    /// Conecta los extremos de las vigas: a una columna, con filete o a momento; a un muro, con su
    /// placa embebida.
    /// </summary>
    public static ResultadoDetalles Soldaduras(
        Document doc, IReadOnlyList<VigaElegida> vigas, IReadOnlyList<Element> columnas,
        IReadOnlyList<Element> muros, OpcionesDetalle o)
    {
        return EnTransaccion(doc, "Conexiones de vigas de CadLink", r =>
        {
            var mat = new Materiales(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructConnections, BuiltInCategory.OST_GenericModel);
            var lado = Unidades.AInternas(o.SoldaduraM);
            var holgura = Unidades.AInternas(0.05);

            var cajasColumnas = Cajas(columnas);
            var cajasMuros = Cajas(muros);

            foreach (var elegida in vigas)
            {
                var viga = elegida.Viga;
                var nombre = $"viga {viga.Id.Value}";

                try
                {
                    if (viga.Location is not LocationCurve lc || lc.Curve is not Line linea)
                    {
                        r.Errores.Add($"«{nombre}»: Viga curva. Solo se conectan vigas rectas.");
                        continue;
                    }

                    var p0 = linea.GetEndPoint(0);
                    var p1 = linea.GetEndPoint(1);
                    var eje = (p1 - p0).Normalize();
                    var caras = CarasDeExtremo(viga, eje);

                    // UN extremo si se dio clic en la viga: el mas cercano al clic. Si no, los dos.
                    var extremos = elegida.Clic is { } clic
                        ? new[] { Conexion.ExtremoMasCercano(V(p0), V(p1), V(clic)) }
                        : new[] { 0, 1 };

                    var hechos = 0;

                    foreach (var extremo in extremos)
                    {
                        var donde = $"«{nombre}, extremo {extremo + 1}»";
                        var cara = Extremo(caras, p0, eje, extremo);

                        if (cara is null)
                        {
                            r.Avisos.Add($"{donde}: Extremo sin cara plana.");
                            continue;
                        }

                        var contorno = ContornoExterior(cara);

                        if (contorno is null)
                        {
                            continue;
                        }

                        var enColumna = Toca(cajasColumnas, cara.Origin, holgura);
                        var enMuro = !enColumna && Toca(cajasMuros, cara.Origin, holgura);

                        if (o.SoloEnApoyos && !enColumna && !enMuro)
                        {
                            continue;
                        }

                        var marco = MarcoDeExtremo(cara, contorno);
                        var piezas = new List<(GeometryObject Solido, string Que)>();

                        if (enMuro && o.PlacaEnMuro)
                        {
                            PlacaEmbebida(marco, contorno, lado, o, mat, piezas, r, donde);
                        }
                        else if (enColumna && o.Conexion == TipoConexion.Momento)
                        {
                            if (!AMomento(marco, contorno, lado, o, mat, piezas, r, donde))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            piezas.Add((Filete(contorno, marco.A, lado, mat.Soldadura, r, donde),
                                $"filete de {Mm(o.SoldaduraM)} mm todo alrededor"));
                        }

                        var llave = LlaveDetalle.De(ClaseDetalle.Soldadura, viga.Id.Value, extremo);

                        if (Solidos.Borrar(doc, llave) > 0)
                        {
                            r.Reemplazadas++;
                        }

                        foreach (var (solido, que) in piezas)
                        {
                            Solidos.Forma(doc, categoria, new List<GeometryObject> { solido }, llave,
                                $"CadLink: {que}, extremo {extremo + 1}", string.Empty);
                        }

                        hechos++;
                    }

                    if (hechos > 0)
                    {
                        r.Hechas++;
                    }
                    else if (o.SoloEnApoyos)
                    {
                        r.Avisos.Add($"«{nombre}»: No llega a una columna ni a un muro. No se conecto ningun extremo.");
                    }
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo conectar. {e.Message}");
                }
            }
        });
    }

    /// <summary>Los ejes del extremo: U de lado, V hacia arriba, A hacia dentro de la viga.</summary>
    /// <param name="Origen">El centro del perfil, sobre la cara de la columna o del muro.</param>
    private readonly record struct MarcoExtremo(
        XYZ Origen, XYZ U, XYZ V, XYZ A, double U0, double U1, double V0, double V1);

    private static MarcoExtremo MarcoDeExtremo(PlanarFace cara, CurveLoop contorno)
    {
        var a = cara.FaceNormal.Negate().Normalize();

        // V: la vertical, sin la parte que va a lo largo de la viga. En una viga vertical -que no
        // es una viga- se toma la X.
        var arriba = XYZ.BasisZ - (a * XYZ.BasisZ.DotProduct(a));
        var v = arriba.GetLength() < 1e-6 ? XYZ.BasisX : arriba.Normalize();
        var u = v.CrossProduct(a).Normalize();

        var o = cara.Origin;
        double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;

        foreach (var c in contorno)
        {
            foreach (var p in c.Tessellate())
            {
                var d = p - o;
                u0 = Math.Min(u0, d.DotProduct(u));
                u1 = Math.Max(u1, d.DotProduct(u));
                v0 = Math.Min(v0, d.DotProduct(v));
                v1 = Math.Max(v1, d.DotProduct(v));
            }
        }

        var centro = o + (u * ((u0 + u1) / 2)) + (v * ((v0 + v1) / 2));
        var mu = (u1 - u0) / 2;
        var mv = (v1 - v0) / 2;

        return new MarcoExtremo(centro, u, v, a, -mu, mu, -mv, mv);
    }

    /// <summary>
    /// La viga que llega a un muro de concreto: placa embebida a paño del muro, sus anclas hacia
    /// dentro del muro, y la viga soldada a la placa todo alrededor.
    /// </summary>
    private static void PlacaEmbebida(
        MarcoExtremo m, CurveLoop contorno, double lado, OpcionesDetalle o, Materiales mat,
        List<(GeometryObject, string)> piezas, ResultadoDetalles r, string donde)
    {
        var (placa, pernos) = Conexion.PlacaEnMuro(
            Unidades.AMetros(m.U0), Unidades.AMetros(m.U1), Unidades.AMetros(m.V0), Unidades.AMetros(m.V1),
            o.MedidasDeConexion);

        piezas.Add((Solidos.Caja(m.Origen, m.U, m.V, m.A, AInternas(placa), mat.Acero),
            $"placa embebida de {Cm(placa.U1 - placa.U0)}x{Cm(placa.V1 - placa.V0)} cm, {Mm(o.MedidasDeConexion.PlacaMuroEspesorM)} mm"));

        // Las anclas salen de la cara de ATRAS de la placa, hacia dentro del muro: -A.
        var atras = m.Origen - (m.A * Unidades.AInternas(o.MedidasDeConexion.PlacaMuroEspesorM));

        foreach (var perno in pernos)
        {
            var inicio = atras + (m.U * Unidades.AInternas(perno.U)) + (m.V * Unidades.AInternas(perno.V));
            piezas.Add((Solidos.Cilindro(inicio, m.A.Negate(), Unidades.AInternas(perno.LargoM),
                Unidades.AInternas(perno.DiametroM / 2), mat.Anclas), $"ancla de la placa embebida"));
        }

        piezas.Add((Filete(contorno, m.A, lado, mat.Soldadura, r, donde),
            $"filete de {Mm(o.SoldaduraM)} mm todo alrededor, a la placa embebida"));
    }

    /// <summary>
    /// IR a columna a MOMENTO: las placas de los patines, la de cortante con sus tornillos y las
    /// soldaduras de las tres placas a la columna.
    /// </summary>
    /// <returns>False si la viga no es una I y no se pudo.</returns>
    private static bool AMomento(
        MarcoExtremo m, CurveLoop contorno, double lado, OpcionesDetalle o, Materiales mat,
        List<(GeometryObject, string)> piezas, ResultadoDetalles r, string donde)
    {
        var horizontales = new List<double>();
        var verticales = new List<double>();

        foreach (var c in contorno)
        {
            if (c is not Line l)
            {
                continue;
            }

            var a = l.GetEndPoint(0) - m.Origen;
            var b = l.GetEndPoint(1) - m.Origen;

            if (Math.Abs(a.DotProduct(m.V) - b.DotProduct(m.V)) < 1e-6)
            {
                horizontales.Add(Unidades.AMetros(a.DotProduct(m.V)));
            }
            else if (Math.Abs(a.DotProduct(m.U) - b.DotProduct(m.U)) < 1e-6)
            {
                verticales.Add(Unidades.AMetros(a.DotProduct(m.U)));
            }
        }

        var perfil = Conexion.MedirI(horizontales, verticales);

        if (perfil is null)
        {
            r.Errores.Add($"{donde}: No es un perfil I; la conexion a momento es de IR a columna. Usa el filete.");
            return false;
        }

        var (placas, tornillos) = Conexion.Momento(perfil.Value, o.MedidasDeConexion);

        foreach (var c in placas)
        {
            var local = AInternas(c);
            piezas.Add((Solidos.Caja(m.Origen, m.U, m.V, m.A, local, mat.Acero), c.Que));

            // Cada placa soldada a la columna todo alrededor de la cara que la toca.
            piezas.Add((Filete(Solidos.CaraInicial(m.Origen, m.U, m.V, m.A, local), m.A, lado, mat.Soldadura, r, donde),
                $"filete de {Mm(o.SoldaduraM)} mm de la {c.Que} a la columna"));
        }

        foreach (var t in tornillos)
        {
            var radio = Unidades.AInternas(t.DiametroM / 2);
            var centro = m.Origen + (m.V * Unidades.AInternas(t.V)) + (m.A * Unidades.AInternas(t.A));
            var desde = centro + (m.U * Unidades.AInternas(t.U0));
            var largo = Unidades.AInternas(t.U1 - t.U0);

            piezas.Add((Solidos.Cilindro(desde, m.U, largo, radio, mat.Anclas), $"tornillo de {Mm(t.DiametroM)} mm"));

            // Cabeza de un lado y tuerca del otro, contra la placa y contra el alma.
            var llave = Unidades.AInternas(1.5 * t.DiametroM);
            var alto = Unidades.AInternas(0.7 * t.DiametroM);
            var almaU = Unidades.AInternas(-perfil.Value.Tw / 2);
            var placaU = Unidades.AInternas((perfil.Value.Tw / 2) + o.MedidasDeConexion.PlacaCortanteEspesorM);

            piezas.Add((Solidos.Tuerca(centro + (m.U * placaU), m.U, m.V, llave, alto, radio, mat.Anclas), "cabeza del tornillo"));
            piezas.Add((Solidos.Tuerca(centro + (m.U * almaU), m.U.Negate(), m.V, llave, alto, radio, mat.Anclas), "tuerca del tornillo"));
        }

        return true;
    }

    private static Solid Filete(
        CurveLoop contorno, XYZ haciaLaPieza, double lado, ElementId material, ResultadoDetalles r, string donde)
    {
        var filete = Solidos.Filete(contorno, haciaLaPieza, lado, material, out var cuadrado);

        if (cuadrado)
        {
            r.Avisos.Add($"{donde}: Soldadura de seccion cuadrada.");
        }

        return filete;
    }

    // ======================================================================
    //  SOLDAR COLUMNAS
    // ======================================================================

    /// <summary>
    /// El filete todo alrededor de la base o de la cabeza de cada columna, sobre lo que tenga: una
    /// placa, una losa, un dado o la columna de abajo.
    /// </summary>
    public static ResultadoDetalles SoldarColumnas(
        Document doc, IReadOnlyList<FamilyInstance> columnas, OpcionesDetalle o)
    {
        return EnTransaccion(doc, "Soldadura de columnas de CadLink", r =>
        {
            var soldadura = Solidos.Soldadura(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructConnections, BuiltInCategory.OST_GenericModel);
            var lado = Unidades.AInternas(o.SoldaduraM);

            var extremos = o.ExtremosColumna switch
            {
                ExtremosColumna.Base => new[] { true },
                ExtremosColumna.Cabeza => new[] { false },
                _ => new[] { true, false }
            };

            foreach (var col in columnas)
            {
                var nombre = $"columna {col.Id.Value}";
                var hechos = 0;

                foreach (var abajo in extremos)
                {
                    var donde = $"«{nombre}, {(abajo ? "base" : "cabeza")}»";

                    try
                    {
                        var marco = MarcoDeColumna(col, abajo);

                        if (marco is null)
                        {
                            r.Errores.Add($"{donde}: Sin cara plana horizontal.");
                            continue;
                        }

                        // En la base el filete sube por la columna; en la cabeza, baja.
                        var filete = Filete(marco.Value.Contorno, abajo ? XYZ.BasisZ : XYZ.BasisZ.Negate(),
                            lado, soldadura, r, donde);

                        var llave = LlaveDetalle.De(ClaseDetalle.SoldaduraColumna, col.Id.Value, abajo ? 0 : 1);

                        if (Solidos.Borrar(doc, llave) > 0)
                        {
                            r.Reemplazadas++;
                        }

                        Solidos.Forma(doc, categoria, new List<GeometryObject> { filete }, llave,
                            $"CadLink: filete de {Mm(o.SoldaduraM)} mm todo alrededor, {(abajo ? "base" : "cabeza")} de la columna",
                            string.Empty);

                        hechos++;
                    }
                    catch (Exception e)
                    {
                        r.Errores.Add($"{donde}: No se pudo soldar. {e.Message}");
                    }
                }

                if (hechos > 0)
                {
                    r.Hechas++;
                }
            }
        });
    }

    // ======================================================================
    //  ENCAMISADO DE TRABES DE CONCRETO
    // ======================================================================

    /// <summary>
    /// Encamisa cada trabe de concreto con placas, o con angulos y soleras, de cara a cara de
    /// columna.
    /// </summary>
    /// <remarks>
    /// La seccion y el largo se MIDEN del solido que Revit dibujo: asi la trabe ya viene recortada
    /// en las caras de las columnas y bajo la losa, y el acero cubre lo que de verdad queda a la
    /// vista, sea cual sea la familia y como se llamen sus parametros.
    /// </remarks>
    public static ResultadoDetalles Encamisar(
        Document doc, IReadOnlyList<FamilyInstance> trabes, OpcionesEncamisado o)
    {
        return EnTransaccion(doc, "Encamisado de trabes de CadLink", r =>
        {
            var acero = Solidos.Acero(doc);
            var tornilleria = Solidos.Anclas(doc);
            var adhesivo = Solidos.Grout(doc);
            var categoria = Solidos.Categoria(
                doc, BuiltInCategory.OST_StructConnections, BuiltInCategory.OST_GenericModel);

            foreach (var trabe in trabes)
            {
                var nombre = $"trabe {trabe.Id.Value}";

                try
                {
                    if (trabe.Location is not LocationCurve lc || lc.Curve is not Line linea)
                    {
                        r.Errores.Add($"«{nombre}»: Trabe curva. Solo se encamisan trabes rectas.");
                        continue;
                    }

                    var p0 = linea.GetEndPoint(0);
                    var p1 = linea.GetEndPoint(1);
                    var enPlanta = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);

                    if (enPlanta.GetLength() < 1e-6 || Math.Abs(p1.Z - p0.Z) / enPlanta.GetLength() > 0.02)
                    {
                        r.Errores.Add($"«{nombre}»: Inclinada. Solo se encamisan trabes horizontales.");
                        continue;
                    }

                    var eje = enPlanta.Normalize();
                    var u = XYZ.BasisZ.CrossProduct(eje).Normalize();
                    var puntos = Geometria.PuntosDelSolido(trabe);

                    if (puntos.Count < 4)
                    {
                        r.Errores.Add($"«{nombre}»: Sin geometria.");
                        continue;
                    }

                    double a0 = double.MaxValue, a1 = double.MinValue, u0 = double.MaxValue, u1 = double.MinValue;
                    double z0 = double.MaxValue, z1 = double.MinValue;

                    foreach (var p in puntos)
                    {
                        var d = p - p0;
                        a0 = Math.Min(a0, d.DotProduct(eje));
                        a1 = Math.Max(a1, d.DotProduct(eje));
                        u0 = Math.Min(u0, d.DotProduct(u));
                        u1 = Math.Max(u1, d.DotProduct(u));
                        z0 = Math.Min(z0, p.Z);
                        z1 = Math.Max(z1, p.Z);
                    }

                    // El cero de la trabe: centro de su cara de abajo, en la cara de la primera columna.
                    var origen = new XYZ(p0.X, p0.Y, z0) + (eje * a0) + (u * ((u0 + u1) / 2));

                    var (piezas, tornillos) = Encamisado.Piezas(
                        Unidades.AMetros(u1 - u0), Unidades.AMetros(z1 - z0), Unidades.AMetros(a1 - a0), o);

                    if (piezas.Count == 0)
                    {
                        r.Errores.Add($"«{nombre}»: Es demasiado corta para encamisarla.");
                        continue;
                    }

                    var acer = new List<GeometryObject>();
                    var pega = new List<GeometryObject>();
                    var torn = new List<GeometryObject>();

                    foreach (var c in piezas)
                    {
                        var solido = Solidos.Caja(origen, u, XYZ.BasisZ, eje, AInternas(c),
                            c.Que == Encamisado.Adhesivo ? adhesivo : acero);
                        (c.Que == Encamisado.Adhesivo ? pega : acer).Add(solido);
                    }

                    foreach (var t in tornillos)
                    {
                        var radio = Unidades.AInternas(t.DiametroM / 2);
                        var centro = origen + (XYZ.BasisZ * Unidades.AInternas(t.V)) + (eje * Unidades.AInternas(t.A));
                        torn.Add(Solidos.Cilindro(centro + (u * Unidades.AInternas(t.U0)), u,
                            Unidades.AInternas(t.U1 - t.U0), radio, tornilleria));

                        // Tuerca de los dos lados, contra la cara de fuera de las placas.
                        var cara = Unidades.AInternas(t.U1 - 0.025);
                        var llave = Unidades.AInternas(1.5 * t.DiametroM);
                        var alto = Unidades.AInternas(0.8 * t.DiametroM);
                        torn.Add(Solidos.Tuerca(centro + (u * cara), u, XYZ.BasisZ, llave, alto, radio, tornilleria));
                        torn.Add(Solidos.Tuerca(centro - (u * cara), u.Negate(), XYZ.BasisZ, llave, alto, radio, tornilleria));
                    }

                    var llaveTrabe = LlaveDetalle.De(ClaseDetalle.Encamisado, trabe.Id.Value);

                    if (Solidos.Borrar(doc, llaveTrabe) > 0)
                    {
                        r.Reemplazadas++;
                    }

                    var que = o.Tipo == TipoEncamisado.Placas
                        ? $"placas de {Mm(o.EspesorM)} mm"
                        : $"angulos de {Mm(o.LadoAnguloM)}x{Mm(o.EspesorM)} mm y soleras de {Mm(o.AnchoSoleraM)}x{Mm(o.EspesorSoleraM)} mm @ {Cm(o.SeparacionM)} cm";

                    Solidos.Forma(doc, categoria, acer, llaveTrabe, $"CadLink: encamisado con {que}", string.Empty);

                    if (pega.Count > 0)
                    {
                        Solidos.Forma(doc, categoria, pega, llaveTrabe + "|adhesivo",
                            $"CadLink: adhesivo epoxico de {Mm(o.AdhesivoM)} mm del encamisado", string.Empty);
                    }

                    if (torn.Count > 0)
                    {
                        Solidos.Forma(doc, categoria, torn, llaveTrabe + "|tornillos",
                            $"CadLink: {tornillos.Count} tornillos pasantes de {Mm(o.TornilloM)} mm @ {Cm(o.SeparacionM)} cm",
                            string.Empty);
                    }

                    r.Hechas++;
                }
                catch (Exception e)
                {
                    r.Errores.Add($"«{nombre}»: No se pudo encamisar. {e.Message}");
                }
            }
        });
    }

    /// <summary>Los materiales de las conexiones, buscados o creados una sola vez por boton.</summary>
    private sealed class Materiales
    {
        public Materiales(Document doc)
        {
            Acero = Solidos.Acero(doc);
            Anclas = Solidos.Anclas(doc);
            Soldadura = Solidos.Soldadura(doc);
        }

        public ElementId Acero { get; }

        public ElementId Anclas { get; }

        public ElementId Soldadura { get; }
    }

    private static CajaLocal AInternas(CajaLocal c) => c with
    {
        U0 = Unidades.AInternas(c.U0), U1 = Unidades.AInternas(c.U1),
        V0 = Unidades.AInternas(c.V0), V1 = Unidades.AInternas(c.V1),
        A0 = Unidades.AInternas(c.A0), A1 = Unidades.AInternas(c.A1)
    };

    private static V3 V(XYZ p) => new(p.X, p.Y, p.Z);

    private static List<BoundingBoxXYZ> Cajas(IEnumerable<Element> piezas) =>
        piezas.Select(c => c.get_BoundingBox(null)).Where(c => c is not null).Cast<BoundingBoxXYZ>().ToList();

    /// <summary>Las caras planas de la viga cuya normal va a lo largo de su eje.</summary>
    private static List<PlanarFace> CarasDeExtremo(Element viga, XYZ eje)
    {
        var res = new List<PlanarFace>();
        var ejeV = new V3(eje.X, eje.Y, eje.Z);

        foreach (var solido in SolidosDe(viga))
        {
            foreach (var f in solido.Faces)
            {
                if (f is PlanarFace pf
                    && Cordon.EsCaraDeExtremo(new V3(pf.FaceNormal.X, pf.FaceNormal.Y, pf.FaceNormal.Z), ejeV))
                {
                    res.Add(pf);
                }
            }
        }

        return res;
    }

    /// <summary>
    /// La cara del extremo: en el arranque, la que mira hacia atras y queda mas atras; en el
    /// final, la que mira hacia delante y queda mas adelante.
    /// </summary>
    private static PlanarFace? Extremo(List<PlanarFace> caras, XYZ p0, XYZ eje, int extremo)
    {
        var signo = extremo == 0 ? -1.0 : 1.0;
        PlanarFace? mejor = null;
        var mejorT = double.NegativeInfinity;

        foreach (var c in caras)
        {
            if (c.FaceNormal.DotProduct(eje) * signo <= 0)
            {
                continue;
            }

            var t = (c.Origin - p0).DotProduct(eje) * signo;

            if (t > mejorT)
            {
                mejorT = t;
                mejor = c;
            }
        }

        return mejor;
    }

    private static bool Toca(List<BoundingBoxXYZ> cajas, XYZ p, double holgura)
    {
        foreach (var c in cajas)
        {
            if (p.X >= c.Min.X - holgura && p.X <= c.Max.X + holgura
                && p.Y >= c.Min.Y - holgura && p.Y <= c.Max.Y + holgura
                && p.Z >= c.Min.Z - holgura && p.Z <= c.Max.Z + holgura)
            {
                return true;
            }
        }

        return false;
    }

    // ======================================================================
    //  LA COLUMNA, MEDIDA DE SU GEOMETRIA
    // ======================================================================

    /// <summary>
    /// Donde arranca la columna y como esta girada, medido de lo que Revit dibujo.
    /// </summary>
    /// <param name="Centro">El centro del paño, a la cota del arranque.</param>
    /// <param name="Ex">La X de la familia, horizontal y unitaria.</param>
    /// <param name="Ey">La Y de la familia, perpendicular a <paramref name="Ex"/>.</param>
    /// <param name="LadoX">Lo que mide el paño a lo largo de <paramref name="Ex"/>.</param>
    /// <param name="Contorno">El paño de abajo: lo que se suelda a la placa.</param>
    private readonly record struct MarcoColumna(
        XYZ Centro, double BaseZ, XYZ Ex, XYZ Ey, double LadoX, double LadoY, CurveLoop Contorno);

    /// <summary>
    /// El arranque de la columna se toma de su CARA DE ABAJO, no de su nivel y su desfase. Con
    /// <c>abajo</c> en false, lo mismo con la cara de arriba: la cabeza.
    /// </summary>
    /// <remarks>
    /// La cara es lo que de verdad se apoya en la placa. El nivel mas el desfase daria lo mismo en
    /// una columna recta, pero no en una inclinada ni en una familia con el origen en otro sitio, y
    /// ademas la cara trae el CONTORNO, que es lo que hace falta para la soldadura.
    /// </remarks>
    /// <param name="abajo">La cara de abajo -el arranque-; con false, la de arriba -la cabeza-.</param>
    private static MarcoColumna? MarcoDeColumna(FamilyInstance col, bool abajo = true)
    {
        PlanarFace? cara = null;
        var signo = abajo ? -1.0 : 1.0;

        foreach (var solido in SolidosDe(col))
        {
            foreach (var f in solido.Faces)
            {
                if (f is PlanarFace pf && pf.FaceNormal.Z * signo > 0.999
                    && (cara is null || (pf.Origin.Z - cara.Origin.Z) * signo > 1e-6))
                {
                    cara = pf;
                }
            }
        }

        if (cara is null)
        {
            return null;
        }

        var contorno = ContornoExterior(cara);

        if (contorno is null)
        {
            return null;
        }

        var ex = new XYZ(col.HandOrientation.X, col.HandOrientation.Y, 0);
        ex = ex.GetLength() < 1e-9 ? XYZ.BasisX : ex.Normalize();
        var ey = XYZ.BasisZ.CrossProduct(ex).Normalize();

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        var z = cara.Origin.Z;

        foreach (var curva in contorno)
        {
            foreach (var p in curva.Tessellate())
            {
                var u = p.DotProduct(ex);
                var v = p.DotProduct(ey);
                minX = Math.Min(minX, u);
                maxX = Math.Max(maxX, u);
                minY = Math.Min(minY, v);
                maxY = Math.Max(maxY, v);
            }
        }

        if (minX > maxX)
        {
            return null;
        }

        var centro = (ex * ((minX + maxX) / 2)) + (ey * ((minY + maxY) / 2)) + new XYZ(0, 0, z);

        return new MarcoColumna(centro, z, ex, ey, maxX - minX, maxY - minY, contorno);
    }

    /// <summary>El contorno de fuera de una cara: el mas largo de sus lazos.</summary>
    /// <remarks>En un tubo la cara tiene dos lazos, el de fuera y el del hueco: se suelda el de fuera.</remarks>
    private static CurveLoop? ContornoExterior(Face cara)
    {
        CurveLoop? mejor = null;
        var largo = 0.0;

        foreach (var lazo in cara.GetEdgesAsCurveLoops())
        {
            var l = lazo.GetExactLength();

            if (l > largo)
            {
                largo = l;
                mejor = lazo;
            }
        }

        return mejor;
    }

    /// <summary>Los solidos de la pieza en coordenadas del modelo.</summary>
    private static List<Solid> SolidosDe(Element e)
    {
        var res = new List<Solid>();
        var geo = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });

        if (geo is null)
        {
            return res;
        }

        void Juntar(IEnumerable<GeometryObject> objetos)
        {
            foreach (var o in objetos)
            {
                if (o is Solid s && s.Volume > 1e-9)
                {
                    res.Add(s);
                }
                else if (o is GeometryInstance gi)
                {
                    Juntar(gi.GetInstanceGeometry());
                }
            }
        }

        Juntar(geo);

        return res;
    }

    // ======================================================================
    //  LA TRANSACCION Y LOS TEXTOS
    // ======================================================================

    private static ResultadoDetalles EnTransaccion(Document doc, string nombre, Action<ResultadoDetalles> trabajo)
    {
        var r = new ResultadoDetalles();

        using var t = new Transaction(doc, nombre);
        t.Start();

        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            trabajo(r);
            t.Commit();
        }
        catch (Exception e)
        {
            t.RollBack();
            r.Hechas = 0;
            r.Reemplazadas = 0;
            r.Errores.Add("Se deshizo todo lo de este boton: " + e.Message);
        }

        return r;
    }

    private static string TextoDeMedidas(PlacaBaseJson p) =>
        $"{Cm(p.LadoXM)}x{Cm(p.LadoYM)} cm, espesor {Mm(p.EspesorM)} mm {p.Acero}".TrimEnd();

    private static string Fc(string fc) =>
        string.IsNullOrWhiteSpace(fc) ? string.Empty : $", f'c {fc.Trim()} kg/cm²";

    private static string Cm(double m) =>
        (m * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string Mm(double m) =>
        (m * 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
