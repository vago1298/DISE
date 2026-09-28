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

REM  Lo mismo que en el paso 2: un .venv copiado de otra maquina existe
REM  pero no arranca. Ver la explicacion en :venv_roto.
"%RAIZ%server\.venv\Scripts\python.exe" -c "pass" >nul 2>&1
if errorlevel 1 goto :venv_roto

cd /d "%RAIZ%server"

REM  El script PREGUNTA en pantalla cual equipo promover cuando hay varios, asi
REM  que su salida no se redirige ni se canaliza: si se hiciera, dejaria de ver
REM  una consola interactiva y volveria a rendirse sin dejar elegir.
".venv\Scripts\python.exe" "scripts\hazme_permanente.py" %*
if errorlevel 1 goto :error_explicado

echo.
pause
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

REM  El script de Python ya explico en pantalla que pasa y que hacer. Aqui solo
REM  se detiene la ventana para poder leerlo.
REM
REM  Antes este caso caia en :error, que lo tapaba con el mensaje generico de
REM  diagnostico.bat. Con varios equipos registrados, el script escribia "elige
REM  uno con --id N" y ese texto era justo el que se perdia: en pantalla solo
REM  quedaba el  pause,  o sea "presione una tecla para continuar", y no habia
REM  forma de elegir nada porque a un .bat abierto con doble clic no se le puede
REM  pasar --id.
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
