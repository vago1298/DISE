#!/usr/bin/env python3
"""Dice qué dirección tienen que poner las PCs de la oficina.

El servidor de licencias corre en UNA computadora y las demás le hablan por la red
local. Desde ellas, `localhost` es su propia máquina —no la del servidor—, así que
dejar esa dirección en su configuración es la causa número uno de «no tiene acceso»:
la aplicación busca un servidor en la propia PC del trabajador, no lo encuentra, y se
queda con la prueba de 30 días o directamente sin licencia.

Este script imprime las direcciones IPv4 de este equipo y la URL ya armada para
copiarla en el `cadlink.config.json` de cada PC.

    python scripts/mi_direccion.py
    python scripts/mi_direccion.py --puerto 8000
"""

from __future__ import annotations

import argparse
import socket


def ip_de_salida() -> str | None:
    """La IP de la tarjeta por la que este equipo sale a la red.

    Se abre un socket UDP hacia una dirección que no existe. **No se manda ningún
    paquete** —UDP no establece conexión— pero el sistema tiene que elegir por qué
    tarjeta saldría, y esa elección es justo la que se quiere saber. Es la forma
    fiable de acertar cuando hay varias tarjetas: cable, WiFi, VPN, Docker.
    """
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    try:
        s.connect(("10.255.255.255", 1))
        return s.getsockname()[0]
    except OSError:
        return None
    finally:
        s.close()


def todas_las_ipv4() -> list[str]:
    """Las demás direcciones del equipo, por si la de salida no es la de la oficina."""
    salida: list[str] = []

    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]

            if not ip.startswith("127.") and ip not in salida:
                salida.append(ip)
    except OSError:
        pass

    return salida


def es_privada(ip: str) -> bool:
    """¿Es una dirección de red local? Son las que sirven para la oficina."""
    if ip.startswith("192.168.") or ip.startswith("10."):
        return True

    if ip.startswith("172."):
        try:
            segundo = int(ip.split(".")[1])
        except (IndexError, ValueError):
            return False

        return 16 <= segundo <= 31

    return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--puerto", type=int, default=8000, help="Puerto del servidor")
    parser.add_argument(
        "--solo-url",
        action="store_true",
        help="Imprime nada más la URL, para usarla desde un .bat",
    )
    args = parser.parse_args()

    principal = ip_de_salida()
    otras = [ip for ip in todas_las_ipv4() if ip != principal]

    if args.solo_url:
        print(f"http://{principal or 'localhost'}:{args.puerto}")
        return 0

    print("=" * 62)
    print("  DIRECCION DE ESTE SERVIDOR EN LA RED")
    print("=" * 62)
    print()

    if principal is None:
        print("No pude averiguar la direccion de este equipo.")
        print("Abre una ventana de comandos y ejecuta:  ipconfig")
        print("Busca la 'Direccion IPv4' de tu tarjeta de red.")
        return 1

    url = f"http://{principal}:{args.puerto}"

    print(f"  Esta computadora es:  {principal}")

    if not es_privada(principal):
        print()
        print("  OJO: esa no parece una direccion de red local. Si tus equipos no la")
        print("  alcanzan, mira las otras de abajo o ejecuta  ipconfig.")

    if otras:
        print(f"  Otras direcciones:    {', '.join(otras)}")

    print()
    print("-" * 62)
    print("  EN CADA PC DE LA OFICINA, en su archivo cadlink.config.json,")
    print("  pon esta linea:")
    print()
    print(f'      "servidorLicencias": "{url}",')
    print()
    print("  Guarda el archivo y vuelve a abrir CadLink en esa PC.")
    print("-" * 62)
    print()
    print("  Comprueba desde la PC del trabajador, en su navegador:")
    print()
    print(f"      {url}/health")
    print()
    print("  Si NO responde, es el firewall de Windows de ESTE equipo. Abre una")
    print("  ventana de comandos COMO ADMINISTRADOR aqui y pega:")
    print()
    print('      netsh advfirewall firewall add rule name="CadLink licencias" '
          f'dir=in action=allow protocol=TCP localport={args.puerto}')
    print()
    print("=" * 62)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
