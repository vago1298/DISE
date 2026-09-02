#!/usr/bin/env python3
"""
Comprueba el BALANCE de llaves, parentesis y corchetes de archivos C#.

No es un compilador: es lo unico que se puede comprobar sin .NET, y detecta el fallo
que mas cuesta encontrar leyendo -una llave de mas o de menos en un archivo de tres mil
lineas-. Recorre el texto con una maquina de estados para no contar los parentesis que
viven dentro de una cadena, de un caracter, de un verbatim @"..." o de un comentario.
"""

import sys

PARES = {')': '(', ']': '[', '}': '{'}
ABREN = set(PARES.values())


def revisar(ruta):
    with open(ruta, encoding='utf-8') as f:
        s = f.read()

    pila = []
    i = 0
    n = len(s)
    linea = 1

    while i < n:
        c = s[i]

        if c == '\n':
            linea += 1
            i += 1
            continue

        # ---- comentarios ----
        if c == '/' and i + 1 < n and s[i + 1] == '/':
            while i < n and s[i] != '\n':
                i += 1
            continue

        if c == '/' and i + 1 < n and s[i + 1] == '*':
            i += 2
            while i + 1 < n and not (s[i] == '*' and s[i + 1] == '/'):
                if s[i] == '\n':
                    linea += 1
                i += 1
            i += 2
            continue

        # ---- verbatim string @"..."  ("" es la comilla escapada) ----
        if c == '@' and i + 1 < n and s[i + 1] == '"':
            i += 2
            while i < n:
                if s[i] == '"':
                    if i + 1 < n and s[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if s[i] == '\n':
                    linea += 1
                i += 1
            continue

        # ---- cadena normal ----
        if c == '"':
            i += 1
            while i < n and s[i] != '"':
                if s[i] == '\\':
                    i += 1
                i += 1
            i += 1
            continue

        # ---- caracter ----
        if c == "'":
            i += 1
            while i < n and s[i] != "'":
                if s[i] == '\\':
                    i += 1
                i += 1
            i += 1
            continue

        # ---- los signos ----
        if c in ABREN:
            pila.append((c, linea))
        elif c in PARES:
            if not pila:
                return f'sobra un «{c}» en la linea {linea}'
            abre, ln = pila.pop()
            if abre != PARES[c]:
                return (f'se cierra con «{c}» en la linea {linea} lo que se abrio '
                        f'con «{abre}» en la linea {ln}')

        i += 1

    if pila:
        abre, ln = pila[-1]
        return f'quedo sin cerrar el «{abre}» de la linea {ln}'

    return None


def main():
    malos = 0

    for ruta in sys.argv[1:]:
        fallo = revisar(ruta)

        if fallo is None:
            print(f'OK      {ruta}')
        else:
            print(f'FALLA   {ruta}: {fallo}')
            malos += 1

    print()
    print(f'{len(sys.argv) - 1} archivo(s), {malos} con fallos de balance.')

    return 1 if malos else 0


if __name__ == '__main__':
    sys.exit(main())
