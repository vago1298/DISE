"""La página para APROBAR las PCs de la oficina: http://localhost:8000/oficina

Las PCs que instalan el paquete de oficina llegan con el código de oficina y se quedan
esperando. Aquí el dueño las ve -nombre del equipo, usuario de Windows, desde dónde y
cuándo- y las aprueba o rechaza con un clic.

SOLO SE ABRE DESDE LA PROPIA COMPUTADORA DEL SERVIDOR. Por eso no pide la clave de
administrador: quien está sentado en ella ya es el dueño. Desde otra PC de la red contesta
403, aunque conozca la dirección.

Los botones llevan un testigo que cambia cada vez que arranca el servidor: así una página
ajena abierta en el navegador del dueño no puede aprobar equipos a sus espaldas.
"""

from __future__ import annotations

import html
import secrets
from datetime import timezone

from fastapi import APIRouter, Depends, HTTPException, Request, status
from fastapi.responses import HTMLResponse, RedirectResponse
from sqlalchemy import case, desc, select
from sqlalchemy.orm import Session

from .config import Settings, get_settings
from .database import get_db
from .models import AuditLog, EstadoSolicitud, Machine, SolicitudOficina, Tier, utcnow

router = APIRouter(tags=["oficina"], include_in_schema=False)

# Cambia en cada arranque. Va en la dirección de cada botón.
_TESTIGO = secrets.token_urlsafe(24)

_LOCALES = {"127.0.0.1", "::1", "localhost"}


def _solo_desde_aqui(request: Request) -> None:
    """Deja pasar solo a quien está en la computadora del servidor."""
    ip = request.client.host if request.client else ""

    # Un proxy en la misma máquina conectaría desde 127.0.0.1 en nombre de cualquiera: si
    # llega reenviada, no es de aquí.
    reenviada = any(h in request.headers for h in ("x-forwarded-for", "forwarded", "x-real-ip"))

    if ip not in _LOCALES or reenviada:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Esta página solo se abre en la computadora del servidor.",
        )


def _texto(v: object) -> str:
    return html.escape("" if v is None else str(v))


def _fecha(d) -> str:
    """En la hora de esta computadora. SQLite devuelve las fechas sin zona: son UTC."""
    if not d:
        return ""
    if d.tzinfo is None:
        d = d.replace(tzinfo=timezone.utc)
    return d.astimezone().strftime("%d/%m/%Y %H:%M")


@router.get("/oficina", response_class=HTMLResponse)
def pagina(
    request: Request,
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> HTMLResponse:
    _solo_desde_aqui(request)

    orden = case(
        (SolicitudOficina.estado == EstadoSolicitud.PENDIENTE.value, 0),
        else_=1,
    )
    filas = db.execute(
        select(SolicitudOficina, Machine)
        .join(Machine, Machine.id == SolicitudOficina.machine_id)
        .order_by(orden, desc(SolicitudOficina.ultima_vez))
        .limit(200)
    ).all()

    internas = len(
        db.scalars(
            select(Machine).where(
                Machine.tier == Tier.INTERNAL.value, Machine.revoked.is_(False)
            )
        ).all()
    )
    pendientes = sum(1 for s, _ in filas if s.estado == EstadoSolicitud.PENDIENTE.value)

    renglones = []
    for s, m in filas:
        if s.estado == EstadoSolicitud.PENDIENTE.value:
            estado = '<span class="pend">ESPERANDO</span>'
        elif s.estado == EstadoSolicitud.APROBADA.value:
            estado = '<span class="ok">APROBADA</span>'
        else:
            estado = '<span class="no">RECHAZADA</span>'

        if m.revoked:
            estado += ' <span class="no">(dada de baja)</span>'

        botones = []
        if s.estado != EstadoSolicitud.APROBADA.value or m.revoked:
            botones.append(
                f'<form method="post" action="/oficina/{s.id}/aprobar?t={_TESTIGO}">'
                '<button class="si">Aprobar</button></form>'
            )
        if s.estado != EstadoSolicitud.RECHAZADA.value:
            botones.append(
                f'<form method="post" action="/oficina/{s.id}/rechazar?t={_TESTIGO}" '
                "onsubmit=\"return confirm('¿Rechazar este equipo? Dejará de funcionar.')\">"
                '<button class="nop">Rechazar</button></form>'
            )

        renglones.append(
            "<tr>"
            f"<td><b>{_texto(s.hostname or m.hostname)}</b></td>"
            f"<td>{_texto(s.os_user or m.os_user)}</td>"
            f"<td>{_texto(s.client_ip)}</td>"
            f"<td>{_fecha(s.creada)}</td>"
            f"<td>{_fecha(s.ultima_vez)}</td>"
            f"<td>{estado}</td>"
            f'<td class="bt">{"".join(botones)}</td>'
            "</tr>"
        )

    tabla = (
        "<table><tr><th>Equipo</th><th>Usuario de Windows</th><th>Desde</th>"
        "<th>Pidió acceso</th><th>Última vez</th><th>Estado</th><th></th></tr>"
        + "".join(renglones)
        + "</table>"
        if renglones
        else "<p class='vacio'>Todavía ninguna PC ha pedido acceso. Instala el paquete de "
        "oficina en una PC, ábrelo, y aparecerá aquí.</p>"
    )

    sin_codigo = (
        "<p class='aviso'>El servidor no tiene CÓDIGO DE OFICINA: arma el paquete de oficina "
        "(6-crear-instalador.bat, opción 3) y reinicia el servidor.</p>"
        if not (settings.OFFICE_CODE or "").strip()
        else ""
    )

    pagina_html = f"""<!doctype html>
<html lang="es"><head><meta charset="utf-8">
<meta http-equiv="refresh" content="20">
<title>CadLink · PCs de la oficina</title>
<style>
 body {{ font-family: Segoe UI, Arial, sans-serif; margin: 28px; color: #1f2933; }}
 h1 {{ font-size: 22px; margin: 0 0 4px; }}
 .sub {{ color: #6b7280; margin-bottom: 18px; }}
 table {{ border-collapse: collapse; width: 100%; }}
 th, td {{ text-align: left; padding: 9px 10px; border-bottom: 1px solid #e5e7eb; }}
 th {{ background: #f3f4f6; font-weight: 600; }}
 .pend {{ color: #b45309; font-weight: 700; }}
 .ok {{ color: #047857; font-weight: 700; }}
 .no {{ color: #b91c1c; font-weight: 700; }}
 .bt form {{ display: inline; margin-right: 6px; }}
 button {{ border: 0; border-radius: 6px; padding: 7px 16px; font-size: 14px; cursor: pointer; }}
 .si {{ background: #047857; color: white; }}
 .nop {{ background: #e5e7eb; color: #1f2933; }}
 .vacio, .aviso {{ padding: 14px; background: #f9fafb; border-radius: 8px; }}
 .aviso {{ background: #fef3c7; }}
</style></head><body>
<h1>PCs de la oficina</h1>
<div class="sub">{pendientes} esperando · {internas} de {settings.INTERNAL_SEATS} equipos internos en uso ·
se actualiza sola cada 20 segundos</div>
{sin_codigo}
{tabla}
<p class="sub">Aprueba solo los equipos que reconozcas. Una PC aprobada queda con licencia
interna; si rechazas una que ya estaba aprobada, deja de funcionar en su próxima renovación.</p>
</body></html>"""

    return HTMLResponse(pagina_html)


def _decidir(
    solicitud_id: int, aprobar: bool, t: str, request: Request, db: Session, settings: Settings
) -> RedirectResponse:
    _solo_desde_aqui(request)

    if not secrets.compare_digest(t or "", _TESTIGO):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Botón vencido: recarga la página y vuelve a pulsarlo.",
        )

    solicitud = db.get(SolicitudOficina, solicitud_id)
    if solicitud is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No existe esa solicitud.")

    machine = db.get(Machine, solicitud.machine_id)
    if machine is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No existe ese equipo.")

    ahora = utcnow()

    if aprobar:
        ya_interna = machine.tier == Tier.INTERNAL.value and not machine.revoked
        usadas = len(
            db.scalars(
                select(Machine).where(
                    Machine.tier == Tier.INTERNAL.value, Machine.revoked.is_(False)
                )
            ).all()
        )
        if not ya_interna and usadas >= settings.INTERNAL_SEATS:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail=(
                    f"Ya hay {usadas} equipos internos, el tope de INTERNAL_SEATS. "
                    "Da de baja uno o sube el tope en server/.env."
                ),
            )

        machine.tier = Tier.INTERNAL.value
        machine.trial_expires_at = None
        machine.revoked = False
        machine.revoked_reason = None
        solicitud.estado = EstadoSolicitud.APROBADA.value
        evento, texto = "OFICINA_APROBADA", "aprobada"
    else:
        # Rechazar una que ya estaba aprobada la da de baja: deja de renovar.
        if machine.tier == Tier.INTERNAL.value:
            machine.revoked = True
            machine.revoked_reason = "El administrador de la oficina rechazó este equipo."
        solicitud.estado = EstadoSolicitud.RECHAZADA.value
        evento, texto = "OFICINA_RECHAZADA", "rechazada"

    solicitud.decidida = ahora
    db.add(
        AuditLog(
            event=evento,
            fingerprint=machine.fingerprint,
            message=f"{solicitud.hostname or machine.hostname}: {texto} desde la página de la oficina",
            client_ip=request.client.host if request.client else None,
        )
    )
    db.commit()

    return RedirectResponse("/oficina", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/oficina/{solicitud_id}/aprobar")
def aprobar(
    solicitud_id: int,
    request: Request,
    t: str = "",
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> RedirectResponse:
    return _decidir(solicitud_id, True, t, request, db, settings)


@router.post("/oficina/{solicitud_id}/rechazar")
def rechazar(
    solicitud_id: int,
    request: Request,
    t: str = "",
    db: Session = Depends(get_db),
    settings: Settings = Depends(get_settings),
) -> RedirectResponse:
    return _decidir(solicitud_id, False, t, request, db, settings)
