#!/usr/bin/env python3
"""Genera los iconos del boton de CadLink en la cinta de Revit, sin dependencias externas.

    python tools/make_iconos_revit.py

===============================================================================
 POR QUE HACEN FALTA

 Un boton de la cinta de Revit sin imagen sale EN BLANCO, con el texto solo
 debajo. No da ningun error: simplemente se ve mal y no se distingue de los
 botones de los demas complementos.

 Revit pide DOS medidas y usa una u otra segun como dibuje el boton:

     LargeImage   32 x 32   el boton grande del panel, que es el caso normal
     Image        16 x 16   cuando el boton se apila o se mete en un desplegable

 Si solo se da la grande, Revit la reduce al vuelo y a 16 px queda una mancha.
 Por eso se dibuja cada medida por separado, igual que en make_icon.py.

===============================================================================
 EL DIBUJO NO SE REPITE AQUI

 Se importa de make_icon.py, que a su vez lo importa de
 make_placeholder_logo.py. El poligono del perfil I, el degradado y el radio de
 las esquinas estan escritos en UN solo sitio.

 Tenerlos copiados aqui acabaria con un icono en Revit que no se parece al de
 la aplicacion, y nadie se daria cuenta hasta verlos juntos en la pantalla.

===============================================================================
 ESTE ES UN MARCADOR DE POSICION

 Es la misma marca provisional que usa el ejecutable. Cuando haya icono real,
 se sustituyen los dos PNG de client/src/CadLink.Revit/Assets/ y basta con
 recompilar: van EMBEBIDOS en la DLL, asi que no hay que copiar nada mas.
"""

from __future__ import annotations

import sys
from pathlib import Path

AQUI = Path(__file__).resolve().parent
sys.path.insert(0, str(AQUI))

from make_icon import como_png, dibujar  # noqa: E402

# Las dos medidas que pide Revit, y el nombre con que se embeben.
MEDIDAS = {
    32: "importar-32.png",
    16: "importar-16.png",
}

DESTINO = AQUI.parent / "client" / "src" / "CadLink.Revit" / "Assets"

# El icono de los botones de ARMAR: la seccion de una trabe vista de punta, con su estribo
# rojo y sus cuatro varillas de esquina. Es lo que se reconoce como «armadura» a 16 px.
ARMAR = {
    32: "armar-32.png",
    16: "armar-16.png",
}


# A 16 px el dibujo suavizado se vuelve una mancha: el estribo y las varillas caen entre
# pixeles. Se dibuja A MANO, pixel por pixel: B borde, C concreto, R estribo, V varilla.
ARMAR_16 = [
    "..BBBBBBBBBBBB..",
    ".BCCCCCCCCCCCCB.",
    "BCCCCCCCCCCCCCCB",
    "BCCRRRRRRRRRRCCB",
    "BCRRRRRRRRRRRRCB",
    "BCRRVVCCCCVVRRCB",
    "BCRRVVCCCCVVRRCB",
    "BCRRCCCCCCCCRRCB",
    "BCRRCCCCCCCCRRCB",
    "BCRRVVCCCCVVRRCB",
    "BCRRVVCCCCVVRRCB",
    "BCRRRRRRRRRRRRCB",
    "BCCRRRRRRRRRRCCB",
    "BCCCCCCCCCCCCCCB",
    ".BCCCCCCCCCCCCB.",
    "..BBBBBBBBBBBB..",
]


def dibujar_armado(medida: int) -> bytes:
    """La seccion armada, con suavizado por supermuestreo (8 x 8 por pixel)."""
    if medida == 16:
        colores = {"B": (110, 116, 124, 255), "C": (200, 203, 207, 255),
                   "R": (214, 40, 40, 255), "V": (40, 44, 52, 255), ".": (0, 0, 0, 0)}
        return bytes(c for fila in ARMAR_16 for ch in fila for c in colores[ch])

    ss = 8
    n = medida

    # Medidas en fraccion del icono. A 16 px el estribo y las varillas engordan un poco
    # para que no se pierdan.
    chico = n <= 16
    m_conc = 0.06                    # margen del concreto
    r_conc = 0.12                    # radio de sus esquinas
    m_est = 0.22 if chico else 0.21  # eje del estribo
    g_est = 0.10 if chico else 0.075 # grosor del estribo
    r_est = 0.10                     # radio del doblez
    r_var = 0.085 if chico else 0.075
    c_var = m_est + g_est / 2 + r_var - 0.01  # centro de las varillas de esquina

    concreto = (200, 203, 207)
    borde = (110, 116, 124)
    rojo = (214, 40, 40)
    varilla = (40, 44, 52)

    def caja_redonda(x, y, m, r):
        """Distancia con signo a un cuadrado de esquinas redondas, de m a 1-m."""
        cx, cy = abs(x - 0.5), abs(y - 0.5)
        h = 0.5 - m - r
        qx, qy = max(cx - h, 0), max(cy - h, 0)
        fuera = (qx * qx + qy * qy) ** 0.5
        return (fuera if (qx > 0 or qy > 0) else max(cx - h, cy - h)) - r

    out = bytearray()

    for py in range(n):
        for px in range(n):
            acum = [0.0, 0.0, 0.0, 0.0]

            for sy in range(ss):
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) / n
                    y = (py + (sy + 0.5) / ss) / n
                    color = None

                    d = caja_redonda(x, y, m_conc, r_conc)
                    if d <= 0:
                        color = borde if d > -0.045 else concreto

                    e = caja_redonda(x, y, m_est, r_est)
                    if abs(e) <= g_est / 2:
                        color = rojo

                    for vx in (c_var, 1 - c_var):
                        for vy in (c_var, 1 - c_var):
                            if (x - vx) ** 2 + (y - vy) ** 2 <= r_var ** 2:
                                color = varilla

                    if color is not None:
                        acum[0] += color[0]
                        acum[1] += color[1]
                        acum[2] += color[2]
                        acum[3] += 1

            k = acum[3]
            if k == 0:
                out.extend((0, 0, 0, 0))
            else:
                out.extend((round(acum[0] / k), round(acum[1] / k), round(acum[2] / k),
                            round(255 * k / (ss * ss))))

    return bytes(out)


def main() -> int:
    DESTINO.mkdir(parents=True, exist_ok=True)

    for medida, nombre in sorted(MEDIDAS.items(), reverse=True):
        rgba = dibujar(medida)
        png = como_png(medida, rgba)

        ruta = DESTINO / nombre
        ruta.write_bytes(png)

        print(f"  {ruta.relative_to(AQUI.parent)}  {medida}x{medida}  {len(png)} bytes")

    for medida, nombre in sorted(ARMAR.items(), reverse=True):
        png = como_png(medida, dibujar_armado(medida))

        ruta = DESTINO / nombre
        ruta.write_bytes(png)

        print(f"  {ruta.relative_to(AQUI.parent)}  {medida}x{medida}  {len(png)} bytes")

    print()
    print("Listo. Van embebidos en la DLL, asi que solo hay que recompilar:")
    print("    7-instalar-plugin-revit.bat")

    return 0


if __name__ == "__main__":
    sys.exit(main())
