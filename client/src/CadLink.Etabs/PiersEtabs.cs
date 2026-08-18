namespace CadLink.Etabs;

/// <summary>
/// Un <b>pier</b> de muro de ETABS, con sus propiedades por nivel.
/// </summary>
/// <remarks>
/// <para>
/// En ETABS un pier es una <b>etiqueta</b> que agrupa los paños de muro que trabajan
/// como un solo elemento vertical. Es lo que permite pedirle a ETABS las fuerzas del
/// muro completo en lugar de elemento por elemento, y es también la unidad con la que
/// se diseña y se dibuja: un plano de muros se rotula por pier, no por paño.
/// </para>
/// <para>
/// Un mismo pier aparece <b>una vez por nivel</b>, y sus medidas pueden cambiar de
/// nivel a nivel. Por eso cada renglón de aquí es la pareja pier + nivel, y no el
/// pier a secas.
/// </para>
/// </remarks>
public sealed class PierEtabs
{
    /// <summary>Etiqueta del pier, tal como está en ETABS.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Nivel al que corresponden estas medidas.</summary>
    public string Story { get; set; } = string.Empty;

    /// <summary>Ángulo del eje del pier, en grados.</summary>
    public double AnguloEje { get; set; }

    /// <summary>Largo del pier en su base, en metros.</summary>
    public double LargoBaseM { get; set; }

    /// <summary>Espesor en la base, en metros.</summary>
    public double EspesorBaseM { get; set; }

    /// <summary>Largo en la parte superior, en metros.</summary>
    public double LargoSupM { get; set; }

    /// <summary>Espesor en la parte superior, en metros.</summary>
    public double EspesorSupM { get; set; }

    /// <summary>Material asignado.</summary>
    public string Material { get; set; } = string.Empty;

    /// <summary>Cuántos paños de área forman el pier en ese nivel.</summary>
    public int Areas { get; set; }

    /// <summary>Cuántos elementos de línea forman el pier en ese nivel.</summary>
    public int Lineas { get; set; }

    /// <summary>Medidas de la base, para la cuadrícula.</summary>
    public string Dimensiones =>
        LargoBaseM > 0 && EspesorBaseM > 0
            ? $"{LargoBaseM * 100:0.#} x {EspesorBaseM * 100:0.#} cm"
            : string.Empty;
}

/// <summary>Resultado de leer los piers: los renglones y lo que pasó al leerlos.</summary>
public sealed class PiersLeidos
{
    public List<PierEtabs> Piers { get; } = new();

    /// <summary>Etiquetas distintas encontradas, aunque no se lograra medir alguna.</summary>
    public List<string> Etiquetas { get; } = new();

    public List<string> Avisos { get; } = new();

    public string Resumen()
    {
        var s = new System.Text.StringBuilder();

        s.AppendLine($"Piers encontrados : {Etiquetas.Count}");
        s.AppendLine($"Renglones (pier x nivel) : {Piers.Count}");

        if (Etiquetas.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("Etiquetas: " + string.Join(", ", Etiquetas));
        }

        if (Avisos.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("Avisos:");

            foreach (var a in Avisos)
            {
                s.AppendLine("  - " + a);
            }
        }

        return s.ToString();
    }
}
