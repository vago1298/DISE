@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title CadLink - Subir a GitHub

REM ---------------------------------------------------------------------
REM  Sin bloques de parentesis a proposito, todo con etiquetas y goto.
REM  En CMD, un parentesis o un caracter raro dentro de un echo que este
REM  dentro de un bloque ( ) rompe el bloque entero, y el error que da no
REM  se parece en nada a la causa. Con etiquetas eso no puede pasar.
REM ---------------------------------------------------------------------

cd /d "%~dp0"

echo ==========================================================
echo    SUBIR CADLINK A GITHUB
echo ==========================================================
echo.

REM ---------------------------------------------------------------
REM  1. Que Git este instalado
REM ---------------------------------------------------------------
where git >nul 2>&1
if not errorlevel 1 goto :hay_git

echo    NO tienes Git instalado.
echo.
echo    Descargalo de:   https://git-scm.com/download/win
echo    Instalalo con las opciones por omision, cierra esta ventana
echo    y vuelve a ejecutar este archivo.
echo.
pause
exit /b 1

:hay_git

REM ---------------------------------------------------------------
REM  2. La direccion del repositorio, preguntada una sola vez
REM ---------------------------------------------------------------
set "CFG=%~dp0.github-repo.txt"
set "REPO="

if not exist "%CFG%" goto :pedir_repo

set /p REPO=<"%CFG%"
echo    Repositorio guardado:
echo       !REPO!
echo.
set "OTRO="
set /p OTRO="   Enter para usar ese, o pega otra direccion: "
if "!OTRO!"=="" goto :repo_listo
set "REPO=!OTRO!"
goto :repo_listo

:pedir_repo
echo    Primero crea el repositorio en GitHub:
echo.
echo       1. Entra a   https://github.com/new
echo       2. Ponle un nombre, por ejemplo   cadlink
echo       3. Marcalo PRIVADO. Es tu programa de paga.
echo       4. NO marques "Add a README file"
echo       5. Pulsa "Create repository"
echo.
echo    Copia la direccion que te muestra, la que termina en .git
echo    Se ve asi:   https://github.com/tuusuario/cadlink.git
echo.
set /p REPO="   Pega aqui la direccion: "

:repo_listo
if not "!REPO!"=="" goto :guardar_repo

echo.
echo    No escribiste ninguna direccion. No se subio nada.
pause
exit /b 1

:guardar_repo
echo !REPO!>"%CFG%"

REM ---------------------------------------------------------------
REM  3. Preparar el repositorio local
REM ---------------------------------------------------------------
echo.
echo ----------------------------------------------------------
echo    Preparando
echo ----------------------------------------------------------

if exist ".git" goto :ya_hay_repo
git init >nul 2>&1
echo    Repositorio local creado.

:ya_hay_repo
git branch -M main >nul 2>&1
git remote remove origin >nul 2>&1
git remote add origin "!REPO!"
echo    Apuntando a tu repositorio.

REM ---------------------------------------------------------------
REM  4. Revision de secretos
REM
REM     El .gitignore ya excluye las llaves, el .env y la base de
REM     licencias. Se revisa igual: si la llave privada llega a
REM     GitHub, cualquiera puede firmar licencias validas y el
REM     esquema de pago queda inservible. Es un minuto bien gastado.
REM ---------------------------------------------------------------
echo.
echo ----------------------------------------------------------
echo    Revisando que no se suba nada secreto
echo ----------------------------------------------------------

git add -A >nul 2>&1

set "LISTA=%TEMP%\cadlink-subir.txt"
git diff --cached --name-only > "!LISTA!" 2>nul

findstr /i /r "keys/ \.pem$ \.env$ \.db$ \.sqlite" "!LISTA!" >nul 2>&1
if errorlevel 1 goto :sin_secretos

echo.
echo    ALTO. Se iban a subir estos archivos, que son secretos:
echo.
findstr /i /r "keys/ \.pem$ \.env$ \.db$ \.sqlite" "!LISTA!"
echo.
echo    No se subio nada. Revisa el archivo .gitignore de esta
echo    carpeta antes de volver a intentar.
echo.
git reset >nul 2>&1
del "!LISTA!" >nul 2>&1
pause
exit /b 1

:sin_secretos
del "!LISTA!" >nul 2>&1
echo    Bien. No hay llaves ni bases de datos en la lista.

REM ---------------------------------------------------------------
REM  5. Guardar y subir
REM ---------------------------------------------------------------
echo.
echo ----------------------------------------------------------
echo    Subiendo
echo ----------------------------------------------------------
echo.

set "MENSAJE="
set /p MENSAJE="   Que cambiaste? Enter para un texto automatico: "
if not "!MENSAJE!"=="" goto :hay_mensaje
set "MENSAJE=Actualizacion de CadLink"

:hay_mensaje
git -c user.name="CadLink" -c user.email="cadlink@local" commit -m "!MENSAJE!" >nul 2>&1
if errorlevel 1 goto :nada_nuevo
echo    Cambios guardados.
goto :empujar

:nada_nuevo
echo    No habia nada nuevo que guardar.

:empujar
echo.
echo    La primera vez se abrira una ventana para que entres a
echo    tu cuenta de GitHub. Es normal.
echo.

git push -u origin main
if not errorlevel 1 goto :listo

echo.
echo ==========================================================
echo    NO SE PUDO SUBIR
echo ==========================================================
echo.
echo    Lo mas comun, por orden:
echo.
echo    1. El repositorio ya tenia contenido.
echo       Abre "Git Bash" en esta carpeta y escribe:
echo          git pull --rebase origin main
echo       y vuelve a ejecutar este archivo.
echo.
echo    2. La direccion esta mal escrita.
echo       Borra el archivo  .github-repo.txt  de esta carpeta
echo       y vuelve a intentar.
echo.
echo    3. No entraste a tu cuenta cuando te lo pidio.
echo.
pause
exit /b 1

:listo
echo.
echo ==========================================================
echo    LISTO
echo ==========================================================
echo.
echo    Tu codigo ya esta en GitHub.
echo.
echo    De aqui en adelante, cada vez que quieras subir cambios
echo    solo ejecuta otra vez este mismo archivo.
echo.
pause
