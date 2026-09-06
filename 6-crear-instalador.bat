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
set "CARPETA_APP=%RAIZ%client\src\CadLink.App"
set "PUBLICADO=%CARPETA_APP%\bin\Release\net8.0-windows\win-x64\publish"
set "CFG=%PUBLICADO%\cadlink.config.json"

if not exist "%CSPROJ%" goto :no_proyecto
if not exist "%GUION%" goto :no_guion


REM ============================================================
REM  QUE PAQUETE SE ARMA
REM
REM  SE PREGUNTA, no se pasa por parametro: al dar doble clic en
REM  un .bat no hay forma de pasarle nada, asi que un parametro
REM  obligatorio seria un modo al que nunca se puede llegar.
REM  Desde la consola tambien se acepta:  6-crear-instalador.bat prueba
REM ============================================================

set "PRUEBA="
if /i "%~1"=="prueba" set "PRUEBA=1"
if defined PRUEBA goto :modo_elegido

echo   ----------------------------------------------------------
echo    QUE PAQUETE QUIERES ARMAR
echo   ----------------------------------------------------------
echo.
echo      1 = DE PRUEBA, para tu propia computadora.
echo          Vale aunque la configuracion apunte a localhost.
echo          NO se lo puedes mandar a un cliente.
echo.
echo      2 = PARA EL CLIENTE, el de verdad.
echo          Exige que la configuracion ya apunte a tu
echo          servidor de licencias, con https.
echo.

set "OPCION="
set /p "OPCION=Escribe 1 o 2 y pulsa Enter: "

if "%OPCION%"=="1" set "PRUEBA=1"
if "%OPCION%"=="1" goto :modo_elegido
if "%OPCION%"=="2" goto :modo_elegido

echo.
echo   No entendi "%OPCION%". Hay que escribir 1 o 2.
goto :error

:modo_elegido
echo.
if defined PRUEBA echo   *** MODO PRUEBA: no se lo mandes a un cliente. ***
if defined PRUEBA echo.


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
REM  EL ICONO DEL EJECUTABLE
REM
REM  Se busca en cuatro sitios y gana el primero que aparezca:
REM
REM    1. Un .ico en la carpeta  installer
REM    2. Un .ico junto a estos .bat
REM    3. La ruta que YA trae cadlink.config.json en su clave
REM       "logo", si apunta a un .ico
REM    4. El de muestra que viene en el repositorio
REM
REM  EL 3 ES EL QUE NO CUESTA NADA. Esa ruta ya esta escrita y
REM  viaja con el proyecto, asi que sigue funcionando cada vez que
REM  se vuelve a descargar el zip, sin volver a copiar nada.
REM
REM  Tiene que ser un .ico de verdad, no un .png renombrado: el
REM  icono va incrustado en el .exe como recurso de Windows, y el
REM  compilador rechaza cualquier otra cosa.
REM
REM  El icono del ACCESO DIRECTO sale del .exe, asi que con esto
REM  quedan los tres: ejecutable, acceso directo e instalador.
REM ============================================================

set "ICONOAPP=%RAIZ%client\src\CadLink.App\Assets\app.ico"
set "CFGFUENTE=%RAIZ%client\src\CadLink.App\cadlink.config.json"

REM  El tamano exacto del .ico del RAYO, el de las primeras versiones. Se reconoce por
REM  ahi para poder rechazarlo: el del perfil I mide 106770.
set "TAMANO_DEL_RAYO=108062"

set "ICONOTUYO="
for %%i in ("%RAIZ%installer\*.ico") do if not defined ICONOTUYO set "ICONOTUYO=%%i"
for %%i in ("%RAIZ%*.ico") do if not defined ICONOTUYO set "ICONOTUYO=%%i"
if defined ICONOTUYO goto :icono_copiar

if not exist "%CFGFUENTE%" goto :icono_del_repo

REM  LOS COMENTARIOS SE DESCARTAN. El bloque de ayuda de esa clave trae rutas de
REM  EJEMPLO iguales a la de verdad, y sin este filtro se tomaria una de ellas y el
REM  aviso senalaria un archivo que nunca existio.
set "LINEA="
for /f "usebackq delims=" %%l in (`findstr /c:"logo" "%CFGFUENTE%" ^| findstr /v /c:"//"`) do if not defined LINEA set "LINEA=%%l"
if not defined LINEA goto :icono_del_repo

REM  De     "logo": "C:/ruta/CADLINK.ico"     a     C:\ruta\CADLINK.ico
REM  Se corta hasta los dos puntos de la clave, se quita la coma final si la hay, y
REM  las comillas las quita el propio for -que es la manera segura, sin un set sin
REM  comillas que se rompa con un caracter raro en la ruta-.
set "LINEA=%LINEA:*: =%"
set "LINEA=%LINEA:,=%"
for /f "delims=" %%r in (%LINEA%) do set "ICONOTUYO=%%r"
if not defined ICONOTUYO goto :icono_del_repo
set "ICONOTUYO=%ICONOTUYO:/=\%"

if /i not "%ICONOTUYO:~-4%"==".ico" goto :icono_no_es_ico
if not exist "%ICONOTUYO%" goto :icono_no_esta
goto :icono_copiar

:icono_no_es_ico
echo   AVISO: la clave "logo" de cadlink.config.json no apunta a un .ico:
echo      %ICONOTUYO%
echo          Ese archivo si sirve para el logo de la pantalla de inicio, pero
echo          para el icono del ejecutable hace falta un .ico. Copia el tuyo
echo          en la carpeta  installer
echo.
set "ICONOTUYO="
goto :icono_del_repo

:icono_no_esta
echo   AVISO: no encuentro el icono que dice cadlink.config.json:
echo      %ICONOTUYO%
echo          Comprueba la ruta, o copia tu .ico en la carpeta  installer
echo.
set "ICONOTUYO="
goto :icono_del_repo

:icono_copiar
copy /y "%ICONOTUYO%" "%ICONOAPP%" >nul
if errorlevel 1 goto :error_icono

echo Icono tomado de:
echo    %ICONOTUYO%
goto :icono_medida

:icono_del_repo
if not exist "%ICONOAPP%" goto :sin_icono
echo Icono: el de muestra del repositorio.
echo    Para usar el tuyo, copia tu .ico en la carpeta  installer
goto :icono_medida

REM  SE DICE QUE TAMANO Y QUE FECHA TIENE EL ICONO QUE SE VA A INCRUSTAR. Parece un
REM  detalle de mas, pero es lo unico que permite saber, sin instalar nada, si el
REM  ejecutable se hizo con el icono nuevo o con el de antes.
:icono_medida
set "TAMICONO="
for %%a in ("%ICONOAPP%") do set "TAMICONO=%%~za"
for %%a in ("%ICONOAPP%") do echo    %%~za bytes, del %%~ta

REM ============================================================
REM  Y SI ES EL DEL RAYO, NO SE ARMA NADA.
REM
REM  El rayo era el icono de las primeras versiones, de cuando se
REM  creyo que el programa hablaba con ETAP -instalaciones
REM  electricas- en lugar de con ETABS. Se cambio por la seccion
REM  de un perfil I, pero un .ico viejo puede seguir dando vueltas
REM  en la carpeta installer, en la raiz, o en una copia del
REM  proyecto que no se volvio a descargar.
REM
REM  Parar aqui es mejor que armar el paquete: el usuario dijo que
REM  no quiere volver a ver ese icono, y un aviso mas entre veinte
REM  renglones de compilacion no se lee.
REM ============================================================
if "%TAMICONO%"=="%TAMANO_DEL_RAYO%" goto :icono_es_el_rayo
echo.
goto :icono_listo

:sin_icono
echo   AVISO: no hay ningun icono. El .exe va a salir con el icono
echo          generico de Windows y el acceso directo tambien.
echo          Copia tu .ico en la carpeta  installer
echo.

:icono_listo


REM ============================================================
REM  PUBLICAR EN LIMPIO
REM  Se borra la carpeta antes: si quedan archivos de una version
REM  anterior, el comodin del instalador se los lleva al cliente.
REM ============================================================

echo Publicando la aplicacion...
echo.

REM  ===========================================================================
REM  SE BORRA LO YA COMPILADO, Y ESTO ES POR EL ICONO.
REM
REM  El icono va INCRUSTADO en el .exe como recurso de Windows, no como archivo
REM  aparte. Si MSBuild considera que el ensamblado ya esta al dia -porque el
REM  codigo no cambio, solo el .ico-, no lo vuelve a compilar y el ejecutable
REM  sale con el icono ANTERIOR. El sintoma es exactamente ese: se cambia el
REM  icono, se vuelve a publicar, y el acceso directo sigue con el de antes.
REM
REM  Cuesta un minuto de compilacion y quita de encima toda una clase de
REM  "lo cambie y no se ve".
REM  ===========================================================================
if exist "%CARPETA_APP%\obj" rd /s /q "%CARPETA_APP%\obj"
if exist "%CARPETA_APP%\bin" rd /s /q "%CARPETA_APP%\bin"
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

REM  Sigue apuntando a tu computadora? Entonces el cliente no podria activar:
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
REM  Sin firma, Windows ensena "Windows protegio tu PC" en azul y hay que
REM  entrar en "Mas informacion" para poder instalar. La mitad de los
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

:icono_es_el_rayo
echo.
echo ==========================================================
echo   ESE ICONO ES EL DEL RAYO
echo ==========================================================
echo.
echo   No se arma nada, para que no vuelvas a ver ese icono.
echo.
echo   El del rayo era de las primeras versiones. El de ahora es
echo   la seccion de un perfil I y mide 106770 bytes.
echo.
echo   El que se iba a usar es:
echo      %ICONOAPP%
echo      %TAMICONO% bytes
if defined ICONOTUYO echo   y salio de:
if defined ICONOTUYO echo      %ICONOTUYO%
echo.
echo   Como arreglarlo, segun de donde salio:
echo.
echo     - Si salio de la carpeta  installer  o de la raiz:
echo       borra ese .ico de ahi. Es una copia vieja.
echo.
echo     - Si es el del propio proyecto: tu carpeta es de una
echo       version anterior. Descarga el zip otra vez y
echo       descomprimelo en una carpeta NUEVA, no encima.
echo.
echo     - Para usar el tuyo: copia tu .ico en  installer
echo.
echo   Para ver todos los iconos que hay y cual se usaria,
echo   ejecuta  verificar-icono.bat
goto :error

:error_icono
echo ERROR: no pude copiar el icono.
echo    De:  %ICONOTUYO%
echo    A:   %ICONOAPP%
echo.
echo Comprueba que el archivo no este abierto en otro programa y
echo que de verdad sea un .ico, no un .png renombrado.
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
