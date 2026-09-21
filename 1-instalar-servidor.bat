@echo off
chcp 65001 >nul
title Paso 1 - Instalar el servidor de licencias
setlocal

cd /d "%~dp0"

echo ==========================================================
echo   PASO 1 de 3 - INSTALAR EL SERVIDOR DE LICENCIAS
echo ==========================================================
echo.
echo Esto se hace UNA SOLA VEZ. Puede tardar 2 o 3 minutos.
echo.

REM ============================================================
REM  Este archivo NO usa bloques con parentesis a proposito.
REM  Un parentesis dentro de un bloque IF hace que Windows crea
REM  que el bloque termino ahi. Aqui todo va con etiquetas y GOTO.
REM ============================================================


REM ---------- Localizar la raiz del proyecto ----------
set "RAIZ=%~dp0"
if exist "%RAIZ%server\requirements.txt" goto :raiz_ok

set "RAIZ=%~dp0cadlink\"
if exist "%RAIZ%server\requirements.txt" goto :raiz_ok

goto :no_proyecto

:raiz_ok
echo Carpeta del proyecto:
echo    %RAIZ%
echo.


REM ============================================================
REM  [1/6] BUSCAR UN PYTHON QUE SIRVA
REM
REM  SE PREFIERE UNA VERSION PROBADA, no la mas nueva. Las
REM  librerias del servidor se distribuyen compiladas -pydantic
REM  lleva su nucleo en Rust, SQLAlchemy en C- y esas ruedas
REM  tardan semanas o meses en aparecer para cada Python nuevo.
REM  Con un Python recien salido, pip no encuentra rueda, intenta
REM  compilar desde el codigo fuente, y sin Rust ni compilador de
REM  C eso termina en un error largo y confuso.
REM
REM  Paso de verdad en una PC con Python 3.14: el paso [3/6]
REM  fallaba y el servidor se quedaba sin librerias, sin llaves y
REM  sin .env.
REM
REM  Asi que se prueba primero con el lanzador "py" pidiendole
REM  3.13, 3.12 y 3.11 -las que llevan tiempo y tienen rueda de
REM  todo-, y solo si no hay ninguna se usa el Python que este en
REM  el PATH, sea el que sea. Con los rangos de requirements.txt
REM  tambien funciona en los mas nuevos; esto solo evita el camino
REM  largo cuando hay alternativa.
REM ============================================================
echo [1/6] Buscando Python...

set "PYEXE="

py -3.13 -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=py -3.13"
if defined PYEXE goto :python_ok

py -3.12 -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=py -3.12"
if defined PYEXE goto :python_ok

py -3.11 -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=py -3.11"
if defined PYEXE goto :python_ok

python -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=python"

if defined PYEXE goto :python_ok

REM El lanzador "py" a secas, que Windows instala en el directorio
REM del sistema. Muchas veces funciona aunque la casilla
REM "Add python.exe to PATH" no se haya marcado al instalar.
py -3 -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=py -3"

if defined PYEXE goto :python_ok
goto :sin_python

:python_ok
for /f "tokens=*" %%v in ('%PYEXE% --version 2^>^&1') do echo       Encontrado: %%v

REM  Y SE AVISA SI ES MAS NUEVO QUE EL ULTIMO PROBADO. No se para la
REM  instalacion -con los rangos de requirements.txt suele funcionar-
REM  pero si el paso [3/6] falla, el aviso de arriba ya dijo por donde
REM  mirar en lugar de dejar al usuario con un error de compilacion.
%PYEXE% -c "import sys; raise SystemExit(0 if sys.version_info[:2] <= (3, 13) else 1)" >nul 2>&1
if not errorlevel 1 goto :python_version_ok

echo.
echo       AVISO: este Python es mas nuevo que el ultimo con el que se
echo       probo esto (3.13). Se intenta igual. Si las librerias fallan
echo       en el paso 3, instala Python 3.13 desde python.org y vuelve
echo       a ejecutar este archivo: no hace falta desinstalar el otro.

:python_version_ok
echo.


REM ============================================================
REM  [2/6] ENTORNO AISLADO
REM
REM  NO BASTA CON QUE EXISTA: TIENE QUE FUNCIONAR.
REM
REM  Un entorno de Python guarda la RUTA ABSOLUTA del Python con
REM  el que se creo -en su pyvenv.cfg y dentro de sus propios
REM  .exe-. Asi que si la carpeta del proyecto se copia a otra
REM  computadora con el .venv adentro, ese .venv trae la ruta del
REM  usuario de la PRIMERA maquina y en la segunda no sirve:
REM
REM     did not find executable at
REM     'C:\Users\PC\AppData\Local\Programs\Python\Python313\python.exe'
REM
REM  Antes aqui solo se preguntaba si existia python.exe. El
REM  copiado lo tiene, asi que se daba por bueno, y los pasos
REM  siguientes fallaban uno tras otro con ese mensaje, que no
REM  dice nada de lo que de verdad pasa.
REM
REM  Ahora se le pide que ejecute algo. Si no puede, se rehace.
REM  Rehacerlo es seguro: en el .venv no hay nada del usuario
REM  -ni llaves, ni .env, ni base de datos-, solo librerias que se
REM  vuelven a bajar en el paso siguiente.
REM ============================================================
echo [2/6] Preparando el entorno de Python...

if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :venv_crear

"%RAIZ%server\.venv\Scripts\python.exe" -c "pass" >nul 2>&1
if not errorlevel 1 goto :venv_listo

echo       El entorno que habia NO funciona en esta computadora.
echo       Se copio de otra maquina -guarda la ruta de su Python-.
echo       Se rehace desde cero. No se pierde nada tuyo.
rmdir /s /q "%RAIZ%server\.venv"

:venv_crear
%PYEXE% -m venv "%RAIZ%server\.venv"
if errorlevel 1 goto :error_venv

if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :error_venv

REM Y se comprueba TAMBIEN el recien creado: si el Python de esta
REM maquina esta a medio instalar, el venv se crea y no arranca.
"%RAIZ%server\.venv\Scripts\python.exe" -c "pass" >nul 2>&1
if errorlevel 1 goto :error_venv

echo       Entorno creado.
goto :venv_fin

:venv_listo
echo       Ya existia y funciona, se reutiliza.

:venv_fin
set "PY=%RAIZ%server\.venv\Scripts\python.exe"
echo.


REM ---------- [3/6] Librerias ----------
echo [3/6] Descargando las librerias necesarias...
echo       Aqui es donde tarda. Ten paciencia.
echo.

"%PY%" -m pip install --upgrade pip --quiet --disable-pip-version-check
"%PY%" -m pip install -r "%RAIZ%server\requirements.txt" --disable-pip-version-check
if errorlevel 1 goto :error_pip

echo.
echo       Librerias instaladas.
echo.


REM ---------- [4/6] Llaves de firma ----------
echo [4/6] Generando las llaves de seguridad...

if exist "%RAIZ%server\keys\private.pem" goto :llaves_listas

"%PY%" "%RAIZ%server\scripts\generate_keys.py" --out "%RAIZ%server\keys"
if errorlevel 1 goto :error_llaves
goto :llaves_fin

:llaves_listas
echo       Las llaves ya existian, NO se regeneran.
echo       Regenerarlas invalidaria las licencias ya instaladas.

:llaves_fin
echo.


REM ---------- [5/6] Archivo de configuracion ----------
echo [5/6] Creando el archivo de configuracion...

"%PY%" "%RAIZ%server\scripts\setup_env.py"
if errorlevel 1 goto :error_env
echo.


REM ---------- [6/6] Llave publica en el cliente ----------
echo [6/6] Insertando la llave publica en la aplicacion...

"%PY%" "%RAIZ%tools\embed_public_key.py" --root "%RAIZ%."
if errorlevel 1 goto :error_embed
echo.


echo ==========================================================
echo   PASO 1 COMPLETADO
echo ==========================================================
echo.
echo GUARDA la clave de administrador que aparecio arriba.
echo Tambien quedo escrita en el archivo server\.env
echo.
echo Ahora ejecuta:  2-iniciar-servidor.bat
echo.
pause
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

:no_proyecto
echo ==========================================================
echo   ERROR: no encuentro los archivos del proyecto
echo ==========================================================
echo.
echo Busque  server\requirements.txt  en:
echo    %~dp0server\
echo    %~dp0cadlink\server\
echo.
echo Junto a este archivo deben estar las carpetas:
echo    client   server   docs   tools
echo.
echo Si al descomprimir quedo una carpeta dentro de otra con el
echo mismo nombre, borra todo y descomprime el zip en  C:\  a
echo secas. Debe quedar  C:\cadlink\  con los .bat adentro.
echo.
goto :error

:sin_python
echo.
echo ==========================================================
echo   ERROR: no encuentro Python
echo ==========================================================
echo.
echo Que hacer:
echo.
echo   1. Entra a  https://www.python.org/downloads/
echo   2. Descarga Python para Windows, el boton amarillo grande.
echo   3. Al instalar, MARCA la casilla que dice:
echo          Add python.exe to PATH
echo      Esta abajo, en la primera pantalla del instalador.
echo      Si no la marcas, nada va a funcionar.
echo   4. Reinicia la computadora.
echo   5. Vuelve a ejecutar este archivo.
echo.
goto :error

:error_venv
echo.
echo ERROR: no se pudo crear el entorno de Python.
echo.
echo Suele ser por permisos de la carpeta, o porque la ruta tiene
echo acentos o esta en OneDrive. Mueve la carpeta a C:\cadlink
echo y vuelve a intentar.
echo.
goto :error

:error_pip
echo.
echo ==========================================================
echo   ERROR: no se pudieron instalar las librerias
echo ==========================================================
echo.
echo Hay dos causas posibles. Mira arriba, en el texto que dejo
echo pip, cual de las dos es.
echo.
echo 1) TU PYTHON ES MUY NUEVO.
echo.
echo    Si arriba se lee algo de "building wheel", "Rust",
echo    "cargo", "Microsoft Visual C++" o "metadata-generation",
echo    es esto: las librerias vienen compiladas y todavia no
echo    hay version compilada para tu Python, asi que pip
echo    intento compilarlas desde el codigo fuente.
echo.
echo    Solucion: instala Python 3.13 desde
echo        https://www.python.org/downloads/release/python-3130/
echo    marcando "Add python.exe to PATH", y vuelve a ejecutar
echo    este archivo. NO hace falta desinstalar el que tienes:
echo    este instalador prefiere solo el 3.13 cuando existe.
echo.
echo 2) NO HAY PASO A INTERNET.
echo.
echo    Si arriba se lee "Could not find a version", "timeout",
echo    "SSL" o "Temporary failure in name resolution", es la
echo    red: el antivirus o el firewall de la empresa suelen
echo    bloquear la descarga de librerias.
echo.
goto :error

:error_llaves
echo.
echo ERROR: no se pudieron generar las llaves de seguridad.
echo.
goto :error

:error_env
echo.
echo ERROR: no se pudo crear el archivo de configuracion.
echo.
goto :error

:error_embed
echo.
echo ERROR: no se pudo insertar la llave publica en la aplicacion.
echo.
goto :error

:error
echo Copia TODO el texto de esta ventana y mandamelo.
echo.
echo Para copiarlo: clic derecho en la ventana, Marcar,
echo selecciona el texto y pulsa Enter.
echo.
echo Tambien puedes ejecutar  diagnostico.bat  y mandarme
echo el archivo diagnostico.txt que genera.
echo.
pause
exit /b 1
