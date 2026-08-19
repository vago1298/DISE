# CadLink

Aplicación de escritorio para Windows que conecta **Excel · ETABS · AutoCAD**, con
licenciamiento de dos niveles: **gratis para las PCs de la oficina**, **de paga por
suscripción para clientes externos**.

> ## ⚠️ CORRECCIÓN PENDIENTE EN EL MODELO DE DATOS
>
> La primera versión de este andamiaje se construyó por error suponiendo **ETAP**
> (análisis de sistemas eléctricos) en lugar de **ETABS** (análisis estructural
> de edificios, de CSI). Son programas distintos de dominios distintos.
>
> **Consecuencia:** las pestañas de datos actuales (Buses, Transformadores,
> Cables, Cargas eléctricas) **son del dominio equivocado** y hay que
> reemplazarlas por las estructurales (nudos, marcos, vigas, columnas, losas,
> combinaciones de carga, armado, etc.), según lo que hagan las macros reales.
>
> **Lo que NO se afecta y sigue siendo válido al 100%:**
> todo el licenciamiento (servidor, huella de hardware, firma RSA, tiers interno
> y comercial, gracia sin conexión, revocación), el splash con el logo, la
> pantalla de activación, el estilo de las pestañas y el arranque de la
> aplicación. Es la mayor parte del trabajo y es independiente del dominio.
>
> **Nota técnica sobre ETABS:** a diferencia de ETAP, ETABS no se controla por
> API REST sino por su **CSI OAPI**, una interfaz COM/.NET. Se agrega una
> referencia a `ETABSv1.dll` desde la carpeta de instalación de ETABS
> (habitualmente `C:\Program Files\Computers and Structures\ETABS 2x\`) y se usan
> las interfaces `cOAPI` y `cSapModel`. Los nombres de los métodos son los mismos
> que en VBA, así que las macros existentes se traducen a C# de forma casi
> mecánica. Referencias:
> [CSI Developer](https://www.csiamerica.com/developer) ·
> [wiki de CSI](https://web.wiki.csiamerica.com/wiki/x/2dwe)
> *(contenido reformulado para cumplir con las restricciones de licencia de las fuentes)*

Un solo ejecutable atiende a todos. Lo que cambia es el nivel de licencia que el
servidor asigna a cada equipo. Ver [`docs/arquitectura-licenciamiento.md`](docs/arquitectura-licenciamiento.md)
para el diseño completo y sus límites.

---

## Estado del proyecto

Antes de invertir tiempo, ten claro qué está listo y qué no:

| Componente | Estado |
|---|---|
| Servidor de licencias (activación, renovación, revocación, webhooks de pago) | ✅ Completo |
| Módulo de licenciamiento del cliente (huella, firma RSA, cache, gracia offline) | ✅ Completo |
| Splash con logo + validación al arrancar | ✅ Completo |
| Pantalla de activación (interna automática / externa con clave) | ✅ Completo |
| Ventana principal con pestañas estilo Excel y cuadrículas editables | ✅ Completo |
| Validación de conectividad y vista previa del unifilar | ✅ Completo |
| Columnas calculadas (kVA, corriente, ampacidad total) | ✅ Completo |
| Conexión a AutoCAD por COM (`AcadConnection`) | ✅ Completo |
| **Una sola barra arriba**: menú y botones en la misma fila, con Ctrl+G / Ctrl+A / Ctrl+N | ✅ Completo |
| **Tema claro u oscuro**, con botón en la barra y recordado entre sesiones | ✅ Completo |
| Dibujar la planta estructural en AutoCAD (`PlantaDrawer`) | ✅ Completo |
| **Columna circular**, elegida en la columna *Elemento* | ✅ Completo |
| **Zuncho helicoidal o en anillos**, a elección del usuario | ✅ Completo |
| **Gancho sísmico del zuncho** a 135° sobre una varilla, con la cola en el núcleo | ✅ Completo |
| **El corte insertado junto al alzado lleva sus llamadas de varillas** | ✅ Completo |
| Alzados y bloques a **1 m** sobre la sección más alta | ✅ Completo |
| Vista previa con fondo azul, igual para las dos formas | ✅ Completo |
| **Rótulo del alzado debajo del bloque insertado y de sus cotas** | ✅ Completo |
| Zuncho en contorno o macizo según el estilo de la sección | ✅ Completo |
| Zuncho en contorno **con el ancho de la varilla** y sin picos en las crestas | ✅ Completo |
| Varillas recortadas donde el zuncho pasa por delante | ✅ Completo |
| Hoja con paneles inmovilizados y color por grupo de columnas | ✅ Completo |
| **Importar desde Excel** | ⛔ Retirado de la interfaz — ver `docs/macro-secciones-concreto.md` §1 |
| **Motor de dibujo en AutoCAD** | 🚧 En proceso — decidida la ruta A (COM) |
| **Lectura de ETABS (CSI OAPI)** | ✅ Completo — `EtabsConnection` por ProgID `CSI.ETABS.API.ETABSObject` |
| **Lectura de SAP2000** | 🚧 En proceso — CSI comparte la OAPI, así que es el mismo lector con otro ProgID |

Los pendientes están marcados en el código con el comentario
`PENDIENTE DE IMPLEMENTAR`.

## Las macros originales

Están analizadas línea por línea. Estos dos documentos son la **especificación
del port**, incluyendo los errores detectados en el código actual:

| Macro | Líneas | Documento |
|---|---|---|
| `SECCIONES ESTRUCTURALES COTAS Y ROTULOS V3` | ~2.500 | [`docs/macro-secciones-concreto.md`](docs/macro-secciones-concreto.md) |
| `PLANOS ESTRUCTURALES` (v50) | ~5.000 | [`docs/macro-plantas-etabs.md`](docs/macro-plantas-etabs.md) |
| `ALZADOS V2` | ~1.900 | [`docs/comparacion-macro-alzados.md`](docs/comparacion-macro-alzados.md) — **cotejo rutina por rutina contra el código real** |

> ⚠️ **El cotejo de `ALZADOS V2` encontró un defecto numérico en el port.** La tabla
> de diámetros de varilla estaba redondeada y el `#2` estaba mal: 0.60 cm en lugar
> de 0.635, lo que daba un **área un 12 % baja** y con ella una cuantía baja, que es
> del lado inseguro. Corregido al nominal exacto (`n/8` de pulgada). Detalle en
> [`docs/comparacion-macro-alzados.md`](docs/comparacion-macro-alzados.md) §1.

**Decisión de arquitectura:** el motor de dibujo va por **COM**, no por plugin
nativo. Razones y consecuencias en
[`docs/decision-ruta-a-com.md`](docs/decision-ruta-a-com.md).

> ### Estado de compilación
>
> | Proyecto | ¿Compilado? |
> |---|---|
> | `CadLink.Cad` | ✅ `dotnet build` limpio, 0 avisos |
> | `CadLink.Etabs` | ✅ `dotnet build` limpio, 0 avisos |
> | `CadLink.Licensing` | ⛔ No se pudo: necesita dos paquetes de NuGet y el entorno donde se escribió no tiene salida a internet |
> | `CadLink.App` | ⛔ No se pudo: depende de `CadLink.Licensing`, y WPF además pide `EnableWindowsTargeting` fuera de Windows |
>
> De los dos que no compilan se comprobó lo que se puede comprobar sin compilar:
> XAML bien formado, **cero errores de sintaxis** de C# (analizados con Roslyn), y
> las validaciones estáticas de `tools/validar.py`, que revisan justo los errores
> que un compilador no ve. Aun así, **espera tener que corregir algún detalle menor
> de `CadLink.App` la primera vez que compiles en Windows**: un error de nombre o de
> tipo en ese proyecto no lo caza nada de lo anterior.
>
> El servidor en Python sí fue verificado con Python 3.11.

---

## Requisitos

**Servidor de licencias** (puede correr en Linux, Windows o macOS)
- Python 3.11 o superior

**Aplicación cliente** (solo Windows)
- Windows 10 o 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 con la carga de trabajo *.NET desktop development* (opcional;
  también funciona todo desde la línea de comandos)

---

## Paso 1 — Levantar el servidor de licencias

```bash
cd server

# Entorno virtual
python -m venv .venv
# Windows:
.venv\Scripts\activate
# Linux/macOS:
source .venv/bin/activate

pip install -r requirements.txt
```

### Generar las llaves de firma

```bash
python scripts/generate_keys.py
```

Esto crea `keys/private.pem` y `keys/public.pem`, y te imprime en pantalla el
bloque de la llave pública que necesitarás en el paso 2.

> 🔑 **La llave privada es el activo más crítico de todo el sistema.** Quien la
> tenga puede emitir licencias válidas y tu esquema de cobro deja de valer. Nunca
> la subas a Git (ya está en `.gitignore`), respáldala en un lugar seguro y
> restringe quién puede leerla en el servidor.

### Configurar

```bash
# Windows:
copy .env.example .env
# Linux/macOS:
cp .env.example .env
```

Edita `.env`. Para la primera prueba local basta con esto:

```ini
ORG_NAME="Ingeniería MiEmpresa, S.A. de C.V."
ADMIN_API_KEY="una-clave-larga-y-aleatoria-que-tu-elijas"
INTERNAL_DOMAIN_SID=""      # vacío por ahora
ALLOW_AUTO_TRIAL=true
```

Genera una `ADMIN_API_KEY` decente con:

```bash
python -c "import secrets; print(secrets.token_urlsafe(48))"
```

### Arrancar

```bash
uvicorn app.main:app --reload --port 8000
```

Comprueba que responde:

- http://localhost:8000/health → `{"status":"ok", ...}`
- http://localhost:8000/docs → interfaz interactiva para probar todos los endpoints

En `/docs` puedes autenticarte para los endpoints `/admin/*` poniendo tu
`ADMIN_API_KEY` en la cabecera `X-Admin-Key`.

---

## Paso 2 — Embeber la llave pública en el cliente

Abre `client/src/CadLink.Licensing/EmbeddedPublicKey.cs` y reemplaza el bloque
marcador por el contenido de `server/keys/public.pem`:

```csharp
public const string Pem = """
    -----BEGIN PUBLIC KEY-----
    MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...
    ...
    -----END PUBLIC KEY-----
    """;
```

Si te olvidas de este paso, la aplicación falla al arrancar con un mensaje
explícito. Es deliberado: es mejor un error ruidoso que un binario que acepte
cualquier token.

---

## Paso 3 — Configurar y ejecutar el cliente

Para pruebas locales, edita `client/src/CadLink.App/AppInfo.cs`:

```csharp
public const string LicenseServerUrl = "http://localhost:8000";
```

Y personaliza tu marca en el mismo archivo:

```csharp
public const string ProductName = "CadLink";                    // nombre de tu producto
public const string CompanyName  = "MiEmpresa, S.A. de C.V.";    // tu razón social
public const string SupportEmail = "soporte@miempresa.com";
```

> En producción la URL **debe** ser `https://`. Sin TLS, cualquiera en la red
> puede interceptar y suplantar las respuestas del servidor de licencias.

### Reemplazar el logo

`client/src/CadLink.App/Assets/logo.png` es un marcador de posición generado
automáticamente. Pon ahí el logo real de tu empresa: PNG con fondo transparente,
mínimo 512×512 px. Queda **embebido en el ejecutable**, así que nadie puede
sustituirlo para cambiar la marca del programa.

### Compilar y ejecutar

```powershell
cd client
dotnet restore
dotnet build
dotnet run --project src\CadLink.App
```

O abre `client\CadLink.sln` en Visual Studio y presiona **F5**.

Al arrancar verás el splash con tu logo, y como el equipo es desconocido para el
servidor recibirá una **licencia de prueba de 30 días**.

---

## Paso 4 — Probar los tres niveles de licencia

### A. Prueba gratuita (comportamiento por defecto)

Ya lo viste en el paso 3. Nota que en la pestaña **Secciones Concreto** los botones
de *Generar dibujo* y *Generar alzados* están deshabilitados, igual que el de
*Dibujar en AutoCAD*: la prueba no incluye exportación. Eso se configura en
`server/app/tokens.py`, función `features_for`.

### B. Licencia interna gratuita (PCs de tus trabajadores)

**Opción 1 — Con Active Directory (automático, recomendado si tienes dominio)**

Obtén el SID de tu dominio en cualquier PC unida a él:

```powershell
(Get-ADDomain).DomainSID.Value
```

Si no tienes las herramientas RSAT instaladas:

```powershell
wmic useraccount where "localaccount=0" get sid
```

Toma el prefijo `S-1-5-21-x-y-z` (los tres bloques largos, sin el último número
que identifica al usuario) y ponlo en `.env`:

```ini
INTERNAL_DOMAIN_SID="S-1-5-21-1234567890-1234567890-1234567890"
INTERNAL_SEATS=30
```

Reinicia el servidor. Ahora **cualquier PC de tu dominio se activa sola**, sin
que el trabajador escriba nada. `INTERNAL_SEATS` es tu tope de seguridad: si se
excede, el servidor deja de regalar licencias y lo registra en la bitácora.

**Opción 2 — Sin Active Directory (lista blanca manual)**

Deja `INTERNAL_DOMAIN_SID` vacío. En la PC del trabajador, abre la aplicación,
ve a la pestaña **Licencia** y pulsa *Copiar huella*. Con esa huella:

```bash
python scripts/register_machine.py \
    --url http://localhost:8000 \
    --key "TU_ADMIN_API_KEY" \
    --fingerprint a3f1c2... \
    --note "Laptop de Juan Pérez - Proyectos"
```

El trabajador pulsa *Revalidar ahora* y su equipo pasa a licencia interna. Para
una oficina de 10–30 equipos es perfectamente manejable, y es la opción más
segura de las dos.

### C. Suscripción de paga (clientes externos)

Emite una licencia desde `/docs` (endpoint `POST /admin/licenses`) o por línea de
comandos:

```bash
curl -X POST http://localhost:8000/admin/licenses \
  -H "X-Admin-Key: TU_ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"org_name":"Constructora del Norte","plan":"ANNUAL","seats":3,"contact_email":"ing@cliente.com"}'
```

Planes disponibles: `MONTHLY` (30 días), `SEMIANNUAL` (182 días), `ANNUAL` (365 días).

La respuesta trae la clave, con formato `XXXX-XXXX-XXXX-XXXX` (sin las letras
I, O ni los dígitos 0, 1, para que se pueda dictar por teléfono sin confusiones).
Se la entregas al cliente y él la escribe en la pantalla de activación.

Para simular el vencimiento y verificar que el bloqueo funciona:

```bash
curl -X POST "http://localhost:8000/admin/licenses/XXXX-XXXX-XXXX-XXXX/status?new_status=CANCELLED" \
  -H "X-Admin-Key: TU_ADMIN_API_KEY"
```

### D. Revocar un equipo (trabajador que se va, PC que se retira)

```bash
# Lista los equipos para encontrar el id
curl http://localhost:8000/admin/machines -H "X-Admin-Key: TU_ADMIN_API_KEY"

# Da de baja el equipo 5
curl -X POST "http://localhost:8000/admin/machines/5/revoke?reason=Baja%20de%20personal" \
  -H "X-Admin-Key: TU_ADMIN_API_KEY"
```

El equipo dejará de funcionar cuando su token expire (hasta 30 días para el tier
interno). **No es un apagado instantáneo**: es el precio de permitir trabajo sin
conexión. Si necesitas efecto más rápido, baja `TOKEN_TTL_INTERNAL_DAYS`.

---

## Paso 5 — Publicar el ejecutable

```powershell
cd client
dotnet publish src\CadLink.App -c Release -r win-x64 --self-contained true
```

El `.exe` queda en `src\CadLink.App\bin\Release\net8.0-windows\win-x64\publish\`.
Con `--self-contained true` no requiere que el cliente instale .NET, a costa de
un archivo más grande (~150 MB). Si prefieres un instalador liviano, usa
`--self-contained false` y exige el runtime .NET 8 como prerrequisito.

### Antes de distribuirlo

1. **Ofusca el ensamblado.** [ConfuserEx](https://github.com/mkaring/ConfuserEx)
   es gratuito. No hace imposible romperlo, solo lo vuelve más caro que pagar la
   suscripción, que es el objetivo realista.
2. **Firma el ejecutable** con un certificado de firma de código. Sin firma,
   Windows SmartScreen y Defender van a alarmar a tus clientes y muchos no lo
   instalarán. Un certificado OV cuesta del orden de 200–400 USD al año.
3. **Crea un instalador** con [Inno Setup](https://jrsoftware.org/isinfo.php).
4. **Prueba en una máquina limpia**, sin .NET y sin tu entorno de desarrollo.

---

## Estructura del proyecto

```
cadlink/
├── docs/
│   └── arquitectura-licenciamiento.md   Diseño, decisiones y límites del esquema
│
├── server/                              Servidor de licencias (Python + FastAPI)
│   ├── app/
│   │   ├── config.py                    Configuración desde .env
│   │   ├── database.py                  Motor y sesión SQLAlchemy
│   │   ├── models.py                    Machine, License, AuditLog
│   │   ├── tokens.py                    Emisión y firma RS256
│   │   ├── schemas.py                   Validación de entrada/salida
│   │   ├── main.py                      /v1/activate, /v1/renew
│   │   ├── admin.py                     Equipos, licencias, bitácora
│   │   └── webhooks.py                  Pagos: extender, suspender, cancelar
│   ├── scripts/
│   │   ├── generate_keys.py             Par RSA sin OpenSSL (recomendado)
│   │   ├── generate_keys.sh             Par RSA con OpenSSL
│   │   └── register_machine.py          Alta manual de PCs internas
│   ├── requirements.txt
│   └── .env.example
│
├── client/                              Aplicación Windows (.NET 8 + WPF)
│   ├── CadLink.sln
│   └── src/
│       ├── CadLink.Licensing/           Librería de licenciamiento
│       │   ├── MachineFingerprint.cs    Huella de hardware vía WMI
│       │   ├── DomainInfo.cs            SID del dominio de Active Directory
│       │   ├── EmbeddedPublicKey.cs     ⚠️ PON AQUÍ TU LLAVE PÚBLICA
│       │   ├── LicenseTokenVerifier.cs  Verificación RS256, solo RS256
│       │   ├── LicenseCache.cs          Cache cifrado con DPAPI
│       │   ├── LicenseApiClient.cs      Cliente HTTP del servidor
│       │   ├── LicenseService.cs        Política de arranque y gracia offline
│       │   ├── LicenseInfo.cs           Estado para la interfaz
│       │   ├── LicenseTier.cs           Tiers y estados
│       │   └── LicensingOptions.cs      Configuración
│       └── CadLink.App/                 Interfaz de usuario
│           ├── AppInfo.cs               ⚠️ TU MARCA Y URL DEL SERVIDOR
│           ├── App.xaml.cs              Arranque: splash → licencia → ventana
│           ├── SplashWindow.xaml        Pantalla con tu logo
│           ├── ActivationWindow.xaml    Activación interna y externa
│           ├── MainWindow.xaml          Pestañas estilo Excel
│           ├── Models/                  Buses, transformadores, cables, cargas
│           ├── Theme/ExcelTabs.xaml     Estilos de las pestañas
│           └── Assets/logo.png          ⚠️ REEMPLAZA POR TU LOGO
│
└── tools/
    └── make_placeholder_logo.py         Generador del logo temporal
```

---

## Las pestañas

Están **arriba**, debajo de la barra única que lleva el menú y los botones de guardar
en la misma fila:

| Hoja | Qué hace |
|---|---|
| **Proyecto** | Solapa de los planos y juego de planos con su numeración |
| **Secciones Concreto** | La tabla principal. Genera secciones y alzados, con vista previa |
| **Secciones Acero** | Pendiente de portar |
| **Zapatas Corridas** | Pendiente de portar |
| **Zapatas Aisladas** | Pendiente de portar |
| **Muros de Contención** | Pendiente de portar |
| **Placa Base** | Pendiente de portar |
| **Conexiones** | Pendiente de portar |
| **ETABS** | Conexión por la CSI OAPI, lectura del modelo y de los piers, visor 3D y extruido |
| **Dibujar planos estructurales** | La planta por nivel, y el botón *Dibujar en AutoCAD* |
| **Licencia** | Tipo, vigencia, módulos habilitados, huella del equipo, revalidar |

Las columnas calculadas son de solo lectura y se actualizan al instante, igual
que una fórmula de Excel.

### La columna circular

La forma se elige en la columna **Elemento**: hay `COLUMNA` y `COLUMNA CIRCULAR`.
Solo la fila puesta como circular se dibuja redonda; las demás no cambian.

> En el plano las dos se rotulan **COLUMNA**. «COLUMNA CIRCULAR» es solo el nombre
> de captura: en el dibujo lo que distingue a una de otra es su forma y su cota de
> diámetro, no el texto del rótulo.

Su armado se captura en tres columnas:

| Columna | Qué hace |
|---|---|
| **N total** | Varillas **totales** del círculo. En una sección redonda no hay lechos |
| **Var total** | Su diámetro. Si va vacía hereda el de *Var esq sup* |
| **Zuncho helic.** | `SI` = el zuncho sube en hélice; vacío = anillos sueltos |

En una fila circular la columna **Base cm** es el **diámetro** y la altura se
ignora; *Revisar datos* lo avisa si traen valores distintos. El estribo diamante
no aplica: es un rombo entre las varillas de dos lechos y en un círculo no hay
lechos.

### Ayudas de captura de la hoja

- **Paneles inmovilizados.** *Elemento* e *ID* se quedan pegados a la izquierda al
  desplazarse por las 27 columnas, y el encabezado no se va al bajar.
- **Color por grupo.** Identificación, geometría, lecho superior, lecho inferior,
  laterales, círculo, estribos, acabado y calculadas llevan cada uno su tono. El
  lecho superior y el inferior son de colores **distintos** a propósito: es el par
  que se confunde al capturar.
- Las columnas **calculadas** van en gris y cursiva, para que se vea que ahí no se
  escribe.

---

## Siguientes pasos sugeridos

1. **Porta UNA macro**, la más valiosa, al importador de Excel con ClosedXML.
   Compara el resultado contra el que produce tu macro actual antes de seguir.
2. **Porta el dibujo de UN tipo de elemento** (las barras) con netDxf.
3. Recién entonces migra el resto. Portar la lógica de dibujo y verificar que
   produzca resultados idénticos es la parte larga del proyecto, no el
   licenciamiento.
4. Conecta el webhook de tu pasarela de pagos a `POST /webhooks/payment` y ajusta
   `_parse_event` al formato de tu proveedor.

## Consideraciones legales

- Tú vendes **tu** software. El cliente debe tener sus propias licencias de
  AutoCAD y de ETAP. Déjalo explícito en tu EULA.
- No redistribuyas DLLs de Autodesk con tu instalador.
- Incluye una exención de responsabilidad sobre los resultados de ingeniería:
  estás en un dominio donde un error de cálculo tiene consecuencias físicas
  reales. Considera un seguro de responsabilidad profesional.

## Librerías recomendadas para lo pendiente

| Necesidad | Librería |
|---|---|
| Leer Excel sin tener Excel instalado | [ClosedXML](https://github.com/ClosedXML/ClosedXML) |
| Escribir DXF sin tener AutoCAD | [netDxf](https://github.com/haplokuon/netDxf) |
| Plugin nativo de AutoCAD | AutoCAD .NET API (`AcCoreMgd.dll`, `AcDbMgd.dll`) |
| ETAP | [etapAPI, su API REST](https://etap.com/product/etap-rest-api) |
| Ofuscación | [ConfuserEx](https://github.com/mkaring/ConfuserEx) |
| Instalador | [Inno Setup](https://jrsoftware.org/isinfo.php) |
