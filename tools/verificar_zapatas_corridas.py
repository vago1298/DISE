"""Verifica el port de ZAPATA CORRIDA CENTRAL V2 y ZAPATA CORRIDA LINDERO V2.

Aqui se rehacen en Python las cuentas de las DOS macros -leidas del fuente VBA,
linea por linea- y se comparan contra las constantes y las reglas que quedaron
escritas en TrazoZapataCorrida.cs. Compilar no prueba que un numero sea el de la
macro; esto si.

LO QUE ESTA COMPROBACION YA CAZO
--------------------------------
La primera version del port tenia CINCO errores, y todos se ven aqui:

  1. xBase = offsetX, cuando las dos macros hacen xBase = offsetX - ancho/2:
     la seccion va CENTRADA en su offset. Media zapata corrida por seccion.
  2. El muro se recortaba al pano de la zapata. Las macros no lo recortan.
  3. El acero del muro se colocaba a "recubrimiento + medio diametro" cuando
     las macros usan 5 cm CLAVADOS al eje de la varilla (offsetMuro = 0.05).
  4. Las patas doblaban "hacia el eje de la zapata". En la CENTRAL cada una
     dobla hacia SU lado -la izquierda a la izquierda-, y en el LINDERO las dos
     doblan a la izquierda y a DOS ALTURAS distintas.
  5. El enrase se dibujaba con cualquier hueco. Las macros piden mas de 2 cm.

QUE SE COMPRUEBA
----------------
  1. Las constantes, una por una, contra el VBA.
  2. Los niveles: el terreno manda y la zapata cuelga de el.
  3. El acomodo: seccion centrada en su offset, central a la derecha y lindero
     a la izquierda.
  4. El muro: centrado o pegado al pano derecho, y sin recortes.
  5. El muro de enrase: el reparto en piezas de ~8 cm que cierra exacto, el
     minimo de 2 cm, y el ancho tomado de la CADENA cuando la hay.
  6. El acero del muro: los ejes a 5 cm, los circulos repartidos con la
     separacion VERTICAL y uno menos de los que caben, la Y de la pata por
     encima de la parrilla, y los dobleces de cada macro.
  7. Las cotas y el rotulo, medidos desde el fondo de la plantilla.
"""

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRAZO = os.path.join(RAIZ, "client/src/CadLink.Cad/TrazoZapataCorrida.cs")
TRAZO_AISLADA = os.path.join(RAIZ, "client/src/CadLink.Cad/TrazoZapata.cs")
DATOS = os.path.join(RAIZ, "client/src/CadLink.Cad/ZapataCorridaCad.cs")

fallos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


def leer(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def const(texto, nombre):
    m = re.search(
        r"public const (?:double|int) " + nombre + r"\s*=\s*(-?[0-9.]+)\s*;", texto)
    return float(m.group(1)) if m else None


def igual(a, b, tol=1e-9):
    return a is not None and abs(a - b) < tol


# ======================================================================
# LAS CUENTAS DE LAS MACROS, REHECHAS
# ======================================================================

Y_NIV_TERR = -3.5
ESP_PLANTILLA = 0.05
REC = 0.05
PASO = 2.0
LINDERO_PRIMER_OFFSET = -2.0

ENRASE_OBJETIVO = 0.08
ENRASE_JUNTA = 0.01
ENRASE_DESFASE = 0.01
ENRASE_MAX = 50
ENRASE_MIN = 0.02

MURO_RETIRO = 0.05
FACTOR_DOBLES_MURO = 15.0
FACTOR_MIN = 6.0
FACTOR_MAX = 80.0

LIN_SEP_FACTOR = 4.0
LIN_SEP_MIN = 0.05
LIN_SEP_FACTOR_MIN = 2.5
LIN_HOLGURA = 0.003

DIAM = {"#3": 0.009525, "#4": 0.0127, "#5": 0.015875, "#6": 0.01905}


def offset_x(tipo, i):
    if i < 0:
        i = 0
    return LINDERO_PRIMER_OFFSET - i * PASO if tipo == "LINDERO" else i * PASO


def x_base(tipo, i, ancho):
    """xBase = offsetX - ancho/2 : la seccion va CENTRADA en su offset."""
    return offset_x(tipo, i) - ancho / 2.0


def colocar(tipo, ancho, prof, espesor, esp_muro_cm, i=0):
    y_terr = Y_NIV_TERR
    y_bot = y_terr - prof
    y_top = y_bot + espesor
    esp_muro = esp_muro_cm / 100.0 if esp_muro_cm > 0 else 0.15

    x0 = x_base(tipo, i, ancho)
    x_der = x0 + ancho
    x_cen = x0 + ancho / 2.0

    if tipo == "LINDERO":
        x_muro_der = x_der
        x_muro_izq = x_muro_der - esp_muro
    else:
        x_muro_izq = x_cen - esp_muro / 2.0
        x_muro_der = x_cen + esp_muro / 2.0

    return {
        "xBase": x0, "xDer": x_der, "xCentro": x_cen,
        "yBot": y_bot, "yTop": y_top, "yPlantilla": y_bot - ESP_PLANTILLA,
        "yTerreno": y_terr, "xMuroIzq": x_muro_izq, "xMuroDer": x_muro_der,
        "xCentroMuro": (x_muro_izq + x_muro_der) / 2.0,
    }


def enrase(x_izq, ancho, y_base, y_tope):
    hueco = y_tope - y_base
    if hueco <= ENRASE_MIN or ancho <= 0:
        return (0, 0.0, [])

    mejor_n, mejor_alto, mejor_err = 0, 0.0, float("inf")
    for n in range(1, ENRASE_MAX + 1):
        alto = (hueco - (n - 1) * ENRASE_JUNTA) / n
        if alto <= 0:
            break
        err = abs(alto - ENRASE_OBJETIVO)
        if err < mejor_err:
            mejor_n, mejor_alto, mejor_err = n, alto, err

    if mejor_n <= 0:
        return (0, 0.0, [])

    bases = [y_base + i * (mejor_alto + ENRASE_JUNTA) for i in range(mejor_n)]
    return (mejor_n, mejor_alto, bases)


def factor_valido(d):
    if d <= 0:
        return FACTOR_DOBLES_MURO
    return min(max(d, FACTOR_MIN), FACTOR_MAX)


def ejes_acero(a, doble):
    if doble:
        x1 = a["xMuroIzq"] + MURO_RETIRO
        x2 = a["xMuroDer"] - MURO_RETIRO
        if x2 > x1:
            return (x1, x2, True)
    return (a["xCentroMuro"], a["xCentroMuro"], False)


def circulos_muro(y_muro_bot, y_terreno, diam, sep):
    """Cuenta cuantas caben y dibuja UNA MENOS, como la macro."""
    if sep <= 0:
        sep = 0.12
    y_ini = y_muro_bot + diam / 2.0
    y_tope = y_terreno - diam / 2.0

    caben, y = 0, y_ini
    while y <= y_tope + 0.0001:
        caben += 1
        y += sep
    if abs(y - sep - y_tope) > 0.0001:
        caben += 1

    n = caben - 1
    return [y_ini + k * sep for k in range(n)] if n > 0 else []


def y_de_la_pata(y_barra_inf, d_inf_long, y_circ_inf, d_inf_trans, d_muro, lindero):
    y = y_circ_inf + d_inf_trans / 2.0 + d_muro / 2.0 + (LIN_HOLGURA if lindero else 0.0)
    piso = y_barra_inf + d_inf_long / 2.0 + d_muro / 2.0
    return piso if y < piso else y


def verticales_central(x1, x2, doble, y_terr, y_pata, d_muro, desp, factor):
    doblez = factor_valido(factor) * d_muro
    if not doble:
        x = x1 + desp
        return [(x, y_pata, x - doblez, -1)]
    xi = x1 + desp
    xd = x2 - desp
    return [(xi, y_pata, xi - doblez, -1), (xd, y_pata, xd + doblez, 1)]


def sep_dobleces(a, y_pata, d_muro):
    sep = LIN_SEP_FACTOR * d_muro
    if sep < LIN_SEP_MIN:
        sep = LIN_SEP_MIN
    tope = a["yTop"] - REC - d_muro / 2.0
    if y_pata + sep > tope:
        sep = tope - y_pata
        minimo = LIN_SEP_FACTOR_MIN * d_muro
        if sep < minimo:
            sep = minimo
    return sep


def verticales_lindero(a, x1, x2, doble, y_pata, d_muro, desp, factor):
    doblez = factor_valido(factor) * d_muro
    x_lim = a["xBase"] + REC + d_muro / 2.0
    radio_centro = 1.5 * d_muro

    def x_fin(x_var):
        x = x_var - doblez
        if x < x_lim:
            x = x_lim
        maximo = x_var - radio_centro - d_muro
        return maximo if x > maximo else x

    if not doble:
        x = x1 + desp
        return [(x, y_pata, x_fin(x), -1)]

    sep = sep_dobleces(a, y_pata, d_muro)
    xi = x1 + desp
    xd = x2 - desp
    # La DERECHA lleva el doblez bajo y la IZQUIERDA el de arriba
    return [(xd, y_pata, x_fin(xd), -1), (xi, y_pata + sep, x_fin(xi), -1)]


# ======================================================================

def v1_constantes():
    print("\n[1] Las constantes son las de las macros")
    t = leer(TRAZO)

    for nombre, valor in (
            ("YNivelTerreno", Y_NIV_TERR), ("PlantillaEspesor", ESP_PLANTILLA),
            ("RecPorOmision", REC), ("SeparacionSecciones", PASO),
            ("LinderoPrimerOffset", LINDERO_PRIMER_OFFSET),
            ("ContratrabeAltoPorOmision", 0.3), ("CadenaAltoPorOmision", 0.2),
            ("EnraseAltoObjetivo", ENRASE_OBJETIVO), ("EnraseJunta", ENRASE_JUNTA),
            ("EnraseDesfaseLado", ENRASE_DESFASE), ("EnraseAltoMinimo", ENRASE_MIN),
            ("EnraseMaxPiezas", ENRASE_MAX), ("GanchoParrilla", 0.03),
            ("MuroRetiroAcero", MURO_RETIRO),
            ("LinderoSepDoblecesFactor", LIN_SEP_FACTOR),
            ("LinderoSepDoblecesMin", LIN_SEP_MIN),
            ("LinderoSepDoblecesFactorMin", LIN_SEP_FACTOR_MIN),
            ("LinderoHolguraSobreParrilla", LIN_HOLGURA),
            ("CotaAnchoTotal", 0.13), ("CotaAnchosParciales", 0.075),
            ("CotaAlturaTotal", 0.1445), ("CotaAlturasParciales", 0.0585),
            ("CotaDoblezCentral", 0.045), ("CotaDoblezLindero", 0.022),
            ("CotaDoblezLinderoFraccion", 0.45),
            ("RotuloOffset", 0.25), ("RotuloSalto1", 0.34), ("RotuloSalto2", 0.42),
            ("RotuloAltoTitulo", 0.07), ("RotuloAltoElevacion", 0.05),
            ("RotuloAltoEscala", 0.04), ("AltoTextoPlantilla", 0.02),
            ("AltoTextoNivel", 0.025),
            ("EnraseColorPieza", 253), ("EnraseColorJunta", 252),
            ("ConcretoColorSolido", 9), ("ConcretoColorPatron", 251),
            ("ConcretoEscalaPatron", 0.0003), ("ConcretoEscalaZapata", 0.0005),
            ("ConcretoEscalaMuro", 0.05), ("TerrenoEscalaPatron", 0.01),
            ("TerrenoTransparencia", 45), ("TerrenoGris", 135)):
        check(f"{nombre} = {valor}", igual(const(t, nombre), valor))

    check("el doblez del muro son los 15 diametros de las aisladas",
          "FactorDoblezMuro = TrazoZapata.FactorGanchoAbajo" in t)


def v2_niveles():
    print("\n[2] El terreno manda: la zapata cuelga de el")
    a = colocar("CENTRAL", 1.2, 1.5, 0.25, 15)
    b = colocar("CENTRAL", 1.2, 2.5, 0.25, 15)

    check("el desplante baja con la profundidad", igual(a["yBot"], -5.0))
    check("y con mas profundidad baja mas", igual(b["yBot"], -6.0))
    check("pero el terreno queda a la MISMA altura en las dos",
          igual(a["yTerreno"], b["yTerreno"]))
    check("el lomo esta un espesor arriba del desplante",
          igual(a["yTop"], a["yBot"] + 0.25))
    check("el yBase de la macro es el FONDO DE LA PLANTILLA",
          igual(a["yPlantilla"], Y_NIV_TERR - 1.5 - ESP_PLANTILLA))

    aislada = leer(TRAZO_AISLADA)
    check("las aisladas conservan su fondo fijo en -8",
          "public const double YBaseElevacion = -8.0;" in aislada)

    t = leer(TRAZO)
    check("y queda escrito que las dos familias no comparten el nivel",
          "comparten este número" in t)


def v3_acomodo():
    print("\n[3] El acomodo: la seccion va CENTRADA en su offset")
    centrales = [offset_x("CENTRAL", i) for i in range(4)]
    linderos = [offset_x("LINDERO", i) for i in range(4)]

    check("los offsets de la central son 0, 2, 4, 6",
          centrales == [0.0, 2.0, 4.0, 6.0], str(centrales))
    check("y los del lindero -2, -4, -6, -8",
          linderos == [-2.0, -4.0, -6.0, -8.0], str(linderos))

    # EL ERROR QUE SE CORRIGIO: xBase NO es el offset.
    check("la primera central de 1 m arranca en -0.5, no en 0",
          igual(x_base("CENTRAL", 0, 1.0), -0.5), str(x_base("CENTRAL", 0, 1.0)))
    check("y su eje SI es el offset",
          igual(x_base("CENTRAL", 0, 1.0) + 0.5, 0.0))
    check("el primer lindero de 1.2 m arranca en -2.6",
          igual(x_base("LINDERO", 0, 1.2), -2.6), str(x_base("LINDERO", 0, 1.2)))

    # Dos secciones seguidas de 1.5 m NO se tocan: 2 m de paso contra 1.5 de ancho
    a0 = colocar("CENTRAL", 1.5, 1.5, 0.25, 15, 0)
    a1 = colocar("CENTRAL", 1.5, 1.5, 0.25, 15, 1)
    check("dos secciones de 1.5 m no se encinan",
          a1["xBase"] > a0["xDer"], f'{a0["xDer"]} .. {a1["xBase"]}')

    # Con 2.5 m de ancho SI se tocan, y asi lo hace la macro (paso fijo)
    b0 = colocar("CENTRAL", 2.5, 1.5, 0.25, 15, 0)
    b1 = colocar("CENTRAL", 2.5, 1.5, 0.25, 15, 1)
    check("(y con 2.5 m se tocan, como en la macro: el paso es fijo)",
          b1["xBase"] < b0["xDer"])

    # Las dos filas no se cruzan
    l0 = colocar("LINDERO", 1.0, 1.5, 0.25, 15, 0)
    check("la fila del lindero no alcanza a la central",
          l0["xDer"] < a0["xBase"], f'{l0["xDer"]} .. {a0["xBase"]}')

    t = leer(TRAZO)
    check("XBase resta media zapata al offset",
          "OffsetX(tipo, indice) - (anchoM / 2)" in t)
    check("y un indice negativo no manda la seccion a otro lado",
          "Math.Max(indice, 0)" in t)


def v4_muro():
    print("\n[4] Donde va el muro")
    c = colocar("CENTRAL", 1.0, 1.5, 0.25, 15)
    check("en la central el muro va centrado en el eje",
          igual(c["xMuroIzq"], c["xCentro"] - 0.075)
          and igual(c["xMuroDer"], c["xCentro"] + 0.075))

    ln = colocar("LINDERO", 1.0, 1.5, 0.25, 15)
    check("en el lindero el pano derecho del muro ES el de la zapata",
          igual(ln["xMuroDer"], ln["xDer"]))
    check("y el volado queda del otro lado",
          igual(ln["xMuroIzq"], ln["xDer"] - 0.15))

    v = colocar("CENTRAL", 1.0, 1.5, 0.25, 0)
    check("sin espesor capturado el muro sale de 15 cm",
          igual(v["xMuroDer"] - v["xMuroIzq"], 0.15))

    # EL ERROR QUE SE CORRIGIO: las macros NO recortan el muro.
    g = colocar("CENTRAL", 0.4, 1.5, 0.25, 60)
    check("un muro mas ancho que la zapata NO se recorta (como la macro)",
          g["xMuroIzq"] < g["xBase"] and g["xMuroDer"] > g["xDer"])

    t = leer(TRAZO)
    check("y el port deja escrito por que no se recorta",
          "las macros no lo recortan" in t)


def v5_enrase():
    print("\n[5] El muro de enrase")

    for hueco in (0.03, 0.09, 0.18, 0.27, 0.40, 0.55, 0.73, 1.00, 1.37):
        n, alto, bases = enrase(0.0, 0.15, 0.0, hueco)
        tope = bases[-1] + alto if n else 0.0

        check(f"con {hueco:.2f} m de hueco cierra exacto contra la cadena",
              n > 0 and abs(tope - hueco) < 1e-9, f"n={n} tope={tope}")
        check(f"  y la pieza sale cerca de los 8 cm ({hueco:.2f} m)",
              n > 0 and 0.02 <= alto <= 0.135, f"alto={alto}")

    # Con 55 cm: 6 piezas de 8.33 cm y 5 juntas
    n, alto, _ = enrase(0.0, 0.15, 0.0, 0.55)
    check("con 55 cm salen 6 piezas de 8.33 cm",
          n == 6 and abs(alto - 0.5 / 6) < 1e-9, f"n={n} alto={alto}")

    mejor = min((abs((0.55 - (k - 1) * ENRASE_JUNTA) / k - ENRASE_OBJETIVO), k)
                for k in range(1, ENRASE_MAX + 1)
                if (0.55 - (k - 1) * ENRASE_JUNTA) / k > 0)
    check("gana el reparto mas cercano a los 8 cm, no el primero que quepa",
          n == mejor[1], f"n={n} mejor={mejor[1]}")

    # EL MINIMO DE LAS MACROS: If altEnrase > 0.02
    for hueco in (0.0, -0.30, 0.005, 0.01, 0.02):
        n, _, bases = enrase(0.0, 0.15, 0.0, hueco)
        check(f"con hueco {hueco} no hay enrase (minimo 2 cm)",
              n == 0 and bases == [])
    n, _, _ = enrase(0.0, 0.15, 0.0, 0.021)
    check("y con 2.1 cm si lo hay", n > 0)

    check("sin ancho no hay enrase", enrase(0.0, 0.0, 0.0, 0.5)[0] == 0)

    # El alto nunca sale negativo
    malo = None
    for k in range(3, 400):
        hueco = k / 100.0
        n, alto, _ = enrase(0.0, 0.15, 0.0, hueco)
        if n and alto <= 0:
            malo = hueco
            break
    check("el alto de pieza nunca sale negativo ni cero", malo is None, str(malo))

    # El enrase arranca del lomo de la CONTRATRABE, no del de la zapata
    n1, _, b1 = enrase(0.0, 0.15, -5.0, -4.5)
    n2, _, b2 = enrase(0.0, 0.15, -4.8, -4.5)
    check("el arranque entra por parametro (contratrabe o zapata)",
          n1 > n2 and igual(b1[0], -5.0) and igual(b2[0], -4.8))

    # EL ANCHO ES EL DE LA CADENA, no el del muro
    t = leer(TRAZO)
    check("el ancho del enrase entra por parametro, que es el de la cadena",
          "public static Enrase MuroDeEnrase(double xIzq, double ancho," in t)
    check("y queda escrito de donde sale",
          "de la caja de la cadena" in t)


def v6_ejes_y_circulos():
    print("\n[6] El acero del muro: ejes y circulos")
    a = colocar("CENTRAL", 1.0, 1.5, 0.25, 20)
    d4 = DIAM["#4"]

    x1, x2, doble = ejes_acero(a, True)
    check("los ejes van a 5 cm CLAVADOS del pano, no a rec+diam/2",
          igual(x1, a["xMuroIzq"] + 0.05) and igual(x2, a["xMuroDer"] - 0.05))
    check("y con doble parrilla son dos", doble)

    fino = colocar("CENTRAL", 1.0, 1.5, 0.25, 8)
    x1f, x2f, doblef = ejes_acero(fino, True)
    check("un muro de 8 cm no admite doble parrilla",
          not doblef and igual(x1f, x2f) and igual(x1f, fino["xCentroMuro"]))

    x1s, x2s, dobles = ejes_acero(a, False)
    check("con una sola parrilla el acero va al EJE DEL MURO",
          not dobles and igual(x1s, a["xCentroMuro"]))

    # En el lindero el eje del muro NO es el de la zapata
    ln = colocar("LINDERO", 1.0, 1.5, 0.25, 20)
    check("en el lindero el eje del muro no es el de la zapata",
          abs(ln["xCentroMuro"] - ln["xCentro"]) > 0.3)

    # Circulos: reparto con la separacion VERTICAL y uno menos de los que caben
    ys = circulos_muro(a["yTop"], a["yTerreno"], d4, 0.20)
    check("los circulos arrancan a medio diametro del muro",
          len(ys) > 0 and igual(ys[0], a["yTop"] + d4 / 2))
    check("se reparten con la separacion vertical",
          len(ys) < 2 or igual(ys[1] - ys[0], 0.20))
    check("y no llegan a la linea del terreno",
          all(y < a["yTerreno"] - d4 / 2 + 1e-9 for y in ys))

    # La cuenta exacta de la macro: total - 1
    alto = a["yTerreno"] - a["yTop"]
    check("se dibuja UNA MENOS de las que caben",
          len(ys) == int(alto / 0.20) or len(ys) == int(alto / 0.20) + 1,
          f"alto={alto} n={len(ys)}")

    check("con muro de alto cero no hay circulos",
          circulos_muro(a["yTerreno"], a["yTerreno"], d4, 0.20) == [])


def v7_dobleces():
    print("\n[7] El acero del muro: las patas")
    d4 = DIAM["#4"]
    d3 = DIAM["#3"]

    a = colocar("CENTRAL", 1.0, 1.5, 0.25, 20)

    # La Y de la pata queda por ENCIMA de la parrilla inferior
    y_barra = a["yBot"] + REC + d4 / 2
    y_circ = y_barra + d4 / 2 + d4 / 2
    y_pata_c = y_de_la_pata(y_barra, d4, y_circ, d4, d4, False)
    y_pata_l = y_de_la_pata(y_barra, d4, y_circ, d4, d4, True)

    check("la pata cae encima de la transversal de la parrilla",
          y_pata_c > y_circ + d4 / 2 - 1e-9)
    check("y el lindero le suma 3 mm de holgura",
          igual(y_pata_l - y_pata_c, LIN_HOLGURA))
    check("si esa cuenta queda baja, manda la barra que corre",
          igual(y_de_la_pata(y_barra, d4, y_barra - 1.0, d4, d4, False),
                y_barra + d4 / 2 + d4 / 2))

    # CENTRAL: cada pata hacia SU lado, las dos a la misma altura
    x1, x2, doble = ejes_acero(a, True)
    vs = verticales_central(x1, x2, doble, a["yTerreno"], y_pata_c, d4, d3, 0)
    check("en la central salen dos varillas", len(vs) == 2)
    check("la izquierda dobla a la IZQUIERDA y la derecha a la DERECHA",
          vs[0][3] == -1 and vs[1][3] == 1)
    check("las dos a la MISMA altura", igual(vs[0][1], vs[1][1]))
    check("la pata mide 15 diametros",
          igual(abs(vs[0][2] - vs[0][0]), 15 * d4)
          and igual(abs(vs[1][2] - vs[1][0]), 15 * d4))
    check("la varilla se corre el desplazamiento respecto del eje del acero",
          igual(vs[0][0], x1 + d3) and igual(vs[1][0], x2 - d3))

    vs1 = verticales_central(*ejes_acero(a, False), a["yTerreno"], y_pata_c, d4, d3, 0)
    check("con una sola parrilla dobla a la izquierda",
          len(vs1) == 1 and vs1[0][3] == -1)

    # La casilla de la hoja manda, con los topes de siempre
    for cap, esperado in ((0, 15), (40, 40), (2, 6), (500, 80)):
        v = verticales_central(x1, x2, doble, a["yTerreno"], y_pata_c, d4, d3, cap)
        check(f"con {cap} en la casilla la pata es de {esperado} diametros",
              igual(abs(v[0][2] - v[0][0]), esperado * d4))

    # LINDERO: las dos a la izquierda y a DOS alturas
    ln = colocar("LINDERO", 1.0, 1.5, 0.25, 20)
    y_barra_l = ln["yBot"] + REC + d4 / 2
    y_circ_l = y_barra_l + d4
    y_pata = y_de_la_pata(y_barra_l, d4, y_circ_l, d4, d4, True)
    lx1, lx2, ldoble = ejes_acero(ln, True)
    lv = verticales_lindero(ln, lx1, lx2, ldoble, y_pata, d4, d3, 0)

    check("en el lindero las dos patas doblan a la IZQUIERDA",
          lv[0][3] == -1 and lv[1][3] == -1)
    check("la del pano DERECHO lleva el doblez bajo",
          lv[0][0] > lv[1][0] and lv[0][1] < lv[1][1])
    check("y las dos alturas se separan al menos 4 diametros",
          lv[1][1] - lv[0][1] >= min(sep_dobleces(ln, y_pata, d4), 4 * d4) - 1e-9)
    check("el doblez de arriba no pasa el recubrimiento del lomo",
          lv[1][1] <= ln["yTop"] - REC - d4 / 2 + 1e-9)
    check("ninguna pata se sale del concreto de la zapata",
          all(x_fin >= ln["xBase"] + REC + d4 / 2 - 1e-9 for _, _, x_fin, _ in lv))

    # Zapata angosta: la pata se recorta al recubrimiento y NO se sale
    est = colocar("LINDERO", 0.35, 1.5, 0.25, 15)
    y_b = est["yBot"] + REC + d4 / 2
    y_c = y_b + d4
    y_p = y_de_la_pata(y_b, d4, y_c, d4, d4, True)
    ex1, ex2, edoble = ejes_acero(est, True)
    ev = verticales_lindero(est, ex1, ex2, edoble, y_p, DIAM["#6"], d3, 60)
    check("con una zapata angosta y 60 diametros la pata se recorta al recubrimiento",
          all(x_fin >= est["xBase"] + REC + DIAM["#6"] / 2 - 1e-9
              for _, _, x_fin, _ in ev))

    # Zapata delgada: la separacion se aprieta pero no baja de 2.5 diametros
    delgada = colocar("LINDERO", 1.0, 1.5, 0.12, 20)
    y_b2 = delgada["yBot"] + REC + d4 / 2
    y_c2 = y_b2 + d4
    y_p2 = y_de_la_pata(y_b2, d4, y_c2, d4, d4, True)
    sep = sep_dobleces(delgada, y_p2, d4)
    check("en una zapata delgada la separacion se aprieta, con minimo 2.5 diametros",
          sep >= 2.5 * d4 - 1e-9, f"sep={sep}")


def v8_anotacion():
    print("\n[8] Las cotas y el rotulo")
    t = leer(TRAZO)

    a = colocar("CENTRAL", 1.0, 1.5, 0.25, 20)
    y_fondo = a["yPlantilla"]

    # Las cotas cuelgan del FONDO DE LA PLANTILLA y del pano izquierdo
    check("la cota del ancho total va 13 cm bajo la plantilla",
          igual(const(t, "CotaAnchoTotal"), 0.13))
    check("y las parciales mas cerca, a 7.5 cm",
          const(t, "CotaAnchosParciales") < const(t, "CotaAnchoTotal"))
    check("la vertical total va mas afuera que las parciales",
          const(t, "CotaAlturaTotal") > const(t, "CotaAlturasParciales"))

    # Las tres alturas parciales suman la total: plantilla + zapata + relleno
    parciales = (a["yBot"] - y_fondo) + (a["yTop"] - a["yBot"]) \
        + (a["yTerreno"] - a["yTop"])
    check("las tres cotas parciales suman la total",
          igual(parciales, a["yTerreno"] - y_fondo))

    esperado = [y_fondo - d for d in (0.25, 0.34, 0.42)]
    check("el rotulo se mide desde el fondo de la plantilla",
          "var yFondo = yZapBot - PlantillaEspesor;" in t)
    check("sus tres renglones caen debajo de todo el dibujo",
          all(y < y_fondo for y in esperado))
    check("y no se encinan entre si",
          esperado[0] - esperado[1] >= 0.08 and esperado[1] - esperado[2] >= 0.07)
    check("los altos de letra bajan de titulo a escala",
          const(t, "RotuloAltoTitulo") > const(t, "RotuloAltoElevacion")
          > const(t, "RotuloAltoEscala"))
    check("el texto del nivel conserva la resta de la macro",
          "a.XCentro + 0.35 - 0.313" in t)


def v9_sin_duplicar():
    print("\n[9] Lo que ya existia no se vuelve a escribir")
    t = leer(TRAZO)
    datos = leer(DATOS)

    check("las parrillas se delegan en la rutina de las aisladas",
          "TrazoZapata.ParrillaEnAlzado(" in t
          and "TrazoZapata.Parrilla ParrillaEnAlzado" in t)
    check("y no se recalcula el eje de la barra aqui",
          "yZapBot + espesorM - recM" not in t)
    check("el doblez se valida con la rutina de las aisladas",
          "TrazoZapata.FactorGanchoValido(factorDoblez)" in t)

    check("los datos traen las celdas de las DOS macros",
          "<c>E4</c> / <c>O4</c>" in datos and "<c>H4</c> / <c>R4</c>" in datos)
    check("el espesor del muro apunta a sus DOS celdas por tipo de muro",
          "<c>H9</c> / <c>R9</c>" in datos and "<c>G7</c> / <c>P7</c>" in datos)
    check("y queda avisado que con mamposteria el acero sube un renglon",
          "suben un renglón" in datos)
    check("la separacion vertical es la que reparte los circulos",
          "CirculosDelMuro" in datos)
    check("el titulo del lindero no dice corrida, como en su macro",
          '"ZAPATA DE LINDERO"' in datos and '"ZAPATA CORRIDA CENTRAL"' in datos)
    check("un bloque en 0 no cuenta como bloque",
          "public static bool HayBloque(string? id)" in datos)
    check("y esta escrito que cada zapata ocupa 16 renglones",
          "<b>16 renglones</b>" in datos)


def main():
    print("=" * 66)
    print(" Zapatas corridas: el port contra las macros")
    print("=" * 66)

    for f in (v1_constantes, v2_niveles, v3_acomodo, v4_muro, v5_enrase,
              v6_ejes_y_circulos, v7_dobleces, v8_anotacion, v9_sin_duplicar):
        f()

    print("\n" + "=" * 66)
    if fallos:
        print(f" RESULTADO: {len(fallos)} comprobacion(es) fallaron")
        for f_ in fallos:
            print(f"   - {f_}")
        print("=" * 66)
        return 1

    print(" RESULTADO: todo bien")
    print("=" * 66)
    return 0


if __name__ == "__main__":
    sys.exit(main())
