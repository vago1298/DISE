# Especificación: Plantas Estructurales (ETABS → Excel → AutoCAD)

Ingeniería inversa de la macro VBA `PLANOS ESTRUCTURALES` (v50).
Flujo: **ETABS → memoria → clasificación geométrica → AutoCAD**, con volcado
opcional a las hojas `MODELO_ETABS` y `SECCIONES`.

---

## 1. La arquitectura ya está bien separada

Esto es lo más importante para el port, y es mérito de cómo está escrita:

```
┌─────────────────────────────────────────────────────────────┐
│  CAPA 1 — LECTURA DE ETABS                                  │
│  LeerModelo, LeerPuntos, DimsDeSeccion, PropiedadDeMuro,     │
│  PropiedadDeLosa, LeerEjes, LeerAlturasDeEntrepiso           │
│  → llena los arreglos en memoria.  Depende de ETABS.         │
├─────────────────────────────────────────────────────────────┤
│  CAPA 2 — CLASIFICACIÓN Y GEOMETRÍA        ⭐ SIN COM        │
│  MarcarMurosTapados, ClasificarAlturaMuros,                  │
│  MarcarCadenasSinMuro, ClasificarApoyoLosas, RecortarAlPano, │
│  PanoRect, PanoCirculo, PanoColumnaW, ContornoLosaAlPano,    │
│  LongitudUnion, CortesEnX/Y, DeltaPano, EjesLocalesFrame     │
│  → aritmética pura.  NO toca ETABS ni AutoCAD.               │
├─────────────────────────────────────────────────────────────┤
│  CAPA 3 — DIBUJO                                             │
│  DibujarElemento, DibujarArmadoLosa, ArmadoMalla,            │
│  DibujarEjes, AcotarEjes, DibujarTitulo, TraerCapasAlFrente  │
│  → depende de AutoCAD.                                       │
└─────────────────────────────────────────────────────────────┘
```

**La capa 2 es la mitad del código y no tiene ninguna dependencia externa.**
Se traduce a C# tal cual y, sobre todo, **se puede probar sin ETABS y sin
AutoCAD**. Ahí está la clave de una migración de bajo riesgo (ver §6).

---

## 2. API de ETABS que se usa

Enlace **temprano**: `Dim myETABS As ETABSv1.cOAPI`, con referencia a la
biblioteca de tipos `ETABSv1`. Conexión a la instancia abierta:

```vba
Set myETABS = GetObject(, "CSI.ETABS.API.ETABSObject")
Set SapModel = myETABS.SapModel
```

| Objeto | Métodos usados |
|---|---|
| `SapModel` | `SetPresentUnits(6)` = kN·m·C, `GetProgramInfo`, `GetModelFilename` |
| `PointObj` | `GetNameList`, `GetCoordCartesian` |
| `FrameObj` | `GetLabelNameList`, `GetPoints`, `GetSection`, `GetLocalAxes`, `GetTransformationMatrix` |
| `AreaObj` | `GetLabelNameList`, `GetPoints`, `GetProperty`, `GetPier` |
| `PropFrame` | `GetRectangle`, `GetCircle`, `GetISection`, `GetTube`, `GetPipe`, `GetChannel`, `GetTee`, `GetAngle` |
| `PropArea` | `GetWall`, `GetSlab`, `GetDeck` |
| `Story` | `GetStories_2` con respaldo a `GetStories` |
| `GridSys` | `GetNameList`, `GetGridSys_2` |

La detección de forma es **por prueba y error en cascada**: se intenta
`GetRectangle`; si falla, `GetCircle`; luego `GetISection`, `GetTube`, `GetPipe`,
`GetChannel`, `GetTee`, `GetAngle`; y si todo falla, se deducen las dimensiones
del **nombre** de la sección (`DimsDesdeNombre`, formato `30X60`).

---

## 3. Modelo de datos

56 arreglos paralelos indexados de 1 a `mNumEl`, con crecimiento por bloques de
2000 (`IniciarElems` / `AgregaElem`). Los vértices de las losas van en un arreglo
aparte (`mVX`, `mVY`) y cada losa guarda `eIV` (índice del primer vértice) y
`eNV` (cuántos).

**Clasificación:**

```
frame vertical (Δx,Δy < tol)     → COLUMNA  → CASTILLO si ambos lados ≤ 20 cm
                                              o el nombre dice CASTILLO
frame horizontal (Δz < tol)      → TRABE    → CONTRATRABE / DALA (peralte ≤ 25 cm
                                              o nombre con DALA o CERRAMIENTO)
frame inclinado                  → DIAGONAL
área vertical (Δz > 0.05)        → MURO     → MAMPOSTERIA / CONCRETO por palabras
área horizontal                  → LOSA     → se ignora si es escalera
```

### ⚠️ Unidades mezcladas

Después de leer, hay un lazo que multiplica por `gFactCoord`:

```
SE ESCALAN:      eX1 eY1 eZ1 eX2 eY2 eZ2 eB eH eT2 eT3 eTf eTw mVX mVY
NO SE ESCALAN:   eEsp eZmin eZmax eAng
```

O sea que el modelo en memoria queda con **dos sistemas de unidades
simultáneos**: coordenadas y dimensiones en unidades de dibujo, y espesor de
losa, cotas Z y ángulos en las unidades del modelo (metros). Funciona porque los
parámetros que se comparan contra cada grupo se escalan de forma coherente
(`gMuroAltMin` sin escalar, `gLosaTol` escalado), pero es un invariante
**implícito y no documentado**. Al portar conviene volverlo explícito con sufijos
en los nombres (`_m` vs `_ud`) o tipos distintos.

---

## 4. Los algoritmos que valen

### 4.1 Cobertura por unión de intervalos (`LongitudUnion`)
Proyecta cada elemento candidato sobre la recta del muro, acumula intervalos
`[t1,t2]`, los ordena y suma la **unión** (no la suma, que contaría los
traslapes). Se usa para tres decisiones:

- `MarcarMurosTapados` — si las cadenas cubren ≥ 80 % del muro, el muro no se dibuja
- `MarcarCadenasSinMuro` — si el muro de piso a techo cubre < 50 % de la cadena,
  la cadena va con línea punteada
- `LadoApoyado` — si hay apoyo en ≥ 70 % del lado del tablero

### 4.2 Llegada al paño (`RecortarAlPano`)
Por cada extremo de muro o trabe se busca el elemento más cercano y se calcula
dónde el rayo del eje corta su huella:

- **Rectángulo** (`PanoRect`) — algoritmo de losa (*slab method*): se intersecta
  el rayo con las dos franjas de los ejes locales y se toma `[tMin, tMax]`.
  Devuelve `tMax - solape`. Si `tMax < 0` el resultado es negativo, lo que
  **alarga** la línea hasta tocar el elemento en lugar de recortarla. Elegante.
- **Círculo** (`PanoCirculo`) — ecuación cuadrática.
- **Columna W** (`PanoColumnaW`) — el caso fino:
  - si la viga entra **entre los patines** (`|d3| > |d2|`), se para en la **cara
    del alma**, con solape forzado a 0 porque el alma mide 6-8 mm
  - si llega **por el patín**, va hasta el **centro** del perfil
  - solo aplica a **columnas** (`eVert`), no a vigas de acero: a una viga se
    llega al paño, no al alma

### 4.3 Ejes locales del frame (`EjesLocalesFrame`)
Reconstruye la terna local de ETABS: para un elemento no vertical,
`r2 = (-u1x·u1z/hn, -u1y·u1z/hn, hn)` y `r3 = u1 × r2`; luego rota por el ángulo
local. Si `USAR_MATRIZ_EJES` está en SI, intenta
`GetTransformationMatrix` y valida que la fila o columna correcta coincida con
`u1` (`MismoVector`) antes de usarla, con dos convenciones de almacenamiento
posibles. Es una defensa correcta contra el cambio de orden fila/columna entre
versiones de la API.

### 4.4 Contorno de la losa al paño (`ContornoLosaAlPano`)
Desplaza cada lado del polígono hacia adentro medio ancho del apoyo que corre
sobre él, y **recalcula los vértices como intersección de las rectas
desplazadas** (no mueve los vértices, que deformaría el polígono). Valida con
tres pruebas: que el sentido de giro se conserve, que el área no crezca, y que no
se colapse por debajo del 25 %.

### 4.5 Armado de losa
- **Apoyo en 4 lados** → armado con bayoneta: polilínea de 6 vértices con
  quiebres a 45°, filete opcional (`PolilineaConFilete` calcula el *bulge* como
  `Tan(φ/4)`), doble línea vía `Offset(±d/2)` con respaldo a `Copy` + `Move`.
  Más bastones a L/4 y corrida.
- **Un sentido** → hatch `ANSI37` a 45°, o parrilla de varillas
- **Volada** → hatch (`LOSA_HATCH_SOLO_VOLADO`)
- **Losacero** → franjas con hatch `FLEX` en el sentido corto, más el rótulo
  `LOSACERO IMSA CALIBRE %C`, donde el calibre se extrae de las notas de la
  sección de ETABS buscando el número después de `CAL`, o el último número

La parrilla se recorta al contorno real (`CortesEnX` / `CortesEnY`, con regla
semiabierta `(ay <= c And by > c)` para que los vértices no cuenten doble) y se
abren huecos en los cruces (`VarillaCortada`) según qué dirección va encima.

### 4.6 Ejes y cotas
Burbujas en los cuatro lados a `EJES_INICIO_BURBUJA_M` de la planta, con la
línea de eje terminando justo donde arranca la burbuja. Cotas en los cuatro
lados, cadena eje a eje a 0.75 y cota total a 1.17, con la línea de extensión de
la total acortada para que se vea el aire contra la burbuja.

El primer y último eje se corren al paño exterior del muro
(`AjustarEjesExtremosAlPano` + `MedioAnchoSobreEje`).

---

## 5. Problemas detectados

### 5.1 Elemento con extremo en el origen  🔴

En `LeerModelo`:

```vba
If Coord(p1, x, y, z) Then
    AgregaElem
    eX1 = x: eY1 = y: eZ1 = z
    x = 0: y = 0: z = 0
    Coord p2, x, y, z          ' ← el valor de retorno se ignora
    eX2 = x: eY2 = y: eZ2 = z
```

Si el punto `p2` no está en el diccionario, `Coord` devuelve False y las
variables se quedan en **cero**: el elemento se registra con un extremo en el
**origen del modelo**. En el dibujo aparece como una línea que cruza toda la
planta hacia la esquina, sin ningún aviso.

Y si falla `p1`, el frame se descarta **en silencio**, sin contador ni aviso: el
resumen dice que leyó N frames pero dibujó menos, y no hay forma de saber cuáles.

**Al portar:** validar los dos extremos, y si falta alguno, registrar el elemento
en la lista de avisos con su etiqueta. Un elemento que desaparece de un plano
estructural sin avisar es peor que un error.

### 5.2 Rendimiento: `AjustePano` es el cuello de botella  🔴

```
AjustePano  →  recorre LOS 2000+ ELEMENTOS
            →  se llama 2 veces por cada tramo de varilla de la parrilla
            →  la parrilla va a 15 cm en las dos direcciones
```

En un tablero de 8 × 6 m son ~93 varillas, cada una con 2 extremos, y si hay
varios tableros por nivel y varios niveles, se llega fácil a **cientos de miles
de recorridos sobre todos los elementos**. Lo mismo aplica, en menor grado, a
`RecorteEnExtremo`, `AnchoApoyoEnLado`, `LadoApoyado`, `HayLosaVecina` y
`ClasificarAlturaMuros` (que es O(muros²) con el lazo interno sobre todo).

**Al portar:** un **índice espacial** (rejilla uniforme o R-tree) sobre muros y
trabes por nivel. Las consultas pasan de O(n) a O(1) amortizado. Es la
optimización con mejor relación esfuerzo/beneficio de todo el proyecto, y no
cambia ni un resultado: solo evita mirar elementos que están lejos.

### 5.3 `SetPresentUnits` cambia el ETABS del usuario  🟠

`InfoDelModelo` hace `SapModel.SetPresentUnits(U_KN_M_C)` y **nunca lo
restaura**. El ingeniero deja ETABS en kgf-cm, corre la macro, y al volver lo
encuentra en kN-m. No corrompe el modelo, pero para un producto de paga es un
efecto secundario que no se pidió.

**Al portar:** leer las unidades actuales, cambiarlas, y restaurarlas al
terminar, incluso si hubo excepción.

### 5.4 `BorrarCapasGeneradas` borra trabajo del usuario  🟠

Borra **todo** lo que esté en ModelSpace con el prefijo `E-`, más las capas
`PIERS` y la de cadena de desplante. Si el ingeniero agregó a mano una anotación
en `E-TEXTO`, o movió algo a esas capas, se pierde sin preguntar.

**Al portar:** marcar las entidades generadas con **XData** propio y borrar solo
las que lleven esa marca. Así lo del usuario sobrevive.

### 5.5 Compatibilidad de versiones de ETABS  🟠 *crítico para vender*

El código tiene este comentario:

> *Si al COMPILAR marca error en la línea de abajo, tu versión de ETABS no tiene
> GetGridSys_2: ponle una comilla al inicio de esa línea.*

Con enlace temprano eso es un **error de compilación**, no de ejecución. Hoy no
importa porque compilas en tu máquina con tu ETABS 2021. Pero **tus clientes van
a tener ETABS 19, 20, 21, 22 y 23**, y un binario compilado contra una versión
puede no encontrar un método en otra.

La macro ya resuelve bien este caso para `GetStories_2` → `GetStories`, con
`On Error Resume Next` y respaldo. Hay que aplicar el mismo patrón a
`GetGridSys_2` y a cualquier otro método que varíe.

**Al portar:** referenciar `ETABSv1.dll` (que CSI mantiene estable a propósito) y
envolver las llamadas variables en `try`/`catch` con respaldo explícito, o
resolverlas por reflexión. Es requisito para poder venderle a cualquiera.

### 5.6 `RecrearAlFrente` cambia los handles  🟡

El respaldo del *bring to front* copia y borra las entidades. Eso les cambia el
handle, lo que rompe cualquier referencia externa (xref, campos, anotaciones
asociativas). Solo se usa si falla `ACAD_SORTENTS`, pero conviene avisar al
usuario cuando ocurre.

### 5.7 Fragilidad de los 56 arreglos paralelos  🟡

`IniciarElems` y `AgregaElem` tienen que declarar **la misma lista de 56
arreglos**. Hoy están sincronizados, pero agregar un campo y olvidarlo en el
`ReDim Preserve` produce un error en tiempo de ejecución solo cuando el modelo
pasa de 2000 elementos — es decir, en el proyecto grande del cliente, no en tus
pruebas.

**Al portar:** una clase `Elemento` y una `List<Elemento>`. Desaparecen ~200
líneas de `ReDim` y la clase entera de bug.

---

## 6. ⭐ La estrategia de migración: ya tienes el arnés de pruebas

Sin darte cuenta lo construiste: la hoja **`MODELO_ETABS`** con sus 35 columnas
es un **volcado completo del estado interno** después de leer y clasificar. Y la
hoja `SECCIONES` es otro.

Eso permite migrar por capas **verificando numéricamente**, sin abrir AutoCAD:

| Fase | Qué se porta | Cómo se verifica |
|---|---|---|
| **1** | Capa 1: lectura de ETABS | El C# escribe su propio `MODELO_ETABS`. Se comparan las columnas de lectura (STORY, CLASE, SECCION, XI..ZJ, T3, T2, FORMA, ESPESOR) **celda por celda** contra la del VBA sobre el mismo modelo |
| **2** | Capa 2: clasificación y geometría | Se comparan las columnas **derivadas**: MURO TAPADO, ALT MURO, MURO COMPLETO, APOYO 4 LADOS, EXT ARMADO I/D/AB/AR, RECORTE I/J, DIR EJE 2/3, SE ARMA. Si cuadran, toda la geometría está bien portada |
| **3** | Capa 3: dibujo | Ahora sí AutoCAD: conteo de entidades por capa, y comparación visual sobre el mismo modelo |

**Las fases 1 y 2 no dependen de la decisión COM vs plugin.** Se pueden empezar
hoy y sirven igual para las dos rutas. Y cubren más de la mitad del código.

Sobre el mismo modelo de ETABS, dos ejecuciones y un `diff` de las dos hojas: si
las columnas derivadas coinciden hasta el último decimal, la migración de la
lógica de ingeniería está probada. Eso es lo que convierte un port de 5.000
líneas de "esperemos que salga igual" en algo verificable.

---

## 7. El sistema de configuración

~250 parámetros en la hoja `CONFIG`, en tres columnas: parámetro, valor,
descripción. Lectura tipada con `CfgS` / `CfgT` (sin recortar espacios) / `CfgD`
/ `CfgB`, y **auto-migración**:

- `VERSION_CONFIG` (29) dispara `MigrarConfig`, que reescribe valores
- `VERSION_PARCHE` (50) dispara parches acumulativos: cada bloque
  `If CfgD("VERSION_PARCHE", 0) < N` se aplica una sola vez y sella la versión
- `PonCfgSiFalta` agrega renglones nuevos sin pisar lo que el usuario ya ajustó
- `PonCfg` sí pisa el valor: se usa solo dentro de los parches

Es un diseño de migración de configuración mejor que el de mucho software
comercial. **Consérvalo en el port**, cambiando el respaldo de la hoja de Excel a
un JSON versionado, con la misma idea de parches idempotentes sellados por número
de versión.

> La hoja `CONFIG` es la fuente de verdad de los valores por omisión; no se
> reproducen aquí los 250 porque se desincronizarían. `CrearHojaConfig` los
> genera todos con su descripción.
