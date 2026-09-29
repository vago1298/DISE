#!/usr/bin/env python3
"""
Revisa un archivo .ifc ya escrito: sintaxis de STEP, referencias y CUENTAS DE ATRIBUTOS.

Por que existe
--------------
Un IFC mal formado no se queja. Si una entidad lleva un atributo de mas o de menos, el
archivo se escribe igual, pesa lo mismo y tiene el mismo aspecto; lo que pasa es que el
visor lo abre VACIO, o peor, abre solo una parte. No hay mensaje de error que seguir.

La causa mas comun es contar mal los atributos, y contarlos mal es facil porque casi todos
son heredados: un IfcColumn tiene nueve atributos y solo UNO es propio, los otros ocho
vienen de cinco entidades por encima. La cuenta no se puede deducir mirando la
documentacion de IfcColumn.

Asi que la tabla CUENTAS de aqui abajo no se escribio a mano: se saco del esquema EXPRESS
oficial de buildingSMART (IFC4_ADD2_TC1.exp), resolviendo la cadena de herencia de cada
entidad y descartando los atributos INVERSE, que no se escriben en el archivo.

Que se comprueba
----------------
  * la envoltura: ISO-10303-21, HEADER, FILE_SCHEMA, DATA, ENDSEC, END-ISO-10303-21
  * cada entidad tiene la forma  #n=TIPO(...);  y ningun numero se repite
  * TODA referencia #n apunta a una entidad que existe
  * cada entidad tiene EXACTAMENTE los atributos que dice el esquema
  * los reales llevan punto decimal, y nunca coma ni exponente
  * el archivo es ASCII: los acentos van escapados en \\X2\\...\\X0\\, no en UTF-8
  * el esqueleto obligatorio: proyecto, terreno, edificio, niveles, unidades y contexto
  * cada pieza cuelga de un nivel y tiene un tipo
  * los contornos de muro y losa estan cerrados

Como se corre
-------------
    python3 tools/verificar_ifc.py
    python3 tools/verificar_ifc.py ruta/a/otro.ifc

Sin argumento toma  tools/prueba-ifc/salida/modelo-de-prueba.ifc,  que es el que deja
`dotnet run` en tools/prueba-ifc. Los dos se complementan: la prueba de C# comprueba la
ARITMETICA -donde queda cada pieza- y esto la SINTAXIS y el esquema.

Devuelve 0 si todo pasa y 1 si algo falla.
"""

import os
import re
import sys

# ======================================================================
#  Cuentas de atributos, del esquema IFC4_ADD2_TC1.exp
#
#  Se resolvio la herencia completa de cada entidad. Los atributos DERIVADOS cuentan
#  -se escriben con asterisco- y los INVERSE no, porque no aparecen en el archivo.
# ======================================================================
CUENTAS = {
    "IFCPROJECT": 9,
    "IFCSITE": 14,
    "IFCBUILDING": 12,
    "IFCBUILDINGSTOREY": 10,
    "IFCCOLUMN": 9,
    "IFCBEAM": 9,
    "IFCMEMBER": 9,
    "IFCSLAB": 9,
    "IFCWALL": 9,
    "IFCCOLUMNTYPE": 10,
    "IFCBEAMTYPE": 10,
    "IFCMEMBERTYPE": 10,
    "IFCSLABTYPE": 10,
    "IFCWALLTYPE": 10,
    "IFCRECTANGLEPROFILEDEF": 5,
    "IFCCIRCLEPROFILEDEF": 4,
    "IFCCIRCLEHOLLOWPROFILEDEF": 5,
    "IFCRECTANGLEHOLLOWPROFILEDEF": 8,
    "IFCISHAPEPROFILEDEF": 10,
    "IFCUSHAPEPROFILEDEF": 10,
    "IFCTSHAPEPROFILEDEF": 12,
    "IFCLSHAPEPROFILEDEF": 9,
    "IFCARBITRARYCLOSEDPROFILEDEF": 3,
    "IFCEXTRUDEDAREASOLID": 4,
    "IFCSHAPEREPRESENTATION": 4,
    "IFCPRODUCTDEFINITIONSHAPE": 3,
    "IFCLOCALPLACEMENT": 2,
    "IFCAXIS2PLACEMENT3D": 3,
    "IFCAXIS2PLACEMENT2D": 2,
    "IFCCARTESIANPOINT": 1,
    "IFCDIRECTION": 1,
    "IFCPOLYLINE": 1,
    "IFCOWNERHISTORY": 8,
    "IFCPERSON": 8,
    "IFCORGANIZATION": 5,
    "IFCPERSONANDORGANIZATION": 3,
    "IFCAPPLICATION": 4,
    "IFCSIUNIT": 4,
    "IFCUNITASSIGNMENT": 1,
    "IFCGEOMETRICREPRESENTATIONCONTEXT": 6,
    "IFCGEOMETRICREPRESENTATIONSUBCONTEXT": 10,
    "IFCRELAGGREGATES": 6,
    "IFCRELCONTAINEDINSPATIALSTRUCTURE": 6,
    "IFCRELDEFINESBYTYPE": 6,
    "IFCRELDEFINESBYPROPERTIES": 6,
    "IFCRELASSOCIATESMATERIAL": 6,
    "IFCMATERIAL": 3,
    "IFCMATERIALPROFILE": 6,
    "IFCMATERIALPROFILESET": 4,
    "IFCPROPERTYSET": 5,
    "IFCPROPERTYSINGLEVALUE": 4,
}

# Los elementos que tienen que colgar de un nivel y tener tipo.
PIEZAS = {"IFCCOLUMN", "IFCBEAM", "IFCMEMBER", "IFCSLAB", "IFCWALL"}

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
POR_OMISION = os.path.join(RAIZ, "tools", "prueba-ifc", "salida", "modelo-de-prueba.ifc")

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + str(detalle)))
    if not ok:
        fallos.append(nombre)


# ======================================================================
#  Lector de STEP
# ======================================================================
def partir(cuerpo):
    """Parte el cuerpo en entidades por el punto y coma, respetando los apostrofos."""
    trozos = []
    actual = []
    en_texto = False
    i = 0
    while i < len(cuerpo):
        c = cuerpo[i]
        if c == "'":
            if en_texto and i + 1 < len(cuerpo) and cuerpo[i + 1] == "'":
                actual.append("''")
                i += 2
                continue
            en_texto = not en_texto
            actual.append(c)
        elif c == ";" and not en_texto:
            trozos.append("".join(actual))
            actual = []
        else:
            actual.append(c)
        i += 1
    if "".join(actual).strip():
        trozos.append("".join(actual))
    return trozos


def argumentos(dentro):
    """Separa los argumentos de primer nivel."""
    salida = []
    actual = []
    hondo = 0
    en_texto = False
    i = 0
    while i < len(dentro):
        c = dentro[i]
        if c == "'":
            if en_texto and i + 1 < len(dentro) and dentro[i + 1] == "'":
                actual.append("''")
                i += 2
                continue
            en_texto = not en_texto
            actual.append(c)
        elif not en_texto and c == "(":
            hondo += 1
            actual.append(c)
        elif not en_texto and c == ")":
            hondo -= 1
            actual.append(c)
        elif not en_texto and c == "," and hondo == 0:
            salida.append("".join(actual).strip())
            actual = []
        else:
            actual.append(c)
        i += 1
    if actual or salida:
        salida.append("".join(actual).strip())
    return salida


ENTIDAD = re.compile(r"^#(\d+)\s*=\s*([A-Z0-9_]+)\s*\((.*)\)$", re.S)


def leer(ruta):
    crudo = open(ruta, "rb").read()
    texto = crudo.decode("utf-8", errors="replace")

    print("\n[1] Envoltura del archivo")
    check("abre con ISO-10303-21", texto.startswith("ISO-10303-21;"))
    check("cierra con END-ISO-10303-21", texto.rstrip().endswith("END-ISO-10303-21;"))
    check("tiene seccion HEADER", "HEADER;" in texto)
    check("tiene seccion DATA", "DATA;" in texto)
    check("declara FILE_SCHEMA", "FILE_SCHEMA((" in texto)

    m = re.search(r"FILE_SCHEMA\(\('([^']+)'\)\)", texto)
    check("el esquema es IFC4", bool(m) and m.group(1) == "IFC4",
          m.group(1) if m else "no aparece")

    # El archivo tiene que ser ASCII. Si hay un byte alto, el escapado \X2\ no se aplico y
    # los acentos llegaran a Revit como basura.
    altos = [(i, b) for i, b in enumerate(crudo) if b > 0x7E]
    check("el archivo es ASCII (los acentos van escapados)", not altos,
          f"{len(altos)} byte(s) altos, el primero en la posicion {altos[0][0]}" if altos else "")

    check("sin BOM", not crudo.startswith(b"\xef\xbb\xbf"))

    cuerpo = texto.split("DATA;", 1)[1].rsplit("ENDSEC;", 1)[0]

    entidades = {}
    repetidos = []
    malformadas = []

    for trozo in partir(cuerpo):
        linea = trozo.strip()
        if not linea:
            continue
        m = ENTIDAD.match(linea)
        if not m:
            malformadas.append(linea[:70])
            continue
        n = int(m.group(1))
        if n in entidades:
            repetidos.append(n)
        entidades[n] = (m.group(2), argumentos(m.group(3)))

    print("\n[2] Forma de cada entidad")
    check("todas tienen la forma #n=TIPO(...)", not malformadas,
          "; ".join(malformadas[:3]))
    check("ningun numero de entidad se repite", not repetidos, repetidos[:5])
    check("hay entidades", len(entidades) > 0, len(entidades))
    print(f"        {len(entidades)} entidades")

    return texto, entidades


# ======================================================================
def referencias(entidades):
    print("\n[3] Referencias")
    sueltas = []
    for n, (tipo, args) in entidades.items():
        for r in re.findall(r"#(\d+)", ",".join(args)):
            if int(r) not in entidades:
                sueltas.append(f"#{n} ({tipo}) apunta a #{r}, que no existe")
    check("toda referencia #n existe", not sueltas, "; ".join(sueltas[:3]))


# ======================================================================
def cuentas(entidades):
    print("\n[4] Cuentas de atributos contra el esquema IFC4")

    malas = []
    desconocidas = set()

    for n, (tipo, args) in entidades.items():
        if tipo not in CUENTAS:
            desconocidas.add(tipo)
            continue
        if len(args) != CUENTAS[tipo]:
            malas.append(f"#{n} {tipo} tiene {len(args)} y el esquema pide {CUENTAS[tipo]}")

    check("cada entidad tiene los atributos que pide el esquema", not malas,
          "; ".join(malas[:5]))

    # Las entidades que no estan en la tabla no se pueden comprobar. No es un fallo, pero
    # conviene saberlo: si aparece una nueva hay que anadirla a CUENTAS.
    if desconocidas:
        print(f"        sin comprobar (no estan en la tabla): {', '.join(sorted(desconocidas))}")

    usadas = {t for (t, _) in entidades.values()}
    print(f"        {len(usadas & set(CUENTAS))} tipos comprobados de {len(usadas)} usados")


# ======================================================================
def reales(entidades):
    print("\n[5] Reales")

    # En STEP un real SIEMPRE lleva punto: '3' es entero y '3.' es real, y son tipos
    # distintos. Y con coma decimal el argumento se partiria en dos.
    comas = []
    exponentes = []

    for n, (tipo, args) in entidades.items():
        # Se mira fuera de los apostrofos: dentro de un texto todo vale.
        limpio = re.sub(r"'(?:[^']|'')*'", "", ",".join(args))
        if re.search(r"\d,\d", limpio.replace(",", "|")) is None:
            pass
        for t in re.findall(r"[-+]?\d+\.?\d*[eE][-+]?\d+", limpio):
            exponentes.append(f"#{n} {tipo}: {t}")

    check("ningun real en notacion cientifica", not exponentes,
          "; ".join(exponentes[:3]))

    # Que los reales lleven punto se comprueba donde importa: las coordenadas y los largos.
    sin_punto = []
    for n, (tipo, args) in entidades.items():
        if tipo in ("IFCCARTESIANPOINT", "IFCDIRECTION"):
            for v in argumentos(args[0].strip("()")):
                if v and "." not in v:
                    sin_punto.append(f"#{n} {tipo}: {v}")
        elif tipo == "IFCEXTRUDEDAREASOLID":
            if args[3] and "." not in args[3]:
                sin_punto.append(f"#{n} {tipo}: largo {args[3]}")

    check("las coordenadas y los largos llevan punto decimal", not sin_punto,
          "; ".join(sin_punto[:3]))


# ======================================================================
def escapes(texto):
    print("\n[6] Escapado de texto")

    # Una secuencia \X2\ se cierra con \X0\ y lleva grupos de CUATRO digitos hex.
    malas = []
    for m in re.finditer(r"\\X2\\(.*?)\\X0\\", texto, re.S):
        hex_ = m.group(1)
        if len(hex_) % 4 != 0 or not re.fullmatch(r"[0-9A-F]*", hex_):
            malas.append(hex_[:20])

    abiertas = texto.count("\\X2\\")
    cerradas = texto.count("\\X0\\")

    check("cada \\X2\\ se cierra con \\X0\\", abiertas == cerradas,
          f"{abiertas} abiertas y {cerradas} cerradas")
    check("los grupos hex son de cuatro digitos en mayusculas", not malas,
          "; ".join(malas[:3]))

    if abiertas:
        print(f"        {abiertas} secuencia(s) de texto no ASCII, escapadas")


# ======================================================================
def esqueleto(entidades):
    print("\n[7] Esqueleto obligatorio")

    def deTipo(t):
        return [n for n, (tipo, _) in entidades.items() if tipo == t]

    proyecto = deTipo("IFCPROJECT")
    sitio = deTipo("IFCSITE")
    edificio = deTipo("IFCBUILDING")
    niveles = deTipo("IFCBUILDINGSTOREY")

    check("hay exactamente un IfcProject", len(proyecto) == 1, len(proyecto))
    check("hay exactamente un IfcSite", len(sitio) == 1, len(sitio))
    check("hay exactamente un IfcBuilding", len(edificio) == 1, len(edificio))
    check("hay al menos un IfcBuildingStorey", len(niveles) >= 1, len(niveles))

    check("el proyecto declara unidades",
          bool(proyecto) and entidades[proyecto[0]][1][8].startswith("#"),
          entidades[proyecto[0]][1][8] if proyecto else "")

    check("el proyecto declara contexto geometrico",
          bool(proyecto) and "#" in entidades[proyecto[0]][1][7],
          entidades[proyecto[0]][1][7] if proyecto else "")

    check("hay asignacion de unidades", bool(deTipo("IFCUNITASSIGNMENT")))
    check("hay contexto geometrico", bool(deTipo("IFCGEOMETRICREPRESENTATIONCONTEXT")))

    # El metro: si las unidades no son metros, todo el modelo sale a otra escala.
    metros = [n for n, (t, a) in entidades.items()
              if t == "IFCSIUNIT" and ".LENGTHUNIT." in a[1] and ".METRE." in a[3]]
    check("la unidad de longitud es el METRO", len(metros) == 1, len(metros))

    # La jerarquia proyecto -> sitio -> edificio -> niveles, por IfcRelAggregates.
    agrega = {}
    for n, (t, a) in entidades.items():
        if t == "IFCRELAGGREGATES":
            padre = a[4]
            hijos = re.findall(r"#(\d+)", a[5])
            agrega.setdefault(padre, []).extend(int(h) for h in hijos)

    check("el proyecto agrega el terreno",
          bool(proyecto) and f"#{proyecto[0]}" in agrega
          and sitio and sitio[0] in agrega[f"#{proyecto[0]}"],
          str(agrega))
    check("el terreno agrega el edificio",
          bool(sitio) and f"#{sitio[0]}" in agrega
          and edificio and edificio[0] in agrega[f"#{sitio[0]}"])
    check("el edificio agrega los niveles",
          bool(edificio) and f"#{edificio[0]}" in agrega
          and set(niveles) <= set(agrega[f"#{edificio[0]}"]))


# ======================================================================
def piezas(entidades):
    print("\n[8] Las piezas")

    lasPiezas = {n for n, (t, _) in entidades.items() if t in PIEZAS}

    if not lasPiezas:
        check("hay piezas que revisar", False, "no hay ninguna columna, trabe, muro ni losa")
        return

    print(f"        {len(lasPiezas)} pieza(s)")

    # Cada pieza colgada de un nivel
    contenidas = set()
    for n, (t, a) in entidades.items():
        if t == "IFCRELCONTAINEDINSPATIALSTRUCTURE":
            contenidas |= {int(x) for x in re.findall(r"#(\d+)", a[4])}

    sueltas = lasPiezas - contenidas
    check("toda pieza cuelga de un nivel", not sueltas,
          f"{len(sueltas)} sin nivel: {sorted(sueltas)[:5]}")

    # Cada pieza con tipo
    conTipo = set()
    for n, (t, a) in entidades.items():
        if t == "IFCRELDEFINESBYTYPE":
            conTipo |= {int(x) for x in re.findall(r"#(\d+)", a[4])}

    sinTipo = lasPiezas - conTipo
    check("toda pieza tiene tipo, que es lo que Revit convierte en familia", not sinTipo,
          f"{len(sinTipo)} sin tipo: {sorted(sinTipo)[:5]}")

    # Cada pieza con geometria y colocacion
    sinForma = [n for n in lasPiezas if not entidades[n][1][6].startswith("#")]
    sinLugar = [n for n in lasPiezas if not entidades[n][1][5].startswith("#")]

    check("toda pieza tiene representacion geometrica", not sinForma, sinForma[:5])
    check("toda pieza tiene colocacion", not sinLugar, sinLugar[:5])

    # El nombre de la seccion en ObjectType, que es de donde Revit saca el nombre del tipo
    sinObjectType = [n for n in lasPiezas if entidades[n][1][4] == "$"]
    check("toda pieza dice su seccion en ObjectType", not sinObjectType,
          sinObjectType[:5])

    # Los GlobalId: 22 caracteres del alfabeto de IFC, y sin repetir
    alfabeto = set("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$")
    vistos = {}
    malos = []
    repes = []

    for n, (t, a) in entidades.items():
        if t not in CUENTAS or not a or not a[0].startswith("'"):
            continue
        # Solo las entidades que heredan de IfcRoot llevan GlobalId de primero.
        if not (t.startswith("IFCREL") or t in PIEZAS
                or t in {"IFCPROJECT", "IFCSITE", "IFCBUILDING", "IFCBUILDINGSTOREY",
                         "IFCPROPERTYSET"} or t.endswith("TYPE")):
            continue
        g = a[0].strip("'")
        if len(g) != 22 or not set(g) <= alfabeto:
            malos.append(f"#{n} {t}: {g}")
        if g in vistos:
            repes.append(f"{g} en #{vistos[g]} y #{n}")
        vistos[g] = n

    check("los GlobalId miden 22 y usan el alfabeto de IFC", not malos, "; ".join(malos[:3]))
    check("ningun GlobalId se repite", not repes, "; ".join(repes[:3]))
    print(f"        {len(vistos)} GlobalId distintos")


# ======================================================================
def contornos(entidades):
    print("\n[9] Contornos de muro y losa")

    lineas = [(n, a) for n, (t, a) in entidades.items() if t == "IFCPOLYLINE"]

    if not lineas:
        print("        no hay ninguno en este archivo")
        return

    abiertos = []
    cortos = []

    for n, a in lineas:
        pts = argumentos(a[0].strip("()"))
        if len(pts) < 4:
            cortos.append(f"#{n} con {len(pts)} puntos")
        elif pts[0] != pts[-1]:
            abiertos.append(f"#{n}: empieza en {pts[0]} y acaba en {pts[-1]}")

    check("todo contorno se cierra repitiendo su primer punto", not abiertos,
          "; ".join(abiertos[:3]))
    check("todo contorno tiene al menos tres puntos mas el cierre", not cortos,
          "; ".join(cortos[:3]))
    print(f"        {len(lineas)} contorno(s)")


# ======================================================================
def main():
    ruta = sys.argv[1] if len(sys.argv) > 1 else POR_OMISION

    print("=" * 78)
    print(" Verificacion de un archivo IFC")
    print("=" * 78)
    print(f" archivo: {ruta}")

    if not os.path.exists(ruta):
        print()
        print(" NO EXISTE.")
        print(" Generalo primero con:")
        print("     cd tools/prueba-ifc && dotnet run")
        print("=" * 78)
        return 1

    texto, entidades = leer(ruta)
    referencias(entidades)
    cuentas(entidades)
    reales(entidades)
    escapes(texto)
    esqueleto(entidades)
    piezas(entidades)
    contornos(entidades)

    print()
    print("=" * 78)
    if fallos:
        print(f" RESULTADO: {len(fallos)} comprobacion(es) fallaron")
        for f in fallos:
            print("   - " + f)
        print("=" * 78)
        return 1
    print(" RESULTADO: el archivo IFC pasa todas las comprobaciones")
    print("=" * 78)
    return 0


if __name__ == "__main__":
    sys.exit(main())
