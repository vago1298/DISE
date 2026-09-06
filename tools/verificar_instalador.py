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
import struct

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
print("EL ICONO DEL EJECUTABLE")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  EL ICONO DEL ACCESO DIRECTO SALE DEL .exe, NO DEL INSTALADOR.
#
#  Sin  Assets\app.ico  el .csproj no pone ningun icono, el .exe se queda con el
#  generico de Windows, y el acceso directo del escritorio hereda ese generico. No hay
#  nada que se pueda arreglar en el instalador: el icono se incrusta al compilar.
#
#  Y UN .ico NO ES UNA IMAGEN, SON VARIAS: Windows toma 16 px para la barra de tareas,
#  32 para el escritorio, 48 para iconos medianos y 256 para la vista grande. Con una
#  sola medida, el sistema la reduce al vuelo y a 16 px queda una manchita.
#
#  Un .ico mal armado no da un icono feo: ROMPE LA COMPILACION. Por eso aqui se abre y
#  se revisa entrada por entrada.
#  ═══════════════════════════════════════════════════════════════════════════════════
RUTA_ICONO = os.path.join(RAIZ, "client", "src", "CadLink.App", "Assets", "app.ico")

check("hay icono para el ejecutable", os.path.exists(RUTA_ICONO))

ICO = b""

if os.path.exists(RUTA_ICONO):
    with open(RUTA_ICONO, "rb") as f:
        ICO = f.read()

entradas = []
problemas = []

if len(ICO) > 6:
    reservado, tipo, cuantas = struct.unpack("<HHH", ICO[:6])

    check("el encabezado dice que es un icono, no un cursor",
          reservado == 0 and tipo == 1, f"reservado={reservado} tipo={tipo}")

    for i in range(cuantas):
        crudo = ICO[6 + 16 * i:6 + 16 * i + 16]

        if len(crudo) < 16:
            problemas.append("directorio truncado")
            break

        w, h, colores, rsv, planos, bits, tam, desde = struct.unpack("<BBBBHHII", crudo)
        w = w or 256
        h = h or 256
        datos = ICO[desde:desde + tam]

        entradas.append(w)

        if len(datos) != tam:
            problemas.append(f"{w}px: la imagen se sale del archivo")
            continue

        if datos[:8] == b"\x89PNG\r\n\x1a\n":
            continue

        #  ENTRADA BMP: el encabezado declara el DOBLE de alto -imagen mas mascara de
        #  1 bit- y el tamano tiene que cuadrar al byte. Si no cuadra, Windows lee la
        #  mascara como pixeles y el icono sale negro o cortado.
        cabeza, bw, bh, bplanos, bbits = struct.unpack("<IiiHH", datos[:16])
        esperado = 40 + w * h * 4 + ((w + 31) // 32) * 4 * h

        if cabeza != 40 or bw != w or bh != 2 * h or bbits != 32:
            problemas.append(f"{w}px: encabezado {cabeza}/{bw}x{bh}/{bbits} bits")
        elif len(datos) != esperado:
            problemas.append(f"{w}px: {len(datos)} bytes, se esperaban {esperado}")

check("todas las entradas estan bien formadas",
      not problemas, "; ".join(problemas))

#  LAS CUATRO MEDIDAS QUE WINDOWS PIDE DE VERDAD.
faltantes = [m for m in (16, 32, 48, 256) if m not in entradas]

check("trae las medidas que Windows usa: 16, 32, 48 y 256",
      not faltantes, f"faltan {faltantes} - hay {sorted(entradas)}")

#  LA DE 256 COMPRIMIDA: sin comprimir son 256 KB de una sola entrada.
if 256 in entradas:
    i = entradas.index(256)
    _, _, _, _, _, _, tam256, desde256 = struct.unpack(
        "<BBBBHHII", ICO[6 + 16 * i:6 + 16 * i + 16])

    check("la de 256 va comprimida en PNG",
          ICO[desde256:desde256 + 8] == b"\x89PNG\r\n\x1a\n" and tam256 < 100_000,
          f"{tam256:,} bytes")

#  EL COMPILADOR LO INCRUSTA. La condicion Exists() esta para que el proyecto compile
#  aunque el icono no este, en lugar de fallar con un error que no dice nada.
check("el compilador lo incrusta en el .exe",
      "<ApplicationIcon Condition=\"Exists('Assets\\app.ico')\">Assets\\app.ico"
      "</ApplicationIcon>" in CSPROJ)

#  Y EL INSTALADOR USA EL MISMO, para que el propio setup.exe no salga generico.
check("y el instalador se pone el mismo icono",
      '#define RutaIcono "..\\client\\src\\CadLink.App\\Assets\\app.ico"' in ISS
      and "SetupIconFile={#RutaIcono}" in ISS)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  PARA PONER EL ICONO PROPIO NO HAY QUE EDITAR NADA: se copia el .ico a la carpeta
#  installer y el .bat lo toma con el nombre que tenga. Pedirle a alguien que renombre
#  un archivo a  app.ico  y lo meta cuatro carpetas adentro es pedirle que se
#  equivoque.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("el .bat toma el icono que uno deje en la carpeta installer",
      'for %%i in ("%RAIZ%installer\\*.ico")' in BAT
      and 'copy /y "%ICONOTUYO%" "%ICONOAPP%"' in BAT)

check("o junto a los propios .bat, que es donde uno lo suelta",
      'for %%i in ("%RAIZ%*.ico")' in BAT)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  Y SI NO, LA RUTA QUE YA ESTA EN LA CONFIGURACION.
#
#  Es la unica de las tres que SOBREVIVE A VOLVER A DESCARGAR EL ZIP, porque viaja
#  dentro del proyecto. Un archivo copiado a mano en installer se pierde en cuanto se
#  extrae una version nueva.
#
#  El .bat lo saca con cuatro sustituciones de cmd, que no se pueden ejecutar aqui. Lo
#  que si se puede es repetirlas en Python sobre la MISMA configuracion y comprobar que
#  el resultado es una ruta a un .ico: asi, si algun dia se cambia el formato del
#  archivo -o se le quita el espacio despues de los dos puntos- esto falla en lugar de
#  fallar callando en la maquina del usuario.
#  ═══════════════════════════════════════════════════════════════════════════════════
check("y si no, la ruta que ya trae la configuracion",
      'findstr /c:"logo" "%CFGFUENTE%" ^| findstr /v /c:"//"' in BAT
      and "set \"LINEA=%LINEA:*: =%\"" in BAT
      and 'set "ICONOTUYO=%ICONOTUYO:/=\\%"' in BAT)

CONFIG = leer("client", "src", "CadLink.App", "cadlink.config.json")


def icono_de_la_config(texto):
    """Las mismas cuatro sustituciones que hace el .bat, en Python."""
    #  findstr /c:"logo"  |  findstr /v /c:"//"      -distingue mayusculas-
    lineas = [l for l in texto.splitlines() if "logo" in l and "//" not in l]

    if not lineas:
        return None

    linea = lineas[0]
    corte = linea.find(": ")            # %LINEA:*: =%

    if corte < 0:
        return None

    linea = linea[corte + 2:].replace(",", "")   # %LINEA:,=%

    return linea.strip('"').replace("/", "\\")   # el for /f y %...:/=\%


RUTA_CFG = icono_de_la_config(CONFIG)

check("el filtro de comentarios deja un solo renglon, no un ejemplo del bloque de ayuda",
      len([l for l in CONFIG.splitlines() if "logo" in l and "//" not in l]) == 1,
      f"{[l.strip()[:40] for l in CONFIG.splitlines() if 'logo' in l and '//' not in l]}")

check("y de ese renglon sale una ruta a un .ico",
      RUTA_CFG is not None and RUTA_CFG.lower().endswith(".ico")
      and "\\" in RUTA_CFG and '"' not in RUTA_CFG,
      f"{RUTA_CFG}")

#  UN .png EN ESA CLAVE ES VALIDO PARA EL LOGO Y NO PARA EL ICONO, y eso se explica en
#  lugar de dejar al usuario pensando que su logo no se aplico.
check("un .png en esa clave se rechaza explicando la diferencia",
      ":icono_no_es_ico" in BAT and "para el icono del ejecutable hace falta un .ico" in BAT)

check("y una ruta que ya no existe tambien se avisa",
      ":icono_no_esta" in BAT and "no encuentro el icono que dice" in BAT)

check("y avisa si no hay ninguno, en lugar de repartir el generico callando",
      ":sin_icono" in BAT and "icono" in BAT and "generico de Windows" in BAT)

#  EL DIBUJO DEL MARCADOR DE POSICION NO SE REPITE: se importa del que ya existia. Con
#  el poligono escrito en dos archivos, el icono y el logo acabarian distintos.
GEN = leer("tools", "make_icon.py")

check("el icono de muestra se genera del mismo dibujo que el logo",
      "from make_placeholder_logo import" in GEN
      and "BOLT" in GEN
      and "MEDIDAS = (16, 24, 32, 48, 64, 128, 256)" in GEN)

print()
print("=" * 78)
print("LO QUE LEE WINDOWS: ASCII PURO Y CRLF")
print("=" * 78)

#  ═══════════════════════════════════════════════════════════════════════════════════
#  ESTO YA PASO, Y COSTO UN DIA.
#
#  El .bat se abria y se cerraba en menos de un segundo, sin mensaje, sin llegar a
#  ningun pause. El contenido estaba bien; lo que estaba mal eran los BYTES:
#
#    1. Tres caracteres no ASCII en unos comentarios. cmd.exe con  chcp 65001  lee el
#       archivo por POSICION DE BYTE y decodifica UTF-8: cuando la cuenta de bytes y la
#       de caracteres dejan de coincidir, un  goto  reanuda la lectura en el sitio
#       equivocado y el archivo termina de golpe. Los otros seis .bat del proyecto
#       tienen CERO bytes no ASCII, y no era casualidad.
#
#    2. Finales de renglon LF en lugar de CRLF, por lo mismo.
#
#  No se puede probar aqui -no hay cmd.exe-, asi que se comprueba lo unico que se
#  puede comprobar: los bytes.
#  ═══════════════════════════════════════════════════════════════════════════════════
DE_WINDOWS = sorted(
    f for f in os.listdir(RAIZ) if f.lower().endswith(".bat")
) + [os.path.join("installer", "CadLink.iss"), os.path.join("installer", "LICENCIA.txt")]

check("se encontraron los archivos que consume Windows",
      len(DE_WINDOWS) >= 9, f"{len(DE_WINDOWS)}: {DE_WINDOWS}")

con_acentos = []
con_lf = []

for nombre in DE_WINDOWS:
    with open(os.path.join(RAIZ, nombre), "rb") as f:
        crudo = f.read()

    if any(b > 126 for b in crudo):
        con_acentos.append(nombre)

    #  Un LF que no venga precedido de CR. Basta uno para descolocar la lectura.
    if crudo.replace(b"\r\n", b"").count(b"\n"):
        con_lf.append(nombre)

check("ninguno lleva un solo byte fuera de ASCII",
      not con_acentos, f"{con_acentos}")

check("y todos terminan los renglones con CRLF",
      not con_lf, f"{con_lf}")

#  Y QUE NO SE DESHAGA AL ENTREGARLO. Sin esto, git convierte los finales de renglon
#  segun quien clone y por donde: el zip de GitHub -que es por donde llegan de verdad-
#  no siempre aplica la conversion.
check("git no toca los finales de renglon de esos archivos",
      re.search(r"^\*\.bat\s+-text\s*$", leer(".gitattributes"), re.M) is not None
      and re.search(r"^\*\.iss\s+-text\s*$", leer(".gitattributes"), re.M) is not None)

check("y se explica por que, para que nadie lo 'arregle'",
      "POSICION DE BYTE" in leer(".gitattributes"))

print()
print("=" * 78)
print("SE PUEDE USAR CON DOBLE CLIC")
print("=" * 78)

#  AL DAR DOBLE CLIC EN UN .bat NO HAY FORMA DE PASARLE PARAMETROS. Pedir el modo por
#  parametro era pedir algo que el usuario no puede dar: se pregunta.
check("el modo se pregunta, no se exige por parametro",
      'set /p "OPCION=' in BAT
      and ":modo_elegido" in BAT
      and 'if "%OPCION%"=="1" set "PRUEBA=1"' in BAT)

check("pero desde la consola tambien se acepta el parametro",
      'if /i "%~1"=="prueba" set "PRUEBA=1"' in BAT)

#  Y SI SE ESCRIBE CUALQUIER OTRA COSA, se avisa y se para con pause: no se arma a
#  ciegas el paquete que no era.
check("una respuesta que no sea 1 ni 2 se rechaza",
      "No entendi" in BAT)

#  TODA SALIDA DE ERROR PASA POR pause. Si no, la ventana se cierra y el usuario no
#  alcanza a leer que fue lo que fallo, que es justo el problema que trajo todo esto.
check("todos los errores se quedan en pantalla con pause",
      BAT.count("pause") >= 2 and BAT.rstrip().endswith("exit /b 1")
      and "pause\nexit /b 1" in BAT.replace("\r\n", "\n"))

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
