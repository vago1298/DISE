"""Comprueba el doblez del diamante en los COSTADOS.

La regla que se comprueba, y que es la que pidio el usuario:

    El doblez de la grapa se apoya sobre UNA varilla si hay alguna en el eje, y
    sobre las DOS MAS CENTRADAS si no hay ninguna al medio.

Eso ya funcionaba arriba y abajo. En los costados NO, y este archivo demuestra
por que. VarillasDelCentro recibe un 'porY' para medir sobre la Y en lugar de la
X, pero el bloque de «el eje cae entre dos» leia 'varillas[i].X' a pelo en vez
de la coordenada que toca. En un costado todas las varillas tienen la MISMA X,
asi que ese bloque no separaba nada y el caso de dos varillas no se daba nunca.

Aqui estan los dos ports, el de antes y el de ahora, para poder ejecutarlos y
ver la diferencia con numeros. En este entorno no hay .NET ni AutoCAD, asi que
correr la formula aparte es la unica forma honesta de comprobarla.

Lo que se comprueba:

  1. Costado con numero PAR de varillas (ninguna a media altura) -> DOS varillas,
     y son las dos mas centradas.
  2. Costado con numero IMPAR -> UNA sola, la del eje.
  3. Que la version vieja fallaba justo en el caso 1.
  4. Que arriba y abajo (porY = False) no cambia nada: mismo resultado que antes.
  5. Que el recorrido de la cinta sigue siendo antihorario con los dos dobleces
     de cada costado, que es lo que impide que la cinta salga hecha un nudo.
"""

ESCALA = 0.01

# Diametros comerciales en cm, los mismos de la tabla del programa
DIAM = {
    "#2": 0.635, "#2.5": 0.794, "#3": 0.952, "#4": 1.270,
    "#5": 1.588, "#6": 1.905, "#8": 2.540,
}

DIA_TOL_CENTRO_FACTOR = 0.5


# ======================================================================
# Los dos ports de VarillasDelCentro
# ======================================================================

def varillas_del_centro_viejo(varillas, cx, por_y=False):
    """Como estaba: el segundo bloque mira la X aunque le pidan medir por Y."""
    if not varillas:
        return []

    def coord(v):
        return v[1] if por_y else v[0]

    mejor = min(range(len(varillas)), key=lambda i: abs(coord(varillas[i]) - cx))
    d_mejor = abs(coord(varillas[mejor]) - cx)

    tol = max(DIA_TOL_CENTRO_FACTOR * varillas[mejor][2], 1e-6)
    if d_mejor <= tol:
        return [varillas[mejor]]

    izq = der = -1
    d_izq = d_der = float("inf")

    for i, v in enumerate(varillas):
        # AQUI ESTABA EL DEFECTO: v[0] es la X, siempre, pase lo que pase.
        if v[0] < cx:
            if cx - v[0] < d_izq:
                d_izq, izq = cx - v[0], i
        elif v[0] - cx < d_der:
            d_der, der = v[0] - cx, i

    if izq >= 0 and der >= 0:
        return [varillas[izq], varillas[der]]

    uno = izq if izq >= 0 else der
    return [varillas[uno]] if uno >= 0 else []


def varillas_del_centro(varillas, cx, por_y=False):
    """Como esta ahora: el segundo bloque usa la MISMA coordenada que el primero."""
    if not varillas:
        return []

    def coord(v):
        return v[1] if por_y else v[0]

    mejor = min(range(len(varillas)), key=lambda i: abs(coord(varillas[i]) - cx))
    d_mejor = abs(coord(varillas[mejor]) - cx)

    tol = max(DIA_TOL_CENTRO_FACTOR * varillas[mejor][2], 1e-6)
    if d_mejor <= tol:
        return [varillas[mejor]]

    izq = der = -1
    d_izq = d_der = float("inf")

    for i, v in enumerate(varillas):
        c = coord(v)
        if c < cx:
            if cx - c < d_izq:
                d_izq, izq = cx - c, i
        elif c - cx < d_der:
            d_der, der = c - cx, i

    if izq >= 0 and der >= 0:
        return [varillas[izq], varillas[der]]

    uno = izq if izq >= 0 else der
    return [varillas[uno]] if uno >= 0 else []


# ======================================================================
# Las varillas laterales de una seccion, como las coloca el dibujante
# ======================================================================

def laterales(b_cm, h_cm, rec_cm, est, var, n_lat):
    """Varillas laterales de un costado, repartidas entre los lechos.

    Devuelve (del costado derecho, cy). Se reparten igual que en el C#: entre la
    varilla de esquina de abajo y la de arriba, sin contarlas.
    """
    b, h, rec = b_cm * ESCALA, h_cm * ESCALA, rec_cm * ESCALA
    d_est = DIAM[est] * ESCALA
    r_var = DIAM[var] * ESCALA / 2

    y1i = rec + d_est + r_var
    y2i = h - rec - d_est - r_var
    x_der = b - rec - d_est - r_var

    cy = (rec + (h - rec)) / 2

    # n_lat por costado, repartidas en los huecos entre las de esquina
    paso = (y2i - y1i) / (n_lat + 1)
    return [(x_der, y1i + (k + 1) * paso, r_var) for k in range(n_lat)], cy


def doblez_lateral(varillas, cy, derecha, port):
    """Port de DoblezLateral, solo la parte que elige y ordena las varillas."""
    sel = port(varillas, cy, por_y=True)
    if not sel:
        return []
    return sorted(sel, key=lambda v: v[1], reverse=not derecha)


# ======================================================================
# Comprobaciones
# ======================================================================

fallos = []


def check(nombre, cond, detalle=""):
    print(("  OK    " if cond else "  FALLA ") + nombre + ("" if cond else "  " + detalle))
    if not cond:
        fallos.append(nombre)


print("=" * 78)
print(" Doblez de la grapa del diamante en los costados")
print("=" * 78)

# ----------------------------------------------------------------------
# 1) Numero PAR de varillas laterales: NINGUNA a media altura
# ----------------------------------------------------------------------
print("\nColumna 40x80, rec 4, est #3, var #6, 2 varillas laterales por costado")
print("(numero PAR: el eje cae ENTRE dos, no sobre ninguna)")

lat, cy = laterales(40, 80, 4, "#3", "#6", 2)
print(f"  media altura cy = {cy:.4f}")
for v in lat:
    print(f"  varilla en y = {v[1]:.4f}   (a {abs(v[1] - cy):.4f} del eje)")

nuevo = doblez_lateral(lat, cy, derecha=True, port=varillas_del_centro)
viejo = doblez_lateral(lat, cy, derecha=True, port=varillas_del_centro_viejo)

check("ahora el doblez agarra DOS varillas", len(nuevo) == 2,
      f"agarro {len(nuevo)}")
check("y son las dos mas centradas",
      len(nuevo) == 2 and
      sorted(round(v[1], 9) for v in nuevo) ==
      sorted(round(v[1], 9) for v in sorted(lat, key=lambda v: abs(v[1] - cy))[:2]))
check("el vertice del doblez queda CENTRADO en media altura",
      len(nuevo) == 2 and abs(sum(v[1] for v in nuevo) / 2 - cy) < 1e-9,
      f"quedo en {sum(v[1] for v in nuevo) / 2:.6f} y cy es {cy:.6f}"
      if len(nuevo) == 2 else "")
check("antes agarraba UNA sola (el defecto)", len(viejo) == 1,
      f"agarro {len(viejo)}")

# El orden importa: en el costado derecho se recorre de abajo hacia arriba
check("en el costado derecho van de abajo hacia arriba",
      len(nuevo) == 2 and nuevo[0][1] < nuevo[1][1])

izquierdo = doblez_lateral(
    [(-v[0], v[1], v[2]) for v in lat], cy, derecha=False, port=varillas_del_centro)
check("y en el izquierdo de arriba hacia abajo",
      len(izquierdo) == 2 and izquierdo[0][1] > izquierdo[1][1])

# ----------------------------------------------------------------------
# 2) Numero IMPAR: hay una justo en el eje
# ----------------------------------------------------------------------
print("\nLa misma columna con 3 varillas laterales por costado (numero IMPAR)")

lat3, cy3 = laterales(40, 80, 4, "#3", "#6", 3)
for v in lat3:
    print(f"  varilla en y = {v[1]:.4f}   (a {abs(v[1] - cy3):.4f} del eje)")

nuevo3 = doblez_lateral(lat3, cy3, derecha=True, port=varillas_del_centro)

check("con una varilla en el eje se agarra ESA sola", len(nuevo3) == 1,
      f"agarro {len(nuevo3)}")
check("y es la del eje",
      len(nuevo3) == 1 and abs(nuevo3[0][1] - cy3) < 1e-9)

# 4 varillas: par otra vez, y las dos centrales no son las de los extremos
print("\nY con 4 varillas laterales por costado (par)")
lat4, cy4 = laterales(40, 80, 4, "#3", "#6", 4)
nuevo4 = doblez_lateral(lat4, cy4, derecha=True, port=varillas_del_centro)
for v in lat4:
    print(f"  varilla en y = {v[1]:.4f}")
check("agarra las dos del medio, no las de los extremos",
      len(nuevo4) == 2 and
      sorted(round(v[1], 9) for v in nuevo4) ==
      sorted(round(v[1], 9) for v in lat4[1:3]),
      f"agarro {[round(v[1], 4) for v in nuevo4]}")

# ----------------------------------------------------------------------
# 3) Arriba y abajo (porY = False) NO cambia
# ----------------------------------------------------------------------
print("\nLechos de arriba y abajo: el cambio no debe tocarlos")

r = DIAM["#6"] * ESCALA / 2
cx = 0.20
lecho_par = [(0.05, 0.7, r), (0.15, 0.7, r), (0.25, 0.7, r), (0.35, 0.7, r)]
lecho_impar = [(0.05, 0.7, r), (0.20, 0.7, r), (0.35, 0.7, r)]

for etiqueta, lecho in (("par", lecho_par), ("impar", lecho_impar)):
    a = varillas_del_centro(lecho, cx)
    b = varillas_del_centro_viejo(lecho, cx)
    check(f"lecho {etiqueta}: mismo resultado que antes",
          [tuple(round(c, 9) for c in v) for v in a] ==
          [tuple(round(c, 9) for c in v) for v in b],
          f"antes {b} ahora {a}")

check("lecho par: dos varillas", len(varillas_del_centro(lecho_par, cx)) == 2)
check("lecho impar: una varilla", len(varillas_del_centro(lecho_impar, cx)) == 1)

# ----------------------------------------------------------------------
# 5) El recorrido completo sigue siendo antihorario
# ----------------------------------------------------------------------
print("\nRecorrido completo de la cinta, con los dos dobleces de cada costado")

b_cm, h_cm, rec_cm = 40, 80, 4
b, h, rec = b_cm * ESCALA, h_cm * ESCALA, rec_cm * ESCALA
d_est = DIAM["#3"] * ESCALA
r_var = DIAM["#6"] * ESCALA / 2

x1e, y1e, x2e, y2e = rec, rec, b - rec, h - rec
cx_s, cy_s = (x1e + x2e) / 2, (y1e + y2e) / 2

lat_der, _ = laterales(b_cm, h_cm, rec_cm, "#3", "#6", 2)
lat_izq = [(b - v[0], v[1], v[2]) for v in lat_der]

# Lecho superior e inferior con numero PAR de varillas: dos en el centro
y_sup = h - rec - d_est - r_var
y_inf = rec + d_est + r_var
sup = [(cx_s - 0.05, y_sup, r_var), (cx_s + 0.05, y_sup, r_var)]
inf = [(cx_s - 0.05, y_inf, r_var), (cx_s + 0.05, y_inf, r_var)]

recorrido = []
recorrido += doblez_lateral(lat_der, cy_s, derecha=True, port=varillas_del_centro)
recorrido += sorted(sup, key=lambda v: v[0], reverse=True)      # derecha -> izquierda
recorrido += doblez_lateral(lat_izq, cy_s, derecha=False, port=varillas_del_centro)
recorrido += sorted(inf, key=lambda v: v[0])                    # izquierda -> derecha

print(f"  {len(recorrido)} circulos en el recorrido")
for i, v in enumerate(recorrido):
    print(f"   {i}: ({v[0]:.4f}, {v[1]:.4f})")

check("el recorrido lleva 8 circulos: 2 por costado y 2 por lecho",
      len(recorrido) == 8, f"lleva {len(recorrido)}")

area = sum(recorrido[k][0] * recorrido[(k + 1) % len(recorrido)][1] -
           recorrido[(k + 1) % len(recorrido)][0] * recorrido[k][1]
           for k in range(len(recorrido)))
check("y sigue siendo antihorario", area > 0, f"area {area:.6f}")


def se_cruza(pts):
    """¿El poligono se cruza consigo mismo? Fuerza bruta, son 8 puntos."""
    n = len(pts)

    def cruzan(p, q, r, s):
        def orient(a, b, c):
            v = ((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
            return 0 if abs(v) < 1e-15 else (1 if v > 0 else -1)

        return (orient(p, q, r) * orient(p, q, s) < 0 and
                orient(r, s, p) * orient(r, s, q) < 0)

    for i in range(n):
        for j in range(i + 2, n):
            if i == 0 and j == n - 1:
                continue
            if cruzan(pts[i], pts[(i + 1) % n], pts[j], pts[(j + 1) % n]):
                return True
    return False


check("y no se cruza consigo mismo",
      not se_cruza([(v[0], v[1]) for v in recorrido]))

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
