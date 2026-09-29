# Exportar el modelo a Revit, por IFC

El botón **«Exportar a Revit (IFC)»** de la pestaña *Dibujar planos estructurales* escribe
el modelo que se acaba de leer de ETABS o SAP2000 como un archivo `.ifc`, que es el formato
abierto que Revit sabe abrir y vincular. No hace falta tener Revit ni AutoCAD abiertos:
escribe un archivo y ya.

---

## Lo primero: qué hace y qué NO hace

Conviene decirlo antes de nada, porque es la parte que se malinterpreta.

**Lo que hace:** lleva a Revit la geometría con su clasificación estructural y, sobre todo,
**un tipo por cada sección del modelo, con el nombre de la sección**. Una columna `C 30x60`
llega a Revit como un elemento de tipo `C 30x60`, no como un sólido anónimo.

**Lo que NO hace:** asignar familias de Revit. Eso **no lo puede hacer un IFC**, y no es una
limitación de CadLink: es del formato. Un IFC describe qué es cada pieza y qué forma tiene;
no sabe qué familias tienes cargadas en tu plantilla ni cuál de ellas quieres usar. Elegir
la familia de cada sección es un paso que se hace **en Revit**, y el exportador está pensado
para que ese paso sea lo más corto posible:

- cada sección es **un tipo con su nombre**, así que la lista que hay que mapear es la de
  secciones del modelo, no la de piezas: veinte tipos en vez de tres mil columnas;
- cada tipo lleva su perfil con medidas (`IfcMaterialProfileSet`), así que al emparejarlo
  puedes comprobar que el peralte y el ancho cuadran;
- y al lado del `.ifc` se escribe un **`.secciones.csv`** con una fila por sección y dos
  columnas vacías, *Familia de Revit* y *Tipo de Revit*, para llenarlas y tener el mapeo
  por escrito antes de tocar el modelo.

---

## Cómo se usa

1. En la pestaña **Dibujar planos estructurales**, lee el modelo como siempre
   (*Leer modelo* / *Leer plantas*).
2. Pulsa **Exportar a Revit (IFC)**. También está en el menú *ETABS / SAP2000*.
3. Elige dónde guardar. Se proponen el nombre de la obra y la extensión `.ifc`.
4. Se escriben **dos** archivos:
   - `LaObra.ifc` — el modelo;
   - `LaObra.secciones.csv` — la tabla de secciones, que abre en Excel con doble clic.
5. En el recuadro de estado aparece el resumen: cuántas piezas, de cuántos niveles, cuántas
   secciones distintas, y los avisos de lo que no se pudo exportar bien.

### Ya en Revit

- **Vincular IFC** (*Insertar → Vincular IFC*) si lo quieres como referencia que se
  actualiza cuando vuelves a exportar. Es lo normal para revisar.
- **Abrir IFC** (*Archivo → Abrir → IFC*) si lo quieres como modelo editable.

Los niveles del modelo llegan como **niveles de Revit** con su cota, y cada pieza cuelga del
suyo. Los tipos aparecen con el nombre de la sección de ETABS, y ahí es donde se hace el
mapeo a tus familias.

> **Vuelve a exportar sin miedo a duplicar.** El identificador de cada pieza
> (`GlobalId`) se calcula a partir de su etiqueta, su clase y su nivel, no al azar. La
> segunda exportación del mismo modelo produce los mismos identificadores, así que una
> herramienta que compare por `GlobalId` puede emparejar en vez de duplicar. Y el archivo
> es byte a byte el mismo si el modelo no cambió.

---

## Qué se exporta, pieza por pieza

| En el modelo | En el IFC | Y en Revit se ve como |
|---|---|---|
| Columna | `IfcColumn` + `IfcColumnType` | Pilar estructural |
| Trabe, dala, cadena | `IfcBeam` + `IfcBeamType` | Estructura (viga) |
| Diagonal | `IfcMember` `.BRACE.` | Arriostre |
| Muro | `IfcWall` `.SHEAR.` + `IfcWallType` | Muro |
| Losa | `IfcSlab` `.FLOOR.` + `IfcSlabType` | Suelo |
| Nivel (*story*) | `IfcBuildingStorey` con su cota | Nivel |

### Las secciones, como perfiles de verdad

Cada forma va como el **perfil paramétrico** que le toca en el esquema, no como un contorno
de puntos. La diferencia importa: un contorno llega a Revit como un sólido cualquiera, y un
perfil llega con su peralte, su ancho, su alma y su patín.

| Forma en ETABS | Perfil en el IFC |
|---|---|
| Rectangular | `IfcRectangleProfileDef` |
| Circular | `IfcCircleProfileDef` |
| Tubo | `IfcCircleHollowProfileDef` |
| Cajón | `IfcRectangleHollowProfileDef` |
| Perfil I | `IfcIShapeProfileDef` |
| Canal | `IfcUShapeProfileDef` |
| Te | `IfcTShapeProfileDef` |
| Ángulo | `IfcLShapeProfileDef` |

Un canal va como **U** y no como `IfcCShapeProfileDef` a propósito: la C del esquema es el
perfil doblado en frío, de espesor constante y con los bordes vueltos hacia dentro, y no
tiene dónde poner un alma y un patín distintos, que es justo lo que trae una sección
laminada de ETABS.

Los muros y las losas no tienen perfil paramétrico —su geometría es el contorno de cada
paño— así que van como `IfcArbitraryClosedProfileDef` extruido por su espesor. Su tipo
lleva el espesor en el juego de propiedades.

### Las propiedades

Cada tipo lleva un juego llamado **`CadLink_Seccion`** con el nombre de la sección, su
forma, el ancho, el peralte, el material y los espesores que apliquen. En Revit sale entre
los parámetros del elemento, y sirve para filtrar y para tabular sin depender del nombre.

---

## Detalles que se resolvieron y conviene no «arreglar»

Van aquí porque las tres son cosas que parecen un error al leer el código.

**La extrusión va del extremo J al I.** Un `IfcAxis2Placement3D` es una terna derecha: dados
su eje Z y su X, la Y sale de Z × X. El perfil necesita su ancho sobre el eje local 3 y su
peralte sobre el 2, porque así lo define el esquema. Poniendo el eje Z = E1 y la referencia
= E3, la Y resulta E1 × E3 = **−E2**, o sea el perfil sale espejeado sobre el eje 2. En un
rectángulo, un círculo o una I no se nota; en un **ángulo, un canal o una te, sí**, y el
archivo abre igual de bien. La salida no es girar el perfil —un giro no arregla un espejo—
sino extruir al revés: eje Z = −E1 y referencia = E3, con lo que la Y da E2 y sigue siendo
terna derecha. La consecuencia es que la extrusión arranca en J y avanza hacia I.

**Los niveles se colocan a su cota, y las piezas relativas a ellos.** La otra opción
—niveles en cero y piezas en coordenadas absolutas— es igual de válida y más fácil, pero
entonces la cota del nivel en Revit y la altura real de las piezas salen de sitios
distintos, y en cuanto una no cuadra con la otra no hay forma de saber cuál está mal.

**Los acentos van escapados.** Una cadena de STEP admite ASCII y nada más; lo demás va en
una secuencia `\X2\...\X0\` con cada carácter en cuatro dígitos hexadecimales. Escribir la
Ñ en UTF-8 dentro del archivo no da error al exportar: da `CAÃ‘ÓN` en Revit.

---

## Lo que el exportador avisa

Nada de esto detiene la exportación; sale en el recuadro de estado.

- Una barra con los dos extremos en el mismo punto **no se exporta**.
- Un paño con menos de tres vértices distintos, o con todos en línea (área cero),
  **no se exporta**.
- Una sección sin ancho o sin peralte se exporta con **12 cm**, para que la pieza se vea y
  se pueda corregir en vez de quedar invisible.
- Un espesor imposible —un alma más gruesa que la sección— se **recorta**. No es cosmético:
  el esquema de IFC impone reglas («el patín será menor que medio peralte»), y un archivo
  que las incumple lo rechaza el lector **entero**, no solo esa pieza.
- Unos ejes locales que no forman terna válida, o que no van a lo largo de la pieza, se
  **rehacen** a partir de sus extremos. Si la sección no es simétrica, conviene revisar su
  giro.
- Un nivel que no está en la lista de niveles se resuelve **por cota**, colgando la pieza
  del más cercano, en vez de descartarla.

---

## Límites conocidos

- **La asignación de familias no es automática**, por lo dicho arriba. Es un paso en Revit.
- **Los tubos y cajones llevan su espesor de pared**, pero el resto de la aplicación (la
  vista previa y el 3D de AutoCAD) los dibuja macizos. Aquí sí salen huecos, así que el IFC
  y la vista previa no coinciden en eso. El IFC es el que está bien.
- **No se exporta armado.** Ni varillas, ni estribos, ni zunchos. Lo que va es la geometría
  estructural y sus secciones.
- **No se exportan cargas ni combinaciones.** El IFC estructural analítico es otra cosa y no
  está hecho.
- **Las secciones variables** (*non prismatic*) salen con las medidas que el lector consiguió
  leer, que son las de un extremo. No hay perfil variable.
- **Sin probar en Revit real.** Todo lo de aquí se comprobó contra el esquema y con pruebas
  automáticas, pero en el entorno donde se escribió no hay Revit. Lo primero que conviene
  hacer es vincular el `.ifc` y comparar contra el 3D que dibuja el propio CadLink.
- **Cabo pendiente, y es el que más vale la pena revisar:** para las **columnas** el lector
  invierte las medidas de CSI (`AnchoM = T3`, `PeralteM = T2`, al contrario que en una
  trabe), y los comentarios del repo no coinciden en qué eje corresponde a `t2` y a `t3`.
  El exportador pasa el par en el mismo orden en que lo consume `Perfil2D.De(...)`, que es
  lo que la aplicación ya usa para la vista extruida y para el 3D de AutoCAD, así que el
  IFC enseña **lo mismo** que la aplicación. Si estuviera al revés, estaría al revés en los
  tres sitios. El síntoma es muy concreto: **las columnas no cuadradas saldrían giradas 90°
  en planta**, y las cuadradas bien. Está anotado en `MainWindow.Ifc.cs`, y se corrige
  cambiando dos líneas.

---

## Cómo está comprobado

El exportador vive en `client/src/CadLink.Ifc`, que es **`net8.0` pelado y sin ninguna
dependencia**, no `net8.0-windows` como los otros cuatro proyectos. Eso no es un descuido:
es lo que permite compilarlo y **ejecutarlo en cualquier máquina**, también en la de Linux
donde se escribe este repositorio y sin acceso a internet. Los proyectos
`net8.0-windows` necesitan bajar su *ref pack*, y de ahí viene que casi todo el C# de aquí
se haya escrito sin poder compilarlo nunca.

Por eso el reparto es el que es: **toda la decisión** —qué entidad, qué perfil, dónde se
coloca— está en `CadLink.Ifc`, y lo que queda en `CadLink.App` (`MainWindow.Ifc.cs`) es
copiar campos, que es donde menos daño hace un error que no se puede compilar.

| Herramienta | Qué comprueba | Dónde corre |
|---|---|---|
| `tools/prueba-ifc` (`dotnet run`) | La **aritmética**: reconstruye la terna de cada colocación y comprueba que el sólido empieza y acaba donde dice el modelo. Más el `GlobalId` (ida y vuelta con 2000 UUID), el formato de los reales, el escapado de acentos, y los casos degenerados. | **Cualquier máquina** |
| `tools/verificar_ifc.py` | La **sintaxis y el esquema** sobre el archivo ya escrito: forma de cada entidad, que toda referencia exista, que el archivo sea ASCII, el esqueleto obligatorio, que cada pieza tenga nivel y tipo, los contornos cerrados, y las **cuentas de atributos**. | Cualquier máquina |
| `tools/validar.py` §25 | Que lo de alrededor no se rompa: que `CadLink.Ifc` siga siendo `net8.0` y sin dependencias, que el botón esté cableado, que el traductor cubra todas las formas y clases del lector, y que no vuelva la trampa de `params`. | Cualquier máquina |

### La tabla de cuentas de atributos

Un IFC mal formado no se queja. Si una entidad lleva un atributo de más o de menos, el
archivo se escribe igual y el visor lo abre **vacío**, sin ningún mensaje. Y contarlos mal
es fácil, porque casi todos son heredados: un `IfcColumn` tiene nueve atributos y solo
**uno** es propio; los otros ocho vienen de cinco entidades por encima. La cuenta no se
puede deducir mirando la documentación de `IfcColumn`.

Así que la tabla `CUENTAS` de `tools/verificar_ifc.py` no se escribió a mano: se resolvió
desde el **esquema EXPRESS oficial** de buildingSMART (`IFC4_ADD2_TC1.exp`), siguiendo la
cadena de herencia de cada entidad y descartando los atributos `INVERSE`, que no se
escriben en el archivo. Y `validar.py` §25 compara esa tabla contra las cuentas anotadas en
los comentarios del C# (`IfcColumn [9]`), para que las dos no se separen.

> Esa comprobación ya sirvió de algo dos veces: una, al descubrir que
> `IfcColumnType` tiene **10** atributos y no 8; y otra, cuando el verificador encontró que
> faltaban las tres relaciones `IfcRelAggregates` y el archivo, siendo válido, estaba
> **desconectado** —en Revit habría aparecido geometría sin niveles—.

*El esquema EXPRESS es de buildingSMART International y no se incluye en este repositorio;
se consultó desde su publicación en
[buildingSMART/IFC4.3.x-development](https://github.com/buildingSMART/IFC4.3.x-development)
(`reference_schemas/IFC4_ADD2_TC1.exp`). Aquí solo queda la tabla de cuentas resultante.*
