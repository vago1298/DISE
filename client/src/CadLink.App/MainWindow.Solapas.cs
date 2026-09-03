using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// El generador de <b>solapas</b>: un layout de AutoCAD por cada plano del juego.
/// </summary>
/// <remarks>
/// <para>
/// Port de la macro <c>GenerarSolapas</c>. La macro leía los datos de bloques de 18 filas de una
/// hoja de Excel; aquí ya están capturados en la pestaña <b>Proyecto</b> —la solapa del juego y la
/// tabla de planos—, así que lo que queda es armar un <see cref="SolapaCad"/> por plano y pasárselo
/// al dibujante.
/// </para>
/// <para>
/// <b>Lo que la macro capturaba y aquí no hace falta:</b> el nombre de la hoja, la fila inicial y el
/// paso entre bloques, las cuatro rutas de archivos, el modo de ajuste, los márgenes del marco, los
/// dígitos del número y los separadores. Eran configuración de la propia hoja de cálculo, o
/// decisiones que aquí ya están tomadas en un solo sitio.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>El bloque del cajetín que se busca primero, como en la macro.</summary>
    private const string CajetinPorOmision = "CAJETIN";

    /// <summary>Le pone a la celda del tamaño de hoja su lista.</summary>
    /// <remarks>
    /// Desde el code-behind y no con un recurso estático, igual que la celda del electrodo de la
    /// placa base: la lista vive en el modelo —<see cref="PlanoRow.Tamanos"/>— que es quien sabe
    /// cuáles son, y así no hay dos copias que puedan discrepar.
    /// </remarks>
    private void LlenarListasSolapas() =>
        ColTamanoHoja.ItemsSource = new PlanoRow().Tamanos;

    /// <summary>
    /// Arma los datos de una solapa juntando lo del <b>juego</b> con lo del <b>plano</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El reparto es el de la macro: calculista, cédula, propietario, ubicación, proyecto, quién
    /// dibujó, fecha y acotación son del juego y salen iguales en los veinte planos; clave,
    /// contenido, detalle, escala y número son de cada plano.
    /// </para>
    /// <para>
    /// La <b>escala</b> sale del plano y no del juego. La del juego es solo el valor por omisión con
    /// el que arranca un plano nuevo: un juego lleva plantas a 1:100 y detalles a 1:20, y rotular
    /// todos con la del juego pone en el plano una escala que no es la del dibujo.
    /// </para>
    /// </remarks>
    private SolapaCad SolapaDeUnPlano(PlanoRow plano)
    {
        var s = _juego.Solapa;

        var medidas = MedidasDeLaHoja(plano.Tamano);

        return new SolapaCad
        {
            Titulo = plano.Contiene,
            Tamano = plano.Tamano.Trim(),
            AnchoPulg = medidas.Ancho,
            AltoPulg = medidas.Alto,
            Horizontal = plano.Horizontal,

            Calculista = s.Calculista,
            Cedula = s.Cedula,
            Propietario = s.Propietario,
            Ubicacion = s.Ubicacion,
            Proyecto = s.Obra,
            Dibujo = s.Dibujo,

            // El mes y el año con letra, que es lo que ya se rotula en el resto de los planos.
            Fecha = s.FechaTexto,
            Acotacion = s.Acotacion,

            Contenido = plano.Contiene,
            Detalle = plano.Detalle,
            Escala = plano.Escala,
            Clave = plano.Clave,
            Numero = plano.Numero,
            Total = plano.Total,
        };
    }

    /// <summary>
    /// Las medidas nominales de un tamaño de hoja, en <b>pulgadas</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sirven para <b>buscar</b> el pliego en el dispositivo, no para dibujar: el dibujante toma la
    /// medida real del papel que quedó asignado. Por eso basta con la nominal y por eso una hoja que
    /// no esté en esta tabla no es un problema —se busca por nombre, que es la tercera estrategia de
    /// <see cref="Solapas.BuscarPapel"/>—.
    /// </para>
    /// <para>
    /// Se devuelve siempre <b>lado corto por lado largo</b>. La orientación la pone el plano, y
    /// <see cref="Solapas.HojaOrientada"/> es quien las acomoda: guardarlas ya orientadas aquí
    /// sería decidir dos veces lo mismo.
    /// </para>
    /// </remarks>
    private static (double Ancho, double Alto) MedidasDeLaHoja(string? tamano)
    {
        var t = Solapas.Normaliza(tamano);

        // ARCH e ANSI en pulgadas; ISO en milímetros pasados a pulgadas.
        var tabla = new Dictionary<string, (double, double)>
        {
            ["archa"] = (9, 12),
            ["archb"] = (12, 18),
            ["archc"] = (18, 24),
            ["archd"] = (24, 36),
            ["arche"] = (36, 48),
            ["arche1"] = (30, 42),
            ["arche2"] = (26, 38),
            ["arche3"] = (27, 39),
            ["ansia"] = (8.5, 11),
            ["ansib"] = (11, 17),
            ["ansic"] = (17, 22),
            ["ansid"] = (22, 34),
            ["ansie"] = (34, 44),
            ["isoa4"] = (210 / 25.4, 297 / 25.4),
            ["isoa3"] = (297 / 25.4, 420 / 25.4),
            ["isoa2"] = (420 / 25.4, 594 / 25.4),
            ["isoa1"] = (594 / 25.4, 841 / 25.4),
            ["isoa0"] = (841 / 25.4, 1189 / 25.4),
        };

        if (tabla.TryGetValue(t, out var m))
        {
            return m;
        }

        // Un tamaño personalizado del despacho: se busca por NOMBRE, así que no hace falta medida.
        // Cero y cero, y SolapaCad.Falta lo dirá si tampoco se encuentra por nombre.
        return (0, 0);
    }

    /// <summary>
    /// Crea en AutoCAD un layout por plano, con su papel y su cajetín relleno.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El cajetín tiene que estar en el dibujo.</b> Si no se llama <c>CAJETIN</c>, se busca el
    /// bloque que más atributos conocidos tenga y se usa ese. Lo que no se hace es traerlo de un
    /// archivo: la macro tenía cuatro rutas configurables, dos estrategias de importación y un aviso
    /// de autorreferencia de veinte renglones para el caso en que el dibujo activo <i>fuera</i> el
    /// archivo de la solapa. Aquí se le pide al usuario que inserte su cajetín una vez, que es un
    /// <c>INSERT</c>, y a cambio desaparecen esos tres caminos y sus errores.
    /// </para>
    /// <para>
    /// Y no se toca <c>INSUNITS</c> ni <c>LIGHTINGUNITS</c>. La macro los apagaba para que AutoCAD
    /// no reescalara el bloque al traerlo del archivo, y para silenciar el aviso de iluminación que
    /// eso provocaba. Insertando por nombre un bloque que ya está en el dibujo no hay conversión de
    /// unidades, así que no hay nada que silenciar: el dibujo se queda como estaba.
    /// </para>
    /// </remarks>
    private void OnGenerarSolapas(object sender, RoutedEventArgs e)
    {
        if (_juego.Planos.Count == 0)
        {
            MessageBox.Show(
                "Agrega al menos un plano al juego antes de generar las solapas.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        var solapas = _juego.Planos.Select(SolapaDeUnPlano).ToList();

        var incompletas = solapas
            .Where(s => s.Falta.Count > 0)
            .Select(s => $"  • {NombreDelPlano(s)}: falta {string.Join("; ", s.Falta)}")
            .ToList();

        if (incompletas.Count > 0)
        {
            MessageBox.Show(
                "Corrige esto antes de generar las solapas:\n\n" + string.Join("\n", incompletas),
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            dynamic app = AcadConnection.Connect(launchIfMissing: false);
            dynamic doc = AcadConnection.GetOrCreateDocument(app);

            var dibujante = new SolapasDrawer(doc);

            // ---------- QUÉ BLOQUE ES EL CAJETÍN ----------
            if (!dibujante.ExisteBloque(CajetinPorOmision))
            {
                var hallado = dibujante.BuscarCajetin(out var cuantos);

                if (hallado is null)
                {
                    MessageBox.Show(
                        $"El dibujo no tiene un bloque «{CajetinPorOmision}», y tampoco encontré " +
                        "ningún bloque con los atributos de una solapa.\n\n" +
                        "El cajetín tiene que ser un BLOQUE CON ATRIBUTOS dentro de este dibujo:\n\n" +
                        "  1. Abre el archivo de tu cajetín\n" +
                        "  2. Selecciónalo con sus atributos y usa BLOCK\n" +
                        $"  3. Llámalo {CajetinPorOmision}\n" +
                        "  4. Insértalo una vez en este dibujo (INSERT) y borra la inserción:\n" +
                        "     la definición se queda cargada\n\n" +
                        "Si los textos del cajetín son texto normal y no atributos, conviértelos " +
                        "primero con ATTDEF o con TXT2ATT.",
                        AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

                    return;
                }

                dibujante.Cajetin = hallado;

                SolapasResumenText.Text =
                    $"Cajetín detectado: {hallado} ({cuantos} atributos).";
            }
            else
            {
                dibujante.Cajetin = CajetinPorOmision;
            }

            // ---------- UN LAYOUT POR PLANO ----------
            var hechos = new List<string>();

            foreach (var s in solapas)
            {
                var nombre = dibujante.Dibujar(s);

                if (nombre.Length > 0)
                {
                    hechos.Add(nombre);
                }
            }

            dibujante.MostrarPrimerLayout();

            SolapasResumenText.Text = hechos.Count == 0
                ? "No se generó ninguna solapa."
                : $"{hechos.Count} de {solapas.Count} solapas generadas con el cajetín " +
                  $"«{dibujante.Cajetin}».";

            MessageBox.Show(
                $"Cajetín: {dibujante.Cajetin}\n" +
                $"Solapas generadas: {hechos.Count} de {solapas.Count}\n" +
                new string('-', 54) + "\n" +
                string.Join("\n", dibujante.Notas),
                AppInfo.ProductName, MessageBoxButton.OK,
                hechos.Count == solapas.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(
                "No se pudieron generar las solapas.\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Cómo se nombra un plano en los avisos: su clave, o su contenido.
    /// </summary>
    /// <remarks>
    /// <b>No se llama <c>Etiqueta</c></b>, aunque sería el nombre natural: en esta misma clase
    /// —repartida en ocho archivos— <c>Etiqueta</c> ya es un método que <b>dibuja</b> un rótulo en
    /// un lienzo. Compilaría, porque el tipo del parámetro los distingue, pero dos métodos con el
    /// mismo nombre y el sentido contrario en la misma clase se leen mal una vez y se copian mal la
    /// siguiente.
    /// </remarks>
    private static string NombreDelPlano(SolapaCad s)
    {
        var clave = s.Clave.Trim();

        if (clave.Length > 0)
        {
            return clave;
        }

        var titulo = s.Titulo.Trim();

        return titulo.Length > 0 ? titulo : "plano sin clave";
    }
}
