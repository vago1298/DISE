using System.ComponentModel;
using System.Windows;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>El cuadro de mapeo de secciones.</summary>
/// <remarks>
/// A proposito no tiene logica: enlaza <see cref="VistaMapeo"/> y devuelve que boton se pulso.
/// Todo lo que decide esta en el nucleo, que se puede probar sin Revit ni ventanas.
/// </remarks>
public partial class VentanaMapeo : Window
{
    private readonly VistaMapeo _vista;

    public VentanaMapeo(VistaMapeo vista)
    {
        _vista = vista ?? throw new ArgumentNullException(nameof(vista));

        InitializeComponent();

        Reja.ItemsSource = _vista.Filas;

        // El resumen se refresca cuando cambia cualquier fila: es lo que dice cuantas
        // secciones quedan sin elegir, y es el dato que decide si merece la pena pulsar ya.
        _vista.PropertyChanged += (_, _) => Refrescar();

        foreach (var f in _vista.Filas)
        {
            f.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FilaVista.Mapeada) or nameof(FilaVista.Tipo))
                {
                    Refrescar();
                }
            };
        }

        NivelesText.Text = "Niveles: " + _vista.Niveles.Explicacion()
                           + (_vista.Niveles.Faltan.Count > 0
                               ? ". Se crearan los que faltan: "
                                 + string.Join(", ", _vista.Niveles.Faltan.Select(n =>
                                     n.Nombre + " a " + Niveles.Cm(n.ElevacionM) + " cm"))
                               : string.Empty);

        Refrescar();
    }

    /// <summary>Que boton se pulso, o <c>null</c> si se cancelo.</summary>
    public Modo? Eleccion { get; private set; }

    private void Refrescar()
    {
        var texto = _vista.Resumen();

        if (_vista.PerdidasDelMapeo.Count > 0)
        {
            texto += "\nDel mapeo anterior, "
                     + _vista.PerdidasDelMapeo.Count
                     + " tipo(s) ya no existen en este proyecto: "
                     + string.Join("; ", _vista.PerdidasDelMapeo.Take(3))
                     + (_vista.PerdidasDelMapeo.Count > 3 ? "..." : string.Empty);
        }

        ResumenText.Text = texto;

        NuevosButton.IsEnabled = _vista.PuedeModelar;
        ActualizarButton.IsEnabled = _vista.PuedeModelar;
        ParecidoButton.IsEnabled = _vista.SinMapear > 0;
    }

    private void OnAceptarLoMasParecido(object sender, RoutedEventArgs e)
    {
        var puestas = _vista.AceptarLoMasParecido();

        if (puestas == 0)
        {
            MessageBox.Show(this,
                "No hay ningun tipo parecido que ofrecer para las filas que faltan. "
                + "Seguramente hace falta cargar una familia con esa forma.",
                "CadLink", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // La reja no se enlaza a una lista nueva, se modifican las filas, asi que hay que
        // pedirle que vuelva a pintar.
        Reja.Items.Refresh();
        Refrescar();
    }

    private void OnModelarNuevos(object sender, RoutedEventArgs e) => Cerrar(Modo.ModelarNuevos);

    private void OnActualizarExistentes(object sender, RoutedEventArgs e) =>
        Cerrar(Modo.ActualizarExistentes);

    private void Cerrar(Modo modo)
    {
        if (_vista.SinMapear > 0)
        {
            var r = MessageBox.Show(this,
                $"Quedan {_vista.SinMapear} seccion(es) sin tipo elegido y NO se modelaran.\n\n"
                + "¿Seguir de todos modos?",
                "CadLink", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Eleccion = modo;
        DialogResult = true;
        Close();
    }
}
