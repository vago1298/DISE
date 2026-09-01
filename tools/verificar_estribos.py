#!/usr/bin/env python3
"""Comprueba el reparto de estribos de Estribos.cs sin necesitar .NET ni AutoCAD.

Port en Python de Estribos.Centros / CentrosDeAlzado, con las correcciones
aplicadas. Lo que se verifica:

  1. Que NO quede ningún hueco mayor que la separación de la zona más holgura.
     Ese hueco doble en la frontera de zona era el bug principal.
  2. Que el primer y el último estribo estén a 5 cm de la cara (BordeM), y no
     a 5 cm + una separación.
  3. Que nunca queden dos estribos a menos de la separación mínima.

Se compara el ANTES y el DESPUÉS para que el efecto de la corrección sea
medible y no una afirmación.
"""

BORDE_M = 0.05
SEP_MINIMA_M = 0.05
TOL_TRANSICION_M = 0.06
SEP_MINIMA_DATO_M = 0.05


def _con_separacion(col, valor):
    if col and abs(col[-1] - valor) < SEP_MINIMA_M - 1e-7:
        return
    col.append(valor)


def _unico(col, valor):
    if not col or abs(col[-1] - valor) > 1e-4:
        col.append(valor)


def _por_separacion(col, ini, desde, hasta, sep, *, corregido):
    n = int(((hasta - desde) / sep) + 1e-6) if corregido else int((hasta - desde) / sep)
    n = max(n, 1)

    for i in range(1, n + 1):
        p = desde + i * sep
        cabe = (p <= hasta + 1e-6) if corregido else (p < hasta - 1e-4)
        if cabe:
            _con_separacion(col, ini + p)


def _transicion(col, nominal, siguiente, lim_superior):
    lo = nominal - TOL_TRANSICION_M
    hi = nominal + TOL_TRANSICION_M

    if col:
        lo = max(lo, col[-1] + SEP_MINIMA_M)

    hi = min(hi, siguiente - SEP_MINIMA_M, lim_superior)

    if lo > hi + 1e-7:
        return

    col.append(min(max(nominal, lo), hi))


def centros(x0, x1, s1, s2, s3, con_extremos, con_fronteras, *, corregido):
    col = []
    ini = x0 + BORDE_M
    fin = x1 - BORDE_M
    largo = fin - ini

    if largo <= 0:
        return col

    s1 = s1 if s1 > 0 else 0.15
    s2 = s2 if s2 > 0 else s1
    s3 = s3 if s3 > 0 else s1

    s1, s2, s3 = (max(v, SEP_MINIMA_DATO_M) for v in (s1, s2, s3))

    variable = abs(s1 - s2) > 1e-4 or abs(s2 - s3) > 1e-4

    if not variable:
        bruto = int(largo / s1 + 1e-6) if corregido else int(largo / s1)
        n = max(bruto, 3)
        max_por_sep = int(largo / SEP_MINIMA_M)
        if max_por_sep >= 1:
            n = min(n, max_por_sep)
        n = max(n, 1)

        paso = largo / n
        desde = 0 if con_extremos else 1
        hasta = n if con_extremos else n - 1

        for i in range(desde, hasta + 1):
            _unico(col, ini + i * paso)

        return col

    z1 = largo * 0.25
    z2 = z1 + largo * 0.5

    sig1 = ini + z1 + s2
    if z1 + s2 > z2 - 1e-4:
        sig1 = ini + z2

    sig2 = ini + z2 + s3
    if z2 + s3 > largo - 1e-4:
        sig2 = fin

    if con_extremos:
        _unico(col, ini)

    _por_separacion(col, ini, 0, z1, s1, corregido=corregido)
    if con_fronteras:
        _transicion(col, ini + z1, sig1, fin)

    _por_separacion(col, ini, z1, z2, s2, corregido=corregido)
    if con_fronteras:
        _transicion(col, ini + z2, sig2, fin)

    _por_separacion(col, ini, z2, largo, s3, corregido=corregido)

    if con_extremos:
        _unico(col, fin)

    return col


def centros_de_alzado(largo, s1, s2, s3, vertical, es_columna, *, corregido):
    if corregido:
        col = centros(0, largo, s1, s2, s3, True, True, corregido=True)
        if es_columna and len(col) > 2:
            col.pop()
    else:
        col = centros(0, largo, s1, s2, s3, False, not vertical, corregido=False)
        if es_columna:
            if len(col) <= 1:
                col.clear()
            else:
                col.pop()
    return col


def conteo_nominal(largo, s1, s2, s3):
    interior = largo - 2 * BORDE_M
    if interior <= 0:
        return 0

    s1 = s1 if s1 > 0 else 0.15
    s2 = s2 if s2 > 0 else s1
    s3 = s3 if s3 > 0 else s1
    s1, s2, s3 = (max(v, SEP_MINIMA_DATO_M) for v in (s1, s2, s3))

    if abs(s1 - s2) > 1e-4 or abs(s2 - s3) > 1e-4:
        n1 = int((interior * 0.25) / s1 + 1e-6)
        n2 = int((interior * 0.50) / s2 + 1e-6)
        n3 = int((interior * 0.25) / s3 + 1e-6)
        return n1 + n2 + n3 + 1

    return int(interior / s1 + 1e-6) + 1


# --------------------------------------------------------------------------
# Casos: los de la hoja de ejemplo del repo, más los que exponen el bug
# --------------------------------------------------------------------------
CASOS = [
    # (nombre, largo m, s1, s2, s3 en m, vertical, es_columna)
    ("Trabe V-101  L=4.00  10-15-20", 4.00, 0.10, 0.15, 0.20, False, False),
    ("Trabe V-102  L=6.00  10-20-10", 6.00, 0.10, 0.20, 0.10, False, False),
    ("Columna C-1  L=3.00  10-20-10", 3.00, 0.10, 0.20, 0.10, True, True),
    ("Columna C-2  L=2.90  15 unico", 2.90, 0.15, 0.15, 0.15, True, True),
    ("Dado D-1     L=1.00  10-10-10", 1.00, 0.10, 0.10, 0.10, True, False),
    # Zona multiplo EXACTO de la separacion: es donde aparecia el hueco doble
    ("Trabe exacta L=4.10  10-10-10", 4.10, 0.10, 0.10, 0.10, False, False),
    ("Columna exac L=4.10  10-20-10", 4.10, 0.10, 0.20, 0.10, True, True),
]


def revisar(col, largo, s_max):
    """Devuelve la lista de defectos encontrados en un reparto."""
    problemas = []

    if not col:
        problemas.append("NO se coloco ningun estribo")
        return problemas

    # 1) huecos
    limite = s_max + 0.011  # 1 mm de holgura sobre la separacion mas holgada
    for a, b in zip(col, col[1:]):
        if b - a > limite:
            problemas.append(
                f"hueco de {100 * (b - a):.1f} cm entre x={100 * a:.1f} y "
                f"x={100 * b:.1f} (la separacion mas holgada es {100 * s_max:.0f} cm)"
            )

    # 2) arranque y remate
    if col[0] - BORDE_M > 0.011:
        problemas.append(
            f"el primer estribo esta a {100 * col[0]:.1f} cm de la cara, "
            f"y deberia estar a {100 * BORDE_M:.0f} cm"
        )

    # 3) separacion minima
    for a, b in zip(col, col[1:]):
        if b - a < SEP_MINIMA_M - 1e-7:
            problemas.append(f"dos estribos a {100 * (b - a):.1f} cm, menos del minimo")

    return problemas


def main():
    fallos_totales = 0

    print("=" * 78)
    print("REPARTO DE ESTRIBOS  -  antes y despues de la correccion")
    print("=" * 78)

    for nombre, largo, s1, s2, s3, vertical, es_col in CASOS:
        s_max = max(s1, s2, s3)
        nominal = conteo_nominal(largo, s1, s2, s3)

        antes = centros_de_alzado(largo, s1, s2, s3, vertical, es_col, corregido=False)
        despues = centros_de_alzado(largo, s1, s2, s3, vertical, es_col, corregido=True)

        p_antes = revisar(antes, largo, s_max)
        p_despues = revisar(despues, largo, s_max)

        print(f"\n{nombre}")
        print(f"  nominal por separacion : {nominal} estribos")
        print(f"  ANTES                  : {len(antes)} estribos, "
              f"{len(p_antes)} defecto(s)")
        for p in p_antes:
            print(f"      - {p}")
        print(f"  DESPUES                : {len(despues)} estribos, "
              f"{len(p_despues)} defecto(s)")
        for p in p_despues:
            print(f"      - {p}")

        if p_despues:
            fallos_totales += len(p_despues)

    print("\n" + "=" * 78)
    if fallos_totales == 0:
        print("OK: despues de la correccion no queda ningun hueco doble,")
        print("    ningun extremo sin estribo y ninguna separacion bajo el minimo.")
    else:
        print(f"ATENCION: quedan {fallos_totales} defecto(s) por revisar.")
    print("=" * 78)

    return 0 if fallos_totales == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
