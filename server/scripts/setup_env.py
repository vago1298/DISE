#!/usr/bin/env python3
"""Crea el archivo .env con una clave de administrador segura ya generada.

Evita que el usuario tenga que inventar una clave aleatoria a mano, que es donde
la gente suele poner algo como "admin123" y dejar el servidor abierto a cualquiera.

Uso (desde la carpeta server):
    python scripts/setup_env.py
"""

from __future__ import annotations

import secrets
import shutil
import sys
from pathlib import Path


def main() -> int:
    server_dir = Path(__file__).resolve().parent.parent
    example = server_dir / ".env.example"
    target = server_dir / ".env"

    if target.exists():
        print(f"El archivo .env ya existe, no se toca: {target}")
        print("Si quieres empezar de cero, bórralo y vuelve a ejecutar esto.")
        return 0

    if not example.exists():
        print(f"ERROR: no encontré {example}", file=sys.stderr)
        return 1

    shutil.copy(example, target)

    admin_key = secrets.token_urlsafe(48)
    webhook_secret = secrets.token_urlsafe(32)

    text = target.read_text(encoding="utf-8")
    text = text.replace(
        'ADMIN_API_KEY="cambia-esto-por-una-clave-larga-y-aleatoria"',
        f'ADMIN_API_KEY="{admin_key}"',
    )
    text = text.replace(
        'PAYMENT_WEBHOOK_SECRET="cambia-esto-tambien"',
        f'PAYMENT_WEBHOOK_SECRET="{webhook_secret}"',
    )
    target.write_text(text, encoding="utf-8")

    print(f"Archivo .env creado: {target}")
    print("\nSe generó automáticamente tu clave de administrador:")
    print(f"\n    {admin_key}\n")
    print("Guárdala. La necesitarás para dar de alta equipos y emitir licencias.")
    print("También quedó escrita dentro del archivo .env, por si la pierdes.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
