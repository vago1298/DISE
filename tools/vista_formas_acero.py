"""Dibuja las nueve formas de perfil de acero en un SVG, para poder MIRARLAS.

    python3 tools/vista_formas_acero.py > docs/formas-acero.svg

POR QUE ESTO EXISTE
-------------------
Las comprobaciones numericas de verificar_perfiles_acero.py dicen que la geometria se
sostiene: que el area es exacta, que el contorno no se cruza, que los arcos de cada
doblez son concentricos y tangentes. Eso caza los errores de calculo, y ya cazo uno
-el doblez interior de la zeta estaba al lado equivocado del alma-.

Lo que NO puede decir es si el perfil se PARECE a lo que tiene que parecer. Un area
correcta y un contorno limpio son compatibles con una te dibujada boca abajo. Para eso
hay que verlo, y para verlo no hace falta AutoCAD: basta un SVG, que GitHub muestra al
abrirlo.

DE DONDE SALE LA GEOMETRIA
--------------------------
No se copia: se IMPORTA de verificar_perfiles_acero.py, que ya tiene el port de las
nueve formas. Copiarla aqui seria tener dos versiones de los mismos vertices, y la
segunda copia es la que se queda vieja.

Importarlo tiene un efecto de lado que conviene: ese modulo comprueba al cargarse, asi
que si la geometria esta mal, ESTE script no llega a dibujar nada. La vista no puede
salir bonita con las formulas rotas.

EL RAYADO
---------
NO hay un color por familia: las doce van en la misma capa PERFILES, como en las macros,
y lo que distingue una de otra es su RAYADO. Cada forma lleva el de la macro que le
corresponde, y las cinco que no tenian macro toman el de la macro cuyo material
comparten. Aqui se dibujan con patrones SVG que imitan el ANSI31 y el ANSI32, y con los
colores ACI de verdad traducidos a RGB.
"""

import colorsys
import contextlib
import importlib.util
import io
import math
import os
import sys


def _cargar_geometria():
    """Carga verificar_perfiles_acero.py y devuelve su modulo, ya ejecutado.

    NO se usa 'import' a secas, y es por una razon concreta: ese archivo es un script y
    acaba con un raise SystemExit para dar su codigo de salida. Con un import normal, la
    excepcion aborta la importacion y el nombre del modulo NUNCA se enlaza, asi que el
    'except SystemExit' se cumple y despues no hay modulo del que sacar las funciones.

    Con module_from_spec el objeto existe ANTES de ejecutarlo, asi que cuando el
    SystemExit corta la ejecucion sus globales ya estan puestas y las funciones se pueden
    usar. Y la salida se calla: aqui interesan las formulas, no su informe.
    """
    ruta = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "verificar_perfiles_acero.py")

    spec = importlib.util.spec_from_file_location("verificar_perfiles_acero", ruta)
    modulo = importlib.util.module_from_spec(spec)

    try:
        with contextlib.redirect_stdout(io.StringIO()):
            spec.loader.exec_module(modulo)
    except SystemExit as e:
        # Si sus comprobaciones fallan, la vista no se dibuja: no tiene sentido
        # enseñar un perfil bonito con las formulas rotas.
        if e.code:
            print("La geometria no pasa sus propias comprobaciones, asi que no se "
                  "dibuja.", file=sys.stderr)
            print("Corre 'python3 tools/verificar_perfiles_acero.py' para ver que "
                  "falla.", file=sys.stderr)
            raise SystemExit(1) from e

    return modulo


g = _cargar_geometria()


# El rayado de cada forma, que es el de su macro. Tiene que decir lo mismo que
# SeccionDrawer.RayarPerfil; validar.py comprueba que los pares (patron, color) de aqui
# esten tambien alli.
#
#     (patron del relleno, color del relleno, patron de la trama, color de la trama)
#
# El relleno en None quiere decir que esa forma no se rellena: solo lleva la trama, como
# el IR.
RAYADO = {
    # Laminados: el de la macro del IR. Sin relleno, solo ANSI32 en 252.
    "I": (None, None, "ANSI32", 252),
    "te": (None, None, "ANSI32", 252),
    "canal": (None, None, "ANSI32", 252),
    "angulo": (None, None, "ANSI32", 252),

    # Formados en frio: el de la macro del CF. Fondo solido 4 y ANSI31 en 142.
    "canal con labios": ("SOLID", 4, "ANSI31", 142),
    "zeta": ("SOLID", 4, "ANSI31", 142),

    # Redondos: el de la macro del OC. Los dos en 162, asi que la trama NO SE VE: es un
    # defecto de la macro que se conserva a proposito, y esta apuntado en la
    # documentacion. Aqui se dibuja igual que saldra en AutoCAD.
    "tubo redondo": ("SOLID", 162, "ANSI31", 162),
    "redondo macizo": ("SOLID", 162, "ANSI31", 162),

    # Tubo rectangular: el de la macro del HSS. De 5 pulgadas para arriba, solido 141 y
    # ANSI31 en 144; por debajo, fondo cian 4 y ANSI31 en 142.
    "tubo rectangular": ("SOLID", 141, "ANSI31", 144),
    "tubo rectangular chico": ("SOLID", 4, "ANSI31", 142),
}


def aci_a_rgb(indice):
    """Un indice de color de AutoCAD a RGB.

    Del 10 en adelante la paleta va en 24 tonos de 15 grados con diez sombras cada uno:
    las PARES son saturadas y las IMPARES palidas, y el brillo baja por parejas. Es la
    regla con la que el 10 da rojo puro, el 11 rosa, el 12 rojo oscuro y el 18 casi
    negro.
    """
    fijos = {
        1: (255, 0, 0), 2: (255, 255, 0), 3: (0, 255, 0), 4: (0, 255, 255),
        5: (0, 0, 255), 6: (255, 0, 255), 7: (255, 255, 255), 8: (128, 128, 128),
        9: (192, 192, 192), 250: (51, 51, 51), 251: (91, 91, 91),
        252: (132, 132, 132), 253: (173, 173, 173), 254: (214, 214, 214),
        255: (255, 255, 255),
    }

    if indice in fijos:
        return fijos[indice]

    if not 10 <= indice <= 249:
        return (128, 128, 128)

    tono = (indice - 10) // 10
    sombra = (indice - 10) % 10

    brillo = [1.0, 1.0, 0.65, 0.65, 0.5, 0.5, 0.3, 0.3, 0.15, 0.15][sombra]
    saturacion = 1.0 if sombra % 2 == 0 else 0.5

    r, v, a = colorsys.hsv_to_rgb((tono * 15) / 360.0, saturacion, brillo)

    return (round(r * 255), round(v * 255), round(a * 255))


def hex_de(indice):
    r, v, a = aci_a_rgb(indice)
    return f"#{r:02x}{v:02x}{a:02x}"


def puntos_con_arcos(pts, bulges, por_arco=14):
    """Los vertices, con cada doblez sustituido por los puntos de su arco.

    Un SVG no tiene bulges, asi que los arcos se aproximan con tramos. Catorce por arco
    de 90 grados es de sobra para que se vea curvo a este tamaño.
    """
    salida = []
    n = len(pts)

    for i, p in enumerate(pts):
        salida.append(p)

        b = bulges.get(i)

        if not b or abs(b) < 1e-15:
            continue

        q = pts[(i + 1) % n]

        cuerda = math.hypot(q[0] - p[0], q[1] - p[1])

        if cuerda < 1e-15:
            continue

        barrido = 4 * math.atan(b)
        r = cuerda / (2 * math.sin(abs(barrido) / 2))

        # El centro: en la mediatriz de la cuerda, al lado que diga el signo del bulge.
        mx, my = (p[0] + q[0]) / 2, (p[1] + q[1]) / 2
        dx, dy = (q[0] - p[0]) / cuerda, (q[1] - p[1]) / cuerda
        alto = math.sqrt(max(r * r - (cuerda / 2) ** 2, 0.0))
        signo = 1.0 if barrido > 0 else -1.0

        cx = mx - signo * alto * dy
        cy = my + signo * alto * dx

        a0 = math.atan2(p[1] - cy, p[0] - cx)

        for k in range(1, por_arco):
            a = a0 + barrido * k / por_arco
            salida.append((cx + (r * math.cos(a)), cy + (r * math.sin(a))))

    return salida


# ---------------------------------------------------------------------------
#  Las nueve formas, con un perfil de verdad cada una
# ---------------------------------------------------------------------------
# Las medidas son del catalogo IMCA, en centimetros, y el nombre es su designacion.

FORMAS = [
    ("IR", "I", "W - 12'' x 30.04 lb/ft",
     lambda: (g.perfil_ir(0, 0, 31.3, 16.6, 0.67, 1.12), {}), None),
    ("IS", "I", "IS - 150 x 9.5 / 450 x 6.4 mm",
     lambda: (g.perfil_ir(0, 0, 46.9, 15.0, 0.64, 0.95), {}), None),
    ("WT", "te", "WT - 8'' x 13.0 lb/ft",
     lambda: (g.perfil_te(0, 0, 19.9, 14.0, 0.64, 0.88), {}), None),
    ("C", "canal", "C - 8'' x 12.0 lb/ft",
     lambda: (g.perfil_canal(0, 0, 20.3, 5.7, 0.56, 0.99, False), {}), None),
    ("CF", "canal con labios", 'CF - 6" x 2" x #14',
     lambda: g.perfil_cf(0, 0, 15.24, 5.08, 0.19, 1.52, 0.24, False)[:2], None),
    ("ZF", "zeta", 'ZF - 8" x 2 3/8" x #14',
     lambda: g.perfil_zeta(0, 0, 20.32, 6.03, 5.4, 0.19, 0.476, False)[:2], None),
    ("L", "angulo", "L - 3'' x 2'' x 1/4''",
     lambda: (g.perfil_angulo(0, 0, 7.62, 5.08, 0.635, False), {}), None),
    ("OR", "tubo rectangular", 'HSS - 6" x 1/4"',
     lambda: g.rectangulo_redondeado(0, 0, 15.2, 15.2, 0.64), ("hueco", 0.64, 0.32)),
    ("OC", "tubo redondo", "PIPE - 4.02 in x 0.19 in", None, ("tubo", 10.2, 0.48)),
    ("OS", "redondo macizo", 'OS - 3/4"', None, ("macizo", 1.91, 0)),
]


def patrones_svg():
    """Las definiciones de patron: uno por cada (patron, color) que se usa."""
    usados = set()

    for relleno_p, relleno_c, trama_p, trama_c in RAYADO.values():
        usados.add((trama_p, trama_c))

    defs = []

    for patron, color in sorted(usados):
        nombre = f"{patron}_{color}".lower()
        c = hex_de(color)

        # El ANSI31 es una familia de rayas a 45 grados; el ANSI32, dos rayas juntas por
        # cada hueco. Aqui se imitan con un patron de 8 px.
        if patron == "ANSI32":
            lineas = (f'<path d="M0,8 l8,-8 M-1,1 l2,-2 M7,9 l2,-2" '
                      f'stroke="{c}" stroke-width="0.9"/>'
                      f'<path d="M0,4 l4,-4 M4,8 l4,-4" '
                      f'stroke="{c}" stroke-width="0.9"/>')
        else:
            lineas = (f'<path d="M0,8 l8,-8 M-1,1 l2,-2 M7,9 l2,-2" '
                      f'stroke="{c}" stroke-width="0.9"/>')

        defs.append(f'<pattern id="{nombre}" width="8" height="8" '
                    f'patternUnits="userSpaceOnUse">{lineas}</pattern>')

    return defs


def main():
    ancho_casilla = 300
    alto_casilla = 340
    columnas = 5
    filas = (len(FORMAS) + columnas - 1) // columnas

    # Una sola escala para todos: asi se ve que un angulo de 3" es de verdad mas pequeño
    # que una IS de 47 cm, que es informacion.
    mas_alto = 46.9
    escala = (alto_casilla - 130) / mas_alto

    w = columnas * ancho_casilla
    h = filas * alto_casilla + 74

    out = []
    out.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
               f'viewBox="0 0 {w} {h}">')
    out.append("<defs>" + "".join(patrones_svg()) + "</defs>")
    out.append('<rect width="100%" height="100%" fill="#1c1c1c"/>')
    out.append('<style>'
               'text{font-family:"Bahnschrift SemiLight",Arial,sans-serif}'
               '.fam{font-size:19px;font-weight:600;fill:#e6e6e6}'
               '.forma{font-size:13px;fill:#c8c8c8}'
               '.perfil{font-size:12px;fill:#8f8f8f}'
               '.med{font-size:11px;fill:#707070}'
               '.tit{font-size:22px;fill:#e6e6e6;font-weight:600}'
               '.sub{font-size:13px;fill:#9a9a9a}'
               '</style>')

    out.append('<text class="tit" x="20" y="32">'
               'Las nueve formas de perfil de acero de CadLink</text>')
    out.append('<text class="sub" x="20" y="54">'
               'Todas en la capa PERFILES, como en las macros: lo que distingue una '
               'familia de otra es su RAYADO, no un color. Misma escala en las diez '
               'casillas, medidas del catalogo IMCA en cm.</text>')

    for i, (familia, forma, perfil, hacer, especial) in enumerate(FORMAS):
        col = i % columnas
        fil = i // columnas

        x0 = col * ancho_casilla
        y0 = 74 + (fil * alto_casilla)

        out.append(f'<g transform="translate({x0},{y0})">')
        out.append(f'<rect x="6" y="6" width="{ancho_casilla - 12}" '
                   f'height="{alto_casilla - 12}" rx="6" fill="#242424" '
                   f'stroke="#333" stroke-width="1"/>')

        out.append(f'<text class="fam" x="20" y="32">{familia}</text>')
        out.append(f'<text class="forma" x="20" y="52">{forma}</text>')
        out.append(f'<text class="perfil" x="20" y="70">'
                   f'{perfil.replace("&", "&amp;").replace("<", "&lt;")}</text>')

        cx_casilla = ancho_casilla / 2
        base = alto_casilla - 46

        # El rayado que le toca. En el tubo rectangular depende del peralte, con el corte
        # de las cinco pulgadas de su macro.
        clave = forma

        if forma == "tubo rectangular" and 15.2 / 2.54 < 4.99:
            clave = "tubo rectangular chico"

        relleno_p, relleno_c, trama_p, trama_c = RAYADO[clave]

        fondo = hex_de(relleno_c) if relleno_p else "none"
        trama = f'url(#{trama_p}_{trama_c}'.lower() + ")"
        borde = hex_de(trama_c)

        if especial and especial[0] in ("tubo", "macizo"):
            _, diam, pared = especial
            r = diam * escala / 2
            cy = base - r

            for pintura in (fondo, trama):
                if pintura == "none":
                    continue
                out.append(f'<circle cx="{cx_casilla:.2f}" cy="{cy:.2f}" r="{r:.2f}" '
                           f'fill="{pintura}" stroke="none"/>')

            out.append(f'<circle cx="{cx_casilla:.2f}" cy="{cy:.2f}" r="{r:.2f}" '
                       f'fill="none" stroke="{borde}" stroke-width="1.4"/>')

            if pared > 0:
                ri = r - (pared * escala)
                if ri > 0:
                    out.append(f'<circle cx="{cx_casilla:.2f}" cy="{cy:.2f}" '
                               f'r="{ri:.2f}" fill="#242424" stroke="{borde}" '
                               f'stroke-width="1.4"/>')

            medida = (f'D {diam} cm, pared {pared} cm' if pared > 0
                      else f'diametro {diam} cm')
        else:
            pts, bulges = hacer()
            trazo = puntos_con_arcos(pts, bulges)

            xs = [p[0] for p in trazo]
            ys = [p[1] for p in trazo]
            anchura = max(xs) - min(xs)
            altura = max(ys) - min(ys)

            dx = cx_casilla - (((min(xs) + max(xs)) / 2) * escala)

            d = " ".join(
                f'{"M" if k == 0 else "L"}{(p[0] * escala) + dx:.2f},'
                f'{base - (p[1] * escala):.2f}'
                for k, p in enumerate(trazo)) + " Z"

            for pintura in (fondo, trama):
                if pintura == "none":
                    continue
                out.append(f'<path d="{d}" fill="{pintura}" stroke="none"/>')

            # El grosor del contorno: solo las formas laminadas llevan el PEDIT de la
            # macro del IR, que es lo que las hace verse como acero.
            grueso = 2.4 if forma in ("I", "te", "canal", "angulo") else 1.4

            out.append(f'<path d="{d}" fill="none" stroke="{borde}" '
                       f'stroke-width="{grueso}" stroke-linejoin="round"/>')

            # El hueco del tubo rectangular, que es una isla del rayado.
            if especial and especial[0] == "hueco":
                _, t, ri = especial
                pi, bi = g.rectangulo_redondeado(t, t, 15.2 - t, 15.2 - t, ri)
                trazo_i = puntos_con_arcos(pi, bi)

                di = " ".join(
                    f'{"M" if k == 0 else "L"}{(p[0] * escala) + dx:.2f},'
                    f'{base - (p[1] * escala):.2f}'
                    for k, p in enumerate(trazo_i)) + " Z"

                out.append(f'<path d="{di}" fill="#242424" stroke="{borde}" '
                           f'stroke-width="1.4" stroke-linejoin="round"/>')

            medida = f'{altura:.2f} x {anchura:.2f} cm'

        out.append(f'<text class="med" x="20" y="{alto_casilla - 20}">'
                   f'{medida}   ·   rayado {trama_p} en {trama_c}'
                   + (f' sobre {relleno_p} en {relleno_c}' if relleno_p else '')
                   + '</text>')
        out.append('</g>')

    out.append('</svg>')

    print("\n".join(out))

    print(f"{len(FORMAS)} secciones dibujadas, {len({f[1] for f in FORMAS})} formas "
          "distintas.", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
