#!/usr/bin/env python3
"""Genera el par de llaves RSA para firmar licencias, sin necesitar OpenSSL.

Usa la librería `cryptography`, que ya se instala como dependencia de
`pyjwt[crypto]`. Pensado para Windows, donde OpenSSL rara vez está en el PATH.

    python scripts/generate_keys.py

Salida:
    keys/private.pem   SOLO en el servidor. Si se filtra, cualquiera puede
                       emitir licencias válidas.
    keys/public.pem    se embebe en el binario del cliente.

Ejecutar UNA sola vez. Si rotas las llaves, todos los clientes con la llave
pública anterior dejarán de validar y tendrán que actualizarse.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    from cryptography.hazmat.primitives import serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
except ImportError:
    print(
        "Falta la librería 'cryptography'. Instálala con:\n"
        "    pip install -r requirements.txt",
        file=sys.stderr,
    )
    raise SystemExit(1) from None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="./keys", help="Carpeta de salida (por defecto ./keys)")
    parser.add_argument(
        "--force",
        action="store_true",
        help="Sobrescribe llaves existentes. INVALIDA a todos los clientes ya instalados.",
    )
    args = parser.parse_args()

    key_dir = Path(args.out)
    private_path = key_dir / "private.pem"
    public_path = key_dir / "public.pem"

    if private_path.exists() and not args.force:
        print(
            f"ERROR: ya existe {private_path}\n"
            "Usa --force solo si estás seguro de querer invalidar a todos los\n"
            "clientes que ya tienen la llave pública anterior.",
            file=sys.stderr,
        )
        return 1

    key_dir.mkdir(parents=True, exist_ok=True)

    print("Generando llave RSA-2048...")
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)

    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    public_pem = private_key.public_key().public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )

    private_path.write_bytes(private_pem)
    public_path.write_bytes(public_pem)

    # En Linux/macOS restringe los permisos. En Windows no aplica: protege la
    # carpeta con los permisos NTFS de la cuenta que ejecuta el servicio.
    try:
        private_path.chmod(0o600)
    except (OSError, NotImplementedError):
        pass

    print(f"\nLlave privada: {private_path}")
    print(f"Llave pública: {public_path}")

    print("\n" + "!" * 70)
    print("RESPALDA LA CARPETA  server/keys/  AHORA MISMO.")
    print("!" * 70)
    print()
    print("Estas llaves son la identidad de tu licenciamiento. Si se pierden y")
    print("hay que generar otras, TODAS las licencias ya instaladas dejan de")
    print("valer: cada equipo de la oficina y cada cliente que paga tendria que")
    print("reactivarse. La base de datos no salva esto, porque los tokens que ya")
    print("estan en los equipos quedaron firmados con la llave anterior.")
    print()
    print("Copia  server/keys/  a un lugar seguro (disco externo o gestor de")
    print("contrasenas). NO la subas a un repositorio: con la llave privada")
    print("cualquiera puede emitir licencias validas.")

    print("\n" + "=" * 70)
    print("Copia el siguiente bloque en el cliente, en el archivo:")
    print("  client/src/CadLink.Licensing/EmbeddedPublicKey.cs")
    print("=" * 70)
    print(public_pem.decode().strip())
    print("=" * 70)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
