#!/usr/bin/env python3
"""
Comprueba los contornos de Perfil2D: son la geometria que comparten la vista extruida
de la ventana y el dibujo 3D en AutoCAD.

Vive aparte porque un contorno mal construido no se ve en el codigo y falla distinto en
cada consumidor: en WPF sale un relleno con agujeros, y en AutoCAD un solido invalido
que la extrusion rechaza sin decir por que. Aqui se caza antes.

Ya cazo uno: el perfil T tenia los vertices del patin mezclados con los del alma y el
contorno se cruzaba consigo mismo.
"""

import math

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


# ======================================================================
#  Espejo de Perfil2D
# ======================================================================
LADOS = 24
FRAC = 0.45          # FraccionMaxima de Perfil2D


def tope(v, mx):
    return mx * 0.2 if v <= 0 else min(v, mx)


def rectangulo(b, h):
    x, y = b / 2, h / 2
    return list(zip([-x, x, x, -x], [-y, -y, y, y]))


def circulo(r, n=LADOS):
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def perfil_i(b, h, tf, tw):
    x, y = b / 2, h / 2
    f = tope(tf, h * FRAC)
    w = tope(tw, b * FRAC) / 2
    return list(zip(
        [-x, x, x, w, w, x, x, -x, -x, -w, -w, -x],
        [-y, -y, -y + f, -y + f, y - f, y - f, y, y, y - f, y - f, -y + f, -y + f]))


def perfil_t(b, h, tf, tw):
    x, y = b / 2, h / 2
    f = tope(tf, h * FRAC)
    w = tope(tw, b * FRAC) / 2
    return list(zip(
        [-w, w, w, x, x, -x, -x, -w],
        [-y, -y, y - f, y - f, y, y, y - f, y - f]))


def canal(b, h, tf, tw):
    x, y = b / 2, h / 2
    f = tope(tf, h * FRAC)
    w = tope(tw, b * FRAC)
    return list(zip(
        [-x, x, x, -x + w, -x + w, x, x, -x],
        [-y, -y, -y + f, -y + f, y - f, y - f, y, y]))


def angulo(b, h, tf, tw):
    x, y = b / 2, h / 2
    ef = tope(tf, h * FRAC)
    ew = tope(tw, b * FRAC)
    return list(zip(
        [-x, x, x, -x + ew, -x + ew, -x],
        [-y, -y, -y + ef, -y + ef, y, y]))


# ======================================================================
#  Utilidades
# ======================================================================
def area(p):
    n = len(p)
    return abs(sum(p[i][0] * p[(i + 1) % n][1] - p[(i + 1) % n][0] * p[i][1]
                   for i in range(n))) / 2


def caja(p):
    xs = [q[0] for q in p]
    ys = [q[1] for q in p]
    return max(xs) - min(xs), max(ys) - min(ys)


def centrado(p):
    xs = [q[0] for q in p]
    ys = [q[1] for q in p]
    return abs(max(xs) + min(xs)) < 1e-9 and abs(max(ys) + min(ys)) < 1e-9


def cruces(p):
    """Cuantos pares de lados NO adyacentes se cortan. Tiene que ser cero."""
    def orient(a, b, c):
        v = (b[1] - a[1]) * (c[0] - b[0]) - (b[0] - a[0]) * (c[1] - b[1])
        return 0 if abs(v) < 1e-15 else (1 if v > 0 else 2)

    def se_cortan(a, b, c, d):
        return orient(a, b, c) != orient(a, b, d) and orient(c, d, a) != orient(c, d, b)

    n = len(p)
    total = 0
    for i in range(n):
        for j in range(i + 2, n):
            if i == 0 and j == n - 1:
                continue
            if se_cortan(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n]):
                total += 1
    return total


print("=" * 78)
print(" Contornos de Perfil2D: los que comparten el visor y el 3D de AutoCAD")
print("=" * 78)

PERFILES = [
    # nombre                contorno                                 ancho  alto
    ("RECT 30x60",          rectangulo(0.30, 0.60),                   0.30, 0.60),
    ("CIRC D=20",           circulo(0.10),                            0.20, 0.20),
    ("I  254x146",          perfil_i(0.146, 0.254, 0.0107, 0.0072),   0.146, 0.254),
    ("T  200x100",          perfil_t(0.100, 0.200, 0.010, 0.007),     0.100, 0.200),
    ("C  200x76",           canal(0.076, 0.200, 0.010, 0.007),        0.076, 0.200),
    ("L  75x75x6",          angulo(0.075, 0.075, 0.006, 0.006),       0.075, 0.075),
]

print("\nMedidas y sanidad de cada contorno")
print(f"  {'perfil':<14}{'vert':>6}{'ancho':>9}{'alto':>9}{'area cm2':>11}")

for nombre, p, b, h in PERFILES:
    w, a = caja(p)
    print(f"  {nombre:<14}{len(p):>6}{w*100:>8.2f}{a*100:>9.2f}{area(p)*1e4:>11.2f}")

    # La caja envolvente tiene que ser EXACTAMENTE el ancho y el peralte pedidos: si no,
    # el perfil no encaja donde el modelo dice que encaja.
    check(f"{nombre}: la caja envolvente es el ancho y el peralte pedidos",
          abs(w - b) < 1e-12 and abs(a - h) < 1e-12,
          f"caja {w:.6f}x{a:.6f}, pedido {b:.6f}x{h:.6f}")

    check(f"{nombre}: va centrado en el origen", centrado(p))
    check(f"{nombre}: encierra area", area(p) > 0)

    # LO IMPORTANTE: un contorno que se cruza da un solido invalido en AutoCAD y un
    # relleno con agujeros en WPF.
    check(f"{nombre}: no se cruza consigo mismo", cruces(p) == 0,
          f"{cruces(p)} cruces entre lados")

# ======================================================================
#  Que los perfiles NO sean cajas: es el motivo de todo esto
# ======================================================================
print("\nQue el perfil no sea una caja")

for nombre, p, b, h in PERFILES:
    if nombre.startswith("RECT"):
        continue

    frac = area(p) / (b * h)
    print(f"  {nombre:<14} ocupa el {100*frac:5.1f} % de su caja")

    check(f"{nombre}: ocupa bastante menos que su caja",
          frac < 0.85, f"{100*frac:.1f} %")

# El rectangulo SI tiene que llenar su caja, que es la comprobacion de que la medida
# no se esta encogiendo por el camino.
r = rectangulo(0.30, 0.60)
check("el rectangulo si llena su caja", abs(area(r) - 0.30 * 0.60) < 1e-12)

# ======================================================================
#  Contra tablas de perfiles reales
# ======================================================================
print("\nContra el area de tabla de un perfil real")

# IPR 254x146x43: 54.8 cm2 de tabla
a_i = area(perfil_i(0.146, 0.254, 0.0107, 0.0072)) * 1e4
print(f"  IPR 254x146x43: tabla 54.8 cm2, contorno {a_i:.1f} cm2 "
      f"({100*(54.8-a_i)/54.8:.0f} % menos)")

# Sale algo menos, y es CORRECTO: el perfil real lleva curvas de acuerdo entre el alma y
# los patines que añaden area, y el contorno las dibuja como esquinas vivas. Lo que no
# puede pasar es que se aleje mucho.
check("el contorno de la I se acerca al area de tabla",
      40 < a_i < 54.8, f"{a_i:.1f} cm2")

check("y queda por DEBAJO, que es lo que toca sin las curvas de acuerdo",
      a_i < 54.8)

# ======================================================================
#  Los espesores que no vienen se estiman, y no pueden romper nada
# ======================================================================
print("\nCuando el modelo no da los espesores")

# Es el caso de una seccion capturada a medias: Perfil2D los estima con proporciones de
# laminado corriente en vez de rendirse y dibujar una caja.
b, h = 0.146, 0.254
tf_est, tw_est = h * 0.08, b * 0.06
p = perfil_i(b, h, tf_est, tw_est)
print(f"  patin estimado {tf_est*1000:.1f} mm, alma {tw_est*1000:.1f} mm")

check("con espesores estimados el perfil sigue siendo valido",
      cruces(p) == 0 and area(p) > 0 and centrado(p))
check("y sigue sin parecer una caja", area(p) / (b * h) < 0.85)

# Y un espesor absurdo tampoco: un patin mayor que medio peralte se recorta.
p = perfil_i(b, h, h, b)
check("un espesor absurdo se recorta y no revienta el contorno",
      cruces(p) == 0 and area(p) > 0,
      f"{cruces(p)} cruces, area {area(p)*1e4:.2f} cm2")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
