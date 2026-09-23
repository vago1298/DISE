#!/usr/bin/env python3
"""
Dibuja en un DXF las cuatro formas posibles del enroscado, una al lado de la otra.

    python tools/variantes_de_rosca.py

Sale docs/variantes-de-rosca.dxf, que se abre en AutoCAD y se mira. Existe por un motivo
concreto: la forma del hilo se pidio tres veces con capturas de pantalla y las tres veces
se leyo mal -el paso, el desfase de las hebras, donde caen los picos-, porque en una
captura de 300 pixeles no se distingue si dos vertices estan a la misma altura o a media.

Asi que en lugar de seguir adivinando, se dibujan las cuatro y se pregunta cual. Cada una
es un cambio de una linea en ElevacionPlacaBase.Roscar.

Las cuatro, todas con el mismo paso y el mismo ancho -lo unico que cambia es COMO se
reparten las hebras-:

    A  una sola hebra en zigzag. Picos alternados, sin cruces.
    B  dos hebras espejadas: cuando una pica a la izquierda la otra pica a la derecha A LA
       MISMA ALTURA. Da equis simetricas. Es lo que se dibujaba al principio.
    C  dos hebras desfasadas MEDIO diente. Los picos de un flanco caen entre los del otro.
       Es lo que se dibuja ahora.
    D  dos hebras, cada una cruzando el ancho en DOS dientes. Diagonales largas y tumbadas.

El DXF va en R12 ASCII a proposito: lo abre cualquier version de AutoCAD y de cualquier
programa de CAD, y se puede leer con un editor de texto para comprobarlo.
"""

from __future__ import annotations

import math
import os

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SALIDA = os.path.join(RAIZ, "docs", "variantes-de-rosca.dxf")

#  El ancla del caso real: 3/8", con la rosca que asoma y el paso que se dibuja hoy.
D = 0.375 * 2.54                     # diametro, en cm
LARGO = max(2.5 * D, 1.5)            # lo que asoma, en cm
PASO = 0.15 * D                      # el paso del hilo, en cm
DIENTES = max(2, math.ceil(LARGO / PASO))
H = LARGO / DIENTES                  # la altura de un diente, ya repartida

SEPARACION = 6 * D                   # entre una variante y la siguiente


def flancos(x):
    """Las dos caras de la barra y el remate de la punta."""
    izq, der = x - D / 2, x + D / 2

    return [
        (izq, 0.0, izq, LARGO),
        (der, 0.0, der, LARGO),
        (izq, LARGO, der, LARGO),
    ]


def hebra(x, arranca_izquierda, desfase, dientes_por_cruce=1):
    """Una hebra en zigzag.

    arranca_izquierda: en que flanco pica el primer vertice.
    desfase: cuanto se sube la hebra entera, en dientes.
    dientes_por_cruce: cuantos dientes tarda en cruzar el ancho. Con 2, la diagonal es el
    doble de tumbada y cruza dos veces a la de al lado.
    """
    izq, der = x - D / 2, x + D / 2
    puntos = []
    i = 0

    while True:
        y = (i * dientes_por_cruce + desfase) * H

        if y > LARGO + 1e-9:
            break

        puntos.append((izq if (i % 2 == 0) == arranca_izquierda else der, y))
        i += 1

    return [(puntos[k][0], puntos[k][1], puntos[k + 1][0], puntos[k + 1][1])
            for k in range(len(puntos) - 1)]


def variante(letra, x):
    """Las lineas de una variante, ya colocadas en su sitio."""
    lineas = list(flancos(x))

    if letra == "A":
        lineas += hebra(x, True, 0)
    elif letra == "B":
        lineas += hebra(x, True, 0) + hebra(x, False, 0)
    elif letra == "C":
        lineas += hebra(x, True, 0) + hebra(x, False, 0.5)
    elif letra == "D":
        lineas += hebra(x, True, 0, 2) + hebra(x, False, 1, 2)

    return lineas


def dxf(lineas, textos):
    """Un DXF R12 ASCII con lineas y textos. Sin capas ni estilos: no hacen falta."""
    t = ["0", "SECTION", "2", "ENTITIES"]

    for x1, y1, x2, y2 in lineas:
        t += ["0", "LINE", "8", "0",
              "10", f"{x1:.6f}", "20", f"{y1:.6f}", "30", "0.0",
              "11", f"{x2:.6f}", "21", f"{y2:.6f}", "31", "0.0"]

    for s, x, y, h in textos:
        t += ["0", "TEXT", "8", "0",
              "10", f"{x:.6f}", "20", f"{y:.6f}", "30", "0.0",
              "40", f"{h:.6f}", "1", s]

    t += ["0", "ENDSEC", "0", "EOF"]

    return "\n".join(t) + "\n"


def main():
    lineas = []
    textos = []

    rotulos = {
        "A": "A  una hebra, sin cruces",
        "B": "B  dos hebras espejadas (lo del principio)",
        "C": "C  dos hebras, medio diente (lo de ahora)",
        "D": "D  dos hebras, diagonal de dos dientes",
    }

    for n, letra in enumerate("ABCD"):
        x = n * SEPARACION

        lineas += variante(letra, x)

        #  La letra, grande y debajo de cada tornillo.
        textos.append((letra, x - D / 2, -1.2 * D, 0.9 * D))

        #  Y el nombre, tumbado a la derecha de todo para que no se pisen entre si.
        textos.append((rotulos[letra], 4 * SEPARACION, LARGO - (n * 1.3 * D), 0.35 * D))

    textos.append((
        f'ancla 3/8"   rosca {LARGO:.2f} cm   {DIENTES} dientes   paso {PASO:.3f} cm',
        0.0, -2.6 * D, 0.35 * D))

    os.makedirs(os.path.dirname(SALIDA), exist_ok=True)

    with open(SALIDA, "w", encoding="ascii") as f:
        f.write(dxf(lineas, textos))

    print(f"{len(lineas)} lineas y {len(textos)} textos en {SALIDA}")
    print(f"  ancla 3/8\": rosca de {LARGO:.2f} cm, {DIENTES} dientes, paso {PASO:.3f} cm")

    for letra in "ABCD":
        print(f"  variante {letra}: {len(variante(letra, 0)) - 3} lineas de hilo")


if __name__ == "__main__":
    main()
