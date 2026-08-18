using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CadLink.App.Models;

/// <summary>
/// Base mínima para notificación de cambios, para que las cuadrículas reflejen
/// las ediciones y las columnas calculadas al instante.
/// </summary>
public abstract class Row : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        RaiseCalculadas();
    }

    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Las clases derivadas avisan aquí de sus columnas calculadas, para no tener
    /// que recordar en cada propiedad cuáles dependen de ella.
    /// </summary>
    protected virtual void RaiseCalculadas() { }
}

/// <summary>
/// Tabla de varillas de refuerzo, en la nomenclatura mexicana por octavos de pulgada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aquí se corrige un error real de la macro de VBA.</b> Ahí la búsqueda se hacía
/// con el texto crudo de la celda:
/// </para>
/// <code>
/// v = varillaDiametros(clave)          ' espera "#4" exacto
/// If v &lt;= 0 Then v = fallback_cm * escala
/// </code>
/// <para>
/// Si la celda traía <c>4</c>, <c>No. 4</c> o cualquier variante, no había error: se
/// usaba el valor por omisión y <b>la sección se dibujaba con el diámetro equivocado
/// sin avisar</b>. En un plano estructural eso puede llegar a obra.
/// </para>
/// <para>
/// Aquí se normaliza primero, y si el diámetro sigue sin reconocerse se reporta
/// como error de captura en lugar de inventar un valor.
/// </para>
/// </remarks>
public static class Varilla
{
    /// <summary>Diámetros nominales en centímetros.</summary>
    public static readonly IReadOnlyDictionary<string, double> DiametrosCm =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["#2"] = 0.60,
            ["#2.5"] = 0.80,
            ["#3"] = 0.95,
            ["#4"] = 1.27,
            ["#5"] = 1.59,
            ["#6"] = 1.90,
            ["#8"] = 2.54,
            ["#10"] = 3.20,
            ["#12"] = 3.80
        };

    /// <summary>
    /// Lleva cualquier variante de captura a la forma canónica <c>#N</c>.
    /// Acepta <c>4</c>, <c>#4</c>, <c>No. 4</c>, <c>var 4</c>, <c>#2.5</c>.
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var t = texto.Trim().ToUpperInvariant().Replace(" ", string.Empty);

        var inicio = t.IndexOf('#');
        var i = inicio >= 0 ? inicio + 1 : 0;

        // Si no hay '#', se salta cualquier prefijo no numérico ("NO.", "VAR")
        while (i < t.Length && !char.IsDigit(t[i]))
        {
            i++;
        }

        var numero = new System.Text.StringBuilder();
        while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.'))
        {
            numero.Append(t[i]);
            i++;
        }

        var n = numero.ToString().TrimEnd('.');
        return n.Length == 0 ? string.Empty : "#" + n;
    }

    /// <summary>Diámetro en cm, o <c>false</c> si no se reconoce.</summary>
    public static bool TryDiametroCm(string? texto, out double cm)
    {
        cm = 0;
        var clave = Normalizar(texto);
        return clave.Length > 0 && DiametrosCm.TryGetValue(clave, out cm);
    }

    /// <summary>Área de una varilla en cm², o 0 si el diámetro no se reconoce.</summary>
    public static double AreaCm2(string? texto) =>
        TryDiametroCm(texto, out var d) ? Math.PI * d * d / 4.0 : 0.0;

    /// <summary>Lista de claves válidas, para mostrarla en los mensajes de error.</summary>
    public static string ClavesValidas => string.Join(", ", DiametrosCm.Keys);
}

/// <summary>
/// Una sección de concreto reforzado, con las mismas columnas que la hoja
/// <i>Secciones Estructurales Concreto</i> del libro de Excel.
/// </summary>
/// <remarks>
/// La correspondencia con las columnas de la macro está en
/// <c>docs/macro-secciones-concreto.md</c>, sección 1.
/// </remarks>
public sealed class SeccionConcretoRow : Row
{
    private string _elemento = "VIGA";
    private string _id = string.Empty;
    private double _baseCm;
    private double _alturaCm;

    private int _nEsqSup;
    private string _diamEsqSup = string.Empty;
    private int _nIntSup;
    private string _diamIntSup = string.Empty;

    private int _nEsqInf;
    private string _diamEsqInf = string.Empty;
    private int _nIntInf;
    private string _diamIntInf = string.Empty;

    private int _nInter;
    private string _diamInter = string.Empty;

    private double _recubrimientoCm = 4;
    private string _estribo = "#3";
    private string _separacionCm = "10-15-20";
    private string _estriboDiamante = string.Empty;
    private string _diamEstriboDiamante = string.Empty;
    // 5 cm es la longitud usual del gancho sísmico. Antes estaba en 1 cm y el
    // resultado era una cola MÁS CORTA QUE EL PROPIO GROSOR DEL ESTRIBO: en el
    // dibujo se veía un muñón en la esquina en lugar de un gancho.
    private double _ganchoCm = 5;
    private string _fc = "250";
    private string _escala = "25";

    // Columna W. 0 = la longitud la calcula el programa acomodando un número
    // entero de estribos en cada zona, igual que la macro de alzados.
    private double _longitudM;

    /// <summary>Columna A: tipo de elemento. Va en mayúsculas en el rótulo.</summary>
    /// <summary>
    /// Tipo de elemento. Al cambiarlo se ajusta el <c>f'c</c> por omisión.
    /// </summary>
    public string Elemento
    {
        get => _elemento;
        set
        {
            Set(ref _elemento, value);
            AplicarFcPorOmision();
        }
    }

    /// <summary>
    /// El usuario escribió el f'c a mano, así que ya no se toca solo.
    /// </summary>
    /// <remarks>
    /// Sin esta bandera, cambiar el elemento pisaría un f'c que el usuario puso a
    /// propósito. Un castillo de 250 es perfectamente legítimo; lo que se quiere es
    /// que <b>por omisión</b> sea 200, no que sea obligatorio.
    /// </remarks>
    private bool _fcManual;

    /// <summary>
    /// f'c por omisión según el elemento: 200 en castillos y cadenas, 250 en el resto.
    /// </summary>
    /// <remarks>
    /// Castillos, cadenas de cerramiento y cadenas de desplante son elementos de
    /// confinamiento, no estructurales principales, y se cuelan con concreto de menor
    /// resistencia. Poner 250 por omisión en ellos obliga a corregir a mano en cada
    /// renglón, y ese es el tipo de dato que se olvida.
    /// </remarks>
    public static string FcPorOmision(string elemento) =>
        EsDeConfinamiento(elemento) ? "200" : "250";

    /// <summary>¿Castillo o cadena?</summary>
    public static bool EsDeConfinamiento(string elemento)
    {
        var e = (elemento ?? string.Empty).Trim().ToUpperInvariant();

        return e.StartsWith("CASTILLO", StringComparison.Ordinal)
               || e.StartsWith("CADENA", StringComparison.Ordinal);
    }

    private void AplicarFcPorOmision()
    {
        if (_fcManual)
        {
            return;
        }

        var nuevo = FcPorOmision(_elemento);

        if (!string.Equals(_fc, nuevo, StringComparison.Ordinal))
        {
            _fc = nuevo;
            Raise(nameof(Fc));
            RaiseCalculadas();
        }
    }

    /// <summary>Columna B: identificador. <b>Es el nombre del bloque de AutoCAD.</b></summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Columna C.</summary>
    public double BaseCm { get => _baseCm; set => Set(ref _baseCm, value); }

    /// <summary>Columna D.</summary>
    public double AlturaCm { get => _alturaCm; set => Set(ref _alturaCm, value); }

    /// <summary>Columna E.</summary>
    public int NEsqSup { get => _nEsqSup; set => Set(ref _nEsqSup, value); }

    /// <summary>Columna F.</summary>
    public string DiamEsqSup { get => _diamEsqSup; set => Set(ref _diamEsqSup, value); }

    /// <summary>Columna G.</summary>
    public int NIntSup { get => _nIntSup; set => Set(ref _nIntSup, value); }

    /// <summary>Columna H. Si va vacía, la macro toma la F.</summary>
    public string DiamIntSup { get => _diamIntSup; set => Set(ref _diamIntSup, value); }

    /// <summary>Columna I.</summary>
    public int NEsqInf { get => _nEsqInf; set => Set(ref _nEsqInf, value); }

    /// <summary>Columna J. Si va vacía, la macro toma la F.</summary>
    public string DiamEsqInf { get => _diamEsqInf; set => Set(ref _diamEsqInf, value); }

    /// <summary>Columna K.</summary>
    public int NIntInf { get => _nIntInf; set => Set(ref _nIntInf, value); }

    /// <summary>Columna L. Si va vacía, la macro toma la J.</summary>
    public string DiamIntInf { get => _diamIntInf; set => Set(ref _diamIntInf, value); }

    /// <summary>Columna M: varillas intermedias laterales <b>por lado</b>.</summary>
    public int NInter { get => _nInter; set => Set(ref _nInter, value); }

    /// <summary>Columna N.</summary>
    public string DiamInter { get => _diamInter; set => Set(ref _diamInter, value); }

    /// <summary>Columna O.</summary>
    public double RecubrimientoCm { get => _recubrimientoCm; set => Set(ref _recubrimientoCm, value); }

    /// <summary>Columna P.</summary>
    public string Estribo { get => _estribo; set => Set(ref _estribo, value); }

    /// <summary>Columna Q: admite varios tramos, por ejemplo <c>10-15-20</c>.</summary>
    public string SeparacionCm { get => _separacionCm; set => Set(ref _separacionCm, value); }

    /// <summary>Columna R: <c>SI</c> para agregar el estribo diamante.</summary>
    public string EstriboDiamante { get => _estriboDiamante; set => Set(ref _estriboDiamante, value); }

    /// <summary>Columna S. Si va vacía, se toma el diámetro del estribo.</summary>
    public string DiamEstriboDiamante
    {
        get => _diamEstriboDiamante;
        set => Set(ref _diamEstriboDiamante, value);
    }

    /// <summary>Columna T: longitud del gancho sísmico. 0 = sin gancho.</summary>
    public double GanchoCm { get => _ganchoCm; set => Set(ref _ganchoCm, value); }

    /// <summary>Columna U.</summary>
    /// <summary>
    /// Resistencia del concreto. Viene puesta según el elemento, y se puede cambiar.
    /// </summary>
    public string Fc
    {
        get => _fc;
        set
        {
            // Escribirlo a mano lo deja fijo: a partir de aqui cambiar el elemento
            // ya no lo pisa.
            _fcManual = true;
            Set(ref _fc, value);
        }
    }

    /// <summary>Columna V.</summary>
    public string Escala { get => _escala; set => Set(ref _escala, value); }

    /// <summary>
    /// Columna W: longitud del elemento, en metros. Solo la usa el alzado.
    /// </summary>
    /// <remarks>
    /// En <c>0</c> el programa la calcula. La macro además interpreta un valor
    /// mayor o igual a 20 como centímetros, y esa conversión se conserva en
    /// <c>Estribos.LongitudDeColumnaW</c>.
    /// </remarks>
    public double LongitudM { get => _longitudM; set => Set(ref _longitudM, value); }

    // ---------------- Columnas calculadas ----------------

    /// <summary>
    /// Diámetros efectivos, aplicando las mismas reglas de herencia de la macro:
    /// si un diámetro va vacío, se hereda del lecho correspondiente.
    /// </summary>
    public string DiamIntSupEfectivo =>
        string.IsNullOrWhiteSpace(DiamIntSup) ? DiamEsqSup : DiamIntSup;

    public string DiamEsqInfEfectivo =>
        string.IsNullOrWhiteSpace(DiamEsqInf) ? DiamEsqSup : DiamEsqInf;

    public string DiamIntInfEfectivo =>
        string.IsNullOrWhiteSpace(DiamIntInf) ? DiamEsqInfEfectivo : DiamIntInf;

    /// <summary>Total de varillas longitudinales. Las laterales cuentan por los dos lados.</summary>
    public int TotalVarillas => NEsqSup + NIntSup + NEsqInf + NIntInf + (2 * NInter);

    /// <summary>
    /// Área total de acero longitudinal, en cm². No la calcula la macro, pero es
    /// lo primero que revisa cualquier estructurista.
    /// </summary>
    public double AreaAceroCm2 =>
        Math.Round(
            (NEsqSup * Varilla.AreaCm2(DiamEsqSup)) +
            (NIntSup * Varilla.AreaCm2(DiamIntSupEfectivo)) +
            (NEsqInf * Varilla.AreaCm2(DiamEsqInfEfectivo)) +
            (NIntInf * Varilla.AreaCm2(DiamIntInfEfectivo)) +
            (2 * NInter * Varilla.AreaCm2(DiamInter)),
            2);

    /// <summary>
    /// Cuantía de acero longitudinal en porcentaje del área bruta.
    /// Es la comprobación inmediata contra los mínimos y máximos de la norma.
    /// </summary>
    public double CuantiaPorcentaje =>
        BaseCm <= 0 || AlturaCm <= 0
            ? 0
            : Math.Round(AreaAceroCm2 / (BaseCm * AlturaCm) * 100.0, 3);

    protected override void RaiseCalculadas()
    {
        Raise(nameof(DiamIntSupEfectivo));
        Raise(nameof(DiamEsqInfEfectivo));
        Raise(nameof(DiamIntInfEfectivo));
        Raise(nameof(TotalVarillas));
        Raise(nameof(AreaAceroCm2));
        Raise(nameof(CuantiaPorcentaje));
    }
}

/// <summary>
/// Datos del proyecto. Hoy contiene las secciones de concreto; los demás módulos
/// se agregan conforme se porten sus macros.
/// </summary>
public sealed class DatosProyecto
{
    public ObservableCollection<SeccionConcretoRow> SeccionesConcreto { get; } = new();

    /// <summary>Carga un ejemplo para que la interfaz no arranque vacía.</summary>
    public static DatosProyecto CrearEjemplo()
    {
        var d = new DatosProyecto();

        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            // TRABE y no VIGA: 'VIGA' no es una de las familias que llevan alzado,
            // así que con ese nombre la fila de ejemplo no generaría ninguno.
            Elemento = "TRABE", Id = "T-101",
            BaseCm = 30, AlturaCm = 60,
            NEsqSup = 2, DiamEsqSup = "#6",
            NIntSup = 2, DiamIntSup = "#4",
            NEsqInf = 3, DiamEsqInf = "#8",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 1, DiamInter = "#3",
            RecubrimientoCm = 4, Estribo = "#3", SeparacionCm = "10-15-20",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "250", Escala = "25"
        });

        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = "COLUMNA", Id = "C-1",
            BaseCm = 40, AlturaCm = 40,
            NEsqSup = 3, DiamEsqSup = "#8",
            NIntSup = 0, DiamIntSup = string.Empty,
            NEsqInf = 3, DiamEsqInf = "#8",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 1, DiamInter = "#8",
            RecubrimientoCm = 4, Estribo = "#3", SeparacionCm = "10-20",
            EstriboDiamante = "SI", DiamEstriboDiamante = "#3",
            GanchoCm = 5, Fc = "250", Escala = "20"
        });

        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = "CASTILLO", Id = "K-1",
            BaseCm = 15, AlturaCm = 15,
            NEsqSup = 2, DiamEsqSup = "#3",
            NIntSup = 0, DiamIntSup = string.Empty,
            NEsqInf = 2, DiamEsqInf = "#3",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 0, DiamInter = string.Empty,
            RecubrimientoCm = 2, Estribo = "#2", SeparacionCm = "20",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "200", Escala = "10"
        });

        return d;
    }
}
