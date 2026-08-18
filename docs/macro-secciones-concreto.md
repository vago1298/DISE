# Especificación: Secciones Estructurales de Concreto

Ingeniería inversa de la macro VBA `SECCIONES ESTRUCTURALES COTAS Y ROTULOS V3`,
hoja *Secciones Estructurales Concreto*. Este documento es la referencia para
portarla a C# sin perder comportamiento.

**Flujo:** Excel → AutoCAD (por COM). Esta macro **no toca ETABS**.

---

## 1. Columnas de la hoja

Se recorre de la fila 2 hasta la última con dato en la columna A. Una fila se
procesa solo si **B (ID) no está vacía**.

| Col | Contenido | Tipo | Notas |
|---|---|---|---|
| A | Tipo de elemento | texto | Va en MAYÚSCULAS en el rótulo |
| B | **ID del elemento** | texto | **Nombre del bloque de AutoCAD.** Si el bloque ya existe, la fila se salta |
| C | Base | cm | |
| D | Altura | cm | |
| E | Nº varillas de esquina, lecho superior | entero | |
| F | Diámetro de esas varillas | `#3`, `#4`… | |
| G | Nº varillas intermedias, lecho superior | entero | |
| H | Diámetro | texto | Si vacío → toma **F** |
| I | Nº varillas de esquina, lecho inferior | entero | |
| J | Diámetro | texto | Si vacío → toma **F** |
| K | Nº varillas intermedias, lecho inferior | entero | |
| L | Diámetro | texto | Si vacío → toma **J** |
| M | Nº varillas intermedias laterales **por lado** | entero | Se dibujan en ambos costados |
| N | Diámetro | texto | |
| O | Recubrimiento | cm | |
| P | Diámetro del estribo | `#3` | |
| Q | Separación de estribos | texto | Admite varios tramos: `10-15-20` |
| R | ¿Estribo diamante? | `SI` / vacío | Comparación en mayúsculas |
| S | Diámetro del estribo diamante | texto | Si vacío → toma **P** |
| T | Longitud del gancho sísmico | cm | 0 = sin gancho |
| U | f'c | texto | Se rotula como `f'c=… kg/cm²` |
| V | Escala | texto | Se rotula como `Escala 1:…` |
| **AC** | **Modo de dibujo global** | `1` o `2` | Ver abajo |

### Modo de dibujo (columna AC)

Se lee **una sola vez** y aplica a **todas** las secciones de la tabla. Se toma
`AC2`; si está vacía, el primer `1` o `2` que aparezca en la columna AC; si no
hay ninguno, se asume `1`.

| AC | Constante de la macro | Efecto |
|---|---|---|
| `1` | `MODO_RELLENA` | Fondo sólido ACI 9 + patrón AR-CONC 251 + estribo sólido 152 + contorno del estribo **negro** |
| `2` | `MODO_SOLO_HATCH` | Únicamente AR-CONC 251. Sin fondo sólido, sin estribo relleno, contorno **por capa**. Las colas de los ganchos se usan como islas para que el rayado no las cruce |

El patrón **AR-CONC se dibuja en los dos modos**. Lo único que cambia entre ellos
es el fondo sólido, el relleno del estribo y el color del contorno.

> ### Los números de la aplicación van AL REVÉS que la celda AC
>
> A pedido expreso del usuario, en la aplicación **tipo 1 = sin relleno** y
> **tipo 2 = rellena**, o sea lo contrario de la celda AC:
>
> | | Sin relleno | Rellena |
> |---|---|---|
> | Celda AC de la macro | `2` | `1` |
> | Tipo en la aplicación | `1` | `2` |
>
> Por eso el importador de Excel **no puede** hacer `(ModoSeccion)ac`: eso
> invertiría el estilo de todas las secciones. La traducción está escrita en un
> solo sitio, `ModoSeccionExt.DesdeCeldaAC`, y hay que usarla siempre.
>
> **Confirmado con el autor:** en la hoja real `AC = 1` es **rellena**, igual que
> en el código VBA (`MODO_RELLENA = 1`). El cruce está solo entre la celda AC y la
> numeración de la aplicación, no dentro de la hoja.

---

## 2. Escala y unidades

```
escala = 0.01        →  1 cm de la hoja = 0.01 unidades de dibujo
```

Es decir: **se captura en centímetros y se dibuja en metros.**

Diámetros nominales de varilla, en cm:

| Clave | cm | Capa | Color ACI |
|---|---|---|---|
| `#2` | 0.60 | `VAR_#2` | 150 |
| `#2.5` | 0.80 | `VAR_#2.5` | 6 |
| `#3` | 0.95 | `VAR_#3` | 132 |
| `#4` | 1.27 | `VAR_#4` | 142 |
| `#5` | 1.59 | `VAR_#5` | 160 |
| `#6` | 1.90 | `VAR_#6` | 4 |
| `#8` | 2.54 | `VAR_#8` | 1 |
| `#10` | 3.20 | `VAR_#10` | 6 |
| `#12` | 3.80 | `VAR_#12` | 15 |

Otras capas: `CONCRETO` (8), `ESTRIBOS` (150), `TEXTOS` (3), `ROTULOS` (3 verde),
`COTAS` (253).

---

## 3. Geometría

Con `p0` = esquina inferior izquierda del concreto, `rec` = recubrimiento,
`dEst` = diámetro del estribo, `dSup`/`dInf` = diámetro de las varillas de
esquina de cada lecho (todo ya en unidades de dibujo):

### Concreto
Polilínea cerrada de `base` × `altura` desde `p0`, capa `CONCRETO`.

### Estribo
Se dibuja como **dos contornos** con esquinas redondeadas, no como un offset:

```
Frontera exterior:  x1 = p0x + rec              radios: rfInf = dEst + dInf/2
                    y1 = p0y + rec                      rfSup = dEst + dSup/2
                    x2 = p0x + base - rec
                    y2 = p0y + altura - rec

Frontera interior:  desplazada dEst hacia adentro   radios: rInf = dInf/2
                                                            rSup = dSup/2
```

Los radios se derivan del diámetro de la varilla que el estribo abraza: el
estribo queda **tangente** a la varilla de esquina. Ese es el detalle que hace
que el dibujo se vea correcto y hay que preservarlo con exactitud.

### Ganchos sísmicos (a 135°)
En la **esquina superior derecha**. Centro del doblez en
`(x2 - dEst - rIn, y2 - dEst - rIn)` con `rIn = dSup/2`, `rOut = rIn + dEst`.
Dirección de las colas: `(-1/√2, -1/√2)`, longitud = columna T.

Cuando el gancho existe, los arcos de esa esquina se dibujan con barrido
extendido (`1.75π → 0.5π` exterior, `1.75π → 0.75π` interior) y la línea
interior derecha se **recorta** si la cola la cruza.

### Varillas longitudinales

Separación del paño: `off = rec + dEst + dVar/2`

- **Lechos de esquina:** `nVars` repartidas de `off` a `base - off`.
  Con `nVars = 1` va al centro.
- **Lechos intermedios:** entre `xIni` y `xFin` con
  `paso = (xFin - xIni)/(nVars + 1)`, posiciones `i·paso` para `i = 1..nVars`
  (quedan **entre** las de esquina, no encima).
- **Laterales (columna M):** `hueco = altura - offSup - offInf`;
  `paso = hueco/(nVarInter + 1)` si hay más de una, `hueco/2` si hay una sola.
  Se dibujan en **los dos costados**.

### Estribo diamante (rombo)
Se construye como una **cinta tangente a N círculos** (`CrearCintaConFillet`):
tangentes exteriores comunes entre círculos consecutivos, unidas con arcos.
Los círculos son, en orden:

1. Centro derecho: `(x2 - rEsqExt, cyS)` con radio `rEsqInt`
2. Varilla(s) superior(es) más cercana(s) al centro
3. Centro izquierdo: `(x1 + rEsqExt, cyS)`
4. Varilla(s) inferior(es) más cercana(s) al centro

`SeleccionaVarillasCentro` elige una varilla si está a menos de
`0.5·radio` del centro de la sección, o las dos que lo flanquean si no.

Después se **recorta** el estribo principal que quede bajo la banda del diamante
(`TrimEstriboBajoDiamante`) y los tramos verticales de los leaders
(`TrimVerticalesEnDiamante`).

---

## 4. Hatch de concreto: dos partes

Se aplica **al final**, cuando ya están estribos, varillas y diamante:

- **Parte 1** — entre la cara del concreto y la frontera **exterior** del estribo
- **Parte 2** — dentro de la frontera **interior**, con las varillas y el
  diamante como **islas** (no se rayan)

El cuerpo del estribo queda sin hatch: es justamente la franja entre las dos
fronteras. Cada parte lleva fondo sólido + `AR-CONC` a escala `0.0003`
(respaldo `ANSI31` si el patrón no existe).

Todo se manda al fondo con `ACAD_SORTENTS`, y luego se sube al frente todo lo
que no sea relleno, para que el contorno negro del estribo nunca quede tapado.

---

## 5. Rotulado

MText bajo cada sección, en `(xCentro, yBase - 0.06)`, anclaje TopCenter,
ancho 0.45, altura 0.03, estilo `SECCIONES` (fuente *Bahnschrift SemiLight*):

```
VIGA
"V-101"
4 vars. #6 C
6 vars. #4 C
Estr. #3 @10-15-20 cm
Est. Diamante #3 @10-15-20 cm     ← solo si R = SI
Rec. 4 cm
f'c=250 kg/cm²
Escala 1:25
```

Las varillas se agrupan por diámetro sumando los cuatro lechos más
`2 × laterales`, y se ordenan de **mayor a menor** diámetro.

### Leaders por lecho
Espina horizontal con una línea vertical y una punta de flecha triangular
(rellena) por varilla, y el texto `N vars. #X C` al extremo izquierdo.
Si el lecho de esquina y el intermedio tienen el **mismo** diámetro se agrupan en
un solo leader; si no, se dibujan en dos niveles desplazados
(`LECHO_SEP_Y = 0.032`, `LECHO_SEP_X = 0.045`).

### Cotas
`AddDimRotated` horizontal arriba y vertical a la derecha, estilo
`COTA_ESTRUCTURAL`: flecha `_OPEN90` de 0.02, líneas ACI 253, texto ACI 1 de
0.017, 2 decimales, longitud fija de extensión 0.035.

---

## 6. Bloques

Cada sección se agrupa en un bloque **con el nombre del ID** (columna B), con el
origen en el **centroide del bounding box** de la geometría (excluyendo cotas y
rótulos). Se insertan en el mismo punto, así que visualmente no se mueven.

Posición de la primera sección: `max(X de los bloques existentes) + 0.7`, o `0`.
Avance entre secciones: `base + 0.35`.

`ActualizarSecciones` guarda el punto de inserción anterior de cada bloque y
redibuja **en el mismo sitio**, incluso si el usuario movió la sección a mano.

---

## 7. Puntos de entrada públicos

| Macro | Qué hace |
|---|---|
| `DibujarSecciones` | Principal. Dibuja las secciones nuevas (salta las que ya son bloque) |
| `ActualizarSecciones` | Redibuja en el mismo sitio con el estilo actual de AC |
| `RedibujarTodasLasSecciones` | Borra los bloques de la tabla y vuelve a dibujar |
| `EstribosAlFrente` | Reordena la capa ESTRIBOS al frente en todo el dibujo |
| `PonerEstribosEnNegro` | Fuerza el contorno negro en todo el dibujo |
| `NormalizarRotuladoExistente` | Pone ByLayer todo lo de la capa ROTULOS |
| `InstalarReactorPlotNegro` / `DesinstalarReactorPlotNegro` | Reactor LISP de impresión |
| `RotulosNegro_ParaImprimirDesdeModel` / `RotulosVerde_EnPantalla` | Interruptores manuales |
| `QuitarOverrideNegroRotulos` | Quita los overrides de viewport |

---

## 8. Verde en pantalla, negro al imprimir

El requisito: la capa `ROTULOS` se ve **verde** en el Model pero sale **negra**
en el PDF. La macro lo resuelve por tres vías simultáneas:

1. Si el dibujo usa estilos nombrados (`.stb`), asigna el estilo `Negro` a la capa
2. Override de color **por viewport** (`-VPLAYER`) en todos los layouts
3. Un **reactor de AutoLISP** que escribe en `ROTULOS_NEGRO_AL_PLOTEAR.lsp`,
   junto al libro, y lo registra en `acaddoc.lsp` de la carpeta de soporte de
   AutoCAD para que se cargue solo en cada dibujo. El reactor pone la capa en
   negro al iniciar `PLOT`/`EXPORTPDF`/`PUBLISH` y la devuelve a verde al terminar

> ⚠️ **Riesgo para un producto comercial.** El punto 3 escribe dentro de la
> carpeta de soporte de AutoCAD del usuario y modifica `acaddoc.lsp`, que es
> configuración compartida. En una instalación corporativa eso suele estar
> bloqueado por permisos, y es justo el comportamiento que los antivirus y los
> departamentos de sistemas marcan como sospechoso. Ver la sección de riesgos.

---

## 9. Problemas detectados que conviene corregir al portar

### 9.1 Diámetro desconocido falla en silencio  🔴

`DiamEnDibujo` busca la clave **cruda** de la celda en la colección:

```vba
v = varillaDiametros(clave)        ' clave = "#4" exacto
If v <= 0 Then v = fallback_cm * escala
```

Si la celda trae `4`, `No. 4`, `#4mm` o cualquier variante, **no lanza error**:
usa el valor por omisión (0.95 cm para varilla, 0.6 cm para estribo) y **dibuja
la sección con el diámetro equivocado sin avisar**. Existe `SoloDiametroHash`
para normalizar, pero no se usa antes de esta búsqueda.

**Al portar:** normalizar primero y, si el diámetro sigue sin reconocerse,
**rechazar la fila con un mensaje claro** en lugar de dibujar algo incorrecto.
En un plano estructural, una varilla dibujada con el diámetro equivocado es un
error que puede llegar a obra.

### 9.2 `On Error Resume Next` generalizado  🟠

Está en casi todas las rutinas. Explica el síntoma que el propio código
documenta: *"ahi era donde algunas secciones se quedaban con el color de la
capa"*. Los fallos intermitentes de COM se tragan en silencio y el resultado es
un dibujo sutilmente distinto, sin ninguna pista de qué pasó.

**Al portar:** capturar excepciones de forma explícita, registrarlas en un log y
reportar al usuario **qué fila** falló y por qué.

### 9.3 Rendimiento  🟠

Varios puntos son cuadráticos o hacen muchas llamadas COM:

- `PuntoDentroDePoly` **crea y borra una línea temporal** en el Model por cada
  prueba de punto. Se llama dos veces por cada segmento evaluado en los recortes
  del diamante.
- `TrimEstriboBajoDiamante` recorre el Model y hace `IntersectWith` por candidato
- `SubirTodoMenosRellenos` y `ObtenerPosicionInicialX` recorren todo el Model
- Los ordenamientos son burbuja

Cada llamada COM cruza la frontera de proceso hacia AutoCAD, y eso es lo que
domina el tiempo. Con pocas secciones no se nota; con una tabla grande sí.

### 9.4 Detalles menores

- `escala` es constante: no se puede dibujar en otras unidades
- `CrearCapa` fuerza el color **cada vez** que corre: pisa el color si el usuario
  lo cambió a propósito
- `ParsearSeparaciones` reemplaza `,` por `.`: en configuración regional con coma
  decimal, `10,5` se vuelve `10.5`, que es lo correcto, pero conviene volverlo
  explícito
- `LeeNumero` limpia `"m."` antes que `"m"`, y `"mm"` antes que `"m"`; el orden
  importa y es frágil

---

## 10. Traducción de la API a C#

La API COM de AutoCAD tiene **los mismos nombres** en VBA y en C#, así que la
traducción es mecánica:

| VBA | C# (COM tardío o interop) |
|---|---|
| `GetObject(, "AutoCAD.Application")` | ⚠️ `Marshal.GetActiveObject` **no existe en .NET 8** — hace falta un P/Invoke a `GetActiveObject` de `oleaut32.dll` |
| `moSpace.AddLightWeightPolyline(pts)` | igual; `double[]` en lugar de `Dim pts() As Double` |
| `pl.SetBulge i, b` | igual |
| `moSpace.AddHatch(0, patrón, False)` | igual |
| `h.AppendOuterLoop arr` | igual; `object[]` |
| `ent.IntersectWith(otro, 0)` | igual; devuelve `double[]` |
| `d.AddObject("ACAD_SORTENTS", "AcDbSortentsTable")` | igual |
| `acadDoc.SendCommand` | igual |
| `Collection` con clave | `Dictionary<string, double>` |
| `Scripting.Dictionary` | `Dictionary<string, T>` |
| `On Error Resume Next` | `try` / `catch` explícito |

Las fórmulas de geometría (radios, tangentes, bulges, sectores anulares) se
copian **tal cual**: son aritmética pura y no dependen del lenguaje.


---

## Estado del port: qué falta todavía

Comparación contra `SECCIONES ESTRUCTURALES COTAS Y ROTULOS V3`. Nada de esto
se ha podido ejecutar: el entorno de desarrollo es Linux y no tiene .NET,
AutoCAD ni ETABS, así que la revisión es de código, no de resultado.

### Ya portado

| Pieza de la macro | Estado |
|---|---|
| Concreto, estribo exterior e interior con radios tangentes a la varilla | Sí |
| Ganchos sísmicos a 135°, con recorte de la línea interior | Sí |
| Varillas: 4 lechos + laterales, con relleno sólido | Sí |
| Hatch de concreto en 2 partes, con las varillas como islas | Sí |
| Modo `AC` 1 / 2 (rellena / no rellena) | Sí |
| Relleno sólido del estribo, cuerpo + doblez + colas | Sí |
| Llamadas por lecho: espina, flechas y `N vars. #X C` | Sí |
| Llamada por varilla lateral | Sí |
| Agrupado de llamadas cuando esquina e intermedia comparten diámetro | Sí |
| Rótulo MText con todas sus líneas | Sí |
| Separaciones tipo `5-10-15` | Sí |
| Cotas con `COTA_ESTRUCTURAL` y estilo `SECCIONES` | Sí |
| Bloque con origen en el centroide, excluyendo COTAS y ROTULOS | Sí |
| Contorno del estribo en negro por color verdadero | Sí |
| Capas y colores ACI de la macro | Sí |
| Estribo diamante: cinta tangente a N círculos (`CrearCintaConFillet`) | Sí |
| `SeleccionaVarillasCentro`: 1 varilla en el eje, o 2 si el eje cae entre dos | Sí |
| `TrimEstriboBajoDiamante` / `TrimVerticalesEnDiamante` | Sí, ver abajo |
| `BloqueYaExiste`: la sección ya dibujada se **salta** | Sí |

### Recorte del estribo bajo el diamante

La macro lo resuelve con `IntersectWith` sobre la geometría ya dibujada. El port
usa **geometría cerrada** en su lugar, y no es un atajo: es lo que permite que el
recorte sea seguro.

El borde exterior de la cinta, alrededor de cada círculo que abraza, es un arco
de radio `R + dDia` centrado en ese círculo. Un tramo recto del estribo a
distancia perpendicular `p` de ese centro queda tapado en un ancho de
`±√((R+dDia)² − p²)`. Es una fórmula, no una prueba de «está dentro del
polígono», que es justo lo que se quería evitar: equivocarse en un test de
interioridad borra el estribo.

Tres seguros, en `RecortarEstriboBajoDiamante`:

1. El tramo original **solo se borra si los trozos nuevos ya se dibujaron**.
2. Si el hueco calculado tapara más del 60 % del tramo, **no se recorta** y se
   avisa. El peor caso legítimo medido es 45.7 % (castillo 15×15 con 4 varillas).
3. Los huecos más angostos que 0.5 mm se descartan. Cuando el diamante es del
   mismo calibre que el estribo, su cinta queda **exactamente tangente** a la
   línea exterior: en coma flotante eso da un hueco de ancho 1e-19 en lugar de
   cero, y sin este filtro el tramo se borraba para redibujarlo troceado por
   nada. Lo encontró `tools/verificar_recorte_diamante.py`, no la lectura del
   código.

Lo que **no** se hace: recortar las colas del gancho. Son diagonales de 45° en la
esquina, y el diamante se dobla en el centro y a media altura, nunca ahí.

La región tapada es la que encierra el borde exterior de la cinta, y son **dos**
cosas: los discos de radio `R + dDia` de cada círculo abrazado (los dobleces) y el
polígono que pasa por los puntos de tangencia (los tramos rectos). Con solo los
discos, las diagonales del diamante cruzaban las líneas del estribo sin cortarlas.

### El diamante rodea la varilla lateral — no está en la macro

En la macro el doblez lateral del diamante es un **círculo ficticio** puesto a
`rEsqExt` del paño, sin mirar si ahí hay una varilla. En un armado normal la hay:
la varilla lateral va justo a media altura del costado. Resultado: la cinta le
pasaba por encima y en el dibujo la diagonal cortaba la varilla por la mitad.

La corrección es abrazarla, no esquivarla. Si el costado tiene varillas laterales,
el doblez **es** la más cercana a media altura, con su radio real: `DoblezLateral`.
La cinta ya sabe abrazar una serie de círculos, así que sale tangente sola.

Como el doblez solo puede abrazar una varilla por costado, queda `RodearLaterales`
como red de seguridad: mete en el recorrido cualquier otra varilla que un tramo
recto atravesaría, en el orden del recorrido para que la cinta no se anude, y da
varias pasadas porque rodear una varilla empuja la cinta y puede cruzar otra. Si el
recorrido nuevo no da una cinta válida, se vuelve al de partida: mejor un diamante
que cruza una varilla que ningún diamante.

Verificado en `tools/verificar_recorte_diamante.py`: en cuatro armados reales no
queda ninguna lateral atravesada, el recorrido sigue siendo antihorario y la cinta
se construye.

### Pendiente

1. **Fondo sólido como propiedad `BackgroundColor` del hatch.** La macro lo
   intenta primero así y solo si falla crea un hatch `SOLID` aparte. El port usa
   directamente esa segunda vía, que es la de respaldo de la propia macro: el
   resultado en pantalla es el mismo, pero son dos objetos en lugar de uno.
3. **Reactor LISP** que pone la capa ROTULOS en negro solo mientras se plotea,
   y los overrides de color por viewport. El rotulado ya se dibuja *por capa*,
   que es la condición para que eso funcione, pero el reactor no se instala.
4. **Actualizar en sitio** (`ActualizarSecciones`): redibujar respetando la
   posición previa de cada bloque.
5. **Importador de Excel**: leer la hoja con las columnas A–V y AC.
6. `DiamEnDibujo`: la macro cae en silencio a un diámetro por omisión cuando la
   clave no existe, y dibuja una varilla del grosor equivocado sin avisar. El
   port avisa. Es una diferencia **deliberada**.
