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

    /// <summary>Cartabones: cantidad total por dirección. Celdas C18 y C19.</summary>
    public int NCartabonesX { get; set; }
    public int NCartabonesY { get; set; }

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

    /// <summary>Cómo se reparten las anclas.</summary>
    public AnclasPlacaBase.Modo ModoAnclas { get; set; } = AnclasPlacaBase.Modo.Perimetral;

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
