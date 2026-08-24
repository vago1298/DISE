namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>rótulo de la planta</b>: qué dice cada renglón y de qué tamaño va.
/// </summary>
/// <remarks>
/// Port de <c>DibujarTitulo</c>, <c>NombreDeNivel</c>, <c>NumeroDeStory</c> y
/// <c>EsCimentacion</c>, en la parte que no toca AutoCAD. Son cuatro reglas y las cuatro
/// tienen truco, así que están aquí sueltas y con prueba propia.
/// </remarks>
public sealed class RotuloPlanta
{
    private readonly ConfigPlano _cfg;

    public RotuloPlanta(ConfigPlano cfg) => _cfg = cfg;

    /// <summary>El primer renglón: <c>ROTULO_TITULO</c>.</summary>
    public string Titulo
    {
        get
        {
            var s = _cfg.Texto("ROTULO_TITULO", "PLANTA  ESTRUCTURAL");
            return s.Length == 0 ? "PLANTA  ESTRUCTURAL" : s;
        }
    }

    /// <summary>Altura del título: <c>ROTULO_ALTURA_TITULO</c>.</summary>
    public double AlturaTitulo => Positivo(_cfg.Numero("ROTULO_ALTURA_TITULO", 0.52), 0.52);

    /// <summary>Altura del segundo renglón: <c>ROTULO_ALTURA_NIVEL</c>.</summary>
    public double AlturaNivel => Positivo(_cfg.Numero("ROTULO_ALTURA_NIVEL", 0.26), 0.26);

    /// <summary>Estilo de texto del rótulo: <c>ROTULO_ESTILO_TEXTO</c>.</summary>
    public string Estilo
    {
        get
        {
            var s = _cfg.Texto("ROTULO_ESTILO_TEXTO", "HAETTENSCHWEILER");
            return s.Length == 0 ? "HAETTENSCHWEILER" : s;
        }
    }

    /// <summary>¿Va centrado a la mitad de la planta? <c>ROTULO_CENTRADO</c>.</summary>
    public bool Centrado => _cfg.Bandera("ROTULO_CENTRADO", true);

    /// <summary>¿Lleva la línea entre los dos renglones? <c>ROTULO_LINEA</c>.</summary>
    public bool ConLinea => _cfg.Bandera("ROTULO_LINEA", true);

    /// <summary>Aire entre los ejes de abajo y el rótulo: <c>ROTULO_SEPARACION_EJES</c>.</summary>
    public double SeparacionEjes
    {
        get
        {
            var s = _cfg.Numero("ROTULO_SEPARACION_EJES", 0.5);
            return s < 0 ? 0 : s;
        }
    }

    /// <summary>
    /// El <b>segundo renglón</b>: el nombre del nivel, y detrás la escala.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <c>NombreDeNivel</c> + <c>ROTULO_ESCALA</c>. Y tiene tres casos, en este orden:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     Si el nivel es la <b>base</b> —su nombre está en <c>CIMENTACION_STORIES</c>— el
    ///     rótulo dice <c>CIMENTACION</c>, no «BASE».
    ///   </item>
    ///   <item>
    ///     Si el nombre acaba en un número —<c>Story1</c>, <c>N 3</c>— se busca en
    ///     <c>ROTULO_NIVELES</c>: <c>Story1</c> es el primero de la lista, o sea PLANTA BAJA,
    ///     <c>Story2</c> el segundo, PRIMER NIVEL… Es lo que hace que el plano diga «PRIMER
    ///     NIVEL» y no «STORY2», que es lo que se rotula de verdad.
    ///   </item>
    ///   <item>Y si no se reconoce, el nombre tal cual, en mayúsculas.</item>
    /// </list>
    /// </remarks>
    public string RenglonDelNivel(string story)
    {
        var nombre = NombreDeNivel(story);
        var escala = _cfg.Texto("ROTULO_ESCALA", "esc. 1/75");

        return escala.Length == 0 ? nombre : $"{nombre} {escala}";
    }

    /// <summary>Solo el nombre, sin la escala.</summary>
    public string NombreDeNivel(string story)
    {
        if (EsCimentacion(story))
        {
            var s = _cfg.Texto("ROTULO_NOMBRE_CIMENTACION", "CIMENTACION").ToUpperInvariant();
            return s.Length == 0 ? "CIMENTACION" : s;
        }

        var n = NumeroDeStory(story);

        if (n >= 1)
        {
            var piezas = _cfg.Texto("ROTULO_NIVELES").Split(',');

            if (n - 1 <= piezas.Length - 1)
            {
                var s = piezas[n - 1].Trim().ToUpperInvariant();

                if (s.Length > 0)
                {
                    return s;
                }
            }

            return $"NIVEL {n - 1}";
        }

        return story.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// ¿Este nivel es la <b>cimentación</b>? Es <c>EsCimentacion</c> de la macro.
    /// </summary>
    /// <remarks>
    /// La comparación es <b>exacta</b> contra cada palabra de <c>CIMENTACION_STORIES</c>, ya
    /// normalizada, y no «contiene»: así un <c>Basement</c> o un <c>Base2</c> no se toman por
    /// la base. Ese detalle está en la macro y es de los que se pierden al portar.
    /// </remarks>
    public bool EsCimentacion(string story)
    {
        var t = Normalizar(story);

        if (t.Length == 0)
        {
            return false;
        }

        foreach (var pieza in _cfg.Texto("CIMENTACION_STORIES", "BASE,CIMENTACION,FOUNDATION")
                                  .Split(','))
        {
            var p = Normalizar(pieza);

            if (p.Length > 0 && t == p)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// El número con el que <b>acaba</b> el nombre del nivel: <c>Story3</c> → 3.
    /// </summary>
    /// <remarks>
    /// Se lee de <b>atrás hacia adelante</b> y se para en el primer carácter que no es cifra,
    /// como <c>NumeroDeStory</c>. Así <c>N 12</c> da 12 y no 1, y <c>2do Piso</c> da 0
    /// —porque el número no está al final—, que es lo que hace la macro.
    /// </remarks>
    public static int NumeroDeStory(string story)
    {
        var d = string.Empty;

        for (var i = story.Length - 1; i >= 0; i--)
        {
            if (char.IsAsciiDigit(story[i]))
            {
                d = story[i] + d;
            }
            else if (d.Length > 0)
            {
                break;
            }
        }

        return d.Length > 0 && int.TryParse(d, out var n) ? n : 0;
    }

    /// <summary>Mayúsculas, sin acentos, sin espacios: el <c>Norm</c> de la macro.</summary>
    public static string Normalizar(string s)
    {
        var t = s.ToUpperInvariant().Trim()
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I')
            .Replace('Ó', 'O').Replace('Ú', 'U').Replace('Ñ', 'N');

        return new string(t.Where(c => (c >= 'A' && c <= 'Z') || char.IsAsciiDigit(c) || c == '.')
                           .ToArray());
    }

    private static double Positivo(double v, double omision) => v > 0 ? v : omision;
}
