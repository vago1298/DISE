using System.Collections.ObjectModel;
using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Una fila de la hoja de <b>zapatas aisladas</b>: central o de lindero.
/// </summary>
/// <remarks>
/// <para>
/// Las columnas son las celdas de las dos macros, y el comentario de cada propiedad dice de qué
/// celda sale. Las dos macros leen <b>las mismas filas</b> —una hoja de dieciséis renglones por
/// sección— y lo único que cambia entre ellas es que la de lindero está corrida diecisiete
/// columnas a la derecha (<c>COL_OFF</c>). Así que aquí hay <b>una</b> tabla con una columna de
/// tipo, y no dos tablas: los datos son los mismos.
/// </para>
/// <para>
/// <b>Las unidades se respetan tal como las capturan las macros</b>: la zapata en metros y el
/// dado y la columna en centímetros. Es la mezcla que traen las hojas, y unificarla aquí
/// obligaría a revisar cada fórmula portada para ver si el factor de escala sigue donde debe.
/// </para>
/// </remarks>
public sealed class ZapataAisladaRow : Row
{
    private string _tipo = ZapataCad.Central;
    private string _id = "Z-1";

    private double _anchoM = 1.5;
    private double _largoM = 1.5;
    private double _profundidadM = 1.2;
    private double _espesorM = 0.3;

    private string _dobleParrilla = "SI";

    private string _varInf = "#4";
    private string _sepInf = "15";
    private string _varInfTrans = "#4";
    private string _sepInfTrans = "15";

    private string _varSup = "#4";
    private string _sepSup = "20";
    private string _varSupTrans = "#4";
    private string _sepSupTrans = "20";

    private string _tipoColumna = TipoColumnaConcreto;
    private string _idColumna = "C-1";
    private string _idDado = "D-1";

    private double _anchoDadoCm = 50;
    private double _anchoColumnaCm = 40;
    private double _recDadoCm = 5;
    private double _recColumnaCm = 5;

    private string _varDadoSup = "#4";
    private string _varDadoInf = "#4";
    private int _nIntDado;
    private string _varIntDado = string.Empty;

    private string _estriboDado = "#3";
    private string _sepEstriboDado = "15";

    private string _fc = "250";

    /// <summary>La columna que desplanta es de concreto: lleva su arranque y su alzado.</summary>
    public const string TipoColumnaConcreto = "COLUMNA DE CONCRETO";

    /// <summary>La columna es de acero: el dado remata con placa base y los ganchos van afuera.</summary>
    /// <remarks>
    /// Cambia dos cosas del dibujo, y las dos vienen de la macro: no se dibuja columna encima
    /// del dado, y los ganchos de arranque del dado doblan hacia <b>afuera</b>, porque no hay
    /// columna de concreto que los reciba.
    /// </remarks>
    public const string TipoColumnaAcero = "COLUMNA DE ACERO";

    /// <summary>Lo que ofrece el desplegable de tipo de columna.</summary>
    public static readonly string[] TiposColumna = { TipoColumnaConcreto, TipoColumnaAcero };

    /// <summary>
    /// Los <b>dados capturados en la hoja de concreto</b>, para elegirlos de una lista.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El dado de una zapata no es un dato suelto: es una <b>sección de concreto</b> que se
    /// captura en su hoja —con su armado, su estribo y su forma, cuadrada o redonda— y que la
    /// macro inserta en la zapata <b>como bloque, buscándolo por su ID</b>. Así que la casilla
    /// ofrece los dados que ya existen en lugar de pedir que se teclee un ID a ciegas: si el ID
    /// no coincide con ninguna sección, el bloque no se encuentra y la zapata sale sin dado.
    /// </para>
    /// <para>
    /// <b>Es una colección estática y observable</b>, y las dos cosas por el mismo motivo: la
    /// lista de la celda se declara en el XAML —es el patrón que funciona en esta hoja— así que
    /// necesita <i>un</i> origen al que apuntar; y siendo observable, la celda se actualiza sola
    /// cuando se agrega o se borra un dado en la hoja de concreto, sin que nadie tenga que
    /// acordarse de refrescar el desplegable. La rellena la ventana en cada cambio.
    /// </para>
    /// <para>
    /// La celda sigue siendo <b>editable</b>: se puede escribir un ID que todavía no esté
    /// capturado, porque el orden en que se llenan las hojas es del usuario, no del programa.
    /// </para>
    /// </remarks>
    public static ObservableCollection<string> DadosDisponibles { get; } = new();

    /// <summary>
    /// Las <b>columnas capturadas</b>, de concreto y de acero, para elegirlas de una lista.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo mismo que con el dado, y por el mismo motivo: la columna que desplanta en la zapata es
    /// una que ya está capturada en otra hoja, y lo que la identifica en el plano es su <b>ID</b>.
    /// Tecleándolo a mano hay dos maneras de equivocarse y ninguna se ve: escribirlo distinto
    /// —«C-1» y «C1» son la misma columna para el calculista y dos distintas para el programa— o
    /// repetirlo, y dos zapatas apuntando a la misma columna es un error de plano.
    /// </para>
    /// <para>
    /// <b>Entran las de las dos hojas</b>: las secciones de concreto cuyo elemento es COLUMNA o
    /// COLUMNA CIRCULAR, y los perfiles de acero cuyo elemento es COLUMNA. Las dos pueden
    /// desplantar en una zapata —de hecho la columna de acero es la que hace que el dado remate
    /// con placa base y que sus ganchos de arranque doblen hacia afuera— así que ofrecer solo las
    /// de concreto dejaría la mitad del trabajo fuera de la lista.
    /// </para>
    /// <para>
    /// Y va marcado de dónde sale cada una, porque el ID no lo dice: el desplegable muestra
    /// «C-1 (concreto)» o «C-4 (acero)», y lo que se guarda es solo el ID. Ver
    /// <see cref="SoloElId"/>.
    /// </para>
    /// </remarks>
    public static ObservableCollection<string> ColumnasDisponibles { get; } = new();

    /// <summary>Los dos tipos de zapata, que salen de <see cref="ZapataCad"/>.</summary>
    /// <remarks>
    /// La lista sale de la clase de geometría a propósito: es la que decide qué hace cada tipo,
    /// así que si algún día se agrega uno, el desplegable lo ofrece sin tocar nada aquí.
    /// </remarks>
    public static string[] Tipos => ZapataCad.Tipos;

    /// <summary><b>CENTRAL</b> o <b>LINDERO</b>. Decide el acomodo y dónde va el dado.</summary>
    public string Tipo
    {
        get => _tipo;
        set
        {
            Set(ref _tipo, (value ?? string.Empty).Trim().ToUpperInvariant());
            Raise(nameof(EsLindero));
            Raise(nameof(Resumen));
        }
    }

    /// <summary>Si es de lindero.</summary>
    public bool EsLindero => ZapataCad.Lindero.Equals(_tipo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombre de la sección. Es la celda <c>G1</c> / <c>X1</c>, y el nombre del bloque.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Ancho de la zapata, en metros. <c>E4</c> / <c>V4</c>.</summary>
    public double AnchoM { get => _anchoM; set { Set(ref _anchoM, value); Raise(nameof(Resumen)); Raise(nameof(Falta)); } }

    /// <summary>Largo en planta, en metros. <c>E5</c> / <c>V5</c>.</summary>
    public double LargoM { get => _largoM; set { Set(ref _largoM, value); Raise(nameof(Resumen)); } }

    /// <summary>Profundidad de desplante, en metros. <c>E6</c> / <c>V6</c>.</summary>
    public double ProfundidadM { get => _profundidadM; set { Set(ref _profundidadM, value); Raise(nameof(Falta)); } }

    /// <summary>Espesor de la zapata, en metros. <c>E7</c> / <c>V7</c>.</summary>
    public double EspesorM { get => _espesorM; set { Set(ref _espesorM, value); Raise(nameof(Falta)); } }

    /// <summary>Recubrimiento de la zapata. La macro lo fija en 5 cm.</summary>
    /// <remarks>
    /// Va como propiedad de solo lectura y no como columna porque en las dos macros es una
    /// constante —<c>rec = 0.05</c>—, y el rótulo del plano dice «Rec. 5 cm». Poner una casilla
    /// que se puede escribir daría a entender que el dibujo la respeta, y hoy no.
    /// </remarks>
    public double RecM => 0.05;

    /// <summary><c>SI</c> para doble parrilla. <c>H9</c> / <c>Y9</c>.</summary>
    public string DobleParrilla
    {
        get => _dobleParrilla;
        set
        {
            Set(ref _dobleParrilla, value);
            Raise(nameof(EsDobleParrilla));
            Raise(nameof(Resumen));
        }
    }

    /// <summary>Si lleva las dos parrillas.</summary>
    public bool EsDobleParrilla =>
        (_dobleParrilla ?? string.Empty).Trim().StartsWith("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parrilla inferior, varilla que corre a lo largo. <c>C9</c>.</summary>
    public string VarInf { get => _varInf; set { Set(ref _varInf, value); Raise(nameof(Falta)); } }

    /// <summary>Su separación en cm. <c>E9</c>.</summary>
    public string SepInf { get => _sepInf; set => Set(ref _sepInf, value); }

    /// <summary>Parrilla inferior, varilla transversal. <c>C11</c>.</summary>
    public string VarInfTrans { get => _varInfTrans; set => Set(ref _varInfTrans, value); }

    /// <summary>Su separación. <c>E11</c>.</summary>
    public string SepInfTrans { get => _sepInfTrans; set => Set(ref _sepInfTrans, value); }

    /// <summary>Parrilla superior, varilla que corre. <c>C13</c>.</summary>
    public string VarSup { get => _varSup; set => Set(ref _varSup, value); }

    /// <summary>Su separación. <c>E13</c>.</summary>
    public string SepSup { get => _sepSup; set => Set(ref _sepSup, value); }

    /// <summary>Parrilla superior, varilla transversal. <c>C15</c>.</summary>
    public string VarSupTrans { get => _varSupTrans; set => Set(ref _varSupTrans, value); }

    /// <summary>Su separación. <c>E15</c>.</summary>
    public string SepSupTrans { get => _sepSupTrans; set => Set(ref _sepSupTrans, value); }

    /// <summary>Qué desplanta el dado: columna de concreto o de acero. <c>H4</c> / <c>Y4</c>.</summary>
    public string TipoColumna
    {
        get => _tipoColumna;
        set
        {
            Set(ref _tipoColumna, value);
            Raise(nameof(EsColumnaDeConcreto));
        }
    }

    /// <summary>Si la columna es de concreto.</summary>
    public bool EsColumnaDeConcreto =>
        (_tipoColumna ?? string.Empty).IndexOf("CONCRETO", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>ID de la columna. <c>H5</c> / <c>Y5</c>.</summary>
    /// <remarks>
    /// Al asignarlo se guarda <b>solo el ID</b>: el desplegable muestra de qué hoja sale cada
    /// columna —«C-1 (concreto)»— y esa aclaración es para quien elige, no para el plano. Si se
    /// guardara tal cual, el rótulo diría «COLUMNA "C-1 (concreto)"».
    /// </remarks>
    public string IdColumna
    {
        get => _idColumna;
        set => Set(ref _idColumna, SoloElId(value));
    }

    /// <summary>Quita la aclaración de la hoja que lleva cada entrada del desplegable.</summary>
    /// <remarks>
    /// Se corta en el primer paréntesis. Un ID con paréntesis dentro no existe: en los planos son
    /// del tipo <c>C-1</c>, <c>CA-2</c>, y el paréntesis es justo lo que se agrega aquí para
    /// decir de dónde viene.
    /// </remarks>
    public static string SoloElId(string? texto)
    {
        var t = (texto ?? string.Empty).Trim();

        var p = t.IndexOf('(');

        return p < 0 ? t : t[..p].Trim();
    }

    /// <summary>ID del dado. <c>H7</c> / <c>Y7</c>. Es el nombre del bloque que se inserta.</summary>
    public string IdDado { get => _idDado; set => Set(ref _idDado, value); }

    /// <summary>Ancho del dado, en cm. <c>G8</c> / <c>X8</c>.</summary>
    public double AnchoDadoCm { get => _anchoDadoCm; set { Set(ref _anchoDadoCm, value); Raise(nameof(Resumen)); } }

    /// <summary>Ancho de la columna, en cm. <c>G6</c> / <c>X6</c>.</summary>
    public double AnchoColumnaCm { get => _anchoColumnaCm; set => Set(ref _anchoColumnaCm, value); }

    /// <summary>Recubrimiento del dado, en cm. <c>N8</c> / <c>AE8</c>.</summary>
    public double RecDadoCm { get => _recDadoCm; set => Set(ref _recDadoCm, value); }

    /// <summary>Recubrimiento de la columna, en cm. <c>N6</c> / <c>AE6</c>.</summary>
    public double RecColumnaCm { get => _recColumnaCm; set => Set(ref _recColumnaCm, value); }

    /// <summary>Varilla de arranque del dado, un paño. <c>J7</c> / <c>AA7</c>.</summary>
    public string VarDadoSup { get => _varDadoSup; set => Set(ref _varDadoSup, value); }

    /// <summary>La del otro paño. <c>J8</c> / <c>AA8</c>.</summary>
    public string VarDadoInf { get => _varDadoInf; set => Set(ref _varDadoInf, value); }

    /// <summary>Cuántas varillas intermedias lleva el dado. <c>K7</c> / <c>AB7</c>.</summary>
    public int NIntDado { get => _nIntDado; set => Set(ref _nIntDado, value); }

    /// <summary>Diámetro de las intermedias. <c>L7</c> / <c>AC7</c>.</summary>
    public string VarIntDado { get => _varIntDado; set => Set(ref _varIntDado, value); }

    /// <summary>Estribo del dado. <c>O7</c> / <c>AF7</c>.</summary>
    public string EstriboDado { get => _estriboDado; set => Set(ref _estriboDado, value); }

    /// <summary>Separación de estribos del dado, admite <c>10-15-20</c>. <c>O8</c> / <c>AF8</c>.</summary>
    public string SepEstriboDado { get => _sepEstriboDado; set => Set(ref _sepEstriboDado, value); }

    /// <summary>f'c del concreto. <c>H10</c> / <c>Y10</c>.</summary>
    public string Fc { get => _fc; set => Set(ref _fc, value); }

    /// <summary>Resumen de la fila, para el renglón de totales y el título de la vista previa.</summary>
    public string Resumen
    {
        get
        {
            var tipo = EsLindero ? "lindero" : "central";
            var parrillas = EsDobleParrilla ? "doble parrilla" : "una parrilla";

            return $"{AnchoM:N2} × {LargoM:N2} m, {tipo}, {parrillas}";
        }
    }

    /// <summary>Qué falta para poder dibujarla, o vacío si no falta nada.</summary>
    /// <remarks>
    /// Las tres medidas que las macros exigen y avisan con un <c>MsgBox</c> —ancho, profundidad
    /// y espesor— más la varilla de la parrilla inferior, que es la que siempre se dibuja. El
    /// largo no entra: si va en blanco, la macro usa el ancho.
    /// </remarks>
    public string Falta
    {
        get
        {
            var faltan = new List<string>();

            if (AnchoM <= 0)
            {
                faltan.Add("el ancho");
            }

            if (ProfundidadM <= 0)
            {
                faltan.Add("la profundidad");
            }

            if (EspesorM <= 0)
            {
                faltan.Add("el espesor");
            }

            if (string.IsNullOrWhiteSpace(VarInf))
            {
                faltan.Add("la varilla de la parrilla inferior");
            }

            return faltan.Count == 0 ? string.Empty : string.Join(", ", faltan);
        }
    }

    /// <summary>Esta fila como datos de geometría, que es lo que leen el dibujante y la previa.</summary>
    /// <remarks>
    /// Es el único puente entre la tabla y la geometría, y por eso está aquí y no repartido: si
    /// la vista previa armara su propio <see cref="ZapataCad"/> y el dibujante otro, un día
    /// dibujarían dos zapatas distintas con la misma fila.
    /// </remarks>
    public ZapataCad AFormatoCad() => new()
    {
        Id = Id,
        Tipo = Tipo,
        AnchoM = AnchoM,
        LargoM = LargoM > 0 ? LargoM : AnchoM,
        ProfundidadM = ProfundidadM,
        EspesorM = EspesorM,
        RecM = RecM,
        AnchoDadoCm = AnchoDadoCm,
        AnchoColumnaCm = AnchoColumnaCm,
        RecDadoCm = RecDadoCm,
        RecColumnaCm = RecColumnaCm,
        ColumnaDeConcreto = EsColumnaDeConcreto,
        DobleParrilla = EsDobleParrilla,
        VarInf = VarInf,
        SepInf = SepInf,
        VarInfTrans = VarInfTrans,
        SepInfTrans = SepInfTrans,
        VarSup = VarSup,
        SepSup = SepSup,
        VarSupTrans = VarSupTrans,
        SepSupTrans = SepSupTrans,
        EstriboDado = EstriboDado,
        SepEstriboDado = SepEstriboDado,
        VarDadoSup = VarDadoSup,
        VarDadoInf = VarDadoInf,
        NIntDado = NIntDado,
        VarIntDado = VarIntDado
    };
}
