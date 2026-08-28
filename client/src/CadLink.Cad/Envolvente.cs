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
}
