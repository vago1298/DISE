namespace CadLink.Etabs;

/// <summary>
/// Lee las etiquetas de <b>pier</b> de los muros y sus propiedades por nivel.
/// </summary>
/// <remarks>
/// <para>
/// Va en su propio archivo y no dentro de <see cref="EtabsReader"/> porque es una
/// lectura <b>independiente</b>: se pide a mano con su botón, no forma parte de leer
/// el modelo, y tarda su tiempo en un edificio con muchos muros.
/// </para>
/// <para>
/// <b>Dos vías, y las dos hacen falta.</b> La buena es <c>PierLabel</c>, que da las
/// medidas por nivel de una vez. Pero <c>GetSectionProperties</c> cambió de firma
/// entre versiones de ETABS, así que si falla se cae a reconstruir los piers
/// recorriendo los paños de muro y preguntándole a cada uno a qué pier pertenece. Esa
/// segunda vía no da medidas, pero sí las etiquetas y cuántos paños tiene cada una,
/// que es lo mínimo para rotular un plano.
/// </para>
/// </remarks>
public static class EtabsPiers
{
    public static PiersLeidos Leer(EtabsConnection cx)
    {
        var r = new PiersLeidos();

        Com.Bitacora.Clear();

        var pierLabel = Com.TryGet(cx.SapModel, "PierLabel");

        if (pierLabel is null)
        {
            r.Avisos.Add(
                "Esta versión de ETABS no expone el objeto PierLabel. Se reconstruyen " +
                "los piers desde los paños de muro.");
        }
        else
        {
            LeerEtiquetas(pierLabel, r);
            LeerPropiedades(pierLabel, r);
        }

        // Los paños se recorren SIEMPRE: completan el conteo de áreas y, si la vía
        // de PierLabel no funcionó, son la única fuente de etiquetas.
        DesdeLosPanos(cx, r);

        if (r.Etiquetas.Count == 0)
        {
            r.Avisos.Add(
                "No se encontró ningún pier. En ETABS los piers se asignan a los muros " +
                "con Assign > Shell > Pier Label; si el modelo no los tiene asignados, " +
                "aquí no hay nada que leer.");
        }

        r.Avisos.Add("--- Detalle por miembro ---");

        foreach (var linea in Com.Bitacora)
        {
            r.Avisos.Add(linea);
        }

        return r;
    }

    private static void LeerEtiquetas(object pierLabel, PiersLeidos r)
    {
        try
        {
            object?[] a = { 0, null };
            Com.Call(pierLabel, "GetNameList", a, 0, 1);

            foreach (var n in Com.AsStrings(a[1]))
            {
                if (!string.IsNullOrWhiteSpace(n) && !r.Etiquetas.Contains(n))
                {
                    r.Etiquetas.Add(n);
                }
            }
        }
        catch (Exception)
        {
            r.Avisos.Add("No se pudo leer la lista de piers con PierLabel.GetNameList.");
        }
    }

    /// <summary>
    /// Medidas de cada pier por nivel, con <c>GetSectionProperties</c>.
    /// </summary>
    /// <remarks>
    /// Los parámetros salen todos por referencia y son arreglos paralelos, uno por
    /// nivel. Se toleran los nulos porque no todas las versiones devuelven todos los
    /// arreglos: falta el material en unas y los conteos en otras, y perder una
    /// columna de la tabla no debe costar la tabla entera.
    /// </remarks>
    private static void LeerPropiedades(object pierLabel, PiersLeidos r)
    {
        foreach (var nombre in r.Etiquetas.ToList())
        {
            try
            {
                // El arreglo se arma con el TAMAÑO QUE DIGA LA FIRMA. Escribir once a
                // mano era lo que rompía la lectura: GetSectionProperties declara
                // diecisiete parámetros, y en otras versiones puede declarar otros
                // tantos. Solo el nombre es de entrada.
                var a = Com.CallConFirma(pierLabel, "GetSectionProperties", (0, nombre));

                if (a is null || a.Length < 11)
                {
                    r.Avisos.Add(
                        $"Pier '{nombre}': GetSectionProperties no respondió. " +
                        "La etiqueta sí quedó leída.");
                    continue;
                }

                var niveles = Com.AsStrings(a[2]);
                var angulo = Com.AsDoubles(a[3]);
                var nAreas = Com.AsDoubles(a[4]);
                var nLineas = Com.AsDoubles(a[5]);
                var anchoBot = Com.AsDoubles(a[6]);
                var espBot = Com.AsDoubles(a[7]);
                var anchoTop = Com.AsDoubles(a[8]);
                var espTop = Com.AsDoubles(a[9]);
                var material = Com.AsStrings(a[10]);

                for (var i = 0; i < niveles.Length; i++)
                {
                    r.Piers.Add(new PierEtabs
                    {
                        Nombre = nombre,
                        Story = niveles[i],
                        AnguloEje = En(angulo, i),
                        Areas = (int)En(nAreas, i),
                        Lineas = (int)En(nLineas, i),
                        LargoBaseM = En(anchoBot, i),
                        EspesorBaseM = En(espBot, i),
                        LargoSupM = En(anchoTop, i),
                        EspesorSupM = En(espTop, i),
                        Material = i < material.Length ? material[i] : string.Empty
                    });
                }
            }
            catch (Exception)
            {
                r.Avisos.Add(
                    $"No se pudieron leer las medidas del pier '{nombre}'. " +
                    "Puede que esta versión de ETABS declare GetSectionProperties de " +
                    "otra forma; las etiquetas sí quedaron leídas.");
            }
        }

        static double En(double[] v, int i) => i < v.Length ? v[i] : 0;
    }

    /// <summary>
    /// Reconstruye los piers preguntando a cada paño de muro a qué pier pertenece.
    /// </summary>
    /// <remarks>
    /// Es el respaldo, y además el que completa el conteo de paños. Se hace con
    /// <c>AreaObj.GetPier</c>, que existe en todas las versiones desde 2016.
    /// </remarks>
    private static void DesdeLosPanos(EtabsConnection cx, PiersLeidos r)
    {
        var areaObj = Com.TryGet(cx.SapModel, "AreaObj");

        if (areaObj is null)
        {
            r.Avisos.Add("No se pudo acceder a los paños de área para contar los piers.");
            return;
        }

        string[] nombres;

        try
        {
            object?[] a = { 0, null };
            Com.Call(areaObj, "GetNameList", a, 0, 1);
            nombres = Com.AsStrings(a[1]);
        }
        catch (Exception)
        {
            r.Avisos.Add("No se pudo listar los paños de área.");
            return;
        }

        var cuenta = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var n in nombres)
        {
            try
            {
                object?[] a = { n, string.Empty };
                Com.Call(areaObj, "GetPier", a, 1);

                var pier = a[1]?.ToString();

                if (string.IsNullOrWhiteSpace(pier))
                {
                    continue;
                }

                cuenta[pier] = cuenta.TryGetValue(pier, out var c) ? c + 1 : 1;

                if (!r.Etiquetas.Contains(pier))
                {
                    r.Etiquetas.Add(pier);
                }
            }
            catch (Exception)
            {
                // Un paño sin pier asignado hace fallar la llamada en algunas
                // versiones en lugar de devolver vacío. No es un problema.
            }
        }

        // Si no hubo medidas, al menos queda un renglón por etiqueta con su conteo.
        foreach (var (pier, c) in cuenta)
        {
            if (r.Piers.Any(p => string.Equals(p.Nombre, pier, StringComparison.Ordinal)))
            {
                continue;
            }

            r.Piers.Add(new PierEtabs
            {
                Nombre = pier,
                Story = "(todos)",
                Areas = c
            });
        }

        r.Etiquetas.Sort(StringComparer.Ordinal);
    }
}
