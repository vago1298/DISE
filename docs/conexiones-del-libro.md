# Conexiones de acero desde el libro de Excel

CadLink lee el libro de revisiones de acero (NTC-Acero 2023, el `.xlsm` con las hojas UNION
VIGA-COLUMNA, PLACA GUSSET, CONTRAVENTEO EXCENTRICO, etc.) y con las medidas de cada hoja:

- **dibuja el detalle en AutoCAD**, desde la pestaña *Conexiones/Detalles* de CadLink;
- **modela la conexión en Revit**, con el botón *Conexiones del libro* del panel *Detalles* de la
  cinta CadLink.

Las dos salen de las mismas piezas, armadas una sola vez en el núcleo
(`client/src/CadLink.Revit.Nucleo/Conexiones/`): el plano y el modelo no pueden decir cosas
distintas.

## Qué hojas se leen

| Hoja del libro | Qué se arma | En Revit se elige |
|---|---|---|
| UNION VIGA-COLUMNA | Placas de patín (soldadas o atornilladas), placa de cortante, placas de continuidad y placa adosada | Clic en la viga, cerca del extremo |
| CONEXION PATINES SOLDADOS | Patines y alma a penetración, placa de cortante, continuidad | Clic en la viga, cerca del extremo |
| CONEXION RBS | Lo mismo, y el recorte del patín (solo en AutoCAD) | Clic en la viga, cerca del extremo |
| CONEXION PLACA EXTREMO | Placa de extremo 4E, 4ES u 8ES con sus tornillos y atiesadores | Clic en la viga, cerca del extremo |
| CONEXION CORTANTE | Placa al alma de la viga que soporta, soldada o atornillada a la soportada | Clic en la viga soportada, cerca del extremo |
| MOMENTO VIGA-VIGA SOLDADA | Empalme, continuidad sobre trabe o cruce sin placas | Clic en la viga, cerca del extremo |
| PLACA GUSSET | La placa de esquina, sus filetes a viga y columna y los cordones de la diagonal | Clic en la diagonal, cerca de la esquina |
| CONTRAVENTEO EXCENTRICO | Atiesadores del eslabón y las placas de las dos diagonales | La viga del eslabón |
| CONTRAVENTEO X (un entrepiso) | Placa del cruce (o cubreplacas del empalme) y placas de refuerzo | Una de las diagonales |
| PLACA BASE | Con el detalle de la hoja Placa Base de CadLink | Las columnas |

Las copias de las hojas (`PLACA GUSSET 1`, `PLACA GUSSET (2)`) se reconocen solas. Una hoja
renombrada se reconoce por su título.

Todavía **no** se dibujan: CONEXION DIAFRAGMAS, CONTRAVENTEO V-K, CONTRAVENTEO (la diagonal),
ATIESADORES y la X en dos niveles. Salen en la lista con el motivo.

## Cómo se lee

- Sin Excel: el `.xlsm` es un ZIP con XML y se abre con lo que trae .NET. Se puede leer aunque el
  libro esté abierto en Excel.
- **Por etiqueta, no por fila.** Cada dato se busca por el texto de la columna B
  («Grueso de la placa superior, tp,s =») en el orden de la hoja, y se lee su valor de la columna C.
  Así no importa que la hoja crezca o que se inserten filas. La tabla de etiquetas se genera con
  `python3 tools/generar_etiquetas_conexiones.py` desde `tools/datos/etiquetas-libro-acero.json`.
- Se leen los **resultados guardados** de las fórmulas. Si el libro se modificó fuera de Excel y
  pide recalcular, CadLink lo avisa: ábrelo en Excel, guárdalo y vuelve a leerlo.
- De cada hoja se cuentan las revisiones que **NO CUMPLEN** y se dice **cuáles** (su texto de la
  columna B). Un error de Excel (`#DIV/0!`, `#N/A`…) en la columna D se cuenta aparte, y «-» o
  «N/A» cuentan como que cumple. También se detectan las hojas que todavía traen **los datos de
  ejemplo** del libro, que salen sin palomita.
- **Si en Excel una hoja cumple y CadLink dice que no**: CadLink lee lo que Excel GUARDÓ la última
  vez. Si el archivo se cambió fuera de Excel, o se guardó sin recalcular, esos valores están
  viejos. Ábrelo en Excel, guárdalo y pulsa «Volver a leer».

## Cómo sale en AutoCAD

- **Aparte del resto del dibujo**: el primer detalle termina en X = -25 y los demás siguen hacia la
  izquierda. **Un renglón por tipo de hoja** (todas las viga-columna juntas, luego las placas
  gusset…), hasta cuatro por renglón, y hacia abajo.
- **Las soldaduras se llaman con la simbología de soldadura de CadLink** (la misma del cuadro de
  la pestaña Conexiones/Detalles): flecha al cordón, tamaño, símbolo (filete, filete en ambos lados,
  todo alrededor o bisel para la penetración completa) y el electrodo en la cola.
- **El cordón se rellena con SOLID de color 9 y 70 de transparencia** sobre su contorno
  (`soldadura.hatch`, `soldadura.color`, `soldadura.transparencia`).
- **Capas de perfiles:** los principales (columna, trabe, la viga que soporta) van en
  `CONEXION PERFIL` (color 7) y los secundarios (la viga que llega, las diagonales) en
  `CONEXION PERFIL SECUNDARIO` (color 51). Todas las líneas de perfil llevan ancho global
  (PEDIT Width) de **0.001** (`perfil.ancho`). El eje va en naranja (color 30) para no
  confundirse con las cotas; una capa `CONEXION EJE` que se quedó en rojo pasa sola al nuevo color.
- **Perfiles con sus medidas reales:** lo que una hoja no trae (la conexión a cortante no da el
  patín de la viga que soporta) se completa con la hoja **IMCA5** del mismo libro. En la conexión
  a cortante se acota la holgura g0 entre la viga soportada y el alma de la que soporta.
- **Break line** en los extremos cortados de vigas, columnas y diagonales, para que se lea que la
  estructura sigue.
- **TRIM bajo los tubos:** en el contraventeo en X (y la gusset), la orilla de la placa que queda
  dentro de la ranura de la diagonal no se dibuja.
- **Textos en la capa ROTULOS** (verde, color 3), como el resto del rotulado de CadLink, y con
  color por capa: se imprimen en negro con el mismo mecanismo del layout (estilo de trazado
  «Negro» en dibujos `.stb`, que se le asigna al crear la capa, u override de color en el
  viewport). Todas las especificaciones empiezan con mayúscula.
- **El despatinado se recorta de la viga:** en la conexión a cortante el patín superior y el alma
  se cortan en escalón (dce × c), en elevación y en planta, en lugar de un rectángulo encima.
- Los textos son MTEXT de **0.09** de alto. Las llamadas se reparten en dos columnas, a la
  izquierda y a la derecha de cada vista. Las que señalan el mismo nudo van **del mismo lado**, y
  lo que ya se señaló en una vista no se repite en la siguiente. Una llamada de más de 32 letras
  (`llamada.letras`) se parte en dos renglones.
- La simbología de soldadura tiene su propio tamaño (**0.04**, `alto.simbolo`) y su flecha mide
  **0.025** (`flecha.largo`); el punto de las llamadas de texto, 0.006 (`llamada.punto`).
- Los datos y las notas van en **tablas** con marco, centradas bajo las vistas: dato | valor (con
  muchos datos, en dos columnas). Las vistas se centran a lo alto entre ellas.
- Entre vistas y entre detalles hay **0.25** (`separacion.detalles`). Todo se cambia en *Estilo de
  dibujo*, perfil «Conexiones de acero».
- Las cotas dicen los centímetros con **dos decimales**.

## Capturar a mano, sin el libro

En *Conexiones/Detalles* → *Capturar a mano* se elige el tipo de conexión, se agrega y se capturan
los mismos datos de su hoja del libro (los que el detalle usa). Los perfiles y aceros se eligen de
los catálogos de CadLink. Lo que el libro calcula con sus fórmulas (medidas del perfil, `k` ≈ tf +
tw, pared de diseño del tubo = 0.93 de la nominal, ángulo de la diagonal, tipo de eslabón) se
completa con el catálogo (`ConexionesAMano` del núcleo). No se revisa el diseño: eso lo hace el
libro. Los datos que CadLink calcula si se dejan vacíos dicen «vacío = lo calcula CadLink». Abajo
hay una **vista previa**: es el mismo dibujante de AutoCAD en su modo de vista previa
(`ConexionAceroDrawer.ParaVistaPrevia`), así que lo que se ve es lo que se dibuja. Las
conexiones capturadas se guardan con el proyecto (`.clk`) y se dibujan igual que las del libro,
con una tabla de datos armada con lo capturado. La subpestaña *Desde el libro de Excel* sigue igual.

## Si cambias el libro

- **Cambiar un valor**: guarda el libro en Excel y pulsa «Volver a leer»; el detalle sale con el
  valor nuevo.
- **Duplicar una hoja** (Excel la llama «PLACA GUSSET (2)», o tú «PLACA GUSSET 4»): aparece como
  otra conexión en la lista, con sus propios valores.
- **Insertar o borrar filas, o mover la hoja de lugar**: no pasa nada, porque cada dato se busca
  por su etiqueta.
- **Cambiar el texto de una etiqueta de la columna B, o mover un valor fuera de la columna C**: ese
  dato ya no se encuentra. No se adivina: se dibuja con cero y la conexión trae una nota que dice
  qué datos faltaron.

## Lo que la hoja no dice y se resuelve con una regla

Se escribe en las notas del detalle para que el dibujante lo revise:

- **Placa gusset y placas de diagonal**: el contorno es la envolvente de sus lados (Lgc, Lgb o
  Lgi) y del extremo de la diagonal con su ranura. El tubo empieza donde sus esquinas libran la
  viga y la columna por max(a, 2tp, 2.5 cm); la placa sobresale 1 cm más allá de la ranura.
- **Placas de refuerzo de la X**: de Lw de largo, desde la mitad de la ranura.
- **Placa de cortante de la RBS**: hasta la línea de tornillos más su distancia al borde.
- **Recorte RBS**: se dibuja en AutoCAD; en Revit hay que recortar la viga.

## En Revit

- Cada pieza es una forma directa (DirectShape) de *Conexiones estructurales*: las placas
  extruidas de su contorno, los cordones como prismas triangulares, los tornillos con cabeza y
  tuerca. Los miembros (viga, columna, diagonal) ya están modelados y no se tocan.
- Volver a poner la conexión en la misma pieza la **cambia**, no la duplica.
- Unión a columna: la cara de la columna de acero que está enfrente es la Z = 0; si no hay una a
  menos de 15 cm, se usa la holgura de la hoja.
- Placa gusset: el punto de trabajo es el extremo de la diagonal; si el ángulo del modelo difiere
  más de 2° del de la hoja, se avisa.

## Comprobación

```
cd tools/prueba-conexiones
dotnet run                                   # el lector y las piezas, con libros armados en la prueba
dotnet run -- ruta/al/libro.xlsm salida/     # ademas, un libro de verdad y sus vistas en JSON
```

`tools/validar.py`, grupo [32], vigila que el núcleo no dependa de Revit ni de AutoCAD, que la
tabla de etiquetas esté al día y que cada pieza esté conectada. La capa de la Revit API se compila
contra los recortes de `tools/prueba-revit-compila`.

**Sin probar todavía**: el dibujo en AutoCAD y el modelado en Revit no se han ejecutado en esos
programas (no están en la máquina donde se escribió). Lo primero que hay que revisar es que las
placas queden del lado correcto de la viga y que las cotas salgan legibles.
