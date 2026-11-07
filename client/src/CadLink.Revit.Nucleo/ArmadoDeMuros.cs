namespace CadLink.Revit.Nucleo;

/// <summary>Como se arma cada muro de concreto elegido.</summary>
/// <remarks>
/// Todo en metros. Las verticales van como capa MAJOR del Area Reinforcement y las horizontales
/// como MINOR, en las dos caras -exterior e interior- si se piden dos parrillas.
/// </remarks>
public sealed class OpcionesArmadoMuro
{
    /// <summary>Dos parrillas, una por cara. Apagado: una sola, la de la cara exterior.</summary>
    public bool DosParrillas { get; set; } = true;

    /// <summary>El nombre del tipo de varilla de Revit de las verticales y de las horizontales.</summary>
    public string VarillaVertical { get; set; } = string.Empty;

    public string VarillaHorizontal { get; set; } = string.Empty;

    public double SeparacionVerticalM { get; set; } = 0.20;

    public double SeparacionHorizontalM { get; set; } = 0.20;

    /// <summary>
    /// El gancho del extremo de ABAJO de las verticales -el que entra a la zapata- y el de ARRIBA.
    /// El largo va en diametros: es la longitud de desarrollo de la pata.
    /// </summary>
    public GanchoMuro GanchoInferior { get; set; } = new(90, 40);

    public GanchoMuro GanchoSuperior { get; set; } = new(90, 12);

    /// <summary>
    /// Con los dos ganchos iguales el armado se queda como Area Reinforcement. Distintos, hay que
    /// soltarlo en varillas: el Area Reinforcement lleva un solo gancho por capa, para las dos puntas.
    /// </summary>
    public bool MismosGanchos => GanchoInferior == GanchoSuperior;

    /// <summary>Lo que el contorno se queda antes del canto del muro en cada orilla. Cero = al canto.</summary>
    public double LibreM { get; set; }
}

/// <summary>Un gancho: su angulo y lo que mide su pata, en diametros de la varilla.</summary>
public readonly record struct GanchoMuro(int AnguloGrados, double Diametros)
{
    public static readonly int[] Angulos = { 0, 90, 135, 180 };

    public bool Ninguno => AnguloGrados <= 0 || Diametros <= 0;

    /// <summary>El nombre del tipo de gancho de Revit que CadLink crea si no existe.</summary>
    public string Nombre => Ninguno
        ? string.Empty
        : $"CadLink {AnguloGrados}° - {Diametros.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}d";
}

/// <summary>Las cuentas del armado de muros, sin nada de Revit.</summary>
public static class ArmadoDeMuros
{
    public const string Marca = "CadLink|ArmadoMuro|";

    public static string Llave(long idMuro) => Marca + idMuro;

    /// <summary>
    /// El contorno del armado en el plano del muro: cuatro esquinas, recorridas en orden, a lo
    /// largo del muro (S) y en altura (Z), metidas <see cref="OpcionesArmadoMuro.LibreM"/>.
    /// </summary>
    /// <returns>Vacio si el muro es tan chico que no queda nada.</returns>
    public static List<(double S, double Z)> Contorno(double largoM, double zBaseM, double zAltaM, double libreM)
    {
        var l = Math.Max(0, libreM);
        var s0 = l;
        var s1 = largoM - l;
        var z0 = zBaseM + l;
        var z1 = zAltaM - l;

        if (s1 - s0 <= 0.05 || z1 - z0 <= 0.05)
        {
            return new List<(double, double)>();
        }

        return new List<(double, double)> { (s0, z0), (s1, z0), (s1, z1), (s0, z1) };
    }

    /// <summary>Cual punta de una varilla es la de abajo: la 0 si arranca abajo, la 1 si no.</summary>
    public static int ExtremoDeAbajo(double zInicio, double zFin) => zInicio <= zFin ? 0 : 1;

    /// <summary>¿La varilla es vertical? Sube mas de lo que avanza.</summary>
    public static bool EsVertical(double dz, double enPlanta) => Math.Abs(dz) > Math.Abs(enPlanta);

    /// <summary>Cuantas varillas caben, para el informe.</summary>
    public static int Cuantas(double tramoM, double separacionM) =>
        separacionM <= 0 || tramoM <= 0 ? 0 : 1 + (int)Math.Floor((tramoM / separacionM) + 1e-9);

    /// <summary>Lo que falta para poder armar. Vacio = se puede.</summary>
    public static List<string> Falta(OpcionesArmadoMuro o)
    {
        var f = new List<string>();

        if (o.VarillaVertical.Trim().Length == 0) { f.Add("la varilla vertical"); }
        if (o.VarillaHorizontal.Trim().Length == 0) { f.Add("la varilla horizontal"); }
        if (o.SeparacionVerticalM < 0.05) { f.Add("una separacion vertical de al menos 5 cm"); }
        if (o.SeparacionHorizontalM < 0.05) { f.Add("una separacion horizontal de al menos 5 cm"); }

        foreach (var g in new[] { o.GanchoInferior, o.GanchoSuperior })
        {
            if (!GanchoMuro.Angulos.Contains(g.AnguloGrados)) { f.Add("ganchos de 90, 135 o 180 grados"); break; }
            if (g.AnguloGrados > 0 && g.Diametros <= 0) { f.Add("el largo de cada gancho, en diametros"); break; }
        }

        return f;
    }
}
