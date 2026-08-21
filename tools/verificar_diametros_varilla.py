"""Comprueba la tabla de diametros de varilla contra la formula.

La varilla del numero n mide n/8 de pulgada, y una pulgada son 25.4 mm EXACTOS.
Asi que la tabla no es una lista de valores medidos: es una formula, y se puede
comprobar. Este archivo existe porque la tabla del port estaba redondeada y uno
de sus valores estaba mal, y eso solo se vio al compararla con la de la macro.

  #2  : 0.60 en el port, 0.64 en la macro, 0.635 el nominal -> area 12.1 % BAJA
  #6  : 1.90 en el port, 1.905 el nominal                   -> area  1.0 % baja
  #10 : 3.20 en el port, 3.175 el nominal                   -> area  1.3 % ALTA
  #12 : 3.80 en el port, 3.81 el nominal                    -> area  0.5 % baja

Una cuantia baja es del lado INSEGURO: hace pasar por bueno un armado que no
llega al minimo. Por eso se corrigio la tabla en lugar de dejarla «casi bien».
"""

import math
import re

fallos = []


def check(nombre, cond, detalle=""):
    print(("  OK    " if cond else "  FALLA ") + nombre + ("" if cond else "  " + detalle))
    if not cond:
        fallos.append(nombre)


def nominal_cm(n):
    """n octavos de pulgada, en centimetros."""
    return n / 8.0 * 2.54


# ----------------------------------------------------------------------
# La tabla se LEE del C#, no se copia aqui
# ----------------------------------------------------------------------
# Copiarla dejaria dos tablas que pueden divergir, que es exactamente el problema
# que este archivo comprueba.
RUTA = "client/src/CadLink.App/Models/StructuralRows.cs"

with open(RUTA, encoding="utf-8") as f:
    fuente = f.read()

bloque = re.search(
    r"DiametrosCm\s*=\s*new Dictionary<string, double>.*?\{(.*?)\};",
    fuente, re.S)

if bloque is None:
    print("No se pudo localizar DiametrosCm en " + RUTA)
    raise SystemExit(1)

tabla = {}
for m in re.finditer(r'\["(#[\d.]+)"\]\s*=\s*([\d.]+)', bloque.group(1)):
    tabla[m.group(1)] = float(m.group(2))

print("=" * 78)
print(" Tabla de diametros de varilla contra la formula n/8 de pulgada")
print("=" * 78)
print(f"\n  leidos {len(tabla)} diametros de {RUTA}\n")

check("la tabla se pudo leer", len(tabla) >= 9, f"solo {len(tabla)}")

cab = "  clave    tabla cm    nominal cm     dif       dif de area"
print(cab)
print("  " + "-" * (len(cab) - 2))

peor = 0.0

for clave, valor in tabla.items():
    n = float(clave[1:])
    nom = nominal_cm(n)
    dif = valor - nom
    dif_area = (valor * valor) / (nom * nom) - 1
    peor = max(peor, abs(dif_area))

    print(f"  {clave:6} {valor:10.5f} {nom:13.5f} {dif:9.5f} {dif_area*100:13.4f}%")

for clave, valor in tabla.items():
    n = float(clave[1:])
    check(f"{clave} coincide con el nominal exacto",
          abs(valor - nominal_cm(n)) < 1e-9,
          f"tabla {valor}, nominal {nominal_cm(n):.6f}")

print(f"\n  peor error de area = {peor*100:.6f} %")
check("ningun diametro tiene error de area", peor < 1e-9,
      f"el peor es {peor*100:.4f} %")

# ----------------------------------------------------------------------
# La macro tenia razon: su tabla se acerca mas al nominal que la vieja del port
# ----------------------------------------------------------------------
print("\nLa tabla de la macro (RebarDiaM) contra la que tenia el port")

macro = {"#2": 0.64, "#2.5": 0.80, "#3": 0.95, "#4": 1.27, "#5": 1.59,
         "#6": 1.91, "#8": 2.54, "#10": 3.18, "#12": 3.81}

vieja = {"#2": 0.60, "#2.5": 0.80, "#3": 0.95, "#4": 1.27, "#5": 1.59,
         "#6": 1.90, "#8": 2.54, "#10": 3.20, "#12": 3.80}


def error_total(t):
    return sum(abs(v - nominal_cm(float(k[1:]))) for k, v in t.items())


e_macro = error_total(macro)
e_vieja = error_total(vieja)
e_nueva = error_total(tabla)

print(f"  error acumulado de la macro     = {e_macro:.5f} cm")
print(f"  error acumulado del port viejo  = {e_vieja:.5f} cm")
print(f"  error acumulado del port nuevo  = {e_nueva:.5f} cm")

check("la macro era mas precisa que el port viejo", e_macro < e_vieja,
      f"macro {e_macro:.5f} contra port {e_vieja:.5f}")
check("y el port nuevo es exacto", e_nueva < 1e-9, f"{e_nueva:.5f} cm")

# El caso concreto que se corrigio
a_vieja = math.pi * 0.60 ** 2 / 4
a_nueva = math.pi * nominal_cm(2) ** 2 / 4
print(f"\n  area de un #2: antes {a_vieja:.5f} cm2, ahora {a_nueva:.5f} cm2 "
      f"({(a_nueva/a_vieja - 1)*100:+.2f} %)")
check("el #2 sube alrededor de un 12 % de area",
      0.11 < (a_nueva / a_vieja - 1) < 0.13,
      f"subio {(a_nueva/a_vieja - 1)*100:.2f} %")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f_ in fallos:
        print("   - " + f_)
else:
    print(" Todo correcto.")
print("=" * 78)
