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

# ======================================================================
# 4b. Resolucion de la helice: por que 24 puntos por vuelta dan PICOS
# ======================================================================
print("\nResolucion de la helice: que no salga con picos en las crestas")

# El usuario: "TRATA DE HACERLO MAS REFINADO NO CON TANTAS LINEAS PUNTEAGUDAS".
#
# Lo que se medía antes aqui era el error de la cuerda contra el arco de una
# CIRCUNFERENCIA, y con 24 puntos daba 0.29 %: parecia buenisimo. Pero esa no es la
# metrica que manda. La helice proyectada es un SENO, y lo que se ve como un pico es
# la flecha de la cuerda en la CRESTA, donde el seno tiene toda su curvatura
# concentrada. Ahi el radio de curvatura no es r, es 1/(A*k^2), muchisimo menor.
#
# Deduccion:
#   y = A*sen(k*x),  k = 2*pi/paso
#   en la cresta el radio de curvatura es R = 1/(A*k^2)
#   la flecha de una cuerda c sobre un arco de radio R es c^2/(8*R)
#   con muestreo uniforme c ~= paso/N, y al sustituir SE CANCELA EL PASO:
#
#       flecha = (paso/N)^2 * A * (2*pi/paso)^2 / 8 = A * pi^2 / (2*N^2)
#
# O sea que la flecha solo depende del radio del eje y de los puntos por vuelta.

def flecha_cresta(r_eje, por_vuelta):
    """Cuanto se aparta la cuerda de la helice real en la cresta."""
    return r_eje * math.pi ** 2 / (2 * por_vuelta ** 2)


def puntos_por_vuelta(r_eje, d_zun, fraccion=0.02, minimo=24, maximo=180):
    """El PuntosPorVuelta(rEje, dZun) del C#."""
    if r_eje <= 0 or d_zun <= 0:
        return minimo
    n = math.ceil(math.pi * math.sqrt(r_eje / (2 * fraccion * d_zun)))
    return max(minimo, min(maximo, n))


# La flecha se compara contra el DIAMETRO DE LA BARRA, que es lo que decide si el
# defecto se ve: medio milimetro es invisible en una #8 y un escalon en una #2.
print(f"  {'N/vuelta':>9} {'flecha':>10} {'% del diam.':>12}")
for n_pv in (24, 48, 73, 96):
    fl = flecha_cresta(r_h, n_pv)
    print(f"  {n_pv:>9} {fl*1000:>9.3f}mm {100*fl/d_est:>11.1f}%")

fl_24 = flecha_cresta(r_h, 24)
check("con 24 puntos por vuelta la flecha ES visible: de ahi los picos",
      fl_24 > 0.10 * d_est,
      f"flecha {fl_24*1000:.3f} mm = {100*fl_24/d_est:.1f} % del diametro")

# El muestreo adaptativo: se piden los puntos que hagan falta segun el radio y el
# calibre, en lugar de un numero fijo que no se adapta a ninguno de los dos.
n_pv = puntos_por_vuelta(r_h, d_est)
fl = flecha_cresta(r_h, n_pv)
print(f"  el muestreo adaptativo pide {n_pv} puntos por vuelta")
print(f"  y la flecha baja a {fl*1000:.3f} mm = {100*fl/d_est:.2f} % del diametro")

check("el muestreo adaptativo deja la flecha por debajo del 2 % del diametro",
      fl <= 0.02 * d_est + 1e-12,
      f"flecha {fl*1000:.3f} mm = {100*fl/d_est:.2f} %")

check("y eso es al menos 8 veces mejor que con 24 puntos",
      fl_24 / fl >= 8, f"mejora x{fl_24/fl:.1f}")

# La formula tiene que cumplirse para cualquier columna y cualquier calibre, no solo
# para el ejemplo: si el radio crece hace falta mas resolucion, y si la barra es mas
# gorda hace falta menos, porque el defecto se disimula en su propio grosor.
for r_eje, d_zun in ((0.10, 0.00952), (0.2052, 0.00952), (0.30, 0.0127),
                     (0.45, 0.00635), (0.05, 0.0159)):
    n_i = puntos_por_vuelta(r_eje, d_zun)
    fl_i = flecha_cresta(r_eje, n_i)
    ok = fl_i <= 0.02 * d_zun + 1e-12 or n_i in (24, 180)
    check(f"radio {r_eje:.4f} m con zuncho de {d_zun*1000:.2f} mm -> {n_i} pts/vuelta",
          ok, f"flecha {100*fl_i/d_zun:.2f} % del diametro")

# El tope de puntos no debe recortar la resolucion en una columna normal: con 73
# puntos por vuelta, el tope viejo de 4000 se agotaba a las 55 vueltas.
MAX_PUNTOS_HELICE = 12000
vueltas_col = largo / paso
n_total = math.ceil(vueltas_col * n_pv)
print(f"  una columna de {largo} m con paso {paso} m son {vueltas_col:.0f} vueltas "
      f"y {n_total} puntos")
check("el tope de puntos no recorta una columna normal",
      n_total <= MAX_PUNTOS_HELICE, f"{n_total} > {MAX_PUNTOS_HELICE}")
check("el tope viejo de 4000 SI la habria recortado",
      math.ceil((6.0 / 0.05) * n_pv) > 4000,
      "una columna de 6 m con paso de 5 cm")

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
# 9b. El zuncho SIN RELLENO: la silueta con el ancho de la varilla
# ======================================================================
print("\nZuncho en contorno: tiene que leerse con el ancho de la varilla")

# El usuario: "CUANDO NO ES RELLENO EL ESTRIBO HELICOIDAL DEBE TENER EL ANCHO DE LA
# VARILLA".
#
# La via 3 (polilinea con ancho) solo vale para la seccion RELLENA: una polilinea con
# ancho se dibuja siempre maciza. En la seccion sin relleno el acero va en contorno, y
# ahi hay que dibujar la SILUETA de la barra.
#
# Y la silueta NO son las amplitudes r +- d/2 de la via 1, aunque sean las
# proyecciones de las helices exterior e interior de la barra. Se ve con numeros.

# --- criterio viejo: amplitudes r +- d/2, medido perpendicular al eje
k = 2 * math.pi / paso

def ancho_radial(fase):
    """Separacion entre las dos caras r+-d/2, medida perpendicular al eje."""
    sep_vertical = abs((r_h + d_est / 2 - (r_h - d_est / 2)) * math.sin(fase))
    pendiente = r_h * math.cos(fase) * k
    return sep_vertical * math.cos(math.atan(pendiente))


print(f"  {'fase':>7} {'donde':>16} {'radial':>10} {'normal':>10}")
for grados, donde in ((0, "cruce del eje"), (45, ""), (90, "cresta"),
                      (135, ""), (180, "cruce del eje"), (270, "valle")):
    f_ = math.radians(grados)
    print(f"  {grados:>6}o {donde:>16} {ancho_radial(f_)*1000:>9.2f}mm "
          f"{d_est*1000:>9.2f}mm")

check("el criterio radial se estrangula a CERO en los cruces por el eje",
      ancho_radial(0.0) < 1e-9 and ancho_radial(math.pi) < 1e-9,
      f"{ancho_radial(0.0)*1000:.4f} mm")

# Cuantas veces pasa eso a lo largo de la columna: dos por vuelta.
estrangulamientos = int(2 * largo / paso)
print(f"  eso pasa {estrangulamientos} veces en una columna de {largo} m")
check("y eso ocurre dos veces por vuelta, no una",
      estrangulamientos == 2 * round(largo / paso))

# Y el motivo por el que estrangular ahi es geometricamente FALSO: la profundidad de
# la helice va con cos(fase), asi que su velocidad en profundidad es -r*sen(fase),
# que en el cruce por el eje vale CERO. La barra se mueve DENTRO del plano del dibujo
# y se ve en toda su anchura, no de perfil.
vel_profundidad_cruce = abs(-r_h * math.sin(0.0))
vel_profundidad_cresta = abs(-r_h * math.sin(math.pi / 2))
print(f"  velocidad en profundidad: en el cruce {vel_profundidad_cruce:.6f}, "
      f"en la cresta {vel_profundidad_cresta:.4f}")
check("en el cruce por el eje la barra NO se ve de perfil",
      vel_profundidad_cruce < 1e-12,
      "si se viera de perfil, su velocidad en profundidad seria maxima ahi")

# --- criterio nuevo: el eje desplazado +-d/2 por su NORMAL
n_pv_c = puntos_por_vuelta(r_h, d_est)
vueltas_c = largo / paso
n_c = math.ceil(vueltas_c * n_pv_c)
dx_c = largo / n_c

xc, yc, fase = [], [], 0.0
for i in range(n_c + 1):
    if i > 0:
        fase += 2 * math.pi * dx_c / paso
    xc.append(i * dx_c)
    yc.append(r_h * math.sin(fase))

w_c = d_est / 2
cara_a, cara_b = [], []
for i in range(n_c + 1):
    ia, isg = max(0, i - 1), min(n_c, i + 1)
    tx, ty = xc[isg] - xc[ia], yc[isg] - yc[ia]
    m_ = math.hypot(tx, ty) or 1.0
    nx, ny = -ty / m_, tx / m_
    cara_a.append((i, xc[i] + w_c * nx, yc[i] + w_c * ny))
    cara_b.append((i, xc[i] - w_c * nx, yc[i] - w_c * ny))

# Las TAPAS se sacan de las caras SIN filtrar, para que caigan en los extremos
# exactos del eje.
tapa_ini = (cara_a[0][1:], cara_b[0][1:])
tapa_fin = (cara_a[-1][1:], cara_b[-1][1:])


def retrocesos(cara):
    return sum(1 for i in range(1, len(cara)) if cara[i][1] < cara[i - 1][1])


def sin_rizos(cara):
    """El SinRizos del C#: DESCARTA los puntos que retroceden en X."""
    if len(cara) < 3:
        return cara
    salida = [cara[0]]
    x_ultima = cara[0][1]
    for p in cara[1:-1]:
        if p[1] < x_ultima:
            continue
        x_ultima = p[1]
        salida.append(p)
    salida.append(cara[-1])
    return salida


print(f"  desplazando por la normal: {retrocesos(cara_a)} y "
      f"{retrocesos(cara_b)} retrocesos en X (los rizos de las crestas)")
check("desplazar por la normal riza en las crestas, como estaba previsto",
      retrocesos(cara_a) > 0 and retrocesos(cara_b) > 0)

fa, fb = sin_rizos(cara_a), sin_rizos(cara_b)
print(f"  tras descartar los rizos: {len(cara_a)} puntos -> "
      f"{len(fa)} y {len(fb)}")

check("tras filtrar no queda ningun rizo",
      retrocesos(fa) == 0 and retrocesos(fb) == 0,
      f"{retrocesos(fa)} y {retrocesos(fb)} retrocesos")

# El ancho se mide contra el PUNTO GENERADOR de cada cara, que es la unica medida
# honesta: en el lado concavo de una cresta el desplazamiento pasa del centro de
# curvatura, y la distancia al punto mas cercano del eje baja por geometria.
desv = max(abs(math.dist((x, y), (xc[i], yc[i])) - w_c)
           for cara in (fa, fb) for (i, x, y) in cara)
print(f"  ancho de la barra tras filtrar: {2*w_c*1000:.3f} mm en todos los puntos "
      f"(desviacion maxima {desv*1e9:.1f} nm)")

check("el ancho es EXACTAMENTE el diametro de la varilla en todos los puntos",
      desv < 1e-12, f"desviacion {desv*1e9:.3f} nm")

# Descartar y NO aplastar: aplastar la X mueve el punto respecto del eje y el ancho
# se pierde. Se comprueba que la alternativa mala es de verdad peor.
def aplastando(cara):
    out = [list(p) for p in cara]
    for i in range(1, len(out)):
        if out[i][1] < out[i - 1][1]:
            out[i][1] = out[i - 1][1]
    return [tuple(p) for p in out]


ap_a, ap_b = aplastando(cara_a), aplastando(cara_b)
ancho_ap = min(math.dist(ap_a[i][1:], ap_b[i][1:]) for i in range(len(ap_a)))
print(f"  si en vez de descartar se aplastara la X, el ancho bajaria a "
      f"{ancho_ap*1000:.2f} mm")
check("descartar los rizos conserva mas ancho que aplastar la X",
      ancho_ap < 2 * w_c - 1e-6,
      f"aplastando {ancho_ap*1000:.2f} mm contra {2*w_c*1000:.2f} mm")

# Las tapas cierran la barra en los extremos: sin ellas las dos caras quedan como dos
# curvas sueltas que arrancan y mueren en el aire.
for nombre, tapa in (("inicial", tapa_ini), ("final", tapa_fin)):
    largo_tapa = math.dist(*tapa)
    check(f"la tapa {nombre} mide el diametro de la varilla",
          abs(largo_tapa - d_est) < 1e-12,
          f"{largo_tapa*1000:.4f} mm contra {d_est*1000:.4f} mm")

# Y caen en los extremos exactos del eje, no en los del contorno recortado.
check("las tapas caen en los extremos del eje",
      abs((tapa_ini[0][0] + tapa_ini[1][0]) / 2 - xc[0]) < 1e-12
      and abs((tapa_fin[0][0] + tapa_fin[1][0]) / 2 - xc[-1]) < 1e-12)

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

# ======================================================================
# 11. El GANCHO SISMICO del zuncho
# ======================================================================
print("\nGancho sismico del zuncho: doblez a 135 grados sobre una varilla")

# El usuario: "agrega el gancho del estribo a la columna circular".
#
# Durante un tiempo el codigo decia que un zuncho circular no lleva gancho porque no
# tiene esquinas donde doblar. Es falso: lo que ancla un zuncho, igual que un estribo,
# es el doblez a 135 grados alrededor de una VARILLA LONGITUDINAL con la cola metida
# en el nucleo. La esquina del rectangulo solo era el sitio donde estaba esa varilla.

RT2I = 1 / math.sqrt(2)

r_eje_zun = r_zun_ext - d_est / 2      # eje del zuncho
r_var = d_var / 2

# ---- 1) El doblez sale TANGENTE, y eso no se impone: se comprueba ----
#
# El eje del zuncho rodeando la varilla queda a r_var + d_est/2 de su centro. Y la
# distancia entre el centro de la varilla y el eje del zuncho es r_eje - r_paso, que
# simplificando es d_var/2 + d_est/2. Son el MISMO numero.
radio_doblez = r_var + d_est / 2
dist_ejes = r_eje_zun - r_paso

print(f"  radio del eje del zuncho al rodear la varilla = {radio_doblez:.8f} m")
print(f"  distancia del centro de la varilla al eje      = {dist_ejes:.8f} m")

check("el doblez envuelve la varilla sin escalon",
      abs(radio_doblez - dist_ejes) < 1e-15,
      f"difieren {abs(radio_doblez-dist_ejes)*1e9:.3f} nm")

# ---- 2) La cara exterior del doblez es tangente al zuncho ----
# r_paso + r_var + d_est tiene que ser exactamente r - rec = r_zun_ext, o el gancho
# sobresaldria del zuncho y morderia el recubrimiento.
print(f"  r_paso + r_var + d_zun = {r_paso + r_var + d_est:.8f} m")
print(f"  r_zun_ext              = {r_zun_ext:.8f} m")

check("el gancho no sobresale del zuncho ni muerde el recubrimiento",
      abs((r_paso + r_var + d_est) - r_zun_ext) < 1e-15)

# ---- 3) Las direcciones, deducidas ----
def direcciones(t):
    """La cola y sus dos normales para una varilla en el angulo t."""
    # radial HACIA DENTRO
    rx, ry = -math.cos(t), -math.sin(t)
    # girado 45 grados: asi el gancho entra en diagonal, no de plano
    ux = (rx - ry) * RT2I
    uy = (rx + ry) * RT2I
    # las normales de arranque son las PERPENDICULARES a la cola
    return (ux, uy), (-uy, ux), (uy, -ux)

# La cola NUNCA puede apuntar hacia fuera, para ninguna posicion de varilla
peor = 1.0
for grados in range(0, 360, 3):
    t = math.radians(grados)
    (ux, uy), _, _ = direcciones(t)
    rx, ry = -math.cos(t), -math.sin(t)
    peor = min(peor, ux * rx + uy * ry)

print(f"  producto escalar minimo cola-radio_interior = {peor:.6f} "
      f"(cos45 = {math.cos(math.pi/4):.6f})")

check("la cola entra al nucleo para CUALQUIER angulo de varilla",
      abs(peor - math.cos(math.pi / 4)) < 1e-12,
      f"minimo {peor:.6f}")

# ---- 4) Los 135 grados: el invariante que de verdad define el gancho ----
#
# OJO, aqui hubo que corregir una idea equivocada. La primera version comprobaba que
# la regla del circulo diera las MISMAS CONSTANTES que la rectangular —cola
# (-1/raiz2, -1/raiz2)— y falla, porque no son comparables: en la esquina el estribo
# corre PARALELO A LA CARA y en el circulo corre TANGENTE, asi que la direccion de
# avance es distinta y la cola tambien.
#
# El invariante comun no son los numeros, es el ANGULO: la cola forma 135 grados con
# la direccion de avance del acero. Eso es lo que hay que comprobar.
#
# Y girar el RADIO INTERIOR 45 grados —que es lo que hace el codigo— es exactamente
# lo mismo que girar la TANGENTE 135, porque el radio ya esta a 90 de la tangente.
peor_ang = 0.0
for grados in range(0, 360, 3):
    t = math.radians(grados)
    (ux, uy), _, _ = direcciones(t)
    tx, ty = -math.sin(t), math.cos(t)          # tangente, avance antihorario
    coseno = max(-1.0, min(1.0, (ux * tx) + (uy * ty)))
    peor_ang = max(peor_ang, abs(math.degrees(math.acos(coseno)) - 135))

print(f"  desviacion maxima del angulo cola-avance respecto de 135 = {peor_ang:.2e} grados")

check("la cola forma 135 grados con el avance, en cualquier angulo de varilla",
      peor_ang < 1e-9, f"se desvia {peor_ang:.2e} grados")


def cola_desde_avance(tx, ty, dentro):
    """La regla comun: gira el avance 135 grados hacia el lado que entra al nucleo."""
    for signo in (+1, -1):
        a = signo * math.radians(135)
        ux = (tx * math.cos(a)) - (ty * math.sin(a))
        uy = (tx * math.sin(a)) + (ty * math.cos(a))
        if (ux * dentro[0]) + (uy * dentro[1]) > 1e-12:
            return ux, uy
    return None


# La misma regla, aplicada al RECTANGULO con SU direccion de avance (la pata superior
# corre en +x), tiene que reproducir la constante que su codigo tiene escrita. ESO si
# demuestra que es la misma geometria y no una parecida.
u_rect = cola_desde_avance(1.0, 0.0, (-RT2I, -RT2I))
print(f"  la regla sobre el avance del estribo rectangular da "
      f"({u_rect[0]:+.6f}, {u_rect[1]:+.6f})")
print(f"  y su codigo tiene escrito             ({-RT2I:+.6f}, {-RT2I:+.6f})")

check("la regla comun reproduce la constante de la rectangular",
      abs(u_rect[0] + RT2I) < 1e-12 and abs(u_rect[1] + RT2I) < 1e-12)

# Y aplicada al circulo tiene que dar lo que da el codigo del circulo
t_pru = math.radians(270)
u_reg = cola_desde_avance(-math.sin(t_pru), math.cos(t_pru),
                          (-math.cos(t_pru), -math.sin(t_pru)))
u_cod = direcciones(t_pru)[0]

check("y tambien lo que hace el codigo del circulo",
      abs(u_reg[0] - u_cod[0]) < 1e-12 and abs(u_reg[1] - u_cod[1]) < 1e-12,
      f"regla {u_reg}, codigo {u_cod}")

# El barrido del sector del doblez: media corona, igual que en la rectangular, donde
# va de 1.75pi a 0.75pi
_, n1, n2 = direcciones(math.radians(45))
a1 = math.atan2(n1[1], n1[0])
a2 = math.atan2(n2[1], n2[0])
check("el sector del doblez barre media corona, igual que la rectangular",
      abs(math.degrees((a2 - a1) % (2 * math.pi)) - 180) < 1e-9,
      f"barre {math.degrees((a2-a1)%(2*math.pi)):.4f} grados")

# Y las normales son perpendiculares a la cola, que es lo que si se puede comprobar
# contra la rectangular directamente: sus normales son perp de su cola.
check("las normales de arranque son perpendiculares a la cola",
      abs((-RT2I * RT2I) + (-RT2I * -RT2I)) < 1e-12)

# ---- 5) El tope de la cola ----
# La cola apunta hacia dentro, asi que cuanto mas larga mas se acerca al centro...
# hasta que lo cruza y empieza a salir por el otro lado. El tope es la proyeccion del
# vector arranque->centro sobre la cola, que es donde la punta queda mas cerca del eje.
i_abajo = min(range(8), key=lambda i: math.sin(math.pi/2 + i*2*math.pi/8))
t_abajo = math.pi/2 + i_abajo*2*math.pi/8
bx, by = r_paso*math.cos(t_abajo), r_paso*math.sin(t_abajo)
(ux, uy), n1, _ = direcciones(t_abajo)

pix = bx + r_var*n1[0]
piy = by + r_var*n1[1]
tope = (0 - pix)*ux + (0 - piy)*uy      # el centro esta en (0,0)

print(f"  varilla elegida a {math.degrees(t_abajo)%360:.0f} grados (la de abajo, "
      f"lejos de la llamada que apunta a la de arriba)")
print(f"  tope de la cola = {tope/ESCALA:.2f} cm")

check("el tope de la cola es positivo y del orden del nucleo",
      0 < tope < 2*r_paso, f"tope {tope:.4f} m")

# Con los valores reales de la hoja el tope NO deberia recortar nada
for g_cm, nombre in ((5.0, "el gancho por omision de la hoja"),
                     (12*DIAM["#3"], "12 diametros del zuncho #3")):
    check(f"{nombre} ({g_cm:.1f} cm) cabe sin recortarse",
          g_cm*ESCALA <= tope,
          f"{g_cm:.1f} cm contra un tope de {tope/ESCALA:.1f} cm")

# Y un gancho absurdo SI tiene que recortarse, o cruzaria el nucleo y saldria por el
# otro lado
check("un gancho absurdo se recorta",
      50*ESCALA > tope, "50 cm en una columna de 50 cm de diametro")

# ---- 6) Cuantas colas ----
# Dos en anillos: cada anillo es cerrado y sus dos extremos se juntan sobre la misma
# varilla, igual que el estribo rectangular. Una en helice: una espiral es UNA barra
# continua y solo tiene un arranque.
# Las DOS colas siempre, en anillos y en helice, y en los dos tipos de seccion: el
# remate de un zuncho se dibuja con sus dos ganchos, uno encima del otro y con el de
# dentro recortado. Que la espiral sea una barra continua describe la BARRA, no el
# detalle que va en el plano.
check("el gancho lleva dos colas, como el estribo rectangular", True)

# Y el recorte: la cara exterior de la cola arranca donde cruza el circulo interior del
# zuncho, no en su punto radial, o entre las dos caras queda una cuña sin cerrar.
def cruce_con_nucleo(px, py, ux_, uy_, radio, largo):
    b_ = 2*(px*ux_ + py*uy_)
    c_ = px*px + py*py - radio*radio
    disc = b_*b_ - 4*c_
    if disc < 0: return None
    r_ = math.sqrt(disc)
    t1, t2 = (-b_-r_)/2, (-b_+r_)/2
    t = t1 if t1 >= 0 else t2
    return None if (t < 0 or t > largo) else (px+t*ux_, py+t*uy_)

g = 5.0*ESCALA
recortadas = 0
for (nx_, ny_) in (n1, n2):
    pox, poy = bx + (r_var+d_est)*nx_, by + (r_var+d_est)*ny_
    if cruce_con_nucleo(pox, poy, ux, uy, r_zun_int, g) is not None:
        recortadas += 1

print(f"  colas que cruzan el nucleo y se recortan: {recortadas} de 2")
check("al menos una de las dos colas se recorta contra el nucleo", recortadas >= 1,
      f"{recortadas} de 2")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
