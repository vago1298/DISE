#!/usr/bin/env python3
"""Prepara el instalador DE OFICINA: cada PC que lo instala pide permiso sola.

Lo llama 6-crear-instalador.bat, opcion 3. Hace dos cosas:

  1. Se asegura de que el servidor tenga un CODIGO DE OFICINA (OFFICE_CODE en server/.env).
     Si no lo tiene, lo genera y lo escribe. La PC que llega con ese codigo queda ESPERANDO
     hasta que el dueño la aprueba con un clic en 8-aprobar-equipos.bat: sin copiar huellas,
     y sin que entre nada que el no haya visto.

  2. Escribe cadlink.oficina.json junto al ejecutable que se va a empaquetar, con la
     direccion de ESTE equipo en la red -la del servidor, no localhost- y el codigo.

La aplicacion lee ese archivo encima de cadlink.config.json. El instalador lo copia SIEMPRE,
asi que instalarlo encima de una version de prueba tambien la deja autorizada.

    python scripts/paquete_oficina.py --salida RUTA\\cadlink.oficina.json

Sale con 0 si todo bien, 3 si el codigo es NUEVO (hay que reiniciar el servidor para que lo
use) y 1 si algo fallo.
"""

from __future__ import annotations

import argparse
import json
import re
import secrets
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mi_direccion import es_privada, ip_de_salida  # noqa: E402

ENV = Path(__file__).resolve().parent.parent / ".env"
CLAVE = "OFFICE_CODE"


def leer_codigo(texto: str) -> str:
    """El valor de OFFICE_CODE en el .env, sin comillas. Vacio si no esta o esta vacio."""
    m = re.search(r'^\s*' + CLAVE + r'\s*=\s*"?([^"\r\n#]*)"?', texto, re.M)
    return m.group(1).strip() if m else ""


def asegurar_codigo() -> tuple[str, bool]:
    """El codigo de oficina del servidor, generandolo si falta. Devuelve (codigo, es_nuevo)."""
    if not ENV.exists():
        raise SystemExit(
            f"ERROR: no encuentro {ENV}\n"
            "       El instalador de oficina se arma en la computadora del SERVIDOR.\n"
            "       Si es esta, ejecuta antes 1-instalar-servidor.bat."
        )

    texto = ENV.read_text(encoding="utf-8")
    codigo = leer_codigo(texto)

    if codigo:
        return codigo, False

    codigo = "OFICINA-" + secrets.token_urlsafe(24)
    linea = f'{CLAVE}="{codigo}"'

    if re.search(r'^\s*' + CLAVE + r'\s*=', texto, re.M):
        texto = re.sub(r'^\s*' + CLAVE + r'\s*=.*$', linea, texto, count=1, flags=re.M)
    else:
        if not texto.endswith("\n"):
            texto += "\n"
        texto += (
            "\n# Codigo de oficina: lo genero 6-crear-instalador.bat (opcion 3).\n"
            "# Cada PC que instala el paquete de oficina pide permiso; se aprueba\n"
            "# con un clic en 8-aprobar-equipos.bat.\n"
            f"{linea}\n"
        )

    ENV.write_text(texto, encoding="utf-8")
    return codigo, True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--salida", required=True, help="Ruta del cadlink.oficina.json a escribir")
    parser.add_argument("--puerto", type=int, default=8000)
    args = parser.parse_args()

    ip = ip_de_salida()

    if not ip or ip.startswith("127."):
        print("ERROR: no pude averiguar la direccion de esta computadora en la red.")
        print("       Revisa que este conectada a la red de la oficina (cable o WiFi).")
        return 1

    url = f"http://{ip}:{args.puerto}"
    codigo, nuevo = asegurar_codigo()

    datos = {
        "_que_es": (
            "Instalador DE OFICINA: este equipo se autoriza solo con el servidor de la "
            "oficina. Lo escribio 6-crear-instalador.bat; no lo edites."
        ),
        "servidorLicencias": url,
        "codigoOficina": codigo,
    }

    salida = Path(args.salida)
    salida.write_text(json.dumps(datos, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")

    print(f"  Servidor de la oficina:  {url}")

    if not es_privada(ip):
        print("  OJO: esa no parece una direccion de red local. Si las PCs no la")
        print("       alcanzan, conecta esta computadora a la red de la oficina.")

    if nuevo:
        print()
        print("  Se creo el CODIGO DE OFICINA del servidor (en server\\.env).")
        print("  >>> CIERRA Y VUELVE A ABRIR 2-iniciar-servidor.bat para que lo use. <<<")
        return 3

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
