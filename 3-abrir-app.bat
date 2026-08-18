@echo off
chcp 65001 >nul
title Paso 3 - Compilar y abrir la aplicacion
setlocal

cd /d "%~dp0"

echo ==========================================================
echo   PASO 3 de 3 - ABRIR LA APLICACION
echo ==========================================================
echo.


REM ============================================================
REM  LOCALIZAR EL PROYECTO
REM  No se asume la forma de la carpeta. Si al descomprimir quedo
REM  anidada, o si el .bat se movio, se busca de todas formas.
REM ============================================================

set "RAIZ=%~dp0"
if exist "%RAIZ%client\src\CadLink.App\CadLink.App.csproj" goto :raiz_ok

REM Caso comun: el zip creo una subcarpeta cadlink dentro de la carpeta
set "RAIZ=%~dp0cadlink\"
if exist "%RAIZ%client\src\CadLink.App\CadLink.App.csproj" goto :raiz_ok

REM Ultimo recurso: buscarlo en todo el arbol
set "PROJ="
for /f "delims=" %%p in ('dir /s /b "%~dp0CadLink.App.csproj" 2^>nul') do set "PROJ=%%p"
if defined PROJ goto :proj_ok
goto :no_proyecto

:raiz_ok
set "PROJ=%RAIZ%client\src\CadLink.App\CadLink.App.csproj"

:proj_ok
for %%d in ("%PROJ%") do set "CARPETA_APP=%%~dpd"
echo Proyecto encontrado en:
echo    %PROJ%
echo.


REM ---------- SDK de .NET ----------
echo Buscando el SDK de .NET...

dotnet --version >nul 2>&1
if errorlevel 1 goto :sin_dotnet

for /f "tokens=*" %%v in ('dotnet --version 2^>^&1') do echo    Encontrado SDK version %%v
echo.


REM ---------- La llave publica tiene que estar puesta ----------
set "LLAVE=%CARPETA_APP%..\CadLink.Licensing\EmbeddedPublicKey.cs"
if not exist "%LLAVE%" goto :sin_llave_archivo

findstr /c:"PEGA_AQUI_TU_LLAVE_PUBLICA" "%LLAVE%" >nul 2>&1
if not errorlevel 1 goto :falta_llave


REM ---------- Compilar y correr ----------
echo Compilando. La primera vez tarda varios minutos.
echo.

dotnet run --project "%PROJ%"
if errorlevel 1 goto :error_compilar

echo.
echo La aplicacion se cerro.
pause
exit /b 0


REM ==========================================================
REM  ERRORES
REM ==========================================================

:no_proyecto
echo ==========================================================
echo   ERROR: no encuentro el proyecto
echo ==========================================================
echo.
echo Busque el archivo  CadLink.App.csproj  en:
echo.
echo    %~dp0client\src\CadLink.App\
echo    %~dp0cadlink\client\src\CadLink.App\
echo    y en todas las subcarpetas de  %~dp0
echo.
echo Esto pasa cuando al descomprimir el zip queda una carpeta
echo dentro de otra con el mismo nombre, o cuando el .bat quedo
echo separado del resto de los archivos.
echo.
echo Como arreglarlo:
echo.
echo   1. Borra la carpeta donde descomprimiste.
echo   2. Descomprime el zip en  C:\  a secas.
echo      Debe quedar  C:\cadlink\  con los .bat adentro.
echo   3. Comprueba que junto a este archivo existan las
echo      carpetas  client  server  docs  tools
echo.
goto :error

:sin_dotnet
echo.
echo ==========================================================
echo   ERROR: no encuentro el SDK de .NET
echo ==========================================================
echo.
echo Que hacer:
echo.
echo   1. Entra a  https://dotnet.microsoft.com/download/dotnet/8.0
echo   2. Busca la columna que dice  SDK
echo      OJO: la columna SDK, no la que dice Runtime.
echo   3. Descarga el instalador de Windows x64 e instalalo.
echo   4. Reinicia la computadora.
echo   5. Vuelve a ejecutar este archivo.
echo.
goto :error

:sin_llave_archivo
echo.
echo ERROR: no encuentro el archivo EmbeddedPublicKey.cs
echo.
echo Lo busque en:
echo    %LLAVE%
echo.
echo La carpeta client parece incompleta. Vuelve a descomprimir el zip.
echo.
goto :error

:falta_llave
echo.
echo ERROR: falta insertar la llave publica.
echo.
echo Ejecuta primero  1-instalar-servidor.bat
echo.
goto :error

:error_compilar
echo.
echo ==========================================================
echo   HUBO ERRORES AL COMPILAR
echo ==========================================================
echo.
echo Copia TODA la lista de errores de arriba y mandamela.
echo Busca las lineas que dicen  error CS
echo.
echo Para copiar: clic derecho en la ventana, Marcar,
echo selecciona el texto y pulsa Enter.
echo.
goto :error

:error
echo Si no sabes que hacer, ejecuta  diagnostico.bat
echo y mandame el archivo diagnostico.txt que genera.
echo.
pause
exit /b 1
