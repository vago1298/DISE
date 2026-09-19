#!/usr/bin/env python3
"""
Ningun StaticResource se usa ANTES de declararse.

Esto no es un detalle de estilo: es lo que tira la aplicacion al arrancar. WPF
construye un ResourceDictionary de arriba abajo, asi que un {StaticResource X}
que aparece antes del <... x:Key="X"> no encuentra nada, y el arranque muere con

    No se pudo iniciar la aplicacion:
    'Se produjo una excepcion al proporcionar un valor en
     System.Windows.Markup.StaticResourceHolder.' (numero de linea: 969)

que es exactamente el error que se reporto al agregar CeldaSoloGrout: heredaba
de CeldaAcabado y estaba escrito 670 lineas mas arriba que el.

El error NO se ve leyendo el XAML -las dos claves existen, solo estan en el orden
equivocado- ni lo atrapa el compilador: es de tiempo de ejecucion, y ademas de
arranque, o sea que el programa no llega ni a abrir la ventana. De ahi esta
comprobacion.

    python tools/verificar_recursos_xaml.py

Lo que se comprueba, archivo por archivo:

  1. Toda clave usada con StaticResource en ESE archivo y declarada en ESE mismo
     archivo, se declara antes del primer uso.
  2. Toda clave usada con StaticResource existe en alguno de los diccionarios del
     proyecto -o el arranque falla igual, por otra razon: la clave no esta-.

Las claves de OTRO archivo no se ordenan: el diccionario se resuelve entero antes
de que la ventana lo use, y para eso estan los MergedDictionaries.
"""

from __future__ import annotations

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIENTE = os.path.join(RAIZ, "client", "src")

fallos: list[str] = []


def check(nombre: str, ok: bool, detalle: str = "") -> None:
    if ok:
        print(f"  OK    {nombre}")
        return

    print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
    fallos.append(f"{nombre}: {detalle}")


def xamls() -> list[str]:
    salida = []

    for base, _, archivos in os.walk(CLIENTE):
        if os.sep + "obj" in base or os.sep + "bin" in base:
            continue

        for a in sorted(archivos):
            if a.endswith(".xaml"):
                salida.append(os.path.join(base, a))

    return sorted(salida)


def leer(p: str) -> str:
    with open(p, encoding="utf-8-sig") as f:
        return f.read()


#  La clave que se DECLARA: x:Key="Nombre". Se guarda la posicion del primer
#  caracter, que es con la que se compara.
RE_CLAVE = re.compile(r'x:Key="([^"{}]+)"')

#  Y la que se USA. Solo el StaticResource: el DynamicResource se resuelve cuando
#  hace falta y por eso puede ir en cualquier orden -es justo lo que hace el tema
#  oscuro-, asi que no entra aqui.
#
#  Se descartan a proposito las claves con llave -{x:Type DataGridCell}, que es
#  como se hereda del estilo de serie- porque no son claves de este diccionario.
RE_USO = re.compile(r"\{StaticResource\s+([^}{\s]+)\s*\}")

print("=" * 78)
print("NINGUN StaticResource SE USA ANTES DE DECLARARSE")
print("=" * 78)

archivos = xamls()

check("se encontraron los XAML del cliente", len(archivos) >= 3,
      f"{len(archivos)} archivos")

#  Todas las claves del proyecto, para el segundo control.
todas: dict[str, str] = {}

for ruta in archivos:
    for m in RE_CLAVE.finditer(leer(ruta)):
        todas.setdefault(m.group(1), os.path.basename(ruta))

for ruta in archivos:
    texto = leer(ruta)
    nombre = os.path.basename(ruta)

    #  Primera declaracion de cada clave en ESTE archivo.
    declara: dict[str, int] = {}

    for m in RE_CLAVE.finditer(texto):
        declara.setdefault(m.group(1), m.start())

    tarde: list[str] = []
    faltan: list[str] = []

    for m in RE_USO.finditer(texto):
        clave = m.group(1)

        if clave in declara:
            if m.start() < declara[clave]:
                renglon = texto.count("\n", 0, m.start()) + 1
                donde = texto.count("\n", 0, declara[clave]) + 1
                tarde.append(f"{clave} (usada en la linea {renglon}, "
                             f"declarada en la {donde})")
            continue

        if clave not in todas:
            renglon = texto.count("\n", 0, m.start()) + 1
            faltan.append(f"{clave} (linea {renglon})")

    check(f"{nombre}: cada clave se declara antes de usarse",
          not tarde, "; ".join(tarde))

    check(f"{nombre}: y todas las claves existen",
          not faltan, "; ".join(faltan))

print()
print("=" * 78)

if fallos:
    print(f"FALLARON {len(fallos)} COMPROBACIONES:")

    for f in fallos:
        print(f"  - {f}")

    print("=" * 78)
    sys.exit(1)

print("OK: el orden de los recursos del XAML aguanta el arranque.")
print("=" * 78)
