# Plan del port: macro PLANOS ESTRUCTURALES (ETABS/SAP2000 → AutoCAD)

Lo que se pidió: **borrar el dibujo de plantas que hay hoy y dejar solo la macro
del usuario**, mandando ella en capas, opciones y todo lo demás, y que **lea
igual de SAP2000 cambiando de opción en una casilla**.

El análisis de la macro está en `macro-plantas-etabs.md`. Este archivo es solo el
orden de trabajo.

## Lo que ya está hecho

- **La casilla ETABS / SAP2000**, en la pestaña «ETABS/SAP2000»
  (`ProgramaCsiCombo`). Manda para todo lo de la pestaña: probar la conexión,
  leer el modelo, leer los piers y armar los planos. El lector es uno solo
  (`EtabsConnection.Destino`), porque CSI comparte la OAPI entre los dos
  programas y lo que cambia es el ProgID y la librería que se carga.
- La capa 1 de lectura (`CadLink.Etabs`): puntos, marcos, áreas, secciones,
  pisos, ejes y piers.

## Lo que falta, por etapas

Cada etapa deja el programa **funcionando**: la de hoy no se borra hasta que la
nueva dibuje lo mismo.

| # | Qué | Dónde | Cómo se comprueba |
|---|---|---|---|
| 1 | La hoja `CONFIG`: los ~250 parámetros con su valor por omisión, la lectura tipada (`CfgS`/`CfgT`/`CfgD`/`CfgB`) y las migraciones por número de versión | `CadLink.Cad/ConfigPlano.cs` + JSON versionado | Un verificador que compare parámetro por parámetro contra la lista de la macro |
| 2 | Capa 2: clasificación y geometría, que es **la mitad del código y no toca ni ETABS ni AutoCAD** | `CadLink.Cad/PlanoEstructural/*.cs` | Volcado del estado interno con las mismas 35 columnas de `MODELO_ETABS`, comparado **celda por celda** contra el de la macro sobre el mismo modelo |
| 3 | Capa 3: el dibujo —elementos, armado de losa, losacero, ejes con burbujas y cotas en los cuatro lados, títulos, orden de capas al frente— | `CadLink.Cad/PlanoEstructuralDrawer.cs` | Conteo de entidades por capa y comparación visual sobre el mismo modelo |
| 4 | Borrar lo viejo: `PlantaDrawer.cs` (658 líneas) y `PlantaCad.cs`, y colgar el botón de la pestaña del dibujo nuevo | | Que el plano salga igual que con la macro |

## Lo que hace falta para arrancar la etapa 1

**El código de la macro, en el repositorio.** Para portarla al pie de la letra
—que es como se ha hecho todo lo demás— hace falta el texto, no el resumen: los
~250 nombres de parámetro de `CONFIG` con su valor por omisión y su descripción,
y los cuerpos de `CrearHojaConfig`, `MigrarConfig` y los parches, están en el
código y **no se pueden inventar**. Si se escriben «parecidos», el plano sale
parecido, y eso no sirve.

Lo cómodo es dejarla en el repositorio, como se hizo con las demás:

```
macros/PLANOS_ESTRUCTURALES.bas
```

En el editor de VBA: clic derecho en el módulo → *Export File…* → guardar el
`.bas` → subirlo. Con eso queda dentro del proyecto, se puede ir comparando
función por función y no se pierde entre mensajes.
