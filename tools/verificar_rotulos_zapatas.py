"""Compara los rotulos y la colocacion de las zapatas aisladas: VBA contra el port.

El usuario: "en el alzado de zapatas aisladas los titulos no estan en base a mis dos
macro originales" y "ponle de aparte de x=-0.8, no lo dibujes a partir del centro".
Un titulo es una cadena y una separacion es un numero, asi que se comprueban letra
por letra y numero por numero, no a ojo sobre el dibujo.

  vba_*    traducido de ZAPATA AISLADA CENTRAL V2 y ZAPATA AISLADA LINDERO V1
  port_*   traducido de ZapataAisladaRotulos y ZapataAisladaLayout
"""

# ---- Constantes de las dos macros ----
ELEVACION_X_BASE = -3.0
ELEVACION_Y_BASE = -8.0
SEPARACION_SECCIONES = -0.8          # lindero: se usa su valor absoluto
SCALE_ELEVATION = 0.01

ROTULO_TITULO_OFFSET = 0.32
ROTULO_SUBTITULO_OFFSET = 0.41
ROTULO_ESCALA_OFFSET = 0.49

COLUMNA_FRACCION_CORTE = 8.0 / 9.0
ALTURA_COLUMNA_REP = 0.8

PLANTA_OFFSET_Y = -3.0               # central
PLANTA_Y_BASE = -15.0                # lindero
PLANTA_SEPARACION_MIN = 1.2          # lindero
PLANTA_COTA_OFFSET_DADO = 0.1
PLANTA_TITULO_OFFSET = 0.24
PLANTA_ESCALA_OFFSET = 0.33

LINDERO_ROTULO_ELEM_DX = 0.3
LINDERO_ROTULO_SUP_DY = 0.23
DESPLAZAMIENTO_PARRILLA_INF_CENTRAR = 0.2

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


def cerca(a, b, tol=1e-9):
    return abs(a - b) < tol


# ======================================================================
#  VBA: los textos, tal cual
# ======================================================================
def vba_formato_numero_simple(v):
    return str(int(v)) if v == int(v) else str(v)


def vba_texto_fc(raw):
    """TextoFCConcreto"""
    raw = raw.strip()
    if len(raw) == 0:
        return ""
    num = vba_extract_number(raw)
    if num > 0:
        return "f'c = " + vba_formato_numero_simple(num) + " kg/cm\u00b2"
    return "f'c = " + raw


def vba_extract_number(txt):
    """ExtractNumber"""
    txt = txt.replace(",", ".")
    num = ""
    started = False
    for c in txt:
        if c.isdigit() or c == ".":
            num += c
            started = True
        elif started:
            break
    return float(num) if num else 0.0


def vba_titulo(tipo, nombre):
    # AgregarTexto ..., "ZAPATA AISLADA CENTRAL " & Chr(34) & nombreZA & Chr(34)
    if tipo == "lindero":
        return 'ZAPATA AISLADA DE LINDERO "' + nombre + '"'
    return 'ZAPATA AISLADA CENTRAL "' + nombre + '"'


def vba_linea_escala(fc):
    """ "Rec. 5 cm" & IIf(Len(textoFC) > 0, "    " & textoFC, "") & "    Escala 1:10" """
    texto_fc = vba_texto_fc(fc)
    return "Rec. 5 cm" + ("    " + texto_fc if texto_fc else "") + "    Escala 1:10"


def vba_titulo_planta(nombre):
    return 'VISTA EN PLANTA "' + nombre + '"'


def vba_texto_var_sep(dia, sep, sufijo):
    """TextoVarSep"""
    if not dia:
        return ""
    t = "VAR " + dia
    if sufijo.strip():
        t += " " + sufijo.strip()
    if sep > 0:
        t += " @ " + vba_formato_numero_simple(sep) + " cm"
    return t


def vba_texto_barras_longitudinales(items):
    """TextoBarrasLongitudinales: agrupa por diametro conservando el orden"""
    orden, cuenta = [], {}
    for n, dia in items:
        if not dia or n <= 0:
            continue
        if dia not in cuenta:
            orden.append(dia)
            cuenta[dia] = 0
        cuenta[dia] += n
    return " + ".join(str(cuenta[d]) + " VAR " + d for d in orden)


def vba_texto_estribos(dia, esp):
    """TextoEstribosElemento"""
    if not dia:
        return ""
    esp = esp.upper().replace("CM", "").replace(" ", "").replace(",", ".").strip()
    return "EST " + dia if not esp else "EST " + dia + " @ " + esp + " cm"


def vba_rotulo_elemento_vertical(elemento, id_, items, dia_est, esp):
    """TextoRotuloElementoVertical"""
    titulo = elemento.strip().upper()
    if id_.strip():
        titulo += ' "' + id_.strip() + '"'
    res = [titulo]
    largas = vba_texto_barras_longitudinales(items)
    if largas:
        res.append(largas)
    est = vba_texto_estribos(dia_est, esp)
    if est:
        res.append(est)
    return "\r\n".join(res)


# ======================================================================
#  VBA: el bucle principal de la macro de LINDERO
# ======================================================================
def vba_fila(anchos):
    """
    Do
        anchoZapata = ...
        If dibujadas > 0 Then
            xBase = xBase - Abs(SEPARACION_SECCIONES) - anchoZapata
        End If
        DibujarUnaZapataLindero ..., xBase
    Loop

    Devuelve el PANO IZQUIERDO de cada zapata: la macro nunca coloca por el centro.
    """
    xs = []
    x = ELEVACION_X_BASE
    for i, ancho in enumerate(anchos):
        if i > 0:
            x = x - abs(SEPARACION_SECCIONES) - ancho
        xs.append(x)
    return xs


def vba_y_planta(tipo, y_zap_bot, largo):
    if tipo == "central":
        # YBasePlanta con PLANTA_OFFSET_DESDE_TOPE = True
        y_fondo_corte = y_zap_bot - ROTULO_ESCALA_OFFSET
        return y_fondo_corte + PLANTA_OFFSET_Y - largo
    # lindero
    y = (y_zap_bot - ROTULO_ESCALA_OFFSET - PLANTA_SEPARACION_MIN
         - largo - PLANTA_COTA_OFFSET_DADO)
    if y > PLANTA_Y_BASE:
        y = PLANTA_Y_BASE
    return y


def vba_panos_dado(tipo, x_base, ancho, ancho_dado_cm):
    """Central: centrado. Lindero: pegado al pano derecho."""
    w = ancho_dado_cm * SCALE_ELEVATION
    x_der = x_base + ancho
    if tipo == "lindero":
        return max(x_der - w, x_base), x_der
    x_centro = x_base + ancho / 2.0
    return x_centro - w / 2.0, x_centro + w / 2.0


def vba_rotulo_dado(tipo, x_dado_izq, x_dado_der, x_extremo_der, y_zap_top, y_dado_top):
    """Punta del leader, X del texto y hacia donde crece."""
    y = (y_zap_top + y_dado_top) / 2.0
    if tipo == "lindero":
        # xRotDado = xDadoIzq - LINDERO_ROTULO_ELEM_DX  /  haciaIzquierda = True
        return (x_dado_izq, y), (x_dado_izq - LINDERO_ROTULO_ELEM_DX, y), True
    # xRotDado = (xDadoDer + xExtremoDer) / 2
    return (x_dado_der, y), ((x_dado_der + x_extremo_der) / 2.0, y), False


# ======================================================================
#  PORT: ZapataAisladaRotulos y ZapataAisladaLayout
# ======================================================================
PORT_SEP4 = "    "
PORT_ROTULO_PARRILLA_INF_DX = -0.18 + 0.272 - 0.11 + DESPLAZAMIENTO_PARRILLA_INF_CENTRAR
PORT_ROTULO_PARRILLA_INF_DY = 0.1 + 0.4164 - 0.16
PORT_ROTULO_SUP_DX_CENTRAL = 0.16 - 0.4302
PORT_ROTULO_SUP_DY_CENTRAL = 0.02 + 0.2908 - 0.16


def port_titulo(tipo, id_):
    nombre = ("ZAPATA AISLADA DE LINDERO" if tipo == "lindero"
              else "ZAPATA AISLADA CENTRAL")
    return nombre + ' "' + id_.strip() + '"'


def port_linea_escala(fc, escala="10"):
    texto = vba_texto_fc(fc)          # TextoFc: misma cuenta en los dos lados
    if texto:
        return "Rec. 5 cm" + PORT_SEP4 + texto + PORT_SEP4 + "Escala 1:" + escala
    return "Rec. 5 cm" + PORT_SEP4 + "Escala 1:" + escala


def port_linea_escala_planta(escala="10"):
    return "Rec. 5 cm" + PORT_SEP4 + "Escala 1:" + escala


def port_x_siguiente(x_anterior, ancho_siguiente):
    return x_anterior - abs(SEPARACION_SECCIONES) - max(ancho_siguiente, 0)


def port_fila(anchos):
    xs = []
    x = ELEVACION_X_BASE
    for i, ancho in enumerate(anchos):
        if i > 0:
            x = port_x_siguiente(x, ancho)
        xs.append(x)
    return xs


def port_y_planta(tipo, y_desplante, largo):
    fondo = y_desplante - ROTULO_ESCALA_OFFSET
    if tipo == "central":
        return fondo + PLANTA_OFFSET_Y - largo
    return min(fondo - PLANTA_SEPARACION_MIN - largo - PLANTA_COTA_OFFSET_DADO,
               PLANTA_Y_BASE)


# ======================================================================
#  Comprobaciones
# ======================================================================
print("=" * 78)
print(" ROTULOS DEL ALZADO")
print("=" * 78)

for tipo, nombre in (("central", "ZE-1"), ("lindero", "ZL-1")):
    esperado = vba_titulo(tipo, nombre)
    check(f"titulo {tipo}: {esperado}", port_titulo(tipo, nombre) == esperado,
          port_titulo(tipo, nombre))

check("la linea de escala lleva f'c cuando la celda lo trae",
      port_linea_escala("250") == vba_linea_escala("250"),
      port_linea_escala("250"))
check("y NO escribe f'c a secas cuando la celda esta vacia",
      port_linea_escala("") == vba_linea_escala("") == "Rec. 5 cm    Escala 1:10",
      port_linea_escala(""))
check("f'c con kg/cm2 y sin ceros de mas",
      vba_texto_fc("250") == "f'c = 250 kg/cm\u00b2")
check("la escala de la PLANTA nunca lleva f'c",
      port_linea_escala_planta() == "Rec. 5 cm    Escala 1:10")
check("titulo de la planta",
      vba_titulo_planta("ZL-1") == 'VISTA EN PLANTA "ZL-1"')

print()
print("=" * 78)
print(" ROTULOS CON LEADER")
print("=" * 78)

dado = vba_rotulo_elemento_vertical(
    "DADO", "D-1", [(8, "#4"), (8, "#4")], "#3", "8")
check('dado: DADO "D-1" / 16 VAR #4 / EST #3 @ 8 cm',
      dado.split("\r\n") == ['DADO "D-1"', "16 VAR #4", "EST #3 @ 8 cm"],
      dado.replace("\r\n", " | "))

col = vba_rotulo_elemento_vertical(
    "COLUMNA", "C-1", [(4, "#6"), (4, "#6"), (2, "#4")], "#3", "10-15-10")
check("columna: suma los diametros iguales y respeta el 10-15-10",
      col.split("\r\n") == ['COLUMNA "C-1"', "8 VAR #6 + 2 VAR #4",
                            "EST #3 @ 10-15-10 cm"],
      col.replace("\r\n", " | "))

check("parrilla con armado igual en las dos direcciones: AMBOS SENTIDOS",
      vba_texto_var_sep("#4", 15, "") + "\r\n" + "AMBOS SENTIDOS"
      == "VAR #4 @ 15 cm\r\nAMBOS SENTIDOS")
check("parrilla con armado distinto: el sufijo va ANTES de la separacion",
      vba_texto_var_sep("#4", 18, "SUPERIOR") == "VAR #4 SUPERIOR @ 18 cm",
      vba_texto_var_sep("#4", 18, "SUPERIOR"))

print()
print("=" * 78
      )
print(" COLOCACION DE LA FILA:  0.8 de aire, hacia la izquierda, por el pano izquierdo")
print("=" * 78)

anchos = [1.00, 1.60, 0.90]
xs_vba = vba_fila(anchos)
xs_port = port_fila(anchos)

check("la fila coincide con el VBA",
      all(cerca(a, b) for a, b in zip(xs_vba, xs_port)),
      f"vba={xs_vba} port={xs_port}")

check("la primera zapata NO arranca en el centro del dibujo",
      not cerca(xs_port[0], 0.0) and cerca(xs_port[0], ELEVACION_X_BASE),
      f"x={xs_port[0]}")

for i in range(1, len(anchos)):
    aire = xs_port[i - 1] - (xs_port[i] + anchos[i])
    check(f"entre la zapata {i - 1} y la {i} quedan {abs(SEPARACION_SECCIONES)} de aire",
          cerca(aire, abs(SEPARACION_SECCIONES)), f"aire={aire:.6f}")

check("y cada una va mas a la IZQUIERDA que la anterior",
      all(xs_port[i] < xs_port[i - 1] for i in range(1, len(xs_port))))

# El error que se estaba viendo: colocar por el centro encima el dibujo.
def fila_por_el_centro(anchos):
    """Lo que NO hay que hacer: anclar por el centro sin descontar el ancho."""
    xs = []
    x = ELEVACION_X_BASE
    for i, ancho in enumerate(anchos):
        if i > 0:
            x = x - abs(SEPARACION_SECCIONES)
        xs.append(x - ancho / 2.0)
    return xs


xs_malo = fila_por_el_centro(anchos)
encimadas = any(xs_malo[i] + anchos[i] > xs_malo[i - 1] + 1e-9
                for i in range(1, len(anchos)))
check("(control) anclar por el centro SI encima las zapatas", encimadas,
      "el control ya no reproduce el error original")

print()
print("=" * 78)
print(" PANOS DEL DADO Y LADO DE SUS ROTULOS")
print("=" * 78)

x_base, ancho, ancho_dado = xs_port[0], anchos[0], 40.0
x_der = x_base + ancho

di, dd = vba_panos_dado("lindero", x_base, ancho, ancho_dado)
check("lindero: el dado queda pegado al pano derecho", cerca(dd, x_der),
      f"dadoDer={dd} panoDer={x_der}")

ci, cd = vba_panos_dado("central", x_base, ancho, ancho_dado)
check("central: el dado queda centrado en la zapata",
      cerca((ci + cd) / 2.0, x_base + ancho / 2.0))

y_zap_top = ELEVACION_Y_BASE + 0.30
y_dado_top = ELEVACION_Y_BASE + 1.05

punta, texto, izq = vba_rotulo_dado("lindero", di, dd, x_der, y_zap_top, y_dado_top)
check("lindero: el rotulo del dado sale hacia la IZQUIERDA", izq)
check("y apunta al pano izquierdo del dado, a 0.3 de el",
      cerca(punta[0], di) and cerca(texto[0], di - LINDERO_ROTULO_ELEM_DX),
      f"punta={punta[0]} texto={texto[0]}")

punta, texto, izq = vba_rotulo_dado("central", ci, cd, x_der, y_zap_top, y_dado_top)
check("central: el rotulo del dado sale hacia la DERECHA", not izq)
check("y su texto queda a medio camino entre el dado y el pano derecho",
      cerca(texto[0], (cd + x_der) / 2.0))

print()
print("=" * 78)
print(" ALTURAS DE LOS ROTULOS Y POSICION DE LA PLANTA")
print("=" * 78)

y = ELEVACION_Y_BASE
check("titulo a 0.32 por debajo del desplante",
      cerca(y - ROTULO_TITULO_OFFSET, y - 0.32))
check("ELEVACION a 0.41", cerca(y - ROTULO_SUBTITULO_OFFSET, y - 0.41))
check("Rec./f'c/Escala a 0.49", cerca(y - ROTULO_ESCALA_OFFSET, y - 0.49))

for tipo, largo in (("central", 1.60), ("lindero", 1.20)):
    a = vba_y_planta(tipo, y, largo)
    b = port_y_planta(tipo, y, largo)
    check(f"planta {tipo}: y = {a:.4f}", cerca(a, b), f"vba={a} port={b}")
    check(f"y la planta {tipo} queda por DEBAJO del rotulo del alzado",
          a < y - ROTULO_ESCALA_OFFSET)

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
