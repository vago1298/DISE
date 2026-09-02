namespace CadLink.Cad;

/// <summary>
/// El resto del detalle de la placa base: perfil, soldadura, cartabones, cotas, leaders,
/// rótulo y el bloque.
/// </summary>
public sealed partial class PlacaBaseDrawer
{
    // ======================================================================
    //  EL PERFIL DE LA COLUMNA
    // ======================================================================

    /// <summary>
    /// Dibuja el perfil centrado en la placa, pidiéndole la geometría a <see cref="TrazoAcero"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La geometría no se calcula aquí.</b> <c>TrazoAcero</c> ya traía portadas las nueve formas
    /// del manual IMCA con las mismas fórmulas que la macro trazaba a mano, así que se reutiliza.
    /// Duplicarlas habría dejado dos juegos de fórmulas para el mismo perfil.
    /// </para>
    /// <para>
    /// <c>TrazoAcero</c> entrega el perfil con su borde izquierdo en <c>x0</c> y su paño inferior en
    /// <c>y0</c>, así que se le pasa la esquina y no el centro. El giro de 90° se aplica después
    /// sobre los puntos, no pidiéndole otro trazo: así el giro es el mismo para las nueve formas.
    /// </para>
    /// </remarks>
    private List<object> DibujarPerfil(PlacaBaseCad p, double xc, double yc)
    {
        var creados = new List<object>();

        if (p.Perfil is null)
        {
            return creados;
        }

        var ancho = p.Perfil.AnchoDibujoCm * _escala;
        var alto = p.Perfil.AltoDibujoCm * _escala;

        var trazo = TrazoAcero.De(p.Perfil, xc - (ancho / 2), yc - (alto / 2), _escala);

        if (trazo is null)
        {
            Nota($"El perfil '{p.Seccion}' no trae medidas suficientes para dibujarse.");
            return creados;
        }

        var girar = GirarEstePerfil(p);

        foreach (var contorno in new[] { trazo.Exterior, trazo.Interior })
        {
            if (contorno is null)
            {
                continue;
            }

            var pts = girar ? ContornoDesplazado.Girar90(contorno.Puntos, xc, yc) : contorno.Puntos;

            var pl = Polilinea(pts, PlacaBaseCapas.Perfiles, contorno.Dobleces);

            if (pl is not null)
            {
                creados.Add(pl);
            }
        }

        foreach (var circulo in new[] { trazo.CircExterior, trazo.CircInterior })
        {
            if (circulo is null)
            {
                continue;
            }

            // Un círculo girado alrededor del centro del perfil queda donde estaba si su centro es
            // el mismo; si no lo es, se gira su centro.
            var (cx, cy) = girar
                ? ContornoDesplazado.Girar90Punto(circulo.Cx, circulo.Cy, xc, yc)
                : (circulo.Cx, circulo.Cy);

            var c = Circulo(cx, cy, circulo.R * 2, PlacaBaseCapas.Perfiles);

            if (c is not null)
            {
                creados.Add(c);
            }
        }

        return creados;
    }


    // Girar90 y Girar90Punto ya NO viven aquí: están en ContornoDesplazado, que es donde también
    // los usa PlacaBaseCad.PanoDeLaColumna. Con una copia en cada sitio, el día que cambie el giro
    // el perfil se dibujaría en una orientación y la soldadura lo rodearía en otra.

    /// <summary>Contorno ancho y rayado propio para las familias con forma de I.</summary>
    private void AcabadoPerfilI(List<object> perfil)
    {
        foreach (var ent in perfil)
        {
            try
            {
                AcadConnection.Retry(() =>
                {
                    dynamic e = ent;
                    e.Layer = PlacaBaseCapas.Perfiles;
                    e.Color = PorCapa;
                    e.ConstantWidth = PlacaBaseCapas.AnchoContornoPerfilI;
                    e.Update();
                });
            }
            catch (Exception ex)
            {
                Fallo("Ancho del contorno del perfil", ex);
            }
        }

        if (perfil.Count == 0)
        {
            return;
        }

        var hatch = Hatch(
            PlacaBaseCapas.PatronPerfilI, PlacaBaseCapas.EscalaHatchPerfilI,
            perfil[0], null, PlacaBaseCapas.Perfiles, PlacaBaseCapas.ColorHatchPerfilI);

        if (hatch is not null)
        {
            AlFondo(new List<object> { hatch });
        }
    }

    // ======================================================================
    //  LA SOLDADURA
    // ======================================================================

    /// <summary>
    /// La franja de soldadura alrededor del perfil, y su leader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La frontera sigue el CONTORNO del perfil</b>, no su rectángulo envolvente. La primera
    /// versión usaba la caja del perfil crecida el espesor, y para un tubo rectangular eso da lo
    /// mismo, pero para todo lo demás no: en un perfil I la caja no es una franja, es la caja
    /// entera rellena de rayado con la I dentro como isla. Se ve en el dibujo y no se parece a una
    /// soldadura.
    /// </para>
    /// <para>
    /// El desplazamiento lo hace <see cref="ContornoDesplazado"/>, aparte y sin COM, en lugar del
    /// <c>Offset</c> de AutoCAD que usaba la macro. La macro creaba una copia temporal del perfil,
    /// la desplazaba a los dos lados, medía las dos y se quedaba con la que creció —un baile que
    /// obligaba a dibujar el perfil <b>dos veces</b> para que un <c>Delete</c> no se llevara la
    /// columna buena—. Calculándolo no hace falta nada de eso y, sobre todo, se puede comprobar sin
    /// AutoCAD delante, que aquí es la única manera de comprobar algo.
    /// </para>
    /// <para>
    /// La frontera es <b>temporal</b>: se borra en cuanto el hatch está hecho, igual que en la
    /// macro. El hatch se crea no asociativo para que borrarla no se lo lleve.
    /// </para>
    /// </remarks>
    private void Soldadura(
        PlacaBaseCad p, List<object> perfil, ContornoDeColumna? contornoExterior,
        double xc, double yc, double xLef)
    {
        var t = p.SoldaduraCm * _escala;

        if (t <= 0 || perfil.Count == 0 || p.Perfil is null)
        {
            return;
        }

        // ---------- La frontera exterior: el paño del perfil corrido hacia fuera ----------
        object? frontera = null;

        // A DÓNDE APUNTA LA FLECHA: al MEDIO de la franja, no a su borde ni al centro de la pieza.
        // Se saca del contorno corrido la MITAD del espesor, así que el punto cae dentro del rayado
        // por construcción, sea el perfil el que sea.
        var puntaFlecha = (X: xc, Y: yc);

        if (contornoExterior?.Circulo is { } circulo)
        {
            // El tubo redondo y el macizo: la franja es un anillo, así que la frontera es la misma
            // circunferencia con el radio crecido.
            frontera = Circulo(circulo.Cx, circulo.Cy, (circulo.R + t) * 2, PlacaBaseCapas.Soldadura);

            puntaFlecha = (circulo.Cx - circulo.R - (t / 2), circulo.Cy);
        }
        else if (contornoExterior?.Puntos is { } puntos)
        {
            var fuera = ContornoDesplazado.HaciaFuera(puntos, t);

            if (fuera is null)
            {
                Nota($"No se pudo calcular la franja de soldadura del perfil '{p.Seccion}': su " +
                     "contorno no da para desplazarse. El perfil y las anclas se dibujaron igual.");
                return;
            }

            frontera = Polilinea(fuera, PlacaBaseCapas.Soldadura, contornoExterior.Dobleces);

            // El eje de la franja, que es el contorno corrido medio espesor. Apuntar al borde de
            // fuera dejaría la flecha justo en la línea, y al de dentro, encima del perfil.
            var medio = ContornoDesplazado.HaciaFuera(puntos, t / 2) ?? fuera;

            puntaFlecha = ContornoDesplazado.PuntoIzquierdo(medio);
        }
        else
        {
            // Sin paño no hay nada que rodear. Pasa si TrazoAcero no supo trazar el perfil, y en ese
            // caso ya se avisó al dibujarlo.
            return;
        }

        if (frontera is null)
        {
            return;
        }

        if (p.DibujarHatchSoldadura)
        {
            var hatch = Hatch(
                PlacaBaseCapas.PatronSoldadura, PlacaBaseCapas.EscalaHatchSoldadura,
                frontera, new List<object> { perfil[0] },
                PlacaBaseCapas.Soldadura, PlacaBaseCapas.ColorLineasSoldadura);

            if (hatch is not null)
            {
                AlFondo(new List<object> { hatch });
            }
        }

        // La frontera era auxiliar. El hatch no es asociativo, así que borrarla no lo afecta.
        Borrar(frontera);

        // ---------- El leader de la soldadura, a la izquierda ----------
        if (!p.DibujarLeaders)
        {
            return;
        }

        var separacion = Math.Max(11.0 * _hTxt, 8.0 * _escala);

        // LA FLECHA APUNTA A LA SOLDADURA, con su propia Y. Antes tomaba la X del borde de la franja
        // y le forzaba el centro vertical de la pieza, y eso en un perfil I es AIRE: a media altura,
        // por la punta del patín no pasa el contorno, está el hueco entre los dos patines. El texto
        // sí se queda centrado; lo que se corrige es dónde acaba la punta.
        LeaderZIzquierda(
            TextoSoldadura(p),
            puntaFlecha.X, puntaFlecha.Y,
            xLef - separacion, yc);
    }

    /// <summary>El texto del leader de soldadura, en dos renglones.</summary>
    private string TextoSoldadura(PlacaBaseCad p)
    {
        var electrodo = p.Electrodo.Trim();
        var espesor = p.TextoSoldadura.Trim().Replace("\"", string.Empty);

        if (espesor.Length == 0 && p.SoldaduraCm > 0)
        {
            espesor = Numero(p.SoldaduraCm / 2.54);
        }

        var s = "SOLDADURA";

        if (electrodo.Length > 0)
        {
            // CON SU XX, igual que el rótulo. Este leader lo ponía crudo, así que un E70 capturado
            // sin sufijo salía «SOLDADURA CON E70» aquí y «ELECTRODO E70XX» tres centímetros más
            // abajo: el mismo dato escrito de dos maneras en el mismo detalle. ConXX es idempotente,
            // así que un E70XX ya capturado no se convierte en E70XXXX.
            s += " CON " + Escapar(ConXX(electrodo));
        }

        if (espesor.Length > 0)
        {
            s += "\\PDE " + Escapar(espesor) + "\" DE ESP.";
        }

        return s;
    }

    // ======================================================================
    //  LOS CARTABONES
    // ======================================================================

    /// <summary>
    /// Los cartabones vistos en planta, partiendo del paño exterior del perfil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La cantidad de cada dirección es el <b>total</b>, y se reparte mitad y mitad entre las dos
    /// caras opuestas, con la impar en la cara positiva. Es el mismo criterio que las anclas.
    /// </para>
    /// <para>
    /// <b>Y van cruzados a propósito:</b> los datos de X —cantidad, espesor y longitud— dibujan los
    /// cartabones que salen de las caras <b>Y</b>, y los de Y salen de las caras <b>X</b>. Es la
    /// corrección que la propia macro documenta: la hoja maneja la longitud en el sentido opuesto al
    /// espesor visto en planta.
    /// </para>
    /// </remarks>
    /// <param name="reparto">
    /// El reparto que se usó, para que los leaders sepan cuál es de X y cuál de Y sin volver a
    /// deducirlo de la geometría ni repetir la regla del descarte.
    /// </param>
    /// <param name="contorno">
    /// El paño del perfil, para que cada cartabón arranque del acero que de verdad tiene al lado, y
    /// para que contra una columna redonda se le recorte la <b>boca de pescado</b>.
    /// </param>
    private List<object> Cartabones(
        PlacaBaseCad p, double xc, double yc, double pX, double pY, ContornoDeColumna? contorno,
        out List<CartabonesPlacaBase.Cartabon> reparto)
    {
        var creados = new List<object>();

        // EL REPARTO LO HACE CartabonesPlacaBase, y este método solo dibuja lo que le diga. La
        // cuenta la necesita también la vista previa de la hoja, y con una copia aquí las dos
        // podrían discrepar: la previa enseñando unos cartabones y el plano poniendo otros.
        reparto = CartabonesPlacaBase.Construir(p, xc, yc, pX, pY, _escala, contorno);

        foreach (var c in reparto)
        {
            // POLILÍNEA Y NO RECTÁNGULO: el cartabón contra una columna redonda lleva un ARCO —la
            // boca de pescado— y un rectángulo no puede describirlo. Los cartabones contra un
            // perfil recto siguen saliendo con sus cuatro vértices y sin ningún bulge, así que en
            // el plano son exactamente lo que eran.
            var r = Polilinea(c.Puntos, PlacaBaseCapas.Cartabones, c.Dobleces);

            if (r is not null)
            {
                creados.Add(r);

                SoldaduraDeCartabon(p, c, r);
            }
        }

        return creados;
    }

    /// <summary>
    /// La franja de soldadura que rodea el contorno de <b>un</b> cartabón, en morado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la misma idea que la soldadura del perfil —el contorno corrido hacia fuera el espesor del
    /// filete, y el rayado en la franja de en medio— con dos diferencias que no son de adorno.
    /// </para>
    /// <para>
    /// <b>Su propio espesor.</b> El cartabón es una placa más delgada que la columna, así que su
    /// filete casi nunca mide lo mismo. Con un solo dato para las dos, el plano diría que se sueldan
    /// igual y no es cierto.
    /// </para>
    /// <para>
    /// <b>Su propia capa y su propio color.</b> Morado, y no el de la soldadura del perfil: en un
    /// detalle con cartabones las dos franjas quedan a un centímetro una de otra, y del mismo color
    /// se leen como una sola soldadura con un espesor. Compartir capa además impediría apagar una
    /// sin la otra.
    /// </para>
    /// <para>
    /// El arco de la boca queda dentro de la franja como cualquier otro tramo, que es lo que se ve
    /// en obra: el filete de la boca es justo el que pega el cartabón al tubo.
    /// </para>
    /// </remarks>
    private void SoldaduraDeCartabon(
        PlacaBaseCad p, CartabonesPlacaBase.Cartabon c, object cartabon)
    {
        if (!p.DibujarSoldadura || !p.DibujarHatchSoldadura || p.SoldaduraCartabonCm <= 0)
        {
            return;
        }

        var t = p.SoldaduraCartabonCm * _escala;

        var fuera = ContornoDesplazado.HaciaFuera(c.Puntos, t);

        if (fuera is null)
        {
            Nota("No se pudo calcular la franja de soldadura de un cartabón: su contorno no da " +
                 "para desplazarse. El cartabón se dibujó igual, sin su soldadura.");
            return;
        }

        var frontera = Polilinea(fuera, PlacaBaseCapas.SoldaduraCartabon, c.Dobleces);

        if (frontera is null)
        {
            return;
        }

        var hatch = Hatch(
            PlacaBaseCapas.PatronSoldadura, PlacaBaseCapas.EscalaHatchSoldadura,
            frontera, new List<object> { cartabon },
            PlacaBaseCapas.SoldaduraCartabon, PlacaBaseCapas.ColorSoldaduraCartabon);

        if (hatch is not null)
        {
            AlFondo(new List<object> { hatch });
        }

        // La frontera era auxiliar, igual que la del perfil. El hatch no es asociativo, así que
        // borrarla no lo afecta.
        Borrar(frontera);
    }

    // ======================================================================
    //  COTAS
    // ======================================================================

    private void CotaH(double x1, double x2, double yRef, double yDim, bool haciaObjeto = false)
    {
        if (Math.Abs(x2 - x1) < 1e-6)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimRotated(
                    Punto(x1, yRef), Punto(x2, yRef), Punto((x1 + x2) / 2, yDim), 0.0);

                AjustarCota(d, haciaObjeto);
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota horizontal", ex);
        }
    }

    private void CotaV(double y1, double y2, double xRef, double xDim, bool haciaObjeto = false)
    {
        if (Math.Abs(y2 - y1) < 1e-6)
        {
            return;
        }

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic d = _ms.AddDimRotated(
                    Punto(xRef, y1), Punto(xRef, y2), Punto(xDim, (y1 + y2) / 2), Pi / 2);

                AjustarCota(d, haciaObjeto);
            });
        }
        catch (Exception ex)
        {
            Fallo("Cota vertical", ex);
        }
    }

    /// <summary>Le pone a la cota su capa, su estilo y el factor de lectura en cm.</summary>
    private void AjustarCota(dynamic d, bool haciaObjeto)
    {
        try
        {
            d.Layer = PlacaBaseCapas.Cotas;
            d.StyleName = PlacaBaseCapas.EstiloCota;
            d.TextGap = 0.08 * 10.0 * _escala;
            d.TextFill = false;

            // Si el dibujo no está en centímetros, se fuerza el factor para que la cota mida en
            // centímetros. Es lo que hace que el plano se lea igual esté en metros o en cm.
            if (Math.Abs((1.0 / _escala) - 1.0) > 1e-6)
            {
                d.LinearScaleFactor = 1.0 / _escala;
            }

            // Solo las dos cotas del dado llevan las líneas de extensión hacia la placa. Sin esto,
            // DIMEXO las pone del lado de fuera y cruzan el dibujo.
            if (haciaObjeto)
            {
                d.ExtensionLineOffset = 0.0;
                d.ExtensionLineExtend = 0.0;
                d.ExtLineFixedLenSuppress = true;
            }

            d.Update();
        }
        catch (Exception ex)
        {
            Fallo("Formato de la cota", ex);
        }
    }

    /// <summary>
    /// La cadena horizontal de arriba: de ancla a ancla y el último tramo hasta el borde derecho.
    /// </summary>
    /// <remarks>
    /// <b>Se omite el primer tramo</b> —borde izquierdo hasta la primera ancla— igual que en la
    /// macro. Ese tramo lo da ya la cota total de abajo, y ponerlo deja dos números pegados en la
    /// esquina.
    /// </remarks>
    private void CadenaH(List<double> xs, double xFin, double yRef, double yDim)
    {
        if (xs.Count == 0)
        {
            return;
        }

        var xa = xs[0];

        for (var i = 1; i < xs.Count; i++)
        {
            CotaH(xa, xs[i], yRef, yDim);
            xa = xs[i];
        }

        CotaH(xa, xFin, yRef, yDim);
    }

    /// <summary>La cadena vertical de la izquierda. Conserva el tramo de arriba.</summary>
    private void CadenaV(List<double> ys, double yFin, double xRef, double xDim)
    {
        if (ys.Count == 0)
        {
            return;
        }

        var ya = ys[0];

        for (var i = 1; i < ys.Count; i++)
        {
            CotaV(ya, ys[i], xRef, xDim);
            ya = ys[i];
        }

        CotaV(ya, yFin, xRef, xDim);
    }

    // ======================================================================
    //  LEADERS
    // ======================================================================

    /// <summary>Dos leaders por dirección: uno al agujero y otro al ancla.</summary>
    private void LeadersDeAnclas(
        PlacaBaseCad p, List<AnclasPlacaBase.Ancla> anclas, double xRig, double yCentro)
    {
        var separacionX = Math.Max(6.0 * _hTxt, 5.0 * _escala);
        var separacionY = 2.0 * _hTxt;

        foreach (var esX in new[] { true, false })
        {
            var idx = IndiceParaLeader(anclas, esX, yCentro);

            if (idx < 0)
            {
                continue;
            }

            var a = anclas[idx];

            var textoAncla = TextoDiametro(esX ? p.TextoDiamAnclaX : p.TextoDiamAnclaY, a.DAncla);
            var textoAgujero = TextoDiametro(esX ? p.TextoDiamAgujeroX : p.TextoDiamAgujeroY, a.DAgujero);

            // La flecha al cuadrante superior derecho del círculo exterior, y la del ancla al
            // inferior derecho del interior: así las dos puntas caen SOBRE su circunferencia y no
            // se confunde a cuál apunta cada una. El 0.3535 es cos(45°)/2.
            LeaderRecto("AGUJERO DE ANCLA " + textoAgujero,
                a.X + (a.DAgujero * 0.3535533906), a.Y + (a.DAgujero * 0.3535533906),
                xRig + separacionX, a.Y + separacionY);

            LeaderRecto("ANCLA " + textoAncla,
                a.X + (a.DAncla * 0.3535533906), a.Y - (a.DAncla * 0.3535533906),
                xRig + separacionX, a.Y - separacionY);
        }
    }

    /// <summary>
    /// Qué ancla se rotula: la de arriba y más a la derecha para X, y la derecha más cercana al
    /// centro para Y.
    /// </summary>
    private int IndiceParaLeader(List<AnclasPlacaBase.Ancla> anclas, bool esX, double yCentro)
    {
        var tol = 0.001 * _escala;
        var idx = -1;
        var xMax = 0.0;
        var yMax = 0.0;
        var mejor = 0.0;

        for (var i = 0; i < anclas.Count; i++)
        {
            if (anclas[i].EsX != esX)
            {
                continue;
            }

            var dist = Math.Abs(anclas[i].Y - yCentro);

            if (idx < 0)
            {
                idx = i;
                xMax = anclas[i].X;
                yMax = anclas[i].Y;
                mejor = dist;
                continue;
            }

            if (esX)
            {
                if (anclas[i].Y > yMax + tol)
                {
                    idx = i;
                    xMax = anclas[i].X;
                    yMax = anclas[i].Y;
                }
                else if (Math.Abs(anclas[i].Y - yMax) <= tol && anclas[i].X > xMax)
                {
                    idx = i;
                    xMax = anclas[i].X;
                }
            }
            else if (anclas[i].X > xMax + tol)
            {
                idx = i;
                xMax = anclas[i].X;
                mejor = dist;
            }
            else if (Math.Abs(anclas[i].X - xMax) <= tol && dist < mejor)
            {
                idx = i;
                mejor = dist;
            }
        }

        return idx;
    }

    /// <summary>Un leader por dirección de cartabones, con su espesor.</summary>
    /// <remarks>
    /// <b>La dirección se lee del reparto, no se vuelve a deducir.</b> Antes se recalculaba aquí la
    /// regla del descarte —«sin espesor o sin longitud, cantidad cero»— y se contaba a mano el
    /// índice del primero de Y. Eran las mismas dos cuentas escritas en dos sitios: cambiar el
    /// criterio del descarte en uno y no en el otro dejaba el leader apuntando a un cartabón que no
    /// era, o a ninguno.
    /// </remarks>
    private void LeadersDeCartabones(
        PlacaBaseCad p, List<object> cartabones,
        List<CartabonesPlacaBase.Cartabon> reparto, double xLef)
    {
        if (cartabones.Count == 0 || reparto.Count != cartabones.Count)
        {
            return;
        }

        var separacionX = Math.Max(6.0 * _hTxt, 5.0 * _escala);
        var separacionY = 2.0 * _hTxt;
        var xTexto = xLef - separacionX;

        // El alto que ocupan todos, para poner el texto de X arriba y el de Y abajo sin que
        // ninguno caiga sobre el detalle.
        var (minY, maxY) = AltoDeTodos(cartabones);

        var primeroX = reparto.FindIndex(c => c.EsX);
        var primeroY = reparto.FindIndex(c => !c.EsX);

        // El filete del cartabón se rotula EN EL MISMO LEADER, en un segundo renglón. Es la
        // soldadura de esa pieza y de ninguna otra, así que un leader propio apuntando al mismo
        // sitio solo añadiría una flecha más a un detalle que ya tiene siete.
        var filete = RenglonDelFileteDeCartabon(p);

        if (primeroX >= 0)
        {
            var (x, y) = PuntaLibre(cartabones[primeroX]);

            LeaderRectoDerecha(
                "CARTABON X DE " + ConPulgadas(p.TextoEspCartabonX) + " DE ESP." + filete,
                x, y, xTexto, maxY + separacionY);
        }

        if (primeroY >= 0)
        {
            var (x, y) = PuntaLibre(cartabones[primeroY]);

            LeaderRectoDerecha(
                "CARTABON Y DE " + ConPulgadas(p.TextoEspCartabonY) + " DE ESP." + filete,
                x, y, xTexto, minY - separacionY);
        }
    }

    /// <summary>El segundo renglón del leader del cartabón: su soldadura. Vacío si no lleva.</summary>
    private string RenglonDelFileteDeCartabon(PlacaBaseCad p)
    {
        if (!p.DibujarSoldadura || p.SoldaduraCartabonCm <= 0)
        {
            return string.Empty;
        }

        var espesor = p.TextoSoldaduraCartabon.Trim().Replace("\"", string.Empty);

        if (espesor.Length == 0)
        {
            espesor = Numero(p.SoldaduraCartabonCm / 2.54);
        }

        return "\\PSOLDADURA DE " + Escapar(espesor) + "\" DE ESP.";
    }

    private (double Min, double Max) AltoDeTodos(List<object> objetos)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var o in objetos)
        {
            var caja = Caja(o);

            if (caja is null)
            {
                continue;
            }

            if (caja.Value.Y1 < min) { min = caja.Value.Y1; }
            if (caja.Value.Y2 > max) { max = caja.Value.Y2; }
        }

        return min < max ? (min, max) : (0, 0);
    }

    /// <summary>El punto medio de la <b>punta libre</b> del cartabón: el extremo lejos del perfil.</summary>
    private (double X, double Y) PuntaLibre(object cartabon)
    {
        var caja = Caja(cartabon);

        if (caja is null)
        {
            return (0, 0);
        }

        var (x1, y1, x2, y2) = caja.Value;

        // El cartabón es una placa: su lado largo dice si es horizontal o vertical, y la punta
        // libre es el extremo de ese lado.
        return x2 - x1 >= y2 - y1
            ? (x2, (y1 + y2) / 2)
            : ((x1 + x2) / 2, y2);
    }

    private (double X1, double Y1, double X2, double Y2)? Caja(object ent)
    {
        try
        {
            return AcadConnection.Retry<(double, double, double, double)?>(() =>
            {
                object? min = null;
                object? max = null;

                var args = new object?[] { min, max };

                ent.GetType().InvokeMember(
                    "GetBoundingBox",
                    System.Reflection.BindingFlags.InvokeMethod,
                    binder: null, target: ent, args: args,
                    modifiers: new[] { new System.Reflection.ParameterModifier(2) { [0] = true, [1] = true } },
                    culture: null, namedParameters: null);

                if (args[0] is not double[] a || args[1] is not double[] b)
                {
                    return null;
                }

                return (a[0], a[1], b[0], b[1]);
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Leader de un tramo recto con el texto a la <b>derecha</b> del elemento.</summary>
    private void LeaderRecto(string s, double xFlecha, double yFlecha, double xTexto, double yTexto)
    {
        var xFin = xTexto - (0.5 * _hTxt);

        // Anclaje 4 = MiddleLeft, y el \pxql; alinea el párrafo a la izquierda.
        Mtexto("\\pxql;" + Escapar(s), xTexto, yTexto, anclaje: 4);

        Linea(xFlecha, yFlecha, xFin, yTexto, PlacaBaseCapas.Rotulos);

        Flecha(xFlecha, yFlecha, xFin, yTexto);
    }

    /// <summary>Igual, con el texto a la <b>izquierda</b>.</summary>
    private void LeaderRectoDerecha(string s, double xFlecha, double yFlecha,
                                    double xTexto, double yTexto)
    {
        var xFin = xTexto + (0.5 * _hTxt);

        // Anclaje 6 = MiddleRight.
        Mtexto("\\pxqr;" + Escapar(s), xTexto, yTexto, anclaje: 6);

        Linea(xFlecha, yFlecha, xFin, yTexto, PlacaBaseCapas.Rotulos);

        Flecha(xFlecha, yFlecha, xFin, yTexto);
    }

    /// <summary>
    /// El leader de la soldadura: una <b>Z</b> que cruza las cotas por encima de sus números.
    /// </summary>
    /// <remarks>
    /// El tramo central se sube un despeje sobre la punta para pasar por encima de los números de
    /// las dos cadenas de cotas. Sin él, la línea del leader cruza el texto de las cotas y las dos
    /// cosas se vuelven ilegibles.
    /// </remarks>
    private void LeaderZIzquierda(string s, double xFlecha, double yFlecha,
                                  double xTexto, double yTexto)
    {
        var xFin = xTexto + (0.5 * _hTxt);
        var dx = xFin - xFlecha;

        if (Math.Abs(dx) < 1e-7)
        {
            return;
        }

        Mtexto("\\pxqr;" + s, xTexto, yTexto, anclaje: 6, exacto: true);

        var despeje = Math.Max(3.0 * _hTxt, 2.5 * _escala);
        var yPaso = yFlecha + despeje;

        var x1 = xFlecha + (0.18 * dx);
        var x2 = xFin - (0.18 * dx);

        try
        {
            AcadConnection.Retry(() =>
            {
                dynamic pl = _ms.AddLightWeightPolyline(new[]
                {
                    xFlecha, yFlecha,
                    x1, yPaso,
                    x2, yPaso,
                    xFin, yTexto
                });

                pl.Closed = false;
                pl.Layer = PlacaBaseCapas.Rotulos;
                pl.Color = PorCapa;
            });
        }
        catch (Exception ex)
        {
            Fallo("Leader de la soldadura", ex);
        }

        Flecha(xFlecha, yFlecha, x1, yPaso);
    }

    /// <summary>
    /// La punta del leader: un triángulo <b>relleno</b> más su contorno.
    /// </summary>
    /// <remarks>
    /// Se dibuja a mano y no con un estilo de leader de AutoCAD, para no depender de <c>DIMBLK</c>
    /// ni del estilo de cota: el vértice cae exactamente donde se pide. Y lleva contorno además del
    /// relleno para que siga viéndose si el dibujo se abre con <c>FILLMODE = 0</c>.
    /// </remarks>
    private void Flecha(double xPunta, double yPunta, double xTexto, double yTexto)
    {
        var dx = xTexto - xPunta;
        var dy = yTexto - yPunta;
        var largo = Math.Sqrt((dx * dx) + (dy * dy));

        if (largo < 1e-7)
        {
            return;
        }

        var ux = dx / largo;
        var uy = dy / largo;
        var px = -uy;
        var py = ux;

        var largoFlecha = _hFle > 0 ? 0.5 * _hFle : 0.45 * _hTxt;
        var semiAncho = 0.4 * largoFlecha;

        var xBase = xPunta + (largoFlecha * ux);
        var yBase = yPunta + (largoFlecha * uy);

        var xIzq = xBase + (semiAncho * px);
        var yIzq = yBase + (semiAncho * py);
        var xDer = xBase - (semiAncho * px);
        var yDer = yBase - (semiAncho * py);

        try
        {
            AcadConnection.Retry(() =>
            {
                // En un SOLID triangular el tercer y el cuarto punto coinciden.
                dynamic sol = _ms.AddSolid(
                    Punto(xPunta, yPunta), Punto(xIzq, yIzq),
                    Punto(xDer, yDer), Punto(xDer, yDer));

                sol.Layer = PlacaBaseCapas.Rotulos;
                sol.Color = PorCapa;
            });
        }
        catch (Exception ex)
        {
            Fallo("Relleno de la flecha del leader", ex);
        }

        Polilinea(new[] { xPunta, yPunta, xIzq, yIzq, xDer, yDer }, PlacaBaseCapas.Rotulos);
    }

    // ======================================================================
    //  EL RÓTULO
    // ======================================================================

    /// <summary>El MTEXT del rótulo, con todas sus líneas, centrado bajo el detalle.</summary>
    private void Rotulo(PlacaBaseCad p, List<AnclasPlacaBase.Ancla> anclas,
                        int nAncX, int nAncY, double xc, double yTop)
    {
        var lineas = new List<string>();

        var titulo = "DETALLE DE PLACA BASE";

        if (p.Marca.Trim().Length > 0)
        {
            titulo += " " + p.Marca.Trim();
        }

        lineas.Add(titulo);

        // Siempre en centímetros, y con el espesor en pulgadas detrás.
        var medidas = $"{Numero(p.LargoCm)} X {Numero(p.AnchoCm)} cm";

        if (p.Espesor.Trim().Length > 0)
        {
            medidas += " X " + ConPulgadas(p.Espesor);
        }

        lineas.Add(medidas);

        if (p.AceroPlaca.Trim().Length > 0)
        {
            lineas.Add("ACERO DE PLACA " + p.AceroPlaca.Trim());
        }

        // Los diámetros de AGUJERO no van aquí: quedan dichos por sus leaders, y repetirlos en el
        // rótulo es la clase de dato duplicado que un día deja de coincidir.
        if (nAncX > 0)
        {
            lineas.Add($"{nAncX} ANCLAS DE {ConPulgadas(p.TextoDiamAnclaX)}");
        }

        if (nAncY > 0)
        {
            lineas.Add($"{nAncY} ANCLAS DE {ConPulgadas(p.TextoDiamAnclaY)}");
        }

        if (p.Seccion.Trim().Length > 0)
        {
            lineas.Add("COLUMNA " + p.Seccion.Trim());
        }

        if (p.ConCartabones && p.NCartabonesX > 0 && p.EspCartabonXCm > 0 && p.LongCartabonXCm > 0)
        {
            lineas.Add($"{p.NCartabonesX} CARTABONES X DE {ConPulgadas(p.TextoEspCartabonX)} " +
                       $"DE ESPESOR, LARGO {Numero(p.LongCartabonXCm)} cm");
        }

        if (p.ConCartabones && p.NCartabonesY > 0 && p.EspCartabonYCm > 0 && p.LongCartabonYCm > 0)
        {
            lineas.Add($"{p.NCartabonesY} CARTABONES Y DE {ConPulgadas(p.TextoEspCartabonY)} " +
                       $"DE ESPESOR, LARGO {Numero(p.LongCartabonYCm)} cm");
        }

        if (p.Electrodo.Trim().Length > 0)
        {
            lineas.Add("ELECTRODO " + ConXX(p.Electrodo));
        }

        lineas.Add($"Acot. cm    Esc. 1:{Numero(p.Escala)}");

        var texto = string.Join("\\P", lineas.Select(Escapar));

        // Anclaje 2 = TopCenter: el punto es el centro del renglón de arriba.
        Mtexto("\\pxqc;" + texto, xc, yTop, anclaje: 2,
               estilo: _estiloRotulo, interlinea: p.SeparacionLineas);
    }

    private object? Mtexto(string texto, double x, double y, int anclaje,
                           string? estilo = null, double interlinea = 0, bool exacto = false)
    {
        if (texto.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return AcadConnection.Retry<object?>(() =>
            {
                dynamic t = _ms.AddMText(Punto(x, y), 0.0, texto);

                t.Layer = PlacaBaseCapas.Rotulos;
                t.Color = PorCapa;
                t.StyleName = estilo ?? PlacaBaseCapas.EstiloTexto;
                t.Height = _hTxt;
                t.Rotation = 0.0;
                t.AttachmentPoint = anclaje;

                // Se reafirma el punto DESPUÉS del anclaje: al cambiarlo, AutoCAD recoloca el
                // texto respecto al punto anterior.
                t.InsertionPoint = Punto(x, y);

                if (interlinea > 0 || exacto)
                {
                    t.LineSpacingStyle = 2;   // acLineSpacingStyleExactly
                    t.LineSpacingFactor = interlinea > 0 ? interlinea : 1.0;
                }

                return (object?)t;
            });
        }
        catch (Exception ex)
        {
            Fallo("MTEXT del rotulado", ex);
            return null;
        }
    }

    // ======================================================================
    //  EL BLOQUE
    // ======================================================================

    /// <summary>
    /// Agrupa la geometría en un bloque con el nombre de la sección.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Solo lo que hay entre <paramref name="inicio"/> y <paramref name="fin"/>, que es geometría:
    /// las cotas y los rótulos se dibujan después de llamar aquí y por eso se quedan fuera.
    /// </para>
    /// <para>
    /// El origen del bloque y su inserción son <b>la esquina de la placa</b>, así que la geometría
    /// se queda exactamente donde estaba.
    /// </para>
    /// <para>
    /// <b>Los originales se borran solo si la copia funcionó.</b> Borrarlos igual dejaría el detalle
    /// sin dibujar por ningún lado.
    /// </para>
    /// </remarks>
    private string Bloquear(string seccion, int inicio, int fin, double xBase, double yBase)
    {
        try
        {
            var objetos = new List<object>();

            AcadConnection.Retry(() =>
            {
                objetos.Clear();

                for (var i = inicio; i < fin; i++)
                {
                    dynamic ent = _ms.Item(i);

                    string capa = ent.Layer;
                    string nombre = ent.ObjectName;

                    // Ni cotas ni rótulos. La comprobación del nombre es el respaldo de la macro:
                    // ninguna dimensión entra aunque su capa se hubiera cambiado a mano.
                    if (string.Equals(capa, PlacaBaseCapas.Cotas, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(capa, PlacaBaseCapas.Rotulos, StringComparison.OrdinalIgnoreCase)
                        || nombre.Contains("Dimension", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    objetos.Add((object)ent);
                }
            });

            if (objetos.Count == 0)
            {
                Nota("No había geometría que agrupar en un bloque.");
                return string.Empty;
            }

            var nombreBloque = NombreLibre(NombreValido(seccion));
            var origen = Punto(xBase, yBase);

            // El Add se llama UNA VEZ y fuera de un reintento: reintentarlo después de haber creado
            // el bloque fallaría por nombre duplicado, y ese error no es de los que se reintentan.
            dynamic bloque = _doc.Blocks.Add(origen, nombreBloque);

            var copiado = AcadArreglos.Llamar(
                $"CopyObjects del detalle '{nombreBloque}'", objetos,
                arr => { _doc.CopyObjects(arr, bloque); }, Fallo, Nota);

            if (!copiado)
            {
                return string.Empty;
            }

            foreach (var o in objetos)
            {
                Borrar(o);
            }

            AcadConnection.Retry(() =>
            {
                dynamic insercion = _ms.InsertBlock(origen, nombreBloque, 1.0, 1.0, 1.0, 0.0);
                insercion.Layer = "0";
                insercion.Update();
            });

            return nombreBloque;
        }
        catch (Exception ex)
        {
            Fallo($"Agrupar el detalle de la placa en un bloque", ex);
            return string.Empty;
        }
    }

    /// <summary>Limpia los caracteres que AutoCAD no admite en un nombre de bloque.</summary>
    private static string NombreValido(string seccion)
    {
        var s = (seccion ?? string.Empty).Replace('\u00A0', ' ').Trim();

        foreach (var c in new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=' })
        {
            s = s.Replace(c, '_');
        }

        if (s.Length == 0)
        {
            s = "PLACA BASE";
        }

        return s.Length > 230 ? s[..230] : s;
    }

    /// <summary>
    /// Conserva el nombre exacto si está libre; si no, le añade un consecutivo.
    /// </summary>
    /// <remarks>
    /// No se sobrescribe un bloque que ya esté en el dibujo: podría ser del usuario, y perderlo por
    /// dibujar una placa con el mismo nombre de sección sería carísimo de recuperar.
    /// </remarks>
    private string NombreLibre(string baseNombre)
    {
        var nombre = baseNombre;
        var n = 1;

        while (ExisteBloque(nombre))
        {
            n++;
            nombre = baseNombre + "_" + n;
        }

        return nombre;
    }

    private bool ExisteBloque(string nombre)
    {
        try
        {
            return AcadConnection.Retry(() =>
            {
                try
                {
                    _ = _doc.Blocks.Item(nombre);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ======================================================================
    //  TEXTOS
    // ======================================================================

    /// <summary>El diámetro con el símbolo <c>%%c</c> y su unidad.</summary>
    private string TextoDiametro(string textoCelda, double diamDwg)
    {
        var s = (textoCelda ?? string.Empty).Trim();

        if (s.Length == 0)
        {
            // Sin texto en la hoja se escribe el número, en pulgadas, que es como se capturan.
            s = Numero(diamDwg / _escala / 2.54);
        }

        if (!s.Contains('"'))
        {
            s += "\"";
        }

        return "%%c" + s;
    }

    /// <summary>Le pone las pulgadas si no las trae.</summary>
    private static string ConPulgadas(string? s)
    {
        var t = (s ?? string.Empty).Trim();

        if (t.Length > 0 && !t.Contains('"'))
        {
            t += "\"";
        }

        return t;
    }

    /// <summary>
    /// El electrodo con el sufijo <c>XX</c>.
    /// </summary>
    /// <remarks>
    /// Es la convención de la macro: un E70 se rotula <c>E70XX</c>, porque los dos últimos dígitos
    /// —posición y tipo de corriente— los elige el taller.
    /// </remarks>
    private static string ConXX(string? electrodo)
    {
        var e = (electrodo ?? string.Empty).Trim();

        if (e.Length == 0)
        {
            return e;
        }

        return e.EndsWith("XX", StringComparison.OrdinalIgnoreCase) ? e : e + "XX";
    }

    /// <summary>Sin decimales si es entero, con dos si no. Es el <c>FmtNum</c> de la macro.</summary>
    private static string Numero(double v) =>
        Math.Abs(v - Math.Truncate(v)) < 0.005
            ? v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Protege los caracteres con significado propio dentro de un MTEXT.</summary>
    private static string Escapar(string s) =>
        s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
}
