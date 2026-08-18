# Arquitectura de licenciamiento — CadLink

> Renombra `CadLink` por el nombre real de tu producto antes de publicar.
> Este documento es la referencia de diseño; el código lo implementa.

## Principio rector

**Un solo binario para todos.** No existe una "versión interna sin licencia". Las PCs de
la oficina y las de los clientes externos ejecutan exactamente el mismo `.exe`; lo único
que cambia es el **tier** que el servidor de licencias le asigna a cada equipo.

Razón: una build interna sin validación se fuga inevitablemente (USB, empleado que
renuncia, respaldo mal manejado) y una vez fuera no hay forma de desactivarla.

## Niveles de licencia (tiers)

| Tier | Destinatario | Costo | Vigencia de suscripción | TTL del token |
|---|---|---|---|---|
| `INTERNAL` | PCs de trabajadores de la oficina | Gratis | Sin fecha de fin | 30 días |
| `COMMERCIAL` | Clientes externos de paga | Según plan | Fin del periodo pagado | 7 días |
| `TRIAL` | Prospectos | Gratis | 30 días desde la 1ª activación | 7 días |
| `REVOKED` | Equipos dados de baja | — | Bloqueado | — |

### Las dos fechas de expiración

Es la distinción más importante del diseño y la que más se confunde:

- **`exp` (TTL del token)** — vida corta. Obliga al cliente a reconectarse con el
  servidor periódicamente. Es el mecanismo que hace posible la **revocación**.
- **`license_expires_at` (fin de suscripción)** — vida larga. Es lo que el cliente pagó.

Un token de `INTERNAL` no expira como licencia, pero **sí** expira como token cada 30
días. Por eso una PC revocada deja de funcionar dentro de ese plazo aunque nunca
vuelvas a tocarla físicamente.

## Criptografía

- Firma **asimétrica RSA-2048, algoritmo JWT `RS256`** (RSASSA-PKCS1-v1_5 + SHA-256).
  Elegido por ser el que tanto PyJWT como `System.Security.Cryptography` de .NET
  implementan de forma nativa, sin dependencias extra ni riesgo de incompatibilidad.
- La **llave privada vive solo en el servidor**. Nunca en el cliente, nunca en el repo.
- La **llave pública va embebida** en el binario del cliente.
- Consecuencia: el cliente puede *verificar* tokens pero es incapaz de *fabricarlos*.
  Un atacante con acceso total al ejecutable no puede emitirse una licencia válida sin
  parchear el binario (que es un ataque distinto y mitigable con ofuscación + firma
  de código).

> No uses HMAC/secreto compartido. Si el cliente puede validar, también puede firmar,
> y tu esquema completo se cae con un desensamblador.

### Claims del token

```json
{
  "jti": "identificador único del token",
  "sub": "huella-de-hardware-sha256",
  "tier": "INTERNAL | COMMERCIAL | TRIAL",
  "org": "Nombre que se muestra en el splash",
  "iat": 1771200000,
  "exp": 1773792000,
  "license_expires_at": null,
  "grace_days": 21,
  "features": ["autocad", "etap", "export-dxf"]
}
```

## Identificación de equipos

### Huella de hardware (fingerprint)

SHA-256 sobre la concatenación de identificadores estables de la máquina:

1. `MachineGuid` del registro (`HKLM\SOFTWARE\Microsoft\Cryptography`)
2. Número de serie de la placa base (`Win32_BaseBoard`)
3. `ProcessorId` del CPU (`Win32_Processor`)
4. Número de serie del disco donde está el sistema (`Win32_DiskDrive`)

Se toman los componentes disponibles y se ignoran los vacíos: algunas VMs y OEMs no
reportan serial de placa. Si cambia hardware mayor, la huella cambia y el equipo debe
reactivarse — es aceptable y poco frecuente, pero **deja el flujo de reactivación
fácil** para no generar fricción con tus propios trabajadores.

### Auto-inscripción de PCs internas por SID de dominio

El cliente reporta el **SID del dominio de Active Directory** al que está unido. El
servidor lo compara contra `INTERNAL_DOMAIN_SID` de su configuración:

```
¿SID de dominio coincide?  ──sí──▶  ¿hay asientos internos libres?  ──sí──▶  INTERNAL
        │                                        │
        no                                       no
        ▼                                        ▼
     TRIAL                            INTERNAL_SEATS_EXCEEDED (alerta al admin)
```

**Por qué el SID y no el nombre del dominio:** el nombre (`MIEMPRESA.local`) es trivial
de falsificar — cualquiera levanta una VM con un dominio de ese nombre. El SID es un
identificador único generado al crear el dominio, con formato
`S-1-5-21-<4 mil millones>-<4 mil millones>-<4 mil millones>`. No es adivinable.

> El SID **no es un secreto criptográfico**. Es un buen discriminante, no una
> credencial. Si alguien lo obtuviera (por ejemplo un extrabajador) podría clonarlo en
> un dominio propio. Mitigación: la lista blanca de abajo, más la revisión periódica
> del panel de administración.

### Lista blanca de huellas (respaldo y control)

Independientemente del dominio, cada equipo queda registrado en la tabla `machines` con
su huella, y tú puedes:

- **Aprobar** manualmente equipos que no están en el dominio (laptops de trabajadores
  en campo, equipos personales autorizados).
- **Revocar** equipos al dar de baja a un trabajador o retirar una PC.
- **Ver** cuándo se conectó cada equipo por última vez.

**Si no tienes Active Directory:** desactiva la auto-inscripción
(`INTERNAL_DOMAIN_SID` vacío) y registra a mano las huellas de tus PCs. Para una
oficina de 10–30 equipos es perfectamente manejable y es la opción más segura.

## Funcionamiento sin internet (periodo de gracia)

Crítico en este dominio: los ingenieros trabajan en obra, en plantas industriales y en
redes cerradas. Una aplicación que se muere sin conexión es una aplicación que se
desinstala.

```
Token válido y en línea         ──▶  Arranca normal, renueva en segundo plano
Token válido, sin conexión      ──▶  Arranca normal (token aún vigente)
Token expirado, sin conexión    ──▶  Arranca dentro del periodo de gracia,
                                     con aviso visible de días restantes
Gracia agotada                  ──▶  Bloquea y exige reconexión
```

El token se guarda en `%LOCALAPPDATA%\CadLink\license.dat`, cifrado con **DPAPI** con
alcance de usuario. Eso impide copiar el archivo a otra máquina o a otro usuario.

### Protección contra retroceso de reloj

Un ataque obvio: atrasar la fecha del sistema para extender la gracia indefinidamente.

Mitigación: en el cache se guarda `last_seen_utc`, el instante más avanzado que la
aplicación ha observado. Si al arrancar el reloj del sistema es **anterior** a ese
valor, se considera manipulación y se exige validación en línea.

## Revocación — el paso que todos olvidan

Cuando un trabajador se va o retiras una PC:

1. Marcas el equipo como `REVOKED` en el panel de administración.
2. El equipo sigue funcionando con su token en cache **hasta que expire** (máximo 30
   días para `INTERNAL`).
3. Al intentar renovar, el servidor rechaza y la aplicación se bloquea.

Si necesitas efecto inmediato, baja el TTL de `INTERNAL` a 24 horas — a costa de que
los equipos deban ver el servidor a diario. Los 30 días son un balance entre control y
tolerancia al trabajo desconectado.

## Flujo de arranque

```
Inicio del .exe
      │
      ▼
Splash con logo (visible mínimo 1.8 s)
      │
      ├──▶ Se lee el cache local y se verifica la firma
      │
      ├──▶ Si el token está por expirar o ya expiró:
      │       intenta renovar contra el servidor (timeout 5 s, no bloquea la UI)
      │
      ├──▶ Si no hay cache: pantalla de activación
      │       - PC del dominio  ▶ activación silenciosa, sin pedir nada
      │       - PC externa      ▶ pide clave de licencia, u ofrece prueba de 30 días
      │
      ▼
El splash muestra el estado según tier y cede el paso a la ventana principal
```

El splash muestra una línea distinta según el tier, lo que además funciona como señal
antifraude discreta:

- `INTERNAL` → «Licencia interna — Nombre de tu Empresa»
- `COMMERCIAL` → «Suscripción activa hasta 15/03/2027»
- `TRIAL` → «Versión de prueba — 12 días restantes»

## Lo que esta arquitectura NO resuelve

Sé honesto contigo mismo sobre los límites:

- **Un atacante determinado con el binario puede parchearlo.** Ninguna protección del
  lado del cliente sobrevive a alguien con suficiente tiempo y un depurador. Lo que
  buscas es que romperlo cueste más que pagar la suscripción.
- **La ofuscación (ConfuserEx, comercial) eleva el costo, no lo hace imposible.**
- **Tu mejor defensa real es el servicio:** actualizaciones frecuentes, soporte,
  librerías de datos que vivan en tu servidor. Una copia parcheada se queda estancada
  en la versión del día que la rompieron.
- Considera mover los cálculos más valiosos al servidor si el valor del producto lo
  justifica. Es la única protección verdaderamente efectiva.
