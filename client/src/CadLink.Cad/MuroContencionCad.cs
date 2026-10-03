namespace CadLink.Cad;

/// <summary>Una varilla del muro: su clave y su diametro ya resuelto en cm.</summary>
/// <remarks>
/// Propia del muro, y no <see cref="VarCad"/>, para que la geometria del muro se pueda
/// compilar y probar sola -tools/prueba-muro-contencion- sin arrastrar el resto de CadLink.Cad.
/// </remarks>
public readonly record struct VarMuro(string Clave, double Cm)
{
    public bool Existe => Cm > 0 && !string.IsNullOrWhiteSpace(Clave);

    public double M => Cm / 100.0;
}

/// <summary>
/// Un <b>muro de contencion</b> listo para dibujar: de concreto armado (en voladizo, con
/// punta, talon y espolon) o de concreto ciclopeo (de gravedad).
/// </summary>
/// <remarks>
/// <para>
/// Medidas en <b>metros</b>, separaciones del acero en <b>cm</b>, como se capturan en la hoja.
/// </para>
/// <para>
/// <b>Concreto armado</b> (la imagen del muro con espolon): la pantalla tiene la cara de la
/// tierra vertical y la exterior inclinada; la zapata va de la punta al talon; el espolon,
/// si lo lleva, cuelga bajo la zapata y de el suben las espigas.
/// </para>
/// <para>
/// <b>Ciclopeo</b> (la imagen de las letras): zapata de espesor <c>d</c> repartida en
/// <c>M</c>, <c>E</c>, <c>G</c> y <c>N</c>; el cuerpo, de altura <c>h</c>, tiene la cara de la
/// tierra vertical y la exterior inclinada, con corona <c>c + b</c>.
/// </para>
/// </remarks>
public sealed class MuroContencionCad
{
    public const string ConcretoArmado = "CONCRETO ARMADO";
    public const string Ciclopeo = "CICLOPEO";

    public string Tipo { get; init; } = ConcretoArmado;

    public bool EsCiclopeo => Tipo == Ciclopeo;

    public string Id { get; init; } = string.Empty;

    /// <summary>Altura de la pantalla o del cuerpo, de la cara de arriba de la zapata a la corona.</summary>
    public double AlturaM { get; init; }

    /// <summary>Espesor de la zapata: el <c>d</c> del ciclopeo.</summary>
    public double EspesorZapataM { get; init; }

    public string Fc { get; init; } = string.Empty;

    public string Escala { get; init; } = "50";

    // ======================================================================
    //  Concreto armado
    // ======================================================================

    /// <summary>Ancho total de la zapata.</summary>
    public double BaseM { get; init; }

    /// <summary>De la orilla de la punta a la cara exterior de la pantalla, al pie.</summary>
    public double PuntaM { get; init; }

    /// <summary>Espesor de la pantalla en la corona.</summary>
    public double CoronaM { get; init; }

    /// <summary>Espesor de la pantalla al pie, sobre la zapata.</summary>
    public double EspesorPieM { get; init; }

    /// <summary>El talon: lo que sobra de la zapata detras de la pantalla.</summary>
    public double TalonM => BaseM - PuntaM - EspesorPieM;

    public double RecCm { get; init; } = 5;

    public bool Espolon { get; init; }

    /// <summary>De la orilla de la punta a la cara izquierda del espolon.</summary>
    public double EspolonDistM { get; init; }

    public double EspolonAnchoM { get; init; }

    /// <summary>Lo que baja el espolon bajo la zapata.</summary>
    public double EspolonProfM { get; init; }

    /// <summary>Vertical de la cara de la tierra: el acero principal de la pantalla.</summary>
    public VarMuro VarVertTierra { get; init; }

    public double SepVertTierraCm { get; init; }

    /// <summary>Vertical de la cara exterior.</summary>
    public VarMuro VarVertExt { get; init; }

    public double SepVertExtCm { get; init; }

    /// <summary>Horizontal de la pantalla, en las dos caras.</summary>
    public VarMuro VarHoriz { get; init; }

    public double SepHorizCm { get; init; }

    /// <summary>Las espigas: suben del espolon a la pantalla. Solo con espolon.</summary>
    public VarMuro VarEspiga { get; init; }

    public double SepEspigaCm { get; init; }

    /// <summary>Lo que suben las espigas sobre la cara de arriba de la zapata.</summary>
    public double LongEspigaM { get; init; }

    /// <summary>Lecho superior de la zapata, a lo ancho.</summary>
    public VarMuro VarZapSup { get; init; }

    public double SepZapSupCm { get; init; }

    /// <summary>Reparticion del lecho superior: las que se ven de punta en el corte.</summary>
    public VarMuro VarRepSup { get; init; }

    public double SepRepSupCm { get; init; }

    public VarMuro VarZapInf { get; init; }

    public double SepZapInfCm { get; init; }

    public VarMuro VarRepInf { get; init; }

    public double SepRepInfCm { get; init; }

    /// <summary>Lo ancho del detalle «acero en la pantalla», en m de muro.</summary>
    public double AnchoDetalleM { get; init; } = 2.0;

    // ======================================================================
    //  Ciclopeo: las letras de su dibujo
    // ======================================================================

    /// <summary>Corona del lado de la cara inclinada.</summary>
    public double CM { get; init; }

    /// <summary>Corona del lado de la tierra.</summary>
    public double BM { get; init; }

    /// <summary>Punta: de la orilla al pie de la cara inclinada.</summary>
    public double MM { get; init; }

    /// <summary>Lo que avanza la cara inclinada.</summary>
    public double EM { get; init; }

    /// <summary>De donde acaba la inclinacion a la cara de la tierra, al pie.</summary>
    public double GM { get; init; }

    /// <summary>Talon: de la cara de la tierra a la orilla.</summary>
    public double NM { get; init; }

    /// <summary>Porcentaje de piedra, para el rotulo.</summary>
    public string PiedraPct { get; init; } = string.Empty;

    /// <summary>Ancho total de la base del ciclopeo.</summary>
    public double BaseCiclopeoM => MM + EM + GM + NM;
}
