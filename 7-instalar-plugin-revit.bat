@echo off
chcp 65001 >nul
title Instalar el complemento de CadLink para Revit
setlocal enabledelayedexpansion

cd /d "%~dp0"

echo ==========================================================
echo   INSTALAR EL COMPLEMENTO DE CADLINK PARA REVIT
echo ==========================================================
echo.
echo   Compila el complemento y lo copia a la carpeta de
echo   complementos de Revit, en cada Revit 2025 o 2026 que
echo   tenga esta computadora.
echo.
echo   Si falta el SDK de .NET 8, se ofrece instalarlo solo,
echo   sin permisos de administrador.
echo.
echo   Si tu Revit esta en otra carpeta, ejecuta:
echo       7-instalar-plugin-revit.bat "D:\Autodesk\Revit 2026"
echo.

set "RAIZ=%~dp0"
set "PROY=%RAIZ%client\src\CadLink.Revit\CadLink.Revit.csproj"
set "MANIFIESTO=%RAIZ%client\src\CadLink.Revit\CadLink.Revit.addin"
set "LOG=%RAIZ%instalar-revit.log"

REM ---------- Que no se ejecute desde dentro del ZIP ----------
REM  Windows deja abrir un .bat de un zip sin descomprimir: lo copia solo a Temp y ahi
REM  no estan las demas carpetas. Es lo primero que falla en una computadora nueva.
echo "%RAIZ%" | findstr /i /c:"\AppData\Local\Temp\" >nul
if not errorlevel 1 goto :desde_zip

if not exist "%PROY%" goto :no_proyecto

echo Instalacion del complemento de CadLink > "%LOG%"
echo Carpeta: %RAIZ% >> "%LOG%"

REM ---------- Revit cerrado ----------
:revisar_revit
tasklist /fi "imagename eq Revit.exe" 2>nul | find /i "Revit.exe" >nul
if errorlevel 1 goto :revit_cerrado
echo.
echo   REVIT ESTA ABIERTO. Cierralo (guarda tu trabajo) y pulsa
echo   una tecla para seguir. Con Revit abierto no se puede
echo   reemplazar el complemento.
echo.
pause >nul
goto :revisar_revit
:revit_cerrado

REM ---------- El SDK de .NET 8 o mas nuevo ----------
set "DOTNET="
call :sdk_en "dotnet"
if defined DOTNET goto :sdk_listo

set "LOCAL=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if exist "%LOCAL%" call :sdk_en "%LOCAL%"
if defined DOTNET goto :sdk_listo

echo.
echo   No encuentro el SDK de .NET 8 en esta computadora.
echo   Puedo descargarlo e instalarlo solo para tu usuario
echo   (unos 200 MB, sin permisos de administrador).
echo.
choice /c SN /m "   Lo instalo ahora"
if errorlevel 2 goto :sin_dotnet

echo.
echo   Descargando e instalando el SDK de .NET 8...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $s = Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $s -UseBasicParsing; & $s -Channel 8.0 -InstallDir (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet')" >> "%LOG%" 2>&1
if exist "%LOCAL%" call :sdk_en "%LOCAL%"
if not defined DOTNET goto :sdk_fallo

:sdk_listo
REM  Con un SDK instalado solo para el usuario, el compilador tiene que saber donde esta.
for %%d in ("!DOTNET!") do set "DOTNET_DIR=%%~dpd"
if /i not "!DOTNET!"=="dotnet" set "DOTNET_ROOT=!DOTNET_DIR!"
if /i not "!DOTNET!"=="dotnet" set "PATH=!DOTNET_DIR!;!PATH!"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
echo    SDK de .NET: !SDKVER!
echo SDK: !DOTNET! !SDKVER! >> "%LOG%"

REM ---------- Que Revit hay ----------
set "VERSIONES="

if "%~1"=="" goto :buscar_revit
set "RUTA_A_MANO=%~1"
if not exist "!RUTA_A_MANO!\RevitAPI.dll" goto :ruta_mala
set "VER_A_MANO="
REM  Con %~1 y no con !RUTA!: del otro lado de una tuberia no hay expansion retardada
REM  y el !...! llegaba tal cual a findstr.
echo "%~1" | findstr /c:"2026" >nul
if not errorlevel 1 set "VER_A_MANO=2026"
echo "%~1" | findstr /c:"2025" >nul
if not errorlevel 1 set "VER_A_MANO=2025"
if not defined VER_A_MANO goto :version_desconocida
set "RUTA_!VER_A_MANO!=!RUTA_A_MANO!"
set "VERSIONES=!VER_A_MANO!"
goto :revit_listo

:buscar_revit
REM  En todos los discos, no solo en C:. Son 2025 y 2026, las que usan .NET 8.
call :buscar 2026
call :buscar 2025

:revit_listo
if not defined VERSIONES goto :sin_revit

REM ---------- Compilar y copiar, para cada Revit ----------
set "HECHAS="
set "FALLO="
for %%v in (!VERSIONES!) do call :instalar %%v
if defined FALLO goto :tras_fallo

echo.
echo ==========================================================
echo   LISTO: complemento instalado para Revit !HECHAS!
echo ==========================================================
echo.
echo   Ahora:
echo     1. Abre Revit y abre un proyecto.
echo     2. Si Revit pregunta si cargar el complemento CadLink,
echo        elige  "Always Load"  (Cargar siempre).
echo     3. Busca la pestana  CadLink  en la cinta.
echo.
pause
exit /b 0


REM ==========================================================
REM  SUBRUTINAS
REM ==========================================================

:sdk_en
REM  Pone DOTNET y SDKVER si ese dotnet tiene un SDK 8 o mas nuevo. Un SDK 9 o 10
REM  tambien compila para .NET 8.
set "SDKVER="
REM  Con CALL delante: si la ruta lleva espacios (un usuario "Juan Perez"), cmd no le
REM  quita las comillas al comando.
for /f "tokens=1" %%s in ('call "%~1" --list-sdks 2^>nul') do call :sdk_linea %%s
if defined SDKVER set "DOTNET=%~1"
exit /b 0

:sdk_linea
for /f "tokens=1 delims=." %%m in ("%~1") do if %%m GEQ 8 set "SDKVER=%~1"
exit /b 0

:buscar
for %%d in (C D E F G H) do if not defined RUTA_%1 if exist "%%d:\Program Files\Autodesk\Revit %1\RevitAPI.dll" set "RUTA_%1=%%d:\Program Files\Autodesk\Revit %1"
if defined RUTA_%1 set "VERSIONES=!VERSIONES! %1"
exit /b 0

:instalar
REM  Si ya fallo uno, los demas no se intentan.
if defined FALLO exit /b 1
set "V=%1"
set "RUTA=!RUTA_%1!"
set "OUT=%TEMP%\CadLink-Revit-%1"
set "DESTINO=%APPDATA%\Autodesk\Revit\Addins\%1"

echo.
echo ----------------------------------------------------------
echo   Revit %1: !RUTA!
echo   Compilando (la primera vez tarda un poco)...
echo ----------------------------------------------------------
echo. >> "%LOG%"
echo ===== Revit %1: !RUTA! ===== >> "%LOG%"

if exist "!OUT!" rmdir /s /q "!OUT!"

REM  --no-incremental: si hay dos Revit, cada uno se compila contra SU RevitAPI.dll.
REM  NuGetAudit=false: sin internet tambien compila (no hay paquetes que bajar).
"!DOTNET!" build "%PROY%" -c Release --nologo --no-incremental -o "!OUT!" /p:RutaRevit="!RUTA!" /p:NuGetAudit=false >> "%LOG%" 2>&1
if errorlevel 1 set "FALLO=compilar"
if not defined FALLO if not exist "!OUT!\CadLink.Revit.dll" set "FALLO=compilar"
if defined FALLO exit /b 1

if not exist "!DESTINO!" mkdir "!DESTINO!"
if not exist "!DESTINO!" set "FALLO=carpeta"
if defined FALLO exit /b 1

REM  TRES archivos: el complemento, su nucleo y el manifiesto. Las DLL de Revit NO se
REM  copian: hacen que Revit cargue dos veces los mismos tipos.
copy /y "!OUT!\CadLink.Revit.dll" "!DESTINO!\" >nul
if errorlevel 1 set "FALLO=copiar"
copy /y "!OUT!\CadLink.Revit.Nucleo.dll" "!DESTINO!\" >nul
if errorlevel 1 set "FALLO=copiar"
copy /y "%MANIFIESTO%" "!DESTINO!\" >nul
if errorlevel 1 set "FALLO=copiar"
if defined FALLO exit /b 1

REM  Sin la marca de "descargado de internet", que en algunas computadoras hace que
REM  Revit no cargue la DLL.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath $env:DESTINO -Filter 'CadLink*' | Unblock-File" >nul 2>&1

echo    Copiado a  !DESTINO!
echo Copiado a !DESTINO! >> "%LOG%"
set "HECHAS=!HECHAS! %1"
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

:tras_fallo
if "!FALLO!"=="carpeta" goto :error_carpeta
if "!FALLO!"=="copiar" goto :error_copiar
goto :error_compilar

:desde_zip
echo.
echo ERROR: lo estas abriendo DESDE DENTRO DEL ZIP.
echo.
echo   1. Clic derecho en el .zip  ^>  "Extraer todo..."
echo   2. Extraelo en una carpeta corta, por ejemplo  C:\CadLink
echo   3. Entra a la carpeta extraida y ejecuta este archivo ahi.
echo.
goto :error

:no_proyecto
echo.
echo ERROR: no encuentro los archivos del proyecto.
echo.
echo Buscaba:  %PROY%
echo.
echo Ejecuta este archivo desde la carpeta extraida del zip,
echo sin moverlo de ahi. Si la carpeta tiene un signo  !  en
echo el nombre, cambiala a una ruta simple como  C:\CadLink
echo.
goto :error

:sin_dotnet
echo.
echo ERROR: hace falta el SDK de .NET 8.
echo.
echo   1. Entra a  https://dotnet.microsoft.com/download/dotnet/8.0
echo   2. Columna  SDK  (no Runtime), Windows x64. Instalalo.
echo   3. Vuelve a ejecutar este archivo.
echo.
goto :error

:sdk_fallo
echo.
echo ERROR: no se pudo instalar el SDK de .NET 8 automaticamente.
echo (revisa la conexion a internet, o un proxy o antivirus).
echo.
echo Instalalo a mano:
echo   https://dotnet.microsoft.com/download/dotnet/8.0
echo   columna  SDK , Windows x64.
echo.
echo Detalle en:  %LOG%
echo.
goto :error

:sin_revit
echo.
echo ERROR: no encuentro Revit 2025 ni 2026 en ningun disco.
echo.
echo Busque  RevitAPI.dll  en  X:\Program Files\Autodesk\Revit 2026
echo y  Revit 2025  de los discos C a H.
echo.
echo Si tu Revit esta en otro sitio, pasale la ruta:
echo    7-instalar-plugin-revit.bat "D:\Autodesk\Revit 2026"
echo.
echo Si tu Revit es 2024 o anterior, este complemento no sirve
echo tal cual (esas versiones usan .NET Framework 4.8).
echo.
goto :error

:ruta_mala
echo.
echo ERROR: en la ruta que pasaste no hay RevitAPI.dll.
echo.
echo    !RUTA_A_MANO!
echo.
echo Tiene que ser la carpeta donde esta Revit.exe.
echo.
goto :error

:version_desconocida
echo.
echo ERROR: no puedo saber que version de Revit es esa.
echo La ruta tiene que llevar 2025 o 2026 en el nombre.
echo.
goto :error

:error_compilar
echo.
echo ERROR: no compilo. Las ultimas lineas del registro:
echo ----------------------------------------------------------
powershell -NoProfile -Command "Get-Content -LiteralPath $env:LOG -Tail 25"
echo ----------------------------------------------------------
echo.
echo Mandame el archivo completo:  %LOG%
echo.
goto :error

:error_carpeta
echo.
echo ERROR: no pude crear la carpeta de complementos.
echo    !DESTINO!
echo.
goto :error

:error_copiar
echo.
echo ERROR: no pude copiar los archivos a
echo    !DESTINO!
echo.
echo Lo mas probable es que Revit siga abierto. Cierralo y
echo vuelve a ejecutar esto.
echo.
goto :error

:error
echo.
pause
exit /b 1
