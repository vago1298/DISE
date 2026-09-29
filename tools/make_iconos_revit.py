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


def main() -> int:
    DESTINO.mkdir(parents=True, exist_ok=True)

    for medida, nombre in sorted(MEDIDAS.items(), reverse=True):
        rgba = dibujar(medida)
        png = como_png(medida, rgba)

        ruta = DESTINO / nombre
        ruta.write_bytes(png)

        print(f"  {ruta.relative_to(AQUI.parent)}  {medida}x{medida}  {len(png)} bytes")

    print()
    print("Listo. Van embebidos en la DLL, asi que solo hay que recompilar:")
    print("    7-instalar-plugin-revit.bat")

    return 0


if __name__ == "__main__":
    sys.exit(main())
