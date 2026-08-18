#!/usr/bin/env python3
"""Pega automáticamente la llave pública en el código del cliente.

Copiar la llave a mano es el paso más fácil de equivocar de toda la instalación:
un espacio de más o un salto de línea perdido y la aplicación deja de validar
licencias sin decir por qué. Este script lo hace por ti.

Uso (desde la carpeta cadlink):
    python tools/embed_public_key.py

Lee:      server/keys/public.pem
Modifica: client/src/CadLink.Licensing/EmbeddedPublicKey.cs
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

INDENT = " " * 8  # coincide con la indentación del literal de cadena en el .cs

# Marcador de posición del archivo .cs.
#
# IMPORTANTE: esta cadena debe aparecer ÚNICAMENTE dentro del bloque PEM del
# marcador, y en ningún otro lugar del código. La primera versión usaba
# "REEMPLAZA_ESTE_BLOQUE", que también aparecía en la comprobación de C# que lo
# buscaba, así que cualquier búsqueda de texto lo encontraba siempre y el
# instalador creía que la llave nunca se había insertado.
MARCADOR = "PEGA_AQUI_TU_LLAVE_PUBLICA"


def build_pem_block(pem_text: str) -> str:
    """Reindenta el PEM para el literal de cadena cruda de C#."""
    lines = [ln.strip() for ln in pem_text.strip().splitlines() if ln.strip()]
    return "\n".join(INDENT + ln for ln in lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".", help="Carpeta raíz del proyecto (cadlink)")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    pem_path = root / "server" / "keys" / "public.pem"
    cs_path = root / "client" / "src" / "CadLink.Licensing" / "EmbeddedPublicKey.cs"

    if not pem_path.exists():
        print(
            f"ERROR: no encontré {pem_path}\n\n"
            "Primero genera las llaves. Ejecuta 1-instalar-servidor.bat,\n"
            "o desde la carpeta server:  python scripts/generate_keys.py",
            file=sys.stderr,
        )
        return 1

    if not cs_path.exists():
        print(f"ERROR: no encontré {cs_path}", file=sys.stderr)
        return 1

    pem_text = pem_path.read_text(encoding="utf-8")

    if "BEGIN PUBLIC KEY" not in pem_text:
        print(
            f"ERROR: {pem_path} no parece una llave pública.\n"
            "Debe empezar con -----BEGIN PUBLIC KEY-----",
            file=sys.stderr,
        )
        return 1

    # Verificación de seguridad: si por error apuntaran a la llave PRIVADA,
    # se embebería en el ejecutable del cliente y quedaría expuesta a todos.
    if "PRIVATE KEY" in pem_text:
        print(
            "ERROR: ese archivo contiene una llave PRIVADA.\n"
            "La llave privada NUNCA debe ir en el cliente. Usa public.pem.",
            file=sys.stderr,
        )
        return 1

    source = cs_path.read_text(encoding="utf-8")

    # Reemplaza el contenido entre los delimitadores del literal de cadena cruda.
    pattern = re.compile(
        r'(public const string Pem = """\n)(.*?)(\n\s*""";)',
        re.DOTALL,
    )

    if not pattern.search(source):
        print(
            f"ERROR: no encontré la constante Pem en {cs_path.name}.\n"
            "¿Se editó el archivo a mano? Pega la llave manualmente.",
            file=sys.stderr,
        )
        return 1

    new_source = pattern.sub(
        lambda m: m.group(1) + build_pem_block(pem_text) + m.group(3),
        source,
        count=1,
    )

    # Quita el comentario del marcador de posición, que ya no aplica.
    # Se usa una expresión regular en lugar de una cadena exacta para que un
    # cambio menor de redacción no deje el comentario viejo pegado.
    new_source = re.sub(
        r"^[ \t]*//[ \t]*\u26a0.*MARCADOR DE POSICION.*$",
        "    // Llave publica insertada automaticamente por tools/embed_public_key.py",
        new_source,
        count=1,
        flags=re.MULTILINE | re.IGNORECASE,
    )

    if new_source == source:
        print("La llave ya estaba puesta, con el mismo contenido. No hubo cambios.")
        return 0

    cs_path.write_text(new_source, encoding="utf-8")

    lines = [ln for ln in pem_text.strip().splitlines() if ln.strip()]
    print("Llave publica insertada correctamente.")
    print(f"  Origen:  {pem_path}")
    print(f"  Destino: {cs_path}")
    print(f"  {len(lines)} lineas de PEM")

    # Verificación final: que el marcador de posición ya NO esté en el archivo.
    # Se comprueba aquí, y no con una búsqueda de texto desde el .bat, porque
    # desde afuera es fácil confundir el marcador con el código que lo menciona.
    if MARCADOR in cs_path.read_text(encoding="utf-8"):
        print(
            f"\nERROR: el marcador {MARCADOR} sigue en el archivo. "
            "La sustitucion no se aplico como se esperaba.",
            file=sys.stderr,
        )
        return 1

    print("\nYa puedes compilar la aplicacion: ejecuta 3-abrir-app.bat")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
