# Sacar CadLink 1.0.0 al público y cobrarlo por mes

Lo que ya está construido, lo que falta, y en qué orden hacerlo.

---

## 1. Lo que ya existe (no hay que construirlo)

El licenciamiento **está completo de punta a punta**. Conviene tenerlo claro antes de pagar por
nada:

| Ya funciona | Dónde |
|---|---|
| Firma de licencias RSA-2048 / RS256 | `server/app/tokens.py`, verificación en `client/src/CadLink.Licensing/LicenseTokenVerifier.cs` |
| Licencia atada al equipo (huella de placa madre + CPU + disco + Windows) | `MachineFingerprint.cs`; el token lleva la huella en `sub` y copiar el archivo a otra PC no sirve |
| **Suscripción con vencimiento** | `License.expires_at`; planes `MONTHLY` (30 días), `SEMIANNUAL` (182), `ANNUAL` (365) en `server/app/models.py` |
| Corte del servicio al no pagar | `_assert_usable` en `server/app/main.py` devuelve **402** y la app pasa a «Suscripción vencida» |
| Renovación automática sin molestar al cliente | el token dura 7 días y se renueva solo 5 días antes (`/v1/renew`) |
| Que siga trabajando sin internet unos días | gracia de 14 días en comercial (`GRACE_DAYS_COMMERCIAL`) |
| Que no le den la vuelta atrasando el reloj | `LicenseService.cs`, `ClockSkewTolerance` + `TouchLastSeen` |
| Asientos por licencia, suspensión, cancelación, revocación | `server/app/admin.py` |
| Claves de activación `XXXX-XXXX-XXXX-XXXX` | `_new_license_key` en `admin.py` |
| **Webhook de pagos que extiende la vigencia** | `server/app/webhooks.py` |
| Versión de prueba con módulos apagados | tier `TRIAL`: sin `export-dxf` ni ETABS |

Lo que **falta** es el empaquetado y el cobro:

- [x] Instalador — **hecho en este cambio**: `installer/CadLink.iss` + `6-crear-instalador.bat`
- [ ] Firma de código (certificado)
- [ ] Servidor de licencias publicado con HTTPS
- [ ] Pasarela de pago conectada al webhook
- [ ] Endurecer el servidor antes de exponerlo
- [ ] Actualizaciones (hoy hay que reinstalar)

---

## 2. Armar el instalador

```
6-crear-instalador.bat
```

Eso hace todo: publica la aplicación autocontenida, comprueba lo que no debe salir mal, y
empaqueta.

Resultado: **`dist\CadLink-Setup-1.0.0.exe`**, un solo archivo de unos 80 MB. Es lo único que le
mandas al cliente. Él le da doble clic y ya: **no necesita .NET, ni Python, ni la consola, ni
permisos de administrador.**

Antes hay que instalar **Inno Setup 6** una sola vez, en tu máquina, desde
[jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php). Es gratis y sin regalías por
instalador. El `.bat` te lo dice si falta.

### Lo que el `.bat` NO te deja hacer, a propósito

| Se detiene si… | Por qué |
|---|---|
| `cadlink.config.json` apunta a `localhost` | el `localhost` del cliente es **su** máquina: ningún cliente podría activar |
| la dirección no es `https` | sin TLS, cualquiera en la red del cliente puede suplantar al servidor de licencias y regalarse licencias |
| falta la llave pública embebida | la app no podría verificar ninguna licencia; nadie podría activar |
| la publicación no dejó `CadLink.exe` | no empaqueta una carpeta a medias |

Al abrirlo te pregunta qué paquete quieres: **`1`** de prueba, para tu propia máquina y sin
servidor público, o **`2`** para el cliente. Desde la consola también se acepta
`6-crear-instalador.bat prueba`.

> **Los `.bat` de este proyecto son ASCII puro y CRLF, y no es cosmético.** `cmd.exe` lee un
> archivo por lotes por *posición de byte*: con un solo carácter acentuado y `chcp 65001`, o con
> finales de renglón LF, un `goto` reanuda la lectura en el sitio equivocado y **la ventana se
> cierra en menos de un segundo, sin mensaje y sin llegar a ningún `pause`**. Lo vigila
> `tools/verificar_instalador.py` y `.gitattributes` evita que git lo deshaga al entregarlo.

### Lo que el instalador hace bien y no es obvio

- **Se instala sin pedir contraseña de administrador** (`PrivilegesRequired=lowest`). En un
  despacho el ingeniero casi nunca es administrador, y un instalador que pide contraseña se queda
  sin instalar. El que sí lo sea puede elegir Program Files.
- **No pisa lo que el cliente editó**: `cadlink.config.json`, `perfiles-acero.csv` y `aceros.csv`
  se copian solo si no existen. Consecuencia: cuando una versión traiga catálogos nuevos, al que ya
  lo tenía instalado **no le llegan** — se le manda el `.csv` aparte.
- **No borra la licencia al desinstalar**, así que reinstalar no obliga a reactivar.
- **Nunca empaqueta un `.pem`, un `.db` ni un `.pdb`.** Si la llave privada se colara en el
  instalador, cualquier cliente podría emitirse licencias válidas y el cobro dejaría de servir.
  Está en los `Excludes` del guion y lo vigila `tools/verificar_instalador.py`.
- **El `AppId` no se cambia nunca más.** Es con lo que Windows reconoce que la versión nueva es la
  misma aplicación y la actualiza encima en lugar de dejar dos instaladas.

### El icono

**Copia tu `CADLINK.ico` en la carpeta `installer` y ya.** Se toma de ahí con el nombre que tenga,
y con eso quedan los tres iconos de golpe: el del ejecutable, el del acceso directo del escritorio
y el del propio instalador.

Tiene que ser un **`.ico` de verdad**, no un `.png` renombrado: el icono va incrustado en el `.exe`
como recurso de Windows y el compilador rechaza cualquier otra cosa.

Dos cosas que conviene saber:

- **El icono del acceso directo sale del `.exe`**, no del instalador. Por eso no se puede arreglar
  desde el instalador: se incrusta al compilar. Mientras no haya `client\src\CadLink.App\Assets\app.ico`,
  el `.exe` no tiene icono y Windows le pone el genérico — es lo que se veía en el escritorio.
- **Un `.ico` no es una imagen, son varias.** Windows toma 16 px para la barra de tareas, 32 para el
  escritorio, 48 para iconos medianos y 256 para la vista grande. Si tu archivo solo trae la grande,
  Windows la reduce al vuelo y a 16 px queda una manchita. Si el tuyo viene de un diseñador, lo más
  probable es que ya traiga todas; si lo hiciste convirtiendo un PNG en una página web, revísalo.

El `app.ico` que está en el repositorio es un **marcador de posición** (el rayo azul, el mismo dibujo
del logo de muestra). Lo genera `tools/make_icon.py` con las siete medidas, y sirve para que la
cadena completa funcione desde el primer día. En cuanto dejes el tuyo en `installer`, lo reemplaza.

> Si ya tenías un acceso directo con el icono viejo, Windows guarda los iconos en caché y puede
> seguir mostrando el anterior un rato. Reinstalar encima suele refrescarlo; si no, cerrar sesión y
> volver a entrar lo arregla siempre.

### Antes de repartir

Pruébalo en una computadora que **no sea la tuya** y que **no tenga .NET instalado**. Es la única
forma de descubrir que faltó una dependencia.

---

## 3. Firma de código

Sin firma, al cliente le sale la pantalla azul **«Windows protegió tu PC»** y tiene que entrar en
«Más información → Ejecutar de todas formas». Buena parte de la gente no pasa de ahí, y si estás
cobrando, ese es el peor momento para parecer sospechoso.

**Ojo con un cambio del mercado:** desde junio de 2023 los certificados de firma de código ya no se
entregan como archivo `.pfx`; la clave privada tiene que vivir en hardware (token USB o HSM en la
nube), y desde febrero de 2026 la vigencia máxima bajó a poco más de un año
([resumen del cambio](https://dupple.com/blog/how-to-digitally-sign-a-powershell-script),
[métodos de entrega](https://ssldragon.com/how-to/code-signing/certificate-delivery-methods)).
Precios de referencia: **OV ~200–500 USD/año, EV ~290–700 USD/año**
([comparativa](https://www.keyq.cloud/blog/windows-code-signing-with-azure-trusted-signing/)).

Alternativa a considerar: **Azure Artifact Signing** (antes Trusted Signing), un servicio
administrado de Microsoft que se paga por mes y evita el token USB
([producto](https://azure.microsoft.com/en-us/products/artifact-signing)) — revisa los requisitos
de validación de identidad de tu empresa antes de contar con él.

Ya configurado en el `.bat`, solo faltan las variables:

```bat
:: Certificado en archivo
setx CADLINK_FIRMA_PFX    "C:\ruta\certificado.pfx"
setx CADLINK_FIRMA_CLAVE  "la contrasena"

:: O certificado en token USB / almacen de Windows
setx CADLINK_FIRMA_NOMBRE "Nombre exacto de la empresa en el certificado"
```

Firma el ejecutable **y** el instalador, en ese orden, y con sello de tiempo — sin sello, la firma
deja de valer el día que expira el certificado y los instaladores ya repartidos empiezan a dar el
aviso azul.

> Aun firmando, SmartScreen tarda en acumular reputación: los primeros días puede seguir avisando
> aunque la firma sea válida. Con EV la reputación es inmediata; es la diferencia real entre OV y EV.

---

## 4. Publicar el servidor de licencias

Hoy el servidor corre con `uvicorn` en tu máquina y la app apunta a `http://localhost:8000`. Para
cobrar hace falta que esté en internet, con HTTPS y encendido siempre.

Lo mínimo que funciona bien:

1. Un VPS chico (1 vCPU / 1 GB alcanza de sobra: son dos peticiones por cliente por semana).
2. Un dominio, `licencias.tudominio.com`.
3. **Caddy** o nginx delante para el certificado TLS automático.
4. `uvicorn` como servicio (`systemd`) para que arranque solo al reiniciar.
5. `cadlink.config.json` → `"servidorLicencias": "https://licencias.tudominio.com"` y **volver a
   armar el instalador**.

Y dos cosas que se olvidan hasta que duelen:

- **Respalda `server/keys/private.pem`.** Si se pierde, no puedes emitir ni renovar licencias, y
  como la llave pública está embebida en los ejecutables ya repartidos, generar otro par **invalida
  a todos los clientes instalados**. Guárdala en dos lugares, cifrada, fuera del servidor.
- **Respalda la base de datos.** Trae tus clientes, sus equipos y sus vigencias. Cuando pases de
  unos pocos clientes, muévete de SQLite a PostgreSQL (`DATABASE_URL`).

---

## 5. Endurecer el servidor ANTES de exponerlo

Los valores de fábrica son para probar en tu red, no para internet. Revisa `server/.env`:

| Ajuste | Está | Debe estar | Por qué |
|---|---|---|---|
| `AUTO_INTERNAL_FIRST_MACHINE` | `true` | **`false`** | el primer equipo que active se queda con licencia **interna permanente y gratis**. En internet, ese primer equipo es un desconocido |
| `ALLOW_AUTO_TRIAL` | `true` | tú decides | `true` = cualquiera que instale obtiene prueba automática; `false` = solo con clave |
| `TRIAL_DAYS` | `1` | 15 o 30 | un día no alcanza para que nadie evalúe nada. El README dice «30 días» y el código dice 1: **están en desacuerdo** |
| `ADMIN_API_KEY` | valor de ejemplo | una clave larga y aleatoria | con ella se emiten licencias y se revocan equipos |
| `INTERNAL_DOMAIN_SID` | vacío | el SID de tu dominio | es lo que distingue los equipos de tu oficina de los de un cliente |

Falta además, y hay que decidirlo antes de vender:

- **Límite de peticiones.** Sin él, alguien puede pedir pruebas en serie desde máquinas virtuales
  (cada VM nueva es una huella nueva). Lo razonable: límite por IP y tope de activaciones por
  licencia y por día.
- **Liberar el asiento al dar de baja un equipo.** «Liberar este equipo» borra el archivo local
  pero **no avisa al servidor**: el equipo sigue contando contra los asientos hasta que lo revoques
  a mano. Si vas a vender por número de equipos, falta un `POST /v1/deactivate`.

---

## 6. Cobrar el mes

El servidor ya sabe qué hacer cuando le avisan de un pago: `POST /webhooks/payment` extiende
`expires_at` por los días del plan y deja la licencia `ACTIVE`; si el pago falla la suspende, y si
se cancela la cancela. Lo que falta es **quién le avisa**.

### Opción A — a mano (empieza aquí)

Para los primeros clientes es lo más sensato: cero integración, cero riesgo, y te da tiempo de ver
si el precio y el producto cuajan.

1. El cliente te paga por transferencia.
2. Creas la licencia una vez: `POST /admin/licenses` con `plan=MONTHLY` → te devuelve la clave
   `XXXX-XXXX-XXXX-XXXX`.
3. Se la mandas; él la escribe en la ventana de activación.
4. Cada mes que te paga: `POST /admin/licenses/{clave}/extend`.

Si deja de pagar, no haces nada: al vencer, el servidor responde 402 y la app deja de generar
dibujos con un aviso claro. **El corte ya es automático**; lo manual es solo el alta y la
renovación.

### Opción B — cobro recurrente automático

Cuando tengas más clientes de los que quieras extender a mano. **Stripe Billing** opera en México,
cobra en MXN con suscripciones recurrentes y tiene webhooks
([Billing](https://stripe.com/mx/billing/subscriptions),
[webhooks de suscripciones](https://docs.stripe.com/billing/subscriptions/webhooks)). Mercado Pago
es la alternativa local si prefieres cobrar con lo que tus clientes ya usan.

Lo que hay que escribir (no es mucho, pero no está hecho):

1. **Adaptar `_parse_event`** en `server/app/webhooks.py`: hoy es un esqueleto genérico. Hay que
   traducir los eventos reales del proveedor (`invoice.paid`, `invoice.payment_failed`,
   `customer.subscription.deleted`…) a los tres que el servidor entiende.
2. **Cambiar la verificación de firma.** Ahora valida un HMAC-SHA256 plano en `X-Signature`;
   Stripe firma distinto (cabecera `Stripe-Signature`, con marca de tiempo). Tal como está, **un
   webhook real de Stripe no pasaría la verificación**.
3. **Alta automática al primer pago**: crear la `License`, guardar `billing_ref` con el id de la
   suscripción, y mandarle la clave por correo. Hoy eso es manual.
4. **Idempotencia**: guardar el id del evento y no procesarlo dos veces. Las pasarelas reintentan.

Un detalle a favor: como el vencimiento vive en el servidor y el token del cliente dura una semana,
**un pago se refleja solo**; el cliente no tiene que hacer nada, ni reinstalar, ni reactivar.

> Si vas a facturar en México necesitarás CFDI: eso lo resuelve tu contador o el propio proveedor de
> pagos, no el programa.

---

## 7. Actualizaciones

Hoy no hay actualizador: para pasar a 1.0.1 el cliente vuelve a correr el instalador nuevo (se
instala encima, conserva su configuración y su licencia). Para dos o tres clientes está bien.

Cuando sean más, lo natural es un aviso en la app cuando el servidor reporte una versión más
nueva — el cliente ya manda su `app_version` en cada activación y renovación, así que el servidor
**ya sabe** quién está atrasado. Es media tarde de trabajo el día que haga falta.

---

## 8. Orden que yo seguiría

1. Armar el paquete de prueba con `6-crear-instalador.bat` (opción `1`) e instalarlo en tu máquina.
2. Corregir el desacuerdo de `TRIAL_DAYS` (1 día) con lo que dice el README (30).
3. Publicar el servidor con HTTPS y poner `AUTO_INTERNAL_FIRST_MACHINE=false`.
4. Cambiar `cadlink.config.json` a la dirección real y armar el instalador de verdad.
5. Cobrar a mano a los primeros clientes (opción A).
6. Comprar el certificado y firmar, cuando ya sepas que se vende.
7. Conectar la pasarela (opción B) cuando extender licencias a mano estorbe.
8. Actualizador.

Los pasos 1 a 5 no cuestan dinero. El 6 es el único que sí.
