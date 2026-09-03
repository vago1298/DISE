using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CadLink.App.Models;
using CadLink.Cad;

// La figura de WPF con la que se pinta la vista previa, con alias y SIN importar
// System.Windows.Shapes entero. El motivo es el de MainWindow.Acero.cs: ese espacio de nombres trae
// un Path y System.IO —que es using GLOBAL, está en el .csproj— trae otro, así que con los dos
// importados escribir «Path» a secas es un CS0104, referencia ambigua. Con el alias a secas, ese
// choque no puede ocurrir en este archivo.
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>placas base</b>: su tabla, su vista previa y el botón que las dibuja.
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
        // LOS ELECTRODOS, CON SU XX. Es la convención del plano: un E70 se escribe «E70XX» porque
        // los dos últimos dígitos —posición y tipo de corriente— los elige el taller, no el
        // calculista. Antes la lista los ofrecía sin el sufijo y el rótulo lo agregaba por su
        // cuenta, así que la celda decía una cosa y el plano otra.
        ColPlacaElectrodo.ItemsSource = new[] { "E60XX", "E70XX", "E80XX", "E90XX" };

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

        // La primera fila queda seleccionada para que la vista previa arranque con algo dibujado en
        // lugar de con un aviso de «selecciona una placa».
        if (PlacasGrid.SelectedItem is null && _datos.PlacasBase.Count > 0)
        {
            PlacasGrid.SelectedIndex = 0;
        }

        DibujarVistaPreviaPlacaBase();
    }

    /// <summary>
    /// Se salió de una celda: es el momento de corregir la separación al borde.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aquí y no en el <c>set</c> de la propiedad. Las celdas de la hoja escriben en cada tecla
    /// —<c>UpdateSourceTrigger=PropertyChanged</c>—, así que corrigiendo en el <c>set</c>, teclear
    /// «5» camino de «50» se convierte en un forcejeo: la celda sube el 5 al mínimo antes de que se
    /// llegue a escribir el 0. Al terminar de editar el usuario ya dijo lo que quería.
    /// </para>
    /// <para>
    /// Y se <b>avisa</b> cuando se mueve un número. Cambiar un valor que el usuario acaba de
    /// escribir, sin decir nada, se parece mucho a que el programa no le hiciera caso.
    /// </para>
    /// </remarks>
    // EL TIPO VA CUALIFICADO. DataGridCellEditEndingEventArgs vive en System.Windows.Controls, que
    // este archivo NO importa, y escribirlo a secas era el CS0246 que rompió la compilación.
    //
    // Se cualifica en lugar de agregar el using, por lo mismo que con FormaPath: este archivo ya
    // usa System.Windows.Controls.TextBlock y .Canvas cualificados, e importar el espacio entero
    // mete unas cincuenta clases en el ámbito —Border, Grid, Panel, Image, Label— en un archivo que
    // solo necesita tres. Cada una es una ambigüedad futura esperando a que alguien declare una
    // variable con ese nombre.
    private void OnCeldaPlacaEditada(
        object? sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (!_listo || e.Row?.Item is not PlacaBaseRow fila)
        {
            return;
        }

        // Se hace DESPUÉS de que la celda haya escrito su valor en la fila. En este evento el
        // enlace todavía no ha corrido, así que la corrección iría contra el valor de antes.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var antesX = fila.SepBordeXCm;
            var antesY = fila.SepBordeYCm;

            if (!fila.AjustarSeparacionesAlBorde())
            {
                return;
            }

            var partes = new List<string>();

            if (Math.Abs(antesX - fila.SepBordeXCm) > 1e-9)
            {
                partes.Add($"X: {antesX:0.#} → {fila.SepBordeXCm:0.#} cm");
            }

            if (Math.Abs(antesY - fila.SepBordeYCm) > 1e-9)
            {
                partes.Add($"Y: {antesY:0.#} → {fila.SepBordeYCm:0.#} cm");
            }

            StatusText.Text =
                $"{Nombre(fila)}: separación al borde ajustada al mínimo de la columna K " +
                $"({fila.BordeMinimo}).  " + string.Join("   ", partes);

            DibujarVistaPreviaPlacaBase();
        }));
    }

    private void OnFilaPlacaEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // AL ELEGIR EL DADO, SUS MEDIDAS SE TRAEN SOLAS. Va ANTES del guardia de _listo a
        // propósito: al abrir un trabajo las filas entran con _listo apagado, y si la referencia se
        // saltara ahí, un .clk guardado con el ID del dado se abriría con las medidas viejas.
        if (sender is PlacaBaseRow fila && e.PropertyName == nameof(PlacaBaseRow.IdDado))
        {
            ReferenciarDadoDePlaca(fila);
        }

        if (!_listo)
        {
            return;
        }

        ActualizarTotalesPlacas();

        // Y la vista previa, EN CADA CELDA QUE SE EDITA. Es lo que la hace útil: si solo se
        // refrescara al cambiar de fila, habría que salir y volver a entrar para ver el efecto de
        // mover una separación, y a esas alturas ya se perdió de vista qué se cambió.
        //
        // Solo si la fila que cambió es la que se está viendo: con veinte filas enlazadas,
        // redibujar por cualquiera de ellas sería veinte veces el trabajo para enseñar lo mismo.
        if (sender is null || ReferenceEquals(sender, PlacasGrid.SelectedItem))
        {
            DibujarVistaPreviaPlacaBase();
        }
    }

    /// <summary>
    /// Trae a la fila las medidas del <b>dado</b> elegido, de la hoja de secciones de concreto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mismo mecanismo que usa la hoja de zapatas con su dado y su columna, y por el mismo
    /// motivo: el dado ya está capturado en su hoja —con su armado, su recubrimiento y su forma—
    /// porque es una sección que se dibuja por su cuenta. Volver a teclear sus medidas aquí es
    /// pedir dos veces el mismo dato, y de los dos sitios el segundo es el que se equivoca.
    /// </para>
    /// <para>
    /// <b>Nunca se escribe un cero.</b> Si la sección no tiene la medida capturada, se deja lo que
    /// hubiera: traer un cero borraría un dato bueno para poner uno que no existe.
    /// </para>
    /// </remarks>
    private void ReferenciarDadoDePlaca(PlacaBaseRow fila)
    {
        var id = ZapataAisladaRow.SoloElId(fila.IdDado);

        if (id.Length == 0)
        {
            return;
        }

        var dado = _datos.SeccionesConcreto.FirstOrDefault(s =>
            EsDado(s.Elemento)
            && ZapataAisladaRow.SoloElId(s.Id).Equals(id, StringComparison.OrdinalIgnoreCase));

        if (dado is null)
        {
            return;
        }

        // LA FORMA PRIMERO. Decide cómo se leen las dos medidas siguientes: en un dado redondo la
        // base es el DIÁMETRO y no hay una segunda dimensión que valga.
        fila.DadoCircular = dado.EsCircular;

        if (dado.BaseCm > 0)
        {
            fila.DadoXCm = dado.BaseCm;
        }

        if (dado.EsCircular)
        {
            // El diámetro, en las dos direcciones: así las cotas del detalle miden lo mismo por los
            // dos lados y el encuadre no se descuadra.
            if (dado.BaseCm > 0)
            {
                fila.DadoYCm = dado.BaseCm;
            }
        }
        else if (dado.AlturaCm > 0)
        {
            fila.DadoYCm = dado.AlturaCm;
        }
    }

    // ======================================================================
    //  LA VISTA PREVIA
    // ======================================================================

    /// <summary>
    /// Engancha la vista previa: se redibuja al cambiar de fila y al redimensionar.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="EnlazarPlacaBase"/> porque esto se hace <b>una vez</b>, en el
    /// arranque: <c>Enlazar</c> se vuelve a llamar al cargar el ejemplo, al borrar todo y al
    /// empezar de nuevo, y suscribirse ahí dejaría el mismo evento enganchado cinco veces.
    /// </remarks>
    private void EngancharVistaPreviaPlacaBase()
    {
        PlacaPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaPlacaBase();
        PlacasGrid.SelectionChanged += (_, _) => DibujarVistaPreviaPlacaBase();
    }

    /// <summary>
    /// Dibuja la placa seleccionada en el lienzo de la pestaña.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La geometría es la MISMA que va a AutoCAD</b>, no una versión de pantalla: las anclas las
    /// coloca <see cref="AnclasPlacaBase"/>, los cartabones <see cref="CartabonesPlacaBase"/> y el
    /// perfil <see cref="TrazoAcero"/>, que son las tres clases que usa el dibujante. Es la razón de
    /// que existan sin nada de COM. Una previa que calculara su propia versión podría acabar
    /// enseñando algo distinto de lo que se dibuja, que es justo lo que no puede hacer.
    /// </para>
    /// <para>
    /// Se trabaja <b>en centímetros</b> —escala 1 y origen en cero— y el ajuste al lienzo se hace al
    /// final. Así la escala de pantalla no se mezcla con la del dibujo.
    /// </para>
    /// </remarks>
    private void DibujarVistaPreviaPlacaBase()
    {
        if (PlacaPreviewCanvas is null)
        {
            return;
        }

        PlacaPreviewCanvas.Children.Clear();

        var ancho = PlacaPreviewCanvas.ActualWidth;
        var alto = PlacaPreviewCanvas.ActualHeight;

        if (ancho < 60 || alto < 60)
        {
            return;
        }

        if (PlacasGrid?.SelectedItem is not PlacaBaseRow fila)
        {
            AvisoVistaPlaca("Selecciona una placa de la tabla para verla dibujada.");
            return;
        }

        var p = fila.AFormatoCad();

        // SI FALTAN MEDIDAS SE DICE CUÁLES, con el mismo texto de la columna «Falta». Dibujar una
        // placa imposible enseñaría un borrón, y un borrón no explica nada. Es el mismo criterio que
        // la previa del concreto y la del acero.
        //
        // OJO: se mira p.Falta —lo que falta CAPTURAR— y no fila.Falta, que además incluye los
        // libramientos J y K. Son dos cosas distintas: sin el largo de la placa no hay nada que
        // dibujar, pero unas anclas demasiado juntas SÍ se pueden dibujar, y es justo el caso en el
        // que la previa más sirve —se ve dónde están y por qué no cumplen—. Con fila.Falta aquí, la
        // placa que incumple sería la única que nunca se llegaría a ver.
        if (p.Falta.Count > 0)
        {
            AvisoVistaPlaca("No se puede dibujar todavía: falta " + string.Join("; ", p.Falta) + ".");
            return;
        }

        // Todo en centímetros, con la placa apoyada en el origen.
        var b = p.AnchoDibujoCm;
        var h = p.AltoDibujoCm;

        if (b <= 0 || h <= 0)
        {
            AvisoVistaPlaca("La placa no tiene ancho o alto que dibujar.");
            return;
        }

        var dadoX = p.DadoXDibujoCm;
        var dadoY = p.DadoYDibujoCm;

        var xc = b / 2;
        var yc = h / 2;

        var (pX, pY) = (p.PerfilXDibujoCm, p.PerfilYDibujoCm);

        // El paño del perfil: lo MISMO que usa el dibujante, y de la misma clase. Con él los
        // cartabones arrancan del acero que de verdad tienen al lado —en una I, del alma— en lugar
        // del rectángulo envolvente.
        var panoColumna = p.PanoDeLaColumna(xc, yc, 1);

        var cartabones = CartabonesPlacaBase.Construir(
            p, xc, yc, pX, pY, 1, panoColumna);

        // Las anclas se calculan ANTES de encuadrar, porque el alzado las necesita y el alzado entra
        // en el encuadre. Es la misma cuenta que el dibujante, ajuste al mínimo de la columna K
        // incluido.
        var dAncX = p.DiamAnclaXCm;
        var dAncY = p.DiamAnclaYCm;

        var dAguX = p.DiamAgujeroXCm > 0 ? p.DiamAgujeroXCm : dAncX + (2.54 / 16);
        var dAguY = p.DiamAgujeroYCm > 0 ? p.DiamAgujeroYCm : dAncY + (2.54 / 16);

        var sepX = AnclasPlacaBase.SepBordeAjustada(p.SepBordeXCm, dAncX, b);
        var sepY = AnclasPlacaBase.SepBordeAjustada(p.SepBordeYCm, dAncY, h);

        if (sepX <= 0)
        {
            sepX = AnclasPlacaBase.SepAuto(
                b, pX, dAguX, 1, AnclasPlacaBase.BordeMinimoCm(dAncX));
        }

        if (sepY <= 0)
        {
            sepY = AnclasPlacaBase.SepAuto(
                h, pY, dAguY, 1, AnclasPlacaBase.BordeMinimoCm(dAncY));
        }

        var anclas = AnclasPlacaBase.Construir(
            0, 0, b, h, p.NAnclasX, p.NAnclasY, sepX, sepY,
            dAncX, dAguX, dAncY, dAguY);

        // ---------- EL ALZADO, con la MISMA clase que el dibujante ----------
        // A la derecha de la planta, igual que en el plano. Está en la previa porque es donde se ven
        // los cuatro datos que en planta no se pueden ver —el espesor de la placa, la altura del
        // cartabón, la longitud del ancla y su doblez—: si uno se captura mal, aquí se nota sin
        // abrir AutoCAD.
        var vistas = VistasDeElevacionPrevia(p, b, h, dadoX, dadoY, pX, pY, sepX, sepY);

        // ---------- Lo que ocupa todo, para encuadrar ----------
        // LA CAJA DE TODO, no «el doble de lo que se separa del centro de la placa». Esa cuenta daba
        // por hecho que el dibujo está centrado en la placa, y con el alzado a la derecha ya no lo
        // está: encuadrando así, el alzado se salía del lienzo entero.
        double x1 = 0, y1 = 0, x2 = b, y2 = h;

        void Meter(double x, double y)
        {
            if (x < x1) { x1 = x; }
            if (y < y1) { y1 = y; }
            if (x > x2) { x2 = x; }
            if (y > y2) { y2 = y; }
        }

        void MeterPuntos(double[] pts)
        {
            for (var i = 0; i + 1 < pts.Length; i += 2)
            {
                Meter(pts[i], pts[i + 1]);
            }
        }

        if (dadoX > 0 && dadoY > 0)
        {
            Meter(xc - (dadoX / 2), yc - (dadoY / 2));
            Meter(xc + (dadoX / 2), yc + (dadoY / 2));
        }

        foreach (var c in cartabones)
        {
            MeterPuntos(c.Puntos);
        }

        foreach (var v in vistas)
        {
            MeterPuntos(v.Concreto);
            MeterPuntos(v.Columna);

            foreach (var c in v.Cartabones)
            {
                MeterPuntos(c);
            }

            foreach (var a in v.Anclas)
            {
                MeterPuntos(a.Vastago);
                MeterPuntos(a.Tuerca);
            }
        }

        const double margen = 34;

        var escala = Math.Min(
            (ancho - (2 * margen)) / (x2 - x1),
            (alto - (2 * margen)) / (y2 - y1));

        if (escala <= 0 || double.IsInfinity(escala) || double.IsNaN(escala))
        {
            return;
        }

        // De centímetros con la Y hacia arriba a píxeles con la Y hacia abajo, centrado en la CAJA.
        var dx = (ancho / 2) - ((x1 + x2) / 2 * escala);
        var dy = (alto / 2) + ((y1 + y2) / 2 * escala);

        var transformar = new TransformGroup();
        transformar.Children.Add(new ScaleTransform(escala, -escala));
        transformar.Children.Add(new TranslateTransform(dx, dy));

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var grisAcero = new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xD3));
        var concreto = new SolidColorBrush(Color.FromRgb(0xD8, 0xD3, 0xC8));
        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x2A, 0x1B));

        // ---------- El dado, al fondo ----------
        if (dadoX > 0 && dadoY > 0)
        {
            var geoDado = p.DadoCircular
                ? new EllipseGeometry(new Point(xc, yc), dadoX / 2, dadoX / 2)
                : (Geometry)new RectangleGeometry(new Rect(
                    xc - (dadoX / 2), yc - (dadoY / 2), dadoX, dadoY));

            geoDado.Transform = transformar;

            PlacaPreviewCanvas.Children.Add(new FormaPath
            {
                Data = geoDado,
                Fill = concreto,
                Stroke = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x78)),
                StrokeThickness = 1.2
            });
        }

        // ---------- La placa ----------
        var geoPlaca = new RectangleGeometry(new Rect(0, 0, b, h)) { Transform = transformar };

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = geoPlaca,
            Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xC3, 0xCB, 0xD3)),
            Stroke = azul,
            StrokeThickness = 2
        });

        // ---------- Los cartabones, con su soldadura morada debajo ----------
        if (cartabones.Count > 0)
        {
            DibujarSoldaduraDeCartabonesPrevia(p, cartabones, transformar);

            // POLIGONAL Y NO RECTÁNGULO: contra una columna redonda el cartabón lleva la boca de
            // pescado, que es un ARCO. Con RectangleGeometry la previa enseñaría un cartabón recto
            // y el plano saldría con el recorte: precisamente la discrepancia que la previa existe
            // para evitar.
            var geoCart = new GeometryGroup { Transform = transformar };

            foreach (var c in cartabones)
            {
                AgregarPoligonal(geoCart, c.Puntos, c.Dobleces);
            }

            PlacaPreviewCanvas.Children.Add(new FormaPath
            {
                Data = geoCart,
                Fill = new SolidColorBrush(Color.FromRgb(0x9A, 0xA6, 0xB2)),
                Stroke = azul,
                StrokeThickness = 1
            });
        }

        // ---------- El perfil de la columna, y la franja de soldadura ----------
        if (p.Perfil is not null)
        {
            var trazo = TrazoAcero.De(
                p.Perfil, xc - (p.Perfil.AnchoDibujoCm / 2), yc - (p.Perfil.AltoDibujoCm / 2), 1);

            if (trazo is not null)
            {
                // LA SOLDADURA VA PRIMERO, debajo del perfil. Es la franja entre el paño del perfil
                // y ese mismo paño corrido hacia fuera el espesor del filete, y se calcula con
                // ContornoDesplazado: LA MISMA clase que usa el dibujante.
                //
                // Está en la previa porque es justo lo que estaba mal —la franja se dibujaba como
                // la caja del perfil crecida, así que en una I el rayado llenaba toda la caja— y
                // aquí se ve sin abrir AutoCAD.
                DibujarSoldaduraPrevia(p, trazo, xc, yc, transformar);

                var geoPerfil = new GeometryGroup
                {
                    FillRule = FillRule.EvenOdd,
                    Transform = transformar
                };

                AgregarPerfilGirado(geoPerfil, trazo, p.GiraElPerfil, xc, yc);

                PlacaPreviewCanvas.Children.Add(new FormaPath
                {
                    Data = geoPerfil,
                    Fill = grisAcero,
                    Stroke = azul,
                    StrokeThickness = 1.6,
                    StrokeLineJoin = PenLineJoin.Round
                });
            }
        }

        // ---------- El alzado, detrás de las anclas de la planta ----------
        DibujarElevacionPrevia(vistas, transformar);

        // ---------- Las anclas: el agujero y el ancla, como en el detalle ----------
        if (anclas.Count > 0)
        {
            var geoAgujeros = new GeometryGroup { Transform = transformar };
            var geoAnclas = new GeometryGroup { Transform = transformar };

            foreach (var a in anclas)
            {
                geoAgujeros.Children.Add(new EllipseGeometry(
                    new Point(a.X, a.Y), a.DAgujero / 2, a.DAgujero / 2));

                geoAnclas.Children.Add(new EllipseGeometry(
                    new Point(a.X, a.Y), a.DAncla / 2, a.DAncla / 2));
            }

            PlacaPreviewCanvas.Children.Add(new FormaPath
            {
                Data = geoAgujeros,
                Fill = Brushes.White,
                Stroke = azul,
                StrokeThickness = 1
            });

            PlacaPreviewCanvas.Children.Add(new FormaPath
            {
                Data = geoAnclas,
                Stroke = rojo,
                StrokeThickness = 1.4
            });
        }

        // ---------- Lo que hay que poder leer sin medir ----------
        var titulo = fila.Marca.Trim().Length > 0 ? fila.Marca.Trim() : "placa sin marca";

        EtiquetaPlaca(
            $"{titulo}    ·    PLACA {b:N0} × {h:N0} × {fila.Espesor}\"",
            10, alto - 40, 12.5, azul, negrita: true);

        var resumen = $"{anclas.Count} ancla(s) de {fila.DiamAnclaX}\"";

        if (p.NAnclasY > 0 && !string.Equals(fila.DiamAnclaX, fila.DiamAnclaY))
        {
            resumen += $" y {fila.DiamAnclaY}\"";
        }

        resumen += $"    ·    sep. borde {sepX:N1} / {sepY:N1} cm";

        if (cartabones.Count > 0)
        {
            resumen += $"    ·    {cartabones.Count} cartabón(es)";
        }

        if (dadoX > 0 && dadoY > 0)
        {
            resumen += p.DadoCircular
                ? $"    ·    dado Ø{dadoX:N0}"
                : $"    ·    dado {dadoX:N0} × {dadoY:N0}";
        }

        EtiquetaPlaca(resumen, 10, alto - 22, 11, Brushes.DimGray);

        // Y EL AVISO DE LOS LIBRAMIENTOS, EN ROJO Y ARRIBA. Es lo único de la previa que significa
        // «esto no se va a dibujar»: el dibujante se niega si no se cumplen las tablas J o K, así
        // que verlo aquí es enterarse con la fila delante y no cuando el botón no hace nada.
        var libramiento = fila.Libramientos;

        if (libramiento.Length > 0)
        {
            EtiquetaPlaca("NO SE DIBUJARÁ — " + libramiento, 10, 8, 11.5, rojo, negrita: true);
        }
    }

    /// <summary>
    /// La franja de soldadura de la vista previa: el paño del perfil corrido hacia fuera.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se dibuja como el <b>hueco</b> entre dos contornos, con la regla par-impar: el desplazado por
    /// fuera y el del perfil por dentro. Es lo mismo que hace el hatch de AutoCAD con el perfil como
    /// isla, así que lo que se ve aquí es lo que va a salir en el plano.
    /// </para>
    /// <para>
    /// Los arcos se muestrean igual que en el perfil, y los <b>bulges se conservan</b> al desplazar:
    /// correr un arco hacia fuera le cambia el radio pero no el ángulo que barre.
    /// </para>
    /// </remarks>
    private void DibujarSoldaduraPrevia(
        PlacaBaseCad p, TrazoAcero.Trazo trazo, double xc, double yc, Transform transformar)
    {
        var t = p.SoldaduraCm;

        if (t <= 0)
        {
            return;
        }

        var rojoSoldadura = new SolidColorBrush(Color.FromArgb(0x80, 0xC0, 0x2A, 0x1B));
        var franja = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = transformar };

        // ---- El tubo redondo y el macizo: la franja es un anillo ----
        if (trazo.CircExterior is { } circ && trazo.Exterior is null)
        {
            var (cx, cy) = GirarSiToca(circ.Cx, circ.Cy, p.GiraElPerfil, xc, yc);

            franja.Children.Add(new EllipseGeometry(new Point(cx, cy), circ.R + t, circ.R + t));
            franja.Children.Add(new EllipseGeometry(new Point(cx, cy), circ.R, circ.R));
        }
        else if (trazo.Exterior is { } contorno)
        {
            var puntos = p.GiraElPerfil
                ? GirarPuntos(contorno.Puntos, xc, yc)
                : contorno.Puntos;

            var fuera = ContornoDesplazado.HaciaFuera(puntos, t);

            if (fuera is null)
            {
                return;
            }

            AgregarPoligonal(franja, fuera, contorno.Dobleces);
            AgregarPoligonal(franja, puntos, contorno.Dobleces);
        }

        if (franja.Children.Count == 0)
        {
            return;
        }

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = franja,
            Fill = rojoSoldadura,
            Stroke = new SolidColorBrush(Color.FromRgb(0xC0, 0x2A, 0x1B)),
            StrokeThickness = 0.8
        });
    }

    /// <summary>
    /// La franja <b>morada</b> que rodea el contorno de cada cartabón.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Igual que la del perfil: el hueco entre el contorno del cartabón y ese mismo contorno corrido
    /// hacia fuera el espesor de su filete, con la regla par-impar, y con
    /// <see cref="ContornoDesplazado"/> —LA MISMA clase que usa el dibujante—.
    /// </para>
    /// <para>
    /// <b>Morada y no roja</b>, que es el mismo color con que sale en el plano: las dos franjas
    /// quedan a un centímetro una de otra y del mismo color se leen como una sola soldadura. Van
    /// todas en un solo <c>FormaPath</c> porque son la misma soldadura repetida.
    /// </para>
    /// </remarks>
    private void DibujarSoldaduraDeCartabonesPrevia(
        PlacaBaseCad p, List<CartabonesPlacaBase.Cartabon> cartabones, Transform transformar)
    {
        var t = p.SoldaduraCartabonCm;

        if (t <= 0)
        {
            return;
        }

        var franja = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = transformar };

        foreach (var c in cartabones)
        {
            var fuera = ContornoDesplazado.HaciaFuera(c.Puntos, t);

            if (fuera is null)
            {
                continue;
            }

            AgregarPoligonal(franja, fuera, c.Dobleces);
            AgregarPoligonal(franja, c.Puntos, c.Dobleces);
        }

        if (franja.Children.Count == 0)
        {
            return;
        }

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = franja,
            Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x7B, 0x2F, 0xBE)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x7B, 0x2F, 0xBE)),
            StrokeThickness = 0.8
        });
    }

    /// <summary>
    /// Las vistas de alzado de la previa, con <b>la misma clase</b> que usa el dibujante.
    /// </summary>
    /// <remarks>
    /// Es lo mismo que hace <c>PlacaBaseDrawer.Elevacion</c>, con las mismas dos direcciones y el
    /// mismo cruce X/Y. Con una copia de la cuenta aquí, la previa y el plano podrían discrepar, y
    /// justo en los datos que solo el alzado enseña.
    /// </remarks>
    private static List<ElevacionPlacaBase.Vista> VistasDeElevacionPrevia(
        PlacaBaseCad p, double b, double h, double dadoX, double dadoY,
        double pX, double pY, double sepX, double sepY)
    {
        if (!p.DibujarElevacion)
        {
            return new List<ElevacionPlacaBase.Vista>();
        }

        // A la derecha del canto de la planta, y de lo que OCUPA -con su dado-, no de la placa.
        var xInicio = Math.Max(b, dadoX) + ElevacionPlacaBase.SeparacionDeLaPlantaCm;

        return ElevacionPlacaBase.Construir(
            xInicio, h / 2, 1, AlturaTextoPrevia, p.EspesorCm, p.ConCartabones,
            DireccionDePrevia(p, b, dadoX, pX, esX: true, sepX, p.DiamAnclaXCm),
            DireccionDePrevia(p, h, dadoY, pY, esX: false, sepY, p.DiamAnclaYCm));
    }

    /// <summary>
    /// La altura de texto con la que el alzado separa su rótulo, en cm.
    /// </summary>
    /// <remarks>
    /// La previa no rotula, así que este número solo mueve el punto del rótulo —que aquí no se
    /// dibuja—. Se pasa igual para que las dos llamadas a <see cref="ElevacionPlacaBase.Construir"/>
    /// reciban lo mismo y la geometría salga idéntica.
    /// </remarks>
    private const double AlturaTextoPrevia = 1.6;

    private static ElevacionPlacaBase.Direccion DireccionDePrevia(
        PlacaBaseCad p, double anchoPlaca, double anchoDado, double anchoPerfil,
        bool esX, double sepBorde, double diamAncla) =>
        new(
            AnchoPlaca: anchoPlaca,
            AnchoDado: p.DibujarDado ? anchoDado : 0,
            AnchoPerfil: anchoPerfil,
            LongCartabon: esX ? p.LongCartabonXCm : p.LongCartabonYCm,
            AltoCartabon: esX ? p.AltoCartabonXCm : p.AltoCartabonYCm,
            CuantosCartabones: Math.Max(0, esX ? p.NCartabonesX : p.NCartabonesY),
            LongAnclaje: esX ? p.LongAnclajeXCm : p.LongAnclajeYCm,
            LongAncla: esX ? p.LongAnclaXCm : p.LongAnclaYCm,
            DoblezAncla: esX ? p.DoblezAnclaXCm : p.DoblezAnclaYCm,
            SepBorde: sepBorde,
            DiamAncla: diamAncla,
            CuantasAnclas: Math.Max(0, esX ? p.NAnclasX : p.NAnclasY));

    /// <summary>
    /// El alzado en la previa: concreto, placa de canto, columna, cartabones y anclas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con los <b>mismos colores</b> que la planta, para que se lea como el mismo detalle: el
    /// concreto beige, la placa azul, el acero gris y las anclas rojas.
    /// </para>
    /// <para>
    /// El vástago del ancla va como <b>poligonal abierta</b>, que con doblez tiene tres puntos.
    /// Cerrándola, la punta de la pata se uniría con el arranque de arriba y el ancla saldría como un
    /// triángulo: es el mismo cuidado que en el dibujante.
    /// </para>
    /// </remarks>
    private void DibujarElevacionPrevia(
        List<ElevacionPlacaBase.Vista> vistas, Transform transformar)
    {
        if (vistas.Count == 0)
        {
            return;
        }

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x2A, 0x1B));

        var geoConcreto = new GeometryGroup { Transform = transformar };
        var geoAcero = new GeometryGroup { Transform = transformar };
        var geoPlaca = new GeometryGroup { Transform = transformar };
        var geoAnclas = new GeometryGroup { Transform = transformar };

        foreach (var v in vistas)
        {
            AgregarPoligonal(geoConcreto, v.Concreto, null);
            AgregarPoligonal(geoPlaca, v.Placa, null);
            AgregarPoligonal(geoAcero, v.Columna, null);

            foreach (var c in v.Cartabones)
            {
                AgregarPoligonal(geoAcero, c, null);
            }

            foreach (var a in v.Anclas)
            {
                AgregarAbierta(geoAnclas, a.Vastago);
                AgregarPoligonal(geoAnclas, a.Tuerca, null);
                AgregarAbierta(geoAnclas, a.Arandela);

                if (a.Remate is { } remate)
                {
                    AgregarAbierta(geoAnclas, remate);
                }
            }
        }

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = geoConcreto,
            Fill = new SolidColorBrush(Color.FromRgb(0xD8, 0xD3, 0xC8)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x78)),
            StrokeThickness = 1.2
        });

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = geoPlaca,
            Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xC3, 0xCB, 0xD3)),
            Stroke = azul,
            StrokeThickness = 2
        });

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = geoAcero,
            Fill = new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xD3)),
            Stroke = azul,
            StrokeThickness = 1.2
        });

        PlacaPreviewCanvas.Children.Add(new FormaPath
        {
            Data = geoAnclas,
            Stroke = rojo,
            StrokeThickness = 1.4
        });
    }

    /// <summary>Una poligonal <b>abierta</b>, de dos o tres puntos, sin relleno.</summary>
    private static void AgregarAbierta(GeometryGroup grupo, double[] puntos)
    {
        if (puntos.Length < 4)
        {
            return;
        }

        var figura = new PathFigure
        {
            StartPoint = new Point(puntos[0], puntos[1]),
            IsClosed = false,
            IsFilled = false
        };

        for (var i = 2; i + 1 < puntos.Length; i += 2)
        {
            figura.Segments.Add(new LineSegment(new Point(puntos[i], puntos[i + 1]), true));
        }

        var geo = new PathGeometry();
        geo.Figures.Add(figura);

        grupo.Children.Add(geo);
    }

    /// <summary>Gira 90° un arreglo plano de puntos, con el mismo giro que el dibujante.</summary>
    private static double[] GirarPuntos(double[] puntos, double xc, double yc)
    {
        var salida = new double[puntos.Length];

        for (var i = 0; i + 1 < puntos.Length; i += 2)
        {
            var (x, y) = GirarSiToca(puntos[i], puntos[i + 1], true, xc, yc);

            salida[i] = x;
            salida[i + 1] = y;
        }

        return salida;
    }

    /// <summary>
    /// Mete una poligonal con arcos en el grupo, muestreando los arcos.
    /// </summary>
    /// <remarks>
    /// El bulge de una polilínea es <c>tan(ángulo / 4)</c>, así que el ángulo que barre el arco sale
    /// de <c>4·atan(bulge)</c> y el radio, de la cuerda. Se muestrea con veinte tramos, que a este
    /// tamaño es de sobra para que un doblez se vea curvo.
    /// </remarks>
    private static void AgregarPoligonal(
        GeometryGroup grupo, double[] puntos, (int Indice, double Bulge)[]? dobleces)
    {
        var n = puntos.Length / 2;

        if (n < 3)
        {
            return;
        }

        var bulge = new double[n];

        if (dobleces is not null)
        {
            foreach (var (indice, b) in dobleces)
            {
                if (indice >= 0 && indice < n)
                {
                    bulge[indice] = b;
                }
            }
        }

        var figura = new PathFigure
        {
            StartPoint = new Point(puntos[0], puntos[1]),
            IsClosed = true,
            IsFilled = true
        };

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;

            var x1 = puntos[2 * i];
            var y1 = puntos[(2 * i) + 1];
            var x2 = puntos[2 * j];
            var y2 = puntos[(2 * j) + 1];

            if (Math.Abs(bulge[i]) < 1e-9)
            {
                figura.Segments.Add(new LineSegment(new Point(x2, y2), true));
                continue;
            }

            var angulo = 4 * Math.Atan(bulge[i]);
            var cuerda = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));

            if (cuerda < 1e-12 || Math.Abs(Math.Sin(angulo / 2)) < 1e-12)
            {
                figura.Segments.Add(new LineSegment(new Point(x2, y2), true));
                continue;
            }

            var radio = cuerda / (2 * Math.Sin(angulo / 2));

            figura.Segments.Add(new ArcSegment(
                new Point(x2, y2),
                new Size(Math.Abs(radio), Math.Abs(radio)),
                0,
                Math.Abs(angulo) > Math.PI,
                angulo > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
                true));
        }

        var geo = new PathGeometry();
        geo.Figures.Add(figura);

        grupo.Children.Add(geo);
    }

    /// <summary>Mete el trazo del perfil en el grupo, girándolo si le toca.</summary>
    /// <remarks>
    /// El giro es el <b>mismo</b> que aplica el dibujante —<c>xd = xc - y ; yd = yc + x</c> sobre las
    /// coordenadas locales— y se aplica igual a los contornos y a los círculos. Se hace aquí sobre
    /// los puntos, y no pidiéndole otro trazo a <see cref="TrazoAcero"/>, por lo mismo que allí: así
    /// el giro es uno solo para las nueve formas.
    /// </remarks>
    private static void AgregarPerfilGirado(
        GeometryGroup grupo, TrazoAcero.Trazo trazo, bool girar, double xc, double yc)
    {
        foreach (var contorno in new[] { trazo.Exterior, trazo.Interior })
        {
            if (contorno is null)
            {
                continue;
            }

            // Los arcos se muestrean: un lienzo de WPF no tiene bulges. Veinte tramos por arco es
            // de sobra para que el doblez de una lámina se vea curvo a este tamaño.
            var pts = TrazoAcero.Muestrear(contorno, 20);

            if (pts.Count < 3)
            {
                continue;
            }

            var primero = GirarSiToca(pts[0].X, pts[0].Y, girar, xc, yc);

            var figura = new PathFigure
            {
                StartPoint = new Point(primero.X, primero.Y),
                IsClosed = true,
                IsFilled = true
            };

            for (var k = 1; k < pts.Count; k++)
            {
                var q = GirarSiToca(pts[k].X, pts[k].Y, girar, xc, yc);

                figura.Segments.Add(new LineSegment(new Point(q.X, q.Y), true));
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

            var c = GirarSiToca(circulo.Cx, circulo.Cy, girar, xc, yc);

            grupo.Children.Add(new EllipseGeometry(new Point(c.X, c.Y), circulo.R, circulo.R));
        }
    }

    private static (double X, double Y) GirarSiToca(
        double x, double y, bool girar, double xc, double yc) =>
        girar ? (xc - (y - yc), yc + (x - xc)) : (x, y);

    /// <summary>Un aviso centrado en la vista previa de placas.</summary>
    private void AvisoVistaPlaca(string texto) =>
        EtiquetaPlaca(texto, 14, 34, 12, Brushes.Gray);

    /// <summary>Un texto en el lienzo de la vista previa de placas.</summary>
    private void EtiquetaPlaca(
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

        PlacaPreviewCanvas.Children.Add(t);
    }

    /// <summary>Pone al día el dado de TODAS las placas.</summary>
    /// <remarks>
    /// Se llama desde donde se refrescan las listas de las otras hojas: al agregar, borrar o
    /// <b>editar</b> una sección de concreto. Editar importa igual que agregar —si el dado crece en
    /// su hoja, la placa que lo usa tiene que crecer con él— y es lo que hace que la medida sea una
    /// referencia y no una copia que envejece.
    /// </remarks>
    private void ReferenciarDadosDeTodasLasPlacas()
    {
        foreach (var fila in _datos.PlacasBase)
        {
            ReferenciarDadoDePlaca(fila);
        }
    }

    // EsDado NO SE VUELVE A ESCRIBIR AQUÍ. Ya existe en MainWindow.Zapatas.cs, y MainWindow es UNA
    // clase partida en varios archivos: declararlo otra vez es el error CS0111. Y además es lo
    // correcto: el criterio de qué cuenta como dado —que el elemento empiece por «DADO», así entran
    // «DADO» y «DADO CIRCULAR»— tiene que ser el mismo para las dos hojas que ofrecen la lista.

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
        IdDado = f.IdDado,
        DadoXCm = f.DadoXCm,
        DadoYCm = f.DadoYCm,
        DadoCircular = f.DadoCircular,

        // LOS AGUJEROS NO SE COPIAN, y no es un olvido: son calculados —el ancla más 1/16"— así
        // que copiando el ancla, el agujero de la copia sale solo y no puede quedar descolgado.
        Familia = f.Familia,
        Seccion = f.Seccion,
        NAnclasX = f.NAnclasX,
        NAnclasY = f.NAnclasY,
        SepBordeXCm = f.SepBordeXCm,
        SepBordeYCm = f.SepBordeYCm,
        DiamAnclaX = f.DiamAnclaX,
        DiamAnclaY = f.DiamAnclaY,
        Electrodo = f.Electrodo,
        Soldadura = f.Soldadura,
        SoldaduraCartabon = f.SoldaduraCartabon,
        NCartabonesX = f.NCartabonesX,
        NCartabonesY = f.NCartabonesY,
        EspCartabonX = f.EspCartabonX,
        EspCartabonY = f.EspCartabonY,
        LongCartabonXCm = f.LongCartabonXCm,
        LongCartabonYCm = f.LongCartabonYCm,
        AltoCartabonXCm = f.AltoCartabonXCm,
        AltoCartabonYCm = f.AltoCartabonYCm,
        LongAnclajeXCm = f.LongAnclajeXCm,
        LongAnclajeYCm = f.LongAnclajeYCm,
        LongAnclaXCm = f.LongAnclaXCm,
        LongAnclaYCm = f.LongAnclaYCm,
        DoblezAnclaXCm = f.DoblezAnclaXCm,
        DoblezAnclaYCm = f.DoblezAnclaYCm,
        ConCartabones = f.ConCartabones,
        Escala = f.Escala,
        GirarPlaca90 = f.GirarPlaca90
    };

    /// <summary>Quita la placa seleccionada.</summary>
    private void OnQuitarPlaca(object sender, RoutedEventArgs e)
    {
        // El caso bueno primero y el aviso al final, que es como se lee de corrido.
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
