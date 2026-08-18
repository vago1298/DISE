# Empieza aquí

Guía para ponerlo a funcionar en tu computadora, escrita para alguien que **no
programa**. No necesitas entender el código. Solo seguir los pasos en orden.

Tiempo aproximado: **40 minutos**, casi todo esperando descargas.

---

## Antes de empezar: qué son las piezas

Tu aplicación tiene **dos partes** que trabajan juntas:

| Pieza | Qué hace | Dónde vivirá |
|---|---|---|
| **La aplicación** | Lo que ven tus trabajadores y tus clientes. La ventana con el logo y las pestañas. | En cada PC |
| **El servidor de licencias** | Decide quién paga y quién no. Lleva la cuenta de los equipos. | En una computadora tuya, siempre encendida |

Por ahora vas a poner **las dos en tu propia computadora**, para probar. Nada
sale a internet todavía. Cuando ya funcione, se mueve el servidor a un lugar
con internet, pero eso es después.

> **¿Qué es un "servidor"?** Solo un programa que se queda esperando preguntas y
> las responde. No es una máquina especial. Tu propia laptop puede serlo.

---

## Paso A — Instalar dos programas

Necesitas instalar estas dos cosas. Son gratis y oficiales.

### A.1 — Python

1. Entra a **https://www.python.org/downloads/**
2. Botón amarillo grande: *Download Python 3.x*
3. Abre el archivo descargado
4. ⚠️ **MUY IMPORTANTE:** antes de dar *Install*, **marca la casilla que dice
   `Add python.exe to PATH`**. Está abajo, en la primera pantalla.
   Si no la marcas, nada va a funcionar y tendrás que desinstalar y repetir.
5. Dale *Install Now* y espera

### A.2 — SDK de .NET 8

1. Entra a **https://dotnet.microsoft.com/download/dotnet/8.0**
2. Busca la columna que dice **SDK** (no la que dice *Runtime*)
3. Descarga el instalador de **Windows x64**
4. Instálalo: siguiente, siguiente, terminar

### A.3 — Reinicia la computadora

Sí, en serio. Los dos programas anteriores cambian configuraciones de Windows
que solo se aplican al reiniciar. Si te lo saltas, los pasos siguientes van a
fallar y no vas a saber por qué.

---

## Paso B — Bajar los archivos del proyecto

Descarga la carpeta del proyecto y descomprímela en un lugar **fácil de
encontrar y sin espacios ni acentos en la ruta**. Por ejemplo:

```
C:\cadlink
```

❌ Evita ponerla en el Escritorio, en *Mis Documentos*, o en OneDrive. Las rutas
con espacios, con acentos, o sincronizadas en la nube causan problemas raros
que cuesta diagnosticar.

Cuando termines, dentro de `C:\cadlink` deberías ver los archivos
`1-instalar-servidor.bat`, `2-iniciar-servidor.bat` y `3-abrir-app.bat`, junto
con las carpetas `client`, `server`, `docs` y `tools`.

---

## Paso C — Los tres dobles clic

### C.1 — Doble clic en `1-instalar-servidor.bat`

Se abre una ventana negra con texto. **Es normal**, así se ven estos programas.
Va a tardar 2 o 3 minutos descargando cosas.

Al terminar, entre todo el texto vas a ver algo así:

```
Se generó automáticamente tu clave de administrador:

    HOCGB2X_RmZYKDZX7Vyna_CYhVO6cBjdCiAVTVkoA-YxjeCjVT05nM2qki_pDBnX
```

📋 **Copia esa clave y guárdala** en un lugar seguro (un archivo de texto, tu
gestor de contraseñas). Es la contraseña de administrador de tu sistema de
licencias: con ella das de alta las PCs de tus trabajadores y emites las
licencias de tus clientes.

Si la pierdes no pasa nada grave: también quedó escrita dentro del archivo
`server\.env`.

Debe terminar diciendo `PASO 1 COMPLETADO`. Pulsa una tecla para cerrar.

### C.2 — Doble clic en `2-iniciar-servidor.bat`

Se abre otra ventana negra y **se queda ahí, aparentemente sin hacer nada**. Eso
es exactamente lo correcto: el servidor está encendido, esperando.

⚠️ **NO CIERRES esta ventana.** Déjala abierta y minimizada mientras uses la
aplicación. Si la cierras, apagas el servidor.

Para comprobar que funciona, abre tu navegador y entra a:

```
http://localhost:8000/docs
```

Debe aparecer una página con una lista de funciones. Esa es la consola de
administración de tus licencias. Más adelante la usarás para dar de alta
equipos y emitir claves.

### C.3 — Doble clic en `3-abrir-app.bat`

**La primera vez tarda varios minutos** compilando. Es normal, solo la primera vez.

Después se abre tu aplicación: primero la pantalla con el logo, y luego la
ventana con las pestañas abajo, como las hojas de Excel.

---

## Qué deberías ver

Al abrirse, la aplicación se identifica sola con el servidor y como es un equipo
desconocido recibe una **licencia de prueba de 30 días**. Arriba a la derecha
dirá algo como *"Versión de prueba — 30 día(s) restantes"*.

Prueba estas cosas para confirmar que todo está bien conectado:

- Ve a la pestaña **Cargas** y cambia un valor de `kW`. Las columnas de `kVA` y
  de corriente se recalculan solas, como una fórmula de Excel.
- Ve a la pestaña **Unifilar**. Verás un diagrama dibujado con los datos de ejemplo.
- Ve a la pestaña **AutoCAD**. El botón *Generar dibujo* está **deshabilitado a
  propósito**: la versión de prueba no incluye exportación. Así es como vas a
  obligar a tus clientes a pagar.
- Ve a la pestaña **Licencia**. Ahí está la huella de tu equipo y el botón
  *Copiar huella*.

Si viste todo eso, **ya tienes el sistema de cobro funcionando**.

---

## Paso D — Convertir tu propia PC en "equipo de la oficina"

Ahora la parte que te interesa: que las PCs de tus trabajadores sean gratis.

1. En la aplicación, ve a la pestaña **Licencia** y pulsa **Copiar huella**
2. Deja la aplicación abierta
3. Abre el navegador en `http://localhost:8000/docs`
4. Busca en la lista **`POST /admin/machines`** y haz clic para desplegarlo
5. Clic en el botón **Try it out**
6. En el cuadro de texto que aparece, pega esto (reemplazando la huella por la
   que copiaste):

```json
{
  "fingerprint": "pega-aqui-tu-huella",
  "tier": "INTERNAL",
  "note": "Mi computadora de pruebas"
}
```

7. Arriba de la página hay un botón **Authorize** 🔓. Haz clic y pega tu clave de
   administrador (la que guardaste en el paso C.1)
8. Vuelve abajo y pulsa **Execute**
9. Regresa a la aplicación, pestaña **Licencia**, y pulsa **Revalidar ahora**

Ahora dirá **"Licencia interna — Ingeniería MiEmpresa"** y el botón de generar
dibujo en la pestaña AutoCAD estará habilitado. Sin fecha de vencimiento y sin
costo.

**Eso es exactamente lo que harás con cada PC de tus trabajadores.** Ellos te
mandan su huella, tú la das de alta, y listo.

> Si tu oficina tiene **dominio de Active Directory** (si tus trabajadores
> inician sesión con un usuario de red administrado por un servidor), esto se
> puede hacer automático y no tendrás que dar de alta ninguna PC a mano. Está
> explicado en el `README.md`, sección *Paso 4, opción 1*. Pregúntale a quien te
> maneje la red si tienen dominio.

---

## Si algo falla

## 🔧 El botón de emergencia

Si algo falla y no sabes qué pasó, **doble clic en `diagnostico.bat`**.

Revisa tu computadora, escribe un archivo `diagnostico.txt` y lo abre solo en el
Bloc de notas. Copia todo ese texto y mándamelo: con eso veo exactamente qué
tienes instalado y en qué paso se atoró, sin que tengas que averiguar nada.

| Lo que ves | Qué significa | Qué hacer |
|---|---|---|
| `ERROR: no encuentro Python` | No está instalado, o no marcaste la casilla | Reinstala Python marcando `Add python.exe to PATH` y **reinicia** |
| `no encuentro el SDK de .NET` | Falta instalarlo, o no reiniciaste | Instala el **SDK** 8 (no el Runtime) y **reinicia** |
| `fallo la descarga de las librerias` | Sin internet, o el antivirus bloqueó | Revisa la conexión y repite el paso 1 |
| `no se pudo crear el entorno` | La ruta tiene acentos, espacios o está en OneDrive | Mueve la carpeta a `C:\cadlink` |
| `EL SERVIDOR NO ARRANCO` | El puerto 8000 está ocupado | Avísame y lo cambio de puerto |
| `HUBO ERRORES AL COMPILAR` | Hay algo que corregir en el código C# | Cópiame las líneas que dicen `error CS` |
| La app dice `No se pudo contactar al servidor` | La ventana del paso 2 está cerrada | Vuelve a ejecutar `2-iniciar-servidor.bat` |
| La ventana se cierra sola al instante | Error muy temprano | Ejecuta `diagnostico.bat` y mándame el archivo |

**Cómo copiar el texto de una ventana negra:** clic derecho dentro de la ventana
→ *Marcar* → selecciona el texto arrastrando → pulsa **Enter**. Ya quedó copiado,
pégalo aquí con `Ctrl+V`.

**Los errores en estos archivos son normales la primera vez.** No están mal
hechos: es que cada computadora tiene una configuración distinta. Cópiame el
texto del error tal cual y lo resolvemos.

> Si Python está instalado pero sin marcar `Add python.exe to PATH`, el paso 1
> hace un **segundo intento con el lanzador `py`**, que Windows instala en la
> carpeta del sistema. Muchas veces funciona sin necesidad de reinstalar nada.

---

## Lo que todavía NO hace

Para que no te lleves una decepción al probarlo: los botones de **Importar desde
Excel**, **Generar dibujo** y **conectar con ETAP** todavía no hacen el trabajo
real. Te muestran un aviso que dice que están pendientes.

Eso es donde va la lógica de tus macros, y para eso necesito ver tus macros.
Todo lo demás —el cobro, las licencias, la interfaz, los cálculos, la vista
previa— ya está funcionando.

---

## Después de que funcione

Cuando ya lo tengas corriendo en tu PC, los siguientes pasos serían:

1. **Poner tu logo y tu nombre real** (te digo qué archivos tocar, son dos)
2. **Portar tu primera macro** para que el botón de Excel sirva de verdad
3. **Mover el servidor a internet** para que funcione fuera de tu oficina
4. **Firmar el ejecutable** para que Windows no lo marque como sospechoso
5. **Conectar la pasarela de pagos** para que los cobros sean automáticos

Uno a la vez. No intentes todo junto.
