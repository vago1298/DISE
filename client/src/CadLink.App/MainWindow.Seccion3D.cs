using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CadLink.App.Models;
using CadLink.Cad;

namespace CadLink.App;

/// <summary>
/// La sección de concreto <b>en 3D</b>: la jaula de armado como sólidos, girable.
/// </summary>
/// <remarks>
/// <para>
/// Es el mismo armado del corte, con las mismas varillas, el mismo estribo, las mismas grapas
/// y el mismo diamante —salen de <see cref="TodasLasVarillas"/>, <see cref="TrazoEstribo"/>,
/// <see cref="TrazoGrapa"/> y <see cref="TrazoDiamante"/>, las mismas funciones que el corte y
/// que el dibujante de AutoCAD—, solo levantado a su longitud.
/// </para>
/// <para>
/// <b>Esto se dibujaba a mano y ahora lo dibuja el motor.</b> Antes se proyectaba cada barra
/// sobre un lienzo plano y se pintaba como una línea gruesa con degradado: una barra que
/// <i>parecía</i> redonda. El techo de aquello está razonado en <see cref="TuboDeMalla"/>, y el
/// resumen es que sin profundidad por píxel dos barras <b>tangentes</b> —un estribo abrazando
/// una varilla lo es— se traspasan siempre, por mucho que se afine el orden de pintado.
/// </para>
/// <para>
/// Con <c>Viewport3D</c> las barras son <b>sólidos de verdad</b>: la oclusión la resuelve el
/// buffer de profundidad, píxel a píxel, y el sombreado sale de una luz en lugar de pintarse.
/// Y hay una ganancia que no se ve pero se nota: <b>girar ya no redibuja nada</b>. La malla se
/// construye una vez y arrastrar solo cambia el ángulo de una transformación.
/// </para>
/// <para>
/// <b>Los ejes.</b> WPF 3D trabaja con la Y hacia arriba, así que: la <b>X</b> es la base de la
/// sección, la <b>Z</b> su peralte, y la <b>Y</b> la longitud de la pieza, que es la que sube.
/// El suelo es el plano Y = 0. Todo lo que llega en coordenadas de la sección pasa por
/// <c>Mundo</c>, en un solo sitio, para que no haya dos convenios.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Si la sección se ve en 3D en lugar del corte plano.</summary>
    private bool _alzado3D;

    /// <summary>Largo que se supone cuando la fila no lo trae, en metros.</summary>
    /// <remarks>
    /// Tres metros es un tramo de trabe corriente. Hace falta un valor porque sin largo no hay
    /// pieza que levantar, y dejarlo en cero enseñaba un recuadro vacío sin explicar por qué.
    /// </remarks>
    public const double LargoPorOmisionM = 3.0;

    // ======================================================================
    //  El giro, el zoom y el encuadre
    // ======================================================================

    /// <summary>Cuánto se ha girado <b>la pieza</b> sobre su eje vertical, en grados.</summary>
    /// <remarks>
    /// <para>
    /// <b>Gira la PIEZA, no la cámara.</b> Es la diferencia entre un plato giratorio y una
    /// cámara dando vueltas, y aquí importa porque el <b>sol y el suelo están quietos</b>: si
    /// girara la cámara, el suelo giraría con ella y la sombra se pasearía por la pantalla.
    /// Girando la pieza, la sombra se queda donde está y solo cambia de forma, como la de un
    /// objeto al que se le da vueltas al sol.
    /// </para>
    /// <para>
    /// Para la sección la imagen es la misma —girar el objeto un ángulo o el ojo el contrario
    /// se ven igual—; la diferencia está solo en lo que está clavado al mundo.
    /// </para>
    /// </remarks>
    private double _giro3DAzimut = GiroAzimutPorOmision;

    /// <summary>Inclinación de la <b>cámara</b>, en grados.</summary>
    /// <remarks>
    /// Esta sí es de la cámara: inclinar la pieza la volcaría, y lo que se quiere es mirarla
    /// desde más arriba o más abajo. Al inclinar, el suelo se inclina también y la sombra
    /// acompaña, que es lo que pasa de verdad al agacharse.
    /// </remarks>
    private double _giro3DElevacion = GiroElevacionPorOmision;

    /// <summary>Cuánto se ha acercado la cámara. 1 es el ajuste a la pieza.</summary>
    private double _zoom3D = 1;

    /// <summary>Desplazamiento de la cámara, en unidades del modelo.</summary>
    private double _pan3DU;

    private double _pan3DV;

    private const double GiroAzimutPorOmision = 0;

    /// <remarks>
    /// 22° es el valor de arranque del visor de ETABS, y por el mismo motivo: es la inclinación
    /// en la que se ven las tres caras de un prisma sin que ninguna quede de canto.
    /// </remarks>
    private const double GiroElevacionPorOmision = 22;

    /// <summary>Desde qué lado mira la cámara. <b>Fijo</b>, porque lo que gira es la pieza.</summary>
    /// <remarks>
    /// 32° pone el suelo en escorzo —ni de frente ni de canto— así que la sombra se lee como
    /// apoyada en un piso y no como una mancha pegada a la pieza.
    /// </remarks>
    private const double AzimutDeLaCamara = 32;

    private const double Zoom3DMin = 0.25;

    private const double Zoom3DMax = 24.0;

    /// <summary>Devuelve el 3D a su encuadre de arranque.</summary>
    private void ReiniciarGiro3D()
    {
        _giro3DAzimut = GiroAzimutPorOmision;
        _giro3DElevacion = GiroElevacionPorOmision;
        _zoom3D = 1;
        _pan3DU = 0;
        _pan3DV = 0;
    }

    private void OnAlternarAlzado3D(object sender, RoutedEventArgs e)
    {
        _alzado3D = !_alzado3D;
        AlzadoVistaButton.Content = _alzado3D ? "3D" : "2D";

        AlzadoVistaButton.ToolTip = _alzado3D
            ? "Viendo la sección en 3D. Arrastra con el izquierdo para girarla,\n"
              + "con el derecho para moverla, rueda para acercar."
            : "Viendo el corte plano. Toca para ver la sección en 3D.";

        // El encuadre del corte plano y el del 3D son cosas distintas: al cambiar de vista se
        // vuelve al ajuste, o se aparece en una esquina de la nueva sin saber por qué.
        ReiniciarEncuadrePrevia();

        DibujarVistaPrevia();
    }

    // ======================================================================
    //  El sol
    // ======================================================================

    /// <summary>Dirección del sol, del cielo hacia el suelo, en el mundo.</summary>
    /// <remarks>
    /// Alto y algo de lado, como a media mañana. De aquí sale el largo de la sombra: un punto a
    /// cota <c>y</c> cae en el suelo corrido <c>y · Sol / |SolY|</c>. Con estos números la
    /// sombra de una pieza de tres metros se extiende unos dos, que es lo que se ve en obra.
    /// </remarks>
    private const double SolX = 0.30;

    private const double SolY = -0.86;

    private const double SolZ = 0.42;

    // ======================================================================
    //  Lo que se guarda de una construcción a la siguiente
    // ======================================================================

    /// <summary>El giro de la jaula. Arrastrar solo le cambia el ángulo.</summary>
    /// <remarks>
    /// Se guarda para no reconstruir la malla al girar. Es la ganancia de fondo del cambio a
    /// un motor 3D: la jaula de una columna de tres metros son decenas de miles de triángulos,
    /// y antes se rehacían enteros en cada movimiento del ratón.
    /// </remarks>
    private AxisAngleRotation3D? _giroDeLaJaula;

    /// <summary>La sombra, que sí se rehace al girar porque cambia de forma.</summary>
    private GeometryModel3D? _modeloDeSombra;

    /// <summary>Las medidas de la pieza en curso, para reajustar la cámara sin rehacer nada.</summary>
    private (double Bx, double By, double Bz)? _cajaDeLaPieza;

    // ======================================================================
    //  Construir la escena
    // ======================================================================

    /// <summary>Del plano de la sección al mundo. <b>El único convenio de ejes.</b></summary>
    private static Point3D Mundo(double seccionX, double seccionY, double alturaEnLaPieza) =>
        new(seccionX, alturaEnLaPieza, seccionY);

    /// <summary>
    /// Arma la sección en 3D: la jaula, la caja de concreto, la sombra y la luz.
    /// </summary>
    /// <remarks>
    /// Se llama al cambiar de sección o de datos, <b>no al girar</b>. Girar pasa por
    /// <see cref="ActualizarGiro3D"/>, que solo mueve la cámara y el ángulo.
    /// </remarks>
    private void ConstruirEscena3D(SeccionConcretoRow s, double ancho, double alto)
    {
        PreviaViewport.Width = Math.Max(10, ancho * 0.5);

        if (s.BaseCm <= 0 || s.AlturaCm <= 0)
        {
            PreviaEscena3D.Content = null;
            _cajaDeLaPieza = null;
            return;
        }

        var largoM = s.LongitudM > 0 ? s.LongitudM : LargoPorOmisionM;

        var bx = s.BaseCm;          // X: la base de la sección
        var bz = s.AlturaCm;        // Z: el peralte
        var by = largoM * 100.0;    // Y: la longitud, que sube

        _cajaDeLaPieza = (bx, by, bz);

        var rec = s.RecubrimientoCm;

        Varilla.TryDiametroCm(s.Estribo, out var de);

        var varillas = TodasLasVarillas(s, de, rec);

        // ---------- Una malla por material ----------
        //
        // Todo lo que comparte color va en la MISMA malla. Un motor 3D dibuja mucho más
        // rápido una malla de sesenta mil triángulos que seis mil mallas de diez, y aquí
        // hay del orden de mil barras.
        var mallaVarillas = new TuboDeMalla.Malla();
        var mallaEstribos = new TuboDeMalla.Malla();
        var mallaConcreto = new TuboDeMalla.Malla();

        void Tubo(
            TuboDeMalla.Malla malla,
            IReadOnlyList<(double X, double Y)> recorrido,
            double zIni, double zFin, bool cerrado, double diamCm)
        {
            if (diamCm <= 0 || recorrido.Count < 2)
            {
                return;
            }

            // La cota sube a lo largo del recorrido, repartida por LARGO y no por número de
            // puntos: los dobleces llevan muchos puntos y los lados rectos pocos, así que por
            // índice casi toda la subida caería dentro de los dobleces.
            var acumulado = new double[recorrido.Count];

            for (var i = 1; i < recorrido.Count; i++)
            {
                var a = recorrido[i - 1];
                var b = recorrido[i];

                acumulado[i] = acumulado[i - 1]
                    + Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            }

            var total = acumulado[^1];

            var eje = new List<(double X, double Y, double Z)>();

            for (var i = 0; i < recorrido.Count; i++)
            {
                var f = total > 1e-9 ? acumulado[i] / total : 0;

                var p = Mundo(recorrido[i].X, recorrido[i].Y, zIni + ((zFin - zIni) * f));

                eje.Add((p.X, p.Y, p.Z));
            }

            TuboDeMalla.Agregar(malla, eje, diamCm / 2, cerrado);
        }

        // ---------- Las varillas longitudinales ----------
        foreach (var (_, vx, vy, vr) in varillas)
        {
            var a = Mundo(vx, vy, 0);
            var b = Mundo(vx, vy, by);

            TuboDeMalla.Agregar(
                mallaVarillas,
                new[] { (a.X, a.Y, a.Z), (b.X, b.Y, b.Z) },
                vr);
        }

        // ---------- El estribo, el diamante y las grapas en cada posición ----------
        //
        // Se calculan UNA vez: son los mismos en todas las posiciones, y armarlos pasa por
        // los arcos de los dobleces y las dos colas.
        //
        // Los dobleces se muestrean con mano ancha —no depende del zoom, porque ahora el
        // motor escala la malla sin volver a construirla— así que se pide detalle de sobra y
        // se olvida el asunto.
        var trazo = TrazoDelEstribo3D(s, de, rec, 14);

        var dDia = DiametroDelDiamante(s, de);
        var hayDiamante = s.LlevaDiamante && dDia > 0;

        var recorridoDia = hayDiamante ? RecorridoDelDiamante3D(s, de, rec, dDia, 14) : null;

        var sep = Separaciones(s.SeparacionCm);

        var centros = Estribos.CentrosDeAlzado(
            largoM,
            sep[0] / 100, sep[1] / 100, sep[2] / 100,
            vertical: true,
            esColumna: true);

        foreach (var pos in centros)
        {
            var zEst = pos * 100.0;

            if (trazo is not null)
            {
                // ===== UN ESTRIBO CERRADO ES UNA HÉLICE MUY PLANA =====
                //
                // Sus dos extremos se juntan en la misma esquina y los dos envuelven la
                // varilla, así que en un plano exacto se ocuparían el mismo sitio. En la pieza
                // uno lapa sobre el otro, y ese lape es de un diámetro. Se reparte a lo largo
                // del recorrido para que empalme sin saltos: un diámetro en un perímetro de
                // casi dos metros no se lee como inclinación.
                var lape = trazo.Value.Cerrado ? 0 : de;

                Tubo(mallaEstribos, trazo.Value.Cuerpo, zEst, zEst + lape,
                     trazo.Value.Cerrado, de);

                // Cada cola a la cota del extremo al que se engancha. Colas[0] arranca donde
                // ACABA el cuerpo y Colas[1] donde EMPIEZA; ese reparto lo fija TrazoEstribo.
                if (trazo.Value.Colas.Count > 0)
                {
                    Tubo(mallaEstribos, trazo.Value.Colas[0],
                         zEst + lape, zEst + lape, false, de);
                }

                if (trazo.Value.Colas.Count > 1)
                {
                    Tubo(mallaEstribos, trazo.Value.Colas[1], zEst, zEst, false, de);
                }
            }

            // El diamante, apilado sobre el estribo y tangente a él: dos barras del mismo
            // calibre en el mismo plano se atravesarían, y en la pieza eso no pasa.
            var zDia = zEst + ((de + dDia) / 2);

            if (recorridoDia is not null)
            {
                Tubo(mallaEstribos, recorridoDia, zDia, zDia, true, dDia);
            }

            // Y las grapas encima, cada una con SU diámetro y apilada sobre la anterior.
            var zGrapa = hayDiamante ? zDia + (dDia / 2) : zEst + (de / 2);

            foreach (var g in s.Grapas)
            {
                if (!Varilla.TryDiametroCm(g.Diametro, out var dGrapa) || dGrapa <= 0)
                {
                    // Sin diámetro reconocido se usa el del estribo, la misma regla que sigue
                    // el dibujo del corte.
                    dGrapa = de;
                }

                var va = BuscarVarillaPrevia(varillas, g.A);
                var vb = BuscarVarillaPrevia(varillas, g.B);

                if (va is null || vb is null)
                {
                    continue;
                }

                zGrapa += dGrapa / 2;

                // El eje de la grapa, con sus dos dobleces y sus dos colas. Sale de
                // TrazoGrapa.Eje, que resuelve la tangencia con la MISMA función que el
                // contorno del plano.
                var eje = TrazoGrapa.Eje(
                    va.Value.X, va.Value.Y, va.Value.R,
                    vb.Value.X, vb.Value.Y, vb.Value.R,
                    dGrapa,
                    s.GanchoCm > 0 ? s.GanchoCm : dGrapa * 6);

                if (eje is not null)
                {
                    Tubo(mallaEstribos, eje, zGrapa, zGrapa, false, dGrapa);
                }
                else
                {
                    // Sin tangente común —dos varillas demasiado juntas— no hay grapa que
                    // envuelva nada, pero el usuario la puso: se dibuja recta para que se vea
                    // que está, igual que hace el corte.
                    Tubo(mallaEstribos,
                         new[] { (va.Value.X, va.Value.Y), (vb.Value.X, vb.Value.Y) },
                         zGrapa, zGrapa, false, dGrapa);
                }

                zGrapa += dGrapa / 2;
            }
        }

        // ---------- La caja de concreto, en alambre ----------
        //
        // Va en alambre y no en caras opacas porque lo que hay que mirar es el armado. Y se
        // hace con tubos finos en lugar de con caras transparentes a propósito: la
        // transparencia en un motor 3D obliga a ordenar lo que hay detrás, y aquí detrás hay
        // mil barras. Un alambre sólido no tiene ese problema.
        var canto = Math.Max(Math.Min(bx, bz) * 0.012, 0.25);

        foreach (var (a, b) in AristasDeLaCaja(bx, by, bz))
        {
            TuboDeMalla.Agregar(
                mallaConcreto,
                new[] { (a.X, a.Y, a.Z), (b.X, b.Y, b.Z) },
                canto, lados: 4);
        }

        // ---------- La escena ----------
        var grupo = new Model3DGroup();

        // La luz: una direccional que hace el sombreado y un poco de ambiente para que la
        // cara de sombra no quede negra. Sin la ambiente, media jaula se pierde.
        grupo.Children.Add(new DirectionalLight(
            Color.FromRgb(0xFF, 0xFB, 0xF2), new Vector3D(SolX, SolY, SolZ)));

        grupo.Children.Add(new AmbientLight(Color.FromRgb(0x62, 0x66, 0x6E)));

        // La jaula, con su giro. La sombra NO va aquí dentro: no es un objeto que gire con la
        // pieza, es la marca que la pieza deja en el suelo, y cambia de forma al girar.
        var jaula = new Model3DGroup();

        Agregar3D(jaula, mallaVarillas, Color.FromRgb(0xC0, 0x39, 0x2B), 0.35);
        Agregar3D(jaula, mallaEstribos, Color.FromRgb(0x1F, 0x6F, 0xB2), 0.35);
        Agregar3D(jaula, mallaConcreto, Color.FromRgb(0x8F, 0xA6, 0xBC), 0.05);

        _giroDeLaJaula = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _giro3DAzimut);

        jaula.Transform = new RotateTransform3D(
            _giroDeLaJaula, new Point3D(bx / 2, 0, bz / 2));

        grupo.Children.Add(jaula);

        _modeloDeSombra = SombraEnElSuelo(bx, by, bz);

        if (_modeloDeSombra is not null)
        {
            grupo.Children.Add(_modeloDeSombra);
        }

        PreviaEscena3D.Content = grupo;

        AjustarCamara3D();

        Etiqueta(PreviaFijaCanvas,
            $"SECCIÓN 3D   ·   L = {largoM:N2} m   ·   {centros.Count} estribos"
            + $"   ·   {mallaVarillas.CuantosTriangulos + mallaEstribos.CuantosTriangulos:N0}"
            + " triángulos"
            + (s.LongitudM > 0 ? string.Empty : "   ·   largo por omisión"),
            26, alto - 18);
    }

    /// <summary>Las doce aristas de la caja de concreto.</summary>
    private static IEnumerable<(Point3D A, Point3D B)> AristasDeLaCaja(
        double bx, double by, double bz)
    {
        var v = new[]
        {
            new Point3D(0, 0, 0), new Point3D(bx, 0, 0),
            new Point3D(bx, 0, bz), new Point3D(0, 0, bz),
            new Point3D(0, by, 0), new Point3D(bx, by, 0),
            new Point3D(bx, by, bz), new Point3D(0, by, bz)
        };

        foreach (var (i, j) in new[]
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        })
        {
            yield return (v[i], v[j]);
        }
    }

    /// <summary>Pasa una malla a un modelo del motor, con su material.</summary>
    /// <remarks>
    /// El material lleva una componente <b>especular</b> además de la difusa: el acero tiene
    /// brillo, y sin ese reflejo una barra cilíndrica se ve como un tubo de plástico mate. El
    /// concreto va con casi nada.
    /// </remarks>
    private static void Agregar3D(
        Model3DGroup grupo, TuboDeMalla.Malla malla, Color color, double brillo)
    {
        if (malla.Triangulos.Count == 0)
        {
            return;
        }

        var geo = new MeshGeometry3D
        {
            Positions = new Point3DCollection(malla.Puntos.Count),
            Normals = new Vector3DCollection(malla.Normales.Count),
            TriangleIndices = new Int32Collection(malla.Triangulos)
        };

        foreach (var (x, y, z) in malla.Puntos)
        {
            geo.Positions.Add(new Point3D(x, y, z));
        }

        foreach (var (x, y, z) in malla.Normales)
        {
            geo.Normals.Add(new Vector3D(x, y, z));
        }

        geo.Freeze();

        var material = new MaterialGroup();

        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));

        if (brillo > 0)
        {
            material.Children.Add(new SpecularMaterial(
                new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Clamp(brillo * 255, 0, 255), 0xFF, 0xFF, 0xFF)),
                28));
        }

        grupo.Children.Add(new GeometryModel3D
        {
            Geometry = geo,
            Material = material,

            // La cara de atrás con el mismo material: si una barra quedara con los
            // triángulos al revés se vería igual en lugar de desaparecer. Es una red de
            // seguridad, no una excusa para no cuidar el sentido —eso lo comprueba
            // prueba-tubo-malla midiendo el volumen—.
            BackMaterial = material
        });
    }

    /// <summary>
    /// La <b>sombra proyectada</b> de la pieza en el suelo, como una cara plana.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada esquina de la pieza se lleva al suelo siguiendo la dirección del sol, y la silueta
    /// es la <b>envolvente convexa</b> de todas: las cuatro de la base, que se quedan donde
    /// están, y las cuatro de arriba, que caen lejos. Sale tumbada a lo largo, no como una
    /// huella pegada a la base.
    /// </para>
    /// <para>
    /// La silueta se calcula con la base <b>ya girada</b>, porque gira la pieza y no la cámara:
    /// la sombra se queda donde está y solo cambia de forma. Por eso este modelo se rehace al
    /// girar mientras la jaula no: la jaula gira con una transformación, la sombra no es una
    /// transformación de nada.
    /// </para>
    /// <para>
    /// Va un poco por encima del suelo —medio milímetro— para que el motor no tenga que decidir
    /// entre dos superficies a la misma cota, que es de donde salen los parpadeos.
    /// </para>
    /// </remarks>
    private GeometryModel3D? SombraEnElSuelo(double bx, double by, double bz)
    {
        var ox = SolX / -SolY * by;
        var oz = SolZ / -SolY * by;

        var gr = _giro3DAzimut * Math.PI / 180.0;
        var co = Math.Cos(gr);
        var se = Math.Sin(gr);

        var cx = bx / 2;
        var cz = bz / 2;

        var puntos = new List<(double X, double Y)>();

        foreach (var (x, z) in new[] { (0.0, 0.0), (bx, 0.0), (bx, bz), (0.0, bz) })
        {
            // El mismo giro que lleva la jaula, aplicado a mano: la sombra no puede heredarlo
            // como transformación porque el corrimiento del sol NO gira con la pieza.
            var dx = x - cx;
            var dz = z - cz;

            var gxx = cx + (dx * co) - (dz * se);
            var gzz = cz + (dx * se) + (dz * co);

            puntos.Add((gxx, gzz));
            puntos.Add((gxx + ox, gzz + oz));
        }

        var silueta = Envolvente.Convexa(puntos);

        if (silueta.Count < 3)
        {
            return null;
        }

        var geo = new MeshGeometry3D();

        const double casiElSuelo = 0.05;

        // Un abanico desde el primer vértice: vale para cualquier polígono CONVEXO, y la
        // envolvente lo es por construcción.
        foreach (var (x, z) in silueta)
        {
            geo.Positions.Add(new Point3D(x, casiElSuelo, z));
            geo.Normals.Add(new Vector3D(0, 1, 0));
        }

        for (var i = 1; i + 1 < silueta.Count; i++)
        {
            geo.TriangleIndices.Add(0);
            geo.TriangleIndices.Add(i);
            geo.TriangleIndices.Add(i + 1);
        }

        geo.Freeze();

        var brocha = new SolidColorBrush(Color.FromArgb(0x4C, 0x16, 0x24, 0x33));
        brocha.Freeze();

        // Emisiva y no difusa: una sombra no se ilumina. Con un material difuso, la luz que
        // hace el sombreado de las barras le pegaría también a la sombra y la aclararía justo
        // donde tiene que ser más oscura.
        var material = new EmissiveMaterial(brocha);

        return new GeometryModel3D
        {
            Geometry = geo,
            Material = material,
            BackMaterial = material
        };
    }

    // ======================================================================
    //  La cámara
    // ======================================================================

    /// <summary>
    /// Coloca la cámara para que la pieza y su sombra quepan en el recuadro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La cámara es <b>ortográfica</b>: un dibujo técnico no lleva punto de fuga, y así las dos
    /// varillas de un lecho se ven del mismo tamaño esté una delante de la otra, que es lo que
    /// se quiere comprobar.
    /// </para>
    /// <para>
    /// <b>El encuadre no cambia al girar.</b> Se mide el CILINDRO que envuelve la pieza —de
    /// radio media diagonal de la sección— y no la pieza tal como queda girada. La silueta
    /// girada cambia con cada grado, y con ella el encuadre: la pieza daría saltos de tamaño
    /// mientras se gira en lugar de quedarse girando en su sitio.
    /// </para>
    /// </remarks>
    private void AjustarCamara3D()
    {
        if (_cajaDeLaPieza is null)
        {
            return;
        }

        var (bx, by, bz) = _cajaDeLaPieza.Value;

        var ancho = PreviaViewport.Width;

        // El alto se toma del CONTENEDOR y no del propio recuadro 3D, y no es un detalle: la
        // primera vez que se pulsa 3D el recuadro viene de estar oculto, así que su ActualHeight
        // todavía vale cero —WPF no lo mide hasta la siguiente pasada de composición— y la
        // cámara se quedaría sin colocar. El contenedor sí está medido siempre.
        var alto = PreviaCorteHost.ActualHeight;

        if (double.IsNaN(ancho) || ancho < 10 || alto < 10)
        {
            return;
        }

        var a = AzimutDeLaCamara * Math.PI / 180.0;
        var e = _giro3DElevacion * Math.PI / 180.0;

        var (sa, ca) = (Math.Sin(a), Math.Cos(a));
        var (sE, cE) = (Math.Sin(e), Math.Cos(e));

        // Hacia dónde mira: el ojo baja desde su altura hacia el centro de la pieza.
        var mira = new Vector3D(-sa * cE, -sE, -ca * cE);

        // El «arriba» de la pantalla: la vertical del mundo quitándole lo que va en la
        // dirección de la mirada. Se degenera solo mirando a plomo, y la inclinación está
        // topada a ±89° justo por eso.
        var arriba = new Vector3D(-sa * sE, cE, -ca * sE);

        var derecha = new Vector3D(ca, 0, -sa);

        // ---------- Lo que tiene que caber ----------
        var radio = Math.Sqrt((bx * bx) + (bz * bz)) / 2;

        var cx = bx / 2;
        var cz = bz / 2;

        var ox = SolX / -SolY * by;
        var oz = SolZ / -SolY * by;

        var cabe = new List<Point3D>();

        foreach (var (dx, dz) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            var x = cx + (dx * radio);
            var z = cz + (dz * radio);

            cabe.Add(new Point3D(x, 0, z));
            cabe.Add(new Point3D(x, by, z));
            cabe.Add(new Point3D(x + ox, 0, z + oz));
        }

        var centro = new Point3D(
            cabe.Average(p => p.X), cabe.Average(p => p.Y), cabe.Average(p => p.Z));

        double medioU = 0;
        double medioV = 0;

        foreach (var p in cabe)
        {
            var d = p - centro;

            medioU = Math.Max(medioU, Math.Abs(Vector3D.DotProduct(d, derecha)));
            medioV = Math.Max(medioV, Math.Abs(Vector3D.DotProduct(d, arriba)));
        }

        var aspecto = ancho / alto;

        // Un margen para que la pieza no toque los bordes del recuadro.
        var anchoNecesario = Math.Max(medioU * 2, medioV * 2 * aspecto) * 1.08;

        if (anchoNecesario < 1e-6)
        {
            return;
        }

        PreviaCamara3D.Width = anchoNecesario / Math.Clamp(_zoom3D, Zoom3DMin, Zoom3DMax);

        // El desplazamiento se aplica en el plano de la pantalla, así que arrastrar mueve la
        // pieza en la dirección del ratón con cualquier giro.
        var objetivo = centro + (derecha * _pan3DU) + (arriba * _pan3DV);

        // La distancia no cambia el tamaño en una cámara ortográfica; solo tiene que dejar la
        // pieza dentro del volumen de recorte. Se saca del propio tamaño de la pieza.
        var lejos = (by + radio) * 4 + 1000;

        PreviaCamara3D.Position = objetivo - (mira * lejos);
        PreviaCamara3D.LookDirection = mira;
        PreviaCamara3D.UpDirection = arriba;
        PreviaCamara3D.NearPlaneDistance = 1;
        PreviaCamara3D.FarPlaneDistance = lejos * 3;
    }

    /// <summary>
    /// Aplica el giro y el encuadre <b>sin reconstruir la malla</b>.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que arrastrar sea instantáneo. La jaula solo cambia el ángulo de su
    /// transformación; la sombra sí se rehace, porque al girar la pieza su silueta cambia de
    /// forma y no es una transformación de la anterior. Rehacer un polígono de seis lados no
    /// cuesta nada; rehacer la jaula sí.
    /// </remarks>
    private void ActualizarGiro3D()
    {
        if (_giroDeLaJaula is null || _cajaDeLaPieza is null)
        {
            return;
        }

        _giroDeLaJaula.Angle = _giro3DAzimut;

        var (bx, by, bz) = _cajaDeLaPieza.Value;

        if (PreviaEscena3D.Content is Model3DGroup grupo && _modeloDeSombra is not null)
        {
            grupo.Children.Remove(_modeloDeSombra);

            _modeloDeSombra = SombraEnElSuelo(bx, by, bz);

            if (_modeloDeSombra is not null)
            {
                grupo.Children.Add(_modeloDeSombra);
            }
        }

        AjustarCamara3D();
    }

    // ======================================================================
    //  La geometría del armado, compartida con el corte y con AutoCAD
    // ======================================================================

    /// <summary>El recorrido del <b>estribo</b> de la fila, listo para el 3D.</summary>
    /// <remarks>
    /// <para>
    /// Las dos reglas de geometría salen del dibujo del corte, no de aquí. En
    /// <c>EstriboExterior</c> la cara de fuera va a <c>rec</c> del paño con radio
    /// <c>dEst + dVar/2</c>, y la de dentro a <c>rec + dEst</c> con radio <c>dVar/2</c>, las dos
    /// con el mismo centro. De ahí que el <b>eje</b> vaya a <c>rec + dEst/2</c> con radio
    /// <c>(dEst + dVar)/2</c>: es la consecuencia de que el doblez envuelva la varilla de la
    /// esquina.
    /// </para>
    /// <para>
    /// Los radios de arriba y de abajo salen distintos cuando los lechos llevan calibres
    /// distintos, que es lo normal en una trabe.
    /// </para>
    /// </remarks>
    private static TrazoEstribo.Trazo? TrazoDelEstribo3D(
        SeccionConcretoRow s, double de, double rec, int tramosPorDoblez)
    {
        if (de <= 0)
        {
            return null;
        }

        Varilla.TryDiametroCm(s.DiamEsqSup, out var dSup);
        Varilla.TryDiametroCm(s.DiamEsqInfEfectivo, out var dInf);

        // Sin calibre reconocido en un lecho se usa el del otro, y si tampoco, el del estribo:
        // el radio del doblez tiene que salir de algo.
        if (dSup <= 0) { dSup = dInf > 0 ? dInf : de; }
        if (dInf <= 0) { dInf = dSup; }

        var medio = de / 2;

        return TrazoEstribo.Eje(
            rec + medio, rec + medio,
            s.BaseCm - rec - medio, s.AlturaCm - rec - medio,
            (de + dSup) / 2, (de + dInf) / 2,
            s.GanchoCm,
            tramosPorDoblez);
    }

    /// <summary>Busca una varilla por su señal en la tabla de la vista previa.</summary>
    /// <remarks>
    /// Devuelve también el <b>radio</b>: el doblez de una grapa envuelve la varilla, así que sin
    /// su radio no se puede saber por dónde pasa. Devuelve <c>null</c> si la señal ya no apunta
    /// a nada, y entonces esa grapa se salta: es lo mismo que hace el dibujo del corte cuando
    /// el lecho se quedó con menos varillas.
    /// </remarks>
    private static (double X, double Y, double R)? BuscarVarillaPrevia(
        List<(RefVarilla Ref, double X, double Y, double R)> varillas, RefVarilla señal)
    {
        foreach (var v in varillas)
        {
            if (v.Ref.Equals(señal))
            {
                return (v.X, v.Y, v.R);
            }
        }

        return null;
    }

    /// <summary>El diámetro del estribo <b>diamante</b>, en centímetros.</summary>
    /// <remarks>
    /// Sin diámetro propio capturado se usa el del estribo principal, que es exactamente la
    /// regla que sigue el dibujante de AutoCAD en <c>EstriboDiamante</c>.
    /// </remarks>
    private static double DiametroDelDiamante(SeccionConcretoRow s, double de) =>
        Varilla.TryDiametroCm(
            string.IsNullOrWhiteSpace(s.DiamEstriboDiamante)
                ? s.Estribo
                : s.DiamEstriboDiamante,
            out var dDia) && dDia > 0
            ? dDia
            : de;

    /// <summary>El <b>eje</b> del estribo diamante en el plano de la sección, muestreado.</summary>
    /// <remarks>
    /// <para>
    /// Sale de <see cref="TrazoDiamante"/>, la misma clase que usa el corte y el dibujante de
    /// AutoCAD. Si se calculara aquí, el diamante del 3D podría abrazar otras varillas que el
    /// de la sección.
    /// </para>
    /// <para>
    /// <b>Se pide la cinta a <c>dDia/2</c> y no a 0.</b> A cero, <c>Cinta</c> devuelve la cara
    /// de DENTRO del diamante, que es lo que el corte necesita para trazar sus dos caras. Aquí
    /// se hace un tubo, así que hace falta el EJE, que va medio diámetro por fuera de esa cara.
    /// Con la cara de dentro, el diamante salía corrido medio diámetro respecto a las varillas
    /// que abraza.
    /// </para>
    /// </remarks>
    private List<(double X, double Y)>? RecorridoDelDiamante3D(
        SeccionConcretoRow s, double de, double rec, double dDia, int tramosPorDoblez)
    {
        if (dDia <= 0)
        {
            return null;
        }

        var x1 = rec;
        var y1 = rec;
        var x2 = s.BaseCm - rec;
        var y2 = s.AlturaCm - rec;

        if (x2 <= x1 || y2 <= y1)
        {
            return null;
        }

        var varSup = PosicionesDeLecho(s, s.NEsqSup, s.DiamEsqSup, de, rec,
                                       arriba: true, intermedio: false);

        var varInf = PosicionesDeLecho(s, s.NEsqInf, s.DiamEsqInfEfectivo, de, rec,
                                       arriba: false, intermedio: false);

        var varLat = PosicionesLaterales(s, de, rec);

        var centros = TrazoDiamante.Centros(x1, y1, x2, y2, dDia, varSup, varInf, varLat);

        if (centros is null)
        {
            return null;
        }

        var geo = TrazoDiamante.Cinta(centros, dDia / 2);

        if (geo is null)
        {
            return null;
        }

        var puntos = TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, tramosPorDoblez);

        return puntos.Count < 3 ? null : puntos;
    }
}
