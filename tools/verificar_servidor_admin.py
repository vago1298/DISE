#!/usr/bin/env python3
"""
La puerta de los endpoints de administracion del servidor de licencias.

Es la parte del servidor que NO se puede probar a ojo: si la clave se deja de pedir
en un endpoint, todo sigue funcionando -mejor incluso, porque responde sin
autenticarse- y lo que queda es un servidor de licencias abierto a quien lo
encuentre. Asi que aqui se comprueba, endpoint por endpoint, que la puerta este.

    python tools/verificar_servidor_admin.py

Se lee el codigo con el modulo ast, sin importar nada: fastapi no hace falta para
comprobar que cada ruta declara su dependencia.

Lo que se vigila:

  1. TODA ruta de /admin exige require_admin. Ninguna excepcion.
  2. require_admin pide la clave como ESQUEMA DE SEGURIDAD -Security(APIKeyHeader)-,
     que es lo que hace aparecer el boton Authorize de /docs.
  3. Con auto_error=False, para que la cabecera ausente siga dando el 401 de siempre
     y no el 403 «Not authenticated» que responderia FastAPI por su cuenta.
  4. La cabecera se sigue llamando X-Admin-Key, que es la que usan los scripts, los
     ejemplos del README y cualquier integracion ya hecha.
  5. La comparacion sigue siendo en tiempo constante y aguanta la cabecera ausente.
  6. Los endpoints de la aplicacion -/v1/activate y /v1/renew- NO piden clave de
     administrador: los llama el cliente, y pedirsela seria repartir la clave con el
     programa.
"""

from __future__ import annotations

import ast
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVER = os.path.join(RAIZ, "server")

fallos: list[str] = []


def check(nombre: str, ok: bool, detalle: str = "") -> None:
    if ok:
        print(f"  OK    {nombre}")
        return

    print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
    fallos.append(f"{nombre}: {detalle}")


def leer(*partes: str) -> str:
    with open(os.path.join(SERVER, *partes), encoding="utf-8") as f:
        return f.read()


_ADMIN = leer("app", "admin.py")
_MAIN = leer("app", "main.py")
_REGISTRA = leer("scripts", "register_machine.py")

print("=" * 78)
print("LA PUERTA DE LOS ENDPOINTS DE ADMINISTRACION")
print("=" * 78)

arbol = ast.parse(_ADMIN)


def decoradores_de_ruta(fn: ast.FunctionDef) -> list[str]:
    """Las rutas que declara una funcion: @router.post("/machines") y compania."""
    rutas = []

    for d in fn.decorator_list:
        if not isinstance(d, ast.Call) or not isinstance(d.func, ast.Attribute):
            continue

        if not isinstance(d.func.value, ast.Name) or d.func.value.id != "router":
            continue

        metodo = d.func.attr.upper()
        camino = d.args[0].value if d.args and isinstance(d.args[0], ast.Constant) else "?"
        rutas.append(f"{metodo} {camino}")

    return rutas


def pide_admin(fn: ast.FunctionDef) -> bool:
    """¿Alguno de sus parametros es Depends(require_admin)?"""
    for valor in list(fn.args.defaults) + [d for d in fn.args.kw_defaults if d]:
        if (isinstance(valor, ast.Call) and isinstance(valor.func, ast.Name)
                and valor.func.id in {"Depends", "Security"}
                and valor.args and isinstance(valor.args[0], ast.Name)
                and valor.args[0].id == "require_admin"):
            return True

    return False


rutas_totales = 0
sin_puerta: list[str] = []

for nodo in ast.walk(arbol):
    if not isinstance(nodo, ast.FunctionDef):
        continue

    rutas = decoradores_de_ruta(nodo)

    if not rutas:
        continue

    rutas_totales += len(rutas)

    if not pide_admin(nodo):
        sin_puerta += [f"{r} ({nodo.name})" for r in rutas]

check("se leyeron las rutas de administracion", rutas_totales >= 9,
      f"{rutas_totales} rutas")

check("TODAS las rutas de /admin exigen la clave", not sin_puerta,
      "; ".join(sin_puerta))

#  ---- 2. LA CLAVE, COMO ESQUEMA DE SEGURIDAD ----
#  Es lo unico que hace aparecer el boton Authorize: una cabecera suelta -Header(...)-
#  obliga a pegar la clave en cada uno de los nueve endpoints, y eso se hace mal.
check("la clave se declara como esquema de seguridad, no como cabecera suelta",
      "from fastapi.security import APIKeyHeader" in _ADMIN
      and "clave_admin = APIKeyHeader(" in _ADMIN
      and "x_admin_key: str | None = Security(clave_admin)" in _ADMIN)

check("y ya no queda la cabecera suelta del administrador",
      'x_admin_key: str = Header(default="")' not in _ADMIN)

check("el esquema tiene nombre y explicacion, para que el candado se entienda",
      'scheme_name="Clave de administrador"' in _ADMIN
      and "ADMIN_API_KEY" in _ADMIN)

#  ---- 3. auto_error=False: el 401 de siempre ----
check("con auto_error=False, la cabecera ausente sigue dando el 401 de siempre",
      "auto_error=False" in _ADMIN
      and "HTTP_401_UNAUTHORIZED" in _ADMIN
      and 'detail="Clave de administrador inválida."' in _ADMIN)

#  ---- 4. LA CABECERA NO CAMBIA DE NOMBRE ----
#  Los scripts y los ejemplos del README ya la usan. Cambiarla seria romper lo que
#  funciona para arreglar la documentacion.
check("la cabecera se sigue llamando X-Admin-Key",
      'CABECERA_ADMIN = "X-Admin-Key"' in _ADMIN
      and "name=CABECERA_ADMIN" in _ADMIN)

check("y el script de alta de equipos sigue mandando esa misma cabecera",
      '"X-Admin-Key": args.key' in _REGISTRA)

readme = ""
for nombre in ("README.md", "EMPIEZA-AQUI.md"):
    ruta = os.path.join(RAIZ, nombre)
    if os.path.exists(ruta):
        with open(ruta, encoding="utf-8") as f:
            readme += f.read()

check("y los ejemplos de la documentacion tambien",
      "X-Admin-Key" in readme)

#  ---- 5. TIEMPO CONSTANTE, Y SIN REVENTAR SIN CABECERA ----
#  compare_digest no admite None: sin el 'or ""', una peticion sin cabecera daba un 500
#  en lugar del 401, y un 500 en la puerta es informacion que se regala.
#  EL CUERPO SE SACA CON ast, no con un regex. Un «hasta el primer renglon en blanco»
#  se paraba dentro de la propia firma -que ya ocupa varias lineas- y dejaba fuera
#  justamente las tres lineas que aqui se comprueban: el verificador decia que faltaban
#  cosas que estaban escritas. Es el mismo aviso falso de siempre, esta vez en el server.
cuerpo = ""

for nodo in ast.walk(arbol):
    if isinstance(nodo, ast.FunctionDef) and nodo.name == "require_admin":
        cuerpo = ast.get_source_segment(_ADMIN, nodo) or ""
        break

check("se pudo leer require_admin", bool(cuerpo))

check("la comparacion sigue siendo en tiempo constante",
      "secrets.compare_digest(" in cuerpo)

check("y aguanta la cabecera ausente sin dar un 500",
      'x_admin_key or ""' in cuerpo)

check("sin ADMIN_API_KEY configurada NO se deja pasar a nadie",
      "if not expected" in cuerpo)

#  ---- 5-bis. LA HUELLA SE ACEPTA COMO LA ENSEÑA LA APLICACION ----
#  La aplicacion MUESTRA la huella agrupada de ocho en ocho y en mayusculas
#  -A49A4785-BA02427A-...-, que es como se lee y se dicta, mientras que el boton «Copiar
#  huella» copia los 64 de corrido. Quien la teclea desde la pantalla manda la primera
#  forma, y el servidor la rechazaba con un 422 que hablaba del JSON: el usuario hacia lo
#  obvio y el error no decia que estaba mal.
#
#  Se prueba DE VERDAD, no por literales: se saca la funcion del propio schemas.py y se le
#  pasan casos. Asi la comprobacion no puede quedarse verde con una normalizacion que no
#  normaliza.
_SCHEMAS = leer("app", "schemas.py")

entorno: dict = {"re": re}
arbol_schemas = ast.parse(_SCHEMAS)

for nodo in arbol_schemas.body:
    interesa = (
        isinstance(nodo, ast.Assign)
        and getattr(nodo.targets[0], "id", "") in {"_FINGERPRINT_RE", "_SEPARADORES_HUELLA"}
    ) or (
        isinstance(nodo, ast.FunctionDef) and nodo.name == "_normalizar_huella"
    )

    if interesa:
        exec(compile(ast.Module([nodo], []), "<schemas>", "exec"), entorno)  # noqa: S102

normaliza = entorno.get("_normalizar_huella")

check("se pudo leer la normalizacion de la huella", callable(normaliza))

if callable(normaliza):
    PLANA = "a49a4785ba02427aa79b87ffe4c9f678a74c8fa7b9e72a2a8cb0ba179c0b4524"
    AGRUPADA = ("A49A4785-BA02427A-A79B87FF-E4C9F678-"
                "A74C8FA7-B9E72A2A-8CB0BA17-9C0B4524")

    def acepta(valor: str) -> str | None:
        try:
            return normaliza(valor)
        except ValueError:
            return None

    check("la huella de corrido se acepta", acepta(PLANA) == PLANA)

    check("la huella COMO LA ENSEÑA LA APLICACION tambien",
          acepta(AGRUPADA) == PLANA, f"{acepta(AGRUPADA)}")

    check("y con espacios en lugar de guiones, por si se pega de un correo",
          acepta(AGRUPADA.replace("-", " ")) == PLANA)

    check("las mayusculas se bajan, que es la forma en que se guarda",
          acepta(PLANA.upper()) == PLANA)

    #  Y LO QUE NO ES UNA HUELLA SIGUE SIENDO UN ERROR. Es la otra mitad: una
    #  normalizacion que acepte cualquier cosa deja entrar la huella de otro equipo.
    for mala, porque in (
        ("", "vacia"),
        (PLANA[:-1], "un caracter de menos"),
        (PLANA + "ab", "dos de mas"),
        (PLANA[:-1] + "z", "un caracter que no es hexadecimal"),
        (PLANA[:-1] + "_", "un separador que no se tolera"),
    ):
        check(f"se rechaza una huella con {porque}", acepta(mala) is None)

    #  El mensaje tiene que hablar de la HUELLA y decir cuantos caracteres llegaron: el
    #  «JSON decode error» de antes mandaba a mirar el sitio equivocado.
    try:
        normaliza(PLANA[:-1])
        mensaje = ""
    except ValueError as exc:
        mensaje = str(exc)

    check("y el error dice que es la huella y cuantos caracteres llegaron",
          "huella" in mensaje.lower() and "63" in mensaje, mensaje)

check("los tres validadores usan la misma normalizacion",
      _SCHEMAS.count("return _normalizar_huella(v)") == 3,
      f"{_SCHEMAS.count('return _normalizar_huella(v)')} de 3")

check("y el script de alta acepta las dos formas igual que el servidor",
      're.sub(r"[\\s\\-]+", "", args.fingerprint).lower()' in _REGISTRA
      and 're.fullmatch(r"[0-9a-f]{64}", fingerprint)' in _REGISTRA)

#  ---- 6. LOS ENDPOINTS DEL CLIENTE NO PIDEN CLAVE DE ADMINISTRADOR ----
#  /v1/activate y /v1/renew los llama la aplicacion instalada. Pedirles la clave de
#  administrador significaria repartirla con el programa, que es lo contrario de
#  protegerla.
arbol_main = ast.parse(_MAIN)
publicas = []

for nodo in ast.walk(arbol_main):
    if not isinstance(nodo, ast.FunctionDef):
        continue

    for d in nodo.decorator_list:
        if (isinstance(d, ast.Call) and isinstance(d.func, ast.Attribute)
                and isinstance(d.func.value, ast.Name) and d.func.value.id == "app"
                and d.args and isinstance(d.args[0], ast.Constant)):
            publicas.append((d.args[0].value, nodo.name))

check("se leyeron los endpoints de la aplicacion", len(publicas) >= 3,
      f"{[p for p, _ in publicas]}")

check("ninguno pide la clave de administrador",
      "require_admin" not in _MAIN,
      "main.py menciona require_admin")

check("y estan los tres de siempre: salud, activar y renovar",
      {"/health", "/v1/activate", "/v1/renew"} <= {p for p, _ in publicas},
      f"{sorted(p for p, _ in publicas)}")

#  ---- Y LA DOCUMENTACION DICE COMO ENTRAR ----
check("la portada de /docs explica el boton Authorize",
      "**Authorize**" in _MAIN and "ADMIN_API_KEY" in _MAIN)

#  ---- 7. LA OFICINA TIENE QUE PODER ALCANZAR EL SERVIDOR ----
#  Es el defecto que se reporto: la huella dada de alta, el equipo como INTERNAL, y la PC
#  del trabajador «sin acceso». Uvicorn sin --host escucha SOLO en 127.0.0.1, asi que el
#  servidor funcionaba perfectamente en la computadora del dueño y ninguna otra lo
#  alcanzaba. Y desde la PC del trabajador, localhost es SU propia maquina.
with open(os.path.join(RAIZ, "2-iniciar-servidor.bat"), encoding="utf-8") as f:
    _BAT = f.read()

check("el servidor escucha en toda la red, no solo en esta computadora",
      "--host 0.0.0.0" in _BAT,
      "sin --host, uvicorn solo atiende a 127.0.0.1")

check("y dice al arrancar que direccion poner en las PCs de la oficina",
      "mi_direccion.py" in _BAT
      and os.path.exists(os.path.join(SERVER, "scripts", "mi_direccion.py")))

_DIR = leer("scripts", "mi_direccion.py")

check("esa ayuda da la URL ya armada para el archivo de configuracion",
      '"servidorLicencias"' in _DIR and "/health" in _DIR)

check("y dice como abrir el puerto en el firewall, que es el otro tropiezo",
      "netsh advfirewall" in _DIR and "localport=" in _DIR)

#  La IP se averigua con el socket UDP, que acierta con varias tarjetas -cable, WiFi,
#  VPN- donde gethostname() devuelve cualquiera.
check("la direccion se averigua por la tarjeta de salida, no por el nombre del equipo",
      "SOCK_DGRAM" in _DIR and "10.255.255.255" in _DIR)

#  Y el archivo de configuracion del cliente avisa de lo de localhost.
with open(os.path.join(RAIZ, "client", "src", "CadLink.App", "cadlink.config.json"),
          encoding="utf-8") as f:
    _CFG = f.read()

check("la configuracion del cliente avisa de que localhost es la propia PC",
      "OJO CON localhost" in _CFG and "192.168" in _CFG)

print()
print("=" * 78)

if fallos:
    print(f"FALLARON {len(fallos)} COMPROBACIONES:")

    for f in fallos:
        print(f"  - {f}")

    print("=" * 78)
    sys.exit(1)

print(f"OK: las {rutas_totales} rutas de administracion estan cerradas, y con candado")
print("    en /docs.")
print("=" * 78)
