using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadLink.Cad.PlanoEstructural;

/// <summary>
/// El <b>castillo modelado como shell de muro</b>: se dibuja como un castillo, no como muro.
/// </summary>
/// <remarks>
/// <para>
/// En ETABS un castillo se puede modelar de dos maneras, y las dos son legítimas: como
/// <b>frame</b> —una barra con su sección de 15×15— o como un <b>shell de muro</b> muy
/// angosto, que es lo que sale cuando el castillo se dibuja junto con el muro del que forma
/// parte. El modelo lo distingue con las <b>notas de la propiedad</b>: ahí dice CASTILLO.
/// </para>
/// <para>
/// Dibujados como muro salían como <b>dos líneas</b> —el muro se dibuja por sus dos paños, sin
/// bloque y sin relleno—, así que en el plano un castillo de shell y uno de frame se veían
/// distintos <b>siendo la misma cosa</b>: uno amarillo y en un bloque que un
/// <c>BLOCKREPLACE</c> cambia por el detalle bueno, y el otro un par de rayas.
/// </para>
/// <para>
/// Aquí el shell se convierte en un elemento de clase <b>columna</b>, y a partir de ahí lo
/// dibuja el mismo camino que un castillo de frame: bloque con el nombre de la sección,
/// relleno <c>SOLID</c> amarillo dentro del bloque, capa <c>E-CASTILLO</c>, rótulo en su
/// esquina, y —esto también cuenta— pasa a ser un <b>apoyo</b>, así que los muros mueren en su
/// paño, el contorno de la losa lo respeta y el eje de orilla se corre a su paño.
/// </para>
/// <para>
/// <b>Solo si dice CASTILLO.</b> Es la única condición, y es a propósito: no se mira el largo
/// ni el espesor. Un shell angosto puede ser un pedazo de muro entre dos vanos, y convertirlo
/// «porque mide 15 cm» sería inventar un castillo que el ingeniero no puso. Si en las notas
/// dice CASTILLO, es un castillo; si no dice nada, es un muro.
/// </para>
/// </remarks>
public static class CastilloDeMuro
{
    /// <summary>La palabra que lo declara, y el tipo que sale de ella.</summary>
    public const string Palabra = "CASTILLO";

    /// <summary>Un largo por debajo de esto no es un elemento: es un shell degenerado.</summary>
    private const double Nada = 1e-9;

    /// <summary>
    /// ¿Este muro es en realidad un <b>castillo</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pregunta por el <see cref="ElementoPlanta.Tipo"/> —que la ventana ya clasificó con
    /// <c>ClasificaTipo</c>, y en un muro ese tipo <b>solo</b> puede salir de las notas— y, de
    /// respaldo, por las <b>notas</b> tal cual, para cuando el elemento llega de otro sitio y
    /// el tipo viene en blanco.
    /// </para>
    /// <para>
    /// El <b>nombre de la sección no se mira</b>: se pidió que fuera la property note, y una
    /// propiedad de muro llamada «MURO CASTILLO 15» es un muro con castillos, no un castillo.
    /// </para>
    /// </remarks>
    public static bool Dice(ElementoPlanta? el)
    {
        if (el is null || el.Clase != ClasePlanta.Muro)
        {
            return false;
        }

        return DicenLasNotas(el.Tipo, el.Notas);
    }

    /// <summary>
    /// ¿El tipo o las notas dicen <b>CASTILLO</b>?
    /// </summary>
    /// <remarks>
    /// Está aparte para que lo pueda preguntar también la <b>ventana</b>: la casilla que decide
    /// si un elemento se dibuja va por clase, y un shell que es un castillo tiene que seguir a
    /// la casilla de las <b>columnas</b>, no a la de los muros. Si no, quien apaga los muros
    /// para ver solo la estructura de castillos los perdería todos.
    /// </remarks>
    public static bool DicenLasNotas(string? tipo, string? notas)
    {
        if (string.Equals(tipo?.Trim(), Palabra, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (notas ?? string.Empty)
            .ToUpperInvariant()
            .Contains(Palabra, StringComparison.Ordinal);
    }

    /// <summary>
    /// El mismo elemento, pero ya como <b>castillo</b>: una sección en su centro, girada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El shell en planta es un <b>segmento con espesor</b>, y un castillo es una
    /// <b>sección en un punto</b>. La traducción es la de <c>PanoDeApoyo.Huella</c>, que ya se
    /// usa para medir paños, y aquí sirve para dibujar: el centro de la sección es el
    /// <b>punto medio</b> del segmento, su ancho es el <b>largo</b> del shell, su peralte es
    /// el <b>espesor</b> del muro y su giro es la <b>dirección</b> del segmento. Así un
    /// castillo de 15×40 modelado de lado sale de lado, como en el modelo.
    /// </para>
    /// <para>
    /// La forma se fija en <c>RECT</c>: la del shell es <c>AREA</c>, que no describe ninguna
    /// sección, y un castillo es un rectángulo. Lo demás —etiqueta, sección, notas, cotas— se
    /// copia tal cual, porque es lo que se rotula y lo que el corte necesita para saber de
    /// dónde a dónde va.
    /// </para>
    /// <para>
    /// Se devuelve un elemento <b>nuevo</b>: el que llega no se toca, para que la planta que
    /// el visor o la tabla tengan en la mano siga diciendo lo que el modelo dice.
    /// </para>
    /// </remarks>
    /// <param name="muro">El shell, tal como llegó del modelo.</param>
    /// <param name="espesorPorOmision">
    /// Espesor en metros para cuando el modelo no dio el del muro. Es el mismo respaldo que
    /// usa el dibujante para un muro cualquiera, y sin él el castillo quedaría de peralte cero
    /// —o sea, sin dibujar—.
    /// </param>
    public static ElementoPlanta Como(
        ElementoPlanta muro, double espesorPorOmision, string prefijo = "K")
    {
        var dx = muro.X2 - muro.X1;
        var dy = muro.Y2 - muro.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        var espesor = muro.AnchoM > Nada ? muro.AnchoM : espesorPorOmision;

        // Un shell de un solo punto no tiene dirección ni largo: se le da el espesor en las
        // dos, que es un castillo cuadrado, en lugar de dejarlo sin dibujar.
        var b = largo > Nada ? largo : espesor;

        var cx = (muro.X1 + muro.X2) / 2;
        var cy = (muro.Y1 + muro.Y2) / 2;

        return new ElementoPlanta
        {
            Clase = ClasePlanta.Columna,
            Tipo = Palabra,
            Forma = "RECT",

            // DE ÁREA: lo usan su nombre —que es su medida— y el rótulo de la cadena, que no se
            // escribe encima de él.
            DeShell = true,

            // DE SHELL: es lo que hace que el bloque lleve las medidas en el nombre. La
            // sección de un shell es la propiedad del MURO —solo fija el espesor— y el largo
            // lo pone cada castillo, así que con el nombre de la sección a secas todos se
            // insertaban con las medidas del primero y salían incompletos.

            // ==========================================================================
            //  SU NOMBRE ES SU MEDIDA: «K 15X23.5»
            // ==========================================================================
            //  Se pidió así, y es lo único que sirve. La sección de un shell es la propiedad
            //  del MURO —«MURO 15», que no dice nada de este castillo— y su etiqueta es el
            //  PIER, que en SAP2000 no existe: el castillo salía sin rótulo y con el nombre de
            //  un muro. Ahora se nombra con lo que de verdad lo describe, su medida en planta:
            //  el ESPESOR por el LARGO, en centímetros, con decimales solo si hacen falta
            //  —un castillo de 23.5 cm es 23.5, no 24—.
            //
            //  Y va en la SECCIÓN además de en la etiqueta porque de ahí sale el nombre del
            //  BLOQUE y el rótulo de la planta: así el bloque se llama «K 15X23.5», cada
            //  medida distinta tiene el suyo, y un BLOCKREPLACE cambia de golpe todos los
            //  castillos de esa medida por el detalle armado.
            Etiqueta = Nombre(prefijo, espesor, b),
            Seccion = Nombre(prefijo, espesor, b),
            Notas = muro.Notas,
            Material = muro.Material,

            // El PIER no se copia: es el rótulo del muro, y en un castillo lo que se rotula es
            // su etiqueta y su sección, igual que en uno de frame.
            X1 = cx,
            Y1 = cy,
            X2 = cx,
            Y2 = cy,

            // Las cotas SÍ, tal cual: en el corte el castillo va de su desplante a su
            // cerramiento, y esa altura es la del shell.
            Z1 = muro.Z1,
            Z2 = muro.Z2,

            AnchoM = b,
            PeralteM = espesor,
            AnguloGrados = largo > Nada ? Math.Atan2(dy, dx) * 180 / Math.PI : 0
        };
    }

    /// <summary>
    /// Cambia <b>en la lista</b> los shells que dicen CASTILLO por su castillo, y dice cuántos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se hace <b>antes de dibujar nada</b>, y por un motivo que se ve en el plano: los apoyos
    /// y las huellas se calculan al principio, así que si la conversión llegara después, los
    /// muros y las cadenas morirían en el <b>eje</b> de este castillo en lugar de en su paño, y
    /// el contorno de la losa se metería por dentro de él.
    /// </para>
    /// <para>
    /// Es <b>idempotente</b>: al segundo paso ya no queda ningún muro que decir CASTILLO, así
    /// que dibujar dos veces la misma planta no duplica ni desplaza nada.
    /// </para>
    /// </remarks>
    /// <returns>Cuántos castillos quedaron, para la bitácora.</returns>
    public static int Normalizar(
        IList<ElementoPlanta>? elementos, double espesorPorOmision, double tolUnirM = 0.02,
        string prefijo = "K", double tolPanoM = 0)
    {
        if (elementos is null)
        {
            return 0;
        }

        // 1) Los shells que dicen CASTILLO, con su sitio en la lista.
        var cuales = new List<int>();

        for (var i = 0; i < elementos.Count; i++)
        {
            if (Dice(elementos[i]))
            {
                cuales.Add(i);
            }
        }

        if (cuales.Count == 0)
        {
            return 0;
        }

        // 2) Los pedazos del MISMO castillo, juntos.
        var grupos = new List<List<int>>();

        foreach (var i in cuales)
        {
            var suyo = grupos.FirstOrDefault(
                g => g.Any(j => MismoCastillo(elementos[j], elementos[i], tolUnirM)));

            if (suyo is null)
            {
                grupos.Add(new List<int> { i });
            }
            else
            {
                suyo.Add(i);
            }
        }

        // 3) Cada grupo, UN castillo completo en el sitio del primero. Los demás se quitan:
        //    si se quedaran, saldrían dos bloques encima del otro.
        var sobran = new List<int>();

        foreach (var g in grupos)
        {
            var piezas = g.Select(j => elementos[j]).ToList();

            var unido = Unido(piezas);

            // Y HASTA EL PAÑO del muro con el que se cruza, antes de convertirlo: la medida
            // que se dibuja y la que da nombre al bloque tienen que ser la misma.
            if (tolPanoM > 0)
            {
                unido = AlPanoDeLosMuros(unido, elementos, espesorPorOmision, tolPanoM);
            }

            elementos[g[0]] = Como(unido, espesorPorOmision, prefijo);
            sobran.AddRange(g.Skip(1));
        }

        sobran.Sort();

        for (var k = sobran.Count - 1; k >= 0; k--)
        {
            elementos.RemoveAt(sobran[k]);
        }

        return grupos.Count;
    }

    /// <summary>
    /// ¿Estos dos shells son <b>pedazos del mismo castillo</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un castillo de shell casi nunca llega de una pieza, y las dos maneras en que se parte
    /// se ven mal en el plano:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Partido a lo alto</b> —lo más común—: el modelador lo dibuja en dos paneles, uno
    ///     hasta el antepecho y otro del dintel arriba. En planta los dos ocupan
    ///     <b>exactamente el mismo sitio</b>, así que salían <b>dos bloques encimados</b>, y en
    ///     el corte, dos castillos cortos en lugar de uno de piso a techo.
    ///   </item>
    ///   <item>
    ///     <b>Partido a lo largo</b>: dos paneles seguidos sobre la misma línea. Cada uno daba
    ///     su bloque, así que el castillo salía en dos mitades en vez de uno completo.
    ///   </item>
    /// </list>
    /// <para>
    /// Son el mismo si van en la <b>misma dirección</b>, están en la <b>misma línea</b> —la
    /// separación perpendicular no llega a la tolerancia— y se <b>tocan o se enciman</b> a lo
    /// largo de ella. Con eso, dos castillos distintos separados 15 cm no se unen, y las dos
    /// mitades de uno sí.
    /// </para>
    /// </remarks>
    public static bool MismoCastillo(ElementoPlanta a, ElementoPlanta b, double tol)
    {
        var (ax, ay, largoA) = Direccion(a);
        var (bx, by, largoB) = Direccion(b);

        // PARALELOS: el seno del ángulo que forman las dos direcciones. Se admiten unos
        // grados porque un shell dibujado a mano nunca queda exacto.
        if (largoA > Nada && largoB > Nada
            && Math.Abs((ax * by) - (ay * bx)) > 0.10)
        {
            return false;
        }

        // La dirección de trabajo: la del que la tenga. Dos puntos sueltos se comparan por
        // distancia, y para eso cualquier dirección sirve.
        var ux = largoA > Nada ? ax : largoB > Nada ? bx : 1;
        var uy = largoA > Nada ? ay : largoB > Nada ? by : 0;

        var (ox, oy) = Centro(a);
        var (px, py) = Centro(b);

        // EN LA MISMA LÍNEA: lo que separa a los dos centros medido de través.
        if (Math.Abs(((py - oy) * ux) - ((px - ox) * uy)) > tol)
        {
            return false;
        }

        // Y QUE SE TOQUEN: sus dos tramos, proyectados sobre la línea, se enciman o les falta
        // menos que la tolerancia para juntarse.
        var (a1, a2) = Tramo(a, ux, uy, ox, oy);
        var (b1, b2) = Tramo(b, ux, uy, ox, oy);

        return Math.Min(a2, b2) >= Math.Max(a1, b1) - tol;
    }

    /// <summary>
    /// Los pedazos de un castillo, <b>en uno solo</b>: el shell completo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El largo es de punta a punta —el más lejano de todos los extremos, medido sobre la
    /// línea— y la dirección la pone la <b>pieza más larga</b>, que es la que mejor la define:
    /// tomando la primera, un pedacito de 5 cm dibujado torcido torcería el castillo entero.
    /// </para>
    /// <para>
    /// El espesor es el <b>mayor</b> de los pedazos —el paño tiene que llegar al más
    /// saliente— y las cotas van del <b>más bajo al más alto</b>, que es lo que hace que en el
    /// corte el castillo partido en antepecho y dintel salga de una pieza, de su desplante a
    /// su cerramiento.
    /// </para>
    /// </remarks>
    public static ElementoPlanta Unido(IReadOnlyList<ElementoPlanta> piezas)
    {
        if (piezas.Count == 1)
        {
            return piezas[0];
        }

        var guia = piezas.OrderByDescending(Largo).First();

        var (ux, uy, largo) = Direccion(guia);

        if (largo <= Nada)
        {
            ux = 1;
            uy = 0;
        }

        var (ox, oy) = Centro(guia);

        var tMin = double.MaxValue;
        var tMax = double.MinValue;

        foreach (var pieza in piezas)
        {
            foreach (var (x, y) in new[] { (pieza.X1, pieza.Y1), (pieza.X2, pieza.Y2) })
            {
                var t = ((x - ox) * ux) + ((y - oy) * uy);

                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
            }
        }

        return new ElementoPlanta
        {
            Clase = ClasePlanta.Muro,
            Tipo = guia.Tipo,
            Forma = guia.Forma,
            Notas = guia.Notas,
            Seccion = guia.Seccion,
            Material = guia.Material,

            // La etiqueta, la primera que traiga alguno: un panel sin pier deja la etiqueta en
            // blanco, y el castillo se quedaba sin rótulo por el pedazo que no la tenía.
            Etiqueta = piezas.FirstOrDefault(x => (x.Etiqueta ?? string.Empty).Length > 0)
                            ?.Etiqueta ?? string.Empty,

            X1 = ox + (ux * tMin),
            Y1 = oy + (uy * tMin),
            X2 = ox + (ux * tMax),
            Y2 = oy + (uy * tMax),

            AnchoM = piezas.Max(x => x.AnchoM),

            Z1 = piezas.Min(x => Math.Min(x.Z1, x.Z2)),
            Z2 = piezas.Max(x => Math.Max(x.Z1, x.Z2))
        };
    }

    /// <summary>
    /// ¿Cae este punto <b>dentro</b> de un castillo de área?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sirve para lo que se pidió: «cuando tenga un área sea castillo, no coloques el nombre de
    /// la cadena». La cadena que muere en el castillo es corta —a veces mide lo que el castillo—
    /// y su rótulo va al <b>centro</b> de la barra, así que ese centro cae dentro del castillo y
    /// el nombre de la cadena acababa escrito sobre el amarillo, encima del rótulo del propio
    /// castillo.
    /// </para>
    /// <para>
    /// Se mide contra la sección <b>ya girada</b>, deshaciendo su giro, que es la misma cuenta
    /// con la que se recorta un muro en el paño de su apoyo: en un castillo a 30° la caja recta
    /// diría que sí donde no lo hay.
    /// </para>
    /// </remarks>
    public static bool HayCastilloDeAreaEn(
        double x, double y, IEnumerable<ElementoPlanta>? elementos)
    {
        if (elementos is null)
        {
            return false;
        }

        foreach (var el in elementos)
        {
            if (!el.DeShell || el.Clase != ClasePlanta.Columna)
            {
                continue;
            }

            var b = el.AnchoM;
            var h = el.PeralteM;

            if (b <= Nada || h <= Nada)
            {
                continue;
            }

            // Al sistema de la sección: se deshace su giro y así se compara con un rectángulo
            // recto, que es lo único que hay que saber comparar.
            var a = el.AnguloGrados * Math.PI / 180;
            var ca = Math.Cos(a);
            var sa = Math.Sin(a);

            var rx = x - el.X1;
            var ry = y - el.Y1;

            var lx = (rx * ca) + (ry * sa);
            var ly = (-rx * sa) + (ry * ca);

            if (Math.Abs(lx) <= b / 2 && Math.Abs(ly) <= h / 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ¿Pasa el <b>texto</b> de un rótulo por encima de un castillo de área?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la pregunta de verdad, y por eso no bastaba con mirar el punto de inserción: el rótulo
    /// de una cadena es un MTEXT <b>centrado</b> en la barra, así que el texto se extiende a los
    /// dos lados de ese punto. Una cadena de 60 cm entre dos castillos tiene su centro <i>entre</i>
    /// los dos —fuera de los dos— y su nombre, «CC 15X25», mide más que la cadena: el punto no
    /// caía dentro de ningún castillo y el texto los tapaba igual. Eso es lo que se seguía viendo.
    /// </para>
    /// <para>
    /// Se recorre el <b>trazo del texto</b> de punta a punta preguntando por cada tramo, así que
    /// el castillo se detecta esté el texto encima, empezando en él o acabando en él. Se pregunta
    /// cada cinco centímetros: un castillo mide quince, así que no se puede colar entre dos
    /// preguntas.
    /// </para>
    /// </remarks>
    public static bool HayCastilloDeAreaBajoElTexto(
        double xA, double yA, double xB, double yB, IEnumerable<ElementoPlanta>? elementos,
        double altoTexto = 0)
    {
        if (elementos is null)
        {
            return false;
        }

        var dx = xB - xA;
        var dy = yB - yA;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        // Un texto sin largo es su punto, y de eso ya sabe el método de al lado.
        if (largo <= Nada)
        {
            return HayCastilloDeAreaEn(xA, yA, elementos);
        }

        // LAS TRES LÍNEAS DE LA CAJA: la del centro y las dos orillas. Un rótulo no es una raya,
        // es una caja de alto por largo —y con el fondo opaco puesto, todavía un poco más—, así
        // que preguntando solo por su centro se escapaba el caso en que el texto tapa el castillo
        // con media letra. El medio alto se toma con holgura por ese fondo.
        var medioAlto = altoTexto > 0 ? altoTexto * 0.65 : 0;

        var nx = largo > Nada ? -dy / largo : 0;
        var ny = largo > Nada ? dx / largo : 0;

        var pasos = Math.Max(2, (int)Math.Ceiling(largo / 0.05));

        foreach (var lado in new[] { 0d, medioAlto, -medioAlto })
        {
            if (lado != 0 && medioAlto <= 0)
            {
                continue;
            }

            for (var i = 0; i <= pasos; i++)
            {
                var t = (double)i / pasos;

                if (HayCastilloDeAreaEn(
                        xA + (dx * t) + (nx * lado), yA + (dy * t) + (ny * lado), elementos))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// El nombre de un castillo por su <b>medida en planta</b>: <c>K 15X23.5</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El espesor primero y el largo después, en centímetros, con hasta dos decimales y
    /// <b>sin ceros de relleno</b>: 15 es «15» y 23.5 es «23.5». Con punto decimal siempre
    /// —cultura invariante—, porque este texto acaba siendo un <b>nombre de bloque</b> de
    /// AutoCAD y no puede depender de la configuración regional de la máquina: la misma
    /// sección saldría «K 15X23,5» en una y «K 15X23.5» en otra, y serían dos bloques.
    /// </para>
    /// <para>
    /// El prefijo se recorta y se le pone <b>un</b> espacio: en la hoja CONFIG se escribe
    /// <c>K</c> y da igual si alguien lo deja como <c>K </c>.
    /// </para>
    /// </remarks>
    public static string Nombre(string? prefijo, double espesorM, double largoM)
    {
        var p = (prefijo ?? string.Empty).Trim();
        var medida = $"{Cm(espesorM)}X{Cm(largoM)}";

        return p.Length > 0 ? p + " " + medida : medida;
    }

    /// <summary>De metros a centímetros, sin decimales de relleno.</summary>
    private static string Cm(double m) =>
        (m * 100).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alarga el castillo hasta el <b>paño</b> del muro con el que se cruza en cada punta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se pidió: «cuando sea área un castillo debes sumarle la mitad del espesor de la sección
    /// en el lado donde se intersecta con otro muro modelado, para que llegue al paño y no se
    /// corte antes». Y es lo que hace falta, porque en el modelo <b>los muros se dibujan por su
    /// eje</b>: el shell del castillo se traza hasta la <i>línea</i> del muro con el que se
    /// topa, así que en el plano el castillo se quedaba a media pared —el paño del muro seguía
    /// más allá de él— y parecía cortado.
    /// </para>
    /// <para>
    /// La cuenta no es «súmale medio espesor y ya», sino <b>hasta dónde falta</b>: se busca
    /// dónde cruza el eje del otro muro y se alarga hasta su cara de más allá, que está a medio
    /// espesor de ese eje. Con el castillo modelado al eje sale exactamente el medio espesor que
    /// se pidió, y si ya llegaba al paño no se alarga nada. Sumar siempre medio espesor lo
    /// pasaría de largo justo en ese caso.
    /// </para>
    /// <para>
    /// Solo cuentan los <b>MUROS</b> —es lo que se pidió— que <b>cruzan</b> al castillo, no los
    /// que corren en su misma dirección, y solo si el cruce cae <b>en la punta</b>: un muro que
    /// pasa por el medio del castillo no lo alarga, y uno que cruza a dos metros tampoco. Otro
    /// castillo de área tampoco cuenta: entre dos castillos no hay paño que alcanzar.
    /// </para>
    /// </remarks>
    /// <param name="castillo">El shell del castillo, ya unido si venía en pedazos.</param>
    /// <param name="otros">Todos los elementos de la planta.</param>
    /// <param name="espesorPorOmision">Espesor para el muro que no dio el suyo.</param>
    /// <param name="tolM">Holgura del encuentro: es <c>PANO_TOLERANCIA_CM</c>.</param>
    public static ElementoPlanta AlPanoDeLosMuros(
        ElementoPlanta castillo, IEnumerable<ElementoPlanta> otros,
        double espesorPorOmision, double tolM)
    {
        var (ux, uy, largo) = Direccion(castillo);

        if (largo <= Nada)
        {
            return castillo;
        }

        // Cuánto le falta a cada punta para llegar al paño. La punta A es la de X1,Y1.
        double faltaA = 0;
        double faltaB = 0;

        foreach (var muro in otros)
        {
            if (muro.Clase != ClasePlanta.Muro || Dice(muro) || ReferenceEquals(muro, castillo))
            {
                continue;
            }

            var (vx, vy, largoMuro) = Direccion(muro);

            if (largoMuro <= Nada)
            {
                continue;
            }

            // QUE SE CRUCEN DE VERDAD: dos líneas casi paralelas no se cruzan en un punto, se
            // acompañan, y ahí no hay paño que alcanzar sino un muro que sigue al castillo.
            var den = (ux * vy) - (uy * vx);

            if (Math.Abs(den) < 0.1)
            {
                continue;
            }

            var wx = muro.X1 - castillo.X1;
            var wy = muro.Y1 - castillo.Y1;

            // Dónde se cruzan: t sobre el castillo —en metros desde su punta A— y s sobre el
            // muro, para comprobar que el cruce cae DENTRO del muro y no en su prolongación.
            var t = ((wx * vy) - (wy * vx)) / den;
            var sMuro = ((wx * uy) - (wy * ux)) / den;

            if (sMuro < -tolM || sMuro > largoMuro + tolM)
            {
                continue;
            }

            var medio = (muro.AnchoM > Nada ? muro.AnchoM : espesorPorOmision) / 2;

            // EN LA PUNTA: el cruce tiene que caer cerca de un extremo. Se admite hasta el
            // espesor del muro más la holgura, que es lo que puede quedar dentro del castillo
            // cuando el modelo los solapa un poco.
            var alcance = tolM + (medio * 2);

            if (Math.Abs(t) <= alcance)
            {
                // Hacia afuera por la punta A: lo que falta para la cara de más allá.
                faltaA = Math.Max(faltaA, medio - t);
            }

            if (Math.Abs(t - largo) <= alcance)
            {
                faltaB = Math.Max(faltaB, (t - largo) + medio);
            }
        }

        if (faltaA <= Nada && faltaB <= Nada)
        {
            return castillo;
        }

        return new ElementoPlanta
        {
            Clase = castillo.Clase,
            Tipo = castillo.Tipo,
            Forma = castillo.Forma,
            Etiqueta = castillo.Etiqueta,
            Seccion = castillo.Seccion,
            Notas = castillo.Notas,
            Material = castillo.Material,

            X1 = castillo.X1 - (ux * Math.Max(faltaA, 0)),
            Y1 = castillo.Y1 - (uy * Math.Max(faltaA, 0)),
            X2 = castillo.X2 + (ux * Math.Max(faltaB, 0)),
            Y2 = castillo.Y2 + (uy * Math.Max(faltaB, 0)),

            Z1 = castillo.Z1,
            Z2 = castillo.Z2,

            AnchoM = castillo.AnchoM,
            PeralteM = castillo.PeralteM,
            AnguloGrados = castillo.AnguloGrados
        };
    }

    /// <summary>La dirección unitaria de un shell en planta, y su largo.</summary>
    private static (double X, double Y, double Largo) Direccion(ElementoPlanta el)
    {
        var dx = el.X2 - el.X1;
        var dy = el.Y2 - el.Y1;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        return largo > Nada ? (dx / largo, dy / largo, largo) : (0, 0, 0);
    }

    /// <summary>El largo en planta, para saber qué pieza manda.</summary>
    private static double Largo(ElementoPlanta el) => Direccion(el).Largo;

    /// <summary>El punto medio en planta.</summary>
    private static (double X, double Y) Centro(ElementoPlanta el) =>
        ((el.X1 + el.X2) / 2, (el.Y1 + el.Y2) / 2);

    /// <summary>
    /// El tramo que ocupa un shell <b>sobre una línea</b>, ordenado de menor a mayor.
    /// </summary>
    private static (double A, double B) Tramo(
        ElementoPlanta el, double ux, double uy, double ox, double oy)
    {
        var t1 = ((el.X1 - ox) * ux) + ((el.Y1 - oy) * uy);
        var t2 = ((el.X2 - ox) * ux) + ((el.Y2 - oy) * uy);

        return t1 <= t2 ? (t1, t2) : (t2, t1);
    }
}
