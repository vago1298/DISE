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

        // De dónde salió el catálogo, porque es la diferencia entre elegir el perfil de una
        // lista y teclear sus medidas: si dice «semilla», el archivo no se encontró y la
        // lista solo trae cuatro perfiles.
        texto += $"   ·   catálogo: {CatalogoPerfiles.Todos.Count} perfil(es) de " +
                 CatalogoPerfiles.Origen;

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

            // El acero se dibuja A LA IZQUIERDA DEL ORIGEN, empezando en x = -0.6 y
            // creciendo hacia la izquierda, igual que las cuatro macros:
            //
            //     xDerechaActual = -0.6
            //     xIzquierdaActual = xDerechaActual - anchoTotal
            //     xDerechaActual = xIzquierdaActual - sepIzq
            //
            // Y esto NO es un capricho de acomodo: el concreto crece hacia la derecha
            // desde donde acabe lo que ya haya en el plano, así que dejando el acero en el
            // semiplano negativo las dos hojas no se pisan nunca, aunque se dibujen en el
            // mismo dwg y en cualquier orden.
            //
            // CADA FAMILIA EN SU PROPIA BANDA, y esto salió de releer las cuatro macros:
            // las cuatro arrancan en x = -0.6, así que si dibujaran a la misma altura se
            // encimarían unas con otras. Lo que las separa es la Y: la macro del IR usa
            // baseY = 0, la del OR 2.0, la del CF 3.5 y la del OC 5.0. Cada familia tiene
            // su renglón en el plano.
            var entidades = 0;
            var dibujados = 0;

            // Se agrupa por familia para recorrer una banda completa antes de pasar a la
            // siguiente, que es lo que hace cada macro con su hoja.
            foreach (var grupo in _datos.SeccionesAcero.GroupBy(f => f.Familia))
            {
                var xDerecha = OrigenAceroCm * escala;
                var y = BandaDeLaFamiliaCm(grupo.Key) * escala;
                var aire = AireDeLaFamiliaCm(grupo.Key) * escala;

                var masAlto = 0.0;

                foreach (var fila in grupo)
                {
                    var perfil = AFormatoAceroCad(fila);

                    var ancho = perfil.AnchoDibujoCm * escala;
                    var xIzquierda = xDerecha - ancho;

                    var saltadasAntes = dibujante.Saltadas.Count;

                    var n = dibujante.DibujarAcero(perfil, xIzquierda, y);

                    if (dibujante.Saltadas.Count == saltadasAntes)
                    {
                        entidades += n;
                        dibujados++;
                    }

                    masAlto = Math.Max(masAlto, perfil.AltoDibujoCm);

                    // El hueco se avanza SIEMPRE, incluso para los que se saltaron.
                    //
                    // Aquí el acomodo es distinto del concreto y por un motivo: el concreto
                    // arranca después de lo que ya esté dibujado, así que lo nuevo nunca
                    // cae encima. El acero arranca en un punto FIJO, el -0.6 de las macros,
                    // y si los saltados no avanzaran el hueco, al volver a dibujar una hoja
                    // con dos perfiles ya hechos los otros dos se dibujarían justo encima.
                    //
                    // Avanzando siempre, cada perfil cae en el MISMO sitio pase lo que
                    // pase: la fila queda igual se dibuje entera o se rehaga solo una.
                    xDerecha = xIzquierda - aire;
                }

                // Y se avisa si la banda se sale de su hueco. Las alturas de las macros se
                // eligieron con perfiles de catálogo corriente, pero el IMCA trae perfiles
                // IS de hasta 1.90 m de peralte, y uno de esos dibujado en la banda del IR
                // —que empieza en 0— llega justo a la del OR, que está en 2.0.
                var techo = TechoDeLaBandaCm(grupo.Key);

                if (techo > 0 && masAlto > techo)
                {
                    MessageBox.Show(
                        $"Los perfiles {grupo.Key} llegan a {masAlto:N0} cm de peralte, y su " +
                        $"banda solo tiene {techo:N0} cm de alto hasta la familia de " +
                        "arriba.\n\nEl dibujo sale completo, pero esa familia puede " +
                        "encimarse con la siguiente. Las alturas de banda son las de tus " +
                        "macros (IR en 0, OR en 2.0, CF en 3.5 y OC en 5.0) y ahí no había " +
                        "perfiles tan altos.\n\nSi lo ves encimado, dibuja esa familia en " +
                        "un plano aparte o dime y separo las bandas.",
                        AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
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

    /// <summary>
    /// Aire entre un perfil y el siguiente, <b>el de cada macro</b>.
    /// </summary>
    /// <remarks>
    /// Es el <c>sepIzq</c> de cada una: 0.45 el IR, 0.55 el OR, 0.60 el OC y 0.65 el CF, en
    /// metros. Antes aquí había uno solo para las cuatro, y era un error de port: cada
    /// familia lleva su rótulo de distinto tamaño debajo —el del IR es el más grande— así
    /// que el hueco que necesita para que los rótulos no se toquen también es distinto.
    /// </remarks>
    private static double AireDeLaFamiliaCm(string? familia) => familia switch
    {
        FamiliaPerfil.Ir => 45,
        FamiliaPerfil.Or => 55,
        FamiliaPerfil.Oc => 60,
        FamiliaPerfil.Cf => 65,
        _ => 55
    };

    /// <summary>
    /// A qué altura va la fila de cada familia, <b>la <c>baseY</c> de cada macro</b>.
    /// </summary>
    /// <remarks>
    /// Las cuatro macros arrancan en la misma x, el −0.6, así que lo único que evita que se
    /// encimen unas con otras es esta altura. En metros: el IR en 0, el OR en 2.0, el CF en
    /// 3.5 y el OC en 5.0.
    /// </remarks>
    private static double BandaDeLaFamiliaCm(string? familia) => familia switch
    {
        FamiliaPerfil.Ir => 0,
        FamiliaPerfil.Or => 200,
        FamiliaPerfil.Cf => 350,
        FamiliaPerfil.Oc => 500,
        _ => 0
    };

    /// <summary>
    /// Cuánto alto tiene la banda de una familia antes de tocar la de arriba.
    /// </summary>
    /// <remarks>
    /// Sirve solo para avisar. La familia de más arriba —el OC— no tiene nada encima, así
    /// que devuelve cero: no hay con qué encimarse.
    /// </remarks>
    private static double TechoDeLaBandaCm(string? familia) => familia switch
    {
        FamiliaPerfil.Ir => 200 - 0,
        FamiliaPerfil.Or => 350 - 200,
        FamiliaPerfil.Cf => 500 - 350,
        _ => 0
    };

    /// <summary>
    /// Dónde empieza la fila de perfiles de acero: el borde <b>derecho</b> del primero.
    /// </summary>
    /// <remarks>
    /// Es el <c>xDerechaActual = -0.6</c> de las cuatro macros, dicho en centímetros porque
    /// es la unidad en la que se captura todo en esta interfaz: −60 cm por la escala de
    /// dibujo da exactamente el −0.6 del original.
    /// </remarks>
    private const double OrigenAceroCm = -60;
}
