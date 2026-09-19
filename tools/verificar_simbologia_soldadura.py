#!/usr/bin/env python3
"""
La simbologia de soldadura: proporciones, posiciones y que no se encime nada.

El dibujo de un simbolo de soldadura no es libre: la AWS A2.4 dice donde va cada
pieza, y cambiar de sitio una es cambiar lo que el simbolo SIGNIFICA. El circulo de
«todo alrededor» tiene que estar en el CODO -en medio de la linea de referencia no
quiere decir nada-, el triangulo del filete lleva el cateto vertical a la izquierda
-con la hipotenusa del otro lado se confunde con el bisel-, y un triangulo encima de
la linea no es lo mismo que debajo: uno es el otro lado de la junta y el otro el lado
de la flecha.

Nada de eso lo atrapa el compilador, y en pantalla los cinco simbolos se ven
«terminados» estando mal. De ahi este espejo: reimplementa en Python el reparto de
SimbolosSoldadura y comprueba la geometria que sale.

    python tools/verificar_simbologia_soldadura.py

Lo que NO se comprueba aqui: que las entidades se creen en AutoCAD -eso necesita
AutoCAD- ni los literales del dibujante, que van en validar.py.
"""

from __future__ import annotations

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

fallos: list[str] = []


def check(nombre: str, ok: bool, detalle: str = "") -> None:
    if ok:
        print(f"  OK    {nombre}")
        return

    print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
    fallos.append(f"{nombre}: {detalle}")


def leer(*partes: str) -> str:
    with open(os.path.join(RAIZ, *partes), encoding="utf-8-sig") as f:
        return f.read()


_CS = leer("client", "src", "CadLink.Cad", "SimbolosSoldadura.cs")
_DRW = leer("client", "src", "CadLink.Cad", "PlacaBaseDrawer.Simbologia.cs")
_APP = leer("client", "src", "CadLink.App", "MainWindow.Simbologia.cs")


def constante(nombre: str) -> float:
    """Lee una constante del C#, para que el espejo no invente sus propios numeros."""
    m = re.search(rf"const double {nombre} = ([0-9.]+);", _CS)

    if m is None:
        raise SystemExit(f"no se encontro la constante {nombre} en SimbolosSoldadura.cs")

    return float(m.group(1))


#  LAS PROPORCIONES SE LEEN DEL C#, no se copian. Copiadas, el espejo seguiria pasando
#  despues de cambiarlas y dejaria de comprobar nada.
TRAMO = constante("TramoFlecha")
LINEA = constante("LineaReferencia")
LADO = constante("LadoSimbolo")
DESDE_CODO = constante("SimboloDesdeCodo")
ASTA = constante("AstaBandera")
VUELO = constante("VueloBandera")
RADIO = constante("RadioCirculo")
LARGO_COLA = constante("LargoCola")
SEMI_COLA = constante("SemiAltoCola")
ALTURA_COLA = constante("AlturaCola")
NOMBRE_DESDE = constante("NombreDesdeLinea")
SEPARACION = constante("SeparacionRenglones")
TITULO_SOBRE = constante("TituloSobreElPrimero")

TIPOS = ["Filete", "TodoAlrededor", "DeCampo", "BiselSimple", "FileteAmbosLados"]


def triangulo(x, y_ref, lado, arriba):
    alto = lado if arriba else -lado
    return [x, y_ref, x, y_ref + alto, x + lado, y_ref]


def un_renglon(tipo, x, y, h):
    abiertas, cerradas, rellenas, textos = [], [], [], []
    circulo = None

    x_codo = x + TRAMO * h
    y_ref = y + TRAMO * h
    x_fin = x_codo + LINEA * h

    leader = [x, y, x_codo, y_ref, x_fin, y_ref]

    xs = x_codo + DESDE_CODO * h
    lado = LADO * h

    if tipo in ("Filete", "TodoAlrededor"):
        cerradas.append(triangulo(xs, y_ref, lado, True))
    elif tipo == "FileteAmbosLados":
        cerradas.append(triangulo(xs, y_ref, lado, True))
        cerradas.append(triangulo(xs, y_ref, lado, False))
    elif tipo == "DeCampo":
        abiertas.append([xs, y_ref, xs, y_ref + ASTA * h])
        rellenas.append([xs, y_ref + ASTA * h,
                         xs + VUELO * h, y_ref + (ASTA - 0.35) * h,
                         xs, y_ref + (ASTA - 0.7) * h])
        cerradas.append(triangulo(xs + VUELO * h + 0.5 * h, y_ref, lado, True))
    elif tipo == "BiselSimple":
        abiertas.append([xs, y_ref, xs + 0.85 * lado, y_ref + 1.25 * lado])
        textos.append(("45%%d", xs - 0.2 * h, y_ref - 1.15 * h, 0.72 * h, 11))

    if tipo == "TodoAlrededor":
        circulo = (x_codo, y_ref, RADIO * h)

    abiertas.append([x_fin + LARGO_COLA * h, y_ref + SEMI_COLA * h,
                     x_fin, y_ref,
                     x_fin + LARGO_COLA * h, y_ref - SEMI_COLA * h])

    textos.append(("TIPO", x_fin + (LARGO_COLA + 0.25) * h, y_ref, ALTURA_COLA * h, 9))
    textos.append((nombre_de(tipo), x_fin + NOMBRE_DESDE * h, y_ref, h, 9))

    return {"tipo": tipo, "leader": leader, "abiertas": abiertas, "cerradas": cerradas,
            "rellenas": rellenas, "circulo": circulo, "textos": textos,
            "x_codo": x_codo, "y_ref": y_ref, "x_fin": x_fin, "xs": xs}


def nombre_de(tipo):
    """El nombre tal como lo escribe el C#, leido de su propio switch."""
    m = re.search(rf'Tipo\.{tipo} => "([^"]+)"', _CS)
    return m.group(1) if m else ""


def legenda(x, y, h):
    renglones = []
    y_renglon = y - TITULO_SOBRE * h

    for t in TIPOS:
        renglones.append(un_renglon(t, x, y_renglon, h))
        y_renglon -= SEPARACION * h

    return renglones


def caja(pts):
    return (min(pts[0::2]), min(pts[1::2]), max(pts[0::2]), max(pts[1::2]))


print("=" * 78)
print("LA SIMBOLOGIA DE SOLDADURA")
print("=" * 78)

H = 1.6
RS = legenda(0.0, 0.0, H)

check("los cinco simbolos del cuadro, en orden",
      [r["tipo"] for r in RS] == TIPOS, f"{[r['tipo'] for r in RS]}")

#  ---- EL LEADER ----
#  Tres puntos: punta, codo y final de la linea de referencia. Y el tramo inclinado va a
#  45 grados, como en el estandar: los dos catetos iguales.
for r in RS:
    lx, ly, cx, cy, fx, fy = r["leader"]

    check(f"{r['tipo']}: el leader va a 45 grados y su linea de referencia es horizontal",
          abs((cx - lx) - (cy - ly)) < 1e-9 and abs(fy - cy) < 1e-9 and fx > cx,
          f"tramo ({cx - lx:.2f}, {cy - ly:.2f})")

#  ---- EL CIRCULO DE «TODO ALREDEDOR» VA EN EL CODO ----
#  Es lo que le da el significado. En medio de la linea no quiere decir nada.
todo = [r for r in RS if r["tipo"] == "TodoAlrededor"][0]
cir = todo["circulo"]

check("el circulo de «todo alrededor» esta EN EL CODO del leader",
      cir is not None
      and abs(cir[0] - todo["x_codo"]) < 1e-9
      and abs(cir[1] - todo["y_ref"]) < 1e-9,
      f"circulo en {cir[:2] if cir else None}, codo en "
      f"({todo['x_codo']:.2f}, {todo['y_ref']:.2f})")

check("y es el UNICO renglon que lo lleva",
      sum(1 for r in RS if r["circulo"] is not None) == 1)

#  ---- EL TRIANGULO DEL FILETE ----
#  Cateto vertical A LA IZQUIERDA y base sobre la linea de referencia: con la hipotenusa
#  del otro lado, el simbolo se confunde con el del bisel.
filete = [r for r in RS if r["tipo"] == "Filete"][0]
t0 = filete["cerradas"][0]

check("el triangulo del filete se apoya en la linea de referencia",
      abs(t0[1] - filete["y_ref"]) < 1e-9 and abs(t0[5] - filete["y_ref"]) < 1e-9)

check("y su cateto vertical va a la IZQUIERDA, con la hipotenusa bajando a la derecha",
      abs(t0[0] - t0[2]) < 1e-9 and t0[4] > t0[0] and t0[3] > t0[1],
      f"vertical en x={t0[0]:.2f} y base hasta x={t0[4]:.2f}")

check("el triangulo es rectangulo y de catetos iguales",
      abs((t0[3] - t0[1]) - (t0[4] - t0[2])) < 1e-9)

#  ---- ENCIMA O DEBAJO NO ES LO MISMO ----
#  Un filete de un solo lado lleva UN triangulo; el de los dos lados lleva uno de cada
#  lado de la linea. Si los dos renglones dibujaran lo mismo, el cuadro mentiria.
ambos = [r for r in RS if r["tipo"] == "FileteAmbosLados"][0]

check("el filete de un solo lado lleva UN triangulo, encima de la linea",
      len(filete["cerradas"]) == 1 and caja(t0)[3] > filete["y_ref"] + 1e-9)

arriba = [c for c in ambos["cerradas"] if caja(c)[3] > ambos["y_ref"] + 1e-9]
abajo = [c for c in ambos["cerradas"] if caja(c)[1] < ambos["y_ref"] - 1e-9]

check("y el de ambos lados lleva DOS, uno de cada lado",
      len(ambos["cerradas"]) == 2 and len(arriba) == 1 and len(abajo) == 1,
      f"{len(arriba)} arriba y {len(abajo)} abajo")

check("los dos arrancan del mismo punto de la linea",
      abs(ambos["cerradas"][0][0] - ambos["cerradas"][1][0]) < 1e-9)

#  ---- LA BANDERA DE LA SOLDADURA DE CAMPO ----
#  Asta vertical y triangulo RELLENO: es lo que distingue de un vistazo lo que se suelda
#  en obra de lo que llega soldado de taller.
campo = [r for r in RS if r["tipo"] == "DeCampo"][0]
asta = campo["abiertas"][0]

check("la bandera de campo lleva asta vertical, desde la linea de referencia",
      abs(asta[0] - asta[2]) < 1e-9
      and abs(asta[1] - campo["y_ref"]) < 1e-9
      and asta[3] > asta[1])

check("y su bandera va RELLENA, colgada de la punta del asta",
      len(campo["rellenas"]) == 1
      and abs(campo["rellenas"][0][1] - asta[3]) < 1e-9
      and campo["rellenas"][0][2] > asta[0],
      f"{len(campo['rellenas'])} relleno(s)")

check("la de campo TAMBIEN lleva su filete: la bandera sola no es un simbolo completo",
      len(campo["cerradas"]) == 1)

check("y no se encima con la bandera",
      caja(campo["cerradas"][0])[0] > caja(campo["rellenas"][0])[2] - 1e-9,
      f"filete arranca en {caja(campo['cerradas'][0])[0]:.2f}, "
      f"bandera acaba en {caja(campo['rellenas'][0])[2]:.2f}")

check("la bandera es el UNICO relleno del cuadro",
      sum(len(r["rellenas"]) for r in RS) == 1)

#  ---- EL BISEL Y SU ANGULO ----
bisel = [r for r in RS if r["tipo"] == "BiselSimple"][0]
inclinada = bisel["abiertas"][0]

check("el bisel es una linea inclinada que sube desde la linea de referencia",
      abs(inclinada[1] - bisel["y_ref"]) < 1e-9
      and inclinada[2] > inclinada[0] and inclinada[3] > inclinada[1])

angulo = [t for t in bisel["textos"] if t[0].startswith("45")]

check("y su angulo se acota DEBAJO de la linea, fuera del simbolo",
      len(angulo) == 1 and angulo[0][2] < bisel["y_ref"] - 1e-9,
      f"{len(angulo)} texto(s) de angulo")

check("el angulo se escribe con el codigo de grado de AutoCAD",
      len(angulo) == 1 and "%%d" in angulo[0][0], f"{angulo[0][0] if angulo else ''}")

#  ---- LA COLA, CON EL PROCESO ----
#  Se abre hacia la derecha desde el final de la linea, y el texto va dentro: sin cola no
#  hay donde escribir el electrodo, que es la mitad de la informacion de una soldadura.
for r in RS:
    cola = r["abiertas"][-1]

    check(f"{r['tipo']}: la cola arranca en el final de la linea y se abre a la derecha",
          abs(cola[2] - r["x_fin"]) < 1e-9 and abs(cola[3] - r["y_ref"]) < 1e-9
          and cola[0] > cola[2] and cola[4] > cola[2]
          and cola[1] > cola[3] > cola[5])

    proceso = [t for t in r["textos"] if t[0] == "TIPO"]

    check(f"{r['tipo']}: y su texto va dentro de la cola",
          len(proceso) == 1 and proceso[0][1] > r["x_fin"] + 1e-9,
          f"{len(proceso)} texto(s)")

#  ---- LOS NOMBRES ----
check("cada renglon dice que significa su simbolo",
      all(any(t[0] == nombre_de(r["tipo"]) for t in r["textos"]) for r in RS)
      and all(len(nombre_de(t)) > 10 for t in TIPOS))

check("y los cinco nombres son distintos",
      len({nombre_de(t) for t in TIPOS}) == 5)

#  ---- LOS RENGLONES NO SE ENCIMAN ----
#  Con dos simbolos superpuestos el cuadro es ilegible, y es lo que pasa si la separacion
#  se queda corta contra lo que sube la bandera -que es la pieza mas alta del cuadro-.
def alto_de(r):
    ys = [r["leader"][1], r["leader"][3]]

    for grupo in (r["abiertas"], r["cerradas"], r["rellenas"]):
        for pts in grupo:
            ys += list(pts[1::2])

    if r["circulo"]:
        ys += [r["circulo"][1] - r["circulo"][2], r["circulo"][1] + r["circulo"][2]]

    return min(ys), max(ys)


for i in range(len(RS) - 1):
    _, arriba_de_abajo = alto_de(RS[i + 1])
    abajo_de_arriba, _ = alto_de(RS[i])

    check(f"el renglon {i + 2} no se encima con el {i + 1}",
          arriba_de_abajo < abajo_de_arriba - 1e-9,
          f"el de abajo llega a {arriba_de_abajo:.2f} y el de arriba baja a "
          f"{abajo_de_arriba:.2f}")

#  Y el titulo queda por encima de todo, no encima del primer simbolo.
_, techo = alto_de(RS[0])

check("el titulo queda por encima del primer simbolo",
      0.0 > techo - 1e-9, f"titulo en 0.00 y el primer simbolo sube a {techo:.2f}")

#  ---- TODO SE MIDE EN ALTURAS DE TEXTO ----
#  Es lo que hace que el cuadro se vea igual a 1:10 y a 1:25. Si alguna medida se
#  escribiera en centimetros, al doble de altura el simbolo saldria descuadrado.
dobles = legenda(0.0, 0.0, 2 * H)

check("con el doble de altura, el cuadro sale al doble de todo",
      all(abs(b["leader"][i] - 2 * a["leader"][i]) < 1e-9
          for a, b in zip(RS, dobles) for i in range(6)))

check("y no hay ninguna medida en centimetros escrita a mano",
      not re.search(r"=\s*[0-9.]+\s*;\s*//\s*cm", _CS)
      and "escala" not in _CS.split("public static Legenda Construir")[1][:600])

#  ---- EL DIBUJANTE Y LA PREVIA USAN ESTA MISMA GEOMETRIA ----
check("el dibujante saca el reparto de SimbolosSoldadura, no lo recalcula",
      "SimbolosSoldadura.Construir(x, y, h, titulo)" in _DRW
      and "Flecha(r.Leader[0], r.Leader[1], r.Leader[2], r.Leader[3]);" in _DRW
      and "_ms.AddSolid(" in _DRW)

check("y lo agrupa en un bloque propio",
      'NombreBloqueSimbologia = "SIMBOLOGIA DE SOLDADURA";' in _DRW
      and "UltimoBloque = Bloquear(NombreBloqueSimbologia, inicio, fin, x, y);" in _DRW)

check("todo va en la capa ROTULOS, que es la que se imprime en negro",
      _DRW.count("PlacaBaseCapas.Rotulos") >= 5
      and "PlacaBaseCapas.Soldadura" not in _DRW)

check("la vista previa pinta la MISMA simbologia que se va a dibujar",
      "SimbolosSoldadura.Construir(0, 0, h, TituloDeLaSimbologia)" in _APP
      and "PuntaDeFlecha(" in _APP)

check("y el boton la manda al origen, para poder arrastrar el bloque a su sitio",
      "DibujarSimbologiaSoldadura(0, 0, TituloDeLaSimbologia)" in _APP)

#  El anclaje del TEXT y el del MTEXT numeran distinto LO MISMO: 9/10/11 contra 4/5/6. Como
#  la simbologia se dibuja con TEXT, tienen que ser los del TEXT; con los del MTEXT nada
#  falla y los textos salen descolocados, que es el error que no se ve venir.
#
#  Se leen del ultimo argumento de cada TextoDwg que se construye en el C#.
usados = set(re.findall(r"new TextoDwg\([^;]*?,\s*(\d+)\)", _CS, re.S))

check("se leyeron los anclajes de los textos", len(usados) > 0, f"{sorted(usados)}")

check("los anclajes son los del TEXT -9, 10 u 11-, no los del MTEXT",
      usados and all(a in {"9", "10", "11"} for a in usados),
      f"anclajes usados: {sorted(usados)}")

#  Y el dibujante los pasa al Alignment del TEXT, no al AttachmentPoint de un MTEXT.
check("y el dibujante los pone en el Alignment de un TEXT",
      "_ms.AddText(t.S, Punto(t.X, t.Y), t.Altura);" in _DRW
      and "txt.Alignment = t.Anclaje;" in _DRW
      and "txt.TextAlignmentPoint = Punto(t.X, t.Y);" in _DRW)

print()
print("=" * 78)

if fallos:
    print(f"FALLARON {len(fallos)} COMPROBACIONES:")

    for f in fallos:
        print(f"  - {f}")

    print("=" * 78)
    sys.exit(1)

print("OK: la simbologia dice lo que tiene que decir.")
print("=" * 78)
