namespace CadLink.Cad;

/// <summary>
/// El <b>eje</b> de una barra de acero en el espacio: lo que hace falta para barrerla.
/// </summary>
/// <remarks>
/// <para>
/// Es la aritmética de <see cref="Jaula3dDrawer"/>, separada a propósito. El dibujante habla con
/// AutoCAD por COM y no se puede probar aquí; esto sí, y es donde está lo que puede salir mal de
/// verdad: la <b>orientación del perfil</b>. Barrer un círculo por un camino solo da una varilla
/// redonda si el círculo arranca <b>perpendicular</b> al camino. Si arranca torcido, la varilla
/// sale con la sección elíptica y más gruesa de lo que dice la tabla.
/// </para>
/// <para>
/// Y no es un detalle teórico: los estribos van en un plano horizontal —su tangente inicial es
/// horizontal— y las varillas longitudinales suben —su tangente es vertical—. Un círculo creado en
/// el plano XY, que es lo que da AutoCAD por omisión, está bien para la varilla y <b>mal girado
/// noventa grados</b> para el estribo.
/// </para>
/// </remarks>
public static class EjeDeBarra
{
    private const double Nada = 1e-9;

    /// <summary>
    /// Quita los puntos <b>repetidos seguidos</b> del recorrido.
    /// </summary>
    /// <remarks>
    /// Salen solos donde una recta empalma con un doblez, y hay que quitarlos: dos puntos iguales
    /// no tienen dirección, así que la tangente ahí es indefinida y AutoCAD rechaza el camino o lo
    /// barre en un pico. Es la misma limpieza que hace <see cref="TuboDeMalla"/> antes de generar
    /// sus anillos, y por el mismo motivo.
    /// </remarks>
    public static List<(double X, double Y, double Z)> Limpio(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double tol = 1e-7)
    {
        var salida = new List<(double X, double Y, double Z)>();

        if (eje is null)
        {
            return salida;
        }

        foreach (var p in eje)
        {
            if (salida.Count > 0 && Distancia(salida[^1], p) <= tol)
            {
                continue;
            }

            salida.Add(p);
        }

        return salida;
    }

    /// <summary>
    /// La <b>tangente</b> al principio del recorrido, unitaria. Nula si no hay recorrido.
    /// </summary>
    /// <remarks>
    /// Es la normal que hay que darle al círculo del perfil para que arranque perpendicular al
    /// camino. Se toma del <b>primer tramo con largo</b> y no del primer par de puntos a secas:
    /// con un punto repetido al principio —que pasa— el primer par daría dirección cero y el
    /// círculo se quedaría en el plano de AutoCAD, o sea mal girado.
    /// </remarks>
    public static (double X, double Y, double Z) TangenteInicial(
        IReadOnlyList<(double X, double Y, double Z)>? eje)
    {
        if (eje is null || eje.Count < 2)
        {
            return (0, 0, 0);
        }

        for (var i = 1; i < eje.Count; i++)
        {
            var dx = eje[i].X - eje[0].X;
            var dy = eje[i].Y - eje[0].Y;
            var dz = eje[i].Z - eje[0].Z;

            var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (largo > Nada)
            {
                return (dx / largo, dy / largo, dz / largo);
            }
        }

        return (0, 0, 0);
    }

    /// <summary>El largo total del recorrido, sumando tramo a tramo.</summary>
    /// <remarks>
    /// Sirve para descartar lo que no da para una varilla y para poder decir cuánto acero se
    /// dibujó, que es un número que el usuario puede comparar con su tabla.
    /// </remarks>
    public static double Largo(IReadOnlyList<(double X, double Y, double Z)>? eje)
    {
        if (eje is null || eje.Count < 2)
        {
            return 0;
        }

        double total = 0;

        for (var i = 1; i < eje.Count; i++)
        {
            total += Distancia(eje[i - 1], eje[i]);
        }

        return total;
    }

    /// <summary>¿El recorrido <b>vuelve a su principio</b>?</summary>
    /// <remarks>
    /// Un estribo es cerrado y una varilla no. Importa para el camino que se le da a AutoCAD: uno
    /// cerrado tiene que cerrarse de verdad, o el barrido deja una muesca en la esquina donde
    /// empezó.
    /// </remarks>
    public static bool Cerrado(
        IReadOnlyList<(double X, double Y, double Z)>? eje, double tol = 1e-6) =>
        eje is not null && eje.Count > 2 && Distancia(eje[0], eje[^1]) <= tol;

    /// <summary>El recorrido en la tira plana de tres en tres que espera AutoCAD.</summary>
    /// <remarks>
    /// AutoCAD recibe los vértices de una polilínea 3D como un solo arreglo de dobles
    /// —x, y, z, x, y, z…—, no como una lista de puntos. Se arma aquí para que el dibujante no
    /// tenga que hacer aritmética de índices con COM de por medio.
    /// </remarks>
    public static double[] Tira(IReadOnlyList<(double X, double Y, double Z)> eje)
    {
        var tira = new double[eje.Count * 3];

        for (var i = 0; i < eje.Count; i++)
        {
            tira[3 * i] = eje[i].X;
            tira[(3 * i) + 1] = eje[i].Y;
            tira[(3 * i) + 2] = eje[i].Z;
        }

        return tira;
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
