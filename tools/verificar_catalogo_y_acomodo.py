"""Verifica el lector del catalogo de perfiles y el acomodo del acero.

Dos cosas que no se pueden comprobar leyendo el codigo:

  1. EL LECTOR DEL CSV. Lo va a escribir una persona exportando de Excel, asi que
     tiene que tragarse comentarios, lineas en blanco, punto y coma o coma de
     separador, punto o coma de decimal, campos vacios y una cabecera. Se le pasan
     todos esos casos y se comprueba que saca lo que debe y que no se cae.

  2. EL ACOMODO. TODAS las secciones se alinean con su borde derecho en x = -0.6, cada
     una en su renglon, y lo que crece es la altura: 70 cm de la cima de una a la base
     de la siguiente. Lo que hay que comprobar es que las secciones NO SE PISAN, que
     ninguna se mete en el semiplano positivo -donde dibuja el concreto- y que el sitio
     de cada una es el MISMO se dibuje la hoja entera o se salte la mitad.
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


# Las dieciseis propiedades geometricas, en el orden en que van en el CSV.
PROPIEDADES = ("peso", "area", "ix", "sx", "rx", "zx", "iy", "sy", "ry", "zy",
               "j", "cw", "xbar", "ybar", "rmin", "ixy")


def opcional(campos, i):
    """Port de CatalogoPerfiles.Opcional: el hueco es None, no cero."""
    if i >= len(campos):
        return None

    texto = campos[i].strip().replace(",", ".")

    if not texto:
        return None

    try:
        return float(texto)
    except ValueError:
        return None


def numero(campos, i):
    """Port de CatalogoPerfiles.Numero: en una MEDIDA, el hueco vale cero."""
    v = opcional(campos, i)

    return 0.0 if v is None else v


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

        fila = {
            "familia": familia,
            "nombre": nombre,
            "peralte": numero(campos, 2),
            "ancho": numero(campos, 3),
            "e_alma": numero(campos, 4),
            "e_patin": numero(campos, 5),
            "labio": numero(campos, 6),
            "radio": numero(campos, 7),
            "ancho2": numero(campos, 8),
        }

        # Las dieciseis propiedades. Se leen como OPCIONAL y no como numero: en una
        # propiedad, el hueco quiere decir «el manual no da esto para esta familia», que
        # no es cero. Es el CatalogoPerfiles.Opcional del programa.
        for i, clave in enumerate(PROPIEDADES):
            fila[clave] = opcional(campos, 9 + i)

        perfiles.append(fila)

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


# ===========================================================================
#  LAS PROPIEDADES GEOMETRICAS: que sean las que dicen ser
# ===========================================================================
#
# Esta es la comprobacion que de verdad hacia falta al traer dieciseis columnas nuevas
# del manual, y no es «que esten»: es que CADA UNA SEA LA QUE DICE SER.
#
# Un mapeo de columna equivocado no se ve. Si el area y el peso se cruzaran, o si el Sx
# saliera de la columna del Zx, el CSV se leeria perfectamente y la tabla mostraria
# numeros creibles. Lo unico que lo caza es la FISICA, que relaciona unas con otras:
#
#     peso = area x 7.85 g/cm3          el acero pesa lo que pesa
#     rx   = raiz(Ix / area)            definicion del radio de giro
#     Zx   > Sx                         el modulo plastico siempre pasa al elastico
#
# Se comprueban sobre el catalogo entero. Las tolerancias son generosas -un 6 %- porque
# el manual redondea a dos o tres cifras, y solo se exige en los perfiles grandes: en un
# angulo con Ix = 0.4 cm4, el propio redondeo ya es un 10 %.

print("\n" + "=" * 78)
print(" Las propiedades geometricas: que sean las que dicen ser")
print("=" * 78)

DENSIDAD_ACERO = 0.785      # kg/m por cada cm2 de seccion

# En los TUBOS el peso va con la pared NOMINAL y el area con la de DISEÑO, que para un
# tubo soldado es 0.93 veces la nominal. Asi que el peso de un tubo sale un 7 % por
# encima de lo que diria su area, y las dos cifras son correctas: no es una errata.
FACTOR_PARED = 0.93

TUBOS = ("OR", "OC")

# QUE SE EXIGE Y QUE NO.
#
# Estas comprobaciones tienen dos cosas distintas que decir, y hay que separarlas:
#
#   * SI EL MAPEO DE COLUMNAS ESTA MAL, falla CASI TODO: cruzar el area con el peso, o
#     leer el Sx de la columna del Zx, descuadra el 100 % de una familia. Eso es un error
#     MIO y tiene que hacer fallar la comprobacion.
#   * SI LA HOJA TRAE UNA CELDA MAL ESCRITA, falla UN perfil. Eso es un dato del usuario,
#     no se corrige por cuenta propia y no puede hacer fallar nada: se cuenta y se avisa.
#
# Asi que lo que se exige es una TASA baja por familia. Con el catalogo de hoy salen 56
# descuadres de 1617 perfiles (3.5 %), todos sueltos y comprobados uno por uno contra el
# manual del AISC: son erratas de la hoja, del tipo «Zx = 2671 donde debia decir 3671».
TASA_MAXIMA = 0.10

con_props = [p for p in entregado if p["peso"] is not None]

print(f"\n    {len(con_props)} de {len(entregado)} perfiles traen propiedades")

check("la mayoria de los perfiles traen sus propiedades",
      len(con_props) > 0.9 * len(entregado),
      f"solo {len(con_props)} de {len(entregado)}")


def tasa(familia, malos, revisados):
    """Comprueba la TASA de descuadres de una familia, no que sea cero."""
    if revisados == 0:
        return

    t = len(malos) / revisados

    check(f"las propiedades de {familia} cuadran entre ellas ({revisados} revisadas)",
          t <= TASA_MAXIMA,
          f"descuadran {len(malos)} ({100 * t:.0f} %): " + "; ".join(malos[:2]))


todos_los_malos = []

for familia in FAMILIAS:
    de_esta = [p for p in con_props if p["familia"] == familia]

    if not de_esta:
        continue

    malos = []
    revisados = 0
    es_tubo = familia in TUBOS

    for p in de_esta:
        # ---- 1. El peso contra el area ----
        if p["area"] is not None and p["area"] >= 5:
            revisados += 1
            esperado = p["area"] * DENSIDAD_ACERO

            if es_tubo:
                esperado /= FACTOR_PARED

            if abs(p["peso"] - esperado) > 0.06 * esperado:
                malos.append(f"{p['nombre']}: pesa {p['peso']} y su area daria "
                             f"{esperado:.1f}")

        # ---- 2. El radio de giro contra la inercia y el area ----
        # Es la que caza que la inercia y el area vengan de SU columna y en SUS unidades:
        # si el area estuviera en mm2 o el Ix saliera de la columna del Zx, el radio no
        # saldria por ningun lado.
        for inercia, radio, eje in (("ix", "rx", "x"), ("iy", "ry", "y")):
            if (p[inercia] is None or p[radio] is None or p["area"] is None
                    or p["area"] < 5 or p[inercia] < 10):
                continue

            revisados += 1
            esperado = math.sqrt(p[inercia] / p["area"])

            if abs(p[radio] - esperado) > 0.06 * esperado:
                malos.append(f"{p['nombre']}: r{eje}={p[radio]} pero raiz(I/A) = "
                             f"{esperado:.2f}")

        # ---- 3. El modulo plastico pasa al elastico ----
        for elastico, plastico, eje in (("sx", "zx", "x"), ("sy", "zy", "y")):
            if p[elastico] is None or p[plastico] is None or p[elastico] < 10:
                continue

            revisados += 1

            # Un 2 % de margen por el redondeo del manual: hay perfiles donde los dos
            # valores redondeados salen practicamente iguales.
            if p[plastico] < 0.98 * p[elastico]:
                malos.append(f"{p['nombre']}: Z{eje}={p[plastico]} < S{eje}={p[elastico]}")

    print(f"    {familia:3}: {revisados:5} comprobaciones, {len(malos):3} descuadran"
          f"   ({100 * len(malos) / revisados:4.1f} %)" if revisados else
          f"    {familia:3}: sin propiedades que comprobar")

    tasa(familia, malos, revisados)
    todos_los_malos += malos

print(f"\n    en total descuadran {len(todos_los_malos)} de "
      f"{sum(1 for _ in con_props)} perfiles con propiedades")

# Y la comprobacion que de verdad separa un error de mapeo de una errata de la hoja: si
# el mapeo estuviera mal, descuadraria casi todo.
check("el descuadre es de celdas sueltas, no de un mapeo de columnas equivocado",
      len(todos_los_malos) < 0.10 * len(con_props),
      f"{len(todos_los_malos)} de {len(con_props)}")

# Y que el convertidor los AVISE, porque son celdas que el usuario tiene que ir a mirar.
with open("tools/catalogo_imca.py", encoding="utf-8") as f:
    fuente_conv = f.read()

check("el convertidor avisa de las propiedades que no cuadran",
      "PROPIEDAD(ES) QUE NO CUADRAN" in fuente_conv
      and "def revisar_propiedades(" in fuente_conv)

check("y no las corrige ni salta el perfil por ellas",
      "NO corrige nada y NO hace que el perfil se salte" in fuente_conv)

check("y cuenta la pared de diseño de los tubos, que no es una errata",
      "FACTOR_PARED_DISEÑO" in fuente_conv)

# ---- 4. Que las propiedades sean POSITIVAS ----
negativas = [(p["nombre"], k) for p in entregado
             for k in ("peso", "area", "ix", "sx", "rx", "zx", "iy", "sy", "ry",
                       "zy", "j", "cw", "rmin", "ixy")
             if p[k] is not None and p[k] <= 0]

check("ninguna propiedad sale negativa ni cero", not negativas,
      "; ".join(f"{n}:{k}" for n, k in negativas[:5]))

# ---- 5. Y que el hueco sea un HUECO, no un cero ----
# Es la diferencia que hace util la tabla: el redondo macizo NO trae Sx en el manual, y
# eso tiene que llegar como vacio. Con cero, la celda diria «0.00» y se leeria como un
# dato: un modulo de seccion cero significa que el perfil no resiste nada.
oss = [p for p in entregado if p["familia"] == "OS"]

check("el redondo macizo llega SIN Sx, no con Sx en cero",
      oss and all(p["sx"] is None for p in oss),
      str([p["sx"] for p in oss[:3]]))

check("y con su area y su inercia, que si las trae",
      oss and all(p["area"] is not None and p["ix"] is not None for p in oss))

# La canal formada en frio no trae rx: el manual da su Ix como valor de diseño y no le
# pone radio de giro.
cfs_con_props = [p for p in entregado if p["familia"] == "CF" and p["peso"] is not None]

check("la canal formada en frio llega sin rx, que el manual no le da",
      cfs_con_props and all(p["rx"] is None for p in cfs_con_props))

# Y el ángulo trae rmin, que es lo que decide su pandeo: es su propiedad clave.
eles_con_props = [p for p in entregado if p["familia"] == "L" and p["peso"] is not None]

check("el angulo trae su rmin, que es con lo que se revisa su pandeo",
      eles_con_props and all(p["rmin"] is not None for p in eles_con_props))

check("y su rmin es menor que su rx, porque es el del eje debil",
      all(p["rmin"] <= p["rx"] + 1e-9 for p in eles_con_props
          if p["rmin"] is not None and p["rx"] is not None))


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

# YA NO HAY AIRE HORIZONTAL POR FAMILIA.
#
# Las macros ponian los perfiles de una familia uno al lado del otro hacia la izquierda,
# separados el sepIzq de su macro -45 el IR, 55 el OR, 60 el OC, 65 el CF-. Con los
# nombres del catalogo IMCA eso no se sostiene: el rotulo va centrado debajo de cada
# seccion y mide casi un metro -«PERFIL: IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm»-, asi
# que los rotulos se pisaban aunque los perfiles no se tocaran.
#
# Ahora cada seccion va en su propio renglon y todas alineadas en la misma x, asi que no
# hay nada al lado con lo que chocar.

# TODAS las secciones en la misma x, una por renglon, y 70 cm de la cima de una a la base
# de la siguiente.
ORDEN_FAMILIAS = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

SEPARACION = 70


def acomodar(perfiles, saltados=()):
    """El acomodo del OnExportAcero: TODAS en x = -0.6, una por renglon.

    'perfiles' son parejas (ancho, alto) en cm. Devuelve una lista de
    (x_izq, x_der, y_base, y_cima) por seccion, en unidades de dibujo, o None para las
    saltadas.

    Las saltadas NO se dibujan pero SI avanzan el renglon: es lo que hace que el sitio
    de cada seccion no dependa de cuales se dibujaron.
    """
    y_cm = 0.0
    puestos = []

    for i, (ancho_cm, alto_cm) in enumerate(perfiles):
        x_der = ORIGEN_CM * ESCALA
        x_izq = x_der - (ancho_cm * ESCALA)
        y_base = y_cm * ESCALA
        y_cima = (y_cm + alto_cm) * ESCALA

        puestos.append(None if i in saltados else (x_izq, x_der, y_base, y_cima))

        y_cm += alto_cm + SEPARACION

    return puestos


# (ancho, alto) de dibujo: el IR de ejemplo, un OR doble, un OC y un CF doble.
PERFILES = [(16.6, 31.3), (2 * 15.2, 15.2), (10.2, 10.2), (2 * 5.08, 15.24)]

puestos = acomodar(PERFILES)

print()
for (ancho, alto), p in zip(PERFILES, puestos):
    print(f"    seccion de {ancho:6.2f} x {alto:6.2f} cm  ->  x de {p[0]:+.4f} a "
          f"{p[1]:+.4f}   y de {p[2]:+.4f} a {p[3]:+.4f}")

# LO PRIMERO: TODAS acaban en -0.6, no solo la primera. Es el cambio.
for i, p in enumerate(puestos):
    check(f"la seccion {i + 1} acaba justo en -0.6",
          abs(p[1] - (-0.6)) < 1e-12, f"{p[1]:+.6f}")

check("todas quedan a la izquierda del origen",
      all(p[1] <= 0 for p in puestos))

# Y las anchas sobresalen MAS a la izquierda, que es la consecuencia de alinear por la
# derecha: los bordes izquierdos NO estan a plomo, y no tienen por que estarlo.
izquierdos = [p[0] for p in puestos]

print(f"\n    los bordes izquierdos van de {min(izquierdos):+.4f} a "
      f"{max(izquierdos):+.4f}: alineadas por la DERECHA, no por la izquierda")

check("las secciones se alinean por su borde derecho, no por el izquierdo",
      len({round(p[1], 9) for p in puestos}) == 1
      and len({round(p[0], 9) for p in puestos}) > 1)

# Que no se pisen EN VERTICAL: la base de una tiene que quedar por encima de la cima de
# la de abajo, y con la separacion completa entre medias.
for i in range(len(puestos) - 1):
    cima = puestos[i][3]
    base_siguiente = puestos[i + 1][2]

    check(f"la seccion {i + 1} y la {i + 2} no se pisan",
          base_siguiente >= cima - 1e-12,
          f"{base_siguiente:+.6f} contra {cima:+.6f}")

    check(f"y entre ellas quedan los {SEPARACION} cm",
          abs((base_siguiente - cima) - SEPARACION * ESCALA) < 1e-12,
          f"{(base_siguiente - cima) / ESCALA:.4f} cm")

# La primera arranca en cero, que es el baseY de la macro del IR.
check("la primera seccion arranca en y = 0", abs(puestos[0][2]) < 1e-12)

# El ancho de una seccion DOBLE es el doble: si no, la segunda se saldria de su hueco.
check("el hueco de una doble mide el doble que el de una simple",
      abs((puestos[1][1] - puestos[1][0]) - 2 * 15.2 * ESCALA) < 1e-12)

# Y LOS 70 CM DAN PARA LO QUE SOBRESALE de una seccion: el rotulo cuelga por debajo de
# su base y las cotas suben por encima de la de abajo.
ROTULO_ABAJO = 6 + (4 * 3)      # gap maximo + cuatro renglones de 3 cm
COTAS_ARRIBA = 6 + 1.5 + 2      # gap maximo + texto + flecha

print(f"\n    de los {SEPARACION} cm, el rotulo de arriba se come {ROTULO_ABAJO} y las "
      f"cotas de abajo {COTAS_ARRIBA}")

check("los 70 cm dan para el rotulo de arriba y las cotas de abajo",
      ROTULO_ABAJO + COTAS_ARRIBA < SEPARACION,
      f"hacen falta {ROTULO_ABAJO + COTAS_ARRIBA:.1f}")

check("y sobran mas de 30 cm de aire",
      SEPARACION - ROTULO_ABAJO - COTAS_ARRIBA > 30,
      f"solo {SEPARACION - ROTULO_ABAJO - COTAS_ARRIBA:.1f} cm")

# ---- Y lo que de verdad importa: los sitios NO cambian si se saltan perfiles ----
print("\n" + "-" * 78)
print(" El sitio de cada perfil no depende de cuales se dibujen")
print("-" * 78)

completo = acomodar(PERFILES)
con_saltados = acomodar(PERFILES, saltados=(0, 2))

print()
for i, (a, b) in enumerate(zip(completo, con_saltados)):
    estado = "saltada" if b is None else f"en y = {b[2]:+.4f}"
    print(f"    seccion {i + 1}: dibujando todas y = {a[2]:+.4f}   "
          f"saltando la 1 y la 3 -> {estado}")

iguales = all(
    b is None or all(abs(a[k] - b[k]) < 1e-12 for k in range(4))
    for a, b in zip(completo, con_saltados))

check("las que se dibujan caen en el MISMO sitio aunque otras se salten", iguales)

# Y la prueba de que hacia falta: si las saltadas no avanzaran el renglon, la segunda
# seccion se dibujaria encima de la primera.
def acomodar_mal(perfiles, saltados=()):
    """El acomodo INGENUO: las saltadas no avanzan el renglon."""
    y_cm = 0.0
    puestos = []

    for i, (ancho_cm, alto_cm) in enumerate(perfiles):
        if i in saltados:
            puestos.append(None)
            continue

        x_der = ORIGEN_CM * ESCALA
        puestos.append((x_der - (ancho_cm * ESCALA), x_der,
                        y_cm * ESCALA, (y_cm + alto_cm) * ESCALA))

        y_cm += alto_cm + SEPARACION

    return puestos


mal = acomodar_mal(PERFILES, saltados=(0,))
bien = acomodar(PERFILES, saltados=(0,))

# En el ingenuo, la seccion 2 ocupa el renglon de la 1, que ya esta en el plano.
encima = abs(mal[1][2] - completo[0][2]) < 1e-12

print(f"\n    sin avanzar el renglon, la seccion 2 arrancaria en y={mal[1][2]:+.4f}, "
      f"justo donde arranca la 1 ({completo[0][2]:+.4f})")
print(f"    avanzandolo, arranca en y={bien[1][2]:+.4f}, en su sitio")

check("sin avanzar el renglon de las saltadas, la siguiente se dibuja encima", encima)
check("y avanzandolo, no", abs(bien[1][2] - completo[0][2]) > 1e-9)


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

# TODAS en la misma x: la x de la derecha se vuelve a poner en el origen DENTRO del
# bucle de secciones, no una vez por familia. Ahi esta el cambio.
check("la x de la derecha se fija en el origen para CADA seccion",
      re.search(r"foreach \(var fila in grupo\)\s*\{\s*"
                r"var perfil = AFormatoAceroCad\(fila\);\s*"
                r"(//[^\n]*\n\s*)*"
                r"var xDerecha = OrigenAceroCm \* escala;", codigo) is not None)

check("y el dibujo crece hacia la izquierda desde ahi",
      "var xIzquierda = xDerecha - (perfil.AnchoDibujoCm * escala);" in codigo)

check("ya no se avanza en x de una seccion a la siguiente",
      "xDerecha = xIzquierda - aire" not in codigo)

check("ya no hay aire horizontal por familia",
      "AireDeLaFamiliaCm" not in codigo)

# LO QUE AVANZA ES EL RENGLON, y avanza SIEMPRE, tambien para las saltadas.
check(f"la separacion entre secciones es de {SEPARACION} cm",
      f"SeparacionEntreSeccionesCm = {SEPARACION}" in codigo)

check("el renglon se avanza con el alto de la seccion mas la separacion",
      "yCm += perfil.AltoDibujoCm + SeparacionEntreSeccionesCm;" in codigo)

check("y se avanza siempre, tambien para las saltadas",
      "se sube al renglón siguiente SIEMPRE" in codigo)

check("ya no queda tabla de alturas de banda",
      "BandaDeLaFamiliaCm" not in codigo and "TechoDeLaBandaCm" not in codigo)

check("la primera seccion arranca en y = 0",
      "var yCm = 0.0;" in codigo)

# El alto que se acumula es el DIBUJADO, no el peralte capturado: en el tubo rectangular
# no son lo mismo, porque se dibuja de pie con su lado mayor en vertical.
check("se acumula el alto dibujado, no el peralte capturado",
      "perfil.AltoDibujoCm + SeparacionEntreSeccionesCm" in codigo)

check("las familias se recorren en el orden de la lista, no en el de captura",
      "OrderBy(g => OrdenDeLaFamilia(g.Key))" in codigo)

check("y se dice a que altura quedo cada familia, que ya no es un numero fijo",
      "bandas.Add(" in codigo and "una por renglón" in codigo)


# ===========================================================================
#  El caso peor: el perfil mas alto de cada familia, una seccion por renglon
# ===========================================================================
#
# Con el catalogo delante, que es la unica manera de saber si la separacion alcanza: la
# IS llega a 1.90 m de peralte, y una hoja con la mas alta de cada familia es lo mas
# exigente que se le puede pedir al acomodo.

print("\n" + "=" * 78)
print(" El caso peor: la seccion mas alta de cada familia")
print("=" * 78)

catalogo_real = []
with open("client/src/CadLink.App/perfiles-acero.csv", encoding="utf-8") as f:
    catalogo_real = leer_catalogo(f.readlines())


def alto_de(p):
    """Port de PerfilAceroCad.AltoDibujoCm: el tubo rectangular se dibuja DE PIE."""
    if p["familia"] == "OR" and p["ancho"] > 0:
        return max(p["peralte"], p["ancho"])

    return p["peralte"]


# El caso peor: una hoja con la seccion MAS ALTA de cada familia del catalogo, en el
# orden en que se dibujan. Es lo mas exigente que se le puede pedir al acomodo.
mas_altos = {}

for p_ in catalogo_real:
    f = p_["familia"]

    if f not in mas_altos or alto_de(p_) > mas_altos[f][1]:
        mas_altos[f] = (p_["nombre"], alto_de(p_), p_["ancho"] or p_["peralte"])

familias_peor = [f for f in ORDEN_FAMILIAS if f in mas_altos]
hoja_peor = [(mas_altos[f][2], mas_altos[f][1]) for f in familias_peor]

puestas = acomodar(hoja_peor)

print()
for f, pu in zip(familias_peor, puestas):
    nombre, alto, ancho = mas_altos[f]
    print(f"    {f:3}: y de {pu[2]:6.2f} a {pu[3]:6.2f} m   "
          f"{alto:6.2f} cm de alto   {nombre[:36]}")

# Que ninguna se encime con la de abajo, con los perfiles REALES mas exigentes.
for i in range(len(puestas) - 1):
    check(f"la seccion {i + 1} y la {i + 2} del caso peor no se pisan",
          puestas[i + 1][2] >= puestas[i][3] - 1e-12,
          f"{puestas[i + 1][2]:.4f} contra {puestas[i][3]:.4f}")

# Y que TODAS acaben en -0.6: da igual la familia y da igual el ancho de la seccion.
check("las doce del caso peor acaban todas en -0.6",
      all(abs(pu[1] - (-0.6)) < 1e-12 for pu in puestas))

print(f"\n    el plano entero, con la mas alta de cada familia, mide "
      f"{puestas[-1][3]:.2f} m de alto")

# Y con perfiles chicos el plano se encoge, que es la ventaja de medir desde la cima de
# cada seccion y no desde una tabla de alturas fijas.
puestas_chicas = acomodar([(5.08, 15.24), (5.08, 7.62), (1.91, 1.91)])

print(f"    una hoja de tres perfiles chicos mide {puestas_chicas[-1][3]:.2f} m")

check("una hoja de perfiles chicos ocupa poco, no un hueco fijo",
      puestas_chicas[-1][3] < 2.0, f"{puestas_chicas[-1][3]:.2f} m")

# Y LA SEPARACION ES UNA SOLA para todas, que es lo que se gana al apilarlas: ya no hay
# que darle a cada familia su propio aire segun lo ancho que sea su rotulo, porque no hay
# nada al lado con lo que chocar.
check("la separacion es una sola para las doce familias",
      f"SeparacionEntreSeccionesCm = {SEPARACION}" in codigo
      and "AireDeLaFamiliaCm" not in codigo)

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
