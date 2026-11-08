#!/usr/bin/env python3
"""Los iconos de la cinta de CadLink en Revit, como los pidio el usuario: piezas en
isometrico, en azules, cada boton con el SUYO (columna, viga, corte, ETABS...).

    python tools/make_iconos_cinta.py

Sin dependencias: un rasterizador de poligonos con supermuestreo y el PNG de make_icon.py.
Cada icono se dibuja en un lienzo de 32 x 32 unidades y se rasteriza en 32 y en 16 px.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

AQUI = Path(__file__).resolve().parent
sys.path.insert(0, str(AQUI))

from make_icon import como_png  # noqa: E402

DESTINO = AQUI.parent / "client" / "src" / "CadLink.Revit" / "Assets"

# Los azules de la cinta: cara de arriba clara, izquierda media, derecha oscura.
ARRIBA = (150, 205, 245, 255)
IZQ = (55, 135, 210, 255)
DER = (25, 85, 165, 255)
BORDE = (225, 240, 255, 255)
VARILLA = (235, 245, 255, 255)
FLECHA = (40, 205, 225, 255)
FLECHA_BORDE = (10, 70, 95, 255)
ROJO = (225, 60, 60, 255)
GRIS = (200, 205, 212, 255)
OSCURO = (35, 45, 60, 255)

COS = math.cos(math.radians(30))
SEN = math.sin(math.radians(30))


def iso(x, y, z, cx=16.0, cy=16.0, s=1.0):
    """Un punto 3D al lienzo: X a la derecha-abajo, Y a la izquierda-abajo, Z arriba."""
    return (cx + (x - y) * COS * s, cy + (x + y) * SEN * s - z * s)


def caja(x, y, z, dx, dy, dz, cx=16, cy=16, s=1.0, colores=(ARRIBA, IZQ, DER)):
    """Un prisma en isometrico: las tres caras visibles y su contorno."""
    p = lambda a, b, c: iso(a, b, c, cx, cy, s)  # noqa: E731
    arriba = [p(x, y, z + dz), p(x + dx, y, z + dz), p(x + dx, y + dy, z + dz), p(x, y + dy, z + dz)]
    izq = [p(x, y + dy, z), p(x + dx, y + dy, z), p(x + dx, y + dy, z + dz), p(x, y + dy, z + dz)]
    der = [p(x + dx, y, z), p(x + dx, y + dy, z), p(x + dx, y + dy, z + dz), p(x + dx, y, z + dz)]
    formas = [(arriba, colores[0]), (izq, colores[1]), (der, colores[2])]
    for cara in (arriba, izq, der):
        formas += lineas(cara + [cara[0]], 0.7, BORDE)
    return formas


def linea(a, b, g, color):
    """Un trazo de grosor g como cuadrilatero."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    l = math.hypot(dx, dy) or 1
    nx, ny = -dy / l * g / 2, dx / l * g / 2
    return ([(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny), (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)], color)


def lineas(pts, g, color):
    return [linea(pts[i], pts[i + 1], g, color) for i in range(len(pts) - 1)]


def circulo(cx, cy, r, color, n=20):
    return ([(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)], color)


def flecha(x, y, largo, hacia, g=3.2):
    """Una flecha gruesa con borde: hacia 'abajo' o 'derecha'."""
    if hacia == "abajo":
        cuerpo = [(x - g / 2, y), (x + g / 2, y), (x + g / 2, y + largo - 3.5), (x - g / 2, y + largo - 3.5)]
        punta = [(x - 4, y + largo - 4), (x + 4, y + largo - 4), (x, y + largo)]
    else:
        cuerpo = [(x, y - g / 2), (x + largo - 3.5, y - g / 2), (x + largo - 3.5, y + g / 2), (x, y + g / 2)]
        punta = [(x + largo - 4, y - 4), (x + largo - 4, y + 4), (x + largo, y)]
    formas = []
    for f in (cuerpo, punta):
        formas += lineas(f + [f[0]], 1.4, FLECHA_BORDE)
    return formas + [(cuerpo, FLECHA), (punta, FLECHA)]


# ---------------------------------------------------------------- los iconos

def columna_acero():
    f = caja(0, 0, 0, 7, 7, 20, cx=16, cy=24, s=1.0)
    # Las varillas: lineas verticales claras en las dos caras.
    for t in (0.25, 0.75):
        a = iso(7 * t, 7, 1.5, 16, 24)
        b = iso(7 * t, 7, 18.5, 16, 24)
        f.append(linea(a, b, 0.9, VARILLA))
        a = iso(7, 7 * t, 1.5, 16, 24)
        b = iso(7, 7 * t, 18.5, 16, 24)
        f.append(linea(a, b, 0.9, VARILLA))
    # Los estribos: anillos rojos.
    for z in (5, 10, 15):
        anillo = [iso(0, 7, z, 16, 24), iso(7, 7, z, 16, 24), iso(7, 0, z, 16, 24)]
        f += lineas(anillo, 0.9, ROJO)
    return f


def seccion(cx, cy, lado):
    """La seccion armada vista de frente: concreto, estribo rojo y cuatro varillas."""
    h = lado / 2
    f = [([(cx - h, cy - h), (cx + h, cy - h), (cx + h, cy + h), (cx - h, cy + h)], GRIS)]
    f += lineas([(cx - h, cy - h), (cx + h, cy - h), (cx + h, cy + h), (cx - h, cy + h), (cx - h, cy - h)], 1.0, OSCURO)
    e = h * 0.62
    f += lineas([(cx - e, cy - e), (cx + e, cy - e), (cx + e, cy + e), (cx - e, cy + e), (cx - e, cy - e)], 1.3, ROJO)
    for vx in (-1, 1):
        for vy in (-1, 1):
            f.append(circulo(cx + vx * (e - 1.4), cy + vy * (e - 1.4), 1.5, OSCURO))
    return f


def columna_corte():
    f = caja(0, 0, 0, 6, 6, 18, cx=10, cy=26, s=1.0)
    # El plano de corte, y la seccion que sale de el.
    f += lineas([iso(-3, 9, 9, 10, 26), iso(9, 9, 9, 10, 26), iso(9, -3, 9, 10, 26)], 1.0, FLECHA)
    return f + seccion(23, 11, 15)


def viga_acero():
    f = caja(0, 0, 0, 26, 6, 8, cx=4, cy=10, s=0.78)
    for z in (1.6, 6.4):
        f.append(linea(iso(1, 6, z, 4, 10, 0.78), iso(25, 6, z, 4, 10, 0.78), 0.9, VARILLA))
    for x in (5, 10, 15, 20):
        f += lineas([iso(x, 6, 0.8, 4, 10, 0.78), iso(x, 6, 7.2, 4, 10, 0.78), iso(x, 0, 7.2, 4, 10, 0.78)], 0.8, ROJO)
    # Una columna de apoyo debajo, para que se lea como viga.
    return caja(-1, -1, -16, 6, 6, 16, cx=4, cy=10, s=0.78, colores=(ARRIBA, IZQ, DER)) + f


def viga_corte():
    f = caja(0, 0, 0, 20, 5, 7, cx=2, cy=14, s=0.78)
    f += lineas([iso(10, 9, -2, 2, 14, 0.78), iso(10, 9, 10, 2, 14, 0.78), iso(10, -3, 10, 2, 14, 0.78)], 1.0, FLECHA)
    return f + seccion(23, 22, 14)


def importar_etabs():
    # La reticula del modelo: cuatro columnas y su losa, y la flecha de entrada.
    f = []
    for x, y in ((0, 0), (12, 0), (0, 12), (12, 12)):
        f += caja(x, y, 0, 2.5, 2.5, 11, cx=15, cy=21, s=0.85)
    f += caja(0, 0, 11, 14.5, 14.5, 2.2, cx=15, cy=21, s=0.85)
    return f + flecha(25, 1, 13, "abajo")


def armar_modelo():
    f = []
    for x, y in ((0, 0), (12, 0), (0, 12), (12, 12)):
        f += caja(x, y, 0, 2.5, 2.5, 11, cx=15, cy=21, s=0.85)
    f += caja(0, 0, 11, 14.5, 2.5, 2.2, cx=15, cy=21, s=0.85)
    f += caja(0, 0, 11, 2.5, 14.5, 2.2, cx=15, cy=21, s=0.85)
    return f + seccion(25, 8, 11)


def armar_tipo():
    # Tres columnas iguales: «todas las de un tipo».
    f = []
    for i, cx in enumerate((7, 16, 25)):
        f += caja(0, 0, 0, 4, 4, 17 - i, cx=cx - 1, cy=26 - i, s=0.9)
    return f


def corte_seccion():
    return seccion(16, 16, 26)


def placa_base():
    # La placa en el piso, sus cuatro anclas y la columna que arranca encima.
    f = caja(0, 0, 0, 16, 16, 1.6, cx=16, cy=17, s=1.0, colores=(GRIS, (150, 158, 168, 255), (110, 118, 130, 255)))
    for x, y in ((2.5, 2.5), (13.5, 2.5), (2.5, 13.5), (13.5, 13.5)):
        cx, cy = iso(x, y, 1.6, 16, 17)
        f.append(circulo(cx, cy, 1.3, OSCURO))
    return f + caja(5.5, 5.5, 1.6, 5, 5, 14, cx=16, cy=17, s=1.0)


def cimentacion():
    # La zapata, el dado y el arranque de la columna.
    gris = (GRIS, (150, 158, 168, 255), (110, 118, 130, 255))
    f = caja(0, 0, 0, 18, 18, 4, cx=16, cy=19, s=0.95, colores=gris)
    f += caja(5.5, 5.5, 4, 7, 7, 7, cx=16, cy=19, s=0.95, colores=gris)
    return f + caja(7, 7, 11, 4, 4, 9, cx=16, cy=19, s=0.95)


def soldar_viga():
    # La columna, la viga que llega y el cordon alrededor del perfil, en rojo.
    f = caja(0, 0, 0, 6, 6, 24, cx=9, cy=28, s=0.95)
    f += caja(6, 1.5, 14, 16, 3, 5, cx=9, cy=28, s=0.95)
    anillo = [iso(6.4, 1.2, 13.6, 9, 28, 0.95), iso(6.4, 4.8, 13.6, 9, 28, 0.95),
              iso(6.4, 4.8, 19.4, 9, 28, 0.95), iso(6.4, 1.2, 19.4, 9, 28, 0.95)]
    return f + lineas(anillo + [anillo[0]], 1.6, ROJO)


def armar_cimentacion():
    # La zapata gris con su parrilla roja y el dado encima.
    gris = (GRIS, (150, 158, 168, 255), (110, 118, 130, 255))
    f = caja(0, 0, 0, 18, 18, 5, cx=16, cy=17, s=0.95, colores=gris)
    for t in (4, 9, 14):
        f.append(linea(iso(t, 1, 5.2, 16, 17, 0.95), iso(t, 17, 5.2, 16, 17, 0.95), 1.0, ROJO))
        f.append(linea(iso(1, t, 5.2, 16, 17, 0.95), iso(17, t, 5.2, 16, 17, 0.95), 1.0, ROJO))
    return f + caja(6, 6, 5, 6, 6, 8, cx=16, cy=17, s=0.95, colores=gris)


def armar_muros():
    # El muro gris con su malla clara: verticales y horizontales.
    gris = (GRIS, (150, 158, 168, 255), (110, 118, 130, 255))
    f = caja(0, 0, 0, 22, 3, 20, cx=6, cy=22, s=0.9, colores=gris)
    for x in (4, 9, 14, 19):
        f.append(linea(iso(x, 3, 1.5, 6, 22, 0.9), iso(x, 3, 18.5, 6, 22, 0.9), 1.0, IZQ))
    for z in (4, 9, 14):
        f.append(linea(iso(1.5, 3, z, 6, 22, 0.9), iso(20.5, 3, z, 6, 22, 0.9), 1.0, ROJO))
    return f


def encamisar():
    # La trabe de concreto gris con sus placas azules abrazandola y dos soleras rojas.
    gris = (GRIS, (150, 158, 168, 255), (110, 118, 130, 255))
    f = caja(0, 0, 0, 26, 6, 9, cx=8, cy=11, s=0.7, colores=gris)
    f += caja(0, 6, -1, 26, 1, 10, cx=8, cy=11, s=0.7)
    f += caja(0, -1, -1.2, 26, 8, 1.2, cx=8, cy=11, s=0.7)
    for x in (7, 18):
        f += lineas([iso(x, 7.2, 9, 8, 11, 0.7), iso(x, 7.2, -1.4, 8, 11, 0.7), iso(x, -1, -1.4, 8, 11, 0.7)], 1.2, ROJO)
    return f


def soldar_columna():
    # La columna sobre su placa y el cordon rojo alrededor de su pie.
    gris = (GRIS, (150, 158, 168, 255), (110, 118, 130, 255))
    f = caja(0, 0, 0, 16, 16, 2, cx=16, cy=19, s=0.95, colores=gris)
    f += caja(5, 5, 2, 6, 6, 16, cx=16, cy=19, s=0.95)
    pie = [iso(4.6, 4.6, 2.3, 16, 19, 0.95), iso(11.4, 4.6, 2.3, 16, 19, 0.95),
           iso(11.4, 11.4, 2.3, 16, 19, 0.95), iso(4.6, 11.4, 2.3, 16, 19, 0.95)]
    return f + lineas(pie + [pie[0]], 1.6, ROJO)


ICONOS = {
    "importar-etabs": importar_etabs,
    "armar-modelo": armar_modelo,
    "columna-acero": columna_acero,
    "columna-corte": columna_corte,
    "viga-acero": viga_acero,
    "viga-corte": viga_corte,
    "armar-tipo": armar_tipo,
    "corte-seccion": corte_seccion,
    "placa-base": placa_base,
    "cimentacion": cimentacion,
    "soldar-viga": soldar_viga,
    "soldar-columna": soldar_columna,
    "encamisar": encamisar,
    "armar-muros": armar_muros,
    "armar-cimentacion": armar_cimentacion,
}


# ---------------------------------------------------------------- el rasterizador

def dentro(x, y, poly):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / ((yj - yi) or 1e-12) + xi:
            c = not c
        j = i
    return c


def rasterizar(formas, n, ss=4):
    escala = n / 32.0
    out = bytearray()
    cajas = []
    for poly, color in formas:
        xs = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        cajas.append((min(xs), max(xs), min(ys), max(ys)))

    for py in range(n):
        for px in range(n):
            acum = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) / escala
                    y = (py + (sy + 0.5) / ss) / escala
                    color = None
                    for (poly, c), (x0, x1, y0, y1) in zip(formas, cajas):
                        if x0 <= x <= x1 and y0 <= y <= y1 and dentro(x, y, poly):
                            color = c
                    if color is not None:
                        a = color[3] / 255
                        acum[0] += color[0] * a
                        acum[1] += color[1] * a
                        acum[2] += color[2] * a
                        acum[3] += a
            k = acum[3]
            if k == 0:
                out.extend((0, 0, 0, 0))
            else:
                out.extend((round(acum[0] / k), round(acum[1] / k), round(acum[2] / k),
                            round(255 * k / (ss * ss))))
    return bytes(out)


def main() -> int:
    DESTINO.mkdir(parents=True, exist_ok=True)
    for nombre, dibujo in ICONOS.items():
        formas = dibujo()
        for n in (32, 16):
            ruta = DESTINO / f"{nombre}-{n}.png"
            ruta.write_bytes(como_png(n, rasterizar(formas, n, 4 if n == 32 else 6)))
            print(f"  {ruta.relative_to(AQUI.parent)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
