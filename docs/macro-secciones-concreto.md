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

#### El gancho del diamante: las dos colas enteras

El gancho del diamante sale de una de las varillas centrales y tiene **dos colas**, una a
cada lado del radio hacia el centro de la sección. Cada cola es un rectángulo largo, y va
con sus **tres líneas**: la exterior, la interior —la que nace pegada a la varilla, tangente
a ella— y la punta que las une. Es la misma `Cola` que usa el gancho del estribo
rectangular, y dibuja las tres siempre: no hay ningún parámetro para saltarse ninguna.

Arriba de la varilla el contorno es **curvo**: es el arco del doblez del extremo que llega
por la diagonal de abajo, la envuelve y sale como la cola de arriba. Ese arco se dibuja solo
en el trozo que asoma del abrazo de la cinta (`ArcoDelDoblez`), porque el resto ya lo traza
el borde de la propia cinta.

#### La línea del diamante se corta con el ancho del brazo

El brazo de arriba pasa **por encima** de la diagonal del rombo. Pero la línea interior de
la cinta estaba dibujada de punta a punta, así que atravesaba el brazo por dentro y en el
plano parecía que la diagonal **cortaba el gancho** en lugar de pasarle por debajo.

Así que se le abre un hueco del ancho del brazo (`AbrirCintaBajoLaCola`). Y hay que decir
qué **no** es esto, porque suena a lo contrario: no le quita ninguna línea al gancho —las
dos colas siguen con sus tres—, el hueco es de la línea del **diamante**, que es la que
pasa por debajo. Solo la de arriba: la cola de abajo pasa por debajo de la cinta, y por ese
lado lo que se recorta es la cola.

El hueco no se estima, se **recorta**: el tramo recto de la cinta contra el rectángulo de la
cola (cuatro semiplanos) y contra la media corona del doblez. Las dos piezas se tocan en la
perpendicular a la varilla, así que su unión es un hueco seguido. Hacen falta las dos: con
solo la cola quedaba un rabito de línea justo encima de la varilla, y en una columna alta
—diagonal muy empinada— la cola no llega a cruzar el tramo y no se abría nada aunque el
doblez lo tapara. Sale del orden de **1.8 cm sobre una diagonal de 16**, un 11 %; si la
cuenta diera más del 50 % no se abre nada y se avisa, para que un error no borre media
diagonal.

Como no se puede borrarle un trozo a una polilínea, se monta otra **abierta**: empieza donde
acaba el hueco, da la vuelta entera por los mismos vértices —con sus mismos bulges, así que
los dobleces no se tocan— y termina donde el hueco empieza. La vieja se borra al final, no
antes, porque hacía de isla del relleno.

#### Dos cosas que se probaron y se revirtieron

1. **Quitar la línea interior de cada cola**, con el argumento de que su sitio lo cubre la
   circunferencia de la varilla. Eran **dos líneas que le faltaban al gancho**, una por
   cola, y sin ellas el rectángulo de la cola no cierra. Se restauraron.
2. **Alargar la cola de arriba hacia atrás** hasta el borde de la cinta, para que no naciera
   «en el aire». `Cola` engorda su cuadrilátero de relleno el espesor del estribo cuando le
   pasan un arranque distinto del natural, así que el alargue se sumaba al inflado y el
   hatch se salía del diamante **1.87 cm**. Era el «hatch que sale».

Lo que sí se recorta es la cola de **abajo**, donde sale del acero de la cinta
(`SalidaDelAceroDelDiamante`), porque por ese lado el gancho pasa por debajo. La de arriba
no, que justo en su arranque acaba el arco del doblez y las dos empalman tangentes.

#### Y los ganchos se ven en la vista previa

Hasta ahora la vista previa dibujaba dos rectángulos de estribo perfectos y **el gancho
aparecía por primera vez en AutoCAD**, que es justo al revés de lo que sirve: que exista, que
sea de 135° y que quepa dentro de la sección es lo primero que se revisa antes de mandar el
plano.

Ahora se dibuja, con la misma geometría del dibujante y en las dos formas de sección:

- En la **rectangular**, el doblez de la esquina superior derecha —centro a `rec + dEst + rIn`
  de las dos caras, media vuelta de 315° a 135°— y sus dos colas hacia el núcleo a 225°, cada
  una con sus tres líneas, con el recorte de la segunda cuando el estribo la cruza.
- En la **circular**, el gancho sobre la varilla de abajo: la cola es el radio hacia dentro
  girado 45° —que es lo que hace los 135°— y del doblez se dibuja solo el arco exterior, desde
  la tangencia con el paño del zuncho, porque el interior *es* la circunferencia de la varilla.

Un detalle que costó y conviene dejar escrito: **las cuentas van con la Y hacia arriba**, como
el dibujo, y la vuelta al lienzo se hace solo al pintar cada punto. En coordenadas de pantalla
la Y está invertida, y ahí «girar el radio 45°» gira para el otro lado: el gancho sale
espejeado —sigue siendo de 135°, pero apuntando al lado contrario que en AutoCAD—, que es
exactamente lo que una vista previa no puede hacer.

#### Y el rombo también, con la geometría del dibujante

El estribo diamante se ve en la vista previa con sus **dos cintas** y su gancho. Y no se
calcula ahí: la geometría se sacó a **`CadLink.Cad/TrazoDiamante.cs`**, que no sabe nada de
AutoCAD ni de WPF, y la usan los dos —el dibujante y la vista previa—.

Es la misma decisión que `TrazoAcero`, y por el mismo motivo: un diamante **no es un rombo**,
es una cinta cerrada tangente a una serie de círculos, con la regla de una o dos varillas por
vértice y con las laterales que hay que rodear. Calcular eso por segunda vez en la vista previa
es la manera de acabar enseñando un rombo con otro vértice, otra varilla abrazada, o esquinas
en pico donde el dibujo lleva dobleces redondeados.

Lo que se movió: `Centros` —el recorrido—, `Cinta` —la tangente, que era `GeometriaCinta`—,
`DoblezLateral`, `RodearLaterales`, `VarillasDelCentro` y los dos helpers de distancia. El
dibujante los llama; no le quedó ninguna copia. Y como `TrazoDiamante` no puede escribir en el
registro del dibujante, las dos notas que da —cuántas varillas laterales acabó rodeando, y si
no pudo— las **devuelve** en una lista.

Lo nuevo es `Muestrear`, que convierte los *bulges* de la cinta en puntos, porque un lienzo de
WPF no dibuja arcos con bulges.

#### Una prueba que se EJECUTA, y lo que cazó

`tools/prueba-trazo-diamante` es un programa que corre contra el `CadLink.Cad` **compilado**,
en lugar de portar el cálculo a Python como el resto de las comprobaciones de este repositorio.
Se corre en Windows con `dotnet run` y devuelve 1 si algo falla.

Hacía falta, y se vio enseguida. Los `verificar_*.py` comprueban la **geometría**, pero no lo
que el código compilado hace: un port correcto conviviendo con un C# equivocado da todo en
verde. Y `Muestrear` estaba equivocado: calculaba el centro del arco con el radio en valor
absoluto y un apaño para decidir de qué lado de la cuerda cae, así que los puntos se salían del
doblez **hasta 0.74 cm** y el rombo de la vista previa habría salido con los dobleces
deformados. Con el radio y la distancia **con signo** —`cuerda / (2·sen(θ/2))` y
`radio·cos(θ/2)`— el lado sale solo, porque el coseno ya cambia de signo pasada la media vuelta.
El peor punto se sale ahora **4·10⁻¹⁵ cm**.

La prueba comprueba, con una columna de 40×40 armada como la del ejemplo: que el recorrido va
antihorario —lo que evita que la cinta salga hecha un nudo—, que abraza las cuatro varillas
centrales, que las dos cintas son **tangentes** a cada círculo al bit, que el muestreo conserva
todos los vértices y que cada punto que añade cae sobre el arco de su doblez.

`tools/verificar_gancho_diamante.py` lo comprueba con números: las seis líneas —tres por
cola—, que la interior sale tangente a la varilla, que lo que el acero dobla es lo que
envuelve, que el relleno no se sale y que alargando la cola sí se salía. Y del hueco: que
empieza en una cara del brazo y acaba en la otra, que es **exactamente** lo que el gancho
tapa —contrastado punto a punto contra un muestreo escrito aparte de las fórmulas del
recorte— y que la cinta abierta conserva todos los vértices de la cerrada.

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
