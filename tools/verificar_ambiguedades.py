"""Caza los CS0104 -«referencia ambigua»- sin necesidad de compilar en Windows.

POR QUE EXISTE ESTE SCRIPT
--------------------------
`CadLink.App` es WPF, asi que aqui no se puede compilar: no hay Windows ni estan los
ensamblados de referencia de WindowsDesktop. Lo que se hace en su lugar es pasarle el
compilador en modo «solo analisis sintactico», y eso NO ve los choques de nombres, porque
para verlos hay que resolver los tipos de verdad.

Y ese hueco dejo pasar un error real: `MainWindow.Acero.cs` escribia `new Path` para la
figura de la vista previa. `System.Windows.Shapes` define un `Path`, y el .csproj de la App
trae `<Using Include="System.IO" />` como using GLOBAL, que define otro. En Windows eso es:

    error CS0104: 'Path' es una referencia ambigua entre 'System.Windows.Shapes.Path' y
                  'System.IO.Path'

El programa no compilaba, y aqui todo daba verde. Este script cierra ese hueco.

COMO LO HACE
------------
Para cada archivo .cs se calcula que namespaces tiene a la vista -los `using` del archivo
MAS los globales, que salen del .csproj y de los implicitos del SDK- y se cruza con una
tabla de nombres de tipo que estan definidos en mas de un namespace de los que este
programa usa. Si dos de los namespaces de un nombre estan a la vista y el archivo escribe
ese nombre A SECAS, hace falta un alias; si no lo hay, es un CS0104.

La tabla es a mano, y tiene que serlo: sin los ensamblados de referencia no hay de donde
sacar la lista de tipos de cada namespace. Lo que se cubre son los choques de las
bibliotecas que este programa usa -WPF, System.IO, y los de WinForms y System.Drawing por
si alguien los agrega-, que son los que pueden aparecer de verdad.
"""

import os
import re

fallos = []
avisos = []


def check(nombre, cond, detalle=""):
    print(f"  {'OK  ' if cond else 'FALLA'}  {nombre}"
          + (f"   [{detalle}]" if detalle and not cond else ""))
    if not cond:
        fallos.append(f"{nombre} {detalle}".strip())


# ===========================================================================
#  Los nombres que estan en dos sitios
# ===========================================================================
#
# Nombre de tipo -> los namespaces que lo definen. Solo hace falta apuntar los namespaces
# que este programa puede llegar a importar: un choque entre dos namespaces que nadie
# importa no puede dar un error.
CHOQUES = {
    # El que rompio la compilacion.
    "Path": ("System.IO", "System.Windows.Shapes"),

    # WPF contra System.Drawing. Pasa en cuanto alguien toca imagenes o impresion.
    "Brush": ("System.Windows.Media", "System.Drawing"),
    "Brushes": ("System.Windows.Media", "System.Drawing"),
    "Pen": ("System.Windows.Media", "System.Drawing"),
    "Color": ("System.Windows.Media", "System.Drawing"),
    "Colors": ("System.Windows.Media", "System.Drawing"),
    "FontFamily": ("System.Windows.Media", "System.Drawing"),
    "Matrix": ("System.Windows.Media", "System.Drawing.Drawing2D"),
    "Point": ("System.Windows", "System.Drawing"),
    "Size": ("System.Windows", "System.Drawing"),
    "Rectangle": ("System.Windows.Shapes", "System.Drawing"),
    "Image": ("System.Windows.Controls", "System.Drawing"),

    # WPF contra WinForms. El clasico de un proyecto que arrastra las dos.
    "Application": ("System.Windows", "System.Windows.Forms"),
    "MessageBox": ("System.Windows", "System.Windows.Forms"),
    "Label": ("System.Windows.Controls", "System.Windows.Forms"),
    "TextBox": ("System.Windows.Controls", "System.Windows.Forms"),
    "Button": ("System.Windows.Controls", "System.Windows.Forms"),
    "ComboBox": ("System.Windows.Controls", "System.Windows.Forms"),
    "CheckBox": ("System.Windows.Controls", "System.Windows.Forms"),
    "DataGrid": ("System.Windows.Controls", "System.Windows.Forms"),
    "ProgressBar": ("System.Windows.Controls", "System.Windows.Forms"),
    "KeyEventArgs": ("System.Windows.Input", "System.Windows.Forms"),
    "MouseEventArgs": ("System.Windows.Input", "System.Windows.Forms"),
    "Cursor": ("System.Windows.Input", "System.Windows.Forms"),
    "Cursors": ("System.Windows.Input", "System.Windows.Forms"),
    "Clipboard": ("System.Windows", "System.Windows.Forms"),
    "Orientation": ("System.Windows.Controls", "System.Windows.Forms"),

    # Y los relojes, que estan en tres sitios.
    "Timer": ("System.Threading", "System.Timers", "System.Windows.Forms"),
}

# Los using IMPLICITOS del SDK. Y aqui hay un detalle que importa: en un proyecto de WPF o
# de WinForms la lista NO incluye System.IO, al contrario que en una biblioteca normal. Por
# eso el .csproj de la App lo agrega a mano, y por eso Path choca en TODO el proyecto.
IMPLICITOS_SDK = (
    "System",
    "System.Collections.Generic",
    "System.Linq",
    "System.Threading",
    "System.Threading.Tasks",
)

# En una biblioteca normal -sin UseWPF ni UseWindowsForms- si entra System.IO, y tambien
# System.Net.Http.
IMPLICITOS_BIBLIOTECA = IMPLICITOS_SDK + ("System.IO", "System.Net.Http")


def sin_comentarios_ni_textos(codigo):
    """El codigo sin comentarios ni cadenas, para no leer nombres donde no hay codigo.

    Un `Path` dentro de un comentario que explica el problema no es un uso, y una ruta
    escrita en una cadena tampoco. Sin esto, el propio comentario que documenta el arreglo
    haria fallar la comprobacion.
    """
    fuera = []
    i = 0
    n = len(codigo)

    while i < n:
        c = codigo[i]
        dos = codigo[i:i + 2]

        if dos == "//":
            fin = codigo.find("\n", i)
            i = n if fin < 0 else fin
        elif dos == "/*":
            fin = codigo.find("*/", i + 2)
            i = n if fin < 0 else fin + 2
        elif dos in ('@"', '$"') or c == '"':
            # Cadena literal, verbatim o interpolada. Se salta hasta su cierre; con las
            # verbatim el escape es "" y con las normales \".
            verbatim = dos == '@"'
            i += 2 if dos in ('@"', '$"') else 1

            while i < n:
                if codigo[i] == "\\" and not verbatim:
                    i += 2
                    continue

                if codigo[i] == '"':
                    if verbatim and codigo[i:i + 2] == '""':
                        i += 2
                        continue
                    i += 1
                    break

                i += 1
        elif c == "'":
            i += 2 if codigo[i:i + 2] != "'\\" else 4

            while i < n and codigo[i] != "'":
                i += 1
            i += 1
        else:
            fuera.append(c)
            i += 1

    return "".join(fuera)


def globales_del_csproj(ruta_csproj):
    """Los namespaces que un .csproj mete en TODOS sus archivos.

    Son los implicitos del SDK -que dependen de si el proyecto es WPF- mas los
    `<Using Include="..." />` escritos a mano, menos los `<Using Remove="..." />`.
    """
    with open(ruta_csproj, encoding="utf-8") as f:
        proyecto = f.read()

    es_wpf = ("<UseWPF>true</UseWPF>" in proyecto
              or "<UseWindowsForms>true</UseWindowsForms>" in proyecto)

    implicitos = ("<ImplicitUsings>enable</ImplicitUsings>" in proyecto)

    globales = set()

    if implicitos:
        globales |= set(IMPLICITOS_SDK if es_wpf else IMPLICITOS_BIBLIOTECA)

    globales |= set(re.findall(r'<Using\s+Include="([^"]+)"', proyecto))
    globales -= set(re.findall(r'<Using\s+Remove="([^"]+)"', proyecto))

    return globales, es_wpf, implicitos


def usings_del_archivo(codigo):
    """Los namespaces que importa el archivo, y los alias que declara.

    Un `using X = ...;` NO importa un namespace: declara un nombre. Y es justo lo que
    resuelve una ambiguedad, asi que hay que distinguirlos.
    """
    importados = set()
    alias = set()

    for linea in codigo.splitlines():
        m = re.match(r"\s*(?:global\s+)?using\s+(?:static\s+)?([^;]+);", linea)

        if not m:
            continue

        cuerpo = m.group(1).strip()
        m_alias = re.match(r"(\w+)\s*=\s*(.+)", cuerpo)

        if m_alias:
            alias.add(m_alias.group(1))
        else:
            importados.add(cuerpo)

    return importados, alias


def sin_directivas_using(codigo):
    """El codigo sin las lineas de `using` de cabecera.

    Hace falta porque el propio alias -`using Path = System.IO.Path;`- escribe el nombre, y
    sin quitarlo cualquier archivo que declare el alias contaria como que USA el nombre.
    Solo se quitan las directivas de cabecera, las que empiezan en la columna cero: un
    `using var lector = ...` dentro de un metodo SI es codigo y tiene que contarse.
    """
    return "\n".join(
        "" if re.match(r"(?:global\s+)?using\s+(?:static\s+)?[\w\.]+\s*(?:=|;)", linea)
        else linea
        for linea in codigo.splitlines())


def usa_el_nombre(codigo, nombre):
    """Si el archivo escribe ese nombre A SECAS, o sea sin namespace delante.

    `System.Windows.Shapes.Path` no es ambiguo, y `p.Path` es un miembro, no un tipo: los
    dos van precedidos de un punto y no cuentan. Tampoco cuenta el nombre cuando forma
    parte de un identificador mas largo -`PathGeometry`, `RutaPath`-, de eso se encarga la
    frontera de palabra.
    """
    return re.search(r"(?<![\w.])" + nombre + r"\b", codigo) is not None


# ===========================================================================
#  El recorrido
# ===========================================================================

print("=" * 78)
print(" Nombres ambiguos: los CS0104 que el analisis sintactico no ve")
print("=" * 78)

RAIZ = "client/src"

proyectos = []

for carpeta in sorted(os.listdir(RAIZ)):
    ruta = os.path.join(RAIZ, carpeta)

    if not os.path.isdir(ruta):
        continue

    csproj = os.path.join(ruta, carpeta + ".csproj")

    if os.path.exists(csproj):
        proyectos.append((carpeta, ruta, csproj))

check("se encontraron los proyectos", len(proyectos) >= 4,
      f"{[p[0] for p in proyectos]}")

# La comprobacion que da sentido a todo lo demas: que en la App System.IO es global. Si
# alguien lo quita del .csproj, este script dejaria de buscar el choque de Path... y el
# choque tambien desapareceria, asi que se informa del estado, no se exige uno.
for nombre, ruta, csproj in proyectos:
    globales, es_wpf, implicitos = globales_del_csproj(csproj)

    print(f"\n{nombre}"
          f"{'  (WPF)' if es_wpf else ''}"
          f"{'' if implicitos else '  (sin using implicitos)'}")
    print(f"    globales: {', '.join(sorted(globales)) or 'ninguno'}")

    archivos = []

    for base, _, ficheros in os.walk(ruta):
        if os.sep + "obj" in base or os.sep + "bin" in base:
            continue

        for f in sorted(ficheros):
            if f.endswith(".cs") and not f.endswith(".g.cs"):
                archivos.append(os.path.join(base, f))

    ambiguos = []
    resueltos = []

    for archivo in archivos:
        with open(archivo, encoding="utf-8") as f:
            codigo = f.read()

        importados, alias = usings_del_archivo(codigo)
        limpio = sin_directivas_using(sin_comentarios_ni_textos(codigo))
        a_la_vista = globales | importados

        for tipo, duenos in CHOQUES.items():
            presentes = [d for d in duenos if d in a_la_vista]

            if len(presentes) < 2:
                continue

            if not usa_el_nombre(limpio, tipo):
                continue

            corto = os.path.relpath(archivo, ruta)

            if tipo in alias:
                resueltos.append(f"{corto}: {tipo}")
            else:
                ambiguos.append(
                    f"{corto}: «{tipo}» a secas con {' y '.join(presentes)} a la vista")

    for r in resueltos:
        print(f"    resuelto con alias  {r}")

    check(f"{nombre}: ningun nombre ambiguo sin alias", not ambiguos,
          "; ".join(ambiguos))

    if not archivos:
        avisos.append(f"{nombre}: no se encontro ningun .cs")


# ===========================================================================
#  Y que el script SIRVE: se prueba contra el error que se dejo pasar
# ===========================================================================
#
# Un detector que nunca ha detectado nada no se distingue de uno roto. Aqui se le pasa el
# codigo tal como estaba cuando el usuario no pudo compilar, y tiene que cazarlo; y la
# version arreglada, que tiene que pasar.
print("\n" + "=" * 78)
print(" El detector, probado contra el error de verdad")
print("=" * 78)

ROTO = '''using System.Windows;
using System.Windows.Shapes;

namespace CadLink.App;

public partial class MainWindow
{
    private void Dibujar()
    {
        Lienzo.Children.Add(new Path { StrokeThickness = 1.6 });
    }
}
'''

ARREGLADO = '''using System.Windows;
using System.Windows.Shapes;

using Path = System.IO.Path;
using FormaPath = System.Windows.Shapes.Path;

namespace CadLink.App;

public partial class MainWindow
{
    private void Dibujar()
    {
        Lienzo.Children.Add(new FormaPath { StrokeThickness = 1.6 });
    }
}
'''

# Y uno que MENCIONA el nombre en un comentario y en una cadena, pero no lo usa: no puede
# dar falso positivo, porque si no, el comentario que explica el arreglo haria fallar.
SOLO_HABLA = '''using System.Windows.Shapes;

namespace CadLink.App;

// Aqui antes se escribia Path a secas y no compilaba.
public static class Ayuda
{
    public const string Nota = "el Path de System.IO";
}
'''


def es_ambiguo(codigo, globales):
    importados, alias = usings_del_archivo(codigo)
    limpio = sin_directivas_using(sin_comentarios_ni_textos(codigo))
    a_la_vista = globales | importados

    for tipo, duenos in CHOQUES.items():
        if len([d for d in duenos if d in a_la_vista]) < 2:
            continue

        if usa_el_nombre(limpio, tipo) and tipo not in alias:
            return tipo

    return None


GLOBALES_APP = set(IMPLICITOS_SDK) | {"System.IO"}

check("caza el «new Path» que rompio la compilacion",
      es_ambiguo(ROTO, GLOBALES_APP) == "Path")
check("y da por bueno el arreglo con alias",
      es_ambiguo(ARREGLADO, GLOBALES_APP) is None,
      f"señalo {es_ambiguo(ARREGLADO, GLOBALES_APP)}")
check("no se queja de un comentario o una cadena que solo mencionan el nombre",
      es_ambiguo(SOLO_HABLA, GLOBALES_APP) is None,
      f"señalo {es_ambiguo(SOLO_HABLA, GLOBALES_APP)}")

# Y sin el using global de System.IO no hay choque: un solo dueño a la vista.
check("sin System.IO global el mismo codigo no es ambiguo",
      es_ambiguo(ROTO, set(IMPLICITOS_SDK)) is None)

# El nombre pegado a otro no cuenta: PathGeometry es un tipo distinto.
check("un nombre mas largo que lo contiene no cuenta",
      es_ambiguo(
          "using System.Windows.Shapes;\nvar g = new PathGeometry();\n",
          GLOBALES_APP) is None)

# Ni el nombre calificado, que es la otra manera legitima de escribirlo.
check("el nombre calificado con su namespace tampoco cuenta",
      es_ambiguo(
          "using System.Windows.Shapes;\n"
          "var p = new System.Windows.Shapes.Path();\n",
          GLOBALES_APP) is None)


print("\n" + "=" * 78)
if fallos:
    print(f" {len(fallos)} PROBLEMA(S):")
    for f in fallos:
        print("   - " + f)
else:
    print(" Todo correcto.")

if avisos:
    print(f"\n {len(avisos)} AVISO(S), que no son fallos:")
    for a in avisos:
        print("   - " + a)
print("=" * 78)

raise SystemExit(1 if fallos else 0)
