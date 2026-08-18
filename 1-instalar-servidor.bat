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


REM ---------- [1/6] Buscar un Python que sirva ----------
echo [1/6] Buscando Python...

set "PYEXE="

python -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=python"

if defined PYEXE goto :python_ok

REM Segundo intento: el lanzador "py", que Windows instala en el
REM directorio del sistema. Muchas veces funciona aunque la casilla
REM "Add python.exe to PATH" no se haya marcado al instalar.
py -3 -c "import sys" >nul 2>&1
if not errorlevel 1 set "PYEXE=py -3"

if defined PYEXE goto :python_ok
goto :sin_python

:python_ok
for /f "tokens=*" %%v in ('%PYEXE% --version 2^>^&1') do echo       Encontrado: %%v
echo.


REM ---------- [2/6] Entorno aislado ----------
echo [2/6] Preparando el entorno de Python...

if exist "%RAIZ%server\.venv\Scripts\python.exe" goto :venv_listo

%PYEXE% -m venv "%RAIZ%server\.venv"
if errorlevel 1 goto :error_venv

if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :error_venv
echo       Entorno creado.
goto :venv_fin

:venv_listo
echo       Ya existia, se reutiliza.

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
echo ERROR: fallo la descarga de las librerias.
echo.
echo Revisa que tengas conexion a internet. Si estas en una red
echo de empresa, el antivirus o el firewall pueden estar
echo bloqueando la descarga.
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
