using System.IO;
using System.Text.Json;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// Donde se guarda el <b>estilo de dibujo</b>: para toda la aplicacion, no por proyecto.
/// </summary>
/// <remarks>
/// <para>
/// Va junto a las preferencias del tema, en <c>%LOCALAPPDATA%\CadLink\estilo-dibujo.json</c>. Es
/// una preferencia de quien dibuja -su letra, sus cotas, sus colores-, asi que vale para todos los
/// trabajos que abra.
/// </para>
/// <para>
/// Solo se guarda <b>lo que difiere del defecto</b>. Si un dia cambia un valor por defecto en el
/// programa, quien no lo toco recibe el nuevo, y el archivo se lee a mano.
/// </para>
/// </remarks>
internal static class EstiloDibujoArchivo
{
    private static string Ruta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CadLink", "estilo-dibujo.json");

    /// <summary>
    /// Lee lo guardado y lo deja en <see cref="EstiloDibujo.Actual"/>. Se llama al arrancar.
    /// </summary>
    /// <remarks>
    /// Cualquier fallo se traga: sin archivo, con un archivo roto o sin permiso, se dibuja con los
    /// valores por defecto, que son los de siempre. No poder leer una preferencia no puede impedir
    /// dibujar.
    /// </remarks>
    public static void Cargar()
    {
        var e = EstiloDibujo.PorDefecto();

        try
        {
            if (File.Exists(Ruta))
            {
                var guardado = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    File.ReadAllText(Ruta));
                e.Aplicar(guardado);
            }
        }
        catch (Exception)
        {
            e = EstiloDibujo.PorDefecto();
        }

        EstiloDibujo.Actual = e;
    }

    /// <summary>Guarda el estilo. Devuelve el error, o vacio si se guardo.</summary>
    public static string Guardar(EstiloDibujo estilo)
    {
        try
        {
            var dir = Path.GetDirectoryName(Ruta);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(Ruta, JsonSerializer.Serialize(
                estilo.ParaGuardar(), new JsonSerializerOptions { WriteIndented = true }));

            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
