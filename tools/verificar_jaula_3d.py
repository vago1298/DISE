#!/usr/bin/env python3
"""Sigue un estribo desde su recorrido hasta los cilindros que se le piden a AutoCAD,
y comprueba que NO se pierda ni un tramo.

Por que existe
--------------
El sintoma era: en el 3D de AutoCAD el estribo sale con el lado derecho y el
inferior, y le faltan el izquierdo y el superior. Y los contadores del dibujante
NO avisaban de nada, porque solo cuentan las piezas que no se pudieron CREAR;
un tramo que nunca llega a pedirse no lo cuenta nadie.

Esto porta la cadena entera --TrazoEstribo.Eje, EjeDeBarra.Limpio,
Simplificado, Curvas y Tramos-- y compara el largo del recorrido original con el
largo que suman los cilindros. Si no cuadra, dice DONDE se perdio.

No necesita .NET ni AutoCAD.
"""

import math

NADA = 1e-12


# ==========================================================================
#  TrazoEstribo.Eje  (solo el Cuerpo, que es donde estaba la duda)
# ==========================================================================
def _arco(puntos, cx, cy, r, a0, a1, tramos):
    if r <= 0:
        puntos.append((cx, cy))
        return

    cuantos = max(1, math.ceil(tramos * abs(a1 - a0) / (0.5 * math.pi)))

    for i in range(cuantos + 1):
        a = a0 + (a1 - a0) * i / cuantos
        puntos.append((cx + r * math.cos(a), cy + r * math.sin(a)))


def _limpiar(puntos):
    out = []
    for p in puntos:
        if out and abs(out[-1][0] - p[0]) < 1e-9 and abs(out[-1][1] - p[1]) < 1e-9:
            continue
        out.append(p)
    return out


def cuerpo_estribo(x1, y1, x2, y2, r_sup, r_inf, gancho, tramos=14):
    ancho, alto = x2 - x1, y2 - y1
    if ancho <= 0 or alto <= 0:
        return None, None

    r_max = min(ancho, alto) / 2
    r_sup = min(max(r_sup, 0), r_max)
    r_inf = min(max(r_inf, 0), r_max)
    tramos = max(1, tramos)

    cg_x, cg_y = x2 - r_sup, y2 - r_sup
    c = [(cg_x, y2)]

    # Arriba, hacia la izquierda
    c.append((x1 + r_sup, y2))
    _arco(c, x1 + r_sup, y2 - r_sup, r_sup, 0.5 * math.pi, math.pi, tramos)

    # Costado izquierdo, bajando
    c.append((x1, y1 + r_inf))
    _arco(c, x1 + r_inf, y1 + r_inf, r_inf, math.pi, 1.5 * math.pi, tramos)

    # Abajo, hacia la derecha
    c.append((x2 - r_inf, y1))
    _arco(c, x2 - r_inf, y1 + r_inf, r_inf, 1.5 * math.pi, 2 * math.pi, tramos)

    # Costado derecho, subiendo
    c.append((x2, cg_y))

    if gancho <= 0 or r_sup <= 0:
        _arco(c, cg_x, cg_y, r_sup, 0, 0.5 * math.pi, tramos)
        return _limpiar(c), True

    return _limpiar(c), False


# ==========================================================================
#  EjeDeBarra
# ==========================================================================
def limpio(eje):
    out = []
    for p in eje:
        if out and _dist(out[-1], p) <= NADA:
            continue
        out.append(p)
    return out


def _dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def _al_segmento(p, a, b):
    ab = [b[i] - a[i] for i in range(3)]
    ap = [p[i] - a[i] for i in range(3)]
    ab2 = sum(v * v for v in ab)

    if ab2 <= NADA:
        return _dist(p, a)

    t = sum(ap[i] * ab[i] for i in range(3)) / ab2
    t = max(0.0, min(1.0, t))
    q = [a[i] + ab[i] * t for i in range(3)]

    return _dist(p, tuple(q))


def simplificado(eje, tol):
    p = limpio(eje)
    if len(p) < 3 or tol <= 0:
        return p

    guardar = [False] * len(p)
    guardar[0] = guardar[-1] = True

    def partir(i, j):
        if j <= i + 1:
            return
        peor, d_peor = -1, 0.0
        for k in range(i + 1, j):
            d = _al_segmento(p[k], p[i], p[j])
            if d > d_peor:
                d_peor, peor = d, k
        if peor < 0 or d_peor <= tol:
            return
        guardar[peor] = True
        partir(i, peor)
        partir(peor, j)

    partir(0, len(p) - 1)

    return [p[i] for i in range(len(p)) if guardar[i]]


def _cruz(u, v):
    return [u[1] * v[2] - u[2] * v[1],
            u[2] * v[0] - u[0] * v[2],
            u[0] * v[1] - u[1] * v[0]]


def _por_tres_puntos(a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    w = _cruz(u, v)
    w2 = sum(x * x for x in w)

    if w2 <= 1e-24:
        return None

    u2 = sum(x * x for x in u)
    v2 = sum(x * x for x in v)

    # centro = a + (u2*(v x w) + v2*(w x u)) / (2*w2)
    vw = _cruz(v, w)
    wu = _cruz(w, u)
    centro = tuple(a[i] + (u2 * vw[i] + v2 * wu[i]) / (2 * w2) for i in range(3))
    radio = _dist(centro, a)
    n = math.sqrt(w2)
    normal = tuple(x / n for x in w)

    return centro, normal, radio


def _al_circulo(p, centro, normal, radio):
    d = [p[i] - centro[i] for i in range(3)]
    fuera = sum(d[i] * normal[i] for i in range(3))
    en_plano = [d[i] - fuera * normal[i] for i in range(3)]
    r = math.sqrt(sum(x * x for x in en_plano))

    return math.sqrt((r - radio) ** 2 + fuera * fuera)


def _angulo_entre(a, b, centro, normal):
    u = [a[i] - centro[i] for i in range(3)]
    v = [b[i] - centro[i] for i in range(3)]
    nu = math.sqrt(sum(x * x for x in u))
    nv = math.sqrt(sum(x * x for x in v))
    if nu <= NADA or nv <= NADA:
        return 0.0
    cos = max(-1.0, min(1.0, sum(u[i] * v[i] for i in range(3)) / (nu * nv)))
    return math.acos(cos)


def _es_recto(p, i, j, tol):
    for k in range(i + 1, j):
        if _al_segmento(p[k], p[i], p[j]) > tol:
            return False
    return True


def _ajustar_arco(p, i, tol):
    if i + 2 >= len(p):
        return None

    circulo = _por_tres_puntos(p[i], p[i + 1], p[i + 2])
    if circulo is None:
        return None

    centro, normal, radio = circulo
    hasta = i + 2

    while hasta + 1 < len(p) and _al_circulo(p[hasta + 1], centro, normal, radio) <= tol:
        hasta += 1

    if hasta < i + 3:
        return None

    barrido = sum(_angulo_entre(p[k], p[k + 1], centro, normal) for k in range(i, hasta))

    if barrido < math.pi / 180:
        return None

    return {"puntos": p[i:hasta + 1], "arco": True}


def curvas(eje, tol):
    p = limpio(eje)
    salida = []

    if len(p) < 2:
        return salida

    tol = tol if tol > 0 else NADA
    i = 0

    while i < len(p) - 1:
        hasta_recta = i + 1
        while hasta_recta + 1 < len(p) and _es_recto(p, i, hasta_recta + 1, tol):
            hasta_recta += 1

        arco = _ajustar_arco(p, i, tol)

        if arco is not None and len(arco["puntos"]) - 1 > hasta_recta - i:
            salida.append(arco)
            i += len(arco["puntos"]) - 1
            continue

        salida.append({"puntos": p[i:hasta_recta + 1], "arco": False})
        i = hasta_recta

    return salida


def cerrado(eje):
    return len(eje) > 2 and _dist(eje[0], eje[-1]) <= 1e-9


def tramos(eje, alargue=0.0):
    salida = []
    if len(eje) < 2:
        return salida

    cerr = cerrado(eje)
    ultimo = len(eje) - 1

    for i in range(1, ultimo + 1):
        a, b = eje[i - 1], eje[i]
        d = [b[k] - a[k] for k in range(3)]
        largo = math.sqrt(sum(x * x for x in d))

        if largo <= NADA:
            continue

        u = [x / largo for x in d]
        atras = alargue if (i - 1 > 0 or cerr) else 0
        delante = alargue if (i < ultimo or cerr) else 0

        salida.append((
            tuple(a[k] - u[k] * atras for k in range(3)),
            tuple(b[k] + u[k] * delante for k in range(3)),
        ))

    return salida


def largo_de(eje):
    return sum(_dist(eje[i - 1], eje[i]) for i in range(1, len(eje)))


# ==========================================================================
#  Jaula3dDrawer.Piezas  ->  los cilindros que se le piden a AutoCAD
# ==========================================================================
TOLERANCIA_EN_RADIOS = 0.01
TOLERANCIA_DE_RECONOCER = 0.005
LARGO_MINIMO = 1e-6


def cilindros_pedidos(eje, radio):
    """Devuelve la lista de cilindros (a, b) que Piezas() acabaria pidiendo."""
    trozos = curvas(eje, radio * TOLERANCIA_DE_RECONOCER)
    cerr = cerrado(eje)

    pedidos = []

    for idx, t in enumerate(trozos):
        atras = idx > 0 or cerr
        delante = idx < len(trozos) - 1 or cerr

        if not t["arco"]:
            # Rama recta: SOLO usa t.A y t.B, o sea el PRIMERO y el ULTIMO punto
            a, b = t["puntos"][0], t["puntos"][-1]

            tr = tramos([a, b], 0)
            if not tr:
                continue

            a, z = tr[0]
            largo = _dist(a, z)
            if largo < LARGO_MINIMO:
                continue

            u = [(z[k] - a[k]) / largo for k in range(3)]
            da = radio if atras else 0
            dd = radio if delante else 0

            pedidos.append((
                tuple(a[k] - u[k] * da for k in range(3)),
                tuple(z[k] + u[k] * dd for k in range(3)),
            ))
            continue

        # Rama arco: cadena de cilindros por SUS PUNTOS
        for a, z in tramos(t["puntos"], radio):
            if _dist(a, z) >= LARGO_MINIMO:
                pedidos.append((a, z))

    return trozos, pedidos


# ==========================================================================
#  Los casos
# ==========================================================================
CASOS = [
    # nombre, base cm, alto cm, rec cm, diam estribo cm, diam esquina cm, gancho cm
    ("Columna 30x60  est#3  esq#8  gancho 5", 30, 60, 4, 0.95, 2.54, 5),
    ("Trabe 25x50    est#3  esq#6  gancho 5", 25, 50, 4, 0.95, 1.90, 5),
    ("Castillo 15x15 est#2  esq#3  gancho 5", 15, 15, 2, 0.60, 0.95, 5),
    ("Columna 40x40  est#4  esq#8  sin gancho", 40, 40, 4, 1.27, 2.54, 0),
]


def revisar(nombre, base, alto, rec, de, dvar, gancho):
    medio = de / 2
    r = (de + dvar) / 2

    cuerpo, cerr = cuerpo_estribo(
        rec + medio, rec + medio,
        base - rec - medio, alto - rec - medio,
        r, r, gancho, tramos=14)

    if cuerpo is None:
        print(f"  {nombre}: el rectangulo no da para un estribo")
        return 1

    # A metros y a 3D, como hace Tubo()/ApuntarBarra: el estribo es plano
    eje = [(x / 100.0, 0.0, y / 100.0) for (x, y) in cuerpo]

    radio = (de / 2) / 100.0

    # Lo que hace Dibujar() antes de nada
    eje_s = simplificado(eje, radio * TOLERANCIA_EN_RADIOS)

    l_original = largo_de(eje)
    l_simplificado = largo_de(eje_s)

    trozos, pedidos = cilindros_pedidos(eje_s, radio)

    # El largo que cubren los cilindros, descontando el alargue de solape
    l_cubierto = 0.0
    for a, b in pedidos:
        l_cubierto += _dist(a, b)

    rectas = sum(1 for t in trozos if not t["arco"])
    arcos = sum(1 for t in trozos if t["arco"])

    # El largo que cubren los TROZOS, que es lo que de verdad se convierte en pieza
    l_trozos = 0.0
    for t in trozos:
        if t["arco"]:
            l_trozos += largo_de(t["puntos"])
        else:
            # OJO: la rama recta solo usa el primero y el ultimo punto
            l_trozos += _dist(t["puntos"][0], t["puntos"][-1])

    falta = l_simplificado - l_trozos

    print(f"\n  {nombre}")
    print(f"    cerrado                : {cerr}")
    print(f"    puntos                 : {len(eje)} -> {len(eje_s)} tras simplificar")
    print(f"    trozos                 : {rectas} recta(s), {arcos} arco(s)")
    print(f"    largo del recorrido    : {l_original * 100:8.3f} cm")
    print(f"    largo tras simplificar : {l_simplificado * 100:8.3f} cm")
    print(f"    largo de los trozos    : {l_trozos * 100:8.3f} cm")
    print(f"    cilindros pedidos      : {len(pedidos)}")

    problemas = 0

    # 1) Los trozos tienen que cubrir el recorrido. Se tolera 1 mm por el atajo de
    #    la cuerda en las rectas, que es legitimo.
    if abs(falta) > 0.001:
        print(f"    *** SE PIERDEN {falta * 100:.3f} cm del recorrido ***")
        problemas += 1

    # 2) Ningun trozo RECTO puede tener puntos intermedios que se salgan de la
    #    cuerda: la rama recta los IGNORA y dibuja solo A->B.
    for idx, t in enumerate(trozos):
        if t["arco"] or len(t["puntos"]) <= 2:
            continue

        peor = max(_al_segmento(q, t["puntos"][0], t["puntos"][-1])
                   for q in t["puntos"][1:-1])

        if peor > radio * 0.5:
            print(f"    *** el trozo recto #{idx} tiene {len(t['puntos'])} puntos y el peor "
                  f"se sale {peor * 1000:.2f} mm de la cuerda: se dibuja como una RECTA "
                  f"y se come la curva ***")
            problemas += 1

    # 3) No puede haber un salto entre el fin de un cilindro y el principio del
    #    siguiente: eso es un HUECO en la varilla.
    for k in range(1, len(pedidos)):
        fin = pedidos[k - 1][1]
        ini = pedidos[k][0]
        salto = _dist(fin, ini)

        # Con el solape, fin va MAS ALLA de ini, asi que la distancia no dice el
        # signo. Se mira si el principio del siguiente se aleja del fin del anterior
        # mas que el propio solape.
        if salto > 2.5 * radio + 0.0005:
            print(f"    *** hueco de {salto * 1000:.2f} mm entre el cilindro {k - 1} "
                  f"y el {k} ***")
            problemas += 1

    if problemas == 0:
        print("    OK: el recorrido se cubre entero, sin huecos")

    return problemas


def main():
    print("=" * 78)
    print("DEL RECORRIDO DEL ESTRIBO A LOS CILINDROS DE AUTOCAD")
    print("=" * 78)

    total = 0
    for caso in CASOS:
        total += revisar(*caso)

    print("\n" + "=" * 78)
    if total == 0:
        print("OK: ningun estribo pierde recorrido.")
    else:
        print(f"ATENCION: {total} problema(s). El estribo llega incompleto a AutoCAD.")
    print("=" * 78)

    return 0 if total == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
