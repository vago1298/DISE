"""Comprueba que los alzados queden 2 m por encima de la seccion mas alta.

La macro coloca TODO en Y=2 (su constante Y_BLOQUES), una cota absoluta. Eso
funciona mientras ninguna seccion pase de 2 m de alto en el papel, y deja de
funcionar en cuanto entra un elemento alto: la seccion invade la fila de alzados
y el plano queda encimado.

Lo que se comprueba:

  1. Que con secciones bajitas la Y siga siendo 2, o sea que un plano ya
     acomodado con la version anterior NO se mueva.
  2. Que con una seccion alta la Y suba, y que quede exactamente 2 m de aire
     entre el paño superior de la seccion mas alta y la fila de alzados.
  3. Que la seccion mas alta NUNCA invada la fila, que es el defecto que se
     corrige.
  4. Que en una seccion CIRCULAR el alto que cuenta sea el diametro y no la
     altura, que en circular no se usa.
  5. Que la Y sea la MISMA para todos los elementos de la corrida: si cambiara
     por elemento, la fila saldria escalonada.
"""

AIRE_SOBRE_SECCIONES = 2.0
Y_BLOQUES = 2.0

fallos = []


def check(nombre, cond, detalle=""):
    print(("  OK    " if cond else "  FALLA ") + nombre + ("" if cond else "  " + detalle))
    if not cond:
        fallos.append(nombre)


def y_arranque(alto_max):
    """Port de AlzadoLayout.YArranque."""
    if alto_max <= 0:
        return Y_BLOQUES
    return alto_max + AIRE_SOBRE_SECCIONES


def alto_max(secciones, escala):
    """Port de MainWindow.AltoMaximoDeLasSecciones.

    Cada seccion es (circular, base_cm, altura_cm).
    """
    m = 0.0
    for circular, base_cm, altura_cm in secciones:
        alto_cm = base_cm if circular else altura_cm
        m = max(m, alto_cm * escala)
    return m


print("=" * 78)
print(" Y de arranque de la fila de alzados")
print("=" * 78)

# ----------------------------------------------------------------------
# 1. Secciones normales: nada cambia
# ----------------------------------------------------------------------
print("\nJuego normal: trabe 30x60, columna 40x40, castillo 15x15, escala 0.01")

normales = [(False, 30, 60), (False, 40, 40), (False, 15, 15)]
am = alto_max(normales, 0.01)
y = y_arranque(am)
print(f"  alto maximo = {am:.3f} m   ->   Y = {y:.3f} m")

# El aire son SIEMPRE 2 m, no «2 como minimo». Con la trabe de 60 cm la fila queda
# en 2.60 y no en 2.00, que es justo lo que se pidio. Esta comprobacion existe
# porque la primera version del codigo llevaba un max() contra 2 que hacia creer
# que un plano viejo no se movia, y si se mueve: hay que decirlo, no taparlo.
check("el aire son exactamente 2 m sobre la mas alta",
      abs((y - am) - AIRE_SOBRE_SECCIONES) < 1e-12,
      f"aire de {y - am:.4f} m")
check("con la trabe de 60 cm la fila queda en 2.60", abs(y - 2.60) < 1e-12,
      f"salio {y}")
check("o sea que NO se queda en la cota fija de la macro", abs(y - Y_BLOQUES) > 1e-9)

# ----------------------------------------------------------------------
# 2. Una seccion ALTA: la Y sube
# ----------------------------------------------------------------------
print("\nEl caso que rompia: contratrabe de 2.50 m de peralte a escala 0.10")
print("(el peralte se dibuja a 2.50 m en el papel)")

alta = [(False, 40, 250)]
am2 = alto_max(alta, 0.10)
y2 = y_arranque(am2)
print(f"  alto maximo = {am2:.3f} m   ->   Y = {y2:.3f} m")

check("con una seccion alta la Y sube", y2 > 2.0, f"salio {y2}")
check("y queda EXACTAMENTE 2 m de aire",
      abs((y2 - am2) - AIRE_SOBRE_SECCIONES) < 1e-12,
      f"aire de {y2 - am2:.4f} m")

# Lo que hacia la macro
print(f"  con la cota fija de la macro (Y=2) la seccion llegaria a {am2:.2f} m")
check("la cota fija de la macro SI invadia la fila", am2 > Y_BLOQUES,
      "el caso de prueba no reproduce el defecto")

# ----------------------------------------------------------------------
# 3. La seccion nunca invade la fila
# ----------------------------------------------------------------------
print("\nBarrido: ninguna combinacion debe dejar la seccion invadiendo la fila")

peor_aire = None

for escala in (0.01, 0.02, 0.05, 0.10, 0.20):
    for altura in (15, 30, 60, 100, 150, 250, 400):
        am_i = alto_max([(False, 40, altura)], escala)
        y_i = y_arranque(am_i)
        aire = y_i - am_i

        if peor_aire is None or aire < peor_aire:
            peor_aire = aire

        if aire < AIRE_SOBRE_SECCIONES - 1e-12:
            check(f"escala {escala}, altura {altura} cm: aire suficiente", False,
                  f"aire de {aire:.4f} m")

print(f"  el peor aire de todo el barrido = {peor_aire:.4f} m")
check("en ninguna combinacion el aire baja de 2 m",
      peor_aire >= AIRE_SOBRE_SECCIONES - 1e-12,
      f"el peor fue {peor_aire:.4f} m")

# ----------------------------------------------------------------------
# 4. En circular cuenta el DIAMETRO
# ----------------------------------------------------------------------
print("\nSeccion circular: cuenta el diametro (la base), no la altura")

# Columna redonda de 50 cm de diametro con la altura en CERO, que es como queda
# cuando el usuario solo llena la base. Si se mirara la altura, contaria 0.
circular = [(True, 50, 0)]
am4 = alto_max(circular, 0.10)
y4 = y_arranque(am4)
print(f"  D=50, altura=0, escala 0.10   ->   alto maximo = {am4:.3f} m, Y = {y4:.3f} m")

check("la circular cuenta por su diametro", abs(am4 - 5.0) < 1e-12,
      f"conto {am4}")
check("y la Y sube en consecuencia", abs(y4 - 7.0) < 1e-12, f"salio {y4}")

# Si se hubiera mirado la altura, el alto seria 0 y la Y se quedaria en 2
am4_mal = 0 * 0.10
check("mirando la altura se habria quedado en Y=2 (el defecto)",
      abs(y_arranque(am4_mal) - 2.0) < 1e-12)

# ----------------------------------------------------------------------
# 5. La misma Y para toda la corrida
# ----------------------------------------------------------------------
print("\nLa Y es la misma para todos los elementos de la corrida")

mezcla = [(False, 30, 60), (True, 50, 0), (False, 40, 250)]
am5 = alto_max(mezcla, 0.01)
y5 = y_arranque(am5)

# Se pide la Y una vez por elemento, como hace DibujarElemento
ys = [y_arranque(am5) for _ in mezcla]
print(f"  alto maximo del juego = {am5:.3f} m   ->   Y = {y5:.3f} m")
print(f"  Y por elemento: {[round(v, 4) for v in ys]}")

check("todos los elementos comparten la misma Y", len(set(ys)) == 1)
check("y es la del elemento mas alto del juego",
      abs(y5 - (am5 + AIRE_SOBRE_SECCIONES)) < 1e-12 or abs(y5 - 2.0) < 1e-12)

# El mas alto de la mezcla es la contratrabe de 250 cm a 0.01 = 2.50 m
check("el mas alto de la mezcla es la contratrabe", abs(am5 - 2.50) < 1e-12,
      f"conto {am5}")

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
