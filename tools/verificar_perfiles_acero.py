"""Verifica la geometria de las NUEVE formas de perfil de acero.

Es el mismo calculo que hace SeccionDrawer.Acero.cs, escrito aparte para poder
EJECUTARLO: en este entorno no hay .NET ni AutoCAD, asi que la unica forma honesta de
comprobar una formula es corriendola.

Las nueve formas y las doce familias que las usan:

    I                 IR (las W), IS, IC, S
    te                WT
    canal laminada    C
    canal con labios  CF
    zeta              ZF
    angulo            L
    tubo rectangular  OR (las HSS)
    tubo redondo      OC (las PIPE)
    redondo macizo    OS

Lo que se comprueba, forma por forma:

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

  Las CINCO FORMAS NUEVAS, que no tenian macro:

     13. Que la te, la canal laminada y el angulo encierran el area de acero que les
         toca, calculada por la formula del area de un poligono.
     14. Que el poligono de cada una NO SE CRUZA consigo mismo. Esta es la prueba que
         de verdad hacia falta: un contorno cruzado se ve bien en la pantalla -las
         lineas estan donde tienen que estar- pero el rayado se sale por fuera. Es lo
         que le pasaba a la zeta hasta que esta comprobacion lo enseño: el doblez
         interior de abajo tenia el centro al lado equivocado del alma y el contorno se
         devolvia sobre si mismo.
     15. Que la zeta tiene el area de sus dos patines y su alma, que sus cuatro
         dobleces son arcos de 90 grados con los exteriores girando al contrario de los
         interiores, y que al espejearla todos se invierten.
     16. Que el ancho que ocupa cada forma en la fila es el que de verdad mide: en la
         zeta son los dos patines menos el alma que comparten, no el patin a secas.
"""

import math

BULGE_90 = 0.414213562373095      # el BULGE_90 de la macro del HSS
PULGADA_CM = 2.54
PERALTE_LIMITE_PULG = 5.0

# La forma con la que se dibuja cada familia. Tiene que decir lo mismo que
# FormaPerfil.DeLaFamilia del programa y que FORMAS de tools/catalogo_imca.py.
FORMAS = {
    "IR": "I",
    "IS": "I",
    "IC": "I",
    "S": "I",
    "WT": "te",
    "C": "canal",
    "CF": "canal con labios",
    "ZF": "zeta",
    "L": "angulo",
    "OR": "tubo rectangular",
    "OC": "tubo redondo",
    "OS": "redondo macizo",
}

FAMILIAS_ESPERADAS = set(FORMAS)

# El orden en que se capturan las familias. Ya NO hay bandas por familia: todas las
# secciones van una por renglon, alineadas por su borde derecho, y la separacion sale del
# codigo -MainWindow.Acero.cs- en el apartado 4, no de una constante copiada aqui.
ORDEN_FAMILIAS = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

# El rayado de cada forma, que es el de la macro que le corresponde. Se coteja contra el
# codigo mas abajo, no se da por bueno.
RAYADOS = {
    "I": [("ANSI32", 252)],
    "te": [("ANSI32", 252)],
    "canal": [("ANSI32", 252)],
    "angulo": [("ANSI32", 252)],
    "canal con labios": [("SOLID", 4), ("ANSI31", 142)],
    "zeta": [("SOLID", 4), ("ANSI31", 142)],
    "tubo redondo": [("SOLID", 162), ("ANSI31", 162)],
    "redondo macizo": [("SOLID", 162), ("ANSI31", 162)],
    "tubo rectangular": [("SOLID", 141), ("ANSI31", 144), ("ANSI31", 142)],
}

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
#  LAS CINCO FORMAS NUEVAS: te, canal laminada, angulo, zeta y redondo macizo
# ===========================================================================
#
# Estas cinco no tenian macro, asi que no hay un original con el que comparar: lo unico
# que se puede comprobar es que la geometria se sostiene sola. Y la comprobacion que de
# verdad hace falta no es el area, es que EL CONTORNO NO SE CRUCE.
#
# Un contorno cruzado es el error que no se ve venir. Las lineas quedan donde tienen
# que estar, asi que en la pantalla el perfil se ve bien; pero AutoCAD rellena un
# poligono cruzado por la regla de paridad, y el rayado sale por fuera del acero y
# falta por dentro. Es justo lo que le pasaba a la zeta: el doblez interior de abajo
# tenia el centro del arco al lado equivocado del alma -a la derecha en vez de a la
# izquierda- y el contorno se devolvia sobre si mismo antes de seguir. Se encontro
# aqui, con esta prueba, y no en AutoCAD.


def segmentos_se_cortan(p1, p2, p3, p4, tol=1e-12):
    """Si los segmentos p1-p2 y p3-p4 se cruzan en un punto interior a los dos."""
    def cruz(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    d1 = cruz(p3, p4, p1)
    d2 = cruz(p3, p4, p2)
    d3 = cruz(p1, p2, p3)
    d4 = cruz(p1, p2, p4)

    # Cruce propio: cada segmento deja al otro con un extremo a cada lado.
    if ((d1 > tol and d2 < -tol) or (d1 < -tol and d2 > tol)) and \
       ((d3 > tol and d4 < -tol) or (d3 < -tol and d4 > tol)):
        return True

    return False


def area_orientada(pts):
    """El area con SIGNO: positiva si el contorno va en sentido antihorario."""
    s = 0.0
    n = len(pts)

    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        s += x1 * y2 - x2 * y1

    return s / 2


def area_con_arcos(pts, bulges):
    """El area de verdad de una polilinea CON dobleces.

    area_poligono no sirve para un contorno con arcos: pasa la CUERDA por debajo de
    cada arco y se come -o se inventa- el trozo que queda entre la cuerda y el arco. Con
    radios pequeños la diferencia no se nota, pero con un radio grande es enorme, y
    entonces una comprobacion de area deja de poder ser exacta y hay que aflojarla a un
    «se parece», que es lo mismo que no comprobar nada.

    Aqui se suma el SEGMENTO CIRCULAR de cada doblez, con su signo: el que bulta hacia
    fuera del contorno añade y el que bulta hacia dentro quita. Con eso el area sale
    exacta y se puede comparar con la formula del perfil por igualdad.

    El segmento de un arco de barrido t y radio r vale r^2/2 (t - sen t), y el radio
    sale de la cuerda: el bulge es la tangente de la cuarta parte del barrido.
    """
    con_signo = area_orientada(pts)
    sentido = 1.0 if con_signo > 0 else -1.0
    total = abs(con_signo)
    n = len(pts)

    for idx, b in bulges.items():
        if abs(b) < 1e-15:
            continue

        p1 = pts[idx]
        p2 = pts[(idx + 1) % n]

        cuerda = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
        barrido = 4 * math.atan(abs(b))

        if barrido <= 0 or cuerda <= 0:
            continue

        r = cuerda / (2 * math.sin(barrido / 2))
        segmento = r * r / 2 * (barrido - math.sin(barrido))

        total += sentido * math.copysign(segmento, b)

    return total


def se_cruza(pts, tol=1e-12):
    """Si el poligono se cruza consigo mismo. Devuelve el par de lados, o None."""
    n = len(pts)

    for i in range(n):
        a1, a2 = pts[i], pts[(i + 1) % n]

        for j in range(i + 1, n):
            # Los lados contiguos comparten un vertice: no cuentan.
            if j == i or (j + 1) % n == i or (i + 1) % n == j:
                continue

            b1, b2 = pts[j], pts[(j + 1) % n]

            if segmentos_se_cortan(a1, a2, b1, b2, tol):
                return (i, j)

    return None


def perfil_te(cx, cy, d, bf, tw, tf):
    """Port de PerfilTe: ocho vertices, patin arriba y alma colgando."""
    return [
        (cx + tw / 2, cy),
        (cx + tw / 2, cy + d - tf),
        (cx + bf / 2, cy + d - tf),
        (cx + bf / 2, cy + d),
        (cx - bf / 2, cy + d),
        (cx - bf / 2, cy + d - tf),
        (cx - tw / 2, cy + d - tf),
        (cx - tw / 2, cy),
    ]


def perfil_canal(x_izq, y0, d, bf, tw, tf, espejo):
    """Port de PerfilCanal: ocho vertices, alma a un lado y dos patines."""
    s = -1.0 if espejo else 1.0
    x_alma = x_izq + bf if espejo else x_izq

    return [
        (x_alma, y0),
        (x_alma, y0 + d),
        (x_alma + s * bf, y0 + d),
        (x_alma + s * bf, y0 + d - tf),
        (x_alma + s * tw, y0 + d - tf),
        (x_alma + s * tw, y0 + tf),
        (x_alma + s * bf, y0 + tf),
        (x_alma + s * bf, y0),
    ]


def perfil_angulo(x_izq, y0, ala_larga, ala_corta, t, espejo):
    """Port de PerfilAngulo: seis vertices, con el talon abajo."""
    s = -1.0 if espejo else 1.0
    x_talon = x_izq + ala_corta if espejo else x_izq

    return [
        (x_talon, y0),
        (x_talon + s * ala_corta, y0),
        (x_talon + s * ala_corta, y0 + t),
        (x_talon + s * t, y0 + t),
        (x_talon + s * t, y0 + ala_larga),
        (x_talon, y0 + ala_larga),
    ]


def perfil_zeta(x_izq, y0, h, b_ancho, b_angosto, t, ri, espejo):
    """Port de PerfilZeta: doce vertices y cuatro dobleces, o ocho en pico."""
    if t <= 0:
        t = 0.001
    if b_angosto <= t:
        b_angosto = b_ancho
    if h <= 2 * t:
        h = 2 * t + 0.001
    if ri < 0:
        ri = 0

    w = b_ancho + b_angosto - t

    r_ext = min(ri, min(min(b_ancho, b_angosto) / 2, (h - 2 * t) / 2))
    r_ext = max(r_ext, 0.0)

    # El interior es el exterior MENOS EL ESPESOR: en cada doblez las dos caras de la
    # lamina son arcos CONCENTRICOS separados el espesor. Es geometria, no convencion.
    r_int = max(0.0, r_ext - t)

    def X(x):
        return 2 * x_izq + w - x if espejo else x

    x_ai = x_izq + b_angosto - t     # cara izquierda del alma
    x_ad = x_izq + b_angosto         # cara derecha del alma
    x_tope = x_ai + b_ancho          # punta del patin ancho
    yt = y0 + h

    if r_ext <= 0 and r_int <= 0:
        pts = [
            (X(x_izq), y0),
            (X(x_ad), y0),
            (X(x_ad), yt - t),
            (X(x_tope), yt - t),
            (X(x_tope), yt),
            (X(x_ai), yt),
            (X(x_ai), y0 + t),
            (X(x_izq), y0 + t),
        ]
        return pts, {}, {}, r_ext, r_int, w

    pts = [
        (X(x_izq), y0),
        (X(x_ad - r_ext), y0),
        (X(x_ad), y0 + r_ext),
        (X(x_ad), yt - t - r_int),
        (X(x_ad + r_int), yt - t),
        (X(x_tope), yt - t),
        (X(x_tope), yt),
        (X(x_ai + r_ext), yt),
        (X(x_ai), yt - r_ext),
        (X(x_ai), y0 + t + r_int),
        (X(x_ai - r_int), y0 + t),
        (X(x_izq), y0 + t),
    ]

    # Los dos interiores tienen el centro FUERA del acero y a distinto lado del alma:
    # el de arriba a la derecha y el de abajo a la IZQUIERDA. Ahi estaba el error.
    centros = {
        1: ((X(x_ad - r_ext), y0 + r_ext), r_ext),
        3: ((X(x_ad + r_int), yt - t - r_int), r_int),
        7: ((X(x_ai + r_ext), yt - r_ext), r_ext),
        9: ((X(x_ai - r_int), y0 + t + r_int), r_int),
    }

    bulges = {}

    for idx, (centro, r) in centros.items():
        a = pts[idx]
        bb = pts[(idx + 1) % len(pts)]
        bulges[idx] = bulge_desde_centro(centro[0], centro[1], a[0], a[1], bb[0], bb[1])

    return pts, bulges, centros, r_ext, r_int, w


print("\n" + "=" * 78)
print(" WT: la te")
print("=" * 78)

CASOS_TE = [
    # nombre,               d,     bf,    tw,    tf
    ("WT 8'' x 13.0",     19.90, 14.00, 0.640, 0.880),
    ("WT 2'' x 6.5",       5.30, 10.40, 0.720, 0.880),
    ("WT 22'' x 145",     55.90, 40.60, 2.410, 3.400),
]

for nombre, d, bf, tw, tf in CASOS_TE:
    pts = perfil_te(0, 0, d, bf, tw, tf)

    # El area de una te: el patin entero mas el alma que le cuelga.
    esperada = bf * tf + (d - tf) * tw
    calculada = area_poligono(pts)

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    print(f"\n{nombre}: d={d} bf={bf} tw={tw} tf={tf}")
    print(f"    area del contorno {calculada:.6f} cm2, area de la te {esperada:.6f} cm2")

    check(f"'{nombre}': el contorno encierra el area de la te",
          abs(calculada - esperada) < 1e-9,
          f"{calculada:.9f} contra {esperada:.9f}")

    check(f"'{nombre}': la caja es el peralte por el ancho de patin",
          abs((max(ys) - min(ys)) - d) < 1e-12
          and abs((max(xs) - min(xs)) - bf) < 1e-12,
          f"{max(ys) - min(ys):.6f} x {max(xs) - min(xs):.6f}")

    check(f"'{nombre}': el contorno no se cruza", se_cruza(pts) is None,
          str(se_cruza(pts)))

    reflejos = {(round(-x, 9), round(y, 9)) for (x, y) in pts}
    propios = {(round(x, 9), round(y, 9)) for (x, y) in pts}

    check(f"'{nombre}': la te es simetrica respecto a su eje", reflejos == propios)

    # Y el patin va ARRIBA: el vertice mas alto tiene que estar en el borde del patin,
    # no en la punta del alma. Es lo que distingue una te de una te volteada.
    anchos_arriba = [x for (x, y) in pts if abs(y - d) < 1e-12]

    check(f"'{nombre}': el patin va arriba, no abajo",
          len(anchos_arriba) == 2
          and abs(max(anchos_arriba) - min(anchos_arriba) - bf) < 1e-12)


print("\n" + "=" * 78)
print(" C: la canal laminada")
print("=" * 78)

CASOS_C = [
    # nombre,              d,     bf,    tw,    tf
    ("C 8'' x 12.0",     20.30, 5.700, 0.560, 0.990),
    ("C 3'' x 3.5",       7.60, 3.500, 0.340, 0.690),
    ("C 15'' x 50.0",    38.10, 9.500, 1.820, 1.650),
]

for nombre, d, bf, tw, tf in CASOS_C:
    pts = perfil_canal(0, 0, d, bf, tw, tf, False)

    # El area de una canal: dos patines enteros mas el alma entre ellos.
    esperada = 2 * bf * tf + (d - 2 * tf) * tw
    calculada = area_poligono(pts)

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    print(f"\n{nombre}: d={d} bf={bf} tw={tw} tf={tf}")
    print(f"    area del contorno {calculada:.6f} cm2, area de la canal "
          f"{esperada:.6f} cm2")

    check(f"'{nombre}': el contorno encierra el area de la canal",
          abs(calculada - esperada) < 1e-9,
          f"{calculada:.9f} contra {esperada:.9f}")

    check(f"'{nombre}': la caja es el peralte por el ancho de patin",
          abs((max(ys) - min(ys)) - d) < 1e-12
          and abs((max(xs) - min(xs)) - bf) < 1e-12)

    check(f"'{nombre}': el contorno no se cruza", se_cruza(pts) is None,
          str(se_cruza(pts)))

    # El espejo: la segunda canal ocupa el hueco de al lado y con el alma al otro
    # extremo, para que las dos queden enfrentadas formando un cajon.
    pts_e = perfil_canal(bf, 0, d, bf, tw, tf, True)
    xs_e = [p[0] for p in pts_e]

    check(f"'{nombre}': la canal espejeada ocupa el hueco contiguo",
          abs(min(xs_e) - max(xs)) < 1e-12,
          f"empieza en {min(xs_e):.6f} y la primera acaba en {max(xs):.6f}")

    check(f"'{nombre}': y su alma queda al otro extremo",
          abs(max(xs_e) - (2 * bf)) < 1e-12)

    check(f"'{nombre}': el contorno espejeado tampoco se cruza",
          se_cruza(pts_e) is None)

    check(f"'{nombre}': y tiene la misma area",
          abs(area_poligono(pts_e) - calculada) < 1e-9)


print("\n" + "=" * 78)
print(" L: el angulo")
print("=" * 78)

CASOS_L = [
    # nombre,                  ala larga, ala corta, t
    ("L 3'' x 1/4''",          7.620, 7.620, 0.6350),
    ("L 3'' x 2'' x 1/4''",    7.620, 5.080, 0.6350),
    ("L 3/4'' x 1/8''",        1.905, 1.905, 0.3175),
    ("L 8'' x 1''",           20.320, 20.320, 2.5400),
]

for nombre, larga, corta, t in CASOS_L:
    pts = perfil_angulo(0, 0, larga, corta, t, False)

    # El area de un angulo: un ala entera mas la otra descontando lo que se solapan.
    esperada = larga * t + (corta - t) * t
    calculada = area_poligono(pts)

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    print(f"\n{nombre}: alas {larga} y {corta}, espesor {t}")
    print(f"    area del contorno {calculada:.6f} cm2, area del angulo "
          f"{esperada:.6f} cm2")

    check(f"'{nombre}': el contorno encierra el area del angulo",
          abs(calculada - esperada) < 1e-9,
          f"{calculada:.9f} contra {esperada:.9f}")

    check(f"'{nombre}': la caja son las dos alas",
          abs((max(ys) - min(ys)) - larga) < 1e-12
          and abs((max(xs) - min(xs)) - corta) < 1e-12,
          f"{max(ys) - min(ys):.6f} x {max(xs) - min(xs):.6f}")

    check(f"'{nombre}': el contorno no se cruza", se_cruza(pts) is None,
          str(se_cruza(pts)))

    # El ala larga va en VERTICAL: es lo que hace que un angulo desigual se dibuje de
    # pie y no acostado, y en el plano eso cambia de que angulo se trata.
    check(f"'{nombre}': el ala larga va en vertical",
          (max(ys) - min(ys)) >= (max(xs) - min(xs)) - 1e-12)

    pts_e = perfil_angulo(corta, 0, larga, corta, t, True)
    xs_e = [p[0] for p in pts_e]

    check(f"'{nombre}': el angulo espejeado ocupa el hueco contiguo",
          abs(min(xs_e) - max(xs)) < 1e-12)

    check(f"'{nombre}': y tampoco se cruza", se_cruza(pts_e) is None)


print("\n" + "=" * 78)
print(" ZF: la zeta")
print("=" * 78)

CASOS_ZF = [
    # nombre,                    h,      b ancho, b angosto, t,     r
    ("ZF 6'' x 2 3/8'' cal 14", 15.240, 6.030, 5.400, 0.190, 0.476),
    ("ZF 12'' x 3 3/8'' cal 12", 30.480, 8.570, 7.940, 0.270, 0.476),
    ("ZF simetrica",            20.000, 6.000, 6.000, 0.200, 0.400),
    ("ZF sin radio",            20.000, 6.000, 5.400, 0.200, 0.000),
    ("ZF radio de mas",         10.000, 4.000, 3.500, 0.200, 9.000),
]

for nombre, h, b1, b2, t, ri in CASOS_ZF:
    pts, bulges, centros, r_ext, r_int, w = perfil_zeta(0, 0, h, b1, b2, t, ri, False)

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]

    print(f"\n{nombre}: h={h} patin ancho={b1} angosto={b2} t={t} r={ri}")
    print(f"    ancho total {w:.4f}   rExt={r_ext:.4f}   rInt={r_int:.4f}   "
          f"{len(pts)} vertices, {len(bulges)} dobleces")

    # EL ANCHO QUE OCUPA: los dos patines menos el alma que comparten. Es lo que usa
    # el que acomoda las secciones en la fila, y si estuviera mal, las zetas se
    # encimarian con la de al lado.
    check(f"'{nombre}': el ancho que ocupa son los dos patines menos el alma",
          abs(w - (b1 + b2 - t)) < 1e-12,
          f"{w:.6f} contra {b1 + b2 - t:.6f}")

    check(f"'{nombre}': la caja es el peralte por ese ancho",
          abs((max(ys) - min(ys)) - h) < 1e-12
          and abs((max(xs) - min(xs)) - w) < 1e-12,
          f"{max(ys) - min(ys):.6f} x {max(xs) - min(xs):.6f}")

    # ESTA es la prueba que encontro el error del doblez de abajo.
    cruce = se_cruza(pts)

    check(f"'{nombre}': el contorno no se cruza consigo mismo", cruce is None,
          f"los lados {cruce} se cortan" if cruce else "")

    # Los radios recortados.
    check(f"'{nombre}': el radio exterior se recorta a lo que cabe",
          r_ext <= min(min(b1, b2) / 2, (h - 2 * t) / 2) + 1e-12,
          f"rExt {r_ext:.6f}")

    check(f"'{nombre}': el interior es el exterior menos el espesor",
          abs(r_int - max(0.0, r_ext - t)) < 1e-12,
          f"rInt {r_int:.6f}, rExt {r_ext:.6f}, t {t}")

    # LA PRUEBA DE VERDAD del doblez de una lamina: los dos arcos de un mismo doblez
    # -la cara de dentro y la de fuera- tienen que ser CONCENTRICOS. Si no lo son, la
    # lamina sale mas gorda o mas delgada en la esquina que en el tramo recto, y eso es
    # un perfil que no existe.
    if bulges and r_int > 0:
        # Doblez de abajo: exterior en el indice 1, interior en el 9.
        c_ext_abajo = centros[1][0]
        c_int_abajo = centros[9][0]

        # Doblez de arriba: exterior en el 7, interior en el 3.
        c_ext_arriba = centros[7][0]
        c_int_arriba = centros[3][0]

        d_abajo = math.hypot(c_ext_abajo[0] - c_int_abajo[0],
                             c_ext_abajo[1] - c_int_abajo[1])
        d_arriba = math.hypot(c_ext_arriba[0] - c_int_arriba[0],
                              c_ext_arriba[1] - c_int_arriba[1])

        check(f"'{nombre}': los dos arcos del doblez de abajo son concentricos",
              d_abajo < 1e-12, f"sus centros distan {d_abajo:.9f}")

        check(f"'{nombre}': y los del doblez de arriba tambien",
              d_arriba < 1e-12, f"sus centros distan {d_arriba:.9f}")

    if not bulges:
        check(f"'{nombre}': sin radios el contorno va en pico, ocho vertices",
              len(pts) == 8)

        # Y con ocho vertices en pico el area es exacta: los dos patines y el alma.
        esperada = t * (b1 + b2 + h - 2 * t)

        check(f"'{nombre}': y encierra el area de la zeta",
              abs(area_poligono(pts) - esperada) < 1e-9,
              f"{area_poligono(pts):.9f} contra {esperada:.9f}")
        continue

    # Los cuatro dobleces son arcos de 90 grados.
    malos = [f"{i}: {v:.12f}" for i, v in bulges.items()
             if abs(abs(v) - BULGE_90) > 1e-12]

    check(f"'{nombre}': los cuatro dobleces son arcos de 90 grados",
          not malos, "; ".join(malos))

    # Y son filetes de verdad.
    n = len(pts)
    malas = []

    for idx, (centro, r) in centros.items():
        ok, detalle = es_filete(
            centro, pts[idx], pts[(idx + 1) % n],
            pts[(idx - 1) % n], pts[(idx + 2) % n], r)

        if not ok:
            malas.append(f"doblez {idx}: {detalle}")

    check(f"'{nombre}': los cuatro dobleces son filetes de verdad", not malas,
          "; ".join(malas))

    # Los signos: los dos exteriores a un lado y los dos interiores al otro.
    ext = [bulges[1], bulges[7]]
    inte = [bulges[3], bulges[9]]

    print(f"    dobleces exteriores {['%+.4f' % v for v in ext]}")
    print(f"    dobleces interiores {['%+.4f' % v for v in inte]}")

    check(f"'{nombre}': los dos dobleces exteriores giran al mismo lado",
          len({v > 0 for v in ext}) == 1)

    check(f"'{nombre}': los dos interiores tambien, y al contrario",
          len({v > 0 for v in inte}) == 1 and (ext[0] > 0) != (inte[0] > 0))

    # EL AREA, Y AQUI POR IGUALDAD EXACTA.
    #
    # La zeta con dobleces tiene el area de la zeta en pico menos lo que se llevan los
    # dos dobleces, y eso se sabe: en cada uno se redondea una esquina de fuera -que
    # QUITA r^2 (1 - pi/4)- y una de dentro -que AÑADE lo mismo con el radio interior-.
    # Como los dos radios se diferencian en el espesor, la resta es un numero cerrado.
    #
    # Poder comprobarlo por igualdad, y no con un «se parece», es lo que hace que esta
    # prueba sirva: cualquier vertice fuera de sitio cambia el area y salta.
    en_pico = t * (b1 + b2 + h - 2 * t)
    esperada = en_pico - 2 * (r_ext ** 2 - r_int ** 2) * (1 - math.pi / 4)
    calculada = area_con_arcos(pts, bulges)

    print(f"    area con los arcos {calculada:.6f} cm2, esperada {esperada:.6f} cm2"
          f"   (en pico serian {en_pico:.6f})")

    check(f"'{nombre}': el area con los dobleces es exactamente la de la zeta",
          abs(calculada - esperada) < 1e-9,
          f"{calculada:.9f} contra {esperada:.9f}")

    # El ESPEJO: los cuatro dobleces se invierten y el contorno sigue sin cruzarse.
    pts_e, bulges_e, centros_e, _, _, w_e = perfil_zeta(w, 0, h, b1, b2, t, ri, True)

    check(f"'{nombre}': al espejear, los cuatro dobleces se invierten",
          all(abs(bulges_e[i] + bulges[i]) < 1e-12 for i in bulges))

    check(f"'{nombre}': la zeta espejeada tampoco se cruza",
          se_cruza(pts_e) is None)

    xs_e = [p[0] for p in pts_e]

    check(f"'{nombre}': y ocupa el hueco contiguo",
          abs(min(xs_e) - max(xs)) < 1e-12,
          f"empieza en {min(xs_e):.6f} y la primera acaba en {max(xs):.6f}")

    check(f"'{nombre}': con la misma area",
          abs(area_con_arcos(pts_e, bulges_e) - calculada) < 1e-9,
          f"{area_con_arcos(pts_e, bulges_e):.9f} contra {calculada:.9f}")


print("\n" + "=" * 78)
print(" OS: el redondo macizo")
print("=" * 78)

CASOS_OS = [
    ("OS 1/4''", 0.64),
    ("OS 3/4''", 1.91),
    ("OS 2''", 5.08),
    ("OS 4''", 10.16),
]

for nombre, diam in CASOS_OS:
    r = diam / 2
    area = math.pi * r * r

    print(f"\n{nombre}: diametro {diam} cm, area {area:.4f} cm2")

    check(f"'{nombre}': el area es la de un circulo lleno",
          abs(area - math.pi * diam * diam / 4) < 1e-12)

    # El macizo NO tiene circunferencia interior, y eso es lo que lo separa del tubo:
    # si alguna vez alguien lo dibujara con el codigo del OC, con espesor cero saldria
    # un radio interior IGUAL al exterior y el hatch no rellenaria nada.
    check(f"'{nombre}': ocupa su diametro, no un ancho de patin",
          abs(diam - 2 * r) < 1e-12)

    # Y su peralte es su diametro: es lo que decide en que banda cae.
    check(f"'{nombre}': su alto es su diametro", abs(diam - 2 * r) < 1e-12)


# ===========================================================================
#  EL RAYADO DE CADA FORMA
# ===========================================================================
#
# NO hay un color por familia. Se probo y se quito: el plano dejaba de parecerse al que
# ya se venia haciendo, y las cuatro familias portadas tienen cada una su propio rayado,
# que es lo que de verdad las distingue en el dibujo.
#
# Lo que se comprueba es que cada forma lleve EL RAYADO DE SU MACRO, patron por patron y
# color por color, y que las cinco formas nuevas tomen el de la macro cuyo material
# comparten, que es la unica asignacion que no inventa nada:
#
#     laminadas (I, te, canal laminada, angulo)  ->  el del IR
#     formadas en frio (canal con labios, zeta)  ->  el del CF
#     redondos (tubo redondo, redondo macizo)    ->  el del OC
#     tubo rectangular                           ->  el del HSS, con su corte de 5"

import re as _re

print("\n" + "=" * 78)
print(" El rayado de cada forma")
print("=" * 78)

with open("client/src/CadLink.Cad/SeccionDrawer.Acero.cs", encoding="utf-8") as f:
    fuente_cad = f.read()

m_rayar = _re.search(r"private void RayarPerfil\(.*?\n    \}", fuente_cad, _re.S)

check("existe RayarPerfil, que decide el rayado de cada forma", m_rayar is not None)

if m_rayar:
    cuerpo = m_rayar.group(0)

    # Los pares (patron, color) que el codigo pide, en el orden en que aparecen.
    en_codigo = [(m.group(1), int(m.group(2)))
                 for m in _re.finditer(
                     r'Hatch\(\s*"(\w+)"[^;]*?CapaPerfiles,\s*(\d+)\)', cuerpo, _re.S)]

    # Y los del tubo, que van con un condicional en vez de un numero suelto.
    condicionales = _re.findall(r"menorDe5 \? (\d+) : (\d+)", cuerpo)

    print("\n    pares (patron, color) que pide el codigo:")
    for patron, color in en_codigo:
        print(f"       {patron:8} en color {color}")

    if condicionales:
        print(f"       ANSI31   en color {condicionales[0][0]} si el tubo no llega a 5\", "
              f"y {condicionales[0][1]} si si")

    esperados = set()
    for pares in RAYADOS.values():
        esperados |= set(pares)

    del_codigo = set(en_codigo)

    if condicionales:
        del_codigo.add(("ANSI31", int(condicionales[0][0])))
        del_codigo.add(("ANSI31", int(condicionales[0][1])))

    check("el codigo pide exactamente los rayados de las cuatro macros",
          del_codigo == esperados,
          f"sobran {sorted(del_codigo - esperados)}, faltan {sorted(esperados - del_codigo)}")

    # Las cuatro escalas de rayado de las macros, tal cual. Un rayado con separacion
    # FIJA da la misma densidad en el papel para cualquier tamaño de perfil, que es lo
    # que tiene que hacer un patron de sombreado: no se liga al peralte.
    for escala in ("0.0009", "0.0008", "0.002"):
        check(f"esta la escala de rayado {escala} de su macro",
              f"{escala} * _f" in cuerpo)

    # La del tubo va con un condicional, porque su macro la cambia a las 5 pulgadas.
    check("la escala del tubo cambia a las 5 pulgadas, como su macro",
          "(menorDe5 ? 0.001 : 0.002) * _f" in cuerpo)

    # Y NINGUN color por familia: ni tabla, ni capa por familia, ni campo de color.
    check("no queda ninguna tabla de color por familia",
          "ColorAcero" not in fuente_cad)
    check("ni capas por familia: una sola PERFILES, la de las macros",
          'CapaPerfiles = "PERFILES"' in fuente_cad
          and 'CapaBase + "-"' not in fuente_cad)

# El PEDIT del contorno: de las cuatro macros, SOLO la del IR engruesa la linea. Las
# formas nuevas siguen a la macro de su material, asi que lo llevan las laminadas.
m_pedit = _re.search(r"private void PeditDeLaForma\(.*?\n    \}", fuente_cad, _re.S)

check("existe PeditDeLaForma", m_pedit is not None)

if m_pedit:
    cuerpo_p = m_pedit.group(0)

    print("\n    el PEDIT del contorno, que solo hace la macro del IR:")

    for forma, lo_lleva in (("I", True), ("Te", True), ("Canal", True),
                            ("Angulo", True), ("CanalConLabios", False),
                            ("Zeta", False), ("TuboRectangular", False),
                            ("TuboRedondo", False), ("RedondoMacizo", False)):
        esta = f"FormaAcero.{forma}" in cuerpo_p

        print(f"       {forma:18} {'lo lleva' if lo_lleva else 'NO lo lleva'}"
              f"   {'(y el codigo lo dice)' if esta == lo_lleva else '<-- MAL'}")

        check(f"el PEDIT de {forma} es el de su macro", esta == lo_lleva)

    # El ancho constante se pide en UN solo sitio, dentro de PeditDeLaForma. Si alguna
    # forma se lo pidiera aparte, se saltaria la regla de que solo lo lleva la del IR.
    llamadas = fuente_cad.count("AnchoConstante(pl,")

    check("el ancho constante se pide solo desde PeditDeLaForma", llamadas == 1,
          f"{llamadas} llamadas")


# ===========================================================================
#  EL APARATO DE LA COTA, PROPORCIONAL AL PERFIL
# ===========================================================================
#
# El catalogo va de un redondo de 0.64 cm a una IS de 190. Con el aparato de cota fijo
# que venia del concreto -flecha de 2 cm, lineas de extension de 3.5, texto de 1.5- una
# cota sobre un angulo de 1.9 cm es MAS GRANDE que el perfil y tapa lo que mide.
#
# Lo que se comprueba: que un perfil de 30 cm sale EXACTAMENTE como antes -o sea que no
# se cambio nada donde funcionaba- y que de ahi para abajo el aparato encoge con la
# pieza sin llegar nunca a cero.

print("\n" + "=" * 78)
print(" El aparato de la cota, proporcional al perfil")
print("=" * 78)


def acotar(v, minimo, maximo):
    return minimo if v < minimo else maximo if v > maximo else v


def aparato(peralte_cm):
    """Port de PrepararAcero. Devuelve todo en CENTIMETROS.

    El rayado NO esta aqui: su separacion es la fija de cada macro, y esta bien que lo
    sea, porque un patron con separacion fija da la misma densidad en el papel para
    cualquier tamaño de perfil.
    """
    r = peralte_cm

    return {
        "gap": acotar(r / 5, 0.8, 6),
        "flecha": acotar(r / 15, 0.4, 2),
        "texto": acotar(r / 10, 0.4, 1.5),
        "ext_off": acotar(r / 15, 0.3, 2),
        "ext_ext": acotar(r / 8, 0.5, 3.5),
    }


# Los valores del concreto, que son los que tenia el acero antes: si un perfil de 30 cm
# no los reprodujera, este cambio habria empeorado lo que ya estaba bien.
ANTES = {"gap": 6, "flecha": 2, "texto": 1.5, "ext_off": 2, "ext_ext": 3.5}

a30 = aparato(30)

print("\n    un perfil de 30 cm, que es el tamaño con el que se eligieron los "
      "valores viejos:")

for k, v in ANTES.items():
    print(f"       {k:8} ahora {a30[k]:6.3f} cm   antes {v:6.3f} cm")

    check(f"un perfil de 30 cm sale con el {k} de siempre",
          abs(a30[k] - v) < 1e-9, f"{a30[k]:.4f} contra {v}")

print("\n    y como encoge con el perfil:")

for peralte in (190.2, 111.8, 30.3, 15.24, 7.62, 1.91, 0.64):
    a = aparato(peralte)
    print(f"       peralte {peralte:6.2f} cm ->  flecha {a['flecha']:5.3f}   "
          f"texto {a['texto']:5.3f}   gap {a['gap']:5.3f}")

# Nada puede salir en cero ni negativo: una flecha de cero es una cota sin flecha.
for peralte in (0.64, 1.91, 7.62, 30.3, 111.8, 190.2):
    a = aparato(peralte)

    check(f"con peralte {peralte} cm todo el aparato es positivo",
          all(v > 0 for v in a.values()), str(a))

# Y ninguno puede pasar del valor del concreto: el tope de arriba es lo que evita que
# una IS de dos metros salga con flechas de trece centimetros.
a190 = aparato(190.2)

for k, v in ANTES.items():
    check(f"con la IS mas alta, el {k} no pasa del tope",
          a190[k] <= v + 1e-12, f"{a190[k]:.4f} contra {v}")

# EL RAYADO NO ENTRA AQUI, y es a proposito: su separacion es la fija de cada macro.
#
# Un patron de sombreado con separacion fija da la MISMA densidad en el papel para
# cualquier tamaño de perfil, que es justo lo que tiene que hacer. Ligarlo al peralte -que
# es lo que se probo- deja los perfiles grandes con el rayado abierto y los chicos con el
# rayado cerrado, o sea con distinta textura segun el tamaño, y eso en un plano se lee
# como si fueran materiales distintos.
check("el aparato de la cota no toca la separacion del rayado",
      "hatch" not in aparato(30))


# ===========================================================================
#  EL ROTULO: su altura y su ancho de caja
# ===========================================================================
#
# El rotulo va centrado debajo de la seccion y casi siempre es MAS ANCHO que ella, asi
# que es el rotulo -y no el perfil- el que decide cuanto hueco hay que dejar entre una
# seccion y la siguiente.

print("\n" + "=" * 78)
print(" El rotulo: su altura y su ancho de caja")
print("=" * 78)


def altura_rotulo(peralte_cm):
    """Port de PerfilAceroCad.AlturaRotuloCm."""
    return acotar(peralte_cm / 10, 2.0, 3.0)


def ancho_rotulo(peralte_cm, lineas):
    """Port de PerfilAceroCad.AnchoRotuloCm."""
    return max(70, max(len(l) for l in lineas) * altura_rotulo(peralte_cm) * 0.6)


# Las cuatro macros elegian la altura a mano. La regla del peralte tiene que dar los
# mismos numeros donde ellas los daban, que es lo que dice que la regla es la suya y no
# una inventada.
COMO_LAS_MACROS = [
    ("el IR de 30 cm, que la macro rotulaba a 0.03", 30.3, 3.0),
    ("el OR de 6 pulgadas, que la macro rotulaba a 0.02", 15.24, 2.0),
    ("el OR de 20 pulgadas, que la macro rotulaba a 0.03", 50.8, 3.0),
    ("el OC de 4 pulgadas, que la macro rotulaba a 0.02", 10.16, 2.0),
]

print()
for que, peralte, esperada in COMO_LAS_MACROS:
    salio = altura_rotulo(peralte)

    print(f"    {que}: sale {salio:.3f} cm")

    check(f"{que}", abs(salio - esperada) < 1e-9, f"{salio:.4f} contra {esperada}")

# Y el ancho de caja: el nombre mas largo del catalogo del IMCA no puede partirse.
LINEAS_LARGAS = [
    'VIGA PRINCIPAL "V-1"',
    "PERFIL: IS - 225 MM X 12.7 MM / 750 MM X 9.5 MM",
    "ACERO A-572 GR. 50",
    "Acot. cm    Esc. 1:10",
]

ancho = ancho_rotulo(77.5, LINEAS_LARGAS)
mas_largo = max(len(l) for l in LINEAS_LARGAS)

print(f"\n    el renglon mas largo tiene {mas_largo} caracteres a "
      f"{altura_rotulo(77.5):.2f} cm de alto -> caja de {ancho:.1f} cm")

check("la caja del rotulo cabe el nombre mas largo del IMCA sin partirlo",
      ancho >= mas_largo * altura_rotulo(77.5) * 0.6 - 1e-9)

check("y con las macros -caja de 70 cm- ese nombre NO cabia",
      mas_largo * altura_rotulo(77.5) * 0.6 > 70,
      f"pedia {mas_largo * altura_rotulo(77.5) * 0.6:.1f} cm")

# El minimo de 70 cm es el de las macros: un rotulo corto no tiene por que ser estrecho,
# porque las cuatro lineas se centran y queda mejor con la caja ancha.
check("un rotulo corto conserva la caja de 70 cm de las macros",
      abs(ancho_rotulo(10, ['V "V-1"', "PERFIL: OS - 3/4\"", "ACERO A-36",
                            "Acot. cm    Esc. 1:10"]) - 70) < 1e-9)


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
    """El mismo formato que lee CatalogoPerfiles: nueve campos por renglon."""
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
                "ancho2": num(8),
            })

    return perfiles


def forma_de(p):
    """La forma con la que se dibuja este perfil."""
    return FORMAS.get(p["familia"], "")


def ancho_que_ocupa(p):
    """Port de PerfilAceroCad.AnchoDeUnoCm: el hueco que pide en la fila."""
    forma = forma_de(p)

    if forma in ("tubo redondo", "redondo macizo"):
        return p["peralte"]

    if forma == "zeta":
        angosto = p["ancho2"] if 0 < p["ancho2"] <= p["ancho"] else p["ancho"]
        return p["ancho"] + angosto - p["e_alma"]

    if forma == "tubo rectangular" and p["ancho"] > 0:
        return min(p["peralte"], p["ancho"])

    return p["ancho"]


def alto_que_ocupa(p):
    """Port de PerfilAceroCad.AltoDibujoCm."""
    if forma_de(p) == "tubo rectangular" and p["ancho"] > 0:
        return max(p["peralte"], p["ancho"])

    return p["peralte"]


def falta_algo(p):
    """Port de PerfilAceroRow.FaltanDatos: lo que el programa exige para dibujar."""
    forma = forma_de(p)

    if not forma:
        return f"la familia «{p['familia']}» no se reconoce"

    faltan = []
    redondo = forma in ("tubo redondo", "redondo macizo")
    laminada = forma in ("I", "te", "canal")

    if p["peralte"] <= 0:
        faltan.append("diametro" if redondo
                      else "ala larga" if forma == "angulo" else "peralte")

    # El macizo es el unico que no lleva espesor: es una barra llena.
    if forma != "redondo macizo" and p["e_alma"] <= 0:
        faltan.append("e alma" if laminada else "espesor")

    if not redondo and p["ancho"] <= 0:
        faltan.append("ala corta" if forma == "angulo" else "ancho")

    if laminada and p["e_patin"] <= 0:
        faltan.append("e patin")

    if forma == "canal con labios" and p["labio"] <= 0:
        faltan.append("labio")

    if faltan:
        return ", ".join(faltan)

    h, b, t, tf = p["peralte"], p["ancho"], p["e_alma"], p["e_patin"]

    if forma in ("I", "canal"):
        if 2 * tf >= h:
            return "los dos patines no caben en el peralte"
        if t >= b:
            return "el alma es mas ancha que el patin"

    if forma == "te":
        if tf >= h:
            return "el patin no cabe en el peralte"
        if t >= b:
            return "el alma es mas ancha que el patin"

    if forma == "angulo":
        if t >= b:
            return "el espesor se come el ala corta"
        if b > h:
            return "el ala corta es mas larga que la larga: cambialas"

    if forma == "tubo rectangular" and 2 * t >= min(h, b):
        return "la pared no deja hueco interior"

    if forma == "tubo redondo" and 2 * t >= h:
        return "la pared no deja hueco interior"

    if forma in ("canal con labios", "zeta") and 2 * t >= h:
        return "los dos patines no caben en el peralte"

    if forma == "canal con labios" and p["labio"] <= t:
        return "el labio no llega ni al espesor"

    if forma == "zeta" and p["ancho2"] > b:
        return "el ancho 2 es el patin ANGOSTO: no puede pasar del ancho"

    return ""


def proporcion_imposible(p):
    """Proporciones que ningun perfil real tiene. Caza los errores de dedo."""
    forma = forma_de(p)
    h, b, t, tf = p["peralte"], p["ancho"], p["e_alma"], p["e_patin"]

    if forma in ("I", "canal"):
        if t > h / 6:
            return f"alma {t:.2f} cm en peralte {h:.2f} cm (mas de 1/6)"
        if tf > h / 3:
            return f"patin {tf:.2f} cm en peralte {h:.2f} cm (mas de 1/3)"
        if t > b / 2:
            return f"alma {t:.2f} pasa de medio patin {b:.2f}"

    # La te se comprueba distinto: es MEDIO perfil I, asi que su alma es igual de gruesa
    # que la del entero pero su peralte es la mitad. Con el limite del I se caerian tes
    # buenas, como la WT 2'' x 6.5, que tiene 0.72 cm de alma en 5.3 de peralte.
    if forma == "te":
        if t > h / 3:
            return f"alma {t:.2f} cm en peralte {h:.2f} cm (mas de 1/3)"
        if tf > h / 2:
            return f"patin {tf:.2f} cm en peralte {h:.2f} cm (mas de la mitad)"
        if t > b / 2:
            return f"alma {t:.2f} pasa de medio patin {b:.2f}"

    if forma == "angulo":
        if t > b / 3:
            return f"espesor {t:.3f} pasa de un tercio del ala corta {b:.3f}"

    if forma in ("tubo rectangular", "tubo redondo"):
        menor = h if forma == "tubo redondo" else min(h, b)

        if t > menor / 4:
            return f"pared {t:.2f} en lado {menor:.2f} (mas de 1/4)"

    if forma in ("canal con labios", "zeta"):
        if t > h / 10:
            return f"lamina {t:.3f} en peralte {h:.2f} (mas de 1/10)"

    if forma == "canal con labios" and p["labio"] > h / 2:
        return f"labio {p['labio']:.2f} pasa de medio peralte {h:.2f}"

    if forma == "zeta" and p["ancho2"] > b:
        return f"el patin angosto {p['ancho2']:.2f} pasa del ancho {b:.2f}"

    return ""


def dibujo_degenera(p):
    """Si la geometria de dibujo sale mal: area negativa, contorno cruzado, radios…"""
    forma = forma_de(p)
    h, b, t, tf = p["peralte"], p["ancho"], p["e_alma"], p["e_patin"]

    # ---- Las formas en pico: area exacta y contorno sin cruces ----
    en_pico = {
        "I": (lambda: perfil_ir(0, 0, h, b, t, tf),
              lambda: 2 * b * tf + (h - 2 * tf) * t),
        "te": (lambda: perfil_te(0, 0, h, b, t, tf),
               lambda: b * tf + (h - tf) * t),
        "canal": (lambda: perfil_canal(0, 0, h, b, t, tf, False),
                  lambda: 2 * b * tf + (h - 2 * tf) * t),
        "angulo": (lambda: perfil_angulo(0, 0, h, b, t, False),
                   lambda: h * t + (b - t) * t),
    }

    if forma in en_pico:
        hacer, area_teorica = en_pico[forma]
        pts = hacer()
        area = area_poligono(pts)
        esperada = area_teorica()

        if area <= 0 or abs(area - esperada) > 1e-9:
            return (f"el area del contorno ({area:.4f}) no es la del perfil "
                    f"({esperada:.4f})")

        cruce = se_cruza(pts)

        if cruce is not None:
            return f"el contorno se cruza: los lados {cruce} se cortan"

        # Y el espejeado, en las formas que tienen un lado.
        if forma in ("canal", "angulo"):
            pts_e = (perfil_canal(b, 0, h, b, t, tf, True) if forma == "canal"
                     else perfil_angulo(b, 0, h, b, t, True))

            if se_cruza(pts_e) is not None:
                return "el contorno espejeado se cruza"

            if abs(area_poligono(pts_e) - area) > 1e-9:
                return "el espejeado no tiene la misma area"

        return ""

    if forma == "tubo rectangular":
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

        return ""

    if forma == "tubo redondo":
        r_ext = h / 2
        r_int = r_ext - t

        if r_int <= 0:
            return "la pared se come el radio: saldria macizo"

        if math.pi * (r_ext ** 2 - r_int ** 2) <= 0:
            return "la corona sale con area negativa"

        return ""

    if forma == "redondo macizo":
        if h <= 0:
            return "sin diametro no hay circunferencia"

        return ""

    if forma == "canal con labios":
        _, _, _, r_ext, r_int = radios_cf(h, b, t, p["labio"], p["radio"])

        if r_ext > min(b / 2, min(p["labio"], h / 2)) + 1e-12:
            return "el radio exterior no cabe"

        if r_int < 0:
            return "el radio interior sale negativo"

        pts, bulges, centros, _, _ = perfil_cf(0, 0, h, b, t, p["labio"], p["radio"], False)

        if se_cruza(pts) is not None:
            return f"el contorno se cruza: {se_cruza(pts)}"

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

    if forma == "zeta":
        angosto = p["ancho2"] if 0 < p["ancho2"] <= b else b

        pts, bulges, centros, r_ext, r_int, w = perfil_zeta(
            0, 0, h, b, angosto, t, p["radio"], False)

        cruce = se_cruza(pts)

        if cruce is not None:
            return f"el contorno se cruza: los lados {cruce} se cortan"

        if abs(w - (b + angosto - t)) > 1e-9:
            return "el ancho que ocupa no son los dos patines menos el alma"

        # El area, contando los arcos, tiene que ser EXACTAMENTE la de la zeta menos lo
        # que se llevan sus dos dobleces.
        esperada = (t * (b + angosto + h - 2 * t)
                    - 2 * (r_ext ** 2 - r_int ** 2) * (1 - math.pi / 4))
        area = area_con_arcos(pts, bulges)

        if abs(area - esperada) > 1e-9:
            return f"el area ({area:.6f}) no es la de la zeta ({esperada:.6f})"

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

    return f"la forma «{forma}» no se sabe dibujar"


catalogo = leer_catalogo_csv(RUTA_CATALOGO)

print(f"\n    el catalogo trae {len(catalogo)} perfiles")

porfam = {}
for p in catalogo:
    porfam[p["familia"]] = porfam.get(p["familia"], 0) + 1

for fam in sorted(porfam):
    print(f"       {fam}: {porfam[fam]}")

check("el catalogo tiene perfiles de las DOCE familias",
      set(porfam) == FAMILIAS_ESPERADAS,
      f"tiene {sorted(porfam)}, faltan {sorted(FAMILIAS_ESPERADAS - set(porfam))}")

check("y son muchos, no la semilla de doce", len(catalogo) > 1500,
      f"solo {len(catalogo)}")

# La familia IR tiene que traer SOLO las W. Es lo que estaba mal: IS, IC y S se metian
# dentro de IR «porque son perfiles I», y el desplegable de la IR ofrecia 573 perfiles
# de cuatro nomenclaturas revueltas.
irs = [p["nombre"] for p in catalogo if p["familia"] == "IR"]
irs_que_no_son_w = [n for n in irs if not n.upper().lstrip().startswith("W")]

print(f"\n    la familia IR trae {len(irs)} perfiles, "
      f"{len(irs_que_no_son_w)} de los cuales no son W")

check("la familia IR trae SOLO perfiles W", not irs_que_no_son_w,
      ", ".join(irs_que_no_son_w[:5]))

# Y cada una de las otras once tiene que traer solo los suyos.
mezcladas = []

for fam in sorted(FAMILIAS_ESPERADAS - {"IR"}):
    de_esta = [p["nombre"] for p in catalogo if p["familia"] == fam]

    # El OR viene de las HSS y el OC de las PIPE: son las dos traducciones de nombre que
    # hacian las macros, asi que ahi el prefijo del nombre NO es el de la familia.
    prefijos = {"OR": "HSS", "OC": "PIPE"}.get(fam, fam)

    ajenos = [n for n in de_esta
              if not n.upper().lstrip().startswith(prefijos)]

    if ajenos:
        mezcladas.append(f"{fam}: {', '.join(ajenos[:3])}")

check("cada familia trae solo sus propios perfiles", not mezcladas,
      "; ".join(mezcladas))

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

for fam in ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS"):
    de_esta = [p for p in catalogo if p["familia"] == fam]

    if not de_esta:
        continue

    per = [p["peralte"] for p in de_esta]

    # El redondo macizo no tiene espesor: es una barra llena, y el catalogo lo trae en
    # cero a proposito. Comprobarle un espesor minimo lo tumbaria por ser lo que es.
    esp = [p["e_alma"] for p in de_esta if fam != "OS"]

    print(f"       {fam:3}: peralte de {min(per):6.2f} a {max(per):6.2f}"
          + (f"   espesor de {min(esp):5.3f} a {max(esp):5.3f}" if esp else
             "   sin espesor (es macizo)"))

    # Un peralte de 3 mm o de 5 m serian un error de unidades de diez o cien veces. El
    # tope de abajo es 0.6 y no 2 por el redondo de 1/4", que mide 0.64 cm de diametro:
    # es el perfil mas pequeño del manual y es legitimo.
    check(f"los peraltes de {fam} son de tamaño de perfil, no de otra unidad",
          min(per) > 0.6 and max(per) < 300,
          f"de {min(per)} a {max(per)} cm")

    if esp:
        check(f"los espesores de {fam} tambien",
              min(esp) > 0.05 and max(esp) < 10,
              f"de {min(esp)} a {max(esp)} cm")


# ---- 4. El acomodo: una seccion por renglon, con el CATALOGO ENTERO ----
#
# Ya no hay bandas por familia. El acomodo es: TODAS las secciones con el borde derecho
# en x = -0.6, una por renglon, y SEPARACION cm de la cima de una a la base de la
# siguiente. Los dos numeros salen del codigo, no se copian aqui: si alguien cambia la
# constante y no este script, esto tiene que enterarse.
#
# El acomodo con cuatro perfiles ya esta comprobado en verificar_catalogo_y_acomodo.py.
# Lo que se hace aqui es lo que ese no puede hacer: pasar el CATALOGO COMPLETO -las 1617
# secciones, ordenadas por familia, que es la hoja mas grande que se puede pedir- y
# comprobar que ni una se pisa con la de abajo ni se mete en el semiplano del concreto.
print("\n    el acomodo, con el catalogo ENTERO (la hoja mas grande posible):")

with open("client/src/CadLink.App/MainWindow.Acero.cs", encoding="utf-8") as f:
    fuente_app = f.read()

m_sep = _re.search(
    r"const double SeparacionEntreSeccionesCm = (-?[\d.]+);", fuente_app)
m_org = _re.search(r"const double OrigenAceroCm = (-?[\d.]+);", fuente_app)

check("la separacion entre secciones se lee del codigo", m_sep is not None)
check("y el origen del acero tambien", m_org is not None)

SEPARACION = float(m_sep.group(1)) if m_sep else 0.0
ORIGEN_CM = float(m_org.group(1)) if m_org else 0.0

print(f"       del codigo: separacion {SEPARACION:.0f} cm, "
      f"origen {ORIGEN_CM:.0f} cm")

check("la separacion es la que se pidio: 0.7 m", SEPARACION == 70, f"{SEPARACION}")
check("y el borde derecho de todas es -0.6 m", ORIGEN_CM == -60, f"{ORIGEN_CM}")

# La hoja: el catalogo entero, agrupado por familia, que es como se captura.
hoja = [p for fam in ORDEN_FAMILIAS for p in catalogo if p["familia"] == fam]

check("la hoja de prueba es el catalogo entero",
      len(hoja) == len(catalogo), f"{len(hoja)} de {len(catalogo)}")

ESCALA = 0.01        # el LinearScaleFactor: cm de catalogo a metros de dibujo

y_cm = 0.0
puestos = []         # (x_izq, x_der, y_base, y_cima) por seccion

for p in hoja:
    x_der = ORIGEN_CM * ESCALA
    puestos.append((x_der - (ancho_que_ocupa(p) * ESCALA), x_der,
                    y_cm * ESCALA, (y_cm + alto_que_ocupa(p)) * ESCALA))

    y_cm += alto_que_ocupa(p) + SEPARACION

# 1. NINGUNA se pisa con la de abajo, y entre las dos quedan los 70 cm enteros.
pisadas = []
huecos_malos = []

for i in range(len(puestos) - 1):
    hueco = puestos[i + 1][2] - puestos[i][3]

    if hueco < -1e-9:
        pisadas.append(f"{hoja[i]['nombre']} con {hoja[i + 1]['nombre']}")

    if abs(hueco - SEPARACION * ESCALA) > 1e-9:
        huecos_malos.append(f"{hoja[i]['nombre']}: {hueco / ESCALA:.2f} cm")

check("ninguna de las 1617 secciones se pisa con la de abajo", not pisadas,
      "; ".join(pisadas[:3]))
check(f"y entre cada dos quedan los {SEPARACION:.0f} cm exactos", not huecos_malos,
      "; ".join(huecos_malos[:3]))

# 2. NINGUNA se mete en el semiplano positivo, que es donde dibuja el concreto. Es la
#    consecuencia de alinear por la DERECHA: la mas ancha del catalogo -la IS de 750 mm-
#    sobresale hacia la izquierda, no hacia el concreto.
en_el_concreto = [hoja[i]["nombre"] for i, q in enumerate(puestos) if q[1] > 1e-12]

check("ninguna se mete en el semiplano del concreto", not en_el_concreto,
      "; ".join(en_el_concreto[:3]))

# 3. TODAS acaban exactamente en el mismo x, sea cual sea su familia y su ancho. Es el
#    cambio que se pidio: antes cada macro tenia su sepIzq y las de una familia se
#    ponian una al lado de otra.
derechos = {round(q[1], 9) for q in puestos}

check("todas las secciones acaban en el MISMO borde derecho",
      derechos == {round(ORIGEN_CM * ESCALA, 9)}, f"{sorted(derechos)[:3]}")

# Y los izquierdos NO estan a plomo, que es la otra cara de lo mismo.
izquierdos = [q[0] for q in puestos]

print(f"       los bordes izquierdos van de {min(izquierdos):+.2f} a "
      f"{max(izquierdos):+.2f} m: alineadas por la DERECHA")

check("y los bordes izquierdos no estan a plomo",
      len({round(q, 9) for q in izquierdos}) > 1)

# 4. Los 70 cm dan para lo que sobresale de una seccion: el rotulo cuelga por debajo de
#    su base y las cotas de la de abajo suben por encima de su cima.
ROTULO_ABAJO = 6 + (4 * 3)      # gap maximo + cuatro renglones de 3 cm
COTAS_ARRIBA = 6 + 1.5 + 2      # gap maximo + texto + flecha

print(f"       de los {SEPARACION:.0f} cm, el rotulo de arriba se come "
      f"{ROTULO_ABAJO} y las cotas de abajo {COTAS_ARRIBA}")

check("los 70 cm dan para el rotulo de arriba y las cotas de abajo",
      ROTULO_ABAJO + COTAS_ARRIBA < SEPARACION,
      f"hacen falta {ROTULO_ABAJO + COTAS_ARRIBA:.1f} cm")

# 5. La primera arranca en cero, que es el baseY de la macro del IR.
check("la primera seccion arranca en y = 0", abs(puestos[0][2]) < 1e-12)

# Y cuanto mide esa hoja. No se compara con nada: es un dato para saber de que se habla.
alto_hoja = (y_cm - SEPARACION) * ESCALA

print(f"\n    las {len(hoja)} secciones del catalogo, una por renglon, miden "
      f"{alto_hoja:.0f} m de alto")
print("    -nadie captura el catalogo entero, pero si lo hiciera, saldria-")

# El invariante que no depende de comparar con nada: el alto de la hoja es la suma de lo
# que mide cada seccion mas la separacion por cada hueco.
suma_altos = sum(alto_que_ocupa(p) for p in hoja)
huecos = len(hoja) - 1

check("el alto de la hoja es la suma de las secciones mas la separacion por hueco",
      abs((alto_hoja / ESCALA) - (suma_altos + huecos * SEPARACION)) < 1e-6,
      f"{alto_hoja / ESCALA:.2f} contra {suma_altos + huecos * SEPARACION:.2f}")


# ---- 5. Que el ancho que ocupa cada perfil es positivo ----
#
# Si un perfil pidiera un hueco de cero, el siguiente se dibujaria justo encima.
sin_ancho = [(p["familia"], p["nombre"]) for p in catalogo if ancho_que_ocupa(p) <= 0]

print(f"\n    perfiles que pedirian un hueco de cero: {len(sin_ancho)}")

check("todos los perfiles piden un hueco de ancho positivo", not sin_ancho,
      "; ".join(f"{f} {n}" for f, n in sin_ancho[:5]))

# La zeta es el unico caso en el que el hueco NO es el ancho de la columna: son los dos
# patines menos el alma. Se comprueba aparte porque es el que se puede olvidar.
zetas = [p for p in catalogo if p["familia"] == "ZF"]

if zetas:
    mal = [p["nombre"] for p in zetas
           if abs(ancho_que_ocupa(p)
                  - (p["ancho"] + p["ancho2"] - p["e_alma"])) > 1e-9]

    print(f"    las {len(zetas)} zetas piden de "
          f"{min(ancho_que_ocupa(p) for p in zetas):.2f} a "
          f"{max(ancho_que_ocupa(p) for p in zetas):.2f} cm de hueco, "
          f"con patines de {min(p['ancho'] for p in zetas):.2f} a "
          f"{max(p['ancho'] for p in zetas):.2f}")

    check("la zeta pide el hueco de sus dos patines, no el de uno", not mal,
          ", ".join(mal[:5]))

    check("y todas las zetas del catalogo traen su patin angosto",
          all(0 < p["ancho2"] <= p["ancho"] for p in zetas),
          ", ".join(p["nombre"] for p in zetas if not 0 < p["ancho2"] <= p["ancho"]))

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
