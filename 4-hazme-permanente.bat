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
echo Si hay varios equipos registrados, abajo te va a preguntar
echo cual quieres. Escribe el ID de la lista, o la palabra TODOS.
echo.

REM ---------- Localizar la raiz del proyecto ----------
set "RAIZ=%~dp0"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

set "RAIZ=%~dp0cadlink\"
if exist "%RAIZ%server\app\main.py" goto :raiz_ok

goto :no_proyecto

:raiz_ok
if not exist "%RAIZ%server\.venv\Scripts\python.exe" goto :falta_instalar

cd /d "%RAIZ%server"

REM El script pregunta en pantalla cual equipo promover cuando hay varios, asi
REM que aqui NO se redirige ni se canaliza su salida: si se hiciera, dejaria de
REM ver una consola interactiva y volveria a rendirse sin dejarte elegir.
".venv\Scripts\python.exe" "scripts\hazme_permanente.py" %*
if errorlevel 1 goto :error_explicado

echo.
pause
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

REM El script de Python ya imprimio en pantalla el motivo y que hacer. Aqui solo
REM se detiene la ventana para que se pueda leer. Antes este caso caia en :error
REM y se tapaba con el mensaje generico de diagnostico.bat, que era lo unico que
REM se llegaba a ver junto al 'presione una tecla para continuar'.
:error_explicado
echo.
pause
exit /b 1


REM ---------- Fallos del entorno, antes de llegar al script ----------

:no_proyecto
echo.
echo ERROR: no encuentro los archivos del proyecto.
echo.
echo Busque  server\app\main.py  en:
echo    %~dp0server\
echo    %~dp0cadlink\server\
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
