"""Modelo de datos del servidor de licencias."""

from __future__ import annotations

import enum
from datetime import datetime, timezone

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class Base(DeclarativeBase):
    pass


class Tier(str, enum.Enum):
    INTERNAL = "INTERNAL"
    COMMERCIAL = "COMMERCIAL"
    TRIAL = "TRIAL"


class LicenseStatus(str, enum.Enum):
    ACTIVE = "ACTIVE"
    SUSPENDED = "SUSPENDED"  # pago fallido, recuperable
    CANCELLED = "CANCELLED"  # cancelada definitivamente


class Plan(str, enum.Enum):
    MONTHLY = "MONTHLY"
    SEMIANNUAL = "SEMIANNUAL"
    ANNUAL = "ANNUAL"

    @property
    def days(self) -> int:
        return {"MONTHLY": 30, "SEMIANNUAL": 182, "ANNUAL": 365}[self.value]


class License(Base):
    """Licencia comercial de un cliente externo. Agrupa uno o varios equipos."""

    __tablename__ = "licenses"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    key: Mapped[str] = mapped_column(String(64), unique=True, index=True)
    org_name: Mapped[str] = mapped_column(String(200))
    contact_email: Mapped[str | None] = mapped_column(String(200), default=None)
    plan: Mapped[str] = mapped_column(String(20), default=Plan.MONTHLY.value)
    seats: Mapped[int] = mapped_column(Integer, default=1)
    status: Mapped[str] = mapped_column(
        String(20), default=LicenseStatus.ACTIVE.value, index=True
    )
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)

    # Referencia al identificador de suscripción en la pasarela de pagos,
    # para poder conciliar webhooks con licencias.
    billing_ref: Mapped[str | None] = mapped_column(String(200), default=None, index=True)

    machines: Mapped[list[Machine]] = relationship(back_populates="license")

    def is_usable(self, now: datetime | None = None) -> bool:
        now = now or utcnow()
        return self.status == LicenseStatus.ACTIVE.value and self.expires_at > now


class Machine(Base):
    """Un equipo físico identificado por su huella de hardware."""

    __tablename__ = "machines"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    fingerprint: Mapped[str] = mapped_column(String(64), unique=True, index=True)
    tier: Mapped[str] = mapped_column(String(20), index=True)

    hostname: Mapped[str | None] = mapped_column(String(200), default=None)
    os_user: Mapped[str | None] = mapped_column(String(200), default=None)
    domain_sid: Mapped[str | None] = mapped_column(String(200), default=None, index=True)
    app_version: Mapped[str | None] = mapped_column(String(40), default=None)

    first_seen: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
    last_seen: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)

    revoked: Mapped[bool] = mapped_column(Boolean, default=False, index=True)
    revoked_reason: Mapped[str | None] = mapped_column(String(300), default=None)

    # Solo para TRIAL
    trial_expires_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), default=None
    )

    license_id: Mapped[int | None] = mapped_column(
        ForeignKey("licenses.id"), default=None
    )
    license: Mapped[License | None] = relationship(back_populates="machines")

    note: Mapped[str | None] = mapped_column(Text, default=None)


class AuditLog(Base):
    """Bitácora de eventos. Indispensable para diagnosticar y detectar abuso."""

    __tablename__ = "audit_log"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    ts: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow, index=True)
    event: Mapped[str] = mapped_column(String(60), index=True)
    fingerprint: Mapped[str | None] = mapped_column(String(64), default=None, index=True)
    message: Mapped[str | None] = mapped_column(Text, default=None)
    client_ip: Mapped[str | None] = mapped_column(String(60), default=None)
