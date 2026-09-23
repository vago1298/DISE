#!/usr/bin/env python3
"""
El boton ORDENAR de la hoja de secciones de concreto.

Lo pidio el usuario asi: «que ponga todos los castillos juntos, todas las trabes
juntas, porque a veces los agrego despues y al dibujarlos en AutoCAD estan
separados». Y ese es el punto: la hoja se dibuja EN EL ORDEN EN QUE ESTA, asi que
ordenar la hoja es ordenar el plano.

    python tools/verificar_orden_secciones.py

Por que hace falta comprobarlo, y no basta con mirar el boton una vez:

  1. El orden es el del DESPLEGABLE. Son dos cosas leyendo la misma lista -la celda
     de Elemento y este boton-, y el dia que se agregue un elemento nuevo tienen que
     seguir coincidiendo. Si se copiara la lista, el elemento nuevo saldria en el
     desplegable y el boton lo mandaria al final sin decir nada.

  2. El ID se compara COMO LO LEE UNA PERSONA: K-2 antes de K-10. Comparado como
     texto a secas, K-10 queda antes de K-2, y en una hoja de cuarenta castillos eso
     se ve enseguida: parece que el boton no funciona.

  3. Ordenar es UN paso de deshacer, no cuarenta. Cada movimiento avisa a la
     coleccion, asi que sin el guardia ordenar cuarenta filas apila treinta y cinco
     pasos en el historial -Ctrl+Z treinta y cinco veces- y redibuja la vista previa
     treinta y cinco veces.

  4. Las filas se MUEVEN, no se copian: la seleccionada sigue siendo el mismo objeto
     y las grapas que cuelgan de cada seccion siguen en su sitio.

La primera mitad del archivo es un ESPEJO en Python del orden que hace el programa,
y la lista de elementos NO esta escrita aqui: se lee del propio C#, asi que el
espejo no puede quedarse viejo. Con el espejo se ordenan hojas de prueba y se
comprueba el resultado. La segunda mitad lee el C# y el XAML y comprueba que el
boton este cableado y que los cuatro puntos de arriba sigan en su sitio.
"""

from __future__ import annotations

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(RAIZ, "client", "src", "CadLink.App")

fallos: list[str] = []


def check(nombre: str, ok: bool, detalle: str = "") -> None:
    if ok:
        print(f"  OK    {nombre}")
        return

    print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
    fallos.append(f"{nombre}: {detalle}")


def leer(*partes: str) -> str:
    with open(os.path.join(APP, *partes), encoding="utf-8") as f:
        return f.read()


def antes(texto: str, primero: str, segundo: str) -> bool:
    """
    Verdadero si «primero» aparece antes que «segundo» en el texto.

    Si falta alguno de los dos devuelve falso en vez de tirar excepcion: cuando una
    comprobacion anterior ya dijo que ese trozo no esta, lo que hace falta es la
    lista completa de fallas, no un traceback a media prueba.
    """
    a = texto.find(primero)
    b = texto.find(segundo)

    return a >= 0 and b >= 0 and a < b


FILAS = leer("Models", "StructuralRows.cs")
ORDEN = leer("MainWindow.Orden.cs")
CODIGO = leer("MainWindow.xaml.cs")
XAML = leer("MainWindow.xaml")


# ======================================================================
#  EL ESPEJO: el mismo orden que hace el programa, en Python
# ======================================================================

def elementos_en_orden() -> list[str]:
    """
    La lista del desplegable, leida del C#.

    Las entradas son de dos clases -constantes como ElementoColumna y literales
    como "CASTILLO"-, y las constantes se resuelven a su valor buscando su
    declaracion en el mismo archivo. Asi la lista de aqui es, literalmente, la del
    programa: si alguien agrega un elemento, esta prueba lo ve.
    """
    cuerpo = re.search(
        r"public static readonly string\[\] ElementosEnOrden\s*=\s*\{(.*?)\};",
        FILAS,
        re.S,
    )

    if cuerpo is None:
        return []

    texto = re.sub(r"//[^\n]*", "", cuerpo.group(1))
    lista: list[str] = []

    for bruto in texto.split(","):
        pieza = bruto.strip()

        if not pieza:
            continue

        literal = re.fullmatch(r'"([^"]*)"', pieza)

        if literal is not None:
            lista.append(literal.group(1))
            continue

        const = re.search(
            rf'public const string {re.escape(pieza)} = "([^"]*)";', FILAS
        )

        if const is None:
            lista.append(f"<<sin resolver: {pieza}>>")
            continue

        lista.append(const.group(1))

    return lista


ELEMENTOS = elementos_en_orden()


def orden_de_elemento(elemento: str | None) -> int:
    """Espejo de SeccionConcretoRow.OrdenDeElemento."""
    e = (elemento or "").strip()

    for i, nombre in enumerate(ELEMENTOS):
        if e.upper() == nombre.upper():
            return i

    return len(ELEMENTOS)


def partir(idd: str) -> tuple[str, int, str]:
    """Espejo de ComparadorDeId.Partir: «CT-12A» -> («CT-», 12, «A»)."""
    i = 0

    while i < len(idd) and not idd[i].isdigit():
        i += 1

    prefijo = idd[:i]
    j = i

    while j < len(idd) and idd[j].isdigit():
        j += 1

    if j == i:
        return (prefijo, -1, "")

    return (prefijo, int(idd[i:j]), idd[j:])


def clave_de_id(idd: str | None) -> tuple:
    """
    Espejo de ComparadorDeId, como clave de ordenacion.

    Los vacios van AL FINAL de su grupo -son las filas a medio capturar, y es donde
    se buscan-, y por eso llevan delante un 1 y los demas un 0.
    """
    x = (idd or "").strip()

    if not x:
        return (1, "", 0, "")

    prefijo, numero, resto = partir(x)

    return (0, prefijo.upper(), numero, resto.upper())


def ordenar(hoja: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """
    Espejo del boton: agrupa por elemento y dentro de cada grupo ordena por ID.

    Cada fila es (elemento, id). El sorted de Python es estable, igual que el
    OrderBy de C#, asi que dos filas iguales conservan el orden de captura.
    """
    return sorted(
        hoja,
        key=lambda f: (
            orden_de_elemento(f[0]),
            (f[0] or "").strip().upper(),
            clave_de_id(f[1]),
        ),
    )


# ======================================================================
#  1. LA LISTA SE LEE, NO SE COPIA
# ======================================================================
print("\n-- La lista de elementos --")

check("la lista de elementos se pudo leer del C#", len(ELEMENTOS) > 0)
check("y todas sus entradas se resolvieron a un nombre",
      all("<<sin resolver" not in e for e in ELEMENTOS),
      "; ".join(e for e in ELEMENTOS if "<<sin resolver" in e))
check("no hay elementos repetidos en el desplegable",
      len(ELEMENTOS) == len(set(ELEMENTOS)),
      f"{len(ELEMENTOS)} entradas, {len(set(ELEMENTOS))} distintas")

# Lo que el usuario pidio por su nombre.
for pieza in ("CASTILLO", "TRABE", "CONTRATRABE", "COLUMNA", "DADO"):
    check(f"{pieza} esta en la lista", pieza in ELEMENTOS)

# OTRO al final: es la fila donde el usuario escribe lo que quiera, y ahi tambien
# recuerda que la casilla se puede teclear.
check("OTRO va al final de la lista",
      ELEMENTOS and ELEMENTOS[-1] == "OTRO",
      ELEMENTOS[-1] if ELEMENTOS else "lista vacia")

# Cada forma redonda, PEGADA a su cuadrada: es donde se busca.
for cuadrada, redonda in (("COLUMNA", "COLUMNA CIRCULAR"), ("DADO", "DADO CIRCULAR")):
    junto = (
        cuadrada in ELEMENTOS
        and redonda in ELEMENTOS
        and ELEMENTOS.index(redonda) == ELEMENTOS.index(cuadrada) + 1
    )
    check(f"{redonda} va justo despues de {cuadrada}", junto)

# Las tres cadenas, juntas: el dibujante ya las conoce como familia.
cadenas = [i for i, e in enumerate(ELEMENTOS) if e.startswith("CADENA ")]
check("las cadenas van seguidas, sin nada en medio",
      len(cadenas) >= 2 and cadenas == list(range(cadenas[0], cadenas[0] + len(cadenas))),
      f"posiciones {cadenas}")

# UNA sola lista: la celda la lee de StructuralRows, no tiene la suya.
check("el desplegable de la celda lee ESA lista",
      "ColElemento.ItemsSource = SeccionConcretoRow.ElementosEnOrden;" in CODIGO)
check("y MainWindow ya no tiene su propia copia de la lista",
      '"CADENA DE CERRAMIENTO"' not in CODIGO,
      "MainWindow.xaml.cs vuelve a escribir los elementos a mano")


# ======================================================================
#  2. LO QUE PIDIO EL USUARIO: LOS CASTILLOS JUNTOS, LAS TRABES JUNTAS
# ======================================================================
print("\n-- Agrupar por elemento --")

# Una hoja como la describio: capturada a ratos, con las trabes repartidas.
hoja = [
    ("CASTILLO", "C-1"),
    ("TRABE", "T-1"),
    ("CASTILLO", "C-2"),
    ("COLUMNA", "COL-1"),
    ("TRABE", "T-2"),
    ("CASTILLO", "C-3"),
    ("TRABE", "T-3"),
]

resultado = ordenar(hoja)
nombres = [e for e, _ in resultado]


def seguidos(lista: list[str], que: str) -> bool:
    """Verdadero si todas las apariciones de «que» estan pegadas."""
    donde = [i for i, e in enumerate(lista) if e == que]

    return bool(donde) and donde == list(range(donde[0], donde[0] + len(donde)))


check("todos los CASTILLO quedan juntos", seguidos(nombres, "CASTILLO"), str(nombres))
check("todas las TRABE quedan juntas", seguidos(nombres, "TRABE"), str(nombres))
check("no se pierde ni se duplica ninguna fila",
      sorted(resultado) == sorted(hoja),
      f"{len(resultado)} filas de {len(hoja)}")
check("las columnas van antes de los castillos, como en el desplegable",
      nombres.index("COLUMNA") < nombres.index("CASTILLO"),
      str(nombres))
check("y los castillos antes de las trabes: primero lo vertical",
      nombres.index("CASTILLO") < nombres.index("TRABE"),
      str(nombres))


# ======================================================================
#  3. EL ID, COMO LO LEE UNA PERSONA
# ======================================================================
print("\n-- El orden del ID --")

muchos = [("CASTILLO", f"C-{n}") for n in (10, 2, 1, 21, 3, 11)]
ids = [i for _, i in ordenar(muchos)]

check("K-2 va antes de K-10: el numero manda, no el texto",
      ids == ["C-1", "C-2", "C-3", "C-10", "C-11", "C-21"],
      str(ids))

# Prefijos distintos dentro del mismo elemento: se agrupan por prefijo.
mezcla = [("OTRO", "B-2"), ("OTRO", "A-10"), ("OTRO", "A-2"), ("OTRO", "B-1")]
check("con prefijos distintos, primero el prefijo y luego el numero",
      [i for _, i in ordenar(mezcla)] == ["A-2", "A-10", "B-1", "B-2"],
      str([i for _, i in ordenar(mezcla)]))

# Las filas a medio capturar, al final de SU grupo: es donde se buscan.
medias = [("TRABE", ""), ("TRABE", "T-2"), ("TRABE", "   "), ("TRABE", "T-1")]
check("las filas sin ID quedan al final de su grupo",
      [i.strip() for _, i in ordenar(medias)] == ["T-1", "T-2", "", ""],
      str([i for _, i in ordenar(medias)]))

# Con sufijo: CT-12A y CT-12B, en orden, y despues del CT-12 pelado.
sufijos = [("CONTRATRABE", "CT-12B"), ("CONTRATRABE", "CT-12"), ("CONTRATRABE", "CT-12A")]
check("el sufijo de letra ordena despues del numero",
      [i for _, i in ordenar(sufijos)] == ["CT-12", "CT-12A", "CT-12B"],
      str([i for _, i in ordenar(sufijos)]))

# Sin numero reconocible no se cae: es una fila sin terminar, y va antes de las
# numeradas del mismo prefijo.
raro = [("TRABE", "T-1"), ("TRABE", "T-"), ("TRABE", "VIGA")]
check("un ID sin numero no rompe el orden",
      len(ordenar(raro)) == 3 and [i for _, i in ordenar(raro)][0] == "T-",
      str([i for _, i in ordenar(raro)]))

# Dos filas iguales conservan el orden de captura: el orden es ESTABLE, asi que
# ordenar dos veces no baila.
gemelas = [("TRABE", "T-1"), ("TRABE", "T-1"), ("CASTILLO", "C-1")]
check("ordenar dos veces da lo mismo: el orden es estable",
      ordenar(ordenar(gemelas)) == ordenar(gemelas))


# ======================================================================
#  4. LOS ESCRITOS A MANO, AL FINAL Y AGRUPADOS
# ======================================================================
print("\n-- Los elementos escritos a mano --")

# La celda de Elemento es de texto libre: «MENSULA» no esta en el desplegable.
mano = [
    ("MENSULA", "M-2"),
    ("TRABE", "T-1"),
    ("VIGA DE TRANSFERENCIA", "V-1"),
    ("MENSULA", "M-1"),
    ("CASTILLO", "C-1"),
]

r = ordenar(mano)
n = [e for e, _ in r]

check("un elemento escrito a mano va DESPUES de los conocidos",
      n.index("MENSULA") > n.index("TRABE"), str(n))
check("y los escritos a mano tambien quedan agrupados",
      seguidos(n, "MENSULA"), str(n))
check("entre ellos se ordenan por nombre",
      n.index("MENSULA") < n.index("VIGA DE TRANSFERENCIA"), str(n))
check("y dentro del suyo, por ID",
      [i for e, i in r if e == "MENSULA"] == ["M-1", "M-2"], str(r))

# El elemento en blanco no tira nada: es la fila recien agregada.
vacias = [("", "?"), ("TRABE", "T-1"), (None, "")]  # type: ignore[list-item]
check("una fila con el elemento en blanco no rompe el orden",
      len(ordenar(vacias)) == 3)

# Mayusculas y minusculas son el mismo elemento: el usuario teclea como le sale.
casos = [("trabe", "T-2"), ("TRABE", "T-1"), ("Trabe", "T-3")]
check("mayusculas y minusculas agrupan igual",
      [i for _, i in ordenar(casos)] == ["T-1", "T-2", "T-3"],
      str([i for _, i in ordenar(casos)]))


# ======================================================================
#  5. EL BOTON, CABLEADO
# ======================================================================
print("\n-- El boton en la hoja --")

boton = re.search(r"<Button[^>]*x:Name=\"OrdenarSeccionesButton\".*?/>", XAML, re.S)

check("el boton Ordenar esta en la hoja de secciones", boton is not None)
check("y dice «Ordenar»", boton is not None and 'Content="Ordenar"' in boton.group(0))
check("y llama a OnOrdenarSecciones",
      boton is not None and 'Click="OnOrdenarSecciones"' in boton.group(0))
check("y explica en su globo que el plano se dibuja en ese orden",
      boton is not None and "ToolTip=" in boton.group(0)
      and "orden" in boton.group(0).lower())
check("el metodo que llama existe",
      "private void OnOrdenarSecciones(object sender, RoutedEventArgs e)" in ORDEN)

# Antes de los botones de dibujar: se ordena y despues se dibuja.
check("va antes del boton de generar alzados",
      antes(XAML, "OrdenarSeccionesButton", "AlzadosButton"))


# ======================================================================
#  6. LAS FILAS SE MUEVEN, NO SE COPIAN
# ======================================================================
print("\n-- Como se reordena --")

check("se mueven las filas con Move", "filas.Move(actual, destino);" in ORDEN)
check("y NO se vacia la coleccion para volver a llenarla",
      "filas.Clear()" not in ORDEN and ".Clear();" not in ORDEN,
      "vaciar y rellenar pierde la seleccion y vuelve a suscribir cada fila")
check("el orden sale del desplegable, no de una lista propia",
      "SeccionConcretoRow.OrdenDeElemento(f.Elemento)" in ORDEN)
check("y el ID se compara con el comparador de personas",
      "SeccionConcretoRow.PorId" in ORDEN)
check("primero se cierra la celda que se este editando",
      "CerrarEdicionDeLasHojas();" in ORDEN,
      "sin esto se ordenaria con el valor viejo de la celda")
check("con menos de dos secciones no hace nada", "filas.Count < 2" in ORDEN)
check("la fila seleccionada se vuelve a seleccionar al terminar",
      "SeccionesGrid.SelectedItem = seleccionada;" in ORDEN
      and "SeccionesGrid.ScrollIntoView(seleccionada);" in ORDEN,
      "si no, la vista previa enseña otra seccion")
check("y se dice cuantas se movieron y cuantos grupos quedaron",
      "movidas} seccion(es) movidas" in ORDEN and "grupo(s) de elemento" in ORDEN)


# ======================================================================
#  7. UN SOLO PASO DE DESHACER
# ======================================================================
print("\n-- Deshacer --")

check("hay una bandera de reordenado", "private bool _reordenando;" in ORDEN)
check("se enciende antes de mover", "_reordenando = true;" in ORDEN)
check("y se apaga en un finally, no despues del bucle",
      re.search(r"finally\s*\{[^}]*_reordenando = false;", ORDEN, re.S) is not None,
      "si un Move fallara, la hoja se quedaria sin deshacer ni redibujar")
check("DatosCambiaron se rinde mientras se reordena",
      re.search(r"private void DatosCambiaron\(\)\s*\{.*?if \(_reordenando\)\s*\{\s*return;",
                CODIGO, re.S) is not None)
check("y el guardia va ANTES de apilar en el historial",
      antes(CODIGO, "if (_reordenando)", "RegistrarEnHistorial();"),
      "apilaria un paso por cada fila movida")
check("el boton avisa del cambio UNA sola vez",
      ORDEN.count("DatosCambiaron();") == 1,
      f"lo llama {ORDEN.count('DatosCambiaron();')} vez/veces")
check("y no avisa si no se movio nada",
      antes(ORDEN, '"Ordenar: las secciones ya estaban agrupadas."',
            "DatosCambiaron();"),
      "un deshacer que no deshace nada visible confunde")
check("y el globo del boton dice que Ctrl+Z lo deshace",
      "Ctrl+Z" in ORDEN and boton is not None and "Ctrl+Z" in boton.group(0))


# ======================================================================
#  8. EL COMPARADOR, DONDE LO ENCUENTRAN LAS COMPROBACIONES
# ======================================================================
print("\n-- El comparador de ID --")

check("el comparador existe",
      "internal sealed class ComparadorDeId : IComparer<string>" in FILAS)
check("y esta suelto, no anidado en la fila",
      re.search(r"^internal sealed class ComparadorDeId", FILAS, re.M) is not None,
      "anidado, corta la lectura de las propiedades de la fila en validar.py")
check("hay una sola instancia, la de PorId",
      FILAS.count("new ComparadorDeId()") == 1)
check("los vacios se resuelven antes de partir el ID",
      "if (x.Length == 0 || y.Length == 0)" in FILAS)
check("y el numero se compara como numero",
      "return nx.CompareTo(ny);" in FILAS)
check("el prefijo se compara sin distinguir mayusculas",
      "string.Compare(px, py, StringComparison.OrdinalIgnoreCase)" in FILAS)
check("un ID demasiado largo para un numero no tira excepcion",
      "long.TryParse(" in FILAS,
      "con int.Parse, un ID de veinte cifras tumbaria la aplicacion")


# ======================================================================
print()
print("=" * 66)

if fallos:
    print(f" RESULTADO: {len(fallos)} comprobacion(es) fallaron")

    for f in fallos:
        print(f"   - {f}")

    print("=" * 66)
    sys.exit(1)

print(" RESULTADO: todo bien. El boton Ordenar agrupa la hoja como se pidio.")
print("=" * 66)
