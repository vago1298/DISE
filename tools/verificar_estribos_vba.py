"""Compara el reparto de estribos del VBA original contra el port de C#.

El usuario dijo: "no respetas las separacion, en fin, no respetas mi codigo
original". Discutirlo no sirve; se comprueba.

Aqui hay DOS implementaciones independientes:

  vba_*   traducido linea por linea del modulo de VBA que el usuario paso,
          incluidos sus Int(), sus tolerancias y sus Exit Sub.
  cs_*    traducido del C# de Estribos.cs, el que usa la aplicacion.

Se corren las dos sobre una matriz de casos y se comparan las posiciones una a
una. Cualquier diferencia es un fallo del port, y sale con nombre y numeros.
"""

import math

# ---- Constantes del VBA ----
STIRRUP_EDGE_OFFSET = 0.05
MIN_STIRRUP_SPACING = 0.05
SEP_MIN_ESTRIBOS = 0.05
TOL_TRANSICION = 0.06
L_INICIAL = 2.0

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


def vint(x):
    """Int() de VBA: trunca hacia -infinito. Para positivos, como floor."""
    return math.floor(x)


# ======================================================================
#  VBA, traducido linea por linea
# ======================================================================
def vba_add_unique_center(col, valor):
    if len(col) == 0:
        col.append(valor)
    elif abs(col[-1] - valor) > 0.0001:
        col.append(valor)


def vba_add_centro_con_separacion(col, valor):
    if len(col) > 0:
        if abs(col[-1] - valor) < SEP_MIN_ESTRIBOS - 0.0000001:
            return
    col.append(valor)


def vba_add_centro_transicion(col, pos_nominal, pos_siguiente, lim_sup):
    hay_prev = len(col) > 0
    prev = col[-1] if hay_prev else 0.0

    lo_lim = pos_nominal - TOL_TRANSICION
    hi_lim = pos_nominal + TOL_TRANSICION

    if hay_prev:
        if (prev + SEP_MIN_ESTRIBOS) > lo_lim:
            lo_lim = prev + SEP_MIN_ESTRIBOS
    if (pos_siguiente - SEP_MIN_ESTRIBOS) < hi_lim:
        hi_lim = pos_siguiente - SEP_MIN_ESTRIBOS
    if lim_sup < hi_lim:
        hi_lim = lim_sup

    if lo_lim > hi_lim + 0.0000001:
        return

    p = pos_nominal
    if p < lo_lim:
        p = lo_lim
    if p > hi_lim:
        p = hi_lim

    col.append(p)


def vba_add_centers_by_spacing(col, inner_start, sec_start, sec_end, spacing):
    n = vint((sec_end - sec_start) / spacing)
    if n < 1:
        n = 1
    for i in range(1, n + 1):
        pos_abs = sec_start + i * spacing
        if pos_abs < sec_end - 0.0001:
            vba_add_centro_con_separacion(col, inner_start + pos_abs)


def vba_build_stirrup_centers(start_pos, end_pos, s1, s2, s3,
                              add_end, add_boundary):
    col = []
    inner_start = start_pos + STIRRUP_EDGE_OFFSET
    inner_end = end_pos - STIRRUP_EDGE_OFFSET
    inner_len = inner_end - inner_start
    if inner_len <= 0:
        return []

    if s1 <= 0:
        s1 = 0.15
    if s2 <= 0:
        s2 = s1
    if s3 <= 0:
        s3 = s1
    if s1 < MIN_STIRRUP_SPACING:
        s1 = MIN_STIRRUP_SPACING
    if s2 < MIN_STIRRUP_SPACING:
        s2 = MIN_STIRRUP_SPACING
    if s3 < MIN_STIRRUP_SPACING:
        s3 = MIN_STIRRUP_SPACING

    variable = (abs(s1 - s2) > 0.0001) or (abs(s2 - s3) > 0.0001)

    if variable:
        sec1_end = inner_len * 0.25
        sec2_end = sec1_end + inner_len * 0.5

        nx1 = inner_start + sec1_end + s2
        if (sec1_end + s2) > (sec2_end - 0.0001):
            nx1 = inner_start + sec2_end
        nx2 = inner_start + sec2_end + s3
        if (sec2_end + s3) > (inner_len - 0.0001):
            nx2 = inner_end

        if add_end:
            vba_add_unique_center(col, inner_start)
        vba_add_centers_by_spacing(col, inner_start, 0.0, sec1_end, s1)
        if add_boundary:
            vba_add_centro_transicion(col, inner_start + sec1_end, nx1, inner_end)
        vba_add_centers_by_spacing(col, inner_start, sec1_end, sec2_end, s2)
        if add_boundary:
            vba_add_centro_transicion(col, inner_start + sec2_end, nx2, inner_end)
        vba_add_centers_by_spacing(col, inner_start, sec2_end, inner_len, s3)
        if add_end:
            vba_add_unique_center(col, inner_end)
    else:
        num = vint(inner_len / s1)
        if num < 3:
            num = 3
        paso = inner_len / num
        if add_end:
            for i in range(0, num + 1):
                vba_add_unique_center(col, inner_start + i * paso)
        else:
            for i in range(1, num):
                vba_add_unique_center(col, inner_start + i * paso)

    return col


def vba_remove_last_center(centers):
    """Solo en COLUMNA: se elimina el ultimo estribo."""
    if len(centers) <= 1:
        return []
    return centers[:-1]


def vba_calculate_flexible_length(s1, s2, s3):
    inner_len = L_INICIAL - 2 * STIRRUP_EDGE_OFFSET
    if s1 <= 0:
        s1 = 0.15
    if s2 <= 0:
        s2 = s1
    if s3 <= 0:
        s3 = s1
    s1 = max(s1, MIN_STIRRUP_SPACING)
    s2 = max(s2, MIN_STIRRUP_SPACING)
    s3 = max(s3, MIN_STIRRUP_SPACING)

    variable = (abs(s1 - s2) > 0.0001) or (abs(s2 - s3) > 0.0001)

    if variable:
        n1 = max(1, vint((inner_len / 4) / s1))
        n2 = max(1, vint((inner_len / 2) / s2))
        n3 = max(1, vint((inner_len / 4) / s3))
        largo = 2 * STIRRUP_EDGE_OFFSET + n1 * s1 + n2 * s2 + n3 * s3
    else:
        num = max(3, vint(inner_len / s1))
        largo = 2 * STIRRUP_EDGE_OFFSET + num * s1

    return max(largo, L_INICIAL * 0.8)


# ======================================================================
#  C#, traducido de Estribos.cs
# ======================================================================
def cs_unico(col, valor):
    if len(col) == 0 or abs(col[-1] - valor) > 1e-4:
        col.append(valor)


def cs_con_separacion(col, valor):
    if len(col) > 0 and abs(col[-1] - valor) < SEP_MIN_ESTRIBOS - 1e-7:
        return
    col.append(valor)


def cs_transicion(col, nominal, siguiente, lim_superior):
    lo = nominal - TOL_TRANSICION
    hi = nominal + TOL_TRANSICION
    if len(col) > 0:
        lo = max(lo, col[-1] + SEP_MIN_ESTRIBOS)
    hi = min(hi, siguiente - SEP_MIN_ESTRIBOS)
    hi = min(hi, lim_superior)
    if lo > hi + 1e-7:
        return
    col.append(min(max(nominal, lo), hi))


def cs_por_separacion(col, ini, desde, hasta, sep):
    n = int((hasta - desde) / sep)
    if n < 1:
        n = 1
    for i in range(1, n + 1):
        p = desde + i * sep
        if p < hasta - 1e-4:
            cs_con_separacion(col, ini + p)


def cs_centros(x0, x1, s1, s2, s3, con_extremos, con_fronteras,
               guarda_minima=True):
    col = []
    ini = x0 + STIRRUP_EDGE_OFFSET
    fin = x1 - STIRRUP_EDGE_OFFSET
    largo = fin - ini
    if largo <= 0:
        return col

    if s1 <= 0:
        s1 = 0.15
    if s2 <= 0:
        s2 = s1
    if s3 <= 0:
        s3 = s1
    s1 = max(s1, MIN_STIRRUP_SPACING)
    s2 = max(s2, MIN_STIRRUP_SPACING)
    s3 = max(s3, MIN_STIRRUP_SPACING)

    variable = abs(s1 - s2) > 1e-4 or abs(s2 - s3) > 1e-4

    if not variable:
        n = int(largo / s1)
        if n < 3:
            n = 3
        if guarda_minima:
            max_por_sep = int(math.floor(largo / SEP_MIN_ESTRIBOS))
            if max_por_sep >= 1 and n > max_por_sep:
                n = max_por_sep
        if n < 1:
            n = 1
        paso = largo / n
        desde = 0 if con_extremos else 1
        hasta = n if con_extremos else n - 1
        for i in range(desde, hasta + 1):
            cs_unico(col, ini + i * paso)
        return col

    z1 = largo * 0.25
    z2 = z1 + largo * 0.5

    sig1 = ini + z1 + s2
    if z1 + s2 > z2 - 1e-4:
        sig1 = ini + z2
    sig2 = ini + z2 + s3
    if z2 + s3 > largo - 1e-4:
        sig2 = fin

    if con_extremos:
        cs_unico(col, ini)
    cs_por_separacion(col, ini, 0, z1, s1)
    if con_fronteras:
        cs_transicion(col, ini + z1, sig1, fin)
    cs_por_separacion(col, ini, z1, z2, s2)
    if con_fronteras:
        cs_transicion(col, ini + z2, sig2, fin)
    cs_por_separacion(col, ini, z2, largo, s3)
    if con_extremos:
        cs_unico(col, fin)
    return col


# ======================================================================
#  Comparacion
# ======================================================================
print("=" * 78)
print(" Reparto de estribos: VBA original  vs  port de C#")
print("=" * 78)

SEPARACIONES = [
    (10, 10, 10), (10, 15, 10), (10, 20, 20), (8, 16, 8), (10, 20, 10),
    (15, 15, 15), (20, 20, 20), (5, 10, 5), (12, 25, 12), (7, 14, 7),
    (10, 10, 20), (20, 10, 20), (30, 30, 30), (5, 5, 5), (25, 40, 25),
]
LARGOS = [0.6, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 0.4, 0.3]

difs = 0
comparados = 0

for (a, b, c) in SEPARACIONES:
    s1, s2, s3 = a / 100.0, b / 100.0, c / 100.0
    for L in LARGOS:
        for extremos in (False, True):
            for fronteras in (False, True):
                v = vba_build_stirrup_centers(0.0, L, s1, s2, s3, extremos, fronteras)
                k = cs_centros(0.0, L, s1, s2, s3, extremos, fronteras)
                comparados += 1

                if len(v) != len(k) or any(abs(p - q) > 1e-9 for p, q in zip(v, k)):
                    difs += 1
                    if difs <= 6:
                        print(f"  DIFERENCIA  {a}-{b}-{c}  L={L}  "
                              f"extremos={extremos} fronteras={fronteras}")
                        print(f"      VBA ({len(v)}): "
                              + ", ".join(f"{p:.4f}" for p in v[:9]))
                        print(f"      C#  ({len(k)}): "
                              + ", ".join(f"{p:.4f}" for p in k[:9]))

print(f"\n  casos comparados: {comparados}")
check("el reparto de estribos coincide con el VBA en todos los casos",
      difs == 0, f"{difs} casos distintos")

# ---- La desviacion DELIBERADA: la guarda de separacion minima ----
# La macro fuerza un minimo de 3 estribos sin volver a mirar la separacion, y en
# un elemento muy corto eso da estribos a 1.67 cm. Se comprueba que la guarda
# solo actua ahi y que en longitudes normales no cambia NADA.
print()
print("=" * 78)
print(" La unica desviacion deliberada: separacion minima en elementos cortos")
print("=" * 78)

toco = []
for (a, b, c) in SEPARACIONES:
    s1, s2, s3 = a / 100.0, b / 100.0, c / 100.0
    for L in LARGOS:
        con = cs_centros(0.0, L, s1, s2, s3, False, True, guarda_minima=True)
        sin = cs_centros(0.0, L, s1, s2, s3, False, True, guarda_minima=False)
        if con != sin:
            toco.append((a, b, c, L, len(sin), len(con)))

print(f"  la guarda cambia el resultado en {len(toco)} de {len(SEPARACIONES)*len(LARGOS)} casos:")
for (a, b, c, L, n0, n1) in toco:
    sep0 = (L - 2 * STIRRUP_EDGE_OFFSET) / max(n0, 1)
    print(f"    {a}-{b}-{c} L={L} m -> sin guarda {n0} estribos a "
          f"{100*sep0:.2f} cm, con guarda {n1}")

check("la guarda solo actua cuando la separacion baja del minimo",
      all(((L - 2 * STIRRUP_EDGE_OFFSET) / max(n0, 1)) < MIN_STIRRUP_SPACING
          for (_, _, _, L, n0, _) in toco),
      "actua en un caso donde la separacion era valida")

# ---- Longitud flexible cuando la columna W viene vacia ----
print()
print("=" * 78)
print(" Longitud cuando la columna W viene vacia")
print("=" * 78)


def cs_longitud_flexible(s1, s2, s3):
    interior = L_INICIAL - 2 * STIRRUP_EDGE_OFFSET
    if s1 <= 0:
        s1 = 0.15
    if s2 <= 0:
        s2 = s1
    if s3 <= 0:
        s3 = s1
    s1 = max(s1, MIN_STIRRUP_SPACING)
    s2 = max(s2, MIN_STIRRUP_SPACING)
    s3 = max(s3, MIN_STIRRUP_SPACING)
    variable = abs(s1 - s2) > 1e-4 or abs(s2 - s3) > 1e-4
    if variable:
        n1 = max(1, int((interior / 4) / s1))
        n2 = max(1, int((interior / 2) / s2))
        n3 = max(1, int((interior / 4) / s3))
        largo = 2 * STIRRUP_EDGE_OFFSET + n1 * s1 + n2 * s2 + n3 * s3
    else:
        n = max(3, int(interior / s1))
        largo = 2 * STIRRUP_EDGE_OFFSET + n * s1
    return max(largo, L_INICIAL * 0.8)


malos = []
for (a, b, c) in SEPARACIONES:
    v = vba_calculate_flexible_length(a / 100.0, b / 100.0, c / 100.0)
    k = cs_longitud_flexible(a / 100.0, b / 100.0, c / 100.0)
    if abs(v - k) > 1e-12:
        malos.append((a, b, c, v, k))

check("la longitud flexible coincide con el VBA", not malos,
      "; ".join(f"{a}-{b}-{c}: VBA {v:.4f} vs C# {k:.4f}" for a, b, c, v, k in malos))

# ---- RemoveLastCenter: solo en COLUMNA ----
print()
print("=" * 78)
print(" RemoveLastCenter: en COLUMNA se quita el ultimo estribo")
print("=" * 78)

base = vba_build_stirrup_centers(0.0, 3.0, 0.10, 0.20, 0.20, False, False)
col = vba_remove_last_center(base)
print(f"  columna 3.00 m, 10-20-20: {len(base)} estribos -> {len(col)} tras quitar el ultimo")
check("quita exactamente uno", len(col) == len(base) - 1,
      f"{len(base)} -> {len(col)}")
check("y quita el ULTIMO, no otro", col == base[:-1])
check("con un solo estribo queda vacio", vba_remove_last_center([0.5]) == [])

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)



# ======================================================================
#  El GANCHO: las dos reglas del VBA
# ======================================================================
# En el VBA el gancho se calcula distinto segun el alzado, y las dos reglas hay
# que respetarlas:
#
#   Alzado HORIZONTAL (trabe / contratrabe), DrawHorizontalBeamGeom:
#       hookSup = HOOK_DIAM_FACTOR * dSup      ' 12 diametros de la varilla
#       hookInf = HOOK_DIAM_FACTOR * dInf
#
#   Alzado VERTICAL (columna / dado), DrawVerticalColumnGeom:
#       hookSup = gancho                       ' el valor de la columna T, tal cual
#       hookInf = gancho
#
#   Y las dos ramas terminan igual:
#       If hookSup > maxSup Then hookSup = maxSup      ' recorte por lo que cabe
#       If hookSup < dSup   Then hookSup = 0#          ' si no cabe, NO hay gancho

print()
print("=" * 78)
print(" El gancho: 12 diametros en la trabe, columna T en la columna")
print("=" * 78)

HOOK_DIAM_FACTOR = 12.0


def vba_hook(vertical, gancho_m, d_barra, disponible):
    """Las dos ramas del VBA, con su recorte y su cero."""
    h = gancho_m if vertical else HOOK_DIAM_FACTOR * d_barra
    if h > disponible:
        h = disponible
    if h < d_barra:
        h = 0.0
    return h


def cs_gancho_nominal(vertical, gancho_m, d_barra):
    return gancho_m if vertical else HOOK_DIAM_FACTOR * d_barra


def cs_gancho_efectivo(nominal, disponible, d_barra):
    g = min(nominal, disponible)
    return 0.0 if g < d_barra else g


def cs_hook(vertical, gancho_m, d_barra, disponible):
    return cs_gancho_efectivo(cs_gancho_nominal(vertical, gancho_m, d_barra),
                              disponible, d_barra)


DIAMS = [0.0064, 0.0095, 0.0127, 0.0159, 0.0191, 0.0254]
GANCHOS = [0.0, 0.02, 0.05, 0.08, 0.10, 0.15]
DISPONIBLES = [0.005, 0.02, 0.05, 0.10, 0.20, 0.35, 1.0]

difs = 0
casos = 0
for vertical in (False, True):
    for d in DIAMS:
        for g in GANCHOS:
            for disp in DISPONIBLES:
                casos += 1
                v = vba_hook(vertical, g, d, disp)
                k = cs_hook(vertical, g, d, disp)
                if abs(v - k) > 1e-15:
                    difs += 1
                    if difs <= 5:
                        print(f"  DIFERENCIA vertical={vertical} d={d} gancho={g} "
                              f"disp={disp}: VBA {v} vs C# {k}")

print(f"  casos comparados: {casos}")
check("el gancho coincide con el VBA en todos los casos", difs == 0,
      f"{difs} distintos")

# El 12 no es un numero cualquiera: es HOOK_DIAM_FACTOR. Si se cambia, el gancho
# de TODAS las trabes sale mal y el dibujo sigue pareciendo razonable.
check("en la trabe el gancho son 12 diametros",
      abs(cs_gancho_nominal(False, 0.05, 0.0095) - 12 * 0.0095) < 1e-15,
      f"salen {cs_gancho_nominal(False, 0.05, 0.0095) / 0.0095:.2f} diametros")

# En la columna manda la columna T, NO los diametros.
check("en la columna el gancho es el valor de la hoja",
      abs(cs_gancho_nominal(True, 0.05, 0.0095) - 0.05) < 1e-15)

# La regla del cero: un gancho mas corto que la propia varilla no representa nada
# y el VBA lo anula. Sin esta regla se dibujan mu#ones de 2 mm.
check("un gancho que no cabe se anula, no se dibuja corto",
      cs_hook(False, 0.05, 0.0254, 0.005) == 0.0,
      f"dio {cs_hook(False, 0.05, 0.0254, 0.005)}")
check("y uno que cabe se recorta a lo disponible",
      abs(cs_hook(False, 0.05, 0.0095, 0.05) - 0.05) < 1e-15,
      f"dio {cs_hook(False, 0.05, 0.0095, 0.05)}")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
