using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadLink.Revit.Nucleo;

namespace CadLink.Revit;

/// <summary>Lo que paso al armar, para el informe.</summary>
public sealed class ResultadoArmado
{
    /// <summary>Piezas que quedaron armadas.</summary>
    public int Piezas { get; set; }

    /// <summary>Varillas creadas, contando cada juego de estribos como uno.</summary>
    public int Varillas { get; set; }

    /// <summary>Piezas de CadLink que el archivo no arma: su seccion no tiene fila en la tabla.</summary>
    public int SinArmado { get; set; }

    /// <summary>Tipos de varilla o de gancho que hubo que crear porque no estaban.</summary>
    public List<string> TiposCreados { get; } = new();

    public List<string> Errores { get; } = new();

    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Pone el <b>armado nativo de Revit</b> -<c>Rebar</c>- en las trabes y columnas que el
/// complemento ya modelo, con el armado que exporta CadLink.
/// </summary>
/// <remarks>
/// <para>
/// <b>Que se arma.</b> Las corridas y laterales, los bastones y los estribos por zonas, de las
/// trabes horizontales y las columnas verticales. Es la fase 1: grapas, estribo diamante,
/// zuncho y piezas inclinadas quedan fuera, y se dice.
/// </para>
/// <para>
/// <b>Como reconoce sus piezas.</b> Por la marca de <i>Comentarios</i> que puso el modelado,
/// <c>CadLink|Trabe|B12|Story1</c>, que es la misma llave de la barra en el archivo. El armado
/// creado lleva su propia marca, <c>CadLink|Armado|</c> mas esa llave, y por eso volver a armar
/// <b>rehace</b> el de esa pieza en lugar de duplicarlo. El armado puesto a mano no se toca.
/// </para>
/// <para>
/// Lo que se decide -donde va cada varilla, hacia donde dobla su gancho- vive en el nucleo,
/// <see cref="PlanDeArmado"/>, y tiene pruebas. Aqui solo se llama a la API.
/// </para>
/// </remarks>
internal static class Armador
{
    /// <summary>El principio de la marca del armado que crea CadLink.</summary>
    public const string MarcaArmado = Llave.Prefijo + "|Armado|";

    public static ResultadoArmado Ejecutar(Document doc, ModeloJson modelo)
    {
        var r = new ResultadoArmado();

        var armados = modelo.Armados
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // La barra del archivo de cada llave. Solo las que traen armado.
        var barras = new Dictionary<string, BarraJson>(StringComparer.Ordinal);

        foreach (var b in modelo.Barras)
        {
            if (b.Armado is not null)
            {
                barras[Llave.De(b)] = b;
            }
        }

        using var t = new Transaction(doc, "Armado de CadLink");
        t.Start();

        // El mismo manejador que el modelado: que un error de una varilla no tire la
        // transaccion entera desde el cuadro de Revit.
        var manejador = new SinCuadros();
        var opciones = t.GetFailureHandlingOptions();

        opciones.SetFailuresPreprocessor(manejador);
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            var tipos = new TiposDeArmado(doc, r);
            var viejo = ArmadoViejo(doc);

            foreach (var inst in PiezasDeCadLink(doc))
            {
                var llave = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?
                    .AsString() ?? string.Empty;

                if (!barras.TryGetValue(llave, out var barra))
                {
                    r.SinArmado++;
                    continue;
                }

                if (!armados.TryGetValue(barra.Armado!.Id, out var armado))
                {
                    r.Errores.Add($"«{barra.Etiqueta}»: el archivo no trae el armado «{barra.Armado.Id}»");
                    continue;
                }

                ArmarPieza(doc, inst, llave, barra, armado, tipos, viejo, r);
            }

            t.Commit();

            if (manejador.ElementosBorrados > 0)
            {
                r.Errores.Add($"«Revit»: {manejador.ElementosBorrados} varilla(s) se borraron "
                              + "porque Revit dio un error que no se puede ignorar.");
            }

            foreach (var m in manejador.Motivos.Take(4))
            {
                r.Avisos.Add("Revit dijo: " + m);
            }
        }
        catch (Exception e)
        {
            t.RollBack();
            r.Errores.Add("Se deshizo el armado completo por un fallo general: " + e.Message);
        }

        return r;
    }

    /// <summary>Las trabes y columnas del proyecto que llevan marca de CadLink.</summary>
    private static List<FamilyInstance> PiezasDeCadLink(Document doc)
    {
        var res = new List<FamilyInstance>();

        foreach (var cat in new[]
                 {
                     BuiltInCategory.OST_StructuralFraming,
                     BuiltInCategory.OST_StructuralColumns
                 })
        {
            foreach (var e in new FilteredElementCollector(doc)
                         .OfCategory(cat)
                         .WhereElementIsNotElementType())
            {
                if (e is FamilyInstance fi
                    && Llave.EsNuestra(fi.get_Parameter(
                        BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()))
                {
                    res.Add(fi);
                }
            }
        }

        return res;
    }

    /// <summary>El armado que CadLink ya habia puesto, agrupado por la llave de su pieza.</summary>
    private static Dictionary<string, List<ElementId>> ArmadoViejo(Document doc)
    {
        var res = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);

        foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(Rebar)))
        {
            var marca = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();

            if (marca is null || !marca.StartsWith(MarcaArmado, StringComparison.Ordinal))
            {
                continue;
            }

            var llave = marca.Substring(MarcaArmado.Length);

            if (!res.TryGetValue(llave, out var lista))
            {
                lista = new List<ElementId>();
                res[llave] = lista;
            }

            lista.Add(e.Id);
        }

        return res;
    }

    private static void ArmarPieza(
        Document doc, FamilyInstance inst, string llave, BarraJson barra, ArmadoJson armado,
        TiposDeArmado tipos, Dictionary<string, List<ElementId>> viejo, ResultadoArmado r)
    {
        var etiqueta = $"«{barra.Etiqueta}» ({armado.Id})";

        var datosHost = RebarHostData.GetRebarHostData(inst);

        if (datosHost is null || !datosHost.IsValidHost())
        {
            r.Errores.Add($"«no admite armado»: {etiqueta}. Revit solo arma piezas de concreto: "
                          + "revisa el material estructural de su familia");
            return;
        }

        var marco = Marco(inst, armado, etiqueta, r);

        if (marco is null)
        {
            return;
        }

        // Lo de la vez anterior, fuera: volver a armar REHACE, no duplica.
        if (viejo.TryGetValue(llave, out var ids) && ids.Count > 0)
        {
            doc.Delete(ids);
        }

        var varillas = PlanDeArmado.Armar(armado, barra.Armado!, marco);
        var hechas = 0;

        foreach (var v in varillas)
        {
            try
            {
                if (Crear(doc, inst, v, llave, tipos) is not null)
                {
                    hechas++;
                }
            }
            catch (Exception e)
            {
                // Agrupado por causa en el informe: una varilla que Revit no acepta suele
                // fallar igual en todas las piezas.
                r.Errores.Add($"«{v.Que} {v.Clave}»: {etiqueta}: {e.Message}");
            }
        }

        if (hechas > 0)
        {
            r.Piezas++;
            r.Varillas += hechas;
        }

        if (barra.Armado!.Por == "medidas")
        {
            r.Avisos.Add($"«armada por sus medidas»: {etiqueta}. Su seccion «{barra.Seccion.Nombre}» "
                         + "no se llama como ninguna fila; se uso la unica fila que mide lo mismo");
        }
    }

    /// <summary>
    /// Donde esta la pieza, en metros. Nulo -y dicho- si es de una forma que la fase 1 no arma.
    /// </summary>
    private static MarcoPieza? Marco(
        FamilyInstance inst, ArmadoJson armado, string etiqueta, ResultadoArmado r)
    {
        var caja = inst.get_BoundingBox(null);

        if (caja is null)
        {
            r.Errores.Add($"«sin geometria»: {etiqueta}");
            return null;
        }

        double M(double internas) => Unidades.AMetros(internas);

        V3 P(XYZ p) => new(M(p.X), M(p.Y), M(p.Z));

        if (armado.EsHorizontal)
        {
            if (inst.Location is not LocationCurve lc || lc.Curve is not Line linea)
            {
                r.Errores.Add($"«trabe sin linea recta»: {etiqueta}");
                return null;
            }

            var p1 = P(linea.GetEndPoint(0));
            var p2 = P(linea.GetEndPoint(1));
            var enPlanta = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));

            // Fase 1: solo trabes horizontales. Con pendiente, la seccion ya no se apoya en
            // la cara de abajo de la caja, y armarla asi la dejaria torcida.
            if (enPlanta < 1e-6 || Math.Abs(p2.Z - p1.Z) / enPlanta > 0.02)
            {
                r.Avisos.Add($"«inclinada, no se armo»: {etiqueta}. La fase 1 arma trabes horizontales");
                return null;
            }

            return MarcoPieza.DeTrabe(p1, p2, M(caja.Min.Z), armado.BaseCm);
        }

        if (inst.Location is not LocationPoint lp)
        {
            r.Avisos.Add($"«columna inclinada, no se armo»: {etiqueta}. La fase 1 arma columnas verticales");
            return null;
        }

        return MarcoPieza.DeColumna(
            P(lp.Point), P(inst.HandOrientation), P(inst.FacingOrientation),
            M(caja.Min.Z), M(caja.Max.Z), armado.BaseCm, armado.AlturaCm);
    }

    /// <summary>Crea una varilla, o un juego de estribos, en la pieza.</summary>
    private static Rebar? Crear(
        Document doc, FamilyInstance host, VarillaArmada v, string llave, TiposDeArmado tipos)
    {
        var tipo = tipos.Barra(v.Clave, v.DiamM);

        var curvas = new List<Curve>();

        for (var i = 1; i < v.Puntos.Count; i++)
        {
            curvas.Add(Line.CreateBound(Punto(v.Puntos[i - 1]), Punto(v.Puntos[i])));
        }

        var estilo = v.Estilo == EstiloVarilla.Estribo ? RebarStyle.StirrupTie : RebarStyle.Standard;

        // POSICIONALES, como todas las llamadas a la Revit API de este complemento: los nombres
        // de sus parametros no se pueden comprobar aqui. Ver validar.py §26.
        var rebar = Rebar.CreateFromCurves(
            doc,
            estilo,
            tipo,
            tipos.Gancho(v.GanchoIni),
            tipos.Gancho(v.GanchoFin),
            host,
            Punto(v.Normal, false),
            curvas,
            Orientacion(v.LadoIni),
            Orientacion(v.LadoFin),
            true,
            true);

        if (rebar is null)
        {
            return null;
        }

        rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(MarcaArmado + llave);

        if (v.Cuenta > 1 && v.PasoM > 0)
        {
            // El juego se reparte hacia el lado de la normal, que en el estribo es el eje de
            // la pieza: del primero hacia el final.
            rebar.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(
                v.Cuenta, Unidades.AInternas(v.PasoM), true, true, true);
        }

        return rebar;
    }

    private static RebarHookOrientation Orientacion(LadoGancho lado) =>
        lado == LadoGancho.Izquierda ? RebarHookOrientation.Left : RebarHookOrientation.Right;

    /// <summary>Un punto -o un vector, sin convertir- de metros a unidades de Revit.</summary>
    private static XYZ Punto(V3 p, bool esPunto = true) =>
        esPunto
            ? new XYZ(Unidades.AInternas(p.X), Unidades.AInternas(p.Y), Unidades.AInternas(p.Z))
            : new XYZ(p.X, p.Y, p.Z);

    /// <summary>
    /// Los tipos de varilla y de gancho del proyecto, buscados una vez y creados si faltan.
    /// </summary>
    private sealed class TiposDeArmado
    {
        private readonly Document _doc;
        private readonly ResultadoArmado _r;
        private readonly List<RebarBarType> _barras;
        private readonly Dictionary<GanchoVarilla, RebarHookType?> _ganchos = new();

        public TiposDeArmado(Document doc, ResultadoArmado r)
        {
            _doc = doc;
            _r = r;
            _barras = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType))
                .OfType<RebarBarType>()
                .ToList();
        }

        /// <summary>
        /// El tipo de varilla: el que se llame como la clave (<c>#4</c>), o el que tenga ese
        /// diametro con medio milimetro de tolerancia. Si no hay, se crea.
        /// </summary>
        public RebarBarType Barra(string clave, double diamM)
        {
            var porNombre = _barras.FirstOrDefault(b =>
                EmparejarArmado.Normalizar(b.Name) == EmparejarArmado.Normalizar(clave));

            if (porNombre is not null)
            {
                return porNombre;
            }

            var porDiametro = _barras.FirstOrDefault(b =>
                Math.Abs(Unidades.AMetros(b.BarNominalDiameter) - diamM) <= 0.0005);

            if (porDiametro is not null)
            {
                return porDiametro;
            }

            var nuevo = RebarBarType.Create(_doc);
            nuevo.BarNominalDiameter = Unidades.AInternas(diamM);
            nuevo.BarModelDiameter = Unidades.AInternas(diamM);
            nuevo.Name = clave;

            _barras.Add(nuevo);
            _r.TiposCreados.Add($"varilla {clave} ({diamM * 1000:0.#} mm)");

            return nuevo;
        }

        /// <summary>
        /// El gancho: <b>los de CadLink</b>, creados una vez con su nombre. Se crean propios en
        /// vez de usar los de la plantilla para que midan lo que miden en el plano: 12
        /// diametros el de 90° y 6 el de 135°, aunque la plantilla traiga otros.
        /// </summary>
        public RebarHookType? Gancho(GanchoVarilla g)
        {
            if (g == GanchoVarilla.Ninguno)
            {
                return null;
            }

            if (_ganchos.TryGetValue(g, out var hecho))
            {
                return hecho;
            }

            var (nombre, grados, multiplo, estilo) = g == GanchoVarilla.De90
                ? ("CadLink - 90° (12 db)", 90.0, 12.0, RebarStyle.Standard)
                : ("CadLink - 135° estribo (6 db)", 135.0, 6.0, RebarStyle.StirrupTie);

            var tipo = new FilteredElementCollector(_doc)
                .OfClass(typeof(RebarHookType))
                .OfType<RebarHookType>()
                .FirstOrDefault(h => h.Name == nombre);

            if (tipo is null)
            {
                tipo = RebarHookType.Create(_doc, grados * Math.PI / 180, multiplo);
                tipo.Name = nombre;
                tipo.Style = estilo;
                _r.TiposCreados.Add("gancho " + nombre);
            }

            _ganchos[g] = tipo;
            return tipo;
        }
    }
}
