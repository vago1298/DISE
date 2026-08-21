using System.Text.Json;

namespace CadLink.App.Models;

/// <summary>
/// Una <b>instantánea</b> de todo lo capturado: lo que hace falta para poder volver atrás.
/// </summary>
/// <remarks>
/// <para>
/// <b>Guarda el trabajo en el MISMO formato del archivo <c>.clk</c></b> —un
/// <see cref="ProyectoGuardado"/> serializado a JSON—, y no una lista de «qué cambió». Es una
/// decisión que conviene explicar, porque el camino obvio es el otro.
/// </para>
/// <para>
/// Con una lista de cambios habría que interceptar <b>cada</b> sitio que toca los datos: las
/// celdas de cinco cuadrículas, los botones de agregar y quitar fila, el catálogo que trae las
/// medidas del perfil solo, el elemento que ajusta el f'c… Cada camino nuevo que alguien
/// agregue y se olvide de registrar deja un cambio que no se puede deshacer, y eso no se nota
/// probando: se nota el día que hace falta.
/// </para>
/// <para>
/// Con la instantánea entera no hay nada que interceptar: se guarda cómo estaba todo y se
/// vuelve a poner. Es más memoria —unos cientos de kB por paso, y se guardan
/// <see cref="Historial.MaximoPasos"/>— y a cambio no hay manera de que un cambio quede fuera.
/// Y reusa el guardado y la apertura del archivo, que es código ya probado: si un trabajo se
/// puede guardar y volver a abrir, se puede deshacer.
/// </para>
/// <para>
/// <b>Las secciones de acero van aparte.</b> El archivo <c>.clk</c> todavía no las guarda —es
/// un hueco del formato, no de esto— así que la instantánea las clona por su cuenta. Si no, al
/// deshacer un cambio del concreto se habría borrado la hoja de acero entera, que es
/// exactamente el tipo de sorpresa que un deshacer no puede dar.
/// </para>
/// </remarks>
public sealed class Instantanea
{
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = false };

    private readonly string _json;
    private readonly List<PerfilAceroRow> _acero;

    /// <summary>Toma la instantánea. El JSON se serializa aquí, no al deshacer.</summary>
    /// <remarks>
    /// Se serializa <b>al tomarla</b> a propósito: así queda una copia inmutable. Guardando el
    /// objeto vivo, el siguiente cambio en la cuadrícula lo modificaría también a él y el
    /// «deshacer» devolvería al estado actual, o sea a nada.
    /// </remarks>
    public Instantanea(ProyectoGuardado proyecto, IEnumerable<PerfilAceroRow> acero)
    {
        _json = JsonSerializer.Serialize(proyecto, Opciones);
        _acero = acero.Select(p => p.Copia()).ToList();
    }

    /// <summary>Qué era lo que se estaba haciendo, para poder decirlo en la barra.</summary>
    public string Que { get; init; } = string.Empty;

    /// <summary>El trabajo tal como estaba. Cada llamada devuelve una copia nueva.</summary>
    public ProyectoGuardado Proyecto =>
        JsonSerializer.Deserialize<ProyectoGuardado>(_json, Opciones) ?? new ProyectoGuardado();

    /// <summary>Las filas de acero tal como estaban, otra vez clonadas.</summary>
    public List<PerfilAceroRow> Acero => _acero.Select(p => p.Copia()).ToList();

    /// <summary>Si esta instantánea guarda lo mismo que la otra.</summary>
    /// <remarks>
    /// Sirve para no apilar un paso cuando no cambió nada. Pasa más de lo que parece: entrar y
    /// salir de una celda sin escribir, o reordenar la cuadrícula, avisan de un cambio que no
    /// lo es, y sin esto el usuario tendría que pulsar Ctrl+Z tres veces para deshacer una cosa.
    /// </remarks>
    public bool EsIgualA(Instantanea? otra) =>
        otra is not null
        && string.Equals(_json, otra._json, StringComparison.Ordinal)
        && _acero.Count == otra._acero.Count
        && !_acero.Where((p, i) => !p.EsIgualA(otra._acero[i])).Any();
}

/// <summary>
/// El <b>historial de deshacer</b>: una pila de instantáneas con tope.
/// </summary>
/// <remarks>
/// <para>
/// Solo <b>deshacer</b>, no rehacer. Es lo que se pidió, y el rehacer tiene una regla que hay
/// que decidir con cuidado —¿qué pasa con la pila de rehacer cuando, después de deshacer, se
/// escribe algo nuevo?—; ponerlo a medias es peor que no tenerlo.
/// </para>
/// <para>
/// El tope existe porque cada paso es una copia del trabajo entero. Treinta pasos cubren de
/// sobra «me equivoqué y quiero volver», que es para lo que sirve, sin que el programa se coma
/// la memoria en una sesión larga.
/// </para>
/// </remarks>
public sealed class Historial
{
    /// <summary>Cuántos pasos atrás se pueden deshacer.</summary>
    public const int MaximoPasos = 30;

    private readonly LinkedList<Instantanea> _pasos = new();

    /// <summary>Cuántos pasos hay guardados.</summary>
    public int Cuantos => _pasos.Count;

    /// <summary>Si hay algo que deshacer.</summary>
    public bool Puede => _pasos.Count > 0;

    /// <summary>Qué se deshace si se pulsa ahora, para poder decirlo en la barra.</summary>
    public string Siguiente => _pasos.Last?.Value.Que ?? string.Empty;

    /// <summary>Apila un paso. Si se pasa del tope, se olvida el más viejo.</summary>
    public void Apilar(Instantanea paso)
    {
        // No se apila un paso que guarda lo mismo que el de arriba: ahorra pulsaciones y
        // evita que el historial se llene de pasos que no cambian nada.
        if (paso.EsIgualA(_pasos.Last?.Value))
        {
            return;
        }

        _pasos.AddLast(paso);

        while (_pasos.Count > MaximoPasos)
        {
            _pasos.RemoveFirst();
        }
    }

    /// <summary>Saca el último paso, o <c>null</c> si no hay.</summary>
    public Instantanea? Deshacer()
    {
        var ultimo = _pasos.Last;

        if (ultimo is null)
        {
            return null;
        }

        _pasos.RemoveLast();

        return ultimo.Value;
    }

    /// <summary>Vacía el historial. Se usa al abrir otro trabajo o empezar de cero.</summary>
    /// <remarks>
    /// Hace falta, y es lo correcto: deshacer después de abrir otro archivo devolvería al
    /// trabajo anterior sin avisar, y el usuario creería que le acaba de deshacer un cambio
    /// cuando en realidad le cambió el archivo entero.
    /// </remarks>
    public void Limpiar() => _pasos.Clear();
}
