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

LOS COLORES
-----------
Son los de verdad, leidos de FormaAcero.cs y traducidos de indice ACI a RGB con la
regla del propio AutoCAD: los indices del 10 en adelante van en 24 tonos de 15 grados y
diez sombras cada uno, las pares saturadas y las impares palidas.
"""

import colorsys
import contextlib
import importlib.util
import io
import math
import os
import re
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


RUTA_COLORES = "client/src/CadLink.Cad/FormaAcero.cs"


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


def leer_colores():
    """La tabla de color por familia, leida de FormaAcero.cs."""
    with open(RUTA_COLORES, encoding="utf-8") as f:
        fuente = f.read()

    return {m.group(1): int(m.group(2))
            for m in re.finditer(r'"(\w+)" => (\d+),', fuente)}


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

def forma_i():
    return g.perfil_ir(0, 0, 31.3, 16.6, 0.67, 1.12), {}


def forma_te():
    return g.perfil_te(0, 0, 19.9, 14.0, 0.64, 0.88), {}


def forma_canal():
    return g.perfil_canal(0, 0, 20.3, 5.7, 0.56, 0.99, False), {}


def forma_angulo():
    return g.perfil_angulo(0, 0, 7.62, 5.08, 0.635, False), {}


def forma_cf():
    pts, bulges, _, _, _ = g.perfil_cf(0, 0, 15.24, 5.08, 0.19, 1.52, 0.24, False)
    return pts, bulges


def forma_zeta():
    pts, bulges, _, _, _, _ = g.perfil_zeta(0, 0, 20.32, 6.03, 5.4, 0.19, 0.476, False)
    return pts, bulges


def forma_or():
    pts, bulges = g.rectangulo_redondeado(0, 0, 15.2, 15.2, 0.64)
    return pts, bulges


FORMAS = [
    ("IR", "perfil I", "W - 12'' x 30.04 lb/ft", forma_i, None),
    ("IS", "perfil I", "IS - 150 x 9.5 / 450 x 6.4 mm",
     lambda: (g.perfil_ir(0, 0, 46.9, 15.0, 0.64, 0.95), {}), None),
    ("WT", "te", "WT - 8'' x 13.0 lb/ft", forma_te, None),
    ("C", "canal laminada", "C - 8'' x 12.0 lb/ft", forma_canal, None),
    ("CF", "canal con labios", 'CF - 6" x 2" x #14', forma_cf, None),
    ("ZF", "zeta", 'ZF - 8" x 2 3/8" x #14', forma_zeta, None),
    ("L", "angulo", "L - 3'' x 2'' x 1/4''", forma_angulo, None),
    ("OR", "tubo rectangular", 'HSS - 6" x 1/4"', forma_or, None),
    ("OC", "tubo redondo", "PIPE - 4.02 in x 0.19 in", None, ("tubo", 10.2, 0.48)),
    ("OS", "redondo macizo", 'OS - 3/4"', None, ("macizo", 1.91, 0)),
]


def main():
    colores = leer_colores()

    # Cada forma en su casilla, todas del mismo tamaño y a la MISMA escala: asi se ve
    # que un angulo de 3" es de verdad mas pequeño que una IS de 47 cm, que es
    # informacion. Lo que se centra es el perfil dentro de su casilla.
    ancho_casilla = 300
    alto_casilla = 330
    columnas = 5
    filas = (len(FORMAS) + columnas - 1) // columnas

    # Una sola escala para todos, la que hace que el mas alto quepa.
    mas_alto = 46.9
    escala = (alto_casilla - 120) / mas_alto

    w = columnas * ancho_casilla
    h = filas * alto_casilla + 70

    out = []
    out.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
               f'viewBox="0 0 {w} {h}">')
    out.append('<rect width="100%" height="100%" fill="#1c1c1c"/>')
    out.append('<style>'
               'text{font-family:"Bahnschrift SemiLight",Arial,sans-serif}'
               '.fam{font-size:19px;font-weight:600}'
               '.forma{font-size:13px;fill:#c8c8c8}'
               '.perfil{font-size:12px;fill:#8f8f8f}'
               '.med{font-size:11px;fill:#707070}'
               '.tit{font-size:22px;fill:#e6e6e6;font-weight:600}'
               '.sub{font-size:13px;fill:#9a9a9a}'
               '</style>')

    out.append(f'<text class="tit" x="20" y="32">'
               f'Las nueve formas de perfil de acero de CadLink</text>')
    out.append('<text class="sub" x="20" y="54">'
               'Doce familias, nueve formas y un color por familia. Todas a la misma '
               'escala: el tamaño relativo es real. Medidas del catalogo IMCA, en cm.'
               '</text>')

    for i, (familia, forma, perfil, hacer, especial) in enumerate(FORMAS):
        col = i % columnas
        fil = i // columnas

        x0 = col * ancho_casilla
        y0 = 70 + (fil * alto_casilla)

        aci = colores.get(familia, 7)
        linea = hex_de(aci)
        relleno = hex_de(aci + 6)
        palido = hex_de(aci + 1)

        out.append(f'<g transform="translate({x0},{y0})">')
        out.append(f'<rect x="6" y="6" width="{ancho_casilla - 12}" '
                   f'height="{alto_casilla - 12}" rx="6" fill="#242424" '
                   f'stroke="#333" stroke-width="1"/>')

        out.append(f'<text class="fam" x="20" y="32" fill="{linea}">{familia}</text>')
        out.append(f'<text class="forma" x="20" y="52">{forma}</text>')
        out.append(f'<text class="perfil" x="20" y="70">'
                   f'{perfil.replace("&", "&amp;").replace("<", "&lt;")}</text>')

        cx_casilla = ancho_casilla / 2
        base = alto_casilla - 40

        if especial:
            que, diam, pared = especial
            r = diam * escala / 2
            cy = base - r

            # Los perfiles chicos NO se rellenan de macizo: el corte de las cinco
            # pulgadas de la macro del HSS, que aqui vale para las nueve formas.
            solido = diam / 2.54 >= 4.99
            fondo = relleno if solido else palido

            out.append(f'<circle cx="{cx_casilla:.2f}" cy="{cy:.2f}" r="{r:.2f}" '
                       f'fill="{fondo}" stroke="{linea}" stroke-width="1.6"/>')

            if que == "tubo" and pared > 0:
                ri = r - (pared * escala)
                if ri > 0:
                    out.append(f'<circle cx="{cx_casilla:.2f}" cy="{cy:.2f}" '
                               f'r="{ri:.2f}" fill="#242424" stroke="{linea}" '
                               f'stroke-width="1.6"/>')

            medida = (f'D {diam} cm, pared {pared} cm' if que == "tubo"
                      else f'diametro {diam} cm')
        else:
            pts, bulges = hacer()
            trazo = puntos_con_arcos(pts, bulges)

            xs = [p[0] for p in trazo]
            ys = [p[1] for p in trazo]
            anchura = max(xs) - min(xs)
            altura = max(ys) - min(ys)

            dx = cx_casilla - (((min(xs) + max(xs)) / 2) * escala)

            solido = altura / 2.54 >= 4.99
            fondo = relleno if solido else palido

            d = " ".join(
                f'{"M" if k == 0 else "L"}{(p[0] * escala) + dx:.2f},'
                f'{base - (p[1] * escala):.2f}'
                for k, p in enumerate(trazo)) + " Z"

            out.append(f'<path d="{d}" fill="{fondo}" stroke="{linea}" '
                       f'stroke-width="1.6" stroke-linejoin="round"/>')

            medida = f'{altura:.2f} x {anchura:.2f} cm'

        out.append(f'<text class="med" x="20" y="{alto_casilla - 18}">{medida}</text>')
        out.append('</g>')

    out.append('</svg>')

    print("\n".join(out))

    print(f"{len(FORMAS)} secciones dibujadas, {len({f[1] for f in FORMAS})} formas "
          "distintas.", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
