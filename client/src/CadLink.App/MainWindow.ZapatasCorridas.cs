using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>zapatas corridas</b>: sus listas, su enlace y su vista previa.
/// </summary>
/// <remarks>
/// <para>
/// Va en un archivo parcial aparte por lo mismo que la de aisladas: es un módulo entero, con sus
/// dos familias —central y de lindero— y sus dos tipos de muro, mampostería y concreto, que no
/// dibujan lo mismo.
/// </para>
/// <para>
/// <b>Toda la geometría sale de <see cref="TrazoZapataCorrida"/></b>, la misma clase que usa el
/// dibujante de AutoCAD. Es la decisión que ya se pagó una vez con las aisladas: cuando la vista
/// previa hacía sus propias cuentas, enseñaba una zapata y el plano salía con otra.
/// </para>
/// <para>
/// La previa dibuja <b>una sola vista</b>, la sección, porque es lo único que dibujan estas dos
/// macros: no hay planta. Por eso aprovecha el cuadro entero en lugar de partirlo en dos mitades.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Llena las listas desplegables de la hoja de zapatas corridas.</summary>
    /// <remarks>
    /// Solo las de <b>lista cerrada</b> —los diámetros—. El tipo, el tipo de muro, las casillas de
    /// SI/NO y los ID de contratrabe y cadena llevan su lista en el XAML, con <c>x:Static</c>: son
    /// celdas que se pueden <b>escribir</b>, y con <c>SelectedItemBinding</c> lo teclado no llega a
    /// la propiedad.
    /// </remarks>
    private void LlenarListasZapatasCorridas()
    {
        var diametros = Varilla.DiametrosCm.Keys.ToList();

        var opcionales = new List<string> { string.Empty };
        opcionales.AddRange(diametros);

        ColZapCorVarInf.ItemsSource = diametros;
        ColZapCorVarInfT.ItemsSource = diametros;
        ColZapCorVarMuro.ItemsSource = diametros;

        // Las de la parrilla superior son opcionales: con una sola parrilla se dejan en blanco.
        ColZapCorVarSup.ItemsSource = opcionales;
        ColZapCorVarSupT.ItemsSource = opcionales;
    }

    /// <summary>
    /// Rellena las listas de <b>contratrabes</b> y <b>cadenas de desplante</b> de la hoja.
    /// </summary>
    /// <remarks>
    /// Las dos salen de la hoja de concreto, porque las dos macros las insertan <b>como bloque
    /// buscándolas por su ID</b>. Se actualizan en su sitio con la misma rutina que las listas de
    /// las aisladas: la celda apunta a esas colecciones desde el XAML, así que sustituirlas la
    /// dejaría mirando la vieja.
    /// </remarks>
    private void ActualizarListasDeZapatasCorridas()
    {
        var contratrabes = _datos.SeccionesConcreto
            .Where(s => EsContratrabe(s.Elemento))
            .Select(s => (s.Id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cadenas = _datos.SeccionesConcreto
            .Where(s => EsCadenaDeDesplante(s.Elemento))
            .Select(s => (s.Id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Refrescar(ZapataCorridaRow.ContratrabesDisponibles, contratrabes);
        Refrescar(ZapataCorridaRow.CadenasDisponibles, cadenas);
    }

    /// <summary>¿La sección es una <b>contratrabe</b>?</summary>
    /// <remarks>
    /// Se mira si <b>empieza</b> por «CONTRATRABE», igual que con los dados y las columnas de la
    /// hoja de aisladas: una capturada como «CONTRATRABE DE LIGA» tiene que salir en la lista, o
    /// el usuario acaba tecleando el ID a mano y la revisión le dice que no existe.
    /// </remarks>
    private static bool EsContratrabe(string? elemento) =>
        (elemento ?? string.Empty).Trim()
        .StartsWith("CONTRATRABE", StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Es una <b>cadena de desplante</b>?</summary>
    /// <remarks>
    /// La de <b>cerramiento</b> no cuenta: esa va arriba del muro, no en la cimentación, y
    /// ofrecerla aquí es invitar a insertar el bloque equivocado en el corte.
    /// </remarks>
    private static bool EsCadenaDeDesplante(string? elemento) =>
        (elemento ?? string.Empty).Trim()
        .StartsWith("CADENA DE DESPLANTE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Enlaza la cuadrícula de zapatas corridas.</summary>
    private void EnlazarZapatasCorridas()
    {
        ZapatasCorridasGrid.ItemsSource = _datos.ZapatasCorridas;

        _datos.ZapatasCorridas.CollectionChanged += (_, e) =>
        {
            // Cada fila avisa de sus propias ediciones: sin esto, la vista previa solo se movería
            // al agregar o quitar filas, no al escribir.
            if (e.NewItems is not null)
            {
                foreach (ZapataCorridaRow fila in e.NewItems)
                {
                    fila.PropertyChanged += OnFilaZapataCorridaEditada;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (ZapataCorridaRow fila in e.OldItems)
                {
                    fila.PropertyChanged -= OnFilaZapataCorridaEditada;
                }
            }

            ActualizarTotalesZapatasCorridas();
            DibujarVistaPreviaZapataCorrida();
        };

        foreach (var fila in _datos.ZapatasCorridas)
        {
            fila.PropertyChanged += OnFilaZapataCorridaEditada;
        }

        ActualizarTotalesZapatasCorridas();
        ActualizarListasDeZapatasCorridas();
        ActualizarGanchoDeCorridas();
    }

    /// <summary>Engancha la vista previa: se redibuja al cambiar de fila y de tamaño.</summary>
    private void EngancharVistaPreviaZapataCorrida()
    {
        ZapataCorridaPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaZapataCorrida();
        ZapatasCorridasGrid.SelectionChanged += (_, _) => DibujarVistaPreviaZapataCorrida();
    }

    private void OnFilaZapataCorridaEditada(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ActualizarTotalesZapatasCorridas();

        // Solo se redibuja si la fila editada es la que se está viendo: sin esta condición,
        // editar una fila de arriba cambiaba el dibujo de la de abajo.
        if (sender is null || ReferenceEquals(sender, ZapatasCorridasGrid.SelectedItem))
        {
            DibujarVistaPreviaZapataCorrida();
        }
    }

    /// <summary>Pone el renglón de totales de la hoja.</summary>
    private void ActualizarTotalesZapatasCorridas()
    {
        var n = _datos.ZapatasCorridas.Count;
        var centrales = _datos.ZapatasCorridas.Count(z => !z.EsLindero);
        var linderos = n - centrales;
        var deConcreto = _datos.ZapatasCorridas.Count(z => z.MuroEsConcreto);
        var incompletas = _datos.ZapatasCorridas.Count(z => z.Falta.Length > 0);

        var texto =
            $"{n} zapata(s) corrida(s)   ·   {centrales} central(es)   ·   {linderos} de lindero"
            + $"   ·   {deConcreto} con muro de concreto   ·   {n - deConcreto} de mampostería";

        if (incompletas > 0)
        {
            texto += $"   ·   {incompletas} con datos incompletos (ver la columna «Falta»)";
        }

        TotalesZapatasCorridasText.Text = texto;
    }

    /// <summary>
    /// Dice, en la casilla de arriba, con qué doblez van a salir las patas del muro.
    /// </summary>
    /// <remarks>
    /// El valor no se captura aquí: es el <b>mismo</b> de la hoja de aisladas, porque en las cuatro
    /// macros el criterio es el de los 15 diámetros y una obra no lleva dos. Se enseña para que no
    /// haya que ir a la otra pestaña a comprobarlo.
    /// </remarks>
    private void ActualizarGanchoDeCorridas()
    {
        var pedido = FactorGanchoElegido;
        var usado = TrazoZapata.FactorGanchoValido(pedido);

        // La casilla de esta hoja se pone al día con el valor del juego, sin disparar su propio
        // TextChanged: si se reescribiera siempre, cada tecla en la otra hoja movería el cursor.
        var texto = usado.ToString("0.#", CultureInfo.InvariantCulture);

        if (ZapCorGanchoBox is not null && ZapCorGanchoBox.Text.Trim() != texto)
        {
            _sincronizandoGancho = true;

            try
            {
                ZapCorGanchoBox.Text = texto;
            }
            finally
            {
                _sincronizandoGancho = false;
            }
        }

        // Con el #4, que es la varilla corriente del muro, para que el número se pueda comparar
        // con lo que se ve en el plano.
        var cm = usado * DiametroCmDeVarilla("#4");

        var hint = $"= {cm.ToString("N1", CultureInfo.CurrentCulture)} cm en una varilla del #4";

        if (pedido <= 0)
        {
            hint += "   (vacío: se usan los 15 de la macro)";
        }
        else if (Math.Abs(pedido - usado) > 1e-9)
        {
            hint += $"   (se pidió {pedido.ToString("0.#", CultureInfo.CurrentCulture)} y se "
                    + $"ajustó al rango {TrazoZapata.FactorGanchoMinimo:0.#}–"
                    + $"{TrazoZapata.FactorGanchoMaximo:0.#})";
        }

        ZapCorGanchoText.Text = hint;
    }

    /// <summary>Está copiándose el valor del doblez de una hoja a la otra.</summary>
    /// <remarks>
    /// El doblez es <b>uno</b> para toda la obra y vive en dos casillas —una por hoja—, así que
    /// cada una tiene que escribir en la otra. Sin esta bandera, esa escritura dispara el
    /// <c>TextChanged</c> de la otra, que vuelve a escribir en la primera: un ciclo sin fin en el
    /// que además el cursor salta mientras se teclea.
    /// </remarks>
    private bool _sincronizandoGancho;

    /// <summary>
    /// La casilla del doblez de <b>esta</b> hoja: escribe el valor del juego.
    /// </summary>
    /// <remarks>
    /// El valor no es de la hoja de corridas ni de la de aisladas: es de la <b>obra</b>. Se puede
    /// cambiar en cualquiera de las dos y la otra se pone al día, porque media obra a 15 diámetros
    /// y la otra media a 40 no es un plano, es un error de armado.
    /// </remarks>
    private void OnGanchoCorridaCambio(object sender, TextChangedEventArgs e)
    {
        if (!_listo || _sincronizandoGancho || ZapGanchoDiametrosBox is null)
        {
            return;
        }

        // Se copia a la casilla de la hoja de aisladas, que es la que manda en el dibujante. Su
        // TextChanged hace el resto: valida, actualiza los dos rótulos y redibuja las dos previas.
        _sincronizandoGancho = true;

        try
        {
            ZapGanchoDiametrosBox.Text = ZapCorGanchoBox.Text;
        }
        finally
        {
            _sincronizandoGancho = false;
        }

        ActualizarGanchoDeCorridas();
        DibujarVistaPreviaZapataCorrida();
    }

    // ======================================================================
    // Los dos botones de la hoja
    // ======================================================================

    /// <summary>
    /// Revisa la hoja y dice, zapata por zapata, <b>dónde se va a dibujar</b>.
    /// </summary>
    /// <remarks>
    /// Lo que <b>falta</b> por capturar y el <b>acomodo</b>: en qué x cae cada sección, que es lo
    /// que no se puede adivinar mirando la tabla porque depende del tipo y del ancho. Los ID
    /// repetidos se avisan porque el ID es el <b>nombre del bloque</b> en AutoCAD.
    /// </remarks>
    private void OnRevisarZapatasCorridas(object sender, RoutedEventArgs e)
    {
        if (!HayZapatasCorridas())
        {
            return;
        }

        RevisarZapatasCorridas(out var problemas, out var acomodo, out var bloquesSinCapturar);

        var texto = problemas.Count == 0
            ? "Las zapatas corridas están completas."
            : $"Hay {problemas.Count} cosa(s) que corregir:\n\n" + string.Join("\n", problemas);

        if (acomodo.Count > 0)
        {
            texto += "\n\nDonde se va a dibujar cada una:\n" + string.Join("\n", acomodo);
        }

        // Se DICE, no se reprocha: el bloque puede existir en el dibujo de AutoCAD sin estar
        // capturado en la hoja de concreto, y eso es corriente en un plano que viene empezado.
        if (bloquesSinCapturar.Count > 0)
        {
            texto += "\n\nBloques que no están capturados en la hoja de concreto (si ya existen "
                     + "en el dibujo de AutoCAD, se insertan igual):\n"
                     + string.Join("\n", bloquesSinCapturar);
        }

        texto += "\n\nSi está todo bien, «Dibujar zapatas corridas en AutoCAD» las pone en el "
                 + "dibujo abierto, en estas mismas posiciones.";

        MessageBox.Show(
            texto, AppInfo.ProductName, MessageBoxButton.OK,
            problemas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>¿Hay algo que revisar o dibujar? Si no, lo dice y devuelve <c>false</c>.</summary>
    private bool HayZapatasCorridas()
    {
        if (_datos.ZapatasCorridas.Count > 0)
        {
            return true;
        }

        MessageBox.Show(
            "No hay ninguna zapata corrida capturada.\n\n"
            + "Agrega una fila en la tabla: el renglón vacío del final sirve para eso.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);

        return false;
    }

    /// <summary>
    /// Comprueba las filas y calcula el acomodo. La usan el botón de revisar y el de dibujar.
    /// </summary>
    /// <returns><c>true</c> si no hay nada que corregir.</returns>
    private bool RevisarZapatasCorridas(
        out List<string> problemas, out List<string> acomodo, out List<string> bloquesSinCapturar)
    {
        problemas = new List<string>();
        acomodo = new List<string>();
        bloquesSinCapturar = new List<string>();

        var vistos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Las dos filas se cuentan por separado: el acomodo de la central va hacia la derecha y
        // el del lindero hacia la izquierda, así que cada familia lleva su propio índice.
        var iCentral = 0;
        var iLindero = 0;

        for (var i = 0; i < _datos.ZapatasCorridas.Count; i++)
        {
            var fila = _datos.ZapatasCorridas[i];
            var renglon = i + 1;

            var id = (fila.Id ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                problemas.Add($"  - Renglón {renglon}: falta el ID, que es el nombre del bloque.");
            }
            else if (vistos.TryGetValue(id, out var antes))
            {
                problemas.Add(
                    $"  - Renglón {renglon}: el ID «{id}» ya está en el renglón {antes}. "
                    + "Dos zapatas con el mismo ID se pelean por el mismo bloque en AutoCAD.");
            }
            else
            {
                vistos[id] = renglon;
            }

            var falta = fila.Falta;

            if (falta.Length > 0)
            {
                problemas.Add($"  - Renglón {renglon} «{id}»: falta {falta}.");
            }

            // Los bloques que se van a buscar en el dibujo.
            if (fila.HayContratrabe
                && !ZapataCorridaRow.ContratrabesDisponibles.Contains(
                    fila.IdContratrabe, StringComparer.OrdinalIgnoreCase))
            {
                bloquesSinCapturar.Add(
                    $"  - Renglón {renglon}: la contratrabe «{fila.IdContratrabe}».");
            }

            if (fila.MuroEsMamposteria && fila.HayCadena
                && !ZapataCorridaRow.CadenasDisponibles.Contains(
                    fila.IdCadena, StringComparer.OrdinalIgnoreCase))
            {
                bloquesSinCapturar.Add(
                    $"  - Renglón {renglon}: la cadena de desplante «{fila.IdCadena}».");
            }

            // Un muro de mampostería sin cadena se dibuja, pero conviene decirlo: sin ella el
            // enrase no tiene contra qué rematar y sube hasta el nivel de terreno.
            if (fila.MuroEsMamposteria && !fila.HayCadena)
            {
                bloquesSinCapturar.Add(
                    $"  - Renglón {renglon}: sin cadena de desplante, el muro de enrase remata "
                    + "en el nivel de terreno.");
            }

            if (falta.Length > 0 || id.Length == 0)
            {
                continue;
            }

            var indice = fila.EsLindero ? iLindero++ : iCentral++;

            var xBase = TrazoZapataCorrida.XBase(fila.Tipo, indice, fila.AnchoM);
            var eje = TrazoZapataCorrida.OffsetX(fila.Tipo, indice);

            acomodo.Add(
                $"  - «{id}» ({(fila.EsLindero ? "lindero" : "central")}): eje en x = "
                + $"{eje.ToString("N2", CultureInfo.CurrentCulture)} m, paño izquierdo en x = "
                + $"{xBase.ToString("N2", CultureInfo.CurrentCulture)} m, desplante en y = "
                + $"{(TrazoZapataCorrida.YNivelTerreno - fila.ProfundidadM).ToString("N2", CultureInfo.CurrentCulture)} m.");
        }

        return problemas.Count == 0;
    }

    /// <summary>
    /// Dibuja las zapatas corridas en el AutoCAD abierto.
    /// </summary>
    /// <remarks>
    /// Mismo camino que las aisladas: licencia, hoja con algo, revisión, conexión al AutoCAD que ya
    /// esté abierto, y el catálogo de varillas <b>pasado</b> al dibujante para que el plano y la
    /// vista previa dibujen la misma varilla del #4.
    /// </remarks>
    private void OnExportZapatasCorridas(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HayZapatasCorridas())
        {
            return;
        }

        if (!RevisarZapatasCorridas(out var problemas, out _, out _))
        {
            MessageBox.Show(
                "Corrige esto antes de dibujar las zapatas corridas:\n\n"
                + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            // El catálogo va en una VARIABLE con su tipo, no como nombre de método suelto: 'doc'
            // es dynamic, y a una llamada dinámica no se le puede pasar un grupo de métodos
            // (CS1976).
            Func<string?, double> catalogoDeVarillas = DiametroCmDeVarilla;

            var dibujante = new ZapataDrawer(doc, catalogoDeVarillas)
            {
                SeccionRellena = ModoElegido == ModoSeccion.Tipo2Rellena,
                FactorGanchoDiametros = FactorGanchoElegido
            };

            var zapatas = _datos.ZapatasCorridas.Select(f => f.AFormatoCad()).ToList();

            var r = dibujante.DibujarCorridas(zapatas);

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var resumen =
                "Listo.\n\n" + r + "\n\n"
                + "Cada zapata corrida quedó con su sección, su muro y sus cotas, en las "
                + "posiciones de tus macros.";

            var fallos = dibujante.Fallos;

            if (fallos.Count == 0)
            {
                StatusText.Text = $"Dibujadas {r.Zapatas} zapata(s) corrida(s) en AutoCAD.";

                MostrarNotas(dibujante.Notas.Count == 0
                    ? string.Empty
                    : "Notas del último dibujo:" + Environment.NewLine
                      + string.Join(Environment.NewLine,
                          dibujante.Notas.Select(n => "  - " + n)));

                MessageBox.Show(resumen, AppInfo.ProductName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var detalle = string.Join(Environment.NewLine, fallos.Select(f => "  - " + f));

                StatusText.Text =
                    $"Dibujadas {r.Zapatas} zapata(s) corrida(s), con {fallos.Count} aviso(s). "
                    + "Ver el detalle bajo la vista previa.";

                MostrarNotas(
                    "AVISOS DEL ULTIMO DIBUJO (" + fallos.Count + "):"
                    + Environment.NewLine + detalle);

                MessageBox.Show(
                    resumen + "\n\n"
                    + "PERO hubo " + fallos.Count + " fallo(s) que se toleraron, así que el "
                    + "dibujo puede estar incompleto:\n\n" + detalle
                    + "\n\nEste mismo texto queda bajo la vista previa.",
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
                "Error al dibujar las zapatas corridas en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ======================================================================
    // Vista previa: la sección
    // ======================================================================

    /// <summary>
    /// Dibuja la zapata corrida seleccionada: la <b>sección</b>, con sus rellenos y sus cotas.
    /// </summary>
    /// <remarks>
    /// Lo que se ve es lo que va a salir en el plano, con dos diferencias a propósito: no se
    /// pintan los rótulos con leader —en un cuadro de trescientos píxeles no se leen— y el hatch
    /// de terreno se pinta entero en lugar de rodear los obstáculos, porque eso es trabajo del
    /// dibujante y aquí no cambia lo que hay que revisar.
    /// </remarks>
    private void DibujarVistaPreviaZapataCorrida()
    {
        ZapataCorridaPreviewCanvas.Children.Clear();

        var ancho = ZapataCorridaPreviewCanvas.ActualWidth;
        var alto = ZapataCorridaPreviewCanvas.ActualHeight;

        if (ancho < 120 || alto < 120)
        {
            return;
        }

        if (ZapatasCorridasGrid.SelectedItem is not ZapataCorridaRow fila)
        {
            AvisoCorrida("Selecciona una zapata corrida de la tabla para verla dibujada.");
            return;
        }

        var falta = fila.Falta;

        if (falta.Length > 0)
        {
            AvisoCorrida($"No se puede dibujar todavía: falta {falta}.");
            return;
        }

        var z = fila.AFormatoCad();

        // El acomodo REAL de esta fila: cada familia lleva su propio índice, igual que en la
        // revisión y que en el dibujante.
        var indice = 0;

        for (var i = 0; i < _datos.ZapatasCorridas.Count; i++)
        {
            var otra = _datos.ZapatasCorridas[i];

            if (ReferenceEquals(otra, fila))
            {
                break;
            }

            if (otra.EsLindero == fila.EsLindero)
            {
                indice++;
            }
        }

        var xBase = TrazoZapataCorrida.XBase(z.Tipo, indice, z.AnchoM);
        var a = TrazoZapataCorrida.Colocar(z, xBase);

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));

        var arriba = 44.0;
        var abajo = 52.0;
        var lados = 18.0;

        var wUtil = ancho - (2 * lados);
        var hUtil = alto - arriba - abajo;

        if (wUtil < 80 || hUtil < 80)
        {
            AvisoCorrida("Agranda la ventana para ver la zapata corrida dibujada.");
            return;
        }

        DibujarSeccionCorridaPrevia(z, fila, a, lados, arriba, wUtil, hUtil, azul, gris);

        // ---------- Título y el dato del acomodo ----------
        var titulo = z.EsLindero
            ? $"ZAPATA DE LINDERO \"{fila.Id}\""
            : $"ZAPATA CORRIDA CENTRAL \"{fila.Id}\"";

        TextoCorrida($"{titulo}    ·    {fila.Resumen}", 12, 24, 12, azul, true);

        var bloques = new List<string>();

        if (fila.HayContratrabe)
        {
            bloques.Add($"contratrabe \"{fila.IdContratrabe}\"");
        }

        if (fila.MuroEsMamposteria && fila.HayCadena)
        {
            bloques.Add($"cadena \"{fila.IdCadena}\"");
        }

        TextoCorrida(
            $"Se dibuja con su eje en x = {a.XCentro.ToString("N2", CultureInfo.CurrentCulture)} m"
            + $"    ·    desplante en y = {a.YZapBot.ToString("N2", CultureInfo.CurrentCulture)} m"
            + (bloques.Count == 0 ? "    ·    sin bloques" : "    ·    " + string.Join(", ", bloques)),
            12, alto - 20, 10.5, gris);

        // ---------- La leyenda: qué es cada color ----------
        var partes = new List<(Brush Color, string Texto)>
        {
            (PincelConcreto, "concreto"),
            (PincelTerreno, "terreno"),
            (new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), "parrilla inf.")
        };

        if (z.DobleParrilla)
        {
            partes.Add((new SolidColorBrush(Color.FromRgb(0xE0, 0x8B, 0x7F)), "parrilla sup."));
        }

        if (fila.MuroEsConcreto)
        {
            partes.Add((new SolidColorBrush(Color.FromRgb(0x0E, 0x6E, 0xA8)), "acero del muro"));
        }
        else
        {
            partes.Add((PincelEnrase, "muro de enrase"));
        }

        LeyendaCorrida(12, alto - 36, partes.ToArray());
    }

    /// <summary>La sección, con todo lo que dibujan las macros de abajo arriba.</summary>
    private void DibujarSeccionCorridaPrevia(
        ZapataCorridaCad z, ZapataCorridaRow fila, TrazoZapataCorrida.Acomodo a,
        double left, double top, double w, double h, Brush azul, Brush gris)
    {
        // Lo que tiene que caber: del fondo de la plantilla al nivel de terreno, y de paño a paño
        // con aire para las cotas, que van a la izquierda y por debajo de la plantilla.
        const double aireCotaX = 0.28;
        const double aireCotaY = 0.22;

        var xMin = Math.Min(a.XBase, a.XMuroIzq) - aireCotaX;
        var xMax = Math.Max(a.XDer, a.XMuroDer);
        var yMin = a.YPlantillaBot - aireCotaY;
        var yMax = a.YTerreno + 0.1;

        var anchoM = Math.Max(xMax - xMin, 0.01);
        var altoM = Math.Max(yMax - yMin, 0.01);

        var esc = Math.Min(w / anchoM, h / altoM);

        var offX = left + ((w - (anchoM * esc)) / 2);
        var offY = top + ((h - (altoM * esc)) / 2);

        double PX(double x) => offX + ((x - xMin) * esc);
        double PY(double y) => offY + ((yMax - y) * esc);

        // ---------- El terreno, a los dos lados del muro ----------
        if (a.XMuroIzq > a.XBase)
        {
            RellenoCorrida(PX(a.XBase), PY(a.YTerreno), PX(a.XMuroIzq), PY(a.YZapTop), PincelTerreno);
        }

        if (a.XMuroDer < a.XDer)
        {
            RellenoCorrida(PX(a.XMuroDer), PY(a.YTerreno), PX(a.XDer), PY(a.YZapTop), PincelTerreno);
        }

        // ---------- La plantilla y la zapata ----------
        RellenoCorrida(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), PincelPlantilla);
        RellenoCorrida(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), PincelConcreto);

        ContornoCorrida(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), azul, 1.2);
        ContornoCorrida(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), gris, 0.8);

        // ---------- El muro ----------
        //
        // La contratrabe se apoya en el PAÑO DE ARRIBA DE LA PLANTILLA —el fondo de la zapata—,
        // como en las dos macros: arranca del desplante y atraviesa el espesor. Aquí no se puede
        // leer su caja, que vive en el dibujo de AutoCAD, así que se usa el alto por omisión de
        // las macros, el mismo que ellas suponen cuando el bloque no aparece.
        var yCT = fila.HayContratrabe
            ? a.YZapBot + TrazoZapataCorrida.ContratrabeAltoPorOmision
            : a.YZapTop;

        if (fila.HayContratrabe)
        {
            RellenoCorrida(PX(a.XMuroIzq), PY(yCT), PX(a.XMuroDer), PY(a.YZapBot), PincelConcreto);
            ContornoCorrida(PX(a.XMuroIzq), PY(yCT), PX(a.XMuroDer), PY(a.YZapBot), azul, 1.0);

            TextoCorrida("CT", PX(a.XCentroMuro) - 8, PY(yCT) + 2, 9, azul);
        }

        if (fila.MuroEsConcreto)
        {
            DibujarMuroDeConcretoPrevio(z, fila, a, yCT, PX, PY, azul);
        }
        else
        {
            DibujarMuroDeEnrasePrevio(fila, a, yCT, PX, PY, azul, gris);
        }

        // ---------- El nivel de terreno ----------
        RectaCorrida(PX(a.XBase), PY(a.YTerreno), PX(a.XDer), PY(a.YTerreno), gris, 1.0);

        // ---------- Las parrillas ----------
        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        var rosa = new SolidColorBrush(Color.FromRgb(0xE0, 0x8B, 0x7F));

        DibujarParrillaCorridaPrevia(z, a, PX, PY, z.VarInf, z.VarInfTrans, z.SepInfTrans, false, rojo);

        if (z.DobleParrilla)
        {
            DibujarParrillaCorridaPrevia(
                z, a, PX, PY, z.VarSup, z.VarSupTrans, z.SepSupTrans, true, rosa);
        }

        // ---------- Las cotas ----------
        CotaHCorrida(PX(a.XBase), PX(a.XDer), PY(a.YPlantillaBot) + 22, z.AnchoM, azul);
        CotaVCorrida(PX(xMin) + 10, PY(a.YTerreno), PY(a.YPlantillaBot), a.YTerreno - a.YPlantillaBot, azul);
        CotaVCorrida(PX(a.XBase) - 8, PY(a.YZapTop), PY(a.YZapBot), z.EspesorM, gris);
    }

    /// <summary>El muro de concreto: su relleno, sus círculos y sus varillas con pata.</summary>
    private void DibujarMuroDeConcretoPrevio(
        ZapataCorridaCad z, ZapataCorridaRow fila, TrazoZapataCorrida.Acomodo a,
        double yCT, Func<double, double> px, Func<double, double> py, Brush azul)
    {
        var m = TrazoZapataCorrida.ColocarMuro(a, yCT, a.YTerreno);

        if (m.YTope <= m.YBase)
        {
            return;
        }

        RellenoCorrida(px(m.XIzq), py(m.YTope), px(m.XDer), py(m.YBase), PincelConcreto);
        ContornoCorrida(px(m.XIzq), py(m.YTope), px(m.XDer), py(m.YBase), azul, 1.2);

        var diamMuro = DiametroMDeVarillaCorrida(z.VarMuro);

        if (diamMuro <= 0)
        {
            return;
        }

        var acero = new SolidColorBrush(Color.FromRgb(0x0E, 0x6E, 0xA8));

        var ejes = TrazoZapataCorrida.EjesDelAcero(m, z.MuroDobleParrilla);

        // Los círculos: las varillas que salen de punta, repartidas con la separación VERTICAL.
        var ys = TrazoZapataCorrida.CirculosDelMuro(
            m, a.YTerreno, diamMuro, TrazoZapata.SeparacionM(z.SepMuroVert));

        var r = Math.Max(diamMuro / 2 * (px(1) - px(0)), 1.2);

        // Solo se colorean con la sección RELLENA, igual que en el plano: en modo normal la
        // varilla va hueca y el rayado del concreto se ve por detrás.
        var rellenas = ModoElegido == ModoSeccion.Tipo2Rellena;

        foreach (var y in ys)
        {
            CirculoCorrida(px(ejes.X1), py(y), r, acero, rellenas);

            if (ejes.Doble)
            {
                CirculoCorrida(px(ejes.X2), py(y), r, acero, rellenas);
            }
        }

        // Las varillas verticales con su pata. La parrilla inferior manda en la altura del
        // doblez, así que se calcula con la misma rutina que el dibujante.
        var diamInf = DiametroMDeVarillaCorrida(z.VarInf);
        var diamInfT = DiametroMDeVarillaCorrida(z.VarInfTrans);

        if (diamInf <= 0 || diamInfT <= 0)
        {
            return;
        }

        var p = TrazoZapataCorrida.ParrillaEnAlzado(
            a, z.EspesorM, z.RecM, diamInf, diamInfT,
            TrazoZapata.SeparacionM(z.SepInfTrans), false);

        var yPata = TrazoZapataCorrida.YDeLaPata(
            p.YBarra, diamInf, p.YCirculos, diamInfT, diamMuro, fila.EsLindero);

        var desp = TrazoZapataCorrida.DesplazamientoDelMuro(
            DiametroMDeVarillaCorrida(z.SepMuroHoriz));

        var barras = fila.EsLindero
            ? TrazoZapataCorrida.VerticalesLindero(
                a, ejes, yPata, diamMuro, desp, z.RecM, FactorGanchoElegido)
            : TrazoZapataCorrida.VerticalesCentral(
                ejes, a.YTerreno, yPata, diamMuro, desp, FactorGanchoElegido);

        foreach (var b in barras)
        {
            RectaCorrida(px(b.X), py(b.YTop), px(b.X), py(b.YEsquina), acero, 1.6);
            RectaCorrida(px(b.X), py(b.YEsquina), px(b.XFinDoblez), py(b.YEsquina), acero, 1.6);
        }
    }

    /// <summary>El muro de enrase: sus piezas y sus juntas, como en la macro.</summary>
    private void DibujarMuroDeEnrasePrevio(
        ZapataCorridaRow fila, TrazoZapataCorrida.Acomodo a, double yCT,
        Func<double, double> px, Func<double, double> py, Brush azul, Brush gris)
    {
        // El tope es el fondo de la cadena de desplante. Sin bloque que medir, se usa el mismo
        // supuesto de las macros: la cadena baja 20 cm del nivel de terreno.
        var yTope = fila.HayCadena
            ? a.YTerreno - TrazoZapataCorrida.CadenaAltoPorOmision
            : a.YTerreno;

        var e = TrazoZapataCorrida.MuroDeEnrase(
            a.XMuroIzq, a.XMuroDer - a.XMuroIzq, yCT, yTope);

        if (e.Piezas == 0)
        {
            // No cabe la hilada: se dibuja el muro macizo, que es lo que queda en el plano.
            if (yTope > yCT)
            {
                RellenoCorrida(px(a.XMuroIzq), py(yTope), px(a.XMuroDer), py(yCT), PincelConcreto);
            }
        }
        else
        {
            foreach (var yb in e.YBases)
            {
                RellenoCorrida(
                    px(e.XIzq), py(yb + e.AltoPieza), px(e.XIzq + e.Ancho), py(yb), PincelEnrase);

                ContornoCorrida(
                    px(e.XIzq), py(yb + e.AltoPieza), px(e.XIzq + e.Ancho), py(yb), azul, 0.8);
            }
        }

        // La cadena de desplante, arriba del enrase.
        if (fila.HayCadena)
        {
            RellenoCorrida(
                px(a.XMuroIzq), py(a.YTerreno), px(a.XMuroDer), py(yTope), PincelConcreto);

            ContornoCorrida(
                px(a.XMuroIzq), py(a.YTerreno), px(a.XMuroDer), py(yTope), azul, 1.0);

            TextoCorrida("CD", px(a.XCentroMuro) - 8, py(a.YTerreno) + 2, 9, gris);
        }
    }

    /// <summary>Una parrilla de la zapata: la barra que corre y los círculos de la transversal.</summary>
    private void DibujarParrillaCorridaPrevia(
        ZapataCorridaCad z, TrazoZapataCorrida.Acomodo a,
        Func<double, double> px, Func<double, double> py,
        string? varBarra, string? varCirculos, string? sepCirculos, bool superior, Brush trazo)
    {
        var diam = DiametroMDeVarillaCorrida(varBarra);
        var diamC = DiametroMDeVarillaCorrida(varCirculos);

        if (diam <= 0)
        {
            return;
        }

        if (diamC <= 0)
        {
            diamC = diam;
        }

        var p = TrazoZapataCorrida.ParrillaEnAlzado(
            a, z.EspesorM, z.RecM, diam, diamC, TrazoZapata.SeparacionM(sepCirculos), superior);

        // La barra que corre, con sus dos ganchos: el gancho son 3 cm en las dos macros.
        RectaCorrida(px(p.XCaraIzq), py(p.YBarra), px(p.XCaraDer), py(p.YBarra), trazo, 1.6);

        var gancho = TrazoZapataCorrida.GanchoParrilla;
        var yGancho = superior ? p.YBarra - gancho : p.YBarra + gancho;

        RectaCorrida(px(p.XCaraIzq), py(p.YBarra), px(p.XCaraIzq), py(yGancho), trazo, 1.6);
        RectaCorrida(px(p.XCaraDer), py(p.YBarra), px(p.XCaraDer), py(yGancho), trazo, 1.6);

        var r = Math.Max(diamC / 2 * (px(1) - px(0)), 1.2);

        foreach (var x in p.Circulos)
        {
            CirculoCorrida(px(x), py(p.YCirculos), r, trazo);
        }
    }

    /// <summary>El diámetro de una varilla en metros, o 0 si la celda no se reconoce.</summary>
    private static double DiametroMDeVarillaCorrida(string? clave) =>
        Varilla.TryDiametroCm(clave, out var cm) ? cm / 100.0 : 0;

    // ======================================================================
    // Primitivas de la previa de corridas
    // ======================================================================
    //
    // Son las mismas de la hoja de aisladas, pero dibujando en el canvas de ESTA hoja. Se
    // repiten en lugar de tocar las de allá porque las de allá funcionan y están probadas: una
    // firma nueva con el canvas por parámetro habría obligado a cambiar las cuarenta llamadas
    // de la otra previa para arreglar algo que no está roto. Los PINCELES sí son los mismos
    // -PincelConcreto, PincelTerreno, PincelPlantilla-, que es lo que importa para que las dos
    // hojas se vean iguales.

    /// <summary>El pincel de las piezas del muro de enrase: block de cemento.</summary>
    /// <remarks>
    /// Es su propio pincel y no el del concreto porque en el plano tampoco son lo mismo: la
    /// macro pinta las piezas con su sólido 253 y las juntas con el 252.
    /// </remarks>
    private static readonly Brush PincelEnrase = Textura(
        Color.FromRgb(0xD8, 0xD2, 0xC6), Color.FromRgb(0xA8, 0x9F, 0x8E), 5, false);

    private void RellenoCorrida(double x1, double y1, double x2, double y2, Brush pincel)
    {
        var w = Math.Abs(x2 - x1);
        var h = Math.Abs(y2 - y1);

        if (w < 0.5 || h < 0.5)
        {
            return;
        }

        var r = new Rectangle { Width = w, Height = h, Fill = pincel };

        System.Windows.Controls.Canvas.SetLeft(r, Math.Min(x1, x2));
        System.Windows.Controls.Canvas.SetTop(r, Math.Min(y1, y2));

        ZapataCorridaPreviewCanvas.Children.Add(r);
    }

    private void RectaCorrida(double x1, double y1, double x2, double y2, Brush trazo, double grosor) =>
        ZapataCorridaPreviewCanvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = trazo,
            StrokeThickness = grosor
        });

    private void ContornoCorrida(
        double xIzq, double yArriba, double xDer, double yAbajo, Brush trazo, double grosor)
    {
        var w = xDer - xIzq;
        var h = yAbajo - yArriba;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        var r = new Rectangle
        {
            Width = w,
            Height = h,
            Stroke = trazo,
            StrokeThickness = grosor
        };

        System.Windows.Controls.Canvas.SetLeft(r, xIzq);
        System.Windows.Controls.Canvas.SetTop(r, yArriba);

        ZapataCorridaPreviewCanvas.Children.Add(r);
    }

    private void CirculoCorrida(double cx, double cy, double radio, Brush trazo, bool relleno = true)
    {
        var d = Math.Max(radio * 2, 2.0);

        var c = relleno
            ? new Ellipse { Width = d, Height = d, Fill = trazo }
            : new Ellipse { Width = d, Height = d, Stroke = trazo, StrokeThickness = 1.1 };

        System.Windows.Controls.Canvas.SetLeft(c, cx - (d / 2));
        System.Windows.Controls.Canvas.SetTop(c, cy - (d / 2));

        ZapataCorridaPreviewCanvas.Children.Add(c);
    }

    private void CotaHCorrida(double x1, double x2, double y, double valorM, Brush trazo)
    {
        if (Math.Abs(x2 - x1) < 6)
        {
            return;
        }

        RectaCorrida(x1, y, x2, y, trazo, 0.8);
        RectaCorrida(x1, y - 4, x1, y + 4, trazo, 0.8);
        RectaCorrida(x2, y - 4, x2, y + 4, trazo, 0.8);

        TextoCorrida(
            valorM.ToString("N2", CultureInfo.CurrentCulture),
            ((x1 + x2) / 2) - 12, y - 15, 10, trazo);
    }

    private void CotaVCorrida(double x, double y1, double y2, double valorM, Brush trazo)
    {
        if (Math.Abs(y2 - y1) < 6)
        {
            return;
        }

        RectaCorrida(x, y1, x, y2, trazo, 0.8);
        RectaCorrida(x - 4, y1, x + 4, y1, trazo, 0.8);
        RectaCorrida(x - 4, y2, x + 4, y2, trazo, 0.8);

        TextoCorrida(
            valorM.ToString("N2", CultureInfo.CurrentCulture),
            x - 26, ((y1 + y2) / 2) - 7, 10, trazo);
    }

    private void LeyendaCorrida(double left, double top, params (Brush Color, string Texto)[] partes)
    {
        var x = left;

        foreach (var (color, texto) in partes)
        {
            var chip = new Rectangle
            {
                Width = 9,
                Height = 9,
                Fill = color,
                RadiusX = 2,
                RadiusY = 2
            };

            System.Windows.Controls.Canvas.SetLeft(chip, x);
            System.Windows.Controls.Canvas.SetTop(chip, top + 3);

            ZapataCorridaPreviewCanvas.Children.Add(chip);

            TextoCorrida(texto, x + 13, top, 9.5, PinceLeyenda);

            x += 13 + (texto.Length * 5.4) + 14;
        }
    }

    private void AvisoCorrida(string texto) =>
        TextoCorrida(texto, 14, 34, 12, Brushes.Gray);

    private void TextoCorrida(
        string texto, double x, double y, double tamano, Brush color, bool negrita = false)
    {
        var t = new System.Windows.Controls.TextBlock
        {
            Text = texto,
            FontSize = tamano,
            Foreground = color,
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal
        };

        System.Windows.Controls.Canvas.SetLeft(t, x);
        System.Windows.Controls.Canvas.SetTop(t, y);

        ZapataCorridaPreviewCanvas.Children.Add(t);
    }
}
