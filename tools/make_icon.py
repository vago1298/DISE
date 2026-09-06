#!/usr/bin/env python3
"""Genera el icono del ejecutable -Assets/app.ico- sin dependencias externas.

    python tools/make_icon.py client/src/CadLink.App/Assets/app.ico

===============================================================================
 POR QUE HACE FALTA UN .ico Y NO BASTA EL logo.png

 El logo.png se usa DENTRO de la aplicacion -el splash, la barra de titulo- y
 para eso vale un PNG. Pero el icono del EJECUTABLE es otra cosa: va incrustado
 en el .exe como recurso de Windows, lo pone el compilador con la propiedad
 ApplicationIcon del .csproj, y tiene que ser un .ico de verdad.

 Sin el, el .exe no tiene icono y Windows le pone el generico. Eso es lo que se
 ve en el acceso directo del escritorio: un icono de "programa cualquiera".

 Y UN .ico NO ES UNA IMAGEN, SON VARIAS. Windows toma la medida que necesita
 segun donde lo dibuje: 16 px en la barra de tareas y en la pestana del
 Explorador, 32 en el escritorio, 48 en vista de iconos medianos, 256 en vista
 de iconos extra grandes y en la ventana de propiedades. Si solo se mete la
 grande, Windows la reduce al vuelo y a 16 px queda una manchita borrosa.
 Por eso aqui se DIBUJA cada medida por separado.

===============================================================================
 EL DIBUJO NO SE REPITE AQUI

 El rayo, el degradado y el radio de las esquinas se importan de
 make_placeholder_logo.py, que es donde ya estaban. Tener el poligono escrito
 en dos archivos acabaria con un icono que no se parece al logo, y nadie se
 daria cuenta hasta verlos juntos.

===============================================================================
 ESTE ES UN MARCADOR DE POSICION

 Reemplazalo por el icono real: copia tu CADLINK.ico a la carpeta  installer  y
 6-crear-instalador.bat  lo usa solo. O ponlo directamente como
 client/src/CadLink.App/Assets/app.ico
"""

from __future__ import annotations

import os
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from make_placeholder_logo import (  # noqa: E402
    ACCENT,
    BOLT,
    BRAND_DARK,
    BRAND_LIGHT,
    point_in_polygon,
    rounded_rect_alpha,
)

#  El dibujo original esta en coordenadas de 0..512 y de ahi se escala a cada medida.
LIENZO = 512

#  LAS MEDIDAS QUE WINDOWS PIDE DE VERDAD. 24 y 64 no las pide casi nadie, pero pesan
#  poco y evitan que el sistema tenga que interpolar en pantallas al 125 % y al 150 %.
MEDIDAS = (16, 24, 32, 48, 64, 128, 256)

#  MUESTREO. Cada pixel se calcula promediando MUESTRAS x MUESTRAS puntos: es lo que
#  suaviza el borde redondeado y el filo del rayo. Sin esto, a 16 px el rayo sale como
#  una escalera.
MUESTRAS = 4


def dibujar(medida: int) -> bytes:
    """El icono a una medida, en RGBA de arriba hacia abajo."""
    escala = LIENZO / medida
    radio = LIENZO * 0.18
    paso = 1.0 / MUESTRAS
    total = MUESTRAS * MUESTRAS

    salida = bytearray()

    for py in range(medida):
        for px in range(medida):
            r_sum = g_sum = b_sum = a_sum = 0

            for sy in range(MUESTRAS):
                for sx in range(MUESTRAS):
                    #  Centro de cada submuestra, llevado al lienzo de 512.
                    x = (px + (sx + 0.5) * paso) * escala
                    y = (py + (sy + 0.5) * paso) * escala

                    alpha = rounded_rect_alpha(x, y, LIENZO, radio)

                    t = (x + y) / (2 * LIENZO)
                    r = BRAND_DARK[0] + (BRAND_LIGHT[0] - BRAND_DARK[0]) * t
                    g = BRAND_DARK[1] + (BRAND_LIGHT[1] - BRAND_DARK[1]) * t
                    b = BRAND_DARK[2] + (BRAND_LIGHT[2] - BRAND_DARK[2]) * t

                    if point_in_polygon(x, y, BOLT):
                        r, g, b = ACCENT

                    #  El color se pondera por su alpha: si no, el borde redondeado
                    #  arrastra el color del fondo y queda un halo oscuro alrededor.
                    r_sum += r * alpha
                    g_sum += g * alpha
                    b_sum += b * alpha
                    a_sum += alpha

            if a_sum <= 0:
                salida.extend((0, 0, 0, 0))
                continue

            salida.extend((
                min(255, int(r_sum / a_sum + 0.5)),
                min(255, int(g_sum / a_sum + 0.5)),
                min(255, int(b_sum / a_sum + 0.5)),
                min(255, int(a_sum / total * 255 + 0.5)),
            ))

    return bytes(salida)


def como_png(medida: int, rgba: bytes) -> bytes:
    """La imagen como archivo PNG, para la entrada de 256."""
    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    filas = bytearray()

    for y in range(medida):
        filas.append(0)  # filtro PNG: ninguno
        filas.extend(rgba[y * medida * 4:(y + 1) * medida * 4])

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", medida, medida, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(filas), 9))
            + chunk(b"IEND", b""))


def como_bmp(medida: int, rgba: bytes) -> bytes:
    """
    La imagen en el formato BMP que espera un .ico.

    Tres cosas que no son obvias y que, mal puestas, dan un icono negro:

      1. La ALTURA DEL ENCABEZADO VA AL DOBLE. Es historia: la entrada guarda la
         imagen y debajo una mascara de transparencia de 1 bit, y el encabezado
         declara las dos juntas.
      2. Las filas van DE ABAJO HACIA ARRIBA.
      3. El orden de los canales es BGRA, no RGBA.

    La mascara se escribe en ceros -"nada tapado"- porque con 32 bits la
    transparencia la lleva el canal alfa. Pero tiene que estar, y con su tamano
    exacto: cada fila se rellena a multiplo de 4 bytes.
    """
    encabezado = struct.pack(
        "<IiiHHIIiiII",
        40,             # tamano del encabezado
        medida,         # ancho
        medida * 2,     # alto: imagen + mascara
        1,              # planos
        32,             # bits por pixel
        0,              # sin compresion
        0, 0, 0, 0, 0,  # tamano de imagen, resolucion y paleta: sin usar
    )

    pixeles = bytearray()

    for y in range(medida - 1, -1, -1):
        fila = rgba[y * medida * 4:(y + 1) * medida * 4]

        for i in range(0, len(fila), 4):
            pixeles.extend((fila[i + 2], fila[i + 1], fila[i], fila[i + 3]))

    mascara = bytes(((medida + 31) // 32) * 4 * medida)

    return encabezado + bytes(pixeles) + mascara


def escribir_ico(destino: Path, imagenes: list[tuple[int, bytes]]) -> None:
    #  Windows resuelve la medida por el encabezado de cada imagen, pero el directorio
    #  se deja ordenado de menor a mayor porque hay programas viejos que toman la
    #  primera entrada que les sirve en lugar de buscar la mejor.
    imagenes = sorted(imagenes, key=lambda p: p[0])

    directorio = bytearray(struct.pack("<HHH", 0, 1, len(imagenes)))
    desplazamiento = 6 + 16 * len(imagenes)
    cuerpo = bytearray()

    for medida, datos in imagenes:
        directorio.extend(struct.pack(
            "<BBBBHHII",
            0 if medida >= 256 else medida,  # 256 se declara como 0
            0 if medida >= 256 else medida,
            0,                               # colores de la paleta: ninguno
            0,                               # reservado
            1,                               # planos
            32,                              # bits por pixel
            len(datos),
            desplazamiento,
        ))

        cuerpo.extend(datos)
        desplazamiento += len(datos)

    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_bytes(bytes(directorio) + bytes(cuerpo))


def main() -> int:
    destino = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("app.ico")

    imagenes: list[tuple[int, bytes]] = []

    for medida in MEDIDAS:
        rgba = dibujar(medida)

        #  LA DE 256 VA COMPRIMIDA EN PNG. Sin comprimir son 256 KB solo esa entrada,
        #  y desde Windows Vista el .ico admite PNG dentro. Las chicas van en BMP, que
        #  es lo que entiende todo, y pesan poco de todos modos.
        datos = como_png(medida, rgba) if medida >= 256 else como_bmp(medida, rgba)

        imagenes.append((medida, datos))
        print(f"   {medida:3d} x {medida:<3d}  {len(datos):>7,} bytes")

    escribir_ico(destino, imagenes)

    print(f"\nEscrito {destino} ({destino.stat().st_size:,} bytes, "
          f"{len(imagenes)} medidas)")
    print("Es un marcador de posicion: reemplazalo por el icono real de tu empresa.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
