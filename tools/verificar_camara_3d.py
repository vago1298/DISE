#!/usr/bin/env python3
"""Comprueba la cámara del 3D de secciones: la proyección y el orden de pintado.

¿Por qué en Python y no en una prueba ejecutable?

La cámara vive en MainWindow.Seccion3D.cs, o sea en CadLink.App, que en este
entorno NO se puede compilar: falta el ref pack de WPF. Pero la cuenta es
aritmética pura y se puede portar. Lo que NO comprueba esto es que el C# diga lo
mismo que este port; eso hay que leerlo. Lo que sí comprueba es que la CUENTA sea
la correcta, que es donde estaba el error.

EL ERROR QUE ESTO CAZA. El orden de pintado se hacía con `Prof(x, y)`, ordenando
de menor a mayor. Dos cosas mal:

  1. Prof crece hacia el FONDO, así que ordenar de menor a mayor pinta lo
     cercano primero y lo lejano encima: justo al revés. El visor de ETABS, que
     usa la misma proyección, ordena con OrderByDescending.

  2. Prof ignora la ALTURA. En una pieza levantada tres metros con una sección de
     medio, mirándola desde 22° el término de la altura barre más de un metro y el
     de la planta apenas medio: manda el que se estaba ignorando. Por eso el
     armado se veía entremezclado y las barras parecían traspasarse.

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


# ----------------------------------------------------------------------
#  El port de la cámara, tal como está en MainWindow.Seccion3D.cs
# ----------------------------------------------------------------------


class Camara:
    def __init__(self, azimut: float, elevacion: float) -> None:
        a = math.radians(azimut)
        e = math.radians(elevacion)

        self.sa, self.ca = math.sin(a), math.cos(a)
        self.se, self.ce = math.sin(e), math.cos(e)

    def proyectar(self, x: float, y: float, z: float) -> tuple[float, float]:
        d = (x * self.sa) + (y * self.ca)

        return ((x * self.ca) - (y * self.sa), -((z * self.ce) + (d * self.se)))

    def prof(self, x: float, y: float) -> float:
        return (x * self.sa) + (y * self.ca)

    def cercania(self, x: float, y: float, z: float) -> float:
        """Lo cerca del ojo. Cuanto mayor, más cerca."""
        return (z * self.se) - (self.ce * self.prof(x, y))

    # Los tres ejes de la pantalla, de los que TIENE que salir todo lo de arriba.
    def derecha(self) -> tuple[float, float, float]:
        return (self.ca, -self.sa, 0.0)

    def arriba(self) -> tuple[float, float, float]:
        return (self.sa * self.se, self.ca * self.se, self.ce)

    def hacia_el_ojo(self) -> tuple[float, float, float]:
        """Derecha × Arriba, que en una terna a derechas sale de la pantalla."""
        r = self.derecha()
        u = self.arriba()

        return (
            (r[1] * u[2]) - (r[2] * u[1]),
            (r[2] * u[0]) - (r[0] * u[2]),
            (r[0] * u[1]) - (r[1] * u[0]),
        )


def punto(v, w) -> float:
    return sum(a * b for a, b in zip(v, w))


def largo(v) -> float:
    return math.sqrt(punto(v, v))


# ----------------------------------------------------------------------


def ternas_ortonormales() -> None:
    print()
    print("Los tres ejes de la pantalla")

    peor_norma = 0.0
    peor_ortog = 0.0

    for az in range(0, 360, 17):
        for el in range(-89, 90, 13):
            c = Camara(az, el)

            r, u, o = c.derecha(), c.arriba(), c.hacia_el_ojo()

            for v in (r, u, o):
                peor_norma = max(peor_norma, abs(largo(v) - 1))

            peor_ortog = max(
                peor_ortog,
                abs(punto(r, u)),
                abs(punto(r, o)),
                abs(punto(u, o)),
            )

    comprobar(peor_norma < 1e-12, "los tres son unitarios",
              f"el peor se desvia {peor_norma:.3e}")

    comprobar(peor_ortog < 1e-12, "y perpendiculares entre si",
              f"el peor producto punto es {peor_ortog:.3e}")


def la_proyeccion_usa_esos_ejes() -> None:
    print()
    print("La proyeccion sale de esos ejes")

    peor_u = 0.0
    peor_v = 0.0

    rnd = random.Random(7)

    for _ in range(4000):
        c = Camara(rnd.uniform(0, 360), rnd.uniform(-89, 89))
        p = (rnd.uniform(-300, 300), rnd.uniform(-300, 300), rnd.uniform(-300, 300))

        u, v = c.proyectar(*p)

        peor_u = max(peor_u, abs(u - punto(p, c.derecha())))
        # En un lienzo la v crece hacia ABAJO, de ahi el signo.
        peor_v = max(peor_v, abs(v + punto(p, c.arriba())))

    comprobar(peor_u < 1e-9, "la u es la proyeccion sobre el eje DERECHA",
              f"se desvia {peor_u:.3e}")

    comprobar(peor_v < 1e-9, "y la v es menos la proyeccion sobre ARRIBA",
              f"se desvia {peor_v:.3e}")


def la_cercania_es_el_eje_que_sale() -> None:
    print()
    print("La cercania")

    peor = 0.0

    rnd = random.Random(11)

    for _ in range(4000):
        c = Camara(rnd.uniform(0, 360), rnd.uniform(-89, 89))
        p = (rnd.uniform(-300, 300), rnd.uniform(-300, 300), rnd.uniform(-300, 300))

        peor = max(peor, abs(c.cercania(*p) - punto(p, c.hacia_el_ojo())))

    comprobar(peor < 1e-9,
              "es exactamente la proyeccion sobre el eje que SALE de la pantalla",
              f"se desvia {peor:.3e}")


def casos_sin_ambiguedad() -> None:
    """Casos donde se sabe a ojo quien tapa a quien."""
    print()
    print("Casos que no admiten discusion")

    # Mirando de frente (azimut 0, elevacion 0): el ojo esta en y = -infinito,
    # asi que la y pequena esta mas cerca.
    c = Camara(0, 0)

    comprobar(c.cercania(0, 0, 0) > c.cercania(0, 10, 0),
              "de frente, lo de y menor esta mas cerca",
              f"{c.cercania(0, 0, 0):.3f} contra {c.cercania(0, 10, 0):.3f}")

    # Los dos caen en el MISMO punto de pantalla, asi que uno tapa al otro de
    # verdad: es el caso critico.
    a = c.proyectar(0, 0, 0)
    b = c.proyectar(0, 10, 0)

    comprobar(abs(a[0] - b[0]) < 1e-12 and abs(a[1] - b[1]) < 1e-12,
              "y los dos caen en el mismo punto de pantalla",
              f"{a} contra {b}")

    # Mirando desde arriba (elevacion 90): lo mas alto esta mas cerca.
    c = Camara(0, 90)

    comprobar(c.cercania(0, 0, 300) > c.cercania(0, 0, 0),
              "desde arriba, lo mas alto esta mas cerca",
              f"{c.cercania(0, 0, 300):.3f} contra {c.cercania(0, 0, 0):.3f}")

    a = c.proyectar(0, 0, 300)
    b = c.proyectar(0, 0, 0)

    comprobar(abs(a[0] - b[0]) < 1e-12 and abs(a[1] - b[1]) < 1e-12,
              "y tambien caen en el mismo punto de pantalla",
              f"{a} contra {b}")


def el_orden_viejo_estaba_al_reves() -> None:
    """La comprobacion que le da sentido al arreglo."""
    print()
    print("Lo que hacia el orden viejo")

    c = Camara(0, 0)

    cerca = (0.0, 0.0, 0.0)
    lejos = (0.0, 10.0, 0.0)

    # El orden viejo: de menor a mayor Prof, y se pinta en ese orden.
    viejo = sorted([cerca, lejos], key=lambda p: c.prof(p[0], p[1]))

    # Lo ULTIMO que se pinta queda encima.
    comprobar(viejo[-1] == lejos,
              "ordenando por Prof de menor a mayor, lo ULTIMO que se pinta es lo LEJANO",
              "no reproduce el error, asi que esta comprobacion no vale")

    # El orden nuevo: de menor a mayor cercania.
    nuevo = sorted([cerca, lejos], key=lambda p: c.cercania(*p))

    comprobar(nuevo[-1] == cerca,
              "y ordenando por cercania, lo ultimo es lo CERCANO",
              f"quedo {nuevo[-1]} encima")


def la_altura_manda_en_una_pieza_alta() -> None:
    """Por que no basta con Prof, aunque se ordene bien."""
    print()
    print("Por que no basta con Prof")

    # Una columna de 40 x 60 levantada 3 m, mirada desde 22 grados, que es la
    # orientacion de arranque del 3D.
    c = Camara(35, 22)

    bx, by, bz = 40.0, 60.0, 300.0

    rango_planta = max(
        abs(c.ce * (c.prof(x, y) - c.prof(x2, y2)))
        for x, y in ((0, 0), (bx, 0), (bx, by), (0, by))
        for x2, y2 in ((0, 0), (bx, 0), (bx, by), (0, by))
    )

    rango_altura = abs(bz * c.se)

    print(f"        el termino de la planta barre {rango_planta:.1f} cm")
    print(f"        el de la altura barre        {rango_altura:.1f} cm")

    comprobar(rango_altura > rango_planta,
              "en una pieza alta el termino de la ALTURA es el que manda",
              "no manda, asi que este caso no justifica el arreglo")

    # Y el caso concreto: dos barras que caen encima en pantalla y que Prof no
    # sabe separar porque comparten planta.
    abajo = (bx / 2, by / 2, 0.0)
    arriba = (bx / 2, by / 2, bz)

    comprobar(abs(c.prof(abajo[0], abajo[1]) - c.prof(arriba[0], arriba[1])) < 1e-12,
              "dos puntos en la misma vertical tienen la MISMA Prof",
              "no la tienen")

    comprobar(c.cercania(*arriba) > c.cercania(*abajo),
              "pero la cercania si los separa, y pone arriba el de mas cota",
              f"{c.cercania(*arriba):.3f} contra {c.cercania(*abajo):.3f}")


def main() -> int:
    print("COMPROBACION DE LA CAMARA DEL 3D DE SECCIONES")
    print("=" * 70)

    ternas_ortonormales()
    la_proyeccion_usa_esos_ejes()
    la_cercania_es_el_eje_que_sale()
    casos_sin_ambiguedad()
    el_orden_viejo_estaba_al_reves()
    la_altura_manda_en_una_pieza_alta()

    print()
    print("=" * 70)

    if FALLOS:
        print(f"{FALLOS} COMPROBACIONES FALLAN")
        return 1

    print("TODO CORRECTO")
    return 0


if __name__ == "__main__":
    sys.exit(main())
