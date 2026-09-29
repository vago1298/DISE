using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CadLink.Etabs;
using CadLink.Ifc;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.App;

/// <summary>
/// Exportar el modelo leido de ETABS o SAP2000 a un archivo IFC, para llevarlo a Revit.
/// </summary>
/// <remarks>
/// <para>
/// Este archivo es <b>a proposito muy tonto</b>: traduce <see cref="ElementoEtabs"/> a los
/// tipos de <c>CadLink.Ifc</c> copiando campos, y nada mas. Toda la decision -que entidad
/// de IFC, que perfil, donde se coloca cada pieza- vive en <c>CadLink.Ifc</c>.
/// </para>
/// <para>
/// El reparto es asi por una razon practica: <c>CadLink.App</c> es WPF y no se puede
/// compilar sin una maquina con Windows y el ref pack, mientras que <c>CadLink.Ifc</c> es
/// net8.0 pelado y se compila y se prueba en cualquier parte. Asi que lo que no se puede
/// comprobar se deja reducido a copiar campos, que es donde menos dano hace un error.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>Exporta el modelo a IFC y deja al lado la tabla de secciones.</summary>
    private void OnExportarIfc(object sender, RoutedEventArgs e)
    {
        if (_modeloEtabs is null || _modeloEtabs.Elementos.Count == 0)
        {
            EtabsStatusText.Text =
                "Primero lee el modelo de ETABS o de SAP2000: no hay nada que exportar.";
            return;
        }

        var obra = string.IsNullOrWhiteSpace(_juego.Solapa.Obra)
            ? "Modelo estructural"
            : _juego.Solapa.Obra.Trim();

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar el modelo a IFC, para abrirlo en Revit",
            Filter = "Archivo IFC (*.ifc)|*.ifc",
            DefaultExt = ".ifc",

            // Se propone el nombre de la obra: es lo que el usuario reconoce.
            FileName = LimpioParaArchivo(obra) + ".ifc"
        };

        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Cursor = Cursors.Wait;

            var paraIfc = AModeloIfc(_modeloEtabs, obra);
            var r = ExportadorIfc.AArchivo(paraIfc, dialogo.FileName);

            // La tabla de secciones se escribe SIEMPRE al lado del .ifc. Es lo que hace
            // falta para el paso que sigue -decidir que familia de Revit le toca a cada
            // seccion- y se agradece tenerla en una hoja en vez de ir sacandola del modelo.
            var tabla = Path.ChangeExtension(dialogo.FileName, ".secciones.csv");
            var cuantas = EscribirTablaDeSecciones(paraIfc, tabla);

            // Y el archivo que come el COMPLEMENTO de Revit. Va junto al .ifc y no en su
            // lugar: son dos caminos distintos y los dos sirven.
            //
            //   el .ifc   -> vincular o abrir en Revit. Rapido, con geometria y tipos, pero
            //                los elementos no son nativos y Revit no pregunta familias.
            //   el .json  -> el complemento de CadLink para Revit. Pregunta que familia va
            //                con cada seccion y crea columnas, trabes, muros y losas
            //                NATIVOS de Revit.
            var paraPlugin = Path.ChangeExtension(dialogo.FileName, null) + ArchivoModelo.Extension;
            ArchivoModelo.Guardar(AModeloJson(_modeloEtabs, obra), paraPlugin);

            EtabsStatusText.Text =
                TextoDelResumen(r, dialogo.FileName, tabla, cuantas, paraPlugin);
            StatusText.Text =
                $"IFC exportado: {r.Total} pieza(s) en {Path.GetFileName(dialogo.FileName)}.";
        }
        catch (Exception ex)
        {
            // Se dice la RUTA y el motivo. "No se pudo exportar" no deja hacer nada.
            MessageBox.Show(
                $"No se pudo exportar el IFC en:\n{dialogo.FileName}\n\n{ex.Message}",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);

            EtabsStatusText.Text = "No se pudo exportar el IFC: " + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    // ==================================================================
    //  La traduccion
    // ==================================================================

    /// <summary>Pasa el modelo leido a los tipos neutros que entiende el exportador.</summary>
    internal static ModeloIfc AModeloIfc(ModeloEtabs modelo, string obra)
    {
        var salida = new ModeloIfc
        {
            Programa = modelo.Programa,
            Archivo = modelo.Archivo,
            Obra = obra
        };

        // Solo los niveles que tienen algo. Un IFC con veinte niveles vacios porque el
        // modelo los traia declarados es un arbol de proyecto en Revit lleno de ruido.
        foreach (var n in modelo.NivelesConElementos())
        {
            salida.Niveles.Add(new NivelIfc { Nombre = n.Nombre, ElevacionM = n.ElevacionM });
        }

        foreach (var el in modelo.Elementos)
        {
            if (string.Equals(el.Forma, "AREA", StringComparison.OrdinalIgnoreCase))
            {
                salida.Panos.Add(APano(el));
            }
            else
            {
                salida.Barras.Add(ABarra(el));
            }
        }

        return salida;
    }

    private static PanoIfc APano(ElementoEtabs el)
    {
        var pano = new PanoIfc
        {
            Etiqueta = el.Etiqueta,
            Clase = el.Clase == ClaseElemento.Muro ? ClaseIfc.Muro : ClaseIfc.Losa,
            Nivel = el.Story,

            // En un muro el nombre util es su PIER, no la propiedad de area: es con lo que
            // se identifica en el modelo y en los planos. Si no tiene, queda la propiedad.
            Seccion = string.IsNullOrWhiteSpace(el.Pier) || el.Clase != ClaseElemento.Muro
                ? el.Seccion
                : el.Seccion + " (" + el.Pier.Trim() + ")",

            // En un area, el "ancho" que trae el lector es el espesor.
            EspesorM = el.AnchoM,
            Material = el.Material
        };

        foreach (var v in el.Vertices3D)
        {
            pano.Vertices.Add(v);
        }

        return pano;
    }

    private static BarraIfc ABarra(ElementoEtabs el)
    {
        var vertical = el.Clase == ClaseElemento.Columna;

        var ux = el.X2 - el.X1;
        var uy = el.Y2 - el.Y1;

        // Los ejes locales los calcula CadLink.Etabs, que es donde vive esa regla. Aqui no
        // se replica: con dos copias, en cuanto una se corrige la otra se queda vieja.
        var (e1, e2, e3) = PuntoDeInsercion.Ejes(vertical, ux, uy, el.AnguloGrados);

        // ---- Ancho y peralte: sobre que eje va cada uno ----
        //
        // El exportador espera el ANCHO sobre el eje local 3 y el PERALTE sobre el 2, que
        // es como el esquema de IFC define un perfil parametrico.
        //
        // Se pasan en el mismo orden en que los consume Perfil2D.De(forma, ancho, alto, ...),
        // que es la unica fuente de secciones que ya usa la aplicacion -la vista extruida y
        // el 3D de AutoCAD-. Es deliberado: asi el IFC ensena lo MISMO que la aplicacion ya
        // dibuja. Si la pareja estuviera cambiada, estaria cambiada en los tres sitios a la
        // vez y se corrige en uno.
        //
        // OJO, cabo pendiente: para las COLUMNAS el lector invierte las medidas de CSI
        // (AnchoM = T3, PeralteM = T2, al contrario que en una trabe; EtabsReader.cs:654) y
        // los comentarios del repo no coinciden en que eje corresponde a t2 y a t3
        // -PuntoDeInsercion dice que t3 se mide sobre el eje 2, y el lector que en una
        // columna el ancho va sobre el 3-. No se puede resolver sin un modelo real delante.
        // El sintoma, si esta al reves, es muy concreto y facil de ver: las columnas NO
        // cuadradas saldrian giradas 90 grados en planta, y las cuadradas bien. En ese caso
        // se cambian estas dos lineas y nada mas.
        var anchoM = el.AnchoM;
        var peralteM = el.PeralteM;

        // El punto cardinal corre el CENTRO de la seccion respecto de la linea de los
        // nudos. dim2 es la medida sobre el eje 2 y dim3 la del 3, en ese orden.
        var (c2, c3) = PuntoDeInsercion.PorPuntoCardinal(
            el.PuntoCardinal, peralteM, anchoM, el.Espejo2, el.Espejo3);

        return new BarraIfc
        {
            Etiqueta = el.Etiqueta,
            Clase = AClase(el.Clase),
            Nivel = el.Story,
            X1 = el.X1, Y1 = el.Y1, Z1 = el.Z1,
            X2 = el.X2, Y2 = el.Y2, Z2 = el.Z2,
            E1 = e1,
            E2 = e2,
            E3 = e3,
            CorrimientoEje2M = c2,
            CorrimientoEje3M = c3,
            Seccion = new SeccionIfc
            {
                Nombre = el.Seccion,
                Forma = AForma(el.Forma),
                AnchoM = anchoM,
                PeralteM = peralteM,
                PatinM = el.PatinM,
                AlmaM = el.AlmaM,
                ParedM = el.ParedM,
                Material = el.Material
            }
        };
    }

    // ==================================================================
    //  La traduccion para el COMPLEMENTO de Revit
    // ==================================================================

    /// <summary>
    /// Pasa el modelo leido al formato que come el complemento de CadLink para Revit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es un formato APARTE del que usa el exportador de IFC, y a proposito. El IFC necesita
    /// la terna de ejes locales ya calculada; el complemento necesita en cambio el ANGULO de
    /// giro, porque es lo que Revit pide en su parametro de rotacion de la seccion. Compartir
    /// un solo modelo obligaria a llevar las dos cosas y a que cada consumidor ignorara la
    /// mitad.
    /// </para>
    /// <para>
    /// Y sobre todo: el JSON es un CONTRATO entre dos programas que se instalan por separado
    /// y se actualizan por separado. Atarlo al modelo interno del exportador de IFC haria
    /// que un cambio pensado para el IFC rompiera los archivos ya repartidos.
    /// </para>
    /// </remarks>
    internal static ModeloJson AModeloJson(ModeloEtabs modelo, string obra)
    {
        var salida = new ModeloJson
        {
            Programa = modelo.Programa,
            Archivo = modelo.Archivo,
            Obra = obra,
            Exportado = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
        };

        foreach (var n in modelo.NivelesConElementos())
        {
            salida.Niveles.Add(new NivelJson { Nombre = n.Nombre, ElevacionM = n.ElevacionM });
        }

        foreach (var el in modelo.Elementos)
        {
            if (string.Equals(el.Forma, "AREA", StringComparison.OrdinalIgnoreCase))
            {
                var pano = new PanoJson
                {
                    Etiqueta = el.Etiqueta,
                    Clase = el.Clase == ClaseElemento.Muro ? ClasePieza.Muro : ClasePieza.Losa,
                    Nivel = el.Story,
                    Seccion = new SeccionJson
                    {
                        Nombre = el.Seccion,
                        Forma = FormaSeccion.Pano,

                        // En un area, el "ancho" que trae el lector es el espesor.
                        EspesorM = el.AnchoM,
                        Material = el.Material,
                        Notas = el.Notas
                    }
                };

                foreach (var v in el.Vertices3D)
                {
                    pano.Vertices.Add(new PuntoJson { X = v.X, Y = v.Y, Z = v.Z });
                }

                salida.Panos.Add(pano);

                continue;
            }

            salida.Barras.Add(new BarraJson
            {
                Etiqueta = el.Etiqueta,
                Clase = APieza(el.Clase),
                Nivel = el.Story,
                P1 = new PuntoJson { X = el.X1, Y = el.Y1, Z = el.Z1 },
                P2 = new PuntoJson { X = el.X2, Y = el.Y2, Z = el.Z2 },
                AnguloGrados = el.AnguloGrados,
                Seccion = new SeccionJson
                {
                    Nombre = el.Seccion,
                    Forma = AFormaSeccion(el.Forma),
                    AnchoM = el.AnchoM,
                    PeralteM = el.PeralteM,
                    PatinM = el.PatinM,
                    AlmaM = el.AlmaM,
                    ParedM = el.ParedM,
                    Material = el.Material,
                    Notas = el.Notas
                }
            });
        }

        return salida;
    }

    private static ClasePieza APieza(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => ClasePieza.Columna,
        ClaseElemento.Trabe => ClasePieza.Trabe,
        ClaseElemento.Diagonal => ClasePieza.Diagonal,
        ClaseElemento.Muro => ClasePieza.Muro,
        ClaseElemento.Losa => ClasePieza.Losa,
        _ => ClasePieza.Trabe
    };

    /// <summary>La forma del lector, en la enumeracion que usa el complemento.</summary>
    internal static FormaSeccion AFormaSeccion(string? forma) =>
        (forma ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "RECT" => FormaSeccion.Rectangulo,
            "CIRC" => FormaSeccion.Circulo,
            "TUBO" => FormaSeccion.Tubo,
            "PIPE" => FormaSeccion.Tubo,
            "CAJON" => FormaSeccion.Cajon,
            "I" => FormaSeccion.PerfilI,
            "C" => FormaSeccion.PerfilC,
            "T" => FormaSeccion.PerfilT,
            "L" => FormaSeccion.PerfilL,
            _ => FormaSeccion.Rectangulo
        };

    private static ClaseIfc AClase(ClaseElemento c) => c switch
    {
        ClaseElemento.Columna => ClaseIfc.Columna,
        ClaseElemento.Trabe => ClaseIfc.Trabe,
        ClaseElemento.Diagonal => ClaseIfc.Diagonal,
        ClaseElemento.Muro => ClaseIfc.Muro,
        ClaseElemento.Losa => ClaseIfc.Losa,

        // Todas las clases de ahora van escritas arriba. Este respaldo es para una clase
        // NUEVA, y tratarla como trabe es lo menos danino: sale como IfcBeam en vez de
        // desaparecer.
        _ => ClaseIfc.Trabe
    };

    /// <summary>Pasa la cadena de forma del lector a la enumeracion del exportador.</summary>
    /// <remarks>
    /// Las cadenas son las que escribe <c>EtabsReader</c>: RECT, CIRC, I, C, T, L, TUBO,
    /// CAJON. Se acepta tambien PIPE, que aparece en algun modelo viejo como sinonimo de
    /// TUBO. Lo que no se reconoce cae en rectangulo, que es el respaldo del propio lector.
    /// </remarks>
    internal static FormaIfc AForma(string? forma) =>
        (forma ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            // RECT va escrito aunque coincida con el respaldo del final. Asi, el dia que el
            // lector aprenda una forma nueva, esa cae en el respaldo y la comprobacion de
            // tools/validar.py lo canta; si el respaldo cubriera tambien las conocidas, no
            // habria forma de distinguir "no traducida" de "traducida al rectangulo".
            "RECT" => FormaIfc.Rectangulo,
            "CIRC" => FormaIfc.Circulo,
            "TUBO" => FormaIfc.Tubo,
            "PIPE" => FormaIfc.Tubo,
            "CAJON" => FormaIfc.Cajon,
            "I" => FormaIfc.PerfilI,
            "C" => FormaIfc.PerfilC,
            "T" => FormaIfc.PerfilT,
            "L" => FormaIfc.PerfilL,
            _ => FormaIfc.Rectangulo
        };

    // ==================================================================
    //  La tabla de secciones, para mapear familias en Revit
    // ==================================================================

    /// <summary>Escribe un CSV con una fila por seccion distinta. Devuelve cuantas hay.</summary>
    /// <remarks>
    /// Sirve para el paso que el IFC NO puede hacer solo: decidir que familia de Revit le
    /// toca a cada seccion. La columna «Familia de Revit» va vacia a proposito, para
    /// llenarla.
    /// </remarks>
    internal static int EscribirTablaDeSecciones(ModeloIfc modelo, string ruta)
    {
        var filas = new SortedDictionary<string, (string Clase, string Nombre, string Forma,
            string Medidas, string Material, int Cuantas)>(StringComparer.OrdinalIgnoreCase);

        void Sumar(string clase, string nombre, string forma, string medidas, string material)
        {
            var clave = clase + "|" + nombre + "|" + medidas;

            if (filas.TryGetValue(clave, out var ya))
            {
                filas[clave] = (ya.Clase, ya.Nombre, ya.Forma, ya.Medidas, ya.Material,
                    ya.Cuantas + 1);
            }
            else
            {
                filas[clave] = (clase, nombre, forma, medidas, material, 1);
            }
        }

        foreach (var b in modelo.Barras)
        {
            Sumar(b.Clase.ToString(), b.Seccion.Nombre, b.Seccion.Forma.ToString(),
                EnCm(b.Seccion.AnchoM) + " x " + EnCm(b.Seccion.PeralteM) + " cm",
                b.Seccion.Material);
        }

        foreach (var p in modelo.Panos)
        {
            Sumar(p.Clase.ToString(), p.Seccion, p.Clase.ToString(),
                "e = " + EnCm(p.EspesorM) + " cm", p.Material);
        }

        var sb = new StringBuilder();

        // Con BOM y punto y coma: es lo que hace que Excel en espanol abra el archivo en
        // columnas y con los acentos bien al hacer doble clic, sin pasar por el asistente
        // de importacion.
        sb.Append('\uFEFF');
        sb.AppendLine("Clase;Seccion;Forma;Medidas;Material;Cuantas;Familia de Revit;Tipo de Revit");

        foreach (var f in filas.Values)
        {
            sb.AppendLine(string.Join(";",
                CampoCsv(f.Clase), CampoCsv(f.Nombre), CampoCsv(f.Forma), CampoCsv(f.Medidas),
                CampoCsv(f.Material),
                f.Cuantas.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty));
        }

        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));

        return filas.Count;
    }

    private static string EnCm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Escapa un campo de CSV.</summary>
    private static string CampoCsv(string? v)
    {
        var t = (v ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        // El separador es el punto y coma, asi que es ESE el que obliga a entrecomillar, no
        // la coma: una seccion llamada "IPR 356; 171" partiria la fila en dos.
        return t.Contains(';') || t.Contains('"')
            ? "\"" + t.Replace("\"", "\"\"") + "\""
            : t;
    }

    // ==================================================================
    //  El aviso al usuario
    // ==================================================================

    private static string TextoDelResumen(
        ResumenIfc r, string ifc, string csv, int secciones, string json)
    {
        var sb = new StringBuilder();

        sb.Append("IFC exportado: ").Append(Path.GetFileName(ifc)).AppendLine(".");
        sb.Append(r.Total).Append(" pieza(s) en ").Append(r.Niveles).Append(" nivel(es): ")
          .Append(r.Columnas).Append(" columna(s), ")
          .Append(r.Trabes).Append(" trabe(s), ")
          .Append(r.Diagonales).Append(" diagonal(es), ")
          .Append(r.Muros).Append(" muro(s) y ")
          .Append(r.Losas).AppendLine(" losa(s).");

        sb.Append(secciones).Append(" seccion(es) distintas, listadas en ")
          .Append(Path.GetFileName(csv)).AppendLine(" para elegir su familia en Revit.");

        sb.AppendLine();
        sb.Append("Para modelar elementos NATIVOS de Revit -y elegir la familia de cada ")
          .AppendLine("seccion en un cuadro- usa el complemento de CadLink para Revit con:");
        sb.Append("  ").AppendLine(Path.GetFileName(json));

        if (r.Avisos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Avisos:");

            // Se ensenan unos cuantos y se dice cuantos quedan. Con un modelo grande, un
            // aviso por pieza llenaria la pantalla y taparia el resto del resumen.
            foreach (var a in r.Avisos.Take(8))
            {
                sb.Append("  - ").AppendLine(a);
            }

            if (r.Avisos.Count > 8)
            {
                sb.Append("  ... y ").Append(r.Avisos.Count - 8).AppendLine(" aviso(s) mas.");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Quita de un nombre lo que Windows no admite en un archivo.</summary>
    private static string LimpioParaArchivo(string nombre)
    {
        var malos = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(nombre.Length);

        foreach (var c in nombre)
        {
            sb.Append(Array.IndexOf(malos, c) >= 0 ? '-' : c);
        }

        var limpio = sb.ToString().Trim();

        return limpio.Length == 0 ? "modelo" : limpio;
    }
}
