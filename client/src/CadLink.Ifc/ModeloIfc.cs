namespace CadLink.Ifc;

/// <summary>Que es cada elemento, para elegir la entidad de IFC que le toca.</summary>
public enum ClaseIfc
{
    Columna,
    Trabe,
    Diagonal,
    Muro,
    Losa
}

/// <summary>La forma de la seccion transversal.</summary>
/// <remarks>
/// Es un espejo de la cadena <c>Forma</c> que trae el lector de ETABS -"RECT", "CIRC",
/// "I", "C", "T", "L", "TUBO", "CAJON"- pero como enumeracion, para que una forma mal
/// escrita se note al compilar y no en el archivo exportado.
/// </remarks>
public enum FormaIfc
{
    Rectangulo,
    Circulo,
    PerfilI,
    PerfilC,
    PerfilT,
    PerfilL,
    Tubo,
    Cajon
}

/// <summary>Una seccion transversal, con sus medidas en metros.</summary>
public sealed class SeccionIfc
{
    /// <summary>El nombre que tiene en ETABS. Es lo que se vera en Revit para mapearla.</summary>
    public string Nombre { get; set; } = string.Empty;

    public FormaIfc Forma { get; set; } = FormaIfc.Rectangulo;

    /// <summary>Ancho: la medida sobre el eje local <b>3</b>. En un circulo, el diametro.</summary>
    public double AnchoM { get; set; }

    /// <summary>Peralte: la medida sobre el eje local <b>2</b>.</summary>
    public double PeralteM { get; set; }

    /// <summary>Espesor del patin. Solo en I, C y T.</summary>
    public double PatinM { get; set; }

    /// <summary>Espesor del alma. Solo en I, C, T y L.</summary>
    public double AlmaM { get; set; }

    /// <summary>Espesor de pared. Solo en tubo y cajon.</summary>
    public double ParedM { get; set; }

    public string Material { get; set; } = string.Empty;

    /// <summary>
    /// Con que identificar dos secciones como la misma, para no repetir el perfil en el
    /// archivo.
    /// </summary>
    /// <remarks>
    /// Lleva las medidas y no solo el nombre a proposito: en un modelo heredado es normal
    /// encontrar dos propiedades con el mismo nombre y distinto peralte, y fundirlas
    /// dejaria la mitad de las barras con la seccion de la otra.
    /// </remarks>
    public string Clave =>
        string.Join(
            "|",
            Nombre.Trim(),
            Forma.ToString(),
            EscritorPaso.Real(AnchoM),
            EscritorPaso.Real(PeralteM),
            EscritorPaso.Real(PatinM),
            EscritorPaso.Real(AlmaM),
            EscritorPaso.Real(ParedM));
}

/// <summary>Una barra: columna, trabe o diagonal.</summary>
/// <remarks>
/// <para>
/// Los tres vectores <see cref="E1"/>, <see cref="E2"/> y <see cref="E3"/> son los ejes
/// locales de CSI en coordenadas globales, <b>ya girados</b> por el angulo de la seccion.
/// Los calcula la aplicacion con <c>PuntoDeInsercion.Ejes(...)</c> y se pasan hechos: esa
/// aritmetica ya vive en CadLink.Etabs y duplicarla aqui seria tener dos versiones de la
/// misma regla, que es como se desincronizan.
/// </para>
/// <para>
/// Se da por supuesto que forman una terna <b>derecha</b> con <c>E3 = E1 x E2</c>, que es
/// la convencion de CSI. <see cref="ExportadorIfc"/> lo comprueba.
/// </para>
/// </remarks>
public sealed class BarraIfc
{
    public string Etiqueta { get; set; } = string.Empty;

    public ClaseIfc Clase { get; set; } = ClaseIfc.Trabe;

    /// <summary>Nombre del nivel al que pertenece. Debe existir en <see cref="ModeloIfc.Niveles"/>.</summary>
    public string Nivel { get; set; } = string.Empty;

    public SeccionIfc Seccion { get; set; } = new();

    public double X1 { get; set; }

    public double Y1 { get; set; }

    public double Z1 { get; set; }

    public double X2 { get; set; }

    public double Y2 { get; set; }

    public double Z2 { get; set; }

    /// <summary>Eje local 1: a lo largo de la barra.</summary>
    public double[] E1 { get; set; } = { 0, 0, 1 };

    /// <summary>Eje local 2: sobre el que se mide el peralte.</summary>
    public double[] E2 { get; set; } = { 1, 0, 0 };

    /// <summary>Eje local 3: sobre el que se mide el ancho.</summary>
    public double[] E3 { get; set; } = { 0, 1, 0 };

    /// <summary>
    /// Cuanto se corre el CENTRO de la seccion sobre el eje local 2, por el punto cardinal.
    /// </summary>
    /// <remarks>
    /// Lo da <c>PuntoDeInsercion.PorPuntoCardinal(...)</c>. Sin esto, una trabe con punto
    /// cardinal 8 -arriba al centro, que es el caso normal- queda medio peralte mas alta de
    /// lo que esta en el modelo.
    /// </remarks>
    public double CorrimientoEje2M { get; set; }

    /// <summary>Lo mismo sobre el eje local 3.</summary>
    public double CorrimientoEje3M { get; set; }

    public double LargoM =>
        Math.Sqrt(((X2 - X1) * (X2 - X1))
                  + ((Y2 - Y1) * (Y2 - Y1))
                  + ((Z2 - Z1) * (Z2 - Z1)));
}

/// <summary>Un pano: muro o losa. Un poligono con espesor.</summary>
public sealed class PanoIfc
{
    public string Etiqueta { get; set; } = string.Empty;

    /// <summary>Solo <see cref="ClaseIfc.Muro"/> o <see cref="ClaseIfc.Losa"/>.</summary>
    public ClaseIfc Clase { get; set; } = ClaseIfc.Losa;

    public string Nivel { get; set; } = string.Empty;

    /// <summary>El nombre de la propiedad de area.</summary>
    public string Seccion { get; set; } = string.Empty;

    public double EspesorM { get; set; }

    public string Material { get; set; } = string.Empty;

    /// <summary>
    /// El contorno, en orden y sin repetir el primero al final. En metros y en globales.
    /// </summary>
    /// <remarks>
    /// Va en 3D, no proyectado en planta, porque un muro proyecta area cero y se perderia.
    /// </remarks>
    public List<(double X, double Y, double Z)> Vertices { get; } = new();
}

/// <summary>Un nivel del edificio.</summary>
public sealed class NivelIfc
{
    public string Nombre { get; set; } = string.Empty;

    public double ElevacionM { get; set; }
}

/// <summary>Todo lo que hace falta para escribir el IFC.</summary>
/// <remarks>
/// Lo rellena la aplicacion a partir de <c>ModeloEtabs</c>. A proposito no se parece a
/// <c>ModeloEtabs</c> mas de lo necesario: lo que se quiere es que este proyecto no
/// dependa de CadLink.Etabs, para poder compilarlo y probarlo en cualquier maquina.
/// </remarks>
public sealed class ModeloIfc
{
    /// <summary>De donde salio: "ETABS" o "SAP2000".</summary>
    public string Programa { get; set; } = string.Empty;

    /// <summary>El archivo del modelo, solo para dejarlo escrito en la cabecera.</summary>
    public string Archivo { get; set; } = string.Empty;

    /// <summary>El nombre de la obra. Sera el del proyecto en Revit.</summary>
    public string Obra { get; set; } = string.Empty;

    public List<NivelIfc> Niveles { get; } = new();

    public List<BarraIfc> Barras { get; } = new();

    public List<PanoIfc> Panos { get; } = new();
}

/// <summary>Que se exporto, para poder decirselo al usuario.</summary>
public sealed class ResumenIfc
{
    public int Columnas { get; set; }

    public int Trabes { get; set; }

    public int Diagonales { get; set; }

    public int Muros { get; set; }

    public int Losas { get; set; }

    public int Niveles { get; set; }

    public int Secciones { get; set; }

    public int Entidades { get; set; }

    /// <summary>Lo que no se pudo exportar, con el motivo. Nunca es fatal.</summary>
    public List<string> Avisos { get; } = new();

    public int Total => Columnas + Trabes + Diagonales + Muros + Losas;
}
