namespace CadLink.Cad;

/// <summary>
/// Crea un <b>layout por plano</b> con su papel, su cajetín y sus atributos rellenados.
/// </summary>
/// <remarks>
/// <para>
/// Port de <c>GenerarSolapas</c>. Lo que decide —qué texto va en cada atributo, qué papel le toca a
/// cada hoja, cómo se llama el layout— lo hace <see cref="Solapas"/>, que no toca COM. Aquí queda
/// solo el diálogo con AutoCAD.
/// </para>
/// <para>
/// El modo es el <b>CENTRADO</b> de la macro, que es el que ella misma recomienda: se inserta el
/// bloque, se <b>mide</b> con <c>GetBoundingBox</c>, se escala sobre su propio centro y se lleva ese
/// centro al centro del área imprimible. No supone nada del punto base del bloque, así que funciona
/// esté donde esté. Los otros cuatro modos de la macro —MARCO, ESTIRAR, UNIFORME, DINÁMICO— no se
/// portaron: ver la nota de <see cref="Solapas.EscalaParaCaber"/> y el resumen del commit.
/// </para>
/// </remarks>
public sealed class SolapasDrawer
{
    private readonly dynamic _doc;

    /// <summary>Lo que pasó, plano por plano. Es lo que se le enseña al usuario al terminar.</summary>
    public List<string> Notas { get; } = new();

    /// <summary>El primer layout que se creó, para dejarlo a la vista al acabar.</summary>
    public string PrimerLayout { get; private set; } = string.Empty;

    public SolapasDrawer(dynamic documento) => _doc = documento;

    /// <summary>El nombre del bloque del cajetín que se va a usar.</summary>
    public string Cajetin { get; set; } = "CAJETIN";

    /// <summary>El dispositivo de ploteo. Del él sale la lista de papeles.</summary>
    public string Dispositivo { get; set; } = "DWG To PDF.pc3";

    /// <summary>Reemplaza el layout si ya existe, en lugar de crear uno con consecutivo.</summary>
    public bool Sobrescribir { get; set; } = true;

    /// <summary>Margen libre alrededor del cajetín al encajarlo, en mm.</summary>
    public double Margen { get; set; }

    // La lista de papeles depende SOLO del dispositivo, y pedirla consulta el driver: es lo más
    // lento de toda la corrida. Se pide una vez.
    private List<string>? _papeles;

    /// <summary>
    /// Busca en el dibujo el bloque que <b>más atributos del cajetín</b> tenga.
    /// </summary>
    /// <remarks>
    /// Port de <c>BuscarCajetinAuto</c>. Existe porque el nombre del bloque no se puede dar por
    /// sabido: cada despacho llama al suyo de una manera. Se piden <b>al menos tres</b> atributos
    /// conocidos para no confundirlo con un bloque de cotas o una marca de nivel, que también
    /// llevan atributos.
    /// </remarks>
    public string? BuscarCajetin(out int cuantos)
    {
        var mejor = 0;
        string? nombre = null;

        try
        {
            AcadConnection.Retry(() =>
            {
                foreach (dynamic blk in _doc.Blocks)
                {
                    string n = blk.Name;

                    if (n.StartsWith("*", StringComparison.Ordinal) || (bool)blk.IsLayout)
                    {
                        continue;
                    }

                    var k = 0;

                    foreach (dynamic ent in blk)
                    {
                        if ((string)ent.ObjectName == "AcDbAttributeDefinition"
                            && Solapas.EsTagConocido((string)ent.TagString))
                        {
                            k++;
                        }
                    }

                    if (k > mejor)
                    {
                        mejor = k;
                        nombre = n;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add("No se pudo revisar los bloques del dibujo: " + ex.Message);
        }

        cuantos = mejor;

        return mejor >= 3 ? nombre : null;
    }

    // ======================================================================
    //  TRAER EL CAJETÍN DE UN ARCHIVO
    // ======================================================================
    //
    //  ═════════════════════════════════════════════════════════════════════════════════════
    //  ESTO LO HACÍA LA MACRO Y LO QUITÉ POR ERROR.
    //
    //  El argumento fue que el usuario puede insertar su cajetín a mano una vez. Es falso en
    //  la práctica: nadie abre el programa para hacer un INSERT y aprenderse que la
    //  definición se queda cargada aunque borre la inserción. Y en un dibujo NUEVO hay que
    //  repetirlo, así que es un paso que se olvida, y cuando se olvida no sale ni una solapa.
    //
    //  CÓMO SE TRAE. AutoCAD acepta una RUTA DE ARCHIVO donde va el nombre del bloque:
    //  InsertBlock con «C:\...\CAJETIN.dwg» crea la definición «CAJETIN» y además la inserta.
    //  Se borra la inserción y la definición se queda. Es exactamente lo que el mensaje de
    //  error le pedía al usuario que hiciera a mano.
    //  ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Dónde buscar el archivo del cajetín. Lo primero que se prueba.</summary>
    public string RutaDelCajetin { get; set; } = string.Empty;

    /// <summary>Carpetas de siempre, después de la configurada y la del dibujo.</summary>
    public List<string> CarpetasExtra { get; } = new();

    /// <summary>Las rutas que se llegaron a mirar, para poder decirlo si no se encuentra.</summary>
    public List<string> RutasMiradas { get; } = new();

    /// <summary>
    /// El archivo del cajetín <b>es</b> el dibujo abierto en AutoCAD.
    /// </summary>
    /// <remarks>
    /// No es un error del usuario, es un malentendido del flujo: tiene abierto su archivo del
    /// cajetín en lugar del plano donde quiere las solapas. Quien llama lo usa para decírselo con
    /// esas palabras en lugar de un «no lo encontré».
    /// </remarks>
    public bool EsAutorreferencia { get; private set; }

    /// <summary>
    /// Busca el archivo del cajetín y trae su definición de bloque al dibujo.
    /// </summary>
    /// <returns>El nombre del bloque que quedó cargado, o <c>null</c>.</returns>
    public string? ImportarCajetin(out string deDonde)
    {
        deDonde = string.Empty;

        RutasMiradas.Clear();

        var carpetaDelDibujo = CarpetaDelDibujo();

        foreach (var ruta in Solapas.RutasAProbar(RutaDelCajetin, carpetaDelDibujo, CarpetasExtra))
        {
            RutasMiradas.Add(ruta);

            foreach (var archivo in ArchivosCandidatos(ruta))
            {
                // EL DIBUJO ABIERTO NO SE PUEDE INSERTAR EN SÍ MISMO. AutoCAD se niega, pero el
                // mensaje que da no dice eso, así que se detecta antes y se salta.
                if (Solapas.MismaRuta(archivo, RutaDelDibujo()))
                {
                    EsAutorreferencia = true;

                    Notas.Add(
                        $"«{archivo}» es el dibujo que está abierto, así que no se puede insertar " +
                        "en sí mismo. Se buscó en otro sitio.");

                    continue;
                }

                // La ruta puede no coincidir aunque sea el mismo archivo —OneDrive, rutas cortas
                // 8.3, unidades de red— y no se aprieta más: si de verdad lo fuera, AutoCAD lo dice
                // al intentarlo. Comparar solo el NOMBRE del archivo daría falsos positivos con un
                // plano que se llame igual en otra carpeta, y el castigo de un falso positivo aquí
                // es «no encuentro el cajetín», que es el problema que se está arreglando.

                var nombre = TraerBloqueDeArchivo(archivo);

                if (nombre is null)
                {
                    continue;
                }

                deDonde = archivo;

                return nombre;
            }
        }

        return null;
    }

    /// <summary>
    /// Los archivos a probar de una ruta: si es archivo, ella; si es carpeta, lo que parezca.
    /// </summary>
    /// <remarks>
    /// Las dos formas porque las dos se usan: hay quien apunta al archivo exacto y hay quien apunta
    /// a la carpeta donde guarda sus formatos. Obligar a una sola sería obligar a la mitad a
    /// cambiar de costumbre.
    /// </remarks>
    private List<string> ArchivosCandidatos(string ruta)
    {
        var salida = new List<string>();

        try
        {
            if (System.IO.File.Exists(ruta))
            {
                if (Solapas.EsArchivoDeDibujo(ruta))
                {
                    salida.Add(ruta);
                }

                return salida;
            }

            if (!System.IO.Directory.Exists(ruta))
            {
                return salida;
            }

            // LOS NOMBRES EXACTOS PRIMERO, y solo después los que se parecen: en una carpeta con
            // «CAJETIN.dwg» y «CAJETIN viejo 2019.dwg», el bueno es el primero.
            foreach (var probable in Solapas.NombresDeArchivoProbables)
            {
                foreach (var ext in Solapas.ExtensionesDeDibujo)
                {
                    var exacto = System.IO.Path.Combine(ruta, probable + ext);

                    if (System.IO.File.Exists(exacto))
                    {
                        salida.Add(exacto);
                    }
                }
            }

            foreach (var a in Solapas.CajetinesDeLaCarpeta(
                         System.IO.Directory.GetFiles(ruta)))
            {
                if (!salida.Contains(a, StringComparer.OrdinalIgnoreCase))
                {
                    salida.Add(a);
                }
            }
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo mirar en «{ruta}»: {ex.Message}");
        }

        return salida;
    }

    /// <summary>
    /// Inserta el archivo como bloque, borra la inserción y deja la <b>definición</b> cargada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Se apagan INSUNITS y LIGHTINGUNITS mientras dura.</b> Al insertar un dibujo como bloque,
    /// AutoCAD lo reescala por la relación entre las unidades del archivo y las del dibujo: un
    /// cajetín dibujado en milímetros insertado en un plano en metros sale mil veces más chico, y no
    /// avisa. Con INSUNITS en 0 —sin unidades— no hay conversión y el cajetín entra a su tamaño.
    /// LIGHTINGUNITS se apaga con él porque cambiar INSUNITS con iluminación fotométrica saca un
    /// aviso modal, y un aviso modal en medio de una corrida de veinte planos la deja colgada.
    /// </para>
    /// <para>
    /// Los dos se <b>restauran siempre</b>, incluso si algo falla en medio: son variables del dibujo
    /// del usuario y dejárselas cambiadas afecta a todo lo que inserte después.
    /// </para>
    /// <para>
    /// <b>Y se prueban dos formas de la ruta.</b> <c>InsertBlock</c> por ActiveX falla en algunas
    /// versiones con las barras invertidas de Windows y funciona con barras normales. Es un defecto
    /// conocido del enlace tardío y no cuesta nada cubrirlo: la alternativa es que el cajetín «no se
    /// encuentre» estando ahí.
    /// </para>
    /// </remarks>
    private string? TraerBloqueDeArchivo(string archivo)
    {
        var esperado = Solapas.NombreDeBloqueDeArchivo(archivo);

        if (esperado.Length == 0)
        {
            return null;
        }

        // LOS BLOQUES QUE YA HABÍA, para poder reconocer el que entre. Ver DescubrirBloqueNuevo:
        // el nombre no se supone, se descubre.
        var antes = NombresDeBloque();

        var insUnits = Variable("INSUNITS");
        var lighting = Variable("LIGHTINGUNITS");

        try
        {
            PonerVariable("LIGHTINGUNITS", 0);
            PonerVariable("INSUNITS", 0);

            // ═══════════════════════════════════════════════════════════════════════════════
            // DOS INTENTOS, Y EL SEGUNDO ES EL QUE HACE FALTA EN EL CASO NORMAL.
            //
            // «Self reference» de AutoCAD NO significa que el archivo sea el dibujo abierto —eso
            // creí primero y el usuario aclaró que no—. Significa que el archivo CONTIENE UN
            // BLOQUE CON SU MISMO NOMBRE: SOLAPA.dwg trae dentro el bloque SOLAPA, así que crear
            // un bloque «SOLAPA» a partir de ese archivo daría uno que se refiere a sí mismo, y
            // AutoCAD se niega. Y eso es lo NORMAL en un cajetín bien hecho, o sea que el caso
            // común era justo el que fallaba.
            //
            // La salida es no pedirle ese nombre: se copia el archivo a uno temporal con un
            // nombre que no choca con nada y se inserta ESE. AutoCAD crea el envoltorio con el
            // nombre del temporal y, de paso, mete en la tabla de bloques del dibujo TODOS los
            // del archivo, incluido el SOLAPA de verdad con sus atributos.
            // ═══════════════════════════════════════════════════════════════════════════════
            foreach (var (forma, temporal) in FormasDeInsertar(archivo))
            {
                object? insercion = null;

                try
                {
                    AcadConnection.Retry(() =>
                    {
                        dynamic ms = _doc.ModelSpace;

                        insercion = (object?)ms.InsertBlock(
                            new[] { 0.0, 0.0, 0.0 }, forma, 1.0, 1.0, 1.0, 0.0);
                    });
                }
                catch (Exception ex)
                {
                    var suMismoNombre = ex.Message.IndexOf(
                        "self reference", StringComparison.OrdinalIgnoreCase) >= 0;

                    Notas.Add(
                        suMismoNombre
                            ? $"«{System.IO.Path.GetFileName(forma)}» trae dentro un bloque con su " +
                              "mismo nombre, así que AutoCAD no lo deja insertar tal cual («Self " +
                              "reference»). Se prueba con una copia temporal."
                            : $"AutoCAD no pudo insertar «{forma}»: {ex.Message}");

                    BorrarTemporal(temporal);

                    continue;
                }

                // La inserción era solo el vehículo: lo que interesa es la DEFINICIÓN, que se queda
                // en la tabla de bloques del dibujo aunque se borre lo insertado.
                if (insercion is not null)
                {
                    try
                    {
                        AcadConnection.Retry(() => ((dynamic)insercion).Delete());
                    }
                    catch (Exception)
                    {
                        Notas.Add(
                            "El cajetín se trajo, pero no se pudo borrar la inserción que quedó en " +
                            "el espacio modelo. Bórrala a mano.");
                    }
                }

                BorrarTemporal(temporal);

                // ═══════════════════════════════════════════════════════════════════════════════
                // EL ARCHIVO TRAE TODA SU TABLA DE BLOQUES, no solo su espacio modelo. Así que el
                // cajetín de verdad —el bloque de dentro del archivo— también acaba de entrar. Se
                // prefiere el que se llame como un cajetín Y TENGA ATRIBUTOS: después de importar
                // entran varios, y el que lleva el nombre del archivo suele ser el envoltorio.
                // ═══════════════════════════════════════════════════════════════════════════════
                var nombre = BuscarCajetinConAtributos()
                             ?? DescubrirBloqueNuevo(antes, esperado);

                if (nombre is null)
                {
                    Notas.Add(
                        $"«{forma}» se insertó sin dar error, pero no apareció ninguna definición " +
                        "de bloque nueva en el dibujo.");

                    continue;
                }

                Notas.Add($"Cajetín «{nombre}» traído de: {archivo}");

                // El envoltorio con el nombre del temporal ya no hace falta: solo envuelve al
                // bloque de verdad, y dejarlo llena de basura la lista de bloques del usuario.
                if (temporal is not null)
                {
                    BorrarBloque(Solapas.NombreDeBloqueDeArchivo(temporal), nombre);
                }

                RevisarAtributos(nombre);

                return nombre;
            }

            return null;
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo traer el cajetín de «{archivo}»: {ex.Message}");

            return null;
        }
        finally
        {
            if (insUnits is not null) { PonerVariable("INSUNITS", insUnits.Value); }
            if (lighting is not null) { PonerVariable("LIGHTINGUNITS", lighting.Value); }
        }
    }

    /// <summary>
    /// Las formas de insertar el archivo: <b>tal cual</b> y, si no, una <b>copia temporal</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La copia temporal es la que resuelve el «Self reference»: un cajetín bien hecho está blocado
    /// dentro de su archivo y con el mismo nombre, y entonces AutoCAD no puede crear un bloque de ese
    /// nombre a partir de ese archivo. Con otro nombre de archivo el problema desaparece, y el bloque
    /// de verdad entra igual en la tabla de bloques del dibujo.
    /// </para>
    /// <para>
    /// El nombre del temporal lleva la hora en <b>ticks</b>, así que no choca ni con un bloque del
    /// dibujo ni con otra corrida. Y se borra siempre, haya funcionado o no.
    /// </para>
    /// <para>
    /// <b>Probé una tercera forma y la quité:</b> la ruta con barras normales, por un defecto conocido
    /// de ActiveX. AutoCAD contestó <c>Key not found</c> las dos veces, o sea que con <c>/</c> no la
    /// trata como archivo sino como nombre de bloque. Solo alargaba el informe de errores.
    /// </para>
    /// </remarks>
    private IEnumerable<(string Ruta, string? Temporal)> FormasDeInsertar(string archivo)
    {
        yield return (archivo, null);

        string? copia = null;

        try
        {
            var nombre = "CADLINK_CAJETIN_" +
                         DateTime.Now.Ticks.ToString(
                             System.Globalization.CultureInfo.InvariantCulture);

            var destino = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                nombre + System.IO.Path.GetExtension(archivo));

            System.IO.File.Copy(archivo, destino, overwrite: true);

            copia = destino;
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo hacer una copia temporal de «{archivo}»: {ex.Message}");
        }

        if (copia is not null)
        {
            yield return (copia, copia);
        }
    }

    private void BorrarTemporal(string? temporal)
    {
        if (temporal is null)
        {
            return;
        }

        try
        {
            System.IO.File.Delete(temporal);
        }
        catch (Exception)
        {
            // Un archivo de sobra en la carpeta temporal de Windows no estorba: el sistema la
            // limpia. Fallar aquí no puede invalidar un cajetín que ya entró.
        }
    }

    /// <summary>Borra una definición de bloque, si no es la que se va a usar.</summary>
    private void BorrarBloque(string nombre, string noBorrarEste)
    {
        if (nombre.Length == 0
            || string.Equals(nombre, noBorrarEste, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                foreach (dynamic blk in _doc.Blocks)
                {
                    if (string.Equals((string)blk.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        blk.Delete();
                        break;
                    }
                }
            });
        }
        catch (Exception)
        {
            Notas.Add(
                $"Quedó en el dibujo un bloque auxiliar llamado «{nombre}», que solo envuelve al " +
                "cajetín. Se puede quitar con PURGE.");
        }
    }

    /// <summary>
    /// El bloque que se llama como un cajetín <b>y tiene atributos</b> de solapa.
    /// </summary>
    /// <remarks>
    /// Se exige lo segundo: después de importar un archivo entran varios bloques, y el que lleva el
    /// nombre del archivo suele ser el envoltorio. El que sirve es el que se puede llenar.
    /// </remarks>
    private string? BuscarCajetinConAtributos()
    {
        foreach (var c in Solapas.BloquesQueParecenCajetin(NombresDeBloque()))
        {
            if (CuantosAtributos(c) > 0)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// Qué definición de bloque <b>apareció</b> después de insertar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El nombre se descubre, no se supone.</b> La primera versión daba por hecho que insertar
    /// <c>SOLAPA.dwg</c> deja un bloque llamado <c>SOLAPA</c>, y cuando no era exactamente así
    /// —porque el archivo trae un nombre de bloque propio, porque AutoCAD lo renombró para no
    /// chocar, o por cualquier otra razón— el programa decía «no encontré el cajetín» con el cajetín
    /// ya cargado en el dibujo. Es lo que le pasó al usuario.
    /// </para>
    /// <para>
    /// Se prefiere el nombre esperado si está, y si no, el bloque nuevo que <b>tenga atributos de
    /// solapa</b>: entre varios recién llegados, ese es el cajetín y los demás son piezas suyas.
    /// </para>
    /// </remarks>
    private string? DescubrirBloqueNuevo(HashSet<string> antes, string esperado)
    {
        if (ExisteBloque(esperado))
        {
            return esperado;
        }

        var nuevos = new List<string>();

        foreach (var n in NombresDeBloque())
        {
            // Los que empiezan por «*» son los anónimos de AutoCAD: los layouts, los hatches y los
            // bloques dinámicos. Ninguno es un cajetín.
            if (!n.StartsWith("*", StringComparison.Ordinal) && !antes.Contains(n))
            {
                nuevos.Add(n);
            }
        }

        if (nuevos.Count == 0)
        {
            return null;
        }

        if (nuevos.Count == 1)
        {
            return nuevos[0];
        }

        string? mejor = null;
        var masAtributos = 0;

        foreach (var n in nuevos)
        {
            var k = CuantosAtributos(n);

            if (k > masAtributos)
            {
                masAtributos = k;
                mejor = n;
            }
        }

        return mejor ?? nuevos[0];
    }

    /// <summary>Los nombres de bloque que tiene el dibujo ahora mismo.</summary>
    private HashSet<string> NombresDeBloque()
    {
        var salida = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            AcadConnection.Retry(() =>
            {
                salida.Clear();

                foreach (dynamic blk in _doc.Blocks)
                {
                    salida.Add((string)blk.Name);
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add("No se pudo leer la tabla de bloques del dibujo: " + ex.Message);
        }

        return salida;
    }

    /// <summary>Cuántos atributos <b>de solapa</b> tiene la definición de un bloque.</summary>
    /// <remarks>
    /// Es la medida de si ese bloque sirve: un cajetín cuyos rótulos son texto normal en lugar de
    /// atributos da cero, y con cero salen veinte solapas en blanco.
    /// </remarks>
    public int CuantosAtributos(string nombre)
    {
        var k = 0;

        try
        {
            AcadConnection.Retry(() =>
            {
                k = 0;

                foreach (dynamic blk in _doc.Blocks)
                {
                    if (!string.Equals((string)blk.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (dynamic ent in blk)
                    {
                        if ((string)ent.ObjectName == "AcDbAttributeDefinition"
                            && Solapas.EsTagConocido((string)ent.TagString))
                        {
                            k++;
                        }
                    }

                    break;
                }
            });
        }
        catch (Exception)
        {
            // Sin la cuenta no se puede avisar del cajetin sin atributos, pero tampoco impide nada.
        }

        return k;
    }

    private int? Variable(string nombre)
    {
        try
        {
            return AcadConnection.Retry<int?>(() => (int)_doc.GetVariable(nombre));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void PonerVariable(string nombre, int valor)
    {
        try
        {
            AcadConnection.Retry(() => _doc.SetVariable(nombre, valor));
        }
        catch (Exception)
        {
            // Si la variable no existe en esta versión, no pasa nada: lo único que se pierde es la
            // protección contra el reescalado, y eso se ve en el dibujo.
        }
    }

    /// <summary>La ruta completa del dibujo abierto, o vacío si nunca se ha guardado.</summary>
    private string RutaDelDibujo()
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                string carpeta = _doc.Path;
                string nombre = _doc.Name;

                return carpeta.Length == 0
                    ? string.Empty
                    : System.IO.Path.Combine(carpeta, nombre);
            });
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>La carpeta del dibujo abierto. Ahí es donde de verdad suele estar el cajetín.</summary>
    private string CarpetaDelDibujo()
    {
        try
        {
            return AcadConnection.Retry(() => (string)_doc.Path);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// El bloque del dibujo que se <b>llama</b> como un cajetín, si hay alguno.
    /// </summary>
    /// <remarks>
    /// Va antes de buscar por atributos, y hace falta: los atributos del cajetín de cada despacho se
    /// llaman como quiera su autor —<c>UBICACIÓN</c> con acento, <c>PROY_1</c>, <c>OBRA</c>— y
    /// ninguno de esos coincide con los que este programa conoce. Con un bloque llamado
    /// <c>SOLAPA</c> ya cargado, buscar solo por atributos decía que no había cajetín.
    /// </remarks>
    public string? BuscarCajetinPorNombre()
    {
        var candidatos = Solapas.BloquesQueParecenCajetin(NombresDeBloque());

        if (candidatos.Count == 0)
        {
            return null;
        }

        // Entre los que se llaman como un cajetín, el que MÁS atributos conocidos tenga: si hay un
        // «SOLAPA» y un «SOLAPA VIEJA», el que sirve es el que este programa puede llenar.
        string? mejor = null;
        var masAtributos = -1;

        foreach (var c in candidatos)
        {
            var k = CuantosAtributos(c);

            if (k > masAtributos)
            {
                masAtributos = k;
                mejor = c;
            }
        }

        return mejor;
    }

    /// <summary>
    /// Los <b>tags de verdad</b> de un bloque, para poder decir por qué no se puede llenar.
    /// </summary>
    /// <remarks>
    /// Es el diagnóstico que faltaba. Un cajetín con atributos llamados <c>OBRA</c> y
    /// <c>UBICACIÓN</c> en lugar de <c>PROYECTO</c> y <c>UBICACION</c> se queda en blanco, y sin ver
    /// la lista real no hay manera de adivinarlo: el programa dice «cero atributos» y el usuario
    /// está mirando un cajetín lleno de ellos.
    /// </remarks>
    public List<string> TagsDelBloque(string nombre)
    {
        var salida = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                salida.Clear();

                foreach (dynamic blk in _doc.Blocks)
                {
                    if (!string.Equals((string)blk.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (dynamic ent in blk)
                    {
                        if ((string)ent.ObjectName == "AcDbAttributeDefinition")
                        {
                            salida.Add((string)ent.TagString);
                        }
                    }

                    break;
                }
            });
        }
        catch (Exception)
        {
            // Sin la lista, el aviso sale sin ella. No impide nada.
        }

        return salida;
    }

    /// <summary>
    /// Deja dicho en las notas si un cajetín se puede llenar, y si no, por qué.
    /// </summary>
    public void RevisarAtributos(string nombre)
    {
        var conocidos = CuantosAtributos(nombre);

        if (conocidos > 0)
        {
            Notas.Add($"Cajetín «{nombre}»: {conocidos} atributos de solapa que se van a llenar.");

            return;
        }

        var todos = TagsDelBloque(nombre);

        if (todos.Count == 0)
        {
            Notas.Add(
                $"AVISO: el bloque «{nombre}» no tiene NINGÚN atributo, así que el cajetín va a " +
                "salir en blanco. Sus rótulos son texto normal y tienen que ser ATRIBUTOS: " +
                "conviértelos con ATTDEF o con TXT2ATT.");

            return;
        }

        // TIENE ATRIBUTOS, PERO CON OTROS NOMBRES. Es el caso que no se podía diagnosticar: el
        // usuario ve un cajetín lleno de atributos y el programa dice que no encuentra ninguno.
        Notas.Add(
            $"AVISO: el bloque «{nombre}» tiene {todos.Count} atributos, pero ninguno se llama como " +
            "los que esta solapa llena, así que va a salir en blanco." +
            "\n    Los que tiene:    " + string.Join(", ", todos) +
            "\n    Los que necesita: " + string.Join(", ", Solapas.TagsConocidos) +
            "\n    Renombra los tags del bloque con BEDIT y ATTDEF para que coincidan.");
    }

    // ======================================================================
    //  EL CAJETÍN SUELTO EN EL ESPACIO MODELO
    // ======================================================================
    //
    //  Para cuando el cajetín está en el dibujo abierto pero SIN BLOCAR: el recuadro dibujado y
    //  sus atributos sueltos en el espacio modelo. Ahí no hay ningún bloque que buscar.

    /// <summary>
    /// Cuántas definiciones de atributo <b>de solapa</b> hay sueltas en el espacio modelo.
    /// </summary>
    public int AtributosSueltos(out List<string> todosLosTags)
    {
        var conocidos = 0;
        var tags = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                conocidos = 0;
                tags.Clear();

                foreach (dynamic ent in _doc.ModelSpace)
                {
                    if ((string)ent.ObjectName != "AcDbAttributeDefinition")
                    {
                        continue;
                    }

                    string tag = ent.TagString;

                    tags.Add(tag);

                    if (Solapas.EsTagConocido(tag))
                    {
                        conocidos++;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add("No se pudo revisar el espacio modelo: " + ex.Message);
        }

        todosLosTags = tags;

        return conocidos;
    }

    /// <summary>
    /// Forma un bloque con <b>todo el espacio modelo</b>, que es donde vive un cajetín sin blocar.
    /// </summary>
    /// <returns>El nombre del bloque creado, o <c>null</c>.</returns>
    /// <remarks>
    /// <para>
    /// <b>No se borra nada.</b> Se copia a la definición del bloque y los originales se quedan
    /// exactamente donde estaban. Es el dibujo del usuario: dejarle el espacio modelo vacío para
    /// ahorrar una copia sería cambiarle su archivo por un atajo del programa.
    /// </para>
    /// <para>
    /// El punto base es el origen y no importa: el cajetín se mide con <c>GetBoundingBox</c> y se
    /// centra en la hoja, así que da igual dónde tenga su base. Ver <c>EncajarYCentrar</c>.
    /// </para>
    /// </remarks>
    public string? CrearCajetinDelEspacioModelo(string nombreDeseado)
    {
        var nombre = Solapas.NombreLibre(
            Solapas.Limpiar(nombreDeseado), NombresDeBloque(), sobrescribir: false);

        var objetos = new List<object>();

        try
        {
            AcadConnection.Retry(() =>
            {
                objetos.Clear();

                foreach (dynamic ent in _doc.ModelSpace)
                {
                    objetos.Add((object)ent);
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add("No se pudo recorrer el espacio modelo: " + ex.Message);

            return null;
        }

        if (objetos.Count == 0)
        {
            Notas.Add("El espacio modelo está vacío: no hay con qué formar el cajetín.");

            return null;
        }

        dynamic? bloque = null;

        try
        {
            // El Add va FUERA de un reintento: repetirlo después de haber creado el bloque falla por
            // nombre duplicado, y ese error no es de los que se reintentan. Es la misma nota que
            // lleva Bloquear en el dibujante de la placa base.
            bloque = _doc.Blocks.Add(new[] { 0.0, 0.0, 0.0 }, nombre);
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo crear la definición del bloque «{nombre}»: {ex.Message}");

            return null;
        }

        // CopyObjects va por AcadArreglos, que es quien resuelve el tipo del SAFEARRAY: con un
        // object[] de .NET, AutoCAD contesta «Invalid object array». Ya estaba resuelto en este
        // proyecto y se reutiliza.
        var copiado = AcadArreglos.Llamar(
            $"CopyObjects al bloque '{nombre}'", objetos,
            arr => { _doc.CopyObjects(arr, bloque); },
            (que, ex) => Notas.Add($"{que}: {ex.Message}"),
            n => Notas.Add(n));

        if (!copiado)
        {
            try
            {
                bloque.Delete();
            }
            catch (Exception)
            {
                Notas.Add(
                    $"Quedó una definición de bloque vacía llamada «{nombre}». Bórrala con PURGE.");
            }

            return null;
        }

        Notas.Add(
            $"Cajetín «{nombre}» formado con las {objetos.Count} entidades del espacio modelo. " +
            "Los originales no se tocaron.");

        return nombre;
    }

    /// <summary>¿El dibujo ya tiene la definición de este bloque?</summary>
    public bool ExisteBloque(string nombre)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                foreach (dynamic blk in _doc.Blocks)
                {
                    if (string.Equals((string)blk.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// El layout de un plano, con su papel, su cajetín y sus atributos.
    /// </summary>
    /// <returns>El nombre del layout, o cadena vacía si no se pudo crear.</returns>
    public string Dibujar(SolapaCad s)
    {
        if (s.Falta.Count > 0)
        {
            Notas.Add($"{Etiqueta(s)}: falta {string.Join("; ", s.Falta)}. No se generó.");

            return string.Empty;
        }

        var nombre = Solapas.NombreLibre(Solapas.NombreDeLayout(s), NombresDeLayout(), Sobrescribir);

        // OBJECT Y NO DYNAMIC, y esto no es estilo. Un argumento dynamic vuelve DINÁMICA LA
        // LLAMADA ENTERA, así que el resultado deja de ser el tipo declarado y pasa a ser dynamic:
        // AreaImprimible ya no devolvía su tupla y «var (x0, y0, x1, y1) = area» no compilaba
        // —CS8133, no se pueden deconstruir los objetos dinámicos—. El dynamic se queda DENTRO de
        // cada método, que es donde hace falta, y las fronteras van tipadas. Es lo que ya hacían
        // los otros dibujantes de este proyecto.
        object? lay = CrearLayout(nombre);

        if (lay is null)
        {
            return string.Empty;
        }

        if (PrimerLayout.Length == 0)
        {
            PrimerLayout = nombre;
        }

        var (w, h) = Solapas.HojaOrientada(s);

        // ---------- EL PAPEL ----------
        var papel = AsignarPapel(lay, s, w, h);

        // ---------- EL ÁREA DONDE SE DIBUJA ----------
        // El área imprimible real, que es el recuadro de rayitas que se ve en pantalla. Si no se
        // puede leer, la medida teórica desde el origen.
        var area = AreaImprimible(lay) ?? (0.0, 0.0, w, h);

        var (x0, y0, x1, y1) = area;

        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;

        // ---------- EL CAJETÍN ----------
        // Se dibuja en lay.Block —el espacio papel de ESTE layout— y no en doc.PaperSpace: el
        // segundo depende de cuál esté activo en AutoCAD, así que el cajetín podía acabar en el
        // layout anterior.
        object? br = Insertar(lay, cx, cy);

        var nAtt = 0;
        var escala = 1.0;

        if (br is not null)
        {
            escala = EncajarYCentrar(br, cx, cy, x1 - x0, y1 - y0);
            nAtt = RellenarAtributos(br, s);
        }

        Notas.Add(
            $"{nombre}: {s.Tamano} {(s.Horizontal ? "horizontal" : "vertical")} " +
            $"({w:0}x{h:0} mm), papel {(papel.Length == 0 ? "NO ASIGNADO" : papel)}, " +
            $"{nAtt} atributos" +
            (Math.Abs(escala - 1) > 1e-6 ? $", escala {escala:0.0000}" : string.Empty));

        return nombre;
    }

    // ======================================================================
    //  EL PAPEL
    // ======================================================================

    /// <summary>
    /// Le pone el papel al layout: primero por <b>configuración de página</b>, y si no, buscándolo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La configuración de página va primero porque ahí el usuario controla dispositivo, tabla de
    /// plumillas, márgenes y escala <b>desde AutoCAD</b>, que es donde sabe hacerlo. Pero no se
    /// confía a ciegas: se comprueba que de verdad traiga el papel que se pidió. Una configuración
    /// creada antes con el papel equivocado es peor que ninguna, porque parece correcta.
    /// </para>
    /// <para>
    /// Y si el pliego exacto no existe en el dispositivo, se avisa. Es lo que la propia macro llama
    /// la causa número uno de que la orientación salga mal: AutoCAD no da error, deja Carta vertical
    /// y el plano entero sale descuadrado.
    /// </para>
    /// </remarks>
    private string AsignarPapel(object lay, SolapaCad s, double w, double h)
    {
        var porConfig = AplicarConfigDePagina(lay, s);

        if (porConfig.Length > 0 && PapelDelLayoutCoincide(lay, w, h))
        {
            return "[config] " + porConfig;
        }

        var elegido = Solapas.BuscarPapel(Papeles(lay), s);

        if (elegido is null)
        {
            Notas.Add(
                $"{Etiqueta(s)}: el tamaño «{s.Tamano}» ({w:0}x{h:0} mm) no existe en " +
                $"{Dispositivo}, así que AutoCAD va a dejar el papel por omisión y la hoja se va " +
                "a ver descuadrada. Créalo como tamaño personalizado en PLOTTERMANAGER.");

            return string.Empty;
        }

        var p = elegido.Value;

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                l.CanonicalMediaName = p.Nombre;
                l.PaperUnits = 1;                // acMillimeters
                l.PlotType = 1;                  // acExtents
                l.UseStandardScale = false;
                l.SetCustomScale(1, 1);
                l.CenterPlot = true;

                if (p.Rotacion >= 0)
                {
                    l.PlotRotation = p.Rotacion;
                }

                l.RefreshPlotDeviceInfo();
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"{Etiqueta(s)}: no se pudo asignar el papel «{p.Nombre}»: {ex.Message}");

            return string.Empty;
        }

        if (!p.Cabe)
        {
            Notas.Add(
                $"{Etiqueta(s)}: el tamaño «{s.Tamano}» no existe en {Dispositivo}. Se usó el " +
                $"pliego más chico donde cabe, «{Solapas.NombreCortoDelPapel(p.Nombre)}», así que " +
                "el plano va a quedar con más margen del previsto.");
        }

        return Solapas.NombreCortoDelPapel(p.Nombre);
    }

    private string AplicarConfigDePagina(object lay, SolapaCad s)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                foreach (dynamic cfg in _doc.PlotConfigurations)
                {
                    if (!Solapas.ConfigPaginaSirve(s, (string)cfg.Name))
                    {
                        continue;
                    }

                    l.CopyFrom(cfg);

                    return (string)cfg.Name;
                }

                return string.Empty;
            });
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private bool PapelDelLayoutCoincide(object lay, double w, double h)
    {
        var papel = MedidaDelPapel(lay);

        return papel is not null && Solapas.PapelCoincide(papel.Value.Ancho, papel.Value.Alto, w, h);
    }

    /// <summary>La medida del papel del layout, en mm.</summary>
    private (double Ancho, double Alto)? MedidaDelPapel(object lay)
    {
        try
        {
            return AcadConnection.Retry<(double, double)?>(() =>
            {
                var r = PorReferencia(lay, "GetPaperSize");

                if (r is null)
                {
                    return null;
                }

                var pw = Numero(r[0]) ?? 0;
                var ph = Numero(r[1]) ?? 0;

                if (pw <= 0 || ph <= 0)
                {
                    return null;
                }

                // 0 = pulgadas, 1 = mm. Sin convertir, una plantilla en pulgadas hace que todas las
                // comparaciones de medida fallen y ningún papel «coincide».
                var k = (int)((dynamic)lay).PaperUnits == 0 ? 25.4 : 1.0;

                return (pw * k, ph * k);
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ======================================================================
    //  LAS LLAMADAS COM QUE DEVUELVEN POR REFERENCIA
    // ======================================================================
    //
    //  GetPaperSize y GetPaperMargins entregan sus resultados en parámetros de SALIDA, y por
    //  enlace tardío eso no se puede pedir con «dynamic» y «out»: hay que llamar por
    //  reflexión y marcar los dos argumentos como por referencia. Es exactamente lo que ya
    //  hacía este proyecto para GetBoundingBox —ver Caja, más abajo—, así que aquí se usa la
    //  misma vía en lugar de abrir una segunda.

    /// <summary>Invoca un método COM con <b>dos argumentos de salida</b> y devuelve los dos.</summary>
    private static object?[]? PorReferencia(object objeto, string metodo)
    {
        try
        {
            var args = new object?[] { null, null };

            objeto.GetType().InvokeMember(
                metodo,
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: objeto, args: args,
                modifiers: new[]
                {
                    new System.Reflection.ParameterModifier(2) { [0] = true, [1] = true },
                },
                culture: null, namedParameters: null);

            return args;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Un número que llegó como variante de COM, o <c>null</c> si no lo era.</summary>
    private static double? Numero(object? v)
    {
        try
        {
            return v is null
                ? null
                : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<string> Papeles(object lay)
    {
        if (_papeles is not null)
        {
            return _papeles;
        }

        var salida = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = lay;

                l.ConfigName = Dispositivo;
                l.RefreshPlotDeviceInfo();

                salida.Clear();

                // DOS FORMAS, y la macro las prueba igual: según la versión de AutoCAD,
                // GetCanonicalMediaNames devuelve la lista o la entrega en un parámetro de salida.
                // Con una sola, en la mitad de las versiones no hay lista y ningún papel se
                // encuentra: justo el fallo que deja el plano en Carta vertical sin avisar.
                object? nombres = null;

                try
                {
                    nombres = l.GetCanonicalMediaNames();
                }
                catch (Exception)
                {
                    nombres = null;
                }

                var sirve = nombres is System.Collections.IEnumerable and not string;

                if (!sirve)
                {
                    var args = new object?[] { null };

                    try
                    {
                        lay.GetType().InvokeMember(
                            "GetCanonicalMediaNames",
                            System.Reflection.BindingFlags.InvokeMethod,
                            binder: null, target: lay, args: args,
                            modifiers: new[]
                            {
                                new System.Reflection.ParameterModifier(1) { [0] = true },
                            },
                            culture: null, namedParameters: null);

                        nombres = args[0];
                    }
                    catch (Exception)
                    {
                        // Se queda con lo que trajera la primera forma.
                    }
                }

                if (nombres is System.Collections.IEnumerable lista and not string)
                {
                    foreach (var n in lista)
                    {
                        var t = Convert.ToString(
                            n, System.Globalization.CultureInfo.InvariantCulture);

                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            salida.Add(t);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add(
                $"No se pudo leer la lista de papeles de {Dispositivo}: {ex.Message}. " +
                "Revisa que ese dispositivo exista en PLOTTERMANAGER.");
        }

        _papeles = salida;

        return salida;
    }

    /// <summary>
    /// El área <b>imprimible</b> del layout: el recuadro de rayitas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se calcula del tamaño de papel menos los márgenes del dispositivo, y no de
    /// <c>LIMMIN</c>/<c>LIMMAX</c>. La macro usaba las dos fuentes y validaba una contra la otra, y
    /// documenta por qué: un layout <b>recién creado</b> todavía no tiene sus límites calculados y
    /// <c>LIMMIN</c> devuelve los del layout anterior, así que la solapa se centraba sobre el área de
    /// otra hoja.
    /// </para>
    /// <para>
    /// Aquí se usa solo la fuente fiable. Da la medida pero no la posición, así que el área se toma
    /// desde el origen: para <b>centrar</b> el cajetín eso es exactamente igual de bueno, y no
    /// depende de activar el layout ni de regenerar el dibujo.
    /// </para>
    /// </remarks>
    private (double X0, double Y0, double X1, double Y1)? AreaImprimible(object lay)
    {
        try
        {
            return AcadConnection.Retry<(double, double, double, double)?>(() =>
            {
                var papel = PorReferencia(lay, "GetPaperSize");

                if (papel is null)
                {
                    return null;
                }

                var pw = Numero(papel[0]) ?? 0;
                var ph = Numero(papel[1]) ?? 0;

                if (pw <= 0 || ph <= 0)
                {
                    return null;
                }

                // GetPaperMargins entrega DOS PUNTOS por referencia: el de abajo-izquierda y el de
                // arriba-derecha. Si no se pueden leer, se usa el papel entero: es un área un pelo
                // mayor, y centrar en ella deja el cajetín igual de centrado.
                var m = ComoNumeros(PorReferencia(lay, "GetPaperMargins"));

                var mIzq = m.Count > 0 ? m[0] : 0;
                var mInf = m.Count > 1 ? m[1] : 0;
                var mDer = m.Count > 3 ? m[3] : 0;
                var mSup = m.Count > 4 ? m[4] : 0;

                dynamic l = lay;

                var k = (int)l.PaperUnits == 0 ? 25.4 : 1.0;

                var w = (pw - (mIzq + mDer)) * k;
                var h = (ph - (mInf + mSup)) * k;

                // La hoja girada intercambia los lados del área imprimible.
                var rot = (int)l.PlotRotation;

                if (rot == 1 || rot == 3)
                {
                    (w, h) = (h, w);
                }

                return w > 1 && h > 1 ? (0.0, 0.0, w, h) : null;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Aplana en una lista de números lo que devolvió una llamada por referencia.
    /// </summary>
    /// <remarks>
    /// Los dos puntos de <c>GetPaperMargins</c> llegan como dos arreglos de tres, así que hay que
    /// aplanar: quedan <c>[izq, inf, 0, der, sup, 0]</c>. Lo que no sea número se salta en lugar de
    /// reventar, porque de esto solo se saca un margen y sin él se usa el papel entero.
    /// </remarks>
    private static List<double> ComoNumeros(object?[]? args)
    {
        var salida = new List<double>();

        if (args is null)
        {
            return salida;
        }

        foreach (var v in args)
        {
            if (v is System.Collections.IEnumerable anidada and not string)
            {
                foreach (var y in anidada)
                {
                    if (Numero(y) is { } n)
                    {
                        salida.Add(n);
                    }
                }
            }
            else if (Numero(v) is { } m)
            {
                salida.Add(m);
            }
        }

        return salida;
    }

    // ======================================================================
    //  EL LAYOUT Y EL BLOQUE
    // ======================================================================

    private List<string> NombresDeLayout()
    {
        var salida = new List<string>();

        try
        {
            AcadConnection.Retry(() =>
            {
                salida.Clear();

                foreach (dynamic lo in _doc.Layouts)
                {
                    salida.Add((string)lo.Name);
                }
            });
        }
        catch (Exception)
        {
            // Sin la lista, NombreLibre no puede evitar el choque. Se sigue: Layouts.Add da su
            // propio error y el plano se reporta como no generado.
        }

        return salida;
    }

    private object? CrearLayout(string nombre)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                if (Sobrescribir)
                {
                    foreach (dynamic lo in _doc.Layouts)
                    {
                        if (string.Equals((string)lo.Name, nombre, StringComparison.OrdinalIgnoreCase))
                        {
                            lo.Delete();
                            break;
                        }
                    }
                }

                return (object?)_doc.Layouts.Add(nombre);
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo crear el layout «{nombre}»: {ex.Message}");

            return null;
        }
    }

    private object? Insertar(object lay, double x, double y)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
                (object?)((dynamic)lay).Block.InsertBlock(
                    new[] { x, y, 0.0 }, Cajetin, 1.0, 1.0, 1.0, 0.0));
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo insertar el cajetín «{Cajetin}»: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Mide el bloque insertado, lo escala sin deformarlo y lleva su centro al del área.
    /// </summary>
    /// <remarks>
    /// Se mide con <c>GetBoundingBox</c>, o sea la medida <b>real de lo que se dibujó</b>. Por eso
    /// funciona sin importar dónde esté el punto base del bloque —al centro, en una esquina o fuera
    /// del dibujo— ni para qué tamaño de hoja se dibujó originalmente. Es lo que la macro llama
    /// «una sola ruta, sin suposiciones».
    /// </remarks>
    private double EncajarYCentrar(object br, double cx, double cy, double w, double h)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic b = br;

                var caja = Caja(br);

                if (caja is null)
                {
                    return 1.0;
                }

                var (bx0, by0, bx1, by1) = caja.Value;

                var s = Solapas.EscalaParaCaber(bx1 - bx0, by1 - by0, w, h, Margen);

                if (Math.Abs(s - 1) > 5e-4)
                {
                    // La base del escalado es el centro de la propia caja, así que el bloque no se
                    // mueve de sitio al escalar y el centrado de abajo es una sola resta.
                    b.ScaleEntity(
                        new[] { (bx0 + bx1) / 2, (by0 + by1) / 2, 0.0 }, s);

                    caja = Caja(br);

                    if (caja is null)
                    {
                        return s;
                    }

                    (bx0, by0, bx1, by1) = caja.Value;
                }

                b.Move(
                    new[] { 0.0, 0.0, 0.0 },
                    new[] { cx - ((bx0 + bx1) / 2), cy - ((by0 + by1) / 2), 0.0 });

                return s;
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"No se pudo encajar el cajetín en la hoja: {ex.Message}");

            return 1;
        }
    }

    private static (double X0, double Y0, double X1, double Y1)? Caja(object ent)
    {
        try
        {
            object? min = null;
            object? max = null;

            var args = new object?[] { min, max };

            ent.GetType().InvokeMember(
                "GetBoundingBox",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: ent, args: args,
                modifiers: new[]
                {
                    new System.Reflection.ParameterModifier(2) { [0] = true, [1] = true },
                },
                culture: null, namedParameters: null);

            if (args[0] is not double[] a || args[1] is not double[] b)
            {
                return null;
            }

            return (a[0], a[1], b[0], b[1]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Escribe los datos en los atributos del cajetín. Devuelve cuántos se llenaron.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Los atributos que <see cref="Solapas.TextoDeAtributo"/> no reconoce <b>no se tocan</b>: puede
    /// haber en el cajetín datos que el dibujante pone a mano, y borrárselos en cada corrida sería
    /// peor que no llenar nada.
    /// </para>
    /// <para>
    /// Y a los que no están alineados a la izquierda se les reasigna el punto de alineación. Es de la
    /// macro y es un defecto conocido de COM: al cambiar el texto de un atributo centrado o alineado
    /// a la derecha, AutoCAD no recalcula su posición y el rótulo queda descolocado dentro de su
    /// recuadro. Reasignar el punto lo obliga.
    /// </para>
    /// </remarks>
    private int RellenarAtributos(object br, SolapaCad s)
    {
        var n = 0;

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic b = br;

                n = 0;

                if (!(bool)b.HasAttributes)
                {
                    return;
                }

                foreach (dynamic att in b.GetAttributes())
                {
                    var texto = Solapas.TextoDeAtributo(s, (string)att.TagString);

                    if (texto is null)
                    {
                        continue;
                    }

                    att.TextString = Solapas.Formatear(texto);

                    if ((int)att.Alignment != 0)
                    {
                        att.TextAlignmentPoint = att.TextAlignmentPoint;
                    }

                    n++;
                }
            });
        }
        catch (Exception ex)
        {
            Notas.Add($"{Etiqueta(s)}: no se pudieron llenar todos los atributos: {ex.Message}");
        }

        return n;
    }

    /// <summary>Deja a la vista el primer layout generado.</summary>
    public void MostrarPrimerLayout()
    {
        if (PrimerLayout.Length == 0)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                _doc.ActiveSpace = 0;                  // acPaperSpace
                _doc.ActiveLayout = _doc.Layouts.Item(PrimerLayout);
                _doc.Regen(1);                         // acAllViewports
            });
        }
        catch (Exception)
        {
            // Que no se vea el layout no invalida nada de lo dibujado.
        }
    }

    private static string Etiqueta(SolapaCad s)
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
