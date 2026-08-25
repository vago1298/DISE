using CadLink.Cad.PlanoEstructural;

namespace CadLink.Cad;

/// <summary>
/// El <b>corte por un eje</b>, dibujado al lado de la planta estructural.
/// </summary>
/// <remarks>
/// <para>
/// Se pidió: que se dibuje <b>el corte que se haya elegido</b> en la pestaña del modelo, a
/// <c>CORTE_SEPARACION_M</c> —10 m— de la planta. Y tiene todo el sentido tenerlos juntos: la
/// planta dice los espesores y las distancias entre ejes, y el corte dice las alturas. Un
/// juego de planos con la planta sola obliga a adivinar las alturas de entrepiso.
/// </para>
/// <para>
/// El corte se arma con la geometría de <see cref="CorteEnAlzado"/> —pura aritmética, aparte y
/// comprobable sin AutoCAD— y aquí solo se dibuja: cada pieza como una polilínea cerrada en la
/// capa que le toca, la línea de cada nivel con su cota, y el rótulo debajo.
/// </para>
/// </remarks>
public sealed partial class PlantaDrawer
{
    /// <summary>
    /// Dibuja el corte elegido, a la derecha de la planta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="dx"/> y <paramref name="dy"/> son el desplazamiento de la planta, y el
    /// corte se pone a partir de ahí: así los dos dibujos van del mismo juego y no hay que
    /// buscar el corte por el dibujo.
    /// </para>
    /// <para>
    /// La coordenada horizontal del corte es la que recorre el eje —en un corte por un eje de
    /// los que van en X se recorre la Y— y la vertical es la <b>cota del modelo</b>, tal cual.
    /// Eso deja el corte a la misma escala que la planta, que es lo que permite medir de uno a
    /// otro.
    /// </para>
    /// </remarks>
    /// <returns>Cuántas piezas se dibujaron.</returns>
    public int DibujarCorte(CorteCad c, double dx, double dy)
    {
        if (c.Elementos.Count == 0 || c.Eje.Length == 0)
        {
            return 0;
        }

        var piezas = CorteEnAlzado.Piezas(c.Elementos, c.EnX, c.Ordenada, c.EspesorM);

        if (piezas.Count == 0)
        {
            Nota($"El corte por el eje {c.Eje} no toca ningún elemento, así que no se " +
                 "dibujó. Prueba con otro eje o sube CORTE_ESPESOR_CM.");
            return 0;
        }

        // ==============================================================================
        //  DÓNDE SE PONE: AL LADO DE LA PLANTA, NO ENCIMA
        // ==============================================================================
        //  A la derecha del ancho de la planta más la separación de la hoja. Se mide la
        //  planta de verdad —lo que ocupan sus elementos— y no se usa un valor fijo, porque
        //  una casa de 8 m y una nave de 60 no pueden llevar la misma separación.
        var anchoPlanta = AnchoDeLoDibujado(c.Elementos, c.EnX);
        var separacion = _cfg.Numero("CORTE_SEPARACION_M", 10);

        var cx = dx + anchoPlanta + separacion;
        var cy = dy;

        var hechas = 0;

        foreach (var p in piezas)
        {
            var capa = CapaDeLaPieza(p.Clase);

            var pts = new[]
            {
                cx + p.X, cy + p.Z,
                cx + p.X + p.Ancho, cy + p.Z,
                cx + p.X + p.Ancho, cy + p.Z + p.Alto,
                cx + p.X, cy + p.Z + p.Alto
            };

            if (PolilineaCerrada(pts, capa) is not null)
            {
                hechas++;
            }
        }

        DibujarNivelesDelCorte(c, cx, cy, piezas);
        RotularElCorte(c, cx, cy, piezas);

        Nota($"Corte por el eje {c.Eje} dibujado con {hechas} pieza(s), a {separacion:0.##} m " +
             "a la derecha de la planta.");

        return hechas;
    }

    /// <summary>
    /// Las <b>líneas de nivel</b> del corte, con su nombre y su cota.
    /// </summary>
    /// <remarks>
    /// Es lo que convierte un montón de rectángulos en un corte que se puede leer: sin las
    /// cotas de nivel no se sabe a qué altura está cada cosa, que es justo lo que se viene a
    /// buscar en un corte. La línea se saca un poco por los dos lados, como en un plano.
    /// </remarks>
    private void DibujarNivelesDelCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        if (c.Niveles.Count == 0)
        {
            return;
        }

        var xMin = piezas.Min(p => p.X);
        var xMax = piezas.Max(p => p.X + p.Ancho);

        var vuela = _cfg.Numero("CORTE_NIVEL_VUELA_M", 0.6);
        var capa = _capas.Prefijo + "EJES";
        var capaTxt = CapaTextos;

        foreach (var (nombre, z) in c.Niveles)
        {
            Linea(cx + xMin - vuela, cy + z, cx + xMax + vuela, cy + z, capa);

            var texto = $"{Rot.NombreDeNivel(nombre)}  " +
                        z.ToString("+0.000;-0.000;±0.000",
                                   System.Globalization.CultureInfo.InvariantCulture);

            Mtexto(cx + xMax + vuela, cy + z, texto, AlturaSecciones(c.AlturaTexto),
                   capaTxt, 0, EstiloSecciones, false, 1);
        }
    }

    /// <summary>El rótulo del corte, debajo: <c>CORTE POR EL EJE 3</c>.</summary>
    private void RotularElCorte(
        CorteCad c, double cx, double cy, List<CorteEnAlzado.Pieza> piezas)
    {
        var xMin = piezas.Min(p => p.X);
        var xMax = piezas.Max(p => p.X + p.Ancho);
        var zMin = piezas.Min(p => p.Z);

        var plantilla = _cfg.Texto("CORTE_ROTULO", "CORTE  POR  EL  EJE  %E");
        var texto = plantilla.Replace("%E", c.Eje);

        var altura = _cfg.Numero("ROTULO_ALTURA_NIVEL", 0.26);
        var abajo = _cfg.Numero("CORTE_ROTULO_ABAJO_M", 1.2);

        Mtexto((cx + ((xMin + xMax) / 2)), cy + zMin - abajo, texto, altura,
               _capas.CapaDeTipo("TITULO"), 0, Rot.Estilo, false);
    }

    /// <summary>El ancho de lo dibujado en planta, para saber dónde empieza el corte.</summary>
    private static double AnchoDeLoDibujado(
        IReadOnlyList<ElementoPlanta> elementos, bool enX)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var el in elementos)
        {
            if (el.Vertices.Count > 0)
            {
                foreach (var (x, y) in el.Vertices)
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }
            else
            {
                min = Math.Min(min, Math.Min(el.X1, el.X2));
                max = Math.Max(max, Math.Max(el.X1, el.X2));
            }
        }

        return max > min ? max - min : 10;
    }

    /// <summary>
    /// La capa que le toca a cada pieza del corte: <b>las mismas</b> de la planta.
    /// </summary>
    /// <remarks>
    /// Compartir capas con la planta no es pereza: es lo que hace que apagar E-MURO apague el
    /// muro en los dos dibujos, y que los colores y los grosores de impresión salgan iguales
    /// en el corte y en la planta sin configurar nada dos veces.
    /// </remarks>
    private string CapaDeLaPieza(ClasePlanta clase) => clase switch
    {
        ClasePlanta.Columna => _capas.CapaDeTipo("COLUMNA"),
        ClasePlanta.Muro => _capas.CapaDeTipo("MURO"),
        _ => _capas.CapaDeTipo("TRABE")
    };
}
