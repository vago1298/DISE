# Plan del port: macro PLANOS ESTRUCTURALES (ETABS/SAP2000 → AutoCAD)

Lo que se pidió: **borrar el dibujo de plantas que hay hoy y dejar solo la macro**,
mandando ella en capas, opciones y todo lo demás, y que **lea igual de SAP2000
cambiando de opción en una casilla**. Y sin tocar ninguna capa ni ningún color.

El análisis de la macro está en `macro-plantas-etabs.md`. Este archivo es el orden
de trabajo y cómo se comprueba cada etapa.

## Hecho

- **La casilla ETABS / SAP2000**, en las **dos** pestañas —«ETABS/SAP2000» y
  «Dibujar planos estructurales»—. Son la misma casilla, atadas con un enlace de
  dos vías, y de ella sale el destino para todo: probar la conexión, leer el
  modelo, leer los piers y leer las plantas. El lector es uno solo
  (`EtabsConnection.Destino`): CSI comparte la OAPI entre los dos programas y lo
  que cambia es el ProgID y la librería que se carga.
- **Capa 1, lectura** (`CadLink.Etabs`): puntos, marcos, áreas, secciones, pisos,
  ejes y piers.
- **Etapa 1: la hoja `CONFIG`** — `CadLink.Cad/PlanoEstructural/ConfigPlano.cs`.
  Los 261 renglones de `CrearHojaConfig` con su valor y su descripción, la lectura
  tipada con las mismas reglas (`CfgS`/`CfgT`/`CfgD`/`CfgB`), las versiones 29 y 50
  y las migraciones por número de versión. En la app no hay hoja de Excel: los
  valores de omisión están en la tabla y solo se guarda **lo que el usuario cambie**,
  que es lo que deja entrar los valores nuevos de cada versión.
- **Las capas y sus colores** — `CadLink.Cad/PlanoEstructural/CapasPlano.cs`. Las
  21 capas de `DefinirCapas` + `CrearCapas`, con los colores de la macro: los que
  ella lleva escritos van con ese número (MURO 6, COLUMNA 1, TRABE 3 con PHANTOM2,
  CONTRATRABE 2, LOSA 8, DIAGONAL 30, OTROS 7, TEXTO 7) y los demás salen de la
  hoja. `PIERS` es la única sin prefijo, igual que allá. Un color fuera de rango
  regresa al de la macro, nunca a blanco.

  Comprobado con `tools/prueba-config-plano` (96 comprobaciones sobre el
  `CadLink.Cad` **compilado**) y con `tools/validar.py`.

## Lo que falta

Cada etapa deja el programa funcionando: **el dibujo de hoy no se borra hasta que
el nuevo dibuje lo mismo**.

| # | Qué | Dónde | Cómo se comprueba |
|---|---|---|---|
| 2 | Los parámetros resueltos: `LeerConfig` entero —las ~200 variables `g*` con su escalado por `FACTOR_UNIDADES`, sus `/100` de centímetros y sus topes— | `ParametrosPlano.cs` | Prueba ejecutable: cada variable contra el valor que le sale a la macro con la hoja de omisión, y contra los topes en los casos raros (0, negativo, fuera de rango) |
| 3 | Capa 2: clasificación y geometría. Es **la mitad del código y no toca ni ETABS ni AutoCAD**: `ClasificaTipo`, `MarcarMurosTapados`, `ClasificarAlturaMuros`, `MarcarCadenasSinMuro`, `ClasificarApoyoLosas`, `RecortarAlPano`, `PanoRect`, `PanoCirculo`, `PanoColumnaW`, `EjesLocalesFrame`, `ContornoLosaAlPano`, `LongitudUnion`, `CortesEnX/Y`, `AjustePano`, `DeltaPano` | `PlanoEstructural/` | Volcado con las mismas 35 columnas de `MODELO_ETABS`, comparado **celda por celda** contra el de la macro sobre el mismo modelo |
| 4 | Capa 3: el dibujo. Elementos y bloques de sección, armado de losa con bayoneta, parrilla recortada al contorno, hatch `ANSI37`, franjas `FLEX` de losacero con su rótulo de calibre, mampostería, rótulos de cadenas, ejes con burbujas, cotas en los cuatro lados, título y `ACAD_SORTENTS` al frente | `PlanoEstructuralDrawer.cs` | Conteo de entidades por capa y comparación visual sobre el mismo modelo |

### Lo que ya hace el dibujante de hoy, mientras llega el nuevo

No es el port, pero es lo que se puede usar entretanto:

- **Todas las plantas de un jalón**, una al lado de otra, del nivel más bajo al más
  alto, con `SEPARACION_ENTRE_PLANTAS`, `OFFSET_Y_INICIAL` y `PLANTAS_POR_FILA` de la
  hoja CONFIG. Con una casilla para dibujar solo la del nivel elegido.
- **Las capas de la macro**, cada una con su color: la capa de cada elemento sale de
  su **tipo** —`E-CASTILLO`, `E-COLUMNA`, `E-DALA`, `E-TRABE`, `E-CONTRATRABE`— y un
  perfil de acero se va a `E-ACERO`.
- **Los rótulos donde los pone la macro**: la sección de la columna en la esquina
  superior derecha, la de la trabe girada a lo largo de la barra y el pier del muro
  corrido al lado. Todos al centro y horizontales era lo que convertía cada nudo en un
  borrón.

Lo que **falta** para que salga igual que la suya: ejes con burbujas, cotas en los
cuatro lados, bloques de sección rellenos, armado de losa, hatch, mampostería y el
rótulo de dos renglones con su tipografía. Todo eso es la etapa 4.
| 5 | Borrar lo viejo: `PlantaDrawer.cs` (658 líneas) y `PlantaCad.cs`, y colgar el botón de la pestaña del dibujo nuevo | | Que el plano salga igual que con la macro |

## Dos cosas que se arreglan al portar

No cambian ningún resultado, y están en `macro-plantas-etabs.md` §5:

- **`AjustePano` recorre los 2000+ elementos** dos veces por cada tramo de varilla
  de la parrilla. Con un índice espacial por nivel las consultas pasan de O(n) a
  O(1) y el dibujo de un edificio deja de tardar minutos.
- **`BorrarCapasGeneradas` borra todo lo que esté en las capas `E-`**, incluido lo
  que el usuario haya puesto ahí a mano. Marcando lo generado con XData propio se
  borra solo lo del plano.
