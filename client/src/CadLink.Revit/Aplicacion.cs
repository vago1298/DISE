using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace CadLink.Revit;

/// <summary>Pone el boton de CadLink en la cinta de Revit.</summary>
public sealed class Aplicacion : IExternalApplication
{
    /// <summary>La pestana de la cinta.</summary>
    public const string Pestana = "CadLink";

    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            CrearCinta(app);
        }
        catch (Exception e)
        {
            // Si esto revienta, Revit ensena un cuadro de complemento roto y lo DESACTIVA.
            // Se avisa con algo que se entienda en vez de dejar que salga la excepcion cruda.
            TaskDialog.Show("CadLink",
                "No se pudo poner el boton de CadLink en la cinta.\n\n" + e.Message);

            return Result.Failed;
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    /// <summary>
    /// La cinta, por ELEMENTO, como se pidio: Entrada, Columnas, Vigas... Cada panel con lo que
    /// se le hace a ese elemento: su acero y su corte.
    /// </summary>
    private static void CrearCinta(UIControlledApplication app)
    {
        try
        {
            app.CreateRibbonTab(Pestana);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe, porque otro complemento de la casa la creo antes. No es un error:
            // se sigue y se anaden los paneles a la que hay.
        }

        // La ruta de ESTA dll, que es lo que Revit necesita para encontrar el comando.
        var dll = typeof(Aplicacion).Assembly.Location;

        // ---- Entrada: lo que llega de ETABS ----
        var entrada = app.CreateRibbonPanel(Pestana, "Entrada");

        Boton(entrada, dll, "CadLinkImportarModelo", "Importar\nETABS", typeof(ComandoImportar).FullName,
            "Importa el modelo estructural exportado por CadLink desde ETABS o SAP2000.",
            "Lee el archivo .cadlink-modelo.json que escribe CadLink, revisa que familias "
            + "hay cargadas en este proyecto, y pregunta que familia y tipo le toca a cada "
            + "seccion del modelo de calculo.\n\n"
            + "Despues crea columnas, trabes, diagonales, muros y losas nativas de Revit, "
            + "cada una en su nivel.\n\n"
            + "La eleccion se guarda: la siguiente vez no hay que volver a mapear.",
            "importar-etabs");

        // Un boton aparte para poder REHACER el armado -despues de cambiar una seccion en
        // CadLink- sin volver a pasar por el cuadro de mapeo.
        Boton(entrada, dll, "CadLinkArmar", "Armar\nmodelo", typeof(ComandoArmar).FullName,
            "Pone el armado de CadLink -varillas, bastones y estribos- en las trabes y "
            + "columnas que importo CadLink.",
            "Lee el mismo archivo .cadlink-modelo.json, que trae el armado de la tabla de "
            + "secciones de concreto, y crea varillas nativas de Revit en cada pieza que "
            + "importo CadLink: corridas y laterales con sus ganchos, bastones con su "
            + "longitud real y estribos por zonas.\n\n"
            + "Volver a armar rehace el armado de CadLink; el puesto a mano no se toca.",
            "armar-modelo");

        // ---- Columnas y Vigas: su acero y su corte ----
        //
        // «Acero» es «Armar por tipo» solo con los tipos de ESE elemento, y «Corte» es «Corte de
        // sección» solo con los suyos: un renglon por tipo, se elige la seccion de CadLink y se
        // arman todas sus piezas de un jalon.
        var columnas = app.CreateRibbonPanel(Pestana, "Columnas");

        Boton(columnas, dll, "CadLinkAceroColumnas", "Acero", typeof(ComandoAceroColumnas).FullName,
            "Arma todas las columnas de cada tipo con su seccion de la tabla de CadLink.",
            TextoAcero("columna"), "columna-acero");

        Boton(columnas, dll, "CadLinkCorteColumnas", "Corte", typeof(ComandoCorteColumnas).FullName,
            "Crea el corte de la seccion de las columnas que pidas, con su nombre y sus cotas.",
            TextoCorte("columna"), "columna-corte");

        var vigas = app.CreateRibbonPanel(Pestana, "Vigas");

        Boton(vigas, dll, "CadLinkAceroVigas", "Acero", typeof(ComandoAceroVigas).FullName,
            "Arma todas las trabes y contratrabes de cada tipo con su seccion de la tabla de CadLink.",
            TextoAcero("trabe"), "viga-acero");

        Boton(vigas, dll, "CadLinkCorteVigas", "Corte", typeof(ComandoCorteVigas).FullName,
            "Crea el corte de la seccion de las trabes que pidas, con su nombre y sus cotas.",
            TextoCorte("trabe"), "viga-corte");

        // ---- Todo junto: columnas y trabes en el mismo cuadro ----
        var todo = app.CreateRibbonPanel(Pestana, "Todo");

        Boton(todo, dll, "CadLinkArmarPorTipo", "Armar\npor tipo", typeof(ComandoArmarPorTipo).FullName,
            "Arma de un jalon todas las columnas y trabes de cada tipo con una seccion "
            + "de la tabla de CadLink.",
            TextoAcero("columna y de trabe"), "armar-tipo");

        Boton(todo, dll, "CadLinkCorte", "Corte\nde sección", typeof(ComandoCorte).FullName,
            "Crea una vista de corte transversal de la seccion que pidas, con su nombre "
            + "y sus cotas.",
            TextoCorte("columna o trabe"), "corte-seccion");

        // ---- Detalles: lo que va DEBAJO y ENTRE las piezas ya modeladas ----
        //
        // Placas base, zapatas y soldaduras. Las tablas de CadLink son de tipos, asi que se eligen
        // las piezas en el modelo y se les pone la fila de la tabla que toque.
        var detalles = app.CreateRibbonPanel(Pestana, "Detalles");

        Boton(detalles, dll, "CadLinkPlacasBase", "Placas\nbase", typeof(ComandoPlacasBase).FullName,
            "Pone la placa base de CadLink bajo las columnas elegidas: placa, anclas, grout y soldadura.",
            "Lee el .cadlink-detalles.json que escribe CadLink con el boton «Para Revit…» de la hoja "
            + "Placa Base. Se eligen las columnas, se escoge la placa, y en cada una se modela la "
            + "placa con sus barrenos, las anclas con su tuerca y su doblez, la cama de grout y el "
            + "filete de la columna todo alrededor.\n\n"
            + "Las anclas van donde las pone el detalle de AutoCAD. La placa se orienta midiendo "
            + "la columna; volver a ponerla reemplaza la anterior.",
            "placa-base");

        Boton(detalles, dll, "CadLinkCimentacion", "Cimenta-\nción", typeof(ComandoCimentacion).FullName,
            "Pone zapatas aisladas bajo las columnas y zapatas corridas bajo los muros elegidos.",
            "Lee el mismo .cadlink-detalles.json, que trae las hojas Zapatas Aisladas y Zapatas "
            + "Corridas. Bajo cada columna: la zapata, el dado y la plantilla, con la profundidad de "
            + "desplante de la tabla; si la columna ya tiene placa base de CadLink, el dado termina "
            + "bajo su grout. Bajo cada muro recto: la zapata corrida con su contratrabe o su enrase.",
            "cimentacion");

        Boton(detalles, dll, "CadLinkSoldarVigas", "Conectar\nvigas", typeof(ComandoSoldarVigas).FullName,
            "Conecta los extremos de las vigas: a una columna con filete o a momento; a un muro, con placa embebida.",
            "A una COLUMNA: filete todo alrededor del perfil, o a momento IR a IR con placa en cada patin "
            + "y placa de cortante atornillada al alma, cada placa soldada a la columna.\n\n"
            + "A un MURO de concreto: placa embebida a paño del muro, con sus anclas, y la viga soldada a "
            + "ella.\n\n"
            + "Se conectan los dos extremos, o uno: das clic en la viga cerca del extremo. El contorno sale "
            + "de la viga tal como Revit la dibujo. No necesita archivo de CadLink.",
            "soldar-viga");

        Boton(detalles, dll, "CadLinkSoldarColumnas", "Soldar\ncolumnas", typeof(ComandoSoldarColumnas).FullName,
            "Pone el filete todo alrededor de la base o de la cabeza de las columnas elegidas.",
            "Sobre lo que la columna tenga: una placa, una losa, un dado o la columna de abajo. El "
            + "contorno sale de la columna tal como esta dibujada; solo se pregunta donde y de que "
            + "tamaño. La placa base de CadLink ya trae su propia soldadura.",
            "soldar-columna");

        Boton(detalles, dll, "CadLinkEncamisar", "Encamisar\ntrabes", typeof(ComandoEncamisar).FullName,
            "Refuerza las trabes de concreto elegidas con placas de acero, o con angulos y soleras.",
            "Placas corridas en el fondo, los lados y si se pide arriba, con tornillos pasantes a media "
            + "altura; o angulos en las esquinas con soleras rodeando la trabe a cada tanto. Con su capa de "
            + "adhesivo epoxico.\n\n"
            + "La seccion y el largo se miden de la trabe dibujada, de cara a cara de columna y bajo la "
            + "losa. Volver a encamisar la misma trabe reemplaza el encamisado anterior.",
            "encamisar");

        Boton(detalles, dll, "CadLinkArmarMuros", "Armar\nmuros", typeof(ComandoArmarMuros).FullName,
            "Pone verticales y horizontales, en una o dos parrillas, en todos los muros de concreto elegidos.",
            "Un Area Reinforcement por muro, de punta a punta y de la base a la corona, con el "
            + "recubrimiento del muro: varilla y separacion de las verticales y de las horizontales, una o "
            + "dos parrillas y el gancho de las verticales. Recorta solo en puertas y ventanas.\n\n"
            + "Los muros que no sean estructurales de concreto se saltan. Volver a armar reemplaza el "
            + "armado de CadLink.",
            "armar-muros");
    }

    private static string TextoAcero(string elemento) =>
        "Lee el .cadlink-armado.json que escribe CadLink con el boton «Armado para "
        + "Revit» de la hoja de secciones de concreto, y ensena un renglon por cada TIPO "
        + $"de {elemento} del proyecto, con cuantas piezas tiene.\n\n"
        + "A cada tipo se le elige su seccion -se sugiere la que se llama o mide igual- y "
        + "al pulsar Armar se ponen varillas nativas en todas sus piezas: corridas, "
        + "laterales, bastones y estribos por zonas con la longitud real de cada una.\n\n"
        + "Las secciones de CadLink que no tengan tipo en Revit se ofrecen crear.\n\n"
        + "Volver a armar rehace el armado de CadLink; el puesto a mano no se toca.";

    private static string TextoCorte(string elemento) =>
        $"Con piezas seleccionadas, ofrece un corte por cada {elemento}; sin "
        + "seleccion, uno por cada tipo que ya tiene seccion de CadLink.\n\n"
        + "Se marca el renglon, se escribe el nombre de la vista y al pulsar Crear sale "
        + "el corte a escala 1:10 con sus llamadas, su etiqueta y sus cotas. "
        + "Usa el ultimo .cadlink-armado.json abierto.";

    /// <summary>Un boton con su texto de ayuda y su icono, en las dos medidas.</summary>
    /// <remarks>
    /// Sin imagen, el boton sale EN BLANCO con el texto solo debajo. Revit pide las dos
    /// medidas y usa una u otra segun como dibuje el boton: la de 32 en el boton grande del
    /// panel, y la de 16 cuando lo apila o lo mete en un desplegable. Dandole solo la grande,
    /// Revit la reduce al vuelo y a 16 px queda una mancha.
    /// </remarks>
    private static void Boton(
        RibbonPanel panel, string dll, string nombre, string texto, string? clase,
        string ayuda, string ayudaLarga, string icono)
    {
        var boton = new PushButtonData(nombre, texto, dll, clase)
        {
            ToolTip = ayuda,
            LongDescription = ayudaLarga
        };

        var (grande, chica) = Iconos(icono);
        boton.LargeImage = grande;
        boton.Image = chica;

        panel.AddItem(boton);
    }

    /// <summary>
    /// El icono de cada boton, en sus dos medidas: piezas en isometrico, en azules, como se pidio.
    /// Los dibuja <c>tools/make_iconos_cinta.py</c>.
    /// </summary>
    private static (ImageSource? Grande, ImageSource? Chica) Iconos(string icono) => icono switch
    {
        "importar-etabs" => (Imagen("CadLink.Revit.importar-etabs-32.png"), Imagen("CadLink.Revit.importar-etabs-16.png")),
        "armar-modelo" => (Imagen("CadLink.Revit.armar-modelo-32.png"), Imagen("CadLink.Revit.armar-modelo-16.png")),
        "columna-acero" => (Imagen("CadLink.Revit.columna-acero-32.png"), Imagen("CadLink.Revit.columna-acero-16.png")),
        "columna-corte" => (Imagen("CadLink.Revit.columna-corte-32.png"), Imagen("CadLink.Revit.columna-corte-16.png")),
        "viga-acero" => (Imagen("CadLink.Revit.viga-acero-32.png"), Imagen("CadLink.Revit.viga-acero-16.png")),
        "viga-corte" => (Imagen("CadLink.Revit.viga-corte-32.png"), Imagen("CadLink.Revit.viga-corte-16.png")),
        "armar-tipo" => (Imagen("CadLink.Revit.armar-tipo-32.png"), Imagen("CadLink.Revit.armar-tipo-16.png")),
        "corte-seccion" => (Imagen("CadLink.Revit.corte-seccion-32.png"), Imagen("CadLink.Revit.corte-seccion-16.png")),
        "placa-base" => (Imagen("CadLink.Revit.placa-base-32.png"), Imagen("CadLink.Revit.placa-base-16.png")),
        "cimentacion" => (Imagen("CadLink.Revit.cimentacion-32.png"), Imagen("CadLink.Revit.cimentacion-16.png")),
        "soldar-viga" => (Imagen("CadLink.Revit.soldar-viga-32.png"), Imagen("CadLink.Revit.soldar-viga-16.png")),
        "soldar-columna" => (Imagen("CadLink.Revit.soldar-columna-32.png"), Imagen("CadLink.Revit.soldar-columna-16.png")),
        "encamisar" => (Imagen("CadLink.Revit.encamisar-32.png"), Imagen("CadLink.Revit.encamisar-16.png")),
        "armar-muros" => (Imagen("CadLink.Revit.armar-muros-32.png"), Imagen("CadLink.Revit.armar-muros-16.png")),
        _ => (null, null)
    };

    /// <summary>Carga un icono embebido en esta DLL.</summary>
    /// <remarks>
    /// <para>
    /// Se lee del ENSAMBLADO, no de un archivo ni por una URI <c>pack://</c>. Las URI pack
    /// dependen del sistema de recursos de WPF, que dentro de Revit no siempre esta
    /// inicializado como una aplicacion WPF normal; leer el flujo del ensamblado funciona
    /// siempre.
    /// </para>
    /// <para>
    /// <b>El <c>CacheOption</c> es imprescindible.</b> Por omision un <c>BitmapImage</c> lee
    /// el flujo de forma diferida, y aqui el flujo se cierra al salir del <c>using</c>: la
    /// imagen se quedaria sin datos y el boton saldria en blanco, que es justo lo que se
    /// quiere arreglar. Con <c>OnLoad</c> se lee entero antes de cerrarlo.
    /// </para>
    /// <para>
    /// Si el icono no se puede cargar se devuelve <c>null</c> y el boton sale sin imagen,
    /// como antes. Un icono no es motivo para que el complemento no arranque.
    /// </para>
    /// </remarks>
    private static ImageSource? Imagen(string nombre)
    {
        try
        {
            using var flujo = typeof(Aplicacion).Assembly.GetManifestResourceStream(nombre);

            if (flujo is null)
            {
                return null;
            }

            var imagen = new BitmapImage();

            imagen.BeginInit();
            imagen.StreamSource = flujo;
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.EndInit();

            // Congelarla permite usarla desde cualquier hilo y evita que WPF la vuelva a
            // leer. En la cinta de Revit, que no es el hilo de una aplicacion WPF propia,
            // una imagen sin congelar puede dar problemas de afinidad de hilo.
            imagen.Freeze();

            return imagen;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
