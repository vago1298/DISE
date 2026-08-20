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
