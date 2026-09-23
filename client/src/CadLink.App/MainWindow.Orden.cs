using System.Windows;
using CadLink.App.Models;

namespace CadLink.App;

/// <summary>
/// El botón <b>Ordenar</b> de la hoja de secciones de concreto.
/// </summary>
/// <remarks>
/// <para>
/// Lo que pidió el usuario: <i>«que ponga todos los castillos juntos, todas las trabes juntas,
/// porque a veces los agrego después y al dibujarlos en AutoCAD están separados»</i>.
/// </para>
/// <para>
/// Y ese es el punto: <b>la hoja se dibuja en el orden en que está</b>. El dibujante recorre las
/// filas de arriba abajo y va colocando cada sección al lado de la anterior, así que una trabe
/// capturada al final del día aparece en el plano lejos de las demás trabes. Ordenar la hoja es
/// ordenar el plano.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// Verdadero mientras el botón <b>Ordenar</b> está moviendo las filas.
    /// </summary>
    /// <remarks>
    /// Con ella encendida, <c>DatosCambiaron</c> no hace nada: un reordenado son decenas de
    /// movimientos que son <b>un solo cambio</b> para el usuario, así que se apila un solo paso de
    /// deshacer y se redibuja una sola vez, al final. Ver <see cref="OnOrdenarSecciones"/>.
    /// </remarks>
    private bool _reordenando;

    /// <summary>
    /// Agrupa las secciones por elemento, y dentro de cada grupo las numera en orden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Se mueven las filas, no se copian.</b> Se usa <c>Move</c> de la colección en lugar de
    /// vaciarla y volver a llenarla, y eso importa por tres cosas: la fila seleccionada sigue
    /// siendo el mismo objeto —así que la vista previa no se pierde—, las grapas que cuelgan de
    /// cada sección siguen en su sitio, y no se disparan cien altas y bajas que volverían a
    /// suscribir los avisos de cada fila.
    /// </para>
    /// <para>
    /// <b>Ordenar es UN paso de deshacer, no cuarenta.</b> Cada <c>Move</c> avisa a la colección y
    /// ese aviso pasa por <c>DatosCambiaron</c>, así que tal cual, ordenar cuarenta filas apilaría
    /// treinta y cinco pasos en el historial —Ctrl+Z treinta y cinco veces para volver atrás— y
    /// redibujaría la vista previa treinta y cinco veces. Por eso se enciende
    /// <see cref="_reordenando"/> mientras se mueven las filas y se llama a <c>DatosCambiaron</c>
    /// UNA vez al final. Ctrl+Z devuelve la hoja a como estaba, de un golpe: ordenar no es una
    /// decisión que haya que pensar dos veces.
    /// </para>
    /// <para>
    /// El orden es el del desplegable —<see cref="SeccionConcretoRow.ElementosEnOrden"/>— y no uno
    /// propio: es el que el usuario ya tiene delante al elegir el elemento, y está en un solo
    /// sitio. Dentro de cada elemento manda el ID, comparado como lo lee una persona: <c>K-2</c>
    /// antes de <c>K-10</c>. Ver <see cref="SeccionConcretoRow.PorId"/>.
    /// </para>
    /// </remarks>
    private void OnOrdenarSecciones(object sender, RoutedEventArgs e)
    {
        // Primero se cierra la celda que se esté editando. Sin esto, lo que hay a medio teclear
        // no está todavía en la fila, así que se ordenaría con el valor viejo y al cerrarse la
        // celda la fila cambiaría de grupo delante del usuario.
        CerrarEdicionDeLasHojas();

        var filas = _datos.SeccionesConcreto;

        if (filas.Count < 2)
        {
            StatusText.Text = "Ordenar: hacen falta al menos dos secciones.";
            return;
        }

        // La seleccionada se guarda para devolverle el foco al terminar: sin esto la selección
        // se queda en el RENGLÓN, no en la fila, y después de ordenar la vista previa enseñaría
        // una sección distinta de la que estaba mirando.
        var seleccionada = SeccionesGrid.SelectedItem as SeccionConcretoRow;

        var ordenadas = filas
            .OrderBy(f => SeccionConcretoRow.OrdenDeElemento(f.Elemento))

            // Los escritos a mano —los que no están en el desplegable— comparten el último
            // lugar, así que entre ellos se ordenan por su nombre. Así también quedan
            // agrupados, que es de lo que se trata.
            .ThenBy(f => (f.Elemento ?? string.Empty).Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.Id ?? string.Empty, SeccionConcretoRow.PorId)
            .ToList();

        var movidas = 0;

        // Encendida mientras se mueven las filas: así los avisos de la colección no apilan un
        // paso de deshacer ni redibujan la vista previa por cada fila. Se apaga en el finally, y
        // ya apagada se llama a DatosCambiaron una sola vez, más abajo.
        _reordenando = true;

        try
        {
            for (var destino = 0; destino < ordenadas.Count; destino++)
            {
                var actual = filas.IndexOf(ordenadas[destino]);

                if (actual == destino)
                {
                    continue;
                }

                filas.Move(actual, destino);
                movidas++;
            }
        }
        finally
        {
            // En el finally y no después del bucle: si un Move tirara una excepción, la bandera
            // se quedaría encendida y la hoja dejaría de registrar deshacer y de redibujar para
            // lo que queda de sesión, sin que nada lo dijera.
            _reordenando = false;
        }

        if (movidas == 0)
        {
            // Nada se movió, así que NO se llama a DatosCambiaron: un «deshacer» que no deshace
            // nada visible es peor que no tener paso ninguno.
            StatusText.Text = "Ordenar: las secciones ya estaban agrupadas.";
            return;
        }

        if (seleccionada is not null)
        {
            SeccionesGrid.SelectedItem = seleccionada;
            SeccionesGrid.ScrollIntoView(seleccionada);
        }

        // El único aviso del reordenado: apila el paso de deshacer con la hoja como estaba ANTES,
        // refresca los recuentos y redibuja la vista previa -ya con la fila reseleccionada, para
        // que enseñe la sección que el usuario estaba mirando-.
        DatosCambiaron();

        // Cuántos GRUPOS quedaron, que es el dato que dice si el botón hizo lo que se esperaba.
        var grupos = filas
            .Select(f => (f.Elemento ?? string.Empty).Trim().ToUpperInvariant())
            .Distinct()
            .Count();

        StatusText.Text =
            $"Ordenar: {movidas} seccion(es) movidas, {grupos} grupo(s) de elemento. " +
            "El plano se dibuja en este orden. Ctrl+Z lo deshace.";
    }
}
