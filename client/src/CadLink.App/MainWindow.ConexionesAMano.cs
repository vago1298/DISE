using System.Windows;
using System.Windows.Controls;
using CadLink.App.Models;
using CadLink.Revit.Nucleo;

namespace CadLink.App;

/// <summary>
/// La subpestaña «Capturar a mano» de Conexiones/Detalles: las mismas conexiones del libro de
/// acero, sin el libro. Se capturan los datos de su hoja y se dibujan con el mismo camino que las
/// leidas del Excel (<see cref="DibujarConexiones"/>).
/// </summary>
/// <remarks>
/// Lo que el libro calcula con sus formulas -las medidas de los perfiles, el angulo de la
/// diagonal, el tipo de eslabon…- lo completa el nucleo (<see cref="ConexionesAMano"/>) con el
/// catalogo de perfiles de CadLink.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Un renglon del desplegable de tipos.</summary>
    private sealed record TipoConexionAMano(string Codigo, string Nombre);

    private void EnlazarConexionesAMano()
    {
        // Se vuelve a llamar al empezar un trabajo nuevo, y entonces _datos es OTRO objeto: se
        // enlaza su coleccion, y solo una vez.
        if (TipoConexionAManoCombo is null || ReferenceEquals(ConexionesAManoLista.ItemsSource, _datos.ConexionesAMano))
        {
            return;
        }

        if (TipoConexionAManoCombo.ItemsSource is null)
        {
            TipoConexionAManoCombo.ItemsSource = ConexionesAMano.Codigos
                .Select(c => new TipoConexionAMano(c, ConexionesDelLibro.Tipos.GetValueOrDefault(c, c)))
                .ToList();
            TipoConexionAManoCombo.SelectedIndex = 0;
        }

        ConexionesAManoLista.ItemsSource = _datos.ConexionesAMano;
        DatosConexionAManoGrid.ItemsSource = null;
        _datos.ConexionesAMano.CollectionChanged += (_, e) =>
        {
            foreach (var c in e.NewItems?.OfType<ConexionAMano>() ?? Enumerable.Empty<ConexionAMano>())
            {
                foreach (var d in c.Datos)
                {
                    d.PropertyChanged += (_, _) =>
                    {
                        RegistrarEnHistorial();

                        if (ReferenceEquals(ConexionesAManoLista.SelectedItem, c))
                        {
                            PedirVistaPreviaConexionAMano();
                        }
                    };
                }
            }

            RegistrarEnHistorial();
            ResumenConexionesAMano();
        };

        ResumenConexionesAMano();
    }

    private void OnAgregarConexionAMano(object sender, RoutedEventArgs e)
    {
        if (TipoConexionAManoCombo.SelectedItem is not TipoConexionAMano tipo)
        {
            return;
        }

        try
        {
            var nueva = new ConexionAMano(tipo.Codigo);
            _datos.ConexionesAMano.Add(nueva);
            ConexionesAManoLista.SelectedItem = nueva;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo agregar la conexion.\n\n" + ex.Message, AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDuplicarConexionAMano(object sender, RoutedEventArgs e)
    {
        if (ConexionesAManoLista.SelectedItem is not ConexionAMano c)
        {
            return;
        }

        var copia = new ConexionAMano(c.Codigo, c.Valores());
        _datos.ConexionesAMano.Insert(_datos.ConexionesAMano.IndexOf(c) + 1, copia);
        ConexionesAManoLista.SelectedItem = copia;
    }

    private void OnQuitarConexionAMano(object sender, RoutedEventArgs e)
    {
        if (ConexionesAManoLista.SelectedItem is not ConexionAMano c)
        {
            return;
        }

        var i = _datos.ConexionesAMano.IndexOf(c);
        _datos.ConexionesAMano.Remove(c);

        if (_datos.ConexionesAMano.Count > 0)
        {
            ConexionesAManoLista.SelectedIndex = Math.Min(i, _datos.ConexionesAMano.Count - 1);
        }
    }

    private void OnConexionAManoElegida(object sender, SelectionChangedEventArgs e)
    {
        DatosConexionAManoGrid.ItemsSource = (ConexionesAManoLista.SelectedItem as ConexionAMano)?.Datos;
        PedirVistaPreviaConexionAMano();
    }

    private void OnConexionAManoPreviewSizeChanged(object sender, SizeChangedEventArgs e) => PedirVistaPreviaConexionAMano();

    private void ResumenConexionesAMano()
    {
        var n = _datos.ConexionesAMano.Count;
        ConexionesAManoNotas.Text = n == 0
            ? "Elige un tipo y pulsa «Agregar». Los perfiles se eligen del catalogo; si escribes uno que no este, sus medidas salen en cero."
            : $"{n} conexion(es) capturada(s). Se dibujan todas, un renglon por tipo, a la izquierda de X = {XDeLosDetalles:0}.";
    }

    private void OnDibujarConexionesAMano(object sender, RoutedEventArgs e)
    {
        // Lo que se este escribiendo en la celda abierta tambien cuenta.
        DatosConexionAManoGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var elegidas = new List<(ModeloConexion M, HojaLeida H)>();
        var problemas = new List<string>();

        foreach (var c in _datos.ConexionesAMano)
        {
            try
            {
                var (m, h) = ConexionesAMano.Armar(c.Codigo, c.Titulo, c.Valores(), PerfilDelCatalogo, out var porQueNo);

                if (m is null)
                {
                    problemas.Add($"«{c.Titulo}»: {porQueNo}");
                }
                else
                {
                    elegidas.Add((m, h));
                }
            }
            catch (Exception ex)
            {
                problemas.Add($"«{c.Titulo}»: {ex.Message}");
            }
        }

        if (problemas.Count > 0)
        {
            ConexionesAManoNotas.Text = "No se pudieron armar: " + string.Join(" · ", problemas);
        }

        if (elegidas.Count == 0)
        {
            MessageBox.Show(this,
                problemas.Count == 0 ? "Agrega al menos una conexion." : ConexionesAManoNotas.Text,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DibujarConexiones(elegidas);
    }

    /// <summary>
    /// Las medidas de un perfil del catalogo de CadLink por su nombre: el mismo nombre IMCA que
    /// usa el libro («W - 18'' x 50.06 lb/ft», «HSS - 6" x 1/2"»…).
    /// </summary>
    private static PerfilDeCatalogo? PerfilDelCatalogo(string nombre)
    {
        var n = (nombre ?? string.Empty).Trim();

        if (n.Length == 0)
        {
            return null;
        }

        var p = CatalogoPerfiles.Todos.FirstOrDefault(
            x => string.Equals(x.Nombre, n, StringComparison.OrdinalIgnoreCase));

        if (p is null)
        {
            return null;
        }

        var tubo = p.Familia == FamiliaPerfil.Or;

        // En los tubos el catalogo trae la pared como «alma» y deja el patin en cero.
        var tf = tubo || p.EspesorPatinCm <= 0 ? p.EspesorAlmaCm : p.EspesorPatinCm;
        return new PerfilDeCatalogo(p.PeralteCm, p.AnchoCm, tf, p.EspesorAlmaCm, p.Props.ZxCm3 ?? 0, tubo);
    }
}
