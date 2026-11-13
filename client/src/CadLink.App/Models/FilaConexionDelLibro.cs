using System.ComponentModel;
using CadLink.Revit.Nucleo;

namespace CadLink.App.Models;

/// <summary>
/// Un renglon de la lista «Conexiones desde el libro de Excel»: una hoja del libro de acero y si
/// se dibuja.
/// </summary>
/// <remarks>
/// No se guarda en el <c>.clk</c>: los datos viven en el libro de Excel, y la lista se vuelve a
/// leer de el cada vez. Guardarla seria tener dos copias de las medidas que pueden separarse.
/// </remarks>
public sealed class FilaConexionDelLibro : INotifyPropertyChanged
{
    private bool _dibujar;

    public FilaConexionDelLibro(ConexionDelLibro c)
    {
        Conexion = c;

        // De fabrica se dibuja lo que se puede dibujar y es del proyecto: no las hojas de ejemplo.
        _dibujar = c.SeDibuja && !c.Hoja.DeEjemplo;
    }

    public ConexionDelLibro Conexion { get; }

    public bool Dibujar
    {
        get => _dibujar;
        set
        {
            if (_dibujar != value)
            {
                _dibujar = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dibujar)));
            }
        }
    }

    public bool SePuedeDibujar => Conexion.SeDibuja;

    public string Hoja => Conexion.Hoja.Hoja.Trim();

    public string Tipo => Conexion.Tipo;

    public string Identificacion => Conexion.Hoja.Id;

    /// <summary>Lo que dice la hoja de sus revisiones y si es de ejemplo.</summary>
    public string Estado
    {
        get
        {
            var h = Conexion.Hoja;
            var partes = new List<string>();

            partes.Add(h.NoCumplen > 0 ? $"{h.NoCumplen} NO CUMPLE(N): {string.Join("; ", h.Fallas)}" : "Cumple");

            if (h.ConError > 0)
            {
                partes.Add($"{h.ConError} con error de Excel");
            }

            if (h.RequierenAtiesador > 0)
            {
                partes.Add($"{h.RequierenAtiesador} requieren atiesador");
            }

            if (h.DeEjemplo)
            {
                partes.Add("datos de ejemplo");
            }

            return string.Join(" · ", partes);
        }
    }

    /// <summary>Que se dibuja: las piezas, o por que no.</summary>
    public string Piezas
    {
        get
        {
            var m = Conexion.Modelo;

            if (m is null)
            {
                return Conexion.PorQueNo ?? "No se dibuja.";
            }

            if (m.PlacaBase is { } pb)
            {
                return $"Placa base con {pb.Anclas.Count} anclas (se dibuja con el detalle de la hoja Placa Base)";
            }

            return $"{m.Placas.Count} placas, {m.Cordones.Count} cordones de soldadura, {m.Tornillos.Count} tornillos";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
