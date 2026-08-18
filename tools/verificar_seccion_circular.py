"""Comprueba la geometria de la seccion circular y de la helice del zuncho.

En este entorno no hay .NET ni AutoCAD, asi que correr las formulas aparte es la
unica forma honesta de comprobarlas antes de escribirlas en C#.

Lo que se comprueba:

  1. El circulo de paso: donde van los centros de las varillas, y que el acero
     quepa dentro del zuncho sin salirse del recubrimiento.
  2. El reparto angular de las varillas: N varillas repartidas por igual, con la
     primera arriba, y que ninguna se pise con la siguiente.
  3. El anillo del zuncho: radio exterior e interior, y que sea una corona de un
     diametro de ancho, no una linea.
  4. La HELICE proyectada del alzado: que su proyeccion sea un seno de amplitud
     igual al radio del zuncho y periodo igual al paso, que arranque y termine
     dentro del elemento, y que suba de forma monotona.
  5. Que el zuncho NORMAL (anillos) y el HELICOIDAL cubran la misma longitud con
     el mismo numero de vueltas: la diferencia es como se dibuja, no cuanto acero
     lleva.
"""

import math

ESCALA = 0.01

# Diametros comerciales en cm, la misma tabla del programa
DIAM = {
    "#2": 0.60, "#2.5": 0.80, "#3": 0.95, "#4": 1.27,
    "#5": 1.59, "#6": 1.90, "#8": 2.54, "#10": 3.20, "#12": 3.80,
}

fallos = []


def check(nombre, cond, detalle=""):
    print(("  OK    " if cond else "  FALLA ") + nombre + ("" if cond else "  " + detalle))
    if not cond:
        fallos.append(nombre)


# ======================================================================
# 1 y 2. Circulo de paso y reparto de las varillas
# ======================================================================

def circulo_de_paso(diam_cm, rec_cm, est, var):
    """Radio del circulo donde van los CENTROS de las varillas, en metros."""
    r = diam_cm * ESCALA / 2.0
    rec = rec_cm * ESCALA
    d_est = DIAM[est] * ESCALA
    d_var = DIAM[var] * ESCALA
    return r - rec - d_est - d_var / 2.0, r, rec, d_est, d_var


def varillas(n, r_paso, cx=0.0, cy=0.0):
    """Centros de las n varillas, repartidas por igual, la primera ARRIBA.

    Arrancar arriba y girar en sentido antihorario no es un capricho: es lo que
    hace que con 4 varillas queden a 12, 3, 6 y 9 en punto, que es como se arma y
    como se espera ver en el plano.
    """
    out = []
    for i in range(n):
        a = math.pi / 2.0 + i * 2.0 * math.pi / n
        out.append((cx + r_paso * math.cos(a), cy + r_paso * math.sin(a)))
    return out


print("=" * 78)
print(" Seccion circular: columna D=50, rec 4, zuncho #3, 8 varillas #8")
print("=" * 78)

r_paso, r_ext, rec, d_est, d_var = circulo_de_paso(50, 4, "#3", "#8")
print(f"  radio exterior  = {r_ext:.4f} m")
print(f"  recubrimiento   = {rec:.4f} m")
print(f"  diam. zuncho    = {d_est:.4f} m")
print(f"  diam. varilla   = {d_var:.4f} m")
print(f"  radio de paso   = {r_paso:.4f} m")

check("el radio de paso es positivo", r_paso > 0)

# La varilla completa tiene que caber DENTRO del zuncho, sin morder el recubrimiento
check("la varilla cabe dentro del zuncho",
      r_paso + d_var / 2.0 <= r_ext - rec - d_est + 1e-12,
      f"borde de varilla {r_paso + d_var/2:.4f} > limite {r_ext - rec - d_est:.4f}")

vs = varillas(8, r_paso)
check("se generan las 8 varillas", len(vs) == 8)

# La primera arriba
check("la primera varilla queda ARRIBA",
      abs(vs[0][0]) < 1e-12 and abs(vs[0][1] - r_paso) < 1e-12,
      f"quedo en {vs[0]}")

# Todas sobre el circulo de paso
sobre = all(abs(math.hypot(x, y) - r_paso) < 1e-12 for x, y in vs)
check("todas caen sobre el circulo de paso", sobre)

# Reparto uniforme: la cuerda entre contiguas es constante
cuerdas = [math.hypot(vs[i][0] - vs[(i + 1) % 8][0], vs[i][1] - vs[(i + 1) % 8][1])
           for i in range(8)]
check("estan repartidas por igual", max(cuerdas) - min(cuerdas) < 1e-12,
      f"cuerdas de {min(cuerdas):.6f} a {max(cuerdas):.6f}")

# La cuerda teorica
teorica = 2 * r_paso * math.sin(math.pi / 8)
check("la cuerda coincide con 2*r*sin(pi/n)", abs(cuerdas[0] - teorica) < 1e-12)

libre = cuerdas[0] - d_var
print(f"  separacion libre entre varillas = {libre*100:.2f} cm")
check("las varillas no se pisan", libre > 0, f"se traslapan {-libre*100:.2f} cm")

# Dos casos que la comprobacion tiene que separar, y que se calcularon a mano
# porque «a ojo» los dos parecen imposibles y solo uno lo es:
#
#   20 #12 en 30 cm -> se TRASLAPAN, hay que rechazarlo
#   12 #10 en 30 cm -> caben, pero a 1.17 cm libres, menos de un diametro:
#                      no es un error de dibujo, es un aviso de norma
r_paso_mal, _, _, _, d_var_mal = circulo_de_paso(30, 4, "#3", "#12")
cuerda_mal = 2 * r_paso_mal * math.sin(math.pi / 20)
libre_mal = cuerda_mal - d_var_mal
print(f"  20 varillas #12 en D=30: libre = {libre_mal*100:.2f} cm")
check("20 varillas #12 en una columna de 30 cm se detectan como imposibles",
      libre_mal < 0, f"libre {libre_mal*100:.2f} cm")

r_paso_apr, _, _, _, d_var_apr = circulo_de_paso(30, 4, "#3", "#10")
libre_apr = 2 * r_paso_apr * math.sin(math.pi / 12) - d_var_apr
print(f"  12 varillas #10 en D=30: libre = {libre_apr*100:.2f} cm")
check("12 varillas #10 en D=30 caben, pero con menos de un diametro libre",
      0 < libre_apr < d_var_apr,
      f"libre {libre_apr*100:.2f} cm, diametro {d_var_apr*100:.2f} cm")

# ======================================================================
# 3. El anillo del zuncho
# ======================================================================
print("\nAnillo del zuncho (la corona que se dibuja en la seccion)")

r_zun_ext = r_ext - rec           # cara exterior del zuncho
r_zun_int = r_zun_ext - d_est     # cara interior

print(f"  radio exterior del zuncho = {r_zun_ext:.4f} m")
print(f"  radio interior del zuncho = {r_zun_int:.4f} m")

check("el zuncho queda dentro del concreto", r_zun_ext < r_ext)
check("el zuncho es una corona de un diametro de ancho",
      abs((r_zun_ext - r_zun_int) - d_est) < 1e-12)
check("el radio interior sigue siendo positivo", r_zun_int > 0)

# Con un recubrimiento absurdo el zuncho no cabe: hay que detectarlo, no dibujar
# un anillo de radio negativo.
r_mal = 20 * ESCALA / 2.0
check("un recubrimiento mayor que el radio se detecta",
      r_mal - (12 * ESCALA) - d_est <= 0)

# ======================================================================
# 4. La HELICE proyectada del alzado
# ======================================================================
print("\nHelice del zuncho en el alzado (zuncho helicoidal)")

# La helice de radio r y paso p, vista de frente, se proyecta como
#     x(t) = r*sin(2*pi*t/p)      y(t) = t
# Es un seno de amplitud r y periodo p. No es una aproximacion: es la proyeccion
# exacta sobre el plano del alzado.

def helice(r, paso, largo, por_vuelta=24):
    """Puntos de la helice proyectada, de y=0 a y=largo."""
    if paso <= 0 or largo <= 0:
        return []
    vueltas = largo / paso
    n = max(2, int(math.ceil(vueltas * por_vuelta)))
    return [(r * math.sin(2 * math.pi * (i / n) * vueltas), largo * i / n)
            for i in range(n + 1)]


paso = 0.10          # 10 cm de separacion = paso de la helice
largo = 3.0          # columna de 3 m
r_h = r_zun_ext - d_est / 2.0   # el eje del zuncho

pts = helice(r_h, paso, largo)
print(f"  paso = {paso} m   largo = {largo} m   vueltas = {largo/paso:.1f}")
print(f"  radio del eje del zuncho = {r_h:.4f} m")
print(f"  {len(pts)} puntos en la polilinea")

check("la helice arranca en la base", abs(pts[0][1]) < 1e-12)
check("y termina exactamente en el tope", abs(pts[-1][1] - largo) < 1e-12,
      f"termino en {pts[-1][1]:.6f}")

# Sube siempre: si no fuera monotona, la polilinea se doblaria sobre si misma
monotona = all(pts[i + 1][1] > pts[i][1] - 1e-15 for i in range(len(pts) - 1))
check("sube de forma monotona", monotona)

# La amplitud es el radio: la helice toca los dos paños del zuncho y no se sale
amp = max(abs(x) for x, _ in pts)
check("la amplitud no pasa del radio del zuncho", amp <= r_h + 1e-12,
      f"amplitud {amp:.6f} > radio {r_h:.6f}")
check("y llega practicamente al radio", amp > r_h * 0.95,
      f"amplitud {amp:.6f} contra radio {r_h:.6f}")

# Numero de cruces por el eje = 2 por vuelta. Es la comprobacion de que el paso
# se respeta: si el periodo estuviera mal, saldrian mas o menos cruces.
cruces = sum(1 for i in range(len(pts) - 1)
             if (pts[i][0] <= 0 < pts[i + 1][0]) or (pts[i][0] >= 0 > pts[i + 1][0]))
esperados = 2 * (largo / paso)
print(f"  cruces por el eje = {cruces}   esperados ~= {esperados:.0f}")
check("el paso de la helice se respeta", abs(cruces - esperados) <= 1,
      f"{cruces} contra {esperados:.0f}")

# Resolucion: con pocos puntos por vuelta el seno sale como un zigzag. Se
# comprueba que el error de la cuerda contra el arco real sea pequeño.
por_vuelta = 24
paso_ang = 2 * math.pi / por_vuelta
error_rel = 1 - math.sin(paso_ang / 2) / (paso_ang / 2)
print(f"  con {por_vuelta} puntos por vuelta, el error de la cuerda es "
      f"{error_rel*100:.2f} %")
check("24 puntos por vuelta dan una curva suave", error_rel < 0.005,
      f"error {error_rel*100:.2f} %")

# ======================================================================
# 5. Helicoidal y normal llevan el MISMO acero
# ======================================================================
print("\nZuncho normal (anillos) contra helicoidal: el acero es el mismo")

# Anillos: uno cada 'paso' a lo largo del elemento
anillos = int(largo / paso)
largo_anillos = anillos * 2 * math.pi * r_h

# Helice: una vuelta por paso, y cada vuelta mide sqrt(perimetro^2 + paso^2),
# porque la vuelta va inclinada.
vueltas = largo / paso
largo_helice = vueltas * math.hypot(2 * math.pi * r_h, paso)

print(f"  anillos : {anillos} x {2*math.pi*r_h:.4f} m = {largo_anillos:.3f} m")
print(f"  helice  : {vueltas:.1f} vueltas = {largo_helice:.3f} m")

check("las dos formas dan el mismo numero de vueltas",
      abs(vueltas - anillos) < 1.0, f"{vueltas:.1f} contra {anillos}")

# La helice es un poco mas larga porque va inclinada. Con un paso chico la
# diferencia es minima; se comprueba que sea del orden esperado y no del doble.
exceso = largo_helice / largo_anillos - 1
print(f"  la helice es un {exceso*100:.2f} % mas larga (va inclinada)")
check("la helice es solo un poco mas larga que los anillos",
      0 < exceso < 0.02, f"exceso {exceso*100:.2f} %")

# ======================================================================
# 6. La BANDA del zuncho: las dos caras de la helice
# ======================================================================
print("\nBanda del zuncho: la helice tiene grosor, no es una linea")

# La barra del zuncho tiene diametro d. Su superficie EXTERIOR es una helice de
# radio r + d/2 y la INTERIOR una de radio r - d/2. La proyeccion de cada una es
# un seno de SU radio, con la misma fase. Asi que la banda se dibuja con dos
# senos de amplitudes distintas, no con un seno desplazado en Y.
#
# Esto importa: desplazar el seno en Y daria una banda de grosor constante
# medido en vertical, y el grosor real de la proyeccion se estrecha donde la
# helice cruza el eje, porque ahi la barra va de perfil.
r_out = r_h + d_est / 2
r_in = r_h - d_est / 2

pts_out = helice(r_out, paso, largo)
pts_in = helice(r_in, paso, largo)

check("las dos caras tienen el mismo numero de puntos", len(pts_out) == len(pts_in))
check("van en fase", all(
    (a[0] >= 0) == (b[0] >= 0) for a, b in zip(pts_out, pts_in)))
check("la cara exterior siempre envuelve a la interior", all(
    abs(a[0]) >= abs(b[0]) - 1e-15 for a, b in zip(pts_out, pts_in)))

# En el punto de maxima amplitud la banda mide justo un diametro
amp_out = max(abs(x) for x, _ in pts_out)
amp_in = max(abs(x) for x, _ in pts_in)
print(f"  amplitud exterior = {amp_out:.4f} m   interior = {amp_in:.4f} m")
check("en el maximo la banda mide un diametro",
      abs((amp_out - amp_in) - d_est) < 1e-6,
      f"mide {(amp_out - amp_in):.6f} y el diametro es {d_est:.6f}")

# Y la cara exterior no se sale del concreto
check("la cara exterior del zuncho no se sale del recubrimiento",
      amp_out <= r_ext - rec + 1e-12,
      f"{amp_out:.6f} > {r_ext - rec:.6f}")

# ======================================================================
# 7. Zuncho en ANILLOS: su proyeccion en el alzado
# ======================================================================
print("\nZuncho en anillos: su proyeccion en el alzado es una capsula")

# Un anillo visto de lado se proyecta como un rectangulo de ancho = diametro
# exterior del anillo y alto = diametro de la barra. Es exactamente la capsula
# que ya dibuja el alzado rectangular, asi que el caso 'normal' NO necesita
# geometria nueva: se reutiliza.
ancho_proy = 2 * r_out
alto_proy = d_est
print(f"  ancho proyectado = {ancho_proy:.4f} m   alto = {alto_proy:.4f} m")

check("el anillo proyectado ocupa todo el ancho entre recubrimientos",
      abs(ancho_proy - 2 * (r_ext - rec)) < 1e-12,
      f"{ancho_proy:.6f} contra {2*(r_ext-rec):.6f}")
check("y su alto es el diametro de la barra", abs(alto_proy - d_est) < 1e-15)

# ======================================================================
# 8. Varillas longitudinales proyectadas en el alzado
# ======================================================================
print("\nVarillas longitudinales: donde se ven en el alzado")

# Una varilla en el angulo a se ve en el alzado a una distancia r*cos(a) del eje.
# Las parejas simetricas se proyectan EN EL MISMO SITIO, asi que hay que quitar
# repetidas o se dibujarian varillas encima de otras.
def proyectadas(n, r_paso, tol=1e-9):
    xs = []
    for i in range(n):
        a = math.pi / 2 + i * 2 * math.pi / n
        x = r_paso * math.cos(a)
        if not any(abs(x - y) < tol for y in xs):
            xs.append(x)
    return sorted(xs)


for n in (4, 6, 8, 12):
    pr = proyectadas(n, r_paso)
    print(f"  {n:2d} varillas -> {len(pr)} posiciones distintas en el alzado")

check("8 varillas se ven en 5 posiciones distintas",
      len(proyectadas(8, r_paso)) == 5,
      f"salieron {len(proyectadas(8, r_paso))}")
check("4 varillas se ven en 3 posiciones distintas",
      len(proyectadas(4, r_paso)) == 3)
check("ninguna proyeccion se sale del circulo de paso",
      all(abs(x) <= r_paso + 1e-12 for x in proyectadas(8, r_paso)))

# ======================================================================
# 9. Como se le da GROSOR al zuncho, y por que las dos vias obvias fallan
# ======================================================================
print("\nGrosor del zuncho: las dos vias que NO sirven, y la que si")

# ------------------------------------------------------------------
# VIA 1: contorno cerrado con las dos caras, ida por fuera y vuelta por dentro
# ------------------------------------------------------------------
# Parece la solucion evidente para poder rellenar con un hatch. No sirve, y el
# motivo es de signo: las caras son R*sin(f) y r*sin(f) con R > r, asi que donde
# sin(f) < 0 la cara exterior queda POR DEBAJO de la interior. La banda se cruza
# en cada paso por el eje y el area con signo se anula.

def contorno_radial(r_eje, d, paso, largo, por_vuelta=24):
    vueltas = largo / paso
    n = max(2, int(math.ceil(vueltas * por_vuelta)))
    dx = largo / n

    fases = []
    fase = 0.0
    for i in range(n + 1):
        if i > 0:
            fase += 2 * math.pi * dx / paso
        fases.append(fase)

    r_out, r_in = r_eje + d / 2, r_eje - d / 2
    pts = [(i * dx, r_out * math.sin(fases[i])) for i in range(n + 1)]
    pts += [((n - i) * dx, r_in * math.sin(fases[n - i])) for i in range(n + 1)]
    return pts


def area_con_signo(pts):
    return sum(pts[k][0] * pts[(k + 1) % len(pts)][1] -
               pts[(k + 1) % len(pts)][0] * pts[k][1]
               for k in range(len(pts))) / 2


cont = contorno_radial(r_h, d_est, paso, largo)
area_radial = area_con_signo(cont)
cruces_eje = int(2 * largo / paso)

print(f"  via 1, contorno radial : area = {area_radial:.9f} m2, "
      f"{cruces_eje} cruces por el eje")

check("la via 1 se descarta: encierra area cero", abs(area_radial) < 1e-9,
      f"area {area_radial:.9f}")

# ------------------------------------------------------------------
# VIA 2: desplazar el eje +-d/2 por su NORMAL
# ------------------------------------------------------------------
# Esta si es la silueta geometrica correcta de la barra. Tampoco se puede
# rellenar: la banda se cruza en las CRESTAS, porque el medio diametro de la
# barra es mayor que el radio de curvatura del seno ahi.
k = 2 * math.pi / paso
pendiente_max = r_h * k
curvatura_max = r_h * k * k
radio_curvatura = 1 / curvatura_max

print(f"  via 2, banda por normal: pendiente max {pendiente_max:.1f} "
      f"({math.degrees(math.atan(pendiente_max)):.1f} grados)")
print(f"                           radio de curvatura minimo "
      f"{radio_curvatura*1000:.2f} mm contra d/2 = {d_est/2*1000:.2f} mm")

check("la via 2 se descarta: d/2 supera el radio de curvatura",
      d_est / 2 > radio_curvatura,
      f"d/2 {d_est/2*1000:.2f} mm, radio {radio_curvatura*1000:.2f} mm")

# ------------------------------------------------------------------
# VIA 3: una polilinea del EJE con ancho constante (la que se usa)
# ------------------------------------------------------------------
# AutoCAD dibuja una polilinea de ancho constante como una banda maciza de ese
# ancho real en unidades de dibujo, y resuelve el solo las uniones. No hay
# frontera que cerrar, asi que el problema de las dos vias anteriores desaparece.

def eje_helice(r_eje, paso, largo, por_vuelta=24):
    vueltas = largo / paso
    n = max(2, int(math.ceil(vueltas * por_vuelta)))
    dx = largo / n

    pts, fase = [], 0.0
    for i in range(n + 1):
        if i > 0:
            fase += 2 * math.pi * dx / paso
        pts.append((i * dx, r_eje * math.sin(fase)))
    return pts


eje = eje_helice(r_h, paso, largo)
print(f"  via 3, eje con ancho   : {len(eje)} vertices, ancho = "
      f"{d_est*100:.2f} cm (el diametro de la tabla)")

check("la via 3 es UNA sola polilinea abierta", len(eje) > 0)
check("el eje arranca en la base", abs(eje[0][0]) < 1e-12)
check("y termina en el tope", abs(eje[-1][0] - largo) < 1e-12)
check("el eje no se sale del radio del zuncho",
      all(abs(y) <= r_h + 1e-12 for _, y in eje))

# El ancho es el DIAMETRO de la tabla, no la mitad ni un grosor de linea
check("el ancho de la polilinea es el diametro completo de la varilla",
      abs(d_est - DIAM["#3"] * ESCALA) < 1e-15,
      f"{d_est:.6f} contra {DIAM['#3']*ESCALA:.6f}")

# Y el area que cubre la banda es ancho x largo del eje recorrido
largo_eje = sum(math.hypot(eje[i+1][0]-eje[i][0], eje[i+1][1]-eje[i][1])
                for i in range(len(eje)-1))
largo_helice = (largo / paso) * math.hypot(2 * math.pi * r_h, paso)
print(f"  largo del eje proyectado = {largo_eje:.3f} m")
print(f"  largo real de la helice  = {largo_helice:.3f} m")

check("el eje proyectado es mas corto que la helice real",
      largo_eje < largo_helice,
      f"{largo_eje:.3f} contra {largo_helice:.3f}")
check("pero del mismo orden", largo_eje > 0.5 * largo_helice)

# ======================================================================
# 10. Donde se RECORTAN las varillas: solo los pasos por DELANTE
# ======================================================================
print("\nRecorte de las varillas: el zuncho solo tapa cuando pasa por delante")

# El zuncho cruza la posicion proyectada de una varilla DOS veces por vuelta: una
# con la barra hacia el observador y otra al otro lado del elemento. Solo la
# primera la tapa. Recortar en todos los cruces partiria la varilla en el doble de
# trozos y dejaria huecos donde deberia verse entera.
#
# El criterio es el COSENO de la fase, que es la profundidad de la helice.

def muestrear(r_eje, paso, largo, por_vuelta=24):
    """Port de MuestrearHelice: x, sen y cos de la fase."""
    vueltas = largo / paso
    n = max(8, int(math.ceil(vueltas * por_vuelta)))
    dx = largo / n

    xs, sen, cos = [], [], []
    fase = 0.0
    for i in range(n + 1):
        if i > 0:
            fase += 2 * math.pi * dx / paso
        xs.append(i * dx)
        sen.append(math.sin(fase))
        cos.append(math.cos(fase))
    return xs, sen, cos


def cruces_frontales(xs, sen, cos, r_eje, objetivo):
    """Port de CrucesFrontales."""
    out = []
    if abs(objetivo) > r_eje:
        return out

    for i in range(len(xs) - 1):
        d0 = r_eje * sen[i] - objetivo
        d1 = r_eje * sen[i + 1] - objetivo
        if d0 == 0 or (d0 < 0) == (d1 < 0):
            continue
        t = d0 / (d0 - d1)
        x = xs[i] + t * (xs[i + 1] - xs[i])
        c = cos[i] + t * (cos[i + 1] - cos[i])
        if c > 0:
            out.append(x)
    return sorted(out)


def todos_los_cruces(xs, sen, r_eje, objetivo):
    out = []
    for i in range(len(xs) - 1):
        d0 = r_eje * sen[i] - objetivo
        d1 = r_eje * sen[i + 1] - objetivo
        if d0 == 0 or (d0 < 0) == (d1 < 0):
            continue
        t = d0 / (d0 - d1)
        out.append(xs[i] + t * (xs[i + 1] - xs[i]))
    return out


xs, sen, cos = muestrear(r_h, paso, largo)
vueltas = largo / paso

# Una varilla en el eje del elemento: la cruza en cada media vuelta
frente = cruces_frontales(xs, sen, cos, r_h, 0.0)
todos = todos_los_cruces(xs, sen, r_h, 0.0)

print(f"  varilla en el eje: {len(todos)} cruces en total, "
      f"{len(frente)} por delante")

check("hay dos cruces por vuelta en total",
      abs(len(todos) - 2 * vueltas) <= 1,
      f"{len(todos)} contra {2*vueltas:.0f}")
check("y solo la MITAD son por delante",
      abs(len(frente) - vueltas) <= 1,
      f"{len(frente)} contra {vueltas:.0f}")

# Los cruces frontales van separados justo un paso: es una vez por vuelta
if len(frente) >= 3:
    huecos = [frente[i+1] - frente[i] for i in range(len(frente)-1)]
    print(f"  separacion entre cruces frontales: {min(huecos):.4f} a {max(huecos):.4f} m "
          f"(el paso es {paso})")
    check("los cruces frontales van separados un paso",
          abs(sum(huecos)/len(huecos) - paso) < paso * 0.02,
          f"media {sum(huecos)/len(huecos):.4f}")

# Una varilla en el borde del circulo de paso: tambien la cruza
for idx in (0, 2, 4, 6):
    ang = math.pi / 2 + idx * 2 * math.pi / 8
    yb = r_paso * math.cos(ang)
    fr = cruces_frontales(xs, sen, cos, r_h, yb)
    print(f"  varilla {idx} (y={yb:+.4f}): {len(fr)} recortes")
    check(f"la varilla {idx} se recorta al menos una vez por vuelta",
          len(fr) >= vueltas - 1, f"{len(fr)} recortes")

# Una varilla MAS AFUERA que el zuncho no se recorta nunca: no se cruzan
fuera = cruces_frontales(xs, sen, cos, r_h, r_h * 1.5)
check("una varilla fuera del alcance del zuncho no se recorta", len(fuera) == 0,
      f"{len(fuera)} recortes")

# Y todos los recortes caen DENTRO del elemento
check("todos los recortes caen dentro del elemento",
      all(0 <= x <= largo for x in frente))

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
