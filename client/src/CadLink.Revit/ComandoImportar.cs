using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.Revit;

/// <summary>
/// El comando del boton: importa el modelo de CadLink a este proyecto de Revit.
/// </summary>
/// <remarks>
/// <c>TransactionMode.Manual</c> porque la transaccion la abre <see cref="Modelador"/> cuando
/// toca, y no antes: primero hay que leer el documento y ensenar el cuadro de mapeo, y eso no
/// debe ocurrir con una transaccion abierta.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoImportar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData?.Application?.ActiveUIDocument;

        if (uiDoc?.Document is null)
        {
            message = "Abre un proyecto de Revit antes de importar.";

            return Result.Failed;
        }

        var doc = uiDoc.Document;

        if (doc.IsFamilyDocument)
        {
            TaskDialog.Show("CadLink",
                "Esto se importa en un PROYECTO, no dentro del editor de familias.");

            return Result.Cancelled;
        }

        // ---- 1. El archivo del modelo ----
        var abrir = new OpenFileDialog
        {
            Title = "Abrir el modelo exportado por CadLink",
            Filter = ArchivoModelo.Filtro + "|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (abrir.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        ModeloJson modelo;

        try
        {
            modelo = ArchivoModelo.Leer(abrir.FileName);
        }
        catch (Exception e)
        {
            // Se dice el motivo. "No se pudo leer" no deja hacer nada.
            TaskDialog.Show("CadLink", "No se pudo leer el modelo.\n\n" + e.Message);

            return Result.Failed;
        }

        if (modelo.Piezas == 0)
        {
            TaskDialog.Show("CadLink", "El archivo no trae ninguna pieza que modelar.");

            return Result.Cancelled;
        }

        // ---- 2. Que hay en ESTE proyecto de Revit ----
        var catalogo = LectorDeCatalogo.Leer(doc);

        if (catalogo.Tipos.Count == 0)
        {
            TaskDialog.Show("CadLink",
                "En este proyecto no hay ninguna familia estructural cargada, asi que no hay "
                + "nada con lo que modelar.\n\n"
                + "Carga al menos una familia de pilares y una de vigas, o empieza de una "
                + "plantilla estructural, y vuelve a intentarlo.");

            return Result.Cancelled;
        }

        // ---- 3. El mapeo, con lo elegido la vez anterior si lo hay ----
        var rutaMapeo = ArchivoMapeo.RutaPara(abrir.FileName);
        Mapeo guardado;

        try
        {
            guardado = ArchivoMapeo.LeerOVacio(rutaMapeo);
        }
        catch (Exception e)
        {
            // Un mapeo ilegible no debe impedir importar: se avisa y se sigue sin el.
            TaskDialog.Show("CadLink",
                "Habia un mapeo guardado pero no se pudo leer, asi que se empieza de cero.\n\n"
                + e.Message);

            guardado = new Mapeo();
        }

        var vista = new VistaMapeo(modelo, catalogo, guardado);

        // ---- 4. El cuadro ----
        var ventana = new VentanaMapeo(vista);

        // Sin dueno, la ventana se puede quedar DETRAS de Revit y parece que el comando se
        // colgo. Se le pone la ventana principal de Revit como dueno.
        try
        {
            // El ?. no es de adorno: Application viene declarado como que puede faltar, y
            // ponerle dueno a la ventana es cosmetico. Si no hay de donde sacar el asa, se
            // ensena la ventana sin dueno antes que tirar el comando por un detalle de forma.
            var asaDeRevit = commandData.Application?.MainWindowHandle;

            if (asaDeRevit is not null)
            {
                new WindowInteropHelper(ventana).Owner = asaDeRevit.Value;
            }
        }
        catch (Exception)
        {
            // Si no se puede, se ensena igual: es cosmetico.
        }

        if (ventana.ShowDialog() != true || ventana.Eleccion is null)
        {
            return Result.Cancelled;
        }

        var modo = ventana.Eleccion.Value;
        var mapeo = vista.AMapeo();

        // ---- 5. Guardar el mapeo para la proxima ----
        try
        {
            ArchivoMapeo.Guardar(mapeo, rutaMapeo);
        }
        catch (Exception e)
        {
            // No poder guardar el mapeo no debe abortar la importacion, que es lo que el
            // usuario vino a hacer.
            TaskDialog.Show("CadLink",
                "No se pudo guardar el mapeo para la proxima vez, pero la importacion sigue.\n\n"
                + e.Message);
        }

        // ---- 6. Ajustar los muros, YA CON EL MAPEO ELEGIDO ----
        //
        // Esto se hacia al exportar, y estaba mal de raiz: al exportar todavia no se sabe con
        // que tipo de familia se va a modelar cada pieza, porque se elige aqui. Asi que el muro
        // se recortaba medio castillo de la SECCION DEL CALCULO y el castillo modelado podia
        // medir otra cosa: el muro quedaba metido dentro. Lo mismo con el peralte de la cadena.
        //
        // Ahora se ajusta con las medidas de los tipos elegidos, que son las de las piezas que
        // van a existir de verdad.
        var medidor = Orientacion.Medidor(modelo, mapeo, catalogo);

        var avisosMuros = AjusteDeMuros.AplicarATodos(modelo, null, medidor);

        // Cuantas barras pudieron dar sus medidas REALES. Se dice porque es la diferencia entre
        // recortar el muro al pano del castillo que va a existir y recortarlo al de la seccion
        // del calculo: si esta cuenta sale baja, el ajuste esta trabajando a ciegas y hay que
        // saberlo aqui, no deducirlo del modelo terminado.
        var conMedidas = modelo.Barras.Count(b => medidor(b) is not null);

        avisosMuros.Add(
            $"«medidas»: {conMedidas} de {modelo.Barras.Count} barra(s) se ajustaron con las "
            + "medidas del tipo de Revit elegido; el resto, con las de la seccion del calculo");

        // Las losas A PAÑO: su orilla en la cara exterior de los muros y trabes de fachada, no
        // en su eje. Con los espesores de los tipos elegidos, igual que los muros.
        avisosMuros.AddRange(LosasAPano.AplicarATodos(
            modelo, medidor, Orientacion.MedidorDeMuros(modelo, mapeo, catalogo)));

        // ---- 7. El plan y el modelado ----
        var plan = Planificador.Armar(
            modelo, mapeo, catalogo, LectorDeCatalogo.Existentes(doc), modo);

        if (!plan.HayTrabajo)
        {
            TaskDialog.Show("CadLink", "No hay nada que hacer: " + plan.Resumen() + ".");

            return Result.Succeeded;
        }

        var r = Modelador.Ejecutar(doc, plan);

        // Los avisos que trae el propio archivo van PRIMERO: son decisiones que CadLink tuvo
        // que tomar al exportar -un paño cuyas notas dicen losa y cuyo contorno es vertical,
        // por ejemplo- y quien esta en Revit tiene que poder enterarse sin volver a CadLink.
        r.Avisos.InsertRange(0, modelo.Avisos);
        r.Avisos.InsertRange(0, avisosMuros);

        // EN PANTALLA, SOLO LOS ERRORES: se pidio «solo quiero que me anuncie si hay un error,
        // no todo». El informe completo -recortes de muros, niveles renombrados, avisos de
        // Revit...- se guarda junto al mapeo por si hace falta revisarlo.
        var rutaInforme = Path.ChangeExtension(rutaMapeo, null);
        rutaInforme = (rutaInforme.EndsWith(".cadlink-mapeo", StringComparison.OrdinalIgnoreCase)
            ? rutaInforme.Substring(0, rutaInforme.Length - ".cadlink-mapeo".Length)
            : rutaInforme) + ".cadlink-informe.txt";

        try
        {
            File.WriteAllText(rutaInforme, Informe(modo, plan, r, rutaMapeo, completo: true));
        }
        catch (Exception)
        {
            rutaInforme = string.Empty;
        }

        TaskDialog.Show("CadLink", Informe(modo, plan, r, rutaMapeo, completo: false, rutaInforme));

        return Result.Succeeded;
    }

    /// <param name="completo">Con los avisos. En pantalla va sin ellos: solo los errores.</param>
    /// <param name="rutaInforme">Donde quedo el informe completo, para decirlo.</param>
    private static string Informe(
        Modo modo, Plan plan, ResultadoModelado r, string rutaMapeo, bool completo, string rutaInforme = "")
    {
        var sb = new StringBuilder();

        sb.AppendLine(modo == Modo.ModelarNuevos
            ? "Se modelaron las piezas nuevas."
            : "Se actualizaron las piezas que ya estaban.");

        sb.AppendLine();
        sb.Append("Creadas: ").Append(r.Creadas).AppendLine();
        sb.Append("Actualizadas: ").Append(r.Actualizadas).AppendLine();

        if (r.NivelesCreados > 0)
        {
            sb.Append("Niveles creados: ").Append(r.NivelesCreados).AppendLine();
        }

        if (r.EjesCreados > 0)
        {
            sb.Append("Ejes creados: ").Append(r.EjesCreados).AppendLine();
        }

        if (plan.SinMapeo > 0)
        {
            sb.Append("Sin tipo elegido, no modeladas: ").Append(plan.SinMapeo).AppendLine();
        }

        // Estas dos cuentas existen porque su ausencia escondio el fallo mas caro que ha tenido
        // el complemento: los paños que compartian pier se descartaban sin aparecer en ninguna
        // cuenta, y una planta entera podia quedarse sin modelar mientras el informe decia que
        // todo habia ido bien.
        if (plan.Desempatadas > 0)
        {
            sb.Append("Compartian etiqueta con otra y se distinguieron por su posicion: ")
              .Append(plan.Desempatadas).AppendLine(" (SI se modelan)");
        }

        if (plan.Duplicadas > 0)
        {
            sb.Append("Descartadas por estar repetidas en el mismo sitio: ")
              .Append(plan.Duplicadas).AppendLine();
        }

        if (plan.Sobra > 0)
        {
            sb.AppendLine();
            sb.Append(plan.Sobra).AppendLine(" pieza(s) siguen en Revit con marca de CadLink "
                + "y ya no estan en el modelo. NO se borraron: revisalas a mano.");
        }

        if (completo && r.Avisos.Count > 0)
        {
            // AGRUPADOS por causa, igual que los errores y por el mismo motivo: un muro mallado
            // en cien trozos generaba cien avisos con la misma causa, y ensenar los seis
            // primeros escondia tanto la causa como la escala.
            sb.AppendLine();
            sb.Append(Agrupador.Texto(r.Avisos, 6, "Hay algo que decir de")).AppendLine();
        }

        if (r.Errores.Count > 0)
        {
            // AGRUPADOS por causa, no una lista de los ocho primeros. Una importacion que
            // falla casi nunca falla por trescientos motivos distintos: falla por UNO que
            // afecta a trescientas piezas, y los ocho primeros mensajes dicen todos lo mismo
            // sin que se vea que son iguales. Agrupado se lee "372 x el tipo elegido no admite
            // este contorno", y eso si dice donde mirar.
            sb.AppendLine();
            sb.AppendLine(Agrupador.Texto(r.Errores));
        }

        if (!completo)
        {
            if (r.Errores.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("Sin errores.");
            }

            if (rutaInforme.Length > 0)
            {
                sb.Append("El informe completo esta en ").Append(Path.GetFileName(rutaInforme)).AppendLine(".");
            }
        }

        sb.AppendLine();
        sb.Append("El mapeo quedo guardado en ").Append(Path.GetFileName(rutaMapeo))
          .AppendLine(", asi que la proxima vez no hay que volver a elegir.");

        sb.AppendLine();
        sb.Append("Un solo Ctrl+Z deshace toda la importacion.");

        return sb.ToString();
    }
}
