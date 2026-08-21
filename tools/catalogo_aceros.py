"""Convierte ACEROS.xlsx en el aceros.csv de CadLink.

    python3 tools/catalogo_aceros.py docs/ACEROS.xlsx > client/src/CadLink.App/aceros.csv

QUE TRAE LA HOJA
----------------
Una fila por acero -39 en total- con tres bloques:

    DESIGNACION          col C = ASTM,  col D = NMX
    PROPIEDADES MECANICAS  col F = Fy kg/cm2,  col G = Fy MPa
                           col H = Fu kg/cm2,  col I = Fu MPa
    DISPONIBILIDAD       una columna por tipo de seccion, de la J a la V, con SI,
                         VERIFICAR o '-'

Y la columna B trae el GRUPO -CARBON, ALTA RESISTENCIA Y BAJA ALEACION, RESISTENTE A
CORROSION, TEMPLADO REVENIDO-, escrito una sola vez en la primera fila de cada grupo y
vacio en las demas, como se escribe una tabla en Excel. Aqui se arrastra hacia abajo, para
que cada acero lleve el suyo.

LOS NOMBRES DE LAS COLUMNAS DE DISPONIBILIDAD NO SON LAS FAMILIAS DE CADLINK
---------------------------------------------------------------------------
Y eso hay que traducirlo, porque si no la disponibilidad se pega a la familia equivocada:

    W    -> IR      el perfil I laminado
    IS   -> IS      la I soldada
    IC   -> IC
    S    -> S
    WT   -> WT
    C    -> C
    L    -> L
    CF   -> CF      la canal formada en frio, el monten
    ZF   -> ZF
    PIPE -> OC      el tubo REDONDO
    HSS  -> OR      el tubo RECTANGULAR
    OS   -> OS      el redondo macizo

La diferencia entre PIPE y HSS no es un capricho de la hoja, y se ve en el A-500: el
Gr. B trae 2955 kg/cm2 en la columna PIPE y el Gr. B' trae 3235 en la columna HSS. Es la
misma norma con dos Fy segun la forma del tubo -42 ksi en redondo y 46 en rectangular-, asi
que cambiar una columna por la otra da un Fy equivocado en un 9 %.

LA COLUMNA PLACA SE GUARDA, PERO NO ES UNA FAMILIA
--------------------------------------------------
La placa no es un perfil y CadLink no la dibuja. Se guarda en el CSV porque es lo que dice
la hoja y algun dia hara falta para las conexiones, pero no cuenta como disponibilidad de
ningun perfil.

LAS TRES RESPUESTAS DE LA DISPONIBILIDAD
----------------------------------------
    SI         se consigue en esa seccion
    VERIFICAR  puede conseguirse, hay que confirmarlo con el proveedor
    NO         (el '-' de la hoja) no se hace en esa seccion

VERIFICAR **no** es NO, y por eso son tres y no dos: marcar en rojo un acero que si se puede
conseguir hace que el usuario cambie de acero sin necesidad, y darlo por bueno en silencio
lo deja creyendo que esta confirmado. La tabla los distingue.
"""

import re
import sys
import xml.etree.ElementTree as ET
import zipfile

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

# La columna de la hoja -> la familia de CadLink. El orden es el de FamiliaPerfil.Todas,
# para que el CSV salga en el mismo orden que los desplegables del programa.
COLUMNA_DE_FAMILIA = (
    ("IR", "W"),
    ("IS", "IS"),
    ("IC", "IC"),
    ("S", "S"),
    ("WT", "WT"),
    ("C", "C"),
    ("CF", "CF"),
    ("ZF", "ZF"),
    ("L", "L"),
    ("OR", "HSS"),
    ("OC", "PIPE"),
    ("OS", "OS"),
)

FAMILIAS = tuple(f for f, _ in COLUMNA_DE_FAMILIA)

# Los grupos de la columna B, tal como los escribe la hoja.
GRUPOS = (
    "CARBÓN",
    "ALTA RESISTENCIA Y BAJA ALEACIÓN",
    "RESISTENTE A CORROSIÓN",
    "TEMPLADO REVENIDO",
)


def celdas(ruta):
    """La hoja como {numero de fila: {letra de columna: texto}}."""
    z = zipfile.ZipFile(ruta)

    compartidas = [
        "".join(t.text or "" for t in si.iter(NS + "t"))
        for si in ET.fromstring(z.read("xl/sharedStrings.xml")).findall(NS + "si")]

    hoja = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
    filas = {}

    for fila in hoja.iter(NS + "row"):
        n = int(fila.get("r"))
        cs = {}

        for c in fila.findall(NS + "c"):
            v = c.find(NS + "v")
            tipo = c.get("t")

            if tipo == "s":
                texto = compartidas[int(v.text)] if v is not None else ""
            elif tipo == "inlineStr":
                dentro = c.find(NS + "is")
                texto = ("".join(t.text or "" for t in dentro.iter(NS + "t"))
                         if dentro is not None else "")
            else:
                texto = v.text if v is not None else ""

            cs[re.match(r"([A-Z]+)", c.get("r")).group(1)] = (texto or "").strip()

        filas[n] = cs

    return filas


def numero(texto):
    """El numero de una celda, o None si no hay."""
    t = (texto or "").replace(",", "").strip()

    if not t or t == "-":
        return None

    try:
        return float(t)
    except ValueError:
        return None


def disponibilidad(texto):
    """SI, VERIFICAR o NO, a partir de lo que diga la celda."""
    t = (texto or "").strip().upper()

    if t in ("SI", "SÍ", "S", "X"):
        return "SI"

    if t.startswith("VERIF"):
        return "VERIFICAR"

    # El '-' y la celda vacia son lo mismo: no se hace en esa seccion. Se tratan igual a
    # proposito, porque en la hoja hay filas donde el '-' se quedo sin escribir.
    return "NO"


def encabezados(filas):
    """Que letra de columna tiene cada tipo de seccion, leido de la fila 4.

    Se LEE, no se supone: si alguien inserta una columna en la hoja, las letras se corren
    y una tabla de letras escrita a mano empezaria a pegar la disponibilidad a la familia
    de al lado sin que nada avisara.
    """
    for n in sorted(filas):
        fila = filas[n]

        if fila.get("F") == "Fy" and fila.get("H") == "Fu":
            return {v.upper(): k for k, v in fila.items() if v}

    return {}


def leer(ruta):
    """Los aceros de la hoja, ya con su grupo arrastrado y su disponibilidad traducida."""
    filas = celdas(ruta)
    cabeza = encabezados(filas)

    faltan = [c for _, c in COLUMNA_DE_FAMILIA if c not in cabeza]

    if faltan:
        raise SystemExit(
            f"ERROR: la hoja no trae las columnas de disponibilidad {faltan}. "
            f"Encabezados leidos: {sorted(cabeza)}")

    col_placa = cabeza.get("PLACA")

    aceros = []
    grupo = ""

    for n in sorted(filas):
        fila = filas[n]
        astm = fila.get("C", "")

        # La fila del grupo puede traer el nombre del grupo y el primer acero a la vez.
        if fila.get("B") in GRUPOS:
            grupo = fila["B"]

        # Un acero de verdad tiene designacion Y Fy. Con eso se saltan los encabezados, las
        # filas en blanco y las dos filas de leyenda del final -«NMX», «ASTM»-.
        fy = numero(fila.get("F"))

        if not astm or astm.upper() in ("ASTM", "DESIGNACIÓN") or fy is None:
            continue

        aceros.append({
            "grupo": grupo,
            "astm": astm,
            "nmx": fila.get("D", "") or "-",
            "fy_kgcm2": fy,
            "fy_mpa": numero(fila.get("G")),
            "fu_kgcm2": numero(fila.get("H")),
            "fu_mpa": numero(fila.get("I")),
            "placa": disponibilidad(fila.get(col_placa)) if col_placa else "NO",
            "disp": {
                familia: disponibilidad(fila.get(cabeza[columna]))
                for familia, columna in COLUMNA_DE_FAMILIA},
        })

    return aceros


def revisar(aceros):
    """Lo que no se puede dar por bueno sin mirarlo. Se AVISA, no se corrige."""
    avisos = []

    for a in aceros:
        nombre = a["astm"]

        # 1. Fu tiene que ser mayor que Fy. Si no, uno de los dos esta mal capturado, y no
        #    hay manera de saber cual.
        if a["fu_kgcm2"] is not None and a["fu_kgcm2"] <= a["fy_kgcm2"]:
            avisos.append(
                f"{nombre}: Fu {a['fu_kgcm2']:.0f} no es mayor que Fy {a['fy_kgcm2']:.0f}")

        # 2. Las dos unidades tienen que decir lo mismo: 1 MPa = 10.1972 kg/cm2. Se acepta
        #    un 3 %, porque la hoja redondea los dos valores por su cuenta.
        for etiqueta, kg, mpa in (
                ("Fy", a["fy_kgcm2"], a["fy_mpa"]), ("Fu", a["fu_kgcm2"], a["fu_mpa"])):
            if kg is None or mpa is None or mpa == 0:
                continue

            razon = kg / (mpa * 10.19716)

            if abs(razon - 1) > 0.03:
                avisos.append(
                    f"{nombre}: {etiqueta} en kg/cm2 y en MPa no cuadran "
                    f"({kg:.0f} contra {mpa:.0f} MPa = {mpa * 10.19716:.0f})")

        # 3. Un acero que no se consigue en NINGUNA seccion ni en placa no sirve de nada en
        #    la tabla: seria una opcion del desplegable que siempre sale en rojo.
        if all(v == "NO" for v in a["disp"].values()) and a["placa"] == "NO":
            avisos.append(f"{nombre}: no se consigue en ninguna seccion ni en placa")

        # 4. Y un Fy fuera de rango es un dedazo: el acero estructural va de 2300 a 7100.
        if not 2000 <= a["fy_kgcm2"] <= 7500:
            avisos.append(
                f"{nombre}: Fy de {a['fy_kgcm2']:.0f} kg/cm2 esta fuera de lo creible")

    return avisos


def main():
    ruta = sys.argv[1] if len(sys.argv) > 1 else "docs/ACEROS.xlsx"
    aceros = leer(ruta)

    if not aceros:
        raise SystemExit(f"ERROR: no se leyo ningun acero de {ruta}")

    avisos = revisar(aceros)

    print("# " + "=" * 74)
    print("#  CATALOGO DE ACEROS ESTRUCTURALES DE CADLINK")
    print("# " + "=" * 74)
    print("#")
    print(f"#  Generado de {ruta} con tools/catalogo_aceros.py")
    print(f"#  {len(aceros)} aceros.")
    print("#")
    print("#  Llena el desplegable «Acero» de la pestaña «Secciones Acero», y de aqui")
    print("#  salen el Fy y el Fu que se ven en la tabla y la marca de si ese acero se")
    print("#  consigue en el perfil que se eligio.")
    print("#")
    print("#  Se puede editar con el Bloc de notas o con Excel, y no hay que recompilar")
    print("#  nada: se guarda, se vuelve a abrir CadLink y los cambios ya estan.")
    print("#")
    print("#  Las columnas:")
    print("#")
    print("#  grupo;astm;nmx;fy_kgcm2;fy_mpa;fu_kgcm2;fu_mpa;" + ";".join(FAMILIAS)
          + ";PLACA")
    print("#")
    print("#     grupo      CARBON, ALTA RESISTENCIA..., RESISTENTE A CORROSION o")
    print("#                TEMPLADO REVENIDO")
    print("#     astm       la designacion, que es lo que se ve en el desplegable")
    print("#     nmx        la norma mexicana equivalente, '-' si no tiene")
    print("#     fy, fu     esfuerzo de fluencia y de ruptura. LAS DOS UNIDADES, y la de")
    print("#                kg/cm2 es la que se muestra")
    print("#")
    print("#  Y una columna por familia de perfil, con tres respuestas:")
    print("#")
    print("#     SI         se consigue en esa seccion")
    print("#     VERIFICAR  puede conseguirse; confirmalo con tu proveedor")
    print("#     NO         no se hace en esa seccion. La fila sale marcada en la tabla")
    print("#")
    print("#  PLACA va al final y NO es una familia de perfil: CadLink no dibuja placas.")
    print("#  Se guarda porque es lo que dice la hoja y hara falta para las conexiones.")
    print("#")

    if avisos:
        print("#  AVISOS de la hoja de origen, que NO se corrigieron aqui:")
        print("#")
        for a in avisos:
            print(f"#     - {a}")
        print("#")

    print("# " + "=" * 74)
    print()

    for a in aceros:
        campos = [
            a["grupo"],
            a["astm"],
            a["nmx"],
            f"{a['fy_kgcm2']:.0f}",
            f"{a['fy_mpa']:.0f}" if a["fy_mpa"] is not None else "",
            f"{a['fu_kgcm2']:.0f}" if a["fu_kgcm2"] is not None else "",
            f"{a['fu_mpa']:.0f}" if a["fu_mpa"] is not None else "",
        ]
        campos += [a["disp"][f] for f in FAMILIAS]
        campos.append(a["placa"])

        print(";".join(campos))

    # El informe va a la SALIDA DE ERROR, no al CSV: asi se puede redirigir el CSV a un
    # archivo y seguir viendo lo que la hoja trae raro.
    print(f"\n{len(aceros)} aceros leidos de {ruta}.", file=sys.stderr)

    for familia in FAMILIAS:
        cuantos = sum(1 for a in aceros if a["disp"][familia] == "SI")
        verificar = sum(1 for a in aceros if a["disp"][familia] == "VERIFICAR")

        print(f"   {familia:3}  {cuantos:2} con SI, {verificar:2} por verificar",
              file=sys.stderr)

    if avisos:
        print(f"\n{len(avisos)} AVISO(S) de la hoja, sin corregir:", file=sys.stderr)
        for a in avisos:
            print("   - " + a, file=sys.stderr)


if __name__ == "__main__":
    main()
