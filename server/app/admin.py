"""Endpoints de administración: alta de equipos internos, licencias, revocación.

Protegidos con la cabecera `X-Admin-Key`. Móntalos SIEMPRE detrás de HTTPS.
"""

from __future__ import annotations

import secrets
from datetime import timedelta

from fastapi import APIRouter, Depends, Header, HTTPException, Request, status
from sqlalchemy import desc, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from .config import Settings, get_settings
from .database import get_db
from .models import AuditLog, License, LicenseStatus, Machine, Plan, Tier, utcnow
from .schemas import (
    AuditOut,
    LicenseCreate,
    LicenseOut,
    MachineCreate,
    MachineOut,
    MachineUpdate,
)

router = APIRouter(prefix="/admin", tags=["administración"])


def require_admin(
    x_admin_key: str = Header(default=""),
    settings: Settings = Depends(get_settings),
) -> None:
    """Comparación en tiempo constante para no filtrar la clave por temporización."""
    expected = settings.ADMIN_API_KEY
    if not expected or not secrets.compare_digest(x_admin_key, expected):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED, detail="Clave de administrador inválida."
        )


# ---------------------------------------------------------------------------
# Equipos
# ---------------------------------------------------------------------------


@router.get("/machines", response_model=list[MachineOut])
def list_machines(
    tier: str | None = None,
    revoked: bool | None = None,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> list[Machine]:
    stmt = select(Machine).order_by(desc(Machine.last_seen))
    if tier:
        stmt = stmt.where(Machine.tier == tier.upper())
    if revoked is not None:
        stmt = stmt.where(Machine.revoked.is_(revoked))
    return list(db.scalars(stmt).all())


@router.post("/machines", response_model=MachineOut, status_code=status.HTTP_201_CREATED)
def create_machine(
    payload: MachineCreate,
    request: Request,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> Machine:
    """Pre-registra un equipo por su huella.

    Este es el mecanismo principal si NO tienes Active Directory: pides la huella
    al usuario (la aplicación la muestra en la pantalla de activación) y la das
    de alta aquí como INTERNAL.
    """
    machine = Machine(
        fingerprint=payload.fingerprint,
        tier=payload.tier,
        note=payload.note,
    )
    db.add(machine)
    db.add(
        AuditLog(
            event="MACHINE_PREREGISTERED",
            fingerprint=payload.fingerprint,
            message=f"Alta manual como {payload.tier}",
            client_ip=request.client.host if request.client else None,
        )
    )
    try:
        db.commit()
    except IntegrityError:
        db.rollback()
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Esa huella ya está registrada.",
        ) from None
    return machine


@router.patch("/machines/{machine_id}", response_model=MachineOut)
def update_machine(
    machine_id: int,
    payload: MachineUpdate,
    request: Request,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> Machine:
    machine = db.get(Machine, machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail="Equipo no encontrado.")

    changes: list[str] = []
    if payload.tier is not None:
        tier = payload.tier.upper()
        if tier not in {t.value for t in Tier}:
            raise HTTPException(status_code=400, detail="Tier inválido.")
        changes.append(f"tier {machine.tier}->{tier}")
        machine.tier = tier
    if payload.revoked is not None:
        changes.append(f"revoked {machine.revoked}->{payload.revoked}")
        machine.revoked = payload.revoked
        machine.revoked_reason = payload.revoked_reason
    if payload.note is not None:
        machine.note = payload.note

    db.add(
        AuditLog(
            event="MACHINE_UPDATED",
            fingerprint=machine.fingerprint,
            message="; ".join(changes) or "sin cambios",
            client_ip=request.client.host if request.client else None,
        )
    )
    db.commit()
    return machine


@router.post("/machines/{machine_id}/revoke", response_model=MachineOut)
def revoke_machine(
    machine_id: int,
    request: Request,
    reason: str = "Equipo dado de baja.",
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> Machine:
    """Da de baja un equipo.

    Recuerda: el equipo sigue funcionando con su token en cache hasta que expire
    (máximo el TTL de su tier). No es un apagado instantáneo.
    """
    machine = db.get(Machine, machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail="Equipo no encontrado.")
    machine.revoked = True
    machine.revoked_reason = reason
    db.add(
        AuditLog(
            event="MACHINE_REVOKED",
            fingerprint=machine.fingerprint,
            message=reason,
            client_ip=request.client.host if request.client else None,
        )
    )
    db.commit()
    return machine


# ---------------------------------------------------------------------------
# Licencias comerciales
# ---------------------------------------------------------------------------


def _new_license_key() -> str:
    """Clave legible y fácil de dictar por teléfono: XXXX-XXXX-XXXX-XXXX."""
    alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"  # sin I, O, 0, 1 para evitar confusión
    groups = [
        "".join(secrets.choice(alphabet) for _ in range(4)) for _ in range(4)
    ]
    return "-".join(groups)


@router.get("/licenses", response_model=list[LicenseOut])
def list_licenses(
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> list[License]:
    return list(db.scalars(select(License).order_by(desc(License.created_at))).all())


@router.post("/licenses", response_model=LicenseOut, status_code=status.HTTP_201_CREATED)
def create_license(
    payload: LicenseCreate,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> License:
    """Emite una licencia comercial. Devuelve la clave que entregas al cliente."""
    plan = Plan(payload.plan)
    license_ = License(
        key=_new_license_key(),
        org_name=payload.org_name,
        contact_email=payload.contact_email,
        plan=plan.value,
        seats=payload.seats,
        status=LicenseStatus.ACTIVE.value,
        expires_at=utcnow() + timedelta(days=plan.days),
        billing_ref=payload.billing_ref,
    )
    db.add(license_)
    db.add(AuditLog(event="LICENSE_CREATED", message=f"{license_.key} para {license_.org_name}"))
    db.commit()
    return license_


@router.post("/licenses/{key}/extend", response_model=LicenseOut)
def extend_license(
    key: str,
    periods: int = 1,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> License:
    """Extiende la vigencia por N periodos de su plan.

    Normalmente esto lo dispara el webhook de la pasarela de pagos, no una persona.
    """
    license_ = db.scalar(select(License).where(License.key == key))
    if license_ is None:
        raise HTTPException(status_code=404, detail="Licencia no encontrada.")

    plan = Plan(license_.plan)
    now = utcnow()
    # Si ya venció, cuenta desde hoy; si está vigente, se acumula al final.
    base = license_.expires_at if license_.expires_at > now else now
    license_.expires_at = base + timedelta(days=plan.days * periods)
    license_.status = LicenseStatus.ACTIVE.value

    db.add(
        AuditLog(
            event="LICENSE_EXTENDED",
            message=f"{key} extendida hasta {license_.expires_at.isoformat()}",
        )
    )
    db.commit()
    return license_


@router.post("/licenses/{key}/status", response_model=LicenseOut)
def set_license_status(
    key: str,
    new_status: str,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> License:
    license_ = db.scalar(select(License).where(License.key == key))
    if license_ is None:
        raise HTTPException(status_code=404, detail="Licencia no encontrada.")
    new_status = new_status.upper()
    if new_status not in {s.value for s in LicenseStatus}:
        raise HTTPException(
            status_code=400, detail="Estado inválido: ACTIVE, SUSPENDED o CANCELLED."
        )
    license_.status = new_status
    db.add(AuditLog(event="LICENSE_STATUS", message=f"{key} -> {new_status}"))
    db.commit()
    return license_


# ---------------------------------------------------------------------------
# Bitácora
# ---------------------------------------------------------------------------


@router.get("/audit", response_model=list[AuditOut])
def list_audit(
    limit: int = 200,
    event: str | None = None,
    db: Session = Depends(get_db),
    _: None = Depends(require_admin),
) -> list[AuditLog]:
    stmt = select(AuditLog).order_by(desc(AuditLog.ts)).limit(min(limit, 1000))
    if event:
        stmt = stmt.where(AuditLog.event == event.upper())
    return list(db.scalars(stmt).all())
