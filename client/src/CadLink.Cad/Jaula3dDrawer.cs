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

    /// <summary>
    /// Cuánto se deja que la varilla se separe de su eje al simplificarlo, <b>en fracción de su
    /// propio radio</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se mide contra el radio de la varilla y no en centímetros porque así la tolerancia
    /// significa lo mismo en un del ocho que en un del tres: <b>una veinticincoava parte del
    /// grueso</b> es invisible en las dos. Una cifra fija en centímetros sería holgada para la
    /// varilla gruesa y brutal para la delgada.
    /// </para>
    /// <para>
    /// Y es lo que hace que <b>los ganchos salgan redondos</b>. No porque el gancho reciba más
    /// tramos por ser cerrado —al revés: cuanto más cerrado el doblez, menos tramos hacen falta
    /// para no separarse de la tolerancia— sino porque la tolerancia pasa a medir <b>lo que se
    /// ve</b>. Con la regla de grados que había antes, el error que quedaba dependía del radio del
    /// doblez: los mismos veinte grados dejaban un error invisible en un doblez cerrado y una
    /// arista clara en uno abierto. Por distancia el error es el mismo en todos, y se elige tan
    /// pequeño que en ninguno se note.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>Por qué un doceavo y no menos.</b> En una varilla del cuatro son cinco centésimas de
    /// centímetro: la veinticincoava parte de su grueso, que no se ve ni con la nariz pegada. Bajar
    /// más no mejora nada de lo que se mira y sí cuesta: cada tramo de más es un cilindro más y una
    /// unión más, y las uniones booleanas de AutoCAD son lo caro de esta operación.
    /// </remarks>
    public const double ToleranciaEnRadios = 0.08;

    /// <summary>
    /// Tolerancia para <b>reconocer</b> rectas y arcos en el eje, en fracción del radio de la
    /// varilla.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reconocer no es simplificar, y por eso son dos números.</b> Simplificar admite holgura:
    /// se está eligiendo cuánto error visible se acepta, y el 8% del radio es invisible. Reconocer
    /// no: se está preguntando «¿estos puntos venían de un arco o de una recta?», y la respuesta es
    /// exacta porque los puntos vienen de un arco muestreado sin error.
    /// </para>
    /// <para>
    /// Usar la tolerancia holgada para las dos cosas salía mal, y de una forma que solo se vio al
    /// probarlo: con holgura, el lado recto de un estribo <b>se tragaba el primer punto del doblez
    /// siguiente</b> —a esa distancia, un punto del arco todavía cae dentro de la holgura de la
    /// recta—, y entonces el doblez arrancaba tarde, con un radio y un barrido que no eran los
    /// suyos. El estribo salía en siete arcos en lugar de cuatro. Con tolerancia estrecha, cada
    /// pieza empieza donde de verdad empieza.
    /// </para>
    /// </remarks>
    public const double ToleranciaDeReconocer = 0.005;

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
            $"{Solidos} varilla(s) sólidas en {Cilindros} sólido(s)"
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

    /// <summary>
    /// Pone el dibujo en condiciones de <b>ver</b> los sólidos: sombreado y curvas finas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hace falta porque un dibujo de AutoCAD normal está en <b>«Estructura alámbrica 2D»</b>, y en
    /// ese estilo un sólido <b>no se ve como un sólido</b>: se ven cuatro líneas por cilindro. Las
    /// varillas salían bien y parecían alambres. No es un problema del dibujo, es el estilo visual
    /// de la ventana, y por eso se cambia aquí en lugar de tocar la geometría.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>VSCURRENT</b> a <c>Conceptual</c>: caras llenas y sombreadas. Es lo que hace que se
    ///     vean varillas y no alambres.
    ///   </item>
    ///   <item>
    ///     <b>FACETRES</b> al máximo, 10. Es la finura con la que AutoCAD tesela una superficie
    ///     curva al sombrearla, y con el 0,5 de omisión una varilla del tres —cinco milímetros de
    ///     radio— sale con forma de tuerca. Esto es la mitad de que los ganchos se vean redondos;
    ///     la otra mitad es afinar el doblez, y eso se hace con la tolerancia del eje.
    ///   </item>
    ///   <item>
    ///     <b>ISOLINES</b> a 16, para que también en alámbrico se lea que son tubos.
    ///   </item>
    ///   <item>
    ///     <b>DISPSILH</b> a 1: dibuja la silueta del sólido, que es lo que le da el contorno
    ///     limpio al imprimir.
    ///   </item>
    /// </list>
    /// <para>
    /// Cada una va en su propio intento. Son <b>preferencias de vista</b>, no el dibujo: si una
    /// falla —una versión que no la tenga, un estilo visual con otro nombre— el armado ya está
    /// puesto y correcto, y el usuario puede cambiar el estilo a mano. No hay motivo para que esto
    /// tire la operación.
    /// </para>
    /// </remarks>
    public void PrepararLaVista()
    {
        // Las numéricas primero, que son las que no dan problema de nombre.
        foreach (var (nombre, valor) in new (string, object)[]
                 {
                     ("FACETRES", 10d),
                     ("ISOLINES", 16),
                     ("DISPSILH", 1)
                 })
        {
            try
            {
                AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
            }
            catch (Exception)
            {
                // Son finura de teselado y siluetas: sin ellas se ve, solo más basto.
            }
        }

        var sombreado = Sombrear();

        // ===== Y AHORA SE APAGAN LAS ARISTAS. AQUÍ ESTÁ LO DE LOS GANCHOS =====
        //
        // Un doblez hecho de cilindros rectos tiene una arista de verdad en cada junta, y el
        // sombreado las DIBUJA: el gancho salía con un abanico de rayas. Más tramos no lo
        // arreglan, lo empeoran, porque son más rayas.
        //
        // El doblez curvo de verdad -girar el perfil- sería la solución buena y cierra AutoCAD,
        // así que se resuelve por el otro lado: se le dice a la ventana que NO dibuje aristas. Las
        // facetas siguen ahí en la geometría, pero no se ven, y lo que se ve es una varilla que
        // dobla lisa. La silueta sí se mantiene, que es la que le da el contorno a la pieza.
        //
        // Va DESPUÉS de poner el estilo visual, y no antes: al cambiar de estilo se cargan los
        // ajustes del estilo nuevo y se llevarían esto por delante.
        if (sombreado)
        {
            foreach (var (nombre, valor) in new (string, object)[]
                     {
                         ("VSEDGES", 0),
                         ("VSSILHEDGES", 1),
                         ("VSOBSCUREDEDGES", 0),
                         ("VSINTERSECTIONEDGES", 0)
                     })
            {
                try
                {
                    AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
                }
                catch (Exception)
                {
                    // Si una no está en esta versión, se ven algunas aristas de más. Feo, no roto.
                }
            }
        }

        if (!sombreado)
        {
            _notas.Add(
                "No se pudo poner la vista en sombreado, así que las varillas se van a ver como "
                + "líneas en lugar de como tubos llenos. Están bien dibujadas: es el estilo "
                + "visual de la ventana. Escribe VSCURRENT y elige «Conceptual», o usa el menú "
                + "Vista › Estilos visuales.");
        }
    }

    /// <summary>
    /// Pone la ventana en un estilo visual <b>sombreado</b>. <c>false</c> si ninguno entró.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Se prueban varios nombres y se comprueba el resultado.</b> Y las dos cosas hacen falta.
    /// Los nombres de los estilos visuales <b>están traducidos</b> en AutoCAD, así que en una
    /// instalación en español «Conceptual» puede no existir y llamarse «Sombreado»; se prueba una
    /// lista hasta que una entre.
    /// </para>
    /// <para>
    /// Y se <b>vuelve a leer</b> la variable en lugar de dar por bueno que <c>SetVariable</c>
    /// funcionó, porque con un nombre de estilo que no existe AutoCAD <b>no siempre lanza</b>: se
    /// queda como estaba y no dice nada. Ese silencio es justo lo que dejaba al usuario mirando un
    /// alámbrico y pensando que no se habían dibujado sólidos.
    /// </para>
    /// </remarks>
    private bool Sombrear()
    {
        // 1) POR VARIABLE. Es la vía limpia: sin comandos, sin depender del idioma. Se prueban
        //    varios nombres porque están traducidos, y se COMPRUEBA leyendo de vuelta, porque con
        //    un nombre que no existe AutoCAD se queda como estaba sin lanzar nada.
        foreach (var estilo in new[] { "Conceptual", "Realistic", "Sombreado", "Realista" })
        {
            try
            {
                var puesto = AcadConnection.Retry(() =>
                {
                    _doc.SetVariable("VSCURRENT", estilo);

                    return EstaSombreado();
                });

                if (puesto)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ese nombre no existe aquí, o la variable no se deja escribir. Al siguiente.
            }
        }

        // 2) POR COMANDO. Y hace falta: en AutoCAD 2026, VSCURRENT por SetVariable NO cambia la
        //    ventana —se comprobó, el usuario tenía que ponerlo a mano— porque el estilo visual es
        //    una propiedad de la ventana y no del dibujo. El comando sí entra.
        //
        //    El punto y el guion bajo delante del nombre no son adorno: el punto salta cualquier
        //    redefinición del comando y el guion bajo fuerza el nombre en inglés, así que esto
        //    funciona igual en un AutoCAD en español. Y las opciones van con guion bajo por lo
        //    mismo.
        foreach (var orden in new[]
                 {
                     "_.VSCURRENT\n_Conceptual\n",
                     "_.VSCURRENT\n_C\n",
                     "_.SHADEMODE\n_Conceptual\n"
                 })
        {
            try
            {
                var puesto = AcadConnection.Retry(() =>
                {
                    _doc.SendCommand(orden);

                    return EstaSombreado();
                });

                if (puesto)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ese comando u opción no existe en esta versión. Al siguiente.
            }
        }

        return false;
    }

    /// <summary>¿La ventana está en un estilo <b>que rellena las caras</b>?</summary>
    /// <remarks>
    /// <para>
    /// Se pregunta por lo que se quiere conseguir —caras llenas— y no por un nombre concreto, que
    /// es lo que permite dar por bueno cualquiera de los estilos sombreados y no solo el que se
    /// pidió. Los dos que <b>no</b> valen son los alámbricos, que son justo los que dejaban las
    /// varillas pareciendo líneas.
    /// </para>
    /// <para>
    /// Y si no se puede leer la variable, se contesta <c>false</c>: es mejor intentar de más y como
    /// último recurso avisar al usuario, que dar por puesto algo que no está.
    /// </para>
    /// </remarks>
    private bool EstaSombreado()
    {
        try
        {
            var ahora = ((string)_doc.GetVariable("VSCURRENT") ?? string.Empty).Trim();

            if (ahora.Length == 0)
            {
                return false;
            }

            foreach (var alambre in new[] { "2dwireframe", "wireframe", "alámbrico", "alambrico" })
            {
                if (ahora.Contains(alambre, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Dibuja todas las barras y devuelve la cuenta.</summary>
    /// <remarks>
    /// <b>Las notas no se vacían aquí.</b> Se usa un dibujante nuevo por exportación, así que
    /// empiezan vacías de todos modos, y vaciarlas se llevaría por delante lo que hubiera avisado
    /// <see cref="PrepararLaVista"/>, que es justo lo que el usuario necesita leer si su AutoCAD no
    /// aceptó el estilo sombreado.
    /// </remarks>
    public Resumen Dibujar(IEnumerable<Barra> barras)
    {
        var solidos = 0;
        var lineas = 0;
        var cilindros = 0;
        var cortas = 0;
        var incompletas = 0;

        foreach (var b in barras)
        {
            // Se simplifica ANTES de medir: asi el largo que se reporta es el que se dibuja. Y la
            // tolerancia va contra el radio de ESTA varilla, que es lo que hace que el gancho de
            // una del tres se afine igual de fino que el de una del ocho.
            var eje = EjeDeBarra.Simplificado(b.Eje, b.Radio * ToleranciaEnRadios);

            var largo = EjeDeBarra.Largo(eje);

            if (eje.Count < 2 || largo < LargoMinimo || b.Radio <= 0)
            {
                cortas++;
                continue;
            }

            var (hechos, pedidos, perdidos) = Solida(b, eje);

            if (hechos > 0)
            {
                solidos++;
                cilindros += hechos;

                if (perdidos > 0)
                {
                    incompletas++;

                    // Con el nombre y la cuenta, no solo «alguna falta»: es la diferencia entre
                    // poder buscarla en el dibujo y no saber ni dónde mirar.
                    _notas.Add(
                        $"A la varilla '{b.Id}' le faltan {perdidos} de {pedidos} tramo(s): va a "
                        + "aparecer con un hueco.");
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
                $"En total, {incompletas} varilla(s) quedaron con huecos. Revísalas antes de "
                + "acotar.");
        }

        return new Resumen(solidos, lineas, cilindros);
    }

    /// <summary>
    /// La barra como <b>un solo sólido</b>: una fila de cilindros solapados, fundidos en uno.
    /// Devuelve cuántos sólidos quedaron y cuántos tramos se pedían.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El solape en las uniones lo pone <see cref="EjeDeBarra.Tramos"/> con el <b>radio</b> como
    /// alargue: sin él la parte de fuera de cada doblez queda comida.
    /// </para>
    ///
    /// <para><b>Y LUEGO SE FUNDEN, QUE ES LO QUE HACE QUE SE VEA BIEN</b></para>
    /// <para>
    /// Cilindros solapados sueltos, en sombreado, <b>no se ven como una varilla</b>: se ven como
    /// una ristra de salchichas, porque en cada solape AutoCAD dibuja la línea donde un sólido
    /// entra en el otro. Y afinar el doblez lo empeora, porque son más costuras. Así que los
    /// cilindros de una misma barra se unen con <c>Boolean</c> en uno solo: las caras de dentro
    /// desaparecen y queda <b>una varilla continua</b>, que es lo que se quería.
    /// </para>
    /// <para>
    /// De paso arregla lo otro que molestaba: el dibujo pasa de setecientos objetos a <b>uno por
    /// varilla</b>. Se puede seleccionar una varilla entera de un clic, y medirla y acotarla como
    /// una pieza.
    /// </para>
    /// <para>
    /// <b>La unión consume el sólido que se le pasa</b> —igual que <c>AddExtrudedSolid</c> consume
    /// su región— así que después de unirlo no se vuelve a tocar. Ni se borra: borrar un objeto
    /// COM ya consumido es justo lo que cierra AutoCAD sin dar la cara.
    /// </para>
    /// <para>
    /// Y si una unión falla, <b>no se pierde nada</b>: los dos sólidos se quedan en el dibujo por
    /// separado. Se ve la costura, pero la varilla está y mide lo que tiene que medir.
    /// </para>
    /// </remarks>
    private (int Hechos, int Pedidos, int Perdidos) Solida(
        Barra b, List<(double X, double Y, double Z)> eje)
    {
        var piezas = Piezas(b, eje);

        object? entero = null;

        var sueltos = 0;
        var perdidos = 0;

        foreach (var pieza in piezas)
        {
            var cil = pieza();

            if (cil is null)
            {
                // Esta pieza no salió: la varilla va a tener un hueco justo aquí.
                perdidos++;

                continue;
            }

            if (entero is null)
            {
                entero = cil;
                sueltos = 1;

                continue;
            }

            if (Fundir(entero, cil))
            {
                continue;
            }

            // No se fundió: se queda suelto en el dibujo. Un objeto más y una costura a la
            // vista, pero la varilla no pierde ni un tramo.
            sueltos++;
        }

        return (entero is null ? 0 : sueltos, piezas.Count, perdidos);
    }

    /// <summary>
    /// Las piezas de una barra, cada una lista para crearse: <b>cilindros</b> para los tramos
    /// rectos y <b>toros</b> para los dobleces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se devuelven como funciones sin ejecutar para que <see cref="Solida"/> pueda contarlas,
    /// crearlas y fundirlas en un solo recorrido, sin repetir aquí la lógica de la unión.
    /// </para>
    /// <para>
    /// <b>El solape en las juntas se mantiene</b>, y ahora también en los arcos: un arco se estira
    /// el ángulo que corresponde a un radio de varilla de arco —<c>radio / radioDelDoblez</c>— por
    /// cada punta que dé a otra pieza. Dos superficies que se tocan justo en el borde son el peor
    /// caso para una unión booleana, porque las caras quedan coincidentes; solapadas, la unión es
    /// trivial.
    /// </para>
    /// <para>
    /// Y si el eje no trae arcos reconocibles —una varilla recta, por ejemplo— esto se comporta
    /// exactamente como antes: sale una lista de cilindros.
    /// </para>
    /// </remarks>
    private List<Func<object?>> Piezas(Barra b, List<(double X, double Y, double Z)> eje)
    {
        var trozos = EjeDeBarra.Curvas(eje, b.Radio * ToleranciaDeReconocer);

        var cerrado = EjeDeBarra.Cerrado(eje);

        var piezas = new List<Func<object?>>();

        for (var i = 0; i < trozos.Count; i++)
        {
            var t = trozos[i];

            // Se estira hacia el lado donde haya otra pieza, y no en las puntas libres de la
            // varilla: alargar esas la haria mas larga que la de la tabla.
            var atras = i > 0 || cerrado;
            var delante = i < trozos.Count - 1 || cerrado;

            if (!t.EsArco)
            {
                var tramos = EjeDeBarra.Tramos(
                    new List<(double X, double Y, double Z)> { t.A, t.B },
                    0);

                if (tramos.Count == 0)
                {
                    continue;
                }

                var (a, z) = tramos[0];

                var largo = Math.Sqrt(
                    ((z.X - a.X) * (z.X - a.X))
                    + ((z.Y - a.Y) * (z.Y - a.Y))
                    + ((z.Z - a.Z) * (z.Z - a.Z)));

                if (largo < LargoMinimo)
                {
                    continue;
                }

                var ux = (z.X - a.X) / largo;
                var uy = (z.Y - a.Y) / largo;
                var uz = (z.Z - a.Z) / largo;

                var da = atras ? b.Radio : 0;
                var dd = delante ? b.Radio : 0;

                var p1 = (a.X - (ux * da), a.Y - (uy * da), a.Z - (uz * da));
                var p2 = (z.X + (ux * dd), z.Y + (uy * dd), z.Z + (uz * dd));

                piezas.Add(() => Cilindro(p1, p2, b.Radio, b.Capa));

                continue;
            }

            // UN DOBLEZ: cadena de cilindros por sus propios puntos.
            //
            // AQUÍ IBA UN TORO Y SE QUITÓ. Girar el perfil con AddRevolvedSolid daba el doblez
            // perfecto, de una sola superficie curva y sin una arista dentro. Pero CIERRA AUTOCAD
            // 2026, igual que AddExtrudedSolidAlongPath, y por lo mismo: sin excepción, sin error
            // y sin nada que capturar. Se probó y se cerró.
            //
            // Se intentó a ciegas, que fue el error: es la SEGUNDA API de sólidos con perfil y
            // región que mata AutoCAD en este entorno, y no hay forma de comprobarlo aquí porque
            // el camino COM no se ejecuta. La conclusión, ya con dos casos, es que en esta versión
            // NO se toca ninguna operación que consuma una región. Solo AddCylinder y Boolean, que
            // están probados por el usuario.
            //
            // El precio es que el doblez queda facetado, y las aristas de las facetas se ven en
            // sombreado. Eso NO se arregla con más tramos —serían más aristas— sino apagando el
            // dibujo de aristas de la ventana, que es lo que hace PrepararLaVista con VSEDGES.
            var trozo = t;

            piezas.Add(() => CadenaDeCilindros(trozo, b));
        }

        return piezas;
    }

    /// <summary>Menos que esto no es nada.</summary>
    private const double Nada = 1e-12;

    /// <summary>Funde <paramref name="otro"/> dentro de <paramref name="cuerpo"/>.</summary>
    /// <remarks>
    /// <c>acUnion</c> es el cero del enumerado de operaciones booleanas de AutoCAD. Se pasa el
    /// número y no el nombre porque aquí se habla con AutoCAD por <c>dynamic</c>, sin la biblioteca
    /// de tipos delante.
    /// </remarks>
    private static bool Fundir(object cuerpo, object otro)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                ((dynamic)cuerpo).Boolean(0, (dynamic)otro);

                return true;
            });
        }
        catch (Exception)
        {
            // Una unión puede fallar si los dos sólidos no llegan a tocarse por redondeo. No es
            // motivo para tirar la varilla: se queda en dos piezas.
            return false;
        }
    }

    /// <summary>Un tramo recto como cilindro. <c>null</c> si no se pudo.</summary>
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
    private object? Cilindro(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        double radio,
        string capa)
    {
        var matriz = EjeDeBarra.MatrizDeTramo(a, b);

        if (matriz is null)
        {
            return null;
        }

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;

        var largo = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (largo < LargoMinimo)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic cil = _ms.AddCylinder(new[] { 0d, 0d, 0d }, radio, largo);

                cil.TransformBy(matriz);

                cil.Layer = capa;

                return (object?)cil;
            });
        }
        catch (Exception)
        {
            // Un tramo perdido deja un hueco en una varilla; tirar el dibujo entero es peor.
            return null;
        }
    }


    /// <summary>El doblez a la antigua: cilindros por sus puntos. Es el respaldo del toro.</summary>
    private object? CadenaDeCilindros(EjeDeBarra.Trozo t, Barra b)
    {
        object? entero = null;

        foreach (var (a, z) in EjeDeBarra.Tramos(t.Puntos, b.Radio))
        {
            var cil = Cilindro(a, z, b.Radio, b.Capa);

            if (cil is null)
            {
                continue;
            }

            if (entero is null)
            {
                entero = cil;

                continue;
            }

            Fundir(entero, cil);
        }

        return entero;
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
