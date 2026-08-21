using System.Windows;
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

        if (dado.BaseCm > 0)
        {
            fila.AnchoDadoCm = dado.BaseCm;
        }

        if (dado.RecubrimientoCm > 0)
        {
            fila.RecDadoCm = dado.RecubrimientoCm;
        }
    }

    private static bool EsColumnaDeConcreto(string? elemento)
    {
        var e = (elemento ?? string.Empty).Trim();

        return SeccionConcretoRow.ElementoColumna.Equals(e, StringComparison.OrdinalIgnoreCase)
            || SeccionConcretoRow.ElementoColumnaCircular.Equals(e, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsDado(string? elemento)
    {
        var e = (elemento ?? string.Empty).Trim();

        return SeccionConcretoRow.ElementoDado.Equals(e, StringComparison.OrdinalIgnoreCase)
            || SeccionConcretoRow.ElementoDadoCircular.Equals(e, StringComparison.OrdinalIgnoreCase);
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

            var dibujante = new ZapataDrawer(doc, catalogoDeVarillas);

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
        var abajo = 34.0;

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

        Recta(PX(a.XBase) - 10, PY(a.YTerreno), PX(a.XDer) + 10, PY(a.YTerreno), tierra, 1.2);

        Contorno(PX(a.XBase), PY(a.YZapBot), PX(a.XDer), PY(a.YPlantillaBot), gris, 1.0);
        Contorno(PX(a.XBase), PY(a.YZapTop), PX(a.XDer), PY(a.YZapBot), azul, 1.6);
        Contorno(PX(a.XDadoIzq), PY(a.YDadoTop), PX(a.XDadoDer), PY(a.YZapTop), azul, 1.4);

        if (z.ColumnaDeConcreto)
        {
            var yTope = a.YDadoTop + (0.8 * (8.0 / 9.0));

            Contorno(PX(a.XColIzq), PY(yTope), PX(a.XColDer), PY(a.YDadoTop), azul, 1.4);
        }

        DibujarEstribosDadoPrevio(z, a, PX, PY, gris);

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

        var x1 = PX(a.XBase) - (0.08 * escala);
        var x2 = PX(a.XBase) - (0.20 * escala);

        CotaV(x1, PY(a.YPlantillaBot), PY(a.YZapBot), TrazoZapata.PlantillaEspesor, gris);
        CotaV(x1, PY(a.YZapBot), PY(a.YZapTop), z.EspesorM, gris);
        CotaV(x1, PY(a.YZapTop), PY(a.YTerreno), a.YTerreno - a.YZapTop, gris);
        CotaV(x2, PY(a.YPlantillaBot), PY(a.YTerreno), a.YTerreno - a.YPlantillaBot, gris);

        EtiquetaZapata("ELEVACIÓN", left, PY(a.YPlantillaBot) + (0.30 * escala), 10.5, gris);

        var fc = string.IsNullOrWhiteSpace(fila.Fc) ? string.Empty : $"    ·    f'c = {fila.Fc}";

        EtiquetaZapata(
            $"Rec. 5 cm{fc}", left, PY(a.YPlantillaBot) + (0.30 * escala) + 15, 10, gris);
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

        // El dado va encima de la malla, como en el dibujo: es lo que se ve en planta.
        Contorno(PX(hx1), PY(hy2), PX(hx2), PY(hy1), azul, 1.3);

        // ---------- COTAS ----------
        // Las de la macro: el ancho abajo, el largo a la izquierda y las dos del dado
        // -su ancho arriba y su largo a la derecha-, que ahí miden exactamente el bloque.
        CotaH(PX(a.XBase), PX(a.XDer), PY(yBot) + (0.12 * escala), z.AnchoM, gris);
        CotaV(PX(a.XBase) - (0.12 * escala), PY(yBot), PY(yTop), z.LargoM, gris);

        CotaH(PX(hx1), PX(hx2), PY(yTop) - (0.10 * escala), hx2 - hx1, gris);
        CotaV(PX(a.XDer) + (0.10 * escala), PY(hy1), PY(hy2), hy2 - hy1, gris);

        EtiquetaZapata("PLANTA", left, PY(yBot) + (0.26 * escala), 10.5, gris);
        EtiquetaZapata("Escala 1:10", left, PY(yBot) + (0.26 * escala) + 15, 10, gris);
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
