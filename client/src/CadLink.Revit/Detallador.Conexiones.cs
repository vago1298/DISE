using Autodesk.Revit.DB;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

// ============================================================================
//  LAS CONEXIONES DEL LIBRO DE EXCEL EN REVIT
//
//  El nucleo arma cada conexion en sus ejes locales y en metros (ModeloConexion). Aqui solo se
//  encuentran esos ejes en el modelo -en el extremo de la viga, a la mitad de la viga, en el
//  extremo o en el centro de la diagonal- y cada pieza se vuelve un solido: las placas extruidas
//  de su contorno, los cordones como prismas triangulares, los tornillos como cilindro con cabeza
//  y tuerca. Los miembros (viga, columna, diagonal) ya estan modelados y no se tocan.
// ============================================================================

internal static partial class Detallador
{
    /// <summary>Los ejes de la conexion en el modelo, en pies.</summary>
    private readonly record struct EjesConexion(XYZ Origen, XYZ X, XYZ Y, XYZ Z);

    /// <summary>La conexion en el extremo de una viga: a la columna o a la viga que la soporta.</summary>
    public static ResultadoDetalles ConexionEnExtremo(
        Document doc, VigaElegida elegida, ModeloConexion m, IReadOnlyList<Element> columnas)
    {
        return EnTransaccion(doc, "Conexión del libro de acero (CadLink)", r =>
        {
            var viga = elegida.Viga;
            var donde = $"«viga {viga.Id.Value}»";

            if (viga.Location is not LocationCurve lc || lc.Curve is not Line linea)
            {
                r.Errores.Add($"{donde}: Viga curva. Solo se conectan vigas rectas.");
                return;
            }

            var p0 = linea.GetEndPoint(0);
            var p1 = linea.GetEndPoint(1);
            var eje = (p1 - p0).Normalize();
            var extremo = elegida.Clic is { } clic ? Conexion.ExtremoMasCercano(V(p0), V(p1), V(clic)) : 0;
            donde = $"«viga {viga.Id.Value}, extremo {extremo + 1}»";
            var cara = Extremo(CarasDeExtremo(viga, eje), p0, eje, extremo);

            if (cara is null || ContornoExterior(cara) is not { } contorno)
            {
                r.Errores.Add($"{donde}: El extremo no tiene una cara plana.");
                return;
            }

            var marco = MarcoDeExtremo(cara, contorno);

            // EL CENTRO DEL PERFIL: si la viga va despatinada su cara del extremo es solo el alma
            // que queda, y el centro de esa cara no es el del perfil.
            var origen = marco.Origen;

            if (m.CaraArribaM > 0)
            {
                origen += marco.V * (marco.V1 - Unidades.AInternas(m.CaraArribaM));
            }

            if (m.ZCeroEnLaColumna)
            {
                // Z = 0 es la cara de la columna que esta enfrente; si no hay, la de la hoja.
                var hueco = MasCercanoEnfrente(Cajas(columnas), marco);

                if (hueco is null)
                {
                    r.Avisos.Add($"{donde}: No hay una columna de acero enfrente a menos de {Cm(Conexion.HuecoMaximoM)} cm; la conexion se puso a {Cm(m.HolguraM)} cm de la punta de la viga, como dice la hoja.");
                }

                origen -= marco.A * Unidades.AInternas(hueco ?? m.HolguraM);
            }

            var ejes = new EjesConexion(origen, marco.U, marco.V, marco.A);
            Colocar(doc, m, ejes, LlaveDetalle.De(ClaseDetalle.ConexionDelLibro, viga.Id.Value, extremo), donde, r);
        });
    }

    /// <summary>La conexion a la mitad de una viga: el eslabon del contraventeo excentrico.</summary>
    public static ResultadoDetalles ConexionEnViga(Document doc, FamilyInstance viga, ModeloConexion m)
    {
        return EnTransaccion(doc, "Conexión del libro de acero (CadLink)", r =>
        {
            var donde = $"«viga {viga.Id.Value}»";

            if (viga.Location is not LocationCurve lc || lc.Curve is not Line linea)
            {
                r.Errores.Add($"{donde}: Viga curva. Solo se conectan vigas rectas.");
                return;
            }

            var p0 = linea.GetEndPoint(0);
            var p1 = linea.GetEndPoint(1);
            var eje = (p1 - p0).Normalize();
            var caras = CarasDeExtremo(viga, eje);

            // El eje del PERFIL, no la linea de Revit: la linea suele ir en la cara de arriba.
            XYZ Centro(int extremo, XYZ siNo)
            {
                var c = Extremo(caras, p0, eje, extremo);
                return c is not null && ContornoExterior(c) is { } con ? MarcoDeExtremo(c, con).Origen : siNo;
            }

            var a = Centro(0, p0);
            var b = Centro(1, p1);
            var x = (b - a).Normalize();

            if (Math.Abs(x.Z) > 0.5)
            {
                r.Errores.Add($"{donde}: La pieza es casi vertical: el eslabon va en una viga.");
                return;
            }

            var y = (XYZ.BasisZ - (x * XYZ.BasisZ.DotProduct(x))).Normalize();
            var ejes = new EjesConexion((a + b) * 0.5, x, y, x.CrossProduct(y).Normalize());
            Colocar(doc, m, ejes, LlaveDetalle.De(ClaseDetalle.ConexionDelLibro, viga.Id.Value), donde, r);
        });
    }

    /// <summary>
    /// La conexion de una diagonal: en su extremo mas cercano al clic (la placa gusset de la
    /// esquina) o en su centro (el cruce de la X).
    /// </summary>
    public static ResultadoDetalles ConexionEnDiagonal(
        Document doc, FamilyInstance diagonal, XYZ? clic, ModeloConexion m, bool enElCentro)
    {
        return EnTransaccion(doc, "Conexión del libro de acero (CadLink)", r =>
        {
            var donde = $"«diagonal {diagonal.Id.Value}»";

            if (diagonal.Location is not LocationCurve lc || lc.Curve is not Line linea)
            {
                r.Errores.Add($"{donde}: La diagonal tiene que ser recta.");
                return;
            }

            var p0 = linea.GetEndPoint(0);
            var p1 = linea.GetEndPoint(1);

            if (enElCentro)
            {
                var d = p1 - p0;
                var x = new XYZ(d.X, d.Y, 0);

                if (x.GetLength() < 1e-6)
                {
                    r.Errores.Add($"{donde}: La diagonal es vertical.");
                    return;
                }

                x = x.Normalize();
                var ejes = new EjesConexion((p0 + p1) * 0.5, x, XYZ.BasisZ, x.CrossProduct(XYZ.BasisZ).Normalize());
                Colocar(doc, m, ejes, LlaveDetalle.De(ClaseDetalle.ConexionDelLibro, diagonal.Id.Value), donde, r);
                return;
            }

            var extremo = clic is null ? 0 : Conexion.ExtremoMasCercano(V(p0), V(p1), V(clic));
            var pt = extremo == 0 ? p0 : p1;
            var otro = extremo == 0 ? p1 : p0;
            var hacia = otro - pt;
            var h = new XYZ(hacia.X, hacia.Y, 0);

            if (h.GetLength() < 1e-6 || Math.Abs(hacia.Z) < 1e-6)
            {
                r.Errores.Add($"{donde}: La diagonal tiene que ir inclinada, ni vertical ni horizontal.");
                return;
            }

            // X: de la columna hacia la diagonal; Y: de la viga hacia la diagonal (arriba si la
            // diagonal sube desde ese extremo, abajo si baja).
            var ex = h.Normalize();
            var ey = hacia.Z > 0 ? XYZ.BasisZ : XYZ.BasisZ.Negate();
            var ejesD = new EjesConexion(pt, ex, ey, ex.CrossProduct(ey).Normalize());

            var angulo = Math.Atan2(h.GetLength(), Math.Abs(hacia.Z)) * 180 / Math.PI;

            if (m.AnguloDiagonalGrados > 0 && Math.Abs(angulo - m.AnguloDiagonalGrados) > 2)
            {
                r.Avisos.Add($"{donde}: En el modelo va a {angulo:0.0}° de la vertical y la hoja dice {m.AnguloDiagonalGrados:0.0}°; la placa se armo con la hoja.");
            }

            r.Avisos.Add($"{donde}: El punto de trabajo es el extremo {extremo + 1} de la diagonal; si en el modelo no llega al cruce de los ejes de viga y columna, la placa queda corrida lo mismo.");
            Colocar(doc, m, ejesD, LlaveDetalle.De(ClaseDetalle.ConexionDelLibro, diagonal.Id.Value, extremo), donde, r);
        });
    }

    /// <summary>Las piezas de la conexion como formas de Revit, en los ejes dados.</summary>
    private static void Colocar(Document doc, ModeloConexion m, EjesConexion e, string llave, string donde, ResultadoDetalles r)
    {
        var mat = new Materiales(doc);
        var categoria = Solidos.Categoria(doc, BuiltInCategory.OST_StructConnections, BuiltInCategory.OST_GenericModel);

        XYZ P(V3 p) => e.Origen + (e.X * Unidades.AInternas(p.X)) + (e.Y * Unidades.AInternas(p.Y)) + (e.Z * Unidades.AInternas(p.Z));
        XYZ D(V3 d) => ((e.X * d.X) + (e.Y * d.Y) + (e.Z * d.Z)).Normalize();

        // Todo se calcula ANTES de borrar lo anterior: si una pieza no sale, no se pierde nada.
        var piezas = new List<(GeometryObject Solido, string Que)>();

        foreach (var pl in m.Placas.Where(p => !p.SoloDibujo))
        {
            try
            {
                var puntos = pl.Contorno.Select(c => P(pl.Punto(c.U, c.V))).ToList();
                var solido = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { Solidos.Poligono(puntos) }, D(pl.N), Unidades.AInternas(pl.Espesor),
                    new SolidOptions(mat.Acero, ElementId.InvalidElementId));
                piezas.Add((solido, pl.Rotulo));
            }
            catch (Exception ex)
            {
                r.Avisos.Add($"{donde}: No salio «{pl.Rotulo}»: {ex.Message}");
            }
        }

        foreach (var c in m.Cordones)
        {
            try
            {
                var a = P(c.P0);
                var largo = (P(c.P1) - a).GetLength();
                var triangulo = new List<XYZ> { a, P(c.P0 + (c.Pata1 * c.Lado)), P(c.P0 + (c.Pata2 * c.Lado)) };
                var solido = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { Solidos.Poligono(triangulo) }, (P(c.P1) - a).Normalize(), largo,
                    new SolidOptions(mat.Soldadura, ElementId.InvalidElementId));
                piezas.Add((solido, c.Rotulo));
            }
            catch (Exception ex)
            {
                r.Avisos.Add($"{donde}: No salio un cordon «{c.Rotulo}»: {ex.Message}");
            }
        }

        foreach (var t in m.Tornillos)
        {
            try
            {
                var desde = P(t.Desde);
                var eje = D(t.Eje);
                var largo = Unidades.AInternas(t.Largo);
                var d = Unidades.AInternas(t.Diametro);
                var (u, _) = Solidos.Perpendiculares(eje);
                piezas.Add((Solidos.Cilindro(desde, eje, largo, d / 2, mat.Anclas), t.Rotulo));
                piezas.Add((Solidos.Tuerca(desde + (eje * (0.6 * d)), eje.Negate(), u, 1.6 * d, 0.6 * d, 0, mat.Anclas), "cabeza de " + t.Rotulo));
                piezas.Add((Solidos.Tuerca(desde + (eje * (largo - (0.8 * d))), eje, u, 1.6 * d, 0.8 * d, 0, mat.Anclas), "tuerca de " + t.Rotulo));
            }
            catch (Exception ex)
            {
                r.Avisos.Add($"{donde}: No salio un tornillo: {ex.Message}");
            }
        }

        if (piezas.Count == 0)
        {
            r.Errores.Add($"{donde}: No salio ninguna pieza de la conexion «{m.Hoja}».");
            return;
        }

        if (Solidos.Borrar(doc, llave) > 0)
        {
            r.Reemplazadas++;
        }

        foreach (var (solido, que) in piezas)
        {
            Solidos.Forma(doc, categoria, new List<GeometryObject> { solido }, llave,
                $"CadLink: {que} (hoja «{m.Hoja.Trim()}»)", m.Id);
        }

        r.Hechas++;
    }
}
