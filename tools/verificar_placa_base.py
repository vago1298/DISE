#!/usr/bin/env python3
"""Comprueba el port de la logica pura de la macro de PLACA BASE.

Que se verifica
---------------
1. Las tablas J y K de "Libramientos requeridos para anclas en placas base",
   renglon por renglon, contra los valores del VBA.
2. El criterio de redondeo: al milimetro nominal mas cercano y, si cae entre
   dos renglones, el INMEDIATO SUPERIOR.
3. El reparto PERIMETRAL de las anclas: los totales de la hoja se reparten
   mitad y mitad, la impar va ABAJO en X, y las de Y van ENTRE las hileras de
   X para no repetir las esquinas.
4. El reparto en MALLA, incluida la salvedad del nx = 1.
5. PosLineal: con n = 1 va al CENTRO, no al extremo.
6. SepAuto y sus tres topes.

No necesita .NET ni AutoCAD: es aritmetica.
"""

import math

# ==========================================================================
#  Port de AnclasPlacaBase, para poder compararlo
# ==========================================================================
def separacion_minima_j_mm(diametro_mm):
    d = int(math.floor(diametro_mm + 0.5))
    tabla = [(13, 40), (16, 45), (19, 60), (22, 65), (25, 75), (29, 90),
             (32, 95), (35, 105), (38, 120), (41, 120), (44, 130), (48, 150),
             (51, 150), (57, 170), (64, 195), (70, 210), (76, 225), (89, 270),
             (102, 300)]
    for tope, valor in tabla:
        if d <= tope:
            return float(valor)
    return 3.0 * d


def distancia_minima_k_mm(diametro_mm):
    d = int(math.floor(diametro_mm + 0.5))
    tabla = [(13, 22), (16, 30), (19, 32), (22, 38), (25, 45), (29, 51),
             (32, 57), (35, 60), (38, 65), (41, 70), (44, 75), (48, 85),
             (51, 90), (57, 100), (64, 110), (70, 120), (76, 135), (89, 155),
             (102, 180)]
    for tope, valor in tabla:
        if d <= tope:
            return float(valor)
    return 1.8 * d


def pos_lineal(a, b, n, i):
    return (a + b) / 2.0 if n <= 1 else a + i * (b - a) / (n - 1)


def sep_auto(dim_placa, dim_perfil, d_agujero, escala):
    minimo = 0.5 * escala
    if 0 < dim_perfil < dim_placa:
        s = (dim_placa - dim_perfil) / 4.0
    else:
        s = 0.12 * dim_placa
    if s < d_agujero:
        s = d_agujero
    if s > dim_placa / 2.0 - minimo:
        s = dim_placa / 2.0 - minimo
    if s < minimo:
        s = minimo
    return s


def construir(x0, y0, ancho, alto, nx, ny, sep_x, sep_y,
              d_anc_x, d_agu_x, d_anc_y, d_agu_y, modo="PERIMETRAL"):
    """Devuelve [(x, y, d_ancla, d_agujero, es_x), ...]"""
    anclas = []
    nx = max(nx, 0)
    ny = max(ny, 0)

    if modo == "PERIMETRAL":
        for j in (0, 1):
            en_fila = (nx + 1) // 2 if j == 0 else nx // 2
            yj = y0 + sep_y if j == 0 else y0 + alto - sep_y
            for i in range(en_fila):
                anclas.append((pos_lineal(x0 + sep_x, x0 + ancho - sep_x, en_fila, i),
                               yj, d_anc_x, d_agu_x, True))

        for i in (0, 1):
            en_col = (ny + 1) // 2 if i == 0 else ny // 2
            xi = x0 + sep_x if i == 0 else x0 + ancho - sep_x
            for k in range(1, en_col + 1):
                anclas.append((xi,
                               y0 + sep_y + k * ((alto - 2 * sep_y) / (en_col + 1)),
                               d_anc_y, d_agu_y, False))
        return anclas

    for j in range(ny):
        yj = pos_lineal(y0 + sep_y, y0 + alto - sep_y, ny, j)
        for i in range(nx):
            xi = pos_lineal(x0 + sep_x, x0 + ancho - sep_x, nx, i)
            extrema = (j == 0 or j == ny - 1) and not (nx == 1 and ny > 1)
            if extrema:
                anclas.append((xi, yj, d_anc_x, d_agu_x, True))
            else:
                anclas.append((xi, yj, d_anc_y, d_agu_y, False))
    return anclas


# ==========================================================================
#  Comprobaciones
# ==========================================================================
fallos = []


def check(nombre, ok, detalle=""):
    print(f"  {'OK   ' if ok else 'FALLA'} {nombre}" + (f" -> {detalle}" if detalle and not ok else ""))
    if not ok:
        fallos.append(nombre)


print("=" * 78)
print("PLACA BASE: TABLAS J y K DE LIBRAMIENTOS")
print("=" * 78)

# Los renglones EXACTOS del VBA. D en mm.
J_VBA = {13: 40, 16: 45, 19: 60, 22: 65, 25: 75, 29: 90, 32: 95, 35: 105,
         38: 120, 41: 120, 44: 130, 48: 150, 51: 150, 57: 170, 64: 195,
         70: 210, 76: 225, 89: 270, 102: 300}
K_VBA = {13: 22, 16: 30, 19: 32, 22: 38, 25: 45, 29: 51, 32: 57, 35: 60,
         38: 65, 41: 70, 44: 75, 48: 85, 51: 90, 57: 100, 64: 110, 70: 120,
         76: 135, 89: 155, 102: 180}

check("los 19 renglones de la tabla J coinciden con el VBA",
      all(separacion_minima_j_mm(d) == v for d, v in J_VBA.items()))
check("los 19 renglones de la tabla K coinciden con el VBA",
      all(distancia_minima_k_mm(d) == v for d, v in K_VBA.items()))

# El renglon INMEDIATO SUPERIOR cuando el diametro cae entre dos.
check("un diametro entre renglones toma el INMEDIATO SUPERIOR (J)",
      separacion_minima_j_mm(14) == 45 and separacion_minima_j_mm(20) == 65)
check("un diametro entre renglones toma el INMEDIATO SUPERIOR (K)",
      distancia_minima_k_mm(14) == 30 and distancia_minima_k_mm(20) == 38)

# Redondeo al milimetro nominal mas cercano.
check("el diametro se redondea al milimetro mas cercano",
      separacion_minima_j_mm(12.7) == 40 and separacion_minima_j_mm(15.9) == 45)

# Fuera de tabla.
check("fuera de la tabla, J extrapola a 3 diametros",
      separacion_minima_j_mm(120) == 360)
check("fuera de la tabla, K extrapola con 1.8 diametros",
      abs(distancia_minima_k_mm(120) - 216) < 1e-9)

# Los diametros comerciales en pulgadas, que son los que se usan de verdad.
print("\n  Diametros comerciales (pulgadas -> mm -> J / K):")
for frac, pulg in (("1/2", 0.5), ("5/8", 0.625), ("3/4", 0.75), ("7/8", 0.875),
                   ("1", 1.0), ("1 1/4", 1.25)):
    mm = pulg * 25.4
    print(f"    {frac:6} = {mm:6.2f} mm  ->  J = {separacion_minima_j_mm(mm):5.1f} mm"
          f"   K = {distancia_minima_k_mm(mm):5.1f} mm")

print("\n" + "=" * 78)
print("REPARTO PERIMETRAL DE LAS ANCLAS")
print("=" * 78)

# Placa 40 x 30 cm, 6 anclas en X y 2 en Y, en unidades de cm.
anc = construir(0, 0, 40, 30, nx=6, ny=2, sep_x=5, sep_y=5,
                d_anc_x=1.59, d_agu_x=1.75, d_anc_y=1.27, d_agu_y=1.43)

en_x = [a for a in anc if a[4]]
en_y = [a for a in anc if not a[4]]

check("el total de X se reparte mitad abajo y mitad arriba",
      len(en_x) == 6 and len([a for a in en_x if a[1] == 5]) == 3
      and len([a for a in en_x if a[1] == 25]) == 3)
check("el total de Y se reparte una a cada lado",
      len(en_y) == 2
      and len([a for a in en_y if abs(a[0] - 5) < 1e-9]) == 1
      and len([a for a in en_y if abs(a[0] - 35) < 1e-9]) == 1)

# Impar: la de mas va ABAJO.
impar = construir(0, 0, 40, 30, nx=5, ny=0, sep_x=5, sep_y=5,
                  d_anc_x=1.59, d_agu_x=1.75, d_anc_y=0, d_agu_y=0)
check("con total impar en X, la ancla de mas va ABAJO",
      len([a for a in impar if a[1] == 5]) == 3
      and len([a for a in impar if a[1] == 25]) == 2)

# Las de Y van ENTRE las hileras de X: ninguna comparte posicion con una de X.
comparten = [a for a in en_y if any(abs(a[0] - b[0]) < 1e-9 and abs(a[1] - b[1]) < 1e-9
                                     for b in en_x)]
check("las anclas de Y no caen sobre las esquinas, que son de X",
      len(comparten) == 0)

# Cada direccion lleva SU diametro.
check("cada direccion lleva su propio diametro",
      all(abs(a[2] - 1.59) < 1e-9 for a in en_x)
      and all(abs(a[2] - 1.27) < 1e-9 for a in en_y))

# Se admite 0 en una direccion.
solo_x = construir(0, 0, 40, 30, 4, 0, 5, 5, 1.59, 1.75, 1.27, 1.43)
check("se admite 0 anclas en una direccion",
      len(solo_x) == 4 and all(a[4] for a in solo_x))

print("\n" + "=" * 78)
print("REPARTO EN MALLA")
print("=" * 78)

malla = construir(0, 0, 40, 30, 3, 3, 5, 5, 1.59, 1.75, 1.27, 1.43, modo="MALLA")
check("la malla da nx * ny anclas", len(malla) == 9)
check("las hileras extremas llevan el diametro de X",
      all(abs(a[2] - 1.59) < 1e-9 for a in malla if a[1] in (5, 25)))
check("la hilera interior lleva el diametro de Y",
      all(abs(a[2] - 1.27) < 1e-9 for a in malla if abs(a[1] - 15) < 1e-9))

# La salvedad del nx = 1: una sola columna es una hilera VERTICAL, asi que le
# toca el diametro de Y aunque este en el extremo.
columna = construir(0, 0, 40, 30, 1, 3, 5, 5, 1.59, 1.75, 1.27, 1.43, modo="MALLA")
check("con nx = 1 todas llevan el diametro de Y, aunque esten en el extremo",
      all(abs(a[2] - 1.27) < 1e-9 for a in columna))

print("\n" + "=" * 78)
print("PosLineal Y SepAuto")
print("=" * 78)

check("con n = 1, PosLineal va al CENTRO y no al extremo",
      abs(pos_lineal(5, 35, 1, 0) - 20) < 1e-9)
check("con n = 2, PosLineal da los dos extremos",
      abs(pos_lineal(5, 35, 2, 0) - 5) < 1e-9
      and abs(pos_lineal(5, 35, 2, 1) - 35) < 1e-9)

# SepAuto: a media distancia entre el pano del perfil y el borde.
check("SepAuto cae a media distancia perfil-borde",
      abs(sep_auto(40, 20, 1.75, 1) - 5) < 1e-9)
check("SepAuto nunca baja del diametro del agujero",
      sep_auto(40, 39, 3, 1) >= 3)
check("SepAuto nunca pasa de la mitad de la placa menos medio cm",
      sep_auto(10, 0, 20, 1) <= 10 / 2 - 0.5 + 1e-9)

# ==========================================================================
#  LA FILA DE LA TABLA: el lector de fracciones y el conteo de anclas
# ==========================================================================
#  Port de PlacaBaseRow.Pulgadas -el ValorFraccion de la macro-. En el taller los
#  diametros se piden en fracciones, y equivocarse aqui NO SE VE: leer "1 1/4" como
#  1 pulgada da un ancla creible con la medida equivocada.
def pulgadas(texto):
    if texto is None or texto.strip() == "":
        return 0.0

    limpio = (texto.replace('"', " ")
                   .replace("-", " ")
                   .replace("\u00a0", " ")
                   .replace(",", "."))

    total = 0.0
    algo = False

    for pieza in limpio.split():
        t = pieza.strip()
        if not t:
            continue

        v = 0.0
        if "/" in t:
            partes = t.split("/")
            if len(partes) == 2:
                try:
                    a, b = float(partes[0]), float(partes[1])
                    if abs(b) > 1e-9:
                        v = a / b
                except ValueError:
                    v = 0.0
        else:
            try:
                v = float(t)
            except ValueError:
                v = 0.0

        if v > 0:
            total += v
            algo = True

    return total if algo else 0.0


def total_anclas(nx, ny, en_malla):
    nx = max(0, nx)
    ny = max(0, ny)
    return nx * ny if en_malla else nx + ny


print("\n" + "=" * 78)
print("EL LECTOR DE FRACCIONES DE LA CELDA")
print("=" * 78)

check("una fraccion simple: 5/8", abs(pulgadas("5/8") - 0.625) < 1e-9)
check("un entero: 1", abs(pulgadas("1") - 1.0) < 1e-9)
check("un mixto con espacio: 1 1/4", abs(pulgadas("1 1/4") - 1.25) < 1e-9)
check("un mixto con guion: 1-1/2", abs(pulgadas("1-1/2") - 1.5) < 1e-9)
check("con la comilla de pulgada: 3/4\"", abs(pulgadas('3/4"') - 0.75) < 1e-9)
check("con espacio duro, como al pegar de Excel",
      abs(pulgadas("1\u00a01/8") - 1.125) < 1e-9)
check("la coma vale como punto decimal", abs(pulgadas("0,625") - 0.625) < 1e-9)
check("vacio da cero, que es «sin dato»", pulgadas("") == 0.0 and pulgadas(None) == 0.0)
check("basura da cero y no revienta", pulgadas("como sea") == 0.0)
check("denominador cero da cero y no divide por cero", pulgadas("1/0") == 0.0)

print("\n" + "=" * 78)
print("EL CONTEO DE ANCLAS DE LA TABLA")
print("=" * 78)

#  Los mismos numeros que da Construir: perimetral suma, malla multiplica. Es lo que se
#  le pide al proveedor, asi que la tabla tiene que decir el que de verdad se dibuja.
cuenta_perim = construir(0, 0, 40, 40, 4, 4, 5, 5, 1.9, 2.06, 1.9, 2.06, modo="PERIMETRAL")
cuenta_malla = construir(0, 0, 40, 40, 4, 4, 5, 5, 1.9, 2.06, 1.9, 2.06, modo="MALLA")

check("perimetral: la tabla dice lo mismo que dibuja Construir",
      total_anclas(4, 4, False) == len(cuenta_perim),
      f"{total_anclas(4, 4, False)} vs {len(cuenta_perim)}")
check("malla: la tabla dice lo mismo que dibuja Construir",
      total_anclas(4, 4, True) == len(cuenta_malla),
      f"{total_anclas(4, 4, True)} vs {len(cuenta_malla)}")
check("y no son el mismo numero: 8 contra 16",
      len(cuenta_perim) == 8 and len(cuenta_malla) == 16,
      f"{len(cuenta_perim)} y {len(cuenta_malla)}")
check("un negativo se trata como cero", total_anclas(-3, 4, False) == 4)

print("\n" + "=" * 78)
if fallos:
    print(f"ATENCION: {len(fallos)} comprobacion(es) fallaron.")
    for f in fallos:
        print(f"  - {f}")
else:
    print("OK: la logica pura de la placa base coincide con la macro.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
