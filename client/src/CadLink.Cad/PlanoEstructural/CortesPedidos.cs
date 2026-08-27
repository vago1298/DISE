using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// Qué cortes se piden: por su <b>eje</b>, o por el <b>valor</b> de X o de Y donde uno quiera.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió poder cortar <b>donde sea</b>, aunque la cuadrícula de ETABS o de SAP2000 no tenga un
/// eje ahí, y poder pedir <b>varios de golpe</b>. Las dos cosas son la misma pregunta: de un texto
/// —«A, C, X=4.25»— salir con la lista de cortes que hay que dibujar.
/// </para>
/// <para>
/// Y hay una regla que evita el error más fácil de cometer: si el valor que se escribe <b>cae sobre
/// un eje</b> que existe, el corte se queda con el <b>nombre de ese eje</b> en lugar de con uno
/// inventado. Así, quien escribe «X=4.25» sin saber que ahí está el eje C obtiene el corte por C
/// —rotulado C, comparable con la planta— y no dos cortes iguales con dos nombres distintos.
/// </para>
/// <para>
/// Lo que no se reconoce <b>se devuelve aparte</b>, no se traga en silencio: un eje mal escrito
/// tiene que poder decirse, porque desde fuera «no salió el corte» es indistinguible de «el corte
/// falló».
/// </para>
/// </remarks>
public static class CortesPedidos
{
    /// <summary>Un corte pedido, ya resuelto a una ordenada.</summary>
    /// <param name="Id">Cómo se rotula: el nombre del eje, o el que se le propone.</param>
    /// <param name="EnX">
    /// <c>true</c> = el plano del corte está en <b>X = Ordenada</b> —el corte recorre la Y—;
    /// <c>false</c> = en <b>Y = Ordenada</b>.
    /// </param>
    /// <param name="Ordenada">Dónde, en metros.</param>
    /// <param name="Propuesto">
    /// <c>true</c> = no había eje ahí y el nombre lo pone la app. Es lo que permite avisar de que
    /// ese corte no es de la cuadrícula.
    /// </param>
    public sealed record Peticion(string Id, bool EnX, double Ordenada, bool Propuesto);

    /// <summary>Los cortes que se piden y los textos que no se entendieron.</summary>
    public sealed record Resultado(List<Peticion> Cortes, List<string> NoReconocidos);

    /// <summary>
    /// Interpreta lo que se pidió y devuelve los cortes, sin repetidos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los tres textos se leen igual y se admiten separados por <b>comas, punto y coma o
    /// espacios</b>, que es como cualquiera escribe una lista. En el de los ejes van nombres
    /// —<c>A</c>, <c>3</c>— y también valores con su dirección —<c>X=4.25</c>—, para que quien
    /// quiera se apañe con un solo campo.
    /// </para>
    /// <para>
    /// El <b>orden</b> del resultado es el de lo pedido, y los ejes van primero: si el mismo corte
    /// llega por su nombre y por su valor, se queda el del nombre.
    /// </para>
    /// </remarks>
    /// <param name="ejes">Nombres de eje, y opcionalmente valores con dirección.</param>
    /// <param name="valoresX">Valores de X, para cortes que recorren la Y.</param>
    /// <param name="valoresY">Valores de Y.</param>
    /// <param name="ejesX">La cuadrícula en X: los ejes de las letras.</param>
    /// <param name="ejesY">La cuadrícula en Y: los de los números.</param>
    /// <param name="tolM">
    /// Cuándo un valor «cae sobre» un eje. Cinco centímetros: por debajo de eso, en un plano de
    /// obra, es el mismo sitio.
    /// </param>
    public static Resultado Interpretar(
        string? ejes, string? valoresX, string? valoresY,
        IReadOnlyList<(string Id, double Ordenada)>? ejesX,
        IReadOnlyList<(string Id, double Ordenada)>? ejesY,
        double tolM = 0.05)
    {
        var cortes = new List<Peticion>();
        var malos = new List<string>();

        var enX = ejesX ?? new List<(string, double)>();
        var enY = ejesY ?? new List<(string, double)>();

        // 1) El campo de los ejes: nombres, y valores con dirección para quien los escriba ahí.
        foreach (var t in Trozos(ejes))
        {
            var conDireccion = ConDireccion(t);

            if (conDireccion is { } cd)
            {
                Agregar(cortes, PorValor(cd.EnX, cd.Valor, cd.EnX ? enX : enY, tolM), tolM);
                continue;
            }

            var deEje = PorNombre(t, enX, enY);

            if (deEje is { } p)
            {
                Agregar(cortes, p, tolM);
                continue;
            }

            malos.Add(t);
        }

        // 2) Y los dos campos de valores. Se admite que traigan su dirección escrita —«X=4.25» en
        //    el campo de las X— porque es lo que uno teclea sin pensar, y sería absurdo
        //    rechazarlo.
        foreach (var (texto, esX) in new[] { (valoresX, true), (valoresY, false) })
        {
            foreach (var t in Trozos(texto))
            {
                var conDireccion = ConDireccion(t);

                var valor = conDireccion is { } cd2
                    ? cd2.Valor
                    : Numero(t);

                // Y SI NO ES UN NÚMERO, SE PRUEBA COMO NOMBRE DE EJE de esa dirección: quien
                // escriba «C» en el campo de las X quiere el corte por el eje C, y decírselo con
                // un aviso en lugar de dibujarlo sería quedarse corto por nada.
                if (valor is null)
                {
                    var deEsaDireccion = esX ? enX : enY;

                    var eje = deEsaDireccion.FirstOrDefault(
                        x => string.Equals(x.Id.Trim(), t, StringComparison.OrdinalIgnoreCase));

                    if (eje.Id is not null && eje.Id.Trim().Length > 0)
                    {
                        Agregar(cortes, new Peticion(eje.Id.Trim(), esX, eje.Ordenada, false), tolM);
                        continue;
                    }

                    malos.Add(t);
                    continue;
                }

                var direccion = conDireccion?.EnX ?? esX;

                Agregar(
                    cortes,
                    PorValor(direccion, valor.Value, direccion ? enX : enY, tolM),
                    tolM);
            }
        }

        return new Resultado(cortes, malos);
    }

    /// <summary>
    /// El nombre que se le propone a un corte que <b>no está en la cuadrícula</b>.
    /// </summary>
    /// <remarks>
    /// <c>X=4.25</c>: dice la dirección y el sitio, y con eso el corte se puede volver a pedir
    /// tal cual. Con <b>punto</b> decimal y hasta dos cifras, porque este texto se rotula en el
    /// plano y acaba en el nombre de un bloque: con la coma de la configuración regional, el mismo
    /// corte se llamaría distinto en dos máquinas.
    /// </remarks>
    public static string NombrePropuesto(bool enX, double ordenada) =>
        (enX ? "X=" : "Y=") + ordenada.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Un corte por un valor, con el nombre del eje si cae sobre uno.</summary>
    private static Peticion PorValor(
        bool enX, double valor, IReadOnlyList<(string Id, double Ordenada)> deEsaDireccion,
        double tolM)
    {
        // SI CAE SOBRE UN EJE, ES ESE EJE: se toma el más cercano, no el primero que pase la
        // tolerancia, porque con dos ejes juntos el primero puede no ser el de al lado.
        var mejor = string.Empty;
        var mejorD = double.MaxValue;

        foreach (var (id, ordenada) in deEsaDireccion)
        {
            var d = Math.Abs(ordenada - valor);

            if (d <= tolM && d < mejorD)
            {
                mejorD = d;
                mejor = id;
            }
        }

        return mejor.Length > 0
            ? new Peticion(mejor, enX, valor, false)
            : new Peticion(NombrePropuesto(enX, valor), enX, valor, true);
    }

    /// <summary>Un corte por el <b>nombre</b> de un eje, en X primero y en Y después.</summary>
    private static Peticion? PorNombre(
        string nombre,
        IReadOnlyList<(string Id, double Ordenada)> enX,
        IReadOnlyList<(string Id, double Ordenada)> enY)
    {
        foreach (var (lista, esX) in new[] { (enX, true), (enY, false) })
        {
            foreach (var (id, ordenada) in lista)
            {
                if (string.Equals(id.Trim(), nombre, StringComparison.OrdinalIgnoreCase))
                {
                    return new Peticion(id.Trim(), esX, ordenada, false);
                }
            }
        }

        return null;
    }

    /// <summary>Añade un corte si no está ya pedido, en el mismo sitio y la misma dirección.</summary>
    private static void Agregar(List<Peticion> cortes, Peticion nuevo, double tolM)
    {
        if (cortes.Any(
                c => c.EnX == nuevo.EnX && Math.Abs(c.Ordenada - nuevo.Ordenada) <= tolM))
        {
            return;
        }

        cortes.Add(nuevo);
    }

    /// <summary>
    /// Parte un texto por <b>punto y coma, espacios y comas</b>, respetando la coma decimal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La coma hace dos papeles en español y hay que distinguirlos: en «A, C» separa la lista y en
    /// «X=4,25» es el punto decimal, que es lo que sale del teclado numérico. Así que la coma
    /// separa <b>salvo cuando va entre dos cifras</b>.
    /// </para>
    /// <para>
    /// Queda un caso ambiguo de verdad: <c>3,4</c> puede ser «los ejes 3 y 4» o «tres coma
    /// cuatro». Se lee como el número, y entonces no coincide con ningún eje, así que <b>sale en
    /// los no reconocidos</b> y se avisa: es mejor decirlo que adivinar y dibujar un corte donde
    /// nadie lo pidió. Con «3, 4» o «3;4» funciona.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Trozos(string? texto) =>
        Regex.Split(texto ?? string.Empty, @"[;\s]+|(?<![0-9]),|,(?![0-9])")
             .Select(x => x.Trim())
             .Where(x => x.Length > 0);

    /// <summary>Un trozo del tipo <c>X=4.25</c> o <c>Y:2.1</c>, si lo es.</summary>
    private static (bool EnX, double Valor)? ConDireccion(string t)
    {
        if (t.Length < 2)
        {
            return null;
        }

        var letra = char.ToUpperInvariant(t[0]);

        if (letra != 'X' && letra != 'Y')
        {
            return null;
        }

        var resto = t[1..].TrimStart('=', ':', ' ');
        var valor = Numero(resto);

        return valor is null ? null : (letra == 'X', valor.Value);
    }

    /// <summary>
    /// Un número, con <b>punto o coma</b> decimal.
    /// </summary>
    /// <remarks>
    /// Se admiten los dos porque en un teclado en español la coma es lo que sale del teclado
    /// numérico, y rechazar «4,25» sería pedirle al usuario que adivine. Se lee siempre con la
    /// cultura invariante para que el resultado no dependa de la máquina.
    /// </remarks>
    private static double? Numero(string t)
    {
        var limpio = t.Replace(',', '.').Trim();

        return double.TryParse(
            limpio, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
