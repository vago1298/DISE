using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>
/// Una seccion distinta del modelo, con la cuenta de piezas que la usan.
/// </summary>
/// <remarks>
/// Es la unidad del cuadro de mapeo: se mapea una vez por SECCION, no por pieza. Un modelo
/// con tres mil columnas suele tener quince secciones, y esa es la diferencia entre un
/// cuadro que se puede llenar y uno que no.
/// </remarks>
public sealed class SeccionDelModelo
{
    public required string Clave { get; init; }

    public required ClasePieza Clase { get; init; }

    public required SeccionJson Seccion { get; init; }

    /// <summary>Cuantas piezas del modelo usan esta seccion.</summary>
    public int Cuantas { get; set; }

    /// <summary>En que niveles aparece, en orden.</summary>
    public List<string> Niveles { get; } = new();

    public CategoriaRevit Categoria => Categorias.De(Clase);

    /// <summary>
    /// Lo que se lee en la primera columna del cuadro: <c>CC 15X25 (trabe)</c>.
    /// </summary>
    /// <remarks>
    /// Lleva la clase entre parentesis porque la MISMA propiedad de ETABS se usa a veces
    /// como columna y a veces como trabe, y entonces no van a la misma categoria de Revit
    /// ni al mismo tipo. Sin la clase a la vista, las dos filas se verian identicas y
    /// pareceria un error del programa.
    /// </remarks>
    public string Etiqueta =>
        (string.IsNullOrWhiteSpace(Seccion.Nombre) ? "(sin nombre)" : Seccion.Nombre.Trim())
        + " (" + Categorias.Nombre(Clase) + ")";

    /// <summary>Las medidas en centimetros, para ensenarlas junto a la seccion.</summary>
    public string Medidas
    {
        get
        {
            if (Clase is ClasePieza.Muro or ClasePieza.Losa)
            {
                return "e = " + Cm(Seccion.EspesorM) + " cm";
            }

            if (Seccion.Forma == FormaSeccion.Circulo || Seccion.Forma == FormaSeccion.Tubo)
            {
                return "D = " + Cm(Seccion.AnchoM) + " cm";
            }

            return Cm(Seccion.AnchoM) + " x " + Cm(Seccion.PeralteM) + " cm";
        }
    }

    private static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>Saca del modelo la lista de secciones distintas.</summary>
public static class Inventario
{
    /// <summary>
    /// Con que se consideran la misma seccion dos piezas.
    /// </summary>
    /// <remarks>
    /// Lleva la CLASE y las MEDIDAS, no solo el nombre:
    /// <list type="bullet">
    ///   <item>la clase, porque la misma propiedad usada como columna y como trabe va a dos
    ///   categorias distintas de Revit y necesita dos filas;</item>
    ///   <item>las medidas, porque en un modelo heredado es normal encontrar dos
    ///   propiedades con el mismo nombre y distinto peralte, y fundirlas dejaria la mitad de
    ///   las piezas con la seccion de la otra.</item>
    /// </list>
    /// </remarks>
    public static string Clave(ClasePieza clase, SeccionJson s) =>
        string.Join("|",
            clase.ToString(),
            (s.Nombre ?? string.Empty).Trim(),
            s.Forma.ToString(),
            R(s.AnchoM), R(s.PeralteM), R(s.PatinM), R(s.AlmaM), R(s.ParedM), R(s.EspesorM));

    private static string R(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    /// <summary>Las secciones distintas, ordenadas como se van a ensenar.</summary>
    public static List<SeccionDelModelo> De(ModeloJson modelo)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        var mapa = new Dictionary<string, SeccionDelModelo>(StringComparer.Ordinal);

        void Sumar(ClasePieza clase, SeccionJson s, string nivel)
        {
            var clave = Clave(clase, s);

            if (!mapa.TryGetValue(clave, out var fila))
            {
                fila = new SeccionDelModelo
                {
                    Clave = clave,
                    Clase = clase,
                    Seccion = s
                };

                mapa[clave] = fila;
            }

            fila.Cuantas++;

            var n = (nivel ?? string.Empty).Trim();

            if (n.Length > 0 && !fila.Niveles.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                fila.Niveles.Add(n);
            }
        }

        foreach (var b in modelo.Barras)
        {
            Sumar(b.Clase, b.Seccion, b.Nivel);
        }

        foreach (var p in modelo.Panos)
        {
            Sumar(p.Clase, p.Seccion, p.Nivel);
        }

        // Se ordena por categoria y luego por nombre. Agrupar por categoria importa porque
        // el desplegable de familias cambia con ella: llenar el cuadro es mucho mas rapido
        // si todas las columnas van seguidas.
        return mapa.Values
            .OrderBy(f => f.Categoria)
            .ThenBy(f => f.Seccion.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.Clave, StringComparer.Ordinal)
            .ToList();
    }
}
