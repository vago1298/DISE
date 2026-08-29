using System.Globalization;

namespace CadLink.Cad;

/// <summary>
/// Dibuja la <b>jaula de armado en 3D</b> en AutoCAD: cada barra como sólidos de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Las varillas salen <b>sólidas</b>, no de malla ni de alambre, porque un sólido se puede
/// <b>seccionar, medir y acotar</b> y eso es lo que hace que el dibujo sirva para trabajar. Es el
/// mismo criterio de <see cref="Modelo3dDrawer"/>.
/// </para>
///
/// <para><b>POR QUÉ NO SE BARRE EL PERFIL POR EL EJE</b></para>
/// <para>
/// La forma bonita de hacer esto es barrer un círculo por el eje de la varilla:
/// <c>AddExtrudedSolidAlongPath</c>, una llamada y sale la varilla entera con sus dobleces. Se
/// hizo así y <b>cierra AutoCAD 2026</b>. No lanza excepción, no da error, no se puede capturar:
/// desaparece la ventana y sale el informe de fallo de AutoCAD. Con caminos cerrados —o sea, con
/// todos los estribos— es inmediato.
/// </para>
/// <para>
/// Y arrastraba un problema que tampoco tenía solución limpia: el barrido necesita una
/// <b>región</b> y una <b>polilínea</b> auxiliares, y AutoCAD <b>consume</b> algunas de las dos.
/// Llamar a <c>Delete()</c> sobre un objeto COM ya consumido tampoco lanza excepción: también se
/// lleva AutoCAD por delante. Había que adivinar de quién era cada objeto, y eso no se puede
/// comprobar sin AutoCAD delante.
/// </para>
/// <para>
/// Así que se cambió de raíz. Cada <b>tramo recto</b> del eje se dibuja con
/// <c>AddCylinder</c>, que es la llamada más simple del API de sólidos: un centro, un radio y una
/// altura. No hay perfil, no hay camino, no hay región, no hay auxiliares y por tanto no hay nada
/// que borrar ni dueño que adivinar. Una varilla recta es <b>un</b> cilindro. Una doblada son
/// varios, y ahí están los dos cuidados que sí hacen falta:
/// </para>
/// <list type="number">
///   <item>
///     <b>Los tramos se solapan en las uniones.</b> Dos cilindros que se tocan justo en la punta
///     dejan la esquina del doblez comida. Cada tramo se alarga el radio de la varilla por su
///     propio eje —<see cref="EjeDeBarra.Tramos"/>—, y solo en las uniones: alargar las puntas
///     libres haría la varilla más larga que la de la tabla.
///   </item>
///   <item>
///     <b>El eje se simplifica antes.</b> La vista previa dibuja cada doblez con catorce muestras
///     porque en pantalla se ve la curva; aquí cada muestra sería un sólido y un estribo saldría
///     con ochenta y cuatro. <see cref="EjeDeBarra.Simplificado"/> los baja a unos treinta sin
///     mover las puntas.
///   </item>
/// </list>
/// <para>
/// <b>Un cilindro nace de pie y centrado</b> en el punto que se le pasa, así que se hace en el
/// origen y se lleva a su sitio con <c>TransformBy</c> y la matriz de
/// <see cref="EjeDeBarra.MatrizDeTramo"/>. Es el mismo camino que ya funciona en
/// <see cref="Modelo3dDrawer"/> para las columnas y las trabes del modelo.
/// </para>
/// <para>
/// Y si una barra no sale, <b>no se pierde</b>: se dibuja su eje como polilínea 3D y se anota.
/// Mejor una línea donde va la varilla que un hueco silencioso.
/// </para>
/// </remarks>
public sealed class Jaula3dDrawer
{
    /// <summary>Menos que esto no es una barra: es un nudo mal leído.</summary>
    private const double LargoMinimo = 1e-6;

    /// <summary>Cuánto se deja que se enderece el eje al simplificarlo, en grados.</summary>
    /// <remarks>
    /// Veinte grados deja unos cinco tramos por doblez de noventa. Menos no se distingue en un
    /// dibujo de estructuras y más ya se ve poligonal en un acercamiento a un gancho.
    /// </remarks>
    public const double GradosDeSimplificacion = 20;

    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly List<string> _notas = new();

    /// <summary>Lo que hay que contarle al usuario del último dibujo.</summary>
    public IReadOnlyList<string> Notas => _notas;

    public Jaula3dDrawer(dynamic doc)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => doc.ModelSpace);

        _ = AcadInterop.TipoEntidad;
    }

    /// <summary>Una barra de la jaula, con su recorrido ya en coordenadas del dibujo.</summary>
    /// <remarks>
    /// El recorrido llega <b>ya en las unidades y los ejes de AutoCAD</b> —metros y Z arriba—.
    /// Convertir es cosa de quien llama, porque es quien sabe en qué convenio venía: la vista
    /// previa trabaja en centímetros y con la Y hacia arriba.
    /// </remarks>
    public sealed class Barra
    {
        /// <summary>El eje, punto a punto.</summary>
        public required List<(double X, double Y, double Z)> Eje { get; init; }

        /// <summary>Radio de la varilla, en unidades del dibujo.</summary>
        public required double Radio { get; init; }

        public string Capa { get; init; } = CapaVarillas;

        /// <summary>Para poder nombrarla en un aviso.</summary>
        public string Id { get; init; } = string.Empty;
    }

    /// <summary>Qué salió del dibujo: barras sólidas, barras en línea y cuántos sólidos.</summary>
    public sealed record Resumen(int Solidos, int Lineas, int Cilindros)
    {
        public override string ToString() =>
            $"{Solidos} varilla(s) sólidas ({Cilindros} sólido(s))"
            + (Lineas > 0 ? $" y {Lineas} en línea, que no se pudieron hacer sólidas" : string.Empty);
    }

    public const string CapaVarillas = "E-ACERO 3D";

    public const string CapaEstribos = "E-ESTRIBO 3D";

    /// <summary>Crea las capas de la jaula.</summary>
    public void AsegurarCapas()
    {
        foreach (var (capa, color) in new[]
                 {
                     (CapaVarillas, 1),
                     (CapaEstribos, 5)
                 })
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic c = _doc.Layers.Add(capa);
                    c.Color = color;
                });
            }
            catch (Exception)
            {
                // Sin la capa el dibujo sigue saliendo, en la que esté activa.
            }
        }
    }

    /// <summary>Dibuja todas las barras y devuelve la cuenta.</summary>
    public Resumen Dibujar(IEnumerable<Barra> barras)
    {
        _notas.Clear();

        var solidos = 0;
        var lineas = 0;
        var cilindros = 0;
        var cortas = 0;
        var incompletas = 0;

        foreach (var b in barras)
        {
            // Se simplifica ANTES de medir: asi el largo que se reporta es el que se dibuja.
            var eje = EjeDeBarra.Simplificado(b.Eje, GradosDeSimplificacion);

            var largo = EjeDeBarra.Largo(eje);

            if (eje.Count < 2 || largo < LargoMinimo || b.Radio <= 0)
            {
                cortas++;
                continue;
            }

            var (hechos, pedidos) = Solida(b, eje);

            if (hechos > 0)
            {
                solidos++;
                cilindros += hechos;

                if (hechos < pedidos)
                {
                    incompletas++;
                }

                continue;
            }

            // Respaldo: el eje. Mejor una línea donde va la varilla que un hueco.
            if (Eje3D(eje, b.Capa) is not null)
            {
                lineas++;

                _notas.Add(
                    $"La varilla '{b.Id}' no se pudo hacer sólida —largo "
                    + largo.ToString("0.###", CultureInfo.InvariantCulture)
                    + " m— y se dibujó su eje.");
            }
        }

        if (cortas > 0)
        {
            _notas.Add(
                $"{cortas} recorrido(s) no daban para una varilla —sin largo o sin diámetro— y "
                + "no se dibujaron.");
        }

        if (incompletas > 0)
        {
            _notas.Add(
                $"A {incompletas} varilla(s) les falta algún tramo. Revísalas antes de acotar.");
        }

        return new Resumen(solidos, lineas, cilindros);
    }

    /// <summary>
    /// La barra como una fila de cilindros solapados. Devuelve cuántos salieron y cuántos se
    /// pedían.
    /// </summary>
    /// <remarks>
    /// El solape en las uniones lo pone <see cref="EjeDeBarra.Tramos"/> con el <b>radio</b> como
    /// alargue: sin él la parte de fuera de cada doblez queda comida.
    /// </remarks>
    private (int Hechos, int Pedidos) Solida(Barra b, List<(double X, double Y, double Z)> eje)
    {
        var tramos = EjeDeBarra.Tramos(eje, b.Radio);

        var hechos = 0;

        foreach (var (a, z) in tramos)
        {
            if (Cilindro(a, z, b.Radio, b.Capa))
            {
                hechos++;
            }
        }

        return (hechos, tramos.Count);
    }

    /// <summary>Un tramo recto como cilindro. <c>false</c> si no se pudo.</summary>
    /// <remarks>
    /// <para>
    /// <c>AddCylinder</c> lo hace <b>de pie y centrado</b> en el punto que se le pasa, así que se
    /// hace en el origen y se lleva al tramo con la matriz. Hacerlo ya en el punto medio y
    /// transformarlo después no serviría: <c>TransformBy</c> gira respecto al origen del mundo, y
    /// el cilindro se iría de sitio al girarlo.
    /// </para>
    /// <para>
    /// Y la capa se pone <b>después</b> de transformar, no antes, solo por costumbre de este
    /// código: si la transformación falla, no queda una entidad marcada como buena.
    /// </para>
    /// </remarks>
    private bool Cilindro(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        double radio,
        string capa)
    {
        var matriz = EjeDeBarra.MatrizDeTramo(a, b);

        if (matriz is null)
        {
            return false;
        }

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;

        var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (largo < LargoMinimo)
        {
            return false;
        }

        try
        {
            return AcadConnection.Retry(() =>
            {
                dynamic cil = _ms.AddCylinder(new[] { 0d, 0d, 0d }, radio, largo);

                cil.TransformBy(matriz);

                cil.Layer = capa;

                return true;
            });
        }
        catch (Exception)
        {
            // Un tramo perdido deja un hueco en una varilla; tirar el dibujo entero es peor.
            return false;
        }
    }

    /// <summary>El eje como polilínea 3D, que es el respaldo cuando el sólido falla.</summary>
    private object? Eje3D(List<(double X, double Y, double Z)> eje, string capa)
    {
        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.Add3DPoly(EjeDeBarra.Tira(eje));

                pl.Layer = capa;

                if (EjeDeBarra.Cerrado(eje))
                {
                    pl.Closed = true;
                }

                return (object?)pl;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }
}
