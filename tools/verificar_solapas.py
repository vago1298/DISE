#!/usr/bin/env python3
"""
Comprueba la LOGICA PURA del generador de solapas: port de la macro GenerarSolapas.

No hay .NET aqui, asi que esto es un PORT en Python de client/src/CadLink.Cad/SolapasCad.cs.
Sirve para lo mismo que verificar_placa_base.py: la parte de la macro que se puede equivocar
en silencio se comprueba sin AutoCAD delante, y al final se leen las fuentes en C# para
confirmar que lo que se acaba de probar es lo que de verdad esta escrito.

QUE ES LO QUE SE PUEDE EQUIVOCAR EN SILENCIO AQUI:

  1. LA BUSQUEDA DEL PAPEL. Tres estrategias, un desempate por puntos y un ultimo recurso.
     Cuando falla, AutoCAD NO da error: deja el papel por omision -Carta vertical- y el
     marco, el cajetin y las cotas se dibujan sobre una hoja que no es. La propia macro
     dice que es la causa numero uno de que la orientacion salga mal.

  2. LEER LAS MEDIDAS DEL NOMBRE CANONICO. "ARCH_D_(36.00_x_24.00_Inches)" trae digitos en
     el nombre del tamano -D1, E2, A4- y confundirlos con la medida da un pliego imaginario.

  3. EL TEXTO DE LOS ATRIBUTOS. Un "ING. ING. MIGUEL", un "CED. PROF." solo, un numero de
     plano sin ceros: todo eso sale impreso y no se ve hasta que el juego esta plotado.
"""

import math
import os
import re

fallos = []


def check(nombre, ok, detalle=""):
    if ok:
        print(f"  OK    {nombre}")
    else:
        print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
        fallos.append(nombre)


# ==========================================================================
#  PORT DE Solapas
# ==========================================================================

TITULOS_QUE_SE_QUITAN = ["ING.", "ING", "ARQ.", "ARQ", "M.I.", "DR.", "LIC.", "C."]
PREFIJO_CEDULA = "CED. PROF. "
DIGITOS_DEL_NUMERO = 2
SEPARADOR_DEL_NUMERO = "/"
TOL_MEDIO_MM = 2.0
TOL_PAPEL_MM = 6.0

TAGS_CONOCIDOS = [
    "CALCULISTA", "CEDULA", "PROPIETARIO", "UBICACION", "PROYECTO", "CONTENIDO", "DETALLE",
    "DIBUJO", "FECHA", "ESCALA", "ACOTACION", "CLAVE", "NUMERO", "TOTAL", "TITULO", "TAMANO",
]

CON_ACENTO = "áéíóúñüÁÉÍÓÚÑÜ"
SIN_ACENTO = "aeiounuAEIOUNU"


def sin_acentos(s):
    return "".join(SIN_ACENTO[CON_ACENTO.index(c)] if c in CON_ACENTO else c for c in s or "")


def formatear(s, quitar_acentos=False):
    t = (s or "").strip().upper()
    return sin_acentos(t) if quitar_acentos else t


def normaliza(s):
    t = sin_acentos((s or "").strip().lower())
    return "".join(c for c in t if c not in (" ", "\u00a0", "_", "-"))


def sin_titulo(s):
    t = (s or "").strip()
    for tok in TITULOS_QUE_SE_QUITAN:
        #  El ESPACIO detras es obligatorio: sin el, "Inga Torres" se quedaria en "a Torres".
        if len(t) > len(tok) and t[:len(tok)].upper() == tok and t[len(tok)] == " ":
            return t[len(tok) + 1:].strip()
    return t


def con_ceros(v, digitos):
    if v <= 0:
        return ""
    s = str(v)
    return s if digitos <= 0 else s.rjust(digitos, "0")


def limpiar(s):
    malos = '<>/\\":;?*|,=`'
    t = "".join("-" if c in malos else c for c in s or "").strip()
    if len(t) > 60:
        t = t[:60]
    return t if t else "PLANO"


def nombre_de_layout(sol):
    clave = sol["clave"].strip()
    return limpiar(formatear(clave if clave else sol["titulo"]))


def nombre_libre(nombre, usados, sobrescribir):
    if sobrescribir:
        return nombre
    hay = {u.upper() for u in usados}
    if nombre.upper() not in hay:
        return nombre
    for k in range(1, 1000):
        prueba = f"{nombre}-{k}"
        if prueba.upper() not in hay:
            return prueba
    return nombre


def hoja_orientada(sol):
    a = sol["ancho_pulg"] * 25.4
    b = sol["alto_pulg"] * 25.4
    return (max(a, b), min(a, b)) if sol["horizontal"] else (min(a, b), max(a, b))


def config_pagina_sirve(sol, nombre_config):
    n = normaliza(nombre_config)
    if not n or not sol["tamano"].strip():
        return False
    largo = normaliza(sol["tamano"] + ("Horizontal" if sol["horizontal"] else "Vertical"))
    corto = normaliza(sol["tamano"] + ("H" if sol["horizontal"] else "V"))
    return n in (largo, corto)


def papel_coincide(pw, ph, w, h):
    return (abs(max(pw, ph) - max(w, h)) <= TOL_PAPEL_MM
            and abs(min(pw, ph) - min(w, h)) <= TOL_PAPEL_MM)


def extraer_numeros(s):
    salida = []
    tok = ""
    for c in (s or "") + " ":
        if c.isdigit() or c == ".":
            tok += c
        else:
            if tok:
                try:
                    salida.append(float(tok))
                except ValueError:
                    pass
                tok = ""
    return salida


def medidas_del_nombre(nombre):
    """Las medidas que trae el nombre canonico, EN MM. None si no las trae."""
    s = nombre or ""
    p1 = s.rfind("(")
    p2 = s.rfind(")")
    if p1 >= 0 and p2 > p1:
        s = s[p1 + 1:p2]

    es_mm = "mm" in s.lower()
    nums = extraer_numeros(s)
    if len(nums) < 2:
        return None

    #  LOS DOS ULTIMOS numeros, y de dentro del ULTIMO parentesis: el nombre del tamano
    #  trae digitos -D1, E2, A4- y tomarlos como medida da un pliego que no existe.
    w, h = nums[-2], nums[-1]
    if not es_mm:
        w *= 25.4
        h *= 25.4
    return (w, h) if w > 0 and h > 0 else None


def cerca(x, y):
    return abs(x - y) <= TOL_MEDIO_MM


def primera_palabra(s):
    t = (s or "").strip()
    p = t.find(" ")
    return t[:p] if p > 0 else t


def buscar_papel(medios, sol, preferir_expand=True, usar_full_bleed=False,
                 usar_mas_grande=True):
    """Devuelve (nombre, rotacion, cabe) o None."""
    a_mm, b_mm = hoja_orientada(sol)
    pedido = normaliza(sol["tamano"])
    familia = normaliza(primera_palabra(sol["tamano"]))

    mejor = None
    mejor_punto = -1
    mejor_rot = 0

    cabe = None
    cabe_area = 0.0
    cabe_rot = 0

    for nm in medios:
        if not nm or not nm.strip():
            continue

        nm_n = normaliza(nm)
        es_full = "fullbleed" in nm_n

        #  Ni como acierto ni como respaldo: un full bleed que el plotter no puede honrar
        #  recorta el plano por los cuatro lados.
        if es_full and not usar_full_bleed:
            continue

        punto = -1
        rot = 0
        med = medidas_del_nombre(nm)

        if med is not None:
            mw, mh = med
            if cerca(mw, a_mm) and cerca(mh, b_mm):
                punto, rot = 300, 0
            elif cerca(mw, b_mm) and cerca(mh, a_mm):
                punto, rot = 200, 1

        if punto < 0 and pedido:
            if nm_n == pedido:
                punto, rot = 100, -1
            elif (nm_n.startswith(pedido) and len(nm_n) > len(pedido)
                  and nm_n[len(pedido)] == "("):
                #  EL PARENTESIS ES OBLIGATORIO: sin el, "archd" pescaria "archd1", que es
                #  otro pliego -24x36 contra 26x38- y el plano sale en la hoja de al lado.
                punto, rot = 100, -1

        if punto > 0:
            if familia and nm_n.startswith(familia):
                punto += 5
            if es_full:
                punto += 20
            elif "expand" in nm_n:
                punto += 10 if preferir_expand else -10

            if punto > mejor_punto:
                mejor_punto, mejor, mejor_rot = punto, nm, rot
            continue

        if not usar_mas_grande or med is None:
            continue

        mw, mh = med
        rot_cabe = -2
        if mw >= a_mm - TOL_MEDIO_MM and mh >= b_mm - TOL_MEDIO_MM:
            rot_cabe = 0
        elif mh >= a_mm - TOL_MEDIO_MM and mw >= b_mm - TOL_MEDIO_MM:
            rot_cabe = 1
        if rot_cabe < 0:
            continue

        area = mw * mh
        if familia and nm_n.startswith(familia):
            area *= 0.999
        if cabe is None or area < cabe_area:
            cabe, cabe_area, cabe_rot = nm, area, rot_cabe

    if mejor is not None:
        return (mejor, mejor_rot, True)
    return None if cabe is None else (cabe, cabe_rot, False)


def nombre_corto_del_papel(canonico):
    s = canonico or ""
    p = s.find("(")
    if p > 0:
        s = s[:p]
    return s.replace("_", " ").strip()


def escala_para_caber(bw, bh, w, h, margen=0.0, solo_reducir=False):
    if bw <= 0 or bh <= 0:
        return 1.0
    s = min((w - 2 * margen) / bw, (h - 2 * margen) / bh)
    if s <= 0:
        return 1.0
    return 1.0 if (solo_reducir and s > 1) else s


def texto_del_numero(sol):
    n = con_ceros(sol["numero"], DIGITOS_DEL_NUMERO)
    if sol["total"] > 0:
        return n + SEPARADOR_DEL_NUMERO + con_ceros(sol["total"], DIGITOS_DEL_NUMERO)
    return n


def texto_de_atributo(sol, tag):
    """None = NO TOCAR ese atributo. Cadena vacia = el dato esta en blanco."""
    t = (tag or "").strip().upper()

    if t == "CALCULISTA":
        return sin_titulo(sol["calculista"])
    if t == "CEDULA":
        c = sol["cedula"].strip()
        #  EL PREFIJO SOLO SI HAY NUMERO: con la celda vacia deja un "CED. PROF." solo en
        #  el cajetin, que se lee como un dato que se perdio.
        return "" if not c else PREFIJO_CEDULA + c
    if t == "PROPIETARIO":
        return sol["propietario"]
    if t == "UBICACION":
        return sol["ubicacion"]
    if t == "PROYECTO":
        return sol["proyecto"]
    if t == "CONTENIDO":
        return sol["contenido"]
    if t == "DETALLE":
        return sol["detalle"]
    if t == "DIBUJO":
        return sol["dibujo"]
    if t == "FECHA":
        return sol["fecha"]
    if t == "ESCALA":
        return sol["escala"]
    if t == "ACOTACION":
        return sol["acotacion"]
    if t == "CLAVE":
        return sol["clave"]
    if t == "NUMERO":
        return texto_del_numero(sol)
    if t == "TOTAL":
        return con_ceros(sol["total"], DIGITOS_DEL_NUMERO)
    if t == "TITULO":
        return sol["titulo"]
    if t == "TAMANO":
        return sol["tamano"]

    return None


def es_tag_conocido(tag):
    return (tag or "").strip().upper() in TAGS_CONOCIDOS


def falta_de(sol):
    f = []
    if not sol["titulo"].strip() and not sol["clave"].strip():
        f.append("la clave o el titulo del plano")
    if sol["ancho_pulg"] <= 0 or sol["alto_pulg"] <= 0:
        f.append("el tamano de la hoja")
    return f


def solapa(**kw):
    s = {
        "titulo": "", "tamano": "", "ancho_pulg": 0.0, "alto_pulg": 0.0, "horizontal": True,
        "calculista": "", "cedula": "", "propietario": "", "ubicacion": "", "proyecto": "",
        "dibujo": "", "fecha": "", "acotacion": "", "contenido": "", "detalle": "",
        "escala": "", "clave": "", "numero": 0, "total": 0,
    }
    s.update(kw)
    return s


# ==========================================================================
print("=" * 78)
print("EL TEXTO QUE SALE IMPRESO EN EL CAJETIN")
print("=" * 78)
#  Todo lo de aqui se ve en el plano y no antes. Un "ING. ING. MIGUEL" o un numero de
#  plano sin ceros no rompe nada: sale plotado y hay que volver a generar el juego.

PLANO = solapa(
    titulo="CIMENTACION", tamano="ARCH D", ancho_pulg=24, alto_pulg=36, horizontal=True,
    calculista="Ing. Miguel Angel Ortiz", cedula="1234567",
    propietario="Constructora del Bajio", ubicacion="Leon, Gto.",
    proyecto="Nave industrial", dibujo="MAOB", fecha="AGOSTO DE 2026",
    acotacion="cm", contenido="Planta de cimentacion", detalle="Zapatas Z-1 a Z-4",
    escala="1:50", clave="E-01", numero=1, total=4)

check('el "Ing." del calculista se quita: el cajetin ya lo trae dibujado',
      texto_de_atributo(PLANO, "CALCULISTA") == "Miguel Angel Ortiz",
      repr(texto_de_atributo(PLANO, "CALCULISTA")))

check("y se quitan los demas titulos, con punto y sin punto",
      sin_titulo("ARQ. Ana") == "Ana" and sin_titulo("Arq Ana") == "Ana"
      and sin_titulo("M.I. Ana") == "Ana" and sin_titulo("Dr. Ana") == "Ana"
      and sin_titulo("Lic. Ana") == "Ana" and sin_titulo("C. Ana") == "Ana")

#  PRUEBA NEGATIVA del orden de la lista: si "ING" se probara antes que "ING.", un
#  "Ing. Miguel" perderia solo las tres letras y quedaria ". Miguel".
check('PRUEBA NEGATIVA: con "ING" antes que "ING." quedaria ". Miguel"',
      TITULOS_QUE_SE_QUITAN.index("ING.") < TITULOS_QUE_SE_QUITAN.index("ING")
      and sin_titulo("Ing. Miguel") == "Miguel")

#  Y NO SE MUERDE un nombre que empiece igual. Es lo que exige el espacio detras.
check("un nombre que empieza como un titulo no se toca",
      sin_titulo("Inga Torres") == "Inga Torres"
      and sin_titulo("Ingrid Solis") == "Ingrid Solis"
      and sin_titulo("Cesar Lopez") == "Cesar Lopez",
      f'{sin_titulo("Inga Torres")!r} / {sin_titulo("Ingrid Solis")!r} / '
      f'{sin_titulo("Cesar Lopez")!r}')

check("la cedula lleva su prefijo",
      texto_de_atributo(PLANO, "CEDULA") == "CED. PROF. 1234567")

#  SIN NUMERO NO HAY PREFIJO. Un "CED. PROF." solo en el cajetin se lee como un dato que
#  el programa perdio, no como una celda que no se lleno.
check("y sin numero de cedula NO se pone el prefijo solo",
      texto_de_atributo(solapa(cedula=""), "CEDULA") == ""
      and texto_de_atributo(solapa(cedula="   "), "CEDULA") == "")

check("el numero del plano lleva ceros y su total: 01/04",
      texto_de_atributo(PLANO, "NUMERO") == "01/04",
      repr(texto_de_atributo(PLANO, "NUMERO")))

check("y con dos digitos no se le agregan ceros de mas",
      texto_del_numero(solapa(numero=10, total=12)) == "10/12")

#  El cajetin puede traer el "/" dibujado y dos recuadros: entonces se usan NUMERO y
#  TOTAL por separado, y TOTAL se llena igual. Las dos formas funcionan sin configurar.
check("el atributo TOTAL se llena aparte, para el cajetin que trae el / dibujado",
      texto_de_atributo(PLANO, "TOTAL") == "04")

check("sin total, el numero sale solo",
      texto_del_numero(solapa(numero=3, total=0)) == "03")

#  ---- NULL Y VACIO SON DOS COSAS DISTINTAS ----
#  Es lo que la macro resolvia con vbNullChar. Un atributo que el programa no maneja se
#  DEJA COMO ESTA: puede tener un dato que el dibujante puso a mano, y borrarselo en cada
#  corrida seria peor que no llenar nada.
check("un atributo que el programa no maneja devuelve None, no vacio",
      texto_de_atributo(PLANO, "REVISO") is None
      and texto_de_atributo(PLANO, "") is None
      and texto_de_atributo(PLANO, None) is None)

check("y un dato en blanco SI devuelve vacio, para borrarlo del cajetin",
      texto_de_atributo(solapa(), "PROPIETARIO") == "")

check("los dieciseis atributos de la macro se reconocen todos",
      all(texto_de_atributo(PLANO, t) is not None for t in TAGS_CONOCIDOS),
      "; ".join(t for t in TAGS_CONOCIDOS if texto_de_atributo(PLANO, t) is None))

check("y el tag se reconoce en minusculas y con espacios",
      texto_de_atributo(PLANO, "  clave  ") == "E-01"
      and es_tag_conocido(" cedula ") and not es_tag_conocido("REVISO"))

#  ---- CONTENIDO Y DETALLE SON DOS ATRIBUTOS ----
#  Juntarlos en un renglon desborda el recuadro en cuanto el plano lleva tres cosas, y en
#  el cajetin eso no se arregla sin volver a generar el plano.
check("contenido y detalle van a atributos DISTINTOS",
      texto_de_atributo(PLANO, "CONTENIDO") == "Planta de cimentacion"
      and texto_de_atributo(PLANO, "DETALLE") == "Zapatas Z-1 a Z-4")

#  ---- MAYUSCULAS AL ESCRIBIR, NO AL CAPTURAR ----
check("el formato final es MAYUSCULAS, y se aplica al escribir",
      formatear("Planta de cimentacion") == "PLANTA DE CIMENTACION")

check("y los acentos se pueden quitar, para las fuentes shx",
      formatear("Angel Muñoz", quitar_acentos=False) == "ANGEL MUÑOZ"
      and formatear("Angel Muñoz", quitar_acentos=True) == "ANGEL MUNOZ")


print("\n" + "=" * 78)
print("EL NOMBRE DEL LAYOUT")
print("=" * 78)

check("el layout se llama como la CLAVE del plano",
      nombre_de_layout(PLANO) == "E-01")

#  La clave es corta y unica; el titulo completo cabe pero deja una fila de pestanas que
#  no se puede recorrer.
check("y si no hay clave, como su titulo",
      nombre_de_layout(solapa(titulo="Planta de cimentacion")) == "PLANTA DE CIMENTACION")

#  LOS CARACTERES PROHIBIDOS SE SUSTITUYEN, no se borran: "E-01/02" tiene que quedar
#  "E-01-02" y no "E-0102", que se lee como otra clave.
check("los caracteres que AutoCAD no acepta se cambian por un guion",
      limpiar("E-01/02") == "E-01-02" and limpiar('A<B>C"D:E;F?G*H|I,J=K`L') == "A-B-C-D-E-F-G-H-I-J-K-L",
      repr(limpiar("E-01/02")))

check("un nombre vacio no deja el layout sin nombre",
      limpiar("") == "PLANO" and limpiar("   ") == "PLANO")

#  Una clave hecha SOLO de caracteres prohibidos no queda vacia: cada uno se sustituye,
#  asi que sale "---". Es raro pero es un nombre valido, y sustituir es mejor que borrar
#  -ver la prueba de arriba-. Se fija para que quede dicho cual de las dos reglas manda.
check("y una clave de puros caracteres prohibidos sale sustituida, no vacia",
      limpiar("///") == "---")

check("y no se pasa de 60 caracteres",
      len(limpiar("X" * 200)) == 60)

#  ---- NOMBRE LIBRE ----
check("sin sobrescribir, un nombre repetido recibe consecutivo",
      nombre_libre("E-01", ["Model", "E-01"], False) == "E-01-1"
      and nombre_libre("E-01", ["E-01", "E-01-1"], False) == "E-01-2")

check("y sobrescribiendo se devuelve tal cual: quien llama borra el que habia",
      nombre_libre("E-01", ["E-01"], True) == "E-01")

#  AutoCAD NO distingue mayusculas en los nombres de layout: pedirle "e-01" cuando ya
#  existe "E-01" no crea uno nuevo, falla.
check("la comparacion NO distingue mayusculas, como AutoCAD",
      nombre_libre("E-01", ["e-01"], False) == "E-01-1")

check("un nombre que no choca se queda igual",
      nombre_libre("E-05", ["E-01", "E-02"], False) == "E-05")


print("\n" + "=" * 78)
print("LEER LAS MEDIDAS DEL NOMBRE CANONICO DEL PAPEL")
print("=" * 78)
#  Los nombres canonicos de AutoCAD son asi de feos, y de aqui sale toda la busqueda.

check("ARCH D en pulgadas se lee y se pasa a mm",
      medidas_del_nombre("ARCH_D_(36.00_x_24.00_Inches)") is not None
      and abs(medidas_del_nombre("ARCH_D_(36.00_x_24.00_Inches)")[0] - 914.4) < 0.01
      and abs(medidas_del_nombre("ARCH_D_(36.00_x_24.00_Inches)")[1] - 609.6) < 0.01,
      str(medidas_del_nombre("ARCH_D_(36.00_x_24.00_Inches)")))

check("ISO A1 en milimetros se lee TAL CUAL, sin multiplicar",
      medidas_del_nombre("ISO_A1_(841.00_x_594.00_MM)") == (841.0, 594.0),
      str(medidas_del_nombre("ISO_A1_(841.00_x_594.00_MM)")))

#  ═════════════════════════════════════════════════════════════════════════════
#  LOS DIGITOS DEL NOMBRE NO SON LA MEDIDA. "ARCH_E1", "ISO_A4", "ARCH_expand_D1":
#  todos traen numeros ANTES del parentesis. Tomarlos como medida da un pliego que no
#  existe, y el papel no se encuentra.
#  ═════════════════════════════════════════════════════════════════════════════
check("los digitos del nombre del tamano NO se confunden con la medida",
      medidas_del_nombre("ARCH_E1_(42.00_x_30.00_Inches)") is not None
      and abs(medidas_del_nombre("ARCH_E1_(42.00_x_30.00_Inches)")[0] - 42 * 25.4) < 0.01,
      str(medidas_del_nombre("ARCH_E1_(42.00_x_30.00_Inches)")))

check("ni en ISO A4, ni en ARCH expand D1",
      abs(medidas_del_nombre("ISO_A4_(210.00_x_297.00_MM)")[0] - 210.0) < 0.01
      and abs(medidas_del_nombre("ARCH_expand_D1_(38.00_x_26.00_Inches)")[0]
              - 38 * 25.4) < 0.01)

#  PRUEBA NEGATIVA: tomando el PRIMER numero en lugar de los dos ultimos, ARCH E1 daria
#  una medida de 1 pulgada. Es el error que el "los dos ultimos" evita.
_nums_e1 = extraer_numeros("ARCH_E1_(42.00_x_30.00_Inches)")
check("PRUEBA NEGATIVA: con el primer numero, ARCH E1 mediria una pulgada",
      _nums_e1[0] == 1.0 and _nums_e1[-2] == 42.0,
      f"los numeros del nombre completo son {_nums_e1}")

check("un nombre sin medidas se reconoce como tal",
      medidas_del_nombre("Tamano_de_la_casa") is None
      and medidas_del_nombre("") is None
      and medidas_del_nombre(None) is None)

#  El punto decimal siempre: el nombre canonico lo trae, y leerlo con la coma de la
#  configuracion regional convertiria 36.00 en 3600.
check("el punto decimal se lee siempre como punto, no como separador de miles",
      extraer_numeros("36.00") == [36.0])


print("\n" + "=" * 78)
print("LA BUSQUEDA DEL PAPEL: LA CAUSA NUMERO UNO DE QUE EL PLANO SALGA MAL")
print("=" * 78)
#  Si el pliego no se encuentra, AutoCAD NO da error: deja el papel por omision -Carta
#  vertical- y el marco, el cajetin y las cotas se dibujan sobre una hoja que no es.

MEDIOS = [
    "Letter_(8.50_x_11.00_Inches)",
    "ANSI_D_(34.00_x_22.00_Inches)",
    "ARCH_C_(24.00_x_18.00_Inches)",
    "ARCH_D_(36.00_x_24.00_Inches)",
    "ARCH_expand_D_(36.00_x_24.00_Inches)",
    "ARCH_D1_(38.00_x_26.00_Inches)",
    "ARCH_E1_(42.00_x_30.00_Inches)",
    "ARCH_full_bleed_D_(36.00_x_24.00_Inches)",
    "ISO_A1_(841.00_x_594.00_MM)",
]

D_HORIZ = solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36, horizontal=True)
D_VERT = solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36, horizontal=False)

r = buscar_papel(MEDIOS, D_HORIZ)
check("ARCH D horizontal encuentra su pliego SIN rotar",
      r is not None and r[1] == 0 and r[2] is True,
      f"{r}")

#  Y GANA EL EXPAND: tiene menos margen no imprimible, que es lo que quiere un plano que
#  llega hasta el borde. Es el desempate por puntos.
check("y gana el expand, que tiene menos margen no imprimible",
      r[0] == "ARCH_expand_D_(36.00_x_24.00_Inches)", f"eligio {r[0]}")

check("sin preferir expand, gana el pliego normal",
      buscar_papel(MEDIOS, D_HORIZ, preferir_expand=False)[0]
      == "ARCH_D_(36.00_x_24.00_Inches)")

#  EL FULL BLEED SE DESCARTA por omision: casi ningun plotter puede imprimir sin margen,
#  y el resultado es un plano recortado por los cuatro lados.
check("el full bleed se descarta por omision, aunque mida exactamente lo pedido",
      "full_bleed" not in buscar_papel(MEDIOS, D_HORIZ)[0])

check("y pidiendolo, gana el full bleed",
      "full_bleed" in buscar_papel(MEDIOS, D_HORIZ, usar_full_bleed=True)[0])

#  ---- LA MISMA MEDIDA AL REVES: HAY QUE ROTAR EL PLOTEO ----
rv = buscar_papel(MEDIOS, D_VERT)
check("ARCH D VERTICAL encuentra el mismo pliego, pero pidiendo rotar 90 grados",
      rv is not None and rv[1] == 1 and rv[2] is True,
      f"{rv}")

#  ═════════════════════════════════════════════════════════════════════════════
#  EL PARENTESIS DEL ACIERTO POR NOMBRE. Sin el, "archd" pescaria "archd1", que es otro
#  pliego -24x36 contra 26x38- y el plano sale en la hoja de al lado sin un solo aviso.
#  ═════════════════════════════════════════════════════════════════════════════
SOLO_D1 = ["ARCH_D1_(38.00_x_26.00_Inches)"]
check("PRUEBA NEGATIVA: pedir ARCH D no puede pescar ARCH D1",
      buscar_papel(SOLO_D1, solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36),
                   usar_mas_grande=False) is None,
      "si lo pescara, el plano saldria en una hoja de 26x38 en lugar de 24x36")

#  Y con el ultimo recurso SI lo usa, pero AVISANDO que el plano va a quedar con mas
#  margen. Eso es lo que distingue un acierto de un apano.
r_apano = buscar_papel(SOLO_D1, solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36))
check("y como ultimo recurso lo usa, pero marcado como NO exacto",
      r_apano is not None and r_apano[2] is False,
      f"{r_apano}")

#  ---- EL ACIERTO POR NOMBRE, para los tamanos personalizados sin medidas ----
PERSONAL = ["Tamano_del_despacho", "PLANO_GIPC"]
check("un tamano personalizado se encuentra por NOMBRE",
      buscar_papel(PERSONAL, solapa(tamano="PLANO GIPC", ancho_pulg=24, alto_pulg=36))
      == ("PLANO_GIPC", -1, True))

#  ROTACION -1 = NO TOCAR. En un acierto por nombre no se sabe como esta puesto el
#  pliego, y forzarle una rotacion lo empeoraria.
check("y NO se le toca la rotacion: -1 significa dejarla como esta",
      buscar_papel(PERSONAL, solapa(tamano="PLANO GIPC", ancho_pulg=24, alto_pulg=36))[1]
      == -1)

#  ---- LA FAMILIA DESEMPATA ----
#  ANSI D mide 34x22 y ARCH C mide 24x18: ninguno es 24x36. Pidiendo ARCH, entre dos que
#  empatan gana el ARCH.
EMPATE = ["ANSI_D_(36.00_x_24.00_Inches)", "ARCH_D_(36.00_x_24.00_Inches)"]
check("entre dos pliegos de la misma medida gana el de la familia que se pidio",
      buscar_papel(EMPATE, D_HORIZ)[0] == "ARCH_D_(36.00_x_24.00_Inches)"
      and buscar_papel(EMPATE, solapa(tamano="ANSI D", ancho_pulg=24, alto_pulg=36))[0]
      == "ANSI_D_(36.00_x_24.00_Inches)")

#  ---- EL ULTIMO RECURSO: EL MAS CHICO DONDE QUEPA ----
CHICOS = [
    "Letter_(8.50_x_11.00_Inches)",
    "ARCH_E1_(42.00_x_30.00_Inches)",
    "ARCH_D1_(38.00_x_26.00_Inches)",
]
r_cabe = buscar_papel(CHICOS, solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36))

check("sin el pliego exacto, se usa el MAS CHICO donde quepa",
      r_cabe is not None and r_cabe[0] == "ARCH_D1_(38.00_x_26.00_Inches)"
      and r_cabe[2] is False,
      f"{r_cabe}: de 38x26 y 42x30 se queda con el chico")

check("y una hoja Carta NO se ofrece para un plano de ARCH D",
      "Letter" not in r_cabe[0])

check("si NADA cabe, se devuelve None y hay que avisar",
      buscar_papel(["Letter_(8.50_x_11.00_Inches)"],
                   solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36)) is None)

check("y con el ultimo recurso apagado tampoco se inventa un pliego",
      buscar_papel(CHICOS, solapa(tamano="ARCH D", ancho_pulg=24, alto_pulg=36),
                   usar_mas_grande=False) is None)

#  Una lista vacia o con basura no puede reventar: el dispositivo puede no contestar.
check("una lista vacia o con huecos no revienta",
      buscar_papel([], D_HORIZ) is None
      and buscar_papel(["", "   ", None], D_HORIZ) is None)

check("el nombre corto para el reporte sale legible",
      nombre_corto_del_papel("ARCH_expand_D1_(26.00_x_38.00_Inches)") == "ARCH expand D1")


print("\n" + "=" * 78)
print("LA HOJA, SU ORIENTACION Y SU CONFIGURACION DE PAGINA")
print("=" * 78)

check("horizontal deja el lado largo en X, y vertical al contrario",
      hoja_orientada(D_HORIZ) == (36 * 25.4, 24 * 25.4)
      and hoja_orientada(D_VERT) == (24 * 25.4, 36 * 25.4))

#  Da igual como se capturen el ancho y el alto: la orientacion manda.
check("y da igual el orden en que se capturen las medidas",
      hoja_orientada(solapa(ancho_pulg=36, alto_pulg=24, horizontal=True))
      == hoja_orientada(solapa(ancho_pulg=24, alto_pulg=36, horizontal=True)))

check("la configuracion de pagina se llama como el tamano mas la orientacion",
      config_pagina_sirve(D_HORIZ, "ARCH D Horizontal"))

#  LAS DOS FORMAS que la macro documenta. Sin normalizar, una plantilla con las
#  configuraciones nombradas "ARCH-D-H" no encontraria ninguna.
check("y se reconoce escrita de las tres maneras",
      config_pagina_sirve(D_HORIZ, "ARCH-D-H")
      and config_pagina_sirve(D_HORIZ, "archdhorizontal")
      and config_pagina_sirve(D_HORIZ, "ARCH_D_Horizontal"))

check("una configuracion de OTRA orientacion no sirve",
      not config_pagina_sirve(D_HORIZ, "ARCH D Vertical")
      and config_pagina_sirve(D_VERT, "ARCH D Vertical"))

check("ni una de otro tamano",
      not config_pagina_sirve(D_HORIZ, "ARCH E1 Horizontal"))

check("y sin tamano capturado no sirve ninguna",
      not config_pagina_sirve(solapa(), "ARCH D Horizontal"))

#  ---- EL PAPEL DEL LAYOUT ES EL QUE SE PIDIO? ----
#  Se comparan el lado mayor con el mayor y el menor con el menor, asi que da igual la
#  rotacion del ploteo: lo que se comprueba es que sea el mismo PLIEGO.
check("el papel coincide aunque este rotado",
      papel_coincide(914.4, 609.6, 914.4, 609.6)
      and papel_coincide(609.6, 914.4, 914.4, 609.6))

check("y no coincide con otro pliego",
      not papel_coincide(965.2, 660.4, 914.4, 609.6))

check("con una tolerancia de 6 mm, que absorbe el redondeo del driver",
      papel_coincide(914.0, 609.0, 914.4, 609.6)
      and papel_coincide(920.0, 609.6, 914.4, 609.6)
      and not papel_coincide(925.0, 609.6, 914.4, 609.6),
      "5.6 mm de diferencia pasan; 10.6 no")


print("\n" + "=" * 78)
print("ENCAJAR EL CAJETIN EN LA HOJA")
print("=" * 78)
#  UNA SOLA ESCALA para los dos ejes. Escalando X y Y por separado el marco encaja
#  perfecto y los TEXTOS del cajetin salen estirados, y eso no se arregla sin volver a
#  generar el plano.

check("la escala es unica: el cajetin no se deforma",
      abs(escala_para_caber(400, 300, 800, 900) - 2.0) < 1e-9,
      "de 400x300 a 800x900 manda el eje X: 2.0, no 2.0 y 3.0")

check("y manda el eje que menos da de si",
      abs(escala_para_caber(400, 300, 900, 450) - 1.5) < 1e-9)

check("el margen se descuenta de los dos lados",
      abs(escala_para_caber(100, 100, 220, 220, margen=10) - 2.0) < 1e-9)

check("solo reducir nunca agranda",
      abs(escala_para_caber(100, 100, 500, 500, solo_reducir=True) - 1.0) < 1e-9
      and abs(escala_para_caber(1000, 1000, 500, 500, solo_reducir=True) - 0.5) < 1e-9)

#  Un bloque sin medida no puede escalar: se deja a 1 en lugar de dividir por cero.
check("un bloque sin medida se deja a escala 1, no revienta",
      escala_para_caber(0, 0, 800, 600) == 1.0
      and escala_para_caber(100, 100, 0, 0) == 1.0)


print("\n" + "=" * 78)
print("QUE FALTA CAPTURAR")
print("=" * 78)
#  La macro se saltaba el plano en silencio -"SIN MEDIDAS - saltado"-. Aqui se dice QUE
#  falta, que es lo que permite corregirlo sin adivinar.

check("un plano completo no le falta nada", falta_de(PLANO) == [])

check("sin clave NI titulo se avisa",
      "la clave o el titulo del plano" in falta_de(solapa(ancho_pulg=24, alto_pulg=36)))

check("y con solo la clave ya se puede generar",
      falta_de(solapa(clave="E-01", ancho_pulg=24, alto_pulg=36)) == [])

check("sin medidas de hoja se avisa",
      "el tamano de la hoja" in falta_de(solapa(clave="E-01")))


# ==========================================================================
print("\n" + "=" * 78)
print("Y EL C# HACE LO MISMO QUE ESTE PORT")
print("=" * 78)
#  Un port sirve mientras siga siendo el mismo codigo. Sin esto, la prueba mas verde del
#  mundo podria estar comprobando una version del calculo que ya nadie ejecuta.

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def fuente(*partes):
    with open(os.path.join(RAIZ, *partes), encoding="utf-8") as f:
        return f.read()


CAD = fuente("client", "src", "CadLink.Cad", "SolapasCad.cs")
DRW = fuente("client", "src", "CadLink.Cad", "SolapasDrawer.cs")
APP = fuente("client", "src", "CadLink.App", "MainWindow.Solapas.cs")
FILA = fuente("client", "src", "CadLink.App", "Models", "Solapa.cs")

check("la logica de las solapas vive aparte del dibujante y sin COM",
      "public static class Solapas" in CAD
      and "_doc" not in CAD
      and "AcadConnection" not in CAD
      and "dynamic" not in CAD)

check("los dieciseis atributos estan en el C#, y en TagsConocidos",
      all(f'case "{t}"' in CAD or f'"{t}"' in CAD for t in TAGS_CONOCIDOS)
      and all(f'"{t}"' in CAD for t in TAGS_CONOCIDOS))

#  NULL Y NO VACIO. Es la distincion que evita borrar los atributos que el dibujante
#  puso a mano.
check("un atributo desconocido devuelve null, no cadena vacia",
      "public static string? TextoDeAtributo(" in CAD
      and "default: return null;" in CAD
      and "if (texto is null)" in DRW
      and "continue;" in DRW)

check("los titulos van con los de punto PRIMERO",
      '"ING.", "ING", "ARQ.", "ARQ", "M.I.", "DR.", "LIC.", "C.",' in CAD)

check("y SinTitulo exige el espacio detras, para no morder el nombre",
      "t[tok.Length] == ' '" in CAD)

check("el prefijo de la cedula solo se pone si hay numero",
      'public const string PrefijoCedula = "CED. PROF. ";' in CAD
      and "s.Cedula.Trim().Length == 0 ? string.Empty : PrefijoCedula" in CAD)

check("las tres estrategias del papel estan, con sus puntos",
      "punto = 300;" in CAD and "punto = 200;" in CAD and "punto = 100;" in CAD
      and "punto += 5;" in CAD and "punto += 20;" in CAD
      and "preferirExpand ? 10 : -10" in CAD)

check("y el parentesis obligatorio del acierto por nombre",
      "nmN[pedido.Length] == '('" in CAD)

check("las medidas salen del ULTIMO parentesis y son los DOS ULTIMOS numeros",
      "s.LastIndexOf('(')" in CAD and "s.LastIndexOf(')')" in CAD
      and "nums[nums.Count - 2]" in CAD and "nums[nums.Count - 1]" in CAD)

check("y se leen con InvariantCulture, no con la coma del sistema",
      "CultureInfo.InvariantCulture" in CAD
      and "double.TryParse(" in CAD)

check("el full bleed se descarta antes de puntuarlo",
      "if (esFull && !usarFullBleed)" in CAD)

check("el ultimo recurso marca el papel como NO exacto",
      "public readonly record struct PapelElegido(string Nombre, int Rotacion, bool Cabe);" in CAD
      and "Cabe: false" in CAD and "Cabe: true" in CAD
      and "if (!p.Cabe)" in DRW)

#  Y EL DIBUJANTE AVISA en los dos casos malos: sin pliego, y con un pliego que solo
#  aproxima. Es lo unico que separa un plano descuadrado de un plano descuadrado que
#  alguien va a revisar.
check("el dibujante avisa cuando el tamano no existe en el dispositivo",
      "no existe en " in DRW and "PLOTTERMANAGER" in DRW
      and "papel por omisión" in DRW)

check("el cajetin se busca por sus atributos si no se llama CAJETIN",
      "public string? BuscarCajetin(" in DRW
      and "Solapas.EsTagConocido(" in DRW
      and "return mejor >= 3 ? nombre : null;" in DRW)

#  EN lay.Block Y NO EN _doc.PaperSpace: el segundo depende de cual layout este activo en
#  AutoCAD, asi que el cajetin podia acabar en el layout anterior.
check("se dibuja en lay.Block, no en el espacio papel activo",
      "((dynamic)lay).Block.InsertBlock(" in DRW
      and "_doc.PaperSpace" not in DRW)

check("el cajetin se mide, se escala sin deformar y se centra",
      "Solapas.EscalaParaCaber(" in DRW
      and ".ScaleEntity(" in DRW
      and "GetBoundingBox" in DRW)

#  ---- Y NINGUN PARAMETRO ES 'dynamic' ----
#  Un argumento dynamic vuelve DINAMICA la llamada entera, asi que el resultado deja de
#  tener el tipo declarado. Tres metodos de este archivo devolvian una tupla y recibian
#  un dynamic, y deconstruirla no compilaba -CS8133-. El dynamic se queda DENTRO.
check("las fronteras van tipadas: el dynamic se queda dentro de cada metodo",
      "Caja(object ent)" in DRW
      and "AreaImprimible(object lay)" in DRW
      and "MedidaDelPapel(object lay)" in DRW
      and "object? lay = CrearLayout(nombre);" in DRW
      and "object? br = Insertar(lay, cx, cy);" in DRW)

#  Y lo vigila verificar_usings.py, que es quien tenia que haberlo cazado antes.
USINGS = fuente("tools", "verificar_usings.py")

check("y verificar_usings.py lo comprueba, en TODO el cliente y no solo en la app",
      "RE_TUPLA_CON_DYNAMIC" in USINGS
      and "def revisar_parametros_dynamic(" in USINGS
      and "for ruta in cliente:" in USINGS
      and "revisar_parametros_dynamic(ruta, fh.read())" in USINGS)

check("y los atributos no alineados a la izquierda se recolocan",
      "(int)att.Alignment != 0" in DRW
      and "att.TextAlignmentPoint = att.TextAlignmentPoint;" in DRW)

#  ---- LA HOJA Y EL JUEGO ----
check("el plano trae su tamano, su orientacion y su detalle",
      "public string Detalle {" in FILA
      and "public string Tamano {" in FILA
      and "public bool Horizontal {" in FILA
      and "public string Cedula {" in FILA)

check("y un plano nuevo HEREDA el tamano del anterior",
      "var ultimo = Planos.Count > 0 ? Planos[Planos.Count - 1] : null;" in FILA
      and "Tamano = ultimo?.Tamano ?? " in FILA
      and "Horizontal = ultimo?.Horizontal ?? true" in FILA)

check("la solapa junta lo del juego con lo del plano",
      "private SolapaCad SolapaDeUnPlano(PlanoRow plano)" in APP
      and "Calculista = s.Calculista," in APP
      and "Escala = plano.Escala," in APP
      and "Numero = plano.Numero," in APP)

check("y la escala sale del PLANO, no del juego",
      "Escala = plano.Escala," in APP and "Escala = s.Escala," not in APP)


print("\n" + "=" * 78)
if fallos:
    print(f"ATENCION: {len(fallos)} comprobacion(es) fallaron.")
    for f in fallos:
        print(f"  - {f}")
else:
    print("OK: la logica de las solapas coincide con la macro.")
print("=" * 78)

raise SystemExit(1 if fallos else 0)
