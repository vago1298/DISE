using System.Collections.ObjectModel;
using System.Globalization;

namespace CadLink.App.Models;

/// <summary>
/// Un plano del juego, con su clave y su número.
/// </summary>
/// <remarks>
/// El <b>número</b> no se escribe: lo pone <see cref="JuegoDePlanos"/> según el orden
/// en la lista. Si se pudiera escribir, en cuanto se agrega un plano en medio hay que
/// renumerar todos a mano, y ese es el momento en que un juego sale a obra con dos
/// planos "3 de 8".
/// </remarks>
public sealed class PlanoRow : Row
{
    private string _clave = string.Empty;
    private string _contiene = string.Empty;
    private string _detalle = string.Empty;
    private string _escala = "1:50";
    private string _tamano = "ARCH D (610 x 914 mm)";
    private bool _horizontal = true;
    private int _numero;
    private int _total;

    /// <summary>Clave del plano, por ejemplo <c>E-01</c>.</summary>
    public string Clave { get => _clave; set => Set(ref _clave, value); }

    /// <summary>Qué contiene el plano. Va en la solapa.</summary>
    public string Contiene { get => _contiene; set => Set(ref _contiene, value); }

    /// <summary>
    /// La segunda línea del contenido: <b>sección y detalles</b>.
    /// </summary>
    /// <remarks>
    /// Es un atributo aparte del cajetín —<c>DETALLE</c>— y no el mismo texto de «Contiene» con una
    /// coma. Con los dos juntos, el renglón se desborda del recuadro en cuanto el plano lleva tres
    /// cosas, y en el cajetín eso no se puede arreglar sin volver a generar el plano.
    /// </remarks>
    public string Detalle { get => _detalle; set => Set(ref _detalle, value); }

    public string Escala { get => _escala; set => Set(ref _escala, value); }

    /// <summary>
    /// El tamaño de hoja, tal como lo llama el dispositivo de ploteo: <c>ARCH D</c>, <c>ISO A1</c>…
    /// </summary>
    /// <remarks>
    /// <b>Tiene que existir en el dispositivo.</b> Si no, AutoCAD no da error: deja el papel por
    /// omisión —Carta vertical— y el plano entero sale descuadrado. El generador de solapas lo
    /// comprueba y lo dice.
    /// </remarks>
    public string Tamano { get => _tamano; set => Set(ref _tamano, value); }

    /// <summary>La hoja va <b>acostada</b>.</summary>
    public bool Horizontal { get => _horizontal; set => Set(ref _horizontal, value); }

    /// <summary>Los tamaños de hoja más usados, para la celda.</summary>
    /// <remarks>
    /// Es una ayuda, no una restricción: la celda es escribible porque un dispositivo puede tener
    /// tamaños personalizados con cualquier nombre, y encerrar la lista dejaría fuera justo los que
    /// el despacho se creó a medida.
    /// </remarks>
    public string[] Tamanos => _tamanos;

    /// <remarks>
    /// <b>Con las medidas en milimetros</b>, que es lo que pidio el usuario: saber cuanto mide una
    /// ARCH D sin tener que recordarlo es la diferencia entre elegir bien y elegir por costumbre. Van
    /// lado corto por lado largo, como las nombra AutoCAD -«A4 210 x 297 mm»-.
    ///
    /// El parentesis NO estorba a la busqueda del papel: Solapas.SoloElTamano lo quita antes de
    /// comparar. Y un trabajo guardado con «ARCH D» a secas sigue funcionando igual.
    /// </remarks>
    private static readonly string[] _tamanos =
    {
        "ARCH A (229 x 305 mm)",
        "ARCH B (305 x 457 mm)",
        "ARCH C (457 x 610 mm)",
        "ARCH D (610 x 914 mm)",
        "ARCH E (914 x 1219 mm)",
        "ARCH E1 (762 x 1067 mm)",
        "ARCH E2 (660 x 965 mm)",
        "ARCH E3 (686 x 991 mm)",
        "ANSI A (216 x 279 mm)",
        "ANSI B (279 x 432 mm)",
        "ANSI C (432 x 559 mm)",
        "ANSI D (559 x 864 mm)",
        "ANSI E (864 x 1118 mm)",
        "ISO A4 (210 x 297 mm)",
        "ISO A3 (297 x 420 mm)",
        "ISO A2 (420 x 594 mm)",
        "ISO A1 (594 x 841 mm)",
        "ISO A0 (841 x 1189 mm)",
    };

    /// <summary>Número de este plano. Lo asigna el juego, no se escribe.</summary>
    public int Numero
    {
        get => _numero;
        internal set
        {
            if (_numero != value)
            {
                _numero = value;
                Raise(nameof(Numero));
                Raise(nameof(NumeroTexto));
            }
        }
    }

    /// <summary>Cuántos planos tiene el juego. Lo asigna el juego.</summary>
    public int Total
    {
        get => _total;
        internal set
        {
            if (_total != value)
            {
                _total = value;
                Raise(nameof(Total));
                Raise(nameof(NumeroTexto));
            }
        }
    }

    /// <summary>Lo que se imprime en la solapa: <c>3 de 8</c>.</summary>
    public string NumeroTexto => _total > 0 ? $"{_numero} de {_total}" : _numero.ToString();
}

/// <summary>
/// Datos de la <b>solapa</b> (el cuadro de rótulos) y el juego de planos.
/// </summary>
/// <remarks>
/// Son los campos que van en el recuadro de cada plano: calculista, propietario,
/// ubicación, obra, quién dibujó, fecha, escala, acotación, clave y número.
/// </remarks>
public sealed class Solapa : Row
{
    private string _calculista = string.Empty;
    private string _cedula = string.Empty;
    private string _propietario = string.Empty;
    private string _ubicacion = string.Empty;
    private string _obra = string.Empty;
    private string _dibujo = string.Empty;
    private DateTime _fecha = DateTime.Today;
    private string _escala = "1:50";
    private string _acotacion = "cm";

    public string Calculista { get => _calculista; set => Set(ref _calculista, value); }

    /// <summary>
    /// Cédula profesional del calculista. Va en el cajetín debajo de su nombre.
    /// </summary>
    /// <remarks>
    /// Se captura <b>solo el número</b>: el «CED. PROF.» lo pone el generador de solapas, y solo si
    /// hay número. Capturándolo aquí, una cédula en blanco dejaría un «CED. PROF.» solo en el
    /// cajetín, que se lee como un dato que se perdió.
    /// </remarks>
    public string Cedula { get => _cedula; set => Set(ref _cedula, value); }

    public string Propietario { get => _propietario; set => Set(ref _propietario, value); }

    public string Ubicacion { get => _ubicacion; set => Set(ref _ubicacion, value); }

    /// <summary>Proyecto / Obra.</summary>
    public string Obra { get => _obra; set => Set(ref _obra, value); }

    /// <summary>Quién dibujó.</summary>
    public string Dibujo { get => _dibujo; set => Set(ref _dibujo, value); }

    public DateTime Fecha
    {
        get => _fecha;
        set
        {
            Set(ref _fecha, value);

            // Los dos textos derivan de la fecha, así que hay que reavisar o el
            // rótulo de la solapa se quedaría con el mes anterior.
            Raise(nameof(FechaTexto));
            Raise(nameof(FechaTextoLargo));
        }
    }

    /// <summary>
    /// La fecha como se <b>rotula en la solapa</b>: mes y año con letra.
    /// </summary>
    /// <remarks>
    /// <para>
    /// «AGOSTO DE 2026», no «18/08/2026». Es lo que pidió el usuario y es lo normal en
    /// un juego de planos: la solapa fecha el <b>juego</b>, que se emite en un mes, no
    /// un día concreto. Poner el día invita a que alguien lo lea como fecha de
    /// revisión.
    /// </para>
    /// <para>
    /// El día se sigue capturando y se sigue guardando en el <c>.clk</c>: se puede
    /// elegir en el calendario y está disponible en <see cref="FechaTextoLargo"/> para
    /// quien lo necesite. Lo único que cambia es <b>qué se imprime por omisión</b>.
    /// </para>
    /// <para>
    /// Va en <c>es-MX</c> y no en la cultura del equipo a propósito: el plano se
    /// entrega en español pase lo que pase, y en un Windows en inglés
    /// <c>CurrentCulture</c> daría «August of 2026» en una solapa mexicana.
    /// </para>
    /// </remarks>
    public string FechaTexto => TextoDeMesYAnio(_fecha);

    /// <summary>La fecha completa con el mes en letra, por si hace falta el día.</summary>
    public string FechaTextoLargo =>
        _fecha.ToString("d 'de' MMMM 'de' yyyy", CulturaPlanos).ToUpperInvariant();

    /// <summary>Cultura de los planos. Ver <see cref="FechaTexto"/>.</summary>
    private static readonly CultureInfo CulturaPlanos = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Mes y año con letra, en mayúsculas.</summary>
    public static string TextoDeMesYAnio(DateTime f) =>
        f.ToString("MMMM 'de' yyyy", CulturaPlanos).ToUpperInvariant();

    /// <summary>Escala por omisión del juego. Cada plano puede llevar la suya.</summary>
    public string Escala { get => _escala; set => Set(ref _escala, value); }

    /// <summary>Unidad de acotación: cm, m, mm.</summary>
    public string Acotacion { get => _acotacion; set => Set(ref _acotacion, value); }
}

/// <summary>
/// El juego de planos, que se <b>renumera solo</b>.
/// </summary>
/// <remarks>
/// <para>
/// Lo que pidió el usuario: <i>«cuando se agregue un nuevo plano actualizar en
/// automático el número de planos»</i>. La numeración es una función del orden de la
/// lista, no un dato que se escriba, así que se recalcula entera cada vez que la
/// lista cambia.
/// </para>
/// <para>
/// Recalcular <b>todos</b> y no solo el nuevo es a propósito: al insertar un plano en
/// medio o al borrar uno, cambian el número de los siguientes <b>y</b> el total de
/// todos. Actualizar solo el que se tocó deja el resto diciendo «de 7» cuando ya son
/// 8, y eso solo se descubre con el juego impreso.
/// </para>
/// </remarks>
public sealed class JuegoDePlanos
{
    public Solapa Solapa { get; } = new();

    public ObservableCollection<PlanoRow> Planos { get; } = new();

    public JuegoDePlanos()
    {
        Planos.CollectionChanged += (_, _) => Renumerar();
    }

    /// <summary>Vuelve a poner el número y el total de todos los planos.</summary>
    public void Renumerar()
    {
        var total = Planos.Count;

        for (var i = 0; i < total; i++)
        {
            Planos[i].Numero = i + 1;
            Planos[i].Total = total;
        }
    }

    /// <summary>
    /// Agrega un plano, con su clave puesta si no se indica.
    /// </summary>
    /// <remarks>
    /// La clave por omisión sigue el número: <c>E-01</c>, <c>E-02</c>… Se calcula a
    /// partir de <b>cuántos hay</b> y no de la última clave escrita, para que
    /// renombrar un plano a mano no rompa la serie de los siguientes.
    /// </remarks>
    public PlanoRow Agregar(string? contiene = null, string? clave = null)
    {
        // EL TAMAÑO Y LA ORIENTACIÓN SE HEREDAN del último plano, no se ponen por omisión. Un juego
        // se dibuja casi siempre en la misma hoja, así que arrancar cada plano en ARCH D horizontal
        // obliga a corregir a mano el que se acaba de agregar, y ese es el que se olvida.
        var ultimo = Planos.Count > 0 ? Planos[Planos.Count - 1] : null;

        var p = new PlanoRow
        {
            Contiene = contiene ?? string.Empty,
            Clave = string.IsNullOrWhiteSpace(clave)
                ? $"E-{Planos.Count + 1:00}"
                : clave,
            Escala = ultimo?.Escala ?? Solapa.Escala,
            Tamano = ultimo?.Tamano ?? "ARCH D (610 x 914 mm)",
            Horizontal = ultimo?.Horizontal ?? true
        };

        Planos.Add(p);
        return p;
    }
}
