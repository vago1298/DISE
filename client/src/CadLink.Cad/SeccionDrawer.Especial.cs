namespace CadLink.Cad;

/// <summary>
/// Secciones de forma ESPECIAL -en L, en T o con cualquier contorno-: la parte de
/// <see cref="SeccionDrawer"/> que las dibuja.
/// </summary>
/// <remarks>
/// Va aparte por lo mismo que la circular: el dibujo rectangular esta lleno de reglas de esquinas,
/// lechos y ganchos que aqui no aplican. La geometria -contorno, estribo y varillas- sale de
/// <see cref="GeometriaEspecial"/>, que no sabe de AutoCAD y se comprueba sola; aqui solo se dibuja.
/// Usa las mismas capas, el mismo hatch en dos partes, el mismo bloque con el nombre del ID y el
/// mismo «redibujar en su sitio» que las demas secciones.
/// </remarks>
public sealed partial class SeccionDrawer
{
    /// <summary>Dibuja la seccion especial y la agrupa en su bloque. Devuelve cuantas entidades creo.</summary>
    public int DibujarEspecial(SeccionEspecialCad s, double xIzquierda, double yAbajo)
    {
        double[]? destino = null;
        InicioUltima = -1;

        if (BloqueYaExiste(s.Id))
        {
            if (!Redibujar)
            {
                _saltadas.Add(s.Id);
                return 0;
            }

            destino = VolverASuSitio ? PuntoDeInsercion(s.Id) : null;

            if (!BorrarSeccion(s.Id))
            {
                _saltadas.Add(s.Id);
                return 0;
            }

            _redibujadas.Add(s.Id);
        }

        UltimaFueASuSitio = destino is not null;

        var inicio = (int)AcadConnection.Retry(() => (int)_ms.Count);
        InicioUltima = inicio;

        _varSup.Clear();
        _varInf.Clear();
        _varLat.Clear();
        _tramosEstribo.Clear();

        var conFondoSolido = s.Modo != ModoSeccion.Tipo1SinRelleno;

        if (s.Contorno.Count < 3)
        {
            _log.Add($"Seccion especial '{s.Id}': su forma no es un contorno valido.");
            return 0;
        }

        double[] Puntos(IEnumerable<(double X, double Y)> p) =>
            p.SelectMany(v => new[] { xIzquierda + (v.X * _escala), yAbajo + (v.Y * _escala) }).ToArray();

        // ---------- Concreto ----------
        var plConcreto = Polilinea(Puntos(s.Contorno), "CONCRETO");

        // ---------- Estribo: el contorno corrido el recubrimiento, y su cara de dentro ----------
        var contorno = new List<object>();
        var estExt = GeometriaEspecial.HaciaDentro(s.Contorno, s.RecubrimientoCm);
        var estInt = estExt is null ? null : GeometriaEspecial.HaciaDentro(s.Contorno, s.RecubrimientoCm + s.Estribo.Cm);

        object? plEstExt = null;
        object? plEstInt = null;

        if (s.Estribo.Existe && estExt is not null && estInt is not null)
        {
            plEstExt = Polilinea(Puntos(estExt), "ESTRIBOS");
            plEstInt = Polilinea(Puntos(estInt), "ESTRIBOS");

            if (plEstExt is not null) { contorno.Add(plEstExt); }
            if (plEstInt is not null) { contorno.Add(plEstInt); }

            if (conFondoSolido && plEstExt is not null && plEstInt is not null)
            {
                var relleno = Hatch("SOLID", 1, plEstExt, new List<object> { plEstInt }, "ESTRIBOS", ColorRellenoEstribo);

                if (relleno is not null)
                {
                    AlFondo(new List<object> { relleno });
                }
            }
        }
        else if (s.Estribo.Existe)
        {
            _log.Add($"Seccion especial '{s.Id}': con recubrimiento de {s.RecubrimientoCm:0.#} cm el estribo no cabe en la forma, asi que no se dibujo.");
        }

        // ---------- Varillas ----------
        var varillas = GeometriaEspecial.Varillas(s);
        var circulos = new List<object>();

        if (varillas.Count == 0)
        {
            _log.Add($"Seccion especial '{s.Id}': no caben las varillas dentro del estribo.");
        }
        else if (GeometriaEspecial.HuecoMinimo(varillas) < 0)
        {
            _log.Add($"Seccion especial '{s.Id}': hay varillas que se enciman; baja el numero o sube la separacion maxima.");
        }

        foreach (var v in varillas)
        {
            var c = Varilla(xIzquierda + (v.X * _escala), yAbajo + (v.Y * _escala), v.DiamCm * _escala / 2, v.Clave);

            if (c is not null)
            {
                circulos.Add(c);
            }
        }

        var rellenosVarilla = new List<object>();
        RellenarVarillas(circulos, rellenosVarilla);

        // ---------- Hatch de concreto en dos partes, como la rectangular ----------
        if (plConcreto is not null)
        {
            var creados = new List<object>();

            if (plEstExt is not null && plEstInt is not null)
            {
                ParteHatch(plConcreto, new List<object> { plEstExt }, creados, conFondoSolido);
                ParteHatch(plEstInt, circulos, creados, conFondoSolido);
            }
            else
            {
                ParteHatch(plConcreto, circulos, creados, conFondoSolido);
            }

            if (creados.Count > 0)
            {
                AlFondo(creados);
            }
        }

        if (conFondoSolido)
        {
            if (plConcreto is not null) { Negro(plConcreto); }
            foreach (var e in contorno) { Negro(e); }
            foreach (var c in circulos) { Negro(c); }
        }

        if (contorno.Count > 0)
        {
            AlFrente(contorno);
        }

        var alFrente = new List<object>(rellenosVarilla);
        alFrente.AddRange(circulos);
        AlFrente(alFrente);

        // ---------- Cotas de la caja y rotulo ----------
        var ancho = s.Contorno.Max(v => v.X);
        var alto = s.Contorno.Max(v => v.Y);

        Cotas(xIzquierda, yAbajo, ancho * _escala, alto * _escala);
        RotuloEspecial(s, varillas, xIzquierda + (ancho * _escala / 2), yAbajo - (0.06 * _f));

        var fin = (int)AcadConnection.Retry(() => (int)_ms.Count);

        if (!string.IsNullOrWhiteSpace(s.Id))
        {
            Bloquear(s.Id, inicio, fin, destino);
        }

        return fin - inicio;
    }

    /// <summary>Los renglones del rotulo, los mismos que pinta la vista previa de CadLink.</summary>
    public static List<string> LineasDeRotuloEspecial(SeccionEspecialCad s, IReadOnlyList<VarillaEspecial> varillas)
    {
        var lineas = new List<string> { $"{s.Elemento} \"{s.Id}\"", s.Descripcion };

        foreach (var g in varillas.GroupBy(v => v.Clave).OrderByDescending(g => g.Count()))
        {
            lineas.Add($"{g.Count()} Vars. {g.Key}");
        }

        if (s.Estribo.Existe)
        {
            lineas.Add(s.SeparacionEstribo.Trim().Length > 0
                ? $"E. {s.Estribo.Clave} @ {s.SeparacionEstribo.Trim()} cm"
                : $"E. {s.Estribo.Clave}");
        }

        lineas.Add($"Rec. {s.RecubrimientoCm:0.#} cm");

        if (s.Fc.Trim().Length > 0)
        {
            lineas.Add($"f'c = {s.Fc.Trim()} kg/cm²");
        }

        if (s.Escala.Trim().Length > 0)
        {
            lineas.Add($"Esc. 1:{s.Escala.Trim()}");
        }

        return lineas.Where(l => l.Trim().Length > 0).ToList();
    }

    private void RotuloEspecial(SeccionEspecialCad s, IReadOnlyList<VarillaEspecial> varillas, double xCentro, double yBase)
    {
        var texto = string.Join("\\P", LineasDeRotuloEspecial(s, varillas));

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic mt = _ms.AddMText(new[] { xCentro, yBase, 0d }, 0.45 * _f, texto);
                mt.Height = EstiloSecciones.Numero("alto.rotulo.seccion") * _f;
                mt.AttachmentPoint = 2;
                mt.InsertionPoint = new[] { xCentro, yBase, 0d };
                mt.Layer = "ROTULOS";
                mt.Color = PorCapa;
                mt.StyleName = EstiloTexto;
                mt.Update();
            });
        }
        catch (Exception ex)
        {
            Fallo("Rotulo de la seccion especial", ex);
        }
    }
}
