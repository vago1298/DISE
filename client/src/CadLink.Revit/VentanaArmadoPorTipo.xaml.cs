using System.ComponentModel;
using System.Windows;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>
/// El cuadro «Armar por tipo». Solo enlaza: lo que se propone, lo que se puede marcar y el
/// resumen estan en <see cref="VistaArmadoPorTipo"/>, en el nucleo, que tiene pruebas.
/// </summary>
public partial class VentanaArmadoPorTipo : Window
{
    private readonly VistaArmadoPorTipo _vista;

    public VentanaArmadoPorTipo(VistaArmadoPorTipo vista)
    {
        _vista = vista;
        InitializeComponent();

        Reja.ItemsSource = _vista.Filas;
        RejaVarillas.ItemsSource = _vista.Varillas;
        _vista.PropertyChanged += OnCambio;

        Refrescar();
    }

    private void OnCambio(object? sender, PropertyChangedEventArgs e) => Refrescar();

    private void Refrescar()
    {
        ResumenText.Text = _vista.Resumen;
        ArmarButton.IsEnabled = _vista.PiezasAArmar > 0;
    }

    private void OnMarcarTodas(object sender, RoutedEventArgs e) => _vista.MarcarTodas(true);

    private void OnDesmarcarTodas(object sender, RoutedEventArgs e) => _vista.MarcarTodas(false);

    private void OnArmar(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
