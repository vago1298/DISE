using CadLink.App.Models;
using CadLink.Cad;
using CadLink.Revit.Nucleo;

namespace CadLink.App;

/// <summary>
/// El <b>armado</b> que viaja al complemento de Revit, dentro del mismo
/// <c>.cadlink-modelo.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sale de la tabla de secciones de concreto con <b>las mismas funciones</b> que dibujan en
/// AutoCAD y en la vista previa: las posiciones de las varillas, la cama de los bastones, los
/// estribos por zonas y los tramos de los bastones. Revit no vuelve a decidir nada; solo
/// coloca. Si cambia una regla aqui, cambia en los tres sitios a la vez.
/// </para>
/// <para>
/// Fase 1: trabes, contratrabes, columnas y dados <b>rectangulares</b>, con corridas,
/// laterales, bastones y estribos. Sin grapas, diamante ni zuncho.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// Pone el armado en el modelo que va a Revit. Devuelve cuantas barras quedaron armadas.
    /// </summary>
    private int AgregarArmado(ModeloJson m)
    {
        var armados = new List<ArmadoJson>();
        var filas = new Dictionary<string, SeccionConcretoRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in _datos.SeccionesConcreto)
        {
            var tipo = TipoDe(s.Elemento, s.Id);
            var id = (s.Id ?? string.Empty).Trim();

            if (tipo is null || s.EsCircular || id.Length == 0 || filas.ContainsKey(id))
            {
                continue;
            }

            armados.Add(ArmadoDe(s, tipo.Value));
            filas[id] = s;
        }

        if (armados.Count == 0)
        {
            return 0;
        }

        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sinFila = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var armadas = 0;

        foreach (var b in m.Barras)
        {
            if (b.Clase is not (ClasePieza.Trabe or ClasePieza.Columna)
                || b.Seccion.Forma != FormaSeccion.Rectangulo)
            {
                continue;
            }

            var hallado = EmparejarArmado.Buscar(
                b.Seccion.Nombre, b.Clase, b.Seccion.AnchoM, b.Seccion.PeralteM, armados);

            if (hallado.Armado is null)
            {
                // Una vez por seccion, no por barra: trescientas trabes iguales son UN aviso.
                if (sinFila.Add(b.Seccion.Nombre))
                {
                    m.Avisos.Add($"«armado sin fila»: {b.Seccion.Nombre}: {hallado.Motivo}");
                }

                continue;
            }

            var fila = filas[hallado.Armado.Id];
            var largo = Distancia(b.P1, b.P2);

            if (largo <= 0)
            {
                continue;
            }

            b.Armado = ArmadoDeBarra(fila, hallado.Armado, largo, hallado.Por);
            usados.Add(hallado.Armado.Id);
            armadas++;
        }

        m.Armados = armados.Where(a => usados.Contains(a.Id)).ToList();
        return armadas;
    }

    /// <summary>Lo que no depende de la longitud: la seccion y sus varillas.</summary>
    private static ArmadoJson ArmadoDe(SeccionConcretoRow s, TipoElemento tipo)
    {
        var rec = RecubrimientoDe(s);
        Varilla.TryDiametroCm(s.Estribo, out var de);

        var a = new ArmadoJson
        {
            Id = s.Id.Trim(),
            Tipo = tipo.ToString(),
            BaseCm = s.BaseCm,
            AlturaCm = s.AlturaCm,
            RecubrimientoCm = rec,
            ClaveEstribo = Varilla.Normalizar(s.Estribo),
            DiamEstriboCm = de
        };

        // LAS MISMAS POSICIONES que la vista previa y las grapas: TodasLasVarillas.
        foreach (var (refV, x, y, r) in TodasLasVarillas(s, de, rec))
        {
            var (clave, lecho) = refV.Lecho switch
            {
                LechoVarilla.EsquinaSuperior => (s.DiamEsqSup, "Superior"),
                LechoVarilla.IntermediaSuperior => (s.DiamIntSupEfectivo, "Superior"),
                LechoVarilla.EsquinaInferior => (s.DiamEsqInfEfectivo, "Inferior"),
                LechoVarilla.IntermediaInferior => (s.DiamIntInfEfectivo, "Inferior"),
                _ => (s.DiamInter, "Lateral")
            };

            a.Varillas.Add(new VarillaJson
            {
                Clave = Varilla.Normalizar(clave),
                DiamCm = r * 2,
                XCm = x,
                YCm = y,
                Lecho = lecho
            });
        }

        return a;
    }

    /// <summary>Lo que si depende de la longitud: estribos y bastones de UNA barra.</summary>
    private ArmadoBarraJson ArmadoDeBarra(
        SeccionConcretoRow s, ArmadoJson a, double largoM, string por)
    {
        var rec = RecubrimientoDe(s);
        Varilla.TryDiametroCm(s.Estribo, out var de);

        var sep = Separaciones(s.SeparacionCm);
        var tipo = TipoDe(s.Elemento, s.Id);

        // LOS MISMOS ESTRIBOS QUE EL ALZADO: CentrosDeAlzado, con la longitud de ESTA pieza.
        var centros = Estribos.CentrosDeAlzado(
            largoM, sep[0] / 100, sep[1] / 100, sep[2] / 100,
            vertical: !a.EsHorizontal,
            esColumna: tipo == TipoElemento.Columna);

        var bastones = LlevaBastones(s) ? BastonesCad(s) : new List<BastonCad>();

        if (a.EsHorizontal && bastones.Any(CadLink.Cad.Bastones.EsValido))
        {
            CadLink.Cad.Bastones.QuitarEstribosExtremos(centros);
        }

        var ab = new ArmadoBarraJson
        {
            Id = a.Id,
            Por = por,
            LargoM = largoM,
            EstribosM = centros
        };

        // Y LOS MISMOS BASTONES: su cama pegada al lecho, como en el corte, y sus tramos con la
        // longitud real, como en el alzado.
        var margen = MargenBastonesM(s);

        foreach (var bas in bastones)
        {
            var cama = CamaDeBaston(s, bas, de, rec);

            if (cama is null)
            {
                continue;
            }

            foreach (var t in CadLink.Cad.Bastones.Tramos(bas, largoM, margen))
            {
                ab.Bastones.Add(new TramoBastonJson
                {
                    Clave = bas.Var.Clave,
                    DiamCm = bas.Var.Cm,
                    Lecho = bas.Posicion == PosicionBaston.Superior ? "Superior" : "Inferior",
                    XsCm = cama.Value.Xs,
                    YCm = cama.Value.Y,
                    IniM = t.Ini,
                    FinM = t.Fin,
                    GanchoIni = t.GanchoIzq,
                    GanchoFin = t.GanchoDer
                });
            }
        }

        return ab;
    }

    private static double RecubrimientoDe(SeccionConcretoRow s) =>
        s.RecubrimientoCm > 0 ? s.RecubrimientoCm : 2.5;

    private static double Distancia(PuntoJson a, PuntoJson b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2));
}
