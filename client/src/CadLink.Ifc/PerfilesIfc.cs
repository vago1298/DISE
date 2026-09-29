namespace CadLink.Ifc;

/// <summary>
/// Convierte una <see cref="SeccionIfc"/> en el perfil PARAMETRICO de IFC que le
/// corresponde.
/// </summary>
/// <remarks>
/// <para>
/// Parametrico y no un contorno de puntos, aunque el contorno seria mas facil. Un
/// <c>IfcArbitraryClosedProfileDef</c> llega a Revit como un solido cualquiera; un
/// <c>IfcIShapeProfileDef</c> llega como un perfil con su peralte, su ancho, su alma y su
/// patin, que es lo que permite emparejarlo con una familia. Como el objetivo es
/// justamente elegir la familia de cada seccion en Revit, el perfil tiene que ser
/// parametrico.
/// </para>
/// <para>
/// El orden de los atributos de cada entidad se saco del esquema EXPRESS oficial
/// (IFC4_ADD2_TC1.exp), no de memoria. Va anotado en cada llamada, y
/// <c>tools/verificar_ifc.py</c> comprueba las cuentas contra la misma tabla.
/// </para>
/// </remarks>
internal static class PerfilesIfc
{
    /// <summary>
    /// Tope de un espesor respecto de la medida que lo contiene.
    /// </summary>
    /// <remarks>
    /// El esquema de IFC no solo describe la forma: impone reglas. Un
    /// <c>IfcUShapeProfileDef</c> exige que el patin sea menor que MEDIO peralte, un
    /// <c>IfcIShapeProfileDef</c> que los dos patines juntos quepan en el peralte, y un
    /// <c>IfcLShapeProfileDef</c> que el espesor sea menor que las dos piernas. Un modelo
    /// heredado con una seccion capturada a lo bruto -alma de 30 cm en un perfil de 20-
    /// incumple esas reglas, y entonces el archivo no es invalido "un poco": un lector
    /// estricto lo rechaza entero y no se importa nada.
    /// Asi que el espesor se recorta y se deja aviso. Es la misma proporcion que usa
    /// <c>Perfil2D.FraccionMaxima</c> para la vista previa, para que el IFC y lo que se ve
    /// en pantalla no se contradigan.
    /// </remarks>
    public const double FraccionMaxima = 0.45;

    /// <summary>Escribe el perfil y devuelve su referencia.</summary>
    /// <param name="p">Donde se escribe.</param>
    /// <param name="s">La seccion.</param>
    /// <param name="pos2d">Un <c>IfcAxis2Placement2D</c> en el origen, compartido.</param>
    /// <param name="avisos">Donde se apunta lo que se tuvo que corregir.</param>
    public static Ref Escribir(EscritorPaso p, SeccionIfc s, Ref pos2d, List<string> avisos)
    {
        var area = new Enumeracion("AREA");
        var nombre = string.IsNullOrWhiteSpace(s.Nombre) ? null : s.Nombre.Trim();

        // Un ancho o un peralte en cero significa que no se pudo leer la propiedad. Se
        // pone algo visible en vez de exportar una barra de espesor nulo, que en Revit
        // aparece como un elemento sin geometria y es mas dificil de encontrar que un
        // elemento con una medida obviamente falsa.
        var b = s.AnchoM > 1e-6 ? s.AnchoM : Sustituto(s, avisos, "ancho");
        var h = s.PeralteM > 1e-6 ? s.PeralteM : Sustituto(s, avisos, "peralte");

        switch (s.Forma)
        {
            case FormaIfc.Circulo:
                // IfcCircleProfileDef [4]: ProfileType, ProfileName, Position, Radius
                return p.Ent("IFCCIRCLEPROFILEDEF", area, nombre, pos2d, b / 2.0);

            case FormaIfc.Tubo:
            {
                var r = b / 2.0;
                var t = Recortar(s.ParedM, r, s, avisos, "pared del tubo", Math.Min(b, h) * 0.06);

                // IfcCircleHollowProfileDef [5]: + WallThickness
                return p.Ent("IFCCIRCLEHOLLOWPROFILEDEF", area, nombre, pos2d, r, t);
            }

            case FormaIfc.Cajon:
            {
                var t = Recortar(s.ParedM, Math.Min(b, h) / 2.0, s, avisos,
                    "pared del cajon", Math.Min(b, h) * 0.06);

                // IfcRectangleHollowProfileDef [8]: ProfileType, ProfileName, Position,
                //   XDim, YDim, WallThickness, InnerFilletRadius, OuterFilletRadius
                return p.Ent("IFCRECTANGLEHOLLOWPROFILEDEF",
                    area, nombre, pos2d, b, h, t, null, null);
            }

            case FormaIfc.PerfilI:
            {
                // La regla del esquema es que los DOS patines quepan en el peralte, asi
                // que el tope de cada uno es la mitad de lo que se recorta.
                var tf = Recortar(s.PatinM, h / 2.0, s, avisos, "patin", h * 0.08);
                var tw = Recortar(s.AlmaM, b, s, avisos, "alma", b * 0.06);

                // IfcIShapeProfileDef [10]: ProfileType, ProfileName, Position,
                //   OverallWidth, OverallDepth, WebThickness, FlangeThickness,
                //   FilletRadius, FlangeEdgeRadius, FlangeSlope
                return p.Ent("IFCISHAPEPROFILEDEF",
                    area, nombre, pos2d, b, h, tw, tf, null, null, null);
            }

            case FormaIfc.PerfilC:
            {
                var tf = Recortar(s.PatinM, h / 2.0, s, avisos, "patin", h * 0.08);
                var tw = Recortar(s.AlmaM, b, s, avisos, "alma", b * 0.06);

                // Un canal laminado es la U de IFC, NO la C. IfcCShapeProfileDef es el
                // perfil doblado en frio, de espesor constante y con los bordes vueltos
                // hacia dentro; no tiene donde poner un alma y un patin distintos, que es
                // justo lo que trae una seccion de ETABS.
                // IfcUShapeProfileDef [10]: ProfileType, ProfileName, Position,
                //   Depth, FlangeWidth, WebThickness, FlangeThickness,
                //   FilletRadius, EdgeRadius, FlangeSlope
                return p.Ent("IFCUSHAPEPROFILEDEF",
                    area, nombre, pos2d, h, b, tw, tf, null, null, null);
            }

            case FormaIfc.PerfilT:
            {
                var tf = Recortar(s.PatinM, h, s, avisos, "patin", h * 0.08);
                var tw = Recortar(s.AlmaM, b, s, avisos, "alma", b * 0.06);

                // IfcTShapeProfileDef [12]: ProfileType, ProfileName, Position,
                //   Depth, FlangeWidth, WebThickness, FlangeThickness,
                //   FilletRadius, FlangeEdgeRadius, WebEdgeRadius, WebSlope, FlangeSlope
                return p.Ent("IFCTSHAPEPROFILEDEF",
                    area, nombre, pos2d, h, b, tw, tf, null, null, null, null, null);
            }

            case FormaIfc.PerfilL:
            {
                // El angulo lleva UN espesor para las dos piernas, asi que se toma el alma
                // y se recorta contra la pierna mas corta.
                var t = Recortar(s.AlmaM, Math.Min(b, h), s, avisos, "espesor del angulo",
                    Math.Min(b, h) * 0.08);

                // IfcLShapeProfileDef [9]: ProfileType, ProfileName, Position,
                //   Depth, Width, Thickness, FilletRadius, EdgeRadius, LegSlope
                return p.Ent("IFCLSHAPEPROFILEDEF",
                    area, nombre, pos2d, h, b, t, null, null, null);
            }

            case FormaIfc.Rectangulo:
            default:
                // El rectangulo es tambien el respaldo: es lo que usa el propio lector de
                // ETABS cuando no consigue identificar la seccion, asi que una forma que
                // no se reconozca sale como una caja de sus medidas en vez de perderse.
                // IfcRectangleProfileDef [5]: ProfileType, ProfileName, Position, XDim, YDim
                return p.Ent("IFCRECTANGLEPROFILEDEF", area, nombre, pos2d, b, h);
        }
    }

    /// <summary>El nombre de la entidad de perfil que le toca a cada forma.</summary>
    /// <remarks>Existe para poder comprobarlo en las pruebas sin escribir el archivo.</remarks>
    public static string Entidad(FormaIfc forma, double paredM) => forma switch
    {
        FormaIfc.Rectangulo => "IFCRECTANGLEPROFILEDEF",
        FormaIfc.Circulo => "IFCCIRCLEPROFILEDEF",
        FormaIfc.Tubo => "IFCCIRCLEHOLLOWPROFILEDEF",
        FormaIfc.Cajon => "IFCRECTANGLEHOLLOWPROFILEDEF",
        FormaIfc.PerfilI => "IFCISHAPEPROFILEDEF",
        FormaIfc.PerfilC => "IFCUSHAPEPROFILEDEF",
        FormaIfc.PerfilT => "IFCTSHAPEPROFILEDEF",
        FormaIfc.PerfilL => "IFCLSHAPEPROFILEDEF",
        _ => "IFCRECTANGLEPROFILEDEF"
    };

    private static double Recortar(
        double valor, double maximo, SeccionIfc s, List<string> avisos,
        string que, double porOmision)
    {
        var tope = maximo * FraccionMaxima;

        if (valor <= 1e-6)
        {
            // No venia. Se pone la proporcion de Perfil2D, sin avisar: es lo que la
            // aplicacion ya dibuja en la vista previa, asi que no es una sorpresa.
            return Math.Min(porOmision > 1e-6 ? porOmision : tope, tope);
        }

        if (valor <= tope)
        {
            return valor;
        }

        avisos.Add(
            $"La seccion «{s.Nombre}» traia {que} de {valor * 100:0.#} cm, "
            + $"que no cabe en la seccion; se exporto con {tope * 100:0.#} cm.");

        return tope;
    }

    private static double Sustituto(SeccionIfc s, List<string> avisos, string que)
    {
        avisos.Add(
            $"La seccion «{s.Nombre}» no traia {que}; se exporto con 12 cm "
            + "para que la pieza se vea y se pueda corregir.");

        return 0.12;
    }
}
