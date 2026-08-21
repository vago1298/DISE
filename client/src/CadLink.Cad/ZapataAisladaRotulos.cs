using System.Globalization;
using System.Text;

namespace CadLink.Cad;

/// <summary>
/// Los textos de los rótulos de la zapata aislada, palabra por palabra como los
/// escriben las dos macros.
/// </summary>
/// <remarks>
/// <para>
/// Va aparte del dibujo y sin nada de COM por la misma razón que
/// <see cref="AlzadoLayout"/>: el usuario dijo <i>«los títulos no están en base a mis
/// dos macros originales»</i>, y un título es una cadena, no una opinión. Aquí se
/// puede comparar letra por letra contra el VBA —lo hace
/// <c>tools/verificar_rotulos_zapatas.py</c>— en lugar de revisarlo a ojo sobre el
/// dibujo.
/// </para>
/// <para>
/// Las tres líneas del alzado, tal como salen de la macro:
/// </para>
/// <code>
/// ZAPATA AISLADA CENTRAL "ZE-1"        alto 0.07   centrado en el eje de la zapata
/// ELEVACION                            alto 0.05
/// Rec. 5 cm    f'c = 250 kg/cm²    Escala 1:10     alto 0.04
/// </code>
/// <para>
/// Los cuatro espacios de separación de la última línea son literales:
/// <c>"Rec. 5 cm" &amp; "    " &amp; textoFC &amp; "    Escala 1:10"</c>. Y el f'c
/// <b>desaparece</b> si la celda viene vacía; no se escribe <c>f'c =</c> a secas.
/// </para>
/// </remarks>
public static class ZapataAisladaRotulos
{
    /// <summary>Alto del título del alzado y del de la planta.</summary>
    public const double AlturaTitulo = 0.07;

    /// <summary>Alto de la palabra ELEVACION.</summary>
    public const double AlturaSubtitulo = 0.05;

    /// <summary>Alto de la línea de recubrimiento, f'c y escala.</summary>
    public const double AlturaEscala = 0.04;

    /// <summary>Alto de los rótulos con leader: dado, columna y parrillas.</summary>
    public const double AlturaMText = 0.015;

    /// <summary>Alto de los rótulos de la vista en planta.</summary>
    public const double AlturaMTextPlanta = 0.03;

    /// <summary>Alto del texto del nivel del terreno.</summary>
    public const double AlturaTerreno = 0.025;

    /// <summary>Alto del texto de la plantilla, antes de ajustarlo a su espesor.</summary>
    public const double AlturaPlantilla = 0.02;

    /// <summary>Alto del ID del dado dentro de su bloque, en planta.</summary>
    public const double AlturaIdDadoPlanta = 0.03;

    /// <summary>Segunda línea del alzado. Sin acento, igual que la macro.</summary>
    public const string Subtitulo = "ELEVACION";

    /// <summary>
    /// Recubrimiento de la línea de escala. Está escrito así en la macro, con el
    /// valor metido en el texto, no calculado del dato.
    /// </summary>
    public const string Recubrimiento = "Rec. 5 cm";

    public const string Terreno = "Nivel del terreno";

    public const string Plantilla = "Plantilla de concreto simple f'c: 100 kg/cm²";

    public const string AmbosSentidos = "AMBOS SENTIDOS";

    public const string ParrillaInferior = "PARRILLA INFERIOR";

    public const string ParrillaSuperior = "PARRILLA SUPERIOR";

    /// <summary>Separador de las tres partes de la línea de escala: cuatro espacios.</summary>
    private const string Sep4 = "    ";

    /// <summary>Salto de línea del MText. La macro usa <c>vbCrLf</c>.</summary>
    public const string SaltoLinea = "\r\n";

    /// <summary>
    /// Primera línea del alzado: <c>ZAPATA AISLADA DE LINDERO "ZL-1"</c>.
    /// </summary>
    public static string Titulo(TipoZapata tipo, string id)
    {
        var nombre = tipo == TipoZapata.Lindero
            ? "ZAPATA AISLADA DE LINDERO"
            : "ZAPATA AISLADA CENTRAL";

        return $"{nombre} \"{(id ?? string.Empty).Trim()}\"";
    }

    /// <summary>Título de la vista en planta: <c>VISTA EN PLANTA "ZL-1"</c>.</summary>
    public static string TituloPlanta(string id) =>
        $"VISTA EN PLANTA \"{(id ?? string.Empty).Trim()}\"";

    /// <summary>
    /// Tercera línea del alzado: recubrimiento, f'c y escala.
    /// </summary>
    /// <param name="fc">Celda del f'c, tal como se capturó. Vacía = no se escribe.</param>
    public static string LineaEscala(string? fc, string? escala = "10")
    {
        var texto = TextoFc(fc);
        var esc = string.IsNullOrWhiteSpace(escala) ? "10" : escala!.Trim();

        return texto.Length > 0
            ? Recubrimiento + Sep4 + texto + Sep4 + "Escala 1:" + esc
            : Recubrimiento + Sep4 + "Escala 1:" + esc;
    }

    /// <summary>
    /// Línea de escala de la planta. <b>Nunca</b> lleva f'c: la macro escribe
    /// <c>"Rec. 5 cm    Escala 1:10"</c> tal cual.
    /// </summary>
    public static string LineaEscalaPlanta(string? escala = "10")
    {
        var esc = string.IsNullOrWhiteSpace(escala) ? "10" : escala!.Trim();
        return Recubrimiento + Sep4 + "Escala 1:" + esc;
    }

    /// <summary>
    /// f'c formateado: <c>f'c = 250 kg/cm²</c>.
    /// </summary>
    /// <remarks>
    /// Port de <c>TextoFCConcreto</c>. Si la celda trae un número, se escribe con la
    /// unidad; si trae texto que no es número, se respeta tal cual; si está vacía,
    /// devuelve cadena vacía y la línea de escala se arma sin ella.
    /// </remarks>
    public static string TextoFc(string? fc)
    {
        var raw = (fc ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var num = ExtraeNumero(raw);
        return num > 0
            ? "f'c = " + Numero(num) + " kg/cm²"
            : "f'c = " + raw;
    }

    /// <summary>
    /// Armado de una parrilla: <c>VAR #4 INFERIOR @ 15 cm</c>.
    /// </summary>
    /// <remarks>
    /// Port de <c>TextoVarSep</c>. El sufijo va <b>entre</b> el diámetro y la
    /// separación, no al final, y la separación se omite si es cero.
    /// </remarks>
    public static string VarSep(VarCad varilla, double sepCm, string? sufijo = null)
    {
        var dia = Dia(varilla);
        if (dia.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("VAR ").Append(dia);

        if (!string.IsNullOrWhiteSpace(sufijo))
        {
            sb.Append(' ').Append(sufijo!.Trim());
        }

        if (sepCm > 0)
        {
            sb.Append(" @ ").Append(Numero(sepCm)).Append(" cm");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Las dos direcciones de una parrilla llevan el mismo diámetro y la misma
    /// separación.
    /// </summary>
    public static bool MismoArmado(VarCad a, double sepA, VarCad b, double sepB)
    {
        var da = Dia(a);
        var db = Dia(b);

        if (da.Length == 0 || db.Length == 0)
        {
            return false;
        }

        return da == db && Numero(sepA) == Numero(sepB);
    }

    /// <summary>
    /// Rótulo de una parrilla en el <b>alzado</b>, con sus dos direcciones.
    /// </summary>
    /// <remarks>
    /// Cuando el armado coincide en las dos direcciones la macro no repite el
    /// renglón: escribe el armado y debajo <c>AMBOS SENTIDOS</c>. Si no coincide,
    /// devuelve las dos líneas por separado, que en la macro van a dos MText
    /// distintos con su propio leader; de ahí que esto entregue una tupla y no una
    /// sola cadena.
    /// </remarks>
    /// <param name="sufijoBarra">
    /// Cómo se nombra la varilla que se ve de canto: <c>INFERIOR</c> en la parrilla
    /// de abajo y <c>SUPERIOR</c> en la de arriba.
    /// </param>
    /// <param name="sufijoTransversal">Cómo se nombra la perpendicular.</param>
    public static (string Unico, string Barra, string Transversal) ParrillaAlzado(
        ParrillaCad p, string sufijoBarra, string sufijoTransversal)
    {
        if (p.AmbosSentidos)
        {
            return (VarSep(p.Barra, p.SepBarraCm) + SaltoLinea + AmbosSentidos,
                    string.Empty, string.Empty);
        }

        return (string.Empty,
                VarSep(p.Barra, p.SepBarraCm, sufijoBarra),
                VarSep(p.Transversal, p.SepTransversalCm, sufijoTransversal));
    }

    /// <summary>
    /// Rótulo de una parrilla en la <b>planta</b>: título y las dos direcciones, en
    /// un solo MText de tres renglones.
    /// </summary>
    /// <param name="titulo">
    /// <see cref="ParrillaInferior"/> o <see cref="ParrillaSuperior"/>. Va vacío
    /// cuando la zapata lleva una sola parrilla: entonces la macro escribe solo los
    /// dos armados, sin encabezado.
    /// </param>
    public static string ParrillaPlanta(string? titulo, ParrillaCad p)
    {
        var lineas = new List<string>();

        if (!string.IsNullOrWhiteSpace(titulo))
        {
            lineas.Add(titulo!.Trim());
        }

        lineas.Add(VarSep(p.Barra, p.SepBarraCm));
        lineas.Add(VarSep(p.Transversal, p.SepTransversalCm));

        return string.Join(SaltoLinea, lineas.Where(l => l.Length > 0));
    }

    /// <summary>
    /// Varillas longitudinales del dado o de la columna, agrupadas por diámetro:
    /// <c>16 VAR #4</c>, o <c>8 VAR #6 + 4 VAR #4</c> si se mezclan.
    /// </summary>
    /// <remarks>
    /// Port de <c>TextoBarrasLongitudinales</c>. Suma las cantidades que comparten
    /// diámetro y conserva el orden en que aparecen: superiores, inferiores,
    /// intermedias.
    /// </remarks>
    public static string BarrasLongitudinales(ElementoVerticalCad e)
    {
        var orden = new List<string>();
        var cuenta = new Dictionary<string, int>(StringComparer.Ordinal);

        void Sumar(int n, VarCad v)
        {
            var dia = Dia(v);
            if (dia.Length == 0 || n <= 0)
            {
                return;
            }

            if (!cuenta.ContainsKey(dia))
            {
                orden.Add(dia);
                cuenta[dia] = 0;
            }

            cuenta[dia] += n;
        }

        Sumar(e.NSuperior, e.Superior);
        Sumar(e.NInferior, e.Inferior);
        Sumar(e.NIntermedias, e.IntermediaEfectiva);

        return string.Join(" + ", orden.Select(d => $"{cuenta[d]} VAR {d}"));
    }

    /// <summary>
    /// Estribos del dado o de la columna: <c>EST #3 @ 8 cm</c>.
    /// </summary>
    /// <remarks>
    /// Port de <c>TextoEstribosElemento</c>. La separación se escribe <b>tal como se
    /// capturó</b>, solo limpiando la unidad y los espacios, para que un
    /// <c>8-10-8</c> se lea completo en el rótulo. Sin diámetro no hay renglón.
    /// </remarks>
    public static string Estribos(VarCad estribo, string? separaciones)
    {
        var dia = Dia(estribo);
        if (dia.Length == 0)
        {
            return string.Empty;
        }

        var esp = (separaciones ?? string.Empty).ToUpperInvariant()
            .Replace("CM", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(",", ".")
            .Trim();

        return esp.Length == 0 ? $"EST {dia}" : $"EST {dia} @ {esp} cm";
    }

    /// <summary>
    /// Rótulo completo del dado o de la columna, los tres renglones que van con
    /// leader:
    /// </summary>
    /// <remarks>
    /// <code>
    /// DADO "D-1"
    /// 16 VAR #4
    /// EST #3 @ 8 cm
    /// </code>
    /// Port de <c>TextoRotuloElementoVertical</c>. El título va en mayúsculas y el ID
    /// entrecomillado; los renglones que salgan vacíos no se escriben.
    /// </remarks>
    public static string ElementoVertical(ElementoVerticalCad e)
    {
        var titulo = (e.Elemento ?? string.Empty).Trim().ToUpperInvariant();

        var id = (e.Id ?? string.Empty).Trim();
        if (id.Length > 0)
        {
            titulo += $" \"{id}\"";
        }

        var lineas = new List<string> { titulo };

        var largas = BarrasLongitudinales(e);
        if (largas.Length > 0)
        {
            lineas.Add(largas);
        }

        var est = Estribos(e.Estribo, e.Separaciones);
        if (est.Length > 0)
        {
            lineas.Add(est);
        }

        return string.Join(SaltoLinea, lineas);
    }

    /// <summary>
    /// Etiqueta del diámetro con almohadilla: <c>#4</c>.
    /// </summary>
    /// <remarks>Port de <c>NormalizeDiaLabel</c> más <c>LimpiarTextoVar</c>.</remarks>
    public static string Dia(VarCad v)
    {
        if (!v.Existe)
        {
            return string.Empty;
        }

        var t = (v.Clave ?? string.Empty).Trim().ToUpperInvariant();
        if (t.Length == 0)
        {
            return string.Empty;
        }

        return t.Contains('#') ? t : "#" + t;
    }

    /// <summary>
    /// Número sin ceros de más: <c>15</c>, <c>12.5</c>.
    /// </summary>
    /// <remarks>
    /// Port de <c>FormatoNumeroSimple</c> y <c>LimpiarSeparacion</c>. Se fuerza la
    /// cultura invariante para que una máquina en español no escriba <c>12,5</c> en
    /// el dibujo.
    /// </remarks>
    public static string Numero(double v) =>
        Math.Abs(v - Math.Truncate(v)) < 1e-9
            ? ((long)Math.Truncate(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Primer número que aparece en un texto. Port de <c>ExtractNumber</c>.
    /// </summary>
    public static double ExtraeNumero(string? texto)
    {
        var t = (texto ?? string.Empty).Replace(',', '.');
        var sb = new StringBuilder();
        var empezo = false;

        foreach (var c in t)
        {
            if (char.IsAsciiDigit(c) || c == '.')
            {
                sb.Append(c);
                empezo = true;
            }
            else if (empezo)
            {
                break;
            }
        }

        return sb.Length > 0 &&
               double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }
}
