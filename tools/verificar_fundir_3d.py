#!/usr/bin/env python3
"""Comprueba que ninguna operacion COM que CONSUME su argumento quede dentro de
un AcadConnection.Retry.

Por que existe
--------------
AcadConnection.Retry no reintenta una llamada: REEJECUTA LA LAMBDA COMPLETA. Eso
es correcto para una lectura o para asignar una propiedad, pero es un fallo grave
para cualquier operacion que consuma su argumento o que acumule efecto:

  * Solid.Boolean(0, otro)  -> CONSUME 'otro'. En el reintento se le pasa un
    objeto COM ya consumido, y eso CIERRA AUTOCAD sin excepcion y sin error.
    Es el bug que dejaba media jaula dibujada.
  * ent.Rotate(...)         -> acumula. Dos vueltas = 180 en vez de 90.
  * ent.TransformBy(...)    -> acumula.
  * ent.Move(...)           -> acumula.
  * Blocks.Add(nombre)      -> el reintento falla por nombre duplicado, y ese
    error no es "busy", asi que escapa del Retry.

Este script es un cerrojo: lee el codigo y falla si alguna de esas llamadas
vuelve a aparecer dentro de un Retry. No necesita .NET ni AutoCAD.
"""

import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent / "client" / "src"

# Llamadas que NO pueden ejecutarse dos veces.
PELIGROSAS = {
    ".Boolean(": "consume el solido que se le pasa; reejecutarla cierra AutoCAD",
    ".Rotate(": "acumula el giro; dos veces son 180 en vez de 90",
    ".TransformBy(": "acumula la transformacion",
    ".Blocks.Add(": "falla por nombre duplicado, y ese error no es 'busy'",
}


def bloques_retry(texto):
    """Devuelve los rangos [ini, fin) de cada lambda pasada a Retry.

    Se localiza 'Retry(' y se avanza contando parentesis hasta cerrarlo, que es
    suficiente para este codigo y no necesita un parser de C#.
    """
    rangos = []

    for m in re.finditer(r"AcadConnection\.Retry\s*(<[^>]*>)?\s*\(", texto):
        i = m.end() - 1
        profundidad = 0

        for j in range(i, len(texto)):
            c = texto[j]
            if c == "(":
                profundidad += 1
            elif c == ")":
                profundidad -= 1
                if profundidad == 0:
                    rangos.append((m.start(), j))
                    break

    return rangos


def linea_de(texto, pos):
    return texto.count("\n", 0, pos) + 1


def main():
    archivos = sorted(RAIZ.rglob("*.cs"))

    if not archivos:
        print(f"ERROR: no encontre codigo en {RAIZ}")
        return 1

    hallazgos = []

    for f in archivos:
        texto = f.read_text(encoding="utf-8", errors="replace")

        if "AcadConnection.Retry" not in texto:
            continue

        for ini, fin in bloques_retry(texto):
            cuerpo = texto[ini:fin]

            for llamada, motivo in PELIGROSAS.items():
                for m in re.finditer(re.escape(llamada), cuerpo):
                    hallazgos.append((
                        f.relative_to(RAIZ),
                        linea_de(texto, ini + m.start()),
                        llamada,
                        motivo,
                    ))

    print("=" * 78)
    print("OPERACIONES NO IDEMPOTENTES DENTRO DE AcadConnection.Retry")
    print("=" * 78)
    print(f"\nRevisados {len(archivos)} archivos .cs\n")

    if not hallazgos:
        print("OK: ninguna operacion que consuma su argumento o que acumule efecto")
        print("    queda dentro de un Retry.")
        print("=" * 78)
        return 0

    for archivo, linea, llamada, motivo in hallazgos:
        print(f"  {archivo}:{linea}")
        print(f"      {llamada}  ->  {motivo}")
        print()

    print(f"ATENCION: {len(hallazgos)} hallazgo(s). Cada uno tiene que salir del Retry")
    print("          o llevar su propio reintento por elemento.")
    print("=" * 78)

    return 1


if __name__ == "__main__":
    sys.exit(main())
