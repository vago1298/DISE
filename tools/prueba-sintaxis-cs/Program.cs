using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CadLink.Pruebas;

/// <summary>
/// Revisa la <b>sintaxis</b> de todo el C# del cliente con el analizador de Roslyn.
/// </summary>
/// <remarks>
/// <para>
/// El motivo de que exista está en el .csproj: <c>CadLink.App</c> no se puede compilar en el
/// entorno de desarrollo, así que sus archivos se editan sin que nada los lea. Esto lee al menos
/// la forma.
/// </para>
/// <para>
/// <b>Y no es una compilación.</b> No se resuelven tipos, así que un nombre mal escrito o una
/// variable usada antes de declararla pasan por aquí sin protestar. Pasar esta prueba solo
/// significa que el archivo se puede parsear.
/// </para>
/// </remarks>
internal static class Programa
{
    private static int Main(string[] argumentos)
    {
        // Se descartan las opciones porque «dotnet run --nologo» las reenvía al programa en
        // lugar de quedárselas, y entonces la ruta a revisar sería «--nologo».
        var rutas = argumentos.Where(a => !a.StartsWith('-')).ToArray();

        // Por omisión, el código del cliente. Se acepta una ruta para poder apuntar a otra
        // carpeta al probar la propia herramienta.
        var raiz = rutas.Length > 0
            ? rutas[0]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "client", "src"));

        if (!Directory.Exists(raiz))
        {
            Console.WriteLine("FALLA: no existe la carpeta " + raiz);

            return 1;
        }

        Console.WriteLine("Revisando la sintaxis de " + raiz);

        var errores = 0;
        var archivos = 0;

        foreach (var archivo in Directory
            .EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .OrderBy(r => r, StringComparer.Ordinal))
        {
            // obj/ y bin/ traen código GENERADO (el .g.cs de cada XAML, el AssemblyInfo). No es
            // código que nadie escriba, y si sobra de una compilación vieja puede ni compilar.
            if (EstaGenerado(archivo))
            {
                continue;
            }

            archivos++;

            var arbol = CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(archivo)),
                new CSharpParseOptions(LanguageVersion.Latest));

            foreach (var aviso in arbol.GetDiagnostics())
            {
                if (aviso.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                var donde = aviso.Location.GetLineSpan().StartLinePosition;

                Console.WriteLine(
                    $"  FALLA {archivo}({donde.Line + 1},{donde.Character + 1}): "
                    + $"{aviso.Id}: {aviso.GetMessage()}");

                errores++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{archivos} archivos revisados.");

        if (errores > 0)
        {
            Console.WriteLine($"FALLA: {errores} errores de sintaxis.");

            return 1;
        }

        Console.WriteLine("TODO PASA: ningun error de sintaxis.");
        Console.WriteLine("Recuerda que esto NO es una compilacion: no se resuelven tipos.");

        return 0;
    }

    /// <summary>¿El archivo es código generado y no escrito a mano?</summary>
    private static bool EstaGenerado(string ruta)
    {
        var partes = ruta.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return partes.Contains("obj") || partes.Contains("bin");
    }
}
