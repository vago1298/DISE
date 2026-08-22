using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
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

        // El TIPO y el DESPLANTA no se llenan aqui: su lista va en el XAML, en la
        // plantilla de la celda. Es el patron de la hoja de concreto, y es lo que evita
        // que el enlace pise el valor capturado cuando la lista llega tarde.

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

        // La separacion de estribos NO se llena aqui: su lista va en el XAML, en la
        // plantilla de la celda, porque esa casilla se puede ESCRIBIR A MANO. Con
        // SelectedItemBinding lo que se teclea no llega a la propiedad.
    }

    /// <summary>
    /// Rellena las listas de la hoja de zapatas: los <b>dados</b> y las <b>columnas</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se llama desde <c>DatosCambiaron</c>, o sea en <b>cada</b> cambio de la hoja de concreto:
    /// así la casilla de la zapata ofrece los dados que existen ahora, no los de hace un rato.
    /// Es lo mismo que hace el renglón de totales, y por el mismo motivo.
    /// </para>
    /// <para>
    /// <b>Se actualiza en su sitio, no se sustituye la colección.</b> La lista de la celda está
    /// declarada en el XAML y apunta a esa colección: cambiándola por otra, el desplegable se
    /// quedaría mirando la vieja y no volvería a enterarse de nada.
    /// </para>
    /// <para>
    /// Entran los dos dados, el cuadrado y el redondo: la forma la decide la sección, y la
    /// zapata lo único que necesita es su ID para insertarlo como bloque.
    /// </para>
    /// </remarks>
    private void ActualizarListasDeZapatas()
    {
        ActualizarDadosDisponibles();
        ActualizarColumnasDisponibles();

        // Y las MEDIDAS de lo elegido, no solo las listas: si la columna crece en su hoja, la
        // zapata que la usa se pone al día sola. Es lo que hace que la medida sea una referencia
        // y no una copia que envejece.
        ReferenciarMedidasDeTodas();
    }

    /// <summary>
    /// El doblez del gancho de arranque que se capturó, en <b>diámetros</b>.
    /// </summary>
    /// <remarks>
    /// Lo que se lee de la casilla, sin validar: de eso se encarga
    /// <see cref="TrazoZapata.FactorGanchoValido"/>, que es el mismo para el dibujo y para la vista
    /// previa. Si la casilla está vacía o trae algo que no es número, se devuelve 0 y esa función
    /// resuelve con los 15 de la macro.
    /// </remarks>
    private double FactorGanchoElegido =>
        double.TryParse(
            (ZapGanchoDiametrosBox?.Text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;

    /// <summary>
    /// La casilla del doblez cambió: se avisa de lo que significa y se redibuja la previa.
    /// </summary>
    /// <remarks>
    /// El aviso dice el largo <b>en centímetros</b> para una varilla del #4, que es lo que se
    /// entiende de un tirón: «40 diámetros = 51 cm en una #4». Y dice si el valor se ajustó, porque
    /// un 0 escrito por error saldría dibujado como 15 y sin avisar nadie lo notaría.
    /// </remarks>
    private void OnGanchoZapataCambio(object sender, TextChangedEventArgs e)
    {
        if (!_listo || ZapGanchoHintText is null)
        {
            return;
        }

        var pedido = FactorGanchoElegido;
        var usado = TrazoZapata.FactorGanchoValido(pedido);

        // El #4 como referencia: es la varilla con la que se arma casi todo dado.
        var enCm = usado * DiametroCmDeVarilla("#4");

        var texto = $"{usado:0.#} diámetros = {enCm:0.#} cm en una varilla del #4.";

        if (pedido <= 0)
        {
            texto += "  (la casilla está vacía: se usan los 15 de la macro)";
        }
        else if (Math.Abs(pedido - usado) > 1e-9)
        {
            texto += $"  (se pidió {pedido:0.#} y se ajustó al rango "
                     + $"{TrazoZapata.FactorGanchoMinimo:0.#}–{TrazoZapata.FactorGanchoMaximo:0.#})";
        }

        ZapGanchoHintText.Text = texto;

        DibujarVistaPreviaZapata();
    }

    private void ActualizarDadosDisponibles()
    {
        var dados = _datos.SeccionesConcreto
            .Where(s => EsDado(s.Elemento))
            .Select(s => (s.Id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Refrescar(ZapataAisladaRow.DadosDisponibles, dados);
    }

    /// <summary>
    /// Rellena la lista de <b>columnas</b> con las de las dos hojas: concreto y acero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada entrada lleva su hoja entre paréntesis —«C-1 (concreto)», «C-4 (acero)»— porque el ID
    /// no lo dice y hace falta para elegir: una columna de acero en la zapata cambia el dibujo
    /// del dado. Lo que se guarda en la celda es <b>solo el ID</b>, que es lo que va al plano y lo
    /// que la macro busca.
    /// </para>
    /// <para>
    /// <b>Los ID repetidos entre las dos hojas se marcan.</b> Si hay una columna de concreto y un
    /// perfil de acero con el mismo ID, el desplegable muestra las dos y con su hoja al lado: eso
    /// ya es un error de captura —dos columnas distintas con el mismo nombre en el plano— y
    /// esconderlo eligiendo una sola dejaría al usuario sin saber que existe.
    /// </para>
    /// </remarks>
    private void ActualizarColumnasDisponibles()
    {
        var columnas = new List<string>();

        foreach (var s in _datos.SeccionesConcreto)
        {
            var id = (s.Id ?? string.Empty).Trim();

            if (EsColumnaDeConcreto(s.Elemento) && id.Length > 0)
            {
                columnas.Add($"{id} (concreto)");
            }
        }

        foreach (var p in _datos.SeccionesAcero)
        {
            var elem = (p.Elemento ?? string.Empty).Trim();
            var id = (p.Id ?? string.Empty).Trim();

            if (PerfilAceroRow.ElementoColumna.Equals(elem, StringComparison.OrdinalIgnoreCase)
                && id.Length > 0)
            {
                columnas.Add($"{id} (acero)");
            }
        }

        Refrescar(ZapataAisladaRow.ColumnasDisponibles, columnas);
    }

    /// <summary>
    /// Pone la lista al día <b>en su sitio</b>, sin sustituir la colección.
    /// </summary>
    /// <remarks>
    /// Las dos cosas importan. <b>En su sitio</b> porque el desplegable de la celda apunta a esa
    /// colección desde el XAML: cambiándola por otra, se quedaría mirando la vieja. Y <b>solo si
    /// cambió</b> porque cada cambio de la colección hace que las celdas abiertas se vuelvan a
    /// armar, y eso se ve como un parpadeo mientras se escribe.
    /// </remarks>
    private static void Refrescar(ObservableCollection<string> lista, List<string> nuevos)
    {
        if (lista.Count == nuevos.Count
            && !lista.Where((v, i) => !string.Equals(v, nuevos[i], StringComparison.Ordinal)).Any())
        {
            return;
        }

        lista.Clear();

        foreach (var v in nuevos)
        {
            lista.Add(v);
        }
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
        ActualizarListasDeZapatas();
    }

    /// <summary>Engancha la vista previa: se redibuja al cambiar de fila y de tamaño.</summary>
    private void EngancharVistaPreviaZapata()
    {
        ZapataPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaZapata();
        ZapatasGrid.SelectionChanged += (_, _) => DibujarVistaPreviaZapata();
    }

    private void OnFilaZapataEditada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Al ELEGIR la columna o el dado, sus medidas se traen solas de su hoja: no hay que
        // teclear otra vez el ancho ni el recubrimiento de algo que ya está capturado.
        if (sender is ZapataAisladaRow fila
            && (e.PropertyName == nameof(ZapataAisladaRow.IdColumna)
                || e.PropertyName == nameof(ZapataAisladaRow.IdDado)))
        {
            ReferenciarMedidas(fila);
        }

        ActualizarTotalesZapatas();

        // Solo se redibuja si la fila editada es la que se está viendo. Sin esta condición,
        // editar una fila de arriba cambiaba el dibujo de la de abajo.
        if (sender is null || ReferenceEquals(sender, ZapatasGrid.SelectedItem))
        {
            DibujarVistaPreviaZapata();
        }
    }

    /// <summary>
    /// Trae de su hoja las <b>medidas reales</b> de la columna y del dado elegidos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo que se elige en la celda es un <b>ID</b>, y ese ID ya tiene su sección capturada con su
    /// ancho y su recubrimiento. Volver a teclearlos aquí era pedir dos veces el mismo dato, y de
    /// los dos sitios el segundo es el que se equivoca: una columna de 40 cm apuntada como de 30
    /// en la zapata sale con el dado descuadrado y no hay nada en la tabla que lo delate.
    /// </para>
    /// <para>
    /// <b>Es una referencia, no una copia que se queda vieja.</b> Se vuelve a traer al elegir el
    /// ID y también cada vez que cambia la hoja de secciones —desde
    /// <see cref="ActualizarListasDeZapatas"/>—, así que si la columna crece de 40 a 45 cm en su
    /// hoja, las zapatas que la usan se ponen al día solas. Eso es lo que se pidió: la medida real
    /// ya referenciada, sin tener que ir a moverla.
    /// </para>
    /// <para>
    /// Las celdas siguen siendo editables, y a propósito: se puede querer dibujar un caso a mano.
    /// Pero lo escrito a mano <b>no se guarda contra</b> la sección: la siguiente vez que esa
    /// sección cambie, la medida vuelve a ser la de verdad. Si hace falta otro ancho de manera
    /// permanente, lo que se captura es otra sección.
    /// </para>
    /// <para>
    /// Nunca se escribe un cero: si la sección no tiene la medida capturada, se deja lo que
    /// hubiera. Traer un cero borraría un dato bueno para poner uno que no existe.
    /// </para>
    /// </remarks>
    private void ReferenciarMedidas(ZapataAisladaRow fila)
    {
        ReferenciarColumna(fila);
        ReferenciarDado(fila);
    }

    /// <summary>Pone al día las medidas referenciadas de TODAS las filas.</summary>
    private void ReferenciarMedidasDeTodas()
    {
        foreach (var fila in _datos.ZapatasAisladas)
        {
            ReferenciarMedidas(fila);
        }
    }

    /// <summary>El ancho y el recubrimiento de la columna elegida, de la hoja donde esté.</summary>
    /// <remarks>
    /// Se busca primero en concreto y después en acero, y <b>el tipo de columna se pone solo</b>
    /// con lo que se encuentre: es el dato que decide si en el corte se dibuja columna encima del
    /// dado y hacia dónde doblan los ganchos de arranque, y tenerlo que marcar a mano después de
    /// haber elegido un perfil de acero es una forma de equivocarse gratis.
    /// </remarks>
    private void ReferenciarColumna(ZapataAisladaRow fila)
    {
        var idCol = ZapataAisladaRow.SoloElId(fila.IdColumna);

        if (idCol.Length == 0)
        {
            return;
        }

        var col = _datos.SeccionesConcreto.FirstOrDefault(s =>
            EsColumnaDeConcreto(s.Elemento)
            && ZapataAisladaRow.SoloElId(s.Id).Equals(idCol, StringComparison.OrdinalIgnoreCase));

        if (col is not null)
        {
            // El ancho es la BASE de la sección, y en la circular la base ES el diámetro
            // (SeccionConcretoRow.DiametroCm => BaseCm), así que sirve para las dos.
            if (col.BaseCm > 0)
            {
                fila.AnchoColumnaCm = col.BaseCm;
            }

            if (col.RecubrimientoCm > 0)
            {
                fila.RecColumnaCm = col.RecubrimientoCm;
            }

            fila.TipoColumna = ZapataAisladaRow.TipoColumnaConcreto;

            // La FORMA, igual que con el dado: la necesita la transición dado -> columna, porque
            // en la redonda las varillas van en la circunferencia y lo que se ve en el alzado es
            // su proyección.
            fila.ColumnaCircular = col.EsCircular;

            // Y su ARMADO, que es lo que el dibujante necesita para el arranque de la columna
            // encima del dado. En la macro esto se capturaba otra vez en la hoja de la zapata;
            // aquí sale de la sección, que es donde ya estaba.
            if (col.EsCircular)
            {
                // En la redonda no hay lechos: las dos caras del alzado llevan la misma varilla
                // del círculo. Y SÍ tiene intermedias: de las N repartidas en la circunferencia,
                // dos son las que se ven en las caras y las demás quedan en medio, la mitad por
                // cara. Ponerlas en cero dejaba la columna redonda sin varillas intermedias y,
                // con ellas, sin unión con el dado: la unión solo se dibuja si los dos elementos
                // las tienen.
                var d = col.DiamVarTotalEfectivo;

                fila.VarColSup = d;
                fila.VarColInf = d;
                fila.NIntColumna = col.NVarTotal > 2 ? (col.NVarTotal - 2) / 2 : 0;
                fila.VarIntColumna = d;
            }
            else
            {
                fila.VarColSup = col.DiamEsqSup;
                fila.VarColInf = col.DiamEsqInfEfectivo;
                fila.NIntColumna = IntermediasDeLaSeccion(col);
                fila.VarIntColumna = DiametroIntermediasDe(col);
            }

            // Los conteos del RÓTULO valen igual para la redonda y para la cuadrada, así que van
            // fuera del if: los reparte ConteosDelRotulo según la forma.
            ConteosDelRotulo(col, out var nSupCol, out var nInfCol, out var nIntCol);
            fila.NVarColSup = nSupCol;
            fila.NVarColInf = nInfCol;
            fila.NVarIntColumnaTotal = nIntCol;

            fila.EstriboColumna = col.Estribo;
            fila.SepEstriboColumna = col.SeparacionCm;
            return;
        }

        var perfil = _datos.SeccionesAcero.FirstOrDefault(p =>
            PerfilAceroRow.ElementoColumna.Equals(
                (p.Elemento ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            && ZapataAisladaRow.SoloElId(p.Id).Equals(idCol, StringComparison.OrdinalIgnoreCase));

        if (perfil is null)
        {
            // No está en ninguna hoja: no se toca nada. De eso ya avisa «Revisar zapatas», que
            // es donde se leen los problemas; aquí solo se copian medidas.
            return;
        }

        // De un perfil se toma el PERALTE, que es la medida que se ve en el corte de la zapata:
        // la columna se dibuja de canto, igual que la sección. Si no está capturado se usa el
        // ancho del patín, que es lo único que queda.
        var ancho = perfil.PeralteCm > 0 ? perfil.PeralteCm : perfil.AnchoCm;

        if (ancho > 0)
        {
            fila.AnchoColumnaCm = ancho;
        }

        fila.TipoColumna = ZapataAisladaRow.TipoColumnaAcero;
    }

    /// <summary>El ancho y el recubrimiento del dado elegido, de la hoja de concreto.</summary>
    private void ReferenciarDado(ZapataAisladaRow fila)
    {
        var idDado = ZapataAisladaRow.SoloElId(fila.IdDado);

        if (idDado.Length == 0)
        {
            return;
        }

        var dado = _datos.SeccionesConcreto.FirstOrDefault(s =>
            EsDado(s.Elemento)
            && ZapataAisladaRow.SoloElId(s.Id).Equals(idDado, StringComparison.OrdinalIgnoreCase));

        if (dado is null)
        {
            return;
        }

        // La FORMA del dado: en la planta decide si las mallas se cortan en un cuadrado o en la
        // circunferencia. Es dato de la sección, no de la zapata.
        fila.DadoCircular = dado.EsCircular;

        if (dado.BaseCm > 0)
        {
            fila.AnchoDadoCm = dado.BaseCm;
        }

        if (dado.RecubrimientoCm > 0)
        {
            fila.RecDadoCm = dado.RecubrimientoCm;
        }

        // ---- Y SU ARMADO ----
        // Los arranques son las varillas de las ESQUINAS del dado, las intermedias son sus
        // intermedias y los estribos son los suyos. Todo sale de su sección, sea redondo o
        // cuadrado. Volver a capturarlo aquí era pedir dos veces el mismo dato, y de los dos
        // sitios el segundo es el que se equivoca: un dado armado con el #5 y apuntado con el
        // #4 en la zapata sale con un arranque que no existe y nada en la tabla lo delata.
        if (dado.EsCircular)
        {
            // En la redonda no hay lechos: las dos caras del alzado llevan la misma varilla, y
            // las intermedias del alzado son las que quedan entre las dos esquinas.
            var d = dado.DiamVarTotalEfectivo;

            fila.VarDadoSup = d;
            fila.VarDadoInf = d;

            // De las N varillas repartidas en el círculo, dos son las que se ven en las caras
            // del alzado; las demás quedan en medio, y de ellas se ven la mitad por cara.
            fila.NIntDado = dado.NVarTotal > 2 ? (dado.NVarTotal - 2) / 2 : 0;
            fila.VarIntDado = d;
        }
        else
        {
            // El lecho SUPERIOR es la base de la que heredan los demas -no hay
            // «DiamEsqSupEfectivo» porque no hereda de nadie- y el inferior sí tiene su
            // efectivo, que cae en el superior cuando la celda va vacía.
            fila.VarDadoSup = dado.DiamEsqSup;
            fila.VarDadoInf = dado.DiamEsqInfEfectivo;
            fila.NIntDado = IntermediasDeLaSeccion(dado);
            fila.VarIntDado = DiametroIntermediasDe(dado);
        }

        // Y los conteos del RÓTULO, que no son los que se dibujan: en el alzado va una varilla
        // por paño, pero el rótulo dice cuántas hay de verdad («16 VAR #4», no «VAR #4 + 7 VAR
        // #4»). Son los conteos Z7 / Z8 / K7 de la macro, sacados de la sección.
        ConteosDelRotulo(dado, out var nSupDado, out var nInfDado, out var nIntDado);
        fila.NVarDadoSup = nSupDado;
        fila.NVarDadoInf = nInfDado;
        fila.NVarIntDadoTotal = nIntDado;

        if (!string.IsNullOrWhiteSpace(dado.Estribo))
        {
            fila.EstriboDado = dado.Estribo;
        }

        if (!string.IsNullOrWhiteSpace(dado.SeparacionCm))
        {
            fila.SepEstriboDado = dado.SeparacionCm;
        }
    }

    /// <summary>
    /// Cuántas varillas <b>intermedias</b> se ven en el alzado de una sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son las «Intermedias» de la hoja —<c>NInter</c>—, la columna pensada para las varillas
    /// laterales: las que en el alzado quedan entre las dos de las esquinas.
    /// </para>
    /// <para>
    /// <b>Y si esa celda va en cero, se miran los lechos.</b> Mucha gente captura las intermedias
    /// de una columna en «N int sup» y «N int inf» en lugar de en «Intermedias», y con
    /// <c>NInter</c> a secas el dado y la columna salían sin intermedias y —peor— <b>sin
    /// unión</b>: la unión de las varillas solo se dibuja cuando los dos elementos tienen
    /// intermedias, así que el detalle de los dobleces desaparecía sin que nada lo dijera. Se toma
    /// el mayor de los dos lechos, que es cuántas se ven de canto en el alzado.
    /// </para>
    /// </remarks>
    private static int IntermediasDeLaSeccion(SeccionConcretoRow s) =>
        s.NInter > 0 ? s.NInter : Math.Max(s.NIntSup, s.NIntInf);

    /// <summary>
    /// Los tres conteos que van en el <b>rótulo</b> de un dado o de una columna: paño superior,
    /// paño inferior e intermedias.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Son los conteos que la macro leía de la hoja de la zapata (<c>Z7</c>, <c>Z8</c>, <c>K7</c>)
    /// y aquí salen de la sección, que es donde ya están capturados. <b>No son los que se
    /// dibujan</b>: en el alzado se ve una varilla por paño, pero el rótulo tiene que decir
    /// cuántas hay en la sección completa.
    /// </para>
    /// <para>
    /// Los tres suman <see cref="SeccionConcretoRow.TotalVarillas"/>: en la cuadrada, el lecho de
    /// arriba entero, el de abajo entero y los laterales de los dos costados; en la circular, las
    /// dos que se ven en los paños y todas las demás como intermedias. Así el rótulo dice «16 VAR
    /// #4» en un dado redondo de 16 varillas y no un total inventado.
    /// </para>
    /// </remarks>
    private static void ConteosDelRotulo(
        SeccionConcretoRow s, out int nSup, out int nInf, out int nInt)
    {
        if (s.EsCircular)
        {
            nSup = s.NVarTotal > 0 ? 1 : 0;
            nInf = s.NVarTotal > 1 ? 1 : 0;
            nInt = s.NVarTotal > 2 ? s.NVarTotal - 2 : 0;
            return;
        }

        nSup = s.NEsqSup + s.NIntSup;
        nInf = s.NEsqInf + s.NIntInf;
        nInt = 2 * s.NInter;
    }

    /// <summary>El diámetro de esas intermedias, con la misma regla.</summary>
    private static string DiametroIntermediasDe(SeccionConcretoRow s)
    {
        if (s.NInter > 0 && !string.IsNullOrWhiteSpace(s.DiamInter))
        {
            return s.DiamInter;
        }

        if (s.NIntSup >= s.NIntInf && !string.IsNullOrWhiteSpace(s.DiamIntSupEfectivo))
        {
            return s.DiamIntSupEfectivo;
        }

        if (!string.IsNullOrWhiteSpace(s.DiamIntInfEfectivo))
        {
            return s.DiamIntInfEfectivo;
        }

        // Sin diámetro propio, la intermedia se arma con la varilla de la esquina.
        return s.DiamEsqSup;
    }

    /// <summary>
    /// ¿La sección es una <b>columna</b> de concreto? Cuadrada, rectangular o circular.
    /// </summary>
    /// <remarks>
    /// Se mira si el elemento <b>empieza</b> por «COLUMNA» y no si es igual a uno de los dos
    /// nombres exactos. Es lo que hace que en la lista de «ID col.» salgan <b>todas</b>: una
    /// columna capturada como «COLUMNA RECTANGULAR» o «COLUMNA CUADRADA» se quedaba fuera de la
    /// lista sin que nada lo dijera, y el usuario tenía que teclear el ID a mano —y entonces la
    /// revisión le decía que esa columna no estaba capturada, que era mentira—.
    /// </remarks>
    private static bool EsColumnaDeConcreto(string? elemento) =>
        (elemento ?? string.Empty).Trim()
        .StartsWith(SeccionConcretoRow.ElementoColumna, StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Es un <b>dado</b>? Cuadrado, rectangular o circular, por lo mismo.</summary>
    private static bool EsDado(string? elemento) =>
        (elemento ?? string.Empty).Trim()
        .StartsWith(SeccionConcretoRow.ElementoDado, StringComparison.OrdinalIgnoreCase);

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
    // Los dos botones de la hoja
    // ======================================================================

    /// <summary>
    /// Revisa la hoja de zapatas y dice, zapata por zapata, <b>dónde se va a dibujar</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dos cosas, y las dos hacen falta antes de generar un plano: lo que <b>falta</b> por
    /// capturar, y el <b>acomodo</b> —en qué x cae cada zapata y en qué y cae su planta—, que es
    /// justo lo que no se puede adivinar mirando la tabla porque depende del tipo y de los anchos
    /// de las que van antes.
    /// </para>
    /// <para>
    /// Los ID repetidos se avisan porque el ID es el <b>nombre del bloque</b> en AutoCAD: dos
    /// zapatas con el mismo ID se pelean por el mismo bloque.
    /// </para>
    /// </remarks>
    private void OnRevisarZapatas(object sender, RoutedEventArgs e)
    {
        if (!HayZapatas())
        {
            return;
        }

        RevisarZapatas(out var problemas, out var acomodo, out var columnasRepetidas);

        var texto = problemas.Count == 0
            ? "Las zapatas están completas."
            : $"Hay {problemas.Count} cosa(s) que corregir:\n\n"
              + string.Join("\n", problemas);

        if (acomodo.Count > 0)
        {
            texto += "\n\nDonde se va a dibujar cada una:\n" + string.Join("\n", acomodo);
        }

        // Se DICE, no se reprocha: una columna en varias zapatas es lo normal, porque el ID es
        // el tipo de columna. Se enseña para que una repeticion por descuido se vea, no para
        // que haya que arreglarla.
        if (columnasRepetidas.Count > 0)
        {
            texto += "\n\nColumnas que se repiten (es normal: el ID es el TIPO de columna, "
                     + "y el mismo tipo desplanta en varias zapatas):\n"
                     + string.Join("\n", columnasRepetidas);
        }

        texto += "\n\nSi está todo bien, «Dibujar zapatas en AutoCAD» las pone en el dibujo "
                 + "abierto, en estas mismas posiciones.";

        MessageBox.Show(
            texto, AppInfo.ProductName, MessageBoxButton.OK,
            problemas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>¿Hay algo que revisar o dibujar? Si no, lo dice y devuelve <c>false</c>.</summary>
    private bool HayZapatas()
    {
        if (_datos.ZapatasAisladas.Count > 0)
        {
            return true;
        }

        MessageBox.Show(
            "No hay ninguna zapata capturada.", AppInfo.ProductName,
            MessageBoxButton.OK, MessageBoxImage.Information);

        return false;
    }

    /// <summary>
    /// Revisa las zapatas capturadas. <c>true</c> si no hay nada que corregir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Está separada del botón porque la usan <b>los dos</b>: el de revisar, que enseña el
    /// resultado, y el de dibujar, que se niega a dibujar si hay algo mal. Con la revisión metida
    /// dentro del botón de revisar, el de dibujar tendría su propia copia —siempre más pobre— y
    /// acabaría mandando a AutoCAD zapatas que la otra pantalla ya decía que estaban mal.
    /// </para>
    /// <para>
    /// <paramref name="acomodo"/> sale aparte porque no son problemas: es dónde va a quedar cada
    /// zapata, que es lo que hay que poder leer antes de dibujar.
    /// </para>
    /// <para>
    /// <paramref name="columnasRepetidas"/> tampoco son problemas, y merece decirse por qué:
    /// <b>una misma columna sí puede estar en varias cimentaciones</b>. Lo que se captura en la
    /// hoja de secciones es el <b>tipo</b> de columna —«C-01» es la de 40×40 con su armado—, y
    /// ese tipo se repite en todas las zapatas donde toque. Esto se estaba reportando como error
    /// y, peor, <b>impedía dibujar</b>. Ahora solo se cuenta y se enseña.
    /// </para>
    /// </remarks>
    private bool RevisarZapatas(
        out List<string> problemas, out List<string> acomodo, out List<string> columnasRepetidas)
    {
        problemas = new List<string>();
        acomodo = new List<string>();
        columnasRepetidas = new List<string>();

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // La misma columna PUEDE estar en varias zapatas, asi que esto no es una lista de
        // culpables: es un recuento, para poder decirlo. Ver el comentario de abajo.
        var columnasUsadas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var anchos = _datos.ZapatasAisladas.Select(r => r.AnchoM).ToList();

        for (var i = 0; i < _datos.ZapatasAisladas.Count; i++)
        {
            var fila = _datos.ZapatasAisladas[i];
            var donde = $"Fila {i + 1}";

            var id = (fila.Id ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                problemas.Add($"{donde}: falta el ID. Es el nombre del bloque en AutoCAD.");
            }
            else if (!vistos.Add(id))
            {
                problemas.Add(
                    $"{donde}: el ID «{id}» está repetido. Cada zapata necesita el suyo, " +
                    "porque el ID es el nombre del bloque.");
            }

            var falta = fila.Falta;

            if (falta.Length > 0)
            {
                problemas.Add($"{donde} ({id}): falta {falta}.");
                continue;
            }

            // El dado se busca por su ID entre las secciones de concreto: si no está, la
            // macro no encuentra el bloque y la zapata sale sin dado.
            var idDado = (fila.IdDado ?? string.Empty).Trim();

            if (idDado.Length > 0 && !ZapataAisladaRow.DadosDisponibles.Contains(idDado))
            {
                problemas.Add(
                    $"{donde} ({id}): el dado «{idDado}» no está capturado en «Secciones " +
                    "Concreto», así que no habrá bloque que insertar. Captúralo ahí como " +
                    "DADO o DADO CIRCULAR, o elige uno de la lista.");
            }

            // La columna, igual que el dado: se elige de las dos hojas.
            //
            // Y REPETIRLA NO ES UN ERROR. Lo que se captura en «Secciones» no es una columna
            // del plano, es un TIPO de columna: «C-01» es la columna de 40x40 con su armado, y
            // esa misma columna se repite en todas las cimentaciones donde toque. En un plano
            // normal la mayoria de las zapatas comparten dos o tres tipos de columna; una
            // columna por zapata seria una hoja de secciones con cincuenta columnas iguales.
            //
            // Esto estaba mal: se reportaba como error que dos zapatas usaran la misma columna
            // y ademas IMPEDIA dibujar, porque el boton se niega cuando hay problemas. Se cambio
            // por un RECUENTO, que es lo unico que hacia falta: decir en cuantas zapatas esta
            // cada columna, para que una repeticion que NO fuera intencionada se vea.
            var idCol = ZapataAisladaRow.SoloElId(fila.IdColumna);

            if (idCol.Length > 0)
            {
                var estaCapturada = ZapataAisladaRow.ColumnasDisponibles
                    .Any(c => ZapataAisladaRow.SoloElId(c)
                        .Equals(idCol, StringComparison.OrdinalIgnoreCase));

                if (!estaCapturada)
                {
                    problemas.Add(
                        $"{donde} ({id}): la columna «{idCol}» no está capturada, ni en " +
                        "«Secciones Concreto» ni en «Secciones Acero». Captúrala ahí o elige " +
                        "una de la lista.");
                }

                if (!columnasUsadas.TryGetValue(idCol, out var enQueZapatas))
                {
                    enQueZapatas = new List<string>();
                    columnasUsadas[idCol] = enQueZapatas;
                }

                enQueZapatas.Add(id.Length > 0 ? id : donde);
            }

            var z = fila.AFormatoCad();
            var a = TrazoZapata.Colocar(z, TrazoZapata.XBase(z.Tipo, anchos, i));

            acomodo.Add(
                $"  {id}  ({(fila.EsLindero ? "lindero" : "central")}):  " +
                $"x de {a.XBase:N2} a {a.XDer:N2} m,  planta en y = {a.YPlanta:N2} m");
        }

        foreach (var par in columnasUsadas.Where(p => p.Value.Count > 1))
        {
            columnasRepetidas.Add(
                $"  {par.Key}  desplanta en {par.Value.Count} zapatas:  " +
                string.Join(", ", par.Value));
        }

        return problemas.Count == 0;
    }

    /// <summary>
    /// El botón <b>«Dibujar zapatas en AutoCAD»</b>: manda al dibujo las zapatas capturadas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hace lo mismo que el botón de las secciones y en el mismo orden, porque es el mismo trabajo
    /// y conviene que se comporte igual: <b>revisar</b> primero —y negarse si hay algo mal, en
    /// lugar de dibujar una zapata a medias que luego hay que borrar a mano—, <b>engancharse</b> a
    /// la sesión de AutoCAD que ya esté abierta, dibujar, encuadrar y <b>contar lo que salió</b>,
    /// incluidos los fallos tolerados.
    /// </para>
    /// <para>
    /// <b>No arranca AutoCAD</b> (<c>launchIfMissing: false</c>): abrirlo tarda un minuto y
    /// consume una licencia de red. Si no está abierto se dice, y lo abre quien decide.
    /// </para>
    /// <para>
    /// <b>El acomodo lo decide el dibujante</b>, que lo pide a <see cref="TrazoZapata.XBase"/>.
    /// Aquí no se calcula ninguna posición: si esta pantalla eligiera dónde van, sería un tercer
    /// sitio con una opinión sobre el acomodo, además de la vista previa y de la revisión.
    /// </para>
    /// <para>
    /// El catálogo de varillas se le <b>pasa</b> al dibujante: la tabla de diámetros vive en
    /// <see cref="Varilla"/>, en la ventana, y así el plano y la vista previa dibujan la misma
    /// varilla del #4 sin tener dos tablas que se puedan desincronizar.
    /// </para>
    /// </remarks>
    private void OnExportZapatas(object sender, RoutedEventArgs e)
    {
        if (!_license.HasFeature("export-dxf"))
        {
            MessageBox.Show("Tu licencia no incluye la generación de dibujos.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HayZapatas())
        {
            return;
        }

        if (!RevisarZapatas(out var problemas, out _, out _))
        {
            MessageBox.Show(
                "Corrige esto antes de dibujar las zapatas:\n\n" + string.Join("\n", problemas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            // El catalogo va en una VARIABLE con su tipo, no como nombre de metodo suelto.
            // 'doc' es dynamic, asi que esta construccion se resuelve en tiempo de ejecucion, y
            // a una llamada dinamica no se le puede pasar un grupo de metodos: el compilador la
            // rechaza con CS1976 porque no sabria cual de las sobrecargas convertir ni a que
            // delegado. Con la variable ya es un Func<string?, double> y no hay nada que adivinar.
            Func<string?, double> catalogoDeVarillas = DiametroCmDeVarilla;

            var dibujante = new ZapataDrawer(doc, catalogoDeVarillas)
            {
                // El tipo de sección es del JUEGO, no de cada zapata: sale de los mismos botones
                // de arriba que mandan en las secciones de concreto.
                SeccionRellena = ModoElegido == ModoSeccion.Tipo2Rellena,

                // El doblez del gancho de arranque, en diámetros, también del JUEGO: la casilla de
                // arriba. El dibujante lo valida por su cuenta, así que aquí se pasa tal cual se
                // capturó.
                FactorGanchoDiametros = FactorGanchoElegido
            };

            var zapatas = _datos.ZapatasAisladas.Select(f => f.AFormatoCad()).ToList();

            var r = dibujante.DibujarTodas(zapatas);

            AcadConnection.Retry(() => { app.ZoomExtents(); });

            var resumen =
                "Listo.\n\n" + r + "\n\n" +
                $"Dados insertados como bloque: {r.DadosInsertados}\n" +
                $"Dados dibujados como rectángulo por no estar su bloque: {r.DadosDeRespaldo}\n\n" +
                "Cada zapata quedó con su corte y su planta, en las posiciones de tus macros.";

            var fallos = dibujante.Fallos;

            if (fallos.Count == 0)
            {
                StatusText.Text = $"Dibujadas {r.Zapatas} zapata(s) en AutoCAD.";

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
                    $"Dibujadas {r.Zapatas} zapata(s), con {fallos.Count} aviso(s). " +
                    "Ver el detalle bajo la vista previa.";

                MostrarNotas(
                    "AVISOS DEL ULTIMO DIBUJO (" + fallos.Count + "):" +
                    Environment.NewLine + detalle);

                MessageBox.Show(
                    resumen + "\n\n" +
                    "PERO hubo " + fallos.Count + " fallo(s) que se toleraron, así que el " +
                    "dibujo puede estar incompleto:\n\n" + detalle +
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
                "Error al dibujar las zapatas en AutoCAD:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>El diámetro de una varilla en cm, o 0 si la celda está vacía o no se reconoce.</summary>
    /// <remarks>
    /// Es el puente entre el catálogo de la ventana y el dibujante, que está en la biblioteca de
    /// AutoCAD y no puede ver los modelos de la interfaz. Devuelve 0 en lugar de un diámetro
    /// inventado: quien dibuja ya sabe qué hacer con un 0 —no dibujar esa parrilla y avisar—, y
    /// eso es mejor que sacar un plano con una varilla que nadie pidió.
    /// </remarks>
    private static double DiametroCmDeVarilla(string? clave) =>
        Varilla.TryDiametroCm(clave, out var cm) ? cm : 0;

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
    /// <b>Lo que todavía no está</b> —y conviene que se vea escrito— son los rellenos de concreto
    /// y de terreno, y los rótulos con leader: eso <b>sí</b> lo dibuja
    /// <see cref="ZapataDrawer"/> en AutoCAD, pero aquí taparían el acero en un cuadro de pocos
    /// centímetros. La vista previa enseña la <i>geometría</i> y las <i>cotas</i>, que es lo que
    /// se revisa antes de dibujar; el aspecto se revisa en el plano.
    /// </para>
    /// </remarks>
    private void DibujarVistaPreviaZapata()
    {
        ZapataPreviewCanvas.Children.Clear();

        var ancho = ZapataPreviewCanvas.ActualWidth;
        var alto = ZapataPreviewCanvas.ActualHeight;

        if (ancho < 120 || alto < 120)
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

        // El acomodo REAL de esta fila, con los anchos de todas: es lo que decide en qué x cae
        // y de dónde cuelga su planta, que es parte de lo que hay que revisar.
        var anchos = _datos.ZapatasAisladas.Select(r => r.AnchoM).ToList();
        var indice = _datos.ZapatasAisladas.IndexOf(fila);

        var xBase = TrazoZapata.XBase(z.Tipo, anchos, indice < 0 ? 0 : indice);
        var a = TrazoZapata.Colocar(z, xBase);

        var azul = new SolidColorBrush(Color.FromRgb(0x0B, 0x3D, 0x6B));
        var gris = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA4));

        // ---------- LA MITAD PARA CADA VISTA ----------
        //
        // Antes las dos vistas iban en el MISMO sistema de coordenadas, y eso no se podía
        // usar: la planta cuelga a tres metros de la elevación en la central y a quince en el
        // lindero, así que al meter las dos en el cuadro salían dos dibujos diminutos con un
        // hueco enorme en medio. Ahora cada vista tiene su MITAD y su propia escala, con lo
        // que las dos salen del tamaño del cuadro. La distancia entre ellas se dice con
        // números, en el renglón de abajo, que es donde se puede leer.
        var gap = 18.0;
        var arriba = 44.0;

        // Abajo hay DOS renglones: el del acomodo y la leyenda de colores.
        var abajo = 52.0;

        var wMitad = (ancho - (3 * gap)) / 2;
        var hUtil = alto - arriba - abajo;

        if (wMitad < 60 || hUtil < 60)
        {
            AvisoZapata("Agranda la ventana para ver la zapata dibujada.");
            return;
        }

        DibujarElevacionPrevia(z, a, fila, gap, arriba, wMitad, hUtil, azul, gris);
        DibujarPlantaPrevia(z, a, gap + wMitad + gap, arriba, wMitad, hUtil, azul, gris);

        // ---------- Título y el dato del acomodo ----------
        var titulo = z.Tipo == ZapataCad.Lindero
            ? $"ZAPATA AISLADA DE LINDERO \"{fila.Id}\""
            : $"ZAPATA AISLADA CENTRAL \"{fila.Id}\"";

        EtiquetaZapata($"{titulo}    ·    {fila.Resumen}", 12, 24, 12, azul, true);

        var dado = string.IsNullOrWhiteSpace(fila.IdDado) ? "sin dado" : $"dado \"{fila.IdDado}\"";

        EtiquetaZapata(
            $"Se dibuja en x = {a.XBase:N2} m    ·    la planta cae en y = {a.YPlanta:N2} m"
            + $"    ·    {dado}",
            12, alto - 20, 10.5, gris);

        // ---------- La leyenda: qué es cada color ----------
        LeyendaZapata(
            12, alto - 36,
            (PincelConcreto, "concreto"),
            (PincelTerreno, "terreno"),
            (new SolidColorBrush(Color.FromRgb(0x0E, 0x6E, 0xA8)), "longitudinal"),
            (new SolidColorBrush(Color.FromRgb(0x12, 0x4A, 0x77)), "estribos"),
            (new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), "parrilla inf."),
            (new SolidColorBrush(Color.FromRgb(0xE0, 0x8B, 0x7F)), "parrilla sup."),
            (new SolidColorBrush(Color.FromRgb(0x00, 0xA6, 0xB8)), "transición 1:6"));
    }

    /// <summary>La <b>elevación</b>, en su mitad del cuadro y con sus cotas.</summary>
    /// <remarks>
    /// Se dibuja de abajo arriba, como se arma: la plantilla de concreto simple, la zapata, el
    /// dado, la columna cuando es de concreto, el nivel del terreno, los estribos del dado y las
    /// parrillas con sus ganchos y sus varillas transversales vistas de punta.
    /// </remarks>
    private void DibujarElevacionPrevia(
        ZapataCad z, TrazoZapata.Acomodo a, ZapataAisladaRow fila,
        double left, double top, double w, double h, Brush azul, Brush gris)
    {
        // Lo que tiene que caber: de la plantilla al terreno, o a la punta de la columna.
        var yMin = a.YPlantillaBot;
        var yMax = Math.Max(a.YTerreno, a.YDadoTop + (z.ColumnaDeConcreto ? 0.8 * (8.0 / 9.0) : 0));

        // Aire para las cotas: dos renglones abajo, dos a la izquierda y uno arriba.
        const double aireCota = 0.34;

        var anchoModelo = z.AnchoM + (2 * aireCota);
        var altoModelo = (yMax - yMin) + (2 * aireCota);

        var escala = Math.Min(w / anchoModelo, h / altoModelo);

        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        // Centrado en su mitad.
        var dx = left + ((w - (anchoModelo * escala)) / 2) + (aireCota * escala) - (a.XBase * escala);
        var dy = top + ((h - (altoModelo * escala)) / 2) + ((altoModelo - aireCota) * escala)
                 + (yMin * escala);

        double PX(double x) => dx + (x * escala);
        double PY(double y) => dy - (y * escala);

        var tierra = new SolidColorBrush(Color.FromRgb(0xA9, 0x8A, 0x6A));
        var acero = new SolidColorBrush(Color.FromRgb(0x0E, 0x6E, 0xA8));
        var estribo = new SolidColorBrush(Color.FromRgb(0x12, 0x4A, 0x77));

        var yColTope = a.YDadoTop + (0.8 * (8.0 / 9.0));

        // ---------- 1) LOS RELLENOS, primero, para que el acero se dibuje encima ----------
        // El terreno a los dos lados del dado, como en el plano: por encima del lomo de la zapata
        // y hasta el nivel del terreno. Debajo del lomo está la zapata, no tierra.
        if (a.XDadoIzq > a.XBase)
        {
            Relleno(PX(a.XBase), PY(a.YTerreno), PX(a.XDadoIzq), PY(a.YZapTop), PincelTerreno);
        }

        if (a.XDer > a.XDadoDer)
        {
            Relleno(PX(a.XDadoDer), PY(a.YTerreno), PX(a.XDer), PY(a.YZapTop), PincelTerreno);
        }

        Relleno(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), PincelPlantilla);
        Relleno(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), PincelConcreto);
        Relleno(PX(a.XDadoIzq), PY(a.YDadoTop), PX(a.XDadoDer), PY(a.YZapTop), PincelConcreto);

        if (z.ColumnaDeConcreto)
        {
            Relleno(PX(a.XColIzq), PY(yColTope), PX(a.XColDer), PY(a.YDadoTop), PincelConcreto);
        }

        // ---------- 2) EL NIVEL DEL TERRENO Y LOS CONTORNOS ----------
        Recta(PX(a.XBase) - 10, PY(a.YTerreno), PX(a.XDer) + 10, PY(a.YTerreno), tierra, 1.4);

        Contorno(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), gris, 1.0);
        Contorno(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), azul, 1.6);
        Contorno(PX(a.XDadoIzq), PY(a.YDadoTop), PX(a.XDadoDer), PY(a.YZapTop), azul, 1.4);

        if (z.ColumnaDeConcreto)
        {
            Contorno(PX(a.XColIzq), PY(yColTope), PX(a.XColDer), PY(a.YDadoTop), azul, 1.4);

            // La línea de rotura del tope de la columna: sigue hacia arriba.
            var xm = (a.XColIzq + a.XColDer) / 2;
            var amp = (a.XColDer - a.XColIzq) / 6;

            Recta(PX(a.XColIzq), PY(yColTope), PX(xm - amp), PY(yColTope + 0.04), azul, 1.2);
            Recta(PX(xm - amp), PY(yColTope + 0.04), PX(xm + amp), PY(yColTope - 0.02), azul, 1.2);
            Recta(PX(xm + amp), PY(yColTope - 0.02), PX(a.XColDer), PY(yColTope + 0.03), azul, 1.2);
        }

        // ---------- 3) EL ACERO ----------
        DibujarEstribosDadoPrevio(z, a, PX, PY, estribo);

        // Las longitudinales del dado y de la columna, con su pata de arranque: es el acero que
        // más se revisa y era justo el que no se veía.
        DibujarLongitudinalesPrevias(z, a, PX, PY, acero);

        DibujarParrillaPrevia(z, a, PX, PY, superior: false);

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup))
        {
            DibujarParrillaPrevia(z, a, PX, PY, superior: true);
        }

        // ---------- COTAS ----------
        // Las mismas que pone la macro, y en el mismo sitio: el ancho del dado y los tramos
        // de la zapata abajo, y las verticales a la izquierda, escalonadas para que no se
        // monten. La de la plantilla lleva su número EN MEDIO, que es lo que la macro
        // resuelve con DIMTIX en una cota de 5 cm.
        var yCad = PY(a.YZapBot) + (0.14 * escala);
        var yTot = PY(a.YZapBot) + (0.24 * escala);

        if (a.XDadoIzq > a.XBase + 0.001)
        {
            CotaH(PX(a.XBase), PX(a.XDadoIzq), yCad, a.XDadoIzq - a.XBase, gris);
        }

        CotaH(PX(a.XDadoIzq), PX(a.XDadoDer), yCad, a.XDadoDer - a.XDadoIzq, gris);

        if (a.XDer > a.XDadoDer + 0.001)
        {
            CotaH(PX(a.XDadoDer), PX(a.XDer), yCad, a.XDer - a.XDadoDer, gris);
        }

        CotaH(PX(a.XBase), PX(a.XDer), yTot, z.AnchoM, gris);

        // A LA IZQUIERDA DEL PAÑO IZQUIERDO, pegadas a la cimentación, con las mismas distancias
        // que usa el dibujante. La previa tiene que enseñar lo que va a salir en AutoCAD, así que
        // estas dos X salen de TrazoZapata y no de números escritos aquí.
        var x1 = PX(a.XBase) - (TrazoZapata.AnotacionCotaVert1 * escala);
        var x2 = PX(a.XBase) - (TrazoZapata.AnotacionCotaVert2 * escala);

        CotaV(x1, PY(a.YPlantillaBot), PY(a.YZapBot), TrazoZapata.PlantillaEspesor, gris);
        CotaV(x1, PY(a.YZapBot), PY(a.YZapTop), z.EspesorM, gris);
        CotaV(x1, PY(a.YZapTop), PY(a.YTerreno), a.YTerreno - a.YZapTop, gris);
        CotaV(x2, PY(a.YPlantillaBot), PY(a.YTerreno), a.YTerreno - a.YPlantillaBot, gris);

        var yRotulo = PY(a.YPlantillaBot) + (0.30 * escala);

        EtiquetaZapata("ELEVACIÓN", left, yRotulo, 10.5, azul, true);

        var fc = string.IsNullOrWhiteSpace(fila.Fc) ? string.Empty : $"    ·    f'c = {fila.Fc}";
        var doblez = TrazoZapata.FactorGanchoValido(FactorGanchoElegido);

        EtiquetaZapata(
            $"Rec. {z.RecM * 100:0.#} cm{fc}    ·    doblez {doblez:0.#} Ø",
            left, yRotulo + 15, 10, gris);
    }

    /// <summary>
    /// Las varillas <b>longitudinales</b> del dado y de la columna, con su pata de arranque y su
    /// transición.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el acero que más se revisa de un dado y era el que no se veía en la previa: solo estaban
    /// los estribos, así que el dado salía como una caja rayada. Ahora se dibuja lo mismo que va al
    /// plano y con la misma cuenta:
    /// </para>
    /// <list type="bullet">
    ///   <item>Dónde va cada varilla lo dice <see cref="TrazoZapata.BarrasRectangulares"/>, el mismo
    ///   reparto que usa el dibujante.</item>
    ///   <item>La <b>pata</b> de arranque mide los diámetros de la casilla de la hoja, no los 15
    ///   fijos: así se ve el efecto de cambiarla antes de dibujar.</item>
    ///   <item>La <b>transición</b> a 1:6 sale de <see cref="TrazoZapata.Desplazamiento"/>, así que
    ///   la previa enseña el mismo doblez que va a salir, y si no cabe tampoco lo enseña.</item>
    /// </list>
    /// </remarks>
    private void DibujarLongitudinalesPrevias(
        ZapataCad z, TrazoZapata.Acomodo a,
        Func<double, double> px, Func<double, double> py, Brush trazo)
    {
        var dSup = DiametroMDeVarilla(z.VarDadoSup);
        var dInf = DiametroMDeVarilla(z.VarDadoInf);

        if (dSup <= 0 && dInf <= 0)
        {
            return;
        }

        var recDado = z.RecDadoCm * TrazoZapata.EscalaElevacion;
        var recCol = z.RecColumnaCm * TrazoZapata.EscalaElevacion;

        var barrasDado = TrazoZapata.BarrasRectangulares(
            a.XDadoDer, a.XDadoDer - a.XDadoIzq, recDado, dSup, dInf, z.NIntDado);

        var barrasCol = TrazoZapata.BarrasRectangulares(
            a.XColDer, a.XColDer - a.XColIzq, recCol,
            DiametroMDeVarilla(z.VarColSup), DiametroMDeVarilla(z.VarColInf), z.NIntColumna);

        // La pata de arranque, con los diámetros de la casilla de la hoja.
        var factor = TrazoZapata.FactorGanchoValido(FactorGanchoElegido);

        var yPata = a.YZapBot + recDado;
        var yTopeDado = a.YDadoTop - recDado;

        // ¿Hay transición? Se pregunta igual que el dibujante: mismas posiciones, mismo 1:6.
        var xs = new List<(double Dado, double Col)>();

        if (z.ColumnaDeConcreto)
        {
            var ordD = new List<double> { barrasDado.Izq, barrasDado.Der };
            var ordC = new List<double> { barrasCol.Izq, barrasCol.Der };

            ordD.AddRange(barrasDado.Intermedias);
            ordC.AddRange(barrasCol.Intermedias);

            ordD.Sort();
            ordC.Sort();

            var pares = Math.Min(ordD.Count, ordC.Count);

            for (var i = 0; i < pares; i++)
            {
                xs.Add((ordD[i], ordC[i]));
            }
        }

        var dxMax = xs.Count == 0 ? 0 : xs.Max(p => Math.Abs(p.Col - p.Dado));

        var hayUnion = z.ColumnaDeConcreto
                       && dxMax <= TrazoZapata.DesplazamientoMax
                       && xs.Count > 0;

        var trans = TrazoZapata.Desplazamiento(dxMax, a.YZapTop, a.YDadoTop, recDado);

        var yQuiebre = hayUnion && trans.Cabe ? trans.YZonaBot : yTopeDado;

        // ---- Las varillas del dado ----
        var todas = new List<double> { barrasDado.Izq, barrasDado.Der };
        todas.AddRange(barrasDado.Intermedias);

        foreach (var x in todas)
        {
            // La recta de abajo, hasta donde arranque el doblez.
            Recta(px(x), py(yPata), px(x), py(yQuiebre), trazo, 1.5);

            // Y su pata, hacia dentro del dado.
            var haciaDentro = x < (a.XDadoIzq + a.XDadoDer) / 2 ? 1 : -1;
            var largo = factor * Math.Max(dSup, dInf);

            Recta(px(x), py(yPata), px(x + (haciaDentro * largo)), py(yPata), trazo, 1.5);
        }

        // ---- La transición, si cabe ----
        if (hayUnion && trans.Cabe)
        {
            var cian = new SolidColorBrush(Color.FromRgb(0x00, 0xA6, 0xB8));

            foreach (var (xd, xc) in xs)
            {
                Recta(px(xd), py(trans.YZonaBot), px(xc), py(trans.YDiagTop), cian, 1.6);
                Recta(px(xc), py(trans.YDiagTop), px(xc), py(a.YDadoTop + recCol), cian, 1.5);
            }
        }
        else if (z.ColumnaDeConcreto)
        {
            // Sin transición las del dado siguen rectas hasta su tope, como en el plano.
            foreach (var x in todas)
            {
                Recta(px(x), py(yQuiebre), px(x), py(yTopeDado), trazo, 1.5);
            }
        }

        // ---- Las de la columna ----
        if (!z.ColumnaDeConcreto)
        {
            return;
        }

        var yColTope = a.YDadoTop + (0.8 * (8.0 / 9.0));
        var deColumna = new List<double> { barrasCol.Izq, barrasCol.Der };
        deColumna.AddRange(barrasCol.Intermedias);

        foreach (var x in deColumna)
        {
            Recta(px(x), py(a.YDadoTop + recCol), px(x), py(yColTope), trazo, 1.5);
        }
    }

    /// <summary>El diámetro de una varilla en <b>metros</b>, o 0 si la celda está vacía.</summary>
    private static double DiametroMDeVarilla(string? clave) =>
        Varilla.TryDiametroCm(clave, out var cm) ? cm / 100.0 : 0;

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
        // Los tres tramos los lee TrazoZapata, que es quien los lee tambien al dibujar: asi la
        // previa reparte los estribos igual que el plano.
        var tramos = TrazoZapata.TramosCm(z.SepEstriboDado);

        var centros = TrazoZapata.CentrosEstribos(
            z.ProfundidadM, tramos[0], tramos[1], tramos[2],
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

    /// <summary>La <b>planta</b>, en su mitad del cuadro y con sus cotas.</summary>
    /// <remarks>
    /// El paño de la zapata, el hueco del dado —centrado o pegado al paño derecho, según el
    /// tipo, igual que en la elevación— y las dos mallas. Con doble parrilla va además la línea
    /// de rotura de la diagonal, que es lo que separa una parrilla de la otra en el plano.
    /// </remarks>
    private void DibujarPlantaPrevia(
        ZapataCad z, TrazoZapata.Acomodo a,
        double left, double top, double w, double h, Brush azul, Brush gris)
    {
        var yBot = a.YPlanta;
        var yTop = a.YPlanta + z.LargoM;

        const double aireCota = 0.30;

        var anchoModelo = z.AnchoM + (2 * aireCota);
        var altoModelo = z.LargoM + (2 * aireCota);

        var escala = Math.Min(w / anchoModelo, h / altoModelo);

        if (escala <= 0 || double.IsInfinity(escala))
        {
            return;
        }

        var dx = left + ((w - (anchoModelo * escala)) / 2) + (aireCota * escala) - (a.XBase * escala);
        var dy = top + ((h - (altoModelo * escala)) / 2) + ((altoModelo - aireCota) * escala)
                 + (yBot * escala);

        double PX(double x) => dx + (x * escala);
        double PY(double y) => dy - (y * escala);

        // El concreto de la planta, para que la malla se lea sobre él y no sobre el fondo.
        Relleno(PX(a.XBase), PY(yTop), PX(a.XDer), PY(yBot), PincelConcreto);

        Contorno(PX(a.XBase), PY(yTop), PX(a.XDer), PY(yBot), azul, 1.6);

        var (hx1, hy1, hx2, hy2) = TrazoZapata.HuecoDelDado(z, a.XBase, yBot);

        var rojo = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        var rosa = new SolidColorBrush(Color.FromRgb(0xE0, 0x8B, 0x7F));

        DibujarMallaPrevia(z, a, PX, PY, yBot, yTop, z.VarInf, z.SepInf, z.VarInfTrans,
            z.SepInfTrans, rojo);

        if (z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup))
        {
            DibujarMallaPrevia(z, a, PX, PY, yBot, yTop, z.VarSup, z.SepSup, z.VarSupTrans,
                z.SepSupTrans, rosa);

            Recta(PX(a.XBase), PY(yBot), PX(a.XDer), PY(yTop), gris, 0.8);
        }

        // El dado va encima de la malla, como en el dibujo: es lo que se ve en planta. Y con su
        // relleno, que es lo que hace que se lea como un bloque y no como un cuadro más de la malla.
        Relleno(PX(hx1), PY(hy2), PX(hx2), PY(hy1), PincelConcreto);
        Contorno(PX(hx1), PY(hy2), PX(hx2), PY(hy1), azul, 1.3);

        var idDado = (z.IdDado ?? string.Empty).Trim();

        if (idDado.Length > 0 && hx2 - hx1 > 0.20)
        {
            EtiquetaZapata(
                idDado, ((PX(hx1) + PX(hx2)) / 2) - (idDado.Length * 2.6),
                ((PY(hy1) + PY(hy2)) / 2) - 7, 9.5, azul, true);
        }

        // ---------- COTAS ----------
        // Las de la macro, como en el dibujo: el ancho abajo, el largo a la izquierda, y las dos
        // del dado -su ancho arriba y su largo a la derecha-, que ahí miden exactamente el bloque.
        CotaH(PX(a.XBase), PX(a.XDer), PY(yBot) + (0.12 * escala), z.AnchoM, gris);
        CotaV(PX(a.XBase) - (0.12 * escala), PY(yBot), PY(yTop), z.LargoM, gris);

        CotaH(PX(hx1), PX(hx2), PY(yTop) - (0.10 * escala), hx2 - hx1, gris);
        CotaV(PX(a.XDer) + (0.10 * escala), PY(hy1), PY(hy2), hy2 - hy1, gris);

        EtiquetaZapata("PLANTA", left, PY(yBot) + (0.26 * escala), 10.5, azul, true);

        var parrillas = z.DobleParrilla && !string.IsNullOrWhiteSpace(z.VarSup)
            ? "doble parrilla"
            : "una parrilla";

        EtiquetaZapata(
            $"Escala 1:10    ·    {parrillas}", left, PY(yBot) + (0.26 * escala) + 15, 10, gris);
    }

    /// <summary>Una cota <b>horizontal</b>: su línea, sus dos topes y su número.</summary>
    /// <remarks>
    /// Es una cota de vista previa, no la de AutoCAD: línea con topes y el número encima. Lo que
    /// importa aquí es <b>qué se mide</b> —los mismos tramos que acota la macro— porque es lo que
    /// se revisa antes de mandar el dibujo; el aparato de la cota lo pone AutoCAD con el estilo
    /// COTA_ESTRUCTURAL. Y el número va en <b>metros con dos decimales</b>, como el plano.
    /// </remarks>
    private void CotaH(double x1, double x2, double y, double valorM, Brush trazo)
    {
        if (Math.Abs(x2 - x1) < 6)
        {
            return;
        }

        Recta(x1, y, x2, y, trazo, 0.8);
        Recta(x1, y - 4, x1, y + 4, trazo, 0.8);
        Recta(x2, y - 4, x2, y + 4, trazo, 0.8);

        var texto = valorM.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

        EtiquetaZapata(texto, ((x1 + x2) / 2) - 12, y - 15, 10, trazo);
    }

    /// <summary>Una cota <b>vertical</b>.</summary>
    private void CotaV(double x, double y1, double y2, double valorM, Brush trazo)
    {
        if (Math.Abs(y2 - y1) < 6)
        {
            return;
        }

        Recta(x, y1, x, y2, trazo, 0.8);
        Recta(x - 4, y1, x + 4, y1, trazo, 0.8);
        Recta(x - 4, y2, x + 4, y2, trazo, 0.8);

        var texto = valorM.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

        EtiquetaZapata(texto, x - 26, ((y1 + y2) / 2) - 7, 10, trazo);
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
    /// <remarks>
    /// No lee la celda por su cuenta: se lo pide a <see cref="TrazoZapata.SeparacionM"/>, que es
    /// el mismo lector que usa el dibujante de AutoCAD. Con un lector aquí y otro allá, una celda
    /// escrita «@20» podría salir de 20 cm en la previa y de 12 en el plano.
    /// </remarks>
    private static double LeerSeparacionM(string? texto) => TrazoZapata.SeparacionM(texto);

    // ======================================================================
    // LOS COLORES Y LAS TEXTURAS DE LA VISTA PREVIA
    // ======================================================================
    //
    // LO QUE SE PIDIO: «que la previsualizacion se vea mejor detallada y con colores, se ven muy
    // vacias». Estaba dibujada a puro contorno, asi que un cuadro con la mitad del sitio en blanco
    // no decia si el dibujo iba bien o si faltaba algo.
    //
    // Los colores NO son decorativos: son los mismos papeles que en el plano, uno por cosa, para
    // que de un vistazo se vea qué es cada linea. Y las texturas son las de AutoCAD -el AR-CONC del
    // concreto y el EARTH del terreno- reducidas a un mosaico que se lee en unos centimetros.

    /// <summary>Concreto: gris claro con su granito, como el AR-CONC del plano.</summary>
    private static readonly Brush PincelConcreto = Textura(
        Color.FromRgb(0xE6, 0xE8, 0xEA), Color.FromRgb(0xAE, 0xB6, 0xBD), 9, punteado: true);

    /// <summary>Plantilla de concreto simple: el mismo granito, un tono más oscuro.</summary>
    private static readonly Brush PincelPlantilla = Textura(
        Color.FromRgb(0xD3, 0xD7, 0xDB), Color.FromRgb(0x99, 0xA3, 0xAC), 7, punteado: true);

    /// <summary>Terreno: el ladrillo del EARTH, en tierra.</summary>
    private static readonly Brush PincelTerreno = Textura(
        Color.FromRgb(0xEC, 0xE1, 0xD3), Color.FromRgb(0xC0, 0xA6, 0x86), 11, punteado: false);

    /// <summary>Un mosaico de textura, congelado: se crea una vez y se reutiliza.</summary>
    /// <remarks>
    /// <para>
    /// <paramref name="punteado"/> da el granito del concreto —puntos sueltos— y su contrario da el
    /// ladrillo del terreno: una línea horizontal y las dos verticales alternadas del EARTH.
    /// </para>
    /// <para>
    /// Va <c>Freeze()</c> porque el mosaico no cambia nunca y así WPF no lo vuelve a evaluar en cada
    /// redibujo. La vista previa se redibuja en cada tecla que se pulsa en la tabla.
    /// </para>
    /// </remarks>
    private static Brush Textura(Color fondo, Color trazo, double lado, bool punteado)
    {
        var grupo = new DrawingGroup();

        grupo.Children.Add(new GeometryDrawing(
            new SolidColorBrush(fondo), null, new RectangleGeometry(new Rect(0, 0, lado, lado))));

        var figuras = new GeometryGroup();

        if (punteado)
        {
            var r = lado / 11;

            figuras.Children.Add(new EllipseGeometry(new Point(lado * 0.25, lado * 0.30), r, r));
            figuras.Children.Add(new EllipseGeometry(new Point(lado * 0.70, lado * 0.55), r, r));
            figuras.Children.Add(new EllipseGeometry(new Point(lado * 0.45, lado * 0.85), r, r));

            grupo.Children.Add(new GeometryDrawing(new SolidColorBrush(trazo), null, figuras));
        }
        else
        {
            figuras.Children.Add(new LineGeometry(
                new Point(0, lado / 2), new Point(lado, lado / 2)));
            figuras.Children.Add(new LineGeometry(
                new Point(lado / 2, 0), new Point(lado / 2, lado / 2)));
            figuras.Children.Add(new LineGeometry(
                new Point(0, lado / 2), new Point(0, lado)));

            grupo.Children.Add(new GeometryDrawing(
                null, new Pen(new SolidColorBrush(trazo), 0.7), figuras));
        }

        var pincel = new DrawingBrush(grupo)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, lado, lado),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };

        pincel.Freeze();

        return pincel;
    }

    /// <summary>Un relleno rectangular, por debajo de todo lo que se dibuje después.</summary>
    private void Relleno(double x1, double y1, double x2, double y2, Brush pincel)
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

        ZapataPreviewCanvas.Children.Add(r);
    }

    /// <summary>
    /// La <b>leyenda</b> de colores: qué es cada cosa del dibujo.
    /// </summary>
    /// <remarks>
    /// Con seis colores en el cuadro hace falta decir cuál es cuál, y decirlo <b>en el cuadro</b>:
    /// una leyenda en la ayuda no se lee mientras se revisa un dibujo.
    /// </remarks>
    private void LeyendaZapata(double left, double top, params (Brush Color, string Texto)[] partes)
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

            ZapataPreviewCanvas.Children.Add(chip);

            EtiquetaZapata(texto, x + 13, top, 9.5, PinceLeyenda);

            x += 13 + (texto.Length * 5.4) + 14;
        }
    }

    /// <summary>El gris de los textos de la leyenda.</summary>
    private static readonly Brush PinceLeyenda =
        new SolidColorBrush(Color.FromRgb(0x60, 0x6A, 0x74));

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
