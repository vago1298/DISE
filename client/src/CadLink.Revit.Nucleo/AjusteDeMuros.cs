using System.Globalization;

namespace CadLink.Revit.Nucleo;

/// <summary>
/// Las medidas con las que una barra se va a modelar DE VERDAD en Revit.
/// </summary>
/// <remarks>
/// <para>
/// Esto existe porque el ajuste de los muros se estaba haciendo con las medidas de la seccion
/// de ETABS, y en Revit la pieza no se modela con esas medidas: se modela con las del
/// <b>tipo de familia que eligio el usuario</b>, que puede ser otro tamaño. Un castillo de
/// 15x25 en el calculo modelado con un tipo «300 x 450» mide 30x45 en el modelo.
/// </para>
/// <para>
/// Con las medidas del calculo, el muro se recortaba medio castillo de 15 cm y el castillo de
/// verdad medía 30: el muro quedaba metido dentro. Y se bajaba el peralte de la trabe del
/// calculo en vez del de la trabe modelada, asi que no moria en la cara inferior de su cadena.
/// Ese es el motivo por el que los paños no cuadraban aunque el informe dijera que se habian
/// recortado.
/// </para>
/// <para>
/// Devuelve <c>null</c> cuando no se sabe -no hay tipo elegido, o el tipo no dice sus
/// medidas-, y entonces se usan las del calculo, que es lo mejor que hay.
/// </para>
/// </remarks>
/// <param name="barra">La barra de la que se quieren las medidas.</param>
/// <returns>Ancho y peralte en metros, ya en la orientacion en que se va a colocar.</returns>
public delegate (double AnchoM, double PeralteM)? MedidasModeladas(BarraJson barra);

/// <summary>Que ajustes se le hacen a los muros antes de modelarlos.</summary>
public sealed class OpcionesMuro
{
    /// <summary>Bajar la cabeza del muro hasta la cara inferior de la cadena o trabe de arriba.</summary>
    public bool BajarBajoLaCadena { get; set; } = true;

    /// <summary>Recortar el muro en el pano de los castillos, para que no se meta dentro.</summary>
    public bool RecortarEnCastillos { get; set; } = true;

    /// <summary>Cuanto puede diferir una cota para considerarla la misma. 15 cm.</summary>
    public double ToleranciaZM { get; set; } = 0.15;

    /// <summary>A que distancia del eje del muro cuenta un castillo. 30 cm.</summary>
    public double ToleranciaEjeM { get; set; } = 0.30;
}

/// <summary>
/// Recorta los muros para que encajen como en el plano de AutoCAD: por debajo de la cadena
/// de cerramiento y al pano de los castillos, sin meterse dentro.
/// </summary>
/// <remarks>
/// <para>
/// Sin esto, un muro de ETABS va de cota de piso a cota de piso y de eje a eje de columna. Al
/// modelarlo asi en Revit, el muro OCUPA el mismo sitio que la cadena de arriba y que los
/// castillos de las puntas, y Revit avisa: <i>"One element is completely inside another"</i>,
/// una vez por cada solape. Son los 69 avisos que salieron.
/// </para>
/// <para>
/// No es solo cosmetico: un muro que atraviesa su cadena da mal las cantidades, y el modelo no
/// sirve para medir.
/// </para>
/// <para>
/// Es la misma regla que ya aplica el dibujo en AutoCAD -los muros mueren en el <b>pano</b> del
/// castillo y no en su eje-, aqui sobre la geometria del paño.
/// </para>
/// <para>
/// <b>Solo se ajustan los paños RECTANGULARES y verticales</b>, que son los que tienen cabeza
/// y puntas claras. Un paño de forma libre se deja como esta y se dice: recortar a ciegas algo
/// cuya forma no se entiende es peor que no tocarlo.
/// </para>
/// </remarks>
public static class AjusteDeMuros
{
    /// <summary>Lo que se le hizo a un muro.</summary>
    public sealed record Ajuste(
        List<PuntoJson> Contorno, double BajoM, double RecortadoM, string? Nota);

    /// <summary>Un muro rectangular y vertical, descrito por su eje en planta y sus dos cotas.</summary>
    public sealed record MuroRecto(
        double X1, double Y1, double X2, double Y2, double ZBase, double ZAlta);

    /// <summary>
    /// Reconoce un paño rectangular y vertical. <c>null</c> si no lo es.
    /// </summary>
    /// <remarks>
    /// Se exige: cuatro vertices distintos, exactamente dos cotas distintas, y dos posiciones
    /// distintas en planta. Eso es un rectangulo de pared. Cualquier otra cosa -un paño con un
    /// hueco, un triangulo de un hastial, un paño mallado en trozos- no cumple y se deja en paz.
    /// </remarks>
    public static MuroRecto? ComoRecto(IReadOnlyList<PuntoJson>? v, double tolM = 0.02)
    {
        var limpio = Contornos.SinRepetidos(v, tolM);

        if (limpio.Count != 4)
        {
            return null;
        }

        var zs = new List<double>();
        var enPlanta = new List<(double X, double Y)>();

        foreach (var p in limpio)
        {
            if (!zs.Any(z => Math.Abs(z - p.Z) <= tolM))
            {
                zs.Add(p.Z);
            }

            if (!enPlanta.Any(q => Math.Abs(q.X - p.X) <= tolM && Math.Abs(q.Y - p.Y) <= tolM))
            {
                enPlanta.Add((p.X, p.Y));
            }
        }

        if (zs.Count != 2 || enPlanta.Count != 2)
        {
            return null;
        }

        zs.Sort();

        return new MuroRecto(
            enPlanta[0].X, enPlanta[0].Y, enPlanta[1].X, enPlanta[1].Y, zs[0], zs[1]);
    }

    /// <summary>Devuelve el contorno ajustado de un muro.</summary>
    public static Ajuste Ajustar(
        PanoJson muro, ModeloJson modelo, OpcionesMuro? op = null,
        MedidasModeladas? medidas = null)
    {
        op ??= new OpcionesMuro();

        var recto = ComoRecto(muro.Vertices);

        if (recto is null)
        {
            return new Ajuste(muro.Vertices.ToList(), 0, 0,
                // La etiqueta va DELANTE, con la forma «llave»: motivo, para que el informe
                // agrupe estos avisos por su causa. Con la etiqueta metida en medio del texto
                // cada aviso era un motivo distinto y el informe decia "11 piezas por 11
                // motivos" en vez de "5 x no es un rectangulo vertical".
                $"«{muro.Etiqueta}»: no es un rectangulo vertical, asi que no se recorto ni "
                + "bajo; revisa si se mete en su cadena o en los castillos");
        }

        var largo = Distancia(recto.X1, recto.Y1, recto.X2, recto.Y2);

        if (largo < 1e-6)
        {
            return new Ajuste(muro.Vertices.ToList(), 0, 0, null);
        }

        // La direccion del muro en planta, normalizada.
        var ux = (recto.X2 - recto.X1) / largo;
        var uy = (recto.Y2 - recto.Y1) / largo;

        var zAlta = recto.ZAlta;
        var bajo = 0.0;

        if (op.BajarBajoLaCadena)
        {
            var peralte = PeralteDeLoQueVaEncima(recto, ux, uy, modelo, op, medidas);

            if (peralte > 1e-6)
            {
                zAlta -= peralte;
                bajo = peralte;
            }
        }

        // Si la cadena se come el muro entero, no se modela un muro de altura negativa: se
        // deja como estaba y se avisa. Pasa cuando el entrepiso es mas bajo que el peralte de
        // la trabe, que es sintoma de otro problema.
        if (zAlta - recto.ZBase < 0.05)
        {
            return new Ajuste(muro.Vertices.ToList(), 0, 0,
                $"«{muro.Etiqueta}»: quedaria de menos de 5 cm al bajarlo bajo su cadena, asi "
                + "que se dejo con su altura original");
        }

        var recorteA = 0.0;
        var recorteB = 0.0;

        if (op.RecortarEnCastillos)
        {
            recorteA = MedioCastilloEn(recto.X1, recto.Y1, ux, uy, recto, modelo, op, medidas);
            recorteB = MedioCastilloEn(recto.X2, recto.Y2, ux, uy, recto, modelo, op, medidas);
        }

        // Igual que arriba: si los castillos se comen el muro, no se recorta.
        if (largo - recorteA - recorteB < 0.05)
        {
            recorteA = 0;
            recorteB = 0;
        }

        var ax = recto.X1 + (ux * recorteA);
        var ay = recto.Y1 + (uy * recorteA);
        var bx = recto.X2 - (ux * recorteB);
        var by = recto.Y2 - (uy * recorteB);

        var contorno = new List<PuntoJson>
        {
            new() { X = ax, Y = ay, Z = recto.ZBase },
            new() { X = bx, Y = by, Z = recto.ZBase },
            new() { X = bx, Y = by, Z = zAlta },
            new() { X = ax, Y = ay, Z = zAlta }
        };

        return new Ajuste(contorno, bajo, recorteA + recorteB, null);
    }

    /// <summary>
    /// El peralte de la cadena o trabe que corre por encima del muro.
    /// </summary>
    /// <remarks>
    /// Se busca entre las barras de clase trabe cuya cota coincide con la cabeza del muro y que
    /// van en la MISMA direccion y sobre el mismo eje. Se toma el peralte MAYOR de las que
    /// cumplen: si por encima corren una cadena y una trabe, el muro tiene que morir bajo la
    /// mas alta de las dos caras inferiores, o volveria a solapar.
    /// </remarks>
    public static double PeralteDeLoQueVaEncima(
        MuroRecto muro, double ux, double uy, ModeloJson modelo, OpcionesMuro op,
        MedidasModeladas? medidas = null)
    {
        var mayor = 0.0;

        foreach (var b in modelo.Barras)
        {
            if (b.Clase != ClasePieza.Trabe)
            {
                continue;
            }

            // A la cota de la cabeza del muro.
            var zb = Math.Max(b.P1.Z, b.P2.Z);

            if (Math.Abs(zb - muro.ZAlta) > op.ToleranciaZM)
            {
                continue;
            }

            var lb = Distancia(b.P1.X, b.P1.Y, b.P2.X, b.P2.Y);

            if (lb < 1e-6)
            {
                continue;
            }

            var vx = (b.P2.X - b.P1.X) / lb;
            var vy = (b.P2.Y - b.P1.Y) / lb;

            // Paralela al muro: el producto punto en valor absoluto cerca de 1.
            if (Math.Abs((ux * vx) + (uy * vy)) < 0.98)
            {
                continue;
            }

            // Y sobre el mismo eje: las dos puntas de la barra, cerca de la recta del muro.
            if (DistanciaARecta(b.P1.X, b.P1.Y, muro, ux, uy) > op.ToleranciaEjeM
                || DistanciaARecta(b.P2.X, b.P2.Y, muro, ux, uy) > op.ToleranciaEjeM)
            {
                continue;
            }

            // Y que se solapen a lo largo, no que sea otra barra del mismo eje mas alla.
            if (!SeSolapan(muro, ux, uy, b))
            {
                continue;
            }

            // CUANTO CUELGA la trabe por debajo de su linea, que no es su peralte entero: eso
            // depende de su punto de insercion. Con el punto 8 -arriba al centro- cuelga el
            // peralte completo; con el 10 -el centroide, que es el de OMISION de ETABS- cuelga
            // solo la mitad; con el 2 -abajo al centro- no cuelga nada y se apoya encima.
            //
            // Bajar el muro su peralte entero cuando la trabe solo cuelga la mitad dejaba un
            // hueco de medio peralte entre la cabeza del muro y la cara inferior de su cadena.
            // Y con el peralte del calculo en vez del del tipo modelado, el hueco era otro.
            // Cuelga su PERALTE ENTERO, porque asi es como la coloca el modelador. Las dos
            // reglas salen del mismo sitio a proposito: cuando el modelador suponia una cosa y
            // esto otra, el muro moria a media altura de su cadena y quedaba un hueco.
            var cuelga = CadenaBajoElNivel.CuelgaM(Peralte(b, medidas));

            // La cara inferior tiene que quedar por debajo de la cabeza del muro; si la trabe se
            // apoya encima -punto de abajo- no hay nada que bajar.
            var bajarHasta = (zb - cuelga) - muro.ZAlta;

            if (bajarHasta < 0)
            {
                mayor = Math.Max(mayor, -bajarHasta);
            }
        }

        return mayor;
    }

    /// <summary>El peralte con el que se va a modelar la barra.</summary>
    private static double Peralte(BarraJson b, MedidasModeladas? medidas)
    {
        var m = medidas?.Invoke(b);

        return m is not null && m.Value.PeralteM > 0 ? m.Value.PeralteM : b.Seccion.PeralteM;
    }

    /// <summary>
    /// Medio castillo, medido a lo largo del muro, si hay uno en esa punta.
    /// </summary>
    /// <remarks>
    /// El muro de ETABS llega al EJE de la columna; el muro de verdad muere en su pano. Asi que
    /// se recorta la mitad de lo que mide el castillo en la direccion del muro.
    /// </remarks>
    public static double MedioCastilloEn(
        double x, double y, double ux, double uy,
        MuroRecto muro, ModeloJson modelo, OpcionesMuro op, MedidasModeladas? medidas = null)
    {
        var mayor = 0.0;
        var encontrado = false;

        foreach (var c in modelo.Barras)
        {
            if (c.Clase != ClasePieza.Columna)
            {
                continue;
            }

            // La columna tiene que cruzar la altura del muro, no estar en otro piso.
            var zBaja = Math.Min(c.P1.Z, c.P2.Z);
            var zAlta = Math.Max(c.P1.Z, c.P2.Z);

            if (zAlta < muro.ZBase - op.ToleranciaZM || zBaja > muro.ZAlta + op.ToleranciaZM)
            {
                continue;
            }

            // Y estar en esta punta del muro. Se mira en planta, con el eje de la columna.
            var cx = (c.P1.X + c.P2.X) / 2;
            var cy = (c.P1.Y + c.P2.Y) / 2;

            if (Distancia(cx, cy, x, y) > op.ToleranciaEjeM)
            {
                continue;
            }

            encontrado = true;
            mayor = Math.Max(mayor, MedioAncho(c, ux, uy, medidas));
        }

        // Si en esta punta NO hay castillo, el muro se queda como esta y se APUNTA, porque es la
        // diferencia entre "el muro llega al pano" y "el muro llega al eje". Un muro que no
        // encuentra su castillo es la unica forma en que puede seguir metido dentro de el, asi
        // que hay que poder contarlo en el informe en vez de deducirlo del modelo.
        if (!encontrado)
        {
            SinCastillo++;
        }

        return mayor;
    }

    /// <summary>Puntas de muro en las que no se encontro castillo. Para el informe.</summary>
    /// <remarks>
    /// Es un contador global y bastante tosco, pero responde la pregunta que importa: si el muro
    /// no llega al pano del castillo, ¿es porque se recorto mal o porque no se encontro el
    /// castillo? Se pone a cero en cada <see cref="AplicarATodos"/>.
    /// </remarks>
    public static int SinCastillo { get; private set; }

    /// <summary>
    /// La mitad de lo que mide la seccion de la columna en la direccion del muro.
    /// </summary>
    /// <remarks>
    /// Se proyecta la caja de la seccion sobre la direccion del muro, usando los ejes locales
    /// de la columna. Con una columna cuadrada da lo mismo; con una rectangular girada, no, y
    /// es justo el caso en que recortar de mas o de menos se ve.
    /// <para>
    /// Se toma <c>PeralteM</c> sobre el eje local 2 y <c>AnchoM</c> sobre el 3, la misma
    /// convencion que en el resto de este puente. Si las columnas aparecieran giradas 90
    /// grados, es el mismo cabo que esta anotado en MainWindow.Ifc.cs.
    /// </para>
    /// </remarks>
    public static double MedioAncho(
        BarraJson columna, double ux, double uy, MedidasModeladas? medidas = null)
    {
        var t = columna.AnguloGrados * Math.PI / 180.0;
        var c = Math.Cos(t);
        var s = Math.Sin(t);

        // Ejes locales 2 y 3 de una columna en planta, girados por su angulo.
        var e2x = c;
        var e2y = s;
        var e3x = -s;
        var e3y = c;

        // Las medidas con las que el castillo se va a modelar de verdad. Recortar el muro con
        // las del calculo cuando el tipo elegido es de otro tamaño es justo lo que dejaba el
        // muro metido dentro del castillo.
        var real = medidas?.Invoke(columna);

        var dim2 = real is not null && real.Value.PeralteM > 0
            ? real.Value.PeralteM
            : columna.Seccion.PeralteM;

        var dim3 = real is not null && real.Value.AnchoM > 0
            ? real.Value.AnchoM
            : columna.Seccion.AnchoM;

        var proyeccion = (Math.Abs((ux * e2x) + (uy * e2y)) * dim2)
                         + (Math.Abs((ux * e3x) + (uy * e3y)) * dim3);

        return proyeccion / 2.0;
    }

    private static bool SeSolapan(MuroRecto muro, double ux, double uy, BarraJson b)
    {
        // Se proyecta todo sobre la direccion del muro y se comparan intervalos.
        double S(double x, double y) => ((x - muro.X1) * ux) + ((y - muro.Y1) * uy);

        var m1 = 0.0;
        var m2 = S(muro.X2, muro.Y2);

        var b1 = S(b.P1.X, b.P1.Y);
        var b2 = S(b.P2.X, b.P2.Y);

        var mMin = Math.Min(m1, m2);
        var mMax = Math.Max(m1, m2);
        var bMin = Math.Min(b1, b2);
        var bMax = Math.Max(b1, b2);

        // Un solape de verdad, no un toque de puntas.
        return Math.Min(mMax, bMax) - Math.Max(mMin, bMin) > 0.05;
    }

    private static double DistanciaARecta(
        double x, double y, MuroRecto muro, double ux, double uy)
    {
        var dx = x - muro.X1;
        var dy = y - muro.Y1;

        // La componente perpendicular a la direccion del muro.
        return Math.Abs((dx * -uy) + (dy * ux));
    }

    private static double Distancia(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));


    /// <summary>Ajusta TODOS los muros del modelo, en su sitio, y devuelve el resumen.</summary>
    public static List<string> AplicarATodos(
        ModeloJson modelo, OpcionesMuro? op = null, MedidasModeladas? medidas = null)
    {
        op ??= new OpcionesMuro();

        var avisos = new List<string>();
        var bajados = 0;
        var recortados = 0;
        var sinTocar = 0;

        SinCastillo = 0;

        foreach (var pano in modelo.Panos.Where(p => p.Clase == ClasePieza.Muro))
        {
            var a = Ajustar(pano, modelo, op, medidas);

            if (a.Nota is not null)
            {
                sinTocar++;

                // Se guardan unos pocos: con doscientos paños libres, un aviso por cada uno
                // tapa todo lo demas.
                if (avisos.Count < 5)
                {
                    avisos.Add(a.Nota);
                }

                continue;
            }

            pano.Vertices.Clear();
            pano.Vertices.AddRange(a.Contorno);

            if (a.BajoM > 1e-6)
            {
                bajados++;
            }

            if (a.RecortadoM > 1e-6)
            {
                recortados++;
            }
        }

        if (bajados > 0)
        {
            avisos.Insert(0, $"{bajados} muro(s) se bajaron hasta la cara inferior de su "
                             + "cadena o trabe, para que no se metan dentro.");
        }

        if (recortados > 0)
        {
            avisos.Insert(0, $"{recortados} muro(s) se recortaron al pano de sus castillos, "
                             + "como en el plano de AutoCAD.");
        }

        if (sinTocar > 0)
        {
            avisos.Add($"En total {sinTocar} pano(s) no son rectangulos verticales y se "
                       + "dejaron como estaban.");
        }

        if (SinCastillo > 0)
        {
            avisos.Add($"«castillos»: en {SinCastillo} punta(s) de muro no se encontro castillo "
                       + $"a menos de {Cm(op.ToleranciaEjeM)} cm, asi que esa punta se quedo en "
                       + "el eje y no en el pano");
        }

        return avisos;
    }

    // ==================================================================
    //  A PAÑO DE LAS COLUMNAS QUE DE VERDAD ESTAN EN REVIT
    // ==================================================================

    /// <summary>
    /// Lo que ocupa una columna YA MODELADA: los vertices de su solido en planta y entre que
    /// cotas esta. Se mide en Revit, despues de crearla.
    /// </summary>
    public sealed record HuellaColumna(List<(double X, double Y)> Puntos, double ZMin, double ZMax);

    /// <summary>Lo que hizo <see cref="APanoDeColumnas"/> con un muro.</summary>
    /// <param name="Puntas">Cuantas de sus dos puntas quedaron en el paño de una columna.</param>
    public sealed record APano(List<PuntoJson> Contorno, int Puntas, bool Cambio);

    /// <summary>
    /// Lleva cada punta del muro al PAÑO de la columna que tiene ahi, medida en Revit: la recorta
    /// si se mete en la columna y la ALARGA si se queda corta, para que nunca quede separado ni
    /// dentro de su seccion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidio: «algunos muros no los modelas a paño de las columnas; que no queden separados y
    /// que no queden dentro de la seccion de la columna». Recortar con medio castillo calculado
    /// -la seccion, su angulo y el giro del tipo de Revit- fallaba en algunos: con la cuenta
    /// mal, el muro quedaba corto o metido. Medido sobre la columna que Revit dibujo, el paño es
    /// el de verdad.
    /// </para>
    /// <para>
    /// Una columna cuenta en esa punta si cruza la altura del muro, si su huella toca la franja
    /// del eje del muro y si llega a menos de <paramref name="tolEjeM"/> de la punta. Se toma la
    /// cara de la columna que da hacia el muro. Un muro que no es un rectangulo vertical, o que
    /// se quedaria de menos de 5 cm, se deja como esta.
    /// </para>
    /// </remarks>
    public static APano? APanoDeColumnas(
        IReadOnlyList<PuntoJson> vertices, IReadOnlyList<HuellaColumna> columnas,
        double tolZM = 0.15, double tolEjeM = 0.30)
    {
        var recto = ComoRecto(vertices);

        if (recto is null)
        {
            return null;
        }

        var largo = Distancia(recto.X1, recto.Y1, recto.X2, recto.Y2);

        if (largo < 1e-6)
        {
            return null;
        }

        var ux = (recto.X2 - recto.X1) / largo;
        var uy = (recto.Y2 - recto.Y1) / largo;

        // Desde la punta, hacia DENTRO del muro: lo que hay que mover esa punta. Positivo, se
        // recorta; negativo, se alarga. Null si no hay columna en esa punta.
        double? Cara(double ex, double ey, double dx, double dy)
        {
            double? cara = null;

            foreach (var c in columnas)
            {
                if (c.Puntos.Count == 0 || c.ZMax < recto.ZBase + tolZM || c.ZMin > recto.ZAlta - tolZM)
                {
                    continue;
                }

                var s = c.Puntos.Select(p => ((p.X - ex) * dx) + ((p.Y - ey) * dy)).ToList();
                var n = c.Puntos.Select(p => ((p.X - ex) * -dy) + ((p.Y - ey) * dx)).ToList();

                // Que toque el eje del muro y que este en la punta, no a media pared.
                if (n.Min() > 0.05 || n.Max() < -0.05 || s.Min() > tolEjeM || s.Max() < -tolEjeM)
                {
                    continue;
                }

                // La cara que da hacia el muro es la mas metida en el.
                var hacia = s.Max();
                cara = cara is null ? hacia : Math.Max(cara.Value, hacia);
            }

            return cara;
        }

        var a = Cara(recto.X1, recto.Y1, ux, uy);
        var b = Cara(recto.X2, recto.Y2, -ux, -uy);

        var da = a ?? 0;
        var db = b ?? 0;

        if (largo - da - db < 0.05)
        {
            return new APano(vertices.ToList(), 0, false);
        }

        var contorno = new List<PuntoJson>
        {
            new() { X = recto.X1 + (ux * da), Y = recto.Y1 + (uy * da), Z = recto.ZBase },
            new() { X = recto.X2 - (ux * db), Y = recto.Y2 - (uy * db), Z = recto.ZBase },
            new() { X = recto.X2 - (ux * db), Y = recto.Y2 - (uy * db), Z = recto.ZAlta },
            new() { X = recto.X1 + (ux * da), Y = recto.Y1 + (uy * da), Z = recto.ZAlta }
        };

        return new APano(contorno, (a is null ? 0 : 1) + (b is null ? 0 : 1), Math.Abs(da) + Math.Abs(db) > 1e-6);
    }

    /// <summary>Para los mensajes.</summary>
    public static string Cm(double metros) =>
        (metros * 100).ToString("0.#", CultureInfo.InvariantCulture);
}
