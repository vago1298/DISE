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

// 278 y no los 261 de CrearHojaConfig: se añadieron DIECISIETE renglones que no están en
// su hoja, y todos porque se pidieron:
//   AIRE_SOBRE_LO_DIBUJADO_M        la planta se pone encima de lo que ya haya dibujado
//   CAPAS_TEXTO_AL_FRENTE           los rótulos, encima de todo, en una segunda pasada
//   CAPA_DALA                       la capa de las dalas se llama E-CADENA
//   DRAWORDER_POR_COMANDO           respaldo del orden de dibujo, con el DRAWORDER de verdad
//   LINEAS_AL_PANO                  las líneas mueren en el paño del castillo, no en su eje
//   CAPA_VOLADO / COLOR_VOLADO      la losa en voladizo, en su capa propia
//   APAGAR_CAPA_LOSA                E-LOSA apagada y E-VOLADO encendida
//   LOSA_CONTORNO_FUERA_DE_MUROS    sin línea de losa por dentro del muro ni de la cadena
//   VIGAS_CORTAR_EN_CRUCES          la viga muere en la cara de la que cruza
//   CIMENTACION_SIN_MUROS_SIN_COLUMNAS   sin muros, sin castillos en la base
//   CAPAS_AL_FONDO                  la losa y su armado, al fondo del orden de dibujo
//   VOLADO_POR_NOTA                 el volado se reconoce por su NOTA, no por la geometría
//   ARMADO_LOSA_BAYONETA            en el tablero apoyado va la bayoneta...
//   ARMADO_LOSA_PARRILLA            ...y NO la rejilla, que llenaba el plano
//   EJES_UNIR_TOL_CM                un eje, UNA línea: fuera los ejes repetidos
//   VOLADO_ROTULO_SOLO_ARMADO       en el volado el rótulo solo lleva la varilla
//   LOSA_HATCH_ESCALA_AUTO          ...y el achurado se VE, en vez de ser una mancha gris
//   LOSA_HATCH_SEPARACION_CM        (25 cm entre líneas, de donde sale la escala)
//   LOSA_HATCH_POR_COMANDO          y si la API no lo crea, se manda el -HATCH de verdad
//   VOLADO_CONTORNO_FUERA_DE_MUROS  la línea del volado, solo el contorno exterior
//   VOLADO_TEXTO_1                  «Losa de VOLADO» en el primer renglón de su rótulo
//   CORTE_DIBUJAR / CORTE_SEPARACION_M / CORTE_ESPESOR_CM / CORTE_ROTULO /
//   CORTE_ROTULO_ABAJO_M / CORTE_NIVEL_VUELA_M      el corte por un eje, al lado de la planta
//   LOSA_HATCH_COLOR                el achurado en 142, por objeto, como se pidio
//   VOLADO_SIN_DIVISIONES           varios volados juntos, un solo perimetro
//   CORTE_VER_EL_FONDO              el corte dibuja lo que se ve detras, no solo la rebanada
//   CORTE_FONDO_LINETYPE            y ese fondo va a trazos
//   SHELL_CASTILLO_COMO_COLUMNA     el shell que dice CASTILLO se dibuja como castillo
//   SHELL_CASTILLO_UNIR_TOL_CM      y sus pedazos se unen, para que salga completo
//   SHELL_CASTILLO_DE_OTRO_NIVEL    y el que cruza el nivel se dibuja aunque sea de otro story
//   SHELL_CASTILLO_PREFIJO          y se nombra con su medida: «K 15X23.5»
//   SHELL_CASTILLO_AL_PANO          y se alarga hasta el pano del muro con el que se cruza
//   CADENA_SOLO_LA_MAS_ALTA         de varias cadenas en la misma linea, en planta solo una
//   CADENA_ROTULO_EN_CASTILLO_AREA  y su nombre no va encima de un castillo de area
//   CADENA_DESPLANTE_CONTINUA       la de desplante, con linea continua en cualquier nivel
//   CAPA_MURO_CONCRETO / COLOR_MURO_CONCRETO / MURO_CONCRETO_CAPA_PROPIA
//                                   el muro de concreto sin cadena, en E-MURO DE CONCRETO
//   CORTE_RELLENAR_SOLO_EN_SECCION  se rellena lo que el corte cruza por su lado corto
//   CORTE_COLOR_RELLENO_CADENA      la cadena cortada, morada
//   CORTE_COLOR_RELLENO_TRABE       y la trabe, verde
Igual("la hoja trae los renglones de CrearHojaConfig, mas los cincuenta y uno que se añadieron",
      311, ConfigPlano.PorOmision.Count);

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
// 25 y no los 15 de la hoja: se pidió expresamente que, con el dibujo vacío, el juego
// arranque a Y = 25 en lugar de pegado al origen.
Igual("OFFSET_Y_INICIAL", 25d, cfg.Numero("OFFSET_Y_INICIAL"));
// 10.00 y no los 5.00 de la hoja: se pidió expresamente.
Igual("SEPARACION_ENTRE_PLANTAS", 10d, cfg.Numero("SEPARACION_ENTRE_PLANTAS"));
Igual("AIRE_SOBRE_LO_DIBUJADO_M", 5d, cfg.Numero("AIRE_SOBRE_LO_DIBUJADO_M"));
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

// Lo que manda los rótulos, los ejes de orilla y el bloque de la sección: son los cinco
// arreglos de esta pasada y conviene tener los valores escritos.
Igual("CAPA_PIERS, sin prefijo", "PIERS", cfg.Texto("CAPA_PIERS"));
Igual("COLOR_PIERS", 7d, cfg.Numero("COLOR_PIERS"));
Igual("PIER_SEPARACION_CM", 6d, cfg.Numero("PIER_SEPARACION_CM"));
Igual("COLUMNA_TEXTO_SEPARACION_CM", 2d, cfg.Numero("COLUMNA_TEXTO_SEPARACION_CM"));
Igual("COLOR_RELLENO_BLOQUE, el amarillo", 2d, cfg.Numero("COLOR_RELLENO_BLOQUE"));
Igual("BLOQUE_ROTACION_EXTRA_GRADOS", 0d, cfg.Numero("BLOQUE_ROTACION_EXTRA_GRADOS"));
Igual("EJES_PANO_TOL_CM", 25d, cfg.Numero("EJES_PANO_TOL_CM"));
Igual("LOSA_APOYO_CUBRE", 0.7, cfg.Numero("LOSA_APOYO_CUBRE"));
Igual("LOSA_HATCH_PATRON del volado", "ANSI37", cfg.Texto("LOSA_HATCH_PATRON"));
Igual("LOSA_HATCH_ANGULO", 45d, cfg.Numero("LOSA_HATCH_ANGULO"));
Igual("CADENA_SIN_MURO_LINETYPE", "ACAD_ISO02W100", cfg.Texto("CADENA_SIN_MURO_LINETYPE"));
Igual("CADENA_SIN_MURO_CUBRE", 0.5, cfg.Numero("CADENA_SIN_MURO_CUBRE"));
Igual("PANO_BUSCA_CM", 150d, cfg.Numero("PANO_BUSCA_CM"));
Igual("PALABRAS del volado", "VOLADO,VOLADIZO,VOLADA,CANTILEVER",
      cfg.Texto("LOSA_PALABRAS_VOLADO"));
Igual("LOSACERO_FRANJA_ANCHO_M", 0.15, cfg.Numero("LOSACERO_FRANJA_ANCHO_M"));
Igual("LOSACERO_FRANJA_SEP_M", 0.8, cfg.Numero("LOSACERO_FRANJA_SEP_M"));
Igual("LOSACERO_HATCH_PATRON", "FLEX", cfg.Texto("LOSACERO_HATCH_PATRON"));
Igual("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C",
      cfg.Texto("LOSACERO_TEXTO_PLANTILLA"));
Check("el volado se reconoce por su NOTA y en el tablero va la BAYONETA, no la rejilla",
      cfg.Bandera("VOLADO_POR_NOTA") && cfg.Bandera("ARMADO_LOSA_BAYONETA")
      && !cfg.Bandera("ARMADO_LOSA_PARRILLA", true));
Check("el volado en su capa, la losa apagada, el contorno fuera de los muros, las vigas " +
      "cortadas en los cruces y, sin muros, sin castillos en la base",
      cfg.Bandera("APAGAR_CAPA_LOSA") && cfg.Bandera("LOSA_CONTORNO_FUERA_DE_MUROS")
      && cfg.Bandera("VIGAS_CORTAR_EN_CRUCES") && cfg.Bandera("LOSA_HATCH_SOLO_VOLADO")
      && cfg.Bandera("CIMENTACION_SIN_MUROS_SIN_COLUMNAS")
      && cfg.Bandera("CADENA_SIN_MURO_MARCAR") && cfg.Bandera("CIMENTACION_SIN_PUNTEADA")
      && cfg.Bandera("LINEAS_AL_PANO") && cfg.Bandera("DIBUJAR_ARMADO_LOSA"));
Igual("MAMPOSTERIA_ANCHO", 0.06, cfg.Numero("MAMPOSTERIA_ANCHO"));
Igual("ESPESOR_MURO_CM", 15d, cfg.Numero("ESPESOR_MURO_CM"));
Check("los ejes de orilla van al paño y el rótulo de la cadena lleva fondo",
      cfg.Bandera("EJES_EXTREMOS_AL_PANO") && cfg.Bandera("CADENA_TEXTO_FONDO")
      && cfg.Bandera("CADENA_TEXTO_MTEXT") && cfg.Bandera("RELLENAR_COLUMNAS")
      && cfg.Bandera("COLUMNAS_COMO_BLOQUE"));

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

// LAS DOS CLAVES DEL CASTILLO DE SHELL, con sus valores de omision: encendido, y 2 cm de
// holgura para unir los pedazos del mismo castillo -el modelador lo dibuja en dos paneles,
// hasta el antepecho y del dintel arriba, y en planta los dos ocupan el mismo sitio-.
Check("el castillo de shell viene encendido",
      cfg.Bandera("SHELL_CASTILLO_COMO_COLUMNA", false));
Igual("y sus pedazos se unen con 2 cm de holgura",
      2.0, cfg.Numero("SHELL_CASTILLO_UNIR_TOL_CM", 0));
// Y el castillo de area que se dibuja de corrido en el alzado -asi que ETABS lo guarda en UN
// story- se trae a la planta que cruza, con 20 cm de holgura para el que solo llega a ella.
Check("el castillo de area de otro nivel viene encendido",
      cfg.Bandera("SHELL_CASTILLO_DE_OTRO_NIVEL", false));
// Y SOLO DONDE VA DE PISO A TECHO: tiene que cubrir esta fraccion del entrepiso. Sin eso, el
// castillo que solo TOCA el nivel salia en su planta y otra vez en la de arriba, duplicado.
Igual("y solo cuenta donde cubre tres cuartas partes del entrepiso",
      0.75, cfg.Numero("MURO_FRACCION_ENTREPISO", 0));
// Y se nombra con su medida en planta: «K 15X23.5», que es su rotulo y el nombre de su bloque.
Igual("el castillo de area se nombra con K", "K", cfg.Texto("SHELL_CASTILLO_PREFIJO"));
// Y SE ALARGA HASTA EL PANO del muro con el que se cruza: en el modelo los muros se dibujan por
// su EJE, asi que el shell del castillo llega a la linea del muro y en el plano se quedaba a
// media pared, como cortado.
Check("y se alarga hasta el pano del muro", cfg.Bandera("SHELL_CASTILLO_AL_PANO", false));

// LAS CADENAS: en planta solo la mas alta, y su nombre no va encima de un castillo de area.
Check("en planta solo se dibuja la cadena mas alta",
      cfg.Bandera("CADENA_SOLO_LA_MAS_ALTA", false));
Check("y su nombre no va encima de un castillo de area",
      !cfg.Bandera("CADENA_ROTULO_EN_CASTILLO_AREA", true));
// LA DE DESPLANTE, SIEMPRE CONTINUA: no lleva muro debajo POR DEFINICION -desplanta, es la
// primera-, asi que la regla de «sin muro debajo va a trazos» se las llevaba todas a la punteada.
Check("la cadena de desplante va continua en cualquier nivel",
      cfg.Bandera("CADENA_DESPLANTE_CONTINUA", false));

// EL MURO DE CONCRETO SIN CADENA, EN SU PROPIA CAPA: un muro de concreto es estructura -se arma y
// se cuela- y uno de block es cerramiento; apagando una capa se revisa uno sin el otro. Donde hay
// cadena manda la cadena, igual que en la mamposteria.
// LOS RELLENOS DEL CORTE, cada pieza de su color: el castillo amarillo -como en la planta-, la
// cadena morada y la trabe verde. En un corte por un muro hay tres piezas de concreto distintas a
// la vista y del contorno solo no se distinguen: las tres son un rectangulo.
// Y SOLO LO QUE SE VE EN SECCION: lo que el corte cruza por su lado corto -la cara donde va el
// armado-. Lo que se ve de costado va solo con su linea.
Check("se rellena solo lo que se ve en seccion",
      cfg.Bandera("CORTE_RELLENAR_SOLO_EN_SECCION", false));

Igual("la cadena cortada se rellena de morado",
      6.0, cfg.Numero("CORTE_COLOR_RELLENO_CADENA", 0));
Igual("y la trabe de verde", 3.0, cfg.Numero("CORTE_COLOR_RELLENO_TRABE", 0));

Igual("la capa del muro de concreto se llama como se pidio",
      "MURO DE CONCRETO", cfg.Texto("CAPA_MURO_CONCRETO"));
Check("y viene encendida", cfg.Bandera("MURO_CONCRETO_CAPA_PROPIA", false));

// EL ROTULO DE LA PLANTA, A -0.50 DE LOS EJES, como en la hoja de la macro. Se probo con 5 y se
// pidio volver: lo que estaba mal no era la distancia, sino DESDE DONDE se medía -desde la caja
// de los elementos en lugar de desde los ejes-, que es lo que los dejaba escalonados.
Igual("el rotulo va a medio metro de los ejes de abajo",
      0.5, cfg.Numero("ROTULO_SEPARACION_EJES", 0));

Console.WriteLine();
Console.WriteLine(" Guardar: solo lo que el usuario cambió");

var guardado = libre.ParaGuardar();
Check("se guardan los cinco cambios y no los 311 renglones", guardado.Count == 5);
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
// LA DALA SE LLAMA E-CADENA. El tipo sigue siendo DALA -es lo que devuelve
// ClasificaTipo- pero la capa lleva el nombre de la pieza en obra.
Igual("E-CADENA, la de las dalas", 12, ColorDe("E-CADENA"));
Igual("y ya no se llama E-DALA", -1, ColorDe("E-DALA"));
Igual("aunque el tipo DALA siga yendo a ella", "E-CADENA", capas.CapaDeTipo("DALA"));
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
// LA LOSA EN VOLADIZO, EN SU CAPA: es la 22, y la de la losa se queda APAGADA para que
// se vean los voladizos sin el contorno de todos los paños.
Igual("E-VOLADO, la de la losa en voladizo", 252, ColorDe("E-VOLADO"));
// VEINTITRES: las 22 de la macro mas E-MURO DE CONCRETO, que se pidio aparte para el muro de
// concreto que no lleva cadena.
Igual("son las 23 capas", 23, capas.Todas.Count);
Igual("y la del muro de concreto se llama E-MURO DE CONCRETO",
      "E-MURO DE CONCRETO", capas.CapaMuroConcreto);
Igual("con su color de la hoja", 4,
      capas.Todas.First(c => c.Nombre == "E-MURO DE CONCRETO").Color);
Igual("y la que se apaga es la de la losa", "E-LOSA",
      string.Join(", ", capas.CapasApagadas()));

Console.WriteLine();
Igual("la trabe lleva PHANTOM2", "PHANTOM2", LineaDe("E-TRABE"));
Igual("los ejes, DASHDOT", "DASHDOT", LineaDe("E-EJES"));
// LAS LINEAS DE E-ACERO, CONTINUAS: se pidió así. En la hoja de la macro este renglón va
// vacío -«no toques la que tenga el dibujo»- y por eso las vigas de acero salían a trazos
// cuando la capa ya venía con otra línea de un dibujo anterior.
Igual("y la del acero es CONTINUA", "Continuous", LineaDe("E-ACERO"));
Igual("la cadena de desplante va SIN tipo de línea, nunca punteada",
      string.Empty, LineaDe("E-CADENA DESPLANTE"));

Console.WriteLine();
Igual("el muro va a su capa", "E-MURO", capas.CapaDeTipo("MURO"));
Igual("el castillo a la suya", "E-CASTILLO", capas.CapaDeTipo("CASTILLO"));
Igual("y lo que no está en la tabla, a E-OTROS", "E-OTROS", capas.CapaDeTipo("LO QUE SEA"));
// EL CABEZAL, que se pidio leer de las notas, va con las TRABES: un cabezal es una viga -la que
// cierra un vano o la que reparte sobre los apoyos-. Sin esta traduccion se iria a E-OTROS, que
// es una capa que nadie mira, igual que les pasaba a las tres cadenas.
Igual("el cabezal va a la capa de las trabes", "E-TRABE", capas.CapaDeTipo("CABEZAL"));
Igual("y en minusculas igual", "E-TRABE", capas.CapaDeTipo("cabezal"));

Igual("las capas al frente son las cuatro de la hoja",
      "E-CADENA, E-CADENA DESPLANTE, E-TRABE, E-ACERO",
      string.Join(", ", capas.CapasAlFrente()));

// Y LOS TEXTOS APARTE, para subirlos en una SEGUNDA pasada: así los rótulos quedan
// siempre encima de la geometría y no según el orden en que los halle el recorrido.
// PIERS va SIN prefijo, como en la macro: con E-PIERS los piers se quedaban fuera.
// EL MTEXT NO VA AL FRENTE: tiene que quedar encima de la mamposteria pero DEBAJO de las
// lineas de la cadena y del acero, que es el orden que se pidio.
Igual("la lista de capas de texto al frente va VACIA", "",
      string.Join(", ", capas.CapasDeTextoAlFrente()));

// LA OTRA MITAD DEL ORDEN DE DIBUJO: la losa y su armado, AL FONDO. Da igual cuantas
// veces se suba la cadena si el achurado y la rejilla se dibujaron despues.
//
// Y E-EJES DE ULTIMA, que es lo que se pidio: las capas se van bajando una por una, asi
// que la ultima que se baja queda mas abajo de todas -DRAW ORDER -> SEND TO BACK-.
Igual("las capas al fondo, con los EJES de ultima",
      "E-LOSA, E-ARMADO LOSA, E-VOLADO, E-LOSACERO, E-EJES",
      string.Join(", ", capas.CapasAlFondo()));

var otrosTextos = new ConfigPlano();
otrosTextos.Aplicar(new Dictionary<string, string>
{
    ["CAPAS_TEXTO_AL_FRENTE"] = "TEXTO, E-TITULO ,PIERS,TEXTO"
});
Igual("se admite escribirlas con o sin prefijo, y no se repiten",
      "E-TEXTO, E-TITULO, PIERS",
      string.Join(", ", new CapasPlano(otrosTextos).CapasDeTextoAlFrente()));

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
