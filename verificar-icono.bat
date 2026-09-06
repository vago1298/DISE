@echo off
chcp 65001 >nul
title Por que sigo viendo el icono viejo
setlocal enabledelayedexpansion

cd /d "%~dp0"

echo ==========================================================
echo   DE DONDE SALE EL ICONO
echo ==========================================================
echo.
echo   Esto no cambia nada. Solo mira que iconos hay en esta
echo   carpeta, cual usaria el instalador, y si lo que estas
echo   viendo en el escritorio es una imagen guardada por
echo   Windows.
echo.

set "RAIZ=%~dp0"
set "CARPETA_APP=%RAIZ%client\src\CadLink.App"
set "ICONOAPP=%CARPETA_APP%\Assets\app.ico"
set "CFGFUENTE=%CARPETA_APP%\cadlink.config.json"

REM  Los dos iconos de muestra que ha tenido el proyecto. Se reconocen por su tamano
REM  exacto, que es lo unico que se puede comparar desde un .bat.
set "TAMANO_DEL_RAYO=108062"
set "TAMANO_DEL_PERFIL=106770"


echo ----------------------------------------------------------
echo  1. EL ICONO DEL PROYECTO
echo ----------------------------------------------------------
echo.

if not exist "%ICONOAPP%" goto :no_hay_icono

set "TAM="
for %%a in ("%ICONOAPP%") do set "TAM=%%~za"
for %%a in ("%ICONOAPP%") do echo    %%~za bytes, del %%~ta

if "%TAM%"=="%TAMANO_DEL_RAYO%" echo.
if "%TAM%"=="%TAMANO_DEL_RAYO%" echo    ES EL DEL RAYO. Tu carpeta es de una version
if "%TAM%"=="%TAMANO_DEL_RAYO%" echo    anterior: descarga el zip otra vez y
if "%TAM%"=="%TAMANO_DEL_RAYO%" echo    descomprimelo en una carpeta NUEVA.

if "%TAM%"=="%TAMANO_DEL_PERFIL%" echo.
if "%TAM%"=="%TAMANO_DEL_PERFIL%" echo    Es el del PERFIL I, el de ahora. Correcto.

if "%TAM%"=="%TAMANO_DEL_RAYO%" goto :paso2
if "%TAM%"=="%TAMANO_DEL_PERFIL%" goto :paso2
echo.
echo    No es ninguno de los dos de muestra, asi que es
echo    tu propio icono. Correcto.
goto :paso2

:no_hay_icono
echo    NO HAY ICONO en:
echo       %ICONOAPP%
echo.
echo    Sin el, el ejecutable sale con el icono generico de
echo    Windows. Falta descomprimir el proyecto completo.

:paso2
echo.
echo ----------------------------------------------------------
echo  2. ICONOS QUE MANDAN SOBRE EL DEL PROYECTO
echo ----------------------------------------------------------
echo.
echo   Un .ico en la carpeta  installer  o aqui en la raiz se usa
echo   ANTES que el del proyecto. Una copia vieja olvidada ahi es
echo   la causa mas comun de seguir viendo el icono anterior.
echo.

set "MANDAN="

for %%i in ("%RAIZ%installer\*.ico") do set "MANDAN=1"
for %%i in ("%RAIZ%installer\*.ico") do echo    installer\%%~nxi   %%~zi bytes
for %%i in ("%RAIZ%*.ico") do set "MANDAN=1"
for %%i in ("%RAIZ%*.ico") do echo    %%~nxi   %%~zi bytes

if defined MANDAN goto :paso2b
echo    Ninguno. Se usaria el del proyecto.
goto :paso3

:paso2b
echo.
echo    El PRIMERO de esa lista es el que se usaria. Si mide
echo    %TAMANO_DEL_RAYO% bytes es el del rayo: borralo.

:paso3
echo.
echo ----------------------------------------------------------
echo  3. LA RUTA QUE DICE TU CONFIGURACION
echo ----------------------------------------------------------
echo.

if not exist "%CFGFUENTE%" goto :sin_config

set "LINEA="
for /f "usebackq delims=" %%l in (`findstr /c:"logo" "%CFGFUENTE%" ^| findstr /v /c:"//"`) do if not defined LINEA set "LINEA=%%l"
if not defined LINEA goto :sin_clave

set "LINEA=%LINEA:*: =%"
set "LINEA=%LINEA:,=%"
set "RUTACFG="
for /f "delims=" %%r in (%LINEA%) do set "RUTACFG=%%r"
if not defined RUTACFG goto :sin_clave
set "RUTACFG=%RUTACFG:/=\%"

echo    %RUTACFG%
echo.

if /i not "%RUTACFG:~-4%"==".ico" goto :cfg_no_ico
if not exist "%RUTACFG%" goto :cfg_no_existe
for %%a in ("%RUTACFG%") do echo    Existe, %%~za bytes. Este se usaria si no hay
echo    ninguno en installer ni en la raiz.
goto :paso4

:cfg_no_ico
echo    No es un .ico, asi que sirve para el logo de la pantalla
echo    de inicio pero NO para el icono del ejecutable.
goto :paso4

:cfg_no_existe
echo    NO EXISTE ese archivo. Por eso no se usa tu icono.
goto :paso4

:sin_clave
echo    La configuracion no tiene la clave "logo".
goto :paso4

:sin_config
echo    No encuentro cadlink.config.json

:paso4
echo.
echo ----------------------------------------------------------
echo  4. COMPILACION VIEJA
echo ----------------------------------------------------------
echo.

set "VIEJO="
if exist "%CARPETA_APP%\obj" set "VIEJO=1"
if exist "%CARPETA_APP%\bin" set "VIEJO=1"

if not defined VIEJO echo    No hay nada compilado. El siguiente paquete se
if not defined VIEJO echo    arma desde cero.
if defined VIEJO echo    Hay una compilacion anterior en obj y bin. El icono
if defined VIEJO echo    va incrustado en el .exe, y si no se borra, Windows
if defined VIEJO echo    puede quedarse con el ejecutable de antes. El
if defined VIEJO echo    6-crear-instalador.bat  ya la borra solo.

echo.
echo ----------------------------------------------------------
echo  5. LO QUE YA ESTA INSTALADO
echo ----------------------------------------------------------
echo.

set "PUESTO="
if exist "%LOCALAPPDATA%\Programs\CadLink\CadLink.exe" set "PUESTO=%LOCALAPPDATA%\Programs\CadLink\CadLink.exe"
if not defined PUESTO if exist "%ProgramFiles%\CadLink\CadLink.exe" set "PUESTO=%ProgramFiles%\CadLink\CadLink.exe"
if not defined PUESTO if exist "%ProgramFiles(x86)%\CadLink\CadLink.exe" set "PUESTO=%ProgramFiles(x86)%\CadLink\CadLink.exe"

if not defined PUESTO echo    No encuentro CadLink instalado. El acceso directo
if not defined PUESTO echo    del escritorio apunta a otra cosa: quiza a una
if not defined PUESTO echo    compilacion suelta, y esa no se actualiza al armar
if not defined PUESTO echo    el instalador.
if defined PUESTO echo    %PUESTO%
if defined PUESTO for %%a in ("%PUESTO%") do echo    del %%~ta

echo.
if exist "%RAIZ%dist\*.exe" echo    Instaladores armados:
for %%a in ("%RAIZ%dist\*.exe") do echo       %%~nxa   del %%~ta

echo.
echo ==========================================================
echo   QUE HACER
echo ==========================================================
echo.
echo   1. Si el paso 1 dice RAYO: descarga el zip otra vez y
echo      descomprimelo en una carpeta NUEVA.
echo   2. Si el paso 2 lista algun .ico viejo: borralo.
echo   3. Ejecuta  6-crear-instalador.bat  y elige la opcion 1.
echo   4. Instala el  dist\CadLink-Setup-1.0.0.exe
echo.
echo   MIRA EL ICONO DEL PROPIO INSTALADOR en la carpeta dist:
echo   lleva el mismo icono que la aplicacion y es un archivo
echo   nuevo, asi que Windows no lo tiene guardado de antes. Si
echo   ese se ve bien y el del escritorio no, lo que ves es la
echo   imagen guardada: borra el acceso directo, o cierra sesion
echo   de Windows y vuelve a entrar.
echo.
pause
exit /b 0
