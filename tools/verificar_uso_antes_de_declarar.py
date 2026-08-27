#!/usr/bin/env python3
"""Busca locales usadas antes de declararlas (CS0841) en las fuentes de C#.

¿Por qué a mano y no con el compilador?

En Linux no está el ref pack de WPF, así que CadLink.App no se puede compilar
aquí. Se intentó pasarle Roslyn sin referencias, y NO sirve para este error: si
la declaración sale de una llamada que no se resuelve (out var de =
Varilla.TryDiametroCm(...) donde Varilla es un tipo de otro proyecto), el
compilador marca el tipo como erróneo y SUPRIME el análisis de flujo, así que
el CS0841 no aparece. Se comprobó re-inyectando el bug a propósito: Roslyn sin
referencias lo dejó pasar.

La regla de C# que se revisa aquí es concreta y no necesita tipos:

    una local vive desde su declaración hasta el final del bloque que la
    contiene, y usarla ANTES en ese mismo bloque es CS0841.

Así que por cada declaración se busca su bloque, y dentro de ese bloque se
buscan usos del nombre en una posición de texto anterior a la declaración.

Qué reconoce como declaración:
    var NOMBRE =            out var NOMBRE
    foreach (var NOMBRE     using var NOMBRE =

Lo que NO revisa, a propósito, para no inventar errores:
    declaraciones con tipo explícito (int x = 0), porque distinguir «int x = 0»
    de una llamada o de un campo pide un parser de verdad. Con var basta,
    porque el estilo del proyecto es var.

Uso:  python3 tools/verificar_uso_antes_de_declarar.py [ruta ...]
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent

# Las palabras que van pegadas a un nombre pero no lo declaran como local.
NO_ES_NOMBRE = {"var", "new", "return", "if", "while", "for", "foreach", "out"}


def sin_comentarios_ni_textos(src: str) -> str:
    """Cambia comentarios y literales por espacios, sin mover ninguna posición.

    Se conserva el largo exacto para que los índices sigan sirviendo contra el
    texto original, y los saltos de línea para poder contar renglones.
    """
    fuera = []
    i, n = 0, len(src)

    while i < n:
        c = src[i]
        dos = src[i : i + 2]

        if dos == "//":
            j = src.find("\n", i)
            j = n if j < 0 else j
            fuera.append(" " * (j - i))
            i = j

        elif dos == "/*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            fuera.append("".join(ch if ch == "\n" else " " for ch in src[i:j]))
            i = j

        elif dos in ('@"', '$"') or c == '"':
            # Cadena normal, interpolada, textual o las dos cosas.
            arranque = i
            textual = False
            while src[i] in "@$":
                textual = textual or src[i] == "@"
                i += 1
            i += 1  # la comilla que abre

            while i < n:
                if textual:
                    if src[i] == '"':
                        if src[i : i + 2] == '""':
                            i += 2
                            continue
                        i += 1
                        break
                    i += 1
                else:
                    if src[i] == "\\":
                        i += 2
                        continue
                    if src[i] == '"':
                        i += 1
                        break
                    if src[i] == "\n":
                        break
                    i += 1

            trozo = src[arranque:i]
            fuera.append("".join(ch if ch == "\n" else " " for ch in trozo))

        elif c == "'":
            arranque = i
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == "'":
                    i += 1
                    break
                i += 1
            fuera.append(" " * (i - arranque))

        else:
            fuera.append(c)
            i += 1

    return "".join(fuera)


def parejas_de_pares(limpio: str, abre: str, cierra: str) -> dict[int, int]:
    """Devuelve {posición del que abre: posición del que cierra}."""
    pila: list[int] = []
    pares: dict[int, int] = {}

    for i, c in enumerate(limpio):
        if c == abre:
            pila.append(i)
        elif c == cierra and pila:
            pares[pila.pop()] = i

    return pares


def el_mas_chico_que_contiene(
    pos: int, pares: dict[int, int]
) -> tuple[int, int] | None:
    """El par abre/cierra más chico que contiene pos, o None."""
    mejor = None

    for abre, cierra in pares.items():
        if abre < pos < cierra:
            if mejor is None or abre > mejor[0]:
                mejor = (abre, cierra)

    return mejor


# Los que abren su PROPIO ámbito con la declaración metida en los paréntesis:
# la i de un for vive nada más en ese for, no en el método.
CON_AMBITO_PROPIO = ("for", "foreach", "while", "switch", "fixed", "lock", "catch")


def ambito_de_la_declaracion(
    pos: int,
    limpio: str,
    llaves: dict[int, int],
    parentesis: dict[int, int],
) -> tuple[int, int] | None:
    """Hasta dónde vive la local declarada en pos.

    Son tres casos distintos y confundirlos es lo que llena esto de falsos
    positivos:

    1. Dentro de los paréntesis de un for/foreach/while/...: el ámbito es ESE
       for, no el bloque de afuera. Si no, la i de un for choca con la i del
       for siguiente y las dos son legales.

    2. En la lista de parámetros de una lambda: el ámbito es el cuerpo de la
       lambda.

    3. Cualquier otra cosa, incluido 'out var x' dentro de los argumentos de
       una llamada suelta: el ámbito es el bloque que la contiene.
    """
    paren = el_mas_chico_que_contiene(pos, parentesis)

    if paren is not None:
        abre_p, cierra_p = paren
        antes = limpio[:abre_p].rstrip()

        for clave in CON_AMBITO_PROPIO:
            if re.search(rf"\b{clave}\s*$", antes):
                arranque = len(antes) - len(clave)

                # El cuerpo: un bloque, o una sola sentencia hasta el ';'.
                resto = cierra_p + 1
                while resto < len(limpio) and limpio[resto].isspace():
                    resto += 1

                if resto < len(limpio) and limpio[resto] == "{":
                    fin = llaves.get(resto, len(limpio) - 1)
                else:
                    fin = limpio.find(";", cierra_p)
                    fin = len(limpio) - 1 if fin < 0 else fin

                return (arranque, fin)

        # ¿Lista de parámetros de una lambda?
        despues = limpio[cierra_p + 1 :]
        if re.match(r"\s*=>", despues):
            resto = cierra_p + 1
            while resto < len(limpio) and (limpio[resto].isspace() or limpio[resto] in "=>"):
                resto += 1

            if resto < len(limpio) and limpio[resto] == "{":
                fin = llaves.get(resto, len(limpio) - 1)
            else:
                fin = limpio.find(";", cierra_p)
                fin = len(limpio) - 1 if fin < 0 else fin

            return (abre_p, fin)

    bloque = el_mas_chico_que_contiene(pos, llaves)
    if bloque is None:
        return None

    abre, cierra = bloque

    # Un cuerpo de expresión '=> ...' también es un ámbito, aunque no lleve
    # llaves: en
    #
    #     .OrderByDescending(par => Varilla.TryDiametroCm(par.Key, out var cm) ? cm : 0)
    #
    # esa cm vive nada más en la lambda, así que no choca con la cm del método.
    # Se busca el '=>' más cercano hacia atrás cuya expresión SÍ llegue hasta
    # pos; si nomás es una lambda anterior ya cerrada, no cuenta, porque
    # recortar hasta ahí taparía usos de verdad.
    for f in reversed([m.start() for m in re.finditer(r"=>", limpio[abre:pos])]):
        f += abre
        fin = fin_del_cuerpo_de_expresion(f, limpio, llaves)

        if f < pos <= fin:
            return (f, min(fin, cierra))

    return bloque


def fin_del_cuerpo_de_expresion(f: int, limpio: str, llaves: dict[int, int]) -> int:
    """Hasta dónde llega el cuerpo que arranca en el '=>' de la posición f."""
    i = f + 2
    while i < len(limpio) and limpio[i].isspace():
        i += 1

    # Cuerpo con llaves: termina en la llave que cierra.
    if i < len(limpio) and limpio[i] == "{":
        return llaves.get(i, len(limpio) - 1)

    # Cuerpo de una sola expresión: termina en el ';' o la ',' de su mismo
    # nivel, o donde se cierre un paréntesis que venía de antes.
    hondo = 0
    while i < len(limpio):
        c = limpio[i]

        if c in "([{":
            hondo += 1
        elif c in ")]}":
            if hondo == 0:
                return i - 1
            hondo -= 1
        elif hondo == 0 and c in ";,":
            return i - 1

        i += 1

    return len(limpio) - 1


# 'var x =' / 'out var x' / 'foreach (var x' / 'using var x ='
DECLARA = re.compile(
    r"\b(?:out\s+var|using\s+var|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?=[=)\s])"
)


def revisar(ruta: Path) -> list[str]:
    src = ruta.read_text(encoding="utf-8", errors="replace")
    limpio = sin_comentarios_ni_textos(src)
    pares = parejas_de_pares(limpio, "{", "}")
    parens = parejas_de_pares(limpio, "(", ")")

    renglon_de = [0] * (len(src) + 1)
    r = 1
    for i, c in enumerate(src):
        renglon_de[i] = r
        if c == "\n":
            r += 1
    renglon_de[len(src)] = r

    fallas = []

    for m in DECLARA.finditer(limpio):
        nombre = m.group(1)
        if nombre in NO_ES_NOMBRE:
            continue

        pos_decl = m.start(1)
        ambito = ambito_de_la_declaracion(pos_decl, limpio, pares, parens)
        if ambito is None:
            continue

        abre, _cierra = ambito

        # Usos del nombre dentro del bloque, antes de la declaración.
        for u in re.finditer(rf"\b{re.escape(nombre)}\b", limpio[abre:pos_decl]):
            pos_uso = abre + u.start()

            antes = limpio[max(0, pos_uso - 40) : pos_uso]

            # Si el uso ES una declaración (de otra local con el mismo nombre en
            # un bloque anidado), no es CS0841.
            if re.search(r"\b(?:var|out\s+var|using\s+var)\s+$", antes):
                continue

            # Un nombre precedido de punto es un miembro, no la local.
            if antes.rstrip().endswith("."):
                continue

            # Después de un punto tampoco: obj.de
            if re.match(r"\s*\.", limpio[pos_uso + len(nombre) :]) and antes.rstrip().endswith("."):
                continue

            fallas.append(
                f"{ruta.relative_to(RAIZ)}:{renglon_de[pos_uso]}: "
                f"se usa '{nombre}' antes de declararla "
                f"(la declaración está en el renglón {renglon_de[pos_decl]}) "
                f"-> error CS0841"
            )
            break  # con el primer uso basta para el reporte

    return fallas


def main() -> int:
    objetivos = [Path(a) for a in sys.argv[1:]] or [
        RAIZ / "client" / "src" / "CadLink.App",
        RAIZ / "client" / "src" / "CadLink.Cad",
    ]

    fuentes: list[Path] = []
    for o in objetivos:
        if o.is_dir():
            fuentes += [
                p
                for p in sorted(o.rglob("*.cs"))
                if "obj" not in p.parts and "bin" not in p.parts
            ]
        elif o.is_file():
            fuentes.append(o)

    todas: list[str] = []
    for f in fuentes:
        todas += revisar(f)

    print(f"Revisadas {len(fuentes)} fuentes")
    print()

    if todas:
        print("USO ANTES DE DECLARAR:")
        print()
        for f in todas:
            print(f"  {f}")
        print()
        print("Esto rompe la compilación en Windows.")
        return 1

    print("Ninguna local se usa antes de declararla.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
