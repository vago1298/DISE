# Inventario de `ALZADOS V2`

Cada rutina y cada constante de la macro, con su estado en el port. Este
documento se escribe **antes** de programar, para que se vea qué queda fuera
antes de que salga un dibujo mal, y no después.

Estados: **Sí** portado · **No** pendiente · **N/A** no aplica

---

## 1. Datos que lee de la hoja

Mismas columnas que la macro de secciones, **más la W**.

| Col | Dato | Estado |
|---|---|---|
| A | Elemento. Decide alzado horizontal o vertical | Sí |
| B | ID. Nombre del bloque | Sí |
| C | Base | Sí |
| D | Altura / peralte | Sí |
| E–N | Varillas de los cuatro lechos y laterales | Sí |
| O | Recubrimiento (por omisión 2.5) | Sí |
| P | Estribo | Sí |
| Q | Separaciones `s1-s2-s3` para las zonas L/4-L/2-L/4 | Sí |
| R, S | Estribo diamante: cambia el diámetro del estribo del alzado y añade un renglón al rótulo | Sí |
| T | Gancho, **en cm y se divide entre 100** | Sí |
| U, V | f'c y escala | Sí |
| **W** | **Longitud del elemento. Columna NUEVA** | Sí |
| AC1 | Modo de relleno, igual que en secciones | Sí |

## 2. Clasificación del elemento

| Rutina | Qué hace | Estado |
|---|---|---|
| Prueba `TRABE`/`CONTRATRABE` o ID `T-`/`CT-` | Alzado **horizontal** | Sí |
| Prueba `COLUMNA`/`DADO` o ID `C-`/`D-` | Alzado **vertical**, y si la sección es rectangular dibuja **dos caras** | Sí |
| `TipoElementoTexto` | Texto del título | Sí |

**Solo esas cuatro familias llevan alzado.** Confirmado con el autor: **castillos y
cadenas no lo llevan**, ni la de cerramiento ni la de desplante. Lo que no encaja se
omite y se informa, en lugar de caer por omisión en «trabe», que era el
comportamiento equivocado de la primera versión del port.

El orden de las pruebas importa: `CT-` va **antes** que `C-`, porque una
contratrabe también empieza con C y si no quedaría clasificada como columna.

## 3. Geometría del alzado

| Rutina | Qué hace | Estado |
|---|---|---|
| `AddClosedRect` | Contorno del concreto | Sí |
| `AddHatchConcreto` | Fondo sólido 9 + AR-CONC 251 | Sí |
| `BuildStirrupCenters` | Posición de cada estribo por zonas | Sí |
| `AddCentersBySpacing` | Estribos dentro de una zona | Sí |
| `AddCentroTransicion` | Estribo de frontera, con tolerancia de 6 cm; se omite si no cabe | Sí |
| `AddCentroConSeparacion`, `AddUniqueCenter` | Separación mínima de 5 cm | Sí |
| `RemoveLastCenter` | En COLUMNA se quita el último estribo | Sí |
| `DrawStirrupsCapsulesFront` | Estribo como **cápsula**: rectángulo con `bulge -1` arriba y abajo | Sí |
| `DrawBarWithHooks` | Varilla como banda de un diámetro, con dobleces de 90° | Sí |
| `DrawFaceSegmented` | **La cara de la varilla se corta donde cruza un estribo** | Sí |
| `ShiftClearRight/Left` | Corre el gancho para que no caiga sobre un estribo | Sí |
| Varillas intermedias | Terminan antes de los ganchos | Sí |
| `CrearBordeVarilla` | Contorno cerrado de la varilla, para su relleno | Sí |
| `RellenarSolido` | Relleno del acero | Sí |
| `ForzarContornosNegros` | Contornos en negro, color verdadero | Sí |
| `OrdenarRellenosAlFondo` | Orden: contornos > estribos > varillas > concreto | Sí |
| `RotateEntitiesRange90KeepBase` | El alzado vertical se dibuja horizontal y se **gira 90°** | Sí |

## 4. Bloques

| Rutina | Qué hace | Estado |
|---|---|---|
| `EnsureAlzadoBlockDef` | Bloque `ALZ-<id>`, o `ALZX-`/`ALZY-` en columna rectangular | Sí |
| `PurgeBlockContents` | Limpia el bloque si ya existía | Sí |
| `InsertAlzadoRef` | Inserta en la capa ALZADOS | Sí |
| `SanitizeBlockName`, `UniqueAlzName` | Nombre válido y sin colisiones | Sí |
| `InsertBlockByLeftEdgeGap` | Inserta el bloque de la **sección** y lo alinea | Sí |
| `ForzarYBloque`, `YBaseEfectiva` | Todos los bloques a la misma Y | Sí |
| `AlinearSeccionConAlzado` | Alineación alternativa | N/A (solo si `FORZAR_Y_BLOQUES = False`) |

## 5. Cotas, textos y rótulos

| Rutina | Qué hace | Estado |
|---|---|---|
| `AddDimHorizontal`, `AddDimVertical` | Cotas con `TextOverride` | Sí |
| `AnotarAlzadoHorizontal` | Cotas de armado arriba, estribos y zonas abajo | Sí |
| `AnotarAlzadoVertical` | Armado a la derecha, estribos y zonas a la izquierda | Sí |
| Cotas de gancho | Al lado derecho, dos si las esquinas difieren | Sí |
| `BuildLechoText2`, `BuildSimpleText`, `NormalizeDiaLabel` | «3 Varillas Superiores #8C» | Sí |
| `AddBeamTitle` | «DETALLE DE ALZADO DE TRABE "T-1"» + escala | Sí |
| `AddBeamTitleVertical`, `AddRotatedTextLeftTop` | Título girado 90° | Sí |
| `AddCuteLabelAboveBlock` | «CORTE A-A'» sobre la sección | Sí |
| `AddBlockLabelBelowBlock` | Rótulo de la sección | Sí (ya existía) |
| `AddBlockDimsRightTop` | Cotas de la sección | Sí (ya existía) |
| `AgregarLeadersSeccion` y sus auxiliares | Llamadas de lechos sobre la sección | Sí (ya existía) |

## 6. Longitudes

| Rutina | Estado |
|---|---|
| `ParseLongitudM` (si ≥ 20 se interpreta cm) | Sí |
| `CalculateFlexibleLength` (longitud si la W está vacía) | Sí |

## 7. Lo que queda FUERA en esta entrega

| Rutina | Por qué |
|---|---|
| `EnsureTextStyleIfMissing` con `arial.ttf` | El port ya crea `SECCIONES` con Bahnschrift. **Diferencia deliberada**: la macro de alzados usa Arial y la de secciones Bahnschrift; se unifica en Bahnschrift para que los dos dibujos combinen. Dime si prefieres Arial. |
| `AplicarFondoHatch` (`BackgroundColor`) | Se usa la vía de respaldo de la propia macro: un hatch SOLID aparte |
| Atributos del bloque de sección (`GetAttributes`) | El port no usa bloques con atributos |
| `CMDECHO` | No aplica sin línea de comandos |

## 8. Diferencias de escala que hay que tener presentes

- La macro usa `SCALE_ELEVATION = 0.01` fijo. El port usa la escala de la
  casilla, para que sección y alzado coincidan.
- `HATCH_PATTERN_SCALE = 0.0003` en la macro. En AutoCAD 2026 el valor bueno,
  medido, es **0.01**; se usa el mismo de la casilla que el resto del dibujo.
