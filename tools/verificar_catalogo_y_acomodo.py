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

# Los avisos son cosas que NO tumban la comprobacion pero hay que decir. Estaba usado
# mas abajo sin declararlo, asi que el dia que una banda se quedara corta este script
# habria muerto con un NameError en vez de avisar de lo que encontro.
avisos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


# ===========================================================================
#  1. El lector del catalogo
# ===========================================================================

FAMILIAS = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

# Los prefijos de nombre que delatan una familia. Port de FamiliaPerfil.PorPrefijo.
PREFIJOS = {
    "W": "IR", "IR": "IR", "IPR": "IR",
    "IS": "IS",
    "IC": "IC",
    "S": "S",
    "WT": "WT", "TR": "WT",
    "C": "C", "CE": "C",
    "L": "L", "LI": "L", "LD": "L",
    "HSS": "OR", "OR": "OR", "PTR": "OR",
    "PIPE": "OC", "OC": "OC", "TUBO": "OC",
    "OS": "OS", "VR": "OS",
    "CF": "CF", "MONTEN": "CF", "MON": "CF",
    "ZF": "ZF", "Z": "ZF",
}


def familia_del_nombre(perfil):
    """Port de FamiliaPerfil.DelNombre.

    Se compara con el PREFIJO DE LETRAS COMPLETO, no con un startswith. Es la
    diferencia entre acertar y no: con startswith, un «CF - 3" x 1 1/2"» entra por la
    puerta de la C si esa se prueba antes, y un «WT - 2''» por la de la W.
    """
    p = (perfil or "").strip().upper()

    if not p:
        return None

    letras = ""

    for ch in p:
        if not ch.isalpha():
            break
        letras += ch

    return PREFIJOS.get(letras)


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
            "ancho2": numero(campos, 8),
        })

    return perfiles


print("=" * 78)
print(" El lector del catalogo de perfiles")
print("=" * 78)

# El archivo que se entrega con el programa tiene que leerse, y sus cuatro perfiles
# tienen que salir con la familia que les toca.
with open("client/src/CadLink.App/perfiles-acero.csv", encoding="utf-8") as f:
    entregado = leer_catalogo(f.readlines())

porfam = {}
for p in entregado:
    porfam[p["familia"]] = porfam.get(p["familia"], 0) + 1

print(f"\n    del archivo entregado salen {len(entregado)} perfiles: "
      + ", ".join(f"{n} {f}" for f, n in sorted(porfam.items())))

# Ya no es la semilla de cuatro: es el catalogo del IMCA. No se fija el numero exacto
# a proposito -crecera cuando se añadan familias- pero si que sea un catalogo de verdad
# y que las cuatro familias esten.
check("el catalogo que se entrega se lee", len(entregado) > 1500,
      f"salieron {len(entregado)}")
check("y trae las DOCE familias", set(porfam) == set(FAMILIAS),
      f"tiene {sorted(porfam)}, faltan {sorted(set(FAMILIAS) - set(porfam))}")

# Las cuatro familias de forma I y la te y la canal traen sus cuatro medidas: sin el
# espesor de patin no se puede dibujar ninguna de ellas.
LAMINADAS = ("IR", "IS", "IC", "S", "WT", "C")

laminadas = [p for p in entregado if p["familia"] in LAMINADAS]
sin_medidas = [f"{p['familia']} {p['nombre']}" for p in laminadas
               if not (p["peralte"] > 0 and p["ancho"] > 0
                       and p["e_alma"] > 0 and p["e_patin"] > 0)]

check("las seis familias laminadas traen sus cuatro medidas", not sin_medidas,
      f"{len(sin_medidas)} sin completar: {sin_medidas[:3]}")

# El OC es redondo: no puede traer ancho ni espesor de patin, porque no los tiene.
ocs = [p for p in entregado if p["familia"] == "OC"]
con_sobras = [p["nombre"] for p in ocs if p["ancho"] or p["e_patin"]]

check("ningun OC trae ancho ni espesor de patin", not con_sobras,
      f"{len(con_sobras)}: {con_sobras[:3]}")

# Y el OS es macizo: solo diametro. Ni ancho, ni pared, ni patin.
oss = [p for p in entregado if p["familia"] == "OS"]
os_con_sobras = [p["nombre"] for p in oss
                 if p["ancho"] or p["e_alma"] or p["e_patin"]]

check("los OS traen SOLO su diametro", oss and not os_con_sobras,
      f"{len(os_con_sobras)}: {os_con_sobras[:3]}")

# El labio es SOLO del CF; el radio, del CF y de la ZF; el ancho 2, solo de la ZF.
cfs = [p for p in entregado if p["familia"] == "CF"]
zfs = [p for p in entregado if p["familia"] == "ZF"]

sin_labio = [p["nombre"] for p in cfs if not (p["labio"] > 0 and p["radio"] > 0)]
otros_con_labio = [f"{p['familia']} {p['nombre']}" for p in entregado
                   if p["familia"] != "CF" and p["labio"]]
otros_con_radio = [f"{p['familia']} {p['nombre']}" for p in entregado
                   if p["familia"] not in ("CF", "ZF") and p["radio"]]

check("todos los CF traen labio y radio", not sin_labio,
      f"{len(sin_labio)}: {sin_labio[:3]}")
check("y ninguna otra familia trae labio", not otros_con_labio,
      f"{len(otros_con_labio)}: {otros_con_labio[:3]}")
check("el radio solo lo traen las dos formadas en frio, el CF y la ZF",
      not otros_con_radio,
      f"{len(otros_con_radio)}: {otros_con_radio[:3]}")

# El ancho 2 es el patin ANGOSTO de la zeta, y no lo tiene nadie mas.
sin_ancho2 = [p["nombre"] for p in zfs
              if not (0 < p["ancho2"] <= p["ancho"])]
otros_con_ancho2 = [f"{p['familia']} {p['nombre']}" for p in entregado
                    if p["familia"] != "ZF" and p["ancho2"]]

check("todas las ZF traen su patin angosto, y no pasa del ancho",
      zfs and not sin_ancho2, f"{len(sin_ancho2)}: {sin_ancho2[:3]}")
check("y el ancho 2 no lo trae ninguna otra familia", not otros_con_ancho2,
      f"{len(otros_con_ancho2)}: {otros_con_ancho2[:3]}")

# La L no trae ni patin ni labio ni radio: sus unicas medidas son las dos alas y el
# espesor, porque la hoja del IMCA no le da nada mas.
eles = [p for p in entregado if p["familia"] == "L"]
eles_con_sobras = [p["nombre"] for p in eles
                   if p["e_patin"] or p["labio"] or p["radio"]]

check("los angulos traen solo sus dos alas y su espesor",
      eles and not eles_con_sobras, f"{len(eles_con_sobras)}: {eles_con_sobras[:3]}")

check("y su ala corta nunca es mas larga que la larga",
      all(p["ancho"] <= p["peralte"] + 1e-9 for p in eles),
      str([p["nombre"] for p in eles if p["ancho"] > p["peralte"]][:3]))

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

    ("una zeta con su noveno campo, el patin angosto",
     ['ZF;ZF - 8" x 2 3/8" x #14;20.32;6.03;0.19;;;0.476;5.4'],
     1),

    ("un CSV VIEJO de ocho columnas, sin el ancho 2",
     ["CF;CF - 6\" x 2\" x #14;15.24;5.08;0.19;;1.52;0.24"],
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

# La zeta con su noveno campo tiene que llegar con el patin angosto, y una linea vieja
# de ocho columnas con el ancho 2 en cero, que es lo que significa «zeta simetrica».
zeta = leer_catalogo(['ZF;ZF - 8" x 2 3/8" x #14;20.32;6.03;0.19;;;0.476;5.4'])[0]
vieja = leer_catalogo(["CF;CF - 6\" x 2\" x #14;15.24;5.08;0.19;;1.52;0.24"])[0]

check("el patin angosto de la zeta se lee del noveno campo",
      abs(zeta["ancho2"] - 5.4) < 1e-12, str(zeta))
check("y un CSV de ocho columnas deja el ancho 2 en cero, no rompe",
      vieja["ancho2"] == 0 and abs(vieja["labio"] - 1.52) < 1e-12, str(vieja))


# ---- Los prefijos que se pisan unos con otros ----
#
# Es donde estaba el error de verdad. Con doce familias hay prefijos que son prefijo de
# otro -W y WT, C y CF, Z y ZF, O y OR/OC/OS- asi que un startswith los confunde: un
# «CF - 3" x 1 1/2"» acaba en la familia C si esa se prueba antes, y un «WT - 2''» en la
# W. Tomando las letras de delante ENTERAS, el orden de la tabla deja de importar.
print("\n" + "-" * 78)
print(" Los prefijos que se pisan unos con otros")
print("-" * 78)

NOMBRES = [
    ("W - 12'' x 30.04 lb/ft", "IR"),
    ("WT - 8'' x 13.0 lb/ft", "WT"),
    ("W12X30", "IR"),
    ("C - 8'' x 12.0 lb/ft", "C"),
    ('CF - 6" x 2" x #14', "CF"),
    ('ZF - 8" x 2 3/8" x #14', "ZF"),
    ("S - 10'' x 25.4 lb/ft", "S"),
    ("IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm", "IS"),
    ("IC - 16 '' x 52.14 lb/ft", "IC"),
    ("IR - 12'' x 30 lb/ft", "IR"),
    ("L - 3'' x 2'' x 1/4''", "L"),
    ('HSS - 6" x 1/4"', "OR"),
    ("PIPE - 4.02 in x 0.19 in", "OC"),
    ('OS - 3/4"', "OS"),
    ("MONTEN 6X2", "CF"),
    ("PTR 4X4", "OR"),
    ("LO QUE SEA", None),
    ("", None),
]

print()
for nombre, esperada in NOMBRES:
    salio = familia_del_nombre(nombre)

    print(f"    {nombre!r:48} -> {salio}")

    check(f"'{nombre}' es de la familia {esperada}", salio == esperada,
          f"salio {salio}")

# Y la comprobacion que resume las de arriba: cada perfil del catalogo entregado tiene
# que caer en SU familia si se le quita la columna. Es la prueba mas fuerte que se puede
# hacer del deductor, porque son mil seiscientos nombres reales.
mal_deducidos = [(p["familia"], p["nombre"], familia_del_nombre(p["nombre"]))
                 for p in entregado
                 if familia_del_nombre(p["nombre"]) != p["familia"]]

print(f"\n    de los {len(entregado)} nombres del catalogo, "
      f"{len(mal_deducidos)} se deducirian mal")

check("los mil seiscientos nombres del catalogo se deducen a su propia familia",
      not mal_deducidos,
      "; ".join(f"{f} {n} -> {s}" for f, n, s in mal_deducidos[:5]))


# ===========================================================================
#  2. El acomodo a la izquierda del origen
# ===========================================================================

print("\n" + "=" * 78)
print(" El acomodo del acero: a la izquierda del origen, desde -0.6")
print("=" * 78)

ORIGEN_CM = -60          # el xDerechaActual = -0.6 de las macros, en cm
ESCALA = 0.01

# El aire y la banda de cada familia, en centimetros. Las doce arrancan en la misma x,
# asi que lo unico que evita que se encimen es la banda.
#
# Las cuatro primeras son el sepIzq y el baseY de las macros, y siguen donde estaban a
# proposito: quien vuelva a generar un plano suyo encuentra el acero en su sitio. Las
# ocho nuevas se apilan encima, a partir de 6.5 m.
AIRE = {
    "IR": 45, "OR": 55, "OC": 60, "CF": 65,
    "IS": 45, "IC": 45, "S": 50, "WT": 55,
    "C": 60, "ZF": 65, "L": 70, "OS": 70,
}

# La altura de las bandas NO es una tabla: se CALCULA. La primera arranca en cero y cada
# una de las siguientes va un metro por encima de la seccion MAS ALTA de la de abajo.
ORDEN_FAMILIAS = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

SEPARACION_BANDAS = 100

AIRE_CM = AIRE["OR"]     # el que usan las pruebas de una sola familia


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
check("se dibuja de derecha a izquierda",
      "var xIzquierda = xDerecha - ancho;" in codigo)
check("y el hueco se avanza SIEMPRE, tambien para los saltados",
      re.search(r"xDerecha = xIzquierda - aire;", codigo) is not None
      and "El hueco se avanza SIEMPRE" in codigo)

# El aire de CADA una de las doce familias.
CLAVE = {
    "IR": "Ir", "IS": "Is", "IC": "Ic", "S": "S", "WT": "Wt", "C": "C",
    "CF": "Cf", "ZF": "Zf", "L": "L", "OR": "Or", "OC": "Oc", "OS": "Os",
}

bloque_aire = codigo.split("AireDeLaFamiliaCm")[-1].split("OrdenDeLaFamilia")[0]

for fam, aire in AIRE.items():
    check(f"el aire de {fam} en el codigo es {aire} cm",
          f"FamiliaPerfil.{CLAVE[fam]} => {aire}," in bloque_aire)

# LA BANDA SE CALCULA, no se busca en una tabla. Lo que hay que comprobar es que ya no
# quede tabla ninguna y que la cuenta sea la de subir un metro sobre la mas alta.
check(f"la separacion entre bandas es de {SEPARACION_BANDAS} cm",
      f"SeparacionDeBandasCm = {SEPARACION_BANDAS}" in codigo)

check("la banda de la siguiente familia se calcula con la seccion mas alta",
      "yCm += masAlto + SeparacionDeBandasCm;" in codigo)

check("y ya no queda tabla de alturas de banda",
      "BandaDeLaFamiliaCm" not in codigo and "TechoDeLaBandaCm" not in codigo)

check("la primera banda arranca en cero, como la macro del IR",
      "var yCm = 0.0;" in codigo)

# El alto que se acumula es el DIBUJADO, no el peralte capturado: en el tubo rectangular
# no son lo mismo, porque se dibuja de pie con su lado mayor en vertical.
check("se acumula el alto dibujado, no el peralte capturado",
      "masAlto = Math.Max(masAlto, perfil.AltoDibujoCm);" in codigo)

# Y EL AIRE LO MANDA EL ROTULO cuando es mas ancho que el perfil. Sin esto, dos
# secciones con un nombre largo quedan separadas pero sus rotulos se pisan.
check("el aire se recalcula con el ancho del rotulo de cada perfil",
      "perfil.AnchoRotuloCm - perfil.AnchoDibujoCm" in codigo)

check("las familias se recorren en el orden de la lista, no en el de captura",
      "OrderBy(g => OrdenDeLaFamilia(g.Key))" in codigo)

check("y se dice a que altura quedo cada familia, que ya no es un numero fijo",
      "bandas.Add(" in codigo and "Cada familia en su banda" in codigo)


# ===========================================================================
#  Las bandas: que las cuatro familias no se encimen entre ellas
# ===========================================================================
#
# Las cuatro macros arrancan en x = -0.6, asi que lo unico que las separa es la Y. Con
# el catalogo del IMCA eso hay que comprobarlo de verdad, porque trae perfiles IS de
# hasta 1.90 m de peralte y la banda del IR solo tiene 2.00 m hasta la del OR.

print("\n" + "=" * 78)
print(" Las bandas de cada familia")
print("=" * 78)

catalogo_real = []
with open("client/src/CadLink.App/perfiles-acero.csv", encoding="utf-8") as f:
    catalogo_real = leer_catalogo(f.readlines())


def alto_de(p):
    """Port de PerfilAceroCad.AltoDibujoCm: el tubo rectangular se dibuja DE PIE."""
    if p["familia"] == "OR" and p["ancho"] > 0:
        return max(p["peralte"], p["ancho"])

    return p["peralte"]


def apilar(alturas):
    """El calculo de las bandas del OnExportAcero.

    Devuelve {familia: (base, alto)}. La primera arranca en cero y cada una de las
    siguientes va un metro por encima de la CIMA de la de abajo.
    """
    y = 0.0
    bandas = {}

    for fam in ORDEN_FAMILIAS:
        if fam not in alturas:
            continue

        bandas[fam] = (y, alturas[fam])
        y += alturas[fam] + SEPARACION_BANDAS

    return bandas


# El caso peor: una hoja con el perfil mas alto de cada familia del catalogo.
altos_del_catalogo = {}

for p in catalogo_real:
    f = p["familia"]
    altos_del_catalogo[f] = max(altos_del_catalogo.get(f, 0), alto_de(p))

bandas = apilar(altos_del_catalogo)

print()
for fam, (base, alto) in bandas.items():
    print(f"    {fam:3}: banda en {base / 100:6.2f} m   la mas alta mide {alto:6.2f} cm"
          f"   su cima queda en {(base + alto) / 100:6.2f} m")

# LO QUE HAY QUE COMPROBAR: que entre la cima de una y la base de la siguiente haya
# exactamente un metro. Es lo que garantiza que no se encimen, y ya no depende de que
# una tabla de alturas este bien puesta.
anterior = None

for fam, (base, alto) in bandas.items():
    if anterior is not None:
        fam_ant, cima_ant = anterior

        check(f"entre la cima de {fam_ant} y la base de {fam} hay un metro justo",
              abs((base - cima_ant) - SEPARACION_BANDAS) < 1e-9,
              f"{base - cima_ant:.2f} cm")

    anterior = (fam, base + alto)

check("la primera banda arranca en cero, como la macro del IR",
      next(iter(bandas.values()))[0] == 0)

# Y con una hoja de perfiles chicos las bandas se acercan, que es de lo que se trata:
# con la tabla de alturas fijas la OS se dibujaba a 17 m aunque la hoja solo llevara
# angulos de 3 pulgadas.
chicos = {"L": 7.62, "OS": 1.91, "CF": 15.24}
bandas_chicas = apilar(chicos)

print("\n    una hoja de solo perfiles chicos (CF, L y OS):")
for fam, (base, alto) in bandas_chicas.items():
    print(f"    {fam:3}: banda en {base / 100:6.2f} m   la mas alta mide {alto:6.2f} cm")

check("con perfiles chicos las bandas se acercan en lugar de quedarse fijas",
      max(b for b, _ in bandas_chicas.values()) < 300,
      f"la ultima queda en {max(b for b, _ in bandas_chicas.values()):.0f} cm")

# El metro da de sobra para lo que sobresale de una seccion: el rotulo cuelga por debajo
# de la base y las cotas suben por encima del perfil de abajo.
ROTULO_ABAJO = 6 + (4 * 3)
COTAS_ARRIBA = 6 + 1.5 + 2

check("el metro de separacion da para el rotulo de arriba y las cotas de abajo",
      ROTULO_ABAJO + COTAS_ARRIBA < SEPARACION_BANDAS,
      f"hacen falta {ROTULO_ABAJO + COTAS_ARRIBA:.1f} de {SEPARACION_BANDAS}")

# El aire NO es el mismo para todas: el rotulo de una familia de perfiles estrechos es
# mucho mas ancho que su seccion, asi que necesita mas hueco que uno de perfiles anchos.
# Es al reves de lo que parece, y por eso se comprueba.
check("las familias tienen aires distintos entre si", len(set(AIRE.values())) >= 4,
      str(sorted(set(AIRE.values()))))

check("y el angulo, que es el mas estrecho, lleva mas aire que la IR",
      AIRE["L"] > AIRE["IR"], f"L={AIRE['L']} IR={AIRE['IR']}")

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
