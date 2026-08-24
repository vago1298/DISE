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
| Parrilla | **un rótulo por parrilla**, con su cabecera y sus dos flechas: la de abajo a la izquierda y la de arriba a la derecha | reparto por tipo de varilla, apilado | Sí |
| Muro de enrase | **siempre a la derecha** de la hilada, a 6 cm de su paño, con la flecha **en el paño** | igual | Sí |
| Contratrabe | flecha a su **esquina superior derecha**, con el renglón corrido 6 cm a la izquierda | igual | Sí |
| Cadena de desplante | texto **siempre despegado 5 cm** de su paño, en 26 cm de ancho | igual | Sí |
| Muro de concreto | **siempre a 6 cm** del paño derecho, en 25 cm de ancho, con la «C» y el número en las dos varillas, y la flecha **en el paño** | por la izquierda, como su macro | Sí |
| Nivel del terreno | a la **izquierda**, arrancando en el paño izquierdo de la zapata | igual | Sí |
| Muro de concreto | `xMuroDer + 0.12 − 0.05`, ancho 0.32, anclado a la izquierda | `xMuroIzq − 0.27`, ancho 0.25, centrado | Sí |
| Punta del leader del muro | la varilla del paño derecho si hay dos, al 55 % de la altura | igual | Sí |
| Cota de la pata | 4.5 cm sobre su eje, las dos iguales | 45 % de la separación la de abajo, 2.2 cm la de arriba | Sí |
| Máscara de fondo | todos los MText, para que el terreno no se lea por detrás | igual | Sí |

**Cabecera arriba de todo.** El primer renglón dice de qué parrilla se está hablando —`PARRILLA
INFERIOR` o `PARRILLA SUPERIOR`—, y debajo van las varillas. Hace falta en cuanto hay dos: la palabra
del lecho dice en qué cama va cada varilla **dentro** de su parrilla, no de qué parrilla es.

```
PARRILLA INFERIOR            PARRILLA INFERIOR
VAR #4C @ 20 cm              VAR #3C @ 20 cm
INFERIOR                     AMBOS SENTIDOS
VAR #3C @ 15 cm
SUPERIOR
```

**Cada leader sale de su palabra, con quiebre y cada uno por su lado.** Cada flecha arranca **donde
acaba la palabra** de su renglón —el que dice `INFERIOR`, `SUPERIOR` o `AMBOS SENTIDOS`— con una
**cola horizontal de 6 cm** y de ahí en diagonal hasta la varilla. Los renglones de un MText van
**centrados** en su ancho de columna, así que el borde del bloque no dice dónde acaba la palabra:
entre el final de `INFERIOR` y el borde puede haber 6 cm de aire, y la cola que arrancaba en el borde
parecía suelta. Así que el renglón se **mide** —se crea, se mide y se borra— y la cola se pega al
final de la palabra.

La de **flexión** sale por el borde **izquierdo** y la de **temperatura** por el **derecho**, así que
las dos líneas se abren en lugar de cruzarse. Y cada una se pega a la varilla que tiene **debajo**: la
de flexión al punto de la barra más cercano —es una línea continua, sirve cualquier punto— y la de
temperatura a la varilla de punta más cercana. Con un solo armado salen **las dos** igual: son dos
varillas, una de canto y otra de punta, y las dos se señalan aunque el renglón que las describe sea el
mismo.

La cola nunca acaba encima del bloque de la contratrabe: si cae dentro de su ancho se corre al paño
más cercano. Fuera de la zapata sí puede acabar, porque ahí solo hay tierra, y eso es lo que le da
largo al quiebre cuando el rótulo es casi tan ancho como el volado. Y los leaders se suben **al
frente** —el *bring to front* de AutoCAD— porque la diagonal cruza por detrás del propio bloque de
texto y la máscara de fondo del MText la borraba a la mitad.

Las dos flechas se quedan dentro de una **franja**: el volado de ese lado, y siempre entre las caras
del acero. Fuera de ella la punta acabaría debajo de la contratrabe y la línea cruzaría el bloque
para llegar. Y como el rótulo es casi tan ancho como el volado, el rótulo se sube **al frente** al
final: su máscara de fondo corta la línea donde la cruza, en lugar de que la línea se dibuje encima
de las letras.

**Dos renglones, no uno.** El rótulo de parrilla es un **MText de 22 cm de ancho**: la varilla en el
primer renglón y su palabra en el segundo. En una sola línea medía 30 cm, y en una zapata de 80 con
una contratrabe de 30 no cabe en el volado: el renglón de la parrilla de abajo acababa **dentro** del
bloque de la contratrabe. El corte va escrito con un salto de línea y no se deja al reparto
automático, para que la palabra caiga siempre abajo aunque el número de varilla cambie de largo.

**La «C» del armado y el lecho de cada varilla.** El número de varilla lleva una **«C»** detrás
—`VAR #4C @ 20 cm`—, y en el segundo renglón va la palabra del **lecho**. En la parrilla de abajo la
de flexión se apoya en el recubrimiento y la de temperatura descansa encima, así que dicen
`INFERIOR` y `SUPERIOR`; en la parrilla de arriba es **al revés**, porque ahí la de flexión se amarra
por el lomo. Cuando los dos sentidos llevan **la misma varilla y la misma separación** sobra
rotularlos dos veces: sale un solo renglón con `AMBOS SENTIDOS` en la segunda línea, en el lado
izquierdo y con la flecha en la varilla de flexión.

**«Cada lado» es el volado libre.** El renglón se centra en la mitad del tramo que va del paño de la
zapata al paño de lo que hay en medio —la contratrabe si sobresale, y si no el muro—, no en la cuarta
parte del ancho. Y si el volado es más estrecho que el propio renglón, el rótulo se corre hacia
fuera hasta que quepa: antes se sale de la zapata que taparse con la contratrabe.

**El renglón se mide desde el lomo del concreto, no desde la varilla.** Sube **10 cm** sobre el
paño de arriba de la zapata, y eso arregla dos cosas de golpe: el texto **nunca cae dentro de la
sección** —con la medida vieja, una zapata de 50 cm de espesor dejaba el rótulo de la parrilla
inferior enterrado en el rayado del concreto— y con **doble parrilla** se sube solo, porque el acero
de arriba también está por debajo de ese paño.

**Con doble parrilla, un rótulo por parrilla y uno en cada lado.** En la **central**, la parrilla de
abajo se rotula a la **izquierda** y la de arriba a la **derecha**, las dos a la misma altura y con
sus **dos varillas en el mismo MText**. De ese rótulo salen dos flechas, una del cuarto izquierdo de
su borde inferior y otra del cuarto derecho: la primera a la varilla de flexión —una línea continua,
sirve cualquier punto— y la segunda a la varilla de punta más cercana. Así cada lado lleva **un solo
renglón** y ningún leader tiene que cruzar por encima de otro.

Antes se repartían por tipo de varilla —flexión a la izquierda y temperatura a la derecha, los dos
lechos apilados en cada lado—, y con la contratrabe de 30 en una zapata de 80 no queda hueco para dos
carriles: el leader del renglón de arriba acababa atravesando el de abajo. El **lindero** conserva ese
reparto, porque su muro está pegado al paño derecho y el lado derecho no existe; y con **una sola
parrilla** también, que es como se aprobó.

**El codo del acero del muro se rellena.** Con sección rellena, la varilla del muro de concreto
va maciza de punta a punta: círculos, tramo recto, pata **y codo**. El codo se rellena siguiendo
sus **dos arcos**, que no son concéntricos porque cada macro le da su radio a la cara de dentro y
a la de fuera —la central `Ø/4` y `Ø/2`, el lindero `Ø` y `2Ø`—; un sector anular concéntrico no
casaría con el contorno.

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
2. **El hatch de terreno, ceñido a lo que sobresale.** La altura entre el lomo de
   la zapata y el nivel de terreno se parte en **bandas** por cada arranque y
   cada remate de pieza —contratrabe, muro de concreto, hilada de enrase y
   cadena—, y en cada banda la tierra se detiene en el paño de la pieza **más
   ancha a esa altura**. Así el borde sale en **escalera** y se ajusta solo: si la
   contratrabe sobresale del muro, la tierra se retira en su banda y vuelve a
   cerrarse encima. Las bandas se cosen en **un solo contorno por lado** y no en
   un hatch por banda, porque dos hatches apilados cortan el rayado en la junta.
3. **Los leaders y los rótulos de parrilla**, con su cola dibujada al final para
   que queden al frente del hatch y del bloque.
4. **El orden de dibujo** de los contornos del enrase, con `ACAD_SORTENTS`,
   igual que ya se hace con los estribos de las aisladas.
5. **El corte de las varillas del muro** en cada cruce con el acero de la
   zapata, que en la central se hace con oclusores y en el lindero no.


## 10. Los colores de capa, que son de la macro

La lista de `CrearCapa` de la macro de sección estructural vive ahora en un solo sitio,
`CapasCad.cs`, y la usan los tres dibujantes —secciones, alzados y zapatas—:

| Capa | ACI | | Capa | ACI |
|---|---|---|---|---|
| `VAR_#2` | 150 | | `VAR_#8` | 1 |
| `VAR_#2.5` | 6 | | `VAR_#10` | 6 |
| `VAR_#3` | 132 | | `VAR_#12` | 15 |
| `VAR_#4` | 142 | | `TEXTOS` | 3 |
| `VAR_#5` | 160 | | `CONCRETO` | 8 |
| `VAR_#6` | 4 | | `ESTRIBOS` | 150 |

Estaba escrita **solo** en el dibujante de secciones, así que el de zapatas creaba `VAR_#5`
**sin color** y AutoCAD la dejaba en blanco: se capturaba una varilla del #5 y salía blanca en
lugar del 160. Para estas doce capas el color se **fuerza** aunque la capa ya exista, que es lo que
hace `CrearCapa` en el módulo de la macro y lo que mantiene el juego de planos de una pieza. Las
capas que no están en la tabla —`COTAS`, `ROTULOS`, `TERRENO`, `PLANTILLA`, las de bloque— solo se
pintan al crearlas, y si ya están se dejan como las tenga el usuario.


## 11. Las ocho columnas de parrilla de la hoja

Las mismas en la hoja de **corridas** y en la de **aisladas**, y con la banda de arriba diciendo de
qué parrilla es cada grupo:

| PARRILLA INFERIOR | | | | PARRILLA SUPERIOR | | | |
|---|---|---|---|---|---|---|---|
| `Var Inf. Flexión` | `@ cm` | `Var. Sup. Temp.` | `@ cm` | `Var Sup. Flexión` | `@ cm` | `Var. Inf. Temp.` | `@ cm` |

El nombre dice el **lecho** y el **trabajo** de cada varilla, que es como sale rotulada en el plano:
en la parrilla de abajo la de flexión va en el lecho inferior y la de temperatura se apoya encima; en
la de arriba es al revés, porque la de flexión se amarra por el lomo.

La cuadrícula de WPF no sabe juntar columnas bajo un título, así que la **banda** va en la cabecera de
la primera columna de cada grupo y las otras tres llevan el renglón de arriba en blanco: con eso los
nombres de columna quedan todos a la misma altura y se lee de un golpe dónde empieza cada parrilla. La
cabecera pasó de 32 a 40 px para que quepan los dos renglones.

Y las **cuatro casillas de la parrilla superior se apagan** cuando la fila no lleva doble parrilla,
con el mismo criterio que las del armado del muro: una celda que el dibujo no va a leer no se deja
escribir, porque capturar ahí un dato y no verlo en el plano es media hora buscando el error.
