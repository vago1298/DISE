using System.Diagnostics;
using System.Windows;
using CadLink.Etabs;
using CadLink.Licensing;

namespace CadLink.App;

/// <summary>
/// Arranque de la aplicación: splash con logo, validación de licencia y apertura
/// de la ventana principal.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Tiempo mínimo que el splash permanece visible. Sin este mínimo, en un
    /// equipo rápido con licencia en cache el logo aparecería y desaparecería en
    /// un destello, que se ve como un parpadeo defectuoso.
    /// </summary>
    private static readonly TimeSpan MinimumSplashTime = TimeSpan.FromMilliseconds(1800);

    private LicenseService? _licenseService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sin ventana principal todavía; si no se cambia, la app se cerraría al
        // ocultar el splash.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Ruta manual a ETABSv1.dll, si la configuración trae una. Normalmente
        // está vacía y la librería se localiza sola junto al ETABS abierto.
        EtabsAssembly.RutaConfigurada = AppInfo.RutaLibreriaEtabs;

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "Ocurrió un error inesperado:\n\n" + args.Exception.Message,
                AppInfo.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _ = RunStartupAsync();
    }

    /// <summary>
    /// Envoltura del arranque. Sin este try/catch, una excepción en el arranque
    /// asíncrono quedaría sin observar y la aplicación se cerraría en silencio,
    /// sin darle al usuario ninguna pista de qué pasó.
    /// </summary>
    private async Task RunStartupAsync()
    {
        try
        {
            await StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo iniciar la aplicación:\n\n" + ex.Message,
                AppInfo.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartAsync()
    {
        var splash = new SplashWindow();
        splash.Show();

        var stopwatch = Stopwatch.StartNew();

        _licenseService = new LicenseService(AppInfo.CreateLicensingOptions());

        splash.SetStatus("Identificando este equipo…");
        var info = await Task.Run(() => _licenseService.EvaluateAsync()).ConfigureAwait(true);

        splash.SetLicenseResult(info);

        // Respeta el tiempo mínimo de splash antes de continuar.
        var remaining = MinimumSplashTime - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining).ConfigureAwait(true);
        }

        // Estados sin salida: no hay nada que el usuario pueda hacer desde aquí.
        if (info.State is LicenseState.Revoked or LicenseState.Error)
        {
            splash.Close();
            ShowBlockingMessage(info);
            Shutdown(1);
            return;
        }

        // Estados recuperables: se ofrece la pantalla de activación.
        if (!info.IsUsable)
        {
            splash.Hide();

            var activation = new ActivationWindow(_licenseService, info);
            var accepted = activation.ShowDialog() == true;

            splash.Close();

            if (!accepted || activation.Result is null || !activation.Result.IsUsable)
            {
                Shutdown(1);
                return;
            }

            info = activation.Result;
        }
        else
        {
            splash.Close();
        }

        // Nombre completo del tipo: "MainWindow" también es una propiedad de
        // Application, y calificarlo evita cualquier duda al leer el código.
        var main = new CadLink.App.MainWindow(_licenseService, info);
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }

    private static void ShowBlockingMessage(LicenseInfo info)
    {
        var detail = string.IsNullOrWhiteSpace(info.Message) ? info.StatusLine : info.Message;

        MessageBox.Show(
            $"{detail}\n\n" +
            $"Huella de este equipo:\n{MachineFingerprint.ToDisplayGroups(info.Fingerprint)}\n\n" +
            $"Si crees que es un error, escribe a {AppInfo.SupportEmail} " +
            "e incluye la huella de arriba.",
            AppInfo.ProductName + " — Licencia",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _licenseService?.Dispose();
        base.OnExit(e);
    }
}
