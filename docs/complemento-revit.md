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

### Cuatro convenciones de ETABS que hay que deshacer

No son detalles de implementación: son las formas en que el modelo de cálculo dice una cosa y
Revit entiende otra. Cada una produjo un fallo con un síntoma que no se parecía a su causa.

| En ETABS | Si se toma tal cual | Lo que hace el complemento |
|---|---|---|
| Una **viga** se inserta por *top center*: la línea que se exporta es la de su **cara de arriba** | La cadena de cerramiento queda un peralte más arriba y asoma por encima del muro | Justifica la trabe con `z Justification = Top` e `y Justification = Origin` —el **punto cardinal 8**— y después **mide la pieza** y la baja si aun así asoma |
| Un **área** pertenece a la planta de su **parte de arriba** | Un muro de planta baja queda atado a la planta primera y no sale en la vista de planta baja | Lo ata al nivel más cercano a su **base**, igual que ya se hacía con las columnas |
| La etiqueta de un muro es su **pier**, y un pier agrupa **varios** paños | Los trozos de un muro mallado comparten llave y solo se modela uno: la planta con más huecos se queda vacía | La etiqueta de un paño lleva **siempre** su posición; y si aun así dos llaves chocan, se desempatan en vez de descartar |
| La orientación de una sección es el **ángulo del eje local**, no un intercambio de medidas | Los castillos a plomo salen girados 90° | Rota la pieza como hace el plano de AutoCAD, sumando el cuarto de vuelta que corrige un tipo de Revit emparejado con las medidas al revés |

La equivalencia entre la justificación de Revit y el punto cardinal de ETABS no es una
suposición: es la que usa el propio exportador de IFC de Autodesk para traducir entre los dos
sistemas.

#### Por qué la trabe se mide y no solo se justifica

Pedir la justificación no basta, y fallaba de la peor manera: `get_Parameter` devuelve `null`
en una familia que no la expone, el `?.` se lo traga, y la trabe queda un peralte más arriba
**sin que nada lo diga**. Así que después de crearlas se mide la caja de cada trabe y se baja
la que asome. Medir es independiente de dónde tenga el origen la familia y de si el parámetro
se aplicó. Va en **una sola pasada** al final, con un único `Regenerate`: hacerlo pieza por
pieza en un modelo de cuatrocientas es inusable.

### Los niveles y la malla de ejes

**Los niveles se renombran** al exportar, porque `Story1` no sirve en un plano:

| Cota | Nombre |
|---|---|
| bajo cero | `Cimentacion` |
| 0 | `Planta baja +0.00` |
| 2.89 | `Nvl-01 + 2.89` |
| 5.78 | `Nvl-02 + 5.78` |

La numeración va por **cota**, no por el número que traiga el nombre de ETABS: ETABS lista las
plantas de arriba abajo, así que `Story3` puede estar debajo de `Story2`. El renombrado ocurre
sobre el modelo ya armado y arrastra **la planta que cita cada pieza**; renombrar solo el nivel
dejaría a las piezas pidiendo una planta que ya no existe, y esas piezas no se modelarían.

**La malla de ejes** se crea con el mismo criterio que el plano de AutoCAD: **los extremos a
paño y el interior al eje**. ETABS modela los muros por su línea media, así que el eje de
fachada pasa por el centro del muro; en un plano lo que se acota por fuera es la cara
exterior, de modo que el primer y el último eje de cada dirección se corren hacia fuera medio
espesor de la pieza más gruesa que corre a lo largo de ellos, con preferencia **muro, trabe,
apoyo**. Los ejes interiores no se mueven. Un eje cuyo nombre ya existe no se vuelve a crear,
para que reimportar no deje seis rejillas llamadas `1` una encima de otra.

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

## El armado (fase 1)

El botón **CadLink → Armar** pone **varillas nativas de Revit** (`Rebar`) en las trabes y
columnas que ya importó *Importar modelo*.

### Cómo se usa

1. En CadLink, llena la tabla de secciones de concreto y exporta con **Exportar a Revit
   (IFC)**. El armado viaja en el mismo `.cadlink-modelo.json` (versión 4).
2. En Revit: **Importar modelo**, si aún no están las piezas, y luego **Armar**, con el mismo
   archivo.

### Qué fila arma cada pieza

La sección de ETABS se empareja con la fila de la tabla:

1. **Por nombre**: la sección se llama como el ID de la fila, sin distinguir mayúsculas,
   espacios, guiones ni puntos (`T 01` = `T-01`).
2. **Por medidas**: si no, la **única** fila del mismo tipo con la misma base y peralte.
   Si hay dos, no se elige ninguna y se avisa: dos trabes de 30×60 con distinto armado no se
   distinguen por sus medidas.

Lo que no tiene fila se modela igual y se queda sin varillas; el informe lo dice por sección.

### Qué se arma

| | Trabe y contratrabe | Columna y dado |
|---|---|---|
| Corridas de arriba y abajo | Con gancho de 90° (12 db) en las dos puntas, hacia dentro | Rectas |
| Laterales | Rectas | Rectas |
| Bastones | Con su longitud real, su lecho y sus ganchos de 12 db | — |
| Estribos | Por zonas, en juegos de separación constante, gancho de 135° | Igual |

Las posiciones, los estribos y los tramos de los bastones **los calcula CadLink** con las
mismas funciones del dibujo de AutoCAD. Revit no vuelve a decidir nada. Si se cambia una regla
en CadLink, cambia en los dos sitios.

Los ganchos son tipos propios, `CadLink - 90° (12 db)` y `CadLink - 135° estribo (6 db)`, para
que midan lo del plano aunque la plantilla traiga otros. Los tipos de varilla se buscan por
nombre (`#4`) o por diámetro; si faltan, se crean, y el informe lo dice.

**Volver a armar rehace** el armado de CadLink de cada pieza (marca `CadLink|Armado|…` en
*Comentarios*). El armado puesto a mano no se toca. Todo va en una transacción: un Ctrl+Z.

### Límites de la fase 1

- Solo trabes **horizontales** y columnas **verticales**; las inclinadas se saltan y se avisa.
- Solo secciones **rectangulares**. Sin grapas, estribo diamante ni zuncho.
- Las varillas van de recubrimiento a recubrimiento de la línea de la pieza; no se calculan
  anclajes ni traslapes en los nudos.
- La pieza tiene que ser de **concreto**: Revit no arma otro material, y se avisa.

### Sin probar, y qué revisar primero

**Nada del armado se ha ejecutado en Revit.** La lógica tiene 38 comprobaciones en
`tools/prueba-revit` y la capa de Revit compila contra `tools/prueba-revit-compila`, pero los
recortes de la API de armado (`Recortes.Armado.cs`) salen de la documentación, no de la DLL.

En la primera prueba:

1. **Que compile**: si falla, la firma que no cuadra estará en `Recortes.Armado.cs`.
2. **Hacia dónde doblan los ganchos.** Es la suposición más delicada. Si salen hacia fuera de
   la pieza, se invierte **una línea**: `PlanDeArmado.Lado`.
3. **Que la columna no salga girada**: su base va por la `HandOrientation` de la familia.
4. **La cara de abajo de la trabe**: se toma de la caja del elemento.

---

## Armar por tipo: todas las piezas de un tipo, de un jalón

En Revit el armado se pone eligiendo una pieza ya dibujada. Con cien trabes iguales eso es
elegirla cien veces. El botón **CadLink → Armar por tipo** lo hace **una vez por tipo**, en
**cualquier proyecto** (no hace falta haber importado desde ETABS).

### Cómo se usa

1. En CadLink, hoja **Secciones Concreto**, pulsa **Armado para Revit…**. Guarda un
   `.cadlink-armado.json` con todas las trabes, contratrabes, columnas y dados rectangulares de la
   hoja, con sus varillas, estribos por zonas y bastones.
2. En Revit: **CadLink → Armar por tipo** y abre ese archivo.
3. Sale un renglón por cada **tipo** de columna estructural y de trabe (armazón estructural) que
   tiene piezas en el proyecto: familia, tipo, medidas, cuántas piezas tiene y cuántas son de
   concreto.
4. En **Sección de CadLink** eliges qué fila de la tabla lo arma. Ya viene sugerida la que se
   **llama** como el tipo o, si no, la única que **mide** lo mismo. Solo se ofrecen trabes para
   las trabes y columnas o dados para las columnas.
5. Marca los tipos que quieras (**Marcar todas** / **Desmarcar todas**) y pulsa **Armar**.

### Qué tipo de armadura de Revit lleva cada varilla

Abajo del cuadro hay una segunda tabla, **"Tipo de armadura de Revit con que se pone cada
varilla"**. Sale sola de las secciones marcadas: un renglón por pieza, uso y diámetro —*Corridas y
laterales #4 de trabes*, *Bastones #5 de trabes*, *Estribos #3 de columnas*…— y en cada uno eliges
el tipo de armadura del proyecto (`VAR #4C TRABES`, `VAR #4C COLUMNAS/CASTILLOS`, `VAR #5C BASTON`,
`VAR #3C ESTRIBOS TRABES/CADENAS`…).

- Ya viene **sugerido** el que mejor se llama: el número de la varilla exacto (el #2 no es el
  #2.5), la palabra de su uso (ESTRIBO, BASTON) y la de su pieza (TRABE/CADENA o
  COLUMNA/CASTILLO). Los de losas, muros, zapatas y grapas quedan al final.
- Lo que elijas **se recuerda** para la próxima vez, en `%LOCALAPPDATA%\CadLink\armaduras-revit.json`.
- *(automático: por diámetro)* hace lo de siempre: el tipo que se llame como la varilla (`#4`) o
  tenga su diámetro, y si no hay, lo crea.

Los botones **Armar** y **Armar por tipo** llevan su propio icono: la sección de una trabe con su
estribo y sus cuatro varillas.

### El despiece en Revit

Abajo del cuadro hay dos casillas, las dos encendidas:

- **Escribir propiedades de tipo.** En el tipo de Revit de cada sección armada se escriben:

  | Propiedad de tipo | Qué lleva | Ejemplo |
  |---|---|---|
  | Código de montaje | Las varillas, como en el rótulo de AutoCAD | `4 vars. #3C` |
  | Nota clave | Siempre | `CONCRETO` |
  | Modelo | Sus medidas | `15 X 30 CM` |
  | Descripción | El ID de CadLink | `T-04` |
  | Marca de tipo | El elemento | `TRABE`, `CASTILLO`, `COLUMNA`… |
  | Comentarios de tipo | El estribo | `Estr. #3C @15 cm` |

  Las **etiquetas** leen esas propiedades: para cambiar lo que dice el plano se editan las
  propiedades de tipo. Los textos son los mismos del rótulo de AutoCAD (`LineasDeRotulo`).
- **Crear el despiece.** Un **corte por sección** a escala 1:10 (a media longitud en la trabe,
  a media altura en la columna), con las llamadas de sus lechos (`2 vars. #3C`) a la izquierda y
  la **etiqueta** de la pieza debajo, acomodados en renglones en la hoja **"DESPIECE DE
  SECCIONES - CadLink"**. Si no caben, se crea otra hoja. La etiqueta es la de la categoría que
  tenga cargada el proyecto (armazón o pilar estructural); si no hay, el informe lo dice.
  Cada corte lleva sus **cotas**: la base arriba y el peralte a la derecha (a la izquierda van
  las llamadas), amarradas a los planos de referencia de la familia.

**La misma sección para todas las de su medida**: al elegir a mano la sección de un tipo, los
demás tipos de la misma clase y las mismas medidas (todas las 15x30, por ejemplo) toman la misma.
Los que ya elegiste a mano no se tocan.

Todo —armado, propiedades y despiece— va en la misma transacción: un Ctrl+Z.

Cada pieza se arma con **su longitud real**: los estribos por zonas y los bastones se calculan en
Revit con las mismas reglas del alzado de AutoCAD. El reparto de estribos es el **mismo archivo**
(`Estribos.cs`) compilado en el núcleo; los tramos de bastón siguen la regla de
`Bastones.Tramos`, comprobada igual en 16 000 casos.

### Corte de sección: un corte nuevo, con su nombre

El botón **Corte de sección** crea solo la vista —no rearma nada— de la sección que pidas:

1. Selecciona en Revit las trabes o columnas que quieres cortar (opcional). Sin selección, sale
   un renglón por cada tipo que ya tiene sección de CadLink, cortado en su primera pieza.
2. Marca el renglón, escribe el nombre del corte (se propone `Corte T-01 - 6.00m` o
   `Corte K-01`) y pulsa **Crear cortes**.
3. Se crea el corte 1:10 con llamadas, etiqueta y cotas, y se abre el primero. Si el nombre ya
   existe se le añade `(2)`, `(3)`…

La sección de cada tipo sale de su **Descripción** (la escribe *Armar por tipo*) o, si no, de
su nombre o medidas. Usa el último `.cadlink-armado.json` abierto; solo lo pide si no hay
ninguno.

### Lo que avisa el cuadro (columna Notas)

- **Ámbar**: la sección elegida no mide lo que el tipo de Revit. Se puede armar igual, pero las
  varillas se colocan con las medidas de la sección de CadLink.
- **Gris**: ninguna pieza del tipo es de concreto; Revit no deja armarlas y no se puede marcar.
- *"N ya armadas: se rehacen"*: volver a armar **rehace** el armado de CadLink de esas piezas
  (marca `CadLink|Armado|…`); el puesto a mano no se toca.

Todo va en una sola transacción: **un Ctrl+Z** deshace el armado de todos los tipos. Los
límites son los de la fase 1 (arriba): piezas horizontales o verticales y secciones
rectangulares.

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
