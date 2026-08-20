"""Verifica hacia donde apunta la cola del gancho sismico del diamante.

Escrito aparte para poder EJECUTARLO: en este entorno no hay .NET ni AutoCAD, asi
que la unica forma honesta de comprobar una direccion es calcularla.

EL ERROR QUE SE CORRIGE
-----------------------
La primera version tomaba el radio hacia el nucleo y lo giraba 45 grados. Ese giro
es del ZUNCHO CIRCULAR y solo tiene sentido ahi: en el zuncho el acero llega al
doblez en direccion tangente, o sea perpendicular al radio, asi que girar el radio
45 grados equivale a doblar el acero 135, que es el gancho sismico.

En el diamante el acero NO llega en tangente: llega por la diagonal del rombo. Y en
una seccion cuadrada las dos diagonales que salen del vertice van a +-45 grados del
eje, asi que la cola girada 45 caia EXACTAMENTE encima de una de ellas. En el dibujo
el gancho no se veia como un gancho metido en el concreto, sino como si el estribo
siguiera de largo.

La regla correcta es la del estribo RECTANGULAR: la cola apunta al nucleo, o sea el
radio SIN girar. Aqui se comprueba:

  1. Que la cola nueva apunta al centro de la seccion.
  2. Que se separa de las dos diagonales del diamante, o sea que ya no se monta
     sobre el acero. En todas las secciones: cuadrada, alta y achatada.
  3. Que la regla vieja SI se montaba, para que quede probado que el error era real
     y que esta comprobacion lo habria cazado.
  4. Que en la seccion cuadrada la cola queda a 135 grados del acero que llega, que
     es el gancho sismico de norma.
  5. Que el doblez envuelve el lado OPUESTO a las colas.
  6. Que la cola se recorta antes de cruzar el centro.
  7. Que es la MISMA regla que usa el gancho del estribo rectangular.
"""

import math

# Diametros comerciales en cm
DIAM = {
    "#2": 0.635, "#2.5": 0.794, "#3": 0.952, "#4": 1.270,
    "#5": 1.588, "#6": 1.905, "#8": 2.540,
}

RT2I = 0.707106781186547

fallos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


def unit(vx, vy):
    n = math.hypot(vx, vy)
    return (vx / n, vy / n)


def grados_entre(a, b):
    """Angulo entre dos direcciones unitarias, en grados, de 0 a 180."""
    d = max(-1.0, min(1.0, a[0] * b[0] + a[1] * b[1]))
    return math.degrees(math.acos(d))


def rot45(v):
    """El giro de 45 grados de la version vieja: el del zuncho circular.

    Se escribe con la constante RT2I recortada que usa el C#, no con math.sqrt, para
    que las cuentas salgan con el mismo error de redondeo que el programa. Por eso
    las comparaciones de esta prueba se hacen con tolerancia de 1e-4 grados y no de
    1e-9: el error de RT2I ya vale 2e-6 grados.
    """
    return ((v[0] - v[1]) * RT2I, (v[0] + v[1]) * RT2I)


def rotar(v, grados):
    """Gira una direccion, en grados, en sentido antihorario."""
    a = math.radians(grados)
    c, s = math.cos(a), math.sin(a)
    return (v[0] * c - v[1] * s, v[0] * s + v[1] * c)


def separacion_minima(direccion, leg_a, leg_b):
    """Lo que se separa una direccion del acero del diamante: el peor de los dos."""
    return min(grados_entre(direccion, leg_a), grados_entre(direccion, leg_b))


def dentro_del_vertice(d, leg_a, leg_b, eps=1e-9):
    """Si la direccion cae en la cuna que abren las dos diagonales del vertice.

    Es la condicion que de verdad importa, y la que me faltaba al primer intento: una
    direccion casi perpendicular a las diagonales se separa mucho de ellas -y por eso
    ganaba el barrido- pero se sale de la seccion al recubrimiento. La cola tiene que
    quedar ENTRE las dos diagonales, que es donde esta el concreto del nucleo.

    Se resuelve d = a*leg_a + b*leg_b y se exige a >= 0 y b >= 0.
    """
    det = leg_a[0] * leg_b[1] - leg_a[1] * leg_b[0]

    if abs(det) < 1e-12:
        return False

    a = (d[0] * leg_b[1] - d[1] * leg_b[0]) / det
    b = (leg_a[0] * d[1] - leg_a[1] * d[0]) / det

    return a >= -eps and b >= -eps


def mejor_direccion(leg_a, leg_b, eps=1e-9):
    """Barre 3600 direcciones DENTRO de la cuna y devuelve la que mas se separa.

    Esto es lo que demuestra que la bisectriz es la eleccion correcta y no una
    cualquiera que resulte pasar las pruebas: no hay ninguna mejor.
    """
    mejor, mejor_sep = None, -1.0

    for k in range(3600):
        d = (math.cos(math.radians(k / 10)), math.sin(math.radians(k / 10)))

        if not dentro_del_vertice(d, leg_a, leg_b, eps=1e-6):
            continue

        sep = separacion_minima(d, leg_a, leg_b)

        if sep > mejor_sep:
            mejor, mejor_sep = d, sep

    return mejor, mejor_sep


def vertice(b, h, rec, est, var, n_lat, sep=10.0):
    """Geometria del vertice izquierdo del diamante.

    Devuelve (barra, centro, leg_arriba, leg_abajo), donde las 'legs' son las
    direcciones de las dos diagonales del rombo que salen de ese vertice.

    Las diagonales se toman de centro a centro de las varillas que la cinta abraza.
    Es exacto cuando las dos varillas son del mismo diametro -el caso normal, todo
    el armado longitudinal igual-, porque la tangente comun a dos circulos iguales
    es paralela a la linea de centros.

    n_lat: cuantas varillas laterales hay en ESE costado. Con 1 el vertice cae a
    media altura; con 2 el diamante dobla sobre las dos y el gancho va en la de
    arriba, que es donde el acero llega recorriendo el costado.
    """
    d_est = DIAM[est]
    r_var = DIAM[var] / 2

    x1, y1 = rec, rec
    x2, y2 = b - rec, h - rec
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2

    # Varillas: centro a un recubrimiento + estribo + radio de la paredes
    xl = rec + d_est + r_var
    yt = h - rec - d_est - r_var
    yb = rec + d_est + r_var

    if n_lat == 1:
        barra = (xl, cy, r_var)
    else:
        # Con dos, el gancho va en la de ARRIBA.
        barra = (xl, cy + sep / 2, r_var)

        # Y la vecina de abajo es el otro circulo del vertice: la cinta va vertical
        # entre las dos, asi que esa es la diagonal de abajo para este circulo.
        vecina_abajo = (xl, cy - sep / 2)

    sup = (cx, yt)
    inf = (cx, yb)

    leg_arriba = unit(sup[0] - barra[0], sup[1] - barra[1])

    if n_lat == 1:
        leg_abajo = unit(inf[0] - barra[0], inf[1] - barra[1])
    else:
        leg_abajo = unit(vecina_abajo[0] - barra[0], vecina_abajo[1] - barra[1])

    return barra, (cx, cy), leg_arriba, leg_abajo


CASOS = [
    # nombre,                                       b,   h, rec, est,  var, n_lat
    ("Columna 40x40 cuadrada, 1 lateral",           40,  40,   4, "#3", "#8", 1),
    ("Columna 30x60 alta, 1 lateral",               30,  60,   4, "#3", "#8", 1),
    ("Trabe 60x30 achatada, 1 lateral",             60,  30,   4, "#3", "#6", 1),
    ("Columna 100x100, 1 lateral",                 100, 100,   5, "#4", "#8", 1),
    ("Columna 40x40, 2 laterales (eje entre dos)",  40,  40,   4, "#3", "#8", 2),
    ("Trabe 80x25 muy achatada, 1 lateral",         80,  25,   4, "#3", "#5", 1),
]

# Por debajo de esta separacion la cola se ve pegada a la diagonal. NO se exige como
# condicion, porque en una seccion muy achatada las dos diagonales del rombo son casi
# horizontales y NINGUNA direccion se separa mas: se avisa y se sigue. Lo que si se
# exige es que no haya ninguna direccion mejor que la elegida.
SEP_COMODA = 15.0

avisos = []

print("=" * 78)
print(" Direccion de la cola del gancho del diamante")
print("=" * 78)

for nombre, b, h, rec, est, var, n_lat in CASOS:
    barra, centro, leg_arr, leg_aba = vertice(b, h, rec, est, var, n_lat)

    # La regla NUEVA: el radio hacia el nucleo, sin girar.
    u = unit(centro[0] - barra[0], centro[1] - barra[1])

    # La regla VIEJA: ese radio girado 45 grados.
    u_viejo = rot45(u)

    sep_arr = grados_entre(u, leg_arr)
    sep_aba = grados_entre(u, leg_aba)
    vie_arr = grados_entre(u_viejo, leg_arr)
    vie_aba = grados_entre(u_viejo, leg_aba)

    print(f"\n{nombre}")
    print(f"    diagonales del rombo: {math.degrees(math.atan2(leg_arr[1], leg_arr[0])):+7.2f}° "
          f"y {math.degrees(math.atan2(leg_aba[1], leg_aba[0])):+7.2f}°")
    print(f"    cola NUEVA {math.degrees(math.atan2(u[1], u[0])):+7.2f}°"
          f"   separacion de las diagonales: {sep_arr:6.2f}° y {sep_aba:6.2f}°")
    print(f"    cola VIEJA {math.degrees(math.atan2(u_viejo[1], u_viejo[0])):+7.2f}°"
          f"   separacion de las diagonales: {vie_arr:6.2f}° y {vie_aba:6.2f}°")

    # 1. Apunta al nucleo.
    hacia = unit(centro[0] - barra[0], centro[1] - barra[1])
    check(f"'{nombre}': la cola apunta al nucleo",
          abs(u[0] - hacia[0]) < 1e-12 and abs(u[1] - hacia[1]) < 1e-12)
    check(f"'{nombre}': y entra en la seccion, no sale de ella",
          u[0] > 0, f"componente X {u[0]:+.4f}")

    # 2. No se monta sobre el acero del diamante, y queda DENTRO de la cuna del
    # vertice, que es donde esta el concreto que el gancho tiene que morder.
    check(f"'{nombre}': la cola no cae sobre ninguna diagonal",
          min(sep_arr, sep_aba) > 1.0,
          f"la mas cercana a {min(sep_arr, sep_aba):.4f}°")
    check(f"'{nombre}': la cola queda dentro de la cuna del vertice",
          dentro_del_vertice(u, leg_arr, leg_aba))

    _, sep_optima = mejor_direccion(leg_arr, leg_aba)

    print(f"    la mejor direccion posible dentro de la cuna separa {sep_optima:.2f}°")

    if n_lat == 1:
        # Vertice simetrico: el radio al nucleo ES la bisectriz, asi que tiene que
        # salir la mejor direccion posible. Las dos separaciones iguales son la senal.
        check(f"'{nombre}': va por la bisectriz del vertice",
              abs(sep_arr - sep_aba) < 1e-9,
              f"{sep_arr:.6f}° contra {sep_aba:.6f}°")
        check(f"'{nombre}': y es la direccion que mas se separa del acero",
              min(sep_arr, sep_aba) >= sep_optima - 0.06,
              f"elegida {min(sep_arr, sep_aba):.2f}°, la mejor {sep_optima:.2f}°")
    else:
        # Vertice de DOS varillas: la cinta va vertical entre las dos, asi que la cuna
        # es asimetrica y el radio al nucleo ya no coincide con su bisectriz. Se queda
        # el radio igual: es lo que pidio el usuario -la cola mira al nucleo- y es la
        # regla del estribo rectangular. Lo que se exige es que la separacion siga
        # siendo amplia, no que sea la maxima.
        check(f"'{nombre}': aun sin ser la bisectriz, se separa de sobra del acero",
              min(sep_arr, sep_aba) >= SEP_COMODA,
              f"{min(sep_arr, sep_aba):.2f}°, comodo desde {SEP_COMODA}°")

    if min(sep_arr, sep_aba) < SEP_COMODA:
        avisos.append(
            f"{nombre}: la cola queda a solo {min(sep_arr, sep_aba):.1f}° de las "
            "diagonales. La seccion es tan achatada que el rombo va casi horizontal "
            "y no hay direccion mejor; el gancho se vera apretado.")
        print(f"    AVISO: seccion muy achatada, la cola queda a "
              f"{min(sep_arr, sep_aba):.1f}° del acero del diamante")

# ---------------------------------------------------------------------------
# 3. El error viejo, probado: en la seccion cuadrada la cola caia ENCIMA.
# ---------------------------------------------------------------------------
print("\n" + "-" * 78)
print(" El error de la version vieja")
print("-" * 78)

barra, centro, leg_arr, leg_aba = vertice(40, 40, 4, "#3", "#8", 1)
u = unit(centro[0] - barra[0], centro[1] - barra[1])
u_viejo = rot45(u)

check("la seccion cuadrada tiene las diagonales a 45 grados del eje",
      abs(grados_entre((1.0, 0.0), leg_arr) - 45.0) < 1e-9,
      f"{grados_entre((1.0, 0.0), leg_arr):.6f}°")

# La tolerancia es 1e-4 grados y no 1e-9 porque RT2I viene recortada a 15 cifras en
# el C# y ese recorte solo vale 2e-6 grados: pedir 1e-9 seria medir el redondeo de la
# constante, no la geometria.
check("la cola VIEJA caia exactamente sobre la diagonal de arriba",
      grados_entre(u_viejo, leg_arr) < 1e-4,
      f"separacion {grados_entre(u_viejo, leg_arr):.6f}°")

check("y la NUEVA se separa de las dos por igual",
      abs(grados_entre(u, leg_arr) - 45.0) < 1e-9
      and abs(grados_entre(u, leg_aba) - 45.0) < 1e-9)

# ---------------------------------------------------------------------------
# 4. El gancho de norma: 135 grados respecto al acero que llega.
# ---------------------------------------------------------------------------
print("\n" + "-" * 78)
print(" El doblez de 135 grados")
print("-" * 78)

# El acero llega al vertice bajando por la diagonal de arriba: su direccion de
# avance es la contraria a leg_arriba.
avance_a = (-leg_arr[0], -leg_arr[1])
avance_b = (-leg_aba[0], -leg_aba[1])

print(f"    avance del acero por arriba {math.degrees(math.atan2(avance_a[1], avance_a[0])):+7.2f}°"
      f"   cola {math.degrees(math.atan2(u[1], u[0])):+7.2f}°"
      f"   doblez {grados_entre(avance_a, u):6.2f}°")
print(f"    avance del acero por abajo  {math.degrees(math.atan2(avance_b[1], avance_b[0])):+7.2f}°"
      f"   cola {math.degrees(math.atan2(u[1], u[0])):+7.2f}°"
      f"   doblez {grados_entre(avance_b, u):6.2f}°")

check("el extremo que llega por arriba dobla 135 grados",
      abs(grados_entre(avance_a, u) - 135.0) < 1e-9,
      f"{grados_entre(avance_a, u):.6f}°")
check("y el que llega por abajo, tambien",
      abs(grados_entre(avance_b, u) - 135.0) < 1e-9,
      f"{grados_entre(avance_b, u):.6f}°")

# El giro tiene que ser HACIA DENTRO. Doblar 135 grados admite dos soluciones, una a
# cada lado del acero, y la otra saca la cola de la seccion. Se comprueba que la
# eleccion no es casual: de las dos, solo una entra al nucleo.
hacia_nucleo = unit(centro[0] - barra[0], centro[1] - barra[1])

candidatas = [rotar(avance_a, +135), rotar(avance_a, -135)]

for c in candidatas:
    print(f"    doblando 135° -> {math.degrees(math.atan2(c[1], c[0])):+7.2f}°"
          f"   proyeccion sobre el radio al nucleo {c[0] * hacia_nucleo[0] + c[1] * hacia_nucleo[1]:+.4f}")

check("las dos formas de doblar 135 grados dan 135 grados",
      all(abs(grados_entre(avance_a, c) - 135.0) < 1e-9 for c in candidatas))

dentro = [c for c in candidatas if c[0] * hacia_nucleo[0] + c[1] * hacia_nucleo[1] > 1e-9]

check("y solo UNA de las dos entra al nucleo", len(dentro) == 1,
      f"entran {len(dentro)}")
check("la que entra es la que dibuja el programa",
      len(dentro) == 1
      and abs(dentro[0][0] - u[0]) < 1e-9 and abs(dentro[0][1] - u[1]) < 1e-9)

# ---------------------------------------------------------------------------
# 5. El doblez envuelve el lado OPUESTO a las colas.
# ---------------------------------------------------------------------------
print("\n" + "-" * 78)
print(" El doblez y el recorte")
print("-" * 78)

# Como en el C#: las normales de arranque son las perpendiculares a la cola, y el
# sector va de una a la otra, media corona.
n1 = (-u[1], u[0])
n2 = (u[1], -u[0])
a1 = math.atan2(n1[1], n1[0])

medio = (math.cos(a1 + math.pi / 2), math.sin(a1 + math.pi / 2))

check("el doblez es media corona",
      abs(((a1 + math.pi) - a1) - math.pi) < 1e-12)
check("y su punto medio cae en el lado opuesto a las colas",
      abs(medio[0] + u[0]) < 1e-12 and abs(medio[1] + u[1]) < 1e-12,
      f"medio ({medio[0]:+.4f},{medio[1]:+.4f}) contra -u ({-u[0]:+.4f},{-u[1]:+.4f})")
check("las dos colas arrancan en lados opuestos de la varilla",
      abs(n1[0] + n2[0]) < 1e-12 and abs(n1[1] + n2[1]) < 1e-12)
check("y sus arranques son perpendiculares a la cola",
      abs(n1[0] * u[0] + n1[1] * u[1]) < 1e-12)

# 6. El recorte: la cola no puede cruzar el centro de la seccion.
r_in = barra[2]
d_dia = DIAM["#3"]
pi_x = barra[0] + r_in * n1[0]
pi_y = barra[1] + r_in * n1[1]
tope = (centro[0] - pi_x) * u[0] + (centro[1] - pi_y) * u[1]

gancho_pedido = 12 * DIAM["#8"]      # 12 db, el gancho de norma
gancho = min(gancho_pedido, tope) if tope > 0 else gancho_pedido

print(f"    tope hasta el centro {tope:.2f} cm"
      f"   gancho pedido {gancho_pedido:.2f} cm   dibujado {gancho:.2f} cm")

check("el tope de la cola es positivo", tope > 0, f"{tope:.4f} cm")
check("un gancho de 5 cm cabe sin recortarse", 5.0 <= tope, f"tope {tope:.2f} cm")
check("y la cola dibujada nunca pasa del centro",
      gancho <= tope + 1e-12,
      f"cola {gancho:.4f} cm, tope {tope:.4f} cm")

fin_x = pi_x + gancho * u[0]
fin_y = pi_y + gancho * u[1]
check("la punta de la cola queda dentro del nucleo",
      (centro[0] - fin_x) * u[0] + (centro[1] - fin_y) * u[1] >= -1e-12,
      f"punta en ({fin_x:.2f},{fin_y:.2f}), centro en ({centro[0]:.2f},{centro[1]:.2f})")

# ---------------------------------------------------------------------------
# 7. Es la MISMA regla que el gancho del estribo rectangular.
# ---------------------------------------------------------------------------
print("\n" + "-" * 78)
print(" La misma regla que el estribo rectangular")
print("-" * 78)

# En el C# el rectangular la trae escrita a mano: u = (-Rt2I, -Rt2I) en la esquina
# de arriba a la derecha. Se comprueba que eso ES el radio hacia el nucleo, o sea
# que las dos partes del programa siguen la misma regla y no dos distintas.
b = h = 40.0
rec, d_est, r_var = 4.0, DIAM["#3"], DIAM["#8"] / 2
esquina = (b - rec - d_est - r_var, h - rec - d_est - r_var)
centro_sec = (b / 2, h / 2)
u_esq = unit(centro_sec[0] - esquina[0], centro_sec[1] - esquina[1])

print(f"    esquina ({esquina[0]:.2f},{esquina[1]:.2f})"
      f"   radio al nucleo ({u_esq[0]:+.6f},{u_esq[1]:+.6f})"
      f"   el C# usa ({-RT2I:+.6f},{-RT2I:+.6f})")

check("en el rectangular, la cola escrita a mano ES el radio hacia el nucleo",
      abs(u_esq[0] + RT2I) < 1e-12 and abs(u_esq[1] + RT2I) < 1e-12)


# ===========================================================================
#  NINGUNA LINEA DEL GANCHO DEBE QUEDAR DENTRO DEL ACERO DEL DIAMANTE
# ===========================================================================
#
# El defecto que el usuario ve: rayas negras cruzando el relleno azul por encima y
# por debajo de la varilla. Son lineas del gancho dibujadas donde la cinta del
# diamante ya esta, o sea rayas por dentro del acero. En el dibujo eso se lee como
# una grieta, no como un contorno.
#
# Aqui se calcula, con la MISMA geometria con la que se dibuja la cinta, que parte de
# cada linea del gancho cae dentro de ese acero. Se comprueba:
#
#   1. Que el arco del doblez, fuera de donde la cinta abraza la varilla, cae dentro
#      del acero: era la raya que se veia. Por eso ya no se dibuja.
#   2. Que entre los dos puntos de tangencia ese arco ES el borde exterior de la
#      cinta -mismo centro, mismo radio, mismo barrido-, o sea que no se pierde
#      ninguna linea al no dibujarlo: ya estaba dibujada.
#   3. Que la linea exterior de cada cola arranca dentro del acero, y que el punto de
#      salida que calcula el programa es el de verdad, comparado contra un muestreo
#      fino e independiente.
#   4. Que despues del recorte NINGUN punto de NINGUNA linea del gancho queda dentro
#      del acero del diamante.

print("\n" + "=" * 78)
print(" Lineas del gancho contra el acero del diamante")
print("=" * 78)


def geometria_cinta(centros, extra):
    """Port de GeometriaCinta: los puntos de tangencia de la cinta."""
    n = len(centros)

    if n < 3:
        return None

    r = [c[2] + extra for c in centros]

    if any(v <= 0 for v in r):
        return None

    mx = [0.0] * n
    my = [0.0] * n

    for i in range(n):
        j = (i + 1) % n
        dx = centros[j][0] - centros[i][0]
        dy = centros[j][1] - centros[i][1]
        d = math.hypot(dx, dy)

        if d < 1e-7:
            return None

        vx, vy = dx / d, dy / d
        cc = max(-0.999999, min(0.999999, (r[i] - r[j]) / d))
        ss = math.sqrt(1 - cc * cc)

        mx[i] = cc * vx + ss * vy
        my[i] = cc * vy - ss * vx

    pts = [0.0] * (4 * n)

    for i in range(n):
        p = (i + n - 1) % n
        pts[4 * i + 0] = centros[i][0] + r[i] * mx[p]
        pts[4 * i + 1] = centros[i][1] + r[i] * my[p]
        pts[4 * i + 2] = centros[i][0] + r[i] * mx[i]
        pts[4 * i + 3] = centros[i][1] + r[i] * my[i]

    return pts


def punto_en_poligono(px, py, pts):
    """Port de PuntoEnPoligono: conteo de cruces."""
    n = len(pts) // 2
    dentro = False
    j = n - 1

    for i in range(n):
        xi, yi = pts[2 * i], pts[2 * i + 1]
        xj, yj = pts[2 * j], pts[2 * j + 1]

        if (yi > py) != (yj > py) and px < (xj - xi) * (py - yi) / (yj - yi) + xi:
            dentro = not dentro

        j = i

    return dentro


def en_region(p, centros, extra):
    """Lo que encierra un borde de la cinta.

    Es la caracterizacion exacta que ya usaba el C# para recortar el estribo bajo el
    diamante: la region que encierra el borde es la union de los DISCOS de radio
    R+extra centrados en los circulos que la cinta abraza, mas el POLIGONO que pasa
    por los puntos de tangencia. Los discos cubren los dobleces y el poligono los
    tramos rectos.
    """
    for (cx_, cy_, r_) in centros:
        if math.hypot(p[0] - cx_, p[1] - cy_) < r_ + extra:
            return True

    pts = geometria_cinta(centros, extra)

    return pts is not None and punto_en_poligono(p[0], p[1], pts)


def dentro_del_acero(p, centros, d_dia, margen=0.0):
    """Si el punto cae en el acero de la cinta: dentro del borde exterior y fuera del
    interior. Con 'margen' se mide 'bien dentro', para no discutir el pelo del borde.
    """
    return (en_region(p, centros, d_dia - margen)
            and not en_region(p, centros, margen))


def salida_del_acero(pts, centros, i_barra, n_, p, u, largo):
    """Port de SalidaDelAceroDelDiamante."""
    n = len(centros)

    if n < 3 or i_barra < 0:
        return None

    c = centros[i_barra]

    llega = (pts[4 * i_barra], pts[4 * i_barra + 1])
    sale = (pts[4 * i_barra + 2], pts[4 * i_barra + 3])

    lado_llega = ((llega[0] - c[0]) * n_[0] + (llega[1] - c[1]) * n_[1]) / c[2]
    lado_sale = ((sale[0] - c[0]) * n_[0] + (sale[1] - c[1]) * n_[1]) / c[2]

    if lado_llega >= lado_sale:
        previo = (i_barra - 1) % n
        a = (pts[4 * previo + 2], pts[4 * previo + 3])
        b = llega
    else:
        sig = (i_barra + 1) % n
        a = sale
        b = (pts[4 * sig], pts[4 * sig + 1])

    dx, dy = b[0] - a[0], b[1] - a[1]
    cruz = u[0] * dy - u[1] * dx

    if abs(cruz) < 1e-12:
        return None

    rx, ry = a[0] - p[0], a[1] - p[1]
    t = (rx * dy - ry * dx) / cruz
    s = (rx * u[1] - ry * u[0]) / cruz

    if t <= 1e-12 or t >= largo or s < -1e-9 or s > 1 + 1e-9:
        return None

    return (p[0] + t * u[0], p[1] + t * u[1]), t


def armado(b, h, rec, est, var, n_lat_izq=1, sep=10.0):
    """Los circulos que el diamante abraza, en el orden del recorrido: derecha,
    arriba, izquierda, abajo. Antihorario, como exige la cinta."""
    d_est = DIAM[est]
    r = DIAM[var] / 2

    cx, cy = b / 2, h / 2
    xl = rec + d_est + r
    xr = b - rec - d_est - r
    yt = h - rec - d_est - r
    yb = rec + d_est + r

    centros = [(xr, cy, r), (cx, yt, r)]

    if n_lat_izq == 1:
        centros.append((xl, cy, r))
        i_barra = 2
    else:
        centros.append((xl, cy + sep / 2, r))
        centros.append((xl, cy - sep / 2, r))
        i_barra = 2      # el gancho va en la de ARRIBA

    centros.append((cx, yb, r))

    return centros, i_barra, (cx, cy)


def lineas_del_gancho(centros, i_barra, centro, d_dia, gancho_cm, recortar=True):
    """Las lineas que el programa dibuja del gancho, ya recortadas.

    Devuelve una lista de (nombre, (x0,y0), (x1,y1)) y el recorte aplicado a cada
    cola, para poder comprobarlas punto a punto.
    """
    barra = centros[i_barra]
    r_in = barra[2]
    r_out = r_in + d_dia

    u = unit(centro[0] - barra[0], centro[1] - barra[1])
    n1 = (-u[1], u[0])
    n2 = (u[1], -u[0])

    # El tope hacia el nucleo, igual que el C#.
    pi1 = (barra[0] + r_in * n1[0], barra[1] + r_in * n1[1])
    tope = (centro[0] - pi1[0]) * u[0] + (centro[1] - pi1[1]) * u[1]
    largo = min(gancho_cm, tope) if tope > 0 else gancho_cm

    pts_int = geometria_cinta(centros, 0)

    lineas = []
    recortes = {}

    for nombre, nn in (("cola de arriba", n1), ("cola de abajo", n2)):
        p_in = (barra[0] + r_in * nn[0], barra[1] + r_in * nn[1])
        p_out = (barra[0] + r_out * nn[0], barra[1] + r_out * nn[1])

        q_in = (p_in[0] + largo * u[0], p_in[1] + largo * u[1])
        q_out = (p_out[0] + largo * u[0], p_out[1] + largo * u[1])

        arranque = p_out

        if recortar:
            sal = salida_del_acero(pts_int, centros, i_barra, nn, p_out, u, largo)

            if sal is not None:
                arranque, t = sal
                recortes[nombre] = t

        lineas.append((f"{nombre}: linea interior", p_in, q_in))
        lineas.append((f"{nombre}: linea exterior", arranque, q_out))
        lineas.append((f"{nombre}: punta", q_in, q_out))

    return lineas, recortes, largo, u, (n1, n2)


CASOS_LINEAS = [
    # nombre,                                b,   h, rec, est,  var, n_lat, gancho
    ("Columna 40x40, 1 lateral",             40,  40,   4, "#3", "#8", 1, 5.0),
    ("Columna 30x60, 1 lateral",             30,  60,   4, "#3", "#8", 1, 5.0),
    ("Trabe 60x30, 1 lateral",               60,  30,   4, "#3", "#6", 1, 5.0),
    ("Columna 100x100, 1 lateral",          100, 100,   5, "#4", "#8", 1, 8.0),
    ("Columna 40x40, 2 laterales",           40,  40,   4, "#3", "#8", 2, 5.0),
]

MARGEN = 0.02      # 0.2 mm: 'bien dentro' del acero, sin discutir el pelo del borde
MUESTRAS = 400

hubo_defecto = []
arco_metido = []

for nombre, b, h, rec, est, var, n_lat, gancho_cm in CASOS_LINEAS:
    d_dia = DIAM[est]
    centros, i_barra, centro = armado(b, h, rec, est, var, n_lat)
    barra = centros[i_barra]
    r_in = barra[2]
    r_out = r_in + d_dia

    print(f"\n{nombre}")

    # ---- 1. El arco del doblez, fuera de la tangencia, cae DENTRO del acero ----
    pts_ext = geometria_cinta(centros, d_dia)
    llega = (pts_ext[4 * i_barra], pts_ext[4 * i_barra + 1])
    sale = (pts_ext[4 * i_barra + 2], pts_ext[4 * i_barra + 3])

    a_llega = math.degrees(math.atan2(llega[1] - barra[1], llega[0] - barra[0])) % 360
    a_sale = math.degrees(math.atan2(sale[1] - barra[1], sale[0] - barra[0])) % 360

    u = unit(centro[0] - barra[0], centro[1] - barra[1])
    n1 = (-u[1], u[0])
    a1 = math.degrees(math.atan2(n1[1], n1[0])) % 360      # arranque del arco del C#

    print(f"    la cinta abraza la varilla de {min(a_llega, a_sale):.2f}° a "
          f"{max(a_llega, a_sale):.2f}°   el arco del gancho iba de {a1:.2f}° a "
          f"{(a1 + 180) % 360:.2f}°")

    # El radio del arco del gancho y el del borde exterior de la cinta son el MISMO.
    check(f"'{nombre}': el arco del gancho tiene el radio del borde de la cinta",
          abs(math.hypot(llega[0] - barra[0], llega[1] - barra[1]) - r_out) < 1e-12
          and abs(math.hypot(sale[0] - barra[0], sale[1] - barra[1]) - r_out) < 1e-12)

    # Los trozos del arco que se salen del abrazo de la cinta: dentro del acero.
    dentro_fuera_del_abrazo = 0
    total_fuera_del_abrazo = 0
    dentro_del_abrazo = 0
    total_del_abrazo = 0

    lo = min(a_llega, a_sale)
    hi = max(a_llega, a_sale)

    for k in range(MUESTRAS + 1):
        ang = a1 + 180.0 * k / MUESTRAS
        p = (barra[0] + r_out * math.cos(math.radians(ang)),
             barra[1] + r_out * math.sin(math.radians(ang)))

        en_abrazo = lo - 1e-9 <= ang % 360 <= hi + 1e-9

        if en_abrazo:
            total_del_abrazo += 1
            dentro_del_abrazo += 1 if dentro_del_acero(p, centros, d_dia, MARGEN) else 0
        else:
            total_fuera_del_abrazo += 1
            dentro_fuera_del_abrazo += 1 if dentro_del_acero(p, centros, d_dia, MARGEN) else 0

    # Se mide en CENTIMETROS de arco, no en porcentaje de muestras. El porcentaje no
    # sirve como condicion: el arco va justo por el borde exterior de la cinta, asi que
    # cerca de la tangencia las muestras caen sobre el borde y no 'bien dentro'. Lo que
    # importa es si el trozo metido en el relleno es lo bastante largo para VERSE.
    largo_muestra = math.radians(180.0 / MUESTRAS) * r_out
    largo_dentro = dentro_fuera_del_abrazo * largo_muestra

    print(f"    del arco fuera del abrazo caian {largo_dentro:.2f} cm dentro del acero "
          f"({dentro_fuera_del_abrazo} de {total_fuera_del_abrazo} muestras)")

    check(f"'{nombre}': el arco cruzaba el acero por un tramo bien visible",
          largo_dentro > 0.2,
          f"solo {largo_dentro:.4f} cm")

    arco_metido.append(largo_dentro)

    # Y dentro del abrazo el arco ES el borde: ni dentro ni fuera, esta EN el borde.
    # Se comprueba por el otro lado: ninguna muestra del abrazo esta bien dentro.
    check(f"'{nombre}': dentro del abrazo el arco es el propio borde de la cinta",
          dentro_del_abrazo == 0,
          f"{dentro_del_abrazo} de {total_del_abrazo} muestras dentro")

    # ---- 2. El punto de salida del programa contra un muestreo independiente ----
    lineas, recortes, largo, u, (n1, n2) = lineas_del_gancho(
        centros, i_barra, centro, d_dia, gancho_cm)

    for etiqueta, nn in (("cola de arriba", n1), ("cola de abajo", n2)):
        p_out = (barra[0] + r_out * nn[0], barra[1] + r_out * nn[1])

        # Muestreo fino: el primer punto de la linea que ya NO esta en el acero.
        paso = largo / 20000
        t_muestreo = None

        for k in range(20001):
            t = k * paso
            p = (p_out[0] + t * u[0], p_out[1] + t * u[1])

            if not dentro_del_acero(p, centros, d_dia, 0.0):
                t_muestreo = t
                break

        t_prog = recortes.get(etiqueta)

        print(f"    {etiqueta}: recorte del programa "
              f"{('%.4f cm' % t_prog) if t_prog is not None else 'ninguno'}"
              f"   por muestreo {('%.4f cm' % t_muestreo) if t_muestreo is not None else 'nunca sale'}")

        # No siempre hay que recortar, y eso NO es un fallo: si la diagonal llega muy
        # empinada -una columna alta- la cola arranca ya fuera del acero de la cinta y
        # no hay nada que quitar. Lo que se exige es que el programa recorte cuando hace
        # falta, donde hace falta, y que no recorte cuando no.
        arranca_dentro = t_muestreo is None or t_muestreo > 2 * paso

        if arranca_dentro:
            check(f"'{nombre}', {etiqueta}: el programa recorta la linea que entra "
                  "en el acero", t_prog is not None)

            if t_prog is not None and t_muestreo is not None:
                check(f"'{nombre}', {etiqueta}: y recorta donde de verdad sale del acero",
                      abs(t_prog - t_muestreo) <= 2 * paso,
                      f"programa {t_prog:.6f}, muestreo {t_muestreo:.6f}, "
                      f"paso {paso:.6f}")
        else:
            check(f"'{nombre}', {etiqueta}: no recorta nada donde no hace falta",
                  t_prog is None,
                  f"recorto {t_prog} sin necesidad")

    # ---- 3. Ya recortadas, ninguna linea queda dentro del acero ----
    peores = []

    for etiqueta, p0, p1 in lineas:
        dentro = 0

        for k in range(MUESTRAS + 1):
            f = k / MUESTRAS
            p = (p0[0] + f * (p1[0] - p0[0]), p0[1] + f * (p1[1] - p0[1]))

            if dentro_del_acero(p, centros, d_dia, MARGEN):
                dentro += 1

        if dentro:
            peores.append(f"{etiqueta}: {dentro}/{MUESTRAS + 1}")

    check(f"'{nombre}': ninguna linea del gancho queda dentro del acero",
          not peores, "; ".join(peores))

    # ---- 4. Y sin recortar SI quedaban: el defecto era real ----
    sin_recortar, _, _, _, _ = lineas_del_gancho(
        centros, i_barra, centro, d_dia, gancho_cm, recortar=False)

    metidas = 0

    for etiqueta, p0, p1 in sin_recortar:
        for k in range(MUESTRAS + 1):
            f = k / MUESTRAS
            p = (p0[0] + f * (p1[0] - p0[0]), p0[1] + f * (p1[1] - p0[1]))

            if dentro_del_acero(p, centros, d_dia, MARGEN):
                metidas += 1

    print(f"    sin recortar quedaban {metidas} muestras de linea dentro del acero")

    if metidas > 0:
        hubo_defecto.append(f"{nombre}: {metidas} muestras")

# El defecto no aparece en TODOS los armados -en una columna alta la cola nace ya fuera
# del acero-, asi que se exige que aparezca en alguno: si no apareciera en ninguno, el
# recorte no estaria probado y podria estar sin hacer nada.
print()
check("el recorte de las colas hace falta de verdad en algun armado",
      len(hubo_defecto) > 0)

# El arco, en cambio, se metia en el acero en TODOS los armados probados. Por eso no se
# recorta: se deja de dibujar, que su parte visible ya la traza la cinta.
check("el arco del doblez se metia en el acero en todos los armados",
      len(arco_metido) == len(CASOS_LINEAS) and min(arco_metido) > 0.2,
      f"el menor fue {min(arco_metido):.4f} cm" if arco_metido else "sin datos")

print(f"    armados donde la cola entraba en el acero: {len(hubo_defecto)} de "
      f"{len(CASOS_LINEAS)}")
print(f"    arco metido en el acero: de {min(arco_metido):.2f} a "
      f"{max(arco_metido):.2f} cm")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")

if avisos:
    print(f"\n {len(avisos)} AVISO(S), que no son fallos:")
    for a in avisos:
        print("   - " + a)
print("=" * 78)

raise SystemExit(1 if fallos else 0)
