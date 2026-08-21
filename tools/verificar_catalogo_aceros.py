"""Comprueba el catalogo de aceros: el CSV, el lector del programa y la disponibilidad.

Cuatro cosas que no se ven leyendo el codigo:

  1. QUE EL CSV ESTA AL DIA. Lo genera tools/catalogo_aceros.py de docs/ACEROS.xlsx. Si
     alguien edita la hoja y no vuelve a generarlo, el programa sigue usando el viejo y
     nadie se enteraria: el CSV es un archivo valido, solo que con datos de antes.

  2. EL LECTOR DEL CSV. Lo va a editar una persona en Excel o en el Bloc de notas, asi que
     tiene que tragarse comentarios, lineas en blanco, punto y coma o coma de separador,
     coma decimal, columnas de menos y celdas raras. Se le pasan todos esos casos.

  3. LA BUSQUEDA POR DESIGNACION. El mismo acero se escribe «A-572 GR. 50» o
     «A-572-Gr. 50», y los dos son el mismo. Pero «A-500-Gr. B» y «A-500-Gr. B'» son DOS
     ACEROS DISTINTOS -el redondo y el rectangular, con Fy 2955 y 3235-, asi que el
     apostrofo tiene que contar. Es la unica diferencia entre las dos designaciones.

  4. QUE EL EJEMPLO DEL PROGRAMA NO ARRANQUE EN ROJO. La tabla marca la fila cuando el
     acero no se hace en ese perfil. Si el ejemplo trae combinaciones imposibles, arranca
     con media hoja marcada y le enseña al usuario a ignorar la marca. Se leen las doce
     filas del ejemplo del codigo y se comprueban contra el catalogo, una por una.
"""

import importlib.util
import os
import re

fallos = []
avisos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


def leer(ruta):
    with open(ruta, encoding="utf-8") as f:
        return f.read()


RUTA_CSV = "client/src/CadLink.App/aceros.csv"
RUTA_XLSX = "docs/ACEROS.xlsx"

# El orden de FamiliaPerfil.Todas, que es el de las columnas del CSV.
FAMILIAS = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

SI = "SI"
VERIFICAR = "VERIFICAR"
NO = "NO"


# ===========================================================================
#  1. El CSV esta al dia con la hoja
# ===========================================================================

print("=" * 78)
print(" El CSV contra la hoja de la que sale")
print("=" * 78)

spec = importlib.util.spec_from_file_location(
    "catalogo_aceros", os.path.join("tools", "catalogo_aceros.py"))
gen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen)

check("esta la hoja de aceros", os.path.exists(RUTA_XLSX))
check("y el CSV generado", os.path.exists(RUTA_CSV))

de_la_hoja = gen.leer(RUTA_XLSX)

print(f"\n    la hoja trae {len(de_la_hoja)} aceros")

# Las filas del CSV, sin comentarios ni blancos.
filas_csv = [l for l in leer(RUTA_CSV).splitlines()
             if l.strip() and not l.startswith("#")]

check("el CSV trae los mismos aceros que la hoja",
      len(filas_csv) == len(de_la_hoja),
      f"{len(filas_csv)} en el CSV, {len(de_la_hoja)} en la hoja")

# Y los mismos NUMEROS, no solo la misma cuenta: se compara acero por acero.
distintos = []

for a, linea in zip(de_la_hoja, filas_csv):
    campos = linea.split(";")

    if len(campos) < 8 + len(FAMILIAS):
        distintos.append(f"{a['astm']}: la linea del CSV tiene {len(campos)} campos")
        continue

    esperado = (
        [a["grupo"], a["astm"], a["nmx"], f"{a['fy_kgcm2']:.0f}"]
        + [a["disp"][f] for f in FAMILIAS])

    real = campos[:4] + campos[7:7 + len(FAMILIAS)]

    if esperado != real:
        distintos.append(f"{a['astm']}: {esperado} contra {real}")

check("y los mismos datos, acero por acero", not distintos,
      "; ".join(distintos[:3]))

print("    -si esto falla, vuelve a generar el CSV:")
print("     python3 tools/catalogo_aceros.py docs/ACEROS.xlsx "
      "> client/src/CadLink.App/aceros.csv")


# ===========================================================================
#  2. El lector del CSV, con los casos que se le van a dar
# ===========================================================================

print("\n" + "=" * 78)
print(" El lector del CSV")
print("=" * 78)


def respuesta(celda):
    """Port de CatalogoAceros.Respuesta."""
    t = (celda or "").strip().upper()

    if t in ("SI", "SÍ", "S", "X"):
        return SI

    if t in ("NO", "-", "", "--"):
        return NO

    return VERIFICAR


def numero(campo):
    """Port de CatalogoAceros.Numero."""
    t = (campo or "").strip()

    if not t or t == "-":
        return None

    try:
        return float(t.replace(",", "."))
    except ValueError:
        return None


def leer_csv(lineas):
    """Port de CatalogoAceros.Leer."""
    aceros = []

    for cruda in lineas:
        linea = (cruda or "").strip()

        if not linea or linea.startswith("#"):
            continue

        sep = ";" if ";" in linea else ","
        campos = linea.split(sep)

        if len(campos) < 4:
            continue

        astm = campos[1].strip()
        fy = numero(campos[3])

        if not astm or fy is None or fy <= 0:
            continue

        disp = {}

        for i, familia in enumerate(FAMILIAS):
            col = 7 + i

            if col < len(campos):
                disp[familia] = respuesta(campos[col])

        col_placa = 7 + len(FAMILIAS)

        aceros.append({
            "grupo": campos[0].strip(),
            "astm": astm,
            "nmx": campos[2].strip(),
            "fy": fy,
            "fy_mpa": numero(campos[4]) if len(campos) > 4 else None,
            "fu": numero(campos[5]) if len(campos) > 5 else None,
            "fu_mpa": numero(campos[6]) if len(campos) > 6 else None,
            "disp": disp,
            "placa": respuesta(campos[col_placa]) if col_placa < len(campos) else VERIFICAR,
        })

    return aceros


LINEA_BUENA = ("CARBÓN;A-36;B-255;2530;250;4080;400;"
               "VERIFICAR;VERIFICAR;VERIFICAR;SI;VERIFICAR;SI;NO;NO;SI;NO;NO;NO;SI")

CASOS = [
    ("una linea normal", [LINEA_BUENA], 1),
    ("comentarios y lineas en blanco", ["# comentario", "", "   ", LINEA_BUENA], 1),
    ("separador de coma", [LINEA_BUENA.replace(";", ",")], 1),
    ("decimal con coma", ["CARBÓN;A-36;B-255;2530,5;250;4080;400"], 1),
    ("sin las columnas de disponibilidad", ["CARBÓN;A-36;B-255;2530"], 1),
    ("sin Fy, que no se puede usar", ["CARBÓN;A-36;B-255;"], 0),
    ("con Fy en cero", ["CARBÓN;A-36;B-255;0"], 0),
    ("con Fy que no es un numero", ["CARBÓN;A-36;B-255;acero"], 0),
    ("sin designacion", ["CARBÓN;;B-255;2530"], 0),
    ("una linea con dos campos", ["CARBÓN;A-36"], 0),
    ("el encabezado de un Excel", ["grupo;astm;nmx;fy_kgcm2"], 0),
]

print()
for nombre, lineas, esperados in CASOS:
    salieron = leer_csv(lineas)

    check(f"{nombre}: salen {esperados}", len(salieron) == esperados,
          f"salieron {len(salieron)}")

# Los tres valores de la disponibilidad, y sobre todo el que NO es rojo.
print()
for celda, esperado in (("SI", SI), ("Sí", SI), ("s", SI), ("X", SI),
                        ("VERIFICAR", VERIFICAR), ("verificar", VERIFICAR),
                        ("Verificar con proveedor", VERIFICAR),
                        ("-", NO), ("", NO), ("NO", NO), ("--", NO)):
    check(f"la celda «{celda}» se lee como {esperado}", respuesta(celda) == esperado,
          respuesta(celda))

# Y LO QUE IMPORTA DE ESA TABLA: una celda que no se entiende cae en VERIFICAR, nunca en
# NO. Marcar la fila en rojo por una celda que el programa no supo leer seria afirmar algo
# que el archivo no dice.
check("una celda que no se entiende cae en VERIFICAR, no en NO",
      respuesta("tal vez") == VERIFICAR and respuesta("?") == VERIFICAR)

# Las columnas que faltan tampoco son NO.
sin_columnas = leer_csv(["CARBÓN;A-36;B-255;2530"])[0]

check("y una columna que falta se queda sin dato, no en NO",
      sin_columnas["disp"] == {},
      f"{sin_columnas['disp']}")


# ===========================================================================
#  3. La busqueda por designacion
# ===========================================================================

print("\n" + "=" * 78)
print(" La busqueda por designacion")
print("=" * 78)


def clave(texto):
    """Port de CatalogoAceros.Clave: solo letras, digitos y apostrofos."""
    return "".join(c for c in (texto or "").strip().upper()
                   if c.isalnum() or c == "'")


PAREJAS_IGUALES = [
    ("A-572 GR. 50", "A-572-Gr. 50"),      # como se escribia antes / como lo escribe la hoja
    ("A-500 GR. B", "A-500-Gr. B"),
    ("A-53 GR. B", "A-53-Gr. B"),
    ("a992", "A-992"),
    ("A 36", "A-36"),
]

print()
for viejo, nuevo in PAREJAS_IGUALES:
    check(f"«{viejo}» y «{nuevo}» son el mismo acero",
          clave(viejo) == clave(nuevo), f"{clave(viejo)} contra {clave(nuevo)}")

# Y LA QUE NO PUEDE CONFUNDIRSE: el apostrofo distingue dos aceros de verdad.
check("«A-500-Gr. B» y «A-500-Gr. B'» NO son el mismo",
      clave("A-500-Gr. B") != clave("A-500-Gr. B'"))

del_csv = leer_csv(leer(RUTA_CSV).splitlines())
por_clave = {clave(a["astm"]): a for a in del_csv}

check("las 39 designaciones dan 39 claves distintas",
      len(por_clave) == len(del_csv),
      f"{len(por_clave)} claves para {len(del_csv)} aceros")

b = por_clave.get(clave("A-500-Gr. B"))
bp = por_clave.get(clave("A-500-Gr. B'"))

check("los dos A-500 Gr. B estan en el catalogo", b is not None and bp is not None)

if b and bp:
    print(f"\n    A-500-Gr. B  Fy {b['fy']:.0f}   se hace en: "
          f"{', '.join(f for f in FAMILIAS if b['disp'][f] == SI) or 'ninguna'}")
    print(f"    A-500-Gr. B' Fy {bp['fy']:.0f}   se hace en: "
          f"{', '.join(f for f in FAMILIAS if bp['disp'][f] == SI) or 'ninguna'}")

    check("el de sin apostrofo es el del tubo REDONDO", b["disp"]["OC"] == SI)
    check("y el de con apostrofo el del RECTANGULAR", bp["disp"]["OR"] == SI)
    check("y tienen Fy distinto, que es lo que se perderia al confundirlos",
          abs(b["fy"] - bp["fy"]) > 100,
          f"{b['fy']:.0f} y {bp['fy']:.0f}")


# ===========================================================================
#  4. La fisica de los numeros
# ===========================================================================

print("\n" + "=" * 78)
print(" Los numeros del catalogo")
print("=" * 78)

sin_fu = [a["astm"] for a in del_csv if a["fu"] is None]
fu_menor = [f"{a['astm']}: Fu {a['fu']:.0f} <= Fy {a['fy']:.0f}"
            for a in del_csv if a["fu"] is not None and a["fu"] <= a["fy"]]

print()
check("todos traen Fu", not sin_fu, f"sin Fu: {sin_fu[:3]}")
check("y en todos Fu es mayor que Fy", not fu_menor, "; ".join(fu_menor[:3]))

fuera_de_rango = [f"{a['astm']}: {a['fy']:.0f}"
                  for a in del_csv if not 2000 <= a["fy"] <= 7500]

check("todos los Fy son creibles para acero estructural", not fuera_de_rango,
      "; ".join(fuera_de_rango[:3]))

# Las dos unidades tienen que decir lo mismo: 1 MPa = 10.1972 kg/cm2. Se acepta un 3 %
# porque la hoja redondea cada valor por su cuenta.
descuadres = []

for a in del_csv:
    for etiqueta, kg, mpa in (("Fy", a["fy"], a["fy_mpa"]), ("Fu", a["fu"], a["fu_mpa"])):
        if kg is None or not mpa:
            continue

        if abs(kg / (mpa * 10.19716) - 1) > 0.03:
            descuadres.append(
                f"{a['astm']} {etiqueta}: {kg:.0f} kg/cm2 contra {mpa:.0f} MPa "
                f"= {mpa * 10.19716:.0f}")

# ESTO SE AVISA, NO SE EXIGE: son celdas de la hoja del usuario, y el programa no las
# corrige. Lo que se exige es que el generador del CSV las haya reportado, para que quien
# lo corra se entere.
if descuadres:
    for d in descuadres:
        avisos.append(f"la hoja no cuadra en {d}")

print(f"    {len(descuadres)} acero(s) con las dos unidades descuadradas "
      "(celdas de la hoja, se avisan)")

informe = gen.revisar(gen.leer(RUTA_XLSX))

check("el generador avisa de los descuadres que encuentra",
      len(informe) >= len(descuadres),
      f"el generador avisa {len(informe)} y aqui salen {len(descuadres)}")


# ===========================================================================
#  5. La disponibilidad: cada familia tiene con que dibujarse
# ===========================================================================

print("\n" + "=" * 78)
print(" La disponibilidad, familia por familia")
print("=" * 78)

print()
for familia in FAMILIAS:
    con_si = [a["astm"] for a in del_csv if a["disp"].get(familia) == SI]
    con_ver = [a["astm"] for a in del_csv if a["disp"].get(familia) == VERIFICAR]

    print(f"    {familia:3}  {len(con_si)} con SI, {len(con_ver)} por verificar"
          f"   ({', '.join(con_si[:3]) or 'ninguno'})")

    # Cada familia tiene que tener AL MENOS UN acero que se haga en ella. Si alguna no
    # tuviera ninguno, cualquier fila de esa familia saldria marcada, y una marca que sale
    # siempre no informa de nada.
    check(f"la familia {familia} tiene algun acero que se hace en ella", len(con_si) > 0)

# Y ninguna familia puede estar en TODOS los aceros: si un acero se hiciera en las doce,
# o la hoja esta mal o la columna no dice nada.
todas = [a["astm"] for a in del_csv
         if all(a["disp"].get(f) == SI for f in FAMILIAS)]

check("ningun acero se hace en las doce familias a la vez", not todas,
      f"{todas}")


# ===========================================================================
#  6. El ejemplo del programa no arranca en rojo
# ===========================================================================

print("\n" + "=" * 78)
print(" Las doce filas del ejemplo, contra el catalogo")
print("=" * 78)

fuente = leer("client/src/CadLink.App/Models/StructuralRows.cs")
constantes = leer("client/src/CadLink.App/Models/PerfilAceroRow.cs")

# Las constantes de acero del modelo: AceroA36 = "A-36", etc.
valor_de = dict(re.findall(
    r'public const string (Acero\w+) = "((?:[^"\\]|\\.)*)";', constantes))

valor_de = {k: v.replace('\\"', '"') for k, v in valor_de.items()}

print(f"\n    constantes de acero en el modelo: {len(valor_de)}")

check("las constantes de acero se pudieron leer", len(valor_de) >= 5, f"{valor_de}")

# Cada constante tiene que EXISTIR en el catalogo: una constante que no esta deja la celda
# del desplegable en blanco, porque el desplegable se llena del catalogo.
fuera = [f"{k} = «{v}»" for k, v in valor_de.items() if clave(v) not in por_clave]

check("todas las constantes de acero estan en el catalogo", not fuera,
      "; ".join(fuera))

# Y las doce filas del ejemplo: familia y acero, leidos del codigo.
filas_ejemplo = re.findall(
    r"Acero\(FamiliaPerfil\.(\w+),.*?PerfilAceroRow\.(Acero\w+),",
    fuente, re.S)

print(f"    filas de acero en el ejemplo: {len(filas_ejemplo)}")

check("el ejemplo trae doce filas de acero", len(filas_ejemplo) == 12,
      f"{len(filas_ejemplo)}")

marcadas = []
por_verificar = []

print()
for familia_cs, constante in filas_ejemplo:
    familia = familia_cs.upper()
    designacion = valor_de.get(constante, "")
    acero = por_clave.get(clave(designacion))

    estado = acero["disp"].get(familia, VERIFICAR) if acero else "FUERA DEL CATALOGO"

    print(f"    {familia:3} con {designacion:16} -> {estado}")

    if estado == NO:
        marcadas.append(f"{familia} con {designacion}")
    elif estado != SI:
        por_verificar.append(f"{familia} con {designacion}")

check("ninguna fila del ejemplo arranca marcada en rojo", not marcadas,
      "; ".join(marcadas))

if por_verificar:
    avisos.append(
        "filas del ejemplo con acero por verificar: " + "; ".join(por_verificar))


print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")

if avisos:
    print(f"\n {len(avisos)} AVISO(S), que no son fallos:")
    for a in avisos:
        print("   - " + a)
print("=" * 78)

raise SystemExit(1 if fallos else 0)
