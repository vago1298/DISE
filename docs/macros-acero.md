# Las cuatro macros de acero, una por una

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
| Las cuatro familias | cuatro hojas y cuatro botones | una tabla y una columna de familia | cuatro bloques de columnas separados obligan a saberse dónde se captura cada cosa y dejan el 75 % de la fila en blanco |
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

## 6. Lo que falta

El catálogo del IMCA trae **499 perfiles de familias que el dibujante todavía no sabe
hacer**, y por eso quedan fuera:

| Familia | Cuántos | Qué haría falta |
|---|---|---|
| `WT` | 274 | te: medio perfil I, con el alma colgando de un solo patín |
| `L` | 144 | ángulo: dos alas en escuadra con radios de acuerdo |
| `OS` | 36 | redondo macizo: una circunferencia rellena |
| `C` | 31 | canal laminado: **no es el CF**, no lleva labios y su patín va en cuña |
| `ZF` | 14 | zeta formada en frío |

El más fácil es el `OS` —una circunferencia— y el que más se parece a algo que ya existe es
el `WT`, que es el IR partido por la mitad.
