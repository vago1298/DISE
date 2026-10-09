"""Servidor de licencias — endpoints públicos consumidos por la aplicación cliente."""

from __future__ import annotations

import hmac
import logging
from contextlib import asynccontextmanager
from datetime import datetime, timedelta

from fastapi import Depends, FastAPI, HTTPException, Request, status
from sqlalchemy import select
from sqlalchemy.orm import Session

from .admin import router as admin_router
from .config import Settings, get_settings
from .database import get_db, init_db
from .models import (
    AuditLog,
    EstadoSolicitud,
    License,
    Machine,
    SolicitudOficina,
    Tier,
    utcnow,
)
from .schemas import ActivateRequest, RenewRequest, TokenResponse
from .tokens import issue_token
from .oficina import router as oficina_router
from .webhooks import router as webhooks_router

log = logging.getLogger("cadlink.license")


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    settings = get_settings()
    if not settings.ADMIN_API_KEY or "cambia-esto" in settings.ADMIN_API_KEY:
        log.warning(
            "ADMIN_API_KEY no configurada o con el valor de ejemplo. "
            "Los endpoints /admin quedan expuestos a quien adivine la clave."
        )
    if not settings.INTERNAL_DOMAIN_SID:
        log.info(
            "INTERNAL_DOMAIN_SID vacío: la auto-inscripción de equipos internos está "
            "desactivada. Registra las huellas de tus PCs manualmente."
        )
    yield


app = FastAPI(
    title="CadLink License Server",
    version="1.0.0",
    description=(
        "Emisión y validación de licencias para CadLink.\n\n"
        "**Para usar los endpoints de administración**, pulsa primero **Authorize** "
        "(arriba a la derecha) y pega ahí la `ADMIN_API_KEY` del `.env` del servidor. "
        "Queda puesta en todos los `/admin/*` mientras dure la sesión del navegador.\n\n"
        "Los de `licencias` —`/v1/activate` y `/v1/renew`— los llama la aplicación y no "
        "llevan clave de administrador: van firmados con la huella del equipo."
    ),
    lifespan=lifespan,
)

app.include_router(admin_router)
app.include_router(webhooks_router)
app.include_router(oficina_router)


def _audit(
    db: Session,
    event: str,
    *,
    fingerprint: str | None = None,
    message: str | None = None,
    request: Request | None = None,
) -> None:
    db.add(
        AuditLog(
            event=event,
            fingerprint=fingerprint,
            message=message,
            client_ip=request.client.host if request and request.client else None,
        )
    )


@app.get("/health", tags=["infra"])
def health() -> dict:
    return {"status": "ok", "time": utcnow().isoformat()}


# ---------------------------------------------------------------------------
# Activación
# ---------------------------------------------------------------------------


@app.post("/v1/activate", response_model=TokenResponse, tags=["licencias"])
def activate(
    payload: ActivateRequest,
    request: Request,
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> TokenResponse:
    """Primera activación de un equipo, o re-activación tras perder el cache local.

    Decide el tier así:
      1. Si el equipo ya existe, respeta el tier que tenga asignado.
      2. Si trae clave de licencia válida  -> COMMERCIAL
      3. Si trae el código de oficina      -> INTERNAL (si hay asientos libres)
      4. Si el SID de dominio coincide     -> INTERNAL (si hay asientos libres)
      5. En cualquier otro caso            -> TRIAL (si está permitido)

    Un equipo que ya estaba en PRUEBA y llega con el código de oficina se promueve a
    INTERNAL: es lo que pasa al instalar el paquete de oficina encima de uno de prueba.
    """
    now = utcnow()
    machine = db.scalar(
        select(Machine).where(Machine.fingerprint == payload.fingerprint)
    )

    if machine is None:
        machine = _create_machine(payload, db, settings, request, now)
    else:
        _refresh_machine(machine, payload, now)
        # Si un equipo que estaba en prueba llega con clave de licencia, se promueve.
        if payload.license_key and machine.tier != Tier.COMMERCIAL.value:
            license_ = _resolve_license(payload.license_key, db)
            _check_seats(license_, db, exclude_machine_id=machine.id)
            machine.tier = Tier.COMMERCIAL.value
            machine.license_id = license_.id
            machine.trial_expires_at = None
            _audit(
                db,
                "TIER_UPGRADED",
                fingerprint=machine.fingerprint,
                message=f"Promovido a COMMERCIAL con licencia {license_.key}",
                request=request,
            )

    # EL CODIGO DE OFICINA NO AUTORIZA SOLO: deja la PC esperando a que el dueño la apruebe
    # en http://localhost:8000/oficina (8-aprobar-equipos.bat). Así, una copia del instalador
    # o de la carpeta instalada que acabe en una PC ajena no entra sin que él la vea.
    if (machine.tier == Tier.TRIAL.value and not machine.revoked and not payload.license_key
            and _es_codigo_de_oficina(payload.office_code, settings)):
        _tramitar_oficina(machine, payload, db, request, now)

    license_ = (
        db.get(License, machine.license_id) if machine.license_id is not None else None
    )

    _assert_usable(machine, license_, now)

    result = issue_token(machine, license_, settings)
    _audit(
        db,
        "ACTIVATED",
        fingerprint=machine.fingerprint,
        message=f"tier={machine.tier} host={machine.hostname}",
        request=request,
    )
    db.commit()
    return TokenResponse(**result)


@app.post("/v1/renew", response_model=TokenResponse, tags=["licencias"])
def renew(
    payload: RenewRequest,
    request: Request,
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> TokenResponse:
    """Renovación periódica. Es el punto donde surte efecto una revocación."""
    now = utcnow()
    machine = db.scalar(
        select(Machine).where(Machine.fingerprint == payload.fingerprint)
    )
    if machine is None:
        # El equipo no existe: obligar a pasar por /activate.
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Equipo no registrado. Se requiere activación.",
        )

    machine.last_seen = now
    if payload.app_version:
        machine.app_version = payload.app_version

    license_ = (
        db.get(License, machine.license_id) if machine.license_id is not None else None
    )
    _assert_usable(machine, license_, now)

    result = issue_token(machine, license_, settings)
    _audit(db, "RENEWED", fingerprint=machine.fingerprint, request=request)
    db.commit()
    return TokenResponse(**result)


# ---------------------------------------------------------------------------
# Lógica de decisión
# ---------------------------------------------------------------------------


def _create_machine(
    payload: ActivateRequest,
    db: Session,
    settings: Settings,
    request: Request,
    now: datetime,
) -> Machine:
    machine = Machine(
        fingerprint=payload.fingerprint,
        hostname=payload.hostname,
        os_user=payload.os_user,
        domain_sid=payload.domain_sid,
        app_version=payload.app_version,
        first_seen=now,
        last_seen=now,
        tier=Tier.TRIAL.value,
    )

    if payload.license_key:
        license_ = _resolve_license(payload.license_key, db)
        _check_seats(license_, db)
        machine.tier = Tier.COMMERCIAL.value
        machine.license_id = license_.id

    elif settings.AUTO_INTERNAL_FIRST_MACHINE and _no_hay_equipos(db):
        # Es el primer equipo que se registra: se asume que es el del dueño.
        machine.tier = Tier.INTERNAL.value
        _audit(
            db,
            "INTERNAL_PRIMER_EQUIPO",
            fingerprint=payload.fingerprint,
            message=(
                f"Primer equipo del sistema ({payload.hostname}): licencia INTERNA "
                "permanente. Apaga AUTO_INTERNAL_FIRST_MACHINE antes de exponer "
                "el servidor a internet."
            ),
            request=request,
        )

    elif _es_codigo_de_oficina(payload.office_code, settings):
        # El instalador de oficina: el equipo se registra SIN prueba y queda esperando a que
        # el dueño lo apruebe. La solicitud la abre _tramitar_oficina, en activate().
        machine.trial_expires_at = None

    elif _is_internal_domain(payload.domain_sid, settings):
        if _internal_seats_used(db) >= settings.INTERNAL_SEATS:
            _audit(
                db,
                "INTERNAL_SEATS_EXCEEDED",
                fingerprint=payload.fingerprint,
                message=(
                    f"Se alcanzó el tope de {settings.INTERNAL_SEATS} equipos internos. "
                    f"Equipo {payload.hostname} quedó en TRIAL."
                ),
                request=request,
            )
            machine.tier = Tier.TRIAL.value
            machine.trial_expires_at = now + timedelta(days=settings.TRIAL_DAYS)
        else:
            machine.tier = Tier.INTERNAL.value
            _audit(
                db,
                "INTERNAL_AUTOENROLL",
                fingerprint=payload.fingerprint,
                message=f"Auto-inscrito por SID de dominio: {payload.hostname}",
                request=request,
            )

    else:
        if not settings.ALLOW_AUTO_TRIAL:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Se requiere una clave de licencia para usar esta aplicación.",
            )
        machine.trial_expires_at = now + timedelta(days=settings.TRIAL_DAYS)

    db.add(machine)
    db.flush()  # asigna machine.id sin cerrar la transacción
    return machine


def _refresh_machine(machine: Machine, payload: ActivateRequest, now: datetime) -> None:
    machine.last_seen = now
    if payload.hostname:
        machine.hostname = payload.hostname
    if payload.os_user:
        machine.os_user = payload.os_user
    if payload.domain_sid:
        machine.domain_sid = payload.domain_sid
    if payload.app_version:
        machine.app_version = payload.app_version


def _is_internal_domain(domain_sid: str | None, settings: Settings) -> bool:
    configured = (settings.INTERNAL_DOMAIN_SID or "").strip().upper()
    if not configured or not domain_sid:
        return False
    return domain_sid.strip().upper() == configured


MENSAJE_PENDIENTE = (
    "ESPERANDO APROBACIÓN: este equipo ya pidió acceso a la oficina. Pide al "
    "administrador que lo apruebe en su computadora (8-aprobar-equipos.bat). "
    "En cuanto lo apruebe, esta pantalla entra sola."
)

MENSAJE_RECHAZADO = (
    "El administrador de la oficina RECHAZÓ este equipo. Si es un error, pídele que lo "
    "apruebe en 8-aprobar-equipos.bat y vuelve a abrir el programa."
)


def _tramitar_oficina(
    machine: Machine, payload: ActivateRequest, db: Session, request: Request, now: datetime
) -> None:
    """Abre o actualiza la solicitud de una PC con el código de oficina, y la deja esperando.

    Siempre termina en error HTTP, porque una PC en prueba con el código no recibe licencia
    hasta que el dueño la aprueba: entonces su tier pasa a INTERNAL y ya no entra aquí.
    """
    solicitud = db.scalar(
        select(SolicitudOficina).where(SolicitudOficina.machine_id == machine.id)
    )
    ip = request.client.host if request.client else None

    if solicitud is None:
        solicitud = SolicitudOficina(
            machine_id=machine.id,
            estado=EstadoSolicitud.PENDIENTE.value,
            hostname=payload.hostname,
            os_user=payload.os_user,
            client_ip=ip,
            creada=now,
            ultima_vez=now,
        )
        db.add(solicitud)
        _audit(
            db,
            "SOLICITUD_OFICINA",
            fingerprint=machine.fingerprint,
            message=f"{payload.hostname} ({payload.os_user}) pide acceso a la oficina",
            request=request,
        )
        log.warning(
            "NUEVA PC PIDIENDO ACCESO: %s (%s). Apruebala en http://localhost:8000/oficina",
            payload.hostname, payload.os_user,
        )
    else:
        solicitud.ultima_vez = now
        solicitud.hostname = payload.hostname or solicitud.hostname
        solicitud.os_user = payload.os_user or solicitud.os_user
        solicitud.client_ip = ip or solicitud.client_ip

    rechazada = solicitud.estado == EstadoSolicitud.RECHAZADA.value

    # Se guarda ANTES de contestar con error: una excepción sin commit se llevaría la
    # solicitud, y el dueño nunca la vería.
    db.commit()

    if rechazada:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail=MENSAJE_RECHAZADO)

    raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail=MENSAJE_PENDIENTE)


def _es_codigo_de_oficina(codigo: str | None, settings: Settings) -> bool:
    """¿Trae el código de oficina del servidor? Sin código configurado, nunca."""
    configurado = (settings.OFFICE_CODE or "").strip()
    if not configurado or not codigo:
        return False
    # compare_digest: el tiempo de la comparación no dice cuántas letras se acertaron.
    return hmac.compare_digest(codigo.strip().encode(), configurado.encode())


def _no_hay_equipos(db: Session) -> bool:
    """True si la base de datos todavía no tiene ningún equipo registrado."""
    return db.scalar(select(Machine).limit(1)) is None


def _internal_seats_used(db: Session) -> int:
    stmt = select(Machine).where(
        Machine.tier == Tier.INTERNAL.value, Machine.revoked.is_(False)
    )
    return len(db.scalars(stmt).all())


def _resolve_license(key: str, db: Session) -> License:
    license_ = db.scalar(select(License).where(License.key == key.strip()))
    if license_ is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Clave de licencia no reconocida.",
        )
    return license_


def _check_seats(
    license_: License, db: Session, exclude_machine_id: int | None = None
) -> None:
    stmt = select(Machine).where(
        Machine.license_id == license_.id, Machine.revoked.is_(False)
    )
    if exclude_machine_id is not None:
        stmt = stmt.where(Machine.id != exclude_machine_id)
    used = len(db.scalars(stmt).all())
    if used >= license_.seats:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=(
                f"La licencia ya tiene {used} de {license_.seats} equipos activos. "
                "Da de baja un equipo o amplía el número de asientos."
            ),
        )


def _assert_usable(machine: Machine, license_: License | None, now: datetime) -> None:
    """Rechaza equipos revocados, suscripciones vencidas y pruebas agotadas."""
    if machine.revoked:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=machine.revoked_reason or "Este equipo fue dado de baja.",
        )

    if machine.tier == Tier.COMMERCIAL.value:
        if license_ is None:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="El equipo no tiene una licencia asociada.",
            )
        if not license_.is_usable(now):
            raise HTTPException(
                status_code=status.HTTP_402_PAYMENT_REQUIRED,
                detail=(
                    "La suscripción está vencida o suspendida. "
                    "Renueva para seguir usando la aplicación."
                ),
            )

    elif machine.tier == Tier.TRIAL.value:
        if machine.trial_expires_at is None or machine.trial_expires_at <= now:
            raise HTTPException(
                status_code=status.HTTP_402_PAYMENT_REQUIRED,
                detail="El periodo de prueba terminó. Adquiere una suscripción para continuar.",
            )
