using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using CadLink.App.Models;
using CadLink.Cad;

// Mismo choque que en la pestaña de acero: System.Windows.Shapes define un Path y el proyecto
// trae System.IO como using GLOBAL. Los alias dicen cuál es cuál.
using Path = System.IO.Path;
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

/// <summary>
/// La pestaña de <b>zapatas aisladas</b>: sus listas, su enlace y su vista previa.
/// </summary>
/// <remarks>
/// <para>
/// Va en un archivo parcial aparte por lo mismo que la de acero: es un módulo entero, con sus
/// dos familias —central y de lindero—, su elevación y su planta.
/// </para>
/// <para>
/// <b>Toda la geometría sale de <see cref="TrazoZapata"/></b>, que es la clase que va a usar
/// también el dibujante de AutoCAD. Es la misma decisión que con <c>TrazoAcero</c> y
/// <c>TrazoDiamante</c>, y aquí importa el doble: lo que hay que revisar antes de mandar el
/// dibujo no es solo la zapata, es <b>a qué distancia</b> queda cada cosa —la planta colgada de
/// la vista de corte, las secciones creciendo a la derecha o a la izquierda—, y esas distancias
/// son justo las que una copia del cálculo dejaría de respetar.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Llena las listas desplegables de la hoja de zapatas.</summary>
    private void LlenarListasZapatas()
    {
        var diametros = Varilla.DiametrosCm.Keys.ToList();

        var opcionales = new List<string> { string.Empty };
        opcionales.AddRange(diametros);

        ColTipoZapata.ItemsSource = ZapataAisladaRow.Tipos;
        ColZapTipoColumna.ItemsSource = ZapataAisladaRow.TiposColumna;

        ColZapVarInf.ItemsSource = diametros;
        ColZapVarInfT.ItemsSource = diametros;
        ColZapVarDadoSup.ItemsSource = diametros;
        ColZapVarDadoInf.ItemsSource = diametros;
        ColZapEstribo.ItemsSource = diametros;

        // Las de la parrilla superior y la intermedia del dado son opcionales: con una sola
        // parrilla o sin intermedias se dejan en blanco.
        ColZapVarSup.ItemsSource = opcionales;
        ColZapVarSupT.ItemsSource = opcionales;
        ColZapVarIntDado.ItemsSource = opcionales;

        ColZapSepEstribo.ItemsSource = SeccionConcretoRow.SeparacionesUsuales;
    }

    /// <summary>Enlaza la cuadrícula de zapatas y engancha su vista previa.</summary>
    private void EnlazarZapatas()
    {
        ZapatasGrid.ItemsSource = _datos.ZapatasAisladas;

        _datos.ZapatasAisladas.CollectionChanged += (_, e) =>
        {
            // Cada fila avisa de sus propias ediciones, igual que en concreto y en acero: sin
            // esto, la vista previa solo se movería al agregar o quitar filas, no al escribir.
            if (e.NewItems is not null)
            {
                foreach (ZapataAisladaRow fila in e.NewItems)
                {
                    fila.PropertyChanged += OnFilaZapataEditada;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (ZapataAisladaRow fila in e.OldItems)
                {
                    fila.PropertyChanged -= OnFilaZapataEditada;
                }
            }

            ActualizarTotalesZapatas();
            DibujarVistaPreviaZapata();
        };

        foreach (var fila in _datos.ZapatasAisladas)
        {
            fila.PropertyChanged += OnFilaZapataEditada;
        }

        ActualizarTotalesZapatas();
    }

    /// <summary>Engancha la vista previa: se redibuja al cambiar de fila y de tamaño.</summary>
    private void EngancharVistaPreviaZapata()
    {
        ZapataPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaZapata();
        ZapatasGrid.SelectionChanged += (_, _) => DibujarVistaPreviaZapata();
    }

    private void OnFilaZapataEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ActualizarTotalesZapatas();

        // Solo se redibuja si la fila editada es la que se está viendo. Sin esta condición,
        // editar una fila de arriba cambiaba el dibujo de la de abajo.
        if (sender is null || ReferenceEquals(sender, ZapatasGrid.SelectedItem))
        {
            DibujarVistaPreviaZapata();
        }
    }

    private void ActualizarTotalesZapatas()
    {
        var n = _datos.ZapatasAisladas.Count;
        var centrales = _datos.ZapatasAisladas.Count(z => !z.EsLindero);
        var linderos = n - centrales;
        var incompletas = _datos.ZapatasAisladas.Count(z => z.Falta.Length > 0);

        var texto = $"{n} zapata(s)   ·   {centrales} central(es)   ·   {linderos} de lindero";

        if (incompletas > 0)
        {
            texto += $"   ·   {incompletas} con datos incompletos (ver la columna «Falta»)";
        }

        TotalesZapatasText.Text = texto;
    }

    // ======================================================================
    // Vista previa: elevación y planta
    // ======================================================================

    /// <summary>
    /// Dibuja la zapata seleccionada: <b>elevación y planta</b>, a la misma escala.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos vistas van juntas y con <b>la misma escala</b> porque es como salen en el plano, y
    /// porque la distancia entre ellas es parte de lo que hay que revisar: la planta cuelga de la
    /// vista de corte —tres metros por debajo del rótulo en la central, y en −15 o más abajo en el
    /// lindero—, y esa regla es distinta en cada macro.
    /// </para>
    /// <para>
    /// Lo que se dibuja de la elevación: la plantilla de concreto simple, la zapata, el dado, la
    /// columna cuando es de concreto, el nivel del terreno, las dos parrillas con sus ganchos y
    /// sus varillas transversales vistas de punta, y los estribos del dado en las posiciones que
    /// reparte <see cref="TrazoZapata.CentrosEstribos"/>. De la planta: el paño de la zapata, el
    /// hueco del dado y las dos mallas.
    /// </para>
    /// <para>
    /// <b>Lo que todavía no está</b> —y conviene que se vea escrito— son las cotas, los rótulos
    /// con leader y los rellenos de la macro. La vista previa enseña la <i>geometría</i>, que es
    /// lo que se revisa antes de dibujar; los rótulos se revisan en el plano.
    /// </para>
    /// </remarks>
    private void DibujarVistaPreviaZapata()
    {
        ZapataPreviewCanvas.Children.Clear();

        var ancho = ZapataPreviewCanvas.ActualWidth;
        var alto = ZapataPreviewCanvas.ActualHeight;

        if (ancho < 80 || alto < 80)
        {
            return;
        }

        if (ZapatasGrid.SelectedItem is not ZapataAisladaRow fila)
        {
            AvisoZapata("Selecciona una zapata de la tabla para verla dibujada.");
            return;
        }

        var falta = fila.Falta;

        if (falta.Length > 0)
        {
            AvisoZapata($"No se puede dibujar todavía: falta {falta}.");
            return;
        }

        var z = fila.AFormatoCad();

        // El acomodo REAL de esta fila, con los anchos de todas: es lo que decide en qué x cae.
        var anchos = _datos.ZapatasAisladas.Select(r => r.AnchoM).ToList();
        var indice = _datos.ZapatasAisladas.IndexOf(fila);

        var xBase = TrazoZapata.XBase(z.Tipo, anchos, indice < 0 ? 0 : indice);
        var a = TrazoZapata.Colocar(z, xBase);

        // ---------- Escala: tienen que caber la elevación Y la planta ----------
        var yMin = a.YPlanta;
        var yMax = Math.Max(a.YTerreno, a.YDadoTop + (z.ColumnaDeConcreto ? 0.8 : 0));

        var xMin = Math.Min(a.XBase, a.XBase);
        var xMax = a.XDer;

        const double margen = 26;

        var escala = Math.Min(
            (ancho - (2 * margen)) / Math.Max(xMax - xMin, 0.01),
            (alto - (2 * margen)) / Math.Max(yMax - yMin, 0.01));

        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        var dx = margen - (xMin * escala);
        var dy = alto - margen + (yMin * escala);

        double PX(double x) => dx + (x * escala);
        double PY(double y) => dy - (y * escala);

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));
        var tierra = new SolidColorBrush(Color.FromRgb(0xA9, 0x8A, 0x6A));

        // ---------- Terreno ----------
        Recta(PX(a.XBase) - 12, PY(a.YTerreno), PX(a.XDer) + 12, PY(a.YTerreno), tierra, 1.2);

        // ---------- Plantilla de concreto simple ----------
        Contorno(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), gris, 1.0);

        // ---------- Zapata ----------
        Contorno(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), azul, 1.6);

        // ---------- Dado ----------
        Contorno(PX(a.XDadoIzq), PY(a.YDadoTop), PX(a.XDadoDer), PY(a.YZapTop), azul, 1.4);

        // ---------- Columna, solo si es de concreto ----------
        if (z.ColumnaDeConcreto)
        {
            // La macro dibuja 0.8 m de columna y le corta los 8/9, para que se lea que sigue.
            var yTope = a.YDadoTop + (0.8 * (8.0 / 9.0));

            Contorno(PX(a.XColIzq), PY(yTope), PX(a.XColDer), PY(a.YDadoTop), azul, 1.4);
        }

        // ---------- Estribos del dado ----------
        DibujarEstribosDadoPrevio(z, a, PX, PY, gris);

        // ---------- Parrillas ----------
        DibujarParrillaPrevia(z, a, PX, PY, superior: false);

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup))
        {
            DibujarParrillaPrevia(z, a, PX, PY, superior: true);
        }

        // ---------- Planta ----------
        DibujarPlantaPrevia(z, a, PX, PY, azul, gris);

        // ---------- Etiquetas ----------
        var titulo = z.Tipo == ZapataCad.Lindero
            ? $"ZAPATA AISLADA DE LINDERO \"{fila.Id}\""
            : $"ZAPATA AISLADA CENTRAL \"{fila.Id}\"";

        EtiquetaZapata($"{titulo}    ·    {fila.Resumen}", 12, 26, 12, azul, true);

        EtiquetaZapata("ELEVACIÓN", PX(a.XBase), PY(a.YZapBot) + 6, 10.5, gris);
        EtiquetaZapata("PLANTA", PX(a.XBase), PY(a.YPlanta + z.LargoM) + 6, 10.5, gris);

        EtiquetaZapata(
            $"x = {a.XBase:N2} m    ·    planta en y = {a.YPlanta:N2} m",
            12, alto - 20, 10.5, gris);
    }

    /// <summary>Los estribos del dado, en las posiciones que reparte la macro.</summary>
    /// <remarks>
    /// El dado se dibuja tendido y se rota 90°, así que los centros que devuelve
    /// <see cref="TrazoZapata.CentrosEstribos"/> se miden <b>a lo largo</b> del dado: aquí eso es
    /// la Y, contada desde el desplante. Y se saltan los primeros, que es donde está la parrilla
    /// de la zapata: dos con doble parrilla y uno con una sola.
    /// </remarks>
    private void DibujarEstribosDadoPrevio(
        ZapataCad z, TrazoZapata.Acomodo a,
        Func<double, double> px, Func<double, double> py, Brush trazo)
    {
        var partes = (z.SepEstriboDado ?? string.Empty)
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('-');

        double Sep(int i) =>
            i < partes.Length
            && double.TryParse(
                partes[i].Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : 0;

        var s1 = Sep(0);

        if (s1 <= 0)
        {
            s1 = 15;
        }

        var largo = z.ProfundidadM;

        var centros = TrazoZapata.CentrosEstribos(
            largo, s1, Sep(1), Sep(2),
            TrazoZapata.EstriboRetiroBorde, TrazoZapata.EstriboRetiroBorde);

        if (centros.Length == 0)
        {
            return;
        }

        TrazoZapata.Sobresalir(centros);

        centros = TrazoZapata.QuitarPrimeros(centros, z.DobleParrilla ? 2 : 1);

        var recDado = z.RecDadoCm * TrazoZapata.EscalaElevacion;

        var x1 = a.XDadoIzq + recDado;
        var x2 = a.XDadoDer - recDado;

        if (x2 <= x1)
        {
            return;
        }

        foreach (var c in centros)
        {
            var y = a.YZapBot + c;

            if (y < a.YZapBot || y > a.YDadoTop)
            {
                continue;
            }

            Recta(px(x1), py(y), px(x2), py(y), trazo, 1.0);
        }
    }

    /// <summary>Una parrilla en la elevación: su barra con ganchos y sus transversales.</summary>
    private void DibujarParrillaPrevia(
        ZapataCad z, TrazoZapata.Acomodo a,
        Func<double, double> px, Func<double, double> py, bool superior)
    {
        var varBarra = superior ? z.VarSup : z.VarInf;
        var varTrans = superior ? z.VarSupTrans : z.VarInfTrans;
        var sepTrans = superior ? z.SepSupTrans : z.SepInfTrans;

        if (!Varilla.TryDiametroCm(varBarra, out var dBarraCm) || dBarraCm <= 0)
        {
            return;
        }

        Varilla.TryDiametroCm(varTrans, out var dTransCm);

        var diam = dBarraCm / 100.0;
        var diamT = dTransCm / 100.0;

        var sep = LeerSeparacionM(sepTrans);

        var p = TrazoZapata.ParrillaEnAlzado(
            a.XBase, a.YZapBot, z.AnchoM, z.EspesorM, z.RecM, diam, diamT, sep, superior);

        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        // La barra que corre, con su gancho en cada extremo. El gancho dobla hacia DENTRO de la
        // zapata: hacia abajo en la parrilla superior y hacia arriba en la inferior, que es como
        // se arma y como lo dibuja la macro.
        var yTip = superior
            ? p.YBarra - TrazoZapata.GanchoParrilla
            : p.YBarra + TrazoZapata.GanchoParrilla;

        Recta(px(p.XCaraIzq), py(p.YBarra), px(p.XCaraDer), py(p.YBarra), rojo, 1.6);
        Recta(px(p.XCaraIzq), py(p.YBarra), px(p.XCaraIzq), py(yTip), rojo, 1.6);
        Recta(px(p.XCaraDer), py(p.YBarra), px(p.XCaraDer), py(yTip), rojo, 1.6);

        // Y las transversales, vistas de punta.
        var r = Math.Max(diamT * 100 / 2 * (px(1) - px(0)) / 100, 1.6);

        foreach (var x in p.Circulos)
        {
            var c = new Ellipse
            {
                Width = 2 * r,
                Height = 2 * r,
                Fill = rojo
            };

            System.Windows.Controls.Canvas.SetLeft(c, px(x) - r);
            System.Windows.Controls.Canvas.SetTop(c, py(p.YCirculos) - r);

            ZapataPreviewCanvas.Children.Add(c);
        }
    }

    /// <summary>La vista en planta: el paño, el hueco del dado y las dos mallas.</summary>
    private void DibujarPlantaPrevia(
        ZapataCad z, TrazoZapata.Acomodo a,
        Func<double, double> px, Func<double, double> py, Brush azul, Brush gris)
    {
        var yBot = a.YPlanta;
        var yTop = a.YPlanta + z.LargoM;

        Contorno(px(a.XBase), py(yTop), px(a.XDer), py(yBot), azul, 1.6);

        var (hx1, hy1, hx2, hy2) = TrazoZapata.HuecoDelDado(z, a.XBase, yBot);

        Contorno(px(hx1), py(hy2), px(hx2), py(hy1), azul, 1.2);

        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        var rosa = new SolidColorBrush(Color.FromRgb(0xE0, 0x8B, 0x7F));

        // Las dos mallas: la inferior en rojo y la superior en un rojo más claro, para que se
        // distingan. En el plano lo que las separa es la línea de rotura de la diagonal.
        DibujarMallaPrevia(z, a, px, py, yBot, yTop, z.VarInf, z.SepInf, z.VarInfTrans,
            z.SepInfTrans, rojo);

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup))
        {
            DibujarMallaPrevia(z, a, px, py, yBot, yTop, z.VarSup, z.SepSup, z.VarSupTrans,
                z.SepSupTrans, rosa);

            // La línea de rotura de la diagonal, que es lo que separa las dos parrillas.
            Recta(px(a.XBase), py(yBot), px(a.XDer), py(yTop), gris, 0.8);
        }
    }

    private void DibujarMallaPrevia(
        ZapataCad z, TrazoZapata.Acomodo a,
        Func<double, double> px, Func<double, double> py,
        double yBot, double yTop,
        string varX, string sepX, string varY, string sepY, Brush trazo)
    {
        if (!Varilla.TryDiametroCm(varX, out var dxCm))
        {
            return;
        }

        Varilla.TryDiametroCm(varY, out var dyCm);

        var rX = dxCm / 200.0;
        var rY = dyCm / 200.0;

        var xIni = a.XBase + z.RecM;
        var xFin = a.XDer - z.RecM;
        var yIni = yBot + z.RecM;
        var yFin = yTop - z.RecM;

        var sX = LeerSeparacionM(sepX);
        var sY = LeerSeparacionM(sepY);

        // Las que corren en X se reparten a lo largo de Y, y al contrario. Es lo que hace
        // DibujarMallaPlanta con PosicionesConSeparacion.
        foreach (var y in TrazoZapata.Posiciones(yIni + rX, yFin - rX, sX))
        {
            Recta(px(xIni), py(y), px(xFin), py(y), trazo, 0.9);
        }

        foreach (var x in TrazoZapata.Posiciones(xIni + rY, xFin - rY, sY))
        {
            Recta(px(x), py(yIni), px(x), py(yFin), trazo, 0.9);
        }
    }

    /// <summary>La separación de una celda de texto, en metros. Vacía o cero cae en 12 cm.</summary>
    private static double LeerSeparacionM(string? texto)
    {
        var t = (texto ?? string.Empty)
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(',', '.')
            .Trim();

        return double.TryParse(
            t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v / 100.0
            : 0.12;
    }

    private void Recta(double x1, double y1, double x2, double y2, Brush trazo, double grosor) =>
        ZapataPreviewCanvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = trazo,
            StrokeThickness = grosor
        });

    private void Contorno(
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

        ZapataPreviewCanvas.Children.Add(r);
    }

    private void AvisoZapata(string texto) =>
        EtiquetaZapata(texto, 14, 34, 12, Brushes.Gray);

    private void EtiquetaZapata(
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

        ZapataPreviewCanvas.Children.Add(t);
    }
}
