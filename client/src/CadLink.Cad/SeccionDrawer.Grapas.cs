namespace CadLink.Cad;

/// <summary>
/// Las <b>grapas</b> en AutoCAD: los estribos suplementarios que unen dos varillas
/// longitudinales.
/// </summary>
/// <remarks>
/// <para>
/// La geometría no se calcula aquí: sale de <see cref="TrazoGrapa.Contorno"/>, que es la
/// misma clase que usa la vista previa en pantalla. Es la razón de que esa clase exista y
/// de que no dependa de WPF. Calcular el trazo por segunda vez aquí sería la manera de
/// acabar dibujando en el plano una grapa distinta de la que el usuario colocó mirando la
/// pantalla.
/// </para>
/// <para>
/// Lo que <b>sí</b> se resuelve aquí es <i>dónde</i> están las varillas. Y no se puede
/// reaprovechar el trabajo de la vista previa: las dos capas reparten las varillas por su
/// cuenta —está razonado en <c>PosicionesDeLecho</c>— y aquí todo va en unidades de
/// dibujo, o sea centímetros por la escala, y desde la esquina de la sección y no desde
/// el origen. Ver <see cref="TodasLasVarillasCad"/>.
/// </para>
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>
    /// <b>Todas</b> las varillas longitudinales, con la señal que las identifica.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el gemelo de <c>TodasLasVarillas</c> de la aplicación, y <b>el orden de los
    /// cinco grupos y el índice dentro de cada uno tienen que ser idénticos</b>: una
    /// grapa se guarda como «la tercera del lecho inferior», así que si las dos tablas
    /// numeraran distinto, la grapa se vería en pantalla agarrada de una varilla y en el
    /// plano de otra, sin ningún error que lo delatara.
    /// </para>
    /// <para>
    /// <b>No se puede armar con <c>_varSup</c> / <c>_varInf</c>.</b> Esas listas las
    /// llena <see cref="Lecho"/> mezclando las de esquina con las intermedias en una
    /// sola secuencia, así que de ahí ya no hay forma de recuperar de qué grupo era cada
    /// una. Por eso se vuelve a llamar a <see cref="PosicionesDeLecho"/> y
    /// <see cref="PosicionesLaterales"/>, que son justo las que dijeron dónde poner los
    /// círculos que se dibujaron: la grapa cae sobre la varilla que de verdad está en el
    /// plano.
    /// </para>
    /// </remarks>
    private List<(RefVarilla Ref, double X, double Y, double R)> TodasLasVarillasCad(
        SeccionCad s, double x0, double y0, double b, double h,
        double rec, double dEst, double dSup, double dInf)
    {
        var salida = new List<(RefVarilla Ref, double X, double Y, double R)>();

        var pSup = PosicionesDeLecho(s.Superior, x0, y0, b, h, rec, dEst, arriba: true);
        var pInf = PosicionesDeLecho(s.Inferior, x0, y0, b, h, rec, dEst, arriba: false);

        void Agrupar(LechoVarilla lecho, double[] xs, double y, double radio)
        {
            for (var i = 0; i < xs.Length; i++)
            {
                salida.Add((new RefVarilla(lecho, i), xs[i], y, radio));
            }
        }

        // Los cuatro lechos, en el MISMO orden que la tabla de la aplicación.
        Agrupar(LechoVarilla.EsquinaSuperior, pSup.Esquina, pSup.YEsquina,
                s.Superior.Esquina.Cm * _escala / 2);

        Agrupar(LechoVarilla.IntermediaSuperior, pSup.Intermedia, pSup.YIntermedia,
                s.Superior.Intermedia.Cm * _escala / 2);

        Agrupar(LechoVarilla.EsquinaInferior, pInf.Esquina, pInf.YEsquina,
                s.Inferior.Esquina.Cm * _escala / 2);

        Agrupar(LechoVarilla.IntermediaInferior, pInf.Intermedia, pInf.YIntermedia,
                s.Inferior.Intermedia.Cm * _escala / 2);

        // Las laterales: izquierda y luego derecha por cada altura, que es exactamente
        // el orden en que las anota Laterales() y el mismo que usa la vista previa.
        var rLat = s.Lateral.Cm * _escala / 2;
        var indice = 0;

        foreach (var (xIzq, xDer, y) in
                 PosicionesLaterales(s, x0, y0, b, h, rec, dEst, dSup, dInf))
        {
            salida.Add((new RefVarilla(LechoVarilla.Lateral, indice++), xIzq, y, rLat));
            salida.Add((new RefVarilla(LechoVarilla.Lateral, indice++), xDer, y, rLat));
        }

        return salida;
    }

    /// <summary>Busca una varilla por su señal, o <c>null</c> si ya no existe.</summary>
    /// <remarks>
    /// Devolver <c>null</c> es parte del diseño: si el lecho se quedó con menos varillas
    /// de las que había cuando se puso la grapa, esa grapa ya no señala a nada y se
    /// salta, en lugar de agarrarse de otra varilla y dibujar algo que nadie pidió.
    /// </remarks>
    private static (double X, double Y, double R)? BuscarVarillaCad(
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

    /// <summary>Dibuja las grapas de la sección.</summary>
    /// <remarks>
    /// <para>
    /// Cada grapa es <b>una polilínea cerrada</b> en la capa <c>ESTRIBOS</c>, que es
    /// donde va el estribo y su gancho: una grapa es un estribo. Se agrega a
    /// <paramref name="contorno"/> para que reciba el contorno negro del tipo 2 igual que
    /// el resto del estribo, y para que <c>EstribosAlFrente</c> la suba al frente, que
    /// filtra justo por esa capa.
    /// </para>
    /// <para>
    /// <b>Por qué no se registra como isla del achurado.</b> Las islas de la parte de
    /// dentro se recortan contra la cara interior del estribo, y una grapa se sale de
    /// ahí: sus dobleces envuelven varillas que están pegadas a esa cara, así que el
    /// borde exterior asoma un diámetro de grapa por fuera. AutoCAD rechaza un lazo
    /// interior que no esté contenido —<c>0x80200003</c>— y es exactamente el motivo por
    /// el que el estribo diamante tampoco se registra, razonado en
    /// <c>HatchDeConcreto</c>. En su lugar el relleno se pone <i>encima</i> del achurado,
    /// que es lo que hace <c>RellenoDelGancho</c> con <c>AlFondo</c>.
    /// </para>
    /// <para>
    /// Se llama <b>después</b> de las varillas y antes del diamante, igual que en la
    /// vista previa: como <c>AlFrente</c> sube los círculos al final, la varilla acaba
    /// tapando la parte del doblez que le pasa por detrás, que es como está armado.
    /// </para>
    /// </remarks>
    private void GrapasDeLaSeccion(
        SeccionCad s, List<object> contorno,
        double x0, double y0, double b, double h,
        double rec, double dEst, double dSup, double dInf, bool conFondoSolido)
    {
        if (s.Grapas.Count == 0)
        {
            return;
        }

        var varillas = TodasLasVarillasCad(s, x0, y0, b, h, rec, dEst, dSup, dInf);

        // Los contornos de las grapas dibujadas. Se juntan porque hacen falta DOS veces
        // al final: para rellenarlas en el tipo 2 —RellenoDelGancho manda todos sus
        // achurados al fondo en una sola pasada— y para recortar el estribo por donde le
        // pasan por encima.
        var contornos = new List<double[]>();

        // Se resuelven primero y se ordenan con la MISMA regla que la vista previa:
        // TrazoGrapa.ClaveDeOrden, la más larga debajo y la más corta encima, y en el
        // empate la horizontal encima. Ordenar igual es lo que hace que la pantalla no
        // mienta sobre cuál pasa por delante.
        var resueltas = new List<((double X, double Y, double R) A,
                                  (double X, double Y, double R) B,
                                  double DGrapa)>();

        foreach (var g in s.Grapas)
        {
            // Un diámetro que no se reconoció se salta. La aplicación ya lo resolvió a
            // centímetros, así que si llega en cero es que la captura estaba mal, y
            // dibujar la grapa con un calibre inventado es el error de la macro de VBA
            // que este programa existe para no repetir.
            if (!g.Var.Existe)
            {
                continue;
            }

            var va = BuscarVarillaCad(varillas, g.A);
            var vb = BuscarVarillaCad(varillas, g.B);

            if (va is null || vb is null)
            {
                continue;
            }

            resueltas.Add((va.Value, vb.Value, g.Var.Cm * _escala));
        }

        resueltas.Sort((p, q) =>
        {
            var kp = TrazoGrapa.ClaveDeOrden(p.A.X, p.A.Y, p.B.X, p.B.Y);
            var kq = TrazoGrapa.ClaveDeOrden(q.A.X, q.A.Y, q.B.X, q.B.Y);

            var porLargo = kp.Primero.CompareTo(kq.Primero);

            return porLargo != 0 ? porLargo : kp.Segundo.CompareTo(kq.Segundo);
        });

        foreach (var (va, vb, dGrapa) in resueltas)
        {
            // El gancho de la sección da el largo de las colas. Sin gancho capturado se
            // usan seis diámetros, el mínimo de norma para un doblez sísmico. Es la
            // misma regla que la vista previa.
            var cola = s.GanchoCm > 0 ? s.GanchoCm * _escala : dGrapa * 6;

            var puntos = TrazoGrapa.Contorno(
                va.X, va.Y, va.R,
                vb.X, vb.Y, vb.R,
                dGrapa, cola);

            if (puntos is null || puntos.Count < 3)
            {
                continue;
            }

            var plano = Aplanar(puntos);

            Agregar(contorno, PolyCerrada(plano));
            contornos.Add(plano);
        }

        if (conFondoSolido && contornos.Count > 0)
        {
            // Sin sectores: el contorno de la grapa ya trae sus dobleces muestreados en
            // tramos rectos, así que no hay ningún arco que rellenar por separado.
            RellenoDelGancho(contornos, new List<double[]>());
        }

        // Y AL FINAL, con las grapas ya dibujadas: se abre el estribo por donde le pasan
        // por encima. Al final por lo mismo que el recorte del diamante está al final de
        // EstriboDiamante: si se recortara antes y luego fallara el dibujo de la grapa,
        // el estribo se quedaría con un hueco sin nada que lo justifique.
        RecortarEstriboBajoGrapas(contorno, contornos);
    }

    /// <summary>
    /// Abre el estribo por donde la grapa le pasa <b>por encima</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mismo trato que recibe el estribo bajo el diamante, y por el mismo motivo:
    /// una grapa se coloca por fuera del estribo, así que debe verse pasar por delante.
    /// Sin recortar, la línea del estribo cruza la grapa y el dibujo se lee al revés, con
    /// el estribo por encima.
    /// </para>
    /// <para>
    /// <b>Y no basta con el orden de dibujo.</b> Al final de la sección,
    /// <c>EstribosAlFrente</c> sube al frente TODO lo que está en la capa
    /// <c>ESTRIBOS</c> —la grapa incluida, pero también las líneas del estribo—, así que
    /// quien acabe encima depende del orden interno del bloque y no de lo que uno quiera.
    /// En el tipo 1, además, la grapa no lleva relleno que pudiera tapar nada. El recorte
    /// es la única manera de que el efecto sea firme en los dos estilos.
    /// </para>
    /// <para>
    /// El contorno de la grapa ya viene <b>poligonizado</b> desde
    /// <see cref="TrazoGrapa.Contorno"/> —sus dobleces son tramos rectos—, así que se
    /// puede reusar <c>DentroDelPoligono</c> tal cual, sin tener que añadir el término de
    /// los arcos que sí necesita el recorte del diamante.
    /// </para>
    /// <para>
    /// Los trozos que sobreviven se vuelven a anotar con <see cref="Tramo"/>, o sea que
    /// entran otra vez en <c>_tramosEstribo</c>. Es imprescindible: el diamante recorta
    /// DESPUÉS, y si los trozos nuevos no estuvieran en la lista, el diamante cruzaría
    /// sobre ellos sin recortarlos y el defecto volvería, pero solo en las secciones que
    /// llevan grapa y diamante a la vez.
    /// </para>
    /// </remarks>
    private void RecortarEstriboBajoGrapas(
        List<object> contorno, List<double[]> contornosDeGrapa)
    {
        if (contornosDeGrapa.Count == 0)
        {
            return;
        }

        // Se recorre una copia: la lista se modifica dentro del bucle, tanto al quitar
        // el tramo original como al anotar los trozos nuevos.
        foreach (var tramo in _tramosEstribo.ToList())
        {
            var largo = tramo.B - tramo.A;

            if (largo <= LargoMinTramo)
            {
                continue;
            }

            // Lo que tapan TODAS las grapas juntas, unido.
            var brutos = new List<(double Ini, double Fin)>();

            foreach (var poligono in contornosDeGrapa)
            {
                foreach (var trozo in DentroDelPoligono(tramo, poligono))
                {
                    if (trozo.Fin - trozo.Ini > LargoMinTramo)
                    {
                        brutos.Add(trozo);
                    }
                }
            }

            if (brutos.Count == 0)
            {
                continue;
            }

            var tapado = UnirIntervalos(brutos);
            var suma = tapado.Sum(i => i.Fin - i.Ini);

            // El mismo seguro que el recorte del diamante: una grapa tapa un trozo
            // corto del estribo, así que si la cuenta dice que tapa medio tramo es que
            // algo está mal, y es mejor un dibujo con la línea cruzada que un estribo
            // borrado de lado a lado.
            if (suma > FraccionMaxRecorte * largo)
            {
                Nota(
                    "Grapas: no se recortó un tramo del estribo porque el hueco " +
                    $"calculado tapaba el {100 * suma / largo:0} % del tramo. El dibujo " +
                    "queda completo, con la línea del estribo cruzando la grapa.");
                continue;
            }

            // Lo que queda del tramo: los huecos en negativo.
            var trozos = new List<(double A, double B)>();
            var cursor = tramo.A;

            foreach (var (ini, fin) in tapado)
            {
                if (ini > cursor)
                {
                    trozos.Add((cursor, ini));
                }

                cursor = Math.Max(cursor, fin);
            }

            if (cursor < tramo.B)
            {
                trozos.Add((cursor, tramo.B));
            }

            // PRIMERO se dibujan los trozos nuevos, y solo si alguno se creó se borra el
            // original: al revés, un fallo al dibujar dejaría el estribo abierto.
            var nuevos = new List<object>();

            foreach (var (a, bb) in trozos)
            {
                if (bb - a < LargoMinTramo)
                {
                    continue;
                }

                var linea = tramo.Horizontal
                    ? Linea(a, tramo.Fijo, bb, tramo.Fijo, "ESTRIBOS")
                    : Linea(tramo.Fijo, a, tramo.Fijo, bb, "ESTRIBOS");

                if (linea is not null)
                {
                    nuevos.Add(linea);

                    // Se anota como tramo recortable, para que el diamante pueda
                    // recortarlo después. Ver el remark de este método.
                    Tramo(contorno, linea, tramo.Horizontal, tramo.Fijo, a, bb);
                }
            }

            if (nuevos.Count == 0)
            {
                Nota(
                    "Grapas: no se pudo redibujar un tramo recortado del estribo, así " +
                    "que se dejó el tramo entero.");
                continue;
            }

            Borrar(tramo.Ent);
            contorno.Remove(tramo.Ent);
            _tramosEstribo.Remove(tramo);
        }
    }

    /// <summary>Une intervalos que se solapan, y los devuelve ordenados.</summary>
    /// <remarks>
    /// Hace falta porque los dobleces de una grapa y su tramo recto se tocan, y porque
    /// dos grapas pueden cruzar el mismo tramo del estribo. Sin unir, el complemento
    /// saldría con trozos de largo negativo.
    /// </remarks>
    private static List<(double Ini, double Fin)> UnirIntervalos(
        List<(double Ini, double Fin)> brutos)
    {
        brutos.Sort((p, q) => p.Ini.CompareTo(q.Ini));

        var union = new List<(double Ini, double Fin)> { brutos[0] };

        for (var i = 1; i < brutos.Count; i++)
        {
            var ultimo = union[^1];

            if (brutos[i].Ini <= ultimo.Fin)
            {
                if (brutos[i].Fin > ultimo.Fin)
                {
                    union[^1] = (ultimo.Ini, brutos[i].Fin);
                }
            }
            else
            {
                union.Add(brutos[i]);
            }
        }

        return union;
    }

    /// <summary>
    /// Pasa una lista de puntos al arreglo plano <c>x1,y1,x2,y2…</c> que pide AutoCAD.
    /// </summary>
    private static double[] Aplanar(List<(double X, double Y)> puntos)
    {
        var plano = new double[puntos.Count * 2];

        for (var i = 0; i < puntos.Count; i++)
        {
            plano[i * 2] = puntos[i].X;
            plano[(i * 2) + 1] = puntos[i].Y;
        }

        return plano;
    }

    /// <summary>
    /// Las grapas agrupadas <b>por diámetro</b>, para el rótulo.
    /// </summary>
    /// <remarks>
    /// Solo cuenta las que de verdad se van a dibujar: las que tienen diámetro
    /// reconocido. Una grapa que se salta al dibujar no debe aparecer en el rótulo, o el
    /// plano diría que hay cuatro grapas donde se ven tres.
    /// </remarks>
    private static List<(string Clave, int Cuantas)> GrapasPorDiametro(SeccionCad s)
    {
        var cuenta = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in s.Grapas)
        {
            if (!g.Var.Existe || string.IsNullOrWhiteSpace(g.Var.Clave))
            {
                continue;
            }

            cuenta[g.Var.Clave] = cuenta.TryGetValue(g.Var.Clave, out var n) ? n + 1 : 1;
        }

        // Del más grueso al más delgado, igual que las llamadas de varilla del rótulo.
        return cuenta
            .OrderByDescending(par => NumeroDeVarilla(par.Key))
            .Select(par => (par.Key, par.Value))
            .ToList();
    }

    /// <summary>El número de una clave de varilla, para poder ordenarlas por calibre.</summary>
    private static double NumeroDeVarilla(string clave)
    {
        var limpio = (clave ?? string.Empty).Trim().TrimStart('#');

        return double.TryParse(limpio, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }
}
