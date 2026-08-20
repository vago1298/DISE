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

        // La FORMA se resuelve aquí, no en el dibujante. Es la misma división que con todo
        // lo demás: la interfaz decide qué es cada cosa y el dibujante solo dibuja. Así el
        // dibujante no necesita saber que una IS y una W se trazan igual.
        Forma = r.Forma,

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
        RadioCm = r.RadioCm,
        AnchoMenorCm = r.AnchoMenorCm
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
                    $"{donde}: la familia «{p.Familia}» no se reconoce. Las que hay son " +
                    string.Join(", ", FamiliaPerfil.Todas) + ".");
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
            // encimarían unas con otras. Lo que las separa es la Y.
            var entidades = 0;
            var dibujados = 0;
            var bandas = new List<string>();

            // LA ALTURA DE CADA BANDA SE CALCULA, no está en una tabla.
            //
            // La primera arranca en 0 y cada una de las siguientes va UN METRO por encima de
            // la sección MÁS ALTA de la de abajo. Las macros lo tenían en cuatro números
            // fijos —el IR en 0, el OR en 2.0, el CF en 3.5, el OC en 5.0— y con doce
            // familias esa tabla no hay manera de acertarla: para que nunca se encimen habría
            // que reservarle a cada una el hueco de su perfil más alto del catálogo, y la IS
            // llega a 1.90 m, así que una hoja de ángulos y montenes quedaría con metros de
            // papel vacío entre banda y banda.
            //
            // Calculándola, el plano sale siempre lo más compacto que puede y NO SE PUEDE
            // ENCIMAR: el metro se mide desde la sección más alta que se acabó de dibujar,
            // no desde la más alta que podría haber.
            var yCm = 0.0;

            // Se agrupa por familia para recorrer una banda completa antes de pasar a la
            // siguiente, que es lo que hace cada macro con su hoja. Y se recorren en el
            // ORDEN DE LA LISTA de familias, no en el de captura: así el plano sale siempre
            // igual aunque las filas estén revueltas, y las familias que se parecen quedan
            // en bandas vecinas para poder compararlas.
            foreach (var grupo in _datos.SeccionesAcero
                         .GroupBy(f => f.Familia)
                         .OrderBy(g => OrdenDeLaFamilia(g.Key)))
            {
                var xDerecha = OrigenAceroCm * escala;
                var y = yCm * escala;
                var aireFamilia = AireDeLaFamiliaCm(grupo.Key);

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

                    // EL AIRE LO MANDA EL RÓTULO, no el perfil.
                    //
                    // El rótulo va centrado debajo de la sección y casi siempre es MÁS ANCHO
                    // que ella: un renglón como «PERFIL: IS - 225 mm x 12.7 mm / 750 mm x
                    // 9.5 mm» mide casi un metro, y el perfil que rotula, 22 cm. Con el aire
                    // de las macros —45 cm— dos secciones así quedan a 67 cm de centro a
                    // centro y sus rótulos se pisan, aunque las secciones no se toquen.
                    //
                    // Así que el hueco es el mayor de los dos: el de la macro, o el que
                    // necesita el rótulo con diez centímetros de respiro.
                    var aire = Math.Max(
                        aireFamilia,
                        perfil.AnchoRotuloCm - perfil.AnchoDibujoCm + 10) * escala;

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

                bandas.Add(
                    $"{grupo.Key} en y = {yCm / 100:0.00} m " +
                    $"(la más alta mide {masAlto:N0} cm)");

                // Y aquí se sube a la banda siguiente: la sección más alta de esta familia,
                // más el metro de separación. Se suma el ALTO DIBUJADO, no el peralte
                // capturado, porque en el tubo rectangular no son lo mismo: un tubo se
                // dibuja de pie, con su lado mayor en vertical, aunque se haya capturado al
                // revés.
                yCm += masAlto + SeparacionDeBandasCm;
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

            // A qué altura quedó cada familia. Se dice porque ya no es un número fijo que se
            // pueda consultar: se calcula con las secciones de esta hoja, así que la única
            // manera de saber dónde buscar cada familia en el plano es que el programa lo
            // diga.
            var dondeQuedaron = bandas.Count == 0
                ? string.Empty
                : $"\n\nCada familia en su banda, separadas {SeparacionDeBandasCm / 100:0} m " +
                  "de la sección más alta de la de abajo:\n  " +
                  string.Join("\n  ", bandas);

            var resumen =
                "Listo.\n\n" +
                $"{dibujados} perfil(es) dibujados\n" +
                $"{entidades} entidades creadas\n\n" +
                "Cada perfil quedó agrupado en un bloque con el nombre de su ID." +
                dondeQuedaron +
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
    /// Aire mínimo entre un perfil y el siguiente, en centímetros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las cuatro primeras son el <c>sepIzq</c> de cada macro: 0.45 el IR, 0.55 el OR, 0.60
    /// el OC y 0.65 el CF, en metros. Antes aquí había uno solo para las cuatro, y era un
    /// error de port: cada familia lleva su rótulo de distinto tamaño debajo —el del IR es
    /// el más grande— así que el hueco que necesita también es distinto.
    /// </para>
    /// <para>
    /// Las ocho nuevas se ponen <b>al revés de lo que parece</b>: las familias de perfiles
    /// anchos llevan menos aire y las de perfiles estrechos, más. No es un descuido. El hueco
    /// no lo pide la sección, lo pide su rótulo, y el rótulo mide casi lo mismo para todas:
    /// un ángulo de 5 cm con un rótulo de 70 necesita 65 cm de aire para que su rótulo no
    /// toque el de al lado, y una IS de 22 cm con el mismo rótulo necesita 48. Es un mínimo,
    /// además: el que dibuja recalcula el aire con el ancho real del rótulo de cada perfil.
    /// </para>
    /// </remarks>
    private static double AireDeLaFamiliaCm(string? familia) => familia switch
    {
        // Las cuatro de las macros, tal cual.
        FamiliaPerfil.Ir => 45,
        FamiliaPerfil.Or => 55,
        FamiliaPerfil.Oc => 60,
        FamiliaPerfil.Cf => 65,

        // Las de perfil ancho, como el IR.
        FamiliaPerfil.Is => 45,
        FamiliaPerfil.Ic => 45,
        FamiliaPerfil.S => 50,
        FamiliaPerfil.Wt => 55,

        // Las estrechas, que necesitan más porque su rótulo es más ancho que ellas.
        FamiliaPerfil.C => 60,
        FamiliaPerfil.Zf => 65,
        FamiliaPerfil.L => 70,
        FamiliaPerfil.Os => 70,

        _ => 55
    };

    /// <summary>
    /// Separación vertical entre la sección más alta de una familia y la banda de arriba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Un metro, y se mide desde la sección más alta que se dibujó</b>, no desde la más
    /// alta que podría haber. Antes había una tabla de alturas fijas —la del IR en 0, la del
    /// OR en 2.0…— que venía de las cuatro macros, y con doce familias esa tabla no hay
    /// manera de acertarla: para que nunca se encimen hay que reservarle a cada familia el
    /// hueco de su perfil más alto del catálogo, y la IS llega a 1.90 m, así que una hoja de
    /// ángulos y montenes quedaba con metros de papel vacío entre banda y banda.
    /// </para>
    /// <para>
    /// El metro da de sobra para lo que sobresale de la sección: los cuatro renglones del
    /// rótulo cuelgan del orden de 20 cm por debajo de su base, y las cotas suben otros 15
    /// por encima del perfil de abajo.
    /// </para>
    /// <para>
    /// <b>Lo que se pierde al calcularla</b> conviene saberlo: la altura de una familia
    /// depende ahora de las que tenga debajo, así que si se agrega un perfil más alto a la
    /// IR, todo lo que esté encima sube. Dibujando la hoja entera eso da igual —cada cosa
    /// cae en su sitio— pero si se redibuja después de haber cambiado alturas, las secciones
    /// que ya son bloque se quedan donde estaban y las nuevas van a la altura nueva. Si pasa,
    /// se borran los bloques y se dibuja la hoja de una vez.
    /// </para>
    /// </remarks>
    private const double SeparacionDeBandasCm = 100;

    /// <summary>Dónde va una familia en el orden del acomodo.</summary>
    /// <remarks>
    /// Sale de <see cref="FamiliaPerfil.Todas"/>, que ya está en el orden de las bandas, así
    /// que no hay una segunda lista que se pueda quedar vieja. Una familia que no esté en la
    /// lista se va al final en lugar de al principio: si algo raro se colara, mejor que no
    /// desplace lo que sí se reconoce.
    /// </remarks>
    private static int OrdenDeLaFamilia(string? familia)
    {
        var i = Array.IndexOf(FamiliaPerfil.Todas, (familia ?? string.Empty).Trim().ToUpperInvariant());

        return i < 0 ? int.MaxValue : i;
    }

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
