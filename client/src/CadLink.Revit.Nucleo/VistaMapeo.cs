using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CadLink.Revit.Nucleo;

/// <summary>Base para avisar de los cambios a la interfaz.</summary>
/// <remarks>
/// <see cref="INotifyPropertyChanged"/> esta en el BCL, no en WPF, asi que esto vive en el
/// nucleo y se puede probar sin Revit ni ventanas. Es lo que permite comprobar el
/// COMPORTAMIENTO del cuadro -que al cambiar de familia se repueblen los tipos, que el boton
/// se apague si falta algo- en vez de tener que abrirlo para verlo.
/// </remarks>
public abstract class Avisador : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Aviso([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}

/// <summary>Una fila del cuadro de mapeo.</summary>
public sealed class FilaVista : Avisador
{
    private string _familia = string.Empty;
    private TipoRevit? _tipo;

    internal FilaVista(SeccionDelModelo seccion, CatalogoRevit catalogo)
    {
        Seccion = seccion;
        Catalogo = catalogo;

        Familias = new ObservableCollection<string>(catalogo.FamiliasDe(seccion.Categoria));
        Tipos = new ObservableCollection<TipoRevit>();
    }

    public SeccionDelModelo Seccion { get; }

    private CatalogoRevit Catalogo { get; }

    /// <summary>Primera columna: <c>CC 15X25 (trabe)</c>.</summary>
    public string Etiqueta => Seccion.Etiqueta;

    public string Medidas => Seccion.Medidas;

    public int Cuantas => Seccion.Cuantas;

    public string Categoria => Categorias.Nombre(Seccion.Categoria);

    /// <summary>Las familias que se pueden elegir, las de la categoria de esta fila.</summary>
    public ObservableCollection<string> Familias { get; }

    /// <summary>Los tipos de la familia elegida.</summary>
    public ObservableCollection<TipoRevit> Tipos { get; }

    /// <summary>Lo que el emparejador propuso, para el tooltip.</summary>
    public string Sugerido { get; internal set; } = string.Empty;

    /// <summary>Si lo que hay puesto lo puso el emparejador y no la persona.</summary>
    public bool EsSugerencia { get; internal set; }

    public string Familia
    {
        get => _familia;
        set
        {
            var v = value ?? string.Empty;

            if (_familia == v)
            {
                return;
            }

            _familia = v;
            Aviso();

            RepoblarTipos();
        }
    }

    public TipoRevit? Tipo
    {
        get => _tipo;
        set
        {
            if (ReferenceEquals(_tipo, value))
            {
                return;
            }

            _tipo = value;

            // Elegir a mano deja de ser una sugerencia: importa para poder ensenar en el
            // cuadro que filas reviso una persona y cuales vienen propuestas.
            EsSugerencia = false;

            Aviso();
            Aviso(nameof(Mapeada));
        }
    }

    /// <summary>Si la fila ya tiene tipo elegido.</summary>
    public bool Mapeada => _tipo is not null;

    /// <summary>
    /// Vuelve a llenar los tipos con los de la familia elegida.
    /// </summary>
    /// <remarks>
    /// Al cambiar de familia, el tipo que estuviera puesto casi nunca pertenece a la nueva,
    /// asi que se descarta. Si se dejara, el cuadro mostraria "Familia A / Tipo de la B" y al
    /// modelar se usaria un tipo que no corresponde a lo que se ve.
    /// </remarks>
    private void RepoblarTipos()
    {
        Tipos.Clear();

        foreach (var t in Catalogo.TiposDe(Seccion.Categoria, _familia))
        {
            Tipos.Add(t);
        }

        if (_tipo is not null
            && !string.Equals(_tipo.Familia, _familia, StringComparison.CurrentCultureIgnoreCase))
        {
            _tipo = null;
            Aviso(nameof(Tipo));
            Aviso(nameof(Mapeada));
        }
    }

    /// <summary>Pone familia y tipo de una vez, sin perder el tipo por el camino.</summary>
    internal void Poner(TipoRevit tipo, bool esSugerencia, string porque)
    {
        _familia = tipo.Familia;
        Aviso(nameof(Familia));

        RepoblarTipos();

        // Se busca el MISMO objeto de la lista: los desplegables de WPF comparan por
        // referencia, y poner uno equivalente pero distinto dejaria el control en blanco.
        var enLista = Tipos.FirstOrDefault(t => t.Id == tipo.Id)
                      ?? Tipos.FirstOrDefault(t =>
                          string.Equals(t.Tipo, tipo.Tipo, StringComparison.CurrentCultureIgnoreCase));

        _tipo = enLista ?? tipo;
        EsSugerencia = esSugerencia;
        Sugerido = porque;

        Aviso(nameof(Tipo));
        Aviso(nameof(Mapeada));
        Aviso(nameof(Sugerido));
        Aviso(nameof(EsSugerencia));
    }
}

/// <summary>
/// El cuadro de mapeo: una fila por seccion del modelo, con su familia y su tipo de Revit.
/// </summary>
/// <remarks>
/// Es el modelo de la ventana, sin ventana. La ventana de WPF solo lo enlaza; toda la
/// decision -que se propone, que pasa al cambiar de familia, cuando se pueden pulsar los
/// botones- esta aqui y tiene pruebas.
/// </remarks>
public sealed class VistaMapeo : Avisador
{
    public VistaMapeo(ModeloJson modelo, CatalogoRevit catalogo, Mapeo? guardado = null)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        if (catalogo is null)
        {
            throw new ArgumentNullException(nameof(catalogo));
        }

        Modelo = modelo;
        Catalogo = catalogo;

        var secciones = Inventario.De(modelo);
        var sugerencias = Sugeridor.ParaTodas(secciones, catalogo);

        // Un mapeo guardado puede venir de otro proyecto, donde los tipos eran otros. Se
        // reata contra el catalogo de AHORA y lo que ya no exista se reporta.
        if (guardado is not null)
        {
            PerdidasDelMapeo.AddRange(guardado.Reatar(catalogo).Select(f =>
                f.Seccion + " -> " + f.Familia + " : " + f.Tipo));
        }

        foreach (var s in secciones)
        {
            var fila = new FilaVista(s, catalogo);

            var delMapeo = guardado?.TipoDe(s, catalogo);

            if (delMapeo is not null)
            {
                // Lo guardado MANDA sobre lo sugerido: es una decision que alguien ya tomo.
                fila.Poner(delMapeo, esSugerencia: false, "elegido en una importacion anterior");
            }
            else if (sugerencias.TryGetValue(s.Clave, out var sug) && sug.EsBuena && sug.Tipo is not null)
            {
                fila.Poner(sug.Tipo, esSugerencia: true, sug.Porque);
            }
            else
            {
                // Sin sugerencia buena se deja VACIO, con la pista a la vista. Aceptar una
                // propuesta floja sin mirarla produce un modelo con las secciones cambiadas,
                // y eso es peor que un cuadro que obliga a elegir.
                fila.Sugerido = sugerencias.TryGetValue(s.Clave, out var floja)
                    ? (floja.Tipo is null
                        ? floja.Porque
                        : "lo mas parecido seria " + floja.Tipo.NombreCompleto + ", pero no "
                          + "convence: " + floja.Porque)
                    : string.Empty;
            }

            fila.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FilaVista.Mapeada) or nameof(FilaVista.Tipo))
                {
                    Aviso(nameof(SinMapear));
                    Aviso(nameof(Mapeadas));
                    Aviso(nameof(PuedeModelar));
                    Aviso(nameof(Resumen));
                }
            };

            Filas.Add(fila);
        }

        Niveles = Nucleo.Niveles.Resolver(modelo, catalogo);
    }

    public ModeloJson Modelo { get; }

    public CatalogoRevit Catalogo { get; }

    public ObservableCollection<FilaVista> Filas { get; } = new();

    /// <summary>El emparejamiento de niveles, para ensenarlo antes de modelar.</summary>
    public Niveles Niveles { get; }

    /// <summary>Filas del mapeo guardado cuyo tipo ya no existe en este proyecto.</summary>
    public List<string> PerdidasDelMapeo { get; } = new();

    public int Total => Filas.Count;

    public int Mapeadas => Filas.Count(f => f.Mapeada);

    public int SinMapear => Filas.Count(f => !f.Mapeada);

    public int Sugeridas => Filas.Count(f => f.EsSugerencia);

    /// <summary>
    /// Si se puede modelar. Basta con que haya UNA fila mapeada.
    /// </summary>
    /// <remarks>
    /// No se exige que esten todas a proposito. En un modelo grande es normal querer traer
    /// primero las columnas y dejar los muros para despues; obligar a mapearlo todo de una
    /// vez convertiria una tarea que se puede hacer por partes en una que no se puede
    /// empezar. Lo que no esta mapeado se informa y se salta.
    /// </remarks>
    public bool PuedeModelar => Mapeadas > 0;

    public string Resumen()
    {
        if (Total == 0)
        {
            return "El modelo no trae ninguna seccion.";
        }

        var t = $"{Mapeadas} de {Total} seccion(es) con tipo elegido";

        if (Sugeridas > 0)
        {
            t += $", {Sugeridas} propuesta(s) automaticamente";
        }

        if (SinMapear > 0)
        {
            t += $". Las {SinMapear} sin elegir NO se modelaran";
        }

        return t + ".";
    }

    /// <summary>El mapeo resultante, para guardarlo y para armar el plan.</summary>
    public Mapeo AMapeo()
    {
        var m = new Mapeo { Obra = Modelo.Obra };

        foreach (var f in Filas)
        {
            if (f.Tipo is not null)
            {
                m.Poner(f.Seccion, f.Tipo);
            }
        }

        return m;
    }

    /// <summary>Acepta todas las sugerencias que quedaron sin poner por flojas.</summary>
    /// <remarks>
    /// Existe para el caso en que alguien mira la lista, ve que las propuestas flojas son
    /// correctas de todos modos, y no quiere elegirlas una por una. Es un acto explicito: no
    /// pasa nunca solo.
    /// </remarks>
    public int AceptarLoMasParecido()
    {
        var puestas = 0;

        foreach (var f in Filas.Where(x => !x.Mapeada))
        {
            var s = Sugeridor.Para(f.Seccion, Catalogo);

            if (s.Tipo is null)
            {
                continue;
            }

            f.Poner(s.Tipo, esSugerencia: true, s.Porque);
            puestas++;
        }

        if (puestas > 0)
        {
            Aviso(nameof(SinMapear));
            Aviso(nameof(Mapeadas));
            Aviso(nameof(PuedeModelar));
        }

        return puestas;
    }
}
