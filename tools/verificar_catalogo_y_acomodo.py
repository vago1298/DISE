"""Verifica el lector del catalogo de perfiles y el acomodo del acero.

Dos cosas que no se pueden comprobar leyendo el codigo:

  1. EL LECTOR DEL CSV. Lo va a escribir una persona exportando de Excel, asi que
     tiene que tragarse comentarios, lineas en blanco, punto y coma o coma de
     separador, punto o coma de decimal, campos vacios y una cabecera. Se le pasan
     todos esos casos y se comprueba que saca lo que debe y que no se cae.

  2. EL ACOMODO A LA IZQUIERDA DEL ORIGEN. El acero se dibuja desde x = -0.6 hacia
     la izquierda, como las macros. Lo que hay que comprobar es que los perfiles NO
     SE PISAN entre ellos, que ninguno se mete en el semiplano positivo -donde
     dibuja el concreto- y que el sitio de cada uno es el MISMO se dibuje la hoja
     entera o se salte la mitad.
"""

import math
import re

fallos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


# ===========================================================================
#  1. El lector del catalogo
# ===========================================================================

FAMILIAS = ("IR", "OR", "OC", "CF")


def familia_del_nombre(perfil):
    """Port de FamiliaPerfil.DelNombre."""
    p = (perfil or "").strip().upper()

    if not p:
        return None

    for pre, fam in (("HSS", "OR"), ("OR", "OR"), ("PTR", "OR"),
                     ("PIPE", "OC"), ("OC", "OC"), ("TUBO", "OC"),
                     ("CF", "CF"), ("CANAL", "CF"), ("MONTEN", "CF"),
                     ("W", "IR"), ("IR", "IR"), ("IPR", "IR")):
        if p.startswith(pre):
            return fam

    return None


def numero(campos, i):
    """Port de CatalogoPerfiles.Numero."""
    if i >= len(campos):
        return 0.0

    texto = campos[i].strip().replace(",", ".")

    try:
        return float(texto)
    except ValueError:
        return 0.0


def leer_catalogo(lineas):
    """Port de CatalogoPerfiles.Leer."""
    perfiles = []

    for cruda in lineas:
        linea = (cruda or "").strip()

        if not linea or linea.startswith("#"):
            continue

        sep = ";" if ";" in linea else ","
        campos = linea.split(sep)

        if len(campos) < 3:
            continue

        familia = campos[0].strip().upper()
        nombre = campos[1].strip()

        if not nombre:
            continue

        if familia not in FAMILIAS:
            familia = familia_del_nombre(nombre) or ""

            if not familia:
                continue

        if numero(campos, 2) <= 0:
            continue

        perfiles.append({
            "familia": familia,
            "nombre": nombre,
            "peralte": numero(campos, 2),
            "ancho": numero(campos, 3),
            "e_alma": numero(campos, 4),
            "e_patin": numero(campos, 5),
            "labio": numero(campos, 6),
            "radio": numero(campos, 7),
        })

    return perfiles


print("=" * 78)
print(" El lector del catalogo de perfiles")
print("=" * 78)

# El archivo que se entrega con el programa tiene que leerse, y sus cuatro perfiles
# tienen que salir con la familia que les toca.
with open("client/src/CadLink.App/perfiles-acero.csv", encoding="utf-8") as f:
    entregado = leer_catalogo(f.readlines())

print(f"\n    del archivo entregado salen {len(entregado)} perfiles: "
      + ", ".join(f"{p['nombre']} ({p['familia']})" for p in entregado))

check("el catalogo que se entrega se lee", len(entregado) == 4,
      f"salieron {len(entregado)}")
check("y trae uno de cada familia",
      {p["familia"] for p in entregado} == set(FAMILIAS))

# Las medidas del IR de la semilla, que son las que llegan a la fila al elegirlo.
ir = next((p for p in entregado if p["familia"] == "IR"), None)
check("el IR de la semilla trae sus cuatro medidas",
      ir is not None and ir["peralte"] > 0 and ir["ancho"] > 0
      and ir["e_alma"] > 0 and ir["e_patin"] > 0,
      str(ir))

# El OC no lleva ancho ni espesor de patin: es redondo y su pared es una sola.
oc = next((p for p in entregado if p["familia"] == "OC"), None)
check("el OC no trae ancho ni espesor de patin",
      oc is not None and oc["ancho"] == 0 and oc["e_patin"] == 0, str(oc))

# El CF es el unico que trae labio y radio.
cf = next((p for p in entregado if p["familia"] == "CF"), None)
check("solo el CF trae labio y radio",
      cf is not None and cf["labio"] > 0 and cf["radio"] > 0
      and all(p["labio"] == 0 and p["radio"] == 0
              for p in entregado if p["familia"] != "CF"),
      str(cf))

# ---- Lo que el usuario le va a echar de verdad ----
print("\n" + "-" * 78)
print(" Casos raros que el archivo va a traer")
print("-" * 78)

CASOS = [
    ("una cabecera exportada de Excel",
     ["familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio",
      "IR;W14X30;35.1;17.1;0.69;0.99;;"],
     1),

    ("separador de COMA en vez de punto y coma",
     ["IR,W16X26,39.9,14.0,0.64,0.86,,"],
     1),

    ("decimal con COMA, como sale de un Excel en español",
     ["IR;W16X26;39,9;14,0;0,64;0,86;;"],
     1),

    ("lineas en blanco y comentarios en medio",
     ["", "# los IR", "IR;W12X30;31.3;16.5;0.66;1.11;;", "   ", "# fin"],
     1),

    ("sin la columna de familia, deducida del nombre",
     [";HSS4X4X1/4;10.16;10.16;0.635;;;"],
     1),

    ("una familia inventada, con nombre que no la delata",
     ["XX;LO QUE SEA;10;10;1;;;"],
     0),

    ("un renglon sin peralte, que no se puede dibujar",
     ["IR;W12X30;;16.5;0.66;1.11;;"],
     0),

    ("un renglon con solo dos campos",
     ["IR;W12X30"],
     0),

    ("campos de sobra al final, por una columna extra en el Excel",
     ["IR;W12X30;31.3;16.5;0.66;1.11;;;56.8;peso"],
     1),

    ("espacios de sobra por todos lados",
     ["  IR ;  W12X30  ; 31.3 ; 16.5 ; 0.66 ; 1.11 ; ; "],
     1),
]

for nombre, lineas, esperados in CASOS:
    salieron = leer_catalogo(lineas)

    print(f"\n    {nombre}: {len(salieron)} perfil(es)")

    check(f"'{nombre}': salen {esperados}", len(salieron) == esperados,
          f"salieron {len(salieron)}")

    if esperados == 1 and salieron:
        p = salieron[0]

        check(f"'{nombre}': la familia se resuelve", p["familia"] in FAMILIAS,
              p["familia"])
        check(f"'{nombre}': el peralte se lee", p["peralte"] > 0, str(p["peralte"]))

# El decimal con coma tiene que dar EL MISMO numero que con punto: si no, un 39,9 se
# leeria como 399 y el perfil saldria diez veces mas grande.
con_punto = leer_catalogo(["IR;W16X26;39.9;14.0;0.64;0.86;;"])[0]
con_coma = leer_catalogo(["IR;W16X26;39,9;14,0;0,64;0,86;;"])[0]

check("el decimal con coma da el mismo numero que con punto",
      all(abs(con_punto[k] - con_coma[k]) < 1e-12
          for k in ("peralte", "ancho", "e_alma", "e_patin")),
      f"{con_punto} contra {con_coma}")

# Y una linea de basura no puede tumbar el catalogo entero: se salta y los demas pasan.
mezclado = leer_catalogo([
    "IR;W12X30;31.3;16.5;0.66;1.11;;",
    "esto no es un perfil",
    "IR;W14X30;35.1;17.1;0.69;0.99;;",
])

check("una linea de basura se salta sin tumbar el resto", len(mezclado) == 2,
      f"salieron {len(mezclado)}")


# ===========================================================================
#  2. El acomodo a la izquierda del origen
# ===========================================================================

print("\n" + "=" * 78)
print(" El acomodo del acero: a la izquierda del origen, desde -0.6")
print("=" * 78)

ORIGEN_CM = -60          # el xDerechaActual = -0.6 de las macros, en cm
AIRE_CM = 55
ESCALA = 0.01


def acomodar(perfiles, saltados=()):
    """El acomodo del OnExportAcero: de -0.6 hacia la izquierda.

    'perfiles' son los anchos de dibujo en cm. Devuelve la lista de (x_izq, x_der)
    de cada uno, en unidades de dibujo. Los saltados NO se dibujan, pero SI avanzan
    el hueco: es lo que hace que el sitio de cada perfil no dependa de cuales se
    dibujaron.
    """
    x_der = ORIGEN_CM * ESCALA
    huecos = []

    for i, ancho_cm in enumerate(perfiles):
        ancho = ancho_cm * ESCALA
        x_izq = x_der - ancho

        huecos.append(None if i in saltados else (x_izq, x_der))

        x_der = x_izq - AIRE_CM * ESCALA

    return huecos


# Anchos de dibujo: el IR de ejemplo, un OR doble, un OC y un CF doble.
ANCHOS = [16.5, 2 * 15.24, 11.43, 2 * 5.0]

huecos = acomodar(ANCHOS)

print()
for ancho, hueco in zip(ANCHOS, huecos):
    print(f"    perfil de {ancho:6.2f} cm  ->  de x={hueco[0]:+.4f} a {hueco[1]:+.4f}")

check("el primer perfil acaba justo en -0.6",
      abs(huecos[0][1] - (-0.6)) < 1e-12, f"{huecos[0][1]:+.6f}")

check("todos quedan a la izquierda del origen",
      all(h[1] <= 0 for h in huecos),
      str([f"{h[1]:+.4f}" for h in huecos]))

# Que no se pisen: el borde izquierdo de uno tiene que quedar a la derecha del borde
# derecho del siguiente, y con el aire completo entre medias.
for i in range(len(huecos) - 1):
    izq_actual = huecos[i][0]
    der_siguiente = huecos[i + 1][1]

    check(f"el perfil {i + 1} y el {i + 2} no se pisan",
          der_siguiente <= izq_actual + 1e-12,
          f"{der_siguiente:+.6f} contra {izq_actual:+.6f}")

    check(f"y entre ellos queda el aire de {AIRE_CM} cm",
          abs((izq_actual - der_siguiente) - AIRE_CM * ESCALA) < 1e-12,
          f"{(izq_actual - der_siguiente) / ESCALA:.4f} cm")

# El ancho de un perfil DOBLE es el doble: si no, el segundo se saldria de su hueco.
check("el hueco de un doble mide el doble que el de un simple",
      abs((huecos[1][1] - huecos[1][0]) - 2 * 15.24 * ESCALA) < 1e-12)

# ---- Y lo que de verdad importa: los sitios NO cambian si se saltan perfiles ----
print("\n" + "-" * 78)
print(" El sitio de cada perfil no depende de cuales se dibujen")
print("-" * 78)

completo = acomodar(ANCHOS)
con_saltados = acomodar(ANCHOS, saltados=(0, 2))

print()
for i, (a, b) in enumerate(zip(completo, con_saltados)):
    estado = "saltado" if b is None else f"de {b[0]:+.4f} a {b[1]:+.4f}"
    print(f"    perfil {i + 1}: dibujando todos {a[0]:+.4f}   "
          f"saltando el 1 y el 3 -> {estado}")

iguales = all(
    b is None or (abs(a[0] - b[0]) < 1e-12 and abs(a[1] - b[1]) < 1e-12)
    for a, b in zip(completo, con_saltados))

check("los que se dibujan caen en el MISMO sitio aunque otros se salten", iguales)

# Y la prueba de que hacia falta: si los saltados no avanzaran el hueco, el segundo
# perfil se dibujaria encima del primero.
def acomodar_mal(perfiles, saltados=()):
    """El acomodo INGENUO: los saltados no avanzan el hueco."""
    x_der = ORIGEN_CM * ESCALA
    huecos = []

    for i, ancho_cm in enumerate(perfiles):
        ancho = ancho_cm * ESCALA
        x_izq = x_der - ancho

        if i in saltados:
            huecos.append(None)
            continue

        huecos.append((x_izq, x_der))
        x_der = x_izq - AIRE_CM * ESCALA

    return huecos


mal = acomodar_mal(ANCHOS, saltados=(0,))
bien = acomodar(ANCHOS, saltados=(0,))

# En el ingenuo, el perfil 2 ocupa el hueco del 1, que ya esta dibujado en el plano.
encima = abs(mal[1][1] - completo[0][1]) < 1e-12

print(f"\n    sin avanzar el hueco, el perfil 2 acabaria en x={mal[1][1]:+.4f}, "
      f"justo donde acaba el 1 ({completo[0][1]:+.4f})")
print(f"    avanzandolo, acaba en x={bien[1][1]:+.4f}, en su sitio")

check("sin avanzar el hueco de los saltados, el siguiente se dibuja encima", encima)
check("y avanzandolo, no", abs(bien[1][1] - completo[0][1]) > 1e-9)


# ===========================================================================
#  3. Que el codigo sea el que se acaba de comprobar
# ===========================================================================

print("\n" + "-" * 78)
print(" Que el codigo diga estos mismos numeros")
print("-" * 78)

with open("client/src/CadLink.App/MainWindow.Acero.cs", encoding="utf-8") as f:
    codigo = f.read()

check("el origen del acero es -60 cm, el -0.6 de las macros",
      "OrigenAceroCm = -60" in codigo)
check("el aire entre perfiles es de 55 cm",
      "AireEntrePerfilesCm = 55" in codigo)
check("se dibuja de derecha a izquierda",
      "var xIzquierda = xDerecha - ancho;" in codigo)
check("y el hueco se avanza SIEMPRE, tambien para los saltados",
      re.search(r"xDerecha = xIzquierda - \(AireEntrePerfilesCm \* escala\);",
                codigo) is not None
      and "El hueco se avanza SIEMPRE" in codigo)

# ===========================================================================
#  4. La vuelta completa: Excel -> CSV -> lo que lee el programa
# ===========================================================================
#
# Es la prueba que de verdad importa del convertidor: no que corra, sino que lo que
# escribe lo entienda el lector del programa y con los MISMOS numeros. Se fabrica un
# xlsx con lo que trae una hoja de verdad -titulos arriba, filas en blanco, columnas
# en otro orden, familias sin poner y dos renglones malos-, se convierte y se lee.

import os
import subprocess
import tempfile
import zipfile

print("\n" + "=" * 78)
print(" La vuelta completa: Excel -> CSV -> catalogo")
print("=" * 78)


def escribir_xlsx(ruta, filas):
    """Un xlsx minimo, escrito con la biblioteca estandar."""
    def celda(i, j, v):
        ref = ""
        n = j + 1

        while n:
            n, r = divmod(n - 1, 26)
            ref = chr(65 + r) + ref

        return f'<c r="{ref}{i + 1}" t="inlineStr"><is><t>{v}</t></is></c>'

    cuerpo = "".join(
        f'<row r="{i + 1}">'
        + "".join(celda(i, j, v) for j, v in enumerate(f) if v != "")
        + "</row>"
        for i, f in enumerate(filas))

    hoja = ('<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/'
            f'spreadsheetml/2006/main"><sheetData>{cuerpo}</sheetData></worksheet>')

    with zipfile.ZipFile(ruta, "w") as z:
        z.writestr("xl/worksheets/sheet1.xml", hoja)
        z.writestr("[Content_Types].xml", "<Types/>")


HOJA = [
    ["TABLA DE PERFILES", "", "", "", "", ""],
    ["Proyecto: lo que sea", "", "", "", "", ""],
    [],
    # Encabezados en nomenclatura de catalogo, y en otro orden que el CSV.
    ["Designacion", "Familia", "d", "bf", "tw", "tf"],
    ["W12X30", "", "31.3", "16.5", "0.66", "1.11"],
    ["W14X30", "IR", "35.1", "17.1", "0.69", "0.99"],
    ["HSS6X6X1/4", "", "15.24", "15.24", "0.635", ""],
    ["PIPE 4 STD", "", "11.43", "", "0.602", ""],
    ["LO QUE SEA", "", "10", "10", "1", ""],       # familia imposible de deducir
    ["W16X26", "", "", "14.0", "0.64", "0.86"],    # sin peralte
]

with tempfile.TemporaryDirectory() as tmp:
    xlsx = os.path.join(tmp, "perfiles.xlsx")
    escribir_xlsx(xlsx, HOJA)

    r = subprocess.run(
        ["python3", "tools/catalogo_desde_excel.py", xlsx],
        capture_output=True, text=True)

    check("el convertidor corre sin caerse", r.returncode == 0, r.stderr[-300:])

    convertidos = leer_catalogo(r.stdout.splitlines())

    print(f"\n    del xlsx salieron {len(convertidos)} perfiles: "
          + ", ".join(f"{p['nombre']} ({p['familia']})" for p in convertidos))

    # Cuatro buenos de seis renglones: los dos malos se saltan CON MOTIVO.
    check("salen los cuatro perfiles buenos", len(convertidos) == 4,
          f"salieron {len(convertidos)}")
    check("y se dice por que se saltaron los otros dos",
          "no se sabe de que familia es" in r.stderr and "sin peralte" in r.stderr)

    # Las familias, deducidas del nombre cuando la columna venia vacia.
    porfam = {p["familia"] for p in convertidos}
    check("las familias se deducen del nombre", porfam == {"IR", "OR", "OC"},
          str(porfam))

    # Y LOS NUMEROS: los del xlsx, no otros. Es lo que hace que valga la pena.
    w12 = next((p for p in convertidos if p["nombre"] == "W12X30"), None)

    check("el W12X30 llega con sus cuatro medidas exactas",
          w12 is not None
          and abs(w12["peralte"] - 31.3) < 1e-9
          and abs(w12["ancho"] - 16.5) < 1e-9
          and abs(w12["e_alma"] - 0.66) < 1e-9
          and abs(w12["e_patin"] - 1.11) < 1e-9,
          str(w12))

    pipe = next((p for p in convertidos if p["nombre"] == "PIPE 4 STD"), None)

    check("el tubo redondo llega sin ancho ni espesor de patin",
          pipe is not None and pipe["ancho"] == 0 and pipe["e_patin"] == 0,
          str(pipe))

    # ---- Las unidades: se avisa, no se convierte solo ----
    HOJA_MM = [
        ["Designacion", "Familia", "d", "bf", "tw", "tf"],
        ["W12X30", "IR", "313", "165", "6.6", "11.1"],
    ]

    xlsx_mm = os.path.join(tmp, "milimetros.xlsx")
    escribir_xlsx(xlsx_mm, HOJA_MM)

    sin_flag = subprocess.run(
        ["python3", "tools/catalogo_desde_excel.py", xlsx_mm],
        capture_output=True, text=True)

    con_flag = subprocess.run(
        ["python3", "tools/catalogo_desde_excel.py", xlsx_mm, "--mm"],
        capture_output=True, text=True)

    en_cm = leer_catalogo(con_flag.stdout.splitlines())
    tal_cual = leer_catalogo(sin_flag.stdout.splitlines())

    print(f"\n    en milimetros y sin pedir conversion: peralte "
          f"{tal_cual[0]['peralte']}")
    print(f"    pidiendola con --mm: peralte {en_cm[0]['peralte']}")

    check("una hoja en milimetros se avisa", "MILIMETROS" in sin_flag.stderr)
    check("pero NO se convierte sola",
          abs(tal_cual[0]["peralte"] - 313) < 1e-9)
    check("y con --mm si se convierte",
          abs(en_cm[0]["peralte"] - 31.3) < 1e-9, str(en_cm[0]))


print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f_ in fallos:
        print("   - " + f_)
else:
    print(" Todo correcto.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
