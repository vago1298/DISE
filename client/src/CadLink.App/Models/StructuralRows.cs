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
    /// <summary>
    /// Diámetros nominales en centímetros: <b>n octavos de pulgada</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Estos números estaban redondeados y uno de ellos estaba mal.</b> Salió al
    /// comparar el port con la macro de alzados, cuya tabla <c>RebarDiaM</c> es más
    /// precisa que la que tenía aquí:
    /// </para>
    /// <list type="table">
    ///   <item><term>#2</term><description>0.60 aquí, 0.64 en la macro, 0.635 el nominal. El área salía <b>12.1 % baja</b>.</description></item>
    ///   <item><term>#6</term><description>1.90 aquí, 1.905 el nominal. Área 1.0 % baja.</description></item>
    ///   <item><term>#10</term><description>3.20 aquí, 3.175 el nominal. Área 1.3 % <b>alta</b>.</description></item>
    ///   <item><term>#12</term><description>3.80 aquí, 3.81 el nominal. Área 0.5 % baja.</description></item>
    /// </list>
    /// <para>
    /// Un 12 % de menos en el área de un #2 no es un detalle de dibujo: se propaga a
    /// <c>AreaAceroCm2</c> y a la cuantía, y una cuantía baja es del lado
    /// <b>inseguro</b>, porque hace pasar por bueno un armado que no llega al mínimo.
    /// </para>
    /// <para>
    /// Así que la tabla se pone en el valor <b>exacto</b>: la varilla del número
    /// <c>n</c> mide <c>n/8</c> de pulgada, y una pulgada son 25.4 mm exactos. No se
    /// redondea nada, y la comprobación está en
    /// <c>tools/verificar_diametros_varilla.py</c>.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, double> DiametrosCm =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["#2"] = 0.635,       // 2/8"
            ["#2.5"] = 0.79375,   // 2.5/8"
            ["#3"] = 0.9525,      // 3/8"
            ["#4"] = 1.27,        // 4/8"
            ["#5"] = 1.5875,      // 5/8"
            ["#6"] = 1.905,       // 6/8"
            ["#8"] = 2.54,        // 8/8"
            ["#10"] = 3.175,      // 10/8"
            ["#12"] = 3.81        // 12/8"
        };

    /// <summary>Diámetro nominal exacto de la varilla número <paramref name="n"/>.</summary>
    /// <remarks>
    /// Existe para que la tabla se pueda comprobar contra la fórmula en lugar de
    /// contra otra tabla escrita a mano, que es como se colaron los redondeos.
    /// </remarks>
    public static double NominalCm(double n) => n / 8.0 * 2.54;

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

    // ---------------- Sección circular ----------------
    // Va POR FILA, no por corrida: en un mismo juego de planos conviven columnas
    // rectangulares y circulares, y el usuario pidió expresamente que solo la
    // sección que él marque salga redonda.
    private string _circular = string.Empty;
    private int _nVarTotal;
    private string _diamVarTotal = string.Empty;
    private string _zunchoHelicoidal = string.Empty;

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

    // ==================================================================
    // Sección circular
    // ==================================================================

    /// <summary>
    /// <c>SI</c> para que <b>esta</b> sección se dibuje redonda.
    /// </summary>
    /// <remarks>
    /// Es por fila a propósito. Un juego de planos normal mezcla columnas
    /// rectangulares y circulares, así que un interruptor global obligaría a hacer
    /// dos corridas y a acomodar el plano a mano.
    /// </remarks>
    public string Circular
    {
        get => _circular;
        set
        {
            Set(ref _circular, value);

            // El armado se lee de otras columnas segun la forma, así que al cambiar
            // la forma hay que reavisar de las calculadas. Set() ya lo hace, pero
            // tambien cambia EsCircular y DiametroCm, que la cuadricula usa para
            // atenuar las columnas que dejan de aplicar.
            Raise(nameof(EsCircular));
            Raise(nameof(DiametroCm));
        }
    }

    /// <summary>¿Esta sección es circular?</summary>
    public bool EsCircular =>
        (_circular ?? string.Empty).Trim().Equals("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Diámetro de la sección circular, en cm. Es la <b>base</b>.
    /// </summary>
    /// <remarks>
    /// No se agrega una columna nueva para el diámetro: en una sección redonda la
    /// base ES el diámetro, y tener dos casillas para el mismo número es la forma
    /// segura de que un día no coincidan. La altura se ignora, y
    /// <c>Revisar</c> lo avisa si trae un valor distinto.
    /// </remarks>
    public double DiametroCm => BaseCm;

    /// <summary>
    /// Varillas <b>totales</b> del círculo, no por lecho.
    /// </summary>
    /// <remarks>
    /// En una columna redonda no hay lecho superior ni inferior: el acero se reparte
    /// en un solo círculo de paso. Pedirlo por lechos obligaría al usuario a hacer
    /// una división mental que además no tiene una respuesta única.
    /// </remarks>
    public int NVarTotal { get => _nVarTotal; set => Set(ref _nVarTotal, value); }

    /// <summary>Diámetro de las varillas del círculo. Si va vacío se toma la F.</summary>
    public string DiamVarTotal { get => _diamVarTotal; set => Set(ref _diamVarTotal, value); }

    /// <summary>Diámetro efectivo de las varillas del círculo.</summary>
    public string DiamVarTotalEfectivo =>
        string.IsNullOrWhiteSpace(DiamVarTotal) ? DiamEsqSup : DiamVarTotal;

    /// <summary>
    /// <c>SI</c> = el zuncho sube en <b>hélice</b>; vacío = anillos sueltos.
    /// </summary>
    /// <remarks>
    /// Lo decide el usuario y no el programa, porque son dos formas de armar
    /// distintas y las dos son correctas: la hélice se arma de una pieza continua y
    /// el anillo se corta y se amarra uno por uno. Solo cambia el alzado; en la
    /// sección las dos se ven igual, como un anillo.
    /// </remarks>
    public string ZunchoHelicoidal
    {
        get => _zunchoHelicoidal;
        set
        {
            Set(ref _zunchoHelicoidal, value);
            Raise(nameof(EsZunchoHelicoidal));
        }
    }

    /// <summary>¿El zuncho sube en hélice?</summary>
    public bool EsZunchoHelicoidal =>
        (_zunchoHelicoidal ?? string.Empty).Trim()
            .Equals("SI", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Total de varillas longitudinales.
    /// </summary>
    /// <remarks>
    /// En la sección <b>circular</b> es el conteo total y ya está: no se suma nada,
    /// porque no hay lechos. En la rectangular se suman los cuatro grupos y las
    /// laterales cuentan por los dos lados.
    /// </remarks>
    public int TotalVarillas =>
        EsCircular
            ? NVarTotal
            : NEsqSup + NIntSup + NEsqInf + NIntInf + (2 * NInter);

    /// <summary>
    /// Área total de acero longitudinal, en cm². No la calcula la macro, pero es
    /// lo primero que revisa cualquier estructurista.
    /// </summary>
    public double AreaAceroCm2 =>
        Math.Round(
            EsCircular
                ? NVarTotal * Varilla.AreaCm2(DiamVarTotalEfectivo)
                : (NEsqSup * Varilla.AreaCm2(DiamEsqSup)) +
                  (NIntSup * Varilla.AreaCm2(DiamIntSupEfectivo)) +
                  (NEsqInf * Varilla.AreaCm2(DiamEsqInfEfectivo)) +
                  (NIntInf * Varilla.AreaCm2(DiamIntInfEfectivo)) +
                  (2 * NInter * Varilla.AreaCm2(DiamInter)),
            2);

    /// <summary>
    /// Área bruta de concreto, en cm². Depende de la forma.
    /// </summary>
    /// <remarks>
    /// Existe aparte de la cuantía porque es el número que se equivoca solo: usar
    /// <c>base × altura</c> en una sección redonda da un área un 27 % mayor que la
    /// real (<c>D²</c> contra <c>πD²/4</c>) y por tanto una cuantía un 27 % MENOR
    /// que la verdadera. Una cuantía subestimada es del lado inseguro: hace pasar
    /// por bueno un armado que no llega al mínimo.
    /// </remarks>
    public double AreaBrutaCm2 =>
        EsCircular
            ? Math.PI * DiametroCm * DiametroCm / 4.0
            : BaseCm * AlturaCm;

    /// <summary>
    /// Cuantía de acero longitudinal en porcentaje del área bruta.
    /// Es la comprobación inmediata contra los mínimos y máximos de la norma.
    /// </summary>
    public double CuantiaPorcentaje =>
        AreaBrutaCm2 <= 0
            ? 0
            : Math.Round(AreaAceroCm2 / AreaBrutaCm2 * 100.0, 3);

    protected override void RaiseCalculadas()
    {
        Raise(nameof(DiamIntSupEfectivo));
        Raise(nameof(DiamEsqInfEfectivo));
        Raise(nameof(DiamIntInfEfectivo));
        Raise(nameof(DiamVarTotalEfectivo));
        Raise(nameof(EsCircular));
        Raise(nameof(DiametroCm));
        Raise(nameof(EsZunchoHelicoidal));
        Raise(nameof(TotalVarillas));
        Raise(nameof(AreaAceroCm2));
        Raise(nameof(AreaBrutaCm2));
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

        // Columna REDONDA, para que el ejemplo muestre las dos formas. La base es el
        // diametro y el armado se captura como TOTAL, no por lechos.
        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = "COLUMNA", Id = "C-2",
            Circular = "SI",
            BaseCm = 50, AlturaCm = 50,
            NVarTotal = 8, DiamVarTotal = "#8",
            ZunchoHelicoidal = "SI",
            RecubrimientoCm = 4, Estribo = "#3", SeparacionCm = "10-20",
            GanchoCm = 5, Fc = "250", Escala = "20",
            LongitudM = 3
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
