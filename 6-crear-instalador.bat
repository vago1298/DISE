@echo off
chcp 65001 >nul
title Crear el instalador de CadLink
setlocal enabledelayedexpansion

cd /d "%~dp0"

echo ==========================================================
echo   CREAR EL INSTALADOR PARA ENTREGARLE AL CLIENTE
echo ==========================================================
echo.
echo   Esto hace dos cosas:
echo.
echo     1. Publica la aplicacion AUTOCONTENIDA. El cliente no
echo        necesita instalar .NET ni Python: todo va dentro.
echo     2. Empaqueta un solo  CadLink-Setup-1.0.0.exe  que se
echo        abre con doble clic.
echo.
echo   Tarda varios minutos la primera vez.
echo.

set "RAIZ=%~dp0"
set "CSPROJ=%RAIZ%client\src\CadLink.App\CadLink.App.csproj"
set "LLAVE=%RAIZ%client\src\CadLink.Licensing\EmbeddedPublicKey.cs"
set "GUION=%RAIZ%installer\CadLink.iss"
set "PUBLICADO=%RAIZ%client\src\CadLink.App\bin\Release\net8.0-windows\win-x64\publish"
set "CFG=%PUBLICADO%\cadlink.config.json"

REM  MODO PRUEBA: crea el instalador aunque la configuracion siga apuntando a
REM  tu propia computadora, para poder probar la instalacion sin servidor.
REM  El archivo sale marcado, para que no se entregue por error.
set "PRUEBA="
if /i "%~1"=="prueba" set "PRUEBA=1"
if defined PRUEBA echo   *** MODO PRUEBA: no se lo mandes a un cliente. ***
if defined PRUEBA echo.

if not exist "%CSPROJ%" goto :no_proyecto
if not exist "%GUION%" goto :no_guion


REM ============================================================
REM  LA VERSION SALE DEL .csproj, NO SE ESCRIBE AQUI
REM  Asi el nombre del instalador, lo que dice Agregar o quitar
REM  programas y lo que la app reporta al servidor de licencias
REM  no pueden discrepar.
REM ============================================================

set "VER="
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" "%CSPROJ%"') do set "VER=%%v"
if not defined VER goto :sin_version

echo Version a publicar: %VER%
echo.


REM ---------- SDK de .NET ----------
dotnet --version >nul 2>&1
if errorlevel 1 goto :sin_dotnet


REM ---------- La llave publica tiene que estar puesta ----------
REM  Sin ella la app no puede verificar ninguna licencia y NINGUN cliente
REM  podria activar. Es el error mas caro de descubrir despues de repartir.
if not exist "%LLAVE%" goto :sin_llave_archivo
findstr /c:"PEGA_AQUI_TU_LLAVE_PUBLICA" "%LLAVE%" >nul 2>&1
if not errorlevel 1 goto :falta_llave


REM ============================================================
REM  PUBLICAR EN LIMPIO
REM  Se borra la carpeta antes: si quedan archivos de una version
REM  anterior, el comodin del instalador se los lleva al cliente.
REM ============================================================

echo Publicando la aplicacion...
echo.

if exist "%PUBLICADO%" rd /s /q "%PUBLICADO%"

dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -o "%PUBLICADO%"
if errorlevel 1 goto :error_publicar

if not exist "%PUBLICADO%\CadLink.exe" goto :sin_exe

echo.
echo Publicado en:
echo    %PUBLICADO%
echo.


REM ============================================================
REM  LA CONFIGURACION QUE SE VA A REPARTIR
REM ============================================================

if not exist "%CFG%" goto :sin_config

REM  ¿Sigue apuntando a tu computadora? Entonces el cliente no podria activar:
REM  su localhost es SU maquina, no la tuya.
findstr /c:"servidorLicencias" "%CFG%" | findstr /c:"localhost" >nul 2>&1
if not errorlevel 1 goto :config_localhost

REM  Y tiene que ser https. Sin TLS, cualquiera en la red del cliente puede
REM  suplantar las respuestas del servidor de licencias.
findstr /c:"servidorLicencias" "%CFG%" | findstr /c:"https://" >nul 2>&1
if errorlevel 1 goto :config_sin_tls

:config_ok
REM  El logo apunta a una carpeta tuya. No rompe nada -la app usa el logo
REM  embebido si no lo encuentra- pero le ensena al cliente tus rutas.
findstr /c:"logo" "%CFG%" | findstr /i /c:"C:/Users" >nul 2>&1
if not errorlevel 1 echo   AVISO: la clave "logo" apunta a una carpeta de tu equipo. Dejala vacia para repartir.
findstr /c:"logo" "%CFG%" | findstr /i /c:"C:\\Users" >nul 2>&1
if not errorlevel 1 echo   AVISO: la clave "logo" apunta a una carpeta de tu equipo. Dejala vacia para repartir.


REM ============================================================
REM  FIRMA DEL EJECUTABLE  (opcional, pero muy recomendable)
REM
REM  Sin firma, Windows ensena «Windows protegio tu PC» en azul y hay que
REM  entrar en «Mas informacion» para poder instalar. La mitad de los
REM  clientes no pasan de ahi.
REM
REM  DOS FORMAS, porque desde 2023 ya no venden certificados de codigo como
REM  archivo: los nuevos llegan en un token USB o en un HSM en la nube.
REM
REM    Certificado en ARCHIVO -los viejos, o uno de prueba-:
REM       setx CADLINK_FIRMA_PFX    "C:\ruta\certificado.pfx"
REM       setx CADLINK_FIRMA_CLAVE  "la contrasena del pfx"
REM
REM    Certificado en TOKEN USB o en el almacen de Windows:
REM       setx CADLINK_FIRMA_NOMBRE "Nombre exacto de la empresa en el certificado"
REM
REM  El ejecutable se firma ANTES de empaquetar y el instalador DESPUES: si se
REM  firma solo el instalador, Windows marca el .exe al abrirlo.
REM ============================================================

set "SELLO=http://timestamp.digicert.com"

set "SIGNTOOL="
for /f "delims=" %%s in ('where signtool 2^>nul') do if not defined SIGNTOOL set "SIGNTOOL=%%s"

set "HAYFIRMA="
if defined CADLINK_FIRMA_PFX set "HAYFIRMA=1"
if defined CADLINK_FIRMA_NOMBRE set "HAYFIRMA=1"

if not defined HAYFIRMA goto :sin_firmar
if not defined SIGNTOOL goto :sin_signtool

call :firmar "%PUBLICADO%\CadLink.exe"
if errorlevel 1 goto :error_firma
echo.
goto :empaquetar

:sin_signtool
echo   AVISO: hay certificado configurado pero no encuentro signtool.exe.
echo          Viene con el SDK de Windows. Se empaqueta SIN firmar.
echo.
goto :empaquetar

:sin_firmar
echo   AVISO: el ejecutable NO va firmado. Windows va a mostrarle al cliente
echo          la pantalla azul de "Windows protegio tu PC".
echo          Como firmarlo: docs\distribucion.md
echo.


REM ============================================================
REM  EMPAQUETAR CON INNO SETUP
REM ============================================================

:empaquetar
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC for /f "delims=" %%s in ('where ISCC 2^>nul') do if not defined ISCC set "ISCC=%%s"
if not defined ISCC goto :sin_inno

echo Empaquetando el instalador...
echo.

"%ISCC%" /DVersion=%VER% "%GUION%"
if errorlevel 1 goto :error_iscc

set "SETUPEXE=%RAIZ%dist\CadLink-Setup-%VER%.exe"
if not exist "%SETUPEXE%" goto :sin_setup

if not defined HAYFIRMA goto :listo
if not defined SIGNTOOL goto :listo

call :firmar "%SETUPEXE%"
if errorlevel 1 goto :error_firma


:listo
echo.
echo ==========================================================
echo   LISTO
echo ==========================================================
echo.
echo   %SETUPEXE%
echo.
echo   Eso es lo unico que le mandas al cliente. Antes de
echo   repartirlo, pruebalo en una computadora que NO sea la
echo   tuya y que NO tenga .NET instalado.
echo.
if defined PRUEBA echo   *** ES UN PAQUETE DE PRUEBA: apunta a localhost. ***
if defined PRUEBA echo.
pause
exit /b 0


REM ==========================================================
REM  FIRMAR UN ARCHIVO   ( %1 = ruta )
REM  El sello de tiempo no es opcional: sin el, la firma deja de
REM  valer el dia que el certificado expira, y con eso los
REM  instaladores que ya repartiste empiezan a dar el aviso azul.
REM ==========================================================

:firmar
if defined CADLINK_FIRMA_PFX goto :firmar_archivo
if defined CADLINK_FIRMA_NOMBRE goto :firmar_almacen
exit /b 0

:firmar_archivo
echo Firmando %~nx1 con el certificado en archivo...
"%SIGNTOOL%" sign /fd SHA256 /tr %SELLO% /td SHA256 /f "%CADLINK_FIRMA_PFX%" /p "%CADLINK_FIRMA_CLAVE%" "%~1"
exit /b %errorlevel%

:firmar_almacen
echo Firmando %~nx1 con el certificado del almacen...
"%SIGNTOOL%" sign /fd SHA256 /tr %SELLO% /td SHA256 /n "%CADLINK_FIRMA_NOMBRE%" "%~1"
exit /b %errorlevel%


REM ==========================================================
REM  ERRORES
REM ==========================================================

:no_proyecto
echo ERROR: no encuentro el proyecto de la aplicacion.
echo    Lo busque en: %CSPROJ%
echo.
echo Descomprime el zip completo y ejecuta este archivo desde
echo la carpeta donde estan los otros .bat
goto :error

:no_guion
echo ERROR: no encuentro el guion del instalador.
echo    Lo busque en: %GUION%
goto :error

:sin_version
echo ERROR: no pude leer la version del .csproj
echo.
echo Abre  %CSPROJ%
echo y comprueba que tenga un renglon como este:
echo    (Version)1.0.0(/Version)   pero con picoparentesis
goto :error

:sin_dotnet
echo ==========================================================
echo   ERROR: no encuentro el SDK de .NET
echo ==========================================================
echo.
echo   1. Entra a  https://dotnet.microsoft.com/download/dotnet/8.0
echo   2. Descarga la columna que dice  SDK  -no Runtime-, Windows x64.
echo   3. Instalalo, reinicia y vuelve a ejecutar este archivo.
goto :error

:sin_llave_archivo
echo ERROR: no encuentro EmbeddedPublicKey.cs
echo    Lo busque en: %LLAVE%
goto :error

:falta_llave
echo ==========================================================
echo   ERROR: falta la llave publica
echo ==========================================================
echo.
echo Sin la llave publica embebida, la aplicacion no puede
echo verificar ninguna licencia y NINGUN cliente podria activar.
echo.
echo Ejecuta primero  1-instalar-servidor.bat
goto :error

:error_publicar
echo ==========================================================
echo   HUBO ERRORES AL PUBLICAR
echo ==========================================================
echo.
echo Copia las lineas que dicen  error CS  y mandamelas.
goto :error

:sin_exe
echo ERROR: la publicacion termino pero no hay CadLink.exe
echo    Lo busque en: %PUBLICADO%
goto :error

:sin_config
echo ERROR: no se publico cadlink.config.json
echo    Lo busque en: %CFG%
echo.
echo Comprueba en el .csproj que siga el bloque None Update de
echo cadlink.config.json con CopyToOutputDirectory.
goto :error

:config_localhost
if defined PRUEBA goto :config_ok
echo ==========================================================
echo   ERROR: la configuracion apunta a localhost
echo ==========================================================
echo.
echo   client\src\CadLink.App\cadlink.config.json  todavia dice
echo   "servidorLicencias": "http://localhost:8000"
echo.
echo   El localhost del cliente es SU computadora, no la tuya:
echo   ningun cliente podria activar. Ponle la direccion publica
echo   de tu servidor de licencias, con https.
echo.
echo   Si solo quieres probar el instalador en tu maquina:
echo      6-crear-instalador.bat prueba
goto :error

:config_sin_tls
if defined PRUEBA goto :config_ok
echo ==========================================================
echo   ERROR: el servidor de licencias no usa https
echo ==========================================================
echo.
echo   Sin TLS, cualquiera en la red del cliente puede suplantar
echo   las respuestas del servidor y regalarse licencias.
echo.
echo   Pon una direccion https en cadlink.config.json, o usa
echo      6-crear-instalador.bat prueba
goto :error

:error_firma
echo ==========================================================
echo   ERROR AL FIRMAR
echo ==========================================================
echo.
echo Revisa el certificado:
echo    CADLINK_FIRMA_PFX    = %CADLINK_FIRMA_PFX%
echo    CADLINK_FIRMA_NOMBRE = %CADLINK_FIRMA_NOMBRE%
echo    CADLINK_FIRMA_CLAVE  = (no se muestra)
echo.
echo Con token USB, el nombre tiene que ser EXACTAMENTE el del
echo certificado. Para verlo:  certmgr.msc, carpeta Personal.
goto :error

:sin_inno
echo ==========================================================
echo   FALTA INNO SETUP
echo ==========================================================
echo.
echo   Es gratis y es lo que arma el instalador.
echo.
echo   1. Entra a  https://jrsoftware.org/isdl.php
echo   2. Descarga  Inno Setup 6  e instalalo con todo lo que
echo      trae por omision.
echo   3. Vuelve a ejecutar este archivo.
echo.
echo   La aplicacion YA quedo publicada en:
echo      %PUBLICADO%
goto :error

:error_iscc
echo ==========================================================
echo   HUBO ERRORES AL EMPAQUETAR
echo ==========================================================
echo.
echo Copia el mensaje de arriba. Empieza por  Error  y dice el
echo renglon del guion  installer\CadLink.iss
goto :error

:sin_setup
echo ERROR: Inno Setup termino pero no encuentro el instalador.
echo    Lo busque en: %SETUPEXE%
goto :error

:error
echo.
pause
exit /b 1
