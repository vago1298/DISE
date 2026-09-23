using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Base mínima para notificación de cambios, para que las cuadrículas reflejen
/// las ediciones y las columnas calculadas al instante.
/// </summary>
public abstract class Row : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <remarks>
    /// <b>Una medida se guarda con los decimales que admite su celda</b>, y de eso se encarga
    /// <see cref="Medidas.Redondear"/>. Está aquí y no en cada propiedad porque por aquí pasan
    /// TODAS: así no hay forma de que una se quede guardando 1.234 mientras la celda enseña
    /// 1.23. El redondeo va ANTES de la comparación, o volver a teclear el mismo valor
    /// redondeado avisaría de un cambio que no hubo.
    /// </remarks>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (value is double medida)
        {
            value = (T)(object)Medidas.Redondear(medida, name);
        }

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

    /// <summary>
    /// Los números de varilla, en orden, para las listas desplegables del XAML.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe porque una lista de una <b>celda de plantilla</b> no se puede rellenar por
    /// código como las de <c>DataGridComboBoxColumn</c>: la celda no tiene
    /// <c>x:Name</c>, se crea una por fila. Se ataca desde el XAML con
    /// <c>{Binding Source={x:Static models:Varilla.Diametros}}</c>.
    /// </para>
    /// <para>
    /// Sale de <see cref="DiametrosCm"/>, así que hay <b>una sola</b> tabla de
    /// diámetros y añadir una varilla la deja en las dos partes.
    /// </para>
    /// </remarks>
    public static readonly string[] Diametros = DiametrosCm.Keys.ToArray();

    /// <summary>
    /// Los mismos números, con el <b>blanco</b> delante: es la lista de las casillas
    /// que se pueden dejar vacías —las de la parrilla superior cuando no hay doble
    /// parrilla, o la vertical del muro, que en blanco copia la horizontal—.
    /// </summary>
    public static readonly string[] DiametrosOpcionales =
        new[] { string.Empty }.Concat(Diametros).ToArray();
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
    private string _estribo = EstriboGeneral;
    private string _separacionCm = "10-20-10";
    private string _estriboDiamante = string.Empty;
    private string _diamEstriboDiamante = string.Empty;
    // 5 cm es la longitud usual del gancho sísmico. Antes estaba en 1 cm y el
    // resultado era una cola MÁS CORTA QUE EL PROPIO GROSOR DEL ESTRIBO: en el
    // dibujo se veía un muñón en la esquina en lugar de un gancho.
    private double _ganchoCm = 5;
    private string _fc = "250";
    private string _escala = EscalaPorOmision;

    // Columna W. 0 = la longitud la calcula el programa acomodando un número
    // entero de estribos en cada zona, igual que la macro de alzados.
    // En cero: el largo lo pone quien captura, y si lo deja vacío la vista previa usa su
    // propio valor de respaldo para poder dibujar algo. Se probó ponerle 3 m aquí y se
    // revirtió: eso le metía 3 m a TODAS las secciones, también a las que ya tenían su
    // medida puesta a mano.
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
            AplicarEstriboPorOmision();
            AplicarPrefijoDeId();

            // El Elemento decide la FORMA: al pasar de COLUMNA a COLUMNA CIRCULAR
            // cambia todo lo que depende de ella, y la cuadricula y la vista previa
            // tienen que enterarse.
            Raise(nameof(EsCircular));
            Raise(nameof(ElementoRotulo));
            Raise(nameof(DiametroCm));

            // Al cambiar el elemento cambia el prefijo, y con el la parte editable.
            Raise(nameof(PrefijoId));
            Raise(nameof(NumeroId));
        }
    }

    // ==================================================================
    //  Prefijo del ID según el elemento
    // ==================================================================

    /// <summary>
    /// Prefijo de ID que le toca a cada elemento, y <c>null</c> si no le toca ninguno.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son los mismos prefijos que <c>MainWindow.TipoDe</c> ya reconoce para adivinar el
    /// tipo cuando el elemento viene en blanco, así que esto no inventa una convención
    /// nueva: la <b>completa sola</b> en lugar de esperar a que el usuario se la sepa de
    /// memoria.
    /// </para>
    /// <para>
    /// <b>OTRO no lleva prefijo</b> a propósito: es la fila donde el usuario pone lo que
    /// quiera, y ahí el ID también es suyo.
    /// </para>
    /// </remarks>
    public static string? PrefijoDeId(string? elemento)
    {
        var e = (elemento ?? string.Empty).Trim().ToUpperInvariant();

        return e switch
        {
            // Las dos columnas comparten prefijo: en el plano las dos son COLUMNA, y la
            // forma no cambia como se numeran.
            "COLUMNA" => "C-",
            "COLUMNA CIRCULAR" => "C-",
            // Los dos dados comparten prefijo, por lo mismo que las dos columnas.
            "DADO" => "D-",
            "DADO CIRCULAR" => "D-",
            "CASTILLO" => "K-",
            "TRABE" => "T-",
            "CONTRATRABE" => "CT-",
            "CABEZAL" => "CA-",
            "CADENA DE CERRAMIENTO" => "CC-",
            "CADENA DE DESPLANTE" => "CD-",
            // La intermedia con su propio prefijo: en el plano hay que poder distinguir de qué
            // cadena habla un ID, y CI- no choca con ninguno de los ya usados.
            "CADENA INTERMEDIA" => "CI-",
            _ => null
        };
    }

    /// <summary>Todos los prefijos conocidos, del más largo al más corto.</summary>
    /// <remarks>
    /// El orden importa: al reconocer el prefijo de un ID hay que probar <c>CT-</c> antes
    /// que <c>C-</c>, o «CT-3» se leería como una columna llamada «T-3».
    /// </remarks>
    private static readonly string[] Prefijos =
        { "CT-", "CC-", "CD-", "CI-", "CA-", "C-", "D-", "K-", "T-" };

    /// <summary>
    /// Pone en el ID el prefijo del elemento, <b>conservando el número</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Al elegir TRABE el ID pasa a <c>T-</c> y el usuario escribe el número. Si ya había
    /// un número, se conserva: de <c>T-101</c> a COLUMNA sale <c>C-101</c>, no
    /// <c>C-</c> vacío. Es lo que espera quien se equivocó de elemento y lo corrige.
    /// </para>
    /// <para>
    /// <b>Un ID que no sigue la convención NO se toca.</b> Si el usuario escribió
    /// «MÉNSULA-3» o «EJE 4», eso es suyo y cambiar el elemento no puede borrárselo: solo
    /// se reescribe cuando el ID está vacío o cuando empieza por uno de los prefijos
    /// conocidos, que es la señal de que lo puso este mismo mecanismo.
    /// </para>
    /// </remarks>
    private void AplicarPrefijoDeId()
    {
        var prefijo = PrefijoDeId(_elemento);

        if (prefijo is null)
        {
            // OTRO y cualquier nombre escrito a mano: el ID se queda como esté.
            return;
        }

        var actual = (_id ?? string.Empty).Trim();

        if (actual.Length == 0)
        {
            Id = prefijo;
            return;
        }

        // ¿Empieza por un prefijo conocido? Entonces se cambia el prefijo y se conserva
        // lo que venía detrás.
        foreach (var p in Prefijos)
        {
            if (!actual.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resto = actual[p.Length..];

            if (!string.Equals(p, prefijo, StringComparison.OrdinalIgnoreCase))
            {
                Id = prefijo + resto;
            }

            return;
        }

        // No sigue la convención: es un ID del usuario y no se toca.
    }

    /// <summary>
    /// El <b>prefijo</b> del ID de esta fila, o cadena vacía si no le toca ninguno.
    /// </summary>
    /// <remarks>
    /// Es de solo lectura y se muestra fijo al editar la celda del ID, para que el
    /// usuario no pueda romper la nomenclatura sin querer.
    /// </remarks>
    public string PrefijoId => PrefijoDeId(_elemento) ?? string.Empty;

    /// <summary>
    /// La <b>parte editable</b> del ID: el número, sin el prefijo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El usuario pidió que al editar la celda del ID solo se toque el número: si la
    /// trabe es <c>T-01</c>, editar debe cambiar el <c>01</c> por el <c>02</c> y no dejar
    /// borrar el <c>T-</c>. Eso se resuelve <b>separando el dato</b>, no intentando
    /// controlar el cursor dentro de la celda: la plantilla de edición pinta el prefijo
    /// como texto fijo y engancha el cuadro de escritura aquí.
    /// </para>
    /// <para>
    /// <b>Y el caso OTRO sale gratis</b>, que es lo bonito de plantearlo así. En OTRO no
    /// hay prefijo, así que <see cref="PrefijoId"/> es la cadena vacía, esta propiedad
    /// vale el ID entero y al escribirla se escribe el ID entero. O sea que el mismo
    /// mecanismo da «solo el número» en los elementos con nomenclatura y «todo editable»
    /// en OTRO, sin una sola condición de por medio.
    /// </para>
    /// </remarks>
    public string NumeroId
    {
        get
        {
            var id = _id ?? string.Empty;
            var p = PrefijoId;

            return p.Length > 0 && id.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                ? id[p.Length..]
                : id;
        }

        set => Id = PrefijoId + (value ?? string.Empty);
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

    // ==================================================================
    //  Estribo por omisión según el elemento
    // ==================================================================

    /// <summary>Estribo de siempre: el #3.</summary>
    public const string EstriboGeneral = "#3";

    /// <summary>El del confinamiento: castillos y cadenas se arman con el #2.</summary>
    public const string EstriboConfinamiento = "#2";

    /// <summary>
    /// El usuario eligió el estribo a mano, así que ya no se toca solo.
    /// </summary>
    /// <remarks>
    /// Misma bandera —y misma razón— que <c>_fcManual</c>: cambiar el elemento no puede
    /// pisar un estribo que se puso a propósito. Un castillo con estribo del #3 es
    /// perfectamente legítimo; lo que se quiere es que <b>por omisión</b> sea del #2.
    /// </remarks>
    private bool _estriboManual;

    /// <summary>
    /// Estribo por omisión según el elemento: <b>#2 en castillos y cadenas</b>, #3 en el
    /// resto.
    /// </summary>
    /// <remarks>
    /// Es el mismo criterio que el f'c de 200: castillos, cadenas de cerramiento, de
    /// desplante e intermedias son elementos de confinamiento, y en la práctica se arman
    /// con estribo del #2. Poner el #3 por omisión en ellos obliga a corregir a mano en
    /// cada renglón, y ese es justo el dato que se olvida.
    /// </remarks>
    public static string EstriboPorOmision(string elemento) =>
        EsDeConfinamiento(elemento) ? EstriboConfinamiento : EstriboGeneral;

    private void AplicarEstriboPorOmision()
    {
        if (_estriboManual)
        {
            return;
        }

        var nuevo = EstriboPorOmision(_elemento);

        if (!string.Equals(_estribo, nuevo, StringComparison.Ordinal))
        {
            // Se escribe el campo y NO la propiedad: pasar por la propiedad levantaría
            // la bandera de «lo puso el usuario» y el estribo dejaría de seguir al
            // elemento después del primer cambio.
            _estribo = nuevo;
            Raise(nameof(Estribo));
            RaiseCalculadas();
        }
    }

    /// <summary>Columna B: identificador. <b>Es el nombre del bloque de AutoCAD.</b></summary>
    public string Id
    {
        get => _id;
        set
        {
            Set(ref _id, value);

            // La celda del ID se pinta con el prefijo aparte del numero, asi que las dos
            // partes tienen que enterarse de que el ID cambio.
            Raise(nameof(PrefijoId));
            Raise(nameof(NumeroId));
        }
    }

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

    // ==================================================================
    //  LOS CONTEOS DE VARILLAS, COMO TEXTO
    // ==================================================================
    //
    //  Las cinco casillas de conteo viven ahora DENTRO de las columnas agrupadas
    //  -LECHO SUPERIOR, LECHO INFERIOR e INTERMEDIAS-, y una celda de plantilla no
    //  tiene modo edicion: la casilla ES un TextBox, siempre vivo.
    //
    //  Y UN TextBox ENLAZADO A UN int NO SE PUEDE VACIAR. Al borrar el contenido para
    //  teclear otro numero, la cadena vacia no convierte a int: el enlace falla, la
    //  casilla se queda con el marco rojo de dato invalido y el valor viejo, y hay que
    //  volver a escribir encima. En una columna normal eso no se notaba porque el enlace
    //  solo confirmaba al salir de la celda.
    //
    //  Con estas cinco propiedades de texto la casilla enlaza a una CADENA y traduce
    //  ella misma:
    //
    //    * vacia o en blanco  ->  0, que es lo que significa «no hay varillas» en toda
    //                             la hoja. Borrar la casilla es una forma legitima de
    //                             capturar, no un error.
    //    * un numero          ->  ese numero, nunca negativo.
    //    * cualquier otra cosa -> se ignora y se conserva lo que habia. Teclear una
    //                             letra por error no borra el dato.
    //
    //  Y NO avisan de vuelta al escribir -el int no levanta el PropertyChanged de su
    //  texto-, a proposito: si lo hicieran, el enlace reescribiria la casilla en cada
    //  tecla y al teclear «12» sobre un «0» saldria «012». Es el mismo motivo por el que
    //  las celdas de medida de esta hoja no confirman en cada tecla, y esta explicado
    //  ahi mismo, en el XAML.

    /// <summary>Lo que se lee y se escribe en la casilla de <see cref="NEsqSup"/>.</summary>
    public string NEsqSupTexto
    {
        get => _nEsqSup.ToString(CultureInfo.InvariantCulture);
        set => NEsqSup = Conteo(value, _nEsqSup);
    }

    /// <summary>Lo que se lee y se escribe en la casilla de <see cref="NIntSup"/>.</summary>
    public string NIntSupTexto
    {
        get => _nIntSup.ToString(CultureInfo.InvariantCulture);
        set => NIntSup = Conteo(value, _nIntSup);
    }

    /// <summary>Lo que se lee y se escribe en la casilla de <see cref="NEsqInf"/>.</summary>
    public string NEsqInfTexto
    {
        get => _nEsqInf.ToString(CultureInfo.InvariantCulture);
        set => NEsqInf = Conteo(value, _nEsqInf);
    }

    /// <summary>Lo que se lee y se escribe en la casilla de <see cref="NIntInf"/>.</summary>
    public string NIntInfTexto
    {
        get => _nIntInf.ToString(CultureInfo.InvariantCulture);
        set => NIntInf = Conteo(value, _nIntInf);
    }

    /// <summary>Lo que se lee y se escribe en la casilla de <see cref="NInter"/>.</summary>
    public string NInterTexto
    {
        get => _nInter.ToString(CultureInfo.InvariantCulture);
        set => NInter = Conteo(value, _nInter);
    }

    /// <summary>
    /// Traduce lo que se teclea en una casilla de conteo. Ver el comentario de arriba.
    /// </summary>
    /// <param name="texto">Lo que hay escrito en la casilla.</param>
    /// <param name="actual">Lo que vale ahora, para poder conservarlo si no es un numero.</param>
    private static int Conteo(string? texto, int actual)
    {
        var t = (texto ?? string.Empty).Trim();

        if (t.Length == 0)
        {
            return 0;
        }

        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Max(0, n)
            : actual;
    }

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

    /// <summary>Nombre del elemento cuando es una columna redonda.</summary>
    /// <remarks>
    /// La forma se elige en la columna <b>Elemento</b>, no en una casilla aparte. Es
    /// una decisión del usuario y tiene sentido: una columna circular <i>es</i> otro
    /// tipo de elemento, no una columna normal con una opción marcada, y así la lista
    /// desplegable de Elemento muestra de una vez todo lo que se puede capturar.
    /// </remarks>
    public const string ElementoColumnaCircular = "COLUMNA CIRCULAR";

    /// <summary>Dado de desplante <b>redondo</b>: el que desplanta una columna circular.</summary>
    /// <remarks>
    /// Es al DADO lo que <see cref="ElementoColumnaCircular"/> es a la COLUMNA: solo cambia la
    /// FORMA de la sección, que pasa a ser un círculo con su zuncho y sus varillas repartidas
    /// en el círculo de paso. En el plano se rotula <b>DADO</b>, igual que el cuadrado, porque
    /// lo que distingue a uno de otro es su dibujo y su cota de diámetro, no el nombre.
    /// </remarks>
    public const string ElementoDadoCircular = "DADO CIRCULAR";

    /// <summary>Dado de desplante rectangular.</summary>
    public const string ElementoDado = "DADO";

    /// <summary>Nombre con el que se <b>rotula</b> una columna, redonda o no.</summary>
    public const string ElementoColumna = "COLUMNA";

    /// <summary>Cabezal de pilas o de pilotes.</summary>
    /// <remarks>
    /// Lleva alzado <b>horizontal</b>: es una pieza tendida, como una trabe, no un
    /// elemento vertical. Ver <c>MainWindow.TipoDe</c>.
    /// </remarks>
    public const string ElementoCabezal = "CABEZAL";

    /// <summary>
    /// <b>OTRO</b>: cualquier elemento que no esté en la lista.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está en el desplegable como recordatorio de que la casilla <b>se puede
    /// escribir</b>: el combo de Elemento es editable, así que en vez de elegir OTRO se
    /// puede teclear directamente el nombre que se quiera —«MÉNSULA», «VIGA DE
    /// TRANSFERENCIA», lo que sea— y ese nombre es el que sale en el rótulo del plano.
    /// </para>
    /// <para>
    /// Un elemento escrito a mano <b>no lleva alzado</b> a menos que su ID empiece por
    /// el prefijo de un tipo conocido (<c>C-</c>, <c>T-</c>, <c>D-</c>, <c>CT-</c>),
    /// porque el programa no puede adivinar si es una pieza tendida o de pie. Sí lleva
    /// sección, con su armado y su rótulo.
    /// </para>
    /// </remarks>
    public const string ElementoOtro = "OTRO";

    // ==================================================================
    //  LOS ELEMENTOS, EN SU ORDEN
    // ==================================================================

    /// <summary>
    /// Los elementos del desplegable, <b>en el orden en que se ofrecen</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Estaba escrito dentro de <c>MainWindow.LlenarListas</c>, y ahora vive aquí porque lo
    /// usan DOS cosas: el desplegable de la celda y el botón <b>Ordenar</b> de la hoja. Ese
    /// botón agrupa las secciones por elemento, y el orden que usa es <b>este mismo</b>: el
    /// que el usuario ya ve al elegir. Con la lista escrita en dos sitios, el día que se
    /// agregara un elemento nuevo el desplegable y el orden dejarían de coincidir.
    /// </para>
    /// <para>
    /// El orden va por familias: primero lo vertical —columnas, dados, castillos—, luego lo
    /// horizontal —trabes, contratrabes, cabezales— y al final las cadenas. Las dos variantes
    /// de una misma familia van juntas, y la CIRCULAR justo después de su cuadrada, que es
    /// donde se busca.
    /// </para>
    /// <para>
    /// <b>OTRO va al final</b> a propósito: es la fila donde el usuario pone lo que quiera, y
    /// ahí también recuerda que la casilla se puede escribir.
    /// </para>
    /// </remarks>
    public static readonly string[] ElementosEnOrden =
    {
        // COLUMNA y DADO van juntos porque son los dos verticales, y son los dos que llevan
        // alzado vertical. La CIRCULAR va justo después de su cuadrada: es donde se elige la
        // FORMA, y en el plano las dos se rotulan igual. Ver ElementoRotulo.
        ElementoColumna,
        ElementoColumnaCircular,
        ElementoDado,
        ElementoDadoCircular,
        "CASTILLO",
        "TRABE",
        "CONTRATRABE",
        ElementoCabezal,

        // Las TRES cadenas, juntas: el dibujante ya las conoce y tiene reglas propias para la
        // intermedia en el corte.
        "CADENA DE CERRAMIENTO",
        "CADENA DE DESPLANTE",
        "CADENA INTERMEDIA",
        ElementoOtro,
    };

    /// <summary>
    /// Qué lugar le toca a un elemento al ordenar la hoja.
    /// </summary>
    /// <remarks>
    /// El de <see cref="ElementosEnOrden"/>. Un nombre escrito a mano —que no está en la
    /// lista— va <b>después de todos</b> los conocidos, y entre ellos se ordenan alfabéticamente
    /// en <c>MainWindow</c>: así también quedan agrupados, que es de lo que se trata, sin
    /// colarse en medio de las familias del plano.
    /// </remarks>
    public static int OrdenDeElemento(string? elemento)
    {
        var e = (elemento ?? string.Empty).Trim();

        for (var i = 0; i < ElementosEnOrden.Length; i++)
        {
            if (string.Equals(e, ElementosEnOrden[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return ElementosEnOrden.Length;
    }

    /// <summary>
    /// Compara dos ID de sección como los lee una persona: <c>K-2</c> antes de <c>K-10</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un ID es un prefijo y un número —<c>K-1</c>, <c>CT-12</c>—, así que comparándolos como
    /// texto a secas <b>K-10 queda antes de K-2</b>, porque el «1» es menor que el «2». En una
    /// lista de cuarenta castillos eso se ve enseguida y parece que el botón de ordenar no
    /// funciona.
    /// </para>
    /// <para>
    /// Los que <b>no</b> siguen la convención se comparan como texto, y los <b>vacíos van al
    /// final</b> de su grupo: son las filas a medio capturar, y es donde se buscan.
    /// </para>
    /// <para>
    /// Quien hace la comparación es <see cref="ComparadorDeId"/>, y está <b>al final de este
    /// archivo</b> y no anidado aquí: las comprobaciones de <c>tools/validar.py</c> leen las
    /// propiedades de una fila desde su <c>class</c> hasta la siguiente, y una clase anidada
    /// les cortaba la lectura a media fila.
    /// </para>
    /// </remarks>
    public static readonly IComparer<string> PorId = new ComparadorDeId();

    /// <summary>
    /// Las separaciones de estribos que se usan a diario, para el desplegable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son <b>sugerencias, no una lista cerrada</b>: la celda sigue siendo de texto libre
    /// y se puede teclear cualquier otra. Por eso el combo de esa columna va con
    /// <c>IsEditable</c> y enlazado por <c>Text</c> y no por <c>SelectedItem</c>: con
    /// <c>SelectedItem</c>, lo que se teclea a mano no llega a la propiedad y se perdería
    /// al salir de la celda.
    /// </para>
    /// <para>
    /// El orden no es alfabético, es de uso: primero las de tres tramos —confinamiento en
    /// los extremos y el centro más abierto, que es el caso normal de una trabe o una
    /// columna— de la más cerrada a la más abierta, y al final las de separación única
    /// (15 y 20 cm), que son las que se usan en parrillas y mallas de zapata.
    /// </para>
    /// <para>
    /// La lista se dejó corta a propósito: solo las cinco de tres tramos que se repiten
    /// en casi todos los planos más las dos únicas. Cualquier otra —"5-10-15", "10-20",
    /// "30"— se teclea a mano en la celda; se guarda igual porque la columna es de texto
    /// libre. Esta misma lista alimenta el desplegable de Secciones de Concreto y el de
    /// Zapatas Aisladas, así que cambiarla aquí las cambia en las dos hojas.
    /// </para>
    /// </remarks>
    public static readonly string[] SeparacionesUsuales =
    {
        "5-10-5",
        "6-12-6",
        "7-14-7",
        "8-16-8",
        "9-18-9",
        "10-20-10",
        "15",
        "20"
    };

    /// <summary>
    /// Columna heredada: <c>SI</c> marcaba la sección como redonda.
    /// </summary>
    /// <remarks>
    /// <b>Ya no se captura.</b> La forma se elige en <see cref="Elemento"/>, poniendo
    /// «COLUMNA CIRCULAR». Esta propiedad se conserva <b>solo</b> para que un archivo
    /// <c>.clk</c> guardado con la versión anterior siga abriendo con sus columnas
    /// redondas intactas; por eso <see cref="EsCircular"/> la sigue mirando.
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
    /// <remarks>
    /// Manda el <b>Elemento</b>. La columna <see cref="Circular"/> se sigue mirando
    /// para no romper los <c>.clk</c> guardados antes de que la forma se eligiera
    /// desde el Elemento; un archivo viejo no tiene «COLUMNA CIRCULAR» en ninguna
    /// fila y sin esto sus columnas redondas volverían a salir cuadradas.
    /// </remarks>
    public bool EsCircular =>
        EsElementoCircular(_elemento)
        || (_circular ?? string.Empty).Trim().Equals("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Esta sección lleva estribo diamante?</summary>
    /// <remarks>
    /// La columna R dice <c>SI</c>, y la sección tiene que ser <b>rectangular</b>: en una
    /// redonda no hay rombo que dibujar, y la macro ignoraba la columna R en ese caso. Es la
    /// misma regla que aplica el dibujante al armar el <c>SeccionCad</c>; está aquí para que
    /// la vista previa no tenga que repetirla y para que las dos no puedan discrepar.
    /// </remarks>
    public bool LlevaDiamante =>
        !EsCircular
        && (_estriboDiamante ?? string.Empty).Trim()
            .Equals("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>¿El nombre del elemento es el de una columna redonda?</summary>
    public static bool EsElementoCircular(string? elemento)
    {
        var e = (elemento ?? string.Empty).Trim();

        // Las DOS formas redondas: la columna y el dado. Sin el dado, un «DADO CIRCULAR»
        // se dibujaria como un rectangulo con estribos rectangulares, o sea NO se
        // dibujaria lo que se pidio.
        return e.Equals(ElementoColumnaCircular, StringComparison.OrdinalIgnoreCase)
               || e.Equals(ElementoDadoCircular, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nombre del elemento <b>tal como debe aparecer en el plano</b>.
    /// </summary>
    /// <remarks>
    /// Una columna redonda se rotula <b>COLUMNA</b>, igual que una cuadrada. Lo pidió
    /// el usuario y es lo correcto: en el plano lo que distingue a una de otra es su
    /// dibujo y su cota de diámetro, no el nombre. Escribir «COLUMNA CIRCULAR» en el
    /// rótulo sería redundante, y además rompería la nomenclatura del juego de planos.
    /// <para>
    /// «COLUMNA CIRCULAR» es solo el nombre de <b>captura</b>, el que se elige en la
    /// cuadrícula para decidir la forma.
    /// </para>
    /// </remarks>
    public string ElementoRotulo
    {
        get
        {
            var e = (_elemento ?? string.Empty).Trim();

            // Cada forma redonda se rotula con el nombre de SU pieza: la columna redonda
            // como COLUMNA y el dado redondo como DADO. Rotular el dado como «COLUMNA»
            // -que es lo que pasaba con el atajo de antes- pondria en el plano una pieza
            // que no es la que se dibujo.
            if (e.Equals(ElementoColumnaCircular, StringComparison.OrdinalIgnoreCase))
            {
                return ElementoColumna;
            }

            if (e.Equals(ElementoDadoCircular, StringComparison.OrdinalIgnoreCase))
            {
                return ElementoDado;
            }

            return _elemento ?? string.Empty;
        }
    }

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
    /// <remarks>
    /// Viene puesto según el elemento —#2 en castillos y cadenas, #3 en el resto— y
    /// <b>se puede cambiar</b>: elegirlo a mano lo deja fijo y a partir de ahí cambiar
    /// el elemento ya no lo pisa. Ver <see cref="EstriboPorOmision"/>.
    /// </remarks>
    public string Estribo
    {
        get => _estribo;
        set
        {
            _estriboManual = true;
            Set(ref _estribo, value);
        }
    }

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

    /// <summary>
    /// Escala por omisión del detalle: el <b>1:10</b> con el que se rotula la sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes venía en 25 —y en 20 en la mitad de las filas de ejemplo—, y había que
    /// corregir renglón por renglón: en la práctica las secciones de este juego de planos
    /// se dibujan a 1:10. Es el mismo número al que ya recurre el alzado cuando la celda
    /// va vacía (<c>AlzadoDrawer</c>), así que ahora la tabla y el dibujo coinciden.
    /// </para>
    /// <para>
    /// <b>Es solo el valor de arranque.</b> La celda se sigue escribiendo a mano: quien
    /// necesite 1:20 o 1:25 en una sección lo pone y ahí se queda.
    /// </para>
    /// </remarks>
    public const string EscalaPorOmision = "10";

    /// <summary>Columna V. Empieza en <see cref="EscalaPorOmision"/> y se puede cambiar.</summary>
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

    // ======================================================================
    //  Grapas: los estribos suplementarios que unen dos varillas
    // ======================================================================

    private readonly List<GrapaSeccion> _grapas = new();

    /// <summary>Las grapas de esta sección.</summary>
    /// <remarks>
    /// <para>
    /// Se expone como <b>solo lectura</b> y se toca por los métodos de abajo, no
    /// devolviendo la lista para que quien quiera le añada cosas.
    /// </para>
    /// <para>
    /// El motivo es el aviso de cambios. <see cref="Row.Set{T}"/> compara con
    /// <c>EqualityComparer</c>, y la referencia de una lista NO cambia al añadirle un
    /// elemento: si el resto del programa metiera grapas directamente en la lista,
    /// <c>PropertyChanged</c> no se dispararía nunca y la vista previa no se
    /// enteraría hasta el siguiente redibujado por otro motivo. Pasando por estos
    /// métodos, el aviso sale siempre.
    /// </para>
    /// </remarks>
    public IReadOnlyList<GrapaSeccion> Grapas => _grapas;

    /// <summary>Pone una grapa entre dos varillas.</summary>
    /// <remarks>
    /// No admite grapas de una varilla a sí misma ni repetidas: las dos cosas se ven
    /// igual en el dibujo que nada, y una repetida no habría forma de quitarla porque
    /// tapa a la otra.
    /// </remarks>
    public bool AgregarGrapa(RefVarilla a, RefVarilla b, string diametro)
    {
        if (a.Equals(b) || YaHayGrapa(a, b))
        {
            return false;
        }

        _grapas.Add(new GrapaSeccion { A = a, B = b, Diametro = diametro });
        Raise(nameof(Grapas));
        return true;
    }

    /// <summary>¿Ya hay una grapa entre esas dos varillas?</summary>
    public bool YaHayGrapa(RefVarilla a, RefVarilla b) => _grapas.Any(g => g.Une(a, b));

    /// <summary>Quita la grapa que une esas dos varillas, si la hay.</summary>
    public bool QuitarGrapa(RefVarilla a, RefVarilla b)
    {
        var cuantas = _grapas.RemoveAll(g => g.Une(a, b));

        if (cuantas == 0)
        {
            return false;
        }

        Raise(nameof(Grapas));
        return true;
    }

    /// <summary>Quita todas las grapas de la sección.</summary>
    public void LimpiarGrapas()
    {
        if (_grapas.Count == 0)
        {
            return;
        }

        _grapas.Clear();
        Raise(nameof(Grapas));
    }

    /// <summary>
    /// Mete una grapa <b>sin</b> avisar de que cambió, para cargar el proyecto.
    /// </summary>
    /// <remarks>
    /// Al abrir un archivo se cargan las filas de golpe y el aviso por grapa no sirve
    /// de nada: la vista previa se redibuja una vez al final. Es el mismo motivo por
    /// el que la carga apaga <c>_listo</c>.
    /// </remarks>
    public void CargarGrapa(GrapaSeccion g) => _grapas.Add(g);

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

    /// <summary>Las secciones de acero: perfiles IR, OR, OC y CF.</summary>
    public ObservableCollection<PerfilAceroRow> SeccionesAcero { get; } = new();

    /// <summary>Las zapatas aisladas: centrales y de lindero, en una sola tabla.</summary>
    /// <remarks>
    /// Una tabla y no dos porque las dos macros leen <b>las mismas filas</b>: la de lindero es
    /// la misma hoja corrida diecisiete columnas. Lo que cambia es el acomodo y dónde va el
    /// dado, y eso lo dice la columna de tipo.
    /// </remarks>
    public ObservableCollection<ZapataAisladaRow> ZapatasAisladas { get; } = new();

    /// <summary>Las zapatas corridas: centrales y de lindero, también en una sola tabla.</summary>
    /// <remarks>
    /// Por lo mismo que las aisladas: las dos macros de corrida leen <b>las mismas celdas</b> en
    /// columnas distintas, y lo que cambia —el acomodo, dónde va el muro y cómo arranca su acero—
    /// lo dice la columna de tipo.
    /// </remarks>
    public ObservableCollection<ZapataCorridaRow> ZapatasCorridas { get; } = new();

    /// <summary>Las placas base, con sus anclas, sus cartabones y su dado.</summary>
    /// <remarks>
    /// Una sola tabla para las diez familias de perfil, por lo mismo que la hoja de acero: la
    /// macro <c>DibujarPlacaBase_BloqueXX</c> lee <b>las mismas celdas</b> sea la columna un IR o
    /// un tubo. Lo único que cambia es la forma que se dibuja encima de la placa, y eso lo dice
    /// la familia.
    /// </remarks>
    public ObservableCollection<PlacaBaseRow> PlacasBase { get; } = new();

    /// <summary>Carga un ejemplo para que la interfaz no arranque vacía.</summary>
    /// <remarks>
    /// <b>Ninguna sección de concreto del ejemplo escribe su ESTRIBO ni su ESCALA a mano</b>,
    /// y es a propósito: los dos los pone ya el elemento —#2 en castillos y cadenas, #3 en
    /// el resto, y escala 1:10 en todas—, así que ponerlos aquí sería repetir el mismo dato
    /// en dos sitios. Y algo peor: escribir el estribo levanta la bandera de «lo eligió el
    /// usuario», y entonces cambiarle el elemento a una fila del ejemplo ya no lo
    /// actualizaría. Ver <see cref="SeccionConcretoRow.EstriboPorOmision"/>.
    /// </remarks>
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
            RecubrimientoCm = 4, SeparacionCm = "10-20-10",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "250"
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
            RecubrimientoCm = 4, SeparacionCm = "10-20",
            EstriboDiamante = "SI", DiamEstriboDiamante = "#3",
            GanchoCm = 5, Fc = "250"
        });

        // Columna REDONDA, para que el ejemplo muestre las dos formas. La base es el
        // diametro y el armado se captura como TOTAL, no por lechos.
        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            // La forma la manda el Elemento. En el plano se rotula «COLUMNA».
            // La constante va CUALIFICADA: este metodo vive en DatosProyecto, no en
            // SeccionConcretoRow, asi que sin el nombre de la clase delante no esta
            // en ambito. Es el CS0103 que rompio la compilacion.
            Elemento = SeccionConcretoRow.ElementoColumnaCircular, Id = "C-2",
            BaseCm = 50, AlturaCm = 50,
            NVarTotal = 8, DiamVarTotal = "#8",
            ZunchoHelicoidal = "SI",
            RecubrimientoCm = 4, SeparacionCm = "10-20",
            GanchoCm = 5, Fc = "250",
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
            RecubrimientoCm = 2, SeparacionCm = "20",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "200"
        });

        // UN DADO, y está aquí por la misma razón que la contratrabe de abajo: la hoja de PLACAS
        // BASE toma las medidas de su dado de ESTA hoja, buscándolo por su ID. Sin un dado
        // capturado, el desplegable «ID dado» de la placa saldría vacío en el ejemplo y no se vería
        // que las dos hojas están enlazadas —que es justo lo que hay que ver de un tirón—.
        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = SeccionConcretoRow.ElementoDado, Id = "D-1",
            BaseCm = 50, AlturaCm = 50,
            NEsqSup = 2, DiamEsqSup = "#4",
            NIntSup = 0, DiamIntSup = string.Empty,
            NEsqInf = 2, DiamEsqInf = "#4",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 0, DiamInter = string.Empty,
            RecubrimientoCm = 4, SeparacionCm = "10-20-10",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "250",
            LongitudM = 1
        });

        // Estas dos son las que usan las ZAPATAS CORRIDAS del ejemplo: las dos macros las
        // insertan como BLOQUE buscándolas por su ID, así que tienen que existir aquí para
        // que la hoja de corridas arranque sin avisos y se vea de una vez cómo se enlazan.
        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = "CONTRATRABE", Id = "CT-1",
            BaseCm = 20, AlturaCm = 40,
            NEsqSup = 2, DiamEsqSup = "#4",
            NIntSup = 0, DiamIntSup = string.Empty,
            NEsqInf = 2, DiamEsqInf = "#4",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 0, DiamInter = string.Empty,
            RecubrimientoCm = 4, SeparacionCm = "9-18-9",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "250"
        });

        d.SeccionesConcreto.Add(new SeccionConcretoRow
        {
            Elemento = "CADENA DE DESPLANTE", Id = "CD-1",
            BaseCm = 15, AlturaCm = 20,
            NEsqSup = 2, DiamEsqSup = "#3",
            NIntSup = 0, DiamIntSup = string.Empty,
            NEsqInf = 2, DiamEsqInf = "#3",
            NIntInf = 0, DiamIntInf = string.Empty,
            NInter = 0, DiamInter = string.Empty,
            RecubrimientoCm = 3, SeparacionCm = "20",
            EstriboDiamante = string.Empty, DiamEstriboDiamante = string.Empty,
            GanchoCm = 5, Fc = "200"
        });

        // ---------- Secciones de acero, UNA DE CADA FAMILIA ----------
        //
        // Son doce, una por familia, y eso es a propósito: entre las doce se dibujan las
        // NUEVE formas distintas, así que el ejemplo enseña de una vez todo lo que la hoja
        // sabe hacer. Y de paso enseña lo que no se ve mirando una sola fila: que la IR, la
        // IS, la IC y la S se dibujan iguales y solo se distinguen por el color y el nombre.
        //
        // Los nombres son las DESIGNACIONES DEL MANUAL IMCA, tal como salen en el
        // desplegable, no versiones abreviadas: así, al abrir la celda «Perfil», el que ya
        // está puesto aparece marcado en la lista. Y las medidas son las del catálogo, no
        // números inventados: se pueden cotejar contra la tabla de perfiles.

        void Acero(
            string familia, string perfil, string id, string elemento, string acero,
            double peralte, double ancho = 0, double eAlma = 0, double ePatin = 0,
            double labio = 0, double radio = 0, double anchoMenor = 0,
            string clasificacion = "")
        {
            d.SeccionesAcero.Add(new PerfilAceroRow
            {
                Familia = familia, Perfil = perfil, Id = id,
                Elemento = elemento, Clasificacion = clasificacion, Acero = acero,
                PeralteCm = peralte, AnchoCm = ancho,
                EspesorAlmaCm = eAlma, EspesorPatinCm = ePatin,
                LabioCm = labio, RadioCm = radio, AnchoMenorCm = anchoMenor
            });
        }

        // EL ACERO DE CADA FILA ES UNO QUE EL MANUAL DA COMO DISPONIBLE EN ESA FAMILIA,
        // y no el mismo para todas. No es un detalle: la tabla marca en rojo la fila cuando
        // el acero no se hace en el perfil, así que un ejemplo con aceros mal puestos
        // arrancaría con media hoja en rojo y enseñaría a ignorar el rojo.
        //
        // De ahí que el monten y la zeta lleven A-1008 —son lámina rolada en frío, y el
        // A-36 no se hace en lámina—, el tubo rectangular lleve A-500 Gr. B' —el Gr. B sin
        // apóstrofo es el tubo redondo, con otro Fy— y el tensor de redondo macizo lleve
        // A-572 Gr. 50.
        //
        // Forma I: cuatro familias que se dibujan igual.
        Acero(FamiliaPerfil.Ir, "W - 12'' x 30.04 lb/ft", "V-1",
              PerfilAceroRow.ElementoViga, PerfilAceroRow.AceroA992,
              31.3, 16.6, 0.67, 1.12, clasificacion: "PRINCIPAL");

        Acero(FamiliaPerfil.Is, "IS - 150 mm x 9.5 mm / 450 mm x 6.4 mm", "VA-1",
              PerfilAceroRow.ElementoViga, PerfilAceroRow.AceroA572,
              46.9, 15.0, 0.64, 0.95, clasificacion: "PRINCIPAL");

        Acero(FamiliaPerfil.Ic, "IC - 16 '' x 52.14 lb/ft", "CA-1",
              PerfilAceroRow.ElementoColumna, PerfilAceroRow.AceroA572,
              39.9, 14.0, 0.64, 0.88);

        Acero(FamiliaPerfil.S, "S - 10'' x 25.4 lb/ft", "V-2",
              PerfilAceroRow.ElementoViga, PerfilAceroRow.AceroA36,
              25.4, 11.8, 0.79, 1.25, clasificacion: "SECUNDARIA");

        // Te y canal laminada.
        Acero(FamiliaPerfil.Wt, "WT - 8'' x 13.0 lb/ft", "CS-1",
              "PUNTAL", PerfilAceroRow.AceroA572,
              19.9, 14.0, 0.64, 0.88);

        Acero(FamiliaPerfil.C, "C - 8'' x 12.0 lb/ft", "AT-1",
              "ATIESADOR", PerfilAceroRow.AceroA36,
              20.3, 5.7, 0.56, 0.99);

        // Formados en frío. El monten es el larguero de cubierta y la zeta su alternativa:
        // la zeta lleva el patín angosto, que es lo que permite traslaparlas en el apoyo.
        Acero(FamiliaPerfil.Cf, "CF - 6\" x 2\" x #14", "MO-1",
              "MONTEN", PerfilAceroRow.AceroA1008,
              15.24, 5.08, 0.19, labio: 1.52, radio: 0.24);

        Acero(FamiliaPerfil.Zf, "ZF - 8\" x 2 3/8\" x #14", "LG-1",
              "LARGUERO", PerfilAceroRow.AceroA1008,
              20.32, 6.03, 0.19, radio: 0.476, anchoMenor: 5.4);

        // Ángulo: sus dos alas y su espesor, que es lo único que da el manual.
        Acero(FamiliaPerfil.L, "L - 3'' x 1/4''", "DG-1",
              "DIAGONAL", PerfilAceroRow.AceroA36,
              7.62, 7.62, 0.635);

        // Tubos y redondo macizo.
        Acero(FamiliaPerfil.Or, "HSS - 6\" x 1/4\"", "C-1",
              PerfilAceroRow.ElementoColumna, PerfilAceroRow.AceroA500Bp,
              15.2, 15.2, 0.64);

        Acero(FamiliaPerfil.Oc, "PIPE - 4.02 in x 0.19 in", "PT-1",
              "PUNTAL", PerfilAceroRow.AceroA53B,
              10.2, eAlma: 0.48);

        Acero(FamiliaPerfil.Os, "OS - 3/4\"", "TN-1",
              PerfilAceroRow.ElementoTensor, PerfilAceroRow.AceroA572,
              1.91);

        // ---------- Zapatas aisladas: una de cada tipo ----------
        //
        // Una CENTRAL con doble parrilla y una de LINDERO con una sola, que es el caso
        // corriente de cada una. Con las dos en el ejemplo se ve de una vez lo que cambia
        // entre ellas: el dado centrado contra el dado pegado al paño, y el acomodo, que
        // en la central crece a la derecha y en el lindero a la izquierda.
        d.ZapatasAisladas.Add(new ZapataAisladaRow
        {
            Tipo = ZapataCad.Central, Id = "Z-1",
            AnchoM = 1.6, LargoM = 1.6, ProfundidadM = 1.2, EspesorM = 0.3,
            DobleParrilla = "SI",
            VarInf = "#4", SepInf = "15", VarInfTrans = "#4", SepInfTrans = "15",
            VarSup = "#4", SepSup = "20", VarSupTrans = "#4", SepSupTrans = "20",
            TipoColumna = ZapataAisladaRow.TipoColumnaConcreto,
            IdColumna = "C-1", IdDado = "D-1",
            AnchoDadoCm = 50, AnchoColumnaCm = 40,
            VarDadoSup = "#5", VarDadoInf = "#5", NIntDado = 1, VarIntDado = "#4",
            EstriboDado = "#3", SepEstriboDado = "9-18-9",
            Fc = "250"
        });

        d.ZapatasAisladas.Add(new ZapataAisladaRow
        {
            Tipo = ZapataCad.Lindero, Id = "ZL-1",
            AnchoM = 1.4, LargoM = 1.8, ProfundidadM = 1.2, EspesorM = 0.3,
            DobleParrilla = "NO",
            VarInf = "#4", SepInf = "15", VarInfTrans = "#4", SepInfTrans = "15",
            TipoColumna = ZapataAisladaRow.TipoColumnaConcreto,
            IdColumna = "C-3", IdDado = "D-3",
            AnchoDadoCm = 45, AnchoColumnaCm = 35,
            VarDadoSup = "#5", VarDadoInf = "#5",
            EstriboDado = "#3", SepEstriboDado = "15",
            Fc = "250"
        });

        // ---------- Zapatas corridas: una de cada tipo ----------
        //
        // Una CENTRAL con muro de mampostería —el caso corriente: lleva cadena de desplante y
        // muro de enrase de block— y una de LINDERO con muro de concreto y doble parrilla, que
        // es donde se ve lo que las separa: en la central cada pata del muro dobla hacia su
        // lado, y en el lindero las dos doblan a la izquierda y a dos alturas, porque por la
        // derecha está el lindero.
        d.ZapatasCorridas.Add(new ZapataCorridaRow
        {
            Tipo = ZapataCorridaCad.Central, Id = "ZC-1",
            AnchoM = 0.8, ProfundidadM = 1.0, EspesorM = 0.2,
            DobleParrilla = "NO",
            VarInf = "#4", SepInf = "20", VarInfTrans = "#3", SepInfTrans = "20",
            TipoMuro = ZapataCorridaCad.MuroMamposteria,
            EspesorMuroCm = 15,
            IdCadena = "CD-1", IdContratrabe = string.Empty,
            VarMuro = "#3", SepMuroHoriz = "20", SepMuroVert = "20",
            Fc = "250"
        });

        d.ZapatasCorridas.Add(new ZapataCorridaRow
        {
            Tipo = ZapataCorridaCad.Lindero, Id = "ZCL-1",
            AnchoM = 1.0, ProfundidadM = 1.2, EspesorM = 0.25,
            DobleParrilla = "SI",
            VarInf = "#4", SepInf = "15", VarInfTrans = "#4", SepInfTrans = "20",
            VarSup = "#4", SepSup = "20", VarSupTrans = "#4", SepSupTrans = "20",
            TipoMuro = ZapataCorridaCad.MuroConcreto,
            EspesorMuroCm = 20,
            MuroDobleParrilla = "SI",
            VarMuro = "#4", SepMuroHoriz = "20", SepMuroVert = "20",
            IdContratrabe = "CT-1",
            Fc = "250"
        });

        // ===== PLACA BASE =====
        // La del ejemplo de la macro: placa de 40x40 en 1", cuatro anclas de 3/4" repartidas en el
        // perímetro, dado de concreto de 50x50 y una IR de 8" encima. Las separaciones al borde van
        // en CERO, que es la manera de decir «calcúlalas»: la placa sobresale del patín y las anclas
        // se acomodan en ese sobrante.
        d.PlacasBase.Add(new PlacaBaseRow
        {
            Marca = "PB-1",
            LargoCm = 40, AnchoCm = 40,
            Espesor = "1", AceroPlaca = "A-36",

            // EL DADO SE REFERENCIA, no se teclea: «D-1» es el dado capturado arriba en esta misma
            // hoja, y de ahí salen sus 50x50. Las dos medidas se dejan puestas porque la referencia
            // se resuelve al arrancar la ventana, no aquí: así el ejemplo se ve completo también si
            // alguien mira estas filas antes de que la interfaz las enlace.
            IdDado = "D-1",
            DadoXCm = 50,
            DadoYCm = 50,
            // EL NOMBRE ES EL DEL CATALOGO, tal cual. La búsqueda es por nombre exacto, así que un
            // «W8X31» escrito a la americana no se encuentra y la placa saldría sin su columna.
            Familia = FamiliaPerfil.Ir, Seccion = "W - 8'' x 31.04 lb/ft",
            NAnclasX = 4, NAnclasY = 0,
            SepBordeXCm = 0, SepBordeYCm = 0,
            DiamAnclaX = "3/4", DiamAnclaY = "3/4",
            Electrodo = "E70XX",
            Soldadura = "1/4",
            SoldaduraCartabon = "3/16",
            ConCartabones = false,
            // Solo se ven en el alzado: la longitud VERTICAL del ancla -E12 y E13, lo que se
            // ahoga en el concreto- y, si se encienden los cartabones, su altura -F18 y F19-.
            //
            // Y NO se escribe la longitud total del ancla. Era la otra casilla de la misma
            // barra, se quito de la hoja, y en cero el alzado deduce el largo de la longitud
            // vertical: una sola verdad para el largo del ancla.
            LongAnclajeXCm = 30, LongAnclajeYCm = 30,
            DoblezAnclaXCm = 10, DoblezAnclaYCm = 10,
            AltoCartabonXCm = 20, AltoCartabonYCm = 20,
            Escala = 10
        });

        return d;
    }
}


/// <summary>
/// Compara dos ID de sección como los lee una persona: <c>K-2</c> antes de <c>K-10</c>.
/// </summary>
/// <remarks>
/// <para>
/// Un ID es un prefijo y un número —<c>K-1</c>, <c>CT-12</c>—, así que comparándolos como texto
/// a secas <b>K-10 queda antes de K-2</b>, porque el «1» es menor que el «2». En una lista de
/// cuarenta castillos eso se ve enseguida y parece que el botón <b>Ordenar</b> no funciona.
/// </para>
/// <para>
/// Se llega a él por <see cref="SeccionConcretoRow.PorId"/>, que es la única instancia: no
/// guarda nada, así que una sola sirve para toda la aplicación.
/// </para>
/// <para>
/// Es una clase <b>suelta</b>, no anidada en <see cref="SeccionConcretoRow"/>, porque las
/// comprobaciones de <c>tools/validar.py</c> leen las propiedades de una fila desde su
/// <c>class</c> hasta la siguiente que aparece en el archivo, y una clase anidada les cortaba
/// esa lectura a media fila.
/// </para>
/// </remarks>
internal sealed class ComparadorDeId : IComparer<string>
{
    public int Compare(string? a, string? b)
    {
        var x = (a ?? string.Empty).Trim();
        var y = (b ?? string.Empty).Trim();

        // Los vacíos, al final. Y dos vacíos empatan, que es lo que deja el orden de captura
        // entre ellos: OrderBy es estable.
        if (x.Length == 0 || y.Length == 0)
        {
            return x.Length == y.Length ? 0 : (x.Length == 0 ? 1 : -1);
        }

        var (px, nx, rx) = Partir(x);
        var (py, ny, ry) = Partir(y);

        var porPrefijo = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);

        if (porPrefijo != 0)
        {
            return porPrefijo;
        }

        if (nx != ny)
        {
            return nx.CompareTo(ny);
        }

        return string.Compare(rx, ry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parte «CT-12A» en «CT-», 12 y «A».</summary>
    /// <remarks>
    /// Sin número reconocible devuelve <c>-1</c>, que lo pone antes que cualquier numerado del
    /// mismo prefijo: un «K-» a secas es una fila sin terminar.
    /// </remarks>
    private static (string Prefijo, long Numero, string Resto) Partir(string id)
    {
        var i = 0;

        while (i < id.Length && !char.IsDigit(id[i]))
        {
            i++;
        }

        var prefijo = id[..i];
        var j = i;

        while (j < id.Length && char.IsDigit(id[j]))
        {
            j++;
        }

        if (j == i)
        {
            return (prefijo, -1, string.Empty);
        }

        return long.TryParse(id[i..j], out var n)
            ? (prefijo, n, id[j..])
            : (prefijo, -1, id[i..]);
    }
}
