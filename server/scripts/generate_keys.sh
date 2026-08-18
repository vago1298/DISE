#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Genera el par de llaves RSA para firmar los tokens de licencia.
#
#   private.pem  -> SOLO en el servidor. Si se filtra, cualquiera puede emitir
#                   licencias válidas y tu esquema completo queda inservible.
#   public.pem   -> se embebe en el binario del cliente.
#
# Ejecutar una sola vez. Si rotas las llaves, TODOS los clientes con la llave
# pública anterior dejarán de validar y deberán actualizarse.
# ---------------------------------------------------------------------------
set -euo pipefail

KEY_DIR="${1:-./keys}"
mkdir -p "$KEY_DIR"

if [[ -f "$KEY_DIR/private.pem" ]]; then
  echo "ERROR: ya existe $KEY_DIR/private.pem"
  echo "Bórralo a mano solo si estás seguro de querer invalidar a todos los clientes."
  exit 1
fi

echo "Generando llave privada RSA-2048..."
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 \
  -out "$KEY_DIR/private.pem"

echo "Extrayendo llave pública..."
openssl rsa -pubout -in "$KEY_DIR/private.pem" -out "$KEY_DIR/public.pem"

chmod 600 "$KEY_DIR/private.pem"
chmod 644 "$KEY_DIR/public.pem"

echo
echo "Listo."
echo "  Privada: $KEY_DIR/private.pem  (chmod 600, nunca la subas al repo)"
echo "  Pública: $KEY_DIR/public.pem"
echo
echo "Siguiente paso: copia el contenido de public.pem a"
echo "  client/src/CadLink.Licensing/EmbeddedPublicKey.cs"
