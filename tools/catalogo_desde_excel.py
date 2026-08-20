"""Convierte una hoja de perfiles de Excel en el perfiles-acero.csv de CadLink.

    python3 tools/catalogo_desde_excel.py MI_HOJA.xlsx > client/src/CadLink.App/perfiles-acero.csv

Tambien lee un .csv o un .txt ya exportado, por si es mas facil exportar que mandar el
libro entero.

POR QUE ESTE SCRIPT Y NO LEER EL EXCEL DESDE EL PROGRAMA
--------------------------------------------------------
Porque el catalogo se escribe una vez y se lee mil. Meter un lector de xlsx en CadLink
obligaria a arrastrar una biblioteca entera -y a mantenerla- para algo que se hace el dia
que cambia la lista de perfiles. Asi el programa solo sabe leer un CSV de ocho campos,
que es lo mas simple que puede ser, y la conversion se hace aparte.

NO NECESITA NADA INSTALADO. Un .xlsx es un zip con XML dentro, y eso se abre con la
biblioteca estandar de Python.

QUE ESPERA ENCONTRAR
--------------------
Una fila de encabezados en cualquier parte de la hoja, y debajo los perfiles. Los
encabezados se reconocen por su nombre, en español o en la nomenclatura de los catalogos,
asi que da igual el orden de las columnas:

    peralte    peralte, d, h, alto, altura, diametro, diam, od
    ancho      ancho, b, bf, patin, ala
    e alma     e alma, tw, alma, espesor, t, pared, e
    e patin    e patin, tf, patin espesor
    labio      labio, lip
    radio      radio, r, ri, doblez
    nombre     perfil, seccion, designacion, nombre, clave
    familia    familia, tipo, grupo

Si no hay columna de familia, se deduce del nombre: W... es IR, HSS... es OR, PIPE... es
OC y CF... es CF. Y si no se deduce, la fila se salta y se avisa al final.

LAS UNIDADES
------------
Si los numeros parecen milimetros -un peralte de 300 en vez de 30- se avisa, pero NO se
convierte solo: convertir por si mismo lo que parece es como acaban los planos con
perfiles diez veces mas grandes. Para convertir hay que pedirlo:

    python3 tools/catalogo_desde_excel.py MI_HOJA.xlsx --mm
"""

import re
import sys
import xml.etree.ElementTree as ET
import zipfile

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

FAMILIAS = ("IR", "OR", "OC", "CF")

# Como se llama cada columna en las hojas de verdad. El orden importa: se prueba de lo
# mas especifico a lo mas general, porque 'e patin' contiene 'patin' y 'espesor' a secas
# tiene que caer en el alma y no en el patin.
COLUMNAS = [
    ("e_patin", ("e patin", "epatin", "e_patin", "tf", "espesor patin",
                 "espesor de patin", "patin espesor")),
    ("e_alma", ("e alma", "ealma", "e_alma", "tw", "espesor alma",
                "espesor de alma", "espesor", "pared", "e pared", "t", "e")),
    ("ancho", ("ancho", "bf", "b", "patin", "ala", "ancho patin")),
    ("peralte", ("peralte", "d", "h", "alto", "altura", "diametro",
                 "diam", "od", "diametro exterior")),
    ("labio", ("labio", "lip")),
    ("radio", ("radio", "r", "ri", "doblez", "radio doblez")),
    ("nombre", ("perfil", "seccion", "sección", "designacion", "designación",
                "nombre", "clave", "id")),
    ("familia", ("familia", "tipo", "grupo")),
]


def normalizar(texto):
    """Baja a minusculas, quita acentos y deja un solo espacio."""
    t = (texto or "").strip().lower()

    for a, b in (("á", "a"), ("é", "e"), ("í", "i"), ("ó", "o"), ("ú", "u"),
                 ("ñ", "n"), (".", " "), ("(", " "), (")", " "), ("_", " ")):
        t = t.replace(a, b)

    return re.sub(r"\s+", " ", t).strip()


def familia_del_nombre(perfil):
    """La misma regla que FamiliaPerfil.DelNombre del programa."""
    p = (perfil or "").strip().upper()

    for pre, fam in (("HSS", "OR"), ("PTR", "OR"), ("OR", "OR"),
                     ("PIPE", "OC"), ("TUBO", "OC"), ("OC", "OC"),
                     ("CF", "CF"), ("CANAL", "CF"), ("MONTEN", "CF"),
                     ("IPR", "IR"), ("IR", "IR"), ("W", "IR")):
        if p.startswith(pre):
            return fam

    return None


# ---------------------------------------------------------------------------
#  Leer un xlsx sin bibliotecas
# ---------------------------------------------------------------------------

def celda_a_columna(ref):
    """'BC12' -> 54. El indice de columna, empezando en 0."""
    letras = re.match(r"([A-Z]+)", ref or "")

    if not letras:
        return 0

    n = 0
    for ch in letras.group(1):
        n = n * 26 + (ord(ch) - 64)

    return n - 1


def filas_de_xlsx(ruta):
    """Las filas de la PRIMERA hoja, como listas de texto."""
    with zipfile.ZipFile(ruta) as z:
        # Las cadenas van en una tabla aparte, y las celdas la referencian por indice.
        compartidas = []

        if "xl/sharedStrings.xml" in z.namelist():
            raiz = ET.fromstring(z.read("xl/sharedStrings.xml"))

            for si in raiz.findall(f"{NS}si"):
                # El texto de una celda puede venir partido en varios trozos con
                # formato distinto: hay que juntarlos todos.
                compartidas.append("".join(t.text or "" for t in si.iter(f"{NS}t")))

        hojas = sorted(n for n in z.namelist()
                       if n.startswith("xl/worksheets/sheet") and n.endswith(".xml"))

        if not hojas:
            raise SystemExit("Ese archivo no trae ninguna hoja de calculo.")

        raiz = ET.fromstring(z.read(hojas[0]))

        for fila in raiz.iter(f"{NS}row"):
            celdas = {}

            for c in fila.findall(f"{NS}c"):
                i = celda_a_columna(c.get("r"))
                tipo = c.get("t")

                v = c.find(f"{NS}v")
                texto = ""

                if tipo == "s" and v is not None:
                    idx = int(v.text or "0")
                    texto = compartidas[idx] if 0 <= idx < len(compartidas) else ""
                elif tipo == "inlineStr":
                    is_ = c.find(f"{NS}is")
                    if is_ is not None:
                        texto = "".join(t.text or "" for t in is_.iter(f"{NS}t"))
                elif v is not None:
                    texto = v.text or ""

                celdas[i] = texto.strip()

            if celdas:
                ancho = max(celdas) + 1
                yield [celdas.get(i, "") for i in range(ancho)]


def filas_de_texto(ruta):
    """Las filas de un csv o txt, separando por punto y coma, coma o tabulador."""
    with open(ruta, encoding="utf-8-sig", errors="replace") as f:
        for linea in f:
            linea = linea.rstrip("\n").rstrip("\r")

            if not linea.strip():
                continue

            sep = "\t" if "\t" in linea else (";" if ";" in linea else ",")
            yield [c.strip() for c in linea.split(sep)]


# ---------------------------------------------------------------------------
#  Encontrar los encabezados
# ---------------------------------------------------------------------------

def mapear_encabezados(fila):
    """De una fila de encabezados saca {campo: indice}, o None si no lo es."""
    mapa = {}

    for i, celda in enumerate(fila):
        nombre = normalizar(celda)

        if not nombre:
            continue

        for campo, alias in COLUMNAS:
            if campo in mapa:
                continue

            if nombre in alias:
                mapa[campo] = i
                break

    # Para dar una fila por encabezado hacen falta al menos el nombre y el peralte: es
    # lo minimo con lo que se puede identificar y dibujar un perfil.
    return mapa if "nombre" in mapa and "peralte" in mapa else None


def numero(texto):
    """El numero de una celda, o None. Tolera comas, espacios y unidades pegadas."""
    t = (texto or "").strip().replace(",", ".")
    t = re.sub(r"[^0-9.\-]", "", t)

    if not t or t in ("-", "."):
        return None

    try:
        return float(t)
    except ValueError:
        return None


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1

    ruta = argv[1]
    a_mm = "--mm" in argv

    filas = list(
        filas_de_xlsx(ruta) if ruta.lower().endswith((".xlsx", ".xlsm"))
        else filas_de_texto(ruta))

    if not filas:
        raise SystemExit("El archivo esta vacio.")

    # Los encabezados pueden estar en cualquier fila: las hojas de verdad traen
    # titulos, logos y filas en blanco antes de la tabla.
    mapa = None
    inicio = 0

    for i, fila in enumerate(filas):
        mapa = mapear_encabezados(fila)

        if mapa:
            inicio = i + 1
            break

    if not mapa:
        raise SystemExit(
            "No encontre la fila de encabezados. Hace falta al menos una columna de\n"
            "nombre de perfil (perfil, seccion, designacion...) y una de peralte\n"
            "(peralte, d, h, diametro...). Mira la lista de nombres reconocidos\n"
            "arriba en este script.")

    perfiles = []
    saltadas = []
    sospecha_mm = 0

    for n, fila in enumerate(filas[inicio:], start=inicio + 1):
        def campo(nombre):
            i = mapa.get(nombre)
            return fila[i] if i is not None and i < len(fila) else ""

        nombre = campo("nombre").strip()

        if not nombre:
            continue

        familia = (campo("familia") or "").strip().upper()

        if familia not in FAMILIAS:
            familia = familia_del_nombre(nombre) or ""

        if not familia:
            saltadas.append(f"fila {n}: '{nombre}' (no se sabe de que familia es)")
            continue

        medidas = {c: numero(campo(c)) for c in
                   ("peralte", "ancho", "e_alma", "e_patin", "labio", "radio")}

        if not medidas["peralte"]:
            saltadas.append(f"fila {n}: '{nombre}' (sin peralte)")
            continue

        if medidas["peralte"] > 200:
            sospecha_mm += 1

        if a_mm:
            medidas = {k: (v / 10 if v else v) for k, v in medidas.items()}

        perfiles.append((familia, nombre, medidas))

    if not perfiles:
        raise SystemExit("No salio ningun perfil. Revisa los encabezados.")

    # ---- El CSV ----
    print("# Catalogo de perfiles de acero de CadLink.")
    print(f"# Generado de {ruta} con tools/catalogo_desde_excel.py")
    print("# Medidas en CENTIMETROS.")
    print("# familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio")

    def fmt(v):
        return "" if not v else f"{v:g}"

    for familia in FAMILIAS:
        de_esta = [p for p in perfiles if p[0] == familia]

        if not de_esta:
            continue

        print(f"#")
        print(f"# {familia}: {len(de_esta)} perfil(es)")

        for _, nombre, m in de_esta:
            print(";".join([
                familia, nombre,
                fmt(m["peralte"]), fmt(m["ancho"]), fmt(m["e_alma"]),
                fmt(m["e_patin"]), fmt(m["labio"]), fmt(m["radio"]),
            ]))

    # ---- El informe, por la salida de errores para no ensuciar el CSV ----
    print(f"\nSalieron {len(perfiles)} perfiles:", file=sys.stderr)

    for familia in FAMILIAS:
        n = sum(1 for p in perfiles if p[0] == familia)
        if n:
            print(f"   {familia}: {n}", file=sys.stderr)

    if saltadas:
        print(f"\nSe saltaron {len(saltadas)} fila(s):", file=sys.stderr)
        for s in saltadas[:20]:
            print("   - " + s, file=sys.stderr)

    if sospecha_mm and not a_mm:
        print(
            f"\nOJO: {sospecha_mm} perfil(es) traen un peralte mayor de 200. Si la hoja\n"
            "esta en MILIMETROS, vuelve a correr esto con --mm. No se convierte solo a\n"
            "proposito: convertir por si mismo lo que PARECE es como acaban los planos\n"
            "con perfiles diez veces mas grandes.", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
