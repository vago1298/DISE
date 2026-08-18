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

".venv\Scripts\python.exe" -m uvicorn app.main:app --port 8000
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
