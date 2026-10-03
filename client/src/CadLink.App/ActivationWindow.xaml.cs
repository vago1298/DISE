using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    /// <summary>
    /// Mientras el equipo espera a que el dueño lo apruebe -paquete de oficina-, se vuelve a
    /// preguntar solo cada pocos segundos: en cuanto lo aprueba, esta ventana entra sola y el
    /// trabajador no tiene que pulsar nada.
    /// </summary>
    private readonly DispatcherTimer _espera = new() { Interval = TimeSpan.FromSeconds(15) };

    /// <summary>Lo que dice el servidor de una PC del paquete de oficina que espera aprobación.</summary>
    private const string SenalDeEspera = "ESPERANDO APROBACIÓN";

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
        Icon = Branding.Icono;

        if (!string.IsNullOrWhiteSpace(current.Message))
        {
            ReasonText.Text = current.Message;
        }

        _espera.Tick += async (_, _) =>
        {
            _espera.Stop();
            await RunActivationAsync(licenseKey: null).ConfigureAwait(true);
        };
        Closed += (_, _) => _espera.Stop();

        EsperarSiToca(current.Message);

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

            var texto = string.IsNullOrWhiteSpace(info.Message) ? info.StatusLine : info.Message;
            var esperando = EsperarSiToca(info.Message);

            ShowMessage(
                esperando ? texto + "\n\nSe vuelve a preguntar solo cada 15 segundos." : texto,
                isError: !esperando);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Si el servidor dijo que este equipo espera aprobación, vuelve a preguntar solo.</summary>
    private bool EsperarSiToca(string? mensaje)
    {
        var esperando = !string.IsNullOrWhiteSpace(mensaje)
                        && mensaje.Contains(SenalDeEspera, StringComparison.OrdinalIgnoreCase);

        if (esperando)
        {
            _espera.Stop();
            _espera.Start();
        }

        return esperando;
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
