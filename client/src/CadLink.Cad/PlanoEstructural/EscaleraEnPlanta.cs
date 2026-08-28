namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// La <b>escalera</b> en la planta: se aparta del dibujo y se deja <b>solo su contorno</b>.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió tal cual: «nada de losa de escalera en planos… tampoco las que se modelan como
/// muro… solo dibuja el contorno de las escaleras, puro contorno nada más».
/// </para>
/// <para>
/// Y es lo correcto en un plano estructural. Una escalera <b>no es un tablero de losa</b>: no se
/// arma con parrilla, no lleva bastones por claro y no se cota como un paño. Pero tampoco puede
/// desaparecer, porque hay que ver <b>dónde está</b> y que el hueco de la escalera no se
/// confunda con un vacío. Así que se queda su perímetro y nada más: el armado de la escalera va
/// en su propio detalle, a otra escala, donde se puede dibujar de verdad.
/// </para>
///
/// <para><b>POR QUÉ SE APARTA EN LUGAR DE FILTRARSE AL DIBUJAR</b></para>
/// <para>
/// Los elementos se <b>sacan de la lista</b> antes de que el dibujante empiece, y eso es lo que
/// hace cierto el «nada más». Un filtro puesto donde se dibuja el contorno dejaría la escalera
/// fuera del papel pero <b>dentro</b> de todo lo demás: agrupada con el tablero de al lado
/// —cambiándole el armado y su rótulo—, contada como voladizo si su nota lo dice, metida en el
/// recuadro del título y, si es de muro, con su línea doble, su relleno y su bloque. Sacándola de
/// la lista, ninguna de esas etapas la ve siquiera.
/// </para>
/// <para>
/// <b>Y se aparta ANTES de las conversiones de shell</b> —el castillo de muro y la cadena de
/// muro—, no después. Un escalón modelado como shell angosto y corto es exactamente lo que esas
/// conversiones buscan, así que si llegaran primero convertirían los peldaños en cadenas y
/// entonces ya no serían muros: se colarían al plano como cadenas, con su capa y su rótulo.
/// </para>
///
/// <para><b>QUÉ SE APARTA Y QUÉ NO</b></para>
/// <para>
/// Solo <b>losas y muros</b>, que son las dos formas en que se modela la escalera. Una
/// <b>columna o una trabe</b> cuya nota diga ESCALERA es estructura de verdad —el apoyo del
/// descanso, la trabe de arranque— y tiene que salir en el plano con su rótulo y su sección.
/// Apartarla dejaría la escalera sin apoyo dibujado, que es un error de plano, no de dibujo.
/// </para>
/// </remarks>
public static class EscaleraEnPlanta
{
    /// <summary>Grosor con el que se dibuja un muro que no dice el suyo, en metros.</summary>
    private const double EspesorPorOmision = 0.15;

    /// <summary>
    /// ¿Es una <b>escalera</b> —o una rampa, o un descanso—? Lo dicen la etiqueta, las notas o
    /// la sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se reconoce por el <b>texto</b> y no por la geometría, por el mismo motivo que el
    /// voladizo: la escalera en el modelo es un shell como cualquier otro, y quien sabe que es
    /// una escalera es el ingeniero, que lo escribe en la propiedad. Deducirlo de la inclinación
    /// fallaría con la rampa a 6 % y con el descanso, que es horizontal.
    /// </para>
    /// <para>
    /// Se mira la <b>etiqueta primero</b>, igual que en <see cref="LosaEnPlanta.DiceLosacero"/>:
    /// la propiedad puede llamarse «SLAB1» y quien lo dice de verdad son las notas o la etiqueta.
    /// </para>
    /// </remarks>
    public static bool DiceEscalera(
        string? etiqueta, string? notas, string? seccion, string palabras) =>
        PalabraEscalera(etiqueta, notas, seccion, palabras).Length > 0;

    /// <summary>
    /// <b>Cuál</b> es la palabra que delató la escalera.
    /// </summary>
    /// <remarks>
    /// Devuelve la palabra y no solo un sí o un no porque el apartado se <b>avisa</b>: el aviso
    /// dice cuántas escaleras se apartaron y por qué palabra. Sin eso, una losa que se queda en
    /// puro contorno porque su sección se llama «ESCALONADA» es un misterio de media tarde.
    /// </remarks>
    public static string PalabraEscalera(
        string? etiqueta, string? notas, string? seccion, string palabras)
    {
        var texto = ((etiqueta ?? string.Empty) + " " + (notas ?? string.Empty) + " " +
                     (seccion ?? string.Empty)).ToUpperInvariant();

        if (texto.Trim().Length == 0)
        {
            return string.Empty;
        }

        foreach (var palabra in palabras.Split(','))
        {
            var p = palabra.Trim().ToUpperInvariant();

            if (p.Length > 0 && texto.Contains(p, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return string.Empty;
    }

    /// <summary>¿Este elemento es una escalera de las que se apartan?</summary>
    /// <remarks>
    /// Losas y muros, no columnas ni trabes: el razonamiento está en la cabecera de la clase.
    /// </remarks>
    public static bool EsEscalera(ElementoPlanta el, string palabras) =>
        el.Clase is ClasePlanta.Losa or ClasePlanta.Muro
        && DiceEscalera(el.Etiqueta, el.Notas, el.Seccion, palabras);

    /// <summary>
    /// <b>Saca de la planta</b> las escaleras y las devuelve, para dibujar solo su contorno.
    /// </summary>
    /// <remarks>
    /// Se recorre la lista <b>al revés</b> a propósito: quitando de delante hacia atrás los
    /// índices se corren y se salta un elemento, y con varios peldaños seguidos —que es
    /// justamente cómo llega una escalera de muro— se notaría.
    /// </remarks>
    public static List<ElementoPlanta> Apartar(List<ElementoPlanta> elementos, string palabras)
    {
        var apartadas = new List<ElementoPlanta>();

        for (var i = elementos.Count - 1; i >= 0; i--)
        {
            if (!EsEscalera(elementos[i], palabras))
            {
                continue;
            }

            apartadas.Add(elementos[i]);
            elementos.RemoveAt(i);
        }

        // Se devuelven en el orden en que estaban, que es el del modelo: así el dibujo sale
        // igual dos veces seguidas y los avisos se leen en el orden esperado.
        apartadas.Reverse();

        return apartadas;
    }

    /// <summary>
    /// El <b>contorno</b> de una escalera: su paño si es losa, su huella si es muro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Una losa ya trae su polígono. Un muro trae un <b>eje</b> y un grosor, así que su contorno
    /// en planta es el rectángulo que ocupa: el eje engordado medio grosor a cada lado. Es la
    /// misma huella con la que se recortan los muros, y por eso el peldaño sale con su ancho real
    /// y no como una raya.
    /// </para>
    /// <para>
    /// Devuelve la lista vacía si el elemento no da para cerrar nada —una losa con dos vértices,
    /// un muro de largo cero—, y entonces no se dibuja: es mejor que una polilínea degenerada.
    /// </para>
    /// </remarks>
    public static List<(double X, double Y)> Contorno(ElementoPlanta el)
    {
        if (el.Clase == ClasePlanta.Losa)
        {
            return el.Vertices.Count >= 3
                ? el.Vertices.ToList()
                : new List<(double X, double Y)>();
        }

        // Un muro con su polígono ya resuelto se respeta: es más exacto que engordar el eje.
        if (el.Vertices.Count >= 3)
        {
            return el.Vertices.ToList();
        }

        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;

        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-9)
        {
            return new List<(double X, double Y)>();
        }

        dx /= largo;
        dy /= largo;

        // El grosor: el del modelo si lo trae, y si no el de omisión. AnchoM de un muro es su
        // espesor, no su largo: el largo sale del eje.
        var medio = (el.AnchoM > 1e-4 ? el.AnchoM : EspesorPorOmision) / 2;

        var nx = -dy * medio;
        var ny = dx * medio;

        return new List<(double X, double Y)>
        {
            (el.X1 + nx, el.Y1 + ny),
            (el.X2 + nx, el.Y2 + ny),
            (el.X2 - nx, el.Y2 - ny),
            (el.X1 - nx, el.Y1 - ny),
        };
    }
}
