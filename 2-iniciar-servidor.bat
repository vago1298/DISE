@echo off
chcp 65001 >nul
title Servidor de licencias - NO CIERRES ESTA VENTANA
setlocal

cd /d "%~dp0"

REM ---------- Localizar la raiz del proyecto ----------
set "RAIZ=%~dp0"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

set "RAIZ=%~dp0cadlink\"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

goto :no_proyecto

:raiz_ok
if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :falta_instalar

REM  QUE EXISTA NO BASTA: TIENE QUE ARRANCAR. Un .venv copiado de otra
REM  computadora trae python.exe pero apunta al Python de la maquina
REM  original, asi que falla con "did not find executable at ...". Se
REM  comprueba aqui para poder decir QUE pasa en lugar de dejar que el
REM  mensaje de Python salga suelto.
"%RAIZ%server\.venv\Scripts\python.exe" -c "pass" >nul 2>&1
if errorlevel 1 goto :venv_roto
if not exist "%RAIZ%server\keys\private.pem" goto :faltan_llaves
if not exist "%RAIZ%server\.env" goto :falta_env

cd /d "%RAIZ%server"

echo ==========================================================
echo   SERVIDOR DE LICENCIAS
echo ==========================================================
echo.
echo   DEJA ESTA VENTANA ABIERTA mientras uses la aplicacion.
echo   Si la cierras, apagas el servidor.
echo.
echo   Para revisar los equipos y las licencias, abre esto en
echo   tu navegador:
echo.
echo        http://localhost:8000/docs
echo.
echo   Para detener el servidor: pulsa Ctrl+C.
echo.
echo ==========================================================
echo.

REM ==========================================================
REM  LA DIRECCION PARA LAS PCs DE LA OFICINA
REM
REM  Se imprime ANTES de arrancar porque despues uvicorn se
REM  queda escribiendo su registro y esto se pierde hacia
REM  arriba. Es el dato que hace falta para que los demas
REM  equipos encuentren el servidor: desde ellos, localhost es
REM  su propia maquina.
REM ==========================================================
".venv\Scripts\python.exe" "scripts\mi_direccion.py" --puerto 8000

echo.

REM ==========================================================
REM  --host 0.0.0.0  ES LO QUE PERMITE QUE LA OFICINA ENTRE.
REM
REM  Sin ese parametro, uvicorn escucha SOLO en 127.0.0.1: el
REM  servidor funciona perfectamente en esta computadora y
REM  ninguna otra lo alcanza. Ahi estaba el "la PC del
REM  trabajador no tiene acceso": la huella dada de alta, el
REM  equipo como INTERNAL, y la aplicacion sin poder preguntar.
REM
REM  Escuchar en toda la red deja los endpoints /admin al
REM  alcance de la oficina, y por eso existe la ADMIN_API_KEY:
REM  sin ella no se da de alta ni se revoca nada. Para exponerlo
REM  a INTERNET hace falta ademas HTTPS: ver el README.
REM ==========================================================
".venv\Scripts\python.exe" -m uvicorn app.main:app --host 0.0.0.0 --port 8000
if errorlevel 1 goto :error_arranque

echo.
echo El servidor se detuvo.
pause
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

:no_proyecto
echo.
echo ERROR: no encuentro los archivos del proyecto.
echo.
echo Busque  server\app\main.py  en:
echo    %~dp0server\
echo    %~dp0cadlink\server\
echo.
echo Junto a este archivo deben estar las carpetas
echo    client   server   docs   tools
echo.
goto :error

:venv_roto
echo.
echo ==========================================================
echo   ERROR: el entorno de Python no sirve en esta computadora
echo ==========================================================
echo.
echo La carpeta  server\.venv  se copio de OTRA maquina. Un entorno
echo de Python guarda la ruta absoluta del Python con el que se
echo creo, asi que en esta computadora apunta a un usuario que no
echo existe y de ahi el mensaje:
echo.
echo     did not find executable at 'C:\Users\...\python.exe'
echo.
echo Solucion: ejecuta  1-instalar-servidor.bat
echo Ese lo detecta y lo rehace solo. No se pierde nada tuyo: las
echo llaves, el .env y la base de datos NO estan ahi dentro.
echo.
goto :error

:falta_instalar
echo.
echo ERROR: falta instalar primero.
echo.
echo Ejecuta  1-instalar-servidor.bat  y espera a que diga
echo PASO 1 COMPLETADO.
echo.
goto :error

:faltan_llaves
echo.
echo ERROR: faltan las llaves de seguridad.
echo.
echo Ejecuta  1-instalar-servidor.bat
echo.
goto :error

:falta_env
echo.
echo ERROR: falta el archivo de configuracion server\.env
echo.
echo Ejecuta  1-instalar-servidor.bat
echo.
goto :error

:error_arranque
echo.
echo ==========================================================
echo   EL SERVIDOR NO ARRANCO
echo ==========================================================
echo.
echo Causas mas comunes:
echo.
echo   - El puerto 8000 ya esta ocupado por otro programa,
echo     o por otra ventana de este mismo servidor ya abierta.
echo.
echo   - Falta alguna libreria. Vuelve a ejecutar el paso 1.
echo.
echo   - El archivo server\.env tiene un valor mal escrito.
echo.
goto :error

:error
echo Copia TODO el texto de esta ventana y mandamelo,
echo o ejecuta  diagnostico.bat  y mandame el archivo que genera.
echo.
pause
exit /b 1
