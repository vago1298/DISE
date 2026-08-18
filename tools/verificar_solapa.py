"""Comprueba la numeracion automatica del juego de planos.

Lo que pidio el usuario: "cuando se agregue un nuevo plano actualizar en automatico
el numero de planos y asi". La numeracion es una funcion del ORDEN de la lista, no
un dato que se escriba, y el detalle que se rompe solo cuando ya es tarde es el
TOTAL: al agregar el octavo plano, los siete anteriores tienen que pasar de "de 7"
a "de 8". Actualizar solo el nuevo deja seis planos mintiendo.

Aqui se traduce JuegoDePlanos.Renumerar y se comprueba en las cuatro operaciones
que de verdad ocurren: agregar al final, insertar en medio, borrar y reordenar.
"""

fallos = []


def check(nombre, ok, detalle=""):
    print(("  OK    " if ok else "  FALLA ") + nombre + ("" if ok else "  -> " + detalle))
    if not ok:
        fallos.append(nombre)


class Plano:
    def __init__(self, clave="", contiene=""):
        self.clave = clave
        self.contiene = contiene
        self.numero = 0
        self.total = 0

    @property
    def numero_texto(self):
        return f"{self.numero} de {self.total}" if self.total > 0 else str(self.numero)

    def __repr__(self):
        return f"{self.clave}[{self.numero_texto}]"


class Juego:
    """Port de JuegoDePlanos."""

    def __init__(self):
        self.planos = []

    def renumerar(self):
        total = len(self.planos)
        for i, p in enumerate(self.planos):
            p.numero = i + 1
            p.total = total

    def agregar(self, contiene="", clave=None):
        p = Plano(clave or f"E-{len(self.planos) + 1:02d}", contiene)
        self.planos.append(p)
        self.renumerar()
        return p

    def insertar(self, i, contiene="", clave=None):
        p = Plano(clave or f"E-{len(self.planos) + 1:02d}", contiene)
        self.planos.insert(i, p)
        self.renumerar()
        return p

    def borrar(self, i):
        del self.planos[i]
        self.renumerar()

    def mover(self, desde, hasta):
        p = self.planos.pop(desde)
        self.planos.insert(hasta, p)
        self.renumerar()


print("=" * 78)
print(" Numeracion automatica del juego de planos")
print("=" * 78)

j = Juego()

# --- Agregar al final ---
for n in range(1, 6):
    j.agregar(f"Plano {n}")
    print(f"  tras agregar {n}: " + "  ".join(p.numero_texto for p in j.planos))

check("todos numerados en orden",
      [p.numero for p in j.planos] == [1, 2, 3, 4, 5])
check("TODOS traen el total actualizado, no solo el ultimo",
      all(p.total == 5 for p in j.planos),
      f"totales {[p.total for p in j.planos]}")
check("la clave por omision sigue el numero",
      [p.clave for p in j.planos] == ["E-01", "E-02", "E-03", "E-04", "E-05"])

# --- Insertar en medio: es donde se rompe la numeracion escrita a mano ---
j.insertar(2, "Plano nuevo en medio")
print("\n  tras insertar en la posicion 2: " + "  ".join(p.numero_texto for p in j.planos))

check("al insertar en medio se renumera todo",
      [p.numero for p in j.planos] == [1, 2, 3, 4, 5, 6])
check("y el total sube en todos", all(p.total == 6 for p in j.planos))
check("el insertado queda con el numero de su posicion",
      j.planos[2].numero == 3)
check("y el que estaba ahi se corre",
      j.planos[3].contiene == "Plano 3")

# --- Borrar ---
j.borrar(0)
print("  tras borrar el primero:        " + "  ".join(p.numero_texto for p in j.planos))

check("al borrar se renumera todo",
      [p.numero for p in j.planos] == [1, 2, 3, 4, 5])
check("y el total baja en todos", all(p.total == 5 for p in j.planos))

# --- Reordenar ---
antes = [p.contiene for p in j.planos]
j.mover(4, 0)
print("  tras mover el ultimo al frente:" + "  ".join(p.numero_texto for p in j.planos))

check("al reordenar los numeros siguen el orden de la lista",
      [p.numero for p in j.planos] == [1, 2, 3, 4, 5])
check("el que se movio ahora es el 1", j.planos[0].contiene == antes[4])

# --- Un juego vacio y uno de un solo plano ---
v = Juego()
check("un juego vacio no truena", v.planos == [])

u = Juego()
u.agregar("Unico")
check("con un solo plano dice '1 de 1'", u.planos[0].numero_texto == "1 de 1",
      u.planos[0].numero_texto)

# --- Renombrar una clave a mano NO debe romper la serie de las siguientes ---
r = Juego()
for n in range(1, 4):
    r.agregar(f"P{n}")
r.planos[1].clave = "E-99"
nueva = r.agregar("P4")
check("renombrar una clave no rompe la serie de las siguientes",
      nueva.clave == "E-04", f"la nueva salio {nueva.clave}")
check("y los numeros no se enteran de las claves",
      [p.numero for p in r.planos] == [1, 2, 3, 4])

print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")
print("=" * 78)
