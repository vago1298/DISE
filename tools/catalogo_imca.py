"""Convierte el manual IMCA (docs/IMCA5.xlsx) en el perfiles-acero.csv de CadLink.

    python3 tools/catalogo_imca.py docs/IMCA5.xlsx > client/src/CadLink.App/perfiles-acero.csv

POR QUE UN SCRIPT APARTE DEL GENERICO
-------------------------------------
catalogo_desde_excel.py sirve para una hoja normal: una fila de encabezados y debajo
los perfiles. La hoja del IMCA no es asi, y por cuatro razones que hay que tratar una
por una:

  1. CADA FAMILIA USA COLUMNAS DISTINTAS. El perfil I trae sus medidas en las columnas
     con encabezado -d, h, tw, bf, tf-, pero el tubo rectangular las trae en tres
     columnas SIN encabezado y el tubo redondo en otras dos, tambien sin encabezado.
     Un lector guiado por encabezados no puede adivinarlas.

  2. LAS UNIDADES CAMBIAN DE UNA COLUMNA A OTRA. Casi todas las medidas van en
     milimetros, pero la columna 'tdist' -el espesor de diseño- va en CENTIMETROS para
     el tubo y en MILIMETROS para la lamina doblada. Confundirlas es un error de diez
     veces que no se ve en el dibujo: sale un perfil creible con la medida mal.

  3. EL ANGULO NO TRAE NINGUNA MEDIDA. Las 144 filas de la familia L tienen TODAS las
     columnas de geometria en '-': solo hay peso, area, gramil y J. Sus medidas estan
     unicamente en la DESIGNACION -«L - 3'' x 2'' x 1/4''»-, asi que hay que leerlas
     de ahi, en pulgadas, y pasarlas a centimetros.

  4. UNA FAMILIA TRAE UNA MEDIDA QUE NINGUNA OTRA TIENE. La zeta formada en frio
     tiene los DOS PATINES DE DISTINTO ANCHO -60.3 y 54 mm en la de 2 3/8"-, y no es
     un error de la hoja: es lo que permite traslapar dos zetas en el apoyo. Por eso
     el CSV lleva una novena columna, 'ancho2', que solo usa la ZF.

QUE COLUMNAS SE USAN, FAMILIA POR FAMILIA
-----------------------------------------
    W, IS, IC, S  (forma I)        col 2 = d (mm)   col 27 = bf   col 26 = tw   col 28 = tf
    WT            (forma te)       las mismas cuatro
    C             (canal laminada) las mismas cuatro
    HSS -> OR     (tubo rect.)     col 6 = h (mm)   col 7 = b     col 8 = espesor
    PIPE -> OC    (tubo redondo)   col 12 = D (mm)  col 13 = espesor
    OS            (redondo macizo) col 6 = D (mm)   -- la col 2 de esta familia esta
                                   en CENTIMETROS, no en mm: 0.638 para el de 1/4"
    CF            (canal c/labios) col 6 = h (mm)   col 7 = b     col 4 = espesor
                                   col 11 = labio   col 12 = radio de doblez
    ZF            (zeta)           col 6 = h (mm)   col 7 = patin ancho
                                   col 8 = patin angosto            col 4 = espesor
                                   col 68 = r
    L             (angulo)         NINGUNA: se lee de la designacion

TODO SALE EN CENTIMETROS, que es lo que lee CadLink.

LAS PROPIEDADES GEOMETRICAS
---------------------------
Ademas de las medidas, se traen las dieciseis propiedades con las que se diseña, que la
hoja ya da en unidades de centimetro:

    peso kg/m, area cm2, Ix Iy J cm4, Sx Sy Zx Zy cm3, rx ry rmin cm, Cw cm6,
    x barra e y barra cm (el centroide), Ixy cm4

De sus columnas, con dos casos que hay que tratar aparte:

  * LAS FORMADAS EN FRIO -CF y ZF- no traen Ix ni Sx en las columnas 48 y 50: las
    traen en la 71 y la 72, 'Idx' y 'Sxe', que son los valores de DISEÑO -calculados
    con el metodo del ancho efectivo, que es como se diseña una lamina delgada-. Se
    toman de ahi y se avisa en el CSV, porque no son exactamente lo mismo que el Ix
    geometrico de un perfil laminado.
  * EL CENTROIDE cambia de columna segun la familia: la canal y el CF lo traen en la
    35, el angulo en la 57, y la te y el angulo traen su 'y barra' en la 52.

Lo que la hoja trae y NO se recoge, por si algun dia hace falta: las relaciones de
esbeltez (b/2tf, h/tw, D/t, b/t), los ejes principales del angulo (Iz, Sz, Iw, Sw,
rw), las cotas de detallado (kdet, k, T, gramil, soldadura minima) y las propiedades
efectivas por grado de acero (Ae, Mnx0).

LO QUE SE COMPRUEBA AL CONVERTIR
--------------------------------
Las columnas numericas se cotejan contra los nominales en pulgadas que la propia hoja
trae como texto: un tubo que dice 2" tiene que medir 50.8 mm. Lo que no cuadre se avisa
con su nombre y su diferencia, sin corregirlo por cuenta propia: si la hoja y su propio
nominal no coinciden, quien tiene que decidir es el usuario.
"""

import math
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

MM_POR_PULGADA = 25.4

# La familia del IMCA -> la familia de CadLink.
#
# CASI SIEMPRE ES LA MISMA LETRA, y eso es deliberado. Antes las cuatro familias que
# se sabian dibujar metian IS, IC y S dentro de IR «porque son perfiles I», y el
# resultado era que el desplegable de IR ofrecia 573 perfiles con cuatro nombres
# distintos mezclados: quien buscaba una W tenia que ir sorteando IS, IC y S.
#
# Ahora la FAMILIA y la FORMA son dos cosas distintas: la familia es la lista en la
# que se busca el perfil -y el nombre con el que se rotula-, y la forma es lo que se
# dibuja. Cuatro familias comparten la forma I y siguen siendo cuatro listas.
#
# Las tres unicas traducciones de nombre son las que ya hacian las macros, porque son
# nomenclatura americana que en el plano mexicano se rotula de otro modo:
#     W -> IR      HSS -> OR      PIPE -> OC
FAMILIAS = {
    "W": "IR",
    "IS": "IS",
    "IC": "IC",
    "S": "S",
    "WT": "WT",
    "C": "C",
    "L": "L",
    "HSS": "OR",
    "PIPE": "OC",
    "OS": "OS",
    "CF": "CF",
    "ZF": "ZF",
}

# La forma con la que se dibuja cada familia de CadLink. Tiene que decir lo mismo que
# FormaPerfil.DeLaFamilia del programa; si aqui se agrega una familia, alla tambien.
FORMAS = {
    "IR": "I",
    "IS": "I",
    "IC": "I",
    "S": "I",
    "WT": "te",
    "C": "canal",
    "CF": "canal con labios",
    "ZF": "zeta",
    "L": "angulo",
    "OR": "tubo rectangular",
    "OC": "tubo redondo",
    "OS": "redondo macizo",
}

# El orden en que salen las familias en el CSV y en el informe: por forma, para que se
# lea como una tabla de perfiles y no como un revoltijo.
ORDEN = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

# Las dieciseis propiedades geometricas, con la columna de la que sale cada una.
#
# La segunda columna de la pareja es la ALTERNATIVA, para las familias que traen esa
# propiedad en otro sitio; se usa solo si la primera viene vacia:
#
#   Ix y Sx     las formadas en frio los traen en Idx y Sxe (71 y 72), que son los
#               valores de DISEÑO por ancho efectivo
#   x barra     la canal y el CF en la 35, el angulo en la 57
#   rmin        la zeta lo trae como 'rmin' (81) y el angulo como 'rz' (63), que es lo
#               mismo: el radio de giro del eje principal debil
PROPIEDADES = (
    ("peso", 18, None),      # kg/m
    ("area", 20, None),      # cm2
    ("ix", 48, 71),          # cm4
    ("sx", 50, 72),          # cm3
    ("rx", 51, None),        # cm
    ("zx", 49, None),        # cm3
    ("iy", 53, None),        # cm4
    ("sy", 55, None),        # cm3
    ("ry", 56, None),        # cm
    ("zy", 54, None),        # cm3
    ("j", 44, None),         # cm4
    ("cw", 45, None),        # cm6
    ("xbar", 35, 57),        # cm
    ("ybar", 52, None),      # cm
    ("rmin", 81, 63),        # cm
    ("ixy", 78, None),       # cm4
)


# El acero pesa 7.85 g/cm3, o sea 0.785 kg/m por cada cm2 de seccion.
KG_POR_CM2 = 0.785

# ...salvo en los TUBOS, y esto no es una errata de la hoja sino como se hacen las
# tablas: el PESO de un tubo se calcula con su pared NOMINAL, mientras que su AREA y sus
# inercias se calculan con la pared de DISEÑO, que para un tubo soldado es 0.93 veces la
# nominal. Asi que en un tubo el peso sale un 7 % por encima de lo que diria su area, y
# las dos cifras son correctas.
FACTOR_PARED_DISEÑO = 0.93


def propiedades_de(c):
    """Las dieciseis propiedades de una fila, o None cada una si la hoja no la da.

    No se calcula ninguna: si la hoja no trae el dato, sale vacio. Un Ix deducido de
    rx y del area seria un numero que nadie firmo, y en una tabla de perfiles eso es
    peor que un hueco.
    """
    valores = []

    for _, col, alterna in PROPIEDADES:
        v = numero(c.get(col))

        if v is None and alterna is not None:
            v = numero(c.get(alterna))

        valores.append(v)

    return valores


def columna(ref):
    """'BC12' -> 54."""
    n = 0

    for ch in "".join(c for c in ref if c.isalpha()):
        n = n * 26 + (ord(ch) - 64)

    return n - 1


def filas_del_libro(ruta):
    """Las filas de la primera hoja: {numero de fila: {columna: texto}}."""
    with zipfile.ZipFile(ruta) as z:
        compartidas = []

        if "xl/sharedStrings.xml" in z.namelist():
            raiz = ET.fromstring(z.read("xl/sharedStrings.xml"))

            for si in raiz.findall(f"{NS}si"):
                compartidas.append("".join(t.text or "" for t in si.iter(f"{NS}t")))

        hojas = sorted(n for n in z.namelist()
                       if n.startswith("xl/worksheets/sheet") and n.endswith(".xml"))

        if not hojas:
            raise SystemExit("El libro no trae ninguna hoja.")

        filas = {}

        for fila in ET.fromstring(z.read(hojas[0])).iter(f"{NS}row"):
            celdas = {}

            for c in fila.findall(f"{NS}c"):
                i = columna(c.get("r"))
                tipo = c.get("t")
                v = c.find(f"{NS}v")
                texto = ""

                if tipo == "s" and v is not None:
                    k = int(v.text or 0)
                    texto = compartidas[k] if 0 <= k < len(compartidas) else ""
                elif tipo == "inlineStr":
                    is_ = c.find(f"{NS}is")
                    if is_ is not None:
                        texto = "".join(t.text or "" for t in is_.iter(f"{NS}t"))
                elif v is not None:
                    texto = v.text or ""

                if texto.strip():
                    celdas[i] = texto.strip()

            if celdas:
                filas[int(fila.get("r"))] = celdas

        return filas


def numero(texto):
    """El numero de una celda, o None. El guion de la hoja significa «no aplica»."""
    t = (texto or "").strip()

    if t in ("", "-", "—"):
        return None

    try:
        return float(t.replace(",", "."))
    except ValueError:
        return None


def pulgadas(texto):
    """'2 1/2"' -> 2.5. Lo que la hoja trae como nominal, para poder cotejarlo."""
    t = (texto or "").strip().replace('"', "").replace("''", "").replace("in", "")
    t = t.strip()

    if not t:
        return None

    # Entero y fraccion: 2 1/2
    m = re.fullmatch(r"(\d+)\s+(\d+)/(\d+)", t)
    if m:
        return int(m.group(1)) + int(m.group(2)) / int(m.group(3))

    # Solo fraccion: 1/2
    m = re.fullmatch(r"(\d+)/(\d+)", t)
    if m:
        return int(m.group(1)) / int(m.group(2))

    try:
        return float(t)
    except ValueError:
        return None


def medidas_del_angulo(designacion):
    """Las medidas de un angulo, leidas de su NOMBRE y devueltas en milimetros.

    Las 144 filas de la familia L no traen NINGUNA medida en la hoja -todas las
    columnas de geometria estan en '-'-, asi que la designacion es la unica fuente:

        L - 3/4'' x 1/8''            ->  alas iguales de 3/4",  espesor 1/8"
        L - 3'' x 2'' x 1/4''        ->  alas de 3" y 2",       espesor 1/4"

    Devuelve (ala mayor, ala menor, espesor) en mm, o None si el nombre no se entiende.
    Leerlo del nombre no es una comodidad: es que no hay de donde mas sacarlo.
    """
    t = (designacion or "").upper()

    # Se quita el prefijo 'L -' y se parten los factores por la equis.
    t = re.sub(r"^\s*L\s*-?\s*", "", t)

    trozos = [p.strip() for p in t.split("X")]

    valores = [pulgadas(p) for p in trozos]

    if any(v is None for v in valores):
        return None

    if len(valores) == 2:
        ala1 = ala2 = valores[0]
        espesor = valores[1]
    elif len(valores) == 3:
        ala1, ala2, espesor = valores
    else:
        return None

    if not ala1 or not ala2 or not espesor:
        return None

    return (max(ala1, ala2) * MM_POR_PULGADA,
            min(ala1, ala2) * MM_POR_PULGADA,
            espesor * MM_POR_PULGADA)


def revisar_propiedades(nombre, forma, props):
    """Coteja las propiedades entre ellas por fisica. Devuelve la lista de dudas.

    NO corrige nada y NO hace que el perfil se salte: son propiedades para mostrar, no
    medidas para dibujar, asi que un Ix dudoso no impide dibujar el perfil. Lo que hace
    falta es decirlo, con nombre y numeros, para que se pueda ir a la celda.

    Y no se puede adivinar CUAL de los dos numeros esta mal. Cuando el peso no cuadra con
    el area, puede ser el peso o puede ser el area; cuando el radio de giro no cuadra con
    la inercia, casi siempre es la inercia -porque el radio es un numero corto y se copia
    bien- pero no siempre. Se avisa de la pareja que no cuadra y decide quien tiene la
    hoja delante.
    """
    (peso, area, ix, sx, rx, zx, iy, sy, ry, zy, j, cw,
     xbar, ybar, rmin, ixy) = props

    dudas = []
    es_tubo = forma in ("tubo rectangular", "tubo redondo")

    # ---- El peso contra el area ----
    if peso and area and area >= 5:
        esperado = area * KG_POR_CM2

        # En un tubo el peso va con la pared NOMINAL y el area con la de DISEÑO, asi que
        # la razon correcta no es 1 sino 1/0.93. Ver FACTOR_PARED_DISEÑO.
        if es_tubo:
            esperado /= FACTOR_PARED_DISEÑO

        if abs(peso - esperado) > 0.06 * esperado:
            dudas.append(
                f"{nombre}: pesa {peso} kg/m y su area de {area} cm2 daria "
                f"{esperado:.1f}" + (" (contando la pared de diseño)" if es_tubo else ""))

    # ---- El radio de giro contra la inercia y el area ----
    for inercia, radio, eje in ((ix, rx, "x"), (iy, ry, "y")):
        if not (inercia and radio and area) or area < 5 or inercia < 10:
            continue

        esperado = math.sqrt(inercia / area)

        if abs(radio - esperado) > 0.06 * esperado:
            dudas.append(
                f"{nombre}: r{eje} = {radio} cm pero raiz(I{eje}/area) = "
                f"raiz({inercia}/{area}) = {esperado:.2f}")

    # ---- El modulo plastico contra el elastico ----
    for elastico, plastico, eje in ((sx, zx, "x"), (sy, zy, "y")):
        if not (elastico and plastico) or elastico < 10:
            continue

        # Un 2 % de margen por el redondeo del manual: hay perfiles donde los dos valores
        # redondeados salen practicamente iguales.
        if plastico < 0.98 * elastico:
            dudas.append(
                f"{nombre}: Z{eje} = {plastico} cm3 es MENOR que S{eje} = {elastico}, y "
                "el modulo plastico nunca baja del elastico")

    return dudas


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1

    filas = filas_del_libro(argv[1])

    perfiles = []
    fuera = {}
    avisos = []

    # Las dudas de las PROPIEDADES van en su propia lista, separadas de los avisos de las
    # medidas: unas hacen que el perfil se salte y las otras no, y mezcladas parecerian
    # igual de graves.
    avisos_props = []

    for r in sorted(filas):
        if r <= 2:
            continue

        c = filas[r]
        familia_imca = c.get(0, "").strip().upper()

        if not familia_imca:
            continue

        nombre = c.get(1, "").strip()

        if not nombre:
            continue

        familia = FAMILIAS.get(familia_imca)

        if familia is None:
            fuera.setdefault(familia_imca or "(vacia)", 0)
            fuera[familia_imca or "(vacia)"] += 1
            continue

        forma = FORMAS[familia]

        # ------------------------------------------------------------------
        #  Las medidas, cada familia de sus columnas y en sus unidades
        # ------------------------------------------------------------------
        peralte = ancho = espesor = e_patin = labio = radio = ancho2 = None

        if forma in ("I", "te", "canal"):
            # Perfil I, te y canal laminada: todo en milimetros, en las columnas con
            # encabezado. Se usa 'd', el peralte TOTAL, y no 'h', que es el alma libre.
            peralte = numero(c.get(2))
            ancho = numero(c.get(27))
            espesor = numero(c.get(26))
            e_patin = numero(c.get(28))

        elif forma == "tubo rectangular":
            # Tubo rectangular: tres columnas sin encabezado, en milimetros. El
            # peralte es el lado MAYOR, que es como se dibuja.
            a = numero(c.get(6))
            b = numero(c.get(7))
            espesor = numero(c.get(8))

            if a is not None and b is not None:
                peralte, ancho = max(a, b), min(a, b)

            # Se cotejan contra los nominales en pulgadas de la propia hoja.
            for col_num, col_pulg, que in ((6, 9, "lado 1"), (7, 10, "lado 2"),
                                           (8, 11, "espesor")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    # Un milimetro de tolerancia: la hoja redondea 50.8 a 51.
                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} son {esperado:.1f} mm")

        elif forma == "tubo redondo":
            # Tubo redondo: diametro y espesor en milimetros.
            peralte = numero(c.get(12))
            espesor = numero(c.get(13))

            for col_num, col_pulg, que in ((12, 14, "diametro"), (13, 15, "espesor")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} in son {esperado:.1f} mm")

        elif forma == "redondo macizo":
            # Redondo macizo: solo el diametro, y se toma de la columna 6, que esta en
            # MILIMETROS. La columna 2 -la 'd' con la que vienen los perfiles I- en
            # esta familia esta en CENTIMETROS: dice 0.638 para el de 1/4". Tomarla
            # por milimetros daria una varilla de 0.6 mm.
            peralte = numero(c.get(6))

            # El espesor no aplica: es macizo. Se pone en cero y la forma lo sabe.
            espesor = 0

            p = pulgadas(c.get(11))

            if peralte is not None and p is not None:
                esperado = p * MM_POR_PULGADA

                if abs(peralte - esperado) > 1.0:
                    avisos.append(
                        f"{nombre}: el diametro dice {peralte} mm pero su nominal "
                        f"{c.get(11)} son {esperado:.1f} mm")

        elif forma == "canal con labios":
            # Lamina doblada: peralte y ancho en milimetros, y aqui 'tdist' SI esta en
            # milimetros -es el calibre-, al contrario que en el tubo.
            peralte = numero(c.get(6))
            ancho = numero(c.get(7))
            espesor = numero(c.get(4))
            labio = numero(c.get(11))
            radio = numero(c.get(12))

            for col_num, col_pulg, que in ((6, 9, "peralte"), (7, 10, "ancho")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} son {esperado:.1f} mm")

        elif forma == "zeta":
            # Zeta formada en frio. Los DOS PATINES SON DE DISTINTO ANCHO -60.3 y 54
            # en la de 2 3/8"-, y eso no es una errata de la hoja: es lo que deja
            # traslapar dos zetas en el apoyo, porque la angosta entra dentro de la
            # ancha. Por eso hay una columna 'ancho2' en el CSV.
            #
            # El radio sale de la columna 'r', no de la 12 como en el CF: en la ZF la
            # 12 esta vacia.
            peralte = numero(c.get(6))
            ancho = numero(c.get(7))
            ancho2 = numero(c.get(8))
            espesor = numero(c.get(4))
            radio = numero(c.get(68))

            for col_num, col_pulg, que in ((6, 9, "peralte"), (7, 10, "patin")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} son {esperado:.1f} mm")

        elif forma == "angulo":
            # El angulo NO trae medidas en la hoja: se leen de su nombre.
            m = medidas_del_angulo(nombre)

            if m is None:
                avisos.append(
                    f"{nombre}: se salta, su nombre no dice las medidas y la hoja "
                    "tiene todas las columnas de geometria en '-'")
                continue

            peralte, ancho, espesor = m

        if not peralte or espesor is None:
            avisos.append(f"{nombre}: se salta, sin peralte o sin espesor en la hoja")
            continue

        # ------------------------------------------------------------------
        #  Que las medidas sean POSIBLES, no solo que existan
        # ------------------------------------------------------------------
        # Esto caza los errores de dedo de la hoja, que es lo que ninguna otra
        # comprobacion ve: un numero que esta en su celda, es un numero y aun asi no
        # puede ser. Los dos casos reales de este archivo son
        #
        #     W - 36'' x 442.16 lb/ft   ->   alma de 346 mm  (deberia ser 34.6)
        #     L - 1/4'' x 1/8''         ->   un ala de 1/4" con 1/8" de espesor
        #
        # El del angulo se ve solo al mirar la tabla: esa fila esta entre las de 1" y
        # las de 1 1/4", su peso -1.5 kg/m- y su gramil -18 mm- son los de la
        # «L - 1 1/4'' x 1/8''», asi que le falta el «1 » al nombre. Un ala de 6.35 mm
        # con 3.18 mm de espesor no es un angulo, es media barra.
        #
        # Los limites son de PROPORCION, no de tamaño, asi que valen igual para un
        # perfil de 8 cm y para uno de 2 m.
        problema = None

        if forma in ("I", "canal"):
            if espesor > peralte / 6:
                problema = (f"el alma mide {espesor} mm en un peralte de {peralte} mm, "
                            "mas de la sexta parte")
            elif e_patin and e_patin > peralte / 3:
                problema = (f"el patin mide {e_patin} mm en un peralte de {peralte} mm, "
                            "mas de la tercera parte")
            elif ancho and espesor > ancho / 2:
                problema = (f"el alma ({espesor} mm) pasa de la mitad del patin "
                            f"({ancho} mm)")

        elif forma == "te":
            # La te se comprueba distinto que el perfil I, y no por capricho: la te es
            # MEDIO perfil I, asi que su alma es igual de gruesa que la del entero
            # pero su peralte es la mitad. Con el limite del I -la sexta parte- se
            # caerian tes buenas: la WT - 2'' x 6.5 lb/ft tiene 7.2 mm de alma en 53
            # de peralte, que es casi la septima parte y es correcta.
            if espesor > peralte / 3:
                problema = (f"el alma mide {espesor} mm en un peralte de {peralte} mm, "
                            "mas de la tercera parte")
            elif e_patin and e_patin > peralte / 2:
                problema = (f"el patin mide {e_patin} mm en un peralte de {peralte} mm, "
                            "mas de la mitad")
            elif ancho and espesor > ancho / 2:
                problema = (f"el alma ({espesor} mm) pasa de la mitad del patin "
                            f"({ancho} mm)")

        elif forma in ("tubo rectangular", "tubo redondo"):
            menor = peralte if forma == "tubo redondo" else min(peralte, ancho or peralte)

            if espesor > menor / 4:
                problema = (f"la pared mide {espesor} mm en un lado de {menor} mm, "
                            "mas de la cuarta parte")

        elif forma == "canal con labios":
            if espesor > peralte / 10:
                problema = (f"la lamina mide {espesor} mm en un peralte de "
                            f"{peralte} mm, mas de la decima parte")
            elif labio and labio > peralte / 2:
                problema = (f"el labio ({labio} mm) pasa de la mitad del peralte "
                            f"({peralte} mm)")

        elif forma == "zeta":
            if espesor > peralte / 10:
                problema = (f"la lamina mide {espesor} mm en un peralte de "
                            f"{peralte} mm, mas de la decima parte")
            elif ancho2 and ancho2 > ancho:
                problema = (f"el patin angosto ({ancho2} mm) es mas ancho que el "
                            f"ancho ({ancho} mm), que es al reves de lo que debe ser")

        elif forma == "angulo":
            # En el angulo manda el ala CORTA: un ala de 2" con 1/2" de espesor sigue
            # siendo un angulo, pero una de 1/4" con 1/8" no.
            if espesor > ancho / 3:
                problema = (f"el espesor ({espesor:.2f} mm) pasa de la tercera parte "
                            f"del ala corta ({ancho:.2f} mm)")

        if problema:
            avisos.append(f"{nombre}: SE SALTA, {problema}. Revisa esa celda en la hoja")
            continue

        # ------------------------------------------------------------------
        #  Y que las PROPIEDADES cuadren entre ellas
        # ------------------------------------------------------------------
        # Estas se cotejan aparte y NO hacen que el perfil se salte, y las dos cosas son
        # a proposito: son propiedades para mostrar, no medidas para dibujar, asi que un
        # Ix dudoso no impide dibujar el perfil. Lo que hace falta es DECIRLO.
        #
        # Se cotejan por FISICA, que es lo unico que caza un numero mal escrito en una
        # columna de propiedades:
        #
        #     peso = area x 7.85 g/cm3      el acero pesa lo que pesa
        #     r    = raiz(I / area)         definicion del radio de giro
        #     Z    >= S                     el modulo plastico pasa al elastico
        props = propiedades_de(c)
        dudas = revisar_propiedades(nombre, forma, props)

        for duda in dudas:
            avisos_props.append(duda)

        # El redondo macizo es la unica forma sin ancho ni espesor: es un circulo.
        if forma not in ("tubo redondo", "redondo macizo") and not ancho:
            avisos.append(f"{nombre}: se salta, sin ancho en la hoja")
            continue

        if forma in ("I", "te", "canal") and not e_patin:
            avisos.append(f"{nombre}: se salta, sin espesor de patin en la hoja")
            continue

        if forma == "canal con labios" and (not labio or not radio):
            avisos.append(f"{nombre}: se salta, sin labio o sin radio en la hoja")
            continue

        if forma == "zeta" and not ancho2:
            avisos.append(f"{nombre}: se salta, sin el ancho del patin angosto")
            continue

        # Todo estaba en milimetros: a centimetros, que es lo que lee CadLink.
        def cm(v):
            return None if v is None else v / 10

        # Las propiedades NO se convierten: la hoja ya las da en unidades de
        # centimetro -kg/m, cm2, cm3, cm4, cm6- al contrario que las medidas, que
        # vienen en milimetros. Pasarlas por cm() las dividiria por diez.
        perfiles.append((
            familia,
            nombre,
            cm(peralte), cm(ancho), cm(espesor), cm(e_patin), cm(labio), cm(radio),
            cm(ancho2),
            familia_imca,
            props,
        ))

    if not perfiles:
        raise SystemExit("No salio ningun perfil.")

    # ------------------------------------------------------------------
    #  El CSV
    # ------------------------------------------------------------------
    def fmt(v):
        return "" if v is None else f"{v:g}"

    print("# ============================================================================")
    print("#  CATALOGO DE PERFILES DE ACERO DE CADLINK")
    print("# ============================================================================")
    print("#")
    print("#  Generado del manual IMCA con tools/catalogo_imca.py")
    print(f"#  {len(perfiles)} perfiles de {len({p[0] for p in perfiles})} familias.")
    print("#  TODAS LAS MEDIDAS EN CENTIMETROS.")
    print("#")
    print("#  Este archivo llena los desplegables de la pestaña «Secciones Acero»: al")
    print("#  elegir la familia, la celda «Perfil» ofrece los perfiles de esa familia, y")
    print("#  al elegir uno se traen sus medidas solas.")
    print("#")
    print("#  Se puede editar con el Bloc de notas o con Excel, y no hay que recompilar")
    print("#  nada: se guarda, se vuelve a abrir CadLink y los cambios ya estan.")
    print("#")
    print("#  LAS MEDIDAS, que son las que se dibujan (columnas 1 a 9):")
    print("#")
    print("#  familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio;ancho2")
    print("#")
    print("#     peralte   en OC y OS es el DIAMETRO EXTERIOR; en la L, el ala larga")
    print("#     ancho     patin en I/te/canales, cara en OR, ala corta en la L")
    print("#     e_alma    alma en I/te/canal; PARED en OR y OC; lamina en CF y ZF")
    print("#     e_patin   solo las formas laminadas: I, te y canal")
    print("#     labio     solo el CF")
    print("#     radio     doblez exterior: el CF y la ZF")
    print("#     ancho2    solo la ZF: su patin ANGOSTO, el que permite el traslape")
    print("#")
    print("#  El redondo macizo (OS) solo usa el peralte: no tiene pared ni ancho.")
    print("#")
    print("#  LAS PROPIEDADES GEOMETRICAS, que solo se muestran (columnas 10 a 25):")
    print("#")
    print("#  ...;peso;area;ix;sx;rx;zx;iy;sy;ry;zy;j;cw;xbar;ybar;rmin;ixy")
    print("#")
    print("#     peso        kg/m          area        cm2")
    print("#     ix iy       cm4           sx sy       cm3")
    print("#     zx zy       cm3           rx ry rmin  cm")
    print("#     j           cm4           cw          cm6")
    print("#     xbar ybar   cm            ixy         cm4")
    print("#")
    print("#  ESTAS YA VIENEN EN CENTIMETROS EN EL MANUAL: no se convierten, al")
    print("#  contrario que las medidas, que vienen en milimetros.")
    print("#")
    print("#  Una propiedad VACIA quiere decir que el manual no la da para esa familia,")
    print("#  no que valga cero. No se calcula ninguna: un Ix deducido de rx y del area")
    print("#  seria un numero que nadie firmo.")
    print("#")
    print("#  OJO CON EL Ix Y EL Sx DE LAS FORMADAS EN FRIO (CF y ZF): el manual no los")
    print("#  da en su columna, los da como Idx y Sxe, que son los valores de DISEÑO")
    print("#  calculados por ancho efectivo. Es lo que hay, y es con lo que se diseña una")
    print("#  lamina delgada, pero no es el Ix geometrico de un perfil laminado.")
    print("#")
    print("#  Lo que el manual trae y NO esta aqui: las relaciones de esbeltez (b/2tf,")
    print("#  h/tw, D/t, b/t), los ejes principales del angulo (Iz, Sz, Iw, Sw, rw), las")
    print("#  cotas de detallado (kdet, k, T, gramil) y las propiedades efectivas por")
    print("#  grado de acero (Ae, Mnx0).")
    print("#")

    for familia in ORDEN:
        de_esta = [p for p in perfiles if p[0] == familia]

        if not de_esta:
            continue

        origen = sorted({p[9] for p in de_esta})

        print("#")
        print(f"# {'-' * 74}")
        print(f"#  {familia}: {len(de_esta)} perfiles, forma {FORMAS[familia]}   "
              f"(del IMCA: {', '.join(origen)})")

        # Y se dice QUE PROPIEDADES trae esta familia, contando las que vienen llenas.
        # Es informacion que solo se puede saber contando: el manual deja huecos
        # distintos en cada familia, y sin esto el usuario ve una columna vacia en su
        # tabla y no sabe si es que falta el dato o que su perfil esta mal.
        cuantas = []

        for i, (nombre_prop, _, _) in enumerate(PROPIEDADES):
            con = sum(1 for p in de_esta if p[10][i] is not None)

            if con == len(de_esta):
                cuantas.append(nombre_prop)
            elif con > 0:
                cuantas.append(f"{nombre_prop}({con})")

        print(f"#  propiedades: {', '.join(cuantas) if cuantas else 'ninguna'}")
        print(f"# {'-' * 74}")

        for p in de_esta:
            campos = [p[0], p[1], fmt(p[2]), fmt(p[3]), fmt(p[4]),
                      fmt(p[5]), fmt(p[6]), fmt(p[7]), fmt(p[8])]
            campos += [fmt(v) for v in p[10]]

            print(";".join(campos))

    # ------------------------------------------------------------------
    #  El informe, por la salida de errores para no ensuciar el CSV
    # ------------------------------------------------------------------
    print(f"\n{len(perfiles)} perfiles convertidos:", file=sys.stderr)

    for familia in ORDEN:
        de_esta = [p for p in perfiles if p[0] == familia]

        if de_esta:
            por_imca = ", ".join(sorted({p[9] for p in de_esta}))
            alto = max(p[2] for p in de_esta)

            print(f"   {familia:4} {len(de_esta):4}   forma {FORMAS[familia]:18}"
                  f" del IMCA {por_imca:5}  peralte maximo {alto:7.2f} cm",
                  file=sys.stderr)

    if fuera:
        total = sum(fuera.values())
        print(f"\n{total} filas FUERA, de familias que no estan en la tabla:",
              file=sys.stderr)

        for k, n in sorted(fuera.items(), key=lambda x: -x[1]):
            print(f"   {k:5} {n:4}", file=sys.stderr)

    if avisos:
        print(f"\n{len(avisos)} aviso(s) al cotejar las medidas con sus nominales:",
              file=sys.stderr)

        for a in avisos[:40]:
            print("   - " + a, file=sys.stderr)

        if len(avisos) > 40:
            print(f"   ... y {len(avisos) - 40} mas", file=sys.stderr)

    # ------------------------------------------------------------------
    #  Las propiedades que no cuadran entre ellas
    # ------------------------------------------------------------------
    if avisos_props:
        print(f"\n{len(avisos_props)} PROPIEDAD(ES) QUE NO CUADRAN, de "
              f"{len(perfiles)} perfiles.", file=sys.stderr)
        print("Estos perfiles SI se dibujan: lo que no cuadra es una propiedad que solo",
              file=sys.stderr)
        print("se muestra, no una medida. Pero conviene mirar esas celdas de la hoja,",
              file=sys.stderr)
        print("porque el numero se va a ver en la tabla y es creible:", file=sys.stderr)
        print(file=sys.stderr)

        # Se listan TODAS, sin cortar: cada una es una celda que hay que ir a mirar, y una
        # lista recortada obliga a volver a correr el convertidor para ver el resto.
        for a in avisos_props:
            print("   - " + a, file=sys.stderr)

        print("\nSe cotejan por fisica: peso = area x 7.85 g/cm3, r = raiz(I/area) y",
              file=sys.stderr)
        print("Z >= S. En los tubos el peso va con la pared nominal y el area con la de",
              file=sys.stderr)
        print("diseño (0.93 t), y eso ya esta contado.", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
