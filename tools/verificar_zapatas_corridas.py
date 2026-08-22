"""Verifica el port de ZAPATA CORRIDA CENTRAL V2 y ZAPATA CORRIDA LINDERO V2.

Escrito aparte para poder EJECUTARLO. Aqui se rehacen en Python las cuentas de las
dos macros y se comparan, numero a numero, contra las constantes y las reglas que
quedaron escritas en TrazoZapataCorrida.cs. Compilar no prueba que un numero sea el
de la macro; esto si.

QUE SE COMPRUEBA
----------------
  1. Los niveles: el terreno manda y la zapata cuelga de el, al contrario que las
     aisladas, que tienen el fondo fijo. Mezclar las dos reglas descuadra el corte.
  2. El acomodo: la central crece a la derecha desde 0 y el lindero a la izquierda
     desde -2, asi que las dos familias no se encinan en el mismo dibujo.
  3. El muro: centrado en la central, pegado al pano derecho en el lindero, y
     recortado si viene mas ancho que la zapata.
  4. El muro de enrase: el reparto en piezas de ~8 cm con junta de 1 cm, que es la
     unica cuenta con truco de las dos macros. Debe cerrar EXACTO contra la cadena.
  5. El acero del muro: cuantas barras, donde, y la pata de 15 diametros doblada
     hacia el eje de la zapata, que es el unico lado donde hay concreto.
  6. Que las parrillas NO se recalculan: se delega en la rutina de las aisladas,
     porque en las macros es la misma.
  7. Que el rotulo se mide desde el fondo de la PLANTILLA, no de la zapata.
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
    """El valor de un `const double`/`const int` del fuente, o None."""
    m = re.search(
        r"public const (?:double|int) " + nombre + r"\s*=\s*(-?[0-9.]+)\s*;", texto)
    return float(m.group(1)) if m else None


def igual(a, b, tol=1e-9):
    return a is not None and abs(a - b) < tol


# ======================================================================
# Las cuentas de las macros, rehechas
# ======================================================================

Y_NIV_TERR = -3.5
ESP_PLANTILLA = 0.05
REC = 0.05
PASO = 2.0
LINDERO_PRIMERA = -2.0

ENRASE_OBJETIVO = 0.08
ENRASE_JUNTA = 0.01
ENRASE_MAX = 50

FACTOR_GANCHO = 15.0
FACTOR_MIN = 6.0
FACTOR_MAX = 80.0


def x_base(tipo, i):
    return LINDERO_PRIMERA - i * PASO if tipo == "LINDERO" else i * PASO


def colocar(tipo, ancho, prof, espesor, esp_muro_cm):
    """Port en Python del Colocar de la macro."""
    y_terr = Y_NIV_TERR
    y_bot = y_terr - prof
    y_top = y_bot + espesor
    esp_muro = esp_muro_cm / 100.0 if esp_muro_cm > 0 else 0.15

    x0 = 0.0
    x_der = x0 + ancho
    x_cen = x0 + ancho / 2

    if tipo == "LINDERO":
        x_muro_der = x_der
        x_muro_izq = max(x_muro_der - esp_muro, x0)
    else:
        x_muro_izq = max(x_cen - esp_muro / 2, x0)
        x_muro_der = min(x_cen + esp_muro / 2, x_der)

    return {
        "xBase": x0, "xDer": x_der, "xCentro": x_cen,
        "yBot": y_bot, "yTop": y_top, "yPlantilla": y_bot - ESP_PLANTILLA,
        "yTerreno": y_terr, "xMuroIzq": x_muro_izq, "xMuroDer": x_muro_der,
    }


def enrase(y_base, y_tope):
    """El reparto del muro de enrase: (piezas, alto, bases)."""
    hueco = y_tope - y_base
    if hueco <= ENRASE_JUNTA:
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
        return FACTOR_GANCHO
    return min(max(d, FACTOR_MIN), FACTOR_MAX)


def verticales(a, doble, diam, factor):
    rec = REC
    if doble and (a["xMuroDer"] - a["xMuroIzq"]) > 2 * rec + diam:
        xs = [a["xMuroIzq"] + rec + diam / 2, a["xMuroDer"] - rec - diam / 2]
    else:
        xs = [(a["xMuroIzq"] + a["xMuroDer"]) / 2]

    sentidos = [-1 if x > a["xCentro"] + 1e-9 else 1 for x in xs]
    return xs, sentidos, factor_valido(factor) * diam


# ======================================================================

def v1_constantes():
    print("\n[1] Las constantes son las de las macros")
    t = leer(TRAZO)

    check("el nivel de terreno es -3.5", igual(const(t, "YNivelTerreno"), Y_NIV_TERR))
    check("la plantilla es de 5 cm", igual(const(t, "PlantillaEspesor"), ESP_PLANTILLA))
    check("el recubrimiento es de 5 cm", igual(const(t, "RecPorOmision"), REC))
    check("el paso entre secciones es de 2 m",
          igual(const(t, "SeparacionSecciones"), PASO))
    check("el lindero arranca en -2", igual(const(t, "LinderoXPrimera"), LINDERO_PRIMERA))
    check("la pieza del enrase busca los 8 cm",
          igual(const(t, "EnraseAltoObjetivo"), ENRASE_OBJETIVO))
    check("la junta del enrase es de 1 cm", igual(const(t, "EnraseJunta"), ENRASE_JUNTA))
    check("la pieza se mete 1 cm por lado",
          igual(const(t, "EnraseDesfaseLado"), 0.01))
    check("el reparto se busca hasta 50 piezas",
          igual(const(t, "EnraseMaxPiezas"), ENRASE_MAX))
    check("la escala del patron es la del relleno de siempre",
          igual(const(t, "ConcretoEscalaPatron"), 0.0003))

    for nombre, valor in (("EnraseColorPieza", 253), ("EnraseColorJunta", 252),
                          ("ConcretoColorSolido", 9), ("ConcretoColorPatron", 251)):
        check(f"el color {nombre} es {valor}", igual(const(t, nombre), valor))

    for nombre, valor in (("CotaOffsetVert1", 0.13), ("CotaOffsetHoriz", 0.075),
                          ("CotaOffsetVert2", 0.1445), ("CotaOffsetHoriz2", 0.0585),
                          ("RotuloOffset", 0.25), ("RotuloSalto1", 0.34),
                          ("RotuloSalto2", 0.42)):
        check(f"la distancia {nombre} vale {valor}", igual(const(t, nombre), valor))


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
    check("la plantilla va debajo del desplante",
          igual(a["yPlantilla"], a["yBot"] - 0.05))

    # La regla CONTRARIA es la de las aisladas, y sigue estando ahi: fondo fijo en -8.
    aislada = leer(TRAZO_AISLADA)
    check("las aisladas conservan su fondo fijo en -8",
          "public const double YBaseElevacion = -8.0;" in aislada)

    t = leer(TRAZO)
    check("y queda escrito que las dos familias no comparten el nivel",
          "no</b> comparten este número" in t)


def v3_acomodo():
    print("\n[3] El acomodo de las dos filas")
    centrales = [x_base("CENTRAL", i) for i in range(4)]
    linderos = [x_base("LINDERO", i) for i in range(4)]

    check("la central arranca en 0 y crece a la derecha",
          centrales == [0.0, 2.0, 4.0, 6.0], str(centrales))
    check("el lindero arranca en -2 y crece a la izquierda",
          linderos == [-2.0, -4.0, -6.0, -8.0], str(linderos))
    check("y las dos filas no se encinan",
          max(linderos) + 1.5 <= min(centrales))

    t = leer(TRAZO)
    check("XBase no acepta indices negativos",
          "Math.Max(indice, 0)" in t)


def v4_muro():
    print("\n[4] Donde va el muro")
    c = colocar("CENTRAL", 1.0, 1.5, 0.25, 15)
    check("en la central el muro va centrado",
          igual(c["xMuroIzq"], 0.425) and igual(c["xMuroDer"], 0.575),
          f'{c["xMuroIzq"]}..{c["xMuroDer"]}')

    ln = colocar("LINDERO", 1.0, 1.5, 0.25, 15)
    check("en el lindero el pano derecho del muro ES el de la zapata",
          igual(ln["xMuroDer"], ln["xDer"]))
    check("y el muro queda dentro de la zapata",
          igual(ln["xMuroIzq"], 0.85))

    # Espesor por omision: la macro usa 15 cm cuando la celda esta vacia.
    v = colocar("CENTRAL", 1.0, 1.5, 0.25, 0)
    check("sin espesor capturado el muro sale de 15 cm",
          igual(v["xMuroDer"] - v["xMuroIzq"], 0.15))

    # Un muro mas ancho que la zapata se recorta, no se sale del concreto.
    ancho_c = colocar("CENTRAL", 0.4, 1.5, 0.25, 60)
    check("un muro mas ancho que la central se recorta a sus dos panos",
          igual(ancho_c["xMuroIzq"], 0.0) and igual(ancho_c["xMuroDer"], 0.4))

    ancho_l = colocar("LINDERO", 0.4, 1.5, 0.25, 60)
    check("y en el lindero se recorta al pano izquierdo",
          igual(ancho_l["xMuroIzq"], 0.0) and igual(ancho_l["xMuroDer"], 0.4))


def v5_enrase():
    print("\n[5] El muro de enrase, la unica cuenta con truco")

    # Hueco tipico: del lomo de la zapata al fondo de la cadena.
    for hueco in (0.09, 0.18, 0.27, 0.40, 0.55, 0.73, 1.00, 1.37):
        n, alto, bases = enrase(0.0, hueco)
        tope = bases[-1] + alto if n else 0.0

        check(f"con {hueco:.2f} m de hueco cierra exacto contra la cadena",
              n > 0 and abs(tope - hueco) < 1e-9, f"n={n} tope={tope}")
        check(f"  y la pieza sale cerca de los 8 cm ({hueco:.2f} m)",
              n > 0 and 0.04 <= alto <= 0.135, f"alto={alto}")
        check(f"  sin media pieza al final ({hueco:.2f} m)",
              n > 0 and all(abs((bases[i + 1] - bases[i]) - (alto + ENRASE_JUNTA)) < 1e-9
                            for i in range(n - 1)))

    # El reparto elegido es el MEJOR de los 50, no el primero que quepa.
    n, alto, _ = enrase(0.0, 0.55)
    mejor = min(
        ((abs((0.55 - (k - 1) * ENRASE_JUNTA) / k - ENRASE_OBJETIVO), k)
         for k in range(1, ENRASE_MAX + 1)
         if (0.55 - (k - 1) * ENRASE_JUNTA) / k > 0))
    check("gana el reparto mas cercano a los 8 cm, no el primero",
          n == mejor[1], f"n={n} mejor={mejor[1]}")

    # Casos degenerados: no hay enrase, y NO hay una pieza aplastada.
    for hueco in (0.0, -0.3, 0.005, ENRASE_JUNTA):
        n, alto, bases = enrase(0.0, hueco)
        check(f"con hueco {hueco} no se dibuja enrase", n == 0 and bases == [])

    # Nunca sale un alto negativo, aunque las juntas se coman el hueco.
    for hueco in [x / 100 for x in range(2, 200)]:
        n, alto, _ = enrase(0.0, hueco)
        if n and alto <= 0:
            check(f"alto positivo con hueco {hueco}", False, f"alto={alto}")
            break
    else:
        check("el alto de pieza nunca sale negativo ni cero", True)

    # El enrase arranca de donde se le diga: del lomo de la zapata o de la
    # contratrabe. Es lo que hace que la contratrabe mande.
    n1, a1, b1 = enrase(-5.0, -4.5)
    n2, a2, b2 = enrase(-4.8, -4.5)
    check("el arranque del enrase entra por parametro",
          n1 > n2 and igual(b1[0], -5.0) and igual(b2[0], -4.8))


def v6_acero_del_muro():
    print("\n[6] El acero vertical del muro y su pata")
    diam = 0.0127  # #4

    c = colocar("CENTRAL", 1.0, 1.5, 0.25, 20)
    xs, sentidos, doblez = verticales(c, True, diam, 0)

    check("con doble parrilla salen dos barras", len(xs) == 2)
    check("cada una a su recubrimiento del pano",
          igual(xs[0], c["xMuroIzq"] + REC + diam / 2)
          and igual(xs[1], c["xMuroDer"] - REC - diam / 2))
    check("en la central las dos patas se miran",
          sentidos == [1, -1], str(sentidos))
    check("la pata son 15 diametros por omision",
          igual(doblez, 15 * diam), f"{doblez}")

    xs1, sent1, _ = verticales(c, False, diam, 0)
    check("con una sola parrilla la barra va al eje del muro",
          len(xs1) == 1 and igual(xs1[0], (c["xMuroIzq"] + c["xMuroDer"]) / 2))

    ln = colocar("LINDERO", 1.0, 1.5, 0.25, 20)
    xs2, sent2, _ = verticales(ln, True, diam, 0)
    check("en el lindero las dos patas doblan hacia el eje, lejos del lindero",
          sent2 == [-1, -1], str(sent2))

    # La casilla de la hoja manda, con los mismos topes que las aisladas.
    check("con 40 en la casilla la pata es de 40 diametros",
          igual(verticales(c, True, diam, 40)[2], 40 * diam))
    check("una casilla en blanco cae en los 15 de la macro",
          igual(verticales(c, True, diam, 0)[2], 15 * diam))
    check("un 2 se sube al minimo de 6",
          igual(verticales(c, True, diam, 2)[2], 6 * diam))
    check("un 500 se baja al maximo de 80",
          igual(verticales(c, True, diam, 500)[2], 80 * diam))

    # Y el tope lo pone UNA sola rutina, la de las aisladas: si cada familia
    # validara lo suyo, medio plano saldria con patas de otro largo.
    t = leer(TRAZO)
    check("el factor se valida con la rutina de las aisladas, no con una copia",
          "TrazoZapata.FactorGanchoValido(factorDoblez)" in t)
    check("y no hay un factor de gancho propio duplicado",
          "FactorGanchoAbajo" not in t and "FactorGanchoMinimo" not in t)

    # Un muro delgado no puede llevar dos barras: no cabrian los recubrimientos.
    fino = colocar("CENTRAL", 1.0, 1.5, 0.25, 8)
    check("un muro de 8 cm no admite doble parrilla y lleva una barra al eje",
          len(verticales(fino, True, diam, 0)[0]) == 1)


def v7_sin_duplicar():
    print("\n[7] Lo que ya existia no se vuelve a escribir")
    t = leer(TRAZO)

    check("las parrillas se delegan en la rutina de las aisladas",
          "TrazoZapata.ParrillaEnAlzado(" in t)
    check("y no se recalcula el eje de la barra aqui",
          "yZapBot + espesorM - recM" not in t)
    check("el tipo de la parrilla es el mismo, no una copia",
          "TrazoZapata.Parrilla ParrillaEnAlzado" in t)

    datos = leer(DATOS)
    check("los datos traen las celdas de las DOS macros anotadas",
          "<c>E4</c> / <c>O4</c>" in datos)
    check("y el titulo del lindero no dice corrida, como en su macro",
          '"ZAPATA DE LINDERO"' in datos and '"ZAPATA CORRIDA CENTRAL"' in datos)
    check("un bloque en 0 no cuenta como bloque",
          "public static bool HayBloque(string? id)" in datos)


def v8_rotulo_y_muro():
    print("\n[8] El rotulo y el alto del muro")
    t = leer(TRAZO)

    y_bot = -5.0
    esperado = [y_bot - ESP_PLANTILLA - d for d in (0.25, 0.34, 0.42)]

    check("el rotulo se mide desde el fondo de la plantilla",
          "var yFondo = yZapBot - PlantillaEspesor;" in t)
    check("y sus tres renglones caen debajo de todo el dibujo",
          all(y < y_bot - ESP_PLANTILLA for y in esperado))
    check("los renglones no se encinan entre si",
          esperado[0] - esperado[1] >= 0.08 and esperado[1] - esperado[2] >= 0.08)

    check("un muro nunca sale de alto negativo",
          "Math.Max(yTope, yBase)" in t)


def main():
    print("=" * 66)
    print(" Zapatas corridas: el port contra las macros")
    print("=" * 66)

    for f in (v1_constantes, v2_niveles, v3_acomodo, v4_muro, v5_enrase,
              v6_acero_del_muro, v7_sin_duplicar, v8_rotulo_y_muro):
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
