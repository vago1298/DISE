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
echo   complementos de Revit. Al terminar, abre Revit y busca
echo   la pestana CadLink en la cinta.
echo.
echo   Hace falta:
echo     - Revit 2025 o 2026 instalado
echo     - el SDK de .NET 8
echo.
echo   Si tu Revit esta en otra carpeta, arrastra este archivo
echo   a una consola y pasale la ruta, o ejecuta:
echo       7-instalar-plugin-revit.bat "D:\Autodesk\Revit 2026"
echo.

set "RAIZ=%~dp0"
set "SLN=%RAIZ%client\CadLink.Revit.sln"
set "SALIDA=%RAIZ%client\src\CadLink.Revit\bin\Release"
set "MANIFIESTO=%RAIZ%client\src\CadLink.Revit\CadLink.Revit.addin"

if not exist "%SLN%" goto :no_proyecto

REM ---------- El SDK de .NET ----------
dotnet --version >nul 2>&1
if errorlevel 1 goto :sin_dotnet

for /f "tokens=*" %%v in ('dotnet --version 2^>^&1') do echo    SDK de .NET: %%v

REM ---------- Donde esta Revit ----------
REM  Se busca 2026 y luego 2025. Son las dos versiones que usan .NET 8; de 2024 para
REM  atras la Revit API era .NET Framework 4.8 y este complemento no sirve sin
REM  recompilarlo con otro TargetFramework.
set "VER="
set "RUTAREVIT="

if not "%~1"=="" set "RUTAREVIT=%~1"
if not "%~1"=="" goto :ruta_a_mano

if exist "C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll" set "VER=2026"
if exist "C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll" set "RUTAREVIT=C:\Program Files\Autodesk\Revit 2026"

if not defined VER if exist "C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll" set "VER=2025"
if not defined VER if exist "C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll" set "RUTAREVIT=C:\Program Files\Autodesk\Revit 2025"

goto :revit_listo

:ruta_a_mano
REM  La version se saca del nombre de la carpeta que dio el usuario, para saber a que
REM  carpeta de complementos hay que copiar.
if not exist "!RUTAREVIT!\RevitAPI.dll" goto :ruta_mala
REM  Ojo con la tuberia: cmd.exe parte el renglon por el | ANTES de mirar el if, asi
REM  que "if cond echo X | findstr Y" no hace lo que parece. Se dejan los findstr
REM  sueltos, uno por version.
echo !RUTAREVIT! | findstr /c:"2026" >nul
if not errorlevel 1 set "VER=2026"

echo !RUTAREVIT! | findstr /c:"2025" >nul
if not errorlevel 1 set "VER=2025"

if not defined VER goto :version_desconocida

:revit_listo
if not defined VER goto :sin_revit

echo    Revit !VER!: !RUTAREVIT!
echo.

REM ---------- Compilar ----------
echo ----------------------------------------------------------
echo   Compilando. La primera vez tarda un poco.
echo ----------------------------------------------------------
echo.

dotnet build "%SLN%" -c Release --nologo /p:RutaRevit="!RUTAREVIT!"
if errorlevel 1 goto :error_compilar

if not exist "%SALIDA%\CadLink.Revit.dll" goto :sin_dll

REM ---------- Copiar ----------
REM  Van TRES archivos: el complemento, su nucleo y el manifiesto. Las DLL de Revit NO
REM  se copian a proposito; copiarlas hace que Revit cargue dos veces los mismos tipos
REM  y falle con errores incomprensibles.
set "DESTINO=%APPDATA%\Autodesk\Revit\Addins\!VER!"

if not exist "!DESTINO!" mkdir "!DESTINO!"
if not exist "!DESTINO!" goto :error_carpeta

echo.
echo    Copiando a  !DESTINO!

copy /y "%SALIDA%\CadLink.Revit.dll" "!DESTINO!\" >nul
if errorlevel 1 goto :error_copiar

copy /y "%SALIDA%\CadLink.Revit.Nucleo.dll" "!DESTINO!\" >nul
if errorlevel 1 goto :error_copiar

copy /y "%MANIFIESTO%" "!DESTINO!\" >nul
if errorlevel 1 goto :error_copiar

echo.
echo ==========================================================
echo   LISTO
echo ==========================================================
echo.
echo   Ahora:
echo.
echo     1. CIERRA Revit si lo tenias abierto. El complemento se
echo        carga al arrancar, asi que no aparece hasta que se
echo        vuelve a abrir.
echo     2. Abre Revit y abre un proyecto.
echo     3. Busca la pestana  CadLink  en la cinta.
echo     4. Pulsa  Importar modelo  y elige el archivo
echo        .cadlink-modelo.json  que dejo CadLink al exportar.
echo.
echo   Ahi se abre la ventana de mapeo: una fila por seccion,
echo   con las familias que TU tienes cargadas en el proyecto.
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
echo Buscaba:  %SLN%
echo.
echo Ejecuta este archivo desde la carpeta del proyecto.
echo.
goto :error

:sin_dotnet
echo.
echo ERROR: no esta instalado el SDK de .NET.
echo.
echo   1. Entra a  https://dotnet.microsoft.com/download/dotnet/8.0
echo   2. Busca la columna que dice  SDK , no la de Runtime.
echo   3. Descarga el de Windows x64 e instalalo.
echo   4. REINICIA la computadora y vuelve a ejecutar esto.
echo.
goto :error

:sin_revit
echo.
echo ERROR: no encuentro Revit 2025 ni 2026.
echo.
echo Busque  RevitAPI.dll  en:
echo    C:\Program Files\Autodesk\Revit 2026\
echo    C:\Program Files\Autodesk\Revit 2025\
echo.
echo Si tu Revit esta en otro sitio, pasale la ruta:
echo    7-instalar-plugin-revit.bat "D:\Autodesk\Revit 2026"
echo.
echo Y si tu Revit es 2024 o anterior, este complemento NO sirve
echo tal cual: esas versiones usan .NET Framework 4.8 en vez de
echo .NET 8, y hay que recompilarlo. Avisame y lo preparo.
echo.
goto :error

:ruta_mala
echo.
echo ERROR: en la ruta que pasaste no hay RevitAPI.dll.
echo.
echo    !RUTAREVIT!
echo.
echo Tiene que ser la carpeta donde esta Revit.exe.
echo.
goto :error

:version_desconocida
echo.
echo ERROR: no puedo saber que version de Revit es esa.
echo.
echo    !RUTAREVIT!
echo.
echo Hace falta para saber a que carpeta de complementos copiar.
echo La ruta tiene que llevar 2025 o 2026 en el nombre.
echo.
goto :error

:error_compilar
echo.
echo ERROR: no compilo.
echo.
echo Los errores estan arriba. Copialos y mandamelos.
echo.
goto :error

:sin_dll
echo.
echo ERROR: compilo pero no encuentro el resultado.
echo.
echo Buscaba:  %SALIDA%\CadLink.Revit.dll
echo.
goto :error

:error_carpeta
echo.
echo ERROR: no pude crear la carpeta de complementos.
echo.
echo    !DESTINO!
echo.
goto :error

:error_copiar
echo.
echo ERROR: no pude copiar los archivos.
echo.
echo Lo mas probable es que REVIT ESTE ABIERTO y tenga el
echo complemento cargado. Cierra Revit y vuelve a intentarlo.
echo.
goto :error

:error
echo.
pause
exit /b 1
