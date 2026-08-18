#!/usr/bin/env python3
"""Convierte un equipo en INTERNO permanente, sin copiar huellas a mano.

Pensado para el equipo del dueño. Trabaja directo sobre la base de datos, así que
NO hace falta que el servidor esté encendido ni usar la clave de administrador.

Uso normal, cuando solo hay un equipo registrado:
    python scripts/hazme_permanente.py

Si hay varios, primero se listan y luego se elige:
    python scripts/hazme_permanente.py --lista
    python scripts/hazme_permanente.py --id 3
    python scripts/hazme_permanente.py --todos
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

# Permite ejecutarlo desde cualquier carpeta
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from sqlalchemy import select  # noqa: E402

from app.database import SessionLocal, init_db  # noqa: E402
from app.models import Machine, Tier  # noqa: E402


def texto_equipo(m: Machine) -> str:
    marca = "  <-- REVOCADO" if m.revoked else ""
    return (
        f"  id={m.id:<4} tier={m.tier:<11} "
        f"equipo={(m.hostname or '?'):<20} "
        f"visto={m.last_seen:%d/%m/%Y %H:%M}{marca}\n"
        f"        huella={m.fingerprint}"
    )


def servidor_encendido(timeout: float = 1.5) -> bool:
    """¿Hay algo escuchando en el puerto del servidor de licencias?"""
    import socket

    try:
        with socket.create_connection(("127.0.0.1", 8000), timeout=timeout):
            return True
    except OSError:
        return False


def sin_equipos() -> int:
    """Explica el orden correcto cuando la base está vacía.

    Un equipo solo aparece en la base cuando la aplicación logra hablar con el
    servidor. Decir nada más «abre la aplicación» es un mal consejo si el
    servidor está apagado: la aplicación se abre, falla la activación y la base
    sigue vacía. Así que primero se comprueba el servidor.
    """
    encendido = servidor_encendido()

    print("Todavia no hay ningun equipo registrado en esta base de datos.")
    print(f"  Base de datos : {ruta_bd()}")
    print(f"  Servidor      : {'ENCENDIDO' if encendido else 'APAGADO'}  (127.0.0.1:8000)")
    print()
    print("Un equipo se registra cuando la aplicacion logra hablar con el")
    print("servidor. Si el servidor esta apagado, la activacion falla y la")
    print("base se queda vacia.")
    print()

    if not encendido:
        print("EL SERVIDOR ESTA APAGADO. Ese es el problema. Haz esto en orden:")
        print()
        print("  1. Ejecuta  2-iniciar-servidor.bat  y DEJA ESA VENTANA ABIERTA.")
        print("  2. Ejecuta  3-abrir-app.bat")
        print("     En la ventana de activacion pulsa 'Reintentar activacion")
        print("     automatica'. Debe entrar sin pedirte ninguna clave.")
        print("  3. Cierra la aplicacion y ejecuta  4-hazme-permanente.bat")
        return 1

    print("El servidor SI esta encendido, asi que el problema es otro:")
    print()
    print("  - Abre  3-abrir-app.bat  y pulsa 'Reintentar activacion automatica'.")
    print("  - Si sale un error, mandame el texto COMPLETO de ese error.")
    print("  - Comprueba que  cadlink.config.json  apunte a  http://localhost:8000")
    print("  - Si acabas de reinstalar y borraste  server\\keys\\, se generaron")
    print("    llaves nuevas y las licencias viejas ya no valen: hay que")
    print("    reactivar cada equipo.")
    return 1


def ruta_bd() -> str:
    """Ruta real del archivo de base de datos, para que no haya dudas de cual se usa."""
    from app.config import get_settings

    url = get_settings().DATABASE_URL
    return url[len("sqlite:///") :] if url.startswith("sqlite:///") else url


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--id", type=int, default=None, help="id del equipo a promover")
    parser.add_argument("--lista", action="store_true", help="solo listar los equipos")
    parser.add_argument("--todos", action="store_true", help="promover TODOS los equipos")
    args = parser.parse_args()

    init_db()
    db = SessionLocal()

    try:
        equipos = list(db.scalars(select(Machine).order_by(Machine.id)).all())

        if not equipos:
            return sin_equipos()

        print(f"Base de datos: {ruta_bd()}\n")
        print(f"Equipos registrados: {len(equipos)}\n")
        for m in equipos:
            print(texto_equipo(m))
        print()

        if args.lista:
            return 0

        if args.todos:
            elegidos = equipos
        elif args.id is not None:
            elegidos = [m for m in equipos if m.id == args.id]
            if not elegidos:
                print(f"ERROR: no existe un equipo con id={args.id}", file=sys.stderr)
                return 1
        elif len(equipos) == 1:
            elegidos = equipos
        else:
            print(
                "Hay varios equipos. Elige uno con  --id N  o promueve todos\n"
                "con  --todos",
                file=sys.stderr,
            )
            return 1

        for m in elegidos:
            antes = m.tier
            m.tier = Tier.INTERNAL.value
            m.revoked = False
            m.revoked_reason = None
            m.trial_expires_at = None   # el tier interno no vence
            m.license_id = None         # deja de depender de una suscripcion
            print(f"Equipo id={m.id} ({m.hostname or '?'}): {antes} -> INTERNAL")

        db.commit()

        print()
        print("=" * 62)
        print("LISTO. Licencia INTERNA PERMANENTE, sin fecha de vencimiento.")
        print("=" * 62)
        print()
        print("Ahora en la aplicacion, pestaña Licencia, pulsa 'Revalidar ahora'.")
        print("Debe decir: Licencia interna.")
        return 0

    finally:
        db.close()


if __name__ == "__main__":
    raise SystemExit(main())
