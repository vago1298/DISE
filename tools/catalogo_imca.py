"""Convierte el manual IMCA (docs/IMCA5.xlsx) en el perfiles-acero.csv de CadLink.

    python3 tools/catalogo_imca.py docs/IMCA5.xlsx > client/src/CadLink.App/perfiles-acero.csv

POR QUE UN SCRIPT APARTE DEL GENERICO
-------------------------------------
catalogo_desde_excel.py sirve para una hoja normal: una fila de encabezados y debajo
los perfiles. La hoja del IMCA no es asi, y por tres razones que hay que tratar una por
una:

  1. CADA FAMILIA USA COLUMNAS DISTINTAS. El perfil I trae sus medidas en las columnas
     con encabezado -d, h, tw, bf, tf-, pero el tubo rectangular las trae en tres
     columnas SIN encabezado y el tubo redondo en otras dos, tambien sin encabezado.
     Un lector guiado por encabezados no puede adivinarlas.

  2. LAS UNIDADES CAMBIAN DE UNA COLUMNA A OTRA. Casi todas las medidas van en
     milimetros, pero la columna 'tdist' -el espesor de diseño- va en CENTIMETROS para
     el tubo y en MILIMETROS para la lamina doblada. Confundirlas es un error de diez
     veces que no se ve en el dibujo: sale un perfil creible con la medida mal.

  3. HAY FAMILIAS QUE ESTE PROGRAMA TODAVIA NO SABE DIBUJAR: angulos, tes, canales
     laminados, zetas y redondos macizos. Se dejan fuera y se dice cuantas, en lugar de
     colarlas como si fueran otra cosa.

QUE COLUMNAS SE USAN, FAMILIA POR FAMILIA
-----------------------------------------
    W, IS, IC, S  ->  IR     col 2 = d (mm)    col 27 = bf   col 26 = tw   col 28 = tf
    HSS           ->  OR     col 6 = h (mm)    col 7 = b     col 8 = espesor
    PIPE          ->  OC     col 12 = D (mm)   col 13 = espesor
    CF            ->  CF     col 6 = h (mm)    col 7 = b     col 4 = espesor
                             col 11 = labio    col 12 = radio de doblez

TODO SALE EN CENTIMETROS, que es lo que lee CadLink.

LO QUE SE COMPRUEBA AL CONVERTIR
--------------------------------
Las columnas numericas se cotejan contra los nominales en pulgadas que la propia hoja
trae como texto: un tubo que dice 2" tiene que medir 50.8 mm. Lo que no cuadre se avisa
con su nombre y su diferencia, sin corregirlo por cuenta propia: si la hoja y su propio
nominal no coinciden, quien tiene que decidir es el usuario.
"""

import re
import sys
import xml.etree.ElementTree as ET
import zipfile

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

MM_POR_PULGADA = 25.4

# La familia del IMCA -> la familia de CadLink, o None si no se sabe dibujar.
#
# IS, IC y S se dibujan con la FORMA del IR porque son perfiles I: alma y dos patines.
# El nombre del rotulo NO se traduce, se conserva el del catalogo, asi que en el plano
# una IS sigue diciendo IS. Lo unico que se comparte es la forma.
FAMILIAS = {
    "W": "IR",
    "IS": "IR",
    "IC": "IR",
    "S": "IR",
    "HSS": "OR",
    "PIPE": "OC",
    "CF": "CF",
}

# Las que este programa no sabe dibujar todavia, con el motivo.
SIN_FORMA = {
    "L": "angulo: dos alas en escuadra, sin forma en el dibujante",
    "WT": "te: medio perfil I, sin forma en el dibujante",
    "C": "canal laminado: sin labios y con patin en cuña, no es el CF",
    "ZF": "zeta formada en frio, sin forma en el dibujante",
    "OS": "redondo macizo, sin forma en el dibujante",
}


def columna(ref):
    """'BC12' -> 54."""
    n = 0

    for ch in "".join(c for c in ref if c.isalpha()):
        n = n * 26 + (ord(ch) - 64)

    return n - 1


def filas_del_libro(ruta):
    """Las filas de la primera hoja: {numero de fila: {columna: texto}}."""
    with zipfile.ZipFile(ruta) as z:
        compartidas = []

        if "xl/sharedStrings.xml" in z.namelist():
            raiz = ET.fromstring(z.read("xl/sharedStrings.xml"))

            for si in raiz.findall(f"{NS}si"):
                compartidas.append("".join(t.text or "" for t in si.iter(f"{NS}t")))

        hojas = sorted(n for n in z.namelist()
                       if n.startswith("xl/worksheets/sheet") and n.endswith(".xml"))

        if not hojas:
            raise SystemExit("El libro no trae ninguna hoja.")

        filas = {}

        for fila in ET.fromstring(z.read(hojas[0])).iter(f"{NS}row"):
            celdas = {}

            for c in fila.findall(f"{NS}c"):
                i = columna(c.get("r"))
                tipo = c.get("t")
                v = c.find(f"{NS}v")
                texto = ""

                if tipo == "s" and v is not None:
                    k = int(v.text or 0)
                    texto = compartidas[k] if 0 <= k < len(compartidas) else ""
                elif tipo == "inlineStr":
                    is_ = c.find(f"{NS}is")
                    if is_ is not None:
                        texto = "".join(t.text or "" for t in is_.iter(f"{NS}t"))
                elif v is not None:
                    texto = v.text or ""

                if texto.strip():
                    celdas[i] = texto.strip()

            if celdas:
                filas[int(fila.get("r"))] = celdas

        return filas


def numero(texto):
    """El numero de una celda, o None. El guion de la hoja significa «no aplica»."""
    t = (texto or "").strip()

    if t in ("", "-", "—"):
        return None

    try:
        return float(t.replace(",", "."))
    except ValueError:
        return None


def pulgadas(texto):
    """'2 1/2"' -> 2.5. Lo que la hoja trae como nominal, para poder cotejarlo."""
    t = (texto or "").strip().replace('"', "").replace("''", "").replace("in", "")
    t = t.strip()

    if not t:
        return None

    # Entero y fraccion: 2 1/2
    m = re.fullmatch(r"(\d+)\s+(\d+)/(\d+)", t)
    if m:
        return int(m.group(1)) + int(m.group(2)) / int(m.group(3))

    # Solo fraccion: 1/2
    m = re.fullmatch(r"(\d+)/(\d+)", t)
    if m:
        return int(m.group(1)) / int(m.group(2))

    try:
        return float(t)
    except ValueError:
        return None


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1

    filas = filas_del_libro(argv[1])

    perfiles = []
    fuera = {}
    avisos = []

    for r in sorted(filas):
        if r <= 2:
            continue

        c = filas[r]
        familia_imca = c.get(0, "").strip().upper()

        if not familia_imca:
            continue

        nombre = c.get(1, "").strip()

        if not nombre:
            continue

        if familia_imca in SIN_FORMA:
            fuera.setdefault(familia_imca, 0)
            fuera[familia_imca] += 1
            continue

        familia = FAMILIAS.get(familia_imca)

        if familia is None:
            fuera.setdefault(familia_imca or "(vacia)", 0)
            fuera[familia_imca or "(vacia)"] += 1
            continue

        # ------------------------------------------------------------------
        #  Las medidas, cada familia de sus columnas y en sus unidades
        # ------------------------------------------------------------------
        peralte = ancho = espesor = e_patin = labio = radio = None

        if familia == "IR":
            # Perfil I: todo en milimetros, en las columnas con encabezado.
            # Se usa 'd', el peralte TOTAL, y no 'h', que es el alma libre.
            peralte = numero(c.get(2))
            ancho = numero(c.get(27))
            espesor = numero(c.get(26))
            e_patin = numero(c.get(28))

        elif familia == "OR":
            # Tubo rectangular: tres columnas sin encabezado, en milimetros. El
            # peralte es el lado MAYOR, que es como se dibuja.
            a = numero(c.get(6))
            b = numero(c.get(7))
            espesor = numero(c.get(8))

            if a is not None and b is not None:
                peralte, ancho = max(a, b), min(a, b)

            # Se cotejan contra los nominales en pulgadas de la propia hoja.
            for col_num, col_pulg, que in ((6, 9, "lado 1"), (7, 10, "lado 2"),
                                           (8, 11, "espesor")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    # Un milimetro de tolerancia: la hoja redondea 50.8 a 51.
                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} son {esperado:.1f} mm")

        elif familia == "OC":
            # Tubo redondo: diametro y espesor en milimetros.
            peralte = numero(c.get(12))
            espesor = numero(c.get(13))

            for col_num, col_pulg, que in ((12, 14, "diametro"), (13, 15, "espesor")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} in son {esperado:.1f} mm")

        elif familia == "CF":
            # Lamina doblada: peralte y ancho en milimetros, y aqui 'tdist' SI esta en
            # milimetros -es el calibre-, al contrario que en el tubo.
            peralte = numero(c.get(6))
            ancho = numero(c.get(7))
            espesor = numero(c.get(4))
            labio = numero(c.get(11))
            radio = numero(c.get(12))

            for col_num, col_pulg, que in ((6, 9, "peralte"), (7, 10, "ancho")):
                v = numero(c.get(col_num))
                p = pulgadas(c.get(col_pulg))

                if v is not None and p is not None:
                    esperado = p * MM_POR_PULGADA

                    if abs(v - esperado) > 1.0:
                        avisos.append(
                            f"{nombre}: el {que} dice {v} mm pero su nominal "
                            f"{c.get(col_pulg)} son {esperado:.1f} mm")

        if not peralte or not espesor:
            avisos.append(f"{nombre}: se salta, sin peralte o sin espesor en la hoja")
            continue

        # ------------------------------------------------------------------
        #  Que las medidas sean POSIBLES, no solo que existan
        # ------------------------------------------------------------------
        # Esto caza los errores de dedo de la hoja, que es lo que ninguna otra
        # comprobacion ve: un numero que esta en su celda, es un numero y aun asi no
        # puede ser. El caso real de este archivo es el
        #
        #     W - 36'' x 442.16 lb/ft   ->   alma de 346 mm
        #
        # cuando sus dos vecinos de la tabla tienen 31 y 38.1: alguien escribio 346 en
        # lugar de 34.6. Un perfil asi se dibuja con el alma mas gorda que el patin y
        # en el plano se ve como un borron.
        #
        # Los limites son de proporcion, no de tamaño, asi que valen igual para un
        # perfil de 8 cm y para uno de 2 m: ningun perfil laminado tiene el alma mas
        # gruesa que la sexta parte de su peralte ni el patin mas que un tercio.
        problema = None

        if familia == "IR":
            if espesor > peralte / 6:
                problema = (f"el alma mide {espesor} mm en un peralte de {peralte} mm, "
                            "mas de la sexta parte")
            elif e_patin and e_patin > peralte / 3:
                problema = (f"el patin mide {e_patin} mm en un peralte de {peralte} mm, "
                            "mas de la tercera parte")
            elif ancho and espesor > ancho / 2:
                problema = (f"el alma ({espesor} mm) pasa de la mitad del patin "
                            f"({ancho} mm)")

        elif familia in ("OR", "OC"):
            menor = peralte if familia == "OC" else min(peralte, ancho or peralte)

            if espesor > menor / 4:
                problema = (f"la pared mide {espesor} mm en un lado de {menor} mm, "
                            "mas de la cuarta parte")

        elif familia == "CF":
            if espesor > peralte / 10:
                problema = (f"la lamina mide {espesor} mm en un peralte de "
                            f"{peralte} mm, mas de la decima parte")
            elif labio and labio > peralte / 2:
                problema = (f"el labio ({labio} mm) pasa de la mitad del peralte "
                            f"({peralte} mm)")

        if problema:
            avisos.append(f"{nombre}: SE SALTA, {problema}. Revisa esa celda en la hoja")
            continue

        if familia != "OC" and not ancho:
            avisos.append(f"{nombre}: se salta, sin ancho en la hoja")
            continue

        if familia == "IR" and not e_patin:
            avisos.append(f"{nombre}: se salta, sin espesor de patin en la hoja")
            continue

        if familia == "CF" and (not labio or not radio):
            avisos.append(f"{nombre}: se salta, sin labio o sin radio en la hoja")
            continue

        # Todo estaba en milimetros: a centimetros, que es lo que lee CadLink.
        def cm(v):
            return None if v is None else v / 10

        perfiles.append((
            familia,
            nombre,
            cm(peralte), cm(ancho), cm(espesor), cm(e_patin), cm(labio), cm(radio),
            familia_imca,
        ))

    if not perfiles:
        raise SystemExit("No salio ningun perfil.")

    # ------------------------------------------------------------------
    #  El CSV
    # ------------------------------------------------------------------
    def fmt(v):
        return "" if v is None else f"{v:g}"

    print("# ============================================================================")
    print("#  CATALOGO DE PERFILES DE ACERO DE CADLINK")
    print("# ============================================================================")
    print("#")
    print(f"#  Generado del manual IMCA con tools/catalogo_imca.py")
    print(f"#  {len(perfiles)} perfiles. TODAS LAS MEDIDAS EN CENTIMETROS.")
    print("#")
    print("#  Este archivo llena los desplegables de la pestaña «Secciones Acero»: al")
    print("#  elegir la familia, la celda «Perfil» ofrece los perfiles de esa familia, y")
    print("#  al elegir uno se traen sus medidas solas.")
    print("#")
    print("#  Se puede editar con el Bloc de notas o con Excel, y no hay que recompilar")
    print("#  nada: se guarda, se vuelve a abrir CadLink y los cambios ya estan.")
    print("#")
    print("#  familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio")
    print("#")
    print("#     peralte   en el OC es el DIAMETRO EXTERIOR")
    print("#     ancho     patin en IR y CF, cara en OR. El OC no lo usa")
    print("#     e_alma    espesor del alma en el IR; espesor de PARED en OR, OC y CF")
    print("#     e_patin   solo el IR")
    print("#     labio     solo el CF")
    print("#     radio     solo el CF, el radio de doblez exterior")
    print("#")

    for familia in ("IR", "OR", "OC", "CF"):
        de_esta = [p for p in perfiles if p[0] == familia]

        if not de_esta:
            continue

        origen = sorted({p[8] for p in de_esta})

        print("#")
        print(f"# {'-' * 74}")
        print(f"#  {familia}: {len(de_esta)} perfiles   "
              f"(del IMCA: {', '.join(origen)})")
        print(f"# {'-' * 74}")

        for p in de_esta:
            print(";".join([p[0], p[1], fmt(p[2]), fmt(p[3]), fmt(p[4]),
                            fmt(p[5]), fmt(p[6]), fmt(p[7])]))

    # ------------------------------------------------------------------
    #  El informe, por la salida de errores para no ensuciar el CSV
    # ------------------------------------------------------------------
    print(f"\n{len(perfiles)} perfiles convertidos:", file=sys.stderr)

    for familia in ("IR", "OR", "OC", "CF"):
        de_esta = [p for p in perfiles if p[0] == familia]

        if de_esta:
            por_imca = {}

            for p in de_esta:
                por_imca[p[8]] = por_imca.get(p[8], 0) + 1

            detalle = ", ".join(f"{n} {k}" for k, n in sorted(por_imca.items()))
            print(f"   {familia}: {len(de_esta):4}   ({detalle})", file=sys.stderr)

    if fuera:
        total = sum(fuera.values())
        print(f"\n{total} perfiles FUERA, de familias que el dibujante no sabe hacer:",
              file=sys.stderr)

        for k, n in sorted(fuera.items(), key=lambda x: -x[1]):
            motivo = SIN_FORMA.get(k, "familia desconocida en la hoja")
            print(f"   {k:5} {n:4}   {motivo}", file=sys.stderr)

    if avisos:
        print(f"\n{len(avisos)} aviso(s) al cotejar las medidas con sus nominales:",
              file=sys.stderr)

        for a in avisos[:40]:
            print("   - " + a, file=sys.stderr)

        if len(avisos) > 40:
            print(f"   ... y {len(avisos) - 40} mas", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
