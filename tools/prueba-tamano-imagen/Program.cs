using CadLink.Cad;

namespace CadLink.Pruebas;

/// <summary>
/// Prueba de <see cref="TamanoDeImagen"/>: a qué tamaño se guarda el JPG del 3D.
/// </summary>
/// <remarks>
/// El razonamiento de qué se cubre y por qué está en el .csproj. En resumen: que el aspecto
/// sobreviva a los recortes, que los topes se apliquen al tamaño <b>dibujado</b> y no al guardado,
/// y que ningún caso raro devuelva un tamaño imposible.
/// </remarks>
internal static class Program
{
    private static int _fallos;

    // Los mismos valores con los que trabaja la aplicación, para probar el caso de verdad.
    private const int Ancho = 4000;
    private const int Super = 2;
    private const int TopeLado = 8000;
    private const long TopeArea = 44_000_000;

    private static void Comprobar(bool cond, string que, string porque = "")
    {
        if (cond)
        {
            Console.WriteLine($"  OK    {que}");
            return;
        }

        Console.WriteLine($"  FALLA {que}");

        if (porque.Length > 0)
        {
            Console.WriteLine($"        {porque}");
        }

        _fallos++;
    }

    private static TamanoDeImagen.Tamano Calcular(double aspecto) =>
        TamanoDeImagen.Calcular(aspecto, Ancho, Super, TopeLado, TopeArea);

    private static int Main()
    {
        Console.WriteLine("PRUEBA DE TamanoDeImagen");
        Console.WriteLine(new string('=', 70));

        ElAspectoSobrevive();
        ElSupermuestreoSeAplicaAlQueSeDibuja();
        LosTopesSonDelQueSeDibuja();
        LaMemoriaNoSeDispara();
        Degenerados();

        Console.WriteLine(new string('=', 70));

        if (_fallos == 0)
        {
            Console.WriteLine("TODO PASA");
            return 0;
        }

        Console.WriteLine($"FALLAN {_fallos} comprobacion(es)");
        return 1;
    }

    // =================================================================================
    //  LO QUE NO SE NEGOCIA: EL ASPECTO
    // =================================================================================

    private static void ElAspectoSobrevive()
    {
        Console.WriteLine();
        Console.WriteLine("El aspecto que entra es el que sale:");

        // Se barre desde muy apaisado hasta muy alto, incluidos los que fuerzan los dos topes.
        foreach (var aspecto in new[]
                 { 0.2, 0.35, 0.5, 0.64, 0.8, 1.0, 1.25, 1.6, 2.0, 3.0, 5.0 })
        {
            var t = Calcular(aspecto);

            var salida = (double)t.Ancho / t.Alto;

            // La tolerancia se mide EN PIXELES y no en proporcion, y es a proposito: con lados
            // enteros no se puede representar exactamente una proporcion cualquiera, asi que lo
            // que se puede exigir es que el par de enteros no se aleje mas de un pixel del
            // ideal. Eso es medio pixel de desvio por lado, o sea invisible.
            var desvio = Math.Abs(t.Ancho - (t.Alto * aspecto));

            Comprobar(desvio <= 1.5,
                $"aspecto {aspecto}: sale {salida:0.0000} ({t.Ancho}x{t.Alto})",
                $"se desvio {desvio:0.00} pixeles, mas de 1.5");
        }

        // Y el tamaño DIBUJADO tiene el mismo aspecto, porque es el guardado por un entero.
        var q = Calcular(0.64);

        Comprobar(q.AnchoQueSeDibuja == q.Ancho * Super
                  && q.AltoQueSeDibuja == q.Alto * Super,
            "el tamaño dibujado es el guardado por el supermuestreo, exacto");
    }

    // =================================================================================
    //  EL SUPERMUESTREO
    // =================================================================================

    private static void ElSupermuestreoSeAplicaAlQueSeDibuja()
    {
        Console.WriteLine();
        Console.WriteLine("El supermuestreo:");

        // Sin supermuestreo y con un aspecto que no fuerza ningun tope, se guarda el ancho
        // pedido tal cual.
        var sin = TamanoDeImagen.Calcular(1.0, Ancho, 1, TopeLado, TopeArea);

        Comprobar(sin.Ancho == Ancho, $"sin supermuestreo se guarda el ancho pedido: {sin.Ancho}");
        Comprobar(sin.AnchoQueSeDibuja == sin.Ancho,
            "y lo que se dibuja es lo mismo que se guarda");

        // Con supermuestreo 2 y el MISMO aspecto, el ancho guardado tiene que BAJAR si el tope
        // por lado aprieta: 4000 x 2 = 8000, que es justo el tope, asi que aqui no baja.
        var con = TamanoDeImagen.Calcular(1.0, Ancho, 2, TopeLado, TopeArea);

        Comprobar(con.AnchoQueSeDibuja <= TopeLado,
            $"con supermuestreo, lo dibujado no pasa del tope por lado: {con.AnchoQueSeDibuja}");

        // Y con supermuestreo 4 SI tiene que bajar: 4000 x 4 = 16000, el doble del tope.
        var cuatro = TamanoDeImagen.Calcular(1.0, Ancho, 4, TopeLado, TopeArea);

        Comprobar(cuatro.AnchoQueSeDibuja <= TopeLado,
            $"con supermuestreo 4 tampoco: {cuatro.AnchoQueSeDibuja}");
        Comprobar(cuatro.Ancho < con.Ancho,
            "y para conseguirlo se guarda una imagen mas pequeña, que es el precio",
            $"con 2 -> {con.Ancho}, con 4 -> {cuatro.Ancho}");
    }

    // =================================================================================
    //  LOS TOPES SON DEL TAMAÑO QUE SE DIBUJA, NO DEL QUE SE GUARDA
    // =================================================================================
    //  Es LA trampa de esta cuenta. Aplicandolos al tamaño guardado, con supermuestreo 2 se
    //  colaria una superficie de cuatro veces el area permitida.
    private static void LosTopesSonDelQueSeDibuja()
    {
        Console.WriteLine();
        Console.WriteLine("Los topes son del tamaño que se DIBUJA:");

        foreach (var aspecto in new[] { 0.2, 0.4, 0.64, 1.0, 1.5, 2.5, 5.0 })
        {
            var t = Calcular(aspecto);

            Comprobar(t.AnchoQueSeDibuja <= TopeLado && t.AltoQueSeDibuja <= TopeLado,
                $"aspecto {aspecto}: ningun lado dibujado pasa de {TopeLado} " +
                $"({t.AnchoQueSeDibuja}x{t.AltoQueSeDibuja})");

            Comprobar((long)t.AnchoQueSeDibuja * t.AltoQueSeDibuja <= TopeArea,
                $"aspecto {aspecto}: el area dibujada cabe en el tope " +
                $"({(long)t.AnchoQueSeDibuja * t.AltoQueSeDibuja:N0})");
        }

        // EL CASO ALTO Y ESTRECHO, que es el del recuadro de la vista previa: ocupa la mitad
        // del ancho del lienzo y todo el alto, asi que el aspecto ronda 0.64. Es donde el tope
        // por lado aprieta de verdad, y donde la version anterior habria pedido un mapa enorme.
        var previa = Calcular(0.64);

        Comprobar(previa.AltoQueSeDibuja <= TopeLado,
            $"el recuadro alto y estrecho de la vista previa cabe: {previa.AnchoQueSeDibuja}x{previa.AltoQueSeDibuja}");

        // Y sigue siendo una imagen util: mas de 2000 px de ancho, o sea mas de una hoja a
        // 300 ppp. Si esta comprobacion falla, los topes se han apretado de mas.
        Comprobar(previa.Ancho >= 2000,
            $"y sigue guardando una imagen grande: {previa.Ancho}x{previa.Alto}",
            $"solo {previa.Ancho} px de ancho");
    }

    // =================================================================================
    //  LA MEMORIA
    // =================================================================================

    private static void LaMemoriaNoSeDispara()
    {
        Console.WriteLine();
        Console.WriteLine("La memoria:");

        // Cuatro bytes por pixel, y hay dos mapas vivos a la vez: el grande y el reducido.
        foreach (var aspecto in new[] { 0.2, 0.64, 1.0, 2.0, 5.0 })
        {
            var t = Calcular(aspecto);

            var bytes = ((long)t.AnchoQueSeDibuja * t.AltoQueSeDibuja * 4)
                        + ((long)t.Ancho * t.Alto * 4);

            var mb = bytes / (1024.0 * 1024.0);

            Comprobar(mb < 260,
                $"aspecto {aspecto}: los dos mapas ocupan {mb:0} MB",
                $"son {mb:0} MB, demasiado para una exportacion");
        }
    }

    // =================================================================================
    //  LOS DEGENERADOS
    // =================================================================================

    private static void Degenerados()
    {
        Console.WriteLine();
        Console.WriteLine("Los degenerados:");

        // Un aspecto imposible no puede dar una imagen imposible: se cae al cuadrado.
        foreach (var malo in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            var t = Calcular(malo);

            Comprobar(t.Ancho > 0 && t.Alto > 0 && t.AnchoQueSeDibuja > 0 && t.AltoQueSeDibuja > 0,
                $"aspecto {malo} da un tamaño valido: {t.Ancho}x{t.Alto}");
        }

        // Sin topes se respeta el ancho pedido.
        var libre = TamanoDeImagen.Calcular(1.0, 4000, 2, 0, 0);

        Comprobar(libre.Ancho == 4000, "sin topes se guarda el ancho pedido");
        Comprobar(libre.AnchoQueSeDibuja == 8000, "y se dibuja al doble");

        // Un ancho de cero o negativo no puede dar un mapa de cero pixeles.
        Comprobar(Calcular(1.0) is { Ancho: > 0 }, "el caso normal da algo");
        Comprobar(TamanoDeImagen.Calcular(1.0, 0, 2, TopeLado, TopeArea).Ancho >= 1,
            "un ancho pedido de cero sigue dando al menos un pixel");
        Comprobar(TamanoDeImagen.Calcular(1.0, -50, 2, TopeLado, TopeArea).Ancho >= 1,
            "y un ancho negativo tambien");

        // Un supermuestreo de cero o negativo se toma como 1: nunca puede multiplicar por cero,
        // que daria un mapa de ancho cero y una excepcion al crearlo.
        Comprobar(TamanoDeImagen.Calcular(1.0, 1000, 0, TopeLado, TopeArea).AnchoQueSeDibuja == 1000,
            "un supermuestreo de cero se toma como 1");
        Comprobar(TamanoDeImagen.Calcular(1.0, 1000, -3, TopeLado, TopeArea).AnchoQueSeDibuja == 1000,
            "y uno negativo tambien");
    }
}
