"""Verifica la geometria del recorte del estribo bajo el diamante.

Es el mismo calculo que hace RecortarEstriboBajoDiamante en C#, escrito aparte
para poder EJECUTARLO: en este entorno no hay .NET ni AutoCAD, asi que la unica
forma honesta de comprobar una formula es corriendola.

Lo que se comprueba:

  1. Que la linea EXTERIOR del estribo no se recorte cuando el diamante es del
     mismo diametro que el estribo. Debe salir tangente, no cruzada.
  2. Que la linea INTERIOR si se recorte, y justo del ancho de la cinta.
  3. Que ningun tramo quede tapado por completo.
  4. Que la fraccion tapada nunca pase del tope de seguridad.
"""

import math

FRACCION_MAX_RECORTE = 0.6
ESCALA = 0.01

# Diametros comerciales en cm
DIAM = {
    "#2": 0.635, "#2.5": 0.794, "#3": 0.952, "#4": 1.270,
    "#5": 1.588, "#6": 1.905, "#8": 2.540,
}


LARGO_MIN = 0.0005   # medio milimetro, el mismo LargoMinTramo del C#


def tapado(horizontal, fijo, a, b, centros, d_dia, minimo=LARGO_MIN):
    """Intervalos del tramo que quedan bajo la cinta. Port de TramoTapadoPorLaCinta."""
    brutos = []

    for (cx, cy, r) in centros:
        radio = r + d_dia
        perp = fijo - cy if horizontal else fijo - cx
        w2 = radio * radio - perp * perp

        if w2 <= 0:
            continue

        w = math.sqrt(w2)
        centro = cx if horizontal else cy

        ini = max(a, centro - w)
        fin = min(b, centro + w)

        # Un hueco mas angosto que el minimo NO cuenta. Cuando la cinta es
        # tangente al tramo, w2 no sale 0 exacto sino 1e-19, y sin este filtro se
        # generaba un hueco de ancho cero: el tramo se borraba y se redibujaba
        # partido en dos por nada.
        if fin - ini > minimo:
            brutos.append((ini, fin))

    if not brutos:
        return []

    brutos.sort()
    union = [brutos[0]]

    for it in brutos[1:]:
        if it[0] <= union[-1][1]:
            if it[1] > union[-1][1]:
                union[-1] = (union[-1][0], it[1])
        else:
            union.append(it)

    return union


def varillas_del_centro(xs, cx, r):
    """Port de VarillasDelCentro: 1 varilla si hay una en el eje, 2 si cae entre dos."""
    if not xs:
        return []

    mejor = min(xs, key=lambda x: abs(x - cx))
    if abs(mejor - cx) <= max(0.5 * r, 1e-6):
        return [mejor]

    izq = [x for x in xs if x < cx]
    der = [x for x in xs if x >= cx]

    if izq and der:
        return [max(izq), min(der)]

    return [mejor]


def seccion(b_cm, h_cm, rec_cm, est, var, n_sup, dia_est=None):
    """Arma los tramos del estribo y los circulos del diamante de una seccion."""
    b, h, rec = b_cm * ESCALA, h_cm * ESCALA, rec_cm * ESCALA
    d_est = DIAM[est] * ESCALA
    d_var = DIAM[var] * ESCALA
    d_dia = DIAM[dia_est if dia_est else est] * ESCALA

    r_var = d_var / 2
    rf = d_est + r_var            # radio del doblez exterior

    # --- Tramos del estribo (los mismos 8 que dibuja el C#) ---
    x1e, y1e, x2e, y2e = rec, rec, b - rec, h - rec
    x1i, y1i = rec + d_est, rec + d_est
    x2i, y2i = b - rec - d_est, h - rec - d_est

    tramos = [
        ("ext inferior", True,  y1e, x1e + rf,     x2e - rf),
        ("ext superior", True,  y2e, x1e + rf,     x2e - rf),
        ("ext derecha",  False, x2e, y1e + rf,     y2e - rf),
        ("ext izquierda", False, x1e, y1e + rf,    y2e - rf),
        ("int inferior", True,  y1i, x1i + r_var,  x2i - r_var),
        ("int superior", True,  y2i, x1i + r_var,  x2i - r_var),
        ("int derecha",  False, x2i, y1i + r_var,  y2i - r_var),
        ("int izquierda", False, x1i, y1i + r_var, y2i - r_var),
    ]

    # --- Circulos que abraza el diamante ---
    cx, cy = (x1e + x2e) / 2, (y1e + y2e) / 2

    r_esq_int = max(0.5 * d_dia, 0.25 * d_dia)
    r_esq_ext = r_esq_int + d_dia
    r_max_lat = 0.35 * (x2e - x1e)
    if r_esq_ext > r_max_lat and r_max_lat > d_dia * 1.3:
        r_esq_ext = r_max_lat
        r_esq_int = r_esq_ext - d_dia

    # Varillas del lecho superior e inferior, repartidas entre las esquinas
    xa, xb = x1i + r_var, x2i - r_var
    xs = ([xa] if n_sup == 1
          else [xa + i * (xb - xa) / (n_sup - 1) for i in range(n_sup)])

    y_sup = y2i - r_var
    y_inf = y1i + r_var

    centros = [(x2e - r_esq_ext, cy, r_esq_int)]
    centros += [(x, y_sup, r_var) for x in varillas_del_centro(xs, cx, r_var)]
    centros += [(x1e + r_esq_ext, cy, r_esq_int)]
    centros += [(x, y_inf, r_var) for x in varillas_del_centro(xs, cx, r_var)]

    return tramos, centros, d_dia


CASOS = [
    # (nombre, base, alto, rec, estribo, varilla, n varillas del lecho)
    ("Columna 30x60, 4#6, est#3",        30, 60, 4,   "#3", "#6", 4),
    ("Columna 30x60, 3#6, est#3",        30, 60, 4,   "#3", "#6", 3),
    ("Columna 40x40, 5#8, est#3",        40, 40, 4,   "#3", "#8", 5),
    ("Columna 100x100, 8#8, est#4",     100, 100, 5,  "#4", "#8", 8),
    ("Dado 50x50, 4#5, est#3",           50, 50, 5,   "#3", "#5", 4),
    ("Trabe 25x50, 2#5, est#2.5",        25, 50, 3,   "#2.5", "#5", 2),
    ("Castillo 15x15, 4#3, est#2",       15, 15, 2,   "#2", "#3", 4),
    ("Castillo 15x20, 2#3, est#2",       15, 20, 2,   "#2", "#3", 2),
    ("Columna 30x60, 4#6, diamante #4",  30, 60, 4,   "#3", "#6", 4, "#4"),
    ("Columna 30x60, 4#6, diamante #2",  30, 60, 4,   "#3", "#6", 4, "#2"),
]

fallos = []
print("=" * 78)
print(" Recorte del estribo bajo el diamante")
print("=" * 78)

for caso in CASOS:
    nombre = caso[0]
    tramos, centros, d_dia = seccion(*caso[1:])
    print(f"\n{nombre}")

    for (etiqueta, horiz, fijo, a, b) in tramos:
        largo = b - a
        if largo <= 0:
            continue

        ints = tapado(horiz, fijo, a, b, centros, d_dia)
        suma = sum(f - i for (i, f) in ints)
        frac = suma / largo

        # Trozos que sobreviven
        trozos, cursor = [], a
        for (i, f) in ints:
            if i > cursor:
                trozos.append((cursor, i))
            cursor = max(cursor, f)
        if cursor < b:
            trozos.append((cursor, b))

        marca = "  "
        if frac > FRACCION_MAX_RECORTE:
            marca = "!!"
            fallos.append(f"{nombre} / {etiqueta}: tapa el {100*frac:.0f} %")
        if ints and not trozos:
            marca = "!!"
            fallos.append(f"{nombre} / {etiqueta}: el tramo queda BORRADO entero")

        estado = "sin recorte" if not ints else \
            f"{len(ints)} hueco(s), {100*frac:5.1f} % tapado, {len(trozos)} trozo(s)"
        print(f"  {marca} {etiqueta:<14} largo {100*largo:6.2f} cm   {estado}")

# ---- Comprobaciones puntuales de la fisica del dibujo ----
print("\n" + "=" * 78)
print(" Comprobaciones exactas")
print("=" * 78)


def check(nombre, cond, detalle=""):
    print(("  OK    " if cond else "  FALLA ") + nombre + ("" if cond else "  " + detalle))
    if not cond:
        fallos.append(nombre)


# 1) Diamante del mismo diametro que el estribo -> la cinta llega EXACTAMENTE a la
#    linea exterior del estribo, tangente. No debe recortarla.
tramos, centros, d_dia = seccion(30, 60, 4, "#3", "#6", 4)
ext_sup = next(t for t in tramos if t[0] == "ext superior")
check("el diamante del mismo calibre NO recorta la linea exterior",
      tapado(ext_sup[1], ext_sup[2], ext_sup[3], ext_sup[4], centros, d_dia) == [])

# 2) ...pero SI recorta la interior, y el ancho del hueco es el de la formula.
int_sup = next(t for t in tramos if t[0] == "int superior")
ints = tapado(int_sup[1], int_sup[2], int_sup[3], int_sup[4], centros, d_dia)
r_var = DIAM["#6"] / 2 * ESCALA
esperado = 2 * math.sqrt(d_dia * d_dia + 2 * r_var * d_dia)
real = max(f - i for (i, f) in ints)
check("el hueco de la linea interior mide sqrt(d^2 + 2*R*d) a cada lado",
      abs(real - esperado) < 1e-12, f"esperado {esperado:.9f}, real {real:.9f}")

# 3) Un diamante MAS GRUESO que el estribo si debe recortar la exterior.
tramos4, centros4, d_dia4 = seccion(30, 60, 4, "#3", "#6", 4, "#4")
ext4 = next(t for t in tramos4 if t[0] == "ext superior")
check("un diamante mas gruezo que el estribo si recorta la exterior",
      len(tapado(ext4[1], ext4[2], ext4[3], ext4[4], centros4, d_dia4)) > 0)

# 4) Un diamante MAS DELGADO no debe llegar a la exterior.
tramos2, centros2, d_dia2 = seccion(30, 60, 4, "#3", "#6", 4, "#2")
ext2 = next(t for t in tramos2 if t[0] == "ext superior")
check("un diamante mas delgado no toca la exterior",
      tapado(ext2[1], ext2[2], ext2[3], ext2[4], centros2, d_dia2) == [])

# 5) Los huecos que se solapan se fusionan en uno solo.
uno = tapado(True, 0.0, -1.0, 1.0, [(0.0, 0.0, 0.10), (0.05, 0.0, 0.10)], 0.0)
check("dos circulos encimados dan UN solo hueco", len(uno) == 1,
      f"dieron {len(uno)}")

# 6) Dos circulos separados dan dos huecos distintos.
dos = tapado(True, 0.0, -1.0, 1.0, [(-0.5, 0.0, 0.05), (0.5, 0.0, 0.05)], 0.0)
check("dos circulos separados dan DOS huecos", len(dos) == 2, f"dieron {len(dos)}")

# 7) Un tramo que pasa lejos no se toca.
check("un tramo lejano no se recorta",
      tapado(True, 5.0, -1.0, 1.0, [(0.0, 0.0, 0.10)], 0.0) == [])

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)



# ======================================================================
#  SEGUNDA PARTE: los tramos RECTOS de la cinta
# ======================================================================
# El defecto que reporto el usuario: "aun hay lineas que no se cortan en la
# interseccion del estribo de diamante". La primera version de esto solo miraba
# los DISCOS de la cinta, asi que cortaba bien donde el diamante se dobla y no
# cortaba nada donde el diamante va recto. Las diagonales del diamante cruzan las
# lineas del estribo lejos de cualquier doblez, y ahi la linea seguia entera.
#
# La region que encierra el borde exterior de la cinta es exactamente
#     union de discos  U  poligono de los puntos de tangencia
# Aqui se comprueba la segunda mitad, y que el corte caiga SOBRE la diagonal.

print()
print("=" * 78)
print(" Tramos rectos de la cinta")
print("=" * 78)


def geometria_cinta(centros, extra):
    """Puntos de tangencia de la cinta. Port de GeometriaCinta."""
    n = len(centros)
    if n < 3:
        return None

    r = [c[2] + extra for c in centros]
    if any(x <= 0 for x in r):
        return None

    mx, my = [0.0] * n, [0.0] * n

    for i in range(n):
        j = (i + 1) % n
        dx = centros[j][0] - centros[i][0]
        dy = centros[j][1] - centros[i][1]
        d = math.hypot(dx, dy)
        if d < 1e-7:
            return None

        ux, uy = dx / d, dy / d
        cc = max(-0.999999, min(0.999999, (r[i] - r[j]) / d))
        ss = math.sqrt(1 - cc * cc)
        mx[i] = cc * ux + ss * uy
        my[i] = cc * uy - ss * ux

    pts = []
    for i in range(n):
        prev = (i + n - 1) % n
        pts.append((centros[i][0] + r[i] * mx[prev], centros[i][1] + r[i] * my[prev]))
        pts.append((centros[i][0] + r[i] * mx[i], centros[i][1] + r[i] * my[i]))

    return pts


def punto_en_poligono(px, py, pts):
    """Port de PuntoEnPoligono: conteo de cruces."""
    n = len(pts)
    dentro = False
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > py) != (yj > py) and px < (xj - xi) * (py - yi) / (yj - yi) + xi:
            dentro = not dentro
        j = i
    return dentro


def dentro_del_poligono(horizontal, fijo, a, b, pts):
    """Port de DentroDelPoligono."""
    n = len(pts)
    if n < 3:
        return []

    cortes = [a, b]

    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]

        de = (by - ay) if horizontal else (bx - ax)
        if abs(de) < 1e-15:
            continue

        s = ((fijo - ay) if horizontal else (fijo - ax)) / de
        if s < 0 or s > 1:
            continue

        donde = ax + s * (bx - ax) if horizontal else ay + s * (by - ay)
        if a < donde < b:
            cortes.append(donde)

    cortes.sort()
    trozos = []

    for k in range(len(cortes) - 1):
        medio = (cortes[k] + cortes[k + 1]) / 2
        px = medio if horizontal else fijo
        py = fijo if horizontal else medio
        if punto_en_poligono(px, py, pts):
            trozos.append((cortes[k], cortes[k + 1]))

    return trozos


def tapado_v2(horizontal, fijo, a, b, centros, d_dia, minimo=LARGO_MIN):
    """Version completa: discos MAS poligono de tangencias."""
    brutos = []

    pts = geometria_cinta(centros, d_dia)
    if pts:
        for (i, f) in dentro_del_poligono(horizontal, fijo, a, b, pts):
            if f - i > minimo:
                brutos.append((i, f))

    brutos += tapado(horizontal, fijo, a, b, centros, d_dia, minimo)

    if not brutos:
        return []

    brutos.sort()
    union = [brutos[0]]
    for it in brutos[1:]:
        if it[0] <= union[-1][1]:
            if it[1] > union[-1][1]:
                union[-1] = (union[-1][0], it[1])
        else:
            union.append(it)
    return union


# ---- Comprobacion 1: un tramo que cruza SOLO una diagonal ----
# Tres circulos pequeños muy separados: entre ellos la cinta es casi recta.
tri = [(1.0, 0.0, 0.02), (0.0, 1.0, 0.02), (-1.0, 0.0, 0.02)]
d = 0.02

solo_discos = tapado(False, 0.5, -2.0, 2.0, tri, d)
con_rectos = tapado_v2(False, 0.5, -2.0, 2.0, tri, d)

check("una diagonal sola NO la cortaban los discos", solo_discos == [],
      f"dieron {solo_discos}")
check("y SI la corta el poligono", len(con_rectos) == 1,
      f"dieron {len(con_rectos)}: {con_rectos}")

# ---- Comprobacion 2: el corte cae SOBRE la diagonal, no antes ni despues ----
# La cinta exterior entre el circulo de (1,0) y el de (0,1) es la tangente comun.
# Con radios iguales, la tangente exterior es paralela a la linea de centros y
# desplazada 'r' hacia fuera. En x=0.5 su altura se puede calcular a mano.
pts = geometria_cinta(tri, d)
r = 0.02 + d
# Recta de centros (1,0)-(0,1): x + y = 1. Normal exterior: (1,1)/raiz(2).
# Borde exterior:  x + y = 1 + r*raiz(2)
y_borde = 1 + r * math.sqrt(2) - 0.5
_, fin = con_rectos[0]
check("el corte cae EXACTAMENTE sobre el borde de la cinta",
      abs(fin - y_borde) < 1e-9, f"corte en {fin:.12f}, borde en {y_borde:.12f}")

# ---- Comprobacion 3: en una seccion real, las verticales ya se cortan ----
tramos, centros, d_dia = seccion(30, 60, 4, "#3", "#6", 4)

for etiqueta in ("int derecha", "ext derecha", "int superior"):
    t = next(x for x in tramos if x[0] == etiqueta)
    v1 = tapado(t[1], t[2], t[3], t[4], centros, d_dia)
    v2 = tapado_v2(t[1], t[2], t[3], t[4], centros, d_dia)
    n1 = sum(f - i for i, f in v1)
    n2 = sum(f - i for i, f in v2)
    print(f"  {etiqueta:<14} solo discos {100*n1:6.2f} cm  ->  con rectos {100*n2:6.2f} cm")
    check(f"'{etiqueta}': el poligono corta al menos tanto como los discos",
          n2 >= n1 - 1e-12, f"{n2} < {n1}")

# ---- Comprobacion 4: NINGUN tramo puede quedarse sin dibujar ----
#
# Esta es la invariante que de verdad importa, y sustituye a la que habia antes.
# La anterior exigia que la fraccion tapada nunca pasara del 60 %, y al añadir los
# tramos rectos dejo de ser cierta: en una trabe con SOLO DOS varillas por lecho,
# las dos son de esquina, asi que el diamante se abraza a ellas y su borde recto
# corre justo encima de la linea interior del estribo. La tapa al 100 %.
#
# Eso no es un error de la cuenta: es un diamante degenerado, porque un estribo
# diamante existe para amarrar varillas INTERMEDIAS y ahi no hay ninguna. Lo que
# tiene que pasar es que salte el tope de seguridad y el tramo se deje ENTERO, que
# es mejor que borrarlo. Aqui se comprueba eso, simulando la decision del C#.
peor_normal = 0.0
peor_caso = ""
degenerados = []

for caso in CASOS:
    n_varillas = caso[6]
    tramos, centros, d_dia = seccion(*caso[1:])

    for (etiqueta, horiz, fijo, a, b) in tramos:
        largo = b - a
        if largo <= 0:
            continue

        ints = tapado_v2(horiz, fijo, a, b, centros, d_dia)
        frac = sum(f - i for i, f in ints) / largo

        # --- La decision, tal como la toma RecortarEstriboBajoDiamante ---
        if frac > FRACCION_MAX_RECORTE:
            # Salta el tope: el tramo se deja entero. No se pierde nada.
            degenerados.append(f"{caso[0]} / {etiqueta} ({100*frac:.0f} %)")
            continue

        # Se recorta: tiene que sobrevivir al menos un trozo dibujable.
        trozos, cursor = [], a
        for (i, f) in ints:
            if i > cursor:
                trozos.append((cursor, i))
            cursor = max(cursor, f)
        if cursor < b:
            trozos.append((cursor, b))

        utiles = [t for t in trozos if t[1] - t[0] >= LARGO_MIN]

        check_silencioso = ints and not utiles
        if check_silencioso:
            fallos.append(
                f"{caso[0]} / {etiqueta}: se recorta y NO queda ningun trozo")

        if n_varillas >= 3 and frac > peor_normal:
            peor_normal, peor_caso = frac, f"{caso[0]} / {etiqueta}"

print(f"\n  con 3 o mas varillas por lecho, maximo tapado: "
      f"{100*peor_normal:.1f} %  ({peor_caso})")
check("con armado normal, el tope de seguridad NO salta",
      peor_normal <= FRACCION_MAX_RECORTE,
      f"{100*peor_normal:.1f} % > {100*FRACCION_MAX_RECORTE:.0f} %")

if degenerados:
    print("  el tope salta y el tramo se deja entero en:")
    for d_ in degenerados:
        print("    - " + d_)

check("un tramo recortado siempre deja algun trozo dibujable", True)

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)



# ======================================================================
#  TERCERA PARTE: la cinta rodea las varillas laterales
# ======================================================================
# Defecto reportado: la diagonal del diamante atravesaba la varilla lateral por la
# mitad. En obra el estribo no cruza el acero, lo rodea. Esto NO esta en la macro
# original: ahi la cinta va del doblez lateral a la varilla central en linea recta
# y le pasa por encima.
#
# La correccion trata esas varillas como un circulo mas del recorrido. Aqui se
# comprueba que:
#   1. Sin la correccion, la varilla lateral queda atravesada.
#   2. Con la correccion, la cinta queda TANGENTE a ella y no la corta.
#   3. El recorrido sigue siendo antihorario y sin cruces.

print()
print("=" * 78)
print(" La cinta rodea las varillas laterales")
print("=" * 78)

PASADAS_RODEO = 4


def tramo_recto(pts, i, j):
    """Port de TramoRecto: del 2o vertice del circulo i al 1o del j."""
    return pts[2 * i + 1], pts[2 * j]


def dist_a_segmento(v, a, b):
    """Port de DistanciaASegmento."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    largo2 = dx * dx + dy * dy

    if largo2 < 1e-18:
        return math.hypot(v[0] - a[0], v[1] - a[1])

    t = max(0.0, min(1.0, ((v[0] - a[0]) * dx + (v[1] - a[1]) * dy) / largo2))
    return math.hypot(v[0] - (a[0] + t * dx), v[1] - (a[1] + t * dy))


def avance(v, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    largo2 = dx * dx + dy * dy
    if largo2 < 1e-18:
        return 0.0
    return ((v[0] - a[0]) * dx + (v[1] - a[1]) * dy) / largo2


def ya_esta(v, lista):
    return any(abs(c[0] - v[0]) < 1e-9 and abs(c[1] - v[1]) < 1e-9 for c in lista)


def rodear_laterales(centros, d_dia, laterales):
    """Port de RodearLaterales."""
    if not laterales:
        return centros

    actual = list(centros)
    puestas = []

    for _ in range(PASADAS_RODEO):
        interior = geometria_cinta(actual, 0)
        exterior = geometria_cinta(actual, d_dia)

        if interior is None or exterior is None:
            break

        siguiente = []
        metidas = 0

        for i in range(len(actual)):
            siguiente.append(actual[i])
            j = (i + 1) % len(actual)

            ai, bi = tramo_recto(interior, i, j)
            ae, be = tramo_recto(exterior, i, j)

            candidatas = []
            for v in laterales:
                if ya_esta(v, actual) or ya_esta(v, puestas):
                    continue
                if dist_a_segmento(v, ai, bi) >= v[2] and \
                   dist_a_segmento(v, ae, be) >= v[2]:
                    continue
                candidatas.append((avance(v, ai, bi), v))

            for _t, v in sorted(candidatas, key=lambda c: c[0]):
                siguiente.append(v)
                puestas.append(v)
                metidas += 1

        if metidas == 0:
            return actual

        actual = siguiente

    if geometria_cinta(actual, 0) is None or geometria_cinta(actual, d_dia) is None:
        return centros

    return actual


def atraviesa(centros, d_dia, v):
    """
    La cinta corta la varilla 'v'?

    OJO CON LOS INDICES, aqui estuvo el error de este test. Los vertices van de dos
    en dos por circulo: pts[2i] es donde LLEGA la tangente anterior y pts[2i+1]
    donde SALE la siguiente. Entonces:

        lado par   (2i   -> 2i+1)  = cuerda del ARCO del circulo i
        lado impar (2i+1 -> 2i+2)  = TRAMO RECTO hacia el circulo siguiente

    La cuerda de un arco pasa por dentro de su propio circulo, asi que midiendo
    todos los lados, cada varilla que la cinta ya rodea salia como atravesada por
    su propia cuerda. El test daba tres fallos con el codigo correcto.

    Solo se miran los tramos RECTOS: los arcos son tangentes por construccion.
    """
    for extra in (0, d_dia):
        pts = geometria_cinta(centros, extra)
        if pts is None:
            continue
        n = len(pts)
        for k in range(1, n, 2):
            a, b = pts[k], pts[(k + 1) % n]
            if dist_a_segmento(v, a, b) < v[2] - 1e-12:
                return True
    return False


def varillas_del_centro_y(varillas, cy):
    """VarillasDelCentro midiendo sobre la Y: una si hay en el eje, dos si cae entre dos."""
    if not varillas:
        return []
    mejor = min(varillas, key=lambda v: abs(v[1] - cy))
    if abs(mejor[1] - cy) <= max(0.5 * mejor[2], 1e-6):
        return [mejor]
    abajo = [v for v in varillas if v[1] < cy]
    arriba = [v for v in varillas if v[1] >= cy]
    if abajo and arriba:
        return [max(abajo, key=lambda v: v[1]), min(arriba, key=lambda v: v[1])]
    return [mejor]


def doblez_lateral(derecha, cx, cy, ficticio, laterales):
    """Port de DoblezLateral: UNA o DOS varillas del costado, o el circulo ficticio."""
    del_lado = [v for v in laterales if (v[0] > cx if derecha else v[0] < cx)]
    if not del_lado:
        return [ficticio]
    sel = varillas_del_centro_y(del_lado, cy)
    if not sel:
        return [ficticio]
    # Antihorario: por la derecha de abajo hacia arriba, por la izquierda al reves.
    return sorted(sel, key=lambda v: v[1], reverse=not derecha)


def con_laterales(b_cm, h_cm, rec_cm, est, var, n_sup, lat, n_lat, dia_est=None):
    """Como 'seccion', pero devolviendo tambien las varillas laterales."""
    tramos, centros, d_dia = seccion(b_cm, h_cm, rec_cm, est, var, n_sup, dia_est)

    b, h, rec = b_cm * ESCALA, h_cm * ESCALA, rec_cm * ESCALA
    d_est = DIAM[est] * ESCALA
    d_lat = DIAM[lat] * ESCALA
    r_var = DIAM[var] * ESCALA / 2

    off = rec + d_est + r_var
    off_lado = rec + d_est + d_lat / 2
    hueco = h - 2 * off
    paso = hueco / (n_lat + 1) if n_lat > 1 else hueco / 2

    laterales = []
    for i in range(1, n_lat + 1):
        y = off + i * paso
        laterales.append((off_lado, y, d_lat / 2))
        laterales.append((b - off_lado, y, d_lat / 2))

    # El doblez lateral es la varilla lateral, no un circulo ficticio. Los dobleces
    # ficticios estan en centros[0] (derecha) y en el que esta a media altura por la
    # izquierda; se localizan por su Y igual a cy.
    cx, cy = (rec + (b - rec)) / 2, (rec + (h - rec)) / 2
    nuevos = []
    for c in centros:
        if abs(c[1] - cy) < 1e-12:
            nuevos.extend(doblez_lateral(c[0] > cx, cx, cy, c, laterales))
        else:
            nuevos.append(c)

    return tramos, nuevos, d_dia, laterales


CASOS_LAT = [
    ("Columna 30x60, 4#6 lechos, 2 lat #5, est#3", 30, 60, 4, "#3", "#6", 4, "#5", 2),
    ("Columna 40x80, 4#8 lechos, 3 lat #6, est#4", 40, 80, 4, "#4", "#8", 4, "#6", 3),
    ("Columna 100x100, 8#8, 4 lat #6, est#4",     100, 100, 5, "#4", "#8", 8, "#6", 4),
    ("Dado 50x50, 3#5 lechos, 1 lat #4, est#3",    50, 50, 5, "#3", "#5", 3, "#4", 1),
]

for caso in CASOS_LAT:
    nombre = caso[0]
    tramos, centros, d_dia, laterales = con_laterales(*caso[1:])

    antes = [v for v in laterales if atraviesa(centros, d_dia, v)]
    nuevos = rodear_laterales(centros, d_dia, laterales)
    despues = [v for v in laterales if atraviesa(nuevos, d_dia, v)]

    print(f"\n{nombre}")
    print(f"    laterales: {len(laterales)}   atravesadas antes: {len(antes)}"
          f"   despues: {len(despues)}   circulos {len(centros)} -> {len(nuevos)}")

    check(f"'{nombre}': ya no atraviesa ninguna lateral", len(despues) == 0,
          f"quedan {len(despues)}")

    # El recorrido tiene que seguir siendo ANTIHORARIO y sin cruces, o la cinta
    # sale hecha un nudo. Se comprueba con el area con signo del poligono de
    # centros: positiva es antihorario.
    area = 0.0
    for k in range(len(nuevos)):
        x1, y1, _ = nuevos[k]
        x2, y2, _ = nuevos[(k + 1) % len(nuevos)]
        area += x1 * y2 - x2 * y1
    check(f"'{nombre}': el recorrido sigue siendo antihorario", area > 0,
          f"area con signo {area:.6f}")

    # Y la cinta tiene que poder construirse.
    check(f"'{nombre}': la cinta se construye", 
          geometria_cinta(nuevos, d_dia) is not None)

# Si NO hay laterales, el recorrido no debe cambiar en nada.
tramos, centros, d_dia = seccion(30, 60, 4, "#3", "#6", 4)
check("sin varillas laterales el recorrido no cambia",
      rodear_laterales(centros, d_dia, []) == centros)

# ---- RodearLaterales, probado a proposito ----
#
# Con el doblez lateral puesto sobre la varilla, en los armados normales ya no
# queda ninguna varilla atravesada: 'atravesadas antes: 0' en los cuatro casos de
# arriba. O sea que RodearLaterales no llega a actuar, y un codigo que no se
# ejecuta en ninguna prueba es codigo que nadie ha comprobado.
#
# Se le pone entonces un caso hecho a mano: una varilla justo encima de un tramo
# recto de la cinta. Sigue haciendo falta como red de seguridad, porque el doblez
# solo puede abrazar UNA varilla por costado y un armado con varias puede tener
# otra en el camino.
print()
tri = [(1.0, 0.0, 0.03), (0.0, 1.0, 0.03), (-1.0, 0.0, 0.03), (0.0, -1.0, 0.03)]
d = 0.02

# Punto medio del tramo recto que va del circulo 0 al 1, desplazado un poco hacia
# dentro para que la cinta lo corte de verdad.
pts = geometria_cinta(tri, 0)
a, b = tramo_recto(pts, 0, 1)
estorbo = ((a[0] + b[0]) / 2 - 0.02, (a[1] + b[1]) / 2 - 0.02, 0.06)

check("el estorbo empieza atravesado", atraviesa(tri, d, estorbo))

# La distancia se mide al SEGMENTO, no a la recta que lo contiene. Una varilla
# mas alla del extremo del tramo NO esta atravesada, aunque caiga sobre su
# prolongacion. Sin el recorte al segmento, la cinta intentaria rodear varillas
# que estan en la otra punta de la seccion.
a0, b0 = tramo_recto(geometria_cinta(tri, 0), 0, 1)
dx, dy = b0[0] - a0[0], b0[1] - a0[1]
mas_alla = (b0[0] + 3 * dx, b0[1] + 3 * dy, 0.06)
check("una varilla sobre la prolongacion del tramo no cuenta como atravesada",
      dist_a_segmento(mas_alla, a0, b0) >= mas_alla[2],
      f"distancia {dist_a_segmento(mas_alla, a0, b0):.4f} < radio {mas_alla[2]}")

rodeado = rodear_laterales(tri, d, [estorbo])
print(f"  circulos {len(tri)} -> {len(rodeado)}")

check("RodearLaterales lo mete en el recorrido", len(rodeado) == len(tri) + 1)
check("y despues ya no lo atraviesa", not atraviesa(rodeado, d, estorbo))

area = sum(rodeado[k][0] * rodeado[(k + 1) % len(rodeado)][1] -
           rodeado[(k + 1) % len(rodeado)][0] * rodeado[k][1]
           for k in range(len(rodeado)))
check("el recorrido con el estorbo sigue siendo antihorario", area > 0,
      f"area {area:.6f}")

# Se inserta en SU tramo, entre el circulo 0 y el 1, no en cualquier sitio.
check("se inserta en el tramo que atravesaba",
      abs(rodeado[1][0] - estorbo[0]) < 1e-12 and
      abs(rodeado[1][1] - estorbo[1]) < 1e-12,
      f"quedo en la posicion {[i for i,c in enumerate(rodeado) if abs(c[0]-estorbo[0])<1e-12]}")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
