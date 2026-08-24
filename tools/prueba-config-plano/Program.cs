using CadLink.Cad.PlanoEstructural;

// =====================================================================================
//  PRUEBA EJECUTABLE DE LA HOJA CONFIG Y DE LAS CAPAS DEL PLANO ESTRUCTURAL
// =====================================================================================
//  Etapa 1 del port de la macro PLANOS ESTRUCTURALES. Lo que se comprueba aquí es que
//  los ~260 parámetros de la hoja CONFIG y las 21 capas con sus colores salgan EXACTAMENTE
//  como en la macro, porque todo lo que se dibuje después cuelga de estos valores.
//
//  Se ejecuta el CadLink.Cad compilado, no un port a otro lenguaje: un verificador que
//  copia la cuenta en Python puede dar verde con el C# equivocado.
//
//      cd tools/prueba-config-plano
//      dotnet run
//
//  Devuelve 0 si todo pasa y 1 si algo falla.
// =====================================================================================

var fallos = 0;

void Check(string que, bool ok)
{
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}");
    if (!ok)
    {
        fallos++;
    }
}

void Igual(string que, object esperado, object real)
{
    var ok = Equals(esperado, real);
    Console.WriteLine($"  {(ok ? "OK   " : "FALLA")} {que}" +
                      (ok ? string.Empty : $"   esperado <{esperado}>, salió <{real}>"));
    if (!ok)
    {
        fallos++;
    }
}

Console.WriteLine("=====================================================================");
Console.WriteLine(" LA HOJA CONFIG");
Console.WriteLine("=====================================================================");

var cfg = new ConfigPlano();

Igual("la hoja trae los 261 renglones de CrearHojaConfig", 261, ConfigPlano.PorOmision.Count);

var repes = ConfigPlano.PorOmision
    .GroupBy(r => r.Parametro, StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToList();
Check("ningún parámetro está dos veces" +
      (repes.Count == 0 ? string.Empty : ": " + string.Join(", ", repes)), repes.Count == 0);

Check("todos tienen descripción",
      ConfigPlano.PorOmision.All(r => r.Descripcion.Trim().Length > 0));

Igual("VERSION_CONFIG es la 29", 29d, ConfigPlano.VersionConfig);
Igual("VERSION_PARCHE es el 50", 50d, ConfigPlano.VersionParche);
Igual("y los dos están en la hoja", 29d, cfg.Numero("VERSION_CONFIG"));
Igual("VERSION_PARCHE en la hoja", 50d, cfg.Numero("VERSION_PARCHE"));

Console.WriteLine();
Console.WriteLine(" Los valores que mandan el dibujo");

Igual("FACTOR_UNIDADES", 1d, cfg.Numero("FACTOR_UNIDADES"));
Igual("PREFIJO_CAPAS", "E-", cfg.Texto("PREFIJO_CAPAS"));
Igual("ALTURA_TEXTO", 0.12, cfg.Numero("ALTURA_TEXTO"));
Igual("OFFSET_Y_INICIAL", 15d, cfg.Numero("OFFSET_Y_INICIAL"));
Igual("SEPARACION_ENTRE_PLANTAS", 5d, cfg.Numero("SEPARACION_ENTRE_PLANTAS"));
Igual("MALLA_SEP_CM", 15d, cfg.Numero("MALLA_SEP_CM"));
Igual("SEC_ALTURA", 0.12, cfg.Numero("SEC_ALTURA"));
Igual("CADENA_TEXTO_ALTURA", 0.09, cfg.Numero("CADENA_TEXTO_ALTURA"));
Igual("LOSA_TEXTO_ALTURA", 0.072, cfg.Numero("LOSA_TEXTO_ALTURA"));
Igual("LOSA_HATCH_ESCALA", 0.0475, cfg.Numero("LOSA_HATCH_ESCALA"));
Igual("LOSACERO_HATCH_ESCALA", 0.02, cfg.Numero("LOSACERO_HATCH_ESCALA"));
Igual("LOSACERO_FRANJA_ANCHO_M", 0.15, cfg.Numero("LOSACERO_FRANJA_ANCHO_M"));
Igual("COTAS_SEPARACION", 0.75, cfg.Numero("COTAS_SEPARACION"));
Igual("COTAS_SEPARACION_TOTAL", 1.17, cfg.Numero("COTAS_SEPARACION_TOTAL"));
Igual("EJES_INICIO_BURBUJA_M", 2d, cfg.Numero("EJES_INICIO_BURBUJA_M"));
Igual("EJES_SALE_CORTO_M", 0d, cfg.Numero("EJES_SALE_CORTO_M"));
Igual("PANO_SOLAPE_CM", 0d, cfg.Numero("PANO_SOLAPE_CM"));
Igual("PANO_BUSCA_CM", 150d, cfg.Numero("PANO_BUSCA_CM"));
Igual("PANO_ALARGAR_MAX_CM", 150d, cfg.Numero("PANO_ALARGAR_MAX_CM"));
Igual("PANO_ALMA_W_MODO", "ALMA", cfg.Texto("PANO_ALMA_W_MODO"));
Igual("COTA_EXT_LINE_EXT", 0d, cfg.Numero("COTA_EXT_LINE_EXT"));
Igual("COTA_EXT_LINE_OFFSET", 0.5, cfg.Numero("COTA_EXT_LINE_OFFSET"));
Igual("COTA_PRECISION", 3d, cfg.Numero("COTA_PRECISION"));
Igual("ESTILO_COTA", "COTA_DIM", cfg.Texto("ESTILO_COTA"));
Igual("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES", cfg.Texto("SEC_ESTILO_TEXTO"));
Igual("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS", cfg.Texto("CADENA_ESTILO_TEXTO"));
Igual("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS", cfg.Texto("LOSA_ESTILO_TEXTO"));
Igual("ROTULO_ESTILO_TEXTO", "HAETTENSCHWEILER", cfg.Texto("ROTULO_ESTILO_TEXTO"));
Igual("LOSA_HATCH_PATRON", "ANSI37", cfg.Texto("LOSA_HATCH_PATRON"));
Igual("LOSACERO_HATCH_PATRON", "FLEX", cfg.Texto("LOSACERO_HATCH_PATRON"));
Igual("CADENA_SIN_MURO_LINETYPE", "ACAD_ISO02W100", cfg.Texto("CADENA_SIN_MURO_LINETYPE"));
Igual("LINETYPE_EJES", "DASHDOT", cfg.Texto("LINETYPE_EJES"));
Igual("LINETYPE_TRABE", "PHANTOM2", cfg.Texto("LINETYPE_TRABE"));
Igual("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C",
      cfg.Texto("LOSACERO_TEXTO_PLANTILLA"));
Igual("ROTULO_TITULO con sus DOS espacios", "PLANTA  ESTRUCTURAL", cfg.Texto("ROTULO_TITULO"));
Igual("CIMENTACION_STORIES", "BASE,CIMENTACION,FOUNDATION", cfg.Texto("CIMENTACION_STORIES"));

Console.WriteLine();
Console.WriteLine(" Las banderas: SI / NO");

Check("las cuatro filas de cotas están prendidas",
      cfg.Bandera("COTAS_ARRIBA") && cfg.Bandera("COTAS_ABAJO")
      && cfg.Bandera("COTAS_IZQUIERDA") && cfg.Bandera("COTAS_DERECHA"));
Check("el armado SIEMPRE al paño", cfg.Bandera("ARMADO_PANO_SIEMPRE"));
Check("las vigas de acero en BYLAYER", cfg.Bandera("ACERO_LINEA_BYLAYER"));
Check("el alma de las columnas W", cfg.Bandera("PANO_ALMA_W"));
Check("las capas al frente", cfg.Bandera("TRAER_AL_FRENTE") && cfg.Bandera("PONER_SORTENTS_127"));
Check("y las que van en NO siguen en NO",
      !cfg.Bandera("ETIQUETA_ID_COLUMNAS", true)
      && !cfg.Bandera("CADENA_CORTA_LINEA", true)
      && !cfg.Bandera("LOSACERO_TEXTO_REDEFINIR", true)
      && !cfg.Bandera("DIBUJAR_EN_NUEVO_DIBUJO", true)
      && !cfg.Bandera("GIRAR_SECCIONES_90", true)
      && !cfg.Bandera("BLOQUE_SUFIJO_ROTACION", true));

Console.WriteLine();
Console.WriteLine(" La lectura, con las reglas de CfgS / CfgT / CfgD / CfgB");

// CfgT NO recorta: los espacios de LOSA_TEXTO_2 son los que dejan el hueco del número.
Igual("LOSA_TEXTO_2 conserva sus espacios de adelante",
      "       cm de espesor", cfg.TextoTalCual("LOSA_TEXTO_2"));
Check("y CfgS sí los recorta", cfg.Texto("LOSA_TEXTO_2") == "cm de espesor");

var libre = new ConfigPlano();
libre.Aplicar(new Dictionary<string, string>
{
    ["MALLA_SEP_CM"] = "0,5",          // coma decimal, como la escribe Excel en español
    ["ALTURA_TEXTO"] = "0.20 m",       // con basura detrás, como el Val de VBA
    ["COTA_TOTAL"] = "VERDADERO",
    ["LOSA_HATCH"] = "X",
    ["ACOTAR_EJES"] = "cualquier cosa" // no se reconoce: se queda con el de omisión
});

Igual("la coma decimal se lee como punto", 0.5, libre.Numero("MALLA_SEP_CM"));
Igual("y se lee lo que se pueda del principio", 0.20, libre.Numero("ALTURA_TEXTO"));
Check("VERDADERO y X también son SI",
      libre.Bandera("COTA_TOTAL") && libre.Bandera("LOSA_HATCH"));
Check("una celda que no se entiende deja el valor de omisión",
      libre.Bandera("ACOTAR_EJES", true) && !libre.Bandera("ACOTAR_EJES", false));
Igual("un parámetro que no existe devuelve lo que se le pase",
      7.5, cfg.Numero("NO_EXISTE_ESTE", 7.5));

Console.WriteLine();
Console.WriteLine(" Guardar: solo lo que el usuario cambió");

var guardado = libre.ParaGuardar();
Check("se guardan los cinco cambios y no los 261 renglones", guardado.Count == 5);
Check("y entre ellos está el que se tocó", guardado.ContainsKey("MALLA_SEP_CM"));

var virgen = new ConfigPlano();
virgen.Aplicar(new Dictionary<string, string> { ["MALLA_SEP_CM"] = "15" });
Check("un valor igual al de omisión no se guarda", virgen.ParaGuardar().Count == 0);

Console.WriteLine();
Console.WriteLine("=====================================================================");
Console.WriteLine(" LAS CAPAS Y SUS COLORES");
Console.WriteLine("=====================================================================");

var capas = new CapasPlano(cfg);

foreach (var c in capas.Todas)
{
    Console.WriteLine($"        {c.Nombre,-22} color {c.Color,3}" +
                      (c.TipoDeLinea.Length > 0 ? $"   {c.TipoDeLinea}" : string.Empty));
}

Console.WriteLine();

int ColorDe(string nombre) =>
    capas.Todas.FirstOrDefault(c => c.Nombre == nombre)?.Color ?? -1;

string LineaDe(string nombre) =>
    capas.Todas.FirstOrDefault(c => c.Nombre == nombre)?.TipoDeLinea ?? "(no está)";

// Los colores de la macro, uno por uno. NINGUNO se cambia.
Igual("E-MURO", 6, ColorDe("E-MURO"));
Igual("E-COLUMNA", 1, ColorDe("E-COLUMNA"));
Igual("E-CASTILLO", 1, ColorDe("E-CASTILLO"));
Igual("E-TRABE", 3, ColorDe("E-TRABE"));
Igual("E-CONTRATRABE", 2, ColorDe("E-CONTRATRABE"));
Igual("E-DALA", 12, ColorDe("E-DALA"));
Igual("E-LOSA", 8, ColorDe("E-LOSA"));
Igual("E-DIAGONAL", 30, ColorDe("E-DIAGONAL"));
Igual("E-OTROS", 7, ColorDe("E-OTROS"));
Igual("E-ACERO", 130, ColorDe("E-ACERO"));
Igual("E-TEXTO", 7, ColorDe("E-TEXTO"));
Igual("E-TITULO", 7, ColorDe("E-TITULO"));
Igual("E-EJES", 8, ColorDe("E-EJES"));
Igual("E-EJES-BURBUJA", 4, ColorDe("E-EJES-BURBUJA"));
Igual("E-EJES-TEXTO", 6, ColorDe("E-EJES-TEXTO"));
Igual("E-ARMADO LOSA", 142, ColorDe("E-ARMADO LOSA"));
Igual("E-MAMPOSTERIA", 30, ColorDe("E-MAMPOSTERIA"));
Igual("E-CADENA DESPLANTE", 1, ColorDe("E-CADENA DESPLANTE"));
Igual("PIERS, sin prefijo", 7, ColorDe("PIERS"));
Igual("E-LOSACERO", 6, ColorDe("E-LOSACERO"));
Igual("E-COTAS", 8, ColorDe("E-COTAS"));
Igual("son las 21 capas", 21, capas.Todas.Count);

Console.WriteLine();
Igual("la trabe lleva PHANTOM2", "PHANTOM2", LineaDe("E-TRABE"));
Igual("los ejes, DASHDOT", "DASHDOT", LineaDe("E-EJES"));
Igual("y la del acero no se toca (LINETYPE_ACERO vacío)", string.Empty, LineaDe("E-ACERO"));
Igual("la cadena de desplante va SIN tipo de línea, nunca punteada",
      string.Empty, LineaDe("E-CADENA DESPLANTE"));

Console.WriteLine();
Igual("el muro va a su capa", "E-MURO", capas.CapaDeTipo("MURO"));
Igual("el castillo a la suya", "E-CASTILLO", capas.CapaDeTipo("CASTILLO"));
Igual("y lo que no está en la tabla, a E-OTROS", "E-OTROS", capas.CapaDeTipo("LO QUE SEA"));

Igual("las capas al frente son las cuatro de la hoja",
      "E-DALA, E-CADENA DESPLANTE, E-TRABE, E-ACERO",
      string.Join(", ", capas.CapasAlFrente()));

Check("se reconoce lo generado por el plano",
      capas.EsCapaGenerada("E-LOSA") && capas.EsCapaGenerada("e-armado losa")
      && capas.EsCapaGenerada("PIERS") && capas.EsCapaGenerada("E-CADENA DESPLANTE"));
Check("y lo del usuario no se toca",
      !capas.EsCapaGenerada("MI CAPA") && !capas.EsCapaGenerada("0")
      && !capas.EsCapaGenerada("DEFPOINTS"));

Console.WriteLine();
Console.WriteLine(" Y si el usuario cambia un color en la hoja, manda la hoja");

var otra = new ConfigPlano();
otra.Aplicar(new Dictionary<string, string>
{
    ["COLOR_CASTILLO"] = "5",
    ["COLOR_ACERO"] = "999",           // fuera de rango: se regresa al de la macro
    ["PREFIJO_CAPAS"] = "EST-",
    ["CAPA_CADENA_DESPLANTE"] = "DESPLANTE"
});
var capas2 = new CapasPlano(otra);

Igual("el castillo toma el color de la hoja", 5,
      capas2.Todas.First(c => c.Nombre == "EST-CASTILLO").Color);
Igual("un color imposible se regresa al de la macro", 130,
      capas2.Todas.First(c => c.Nombre == "EST-ACERO").Color);
Igual("el prefijo también sale de la hoja", "EST-MURO", capas2.CapaDeTipo("MURO"));
Igual("y la cadena de desplante se lo pone si le falta",
      "EST-DESPLANTE", capas2.CapaCadenaDesplante);

var conPrefijo = new ConfigPlano();
conPrefijo.Aplicar(new Dictionary<string, string>
{
    ["CAPA_CADENA_DESPLANTE"] = "E-CADENA DESPLANTE"
});
Igual("y no lo pone dos veces si ya lo trae",
      "E-CADENA DESPLANTE", new CapasPlano(conPrefijo).CapaCadenaDesplante);

Console.WriteLine();
Console.WriteLine("=====================================================================");
if (fallos == 0)
{
    Console.WriteLine(" RESULTADO: todo bien");
}
else
{
    Console.WriteLine($" RESULTADO: {fallos} comprobación(es) fallaron");
}

Console.WriteLine("=====================================================================");
return fallos == 0 ? 0 : 1;
