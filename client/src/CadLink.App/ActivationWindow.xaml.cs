using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CadLink.Licensing;

namespace CadLink.App;

/// <summary>
/// Pantalla de activación. Aparece cuando la validación silenciosa no alcanzó.
/// </summary>
/// <remarks>
/// Atiende dos públicos con la misma ventana:
/// <list type="bullet">
///   <item>
///     <b>Trabajadores de la oficina.</b> Normalmente nunca la ven: su PC se
///     activa sola por el SID del dominio. Si la ven es porque no había red, o
///     porque el equipo no está en el dominio. Copian la huella, la envían a
///     sistemas, y con el alta hecha basta "Reintentar activación automática".
///   </item>
///   <item>
///     <b>Clientes externos.</b> Escriben la clave de licencia que les vendiste.
///   </item>
///  </list>
/// </remarks>
public partial class ActivationWindow : Window
{
    private readonly LicenseService _service;

    /// <summary>Licencia obtenida si la activación tuvo éxito.</summary>
    public LicenseInfo? Result { get; private set; }

    public ActivationWindow(LicenseService service, LicenseInfo current)
    {
        _service = service;
        InitializeComponent();

        Title = $"{AppInfo.ProductName} — Activación";
        FingerprintBox.Text = service.FingerprintDisplay;
        SupportText.Text = AppInfo.SupportEmail;

        LogoImage.Source = Branding.Logo;
        Icon = Branding.Logo;

        if (!string.IsNullOrWhiteSpace(current.Message))
        {
            ReasonText.Text = current.Message;
        }

        // Si la suscripción venció, el camino correcto es la clave, no el reintento.
        if (current.State == LicenseState.Expired)
        {
            LicenseKeyBox.Focus();
        }
    }

    private void OnCopyFingerprint(object sender, RoutedEventArgs e)
    {
        try
        {
            // Se copia la huella en minúsculas sin guiones: es el formato que
            // espera el servidor, así se evita que el administrador tenga que
            // limpiarla a mano.
            Clipboard.SetText(_service.Fingerprint);
            ShowMessage("Huella copiada al portapapeles.", isError: false);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // El portapapeles puede estar bloqueado por otra aplicación.
            ShowMessage(
                "No se pudo copiar. Selecciona el texto de la huella y cópialo con Ctrl+C.",
                isError: true);
        }
    }

    private async void OnActivateWithKey(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowMessage("Escribe la clave de licencia que recibiste.", isError: true);
            LicenseKeyBox.Focus();
            return;
        }

        await RunActivationAsync(key).ConfigureAwait(true);
    }

    private async void OnActivateAutomatically(object sender, RoutedEventArgs e)
    {
        await RunActivationAsync(licenseKey: null).ConfigureAwait(true);
    }

    private async Task RunActivationAsync(string? licenseKey)
    {
        SetBusy(true);
        ShowMessage("Contactando al servidor de licencias…", isError: false);

        try
        {
            var info = await _service.ActivateAsync(licenseKey).ConfigureAwait(true);

            if (info.IsUsable)
            {
                Result = info;
                DialogResult = true;
                Close();
                return;
            }

            ShowMessage(
                string.IsNullOrWhiteSpace(info.Message) ? info.StatusLine : info.Message,
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ActivateButton.IsEnabled = !busy;
        AutoButton.IsEnabled = !busy;
        LicenseKeyBox.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        var key = isError ? "DangerBrush" : "SuccessBrush";
        if (TryFindResource(key) is SolidColorBrush brush)
        {
            MessageText.Foreground = brush;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
