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
- De cada hoja se cuentan las revisiones que **NO CUMPLEN** y se avisa; también se detectan las
  hojas que todavía traen **los datos de ejemplo** del libro, que salen sin palomita.

## Cómo sale en AutoCAD

- **Aparte del resto del dibujo**: el primer detalle termina en X = -25 y los demás siguen hacia la
  izquierda, en renglones de tres y hacia abajo.
- **Las soldaduras se llaman con la simbología de soldadura de CadLink** (la misma del cuadro de
  la pestaña Conexiones/Detalles): flecha al cordón, tamaño, símbolo (filete, filete en ambos lados,
  todo alrededor o bisel para la penetración completa) y el electrodo en la cola.
- **El cordón se rellena con el hatch de la soldadura de la placa base** (patrón y escala de su
  perfil en *Estilo de dibujo*), sobre su contorno; no es una polilínea con ancho.
- Los textos son MTEXT de **0.07** de alto con su punto de justificación; el cuadro de datos y las
  notas van cada uno en un solo MTEXT justificado. Se cambian en *Estilo de dibujo*, perfil
  «Conexiones de acero».
- Las cotas dicen los centímetros con **dos decimales**.

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
