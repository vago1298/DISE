using System.Globalization;
using System.Reflection;

namespace CadLink.Etabs;

/// <summary>
/// Llamadas a COM por enlace tardío, con soporte de parámetros por referencia.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe esta clase.</b> Casi toda la CSI OAPI devuelve sus
/// resultados en parámetros <c>ByRef</c>:
/// </para>
/// <code>
/// ret = SapModel.PointObj.GetNameList(NumberNames, MyName)   ' los dos salen llenos
/// </code>
/// <para>
/// En C#, la palabra <c>dynamic</c> <b>no</b> maneja bien esos parámetros de
/// salida sobre un objeto COM. La forma que sí funciona es
/// <see cref="Type.InvokeMember(string, BindingFlags, Binder, object, object[], ParameterModifier[], System.Globalization.CultureInfo, string[])"/>
/// con un <see cref="ParameterModifier"/>: marca cuáles argumentos van por
/// referencia y, al volver, los valores quedan escritos en el arreglo.
/// </para>
/// <para>
/// Es el equivalente exacto de lo que hace VBA con <c>Object</c>, y evita tener
/// que referenciar <c>ETABSv1.dll</c>, que cambia de ruta en cada versión.
/// </para>
/// </remarks>
internal static class Com
{
    /// <summary>
    /// Invoca un método COM. Los índices de <paramref name="porReferencia"/>
    /// indican qué argumentos son de salida.
    /// </summary>
    /// <returns>El valor de retorno; en la OAPI suele ser 0 cuando todo salió bien.</returns>
    /// <summary>
    /// Qué vía funcionó y qué falló en cada miembro. Se muestra al usuario.
    /// </summary>
    /// <remarks>
    /// Sin esto, los avisos del lector eran del tipo <i>«no se pudieron leer los
    /// puntos del modelo»</i>, que no dice nada: no distingue un ETABS sin modelo de
    /// un miembro que no se encuentra o de un tipo que no se puede convertir. Ese
    /// silencio costó varias vueltas.
    /// </remarks>
    public static List<string> Bitacora { get; } = new();

    private static void Anota(string linea)
    {
        if (!Bitacora.Contains(linea))
        {
            Bitacora.Add(linea);
        }
    }

    /// <summary>
    /// Busca un miembro en las <b>interfaces del ensamblado</b> de ETABS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es la pieza que faltaba.</b> Todo lo que ETABS entrega —el
    /// <c>SapModel</c>, y dentro de él <c>PointObj</c>, <c>FrameObj</c>,
    /// <c>AreaObj</c>, <c>Story</c>— llega a .NET como <c>System.__ComObject</c>, un
    /// envoltorio que <b>no declara ningún miembro</b>. Por eso
    /// <c>GetType().InvokeMember(...)</c> falla en todos ellos y el lector reportaba
    /// cero puntos, cero frames y cero áreas con el modelo abierto y cargado.
    /// </para>
    /// <para>
    /// La solución es pedir el miembro a la interfaz que lo declara, sacada de
    /// <c>ETABSv1.dll</c>. El runtime hace entonces el <c>QueryInterface</c> por su
    /// cuenta y la llamada entra por la vtable, que es lo mismo que consigue VBA con
    /// su referencia a la librería.
    /// </para>
    /// </remarks>
    private static IEnumerable<MethodInfo> MetodosDeInterfaz(string metodo, int cuantosArgs)
    {
        foreach (var t in EtabsAssembly.TiposQueDeclaran(metodo))
        {
            MethodInfo[] ms;
            try
            {
                ms = t.GetMethods();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var m in ms)
            {
                if (m.Name != metodo)
                {
                    continue;
                }

                // Se admite que la interfaz declare MAS parametros de los que se
                // pasan: casi toda la OAPI termina en un CSys opcional, y exigir
                // firma exacta hacia que no se encontrara ni un solo metodo.
                if (m.GetParameters().Length >= cuantosArgs)
                {
                    yield return m;
                }
            }
        }
    }

    /// <summary>
    /// Invoca un método por la interfaz del ensamblado, con IDispatch como respaldo.
    /// </summary>
    /// <remarks>
    /// La vía de la interfaz tiene además una ventaja sobre <c>IDispatch</c>:
    /// <c>MethodInfo.Invoke</c> resuelve los parámetros <c>ByRef</c> por sí solo,
    /// escribiendo los resultados en el arreglo, sin necesidad de
    /// <see cref="ParameterModifier"/>.
    /// </remarks>
    public static object? Call(
        object objetivo, string metodo, object?[] args, params int[] porReferencia)
    {
        foreach (var m in MetodosDeInterfaz(metodo, args.Length))
        {
            try
            {
                var ps = m.GetParameters();

                // Se rellenan los parametros opcionales que no se pasaron.
                var todos = args;

                if (ps.Length > args.Length)
                {
                    todos = new object?[ps.Length];
                    Array.Copy(args, todos, args.Length);

                    for (var i = args.Length; i < ps.Length; i++)
                    {
                        todos[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : Type.Missing;
                    }
                }

                var r = m.Invoke(objetivo, todos);

                // Los ByRef quedaron escritos en 'todos'; hay que devolverlos.
                if (!ReferenceEquals(todos, args))
                {
                    Array.Copy(todos, args, args.Length);
                }

                Anota($"{metodo}: por la interfaz '{m.DeclaringType?.Name}'.");
                return r;
            }
            catch (Exception ex)
            {
                Anota($"{metodo} por '{m.DeclaringType?.Name}': {Detalle(ex)}");
            }
        }

        return PorIDispatch(objetivo, metodo, args, porReferencia);
    }

    /// <summary>
    /// Llama a un método construyendo el arreglo de argumentos <b>a partir de su
    /// firma real</b>, no de lo que uno supone que recibe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto es lo que hacía fallar la lectura de los piers.</b>
    /// <c>PierLabel.GetSectionProperties</c> declara <b>diecisiete</b> parámetros
    /// —hasta los centroides de arriba y de abajo— y aquí se le pasaban once. Todos
    /// son <c>ByRef</c> y ninguno opcional, así que rellenar los que faltan con
    /// <c>Type.Missing</c> no vale: la llamada revienta. De ahí el aviso repetido
    /// <i>«no se pudieron leer las medidas del pier»</i> en cada uno de los piers, con
    /// las etiquetas sí leídas.
    /// </para>
    /// <para>
    /// El número de parámetros además <b>cambia entre versiones</b> de ETABS, así que
    /// no sirve escribir diecisiete a mano: hay que preguntárselo a la firma. Se le
    /// pide el arreglo del tamaño que diga, se rellena con un valor neutro del tipo
    /// que toque, y se ponen encima solo los datos de entrada.
    /// </para>
    /// </remarks>
    /// <param name="entradas">Índice y valor de los argumentos que sí son de entrada.</param>
    /// <returns>El arreglo con los resultados escritos, o <c>null</c> si falló.</returns>
    public static object?[]? CallConFirma(
        object objetivo, string metodo, params (int Indice, object? Valor)[] entradas)
    {
        // Cualquier numero de argumentos: aqui la firma la manda el metodo.
        foreach (var m in MetodosDeInterfaz(metodo, 0))
        {
            try
            {
                var ps = m.GetParameters();
                var args = new object?[ps.Length];

                for (var i = 0; i < ps.Length; i++)
                {
                    args[i] = ValorNeutro(ps[i]);
                }

                foreach (var (indice, valor) in entradas)
                {
                    if (indice >= 0 && indice < args.Length)
                    {
                        args[indice] = valor;
                    }
                }

                m.Invoke(objetivo, args);

                Anota($"{metodo}: por '{m.DeclaringType?.Name}' con {ps.Length} argumentos.");
                return args;
            }
            catch (Exception ex)
            {
                Anota($"{metodo} por '{m.DeclaringType?.Name}' " +
                      $"({m.GetParameters().Length} args): {Detalle(ex)}");
            }
        }

        return null;
    }

    /// <summary>
    /// Llama a un método y devuelve sus resultados <b>por el nombre del parámetro</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <see cref="CallConFirma"/> con una vuelta más, y resuelve el problema de fondo de
    /// esta API: <b>las firmas cambian</b> entre versiones y entre ETABS y SAP2000, así que
    /// leer «la posición 6» es una apuesta. Aquí se pregunta a la firma real cómo se llama cada
    /// parámetro y se devuelve un diccionario: quien llama pide <c>Notes</c> o
    /// <c>Thickness</c> y no le importa en qué hueco vinieran.
    /// </para>
    /// <para>
    /// Y como <see cref="CallConFirma"/>, cada parámetro se rellena con un <b>valor neutro de
    /// su tipo</b>. Eso es lo que permite llamar a métodos con <b>enumeraciones</b> —el
    /// <c>eWallPropType</c> y el <c>eShellType</c> de <c>GetWall</c>—, que es justo donde se
    /// atascaba la lectura: pasándoles un 0 entero, la invocación revienta con un choque de
    /// tipos y la propiedad se queda sin notas.
    /// </para>
    /// <para>
    /// Solo se acepta la llamada si la OAPI devolvió <b>0</b>: preguntarle a una propiedad de
    /// losa por <c>GetWall</c> no falla, devuelve error, y con la respuesta vacía parecería que
    /// la propiedad no tiene notas.
    /// </para>
    /// </remarks>
    /// <returns>Los parámetros por su nombre, o <c>null</c> si ninguna firma respondió.</returns>
    public static Dictionary<string, object?>? CallPorNombre(
        object objetivo, string metodo, params (int Indice, object? Valor)[] entradas)
    {
        foreach (var m in MetodosDeInterfaz(metodo, 0))
        {
            try
            {
                var ps = m.GetParameters();
                var args = new object?[ps.Length];

                for (var i = 0; i < ps.Length; i++)
                {
                    args[i] = ValorNeutro(ps[i]);
                }

                foreach (var (indice, valor) in entradas)
                {
                    if (indice >= 0 && indice < args.Length)
                    {
                        args[indice] = valor;
                    }
                }

                var r = m.Invoke(objetivo, args);

                if (r is not null && Convert.ToInt32(r) != 0)
                {
                    Anota($"{metodo} por '{m.DeclaringType?.Name}': la OAPI devolvió error.");
                    continue;
                }

                var salida = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < ps.Length; i++)
                {
                    salida[ps[i].Name ?? i.ToString(CultureInfo.InvariantCulture)] = args[i];
                }

                Anota($"{metodo}: por '{m.DeclaringType?.Name}', {ps.Length} parámetros " +
                      "leídos por su nombre.");
                return salida;
            }
            catch (Exception ex)
            {
                Anota($"{metodo} por '{m.DeclaringType?.Name}' " +
                      $"({m.GetParameters().Length} args): {Detalle(ex)}");
            }
        }

        return null;
    }

    /// <summary>Valor de arranque de un parámetro, según su tipo.</summary>
    /// <remarks>
    /// Los arreglos van a <c>null</c> a propósito: la OAPI los crea ella. Los números
    /// a cero y las cadenas a vacío, porque un <c>null</c> en un <c>ref int</c> hace
    /// fallar la invocación.
    /// </remarks>
    private static object? ValorNeutro(ParameterInfo p)
    {
        if (p.HasDefaultValue)
        {
            return p.DefaultValue;
        }

        var t = p.ParameterType;

        if (t.IsByRef)
        {
            t = t.GetElementType() ?? t;
        }

        if (t.IsArray)
        {
            return null;
        }

        if (t == typeof(string))
        {
            return string.Empty;
        }

        if (t == typeof(bool))
        {
            return false;
        }

        if (t.IsValueType)
        {
            return Activator.CreateInstance(t);
        }

        return null;
    }

    private static object? PorIDispatch(
        object objetivo, string metodo, object?[] args, int[] porReferencia)
    {
        ParameterModifier[]? modificadores = null;

        if (args.Length > 0)
        {
            var m = new ParameterModifier(args.Length);
            foreach (var i in porReferencia)
            {
                if (i >= 0 && i < args.Length)
                {
                    m[i] = true;
                }
            }

            modificadores = new[] { m };
        }

        return objetivo.GetType().InvokeMember(
            metodo,
            BindingFlags.InvokeMethod,
            binder: null,
            target: objetivo,
            args: args,
            modifiers: modificadores,
            culture: null,
            namedParameters: null);
    }

    /// <summary>Igual que <see cref="Call"/> pero devolviendo el código de retorno como entero.</summary>
    public static int CallRet(
        object objetivo, string metodo, object?[] args, params int[] porReferencia)
    {
        var r = Call(objetivo, metodo, args, porReferencia);
        return r is null ? -1 : Convert.ToInt32(r);
    }

    /// <summary>Lee una propiedad COM, por ejemplo <c>SapModel</c> o <c>PointObj</c>.</summary>
    public static object Get(object objetivo, string propiedad)
    {
        return TryGet(objetivo, propiedad) ?? throw new EtabsException(
            $"ETABS devolvió vacío al pedir '{propiedad}'. " +
            "Detalle de los intentos:" + Environment.NewLine +
            string.Join(Environment.NewLine, Bitacora.TakeLast(6).Select(l => "  " + l)));
    }

    /// <summary>Como <see cref="Get"/> pero devuelve <c>null</c> en lugar de lanzar.</summary>
    /// <remarks>
    /// Primero las <b>interfaces del ensamblado</b> y solo después
    /// <c>IDispatch</c>. Ver <see cref="MetodosDeInterfaz"/>: sobre un
    /// <c>__ComObject</c>, IDispatch no llega a estos miembros, y eso era lo que
    /// dejaba el lector con cero puntos y cero frames.
    /// </remarks>
    public static object? TryGet(object objetivo, string propiedad)
    {
        foreach (var t in EtabsAssembly.TiposQueDeclaran(propiedad))
        {
            try
            {
                var p = t.GetProperty(propiedad);
                if (p is null)
                {
                    continue;
                }

                var v = p.GetValue(objetivo);
                if (v is not null)
                {
                    Anota($"{propiedad}: por la interfaz '{t.Name}'.");
                    return v;
                }
            }
            catch (Exception ex)
            {
                Anota($"{propiedad} por '{t.Name}': {Detalle(ex)}");
            }
        }

        try
        {
            var v = objetivo.GetType().InvokeMember(
                propiedad, BindingFlags.GetProperty, null, objetivo, null);

            if (v is not null)
            {
                Anota($"{propiedad}: por IDispatch.");
            }

            return v;
        }
        catch (Exception ex)
        {
            Anota($"{propiedad} por IDispatch: {Detalle(ex)}");
            return null;
        }
    }

    /// <summary>Tipo, HRESULT y texto, desenvolviendo la excepción interna.</summary>
    private static string Detalle(Exception ex)
    {
        var e = ex;
        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var s = e.GetType().Name;

        if (e is System.Runtime.InteropServices.COMException com)
        {
            s += $" 0x{(uint)com.HResult:X8}";
        }

        return s + ": " + e.Message.Replace(Environment.NewLine, " ").Trim();
    }

    /// <summary>Convierte a arreglo de cadenas lo que la API devuelva, tolerando nulos.</summary>
    public static string[] AsStrings(object? v) => v switch
    {
        string[] s => s,
        object[] o => o.Select(x => x?.ToString() ?? string.Empty).ToArray(),
        null => Array.Empty<string>(),
        _ => new[] { v.ToString() ?? string.Empty }
    };

    public static double[] AsDoubles(object? v) => v switch
    {
        double[] d => d,
        object[] o => o.Select(x => x is null ? 0d : Convert.ToDouble(x)).ToArray(),
        _ => Array.Empty<double>()
    };
}

/// <summary>Falla al hablar con ETABS.</summary>
public sealed class EtabsException : Exception
{
    public EtabsException(string mensaje) : base(mensaje) { }

    public EtabsException(string mensaje, Exception? interna) : base(mensaje, interna) { }
}
