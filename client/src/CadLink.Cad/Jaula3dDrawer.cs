using System.Globalization;

namespace CadLink.Cad;

/// <summary>
/// Dibuja la <b>jaula de armado en 3D</b> en AutoCAD: cada barra como un sólido de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Cada barra se hace <b>barriendo un círculo por su eje</b>: el círculo es la sección de la
/// varilla y el eje es el recorrido que ya calcula la vista previa —con sus dobleces, sus ganchos
/// y sus lapes—. El resultado son varillas que se pueden <b>seccionar, medir y acotar</b> en
/// AutoCAD, que es lo que hace que el dibujo sirva para trabajar y no solo para mirar. Es el mismo
/// criterio de <see cref="Modelo3dDrawer"/>, y por eso no se usan mallas.
/// </para>
/// <para>
/// <b>El perfil tiene que arrancar perpendicular al camino.</b> Barrer un círculo torcido da una
/// varilla de sección elíptica y más gruesa que la de la tabla. Y no es un caso raro: los estribos
/// van en un plano horizontal y las varillas suben, así que el círculo que AutoCAD crea por
/// omisión —en el plano XY— está bien para una y girado noventa grados para la otra. La cuenta de
/// la tangente está en <see cref="EjeDeBarra"/>, aparte y probada.
/// </para>
///
/// <para><b>EL PROBLEMA DE QUIÉN BORRA QUÉ, Y CÓMO SE RESUELVE</b></para>
/// <para>
/// Un barrido necesita dos objetos auxiliares: la <b>región</b> del perfil y la <b>polilínea</b>
/// del camino. Al terminar hay que quitarlos, y aquí está la trampa: <c>AddExtrudedSolid</c>
/// <b>consume</b> el perfil —se comprobó a base de cerrar AutoCAD—, y con el barrido por camino no
/// está claro si consume también el camino. Y el fallo no es benigno: llamar a <c>Delete()</c>
/// sobre un objeto COM ya destruido <b>no lanza excepción, se lleva AutoCAD por delante</b>, así
/// que no hay forma de capturarlo.
/// </para>
/// <para>
/// Así que no se adivina. Los auxiliares se crean en una <b>capa de trabajo</b> y al final se
/// recorre el espacio modelo <b>una vez</b> borrando lo que siga en ella. Lo que AutoCAD consumió
/// ya no está en el espacio modelo, así que no aparece y no se toca; lo que sobró aparece y se
/// borra. La corrección deja de depender de saber quién es el dueño de cada objeto, que es
/// justamente lo que no se puede comprobar sin AutoCAD delante.
/// </para>
/// <para>
/// Y si el barrido falla en una barra, esa barra <b>no se pierde</b>: se dibuja su eje como
/// polilínea 3D y se anota. Mejor una línea donde va la varilla que un hueco silencioso.
/// </para>
/// </remarks>
public sealed class Jaula3dDrawer
{
    /// <summary>Capa donde se dejan los auxiliares del barrido.</summary>
    /// <remarks>
    /// El nombre empieza por el guion bajo para que quede al principio de la lista de capas y se
    /// note si alguna vez queda algo dentro. En un dibujo terminado tiene que estar vacía.
    /// </remarks>
    public const string CapaDeTrabajo = "_CADLINK-TRABAJO";

    /// <summary>Menos que esto no es una barra: es un nudo mal leído.</summary>
    private const double LargoMinimo = 1e-6;

    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly List<string> _notas = new();

    /// <summary>Lo que hay que contarle al usuario del último dibujo.</summary>
    public IReadOnlyList<string> Notas => _notas;

    public Jaula3dDrawer(dynamic doc)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);

        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Una barra de la jaula, con su recorrido ya en coordenadas del dibujo.</summary>
    /// <remarks>
    /// El recorrido llega <b>ya en las unidades y los ejes de AutoCAD</b> —metros y Z arriba—.
    /// Convertir es cosa de quien llama, porque es quien sabe en qué convenio venía: la vista
    /// previa trabaja en centímetros y con la Y hacia arriba.
    /// </remarks>
    public sealed class Barra
    {
        /// <summary>El eje, punto a punto.</summary>
        public required List<(double X, double Y, double Z)> Eje { get; init; }

        /// <summary>Radio de la varilla, en unidades del dibujo.</summary>
        public required double Radio { get; init; }

        public string Capa { get; init; } = CapaVarillas;

        /// <summary>Para poder nombrarla en un aviso.</summary>
        public string Id { get; init; } = string.Empty;
    }

    /// <summary>Cuántas barras salieron sólidas y cuántas se quedaron en línea.</summary>
    public sealed record Resumen(int Solidos, int Lineas)
    {
        public override string ToString() =>
            $"{Solidos} varilla(s) sólidas"
            + (Lineas > 0 ? $" y {Lineas} en línea, que no se pudieron barrer" : string.Empty);
    }

    public const string CapaVarillas = "E-ACERO 3D";

    public const string CapaEstribos = "E-ESTRIBO 3D";

    /// <summary>Crea las capas de la jaula y la de trabajo.</summary>
    public void AsegurarCapas()
    {
        foreach (var (capa, color) in new[]
                 {
                     (CapaVarillas, 1),
                     (CapaEstribos, 5),
                     (CapaDeTrabajo, 8)
                 })
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic c = _doc.Layers.Add(capa);
                    c.Color = color;
                });
            }
            catch (Exception)
            {
                // Sin la capa el dibujo sigue saliendo, en la que esté activa.
            }
        }
    }

    /// <summary>Dibuja todas las barras y devuelve la cuenta.</summary>
    public Resumen Dibujar(IEnumerable<Barra> barras)
    {
        _notas.Clear();

        var solidos = 0;
        var lineas = 0;
        var cortas = 0;

        foreach (var b in barras)
        {
            var eje = EjeDeBarra.Limpio(b.Eje);

            var largo = EjeDeBarra.Largo(eje);

            if (eje.Count < 2 || largo < LargoMinimo || b.Radio <= 0)
            {
                cortas++;
                continue;
            }

            if (Solido(b, eje))
            {
                solidos++;
                continue;
            }

            // Respaldo: el eje. Mejor una línea donde va la varilla que un hueco.
            if (Eje3D(eje, b.Capa) is not null)
            {
                lineas++;

                _notas.Add(
                    $"La varilla '{b.Id}' no se pudo barrer —largo "
                    + largo.ToString("0.###", CultureInfo.InvariantCulture)
                    + " m— y se dibujó su eje.");
            }
        }

        if (cortas > 0)
        {
            _notas.Add(
                $"{cortas} recorrido(s) no daban para una varilla —sin largo o sin diámetro— y "
                + "no se dibujaron.");
        }

        LimpiarLaCapaDeTrabajo();

        return new Resumen(solidos, lineas);
    }

    /// <summary>Barre el círculo por el eje. <c>false</c> si no se pudo.</summary>
    private bool Solido(Barra b, List<(double X, double Y, double Z)> eje)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                // 1) EL CAMINO. Una polilínea 3D admite cualquier recorrido, que es lo que hace
                //    falta: el eje de un estribo con sus dobleces no está en ningún plano
                //    cómodo, y el de una varilla con su lape tampoco.
                dynamic camino = _ms.Add3DPoly(EjeDeBarra.Tira(eje));

                camino.Layer = CapaDeTrabajo;

                // Un estribo vuelve a su principio. Cerrarlo de verdad importa: sin esto el
                // barrido deja una muesca en la esquina donde empezó.
                if (EjeDeBarra.Cerrado(eje))
                {
                    camino.Closed = true;
                }

                // 2) EL PERFIL, PERPENDICULAR AL CAMINO. Ver la cabecera: torcido, la varilla
                //    sale elíptica y más gorda que la de la tabla.
                var (tx, ty, tz) = EjeDeBarra.TangenteInicial(eje);

                var p0 = eje[0];

                dynamic circulo = _ms.AddCircle(new[] { p0.X, p0.Y, p0.Z }, b.Radio);

                circulo.Layer = CapaDeTrabajo;
                circulo.Normal = new[] { tx, ty, tz };

                // El centro SE VUELVE A PONER después de la normal, y no es por gusto: al
                // cambiar el plano del círculo, AutoCAD reinterpreta su centro en el sistema
                // nuevo y la varilla arrancaría desplazada.
                circulo.Center = new[] { p0.X, p0.Y, p0.Z };

                // 3) LA REGIÓN: el barrido pide una región, no una curva.
                var regiones = _ms.AddRegion(new object[] { circulo });

                if (regiones is null || (int)regiones.Length < 1)
                {
                    return false;
                }

                dynamic region = regiones[0];

                region.Layer = CapaDeTrabajo;

                // 4) Y EL BARRIDO. No se borra nada aquí: quién es el dueño del perfil y del
                //    camino después de esto es justamente lo que no se puede comprobar sin
                //    AutoCAD delante, y equivocarse cierra el programa. Lo resuelve la capa de
                //    trabajo al final.
                dynamic solido = _ms.AddExtrudedSolidAlongPath(region, camino);

                solido.Layer = b.Capa;

                return true;
            });
        }
        catch (Exception)
        {
            // Un barrido puede fallar por geometría —un doblez más cerrado que el radio de la
            // varilla— y eso no es motivo para tirar el dibujo entero.
            return false;
        }
    }

    /// <summary>El eje como polilínea 3D, que es el respaldo cuando el barrido falla.</summary>
    private object? Eje3D(List<(double X, double Y, double Z)> eje, string capa)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.Add3DPoly(EjeDeBarra.Tira(eje));

                pl.Layer = capa;

                if (EjeDeBarra.Cerrado(eje))
                {
                    pl.Closed = true;
                }

                return (object?)pl;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Borra lo que haya quedado en la capa de trabajo, <b>sin adivinar quién es el dueño</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la pieza que hace segura toda esta clase, y está razonada en la cabecera. Se recorre el
    /// espacio modelo y se borra lo que siga en la capa de trabajo. Lo que AutoCAD consumió al
    /// barrer <b>ya no está en el espacio modelo</b>, así que no aparece en el recorrido y no se
    /// toca; lo que sobró aparece y se borra.
    /// </para>
    /// <para>
    /// Se recorre <b>una sola vez al final</b> y no por barra: recorrer el espacio modelo es
    /// caro, y con una jaula de cien varillas serían cien recorridos de un dibujo que puede
    /// tener miles de objetos.
    /// </para>
    /// <para>
    /// Y se recogen primero las entidades y se borran después. Borrar mientras se recorre una
    /// colección COM que se está modificando salta objetos, y ahí es donde quedaría un auxiliar
    /// suelto en el dibujo.
    /// </para>
    /// </remarks>
    private void LimpiarLaCapaDeTrabajo()
    {
        try
        {
            var sobras = AcadConnection.Retry(() =>
            {
                var lista = new List<object>();

                var cuantos = (int)_ms.Count;

                for (var i = 0; i < cuantos; i++)
                {
                    dynamic ent = _ms.Item(i);

                    if (string.Equals(
                            (string)ent.Layer, CapaDeTrabajo, StringComparison.OrdinalIgnoreCase))
                    {
                        lista.Add(ent);
                    }
                }

                return lista;
            });

            foreach (var ent in sobras)
            {
                try
                {
                    AcadConnection.Retry(() => { ((dynamic)ent).Delete(); });
                }
                catch (Exception)
                {
                    // Una sobra que no se puede borrar es un objeto de más en una capa que se
                    // puede apagar. No es motivo para nada.
                }
            }

            if (sobras.Count > 0)
            {
                _notas.Add(
                    $"Se limpiaron {sobras.Count} objeto(s) auxiliares del barrido de la capa "
                    + CapaDeTrabajo + ".");
            }
        }
        catch (Exception)
        {
            _notas.Add(
                "No se pudo recorrer el espacio modelo para limpiar la capa "
                + CapaDeTrabajo + ". Puedes borrarla a mano: solo lleva auxiliares.");
        }
    }
}
