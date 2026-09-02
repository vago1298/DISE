using System.Globalization;
using System.Windows.Data;

namespace CadLink.App;

/// <summary>
/// Le pone el símbolo de <b>pulgada</b> a un valor para verlo en la cuadrícula.
/// </summary>
/// <remarks>
/// <para>
/// Las celdas de la hoja de placas base que van en pulgadas —el espesor de la placa, los diámetros
/// de ancla y de agujero, la soldadura y los espesores de cartabón— se capturan en <b>formato
/// libre</b>: <c>1</c>, <c>3/4</c>, <c>1 1/4</c>, con comillas o sin ellas. Eso es lo cómodo al
/// escribir, y lo malo al leer: en la columna queda un <c>1</c> suelto que no dice si es una pulgada
/// o un centímetro. El encabezado lo dice, pero el encabezado se pierde de vista en cuanto la tabla
/// se desplaza a lo ancho, y esta hoja tiene cuarenta columnas.
/// </para>
/// <para>
/// Así que el símbolo se <b>pone al mostrar</b>, no al guardar. Guardarlo dentro del texto obligaría
/// a quitarlo antes de cada cuenta, y ese es el tipo de limpieza que se olvida en un sitio: un
/// espesor guardado como <c>1"</c> que en algún camino se lea sin limpiar da cero, y un cero en un
/// espesor no salta a la vista.
/// </para>
/// <para>
/// Es un convertidor y no una propiedad por celda a propósito: son <b>ocho</b> columnas con el mismo
/// problema, y ocho propiedades de solo lectura —cada una con su <c>Raise</c> que hay que acordarse
/// de disparar— es la clase de repetición en la que una se queda sin actualizar.
/// </para>
/// </remarks>
public sealed class ConPulgadas : IValueConverter
{
    /// <summary>El símbolo, en un solo sitio.</summary>
    public const string Simbolo = "\"";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var texto = (value as string ?? value?.ToString() ?? string.Empty).Trim();

        if (texto.Length == 0)
        {
            // Vacío se queda vacío. Un «"» solo en la celda parecería un dato.
            return string.Empty;
        }

        // Si ya lo trae —porque se capturó con comillas— no se duplica.
        return texto.EndsWith(Simbolo, StringComparison.Ordinal) ? texto : texto + Simbolo;
    }

    /// <summary>
    /// Quita el símbolo al volver.
    /// </summary>
    /// <remarks>
    /// Hace falta aunque las celdas editables se enlacen al valor crudo: un enlace de dos vías que
    /// no sepa volver lanza en cuanto algo lo use, y dejarlo sin implementar es dejar una trampa
    /// para el día que alguien enlace una celda de edición a través de aquí.
    /// </remarks>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var texto = (value as string ?? value?.ToString() ?? string.Empty).Trim();

        return texto.EndsWith(Simbolo, StringComparison.Ordinal)
            ? texto[..^Simbolo.Length].Trim()
            : texto;
    }
}
