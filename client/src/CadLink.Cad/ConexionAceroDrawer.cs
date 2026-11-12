using System.Reflection;

namespace CadLink.Cad;

/// <summary>
/// Dibuja en AutoCAD el detalle de una conexion de acero leida del libro de Excel: su titulo, sus
/// vistas una junto a otra con sus llamadas y cotas, y abajo el cuadro de datos de la hoja.
/// </summary>
/// <remarks>
/// <para>
/// Tamaño real, como la placa base: <paramref name="escala"/> son unidades del dibujo por cm
/// (0.01 = el dibujo en metros). Los textos salen de «Estilo de dibujo» y estan pensados para
/// imprimir a 1:10; si el dibujo no esta en metros se escalan en la misma proporcion.
/// </para>
/// <para>
/// Como los demas dibujantes, cada llamada a AutoCAD va con su reintento y su try: una entidad que
/// AutoCAD rechaza se anota en <see cref="Fallos"/> y el detalle sigue.
/// </para>
/// </remarks>
public sealed class ConexionAceroDrawer
{
    private readonly dynamic _doc;
    private readonly dynamic _ms;
    private readonly double _escala;
    private readonly double _f;
    private readonly List<string> _fallos = new();
    private readonly List<string> _notas = new();

    /// <summary>En la vista previa no hay AutoCAD: los trazos se graban aqui.</summary>
    private readonly List<TrazoConexion>? _grabado;

    public ConexionAceroDrawer(dynamic doc, double escala = 0.01)
    {
        _doc = doc;
        _ms = AcadConnection.Retry(() => (object)doc.ModelSpace);
        _escala = escala > 0 ? escala : 0.01;
        _f = _escala / 0.01;
    }

    private ConexionAceroDrawer(double escala)
    {
        _doc = null!;
        _ms = null!;
        _escala = escala > 0 ? escala : 0.01;
        _f = _escala / 0.01;
        _grabado = new List<TrazoConexion>();
    }

    /// <summary>
    /// Un dibujante SIN AutoCAD: hace el mismo acomodo y graba los trazos en <see cref="Grabado"/>,
    /// para la vista previa de CadLink. Lo que se ve ahi es lo que se dibujaria.
    /// </summary>
    public static ConexionAceroDrawer ParaVistaPrevia(double escala = 0.01) => new(escala);

    /// <summary>Los trazos de la vista previa; vacio si se dibuja en AutoCAD.</summary>
    public IReadOnlyList<TrazoConexion> Grabado => (IReadOnlyList<TrazoConexion>?)_grabado ?? Array.Empty<TrazoConexion>();

    public IReadOnlyList<string> Fallos => _fallos;

    public IReadOnlyList<string> Notas => _notas;

    /// <summary>Lo que midio a lo ancho el ultimo detalle, en unidades del dibujo.</summary>
    public double UltimoAncho { get; private set; }

    private double HTexto => ConexionAceroCapas.AltoTexto * _f;

    // =====================================================================
    //  Capas y estilos
    // =====================================================================

    public void AsegurarCapas()
    {
        if (_grabado is not null)
        {
            return;
        }

        foreach (var c in Enum.GetValues<CapaConexion>())
        {
            Capa(c);
        }

        EstiloDeTexto();
        EstiloDeCota();
    }

    private void Capa(CapaConexion c)
    {
        var nombre = ConexionAceroCapas.Nombre(c);
        var color = ConexionAceroCapas.ColorDeCapa(c);
        var tipo = ConexionAceroCapas.TipoDeLinea(c);

        if (tipo is not null)
        {
            TipoDeLinea(tipo);
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic capas = _doc.Layers;
                dynamic capa;
                var nueva = false;

                try
                {
                    capa = capas.Item(nombre);
                }
                catch (Exception)
                {
                    capa = capas.Add(nombre);
                    nueva = true;
                }

                // Una capa nueva, o una cuyo color eligio el usuario en «Estilo de dibujo». La del eje
                // era roja y se confundia con las cotas: si sigue con aquel rojo, pasa al color nuevo.
                if (nueva || ConexionAceroCapas.ColorElegido(c) || (c == CapaConexion.Eje && (int)capa.Color == 1))
                {
                    capa.Color = color;
                }

                // ROTULOS se imprime en negro: con estilos de trazado nombrados (.stb), su estilo
                // «Negro», como la macro. Con .ctb no hay estilo por capa y esto no aplica.
                if (nueva && c == CapaConexion.Texto)
                {
                    try
                    {
                        capa.PlotStyleName = "Negro";
                    }
                    catch (Exception)
                    {
                        // Dibujo con .ctb o sin el estilo «Negro»: la capa queda como esta.
                    }
                }

                if (nueva && tipo is not null)
                {
                    try
                    {
                        capa.Linetype = tipo;
                    }
                    catch (Exception)
                    {
                        // Sin el tipo de linea cargado, la capa queda continua.
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Fallo($"Capa '{nombre}'", ex);
        }
    }

    private void TipoDeLinea(string nombre)
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Linetypes.Item(nombre);
                }
                catch (Exception)
                {
                    try
                    {
                        _doc.Linetypes.Load(nombre, "acad.lin");
                    }
                    catch (Exception)
                    {
                        _doc.Linetypes.Load(nombre, "acadiso.lin");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Nota($"No se pudo cargar el tipo de linea {nombre}: las lineas ocultas o de eje salen continuas. ({ex.Message})");
        }
    }

    private void EstiloDeTexto()
    {
        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.TextStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(ConexionAceroCapas.EstiloTexto);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(ConexionAceroCapas.EstiloTexto);
                }

                estilo.SetFont(ConexionAceroCapas.Fuente, false, false, 0, 34);
                estilo.Width = 1.0;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Estilo de texto '{ConexionAceroCapas.EstiloTexto}'", ex);
        }
    }

    private void Variable(string nombre, object valor)
    {
        try
        {
            AcadConnection.Retry(() => { _doc.SetVariable(nombre, valor); });
        }
        catch (Exception ex)
        {
            Fallo($"Variable de cota {nombre}", ex);
        }
    }

    private void EstiloDeCota()
    {
        var alto = ConexionAceroCapas.AltoCota * _f;
        var marca = ConexionAceroCapas.TamanoMarcaCota * _f;
        var tipoMarca = ConexionAceroCapas.TipoMarca;

        Variable("DIMSCALE", 1.0);
        Variable("DIMTXT", alto);
        Variable("DIMASZ", marca);
        Variable("DIMEXE", marca);
        Variable("DIMEXO", marca / 2);
        Variable("DIMGAP", alto / 3);
        Variable("DIMTAD", 1);
        Variable("DIMTIH", 0);
        Variable("DIMTOH", 0);
        Variable("DIMATFIT", 3);
        Variable("DIMTOFL", 1);
        Variable("DIMCLRT", ConexionAceroCapas.ColorNumeroCota);
        Variable("DIMTXSTY", ConexionAceroCapas.EstiloTexto);
        Variable("DIMSAH", 1);
        Variable("DIMBLK", tipoMarca);
        Variable("DIMBLK1", tipoMarca);
        Variable("DIMBLK2", tipoMarca);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic estilos = _doc.DimStyles;
                dynamic estilo;

                try
                {
                    estilo = estilos.Item(ConexionAceroCapas.EstiloCota);
                }
                catch (Exception)
                {
                    estilo = estilos.Add(ConexionAceroCapas.EstiloCota);
                }

                estilo.CopyFrom(_doc);
                _doc.ActiveDimStyle = estilo;
            });
        }
        catch (Exception ex)
        {
            Fallo($"Estilo de cota '{ConexionAceroCapas.EstiloCota}'", ex);
        }
    }

    // =====================================================================
    //  El detalle
    // =====================================================================

    /// <summary>
    /// El acomodo de una vista: las llamadas se reparten en DOS columnas, la de la izquierda para
    /// las piezas de la mitad izquierda y la de la derecha para las otras. Asi las lineas no cruzan
    /// todo el detalle ni se enciman.
    /// </summary>
    /// <remarks>
    /// Las llamadas que muerden CERCA una de otra -las del mismo nudo- van del mismo lado: si no,
    /// llegan al nudo desde los dos lados y lo tapan.
    /// </remarks>
    private sealed record Acomodo(
        double Holgura, double AnchoGeo, double AltoGeo, double AnchoIzq, double AnchoDer,
        List<LlamadaConexion> Izquierda, List<LlamadaConexion> Derecha, double AltoColumnas)
    {
        public double Ancho => AnchoIzq + (2 * Holgura) + AnchoGeo + AnchoDer;
    }

    private double HSimbolo => ConexionAceroCapas.AltoSimbolo * _f;

    /// <summary>Lo que se aparta una vista de la otra, y un detalle del siguiente.</summary>
    private double Separacion => ConexionAceroCapas.SeparacionDetalles * _f;

    private Acomodo Acomodar(VistaConexionCad v)
    {
        var h = HTexto;
        var centro = (v.MinX + v.MaxX) / 2;

        // Los nudos: las llamadas que muerden a menos de tres alturas de texto una de otra (la
        // vista viene en cm; el texto, en unidades del dibujo).
        var cerca = 3 * h / _escala;
        var nudos = new List<List<LlamadaConexion>>();

        foreach (var l in v.Llamadas.OrderBy(q => q.X).ThenBy(q => q.Y))
        {
            var nudo = nudos.FirstOrDefault(g => g.Any(q => Math.Abs(q.X - l.X) < cerca && Math.Abs(q.Y - l.Y) < cerca));

            if (nudo is null)
            {
                nudos.Add(new List<LlamadaConexion> { l });
            }
            else
            {
                nudo.Add(l);
            }
        }

        static double Medio(List<LlamadaConexion> g) => g.Average(q => q.X);
        static int Cuantas(List<List<LlamadaConexion>> lado) => lado.Sum(g => g.Count);

        var izqN = nudos.Where(g => Medio(g) < centro).ToList();
        var derN = nudos.Where(g => Medio(g) >= centro).ToList();

        // Que ninguna columna se cargue de mas: pasa el nudo mas cercano al centro, entero, si con
        // eso se emparejan.
        static bool Pasar(List<List<LlamadaConexion>> de, List<List<LlamadaConexion>> a, bool desdeLaIzquierda)
        {
            var dif = Cuantas(de) - Cuantas(a);

            if (dif <= 2)
            {
                return false;
            }

            var nudo = (desdeLaIzquierda ? de.OrderByDescending(Medio) : de.OrderBy(Medio)).FirstOrDefault(g => g.Count < dif);

            if (nudo is null)
            {
                return false;
            }

            de.Remove(nudo);
            a.Add(nudo);
            return true;
        }

        while (Pasar(izqN, derN, desdeLaIzquierda: true))
        {
        }

        while (Pasar(derN, izqN, desdeLaIzquierda: false))
        {
        }

        // Un solo nudo con muchas llamadas (la columna de una union viga-columna) dejaria una
        // columna larguisima y la otra vacia: entonces si se reparte, por llamada, empezando por
        // las mas cercanas al centro.
        static void Repartir(List<List<LlamadaConexion>> de, List<List<LlamadaConexion>> a, bool desdeLaIzquierda)
        {
            while (Cuantas(de) - Cuantas(a) > 6)
            {
                var todas = de.SelectMany(g => g).ToList();
                var l = desdeLaIzquierda ? todas.OrderByDescending(q => q.X).First() : todas.OrderBy(q => q.X).First();
                var g0 = de.First(g => g.Contains(l));
                g0.Remove(l);

                if (g0.Count == 0)
                {
                    de.Remove(g0);
                }

                a.Add(new List<LlamadaConexion> { l });
            }
        }

        Repartir(izqN, derN, desdeLaIzquierda: true);
        Repartir(derN, izqN, desdeLaIzquierda: false);

        var izq = izqN.SelectMany(g => g).OrderByDescending(l => l.Y).ToList();
        var der = derN.SelectMany(g => g).OrderByDescending(l => l.Y).ToList();

        double Ancho(List<LlamadaConexion> lista) => lista.Count == 0
            ? 0
            : (2.6 * h) + lista.Max(l => l.Simbolo is null
                ? AnchoDeRenglones(l.Texto, h)
                : SimbolosSoldadura.LargoApuntado(HSimbolo, l.Electrodo) + (0.6 * h) + AnchoDeRenglones(l.Descripcion, h));

        double Alto(List<LlamadaConexion> lista) => lista.Sum(l => Paso(l));

        return new Acomodo(1.5 * h, (v.MaxX - v.MinX) * _escala, (v.MaxY - v.MinY) * _escala, Ancho(izq), Ancho(der),
            izq, der, Math.Max(Alto(izq), Alto(der)));
    }

    /// <summary>De renglon a renglon: el simbolo de soldadura pide mas que un texto, y una llamada larga va en dos renglones.</summary>
    private double Paso(LlamadaConexion l)
    {
        var h = HTexto;
        var n = Math.Max(1, Partir(l.Simbolo is null ? l.Texto : l.Descripcion).Count);
        var texto = (1.9 * h) + ((n - 1) * 1.6 * h);

        return l.Simbolo is null
            ? texto
            : Math.Max(texto, SimbolosSoldadura.AltoApuntado(HSimbolo) + (0.5 * HTexto));
    }

    /// <summary>
    /// El ancho de un texto de una linea, a ojo: la fuente del estilo es angosta (semicondensada) y
    /// mide un poco mas de media altura por letra.
    /// </summary>
    private static double AnchoDeTexto(string texto, double h) => texto.Length * 0.56 * h;

    private double AnchoDeRenglones(string texto, double h) =>
        Partir(texto).Select(t => AnchoDeTexto(t, h)).DefaultIfEmpty(0).Max();

    /// <summary>Toda especificacion empieza con mayuscula: «Placa a la viga, ambos lados».</summary>
    private static string Mayuscula(string texto)
    {
        var t = (texto ?? string.Empty).TrimStart();
        return t.Length > 0 && char.IsLower(t[0]) ? char.ToUpperInvariant(t[0]) + t[1..] : t;
    }

    /// <summary>Una llamada larga, partida en renglones de las letras del estilo, por palabras.</summary>
    private static List<string> Partir(string texto) => Partir(texto, (int)Math.Max(12, ConexionAceroCapas.LetrasPorRenglon));

    private static List<string> Partir(string texto, int letras)
    {
        var renglones = new List<string>();
        var actual = string.Empty;

        foreach (var palabra in (texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (actual.Length > 0 && actual.Length + 1 + palabra.Length > letras)
            {
                renglones.Add(actual);
                actual = palabra;
            }
            else
            {
                actual = actual.Length == 0 ? palabra : actual + " " + palabra;
            }
        }

        if (actual.Length > 0)
        {
            renglones.Add(actual);
        }

        return renglones;
    }

    private double AnchoDeVistas(ConexionAceroCad c)
    {
        var vistas = SinRepetir(c);
        return vistas.Select(Acomodar).Sum(a => a.Ancho + Separacion) - (vistas.Count > 0 ? Separacion : 0);
    }

    /// <summary>
    /// Las vistas con cada llamada UNA vez: lo que ya se llamo en una vista no se vuelve a llamar
    /// en la siguiente (la placa superior se señala en la elevacion y no otra vez en la planta).
    /// Es lo que mas descarga los nudos de las uniones con muchas piezas.
    /// </summary>
    private static List<VistaConexionCad> SinRepetir(ConexionAceroCad c)
    {
        var dichas = new HashSet<string>(StringComparer.Ordinal);
        var lista = new List<VistaConexionCad>();

        foreach (var v in c.Vistas)
        {
            var nueva = new VistaConexionCad { Titulo = v.Titulo, MinX = v.MinX, MinY = v.MinY, MaxX = v.MaxX, MaxY = v.MaxY };
            nueva.Lineas.AddRange(v.Lineas);
            nueva.Contornos.AddRange(v.Contornos);
            nueva.Circulos.AddRange(v.Circulos);
            nueva.Cotas.AddRange(v.Cotas);

            var deEsta = new List<string>();

            foreach (var l in v.Llamadas)
            {
                var clave = l.Simbolo is null ? l.Texto.Trim() : $"{l.Simbolo}|{l.Tamano}|{l.Descripcion.Trim()}";

                if (!dichas.Contains(clave))
                {
                    nueva.Llamadas.Add(l);
                    deEsta.Add(clave);
                }
            }

            dichas.UnionWith(deEsta);
            lista.Add(nueva);
        }

        return lista;
    }

    /// <summary>Lo que va a medir el detalle a lo ancho, en unidades del dibujo, sin dibujarlo.</summary>
    public double Medir(ConexionAceroCad c)
    {
        var vistas = AnchoDeVistas(c);
        var (datos, notas) = Tablas(c, vistas);
        return Math.Max(Math.Max(vistas, AnchoDeTexto(c.Titulo, ConexionAceroCapas.AltoTitulo * _f)),
            Math.Max(datos?.Ancho ?? 0, notas?.Ancho ?? 0));
    }

    /// <summary>
    /// Dibuja el detalle con su esquina de arriba a la izquierda en (<paramref name="x0"/>,
    /// <paramref name="y0"/>), en unidades del dibujo. Devuelve cuantas entidades se crearon.
    /// </summary>
    /// <remarks>
    /// El titulo, las vistas, el nombre de cada vista y las tablas de datos y de notas van
    /// CENTRADOS en el detalle.
    /// </remarks>
    public int Dibujar(ConexionAceroCad c, double x0, double y0)
    {
        var antes = Contar();
        var h = HTexto;
        var hTitulo = ConexionAceroCapas.AltoTitulo * _f;
        var hVista = ConexionAceroCapas.AltoVista * _f;
        var anchoVistas = AnchoDeVistas(c);
        var (datos, notas) = Tablas(c, anchoVistas);
        var ancho = Medir(c);
        var centro = x0 + (ancho / 2);

        Texto(c.Titulo, centro, y0, hTitulo, CapaConexion.Texto, anclaje: 2);
        Texto(c.Subtitulo, centro, y0 - (1.7 * hTitulo), 0.8 * h, CapaConexion.Texto, anclaje: 2);

        var arriba = y0 - (1.7 * hTitulo) - (2.5 * h);
        var x = centro - (anchoVistas / 2);
        var abajo = arriba;

        // Las vistas, centradas a lo alto entre ellas: la planta chica no se queda colgada arriba.
        var vistas = SinRepetir(c);
        var altos = vistas.Select(v => AltoDeVista(Acomodar(v), hVista)).ToList();
        var mayor = altos.DefaultIfEmpty(0).Max();

        for (var i = 0; i < vistas.Count; i++)
        {
            var (anchoVista, alto) = Vista(vistas[i], x, arriba - ((mayor - altos[i]) / 2), hVista);
            x += anchoVista + Separacion;
            abajo = Math.Min(abajo, arriba - ((mayor - altos[i]) / 2) - alto);
        }

        // Los datos y las notas, en tablas centradas bajo las vistas.
        var y = abajo - (1.5 * h);

        if (datos is not null)
        {
            y -= DibujarTabla(datos, centro, y) + (1.2 * h);
        }

        if (notas is not null)
        {
            y -= DibujarTabla(notas, centro, y) + (1.2 * h);
        }

        UltimoAncho = ancho;
        UltimoAlto = y0 - y;
        return Contar() - antes;
    }

    /// <summary>Lo que midio a lo alto el ultimo detalle.</summary>
    public double UltimoAlto { get; private set; }

    // =====================================================================
    //  Las tablas de datos y de notas
    // =====================================================================

    /// <summary>Un renglon de tabla: dato y valor, o un texto que ocupa todo el ancho.</summary>
    private sealed record FilaTabla(IReadOnlyList<string> Dato, IReadOnlyList<string> Valor)
    {
        public int Renglones => Math.Max(1, Math.Max(Dato.Count, Valor.Count));
    }

    /// <summary>
    /// Una tabla: su titulo y sus renglones, en una o dos columnas de dato y valor (con muchos
    /// datos, dos columnas para que no quede una tira larguisima).
    /// </summary>
    private sealed record TablaConexion(string Titulo, double AnchoColumna, double AnchoDato, IReadOnlyList<IReadOnlyList<FilaTabla>> Columnas)
    {
        public double Ancho => AnchoColumna * Columnas.Count;
    }

    /// <summary>La letra a lo ancho dentro de las tablas: algo holgada, para que nada se salga del marco.</summary>
    private const double LetraDeTabla = 0.62;

    /// <summary>Con mas renglones que estos, la tabla de datos va en dos columnas.</summary>
    private const int RenglonesPorColumna = 14;

    /// <summary>
    /// Las tablas del detalle, del mismo ancho las dos. Cada renglon del resumen de la hoja se
    /// parte en sus piezas («·») y cada pieza en dato y valor («Placa superior: PL 31 × 80»).
    /// </summary>
    private (TablaConexion? Datos, TablaConexion? Notas) Tablas(ConexionAceroCad c, double anchoVistas)
    {
        var h = HTexto;
        var letra = LetraDeTabla * h;
        var pad = 0.6 * h;

        var piezas = c.Renglones
            .SelectMany(r => r.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Trim('—', '-', ' ').Length > 0)
            .Select(p =>
            {
                var i = p.IndexOf(": ", StringComparison.Ordinal);
                return i > 0 ? (Dato: Mayuscula(p[..i]), Valor: Mayuscula(p[(i + 2)..])) : (Dato: string.Empty, Valor: Mayuscula(p));
            })
            .ToList();

        var notas = c.Notas.Select((n, i) => (Dato: $"{i + 1}.", Valor: Mayuscula(n))).ToList();
        var columnas = piezas.Count > RenglonesPorColumna ? 2 : 1;

        // La columna de los datos: hasta treinta letras; un dato mas largo se parte en renglones.
        var largoDato = piezas.Where(p => p.Dato.Length > 0).Select(p => Math.Min(30, p.Dato.Length)).DefaultIfEmpty(0).Max();
        var anchoDato = largoDato > 0 ? (largoDato * letra) + (2 * pad) : 0;
        var anchoNumero = notas.Count > 0 ? (3 * letra) + (2 * pad) : 0;

        // Lo que pedirian sin partir, con tope: una tabla que se lee de un vistazo, no un renglon
        // que cruza todo el detalle. Lo largo se parte en renglones. Con dos columnas, la del valor
        // es la del valor mas largo, hasta veinticuatro letras.
        var natural = piezas.Select(p => (p.Dato.Length > 0 ? anchoDato : 0) + (p.Valor.Length * letra) + (2 * pad)).DefaultIfEmpty(0).Max();
        var tope = columnas == 2 ? anchoDato + (24 * letra) + (2 * pad) : Math.Min(Math.Max(anchoVistas, 40 * letra), 60 * letra);
        var anchoColumna = Math.Max(Math.Min(natural, tope), (columnas == 2 ? 30 : 34) * letra);

        // Las notas, del ancho de los datos (o de lo que pidan, con el mismo tope).
        var anchoNotas = piezas.Count > 0
            ? anchoColumna * columnas
            : Math.Max(Math.Min(notas.Select(n => anchoNumero + (n.Valor.Length * letra) + (2 * pad)).DefaultIfEmpty(0).Max(), 60 * letra), 34 * letra);

        List<FilaTabla> Filas(IEnumerable<(string Dato, string Valor)> lista, double ancho, double anchoIzq) => lista.Select(p =>
        {
            var izq = p.Dato.Length > 0 ? anchoIzq : 0;
            var dato = p.Dato.Length > 0 ? Partir(p.Dato, (int)Math.Max(6, (anchoIzq - (2 * pad)) / letra)) : new List<string>();
            var valor = Partir(p.Valor, (int)Math.Max(10, (ancho - izq - (2 * pad)) / letra));
            return new FilaTabla(dato, valor);
        }).ToList();

        TablaConexion? datos = null;

        if (piezas.Count > 0)
        {
            var filas = Filas(piezas, anchoColumna, anchoDato);
            var partes = new List<IReadOnlyList<FilaTabla>> { filas };

            if (columnas == 2)
            {
                // Se parte por la mitad de lo ALTO, no de los renglones: los partidos ocupan mas.
                var total = filas.Sum(f => f.Renglones);
                var suma = 0;
                var corte = filas.FindIndex(f => (suma += f.Renglones) * 2 >= total) + 1;
                partes = new List<IReadOnlyList<FilaTabla>> { filas.Take(corte).ToList(), filas.Skip(corte).ToList() };
            }

            datos = new TablaConexion("DATOS DE LA CONEXIÓN", anchoColumna, anchoDato, partes);
        }

        var tNotas = notas.Count > 0
            ? new TablaConexion("NOTAS", anchoNotas, anchoNumero, new[] { (IReadOnlyList<FilaTabla>)Filas(notas, anchoNotas, anchoNumero) })
            : null;
        return (datos, tNotas);
    }

    /// <summary>Una tabla con su marco, su encabezado y sus renglones, centrada en x. Devuelve su alto.</summary>
    private double DibujarTabla(TablaConexion t, double centro, double arriba)
    {
        var h = HTexto;
        var hVista = ConexionAceroCapas.AltoVista * _f;
        var pad = 0.6 * h;
        var x0 = centro - (t.Ancho / 2);
        var x1 = centro + (t.Ancho / 2);
        var hEncabezado = 1.9 * hVista;

        Texto(t.Titulo, centro, arriba - (hEncabezado / 2), hVista, CapaConexion.Texto, anclaje: 5);
        Linea(x0, arriba - hEncabezado, x1, arriba - hEncabezado, CapaConexion.Texto);

        // Cada renglon, un texto a media altura: el paso entre renglones queda exacto.
        void Renglones(IReadOnlyList<string> lineas, double x, double y)
        {
            for (var k = 0; k < lineas.Count; k++)
            {
                Texto(lineas[k], x, y - (k * 1.6 * h), h, CapaConexion.Texto, anclaje: 4);
            }
        }

        double Alto(FilaTabla f) => (f.Renglones * 1.6 * h) + (0.5 * h);
        var fondo = arriba - hEncabezado - t.Columnas.Select(col => col.Sum(Alto)).DefaultIfEmpty(0).Max();

        for (var c = 0; c < t.Columnas.Count; c++)
        {
            var xc = x0 + (c * t.AnchoColumna);
            var y = arriba - hEncabezado;
            var filas = t.Columnas[c];

            if (c > 0)
            {
                Linea(xc, y, xc, fondo, CapaConexion.Texto);
            }

            for (var i = 0; i < filas.Count; i++)
            {
                var f = filas[i];
                var alto = Alto(f);
                var yTexto = y - (0.25 * h) - (0.8 * h);
                var conDato = f.Dato.Count > 0;

                if (conDato)
                {
                    Renglones(f.Dato, xc + pad, yTexto);
                    Linea(xc + t.AnchoDato, y, xc + t.AnchoDato, y - alto, CapaConexion.Texto);
                }

                Renglones(f.Valor, conDato ? xc + t.AnchoDato + pad : xc + pad, yTexto);

                y -= alto;

                if (i < filas.Count - 1 || y > fondo + 1e-9)
                {
                    Linea(xc, y, xc + t.AnchoColumna, y, CapaConexion.Texto);
                }
            }
        }

        Polilinea(new[] { (x0, arriba), (x1, arriba), (x1, fondo), (x0, fondo) }, true, CapaConexion.Texto);
        return arriba - fondo;
    }

    // Los colores del rayado de las secciones de acero (SeccionDrawer.RayarPerfil, los de la macro).
    private const int ColorRayaLaminado = 252;
    private const int ColorFondoTubo = 141;
    private const int ColorRayaTubo = 144;
    private const int ColorRayaTuboChico = 142;
    private const int ColorFondoTuboChico = 4;

    /// <summary>
    /// El rayado de un perfil en corte, el de las secciones de acero: los laminados con su patron
    /// de color 252; el tubo de 5" o mas con fondo 141 y raya 144, y el chico con fondo 4 y raya
    /// 142. El tubo lleva su hueco como isla.
    /// </summary>
    private void RayarCorte(object contorno, object? isla, ContornoConexion p)
    {
        if (p.Corte == CorteConexion.Laminado)
        {
            Rellenar(contorno, p.Capa, ConexionAceroCapas.PatronLaminado, ConexionAceroCapas.EscalaLaminado * _f, ColorRayaLaminado, isla: isla);
            return;
        }

        var menorDe5 = p.PeralteCm / 2.54 < 5 - 0.01;
        Rellenar(contorno, p.Capa, "SOLID", 1, menorDe5 ? ColorFondoTuboChico : ColorFondoTubo, isla: isla);
        Rellenar(contorno, p.Capa, ConexionAceroCapas.PatronTubo, ConexionAceroCapas.EscalaTubo(menorDe5) * _f,
            menorDe5 ? ColorRayaTuboChico : ColorRayaTubo, isla: isla);
    }

    /// <summary>Lo alto de una vista con su nombre, sin dibujarla: lo mismo que devuelve <see cref="Vista"/>.</summary>
    private static double AltoDeVista(Acomodo a, double hVista) =>
        Math.Max(a.AltoGeo, a.AltoColumnas) + (2 * a.Holgura) + (2.2 * hVista);

    /// <summary>Una vista con su esquina de arriba a la izquierda en (x, y). Devuelve lo que ocupo.</summary>
    private (double Ancho, double Alto) Vista(VistaConexionCad v, double x, double y, double hVista)
    {
        var h = HTexto;
        var desfase = ConexionAceroCapas.DesfaseCota * _f;
        var a = Acomodar(v);
        var alto = Math.Max(a.AltoGeo, a.AltoColumnas) + (2 * a.Holgura);

        // El dibujo, centrado a lo alto en lo que ocupan sus llamadas.
        var xGeo = x + a.AnchoIzq + a.Holgura;
        var yGeo = y - ((alto - a.AltoGeo) / 2);
        double X(double cx) => xGeo + ((cx - v.MinX) * _escala);
        double Y(double cy) => yGeo - ((v.MaxY - cy) * _escala);

        foreach (var l in v.Lineas)
        {
            Linea(X(l.X1), Y(l.Y1), X(l.X2), Y(l.Y2), l.Capa);
        }

        foreach (var p in v.Contornos)
        {
            var puntos = p.Puntos.Select(q => (X(q.X), Y(q.Y))).ToList();
            var pl = Polilinea(puntos, p.Cerrado, p.Capa);

            // Un perfil en corte se raya como en las secciones de acero.
            if (p.Corte != CorteConexion.Ninguno && pl is not null && p.Cerrado && puntos.Count >= 3)
            {
                object? isla = null;

                if (p.Isla is { Count: >= 3 } hueco)
                {
                    // Una segunda polilinea, la del hueco, que ademas se queda dibujada.
                    isla = Polilinea(hueco.Select(q => (X(q.X), Y(q.Y))).ToList(), true, p.Capa);
                }

                RayarCorte(pl, isla, p);
            }

            // La soldadura se rellena sobre su contorno: SOLID de color 9 con 70 de transparencia.
            if (p.Relleno && pl is not null && p.Cerrado && puntos.Count >= 3)
            {
                Rellenar(pl, p.Capa, ConexionAceroCapas.PatronSoldadura, ConexionAceroCapas.EscalaSoldadura * _f,
                    ConexionAceroCapas.ColorSoldadura, ConexionAceroCapas.TransparenciaSoldadura);
            }
        }

        foreach (var c in v.Circulos)
        {
            Circulo(X(c.X), Y(c.Y), c.R * _escala, c.Capa);
        }

        foreach (var c in v.Cotas)
        {
            Cota(X(c.X1), Y(c.Y1), X(c.X2), Y(c.Y2), c.Nx * desfase, c.Ny * desfase, c.Texto);
        }

        // Las dos columnas de llamadas, centradas a lo alto en el dibujo.
        void Columna(List<LlamadaConexion> lista, double xCodo, bool derecha)
        {
            var total = lista.Sum(Paso);
            var yl = y - ((alto - total) / 2);

            foreach (var l in lista)
            {
                var paso = Paso(l);
                var ym = yl - (paso / 2);
                var px = X(l.X);
                var py = Y(l.Y);

                if (l.Simbolo is { } tipo)
                {
                    var hs = HSimbolo;
                    var r = SimbolosSoldadura.Apuntado(tipo, px, py, xCodo, ym, hs, l.Tamano, l.Electrodo, derecha,
                        ConexionAceroCapas.LargoFlecha * _f);
                    DibujarSimbolo(r);
                    var largo = SimbolosSoldadura.LargoApuntado(hs, l.Electrodo) + (0.6 * h);
                    Texto(Partir(Mayuscula(l.Descripcion)), derecha ? xCodo + largo : xCodo - largo, ym, h, CapaConexion.Texto, anclaje: derecha ? 4 : 6);
                }
                else
                {
                    var xt = derecha ? xCodo + (0.4 * h) : xCodo - (0.4 * h);
                    Linea(px, py, xCodo, ym, CapaConexion.Texto);
                    Circulo(px, py, ConexionAceroCapas.RadioPunto * _f, CapaConexion.Texto);
                    Texto(Partir(Mayuscula(l.Texto)), xt, ym, h, CapaConexion.Texto, anclaje: derecha ? 4 : 6);
                }

                yl -= paso;
            }
        }

        Columna(a.Izquierda, x + a.AnchoIzq - (1.5 * h), derecha: false);
        Columna(a.Derecha, xGeo + a.AnchoGeo + a.Holgura + (1.5 * h), derecha: true);

        Texto(v.Titulo, xGeo + (a.AnchoGeo / 2), y - alto - (0.4 * hVista), hVista, CapaConexion.Texto, anclaje: 2);
        return (a.Ancho, alto + (2.2 * hVista));
    }

    /// <summary>Un renglon de la simbologia: leader, simbolo, rellenos, circulo y textos.</summary>
    private void DibujarSimbolo(SimbolosSoldadura.Renglon r)
    {
        var capa = CapaConexion.Soldadura;
        Polilinea(Pares(r.Leader), false, capa);

        foreach (var a in r.Abiertas)
        {
            Polilinea(Pares(a), false, capa);
        }

        foreach (var c in r.Cerradas)
        {
            Polilinea(Pares(c), true, capa);
        }

        foreach (var f in r.Rellenas)
        {
            if (Polilinea(Pares(f), true, capa) is { } pl)
            {
                // Las flechas y la bandera van llenas: el SOLID de siempre.
                Rellenar(pl, capa, "SOLID", 1.0);
            }
        }

        if (r.Circulo is { } c0)
        {
            Circulo(c0.X, c0.Y, c0.R, capa);
        }

        foreach (var t in r.Textos)
        {
            // Los anclajes del TEXT de la simbologia, como MTEXT: 9 izquierda, 10 centro, 11 derecha.
            var anclaje = t.Anclaje switch { 10 => 5, 11 => 6, _ => 4 };
            Texto(t.S, t.X, t.Y, t.Altura, CapaConexion.Texto, anclaje);
        }
    }

    private static List<(double X, double Y)> Pares(double[] a)
    {
        var l = new List<(double, double)>();

        for (var i = 0; i + 1 < a.Length; i += 2)
        {
            l.Add((a[i], a[i + 1]));
        }

        return l;
    }

    // =====================================================================
    //  Entidades
    // =====================================================================

    private static double[] P(double x, double y) => new[] { x, y, 0.0 };

    private int Contar()
    {
        if (_grabado is not null)
        {
            return _grabado.Count;
        }

        try
        {
            return AcadConnection.Retry(() => (int)_ms.Count);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void Capear(dynamic e, CapaConexion capa)
    {
        e.Layer = ConexionAceroCapas.Nombre(capa);

        if (ConexionAceroCapas.TipoDeLinea(capa) is not null)
        {
            e.LinetypeScale = ConexionAceroCapas.EscalaLineas * _f;
        }
    }

    private void Linea(double x1, double y1, double x2, double y2, CapaConexion capa)
    {
        // Los perfiles van con ancho global (PEDIT Width): una LINE no lo tiene, asi que van como
        // polilinea de dos puntos.
        if (ConexionAceroCapas.EsPerfil(capa))
        {
            Polilinea(new[] { (x1, y1), (x2, y2) }, false, capa);
            return;
        }

        if (_grabado is not null)
        {
            _grabado.Add(new TrazoConexion(TipoTrazo.Linea, capa, new[] { x1, y1, x2, y2 }));
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic l = _ms.AddLine(P(x1, y1), P(x2, y2));
                Capear(l, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Linea", ex);
        }
    }

    private object? Polilinea(IReadOnlyList<(double X, double Y)> puntos, bool cerrada, CapaConexion capa)
    {
        if (puntos.Count < 2)
        {
            return null;
        }

        var arreglo = new double[puntos.Count * 2];

        for (var i = 0; i < puntos.Count; i++)
        {
            arreglo[2 * i] = puntos[i].X;
            arreglo[(2 * i) + 1] = puntos[i].Y;
        }

        var grueso = ConexionAceroCapas.EsPerfil(capa) ? ConexionAceroCapas.GruesoDePerfil * _f : 0;

        if (_grabado is not null)
        {
            var t = new TrazoConexion(TipoTrazo.Polilinea, capa, arreglo, cerrada && puntos.Count >= 3, Grueso: grueso);
            _grabado.Add(t);
            return t;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(arreglo);
                pl.Closed = cerrada && puntos.Count >= 3;

                if (grueso > 0)
                {
                    pl.ConstantWidth = grueso;
                }

                Capear(pl, capa);
                return (object?)pl;
            });
        }
        catch (Exception ex)
        {
            Fallo("Polilinea", ex);
            return null;
        }
    }

    /// <param name="color">El color ACI del relleno; 0 = por capa.</param>
    /// <param name="transparencia">De 0 a 90; 0 = sin transparencia.</param>
    /// <param name="isla">El contorno del hueco (el del tubo), o null.</param>
    private void Rellenar(object contorno, CapaConexion capa, string patron, double escala, int color = 0, double transparencia = 0,
        object? isla = null)
    {
        if (_grabado is not null)
        {
            if (contorno is TrazoConexion t)
            {
                _grabado.Add(t with
                {
                    Tipo = TipoTrazo.Relleno, Capa = capa, Texto = patron, Color = color, Transparencia = transparencia, Grueso = 0,
                    Isla = (isla as TrazoConexion)?.Puntos
                });
            }

            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic hh = _ms.AddHatch(0, patron, false);

                var ok = AcadArreglos.Llamar($"AppendOuterLoop del hatch '{patron}'", new[] { contorno },
                    arr => { hh.AppendOuterLoop(arr); }, Fallo, Nota);

                if (!ok)
                {
                    try
                    {
                        hh.Delete();
                    }
                    catch (Exception)
                    {
                        // Ya no estaba.
                    }

                    return;
                }

                if (isla is not null)
                {
                    AcadArreglos.Llamar($"AppendInnerLoop del hatch '{patron}'", new[] { isla },
                        arr => { hh.AppendInnerLoop(arr); }, Fallo, Nota);
                }

                if (!string.Equals(patron, "SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    hh.PatternScale = escala;
                }

                hh.Layer = ConexionAceroCapas.Nombre(capa);

                if (color is > 0 and < 256)
                {
                    hh.Color = color;
                }

                if (transparencia > 0)
                {
                    try
                    {
                        // La transparencia de la entidad, como en la paleta de propiedades: «70».
                        hh.EntityTransparency = Math.Round(Math.Min(90, transparencia)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex)
                    {
                        Nota("La transparencia del relleno de la soldadura no se pudo poner (" + ex.Message + "): el AutoCAD es anterior a 2011.");
                    }
                }

                hh.Evaluate();
            });
        }
        catch (Exception ex)
        {
            Fallo($"Hatch '{patron}'", ex);
        }
    }

    private void Circulo(double x, double y, double r, CapaConexion capa)
    {
        if (r <= 0)
        {
            return;
        }

        if (_grabado is not null)
        {
            _grabado.Add(new TrazoConexion(TipoTrazo.Circulo, capa, new[] { x, y }, Radio: r));
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic c = _ms.AddCircle(P(x, y), r);
                Capear(c, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Circulo", ex);
        }
    }

    /// <summary>
    /// Un MTEXT en (x, y) con su punto de anclaje: 1 arriba a la izquierda, 2 arriba al centro, 4 al
    /// medio a la izquierda, 5 al centro, 6 al medio a la derecha.
    /// </summary>
    private void Texto(string texto, double x, double y, double alto, CapaConexion capa, int anclaje = 1) =>
        Texto(new[] { texto }, x, y, alto, capa, anclaje);

    /// <summary>Varios renglones en UN MTEXT (\P), con el anclaje de arriba.</summary>
    private void Texto(IReadOnlyList<string> renglones, double x, double y, double alto, CapaConexion capa, int anclaje = 1)
    {
        var lista = renglones.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

        if (lista.Count == 0)
        {
            return;
        }

        if (_grabado is not null)
        {
            _grabado.Add(new TrazoConexion(TipoTrazo.Texto, capa, new[] { x, y }, Texto: string.Join("\n", lista), Alto: alto, Anclaje: anclaje));
            return;
        }

        var contenido = string.Join("\\P", lista.Select(Escapar));

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic t = _ms.AddMText(P(x, y), 0.0, contenido);
                t.AttachmentPoint = anclaje;
                t.InsertionPoint = P(x, y);
                t.StyleName = ConexionAceroCapas.EstiloTexto;
                t.Height = alto;
                Capear(t, capa);
            });
        }
        catch (Exception ex)
        {
            Fallo("Texto", ex);
        }
    }

    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

    private void Cota(double x1, double y1, double x2, double y2, double dx, double dy, string texto)
    {
        if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 1e-9)
        {
            return;
        }

        if (_grabado is not null)
        {
            _grabado.Add(new TrazoConexion(TipoTrazo.Cota, CapaConexion.Cota,
                new[] { x1, y1, x2, y2, ((x1 + x2) / 2) + dx, ((y1 + y2) / 2) + dy }, Texto: texto,
                Alto: ConexionAceroCapas.AltoCota * _f));
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimAligned(P(x1, y1), P(x2, y2), P(((x1 + x2) / 2) + dx, ((y1 + y2) / 2) + dy));
                d.Layer = ConexionAceroCapas.Nombre(CapaConexion.Cota);
                d.StyleName = ConexionAceroCapas.EstiloCota;

                // El numero es el de la hoja, en cm: no depende de las unidades del dibujo.
                d.TextOverride = texto;
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota", ex);
        }
    }

    // =====================================================================

    private void Fallo(string operacion, Exception ex)
    {
        var e = ex;

        while (e is TargetInvocationException && e.InnerException is not null)
        {
            e = e.InnerException;
        }

        var linea = operacion + " -> " + e.GetType().Name + ": " + e.Message.Replace(Environment.NewLine, " ").Trim();

        if (!_fallos.Contains(linea))
        {
            _fallos.Add(linea);
        }
    }

    private void Nota(string texto)
    {
        if (!_notas.Contains(texto))
        {
            _notas.Add(texto);
        }
    }
}
