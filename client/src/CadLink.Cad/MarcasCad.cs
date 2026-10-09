namespace CadLink.Cad;

/// <summary>
/// Marca con <b>XData</b> todo lo que se dibuja para un elemento, para poder encontrarlo y
/// borrarlo después sin tocar nada más.
/// </summary>
/// <remarks>
/// <para>
/// <b>Para qué existe.</b> Se pidió poder redibujar <b>un solo elemento</b> —porque le
/// cambiaron las medidas o el armado— sin volver a dibujar todos. La geometría de una
/// sección y de un alzado vive en un bloque y ese sí se encuentra por nombre, pero
/// <b>las cotas y los rótulos van sueltos</b> en el espacio modelo, a propósito —ver
/// <c>SeccionDrawer.Bloquear</c>—. Sin una marca, al rehacer la pieza las cotas viejas se
/// quedarían en el plano midiendo lo que ya no es.
/// </para>
/// <para>
/// Cada entidad lleva bajo la aplicación <see cref="NombreApp"/>:
/// </para>
/// <list type="bullet">
///   <item><c>1000</c> la clave del elemento: <c>SEC|ID</c> o <c>ALZ|ID</c>.</item>
///   <item><c>1040</c> y <c>1040</c>: la X y la Y donde se colocó, para volver a ese sitio.</item>
/// </list>
/// <para>
/// La XData viaja con la entidad al copiarla, moverla y guardar el DWG, y no se ve ni se
/// imprime.
/// </para>
/// </remarks>
public static class MarcasCad
{
    /// <summary>Nombre de la aplicación registrada para la XData.</summary>
    public const string NombreApp = "CADLINK";

    /// <summary>Clave de las piezas de la fila de secciones.</summary>
    public static string ClaveSeccion(string? id) => "SEC|" + NormalizarId(id);

    /// <summary>Clave de todo lo de un alzado, su corte insertado incluido.</summary>
    public static string ClaveAlzado(string? id) => "ALZ|" + NormalizarId(id);

    private static string NormalizarId(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Una entidad marcada, con el sitio que se guardó en ella.</summary>
    public sealed record Marcada(object Entidad, double X, double Y);

    /// <summary>¿Es una inserción de bloque?</summary>
    public static bool EsBloque(object entidad)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                string clase = ((dynamic)entidad).ObjectName;
                return clase.Contains("BlockReference", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Registra la aplicación. Sin esto AutoCAD rechaza el <c>SetXData</c>.
    /// </summary>
    public static void Registrar(dynamic doc)
    {
        try
        {
            AcadConnection.Retry(() => { doc.RegisteredApplications.Add(NombreApp); });
        }
        catch (Exception)
        {
            // Ya estaba registrada: es lo normal a partir del segundo dibujo.
        }
    }

    /// <summary>
    /// Marca las entidades del espacio modelo desde <paramref name="desde"/> hasta el final.
    /// </summary>
    /// <remarks>
    /// Funciona porque AutoCAD añade siempre al <b>final</b> del espacio modelo: lo que se
    /// dibujó para un elemento es el tramo que va del conteo de antes al de después.
    /// </remarks>
    /// <returns>Cuántas se marcaron.</returns>
    public static int Marcar(dynamic ms, int desde, string clave, double x, double y)
    {
        var marcadas = 0;
        var total = AcadConnection.Retry(() => (int)ms.Count);

        var tipos = new short[] { 1001, 1000, 1040, 1040 };
        var valores = new object[] { NombreApp, clave, x, y };

        for (var i = Math.Max(0, desde); i < total; i++)
        {
            var indice = i;

            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic ent = ms.Item(indice);
                    ent.SetXData(tipos, valores);
                });

                marcadas++;
            }
            catch (Exception)
            {
                // Una entidad sin marca solo significa que no se borrará sola la próxima vez.
            }
        }

        return marcadas;
    }

    /// <summary>Todas las entidades del espacio modelo marcadas con <paramref name="clave"/>.</summary>
    /// <remarks>
    /// Se buscan con un conjunto de selección <b>filtrado por la aplicación</b>, que AutoCAD
    /// resuelve por dentro: recorrer el modelo entero desde fuera, entidad por entidad,
    /// tarda segundos en un plano grande. Si el filtro falla se recorre a mano.
    /// </remarks>
    public static List<Marcada> Buscar(dynamic doc, dynamic ms, string clave)
    {
        var res = new List<Marcada>();

        // Con el tipo escrito: con doc y ms dynamic, 'var' haría dynamic todo el bucle.
        List<object> candidatas = Candidatas(doc, ms);

        foreach (var ent in candidatas)
        {
            var leida = Leer(ent);
            if (leida is not null && string.Equals(leida.Value.Clave, clave, StringComparison.Ordinal))
            {
                res.Add(new Marcada(ent, leida.Value.X, leida.Value.Y));
            }
        }

        return res;
    }

    /// <summary>Borra las entidades. Devuelve cuántas se borraron.</summary>
    public static int Borrar(IEnumerable<Marcada> marcadas)
    {
        var n = 0;

        foreach (var m in marcadas)
        {
            try
            {
                AcadConnection.Retry(() => { ((dynamic)m.Entidad).Delete(); });
                n++;
            }
            catch (Exception)
            {
                // Ya borrada —p. ej. la inserción de la sección, que borra su dibujante— o
                // bloqueada en una capa: se sigue con las demás.
            }
        }

        return n;
    }

    private static List<object> Candidatas(dynamic doc, dynamic ms)
    {
        var lista = new List<object>();
        dynamic? ss = null;

        try
        {
            AcadConnection.Retry(() =>
            {
                lista.Clear();

                var nombre = "CADLINK_" + Guid.NewGuid().ToString("N")[..8];
                ss = doc.SelectionSets.Add(nombre);

                // acSelectionSetAll = 5, filtrado por el nombre de la aplicación (1001).
                ss.Select(5, Type.Missing, Type.Missing, new short[] { 1001 }, new object[] { NombreApp });

                var n = (int)ss.Count;
                for (var i = 0; i < n; i++)
                {
                    lista.Add((object)ss.Item(i));
                }
            });

            return lista;
        }
        catch (Exception)
        {
            // Sin conjunto de selección, se recorre el modelo. Es más lento, pero sale.
            lista.Clear();

            var total = AcadConnection.Retry(() => (int)ms.Count);
            for (var i = 0; i < total; i++)
            {
                var indice = i;
                try
                {
                    lista.Add(AcadConnection.Retry(() => (object)ms.Item(indice)));
                }
                catch (Exception)
                {
                    // Se sigue con la siguiente.
                }
            }

            return lista;
        }
        finally
        {
            try
            {
                ss?.Delete();
            }
            catch (Exception)
            {
                // Un conjunto que no se borra se va con el documento.
            }
        }
    }

    private static (string Clave, double X, double Y)? Leer(object entidad)
    {
        try
        {
            return AcadConnection.Retry<(string, double, double)?>(() =>
            {
                dynamic ent = entidad;

                // Declaradas aparte: con un receptor dynamic, el 'out' en línea no infiere tipo.
                object tipos = null!;
                object valores = null!;
                ent.GetXData(NombreApp, out tipos, out valores);

                if (valores is not object[] v || v.Length < 2)
                {
                    return null;
                }

                var clave = v[1]?.ToString() ?? string.Empty;
                var x = v.Length > 2 ? Convert.ToDouble(v[2]) : 0d;
                var y = v.Length > 3 ? Convert.ToDouble(v[3]) : 0d;

                return (clave, x, y);
            });
        }
        catch (Exception)
        {
            // Sin XData nuestra, o de un formato que no se entiende: no es de un elemento.
            return null;
        }
    }
}
