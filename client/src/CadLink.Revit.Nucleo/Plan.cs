using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>
/// La marca con que se reconoce una pieza puesta por CadLink.
/// </summary>
/// <remarks>
/// <para>
/// Se escribe en el parametro <b>Comentarios</b> de cada pieza creada. No en
/// <b>Marca</b>, que seria lo natural: Revit avisa de marcas repetidas y llenaria la
/// pantalla de advertencias en cuanto dos piezas compartieran etiqueta.
/// </para>
/// <para>
/// La llave lleva el NIVEL ademas de la etiqueta, porque las etiquetas de ETABS se repiten
/// de un nivel a otro: la columna "C1" existe en todos los pisos. Sin el nivel, la segunda
/// vez que se importara se creerian todas la misma pieza.
/// </para>
/// </remarks>
public static class Llave
{
    public const string Prefijo = "CadLink";

    public static string De(ClasePieza clase, string? etiqueta, string? nivel) =>
        string.Join("|",
            Prefijo,
            clase.ToString(),
            (etiqueta ?? string.Empty).Trim(),
            (nivel ?? string.Empty).Trim());

    public static string De(BarraJson b) => De(b.Clase, b.Etiqueta, b.Nivel);

    public static string De(PanoJson p) => De(p.Clase, p.Etiqueta, p.Nivel);

    /// <summary>Si un comentario de Revit es una llave nuestra.</summary>
    public static bool EsNuestra(string? comentario) =>
        comentario is not null
        && comentario.StartsWith(Prefijo + "|", StringComparison.Ordinal);
}

/// <summary>Una pieza que YA esta en el modelo de Revit, puesta por CadLink.</summary>
public sealed class PiezaExistente
{
    /// <summary>El <c>ElementId</c> de la pieza.</summary>
    public long Id { get; set; }

    /// <summary>La llave leida de su parametro de comentarios.</summary>
    public string Llave { get; set; } = string.Empty;

    /// <summary>El <c>ElementId</c> del tipo que tiene ahora.</summary>
    public long TipoId { get; set; }

    /// <summary>Donde esta ahora, si el complemento lo pudo leer. En metros.</summary>
    public PuntoJson? P1 { get; set; }

    public PuntoJson? P2 { get; set; }
}

/// <summary>Que hacer con una pieza.</summary>
public enum Accion
{
    /// <summary>No esta en Revit: hay que crearla.</summary>
    Crear,

    /// <summary>Esta, pero con otro tipo o en otro sitio.</summary>
    Actualizar,

    /// <summary>Esta y coincide: no se toca.</summary>
    DejarIgual,

    /// <summary>Su seccion no tiene tipo elegido en el cuadro.</summary>
    SinMapeo,

    /// <summary>Esta en Revit con marca de CadLink y ya no esta en el modelo.</summary>
    Sobra
}

/// <summary>Una linea del plan.</summary>
public sealed class Paso
{
    public required string Llave { get; init; }

    public required Accion Accion { get; init; }

    /// <summary>La barra, si el paso es de una barra.</summary>
    public BarraJson? Barra { get; init; }

    /// <summary>El pano, si el paso es de un pano.</summary>
    public PanoJson? Pano { get; init; }

    /// <summary>El tipo de Revit que hay que poner. Nulo en <see cref="Accion.SinMapeo"/> y <see cref="Accion.Sobra"/>.</summary>
    public TipoRevit? Tipo { get; init; }

    /// <summary>El nivel de Revit donde va, por nombre.</summary>
    public string NivelRevit { get; init; } = string.Empty;

    /// <summary>El <c>ElementId</c> de la pieza que ya existe, o 0.</summary>
    public long IdExistente { get; init; }

    /// <summary>En palabras, por que este paso. Va al informe.</summary>
    public string Motivo { get; init; } = string.Empty;

    public ClasePieza Clase => Barra?.Clase ?? Pano?.Clase ?? ClasePieza.Trabe;
}

/// <summary>Lo que se va a hacer, antes de hacerlo.</summary>
/// <remarks>
/// Se arma completo y se cuenta ANTES de abrir la transaccion de Revit. Asi se le puede
/// decir al usuario "voy a crear 412 y a cambiar 33" y que decida, en vez de enterarse
/// cuando ya esta hecho.
/// </remarks>
public sealed class Plan
{
    public List<Paso> Pasos { get; } = new();

    /// <summary>Niveles del modelo que no existen en Revit y habria que crear.</summary>
    public List<NivelJson> NivelesQueFaltan { get; } = new();

    /// <summary>
    /// El modelo del que salio el plan, para lo que no es pieza por pieza.
    /// </summary>
    /// <remarks>
    /// Lo necesita la malla de ejes: para saber hasta donde llega cada linea hay que mirar toda
    /// la geometria, no un paso.
    /// </remarks>
    public ModeloJson? Modelo { get; set; }

    /// <summary>La cuadricula de ejes, ya colocada, si el modelo la trae.</summary>
    public CuadriculaJson? Cuadricula => Modelo?.Cuadricula;

    /// <summary>Avisos no fatales.</summary>
    public List<string> Avisos { get; } = new();

    /// <summary>
    /// Piezas que compartian etiqueta y nivel con otra y se distinguieron por su posicion.
    /// </summary>
    /// <remarks>
    /// Es normal que sea alto en un modelo con piers: un pier agrupa varios paños. Lo que
    /// importa es que estas piezas SI se modelan; antes se descartaban.
    /// </remarks>
    public int Desempatadas { get; set; }

    /// <summary>Piezas descartadas por ser otra pieza igual en el mismo sitio.</summary>
    public int Duplicadas { get; set; }

    public int Cuantos(Accion a) => Pasos.Count(p => p.Accion == a);

    public int Crear => Cuantos(Accion.Crear);

    public int Actualizar => Cuantos(Accion.Actualizar);

    public int DejarIgual => Cuantos(Accion.DejarIgual);

    public int SinMapeo => Cuantos(Accion.SinMapeo);

    public int Sobra => Cuantos(Accion.Sobra);

    /// <summary>Si hay algo que tocar en Revit.</summary>
    public bool HayTrabajo => Crear > 0 || Actualizar > 0;

    public string Resumen()
    {
        var partes = new List<string>();

        if (Crear > 0)
        {
            partes.Add($"crear {Crear}");
        }

        if (Actualizar > 0)
        {
            partes.Add($"actualizar {Actualizar}");
        }

        if (DejarIgual > 0)
        {
            partes.Add($"dejar igual {DejarIgual}");
        }

        if (SinMapeo > 0)
        {
            partes.Add($"{SinMapeo} sin tipo elegido");
        }

        if (Sobra > 0)
        {
            partes.Add($"{Sobra} que ya no estan en el modelo");
        }

        return partes.Count == 0 ? "no hay nada que hacer" : string.Join(", ", partes);
    }
}

/// <summary>Que boton se pulso.</summary>
public enum Modo
{
    /// <summary>
    /// Crear lo que falta y no tocar lo que ya esta.
    /// </summary>
    /// <remarks>
    /// Es lo que se quiere la primera vez, y tambien cuando el calculo crecio y solo hay
    /// piezas nuevas. Lo que ya esta en Revit puede llevar trabajo encima -conexiones,
    /// anotaciones, ajustes a mano- y machacarlo sin que nadie lo pida seria destructivo.
    /// </remarks>
    ModelarNuevos,

    /// <summary>
    /// Re-tipar y recolocar lo que ya esta, y NO crear nada nuevo.
    /// </summary>
    /// <remarks>
    /// Es para despues de cambiar secciones en ETABS: el modelo de Revit ya esta armado y lo
    /// que hace falta es que las piezas adopten la seccion nueva. No crea nada a proposito,
    /// para que sea una operacion acotada y facil de deshacer.
    /// </remarks>
    ActualizarExistentes
}

/// <summary>Arma el plan.</summary>
public static class Planificador
{
    /// <summary>Cuanto se puede mover una pieza sin considerarla movida. 5 mm.</summary>
    public const double ToleranciaM = 0.005;

    public static Plan Armar(
        ModeloJson modelo,
        Mapeo mapeo,
        CatalogoRevit catalogo,
        IEnumerable<PiezaExistente>? existentes,
        Modo modo)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        var plan = new Plan { Modelo = modelo };
        var secciones = Inventario.De(modelo).ToDictionary(s => s.Clave, StringComparer.Ordinal);

        var yaEsta = new Dictionary<string, PiezaExistente>(StringComparer.Ordinal);

        foreach (var e in existentes ?? Enumerable.Empty<PiezaExistente>())
        {
            if (!Llave.EsNuestra(e.Llave))
            {
                continue;
            }

            // Con la llave repetida se queda la primera y se avisa: es sintoma de que
            // alguien copio una pieza a mano, y actualizar solo una de las dos dejaria el
            // modelo incoherente sin decirlo.
            if (!yaEsta.TryAdd(e.Llave, e))
            {
                plan.Avisos.Add(
                    $"«{e.Llave}»: hay mas de una pieza en Revit con esta marca, se actualizara "
                    + "solo una; revisa si se duplico a mano");
            }
        }

        var niveles = Niveles.Resolver(modelo, catalogo);
        plan.NivelesQueFaltan.AddRange(niveles.Faltan);

        var vistas = new HashSet<string>(StringComparer.Ordinal);

        // Las llaves con la posicion pegada, de TODAS las piezas. Es lo que distingue una pieza
        // repetida de verdad -misma etiqueta, mismo nivel y mismo sitio- de dos piezas que solo
        // comparten la etiqueta, como los trozos de un muro mallado bajo un mismo pier.
        var sitios = new HashSet<string>(StringComparer.Ordinal);

        void Uno(string llave, ClasePieza clase, SeccionJson seccion, string nivelModelo,
                 BarraJson? barra, PanoJson? pano)
        {
            // ---- Llave repetida: se DESEMPATA por geometria, no se descarta la pieza ----
            //
            // Antes esto hacia «return» y la pieza desaparecia del plan entera: no contaba en
            // ninguna cuenta -ni creadas, ni saltadas- y solo dejaba un aviso, de los que el
            // informe ensena unos pocos. Asi es como una planta entera podia no modelarse sin
            // que el informe dijera que faltaba nada.
            //
            // Perder geometria en silencio no es una opcion aceptable, asi que si la llave se
            // repite se le anade DONDE esta la pieza, que es un dato que la distingue y que es
            // el mismo en cada exportacion -a diferencia de un contador, que depende del orden
            // en que se recorra el modelo y convertiria cada reimportacion en un duplicado-.
            // La llave de la pieza MAS donde esta. Se calcula para todas, no solo para las que
            // chocan, porque es lo que permite distinguir "dos piezas que se llaman igual" de
            // "la misma pieza dos veces": si solo se le pusiera el sitio a la segunda, la
            // primera se quedaria sin el y las dos parecerian distintas.
            var puntos = pano is not null
                ? (IEnumerable<PuntoJson>)pano.Vertices
                : new[] { barra!.P1, barra.P2 };

            var conSitio = llave + Etiquetas.Donde(puntos);

            if (!sitios.Add(conSitio))
            {
                // Misma etiqueta, mismo nivel Y mismo sitio: entonces si es la misma pieza dos
                // veces, y modelar las dos dejaria dos elementos superpuestos.
                //
                // Los avisos llevan la forma «llave»: motivo a proposito, para que el informe
                // los agrupe por causa. Sin eso, un muro mallado en cien trozos producia cien
                // avisos distintos y el informe ensenaba los seis primeros.
                plan.Avisos.Add(
                    $"«{llave}»: hay otra pieza igual en el mismo sitio, solo se modela una");

                plan.Duplicadas++;

                return;
            }

            if (!vistas.Add(llave))
            {
                plan.Avisos.Add(
                    $"«{llave}»: habia mas de una pieza con esta etiqueta y este nivel, se "
                    + "distinguen por su posicion para que todas se modelen");

                plan.Desempatadas++;
                llave = conSitio;
                vistas.Add(llave);
            }

            var clave = Inventario.Clave(clase, seccion);
            var s = secciones.TryGetValue(clave, out var enc) ? enc : null;
            var tipo = s is null ? null : mapeo.TipoDe(s, catalogo);

            if (tipo is null)
            {
                plan.Pasos.Add(new Paso
                {
                    Llave = llave,
                    Accion = Accion.SinMapeo,
                    Barra = barra,
                    Pano = pano,
                    NivelRevit = niveles.Nombre(nivelModelo),
                    Motivo = "la seccion «" + seccion.Nombre + "» no tiene tipo de Revit elegido"
                });

                return;
            }

            var nivelRevit = niveles.Nombre(nivelModelo);

            if (yaEsta.TryGetValue(llave, out var existe))
            {
                var cambios = new List<string>();

                if (existe.TipoId != tipo.Id)
                {
                    cambios.Add("cambia el tipo a " + tipo.NombreCompleto);
                }

                if (barra is not null && Movida(existe, barra))
                {
                    cambios.Add("se movio en el modelo de calculo");
                }

                if (cambios.Count == 0)
                {
                    plan.Pasos.Add(new Paso
                    {
                        Llave = llave,
                        Accion = Accion.DejarIgual,
                        Barra = barra,
                        Pano = pano,
                        Tipo = tipo,
                        NivelRevit = nivelRevit,
                        IdExistente = existe.Id,
                        Motivo = "ya esta y coincide"
                    });

                    return;
                }

                plan.Pasos.Add(new Paso
                {
                    Llave = llave,

                    // En modo "modelar nuevos" lo que ya esta NO se toca, aunque haya
                    // cambiado. Es la diferencia entre los dos botones.
                    Accion = modo == Modo.ActualizarExistentes
                        ? Accion.Actualizar
                        : Accion.DejarIgual,

                    Barra = barra,
                    Pano = pano,
                    Tipo = tipo,
                    NivelRevit = nivelRevit,
                    IdExistente = existe.Id,
                    Motivo = modo == Modo.ActualizarExistentes
                        ? string.Join(" y ", cambios)
                        : "cambio en el modelo, pero se pidio solo modelar nuevos: "
                          + string.Join(" y ", cambios)
                });

                return;
            }

            // No esta en Revit.
            plan.Pasos.Add(new Paso
            {
                Llave = llave,

                // En modo "actualizar existentes" no se crea nada, a proposito.
                Accion = modo == Modo.ModelarNuevos ? Accion.Crear : Accion.DejarIgual,
                Barra = barra,
                Pano = pano,
                Tipo = tipo,
                NivelRevit = nivelRevit,
                Motivo = modo == Modo.ModelarNuevos
                    ? "no esta en Revit"
                    : "no esta en Revit, pero se pidio solo actualizar existentes"
            });
        }

        foreach (var b in modelo.Barras)
        {
            Uno(Llave.De(b), b.Clase, b.Seccion, b.Nivel, b, null);
        }

        foreach (var p in modelo.Panos)
        {
            Uno(Llave.De(p), p.Clase, p.Seccion, p.Nivel, null, p);
        }

        // Lo que esta en Revit con nuestra marca y ya no esta en el modelo. Se INFORMA y no
        // se borra: borrar en Revit sin que nadie lo pida no se puede deshacer de forma
        // obvia, y una pieza que "sobra" puede ser una que alguien renombro a mano.
        foreach (var (llave, e) in yaEsta)
        {
            if (vistas.Contains(llave))
            {
                continue;
            }

            plan.Pasos.Add(new Paso
            {
                Llave = llave,
                Accion = Accion.Sobra,
                IdExistente = e.Id,
                Motivo = "esta en Revit con marca de CadLink y ya no esta en el modelo"
            });
        }

        return plan;
    }

    private static bool Movida(PiezaExistente e, BarraJson b)
    {
        if (e.P1 is null || e.P2 is null)
        {
            // El complemento no leyo la posicion: no se puede saber, asi que no se dice que
            // se movio. Preferible a inventar un cambio y recolocar piezas que estaban bien.
            return false;
        }

        return Lejos(e.P1, b.P1) || Lejos(e.P2, b.P2);
    }

    private static bool Lejos(PuntoJson a, PuntoJson b) =>
        Math.Abs(a.X - b.X) > ToleranciaM
        || Math.Abs(a.Y - b.Y) > ToleranciaM
        || Math.Abs(a.Z - b.Z) > ToleranciaM;
}

/// <summary>Empareja los niveles del modelo con los del proyecto de Revit.</summary>
public sealed class Niveles
{
    private readonly Dictionary<string, string> _porNombre = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Los niveles del modelo que no tienen pareja en Revit.</summary>
    public List<NivelJson> Faltan { get; } = new();

    /// <summary>Como se llama en Revit el nivel que en el modelo se llama asi.</summary>
    public string Nombre(string? delModelo)
    {
        var n = (delModelo ?? string.Empty).Trim();

        return _porNombre.TryGetValue(n, out var r) ? r : n;
    }

    /// <summary>Cuanto pueden diferir dos cotas para considerarlas el mismo nivel. 10 cm.</summary>
    public const double ToleranciaM = 0.10;

    /// <summary>
    /// Empareja por NOMBRE y, si no, por COTA.
    /// </summary>
    /// <remarks>
    /// Primero por nombre porque es lo que el usuario reconoce, y despues por cota porque los
    /// nombres casi nunca coinciden: en ETABS el nivel se llama "Story1" y en Revit "PLANTA
    /// BAJA". Emparejar por cota con 10 cm de tolerancia acierta en la practica y ahorra
    /// crear niveles duplicados a 3.00 y a 3.001.
    /// </remarks>
    public static Niveles Resolver(ModeloJson modelo, CatalogoRevit catalogo)
    {
        var r = new Niveles();
        var deRevit = catalogo?.Niveles ?? new List<NivelJson>();

        foreach (var n in modelo.Niveles)
        {
            var nombre = (n.Nombre ?? string.Empty).Trim();

            if (nombre.Length == 0)
            {
                continue;
            }

            var porNombre = deRevit.FirstOrDefault(x =>
                string.Equals((x.Nombre ?? string.Empty).Trim(), nombre,
                    StringComparison.OrdinalIgnoreCase));

            if (porNombre is not null)
            {
                r._porNombre[nombre] = porNombre.Nombre;
                continue;
            }

            var porCota = deRevit
                .Where(x => Math.Abs(x.ElevacionM - n.ElevacionM) <= ToleranciaM)
                .OrderBy(x => Math.Abs(x.ElevacionM - n.ElevacionM))
                .FirstOrDefault();

            if (porCota is not null)
            {
                r._porNombre[nombre] = porCota.Nombre;
                continue;
            }

            r.Faltan.Add(n);
            r._porNombre[nombre] = nombre;
        }

        return r;
    }

    /// <summary>Las parejas, para poder ensenarlas y comprobarlas.</summary>
    public IReadOnlyDictionary<string, string> Parejas => _porNombre;

    public string Explicacion()
    {
        if (_porNombre.Count == 0)
        {
            return "sin niveles";
        }

        return string.Join(", ", _porNombre
            .OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => string.Equals(p.Key, p.Value, StringComparison.OrdinalIgnoreCase)
                ? p.Key
                : p.Key + " -> " + p.Value));
    }

    /// <summary>La cota de un nivel del modelo, en centimetros, para los mensajes.</summary>
    public static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);
}
