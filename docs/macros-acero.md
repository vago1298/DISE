# Las cuatro macros de acero, una por una

> Los apartados 1 a 5 son la revisión de las **cuatro macros originales** —IR, HSS, OC y CF—.
> Del 6 en adelante está lo que el port añadió por encima de ellas: las cinco formas que no
> tenían macro, la separación entre familia y forma, y el aparato de cota proporcional.

Auditoría de `SECCIONES DE IR V2`, `SECCIONES OR V2`, `SECCIONES OC` y `SECCIONES CF V1`
frente al port de CadLink (`SeccionDrawer.Acero.cs` y `MainWindow.Acero.cs`).

Sirve para dos cosas: dejar claro **qué hace cada una** y no perder de vista **en qué se
diferencian**, que es donde estaban los detalles que al port le faltaban.

---

## 1. Qué hace cada macro

Las cuatro siguen el mismo guion —conectar con AutoCAD, crear capas y estilos, recorrer la
hoja de cuatro en cuatro filas, dibujar, acotar, rotular y agrupar en bloque— y se
diferencian en la forma que dibujan y en los números con los que la acomodan.

| | **IR** (`W`) | **OR** (`HSS`) | **OC** (`PIPE`) | **CF** |
|---|---|---|---|---|
| Forma | perfil I de 12 vértices | rectángulo redondeado doble | dos circunferencias | canal con labios y 8 dobleces |
| Columnas de la hoja | D, E, F, G | L, M, N | AJ, AL | T, U, V, W |
| Rayado | `ANSI32` 0.0009 color 252 | según peralte, ver abajo | `SOLID` 162 + `ANSI31` 0.002 color 162 | `SOLID` 4 + `ANSI31` 0.0008 color 142 |
| Cotas | patín, peralte, e patín, e alma | ancho, peralte, e pared | diámetro + texto `e=…` | peralte, patín, labio, e alma |
| `baseY` | 0 | 2.0 | 5.0 | 3.5 |
| `sepIzq` | 0.45 | 0.55 | 0.60 | 0.65 |
| `gap` de cotas | 0.06 | 0.06 | 0.06 | **0.08** |
| Alto del rótulo | 0.03 | 0.02 o 0.03 | 0.02 | 0.022 |
| Ancho del MText | 0.7 | 0.7 | **2.5** | 0.7 |
| Y del rótulo | `baseY − 0.06` | `baseY − 0.06` | `baseY − 0.06` | `baseY − 0.05` |
| Nombre del bloque | el perfil (col. A) | el perfil (col. I) | el nombre (col. AG) | `CF_` + nombre |
| Salto de renglón | `vbCrLf` | `\P` | `\P` | `vbCrLf` |

Particularidades de una sola macro:

- **OR** es la única que **decide el rayado por el peralte**: por debajo de 5″ raya fino
  (`ANSI31` 0.001, color 142) con **fondo cian**, y de ahí para arriba rellena sólido
  (color 141) con `ANSI31` 0.002 color 144 encima. También es la única que **ordena `b` y
  `h`** para que el peralte sea el lado mayor, y la única que pinta el **fondo** de un
  rayado, con un objeto `AcCmColor` que pide por `GetInterfaceObject` probando versiones de
  AutoCAD de la 26 hacia abajo.
- **IR** es la única que le pone **ancho constante** a la polilínea (`ConstantWidth` 0.001,
  el `PEDIT > Width` de toda la vida) y la única cuyo rótulo lleva **clasificación de
  viga** («VIGA PRINCIPAL "V-1"»).
- **OC** es la única que escribe el espesor como **texto** (`e=0.602 cm`) en lugar de como
  cota: un tubo redondo no tiene dos caras paralelas que acotar de frente.
- **CF** es la única que **dibuja el contorno en piezas** —24 líneas y arcos que después une
  con `JoinEntities`— y además construye **otra** polilínea con bulges para el rayado. Las
  otras tres dibujan una sola polilínea.

---

## 2. Qué se repite, literalmente, en las cuatro

Esto es el bulto de las cuatro macros, y es idéntico:

| Rutina | Qué hace |
|---|---|
| `SanitizarNombreBloque` | quita `< > / \ " : ; ? * \| = \` ,` y cambia espacios por `_` |
| `CrearBloqueDesdeObjetos` | copia los objetos a la definición, borra los originales, inserta la referencia |
| `BloqueExiste` | `Blocks.Item(nombre)` con `On Error` |
| `FormatearCota` | estilo `COTA_ESTRUCTURAL`, capa `COTAS`, color 256, `TextStyle` **ACERO**, `TextHeight` 0.015, `LinearScaleFactor` **100** |
| `AplicarDimStyle` | pone `ActiveDimStyle` **si existe** |
| `AsegurarCapaCotas` / `AsegurarCapaRotulos` | crean la capa; el color solo si es nueva |
| `AsegurarTextStyleAcero` | crea el estilo de texto `ACERO` |
| El preámbulo | `GetObject` / `CreateObject`, `Visible`, `ModelSpace` |
| El recorrido | `fila = 10`, de cuatro en cuatro; ID en `fila−1`, acero en `fila+1`, info en `fila+2` |
| El cierre | `ZoomExtents` y un `MsgBox` con el conteo |

En el port todo eso existe **una sola vez**, compartido con las secciones de concreto:
`Hatch`, `FormatearCota`, `Bloquear`, `Capa`, `AsegurarEstiloTexto`, `ConfigurarCotas`. Por
eso `SeccionDrawer.Acero.cs` es un archivo parcial de la misma clase y no una clase nueva.

---

## 3. Lo que las macros no hacen y hay que hacer

Tres cosas que salieron de esta revisión:

**El estilo de texto `ACERO` no estaba en el port.** Las cuatro macros lo crean y se lo
ponen a cada cota (`dimObj.TextStyle = "ACERO"`), que es distinto del de los rótulos
(`SECCIONES`). El port usaba el de los rótulos para todo. Ya se crea, en
`AsegurarEstiloAcero`, y cada cota de acero lo lleva junto con su altura de 0.015.

**Las cuatro se contradicen en cómo es ese estilo:**

| Macro | Fuente | Altura |
|---|---|---|
| IR (V2) | `BAHNSCHRIFT SEMILIGHT` | 0.015 |
| CF, OC, OR | `arial.ttf` | 0 |

Se toma la **fuente de la IR**, que es la única en V2 y la que coincide con los rótulos y
con el concreto: si no, el mismo plano tendría dos tipografías en las cotas según de qué
perfil sea cada una. Y se toma la **altura de las otras tres**, el 0, porque hace falta: un
estilo con altura fija manda sobre la del texto, así que con el 0.015 en el estilo ninguna
cota podría fijar la suya… y las cuatro macros, la IR incluida, se la fijan una por una.
La IR se contradice a sí misma; con altura 0 las dos cosas encajan.

**Ninguna macro crea `COTA_ESTRUCTURAL` ni `SECCIONES`.** Solo los aplican, con
`On Error Resume Next`. Si el dibujo no los trae —un `.dwg` en blanco, por ejemplo— las
cotas salen con el estilo que estuviera activo y el `mtxt.styleName = "SECCIONES"` falla en
silencio. El port los crea los dos.

---

## 4. Cosas que conviene mirar

Salieron de comparar las cuatro entre sí. No están «arregladas» por cuenta propia porque son
decisiones de dibujo, no errores de programación:

**El rayado del OC es invisible.** `AplicarHatchOC` pone el relleno sólido en color 162 y
encima el `ANSI31` **también en 162**. Al ser el mismo color, las líneas del rayado no se
distinguen del relleno: el tubo se ve macizo. Las otras tres familias usan dos colores
distintos —el CF, por ejemplo, fondo 4 y líneas 142—. El port lo deja como está la macro;
si se quiere que el rayado se vea, basta cambiar el color de las líneas.

**`FormatearPerfilIR` cambia todas las W, no la primera.** Usa
`Replace(s, "W", "IR", 1, -1, vbTextCompare)`, así que un perfil llamado `W12X30 WELDED`
saldría rotulado `IR12X30 IRELDED`. El port solo traduce el prefijo.

**El nombre del bloque es el del perfil, no el del elemento.** Dos vigas distintas con el
mismo perfil chocan, y la macro lo resuelve numerando: `IR_305X38_1`, `_2`… En el port el
bloque se llama como el **ID** de la sección, igual que en el concreto, porque es lo que
permite saltarse las que ya existen y rehacerlas en su sitio.

**`sepDoble` está muerto en el CF.** `AgregarCotasCF` lo recibe y lo suma, pero quien la
llama le pasa siempre `0`.

**Los comentarios del OR no coinciden con sus constantes.** Dicen «SOLID color 94» y
«ANSI31 color 80», pero las constantes valen 141 y 144. Manda la constante, que es lo que se
ejecuta, y es lo que se portó.

---

## 5. Lo que el port hace distinto, y por qué

| | Macros | Port | Motivo |
|---|---|---|---|
| Las familias | cuatro hojas y cuatro botones | **doce** familias en una tabla, con una columna de familia | cuatro bloques de columnas separados obligan a saberse dónde se captura cada cosa y dejan el 75 % de la fila en blanco |
| Familia y forma | lo mismo: una macro por familia | dos cosas distintas: doce familias, nueve formas | cuatro familias se dibujan igual pero tienen que ser cuatro listas separadas. Ver el apartado 7 |
| Aparato de la cota | flecha 2 cm, texto 1.5, fijos | proporcional al peralte, con topes | el catálogo va de 0.64 cm a 1.90 m. Ver el apartado 8 |
| Grosor del contorno | solo el IR hace `PEDIT > Width` | **las nueve formas** llevan 1 mm de ancho constante | es lo que hace que una sección se lea como acero y no como una línea de construcción. Tenerlo en una de cuatro es una inconsistencia, no una decisión |
| Dimensiones | se teclean | salen del catálogo IMCA al elegir el perfil | cuatro números por fila son cuatro oportunidades de equivocarse, y un espesor mal escrito no se ve en el dibujo |
| Contorno del CF | 24 líneas y arcos unidos, más otra polilínea igual para el rayado | **una** polilínea con bulges | dos entidades con la misma geometría, una encima de otra |
| Perfil espejeado | se calcula con el signo `s` | igual, pero el bulge sale del **barrido real** | al invertir el lado, los ocho barridos cambian de signo solos |
| `LinearScaleFactor` | `100` fijo | `1 / escala` | el mismo número dibujando en metros, pero sigue valiendo a otra escala |
| Bloque repetido | se numera `_1`, `_2` | se **salta**, o se rehace en su sitio | no deshacer el acomodo de un plano ya armado |
| Orden de dibujo | — | se avisa como nota, no como fallo | reordenar es estético: el dibujo está completo aunque falle |

Lo que **sí** se conserva tal cual, porque son decisiones del dibujo y no del programa: la
geometría de las cuatro formas, los patrones y colores de rayado, el corte de las 5″ del OR,
qué cotas lleva cada familia, la altura del rótulo, y el acomodo — `x = −0.6` hacia la
izquierda, cada familia en su banda de `baseY` y con su propio `sepIzq`.

---

## 6. Las cinco formas que no tenían macro

> **Se pueden ver sin abrir AutoCAD:** [`formas-acero.svg`](formas-acero.svg) dibuja las
> nueve formas a la misma escala y con el color de cada familia. Lo genera
> `tools/vista_formas_acero.py` con la misma geometría que se verifica, así que si las
> comprobaciones fallan la imagen no se genera.

El catálogo del IMCA traía **499 perfiles de familias que el dibujante no sabía hacer**, y
quedaban fuera. Ya están las cinco:

| Familia | Cuántos | Forma | Cómo se dibuja |
|---|---|---|---|
| `WT` | 274 | te | ocho vértices, patín arriba y alma colgando. El peralte es el **total** (`d`), no la `h` del alma libre |
| `L` | 143 | ángulo | seis vértices con el talón abajo. **En pico**: el manual no da ningún radio |
| `OS` | 36 | redondo macizo | una circunferencia rellena |
| `C` | 31 | canal laminada | ocho vértices. **No es el CF**: sin labios, y con alma y patines de distinto espesor |
| `ZF` | 14 | zeta | doce vértices y cuatro dobleces, con los **dos patines de distinto ancho** |

Tres cosas de esas cinco merecen explicación:

**El ángulo no trae ninguna medida en la hoja.** Las 143 filas de la familia `L` tienen
**todas** las columnas de geometría en `-`: solo hay peso, área, gramil y `J`. Sus medidas
están únicamente en la designación —`L - 3'' x 2'' x 1/4''`— así que hay que leerlas de ahí,
en pulgadas, y distinguir las de alas iguales (dos números) de las desiguales (tres). Por lo
mismo se dibuja en pico: inventarle un radio de acuerdo sería dibujar un dato que nadie dio.

**El redondo macizo trae su diámetro en dos columnas y en dos unidades.** La columna 2 —la
`d` con la que vienen los perfiles I— en esta familia está en **centímetros**: dice `0.638`
para el de 1/4″. Se usa la columna 6, que está en milímetros como todas las demás. Y es la
única forma que **no lleva ninguna cota**: un redondo tiene una sola dimensión, y para casi
todo el catálogo —de 1/4″ a 4″— el aparato de una cota mide más que la varilla. Va como
texto, con el símbolo Ø.

**La zeta tiene los dos patines de distinto ancho, y no es una errata.** 60.3 y 54 mm en la
de 2 3/8″, y los dos valores son fijos para todos los calibres: es lo que permite traslapar
dos zetas en el apoyo, porque el patín angosto de una entra dentro del ancho de la otra. Por
eso el CSV tiene una novena columna, `ancho2`, que solo usa esta familia, y por eso el hueco
que ocupa una zeta en la fila **no es su patín**: es la suma de los dos menos el alma que
comparten.

Y una cosa que la verificación numérica encontró y el ojo no habría visto: en la zeta, los
dos dobleces interiores tienen el centro del arco a **distinto lado del alma** —el de arriba
a la derecha, el de abajo a la izquierda— porque los dos patines salen a lados contrarios.
Con los dos al mismo lado, que es como estaban, el contorno de abajo se devolvía sobre sí
mismo: las líneas quedaban donde tenían que estar, así que en la pantalla el perfil se veía
bien, pero AutoCAD rellena un polígono cruzado por paridad y el rayado salía por fuera del
acero. Lo cazó la prueba de que el contorno no se corta consigo mismo, en
`tools/verificar_perfiles_acero.py`.

---

## 7. Familia y forma son dos cosas distintas

Doce familias, nueve formas. Cuatro familias —`IR`, `IS`, `IC` y `S`— se dibujan **igual**,
porque las cuatro son un alma con dos patines, y aun así tienen que ser cuatro listas
separadas: quien pide una IR quiere ver **solo** las W. Antes las cuatro se metían en `IR`
«porque son perfiles I», y el desplegable de la IR ofrecía 573 perfiles de cuatro
nomenclaturas revueltas, en el que había que ir sorteando IS, IC y S para encontrar una W.

Separadas, lo único que las distingue en el plano es el **color**, y por eso cada familia
tiene el suyo y una capa propia —`PERFILES-IR`, `PERFILES-IS`…—. El color va en la capa y los
objetos van «por capa», que es como se hace en AutoCAD: así se puede apagar una familia
entera de un clic, recolorearla o dejarla fuera de la impresión. La `PERFILES` a secas se
sigue creando porque es la de las macros.

Eso además arregla el **rayado invisible del OC** que está apuntado más arriba: el relleno
macizo va seis pasos más oscuro que el rayado dentro del mismo tono, así que el rayado se lee.

---

## 8. El aparato de la cota, ahora proporcional al perfil

Las macros dejaban la flecha en 2 cm, las líneas de extensión en 3.5 y el texto en 1.5. Esos
números vienen del concreto, donde una sección es de 30 por 60 y son un 5 % de la pieza. El
catálogo de acero va de un redondo de **0.64 cm** a una IS de **190**: con el aparato fijo,
una flecha de 2 cm sobre un ángulo de 1.9 cm es más grande que el perfil y la cota tapa lo
que pretende medir.

Ahora sale del peralte, con topes arriba y abajo, y está puesto para que **un perfil de 30 cm
salga exactamente como antes**: 30 entre 15 son los 2 cm de flecha de siempre. Lo que cambia
es que de ahí para abajo el aparato encoge con la pieza.

La separación del rayado también, y esa no era cosmética: con el `0.0009` fijo de la macro
del IR, una IS de 1.90 m se rayaba con **más de dos mil líneas**, y a eso AutoCAD contesta
«el patrón de sombreado es demasiado denso» y no dibuja nada. Ligada al peralte, cada perfil
lleva del orden de trescientas.

Y la altura del rótulo sale de la única de las cuatro reglas de las macros que tenía un
motivo: la del OR, que ponía 0.02 si su primer número no pasaba de 6 y 0.03 si sí, porque el
rótulo se centra bajo el perfil y en uno chico un texto grande sobresale por los lados.
Generalizada al peralte, da los mismos números donde ellas los daban. El **ancho de la caja**
ya no es fijo: se calcula del renglón más largo, porque hay nombres de cuarenta y seis
caracteres —`IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm`— que en la caja de 0.7 m de las macros
se partían en tres renglones. La macro del OC ya la subía a 2.5 a mano «para que el renglón
del perfil no se parta en dos»: era el mismo problema, parcheado en una de las cuatro.

Y de ahí sale otra cosa: **el aire entre secciones lo manda el rótulo**, no el perfil. Un
renglón de casi un metro debajo de una sección de 22 cm significa que dos secciones seguidas
pueden quedar separadas y sus rótulos pisarse. El hueco es el mayor de los dos.

---

## 9. Lo que sigue faltando

Nada del catálogo IMCA: las doce familias de la hoja se dibujan. Lo que queda son cosas de
detalle que el manual no da o que a escala de plano no se verían:

- El **acuerdo entre alma y patín** de las formas laminadas (I, te, canal). Las macros
  tampoco lo dibujan.
- La **cuña del patín** de la canal laminada y de la S. El manual da un solo `tf`, que es el
  espesor medio.
- Los **radios del ángulo**, que la hoja no trae.
