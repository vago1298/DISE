using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>Donde cuelga una columna: nivel de base, nivel de punta y sus desfases.</summary>
public sealed record ColocacionColumna(
    string NivelBase, double DesfaseBaseM, string NivelPunta, double DesfasePuntaM);

/// <summary>
/// Decide entre que niveles va una columna y con que desfases.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque crear una columna con la API de columna INCLINADA -pasandole la linea de
/// sus dos extremos- produce este error, que Revit marca como <b>imposible de ignorar</b>:
/// </para>
/// <para>
/// <i>"Position of end cut planes has resulted in a slanted column without any geometry."</i>
/// </para>
/// <para>
/// Y como no se puede ignorar, al cancelar el cuadro se deshace la transaccion entera: el
/// informe dice que se crearon cuatrocientas piezas y en el modelo no aparece ninguna.
/// </para>
/// <para>
/// La salida es no usar esa API para las columnas verticales, que son casi todas: se coloca la
/// columna en su punto y se le dan <b>nivel de base y nivel de punta</b> con sus desfases, que
/// es como se modela una columna en Revit a mano. Esta clase calcula esos cuatro valores.
/// </para>
/// <para>
/// El otro motivo por el que hacia falta: ETABS asigna la columna al nivel al que <b>sube</b>,
/// no al que arranca. Usando ese nivel como base, la columna queda con su base por encima de
/// su punta.
/// </para>
/// </remarks>
public static class Colocacion
{
    /// <summary>Por debajo de esto una barra se considera vertical.</summary>
    /// <remarks>
    /// 2 cm de desvio en planta. Un modelo real no tiene columnas perfectamente verticales al
    /// milimetro, y tratar como inclinada una que esta 3 mm fuera de plomo la manda por la API
    /// que da el error.
    /// </remarks>
    public const double ToleranciaPlomoM = 0.02;

    /// <summary>Si la barra es una columna vertical, que se coloca por niveles.</summary>
    public static bool EsVertical(BarraJson b)
    {
        var dx = b.P2.X - b.P1.X;
        var dy = b.P2.Y - b.P1.Y;

        return Math.Sqrt((dx * dx) + (dy * dy)) <= ToleranciaPlomoM
               && Math.Abs(b.P2.Z - b.P1.Z) > 1e-6;
    }

    /// <summary>
    /// Entre que niveles va la columna, y cuanto se desfasa de cada uno.
    /// </summary>
    /// <remarks>
    /// Se toma para cada extremo el nivel de cota MAS CERCANA, y el desfase es lo que falta.
    /// Asi la columna queda atada a niveles -que es lo que permite que se mueva con ellos y que
    /// se vea en las vistas de planta- en vez de suelta en el espacio.
    /// </remarks>
    public static ColocacionColumna De(
        double zBaseM, double zPuntaM, IReadOnlyList<NivelJson>? niveles)
    {
        var lista = (niveles ?? new List<NivelJson>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Nombre))
            .ToList();

        // Las dos cotas, ordenadas: la base es la de abajo, pase lo que pase. Una columna con
        // la base por encima de la punta es justo el caso que se queda sin geometria.
        var abajo = Math.Min(zBaseM, zPuntaM);
        var arriba = Math.Max(zBaseM, zPuntaM);

        if (lista.Count == 0)
        {
            // Sin niveles no hay a que atarla. Quien llama tendra que crear uno.
            return new ColocacionColumna(string.Empty, abajo, string.Empty, arriba);
        }

        var nBase = MasCercano(lista, abajo);
        var nPunta = MasCercano(lista, arriba);

        return new ColocacionColumna(
            nBase.Nombre, abajo - nBase.ElevacionM,
            nPunta.Nombre, arriba - nPunta.ElevacionM);
    }

    private static NivelJson MasCercano(List<NivelJson> niveles, double z) =>
        niveles
            .OrderBy(n => Math.Abs(n.ElevacionM - z))
            .ThenBy(n => n.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .First();

    /// <summary>
    /// A que nivel va un paño: el de su BASE, no el de la planta que ETABS le asigna.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mismo desajuste que en las columnas, y hasta ahora solo se corregia para ellas.
    /// ETABS asigna un area a la planta de su <b>parte de arriba</b>: un muro que va del suelo
    /// de planta baja al de la primera planta pertenece a la planta primera. Si se le pasa ese
    /// nivel a Revit, el muro queda con su restriccion de base una planta por encima de donde
    /// esta, y en la vista de planta baja no aparece: el muro existe, pero no en la planta en
    /// la que se le busca.
    /// </para>
    /// <para>
    /// Se devuelve el nombre del nivel de cota mas cercana a la BASE del paño, y el desfase que
    /// falta para llegar a ella. Si no hay niveles, se devuelve vacio y quien llama se queda con
    /// el que traiga el paso.
    /// </para>
    /// </remarks>
    /// <returns>El nombre del nivel y el desfase en metros desde su cota.</returns>
    public static (string Nombre, double DesfaseM) NivelDePano(
        double zBaseM, IReadOnlyList<NivelJson>? niveles)
    {
        var lista = (niveles ?? new List<NivelJson>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Nombre))
            .ToList();

        if (lista.Count == 0)
        {
            return (string.Empty, 0);
        }

        var n = MasCercano(lista, zBaseM);

        return (n.Nombre, zBaseM - n.ElevacionM);
    }
}

/// <summary>
/// El giro que hay que darle a una pieza para que se vea igual que en el modelo de calculo.
/// </summary>
/// <remarks>
/// <para>
/// La regla es la del plano de AutoCAD, que es el que esta bien: el contorno se arma con
/// <c>AnchoM</c> sobre la <b>X</b> y <c>PeralteM</c> sobre la <b>Y</b>, y despues se gira todo
/// por el angulo del eje local que dio ETABS. No hay ningun intercambio de medidas: el
/// intercambio SALE del giro.
/// </para>
/// <para>
/// En Revit hay un segundo giro que en AutoCAD no existe, y es el que faltaba. El plano dibuja
/// la seccion que trae el modelo; Revit coloca un TIPO DE FAMILIA, que tiene sus propias
/// medidas. El emparejador acepta a proposito un tipo con las medidas al reves -un
/// «300 x 450» de Revit sirve para una seccion de 45x30, porque lo que cambia es el giro de la
/// pieza y no el tipo-, pero calculaba esa decision y la tiraba. Sin recuperarla, una seccion
/// emparejada al reves sale girada noventa grados por construccion.
/// </para>
/// </remarks>
public static class Orientacion
{
    /// <summary>
    /// Las medidas con las que se va a modelar cada barra, segun el mapeo elegido.
    /// </summary>
    /// <remarks>
    /// Es lo que necesita <see cref="AjusteDeMuros"/> para recortar el muro al pano del
    /// castillo QUE VA A EXISTIR y no al de la seccion del calculo. Si el tipo vino emparejado
    /// con las medidas al reves, se devuelven cambiadas, porque asi es como va a quedar
    /// colocado.
    /// </remarks>
    public static MedidasModeladas Medidor(
        ModeloJson modelo, Mapeo mapeo, CatalogoRevit catalogo)
    {
        var secciones = Inventario.De(modelo)
            .ToDictionary(s => s.Clave, StringComparer.Ordinal);

        return b =>
        {
            var clave = Inventario.Clave(b.Clase, b.Seccion);

            if (!secciones.TryGetValue(clave, out var s))
            {
                return null;
            }

            var tipo = mapeo.TipoDe(s, catalogo);

            if (tipo?.AnchoM is not > 0 || tipo.PeralteM is not > 0)
            {
                return null;
            }

            return TipoGirado(b.Seccion, tipo)
                ? (tipo.PeralteM.Value, tipo.AnchoM.Value)
                : (tipo.AnchoM.Value, tipo.PeralteM.Value);
        };
    }

    /// <summary>
    /// El espesor con que se va a modelar cada muro: el de su tipo de Revit elegido, o null si
    /// el tipo no lo dice. Es el <see cref="Medidor"/> de los paños.
    /// </summary>
    public static EspesorModelado MedidorDeMuros(ModeloJson modelo, Mapeo mapeo, CatalogoRevit catalogo)
    {
        var secciones = Inventario.De(modelo)
            .ToDictionary(s => s.Clave, StringComparer.Ordinal);

        return p => secciones.TryGetValue(Inventario.Clave(p.Clase, p.Seccion), out var s)
                    && mapeo.TipoDe(s, catalogo)?.EspesorM is > 0 and var e
            ? e
            : null;
    }

    /// <summary>Por debajo de esto una seccion se considera cuadrada.</summary>
    /// <remarks>
    /// En una seccion cuadrada el giro de noventa grados no se nota, asi que no se aplica: se
    /// evita mover piezas sin motivo.
    /// </remarks>
    public const double ToleranciaCuadradaM = 0.002;

    /// <summary>Si el tipo de Revit elegido trae las medidas al reves que la seccion.</summary>
    public static bool TipoGirado(SeccionJson? seccion, TipoRevit? tipo)
    {
        if (seccion is null || tipo is null)
        {
            return false;
        }

        var sa = seccion.AnchoM;
        var sp = seccion.PeralteM;
        var ta = tipo.AnchoM;
        var tp = tipo.PeralteM;

        if (sa <= 0 || sp <= 0 || ta is null or <= 0 || tp is null or <= 0)
        {
            // Sin las cuatro medidas no se puede decidir. Se deja como este: girar a ciegas
            // seria peor que no girar.
            return false;
        }

        if (Math.Abs(sa - sp) <= ToleranciaCuadradaM)
        {
            return false;
        }

        var directo = Math.Abs(sa - ta.Value) + Math.Abs(sp - tp.Value);
        var girado = Math.Abs(sa - tp.Value) + Math.Abs(sp - ta.Value);

        return girado < directo - 1e-9;
    }

    /// <summary>El giro total de la pieza, en RADIANES.</summary>
    /// <remarks>
    /// El del modelo mas, si hace falta, el cuarto de vuelta que corrige un tipo emparejado
    /// con las medidas al reves.
    /// </remarks>
    public static double GiroRad(BarraJson? b, TipoRevit? tipo)
    {
        if (b is null)
        {
            return 0;
        }

        var g = b.AnguloGrados * Math.PI / 180.0;

        if (TipoGirado(b.Seccion, tipo))
        {
            g += Math.PI / 2.0;
        }

        return g;
    }

    /// <summary>Si el giro es lo bastante grande para que valga la pena aplicarlo.</summary>
    public static bool Vale(double giroRad) => Math.Abs(giroRad) > 1e-6;
}

/// <summary>
/// Una losa lista para Revit: su contorno en el plano de apoyo y, si esta inclinada, la
/// flecha de pendiente con que se le da la inclinacion.
/// </summary>
/// <param name="ZBaseM">La cota del plano de apoyo: la del vertice mas bajo.</param>
/// <param name="EnPlanta">El contorno, todo a <paramref name="ZBaseM"/>.</param>
/// <param name="ColaX">Donde arranca la flecha de pendiente. Es el vertice mas bajo.</param>
/// <param name="PuntaX">Hacia donde apunta: la planta del vertice mas alto.</param>
/// <param name="AnguloRad">La pendiente, en radianes, que es lo que pide Revit.</param>
/// <param name="DesnivelM">Cuanto sube del vertice mas bajo al mas alto.</param>
public sealed record FormaDeLosa(
    double ZBaseM,
    List<PuntoJson> EnPlanta,
    double ColaX, double ColaY,
    double PuntaX, double PuntaY,
    double AnguloRad,
    double DesnivelM)
{
    /// <summary>Si la losa es plana y se crea sin flecha de pendiente.</summary>
    public bool EsPlana => DesnivelM <= Losas.ToleranciaPlanaM || AnguloRad <= 1e-9;
}

/// <summary>
/// Prepara una losa para Revit conservando su INCLINACION.
/// </summary>
/// <remarks>
/// <para>
/// <c>Floor.Create</c> solo acepta un contorno plano y paralelo a XY, asi que una losa
/// inclinada no se puede crear de una vez. Pero aplanarla y dejarla asi pierde el dato:
/// una losa de entrepiso con pendiente modelada en el calculo tiene que salir con su
/// pendiente.
/// </para>
/// <para>
/// Se usa la sobrecarga de <c>Floor.Create</c> que acepta una <b>flecha de pendiente</b>: se le
/// pasa el contorno plano en la cota mas baja, una linea HORIZONTAL que marca hacia donde sube,
/// y el angulo en radianes. Es API documentada y estable.
/// </para>
/// <para>
/// <b>No se usa SlabShapeEditor.</b> Fue el primer intento y no compila: <c>Floor</c> ya no
/// expone esa propiedad -en Revit 2026 da
/// <c>CS1061: "Floor" no contiene una definicion para "SlabShapeEditor"</c>- porque el miembro
/// cambio de sitio al reorganizarse la jerarquia de suelos y toposolidos. La flecha de
/// pendiente hace lo mismo con una sola llamada y sin depender de donde viva ese miembro en
/// cada version.
/// </para>
/// <para>
/// La cota de apoyo es la mas BAJA del contorno: asi la losa solo sube desde ahi, que es lo que
/// entiende la flecha de pendiente.
/// </para>
/// <para>
/// <b>Limite:</b> una flecha de pendiente hace un plano inclinado. Una losa ALABEADA -cuyos
/// cuatro vertices no estan en un plano- no se puede reproducir asi, y se aproxima por el plano
/// que pasa por su vertice mas bajo y su mas alto. Se avisa cuando pasa.
/// </para>
/// </remarks>
public static class Losas
{
    /// <summary>Por debajo de esto la losa se considera plana. 5 mm.</summary>
    /// <remarks>
    /// Un contorno que viene de una malla de ETABS trae vertices que difieren fracciones de
    /// milimetro. Eso no es una losa inclinada: es ruido, y editar la forma por eso solo
    /// agrega subelementos al modelo sin que nada cambie a la vista.
    /// </remarks>
    public const double ToleranciaPlanaM = 0.005;

    /// <summary>
    /// Saca el contorno de apoyo y la flecha de pendiente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La flecha va del vertice MAS BAJO a la planta del MAS ALTO, y el angulo es el que forman
    /// esa carrera horizontal y el desnivel entre los dos. Para una losa plana inclinada eso es
    /// exacto.
    /// </para>
    /// <para>
    /// Se eligen esos dos vertices, y no se ajusta un plano por minimos cuadrados, porque la
    /// cola de la flecha tiene que caer <b>sobre el contorno</b>: un vertice lo cumple siempre,
    /// un punto calculado no.
    /// </para>
    /// </remarks>
    public static FormaDeLosa Preparar(IReadOnlyList<PuntoJson>? vertices)
    {
        var v = Contornos.SinRepetidos(vertices, 0.001);

        if (v.Count == 0)
        {
            return new FormaDeLosa(0, new List<PuntoJson>(), 0, 0, 0, 0, 0, 0);
        }

        var bajo = v[0];
        var alto = v[0];

        foreach (var p in v)
        {
            if (p.Z < bajo.Z)
            {
                bajo = p;
            }

            if (p.Z > alto.Z)
            {
                alto = p;
            }
        }

        var zMin = bajo.Z;
        var desnivel = alto.Z - zMin;

        var enPlanta = v
            .Select(p => new PuntoJson { X = p.X, Y = p.Y, Z = zMin })
            .ToList();

        var dx = alto.X - bajo.X;
        var dy = alto.Y - bajo.Y;
        var carrera = Math.Sqrt((dx * dx) + (dy * dy));

        // Sin carrera en planta no hay pendiente que expresar: los dos vertices estan uno
        // encima del otro, asi que el contorno no es una losa.
        if (desnivel <= ToleranciaPlanaM || carrera < 0.01)
        {
            return new FormaDeLosa(
                zMin, enPlanta, bajo.X, bajo.Y, bajo.X, bajo.Y, 0, desnivel);
        }

        return new FormaDeLosa(
            zMin, enPlanta,
            bajo.X, bajo.Y,
            alto.X, alto.Y,
            Math.Atan2(desnivel, carrera),
            desnivel);
    }

    /// <summary>
    /// Si los vertices estan en un plano, con la tolerancia dada.
    /// </summary>
    /// <remarks>
    /// Una flecha de pendiente solo hace un PLANO inclinado. Si la losa esta alabeada, la
    /// inclinacion que se consigue es una aproximacion y hay que decirlo. Se mide comparando la
    /// cota de cada vertice con la que le tocaria en el plano que definen la cola y la punta de
    /// la flecha.
    /// </remarks>
    public static double DesviacionDelPlano(
        IReadOnlyList<PuntoJson>? vertices, FormaDeLosa forma)
    {
        var v = Contornos.SinRepetidos(vertices, 0.001);

        if (v.Count < 4 || forma.AnguloRad <= 1e-9)
        {
            return 0;
        }

        var dx = forma.PuntaX - forma.ColaX;
        var dy = forma.PuntaY - forma.ColaY;
        var carrera = Math.Sqrt((dx * dx) + (dy * dy));

        if (carrera < 1e-9)
        {
            return 0;
        }

        // La direccion de maxima pendiente, y cuanto sube por metro recorrido en ella.
        var ux = dx / carrera;
        var uy = dy / carrera;
        var subidaPorMetro = Math.Tan(forma.AnguloRad);

        var mayor = 0.0;

        foreach (var p in v)
        {
            var avance = ((p.X - forma.ColaX) * ux) + ((p.Y - forma.ColaY) * uy);
            var zDelPlano = forma.ZBaseM + (avance * subidaPorMetro);

            mayor = Math.Max(mayor, Math.Abs(p.Z - zDelPlano));
        }

        return mayor;
    }

    /// <summary>Para los mensajes.</summary>
    public static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);
}
