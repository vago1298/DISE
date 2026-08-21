# Inventario de `ZAPATA AISLADA CENTRAL V2` y `ZAPATA AISLADA LINDERO V1`

Las dos macros son **el mismo dibujo** con el dado corrido. Este documento dice qué
comparten, en qué se separan y qué está portado, para que se vea lo que falta antes
de que salga un dibujo mal.

Estados: **Sí** portado · **No** pendiente

---

## 1. Lo que cambia entre las dos

Solo tres cosas. Todo lo demás es común.

| | Central | Lindero |
|---|---|---|
| Dado y columna | centrados en la zapata | pegados al **paño derecho** (el lindero) |
| Rótulo del dado y de la columna | a la **derecha**, en `(xDadoDer + xExtremoDer)/2` | a la **izquierda**, a `0.30` del paño izquierdo |
| Ganchos de arranque del dado | uno a cada lado | los **dos** hacia la izquierda: nada puede salir del lindero |
| Rótulo de la parrilla superior | por la derecha del lomo | **centrado** sobre la zapata, a `0.23` del lomo |
| Vista en planta | cuelga a `-3` del fondo del alzado | lo que haga falta, con tope en `-15` |
| Origen y separación | `x = 0`, avanza `+1.0` a la derecha | `x = -3`, avanza `-0.8` a la **izquierda** |

**Se unificó la colocación en la del lindero**, a pedido del usuario: `-0.8` de
separación, hacia la izquierda, y la zapata anclada por su **paño izquierdo**. Antes
se colocaba respecto del centro y dos zapatas seguidas se encimaban: los dos títulos
quedaban uno sobre otro. Está en `ZapataAisladaLayout.XSiguiente`.

## 2. Rótulos

Los textos son idénticos en las dos macros salvo el nombre del tipo.

| Renglón | Texto | Alto | Y | Estado |
|---|---|---|---|---|
| Título | `ZAPATA AISLADA CENTRAL "ZE-1"` · `ZAPATA AISLADA DE LINDERO "ZL-1"` | 0.07 | desplante − 0.32 | Sí |
| Subtítulo | `ELEVACION`, sin acento | 0.05 | − 0.41 | Sí |
| Escala | `Rec. 5 cm    f'c = 250 kg/cm²    Escala 1:10` | 0.04 | − 0.49 | Sí |
| Planta | `VISTA EN PLANTA "ZL-1"` | 0.07 | planta − 0.24 | Sí |
| Escala de planta | `Rec. 5 cm    Escala 1:10`, **sin f'c** | 0.04 | planta − 0.33 | Sí |
| Terreno | `Nivel del terreno` | 0.025 | terreno + 0.03 | Sí |
| Plantilla | `Plantilla de concreto simple f'c: 100 kg/cm²` | 0.02 | en medio de la plantilla | Sí |
| Dado / columna | `DADO "D-1"` + `16 VAR #4` + `EST #3 @ 8 cm` | 0.015 | a media altura del elemento | Sí |
| Parrillas | `VAR #4 @ 15 cm` + `AMBOS SENTIDOS`, o los dos armados por separado | 0.015 | ver constantes | Sí |
| Parrillas en planta | `PARRILLA INFERIOR` + los dos armados | 0.03 | fracciones del ancho | Sí |

Tres detalles que se comprueban en `tools/verificar_rotulos_zapatas.py` porque son
fáciles de perder al portar:

1. Los separadores de la línea de escala son **cuatro espacios** literales.
2. Si la celda del f'c viene vacía, el `f'c =` **desaparece**; no se escribe a secas.
3. En `VAR #4 SUPERIOR @ 18 cm` el sufijo va **antes** de la separación.
4. El rótulo del dado **suma las cantidades que comparten diámetro**: 8 + 8 del mismo
   `#4` se escriben `16 VAR #4`, no dos renglones.

## 3. Geometría

| Rutina de la macro | Qué hace | Estado |
|---|---|---|
| Colocación de la fila, `xBase` | Paño izquierdo de cada zapata | Sí |
| `DibujarContornoZapataConDado` | Contorno con el hueco del dado | No |
| `DibujarHatchConcretoRect` | Sólido 9 + AR-CONC 251, según el modo de relleno | No |
| `DibujarPlantillaConcretoSimple` | Plantilla de 5 cm y su texto | No |
| `DibujarHatchTerreno` | EARTH a los dos lados del dado, transparencia 45 | No |
| `DibujarParrillaZapata` | Barra de canto con ganchos + círculos de la transversal | No |
| `DrawVerticalElementFromAlzados` | Dado y columna: se dibujan tendidos y se giran 90° | No |
| `DrawStirrupsCapsulesFront` | Estribo como cápsula, con relleno 152 | No |
| `PrepararUnionDadoColumna` | Emparejado de barras dado–columna y su desplazamiento | No |
| `RecortarVerticalesZonaDobleces` | Recorte de las verticales en la zona de dobleces | No |
| `DibujarMallaPlanta` | Malla en planta, con recortes en los cruces y en el hueco | No |
| `DibujarBreakLineEntre` | Línea de rotura de la diagonal de la planta | No |
| Cotas verticales y de cadena | Plantilla, espesor, tierra y total | No |
| `CotasDoblezGanchosDado` | Cotas de las patas de los ganchos | No |

## 4. Dónde está cada cosa en el port

| Archivo | Qué resuelve |
|---|---|
| `client/src/CadLink.Cad/ZapataAisladaCad.cs` | Datos de una zapata, con las celdas de origen anotadas |
| `client/src/CadLink.Cad/ZapataAisladaRotulos.cs` | Los textos, palabra por palabra |
| `client/src/CadLink.Cad/ZapataAisladaLayout.cs` | Colocación de la fila y punto de cada rótulo y cota |
| `tools/verificar_rotulos_zapatas.py` | Compara VBA contra port, texto a texto y número a número |

El motor de dibujo (`ZapataAisladaDrawer`) es el siguiente paso: la geometría de la
tabla anterior y el enganche con la pestaña **Zapatas Aisladas**, que hoy es un
marcador de posición.
