"""
Comprueba las CELDAS DE MEDIDA: que se pueda teclear la medida completa de corrido y que
los decimales que se guardan sean los que la celda ensena.

EL PROBLEMA QUE ESTO CIERRA, tal como lo reporto el usuario:

    «cuando yo pongo 1 en automatico pones 1.00 y debo editar a mano el .00 para poner
     .30, y quiero que sea 1.30 corrido»

La causa era una combinacion de dos cosas que por separado estan bien:

  StringFormat=N2                        -> la celda se ensena con dos decimales
  UpdateSourceTrigger=PropertyChanged    -> la fila se entera en CADA TECLA

Juntas, al teclear el «1» el enlace escribe 1 en la fila, la fila avisa, y el enlace
DEVUELVE el texto formateado a la celda: «1.00». El cursor se va al final y para escribir
«1.30» hay que borrar el «.00» a mano. Con cualquiera de las dos sola no pasa nada.

Asi que la regla es: LAS DOS NO VAN JUNTAS NUNCA. Las celdas con formato -las medidas-
confirman al salir de la celda; las que no llevan formato -numero de varillas, diametros,
f'c, los desplegables- siguen confirmando en cada tecla, que es lo que mueve la vista
previa mientras se escribe.

Y como ya no se confirma en cada tecla, hay un cabo que atar: un atajo de teclado no
quita el foco de la celda. Con Ctrl+S encima de una celda a medio escribir, lo teclado se
habria perdido. De eso se encarga CerrarEdicionDeLasHojas().

LOS DECIMALES: dos en las medidas, tres en las longitudes de las elevaciones. Se redondea
al guardar -no solo al ensenar- para que la celda no diga 1.23 mientras el plano se dibuja
con 1.234.
"""

import os
import re

fallos = []


def check(nombre, ok, detalle=""):
    if ok:
        print(f"  OK    {nombre}")
    else:
        print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
        fallos.append(nombre)


RAIZ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
APP = os.path.join(RAIZ, "client", "src", "CadLink.App")


def leer(*partes):
    with open(os.path.join(APP, *partes), encoding="utf-8") as f:
        return f.read()


XAML = leer("MainWindow.xaml")
MEDIDAS = leer("Models", "Medidas.cs")
FILAS = leer("Models", "StructuralRows.cs")
VENTANA = leer("MainWindow.xaml.cs")

print()
print("=" * 78)
print("LA MEDIDA SE TECLEA COMPLETA: FORMATO Y CONFIRMACION EN CADA TECLA NO VAN JUNTOS")
print("=" * 78)

#  LA REGLA, EN TODA LA VENTANA. Con una sola celda que quede, el usuario se vuelve a
#  topar con ella, y no hay manera de adivinar cual es sin recorrerlas todas.
juntos = re.findall(
    r"\{Binding [\w.]+, StringFormat=N\d, UpdateSourceTrigger=PropertyChanged\}", XAML)
juntos += re.findall(
    r"\{Binding [\w.]+, UpdateSourceTrigger=PropertyChanged, StringFormat=N\d\}", XAML)

check("ninguna celda lleva formato y confirmacion en cada tecla a la vez",
      not juntos, f"{len(juntos)} celdas: {juntos[:3]}")

#  Y LAS QUE NO LLEVAN FORMATO SIGUEN EN TIEMPO REAL: quitarselo a todas habria apagado
#  la vista previa mientras se escribe, que es lo que se pidio en una vuelta anterior.
check("las celdas sin formato siguen confirmando en cada tecla",
      XAML.count("UpdateSourceTrigger=PropertyChanged") > 30,
      f"{XAML.count('UpdateSourceTrigger=PropertyChanged')} celdas en tiempo real")

check("y la vista previa sigue escuchando la edicion de cada fila",
      "fila.PropertyChanged += OnFilaEditada;" in VENTANA)

#  EL COMENTARIO EN LA HOJA. Sin el, el primero que quiera vista previa en tiempo real en
#  una medida vuelve a poner el UpdateSourceTrigger y el problema regresa.
check("la hoja explica por que las medidas no llevan UpdateSourceTrigger",
      "LAS CELDAS DE MEDIDA NO LLEVAN UpdateSourceTrigger=PropertyChanged." in XAML
      and "1.30" in XAML)

print()
print("=" * 78)
print("DOS DECIMALES EN LAS MEDIDAS")
print("=" * 78)

#  TODAS LAS CELDAS DONDE SE TECLEA UNA MEDIDA, hoja por hoja. Antes estaban a N0, N1 y N2
#  segun quien escribio la columna: en la misma tabla se veia «15» al lado de «0.20».
DE_DOS_DECIMALES = [
    # Secciones de concreto
    "BaseCm", "AlturaCm", "RecubrimientoCm", "GanchoCm",
    # Perfiles de acero
    "PeralteCm", "AnchoCm", "LabioCm", "RadioCm", "AnchoMenorCm",
    # Zapatas, en metros como en la macro
    "AnchoM", "LargoM", "ProfundidadM", "EspesorM", "EspesorMuroCm",
    "AnchoDadoCm", "AnchoColumnaCm", "RecDadoCm",
    # Placa base
    "LargoCm", "SepBordeXCm", "SepBordeYCm",
    "LongCartabonXCm", "LongCartabonYCm", "AltoCartabonXCm", "AltoCartabonYCm",
    "LongAnclajeXCm", "LongAnclajeYCm",
    "DoblezAnclaXCm", "DoblezAnclaYCm",
    #  LongAnclaXCm y LongAnclaYCm YA NO ESTAN en esta lista, y no es un olvido: la
    #  hoja de placas dejo de tener casilla para la longitud TOTAL del ancla. Eran dos
    #  casillas para la misma barra -la vertical y el total- y se pisaban, asi que se
    #  quedo una, «Longitud de ancla X vertical», que es LongAnclajeXCm.
    #
    #  Las propiedades siguen existiendo y se siguen guardando en el .clk, para que un
    #  trabajo viejo se abra igual, pero sin celda no hay formato que comprobar. Lo que
    #  SI se comprueba -en validar.py- es que la casilla no haya vuelto por la puerta
    #  de atras y que en cero el alzado deduzca el largo de la longitud vertical.
]

faltan = [p for p in DE_DOS_DECIMALES
          if '{Binding %s, StringFormat=N2}' % p not in XAML]

check("las celdas de medida se ensenan con dos decimales",
      not faltan, f"{len(faltan)} sin N2: {faltan}")

#  ═══════════════════════════════════════════════════════════════════════════════════
#  Y NINGUNA SE QUEDA CON LOS DECIMALES DE ANTES, pero esto SOLO EN LAS HOJAS QUE SE
#  EDITAN.
#
#  Buscarlo en toda la ventana da dos falsos positivos: la tabla de secciones del modelo
#  de ETABS -IsReadOnly- ensena el peralte y el ancho que trae el modelo con un decimal,
#  y ahi nadie teclea nada. La regla es de las celdas donde se METE una medida.
#  ═══════════════════════════════════════════════════════════════════════════════════
HOJAS_QUE_SE_EDITAN = [
    "SeccionesGrid", "AceroGrid", "ZapatasCorridasGrid", "ZapatasGrid", "PlacasGrid",
]


def columnas_de(hoja):
    i = XAML.find('x:Name="%s"' % hoja)
    j = XAML.rfind("</DataGrid.Columns>", i, XAML.find("</DataGrid>", i))

    return XAML[i:j] if i >= 0 and j > i else ""


EDITABLES = "\n".join(columnas_de(h) for h in HOJAS_QUE_SE_EDITAN)

check("se encontraron las cinco hojas que se editan",
      all(columnas_de(h) for h in HOJAS_QUE_SE_EDITAN)
      and EDITABLES.count("DataGridTextColumn") > 40,
      f"{EDITABLES.count('DataGridTextColumn')} columnas de texto")

sobran = [p for p in DE_DOS_DECIMALES
          if re.search(r"\{Binding %s, StringFormat=N[013456789]" % p, EDITABLES)]

check("y ninguna se queda con N0 ni con N1",
      not sobran, f"{len(sobran)} con otros decimales: {sobran}")

print()
print("=" * 78)
print("TRES DECIMALES DONDE HACEN FALTA")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  LA LONGITUD DE LAS ELEVACIONES: el largo en metros del que sale el ALZADO. Ahi se
#  captura el milimetro -3.125 m-, y con dos decimales el alzado saldria 5 mm corto.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("el largo del alzado admite tres decimales",
      '{Binding LongitudM, StringFormat=N3}' in XAML
      and "nameof(SeccionConcretoRow.LongitudM)" in MEDIDAS)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  Y LOS ESPESORES DEL PERFIL, por un motivo distinto: salen del CATALOGO, donde un alma
#  de 1/4" son 0.635 cm exactos. Redondear a 0.64 cambia el perfil, y ademas la fila
#  dejaria de coincidir con el catalogo del que se eligio.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("los espesores del perfil tambien, porque salen del catalogo",
      '{Binding EspesorAlmaCm, StringFormat=N3}' in XAML
      and '{Binding EspesorPatinCm, StringFormat=N3}' in XAML
      and "nameof(PerfilAceroRow.EspesorAlmaCm)" in MEDIDAS
      and "nameof(PerfilAceroRow.EspesorPatinCm)" in MEDIDAS)

check("y el motivo esta escrito, no solo la excepcion",
      "0.635" in MEDIDAS and "catálogo" in MEDIDAS)

#  LO QUE NO ES UNA MEDIDA SE QUEDA COMO ESTABA: la escala es 1:50 y el dado es una
#  columna calculada de solo lectura, que validar.py comprueba aparte.
check("lo que no es una medida no se toca: la escala sigue sin decimales",
      '{Binding Escala, StringFormat=N0}' in XAML)

check("y el dado calculado sigue con un decimal",
      '{Binding DadoXCm, StringFormat=N1}' in XAML
      and '{Binding DadoYCm, StringFormat=N1}' in XAML)

print()
print("=" * 78)
print("LO QUE SE GUARDA ES LO QUE SE VE")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  EL REDONDEO VA EN Row.Set, QUE ES POR DONDE PASAN TODAS LAS PROPIEDADES.
#
#  Ponerlo en cada propiedad seria ciento sesenta sitios donde olvidarse de uno, y el
#  olvido no se ve: la celda ensena 1.23 y el plano se dibuja con 1.234.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("el redondeo esta en Row.Set, por donde pasan todas las propiedades",
      "protected void Set<T>(ref T field, T value" in FILAS
      and "if (value is double medida)" in FILAS
      and "value = (T)(object)Medidas.Redondear(medida, name);" in FILAS)

#  ANTES DE COMPARAR. Si se redondeara despues, teclear el mismo valor ya redondeado
#  avisaria de un cambio que no hubo y la vista previa se redibujaria de gorra.
check("y va ANTES de comparar con el valor que ya tenia",
      FILAS.index("value = (T)(object)Medidas.Redondear(medida, name);")
      < FILAS.index("if (EqualityComparer<T>.Default.Equals(field, value))"))

check("los decimales estan en un solo sitio",
      "public const int Decimales = 2;" in MEDIDAS
      and "public const int DecimalesDeLongitud = 3;" in MEDIDAS
      and "public static int DecimalesDe(string? propiedad)" in MEDIDAS)

#  POR nameof Y NO POR TEXTO: si algun dia se renombra la propiedad, el compilador obliga
#  a renombrarla aqui tambien en lugar de dejar la excepcion muerta sin avisar.
check("las excepciones van por nameof, no por texto",
      'ConTresDecimales' in MEDIDAS
      and '"LongitudM"' not in MEDIDAS
      and '"EspesorAlmaCm"' not in MEDIDAS)

#  AL ALZA EN EL MEDIO, no al par: Math.Round por omision deja 0.125 en 0.12, y en una
#  medida eso sorprende -se teclea y sale otra cosa sin explicacion-.
check("el medio se redondea al alza, no al par",
      "MidpointRounding.AwayFromZero" in MEDIDAS)

#  Y NO SE REDONDEA LO QUE NO ES UN NUMERO: Math.Round revienta con NaN? No, pero lo
#  devuelve tal cual y mas vale decirlo que dejarlo al azar de la implementacion.
check("un NaN o un infinito se devuelven tal cual",
      "double.IsNaN(valor) || double.IsInfinity(valor)" in MEDIDAS)

print()
print("=" * 78)
print("LA CELDA ABIERTA SE CIERRA ANTES DE LEER LA HOJA")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  ESTO ES EL CABO QUE DEJA SUELTO QUITAR LA CONFIRMACION EN CADA TECLA.
#
#  Al salir de la celda -Tab, Enter, o pulsar un boton, que tambien quita el foco- WPF
#  confirma solo. Lo que NO quita el foco es un ATAJO DE TECLADO: con Ctrl+S encima de
#  una celda a medio escribir, lo teclado no se habria guardado.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("hay una manera de cerrar la celda abierta",
      "private void CerrarEdicionDeLasHojas()" in VENTANA
      and "CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true)" in VENTANA)

#  LA FILA Y NO SOLO LA CELDA: con CommitEdit de celda la fila se queda abierta y la
#  siguiente lectura vuelve a ver el valor viejo.
check("se cierra la FILA, no solo la celda",
      "DataGridEditingUnit.Row" in VENTANA
      and "DataGridEditingUnit.Cell" not in VENTANA)

check("y se cierran las seis hojas que se editan",
      all(h in VENTANA for h in [
          "SeccionesGrid, AceroGrid, ZapatasCorridasGrid, ZapatasGrid, PlacasGrid, PlanosGrid",
      ]))

#  UNA HOJA QUE NO ESTA EN EDICION TIRA InvalidOperationException, y eso no es un fallo:
#  sin el catch, pulsar el boton sin haber editado nada reventaria.
check("una hoja que no estaba en edicion no revienta",
      "catch (InvalidOperationException)" in VENTANA)

#  Y SE LLAMA DESDE TODO LO QUE LEE LAS HOJAS. Faltando una, esa se queda con el valor de
#  antes de la ultima tecleada, que es el mismo problema con otra cara.
LECTORES = [
    ("MainWindow.xaml.cs", "OnExport"),
    ("MainWindow.xaml.cs", "OnValidate"),
    ("MainWindow.xaml.cs", "OnExportAlzados"),
    ("MainWindow.xaml.cs", "OnDibujarPlantaCad"),
    ("MainWindow.xaml.cs", "Guardar"),
    ("MainWindow.Acero.cs", "OnExportAcero"),
    ("MainWindow.Zapatas.cs", "OnRevisarZapatas"),
    ("MainWindow.Zapatas.cs", "OnExportZapatas"),
    ("MainWindow.ZapatasCorridas.cs", "OnRevisarZapatasCorridas"),
    ("MainWindow.ZapatasCorridas.cs", "OnExportZapatasCorridas"),
    ("MainWindow.PlacaBase.cs", "OnDibujarPlacaBase"),
    ("MainWindow.Solapas.cs", "OnGenerarSolapas"),
]

sin_cerrar = []

for archivo, metodo in LECTORES:
    texto = leer(archivo)
    i = texto.find(f" {metodo}(")

    if i < 0:
        sin_cerrar.append(f"{metodo} no existe")
        continue

    # Lo primero que hace el metodo, antes de cualquier lectura de la hoja.
    if "CerrarEdicionDeLasHojas();" not in texto[i:i + 400]:
        sin_cerrar.append(metodo)

check("todo lo que lee las hojas cierra la celda abierta primero",
      not sin_cerrar, f"{len(sin_cerrar)} sin cerrar: {sin_cerrar}")

check("y son los doce que leen las hojas, no menos",
      len(LECTORES) == 12)

print()
print("=" * 78)

if fallos:
    print(f"FALLARON {len(fallos)} COMPROBACIONES:")

    for f in fallos:
        print("  -", f)

    print("=" * 78)
    raise SystemExit(1)

print("OK: la medida se teclea completa y los decimales cuadran.")
print("=" * 78)
