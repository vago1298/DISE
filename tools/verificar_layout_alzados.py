"""Compara la colocacion de los alzados: VBA original contra el port de C#.

El usuario: "NO RESPETAS QUE LLEVEN EL BLOQUE DE LA SECCION A UN COSTADO, LA
SEPARACION ENTRE ELEMENTOS". Eso son cinco constantes y dos formulas, asi que se
comprueba numero a numero en lugar de a ojo.

  vba_fila   traducido del bucle principal de Alzados_Trabes_Desde_Excel
  cs_fila    traducido de AlzadoLayout.Colocar
"""

# ---- Constantes del VBA ----
Y_BLOQUES = 2.0
SEP_SECCIONES = 0.6
MARGEN_COL = 0.4
SEP_CARAS = 0.3
SEP_SEC_ALZ = 0.2
HOOK_DIM_OFF_2 = 0.14
DIM_OFF_3 = 0.24
ROTULO_OFF_COL = 0.09
SCALE_ELEVATION = 0.01

ANCHO_COTAS_VERTICAL = DIM_OFF_3 + ROTULO_OFF_COL + 0.1

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


# ======================================================================
#  VBA: el bucle principal, tal cual
# ======================================================================
def vba_fila(elementos):
    """
    Recorre la fila como el VBA y devuelve, por elemento, donde queda cada cosa.

    'elementos' son diccionarios con: vertical, ancho_sec, alto_sec, largo, dos_caras
    """
    x0 = 0.0
    puestos = []

    for el in elementos:
        if el["vertical"]:
            # x0 = x0 + MARGEN_COL
            x0 = x0 + MARGEN_COL

            # Set br = InsertBlockByLeftEdgeGap(ms, x0, YBaseEfectiva(), ..., True)
            # ForzarYBloque br, YBaseEfectiva()   -> pano inferior en Y_BLOQUES
            x_sec = x0
            block_width = el["ancho_sec"]
            block_top_y = Y_BLOQUES + el["alto_sec"]

            # xInsAlz = x0 + blockWidth
            x_ins_alz = x0 + block_width

            # topY1 = DrawVerticalColumnBlock(..., blockTopY + SEP_SEC_ALZ, L_VERT, ...)
            y_alz = block_top_y + SEP_SEC_ALZ
            top_y1 = y_alz + el["largo"]

            y_alz2 = None
            if el["dos_caras"]:
                # topY2 = DrawVerticalColumnBlock(..., topY1 + SEP_CARAS, ...)
                y_alz2 = top_y1 + SEP_CARAS

            # alzadoWidth = DIM_OFF_3 + ROTULO_OFF_COL + 0.1
            # totalWidth  = blockWidth + alzadoWidth
            # x0 = x0 + totalWidth + SEP_SECCIONES
            total_width = block_width + ANCHO_COTAS_VERTICAL
            x_sig = x0 + total_width + SEP_SECCIONES

            puestos.append({
                "x_sec": x_sec, "x_alz": x_ins_alz,
                "y_alz": y_alz, "y_alz2": y_alz2, "x_sig": x_sig,
            })
            x0 = x_sig
        else:
            # yBeam = YBaseEfectiva()
            y_beam = Y_BLOQUES
            x_sec = x0
            block_width = el["ancho_sec"]

            # xBeam = x0 + blockWidth + SEP_SEC_ALZ
            x_beam = x0 + block_width + SEP_SEC_ALZ

            # totalWidth = blockWidth + SEP_SEC_ALZ + L_AJUSTADA + HOOK_DIM_OFF_2
            # x0 = x0 + totalWidth + SEP_SECCIONES
            total_width = block_width + SEP_SEC_ALZ + el["largo"] + HOOK_DIM_OFF_2
            x_sig = x0 + total_width + SEP_SECCIONES

            puestos.append({
                "x_sec": x_sec, "x_alz": x_beam,
                "y_alz": y_beam, "y_alz2": None, "x_sig": x_sig,
            })
            x0 = x_sig

    return puestos


# ======================================================================
#  C#: AlzadoLayout.Colocar
# ======================================================================
def cs_colocar(x0, vertical, ancho_sec, tope_sec, largo, dos_caras):
    if vertical:
        x_sec = x0 + MARGEN_COL
        x_alz = x_sec + ancho_sec
        y1 = tope_sec + SEP_SEC_ALZ
        return {
            "x_sec": x_sec,
            "x_alz": x_alz,
            "y_alz": y1,
            "y_alz2": (y1 + largo + SEP_CARAS) if dos_caras else None,
            "x_sig": x_sec + ancho_sec + ANCHO_COTAS_VERTICAL + SEP_SECCIONES,
        }
    return {
        "x_sec": x0,
        "x_alz": x0 + ancho_sec + SEP_SEC_ALZ,
        "y_alz": Y_BLOQUES,
        "y_alz2": None,
        "x_sig": x0 + ancho_sec + SEP_SEC_ALZ + largo + HOOK_DIM_OFF_2 + SEP_SECCIONES,
    }


def cs_fila(elementos):
    x0 = 0.0
    puestos = []
    for el in elementos:
        tope = Y_BLOQUES + el["alto_sec"]
        p = cs_colocar(x0, el["vertical"], el["ancho_sec"], tope,
                       el["largo"], el["dos_caras"])
        puestos.append(p)
        x0 = p["x_sig"]
    return puestos


# ======================================================================
#  Comparacion
# ======================================================================
print("=" * 78)
print(" Colocacion de los alzados: VBA original  vs  port de C#")
print("=" * 78)

FILAS = [
    # Solo trabes
    [dict(vertical=False, ancho_sec=0.30, alto_sec=0.60, largo=3.0, dos_caras=False),
     dict(vertical=False, ancho_sec=0.25, alto_sec=0.50, largo=4.5, dos_caras=False)],
    # Solo columnas, una cuadrada y una rectangular
    [dict(vertical=True, ancho_sec=0.40, alto_sec=0.40, largo=3.0, dos_caras=False),
     dict(vertical=True, ancho_sec=0.30, alto_sec=0.60, largo=3.0, dos_caras=True)],
    # Mezcla, que es el caso real de una hoja
    [dict(vertical=False, ancho_sec=0.30, alto_sec=0.60, largo=3.0, dos_caras=False),
     dict(vertical=True, ancho_sec=0.40, alto_sec=0.40, largo=3.0, dos_caras=False),
     dict(vertical=False, ancho_sec=0.20, alto_sec=0.40, largo=2.7, dos_caras=False),
     dict(vertical=True, ancho_sec=0.50, alto_sec=0.80, largo=4.0, dos_caras=True),
     dict(vertical=True, ancho_sec=0.30, alto_sec=0.30, largo=1.0, dos_caras=False)],
    # Un dado corto y una contratrabe larga
    [dict(vertical=True, ancho_sec=0.35, alto_sec=0.35, largo=1.0, dos_caras=False),
     dict(vertical=False, ancho_sec=0.20, alto_sec=0.40, largo=8.0, dos_caras=False)],
    # Bloque de seccion inexistente: la macro supone 0.8 x 0.4
    [dict(vertical=True, ancho_sec=0.80, alto_sec=0.40, largo=3.0, dos_caras=False),
     dict(vertical=False, ancho_sec=0.80, alto_sec=0.40, largo=2.0, dos_caras=False)],
]

difs = 0
for n, fila in enumerate(FILAS, 1):
    v = vba_fila(fila)
    k = cs_fila(fila)
    print(f"\n  fila {n} ({len(fila)} elementos)")

    for i, (pv, pk) in enumerate(zip(v, k)):
        iguales = all(
            (pv[key] is None and pk[key] is None) or
            (pv[key] is not None and pk[key] is not None and abs(pv[key] - pk[key]) < 1e-12)
            for key in ("x_sec", "x_alz", "y_alz", "y_alz2", "x_sig")
        )
        marca = "  " if iguales else "!!"
        if not iguales:
            difs += 1

        y2 = f"{pk['y_alz2']:.3f}" if pk["y_alz2"] is not None else "-"
        print(f"    {marca} [{i}] seccion x={pk['x_sec']:.3f}  "
              f"alzado x={pk['x_alz']:.3f} y={pk['y_alz']:.3f}  "
              f"2a cara y={y2}  siguiente x={pk['x_sig']:.3f}")
        if not iguales:
            print(f"        VBA: {pv}")
            print(f"        C# : {pk}")

check("la colocacion coincide con el VBA en todas las filas", difs == 0,
      f"{difs} elementos distintos")

# ---- Comprobaciones de la fisica del plano ----
print()
print("=" * 78)
print(" Que el plano quede legible")
print("=" * 78)

# 1) Los elementos no se enciman: el siguiente arranca despues del anterior.
fila = FILAS[2]
k = cs_fila(fila)
solapes = []
for i in range(len(k) - 1):
    # Borde derecho ocupado por el elemento i
    el = fila[i]
    if el["vertical"]:
        derecha = k[i]["x_sec"] + el["ancho_sec"] + ANCHO_COTAS_VERTICAL
    else:
        derecha = k[i]["x_alz"] + el["largo"] + HOOK_DIM_OFF_2
    if k[i + 1]["x_sec"] < derecha - 1e-12:
        solapes.append((i, derecha, k[i + 1]["x_sec"]))

check("ningun elemento se encima con el siguiente", not solapes,
      "; ".join(f"[{i}] termina en {d:.3f} y el siguiente empieza en {s:.3f}"
                for i, d, s in solapes))

# 2) La separacion entre elementos es EXACTAMENTE SEP_SECCIONES cuando la manda
#    la formula, o mayor por el MARGEN_COL de una columna.
for i in range(len(k) - 1):
    el = fila[i]
    if el["vertical"]:
        derecha = k[i]["x_sec"] + el["ancho_sec"] + ANCHO_COTAS_VERTICAL
    else:
        derecha = k[i]["x_alz"] + el["largo"] + HOOK_DIM_OFF_2
    hueco = k[i + 1]["x_sec"] - derecha
    esperado = SEP_SECCIONES + (MARGEN_COL if fila[i + 1]["vertical"] else 0.0)
    check(f"el hueco tras el elemento {i} es el de la macro",
          abs(hueco - esperado) < 1e-12,
          f"hueco {hueco:.4f}, esperado {esperado:.4f}")

# 3) En la trabe, la seccion queda A LA IZQUIERDA del alzado y separada SEP_SEC_ALZ.
t = dict(vertical=False, ancho_sec=0.30, alto_sec=0.60, largo=3.0, dos_caras=False)
p = cs_colocar(0.0, **{k2: t[k2] for k2 in ("vertical", "ancho_sec", "largo", "dos_caras")},
               tope_sec=Y_BLOQUES + t["alto_sec"])
check("en la trabe la seccion va al lado izquierdo del alzado",
      abs(p["x_alz"] - (p["x_sec"] + t["ancho_sec"] + SEP_SEC_ALZ)) < 1e-12)
check("y los dos apoyados en la misma Y", abs(p["y_alz"] - Y_BLOQUES) < 1e-12)

# 4) En la columna, el alzado va ENCIMA de la seccion, no al lado.
c = dict(vertical=True, ancho_sec=0.40, alto_sec=0.40, largo=3.0, dos_caras=True)
p = cs_colocar(0.0, **{k2: c[k2] for k2 in ("vertical", "ancho_sec", "largo", "dos_caras")},
               tope_sec=Y_BLOQUES + c["alto_sec"])
check("en la columna el alzado arranca encima del pano superior de la seccion",
      abs(p["y_alz"] - (Y_BLOQUES + c["alto_sec"] + SEP_SEC_ALZ)) < 1e-12,
      f"y={p['y_alz']:.4f}")
check("y la segunda cara, a SEP_CARAS de la primera",
      abs(p["y_alz2"] - (p["y_alz"] + c["largo"] + SEP_CARAS)) < 1e-12)
check("la columna abre MARGEN_COL antes de su seccion",
      abs(p["x_sec"] - MARGEN_COL) < 1e-12)

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
