using System.Collections.ObjectModel;
using System.ComponentModel;
using CadLink.Revit.Nucleo;

namespace CadLink.App.Models;

/// <summary>
/// Una conexion capturada a mano en la subpestaña «Capturar a mano»: los mismos datos de su hoja
/// del libro de acero, sin el libro.
/// </summary>
public sealed class ConexionAMano : INotifyPropertyChanged
{
    public ConexionAMano(string codigo, IReadOnlyDictionary<string, string>? valores = null)
    {
        Codigo = codigo;
        var defectos = ConexionesAMano.Defectos(codigo);

        foreach (var d in ConexionesAMano.Datos(codigo))
        {
            var v = valores is not null && valores.TryGetValue(d.Nombre, out var x) ? x : defectos.GetValueOrDefault(d.Nombre, string.Empty);
            var dato = new DatoAMano(d, v, ConexionesAMano.SeCalcula(codigo, d.Nombre));
            dato.PropertyChanged += (_, e) =>
            {
                if (dato.Nombre == "ID")
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Titulo)));
                }
            };
            Datos.Add(dato);
        }
    }

    public string Codigo { get; }

    public string Tipo => ConexionesDelLibro.Tipos.GetValueOrDefault(Codigo, Codigo);

    public ObservableCollection<DatoAMano> Datos { get; } = new();

    /// <summary>Como sale en la lista: su identificacion y su tipo.</summary>
    public string Titulo
    {
        get
        {
            var id = Datos.FirstOrDefault(d => d.Nombre == "ID")?.Valor ?? string.Empty;
            return (id.Trim().Length > 0 ? id.Trim() + "  ·  " : string.Empty) + Tipo;
        }
    }

    public Dictionary<string, string> Valores() => Datos.ToDictionary(d => d.Nombre, d => d.Valor ?? string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Un dato de una conexion capturada a mano.</summary>
public sealed class DatoAMano : INotifyPropertyChanged
{
    private string _valor;

    public DatoAMano(DatoDelLibro dato, string valor, bool seCalcula = false)
    {
        Dato = dato;
        _valor = valor;
        SeCalcula = seCalcula;
    }

    /// <summary>Si, dejado en blanco, CadLink lo calcula con el catalogo (como el libro con su formula).</summary>
    public bool SeCalcula { get; }

    public string Nota => SeCalcula ? "vacío = lo calcula CadLink" : string.Empty;

    public DatoDelLibro Dato { get; }

    public string Nombre => Dato.Nombre;

    public string Descripcion => Dato.Descripcion;

    public string Unidad => Dato.Unidad;

    public string Valor
    {
        get => _valor;
        set
        {
            if (_valor != value)
            {
                _valor = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Valor)));
            }
        }
    }

    /// <summary>Las opciones de la hoja; los perfiles y los aceros salen del catalogo de CadLink.</summary>
    public IReadOnlyList<string> Opciones
    {
        get
        {
            if (string.IsNullOrEmpty(Dato.Opciones))
            {
                return Array.Empty<string>();
            }

            var lista = new List<string>();

            foreach (var o in Dato.Opciones.Split('|'))
            {
                switch (o)
                {
                    case "@IR":
                    case "@OR":
                    case "@IS":
                    case "@L":
                        lista.AddRange(CatalogoPerfiles.NombresDe(o[1..]));
                        break;
                    case "@ACEROS":
                        lista.AddRange(CatalogoAceros.Nombres);
                        break;
                    default:
                        lista.Add(o);
                        break;
                }
            }

            return lista;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
