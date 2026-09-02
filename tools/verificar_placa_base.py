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
import re

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
print("PLACA BASE: TABLAS J, K y L DE LIBRAMIENTOS")
print("=" * 78)

# ==========================================================================
#  LA AUTORIDAD ES EL ORIGINAL EN MILIMETROS, Y EL PORT DEL VBA COINCIDIA
# ==========================================================================
#  Hay que dejar escrito lo que paso, porque el error se cometio en las dos
#  direcciones y la segunda vez fue peor:
#
#  1) El port reprodujo fielmente la tabla del VBA. Correcto.
#  2) Llego una captura del mismo cuadro en PULGADAS, se cotejo contra ella y se
#     «corrigieron» J y K en dos renglones. INCORRECTO: la captura era la que
#     estaba mal. Le faltaba el renglon de 48 mm -1 7/8"- y al faltarle, los
#     valores de 1 5/8" y 1 3/4" salian corridos uno hacia arriba.
#  3) Llego el original -Hylsa ES-03-001, en milimetros, 19 renglones- y se
#     revirtio al valor del VBA.
#
#      D                 VBA / original   captura en pulgadas
#      1 5/8"  41 mm     J=120  K=70      J=130  K=75   <- del renglon siguiente
#      1 3/4"  44 mm     J=130  K=75      J=150  K=85   <- del renglon siguiente
#      1 7/8"  48 mm     J=150  K=85      (no estaba)
#
#  LA DEFENSA CONTRA QUE VUELVA A PASAR es contar los renglones antes de tocar
#  nada: el cuadro tiene DIECINUEVE. Si una transcripcion trae dieciocho, le falta
#  uno y los valores de abajo estan corridos.
J_CUADRO = {13: 40, 16: 45, 19: 60, 22: 65, 25: 75, 29: 90, 32: 95, 35: 105,
            38: 120, 41: 120, 44: 130, 48: 150, 51: 150, 57: 170, 64: 195,
            70: 210, 76: 225, 89: 270, 102: 300}
K_CUADRO = {13: 22, 16: 30, 19: 32, 22: 38, 25: 45, 29: 51, 32: 57, 35: 60,
            38: 65, 41: 70, 44: 75, 48: 85, 51: 90, 57: 100, 64: 110, 70: 120,
            76: 135, 89: 155, 102: 180}
L_CUADRO = {13: 23, 16: 28, 19: 34, 22: 37, 25: 44, 29: 49, 32: 55, 35: 60,
            38: 66, 41: 71, 44: 76, 48: 82, 51: 87, 57: 97, 64: 107, 70: 118,
            76: 130, 89: 150, 102: 172}

check("el cuadro tiene 19 renglones en las tres columnas",
      len(J_CUADRO) == 19 and len(K_CUADRO) == 19 and len(L_CUADRO) == 19,
      f"J={len(J_CUADRO)} K={len(K_CUADRO)} L={len(L_CUADRO)}")

check("los 19 renglones de la tabla J coinciden con el original",
      all(separacion_minima_j_mm(d) == v for d, v in J_CUADRO.items()))
check("los 19 renglones de la tabla K coinciden con el original",
      all(distancia_minima_k_mm(d) == v for d, v in K_CUADRO.items()))

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

# ==========================================================================
#  LOS CARTABONES: port de CartabonesPlacaBase
# ==========================================================================
#  Dos cosas que no se ven en el dibujo y si en obra:
#
#  1. EL CRUCE X/Y. Los datos de X dibujan los cartabones que salen de las caras Y,
#     y al contrario. Es la correccion que la macro documenta, e intercambiarla saca
#     los cartabones con la longitud del otro sentido: se ve creible y esta mal.
#  2. EL 60 % CENTRAL. No se reparten sobre la cara entera: un cartabon en el extremo
#     del patin cae donde el perfil ya no tiene alma que lo respalde.
FRACCION_DE_LA_CARA = 0.6


def posicion_cartabon(centro, dimension, indice, cuantos):
    if cuantos <= 1 or dimension <= 0:
        return centro

    tramo = FRACCION_DE_LA_CARA * dimension

    return centro - tramo / 2.0 + (indice - 1) * tramo / (cuantos - 1)


def construir_cartabones(con_cartabones, n_x, n_y, esp_x, esp_y, largo_x, largo_y,
                         xc, yc, p_x, p_y, escala=1.0):
    """Devuelve [(x1, y1, x2, y2, es_x), ...] en el mismo orden que el C#."""
    salida = []

    if not con_cartabones or escala <= 0:
        return salida

    esp_x *= escala
    esp_y *= escala
    largo_x *= escala
    largo_y *= escala

    nx = max(0, n_x) if esp_x > 0 and largo_x > 0 else 0
    ny = max(0, n_y) if esp_y > 0 and largo_y > 0 else 0

    # Cartabones X: placas verticales, desde las caras +Y y -Y.
    for lado in (0, 1):
        cuantos = (nx + 1) // 2 if lado == 0 else nx // 2

        for i in range(1, cuantos + 1):
            x = posicion_cartabon(xc, p_x, i, cuantos)

            if lado == 0:
                salida.append((x - esp_x / 2, yc + p_y / 2,
                               x + esp_x / 2, yc + p_y / 2 + largo_x, True))
            else:
                salida.append((x - esp_x / 2, yc - p_y / 2 - largo_x,
                               x + esp_x / 2, yc - p_y / 2, True))

    # Cartabones Y: placas horizontales, desde las caras +X y -X.
    for lado in (0, 1):
        cuantos = (ny + 1) // 2 if lado == 0 else ny // 2

        for i in range(1, cuantos + 1):
            y = posicion_cartabon(yc, p_y, i, cuantos)

            if lado == 0:
                salida.append((xc + p_x / 2, y - esp_y / 2,
                               xc + p_x / 2 + largo_y, y + esp_y / 2, False))
            else:
                salida.append((xc - p_x / 2 - largo_y, y - esp_y / 2,
                               xc - p_x / 2, y + esp_y / 2, False))

    return salida


print("\n" + "=" * 78)
print("EL REPARTO DE LOS CARTABONES")
print("=" * 78)

#  Placa 40x40, perfil de 20x20 centrado, 4 cartabones por sentido de 1.27 cm de
#  espesor y 15 cm de largo.
cart = construir_cartabones(True, 4, 4, 1.27, 1.27, 15, 15, 20, 20, 20, 20)

check("con la casilla apagada no sale ninguno",
      len(construir_cartabones(False, 4, 4, 1.27, 1.27, 15, 15, 20, 20, 20, 20)) == 0)

check("4 y 4 dan ocho cartabones", len(cart) == 8, f"{len(cart)}")

check("los cuatro primeros son de X y los cuatro ultimos de Y",
      all(c[4] for c in cart[:4]) and not any(c[4] for c in cart[4:]))

#  El cruce: los de X salen en VERTICAL de las caras horizontales, asi que su lado
#  largo es el vertical y mide LargoX.
altos = [c for c in cart if c[4]]
anchos = [c for c in cart if not c[4]]

check("los de X son placas VERTICALES de largo LongCartabonX",
      all(abs(abs(c[3] - c[1]) - 15) < 1e-9 for c in altos)
      and all(abs(abs(c[2] - c[0]) - 1.27) < 1e-9 for c in altos))

check("los de Y son placas HORIZONTALES de largo LongCartabonY",
      all(abs(abs(c[2] - c[0]) - 15) < 1e-9 for c in anchos)
      and all(abs(abs(c[3] - c[1]) - 1.27) < 1e-9 for c in anchos))

#  Y arrancan del PANO del perfil, no del centro ni del borde de la placa.
check("arrancan del pano del perfil, no del centro",
      any(abs(c[1] - 30) < 1e-9 for c in altos)     # cara +Y: 20 + 20/2
      and any(abs(c[3] - 10) < 1e-9 for c in altos))  # cara -Y: 20 - 20/2

#  La impar va en la cara POSITIVA, igual que en las anclas.
impar = construir_cartabones(True, 3, 0, 1.27, 1.27, 15, 15, 20, 20, 20, 20)

check("con 3, dos van en la cara positiva y una en la negativa",
      len(impar) == 3
      and sum(1 for c in impar if c[1] > 20) == 2
      and sum(1 for c in impar if c[1] < 20) == 1,
      f"{len(impar)} cartabones")

#  Sin espesor o sin longitud no hay cartabon: la macro pone la cantidad en cero en
#  lugar de dibujar una placa de grueso nulo.
check("sin espesor no sale ninguno de ese sentido",
      len(construir_cartabones(True, 4, 4, 0, 1.27, 15, 15, 20, 20, 20, 20)) == 4)
check("sin longitud no sale ninguno de ese sentido",
      len(construir_cartabones(True, 4, 4, 1.27, 1.27, 0, 15, 20, 20, 20, 20)) == 4)

print("\n" + "=" * 78)
print("EL 60 % CENTRAL DE LA CARA")
print("=" * 78)

check("con uno solo va al centro, que es donde esta el alma",
      abs(posicion_cartabon(20, 20, 1, 1) - 20) < 1e-9)

#  Cara de 20 cm: el 60 % son 12, o sea de 14 a 26 con el centro en 20.
check("con dos, a los extremos del 60 % central",
      abs(posicion_cartabon(20, 20, 1, 2) - 14) < 1e-9
      and abs(posicion_cartabon(20, 20, 2, 2) - 26) < 1e-9)

check("con tres, el de en medio cae en el centro",
      abs(posicion_cartabon(20, 20, 2, 3) - 20) < 1e-9)

check("NINGUNO se sale del 60 % central de la cara",
      all(14 - 1e-9 <= posicion_cartabon(20, 20, i, 5) <= 26 + 1e-9
          for i in range(1, 6)))

check("sin dimension de cara va al centro y no divide por cero",
      abs(posicion_cartabon(20, 0, 2, 4) - 20) < 1e-9)

# ==========================================================================
#  EL AGUJERO AUTOMATICO: ancla + 1/16", escrito en fraccion
# ==========================================================================
#  Port de PlacaBaseRow.ComoFraccion, AgujeroAutomatico y SeguirConElAgujero.
#
#  La vuelta a fraccion hace falta por lo mismo que el lector: en el taller los
#  diametros se piden en fracciones. Un agujero que en el plano dijera 0.8125"
#  obligaria a traducir en obra.
def mcd(a, b):
    while b:
        a, b = b, a % b
    return a or 1


def como_fraccion(pulgadas):
    if pulgadas <= 0:
        return ""

    for den in (16, 32, 64):
        exacto = pulgadas * den
        redondo = round(exacto)

        if abs(exacto - redondo) > 1e-6 or redondo <= 0:
            continue

        n, d = int(redondo), den
        g = mcd(n, d)
        n //= g
        d //= g

        entero, resto = divmod(n, d)

        if resto == 0:
            return str(entero)

        return f"{resto}/{d}" if entero == 0 else f"{entero} {resto}/{d}"

    # Antes que redondear en silencio a la fraccion de al lado, se escribe decimal.
    return f"{pulgadas:.4f}".rstrip("0").rstrip(".")


def agujero_automatico(ancla):
    d = pulgadas(ancla)

    return "" if d <= 0 else como_fraccion(d + 1.0 / 16.0)


def seguir_con_el_agujero(ancla_antes, ancla_ahora, agujero_puesto):
    """Devuelve el agujero que queda tras cambiar el ancla."""
    puesto = (agujero_puesto or "").strip()

    if puesto and puesto != agujero_automatico(ancla_antes):
        return puesto        # lo escribio el usuario: se respeta

    return agujero_automatico(ancla_ahora)


print("\n" + "=" * 78)
print("EL AGUJERO AUTOMATICO")
print("=" * 78)

#  Los ocho diametros usuales, con su agujero. Todos caen en dieciseisavos exactos,
#  que es la razon de probar 16 antes que nada.
esperados = {
    "1/2":   "9/16",
    "5/8":   "11/16",
    "3/4":   "13/16",
    "7/8":   "15/16",
    "1":     "1 1/16",
    "1 1/8": "1 3/16",
    "1 1/4": "1 5/16",
    "1 1/2": "1 9/16",
}

for ancla, agujero in esperados.items():
    check(f'ancla {ancla}" -> agujero {agujero}"',
          agujero_automatico(ancla) == agujero,
          f'dio "{agujero_automatico(ancla)}"')

check("y el agujero es SIEMPRE 1/16 mayor que su ancla",
      all(abs(pulgadas(a) + 1 / 16 - pulgadas(g)) < 1e-9
          for a, g in esperados.items()))

check("sin ancla no se propone agujero, y no se inventa 1/16",
      agujero_automatico("") == "" and agujero_automatico(None) == ""
      and agujero_automatico("como sea") == "")

print("\n" + "=" * 78)
print("LA VUELTA A FRACCION")
print("=" * 78)

check("se reduce: 8/16 se escribe 1/2", como_fraccion(0.5) == "1/2")
check("un entero se escribe entero, sin /1", como_fraccion(2.0) == "2")
check("un mixto lleva el entero delante", como_fraccion(1.25) == "1 1/4")
check("los treintaidosavos tambien salen", como_fraccion(1.0 / 32) == "1/32")
check("y los sesentaicuatroavos", como_fraccion(3.0 / 64) == "3/64")
check("cero y negativos dan vacio",
      como_fraccion(0) == "" and como_fraccion(-1) == "")

#  Lo importante: un decimal que NO es fraccion de taller no se redondea en silencio
#  a la de al lado. Redondear seria poner en el plano una medida que nadie pidio.
check("un decimal cualquiera se escribe decimal, no se redondea a la fraccion vecina",
      como_fraccion(0.7) == "0.7", f'dio "{como_fraccion(0.7)}"')

#  Y el viaje de ida y vuelta cierra: lo que se escribe se vuelve a leer igual.
check("ida y vuelta: leer(escribir(x)) == x para los ocho agujeros",
      all(abs(pulgadas(como_fraccion(pulgadas(g))) - pulgadas(g)) < 1e-9
          for g in esperados.values()))

print("\n" + "=" * 78)
print("EL AGUJERO SIGUE AL ANCLA, PERO NO PISA LO ESCRITO A MANO")
print("=" * 78)

check("en blanco, se llena con el automatico del ancla nueva",
      seguir_con_el_agujero("3/4", "1", "") == "1 1/16")

check("si tenia el automatico del ancla vieja, pasa al de la nueva",
      seguir_con_el_agujero("3/4", "1", "13/16") == "1 1/16")

#  Este es el que importa: un agujero HOLGADO a proposito -para tener margen al
#  cuadrar la columna en montaje- no se puede perder al tocar el ancla.
check("un agujero holgado escrito a mano SE RESPETA",
      seguir_con_el_agujero("3/4", "1", "1 1/4") == "1 1/4")

check("y se sigue respetando aunque se cambie el ancla dos veces",
      seguir_con_el_agujero("1", "1 1/8",
                            seguir_con_el_agujero("3/4", "1", "1 1/4")) == "1 1/4")

check("si el ancla nueva no se entiende, el agujero queda vacio y no a medias",
      seguir_con_el_agujero("3/4", "", "13/16") == "")

# ==========================================================================
#  EL CUADRO DE LIBRAMIENTOS, RENGLON POR RENGLON
# ==========================================================================
#  Las TRES columnas -J, K y L- transcritas de la tabla del usuario, en pulgadas, y
#  comparadas contra lo que devuelven las funciones. Es la comprobacion que encontro
#  el corrimiento de la version anterior: la J repetia el 120 del 1 1/2" en el
#  1 5/8" y arrastraba los dos siguientes, y la K hacia lo mismo.
#
#  Se escribe en PULGADAS y no en milimetros a proposito: asi se puede cotejar de un
#  vistazo con la tabla impresa, que es como esta. La conversion a mm la hace la
#  prueba, que es justo lo que hace el programa.
def distancia_minima_l_mm(diametro_mm):
    d = int(math.floor(diametro_mm + 0.5))
    tabla = [(13, 23), (16, 28), (19, 34), (22, 37), (25, 44), (29, 49),
             (32, 55), (35, 60), (38, 66), (41, 71), (44, 76), (48, 82),
             (51, 87), (57, 97), (64, 107), (70, 118), (76, 130), (89, 150),
             (102, 172)]
    for tope, valor in tabla:
        if d <= tope:
            return float(valor)
    return 1.7 * d


#  EL CUADRO, TRANSCRITO DEL ORIGINAL EN MILIMETROS.
#
#  Fuente: Hylsa, Estandar de Ingenieria ES-03-001, «LIBRAMIENTOS REQUERIDOS PARA
#  ANCLAS EN PLACAS BASE», pag. 5 de 5, 30-MAY-80. DIECINUEVE renglones.
#
#      D  -  DIAMETRO DEL ANCLA (mm)
#      J  -  DISTANCIA MINIMA ENTRE ANCLAS (mm)
#      K  -  DISTANCIA MINIMA DEL ANCLA AL CANTO RECORTADO DE LA PLACA (mm)
#      L  -  DISTANCIA MINIMA DE COLUMNA/CARTABON PARA ATORNILLAR (mm)
#
#  ─── POR QUE ESTA EN MILIMETROS Y NO EN PULGADAS ────────────────────────────────
#  Hubo una vuelta en la que este cuadro se transcribio de una hoja de calculo que
#  lo tenia en pulgadas, y esa transcripcion habia TIRADO el renglon de 48 mm
#  -1 7/8", que existe pero es raro-. Al tirarlo, los valores de 1 5/8" y 1 3/4"
#  quedaron corridos un renglon, y el port se «corrigio» contra ellos:
#
#      D                 original   transcripcion
#      1 5/8"  41 mm     J=120      J=130   <- el 130 es del 1 3/4"
#      1 3/4"  44 mm     J=130      J=150   <- el 150 es del 1 7/8"
#      1 7/8"  48 mm     J=150      (no estaba)
#
#  El original manda. Y la defensa contra que vuelva a pasar es contar los
#  renglones: si no son diecinueve, a la transcripcion le falta uno.
CUADRO = [
    ( 13,  40,  22,  23),
    ( 16,  45,  30,  28),
    ( 19,  60,  32,  34),
    ( 22,  65,  38,  37),
    ( 25,  75,  45,  44),
    ( 29,  90,  51,  49),
    ( 32,  95,  57,  55),
    ( 35, 105,  60,  60),
    ( 38, 120,  65,  66),
    ( 41, 120,  70,  71),
    ( 44, 130,  75,  76),
    ( 48, 150,  85,  82),
    ( 51, 150,  90,  87),
    ( 57, 170, 100,  97),
    ( 64, 195, 110, 107),
    ( 70, 210, 120, 118),
    ( 76, 225, 135, 130),
    ( 89, 270, 155, 150),
    (102, 300, 180, 172),
]

print("\n" + "=" * 78)
print("EL CUADRO DE LIBRAMIENTOS: LAS TRES COLUMNAS, RENGLON POR RENGLON")
print("=" * 78)

check("el cuadro transcrito tiene los DIECINUEVE renglones del original",
      len(CUADRO) == 19, f"{len(CUADRO)} renglones")

malos_j, malos_k, malos_l = [], [], []

for d, j, k, l in CUADRO:
    if separacion_minima_j_mm(d) != j:
        malos_j.append(f"{d} mm: {separacion_minima_j_mm(d):.0f} en vez de {j}")

    if distancia_minima_k_mm(d) != k:
        malos_k.append(f"{d} mm: {distancia_minima_k_mm(d):.0f} en vez de {k}")

    if distancia_minima_l_mm(d) != l:
        malos_l.append(f"{d} mm: {distancia_minima_l_mm(d):.0f} en vez de {l}")

check(f"columna J: los {len(CUADRO)} renglones coinciden con el original",
      not malos_j, "; ".join(malos_j))
check(f"columna K: los {len(CUADRO)} renglones coinciden con el original",
      not malos_k, "; ".join(malos_k))
check(f"columna L: los {len(CUADRO)} renglones coinciden con el original",
      not malos_l, "; ".join(malos_l))

#  ─── LOS TRES RENGLONES QUE LA TRANSCRIPCION A PULGADAS HABIA MOVIDO ────────────
#  Se fijan uno por uno y con su numero para que nadie los vuelva a «corregir». La
#  transcripcion decia J=130 en el 1 5/8" y J=150 en el 1 3/4": los valores del
#  renglon siguiente, porque le faltaba el de 48 mm.
check("el 1 5/8\" (41 mm) pide J = 120, no 130",
      separacion_minima_j_mm(41) == 120, f"dio {separacion_minima_j_mm(41):.0f}")
check("el 1 3/4\" (44 mm) pide J = 130, no 150",
      separacion_minima_j_mm(44) == 130, f"dio {separacion_minima_j_mm(44):.0f}")
check("y el 1 7/8\" (48 mm) EXISTE y pide J = 150",
      separacion_minima_j_mm(48) == 150, f"dio {separacion_minima_j_mm(48):.0f}")

check("el 1 5/8\" (41 mm) pide K = 70, no 75",
      distancia_minima_k_mm(41) == 70, f"dio {distancia_minima_k_mm(41):.0f}")
check("el 1 3/4\" (44 mm) pide K = 75, no 85",
      distancia_minima_k_mm(44) == 75, f"dio {distancia_minima_k_mm(44):.0f}")
check("y el 1 7/8\" (48 mm) pide K = 85",
      distancia_minima_k_mm(48) == 85, f"dio {distancia_minima_k_mm(48):.0f}")

check("el 1 5/8\" (41 mm) pide L = 71, no 76",
      distancia_minima_l_mm(41) == 71, f"dio {distancia_minima_l_mm(41):.0f}")
check("el 1 3/4\" (44 mm) pide L = 76, no 82",
      distancia_minima_l_mm(44) == 76, f"dio {distancia_minima_l_mm(44):.0f}")
check("y el 1 7/8\" (48 mm) pide L = 82",
      distancia_minima_l_mm(48) == 82, f"dio {distancia_minima_l_mm(48):.0f}")

#  Y EL PUENTE PULGADAS -> MILIMETROS, que es donde se cometio el error: cada medida
#  comercial en pulgadas tiene que caer en SU renglon del cuadro.
pares = [("1/2", 13), ("5/8", 16), ("3/4", 19), ("7/8", 22), ("1", 25),
         ("1 1/8", 29), ("1 1/4", 32), ("1 3/8", 35), ("1 1/2", 38),
         ("1 5/8", 41), ("1 3/4", 44), ("1 7/8", 48), ("2", 51),
         ("2 1/4", 57), ("2 1/2", 64), ("2 3/4", 70), ("3", 76),
         ("3 1/2", 89), ("4", 102)]

descuadres = [
    f'{p}" cae en el renglon de {d} y da J={separacion_minima_j_mm(pulgadas(p) * 25.4):.0f}, '
    f'no {separacion_minima_j_mm(d):.0f}'
    for p, d in pares
    if separacion_minima_j_mm(pulgadas(p) * 25.4) != separacion_minima_j_mm(d)]

check("cada diametro comercial en pulgadas cae en SU renglon del cuadro",
      not descuadres, "; ".join(descuadres))

#  La L NO es la K con otro nombre: en unos diametros es mayor y en otros menor, asi
#  que quedarse con una de las dos deja pasar los casos en los que manda la otra.
mayor_l = [d for d, j, k, l in CUADRO if l > k]
mayor_k = [d for d, j, k, l in CUADRO if k > l]

check("la L no se puede deducir de la K: a veces es mayor y a veces menor",
      len(mayor_l) >= 3 and len(mayor_k) >= 3,
      f"L mayor en {len(mayor_l)} renglones, K mayor en {len(mayor_k)}")

print("\n" + "=" * 78)
print("LA SEPARACION AL BORDE SE AJUSTA AL MINIMO DE LA COLUMNA K")
print("=" * 78)


def borde_minimo_cm(diam_ancla_cm):
    """La columna K, en cm: del ancla al canto recortado de la placa."""
    return 0.0 if diam_ancla_cm <= 0 else distancia_minima_k_mm(diam_ancla_cm * 10) / 10.0


def sep_borde_ajustada(sep_pedida_cm, diam_ancla_cm, dim_placa_cm):
    if sep_pedida_cm <= 0:
        return 0.0

    s = max(sep_pedida_cm, borde_minimo_cm(diam_ancla_cm))

    if dim_placa_cm > 0:
        tope = dim_placa_cm / 2.0 - 0.5
        if 0 < tope < s:
            s = tope

    return s


#  Un ancla de 3/4" son 19 mm, y la columna K pide 32 mm = 3.2 cm al canto de la placa.
#  OJO: la L de ese mismo diametro pide 34 mm. Que los dos numeros sean parecidos es
#  justo lo que hizo facil confundir las columnas, asi que aqui se comprueba que se
#  usa la K -3.2- y no la L -3.4-.
d34 = pulgadas("3/4") * 2.54

check("una separacion por debajo del minimo se sube al minimo de K",
      abs(sep_borde_ajustada(2.0, d34, 40) - 3.2) < 1e-9,
      f"dio {sep_borde_ajustada(2.0, d34, 40)}, se esperaba 3.2 (K)")

check("y NO al de L, que para ese diametro seria 3.4",
      abs(sep_borde_ajustada(2.0, d34, 40) - 3.4) > 1e-9)

check("una separacion que ya cumple no se toca",
      abs(sep_borde_ajustada(6.0, d34, 40) - 6.0) < 1e-9)

check("en cero se queda en cero, que significa «calculala»",
      sep_borde_ajustada(0, d34, 40) == 0)

#  Un ancla de 4" pide K = 180 mm = 18 cm. En una placa de 20 cm no cabe: el tope de
#  media placa manda, y quien avisa es la revision de la K.
d4 = pulgadas("4") * 2.54

check("si el minimo no cabe en la placa, gana el tope de media placa",
      abs(sep_borde_ajustada(5.0, d4, 20) - 9.5) < 1e-9,
      f"dio {sep_borde_ajustada(5.0, d4, 20)}")

#  Y el automatico tambien lo respeta: sin esto, dejar la celda en cero seria la
#  manera de saltarse el cuadro.
def sep_auto_con_borde(dim_placa, dim_perfil, d_agujero, escala, borde_libre=0.0):
    minimo = 0.5 * escala

    if 0 < dim_perfil < dim_placa:
        s = (dim_placa - dim_perfil) / 4.0
    else:
        s = 0.12 * dim_placa

    if s < d_agujero:
        s = d_agujero
    if borde_libre > 0 and s < borde_libre:
        s = borde_libre
    if s > dim_placa / 2 - minimo:
        s = dim_placa / 2 - minimo
    if s < minimo:
        s = minimo

    return s


#  Placa de 40 con perfil de 36: el sobrante da 1 cm, muy por debajo de los 3.4 que
#  pide un ancla de 3/4". Sin el borde libre saldria 1 cm.
check("con el sobrante por debajo del minimo, el automatico sube al minimo",
      abs(sep_auto_con_borde(40, 36, 2.06, 1, 3.4) - 3.4) < 1e-9,
      f"dio {sep_auto_con_borde(40, 36, 2.06, 1, 3.4)}")

check("y sin el minimo daria menos, que es el defecto que esto corrige",
      sep_auto_con_borde(40, 36, 2.06, 1, 0) < 3.4)

# ==========================================================================
#  LA FRANJA DE SOLDADURA: EL CONTORNO DESPLAZADO HACIA FUERA
# ==========================================================================
#  Port de ContornoDesplazado. Esto es el arreglo del defecto que se veia en el
#  dibujo: la franja se generaba con el RECTANGULO ENVOLVENTE del perfil crecido el
#  espesor, asi que en un perfil I no era una franja, era la caja entera rellena de
#  rayado con la I dentro como isla.
#
#  La prueba fuerte no es «el area crece»: es que CADA ARISTA desplazada quede a
#  distancia t de la suya. Eso es lo que hace que sea una franja de ancho constante,
#  o sea un filete, y es lo que un desplazamiento por la bisectriz NO cumple.
TOL = 1e-9


def area_con_signo(pts):
    n = len(pts) // 2
    suma = 0.0
    for i in range(n):
        j = (i + 1) % n
        suma += pts[2 * i] * pts[2 * j + 1] - pts[2 * j] * pts[2 * i + 1]
    return suma / 2


def _arista_atras(sirve, n, v):
    for k in range(1, n + 1):
        i = (v - k) % n
        if sirve[i]:
            return i
    return v


def _arista_adelante(sirve, n, v):
    for k in range(n):
        i = (v + k) % n
        if sirve[i]:
            return i
    return v


def hacia_fuera(pts, t):
    if pts is None or len(pts) < 6 or len(pts) % 2 != 0:
        return None
    if abs(t) <= TOL:
        return list(pts)

    area = area_con_signo(pts)
    if abs(area) <= TOL:
        return None

    n = len(pts) // 2
    sentido = 1.0 if area > 0 else -1.0

    dx = [0.0] * n
    dy = [0.0] * n
    sirve = [False] * n

    for i in range(n):
        j = (i + 1) % n
        ax = pts[2 * j] - pts[2 * i]
        ay = pts[2 * j + 1] - pts[2 * i + 1]
        largo = math.sqrt(ax * ax + ay * ay)
        if largo <= TOL:
            continue
        dx[i] = ax / largo
        dy[i] = ay / largo
        sirve[i] = True

    if sum(1 for x in sirve if x) < 2:
        return None

    salida = [0.0] * len(pts)

    for i in range(n):
        entra = _arista_atras(sirve, n, i)
        sale = _arista_adelante(sirve, n, i)

        a1x = pts[2 * i] + t * sentido * dy[entra]
        a1y = pts[2 * i + 1] - t * sentido * dx[entra]
        a2x = pts[2 * i] + t * sentido * dy[sale]
        a2y = pts[2 * i + 1] - t * sentido * dx[sale]

        cruz = dx[entra] * dy[sale] - dy[entra] * dx[sale]

        if abs(cruz) <= 1e-7:
            salida[2 * i] = a1x
            salida[2 * i + 1] = a1y
            continue

        u = ((a2x - a1x) * dy[sale] - (a2y - a1y) * dx[sale]) / cruz
        salida[2 * i] = a1x + u * dx[entra]
        salida[2 * i + 1] = a1y + u * dy[entra]

    return salida


def dentro(pts, x, y):
    """Punto en poligono, por cruces."""
    n = len(pts) // 2
    adentro = False
    for i in range(n):
        j = (i + 1) % n
        x1, y1 = pts[2 * i], pts[2 * i + 1]
        x2, y2 = pts[2 * j], pts[2 * j + 1]
        if (y1 > y) != (y2 > y):
            xc = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
            if x < xc:
                adentro = not adentro
    return adentro


def aristas_a_distancia(orig, desp, t):
    """Cada arista desplazada esta sobre la paralela a la suya, a distancia t hacia FUERA."""
    n = len(orig) // 2
    sentido = 1.0 if area_con_signo(orig) > 0 else -1.0
    peor = 0.0

    for i in range(n):
        j = (i + 1) % n
        ax = orig[2 * j] - orig[2 * i]
        ay = orig[2 * j + 1] - orig[2 * i + 1]
        largo = math.sqrt(ax * ax + ay * ay)
        if largo <= TOL:
            continue

        nx = sentido * (ay / largo)
        ny = -sentido * (ax / largo)

        # La recta desplazada pasa por (orig_i + t*n) con la misma direccion.
        px = orig[2 * i] + t * nx
        py = orig[2 * i + 1] + t * ny

        # Los DOS extremos de la arista desplazada tienen que estar en esa recta:
        # su distancia con signo a la recta es cero.
        for k in (i, j):
            d = (desp[2 * k] - px) * nx + (desp[2 * k + 1] - py) * ny
            peor = max(peor, abs(d))

    return peor


#  Un perfil I de verdad, en centimetros: peralte 20.4, patin 20.4, alma 0.73,
#  patin 1.11 -el W 8x31 del ejemplo-. Doce vertices, con CUATRO reflejos, que son
#  los que hacen que el rectangulo envolvente no sirva.
def perfil_i(xc, yc, h, b, tw, tf):
    x0, y0 = xc - b / 2, yc - h / 2
    return [
        x0, y0,
        x0 + b, y0,
        x0 + b, y0 + tf,
        x0 + b / 2 + tw / 2, y0 + tf,
        x0 + b / 2 + tw / 2, y0 + h - tf,
        x0 + b, y0 + h - tf,
        x0 + b, y0 + h,
        x0, y0 + h,
        x0, y0 + h - tf,
        x0 + b / 2 - tw / 2, y0 + h - tf,
        x0 + b / 2 - tw / 2, y0 + tf,
        x0, y0 + tf,
    ]


print("\n" + "=" * 78)
print("LA FRANJA DE SOLDADURA: EL CONTORNO DESPLAZADO")
print("=" * 78)

t_sold = 0.635          # 1/4" en cm
cuadro = [0, 0, 10, 0, 10, 10, 0, 10]

check("un cuadrado antihorario crece por los cuatro lados",
      hacia_fuera(cuadro, 1) == [-1, -1, 11, -1, 11, 11, -1, 11],
      f"{hacia_fuera(cuadro, 1)}")

#  EL SENTIDO NO PUEDE IMPORTAR: TrazoAcero entrega unas formas antihorarias y otras
#  horarias -el angulo y la canal se espejean-, asi que suponer un sentido desplazaria
#  la mitad de las formas hacia DENTRO.
cuadro_horario = [0, 0, 0, 10, 10, 10, 10, 0]
d_horario = hacia_fuera(cuadro_horario, 1)

check("y uno HORARIO tambien crece, no se encoge",
      area_con_signo(cuadro) > 0 and area_con_signo(cuadro_horario) < 0
      and abs(abs(area_con_signo(d_horario)) - 144) < 1e-9,
      f"area {abs(area_con_signo(d_horario))}, esperada 144")

#  Ahora el caso del defecto: el perfil I.
i_pts = perfil_i(0, 0, 20.4, 20.4, 0.73, 1.11)
i_desp = hacia_fuera(i_pts, t_sold)

check("el perfil I se desplaza: doce vertices, doce vertices",
      i_desp is not None and len(i_desp) == len(i_pts))

check("TODAS las aristas de la I quedan a distancia t exacta",
      aristas_a_distancia(i_pts, i_desp, t_sold) < 1e-9,
      f"peor desvio {aristas_a_distancia(i_pts, i_desp, t_sold):.2e}")

check("y el contorno desplazado CONTIENE al perfil, no al contrario",
      all(dentro(i_desp, i_pts[2 * k], i_pts[2 * k + 1]) for k in range(len(i_pts) // 2)))

#  Y LA COMPARACION CON LO QUE HABIA: el rectangulo envolvente crecido. La franja
#  buena tiene que ser MUCHO mas chica en area que la caja menos el perfil, porque la
#  caja de una I es casi toda aire.
area_i = abs(area_con_signo(i_pts))
area_franja = abs(area_con_signo(i_desp)) - area_i

caja = 20.4 + 2 * t_sold
area_caja_menos_i = caja * caja - area_i

check("la franja buena es mucho menor que la caja envolvente menos el perfil",
      area_franja < area_caja_menos_i / 3,
      f"franja {area_franja:.1f} cm2 contra {area_caja_menos_i:.1f} cm2 de la caja")

#  El ancho de la franja se puede estimar: area / perimetro ~ t. Con la caja no se
#  parece a nada.
perim_i = sum(
    math.hypot(i_pts[2 * ((k + 1) % 12)] - i_pts[2 * k],
               i_pts[2 * ((k + 1) % 12) + 1] - i_pts[2 * k + 1])
    for k in range(12))

check("y su ancho medio es el espesor de la soldadura",
      abs(area_franja / perim_i - t_sold) < 0.15 * t_sold,
      f"ancho medio {area_franja / perim_i:.3f} cm, espesor {t_sold:.3f} cm")

#  Las otras formas: una canal -tres reflejos-, un angulo -uno- y una te.
canal = [0, 0, 10, 0, 10, 1, 1.5, 1, 1.5, 9, 10, 9, 10, 10, 0, 10]
angulo = [0, 0, 10, 0, 10, 1.2, 1.2, 1.2, 1.2, 10, 0, 10]
te = [0, 9, 0, 10, 12, 10, 12, 9, 6.7, 9, 6.7, 0, 5.3, 0, 5.3, 9]

for nombre, forma in (("canal", canal), ("angulo", angulo), ("te", te)):
    d = hacia_fuera(forma, t_sold)

    check(f"la {nombre}: todas sus aristas a distancia t exacta",
          d is not None and aristas_a_distancia(forma, d, t_sold) < 1e-9,
          "no se pudo desplazar" if d is None
          else f"peor desvio {aristas_a_distancia(forma, d, t_sold):.2e}")

    check(f"y el contorno de la {nombre} contiene a su perfil",
          d is not None
          and all(dentro(d, forma[2 * k], forma[2 * k + 1])
                  for k in range(len(forma) // 2)))

#  Un contorno degenerado no revienta: se contesta que no se puede y el dibujante lo
#  dice en las notas en lugar de dibujar una franja al azar.
check("un contorno de area nula no se desplaza, se rechaza",
      hacia_fuera([0, 0, 5, 0, 10, 0], 1) is None)
check("y menos de tres puntos tampoco",
      hacia_fuera([0, 0, 1, 1], 1) is None)
check("un espesor cero devuelve el mismo contorno",
      hacia_fuera(cuadro, 0) == list(cuadro))

# ==========================================================================
#  LA FLECHA DE LA SOLDADURA APUNTA A LA SOLDADURA
# ==========================================================================
#  Port de ContornoDesplazado.PuntoIzquierdo.
#
#  El defecto: se tomaba la X mas chica de la franja y se le forzaba el CENTRO
#  VERTICAL de la pieza. En un perfil I eso es AIRE: la X mas chica es la punta del
#  patin, y a media altura por ahi no pasa el contorno, esta el hueco entre los dos
#  patines. La flecha acababa señalando a nada.
#
#  La prueba de verdad es geometrica: la punta tiene que caer DENTRO de la franja, o
#  sea entre el contorno del perfil y el contorno corrido el espesor. Se comprueba
#  con «punto en poligono» sobre los dos contornos: dentro del de fuera y fuera del
#  del perfil.
def punto_izquierdo(pts):
    if pts is None or len(pts) < 4 or len(pts) % 2 != 0:
        return (0.0, 0.0)

    n = len(pts) // 2
    xs = [pts[2 * i] for i in range(n)]
    min_x, max_x = min(xs), max(xs)
    holgura = 1e-9 + 1e-7 * max(1e-9, max_x - min_x)

    mejor_largo = -1.0
    mejor = (min_x, pts[1])

    for i in range(n):
        j = (i + 1) % n
        if pts[2 * i] > min_x + holgura or pts[2 * j] > min_x + holgura:
            continue
        largo = abs(pts[2 * j + 1] - pts[2 * i + 1])
        if largo > mejor_largo:
            mejor_largo = largo
            mejor = ((pts[2 * i] + pts[2 * j]) / 2,
                     (pts[2 * i + 1] + pts[2 * j + 1]) / 2)

    if mejor_largo > holgura:
        return mejor

    for i in range(n):
        if pts[2 * i] <= min_x + holgura:
            return (pts[2 * i], pts[2 * i + 1])

    return mejor


def punta_de_la_flecha(perfil, t):
    """Lo que hace el dibujante: el contorno corrido MEDIO espesor, por la izquierda."""
    medio = hacia_fuera(perfil, t / 2.0)
    return punto_izquierdo(medio if medio is not None else perfil)


print("\n" + "=" * 78)
print("LA FLECHA DE LA SOLDADURA CAE DENTRO DE LA FRANJA")
print("=" * 78)

t_f = 0.635

formas = {
    "perfil I": perfil_i(0, 0, 20.4, 20.4, 0.73, 1.11),
    "canal": canal,
    "angulo": angulo,
    "te": te,
}

for nombre, forma in formas.items():
    fuera = hacia_fuera(forma, t_f)
    px, py = punta_de_la_flecha(forma, t_f)

    check(f"la {nombre}: la punta cae DENTRO de la franja",
          dentro(fuera, px, py) and not dentro(forma, px, py),
          f"punta ({px:.3f}, {py:.3f})")

#  Y EL CASO CONCRETO QUE SE VEIA MAL, escrito con su numero. En el perfil I de
#  20.4 de peralte con patin de 1.11, el centro vertical esta en y = 0 y la punta
#  buena cae en el canto del patin, muy lejos de ahi.
i_pts2 = perfil_i(0, 0, 20.4, 20.4, 0.73, 1.11)
px_i, py_i = punta_de_la_flecha(i_pts2, t_f)

check("y en el perfil I NO se queda en el centro vertical, que es el hueco",
      abs(py_i) > 8.0,
      f"la punta quedo en y = {py_i:.3f}; el centro es y = 0")

#  EL DEFECTO, DEMOSTRADO. El punto que usaba la version anterior era (X minima de la
#  franja, centro vertical). Se reconstruye y se comprueba que NO esta dentro de la
#  franja: si esta comprobacion se pusiera en verde con el codigo viejo, no probaria
#  nada.
x_min_franja = min(hacia_fuera(i_pts2, t_f)[2 * k] for k in range(12))
punta_vieja = (x_min_franja, 0.0)

check("el punto que usaba la version anterior NO estaba en la franja",
      not dentro(hacia_fuera(i_pts2, t_f), punta_vieja[0], punta_vieja[1]),
      f"punta vieja ({punta_vieja[0]:.3f}, {punta_vieja[1]:.3f})")

check("y la nueva SI lo esta, con la misma medida",
      dentro(hacia_fuera(i_pts2, t_f), px_i, py_i),
      f"punta nueva ({px_i:.3f}, {py_i:.3f})")

#  El punto sale del EJE de la franja, no de su borde: apuntando al borde de fuera la
#  flecha queda justo sobre la linea, y al de dentro, encima del perfil.
borde_fuera = punto_izquierdo(hacia_fuera(i_pts2, t_f))
eje = punta_de_la_flecha(i_pts2, t_f)

check("la punta esta en el EJE de la franja, no en su borde exterior",
      abs(eje[0] - borde_fuera[0]) > t_f / 4,
      f"eje x={eje[0]:.4f}, borde x={borde_fuera[0]:.4f}")

#  Y en una forma sin arista vertical al minimo -una punta- se devuelve el vertice y
#  no se revienta.
rombo = [0, 5, 5, 0, 10, 5, 5, 10]

check("una forma en punta devuelve su vertice, sin reventar",
      punto_izquierdo(rombo) == (0, 5), f"{punto_izquierdo(rombo)}")

check("y un arreglo vacio o corto contesta el origen, sin excepcion",
      punto_izquierdo(None) == (0.0, 0.0) and punto_izquierdo([1, 2]) == (0.0, 0.0))

# ==========================================================================
#  LA COLUMNA L: HOLGURA PARA METER LA LLAVE
# ==========================================================================
#  Port de ContornoDesplazado.DistanciaAlContorno y de RevisarHolguraColumnaL.
#
#  L es «DISTANCIA MINIMA DE COLUMNA/CARTABON PARA ATORNILLAR»: el espacio entre el
#  ancla y el PAÑO DE LA COLUMNA para que entre la llave. En una vuelta anterior se
#  implemento como si fuera una segunda distancia al BORDE DE LA PLACA -o sea, como
#  la K con otro nombre-, y eso es medir contra lo contrario: en el croquis del
#  estandar el orden es canto de la placa -> K -> ancla -> L -> paño de la columna.
def distancia_al_segmento(x, y, x1, y1, x2, y2):
    dx, dy = x2 - x1, y2 - y1
    largo2 = dx * dx + dy * dy

    if largo2 <= TOL:
        return math.hypot(x - x1, y - y1)

    u = ((x - x1) * dx + (y - y1) * dy) / largo2
    u = max(0.0, min(1.0, u))

    return math.hypot(x - (x1 + u * dx), y - (y1 + u * dy))


def distancia_al_contorno(pts, x, y):
    if pts is None or len(pts) < 4 or len(pts) % 2 != 0:
        return float("inf")

    n = len(pts) // 2

    return min(distancia_al_segmento(x, y,
                                     pts[2 * i], pts[2 * i + 1],
                                     pts[2 * ((i + 1) % n)], pts[2 * ((i + 1) % n) + 1])
               for i in range(n))


print("\n" + "=" * 78)
print("LA COLUMNA L: HOLGURA DEL ANCLA A LA COLUMNA, PARA LA LLAVE")
print("=" * 78)

#  Un perfil I de 20.4 x 20.4 centrado en el origen: sus patines llegan a x = +-10.2.
col = perfil_i(0, 0, 20.4, 20.4, 0.73, 1.11)

#  UN ANCLA AL COSTADO, A MEDIA ALTURA. Aqui hay una trampa que conviene dejar
#  escrita: a media altura el perfil I NO tiene patin, tiene el hueco entre los dos.
#  Asi que el acero mas cercano a (-14, 0) no esta a 3.8 cm en horizontal -eso seria
#  el canto del patin si el patin llegara hasta ahi- sino en la ESQUINA del patin
#  inferior, (-10.2, -9.09), en diagonal:
#
#      sqrt(3.8^2 + 9.09^2) = 9.8523 cm
#
#  Es lo correcto y es lo que hay que medir: la holgura de la llave es contra el
#  acero que de verdad esta al lado, esté donde esté.
check("la distancia se mide al acero mas cercano, aunque quede en diagonal",
      abs(distancia_al_contorno(col, -14.0, 0.0) - 9.8523) < 1e-4,
      f"dio {distancia_al_contorno(col, -14.0, 0.0):.4f}, se esperaba 9.8523")

#  ESTA ES LA QUE IMPORTA: un ancla frente a la MITAD de un patin. Midiendo a los
#  vertices daria la distancia a la punta del patin -mucho mayor- y pasaria una
#  holgura que no existe.
#  El ancla en (0, -14) esta 2.9 cm por debajo del patin inferior (y = -10.2).
check("un ancla frente a la mitad de un patin mide contra el patin, no contra su punta",
      abs(distancia_al_contorno(col, 0.0, -14.0) - 3.8) < 1e-9,
      f"dio {distancia_al_contorno(col, 0.0, -14.0):.4f}")

#  Y un ancla en el hueco entre patines mide contra el ALMA, que es lo que tiene al
#  lado: el alma va de x = -0.365 a +0.365.
check("un ancla en el hueco entre patines mide contra el ALMA",
      abs(distancia_al_contorno(col, -5.0, 0.0) - 4.635) < 1e-9,
      f"dio {distancia_al_contorno(col, -5.0, 0.0):.4f}, se esperaba 4.635")


def revisar_holgura_l(anclas, contorno, escala=1.0):
    """(x, y, d_ancla_cm) -> None si todas cumplen, o el ancla que falla."""
    if contorno is None or len(contorno) < 6 or escala <= 0:
        return None

    for i, (x, y, d_cm) in enumerate(anclas):
        requerida = distancia_minima_l_mm(d_cm * 10)
        disponible = distancia_al_contorno(contorno, x, y) / escala * 10

        if disponible + 0.01 < requerida:
            return (i + 1, disponible, requerida)

    return None


#  Un ancla de 3/4" pide L = 34 mm = 3.4 cm de holgura a la columna.
d_anc = pulgadas("3/4") * 2.54

check("un ancla con holgura de sobra pasa",
      revisar_holgura_l([(-14.0, 0.0, d_anc)], col) is None,
      "9.85 cm disponibles contra 3.4 exigidos")

#  Y UNA QUE NO CABE: (0, -13) esta 2.8 cm por debajo de la cara del patin, y la L de
#  un ancla de 3/4" pide 3.4. Falta medio centimetro para la llave.
falla_l = revisar_holgura_l([(0.0, -13.0, d_anc)], col)

check("un ancla demasiado pegada a la columna se reporta, con sus dos numeros",
      falla_l is not None and abs(falla_l[1] - 28.0) < 1e-6
      and abs(falla_l[2] - 34.0) < 1e-6,
      f"{falla_l}")

#  Y SIN COLUMNA NO SE COMPRUEBA NADA: sin perfil dibujado no hay a que medirle la
#  holgura, y rechazar la placa por eso seria rechazarla por un dato que no existe.
check("sin columna no se comprueba la L",
      revisar_holgura_l([(0.0, 0.0, d_anc)], None) is None
      and revisar_holgura_l([(0.0, 0.0, d_anc)], [0, 0, 1, 1]) is None)

#  LA COMPROBACION QUE DISTINGUE LAS DOS LECTURAS. Con la placa de 40x40 del ejemplo
#  y anclas a 4.9 cm del borde -o sea en x = +-15.1-, la holgura a la columna es
#  15.1 - 10.2 = 4.9 cm, que cumple L. Si la L se midiera contra el BORDE DE LA
#  PLACA -la lectura equivocada- lo que se compararia serian esos 4.9 cm contra la
#  misma L y tambien pasaria: por eso el error no salto a la vista.
#
#  Se distinguen en una placa ANCHA con perfil chico: placa de 60, perfil de 20.4,
#  ancla a 3.5 cm del borde, o sea en x = -26.5.
#    - al borde de la placa  :  3.5 cm      <- lo que mediria la lectura equivocada
#    - a la columna          : 18.66 cm     <- lo que mide de verdad, en diagonal a
#                                              la esquina del patin
#  Cinco veces mas. Con una placa justa los dos numeros se parecen, y por eso la
#  confusion no salta a la vista; con una ancha, no hay manera de confundirlos.
col_chico = perfil_i(0, 0, 20.4, 20.4, 0.73, 1.11)
x_ancla = -(60 / 2 - 3.5)

d_a_columna = distancia_al_contorno(col_chico, x_ancla, 0.0)

check("en una placa ancha, la L mide contra la columna y no contra el borde",
      abs(d_a_columna - 18.6633) < 1e-4 and abs(d_a_columna - 3.5) > 1,
      f"dio {d_a_columna:.4f}; al borde serian 3.5")

# ==========================================================================
#  LA LISTA DE DIAMETROS DE ANCLA Y EL CUADRO SON LA MISMA COSA
# ==========================================================================
#  La lista que ofrece la celda «Ø ancla» tiene que ser exactamente los renglones del
#  cuadro de libramientos. Y no por prolijidad: si la celda ofreciera un diametro que
#  el cuadro no tiene, sus J, K y L se resolverian por el renglon inmediato superior
#  -que es lo prudente- pero SIN QUE NADA LO DIJERA, o sea que el usuario creeria
#  estar leyendo la fila de su ancla y estaria leyendo la de otra.
#
#  Se lee del propio codigo, no se copia aqui: copiada, esta prueba se quedaria vieja
#  el dia que alguien toque la lista, que es justo el dia que hace falta.
import os as _os

_fuente_fila = _os.path.join(
    _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))),
    "client", "src", "CadLink.App", "Models", "PlacaBaseRow.cs")

with open(_fuente_fila, encoding="utf-8") as _f:
    _cs = _f.read()

_ini = _cs.index("private static readonly string[] _diametrosAncla")
_fin = _cs.index("};", _ini)

DIAMETROS_CELDA = re.findall(r'"([^"]+)"', _cs[_ini:_fin])

print("\n" + "=" * 78)
print("LA LISTA DE DIAMETROS DE ANCLA CONTRA EL CUADRO")
print("=" * 78)

check(f"la celda ofrece los {len(CUADRO)} diametros del cuadro",
      len(DIAMETROS_CELDA) == len(CUADRO),
      f"la celda tiene {len(DIAMETROS_CELDA)} y el cuadro {len(CUADRO)}")

#  Cada entrada de la lista tiene que caer EXACTAMENTE en su renglon: se convierte a mm
#  y se compara contra el D del cuadro, en el mismo orden.
descuadres = []

for i, texto in enumerate(DIAMETROS_CELDA):
    if i >= len(CUADRO):
        break

    mm = pulgadas(texto) * 25.4
    d_cuadro = CUADRO[i][0]

    # El redondeo del programa: al milimetro nominal mas cercano.
    if int(math.floor(mm + 0.5)) != d_cuadro:
        descuadres.append(f'"{texto}" da {mm:.2f} mm y su renglon es {d_cuadro}')

check("y cada uno cae exactamente en su renglon del cuadro",
      not descuadres, "; ".join(descuadres))

#  Ninguno se puede quedar sin libramientos por caer fuera de la tabla: el ultimo
#  renglon es 102 mm y la lista acaba en 4" = 101.6, que redondea a 102.
fuera = [t for t in DIAMETROS_CELDA if int(math.floor(pulgadas(t) * 25.4 + 0.5)) > 102]

check("ninguno se sale de la tabla, o sea ninguno se extrapola",
      not fuera, f"se salen: {fuera}")

#  Y LOS ONCE QUE FALTABAN, nombrados. La lista vieja tenia ocho y se cortaba en
#  1 1/2": justo antes del tramo donde el cuadro se pone exigente.
faltaban = ["1 3/8", "1 5/8", "1 3/4", "1 7/8", "2", "2 1/4", "2 1/2", "2 3/4",
            "3", "3 1/2", "4"]

ausentes = [d for d in faltaban if d not in DIAMETROS_CELDA]

check("los once diametros que faltaban estan en la lista",
      not ausentes, f"siguen ausentes: {ausentes}")

#  Y el agujero automatico funciona para TODOS, incluidos los nuevos: es el ancla mas
#  1/16", y todos caen en dieciseisavos exactos.
sin_agujero = [d for d in DIAMETROS_CELDA if not agujero_automatico(d)]

check("todos tienen su agujero automatico, tambien los nuevos",
      not sin_agujero, f"sin agujero: {sin_agujero}")

raros = [f'{d}" -> {agujero_automatico(d)}' for d in DIAMETROS_CELDA
         if "/" in agujero_automatico(d)
         and not agujero_automatico(d).split("/")[-1] == "16"]

check("y el agujero de todos cae en dieciseisavos, sin decimales raros",
      not raros, "; ".join(raros))

print("\n" + "=" * 78)
if fallos:
    print(f"ATENCION: {len(fallos)} comprobacion(es) fallaron.")
    for f in fallos:
        print(f"  - {f}")
else:
    print("OK: la logica pura de la placa base coincide con la macro.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
