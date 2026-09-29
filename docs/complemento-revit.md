# El complemento de CadLink para Revit

Un plugin propio que lee el modelo exportado por CadLink, **se conecta a Revit para ver qué
familias tienes cargadas**, te pregunta con cuál modelar cada sección del cálculo, y crea
columnas, trabes, diagonales, muros y losas **nativas de Revit**.

Es lo que el IFC no puede hacer. Conviene tener claro en qué se diferencian.

| | `.ifc` | El complemento |
|---|---|---|
| Cómo entra | *Vincular IFC* o *Abrir IFC* | Botón **CadLink → Importar modelo** |
| Qué llega | Geometría con tipos por sección | **Elementos nativos** de Revit |
| Elegir familia | No es posible | **Sí, en un cuadro, sección por sección** |
| Reimportar | Duplica o hay que rehacer | Actualiza lo que ya está |
| Editable en Revit | Limitado | Como cualquier elemento del modelo |

Los dos salen del mismo botón de CadLink: al exportar se escriben el `.ifc` **y** el
`.cadlink-modelo.json` que come el complemento.

---

## Por qué el IFC no puede pedirte la familia

Porque no es cosa de CadLink: es del formato y del importador de Revit.

Revit, al importar un IFC, mapea **clase IFC → categoría de Revit** (`IfcColumn` → Pilares
estructurales) usando un archivo de texto de mapeo. Es un mapeo **por categoría, no por
familia ni por tipo**, y no hay ningún punto del proceso donde Revit te ofrezca elegir. No
sabe qué familias tienes en tu plantilla.

De ahí este complemento: es un programa que corre **dentro** de Revit, puede preguntarle qué
hay cargado, y puede crear elementos nativos con el tipo que elijas.

---

## Instalar

Hace falta **Revit 2025 o 2026** y el **SDK de .NET 8**.

> Revit 2025 y 2026 usan .NET 8. Hasta Revit 2024 los complementos eran .NET Framework 4.8,
> así que este **no sirve para 2024 o anterior** sin recompilarlo con otro
> `TargetFramework`.

### La forma fácil

Doble clic en **`7-instalar-plugin-revit.bat`**, en la raíz del proyecto. Compila, busca Revit,
y copia los tres archivos donde van.

Si tu Revit está en una carpeta que no es la normal, pasale la ruta:

```
7-instalar-plugin-revit.bat "D:\Autodesk\Revit 2026"
```

Después: **cerrar Revit y volver a abrirlo**. El complemento se carga al arrancar, así que no
aparece hasta que Revit se reinicia. Es el motivo número uno de «lo instalé y no sale nada».

### A mano

Por si el `.bat` falla o querés entender qué hace.

1. Compilar:

   ```
   dotnet build client\CadLink.Revit.sln -c Release
   ```

   Si Revit no está en la ruta normal:

   ```
   dotnet build client\CadLink.Revit.sln -c Release /p:RutaRevit="D:\Autodesk\Revit 2026"
   ```

   Si falta Revit, el proyecto se detiene con un mensaje que lo dice. Sin eso, el primer
   síntoma serían doscientos errores `CS0246` de tipos que no existen, y nadie relaciona eso
   con una ruta mal puesta.

2. Copiar **dos** archivos a la carpeta de complementos de Revit:

   ```
   %APPDATA%\Autodesk\Revit\Addins\2026\
   ```

   - `CadLink.Revit.dll`
   - `CadLink.Revit.addin`
   - y también `CadLink.Revit.Nucleo.dll`, que va al lado de la primera.

3. Abrir Revit. Aparece una pestaña **CadLink** con el botón *Importar modelo*.

---

## Cómo aparece la ventana dentro de Revit

No hace falta ningún otro complemento: la ventana de mapeo **es parte de este**. La cadena
completa, para que se entienda dónde mirar si algo no sale:

```
1. Revit arranca y lee los .addin de  %APPDATA%\Autodesk\Revit\Addins\2026\
        |
2. Encuentra CadLink.Revit.addin, que le dice que cargue CadLink.Revit.dll
   y que la clase de entrada es  CadLink.Revit.Aplicacion
        |
3. Llama a  Aplicacion.OnStartup(...)  -> IExternalApplication
   Ahi se crea la pestana CadLink y el boton "Importar modelo"      (Aplicacion.cs)
        |
4. Al pulsar el boton, Revit ejecuta  ComandoImportar.Execute(...)  -> IExternalCommand
        |
5. El comando pide el archivo, recorre el documento para ver que familias hay,
   y hace  ventana.ShowDialog()                                     (ComandoImportar.cs)
        |
6. Y esa ventana es  VentanaMapeo.xaml  <-- LA DE LA IMAGEN
        |
7. Al pulsar "Modelar nuevos", el comando abre una transaccion y crea los elementos
```

Donde mirar si falla cada paso:

| Síntoma | Dónde está el problema |
|---|---|
| No aparece la pestaña CadLink | Paso 1 o 2: los archivos no están en la carpeta, o no reiniciaste Revit |
| Revit avisa de un complemento que falló al cargar | Paso 3: `FullClassName` del `.addin` no coincide, o falta `CadLink.Revit.Nucleo.dll` al lado |
| El botón está pero no hace nada | Paso 4: falta el atributo `[Transaction]` en el comando |
| Se abre el diálogo de archivo y luego nada | Paso 5: la ventana quedó detrás de Revit, o el modelo no trae piezas |
| La ventana sale vacía | Paso 5: en el proyecto no hay familias estructurales cargadas |

> **Las DLL de Revit no se copian.** Las referencias van con `Private=false` a propósito.
> Copiar `RevitAPI.dll` junto al complemento hace que Revit cargue dos veces los mismos tipos
> y falle con errores incomprensibles.

---

## Usar

1. En CadLink: leer el modelo de ETABS o SAP2000 y pulsar **Exportar a Revit (IFC)**. Se
   escriben el `.ifc`, el `.secciones.csv` y el **`.cadlink-modelo.json`**.
2. En Revit, con el proyecto abierto: **CadLink → Importar modelo**, y elegir ese `.json`.
3. Se abre el cuadro de mapeo.

### El cuadro

Una fila **por sección**, no por pieza. Un modelo con tres mil columnas suele tener quince
secciones, y esa es la diferencia entre un cuadro que se puede llenar y uno que no.

| Columna | Qué es |
|---|---|
| Sección del cálculo | `K 30X60 (columna)` — el nombre de ETABS y su clase |
| Medidas | `30 x 60 cm`, o `e = 20 cm` en muros y losas |
| Piezas | Cuántas la usan |
| Familia de Revit | Las familias **que hay en tu proyecto**, de la categoría que toca |
| Tipo | Los tipos de esa familia |

La clase va entre paréntesis porque **la misma propiedad de ETABS se usa a veces como
columna y a veces como trabe**, y entonces no va a la misma categoría de Revit. Sin la clase
a la vista, las dos filas se verían idénticas y parecería un error del programa.

Los colores: **azul claro** es una propuesta automática, **rojo claro** es una sección sin
tipo elegido, que **no se va a modelar**.

### Los tres botones

- **Modelar nuevos** — crea lo que falta y **no toca** lo que ya está. Es lo que se quiere la
  primera vez, y también cuando el cálculo creció y solo hay piezas nuevas. Lo que ya está en
  Revit puede llevar trabajo encima —uniones, anotaciones, ajustes a mano— y machacarlo sin
  que nadie lo pida sería destructivo.
- **Actualizar existentes** — re-tipa y recoloca lo que ya está, y **no crea nada**. Es para
  después de cambiar secciones en ETABS.
- **Aceptar lo más parecido** — rellena las filas vacías con el candidato más cercano, aunque
  no convenza. Es un acto explícito: nunca pasa solo.

**Un solo Ctrl+Z deshace la importación completa**, porque todo va en una sola transacción.

---

## Cómo se emparejan las secciones con tus familias

El complemento **no trae ninguna lista de familias escrita**. Recorre el proyecto abierto y
ofrece lo que encuentra. Si no tienes ninguna familia de perfiles de acero, una sección de
acero se quedará sin propuesta, y esa es la respuesta correcta.

Para proponer, mira tres cosas con este peso:

| Qué | Puntos | De dónde sale |
|---|---|---|
| **Forma** | 100 | La sección estructural que declara Revit. Es el dato bueno |
| Forma | 60 | Deducida del nombre de la familia, si Revit no la declara |
| Forma distinta | −80 | Descarta en la práctica |
| **Medidas** | hasta 60 | Parámetros del tipo (`b`, `h`, `d`, `bf`…), o del nombre |
| **Nombre** | 40 | Si el del tipo y el de la sección se parecen |

Y solo se deja **puesta** en el cuadro si suma 80 o más. Por debajo se muestra como pista
pero la fila queda vacía: una propuesta floja aceptada sin mirar produce un modelo con las
secciones cambiadas, y eso es peor que un cuadro que obliga a elegir.

Detalles que importan:

- **Las medidas valen al revés.** Un tipo de Revit de 30 × 60 sirve para una sección de
  60 × 30: lo que cambia es el giro de la pieza, no el tipo.
- **Un cajón puede caer en un rectángulo** del mismo tamaño, y un tubo en un círculo. En
  muchas plantillas no hay familia para la versión hueca, y modelar un HSS con un rectángulo
  macizo es mucho mejor que no modelarlo. Lo que **no** se cruza son familias distintas: una
  I nunca se propone donde va un ángulo.
- **Más de un 25 % de diferencia y no se propone nada.** Una columna de 30 y otra de 40 son
  columnas distintas.
- **El nombre del tipo se lee con prudencia.** `300 x 450` se toma como milímetros, y
  `HSS3X2X1/4` **no** se interpreta: son pulgadas con fracciones, y equivocarse ahí
  propondría una familia con las medidas mal por un factor de dos y medio.

### El mapeo se guarda

Junto al modelo, como `<obra>.cadlink-mapeo.json`. La siguiente importación viene ya
rellenada y **lo guardado manda sobre lo propuesto**: es una decisión que alguien ya tomó.

Se guarda por **nombre** de familia y tipo, no por su `ElementId` interno, para que el mapeo
siga sirviendo al abrirlo en otro proyecto. Lo que ya no exista se reporta en vez de dejar la
fila vacía sin explicación.

---

## Qué hace al modelar

| Clase | Se crea como |
|---|---|
| Columna | `FamilyInstance` en Pilares estructurales |
| Trabe | `FamilyInstance` en Estructura, como viga |
| Diagonal | `FamilyInstance` en Estructura, como arriostre |
| Muro | `Wall.Create` desde el **contorno** del paño |
| Losa | `Floor.Create` desde el contorno |

Los muros se crean desde el contorno y no desde una línea con una altura, porque un paño de
muro de ETABS puede ser cualquier polígono y con la otra forma se perdería su geometría en
cuanto no sea un rectángulo.

**Los niveles** se emparejan primero por nombre y, si no, **por cota** con 10 cm de
tolerancia: en ETABS el nivel se llama `Story1` y en Revit `PLANTA BAJA`. Los que no tengan
pareja **se crean**.

### Tres convenciones de ETABS que hay que deshacer

No son detalles de implementación: son las tres formas en que el modelo de cálculo dice una
cosa y Revit entiende otra. Cada una produjo un fallo con un síntoma que no se parecía a su
causa.

| En ETABS | Si se toma tal cual | Lo que hace el complemento |
|---|---|---|
| Una **viga** se inserta por *top center*: la línea que se exporta es la de su **cara de arriba** | La cadena de cerramiento queda un peralte más arriba y asoma por encima del muro | Justifica la trabe con `z Justification = Top` e `y Justification = Origin`, que es el **punto cardinal 8** |
| Un **área** pertenece a la planta de su **parte de arriba** | Un muro de planta baja queda atado a la planta primera y no sale en la vista de planta baja | Lo ata al nivel más cercano a su **base**, igual que ya se hacía con las columnas |
| La etiqueta de un muro es su **pier**, y un pier agrupa **varios** paños | Los trozos de un muro mallado comparten llave y solo se modela uno: la planta con más huecos se queda vacía | La etiqueta de un paño lleva **siempre** su posición; y si aun así dos llaves chocan, se desempatan en vez de descartar |

La equivalencia entre la justificación de Revit y el punto cardinal de ETABS no es una
suposición: es la que usa el propio exportador de IFC de Autodesk para traducir entre los dos
sistemas.

### Cómo reconoce sus propias piezas

Cada pieza creada lleva una marca en su parámetro **Comentarios**:
`CadLink|Columna|C1|Story1`.

- Va en *Comentarios* y no en *Marca* porque Revit avisa de marcas repetidas y llenaría la
  pantalla de advertencias.
- Lleva el **nivel** además de la etiqueta, porque las etiquetas de ETABS se repiten de un
  nivel a otro: la columna `C1` existe en todos los pisos. Sin el nivel, la segunda
  importación creería que todas son la misma pieza.
- **Lo que no lleve esa marca no se toca nunca.** Si alguien puso una viga a mano, se queda.

Las piezas que están en Revit con la marca y **ya no están en el modelo** se **informan y no
se borran**. Borrar en Revit sin que nadie lo pida no se puede deshacer de forma obvia, y una
pieza que "sobra" puede ser una que alguien renombró.

---

## Cómo está construido, y por qué

El complemento es el único proyecto de este repositorio que **no se puede compilar sin Revit
instalado**: necesita `RevitAPI.dll`, que viene con el programa, es de Windows y no se puede
redistribuir.

Así que se partió en dos:

```
CadLink.Revit.Nucleo   net8.0, sin dependencias   -> compila y se EJECUTA en cualquier maquina
    · leer el modelo                                 · 167 comprobaciones en tools/prueba-revit
    · inventariar secciones
    · emparejarlas con las familias
    · el plan de que crear y que actualizar
    · el comportamiento del cuadro

CadLink.Revit          net8.0-windows + RevitAPI  -> NO se puede compilar aqui
    · leer el documento y llenar los DTO del nucleo
    · ensenar la ventana
    · llamar a la API para crear las piezas
```

La regla que lo sostiene: **el núcleo no conoce ningún tipo de Revit.** Lo que sabe de Revit
llega como DTO de texto y números que el complemento rellena. `tools/validar.py` §26
comprueba que ningún archivo del núcleo tenga un `using Autodesk.*`, porque si esa frontera
se rompe se pierde la única parte comprobable del complemento y no hay error que lo delate.

Es la misma división que se hizo con `CadLink.Ifc`, y por el mismo motivo.

### Dos consecuencias prácticas de ese reparto

**La forma de un perfil se traduce como texto.** Revit sabe de verdad qué forma tiene un
perfil: `FamilySymbol.GetStructuralSection()` dice si es una I de patín ancho, un HSS
rectangular o un ángulo. El complemento pasa el **nombre** de esa forma como cadena y la
traducción ocurre en el núcleo. Así tiene pruebas, y así no se escribe en el código el nombre
de ningún miembro de un enum que cambia entre versiones de Revit: nombrar uno que no exista
en la versión instalada es un error de compilación, mientras que un texto que no coincide es
como mucho una forma que no se reconoce, y eso ya se sabe tratar.

**Las unidades se convierten en un solo sitio.** Revit trabaja por dentro en **pies
decimales**, siempre, sin importar lo que muestre el proyecto. Todo pasa por `Unidades.cs`, y
el validador comprueba que nadie más convierta. Es el error más fácil de cometer y el más
difícil de ver: un modelo metido en pies como si fueran metros sale con la escala
multiplicada por 3.28, y como todo queda proporcionado, en pantalla parece bien hasta que
alguien acota.

---

## Qué está comprobado y qué no

| Herramienta | Qué cubre | Dónde corre |
|---|---|---|
| `tools/prueba-revit` (`dotnet run`) | **167 comprobaciones**: el archivo de intercambio, el inventario, el emparejador, el mapeo y su archivo, el plan en los dos modos, el emparejamiento de niveles y el comportamiento del cuadro | **Cualquier máquina** |
| `tools/validar.py` §26 | **42 comprobaciones** de la frontera y del empaquetado: que el núcleo no toque la Revit API, que el complemento no entre en la solución principal, que el manifiesto cuadre con las clases, y los errores clásicos que hacen que un complemento no cargue | Cualquier máquina |

Dos bugs los encontraron esas pruebas y no el compilador: `CParallelFlange` —un canal— se
clasificaba como perfil I porque «parallelflange» aparece en los dos nombres, y el
emparejador se resolvió mirando la letra con que Revit prefija cada perfil.

### Sin probar

**Nada de la capa que llama a la Revit API se ha ejecutado nunca.** No hay Revit en el
entorno donde se escribió. Lo que más conviene revisar en la primera prueba:

- Que el complemento **cargue** y aparezca la pestaña.
- Que la **rotación de la sección** quede bien. Se escribe en
  `BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE`; si las vigas salen giradas, es ahí.
- Que `Wall.Create` y `Floor.Create` acepten los contornos de tus paños. Un paño no plano
  —cosa que pasa en mallas de ETABS— puede ser rechazado.
- Que las **columnas** no salgan giradas 90° en planta. Es el mismo cabo pendiente que en el
  IFC, y está explicado en [`docs/exportar-a-revit.md`](exportar-a-revit.md): el lector
  invierte `T2` y `T3` en las columnas y dos comentarios del repositorio se contradicen.

### Límites conocidos

- **Solo Revit 2025 y 2026** (.NET 8). Para 2024 o anterior hace falta otra compilación.
- **Cinco clases de pieza**: columnas, trabes, diagonales, muros y losas. Es todo lo que un
  modelo de ETABS entrega. No hay zapatas ni placas base, porque el lector no las trae.
- **No se exporta armado** ni cargas ni combinaciones.
- **Los muros cortina y apilados no se ofrecen**: no se pueden crear desde un contorno con un
  espesor, y ofrecerlos sería ofrecer algo que después falla.
- **Las secciones variables** salen con las medidas de un extremo.
- **No se borra nada.** Lo que sobra se informa.
