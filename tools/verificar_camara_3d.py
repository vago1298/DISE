#!/usr/bin/env python3
"""Comprueba la cámara del 3D de secciones: los tres ejes con los que se coloca.

QUÉ COMPRUEBA Y QUÉ NO

La vista 3D ya no proyecta a mano: la dibuja un Viewport3D y la proyección la hace
el motor. Lo que sigue siendo cuenta nuestra es COLOCAR la cámara, y eso son tres
vectores que AjustarCamara3D calcula a partir del giro y la inclinación:

    mira    = (-sen a·cos e,  -sen e,  -cos a·cos e)
    arriba  = (-sen a·sen e,   cos e,  -cos a·sen e)
    derecha = ( cos a,         0,      -sen a)

Si esos tres no forman una terna a derechas, la imagen sale espejeada o de lado, y
el desplazamiento con el ratón se va en la dirección equivocada. Es lo que se
comprueba aquí, porque en Linux no se puede compilar CadLink.App.

Este fichero comprobaba antes otra cosa: el orden de pintado del algoritmo del
pintor, con una función Cercania que ya NO EXISTE. Se reescribió en lugar de
dejarlo pasando en verde sobre código borrado, que es peor que no tener prueba.

Uso:  python3 tools/verificar_camara_3d.py
"""

from __future__ import annotations

import math
import random
import sys

FALLOS = 0


def comprobar(cond: bool, que: str, porque: str = "") -> None:
    global FALLOS

    if cond:
        print(f"  OK    {que}")
        return

    print(f"  FALLA {que}")
    if porque:
        print(f"        {porque}")
    FALLOS += 1


# El port de AjustarCamara3D, con los ejes de WPF: Y hacia arriba.
def ejes(azimut: float, elevacion: float):
    a = math.radians(azimut)
    e = math.radians(elevacion)

    sa, ca = math.sin(a), math.cos(a)
    se, ce = math.sin(e), math.cos(e)

    mira = (-sa * ce, -se, -ca * ce)
    arriba = (-sa * se, ce, -ca * se)
    derecha = (ca, 0.0, -sa)

    return mira, arriba, derecha


def punto(u, v) -> float:
    return sum(a * b for a, b in zip(u, v))


def cruz(u, v):
    return (
        (u[1] * v[2]) - (u[2] * v[1]),
        (u[2] * v[0]) - (u[0] * v[2]),
        (u[0] * v[1]) - (u[1] * v[0]),
    )


def largo(u) -> float:
    return math.sqrt(punto(u, u))


# ----------------------------------------------------------------------


def ternaOrtonormal() -> None:
    print()
    print("Los tres ejes de la camara")

    peorNorma = 0.0
    peorOrtog = 0.0

    for az in range(0, 360, 11):
        for el in range(-89, 90, 7):
            mira, arriba, derecha = ejes(az, el)

            for v in (mira, arriba, derecha):
                peorNorma = max(peorNorma, abs(largo(v) - 1))

            peorOrtog = max(
                peorOrtog,
                abs(punto(mira, arriba)),
                abs(punto(mira, derecha)),
                abs(punto(arriba, derecha)),
            )

    comprobar(peorNorma < 1e-12, "los tres son unitarios",
              f"el peor se desvia {peorNorma:.3e}")

    comprobar(peorOrtog < 1e-12, "y perpendiculares entre si",
              f"el peor producto punto es {peorOrtog:.3e}")


def aDerechas() -> None:
    """derecha x arriba tiene que SALIR de la pantalla, o sea ser -mira."""
    print()
    print("El sentido de la terna")

    peor = 0.0

    for az in range(0, 360, 11):
        for el in range(-89, 90, 7):
            mira, arriba, derecha = ejes(az, el)

            fuera = cruz(derecha, arriba)

            for i in range(3):
                peor = max(peor, abs(fuera[i] + mira[i]))

    comprobar(peor < 1e-12,
              "derecha x arriba sale de la pantalla: la terna va a derechas",
              f"se desvia {peor:.3e}. Con esto mal, la imagen sale espejeada")


def arribaEsArriba() -> None:
    """El 'arriba' de la pantalla tiene que apuntar al cielo del modelo."""
    print()
    print("Que arriba sea arriba")

    peor = -1.0

    for az in range(0, 360, 11):
        for el in range(-88, 89, 7):
            _, arriba, _ = ejes(az, el)

            # La componente vertical del 'arriba' de la pantalla es cos(e), que es
            # positiva mientras no se mire a plomo. Si saliera negativa, la pieza se
            # veria del reves.
            peor = max(peor, -arriba[1])

    comprobar(peor < 0,
              "la vertical del modelo siempre queda hacia arriba en pantalla",
              f"en algun angulo el arriba apunta al suelo ({peor:.3f})")


def mirarDesdeArriba() -> None:
    print()
    print("Casos que no admiten discusion")

    # Inclinacion 0: se mira en horizontal, asi que la mirada no tiene componente
    # vertical y el arriba de la pantalla es la vertical del mundo.
    mira, arriba, _ = ejes(35, 0)

    comprobar(abs(mira[1]) < 1e-12,
              "mirando en horizontal, la mirada no sube ni baja",
              f"su componente vertical es {mira[1]:.3e}")

    comprobar(abs(arriba[1] - 1) < 1e-12,
              "y el arriba de la pantalla es la vertical del mundo",
              f"vale {arriba[1]:.6f}")

    # Inclinacion 89: casi a plomo. La mirada apunta casi todo hacia abajo.
    mira, arriba, _ = ejes(35, 89)

    comprobar(mira[1] < -0.999,
              "casi a plomo, la mirada va practicamente hacia abajo",
              f"su componente vertical es {mira[1]:.6f}")

    comprobar(abs(arriba[1]) < 0.02,
              "y el arriba de la pantalla queda casi horizontal",
              f"vale {arriba[1]:.6f}")

    # Azimut 0: se mira desde -Z, asi que la derecha de la pantalla es +X.
    _, _, derecha = ejes(0, 22)

    comprobar(abs(derecha[0] - 1) < 1e-12 and abs(derecha[2]) < 1e-12,
              "con giro cero, la derecha de la pantalla es el eje X",
              f"vale {derecha}")


def elGiroRecorreLaVuelta() -> None:
    """Girar 360 grados tiene que volver al mismo sitio, sin saltos."""
    print()
    print("El giro completo")

    rnd = random.Random(5)

    peor = 0.0

    for _ in range(2000):
        el = rnd.uniform(-89, 89)
        az = rnd.uniform(0, 360)

        a = ejes(az, el)
        b = ejes(az + 360, el)

        for i in range(3):
            for j in range(3):
                peor = max(peor, abs(a[i][j] - b[i][j]))

    comprobar(peor < 1e-9,
              "girar una vuelta entera deja la camara igual",
              f"se desvia {peor:.3e}")


def main() -> int:
    print("COMPROBACION DE LA CAMARA DEL 3D DE SECCIONES")
    print("=" * 70)

    ternaOrtonormal()
    aDerechas()
    arribaEsArriba()
    mirarDesdeArriba()
    elGiroRecorreLaVuelta()

    print()
    print("=" * 70)

    if FALLOS:
        print(f"{FALLOS} COMPROBACIONES FALLAN")
        return 1

    print("TODO CORRECTO")
    return 0


if __name__ == "__main__":
    sys.exit(main())
