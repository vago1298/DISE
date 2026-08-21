# La macro de alzados contra el port en C#

Cotejo **rutina por rutina** de `ALZADOS V2` —la macro real, la que mandaste—
contra `AlzadoDrawer`, `AlzadoLayout` y `Estribos`.

`docs/inventario-macro-alzados.md` se escribió **antes** de tener la macro
delante, a partir de la descripción. Este documento la contrasta con el código
fuente de verdad. Lo que cambia respecto al inventario va marcado con ⚠️.

---

## 1. Lo que el cotejo encontró mal en el port

### ⚠️ La tabla de diámetros de varilla estaba redondeada, y un valor estaba mal

Es el hallazgo importante del cotejo, y no es de dibujo: es numérico.

| Clave | Port (antes) | Macro `RebarDiaM` | Nominal exacto | Error de área del port |
|---|---|---|---|---|
| `#2` | 0.60 | 0.64 | **0.635** | **−12.1 %** |
| `#6` | 1.90 | 1.91 | **1.905** | −1.0 % |
| `#10` | 3.20 | 3.18 | **3.175** | +1.3 % |
| `#12` | 3.80 | 3.81 | **3.81** | −0.5 % |

Un 12 % de menos en el área de un `#2` se propaga a `AreaAceroCm2` y de ahí a la
cuantía. **Una cuantía baja es del lado inseguro**: hace pasar por bueno un
armado que no llega al mínimo.

**Corregido.** La tabla ya no es una lista de valores redondeados sino la fórmula
exacta: la varilla del número `n` mide `n/8` de pulgada y una pulgada son 25.4 mm
exactos. Comprobado en `tools/verificar_diametros_varilla.py`, que **lee la tabla
del propio C#** en lugar de copiarla, para que no puedan volver a divergir.

La macro estaba más cerca del nominal que el port: error acumulado de 0.026 cm
contra 0.086 cm.

### ⚠️ `Y_BLOQUES` era una cota absoluta y ahora es relativa

La macro pone **todo** en `Y = 2` (`FORZAR_Y_BLOQUES = True`,
`ALINEA_BLOQUE_MODO = 0`, paño inferior). Funciona mientras ninguna sección pase
de 2 m de alto en el papel; en cuanto entra una contratrabe alta, **la sección
invade la fila de alzados**.

Cambiado a lo que pediste: `AlzadoLayout.YArranque(altoMáximo)` devuelve
`altoMáximo + 2`, siempre. Comprobado en `tools/verificar_y_alzados.py`.

> **Consecuencia que conviene tener presente:** un plano acomodado con la versión
> anterior verá la fila de alzados desplazada hacia arriba la próxima vez que se
> generen. Con una trabe de 60 cm a escala 1:100 la fila pasa de `Y=2` a `Y=2.6`.

---

## 2. Rutinas de la macro, una por una

Estados: **Sí** portado · **Dif** portado con diferencia deliberada · **No**
pendiente · **N/A** no aplica

### 2.1 Entrada y lectura de la hoja

| Rutina de la macro | Estado | Dónde / nota |
|---|---|---|
| `Alzados_Trabes_Desde_Excel` | Sí | `OnExportAlzados` + `AlzadoDrawer.DibujarElemento` |
| `LeeNumero` | Sí | `Varilla.TryDiametroCm` y los parseos de la fila |
| `CLngSafe`, `ToDoubleSafe` | Sí | Tipado de la cuadrícula; no hacen falta |
| `ParseSpacings` | Sí | `Separaciones(...)`, con el mismo respaldo de 15 cm |
| `ParseLongitudM` (≥ 20 ⇒ cm) | Sí | `Estribos.LongitudDeColumnaW` |
| `CalculateFlexibleLength` | **Dif** | `Estribos.LongitudFlexible`. El port añade un tope: la macro fuerza 3 estribos mínimo **sin volver a mirar la separación**, y en un elemento muy corto eso da estribos a 1.67 cm, que no existe en obra |
| `LeerModoGlobal` / `AplicarModo` (celda AC1) | Sí | Los dos radios de «Estilo de dibujo» |
| `RebarDiaM` | **Dif** | `Varilla.DiametrosCm`, ahora con el nominal exacto. Ver §1 |
| `VarLayerName` | Sí | `CapaVar` |
| `SoloDiametroHash`, `DiamNumero`, `NormalizeDiaLabel` | Sí | `Varilla.Normalizar` |

### 2.2 Clasificación del elemento

| Rutina | Estado | Nota |
|---|---|---|
| Pruebas `TRABE`/`CONTRATRABE`/`T-`/`CT-` | Sí | Alzado horizontal |
| Pruebas `COLUMNA`/`DADO`/`C-`/`D-` | Sí | Alzado vertical |
| `TipoElementoTexto` | Sí | `AlzadoCad.TipoTexto` |

`CT-` se prueba **antes** que `C-`: una contratrabe también empieza con C.

### 2.3 Reparto de estribos

| Rutina | Estado | Dónde |
|---|---|---|
| `BuildStirrupCenters` | Sí | `Estribos.Centros` |
| `AddCentersBySpacing` | Sí | `Estribos.PorSeparacion` |
| `AddCentroTransicion` (tolerancia 6 cm, se omite si no cabe) | Sí | `Estribos.Transicion` |
| `AddCentroConSeparacion`, `AddUniqueCenter` | Sí | `Estribos.ConSeparacion`, `Estribos.Unico` |
| `RemoveLastCenter` (solo en COLUMNA) | Sí | `Estribos.CentrosDeAlzado`, incluido el caso de un solo estribo, en que la macro devuelve un arreglo **vacío** |
| `CollectionToArray` | N/A | `List<double>` |

Las banderas coinciden: horizontal `(False, True)`, vertical `(False, False)`.

### 2.4 Geometría

| Rutina | Estado | Dónde |
|---|---|---|
| `AddClosedRect` | Sí | `RectCerrado` |
| `AddHatchConcreto`, `CrearHatchAlzado` | Sí | `HatchDeConcreto`, `Hatch` |
| `AplicarFondoHatch` (`BackgroundColor`) | **Dif** | Se usa la vía de respaldo de la propia macro: un hatch SOLID aparte. Menos frágil entre versiones |
| `NuevoAcCmColor` | Sí | `ColorNegro` |
| `DrawStirrupsCapsulesFront` | Sí | `CapsulasDeEstribo`, con `bulge = -1` |
| `DrawBarWithHooks` | Sí | `VarillaConGanchos` |
| `DrawFaceSegmented` | Sí | `CaraSegmentada` |
| `ShiftClearRight` / `ShiftClearLeft` | Sí | `CorrerADerecha` / `CorrerAIzquierda` |
| `CrearBordeVarilla` (con los 4 bulges) | Sí | `BordeDeVarilla` |
| `RellenarSolido` | Sí | `Hatch(..., "SOLID", ...)` |
| `ForzarContornosNegros`, `ColorNegroContorno` | Sí | `ContornosNegros`, `ColorNegro` |
| `OrdenarRellenosAlFondo`, `MoverColeccionAlFondo`, `SortEntsDe` | Sí | `OrdenarRellenos`, `AlFondo` |
| `EnviarAlFondoVarios` | Sí | Igual |
| `RotateEntitiesRange90KeepBase` | Sí | `Girar90` |
| `AddSimpleLine`, `AddArcSafe` | Sí | `Linea`, `Arco` |
| `MinD`, `MaxD`, `APoint` | N/A | `Math.Min/Max`, arreglos |
| `UnionEntityExtents` | Sí | Dentro de `InsertarSeccion` |

### 2.5 Bloques

| Rutina | Estado | Dónde |
|---|---|---|
| `EnsureAlzadoBlockDef` | Sí | `DefinicionDeBloque` |
| `PurgeBlockContents` | Sí | Igual |
| `InsertAlzadoRef` | Sí | `InsertarBloque`, capa ALZADOS |
| `SanitizeBlockName`, `UniqueAlzName`, `ExistsInCol` | Sí | `NombreUnico` |
| Prefijos `ALZ-`, `ALZX-`, `ALZY-` | Sí | Igual |
| `InsertBlockByLeftEdgeGap` | Sí | `InsertarSeccion`, en dos pasos igual que la macro |
| `ForzarYBloque`, `YBaseEfectiva` | **Dif** | Ver §1: la Y ya no es absoluta |
| `AlinearSeccionConAlzado` | N/A | Solo si `FORZAR_Y_BLOQUES = False`, que la macro no usa |

### 2.6 Cotas, textos y rótulos

| Rutina | Estado | Dónde |
|---|---|---|
| `AddDimHorizontal`, `AddDimVertical` | Sí | `Cota` |
| `AnotarAlzadoHorizontal` | Sí | `AnotarHorizontal` |
| `AnotarAlzadoVertical` | Sí | `AnotarVertical` |
| Cotas de gancho (2 si las esquinas difieren) | Sí | `CotasDeGancho` |
| `BuildLechoText2`, `BuildSimpleText` | Sí | `TextoLecho`, `TextoSimple` |
| `AddBeamTitle` | Sí | `Titulo` |
| `AddBeamTitleVertical`, `AddRotatedTextLeftTop` | Sí | `TituloVertical`, `TextoGirado` |
| `AddCuteLabelAboveBlock` | Sí | `RotuloCorte` |
| `AddBlockLabelBelowBlock` | Sí | `SeccionDrawer.Rotulo` |
| `AddBlockDimsRightTop` | Sí | `SeccionDrawer.Cotas` |
| `AgregarLeadersSeccion` | Sí | `SeccionDrawer.LeadersDeLecho` |
| `PosFilaEsquinas`, `PosFilaIntermedias` | Sí | `SeccionDrawer.Lecho` |
| `CrearLeaderLechoMultiple` | Sí | `LeaderLecho` |
| `CrearLeaderParaCadaVarilla` | Sí | `LeaderVarilla` |
| `EnsureTextStyleIfMissing` con `arial.ttf` | **Dif** | El port crea `SECCIONES` con **Bahnschrift**. La macro de alzados usa Arial y la de secciones no; se unificó para que los dos dibujos combinen. **Dime si prefieres Arial** |
| `EnsureLayer` | Sí | `AsegurarCapas` |
| `GetAcadApp` | Sí | `AcadConnection.Connect` |
| `CMDECHO` | N/A | No hay línea de comandos |

### 2.7 Constantes

Todas las de colocación están en `AlzadoLayout` y comprobadas contra el VBA:
`SEP_SECCIONES 0.6`, `MARGEN_COL 0.4`, `SEP_CARAS 0.3`, `SEP_SEC_ALZ 0.2`,
`HOOK_DIM_OFF_2 0.14`, `DIM_OFF_1..4`, `ROTULO_OFF_COL 0.09`.

En `Estribos`: `STIRRUP_EDGE_OFFSET 0.05`, `SEP_MIN_ESTRIBOS 0.05`,
`TOL_TRANSICION 0.06`, `HOOK_DIAM_FACTOR 12`.

En `AlzadoDrawer`: `ARC_OFFSET 0.0039`, `HOOK_CLEAR_H 0.015`,
`HOOK_CLEAR_V 0.01`, `HOOK_CLEAR_EST 0.005`, `BULGE_90`.

⚠️ `SCALE_ELEVATION = 0.01` y `HATCH_PATTERN_SCALE = 0.0003` son fijas en la
macro; el port usa la escala de la casilla para que sección y alzado coincidan.

---

## 3. Lo que **no** está en la macro y ahora sí está

| Añadido | Por qué |
|---|---|
| **Sección circular** por fila, con varillas totales | Lo pediste. La macro solo sabe de rectángulos |
| **Zuncho helicoidal o en anillos**, a tu elección | Lo pediste. La hélice se dibuja como la proyección exacta: un seno de amplitud igual al radio y periodo igual al paso, con las dos caras de la barra en fase |
| Varillas del círculo **proyectadas** al alzado | De 8 varillas se ven 5 posiciones distintas; las parejas simétricas se proyectan una sobre otra y se quitan las repetidas |
| Alzados a **2 m sobre la sección más alta** | Lo pediste. Ver §1 |
| La grapa del diamante agarra las **2 varillas más centradas** también en los costados | Corrección de un defecto del port anterior |
| El diamante **rodea** las varillas laterales en lugar de atravesarlas | La macro las cruza |
| El estribo principal se **abre** por donde pasa el diamante | `TrimEstriboBajoDiamante` |
| Tope de estribos por separación mínima | Ver `CalculateFlexibleLength` en §2.1 |
| **Cotas del bloque de sección** insertado en el alzado | Lo pediste. Ver el apartado 3.1 |

### 3.1 El bloque de sección del alzado va acotado

La macro inserta el bloque de la sección al lado del alzado y lo deja **sin acotar**: las
cotas que dibuja son las del alzado —la longitud, los estribos, los ejes—, no las de la
sección insertada. Así que en el plano la sección se veía, pero no decía cuánto medía.

Ahora `CotasDelCorte(x, y, ancho, alto)` acota **la caja real del bloque insertado**, no las
medidas capturadas: la base por abajo y la altura por la derecha, separadas `SepCotaCorte`
(0.06) por el factor de escala. Y se llama después de `Mover`, para que las dos cotas caigan
donde el bloque quedó.

Dos cosas que hay que saber:

- **Van fuera del bloque, a propósito.** `SeccionDrawer.Bloquear` excluye las capas COTAS y
  ROTULOS al armar el bloque de la sección, así que estas dos cotas no se pueden dibujar
  «dentro» de él: se dibujan en el dibujo, sobre el sitio donde el bloque quedó.
- El rótulo `CORTE A-A'` sube `AltoCotaCorte` (0.09) por el factor, y el aire sobre la
  sección —`AlzadoLayout.AireRotuloAlzado`— pasó de **0.10 a 0.19** para que la cota de la
  base no se meta en el rótulo. Son los 12.5 cm de aire que se habían perdido.
- **Muestran metros**: una sección de 30 × 60 se acota «0.30» y «0.60», igual que las cotas
  del concreto, porque llevan el texto vacío y el número lo mide AutoCAD sobre un dibujo que
  está en metros. Es el mismo defecto de unidades que ya tenían las cotas del alzado, no algo
  nuevo de estas dos.

---

## 4. Lo que sigue pendiente

| Pendiente | Nota |
|---|---|
| **Importador de Excel** | Nada de esto lee todavía la hoja: los datos se capturan en la cuadrícula. Es el hueco grande |
| Atributos del bloque de sección (`GetAttributes`) | El port no usa bloques con atributos |
| Alzado de **castillos y cadenas** | Confirmado contigo: no llevan alzado. Se omiten y se informa |
| Compilar `CadLink.App` | No se puede en este entorno: NuGet no es alcanzable y WPF pide `EnableWindowsTargeting` fuera de Windows |

---

## 5. Cómo comprobar todo esto

```bash
python3 tools/validar.py                       # validaciones estaticas del codigo
python3 tools/verificar_diametros_varilla.py   # la tabla contra la formula n/8"
python3 tools/verificar_estribos_vba.py        # reparto de estribos contra el VBA
python3 tools/verificar_layout_alzados.py      # colocacion contra las constantes
python3 tools/verificar_y_alzados.py           # los 2 m sobre la seccion mas alta
python3 tools/verificar_seccion_circular.py    # circulo de paso, zuncho y helice
python3 tools/verificar_grapa_lateral.py       # la grapa del diamante en costados
python3 tools/verificar_recorte_diamante.py    # el estribo abierto bajo el diamante
python3 tools/verificar_solapa.py              # la solapa del juego de planos
python3 tools/verificar_clk.py                 # ida y vuelta del archivo .clk
```

Y en Windows, lo que aquí no se puede:

```powershell
cd client
dotnet build
```
