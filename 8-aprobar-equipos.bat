@echo off
title CadLink - Aprobar las PCs de la oficina
setlocal

REM ============================================================
REM  APROBAR LAS PCs DE LA OFICINA
REM
REM  Las PCs que instalan el paquete de oficina (6-crear-
REM  instalador.bat, opcion 3) se quedan ESPERANDO hasta que
REM  tu las apruebas. Esto abre la pagina donde las ves y las
REM  apruebas con un clic. Solo se abre en ESTA computadora, la
REM  del servidor.
REM
REM  ASCII puro y CRLF a proposito: con acentos o con finales de
REM  renglon LF, cmd.exe salta al sitio equivocado y la ventana se
REM  cierra sin decir nada.
REM ============================================================

set "URL=http://localhost:8000/oficina"

REM  Esta encendido el servidor? curl.exe viene con Windows 10 y 11.
where curl >nul 2>&1
if errorlevel 1 goto :abrir

curl -s -o nul --max-time 3 http://localhost:8000/health
if errorlevel 1 goto :apagado

:abrir
echo Abriendo la pagina de las PCs de la oficina...
echo    %URL%
start "" "%URL%"
exit /b 0

:apagado
echo.
echo ==========================================================
echo   EL SERVIDOR DE LICENCIAS ESTA APAGADO
echo ==========================================================
echo.
echo   Doble clic en  2-iniciar-servidor.bat , dejalo abierto, y
echo   vuelve a abrir este.
echo.
pause
exit /b 1
