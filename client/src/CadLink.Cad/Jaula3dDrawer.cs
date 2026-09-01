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
    /// <para>
    /// <b>Estaba en 0.08 y era lo que dejaba los ganchos con «tanto doblez».</b> El razonamiento
    /// de antes —que cinco centésimas de centímetro no se ven— es correcto para el <i>error de
    /// posición</i> del eje, pero el defecto que se ve en el gancho no es de posición: es que la
    /// superficie está <b>facetada</b>, y lo que delata una faceta no es cuánto se desvía sino el
    /// <b>quiebre</b> entre una cara y la siguiente.
    /// </para>
    /// <para>
    /// Medido con <c>tools/verificar_jaula_3d.py</c>: con 0.08 un estribo pasaba de <b>47 puntos a
    /// 20</b>, así que de los catorce tramos por doblez que la vista previa se había tomado la
    /// molestia de generar, al doblez le quedaban <b>tres o cuatro</b>. Un cuarto de vuelta en
    /// cuatro trozos no es un doblez, es un chaflán.
    /// </para>
    /// <para>
    /// A <b>0.01</b> el doblez conserva sus tramos y se lee como una curva. Cuesta más cilindros y
    /// más uniones —que es lo caro—, pero es la diferencia entre un gancho y un acordeón, y el
    /// gancho es de lo que más se mira en un armado.
    /// </para>
    /// </remarks>
    public const double ToleranciaEnRadios = 0.01;

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

        // ===== Y AHORA UN REGEN, QUE ES LO QUE FALTABA =====
        //
        // FACETRES, ISOLINES y DISPSILH NO CAMBIAN NADA hasta que se regenera: AutoCAD guarda la
        // representación de pantalla de cada sólido y no la vuelve a calcular solo. ZoomExtents
        // tampoco sirve, porque solo mueve la cámara.
        //
        // Sin esto, DISPSILH quedaba puesto pero sin efecto, y los sólidos seguían dibujados con
        // la representación anterior: los que se habían fundido en uno mostraban muy pocas
        // aristas y en la ventana PARECÍA QUE FALTABAN TRAMOS DEL ESTRIBO. La geometría estaba
        // completa; lo que no se había actualizado era el dibujado.
        //
        // Es justo el defecto reportado: se veían el lado derecho y el inferior —los cilindros
        // que habían quedado sueltos— y no el izquierdo ni el superior, que eran los fundidos.
        Regenerar();

        var sombreado = Sombrear();

        // Otro regen después del estilo visual: cambiar de estilo también rehace la
        // representación, y las variables de arriba tienen que volver a aplicarse encima.
        Regenerar();

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
            // El aviso dice AHORA lo que de verdad importa: que puede PARECER que falta acero.
            //
            // El texto anterior decía «se van a ver como líneas» y «están bien dibujadas», y se
            // quedaba corto: en alámbrico 2D un sólido fundido dibuja muy pocas aristas, así que
            // el estribo no se ve fino, se ve INCOMPLETO —faltando lados enteros—. El usuario
            // leía «están bien dibujadas», miraba un estribo al que le faltaba medio perímetro, y
            // con razón concluía que el programa no lo había dibujado.
            _notas.Add(
                "IMPORTANTE: no se pudo poner la vista en sombreado, y en «Estructura alámbrica "
                + "2D» el acero PUEDE PARECER INCOMPLETO —a un estribo se le ven unos lados y "
                + "otros no—. No falta nada: las varillas fundidas en un solo sólido casi no "
                + "dibujan aristas en ese estilo. Para verlo bien, escribe VSCURRENT y elige "
                + "«Conceptual» (o menú Vista › Estilos visuales), y si aún así ves huecos, "
                + "escribe REGEN. Para comprobarlo sin cambiar de estilo, selecciona un estribo: "
                + "se marcará entero.");
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
    /// <summary>Rehace la representación de pantalla de los sólidos, en todas las ventanas.</summary>
    /// <remarks>
    /// Hace falta porque <c>DISPSILH</c>, <c>ISOLINES</c> y <c>FACETRES</c> solo surten efecto tras
    /// un regen: AutoCAD conserva la representación calculada de cada sólido. El <c>1</c> es
    /// <c>acAllViewports</c>; si esta versión no lo acepta se prueba <c>0</c>,
    /// <c>acActiveViewport</c>, y si tampoco, el comando.
    /// </remarks>
    private void Regenerar()
    {
        foreach (var modo in new object[] { 1, 0 })
        {
            try
            {
                AcadConnection.Retry(() => { _doc.Regen(modo); });

                return;
            }
            catch (Exception)
            {
                // Esta versión no acepta ese modo: se prueba el siguiente.
            }
        }

        try
        {
            // Último recurso. El punto salta redefiniciones y el guion bajo fuerza el nombre en
            // inglés, así que vale también en un AutoCAD en español. Y el ENTER es "\r": con
            // "\n" el comando se queda esperando en la línea de órdenes sin ejecutarse.
            _doc.SendCommand("_.REGENALL\r");
        }
        catch (Exception)
        {
            var aviso =
                "No se pudo regenerar la vista. Si algún tramo de acero se ve incompleto, escribe "
                + "REGEN en AutoCAD: la geometría está dibujada, es solo el refresco de pantalla.";

            if (!_notas.Contains(aviso))
            {
                _notas.Add(aviso);
            }
        }
    }

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
        // ===== EL RETORNO DE CARRO. AQUÍ ESTABA EL FALLO =====
        //
        // Estas órdenes iban terminadas en "\n", y AutoCAD espera "\r" —o un espacio— como
        // ENTER en SendCommand. Con "\n" el comando NO SE EJECUTA: se queda a medias en la línea
        // de órdenes esperando que alguien lo termine.
        //
        // Consecuencia exacta de lo que se veía: el estilo visual nunca cambiaba, la ventana se
        // quedaba en alámbrico 2D, y ahí un sólido fundido de diecinueve cilindros dibuja tan
        // pocas aristas que el estribo PARECE que le falta media vuelta. Y con VSEDGES sin
        // aplicar —solo se fija si el sombreado funcionó—, los dobleces se veían facetados, que
        // es lo del gancho «con tanto doblez».
        //
        // Se prueban las dos terminaciones, "\r" primero, porque en algunas versiones "\n"
        // también vale y no cuesta nada dejar las dos.
        foreach (var orden in new[]
                 {
                     "_.VSCURRENT\r_Conceptual\r",
                     "_.VSCURRENT\r_C\r",
                     "_.SHADEMODE\r_Conceptual\r",
                     "_.VSCURRENT\n_Conceptual\n",
                     "_.SHADEMODE\n_Conceptual\n"
                 })
        {
            try
            {
                // EL SendCommand NO VA DENTRO DE Retry, y es importante: Retry reejecuta la
                // lambda, así que un «ocupado» ENCOLABA EL COMANDO OTRA VEZ. Con varias órdenes
                // por doce reintentos se le podían meter a AutoCAD decenas de comandos en la
                // línea, y ahí es donde un VSCURRENT a medias se come el siguiente como si fuera
                // su opción.
                _doc.SendCommand(orden);
            }
            catch (Exception)
            {
                // Ese comando u opción no existe en esta versión. Al siguiente.
                continue;
            }

            // Y SE COMPRUEBA DESPUÉS DE REGENERAR, no en la misma llamada.
            //
            // SendCommand no termina de aplicarse al volver: el estilo visual es una propiedad de
            // la ventana y hasta que AutoCAD no rehace el dibujado, leer VSCURRENT puede devolver
            // todavía el valor viejo. Verificar acto seguido daba un FALSO NEGATIVO: el comando
            // había funcionado, la comprobación decía que no, se probaba el siguiente y al final
            // se avisaba de que no se pudo sombrear cuando sí se había puesto.
            Regenerar();

            if (EstaSombreado())
            {
                return true;
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
    /// <b>Cuidado con la unión que falla.</b> Aquí decía que si una unión falla «no se pierde
    /// nada, los dos sólidos se quedan en el dibujo por separado». <b>Es falso</b>, y era el
    /// origen de que la sección saliera incompleta: <c>Boolean</c> consume el sólido que se le
    /// pasa <b>antes</b> de decidir si puede unirlo, así que una unión fallida <b>borra</b> ese
    /// tramo. Por eso ahora se comprueba si sobrevivió y, si no, se vuelve a dibujar.
    /// </para>
    /// </remarks>
    private (int Hechos, int Pedidos, int Perdidos) Solida(
        Barra b, List<(double X, double Y, double Z)> eje)
    {
        var piezas = Piezas(b, eje);

        object? entero = null;

        var sueltos = 0;
        var perdidos = 0;
        var rescatados = 0;

        for (var i = 0; i < piezas.Count; i++)
        {
            var cil = piezas[i]();

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

            // ===== LA UNIÓN FALLÓ. ¿SIGUE AHÍ EL TRAMO? =====
            //
            // ESTE ERA EL «no dibujas toda la sección». La suposición de todo este archivo era
            // que una unión fallida deja los dos sólidos en el dibujo, y NO es cierta:
            // 'Boolean' consume el sólido que se le pasa ANTES de decidir si puede unirlo, así
            // que cuando falla —lo más común, dos sólidos que por redondeo no llegan a
            // tocarse— 'otro' ya ha desaparecido del dibujo. El tramo no se queda suelto: se
            // BORRA, y sin una sola excepción de por medio.
            //
            // En el estribo eso se ve exactamente como lo reportó el usuario: el lado derecho y
            // el inferior están, y el izquierdo y el superior faltan. No es media sección al
            // azar, son los tramos cuya unión falló.
            //
            // Así que se comprueba si sobrevivió y, si no, SE VUELVE A CREAR. La pieza se sabe
            // rehacer sola —por eso Piezas() devuelve funciones y no objetos— y el tramo nuevo
            // se deja suelto, sin intentar unirlo otra vez: reintentar la unión que acaba de
            // fallar solo volvería a consumirlo.
            if (Vive(cil))
            {
                // Sobrevivió: se queda suelto. Un objeto más y una costura a la vista, pero la
                // varilla no pierde ni un tramo.
                sueltos++;

                continue;
            }

            var repuesto = piezas[i]();

            if (repuesto is null)
            {
                perdidos++;

                continue;
            }

            sueltos++;
            rescatados++;
        }

        if (rescatados > 0)
        {
            _notas.Add(
                $"La varilla '{b.Id}': {rescatados} tramo(s) se volvieron a dibujar porque la "
                + "unión de sólidos se los había llevado. Quedan como piezas aparte, así que la "
                + "varilla se ve con costuras pero está completa.");
        }

        return (entero is null ? 0 : sueltos, piezas.Count, perdidos);
    }

    /// <summary>¿La entidad sigue viva en el dibujo, o ya se la llevó una unión?</summary>
    /// <remarks>
    /// Se pregunta por una propiedad cualquiera. Si el objeto COM ya fue consumido, leerla lanza,
    /// y eso es la respuesta. No hay una forma más limpia de saberlo: AutoCAD no expone un
    /// «¿sigues ahí?», y por eso este archivo llevaba tanto tiempo dando por hecho que sí.
    /// </remarks>
    private static bool Vive(object ent)
    {
        try
        {
            _ = ((dynamic)ent).ObjectName;

            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
    /// <remarks>
    /// <para>
    /// <b>SE LLAMA UNA SOLA VEZ, SIN REINTENTO, Y ESO NO ES NEGOCIABLE.</b>
    /// </para>
    /// <para>
    /// Aquí había un <c>AcadConnection.Retry</c> envolviendo el <c>Boolean</c>, y era el que
    /// <b>cerraba AutoCAD a media jaula</b>. El motivo está escrito tres párrafos más arriba, en
    /// <see cref="Solida"/>: <i>la unión CONSUME el sólido que se le pasa</i>. Y <c>Retry</c> no
    /// reintenta la llamada: <b>reejecuta la lambda entera</b>. Así que cuando AutoCAD contestaba
    /// «ocupado» —<c>0x8001010A</c>, que es de lo más común mientras se están creando cientos de
    /// sólidos— el reintento volvía a llamar a <c>Boolean</c> pasándole un objeto COM
    /// <b>ya consumido</b>. Es exactamente la operación que el propio archivo describe como la que
    /// «cierra AutoCAD sin dar la cara»: sin excepción, sin error y sin nada que capturar.
    /// </para>
    /// <para>
    /// Y el síntoma es justo el reportado: AutoCAD se va a media corrida y en el dibujo queda
    /// <b>media jaula</b>. No es un dibujo incompleto, es un programa cerrado.
    /// </para>
    /// <para>
    /// No se puede arreglar reintentando con cuidado, porque <b>no hay forma de preguntar</b> si el
    /// <c>Boolean</c> llegó a surtir efecto: si surtió, <c>otro</c> ya no existe y consultarlo es
    /// el mismo crash. La única vía segura es intentarlo <b>una vez</b>.
    /// </para>
    /// <para>
    /// Y no basta con no reintentar: <c>Boolean</c> <b>consume el sólido antes de decidir si puede
    /// unirlo</b>, así que cuando devuelve <c>false</c> el tramo ya <b>no está en el dibujo</b>.
    /// Quien llama tiene que comprobarlo y volver a crearlo. Ver <see cref="Solida"/>.
    /// </para>
    /// </remarks>
    private static bool Fundir(object cuerpo, object otro)
    {
        try
        {
            ((dynamic)cuerpo).Boolean(0, (dynamic)otro);

            return true;
        }
        catch (Exception)
        {
            // Una unión puede fallar si los dos sólidos no llegan a tocarse por redondeo, o
            // porque AutoCAD estaba ocupado. No es motivo para tirar la varilla: se queda en dos
            // piezas, las dos dibujadas.
            //
            // 'otro' NO se borra aquí. Si el Boolean alcanzó a consumirlo, borrarlo es el crash
            // que se acaba de quitar; y si no lo consumió, sigue dibujado y en su sitio, que es
            // lo que se quiere.
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
            // EL AddCylinder Y EL TransformBy VAN EN REINTENTOS SEPARADOS.
            //
            // Juntos en uno solo, un «ocupado» después de crear el cilindro hacía que el
            // reintento creara OTRO, y el primero se quedaba en el dibujo: un cilindro de pie,
            // sin transformar, plantado en el origen del mundo. Con cientos de tramos por jaula
            // eso deja un ramillete de sólidos sueltos en el 0,0,0 que no son ninguna varilla,
            // se seleccionan al hacer marco y falsean el ZoomExtents.
            //
            // TransformBy tampoco es idempotente —aplicarla dos veces transforma dos veces— así
            // que necesita su propio reintento por el mismo motivo que el Boolean de Fundir.
            dynamic cil = AcadConnection.Retry<object>(() =>
                _ms.AddCylinder(new[] { 0d, 0d, 0d }, radio, largo));

            try
            {
                // SIN REINTENTO, y sola. TransformBy acumula: reintentarla después de que haya
                // surtido efecto transformaría el cilindro dos veces y lo mandaría lejos de la
                // varilla. Es el mismo motivo por el que el Boolean de Fundir tampoco se
                // reintenta.
                cil.TransformBy(matriz);
            }
            catch (Exception)
            {
                // Sin transformar, el cilindro está en el origen y no representa nada. Se borra:
                // aquí SÍ es seguro, porque a diferencia del Boolean de Fundir nadie lo ha
                // consumido todavía.
                try
                {
                    AcadConnection.Retry(() => { cil.Delete(); });
                }
                catch (Exception)
                {
                    // Si tampoco se puede borrar, no se insiste.
                }

                return null;
            }

            try
            {
                AcadConnection.Retry(() => { cil.Layer = capa; });
            }
            catch (Exception)
            {
                // La capa es lo de menos: el sólido ya está en su sitio y con su forma.
            }

            return (object?)cil;
        }
        catch (Exception)
        {
            // Un tramo perdido deja un hueco en una varilla; tirar el dibujo entero es peor.
            return null;
        }
    }


    /// <summary>El doblez a la antigua: cilindros por sus puntos. Es el respaldo del toro.</summary>
    /// <remarks>
    /// Lleva el <b>mismo rescate</b> que <see cref="Solida"/>, y por el mismo motivo: una unión
    /// fallida se lleva el cilindro, y aquí eso deja el <b>doblez mordido</b> —una esquina del
    /// estribo a medias— que es justo uno de los defectos que se veían en el dibujo.
    /// </remarks>
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

            if (Fundir(entero, cil) || Vive(cil))
            {
                continue;
            }

            // La unión se lo llevó: se rehace y se queda suelto.
            Cilindro(a, z, b.Radio, b.Capa);
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
