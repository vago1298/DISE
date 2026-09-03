using System;
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
    /// <summary>
    /// Los nombres de bloque que se buscan en el dibujo, en orden.
    /// </summary>
    /// <remarks>
    /// La lista vive en <see cref="Solapas.NombresProbables"/> y es <b>la misma</b> que se usa para
    /// los nombres de archivo: quien guarda su formato en <c>SOLAPA.dwg</c> le llama <c>SOLAPA</c> al
    /// bloque. Buscar solo <c>CAJETIN</c> era pedirle al usuario que adivinara una convención.
    /// </remarks>
    private static string NombresDeCajetin => string.Join(", ", Solapas.NombresProbables);

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
        var t = Solapas.Normaliza(Solapas.SoloElTamano(tamano));

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
            // TRES INTENTOS, en este orden, y el archivo NO es el último recurso sino el tercero:
            //
            //   1. el bloque CAJETIN ya cargado en el dibujo
            //   2. cualquier bloque del dibujo con los atributos de una solapa
            //   3. el ARCHIVO del cajetín, buscado en el disco
            //
            // El disco va detrás de los dos primeros a propósito: si el dibujo YA tiene un cajetín,
            // traer otro del archivo deja dos definiciones parecidas y la siguiente corrida no sabe
            // cuál usar.
            if (!ElegirCajetin(dibujante))
            {
                return;
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
    /// Decide qué bloque se va a usar de cajetín, y avisa con detalle si no hay ninguno.
    /// </summary>
    /// <returns><c>false</c> si no se encontró: quien llama tiene que parar.</returns>
    /// <remarks>
    /// El aviso de fallo dice <b>dónde se buscó</b>. La primera versión explicaba cómo hacer un
    /// BLOCK a mano, que es una lección de AutoCAD que el dibujante no pidió; lo que necesita saber
    /// es en qué carpetas se miró para poder apuntar a la buena.
    /// </remarks>
    private bool ElegirCajetin(SolapasDrawer dibujante)
    {
        // 1. UN BLOQUE QUE SE LLAME COMO UN CAJETIN, ya cargado en el dibujo.
        //    Y no solo «CAJETIN»: tambien SOLAPA, FORMATO y MEMBRETE. Un nombre por omision no es
        //    una convencion que el usuario tenga que adivinar, y quien guarda su formato en
        //    SOLAPA.dwg le llama SOLAPA al bloque.
        var porNombre = dibujante.BuscarCajetinPorNombre();

        if (porNombre is not null)
        {
            dibujante.Cajetin = porNombre;
            dibujante.RevisarAtributos(porNombre);

            SolapasResumenText.Text = $"Cajetin del dibujo: {porNombre}";

            return true;
        }

        // 2. Cualquier otro bloque del dibujo que tenga atributos de solapa, se llame como se llame.
        var porAtributos = dibujante.BuscarCajetin(out var cuantos);

        if (porAtributos is not null)
        {
            dibujante.Cajetin = porAtributos;
            dibujante.RevisarAtributos(porAtributos);

            SolapasResumenText.Text = $"Cajetin detectado por sus atributos: {porAtributos} " +
                                      $"({cuantos} atributos).";

            return true;
        }

        // 3. EL ARCHIVO, en el disco. Es lo que hacia la macro.
        dibujante.RutaDelCajetin = CajetinRutaBox.Text.Trim();

        foreach (var c in CarpetasDondeBuscarElCajetin())
        {
            dibujante.CarpetasExtra.Add(c);
        }

        var deArchivo = dibujante.ImportarCajetin(out var deDonde);

        if (deArchivo is not null)
        {
            dibujante.Cajetin = deArchivo;

            SolapasResumenText.Text = $"Cajetin traido de: {deDonde}";

            return true;
        }

        // 4. EL CAJETIN SUELTO EN EL ESPACIO MODELO.
        //    Un archivo de cajetin normalmente NO tiene ningun bloque dentro: tiene el recuadro
        //    dibujado y sus atributos sueltos, porque para eso sirve -se inserta el archivo entero y
        //    AutoCAD hace el bloque solo-. Asi que cuando el archivo del cajetin ES el dibujo
        //    abierto, no se puede insertar en si mismo Y dentro no hay ningun bloque: los tres
        //    pasos de arriba fallan con el cajetin delante de los ojos.
        if (FormarCajetinDelDibujo(dibujante))
        {
            return true;
        }

        AvisarQueNoHayCajetin(dibujante);

        return false;
    }

    /// <summary>
    /// Forma el bloque con el cajetín que está <b>suelto</b> en el espacio modelo, preguntando.
    /// </summary>
    /// <remarks>
    /// <b>Se pregunta antes</b> porque esto modifica el dibujo del usuario: le agrega una definición
    /// de bloque. No borra nada —los originales se quedan— pero cambiarle el archivo sin avisar no se
    /// hace, y menos cuando el archivo es su plantilla de cajetín.
    /// </remarks>
    private bool FormarCajetinDelDibujo(SolapasDrawer dibujante)
    {
        var conocidos = dibujante.AtributosSueltos(out var tags);

        if (tags.Count == 0)
        {
            return false;
        }

        if (conocidos == 0)
        {
            // TIENE ATRIBUTOS SUELTOS, PERO CON OTROS NOMBRES. Formar el bloque no serviría de nada:
            // saldrían las solapas en blanco. Se dice cuáles hay y cuáles harían falta.
            MessageBox.Show(
                $"En el espacio modelo hay {tags.Count} atributos sueltos, que parecen tu cajetin, " +
                "pero ninguno se llama como los que esta solapa llena.\n\n" +
                "Los que tiene:\n  " + string.Join(", ", tags) + "\n\n" +
                "Los que necesita:\n  " + string.Join(", ", Solapas.TagsConocidos) + "\n\n" +
                "Renombra los tags con ATTDEF -o con el editor de atributos- para que coincidan, y " +
                "vuelve a generar.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

            return false;
        }

        var donde = dibujante.EsAutorreferencia
            ? "Tienes abierto el archivo de tu cajetin, asi que no se puede insertar en si mismo.\n\n"
            : "El dibujo tiene un cajetin suelto en el espacio modelo, sin blocar.\n\n";

        var r = MessageBox.Show(
            donde +
            $"Encontre {conocidos} atributos de solapa sueltos en el espacio modelo. Puedo formar " +
            "con ellos un bloque llamado CAJETIN y usarlo para las solapas.\n\n" +
            "No se borra nada: lo que hay en el espacio modelo se queda donde esta.\n\n" +
            "¿Lo formo?",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (r != MessageBoxResult.Yes)
        {
            return false;
        }

        var nombre = dibujante.CrearCajetinDelEspacioModelo(Solapas.NombresProbables[0]);

        if (nombre is null)
        {
            return false;
        }

        dibujante.Cajetin = nombre;
        dibujante.RevisarAtributos(nombre);

        SolapasResumenText.Text = $"Cajetin formado del espacio modelo: {nombre}";

        return true;
    }

    /// <summary>
    /// El aviso de que no hay cajetín, <b>con lo que pasó de verdad</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aquí van las notas del dibujante, y esto es lo importante.</b> La primera versión de este
    /// aviso listaba las rutas y nada más, así que cuando el archivo estaba en la lista y aun así
    /// fallaba —el caso que reportó el usuario— el motivo real quedaba guardado en
    /// <see cref="SolapasDrawer.Notas"/> y no se enseñaba. Ni él ni yo podíamos ver qué había pasado.
    /// </para>
    /// <para>
    /// Un aviso de error que no dice el error obliga a adivinar, y adivinar cuesta un viaje de ida y
    /// vuelta entero.
    /// </para>
    /// </remarks>
    private void AvisarQueNoHayCajetin(SolapasDrawer dibujante)
    {
        var texto = new System.Text.StringBuilder();

        texto.AppendLine("No encontre el cajetin.");
        texto.AppendLine();

        if (dibujante.Notas.Count > 0)
        {
            texto.AppendLine("QUE PASO:");

            foreach (var n in dibujante.Notas)
            {
                texto.AppendLine("  • " + n);
            }

            texto.AppendLine();
        }

        if (dibujante.EsAutorreferencia)
        {
            texto.AppendLine("LO QUE PASA ES ESTO:");
            texto.AppendLine();
            texto.AppendLine(
                "  El archivo del cajetin que apuntaste ES el dibujo que tienes abierto en");
            texto.AppendLine(
                "  AutoCAD, y un dibujo no se puede insertar dentro de si mismo.");
            texto.AppendLine();
            texto.AppendLine("  Abre TU PLANO -o un dibujo nuevo- y vuelve a generar las solapas.");
            texto.AppendLine("  El cajetin se trae solo desde el archivo, sin que tengas que abrirlo.");
            texto.AppendLine();
        }

        texto.AppendLine("Busque, en este orden:");
        texto.AppendLine($"  1. un bloque del dibujo llamado {NombresDeCajetin}");
        texto.AppendLine("  2. cualquier bloque del dibujo con atributos de solapa");
        texto.AppendLine("  3. el archivo del cajetin, en estas rutas:");

        if (dibujante.RutasMiradas.Count == 0)
        {
            texto.AppendLine("       (ninguna: no hay ruta capturada y el dibujo no se ha guardado)");
        }
        else
        {
            foreach (var r in dibujante.RutasMiradas)
            {
                texto.AppendLine("       " + r);
            }
        }

        texto.AppendLine("  4. un cajetin suelto en el espacio modelo, sin blocar");
        texto.AppendLine();
        texto.AppendLine(
            "Si tu cajetin ya esta en el dibujo pero con otro nombre, renombralo con RENAME a uno " +
            $"de estos: {NombresDeCajetin}.");
        texto.AppendLine();
        texto.AppendLine(
            "Y si sus rotulos son texto normal, tienen que ser ATRIBUTOS: conviertelos con ATTDEF " +
            "o con TXT2ATT.");

        MessageBox.Show(
            texto.ToString(), AppInfo.ProductName,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Las carpetas de siempre, después de la capturada y la del dibujo.
    /// </summary>
    /// <remarks>
    /// Son suposiciones, y por eso van al final: la ruta que el usuario escribe manda sobre
    /// cualquiera de estas. Están para que la primera vez funcione sin capturar nada.
    /// </remarks>
    private static List<string> CarpetasDondeBuscarElCajetin()
    {
        var salida = new List<string>();

        void Meter(string r)
        {
            if (r.Length > 0)
            {
                salida.Add(r);
            }
        }

        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (docs.Length > 0)
            {
                Meter(System.IO.Path.Combine(docs, "CadLink"));
                Meter(System.IO.Path.Combine(docs, "CadLink", "Cajetines"));
                Meter(docs);
            }

            // Junto al programa, que es donde caería un cajetín repartido con la instalación.
            var app = AppContext.BaseDirectory;

            if (app.Length > 0)
            {
                Meter(System.IO.Path.Combine(app, "Cajetines"));
                Meter(app);
            }
        }
        catch (Exception)
        {
            // Sin carpetas de respaldo, quedan la capturada y la del dibujo.
        }

        return salida;
    }

    /// <summary>Elige el archivo del cajetín con el diálogo de siempre.</summary>
    private void OnExaminarCajetin(object sender, RoutedEventArgs e)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elige el archivo del cajetin",
            Filter = "Dibujos de AutoCAD (*.dwg;*.dxf)|*.dwg;*.dxf|Todos los archivos (*.*)|*.*",
            DefaultExt = ".dwg",
        };

        var actual = CajetinRutaBox.Text.Trim();

        try
        {
            if (actual.Length > 0)
            {
                var carpeta = System.IO.Directory.Exists(actual)
                    ? actual
                    : System.IO.Path.GetDirectoryName(actual);

                if (!string.IsNullOrEmpty(carpeta) && System.IO.Directory.Exists(carpeta))
                {
                    dialogo.InitialDirectory = carpeta;
                }
            }
        }
        catch (Exception)
        {
            // Una ruta con caracteres imposibles no debe impedir abrir el diálogo.
        }

        if (dialogo.ShowDialog(this) == true)
        {
            CajetinRutaBox.Text = dialogo.FileName;
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
