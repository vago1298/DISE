using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

// System.Windows.Shapes define un tipo llamado Path, y el proyecto trae System.IO como
// using GLOBAL -esta en el .csproj-, que define otro. Con las dos cosas en el mismo
// archivo, escribir «Path» a secas es un CS0104: referencia ambigua. Los alias dicen cual
// es cual, igual que en MainWindow.xaml.cs: 'Path' es el de archivos y 'FormaPath' es la
// figura de WPF con la que se pinta la vista previa.
using Path = System.IO.Path;
using FormaPath = System.Windows.Shapes.Path;

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

    /// <summary>
    /// Engancha la vista previa de acero: se redibuja al cambiar de fila y al redimensionar.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="EnlazarAcero"/> porque esto se hace UNA VEZ, en el arranque:
    /// <c>Enlazar</c> se vuelve a llamar al cargar el ejemplo, al borrar todo y al empezar de
    /// nuevo, y suscribirse ahí dejaría el mismo evento enganchado cinco veces.
    /// </remarks>
    private void EngancharVistaPreviaAcero()
    {
        AceroPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaAcero();
        AceroGrid.SelectionChanged += (_, _) => DibujarVistaPreviaAcero();
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

        // La primera fila queda seleccionada para que la vista previa arranque con algo
        // dibujado en lugar de con un aviso de «selecciona un perfil».
        if (AceroGrid.SelectedItem is null && _datos.SeccionesAcero.Count > 0)
        {
            AceroGrid.SelectedIndex = 0;
        }

        DibujarVistaPreviaAcero();
    }

    private void OnFilaAceroEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_listo)
        {
            return;
        }

        ActualizarTotalesAcero();

        // Y la vista previa, EN CADA CELDA QUE SE EDITA.
        //
        // Va aquí y no en el SelectionChanged de la cuadrícula porque lo que hace útil una
        // vista previa es que responda mientras se teclea: si solo se refrescara al cambiar
        // de fila, habría que salir y volver a entrar para ver el efecto de cambiar un
        // espesor, y a esas alturas ya se perdió de vista qué se cambió.
        //
        // Solo se redibuja si la fila que cambió es la que se está viendo: con veinte filas
        // enlazadas, redibujar por cualquiera de ellas sería veinte veces el trabajo para
        // enseñar lo mismo.
        if (sender is null || ReferenceEquals(sender, AceroGrid.SelectedItem))
        {
            DibujarVistaPreviaAcero();
        }
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

        // Cuántos traen sus propiedades geométricas.
        //
        // Hace falta decirlo porque las dieciséis columnas del final salen VACÍAS en dos
        // casos que se ven igual y no son lo mismo: un perfil escrito a mano, que no está en
        // el catálogo y no tiene de dónde sacarlas, y un perfil de catálogo de una familia
        // para la que el manual no da esa propiedad. Sin este aviso, el usuario ve celdas en
        // blanco y no sabe si le falta un dato o si el dato no existe.
        var sinPropiedades = _datos.SeccionesAcero.Count(p => p.Propiedades.Cuantas == 0);

        if (sinPropiedades > 0)
        {
            texto += $"   ·   {sinPropiedades} sin propiedades geométricas " +
                     "(no están en el catálogo)";
        }

        // De dónde salió el catálogo, porque es la diferencia entre elegir el perfil de una
        // lista y teclear sus medidas: si dice «semilla», el archivo no se encontró y la
        // lista solo trae doce perfiles, y encima sin propiedades.
        texto += $"   ·   catálogo: {CatalogoPerfiles.Todos.Count} perfil(es) de " +
                 CatalogoPerfiles.Origen;

        TotalesAceroText.Text = texto;
    }

    // ======================================================================
    // Vista previa del perfil
    // ======================================================================

    /// <summary>
    /// Dibuja el perfil seleccionado con su geometría real, a escala y con su hueco.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La geometría sale de <see cref="TrazoAcero"/>, que es el mismo cálculo que usa el
    /// dibujante de AutoCAD.</b> Es la razón de que esa clase exista: una vista previa que
    /// calcula la forma por su cuenta puede acabar enseñando algo distinto de lo que se
    /// dibuja, y entonces no sirve para lo único que sirve una vista previa, que es
    /// confiar en ella.
    /// </para>
    /// <para>
    /// Se dibuja con <b>una sola figura de regla par-impar</b>. Eso hace que el hueco del
    /// tubo sea un hueco de verdad —no un parche del color del fondo— así que se ve el
    /// espesor de pared tal como va a salir, y funciona igual para las nueve formas sin
    /// tener un camino para cada una.
    /// </para>
    /// </remarks>
    private void DibujarVistaPreviaAcero()
    {
        AceroPreviewCanvas.Children.Clear();

        var ancho = AceroPreviewCanvas.ActualWidth;
        var alto = AceroPreviewCanvas.ActualHeight;

        if (ancho < 60 || alto < 60)
        {
            return;
        }

        if (AceroGrid.SelectedItem is not PerfilAceroRow fila)
        {
            AvisoVistaAcero("Selecciona un perfil de la tabla para verlo dibujado.");
            return;
        }

        // Si a la fila le faltan datos se dice CUÁLES, con el mismo texto de la columna
        // «Falta»: dibujar un perfil imposible enseñaría un borrón, y el borrón no explica
        // nada. Es el mismo criterio que la vista previa del concreto.
        var falta = fila.FaltanDatos;

        if (falta.Length > 0)
        {
            AvisoVistaAcero($"No se puede dibujar todavía: falta {falta}.");
            return;
        }

        var p = AFormatoAceroCad(fila);

        // El trazo se pide EN CENTÍMETROS —escala 1 y origen en cero— y el ajuste al lienzo
        // se hace después. Así la escala de pantalla no se mezcla con la del dibujo.
        var cuantos = p.Doble ? 2 : 1;
        var unoCm = p.AnchoDeUnoCm;

        var figuras = new GeometryGroup { FillRule = FillRule.EvenOdd };

        for (var i = 0; i < cuantos; i++)
        {
            var trazo = TrazoAcero.De(p, i * unoCm, 0, 1, espejo: i == 1);

            if (trazo is null)
            {
                AvisoVistaAcero("No se pudo calcular el contorno con esas medidas.");
                return;
            }

            AgregarAlGrupo(figuras, trazo);
        }

        if (figuras.Children.Count == 0)
        {
            AvisoVistaAcero("No se pudo calcular el contorno con esas medidas.");
            return;
        }

        // ---------- Ajuste al lienzo ----------
        const double margen = 40;

        var anchoCm = p.AnchoDibujoCm;
        var altoCm = p.AltoDibujoCm;

        if (anchoCm <= 0 || altoCm <= 0)
        {
            AvisoVistaAcero("El perfil no tiene ancho o alto que dibujar.");
            return;
        }

        var escala = Math.Min(
            (ancho - (2 * margen)) / anchoCm,
            (alto - (2 * margen)) / altoCm);

        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        // De centímetros con la Y hacia arriba a píxeles con la Y hacia abajo, centrado.
        var dx = (ancho - (anchoCm * escala)) / 2;
        var dy = (alto + (altoCm * escala)) / 2;

        var transformar = new TransformGroup();
        transformar.Children.Add(new ScaleTransform(escala, -escala));
        transformar.Children.Add(new TranslateTransform(dx, dy));

        figuras.Transform = transformar;

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));

        AceroPreviewCanvas.Children.Add(new FormaPath
        {
            Data = figuras,

            // Gris de acero, no blanco: así se distingue el acero del hueco del tubo.
            Fill = new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xD3)),
            Stroke = azul,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round
        });

        // ---------- Lo que hay que poder leer sin medir ----------
        Etiqueta(
            $"{fila.PerfilRotulo}    ·    {FormaPerfil.Nombre(fila.Forma)}"
            + (p.Doble ? "    ·    PERFIL DOBLE" : string.Empty),
            10, alto - 40, 12.5, azul, negrita: true);

        Etiqueta(
            $"{altoCm:N2} × {anchoCm:N2} cm" +
            (fila.Propiedades.AreaCm2 is { } a ? $"    ·    área {a:N2} cm²" : string.Empty) +
            (fila.Propiedades.PesoKgM is { } w ? $"    ·    {w:N2} kg/m" : string.Empty),
            10, alto - 22, 11.5, Brushes.DimGray);

        // Y las dos medidas que gobiernan la forma, cada una junto a su lado, para poder
        // cotejarlas con la tabla de un vistazo.
        Etiqueta($"{anchoCm:N2}", dx + (anchoCm * escala / 2) - 16,
                 dy - (altoCm * escala) - 18, 11, azul);

        Etiqueta($"{altoCm:N2}", dx + (anchoCm * escala) + 6,
                 dy - (altoCm * escala / 2) - 8, 11, azul);
    }

    /// <summary>Mete el trazo de un perfil en el grupo de figuras de la vista previa.</summary>
    /// <remarks>
    /// Las cuatro piezas que puede traer un trazo se convierten cada una en su geometría, y
    /// van todas al mismo grupo par-impar: lo que quede encerrado por dos contornos —el hueco
    /// del tubo— sale vacío solo, sin tener que tratarlo aparte.
    /// </remarks>
    private static void AgregarAlGrupo(GeometryGroup grupo, TrazoAcero.Trazo trazo)
    {
        foreach (var contorno in new[] { trazo.Exterior, trazo.Interior })
        {
            if (contorno is null)
            {
                continue;
            }

            // Los arcos se muestrean: un lienzo de WPF no tiene bulges. Veinte tramos por
            // arco es de sobra para que el doblez de una lámina se vea curvo a este tamaño.
            var pts = TrazoAcero.Muestrear(contorno, 20);

            if (pts.Count < 3)
            {
                continue;
            }

            var figura = new PathFigure
            {
                StartPoint = new Point(pts[0].X, pts[0].Y),
                IsClosed = true,
                IsFilled = true
            };

            for (var k = 1; k < pts.Count; k++)
            {
                figura.Segments.Add(
                    new LineSegment(new Point(pts[k].X, pts[k].Y), true));
            }

            var geo = new PathGeometry();
            geo.Figures.Add(figura);

            grupo.Children.Add(geo);
        }

        foreach (var circulo in new[] { trazo.CircExterior, trazo.CircInterior })
        {
            if (circulo is null || circulo.R <= 0)
            {
                continue;
            }

            grupo.Children.Add(new EllipseGeometry(
                new Point(circulo.Cx, circulo.Cy), circulo.R, circulo.R));
        }
    }

    /// <summary>Un aviso centrado en la vista previa de acero.</summary>
    private void AvisoVistaAcero(string texto) =>
        Etiqueta(texto, 14, 34, 12, Brushes.Gray);

    /// <summary>Un texto en el lienzo de la vista previa de acero.</summary>
    private void Etiqueta(
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

        AceroPreviewCanvas.Children.Add(t);
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
            // CADA SECCIÓN EN SU PROPIO RENGLÓN, y todas arrancando en la misma x.
            //
            // Las cuatro macros arrancan en x = -0.6 y van colocando los perfiles de una
            // familia HACIA LA IZQUIERDA, uno tras otro. Eso deja una tira horizontal por
            // familia, y con nombres de catálogo largos —«IS - 225 mm x 12.7 mm / 750 mm x
            // 9.5 mm»— los rótulos, que van centrados debajo y miden casi un metro, se
            // pisan unos con otros aunque los perfiles no se toquen.
            //
            // Así que ahora TODAS las secciones se alinean en x = -0.6, cada una en su
            // renglón, y lo que crece es la altura. Se lee como una tabla de detalles: una
            // sección por renglón, todas con su borde derecho a plomo.
            var entidades = 0;
            var dibujados = 0;
            var bandas = new List<string>();

            // La primera arranca en 0 y cada una va SETENTA CENTÍMETROS por encima de la de
            // abajo, medidos desde su cima. Setenta da de sobra para lo que sobresale de una
            // sección: los cuatro renglones del rótulo cuelgan del orden de 20 cm por debajo
            // de su base y las cotas suben otros 15 por encima del perfil de abajo.
            var yCm = 0.0;

            // Se agrupa por familia y se recorren en el ORDEN DE LA LISTA, no en el de
            // captura: así el plano sale siempre igual aunque las filas estén revueltas, y
            // las familias que se parecen quedan juntas para poder compararlas.
            foreach (var grupo in _datos.SeccionesAcero
                         .GroupBy(f => f.Familia)
                         .OrderBy(g => OrdenDeLaFamilia(g.Key)))
            {
                var yPrimera = yCm;
                var cuantas = 0;

                foreach (var fila in grupo)
                {
                    var perfil = AFormatoAceroCad(fila);

                    // TODAS en x = -0.6. El borde DERECHO va en el origen y el dibujo crece
                    // hacia la izquierda, que es el xDerechaActual de las macros; lo que
                    // cambia es que ya no se avanza en x de una sección a la siguiente.
                    var xDerecha = OrigenAceroCm * escala;
                    var xIzquierda = xDerecha - (perfil.AnchoDibujoCm * escala);

                    var saltadasAntes = dibujante.Saltadas.Count;

                    var n = dibujante.DibujarAcero(perfil, xIzquierda, yCm * escala);

                    if (dibujante.Saltadas.Count == saltadasAntes)
                    {
                        entidades += n;
                        dibujados++;
                    }

                    cuantas++;

                    // Y se sube al renglón siguiente SIEMPRE, incluso si esta sección se
                    // saltó por tener ya su bloque.
                    //
                    // Es lo mismo que antes se hacía en x y por el mismo motivo: el acero
                    // arranca en un punto FIJO, así que si las saltadas no avanzaran el
                    // renglón, al volver a dibujar una hoja con dos secciones ya hechas las
                    // otras se dibujarían justo encima de ellas. Avanzando siempre, cada
                    // sección cae en el MISMO sitio se dibuje la hoja entera o solo una.
                    //
                    // Se suma el ALTO DIBUJADO, no el peralte capturado, porque en el tubo
                    // rectangular no son lo mismo: un tubo se dibuja de pie, con su lado
                    // mayor en vertical, aunque se haya capturado al revés.
                    yCm += perfil.AltoDibujoCm + SeparacionEntreSeccionesCm;
                }

                bandas.Add(
                    $"{grupo.Key}: {cuantas} sección(es), de y = {yPrimera / 100:0.00} m " +
                    $"a {(yCm - SeparacionEntreSeccionesCm) / 100:0.00} m");
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
                : $"\n\nTodas las secciones alineadas en x = {OrigenAceroCm / 100:0.0} m, " +
                  $"una por renglón y separadas {SeparacionEntreSeccionesCm / 100:0.0} m " +
                  "de la cima de la de abajo:\n  " +
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
    /// Separación vertical entre la cima de una sección y la base de la siguiente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Setenta centímetros, y se miden desde la CIMA de la sección de abajo</b>, así que
    /// una sección alta empuja a la siguiente hacia arriba y nunca se tocan.
    /// </para>
    /// <para>
    /// Da de sobra para lo que sobresale de una sección por arriba y por abajo: los cuatro
    /// renglones del rótulo cuelgan del orden de 20 cm por debajo de su base —hasta 6 cm de
    /// separación más cuatro renglones de hasta 3— y las cotas suben otros 15 por encima del
    /// perfil de abajo. Quedan más de 30 cm de aire entre el rótulo de una y las cotas de la
    /// otra.
    /// </para>
    /// <para>
    /// <b>Antes había un aire horizontal por familia</b> —el <c>sepIzq</c> de cada macro: 45
    /// el IR, 55 el OR, 60 el OC, 65 el CF— porque las secciones de una familia se ponían una
    /// al lado de la otra. Ya no hace falta ninguno: todas las secciones se alinean en la
    /// misma x y lo único que las separa es la altura.
    /// </para>
    /// <para>
    /// <b>Lo que cuesta</b> conviene saberlo: la altura de una sección depende de las que
    /// tenga debajo, así que ya no es un número que se pueda consultar. Dibujando la hoja
    /// entera cada cosa cae en su sitio, pero si se agrega una sección en medio y se vuelve a
    /// dibujar, las que ya son bloque se quedan donde estaban y las nuevas van a la altura
    /// nueva. Si pasa, se borran los bloques y se dibuja la hoja de una vez. El programa dice
    /// al terminar a qué altura quedó cada familia.
    /// </para>
    /// </remarks>
    private const double SeparacionEntreSeccionesCm = 70;

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
