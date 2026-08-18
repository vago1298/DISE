"""Sesión y motor de base de datos."""

from collections.abc import Iterator

from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from .config import get_settings
from .models import Base

_settings = get_settings()

# check_same_thread solo aplica a SQLite; permite el uso desde el pool de FastAPI.
_connect_args = (
    {"check_same_thread": False} if _settings.DATABASE_URL.startswith("sqlite") else {}
)

engine = create_engine(
    _settings.DATABASE_URL,
    connect_args=_connect_args,
    pool_pre_ping=True,
)

SessionLocal = sessionmaker(bind=engine, autoflush=False, expire_on_commit=False)


def init_db() -> None:
    """Crea las tablas si no existen.

    Para cambios de esquema en producción usa Alembic en lugar de create_all.
    """
    Base.metadata.create_all(bind=engine)


def get_db() -> Iterator[Session]:
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
