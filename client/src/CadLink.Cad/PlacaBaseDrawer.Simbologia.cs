namespace CadLink.Cad;

/// <summary>
/// La <b>simbología de soldadura</b> llevada a AutoCAD, como un bloque.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué vive en el dibujante de la placa base.</b> La simbología no es de la placa: es el
/// cuadro de notas que acompaña a cualquier detalle de estructura metálica. Pero necesita
/// exactamente lo que este dibujante ya tiene resuelto —sus capas, su estilo de texto, su flecha
/// rellena, su <c>Bloquear</c> y su registro de fallos—, y montar otra clase COM para copiarlo todo
/// sería duplicar doscientas líneas de andamio para dibujar cinco símbolos. Cuando haya más
/// detalles de conexión, este archivo es el sitio natural para partirlos en su propio dibujante.
/// </para>
/// <para>
/// El reparto —las proporciones del símbolo y dónde cae cada pieza— lo hace
/// <see cref="SimbolosSoldadura"/>, que no toca COM. Aquí solo se traducen puntos a entidades.
/// </para>
/// </remarks>
public sealed partial class PlacaBaseDrawer
{
    /// <summary>El nombre del bloque de la simbología.</summary>
    /// <remarks>
    /// Fijo y no derivado de nada: la simbología es UNA por plano. Si ya existe un bloque con ese
    /// nombre, <c>NombreLibre</c> le pone consecutivo en lugar de pisarlo —igual que con las
    /// placas—, así que dibujarla dos veces deja dos bloques y no borra el del usuario.
    /// </remarks>
    public const string NombreBloqueSimbologia = "SIMBOLOGIA DE SOLDADURA";

    /// <summary>
    /// Dibuja la simbología de soldadura y la deja agrupada en su bloque.
    /// </summary>
    /// <param name="x">Esquina izquierda: por ahí pasan las puntas de las flechas.</param>
    /// <param name="y">El renglón del título; los símbolos cuelgan hacia abajo.</param>
    /// <param name="titulo">El encabezado del cuadro.</param>
    /// <returns>Cuántas entidades se crearon. <c>0</c> = no se dibujó nada.</returns>
    /// <remarks>
    /// <para>
    /// <b>Todo va en la capa ROTULOS</b>, símbolos incluidos. Es una anotación —no una pieza— y
    /// ROTULOS es la capa que este programa imprime en negro, que es como tiene que salir un cuadro
    /// de notas. Puesta en SOLDADURA saldría del color de esa capa y el cuadro no imprimiría igual
    /// que el resto del rotulado.
    /// </para>
    /// <para>
    /// Y se agrupa en un bloque por lo mismo que el detalle de la placa: es un cuadro que se coloca
    /// una vez y se mueve entero al sitio del plano donde toque.
    /// </para>
    /// </remarks>
    public int DibujarSimbologiaSoldadura(
        double x, double y, string titulo = "SIMBOLOGIA DE SOLDADURA")
    {
        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);

        AsegurarCapas();
        AsegurarEstiloTexto();

        // La altura del texto es la del estilo de esta macro, la misma de los rótulos del detalle:
        // así la simbología y los rótulos de las placas se leen como una sola familia.
        var h = _hTxt;

        var legenda = SimbolosSoldadura.Construir(x, y, h, titulo);

        Texto(legenda.Titulo);

        // UN SOLO RECORRIDO PARA LOS DOS SITIOS. Este bucle estaba escrito aquí y el detalle de la
        // placa dibuja ahora su propio símbolo de soldadura —el de «todo alrededor»—, así que la
        // parte de recorrer el renglón se movió a DibujarSimbolo. Con dos copias, el día que el
        // triángulo del filete cambie de forma, el cuadro que explica la simbología y el símbolo del
        // detalle dirían cosas distintas en el mismo plano.
        foreach (var r in legenda.Renglones)
        {
            DibujarSimbolo(r);
        }

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        UltimoBloque = Bloquear(NombreBloqueSimbologia, inicio, fin, x, y);

        return fin - inicio;
    }

    /// <summary>Un texto de la simbología, con su altura y su anclaje propios.</summary>
    /// <remarks>
    /// TEXT y no MTEXT, y con la altura EXPLÍCITA en lugar de la del estilo: en un mismo renglón
    /// conviven tres alturas —el título, el nombre y el proceso de la cola— y el estilo solo sabe
    /// de una. Ver <see cref="SimbolosSoldadura.TextoDwg"/>.
    /// </remarks>
    private object? Texto(SimbolosSoldadura.TextoDwg t)
    {
        if (t.S.Trim().Length == 0 || t.Altura <= 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic txt = _ms.AddText(t.S, Punto(t.X, t.Y), t.Altura);

                txt.StyleName = PlacaBaseCapas.EstiloTexto;
                txt.Height = t.Altura;
                txt.Layer = PlacaBaseCapas.Rotulos;
                txt.Color = PorCapa;

                // El anclaje se pide con Alignment, y con Alignment != 0 el punto que MANDA es
                // TextAlignmentPoint: si solo se pone InsertionPoint, el texto se queda donde
                // estaba y el anclaje no hace nada. Es el mismo cuidado que en Mtexto.
                txt.Alignment = t.Anclaje;
                txt.TextAlignmentPoint = Punto(t.X, t.Y);
                txt.Update();

                return (object)txt;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Texto «{t.S}» de la simbología", ex);
            return null;
        }
    }

    /// <summary>Un triángulo <b>relleno</b>: la bandera de la soldadura de campo.</summary>
    /// <remarks>
    /// Un SOLID y no un hatch: son tres vértices y un relleno liso, y un hatch para eso trae detrás
    /// su contorno, su patrón y su evaluación —tres cosas que pueden fallar— para el mismo
    /// resultado. En un SOLID triangular el tercer vértice y el cuarto coinciden, igual que en la
    /// punta de la flecha del leader.
    /// </remarks>
    private object? Solido(double[] triangulo)
    {
        if (triangulo.Length < 6)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic sol = _ms.AddSolid(
                    Punto(triangulo[0], triangulo[1]),
                    Punto(triangulo[2], triangulo[3]),
                    Punto(triangulo[4], triangulo[5]),
                    Punto(triangulo[4], triangulo[5]));

                sol.Layer = PlacaBaseCapas.Rotulos;
                sol.Color = PorCapa;

                return (object)sol;
            });
        }
        catch (Exception ex)
        {
            Fallo("Relleno de la bandera de la simbología", ex);
            return null;
        }
    }
}
