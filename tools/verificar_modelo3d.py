#!/usr/bin/env python3
"""
Comprueba la matriz con que Modelo3dDrawer coloca cada barra en el 3D de AutoCAD.

El perfil se construye plano en el XY, se extruye en +Z, y una matriz lo lleva a su
sitio y a su direccion. Si esa matriz esta mal, el solido sale girado o desplazado y en
un modelo de cientos de barras eso NO se detecta a ojo: se ve un revoltijo y no se sabe
cual de las barras esta mal.

Se comprueba con numeros lo que no se puede comprobar mirando: que el marco sea
ortonormal, que el solido vaya de P1 a P2, y que el alto del perfil quede lo mas
vertical que permita la barra.
"""

import math

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


# ======================================================================
#  Espejo de Modelo3dDrawer.Matriz
# ======================================================================
def matriz(p1, p2):
    largo = math.dist(p1, p2)
    w = [(p2[i] - p1[i]) / largo for i in range(3)]

    # u = Z x w, la perpendicular comun entre la barra y la vertical
    u = [-w[1], w[0], 0.0]
    n = math.hypot(u[0], u[1])

    if n < 1e-9:
        # barra VERTICAL: una columna. El perfil se orienta en planta.
        u = [1.0, 0.0, 0.0]
        v = [0.0, 1.0 if w[2] > 0 else -1.0, 0.0]
    else:
        u = [u[0] / n, u[1] / n, 0.0]
        v = [w[1] * u[2] - w[2] * u[1],
             w[2] * u[0] - w[0] * u[2],
             w[0] * u[1] - w[1] * u[0]]

    return u, v, w, largo


def aplicar(u, v, w, p1, punto):
    """Lleva un punto del perfil (x, y, z) a su sitio, como hace TransformBy."""
    return [p1[i] + u[i] * punto[0] + v[i] * punto[1] + w[i] * punto[2]
            for i in range(3)]


def dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def ortonormal(u, v, w):
    return (abs(math.sqrt(dot(u, u)) - 1) < 1e-12
            and abs(math.sqrt(dot(v, v)) - 1) < 1e-12
            and abs(math.sqrt(dot(w, w)) - 1) < 1e-12
            and abs(dot(u, v)) < 1e-12
            and abs(dot(u, w)) < 1e-12
            and abs(dot(v, w)) < 1e-12)


print("=" * 78)
print(" Colocacion de las barras del modelo 3D")
print("=" * 78)

BARRAS = [
    ("columna de pie",      (0, 0, 0),      (0, 0, 3)),
    ("columna al reves",    (0, 0, 3),      (0, 0, 0)),
    ("trabe en X",          (0, 0, 3),      (6, 0, 3)),
    ("trabe en Y",          (0, 0, 3),      (0, 6, 3)),
    ("trabe girada 30",     (0, 0, 3),
     (6 * math.cos(math.radians(30)), 6 * math.sin(math.radians(30)), 3)),
    ("diagonal 3D",         (0, 0, 0),      (4, 3, 2)),
    ("cuerda de armadura",  (1, 2, 5),      (3, 2, 5.4)),
    ("montante corto",      (2, 2, 5),      (2, 2, 5.6)),
]

print(f"\n  {'barra':<22}{'largo':>8}{'v.Z':>8}")

for nombre, p1, p2 in BARRAS:
    u, v, w, largo = matriz(p1, p2)
    print(f"  {nombre:<22}{largo:>7.3f}{v[2]:>8.3f}")

    check(f"{nombre}: el marco es ortonormal", ortonormal(u, v, w),
          "si no, el solido sale deformado")

    # El solido se extruyo de z=0 a z=largo. Su base tiene que caer en P1 y su tapa en P2.
    base = aplicar(u, v, w, p1, (0, 0, 0))
    tapa = aplicar(u, v, w, p1, (0, 0, largo))

    check(f"{nombre}: la base cae en P1",
          max(abs(base[i] - p1[i]) for i in range(3)) < 1e-12,
          f"{base} contra {list(p1)}")

    check(f"{nombre}: la tapa cae en P2",
          max(abs(tapa[i] - p2[i]) for i in range(3)) < 1e-12,
          f"{tapa} contra {list(p2)}")

    # El perfil tiene que quedar PERPENDICULAR a la barra: un punto del contorno,
    # medido desde el eje, no puede tener componente a lo largo de la barra.
    borde = aplicar(u, v, w, p1, (0.1, 0.05, 0))
    desde = [borde[i] - p1[i] for i in range(3)]

    check(f"{nombre}: el perfil queda perpendicular a la barra",
          abs(dot(desde, w)) < 1e-12,
          f"componente a lo largo del eje: {dot(desde, w):.3e}")

# ======================================================================
#  Que el alto del perfil mire hacia arriba
# ======================================================================
print("\nQue el alto del perfil quede lo mas vertical posible")

# Es lo que hace que una viga salga con el alma de pie y no tumbada al azar. Para una
# barra NO vertical, v tiene que ser lo mas vertical que permita su inclinacion, y eso
# es exactamente sqrt(1 - wz^2).
for nombre, p1, p2 in BARRAS:
    u, v, w, largo = matriz(p1, p2)

    if abs(w[2]) > 1 - 1e-9:
        continue        # las columnas van aparte

    ideal = math.sqrt(1 - w[2] * w[2])
    print(f"  {nombre:<22} v.Z = {v[2]:.6f}   maximo posible = {ideal:.6f}")

    check(f"{nombre}: el alto del perfil mira lo mas arriba que puede",
          abs(abs(v[2]) - ideal) < 1e-12,
          f"{abs(v[2]):.6f} contra {ideal:.6f}")

# La columna es el caso que se anula, y NO es raro: son todas las columnas del modelo.
u, v, w, _ = matriz((0, 0, 0), (0, 0, 3))
check("en la columna de pie el marco no se anula", ortonormal(u, v, w))

u2, v2, w2, _ = matriz((0, 0, 3), (0, 0, 0))
check("y en la columna al reves tampoco", ortonormal(u2, v2, w2))
check("y las dos quedan derechas, no espejadas",
      abs(v[1]) == 1 and abs(v2[1]) == 1)

# ======================================================================
#  Que el volumen no se deforme
# ======================================================================
print("\nQue el solido no se deforme al colocarlo")

# Un marco ortonormal tiene determinante +-1: si no, la matriz escala o espeja, y el
# perfil saldria con otras medidas de las que dice la seccion.
for nombre, p1, p2 in BARRAS:
    u, v, w, _ = matriz(p1, p2)

    det = (u[0] * (v[1] * w[2] - v[2] * w[1])
           - v[0] * (u[1] * w[2] - u[2] * w[1])
           + w[0] * (u[1] * v[2] - u[2] * v[1]))

    check(f"{nombre}: la matriz no escala ni espeja",
          abs(abs(det) - 1) < 1e-12, f"determinante {det:.12f}")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
