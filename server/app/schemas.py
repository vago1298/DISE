"""Contratos de entrada y salida de la API."""

from __future__ import annotations

import re
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field, field_validator

_FINGERPRINT_RE = re.compile(r"^[0-9a-f]{64}$")
_SID_RE = re.compile(r"^S-1-5-21(-\d+){3,}$")

#: Lo que se le quita a una huella antes de mirarla: espacios y guiones.
_SEPARADORES_HUELLA = re.compile(r"[\s\-]+")


def _normalizar_huella(v: str) -> str:
    """La huella en su forma canónica: 64 hexadecimales en minúsculas.

    **Se aceptan los guiones y las mayúsculas a propósito.** La aplicación ENSEÑA la
    huella agrupada de ocho en ocho y en mayúsculas —``A49A4785-BA02427A-…``, que es como
    se lee y se dicta sin equivocarse— mientras que el botón «Copiar huella» copia los 64
    caracteres de corrido. Quien la teclea desde la pantalla manda la primera forma, y
    antes el servidor la rechazaba con un *422* que hablaba del JSON y no de la huella:
    el usuario hacía lo obvio y el error no decía qué estaba mal.

    Así que aquí se normaliza. Lo que NO se tolera es otra cosa que no sean esos
    separadores: siguen teniendo que quedar exactamente 64 dígitos hexadecimales, ni uno
    más ni uno menos, así que una huella cortada o de otro equipo sigue siendo un error.
    """
    limpia = _SEPARADORES_HUELLA.sub("", (v or "").strip()).lower()

    if _FINGERPRINT_RE.match(limpia):
        return limpia

    sobran = sorted(set(re.sub(r"[0-9a-f]", "", limpia)))

    detalle = f"llegaron {len(limpia)} caracteres"

    if sobran:
        detalle += " y estos no son hexadecimales: " + " ".join(sobran)

    raise ValueError(
        "La huella tiene que ser el SHA-256 del equipo: 64 dígitos hexadecimales. "
        "Se admite tal como la enseña la aplicación, con guiones y mayúsculas. "
        f"Aquí {detalle}."
    )


class ActivateRequest(BaseModel):
    fingerprint: str = Field(..., description="SHA-256 en hexadecimal minúsculas")
    hostname: str | None = Field(default=None, max_length=200)
    os_user: str | None = Field(default=None, max_length=200)
    domain_sid: str | None = Field(default=None, max_length=200)
    app_version: str | None = Field(default=None, max_length=40)
    license_key: str | None = Field(default=None, max_length=64)

    @field_validator("fingerprint")
    @classmethod
    def _check_fingerprint(cls, v: str) -> str:
        return _normalizar_huella(v)

    @field_validator("domain_sid")
    @classmethod
    def _check_sid(cls, v: str | None) -> str | None:
        if v is None or v.strip() == "":
            return None
        v = v.strip().upper()
        if not _SID_RE.match(v):
            # No es error fatal: simplemente no se considerará para tier interno.
            return None
        return v


class RenewRequest(BaseModel):
    fingerprint: str
    app_version: str | None = None

    @field_validator("fingerprint")
    @classmethod
    def _check_fingerprint(cls, v: str) -> str:
        return _normalizar_huella(v)


class TokenResponse(BaseModel):
    token: str
    tier: str
    org: str
    expires_at: str
    license_expires_at: str | None
    grace_days: int


class MachineOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    fingerprint: str
    tier: str
    hostname: str | None
    os_user: str | None
    domain_sid: str | None
    app_version: str | None
    first_seen: datetime
    last_seen: datetime
    revoked: bool
    revoked_reason: str | None
    trial_expires_at: datetime | None
    license_id: int | None
    note: str | None


class MachineCreate(BaseModel):
    """Alta manual de un equipo, típicamente para marcarlo como INTERNAL."""

    fingerprint: str
    tier: str = "INTERNAL"
    note: str | None = None

    @field_validator("fingerprint")
    @classmethod
    def _check_fingerprint(cls, v: str) -> str:
        return _normalizar_huella(v)

    @field_validator("tier")
    @classmethod
    def _check_tier(cls, v: str) -> str:
        v = v.strip().upper()
        if v not in {"INTERNAL", "COMMERCIAL", "TRIAL"}:
            raise ValueError("Tier inválido")
        return v


class MachineUpdate(BaseModel):
    tier: str | None = None
    revoked: bool | None = None
    revoked_reason: str | None = None
    note: str | None = None


class LicenseCreate(BaseModel):
    org_name: str = Field(..., max_length=200)
    contact_email: str | None = None
    plan: str = "MONTHLY"
    seats: int = Field(default=1, ge=1, le=1000)
    billing_ref: str | None = None

    @field_validator("plan")
    @classmethod
    def _check_plan(cls, v: str) -> str:
        v = v.strip().upper()
        if v not in {"MONTHLY", "SEMIANNUAL", "ANNUAL"}:
            raise ValueError("Plan inválido: usa MONTHLY, SEMIANNUAL o ANNUAL")
        return v


class LicenseOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    key: str
    org_name: str
    contact_email: str | None
    plan: str
    seats: int
    status: str
    expires_at: datetime
    created_at: datetime
    billing_ref: str | None


class AuditOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    ts: datetime
    event: str
    fingerprint: str | None
    message: str | None
    client_ip: str | None
