#!/usr/bin/env python3
"""Da de alta la PC de un trabajador como equipo INTERNAL.

Úsalo cuando tu oficina NO tiene Active Directory, o para autorizar laptops que
no están unidas al dominio.

El usuario obtiene su huella desde la pantalla de activación de la aplicación
(botón "Copiar huella de este equipo") y te la envía.

Ejemplo:
    python scripts/register_machine.py \\
        --url https://licencias.miempresa.com \\
        --key "$ADMIN_API_KEY" \\
        --fingerprint a3f1... \\
        --note "Laptop de Juan Pérez - Proyectos"
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.error
import urllib.request


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", required=True, help="URL base del servidor de licencias")
    parser.add_argument("--key", required=True, help="ADMIN_API_KEY")
    parser.add_argument("--fingerprint", required=True, help="SHA-256 de 64 hex")
    parser.add_argument("--tier", default="INTERNAL", choices=["INTERNAL", "COMMERCIAL", "TRIAL"])
    parser.add_argument("--note", default=None, help="A quién pertenece el equipo")
    args = parser.parse_args()

    # Se aceptan los guiones y las mayusculas: la aplicacion ENSEÑA la huella agrupada de
    # ocho en ocho -A49A4785-BA02427A-...-, que es como se lee y se dicta, mientras que el
    # boton «Copiar huella» copia los 64 de corrido. Quien la recibe por telefono o por
    # correo manda la primera forma, y rechazarsela era hacerle buscar el error donde no
    # estaba. El servidor normaliza igual; esto es para avisar ANTES de la llamada.
    fingerprint = re.sub(r"[\s\-]+", "", args.fingerprint).lower()

    if not re.fullmatch(r"[0-9a-f]{64}", fingerprint):
        print(
            f"ERROR: la huella debe ser el SHA-256 del equipo: 64 digitos hexadecimales.\n"
            f"       Quedaron {len(fingerprint)} caracteres utiles despues de quitar "
            f"espacios y guiones.",
            file=sys.stderr,
        )
        return 2

    body = json.dumps(
        {"fingerprint": fingerprint, "tier": args.tier, "note": args.note}
    ).encode()

    req = urllib.request.Request(
        f"{args.url.rstrip('/')}/admin/machines",
        data=body,
        method="POST",
        headers={"Content-Type": "application/json", "X-Admin-Key": args.key},
    )

    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.load(resp)
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode(errors="replace")
        print(f"ERROR {exc.code}: {detail}", file=sys.stderr)
        return 1
    except urllib.error.URLError as exc:
        print(f"ERROR de conexión: {exc.reason}", file=sys.stderr)
        return 1

    print(f"Equipo registrado con id={data['id']} tier={data['tier']}")
    print(f"  {data.get('note') or '(sin nota)'}")
    print("\nPide al usuario que abra la aplicación: se activará automáticamente.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
