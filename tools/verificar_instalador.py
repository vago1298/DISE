"""
Comprueba el INSTALADOR: lo que no se puede probar sin Windows, pero si se puede leer.

Aqui no hay Windows ni Inno Setup, asi que no se puede compilar el paquete. Lo que si se
puede es vigilar las cosas que, si se rompen, no se descubren hasta que el instalador ya
esta en la computadora de un cliente:

  1. QUE LA LLAVE PRIVADA NO SE EMPAQUETE. Firma las licencias. Si se cuela en el
     instalador, cualquier cliente puede emitirse licencias validas y el cobro deja de
     servir. No se arregla con un parche: hay que generar otro par de llaves, y eso
     invalida a TODOS los clientes que ya instalaron.

  2. QUE LA VERSION NO SE DESDOBLE. La version vive en el .csproj. Si el guion del
     instalador trae otra escrita a mano, el cliente acaba con un «CadLink 1.0.0» en
     Agregar o quitar programas que en realidad es otra cosa, y el servidor de licencias
     registra una version que no es la que corre.

  3. QUE NO SE PISE LO QUE EL CLIENTE EDITO. La configuracion y los dos catalogos crecen
     en la maquina del cliente. Copiarlos encima en cada actualizacion le borra el trabajo
     sin avisar.

  4. QUE EL AppId SIGA SIENDO EL MISMO. Es con lo que Windows sabe que la version nueva es
     la misma aplicacion. Cambiarlo deja dos CadLink instalados a la vez.

  5. QUE NO SE REPARTA APUNTANDO A localhost. El localhost del cliente es su maquina: no
     podria activar nunca.
"""

import os
import re

fallos = []


def check(nombre, ok, detalle=""):
    if ok:
        print(f"  OK    {nombre}")
    else:
        print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
        fallos.append(nombre)


RAIZ = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def leer(*partes):
    ruta = os.path.join(RAIZ, *partes)

    if not os.path.exists(ruta):
        return ""

    with open(ruta, encoding="utf-8", errors="replace") as f:
        return f.read()


def prosa(t):
    """
    El texto con los saltos de renglon y las marcas de comentario aplanados.

    Hace falta porque tanto el guion de Inno como el contrato parten las frases en varios
    renglones -y el guion las parte ademas con la barra de continuacion-. Buscar la frase
    tal cual fallaba por un salto de linea, que es un falso positivo de los que hacen
    desconfiar de las comprobaciones.
    """
    return re.sub(r"[\s;\\]+", " ", t)


ISS = leer("installer", "CadLink.iss")
BAT = leer("6-crear-instalador.bat")
CSPROJ = leer("client", "src", "CadLink.App", "CadLink.App.csproj")
EULA = leer("installer", "LICENCIA.txt")
DOC = leer("docs", "distribucion.md")
IGNORE = leer(".gitignore")

print()
print("=" * 78)
print("EL PAQUETE EXISTE Y SE ARMA CON UN SOLO COMANDO")
print("=" * 78)

check("hay guion de instalador", bool(ISS))
check("y un solo comando que lo arma", bool(BAT))

#  AUTOCONTENIDO: es lo que hace que el cliente no tenga que instalar .NET. Sin esto, el
#  instalador pesa poco y no arranca en ninguna maquina que no sea la del que lo hizo.
check("la aplicacion se publica autocontenida, sin exigirle .NET al cliente",
      "--self-contained true" in BAT
      and "-r win-x64" in BAT
      and "<PublishSingleFile>true</PublishSingleFile>" in CSPROJ)

#  LA CARPETA SE BORRA ANTES DE PUBLICAR: si quedan archivos de una version anterior, el
#  comodin del guion se los lleva al cliente.
check("se publica en limpio, sin restos de la version anterior",
      'if exist "%PUBLICADO%" rd /s /q "%PUBLICADO%"' in BAT)

print()
print("=" * 78)
print("LA LLAVE PRIVADA NO SALE DE AQUI")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  ESTE ES EL CHEQUEO IMPORTANTE DE TODO EL ARCHIVO.
#
#  Un instalador con la llave privada dentro no es un error que se parchee: hay que
#  generar otro par de llaves, y como la publica va embebida en los ejecutables ya
#  repartidos, eso invalida a todos los clientes instalados.
#  ═══════════════════════════════════════════════════════════════════════════════════
m_ex = re.search(r"Excludes:\s*\"([^\"]+)\"", ISS)
excluidos = m_ex.group(1).lower() if m_ex else ""

check("el guion excluye las llaves privadas",
      "*.pem" in excluidos and "*.key" in excluidos,
      f"Excludes = {excluidos!r}")

check("y la base de licencias, que trae los datos de los clientes",
      "*.db" in excluidos and "*.sqlite" in excluidos)

#  LOS SIMBOLOS facilitan desarmar el programa y ademas pesan. Se siguen generando, para
#  poder leer un informe de error, pero se quedan en la maquina de quien compila.
check("y los simbolos de depuracion", "*.pdb" in excluidos)

#  NI UNA MENCION a la carpeta de llaves ni al servidor: el instalador es de la APP.
check("el guion no mira la carpeta de llaves ni la del servidor",
      "server\\keys" not in ISS.lower()
      and "private" not in ISS.lower()
      and re.search(r'Source:\s*"[^"]*server', ISS) is None)

#  Y EL REPOSITORIO NO SE LLEVA LOS INSTALADORES ARMADOS: pesan unos 80 MB cada uno y
#  GitHub rechaza los de mas de 100.
check("los instaladores armados no se suben al repositorio",
      re.search(r"^dist/\s*$", IGNORE, re.M) is not None)

print()
print("=" * 78)
print("LA VERSION SALE DE UN SOLO SITIO")
print("=" * 78)

m_ver = re.search(r"<Version>([\d.]+)</Version>", CSPROJ)
version = m_ver.group(1) if m_ver else ""

check("el .csproj declara la version", bool(version), version)

#  EL .bat LA LEE DEL .csproj y se la pasa al guion. Escribirla en dos sitios es como se
#  acaba repartiendo un «1.0.0» que en realidad es otra cosa.
check("el .bat la lee del .csproj, no la trae escrita",
      'for /f "tokens=3 delims=<>" %%v in (\'findstr /c:"<Version>"' in BAT
      and "/DVersion=%VER%" in BAT)

#  Y EL VALOR POR OMISION DEL GUION COINCIDE, para el caso de compilarlo a mano.
m_def = re.search(r'#define\s+Version\s+"([\d.]+)"', ISS)
check("y el valor por omision del guion coincide con el .csproj",
      m_def is not None and m_def.group(1) == version,
      f"guion = {m_def.group(1) if m_def else None}, csproj = {version}")

check("el nombre del archivo lleva la version",
      "OutputBaseFilename={#Nombre}-Setup-{#Version}" in ISS)

print()
print("=" * 78)
print("LO QUE EL CLIENTE EDITO NO SE PISA")
print("=" * 78)

#  La configuracion lleva la direccion del servidor y la ruta de SU logo; los catalogos
#  crecen con los perfiles y los aceros que el agrega.
ISS_PLANO = prosa(ISS)

for archivo in ("cadlink.config.json", "perfiles-acero.csv", "aceros.csv"):
    #  Del  Source:  de este archivo hasta el siguiente: ahi viven sus Flags.
    i = ISS_PLANO.find(archivo + '"')
    j = ISS_PLANO.find("Source:", i + 1)
    regla = ISS_PLANO[i:j if j > i else len(ISS_PLANO)] if i >= 0 else ""

    check(f"{archivo} solo se copia si no existe",
          "onlyifdoesntexist" in regla, regla.strip()[:90])

    #  Y ADEMAS FUERA DEL COMODIN, o se copiaria dos veces y la segunda pisaria.
    check(f"y {archivo} esta excluido del comodin",
          archivo.lower() in excluidos)

#  LA LICENCIA ACTIVADA SE QUEDA: reinstalar no debe obligar a reactivar ni gastar otra
#  activacion en el servidor.
check("el guion dice por que no se borra la licencia al desinstalar",
      "license.dat" in ISS and "Liberar este equipo" in ISS_PLANO)

print()
print("=" * 78)
print("EL PAQUETE SE INSTALA SIN PEDIR CONTRASENA")
print("=" * 78)

#  En un despacho el ingeniero casi nunca es administrador de su maquina. Un instalador que
#  pide contrasena se queda sin instalar hasta que pase el de sistemas.
check("no exige ser administrador",
      "PrivilegesRequired=lowest" in ISS)

check("pero el que lo sea puede instalarlo para todo el equipo",
      "PrivilegesRequiredOverridesAllowed=dialog" in ISS)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  EL AppId NO SE CAMBIA NUNCA MAS. Es con lo que Windows reconoce que la version nueva
#  es la MISMA aplicacion y la actualiza encima. Cambiarlo deja dos CadLink instalados,
#  dos accesos directos y dos entradas en Agregar o quitar programas.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("el AppId esta fijo y no es el de ejemplo de Inno",
      "AppId={{7B2C9E14-4A6D-4F58-9C31-2E8D5A0B7F63}" in ISS
      and "AppId={{XXXX" not in ISS)

check("el guion avisa de que el AppId no se cambia",
      "NO SE CAMBIA NUNCA MAS" in ISS)

check("se pide cerrar la app en lugar de obligar a reiniciar",
      "CloseApplications=yes" in ISS)

#  .NET 8 no corre en Windows 8.1 ni antes. Mas vale decirlo en el instalador, que lo
#  explica, que dejar que la app no arranque sin motivo aparente.
check("se declara el Windows minimo", "MinVersion=10.0" in ISS)

#  EL ICONO ES OPCIONAL: hoy no hay app.ico en el repositorio, y el guion tiene que
#  compilar igual en lugar de reventar por un archivo que falta.
check("el icono es opcional y no rompe la compilacion",
      "#if FileExists(" in ISS and "#ifdef HayIcono" in ISS)

print()
print("=" * 78)
print("NO SE REPARTE UN PAQUETE ROTO")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  LOS TRES FRENOS DEL .bat. Cada uno corresponde a una forma de repartir un instalador
#  que no puede funcionar en casa del cliente.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("se detiene si la configuracion apunta a localhost",
      ":config_localhost" in BAT
      and 'findstr /c:"servidorLicencias" "%CFG%" | findstr /c:"localhost"' in BAT)

check("y si el servidor de licencias no usa https",
      ":config_sin_tls" in BAT
      and 'findstr /c:"servidorLicencias" "%CFG%" | findstr /c:"https://"' in BAT)

check("y si falta la llave publica embebida",
      'findstr /c:"PEGA_AQUI_TU_LLAVE_PUBLICA"' in BAT and ":falta_llave" in BAT)

#  PERO SE PUEDE PROBAR EN CASA: si los frenos no tuvieran salida, no habria manera de
#  probar la instalacion sin montar el servidor publico primero.
check("pero se puede armar un paquete de prueba a proposito",
      'if /i "%~1"=="prueba"' in BAT
      and BAT.count("if defined PRUEBA goto :config_ok") == 2)

check("y el paquete de prueba se anuncia como tal",
      "MODO PRUEBA" in BAT)

print()
print("=" * 78)
print("LA FIRMA DE CODIGO")
print("=" * 78)

#  Desde 2023 los certificados nuevos no vienen como archivo: la llave privada tiene que
#  vivir en un token USB o en un HSM. Por eso hacen falta las dos formas.
check("se puede firmar con certificado en archivo y con token USB",
      "CADLINK_FIRMA_PFX" in BAT
      and "CADLINK_FIRMA_NOMBRE" in BAT
      and ":firmar_archivo" in BAT
      and ":firmar_almacen" in BAT)

#  SE FIRMAN LOS DOS, y en ese orden: si se firma solo el instalador, Windows marca el
#  .exe cuando el cliente lo abre.
check("se firman el ejecutable y el instalador",
      'call :firmar "%PUBLICADO%\\CadLink.exe"' in BAT
      and 'call :firmar "%SETUPEXE%"' in BAT
      and BAT.index('call :firmar "%PUBLICADO%\\CadLink.exe"')
          < BAT.index('call :firmar "%SETUPEXE%"'))

#  EL SELLO DE TIEMPO NO ES OPCIONAL: sin el, la firma deja de valer el dia que el
#  certificado expira, y los instaladores ya repartidos empiezan a dar el aviso azul.
check("con sello de tiempo", "/tr %SELLO%" in BAT and BAT.count("/tr %SELLO%") == 2)

#  Y SI NO HAY CERTIFICADO, se avisa en lugar de repartir en silencio algo que Windows va
#  a marcar como sospechoso.
check("y si no hay certificado, se avisa",
      ":sin_firmar" in BAT and "Windows protegio tu PC" in BAT)

print()
print("=" * 78)
print("LO QUE SE ENTREGA Y LO QUE FALTA, ESCRITO")
print("=" * 78)

check("hay un contrato de licencia y el instalador lo ensena",
      bool(EULA) and "LicenseFile=LICENCIA.txt" in ISS)

#  ES UNA PLANTILLA Y LO DICE. Un contrato de licencia de un programa de calculo
#  estructural lo revisa un abogado, no se copia de un repositorio.
EULA_PLANO = prosa(EULA)

check("y dice que es una plantilla que hay que revisar con un abogado",
      "NO ES ASESORIA LEGAL" in EULA_PLANO and "ABOGADO" in EULA_PLANO.upper())

#  LAS DOS CLAUSULAS QUE IMPORTAN EN UN PROGRAMA DE ESTRUCTURAS.
check("el contrato dice que el resultado lo revisa y lo firma un ingeniero",
      "revisado y firmado por un ingeniero" in EULA_PLANO)

check("y que ningun proyecto del cliente sale de su computadora",
      "NO se manda ningun proyecto" in EULA_PLANO)

check("hay guia de distribucion", bool(DOC))

#  LO QUE FALTA TIENE QUE ESTAR ESCRITO, o se descubre cobrando: el servidor de fabrica
#  regala una licencia interna permanente al PRIMER equipo que active, y en internet ese
#  primer equipo es un desconocido.
check("la guia avisa del ajuste que regala licencias en internet",
      "AUTO_INTERNAL_FIRST_MACHINE" in DOC)

#  Y DEL DESACUERDO ENTRE EL README -30 dias de prueba- Y EL CODIGO -1 dia-.
check("y del desacuerdo de los dias de prueba",
      "TRIAL_DAYS" in DOC)

#  Y DE QUE EL WEBHOOK DE PAGOS, TAL COMO ESTA, NO VERIFICA UNA FIRMA DE STRIPE.
check("y de que el webhook de pagos todavia no habla con una pasarela real",
      "_parse_event" in DOC and "Stripe-Signature" in DOC)

#  Y DE QUE «LIBERAR ESTE EQUIPO» NO LIBERA EL ASIENTO EN EL SERVIDOR.
check("y de que dar de baja un equipo no libera su asiento",
      "/v1/deactivate" in DOC)

print()
print("=" * 78)

if fallos:
    print(f"FALLARON {len(fallos)} COMPROBACIONES:")

    for f in fallos:
        print("  -", f)

    print("=" * 78)
    raise SystemExit(1)

print("OK: el instalador se arma solo, no se lleva secretos y no pisa al cliente.")
print("=" * 78)
