---
inclusion: always
---

# Regla: todo lo editable del dibujo va en «Estilo de dibujo»

Pedido expreso del usuario: **cualquier cosa nueva que se dibuje en AutoCAD y que se pueda ajustar debe aparecer sola en la ventana «Estilo de dibujo»**. Esto incluye el tamaño de los textos, la fuente, las cotas (alto del número, tamaño y tipo de marca, colores), el color de las capas y los **hatch (patrón y escala)**.

## Cómo se agrega algo

1. **Nada a mano en el dibujante.** No escribas `Height = 0.05`, `AddText(..., 0.03)`, `Dimvar("DIMTXT", 0.025)`, `Capa("X", 140)`, `CrearCapa(x, 8, ...)`, `Hatch("ANSI31", 0.0008, ...)` ni `const string PatronX = "AR-CONC"` (el `SOLID` de los fondos y el patrón de respaldo sí van escritos).
2. **Agrega el ajuste en `client/src/CadLink.Cad/EstiloDibujo.cs`**, dentro de `PorDefecto()` y en el perfil de su tipo de dibujo:
   `.Con("alto.mi-texto", GrupoLetras, "Altura de …", TipoAjuste.Medida, N(0.05))`
   - El valor por defecto es el que el dibujo tenía hasta ahora; sin tocar nada, el dibujo debe salir igual.
   - Usa el grupo que le toca: `GrupoLetras`, `GrupoCotas`, `GrupoColores` o `GrupoHatch`. Los colores de capa llevan la clave `capa.NOMBRE`; un hatch lleva su patrón (`TipoAjuste.Patron`) y su escala (`TipoAjuste.Medida`).
   - Si es un tipo de dibujo nuevo, crea su `PerfilEstilo` y su constante.
3. **Léelo del perfil al dibujar**, por ejemplo `EstiloZapatas.Numero("alto.mi-texto")` o `EstiloDibujo.Actual.Perfil(EstiloDibujo.Muros).Numero(...)`. Si la vista previa de CadLink pinta lo mismo, debe leer el mismo valor.
4. **Si el usuario cambió el color de una capa** (`Cambiado("capa.X")`), se le pone aunque la capa ya exista.
5. La ventana (`MainWindow.EstiloDibujo.cs`) **no se toca**: muestra sola cada ajuste de cada perfil, con su editor según el tipo, su valor por defecto y el botón ↺.

## Lo que lo comprueba

- `tools/validar.py`, grupo `[28]`:
  - falla si un dibujante de `CadLink.Cad` escribe a mano una altura de texto, una variable de cota, el color de una capa o un hatch;
  - falla si un dibujante pide un ajuste que no existe en su perfil.
- `tools/prueba-estilo-dibujo`: los valores por defecto, lo que se guarda y lo que se lee.
