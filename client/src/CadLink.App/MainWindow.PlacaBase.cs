using System.Windows;
using System.Windows.Input;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>placas base</b>: su tabla y el botón que las dibuja en AutoCAD.
/// </summary>
public partial class MainWindow
{
    /// <summary>La fila seleccionada, o <c>null</c> si no hay ninguna.</summary>
    private PlacaBaseRow? PlacaSeleccionada => PlacasGrid?.SelectedItem as PlacaBaseRow;

    /// <summary>Llena los desplegables de la hoja de placas base.</summary>
    /// <remarks>
    /// <para>
    /// Se llama <b>una vez</b>, desde <c>LlenarListas</c>: las listas de las columnas no dependen
    /// del proyecto abierto, así que rehacerlas al cargar otro sería trabajo perdido.
    /// </para>
    /// <para>
    /// Las familias salen de <see cref="FamiliaPerfil"/> y los aceros de
    /// <see cref="CatalogoAceros"/>, no de listas escritas a mano: son las mismas que usa la hoja
    /// de acero, así que las dos ofrecen exactamente lo mismo y no se pueden desincronizar.
    /// </para>
    /// </remarks>
    private void LlenarListasPlacaBase()
    {
        ColPlacaFamilia.ItemsSource = FamiliaPerfil.Todas;
        ColPlacaAcero.ItemsSource = CatalogoAceros.Nombres;
        ColPlacaElectrodo.ItemsSource = new[] { "E60", "E70", "E80", "E90" };

        // Las celdas en FRACCIONES —el espesor, los diámetros de ancla y de agujero, la soldadura
        // y los cartabones— no se llenan aquí: son desplegables editables y su lista sale de la
        // fila, en PlacaBaseRow. Ver la nota de DiametrosAncla.
    }

    /// <summary>
    /// Ata la cuadrícula de placas a la colección del proyecto abierto.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="LlenarListasPlacaBase"/> porque se llama también al cargar el
    /// ejemplo, al borrar todo y al empezar de nuevo: en esos tres casos <c>_datos</c> es OTRO
    /// objeto, y una cuadrícula atada en el constructor seguiría mostrando el proyecto anterior.
    /// </remarks>
    private void EnlazarPlacaBase()
    {
        PlacasGrid.ItemsSource = _datos.PlacasBase;

        // La colección avisa de filas agregadas o quitadas, pero NO de celdas editadas, así que
        // hay que escuchar cada fila: el renglón de totales sirve mientras se escribe, que es
        // cuando dice si las anclas caben.
        _datos.PlacasBase.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (Row fila in e.OldItems)
                {
                    fila.PropertyChanged -= OnFilaPlacaEditada;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (Row fila in e.NewItems)
                {
                    fila.PropertyChanged += OnFilaPlacaEditada;
                }
            }

            ActualizarTotalesPlacas();
        };

        foreach (var fila in _datos.PlacasBase)
        {
            fila.PropertyChanged += OnFilaPlacaEditada;
        }

        ActualizarTotalesPlacas();

        if (PlacasGrid.SelectedItem is null && _datos.PlacasBase.Count > 0)
        {
            PlacasGrid.SelectedIndex = 0;
        }
    }

    private void OnFilaPlacaEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        ActualizarTotalesPlacas();
    }

    /// <summary>
    /// El renglón de totales: cuántas placas, cuántas anclas y qué no se puede dibujar.
    /// </summary>
    /// <remarks>
    /// <b>El número de anclas está aquí a propósito.</b> Es el dato que se pide al proveedor y no
    /// se puede sacar mirando la tabla: cada fila reparte los suyos, y sumar las columnas «anclas
    /// X» y «anclas Y» de diez filas a mano es justo la cuenta que se hace mal.
    /// </remarks>
    private void ActualizarTotalesPlacas()
    {
        // Se llama también desde DatosCambiaron, que corre al abrir un trabajo y al deshacer. El
        // control ya existe en todos esos caminos, pero la comprobación no cuesta nada y evita que
        // reordenar el arranque tumbe la ventana con una referencia nula.
        if (TotalesPlacasText is null)
        {
            return;
        }

        var placas = _datos.PlacasBase.Count;

        if (placas == 0)
        {
            TotalesPlacasText.Text =
                "Sin placas capturadas. Usa «Agregar placa» para empezar.";
            return;
        }

        var anclas = _datos.PlacasBase.Sum(f => f.TotalAnclas);
        var conCartabones = _datos.PlacasBase.Count(f => f.ConCartabones);

        var texto = $"{placas} placa(s)  ·  {anclas} ancla(s) en total";

        if (conCartabones > 0)
        {
            texto += $"  ·  {conCartabones} con cartabones";
        }

        // LO QUE FALTA SE DICE AQUÍ Y NO SOLO AL DIBUJAR. Es la diferencia entre verlo mientras se
        // captura y verlo cuando el botón se niega.
        var incompletas = _datos.PlacasBase.Count(f => f.Falta.Length > 0);

        if (incompletas > 0)
        {
            texto += $"  ·  {incompletas} sin poder dibujarse (mira la columna «Falta»)";
        }

        TotalesPlacasText.Text = texto;
    }

    /// <summary>Agrega una placa a la hoja.</summary>
    /// <remarks>
    /// La fila nueva <b>copia la seleccionada</b> si hay una. En una nave con veinte placas iguales
    /// salvo la marca, arrancar de cero cada fila es volver a capturar veinte celdas que ya
    /// estaban capturadas.
    /// </remarks>
    private void OnAgregarPlaca(object sender, RoutedEventArgs e)
    {
        var nueva = PlacaSeleccionada is { } modelo ? Copiar(modelo) : new PlacaBaseRow();

        nueva.Marca = MarcaLibre();

        _datos.PlacasBase.Add(nueva);

        PlacasGrid.SelectedItem = nueva;
        PlacasGrid.ScrollIntoView(nueva);
    }

    /// <summary>Una marca que no esté usada: PB-1, PB-2, PB-3...</summary>
    private string MarcaLibre()
    {
        var usadas = _datos.PlacasBase
            .Select(f => f.Marca.Trim())
            .Where(m => m.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var n = 1; n <= usadas.Count + 1; n++)
        {
            var m = "PB-" + n;

            if (!usadas.Contains(m))
            {
                return m;
            }
        }

        return "PB-" + (_datos.PlacasBase.Count + 1);
    }

    /// <summary>Una copia de la fila, con todas sus celdas.</summary>
    private static PlacaBaseRow Copiar(PlacaBaseRow f) => new()
    {
        LargoCm = f.LargoCm,
        AnchoCm = f.AnchoCm,
        Espesor = f.Espesor,
        AceroPlaca = f.AceroPlaca,
        DadoXCm = f.DadoXCm,
        DadoYCm = f.DadoYCm,
        Familia = f.Familia,
        Seccion = f.Seccion,
        NAnclasX = f.NAnclasX,
        NAnclasY = f.NAnclasY,
        SepBordeXCm = f.SepBordeXCm,
        SepBordeYCm = f.SepBordeYCm,
        DiamAnclaX = f.DiamAnclaX,
        DiamAnclaY = f.DiamAnclaY,
        DiamAgujeroX = f.DiamAgujeroX,
        DiamAgujeroY = f.DiamAgujeroY,
        Electrodo = f.Electrodo,
        Soldadura = f.Soldadura,
        NCartabonesX = f.NCartabonesX,
        NCartabonesY = f.NCartabonesY,
        EspCartabonX = f.EspCartabonX,
        EspCartabonY = f.EspCartabonY,
        LongCartabonXCm = f.LongCartabonXCm,
        LongCartabonYCm = f.LongCartabonYCm,
        ConCartabones = f.ConCartabones,
        Escala = f.Escala,
        GirarPlaca90 = f.GirarPlaca90,
        AnclasEnMalla = f.AnclasEnMalla
    };

    /// <summary>Quita la placa seleccionada.</summary>
    private void OnQuitarPlaca(object sender, RoutedEventArgs e)
    {
        // La variable se declara en un patrón POSITIVO. Escrito al revés —«is not { } fila»— no
        // compila: C# no permite declarar una variable dentro de un patrón negado.
        if (PlacaSeleccionada is { } fila)
        {
            _datos.PlacasBase.Remove(fila);
            return;
        }

        MessageBox.Show("Selecciona la placa que quieres quitar.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Dibuja en AutoCAD el detalle de las placas base de la hoja.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada placa va en su propio <c>try</c>, por lo mismo que las secciones de concreto: un
    /// «AutoCAD ocupado» en la placa 2 de 5 no debe abortar la corrida y dejar las tres siguientes
    /// sin dibujar.
    /// </para>
    /// <para>
    /// Las placas se reparten en fila hacia la derecha, dejando aire entre una y otra, y arrancando
    /// del punto de inserción que trae la primera.
    /// </para>
    /// </remarks>
    private void OnDibujarPlacaBase(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_datos.PlacasBase.Count == 0)
        {
            MessageBox.Show(
                "No hay ninguna placa capturada. Usa «Agregar placa» para empezar.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // LO QUE FALTA SE DICE ANTES DE CONECTAR CON AUTOCAD. Abrir AutoCAD para después decir que
        // una placa no tiene medidas es hacerle perder el tiempo al usuario dos veces.
        var incompletas = _datos.PlacasBase
            .Where(f => f.Falta.Length > 0)
            .Select(f => $"  • {Nombre(f)}: falta {f.Falta}")
            .ToList();

        if (incompletas.Count > 0)
        {
            MessageBox.Show(
                "Corrige esto antes de dibujar:\n\n" + string.Join("\n", incompletas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            var escala = LeerEscala();

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new PlacaBaseDrawer(doc, escala);

            dibujante.AsegurarCapas();

            var dibujadas = 0;
            var entidades = 0;
            var bloques = new List<string>();
            var partidas = new List<string>();
            var rechazadas = new List<string>();

            // El punto de arranque es el que trae PlacaBaseCad por omisión —el de la macro— y de
            // ahí en adelante las placas se reparten hacia la derecha. Se lee de la primera fila y
            // no se escribe un cero aquí para que el día que ese punto se capture, esto lo respete
            // en lugar de pisarlo.
            var x = _datos.PlacasBase[0].AFormatoCad().InsercionX;

            foreach (var fila in _datos.PlacasBase)
            {
                var p = fila.AFormatoCad();

                p.InsercionX = x;

                int n;

                try
                {
                    n = dibujante.Dibujar(p);
                }
                catch (Exception ex)
                {
                    partidas.Add($"{Nombre(fila)} ({ex.Message.Split('\n')[0].Trim()})");

                    // Se avanza igual, para no encimarle la siguiente a lo que alcanzó a dibujarse.
                    x += Paso(p, escala);
                    continue;
                }

                if (n == 0)
                {
                    // Cero entidades con la fila completa solo puede ser una cosa: los libramientos
                    // J o K no se cumplen y el dibujante se negó a dibujar. El motivo está en sus
                    // fallos, con las dos distancias y el par de anclas.
                    rechazadas.Add(Nombre(fila));
                    continue;
                }

                dibujadas++;
                entidades += n;

                if (dibujante.UltimoBloque.Length > 0)
                {
                    bloques.Add(dibujante.UltimoBloque);
                }

                x += Paso(p, escala);
            }

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            MostrarResultadoPlacas(dibujante, dibujadas, entidades, bloques, partidas, rechazadas);
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
                "Error al dibujar la placa base en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>Cuánto se corre a la derecha para la placa siguiente, en unidades de dibujo.</summary>
    /// <remarks>
    /// <para>
    /// Se mide con <see cref="PlacaBaseCad.AnchoTotalDibujoCm"/> —la placa o el dado, el que
    /// sobresalga— y no con el ancho de la placa: el dado es casi siempre <b>más grande</b>, así
    /// que separando por el ancho de la placa el dado de una se mete en el de la siguiente.
    /// </para>
    /// <para>
    /// Los 60 cm de aire son para el rotulado: el detalle lleva cotas y leaders a los dos costados,
    /// y esos no caben dentro de la huella de la placa.
    /// </para>
    /// </remarks>
    private static double Paso(PlacaBaseCad p, double escala) =>
        (p.AnchoTotalDibujoCm + 60) * escala;

    /// <summary>Cómo se llama una placa en los avisos: su marca, o su sección si no tiene.</summary>
    private static string Nombre(PlacaBaseRow f)
    {
        if (f.Marca.Trim().Length > 0)
        {
            return f.Marca.Trim();
        }

        return f.Seccion.Trim().Length > 0 ? f.Seccion.Trim() : "placa sin marca";
    }

    /// <summary>El resumen de la corrida, con lo que salió y lo que no.</summary>
    private void MostrarResultadoPlacas(
        PlacaBaseDrawer dibujante, int dibujadas, int entidades,
        List<string> bloques, List<string> partidas, List<string> rechazadas)
    {
        var resumen =
            $"Listo.\n\n{dibujadas} placa(s) dibujadas\n{entidades} entidades creadas\n\n" +
            "Cada detalle quedó agrupado en un bloque con el nombre de su sección. Las COTAS y " +
            "los ROTULOS —incluidos los leaders— se quedan fuera del bloque, así que el detalle " +
            "se puede mover sin arrastrarlas.";

        if (bloques.Count > 0)
        {
            resumen += "\n\nBloques: " + string.Join(", ", bloques.Distinct());
        }

        // LAS RECHAZADAS SE DICEN PRIMERO Y CON SU MOTIVO. Es lo importante del aviso: no se
        // dibujaron porque no se pueden construir, no porque el programa fallara.
        if (rechazadas.Count > 0)
        {
            resumen +=
                $"\n\nNO SE DIBUJARON {rechazadas.Count} placa(s) porque no cumplen los " +
                "libramientos\nmínimos de las tablas J y K:\n  " +
                string.Join(", ", rechazadas) +
                "\n\nUna placa con las anclas más juntas de lo que la tabla permite no es un " +
                "detalle\na medias: es uno que no se puede construir. El motivo exacto —el par " +
                "de anclas,\nla distancia disponible y la exigida— está en los avisos de abajo.";
        }

        if (partidas.Count > 0)
        {
            resumen +=
                $"\n\nQUEDARON A MEDIAS {partidas.Count} placa(s), porque AutoCAD rechazó " +
                "alguna\nllamada mientras se dibujaban:\n  " + string.Join("\n  ", partidas) +
                "\n\nBórralas en AutoCAD y vuelve a dibujar.";
        }

        var fallos = dibujante.Fallos;

        StatusText.Text = fallos.Count == 0
            ? $"Dibujadas {dibujadas} placa(s) base en AutoCAD."
            : $"Dibujadas {dibujadas} placa(s) base, con {fallos.Count} aviso(s).";

        // LAS NOTAS Y LOS AVISOS VAN AL PANEL DE ESTA PESTAÑA, no al de la hoja de concreto. Son
        // de aquí —el par de anclas que no cumple, la distancia disponible y la exigida— y en la
        // otra pestaña nadie los va a mirar.
        //
        // Y quedan a mano pero NO interrumpen cuando no hay fallos: si el dibujo salió bien, un
        // cuadro de advertencia enseña a ignorar los cuadros de advertencia.
        var lineas = new List<string>();

        if (fallos.Count > 0)
        {
            lineas.Add("AVISOS DEL ULTIMO DIBUJO (" + fallos.Count + "):");
            lineas.AddRange(fallos.Select(f => "  - " + f));
        }

        if (dibujante.Notas.Count > 0)
        {
            if (lineas.Count > 0)
            {
                lineas.Add(string.Empty);
            }

            lineas.Add("Notas del ultimo dibujo:");
            lineas.AddRange(dibujante.Notas.Select(n => "  - " + n));
        }

        PlacasNotasText.Text = string.Join(Environment.NewLine, lineas);

        // El panel se pliega en cada dibujo, y se abre solo si hay avisos: así lo que hay que
        // leer está a la vista y lo informativo no ocupa la pantalla.
        PlacasNotasPanel.IsExpanded = fallos.Count > 0;

        if (fallos.Count == 0)
        {
            MessageBox.Show(resumen, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var detalle = string.Join(Environment.NewLine, fallos.Select(f => "  - " + f));

        MessageBox.Show(
            resumen + "\n\nAvisos (" + fallos.Count + "):\n\n" + detalle +
            "\n\nEste mismo texto queda en «Notas y avisos» al pie de esta pestaña.",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
