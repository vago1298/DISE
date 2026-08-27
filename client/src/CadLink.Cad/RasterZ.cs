namespace CadLink.Cad;

/// <summary>
/// Un <b>rasterizador con Z-buffer</b>: pinta triángulos y decide por PÍXEL qué queda delante.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta.</b> La vista extruida pintaba las caras ordenadas de lejos a cerca
/// —el «algoritmo del pintor»— y eso falla siempre en el mismo caso: cuando dos caras se
/// <b>atraviesan</b> o cuando una es mucho más grande que la otra. Al ordenar por la
/// profundidad MEDIA de cada cara, una losa entera queda delante o detrás de un muro entero,
/// y no hay ordenación posible que sea correcta: la losa se ve <b>cortada</b> por el muro, o el
/// muro le pasa por encima. No es un problema del motor de dibujo ni de la máquina; es que el
/// método no puede resolver ese caso.
/// </para>
/// <para>
/// La solución de verdad —la que usa cualquier programa de 3D, ETABS incluido— es guardar la
/// <b>profundidad de cada píxel</b> y pintar solo cuando lo nuevo está más cerca que lo que ya
/// había. Con eso la intersección de dos caras sale exacta, sin ordenar nada y sin partir
/// geometría.
/// </para>
/// <para>
/// <b>Por qué a mano y no con Viewport3D.</b> Porque así esto es aritmética pura, sin WPF, y se
/// puede <b>comprobar sin abrir la ventana</b>: las pruebas pintan triángulos en un buffer de
/// 40×40 y miran píxel por píxel quién quedó delante. Con el 3D de WPF habría que creerse el
/// resultado. Y el coste no es problema: son unas decenas de miles de píxeles por cara, que en
/// C# se pintan en microsegundos.
/// </para>
/// </remarks>
public sealed class RasterZ
{
    /// <summary>Profundidad de «vacío»: más lejos que cualquier cosa que se pinte.</summary>
    public const double Lejos = double.MaxValue;

    private readonly double[] _z;

    public RasterZ(int ancho, int alto)
    {
        Ancho = Math.Max(1, ancho);
        Alto = Math.Max(1, alto);

        Pixeles = new int[Ancho * Alto];
        _z = new double[Ancho * Alto];

        Limpiar(0);
    }

    public int Ancho { get; }

    public int Alto { get; }

    /// <summary>Los píxeles, en formato <c>0xAARRGGBB</c>, listos para volcar a un mapa de bits.</summary>
    public int[] Pixeles { get; }

    /// <summary>Deja todo del color de fondo y con la profundidad al infinito.</summary>
    public void Limpiar(int colorFondo)
    {
        for (var i = 0; i < Pixeles.Length; i++)
        {
            Pixeles[i] = colorFondo;
            _z[i] = Lejos;
        }
    }

    /// <summary>El color que quedó en un píxel. Fuera del buffer devuelve 0.</summary>
    public int PixelEn(int x, int y) =>
        Dentro(x, y) ? Pixeles[(y * Ancho) + x] : 0;

    /// <summary>La profundidad que quedó en un píxel: menor es <b>más cerca</b>.</summary>
    public double ProfundidadEn(int x, int y) =>
        Dentro(x, y) ? _z[(y * Ancho) + x] : Lejos;

    /// <summary>
    /// Pinta un <b>triángulo</b> con su profundidad interpolada en cada píxel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se recorre solo la caja que envuelve al triángulo y se usan las <b>funciones de arista</b>
    /// —el producto cruzado de cada lado con el punto— para saber si el píxel está dentro. Es el
    /// método estándar y no tiene casos especiales: da igual la forma del triángulo y da igual
    /// en qué sentido vengan sus vértices, lo que importa aquí porque las caras de un prisma
    /// llegan en los dos sentidos y todas se tienen que ver.
    /// </para>
    /// <para>
    /// La profundidad se interpola con las mismas coordenadas baricéntricas que salen de las
    /// funciones de arista: es lineal en pantalla, que es exacto para una proyección
    /// <b>axonométrica</b> como la de esta vista —sin perspectiva, sin división por Z— y por eso
    /// aquí no hace falta la corrección perspectiva.
    /// </para>
    /// </remarks>
    public void Triangulo(
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        double x3, double y3, double z3,
        int color)
    {
        var area = ((x2 - x1) * (y3 - y1)) - ((y2 - y1) * (x3 - x1));

        // Un triángulo degenerado —los tres vértices en una línea— no tiene interior que
        // pintar, y su área en cero haría una división por cero al interpolar.
        if (Math.Abs(area) < 1e-12)
        {
            return;
        }

        var xa = Math.Max(0, (int)Math.Floor(Math.Min(x1, Math.Min(x2, x3))));
        var xb = Math.Min(Ancho - 1, (int)Math.Ceiling(Math.Max(x1, Math.Max(x2, x3))));
        var ya = Math.Max(0, (int)Math.Floor(Math.Min(y1, Math.Min(y2, y3))));
        var yb = Math.Min(Alto - 1, (int)Math.Ceiling(Math.Max(y1, Math.Max(y2, y3))));

        for (var y = ya; y <= yb; y++)
        {
            for (var x = xa; x <= xb; x++)
            {
                // El centro del píxel, que es lo que se prueba: con la esquina, los bordes
                // salen corridos medio píxel y dos caras pegadas dejan una costura.
                var px = x + 0.5;
                var py = y + 0.5;

                var w1 = (((x2 - px) * (y3 - py)) - ((y2 - py) * (x3 - px))) / area;
                var w2 = (((x3 - px) * (y1 - py)) - ((y3 - py) * (x1 - px))) / area;
                var w3 = 1 - w1 - w2;

                // Dentro del triángulo, en cualquiera de los dos sentidos de giro.
                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9)
                {
                    continue;
                }

                Poner(x, y, (w1 * z1) + (w2 * z2) + (w3 * z3), color);
            }
        }
    }

    /// <summary>
    /// Pinta una <b>línea</b> con profundidad: sirve para las aristas de las caras.
    /// </summary>
    /// <remarks>
    /// Con un <paramref name="sesgo"/> que la acerca un poco a la cámara. Las aristas están
    /// EXACTAMENTE sobre la cara que bordean, así que sin ese empujón la mitad de sus píxeles
    /// perderían el desempate contra la propia cara y el contorno saldría a puntos.
    /// </remarks>
    public void Linea(
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        int color, double sesgo = 0.05)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;

        // Un paso por píxel: se avanza por el eje que más recorre, que es lo que deja la
        // línea sin huecos.
        var pasos = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));

        if (pasos <= 0)
        {
            Poner((int)Math.Round(x1), (int)Math.Round(y1), z1 - sesgo, color);
            return;
        }

        for (var i = 0; i <= pasos; i++)
        {
            var t = (double)i / pasos;

            Poner(
                (int)Math.Round(x1 + (dx * t)),
                (int)Math.Round(y1 + (dy * t)),
                z1 + ((z2 - z1) * t) - sesgo,
                color);
        }
    }

    /// <summary>
    /// La prueba de profundidad: se pinta <b>solo si está más cerca</b> que lo que ya había.
    /// </summary>
    /// <remarks>
    /// Aquí está toda la gracia del asunto, y es lo que el orden por caras no puede hacer: la
    /// decisión se toma <b>píxel a píxel</b>, así que dos caras que se cruzan se ven cruzadas
    /// —cada una delante en su mitad— sin partir ninguna de las dos.
    /// </remarks>
    private void Poner(int x, int y, double z, int color)
    {
        if (!Dentro(x, y))
        {
            return;
        }

        var i = (y * Ancho) + x;

        if (z >= _z[i])
        {
            return;
        }

        _z[i] = z;
        Pixeles[i] = color;
    }

    private bool Dentro(int x, int y) => x >= 0 && y >= 0 && x < Ancho && y < Alto;
}
