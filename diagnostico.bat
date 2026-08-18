@echo off
chcp 65001 >nul
title Diagnostico de CadLink
setlocal

cd /d "%~dp0"
set "LOG=%~dp0diagnostico.txt"

echo ==========================================================
echo   DIAGNOSTICO
echo ==========================================================
echo.
echo Recopilando informacion...
echo.

REM Crear el archivo vacio. Se usa 'type nul' en lugar de un echo con
REM redireccion para no mezclar el texto con el operador de redireccion.
type nul > "%LOG%"

REM ============================================================
REM  OJO con los espacios antes de >>
REM  Si se escribe  echo valor=%%errorlevel%%>> archivo  y el valor
REM  es 1, cmd lee  1>>  como "redirigir descriptor 1" y se pierde
REM  el numero. El espacio antes de >> lo evita.
REM ============================================================

echo ===== DIAGNOSTICO CadLink ===== >> "%LOG%"
echo Fecha: %DATE% %TIME% >> "%LOG%"
echo Carpeta: %~dp0 >> "%LOG%"
echo. >> "%LOG%"

echo --- Version de Windows --- >> "%LOG%"
ver >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- python --version --- >> "%LOG%"
python --version >> "%LOG%" 2>&1
call :anota_nivel
echo. >> "%LOG%"

echo --- py -3 --version --- >> "%LOG%"
py -3 --version >> "%LOG%" 2>&1
call :anota_nivel
echo. >> "%LOG%"

echo --- where python --- >> "%LOG%"
where python >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- dotnet --version --- >> "%LOG%"
dotnet --version >> "%LOG%" 2>&1
call :anota_nivel
echo. >> "%LOG%"

echo --- dotnet --list-sdks --- >> "%LOG%"
dotnet --list-sdks >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- Entorno virtual --- >> "%LOG%"
if exist "server\.venv\Scripts\python.exe" echo SI existe server\.venv\Scripts\python.exe >> "%LOG%"
if not exist "server\.venv\Scripts\python.exe" echo NO existe server\.venv\Scripts\python.exe >> "%LOG%"
echo. >> "%LOG%"

echo --- Llaves de firma --- >> "%LOG%"
if exist "server\keys\private.pem" echo SI existe private.pem >> "%LOG%"
if not exist "server\keys\private.pem" echo NO existe private.pem >> "%LOG%"
if exist "server\keys\public.pem" echo SI existe public.pem >> "%LOG%"
if not exist "server\keys\public.pem" echo NO existe public.pem >> "%LOG%"
echo. >> "%LOG%"

echo --- Archivo de configuracion --- >> "%LOG%"
if exist "server\.env" echo SI existe server\.env >> "%LOG%"
if not exist "server\.env" echo NO existe server\.env >> "%LOG%"
echo. >> "%LOG%"

echo --- Llave publica insertada en el cliente --- >> "%LOG%"
findstr /c:"PEGA_AQUI_TU_LLAVE_PUBLICA" "client\src\CadLink.Licensing\EmbeddedPublicKey.cs" >nul 2>&1
if errorlevel 1 echo SI, la llave ya esta puesta >> "%LOG%"
if not errorlevel 1 echo NO, todavia tiene el marcador de posicion >> "%LOG%"
findstr /c:"BEGIN PUBLIC KEY" "client\src\CadLink.Licensing\EmbeddedPublicKey.cs" >nul 2>&1
if not errorlevel 1 echo El archivo contiene un bloque PEM >> "%LOG%"
echo. >> "%LOG%"

echo --- Librerias instaladas --- >> "%LOG%"
if exist "server\.venv\Scripts\python.exe" "server\.venv\Scripts\python.exe" -m pip list >> "%LOG%" 2>&1
if not exist "server\.venv\Scripts\python.exe" echo Sin entorno virtual, no hay librerias que listar >> "%LOG%"
echo. >> "%LOG%"

echo --- Archivos junto a este .bat --- >> "%LOG%"
dir /b >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- Carpetas que deberian existir --- >> "%LOG%"
if exist "client" echo SI existe client >> "%LOG%"
if not exist "client" echo FALTA la carpeta client >> "%LOG%"
if exist "server" echo SI existe server >> "%LOG%"
if not exist "server" echo FALTA la carpeta server >> "%LOG%"
if exist "tools" echo SI existe tools >> "%LOG%"
if not exist "tools" echo FALTA la carpeta tools >> "%LOG%"
if exist "docs" echo SI existe docs >> "%LOG%"
if not exist "docs" echo FALTA la carpeta docs >> "%LOG%"
if exist "cadlink" echo OJO: hay una carpeta cadlink ANIDADA aqui dentro >> "%LOG%"
echo. >> "%LOG%"

echo --- Donde esta realmente CadLink.App.csproj --- >> "%LOG%"
dir /s /b "CadLink.App.csproj" >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- Donde esta realmente requirements.txt --- >> "%LOG%"
dir /s /b "requirements.txt" >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo --- Estructura de carpetas --- >> "%LOG%"
dir /s /b /ad >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo ==========================================================
echo   LISTO
echo ==========================================================
echo.
echo Se creo el archivo:
echo    %LOG%
echo.
echo Lo voy a abrir en el Bloc de notas. Copia TODO el texto
echo y mandamelo por el chat.
echo.
timeout /t 2 >nul
start "" notepad "%LOG%"
exit /b 0


REM ---------- subrutina: anota el codigo de salida ----------
:anota_nivel
if errorlevel 1 echo   ^(no se pudo ejecutar^) >> "%LOG%"
if not errorlevel 1 echo   ^(ejecutado correctamente^) >> "%LOG%"
exit /b 0
