# Inventario de `ZAPATA CORRIDA CENTRAL V2` y `ZAPATA CORRIDA LINDERO V2`

Las dos macros, lado a lado, con su estado en el port. Se escribe **antes** de
dibujar nada en AutoCAD: es lo que ya evitó, en las aisladas, portar a ciegas y
descubrir después que faltaba media rutina.

Estados: **Sí** portado · **Falta** pendiente · **N/A** no aplica

---

## 1. Lo que se corrigió al leer el fuente

Este documento y el port se escribieron primero **de memoria**, y al comparar
contra el VBA salieron **cinco errores**. Se dejan escritos porque son
exactamente los que la comprobación de ahora vigila:

| Se había puesto | Lo que dicen las macros |
|---|---|
| `xBase = offsetX` | `xBase = offsetX - anchoZapata / 2`: la sección va **centrada** en su offset. Media zapata corrida por sección, y el rótulo —que se centra en el eje— descuadrado |
| El muro se recortaba al paño de la zapata | **No se recorta.** Un muro más ancho que la zapata se dibuja saliéndose, y así se ve el dato mal capturado |
| Acero del muro a `rec + diámetro / 2` | `offsetMuro = 0.05`: **5 cm clavados** al eje de la varilla |
| Las patas doblaban «hacia el eje de la zapata» | La **central** dobla cada una hacia **su** lado; el **lindero** dobla las dos a la izquierda y a **dos alturas distintas** |
| El enrase se dibujaba con cualquier hueco | `If altEnrase > 0.02`: por debajo de 2 cm no hay enrase |
| La contratrabe se apoyaba en el **lomo** de la zapata, y salía flotando encima | Se apoya en **`yZapBot`**, el paño de arriba de la plantilla: arranca del desplante y atraviesa el espesor. De ahí sale que la línea superior de la zapata se interrumpa |

## 2. Lo que comparten las dos macros

| Qué | Valor | Estado |
|---|---|---|
| Nivel de terreno natural | `yNivTerr = −3.5` | Sí |
| `yBase` de la macro | el **fondo de la plantilla**: `yNivTerr − profundidad − 0.05` | Sí |
| Plantilla de concreto simple | 5 cm, con su texto a f'c 100 kg/cm² | Sí |
| Recubrimiento | 5 cm | Sí |
| Paso entre secciones | 2 m **fijos**, con la zapata centrada en cada offset | Sí |
| Parrillas | la misma rutina que las aisladas: gancho 3 cm, círculos con tolerancia del 20 % | Sí |
| Muro de enrase | piezas de ≈8 cm, junta 1 cm, desfase 1 cm, de 1 a 50 piezas, mínimo 2 cm de hueco | Sí |
| Ancho del enrase | el de la **caja de la cadena de desplante**, no el del muro | Sí |
| Contratrabe y cadena | por **bloque**; la contratrabe se inserta **antes** y su huella manda en el hatch y en la línea superior de la zapata | Sí |
| Apoyo de la contratrabe | en **`yZapBot`**, el paño de arriba de la plantilla: arranca del desplante y atraviesa el espesor | Sí |
| Apoyo de la cadena | **colgada** del nivel de terreno, por su cara de arriba | Sí |
| Acero del muro de concreto | ejes a 5 cm del paño, círculos con la separación **vertical**, uno menos de los que caben | Sí |
| Doblez del muro | 15 diámetros | Sí |
| Cotas | `0.13` ancho total, `0.075` anchos parciales, `0.1445` altura total, `0.0585` alturas parciales | Sí |
| Rótulo | `0.25` título (0.07), `0.34` ELEVACION (0.05), `0.42` f'c + Rec. + Escala (0.04) | Sí |
| Texto «Nivel del terreno» | `xCentro + 0.35 − 0.313`, alto 0.025 | Sí |
| Relleno del concreto (B3 = 1) | SOLID 9 + `AR-CONC` 251 a `0.0003` | Sí |
| Sin relleno (B3 = 2) | `AR-CONC` a `0.0005` en zapata y plantilla, y a **`0.05`** en el muro | Sí |
| Relleno del enrase | pieza SOLID 253, junta SOLID 252, contornos en negro y al frente | Sí |
| Terreno | `EARTH` a `0.01`, transparencia 45, capa gris RGB 135,135,135 | Falta (COM) |

## 3. Lo que las separa

| Decisión | Central | Lindero |
|---|---|---|
| Acomodo de la fila | `offsetX = i · 2`, a la **derecha** desde 0 | `offsetX = −2 − i · 2`, a la **izquierda** |
| Posición del muro | **centrado** en el eje | pegado al **paño derecho**: `xMuroR = xBase + ancho` |
| Eje del acero con una parrilla | el eje de la zapata, que coincide con el del muro | el eje **del muro**, que no es el de la zapata |
| Patas del muro de concreto | cada una hacia **su** lado, a la **misma** altura | las dos a la **izquierda**, a **dos alturas**: la del paño derecho abajo y la del izquierdo `4Ø` (mín. 5 cm) más arriba |
| Recorte de la pata | no lo hace | al recubrimiento del paño izquierdo de la zapata |
| Holgura sobre la parrilla | ninguna | 3 mm |
| Cota de la pata | 4.5 cm sobre su eje | 2.2 cm la de arriba y 45 % de la separación la de abajo |
| Cotas parciales de ancho | **tres** tramos | **dos**: la contratrabe llega al paño derecho |
| Nombre del bloque | el ID tal cual, con `-ZAP` si choca con la contratrabe o la cadena | `ZAPATA_LINDERO_` + ID |
| Título del rótulo | `ZAPATA CORRIDA CENTRAL "Z-1"` | `ZAPATA DE LINDERO "Z-1"` — sin la palabra «corrida», así está en su macro |
| Celda B3 | se lee **una vez** y vale para todo | por sección, heredando la B3 si está vacía |
| Hatch de terreno | por bandas, rodeando cada obstáculo | dos rectángulos, a los lados del muro |
| Rótulos de parrilla | con leader de landing | leader **recto** con flecha sólida dibujada |

Que las filas crezcan en sentidos contrarios no es estético: es lo que permite
que las dos familias convivan en el mismo dibujo sin encimarse.

## 4. Celdas que lee cada una

Cada zapata ocupa **16 renglones** (`filaBase = 4 + n · 16`), y el ID vacío es
la señal de parar. Están anotadas una por una en `ZapataCorridaCad.cs`.

| Dato | Central | Lindero | Estado |
|---|---|---|---|
| ID del elemento | `G1`, `G17`, … | `P1`, `P17`, … | Sí |
| Modo de relleno | `B3` | `B3`, `B19`, … | Sí |
| Ancho, profundidad, espesor | `E4`, `E5`, `E6` | `O4`, `O5`, `O6` | Sí |
| Parrilla inferior: varilla y separación | `C8`, `E8` | `C8`, `O8` | Sí |
| Parrilla inferior transversal | `C10`, `E10` | `C10`, `O10` | Sí |
| Doble parrilla | `H8` | `R8` | Sí |
| Parrilla superior y su transversal | `C12`, `E12`, `C14`, `E14` | `C12`, `O12`, `C14`, `O14` | Sí |
| Tipo de muro | `H4` | `R4` | Sí |
| Cadena de desplante | `H5` | `R5` | Sí |
| Contratrabe | `H6` | `R6` | Sí |
| Espesor del muro, en **cm** | `H9` concreto · `G7` mampostería | `R9` · `P7` | Sí |
| Muro: doble parrilla | `H10` | `R10` | Sí |
| Muro: varilla | `H11` concreto · `H10` mampostería | `R11` · `R10` | Sí |
| Muro: separación horizontal | `H12` · `H11` | `R12` · `R11` | Sí |
| Muro: separación vertical | `H13` · `H12` | `R13` · `R12` | Sí |
| f'c | `J8` | `T8` | Sí |

**Con mampostería las tres celdas del acero suben un renglón**, porque no hay
casilla de doble parrilla. Es la única trampa de la lectura, y leerla mal saca
la varilla del muro de la casilla de al lado.

Las medidas de la zapata están en **metros** y los espesores de muro en
**centímetros** —la macro los divide entre 100—, y esa distinción se conserva:
es la que evita que un muro de 15 cm salga de 15 m.

## 5. Las dos cuentas con truco

### El reparto del muro de enrase

No dibuja piezas de alto fijo. Busca en cuántas piezas iguales cabe el hueco
para que cada una salga lo más cerca posible de los 8 cm de una pieza real:

```
si (yCadenaFondo − yContratrabeLomo) <= 0.02  ->  no hay enrase
para n = 1 … 50:
    alto = (hueco − (n − 1) · 0.01) / n
    gana el n con |alto − 0.08| más chico y alto > 0
```

Así el enrase remata justo contra la cadena, sin media pieza al final. Con 55 cm
de hueco salen **6 piezas de 8.33 cm** y 5 juntas, que suman los 55 exactos.

### Los círculos del muro de concreto

Son las varillas que en el corte se ven de punta, y las reparte la separación
**vertical**. La macro cuenta cuántas caben desde `yMuroBot + Ø/2` hasta
`yNivTerr − Ø/2` y dibuja **una menos**: la de más caía encima de la línea del
nivel de terreno.

## 6. Estado del port, archivo por archivo

| Archivo | Qué es | Estado |
|---|---|---|
| `CadLink.Cad/ZapataCorridaCad.cs` | los datos de la hoja, con las celdas de las dos macros anotadas | Sí |
| `CadLink.Cad/TrazoZapataCorrida.cs` | la geometría pura: acomodo, alturas, muro, enrase, acero del muro, cotas y rótulo | Sí |
| `tools/verificar_zapatas_corridas.py` | las cuentas rehechas, número a número | Sí |
| `tools/prueba-zapata` | los mismos números **ejecutando el C# compilado** | Sí |
| `tools/validar.py` bloque `[22]` | que el port no se salga de las macros ni duplique lo de las aisladas | Sí |
| `CadLink.Cad/ZapataDrawer.Corrida.cs` | el dibujante COM, como parcial de `ZapataDrawer` | Sí |
| `CadLink.App/Models/ZapataCorridaRow.cs` | la fila de la hoja, con sus listas y su `AFormatoCad()` | Sí |
| `CadLink.App` pestaña «Zapatas Corridas» | la hoja de captura, los dos botones y la vista previa | Sí |
| `tools/validar.py` bloque `[23]` | que la pestaña, el modelo, el guardado y el dibujante estén enganchados | Sí |

El dibujante va como **parcial de `ZapataDrawer`** y no como clase aparte: las corridas
necesitan lo mismo que las aisladas para hablar con AutoCAD —líneas, hatches, cotas, textos,
el reintento cuando el programa está ocupado, el bloque propio, el orden de dibujo—, y eso son
ochocientas líneas de COM ya probadas contra el AutoCAD del usuario. Lo propio de esta hoja
—acomodo, contratrabe y cadena como bloque, muro de enrase, muro de concreto con su acero,
terreno y anotación— vive solo en ese archivo.

## 7. Lo que la hoja hace distinto de las macros, a propósito

Cinco cosas, y ninguna cambia el dibujo:

0. **Las casillas que no aplican se apagan.** Con muro de **mampostería** se apagan las
   cuatro del armado del muro; con muro de **concreto** se apaga la de la cadena de
   desplante. Las macros leen esas celdas y luego no las usan, así que dejarlas
   escribibles invita a capturar un dato que el plano ignora —y después a no entender por
   qué no sale—. Se apagan solas, con `IsEnabled` enlazado a la fila.


1. **El estilo de dibujo y el doblez del acero del muro son del juego entero**, no de cada
   sección: los radios están atados a los de la hoja de concreto y el doblez se lee de la
   casilla de la hoja de aisladas. En las macros, la central lee `B3` una vez y la de lindero
   la lee por sección heredando `B3`; el criterio de la de lindero es el bueno y es el que
   quedó. Media obra a 15 diámetros y la otra media a 40 no es un plano.
2. **La contratrabe y la cadena se eligen de una lista** con las que ya están capturadas en la
   hoja de concreto, en lugar de teclear el ID a ciegas. La celda sigue siendo editable: el
   bloque puede existir en el dibujo sin estar en la hoja, y eso es corriente.
3. **La columna «Falta» avisa de dos cosas que las macros no comprueban**: la varilla de la
   parrilla superior cuando se pidió doble parrilla, y la del muro de concreto. Las macros
   dibujan y dejan el muro sin acero sin decir nada.

## 8. Los rótulos con leader, uno por uno

| Rótulo | Central | Lindero | Estado |
|---|---|---|---|
| Parrillas | mismo texto y mismas distancias que las aisladas | igual, con la franja recortada por la contratrabe | Sí |
| Muro de enrase | por la **derecha** de la hilada, a 10 cm | por la **izquierda**, a 30 cm, con el leader desde el borde derecho del rótulo | Sí |
| Contratrabe | `xCentro − 0.62`, 30 cm sobre su centro | `xCentroMuro − 0.75`, 14 cm sobre su lomo y punta 4 cm por debajo | Sí |
| Cadena de desplante | `xCentro − 0.78`, a su altura | `xCentroMuro − 0.85` | Sí |
| Muro de concreto | `xMuroDer + 0.12 − 0.05`, ancho 0.32, anclado a la izquierda | `xMuroIzq − 0.27`, ancho 0.25, centrado | Sí |
| Punta del leader del muro | la varilla del paño derecho si hay dos, al 55 % de la altura | igual | Sí |
| Cota de la pata | 4.5 cm sobre su eje, las dos iguales | 45 % de la separación la de abajo, 2.2 cm la de arriba | Sí |
| Máscara de fondo | todos los MText, para que el terreno no se lea por detrás | igual | Sí |

**Dos decisiones que conviene saber.** El hatch de terreno de la central abre una
**isla** por cada rótulo; aquí no se abren islas porque los MText llevan máscara de fondo, que
tapa igual y no depende de recalcular el hatch cuando un rótulo se mueve. Y las **cotas de las
patas** del muro se dibujan fuera del bloque, como en las macros: dentro quedarían pegadas a la
geometría y no se podrían mover en el plano.

## 9. Lo que queda para el dibujante

No es geometría: es todo lo que necesita el dibujo abierto, y por eso no vive en
`TrazoZapataCorrida`.

1. **Los bloques.** Contratrabe alineada por su esquina —inferior derecha en el
   lindero, centro inferior en la central— y cadena de desplante por la
   superior. La caja de los dos manda en el hatch, en el hueco de la línea
   superior de la zapata y en el ancho del enrase.
2. **El hatch de terreno.** La central lo parte en bandas horizontales para
   rodear cada obstáculo y le abre un hueco por cada rótulo; el lindero dibuja
   dos rectángulos a los lados del muro.
3. **Los leaders y los rótulos de parrilla**, con su cola dibujada al final para
   que queden al frente del hatch y del bloque.
4. **El orden de dibujo** de los contornos del enrase, con `ACAD_SORTENTS`,
   igual que ya se hace con los estribos de las aisladas.
5. **El corte de las varillas del muro** en cada cruce con el acero de la
   zapata, que en la central se hace con oclusores y en el lindero no.
