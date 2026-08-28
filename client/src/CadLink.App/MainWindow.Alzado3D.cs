using System.Windows;

// System.Windows.Controls es donde vive Canvas, y de ahi salen Canvas.SetLeft y
// Canvas.SetTop, que son las que colocan una figura dentro del lienzo. Sin este using el
// error que sale es un CS0103 diciendo que «Canvas» no existe, que despista bastante:
// parece que falte una referencia y lo que falta es el espacio de nombres.
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// El alzado <b>en 3D</b>: la jaula de armado vista en isométrico.
/// </summary>
/// <remarks>
/// <para>
/// Es la misma pieza que el alzado plano, con los mismos datos y las mismas posiciones
/// —los estribos salen de <c>Estribos.CentrosDeAlzado</c> y las varillas de
/// <c>PosicionesDeLecho</c>/<c>PosicionesLaterales</c>, igual que el corte—, solo
/// proyectada. Si se calcularan aparte, la vista en 3D enseñaría un armado y el plano
/// otro.
/// </para>
/// <para>
/// La proyección es un <b>isométrico</b> hecho a mano sobre un <c>Canvas</c>, sin
/// <c>Viewport3D</c>. Es la misma decisión que ya está razonada en <c>VistaModelo</c>: WPF
/// 3D no tiene primitiva de línea, y una jaula de armado es toda líneas.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Si el alzado se ve en 3D en lugar de plano.</summary>
    private bool _alzado3D;

    /// <summary>Largo que se supone cuando la fila no lo trae, en metros.</summary>
    /// <remarks>
    /// Tres metros es un tramo de trabe corriente. Hace falta un valor porque sin largo
    /// no hay alzado que dibujar, y dejarlo en cero enseñaba un recuadro vacío sin
    /// explicar por qué.
    /// </remarks>
    public const double LargoPorOmisionM = 3.0;

    private void OnAlternarAlzado3D(object sender, RoutedEventArgs e)
    {
        _alzado3D = !_alzado3D;
        AlzadoVistaButton.Content = _alzado3D ? "3D" : "2D";

        AlzadoVistaButton.ToolTip = _alzado3D
            ? "Viendo el alzado en 3D. Toca para volver al plano."
            : "Viendo el alzado plano. Toca para verlo en 3D.";

        DibujarVistaPrevia();
    }

    /// <summary>Dibuja la jaula de armado en isométrico.</summary>
    /// <param name="a">Los datos del alzado, los mismos que usa el dibujo plano.</param>
    /// <param name="izquierda">Desde dónde se puede ocupar el lienzo.</param>
    /// <param name="alto">Alto disponible.</param>
    private void DibujarAlzado3DPrevio(AlzadoCad a, double izquierda, double alto)
    {
        var largoM = a.LongitudM > 0 ? a.LongitudM : LargoPorOmisionM;

        if (a.BaseCm <= 0 || a.AlturaCm <= 0)
        {
            return;
        }

        // Todo en centímetros: el largo de la pieza va en X, el peralte en Y y la base
        // en Z. Así la pieza se ve tumbada, como en el alzado plano.
        var lx = largoM * 100.0;
        var hy = a.AlturaCm;
        var bz = a.BaseCm;

        // ---------- El isométrico ----------
        //
        // Los tres ejes a 120°, que es el isométrico de toda la vida: X y Z se abren en
        // diagonal y Y sube. cos30 y sen30 son las dos constantes que hacen falta.
        const double c30 = 0.86602540378443864;
        const double s30 = 0.5;

        // La escala sale de encajar la caja proyectada en el hueco que queda. Se calcula
        // con las esquinas de la caja, no con el largo a secas: en isométrico el ancho en
        // pantalla depende de los tres lados a la vez.
        var anchoIso = (lx + bz) * c30;
        var altoIso = hy + ((lx + bz) * s30);

        var anchoDisp = PreviaFijaCanvas.ActualWidth - izquierda - 24;
        var altoDisp = alto - 52;

        if (anchoDisp < 40 || altoDisp < 40 || anchoIso <= 0 || altoIso <= 0)
        {
            return;
        }

        var k = Math.Min(anchoDisp / anchoIso, altoDisp / altoIso);

        if (k <= 0 || double.IsInfinity(k))
        {
            return;
        }

        // El origen: la esquina de atrás a la izquierda de la caja, colocada de modo que
        // todo lo proyectado caiga dentro del hueco.
        var ox = izquierda + (bz * c30 * k);
        var oy = 34 + (hy * k) + (bz * s30 * k);

        Point P(double x, double y, double z) => new(
            ox + ((x - z) * c30 * k),
            oy - (y * k) + ((x + z) * s30 * k));

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));

        void Linea3D(Point p, Point q, Brush brocha, double grosor, double opacidad = 1.0)
        {
            PreviaFijaCanvas.Children.Add(new Line
            {
                X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
                Stroke = brocha,
                StrokeThickness = grosor,
                Opacity = opacidad
            });
        }

        // ---------- La caja de concreto, en alambre ----------
        //
        // Va tenue y en alambre a propósito: lo que hay que mirar es el armado, y una
        // caja opaca lo taparía entero. Es lo mismo que hace el visor de ETABS con el
        // modelo.
        var esquinas = new[]
        {
            P(0, 0, 0), P(lx, 0, 0), P(lx, hy, 0), P(0, hy, 0),
            P(0, 0, bz), P(lx, 0, bz), P(lx, hy, bz), P(0, hy, bz)
        };

        var aristas = new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        };

        foreach (var (i, j) in aristas)
        {
            Linea3D(esquinas[i], esquinas[j], azul, 1.0, 0.45);
        }

        // ---------- Los estribos ----------
        //
        // Uno por cada posición que dice Estribos.CentrosDeAlzado, la MISMA función que
        // usa el dibujo plano y la que usa AutoCAD.
        var centros = Estribos.CentrosDeAlzado(
            largoM,
            a.SeparacionesCm[0] / 100, a.SeparacionesCm[1] / 100, a.SeparacionesCm[2] / 100,
            vertical: a.EsVertical,
            esColumna: a.Tipo == TipoElemento.Columna);

        var rec = a.RecubrimientoCm;

        // ---------- El armado, apuntado para pintarlo de atrás hacia delante ----------
        // Mismo trato que la sección de pie, y por lo mismo: sin ordenar por profundidad,
        // quién tapa a quién lo decide el orden del código y los cruces no se leen.
        var piezas = new List<(double Prof, Action Pintar)>();

        void Barra(
            double x1, double y1, double z1,
            double x2, double y2, double z2,
            Color color, double diamCm)
        {
            var p = P(x1, y1, z1);
            var q = P(x2, y2, z2);

            // El grueso ES el diámetro a la escala, con un único tope mínimo para todas
            // las familias: así se conservan las proporciones entre calibres.
            var grueso = Math.Max(diamCm * k, 0.7);

            piezas.Add((
                (x1 + z1 + x2 + z2) / 2,
                () => BarraRedonda3D(PreviaFijaCanvas, p, q, color, grueso)));
        }

        var colorEstriboAlz = Color.FromRgb(0x1F, 0x6F, 0xB2);
        var colorVarillaAlz = Color.FromRgb(0x1D, 0x8A, 0x4E);

        var fila = Seleccionada;

        // Las varillas y el diamante necesitan la fila; los estribos, no.
        var conArmado = fila is not null && !fila.EsCircular;

        // El diámetro del estribo PRINCIPAL. Antes aquí había un 1.1 fijo, así que un #3 y
        // un #5 se veían idénticos.
        //
        // Y NO se usa a.EstriboDibujo, aunque sea lo que usa el alzado plano: ese campo
        // trae el del DIAMANTE cuando la sección lleva diamante —se arma así en
        // MainWindow.xaml.cs, «diamante && varDiamante.Existe ? varDiamante : estribo»—.
        // Aquí hacen falta los dos por separado, que es justo el error que se está
        // arreglando. Solo se cae a EstriboDibujo cuando no hay fila de la que sacarlo.
        var deAlz = conArmado
                    && Varilla.TryDiametroCm(fila!.Estribo, out var dEstFila)
                    && dEstFila > 0
            ? dEstFila
            : a.EstriboDibujo.Cm;

        var varillasAlz = conArmado
            ? TodasLasVarillas(fila!, deAlz, fila!.RecubrimientoCm)
            : new List<(RefVarilla Ref, double X, double Y, double R)>();

        // Las varillas longitudinales, de las mismas funciones que reparten las del corte.
        // En el corte la X es a lo ancho de la sección, que aquí es la Z; y la Y del corte
        // es el peralte, que aquí sigue siendo Y.
        foreach (var (_, zx, vy, vr) in varillasAlz)
        {
            Barra(0, vy, zx, lx, vy, zx, colorVarillaAlz, vr * 2);
        }

        var dDiaAlz = conArmado ? DiametroDelDiamante(fila!, deAlz) : 0;
        var hayDiamanteAlz = conArmado && fila!.LlevaDiamante && dDiaAlz > 0;

        var recorridoAlz = hayDiamanteAlz
            ? RecorridoDelDiamante3D(fila!, deAlz, fila!.RecubrimientoCm, dDiaAlz)
            : null;

        foreach (var c in centros)
        {
            var xEst = c * 100.0;

            // El estribo: un rectángulo en el plano de la sección, o sea a X fija.
            if (rec > 0 && rec * 2 < bz && rec * 2 < hy)
            {
                var e = new[]
                {
                    (Y: rec, Z: rec), (Y: rec, Z: bz - rec),
                    (Y: hy - rec, Z: bz - rec), (Y: hy - rec, Z: rec)
                };

                for (var v = 0; v < 4; v++)
                {
                    var p1 = e[v];
                    var p2 = e[(v + 1) % 4];

                    Barra(xEst, p1.Y, p1.Z, xEst, p2.Y, p2.Z, colorEstriboAlz, deAlz);
                }
            }

            // El diamante, apilado sobre el estribo y tangente a él. Antes el alzado en 3D
            // NO lo dibujaba: la jaula enseñaba un armado y el corte otro.
            var xDia = xEst + ((deAlz + dDiaAlz) / 2);

            if (recorridoAlz is not null)
            {
                for (var i = 0; i < recorridoAlz.Count; i++)
                {
                    var p1 = recorridoAlz[i];
                    var p2 = recorridoAlz[(i + 1) % recorridoAlz.Count];

                    Barra(xDia, p1.Y, p1.X, xDia, p2.Y, p2.X, colorEstriboAlz, dDiaAlz);
                }
            }

            // Y las grapas encima, cada una con SU diámetro.
            var xGrapa = hayDiamanteAlz ? xDia + (dDiaAlz / 2) : xEst + (deAlz / 2);

            if (!conArmado)
            {
                continue;
            }

            foreach (var g in fila!.Grapas)
            {
                if (!Varilla.TryDiametroCm(g.Diametro, out var dGrapa) || dGrapa <= 0)
                {
                    dGrapa = deAlz;
                }

                var va = BuscarVarillaPrevia(varillasAlz, g.A);
                var vb = BuscarVarillaPrevia(varillasAlz, g.B);

                if (va is null || vb is null)
                {
                    continue;
                }

                xGrapa += dGrapa / 2;

                Barra(xGrapa, va.Value.Y, va.Value.X,
                      xGrapa, vb.Value.Y, vb.Value.X, colorEstriboAlz, dGrapa);

                xGrapa += dGrapa / 2;
            }
        }

        foreach (var (_, pintar) in piezas.OrderBy(p => p.Prof))
        {
            pintar();
        }

        Etiqueta(PreviaFijaCanvas,
            $"ALZADO 3D  {a.TipoTexto}  {a.Id}", izquierda, 12);

        Etiqueta(PreviaFijaCanvas,
            $"L = {largoM:N2} m   ·   {centros.Count} estribos   ·   "
            + $"{a.SeparacionesCm[0]:N0}-{a.SeparacionesCm[1]:N0}-{a.SeparacionesCm[2]:N0} cm"
            + (a.LongitudM > 0 ? string.Empty : "   ·   largo por omisión"),
            izquierda, alto - 16);
    }
}


public partial class MainWindow
{
    /// <summary>
    /// La <b>sección en corte</b> vista en 3D: una rebanada de la pieza en isométrico.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la misma sección del corte plano, con las mismas varillas y el mismo estribo
    /// —salen de <see cref="TodasLasVarillas"/> y del recubrimiento de la fila—, solo
    /// proyectada y con un poco de fondo para que se vea que es un cuerpo y no un dibujo.
    /// </para>
    /// <para>
    /// Se dibuja una <b>rebanada corta</b>, no la pieza entera: lo que interesa aquí es el
    /// acomodo del armado en la sección, y una rebanada lo enseña sin tapar las varillas
    /// del fondo. La pieza completa ya se ve a la derecha, en el alzado en 3D.
    /// </para>
    /// </remarks>
    /// <summary>
    /// La sección en 3D: <b>el elemento de pie</b>, con sus estribos a la separación real.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No es una rebanada: es la pieza <b>levantada</b> a su longitud, con los estribos
    /// repartidos como dice la tabla. Lo que se ve en el corte es una sección; puesta de pie
    /// y con sus estribos, se ve el elemento.
    /// </para>
    /// <para>
    /// Las posiciones de los estribos salen de <c>Estribos.CentrosDeAlzado</c> y las
    /// varillas de <see cref="TodasLasVarillas"/>: las MISMAS funciones que el corte y que
    /// el dibujo de AutoCAD, así que las tres vistas no pueden discrepar.
    /// </para>
    /// </remarks>
    private void DibujarSeccion3DPrevia(SeccionConcretoRow s, double ancho, double alto)
    {
        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            return;
        }

        // De pie: el ancho de la sección en X, el fondo en Z y la LONGITUD en Y, que es la
        // que sube. Si la fila no trae largo se usa el de respaldo para poder dibujar.
        var largoM = s.LongitudM > 0 ? s.LongitudM : LargoPorOmisionM;

        var bx0 = s.BaseCm;
        var dz = s.AlturaCm;
        var hy = largoM * 100.0;

        const double c30 = 0.86602540378443864;
        const double s30 = 0.5;

        var anchoIso = (bx0 + dz) * c30;
        var altoIso = hy + ((bx0 + dz) * s30);

        var anchoDisp = (ancho * 0.46) - 28;
        var altoDisp = alto - 76;

        if (anchoDisp < 30 || altoDisp < 30 || anchoIso <= 0 || altoIso <= 0)
        {
            return;
        }

        var k = Math.Min(anchoDisp / anchoIso, altoDisp / altoIso);

        if (k <= 0 || double.IsInfinity(k))
        {
            return;
        }

        var ox = 30 + (dz * c30 * k);
        var oy = 46 + (hy * k);

        Point P(double x, double y, double z) => new(
            ox + ((x - z) * c30 * k),
            oy - (y * k) + ((x + z) * s30 * k));

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));

        void L3(Point p, Point q, Brush brocha, double grosor, double opacidad = 1.0)
        {
            PreviewCanvas.Children.Add(new Line
            {
                X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
                Stroke = brocha,
                StrokeThickness = grosor,
                Opacity = opacidad
            });
        }

        // La caja del elemento, en alambre y tenue: lo que hay que mirar es el armado.
        var v = new[]
        {
            P(0, 0, 0), P(bx0, 0, 0), P(bx0, hy, 0), P(0, hy, 0),
            P(0, 0, dz), P(bx0, 0, dz), P(bx0, hy, dz), P(0, hy, dz)
        };

        foreach (var (i, j) in new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        })
        {
            L3(v[i], v[j], azul, 1.0, 0.4);
        }

        // Los estribos, a la separación de la tabla. En un elemento de pie el reparto es el
        // de columna, que es justo lo que dice esVertical.
        // Separaciones(...) es el mismo lector de la columna «Sep cm» que usa el dibujo de
        // AutoCAD, así que el reparto de aquí sale de lo que dice la tabla.
        var sep = Separaciones(s.SeparacionCm);

        var centros = Estribos.CentrosDeAlzado(
            largoM,
            sep[0] / 100, sep[1] / 100, sep[2] / 100,
            vertical: true,
            esColumna: true);

        var rec = s.RecubrimientoCm;

        // El diametro del estribo, que aqui es GROSOR y no solo una cota: el 3D es para ver
        // los espesores reales, asi que un #3 y un #4 tienen que verse distintos.
        Varilla.TryDiametroCm(s.Estribo, out var de);

        // ==============================================================================
        //  TODO EL ARMADO SE APUNTA PRIMERO Y SE PINTA DESPUES, DE ATRAS HACIA DELANTE
        // ==============================================================================
        //
        // Sin esto el orden de encima/debajo lo decidia el orden del codigo: todas las
        // varillas tapaban a todos los estribos, tambien las que estan detras, y en el
        // cruce de un estribo con el diamante no se sabia cual pasa por delante. Con las
        // piezas ordenadas por profundidad, lo que esta mas cerca del ojo se pinta al
        // final y tapa a lo de atras, que es lo unico que hace que un cruce se lea.
        //
        // Es el algoritmo del pintor. Con barras redondas y cruces sueltos es exacto de
        // sobra; lo mismo hace el visor de ETABS con las barras extruidas.
        var piezas = new List<(double Prof, Action Pintar)>();

        void Barra(
            double x1, double v1, double z1,
            double x2, double v2, double z2,
            Color color, double diamCm)
        {
            var p = P(x1, v1, z1);
            var q = P(x2, v2, z2);

            // EL GRUESO ES EL DIAMETRO a la escala del dibujo, sin mas.
            //
            // El tope de 0.7 px es solo para que una barra no se vuelva invisible, y es
            // el MISMO para todas a proposito: antes cada familia tenia el suyo -1.6 la
            // varilla, 1.4 el estribo, 1.0 la grapa- y a escalas normales los tres topes
            // mandaban sobre el diametro, asi que todo salia casi del mismo ancho y las
            // proporciones entre calibres desaparecian. Con un solo tope, un #8 y un #3
            // se ven distintos porque lo son.
            var grueso = Math.Max(diamCm * k, 0.7);

            // La profundidad en isometrico es x + z: cuanto mas grande, mas cerca del ojo.
            // Se toma el centro de la barra, que es lo que decide el orden de pintado.
            piezas.Add((
                (x1 + z1 + x2 + z2) / 2,
                () => BarraRedonda3D(PreviewCanvas, p, q, color, grueso)));
        }

        var colorEstribo = Color.FromRgb(0x1F, 0x6F, 0xB2);
        var colorVarilla = Color.FromRgb(0xC0, 0x39, 0x2B);

        var varillas = TodasLasVarillas(s, de, rec);

        // ---------- Las varillas, de abajo arriba ----------
        // La X del corte es la X, y su Y es el fondo.
        foreach (var (_, vx, vz, vr) in varillas)
        {
            Barra(vx, 0, vz, vx, hy, vz, colorVarilla, vr * 2);
        }

        // ---------- Estribo, diamante y grapas, en cada posicion ----------
        //
        // VAN APILADOS A LO LARGO DE LA PIEZA, no los tres en el mismo plano.
        //
        // Antes se dibujaban los tres exactamente a la misma altura, y eso no es solo
        // un detalle de dibujo: dos barras del mismo calibre en el mismo plano se
        // ATRAVIESAN, que en la pieza no puede pasar. En el armado real se amarran una
        // pegada a la siguiente, y por eso una va encima de la otra. Aqui se apilan en
        // ese mismo orden -estribo, luego diamante, luego grapa-, cada una tangente a la
        // anterior, que es el mismo orden de encima/debajo que lleva el corte.
        var dDia = DiametroDelDiamante(s, de);

        var hayDiamante = s.LlevaDiamante && dDia > 0;

        // El recorrido se calcula UNA vez y se reutiliza en cada estribo: es el mismo en
        // todos, y armarlo pasa por TrazoDiamante.Centros, Cinta y Muestrear. Calcularlo
        // dentro del bucle era repetir ese trabajo treinta veces por redibujado.
        var recorrido = hayDiamante ? RecorridoDelDiamante3D(s, de, rec, dDia) : null;

        foreach (var c in centros)
        {
            var yEst = c * 100.0;

            // El estribo, cerrado en el plano de la seccion.
            if (rec > 0 && rec * 2 < bx0 && rec * 2 < dz)
            {
                var e = new[]
                {
                    (X: rec, Z: rec), (X: bx0 - rec, Z: rec),
                    (X: bx0 - rec, Z: dz - rec), (X: rec, Z: dz - rec)
                };

                for (var i = 0; i < 4; i++)
                {
                    var a = e[i];
                    var b = e[(i + 1) % 4];

                    Barra(a.X, yEst, a.Z, b.X, yEst, b.Z, colorEstribo, de);
                }
            }

            // El diamante, pegado al estribo: sus dos ejes separados media suma de los
            // dos diametros es justo tangencia.
            var yDia = yEst + ((de + dDia) / 2);

            if (recorrido is not null)
            {
                for (var i = 0; i < recorrido.Count; i++)
                {
                    var a = recorrido[i];
                    var b = recorrido[(i + 1) % recorrido.Count];

                    Barra(a.X, yDia, a.Y, b.X, yDia, b.Y, colorEstribo, dDia);
                }
            }

            // Y las grapas encima de todo, cada una con SU diametro y apilada sobre la
            // anterior. Antes las dibujaba todas con el diametro del estribo principal,
            // asi que una grapa del #4 se veia igual que una del #3.
            var yGrapa = hayDiamante ? yDia + (dDia / 2) : yEst + (de / 2);

            foreach (var g in s.Grapas)
            {
                if (!Varilla.TryDiametroCm(g.Diametro, out var dGrapa) || dGrapa <= 0)
                {
                    // Sin diametro reconocido se usa el del estribo, que es la misma
                    // regla que sigue el dibujo del corte.
                    dGrapa = de;
                }

                var va = BuscarVarillaPrevia(varillas, g.A);
                var vb = BuscarVarillaPrevia(varillas, g.B);

                if (va is null || vb is null)
                {
                    continue;
                }

                yGrapa += dGrapa / 2;

                Barra(va.Value.X, yGrapa, va.Value.Y,
                      vb.Value.X, yGrapa, vb.Value.Y, colorEstribo, dGrapa);

                yGrapa += dGrapa / 2;
            }
        }

        // ---------- Y AHORA SI, DE ATRAS HACIA DELANTE ----------
        foreach (var (_, pintar) in piezas.OrderBy(p => p.Prof))
        {
            pintar();
        }
    }
}


public partial class MainWindow
{

    /// <summary>
    /// Una <b>barra redonda</b> en el 3D: cilíndrica, no una raya plana.
    /// </summary>
    /// <remarks>
    /// El volumen se consigue con dos cosas: las puntas <b>redondeadas</b>, que cierran el
    /// cilindro en lugar de cortarlo a escuadra, y un <b>degradado</b> a lo ancho —claro en
    /// el borde de la luz, oscuro en el otro— que es como se lee un tubo. Es la misma idea
    /// que usa el visor de ETABS para las barras extruidas: en un lienzo no hay iluminación,
    /// así que el relieve se pinta.
    /// <para>
    /// El degradado va PERPENDICULAR a la barra, así que se calcula con su dirección: un
    /// degradado fijo se vería girado en las barras que no van en el mismo sentido.
    /// </para>
    /// </remarks>
    /// <summary>Brochas de barra ya hechas, por color y dirección.</summary>
    /// <remarks>
    /// <para>
    /// Hace falta porque el diamante muestreado da del orden de cincuenta barras por
    /// estribo, y una columna de tres metros lleva treinta estribos: sin caché serían miles
    /// de degradados nuevos <b>en cada redibujado</b>, y el dibujo se rehace con cada tecla
    /// que se toca en la tabla.
    /// </para>
    /// <para>
    /// La clave es el color y la <b>dirección</b>, y no hace falta más: el degradado se
    /// define en coordenadas relativas al recuadro de la barra, así que dos barras
    /// paralelas del mismo color lo tienen idéntico por largas o cortas que sean. La
    /// dirección se redondea a un grado, que a ojo no se distingue y hace que las barras
    /// del mismo estribo compartan brocha.
    /// </para>
    /// </remarks>
    private readonly Dictionary<(uint Color, int Grados), Brush> _brochasDeBarra = new();

    /// <summary>La brocha de una barra: clara en el borde de la luz y oscura en el otro.</summary>
    private Brush BrochaDeBarra(Color color, double angulo)
    {
        var grados = (int)Math.Round(angulo * 180 / Math.PI);

        var clave = ((uint)((color.R << 16) | (color.G << 8) | color.B), grados);

        if (_brochasDeBarra.TryGetValue(clave, out var ya))
        {
            return ya;
        }

        // La normal en coordenadas de la propia barra: el degradado cruza su ancho. Se
        // recalcula del ángulo redondeado, no del original, para que la brocha guardada
        // corresponda de verdad a la clave con la que se guarda.
        var rad = grados * Math.PI / 180;
        var nx = -Math.Sin(rad);
        var ny = Math.Cos(rad);

        Color Mezcla(Color c, double f) => Color.FromRgb(
            (byte)Math.Clamp(c.R * f, 0, 255),
            (byte)Math.Clamp(c.G * f, 0, 255),
            (byte)Math.Clamp(c.B * f, 0, 255));

        var brocha = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0.5 - (nx / 2), 0.5 - (ny / 2)),
            EndPoint = new Point(0.5 + (nx / 2), 0.5 + (ny / 2)),
            GradientStops =
            {
                new GradientStop(Mezcla(color, 0.55), 0.0),
                new GradientStop(Mezcla(color, 1.25), 0.35),
                new GradientStop(color, 0.62),
                new GradientStop(Mezcla(color, 0.5), 1.0)
            }
        };

        // Congelada: una brocha inmutable WPF la puede compartir entre miles de figuras
        // sin volver a resolverla en cada pintado. Es la mitad del motivo de la caché.
        brocha.Freeze();

        _brochasDeBarra[clave] = brocha;

        return brocha;
    }

    private void BarraRedonda3D(Canvas lienzo, Point p, Point q, Color color, double grueso)
    {
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 0.5 || grueso <= 0)
        {
            return;
        }

        var brocha = BrochaDeBarra(color, Math.Atan2(dy, dx));

        lienzo.Children.Add(new Line
        {
            X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y,
            Stroke = brocha,
            StrokeThickness = grueso,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    /// <summary>
    /// Busca una varilla por su señal en la tabla de la vista previa.
    /// </summary>
    /// <remarks>
    /// Igual que <c>BuscarVarilla</c>, pero devolviendo solo X e Y, que es lo que hace
    /// falta para colocar una grapa en el 3D. Devuelve <c>null</c> si la señal ya no
    /// apunta a nada, y entonces esa grapa se salta: es lo mismo que hace el dibujo del
    /// corte cuando el lecho se quedó con menos varillas.
    /// </remarks>
    private static (double X, double Y)? BuscarVarillaPrevia(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas, RefVarilla señal)
    {
        foreach (var v in varillas)
        {
            if (v.Ref.Equals(señal))
            {
                return (v.X, v.Y);
            }
        }

        return null;
    }

    /// <summary>El diámetro del estribo <b>diamante</b>, en centímetros.</summary>
    /// <remarks>
    /// Sin diámetro propio capturado se usa el del estribo principal, que es exactamente
    /// la regla que sigue el dibujante de AutoCAD en <c>EstriboDiamante</c>. Está en su
    /// propia función porque hace falta en dos sitios —para el grueso de la barra y para el
    /// recorrido— y las dos cuentas tienen que dar el mismo número.
    /// </remarks>
    private static double DiametroDelDiamante(SeccionConcretoRow s, double de) =>
        Varilla.TryDiametroCm(
            string.IsNullOrWhiteSpace(s.DiamEstriboDiamante)
                ? s.Estribo
                : s.DiamEstriboDiamante,
            out var dDia) && dDia > 0
            ? dDia
            : de;

    /// <summary>
    /// El <b>recorrido</b> del estribo diamante en el plano de la sección, muestreado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sale de <see cref="TrazoDiamante"/>, la misma clase que usa el corte y el dibujante
    /// de AutoCAD, y se muestrea en tramos rectos porque el lienzo no tiene arcos. Si se
    /// calculara aquí, el diamante del 3D podría abrazar otras varillas que el de la
    /// sección.
    /// </para>
    /// <para>
    /// <b>Solo devuelve geometría; no pinta.</b> Antes esta función dibujaba directamente,
    /// y por eso el diamante quedaba fuera del orden por profundidad y se pintaba con el
    /// grueso que le pasara el que llamaba —que era el del estribo principal, no el
    /// suyo—. Devolviendo el recorrido, el que llama decide el grueso, la altura y cuándo
    /// se pinta.
    /// </para>
    /// </remarks>
    /// <returns>El recorrido cerrado, o <c>null</c> si no se pudo armar.</returns>
    private List<(double X, double Y)>? RecorridoDelDiamante3D(
        SeccionConcretoRow s, double de, double rec, double dDia)
    {
        if (dDia <= 0)
        {
            return null;
        }

        var x1 = rec;
        var y1 = rec;
        var x2 = s.BaseCm - rec;
        var y2 = s.AlturaCm - rec;

        if (x2 <= x1 || y2 <= y1)
        {
            return null;
        }

        var varSup = PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                       arriba: true, intermedio: false);

        var varInf = PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                       arriba: false, intermedio: false);

        var varLat = PosicionesLaterales(s, de, rec);

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dDia, varSup, varInf, varLat);

        if (centros is null)
        {
            return null;
        }

        var geo = TrazoDiamante.Cinta(centros, 0);

        if (geo is null)
        {
            return null;
        }

        var puntos = TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, 8);

        return puntos.Count < 3 ? null : puntos;
    }
}
