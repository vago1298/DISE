@echo off
chcp 65001 >nul
title Hacer permanente la licencia de este equipo
setlocal

cd /d "%~dp0"

echo ==========================================================
echo   LICENCIA INTERNA PERMANENTE PARA ESTE EQUIPO
echo ==========================================================
echo.
echo Convierte tu equipo en equipo de la empresa: licencia
echo gratuita, sin fecha de vencimiento y con todos los modulos.
echo.

REM ---------- Localizar la raiz del proyecto ----------
set "RAIZ=%~dp0"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

set "RAIZ=%~dp0cadlink\"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

goto :no_proyecto

:raiz_ok
if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :falta_instalar

REM  Lo mismo que en el paso 2: un .venv copiado de otra maquina existe
REM  pero no arranca. Ver la explicacion en :venv_roto.
"%RAIZ%server\.venv\Scripts\python.exe" -c "pass" >nul 2>&1
if errorlevel 1 goto :venv_roto

cd /d "%RAIZ%server"

".venv\Scripts\python.exe" "scripts\hazme_permanente.py" %*
if errorlevel 1 goto :error

echo.
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
echo Ejecuta  1-instalar-servidor.bat
echo.
goto :error

:error
echo.
echo Si no sabes que hacer, ejecuta  diagnostico.bat
echo y mandame el archivo diagnostico.txt que genera.
echo.
pause
exit /b 1
