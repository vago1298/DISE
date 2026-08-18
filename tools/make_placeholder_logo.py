#!/usr/bin/env python3
"""Genera un logo PNG de marcador de posición, sin dependencias externas.

Existe solo para que el proyecto compile y el splash muestre algo desde el primer
arranque. REEMPLÁZALO por el logo real de tu empresa:

    client/src/CadLink.App/Assets/logo.png

Recomendación para el logo definitivo: PNG con fondo transparente, al menos
512x512 px, para que se vea nítido en pantallas de alta densidad (4K).

Uso:
    python tools/make_placeholder_logo.py client/src/CadLink.App/Assets/logo.png
"""

from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

SIZE = 512
BRAND_DARK = (11, 61, 107)      # azul corporativo oscuro
BRAND_LIGHT = (23, 118, 191)    # azul corporativo claro
ACCENT = (255, 199, 44)         # amarillo del rayo

# Rayo estilizado, en coordenadas de 0..512
BOLT = [
    (300, 60), (170, 280), (250, 280), (205, 452),
    (350, 232), (265, 232), (330, 60),
]


def point_in_polygon(x: float, y: float, poly: list[tuple[int, int]]) -> bool:
    """Ray casting estándar: cuenta cruces con las aristas."""
    inside = False
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        if (y1 > y) != (y2 > y):
            x_cross = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
            if x < x_cross:
                inside = not inside
    return inside


def rounded_rect_alpha(x: float, y: float, size: int, radius: float) -> float:
    """Alpha suavizado de un cuadrado con esquinas redondeadas."""
    cx = min(max(x, radius), size - radius)
    cy = min(max(y, radius), size - radius)
    dist = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
    if dist <= radius - 1:
        return 1.0
    if dist >= radius:
        return 0.0
    return radius - dist


def build_rows() -> list[bytearray]:
    radius = SIZE * 0.18
    rows: list[bytearray] = []

    for py in range(SIZE):
        row = bytearray()
        row.append(0)  # filtro PNG: None
        for px in range(SIZE):
            x, y = px + 0.5, py + 0.5

            alpha = rounded_rect_alpha(x, y, SIZE, radius)

            # Degradado diagonal del fondo
            t = (px + py) / (2 * SIZE)
            r = int(BRAND_DARK[0] + (BRAND_LIGHT[0] - BRAND_DARK[0]) * t)
            g = int(BRAND_DARK[1] + (BRAND_LIGHT[1] - BRAND_DARK[1]) * t)
            b = int(BRAND_DARK[2] + (BRAND_LIGHT[2] - BRAND_DARK[2]) * t)

            if point_in_polygon(x, y, BOLT):
                r, g, b = ACCENT

            row.extend((r, g, b, int(alpha * 255)))
        rows.append(row)

    return rows


def write_png(path: Path, rows: list[bytearray]) -> None:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    # bit depth 8, color type 6 (RGBA)
    ihdr = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)
    raw = b"".join(bytes(r) for r in rows)

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def main() -> int:
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("logo.png")
    write_png(target, build_rows())
    print(f"Escrito {target} ({target.stat().st_size:,} bytes, {SIZE}x{SIZE} RGBA)")
    print("Recuerda reemplazarlo por el logo real de tu empresa.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
