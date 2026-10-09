using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CadLink.Revit.Nucleo;

/// <summary>Lo que el emparejador propone para una seccion.</summary>
public sealed class Sugerencia
{
    /// <summary>El tipo propuesto, o <c>null</c> si no hay nada razonable.</summary>
    public TipoRevit? Tipo { get; init; }

    public int Puntos { get; init; }

    /// <summary>En palabras, por que se propone eso. Va al tooltip de la fila.</summary>
    public string Porque { get; init; } = string.Empty;

    /// <summary>
    /// Si la propuesta es lo bastante buena para dejarla PUESTA en el cuadro.
    /// </summary>
    /// <remarks>
    /// Por debajo del umbral se ensena como pista pero la fila se deja vacia. Es a
    /// proposito: una sugerencia floja aceptada sin mirar produce un modelo con las
    /// secciones cambiadas, y eso es peor que un cuadro que obliga a elegir.
    /// </remarks>
    public bool EsBuena => Tipo is not null && Puntos >= Sugeridor.UmbralParaPreseleccionar;
}

/// <summary>
/// Propone, para cada seccion del modelo, el tipo de Revit que mejor le cuadra de entre los
/// que DE VERDAD hay en el proyecto.
/// </summary>
/// <remarks>
/// <para>
/// No inventa familias ni las carga: solo elige entre lo que el complemento encontro en el
/// documento abierto. Si en el proyecto no hay ninguna familia de perfiles de acero, una
/// seccion de acero se quedara sin sugerencia, y eso es la respuesta correcta.
/// </para>
/// <para>
/// El emparejamiento es por puntos y no por reglas rigidas, porque los nombres de familia
/// dependen de la plantilla de cada despacho y no hay ninguno que se pueda dar por hecho.
/// Se mira, en este orden de peso: la FORMA, las MEDIDAS y el NOMBRE.
/// </para>
/// </remarks>
public static class Sugeridor
{
    /// <summary>Puntos por acertar la forma, sabiendola de los parametros del tipo.</summary>
    public const int PuntosForma = 100;

    /// <summary>Puntos por acertar la forma, deduciendola del nombre.</summary>
    public const int PuntosFormaPorNombre = 60;

    /// <summary>Castigo por contradecir la forma. Descarta en la practica.</summary>
    public const int CastigoFormaDistinta = -80;

    /// <summary>Puntos maximos por que las medidas coincidan.</summary>
    public const int PuntosMedidas = 60;

    /// <summary>Puntos por que el nombre del tipo contenga el de la seccion, o al reves.</summary>
    public const int PuntosNombre = 40;

    /// <summary>A partir de aqui la sugerencia se deja puesta en el cuadro.</summary>
    public const int UmbralParaPreseleccionar = 80;

    // ==================================================================
    //  Deducir la forma de un nombre
    // ==================================================================

    /// <summary>
    /// Palabras que delatan una forma en el nombre de una familia o de un tipo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se prueban EN ESTE ORDEN, y gana la primera que aparece, porque las palabras se
    /// solapan: "tubular rectangular" contiene "rectangular", y un HSS es un cajon aunque
    /// su nombre no diga "cajon". Lo especifico va antes que lo general.
    /// </para>
    /// <para>
    /// Van en espanol y en ingles porque la plantilla de Revit puede estar en cualquiera de
    /// los dos, y los perfiles se nombran con siglas de tres normas distintas. No es una
    /// lista cerrada: lo que no se reconozca simplemente no puntua por forma.
    /// </para>
    /// </remarks>
    private static readonly (FormaSeccion Forma, string[] Palabras)[] Pistas =
    {
        // Redondo hueco antes que el cajon, porque "HSS redondo" lleva las dos palabras.
        (FormaSeccion.Tubo, new[]
        {
            "tubo redondo", "tubo circular", "tuberia", "pipe", "round hss", "hss round",
            "circular hueco", "hueco circular", "chs"
        }),

        (FormaSeccion.Cajon, new[]
        {
            "hss", "ptr", "cajon", "tubular rectangular", "rectangular hollow",
            "square hollow", "rhs", "shs", "tubo cuadrado", "tubo rectangular", "box"
        }),

        (FormaSeccion.PerfilI, new[]
        {
            "ala ancha", "wide flange", "perfil i", "viga i", "i-shape", "ishape",
            "ipr", "ipe", "ipn", "hea", "heb", "hem", "universal beam", "universal column",
            "w-wide", "patin ancho"
        }),

        (FormaSeccion.PerfilC, new[]
        {
            "canal", "channel", "perfil c", "upn", "upe", "c-shape"
        }),

        (FormaSeccion.PerfilT, new[]
        {
            "perfil t", "t-shape", "tshape", "tee"
        }),

        (FormaSeccion.PerfilL, new[]
        {
            "angulo", "angle", "perfil l", "l-shape", "lshape", "lados iguales",
            "lado igual"
        }),

        (FormaSeccion.Circulo, new[]
        {
            "circular", "redondo", "redonda", "round", "cilindrico", "cilindrica"
        }),

        (FormaSeccion.Rectangulo, new[]
        {
            "rectangular", "rectangulo", "rect", "cuadrado", "square",
            "hormigon", "concreto", "concrete"
        })
    };

    /// <summary>Siglas cortas que solo valen si van como palabra suelta.</summary>
    /// <remarks>
    /// "HE" o "W" dentro de una palabra cualquiera saltaria a cada rato. Con frontera de
    /// palabra, "HE100A" si cuenta y "Hormigon" no.
    /// </remarks>
    private static readonly (FormaSeccion Forma, string[] Siglas)[] Siglas =
    {
        (FormaSeccion.PerfilI, new[] { "w", "he", "ir", "ub", "uc", "hp" }),
        (FormaSeccion.PerfilC, new[] { "c", "mc", "ce" }),
        (FormaSeccion.PerfilL, new[] { "l", "li", "la" }),
        (FormaSeccion.PerfilT, new[] { "t", "wt", "mt" })
    };

    /// <summary>Deduce la forma de un texto, o <c>null</c> si no se reconoce nada.</summary>
    public static FormaSeccion? FormaPorNombre(string? texto)
    {
        var t = Normalizar(texto);

        if (t.Length == 0)
        {
            return null;
        }

        foreach (var (forma, palabras) in Pistas)
        {
            foreach (var p in palabras)
            {
                if (t.Contains(p, StringComparison.Ordinal))
                {
                    return forma;
                }
            }
        }

        foreach (var (forma, siglas) in Siglas)
        {
            foreach (var s in siglas)
            {
                // La sigla puede ir pegada al numero -"W18X50", "HE100A"- o suelta -"C 6".
                if (Regex.IsMatch(t, @"(^|[^a-z0-9])" + Regex.Escape(s) + @"\s*\d"))
                {
                    return forma;
                }
            }
        }

        return null;
    }

    /// <summary>Minusculas, sin acentos y con los separadores a espacio.</summary>
    private static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length);

        foreach (var c in s.Trim().ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'á' or 'à' or 'ä' or 'â' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                '_' or '-' or '/' or ':' => ' ',
                _ => c
            });
        }

        return sb.ToString();
    }

    // ==================================================================
    //  Emparejar
    // ==================================================================

    /// <summary>La mejor propuesta para una seccion.</summary>
    public static Sugerencia Para(SeccionDelModelo seccion, CatalogoRevit catalogo)
    {
        if (seccion is null)
        {
            throw new ArgumentNullException(nameof(seccion));
        }

        if (catalogo is null)
        {
            throw new ArgumentNullException(nameof(catalogo));
        }

        var candidatos = catalogo.DeCategoria(seccion.Categoria);

        if (candidatos.Count == 0)
        {
            return new Sugerencia
            {
                Tipo = null,
                Puntos = 0,
                Porque = "En el proyecto de Revit no hay ningun tipo de "
                         + Categorias.Nombre(seccion.Categoria).ToLowerInvariant()
                         + ". Carga una familia y vuelve a abrir el cuadro."
            };
        }

        TipoRevit? mejor = null;
        var mejorPuntos = int.MinValue;
        var mejorPorque = string.Empty;

        // El orden de los candidatos ya es estable -viene ordenado por familia y tipo-, y
        // solo se cambia de mejor con puntuacion ESTRICTAMENTE mayor. Asi, con dos empates,
        // gana siempre el primero alfabeticamente y la sugerencia no baila entre corridas.
        foreach (var c in candidatos)
        {
            var (puntos, porque) = Puntuar(seccion, c);

            if (puntos > mejorPuntos)
            {
                mejor = c;
                mejorPuntos = puntos;
                mejorPorque = porque;
            }
        }

        return new Sugerencia { Tipo = mejor, Puntos = mejorPuntos, Porque = mejorPorque };
    }

    /// <summary>Las propuestas de todas las secciones, por su clave.</summary>
    public static Dictionary<string, Sugerencia> ParaTodas(
        IEnumerable<SeccionDelModelo> secciones, CatalogoRevit catalogo)
    {
        var salida = new Dictionary<string, Sugerencia>(StringComparer.Ordinal);

        foreach (var s in secciones)
        {
            salida[s.Clave] = Para(s, catalogo);
        }

        return salida;
    }

    private static (int Puntos, string Porque) Puntuar(SeccionDelModelo s, TipoRevit c)
    {
        var puntos = 0;
        var razones = new List<string>();

        // ---- Forma ----
        var esPano = s.Clase is ClasePieza.Muro or ClasePieza.Losa;

        if (!esPano)
        {
            var delTipo = c.Forma ?? FormaPorNombre(c.NombreCompleto);

            if (delTipo is null)
            {
                razones.Add("no se pudo saber la forma del tipo de Revit");
            }
            else if (MismaFamiliaDeForma(delTipo.Value, s.Seccion.Forma))
            {
                var seguro = c.Forma is not null;
                puntos += seguro ? PuntosForma : PuntosFormaPorNombre;
                razones.Add(seguro
                    ? "la forma coincide, leida de los parametros del tipo"
                    : "la forma coincide, deducida del nombre del tipo");
            }
            else
            {
                puntos += CastigoFormaDistinta;
                razones.Add($"la forma no coincide: la seccion es {Legible(s.Seccion.Forma)} "
                            + $"y el tipo parece {Legible(delTipo.Value)}");
            }
        }

        // ---- Medidas ----
        var (cerca, detalle) = Medidas(s, c);

        if (cerca.HasValue)
        {
            var gana = (int)Math.Round(PuntosMedidas * cerca.Value);
            puntos += gana;
            razones.Add(detalle);
        }

        // ---- Nombre ----
        var nombreSeccion = Normalizar(s.Seccion.Nombre);
        var nombreTipo = Normalizar(c.Tipo);

        if (nombreSeccion.Length >= 2 && nombreTipo.Length >= 2
            && (nombreTipo.Contains(nombreSeccion, StringComparison.Ordinal)
                || nombreSeccion.Contains(nombreTipo, StringComparison.Ordinal)))
        {
            puntos += PuntosNombre;
            razones.Add("el nombre del tipo y el de la seccion se parecen");
        }

        return (puntos, string.Join("; ", razones));
    }

    /// <summary>
    /// Si dos formas son "la misma" para emparejar.
    /// </summary>
    /// <remarks>
    /// Un tubo y un circulo se aceptan entre si, y un cajon y un rectangulo tambien: en
    /// muchas plantillas no hay familia para la version hueca, y modelar un HSS con un
    /// rectangulo macizo del mismo tamano es mucho mejor que no modelarlo. Lo que NO se
    /// acepta es cruzar familias distintas, como poner una I donde va un angulo.
    /// </remarks>
    public static bool MismaFamiliaDeForma(FormaSeccion a, FormaSeccion b)
    {
        if (a == b)
        {
            return true;
        }

        static FormaSeccion Grupo(FormaSeccion f) => f switch
        {
            FormaSeccion.Tubo => FormaSeccion.Circulo,
            FormaSeccion.Cajon => FormaSeccion.Rectangulo,
            _ => f
        };

        return Grupo(a) == Grupo(b);
    }

    /// <summary>Cuanto se parecen las medidas: de 0 a 1, o <c>null</c> si no se sabe.</summary>
    private static (double? Cerca, string Detalle) Medidas(SeccionDelModelo s, TipoRevit c)
    {
        if (s.Clase is ClasePieza.Muro or ClasePieza.Losa)
        {
            if (c.EspesorM is null or <= 0 || s.Seccion.EspesorM <= 0)
            {
                return (null, string.Empty);
            }

            var e = Error(s.Seccion.EspesorM, c.EspesorM.Value);

            return (Cercania(e),
                $"el espesor del tipo es {Cm(c.EspesorM.Value)} cm y la seccion pide "
                + $"{Cm(s.Seccion.EspesorM)} cm");
        }

        if (c.AnchoM is null or <= 0 || c.PeralteM is null or <= 0
            || s.Seccion.AnchoM <= 0 || s.Seccion.PeralteM <= 0)
        {
            return (null, string.Empty);
        }

        // Se prueban las dos orientaciones y se queda la mejor. Un tipo de Revit de 30 x 60
        // sirve para una seccion de 60 x 30: lo que cambia es el giro de la pieza, no el
        // tipo, y obligar a que el orden coincida descartaria la mitad de los aciertos.
        var directo = (Error(s.Seccion.AnchoM, c.AnchoM.Value)
                       + Error(s.Seccion.PeralteM, c.PeralteM.Value)) / 2;

        var girado = (Error(s.Seccion.AnchoM, c.PeralteM.Value)
                      + Error(s.Seccion.PeralteM, c.AnchoM.Value)) / 2;

        var e2 = Math.Min(directo, girado);

        return (Cercania(e2),
            $"el tipo mide {Cm(c.AnchoM.Value)} x {Cm(c.PeralteM.Value)} cm y la seccion pide "
            + $"{Cm(s.Seccion.AnchoM)} x {Cm(s.Seccion.PeralteM)} cm");
    }

    private static double Error(double pedido, double tiene) =>
        pedido <= 0 ? 1 : Math.Abs(tiene - pedido) / pedido;

    /// <summary>
    /// Pasa un error relativo a una cercania de 0 a 1.
    /// </summary>
    /// <remarks>
    /// Se anula al 25 % de error. Mas alla de eso, dos secciones ya no son "parecidas": una
    /// columna de 30 y otra de 40 son columnas distintas, y proponer una por la otra seria
    /// peor que no proponer nada.
    /// </remarks>
    private static double Cercania(double error) => Math.Max(0, 1 - (error * 4));

    private static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>El nombre de una forma, para los mensajes.</summary>
    public static string Legible(FormaSeccion f) => f switch
    {
        FormaSeccion.Rectangulo => "rectangular",
        FormaSeccion.Circulo => "circular",
        FormaSeccion.PerfilI => "un perfil I",
        FormaSeccion.PerfilC => "un canal",
        FormaSeccion.PerfilT => "una te",
        FormaSeccion.PerfilL => "un angulo",
        FormaSeccion.Tubo => "un tubo redondo",
        FormaSeccion.Cajon => "un cajon",
        FormaSeccion.Pano => "un pano",
        _ => f.ToString()
    };
}
