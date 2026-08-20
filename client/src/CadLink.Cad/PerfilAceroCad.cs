namespace CadLink.Cad;

/// <summary>
/// Un perfil de acero listo para dibujar: <b>todo en centímetros y ya resuelto</b>.
/// </summary>
/// <remarks>
/// <para>
/// Es el equivalente de <see cref="SeccionCad"/> para la hoja de acero. La capa de
/// interfaz decide familia, traducciones de nombre y textos del rótulo; aquí llegan hechos,
/// de modo que el dibujante no vuelve a interpretar nada: solo dibuja.
/// </para>
/// <para>
/// Las medidas se llaman por lo que son y <b>cada familia usa las que necesita</b>. Lo que
/// en un IR es el espesor del alma, en un tubo es el espesor de la pared: es el mismo dato
/// —el grueso del acero— y por eso comparten campo.
/// </para>
/// </remarks>
public sealed class PerfilAceroCad
{
    /// <summary>Familia: <c>IR</c>, <c>OR</c>, <c>OC</c> o <c>CF</c>.</summary>
    public string Familia { get; init; } = "IR";

    /// <summary>Identificador. Es el nombre del bloque en AutoCAD.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Primer renglón del rótulo, ya armado: «VIGA PRINCIPAL».</summary>
    public string Elemento { get; init; } = string.Empty;

    /// <summary>Nombre del perfil ya traducido a IR/OR/OC y con el calibre en CAL.</summary>
    public string Perfil { get; init; } = string.Empty;

    /// <summary>Tipo de acero, para el renglón «ACERO …».</summary>
    public string Acero { get; init; } = string.Empty;

    /// <summary>Dos perfiles juntos. En el CF el segundo va espejeado.</summary>
    public bool Doble { get; init; }

    /// <summary>Peralte. En el <c>OC</c> es el diámetro exterior.</summary>
    public double PeralteCm { get; init; }

    /// <summary>Ancho de patín en IR y CF, ancho de la cara en OR. El OC no lo usa.</summary>
    public double AnchoCm { get; init; }

    /// <summary>Espesor del alma en el IR, de la pared en OR, OC y CF.</summary>
    public double EspesorCm { get; init; }

    /// <summary>Espesor del patín. Solo el IR.</summary>
    public double EspesorPatinCm { get; init; }

    /// <summary>Largo del labio. Solo el CF.</summary>
    public double LabioCm { get; init; }

    /// <summary>Radio de doblez exterior. Solo el CF.</summary>
    public double RadioCm { get; init; }

    /// <summary>Escala que se escribe en el rótulo, sin dibujar nada a esa escala.</summary>
    public string EscalaRotulo { get; init; } = "1:10";

    /// <summary>El <b>ancho que ocupa el dibujo</b>, con el doble ya contado.</summary>
    /// <remarks>
    /// Lo usa quien va colocando las secciones una tras otra: es lo que hay que avanzar en
    /// X. En el OC el ancho es el diámetro, que va en <see cref="PeralteCm"/>.
    /// </remarks>
    public double AnchoDibujoCm
    {
        get
        {
            var uno = Familia == "OC" ? PeralteCm : AnchoCm;
            return Doble ? 2 * uno : uno;
        }
    }

    /// <summary>El <b>alto</b> del dibujo. En el OC es el diámetro.</summary>
    public double AltoDibujoCm => PeralteCm;
}
