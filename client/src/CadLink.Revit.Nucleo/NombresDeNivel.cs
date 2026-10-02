using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>
/// Traduce los nombres de planta del modelo de calculo a nombres de nivel de Revit.
/// </summary>
/// <remarks>
/// <para>
/// ETABS llama a sus plantas <c>Story1</c>, <c>Story2</c>, <c>Base</c>. Eso sirve para calcular
/// y no sirve para un plano: en Revit el nombre del nivel se ve en cada vista, en cada corte y
/// en cada cajetin, y «Story1» no le dice nada a nadie.
/// </para>
/// <para>
/// Lo que se genera es el nombre y la cota juntos, que es como se rotula un nivel en obra:
/// </para>
/// <list type="table">
///   <listheader><term>Cota</term><description>Nombre</description></listheader>
///   <item><term>0</term><description><c>Planta baja +0.00</c></description></item>
///   <item><term>2.89</term><description><c>Nvl-01 + 2.89</c></description></item>
///   <item><term>5.78</term><description><c>Nvl-02 + 5.78</c></description></item>
///   <item><term>bajo cero</term><description><c>Cimentacion</c></description></item>
/// </list>
/// <para>
/// La numeracion es por ORDEN DE COTA, no por el numero que traiga el nombre de ETABS: un
/// modelo donde las plantas se llaman <c>Story1, Story2</c> pero <c>Story1</c> esta arriba
/// -pasa, porque ETABS las lista de arriba abajo- daria los numeros al reves.
/// </para>
/// <para>
/// El nombre se pone en el modelo ANTES de planificar, no al crear el nivel en Revit. Si se
/// renombrara solo al crearlo, el emparejador de niveles y cada pieza -que se refieren a su
/// planta por el nombre- seguirian hablando de «Story1» y no encontrarian el nivel.
/// </para>
/// </remarks>
public static class NombresDeNivel
{
    /// <summary>Por debajo de esta cota, en valor absoluto, la planta es la baja.</summary>
    public const double ToleranciaCeroM = 0.005;

    public const string PlantaBaja = "Planta baja";

    public const string Cimentacion = "Cimentacion";

    /// <summary>
    /// El mapa de nombre del modelo a nombre de Revit, para los niveles que se le pasen.
    /// </summary>
    public static Dictionary<string, string> Traducir(IEnumerable<NivelJson>? niveles)
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var lista = (niveles ?? Enumerable.Empty<NivelJson>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Nombre))
            .ToList();

        if (lista.Count == 0)
        {
            return mapa;
        }

        // Por cota de abajo arriba. Es lo que decide la numeracion, y hace que el resultado no
        // dependa del orden en que ETABS haya listado las plantas.
        var porCota = lista
            .OrderBy(n => n.ElevacionM)
            .ThenBy(n => n.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numero = 0;

        foreach (var n in porCota)
        {
            string nombre;

            if (n.ElevacionM < -ToleranciaCeroM)
            {
                // Bajo cero es cimentacion. Si hay mas de una, la cota las distingue: dos
                // niveles con el mismo nombre no se pueden crear en Revit.
                nombre = usados.Contains(Cimentacion)
                    ? Cimentacion + " " + Cota(n.ElevacionM)
                    : Cimentacion;
            }
            else if (n.ElevacionM <= ToleranciaCeroM)
            {
                nombre = PlantaBaja + " " + Cota(0).Replace(" ", string.Empty);
            }
            else
            {
                numero++;
                nombre = "Nvl-" + numero.ToString("00", CultureInfo.InvariantCulture)
                         + " " + Cota(n.ElevacionM);
            }

            // Ultima red: si por lo que sea el nombre ya estaba, se le pega un sufijo. Un
            // nombre repetido hace que Revit rechace el nivel, y perder un nivel significa
            // perder todas las piezas que cuelgan de el.
            var final = nombre;
            var i = 2;

            while (!usados.Add(final))
            {
                final = nombre + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                i++;
            }

            mapa[n.Nombre.Trim()] = final;
        }

        return mapa;
    }

    /// <summary>La cota como se rotula: <c>+ 2.89</c>, <c>- 0.60</c>, <c>+ 0.00</c>.</summary>
    public static string Cota(double metros) =>
        metros.ToString("+ 0.00;- 0.00;+ 0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renombra los niveles del modelo y, con ellos, la planta a la que apunta cada pieza.
    /// </summary>
    /// <remarks>
    /// Las dos cosas van juntas a proposito. Renombrar los niveles y dejar que las piezas
    /// sigan citando el nombre viejo es la forma de que no se modele nada: la pieza pide una
    /// planta que ya no existe.
    /// </remarks>
    /// <returns>El mapa que se aplico, para poder decirlo en el informe.</returns>
    public static Dictionary<string, string> Aplicar(ModeloJson? modelo)
    {
        if (modelo is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var mapa = Traducir(modelo.Niveles);

        if (mapa.Count == 0)
        {
            return mapa;
        }

        string Nuevo(string? viejo) =>
            viejo is not null && mapa.TryGetValue(viejo.Trim(), out var n) ? n : viejo ?? string.Empty;

        foreach (var n in modelo.Niveles)
        {
            n.Nombre = Nuevo(n.Nombre);
        }

        foreach (var b in modelo.Barras)
        {
            b.Nivel = Nuevo(b.Nivel);
        }

        foreach (var p in modelo.Panos)
        {
            p.Nivel = Nuevo(p.Nivel);
        }

        return mapa;
    }
}
