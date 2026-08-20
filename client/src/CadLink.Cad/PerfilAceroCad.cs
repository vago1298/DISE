namespace CadLink.Cad;

/// <summary>
/// Un perfil de acero listo para dibujar: <b>todo en centímetros y ya resuelto</b>.
/// </summary>
/// <remarks>
/// <para>
/// Es el equivalente de <see cref="SeccionCad"/> para la hoja de acero. La capa de
/// interfaz decide familia, forma, traducciones de nombre y textos del rótulo; aquí llegan
/// hechos, de modo que el dibujante no vuelve a interpretar nada: solo dibuja.
/// </para>
/// <para>
/// Las medidas se llaman por lo que son y <b>cada forma usa las que necesita</b>. Lo que
/// en un IR es el espesor del alma, en un tubo es el espesor de la pared: es el mismo dato
/// —el grueso del acero— y por eso comparten campo.
/// </para>
/// </remarks>
public sealed class PerfilAceroCad
{
    /// <summary>
    /// Familia: una de las doce del catálogo. Decide el <b>color</b> y la capa.
    /// </summary>
    /// <remarks>
    /// No decide la geometría: eso es <see cref="Forma"/>. Cuatro familias distintas —IR,
    /// IS, IC y S— se dibujan con la misma forma y con cuatro colores distintos, que es
    /// exactamente lo que hace falta para poder decir cuál es cuál en el plano.
    /// </remarks>
    public string Familia { get; init; } = "IR";

    /// <summary>La forma que se traza. Una de <see cref="FormaAcero.Todas"/>.</summary>
    public string Forma { get; init; } = FormaAcero.I;

    /// <summary>Identificador. Es el nombre del bloque en AutoCAD.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Primer renglón del rótulo, ya armado: «VIGA PRINCIPAL».</summary>
    public string Elemento { get; init; } = string.Empty;

    /// <summary>Nombre del perfil ya traducido y con el calibre en CAL.</summary>
    public string Perfil { get; init; } = string.Empty;

    /// <summary>Tipo de acero, para el renglón «ACERO …».</summary>
    public string Acero { get; init; } = string.Empty;

    /// <summary>Dos perfiles juntos. En las formas con un lado, el segundo va espejeado.</summary>
    public bool Doble { get; init; }

    /// <summary>Peralte. En el tubo redondo y el macizo es el diámetro; en la L, el ala larga.</summary>
    public double PeralteCm { get; init; }

    /// <summary>Ancho de patín; cara del tubo rectangular; ala corta del ángulo.</summary>
    public double AnchoCm { get; init; }

    /// <summary>Espesor del alma en las laminadas, de la pared en los tubos, de la lámina en frío.</summary>
    public double EspesorCm { get; init; }

    /// <summary>Espesor del patín. Solo las formas laminadas: I, te y canal.</summary>
    public double EspesorPatinCm { get; init; }

    /// <summary>Largo del labio. Solo la canal con labios.</summary>
    public double LabioCm { get; init; }

    /// <summary>Radio de doblez exterior. La canal con labios y la zeta.</summary>
    public double RadioCm { get; init; }

    /// <summary>El patín <b>angosto</b> de la zeta. En cero, la zeta sale simétrica.</summary>
    public double AnchoMenorCm { get; init; }

    /// <summary>Escala que se escribe en el rótulo, sin dibujar nada a esa escala.</summary>
    public string EscalaRotulo { get; init; } = "1:10";

    /// <summary>
    /// El <b>ancho que ocupa UN perfil</b>, según su forma.
    /// </summary>
    /// <remarks>
    /// Casi todas las formas ocupan su ancho de patín, pero hay tres excepciones y las tres
    /// importan para que las secciones no se encimen: los dos redondos ocupan su
    /// <b>diámetro</b>, que viene en el peralte, y la <b>zeta</b> ocupa más que su patín,
    /// porque sus dos patines salen a lados contrarios del alma —el ancho de una zeta es la
    /// suma de los dos menos el espesor del alma que comparten—.
    /// </remarks>
    public double AnchoDeUnoCm => Forma switch
    {
        FormaAcero.TuboRedondo or FormaAcero.RedondoMacizo => PeralteCm,
        FormaAcero.Zeta => AnchoCm + PatinAngostoCm - EspesorCm,

        // El tubo rectangular se dibuja DE PIE: su lado menor es el ancho, pase lo que pase
        // en la captura. Es la regla de la macro —un tubo de 10x20 y otro de 20x10 son el
        // mismo tubo— y aquí se aplica también al hueco, no solo al trazo: antes el trazo se
        // volteaba y el hueco no, así que un tubo capturado al revés se dibujaba estrecho
        // dentro de un hueco ancho y dejaba un agujero en la fila.
        FormaAcero.TuboRectangular when AnchoCm > 0 => Math.Min(PeralteCm, AnchoCm),

        _ => AnchoCm
    };

    /// <summary>El patín angosto de la zeta, o el ancho si no se capturó.</summary>
    /// <remarks>
    /// Que el ancho 2 en cero signifique «igual al ancho» es lo que permite capturar una
    /// zeta de fabricación propia, que sí es simétrica, sin tener que repetir el número.
    /// </remarks>
    public double PatinAngostoCm =>
        AnchoMenorCm > 0 && AnchoMenorCm <= AnchoCm ? AnchoMenorCm : AnchoCm;

    /// <summary>El <b>ancho que ocupa el dibujo</b>, con el doble ya contado.</summary>
    /// <remarks>
    /// Lo usa quien va colocando las secciones una tras otra: es lo que hay que avanzar en
    /// X.
    /// </remarks>
    public double AnchoDibujoCm => Doble ? 2 * AnchoDeUnoCm : AnchoDeUnoCm;

    /// <summary>El <b>alto</b> del dibujo. En los redondos es el diámetro.</summary>
    /// <remarks>
    /// En el tubo rectangular es el lado <b>mayor</b>, por lo mismo que
    /// <see cref="AnchoDeUnoCm"/> es el menor: el tubo se dibuja de pie.
    /// </remarks>
    public double AltoDibujoCm => Forma == FormaAcero.TuboRectangular && AnchoCm > 0
        ? Math.Max(PeralteCm, AnchoCm)
        : PeralteCm;

    // ==================================================================
    //  El rótulo
    // ==================================================================

    /// <summary>
    /// Los cuatro renglones del rótulo, ya armados.
    /// </summary>
    /// <remarks>
    /// Se calculan aquí, y no en el dibujante, porque <b>el que acomoda las secciones
    /// necesita saber cuánto miden</b>: el rótulo va centrado bajo el perfil y suele ser más
    /// ancho que él, así que es el rótulo —y no la sección— el que decide cuánto hueco hay
    /// que dejar entre un perfil y el siguiente. Con los renglones aquí, el dibujante los
    /// escribe y el que acomoda los mide, y los dos hablan del mismo texto.
    /// </remarks>
    public IReadOnlyList<string> LineasRotulo => new[]
    {
        $"{Elemento.ToUpperInvariant()} \"{Id}\"",
        (Doble ? "PERFIL DOBLE: " : "PERFIL: ") + Perfil.ToUpperInvariant(),
        "ACERO " + Acero.ToUpperInvariant(),
        $"Acot. cm    Esc. {EscalaRotulo}"
    };

    /// <summary>
    /// Altura de la letra del rótulo, en centímetros, <b>proporcional al perfil</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las cuatro macros usaban cuatro alturas fijas —0.03 el IR, 0.022 el CF, 0.02 el OC— y
    /// solo la del OR tenía una regla: 0.02 si su primer número no pasaba de 6, y 0.03 si sí.
    /// <b>Esa es la que se generalizó</b>, porque es la única con un motivo: el rótulo se
    /// centra bajo el perfil, así que en un perfil chico un texto grande sobresale por los
    /// lados y se mete en el de al lado.
    /// </para>
    /// <para>
    /// Con el peralte dividido entre diez sale 3 cm para un perfil de 30 —lo que usaba el
    /// IR—, 2 cm para uno de 20 —lo que usaba el OR chico y el OC— y el tope de abajo lo
    /// mantiene en 2 cm para el resto, que a escala 1:10 son 2 mm en el papel: el mínimo con
    /// el que un plano se sigue leyendo.
    /// </para>
    /// </remarks>
    public double AlturaRotuloCm => Math.Clamp(PeralteCm / 10, 2.0, 3.0);

    /// <summary>
    /// Ancho de la caja del rótulo, en centímetros, <b>calculado del renglón más largo</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las macros lo dejaban en 0.7 m fijo, salvo la del tubo redondo que lo subía a 2.5
    /// «para que el renglón del perfil no se parta en dos»: o sea que el problema ya se
    /// conocía y se parcheó a mano en una de las cuatro. Y con el catálogo del IMCA es peor,
    /// porque hay nombres de cuarenta y seis caracteres —«IS - 225 mm x 12.7 mm / 750 mm x
    /// 9.5 mm»— que en una caja de 0.7 se parten en tres renglones.
    /// </para>
    /// <para>
    /// El 0.6 es la relación ancho/alto media de la Bahnschrift SemiLight, que es condensada.
    /// No hace falta que sea exacto: si sobra un poco, el MText no parte nada, y de eso se
    /// trata.
    /// </para>
    /// </remarks>
    public double AnchoRotuloCm
    {
        get
        {
            var masLargo = 0;

            foreach (var linea in LineasRotulo)
            {
                if (linea.Length > masLargo)
                {
                    masLargo = linea.Length;
                }
            }

            return Math.Max(70, masLargo * AlturaRotuloCm * 0.6);
        }
    }
}
