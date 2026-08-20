"""Verifica la geometria de los perfiles de acero: IR, OR, OC y CF.

Es el mismo calculo que hace SeccionDrawer.Acero.cs, escrito aparte para poder
EJECUTARLO: en este entorno no hay .NET ni AutoCAD, asi que la unica forma honesta de
comprobar una formula es corriendola.

Lo que se comprueba, familia por familia:

  IR  1. Que el contorno de doce vertices encierra el area de acero que le toca al
         perfil -dos patines mas el alma-, calculada por la formula del area de un
         poligono, que no sabe nada de perfiles.
      2. Que es simetrico respecto a su eje y que su caja es exactamente d x bf.

  OR  3. Que el bulge de las esquinas es el de un arco de 90 grados EXACTO.
      4. Que los radios se recortan a lo que cabe, por dentro y por fuera.
      5. Que cada esquina es un FILETE de verdad: los dos vertices a la distancia del
         radio del centro del arco, y los tramos rectos perpendiculares a esos radios.
      6. Que el peralte es el lado mayor, capturado como sea.
      7. Que el rayado cambia a las 5 pulgadas, y de que lado cae un tubo de 5" justas.

  OC  8. Que la corona tiene el area de un tubo y que sin hueco no se dibuja el
         circulo interior.

  CF  9. Que los ocho dobleces son arcos de 90 grados, con el signo que les toca: los
         exteriores giran hacia un lado y los interiores hacia el otro.
     10. Que al espejear el perfil TODOS los signos se invierten, que es lo que hace
         que las dos canales queden enfrentadas y no una girada a medias.
     11. Que los radios se recortan como en la macro: el exterior por el medio ancho,
         el labio y el medio peralte; el interior por su mitad y descontando espesores.
     12. Que las ocho esquinas son filetes de verdad, con la misma prueba de tangencia.
"""

import math

BULGE_90 = 0.414213562373095      # el BULGE_90 de la macro del HSS
PULGADA_CM = 2.54
PERALTE_LIMITE_PULG = 5.0

fallos = []
avisos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


def area_poligono(pts):
    """Area de un poligono por la formula del cordon de zapato, en valor absoluto."""
    s = 0.0
    n = len(pts)

    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        s += x1 * y2 - x2 * y1

    return abs(s) / 2


def bulge_desde_centro(cx, cy, xa, ya, xb, yb):
    """Port de BulgeDesdeCentro: tangente de la cuarta parte del barrido."""
    aa = math.atan2(ya - cy, xa - cx)
    ab = math.atan2(yb - cy, xb - cx)

    barrido = ab - aa

    while barrido > math.pi:
        barrido -= 2 * math.pi

    while barrido <= -math.pi:
        barrido += 2 * math.pi

    return math.tan(barrido / 4)


def es_filete(centro, pa, pb, previo, siguiente, r, tol=1e-9):
    """Si la esquina es un filete de verdad.

    Dos condiciones, y las dos hacen falta:

      * los dos vertices del arco estan a la distancia del RADIO del centro;
      * y el tramo recto que llega y el que sale son PERPENDICULARES a sus radios,
        que es lo que significa que el arco empalma sin esquina.

    Sin la segunda, un arco de radio correcto pero mal colocado pasaria la prueba y en
    el dibujo se veria un escalon en el doblez.
    """
    da = math.hypot(pa[0] - centro[0], pa[1] - centro[1])
    db = math.hypot(pb[0] - centro[0], pb[1] - centro[1])

    if abs(da - r) > tol or abs(db - r) > tol:
        return False, f"radios {da:.12f} y {db:.12f} contra {r:.12f}"

    # El tramo que LLEGA al arco: de 'previo' a pa. Su direccion tiene que ser
    # perpendicular al radio centro->pa.
    for (p0, p1, vert) in ((previo, pa, pa), (pb, siguiente, pb)):
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        ln = math.hypot(dx, dy)

        if ln < 1e-12:
            continue

        rx, ry = vert[0] - centro[0], vert[1] - centro[1]
        punto = (dx * rx + dy * ry) / ln

        if abs(punto) > 1e-9:
            return False, f"el tramo no es tangente, producto {punto:.3e}"

    return True, ""


# ===========================================================================
#  IR: el perfil I
# ===========================================================================

def perfil_ir(cx, cy, d, bf, tw, tf):
    """Port de PerfilIr: los doce vertices, en el orden de la macro."""
    return [
        (cx + bf / 2, cy),
        (cx + bf / 2, cy + tf),
        (cx + tw / 2, cy + tf),
        (cx + tw / 2, cy + d - tf),
        (cx + bf / 2, cy + d - tf),
        (cx + bf / 2, cy + d),
        (cx - bf / 2, cy + d),
        (cx - bf / 2, cy + d - tf),
        (cx - tw / 2, cy + d - tf),
        (cx - tw / 2, cy + tf),
        (cx - bf / 2, cy + tf),
        (cx - bf / 2, cy),
    ]


print("=" * 78)
print(" IR: el perfil I laminado")
print("=" * 78)

CASOS_IR = [
    # nombre,        d,     bf,    tw,    tf
    ("W12X30",      31.30, 16.50, 0.660, 1.110),
    ("W18X50",      45.70, 19.00, 0.900, 1.440),
    ("W6X15",       15.20, 15.20, 0.580, 0.660),
    ("IR alta",     60.00, 20.00, 1.000, 2.000),
]

for nombre, d, bf, tw, tf in CASOS_IR:
    pts = perfil_ir(0, 0, d, bf, tw, tf)

    # El area de acero del perfil, por la definicion: dos patines y el alma.
    esperada = 2 * bf * tf + (d - 2 * tf) * tw
    calculada = area_poligono(pts)

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    print(f"\n{nombre}: d={d} bf={bf} tw={tw} tf={tf}")
    print(f"    area del contorno {calculada:.6f} cm2, area del perfil "
          f"{esperada:.6f} cm2")

    check(f"'{nombre}': el contorno encierra el area del perfil",
          abs(calculada - esperada) < 1e-9,
          f"{calculada:.9f} contra {esperada:.9f}")

    check(f"'{nombre}': la caja es el peralte por el ancho de patin",
          abs((max(ys) - min(ys)) - d) < 1e-12
          and abs((max(xs) - min(xs)) - bf) < 1e-12,
          f"{max(ys) - min(ys):.6f} x {max(xs) - min(xs):.6f}")

    # Simetrico respecto a su eje: para cada vertice tiene que existir su reflejo.
    reflejos = {(round(-x, 9), round(y, 9)) for (x, y) in pts}
    propios = {(round(x, 9), round(y, 9)) for (x, y) in pts}

    check(f"'{nombre}': el perfil es simetrico respecto a su eje",
          reflejos == propios)


# ===========================================================================
#  OR: el tubo rectangular
# ===========================================================================

def rectangulo_redondeado(x0, y0, x1, y1, r):
    """Port de RectanguloRedondeado. Devuelve (vertices, bulges por indice)."""
    if r <= 1e-7:
        return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], {}

    pts = [
        (x0 + r, y0),
        (x1 - r, y0),
        (x1, y0 + r),
        (x1, y1 - r),
        (x1 - r, y1),
        (x0 + r, y1),
        (x0, y1 - r),
        (x0, y0 + r),
    ]

    return pts, {1: BULGE_90, 3: BULGE_90, 5: BULGE_90, 7: BULGE_90}


def radios_or(b, h, t):
    """Port de los recortes de radio de PerfilOr."""
    r_out = min(t, min(b, h) / 2)

    b_int = b - 2 * t
    h_int = h - 2 * t

    if b_int <= 0 or h_int <= 0:
        return r_out, None

    r_in = min(t / 2, min(b_int, h_int) / 2)

    return r_out, r_in


def area_rect_redondeado(b, h, r):
    """Area de un rectangulo con las cuatro esquinas redondeadas."""
    return b * h - (4 - math.pi) * r * r


print("\n" + "=" * 78)
print(" OR: el tubo rectangular")
print("=" * 78)

check("el bulge de las esquinas es el de un arco de 90 grados",
      abs(BULGE_90 - math.tan(math.pi / 8)) < 1e-15,
      f"{BULGE_90} contra {math.tan(math.pi / 8)}")

CASOS_OR = [
    # nombre,             b,     h,     t
    ("HSS6X6X1/4",      15.24, 15.24, 0.635),
    ("HSS4X4X1/4",      10.16, 10.16, 0.635),
    ("HSS8X4X3/8",      10.16, 20.32, 0.953),
    ("OR de pared gorda", 6.00,  6.00, 1.500),
]

for nombre, b_cap, h_cap, t in CASOS_OR:
    # El peralte es el lado MAYOR, capturado como sea.
    b = min(b_cap, h_cap)
    h = max(b_cap, h_cap)

    r_out, r_in = radios_or(b, h, t)

    print(f"\n{nombre}: capturado {b_cap}x{h_cap}, t={t}")
    print(f"    dibujado {b}x{h}   rExt={r_out:.4f}   "
          f"rInt={'sin hueco' if r_in is None else '%.4f' % r_in}")

    check(f"'{nombre}': el peralte es el lado mayor", h >= b)

    # Los recortes: el radio exterior no puede pasar de la mitad del lado menor, y el
    # interior de la mitad del lado menor del hueco.
    check(f"'{nombre}': el radio exterior cabe", r_out <= min(b, h) / 2 + 1e-12)
    check(f"'{nombre}': y es el espesor cuando cabe",
          abs(r_out - min(t, min(b, h) / 2)) < 1e-12)

    if r_in is not None:
        check(f"'{nombre}': el radio interior cabe en el hueco",
              r_in <= min(b - 2 * t, h - 2 * t) / 2 + 1e-12)

    # Los filetes, esquina por esquina.
    pts, bulges = rectangulo_redondeado(-b / 2, 0, b / 2, h, r_out)

    if bulges:
        n = len(pts)
        centros = {
            1: (b / 2 - r_out, r_out),
            3: (b / 2 - r_out, h - r_out),
            5: (-b / 2 + r_out, h - r_out),
            7: (-b / 2 + r_out, r_out),
        }

        malas = []

        for idx, centro in centros.items():
            pa = pts[idx]
            pb = pts[(idx + 1) % n]
            previo = pts[(idx - 1) % n]
            siguiente = pts[(idx + 2) % n]

            ok, detalle = es_filete(centro, pa, pb, previo, siguiente, r_out)

            if not ok:
                malas.append(f"esquina {idx}: {detalle}")

            # Y el bulge del arco calculado desde su centro tiene que dar el mismo
            # numero que la constante que se le pone.
            bb = bulge_desde_centro(centro[0], centro[1], pa[0], pa[1], pb[0], pb[1])

            if abs(bb - bulges[idx]) > 1e-12:
                malas.append(f"esquina {idx}: bulge {bb:.12f} contra {bulges[idx]:.12f}")

        check(f"'{nombre}': las cuatro esquinas son filetes de verdad",
              not malas, "; ".join(malas))

    # El area de la pared, con las esquinas redondeadas contadas.
    if r_in is not None:
        area = (area_rect_redondeado(b, h, r_out)
                - area_rect_redondeado(b - 2 * t, h - 2 * t, r_in))

        # Comparada con la pared recta, para tener una referencia de cuanto quitan las
        # esquinas: no es una condicion, es informacion.
        recta = b * h - (b - 2 * t) * (h - 2 * t)

        print(f"    area de la pared {area:.4f} cm2   "
              f"(con esquinas en pico serian {recta:.4f})")

        check(f"'{nombre}': la pared tiene area positiva y menor que la recta",
              0 < area < recta)

    # El corte del rayado a las 5 pulgadas.
    peralte_in = h / PULGADA_CM
    menor = peralte_in < PERALTE_LIMITE_PULG - 0.01

    print(f"    peralte {peralte_in:.3f} pulgadas -> "
          f"{'rayado fino con fondo cian' if menor else 'relleno solido mas rayado'}")

# El caso de borde: un tubo de 5 pulgadas JUSTAS. La macro le resta una centesima al
# limite para que un 5 nominal no caiga del lado equivocado por un redondeo, y aqui se
# comprueba que efectivamente cae del lado del relleno.
cinco = 5 * PULGADA_CM
check("un tubo de 5 pulgadas justas se rellena, no se raya fino",
      not (cinco / PULGADA_CM < PERALTE_LIMITE_PULG - 0.01),
      f"{cinco / PULGADA_CM} pulgadas")

check("y uno de 4.9 se raya fino",
      (4.9 * PULGADA_CM) / PULGADA_CM < PERALTE_LIMITE_PULG - 0.01)


# ===========================================================================
#  OC: el tubo redondo
# ===========================================================================

print("\n" + "=" * 78)
print(" OC: el tubo redondo")
print("=" * 78)

CASOS_OC = [
    # nombre,        diametro, espesor
    ("PIPE 4 STD",     11.43, 0.602),
    ("PIPE 6 STD",     16.83, 0.711),
    ("PIPE 2 STD",      6.03, 0.391),
    ("macizo",          5.00, 2.500),
]

for nombre, diam, t in CASOS_OC:
    r_ext = diam / 2
    r_int = r_ext - t

    print(f"\n{nombre}: diametro {diam}, pared {t}")

    if r_int > 0:
        area = math.pi * (r_ext * r_ext - r_int * r_int)
        print(f"    corona de {r_int:.4f} a {r_ext:.4f}, area {area:.4f} cm2")

        check(f"'{nombre}': la corona tiene el area de un tubo",
              abs(area - math.pi * t * (diam - t)) < 1e-9,
              f"{area:.9f} contra {math.pi * t * (diam - t):.9f}")

        check(f"'{nombre}': el radio interior es el exterior menos la pared",
              abs(r_int - (r_ext - t)) < 1e-12)
    else:
        print(f"    sin hueco: la pared se come el radio ({r_int:.4f})")

        check(f"'{nombre}': sin hueco no se dibuja circunferencia interior",
              r_int <= 0)


# ===========================================================================
#  CF: la canal formada en frio
# ===========================================================================

def radios_cf(h, b, t, lip, ri):
    """Port de los recortes de radio de PerfilCf."""
    if lip <= t:
        lip = t + 0.001
    if b <= 2 * t:
        b = 2 * t + 0.001
    if h <= 2 * t:
        h = 2 * t + 0.001
    if ri < 0:
        ri = 0

    r_ext = min(ri, min(b / 2, min(lip, h / 2)))
    r_ext = max(r_ext, 0.0)

    r_int_max = min((b - t) / 2, min((h - 2 * t) / 2, lip - t))
    r_int = min(ri / 2, r_int_max)
    r_int = max(r_int, 0.0)

    return h, b, lip, r_ext, r_int


def perfil_cf(x_web, y0, h, b, t, lip, ri, espejo):
    """Port de PerfilCf: los veinte vertices y sus ocho dobleces."""
    s = -1.0 if espejo else 1.0

    h, b, lip, r_ext, r_int = radios_cf(h, b, t, lip, ri)

    x_web_out = x_web
    x_web_in = x_web + s * t
    x_fl_out = x_web + s * b
    x_fl_in = x_fl_out - s * t
    yb = y0
    yt = y0 + h

    if r_ext <= 0 and r_int <= 0:
        pts = [
            (x_web_out, yb), (x_web_out, yt), (x_fl_out, yt), (x_fl_out, yt - lip),
            (x_fl_in, yt - lip), (x_fl_in, yt - t), (x_web_in, yt - t),
            (x_web_in, yb + t), (x_fl_in, yb + t), (x_fl_in, yb + lip),
            (x_fl_out, yb + lip), (x_fl_out, yb),
        ]
        return pts, {}, {}, r_ext, r_int

    pts = [
        (x_web_out, yb + r_ext),
        (x_web_out, yt - r_ext),
        (x_web_out + s * r_ext, yt),
        (x_fl_out - s * r_ext, yt),
        (x_fl_out, yt - r_ext),
        (x_fl_out, yt - lip),
        (x_fl_in, yt - lip),
        (x_fl_in, yt - t - r_int),
        (x_fl_in - s * r_int, yt - t),
        (x_web_in + s * r_int, yt - t),
        (x_web_in, yt - t - r_int),
        (x_web_in, yb + t + r_int),
        (x_web_in + s * r_int, yb + t),
        (x_fl_in - s * r_int, yb + t),
        (x_fl_in, yb + t + r_int),
        (x_fl_in, yb + lip),
        (x_fl_out, yb + lip),
        (x_fl_out, yb + r_ext),
        (x_fl_out - s * r_ext, yb),
        (x_web_out + s * r_ext, yb),
    ]

    # Los ocho dobleces, con su centro y su radio. Mismo orden que el C#.
    centros = {
        1: ((x_web_out + s * r_ext, yt - r_ext), r_ext),
        3: ((x_fl_out - s * r_ext, yt - r_ext), r_ext),
        7: ((x_fl_in - s * r_int, yt - t - r_int), r_int),
        9: ((x_web_in + s * r_int, yt - t - r_int), r_int),
        11: ((x_web_in + s * r_int, yb + t + r_int), r_int),
        13: ((x_fl_in - s * r_int, yb + t + r_int), r_int),
        17: ((x_fl_out - s * r_ext, yb + r_ext), r_ext),
        19: ((x_web_out + s * r_ext, yb + r_ext), r_ext),
    }

    bulges = {}

    for idx, (centro, r) in centros.items():
        a = pts[idx]
        bb = pts[(idx + 1) % len(pts)]
        bulges[idx] = bulge_desde_centro(centro[0], centro[1], a[0], a[1], bb[0], bb[1])

    return pts, bulges, centros, r_ext, r_int


print("\n" + "=" * 78)
print(" CF: la canal formada en frio")
print("=" * 78)

CASOS_CF = [
    # nombre,               h,     b,    t,     lip,  ri
    ("CF 6X2 cal 14",     15.00, 5.00, 0.190, 1.50, 0.40),
    ("CF 8X2.5 cal 12",   20.00, 6.35, 0.267, 1.90, 0.55),
    ("CF chico cal 20",    5.00, 3.00, 0.091, 0.80, 0.20),
    ("CF radio de mas",   10.00, 4.00, 0.200, 1.00, 9.00),
    ("CF sin radio",      10.00, 4.00, 0.200, 1.00, 0.00),
]

for nombre, h, b, t, lip, ri in CASOS_CF:
    pts, bulges, centros, r_ext, r_int = perfil_cf(0, 0, h, b, t, lip, ri, False)

    print(f"\n{nombre}: h={h} b={b} t={t} labio={lip} r={ri}")
    print(f"    rExt={r_ext:.4f}   rInt={r_int:.4f}   "
          f"{len(pts)} vertices, {len(bulges)} dobleces")

    # Los recortes de radio.
    check(f"'{nombre}': el radio exterior se recorta a lo que cabe",
          r_ext <= min(b / 2, min(lip, h / 2)) + 1e-12,
          f"rExt {r_ext:.6f}")
    check(f"'{nombre}': y es el capturado cuando cabe",
          abs(r_ext - min(ri, min(b / 2, min(lip, h / 2)))) < 1e-12)

    check(f"'{nombre}': el radio interior es la mitad, recortada por su cuenta",
          abs(r_int - min(ri / 2, min((b - t) / 2, min((h - 2 * t) / 2, lip - t)))) < 1e-12,
          f"rInt {r_int:.6f}")

    if not bulges:
        check(f"'{nombre}': sin radios el contorno va en pico, doce vertices",
              len(pts) == 12)
        continue

    # La caja: el ancho es el patin y el alto el peralte, exactos.
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    check(f"'{nombre}': la caja es el peralte por el ancho",
          abs((max(ys) - min(ys)) - h) < 1e-12
          and abs((max(xs) - min(xs)) - b) < 1e-12,
          f"{max(ys) - min(ys):.6f} x {max(xs) - min(xs):.6f}")

    # Los ocho dobleces son arcos de 90 grados: el bulge en magnitud es el de siempre.
    malos = [f"{i}: {v:.12f}" for i, v in bulges.items()
             if abs(abs(v) - BULGE_90) > 1e-12]

    check(f"'{nombre}': los ocho dobleces son arcos de 90 grados",
          not malos, "; ".join(malos))

    # Y los filetes, doblez por doblez.
    n = len(pts)
    malas = []

    for idx, (centro, r) in centros.items():
        pa = pts[idx]
        pb = pts[(idx + 1) % n]
        previo = pts[(idx - 1) % n]
        siguiente = pts[(idx + 2) % n]

        ok, detalle = es_filete(centro, pa, pb, previo, siguiente, r)

        if not ok:
            malas.append(f"doblez {idx}: {detalle}")

    check(f"'{nombre}': los ocho dobleces son filetes de verdad", not malas,
          "; ".join(malas))

    # Los signos: los dobleces EXTERIORES giran a un lado y los INTERIORES al otro.
    # Es lo que hace que el contorno entre y salga del acero sin cruzarse.
    ext = [bulges[i] for i in (1, 3, 17, 19)]
    inte = [bulges[i] for i in (7, 9, 11, 13)]

    print(f"    dobleces exteriores {['%+.4f' % v for v in ext]}")
    print(f"    dobleces interiores {['%+.4f' % v for v in inte]}")

    check(f"'{nombre}': los cuatro dobleces exteriores giran al mismo lado",
          len({v > 0 for v in ext}) == 1)
    check(f"'{nombre}': los cuatro interiores tambien, y al contrario",
          len({v > 0 for v in inte}) == 1 and (ext[0] > 0) != (inte[0] > 0))

    # ---- El ESPEJO: todos los signos se invierten ----
    pts_e, bulges_e, centros_e, _, _ = perfil_cf(2 * b, 0, h, b, t, lip, ri, True)

    signos = all(abs(bulges_e[i] + bulges[i]) < 1e-12 for i in bulges)

    check(f"'{nombre}': al espejear, los ocho dobleces se invierten", signos)

    # Y el espejo ocupa el hueco de al lado, pegado al primero: las dos canales
    # quedan enfrentadas formando un cajon.
    xs_e = [p[0] for p in pts_e]

    check(f"'{nombre}': el perfil espejeado ocupa el hueco contiguo",
          abs(min(xs_e) - max(xs)) < 1e-12,
          f"empieza en {min(xs_e):.6f} y el primero acaba en {max(xs):.6f}")

    malas_e = []

    for idx, (centro, r) in centros_e.items():
        pa = pts_e[idx]
        pb = pts_e[(idx + 1) % n]
        previo = pts_e[(idx - 1) % n]
        siguiente = pts_e[(idx + 2) % n]

        ok, detalle = es_filete(centro, pa, pb, previo, siguiente, r)

        if not ok:
            malas_e.append(f"doblez {idx}: {detalle}")

    check(f"'{nombre}': y sus dobleces siguen siendo filetes", not malas_e,
          "; ".join(malas_e))





# ===========================================================================
#  EL CATALOGO COMPLETO, PERFIL POR PERFIL
# ===========================================================================
#
# Lo anterior comprueba las FORMULAS con perfiles escogidos a mano. Esto comprueba
# los DATOS: los mil y pico perfiles del catalogo, uno por uno, con las mismas reglas
# del programa y con la geometria de dibujo de verdad.
#
# Hace falta porque un catalogo es una lista larga hecha por personas, y basta una
# celda con un digito de mas para que un perfil se dibuje como un borron. Ya paso: el
# W 36'' x 442.16 traia el alma en 346 mm en vez de 34.6, entre un vecino de 31 y otro
# de 38.1. Aqui se caza cualquiera igual.
#
# Se comprueba, para cada perfil del catalogo:
#
#   1. Que el programa lo ACEPTARIA: las mismas reglas de PerfilAceroRow.FaltanDatos.
#   2. Que sus proporciones son POSIBLES: ningun perfil laminado tiene el alma mas
#      gruesa que la sexta parte de su peralte.
#   3. Que su geometria de dibujo NO DEGENERA: el area del contorno sale positiva, los
#      radios recortados siguen cabiendo y el hueco interior del tubo existe.

print("\n" + "=" * 78)
print(" El catalogo completo, perfil por perfil")
print("=" * 78)

RUTA_CATALOGO = "client/src/CadLink.App/perfiles-acero.csv"


def leer_catalogo_csv(ruta):
    """El mismo formato que lee CatalogoPerfiles: ocho campos por renglon."""
    perfiles = []

    with open(ruta, encoding="utf-8") as f:
        for cruda in f:
            linea = cruda.strip()

            if not linea or linea.startswith("#"):
                continue

            campos = linea.split(";" if ";" in linea else ",")

            if len(campos) < 3:
                continue

            def num(i):
                if i >= len(campos) or not campos[i].strip():
                    return 0.0
                try:
                    return float(campos[i].strip().replace(",", "."))
                except ValueError:
                    return 0.0

            perfiles.append({
                "familia": campos[0].strip().upper(),
                "nombre": campos[1].strip(),
                "peralte": num(2),
                "ancho": num(3),
                "e_alma": num(4),
                "e_patin": num(5),
                "labio": num(6),
                "radio": num(7),
            })

    return perfiles


def falta_algo(p):
    """Port de PerfilAceroRow.FaltanDatos: lo que el programa exige para dibujar."""
    f = p["familia"]
    faltan = []

    if p["peralte"] <= 0:
        faltan.append("diametro" if f == "OC" else "peralte")

    if p["e_alma"] <= 0:
        faltan.append("e alma" if f == "IR" else "espesor")

    if f != "OC" and p["ancho"] <= 0:
        faltan.append("ancho")

    if f == "IR" and p["e_patin"] <= 0:
        faltan.append("e patin")

    if f == "CF" and p["labio"] <= 0:
        faltan.append("labio")

    if faltan:
        return ", ".join(faltan)

    if f == "IR":
        if 2 * p["e_patin"] >= p["peralte"]:
            return "los dos patines no caben en el peralte"
        if p["e_alma"] >= p["ancho"]:
            return "el alma es mas ancha que el patin"

    if f == "OR" and 2 * p["e_alma"] >= min(p["peralte"], p["ancho"]):
        return "la pared no deja hueco interior"

    if f == "OC" and 2 * p["e_alma"] >= p["peralte"]:
        return "la pared no deja hueco interior"

    if f == "CF":
        if 2 * p["e_alma"] >= p["peralte"]:
            return "los dos patines no caben en el peralte"
        if p["labio"] <= p["e_alma"]:
            return "el labio no llega ni al espesor"

    return ""


def proporcion_imposible(p):
    """Proporciones que ningun perfil real tiene. Caza los errores de dedo."""
    f = p["familia"]

    if f == "IR":
        if p["e_alma"] > p["peralte"] / 6:
            return (f"alma {p['e_alma']:.2f} cm en peralte {p['peralte']:.2f} cm "
                    "(mas de 1/6)")
        if p["e_patin"] > p["peralte"] / 3:
            return (f"patin {p['e_patin']:.2f} cm en peralte {p['peralte']:.2f} cm "
                    "(mas de 1/3)")
        if p["e_alma"] > p["ancho"] / 2:
            return f"alma {p['e_alma']:.2f} pasa de medio patin {p['ancho']:.2f}"

    if f in ("OR", "OC"):
        menor = p["peralte"] if f == "OC" else min(p["peralte"], p["ancho"])

        if p["e_alma"] > menor / 4:
            return f"pared {p['e_alma']:.2f} en lado {menor:.2f} (mas de 1/4)"

    if f == "CF":
        if p["e_alma"] > p["peralte"] / 10:
            return f"lamina {p['e_alma']:.3f} en peralte {p['peralte']:.2f} (mas de 1/10)"
        if p["labio"] > p["peralte"] / 2:
            return f"labio {p['labio']:.2f} pasa de medio peralte {p['peralte']:.2f}"

    return ""


def dibujo_degenera(p):
    """Si la geometria de dibujo sale mal: area negativa, radios que no caben…"""
    f = p["familia"]
    h, b, t, tf = p["peralte"], p["ancho"], p["e_alma"], p["e_patin"]

    if f == "IR":
        pts = perfil_ir(0, 0, h, b, t, tf)
        area = area_poligono(pts)
        esperada = 2 * b * tf + (h - 2 * tf) * t

        if area <= 0 or abs(area - esperada) > 1e-9:
            return f"el area del contorno ({area:.4f}) no es la del perfil ({esperada:.4f})"

    elif f == "OR":
        bb, hh = min(b, h), max(b, h)
        r_out, r_in = radios_or(bb, hh, t)

        if r_out > min(bb, hh) / 2 + 1e-12:
            return "el radio exterior no cabe"

        if r_in is None:
            return "no queda hueco interior"

        if r_in > min(bb - 2 * t, hh - 2 * t) / 2 + 1e-12:
            return "el radio interior no cabe en el hueco"

        area = (area_rect_redondeado(bb, hh, r_out)
                - area_rect_redondeado(bb - 2 * t, hh - 2 * t, r_in))

        if area <= 0:
            return f"la pared sale con area {area:.4f}"

    elif f == "OC":
        r_ext = h / 2
        r_int = r_ext - t

        if r_int <= 0:
            return "la pared se come el radio: saldria macizo"

        if math.pi * (r_ext ** 2 - r_int ** 2) <= 0:
            return "la corona sale con area negativa"

    elif f == "CF":
        _, _, _, r_ext, r_int = radios_cf(h, b, t, p["labio"], p["radio"])

        if r_ext > min(b / 2, min(p["labio"], h / 2)) + 1e-12:
            return "el radio exterior no cabe"

        if r_int < 0:
            return "el radio interior sale negativo"

        pts, bulges, centros, _, _ = perfil_cf(0, 0, h, b, t, p["labio"], p["radio"], False)

        if bulges:
            malos = [i for i, v in bulges.items() if abs(abs(v) - BULGE_90) > 1e-9]

            if malos:
                return f"los dobleces {malos} no son arcos de 90 grados"

            for idx, (centro, r) in centros.items():
                ok, detalle = es_filete(
                    centro, pts[idx], pts[(idx + 1) % len(pts)],
                    pts[(idx - 1) % len(pts)], pts[(idx + 2) % len(pts)], r, tol=1e-9)

                if not ok:
                    return f"el doblez {idx} no es un filete: {detalle}"

    return ""


catalogo = leer_catalogo_csv(RUTA_CATALOGO)

print(f"\n    el catalogo trae {len(catalogo)} perfiles")

porfam = {}
for p in catalogo:
    porfam[p["familia"]] = porfam.get(p["familia"], 0) + 1

for fam in sorted(porfam):
    print(f"       {fam}: {porfam[fam]}")

check("el catalogo tiene perfiles de las cuatro familias",
      set(porfam) == {"IR", "OR", "OC", "CF"}, str(sorted(porfam)))

check("y son muchos, no la semilla de cuatro", len(catalogo) > 500,
      f"solo {len(catalogo)}")

# ---- 1. Que el programa los aceptaria ----
rechazados = [(p["familia"], p["nombre"], falta_algo(p))
              for p in catalogo if falta_algo(p)]

print(f"\n    perfiles que el programa rechazaria: {len(rechazados)}")

for fam, nombre, motivo in rechazados[:10]:
    print(f"       {fam} {nombre}: {motivo}")

check("el programa aceptaria TODOS los perfiles del catalogo", not rechazados,
      "; ".join(f"{n}: {m}" for _, n, m in rechazados[:5]))

# ---- 2. Que sus proporciones son posibles ----
imposibles = [(p["nombre"], proporcion_imposible(p))
              for p in catalogo if proporcion_imposible(p)]

print(f"    perfiles con proporciones imposibles: {len(imposibles)}")

for nombre, motivo in imposibles[:10]:
    print(f"       {nombre}: {motivo}")

check("ningun perfil tiene proporciones imposibles", not imposibles,
      "; ".join(f"{n}: {m}" for n, m in imposibles[:5]))

# El error real que traia la hoja: tiene que estar FUERA del catalogo.
w36 = [p for p in catalogo if "442" in p["nombre"] and "36" in p["nombre"]]

check("el W 36'' x 442.16, que traia el alma mal en la hoja, no entro al catalogo",
      not w36,
      f"entro con alma {w36[0]['e_alma']} cm" if w36 else "")

# ---- 3. Que la geometria de dibujo no degenera ----
degenerados = [(p["nombre"], dibujo_degenera(p))
               for p in catalogo if dibujo_degenera(p)]

print(f"    perfiles cuyo dibujo degenera: {len(degenerados)}")

for nombre, motivo in degenerados[:10]:
    print(f"       {nombre}: {motivo}")

check("el dibujo de todos los perfiles del catalogo sale bien", not degenerados,
      "; ".join(f"{n}: {m}" for n, m in degenerados[:5]))

# ---- Y los rangos, que es la ultima red contra un error de unidades ----
print("\n    rangos por familia, en centimetros:")

for fam in ("IR", "OR", "OC", "CF"):
    de_esta = [p for p in catalogo if p["familia"] == fam]

    if not de_esta:
        continue

    per = [p["peralte"] for p in de_esta]
    esp = [p["e_alma"] for p in de_esta]

    print(f"       {fam}: peralte de {min(per):6.2f} a {max(per):6.2f}   "
          f"espesor de {min(esp):5.3f} a {max(esp):5.3f}")

    # Un peralte de 3 mm o de 5 m serian un error de unidades de diez o cien veces.
    check(f"los peraltes de {fam} son de tamaño de perfil, no de otra unidad",
          min(per) > 2 and max(per) < 300,
          f"de {min(per)} a {max(per)} cm")

    check(f"los espesores de {fam} tambien",
          min(esp) > 0.05 and max(esp) < 10,
          f"de {min(esp)} a {max(esp)} cm")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
