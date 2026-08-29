namespace CadLink.Cad;

/// <summary>
/// La <b>envolvente convexa</b> de unos puntos en el plano.
/// </summary>
/// <remarks>
/// <para>
/// Hace falta para la <b>sombra</b> de la vista en 3D. La sombra de un prisma en el suelo es
/// la unión de su base con su tapa corrida, o sea de dos polígonos iguales desplazados, y su
/// silueta tiene más lados que cada uno: la de dos rectángulos alineados con los ejes tiene
/// seis. Dibujar los dos por separado no vale, porque al ser translúcidos la zona común
/// saldría del doble de oscura.
/// </para>
/// <para>
/// <b>Está aquí y no en la aplicación a propósito.</b> Hubo una primera versión metida en
/// <c>MainWindow.Seccion3D.cs</c> y se quitó por una razón concreta: <c>CadLink.App</c> no se
/// puede compilar ni probar en el entorno donde se trabaja —falta el ref pack de WPF—, y un
/// algoritmo con casos límite sin una prueba que los recorra no aguanta. Aquí sí se puede
/// ejecutar contra el binario, y es lo que hace <c>tools/prueba-envolvente</c>.
/// </para>
/// <para>
/// Mientras la pieza no giraba, la silueta se podía escribir a mano como un hexágono. Al
/// hacer que la pieza gire —el sol y el suelo quietos, la sección dando vueltas— la base ya
/// no está alineada con los ejes y esa fórmula deja de valer: hay que resolver la envolvente
/// de verdad.
/// </para>
/// </remarks>
public static class Envolvente
{
    /// <summary>
    /// La envolvente convexa, en sentido <b>antihorario</b> y sin puntos repetidos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cadena monótona de Andrew: se ordenan los puntos por X y luego por Y, y se recorren dos
    /// veces —de izquierda a derecha para el borde de abajo y de vuelta para el de arriba—
    /// quitando el punto anterior mientras el giro no sea a la izquierda.
    /// </para>
    /// <para>
    /// Los puntos <b>alineados</b> se descartan, con el <c>&lt;= 0</c> de la comparación: un
    /// vértice de más en el medio de un lado recto no cambia la figura pero sí el número de
    /// lados, y el que llama lo usa para decidir cosas.
    /// </para>
    /// <para>
    /// Con menos de tres puntos distintos no hay polígono: se devuelven los que haya, sin
    /// inventar nada. Es responsabilidad del que llama no dibujar un polígono de dos puntos.
    /// </para>
    /// </remarks>
    public static List<(double X, double Y)> Convexa(
        IReadOnlyList<(double X, double Y)> puntos, double tolerancia = 1e-9)
    {
        // Primero fuera los repetidos: con puntos duplicados la cadena monótona puede dejar
        // vértices dobles en las esquinas.
        var unicos = new List<(double X, double Y)>();

        foreach (var p in puntos.OrderBy(p => p.X).ThenBy(p => p.Y))
        {
            if (unicos.Count > 0
                && Math.Abs(unicos[^1].X - p.X) <= tolerancia
                && Math.Abs(unicos[^1].Y - p.Y) <= tolerancia)
            {
                continue;
            }

            unicos.Add(p);
        }

        if (unicos.Count < 3)
        {
            return unicos;
        }

        // El giro de o->a->b: positivo es a la izquierda.
        static double Giro(
            (double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

        var abajo = new List<(double X, double Y)>();

        foreach (var p in unicos)
        {
            while (abajo.Count >= 2 && Giro(abajo[^2], abajo[^1], p) <= tolerancia)
            {
                abajo.RemoveAt(abajo.Count - 1);
            }

            abajo.Add(p);
        }

        var arriba = new List<(double X, double Y)>();

        for (var i = unicos.Count - 1; i >= 0; i--)
        {
            var p = unicos[i];

            while (arriba.Count >= 2 && Giro(arriba[^2], arriba[^1], p) <= tolerancia)
            {
                arriba.RemoveAt(arriba.Count - 1);
            }

            arriba.Add(p);
        }

        // Todos alineados: las dos cadenas son el mismo segmento ida y vuelta, y no hay
        // polígono que devolver.
        if (abajo.Count < 3 && arriba.Count < 3)
        {
            return new List<(double X, double Y)> { unicos[0], unicos[^1] };
        }

        // Los extremos están en las dos cadenas: se quita uno de cada.
        abajo.RemoveAt(abajo.Count - 1);
        arriba.RemoveAt(arriba.Count - 1);

        abajo.AddRange(arriba);

        return abajo;
    }

    /// <summary>
    /// El mismo polígono convexo <b>ensanchado</b> <paramref name="cuanto"/> metros por fuera.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es para la <b>penumbra</b> de la sombra. Una sombra de verdad no tiene el borde cortado a
    /// tijera: se va deshaciendo en una banda de <b>ancho constante</b> alrededor de la silueta.
    /// Y ahí estaba el defecto de la versión anterior, que apilaba siluetas <i>escaladas</i>
    /// desde el centro: al escalar, la banda sale proporcional a la distancia al centro, así que
    /// en una sombra alargada la punta se ensancha muchísimo y los costados casi nada. Eso no se
    /// lee como una sombra difuminada, se lee como platos apilados, que es justo lo que se veía.
    /// </para>
    /// <para>
    /// Cada lado se desplaza hacia fuera <paramref name="cuanto"/> y las esquinas se recuperan
    /// <b>cortando</b> los lados desplazados. El sentido de «fuera» se saca del signo del área,
    /// así que da igual si el polígono viene en sentido horario o antihorario.
    /// </para>
    /// <para>
    /// <b>El tope del pico</b> —<paramref name="topeDePico"/>— es lo que salva las esquinas muy
    /// agudas. En un vértice casi en punta, los dos lados desplazados se cortan lejísimos y sale
    /// una espina larguísima, que en la sombra aparece como un pincho saliendo de la nada.
    /// Pasado el tope se <b>chaflana</b>: en lugar de una esquina en punta se ponen dos puntos,
    /// uno por lado. Es el «miter limit» de siempre, y es la diferencia entre una sombra con el
    /// borde redondeado y una con agujas.
    /// </para>
    /// </remarks>
    /// <param name="convexa">El polígono, convexo. Sale de <see cref="Convexa"/>.</param>
    /// <param name="cuanto">Cuánto se ensancha, en las mismas unidades que los puntos.</param>
    /// <param name="topeDePico">
    /// Cuántas veces <paramref name="cuanto"/> se admite que la esquina se aleje antes de
    /// chaflanarla.
    /// </param>
    public static List<(double X, double Y)> Ensanchada(
        IReadOnlyList<(double X, double Y)> convexa,
        double cuanto,
        double topeDePico = 3.0)
    {
        var n = convexa?.Count ?? 0;

        if (convexa is null || n < 3 || cuanto <= 0)
        {
            return convexa?.ToList() ?? new List<(double X, double Y)>();
        }

        // El signo del área dice el sentido del recorrido, y de ahí cuál de las dos normales de
        // cada lado apunta hacia fuera.
        double doble = 0;

        for (var i = 0; i < n; i++)
        {
            var a = convexa[i];
            var b = convexa[(i + 1) % n];

            doble += (a.X * b.Y) - (b.X * a.Y);
        }

        // Antihorario (área positiva): la normal exterior del lado a→b es (dy, -dx).
        var signo = doble >= 0 ? 1.0 : -1.0;

        // Cada lado, ya desplazado: un punto suyo y su dirección.
        var lados = new (double Px, double Py, double Dx, double Dy, double Nx, double Ny)[n];

        for (var i = 0; i < n; i++)
        {
            var a = convexa[i];
            var b = convexa[(i + 1) % n];

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;

            var largo = Math.Sqrt((dx * dx) + (dy * dy));

            if (largo < 1e-12)
            {
                // Un lado de largo cero no tiene normal: se deja donde está y las esquinas
                // vecinas lo resuelven entre ellas.
                lados[i] = (a.X, a.Y, 1, 0, 0, 0);
                continue;
            }

            dx /= largo;
            dy /= largo;

            var nx = signo * dy;
            var ny = signo * -dx;

            lados[i] = (a.X + (nx * cuanto), a.Y + (ny * cuanto), dx, dy, nx, ny);
        }

        var salida = new List<(double X, double Y)>(n + 4);

        for (var j = 0; j < n; j++)
        {
            // La esquina j está entre el lado anterior y el lado j.
            var r = lados[(j - 1 + n) % n];
            var s = lados[j];

            var det = (r.Dx * -s.Dy) - (r.Dy * -s.Dx);

            var v = convexa[j];

            if (Math.Abs(det) < 1e-12)
            {
                // Lados paralelos: no hay esquina que cortar, se desplaza el vértice.
                salida.Add((v.X + (s.Nx * cuanto), v.Y + (s.Ny * cuanto)));
                continue;
            }

            var ex = s.Px - r.Px;
            var ey = s.Py - r.Py;

            var t = ((ex * -s.Dy) - (ey * -s.Dx)) / det;

            var px = r.Px + (r.Dx * t);
            var py = r.Py + (r.Dy * t);

            // ¿Se fue muy lejos? Entonces se chaflana con un punto por lado.
            var lejos = Math.Sqrt(((px - v.X) * (px - v.X)) + ((py - v.Y) * (py - v.Y)));

            if (lejos > cuanto * topeDePico)
            {
                salida.Add((v.X + (r.Nx * cuanto), v.Y + (r.Ny * cuanto)));
                salida.Add((v.X + (s.Nx * cuanto), v.Y + (s.Ny * cuanto)));

                continue;
            }

            salida.Add((px, py));
        }

        return salida;
    }
}
