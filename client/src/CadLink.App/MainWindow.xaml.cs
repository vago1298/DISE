using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;
using CadLink.Etabs;
using CadLink.Licensing;
using Microsoft.Win32;

// System.Windows.Shapes tambien define un tipo llamado Path, que choca con
// System.IO.Path. Estos alias dejan claro cual se usa en cada caso: 'Path' es
// el de archivos y 'FormaPath' es la figura de WPF que se usa en la vista previa.
using Path = System.IO.Path;
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

/// <summary>
/// Ventana principal, organizada en hojas al estilo de Excel, con los mismos
/// módulos del libro original.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LicenseService _licenseService;
    private LicenseInfo _license;
    private DatosProyecto _datos = DatosProyecto.CrearEjemplo();
    private ModeloEtabs? _modeloEtabs;

    /// <summary>
    /// De <b>qué programa</b> es el modelo que está en <see cref="_modeloEtabs"/>.
    /// </summary>
    /// <remarks>
    /// Hace falta porque los dos programas dan un <c>ModeloEtabs</c> igualito y, sin
    /// guardar de dónde salió, con la casilla en SAP2000 se seguía enseñando la tabla de
    /// ETABS sin avisar. Con esto la tabla se vacía cuando dejan de coincidir y el botón
    /// vuelve a leer en lugar de reaprovechar lo que hay.
    /// </remarks>
    private EtabsConnection.ProgramaCsi? _destinoLeido;

    private readonly VistaModelo _vista = new();
    private Point _arrastreDesde;
    private bool _girando;
    private bool _moviendo;
    private bool _listo;

    public MainWindow(LicenseService licenseService, LicenseInfo license)
    {
        _licenseService = licenseService;
        _license = license;

        InitializeComponent();

        // El logo ya no se pinta en un encabezado propio: es el ICONO de la ventana,
        // que es donde Windows lo muestra sin gastar alto de la hoja.
        Icon = Branding.Logo;

        LlenarListas();

        HeaderVersion.Text = "v" + AppInfo.Version;

        // El nombre del producto y la empresa van SOLO en la barra de titulo. Antes
        // se repetian en el encabezado azul, que es lo que se quito.
        var empresa = AppInfo.CompanyName;
        var hayEmpresa = !string.IsNullOrWhiteSpace(empresa);

        Title = hayEmpresa
            ? $"{AppInfo.ProductName} — {empresa}"
            : AppInfo.ProductName;

        Enlazar();
        AplicarLicencia(license);

        // El tema que el usuario dejó puesto la última vez. Va AQUÍ, en el
        // constructor, y no en el Loaded: WPF no dibuja nada hasta que el
        // constructor termina, así que la ventana ya aparece con su tema y no se ve
        // el parpadeo de claro a oscuro.
        Tema.Cargar();
        TemaButton.Content = Tema.TextoDelBoton;

        PreviewCanvas.SizeChanged += (_, _) => DibujarVistaPrevia();
        SeccionesGrid.SelectionChanged += OnSeccionSeleccionada;

        // Lo mismo para la hoja de acero. Va aquí, junto a lo del concreto, porque las dos
        // vistas previas se enganchan UNA VEZ en el arranque: Enlazar se vuelve a llamar al
        // cargar el ejemplo y al empezar de nuevo, y suscribirse ahí dejaría el mismo evento
        // enganchado cinco veces.
        EngancharVistaPreviaAcero();
        EngancharVistaPreviaZapata();
        EngancharVistaPreviaZapataCorrida();

        // Los lienzos del visor se redibujan al cambiar de tamaño: la escala se
        // calcula con el ancho y el alto reales, que valen 0 hasta que WPF hace
        // el primer layout.
        Vista3DCanvas.SizeChanged += (_, _) => _vista.Dibujar3D(Vista3DCanvas);
        ExtruidaCanvas.SizeChanged += (_, _) => _vista.DibujarExtruido(ExtruidaCanvas);
        PlantaCanvas.SizeChanged += (_, _) => DibujarPlanta();

        Loaded += (_, _) =>
        {
            DibujarVistaPrevia();
            RedibujarVistas();
        };

        PrepararSolapa();

        _listo = true;
    }

    /// <summary>
    /// Estilo elegido para TODAS las secciones. Equivale a la celda AC de la hoja.
    /// </summary>
    private ModoSeccion ModoElegido =>
        Tipo1Radio.IsChecked == true ? ModoSeccion.Tipo1SinRelleno : ModoSeccion.Tipo2Rellena;

    /// <remarks>
    /// El evento Checked de un RadioButton con IsChecked en el XAML se dispara
    /// durante InitializeComponent, cuando los demás controles todavía no existen.
    /// Esta bandera evita el fallo por referencia nula en ese primer disparo.
    /// </remarks>
    private void OnTipoSeccionCambiado(object sender, RoutedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        DibujarVistaPrevia();
        StatusText.Text = ModoElegido == ModoSeccion.Tipo1SinRelleno
            ? "Secciones tipo 1: no rellenas."
            : "Secciones tipo 2: rellenas.";
    }

    // ======================================================================
    // Datos
    // ======================================================================

    /// <summary>
    /// Llena las listas desplegables de las celdas, al estilo de la validación de
    /// datos de Excel.
    /// </summary>
    /// <remarks>
    /// Las listas salen de <see cref="Varilla.DiametrosCm"/>, que es la misma tabla
    /// que usa la validación. Así no pueden desincronizarse: si algún día se agrega
    /// un diámetro, aparece en el desplegable y se acepta al validar, sin tocar dos
    /// lugares.
    /// </remarks>
    private void LlenarListas()
    {
        var diametros = Varilla.DiametrosCm.Keys.ToList();

        // Las columnas opcionales llevan una entrada vacía al principio, para
        // poder dejarlas en blanco y que herede el diámetro del otro lecho.
        var opcionales = new List<string> { string.Empty };
        opcionales.AddRange(diametros);

        ColElemento.ItemsSource = new[]
        {
            // COLUMNA y DADO van juntos porque son los dos verticales, y son los
            // dos que llevan alzado vertical.
            //
            // COLUMNA CIRCULAR va justo despues de COLUMNA: es donde se elige la
            // FORMA. En el plano las dos se rotulan «COLUMNA», ver
            // SeccionConcretoRow.ElementoRotulo.
            SeccionConcretoRow.ElementoColumna,
            SeccionConcretoRow.ElementoColumnaCircular,
            // Y los dos dados, con la misma idea: DADO CIRCULAR va justo despues de
            // DADO porque es donde se elige la FORMA. Los dos se rotulan «DADO».
            SeccionConcretoRow.ElementoDado,
            SeccionConcretoRow.ElementoDadoCircular,
            "CASTILLO", "TRABE", "CONTRATRABE",
            SeccionConcretoRow.ElementoCabezal,
            "CADENA DE CERRAMIENTO", "CADENA DE DESPLANTE",

            // OTRO va AL FINAL, y es un recordatorio de que la casilla se puede
            // escribir: el combo es editable, asi que se puede teclear cualquier nombre
            // y ese es el que sale en el rotulo. Ver SeccionConcretoRow.ElementoOtro.
            SeccionConcretoRow.ElementoOtro
        };

        ColVarEsqSup.ItemsSource = diametros;
        ColEstribo.ItemsSource = diametros;

        ColVarIntSup.ItemsSource = opcionales;
        ColVarEsqInf.ItemsSource = opcionales;
        ColVarIntInf.ItemsSource = opcionales;
        ColVarLateral.ItemsSource = opcionales;
        ColVarDiamante.ItemsSource = opcionales;

        ColDiamante.ItemsSource = new[] { string.Empty, "SI" };

        // Seccion circular. La FORMA se elige en ColElemento, no aqui: lo unico que
        // se captura es su armado. «Var total» es opcional porque si va vacia hereda
        // el diametro de la columna F, igual que los demas diametros de la hoja.
        ColZuncho.ItemsSource = new[] { string.Empty, "SI" };
        ColVarTotal.ItemsSource = opcionales;

        // Y las de la hoja de acero, que viven en MainWindow.Acero.cs.
        LlenarListasAcero();
        LlenarListasZapatas();
        LlenarListasZapatasCorridas();
    }

    private void Enlazar()
    {
        SeccionesGrid.ItemsSource = _datos.SeccionesConcreto;

        // TIEMPO REAL. La colección solo avisa cuando se agrega o se quita una fila,
        // no cuando se EDITA una celda. Sin esto, cambiar el gancho o la separación no
        // movía la vista previa hasta que se seleccionaba otra sección: el usuario
        // ajustaba un valor y no veía el efecto, que es justo lo que sirve la vista.
        //
        // Se escucha el PropertyChanged de cada fila, y se entra y se sale de la
        // suscripción con la colección para no dejar filas escuchando después de
        // borrarlas, que es una fuga de memoria y además redibuja de más.
        _datos.SeccionesConcreto.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (Row fila in e.OldItems)
                {
                    fila.PropertyChanged -= OnFilaEditada;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (Row fila in e.NewItems)
                {
                    fila.PropertyChanged += OnFilaEditada;
                }
            }

            DatosCambiaron();
        };

        foreach (var fila in _datos.SeccionesConcreto)
        {
            fila.PropertyChanged += OnFilaEditada;
        }

        if (_datos.SeccionesConcreto.Count > 0)
        {
            SeccionesGrid.SelectedIndex = 0;
        }

        // La hoja de acero se enlaza AQUI, dentro de Enlazar, y no en el constructor:
        // Enlazar se vuelve a llamar al cargar el ejemplo, al borrar todo y al empezar un
        // trabajo nuevo, y en esos tres casos _datos es OTRO objeto. Enlazando el acero
        // aparte, su cuadricula seguiria mostrando la coleccion del proyecto anterior.
        EnlazarAcero();
        EnlazarZapatas();
        EnlazarZapatasCorridas();

        DatosCambiaron();
    }

    /// <summary>
    /// Una celda cambió: se redibuja al instante.
    /// </summary>
    /// <remarks>
    /// Solo se redibuja si la fila editada es la que se está viendo. En una hoja con
    /// cien secciones, redibujar por cada tecla de cualquier fila haría la edición
    /// pesada sin que se viera nada distinto.
    /// </remarks>
    private void OnFilaEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        ActualizarTotales();

        // LAS LISTAS DE LA HOJA DE ZAPATAS SE REFRESCAN TAMBIÉN AL EDITAR UNA FILA, no solo al
        // agregarla o borrarla. Aquí estaba el defecto de «no me aparece el dado que tengo»: al
        // agregar la fila su ID está vacío, y el ID y el elemento se escriben DESPUÉS —editando—,
        // así que la lista se armaba con la fila en blanco y no volvía a mirarla. El dado existía
        // en su hoja y el desplegable de la zapata no lo ofrecía.
        //
        // Va sin filtrar por propiedad a propósito: el ID y el elemento deciden si entra en la
        // lista, y la base, el recubrimiento y el armado deciden las medidas que la zapata trae
        // por referencia. Filtrar por nombre de propiedad es la clase de lista que se queda corta
        // en cuanto se agrega una columna.
        ActualizarListasDeZapatas();

        if (ReferenceEquals(sender, Seleccionada))
        {
            DibujarVistaPrevia();
        }
    }

    private void DatosCambiaron()
    {
        RegistrarEnHistorial();

        // Las listas de la hoja de zapatas —los dados y las columnas— salen de ESTA hoja,
        // así que se refrescan aquí: al agregar, borrar o renombrar uno, el desplegable se
        // entera solo. Las columnas de acero avisan por su lado, desde su propia hoja.
        ActualizarListasDeZapatas();

        // Y las de la hoja de zapatas corridas: la contratrabe y la cadena de desplante también
        // se capturan en la hoja de concreto y también se insertan como bloque por su ID.
        ActualizarListasDeZapatasCorridas();

        ActualizarContadores();
        ActualizarTotales();
        DibujarVistaPrevia();
    }

    // ======================================================================
    // DESHACER (Ctrl+Z)
    // ======================================================================

    private readonly Historial _historial = new();

    /// <summary>El trabajo tal como quedó después del último cambio.</summary>
    /// <remarks>
    /// Es la pieza que hace que esto funcione sin interceptar cada sitio que toca los datos: se
    /// guarda cómo quedó todo, y cuando llega el cambio SIGUIENTE, lo que se apila es este
    /// estado —el de antes—, no el nuevo. Sin él habría que acordarse de tomar la instantánea
    /// antes de cada cambio, en cada uno de los caminos que los producen.
    /// </remarks>
    private Instantanea? _estadoActual;

    /// <summary>Si se está deshaciendo, para no apilar el propio deshacer.</summary>
    private bool _deshaciendo;

    private void RegistrarEnHistorial()
    {
        // Al arrancar, mientras se enlazan las cuadrículas, no hay nada que deshacer todavía.
        if (!_listo || _deshaciendo)
        {
            return;
        }

        if (_estadoActual is not null)
        {
            _historial.Apilar(_estadoActual);
        }

        _estadoActual = TomarInstantanea();
        ActualizarBotonDeshacer();
    }

    private Instantanea TomarInstantanea() =>
        new(ArmarProyecto(), _datos.SeccionesAcero);

    private void ActualizarBotonDeshacer()
    {
        DeshacerButton.IsEnabled = _historial.Puede;

        DeshacerButton.ToolTip = _historial.Puede
            ? $"Deshace el último cambio (Ctrl+Z). Hay {_historial.Cuantos} paso(s) guardados."
            : "No hay nada que deshacer.";
    }

    /// <summary>
    /// Deshace el último cambio. También responde a <b>Ctrl+Z</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Si el cursor está dentro de una celda, gana la celda.</b> Ahí Ctrl+Z es el deshacer
    /// del cuadro de texto —letra por letra, mientras se escribe— y es lo que cualquiera
    /// espera: quitarle al usuario el deshacer de lo que está tecleando para deshacerle en su
    /// lugar la fila anterior sería una sorpresa desagradable. El historial del trabajo entra
    /// cuando la celda ya no tiene nada que deshacer.
    /// </para>
    /// </remarks>
    private void OnDeshacer(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox caja && caja.CanUndo)
        {
            caja.Undo();
            return;
        }

        var paso = _historial.Deshacer();

        if (paso is null)
        {
            StatusText.Text = "No hay nada que deshacer.";
            return;
        }

        _deshaciendo = true;

        try
        {
            AplicarProyecto(paso.Proyecto);

            // Las secciones de acero van aparte porque el archivo .clk todavía no las guarda.
            _datos.SeccionesAcero.Clear();

            foreach (var p in paso.Acero)
            {
                _datos.SeccionesAcero.Add(p);
            }
        }
        finally
        {
            _deshaciendo = false;
        }

        // El estado actual pasa a ser el que se acaba de poner, no el que había: si no, el
        // siguiente cambio apilaría otra vez el estado deshecho y Ctrl+Z se quedaría dando
        // vueltas entre dos estados sin avanzar hacia atrás.
        _estadoActual = paso;

        ActualizarBotonDeshacer();
        ActualizarTotalesAcero();
        DibujarVistaPreviaAcero();

        StatusText.Text = _historial.Puede
            ? $"Se deshizo el último cambio. Quedan {_historial.Cuantos} paso(s) atrás."
            : "Se deshizo el último cambio. Ya no queda nada que deshacer.";
    }

    /// <summary>
    /// Borra el historial: al abrir otro trabajo, al empezar de cero o al cargar el ejemplo.
    /// </summary>
    /// <remarks>
    /// Deshacer después de abrir otro archivo devolvería al trabajo anterior sin avisar, y el
    /// usuario creería que le deshicieron un cambio cuando lo que le cambió fue el archivo.
    /// </remarks>
    private void OlvidarHistorial()
    {
        _historial.Limpiar();
        _estadoActual = _listo ? TomarInstantanea() : null;
        ActualizarBotonDeshacer();
    }

    private void ActualizarContadores() =>
        CountsText.Text = $"Secciones de concreto: {_datos.SeccionesConcreto.Count}";

    private void ActualizarTotales()
    {
        var n = _datos.SeccionesConcreto.Count;
        var vars = _datos.SeccionesConcreto.Sum(s => s.TotalVarillas);
        var acero = _datos.SeccionesConcreto.Sum(s => s.AreaAceroCm2);

        TotalesText.Text =
            $"{n} seccion(es)   ·   {vars} varillas longitudinales   ·   " +
            $"acero total {acero:N2} cm²";
    }

    private void OnSeccionSeleccionada(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SeccionesGrid))
        {
            return;
        }

        DibujarVistaPrevia();
    }

    private SeccionConcretoRow? Seleccionada => SeccionesGrid.SelectedItem as SeccionConcretoRow;

    // ======================================================================
    // Licencia
    // ======================================================================

    private void AplicarLicencia(LicenseInfo info)
    {
        info = AppInfo.ConNombreDeEmpresa(info);
        _license = info;

        HeaderLicense.Text = info.StatusLine;
        StatusText.Text = info.StatusLine;

        LicTierText.Text = info.Tier switch
        {
            LicenseTier.Internal => "Interna, permanente (uso de la empresa)",
            LicenseTier.Commercial => "Comercial (suscripción)",
            LicenseTier.Trial => "Prueba gratuita",
            _ => "Desconocida"
        };

        LicOrgText.Text = string.IsNullOrWhiteSpace(info.Organization) ? "—" : info.Organization;

        LicExpiryText.Text = info.LicenseExpiresAt is null
            ? "Sin fecha de vencimiento"
            : $"{info.LicenseExpiresAt.Value.ToLocalTime():dd/MM/yyyy} " +
              $"({info.DaysRemaining} día(s) restantes)";

        LicTokenExpiryText.Text = info.TokenExpiresAt is null
            ? "—"
            : info.TokenExpiresAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);

        LicFeaturesText.Text = info.Features.Count == 0 ? "—" : string.Join(", ", info.Features);
        LicFingerprintText.Text = MachineFingerprint.ToDisplayGroups(info.Fingerprint);

        if (info.State == LicenseState.Grace)
        {
            NoticeBar.Visibility = Visibility.Visible;
            NoticeText.Text =
                $"Trabajando sin conexión. Quedan {info.GraceDaysRemaining} día(s) antes de que " +
                "el programa necesite validar la licencia en línea.";
        }
        else if (info.Tier == LicenseTier.Trial)
        {
            NoticeBar.Visibility = Visibility.Visible;
            NoticeText.Text =
                $"Versión de prueba: {info.DaysRemaining ?? 0} día(s) restantes. " +
                "Si este es tu equipo, ejecuta 4-hazme-permanente.bat para dejarlo con " +
                "licencia interna permanente.";
        }
        else
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        AplicarModulos();
    }

    /// <summary>
    /// Habilita o deshabilita módulos según el tier.
    /// </summary>
    /// <remarks>
    /// Esta comprobación es por comodidad del usuario, NO es la medida de
    /// seguridad: ocultar un botón no impide nada. La validación real va también
    /// en el código que ejecuta la función.
    /// </remarks>
    private void AplicarModulos()
    {
        var puedeDibujar = _license.HasFeature("export-dxf");
        ExportButton.IsEnabled = puedeDibujar;
        AlzadosButton.IsEnabled = puedeDibujar;

        // La planta se dibuja con el MISMO permiso que las secciones y los alzados:
        // es generar dibujo. Dejarla habilitada en la version de prueba seria una
        // puerta abierta al modulo que se cobra.
        PlantaCadButton.IsEnabled = puedeDibujar;

        // Las zapatas, igual. El boton nacia apagado en el XAML porque el dibujante no
        // existia; ahora existe, asi que quien decide si esta encendido es la LICENCIA y no
        // el XAML. Sin esta linea, el unico boton de dibujo de la aplicacion que se puede
        // pulsar en la version de prueba seria este.
        DibujarZapatasButton.IsEnabled = puedeDibujar;

        // Y las corridas, por lo mismo.
        DibujarZapatasCorridasButton.IsEnabled = puedeDibujar;

        MostrarNotas(puedeDibujar
            ? "Cada sección se dibuja y se agrupa en un bloque con el nombre de su ID."
            : "La generación de dibujos no está incluida en la versión de prueba.");

        var puedeEtabs = _license.HasFeature("etabs");
        EtabsTab.IsEnabled = puedeEtabs;
        if (!puedeEtabs)
        {
            EtabsStatusText.Text = "El módulo de ETABS no está incluido en tu licencia.";
        }
    }

    /// <summary>
    /// Pone las notas del último dibujo, y deja el panel <b>plegado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las notas vivían encima de la vista previa, en una capa semitransparente pegada al
    /// borde de abajo, y ahí <b>tapaban el dibujo</b> justo donde va el rótulo de la sección
    /// y la cota de la base. Ahora van debajo, en su propio renglón y dentro de un
    /// <c>Expander</c>: si no hay nada que decir no ocupan ni un píxel —la visibilidad la
    /// manda el propio texto, con un disparador en el XAML— y si hay algo se ve una línea que
    /// se abre al tocarla.
    /// </para>
    /// <para>
    /// <b>Y se pliega en cada dibujo</b>, no solo al arrancar. Si el usuario lo dejó abierto
    /// para leer las notas de un dibujo, el siguiente no tiene por qué heredar el panel
    /// abierto tapando media pestaña: las notas nuevas se anuncian con la línea de la
    /// cabecera, que es donde se enteró la primera vez.
    /// </para>
    /// <para>
    /// Los cuatro sitios que escriben notas pasan por aquí, que es el motivo de que exista:
    /// con la asignación repetida cuatro veces, plegar el panel había que acordarse de
    /// hacerlo en los cuatro.
    /// </para>
    /// </remarks>
    private void MostrarNotas(string texto)
    {
        ExportHintText.Text = texto;
        NotasPanel.IsExpanded = false;
    }

    private async void OnRevalidate(object sender, RoutedEventArgs e)
    {
        LicMessageText.Text = "Contactando al servidor de licencias…";
        var info = await _licenseService.EvaluateAsync().ConfigureAwait(true);
        AplicarLicencia(info);
        LicMessageText.Text = info.IsUsable
            ? "Licencia revalidada correctamente."
            : "No se pudo revalidar: " + info.Message;
    }

    private void OnCopyFingerprint(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_licenseService.Fingerprint);
            LicMessageText.Text = "Huella copiada al portapapeles.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            LicMessageText.Text = "No se pudo acceder al portapapeles. Copia el texto con Ctrl+C.";
        }
    }

    private void OnDeactivate(object sender, RoutedEventArgs e)
    {
        var confirmar = MessageBox.Show(
            "Se borrará la licencia guardada en este equipo y el programa se cerrará.\n\n" +
            "¿Continuar?",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
        {
            return;
        }

        _licenseService.Deactivate();
        MessageBox.Show("Licencia liberada. El programa se cerrará.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
        Application.Current.Shutdown();
    }

    // ======================================================================
    // Proyecto
    // ======================================================================

    // El importador de Excel SE RETIRO. Ofrecia un boton en la barra, otro en la hoja
    // Proyecto y una entrada de menu, y las tres terminaban en el mismo aviso de «no
    // esta implementado». Un boton que solo sirve para decir que no funciona estorba
    // mas de lo que ayuda: ocupa sitio en la barra y hace dudar de si el problema es
    // del programa o de la hoja de calculo.
    //
    // Cuando se porte de verdad, lo que hace falta esta escrito en
    // docs/macro-secciones-concreto.md seccion 1: leer la hoja «Secciones
    // Estructurales Concreto» con ClosedXML, columnas A a V mas AC, y llenar
    // _datos.SeccionesConcreto.

    private void OnLoadSample(object sender, RoutedEventArgs e)
    {
        _datos = DatosProyecto.CrearEjemplo();
        Enlazar();

        // El historial se olvida: deshacer aquí devolvería al trabajo de antes del ejemplo,
        // que no es «el último cambio» sino otro trabajo.
        OlvidarHistorial();

        StatusText.Text = "Ejemplo cargado.";
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        var confirmar = MessageBox.Show("Se borrarán todos los datos capturados. ¿Continuar?",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
        {
            return;
        }

        _datos = new DatosProyecto();
        Enlazar();
        OlvidarHistorial();
        StatusText.Text = "Datos borrados.";
    }

    // ======================================================================
    // Barra de arriba: nuevo, salir, acerca de
    // ======================================================================

    /// <summary>
    /// Empieza un trabajo en blanco: secciones, juego de planos y solapa.
    /// </summary>
    /// <remarks>
    /// <b>Nuevo no es lo mismo que «Limpiar todo».</b> Limpiar borra la tabla de
    /// secciones y deja el resto como estaba; Nuevo deja la ventana como recién
    /// abierta, incluido el juego de planos, la solapa y —esto es lo importante— la
    /// ruta del archivo. Si no se olvidara la ruta, el primer Ctrl+G del trabajo
    /// nuevo sobreescribiría en silencio el .clk anterior.
    /// </remarks>
    private void OnNuevoTrabajo(object sender, ExecutedRoutedEventArgs e)
    {
        var confirmar = MessageBox.Show(
            "Se empezará un trabajo en blanco y se perderá lo que no esté guardado.\n\n" +
            "¿Continuar?",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
        {
            return;
        }

        var estaba = _listo;
        _listo = false;

        try
        {
            _datos = new DatosProyecto();
            _juego.Planos.Clear();

            _juego.Solapa.Calculista = string.Empty;
            _juego.Solapa.Propietario = string.Empty;
            _juego.Solapa.Ubicacion = string.Empty;
            _juego.Solapa.Obra = string.Empty;
            _juego.Solapa.Dibujo = string.Empty;
            _juego.Solapa.Fecha = DateTime.Today;

            CalculistaBox.Text = string.Empty;
            PropietarioBox.Text = string.Empty;
            UbicacionBox.Text = string.Empty;
            ObraBox.Text = string.Empty;
            DibujoBox.Text = string.Empty;
            FechaPicker.SelectedDate = DateTime.Today;
            RefrescarFecha();

            _modeloEtabs = null;
            _vista.Modelo = null;
            _vista.Reiniciar();
            NivelPlantaCombo.Items.Clear();

            // La ruta se suelta A PROPOSITO: ver el comentario de arriba.
            _archivoActual = string.Empty;
            ArchivoText.Text = "Trabajo nuevo, sin guardar";

            Enlazar();
        }
        finally
        {
            _listo = estaba;
        }

        ResumenPlanos();
        PlantasResumenText.Text = string.Empty;
        RedibujarVistas();
        OlvidarHistorial();
        StatusText.Text = "Trabajo nuevo.";
    }

    private void OnSalir(object sender, RoutedEventArgs e) => Close();

    private void OnAcercaDe(object sender, RoutedEventArgs e)
    {
        var empresa = string.IsNullOrWhiteSpace(AppInfo.CompanyName)
            ? string.Empty
            : Environment.NewLine + AppInfo.CompanyName;

        MessageBox.Show(
            $"{AppInfo.ProductName} v{AppInfo.Version}{empresa}" +
            Environment.NewLine + Environment.NewLine +
            _license.StatusLine +
            Environment.NewLine + Environment.NewLine +
            "Soporte: " + AppInfo.SupportEmail,
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ======================================================================
    // ETABS
    // ======================================================================

    private void OnTestEtabs(object sender, RoutedEventArgs e)
    {
        try
        {
            Cursor = Cursors.Wait;

            using var cx = new EtabsConnection { Destino = DestinoCsi };
            cx.Conectar();

            EtabsStatusText.Text =
                "Conexión correcta.\n\n" +
                $"Programa : {cx.Programa}\n" +
                $"Modelo   : {cx.Modelo}\n\n" +
                $"Ya puedes pulsar 'Leer modelo de {cx.NombreDelDestino}'.";

            // El nombre sale de la CONEXION y no escrito a mano: la casilla pudo decir
            // SAP2000, y un mensaje que diga ETABS en ese caso es un error a la vista.
            StatusText.Text = $"{cx.NombreDelDestino} conectado.";
        }
        catch (EtabsException ex)
        {
            EtabsStatusText.Text = "No se pudo conectar.\n\n" + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Lee las etiquetas de pier de los muros. Es una lectura aparte, a petición.
    /// </summary>
    /// <remarks>
    /// No se hace junto con el modelo a propósito: recorrer todos los paños de muro
    /// preguntando su pier tarda, y en un modelo sin piers asignados no aporta nada.
    /// </remarks>
    private void OnLeerPiers(object sender, RoutedEventArgs e)
    {
        try
        {
            Cursor = Cursors.Wait;
            EtabsStatusText.Text = "Leyendo los piers de los muros…";

            using var cx = new EtabsConnection { Destino = DestinoCsi };
            cx.Conectar();

            var piers = EtabsPiers.Leer(cx);

            PiersGrid.ItemsSource = piers.Piers;

            // La tabla aparece solo cuando hay algo que enseñar. Una tabla vacía
            // encima de la de elementos solo estorba.
            var hay = piers.Piers.Count > 0;
            PiersGrid.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
            PiersTitulo.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;

            EtabsStatusText.Text = piers.Resumen();
            StatusText.Text = hay
                ? $"Piers leídos: {piers.Etiquetas.Count} etiqueta(s), " +
                  $"{piers.Piers.Count} renglón(es)."
                : "No se encontró ningún pier asignado en el modelo.";
        }
        catch (EtabsException ex)
        {
            EtabsStatusText.Text = "No se pudieron leer los piers.\n\n" + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// <b>De dónde se lee</b>: lo que diga la casilla de la pestaña, ETABS o SAP2000.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está en <b>un solo sitio</b> a propósito: lo usan la prueba de conexión, la
    /// lectura del modelo, la de los piers y el armado de los planos, y así no hay
    /// manera de que un botón se quede hablándole a ETABS cuando la casilla dice
    /// SAP2000.
    /// </para>
    /// <para>
    /// El lector es el mismo para los dos, y no es un atajo: CSI comparte la OAPI entre
    /// ETABS y SAP2000 —la misma interfaz <c>cOAPI</c>, el mismo <c>SapModel</c> y las
    /// mismas llamadas para pisos, marcos y áreas— así que lo único que cambia es el
    /// ProgID con el que se pide el objeto activo y la librería que se carga. Ver
    /// <c>EtabsConnection.ProgramaCsi</c>.
    /// </para>
    /// <para>
    /// Se lee con <c>?.</c> porque el constructor de la ventana llama a rutinas que
    /// preguntan por el destino <b>antes</b> de que el XAML haya creado la casilla; sin
    /// casilla todavía, el destino es ETABS.
    /// </para>
    /// </remarks>
    private EtabsConnection.ProgramaCsi DestinoCsi =>
        ProgramaCsiCombo?.SelectedIndex == 1
            ? EtabsConnection.ProgramaCsi.Sap2000
            : EtabsConnection.ProgramaCsi.Etabs;

    /// <summary>Nombre del programa elegido, para los mensajes.</summary>
    private string NombreDestinoCsi =>
        DestinoCsi == EtabsConnection.ProgramaCsi.Sap2000 ? "SAP2000" : "ETABS";

    /// <summary>
    /// Al cambiar la casilla, el botón de leer dice a quién se le va a leer.
    /// </summary>
    /// <remarks>
    /// Es la única forma de que se vea que la casilla surtió efecto: si el botón siguiera
    /// diciendo «Leer modelo» a secas, no habría manera de saber a qué programa apunta
    /// sin pulsarlo.
    /// </remarks>
    private void OnProgramaCsiCambiado(object sender, SelectionChangedEventArgs e)
    {
        if (LeerModeloCsiButton is not null)
        {
            LeerModeloCsiButton.Content = $"Leer modelo de {NombreDestinoCsi}";
        }

        // El de la pestaña de planos, igual: la casilla de allá es la MISMA de aquí
        // —van atadas por el XAML—, así que el botón de leer plantas también dice a
        // quién le va a leer.
        if (LeerPlantasButton is not null)
        {
            LeerPlantasButton.Content = $"Leer plantas de {NombreDestinoCsi}";
        }

        if (LeerSeccionesModeloButton is not null)
        {
            LeerSeccionesModeloButton.Content = $"Leer secciones de {NombreDestinoCsi}";
        }

        // ==============================================================================
        //  Y LA TABLA NO SE QUEDA CON LOS DATOS DEL OTRO PROGRAMA.
        //  Se pidió esto: con la tabla llena de ETABS y la casilla en SAP2000, lo que se
        //  estaba viendo era del programa que NO decía la casilla, y no había forma de
        //  saberlo. Ahora la tabla se vacía en cuanto la casilla deja de coincidir con el
        //  modelo que está en memoria, y el aviso dice qué hay que pulsar.
        // ==============================================================================
        SincronizarSeccionesConLaCasilla();
    }

    /// <summary>
    /// Vacía la tabla de secciones si el modelo que hay en memoria es del otro programa.
    /// </summary>
    private void SincronizarSeccionesConLaCasilla()
    {
        if (SeccionesModeloGrid is null || SeccionesModeloResumenText is null)
        {
            return;
        }

        if (_modeloEtabs is not null && _destinoLeido == DestinoCsi)
        {
            return;
        }

        SeccionesModeloGrid.ItemsSource = null;
        SeccionesModeloResumenText.Text =
            $"Pulsa «Leer secciones de {NombreDestinoCsi}»: " +
            (_modeloEtabs is null
                ? "todavía no hay ningún modelo leído."
                : $"lo que había era del modelo de " +
                  $"{(_destinoLeido == EtabsConnection.ProgramaCsi.Sap2000 ? "SAP2000" : "ETABS")}.");
    }

    /// <summary>Lee el modelo del programa que diga la casilla.</summary>
    private void OnImportModeloCsi(object sender, RoutedEventArgs e) =>
        LeerModeloCsi(DestinoCsi);

    /// <summary>
    /// Lee el modelo abierto en el programa de CSI que se le diga, y lo <b>visualiza</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está en un solo sitio para los dos programas porque el cuerpo era idéntico salvo
    /// el destino: duplicarlo garantizaba que un arreglo entrara en uno y no en el otro.
    /// </para>
    /// <para>
    /// La visualización no hay que añadirla: al dejar el modelo en <c>_vista.Modelo</c> y
    /// llamar a <c>RedibujarVistas</c>, el visor 3D, la vista extruida y la planta se
    /// pintan igual que con ETABS, porque todos trabajan sobre el mismo
    /// <c>ModeloEtabs</c>.
    /// </para>
    /// </remarks>
    private void LeerModeloCsi(EtabsConnection.ProgramaCsi destino)
    {
        try
        {
            Cursor = Cursors.Wait;

            using var cx = new EtabsConnection { Destino = destino };

            EtabsStatusText.Text = $"Leyendo el modelo de {cx.NombreDelDestino}…";

            cx.Conectar();

            var modelo = EtabsReader.Leer(cx);
            _modeloEtabs = modelo;
            _destinoLeido = destino;

            EtabsGrid.ItemsSource = modelo.Elementos;
            EtabsStatusText.Text = modelo.Resumen();
            StatusText.Text =
                $"Modelo de {cx.NombreDelDestino} leído: {modelo.Elementos.Count} " +
                $"elementos en {modelo.Niveles.Count} nivel(es).";

            // El visor se alimenta del mismo modelo que la cuadrícula
            _vista.Modelo = modelo;
            _vista.Reiniciar();
            PoblarNiveles(modelo);
            RedibujarVistas();

            // Y la tabla de secciones del modelo, que sale del mismo modelo: así está
            // puesta sin que haya que volver a leer nada.
            LlenarSeccionesModelo(modelo);
        }
        catch (EtabsException ex)
        {
            EtabsStatusText.Text = "No se pudo leer el modelo.\n\n" + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ======================================================================
    // SECCIONES DEL MODELO: la hoja SECCIONES de la macro
    // ======================================================================

    /// <summary>
    /// Llena la tabla de secciones usadas en el modelo.
    /// </summary>
    /// <remarks>
    /// La tabla la arma <c>SeccionesModelo.Construir</c>, que es el port de
    /// <c>VolcarSecciones</c>: mismos tipos, mismo orden y mismas columnas. Aquí solo se
    /// pone en la cuadrícula y se cuenta lo que salió.
    /// </remarks>
    private void LlenarSeccionesModelo(ModeloEtabs modelo)
    {
        var filas = SeccionesModelo.Construir(modelo);
        SeccionesModeloGrid.ItemsSource = filas;

        var tipos = filas.Select(f => f.Tipo).Distinct().Count();
        SeccionesModeloResumenText.Text =
            $"{filas.Count} sección(es) distinta(s) en {tipos} tipo(s) de elemento, " +
            $"de {modelo.Elementos.Count} elementos del modelo.";
    }

    /// <summary>Lee el modelo y arma la tabla de secciones.</summary>
    /// <remarks>
    /// Si el modelo ya se leyó, no se vuelve a leer: se arma la tabla con el que hay. Leer
    /// otra vez tarda y no cambia nada mientras no se toque el modelo en ETABS.
    /// </remarks>
    private void OnLeerSeccionesModelo(object sender, RoutedEventArgs e)
    {
        // Se reaprovecha el modelo en memoria SOLO si es del programa que dice la casilla.
        // Si la casilla cambió, hay que leer otra vez: son dos modelos distintos.
        if (_modeloEtabs is not null && _modeloEtabs.Elementos.Count > 0 &&
            _destinoLeido == DestinoCsi)
        {
            LlenarSeccionesModelo(_modeloEtabs);
            StatusText.Text =
                $"Tabla de secciones armada con el modelo de {NombreDestinoCsi} que ya " +
                "estaba leído.";
            return;
        }

        LeerModeloCsi(DestinoCsi);
    }

    /// <summary>
    /// Copia la tabla al portapapeles con tabuladores, para pegarla en Excel.
    /// </summary>
    /// <remarks>
    /// Con tabuladores y no con comas a propósito: los nombres de sección y la lista de
    /// niveles llevan comas dentro, y pegado como CSV se partiría en columnas que no son.
    /// </remarks>
    private void OnCopiarSeccionesModelo(object sender, RoutedEventArgs e)
    {
        if (SeccionesModeloGrid.ItemsSource is not IEnumerable<SeccionesModelo.Fila> filas)
        {
            StatusText.Text = "Todavía no hay tabla de secciones: lee el modelo primero.";
            return;
        }

        var s = new System.Text.StringBuilder();
        s.AppendLine("TIPO\tSECCION DE ETABS\tFORMA\tMATERIAL\tT3 PERALTE (cm)\t" +
                     "T2 ANCHO / ESPESOR (cm)\tTF (cm)\tTW (cm)\tCANTIDAD\tNIVELES");

        foreach (var f in filas)
        {
            s.AppendLine(string.Join('\t',
                f.Tipo, f.Seccion, f.Forma, f.Material,
                Num(f.PeralteCm), Num(f.AnchoCm), Num(f.PatinCm), Num(f.AlmaCm),
                f.Cantidad.ToString(), f.Niveles));
        }

        try
        {
            Clipboard.SetText(s.ToString());
            StatusText.Text = "Tabla de secciones copiada: pégala en Excel.";
        }
        catch (Exception ex)
        {
            // El portapapeles lo puede tener tomado otro programa; no es para tirar la app.
            StatusText.Text = "No se pudo copiar al portapapeles: " + ex.Message;
        }

        static string Num(double? v) =>
            v is null ? string.Empty : v.Value.ToString("0.##");
    }

    /// <summary>
    /// Dibuja los alzados de todas las filas en AutoCAD.
    /// </summary>
    /// <remarks>
    /// Va en la misma pestaña que las secciones porque se alimenta de la misma
    /// tabla: la macro de alzados lee las mismas columnas A–V más la W.
    /// </remarks>
    private void OnExportAlzados(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Revisar(out var problemas))
        {
            MessageBox.Show(
                "Corrige esto antes de generar los alzados:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            var escala = LeerEscala();

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new AlzadoDrawer(doc, escala)
            {
                EscalaHatch = LeerEscalaHatch(),

                // Los bloques y los alzados van 2 m por ENCIMA de la seccion mas
                // alta, no en la cota fija Y=2 de la macro. Con una contratrabe o un
                // muro altos, la cota fija dejaba la seccion invadiendo la fila de
                // alzados.
                AltoMaximoSeccion = AltoMaximoDeLasSecciones(escala)
            };

            // Capas de varilla, estilos de texto y de cota: los mismos de la sección
            var secciones = new SeccionDrawer(doc, escala);
            secciones.AsegurarCapas(ClavesDeVarillaUsadas());

            // Las llamadas de las varillas NO viajan dentro del bloque de la sección:
            // Bloquear deja fuera las capas COTAS y ROTULOS a propósito, así que el
            // corte que se inserta junto al alzado llegaba sin ellas. Se rehacen aquí,
            // cuando el alzado avisa de dónde dejó el bloque.
            dibujante.TrasInsertarSeccion = (id, xs, ys) =>
            {
                var fila = _datos.SeccionesConcreto.FirstOrDefault(
                    f => string.Equals((f.Id ?? string.Empty).Trim(), id,
                        StringComparison.OrdinalIgnoreCase));

                if (fila is not null)
                {
                    secciones.LlamadasJuntoAlBloque(AFormatoCad(fila), xs, ys);
                }
            };

            // Y la capa ALZADOS, que solo usa el alzado
            dibujante.AsegurarCapas();

            var x = 0d;
            var dibujados = 0;
            var omitidos = new List<string>();

            foreach (var r in _datos.SeccionesConcreto)
            {
                // Solo trabes, contratrabes, columnas y dados llevan alzado.
                if (TipoDe(r.Elemento, r.Id) is null)
                {
                    omitidos.Add($"{r.Elemento} \"{r.Id}\"");
                    continue;
                }

                var a = AFormatoAlzado(r);

                // DibujarElemento coloca la SECCION al costado del alzado y devuelve
                // la X del elemento siguiente. El avance no se calcula aquí a
                // propósito: depende del tipo de elemento y son cinco constantes de
                // la macro. Vive en AlzadoLayout, comprobado contra el VBA.
                var siguiente = dibujante.DibujarElemento(a, x);

                if (siguiente > x)
                {
                    dibujados++;
                    x = siguiente;
                }
            }

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var fallos = dibujante.Fallos;

            StatusText.Text = $"Dibujados {dibujados} alzado(s) en AutoCAD.";

            // Los omitidos se dicen, no se callan: si alguien esperaba el alzado de
            // un castillo, tiene que enterarse de por qué no salió.
            var nota = omitidos.Count == 0
                ? string.Empty
                : $"\n\nSin alzado ({omitidos.Count}), porque solo lo llevan trabes, " +
                  "contratrabes, columnas y dados:\n  " + string.Join("\n  ", omitidos);

            if (fallos.Count == 0)
            {
                MessageBox.Show(
                    $"Listo.\n\n{dibujados} alzado(s) dibujados.\n\n" +
                    "Cada alzado quedó en su propio bloque, y las cotas por fuera." + nota,
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var detalle = string.Join(Environment.NewLine, fallos.Select(f => "  - " + f));

                MostrarNotas(
                    "AVISOS DEL ULTIMO ALZADO (" + fallos.Count + "):" +
                    Environment.NewLine + detalle);

                MessageBox.Show(
                    $"{dibujados} alzado(s) dibujados, pero hubo {fallos.Count} fallo(s) " +
                    "que se toleraron:\n\n" + detalle,
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudieron generar los alzados:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ======================================================================
    // Visor del modelo: 3D y planta
    // ======================================================================

    /// <summary>Llena la lista de niveles, con una opción para ver todos.</summary>
    private void PoblarNiveles(ModeloEtabs modelo)
    {
        NivelPlantaCombo.Items.Clear();
        NivelPlantaCombo.Items.Add("(todos los niveles)");

        // LOS NIVELES QUE TIENEN ELEMENTOS, de arriba abajo: la BASE incluida.
        // GetStories no devuelve el nivel base, así que con la lista de la API a secas la
        // planta de cimentación no aparecía ni en esta lista ni en el dibujo, aunque el
        // modelo tenga ahí las cadenas de desplante. NivelesConElementos los saca de los
        // propios elementos, como StoriesDesdeElementos de la macro.
        //
        // Aquí van del más alto al más bajo porque es una lista para elegir a mano y lo que
        // se suele mirar es el nivel de arriba; en el DIBUJO van al revés, ascendente, que
        // es el ORDEN_NIVELES de la hoja.
        var nombres = modelo.NivelesConElementos(ascendente: false)
            .Select(n => n.Nombre)
            .ToList();

        foreach (var n in nombres)
        {
            NivelPlantaCombo.Items.Add(n);
        }

        // Arranca en el nivel más alto, que suele ser el de interés
        NivelPlantaCombo.SelectedIndex = nombres.Count > 0 ? 1 : 0;
    }

    /// <summary>Nivel elegido, o <c>null</c> cuando están seleccionados todos.</summary>
    private string? NivelElegido =>
        NivelPlantaCombo.SelectedIndex <= 0
            ? null
            : NivelPlantaCombo.SelectedItem?.ToString();

    private void RedibujarVistas()
    {
        if (!_listo)
        {
            return;
        }

        _vista.VerColumnas = VerColumnasChk.IsChecked == true;
        _vista.VerTrabes = VerTrabesChk.IsChecked == true;
        _vista.VerDiagonales = VerDiagonalesChk.IsChecked == true;
        _vista.VerMuros = VerMurosChk.IsChecked == true;
        _vista.VerLosas = VerLosasChk.IsChecked == true;

        _vista.Dibujar3D(Vista3DCanvas);
        _vista.DibujarExtruido(ExtruidaCanvas);

        // La planta va aparte: vive en el modulo de planos y usa sus propias
        // casillas, asi que se dibuja con DibujarPlanta y no aqui.
        DibujarPlanta();
    }

    private void OnFiltroVistaCambiado(object sender, RoutedEventArgs e) => RedibujarVistas();

    /// <summary>
    /// Filtros de la planta, que ahora vive en su propio módulo.
    /// </summary>
    /// <remarks>
    /// La planta tiene sus <b>propias</b> casillas y no las del visor de ETABS: son
    /// dos pestañas distintas y compartirlas obligaría al usuario a ir a la otra para
    /// cambiar lo que está viendo aquí. Las diagonales no aparecen porque en planta se
    /// proyectan como una línea suelta que no dice nada.
    /// </remarks>
    private void OnFiltroPlanoCambiado(object sender, RoutedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        DibujarPlanta();
    }

    // ======================================================================
    // Guardar y abrir el trabajo (.clk)
    // ======================================================================

    /// <summary>Archivo abierto, para que «Guardar» no vuelva a preguntar.</summary>
    private string _archivoActual = string.Empty;

    // Los tres de abajo se disparan desde la barra de arriba, el menu Archivo y el
    // teclado. Por eso van como Executed de un ApplicationCommand y no como Click:
    // asi el atajo, el menu y el boton comparten UNA sola ruta de codigo, y no hay
    // forma de que uno guarde y otro no. Su firma pide ExecutedRoutedEventArgs.
    private void OnGuardarTrabajo(object sender, ExecutedRoutedEventArgs e) => Guardar();

    private void OnGuardarComo(object sender, ExecutedRoutedEventArgs e) => GuardarComo();

    private void OnAbrirTrabajo(object sender, ExecutedRoutedEventArgs e) => AbrirTrabajo();

    private void Guardar()
    {
        if (string.IsNullOrWhiteSpace(_archivoActual))
        {
            GuardarComo();
            return;
        }

        GuardarEn(_archivoActual);
    }

    private void GuardarComo()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar el trabajo",
            Filter = ArchivoProyecto.Filtro,
            DefaultExt = ArchivoProyecto.Extension,

            // Se propone el nombre de la obra: es lo que el usuario reconoce.
            FileName = string.IsNullOrWhiteSpace(_juego.Solapa.Obra)
                ? "trabajo" + ArchivoProyecto.Extension
                : _juego.Solapa.Obra + ArchivoProyecto.Extension
        };

        if (dialogo.ShowDialog(this) == true)
        {
            GuardarEn(dialogo.FileName);
        }
    }

    private void GuardarEn(string ruta)
    {
        try
        {
            ArchivoProyecto.Guardar(ruta, ArmarProyecto());

            _archivoActual = ruta;
            ArchivoText.Text = "Guardado: " + Path.GetFileName(ruta);
            StatusText.Text = $"Trabajo guardado en {Path.GetFileName(ruta)}.";
        }
        catch (Exception ex)
        {
            // Se dice la RUTA y el motivo: "no se pudo guardar" no deja hacer nada.
            MessageBox.Show(
                $"No se pudo guardar en:\n{ruta}\n\n{ex.Message}",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AbrirTrabajo()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Abrir un trabajo",
            Filter = ArchivoProyecto.Filtro,
            DefaultExt = ArchivoProyecto.Extension
        };

        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            AplicarProyecto(ArchivoProyecto.Leer(dialogo.FileName));

            // Se abrió OTRO trabajo: lo de antes ya no es «el último cambio».
            OlvidarHistorial();

            _archivoActual = dialogo.FileName;
            ArchivoText.Text = "Abierto: " + Path.GetFileName(dialogo.FileName);
            StatusText.Text = $"Trabajo abierto: {Path.GetFileName(dialogo.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo abrir el trabajo.\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Recoge de la interfaz todo lo que hay que guardar.</summary>
    private ProyectoGuardado ArmarProyecto()
    {
        var p = new ProyectoGuardado
        {
            Aplicacion = AppInfo.ProductName,
            Calculista = _juego.Solapa.Calculista,
            Propietario = _juego.Solapa.Propietario,
            Ubicacion = _juego.Solapa.Ubicacion,
            Obra = _juego.Solapa.Obra,
            Dibujo = _juego.Solapa.Dibujo,
            Fecha = _juego.Solapa.Fecha,
            Escala = _juego.Solapa.Escala,
            Acotacion = _juego.Solapa.Acotacion,
            EscalaDibujo = LeerEscala(),
            EscalaHatch = LeerEscalaHatch(),
            ModoSeccion = (int)ModoElegido,

            // El doblez del gancho de las zapatas es del juego, igual que el modo de sección: se
            // guarda con él y no con cada zapata.
            GanchoZapatasDiametros = FactorGanchoElegido
        };

        foreach (var pl in _juego.Planos)
        {
            p.Planos.Add(new PlanoGuardado
            {
                Clave = pl.Clave, Contiene = pl.Contiene, Escala = pl.Escala
            });
        }

        foreach (var s in _datos.SeccionesConcreto)
        {
            p.Secciones.Add(new SeccionGuardada
            {
                Elemento = s.Elemento, Id = s.Id,
                BaseCm = s.BaseCm, AlturaCm = s.AlturaCm,
                NEsqSup = s.NEsqSup, DiamEsqSup = s.DiamEsqSup,
                NIntSup = s.NIntSup, DiamIntSup = s.DiamIntSup,
                NEsqInf = s.NEsqInf, DiamEsqInf = s.DiamEsqInf,
                NIntInf = s.NIntInf, DiamIntInf = s.DiamIntInf,
                NInter = s.NInter, DiamInter = s.DiamInter,
                Circular = s.Circular,
                NVarTotal = s.NVarTotal, DiamVarTotal = s.DiamVarTotal,
                ZunchoHelicoidal = s.ZunchoHelicoidal,
                RecubrimientoCm = s.RecubrimientoCm,
                Estribo = s.Estribo, SeparacionCm = s.SeparacionCm,
                EstriboDiamante = s.EstriboDiamante,
                DiamEstriboDiamante = s.DiamEstriboDiamante,
                GanchoCm = s.GanchoCm, Fc = s.Fc, Escala = s.Escala,
                LongitudM = s.LongitudM
            });
        }

        // LAS OTRAS DOS HOJAS. Antes no se guardaban: «guardar trabajo» escribía solo el
        // concreto y el acero y las zapatas se perdían. Van como filas genéricas -pares de
        // nombre y valor- para que una columna nueva se guarde sola el día que se agregue,
        // que es exactamente lo que no pasó cuando llegaron estas dos hojas.
        foreach (var a in _datos.SeccionesAcero)
        {
            p.Acero.Add(FilaSerializable.Leer(a));
        }

        foreach (var z in _datos.ZapatasAisladas)
        {
            p.Zapatas.Add(FilaSerializable.Leer(z));
        }

        foreach (var z in _datos.ZapatasCorridas)
        {
            p.ZapatasCorridas.Add(FilaSerializable.Leer(z));
        }

        return p;
    }

    /// <summary>Vuelca un proyecto leído en la interfaz.</summary>
    /// <remarks>
    /// Se apaga <c>_listo</c> mientras se cargan los datos y se vuelve a encender al
    /// final. Sin eso, cada renglón que entra dispara la vista previa y el recuento, y
    /// abrir un trabajo de cien secciones redibuja cien veces para nada.
    /// </remarks>
    private void AplicarProyecto(ProyectoGuardado p)
    {
        var estaba = _listo;
        _listo = false;

        try
        {
            _juego.Solapa.Calculista = p.Calculista;
            _juego.Solapa.Propietario = p.Propietario;
            _juego.Solapa.Ubicacion = p.Ubicacion;
            _juego.Solapa.Obra = p.Obra;
            _juego.Solapa.Dibujo = p.Dibujo;
            _juego.Solapa.Fecha = p.Fecha;
            _juego.Solapa.Escala = p.Escala;
            _juego.Solapa.Acotacion = p.Acotacion;

            CalculistaBox.Text = p.Calculista;
            PropietarioBox.Text = p.Propietario;
            UbicacionBox.Text = p.Ubicacion;
            ObraBox.Text = p.Obra;
            DibujoBox.Text = p.Dibujo;
            FechaPicker.SelectedDate = p.Fecha;
            RefrescarFecha();
            EscalaSolapaBox.Text = p.Escala;

            HatchScaleBox.Text = p.EscalaHatch.ToString(
                "0.######", CultureInfo.InvariantCulture);

            // El doblez del gancho de las zapatas. Un .clk de antes de esta casilla trae el 15 por
            // omisión, que es con el que se dibujó cuando se guardó.
            ZapGanchoDiametrosBox.Text = TrazoZapata
                .FactorGanchoValido(p.GanchoZapatasDiametros)
                .ToString("0.#", CultureInfo.InvariantCulture);

            // El rótulo de la hoja de corridas lee esa misma casilla, y aquí estamos con
            // _listo en false, así que su TextChanged no va a saltar: se pone al día a mano.
            ActualizarGanchoDeCorridas();

            _juego.Planos.Clear();

            foreach (var pl in p.Planos)
            {
                _juego.Agregar(pl.Contiene, pl.Clave).Escala = pl.Escala;
            }

            _datos.SeccionesConcreto.Clear();

            foreach (var s in p.Secciones)
            {
                _datos.SeccionesConcreto.Add(new SeccionConcretoRow
                {
                    Elemento = s.Elemento, Id = s.Id,
                    BaseCm = s.BaseCm, AlturaCm = s.AlturaCm,
                    NEsqSup = s.NEsqSup, DiamEsqSup = s.DiamEsqSup,
                    NIntSup = s.NIntSup, DiamIntSup = s.DiamIntSup,
                    NEsqInf = s.NEsqInf, DiamEsqInf = s.DiamEsqInf,
                    NIntInf = s.NIntInf, DiamIntInf = s.DiamIntInf,
                    NInter = s.NInter, DiamInter = s.DiamInter,
                    Circular = s.Circular,
                    NVarTotal = s.NVarTotal, DiamVarTotal = s.DiamVarTotal,
                    ZunchoHelicoidal = s.ZunchoHelicoidal,
                    RecubrimientoCm = s.RecubrimientoCm,
                    Estribo = s.Estribo, SeparacionCm = s.SeparacionCm,
                    EstriboDiamante = s.EstriboDiamante,
                    DiamEstriboDiamante = s.DiamEstriboDiamante,
                    GanchoCm = s.GanchoCm,

                    // El f'c va DESPUES del elemento en el inicializador: al
                    // escribirlo se marca como puesto a mano, y asi el valor
                    // guardado no lo pisa el automatico del elemento.
                    Fc = s.Fc,
                    Escala = s.Escala, LongitudM = s.LongitudM
                });
            }

            // ---- Secciones Acero ----
            _datos.SeccionesAcero.Clear();

            foreach (var fila in p.Acero)
            {
                var nueva = new PerfilAceroRow();
                FilaSerializable.Aplicar(nueva, fila);
                _datos.SeccionesAcero.Add(nueva);
            }

            // ---- Zapatas Aisladas ----
            _datos.ZapatasAisladas.Clear();

            foreach (var fila in p.Zapatas)
            {
                var nueva = new ZapataAisladaRow();
                FilaSerializable.Aplicar(nueva, fila);
                _datos.ZapatasAisladas.Add(nueva);
            }

            // ---- Zapatas Corridas ----
            _datos.ZapatasCorridas.Clear();

            foreach (var fila in p.ZapatasCorridas)
            {
                var nueva = new ZapataCorridaRow();
                FilaSerializable.Aplicar(nueva, fila);
                _datos.ZapatasCorridas.Add(nueva);
            }
        }
        finally
        {
            _listo = estaba;
        }

        if (_datos.SeccionesConcreto.Count > 0)
        {
            SeccionesGrid.SelectedIndex = 0;
        }

        DatosCambiaron();
    }

    // ======================================================================
    // Solapa y juego de planos
    // ======================================================================

    private readonly JuegoDePlanos _juego = new();

    /// <summary>
    /// Enlaza la solapa y el juego de planos. Se llama una vez, al arrancar.
    /// </summary>
    /// <remarks>
    /// Los campos se enlazan a mano y no con <c>Binding</c> del XAML a propósito: así
    /// el modelo de la solapa no depende de que la ventana exista, y el día que estos
    /// datos haya que escribirlos en el cuadro de rótulos de AutoCAD se leen del
    /// modelo y no de los controles.
    /// </remarks>
    private void PrepararSolapa()
    {
        PlanosGrid.ItemsSource = _juego.Planos;

        // El resumen se actualiza con la lista, no al pulsar cada botón: así no hay
        // forma de cambiar el juego por un camino que se olvide de refrescarlo.
        _juego.Planos.CollectionChanged += (_, _) => ResumenPlanos();

        FechaPicker.SelectedDate = _juego.Solapa.Fecha;
        EscalaSolapaBox.Text = _juego.Solapa.Escala;

        CalculistaBox.TextChanged += (_, _) => _juego.Solapa.Calculista = CalculistaBox.Text;
        PropietarioBox.TextChanged += (_, _) => _juego.Solapa.Propietario = PropietarioBox.Text;
        UbicacionBox.TextChanged += (_, _) => _juego.Solapa.Ubicacion = UbicacionBox.Text;
        ObraBox.TextChanged += (_, _) => _juego.Solapa.Obra = ObraBox.Text;
        DibujoBox.TextChanged += (_, _) => _juego.Solapa.Dibujo = DibujoBox.Text;
        EscalaSolapaBox.TextChanged += (_, _) => _juego.Solapa.Escala = EscalaSolapaBox.Text;

        FechaPicker.SelectedDateChanged += (_, _) =>
        {
            if (FechaPicker.SelectedDate is not null)
            {
                _juego.Solapa.Fecha = FechaPicker.SelectedDate.Value;
            }

            RefrescarFecha();
        };

        RefrescarFecha();

        AcotacionCombo.SelectionChanged += (_, _) =>
        {
            if (AcotacionCombo.SelectedItem is ComboBoxItem it)
            {
                _juego.Solapa.Acotacion = it.Content?.ToString() ?? "cm";
            }
        };

        ResumenPlanos();
    }

    /// <summary>
    /// Pone al día el texto de la fecha: el mes y el año con letra.
    /// </summary>
    /// <remarks>
    /// Es lo que se rotula en la solapa, así que se muestra al lado del calendario
    /// para que se vea <b>lo que va a salir impreso</b> y no solo lo que se capturó.
    /// </remarks>
    private void RefrescarFecha() =>
        FechaTextoLabel.Text = _juego.Solapa.FechaTexto;

    private void ResumenPlanos()
    {
        var n = _juego.Planos.Count;

        PlanosResumenText.Text = n == 0
            ? "Sin planos todavía."
            : n == 1 ? "1 plano en el juego." : $"{n} planos en el juego.";
    }

    private void OnAgregarPlano(object sender, RoutedEventArgs e)
    {
        // El número y el total los pone el juego; aquí solo se agrega.
        var p = _juego.Agregar();
        PlanosGrid.SelectedItem = p;
        PlanosGrid.ScrollIntoView(p);
    }

    /// <summary>
    /// Quita del juego <b>todos</b> los planos seleccionados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes quitaba solo <c>SelectedItem</c>, o sea uno, aunque hubiera diez
    /// marcados. Con la cuadrícula ya en modo <c>Extended</c> se pueden marcar varios,
    /// así que el botón tiene que quitarlos todos o quedaría prometiendo algo que no
    /// hace.
    /// </para>
    /// <para>
    /// La lista de seleccionados se copia ANTES de empezar a borrar. Si se recorriera
    /// <c>SelectedItems</c> directamente, cada borrado la modificaría mientras se está
    /// recorriendo y saltaría una excepción o se quedarían filas sin quitar.
    /// </para>
    /// </remarks>
    private void OnQuitarPlano(object sender, RoutedEventArgs e)
    {
        var marcados = PlanosGrid.SelectedItems.OfType<PlanoRow>().ToList();

        if (marcados.Count == 0)
        {
            StatusText.Text = "Marca en la tabla los planos que quieres quitar.";
            return;
        }

        foreach (var p in marcados)
        {
            _juego.Planos.Remove(p);
        }

        StatusText.Text = marcados.Count == 1
            ? "Se quitó 1 plano del juego."
            : $"Se quitaron {marcados.Count} planos del juego.";
    }

    private void OnSubirPlano(object sender, RoutedEventArgs e) => MoverPlano(-1);

    private void OnBajarPlano(object sender, RoutedEventArgs e) => MoverPlano(+1);

    /// <summary>
    /// Sube o baja el plano seleccionado. Renumera solo, por la colección.
    /// </summary>
    private void MoverPlano(int paso)
    {
        if (PlanosGrid.SelectedItem is not PlanoRow p)
        {
            return;
        }

        var i = _juego.Planos.IndexOf(p);
        var j = i + paso;

        if (i < 0 || j < 0 || j >= _juego.Planos.Count)
        {
            return;
        }

        _juego.Planos.Move(i, j);

        // Move NO dispara Add ni Remove, así que la renumeración hay que pedirla:
        // sin esto, reordenar dejaba los números en su sitio anterior.
        _juego.Renumerar();

        PlanosGrid.SelectedItem = p;
    }

    /// <summary>
    /// Lee el modelo de ETABS y arma un plano por nivel.
    /// </summary>
    /// <remarks>
    /// Es el arranque del juego de planos: en un edificio, cada nivel es un plano de
    /// planta. Los planos se agregan al juego, así que la numeración sale puesta.
    /// </remarks>
    private void OnLeerPlantas(object sender, RoutedEventArgs e)
    {
        try
        {
            Cursor = Cursors.Wait;

            using var cx = new EtabsConnection { Destino = DestinoCsi };
            cx.Conectar();

            var modelo = EtabsReader.Leer(cx);
            _modeloEtabs = modelo;
            _destinoLeido = DestinoCsi;
            _vista.Modelo = modelo;
            _vista.Reiniciar();
            PoblarNiveles(modelo);
            DibujarPlanta();

            // Del mismo modelo sale la tabla de secciones, así que se llena de una vez.
            LlenarSeccionesModelo(modelo);

            // Un plano por nivel, del más alto al más bajo, que es el orden en que se
            // arma un juego de planos estructurales.
            var niveles = modelo.Niveles
                .OrderByDescending(n => n.ElevacionM)
                .ToList();

            var nuevos = 0;

            foreach (var n in niveles)
            {
                var contiene = $"PLANTA {n.Nombre}";

                // No se duplica lo que ya está: leer dos veces no debe dejar el juego
                // con cada planta repetida.
                if (_juego.Planos.Any(p =>
                        string.Equals(p.Contiene, contiene, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _juego.Agregar(contiene);
                nuevos++;
            }

            PlantasResumenText.Text =
                $"{modelo.Niveles.Count} nivel(es) leídos · {nuevos} plano(s) agregados al juego.";

            StatusText.Text = $"Plantas leídas: {modelo.Niveles.Count} nivel(es).";

            if (modelo.Niveles.Count == 0)
            {
                PlanoHintText.Text =
                    "ETABS no reportó niveles. Revisa la pestaña de ETABS: ahí queda el " +
                    "detalle de por qué falló cada miembro del modelo.";
            }
        }
        catch (EtabsException ex)
        {
            PlantasResumenText.Text = "No se pudieron leer las plantas.";
            PlanoHintText.Text = ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>Redibuja la planta con los filtros de su propia pestaña.</summary>
    private void DibujarPlanta()
    {
        _vista.VerColumnas = VerColumnasPlanoChk.IsChecked == true;
        _vista.VerTrabes = VerTrabesPlanoChk.IsChecked == true;
        _vista.VerMuros = VerMurosPlanoChk.IsChecked == true;
        _vista.VerLosas = VerLosasPlanoChk.IsChecked == true;
        _vista.VerDiagonales = false;

        _vista.DibujarPlanta(PlantaCanvas, NivelElegido);

        // Y se dejan como estaban, para que el visor 3D no herede estos filtros: es
        // el MISMO objeto de vista, así que sin restaurarlos, tocar una casilla aquí
        // cambiaría en silencio lo que se ve en la pestaña de ETABS.
        RestaurarFiltrosDelVisor();
    }

    // ======================================================================
    // Dibujar la planta en AutoCAD
    // ======================================================================

    /// <summary>
    /// Manda a AutoCAD la planta que se está viendo en esta pestaña.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo que se dibuja es <b>exactamente lo que se ve</b>: el nivel elegido en la
    /// lista y los tipos de elemento marcados en las casillas de esta pestaña, no las
    /// del visor de ETABS. Es la razón de que el botón viva aquí y no en la pestaña
    /// de AutoCAD: si estuviera allá, habría que ir y volver para saber qué se va a
    /// dibujar.
    /// </para>
    /// <para>
    /// El modelo se lee UNA vez, con «Leer plantas de ETABS», y este botón trabaja
    /// sobre lo ya leído. Así se puede dibujar nivel por nivel sin volver a
    /// interrogar a ETABS, que en un modelo grande es la parte lenta.
    /// </para>
    /// </remarks>
    private void OnDibujarPlantaCad(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_modeloEtabs is null)
        {
            MessageBox.Show(
                "Todavía no hay ningún modelo leído.\n\n" +
                "Pulsa primero «Leer plantas de ETABS», con ETABS abierto y el modelo " +
                "cargado.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // ==============================================================================
        //  DE UN JALÓN, TODAS LAS PLANTAS: es como se usa y como lo hace la macro.
        //  La casilla «Solo el nivel elegido» está para cuando se quiere una sola.
        // ==============================================================================
        var soloUna = SoloNivelElegidoChk?.IsChecked == true;

        var plantas = soloUna
            ? new List<PlantaCad> { ArmarPlanta(_modeloEtabs) }
            : ArmarTodasLasPlantas(_modeloEtabs);

        if (plantas.Sum(p => p.Elementos.Count) == 0)
        {
            MessageBox.Show(
                soloUna
                    ? "Con el nivel y los filtros actuales no queda ningún elemento que " +
                      "dibujar.\n\nRevisa la lista de niveles y las casillas de «Mostrar»."
                    : "Con los filtros actuales no queda ningún elemento que dibujar.\n\n" +
                      "Revisa las casillas de «Mostrar».",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new PlantaDrawer(doc);
            var r = dibujante.DibujarTodas(plantas);

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var fallos = dibujante.Fallos;

            var cuales = plantas.Count == 1
                ? $"del nivel {(string.IsNullOrWhiteSpace(plantas[0].Nivel) ? "(todos)" : plantas[0].Nivel)}"
                : $"en {plantas.Count} plantas ({string.Join(", ", plantas.Select(p => p.Nivel))})";

            StatusText.Text = $"Dibujado en AutoCAD: {r.Total} elemento(s) {cuales}.";

            PlanoHintText.Text =
                $"Última pasada: {r} en {plantas.Count} planta(s), de un jalón y repartidas " +
                "a la derecha. Quedaron en LAS CAPAS DE LA MACRO —E-CASTILLO, E-COLUMNA, " +
                "E-DALA, E-TRABE, E-CONTRATRABE, E-MURO, E-LOSA, E-ACERO, E-EJES, E-TEXTO " +
                "y E-TITULO— cada una con su color, en metros. Faltan los ejes con " +
                "burbujas, las cotas y el armado de losa: ese es el dibujante nuevo.";

            if (fallos.Count == 0)
            {
                MessageBox.Show(
                    $"Listo.\n\n{r}\n\n" +
                    "Cada tipo de elemento quedó en su propia capa, así que puedes " +
                    "apagar lo que no te interese y seguir trabajando encima.",
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Los avisos NO se callan: casi siempre son medidas que el modelo no
                // entregó, y eso hay que saberlo ANTES de acotar el plano.
                var detalle = string.Join(
                    Environment.NewLine, fallos.Take(25).Select(f => "  - " + f));

                if (fallos.Count > 25)
                {
                    detalle += Environment.NewLine +
                               $"  ... y {fallos.Count - 25} aviso(s) más.";
                }

                PlanoHintText.Text +=
                    Environment.NewLine + Environment.NewLine +
                    $"AVISOS ({fallos.Count}):" + Environment.NewLine + detalle;

                MessageBox.Show(
                    $"{r}\n\nHubo {fallos.Count} aviso(s) que se toleraron:\n\n" + detalle,
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo dibujar la planta:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Alto de la sección más alta, en metros de dibujo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que decide dónde arranca la fila de alzados: van
    /// <see cref="AlzadoLayout.AireSobreSecciones"/> metros por encima de esto.
    /// </para>
    /// <para>
    /// Se calcula de los DATOS y no midiendo los bloques en AutoCAD, y es a propósito.
    /// Medir los bloques parece más fiel —«la sección más alta dibujada»— pero tiene
    /// dos problemas: hay que insertar cada bloque para poder medirlo, y si el usuario
    /// dibuja los alzados sin haber dibujado antes las secciones no habría nada que
    /// medir y la fila se iría a la cota mínima. Con los datos, el resultado es el
    /// mismo y es <b>determinista</b>: la altura del dibujo de una sección es su
    /// peralte por la escala, y en una circular su diámetro.
    /// </para>
    /// </remarks>
    private double AltoMaximoDeLasSecciones(double escala)
    {
        var maximo = 0d;

        foreach (var s in _datos.SeccionesConcreto)
        {
            // En la circular la altura no se usa: el alto del dibujo es el DIAMETRO,
            // que se captura en la base. Tomar AlturaCm dejaria una columna redonda
            // de 50 cm contando como 0 y la fila de alzados se le echaria encima.
            var altoCm = s.EsCircular ? s.DiametroCm : s.AlturaCm;

            maximo = Math.Max(maximo, altoCm * escala);
        }

        return maximo;
    }

    /// <summary>
    /// Traduce el modelo de ETABS a lo que entiende el dibujante de plantas.
    /// </summary>
    /// <remarks>
    /// Esta traducción vive aquí, en la ventana, y no en CadLink.Cad: el dibujante no
    /// referencia a CadLink.Etabs a propósito, igual que no conoce la cuadrícula de
    /// las secciones. Un solo sitio traduce y el dibujante se puede alimentar mañana
    /// de otra fuente sin tocarlo.
    /// </remarks>
    private PlantaCad ArmarPlanta(ModeloEtabs modelo, string? nivelPedido = null)
    {
        var nivel = nivelPedido ?? NivelElegido;

        var p = new PlantaCad
        {
            Nivel = nivel ?? string.Empty,
            Modelo = modelo.Archivo,
            ConRotulos = true
        };

        foreach (var el in modelo.Elementos)
        {
            // El MISMO filtro por nivel que usa el lienzo de esta pestaña
            if (!string.IsNullOrWhiteSpace(nivel) &&
                !string.Equals(el.Story, nivel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!VisibleEnElPlano(el.Clase))
            {
                continue;
            }

            // El TIPO se clasifica con la regla de la macro -ClasificaTipo- porque es lo
            // que decide la CAPA: castillo y columna no van a la misma, ni dala y trabe.
            // Y la FORMA, para que un perfil de acero se vaya a E-ACERO.
            var t2 = el.Clase == ClaseElemento.Columna ? el.PeralteM : el.AnchoM;
            var t3 = el.Clase == ClaseElemento.Columna ? el.AnchoM : el.PeralteM;

            var e = new ElementoPlanta
            {
                Clase = ClasePlantaDe(el.Clase),
                Tipo = SeccionesModelo.ClasificaTipo(el.Clase, el.Seccion, t2, t3),
                Forma = el.Forma,
                Etiqueta = el.Etiqueta,
                Seccion = el.Seccion,
                X1 = el.X1, Y1 = el.Y1,
                X2 = el.X2, Y2 = el.Y2,
                AnchoM = el.AnchoM,
                PeralteM = el.PeralteM
            };

            foreach (var v in el.Vertices)
            {
                e.Vertices.Add((v.X, v.Y));
            }

            p.Elementos.Add(e);
        }

        // El texto se dimensiona respecto al TAMAÑO DE LA PLANTA, no a un valor fijo:
        // 25 cm de letra se lee bien en una planta de 20 m y es ilegible en una de
        // 200. Se toma el lado mayor y se le saca una milésima parte, acotado entre
        // 5 cm y 60 cm para que nunca salga un texto absurdo.
        p.AlturaTexto = AlturaDeTexto(p);

        return p;
    }

    /// <summary>
    /// Una planta <b>por nivel</b>, en el orden en que la macro las reparte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ORDEN_NIVELES = ASC</c>, así que primero el nivel más bajo: el juego se lee de
    /// izquierda a derecha empezando por la cimentación, como se arma un juego de planos.
    /// </para>
    /// <para>
    /// Los niveles <b>sin elementos</b> se saltan, igual que hace <c>HayElementosEn</c>: un
    /// hueco en la fila de plantas por un nivel vacío se ve como un error de dibujo.
    /// </para>
    /// <para>
    /// La altura de texto es la MISMA para todas —la que sale de la planta más grande— para
    /// que el juego se vea de una pieza y no con una letra por planta.
    /// </para>
    /// </remarks>
    private List<PlantaCad> ArmarTodasLasPlantas(ModeloEtabs modelo)
    {
        var plantas = new List<PlantaCad>();

        // Los niveles CON ELEMENTOS y no la lista de la API: es la única forma de que la
        // BASE entre, porque GetStories no la devuelve y en el modelo sí hay elementos con
        // Story = «Base» —las cadenas de desplante—. Ver ModeloEtabs.NivelesConElementos.
        foreach (var n in modelo.NivelesConElementos(ascendente: true))
        {
            var p = ArmarPlanta(modelo, n.Nombre);

            if (p.Elementos.Count > 0)
            {
                plantas.Add(p);
            }
        }

        if (plantas.Count > 1)
        {
            var altura = plantas.Max(p => p.AlturaTexto);
            foreach (var p in plantas)
            {
                p.AlturaTexto = altura;
            }
        }

        return plantas;
    }

    private static double AlturaDeTexto(PlantaCad p)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        void Medir(double x, double y)
        {
            xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x);
            yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y);
        }

        foreach (var el in p.Elementos)
        {
            if (el.Vertices.Count >= 3)
            {
                foreach (var v in el.Vertices)
                {
                    Medir(v.X, v.Y);
                }
            }
            else
            {
                Medir(el.X1, el.Y1);
                Medir(el.X2, el.Y2);
            }
        }

        if (xMax <= xMin && yMax <= yMin)
        {
            return 0.25;
        }

        var lado = Math.Max(xMax - xMin, yMax - yMin);
        return Math.Clamp(lado / 100.0, 0.05, 0.60);
    }

    private bool VisibleEnElPlano(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => VerColumnasPlanoChk.IsChecked == true,
        ClaseElemento.Trabe => VerTrabesPlanoChk.IsChecked == true,
        ClaseElemento.Muro => VerMurosPlanoChk.IsChecked == true,
        ClaseElemento.Losa => VerLosasPlanoChk.IsChecked == true,

        // Las diagonales no tienen casilla en esta pestaña porque en planta se
        // proyectan como una línea suelta que no dice nada. Por eso tampoco se
        // dibujan: el plano saldría con líneas sin explicación.
        _ => false
    };

    private static ClasePlanta ClasePlantaDe(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => ClasePlanta.Columna,
        ClaseElemento.Trabe => ClasePlanta.Trabe,
        ClaseElemento.Muro => ClasePlanta.Muro,
        ClaseElemento.Losa => ClasePlanta.Losa,
        _ => ClasePlanta.Diagonal
    };

    private void RestaurarFiltrosDelVisor()
    {
        _vista.VerColumnas = VerColumnasChk.IsChecked == true;
        _vista.VerTrabes = VerTrabesChk.IsChecked == true;
        _vista.VerDiagonales = VerDiagonalesChk.IsChecked == true;
        _vista.VerMuros = VerMurosChk.IsChecked == true;
        _vista.VerLosas = VerLosasChk.IsChecked == true;
    }

    /// <summary>
    /// Escala del patrón AR-CONC que se escribió en la casilla.
    /// </summary>
    /// <remarks>
    /// Se acepta coma o punto como separador decimal: en un teclado en español la
    /// coma es lo natural, y con <c>InvariantCulture</c> a secas «0,0006» se leería
    /// como 6 y saldría un rayado absurdo.
    /// </remarks>
    private double LeerEscalaHatch()
    {
        var texto = (HatchScaleBox.Text ?? string.Empty).Trim().Replace(',', '.');

        if (double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            && v > 0)
        {
            return v;
        }

        // El mismo valor por omision que EscalaPatronBase en SeccionDrawer: el
        // 0.0003 de la macro. Tienen que coincidir, o lo que el usuario ve en la
        // casilla no es lo que se dibuja cuando la deja en blanco.
        return 0.0003;
    }

    private void OnEscalaHatchCambiada(object sender, RoutedEventArgs e)
    {
        // Se normaliza lo escrito para que se vea qué valor se va a usar de verdad
        HatchScaleBox.Text =
            LeerEscalaHatch().ToString("0.######", CultureInfo.InvariantCulture);
    }

    private void OnVistaTabCambiada(object sender, SelectionChangedEventArgs e)
    {
        // Solo interesa el cambio de las pestañas del visor, no el de los
        // controles de dentro, que propagan el mismo evento.
        if (ReferenceEquals(e.OriginalSource, VistaTabs))
        {
            RedibujarVistas();
        }
    }

    private void OnNivelPlantaCambiado(object sender, SelectionChangedEventArgs e)
    {
        if (_listo)
        {
            DibujarPlanta();
        }
    }

    /// <summary>Vistas predefinidas, con los mismos nombres que usa ETABS.</summary>
    private void OnVistaPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b)
        {
            return;
        }

        switch (b.Tag?.ToString())
        {
            case "ISO":
                _vista.Azimut = 35;
                _vista.Elevacion = 22;
                break;

            case "FRENTE":
                _vista.Azimut = 0;
                _vista.Elevacion = 0;
                break;

            case "LADO":
                _vista.Azimut = 90;
                _vista.Elevacion = 0;
                break;

            case "PLANTA":
                // 90° de elevación mira desde arriba: es la planta
                _vista.Azimut = 0;
                _vista.Elevacion = 90;
                break;
        }

        _vista.Zoom = 1;
        _vista.PanX = 0;
        _vista.PanY = 0;
        RedibujarVistas();
    }

    private void OnVistaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas lienzo)
        {
            return;
        }

        _arrastreDesde = e.GetPosition(lienzo);

        // Girar tiene sentido en las dos vistas de volumen; en planta, no.
        _girando = e.ChangedButton == MouseButton.Left
                   && (ReferenceEquals(lienzo, Vista3DCanvas)
                       || ReferenceEquals(lienzo, ExtruidaCanvas));
        _moviendo = e.ChangedButton == MouseButton.Right;

        if (_girando || _moviendo)
        {
            lienzo.CaptureMouse();
        }
    }

    private void OnVistaMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Canvas lienzo || (!_girando && !_moviendo))
        {
            return;
        }

        var p = e.GetPosition(lienzo);
        var dx = p.X - _arrastreDesde.X;
        var dy = p.Y - _arrastreDesde.Y;
        _arrastreDesde = p;

        if (_girando)
        {
            _vista.Azimut += dx * 0.5;

            // La elevación se limita: pasando de ±90° la vista se voltea y se
            // pierde la noción de qué es arriba.
            _vista.Elevacion = Math.Clamp(_vista.Elevacion + (dy * 0.4), -89, 89);
        }
        else
        {
            _vista.PanX += dx;
            _vista.PanY += dy;
        }

        // Al arrastrar solo se redibuja el lienzo que se está manipulando: hacerlo
        // en los dos duplicaría el trabajo en cada movimiento del mouse.
        // Se redibuja SOLO el lienzo que se esta manipulando. Hacerlo en los tres
        // multiplicaria el trabajo en cada movimiento del mouse, y la extruida es la
        // mas caras de las tres: pinta seis caras por barra.
        if (ReferenceEquals(lienzo, Vista3DCanvas))
        {
            _vista.Dibujar3D(Vista3DCanvas);
        }
        else if (ReferenceEquals(lienzo, ExtruidaCanvas))
        {
            _vista.DibujarExtruido(ExtruidaCanvas);
        }
        else
        {
            DibujarPlanta();
        }
    }

    private void OnVistaMouseUp(object sender, MouseEventArgs e)
    {
        if (sender is Canvas lienzo && (_girando || _moviendo))
        {
            lienzo.ReleaseMouseCapture();
        }

        _girando = false;
        _moviendo = false;
    }

    private void OnVistaWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Canvas lienzo)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        _vista.Zoom = Math.Clamp(_vista.Zoom * factor, 0.08, 60);

        // Se redibuja SOLO el lienzo que se esta manipulando. Hacerlo en los tres
        // multiplicaria el trabajo en cada movimiento del mouse, y la extruida es la
        // mas caras de las tres: pinta seis caras por barra.
        if (ReferenceEquals(lienzo, Vista3DCanvas))
        {
            _vista.Dibujar3D(Vista3DCanvas);
        }
        else if (ReferenceEquals(lienzo, ExtruidaCanvas))
        {
            _vista.DibujarExtruido(ExtruidaCanvas);
        }
        else
        {
            DibujarPlanta();
        }

        // Sin esto la rueda además desplaza el ScrollViewer de la pestaña y la
        // vista se va de la pantalla mientras se hace zoom.
        e.Handled = true;
    }

    // ======================================================================
    // AutoCAD
    // ======================================================================


    private void OnExport(object sender, RoutedEventArgs e)
    {
        // Segunda comprobación, en el punto de ejecución.
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Revisar(out var problemas))
        {
            MessageBox.Show(
                "Corrige esto antes de generar el dibujo:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Ya no hay que elegir modo: se dibuja SIEMPRE por COM sobre la sesion
        // abierta de AutoCAD. La otra opcion de la pestaña que se quito era escribir
        // un DXF, y no estaba implementada: lo unico que hacia era ofrecerse y
        // despues avisar de que no funcionaba.
        try
        {
            Cursor = Cursors.Wait;

            var escala = LeerEscala();

            // launchIfMissing en false a proposito: arrancar AutoCAD tarda mucho y
            // consume una licencia. Mejor pedirle al usuario que lo abra.
            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new SeccionDrawer(doc, escala)
            {
                EscalaHatch = LeerEscalaHatch(),
                Redibujar = RedibujarChk.IsChecked == true
            };

            dibujante.AsegurarCapas(ClavesDeVarillaUsadas());

            // Se empieza después de lo que ya esté dibujado, para no encimarlo
            var x = dibujante.PosicionInicialX();
            var entidades = 0;

            var dibujadas = 0;

            foreach (var s in _datos.SeccionesConcreto)
            {
                var saltadasAntes = dibujante.Saltadas.Count;

                var n = dibujante.Dibujar(AFormatoCad(s), x, 0);

                // Igual que la macro: la seccion que ya es bloque se SALTA. Quien
                // decide es el dibujante, no esta linea: aqui solo se detecta que
                // la salto porque su lista crecio. Asi no hay dos sitios distintos
                // decidiendo lo mismo, que es como se acaba dibujando doble.
                if (dibujante.Saltadas.Count > saltadasAntes)
                {
                    continue;
                }

                entidades += n;
                dibujadas++;

                // Igual que la macro: se avanza el ancho mas 35 cm de aire. Solo lo
                // avanzan las que SI se dibujaron; si lo avanzaran las saltadas,
                // cada seccion ya hecha dejaria un hueco vacio en la fila.
                //
                // Y tampoco lo avanzan las que volvieron a SU SITIO al redibujarse:
                // esas no ocupan lugar nuevo, asi que si avanzaran, actualizar un
                // plano ya acomodado dejaria la fila llena de huecos.
                if (!dibujante.UltimaFueASuSitio)
                {
                    x += (s.BaseCm + 35) * escala;
                }
            }

            var saltadas = dibujante.Saltadas
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Lo ULTIMO: el rotulado por encima de todo. Va aquí, fuera del bucle,
            // porque sube el rotulado de todas las secciones de una sola pasada, y
            // porque tiene que aplicarse despues de que los estribos suban al
            // frente en cada seccion.
            dibujante.RotulosAlFrente();

            // Si los contornos no pudieron quedar en negro de verdad, hay que
            // decirlo: con el fondo del Model oscuro se dibujan BLANCOS y el usuario
            // ve el defecto sin ninguna pista de a qué se debe.
            dibujante.RevisarColorNegro();

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            // Los fallos tolerados se MUESTRAN. Antes se descartaban y por eso los
            // hatches podían faltar sin que nada lo dijera: el dibujo salía
            // incompleto y no había forma de saber qué había fallado.
            var fallos = dibujante.Fallos;

            // Las saltadas se dicen con NOMBRE Y MOTIVO. Un simple "se saltaron 3"
            // no sirve: el usuario tiene que poder ver si la que cambió es una de
            // ellas, y saber qué hacer para forzar el redibujado.
            var rehechas = dibujante.Redibujadas
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var aviso = saltadas.Count == 0
                ? string.Empty
                : $"\n\nSE SALTARON {saltadas.Count} sección(es) porque su bloque ya " +
                  "existe en el dibujo:\n  " + string.Join(", ", saltadas) +
                  "\n\nAsí lo hace tu macro, para no deshacer el acomodo del plano.\n" +
                  "Si cambiaste su armado y quieres rehacerlas, marca la casilla\n" +
                  "\"Redibujar las que ya existen\" y vuelve a dibujar: cada una\n" +
                  "vuelve al mismo sitio donde ya estaba.";

            if (rehechas.Count > 0)
            {
                aviso +=
                    $"\n\nSe REHICIERON {rehechas.Count} sección(es) en su mismo sitio:\n  " +
                    string.Join(", ", rehechas);
            }

            var resumen =
                "Listo.\n\n" +
                $"{dibujadas} sección(es) dibujadas\n" +
                $"{entidades} entidades creadas\n\n" +
                "Cada sección quedó agrupada en un bloque con el nombre de su ID." +
                aviso;

            var conteo = saltadas.Count == 0
                ? $"Dibujadas {dibujadas} sección(es) en AutoCAD."
                : $"Dibujadas {dibujadas} sección(es); {saltadas.Count} saltada(s) " +
                  "por existir ya.";

            if (fallos.Count == 0)
            {
                StatusText.Text = conteo;

                // Las notas informativas quedan a mano, pero NO interrumpen: el
                // dibujo salió bien y no hay nada que el usuario deba atender.
                MostrarNotas(dibujante.Notas.Count == 0
                    ? string.Empty
                    : "Notas del último dibujo:" + Environment.NewLine +
                      string.Join(Environment.NewLine,
                          dibujante.Notas.Select(n => "  - " + n)));

                MessageBox.Show(resumen, AppInfo.ProductName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var detalle = string.Join(Environment.NewLine, fallos.Select(f => "  - " + f));

                StatusText.Text =
                    $"Dibujadas {dibujadas} sección(es), " +
                    $"con {fallos.Count} aviso(s). Ver el detalle bajo la vista previa.";

                MostrarNotas(
                    "AVISOS DEL ULTIMO DIBUJO (" + fallos.Count + "):" +
                    Environment.NewLine + detalle);

                MessageBox.Show(
                    resumen + "\n\n" +
                    "PERO hubo " + fallos.Count + " fallo(s) que se toleraron, " +
                    "así que el dibujo puede estar incompleto:\n\n" + detalle +
                    "\n\nEste mismo texto queda bajo la vista previa.",
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (AcadNotAvailableException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (AcadBusyException ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Error al dibujar en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>Escala de captura a unidades de dibujo. 0.01 = cm a metros.</summary>
    /// <summary>
    /// Escala del dibujo: <b>cuánto mide en AutoCAD un centímetro capturado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ya no se captura en la interfaz. La casilla que había solo servía para
    /// descuadrar el dibujo: el juego de planos se dibuja siempre con la misma
    /// correspondencia, así que exponerla era ofrecer una forma de romperlo sin ganar
    /// nada.
    /// </para>
    /// <para>
    /// <b>El valor NO cambia: sigue siendo 0.01.</b> Es la misma correspondencia de la
    /// macro —se captura en centímetros y se dibuja en metros— y es la que produce la
    /// geometría que ya estás obteniendo. Ponerlo en 1.0 «porque es 1=1» multiplicaría
    /// todo el dibujo por cien: una columna de 50 cm saldría de 50 m.
    /// </para>
    /// <para>
    /// Si algún día hiciera falta otra escala, este es el único sitio que se toca.
    /// </para>
    /// </remarks>
    private const double EscalaDeDibujo = 0.01;

    private double LeerEscala() => EscalaDeDibujo;

    /// <summary>Claves de varilla presentes en la captura, para crear solo esas capas.</summary>
    private IEnumerable<string> ClavesDeVarillaUsadas()
    {
        foreach (var s in _datos.SeccionesConcreto)
        {
            yield return Varilla.Normalizar(s.Estribo);
            yield return Varilla.Normalizar(s.DiamEsqSup);
            yield return Varilla.Normalizar(s.DiamIntSupEfectivo);
            yield return Varilla.Normalizar(s.DiamEsqInfEfectivo);
            yield return Varilla.Normalizar(s.DiamIntInfEfectivo);
            yield return Varilla.Normalizar(s.DiamInter);
            yield return Varilla.Normalizar(s.DiamEstriboDiamante);

            // La varilla del circulo. Sin esto, la capa VAR_#8 de una columna redonda
            // no se crearia y sus varillas saldrian en la capa activa del dibujo, con
            // otro color y sin poder apagarlas por separado.
            yield return Varilla.Normalizar(s.DiamVarTotalEfectivo);
        }
    }

    /// <summary>
    /// Pasa una fila de la cuadrícula al formato que consume el motor de dibujo,
    /// resolviendo aquí los diámetros.
    /// </summary>
    /// <remarks>
    /// La resolución se hace en esta capa a propósito: el motor de dibujo recibe
    /// diámetros ya en centímetros y no puede recibir uno sin reconocer, que es
    /// justamente el error silencioso de la macro.
    /// </remarks>
    private SeccionCad AFormatoCad(SeccionConcretoRow r)
    {
        static VarCad V(string clave) =>
            Varilla.TryDiametroCm(clave, out var cm)
                ? new VarCad(Varilla.Normalizar(clave), cm)
                : new VarCad(string.Empty, 0);

        return new SeccionCad
        {
            Modo = ModoElegido,
            // El nombre que va al PLANO: una columna redonda se rotula COLUMNA.
            Elemento = r.ElementoRotulo,
            Id = r.Id,
            BaseCm = r.BaseCm,
            AlturaCm = r.AlturaCm,
            RecubrimientoCm = r.RecubrimientoCm,
            GanchoCm = r.GanchoCm,
            Estribo = V(r.Estribo),
            Superior = new LechoCad
            {
                NEsquina = r.NEsqSup,
                Esquina = V(r.DiamEsqSup),
                NIntermedia = r.NIntSup,
                Intermedia = V(r.DiamIntSupEfectivo)
            },
            Inferior = new LechoCad
            {
                NEsquina = r.NEsqInf,
                Esquina = V(r.DiamEsqInfEfectivo),
                NIntermedia = r.NIntInf,
                Intermedia = V(r.DiamIntInfEfectivo)
            },
            NLateral = r.NInter,
            Lateral = V(r.DiamInter),
            Fc = r.Fc,
            Escala = r.Escala,
            Separacion = r.SeparacionCm,

            // Columna R de la hoja: la macro compara contra "SI" en mayusculas y
            // sin espacios. Aqui se acepta cualquier variante razonable para que
            // "Si", "sí" o "X" no dejen de dibujar el diamante en silencio.
            //
            // Y NO en la seccion redonda: el diamante es un rombo entre las varillas
            // de dos lechos, y en un circulo no hay lechos ni esquinas donde apoyarlo.
            Diamante = EsSi(r.EstriboDiamante) && !r.EsCircular,

            // Columna S. Vacia = se usa la varilla del estribo principal.
            EstriboDiamanteVar = V(
                string.IsNullOrWhiteSpace(r.DiamEstriboDiamante)
                    ? r.Estribo
                    : r.DiamEstriboDiamante),

            // ---------- Seccion circular ----------
            Circular = r.EsCircular,
            NVarTotal = r.NVarTotal,
            VarTotal = V(r.DiamVarTotalEfectivo),
            ZunchoHelicoidal = r.EsZunchoHelicoidal
        };
    }

    /// <summary>Convierte una fila de la tabla en datos de alzado.</summary>
    private AlzadoCad AFormatoAlzado(SeccionConcretoRow r)
    {
        static VarCad V(string clave) =>
            Varilla.TryDiametroCm(clave, out var cm)
                ? new VarCad(Varilla.Normalizar(clave), cm)
                : new VarCad(string.Empty, 0);

        var estribo = V(r.Estribo);

        // Con diamante, el alzado se dibuja con el diámetro del DIAMANTE, no con el
        // del estribo principal. Es lo que hace la macro al reasignar estrDia.
        //
        // Pero el diamante NO aplica a una sección redonda: es un rombo entre las
        // varillas de dos lechos, y en un círculo no hay lechos. Se descarta AQUI, en
        // una sola variable, para que no se cuele por dos caminos: si se dejara el
        // 'diamante' crudo, el zuncho de la columna redonda se dibujaría con el
        // diámetro del diamante y nadie entendería por qué.
        var diamante = EsSi(r.EstriboDiamante) && !r.EsCircular;

        var varDiamante = V(
            string.IsNullOrWhiteSpace(r.DiamEstriboDiamante) ? r.Estribo : r.DiamEstriboDiamante);

        return new AlzadoCad
        {
            Tipo = TipoDe(r.Elemento, r.Id) ?? TipoElemento.Trabe,
            Modo = ModoElegido,
            // El nombre que va al PLANO: una columna redonda se rotula COLUMNA.
            Elemento = r.ElementoRotulo,
            Id = r.Id,
            BaseCm = r.BaseCm,
            AlturaCm = r.AlturaCm,
            RecubrimientoCm = r.RecubrimientoCm > 0 ? r.RecubrimientoCm : 2.5,
            LongitudM = Estribos.LongitudDeColumnaW(r.LongitudM),
            GanchoCm = r.GanchoCm,
            SeparacionesCm = Separaciones(r.SeparacionCm),
            Estribo = estribo,
            EstriboDibujo = diamante && varDiamante.Existe ? varDiamante : estribo,
            Superior = new LechoCad
            {
                NEsquina = r.NEsqSup,
                Esquina = V(r.DiamEsqSup),
                NIntermedia = r.NIntSup,
                Intermedia = V(r.DiamIntSupEfectivo)
            },
            Inferior = new LechoCad
            {
                NEsquina = r.NEsqInf,
                Esquina = V(r.DiamEsqInfEfectivo),
                NIntermedia = r.NIntInf,
                Intermedia = V(r.DiamIntInfEfectivo)
            },
            NLateral = r.NInter,
            Lateral = V(r.DiamInter),
            Fc = r.Fc,
            Escala = r.Escala,
            Separacion = r.SeparacionCm,

            Diamante = diamante,
            EstriboDiamanteVar = varDiamante,

            // ---------- Seccion circular ----------
            Circular = r.EsCircular,
            NVarTotal = r.NVarTotal,
            VarTotal = V(r.DiamVarTotalEfectivo),
            ZunchoHelicoidal = r.EsZunchoHelicoidal
        };
    }

    /// <summary>
    /// Clasifica el elemento para el alzado, o <c>null</c> si no lleva alzado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Solo cuatro familias llevan alzado</b>: trabe, contratrabe, columna y dado.
    /// Es lo que hace la macro, cuyo bucle envuelve todo en
    /// <c>If isTrabeOrContratrabe Or isColumnaODado Then</c> y salta el resto.
    /// </para>
    /// <para>
    /// Antes esto devolvía <c>Trabe</c> para cualquier cosa que no reconociera, así
    /// que un castillo o una cadena acababan con un alzado de trabe sin haberlo
    /// pedido. Ahora lo que no encaja se <b>omite</b> y se informa.
    /// </para>
    /// <para>
    /// Se reconoce por el nombre de la columna A y, si no coincide, por el prefijo
    /// del ID. El orden importa: <c>CT-</c> se prueba antes que <c>C-</c>, porque una
    /// contratrabe también empieza con C.
    /// </para>
    /// </remarks>
    private static TipoElemento? TipoDe(string? elemento, string? id)
    {
        var e = (elemento ?? string.Empty).Trim().ToUpperInvariant();
        var i = (id ?? string.Empty).Trim().ToUpperInvariant();

        if (e == "CONTRATRABE" || i.StartsWith("CT-", StringComparison.Ordinal))
        {
            return TipoElemento.Contratrabe;
        }

        // «COLUMNA» y «COLUMNA CIRCULAR» son las dos columnas: las dos llevan alzado
        // VERTICAL y las dos se rotulan COLUMNA. Con la comparacion exacta que habia
        // antes, una columna redonda caia al final del metodo y se quedaba SIN alzado
        // salvo que su ID empezara por C-.
        if (e == "COLUMNA" || e == SeccionConcretoRow.ElementoColumnaCircular
            || i.StartsWith("C-", StringComparison.Ordinal))
        {
            return TipoElemento.Columna;
        }

        // Los DOS dados llevan alzado vertical, igual que las dos columnas. Sin el
        // redondo, un DADO CIRCULAR se quedaba sin alzado salvo que su ID empezara por D-.
        if (e == "DADO" || e == SeccionConcretoRow.ElementoDadoCircular
            || i.StartsWith("D-", StringComparison.Ordinal))
        {
            return TipoElemento.Dado;
        }

        if (e == "TRABE" || i.StartsWith("T-", StringComparison.Ordinal))
        {
            return TipoElemento.Trabe;
        }

        // Castillos, cadenas, CABEZAL y cualquier otro elemento: sin alzado.
        //
        // El CABEZAL estuvo un rato devolviendo Trabe, por la idea de que al ser una
        // pieza tendida le tocaba alzado horizontal. El usuario lo quito: un cabezal se
        // documenta con su seccion y su armado, no con un alzado de estribos por zonas
        // L/4-L/2-L/4, que es lo que dibuja el alzado de trabe y no describe un cabezal.
        return null;
    }

    /// <summary>
    /// Separaciones de las tres zonas a partir de la celda <c>10-15-20</c>.
    /// </summary>
    /// <remarks>
    /// Si falta la segunda se repite la primera, y si falta la tercera se repite la
    /// segunda, igual que <c>ParseSpacings</c>. Sin ningún valor se usan 15 cm.
    /// </remarks>
    private static double[] Separaciones(string? celda)
    {
        var partes = (celda ?? string.Empty)
            .Replace(" ", string.Empty)
            .Replace("m", string.Empty)
            .Replace(',', '.')
            .Split('-');

        double Leer(int i)
        {
            if (i >= partes.Length)
            {
                return 0;
            }

            return double.TryParse(partes[i], NumberStyles.Any,
                CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        var a = Leer(0);
        var b = Leer(1);
        var c = Leer(2);

        if (a <= 0 && b <= 0 && c <= 0) { a = 15; }
        if (b <= 0) { b = a; }
        if (c <= 0) { c = b; }

        return new[] { a, b, c };
    }

    /// <summary>¿La celda dice que sí?</summary>
    private static bool EsSi(string? v)
    {
        var t = (v ?? string.Empty).Trim();

        return t.Equals("SI", StringComparison.OrdinalIgnoreCase)
               || t.Equals("SÍ", StringComparison.OrdinalIgnoreCase)
               || t.Equals("S", StringComparison.OrdinalIgnoreCase)
               || t.Equals("X", StringComparison.OrdinalIgnoreCase)
               || t.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
               || t == "1";
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        if (Revisar(out var problemas))
        {
            MessageBox.Show(
                $"Sin errores.\n\n{_datos.SeccionesConcreto.Count} sección(es) revisadas.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBox.Show(
            $"Se encontraron {problemas.Count} problema(s):\n\n" + string.Join("\n", problemas),
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Revisa la captura. Esta validación sí está implementada y funcionando.
    /// </summary>
    /// <remarks>
    /// <b>Incluye la comprobación que le falta a la macro:</b> que todo diámetro
    /// de varilla sea reconocido. En la macro, un diámetro mal escrito no producía
    /// error: se usaba un valor por omisión y la sección se dibujaba con el
    /// diámetro equivocado sin avisar.
    /// </remarks>
    private bool Revisar(out List<string> problemas)
    {
        problemas = new List<string>();

        if (_datos.SeccionesConcreto.Count == 0)
        {
            problemas.Add("• No hay ninguna sección capturada.");
            return false;
        }

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fila = 1;

        foreach (var s in _datos.SeccionesConcreto)
        {
            fila++;
            var etiqueta = string.IsNullOrWhiteSpace(s.Id) ? $"fila {fila}" : s.Id;

            if (string.IsNullOrWhiteSpace(s.Id))
            {
                problemas.Add($"• Fila {fila}: falta el ID. Es el nombre del bloque de AutoCAD.");
            }
            else if (!vistos.Add(s.Id))
            {
                problemas.Add($"• El ID '{s.Id}' está repetido. Cada bloque necesita un nombre único.");
            }

            if (s.RecubrimientoCm < 0)
            {
                problemas.Add($"• {etiqueta}: el recubrimiento no puede ser negativo.");
            }

            RevisarDiametro(problemas, etiqueta, "estribo", s.Estribo, obligatorio: true);

            if (s.EsCircular)
            {
                RevisarCircular(problemas, etiqueta, s);
            }
            else
            {
                RevisarRectangular(problemas, etiqueta, s);
            }
        }

        return problemas.Count == 0;
    }

    /// <summary>Revisiones propias de la sección rectangular.</summary>
    private static void RevisarRectangular(
        List<string> problemas, string etiqueta, SeccionConcretoRow s)
    {
        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            problemas.Add($"• {etiqueta}: base y altura deben ser mayores que cero.");
        }

        RevisarLecho(problemas, etiqueta, "lecho sup. esquina", s.NEsqSup, s.DiamEsqSup);
        RevisarLecho(problemas, etiqueta, "lecho sup. intermedio", s.NIntSup, s.DiamIntSupEfectivo);
        RevisarLecho(problemas, etiqueta, "lecho inf. esquina", s.NEsqInf, s.DiamEsqInfEfectivo);
        RevisarLecho(problemas, etiqueta, "lecho inf. intermedio", s.NIntInf, s.DiamIntInfEfectivo);
        RevisarLecho(problemas, etiqueta, "varillas laterales", s.NInter, s.DiamInter);

        if (string.Equals(s.EstriboDiamante.Trim(), "SI", StringComparison.OrdinalIgnoreCase))
        {
            var d = string.IsNullOrWhiteSpace(s.DiamEstriboDiamante)
                ? s.Estribo
                : s.DiamEstriboDiamante;
            RevisarDiametro(problemas, etiqueta, "estribo diamante", d, obligatorio: true);
        }

        // El acero debe caber: dos varillas de esquina mas los estribos
        if (Varilla.TryDiametroCm(s.Estribo, out var de) &&
            Varilla.TryDiametroCm(s.DiamEsqSup, out var dv) &&
            s.BaseCm > 0)
        {
            var necesario = (2 * s.RecubrimientoCm) + (2 * de) + (2 * dv);
            if (necesario >= s.BaseCm)
            {
                problemas.Add(
                    $"• {etiqueta}: con recubrimiento {s.RecubrimientoCm:N1} cm, estribo " +
                    $"{Varilla.Normalizar(s.Estribo)} y varilla {Varilla.Normalizar(s.DiamEsqSup)} " +
                    $"se necesitan {necesario:N1} cm y la base es de {s.BaseCm:N1} cm.");
            }
        }
    }

    /// <summary>
    /// Revisiones propias de la sección circular.
    /// </summary>
    /// <remarks>
    /// Son OTRAS, no las mismas con un aviso: en una sección redonda no hay lechos
    /// que revisar, la altura no significa nada, y en cambio aparece una comprobación
    /// que la rectangular no necesita —que las varillas quepan en el perímetro del
    /// círculo de paso—, que es el error de captura típico de una columna redonda.
    /// </remarks>
    private static void RevisarCircular(
        List<string> problemas, string etiqueta, SeccionConcretoRow s)
    {
        if (s.DiametroCm <= 0)
        {
            problemas.Add(
                $"• {etiqueta}: es circular, así que la base es el DIÁMETRO y tiene " +
                "que ser mayor que cero.");
            return;
        }

        // La altura no se usa. Si trae algo distinto del diámetro, es que el usuario
        // cree que sirve para algo: mejor decirlo que dibujar callando.
        if (s.AlturaCm > 0 && Math.Abs(s.AlturaCm - s.DiametroCm) > 0.01)
        {
            problemas.Add(
                $"• {etiqueta}: es circular, así que la altura ({s.AlturaCm:N1} cm) se " +
                $"ignora y se usa la base como diámetro ({s.DiametroCm:N1} cm). " +
                "Pon el mismo valor en las dos, o deja la altura en cero.");
        }

        if (s.NVarTotal <= 0)
        {
            problemas.Add(
                $"• {etiqueta}: es circular, así que el armado se captura en «N total» " +
                "y no por lechos. Falta el número de varillas.");
        }

        RevisarDiametro(problemas, etiqueta, "varilla del círculo",
            s.DiamVarTotalEfectivo, obligatorio: s.NVarTotal > 0);

        // Con menos de 3 varillas no hay círculo de acero que confine nada, y el
        // dibujo saldría como dos o una varilla suelta en el aire.
        if (s.NVarTotal is > 0 and < 3)
        {
            problemas.Add(
                $"• {etiqueta}: {s.NVarTotal} varilla(s) en una columna redonda no " +
                "forman un círculo. El mínimo práctico es 3, y lo habitual son 6 u 8.");
        }

        // Los lechos capturados NO se dibujan en una sección circular. Es el error
        // que se comete al marcar «Circular» sobre una fila que ya estaba llena.
        var enLechos = s.NEsqSup + s.NIntSup + s.NEsqInf + s.NIntInf + s.NInter;
        if (enLechos > 0)
        {
            problemas.Add(
                $"• {etiqueta}: es circular, pero tiene {enLechos} varilla(s) " +
                "capturadas por lechos. Esas NO se dibujan: en una sección redonda " +
                "solo se usa «N total». Vacía los lechos o quita el «SI» de Circular.");
        }

        // Que las varillas quepan en el perímetro del círculo de paso. Es la
        // comprobación que de verdad hace falta aquí: con 12 varillas del #10 en una
        // columna de 30 cm el acero se traslapa, y en el dibujo salen las varillas
        // pisándose unas a otras sin ningún aviso.
        if (Varilla.TryDiametroCm(s.Estribo, out var dEst) &&
            Varilla.TryDiametroCm(s.DiamVarTotalEfectivo, out var dVar) &&
            s.NVarTotal >= 3)
        {
            // Radio del círculo donde van los centros de las varillas
            var rPaso = (s.DiametroCm / 2.0) - s.RecubrimientoCm - dEst - (dVar / 2.0);

            if (rPaso <= 0)
            {
                problemas.Add(
                    $"• {etiqueta}: con diámetro {s.DiametroCm:N1} cm, recubrimiento " +
                    $"{s.RecubrimientoCm:N1} cm y zuncho {Varilla.Normalizar(s.Estribo)} " +
                    "no queda sitio para ninguna varilla.");
            }
            else
            {
                // Separación libre entre varillas contiguas, medida sobre la cuerda
                var cuerda = 2 * rPaso * Math.Sin(Math.PI / s.NVarTotal);
                var libre = cuerda - dVar;

                if (libre < 0)
                {
                    problemas.Add(
                        $"• {etiqueta}: {s.NVarTotal} varillas " +
                        $"{Varilla.Normalizar(s.DiamVarTotalEfectivo)} no caben en el " +
                        $"círculo: se traslaparían {-libre:N1} cm. Baja el número o el " +
                        "calibre, o sube el diámetro de la columna.");
                }
                else if (libre < dVar)
                {
                    // El mínimo normativo habitual es la mayor de 1.5·db y 4 cm. Se
                    // avisa con db para no atarse a una norma concreta.
                    problemas.Add(
                        $"• {etiqueta}: {s.NVarTotal} varillas " +
                        $"{Varilla.Normalizar(s.DiamVarTotalEfectivo)} quedan a " +
                        $"{libre:N1} cm libres entre sí, menos de un diámetro " +
                        $"({dVar:N1} cm). Revísalo contra la separación mínima de tu norma.");
                }
            }
        }
    }

    private static void RevisarLecho(
        List<string> problemas, string etiqueta, string lecho, int cantidad, string diametro)
    {
        if (cantidad <= 0)
        {
            return;
        }

        RevisarDiametro(problemas, etiqueta, lecho, diametro, obligatorio: true);
    }

    private static void RevisarDiametro(
        List<string> problemas, string etiqueta, string donde, string diametro, bool obligatorio)
    {
        if (string.IsNullOrWhiteSpace(diametro))
        {
            if (obligatorio)
            {
                problemas.Add($"• {etiqueta}: falta el diámetro del {donde}.");
            }

            return;
        }

        if (!Varilla.TryDiametroCm(diametro, out _))
        {
            problemas.Add(
                $"• {etiqueta}: el diámetro '{diametro}' del {donde} no se reconoce. " +
                $"Válidos: {Varilla.ClavesValidas}.");
        }
    }

    // ======================================================================
    // Vista previa de la sección
    // ======================================================================

    /// <summary>
    /// Dibuja la sección seleccionada con su geometría real: concreto, estribo,
    /// lechos y varillas laterales, a escala.
    /// </summary>
    private void DibujarVistaPrevia()
    {
        PreviewCanvas.Children.Clear();

        var s = Seleccionada;
        var ancho = PreviewCanvas.ActualWidth;
        var alto = PreviewCanvas.ActualHeight;

        if (ancho < 60 || alto < 60)
        {
            return;
        }

        // La seccion redonda se previsualiza aparte, con su propia geometria. Si
        // cayera en el camino de abajo se veria como un rectangulo con estribo
        // rectangular, o sea NO se veria lo que se va a dibujar, que es justo para lo
        // que sirve una vista previa.
        if (s is not null && s.EsCircular)
        {
            DibujarVistaPreviaCircular(s, ancho, alto);
            return;
        }

        if (s is null || s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            PreviewCanvas.Children.Add(new TextBlock
            {
                Text = "Selecciona una sección con base y altura para verla dibujada.",
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(14, 34, 0, 0)
            });
            return;
        }

        const double margen = 34;
        var escala = Math.Min((ancho - (2 * margen)) / s.BaseCm, (alto - (2 * margen)) / s.AlturaCm);
        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        var x0 = (ancho - (s.BaseCm * escala)) / 2;
        var y0 = (alto - (s.AlturaCm * escala)) / 2;

        // Del modelo (y hacia arriba) al lienzo (y hacia abajo)
        double PX(double xcm) => x0 + (xcm * escala);
        double PY(double ycm) => y0 + ((s.AlturaCm - ycm) * escala);

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));
        var negro = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        var conFondoSolido = ModoElegido == ModoSeccion.Tipo2Rellena;

        // Concreto. El fondo solido solo existe en el tipo 1, igual que en
        // AutoCAD, donde es un hatch SOLID de color ACI 9 debajo del AR-CONC.
        var relleno = conFondoSolido
            ? new SolidColorBrush(Color.FromRgb(0xD4, 0xD8, 0xDC))   // ACI 9
            : Brushes.White;

        var cx0 = PX(0);
        var cy0 = PY(s.AlturaCm);
        var cw = s.BaseCm * escala;
        var ch = s.AlturaCm * escala;

        PreviewCanvas.Children.Add(Rectangulo(cx0, cy0, cw, ch, azul, 1.6, relleno));

        // Patron AR-CONC. Se dibuja SIEMPRE, en los dos estilos: es justo el
        // rayado que faltaba en el dibujo. Va antes que estribo y varillas para
        // que estos queden encima, como las islas del hatch real.
        DibujarPatronConcreto(cx0, cy0, cw, ch);

        var rec = s.RecubrimientoCm;
        Varilla.TryDiametroCm(s.Estribo, out var de);

        // Estribo: dos contornos, como en la macro
        if (rec > 0 && rec * 2 < s.BaseCm && rec * 2 < s.AlturaCm)
        {
            var i = rec + de;
            var hayInterior = de > 0 && i * 2 < s.BaseCm && i * 2 < s.AlturaCm;

            // En el tipo 1, el cuerpo del estribo es un hatch SOLID
            // entre las dos fronteras. Se representa como un anillo relleno.
            if (conFondoSolido && hayInterior)
            {
                PreviewCanvas.Children.Add(Anillo(
                    PX(rec), PY(s.AlturaCm - rec),
                    (s.BaseCm - (2 * rec)) * escala, (s.AlturaCm - (2 * rec)) * escala,
                    (de * escala),
                    new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x7B)),   // ACI 152
                    negro));
            }
            else
            {
                var trazo = conFondoSolido ? negro : gris;

                PreviewCanvas.Children.Add(Rectangulo(PX(rec), PY(s.AlturaCm - rec),
                    (s.BaseCm - (2 * rec)) * escala, (s.AlturaCm - (2 * rec)) * escala,
                    trazo, 1.4, null));

                if (hayInterior)
                {
                    PreviewCanvas.Children.Add(Rectangulo(PX(i), PY(s.AlturaCm - i),
                        (s.BaseCm - (2 * i)) * escala, (s.AlturaCm - (2 * i)) * escala,
                        trazo, 1.0, null));
                }
            }
        }

        // EL GANCHO SÍSMICO DEL ESTRIBO, que es lo que faltaba.
        //
        // Va antes de los lechos para que la varilla de la esquina quede ENCIMA de su
        // doblez, igual que en AutoCAD: el gancho se dobla alrededor de esa varilla, así
        // que la varilla tapa la parte del doblez que le pasa por debajo.
        DibujarGanchoPrevio(s, de, rec, escala, PX, PY, conFondoSolido ? negro : gris);

        // Lechos
        DibujarLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec, escala, PX, PY, arriba: true, intermedio: false);
        DibujarLecho(s, s.NIntSup, s.DiamIntSupEfectivo, de, rec, escala, PX, PY, arriba: true, intermedio: true);
        DibujarLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec, escala, PX, PY, arriba: false, intermedio: false);
        DibujarLecho(s, s.NIntInf, s.DiamIntInfEfectivo, de, rec, escala, PX, PY, arriba: false, intermedio: true);

        // Varillas laterales, a los dos lados. Las posiciones salen del mismo sitio que las
        // usa el diamante para rodearlas: si se calcularan dos veces, el rombo podría
        // acabar rodeando una varilla que no es la que se ve dibujada.
        foreach (var (x, y, r) in PosicionesLaterales(s, de, rec))
        {
            Barra(PX(x), PY(y), r * escala);
        }

        // EL ESTRIBO DIAMANTE, encima de las varillas.
        //
        // Va al final y por encima, igual que en AutoCAD, donde las dos cintas se suben al
        // frente con AlFrente: el diamante es lo último que se arma y pasa por delante de
        // las varillas que abraza.
        DibujarDiamantePrevio(s, de, rec, escala, PX, PY, conFondoSolido ? negro : gris);

        // Cotas de referencia
        Etiqueta($"{s.BaseCm:N0} cm", x0 + (s.BaseCm * escala / 2) - 22, y0 + (s.AlturaCm * escala) + 8);
        Etiqueta($"{s.AlturaCm:N0} cm", x0 + (s.BaseCm * escala) + 8, y0 + (s.AlturaCm * escala / 2) - 8);

        // La MISMA linea de titulo que la circular: elemento, ID y resumen del
        // armado. Antes la rectangular solo decia elemento e ID, asi que las dos
        // formas no se veian igual.
        Etiqueta(TituloVistaPrevia(s), 14, 26);

        // El alzado va a la derecha de la sección, en el espacio que sobra
        DibujarAlzadoPrevio(s, x0 + (s.BaseCm * escala) + 70, alto);
    }

    /// <summary>
    /// Vista previa de la sección <b>circular</b>.
    /// </summary>
    /// <remarks>
    /// Usa las MISMAS fórmulas que <c>SeccionDrawer.Circular</c>: el radio de paso
    /// resta recubrimiento, diámetro del zuncho y <b>radio</b> de la varilla, y el
    /// reparto arranca arriba y gira en sentido antihorario. Tienen que coincidir, o
    /// la vista previa estaría mintiendo, que es peor que no tenerla.
    /// </remarks>
    private void DibujarVistaPreviaCircular(SeccionConcretoRow s, double ancho, double alto)
    {
        if (s.DiametroCm <= 0)
        {
            PreviewCanvas.Children.Add(new TextBlock
            {
                Text = "La sección es circular: pon el diámetro en la columna «Base cm».",
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(14, 34, 0, 0)
            });
            return;
        }

        const double margen = 34;

        // Se reserva la mitad derecha para el alzado, igual que en la rectangular
        var escala = Math.Min((ancho * 0.45) / s.DiametroCm, (alto - (2 * margen)) / s.DiametroCm);

        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        var r = s.DiametroCm * escala / 2;
        var cx = margen + r;
        var cy = alto / 2;

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));
        var negro = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        var conFondoSolido = ModoElegido == ModoSeccion.Tipo2Rellena;

        var relleno = conFondoSolido
            ? new SolidColorBrush(Color.FromRgb(0xD4, 0xD8, 0xDC))
            : Brushes.White;

        // ---------- Concreto ----------
        PreviewCanvas.Children.Add(Circunferencia(cx, cy, r, azul, 1.6, relleno));

        var rec = s.RecubrimientoCm * escala;
        Varilla.TryDiametroCm(s.Estribo, out var deCm);
        var dZun = deCm * escala;

        // ---------- Zuncho ----------
        var rZunExt = r - rec;
        var rZunInt = rZunExt - dZun;

        if (rZunInt > 0)
        {
            var trazo = conFondoSolido ? negro : gris;

            PreviewCanvas.Children.Add(Circunferencia(cx, cy, rZunExt, trazo, 1.4, null));
            PreviewCanvas.Children.Add(Circunferencia(cx, cy, rZunInt, trazo, 1.0, null));
        }

        // ---------- Varillas ----------
        Varilla.TryDiametroCm(s.DiamVarTotalEfectivo, out var dVarCm);
        var dVar = dVarCm * escala;
        var rPaso = r - rec - dZun - (dVar / 2);

        if (s.NVarTotal > 0 && rPaso > 0 && dVar > 0)
        {
            for (var i = 0; i < s.NVarTotal; i++)
            {
                // Arriba y antihorario. En el lienzo la Y baja, asi que el seno va
                // con signo NEGATIVO: sin eso el reparto sale girado al reves y no
                // coincidiria con el de AutoCAD.
                var a = (Math.PI / 2) + (i * 2 * Math.PI / s.NVarTotal);

                Barra(cx + (rPaso * Math.Cos(a)), cy - (rPaso * Math.Sin(a)), dVar / 2);
            }
        }

        // ---------- El gancho sísmico del zuncho ----------
        // Va DESPUÉS de las varillas, al contrario que en la rectangular. Ahí el doblez
        // pasa por detrás de la varilla de la esquina; aquí el que se dibuja es solo el
        // arco EXTERIOR del doblez, que va por delante, corrido hasta hacerse tangente al
        // paño del zuncho. Es lo que hace que el gancho se lea como continuación del
        // zuncho y no como una pieza pegada encima.
        DibujarGanchoZunchoPrevio(
            s, cx, cy, r, rec, dZun, dVar, rPaso, escala,
            conFondoSolido ? negro : gris);

        // ---------- Etiquetas ----------
        Etiqueta($"\u00D8 {s.DiametroCm:N0} cm", cx - 26, cy + r + 8);
        Etiqueta(TituloVistaPrevia(s), 14, 26);

        DibujarAlzadoPrevio(s, cx + r + 70, alto);
    }

    /// <summary>
    /// El <b>gancho sísmico del zuncho</b> en la vista previa de la sección circular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Misma geometría que <c>SeccionDrawer.GanchoDelZuncho</c>, y las mismas cuatro
    /// decisiones que allí están razonadas:
    /// </para>
    /// <list type="number">
    /// <item>Se agarra de la varilla <b>de abajo</b>, para no pisarse con la llamada de
    /// varillas, que apunta a la de arriba.</item>
    /// <item>La cola es el radio <b>hacia dentro girado 45°</b>, que es lo que hace los 135°
    /// del gancho de norma; las dos normales de arranque son sus perpendiculares. No se
    /// escriben a mano como en la rectangular porque aquí la varilla puede estar en
    /// cualquier ángulo.</item>
    /// <item>Del doblez se dibuja <b>solo el arco exterior</b>: el interior tiene el radio de
    /// la varilla y su mismo centro, o sea que <i>es</i> la circunferencia de la varilla, que
    /// ya está dibujada.</item>
    /// <item>Y ese arco arranca en la <b>tangencia</b> con el paño exterior del zuncho —que
    /// cae exactamente en la dirección centro → varilla, porque <c>rPaso + rOut = r − rec</c>—
    /// en lugar de donde entra en la banda.</item>
    /// </list>
    /// <para>
    /// <b>Las cuentas van en el sistema del DIBUJO, con la Y hacia arriba</b>, y la vuelta al
    /// lienzo se hace solo al pintar cada punto. No es un capricho: el lienzo tiene la Y al
    /// revés, y ahí «girar el radio 45°» gira para el otro lado, así que el gancho saldría
    /// espejeado —sigue siendo de 135°, pero apuntando al lado contrario que en AutoCAD—.
    /// Una vista previa que enseña el gancho del otro lado es exactamente lo que no puede
    /// hacer.
    /// </para>
    /// </remarks>
    /// <param name="escala">Píxeles por centímetro, para el largo del gancho.</param>
    private void DibujarGanchoZunchoPrevio(
        SeccionConcretoRow s, double cx, double cy, double r, double rec,
        double dZun, double dVar, double rPaso, double escala, Brush trazo)
    {
        var rZunInt = r - rec - dZun;

        if (s.GanchoCm <= 0 || dZun <= 0 || dVar <= 0 || rPaso <= 0 || rZunInt <= 0
            || s.NVarTotal <= 0)
        {
            return;
        }

        // Del sistema del dibujo —centro de la sección en el origen, Y hacia arriba— al
        // lienzo. Todo lo de abajo está en el primero.
        double PX(double x) => cx + x;
        double PY(double y) => cy - y;

        // La varilla de ABAJO de las que se reparten. El reparto arranca arriba y gira
        // antihorario, igual que el del dibujante y que el de las varillas de más arriba.
        double bx = 0, by = 0;
        var primera = true;

        for (var i = 0; i < s.NVarTotal; i++)
        {
            var a = (Math.PI / 2) + (i * 2 * Math.PI / s.NVarTotal);

            var x = rPaso * Math.Cos(a);
            var y = rPaso * Math.Sin(a);

            if (primera || y < by)
            {
                bx = x;
                by = y;
                primera = false;
            }
        }

        // El radio HACIA DENTRO, normalizado. El centro es el origen, así que es −(bx, by).
        var rl = Math.Sqrt((bx * bx) + (by * by));

        if (rl < 1e-9)
        {
            return;
        }

        var rx = -bx / rl;
        var ry = -by / rl;

        const double rt2I = 0.707106781186547;

        // La cola: el radio interior girado 45°. Y las normales, sus perpendiculares.
        var ux = (rx - ry) * rt2I;
        var uy = (rx + ry) * rt2I;

        var n1X = -uy;
        var n1Y = ux;
        var n2X = uy;
        var n2Y = -ux;

        var rIn = dVar / 2;
        var rOut = rIn + dZun;

        var largo = s.GanchoCm * escala;

        // El tope del núcleo, igual que en el dibujante: la proyección del vector
        // arranque → centro sobre la propia cola. Más allá de ahí la punta ya se está
        // alejando del eje por el otro lado.
        var piX = bx + (rIn * n1X);
        var piY = by + (rIn * n1Y);

        var tope = (-piX * ux) + (-piY * uy);

        if (tope > 0 && largo > tope)
        {
            largo = tope;
        }

        if (largo <= 0)
        {
            return;
        }

        // ---------- El arco exterior del doblez ----------
        // De la tangencia con el paño del zuncho al arranque de la segunda cola. La
        // tangencia cae en la dirección centro → varilla, que es la contraria al radio
        // interior.
        var aTangente = Math.Atan2(-ry, -rx);
        var a1 = Math.Atan2(n1Y, n1X);

        var barrido = a1 + Math.PI - aTangente;

        while (barrido < 0)
        {
            barrido += 2 * Math.PI;
        }

        var arco = new PointCollection();

        for (var k = 0; k <= 28; k++)
        {
            var a = aTangente + (k / 28.0 * barrido);

            arco.Add(new Point(
                PX(bx + (rOut * Math.Cos(a))), PY(by + (rOut * Math.Sin(a)))));
        }

        PreviewCanvas.Children.Add(new Polyline
        {
            Points = arco,
            Stroke = trazo,
            StrokeThickness = 1.2
        });

        // ---------- Las dos colas ----------
        // Las DOS, también en hélice: el remate de un zuncho se representa con sus dos
        // ganchos, uno encima del otro, sea espiral o anillo.
        foreach (var (nx, ny) in new[] { (n1X, n1Y), (n2X, n2Y) })
        {
            var pInX = bx + (rIn * nx);
            var pInY = by + (rIn * ny);
            var pOutX = bx + (rOut * nx);
            var pOutY = by + (rOut * ny);

            var qInX = pInX + (largo * ux);
            var qInY = pInY + (largo * uy);
            var qOutX = pOutX + (largo * ux);
            var qOutY = pOutY + (largo * uy);

            foreach (var (x1, y1, x2, y2) in new[]
            {
                (pInX, pInY, qInX, qInY),
                (pOutX, pOutY, qOutX, qOutY),
                (qInX, qInY, qOutX, qOutY)
            })
            {
                PreviewCanvas.Children.Add(new Line
                {
                    X1 = PX(x1), Y1 = PY(y1),
                    X2 = PX(x2), Y2 = PY(y2),
                    Stroke = trazo,
                    StrokeThickness = 1.2
                });
            }
        }
    }

    /// <summary>
    /// Línea de título de la vista previa: elemento, ID y resumen del armado.
    /// </summary>
    /// <remarks>
    /// Es la <b>misma</b> para las dos formas, y por eso vive aquí en lugar de estar
    /// escrita dos veces. Se muestra el nombre de <b>captura</b> —«COLUMNA
    /// CIRCULAR»— y no el de rótulo, porque en la pantalla lo que se quiere confirmar
    /// es que la fila está capturada como se pretende; en el plano sí sale «COLUMNA».
    /// </remarks>
    private static string TituloVistaPrevia(SeccionConcretoRow s)
    {
        var cabeza = $"{s.Elemento}  {s.Id}";

        if (s.EsCircular)
        {
            // Zuncho solo si se pidio zuncho: sin la casilla son ESTRIBOS. Misma regla que
            // el plano -Estribos.EsZuncho-, para que la pantalla y el papel no se
            // contradigan.
            var transversal = Estribos.EsZuncho(s.EsCircular, s.EsZunchoHelicoidal)
                ? "zuncho helicoidal"
                : "estribos";

            return $"{cabeza}   ({s.NVarTotal} vars. " +
                   $"{Varilla.Normalizar(s.DiamVarTotalEfectivo)}, {transversal})";
        }

        var total = s.TotalVarillas;
        var estribo = Varilla.Normalizar(s.Estribo);

        return total > 0
            ? $"{cabeza}   ({total} vars., estribo {estribo})"
            : $"{cabeza}   (estribo {estribo})";
    }

    /// <summary>
    /// El zuncho helicoidal en el alzado de la vista previa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usa la <b>misma</b> geometría que <c>AlzadoDrawer.HeliceDelZuncho</c>: la
    /// proyección de una hélice es un seno de amplitud igual al radio del zuncho, y la
    /// fase se acumula con el paso de cada zona L/4-L/2-L/4 en lugar de con un periodo
    /// fijo. Si aquí se dibujara un seno de paso constante, la vista previa no
    /// mostraría el cierre del zuncho en los extremos, que es justo lo que se quiere
    /// comprobar antes de mandar el dibujo.
    /// </para>
    /// <para>
    /// Se dibujan las <b>dos caras</b> de la barra, con amplitudes <c>r ± d/2</c> y la
    /// misma fase, para que se vea con su grosor real y no como una línea.
    /// </para>
    /// </remarks>
    private void DibujarHelicePrevia(
        AlzadoCad a, double izquierda, double top, double w, double h,
        double rec, double dEst, Brush brocha)
    {
        if (w <= 0 || h <= 0 || dEst <= 0)
        {
            return;
        }

        var yMedio = top + (h / 2);
        var rExt = (h / 2) - rec;
        var rEje = rExt - (dEst / 2);

        if (rEje <= 0)
        {
            return;
        }

        // Separaciones en metros, y de ahí a píxeles del lienzo
        var s = a.SeparacionesCm;
        var esc = w / (a.LongitudM > 0 ? a.LongitudM : 3.0);

        double PasoPx(int i)
        {
            var cm = i < s.Length && s[i] > 0 ? s[i] : 15;
            return cm / 100.0 * esc;
        }

        var p1 = PasoPx(0);
        var p2 = PasoPx(1);
        var p3 = PasoPx(2);

        if (p1 <= 0 || p2 <= 0 || p3 <= 0)
        {
            return;
        }

        var z1 = izquierda + (w * 0.25);
        var z2 = izquierda + (w * 0.75);

        double PasoEn(double x) => x < z1 ? p1 : x < z2 ? p2 : p3;

        var vueltas = ((z1 - izquierda) / p1) + ((z2 - z1) / p2)
                      + ((izquierda + w - z2) / p3);

        if (vueltas <= 0)
        {
            return;
        }

        // 12 puntos por vuelta bastan en pantalla: el lienzo tiene unos pocos
        // cientos de píxeles y más puntos no se distinguen.
        var n = Math.Clamp((int)Math.Ceiling(vueltas * 12), 8, 1200);
        var dx = w / n;

        var caraExt = new PointCollection();
        var caraInt = new PointCollection();

        var fase = 0d;

        for (var i = 0; i <= n; i++)
        {
            var x = izquierda + (i * dx);

            if (i > 0)
            {
                fase += 2 * Math.PI * dx / PasoEn(x - (dx / 2));
            }

            var sen = Math.Sin(fase);

            caraExt.Add(new Point(x, yMedio + ((rEje + (dEst / 2)) * sen)));
            caraInt.Add(new Point(x, yMedio + ((rEje - (dEst / 2)) * sen)));
        }

        foreach (var cara in new[] { caraExt, caraInt })
        {
            PreviewCanvas.Children.Add(new Polyline
            {
                Points = cara,
                Stroke = brocha,
                StrokeThickness = 0.9
            });
        }
    }

    /// <summary>Una circunferencia en el lienzo de la vista previa.</summary>
    private static System.Windows.Shapes.Ellipse Circunferencia(
        double cx, double cy, double r, Brush trazo, double grosor, Brush? relleno)
    {
        var e = new System.Windows.Shapes.Ellipse
        {
            Width = 2 * r,
            Height = 2 * r,
            Stroke = trazo,
            StrokeThickness = grosor,
            Fill = relleno
        };

        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    /// <summary>
    /// Vista previa del alzado, con el reparto real de estribos.
    /// </summary>
    /// <remarks>
    /// Usa <see cref="Estribos.Centros"/>, la <b>misma</b> aritmética que el dibujo de
    /// AutoCAD. Así el reparto por zonas L/4-L/2-L/4, los estribos de frontera y la
    /// separación mínima se ven aquí antes de mandar nada a AutoCAD.
    /// </remarks>
    private void DibujarAlzadoPrevio(SeccionConcretoRow s, double izquierda, double alto)
    {
        // Mismo filtro que al dibujar: si este elemento no lleva alzado, la vista
        // previa lo dice en lugar de mostrar uno que nunca se va a generar.
        if (TipoDe(s.Elemento, s.Id) is null)
        {
            Etiqueta($"{s.Elemento} no lleva alzado.", izquierda, (alto / 2) - 10);
            Etiqueta("Solo trabes, contratrabes, columnas y dados.",
                izquierda, (alto / 2) + 8);
            return;
        }

        var a = AFormatoAlzado(s);

        var largo = a.LongitudM > 0
            ? a.LongitudM
            : a.EsVertical
                ? (a.Tipo == TipoElemento.Dado ? 1.0 : 3.0)
                : Estribos.LongitudFlexible(
                    a.SeparacionesCm[0] / 100, a.SeparacionesCm[1] / 100, a.SeparacionesCm[2] / 100);

        // El alzado se dibuja siempre tendido: para una columna es el mismo dibujo
        // girado, y en un panel ancho y bajo se lee mejor así.
        var anchoDisp = PreviewCanvas.ActualWidth - izquierda - 20;
        var peralteM = (a.EsVertical ? (a.BaseCm > 0 ? a.BaseCm : a.AlturaCm) : a.AlturaCm) / 100.0;

        if (anchoDisp < 60 || largo <= 0 || peralteM <= 0)
        {
            return;
        }

        // El alzado se estira a lo LARGO: manda el ancho disponible y el peralte solo
        // limita si de verdad no cabe de alto. Antes el tope era 0.55 del alto y en un
        // elemento de poco peralte era ESE tope el que mandaba, así que el alzado
        // salía corto y apretado con media pantalla vacía a la derecha.
        var esc = Math.Min(anchoDisp / largo, (alto * 0.92) / peralteM);
        if (esc <= 0 || double.IsInfinity(esc))
        {
            return;
        }

        var w = largo * esc;
        var h = peralteM * esc;
        var top = (alto - h) / 2;

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));
        var relleno = a.Modo == ModoSeccion.Tipo2Rellena
            ? new SolidColorBrush(Color.FromRgb(0xD4, 0xD8, 0xDC))
            : Brushes.White;

        // Concreto
        PreviewCanvas.Children.Add(Rectangulo(izquierda, top, w, h, azul, 1.4, relleno));
        DibujarPatronConcreto(izquierda, top, w, h);

        var rec = a.RecubrimientoCm / 100.0 * esc;
        var dEst = a.EstriboDibujo.Cm / 100.0 * esc;
        if (dEst < 1.5) { dEst = 1.5; }

        // Estribos: mismas posiciones que en AutoCAD
        // La MISMA función que usa el dibujo de AutoCAD, reglas del elemento
        // incluidas. Ver Estribos.CentrosDeAlzado.
        var centros = Estribos.CentrosDeAlzado(
            largo,
            a.SeparacionesCm[0] / 100, a.SeparacionesCm[1] / 100, a.SeparacionesCm[2] / 100,
            vertical: a.EsVertical,
            esColumna: a.Tipo == TipoElemento.Columna);

        var brochaEst = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xB2));

        if (a.Circular && a.ZunchoHelicoidal)
        {
            // El zuncho helicoidal NO son capsulas repetidas: es una sola pieza que
            // sube en helice. Dibujarlo como estribos sueltos aqui haria que la vista
            // previa mostrara una cosa y AutoCAD otra, que es lo peor que puede hacer
            // una vista previa.
            DibujarHelicePrevia(a, izquierda, top, w, h, rec, dEst, brochaEst);
        }
        else
        {
            foreach (var c in centros)
            {
                var xc = izquierda + (c * esc);

                // La cápsula: rectángulo con las puntas redondeadas
                PreviewCanvas.Children.Add(new Rectangle
                {
                    Width = Math.Max(dEst, 1.5),
                    Height = Math.Max(h - (2 * rec) + (2 * dEst), 2),
                    RadiusX = dEst / 2,
                    RadiusY = dEst / 2,
                    Stroke = brochaEst,
                    StrokeThickness = 0.7,
                    Fill = a.Modo == ModoSeccion.Tipo2Rellena
                        ? new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x7B))
                        : null,
                    Margin = new Thickness(xc - (dEst / 2), top + rec - dEst, 0, 0)
                });
            }
        }

        // ---------- Varillas, con su GANCHO SISMICO ----------
        //
        // El gancho se dibuja aquí con la MISMA regla que en AutoCAD, sacada de
        // Estribos: en la trabe mide 12 diámetros y en la columna es el valor de la
        // columna T; y si no cabe ni un diámetro, no se dibuja. Antes la vista previa
        // dibujaba la varilla como una raya recta, así que el gancho —que es lo que
        // el usuario está ajustando en la casilla— era justo lo único que no se veía.
        var ganchoM = a.GanchoCm / 100.0;

        void BarraDeAlzado(double yCentro, double dCm, bool dobleHaciaAbajo, double disponibleM)
        {
            var dM = dCm / 100.0;
            var grosor = Math.Max(dM * esc, 1.4);
            var verde = new SolidColorBrush(Color.FromRgb(0x1D, 0x8A, 0x4E));

            var xIni = izquierda + rec;
            var xFin = izquierda + w - rec;

            PreviewCanvas.Children.Add(new Line
            {
                X1 = xIni, Y1 = yCentro,
                X2 = xFin, Y2 = yCentro,
                Stroke = verde,
                StrokeThickness = grosor
            });

            if (ganchoM <= 0)
            {
                return;
            }

            var gM = Estribos.GanchoEfectivo(
                Estribos.GanchoNominal(a.EsVertical, ganchoM, dM), disponibleM, dM);

            if (gM <= 0)
            {
                return;
            }

            // El gancho va hacia DENTRO de la pieza: el del lecho superior baja y el
            // del inferior sube. Al revés saldría del concreto.
            var g = gM * esc * (dobleHaciaAbajo ? 1 : -1);

            foreach (var x in new[] { xIni, xFin })
            {
                PreviewCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = yCentro,
                    X2 = x, Y2 = yCentro + g,
                    Stroke = verde,
                    StrokeThickness = grosor
                });
            }
        }

        var dSupCm = a.Superior.Esquina.Cm > 0 ? a.Superior.Esquina.Cm : 0.95;
        var dInfCm = a.Inferior.Esquina.Cm > 0 ? a.Inferior.Esquina.Cm : 0.95;

        var recM = a.RecubrimientoCm / 100.0;
        var ySupM = peralteM - recM - (dSupCm / 200.0);
        var yInfM = recM + (dInfCm / 200.0);

        // Lo que cabe para cada gancho: del borde de la varilla al recubrimiento
        // opuesto. Es el mismo recorte que hace la macro con maxSup y maxInf.
        var libreSup = ySupM - (dSupCm / 200.0) - recM;
        var libreInf = peralteM - recM - (yInfM + (dInfCm / 200.0));

        BarraDeAlzado(top + rec + (dSupCm / 100.0 * esc / 2), dSupCm,
            dobleHaciaAbajo: true, disponibleM: libreSup);

        BarraDeAlzado(top + h - rec - (dInfCm / 100.0 * esc / 2), dInfCm,
            dobleHaciaAbajo: false, disponibleM: libreInf);

        Etiqueta($"ALZADO  {a.TipoTexto}  {a.Id}", izquierda, top - 20);

        var textoGancho = ganchoM > 0
            ? $"   ·   gancho {a.GanchoCm:N0} cm"
            : "   ·   sin gancho";

        Etiqueta($"L = {largo:N2} m   ·   {centros.Count} estribos   ·   " +
                 $"{a.SeparacionesCm[0]:N0}-{a.SeparacionesCm[1]:N0}-{a.SeparacionesCm[2]:N0} cm" +
                 textoGancho,
            izquierda, top + h + 8);
    }

    private void DibujarLecho(
        SeccionConcretoRow s, int cantidad, string diametro, double de, double rec,
        double escala, Func<double, double> px, Func<double, double> py,
        bool arriba, bool intermedio)
    {
        foreach (var (x, y, r) in PosicionesDeLecho(s, cantidad, diametro, de, rec, arriba,
                                                   intermedio))
        {
            Barra(px(x), py(y), r * escala);
        }
    }

    /// <summary>
    /// Dónde van las varillas de un lecho, en <b>centímetros</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está separado del dibujo porque lo necesitan <b>dos</b> cosas: pintar las varillas y
    /// armar el recorrido del <b>estribo diamante</b>, que se abraza a las varillas
    /// centrales. Con el reparto escrito dentro del pintado, la vista previa tendría que
    /// calcularlo dos veces y el diamante podría acabar abrazando una varilla que no es la
    /// que se ve dibujada.
    /// </para>
    /// <para>
    /// Es el mismo reparto del dibujante: el lecho <b>de esquina</b> va de paño a paño y el
    /// <b>intermedio</b> queda ENTRE las de esquina, con un paso más.
    /// </para>
    /// </remarks>
    private static List<(double X, double Y, double R)> PosicionesDeLecho(
        SeccionConcretoRow s, int cantidad, string diametro, double de, double rec,
        bool arriba, bool intermedio)
    {
        var salida = new List<(double X, double Y, double R)>();

        if (cantidad <= 0 || !Varilla.TryDiametroCm(diametro, out var d) || d <= 0)
        {
            return salida;
        }

        var off = rec + de + (d / 2);
        var y = arriba ? s.AlturaCm - off : off;
        var r = d / 2;

        if (cantidad == 1)
        {
            salida.Add((s.BaseCm / 2, y, r));
            return salida;
        }

        if (!intermedio)
        {
            // Lecho de esquina: repartido de off a base menos off
            var paso = (s.BaseCm - (2 * off)) / (cantidad - 1);

            for (var i = 0; i < cantidad; i++)
            {
                salida.Add((off + (i * paso), y, r));
            }

            return salida;
        }

        // Lecho intermedio: queda ENTRE las de esquina
        var p = (s.BaseCm - (2 * off)) / (cantidad + 1);

        for (var i = 1; i <= cantidad; i++)
        {
            salida.Add((off + (i * p), y, r));
        }

        return salida;
    }

    /// <summary>
    /// Dónde van las varillas <b>laterales</b>, en centímetros. A los dos costados.
    /// </summary>
    /// <remarks>
    /// Mismo reparto que el dibujante: el hueco es lo que queda entre los dos lechos, y con
    /// una sola varilla va a media altura. Hacen falta aquí porque el diamante <b>rodea</b>
    /// las laterales que le quedan en el camino y <b>dobla</b> sobre la más centrada de cada
    /// costado, así que sin ellas el rombo saldría distinto del que dibuja AutoCAD.
    /// </remarks>
    private static List<(double X, double Y, double R)> PosicionesLaterales(
        SeccionConcretoRow s, double de, double rec)
    {
        var salida = new List<(double X, double Y, double R)>();

        if (s.NInter <= 0 || !Varilla.TryDiametroCm(s.DiamInter, out var dl) || dl <= 0)
        {
            return salida;
        }

        Varilla.TryDiametroCm(s.DiamEsqSup, out var dsup);
        Varilla.TryDiametroCm(s.DiamEsqInfEfectivo, out var dinf);

        var offSup = rec + de + (dsup / 2);
        var offInf = rec + de + (dinf / 2);
        var hueco = s.AlturaCm - offSup - offInf;

        if (hueco <= 0)
        {
            return salida;
        }

        var paso = s.NInter > 1 ? hueco / (s.NInter + 1) : hueco / 2;
        var offLado = rec + de + (dl / 2);

        for (var k = 1; k <= s.NInter; k++)
        {
            var y = offInf + (k * paso);

            salida.Add((offLado, y, dl / 2));
            salida.Add((s.BaseCm - offLado, y, dl / 2));
        }

        return salida;
    }

    /// <summary>
    /// El <b>estribo diamante</b> en la vista previa, con su gancho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La geometría no se calcula aquí.</b> Sale de <see cref="TrazoDiamante"/>, que es la
    /// misma clase que usa el dibujante de AutoCAD: el recorrido de círculos que abraza —con
    /// sus dobleces laterales, la regla de una o dos varillas por vértice y las laterales que
    /// hay que rodear— y las dos cintas tangentes a ellos.
    /// </para>
    /// <para>
    /// Es la razón de que esa clase exista. Un diamante no es un rombo: es una cinta tangente
    /// a una serie de círculos, y calcularla por segunda vez aquí es la manera de acabar
    /// enseñando un rombo con otro vértice, otra varilla abrazada o esquinas en pico donde el
    /// dibujo lleva dobleces redondeados.
    /// </para>
    /// <para>
    /// Los arcos de los dobleces se muestrean en tramos rectos —<c>TrazoDiamante.Muestrear</c>—
    /// porque un lienzo de WPF no tiene <i>bulges</i>.
    /// </para>
    /// </remarks>
    private void DibujarDiamantePrevio(
        SeccionConcretoRow s, double de, double rec, double escala,
        Func<double, double> px, Func<double, double> py, Brush trazo)
    {
        if (!s.LlevaDiamante || s.EsCircular)
        {
            return;
        }

        // El diámetro del diamante: el suyo si lo trae, y si no el del estribo principal.
        // Es lo que hace el dibujante al reasignar estrDia.
        if (!Varilla.TryDiametroCm(s.DiamEstriboDiamante, out var dDia) || dDia <= 0)
        {
            dDia = de;
        }

        if (dDia <= 0 || rec <= 0)
        {
            return;
        }

        // El núcleo, ya descontado el recubrimiento.
        var x1 = rec;
        var y1 = rec;
        var x2 = s.BaseCm - rec;
        var y2 = s.AlturaCm - rec;

        var varSup = new List<(double X, double Y, double R)>();
        varSup.AddRange(PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                         arriba: true, intermedio: false));
        varSup.AddRange(PosicionesDeLecho(s, s.NIntSup, s.DiamIntSupEfectivo, de, rec,
                                         arriba: true, intermedio: true));

        var varInf = new List<(double X, double Y, double R)>();
        varInf.AddRange(PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                         arriba: false, intermedio: false));
        varInf.AddRange(PosicionesDeLecho(s, s.NIntInf, s.DiamIntInfEfectivo, de, rec,
                                         arriba: false, intermedio: true));

        var varLat = PosicionesLaterales(s, de, rec);

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dDia, varSup, varInf, varLat);

        if (centros is null)
        {
            return;
        }

        // Las DOS cintas, como en el dibujo: la interior y la exterior separadas el
        // diámetro del diamante. Con una sola se vería una línea, no una varilla.
        foreach (var extra in new[] { 0.0, dDia })
        {
            var geo = TrazoDiamante.Cinta(centros, extra);

            if (geo is null)
            {
                continue;
            }

            var puntos = TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, 10);

            if (puntos.Count < 3)
            {
                continue;
            }

            var linea = new PointCollection();

            foreach (var (x, y) in puntos)
            {
                linea.Add(new Point(px(x), py(y)));
            }

            // Cerrada: la cinta del diamante es un estribo cerrado.
            linea.Add(new Point(px(puntos[0].X), py(puntos[0].Y)));

            PreviewCanvas.Children.Add(new Polyline
            {
                Points = linea,
                Stroke = trazo,
                StrokeThickness = 1.1
            });
        }

        DibujarGanchoDiamantePrevio(s, centros, dDia, px, py, trazo);
    }

    /// <summary>
    /// El <b>gancho del diamante</b> en la vista previa: doblez sobre una varilla del
    /// costado izquierdo y dos colas hacia el centro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Misma geometría que <c>SeccionDrawer.GanchoDelDiamante</c>, y las mismas dos
    /// decisiones que allí están razonadas:
    /// </para>
    /// <list type="bullet">
    /// <item>va en el costado <b>izquierdo</b>, que es donde el estribo rectangular
    /// <i>no</i> tiene el suyo —el suyo está arriba a la derecha—, para que los dos no se
    /// monten;</item>
    /// <item>y se agarra de la varilla lateral <b>más centrada</b>, que es la que el
    /// diamante ya está abrazando: el gancho remata donde el estribo dobla.</item>
    /// </list>
    /// <para>
    /// Las dos colas van con sus <b>tres líneas</b> cada una, y apuntan al centro de la
    /// sección, que es la dirección del gancho: hacia el núcleo.
    /// </para>
    /// </remarks>
    private void DibujarGanchoDiamantePrevio(
        SeccionConcretoRow s, List<(double X, double Y, double R)> centros, double dDia,
        Func<double, double> px, Func<double, double> py, Brush trazo)
    {
        if (s.GanchoCm <= 0 || centros.Count == 0)
        {
            return;
        }

        var cx = s.BaseCm / 2;
        var cy = s.AlturaCm / 2;

        // La varilla del costado IZQUIERDO más centrada de las que el diamante abraza.
        var izquierda = centros.Where(v => v.X < cx).ToList();

        if (izquierda.Count == 0)
        {
            return;
        }

        var barra = izquierda[0];
        var mejor = Math.Abs(barra.Y - cy);

        foreach (var v in izquierda)
        {
            var d = Math.Abs(v.Y - cy);

            if (d < mejor)
            {
                mejor = d;
                barra = v;
            }
        }

        var rIn = barra.R;
        var rOut = rIn + dDia;

        // La cola apunta al centro de la sección.
        var ux = cx - barra.X;
        var uy = cy - barra.Y;
        var ul = Math.Sqrt((ux * ux) + (uy * uy));

        if (ul < 1e-9)
        {
            return;
        }

        ux /= ul;
        uy /= ul;

        // Las dos normales de arranque: las perpendiculares a la cola.
        var n1X = -uy;
        var n1Y = ux;
        var n2X = uy;
        var n2Y = -ux;

        var largo = s.GanchoCm;

        // El tope hacia el núcleo, igual que en el dibujante: más allá de ahí la punta ya
        // se está alejando del eje por el otro lado.
        var piX = barra.X + (rIn * n1X);
        var piY = barra.Y + (rIn * n1Y);

        var tope = ((cx - piX) * ux) + ((cy - piY) * uy);

        if (tope > 0 && largo > tope)
        {
            largo = tope;
        }

        if (largo <= 0)
        {
            return;
        }

        // El doblez: media corona del lado OPUESTO a las colas, o sea rodeando la cara de
        // atrás de la varilla. Se dibuja su arco exterior, que es el contorno que asoma.
        var a1 = Math.Atan2(n1Y, n1X);

        var arco = new PointCollection();

        for (var k = 0; k <= 24; k++)
        {
            var a = a1 + (k / 24.0 * Math.PI);

            arco.Add(new Point(
                px(barra.X + (rOut * Math.Cos(a))), py(barra.Y + (rOut * Math.Sin(a)))));
        }

        PreviewCanvas.Children.Add(new Polyline
        {
            Points = arco,
            Stroke = trazo,
            StrokeThickness = 1.1
        });

        // Las dos colas, con sus tres líneas cada una.
        foreach (var (nx, ny) in new[] { (n1X, n1Y), (n2X, n2Y) })
        {
            var pInX = barra.X + (rIn * nx);
            var pInY = barra.Y + (rIn * ny);
            var pOutX = barra.X + (rOut * nx);
            var pOutY = barra.Y + (rOut * ny);

            var qInX = pInX + (largo * ux);
            var qInY = pInY + (largo * uy);
            var qOutX = pOutX + (largo * ux);
            var qOutY = pOutY + (largo * uy);

            foreach (var (x1, y1, x2, y2) in new[]
            {
                (pInX, pInY, qInX, qInY),
                (pOutX, pOutY, qOutX, qOutY),
                (qInX, qInY, qOutX, qOutY)
            })
            {
                PreviewCanvas.Children.Add(new Line
                {
                    X1 = px(x1), Y1 = py(y1),
                    X2 = px(x2), Y2 = py(y2),
                    Stroke = trazo,
                    StrokeThickness = 1.1
                });
            }
        }
    }

    /// <summary>
    /// El <b>gancho sísmico</b> del estribo en la vista previa, en la esquina superior
    /// derecha.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es la misma geometría que dibuja AutoCAD</b>, sacada de <c>SeccionDrawer.Ganchos</c>
    /// y de la <c>Cola</c> que usan los dos ganchos —el del estribo y el del diamante—:
    /// </para>
    /// <list type="bullet">
    /// <item>el doblez se envuelve alrededor de la varilla de la esquina, con centro a
    /// <c>rec + dEst + rIn</c> de las dos caras, y barre <b>media vuelta</b>, de 315° a
    /// 135°, que es lo que da el gancho de 135° de norma;</item>
    /// <item>del doblez salen <b>dos colas</b> hacia el núcleo, a 225°, cada una con sus
    /// <b>tres líneas</b>: la interior, la exterior y la punta que las une;</item>
    /// <item>y la segunda cola se <b>recorta</b> donde la cruza el estribo, con la misma
    /// condición del dibujante: solo si el cruce cae dentro del largo del gancho.</item>
    /// </list>
    /// <para>
    /// <b>Por qué importa que se vea.</b> El gancho es lo primero que revisa quien firma el
    /// plano —que exista, que sea de 135° y que quepa dentro de la sección— y era justo lo
    /// que la vista previa no enseñaba: se veían dos rectángulos de estribo perfectos y el
    /// gancho aparecía por primera vez en AutoCAD.
    /// </para>
    /// <para>
    /// Los arcos se muestrean en tramos rectos en lugar de usar un <c>ArcSegment</c>. A este
    /// tamaño la diferencia no se ve, y un muestreo no puede equivocarse de sentido de
    /// barrido, que es el error clásico del arco de WPF: sale el arco complementario y el
    /// gancho apunta para el otro lado.
    /// </para>
    /// </remarks>
    private void DibujarGanchoPrevio(
        SeccionConcretoRow s, double dEst, double rec, double escala,
        Func<double, double> px, Func<double, double> py, Brush trazo)
    {
        // Sin gancho no hay nada que dibujar, y sin estribo tampoco: el doblez se apoya en
        // el espesor del estribo.
        if (s.GanchoCm <= 0 || dEst <= 0 || rec <= 0)
        {
            return;
        }

        // El doblez envuelve la varilla de la ESQUINA SUPERIOR, que es la del lecho
        // superior de esquina. Si no hay varilla ahí, el gancho no tiene alrededor de qué
        // doblarse y el dibujante tampoco lo dibuja.
        if (!Varilla.TryDiametroCm(s.DiamEsqSup, out var dSup) || dSup <= 0)
        {
            return;
        }

        var rIn = dSup / 2;
        var rOut = rIn + dEst;

        var bx = s.BaseCm - rec - dEst - rIn;
        var by = s.AlturaCm - rec - dEst - rIn;

        // Que quepa: con un recubrimiento grande en una sección chica, el centro del doblez
        // se sale del núcleo y dibujarlo pondría el gancho fuera del concreto.
        if (bx <= rec + dEst || by <= rec + dEst)
        {
            return;
        }

        // Media vuelta, de 315° a 135°, pasando por la esquina. Es el sector del dibujante:
        // sectores.Add(new[] { bx, by, rIn, rOut, 1.75 * Pi, 0.75 * Pi }).
        foreach (var r in new[] { rIn, rOut })
        {
            var puntos = new PointCollection();

            for (var k = 0; k <= 24; k++)
            {
                var a = (1.75 * Math.PI) + (k / 24.0 * Math.PI);

                puntos.Add(new Point(
                    px(bx + (r * Math.Cos(a))), py(by + (r * Math.Sin(a)))));
            }

            PreviewCanvas.Children.Add(new Polyline
            {
                Points = puntos,
                Stroke = trazo,
                StrokeThickness = 1.2
            });
        }

        // Las dos colas, hacia el núcleo. Rt2I es cos(45°): la dirección es 225°.
        const double rt2I = 0.707106781186547;
        const double ux = -rt2I;
        const double uy = -rt2I;

        var largo = s.GanchoCm;

        // El recorte de la segunda cola, con la condición del dibujante: el cruce con el
        // estribo tiene que caer DENTRO del largo del gancho. Si el gancho es corto, no
        // llega a cruzarlo y no hay nada que recortar.
        var tCruce = rOut - (Math.Sqrt(2) * rIn);
        var recortar = tCruce >= 0 && tCruce <= largo;

        var colas = new[]
        {
            (Nx: rt2I, Ny: -rt2I, Recortar: false),
            (Nx: -rt2I, Ny: rt2I, Recortar: recortar)
        };

        foreach (var (nx, ny, recorta) in colas)
        {
            var piX = bx + (rIn * nx);
            var piY = by + (rIn * ny);
            var poX = bx + (rOut * nx);
            var poY = by + (rOut * ny);

            // La cola recortada arranca donde la cruza el estribo, no en la perpendicular.
            if (recorta)
            {
                poX = bx + rIn - (Math.Sqrt(2) * rOut);
                poY = by + rIn;
            }

            var qiX = piX + (largo * ux);
            var qiY = piY + (largo * uy);
            var qoX = poX + (largo * ux);
            var qoY = poY + (largo * uy);

            // Las TRES líneas de la cola: interior, exterior y la punta que las cierra.
            foreach (var (ax, ay, bx2, by2) in new[]
            {
                (piX, piY, qiX, qiY),
                (poX, poY, qoX, qoY),
                (qiX, qiY, qoX, qoY)
            })
            {
                PreviewCanvas.Children.Add(new Line
                {
                    X1 = px(ax), Y1 = py(ay),
                    X2 = px(bx2), Y2 = py(by2),
                    Stroke = trazo,
                    StrokeThickness = 1.2
                });
            }
        }
    }

    private void Barra(double cx, double cy, double radio)
    {
        var r = Math.Max(radio, 1.8);
        var c = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x7B, 0x24, 0x1B)),
            StrokeThickness = 0.8
        };
        Canvas.SetLeft(c, cx - r);
        Canvas.SetTop(c, cy - r);
        PreviewCanvas.Children.Add(c);
    }

    /// <summary>
    /// Aproximación del patrón <c>AR-CONC</c> de AutoCAD para la vista previa.
    /// </summary>
    /// <remarks>
    /// No pretende ser idéntico al patrón real: <c>AR-CONC</c> combina líneas
    /// inclinadas con áridos distribuidos al azar, y reproducirlo exactamente
    /// no aporta nada aquí. Lo que importa es que <b>se vea el rayado</b>, para
    /// poder comparar los dos estilos antes de mandar el dibujo a AutoCAD.
    /// <para>
    /// La semilla es fija a propósito: si fuera aleatoria, los áridos saltarían
    /// de sitio en cada redibujado, por ejemplo al cambiar el tamaño del panel.
    /// </para>
    /// </remarks>
    private void DibujarPatronConcreto(double left, double top, double w, double h)
    {
        if (w < 4 || h < 4)
        {
            return;
        }

        var recorte = new RectangleGeometry(new Rect(left, top, w, h));
        var tinta = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0x9C));

        // Líneas a 45°, recortadas al rectángulo de concreto
        var lineas = new GeometryGroup();
        const double paso = 9.0;

        for (var d = -h; d < w + h; d += paso)
        {
            lineas.Children.Add(new LineGeometry(
                new Point(left + d, top + h),
                new Point(left + d + h, top)));
        }

        PreviewCanvas.Children.Add(new FormaPath
        {
            Data = lineas,
            Stroke = tinta,
            StrokeThickness = 0.55,
            Clip = recorte
        });

        // Áridos: puntos dispersos, con semilla fija
        var rnd = new Random(20260817);
        var cuantos = (int)Math.Clamp(w * h / 380.0, 6, 260);
        var aridos = new GeometryGroup();

        for (var k = 0; k < cuantos; k++)
        {
            var px = left + (rnd.NextDouble() * w);
            var py = top + (rnd.NextDouble() * h);
            var r = 0.7 + (rnd.NextDouble() * 1.15);
            aridos.Children.Add(new EllipseGeometry(new Point(px, py), r, r));
        }

        PreviewCanvas.Children.Add(new FormaPath
        {
            Data = aridos,
            Fill = tinta,
            Clip = recorte
        });
    }

    /// <summary>
    /// Anillo relleno: representa el cuerpo del estribo, que en AutoCAD es un
    /// hatch <c>SOLID</c> entre la frontera exterior y la interior.
    /// </summary>
    private static FormaPath Anillo(
        double left, double top, double w, double h, double grosor,
        Brush relleno, Brush trazo)
    {
        var externo = new RectangleGeometry(new Rect(left, top, Math.Max(w, 1), Math.Max(h, 1)));

        var g = Math.Max(grosor, 0.8);
        var iw = Math.Max(w - (2 * g), 0.5);
        var ih = Math.Max(h - (2 * g), 0.5);
        var interno = new RectangleGeometry(new Rect(left + g, top + g, iw, ih));

        return new FormaPath
        {
            // EvenOdd deja hueco el interior: queda el anillo, no un bloque macizo
            Data = new GeometryGroup
            {
                FillRule = FillRule.EvenOdd,
                Children = { externo, interno }
            },
            Fill = relleno,
            Stroke = trazo,
            StrokeThickness = 0.9
        };
    }

    private static Rectangle Rectangulo(
        double left, double top, double w, double h, Brush trazo, double grosor, Brush? relleno)
    {
        var r = new Rectangle
        {
            Width = Math.Max(w, 1),
            Height = Math.Max(h, 1),
            Stroke = trazo,
            StrokeThickness = grosor,
            Fill = relleno
        };
        Canvas.SetLeft(r, left);
        Canvas.SetTop(r, top);
        return r;
    }

    private void Etiqueta(string texto, double left, double top)
    {
        var t = new TextBlock
        {
            Text = texto,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x33))
        };
        Canvas.SetLeft(t, left);
        Canvas.SetTop(t, top);
        PreviewCanvas.Children.Add(t);
    }

    // ==================================================================
    //  Tema claro / oscuro
    // ==================================================================

    /// <summary>Cambia entre el tema claro y el oscuro.</summary>
    /// <remarks>
    /// <para>
    /// El cambio en sí lo hace <see cref="Tema.Alternar"/>, mutando el color de las
    /// brochas de la paleta: eso repinta solo todo lo que las use, que es casi toda la
    /// ventana. Aquí solo quedan las dos cosas que <b>no</b> se enteran por su cuenta.
    /// </para>
    /// <para>
    /// <b>La vista previa</b>, porque su contenido no son controles con brochas de la
    /// paleta: se dibuja desde código sobre un <c>Canvas</c>, y solo se rehace al
    /// cambiar de tamaño. Sin volver a llamarla, el dibujo se quedaría con los colores
    /// del tema anterior hasta que el usuario moviera la ventana.
    /// </para>
    /// <para>
    /// <b>Y el texto del propio botón</b>, que dice a dónde se va, no dónde se está.
    /// </para>
    /// </remarks>
    private void OnCambiarTema(object sender, RoutedEventArgs e)
    {
        Tema.Alternar();

        TemaButton.Content = Tema.TextoDelBoton;

        // Los lienzos que se pintan a mano, no por estilo.
        DibujarVistaPrevia();
        RedibujarVistas();
    }
    /// <summary>Dibuja el modelo completo en 3D en AutoCAD.</summary>
    /// <remarks>
    /// <para>
    /// Cada barra va como un <b>sólido</b> con su perfil real, no como una caja ni como una
    /// línea: es lo que permite después seccionarlo, medirlo y acotarlo en AutoCAD, que es
    /// para lo que sirve tener el modelo ahí.
    /// </para>
    /// <para>
    /// El contorno de cada sección sale de <c>Perfil2D</c>, el mismo que usa la vista
    /// extruida de esta ventana. Comparten la geometría a propósito: con una copia cada uno,
    /// el visor y el dibujo acabarían mostrando perfiles distintos.
    /// </para>
    /// <para>
    /// <b>Las áreas no se extruyen aquí.</b> Un muro o una losa no es una barra con perfil:
    /// es una superficie, y su sólido se construye de otra forma. Se dicen cuántas se
    /// quedaron fuera en lugar de dibujarlas mal.
    /// </para>
    /// </remarks>
    private void OnDibujar3dCad(object sender, RoutedEventArgs e)
    {
        if (_modeloEtabs is null || _modeloEtabs.Elementos.Count == 0)
        {
            EtabsStatusText.Text =
                "Primero lee el modelo de ETABS o de SAP2000: no hay nada que dibujar.";
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new Modelo3dDrawer(doc);
            dibujante.AsegurarCapas();

            var barras = new List<Modelo3dDrawer.Barra>();
            var areas = 0;

            foreach (var el in _modeloEtabs.Elementos)
            {
                // Las areas no son barras: no tienen perfil que extruir.
                if (string.Equals(el.Forma, "AREA", StringComparison.OrdinalIgnoreCase))
                {
                    areas++;
                    continue;
                }

                var c = Perfil2D.De(
                    el.Forma, el.AnchoM, el.PeralteM, el.PatinM, el.AlmaM, el.ParedM);

                barras.Add(new Modelo3dDrawer.Barra
                {
                    P1 = new[] { el.X1, el.Y1, el.Z1 },
                    P2 = new[] { el.X2, el.Y2, el.Z2 },
                    PerfilX = c.X,
                    PerfilY = c.Y,
                    Capa = CapaDe(el.Clase),
                    Id = el.Etiqueta
                });
            }

            var r = dibujante.Dibujar(barras);

            var notas = dibujante.Notas.ToList();

            if (areas > 0)
            {
                notas.Add(
                    $"{areas} área(s) —muros y losas— no se extruyeron: no son barras con "
                    + "perfil.");
            }

            EtabsStatusText.Text =
                $"Modelo 3D dibujado en AutoCAD: {r}."
                + (notas.Count > 0
                    ? Environment.NewLine + Environment.NewLine
                      + string.Join(Environment.NewLine, notas.Select(n => "  - " + n))
                    : string.Empty);

            StatusText.Text = $"Modelo 3D en AutoCAD: {r}.";
        }
        catch (Exception ex)
        {
            EtabsStatusText.Text = "No se pudo dibujar el modelo en 3D.\n\n" + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>La capa del modelo 3D que le toca a cada tipo de elemento.</summary>
    private static string CapaDe(ClaseElemento clase) => clase switch
    {
        ClaseElemento.Columna => "MODELO3D-COLUMNAS",
        ClaseElemento.Trabe => "MODELO3D-TRABES",
        ClaseElemento.Diagonal => "MODELO3D-DIAGONALES",
        ClaseElemento.Muro => "MODELO3D-MUROS",
        ClaseElemento.Losa => "MODELO3D-LOSAS",
        _ => "MODELO3D"
    };

}
