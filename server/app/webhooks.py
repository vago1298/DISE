"""Webhook genérico de pasarela de pagos.

Automatiza el ciclo de vida de la suscripción sin intervención humana:

    pago exitoso      -> extiende la vigencia
    pago fallido      -> suspende (recuperable)
    cancelación       -> cancela

Adapta `_parse_event` al formato de tu pasarela. La verificación de firma que se
muestra es HMAC-SHA256 sobre el cuerpo crudo, que es el patrón de Stripe y Paddle.
Revisa la documentación de tu proveedor: el formato exacto de la cabecera varía.
"""

from __future__ import annotations

import hashlib
import hmac
from datetime import timedelta

from fastapi import APIRouter, Depends, Header, HTTPException, Request, status
from sqlalchemy import select
from sqlalchemy.orm import Session

from .config import Settings, get_settings
from .database import get_db
from .models import AuditLog, License, LicenseStatus, Plan, utcnow

router = APIRouter(prefix="/webhooks", tags=["pagos"])


def _verify_signature(raw_body: bytes, signature: str, secret: str) -> bool:
    expected = hmac.new(secret.encode(), raw_body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, (signature or "").strip().lower())


@router.post("/payment")
async def payment_webhook(
    request: Request,
    x_signature: str = Header(default=""),
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> dict:
    raw = await request.body()

    if not settings.PAYMENT_WEBHOOK_SECRET:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="PAYMENT_WEBHOOK_SECRET no configurado.",
        )
    if not _verify_signature(raw, x_signature, settings.PAYMENT_WEBHOOK_SECRET):
        # No reveles detalles: un atacante no debe aprender nada del error.
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Firma inválida.")

    payload = await request.json()
    event_type, billing_ref = _parse_event(payload)

    if not billing_ref:
        return {"ok": True, "ignored": "sin billing_ref"}

    license_ = db.scalar(select(License).where(License.billing_ref == billing_ref))
    if license_ is None:
        # Responder 200 evita que la pasarela reintente indefinidamente por un
        # evento que nunca vamos a poder procesar.
        db.add(
            AuditLog(
                event="WEBHOOK_ORPHAN",
                message=f"{event_type} para billing_ref desconocido {billing_ref}",
            )
        )
        db.commit()
        return {"ok": True, "ignored": "licencia no encontrada"}

    if event_type in {"payment_succeeded", "subscription_renewed"}:
        plan = Plan(license_.plan)
        now = utcnow()
        base = license_.expires_at if license_.expires_at > now else now
        license_.expires_at = base + timedelta(days=plan.days)
        license_.status = LicenseStatus.ACTIVE.value
        db.add(
            AuditLog(
                event="WEBHOOK_EXTENDED",
                message=f"{license_.key} hasta {license_.expires_at.isoformat()}",
            )
        )

    elif event_type == "payment_failed":
        license_.status = LicenseStatus.SUSPENDED.value
        db.add(AuditLog(event="WEBHOOK_SUSPENDED", message=license_.key))

    elif event_type in {"subscription_cancelled", "subscription_expired"}:
        license_.status = LicenseStatus.CANCELLED.value
        db.add(AuditLog(event="WEBHOOK_CANCELLED", message=license_.key))

    else:
        db.add(AuditLog(event="WEBHOOK_UNHANDLED", message=event_type))

    db.commit()
    return {"ok": True, "event": event_type}


def _parse_event(payload: dict) -> tuple[str, str | None]:
    """Normaliza el evento de la pasarela a (tipo, referencia_de_facturación).

    AJUSTA ESTA FUNCIÓN a tu proveedor. Ejemplos de dónde vive cada dato:
      Stripe -> payload["type"], payload["data"]["object"]["subscription"]
      Paddle -> payload["event_type"], payload["data"]["subscription_id"]
    """
    event_type = payload.get("type") or payload.get("event_type") or "unknown"

    data = payload.get("data") or {}
    if not isinstance(data, dict):
        return str(event_type), None

    obj = data.get("object")
    if not isinstance(obj, dict):
        obj = data

    billing_ref = (
        obj.get("subscription") or obj.get("subscription_id") or obj.get("id")
    )
    return str(event_type), (str(billing_ref) if billing_ref else None)
