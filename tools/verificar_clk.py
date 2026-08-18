"""Comprueba el ida y vuelta del archivo .clk.

Lo que se garantiza: guardar y volver a abrir devuelve EXACTAMENTE lo mismo. Y lo
que se comprueba con mas cuidado son los casos que rompen un formato de archivo en
la practica:

  - acentos y comillas en los textos
  - una lista vacia
  - separacion decimal (el archivo no debe depender de la configuracion regional)
  - un archivo de una version mas nueva: hay que rechazarlo, no leerlo a medias
  - un archivo corrupto: hay que decir QUE archivo y POR QUE

Se traduce ArchivoProyecto a Python. No es el C#, pero el formato es JSON y las
reglas que se prueban aqui son las del formato, no las del lenguaje.
"""

import json
import os
import tempfile

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


VERSION = 1


def guardar(ruta, p):
    """Port de ArchivoProyecto.Guardar: temporal y luego cambio."""
    temporal = ruta + ".tmp"
    with open(temporal, "w", encoding="utf-8") as f:
        json.dump(p, f, indent=2, ensure_ascii=False)
    if os.path.exists(ruta):
        os.remove(ruta)
    os.replace(temporal, ruta)


class DatosInvalidos(Exception):
    pass


def leer(ruta):
    """Port de ArchivoProyecto.Leer."""
    with open(ruta, encoding="utf-8") as f:
        texto = f.read()

    try:
        p = json.loads(texto)
    except json.JSONDecodeError as ex:
        raise DatosInvalidos(
            f"El archivo '{os.path.basename(ruta)}' no parece un trabajo de CadLink. "
            f"Detalle: {ex}") from ex

    if p is None:
        raise DatosInvalidos(f"El archivo '{os.path.basename(ruta)}' esta vacio.")

    if p.get("Version", 1) > VERSION:
        raise DatosInvalidos(
            f"El archivo se guardo con una version mas nueva de CadLink "
            f"(formato {p['Version']}).")

    return p


print("=" * 78)
print(" Archivo .clk: guardar y volver a abrir")
print("=" * 78)

tmp = tempfile.mkdtemp()
ruta = os.path.join(tmp, "obra.clk")

proyecto = {
    "Version": 1,
    "Aplicacion": "CadLink 1.0.0",
    "Calculista": "Ing. José Ramírez Peña",
    "Propietario": 'Constructora "El Álamo" S.A. de C.V.',
    "Ubicacion": "Av. Insurgentes 1234, CDMX",
    "Obra": "Edificio Torre Norte",
    "Dibujo": "M. Ángeles",
    "Escala": "1:50",
    "Acotacion": "cm",
    "EscalaDibujo": 0.01,
    "EscalaHatch": 0.0003,
    "ModoSeccion": 1,
    "Planos": [
        {"Clave": "E-01", "Contiene": "PLANTA NIVEL 3", "Escala": "1:75"},
        {"Clave": "E-02", "Contiene": "PLANTA NIVEL 2", "Escala": "1:75"},
    ],
    "Secciones": [
        {"Elemento": "COLUMNA", "Id": "C-1", "BaseCm": 40.0, "AlturaCm": 40.0,
         "NSupEsquina": 3, "DiaSupEsquina": "#6", "Fc": "250", "GanchoCm": 5.0,
         "Separacion": "10-20-10", "Diamante": True, "DiaDiamante": "#3",
         "RecubrimientoCm": 4.0, "LongitudM": "3.00"},
        {"Elemento": "CASTILLO", "Id": "K-1", "BaseCm": 15.0, "AlturaCm": 15.0,
         "NSupEsquina": 2, "DiaSupEsquina": "#3", "Fc": "200", "GanchoCm": 5.0,
         "Separacion": "20-20-20", "Diamante": False, "DiaDiamante": "",
         "RecubrimientoCm": 2.0, "LongitudM": ""},
    ],
}

guardar(ruta, proyecto)
leido = leer(ruta)

check("el archivo se crea", os.path.exists(ruta))
check("no queda ningun temporal", not os.path.exists(ruta + ".tmp"))
check("lo leido es EXACTAMENTE lo guardado", leido == proyecto)

# Acentos y comillas: si se escaparan, el archivo deja de poder leerse a ojo, que
# es la razon de usar JSON en lugar de binario.
crudo = open(ruta, encoding="utf-8").read()
check("los acentos se guardan tal cual, sin escapar",
      "José Ramírez Peña" in crudo and "\\u00" not in crudo)
check("las comillas dentro de un texto no rompen el archivo",
      leido["Propietario"] == 'Constructora "El Álamo" S.A. de C.V.')

# Los decimales, con punto siempre: el archivo no debe depender de si el Windows
# del usuario usa coma. Un .clk guardado en un equipo tiene que abrir en otro.
check("los decimales van con punto, no con coma",
      '"EscalaHatch": 0.0003' in crudo, "se guardo con otro formato")
check("y se leen como numero", leido["EscalaHatch"] == 0.0003)

# Guardar dos veces sobre el mismo archivo.
guardar(ruta, proyecto)
check("guardar encima funciona", leer(ruta) == proyecto)

# Un proyecto vacio: no debe tronar ni al guardar ni al abrir.
vacio = {"Version": 1, "Planos": [], "Secciones": []}
r2 = os.path.join(tmp, "vacio.clk")
guardar(r2, vacio)
check("un proyecto vacio va y vuelve", leer(r2) == vacio)

# ---- Los casos que hay que RECHAZAR ----
print()
print("=" * 78)
print(" Archivos que hay que rechazar, no leer a medias")
print("=" * 78)

r3 = os.path.join(tmp, "futuro.clk")
guardar(r3, {"Version": 99, "Planos": [], "Secciones": []})

try:
    leer(r3)
    check("un archivo de version mas nueva se rechaza", False, "se leyo igual")
except DatosInvalidos as ex:
    check("un archivo de version mas nueva se rechaza", True)
    check("y el mensaje dice el numero de formato", "99" in str(ex), str(ex))

r4 = os.path.join(tmp, "roto.clk")
with open(r4, "w", encoding="utf-8") as f:
    f.write('{"Version": 1, "Planos": [ esto no es json')

try:
    leer(r4)
    check("un archivo corrupto se rechaza", False, "se leyo igual")
except DatosInvalidos as ex:
    check("un archivo corrupto se rechaza", True)
    check("y el mensaje dice QUE archivo", "roto.clk" in str(ex), str(ex))
    check("y da el detalle del error", "Detalle:" in str(ex), str(ex))

# La extension
check("la extension es .clk", ruta.endswith(".clk"))

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
