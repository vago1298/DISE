namespace CadLink.Cad;

/// <summary>
/// Las <b>capas, colores y patrones</b> de la macro de placa base, tal cual.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió respetar todo: capas, colores y configuraciones. Están aquí, en un solo sitio y con el
/// mismo nombre que tenían las constantes de la macro, para que se puedan comparar renglón por
/// renglón contra el VBA sin buscarlas por el código.
/// </para>
/// <para>
/// <b>No van a la hoja de configuración del plano estructural</b> —<c>ConfigPlano</c>— a propósito:
/// esa hoja es la de la macro de plantas, y mezclar las dos haría que cambiar el color de una capa
/// del plano moviera el de la placa base. Son dos macros distintas y cada una trae sus valores.
/// </para>
/// </remarks>
public static class PlacaBaseCapas
{
    /// <summary>La placa, su contorno y los agujeros de las anclas.</summary>
    public const string Placa = "PLACA BASE";
    public const int ColorPlaca = 140;

    /// <summary>Ancho de la polilínea del contorno de la placa: el <c>PEDIT Width</c>.</summary>
    public const double AnchoLineaPlaca = 0.0004;

    /// <summary>Las anclas, en rojo.</summary>
    public const string Anclas = "ANCLAS";
    public const int ColorAnclas = 1;

    /// <summary>El rotulado, los leaders y sus flechas. En verde.</summary>
    public const string Rotulos = "ROTULOS";
    public const int ColorRotulos = 3;

    public const string Cotas = "COTAS";
    public const int ColorCotas = 7;

    /// <summary>El <b>dado</b> va aquí, no en la capa de la placa.</summary>
    public const string Concreto = "CONCRETO";
    public const int ColorConcreto = 8;

    /// <summary>Todos los perfiles, sea cual sea su familia.</summary>
    public const string Perfiles = "PERFILES";
    public const int ColorPerfiles = 7;

    public const string Cartabones = "CARTABONES";
    public const int ColorCartabones = 140;

    public const string Soldadura = "SOLDADURA";
    public const int ColorSoldadura = 240;

    /// <summary>
    /// La soldadura de los <b>cartabones</b>, en su propia capa y en <b>morado</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aparte de la soldadura del perfil a propósito: son dos filetes con espesores distintos —cada
    /// uno tiene su celda en la hoja— y en el detalle se rotulan por separado. Con las dos en la
    /// misma capa no se podrían apagar por separado ni distinguir de un vistazo cuál es cuál.
    /// </para>
    /// <para>
    /// El <b>210</b> de la paleta de AutoCAD es el violeta. Si el tono no es el que se quiere, se
    /// cambia aquí y solo aquí: la capa se crea con este color forzado.
    /// </para>
    /// </remarks>
    public const string SoldaduraCartabon = "SOLDADURA CARTABON";
    public const int ColorSoldaduraCartabon = 210;

    // ---------- Hatches ----------

    /// <summary>Rayado del dado, solo en la franja que sobresale de la placa.</summary>
    public const string PatronDado = "AR-CONC";
    public const double EscalaHatchDado = 0.0002;

    /// <summary>Rayado del perfil, para las familias con forma de I.</summary>
    public const string PatronPerfilI = "ANSI32";
    public const double EscalaHatchPerfilI = 0.0009;
    public const int ColorHatchPerfilI = 252;

    /// <summary>Ancho de la polilínea del contorno de un perfil I.</summary>
    public const double AnchoContornoPerfilI = 0.001;

    /// <summary>Rayado de la soldadura: la franja entre el perfil y su offset.</summary>
    public const string PatronSoldadura = "JIS_RC_10";
    public const double EscalaHatchSoldadura = 0.0005;
    public const int ColorLineasSoldadura = 240;

    // ---------- Estilos ----------

    public const string EstiloCota = "COTA_ACERO";

    /// <summary>Estilo de texto propio de esta macro, con altura fija.</summary>
    public const string EstiloTexto = "ACERO_PLACA";
    public const double AlturaTextoDwg = 0.016;

    /// <summary>Estilo del MTEXT del rótulo. Si no está en el dibujo se usa el de arriba.</summary>
    public const string EstiloRotulo = "SECCIONES";

    public const string FuenteTexto = "Bahnschrift Light SemiCondensed";

    /// <summary>Flecha de las cotas: la marca oblicua, no el triángulo relleno.</summary>
    public const string FlechaCota = "_OBLIQUE";

    /// <summary>Líneas de extensión del estilo de cota, en unidades de dibujo.</summary>
    public const double DimExe = 0.0;
    public const double DimExo = 0.04;

    /// <summary>Tamaño de flecha ploteado, en mm.</summary>
    public const double AltoFlechaMm = 1.5;
}

/// <summary>
/// El paño exterior de la columna, tal como queda dibujado.
/// </summary>
/// <remarks>
/// Las nueve formas del manual caben en dos casos: siete son una polilínea —con o sin arcos— y dos
/// son una circunferencia. Se guardan aparte porque lo que se hace con ellas es distinto: la
/// poligonal se desplaza vértice a vértice y la circunferencia solo crece de radio.
/// </remarks>
public sealed record ContornoDeColumna
{
    /// <summary>Plano y cerrado: <c>x1,y1,x2,y2…</c>. Nulo si la columna es redonda.</summary>
    public double[]? Puntos { get; init; }

    /// <summary>Los bulges de los vértices que llevan arco.</summary>
    public (int Indice, double Bulge)[]? Dobleces { get; init; }

    /// <summary>La circunferencia, para el tubo redondo y el redondo macizo.</summary>
    public (double Cx, double Cy, double R)? Circulo { get; init; }
}

/// <summary>
/// Todo lo que hace falta para dibujar una placa base, ya en <b>centímetros</b>.
/// </summary>
/// <remarks>
/// <para>
/// Las conversiones de la macro —pulgadas a centímetros de las anclas, metros a centímetros de la
/// placa— se hacen al leer la fila, no aquí. Este objeto llega con todo en cm, que es la unidad en
/// la que el detalle se acota y se rotula, y el dibujante lo pasa a unidades de dibujo con una sola
/// escala. Es el mismo reparto que usan <see cref="SeccionCad"/> y <see cref="AlzadoCad"/>.
/// </para>
/// </remarks>
public sealed class PlacaBaseCad
{
    /// <summary>La marca de la placa, para el rótulo. Celda E2.</summary>
    public string Marca { get; set; } = string.Empty;

    /// <summary>Largo de la placa, en cm. Va al eje Y si no se gira. Celda C5.</summary>
    public double LargoCm { get; set; }

    /// <summary>Ancho de la placa, en cm. Va al eje X si no se gira. Celda C6.</summary>
    public double AnchoCm { get; set; }

    /// <summary>Espesor, tal como se escribe en la hoja —en pulgadas—. Celda E6.</summary>
    public string Espesor { get; set; } = string.Empty;

    /// <summary>
    /// El mismo espesor <b>en centímetros</b>, para poder dibujarlo.
    /// </summary>
    /// <remarks>
    /// El texto no sobra: la hoja lo captura como fracción —<c>3/4</c>, <c>1 1/4</c>— y el rótulo
    /// tiene que decir la fracción. Pero en el <b>alzado</b> la placa se ve de canto y su espesor
    /// <i>es</i> el dibujo, así que hace falta el número. Se resuelve al leer la fila, donde ya vive
    /// el lector de fracciones, y no aquí.
    /// </remarks>
    public double EspesorCm { get; set; }

    /// <summary>Tipo de acero de la placa. Celda E5.</summary>
    public string AceroPlaca { get; set; } = string.Empty;

    /// <summary>Dado de concreto, en cm. Celdas D7 y E7. Cero = sin dado.</summary>
    public double DadoXCm { get; set; }
    public double DadoYCm { get; set; }

    /// <summary>
    /// El dado es <b>redondo</b>. Su diámetro es <see cref="DadoXCm"/>.
    /// </summary>
    /// <remarks>
    /// La macro solo dibujaba dados rectangulares porque su hoja solo daba dos medidas. Ahora el
    /// dado se toma de la hoja de secciones de concreto, y ahí un <c>DADO CIRCULAR</c> es otra
    /// forma, no un cuadrado con las mismas medidas: dibujarlo cuadrado pondría en el plano, con el
    /// mismo ID, un dado que no es el que se armó.
    /// </remarks>
    public bool DadoCircular { get; set; }

    /// <summary>Familia del perfil de la columna. Celda C8.</summary>
    public string Familia { get; set; } = string.Empty;

    /// <summary>Designación del perfil. Celda C9.</summary>
    public string Seccion { get; set; } = string.Empty;

    /// <summary>La geometría del perfil, ya resuelta desde el catálogo.</summary>
    public PerfilAceroCad? Perfil { get; set; }

    /// <summary>Número de anclas en X —horizontal—. Celda C11.</summary>
    public int NAnclasX { get; set; }

    /// <summary>Número de anclas en Y —vertical—. Celda C10.</summary>
    public int NAnclasY { get; set; }

    /// <summary>Separación al borde, en cm. Cero = automática. Celdas E11 y E10.</summary>
    public double SepBordeXCm { get; set; }
    public double SepBordeYCm { get; set; }

    /// <summary>Diámetros de ancla, en cm. Celdas C14 y C15.</summary>
    public double DiamAnclaXCm { get; set; }
    public double DiamAnclaYCm { get; set; }

    /// <summary>Diámetros de agujero, en cm. Celdas E14 y E15.</summary>
    public double DiamAgujeroXCm { get; set; }
    public double DiamAgujeroYCm { get; set; }

    /// <summary>Los diámetros <b>tal como se escriben</b> en la hoja, para los rótulos.</summary>
    /// <remarks>
    /// Se guardan aparte del número porque en la hoja se capturan como fracción —<c>5/8</c>,
    /// <c>3/4</c>— y el rótulo tiene que decir la fracción, no su decimal. Un plano que pide anclas
    /// de <c>0.625"</c> en lugar de <c>5/8"</c> obliga a traducir en obra.
    /// </remarks>
    public string TextoDiamAnclaX { get; set; } = string.Empty;
    public string TextoDiamAnclaY { get; set; } = string.Empty;
    public string TextoDiamAgujeroX { get; set; } = string.Empty;
    public string TextoDiamAgujeroY { get; set; } = string.Empty;

    /// <summary>Electrodo de la soldadura. Celda C16.</summary>
    public string Electrodo { get; set; } = string.Empty;

    /// <summary>Espesor de soldadura, en cm, y su texto. Celda C17.</summary>
    public double SoldaduraCm { get; set; }
    public string TextoSoldadura { get; set; } = string.Empty;

    /// <summary>
    /// Espesor del filete que une los <b>cartabones</b> a la placa y a la columna, en cm.
    /// </summary>
    /// <remarks>
    /// Es un dato aparte del de la columna porque son dos soldaduras distintas: el cartabón es una
    /// placa más delgada, así que su filete casi nunca es el mismo. Cero = sin soldadura de cartabón.
    /// </remarks>
    public double SoldaduraCartabonCm { get; set; }

    /// <summary>El espesor del filete de cartabón tal como se escribió, para el rótulo.</summary>
    public string TextoSoldaduraCartabon { get; set; } = string.Empty;

    /// <summary>Cartabones: cantidad total por dirección. Celdas C18 y C19.</summary>
    public int NCartabonesX { get; set; }
    public int NCartabonesY { get; set; }

    /// <summary>
    /// Lo que <b>sube</b> el cartabón, en cm. Celdas <b>F18</b> para X y <b>F19</b> para Y.
    /// </summary>
    /// <remarks>
    /// Solo se ve en el <b>alzado</b>: en planta el cartabón se ve de canto, así que su altura no
    /// aparece por ningún lado. En cero, el cartabón no sale en el alzado —una placa de altura nula
    /// es una línea suelta al lado de la columna, y eso parece un error del dibujo—.
    /// </remarks>
    public double AltoCartabonXCm { get; set; }
    public double AltoCartabonYCm { get; set; }

    /// <summary>
    /// Lo que se <b>ahoga</b> el ancla en el concreto, en cm. Celdas <b>E12</b> y <b>E13</b>.
    /// </summary>
    /// <remarks>
    /// Como la altura del cartabón, solo se ve en el alzado. Y además gobierna la profundidad del
    /// dado que se dibuja ahí: el concreto baja 5 cm más que el ancla, para que la punta quede
    /// dentro. En cero, el alzado sale sin anclas.
    /// </remarks>
    public double LongAnclajeXCm { get; set; }
    public double LongAnclajeYCm { get; set; }

    /// <summary>
    /// La longitud <b>total desarrollada</b> del ancla, en cm: lo que se corta, doblez incluido.
    /// </summary>
    /// <remarks>
    /// <b>Manda sobre el ahogo</b>, que pasa a ser la consecuencia: lo que queda dentro del concreto
    /// una vez descontado lo que el ancla gasta atravesando la placa y saliendo a la tuerca. De las
    /// dos, esta es la que se puede verificar con una cinta en el taller. En cero se usa el ahogo,
    /// que es lo que se dibujaba antes de que existiera este dato.
    /// </remarks>
    public double LongAnclaXCm { get; set; }
    public double LongAnclaYCm { get; set; }

    /// <summary>La <b>pata</b> del doblez del extremo del ancla, en cm. Cero = ancla recta.</summary>
    /// <remarks>
    /// Con doblez, el ancla es una <b>L</b> y lo que la ancla es la pata, así que el travesaño del
    /// extremo desaparece: dibujar los dos pone en el plano un remate que la pieza no lleva. Las dos
    /// patas apuntan <b>hacia dentro</b>, una contra la otra, que es donde tienen recubrimiento.
    /// </remarks>
    public double DoblezAnclaXCm { get; set; }
    public double DoblezAnclaYCm { get; set; }

    /// <summary>Dibujar el <b>alzado</b> a la derecha de la planta.</summary>
    public bool DibujarElevacion { get; set; } = true;

    /// <summary>Espesor de los cartabones, en cm, y su texto. Celdas C20 y C21.</summary>
    public double EspCartabonXCm { get; set; }
    public double EspCartabonYCm { get; set; }
    public string TextoEspCartabonX { get; set; } = string.Empty;
    public string TextoEspCartabonY { get; set; } = string.Empty;

    /// <summary>
    /// Longitud de los cartabones, en cm. Celdas <b>E19 para X</b> y <b>E18 para Y</b>.
    /// </summary>
    /// <remarks>
    /// El cruce E19/E18 <b>no es un error</b>: es la corrección que la propia macro documenta. La
    /// hoja maneja las longitudes en el sentido opuesto al espesor visto en planta, así que los
    /// datos de X toman E19 y los de Y toman E18. Intercambiarlos dibuja los cartabones con la
    /// longitud del otro sentido.
    /// </remarks>
    public double LongCartabonXCm { get; set; }
    public double LongCartabonYCm { get; set; }

    /// <summary>Dibujar cartabones. Celda F6, «Si».</summary>
    public bool ConCartabones { get; set; }

    /// <summary>Escala del detalle, para el rótulo. Celda V o la de la hoja.</summary>
    public double Escala { get; set; } = 10;

    // ======================================================================
    //  OPCIONES DE DIBUJO
    // ======================================================================

    /// <summary>Gira 90° la placa. El dado gira con ella; las anclas y el rótulo, no.</summary>
    public bool GirarPlaca90 { get; set; } = true;

    /// <summary>El dado gira junto con la placa.</summary>
    public bool GirarDado90 { get; set; } = true;

    /// <summary>
    /// Gira 90° el perfil. Las formas de <b>I</b> se quedan verticales de todas formas.
    /// </summary>
    /// <remarks>
    /// Es la regla <c>GiroPerfil90PorTipo</c> de la macro: la geometría de una I ya nace vertical
    /// —patines horizontales y alma vertical— así que girarla la <b>acuesta</b>, que es justo lo que
    /// no se quiere en una columna.
    /// </remarks>
    public bool GirarPerfil90 { get; set; } = true;

    public bool DibujarDado { get; set; } = true;
    public bool DibujarPerfil { get; set; } = true;
    public bool DibujarSoldadura { get; set; } = true;
    public bool DibujarHatchSoldadura { get; set; } = true;
    public bool DibujarLeaders { get; set; } = true;
    public bool DibujarHatchDado { get; set; } = true;

    /// <summary>Aplica las tablas J y K de libramientos antes de dibujar.</summary>
    public bool ValidarSeparacionAnclas { get; set; } = true;

    /// <summary>Punto de inserción, en unidades de dibujo. Es la esquina inferior izquierda.</summary>
    public double InsercionX { get; set; }
    public double InsercionY { get; set; } = -2;

    /// <summary>El rótulo sube esto y baja <see cref="BajarRotuloCm"/>.</summary>
    public double SubirRotulo { get; set; } = 0.05;
    public double BajarRotuloCm { get; set; } = 2;

    /// <summary>Interlínea del MTEXT del rótulo.</summary>
    public double SeparacionLineas { get; set; } = 1;

    /// <summary>Las medidas de la placa <b>ya orientadas</b> en el dibujo, en cm.</summary>
    public double AnchoDibujoCm => GirarPlaca90 ? LargoCm : AnchoCm;

    /// <summary>El alto de la placa ya orientada, en cm.</summary>
    public double AltoDibujoCm => GirarPlaca90 ? AnchoCm : LargoCm;

    /// <summary>El dado ya orientado, en cm.</summary>
    public double DadoXDibujoCm => GirarPlaca90 && GirarDado90 ? DadoYCm : DadoXCm;

    /// <summary>El dado ya orientado, en cm.</summary>
    public double DadoYDibujoCm => GirarPlaca90 && GirarDado90 ? DadoXCm : DadoYCm;

    /// <summary>
    /// El paño exterior de la columna, <b>tal como se va a dibujar</b>: ya girado y a escala.
    /// </summary>
    /// <param name="xc">Centro del perfil, en unidades de dibujo.</param>
    /// <param name="yc">Centro del perfil, en unidades de dibujo.</param>
    /// <param name="escala">Cuántas unidades de dibujo mide un centímetro.</param>
    /// <remarks>
    /// <para>
    /// Vive aquí y no en el dibujante porque lo necesitan <b>tres</b>: el dibujante, para trazarlo y
    /// para rodearlo de soldadura; la revisión de la <b>columna L</b> del estándar, que mide la
    /// holgura del ancla contra este paño y corre <i>antes</i> de dibujar nada; y la vista previa de
    /// la hoja. Con la cuenta en el dibujante, la revisión no podía llegar a ella —el perfil todavía
    /// no existe cuando hay que decidir si la placa se dibuja o no—.
    /// </para>
    /// <para>
    /// Y con tres copias de «el contorno ya girado», el día que cambie el giro dos de las tres se
    /// quedan atrás: la soldadura rodearía un perfil y la revisión mediría contra otro.
    /// </para>
    /// </remarks>
    public ContornoDeColumna? PanoDeLaColumna(double xc, double yc, double escala)
    {
        if (Perfil is null || escala <= 0)
        {
            return null;
        }

        var ancho = Perfil.AnchoDibujoCm * escala;
        var alto = Perfil.AltoDibujoCm * escala;

        var trazo = TrazoAcero.De(Perfil, xc - (ancho / 2), yc - (alto / 2), escala);

        if (trazo is null)
        {
            return null;
        }

        if (trazo.Exterior is { } contorno)
        {
            return new ContornoDeColumna
            {
                Puntos = GiraElPerfil
                    ? ContornoDesplazado.Girar90(contorno.Puntos, xc, yc)
                    : contorno.Puntos,
                Dobleces = contorno.Dobleces
            };
        }

        if (trazo.CircExterior is { } circulo)
        {
            var (cx, cy) = GiraElPerfil
                ? ContornoDesplazado.Girar90Punto(circulo.Cx, circulo.Cy, xc, yc)
                : (circulo.Cx, circulo.Cy);

            return new ContornoDeColumna { Circulo = (cx, cy, circulo.R) };
        }

        return null;
    }

    /// <summary>
    /// Lo que ocupa el detalle a lo ancho, en cm: la placa o el dado, el que sobresalga.
    /// </summary>
    /// <remarks>
    /// Hace falta para repartir varias placas en fila sin que se pisen. El dado suele ser
    /// <b>más grande</b> que la placa —es lo normal: la placa se apoya en él—, así que separar
    /// las placas por el ancho de la placa encima el dado de una con el de la siguiente.
    /// </remarks>
    public double AnchoTotalDibujoCm => Math.Max(AnchoDibujoCm, DadoXDibujoCm);

    /// <summary>
    /// ¿Se gira el perfil de la columna? Las formas de <b>I</b> nunca.
    /// </summary>
    /// <remarks>
    /// Es <c>GiroPerfil90PorTipo</c> de la macro. La geometría de una I ya nace vertical —patines
    /// horizontales y alma vertical—, que es como va una columna, así que girarla la acuesta.
    /// </remarks>
    public bool GiraElPerfil =>
        GirarPerfil90 &&
        !string.Equals(Perfil?.Forma, FormaAcero.I, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Las medidas del perfil <b>ya orientadas</b> en el dibujo, en cm. Cero si no hay perfil.
    /// </summary>
    /// <remarks>
    /// <b>Viven aquí y no en el dibujante a propósito.</b> Con ellas se calcula la separación al
    /// borde de las anclas, y esa cuenta la hacen DOS sitios: el dibujante, al dibujar, y la
    /// columna «Libramientos» de la tabla, al capturar. Con dos copias de la cuenta, la tabla
    /// puede decir que la placa cumple y el dibujante negarse a dibujarla —o al contrario—, y ese
    /// desacuerdo no tendría ninguna explicación visible para el usuario.
    /// </remarks>
    public double PerfilXDibujoCm =>
        Perfil is null ? 0 : GiraElPerfil ? Perfil.AltoDibujoCm : Perfil.AnchoDibujoCm;

    /// <summary>El perfil ya orientado, en cm. Ver la nota de <see cref="PerfilXDibujoCm"/>.</summary>
    public double PerfilYDibujoCm =>
        Perfil is null ? 0 : GiraElPerfil ? Perfil.AnchoDibujoCm : Perfil.AltoDibujoCm;

    /// <summary>Lo que falta para poder dibujar. Vacío = se puede.</summary>
    /// <remarks>
    /// Se contesta con una lista y no con un <c>bool</c> por lo mismo que en el resto del programa:
    /// «no se puede dibujar» sin decir qué falta obliga a probar celda por celda.
    /// </remarks>
    public IReadOnlyList<string> Falta
    {
        get
        {
            var falta = new List<string>();

            if (LargoCm <= 0) { falta.Add("el largo de la placa"); }
            if (AnchoCm <= 0) { falta.Add("el ancho de la placa"); }

            if (DiamAnclaXCm <= 0 && NAnclasX > 0)
            {
                falta.Add("el diámetro de las anclas en X");
            }

            if (DiamAnclaYCm <= 0 && NAnclasY > 0)
            {
                falta.Add("el diámetro de las anclas en Y");
            }

            if (DibujarPerfil && Perfil is null && Seccion.Trim().Length > 0)
            {
                falta.Add($"la sección «{Seccion}» no se encontró en el catálogo de perfiles");
            }

            return falta;
        }
    }
}
