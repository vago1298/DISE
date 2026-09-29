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
}

/// <summary>El desnivel de cada vertice de una losa respecto de su plano de apoyo.</summary>
public sealed record FormaDeLosa(
    double ZBaseM, List<PuntoJson> EnPlanta, List<double> DesfasesM, double DesnivelM)
{
    /// <summary>Si la losa es plana y no hay que modificar su forma.</summary>
    public bool EsPlana => DesnivelM <= Losas.ToleranciaPlanaM;
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
/// El camino es en dos pasos: se crea PLANA a una cota, y despues se sube o baja cada vertice
/// a su cota real con el editor de forma de la losa. Esta clase calcula lo que hace falta para
/// el segundo paso.
/// </para>
/// <para>
/// La cota de apoyo es la mas BAJA del contorno, no la mas alta: asi todos los desfases salen
/// positivos y la losa se levanta desde su punto mas bajo. Con la cota alta habria que bajar
/// vertices, que funciona igual pero deja la losa por debajo de su nivel mientras se edita.
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

    /// <summary>Descompone el contorno en un plano de apoyo mas un desfase por vertice.</summary>
    public static FormaDeLosa Preparar(IReadOnlyList<PuntoJson>? vertices)
    {
        var v = Contornos.SinRepetidos(vertices, 0.001);

        if (v.Count == 0)
        {
            return new FormaDeLosa(0, new List<PuntoJson>(), new List<double>(), 0);
        }

        var zMin = v.Min(p => p.Z);
        var zMax = v.Max(p => p.Z);

        var enPlanta = v
            .Select(p => new PuntoJson { X = p.X, Y = p.Y, Z = zMin })
            .ToList();

        var desfases = v.Select(p => p.Z - zMin).ToList();

        return new FormaDeLosa(zMin, enPlanta, desfases, zMax - zMin);
    }

    /// <summary>
    /// Empareja cada vertice del editor de Revit con el desfase que le toca, por su posicion
    /// en planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hace falta porque el editor de forma de la losa devuelve SUS vertices en el orden que
    /// quiere, que no es el del contorno que se le dio. Emparejarlos por indice pondria la
    /// pendiente al reves.
    /// </para>
    /// <para>
    /// Se emparejan por cercania en planta, y si alguno no encuentra pareja se devuelve cero
    /// para el: mejor un vertice sin subir que la losa retorcida.
    /// </para>
    /// </remarks>
    public static double DesfaseDe(
        double x, double y, FormaDeLosa forma, double toleranciaM = 0.05)
    {
        var mejor = -1;
        var mejorDistancia = double.MaxValue;

        for (var i = 0; i < forma.EnPlanta.Count; i++)
        {
            var p = forma.EnPlanta[i];
            var d = Math.Sqrt(((p.X - x) * (p.X - x)) + ((p.Y - y) * (p.Y - y)));

            if (d < mejorDistancia)
            {
                mejorDistancia = d;
                mejor = i;
            }
        }

        if (mejor < 0 || mejorDistancia > toleranciaM)
        {
            return 0;
        }

        return forma.DesfasesM[mejor];
    }

    /// <summary>Para los mensajes.</summary>
    public static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);
}
