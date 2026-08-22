using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Una fila de la hoja de <b>zapatas corridas</b>: central o de lindero.
/// </summary>
/// <remarks>
/// <para>
/// Las columnas son las celdas de <c>ZAPATA CORRIDA CENTRAL V2</c> y
/// <c>ZAPATA CORRIDA LINDERO V2</c>, y cada propiedad dice de qué celda sale: primero la de la
/// central y después la del lindero. Las dos macros leen <b>lo mismo</b> en columnas distintas
/// —cada zapata ocupa dieciséis renglones—, así que aquí hay <b>una</b> tabla con una columna de
/// tipo, igual que en las aisladas.
/// </para>
/// <para>
/// <b>Las unidades se respetan tal como las capturan las macros</b>: la zapata en metros y el
/// espesor del muro en centímetros. Es la mezcla que traen las hojas, y unificarla obligaría a
/// revisar cada fórmula portada para ver si el factor de escala sigue donde debe.
/// </para>
/// </remarks>
public sealed class ZapataCorridaRow : Row
{
    private string _tipo = ZapataCorridaCad.Central;
    private string _id = "ZC-1";

    private double _anchoM = 0.8;
    private double _profundidadM = 1.0;
    private double _espesorM = 0.2;

    private string _dobleParrilla = "NO";

    private string _varInf = "#4";
    private string _sepInf = "20";
    private string _varInfTrans = "#3";
    private string _sepInfTrans = "20";

    private string _varSup = string.Empty;
    private string _sepSup = string.Empty;
    private string _varSupTrans = string.Empty;
    private string _sepSupTrans = string.Empty;

    private string _tipoMuro = ZapataCorridaCad.MuroMamposteria;
    private double _espesorMuroCm = 15;

    private string _muroDobleParrilla = "NO";
    private string _varMuro = "#3";
    private string _sepMuroHoriz = "20";
    private string _sepMuroVert = "20";

    private string _idContratrabe = string.Empty;
    private string _idCadena = string.Empty;

    private string _fc = "250";

    /// <summary>Lo que ofrece el desplegable de tipo: <b>CENTRAL</b> o <b>LINDERO</b>.</summary>
    /// <remarks>
    /// Sale de la clase de datos y no se vuelve a escribir aquí: si algún día se agrega un tipo,
    /// el desplegable lo ofrece sin tocar la hoja.
    /// </remarks>
    public static string[] Tipos => ZapataCorridaCad.Tipos;

    /// <summary>Lo que ofrece el desplegable de muro: <b>MAMPOSTERIA</b> o <b>CONCRETO</b>.</summary>
    public static string[] TiposDeMuro => ZapataCorridaCad.TiposDeMuro;

    /// <summary>Lo que ofrecen las casillas de <b>SI</b> y <b>NO</b>.</summary>
    /// <remarks>
    /// La misma lista de las aisladas, y por el mismo motivo: la celda se lee buscando «SI» al
    /// principio, así que un «SÍ» con acento o una «S» dejarían la zapata con una sola parrilla
    /// sin que nada lo dijera. Con la lista no hay forma de equivocarse, y se sigue pudiendo
    /// teclear.
    /// </remarks>
    public static string[] SiNo => ZapataAisladaRow.SiNo;

    /// <summary>Las <b>contratrabes</b> capturadas en la hoja de concreto.</summary>
    /// <remarks>
    /// <para>
    /// La contratrabe de una zapata corrida no es un dato suelto: es una sección de concreto que
    /// se captura en su hoja y que las macros <b>insertan como bloque, buscándola por su ID</b>.
    /// Y su caja manda en tres cosas del dibujo —el hatch de concreto de la zapata, el hueco de
    /// su línea superior y hasta dónde llega el muro de enrase—, así que elegirla de una lista en
    /// lugar de teclear un ID a ciegas es la diferencia entre que salga la sección o que salga
    /// sin contratrabe y sin avisar.
    /// </para>
    /// <para>
    /// <b>Estática y observable</b>, por lo mismo que las de las aisladas: la lista de la celda se
    /// declara en el XAML —es el patrón que funciona en estas hojas— así que necesita un origen
    /// fijo al que apuntar, y siendo observable la celda se pone al día sola cuando se captura una
    /// contratrabe nueva. La rellena la ventana en cada cambio.
    /// </para>
    /// <para>
    /// La celda sigue siendo <b>editable</b>: el bloque puede estar en el dibujo de AutoCAD sin
    /// estar capturado en la hoja, y ese caso es corriente cuando el plano viene empezado.
    /// </para>
    /// </remarks>
    public static ObservableCollection<string> ContratrabesDisponibles { get; } = new();

    /// <summary>Las <b>cadenas de desplante</b> capturadas en la hoja de concreto.</summary>
    /// <remarks>
    /// Igual que la de contratrabes. La cadena es la que remata el muro de enrase por arriba: su
    /// fondo es el tope de la hilada, y su ancho es el que se enrasa, así que sin su bloque el
    /// enrase se dibuja con el ancho del muro y hasta el nivel de terreno.
    /// </remarks>
    public static ObservableCollection<string> CadenasDisponibles { get; } = new();

    /// <summary><b>CENTRAL</b> o <b>LINDERO</b>. Decide el acomodo y dónde va el muro.</summary>
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

    /// <summary>Si es de lindero: el muro va pegado a su paño derecho.</summary>
    public bool EsLindero =>
        ZapataCorridaCad.Lindero.Equals(_tipo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombre de la zapata. <c>G1</c> / <c>P1</c>, y el nombre del bloque.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Ancho de la zapata, en metros. <c>E4</c> / <c>O4</c>.</summary>
    public double AnchoM
    {
        get => _anchoM;
        set { Set(ref _anchoM, value); Raise(nameof(Resumen)); Raise(nameof(Falta)); }
    }

    /// <summary>Profundidad de desplante, en metros. <c>E5</c> / <c>O5</c>.</summary>
    public double ProfundidadM
    {
        get => _profundidadM;
        set { Set(ref _profundidadM, value); Raise(nameof(Falta)); }
    }

    /// <summary>Espesor de la zapata, en metros. <c>E6</c> / <c>O6</c>.</summary>
    public double EspesorM
    {
        get => _espesorM;
        set { Set(ref _espesorM, value); Raise(nameof(Falta)); }
    }

    /// <summary>Recubrimiento de las parrillas. Las macros lo fijan en 5 cm.</summary>
    /// <remarks>
    /// Va como propiedad de solo lectura y no como columna porque en las dos macros es una
    /// constante —<c>rec = 0.05</c>— y el rótulo del plano dice «Rec. 5 cm». Una casilla que se
    /// puede escribir daría a entender que el dibujo la respeta, y hoy no.
    /// </remarks>
    public double RecM => TrazoZapataCorrida.RecPorOmision;

    /// <summary><c>SI</c> para doble parrilla en la zapata. <c>H8</c> / <c>R8</c>.</summary>
    public string DobleParrilla
    {
        get => _dobleParrilla;
        set
        {
            Set(ref _dobleParrilla, value);
            Raise(nameof(EsDobleParrilla));
            Raise(nameof(Resumen));
            Raise(nameof(Falta));
        }
    }

    /// <summary>Si la zapata lleva las dos parrillas.</summary>
    public bool EsDobleParrilla =>
        (_dobleParrilla ?? string.Empty).Trim().StartsWith("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parrilla inferior, varilla que corre en el plano del corte. <c>C8</c>.</summary>
    public string VarInf
    {
        get => _varInf;
        set { Set(ref _varInf, value); Raise(nameof(Falta)); }
    }

    /// <summary>Su separación, en cm. <c>E8</c> / <c>O8</c>.</summary>
    public string SepInf { get => _sepInf; set => Set(ref _sepInf, value); }

    /// <summary>Parrilla inferior, varilla transversal: la que se ve de punta. <c>C10</c>.</summary>
    public string VarInfTrans { get => _varInfTrans; set => Set(ref _varInfTrans, value); }

    /// <summary>Su separación. <c>E10</c> / <c>O10</c>.</summary>
    public string SepInfTrans { get => _sepInfTrans; set => Set(ref _sepInfTrans, value); }

    /// <summary>Parrilla superior, varilla que corre. <c>C12</c>.</summary>
    public string VarSup
    {
        get => _varSup;
        set { Set(ref _varSup, value); Raise(nameof(Falta)); }
    }

    /// <summary>Su separación. <c>E12</c> / <c>O12</c>.</summary>
    public string SepSup { get => _sepSup; set => Set(ref _sepSup, value); }

    /// <summary>Parrilla superior, varilla transversal. <c>C14</c>.</summary>
    public string VarSupTrans { get => _varSupTrans; set => Set(ref _varSupTrans, value); }

    /// <summary>Su separación. <c>E14</c> / <c>O14</c>.</summary>
    public string SepSupTrans { get => _sepSupTrans; set => Set(ref _sepSupTrans, value); }

    /// <summary><b>MAMPOSTERIA</b> o <b>CONCRETO</b>. <c>H4</c> / <c>R4</c>.</summary>
    public string TipoMuro
    {
        get => _tipoMuro;
        set
        {
            Set(ref _tipoMuro, (value ?? string.Empty).Trim().ToUpperInvariant());
            Raise(nameof(MuroEsConcreto));
            Raise(nameof(MuroEsMamposteria));
            Raise(nameof(Resumen));
            Raise(nameof(Falta));
        }
    }

    /// <summary>El muro es de concreto: lleva su acero con doblez y sus círculos.</summary>
    public bool MuroEsConcreto =>
        (_tipoMuro ?? string.Empty).IndexOf(
            ZapataCorridaCad.MuroConcreto, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>El muro es de mampostería: lleva muro de enrase y cadena de desplante.</summary>
    public bool MuroEsMamposteria =>
        (_tipoMuro ?? string.Empty).IndexOf(
            ZapataCorridaCad.MuroMamposteria, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Espesor del muro, en <b>centímetros</b>. <c>H9</c> / <c>R9</c> en concreto, <c>G7</c> /
    /// <c>P7</c> en mampostería.
    /// </summary>
    /// <remarks>
    /// No es la misma celda en los dos casos, y no es un despiste de las macros: el espesor del
    /// muro de mampostería lo pone el block y vive en su propio bloque de celdas. Aquí es una
    /// sola columna porque para el dibujo es un solo dato. Si se deja en cero, las macros usan
    /// <b>15 cm</b>.
    /// </remarks>
    public double EspesorMuroCm
    {
        get => _espesorMuroCm;
        set { Set(ref _espesorMuroCm, value); Raise(nameof(Resumen)); }
    }

    /// <summary><c>SI</c> si el muro de concreto lleva acero en los dos paños. <c>H10</c> / <c>R10</c>.</summary>
    /// <remarks>La mampostería no lo pregunta: las macros la fijan en <c>NO</c>.</remarks>
    public string MuroDobleParrilla
    {
        get => _muroDobleParrilla;
        set
        {
            Set(ref _muroDobleParrilla, value);
            Raise(nameof(EsMuroDobleParrilla));
        }
    }

    /// <summary>Si el muro lleva acero en los dos paños.</summary>
    public bool EsMuroDobleParrilla =>
        (_muroDobleParrilla ?? string.Empty).Trim()
        .StartsWith("SI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Varilla del muro. <c>H11</c> / <c>R11</c> en concreto, <c>H10</c> / <c>R10</c> en
    /// mampostería.
    /// </summary>
    /// <remarks>
    /// Con mampostería las tres celdas del acero del muro <b>suben un renglón</b> en la hoja,
    /// porque no hay casilla de doble parrilla. Aquí son las mismas tres columnas: la trampa era
    /// de la lectura de la hoja, no del dato.
    /// </remarks>
    public string VarMuro
    {
        get => _varMuro;
        set { Set(ref _varMuro, value); Raise(nameof(Falta)); }
    }

    /// <summary>Separación <b>horizontal</b> del muro. <c>H12</c> / <c>R12</c>.</summary>
    /// <remarks>
    /// En el corte transversal <b>no se ve</b>: es la de las varillas verticales medida a lo
    /// largo del muro, y en la sección solo entra en el rótulo.
    /// </remarks>
    public string SepMuroHoriz { get => _sepMuroHoriz; set => Set(ref _sepMuroHoriz, value); }

    /// <summary>Separación <b>vertical</b> del muro. <c>H13</c> / <c>R13</c>.</summary>
    /// <remarks>
    /// Esta sí se ve: es la que reparte hacia arriba las varillas que en el corte salen de punta,
    /// o sea los círculos del muro. Ver <see cref="TrazoZapataCorrida.CirculosDelMuro"/>.
    /// </remarks>
    public string SepMuroVert { get => _sepMuroVert; set => Set(ref _sepMuroVert, value); }

    /// <summary>ID del bloque de la <b>contratrabe</b>. <c>H6</c> / <c>R6</c>.</summary>
    /// <remarks>
    /// Se guarda <b>solo el ID</b>: el desplegable puede mostrar de qué hoja sale y esa
    /// aclaración es para quien elige, no para el plano. Vacío = la zapata no lleva contratrabe.
    /// </remarks>
    public string IdContratrabe
    {
        get => _idContratrabe;
        set => Set(ref _idContratrabe, ZapataAisladaRow.SoloElId(value));
    }

    /// <summary>ID del bloque de la <b>cadena de desplante</b>. <c>H5</c> / <c>R5</c>.</summary>
    public string IdCadena
    {
        get => _idCadena;
        set => Set(ref _idCadena, ZapataAisladaRow.SoloElId(value));
    }

    /// <summary>f'c tal como se captura, para el rótulo. <c>J8</c> / <c>T8</c>.</summary>
    public string Fc { get => _fc; set => Set(ref _fc, value); }

    /// <summary>Si lleva contratrabe de verdad: el ID no está vacío ni es <c>0</c>.</summary>
    public bool HayContratrabe => ZapataCorridaCad.HayBloque(_idContratrabe);

    /// <summary>Si lleva cadena de desplante.</summary>
    public bool HayCadena => ZapataCorridaCad.HayBloque(_idCadena);

    /// <summary>Resumen de la fila, para el renglón de totales y el título de la previa.</summary>
    public string Resumen
    {
        get
        {
            var tipo = EsLindero ? "lindero" : "central";
            var muro = MuroEsConcreto ? "muro de concreto" : "muro de mampostería";
            var parrillas = EsDobleParrilla ? "doble parrilla" : "una parrilla";

            return $"{AnchoM:N2} m de ancho, {tipo}, {muro} e={EspesorMuroCm:N0} cm, {parrillas}";
        }
    }

    /// <summary>Qué falta para poder dibujarla, o vacío si no falta nada.</summary>
    /// <remarks>
    /// Las tres medidas que las macros exigen —ancho, profundidad y espesor— más la varilla de la
    /// parrilla inferior, que es la que siempre se dibuja. Se avisa además de dos cosas que las
    /// macros no comprueban y dejan a medias: la varilla de la parrilla <b>superior</b> cuando se
    /// pidió doble parrilla, y la del <b>muro de concreto</b>, que sin ella sale un muro macizo
    /// sin acero.
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

            if (EsDobleParrilla && string.IsNullOrWhiteSpace(VarSup))
            {
                faltan.Add("la varilla de la parrilla superior (se pidió doble parrilla)");
            }

            if (MuroEsConcreto && string.IsNullOrWhiteSpace(VarMuro))
            {
                faltan.Add("la varilla del muro de concreto");
            }

            return faltan.Count == 0 ? string.Empty : string.Join(", ", faltan);
        }
    }

    /// <summary>Esta fila como datos de geometría, que es lo que leen el dibujante y la previa.</summary>
    /// <remarks>
    /// Es el único puente entre la tabla y la geometría, y por eso está aquí y no repartido: si la
    /// vista previa armara su propio <see cref="ZapataCorridaCad"/> y el dibujante otro, un día
    /// dibujarían dos zapatas distintas con la misma fila. Es la lección de las aisladas.
    /// </remarks>
    public ZapataCorridaCad AFormatoCad() => new()
    {
        Tipo = Tipo,
        Id = Id,
        AnchoM = AnchoM,
        ProfundidadM = ProfundidadM,
        EspesorM = EspesorM,
        RecM = RecM,
        Fc = Fc,

        VarInf = VarInf,
        SepInf = SepInf,
        VarInfTrans = VarInfTrans,
        SepInfTrans = SepInfTrans,
        DobleParrilla = EsDobleParrilla,
        VarSup = VarSup,
        SepSup = SepSup,
        VarSupTrans = VarSupTrans,
        SepSupTrans = SepSupTrans,

        // Los ID van LIMPIOS: en la celda puede haber quedado «CT-1 (concreto)» porque la lista
        // muestra de dónde viene, y el nombre del bloque es «CT-1».
        IdContratrabe = ZapataAisladaRow.SoloElId(IdContratrabe),
        IdCadena = ZapataAisladaRow.SoloElId(IdCadena),

        TipoMuro = TipoMuro,
        EspesorMuroCm = EspesorMuroCm,
        MuroDobleParrilla = EsMuroDobleParrilla,
        VarMuro = VarMuro,
        SepMuroHoriz = SepMuroHoriz,
        SepMuroVert = SepMuroVert

        // El modo de relleno NO viaja por fila: es del juego entero y lo pone el dibujante.
    };
}
