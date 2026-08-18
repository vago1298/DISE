using System.Reflection;
using System.Windows;
using System.Windows.Media;
using CadLink.Licensing;

namespace CadLink.App;

/// <summary>
/// Pantalla de bienvenida con el logo de la empresa.
/// </summary>
/// <remarks>
/// Cumple dos funciones: presentar la marca y dar algo que mirar mientras se
/// valida la licencia, que puede tardar hasta el timeout de red. Sin splash, el
/// usuario vería la aplicación "congelada" un par de segundos al abrir.
/// </remarks>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        VersionText.Text = "Versión " + GetVersion();
        // Vacío = renglón oculto, para no dejar un hueco en el splash
        var empresa = AppInfo.CompanyName;
        var hayEmpresa = !string.IsNullOrWhiteSpace(empresa);

        CompanyText.Text = hayEmpresa ? empresa : string.Empty;
        CompanyText.Visibility = hayEmpresa ? Visibility.Visible : Visibility.Collapsed;
        ProductNameText.Text = AppInfo.ProductName;
        TaglineText.Text = AppInfo.Tagline;

        LogoImage.Source = Branding.Logo;
        Icon = Branding.Logo;
    }

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Actualiza el texto de estado. Seguro de llamar desde cualquier hilo.</summary>
    public void SetStatus(string text)
    {
        if (Dispatcher.CheckAccess())
        {
            StatusText.Text = text;
        }
        else
        {
            Dispatcher.Invoke(() => StatusText.Text = text);
        }
    }

    /// <summary>
    /// Muestra el resultado de la validación con el color que corresponde,
    /// para que el usuario capte de un vistazo si algo requiere su atención.
    /// </summary>
    public void SetLicenseResult(LicenseInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = AppInfo.ConNombreDeEmpresa(info).StatusLine;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;

            var brushKey = info.State switch
            {
                LicenseState.Valid => "SuccessBrush",
                LicenseState.Grace => "WarningBrush",
                _ => "DangerBrush"
            };

            if (TryFindResource(brushKey) is SolidColorBrush brush)
            {
                StatusText.Foreground = brush;
                Progress.Foreground = brush;
            }
        });
    }
}
