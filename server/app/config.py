"""Configuración del servidor de licencias, cargada desde variables de entorno / .env"""

from functools import lru_cache
from pathlib import Path

from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Carpeta 'server/', o sea la de arriba de 'app/'. Sirve para anclar rutas y no
# depender del directorio desde el que se ejecute el proceso.
SERVER_DIR = Path(__file__).resolve().parent.parent

SQLITE = "sqlite:///"


def anclar_sqlite(url: str, base: Path = SERVER_DIR) -> str:
    """Convierte una ruta sqlite relativa en absoluta, anclada a ``base``.

    Se deja como función aparte, y no dentro del validador, para poder probarla
    sin depender de pydantic.

    Las rutas que ya son absolutas, ``:memory:`` y cualquier motor que no sea
    sqlite se devuelven sin tocar.
    """
    if not url.startswith(SQLITE):
        return url

    resto = url[len(SQLITE) :]

    if resto.startswith(":") or Path(resto).is_absolute():
        return url

    # Se quita el './' inicial para que no acabe como 'server/./cadlink.db'
    limpio = resto[2:] if resto.startswith("./") else resto
    return SQLITE + (base / limpio).as_posix()


def anclar_ruta(p: Path, base: Path = SERVER_DIR) -> Path:
    """Ancla una ruta relativa a ``base``."""
    return p if p.is_absolute() else (base / p)


class Settings(BaseSettings):
    # El .env también se ancla a 'server/': si se dejara relativo, arrancar el
    # servidor desde otra carpeta lo ignoraría en silencio y se usarían los
    # valores por omisión sin ningún aviso.
    model_config = SettingsConfigDict(
        env_file=SERVER_DIR / ".env", env_file_encoding="utf-8", extra="ignore"
    )

    # Identidad. Vacío = no se muestra ningún nombre de empresa en el cliente.
    ORG_NAME: str = ""

    # Infraestructura
    #
    # La ruta es ABSOLUTA, anclada a la carpeta 'server/'. Con la ruta relativa
    # './cadlink.db' que había antes, el archivo se creaba en el directorio de
    # trabajo del proceso: bastaba lanzar uvicorn desde otra carpeta para acabar
    # con DOS bases distintas y con la aplicación registrada en una mientras los
    # scripts de administración leían la otra, que aparecía vacía.
    DATABASE_URL: str = f"sqlite:///{(SERVER_DIR / 'cadlink.db').as_posix()}"
    PRIVATE_KEY_PATH: Path = SERVER_DIR / "keys" / "private.pem"

    # Auto-inscripción de equipos internos
    INTERNAL_DOMAIN_SID: str = ""
    INTERNAL_SEATS: int = 30

    # TTL del token (cada cuánto el equipo debe ver el servidor)
    TOKEN_TTL_INTERNAL_DAYS: int = 30
    TOKEN_TTL_COMMERCIAL_DAYS: int = 7
    TOKEN_TTL_TRIAL_DAYS: int = 7

    # Tolerancia sin conexión después de que el token expira
    GRACE_DAYS_INTERNAL: int = 21
    GRACE_DAYS_COMMERCIAL: int = 14
    GRACE_DAYS_TRIAL: int = 0

    # Pruebas gratuitas
    TRIAL_DAYS: int = 1
    ALLOW_AUTO_TRIAL: bool = True

    # El PRIMER equipo que se active recibe licencia INTERNA permanente.
    #
    # Sirve para el equipo del dueño: instalas el servidor, abres la aplicación y
    # tu máquina queda con licencia perpetua sin copiar huellas a mano.
    #
    # Solo aplica cuando la base de datos no tiene ningún equipo todavía, así que
    # se dispara una única vez. Conviene apagarlo antes de exponer el servidor a
    # internet, para que un desconocido no pueda ser "el primero".
    AUTO_INTERNAL_FIRST_MACHINE: bool = True

    # Administración
    ADMIN_API_KEY: str = ""
    PAYMENT_WEBHOOK_SECRET: str = ""

    # Los .env ya repartidos traen 'sqlite:///./cadlink.db', y un valor del .env
    # pisa el valor por omisión de la clase. Sin estos validadores, corregir el
    # default no arreglaría nada en las instalaciones que ya existen.
    @field_validator("DATABASE_URL")
    @classmethod
    def _db_absoluta(cls, v: str) -> str:
        return anclar_sqlite(v)

    @field_validator("PRIVATE_KEY_PATH")
    @classmethod
    def _llave_absoluta(cls, v: Path) -> Path:
        return anclar_ruta(v)

    def token_ttl_days(self, tier: str) -> int:
        return {
            "INTERNAL": self.TOKEN_TTL_INTERNAL_DAYS,
            "COMMERCIAL": self.TOKEN_TTL_COMMERCIAL_DAYS,
            "TRIAL": self.TOKEN_TTL_TRIAL_DAYS,
        }.get(tier, 1)

    def grace_days(self, tier: str) -> int:
        return {
            "INTERNAL": self.GRACE_DAYS_INTERNAL,
            "COMMERCIAL": self.GRACE_DAYS_COMMERCIAL,
            "TRIAL": self.GRACE_DAYS_TRIAL,
        }.get(tier, 0)


@lru_cache
def get_settings() -> Settings:
    return Settings()
