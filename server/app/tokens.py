"""Emisión de tokens de licencia firmados con RSA (RS256).

La llave privada nunca sale de este servidor. El cliente solo lleva la pública,
por lo que puede verificar tokens pero le es imposible fabricarlos.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone
from functools import lru_cache

import jwt

from .config import Settings, get_settings
from .models import License, Machine, Tier

ALGORITHM = "RS256"
ISSUER = "cadlink-license-server"


@lru_cache
def _private_key() -> str:
    settings = get_settings()
    path = settings.PRIVATE_KEY_PATH
    if not path.exists():
        raise RuntimeError(
            f"No se encontró la llave privada en {path}. "
            "Genérala con scripts/generate_keys.sh"
        )
    return path.read_text(encoding="utf-8")


def license_expiry_for(machine: Machine, license_: License | None) -> datetime | None:
    """Fin de la vigencia comercial. None = sin fecha de fin (caso INTERNAL)."""
    if machine.tier == Tier.INTERNAL.value:
        return None
    if machine.tier == Tier.COMMERCIAL.value:
        return license_.expires_at if license_ else None
    if machine.tier == Tier.TRIAL.value:
        return machine.trial_expires_at
    return None


def issue_token(
    machine: Machine,
    license_: License | None,
    settings: Settings | None = None,
) -> dict:
    """Construye y firma el token para un equipo. Devuelve el payload de respuesta."""
    settings = settings or get_settings()
    now = datetime.now(timezone.utc)

    ttl_days = settings.token_ttl_days(machine.tier)
    grace = settings.grace_days(machine.tier)
    expires_at = now + timedelta(days=ttl_days)
    license_expires_at = license_expiry_for(machine, license_)

    # El token NUNCA debe sobrevivir a la suscripción. Sin este tope, un cliente
    # que cancela el último día del periodo pagado seguiría trabajando el TTL
    # completo más los días de gracia.
    if license_expires_at is not None and license_expires_at < expires_at:
        expires_at = license_expires_at
        grace = 0  # tampoco se regala gracia después del fin de la suscripción

    # Nombre que verá el usuario en el splash
    if machine.tier == Tier.INTERNAL.value:
        org = settings.ORG_NAME
    elif license_ is not None:
        org = license_.org_name
    else:
        org = "Versión de prueba"

    claims = {
        "iss": ISSUER,
        "jti": str(uuid.uuid4()),
        "sub": machine.fingerprint,
        "tier": machine.tier,
        "org": org,
        "iat": int(now.timestamp()),
        "exp": int(expires_at.timestamp()),
        "license_expires_at": (
            int(license_expires_at.timestamp()) if license_expires_at else None
        ),
        "grace_days": grace,
        "features": features_for(machine.tier),
    }

    token = jwt.encode(claims, _private_key(), algorithm=ALGORITHM)

    return {
        "token": token,
        "tier": machine.tier,
        "org": org,
        "expires_at": expires_at.isoformat(),
        "license_expires_at": (
            license_expires_at.isoformat() if license_expires_at else None
        ),
        "grace_days": grace,
    }


def features_for(tier: str) -> list[str]:
    """Módulos habilitados por tier.

    Aquí es donde decides qué incluye cada nivel. Por ejemplo, que la prueba
    gratuita permita capturar y revisar pero NO generar el dibujo, que es lo que
    hace que valga la pena comprar.

    Los nombres deben coincidir con los que consulta el cliente en
    ``AplicarModulos``.
    """
    base = ["excel-import", "secciones-concreto", "export-dxf"]

    if tier == Tier.INTERNAL.value:
        return [*base, "etabs", "acero", "cimentacion", "conexiones", "diagnostics"]

    if tier == Tier.COMMERCIAL.value:
        return [*base, "etabs", "acero", "cimentacion", "conexiones"]

    # TRIAL: se captura y se revisa, pero no se dibuja ni se lee ETABS.
    return ["excel-import", "secciones-concreto"]
