"""Contratos de entrada y salida de la API."""

from __future__ import annotations

import re
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field, field_validator

_FINGERPRINT_RE = re.compile(r"^[0-9a-f]{64}$")
_SID_RE = re.compile(r"^S-1-5-21(-\d+){3,}$")


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
        v = v.strip().lower()
        if not _FINGERPRINT_RE.match(v):
            raise ValueError("La huella debe ser un SHA-256 de 64 caracteres hexadecimales")
        return v

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
        v = v.strip().lower()
        if not _FINGERPRINT_RE.match(v):
            raise ValueError("Huella inválida")
        return v


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
        v = v.strip().lower()
        if not _FINGERPRINT_RE.match(v):
            raise ValueError("Huella inválida")
        return v

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
