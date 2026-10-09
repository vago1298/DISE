#!/usr/bin/env python3
"""El icono de los TRABAJOS de CadLink (.clk): el mismo icono del programa, puesto sobre una
hoja con la esquina doblada, que es como Windows distingue un archivo de su programa.

    python tools/make_icono_archivo.py

Lee  client/src/CadLink.App/Assets/app.ico  -el icono que lleve el programa, el provisional o
el de tu empresa- y escribe  archivo.ico  junto a el. Si cambias app.ico, vuelve a correrlo y
los dos siguen pareciendose. Sin dependencias externas.
"""

from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

AQUI = Path(__file__).resolve().parent
sys.path.insert(0, str(AQUI))

from make_icon import como_bmp, como_png, escribir_ico  # noqa: E402

ASSETS = AQUI.parent / "client" / "src" / "CadLink.App" / "Assets"
MEDIDAS = (16, 24, 32, 48, 64, 128, 256)

HOJA = (250, 251, 253)
BORDE = (112, 120, 132)
DOBLEZ = (214, 220, 228)


# ------------------------------------------------------------------ leer app.ico

def _png_a_rgba(datos: bytes) -> tuple[int, bytes]:
    pos = 8
    ancho = alto = 0
    idat = bytearray()
    while pos < len(datos):
        largo, tipo = struct.unpack(">I4s", datos[pos:pos + 8])
        cuerpo = datos[pos + 8:pos + 8 + largo]
        if tipo == b"IHDR":
            ancho, alto, bits, color = struct.unpack(">IIBB", cuerpo[:10])
            if bits != 8 or color != 6:
                raise ValueError("solo PNG RGBA de 8 bits")
        elif tipo == b"IDAT":
            idat.extend(cuerpo)
        pos += 12 + largo
    crudo = zlib.decompress(bytes(idat))
    paso = ancho * 4
    out = bytearray()
    previa = bytearray(paso)
    i = 0
    for _ in range(alto):
        filtro = crudo[i]
        fila = bytearray(crudo[i + 1:i + 1 + paso])
        i += 1 + paso
        for x in range(paso):
            a = fila[x - 4] if x >= 4 else 0
            b = previa[x]
            c = previa[x - 4] if x >= 4 else 0
            if filtro == 1:
                fila[x] = (fila[x] + a) & 255
            elif filtro == 2:
                fila[x] = (fila[x] + b) & 255
            elif filtro == 3:
                fila[x] = (fila[x] + (a + b) // 2) & 255
            elif filtro == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                fila[x] = (fila[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out.extend(fila)
        previa = fila
    return ancho, bytes(out)


def _bmp_a_rgba(datos: bytes) -> tuple[int, bytes]:
    ancho, alto2, _, bits = struct.unpack("<iiHH", datos[4:16])
    if bits != 32:
        raise ValueError("solo BMP de 32 bits")
    n = ancho
    out = bytearray(n * n * 4)
    for y in range(n):
        fila = datos[40 + (n - 1 - y) * n * 4:40 + (n - y) * n * 4]
        for x in range(n):
            b, g, r, a = fila[x * 4:x * 4 + 4]
            out[(y * n + x) * 4:(y * n + x) * 4 + 4] = bytes((r, g, b, a))
    return n, bytes(out)


def leer_ico(ruta: Path) -> tuple[int, bytes]:
    """La imagen MAS GRANDE del .ico, como RGBA."""
    d = ruta.read_bytes()
    n = struct.unpack("<HHH", d[:6])[2]
    mejor = None
    for k in range(n):
        w, _, _, _, _, _, tam, off = struct.unpack("<BBBBHHII", d[6 + 16 * k:22 + 16 * k])
        w = w or 256
        if mejor is None or w > mejor[0]:
            mejor = (w, d[off:off + tam])
    datos = mejor[1]
    return _png_a_rgba(datos) if datos[:8] == b"\x89PNG\r\n\x1a\n" else _bmp_a_rgba(datos)


# ------------------------------------------------------------------ dibujar

def _muestra(fuente, n, x, y):
    """El color de la fuente en (x, y) en [0,1), por vecino mas cercano."""
    px = min(n - 1, max(0, int(x * n)))
    py = min(n - 1, max(0, int(y * n)))
    i = (py * n + px) * 4
    return fuente[i:i + 4]


def dibujar(medida: int, fuente: bytes, nf: int) -> bytes:
    ss = 8 if medida <= 32 else 4
    # La hoja, en fraccion del icono: de 0.14 a 0.86 a lo ancho, con la esquina doblada.
    x0, x1, y0, y1 = 0.14, 0.86, 0.03, 0.97
    pliegue = 0.24
    borde = max(1.0, medida / 40) / medida
    # El icono del programa, sobre la hoja: grande, para que se reconozca a 16 px.
    lado = 0.62 if medida > 24 else 0.70
    lx0 = 0.5 - lado / 2
    ly0 = 0.93 - lado - (0.04 if medida > 24 else 0.0)

    out = bytearray()
    for py in range(medida):
        for px in range(medida):
            acum = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) / medida
                    y = (py + (sy + 0.5) / ss) / medida
                    color = None
                    # Dentro de la hoja: el rectangulo sin el triangulo de la esquina.
                    en_hoja = x0 <= x <= x1 and y0 <= y <= y1 and (x - (x1 - pliegue)) + (y0 + pliegue - y) <= pliegue
                    if en_hoja:
                        dentro = (x0 + borde <= x <= x1 - borde and y0 + borde <= y <= y1 - borde
                                  and (x - (x1 - pliegue)) + (y0 + pliegue - y) <= pliegue - borde * 1.42)
                        color = (*HOJA, 255) if dentro else (*BORDE, 255)
                    # El doblez dibujado encima: triangulo (x1-p, y0) - (x1-p, y0+p) - (x1, y0+p).
                    if x1 - pliegue <= x <= x1 and y0 <= y <= y0 + pliegue and (x - (x1 - pliegue)) <= (y - y0):
                        lejos = min(x - (x1 - pliegue), (y0 + pliegue) - y, ((y - y0) - (x - (x1 - pliegue))) / 1.42)
                        color = (*DOBLEZ, 255) if lejos > borde else (*BORDE, 255)
                    # El icono del programa.
                    if lx0 <= x < lx0 + lado and ly0 <= y < ly0 + lado:
                        r, g, b, a = _muestra(fuente, nf, (x - lx0) / lado, (y - ly0) / lado)
                        if a > 0:
                            al = a / 255
                            base = color or (0, 0, 0, 0)
                            if base[3] == 0:
                                color = (r, g, b, a)
                            else:
                                color = (round(r * al + base[0] * (1 - al)), round(g * al + base[1] * (1 - al)),
                                         round(b * al + base[2] * (1 - al)), 255)
                    if color is not None and color[3] > 0:
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
    origen = ASSETS / "app.ico"
    destino = ASSETS / "archivo.ico"
    nf, fuente = leer_ico(origen)

    imagenes = []
    for m in MEDIDAS:
        rgba = dibujar(m, fuente, nf)
        imagenes.append((m, como_png(m, rgba) if m >= 256 else como_bmp(m, rgba)))
        print(f"   {m:3d} x {m:<3d}")

    escribir_ico(destino, imagenes)
    print(f"\nEscrito {destino.relative_to(AQUI.parent)}, a partir de {origen.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
