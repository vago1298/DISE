using System.Windows;
using System.Windows.Input;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>secciones de acero</b>: sus listas, su enlace y su botón de dibujar.
/// </summary>
/// <remarks>
/// <para>
/// Va en un archivo parcial aparte porque <c>MainWindow.xaml.cs</c> ya pasa de las tres mil
/// líneas y esto es un módulo entero: sus cuatro familias de perfil, su validación y su
/// exportación. Meterlo ahí dejaría el archivo del concreto y el del acero mezclados sin
/// ninguna ventaja.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Llena las listas desplegables de la hoja de acero.</summary>
    /// <remarks>
    /// Las listas salen de <see cref="PerfilAceroRow"/>, el mismo sitio del que sale la
    /// validación, por la misma razón que en el concreto: si se escriben aquí a mano, un día
    /// se agrega una familia y el desplegable se queda viejo.
    /// </remarks>
    private void LlenarListasAcero()
    {
        ColFamilia.ItemsSource = FamiliaPerfil.Todas;
        ColElementoAcero.ItemsSource = PerfilAceroRow.Elementos;
        ColClasificacion.ItemsSource = PerfilAceroRow.Clasificaciones;
        ColAcero.ItemsSource = PerfilAceroRow.Aceros;
    }

    /// <summary>Enlaza la cuadrícula de acero y mantiene sus totales al día.</summary>
    private void EnlazarAcero()
    {
        AceroGrid.ItemsSource = _datos.SeccionesAcero;

        // Igual que en el concreto: la colección avisa de filas agregadas o quitadas, pero
        // no de celdas editadas, así que hay que escuchar cada fila. Sin esto el renglón de
        // totales y el aviso de datos que faltan se quedarían congelados mientras se
        // escribe, que es cuando sirven.
        _datos.SeccionesAcero.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (Row fila in e.OldItems)
                {
                    fila.PropertyChanged -= OnFilaAceroEditada;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (Row fila in e.NewItems)
                {
                    fila.PropertyChanged += OnFilaAceroEditada;
                }
            }

            ActualizarTotalesAcero();
        };

        foreach (var fila in _datos.SeccionesAcero)
        {
            fila.PropertyChanged += OnFilaAceroEditada;
        }

        ActualizarTotalesAcero();
    }

    private void OnFilaAceroEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        ActualizarTotalesAcero();
    }

    /// <summary>
    /// El renglón de totales: cuántos perfiles hay, de qué familias y cuántos les faltan
    /// datos.
    /// </summary>
    /// <remarks>
    /// Lo de «les faltan datos» va aquí y no solo en la columna calculada porque la columna
    /// se ve fila por fila: con veinte perfiles, el usuario necesita saber de un vistazo si
    /// alguno está incompleto antes de mandar a dibujar.
    /// </remarks>
    private void ActualizarTotalesAcero()
    {
        var n = _datos.SeccionesAcero.Count;

        var porFamilia = _datos.SeccionesAcero
            .GroupBy(p => p.Familia)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}");

        var incompletos = _datos.SeccionesAcero.Count(p => p.FaltanDatos.Length > 0);

        var texto = $"{n} perfil(es)";

        var familias = string.Join(", ", porFamilia);

        if (familias.Length > 0)
        {
            texto += "   ·   " + familias;
        }

        if (incompletos > 0)
        {
            texto += $"   ·   {incompletos} con datos incompletos (ver la columna «Falta»)";
        }

        TotalesAceroText.Text = texto;
    }

    /// <summary>
    /// Pasa una fila de la hoja al formato del dibujante: <b>todo resuelto</b>.
    /// </summary>
    /// <remarks>
    /// Aquí se hacen las traducciones de texto —el nombre del perfil a nomenclatura mexicana
    /// y el elemento con su clasificación— para que el dibujante no tenga que interpretar
    /// nada. Es la misma división que con el concreto y <c>AFormatoCad</c>.
    /// </remarks>
    private static PerfilAceroCad AFormatoAceroCad(PerfilAceroRow r) => new()
    {
        Familia = r.Familia,
        Id = r.Id,
        Elemento = r.ElementoRotulo,
        Perfil = r.PerfilRotulo,
        Acero = r.Acero,
        Doble = r.Doble,
        PeralteCm = r.PeralteCm,
        AnchoCm = r.AnchoCm,
        EspesorCm = r.EspesorAlmaCm,
        EspesorPatinCm = r.EspesorPatinCm,
        LabioCm = r.LabioCm,
        RadioCm = r.RadioCm
    };

    /// <summary>
    /// Revisa la hoja de acero antes de dibujar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se revisa lo que <b>no se puede dibujar</b>: sin ID no hay nombre de bloque, con IDs
    /// repetidos el segundo bloque no se crea, y sin las dimensiones de su familia el perfil
    /// saldría cruzado sobre sí mismo. La familia desconocida también se ataja aquí, porque
    /// si no el dibujante la salta y el usuario solo vería que «no se dibujó».
    /// </para>
    /// </remarks>
    private bool RevisarAcero(out List<string> problemas)
    {
        problemas = new List<string>();

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _datos.SeccionesAcero.Count; i++)
        {
            var p = _datos.SeccionesAcero[i];
            var donde = $"Fila {i + 1}";

            var id = (p.Id ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                problemas.Add($"{donde}: falta el ID. Es el nombre del bloque en AutoCAD.");
            }
            else if (!vistos.Add(id))
            {
                problemas.Add(
                    $"{donde}: el ID «{id}» está repetido. Cada perfil necesita el suyo, " +
                    "porque el ID es el nombre del bloque.");
            }

            if (!FamiliaPerfil.Todas.Contains(p.Familia))
            {
                problemas.Add(
                    $"{donde}: la familia «{p.Familia}» no se reconoce. Elige IR, OR, OC o CF.");
            }

            var falta = p.FaltanDatos;

            if (falta.Length > 0)
            {
                problemas.Add($"{donde} ({id}): {falta}.");
            }
        }

        return problemas.Count == 0;
    }

    /// <summary>Dibuja en AutoCAD todos los perfiles de la hoja de acero.</summary>
    private void OnExportAcero(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_datos.SeccionesAcero.Count == 0)
        {
            MessageBox.Show(
                "No hay ningún perfil capturado en la hoja de acero.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!RevisarAcero(out var problemas))
        {
            MessageBox.Show(
                "Corrige esto antes de generar el dibujo:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            var escala = LeerEscala();

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new SeccionDrawer(doc, escala)
            {
                Redibujar = RedibujarAceroChk.IsChecked == true
            };

            dibujante.AsegurarCapasAcero();

            var x = dibujante.PosicionInicialX();
            var entidades = 0;
            var dibujados = 0;

            foreach (var fila in _datos.SeccionesAcero)
            {
                var perfil = AFormatoAceroCad(fila);
                var saltadasAntes = dibujante.Saltadas.Count;

                var n = dibujante.DibujarAcero(perfil, x, 0);

                if (dibujante.Saltadas.Count > saltadasAntes)
                {
                    continue;
                }

                entidades += n;
                dibujados++;

                // Se avanza el ancho del perfil más un aire, y solo lo avanzan los que se
                // dibujaron de nuevo: los que volvieron a su sitio no ocupan lugar nuevo.
                // Es la misma regla del concreto, con el aire de las macros de acero, que
                // separan entre 45 y 65 cm según la familia.
                if (!dibujante.UltimaFueASuSitio)
                {
                    x += (perfil.AnchoDibujoCm + AireEntrePerfilesCm) * escala;
                }
            }

            dibujante.RotulosAlFrente();

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var saltados = dibujante.Saltadas
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rehechos = dibujante.Redibujadas
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var aviso = saltados.Count == 0
                ? string.Empty
                : $"\n\nSE SALTARON {saltados.Count} perfil(es) porque su bloque ya existe " +
                  "en el dibujo:\n  " + string.Join(", ", saltados) +
                  "\n\nSi los cambiaste y quieres rehacerlos, marca «Redibujar las que ya " +
                  "existen»:\ncada uno vuelve al mismo sitio donde estaba.";

            if (rehechos.Count > 0)
            {
                aviso +=
                    $"\n\nSe REHICIERON {rehechos.Count} perfil(es) en su mismo sitio:\n  " +
                    string.Join(", ", rehechos);
            }

            var fallos = dibujante.Fallos;

            var resumen =
                "Listo.\n\n" +
                $"{dibujados} perfil(es) dibujados\n" +
                $"{entidades} entidades creadas\n\n" +
                "Cada perfil quedó agrupado en un bloque con el nombre de su ID." +
                aviso;

            StatusText.Text = saltados.Count == 0
                ? $"Dibujados {dibujados} perfil(es) de acero en AutoCAD."
                : $"Dibujados {dibujados} perfil(es); {saltados.Count} saltado(s) por " +
                  "existir ya.";

            if (fallos.Count == 0)
            {
                MessageBox.Show(resumen, AppInfo.ProductName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var detalle = string.Join(Environment.NewLine, fallos.Select(f => "  - " + f));

                MessageBox.Show(
                    resumen + "\n\nPERO hubo " + fallos.Count + " fallo(s) que se " +
                    "toleraron, así que el dibujo puede estar incompleto:\n\n" + detalle,
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

    /// <summary>Aire entre un perfil y el siguiente, en centímetros.</summary>
    /// <remarks>
    /// Las cuatro macros dejan entre 45 y 65 cm según la familia —<c>sepIzq</c> de 0.45 a
    /// 0.65 en metros—. Aquí es uno solo, 55, porque las secciones se dibujan mezcladas en
    /// la misma fila y un hueco distinto por familia se vería como un acomodo descuidado.
    /// </remarks>
    private const double AireEntrePerfilesCm = 55;
}
