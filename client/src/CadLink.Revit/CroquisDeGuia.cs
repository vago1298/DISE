using System.Windows.Media;

namespace CadLink.Revit;

/// <summary>
/// Los croquis de guia de los cuadros: cada letra en rojo es la del renglon que pide ese dato.
/// </summary>
/// <remarks>
/// Son esquemas, no a escala: lo que importa es que se vea que mide cada letra. Viven aqui, sin
/// nada de la Revit API, igual que <see cref="VentanaDetalle"/>.
/// </remarks>
internal static class CroquisDeGuia
{
    // ------------------------------------------------------------------ Conectar vigas

    /// <summary>La conexion vista de lado: columna, viga IR, placas de patin y de cortante.</summary>
    public static void ConexionAlzado(Croquis c)
    {
        c.Rect(10, 10, 36, 220, Croquis.Acero);
        c.Texto(12, 2, "Columna", 9);

        c.Rect(46, 92, 244, 56, Croquis.Concreto);           // alma
        c.Rect(46, 82, 244, 10, Croquis.AceroOscuro);       // patin de arriba
        c.Rect(46, 148, 244, 10, Croquis.AceroOscuro);      // patin de abajo
        c.Texto(230, 110, "Viga IR", 9);

        c.Rect(46, 72, 120, 10, Croquis.Placa);             // placas de patin
        c.Rect(46, 158, 120, 10, Croquis.Placa);
        c.Rect(46, 100, 70, 40, Croquis.Placa);             // placa de cortante

        foreach (var y in new[] { 108.0, 120, 132 })
        {
            c.Circulo(92, y, 3, Croquis.AceroOscuro);
        }

        c.Filete(46, 72, 7, -7);
        c.Filete(46, 168, 7, 7);
        c.Filete(46, 100, 5, 5);

        c.Cota(46, 58, 166, 58, "B");
        c.Llamada(160, 77, 215, 40, "C");
        c.Llamada(49, 68, 95, 30, "D");
        c.Cota(126, 100, 126, 140, "E");

        c.Linea(116, 140, 116, 200, Croquis.Gris, 0.6, true);
        c.Cota(46, 200, 116, 200, "F");

        c.Llamada(49, 103, 150, 218, "H");
        c.Llamada(116, 120, 190, 210, "I");
        c.Llamada(92, 132, 235, 190, "J");
        c.Cota(104, 108, 104, 132, "K");
    }

    /// <summary>La conexion en corte: el ancho de las placas de patin y el espesor de la de cortante.</summary>
    public static void ConexionCorte(Croquis c)
    {
        c.Rect(45, 40, 80, 8, Croquis.AceroOscuro);
        c.Rect(81, 48, 8, 104, Croquis.AceroOscuro);
        c.Rect(45, 152, 80, 8, Croquis.AceroOscuro);

        c.Rect(35, 32, 100, 8, Croquis.Placa);
        c.Rect(35, 160, 100, 8, Croquis.Placa);
        c.Rect(89, 66, 7, 68, Croquis.Placa);
        c.Linea(66, 100, 112, 100, Croquis.AceroOscuro, 3);

        c.Cota(35, 18, 135, 18, "A");
        c.Llamada(93, 80, 145, 80, "G");
        c.Texto(5, 182, "Corte de la viga", 9);
    }

    // ------------------------------------------------------------------ Placas base

    public static void PlacaBase(Croquis c)
    {
        c.Rect(110, 5, 40, 105, Croquis.Acero);
        c.Texto(112, 40, "Col.", 9);

        c.Rect(45, 128, 170, 115, Croquis.Concreto);
        c.Texto(100, 175, "Dado o muro", 9);
        c.Rect(55, 119, 150, 9, Croquis.Grout);
        c.Rect(55, 110, 150, 9, Croquis.Placa);

        foreach (var (x, pata) in new[] { (75.0, 25.0), (185.0, -25.0) })
        {
            c.Linea(x, 100, x, 225, Croquis.Varilla, 2.5);
            c.Linea(x, 225, x + pata, 225, Croquis.Varilla, 2.5);
            c.Rect(x - 6, 104, 12, 6, Croquis.AceroOscuro);
        }

        c.Rect(55, 210, 150, 7, Brushes.Transparent, true);

        c.Filete(110, 110, -7, -7);
        c.Filete(150, 110, 7, -7);

        c.Texto(5, 108, "Placa", 9);
        c.Llamada(204, 123, 238, 123, "A");
        c.Llamada(195, 213, 238, 195, "B");
        c.Llamada(106, 107, 60, 70, "C");
        c.Texto(5, 230, "B: contraplaca, opcional", 9);
    }

    // ------------------------------------------------------------------ Cimentacion

    public static void Cimentacion(Croquis c)
    {
        c.Rect(95, 5, 30, 60, Croquis.Acero);
        c.Rect(80, 65, 60, 90, Croquis.Concreto);
        c.Texto(88, 100, "Dado", 9);
        c.Rect(20, 155, 180, 40, Croquis.Concreto);
        c.Texto(85, 168, "Zapata", 9);
        c.Rect(12, 195, 196, 10, Croquis.Grout);

        c.Linea(212, 65, 212, 195, Croquis.Gris, 0.8, true);
        c.Texto(150, 30, "Desplante:", 9);
        c.Texto(150, 42, "el de la hoja", 9);

        c.Llamada(190, 200, 205, 225, "A");
        c.Texto(5, 215, "A: plantilla", 9);
    }

    // ------------------------------------------------------------------ Soldar columnas

    public static void SoldarColumna(Croquis c)
    {
        c.Rect(60, 10, 40, 110, Croquis.Acero);
        c.Rect(20, 120, 120, 10, Croquis.Placa);
        c.Rect(15, 130, 130, 20, Croquis.Concreto);
        c.Filete(60, 120, -8, -8);
        c.Filete(100, 120, 8, -8);
        c.Llamada(56, 116, 25, 80, "A");
        c.Texto(105, 40, "Base", 9);
    }

    // ------------------------------------------------------------------ Encamisar trabes

    /// <summary>El corte de la trabe con placas: espesores, adhesivo, tornillos y anclas.</summary>
    public static void EncamisadoPlacas(Croquis c)
    {
        c.Rect(0, 5, 230, 20, Croquis.Concreto);
        c.Texto(5, 8, "Losa", 9);
        c.Rect(75, 25, 80, 140, Croquis.Concreto);

        c.Rect(70, 25, 5, 145, Croquis.Grout);
        c.Rect(155, 25, 5, 145, Croquis.Grout);
        c.Rect(70, 165, 90, 5, Croquis.Grout);

        c.Rect(62, 25, 8, 153, Croquis.Placa);
        c.Rect(160, 25, 8, 153, Croquis.Placa);
        c.Rect(62, 170, 106, 8, Croquis.Placa);

        foreach (var y in new[] { 60.0, 95, 130 })
        {
            c.Linea(52, y, 178, y, Croquis.AceroOscuro, 2.5);
            c.Rect(55, y - 4, 7, 8, Croquis.AceroOscuro);
            c.Rect(168, y - 4, 7, 8, Croquis.AceroOscuro);
        }

        foreach (var x in new[] { 95.0, 135 })
        {
            c.Linea(x, 183, x, 150, Croquis.AceroOscuro, 2);
            c.Rect(x - 4, 178, 8, 5, Croquis.AceroOscuro);
        }

        c.Llamada(66, 145, 25, 150, "A");
        c.Llamada(72, 110, 25, 110, "B");
        c.Llamada(176, 60, 212, 45, "G");
        c.Cota(205, 60, 205, 130, "H");
        c.Llamada(95, 181, 55, 210, "I");
        c.Cota(145, 150, 145, 170, "J");
        c.Texto(5, 214, "Corte", 9);
    }

    /// <summary>El corte con angulos y soleras.</summary>
    public static void EncamisadoAngulos(Croquis c)
    {
        c.Rect(75, 15, 80, 110, Croquis.Concreto);

        c.Rect(65, 125, 30, 8, Croquis.Placa);
        c.Rect(65, 95, 8, 38, Croquis.Placa);
        c.Rect(135, 125, 30, 8, Croquis.Placa);
        c.Rect(157, 95, 8, 38, Croquis.Placa);

        c.Rect(59, 15, 4, 124, Croquis.Varilla);
        c.Rect(167, 15, 4, 124, Croquis.Varilla);
        c.Rect(59, 135, 112, 4, Croquis.Varilla);

        c.Cota(65, 150, 95, 150, "C");
        c.Llamada(61, 60, 25, 60, "E");
        c.Texto(5, 132, "Angulos y", 9);
        c.Texto(5, 143, "soleras", 9);
    }

    /// <summary>La trabe de lado: separacion, ancho de solera y lo libre junto a la columna.</summary>
    public static void EncamisadoAlzado(Croquis c)
    {
        c.Rect(0, 0, 25, 120, Croquis.Acero);
        c.Rect(25, 25, 205, 65, Croquis.Concreto);

        foreach (var x in new[] { 40.0, 100, 160 })
        {
            c.Rect(x, 20, 10, 75, Croquis.Varilla);
        }

        c.Cota(25, 10, 40, 10, "K");
        c.Cota(45, 108, 105, 108, "F");
        c.Llamada(165, 55, 205, 108, "D");
        c.Texto(180, 30, "De lado", 9);
    }

    // ------------------------------------------------------------------ Armar muros

    public static void ArmadoMuro(Croquis c)
    {
        // Corte: el muro sobre su zapata, las dos parrillas y sus ganchos.
        c.Rect(10, 215, 130, 40, Croquis.Concreto);
        c.Rect(55, 20, 40, 195, Croquis.Concreto);

        c.Linea(62, 30, 62, 240, Croquis.Varilla, 2);
        c.Linea(88, 30, 88, 240, Croquis.Varilla, 2);
        c.Linea(62, 30, 79, 30, Croquis.Varilla, 2);
        c.Linea(88, 30, 71, 30, Croquis.Varilla, 2);
        c.Linea(62, 240, 30, 240, Croquis.Varilla, 2);
        c.Linea(88, 240, 120, 240, Croquis.Varilla, 2);

        c.Llamada(75, 30, 120, 30, "A");
        c.Llamada(40, 240, 22, 200, "B");
        c.Texto(15, 5, "Corte", 9);

        // Alzado: la separacion de las verticales y de las horizontales.
        c.Rect(165, 20, 125, 195, Croquis.Concreto);

        foreach (var x in new[] { 178.0, 203, 228, 253, 278 })
        {
            c.Linea(x, 25, x, 210, Croquis.Varilla, 1.5);
        }

        foreach (var y in new[] { 45.0, 80, 115, 150, 185 })
        {
            c.Linea(168, y, 287, y, Croquis.Varilla, 1.5);
        }

        c.Cota(178, 10, 203, 10, "C");
        c.Cota(157, 45, 157, 80, "D");
        c.Texto(230, 222, "Alzado", 9);
    }

    // ------------------------------------------------------------------ Armar cimentacion

    public static void ArmadoZapata(Croquis c)
    {
        // Corte a lo largo del lado mayor: la zapata, las dos parrillas y sus patas.
        c.Rect(20, 60, 220, 90, Croquis.Concreto);
        c.Rect(110, 5, 40, 55, Croquis.Concreto);

        c.Linea(30, 138, 230, 138, Croquis.Varilla, 2);
        c.Linea(30, 138, 30, 82, Croquis.Varilla, 2);
        c.Linea(230, 138, 230, 82, Croquis.Varilla, 2);

        foreach (var x in new[] { 45.0, 75, 105, 135, 165, 195, 225 })
        {
            c.Circulo(x, 132, 2.5, Croquis.Varilla);
        }

        c.Linea(30, 72, 230, 72, Croquis.Varilla, 1.5, true);
        c.Linea(30, 72, 30, 118, Croquis.Varilla, 1.5, true);
        c.Linea(230, 72, 230, 118, Croquis.Varilla, 1.5, true);

        c.Llamada(140, 138, 140, 180, "A");
        c.Llamada(75, 132, 75, 180, "B");
        c.Llamada(140, 72, 200, 40, "C");
        c.Llamada(60, 78, 25, 40, "D");
        c.Cota(245, 138, 245, 150, "E");
        c.Llamada(30, 100, 8, 100, "F");
        c.Texto(20, 205, "Corte; arriba (punteada) solo con doble parrilla", 9);
    }
}

