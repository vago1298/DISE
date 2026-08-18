# Decisión: ruta A — AutoCAD por COM

**Estado:** decidida. **Fecha:** agosto 2026.

## Lo que se decidió

El motor de dibujo se porta a C# usando **la misma API COM de AutoCAD** que ya
usan las macros, no la API nativa de plugin (`AcCoreMgd` / `AcDbMgd`).

La aplicación es un `.exe` independiente, con su propio splash, su logo y su
licenciamiento, que **maneja AutoCAD desde fuera** — exactamente como hace hoy
Excel.

## Por qué

| | Ruta A (elegida) | Ruta B (plugin nativo) |
|---|---|---|
| Traducción del código | **Mecánica.** `moSpace.AddLightWeightPolyline(pts)` es igual en VBA y en C# | Hay que reescribir cada llamada |
| Riesgo de cambiar el resultado | **Bajo.** Misma API, mismas llamadas, mismo dibujo | Medio |
| Velocidad | Igual que hoy | 10 a 100× más rápido |
| Interfaz | Ventana propia con la marca de la empresa | Comandos dentro de AutoCAD |
| Versiones de AutoCAD | **Una sola compilación sirve para 2021 a 2026** | Hay que compilar por versión |

Lo decisivo es el **riesgo**. Hay ~7.500 líneas de VBA que producen planos que se
van a obra. Cualquier ruta que obligue a reescribir las llamadas de dibujo
introduce la posibilidad de que el resultado cambie de forma sutil y no se note
hasta que ya está construido. Con la ruta A el dibujo debe salir **idéntico**, y
si no sale idéntico es un error de transcripción, no una diferencia de API.

El beneficio de B es la velocidad, y ese problema tiene otra solución más barata
que no toca el dibujo: **índices espaciales** en la capa de geometría
(ver `macro-plantas-etabs.md` §5.2). El cuello de botella real no es COM, es que
`AjustePano` recorre los 2.000 elementos por cada extremo de cada varilla de la
parrilla.

## Consecuencias

**Enlace tardío con `dynamic`.** No se referencia ninguna DLL de Autodesk. Los
objetos COM se manejan con `dynamic`, igual que el `As Object` de la macro. Un
solo binario funciona con cualquier versión de AutoCAD instalada.

**`Marshal.GetActiveObject` no existe en .NET 8.** Fue eliminado al pasar a .NET
Core. Se reemplaza llamando a la función nativa `GetActiveObject` de
`oleaut32.dll`. Resuelto en `CadLink.Cad/AcadConnection.cs`.

**Hay que manejar "AutoCAD ocupado".** Al manejar AutoCAD desde otro proceso, las
llamadas se rechazan con `RPC_E_CALL_REJECTED` o `RPC_E_SERVERCALL_RETRYLATER`
cuando el usuario tiene un comando a medias o un diálogo abierto. Es la causa
número uno de fallos intermitentes. `AcadConnection.Retry` reintenta **solo** esos
dos casos, y deja pasar cualquier otro error en lugar de tragárselo.

**El rendimiento no se resuelve con COM.** Se resuelve en la capa de geometría,
que es pura aritmética y no depende de esta decisión.

## Lo que esta decisión NO cierra

Si más adelante la velocidad estorba de verdad, la capa 3 (dibujo) se puede
migrar a la API nativa **sin tocar** el licenciamiento, la interfaz, la lectura de
ETABS ni la geometría. Por eso las tres capas van en proyectos separados:

```
CadLink.Etabs      lectura de ETABS      ← independiente de esta decisión
CadLink.Geometry   clasificación         ← independiente de esta decisión
CadLink.Cad        dibujo en AutoCAD     ← lo único atado a la ruta A
CadLink.Licensing  licenciamiento        ← independiente
CadLink.App        interfaz              ← independiente
```

## Primer entregable de esta ruta

`client/src/CadLink.Cad/AcadConnection.cs` — conexión, lanzamiento y reintentos.
Es el equivalente verificado en C# de `ConectarAutoCAD()` de las dos macros.
