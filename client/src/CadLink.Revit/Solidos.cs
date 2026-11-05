using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// Los solidos de las placas, las zapatas y las soldaduras, y las formas que los llevan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Todo va como DirectShape</b>, no como familia. Una placa base con sus anclas, una zapata
/// con su dado o un cordon de soldadura alrededor de un perfil cualquiera no tienen una familia
/// de fabrica que los represente, y depender de una familia que el usuario tenga cargada -con
/// sus parametros con el nombre justo, en su idioma- es la forma mas segura de que no salga
/// nada. Un DirectShape sale siempre igual, en cualquier plantilla.
/// </para>
/// <para>
/// El precio: no es una familia parametrica, asi que no se edita arrastrando. Se cambia en
/// CadLink y se vuelve a poner, y la llave de <see cref="LlaveDetalle"/> hace que reemplace a la
/// anterior en lugar de encimarse.
/// </para>
/// <para>
/// Todas las medidas que entran aqui ya estan en unidades INTERNAS de Revit. La conversion se
/// hace una sola vez, en <see cref="Unidades"/>.
/// </para>
/// </remarks>
internal static class Solidos
{
    // ---------------------------------------------------------------------
    //  Materiales
    // ---------------------------------------------------------------------

    /// <summary>El material por nombre; si no existe, se crea con su color.</summary>
    /// <remarks>
    /// Con su color propio cada parte se distingue en una vista sombreada sin hacer nada: la placa
    /// gris, la soldadura casi negra, el grout arena y el concreto claro.
    /// </remarks>
    public static ElementId MaterialDe(Document doc, string nombre, byte r, byte g, byte b)
    {
        foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(Material)))
        {
            if (e is Material m && string.Equals(m.Name, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return m.Id;
            }
        }

        var id = Material.Create(doc, nombre);

        if (doc.GetElement(id) is Material nuevo)
        {
            nuevo.Color = new Color(r, g, b);
        }

        return id;
    }

    public static ElementId Acero(Document doc) => MaterialDe(doc, "CadLink - Acero", 120, 128, 136);

    public static ElementId Anclas(Document doc) => MaterialDe(doc, "CadLink - Anclas", 70, 74, 80);

    public static ElementId Soldadura(Document doc) => MaterialDe(doc, "CadLink - Soldadura", 40, 40, 40);

    public static ElementId Grout(Document doc) => MaterialDe(doc, "CadLink - Grout", 176, 160, 128);

    public static ElementId Concreto(Document doc) => MaterialDe(doc, "CadLink - Concreto", 192, 192, 188);

    public static ElementId Plantilla(Document doc) => MaterialDe(doc, "CadLink - Plantilla", 214, 204, 176);

    // ---------------------------------------------------------------------
    //  Formas
    // ---------------------------------------------------------------------

    /// <summary>
    /// La primera categoria de la lista que acepte un DirectShape. Modelo generico siempre lo acepta.
    /// </summary>
    public static ElementId Categoria(Document doc, params BuiltInCategory[] preferidas)
    {
        foreach (var c in preferidas)
        {
            var id = new ElementId(c);

            if (DirectShape.IsValidCategoryId(id, doc))
            {
                return id;
            }
        }

        return new ElementId(BuiltInCategory.OST_GenericModel);
    }

    /// <summary>Borra lo que CadLink ya habia puesto con esta llave. Devuelve cuantas formas.</summary>
    public static int Borrar(Document doc, string llave)
    {
        var ids = new List<ElementId>();

        foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(DirectShape)))
        {
            if (e is DirectShape ds
                && ds.ApplicationId == LlaveDetalle.Aplicacion
                && ds.ApplicationDataId == llave)
            {
                ids.Add(ds.Id);
            }
        }

        if (ids.Count > 0)
        {
            doc.Delete(ids);
        }

        return ids.Count;
    }

    /// <summary>Lo mas bajo de lo que CadLink puso con esta llave, o null si no hay nada.</summary>
    public static double? FondoDe(Document doc, string llave)
    {
        double? fondo = null;

        foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(DirectShape)))
        {
            if (e is DirectShape ds
                && ds.ApplicationId == LlaveDetalle.Aplicacion
                && ds.ApplicationDataId == llave
                && ds.get_BoundingBox(null) is { } caja)
            {
                fondo = fondo is null ? caja.Min.Z : Math.Min(fondo.Value, caja.Min.Z);
            }
        }

        return fondo;
    }

    /// <summary>Una forma con sus solidos, marcada con la llave para encontrarla despues.</summary>
    public static DirectShape Forma(
        Document doc, ElementId categoria, IList<GeometryObject> solidos, string llave,
        string comentario, string marca)
    {
        var ds = DirectShape.CreateElement(doc, categoria);
        ds.ApplicationId = LlaveDetalle.Aplicacion;
        ds.ApplicationDataId = llave;
        ds.SetShape(solidos);

        Texto(ds, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, comentario);
        Texto(ds, BuiltInParameter.ALL_MODEL_MARK, marca);

        return ds;
    }

    private static void Texto(Element e, BuiltInParameter p, string valor)
    {
        if (valor.Length == 0)
        {
            return;
        }

        var par = e.get_Parameter(p);

        if (par is not null && !par.IsReadOnly)
        {
            par.Set(valor);
        }
    }

    // ---------------------------------------------------------------------
    //  Solidos sencillos
    // ---------------------------------------------------------------------

    /// <summary>Un poligono cerrado, sin repetir el primer punto.</summary>
    public static CurveLoop Poligono(IReadOnlyList<XYZ> puntos)
    {
        var lazo = new CurveLoop();

        for (var i = 0; i < puntos.Count; i++)
        {
            lazo.Append(Line.CreateBound(puntos[i], puntos[(i + 1) % puntos.Count]));
        }

        return lazo;
    }

    /// <summary>
    /// Las cuatro esquinas de un rectangulo horizontal centrado en <paramref name="centro"/>, a lo
    /// largo de <paramref name="ex"/> y <paramref name="ey"/>.
    /// </summary>
    public static List<XYZ> Rectangulo(XYZ centro, XYZ ex, XYZ ey, double ladoX, double ladoY)
    {
        var hx = ex * (ladoX / 2);
        var hy = ey * (ladoY / 2);

        return new List<XYZ>
        {
            centro - hx - hy,
            centro + hx - hy,
            centro + hx + hy,
            centro - hx + hy
        };
    }

    /// <summary>Un prisma recto: el poligono, de <paramref name="z0"/> a <paramref name="z1"/>.</summary>
    /// <remarks>Las Z de los puntos se ignoran: manda <paramref name="z0"/>.</remarks>
    public static Solid Prisma(IReadOnlyList<XYZ> planta, double z0, double z1, ElementId material)
    {
        var abajo = planta.Select(p => new XYZ(p.X, p.Y, Math.Min(z0, z1))).ToList();

        return GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { Poligono(abajo) }, XYZ.BasisZ, Math.Abs(z1 - z0),
            new SolidOptions(material, ElementId.InvalidElementId));
    }

    /// <summary>Un circulo en el plano normal a <paramref name="eje"/>.</summary>
    public static CurveLoop CirculoEn(XYZ centro, double radio, XYZ eje)
    {
        var (u, v) = Perpendiculares(eje);

        var lazo = new CurveLoop();
        lazo.Append(Arc.Create(centro, radio, 0, Math.PI, u, v));
        lazo.Append(Arc.Create(centro, radio, Math.PI, 2 * Math.PI, u, v));

        return lazo;
    }

    /// <summary>Un cilindro de <paramref name="desde"/> a lo largo de <paramref name="eje"/> unitario.</summary>
    public static Solid Cilindro(XYZ desde, XYZ eje, double largo, double radio, ElementId material) =>
        GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { CirculoEn(desde, radio, eje) }, eje, largo,
            new SolidOptions(material, ElementId.InvalidElementId));

    /// <summary>Un prisma hexagonal vertical: la tuerca. <paramref name="entreCaras"/> es la llave.</summary>
    public static Solid Hexagono(XYZ centro, XYZ ex, double entreCaras, double z0, double alto, ElementId material)
    {
        var radio = entreCaras / Math.Sqrt(3);
        var ey = XYZ.BasisZ.CrossProduct(ex);
        var puntos = new List<XYZ>();

        for (var i = 0; i < 6; i++)
        {
            var a = (Math.PI / 6) + (i * Math.PI / 3);
            puntos.Add(centro + (ex * (radio * Math.Cos(a))) + (ey * (radio * Math.Sin(a))));
        }

        return Prisma(puntos, z0, z0 + alto, material);
    }

    /// <summary>Quita <paramref name="hueco"/> de <paramref name="solido"/>; si Revit no puede, lo deja entero.</summary>
    public static Solid Restar(Solid solido, Solid hueco)
    {
        try
        {
            return BooleanOperationsUtils.ExecuteBooleanOperation(
                solido, hueco, BooleanOperationsType.Difference) ?? solido;
        }
        catch (Exception)
        {
            return solido;
        }
    }

    // ---------------------------------------------------------------------
    //  El cordon de soldadura
    // ---------------------------------------------------------------------

    /// <summary>
    /// Un filete de soldadura de lado <paramref name="lado"/> alrededor de un contorno cerrado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="contorno"/> es el paño de la pieza que se suelda, en el plano de la otra:
    /// la cara de abajo de la columna sobre la placa, o la cara del extremo de la viga sobre la
    /// columna. <paramref name="haciaLaPieza"/> apunta de ese plano hacia dentro de la pieza.
    /// </para>
    /// <para>
    /// La seccion es el triangulo del filete: una pata sobre el plano, hacia fuera del paño, y la
    /// otra sobre la pieza. Se arma como una piramide truncada -del contorno crecido
    /// <paramref name="lado"/> en el plano al contorno subido <paramref name="lado"/> sobre la
    /// pieza- menos la pieza misma. Funciona con cualquier perfil -I, cajon, tubo, angulo- porque
    /// el contorno sale de la geometria que Revit ya dibujo, no de una tabla.
    /// </para>
    /// <para>
    /// Si Revit no puede hacer la piramide -pasa con contornos con aristas muy cortas-, el cordon
    /// sale de seccion CUADRADA de medio lado. Se nota a simple vista y se avisa, pero el cordon
    /// esta y da la vuelta completa, que es lo que importa del detalle.
    /// </para>
    /// </remarks>
    /// <param name="cuadrado">Se pone en true si hubo que hacerlo cuadrado.</param>
    public static Solid Filete(
        CurveLoop contorno, XYZ haciaLaPieza, double lado, ElementId material, out bool cuadrado)
    {
        cuadrado = false;
        var opciones = new SolidOptions(material, ElementId.InvalidElementId);
        var n = haciaLaPieza.Normalize();

        var fuera = HaciaFuera(contorno, lado, n);

        try
        {
            var arriba = CurveLoop.CreateViaTransform(contorno, Transform.CreateTranslation(n * lado));
            var piramide = GeometryCreationUtilities.CreateBlendGeometry(fuera, arriba, null, opciones);
            var pieza = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { contorno }, n, lado, opciones);

            var filete = BooleanOperationsUtils.ExecuteBooleanOperation(
                piramide, pieza, BooleanOperationsType.Difference);

            if (filete is not null && filete.Volume > 1e-12)
            {
                return filete;
            }
        }
        catch (Exception)
        {
            // Se cae a la seccion cuadrada, abajo.
        }

        cuadrado = true;

        var anillo = GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { fuera }, n, lado / 2, opciones);
        var hueco = GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { contorno }, n, lado / 2, opciones);

        return BooleanOperationsUtils.ExecuteBooleanOperation(
            anillo, hueco, BooleanOperationsType.Difference);
    }

    /// <summary>El contorno crecido <paramref name="lado"/> hacia fuera, en su propio plano.</summary>
    /// <remarks>Ver <see cref="Cordon.ElPrimeroEsElDeFuera"/>: se prueba hacia los dos lados.</remarks>
    private static CurveLoop HaciaFuera(CurveLoop contorno, double lado, XYZ normal)
    {
        // Hacia DENTRO puede no existir: un tubo con esquinas de radio menor que el filete no se
        // puede encoger tanto, y Revit lanza. Entonces el que si salio es el de fuera.
        var a = Desplazado(contorno, lado, normal);
        var b = Desplazado(contorno, -lado, normal);

        if (a is null || b is null)
        {
            return a ?? b ?? throw new InvalidOperationException(
                "Revit no pudo crecer el contorno de la pieza para el filete.");
        }

        return Cordon.ElPrimeroEsElDeFuera(a.GetExactLength(), b.GetExactLength()) ? a : b;
    }

    private static CurveLoop? Desplazado(CurveLoop contorno, double distancia, XYZ normal)
    {
        try
        {
            return CurveLoop.CreateViaOffset(contorno, distancia, normal);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Dos direcciones unitarias perpendiculares al eje y entre si.</summary>
    public static (XYZ U, XYZ V) Perpendiculares(XYZ eje)
    {
        var n = eje.Normalize();
        var auxiliar = Math.Abs(n.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
        var u = auxiliar.CrossProduct(n).Normalize();

        return (u, n.CrossProduct(u).Normalize());
    }
}
