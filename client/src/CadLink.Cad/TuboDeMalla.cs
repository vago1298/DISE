namespace CadLink.Cad;

/// <summary>
/// Convierte el <b>eje</b> de una varilla en la <b>malla de un tubo</b>: triángulos, con sus
/// normales, listos para un motor 3D.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe.</b> La vista en 3D pintaba las barras como líneas gruesas con un
/// degradado sobre un lienzo plano: barras que <i>parecían</i> redondas. Eso tiene un techo
/// que no se arregla afinando, y son tres cosas a la vez:
/// </para>
/// <list type="number">
///   <item>
///     No hay profundidad por píxel. El orden se decidía por segmentos, y dos barras
///     <b>tangentes</b> —un estribo abrazando una varilla es exactamente eso— tienen
///     prácticamente la misma distancia al ojo en el cruce, así que ningún orden de
///     segmentos acierta. De ahí los traspasos según el ángulo.
///   </item>
///   <item>
///     No eran sólidos: el volumen era un degradado pintado, así que no había silueta ni
///     oclusión de verdad.
///   </item>
///   <item>
///     Cada mejora costaba figuras, y ya iban miles por redibujado.
///   </item>
/// </list>
/// <para>
/// Con tubos de malla eso desaparece: el motor resuelve la oclusión <b>por píxel</b> con su
/// buffer de profundidad, las barras son sólidos y el sombreado sale de una luz. Y girar pasa
/// a ser una transformación, no un redibujado: la malla se construye una vez.
/// </para>
/// <para>
/// <b>Está aquí, sin WPF, para poder probarla.</b> Es la misma razón que
/// <see cref="TrazoEstribo"/> y <see cref="Envolvente"/>: <c>CadLink.App</c> no se puede
/// compilar en el entorno de trabajo, así que toda la geometría que se pueda sacar de ahí, se
/// saca. La prueba está en <c>tools/prueba-tubo-malla</c>, y comprueba hasta el <b>volumen</b>
/// del tubo contra la fórmula del prisma.
/// </para>
/// </remarks>
public static class TuboDeMalla
{
    /// <summary>Una malla de triángulos que se va llenando.</summary>
    /// <remarks>
    /// Se acumulan <b>muchas barras en una sola malla</b> a propósito: un motor 3D dibuja
    /// mucho más rápido una malla de sesenta mil triángulos que seis mil mallas de diez. Todo
    /// lo que comparta material —el color— va junto.
    /// </remarks>
    public sealed class Malla
    {
        public List<(double X, double Y, double Z)> Puntos { get; } = new();

        public List<(double X, double Y, double Z)> Normales { get; } = new();

        /// <summary>Índices de tres en tres.</summary>
        public List<int> Triangulos { get; } = new();

        public int CuantosTriangulos => Triangulos.Count / 3;
    }

    /// <summary>Cuántos lados lleva el tubo por omisión.</summary>
    /// <remarks>
    /// Ocho es de sobra para una varilla: a los tamaños de la vista previa el contorno de un
    /// octógono no se distingue de un círculo, y subir a dieciséis dobla los triángulos sin
    /// que se note. Con seis ya se adivina la faceta en las barras gruesas.
    /// </remarks>
    public const int LadosPorOmision = 8;

    /// <summary>
    /// Añade a <paramref name="malla"/> el tubo que recorre <paramref name="eje"/>.
    /// </summary>
    /// <param name="eje">El eje de la barra, en el orden en que la recorre.</param>
    /// <param name="radio">Radio del tubo, o sea medio diámetro de la varilla.</param>
    /// <param name="cerrado">
    /// Si el recorrido vuelve a su principio. Cerrado no lleva tapas; abierto sí, porque una
    /// varilla cortada enseña su sección.
    /// </param>
    /// <param name="lados">Lados del tubo.</param>
    /// <returns>Cuántos triángulos se añadieron. Cero si el eje no daba para un tubo.</returns>
    public static int Agregar(
        Malla malla,
        IReadOnlyList<(double X, double Y, double Z)> eje,
        double radio,
        bool cerrado = false,
        int lados = LadosPorOmision)
    {
        if (radio <= 0 || lados < 3)
        {
            return 0;
        }

        // Fuera los puntos repetidos seguidos: dan una tangente indefinida y un anillo
        // degenerado. Salen solos donde una recta empalma con un doblez.
        var p = new List<(double X, double Y, double Z)>();

        foreach (var q in eje)
        {
            if (p.Count > 0 && Distancia(p[^1], q) < 1e-9)
            {
                continue;
            }

            p.Add(q);
        }

        // En un recorrido cerrado, el último punto puede repetir el primero.
        if (cerrado && p.Count > 1 && Distancia(p[0], p[^1]) < 1e-9)
        {
            p.RemoveAt(p.Count - 1);
        }

        if (p.Count < 2)
        {
            return 0;
        }

        var n = p.Count;
        var trianguloAlEmpezar = malla.Triangulos.Count;
        var baseVertices = malla.Puntos.Count;

        // ---------- La terna que acompaña al eje ----------
        //
        // En cada punto hace falta un plano perpendicular al eje para poner el anillo. El
        // problema conocido es el RETORCIDO: si en cada punto se eligiera una perpendicular
        // cualquiera, el anillo giraría de un punto al siguiente y el tubo saldría trenzado.
        //
        // Se resuelve TRANSPORTANDO la terna: la perpendicular del punto anterior se proyecta
        // sobre el plano del actual y se vuelve a normalizar. Así el anillo gira lo menos
        // posible, que es lo que hace que un tubo doblado no se retuerza.
        var u = PrimeraPerpendicular(Tangente(p, 0, cerrado));

        for (var i = 0; i < n; i++)
        {
            var t = Tangente(p, i, cerrado);

            u = Transportar(u, t);

            var v = Cruz(t, u);

            for (var j = 0; j < lados; j++)
            {
                var a = 2 * Math.PI * j / lados;

                var co = Math.Cos(a);
                var se = Math.Sin(a);

                // La normal de la superficie de un tubo es RADIAL: del eje hacia fuera.
                var nx = (co * u.X) + (se * v.X);
                var ny = (co * u.Y) + (se * v.Y);
                var nz = (co * u.Z) + (se * v.Z);

                malla.Puntos.Add((
                    p[i].X + (radio * nx),
                    p[i].Y + (radio * ny),
                    p[i].Z + (radio * nz)));

                malla.Normales.Add((nx, ny, nz));
            }
        }

        // ---------- Los triángulos entre anillo y anillo ----------
        //
        // El SENTIDO importa: al revés, las normales apuntan hacia dentro y el motor pinta el
        // tubo negro o lo descarta por estar de espaldas. El orden de abajo está comprobado
        // contra el volumen del tubo, que sale positivo solo si el giro es el correcto.
        var anillos = cerrado ? n : n - 1;

        for (var i = 0; i < anillos; i++)
        {
            var a0 = baseVertices + (i * lados);
            var a1 = baseVertices + (((i + 1) % n) * lados);

            for (var j = 0; j < lados; j++)
            {
                var k = (j + 1) % lados;

                malla.Triangulos.Add(a0 + j);
                malla.Triangulos.Add(a0 + k);
                malla.Triangulos.Add(a1 + k);

                malla.Triangulos.Add(a0 + j);
                malla.Triangulos.Add(a1 + k);
                malla.Triangulos.Add(a1 + j);
            }
        }

        // ---------- Las tapas ----------
        if (!cerrado)
        {
            Tapa(malla, p[0], Tangente(p, 0, false), radio, lados, baseVertices, alPrincipio: true);

            Tapa(malla, p[^1], Tangente(p, n - 1, false), radio, lados,
                 baseVertices + ((n - 1) * lados), alPrincipio: false);
        }

        return (malla.Triangulos.Count - trianguloAlEmpezar) / 3;
    }

    /// <summary>Cierra un extremo del tubo con un abanico de triángulos.</summary>
    /// <remarks>
    /// La tapa del principio mira hacia <b>atrás</b> —o sea contra la tangente— y la del final
    /// hacia delante, así que sus triángulos giran al contrario una de la otra. Sin cerrar los
    /// extremos, mirando una varilla de punta se le vería el hueco por dentro.
    /// </remarks>
    private static void Tapa(
        Malla malla,
        (double X, double Y, double Z) centro,
        (double X, double Y, double Z) tangente,
        double radio, int lados, int baseAnillo, bool alPrincipio)
    {
        var iCentro = malla.Puntos.Count;

        malla.Puntos.Add(centro);

        malla.Normales.Add(alPrincipio
            ? (-tangente.X, -tangente.Y, -tangente.Z)
            : tangente);

        // El anillo se REPITE con la normal de la tapa: un vértice no puede tener dos
        // normales, y compartir los del tubo dejaría el canto de la tapa redondeado.
        var iAnillo = malla.Puntos.Count;

        for (var j = 0; j < lados; j++)
        {
            malla.Puntos.Add(malla.Puntos[baseAnillo + j]);

            malla.Normales.Add(alPrincipio
                ? (-tangente.X, -tangente.Y, -tangente.Z)
                : tangente);
        }

        for (var j = 0; j < lados; j++)
        {
            var k = (j + 1) % lados;

            malla.Triangulos.Add(iCentro);

            if (alPrincipio)
            {
                malla.Triangulos.Add(iAnillo + k);
                malla.Triangulos.Add(iAnillo + j);
            }
            else
            {
                malla.Triangulos.Add(iAnillo + j);
                malla.Triangulos.Add(iAnillo + k);
            }
        }
    }

    /// <summary>La dirección del eje en el punto <paramref name="i"/>.</summary>
    /// <remarks>
    /// En los puntos de dentro se toma la del vecino anterior al siguiente, que promedia los
    /// dos tramos y hace que el anillo caiga en la bisectriz del doblez: así el tubo no se
    /// estrangula al doblar. En los extremos de un recorrido abierto solo hay un tramo.
    /// </remarks>
    private static (double X, double Y, double Z) Tangente(
        List<(double X, double Y, double Z)> p, int i, bool cerrado)
    {
        var n = p.Count;

        var antes = cerrado ? p[((i - 1) % n + n) % n] : p[Math.Max(0, i - 1)];
        var despues = cerrado ? p[(i + 1) % n] : p[Math.Min(n - 1, i + 1)];

        var t = (despues.X - antes.X, despues.Y - antes.Y, despues.Z - antes.Z);

        var largo = Math.Sqrt((t.Item1 * t.Item1) + (t.Item2 * t.Item2) + (t.Item3 * t.Item3));

        if (largo < 1e-12)
        {
            return (0, 0, 1);
        }

        return (t.Item1 / largo, t.Item2 / largo, t.Item3 / largo);
    }

    /// <summary>Una perpendicular cualquiera a <paramref name="t"/>, pero estable.</summary>
    /// <remarks>
    /// Se cruza con el eje coordenado <b>menos alineado</b> con la tangente. Cruzar siempre
    /// con el mismo eje falla justo cuando la tangente va en su dirección: el producto sale
    /// cero y la perpendicular queda indefinida. Es el caso de las varillas
    /// longitudinales, que van todas en vertical.
    /// </remarks>
    private static (double X, double Y, double Z) PrimeraPerpendicular(
        (double X, double Y, double Z) t)
    {
        var ax = Math.Abs(t.X);
        var ay = Math.Abs(t.Y);
        var az = Math.Abs(t.Z);

        var eje = ax <= ay && ax <= az
            ? (1.0, 0.0, 0.0)
            : ay <= az
                ? (0.0, 1.0, 0.0)
                : (0.0, 0.0, 1.0);

        return Normalizar(Cruz(t, eje));
    }

    /// <summary>Lleva <paramref name="u"/> al plano perpendicular a <paramref name="t"/>.</summary>
    private static (double X, double Y, double Z) Transportar(
        (double X, double Y, double Z) u, (double X, double Y, double Z) t)
    {
        var d = (u.X * t.X) + (u.Y * t.Y) + (u.Z * t.Z);

        var proyectado = (u.X - (d * t.X), u.Y - (d * t.Y), u.Z - (d * t.Z));

        var largo = Math.Sqrt(
            (proyectado.Item1 * proyectado.Item1)
            + (proyectado.Item2 * proyectado.Item2)
            + (proyectado.Item3 * proyectado.Item3));

        // Si el eje dio media vuelta, la proyección se anula y hay que empezar de nuevo. No
        // pasa en el recorrido de una varilla, pero dejarlo sin cubrir daría una malla con
        // ceros que el motor no sabría dibujar.
        return largo < 1e-9
            ? PrimeraPerpendicular(t)
            : (proyectado.Item1 / largo, proyectado.Item2 / largo, proyectado.Item3 / largo);
    }

    private static (double X, double Y, double Z) Cruz(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        ((a.Y * b.Z) - (a.Z * b.Y),
         (a.Z * b.X) - (a.X * b.Z),
         (a.X * b.Y) - (a.Y * b.X));

    private static (double X, double Y, double Z) Normalizar(
        (double X, double Y, double Z) v)
    {
        var largo = Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        return largo < 1e-12 ? (0, 0, 1) : (v.X / largo, v.Y / largo, v.Z / largo);
    }

    private static double Distancia(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
