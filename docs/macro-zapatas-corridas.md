# Inventario de `ZAPATA CORRIDA CENTRAL V2` y `ZAPATA CORRIDA LINDERO V2`

Las dos macros, lado a lado, con su estado en el port. Se escribe **antes** de
dibujar nada en AutoCAD: es lo que ya evitó, en las aisladas, portar a ciegas y
descubrir después que faltaba media rutina.

Estados: **Sí** portado · **Falta** pendiente · **Confirmar** portado con el
valor de la macro pero con el uso exacto por verificar contra el fuente

---

## 1. Lo que comparten las dos macros

Son la misma macro con dos decisiones cambiadas. Todo esto es idéntico:

| Qué | Valor | Estado |
|---|---|---|
| Nivel de terreno natural | `yNivTerr = −3.5` | Sí |
| Desplante | `yZapBot = yNivTerr − profundidad` | Sí |
| Plantilla de concreto simple | 5 cm bajo el desplante | Sí |
| Recubrimiento de parrillas | 5 cm | Sí |
| Paso entre secciones | 2 m fijos, no «ancho + holgura» | Sí |
| Parrilla inferior y superior | la misma rutina que las aisladas | Sí |
| Muro de enrase | piezas de ≈8 cm, junta 1 cm, desfase 1 cm por lado | Sí |
| Búsqueda del reparto del enrase | de 1 a 50 piezas, la más cerca de 8 cm | Sí |
| Doblez del acero del muro | 15 diámetros | Sí |
| Contratrabe y cadena de desplante | por **bloque**, no se redibujan | Falta (COM) |
| Cotas | `0.13`, `0.075`, `0.1445`, `0.0585` | Confirmar |
| Rótulo | tres renglones a `0.25`, `0.34`, `0.42` | Confirmar |
| Relleno del concreto | SOLID 9 + `AR-CONC` 251 a escala `0.0003` | Sí |
| Relleno del enrase | pieza SOLID 253, junta SOLID 252 | Sí |

## 2. Lo que las separa

Solo dos cosas. Todo lo demás que parece distinto es la misma cuenta escrita en
otras celdas.

| Decisión | Central | Lindero |
|---|---|---|
| Acomodo de la fila | `offsetX = i · 2`, hacia la **derecha** desde 0 | `offsetX = −2 − i · 2`, hacia la **izquierda** |
| Posición del muro | **centrado**: `xCentro − espesor / 2` | pegado al **paño derecho**: `xMuroDer = xBase + ancho` |
| Nombre del bloque | el ID tal cual | `ZAPATA_LINDERO_` + ID |
| Título del rótulo | `ZAPATA CORRIDA CENTRAL "Z-1"` | `ZAPATA DE LINDERO "Z-1"` — sin la palabra «corrida», así está en su macro |

Que las filas crezcan en sentidos contrarios no es un detalle estético: es lo que
permite que las dos familias convivan en el mismo dibujo sin encimarse.

## 3. Celdas que lee cada una

Mismo dato, distinta columna. Están anotadas una por una en
`ZapataCorridaCad.cs`, primero la central y después la de lindero.

| Dato | Central | Lindero | Estado |
|---|---|---|---|
| Ancho, profundidad, espesor | `E4`, `E5`, `E6` | `O4`, `O5`, `O6` | Sí |
| Parrilla inferior: varilla y separación | `C8`, `E8` | `C8`, `O8` | Sí |
| Parrilla inferior transversal | `C10`, `E10` | `C10`, `O10` | Sí |
| Doble parrilla | `H8` | `R8` | Sí |
| Parrilla superior y su transversal | `C12`, `E12`, `C14`, `E14` | `O12`, `O14` | Sí |
| Tipo de muro | `H4` | `R4` | Sí |
| Espesor del muro, en **cm** | `H9` concreto, `G7` mampostería | igual | Sí |
| Muro: doble parrilla, varilla, separaciones | `H10`, `H11`, `H12`, `H13` | `R10`–`R13` | Sí |
| Bloque de contratrabe | `H6` | `R6` | Sí (dato) |
| Bloque de cadena de desplante | `H5` | `R5` | Sí (dato) |
| f'c | `J8` | `T8` | Sí |

Las medidas de la zapata están en **metros** y los espesores de muro en
**centímetros** —la macro los divide entre 100—, y esa distinción se conserva a
propósito: es la que evita que un muro de 15 cm salga de 15 m.

## 4. El muro de enrase, que es la única cuenta con truco

No dibuja piezas de alto fijo. Busca en cuántas piezas iguales cabe el hueco
para que cada una salga lo más cerca posible de los 8 cm de una pieza real:

```
para n = 1 … 50:
    alto = (hueco − (n − 1) · junta) / n      junta = 1 cm
    gana el n con |alto − 0.08| más chico y alto > 0
```

Así el enrase remata justo contra la cadena de desplante, sin media pieza al
final. Si el hueco no da ni para una pieza —la contratrabe llega hasta la
cadena—, no hay enrase y no se dibuja: cero piezas, no una pieza aplastada.

## 5. Estado del port, archivo por archivo

| Archivo | Qué es | Estado |
|---|---|---|
| `CadLink.Cad/ZapataCorridaCad.cs` | los datos de la hoja, con las celdas de las dos macros anotadas | Sí |
| `CadLink.Cad/TrazoZapataCorrida.cs` | la geometría pura: acomodo, alturas, muro, enrase, acero del muro, cotas, rótulo | Sí |
| `tools/verificar_zapatas_corridas.py` | los números del port contra los de las macros | Sí |
| `CadLink.Cad/ZapataCorridaDrawer.cs` | el dibujante COM | Falta |
| `CadLink.App` hoja «Zapatas Corridas» | la hoja de captura y la vista previa | Falta |

## 6. Lo que falta confirmar contra el fuente

Se anota aquí y no se esconde en un comentario: portar con un número correcto en
el sitio equivocado es peor que dejarlo pendiente.

1. **El uso exacto de los cuatro offsets de cota.** Los valores son los de las
   macros; qué cota cuelga de cada uno se verifica al escribir el dibujante,
   comparando contra un plano ya dibujado con la macro.
2. **Qué separación del muro es la horizontal y qué la vertical.** `H12` y `H13`
   se llaman «separación horizontal» y «separación vertical», y en el corte
   transversal solo se ve una de las dos: la de las varillas que salen de punta.
   El port las recibe por parámetro y con nombre explícito, así que corregir la
   asignación es cambiar una línea en el dibujante, no la geometría.
3. **Hacia dónde dobla la barra vertical del muro.** El port la dobla hacia el
   eje de la zapata —el único lado donde hay concreto para anclar— y en el
   lindero eso deja las dos patas hacia la izquierda, lejos del lindero.
