using System.IO;
using System.Windows;
using CadLink.App.Models;
using CadLink.Cad;
using CadLink.Revit.Nucleo;
using Microsoft.Win32;

namespace CadLink.App;

/// <summary>
/// El boton «Para Revit…» de las hojas Placa Base, Zapatas Aisladas y Zapatas Corridas.
/// </summary>
/// <remarks>
/// <para>
/// Escribe las TRES hojas en un solo <c>.cadlink-detalles.json</c>, pulse en la que se pulse: en
/// Revit los botones Placas base y Cimentacion leen el mismo archivo, y tener uno por hoja seria
/// obligar a abrir el que toca en cada boton.
/// </para>
/// <para>
/// Las medidas salen ya resueltas con las MISMAS cuentas del dibujo de AutoCAD -el reparto
/// perimetral de las anclas, la separacion al borde ajustada a la tabla K, el ahogo, la tuerca-,
/// asi que el modelo y el plano no pueden contar dos historias. El complemento de Revit solo las
/// coloca.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private void OnDetallesParaRevit(object sender, RoutedEventArgs e)
    {
        CerrarEdicionDeLasHojas();

        var archivo = new ArchivoDetallesJson
        {
            Aplicacion = AppInfo.ProductName + " " + AppInfo.Version,
            Fecha = DateTime.Now,
            Origen = _archivoActual
        };

        foreach (var fila in _datos.PlacasBase)
        {
            if (string.IsNullOrWhiteSpace(fila.Marca))
            {
                continue;
            }

            var p = fila.AFormatoCad();

            if (p.Falta.Count > 0)
            {
                archivo.Avisos.Add($"Placa {fila.Marca}: falta {string.Join(", ", p.Falta)}. No va.");
                continue;
            }

            archivo.Placas.Add(PlacaParaRevit(p));
        }

        foreach (var fila in _datos.ZapatasAisladas)
        {
            var z = fila.AFormatoCad();

            if (z.Id.Trim().Length > 0)
            {
                archivo.Zapatas.Add(ZapataParaRevit(z));
            }
        }

        foreach (var fila in _datos.ZapatasCorridas)
        {
            var z = fila.AFormatoCad();

            if (z.Id.Trim().Length > 0)
            {
                archivo.Corridas.Add(CorridaParaRevit(z, archivo.Avisos));
            }
        }

        if (archivo.Placas.Count + archivo.Zapatas.Count + archivo.Corridas.Count == 0)
        {
            MessageBox.Show(this,
                "No hay nada que mandar: llena la hoja Placa Base, Zapatas Aisladas o Zapatas Corridas.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var nombre = string.IsNullOrWhiteSpace(_archivoActual)
            ? "detalles"
            : Path.GetFileNameWithoutExtension(_archivoActual);

        var dialogo = new SaveFileDialog
        {
            Title = "Guardar las placas base y las zapatas para ponerlas en Revit",
            Filter = ArchivoDetalles.Filtro,
            DefaultExt = ArchivoDetalles.Extension,
            FileName = nombre + ArchivoDetalles.Extension
        };

        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ArchivoDetalles.Guardar(archivo, dialogo.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo guardar el archivo:\n\n" + ex.Message,
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text =
            $"Para Revit: {archivo.Placas.Count} placa(s), {archivo.Zapatas.Count} zapata(s) y "
            + $"{archivo.Corridas.Count} zapata(s) corrida(s) en {Path.GetFileName(dialogo.FileName)}";

        MessageBox.Show(this,
            $"Se guardaron {archivo.Placas.Count} placa(s) base, {archivo.Zapatas.Count} zapata(s) "
            + $"aislada(s) y {archivo.Corridas.Count} zapata(s) corrida(s).\n\n"
            + "En Revit, pestaña CadLink, panel Detalles:\n"
            + "  - «Placas base»: elige las columnas, abre este archivo y escoge la placa.\n"
            + "  - «Cimentación»: elige columnas y muros, abre este archivo y escoge la zapata.\n"
            + "  - «Soldar vigas»: elige las vigas; no necesita archivo."
            + (archivo.Avisos.Count == 0 ? string.Empty
                : "\n\nNo van o van con aviso:\n  - " + string.Join("\n  - ", archivo.Avisos.Take(10))),
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>La placa, en metros y en los ejes del detalle de AutoCAD.</summary>
    /// <remarks>
    /// Las anclas se colocan con <see cref="AnclasPlacaBase.Construir"/> y su alzado con
    /// <see cref="ElevacionPlacaBase.AnclasDeCanto"/>, en centimetros y a escala 1, que es como
    /// los usa <c>PlacaBaseDrawer</c> con su escala de dibujo. Se mide desde el CENTRO de la placa.
    /// </remarks>
    private static PlacaBaseJson PlacaParaRevit(PlacaBaseCad p)
    {
        var b = p.AnchoDibujoCm;
        var h = p.AltoDibujoCm;
        var grout = p.ConGrout ? Math.Max(0, p.EspesorGroutCm) : 0;

        var dAguX = p.DiamAgujeroXCm > 0 ? p.DiamAgujeroXCm : p.DiamAnclaXCm + (1.0 / 16.0 * 2.54);
        var dAguY = p.DiamAgujeroYCm > 0 ? p.DiamAgujeroYCm : p.DiamAnclaYCm + (1.0 / 16.0 * 2.54);

        // La separacion al borde: la misma cuenta que PlacaBaseDrawer, con escala 1.
        var sepX = AnclasPlacaBase.SepBordeAjustada(p.SepBordeXCm, p.DiamAnclaXCm, b);
        var sepY = AnclasPlacaBase.SepBordeAjustada(p.SepBordeYCm, p.DiamAnclaYCm, h);

        sepX = sepX > 0
            ? sepX
            : AnclasPlacaBase.SepAuto(b, p.PerfilXDibujoCm, dAguX, 1, AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaXCm));

        sepY = sepY > 0
            ? sepY
            : AnclasPlacaBase.SepAuto(h, p.PerfilYDibujoCm, dAguY, 1, AnclasPlacaBase.BordeMinimoCm(p.DiamAnclaYCm));

        var anclas = AnclasPlacaBase.Construir(
            -b / 2, -h / 2, b, h, p.NAnclasX, p.NAnclasY, sepX, sepY,
            p.DiamAnclaXCm, dAguX, p.DiamAnclaYCm, dAguY);

        // El alzado de UN ancla de cada direccion: punta, fondo, pata y tuerca. Con la cara de
        // abajo de la placa en cero, que es desde donde mide el archivo.
        var deX = AlzadoDeUnAncla(p, b, sepX, p.LongAnclajeXCm, p.LongAnclaXCm, p.DoblezAnclaXCm, p.DiamAnclaXCm, grout);
        var deY = AlzadoDeUnAncla(p, h, sepY, p.LongAnclajeYCm, p.LongAnclaYCm, p.DoblezAnclaYCm, p.DiamAnclaYCm, grout);

        var placa = new PlacaBaseJson
        {
            Marca = p.Marca,
            Seccion = p.Seccion,
            Acero = p.AceroPlaca,
            LadoXM = b / 100,
            LadoYM = h / 100,
            EspesorM = p.EspesorCm / 100,
            PerfilXM = p.PerfilXDibujoCm / 100,
            PerfilYM = p.PerfilYDibujoCm / 100,
            GroutM = grout / 100,
            SoldaduraM = p.SoldaduraCm / 100,
            Electrodo = p.Electrodo,
            Cartabones = p.ConCartabones ? CartabonesPlacaBase.Cuantos(p) : 0
        };

        foreach (var a in anclas)
        {
            var alzado = a.EsX ? deX : deY;

            // La pata apunta HACIA DENTRO, a lo largo de su hilera, como en el alzado.
            var haciaX = a.EsX ? (a.X > 1e-9 ? -1.0 : 1.0) : 0.0;
            var haciaY = a.EsX ? 0.0 : (a.Y > 1e-9 ? -1.0 : 1.0);

            placa.Anclas.Add(new AnclaJson
            {
                X = a.X / 100,
                Y = a.Y / 100,
                DiametroM = a.DAncla / 100,
                AgujeroM = a.DAgujero / 100,
                PuntaM = alzado.Punta / 100,
                FondoM = alzado.Fondo / 100,
                PataM = alzado.Pata / 100,
                PataX = haciaX,
                PataY = haciaY,
                TuercaM = alzado.Tuerca / 100,
                TuercaAltoM = alzado.TuercaAlto / 100
            });
        }

        return placa;
    }

    /// <summary>Punta, fondo, pata y tuerca de un ancla, en cm desde la cara de abajo de la placa.</summary>
    private static (double Punta, double Fondo, double Pata, double Tuerca, double TuercaAlto) AlzadoDeUnAncla(
        PlacaBaseCad p, double anchoPlaca, double sepBorde, double ahogo, double largo, double doblez,
        double diametro, double grout)
    {
        var lista = ElevacionPlacaBase.AnclasDeCanto(
            0, 0, p.EspesorCm, anchoPlaca, sepBorde, ahogo, largo, doblez, p.EspesorCm, grout,
            diametro, 1, 1);

        if (lista.Count == 0)
        {
            // Sin ahogo ni longitud la macro no dibuja el ancla en el alzado: aqui tampoco se
            // modela el vastago, solo el barreno.
            return (0, 0, 0, 0, 0);
        }

        var v = lista[0].Vastago;
        var t = lista[0].Tuerca;

        var punta = v[1];
        var fondo = v[3];
        var pata = v.Length >= 6 ? Math.Abs(v[4] - v[2]) : 0;

        var anchoTuerca = Math.Abs(t[2] - t[0]);
        var altoTuerca = Math.Abs(t[5] - t[1]);

        return (punta, fondo, pata, anchoTuerca, altoTuerca);
    }

    private static ZapataAisladaJson ZapataParaRevit(ZapataCad z) => new()
    {
        Id = z.Id,
        Tipo = z.Tipo,
        AnchoM = z.AnchoM,
        LargoM = z.LargoM,
        EspesorM = z.EspesorM,
        ProfundidadM = z.ProfundidadM,
        DadoXM = z.AnchoDadoCm / 100,
        DadoYM = z.AnchoDadoCm / 100,
        DadoCircular = z.DadoCircular,
        Fc = z.Fc
    };

    /// <summary>
    /// La zapata corrida, con las medidas de su contratrabe tomadas de la hoja de secciones de
    /// concreto: en AutoCAD la contratrabe se inserta como bloque por su ID, y en Revit hace falta
    /// su base y su peralte.
    /// </summary>
    private ZapataCorridaJson CorridaParaRevit(ZapataCorridaCad z, List<string> avisos)
    {
        var json = new ZapataCorridaJson
        {
            Id = z.Id,
            Tipo = z.Tipo,
            AnchoM = z.AnchoM,
            EspesorM = z.EspesorM,
            ProfundidadM = z.ProfundidadM,
            Fc = z.Fc
        };

        if (z.HayContratrabe)
        {
            var seccion = _datos.SeccionesConcreto.FirstOrDefault(s =>
                string.Equals((s.Id ?? string.Empty).Trim(), z.IdContratrabe, StringComparison.OrdinalIgnoreCase));

            json.Contratrabe = z.IdContratrabe;

            if (seccion is null)
            {
                avisos.Add($"Zapata corrida {z.Id}: la contratrabe «{z.IdContratrabe}» no esta en la hoja de "
                           + "secciones de concreto; en Revit va sin contratrabe.");
                json.Contratrabe = string.Empty;
            }
            else
            {
                json.ContratrabeAnchoM = seccion.BaseCm / 100;
                json.ContratrabeAltoM = seccion.AlturaCm / 100;
            }
        }
        else
        {
            // Sin contratrabe, el muro de la hoja -de mamposteria o de concreto- baja hasta la
            // zapata: es el enrase.
            json.EnraseM = z.EspesorMuroCm / 100;
        }

        return json;
    }
}
