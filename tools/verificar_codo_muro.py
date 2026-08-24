#!/usr/bin/env python3
"""Comprueba el CONTORNO DE RELLENO de la varilla vertical del muro de concreto.

Por que existe
--------------
El doblez de esa varilla se rellenaba en TRES trozos -el tramo recto, la pata y
el codo- y cada uno cerraba por donde no debia: el del codo cerraba en diagonal,
de una tangencia a la otra. Entre los tres quedaban dos CUNAS sin pintar justo en
la esquina, y en el plano se veia la varilla maciza con un triangulo del color
del concreto metido en el doblez.

Ahora se rellena de una vez, recorriendo el MISMO contorno que se dibuja con
lineas y arcos. Esto de aqui es ese contorno, calculado igual que en
ZapataDrawer.Corrida.cs, y sobre el se comprueba lo que un hatch necesita:

  1. que el poligono sea SIMPLE, o sea que no se cruce consigo mismo -un
     contorno cruzado hace que AutoCAD rellene medio codo, o ninguno-;
  2. que la ESQUINA DEL DOBLEZ quede dentro, que es la cuna que faltaba;
  3. que el eje del tramo recto y el de la pata queden dentro, o sea que el
     contorno cubre la varilla entera y no solo la curva;
  4. y que el area no sea ridicula, que es como se ve un contorno degenerado.

Se prueban los DOS sentidos de doblez, los DOS juegos de radios -la macro central
usa Ø/4 y Ø/2, la de lindero Ø y 2Ø- y cuatro diametros.
"""
import math
import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
FUENTE = RAIZ / "client/src/CadLink.Cad/ZapataDrawer.Corrida.cs"

SEGMENTOS = 10


def contorno(s, d, lindero, ytop=1.0, bx=0.0, yesq=0.0, largo_pata=0.15):
    """El mismo contorno que arma RellenarVarillaDelMuro."""
    mitad = d / 2
    rin = d if lindero else d / 4
    rout = 2 * d if lindero else d / 2

    cxin, cyin = bx + s * (mitad + rin), yesq + mitad + rin
    cxout, cyout = bx - s * (mitad - rout), yesq - mitad + rout

    xdentro, xfuera = bx + s * mitad, bx - s * mitad
    xfin = bx + s * largo_pata
    ypata_bot, ypata_top = yesq - mitad, yesq + mitad

    def angulo(cierre):
        if s < 0:
            return 2 * math.pi if cierre else 3 * math.pi / 2
        return 3 * math.pi / 2 if cierre else math.pi

    # El extremo del barrido por lo que ES: la tangencia con el tramo recto y la
    # tangencia con la pata cambian de numero segun el sentido del doblez.
    a_recto, a_pata = angulo(s < 0), angulo(s > 0)

    pts = [(xdentro, ytop), (xdentro, cyin)]

    for i in range(SEGMENTOS + 1):
        a = a_recto + (a_pata - a_recto) * i / SEGMENTOS
        pts.append((cxin + rin * math.cos(a), cyin + rin * math.sin(a)))

    pts += [(xfin, ypata_top), (xfin, ypata_bot)]

    for i in range(SEGMENTOS + 1):
        a = a_pata + (a_recto - a_pata) * i / SEGMENTOS
        pts.append((cxout + rout * math.cos(a), cyout + rout * math.sin(a)))

    pts.append((xfuera, ytop))

    datos = dict(cxin=cxin, cyin=cyin, rin=rin, mitad=mitad, bx=bx, yesq=yesq)
    return pts, datos


def _orienta(a, b, c):
    v = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    return 0 if abs(v) < 1e-15 else (1 if v > 0 else -1)


def se_cruzan(p, q, r, t):
    return (_orienta(p, q, r) * _orienta(p, q, t) < 0
            and _orienta(r, t, p) * _orienta(r, t, q) < 0)


def es_simple(pts):
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        for j in range(i + 1, n):
            if j == i or (j + 1) % n == i or j == (i + 1) % n:
                continue
            if se_cruzan(a, b, pts[j], pts[(j + 1) % n]):
                return False
    return True


def esta_dentro(pts, x, y):
    dentro = False
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        if (y1 > y) != (y2 > y):
            if x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
                dentro = not dentro
    return dentro


def area(pts):
    s = 0.0
    for i in range(len(pts)):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % len(pts)]
        s += x1 * y2 - x2 * y1
    return abs(s / 2)


FALLOS = []


def check(que, ok):
    print(f"  {'OK   ' if ok else 'FALLA'} {que}")
    if not ok:
        FALLOS.append(que)


def main():
    print("=" * 66)
    print(" El contorno de relleno de la varilla del muro de concreto")
    print("=" * 66)

    fuente = FUENTE.read_text(encoding="utf-8")

    # 0) Que el dibujante siga rellenando de UNA vez y no por trozos.
    check("se rellena con un solo contorno, no en tres trozos",
          "private void RellenarVarillaDelMuro(" in fuente
          and "RellenarTramoDeVarilla(" not in fuente
          and "RellenarCodoDeVarilla(" not in fuente)
    check("y ese contorno lleva los dos arcos y las dos caras",
          "El arco INTERIOR" in fuente and "El arco EXTERIOR" in fuente
          and "La cara de DENTRO" in fuente and "la cara de FUERA" in fuente)
    check("los extremos del barrido se toman por lo que son, no por su numero",
          "var angRecto = AnguloCodo(sentido, sentido < 0);" in fuente
          and "var angPata = AnguloCodo(sentido, sentido > 0);" in fuente)
    check("y los diez segmentos por arco siguen ahi",
          re.search(r"const int segmentos = 10;", fuente) is not None)

    # 1..4) La geometria, sentido por sentido y diametro por diametro.
    for s, comos in ((-1, "doblando a la izquierda"), (1, "doblando a la derecha")):
        for lindero, macro in ((False, "central"), (True, "lindero")):
            malos = []

            for d in (0.00635, 0.009525, 0.0127, 0.0381):
                pts, g = contorno(s, d, lindero)

                if not es_simple(pts):
                    malos.append(f"contorno cruzado con d={d}")

                # La esquina del doblez: sobre la bisectriz, a media varilla del
                # centro del arco interior. Es la cuna que antes quedaba hueca.
                grados = 225 if s < 0 else 315
                ex = g["cxin"] + (g["rin"] + g["mitad"]) * math.cos(math.radians(grados))
                ey = g["cyin"] + (g["rin"] + g["mitad"]) * math.sin(math.radians(grados))

                if not esta_dentro(pts, ex, ey):
                    malos.append(f"la esquina del doblez queda fuera con d={d}")

                if not esta_dentro(pts, g["bx"], g["yesq"] + 0.3):
                    malos.append(f"el tramo recto queda fuera con d={d}")

                if not esta_dentro(pts, g["bx"] + s * 0.10, g["yesq"]):
                    malos.append(f"la pata queda fuera con d={d}")

                if area(pts) < d * 0.1:
                    malos.append(f"area degenerada con d={d}")

            check(f"{comos}, con los radios de la macro {macro}, el relleno cubre "
                  "la varilla entera", not malos)

            for m in malos:
                print(f"          -> {m}")

    print()
    print("=" * 66)
    if FALLOS:
        print(f" RESULTADO: {len(FALLOS)} comprobacion(es) fallaron")
        print("=" * 66)
        return 1

    print(" RESULTADO: todo bien")
    print("=" * 66)
    return 0


if __name__ == "__main__":
    sys.exit(main())
