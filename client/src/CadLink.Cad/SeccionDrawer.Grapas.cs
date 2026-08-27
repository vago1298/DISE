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

        // Los contornos que hay que rellenar en el tipo 2. Se juntan y se rellenan de
        // una vez al final: RellenoDelGancho manda todos sus achurados al fondo en una
        // sola pasada.
        var paraRellenar = new List<double[]>();

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

            var dGrapa = g.Var.Cm * _escala;

            // El gancho de la sección da el largo de las colas. Sin gancho capturado se
            // usan seis diámetros, el mínimo de norma para un doblez sísmico. Es la
            // misma regla que la vista previa.
            var cola = s.GanchoCm > 0 ? s.GanchoCm * _escala : dGrapa * 6;

            var puntos = TrazoGrapa.Contorno(
                va.Value.X, va.Value.Y, va.Value.R,
                vb.Value.X, vb.Value.Y, vb.Value.R,
                dGrapa, cola);

            if (puntos is null || puntos.Count < 3)
            {
                continue;
            }

            var plano = Aplanar(puntos);

            Agregar(contorno, PolyCerrada(plano));
            paraRellenar.Add(plano);
        }

        if (conFondoSolido && paraRellenar.Count > 0)
        {
            // Sin sectores: el contorno de la grapa ya trae sus dobleces muestreados en
            // tramos rectos, así que no hay ningún arco que rellenar por separado.
            RellenoDelGancho(paraRellenar, new List<double[]>());
        }
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
