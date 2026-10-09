---
inclusion: always
---

# Versión de CadLink

- **La versión actual es la 1.1** (`<Version>1.1.0</Version>` en `client/src/CadLink.App/CadLink.App.csproj`). La 1.0.0 ya quedó atrás: no la vuelvas a poner.
- Se escribe con tres números (`1.1.0`) en el `.csproj`. Con dos, la app mostraría `1.1.-1`, porque lee `Major.Minor.Build`. Al usuario se le dice "versión 1.1".
- Se escribe también en el `#define Version` de respaldo de `installer/CadLink.iss`, que tiene que coincidir (lo vigila `tools/verificar_instalador.py`).
- De ahí salen el nombre del instalador (`CadLink-Setup-1.1.0-OFICINA.exe`), lo que aparece en *Agregar o quitar programas* y lo que la app le reporta al servidor de licencias.
- **La versión la decide el usuario.** No la subas por tu cuenta; cuando él diga la siguiente, se cambia en el `.csproj` y en el `.iss`.
