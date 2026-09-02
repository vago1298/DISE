#!/usr/bin/env python3
"""
Validaciones estaticas del proyecto CadLink.

Este entorno (Linux) no tiene .NET, asi que no se puede compilar ni ejecutar
el cliente C#. Estas comprobaciones atrapan de forma estatica las clases de
error que ya rompieron la compilacion en la maquina del usuario, para no
volver a mandarle un zip que no compila.

Uso:
    python tools/validar.py

Salida: una linea por comprobacion y un resumen. Codigo de salida != 0 si
alguna comprobacion falla.
"""

from __future__ import annotations

import ast
import os
import re
import sys
import xml.etree.ElementTree as ET

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

fallos: list[str] = []
avisos: list[str] = []


def ruta(*partes: str) -> str:
    return os.path.join(RAIZ, *partes)


def leer(p: str) -> str:
    with open(p, encoding="utf-8-sig") as f:
        return f.read()


def archivos(ext: str, subdir: str = "") -> list[str]:
    base = ruta(subdir) if subdir else RAIZ
    encontrados = []
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [
            d for d in dirnames
            if d not in {"bin", "obj", "__pycache__", ".venv", ".git", "keys"}
        ]
        for fn in filenames:
            if fn.endswith(ext):
                encontrados.append(os.path.join(dirpath, fn))
    return sorted(encontrados)


def rel(p: str) -> str:
    return os.path.relpath(p, RAIZ)


def check(nombre: str, ok: bool, detalle: str = "") -> None:
    if ok:
        print(f"  OK    {nombre}")
    else:
        print(f"  FALLA {nombre}" + (f" -> {detalle}" if detalle else ""))
        fallos.append(f"{nombre}: {detalle}")


# ======================================================================
# 1. XML bien formado (csproj, sln-adjacent, xaml) y sin '--' en comentarios
# ======================================================================
def v1_xml() -> None:
    print("\n[1] XML bien formado (.csproj / .xaml)")
    malos = []
    dobles = []
    for p in archivos(".csproj") + archivos(".xaml"):
        txt = leer(p)
        try:
            ET.fromstring(txt)
        except ET.ParseError as e:
            malos.append(f"{rel(p)}: {e}")
        # '--' dentro de un comentario XML es ilegal (rompio la build antes:
        # '--self-contained' dentro de <!-- ... --> => MSB4025)
        for m in re.finditer(r"<!--(.*?)-->", txt, re.S):
            if "--" in m.group(1):
                linea = txt[: m.start()].count("\n") + 1
                dobles.append(f"{rel(p)}:{linea}")
    check("todos los XML parsean", not malos, "; ".join(malos))
    check("sin '--' dentro de comentarios XML", not dobles, "; ".join(dobles))


# ======================================================================
# 2. .bat sin bloques de parentesis y con todas las etiquetas goto definidas
# ======================================================================
def v2_bat() -> None:
    print("\n[2] Archivos .bat")
    con_bloque = []
    faltan_labels = []
    for p in archivos(".bat"):
        txt = leer(p)
        lineas = txt.splitlines()
        etiquetas = {
            m.group(1).lower()
            for m in re.finditer(r"^\s*:([A-Za-z0-9_\-]+)", txt, re.M)
        }
        etiquetas.add("eof")
        for i, ln in enumerate(lineas, 1):
            s = ln.strip()
            # un 'if ... (' abre bloque; dentro de un bloque un echo con ')'
            # cierra el bloque antes de tiempo. Se prohiben los bloques.
            if re.match(r"^(if|for)\b.*\($", s, re.I) or re.search(
                r"^\s*(if|for)\b.*\(\s*$", s, re.I
            ):
                con_bloque.append(f"{rel(p)}:{i}")
            for m in re.finditer(r"\bgoto\s+:?([A-Za-z0-9_\-]+)", ln, re.I):
                if m.group(1).lower() not in etiquetas:
                    faltan_labels.append(f"{rel(p)}:{i} -> {m.group(1)}")
    check("sin bloques ( ) en if/for", not con_bloque, "; ".join(con_bloque))
    check("todas las etiquetas goto existen", not faltan_labels, "; ".join(faltan_labels))


# ======================================================================
# 3. Usings faltantes por proyecto
#    Los proyectos WPF NO reciben System.IO / System.Net.Http implicitos.
# ======================================================================
IMPLICITOS_BASE = {
    "System", "System.Collections.Generic", "System.IO", "System.Linq",
    "System.Net.Http", "System.Threading", "System.Threading.Tasks",
}
# WPF (Microsoft.NET.Sdk con UseWPF) usa un set distinto
IMPLICITOS_WPF = {
    "System", "System.Collections.Generic", "System.Linq",
    "System.Threading", "System.Threading.Tasks",
}

TIPOS = {
    "System.IO": ["Path", "File", "Directory", "IOException", "Stream",
                  "FileStream", "StreamReader", "StreamWriter", "FileInfo",
                  "DirectoryInfo", "MemoryStream", "SearchOption"],
    "System.Net.Http": ["HttpClient", "HttpResponseMessage", "HttpRequestMessage",
                        "StringContent", "HttpMethod", "HttpContent"],
    "System.Linq": ["Enumerable"],
    "System.Text": ["Encoding", "StringBuilder"],
    "System.Text.Json": ["JsonSerializer", "JsonDocument", "JsonElement",
                         "JsonSerializerOptions"],
    "System.Collections.Generic": ["List", "Dictionary", "HashSet", "IEnumerable"],
    "System.Globalization": ["CultureInfo"],
    "System.Runtime.InteropServices": ["Marshal", "DllImport", "ComVisible"],
    "System.Security.Cryptography": ["RSA", "SHA256", "ProtectedData"],
    "System.Diagnostics": ["Process", "Stopwatch", "Debug"],
    "System.Management": ["ManagementObjectSearcher", "ManagementClass"],
    "System.Reflection": ["BindingFlags", "ParameterModifier"],
    "System.Windows.Media.Imaging": ["BitmapDecoder", "BitmapFrame", "BitmapImage"],
}


def proyecto_de(p: str) -> str | None:
    d = os.path.dirname(p)
    while d.startswith(RAIZ):
        cands = [f for f in os.listdir(d) if f.endswith(".csproj")]
        if cands:
            return os.path.join(d, cands[0])
        d = os.path.dirname(d)
    return None


def v3_usings() -> None:
    print("\n[3] Usings faltantes (por proyecto)")
    # cache: csproj -> (es_wpf, usings_explicitos)
    info: dict[str, tuple[bool, set[str]]] = {}
    for cs in archivos(".csproj"):
        txt = leer(cs)
        es_wpf = "<UseWPF>true</UseWPF>" in txt.replace(" ", "")
        explicitos = set(
            re.findall(r'<Using\s+Include="([^"]+)"', txt)
        )
        implicit_disabled = "<ImplicitUsings>disable" in txt
        base = set() if implicit_disabled else (
            IMPLICITOS_WPF if es_wpf else IMPLICITOS_BASE
        )
        info[cs] = (es_wpf, base | explicitos)

    problemas = []
    for p in archivos(".cs"):
        if os.sep + "obj" + os.sep in p or os.sep + "bin" + os.sep in p:
            continue
        proj = proyecto_de(p)
        if proj is None or proj not in info:
            continue
        _, disponibles = info[proj]
        txt = leer(p)
        # quitar comentarios y cadenas para no dar falsos positivos
        limpio = re.sub(r"//[^\n]*", "", txt)
        limpio = re.sub(r"/\*.*?\*/", "", limpio, flags=re.S)
        limpio = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', limpio)
        # quitar los nombres COMPLETAMENTE CALIFICADOS. Escribir
        # System.Globalization.CultureInfo.InvariantCulture no necesita ningun
        # using; si no se quita, '\bCultureInfo\.' casa dentro del nombre
        # calificado y produce un falso positivo. Se borra toda cadena con dos
        # puntos o mas (un solo punto, como Path.Combine, si necesita using).
        limpio = re.sub(
            r"\b(?:[A-Za-z_][A-Za-z0-9_]*\.){2,}[A-Za-z_][A-Za-z0-9_]*\b",
            "CALIFICADO", limpio,
        )
        locales = set(re.findall(r"^\s*(?:global\s+)?using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;", txt, re.M))
        # alias: using Path = System.IO.Path;
        alias = set(re.findall(r"^\s*using\s+([A-Za-z0-9_]+)\s*=", txt, re.M))
        tiene = disponibles | locales
        for ns, tipos in TIPOS.items():
            if ns in tiene:
                continue
            for t in tipos:
                if t in alias:
                    continue
                # uso como tipo o miembro estatico
                if re.search(rf"\b{t}\s*\.", limpio) or re.search(
                    rf"\b(?:new\s+){t}\b", limpio
                ) or re.search(rf"\bcatch\s*\(\s*{t}\b", limpio) or re.search(
                    rf"^\s*(?:private|public|internal|protected|static|readonly|\s)*{t}[\?\s]", limpio, re.M
                ):
                    problemas.append(f"{rel(p)}: '{t}' necesita using {ns}")
                    break
    check("sin usings faltantes", not problemas, "; ".join(sorted(set(problemas))[:12]))


# ======================================================================
# 4. CS0050: miembro public que expone un tipo internal
# ======================================================================
def v4_cs0050() -> None:
    print("\n[4] CS0050 (public expone internal)")
    internos: set[str] = set()
    for p in archivos(".cs"):
        for m in re.finditer(
            r"^\s*internal\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*"
            r"(?:class|record|struct|interface|enum)\s+([A-Za-z0-9_]+)",
            leer(p), re.M,
        ):
            internos.add(m.group(1))

    problemas = []
    for p in archivos(".cs"):
        txt = leer(p)

        # Declaraciones de tipo con su accesibilidad, en orden de aparicion.
        # Hace falta porque un miembro 'public' dentro de una clase 'internal'
        # tiene accesibilidad EFECTIVA internal: eso es legal y no es CS0050.
        decls = [
            (m.start(), m.group(1))
            for m in re.finditer(
                r"^\s*(public|internal|private|protected)\s+"
                r"(?:sealed\s+|abstract\s+|partial\s+|static\s+)*"
                r"(?:class|record|struct|interface)\b",
                txt, re.M,
            )
        ]

        def contenedor_es_public(pos: int) -> bool:
            acc = None
            for start, a in decls:
                if start < pos:
                    acc = a
                else:
                    break
            # sin declaracion previa no se puede afirmar nada: no se reporta
            return acc == "public"

        # metodos/propiedades public cuyo tipo de retorno es internal
        for m in re.finditer(
            r"^\s*public\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+)*"
            r"([A-Za-z0-9_<>\?\[\],\s\.]+?)\s+([A-Za-z0-9_]+)\s*[\(\{=]",
            txt, re.M,
        ):
            if not contenedor_es_public(m.start()):
                continue
            ret = m.group(1)
            for t in internos:
                if re.search(rf"\b{t}\b", ret):
                    linea = txt[: m.start()].count("\n") + 1
                    problemas.append(f"{rel(p)}:{linea} devuelve internal '{t}'")
    check("ningun public devuelve internal", not problemas, "; ".join(problemas))


# ======================================================================
# 5. '.Value' sobre un tipo no nullable despues de '?? throw'
# ======================================================================
def v5_value() -> None:
    print("\n[5] '.Value' tras '?? throw' (CS1061)")
    problemas = []
    for p in archivos(".cs"):
        txt = leer(p)
        # var x = algo ?? throw ...;  luego  x.Value
        for m in re.finditer(
            r"var\s+([A-Za-z0-9_]+)\s*=\s*[^;]*\?\?\s*throw[^;]*;", txt
        ):
            var = m.group(1)
            resto = txt[m.end():]
            mm = re.search(rf"\b{var}\.Value\b", resto)
            if mm:
                linea = txt[: m.end() + mm.start()].count("\n") + 1
                problemas.append(f"{rel(p)}:{linea} {var}.Value")
    check("sin '.Value' redundante", not problemas, "; ".join(problemas))


# ======================================================================
# 6. Handlers de XAML existen en el code-behind
# ======================================================================
def v6_handlers() -> None:
    print("\n[6] Handlers de XAML definidos en code-behind")
    problemas = []
    for x in archivos(".xaml"):
        cb = x + ".cs"
        if not os.path.exists(cb):
            continue

        # El code-behind puede estar repartido en VARIOS archivos parciales, y hay que
        # leerlos todos. Antes solo se leia MainWindow.xaml.cs, asi que un handler que
        # viviera en MainWindow.Acero.cs se reportaba como inexistente: la comprobacion
        # daba un falso positivo justo cuando el code-behind se parte para no crecer sin
        # freno, que es lo que se quiere que se pueda hacer.
        base = os.path.basename(x)[: -len(".xaml")]
        carpeta = os.path.dirname(x)

        codigo = "\n".join(
            leer(os.path.join(carpeta, f))
            for f in sorted(os.listdir(carpeta))
            if f.endswith(".cs") and (f == base + ".xaml.cs" or f.startswith(base + "."))
        )

        txt = leer(x)
        handlers = set()
        # 'Executed' y 'CanExecute' van en la lista igual que 'Click'. Sin ellos,
        # cablear un boton por ApplicationCommands se saltaba esta comprobacion
        # entera: el metodo podia no existir y aqui nadie se enteraba.
        for m in re.finditer(
            r'\b(?:Click|Checked|Unchecked|SelectionChanged|TextChanged|Loaded|'
            r'Closing|Closed|MouseDown|MouseUp|KeyDown|KeyUp|GotFocus|LostFocus|'
            r'SizeChanged|PreviewKeyDown|Drop|DragOver|Executed|CanExecute)'
            r'\s*=\s*"([A-Za-z0-9_]+)"',
            txt,
        ):
            handlers.add(m.group(1))
        for hd in sorted(handlers):
            if not re.search(rf"\b(?:void|Task)\s+{hd}\s*\(", codigo):
                problemas.append(f"{rel(x)} -> {hd}")
    check("todos los handlers existen", not problemas, "; ".join(problemas))


# ======================================================================
# 7. x:Name usados en code-behind existen en el XAML
# ======================================================================
# Tipos del framework que terminan como un nombre de control pero NO son
# controles nombrados en el XAML. Sin esta lista, 'MessageBox.Show(...)' se
# reporta como un x:Name inexistente.
TIPOS_FRAMEWORK = {
    "MessageBox", "MessageBoxButton", "MessageBoxImage", "MessageBoxResult",
    "CheckBox", "TextBox", "ComboBox", "ListBox", "GroupBox", "PasswordBox",
    "Button", "RadioButton", "ToggleButton", "RepeatButton",
    "Grid", "StackPanel", "DockPanel", "WrapPanel", "Panel", "Canvas",
    "TabControl", "TabItem", "Border", "Slider", "Image", "Label",
    "ComboBoxItem", "ListBoxItem", "TextBlock", "ScrollViewer",
    "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog",
    "MouseButton", "MouseButtonState", "Mouse", "Keyboard", "Key",
    "Clipboard", "Application", "Window", "Brushes", "Colors", "Color",
    "SolidColorBrush", "FontWeights", "HorizontalAlignment",
    "VerticalAlignment", "Visibility", "Cursors",
}


def v7_names() -> None:
    print("\n[7] x:Name referenciados existen en XAML")
    problemas = []
    for x in archivos(".xaml"):
        cb = x + ".cs"
        if not os.path.exists(cb):
            continue
        nombres = set(re.findall(r'x:Name\s*=\s*"([A-Za-z0-9_]+)"', leer(x)))
        codigo = leer(cb)
        codigo = re.sub(r"//[^\n]*", "", codigo)
        codigo = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', codigo)
        # identificadores declarados localmente en el code-behind
        locales = set(re.findall(r"\b(?:var|int|double|string|bool|object)\s+([A-Za-z0-9_]+)\s*=", codigo))
        locales |= set(re.findall(r"\b(?:private|public|internal|protected)\s+[A-Za-z0-9_<>\?\.]+\s+([A-Za-z0-9_]+)\s*[;=\{]", codigo))
        locales |= set(re.findall(r"([A-Za-z0-9_]+)\s*=>", codigo))
        # candidatos: Algo.Propiedad donde Algo parece un control WPF
        for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]*(?:Text|Radio|Box|Btn|Button|Grid|Panel|Canvas|List|Combo|Check|Label|Image|Tab|Border|Slider))\.", codigo):
            n = m.group(1)
            if n in nombres or n in locales or n in TIPOS_FRAMEWORK:
                continue
            problemas.append(f"{rel(cb)} -> {n}")
    check("sin x:Name inexistentes", not problemas, "; ".join(sorted(set(problemas))[:12]))


# ======================================================================
# 8. Python del servidor compila
# ======================================================================
def v8_python() -> None:
    print("\n[8] Sintaxis de Python")
    problemas = []
    for p in archivos(".py"):
        try:
            ast.parse(leer(p))
        except SyntaxError as e:
            problemas.append(f"{rel(p)}:{e.lineno} {e.msg}")
    check("todo el Python parsea", not problemas, "; ".join(problemas))


# ======================================================================
# 9. Modo de seccion (tipo 1 / tipo 2): dataflow real, no coincidencia de texto
#
#    La version anterior de esta comprobacion buscaba el texto literal
#    'conFondo: rellena' y daba un FALSO POSITIVO, porque las llamadas pasan
#    el argumento por posicion. Ahora se sigue el flujo de verdad.
# ======================================================================
def v9_modo() -> None:
    print("\n[9] Estilo tipo 1 / tipo 2 conectado de punta a punta")
    drawer = ruta("client", "src", "CadLink.Cad", "SeccionDrawer.cs")
    seccion = ruta("client", "src", "CadLink.Cad", "SeccionCad.cs")
    ventana = ruta("client", "src", "CadLink.App", "MainWindow.xaml")
    codigo = ruta("client", "src", "CadLink.App", "MainWindow.xaml.cs")

    for p in (drawer, seccion, ventana, codigo):
        if not os.path.exists(p):
            check("archivos del modo presentes", False, f"falta {rel(p)}")
            return
    check("archivos del modo presentes", True)

    td = leer(drawer)
    ts = leer(seccion)
    tv = leer(ventana)
    tc = leer(codigo)

    # a) el enum tiene los dos valores
    check(
        "enum ModoSeccion con Tipo1SinRelleno y Tipo2Rellena",
        "Tipo1SinRelleno" in ts and "Tipo2Rellena" in ts,
    )

    # b) la UI ofrece las dos opciones y las mapea al enum
    check(
        "radios tipo 1 / tipo 2 en el XAML",
        "Tipo1Radio" in tv and "Tipo2Radio" in tv,
    )
    check(
        "la UI mapea el radio al enum",
        "ModoSeccion.Tipo1SinRelleno" in tc and "ModoSeccion.Tipo2Rellena" in tc,
    )

    # c) el drawer deriva el flag de fondo solido desde el modo
    m = re.search(r"var\s+(\w+)\s*=\s*s\.Modo\s+switch", td)
    check("el drawer deriva el flag desde s.Modo", m is not None)
    if not m:
        return
    flag = m.group(1)

    # el switch debe mandar Tipo2 a false
    bloque = td[m.end(): m.end() + 400]
    check(
        "Tipo1SinRelleno => false",
        re.search(r"Tipo1SinRelleno\s*=>\s*false", bloque) is not None,
    )
    check(
        "Tipo2Rellena => true",
        re.search(r"Tipo2Rellena\s*=>\s*true", bloque) is not None,
    )

    # d) el flag llega a HatchDeConcreto (por posicion o por nombre)
    llam = re.search(r"HatchDeConcreto\((.*?)\);", td, re.S)
    check(
        f"'{flag}' se pasa a HatchDeConcreto",
        llam is not None and re.search(rf"\b{flag}\b", llam.group(1)) is not None,
    )

    # e) la firma de HatchDeConcreto lo recibe y ParteHatch lo usa como conFondo
    firma = re.search(r"private\s+void\s+HatchDeConcreto\((.*?)\)\s*\{", td, re.S)
    param = None
    if firma:
        ps = firma.group(1)
        mp = re.search(r"bool\s+(\w+)\s*\)?\s*$", ps.strip())
        if mp:
            param = mp.group(1)
    check("HatchDeConcreto recibe un bool final", param is not None, str(firma is not None))

    if param:
        # Todas las LLAMADAS a ParteHatch deben pasar ese parametro. Hay que
        # excluir la DECLARACION del metodo: si no, el patron casa desde
        # 'private void ParteHatch(' hasta el primer ');' del cuerpo y reporta
        # un falso positivo.
        llamadas = [
            m.group(1)
            for m in re.finditer(r"(?<!void\s)\bParteHatch\(([^;()]*(?:\([^()]*\)[^;()]*)*)\);", td, re.S)
            if "bool " not in m.group(1)
        ]
        check("hay llamadas a ParteHatch", len(llamadas) >= 2, f"{len(llamadas)}")
        malas = [c.strip() for c in llamadas if not re.search(rf"\b{param}\b", c)]
        check("toda llamada a ParteHatch propaga el flag", not malas, "; ".join(malas))

    # f) ParteHatch pinta el fondo SOLID solo si el flag es verdadero
    ph = re.search(
        r"private\s+void\s+ParteHatch\([^)]*bool\s+(\w+)\s*\)\s*\{(.*?)\n    \}",
        td, re.S,
    )
    check("ParteHatch localizado", ph is not None)
    if ph:
        nombre, cuerpo = ph.group(1), ph.group(2)
        guarda = re.search(rf"if\s*\(\s*{nombre}\s*\)\s*\{{(.*?)\n        \}}", cuerpo, re.S)
        check(f"fondo solido protegido por 'if ({nombre})'", guarda is not None)
        if guarda:
            check(
                "el SOLID va dentro de la guarda",
                '"SOLID"' in guarda.group(1),
            )
            # y el patron AR-CONC va SIEMPRE, fuera de la guarda
            fuera = cuerpo.replace(guarda.group(0), "")
            check(
                "el patron AR-CONC se dibuja siempre",
                "PatronConcreto" in fuera,
            )

    # g) el relleno del estribo y el contorno negro solo en tipo 1
    check(
        "relleno del estribo solo en tipo 1",
        re.search(rf"if\s*\(\s*{flag}\s*&&", td) is not None,
    )
    check(
        "contorno negro solo en tipo 1",
        re.search(rf"if\s*\(\s*{flag}\s*\)\s*\n\s*\{{\s*\n\s*foreach", td) is not None,
    )


# ======================================================================
# 10. Nombres tapados: alias de using y metodos que tapan un tipo
#
#     Esta familia de errores ya rompio la compilacion tres veces:
#       - 'Path' ambiguo entre System.IO y System.Windows.Shapes  (CS0104)
#       - 'new Path { ... }' con 'using Path = System.IO.Path'    (estatico)
#       - un metodo 'Rect(...)' tapando el struct System.Windows.Rect (CS0118)
# ======================================================================
# Clases estaticas: no se pueden instanciar con 'new'
ESTATICAS = {
    "System.IO.Path", "System.IO.File", "System.IO.Directory",
    "System.Console", "System.Math", "System.Convert",
    "System.Text.Json.JsonSerializer",
}

# Tipos del framework que se instancian con 'new' y cuyo nombre es corto: si un
# metodo del archivo se llama igual, la llamada 'new Nombre(...)' no compila.
TIPOS_INSTANCIABLES = {
    "Rect", "Point", "Size", "Color", "Vector", "Thickness", "Matrix",
    "Span", "Range", "Index", "Uri", "Random", "Exception",
}


def v10_nombres_tapados() -> None:
    print("\n[10] Nombres tapados (alias de using / metodos que tapan tipos)")
    problemas = []

    for p in archivos(".cs"):
        txt = leer(p)
        limpio = re.sub(r"//[^\n]*", "", txt)
        limpio = re.sub(r"/\*.*?\*/", "", limpio, flags=re.S)
        limpio = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', limpio)

        # (a) alias hacia una clase estatica, instanciada con 'new'
        for m in re.finditer(
            r"^\s*using\s+([A-Za-z0-9_]+)\s*=\s*([A-Za-z0-9_.]+)\s*;", txt, re.M
        ):
            alias, destino = m.group(1), m.group(2)
            if destino in ESTATICAS and re.search(rf"\bnew\s+{alias}\b", limpio):
                problemas.append(
                    f"{rel(p)}: 'new {alias}' pero el alias apunta a "
                    f"{destino}, que es estatico"
                )

        # (b) metodo cuyo nombre tapa un tipo instanciado con 'new' en el archivo
        metodos = set(
            re.findall(
                r"^\s*(?:private|public|internal|protected)\s+"
                r"(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+)*"
                r"[A-Za-z0-9_<>\?\[\],\.]+\s+([A-Za-z0-9_]+)\s*\(",
                limpio, re.M,
            )
        )
        for nombre in sorted(metodos & TIPOS_INSTANCIABLES):
            if re.search(rf"\bnew\s+{nombre}\s*\(", limpio):
                problemas.append(
                    f"{rel(p)}: el metodo '{nombre}' tapa al tipo "
                    f"'{nombre}', usado con new (CS0118)"
                )

    check("sin nombres tapados", not problemas, "; ".join(sorted(set(problemas))))


# ======================================================================
# 11. Visor del modelo: proyección coherente y orden de dibujado
#
#     La proyección aparece en DOS sitios (el modelo y la terna de ejes). Si se
#     corrige en uno y no en el otro, la terna marca un giro distinto al que se
#     ve. Y el orden del algoritmo del pintor tiene que ir de lejos a cerca.
# ======================================================================
def v11_visor() -> None:
    print("\n[11] Visor 3D / planta")
    p = ruta("client", "src", "CadLink.App", "VistaModelo.cs")
    if not os.path.exists(p):
        check("VistaModelo.cs presente", False, rel(p))
        return
    check("VistaModelo.cs presente", True)

    t = leer(p)
    tx_planos = leer(ruta("client", "src", "CadLink.App", "MainWindow.xaml"))
    codigo_planos = leer(ruta("client", "src", "CadLink.App", "MainWindow.xaml.cs"))

    # La fórmula correcta suma los dos términos y niega el conjunto
    formulas = re.findall(r"-\(\((\w+) \* [Cc]e\) \+ \((\w+) \* [Ss]e\)\)", t)
    check(
        "las dos proyecciones usan la formula correcta",
        len(formulas) >= 2,
        f"encontradas {len(formulas)}, se esperaban 2 (modelo y terna)",
    )

    # La fórmula con el signo invertido no debe reaparecer
    malas = re.findall(r"\(\w+ \* se\)\s*-\s*\(\w+ \* ce\)", t)
    check(
        "sin la formula del signo invertido",
        not malas,
        "; ".join(malas),
    )

    # EL ORDEN DEL PINTOR se queda solo en la vista de ALAMBRE, donde no hay caras que se
    # atraviesen. La EXTRUIDA ya no ordena: pinta con Z-BUFFER, que es lo que arregla la losa
    # que se veia cortada por el muro -dos caras que se cruzan no tienen orden correcto,
    # porque cada una esta delante en una parte-.
    check(
        "el alambre sigue pintando de lejos a cerca",
        "OrderByDescending" in t and re.search(r"OrderBy\(t => t\.Prof\)", t) is None,
    )

    # ------------------------------------------------------------------
    # LA EXTRUIDA, CON Z-BUFFER: LA LOSA YA NO SE VE CORTADA
    # ------------------------------------------------------------------
    #  No era el motor de dibujo, era el METODO: ordenar caras por su profundidad media no
    #  puede resolver dos caras que se atraviesan. Con Z-buffer la decision se toma por PIXEL.
    # Se releen aqui: las variables de mas abajo son de otra parte de la funcion.
    rast = leer(ruta("client/src/CadLink.Cad/RasterZ.cs"))
    ext_z = leer(ruta("client/src/CadLink.App/VistaModelo.Extruida.cs"))
    pre_z = leer(ruta("tools/prueba-ejes-plano/Program.cs"))

    check("hay un rasterizador con Z-buffer, y sin WPF para poder probarlo",
          "public sealed class RasterZ" in rast
          and "public void Triangulo(" in rast
          and "public void Linea(" in rast
          and "if (z >= _z[i])" in rast
          and "using System.Windows" not in rast)
    check("la extruida lo usa en vez de ordenar caras",
          "var lienzoZ = new RasterZ(" in ext_z
          and "lienzoZ.Triangulo(" in ext_z
          and "MostrarRaster(lienzo, lienzoZ);" in ext_z
          and "OrderByDescending" not in ext_z)
    # LA PROFUNDIDAD, POR VERTICE: con una sola por cara solo se puede ordenar.
    check("la cara guarda la profundidad de cada vertice",
          "public required double[] Prof { get; init; }" in ext_z
          and "Prof = cara.Select(p => cam.Prof(p.X, p.Y)).ToArray()," in ext_z)
    # LAS ARISTAS, con sesgo hacia la camara: sin el, saldrian a puntos contra su propia cara.
    check("las aristas se acercan un pelo para no pelearse con su cara",
          "double sesgo = 0.05" in rast
          and "z1 + ((z2 - z1) * t) - sesgo" in rast)
    # Y SE VUELCA COMO UNA IMAGEN: un objeto en el lienzo en vez de miles de poligonos.
    check("el resultado se vuelca como una imagen, no como miles de poligonos",
          "WriteableBitmap(" in ext_z
          and "PixelFormats.Bgra32" in ext_z
          and "NearestNeighbor" in ext_z)
    check("hay prueba ejecutable del Z-buffer",
          "a la izquierda queda la cara que ahi esta mas cerca" in pre_z
          and "lo de detras no tapa lo de delante aunque se pinte despues" in pre_z
          and "un triangulo sin area no pinta" in pre_z)

    # La vista en planta dedicada invierte la Y, porque la del lienzo crece
    # hacia abajo y la del modelo hacia arriba
    # La Y del modelo sube y la del lienzo baja. La formula ya no arranca de h/2 sino del
    # CENTRO DEL HUECO UTIL, porque los margenes son asimetricos: arriba y a la izquierda hay
    # que dejar sitio para las cotas y las burbujas. Lo que no cambia es el signo.
    check(
        "la planta invierte la Y",
        re.search(r"centroY - \(\(y - cy\) \* escala\)", t) is not None,
    )
    check(
        "y se centra en el hueco util, no en el lienzo",
        "var centroX = (w + MargenAnotado - MargenLibre) / 2;" in t
        and "var centroY = (h + MargenAnotado - MargenLibre) / 2;" in t,
    )

    # ------------------------------------------------------------------
    # LA PREVISUALIZACION, CON LA ORIENTACION Y LA POSICION DE VERDAD
    # ------------------------------------------------------------------
    #  Se pidio expresamente: la vista previa tiene que ensenar lo que se va a dibujar. Antes
    #  la columna era un Rectangle de WPF -y un Rectangle NO GIRA, esta alineado a los ejes
    #  del lienzo-, asi que una columna de 20x60 girada 90 grados se veia de 20x60 derecha, y
    #  la trabe era una linea de 1.4 pixeles pase lo que pase: dos trabes de 15 y de 35 se
    #  veian iguales.
    check("la columna de la vista previa se dibuja con su seccion girada",
          "SeccionEnPlanta.Contorno(" in t
          and "SeccionEnPlanta.Colocar(" in t
          and "el.AnguloGrados" in t
          and "using CadLink.Cad.PlanoEstructural;" in t)
    check("y comparte la geometria con el dibujante de AutoCAD, no una copia",
          "SeccionEnPlanta.EsRedonda(el.Forma)" in t
          and "el.PatinM, el.AlmaM, el.ParedM" in t)
    # EL GROSOR REAL, que es lo que se pidio: la trabe salia como una linea de 1.4 px pase lo
    # que pase -una de 15 y otra de 35 se veian iguales- y el muro como un trazo con un
    # minimo de 2.2 px, que a poco zoom lo engorda y a mucho zoom lo adelgaza. Ahora se
    # dibuja la HUELLA en metros del modelo, asi que el grosor de la pantalla es el de verdad
    # a la escala del momento.
    check("la trabe y el muro se dibujan con su grosor real, no con una linea fija",
          "private void DibujarBarraEnPlanta(" in t
          and "DibujarBarraEnPlanta(lienzo, el, APantallaPlanta, anchoReal)" in t
          and "var anchoReal = AnchoEnPlanta(el);" in t
          and "ClaseElemento.Muro => RellenoMuro," in t)
    # Y CON EL MISMO RESPALDO QUE EL DIBUJANTE cuando ETABS no da la medida: si la vista
    # previa dibujara un pelo donde el plano va a dibujar 15 cm, estaria mintiendo.
    dib_esp = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.cs"))
    check("y con los mismos valores de omision que el dibujante",
          "private static double AnchoEnPlanta(" in t
          and "private const double EspesorMuroPorOmision = 0.15;" in t
          and "private const double AnchoTrabePorOmision = 0.20;" in t
          and "private const double EspesorMuroPorOmision = 0.15;" in dib_esp
          and "private const double AnchoTrabePorOmision = 0.20;" in dib_esp)
    # Y el pelo solo cuando la huella no llegaria a un pixel: ahi el grosor no se puede
    # representar y lo que importa es que el elemento no desaparezca.
    check("con el zoom muy lejos queda una linea de un pelo, no un grosor inventado",
          "if (anchoReal * escala >= 1.2)" in t
          and "Math.Max(2.2, el.AnchoM * escala)" not in t)
    # Y EL ORDEN DE PINTADO: ahora que las piezas van rellenas, una trabe ancha podia tapar
    # la seccion de la columna, que es justo lo que se viene a comprobar aqui.
    check("el orden de pintado deja la columna al frente",
          "static int Capa(ElementoEtabs el) => el.Clase switch" in t
          and "ClaseElemento.Columna => 3," in t)
    # LA MARCA MINIMA solo cuando la seccion no se puede dibujar: sin medidas o con el zoom
    # tan lejos que mide menos de tres pixeles. Lo que no puede hacer es fingir un tamano.
    check("sin medidas queda una marca, y se dice que es una marca",
          "private static void MarcaDeColumna(" in t)
    # LA EXTRUIDA TAMBIEN: su triedro salia solo de la geometria del eje, asi que todas las
    # columnas quedaban alineadas con la X y la Y globales.
    ext = leer(ruta("client", "src", "CadLink.App", "VistaModelo.Extruida.cs"))
    check("la vista extruida gira el prisma con los ejes locales",
          "Math.Abs(el.AnguloGrados) > 1e-9" in ext_z
          and "(n1.Item1 * ca) + (n2.Item1 * sa)" in ext_z
          and "(n2.Item1 * ca) - (n1.Item1 * sa)" in ext_z)
    # ------------------------------------------------------------------
    # LA CUADRICULA DE EJES EN LA VISTA PREVIA
    # ------------------------------------------------------------------
    #  Se pidio. Sale del modelo si el programa la dio y, si no, se deduce de las columnas
    #  -el mismo respaldo del plano-, y pasa por el MISMO filtro de repetidos, porque la
    #  cuadricula de ETABS suele traer el mismo eje declarado dos veces.
    check("la vista previa dibuja la cuadricula de ejes",
          "private List<(string Id, double Ordenada)> EjesDeLaPlanta(" in t
          and "private static void DibujarEjesEnPlanta(" in t
          and "Modelo.Ejes ?? EjesModelo.DesdeGeometria(Modelo)" in t
          and "EjesPlano.SinRepetidos(lista, 0.01)" in t)
    # LOS EJES SE MIDEN CON LOS ELEMENTOS: un eje puede caer por fuera de lo construido -el
    # de una fachada sin muro- y sin contarlo se dibujaria fuera del lienzo.
    check("los ejes entran en el encuadre, para que no queden fuera del lienzo",
          "var ejesX = EjesDeLaPlanta(true);" in t
          and "xMin = Math.Min(xMin, o);" in t
          and "yMax = Math.Max(yMax, o);" in t)
    # AL FONDO, como en el plano: el eje es la referencia, no el dibujo.
    check("y van al fondo, antes que los elementos",
          "DibujarEjesEnPlanta(lienzo, ejesX, ejesY, APantallaPlanta" in t)
    # LA BURBUJA EN PIXELES, no en metros: es un rotulo y tiene que leerse igual de cerca
    # que de lejos, igual que en el plano su radio va en papel.
    check("la burbuja del eje va en pixeles y rellena, para que se lea",
          "const double Radio = 9;" in t
          and "Fill = RellenoBurbuja" in t
          and "StrokeDashArray = trazos" in t)
    # LAS COTAS: un eje sin cota no dice nada, lo que se replantea son las DISTANCIAS. Van
    # del lado contrario a las burbujas -abajo y a la derecha- para que no se estorben.
    check("los ejes se acotan, con la parcial y la total",
          "private static void AcotarEjes(" in t
          and "AcotarEjes(lienzo, ejesX, ejesY, aPantalla, arriba, abajo);" in t
          and 'v.ToString("0.000"' in t
          and "if (orden.Count > 2)" in t)
    # EL NUMERO SOLO SI CABE: con la vista alejada dos ejes pueden quedar a diez pixeles y
    # los rotulos se encimarian hasta ser ilegibles. La linea de cota siempre se dibuja.
    check("el numero de la cota se escribe solo si cabe",
          "void NumeroArriba(" in t
          and "void NumeroAlLado(" in t
          and "if (hueco < texto.Length * 5.6)" in t
          and "if (hueco < 13)" in t)
    # LA COTA VERTICAL VA GIRADA: en un plano se lee de abajo arriba, y ademas a la izquierda
    # solo hay 18 px entre la cota parcial y la total, donde el numero en horizontal no cabe.
    check("la cota vertical lleva su numero girado",
          "RenderTransform = new RotateTransform(-90)" in t)
    # Y HAY SITIO PARA TODO ESO: sin ampliar el margen, burbujas y cotas quedaban cortadas
    # contra el borde del lienzo.
    check("el margen del lienzo deja sitio a burbujas y cotas",
          "private const double MargenAnotado = 78;" in t
          and "private const double MargenLibre = 18;" in t)
    # Y LAS COTAS, ARRIBA Y A LA IZQUIERDA NADA MAS, que es como se pidio. Comparten lado con
    # las burbujas, asi que la burbuja se va la mas afuera: parcial 22, total 40, burbuja 58.
    check("las cotas van arriba y a la izquierda, con la burbuja por fuera",
          "const double Parcial = 22;" in t
          and "const double Total = 40;" in t
          and "const double SaleBurbuja = 58;" in t
          and "var y = arriba.Y - Parcial;" in t
          and "var x = arriba.X - Parcial;" in t
          and "Burbuja(x, arriba.Y - SaleBurbuja, id);" in t
          and "Burbuja(arriba.X - SaleBurbuja, y, id);" in t)

    # ------------------------------------------------------------------
    # MOVER LA PLANTA CON EL RATON
    # ------------------------------------------------------------------
    #  Aqui estaba el «solo me deja hacer zoom»: mover era SOLO con el boton derecho, y en la
    #  planta el izquierdo no hacia nada -no hay nada que girar-, asi que quien arrastraba con
    #  el izquierdo, que es lo natural, no veia respuesta.
    check("en la planta el boton izquierdo tambien mueve",
          "var esPlanta = ReferenceEquals(lienzo, PlantaCanvas);" in codigo_planos
          and "_girando = e.ChangedButton == MouseButton.Left && !esPlanta;" in codigo_planos
          and "|| (esPlanta && e.ChangedButton == MouseButton.Left);" in codigo_planos)
    check("y el lienzo de la planta escucha el boton izquierdo",
          'MouseLeftButtonDown="OnVistaMouseDown"' in tx_planos
          and 'MouseLeftButtonUp="OnVistaMouseUp"' in tx_planos)

    # Y SE PUEDE APAGAR: en un modelo con muchos ejes la cuadricula tapa lo que se mira.
    # EL CORTE SE ELIGE EN LAS DOS PESTAÑAS: en el visor se busca y en la de planos se
    # dibuja, asi que tener que ir a la otra para escoger el eje y volver es un viaje que no
    # hace falta. Las dos listas llevan lo mismo y en el mismo orden, asi que igualarlas es
    # copiar el indice -por nombre o por ordenada aparecerian diferencias por redondeo-.
    check("el corte se elige tambien en la pestaña de planos",
          'x:Name="CortePlanoCombo"' in tx_planos
          and tx_planos.count('SelectionChanged="OnCorteEjeCambiado"') == 2
          and "var listas = new[] { CorteEjeCombo, CortePlanoCombo };" in codigo_planos
          and "private void IgualarLaOtraListaDeCortes(" in codigo_planos)
    # Y CON GUARDA: sin ella, sincronizar una lista dispara el evento de la otra, que vuelve a
    # sincronizar la primera. El finally tambien importa: si la bandera se queda puesta, el
    # desplegable deja de responder y no hay forma de saber por que.
    check("y sincronizarlas no se muerde la cola",
          "if (!_listo || _sincronizandoCortes)" in codigo_planos
          and "_sincronizandoCortes = true;" in codigo_planos
          and "finally" in codigo_planos)

    check("los ejes se pueden apagar con su casilla",
          "public bool VerEjes { get; set; } = true;" in t
          and "if (!VerEjes || Modelo is null)" in t
          and 'x:Name="VerEjesPlanoChk"' in tx_planos
          and "_vista.VerEjes = VerEjesPlanoChk.IsChecked == true;" in codigo_planos)

    # LA POSICION viene sola: el visor lee el MISMO ModeloEtabs al que el lector ya le
    # aplico el punto de insercion, asi que la vista previa y el plano coinciden.
    check("y la posicion sale del modelo ya corregido, sin recalcularla aparte",
          "GetInsertionPoint" not in t
          and "e.X1 +=" not in t)

    # LA PESTAÑA DEL MODELO YA NO EXISTE: primero se movio al lado de la de planos y despues
    # se pidio meterla DENTRO, que es donde esta. Lo que se comprueba ahora es eso: que el
    # visor viva dentro de la pestaña de planos, con la planta a la izquierda y el 3D a la
    # derecha, y que no haya quedado una pestaña suelta.
    orden = re.findall(r'<TabItem[^>]*Header="([^"]+)"', tx_planos)
    check("el visor del modelo vive dentro de la pestaña de planos",
          "Dibujar planos estructurales" in orden
          and "ETABS/SAP2000" not in orden
          and "Vista en planta y cortes" in tx_planos
          and 'Grid.Row="2" Grid.Column="1"' in tx_planos)

    # Los lienzos necesitan Background para recibir el mouse
    x = ruta("client", "src", "CadLink.App", "MainWindow.xaml")
    tx = leer(x)
    for nombre in ("Vista3DCanvas", "PlantaCanvas"):
        m = re.search(rf'<Canvas x:Name="{nombre}"(.*?)/>', tx, re.S)
        check(
            f"{nombre} tiene Background (si no, ignora el mouse)",
            m is not None and "Background=" in m.group(1),
        )


# ======================================================================
# 12. Fidelidad a la macro y peticiones explicitas del usuario
# ======================================================================
def v12_fidelidad() -> None:
    print("\n[12] Fidelidad a la macro y peticiones del usuario")

    drawer = leer(ruta("client", "src", "CadLink.Cad", "SeccionDrawer.cs"))
    xaml = leer(ruta("client", "src", "CadLink.App", "MainWindow.xaml"))
    codigo = leer(ruta("client", "src", "CadLink.App", "MainWindow.xaml.cs"))
    cfg = leer(ruta("client", "src", "CadLink.App", "cadlink.config.json"))
    etabs = leer(ruta("client", "src", "CadLink.Etabs", "EtabsConnection.cs"))

    # --- El bloque NO debe absorber cotas ni rotulos (como la macro) ---
    check(
        "el bloque excluye COTAS y ROTULOS",
        '"COTAS", StringComparison.OrdinalIgnoreCase' in drawer
        and '"ROTULOS", StringComparison.OrdinalIgnoreCase' in drawer,
    )

    # --- Contorno del estribo en NEGRO por color verdadero, no ACI 7 ---
    check("el negro usa color verdadero (TrueColor)", "e.TrueColor = negro" in drawer)
    check("AcCmColor se busca por versiones", 'GetInterfaceObject("AutoCAD.AcCmColor.' in drawer)

    # --- Llamadas (leaders) de los lechos ---
    for pieza, nota in [
        ("LeaderLecho(", "llamada de lecho"),
        ("LeaderVarilla(", "llamada de varilla lateral"),
        ("FlechaTriangular(", "flecha triangular"),
        ("LeadersDeLecho(", "agrupado de llamadas"),
        ("vars. ", "texto 'N vars. #X C'"),
    ]:
        check(f"existe {nota}", pieza in drawer)

    check(
        "las llamadas se dibujan al dibujar la seccion",
        re.search(r"LeadersDeLecho\(s\.Superior", drawer) is not None
        and re.search(r"LeadersDeLecho\(s\.Inferior", drawer) is not None,
    )
    check(
        "las laterales llevan llamada",
        re.search(r"LeaderVarilla\(xIzq", drawer) is not None
        and re.search(r"LeaderVarilla\(xDer", drawer) is not None,
    )
    check("el rotulado va por capa (256)", "PorCapa = 256" in drawer)
    check("capa TEXTOS creada", 'Capa("TEXTOS"' in drawer)
    check("separaciones tipo 5-10-15", "Separaciones(" in drawer)

    # --- Peticiones explicitas sobre la interfaz ---
    check("sin el texto '(AC = n)'", "(AC =" not in xaml)
    # INVERTIDO a pedido del usuario: tipo 1 = no rellena, tipo 2 = rellena.
    # Queda al reves que la celda AC de la macro, y por eso existe DesdeCeldaAC.
    check(
        "etiquetas invertidas: 1 no rellena, 2 rellena",
        "Seccion tipo 1 - no rellena" in xaml and "Seccion tipo 2 - rellena" in xaml,
    )
    check(
        "el tipo 1 queda marcado por omision",
        re.search(r'x:Name="Tipo1Radio"[^>]*IsChecked="True"', xaml, re.S) is not None,
    )
    check(
        "existe la traduccion de la celda AC",
        "DesdeCeldaAC" in leer(ruta("client", "src", "CadLink.Cad", "SeccionCad.cs")),
    )
    # Mapeo CONFIRMADO con el autor de la macro: AC=1 rellena, AC=2 sin relleno.
    # Se comprueban las DOS direcciones: con una sola, invertir la otra pasaria.
    seccion_cad = leer(ruta("client", "src", "CadLink.Cad", "SeccionCad.cs"))
    check(
        "AC=1 se traduce a rellena, como en la macro",
        re.search(r"1 => ModoSeccion\.Tipo2Rellena", seccion_cad) is not None,
    )
    check(
        "AC=2 se traduce a sin relleno",
        re.search(r"2 => ModoSeccion\.Tipo1SinRelleno", seccion_cad) is not None,
    )
    check(
        "si la celda AC viene vacia se asume rellena, como la macro",
        re.search(r"_ => ModoSeccion\.Tipo2Rellena", seccion_cad) is not None,
    )
    check("sin la descripcion larga del estilo", "La de uso normal" not in xaml)

    filas_cs = leer(ruta("client", "src", "CadLink.App", "Models", "StructuralRows.cs"))

    # La lista de elementos. COLUMNA y COLUMNA CIRCULAR llegan por CONSTANTE y no
    # como literal, porque el nombre lo comparten el desplegable, la clasificacion
    # del tipo y el rotulo: escribirlo tres veces es como se desincroniza.
    m = re.search(r"ColElemento\.ItemsSource = new\[\](.*?)\n\s*\};", codigo, re.S)
    lista = m.group(1) if m else ""
    check("lista de elementos localizada", m is not None)

    for fuera in ["MURO", "LOSA", "DALA", "VIGA"]:
        check(f"sin {fuera} en la lista", f'"{fuera}"' not in lista)

    # Los que van como literal en la lista
    # CABEZAL y OTRO se anadieron a peticion del usuario. CABEZAL lleva alzado
    # horizontal, porque es una pieza tendida; OTRO es el recordatorio de que la casilla
    # admite un nombre escrito a mano.
    check("la lista incluye CABEZAL", "ElementoCabezal" in codigo)
    check("y OTRO", "ElementoOtro" in codigo)
    # El CABEZAL NO lleva alzado. Estuvo un rato devolviendo Trabe, y el usuario lo
    # quito: un cabezal se documenta con su seccion y su armado, no con un alzado de
    # estribos por zonas L/4-L/2-L/4, que es lo que dibuja el alzado de trabe.
    check("el CABEZAL no lleva alzado",
          "if (e == SeccionConcretoRow.ElementoCabezal)" not in codigo)
    check("y queda dicho por que, para que nadie lo vuelva a anadir",
          "CABEZAL y cualquier otro elemento: sin alzado" in codigo)

    for dentro in ["CASTILLO", "TRABE", "CONTRATRABE",
                   "CADENA DE CERRAMIENTO", "CADENA DE DESPLANTE"]:
        check(f"con {dentro} en la lista", f'"{dentro}"' in lista)

    # Y los CUATRO que van por constante: las dos columnas y los dos dados. Cada pareja es
    # la misma pieza con dos formas, y la constante es la que las mantiene juntas.
    check("con COLUMNA en la lista",
          "SeccionConcretoRow.ElementoColumna," in lista)
    check("con COLUMNA CIRCULAR en la lista",
          "SeccionConcretoRow.ElementoColumnaCircular" in lista)
    check("con DADO en la lista",
          "SeccionConcretoRow.ElementoDado," in lista)
    check("con DADO CIRCULAR en la lista",
          "SeccionConcretoRow.ElementoDadoCircular" in lista)

    # Las constantes tienen que valer lo que se espera: si alguien cambiara
    # ElementoColumnaCircular por otra cosa, TipoDe dejaria de reconocerla y la
    # columna redonda se quedaria sin alzado.
    check('ElementoColumna vale "COLUMNA"',
          re.search(r'ElementoColumna\s*=\s*"COLUMNA"\s*;', filas_cs) is not None)
    check('ElementoColumnaCircular vale "COLUMNA CIRCULAR"',
          re.search(r'ElementoColumnaCircular\s*=\s*"COLUMNA CIRCULAR"\s*;',
                    filas_cs) is not None)

    # La fila de ejemplo no puede usar un nombre que no lleva alzado, o el usuario
    # abre el programa, pulsa Generar alzados y no sale nada.
    check(
        "los ejemplos usan un elemento que si lleva alzado",
        'Elemento = "VIGA"'
        not in leer(ruta("client", "src", "CadLink.App", "Models", "StructuralRows.cs")),
    )

    # --- Nombre de empresa vacio, sin guion suelto ---
    check(
        "nombreEmpresa vacio en la configuracion",
        re.search(r'"nombreEmpresa"\s*:\s*""', cfg) is not None,
    )
    lic = leer(ruta("client", "src", "CadLink.Licensing", "LicenseInfo.cs"))
    check(
        "sin guion suelto cuando no hay empresa",
        "LicenseTier.Internal when string.IsNullOrWhiteSpace(Organization)" in lic,
    )
    check(
        "el cliente manda el nombre mostrado",
        "ConNombreDeEmpresa" in leer(ruta("client", "src", "CadLink.App", "AppInfo.cs")),
    )

    # --- ETABS: la libreria va PRIMERO ---
    orden = re.search(r"var vias = new .*?\{(.*?)\};", etabs, re.S)
    check("orden de vias de ETABS localizado", orden is not None)
    if orden:
        cuerpo = orden.group(1)
        pos_lib = cuerpo.find("PorEnsamblado")
        pos_rot = cuerpo.find("PorObjetoActivo")
        check(
            "ETABSv1.dll se intenta antes que el enlace tardio",
            0 <= pos_lib < pos_rot,
            f"lib={pos_lib} rot={pos_rot}",
        )
    check(
        "la DLL se busca junto al ETABS en ejecucion",
        "CarpetasDeProcesosEtabs" in leer(
            ruta("client", "src", "CadLink.Etabs", "EtabsAssembly.cs")),
    )

    # La clave de configuracion tiene que estar CONECTADA, no solo documentada:
    # el mensaje de error le dice al usuario que la use.
    check(
        "rutaLibreriaEtabs existe en la configuracion",
        '"rutaLibreriaEtabs"' in cfg,
    )
    check(
        "rutaLibreriaEtabs se lee al arrancar",
        "EtabsAssembly.RutaConfigurada = AppInfo.RutaLibreriaEtabs"
        in leer(ruta("client", "src", "CadLink.App", "App.xaml.cs")),
    )

    # --- Apariencia: estilo de texto, colores por capa, cotas y orden ---
    check("estilo de texto SECCIONES creado", "AsegurarEstiloTexto" in drawer)
    check(
        "la fuente es la de la macro",
        'FuenteTexto = "BAHNSCHRIFT SEMILIGHT"' in drawer,
    )
    check(
        "los dos MText llevan el estilo",
        len(re.findall(r"mt\.StyleName = EstiloTexto", drawer)) == 2,
        f"{len(re.findall(r'mt.StyleName = EstiloTexto', drawer))} de 2",
    )
    check("DIMTXSTY apunta al estilo", 'Dimvar("DIMTXSTY", EstiloTexto)' in drawer)

    # Sin color explicito las entidades heredan CECOLOR y se ignoran los colores
    # de capa. Son 11 sitios: primitivas, varillas, rellenos y rotulado.
    n_color = len(re.findall(r"\.Color = PorCapa", drawer))
    check("color por capa explicito en las primitivas", n_color >= 11, f"solo {n_color}")

    for var, nota in [
        ("DIMBLK", "flechas _OPEN90"),
        ("DIMCLRD", "color de la linea de cota"),
        ("DIMCLRT", "color del texto de cota"),
        ("DIMFXLON", "extension de longitud fija"),
        ("DIMTAD", "posicion del texto"),
    ]:
        check(f"cotas: {nota}", f'Dimvar("{var}"' in drawer)

    # ------------------------------------------------------------------
    # Cotas: tolerancia POR INSTRUCCION
    # ------------------------------------------------------------------
    # El bug de las lineas de extension enormes: las ~25 asignaciones de
    # variables de cota estaban dentro de UN SOLO try. En VBA, 'On Error Resume
    # Next' tolera por instruccion, asi que un rechazo (DIMBLK) se salta y las
    # demas siguen; en C# abortaba el bloque y DIMEXO/DIMEXE/DIMFXL/DIMFXLON se
    # quedaban con los valores de la plantilla del usuario.
    m_var = re.search(
        r"private void AplicarVariablesDeCota\(\).*?\n    \}", drawer, re.S
    )
    check("existe AplicarVariablesDeCota", m_var is not None)

    if m_var:
        cuerpo = m_var.group(0)
        n_dimvar = len(re.findall(r"\bDimvar\(", cuerpo))

        # Se comprueba la LLAMADA, no la declaracion: que de verdad se usen
        # todas por separado y no haya quedado ningun SetVariable suelto.
        check(
            "todas las variables de cota van por Dimvar",
            n_dimvar >= 24,
            f"solo {n_dimvar} llamadas",
        )
        # DIMTOFF se quito porque NO EXISTE en AutoCAD. Las parecidas son
        # DIMTOFL y DIMTMOVE, que hacen otra cosa; la separacion del texto ya la
        # da DIMGAP. El AutoCAD del usuario la rechazaba con 0x80210066.
        # Se busca la LLAMADA, no la palabra: el comentario que explica por que se
        # quito tambien la menciona, y buscar la palabra suelta fallaba por eso.
        check("no se fija la inexistente DIMTOFF",
              'Dimvar("DIMTOFF"' not in drawer)
        check(
            "ningun SetVariable crudo entre las variables de cota",
            "SetVariable" not in cuerpo,
        )
        # Sin try dentro del cuerpo: la tolerancia la da Dimvar, y un try que
        # envuelva varias llamadas reintroduce exactamente el bug.
        check(
            "AplicarVariablesDeCota no envuelve varias asignaciones en un try",
            not re.search(r"\btry\b", cuerpo),
        )

    m_dimvar = re.search(r"private void Dimvar\(.*?\n    \}", drawer, re.S)
    check("Dimvar existe", m_dimvar is not None)

    if m_dimvar:
        cuerpo = m_dimvar.group(0)
        check(
            "Dimvar tolera el fallo de UNA variable",
            len(re.findall(r"\btry\b", cuerpo)) == 1 and "catch" in cuerpo,
        )
        # Lo que costo cuatro rondas de depuracion: el catch que se tragaba la
        # excepcion. Debe decir QUE variable fallo.
        check(
            "Dimvar reporta cual variable fallo",
            "Fallo(" in cuerpo and "{nombre}" in cuerpo,
        )

    # Las variables se reaplican por seccion, como la macro, que llama a
    # ConfigurarVariablesDeCota dentro de ColocarCotasParaConcreto.
    m_cotas = re.search(
        r"private void Cotas\(double x0.*?\n    \}", drawer, re.S
    )
    check("existe el metodo Cotas", m_cotas is not None)

    if m_cotas:
        check(
            "las variables de cota se reaplican en cada seccion",
            "ConfigurarCotas()" in m_cotas.group(0),
        )

    # La ultima linea de defensa: aunque el estilo del dibujo traiga longitudes
    # de otro plano, estas propiedades del OBJETO mandan sobre el estilo.
    m_fmt = re.search(r"private void FormatearCota\(.*?\n    \}", drawer, re.S)
    check("existe FormatearCota", m_fmt is not None)

    if m_fmt:
        cuerpo = m_fmt.group(0)

        # El orden manda: asignar StyleName vuelca los valores del estilo sobre
        # la cota, asi que TIENE que ir antes que los ajustes.
        pos_estilo = cuerpo.find('"StyleName"')
        pos_fija = cuerpo.find('"ExtLineFixedLen"')
        check(
            "el estilo se asigna ANTES de los ajustes de la cota",
            pos_estilo != -1 and pos_fija != -1 and pos_estilo < pos_fija,
        )

        for prop in [
            "ExtensionLineOffset",
            "ExtensionLineExtend",
            "ExtLineFixedLen",
            "ExtLineFixedLenSuppress",
            "PrimaryUnitsPrecision",
        ]:
            check(
                f"la cota fija {prop} en el objeto",
                f'PropCota(cota, "{prop}"' in cuerpo,
            )

        n_prop = len(re.findall(r"PropCota\(cota,", cuerpo))
        check(
            "cada propiedad de la cota va por separado",
            n_prop >= 12,
            f"solo {n_prop}",
        )

    m_prop = re.search(r"private void PropCota\(.*?\n    \}", drawer, re.S)
    check("PropCota existe", m_prop is not None)

    if m_prop:
        cuerpo = m_prop.group(0)
        check(
            "PropCota tolera una propiedad ausente",
            len(re.findall(r"\btry\b", cuerpo)) == 1 and "catch" in cuerpo,
        )
        check(
            "PropCota dice cual propiedad fallo",
            "{propiedad}" in cuerpo,
        )

    check("estilo de cota COTA_ESTRUCTURAL", 'EstiloCota = "COTA_ESTRUCTURAL"' in drawer)
    check("el estilo de cota se crea con CopyFrom", "estilo.CopyFrom(_doc)" in drawer)
    check("cada cota se formatea", len(re.findall(r"FormatearCota\(d", drawer)) == 2)
    check("contorno de estribos al frente", "EstribosAlFrente(" in drawer)

    # El rotulado (leaders, flechas y textos) va ENCIMA DE TODO. Sin esto las
    # varillas y el estribo muerden las flechitas.
    check("el rotulado sube al frente", "public void RotulosAlFrente" in drawer)
    check("se llama al terminar de dibujar", "dibujante.RotulosAlFrente()" in codigo)
    # Se acota al metodo OnExport: ahora hay varios ZoomExtents en el archivo y
    # buscar el primero comparaba posiciones de metodos distintos.
    m_exp = re.search(r"private void OnExport\(.*?\n    \}", codigo, re.S)
    check("metodo OnExport localizado", m_exp is not None)
    if m_exp:
        cuerpo_exp = m_exp.group(0)
        check(
            "el rotulado sube antes del zoom extents",
            0 <= cuerpo_exp.find("dibujante.RotulosAlFrente()")
            < cuerpo_exp.find("app.ZoomExtents"),
        )
    check(
        "el rotulado sube despues de los estribos",
        # EstribosAlFrente es por seccion, dentro de Dibujar; RotulosAlFrente es
        # global y posterior al bucle, asi que queda por encima.
        "EstribosAlFrente(inicio" in drawer,
    )

    # Escala del hatch: el 0.0003 de la macro, que es el valor con el que el
    # usuario trabaja. Estuvo en 0.01 mientras el rayado salia microscopico por
    # otros motivos; al arreglarse esos, volvio el valor de la macro.
    check("escala del hatch por omision 0.0003",
          "EscalaPatronBase = 0.0003" in drawer)
    # Los TRES tienen que decir lo mismo: la constante, la casilla y el respaldo
    # de cuando lo escrito no se puede leer. Si no coinciden, lo que el usuario ve
    # no es lo que se dibuja, y eso es de lo mas dificil de diagnosticar.
    check(
        "la casilla arranca en 0.0003",
        re.search(r'x:Name="HatchScaleBox" Text="0\.0003"', xaml) is not None,
    )
    # Se acota a LeerEscalaHatch: el otro 'return 0.01' del archivo es la escala
    # del DIBUJO, que si debe seguir siendo 0.01. Sin acotar, el check confundia
    # las dos escalas.
    m_hs = re.search(r"private double LeerEscalaHatch\(\).*?\n    \}", codigo, re.S)
    check("se puede leer LeerEscalaHatch", m_hs is not None)
    if m_hs:
        check("el respaldo al leer la casilla es 0.0003",
              "return 0.0003;" in m_hs.group(0))
        check("y ya no queda el 0.01 de antes", "0.01" not in m_hs.group(0))
    check("la constante ya no es 0.01", "EscalaPatronBase = 0.01" not in drawer)

    # El cuadrilatero de la cola del gancho va INFLADO. Con los puntos crudos
    # quedan costuras blancas y una cuña sin rellenar.
    check("el quad del gancho se infla", "SolapeGancho" in drawer)
    check(
        "la cola recortada se alarga el espesor del estribo",
        re.search(r"recortar \? \(espesor > 0 \? espesor : solape\) : solape", drawer)
        is not None,
    )

    # El boton de dibujar tiene que estar en la pestaña de secciones, no en otra
    conc = xaml[xaml.find('Header="Secciones Concreto"'):xaml.find('Header="Secciones Acero"')]
    check(
        "el boton 'Generar dibujo' esta en Secciones Concreto",
        'x:Name="ExportButton"' in conc and "Generar dibujo" in conc,
    )

    # --- GetBoundingBox NO se puede llamar con dynamic (params por referencia) ---
    check(
        "GetBoundingBox se llama por InvokeMember, no con dynamic",
        "ent.GetBoundingBox(out" not in drawer
        and 'InvokeMember(\n                "GetBoundingBox"' in drawer,
    )
    check(
        "usa ParameterModifier para los parametros por referencia",
        "new ParameterModifier(2)" in drawer,
    )

    # --- 0x8021007B "Invalid object array": AutoCAD quiere VT_DISPATCH ---
    # La logica de arreglos vive en AcadArreglos.cs, compartida por los dibujantes.
    arreglos = leer(ruta("client", "src", "CadLink.Cad", "AcadArreglos.cs"))

    # Se busca el USO real, no la mención en un comentario: con 'DispatchWrapper'
    # a secas la comprobación pasaba aunque el codigo ya no lo usara.
    check(
        "usa DispatchWrapper para los arreglos de entidades",
        "new DispatchWrapper(" in arreglos,
    )
    check(
        "ninguna llamada pasa el arreglo crudo",
        "AppendOuterLoop(new[]" not in drawer
        and "AppendInnerLoop(new[]" not in drawer
        and "CopyObjects(objetos.ToArray()" not in drawer
        and "MoveToBottom(objetos.ToArray()" not in drawer
        and "MoveToTop(objetos.ToArray()" not in drawer,
    )
    # Se cuentan LOS DOS envoltorios. Son el mismo mecanismo con distinto reporte:
    # ConArregloParaOrdenar es el de las llamadas de ORDEN DE DIBUJO, que al fallar
    # dejan nota en lugar de fallo porque son esteticas. Contando solo el primero, el
    # dia que una llamada paso al otro envoltorio esta comprobacion se cayo sola.
    n_arr = len(re.findall(r"ConArreglo(?:DeEntidades|ParaOrdenar)\(", drawer))
    check(
        "las 6 llamadas con arreglo pasan por alguno de los dos envoltorios",
        n_arr >= 6,
        f"solo {n_arr}",
    )

    # El arreglo TIPADO es la unica forma de generar un SAFEARRAY de VT_DISPATCH,
    # que es lo que AutoCAD acepta. DispatchWrapper NO basta: se probo y falla.
    interop = leer(ruta("client", "src", "CadLink.Cad", "AcadInterop.cs"))
    check("existe el cargador de la interop de AutoCAD", "class AcadInterop" in interop)
    check("busca AcadEntity", '"Autodesk.AutoCAD.Interop.Common.AcadEntity"' in interop)
    check(
        "la interop se busca junto al AutoCAD en ejecucion",
        "CarpetasDeAutoCad" in interop,
    )
    check("construye un arreglo tipado", "Array.CreateInstance(tipo" in interop)
    check("se usa el arreglo tipado", "AcadInterop.ArregloTipado(" in arreglos)
    check(
        "el arreglo tipado se intenta PRIMERO",
        0 <= arreglos.find("AcadInterop.ArregloTipado")
        < arreglos.find("new DispatchWrapper("),
    )
    check(
        "los dibujantes delegan en el helper compartido",
        "AcadArreglos.Llamar(" in drawer,
    )

    # --- Escala del hatch ajustable, igual en los dos tipos ---
    check("la escala del hatch es ajustable", "public double EscalaHatch" in drawer)
    check(
        "la escala del hatch sigue a la del dibujo",
        "EscalaHatch * _f" in drawer,
    )
    check(
        "el patron usa la escala efectiva y no la constante",
        "EscalaHatchEfectiva" in drawer and "EscalaPatron," not in drawer,
    )
    check("hay casilla para la escala del hatch", 'x:Name="HatchScaleBox"' in xaml)
    check("la casilla se lee al dibujar", "EscalaHatch = LeerEscalaHatch()" in codigo)
    check(
        "acepta coma decimal",
        "Replace(',', '.')" in codigo,
    )

    # --- Cotas con 2 decimales, fijado tambien en el objeto ---
    check(
        "las cotas fijan 2 decimales en el objeto",
        'PropCota(cota, "PrimaryUnitsPrecision", 2)' in drawer,
    )
    check("DIMDEC en 2", 'Dimvar("DIMDEC", 2)' in drawer)

    # --- Notas y fallos SEPARADOS ---
    check("los fallos van aparte de las notas", "public IReadOnlyList<string> Fallos" in drawer)
    check("hay notas informativas", "public IReadOnlyList<string> Notas" in drawer)
    check(
        "el aviso solo salta con fallos de verdad",
        "dibujante.Fallos" in codigo,
    )
    # Un hatch que falla hay que BORRARLO: si se queda, es una entidad degenerada
    # sin extension y despues rompe GetBoundingBox y el bloque entero.
    check(
        "el hatch fallido se borra",
        re.search(r"if \(!frontera\)\s*\{.*?Borrar\(\(object\)h\)", drawer, re.S) is not None,
    )
    check(
        "los originales solo se borran si CopyObjects funciono",
        re.search(r"if \(!copiado\)\s*\{\s*return;", drawer) is not None,
    )

    # --- Los fallos tolerados NO se descartan: se reportan ---
    n_fallo = len(re.findall(r"\bFallo\(", drawer))
    check("los fallos del dibujo se registran", n_fallo >= 9, f"solo {n_fallo}")
    check("el diagnostico se expone", "public IReadOnlyList<string> Diagnostico" in drawer)
    check(
        "el diagnostico se le muestra al usuario",
        "dibujante.Fallos" in codigo and "dibujante.Notas" in codigo,
    )
    for op in ["Hatch '{patron}'", "Hatch de concreto", "PolyRectFillet",
               "GetBoundingBox", "en un bloque"]:
        check(f"se registra el fallo de: {op}", op in drawer)

    # ---------- Estribo diamante ----------
    dia = leer(ruta("client", "src", "CadLink.Cad", "SeccionDrawer.Diamante.cs"))
    check("existe el estribo diamante", "private void EstriboDiamante(" in dia)
    check("cinta tangente a N circulos", "private object? CintaTangente(" in dia)
    check("eleccion de varillas del centro", "VarillasDelCentro(" in dia)
    check(
        "se dibuja cuando la columna R dice si",
        re.search(r"if \(s\.Diamante\)\s*\{\s*EstriboDiamante\(", drawer, re.S) is not None,
    )
    check(
        "el diamante va DESPUES de las varillas",
        drawer.find("RellenarVarillas(circulos") < drawer.find("EstriboDiamante(s,"),
    )
    check(
        "los dos contornos del diamante son islas del hatch",
        "_diamExt is not null" in drawer and "_diamInt is not null" in drawer,
    )
    # La cinta y la eleccion de varillas ya no viven en el dibujante: se sacaron a
    # TrazoDiamante para que la VISTA PREVIA use exactamente la misma geometria. El
    # dibujante las llama, asi que estas dos protecciones se comprueban donde estan.
    trazo_dia_ = leer(ruta("client/src/CadLink.Cad/TrazoDiamante.cs"))

    check(
        "el radio se protege contra tangente inexistente",
        "Math.Clamp(cc, -0.999999, 0.999999)" in trazo_dia_,
    )
    check(
        "dos circulos coincidentes no producen NaN",
        re.search(r"if \(d < 1e-7\)\s*\{\s*return null;", trazo_dia_) is not None,
    )
    check("relleno solido del diamante en tipo rellena", 'ColorRellenoEstribo)' in dia)
    check(
        "el diamante hereda la varilla del estribo si no trae propia",
        "EstriboDiamanteVar" in leer(ruta("client", "src", "CadLink.Cad", "SeccionCad.cs")),
    )
    check("la columna R acepta variantes de 'si'", "EsSi(" in codigo)

    # ---------- Varillas: contorno negro y al frente en la rellena ----------
    check(
        "las varillas se pintan de negro en la rellena",
        re.search(r"foreach \(var circulo in circulos\)\s*\{\s*Negro\(circulo\);", drawer, re.S)
        is not None,
    )
    check(
        "las varillas suben al frente",
        "var varillas = new List<object>(rellenosVarilla);" in drawer
        and "AlFrente(varillas);" in drawer,
    )
    check(
        "el relleno de la varilla va debajo de su contorno",
        drawer.find("new List<object>(rellenosVarilla)")
        < drawer.find("varillas.AddRange(circulos)"),
    )

    # ---------- Detalles de la macro de secciones que faltaban ----------
    check(
        "el rotulo lleva el renglon del estribo diamante",
        '"Est. Diamante ' in drawer,
    )
    check(
        "el diamante se rotula con la misma separacion que el estribo",
        re.search(r'lineas\.Add\(\$"Est\. Diamante \{clave\} @\{sep\} cm"\)', drawer)
        is not None,
    )

    # CopyObjects no conserva el orden de dibujo: hay que rehacerlo DENTRO del bloque
    # Se exige la LLAMADA, no la declaracion: buscar 'OrdenarDentroDelBloque('
    # a secas casaba con la firma del metodo y la comprobacion pasaba aunque
    # nadie lo llamara.
    check(
        "se reordena dentro del bloque",
        "OrdenarDentroDelBloque(bloque);" in drawer,
    )
    check(
        "el reordenado del bloque se hace tras CopyObjects",
        0 <= drawer.find("CopyObjects de la seccion")
        < drawer.find("OrdenarDentroDelBloque(bloque)"),
    )
    check(
        "el relleno del estribo NO sube dentro del bloque",
        re.search(r"!esHatch && string\.Equals\(capa, \"ESTRIBOS\"", drawer) is not None,
    )

    # Islas duplicadas: con estilo Normal se anulan y la varilla saldria rayada
    check(
        "hay guarda contra duplicar la isla del hatch",
        "yaSurtioEfecto" in arreglos and "yaSurtioEfecto:" in drawer,
    )
    check("se cuenta el numero de lazos del hatch", "NumberOfLoops" in drawer)

    # Tangencia de las islas: con 2 micras AutoCAD rechaza la isla con
    # 0x80200003 Invalid input, porque la varilla de esquina cae EXACTAMENTE sobre
    # la frontera interior del estribo.
    # La holgura vive entre DOS defectos opuestos, y los dos se sufrieron:
    #
    #   Demasiado poca (las 2 micras de la macro): la varilla de esquina cae
    #   EXACTAMENTE sobre la frontera y AutoCAD rechaza la isla con
    #   0x80200003 Invalid input.
    #
    #   Demasiada (las 0.2 mm que se pusieron para arreglar lo anterior): la
    #   frontera del relleno se separa del contorno y aparece una RENDIJA del color
    #   del fondo. El usuario la vio como un halo blanco: "el hatch solido del
    #   estribo no llega a su linea".
    #
    # 20 micras: diez veces la macro, cincuenta veces menos que 0.2 mm.
    m_eps = re.search(r"EpsTangencia = ([\d.]+);", drawer)
    check("holgura de tangencia localizada", m_eps is not None)
    if m_eps:
        eps = float(m_eps.group(1))
        check(
            "la holgura supera las 2 micras de la macro (isla rechazada)",
            eps > 0.000002,
            f"es {eps} = {eps * 1000:.4f} mm",
        )
        check(
            "y no llega a verse como rendija (<= 0.05 mm)",
            eps <= 0.00005,
            f"es {eps * 1000:.4f} mm, la rendija se nota",
        )
    check("la holgura sigue a la escala del dibujo", "EpsTangencia * _f" in drawer)
    check(
        "la frontera del hatch usa la holgura escalada",
        "dEst - EpsHatch" in drawer and "dEst + EpsHatch" in drawer,
    )
    check(
        "el diagnostico dice QUE isla fallo",
        re.search(r"AppendInnerLoop del hatch '\{patron\}' \(\{Que\(isla\)\}\)", drawer)
        is not None,
    )

    # Empezar despues de lo ya dibujado
    check("existe la posicion inicial X", "public double PosicionInicialX()" in drawer)
    check(
        "el dibujo empieza despues de lo que ya existe",
        "dibujante.PosicionInicialX()" in codigo,
    )

    # ---------- Alzados ----------
    alz = leer(ruta("client", "src", "CadLink.Cad", "AlzadoDrawer.cs"))
    est = leer(ruta("client", "src", "CadLink.Cad", "Estribos.cs"))

    check("existe el dibujante de alzados", "class AlzadoDrawer" in alz)
    check("el reparto de estribos esta aparte y sin COM", "class Estribos" in est)
    check("no usa COM en el reparto de estribos", "AcadConnection" not in est)

    for pieza, nota in [
        ("CapsulasDeEstribo(", "estribo como capsula"),
        ("CaraSegmentada(", "cara de varilla cortada por los estribos"),
        ("VarillaConGanchos(", "varilla con dobleces"),
        ("Girar90(", "giro de 90 grados del alzado vertical"),
        ("BordeDeVarilla(", "contorno para el relleno de la varilla"),
        ("CorrerADerecha(", "gancho que se corre para no caer sobre un estribo"),
    ]:
        check(f"alzado: {nota}", pieza in alz)

    check(
        "la capsula se hace con bulge -1",
        re.search(r"\(0, -1\), \(2, -1\)", alz) is not None,
    )
    check(
        "el alzado vertical se gira y el horizontal no",
        "girar: true" in alz and "girar: false" in alz,
    )
    # Las reglas del elemento viven en UNA sola funcion, Estribos.CentrosDeAlzado,
    # usada por el dibujo Y por la vista previa. Antes cada uno las aplicaba por su
    # cuenta, y ese es el camino a que la vista previa diga 16 estribos y AutoCAD
    # dibuje 17.
    est = leer(ruta("client/src/CadLink.Cad/Estribos.cs"))

    check(
        "en columna se quita el ultimo estribo",
        "esColumna" in est and "centros.RemoveAt(centros.Count - 1)" in est,
    )
    # LAS FRONTERAS DE ZONA, AHORA SIEMPRE. Antes era "conFronteras: !vertical", copiando el VBA:
    # el estribo de la frontera entre zonas solo se ponia en el alzado horizontal. El problema es
    # que PorSeparacion descartaba tambien el estribo que cae EXACTAMENTE en la frontera, y en
    # columna y dado no lo tapaba nadie, asi que en cada frontera L/4-L/2 quedaba un hueco de DOS
    # separaciones. Medido: columna L=4.10 con 10-20-10 tenia dos huecos de 30 cm donde la
    # separacion mas holgada es 20. Ver tools/verificar_estribos.py.
    check(
        "las fronteras de zona se ponen SIEMPRE, no solo en el horizontal",
        "conFronteras: true" in est and "conFronteras: !vertical" not in est,
    )
    n_comp = len(re.findall(r"Estribos\.CentrosDeAlzado\(", codigo + alz))
    check("el dibujo y la vista previa usan el MISMO reparto", n_comp == 2,
          f"lo usan {n_comp} sitios, se esperaban 2")
    check("ya no se reparte por separado en cada sitio",
          "Estribos.Centros(" not in codigo)
    # El gancho, con la misma regla en los dos: 12 diametros en la trabe, el valor
    # de la columna T en la columna.
    check("el gancho usa la misma regla en el dibujo y en la vista previa",
          "Estribos.GanchoNominal(" in alz and "Estribos.GanchoNominal(" in codigo)
    check("y el recorte por lo que cabe, tambien",
          "Estribos.GanchoEfectivo(" in alz and "Estribos.GanchoEfectivo(" in codigo)
    # HOOK_DIAM_FACTOR del VBA. Cambiarlo estropea el gancho de TODAS las trabes y
    # el dibujo sigue pareciendo razonable, asi que conviene fijarlo.
    check("el gancho de la trabe son 12 diametros",
          "FactorGanchoDiametros = 12.0" in est)
    check("un gancho que no cabe se anula", "g < dBarraM ? 0 : g" in est)
    check(
        "guarda contra separacion menor que la minima",
        "maxPorSeparacion" in est,
    )
    check("el alzado reusa el helper de arreglos", "AcadArreglos.Llamar(" in alz)

    # La capa ALZADOS la tiene que crear el propio dibujante de alzados: asignar
    # una capa inexistente da 0x80200014 Key not found al insertar el bloque.
    check("el alzado crea su capa ALZADOS", '"ALZADOS", "CONCRETO"' in alz)
    check("se llama a AsegurarCapas del alzado", "dibujante.AsegurarCapas()" in codigo)
    check(
        "la capa se pone en su propio try, aparte de la insercion",
        "Poner el bloque" in alz,
    )

    # Solo cuatro familias llevan alzado
    check(
        "TipoDe puede devolver null",
        "private static TipoElemento? TipoDe(" in codigo,
    )
    check(
        "lo que no lleva alzado se omite",
        re.search(r"if \(TipoDe\(r\.Elemento, r\.Id\) is null\)\s*\{\s*omitidos\.Add", codigo, re.S)
        is not None,
    )
    check(
        "los omitidos se informan",
        "Sin alzado (" in codigo,
    )
    check(
        "la vista previa respeta el filtro",
        re.search(r"if \(TipoDe\(s\.Elemento, s\.Id\) is null\)", codigo) is not None,
    )
    check(
        "CT- se prueba antes que C-",
        codigo.find('i.StartsWith("CT-"') < codigo.find('i.StartsWith("C-"'),
    )

    # CONFIRMADO con el autor: castillos y cadenas NO llevan alzado. Se comprueba
    # que la clasificacion reconozca EXACTAMENTE las cuatro familias y ninguna mas,
    # para que nadie las agregue por descuido mas adelante.
    m_tipo = re.search(
        r"private static TipoElemento\? TipoDe\(.*?\n    \}", codigo, re.S)
    check("clasificacion de alzado localizada", m_tipo is not None)

    if m_tipo:
        cuerpo = m_tipo.group(0)

        reconocidos = set(re.findall(r'e == "([A-ZÁÉÍÓÚÑ ]+)"', cuerpo))
        check(
            "solo se reconocen las cuatro familias con alzado",
            reconocidos == {"CONTRATRABE", "COLUMNA", "DADO", "TRABE"},
            f"reconoce {sorted(reconocidos)}",
        )

        for excluido in ["CASTILLO", "CADENA"]:
            check(
                f"{excluido} no lleva alzado",
                excluido not in cuerpo.upper().replace("CASTILLOS, CADENAS", ""),
            )
    check("hay boton de alzados en la primera pestaña", 'x:Name="AlzadosButton"' in xaml)
    check("el boton llama al dibujante", "new AlzadoDrawer(" in codigo)
    check("columna W en la tabla", "LongitudM" in xaml and "LongitudM" in codigo)
    check(
        "la vista previa dibuja el alzado",
        "DibujarAlzadoPrevio(" in codigo,
    )
    check(
        "la vista previa usa el MISMO reparto de estribos",
        re.search(r"DibujarAlzadoPrevio.*?Estribos\.CentrosDeAlzado\(", codigo, re.S)
        is not None,
    )

    filas = leer(ruta("client", "src", "CadLink.App", "Models", "StructuralRows.cs"))
    check("gancho por omision de 5 cm", "_ganchoCm = 5" in filas)
    check(
        "los ejemplos usan gancho de 5 cm",
        "GanchoCm = 1," not in filas and "GanchoCm = 1.5," not in filas,
    )
    check(
        "el relleno del estribo NO sube al frente",
        'nombre.Contains("hatch"' in drawer,
    )


# ======================================================================
# 13. Errores de compilacion que ya me colaron dos veces
#
#     (a) 'yield return' dentro de un try con catch  -> CS1626
#     (b) llamar a un metodo con un numero de argumentos distinto al que
#         declara -> CS1501. Este es el que hacia falta: al editar la firma de
#         un metodo se me borro la linea de parametros, y el resultado fueron
#         14 errores en la maquina del usuario.
# ======================================================================
def _sin_comentarios(t: str) -> str:
    t = re.sub(r"//[^\n]*", "", t)
    t = re.sub(r"/\*.*?\*/", "", t, flags=re.S)
    # cadenas y caracteres, para que sus parentesis y comas no cuenten
    t = re.sub(r'@"(?:[^"]|"")*"', '""', t)
    t = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', t)
    t = re.sub(r"'(?:[^'\\]|\\.)'", "'x'", t)
    return t


def _lista_balanceada(t: str, i: int) -> tuple[str, int] | None:
    """Devuelve el contenido del parentesis que abre en 't[i]' y donde cierra."""
    if i >= len(t) or t[i] != "(":
        return None

    prof = 0
    for j in range(i, len(t)):
        c = t[j]
        if c == "(":
            prof += 1
        elif c == ")":
            prof -= 1
            if prof == 0:
                return t[i + 1 : j], j
    return None


def _cuenta_argumentos(lista: str) -> int:
    """Cuenta argumentos de primer nivel: ignora comas dentro de (), [], {}, <>."""
    if lista.strip() == "":
        return 0

    n = 1
    par = cor = lla = ang = 0
    for c in lista:
        if c == "(":
            par += 1
        elif c == ")":
            par -= 1
        elif c == "[":
            cor += 1
        elif c == "]":
            cor -= 1
        elif c == "{":
            lla += 1
        elif c == "}":
            lla -= 1
        elif c == "<":
            ang += 1
        elif c == ">":
            ang = max(0, ang - 1)
        elif c == "," and par == cor == lla == 0 and ang == 0:
            n += 1
    return n


def v13_compilacion() -> None:
    print("\n[13] Errores de compilacion frecuentes")

    # ---------- (a) yield dentro de try/catch ----------
    problemas_yield = []
    for p in archivos(".cs"):
        t = _sin_comentarios(leer(p))
        for m in re.finditer(r"\btry\s*\{", t):
            ini = m.end() - 1
            prof = 0
            fin = None
            for j in range(ini, len(t)):
                if t[j] == "{":
                    prof += 1
                elif t[j] == "}":
                    prof -= 1
                    if prof == 0:
                        fin = j
                        break
            if fin is None:
                continue

            cuerpo = t[ini:fin]
            despues = t[fin : fin + 40]
            if "yield" in cuerpo and re.match(r"\}\s*catch", despues):
                linea = t[:ini].count("\n") + 1
                problemas_yield.append(f"{rel(p)}:{linea}")

    check(
        "sin 'yield' dentro de try con catch (CS1626)",
        not problemas_yield,
        "; ".join(problemas_yield),
    )

    # ---------- (b) numero de argumentos ----------
    # El tipo de retorno puede empezar con '(' porque puede ser una TUPLA, como
    # '(double[] Esquina, double[] Intermedia, double Y)'. Exigiendo que empezara
    # con letra, la declaracion no se encontraba y el metodo se quedaba SIN
    # revisar en silencio: un falso negativo, que es peor que no tener la
    # comprobacion. Fue justo lo que paso con Lecho().
    decl_re = re.compile(
        r"^[ \t]*(?:\[[^\]]*\]\s*)?"
        r"(?:private|public|internal|protected)\s+"
        r"(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+|new\s+|partial\s+|unsafe\s+)*"
        r"(?P<tipo>[A-Za-z_(][A-Za-z0-9_<>\?\[\],\.\s\(\)]*?)\s+"
        r"(?P<nombre>[A-Za-z_][A-Za-z0-9_]*)\s*(?=\()",
        re.M,
    )

    problemas_args = []
    revisados = 0
    sobrecargados = 0
    for p in archivos(".cs"):
        t = _sin_comentarios(leer(p))

        # nombre -> lista de (minimo, maximo) de parametros; y posiciones a saltar
        firmas: dict[str, list[tuple[int, int]]] = {}
        posiciones_decl: set[int] = set()

        for m in decl_re.finditer(t):
            nombre = m.group("nombre")
            if nombre in {"if", "for", "foreach", "while", "switch", "catch", "return", "using", "lock", "fixed"}:
                continue

            par = _lista_balanceada(t, m.end())
            if par is None:
                continue

            lista, _ = par
            total = _cuenta_argumentos(lista)

            # opcionales: 'tipo x = valor'  |  params
            partes = re.split(r",(?![^(<\[]*[)>\]])", lista) if lista.strip() else []
            opcionales = sum(1 for x in partes if "=" in x)
            tiene_params = "params " in lista

            minimo = total - opcionales
            maximo = 99 if tiene_params else total

            firmas.setdefault(nombre, []).append((minimo, maximo))
            posiciones_decl.add(m.end())

        for nombre, rangos in firmas.items():
            # sobrecargas: se omiten, hay que resolver por tipos
            if len(rangos) != 1:
                sobrecargados += 1
                continue

            revisados += 1
            minimo, maximo = rangos[0]

            for c in re.finditer(rf"(?<![A-Za-z0-9_.]){re.escape(nombre)}\s*(?=\()", t):
                if c.end() in posiciones_decl:
                    continue

                # 'new Nombre(' es un constructor, no este metodo
                antes = t[max(0, c.start() - 6) : c.start()]
                if antes.rstrip().endswith("new"):
                    continue

                par = _lista_balanceada(t, c.end())
                if par is None:
                    continue

                n = _cuenta_argumentos(par[0])
                if not (minimo <= n <= maximo):
                    linea = t[: c.start()].count("\n") + 1
                    esperado = f"{minimo}" if minimo == maximo else f"{minimo}..{maximo}"
                    problemas_args.append(
                        f"{rel(p)}:{linea} {nombre}() recibe {n}, declara {esperado}"
                    )

    check(
        "los metodos se llaman con el numero de argumentos que declaran (CS1501)",
        not problemas_args,
        "; ".join(sorted(set(problemas_args))[:10]),
    )

    # Se informa la COBERTURA a proposito: si un cambio en las expresiones
    # regulares dejara de encontrar declaraciones, la comprobacion pasaria en
    # vacio y no habria forma de notarlo. Con el numero a la vista, si baja de
    # golpe se ve.
    print(f"        cobertura: {revisados} metodos revisados, "
          f"{sobrecargados} sobrecargados omitidos")
    check("la revision de argumentos cubre metodos de verdad", revisados >= 40,
          f"solo {revisados}")


def v14_bloques_diamante_etabs() -> None:
    """Bloques que ya existen, recorte bajo el diamante, punto decimal y ETABS."""
    print("\n[14] Bloques existentes, recorte del diamante, punto decimal, ETABS")

    drawer = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))
    diamante = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Diamante.cs"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    conex = leer(ruta("client/src/CadLink.Etabs/EtabsConnection.cs"))
    ensam = leer(ruta("client/src/CadLink.Etabs/EtabsAssembly.cs"))

    # ------------------------------------------------------------------
    # 1. La seccion que ya es bloque SE SALTA, como la macro
    # ------------------------------------------------------------------
    check("existe BloqueYaExiste", "public bool BloqueYaExiste(" in drawer)

    m_dib = re.search(r"public int Dibujar\(SeccionCad s.*?\n    \}", drawer, re.S)
    check("existe Dibujar", m_dib is not None)

    if m_dib:
        cuerpo = m_dib.group(0)
        # Se comprueba la LLAMADA y que salga ANTES de contar entidades: si el
        # salto fuera despues, ya se habria dibujado algo.
        pos_chk = cuerpo.find("BloqueYaExiste(s.Id)")
        pos_ini = cuerpo.find("var inicio =")
        check("Dibujar salta la seccion que ya es bloque",
              pos_chk != -1 and pos_ini != -1 and pos_chk < pos_ini)
        check("la seccion saltada se anota", "_saltadas.Add(s.Id)" in cuerpo)

    # Lo que el usuario pidio: SALTAR, no borrar y recrear. La definicion
    # anterior no se debe borrar en ningun sitio.
    check("ya NO se borra la definicion anterior del bloque",
          "anterior.Delete()" not in drawer)
    check("las saltadas se pueden consultar",
          "public IReadOnlyList<string> Saltadas" in drawer)

    # El aviso al usuario: sin esto el salto es silencioso y el ingeniero cree
    # que el plano tiene el armado nuevo.
    check("se avisa de las secciones saltadas",
          "SE SALTARON" in codigo and
          "Redibujar las que ya existen" in codigo)
    check("las saltadas NO consumen su sitio en la fila",
          "dibujante.Saltadas.Count > saltadasAntes" in codigo)

    # ------------------------------------------------------------------
    # 1b. ...pero SE PUEDE forzar el redibujado (ActualizarSecciones)
    # ------------------------------------------------------------------
    # Sin esto, una seccion ya dibujada no habia forma de actualizarla salvo
    # purgando su bloque a mano en AutoCAD. El salto por si solo dejaba al
    # usuario sin poder dibujar nada.
    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))

    check("hay interruptor para redibujar", "public bool Redibujar { get; set; }" in drawer)
    check("hay casilla en la interfaz", 'x:Name="RedibujarChk"' in xaml)
    check("la casilla se lee al dibujar",
          "Redibujar = RedibujarChk.IsChecked == true" in codigo)
    check("apagado por omision: la macro salta",
          'x:Name="RedibujarChk"' in xaml and
          not re.search(r'x:Name="RedibujarChk"[^>]*IsChecked="True"', xaml, re.S))

    check("se puede borrar una seccion para rehacerla",
          "public bool BorrarSeccion(" in drawer)
    check("se lee el punto donde ya estaba", "public double[]? PuntoDeInsercion(" in drawer)

    if m_dib:
        cuerpo = m_dib.group(0)
        # El orden manda: si se borrara antes de leer el punto, ya no habria a
        # quien preguntarselo y la seccion acabaria al final de la fila.
        pos_pto = cuerpo.find("destino = PuntoDeInsercion(s.Id)")
        pos_del = cuerpo.find("BorrarSeccion(s.Id)")
        check("el punto se lee ANTES de borrar la seccion",
              pos_pto != -1 and pos_del != -1 and pos_pto < pos_del)
        # Si no se pudo borrar, dibujar encima dejaria dos copias encimadas.
        check("si no se pudo borrar, se salta en lugar de encimar",
              "if (!BorrarSeccion(s.Id))" in cuerpo)

    m_bs = re.search(r"public bool BorrarSeccion\(.*?\n    \}", drawer, re.S)
    check("se puede leer BorrarSeccion", m_bs is not None)

    if m_bs:
        cuerpo = m_bs.group(0)
        # AutoCAD no deja borrar la definicion de un bloque con referencias vivas.
        pos_refs = cuerpo.find("r.Delete()")
        pos_def = cuerpo.find("def.Delete()")
        check("las inserciones se borran ANTES de la definicion",
              pos_refs != -1 and pos_def != -1 and pos_refs < pos_def)
        # Borrar mientras se recorre corre los indices y deja inserciones vivas.
        check("las inserciones se juntan antes de borrarlas",
              "refs.Add((object)ent)" in cuerpo)

    check("la seccion rehecha vuelve a su sitio",
          "insercion.InsertionPoint = destino" in drawer)
    check("la rehecha NO ocupa lugar nuevo en la fila",
          "if (!dibujante.UltimaFueASuSitio)" in codigo)
    check("se avisa de las rehechas", "Se REHICIERON" in codigo)

    # ------------------------------------------------------------------
    # 1c. La vista del modelo ocupa todo el ancho
    # ------------------------------------------------------------------
    m_tabs = re.search(r'<TabControl x:Name="VistaTabs".*?>', xaml, re.S)
    check("se puede leer el TabControl de la vista", m_tabs is not None)

    if m_tabs:
        check("la vista del modelo no lleva ancho maximo",
              "MaxWidth" not in m_tabs.group(0))
        check("la vista del modelo se estira",
              'HorizontalAlignment="Stretch"' in m_tabs.group(0))

    # Y que siga midiendo el lienzo en vez de un ancho fijo, o al estirarse el
    # modelo se dibujaria fuera del cuadro.
    vista = leer(ruta("client/src/CadLink.App/VistaModelo.cs"))
    # Se cuentan las DOS vistas, 3D y planta. Con un simple "esta en el archivo"
    # bastaba con que una lo hiciera bien, y la otra podia tener el ancho fijo:
    # la prueba de mutacion lo demostro.
    n_w = len(re.findall(r"lienzo\.ActualWidth", vista))
    n_h = len(re.findall(r"lienzo\.ActualHeight", vista))
    check("las dos vistas miden el lienzo real", n_w >= 2 and n_h >= 2,
          f"ancho {n_w}, alto {n_h}, se esperaban 2 de cada")
    check("ninguna vista usa un ancho fijo",
          not re.search(r"var w = \d", vista) and not re.search(r"var h = \d", vista))
    check("las dos vistas se redibujan al cambiar de tamaño",
          "Vista3DCanvas.SizeChanged" in codigo and "PlantaCanvas.SizeChanged" in codigo)
    # Sin recorte, el modelo se sale del cuadro al hacer zoom o al moverlo.
    check("los dos lienzos recortan lo que se sale",
          len(re.findall(r'x:Name="(?:Vista3DCanvas|PlantaCanvas)"[^>]*?ClipToBounds="True"',
                         xaml, re.S)) == 2)

    # ------------------------------------------------------------------
    # 1d. Contornos NEGROS de verdad, no ACI 7 (que sale blanco)
    # ------------------------------------------------------------------
    # El usuario: "su linea debe ser negra siempre y no blanca cuando es seccion
    # rellena". Salian blancas porque ColorNegro no lograba crear el AcCmColor por
    # ProgID (su numero de version NO es el año, y en AutoCAD 2026 ya no se
    # acierta adivinandolo del 26 al 15) y caia al ACI 7, que sobre fondo oscuro
    # se dibuja BLANCO. Y caia en silencio.
    alzado = leer(ruta("client/src/CadLink.Cad/AlzadoDrawer.cs"))

    for arch, texto in (("SeccionDrawer", drawer), ("AlzadoDrawer", alzado)):
        m_cn = re.search(r"private object\? ColorNegro\(.*?\n    \}", texto, re.S)
        check(f"{arch}: se puede leer ColorNegro", m_cn is not None)

        if m_cn:
            cuerpo = m_cn.group(0)
            # La via buena: el TrueColor de una entidad ya dibujada. No depende de
            # ninguna version porque no hay ningun nombre que acertar.
            check(f"{arch}: el color se saca del TrueColor de una entidad",
                  "e.TrueColor" in cuerpo)
            # Y debe probarse ANTES de la cascada de ProgIDs.
            pos_tc = cuerpo.find("e.TrueColor")
            pos_pid = cuerpo.find("AutoCAD.AcCmColor.")
            check(f"{arch}: el TrueColor se prueba antes que los ProgID",
                  pos_tc != -1 and pos_pid != -1 and pos_tc < pos_pid)
            # Y que esa rama sea ALCANZABLE. Con solo mirar el orden del texto, se
            # podia dejar el codigo ahi dentro de un 'if (false)' y el check pasaba:
            # lo demostro la prueba de mutacion.
            check(f"{arch}: la rama del TrueColor es alcanzable",
                  "if (ent is not null)" in cuerpo)

        check(f"{arch}: ColorNegro recibe la entidad",
              re.search(r"ColorNegro\(\(?object\)?\)?ent\)|ColorNegro\(ent\)", texto)
              is not None)

    # El aviso: si aun asi cae al ACI 7, hay que DECIRLO.
    check("se marca cuando hubo que caer al ACI 7", "_sinColorVerdadero = true" in drawer)
    check("hay revision del color negro", "public void RevisarColorNegro()" in drawer)
    check("la revision se llama al terminar", "dibujante.RevisarColorNegro()" in codigo)
    check("el aviso explica que el ACI 7 sale blanco",
          "se dibuja BLANCO" in drawer)

    # ------------------------------------------------------------------
    # 1e. El diamante NO se pasa como isla del rayado
    # ------------------------------------------------------------------
    # Es geometricamente imposible: su cinta sobresale de la cara interior del
    # estribo justo su grueso, porque se abraza a una varilla TANGENTE a esa cara.
    # Intentarlo generaba los 6 fallos que vio el usuario: 2 islas imposibles por
    # las 3 vias de marshalling.
    check("el diamante ya no se agrega como isla",
          "islas.Add(_diamExt)" not in drawer and "islas.Add(_diamInt)" not in drawer)
    check("y se explica por que", "no se usa como isla del rayado" in drawer)

    # ------------------------------------------------------------------
    # 1f. ETABS: los miembros del modelo, por las interfaces
    # ------------------------------------------------------------------
    com = leer(ruta("client/src/CadLink.Etabs/ComLateBinding.cs"))

    check("Com busca los metodos en las interfaces",
          "MetodosDeInterfaz(" in com and "EtabsAssembly.TiposQueDeclaran(" in com)

    m_tg = re.search(r"public static object\? TryGet\(.*?\n    \}", com, re.S)
    check("se puede leer TryGet", m_tg is not None)

    if m_tg:
        cuerpo = m_tg.group(0)
        pos_if = cuerpo.find("TiposQueDeclaran")
        pos_id = cuerpo.find("InvokeMember")
        check("las interfaces se prueban ANTES que IDispatch en TryGet",
              pos_if != -1 and pos_id != -1 and pos_if < pos_id)

    m_call = re.search(r"public static object\? Call\(.*?\n    \}", com, re.S)
    check("se puede leer Call", m_call is not None)

    if m_call:
        cuerpo = m_call.group(0)
        check("Call prueba las interfaces primero",
              cuerpo.find("MetodosDeInterfaz") < cuerpo.find("PorIDispatch"))
        # La OAPI termina casi siempre en un CSys opcional. Exigir firma exacta
        # hacia que no se encontrara NI UN metodo.
        check("se rellenan los parametros opcionales",
              "HasDefaultValue" in cuerpo and "Type.Missing" in cuerpo)
        # MethodInfo.Invoke escribe los ByRef en el arreglo que se le pasa; si se
        # copio a otro mas grande, hay que devolverlos.
        check("los ByRef se devuelven al arreglo original",
              "Array.Copy(todos, args, args.Length)" in cuerpo)

    lector = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("el lector adjunta el detalle por miembro",
          "Detalle por miembro" in lector and "Com.Bitacora" in lector)
    check("la bitacora se limpia en cada lectura", "Com.Bitacora.Clear()" in lector)
    check("se avisa si no se leyo nada del modelo",
          "no se pudo leer NADA" in lector)

    # ------------------------------------------------------------------
    # 2. Recorte del estribo bajo el diamante
    # ------------------------------------------------------------------
    check("existe el recorte bajo el diamante",
          "private void RecortarEstriboBajoDiamante(" in diamante)
    check("el recorte se LLAMA al terminar el diamante",
          "RecortarEstriboBajoDiamante(contorno, centros, dDia)" in diamante)

    # Todos los tramos rectos deben registrarse, o el diamante cruzaria los que
    # falten. Son 8: 4 del estribo exterior y 4 del interior.
    n_tramos = (len(re.findall(r"\bHorizontal\(contorno,", drawer)) +
                len(re.findall(r"\bVertical\(contorno,", drawer)))
    check("los 8 tramos rectos del estribo se registran", n_tramos == 8,
          f"son {n_tramos}")
    # Ningun tramo recto del estribo puede dibujarse por su cuenta: el que se
    # escapara quedaria cruzado por el diamante, y solo en un lado de la seccion,
    # que es el peor tipo de error porque parece aleatorio.
    #
    # Se revisan SOLO los dos metodos del estribo. Las colas del gancho tambien
    # llaman a Linea y quedan fuera a proposito: son diagonales de 45 grados en la
    # esquina, y el diamante se dobla en el centro y a media altura, nunca ahi.
    for metodo in ("EstriboExterior", "EstriboInterior"):
        m = re.search(
            r"private void " + metodo + r"\(.*?\n    \}", drawer, re.S)
        check(f"se puede leer {metodo}", m is not None)

        if m:
            check(f"{metodo} no dibuja tramos sin registrar",
                  "Agregar(contorno, Linea(" not in m.group(0))
            # Y que de verdad registre los cuatro que le tocan.
            n = (len(re.findall(r"\bHorizontal\(contorno,", m.group(0))) +
                 len(re.findall(r"\bVertical\(contorno,", m.group(0))))
            check(f"{metodo} registra sus 4 tramos", n == 4, f"son {n}")

    # Los tres seguros contra borrar el estribo por una cuenta mal hecha.
    m_rec = re.search(
        r"private void RecortarEstriboBajoDiamante\(.*?\n    \}", diamante, re.S)
    check("se puede leer el cuerpo del recorte", m_rec is not None)

    if m_rec:
        cuerpo = m_rec.group(0)
        pos_dibuja = cuerpo.find("var nuevos = new List<object>()")
        pos_borra = cuerpo.find("Borrar(tramo.Ent)")
        check("los trozos se dibujan ANTES de borrar el tramo original",
              pos_dibuja != -1 and pos_borra != -1 and pos_dibuja < pos_borra)
        check("no se borra el tramo si no se pudo redibujar",
              "if (nuevos.Count == 0)" in cuerpo)
        check("hay tope de seguridad al recorte",
              "FraccionMaxRecorte * largo" in cuerpo)
        check("el tramo recortado se cambia por los trozos en el contorno",
              "contorno.Remove(tramo.Ent)" in cuerpo and
              "contorno.AddRange(nuevos)" in cuerpo)

    # El bug que encontro la comprobacion numerica: sin ancho minimo, la cinta
    # tangente generaba huecos de ancho cero y troceaba el estribo por nada.
    check("los huecos de ancho cero se descartan",
          "if (fin - ini > minimo)" in diamante)
    check("el minimo se pasa desde el que recorta",
          "dDia, LargoMinTramo)" in diamante)

    # El recorte NO depende del tipo de seccion: el usuario lo pidio para las dos.
    if m_rec:
        check("el recorte se aplica en los dos tipos de seccion",
              "conFondoSolido" not in m_rec.group(0))

    check("hay comprobacion numerica del recorte",
          os.path.exists(ruta("tools/verificar_recorte_diamante.py")))

    # ------------------------------------------------------------------
    # 3. El punto como separador decimal
    # ------------------------------------------------------------------
    check("la cota fija el punto como separador",
          'PropCota(cota, "DecimalSeparator", ".")' in drawer)
    # El tipo importa: la variable va en ASCII, la propiedad en texto.
    # Se prueba el codigo ASCII (lo documentado) y el caracter en texto como
    # respaldo: el AutoCAD 2026 del usuario rechaza el 46 con 0x80210066.
    check("DIMDSEP prueba el ASCII y el caracter en texto",
          'Dimvar("DIMDSEP", 46, ".")' in drawer)
    check("Dimvar acepta valores alternativos",
          "private void Dimvar(string nombre, params object[] valores)" in drawer)
    check("solo se reporta si fallan TODAS las formas",
          "no acepta {formas}" in drawer)
    check("la propiedad NO se fija con el numero 46",
          'DecimalSeparator", 46' not in drawer)

    # ------------------------------------------------------------------
    # 4. ETABS: los miembros se buscan en las INTERFACES
    # ------------------------------------------------------------------
    check("existe la busqueda de tipos por miembro",
          "public static List<Type> TiposQueDeclaran(" in ensam)
    check("las interfaces van primero", "OrderByDescending(t => t.IsInterface)" in ensam)
    check("se toleran los tipos que no cargan",
          "ReflectionTypeLoadException" in ensam)

    check("existe la busqueda de metodos en las interfaces",
          "private MethodInfo? MetodoDe(" in conex)
    # Se comprueba la LLAMADA: la razon del fallo era que GetObject se buscaba
    # solo en la clase Helper.
    check("GetObject se busca con MetodoDe",
          'MetodoDe(tipoHelper, "GetObject", typeof(string))' in conex)
    check("GetObjectProcess tambien",
          'MetodoDe(tipoHelper, "GetObjectProcess"' in conex)
    check("ya no se busca GetObject solo en la clase",
          'tipoHelper.GetMethod("GetObject"' not in conex)

    check("se guarda el tipo de retorno de GetObject",
          "_tipoOapi = m.ReturnType" in conex)

    m_sap = re.search(r"private object\? IntentarSapModel\(.*?\n    \}", conex, re.S)
    check("se puede leer IntentarSapModel", m_sap is not None)

    if m_sap:
        cuerpo = m_sap.group(0)
        # La via de la interfaz debe ir PRIMERO: es la unica que funciona con el
        # envoltorio __ComObject, y las otras solo gastan intentos.
        pos_iface = cuerpo.find("TiposQueDeclaranSapModel()")
        pos_disp = cuerpo.find('Com.Get(candidato, "SapModel")')
        check("la via de la interfaz se prueba ANTES que IDispatch",
              pos_iface != -1 and pos_disp != -1 and pos_iface < pos_disp)
        # El dato que faltaba en el diagnostico para poder concluir algo.
        check("el diagnostico dice el tipo real del objeto",
              "tipo en ejecución" in cuerpo)

    check("el diagnostico lista los miembros si no encuentra el metodo",
          "private static string MiembrosDe(" in conex and
          "MiembrosDe(tipoHelper)" in conex)


def v16_extruida_piers() -> None:
    """Vista extruida, corte del diamante en los tramos rectos, y piers de muros."""
    print("\n[16] Vista extruida, corte en los rectos del diamante, piers")

    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    vista = leer(ruta("client/src/CadLink.App/VistaModelo.cs"))
    ext = leer(ruta("client/src/CadLink.App/VistaModelo.Extruida.cs"))
    diamante = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Diamante.cs"))

    # ------------------------------------------------------------------
    # 1. El corte del diamante cubre TAMBIEN los tramos rectos
    # ------------------------------------------------------------------
    # El defecto: "aun hay lineas que no se cortan en la interseccion del estribo
    # de diamante". El corte solo miraba los DISCOS de la cinta, asi que cortaba
    # en los dobleces y no en las diagonales.
    check("la geometria de la cinta esta separada del dibujo",
          "private static (double[] Pts, double[] Bulges)? GeometriaCinta(" in diamante)
    # Que el dibujo la USE: si dibujara con otra cuenta, el corte no caeria sobre
    # la linea dibujada.
    check("la cinta se dibuja con esa misma geometria",
          "var geo = GeometriaCinta(centros, extra);" in diamante)
    check("el corte usa la misma geometria que el dibujo",
          "GeometriaCinta(centros, dDia)" in diamante)

    check("existe el corte contra el poligono de tangencias",
          "private static List<(double Ini, double Fin)> DentroDelPoligono(" in diamante)
    check("y se llama desde el calculo de lo tapado",
          "DentroDelPoligono(tramo, geo.Value.Pts)" in diamante)

    m_tap = re.search(
        r"private static List<\(double Ini, double Fin\)> TramoTapadoPorLaCinta\(.*?\n    \}",
        diamante, re.S)
    check("se puede leer TramoTapadoPorLaCinta", m_tap is not None)

    if m_tap:
        cuerpo = m_tap.group(0)
        # Las DOS mitades: discos y poligono. Con una sola, el corte queda a medias.
        check("lo tapado suma discos Y tramos rectos",
              "DentroDelPoligono" in cuerpo and "radio * radio" in cuerpo)
        check("los intervalos se fusionan",
              "brutos.Sort(" in cuerpo)

    # El punto dentro del poligono, por conteo de cruces: aguanta poligonos no
    # convexos, que es el caso si el diamante abraza dos varillas muy juntas.
    check("el dentro/fuera va por conteo de cruces",
          "private static bool PuntoEnPoligono(" in diamante)

    check("la comprobacion numerica cubre los tramos rectos",
          "Tramos rectos de la cinta" in
          leer(ruta("tools/verificar_recorte_diamante.py")))

    # ------------------------------------------------------------------
    # 00. Cotas del DOBLEZ DE LOS GANCHOS
    # ------------------------------------------------------------------
    # Faltaban: el gancho se dibujaba pero no se acotaba, asi que del plano no salia
    # la medida con la que se corta y se dobla la varilla en obra.
    alzg = leer(ruta("client/src/CadLink.Cad/AlzadoDrawer.cs"))

    check("existen las cotas del gancho", "private void CotasDeGancho(" in alzg)
    # Se comprueba la LLAMADA, y que este en el alzado HORIZONTAL: la macro no
    # acota el gancho en el vertical, porque ahi el valor lo escribio el usuario.
    m_ah = re.search(r"private void AnotarHorizontal\(.*?\n    \}", alzg, re.S)
    check("se puede leer AnotarHorizontal", m_ah is not None)
    if m_ah:
        check("el alzado horizontal acota el gancho",
              "CotasDeGancho(x, y, x1, geo)" in m_ah.group(0))

    m_av = re.search(r"private void AnotarVertical\(.*?\n    \}", alzg, re.S)
    check("se puede leer AnotarVertical", m_av is not None)
    if m_av:
        check("el vertical NO acota el gancho, como la macro",
              "CotasDeGancho(" not in m_av.group(0))

    m_cg = re.search(r"private void CotasDeGancho\(.*?\n    \}", alzg, re.S)
    check("se puede leer CotasDeGancho", m_cg is not None)

    if m_cg:
        cuerpo = m_cg.group(0)
        # El de arriba SOLO si los diametros de esquina son distintos: si son
        # iguales los dos ganchos miden lo mismo y la segunda cota repetiria el
        # numero.
        check("el gancho superior se acota solo si los diametros difieren",
              "if (geo.GanchoSup > 0 && esquinasDiferentes)" in cuerpo and
              "Math.Abs(geo.DSup - geo.DInf) > 1e-6" in cuerpo)
        # Con dos cotas, la de arriba se aparta para no montarse.
        check("con dos cotas la de arriba se aparta a HOOK_DIM_OFF_2",
              "geo.GanchoInf > 0\n                ? x1 + (HookDimOff2 * _f)" in cuerpo)
        # Se mide sobre la varilla, no sobre la cara del concreto: XbInf y Xb no
        # coinciden cuando el gancho inferior se tuvo que recorrer.
        check("la cota inferior se mide sobre su varilla", "x + geo.XbInf" in cuerpo)
        check("y la superior sobre la suya", "x + geo.Xb;" in cuerpo)

    for nombre, valor in [("HookDimOff1", "0.06"), ("HookDimOff2", "0.14")]:
        check(f"separacion {nombre} = {valor}",
              re.search(rf"{nombre} = {re.escape(valor)}\s*;", alzg) is not None)

    # ------------------------------------------------------------------
    # 000. Solapa y juego de planos
    # ------------------------------------------------------------------
    sol = leer(ruta("client/src/CadLink.App/Models/Solapa.cs"))

    check("existe el modelo de la solapa", "public sealed class Solapa" in sol)

    # Los campos de la solapa que pidio el usuario.
    for campo in ("Calculista", "Propietario", "Ubicacion", "Obra", "Dibujo",
                  "Fecha", "Escala", "Acotacion"):
        check(f"la solapa tiene {campo}",
              re.search(rf"public (?:string|DateTime) {campo}\b", sol) is not None)

    for caja in ("CalculistaBox", "PropietarioBox", "UbicacionBox", "ObraBox",
                 "DibujoBox", "FechaPicker", "EscalaSolapaBox", "AcotacionCombo"):
        check(f"hay casilla para {caja}", f'x:Name="{caja}"' in xaml)

    check("las casillas escriben en el modelo",
          len(re.findall(r"PrepararSolapa\(\)", codigo)) >= 2,
          "aparece solo una vez: esta declarado pero no se llama")

    # El NUMERO de plano se calcula, no se escribe.
    check("existe el juego de planos", "public sealed class JuegoDePlanos" in sol)
    check("el juego renumera", "public void Renumerar()" in sol)
    # Se renumera con la COLECCION, no en cada boton: asi no hay forma de cambiar
    # el juego por un camino que se olvide de renumerar.
    check("la renumeracion cuelga de la coleccion",
          "Planos.CollectionChanged += (_, _) => Renumerar();" in sol)

    m_ren = re.search(r"public void Renumerar\(\).*?\n    \}", sol, re.S)
    check("se puede leer Renumerar", m_ren is not None)
    if m_ren:
        cuerpo = m_ren.group(0)
        # Se recalculan TODOS, numero y total: al insertar o borrar cambian los
        # numeros de los siguientes Y el total de todos.
        check("se renumeran todos, no solo el nuevo",
              "for (var i = 0; i < total; i++)" in cuerpo)
        check("y se actualiza el total de todos",
              "Planos[i].Total = total;" in cuerpo)

    # El numero es de solo lectura en la cuadricula: si se pudiera escribir, el
    # usuario lo cambiaria y el juego quedaria con dos planos con el mismo numero.
    m_np = re.search(r'<DataGridTextColumn[^>]*Header="No\. Plano"[^>]*/>', xaml, re.S)
    check("se puede leer la columna No. Plano", m_np is not None)
    if m_np:
        # Acotado a la propia columna: sin esto el patron encontraba el
        # IsReadOnly de OTRA cuadricula mas abajo y la comprobacion era vacia.
        check("el numero de plano es de solo lectura",
              'IsReadOnly="True"' in m_np.group(0))
    check("Move pide la renumeracion a mano",
          "_juego.Planos.Move(i, j);" in codigo and "_juego.Renumerar();" in codigo)

    check("hay comprobacion de la numeracion",
          os.path.exists(ruta("tools/verificar_solapa.py")))

    # ------------------------------------------------------------------
    # 000b. Boton de leer plantas
    # ------------------------------------------------------------------
    check("hay boton de leer plantas de ETABS",
          "Leer plantas de ETABS" in xaml and 'Click="OnLeerPlantas"' in xaml)
    check("existe el manejador", "private void OnLeerPlantas(" in codigo)

    m_lp = re.search(r"private void OnLeerPlantas\(.*?\n    \}", codigo, re.S)
    check("se puede leer OnLeerPlantas", m_lp is not None)
    if m_lp:
        cuerpo = m_lp.group(0)
        # Un plano por nivel, del mas alto al mas bajo.
        check("arma un plano por nivel", "_juego.Agregar(contiene)" in cuerpo)
        check("del nivel mas alto al mas bajo",
              "OrderByDescending(n => n.ElevacionM)" in cuerpo)
        # Leer dos veces no debe duplicar las plantas en el juego.
        check("no duplica las plantas al leer dos veces",
              "if (_juego.Planos.Any(p =>" in cuerpo)

    # ------------------------------------------------------------------
    # 0. El bloque de la SECCION al costado del alzado, y las separaciones
    # ------------------------------------------------------------------
    # El usuario: "NO RESPETAS QUE LLEVEN EL BLOQUE DE LA SECCION A UN COSTADO, LA
    # SEPARACION ENTRE ELEMENTOS". Son cinco constantes y dos formulas del bucle
    # principal de la macro, asi que viven en una clase aparte, sin COM, para poder
    # comprobarlas numero a numero contra el VBA.
    alz2 = leer(ruta("client/src/CadLink.Cad/AlzadoDrawer.cs"))
    lay = leer(ruta("client/src/CadLink.Cad/AlzadoLayout.cs"))

    check("existe la colocacion como aritmetica aparte",
          "public static class AlzadoLayout" in lay)

    # El aire sobre las secciones NO es una constante de la macro: alli el valor es
    # una cota absoluta de 2 m desde el origen. Aqui es una separacion RELATIVA a la
    # seccion mas alta, y por eso se puede apretar a 1 m sin que nada se encime.
    check("el aire sobre las secciones es de 1 m",
          re.search(r"AireSobreSecciones = 1\.0\s*;", lay) is not None)

    # Las constantes de la macro, con su valor exacto.
    for nombre, valor in [
        ("SepSecciones", "0.6"),
        ("MargenCol", "0.4"),
        ("SepCaras", "0.3"),
        ("SepSecAlz", "0.2"),
        ("HookDimOff2", "0.14"),
    ]:
        check(f"separacion {nombre} = {valor}",
              re.search(rf"{nombre} = {re.escape(valor)}\s*;", lay) is not None)

    # alzadoWidth = DIM_OFF_3 + ROTULO_OFF_COL + 0.1
    check("el ancho de cotas del alzado vertical es el de la macro",
          "AnchoCotasVertical = 0.24 + 0.09 + 0.1" in lay)
    # El Else de la macro cuando el bloque de seccion no existe.
    check("hay medida supuesta si la seccion no existe",
          "AnchoSeccionSupuesto = 0.8" in lay and "AltoSeccionSupuesto = 0.4" in lay)

    # La SECCION se inserta de verdad, junto al alzado, y con su CORTE A-A'.
    check("el alzado inserta el bloque de la seccion",
          "public SeccionPuesta? InsertarSeccion(" in alz2)
    check("y se llama al dibujar el elemento",
          "InsertarSeccion(a.Id, xSec, y)" in alz2)

    # ------------------------------------------------------------------
    # La Y de la fila es RELATIVA a la seccion mas alta, no la cota fija
    # ------------------------------------------------------------------
    # La macro pone todo en Y=2 (su Y_BLOQUES). Con una contratrabe alta, la
    # seccion invade la fila de alzados. Se comprueba que ya no sea una constante.
    check("la Y de la fila se calcula, no es una constante",
          "public static double YArranque(" in lay)

    m_ya = re.search(r"public static double YArranque\(.*?\n    \}", lay, re.S)
    check("se puede leer YArranque", m_ya is not None)
    if m_ya:
        cuerpo = m_ya.group(0)
        # El aire son SIEMPRE 2 m sobre la mas alta. Un max() contra 2 haria que
        # con secciones bajitas se quedara en 2, que NO es lo que se pidio.
        check("el aire se suma al alto de la seccion",
              "altoMaximoSeccion + AireSobreSecciones" in cuerpo)
        check("y no se recorta con un maximo contra la cota fija",
              "Math.Max" not in cuerpo)

    check("el dibujante expone el alto de la seccion mas alta",
          "public double AltoMaximoSeccion" in alz2)
    check("y la Y de la fila sale de ahi",
          "AlzadoLayout.YArranque(AltoMaximoSeccion)" in alz2)

    cod_win = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    check("la ventana calcula el alto maximo y lo pasa",
          "AltoMaximoSeccion = AltoMaximoDeLasSecciones(escala)" in cod_win)

    m_am = re.search(
        r"private double AltoMaximoDeLasSecciones\(.*?\n    \}", cod_win, re.S)
    check("se puede leer AltoMaximoDeLasSecciones", m_am is not None)
    if m_am:
        # En circular el alto del dibujo es el DIAMETRO. Con AlturaCm, una columna
        # redonda contaria 0 y la fila de alzados se le echaria encima.
        check("en circular cuenta el diametro y no la altura",
              "s.EsCircular ? s.DiametroCm : s.AlturaCm" in m_am.group(0))

    check("hay comprobacion numerica de la Y de los alzados",
          os.path.exists(ruta("tools/verificar_y_alzados.py")))
    check("la seccion se rotula CORTE A-A'",
          "CORTE A-A'" in alz2 and "private void RotuloCorte(" in alz2)

    m_ins = re.search(r"public SeccionPuesta\? InsertarSeccion\(.*?\n    \}", alz2, re.S)
    check("se puede leer InsertarSeccion", m_ins is not None)

    if m_ins:
        cuerpo = m_ins.group(0)
        # Se apoya por su PAÑO INFERIOR, no por su punto de insercion: casi todos
        # los bloques de seccion traen el punto base en el centroide, y por eso una
        # columna de 50 cm insertada en Y=2 aparecia en 1.75.
        check("la seccion se apoya por su paño inferior",
              "y - mn[1]" in cuerpo)
        check("y su borde izquierdo en la x pedida", "x - mn[0]" in cuerpo)
        # Si el bloque no existe hay que DECIRLO, no dejar el alzado sin corte y
        # que el usuario lo descubra mirando el plano.
        check("se avisa si no hay bloque de seccion",
              "no hay bloque de sección" in cuerpo)

    # El avance de la fila lo decide AlzadoLayout, no quien llama.
    check("el elemento se dibuja completo",
          "public double DibujarElemento(" in alz2)
    check("quien llama usa DibujarElemento",
          "dibujante.DibujarElemento(a, x)" in codigo)
    check("y ya no avanza la x por su cuenta",
          "x += ancho + 0.6" not in codigo)
    # El MARGEN_COL de la columna, en un solo sitio.
    check("la x de la seccion se pide a AlzadoLayout",
          "AlzadoLayout.XSeccion(x0, a.EsVertical)" in alz2)

    # Las FORMULAS, no solo las constantes.
    #
    # Hace falta comprobarlas aqui porque verificar_layout_alzados.py tiene su PROPIA
    # copia de la logica: compara el VBA contra una traduccion del C#, no contra el C#
    # mismo. Eso sirve para ver que la aritmetica es la correcta, pero NO detecta que
    # alguien cambie el C# despues. Lo demostro una prueba de mutacion: cambiar el
    # alzado de la columna de "encima" a "al lado" pasaba limpio.
    m_col = re.search(
        r"public static Puesto Colocar\(.*?\n    \}", lay, re.S)
    check("se puede leer Colocar", m_col is not None)

    if m_col:
        cuerpo = m_col.group(0)

        # En la columna el alzado va ENCIMA de la seccion: arranca en su paño
        # superior mas SEP_SEC_ALZ.
        # Y ademas se le abre el aire del rotulo, que ahora va debajo del bloque
        # insertado y choca con la seccion si solo se deja SEP_SEC_ALZ.
        check("en la columna el alzado arranca sobre la seccion",
              "topeSeccion + SepSecAlz + AireRotuloAlzado" in cuerpo
              and "YAlzado = y1," in cuerpo)

        # La segunda cara, encima de la primera y con sitio para SU rotulo.
        check("la segunda cara sale del calculo unico del layout",
              "YSegundaCara(y1, largo)" in cuerpo)

        # En la trabe el alzado va a la DERECHA de la seccion, y los dos apoyados.
        check("en la trabe el alzado va al lado de la seccion",
              "XAlzado = x0 + anchoSeccion + SepSecAlz," in cuerpo)
        # Apoyados en la Y de la FILA, que llega como parametro. Antes era la
        # constante YBloques, o sea la cota fija de la macro.
        check("y los dos apoyados en la Y de la fila",
              "YAlzado = yArranque," in cuerpo)
        check("la Y de la fila llega como parametro y no como constante",
              "double yArranque)" in lay)

        # Los dos avances, cada uno con sus terminos.
        check("el avance de la columna es blockWidth + alzadoWidth + SEP_SECCIONES",
              "xSec + anchoSeccion + AnchoCotasVertical + SepSecciones" in cuerpo)
        check("el de la trabe incluye el largo y el aire del gancho",
              "x0 + anchoSeccion + SepSecAlz + largo + HookDimOff2 + SepSecciones"
              in cuerpo)

    # El MARGEN_COL solo en la columna, y en un solo sitio.
    check("XSeccion abre el margen solo en la columna",
          "vertical ? x0 + MargenCol : x0" in lay)

    # ------------------------------------------------------------------
    # Hueco para el rotulo del alzado, que ahora va FUERA del bloque
    # ------------------------------------------------------------------
    # El rotulo cuelga debajo del bloque insertado. En el alzado vertical debajo esta
    # la seccion, y en la segunda cara de una columna rectangular esta el alzado de la
    # primera: sin abrir hueco, el rotulo cae dentro de uno o de otro.
    # 0.19: el hueco sobre la seccion carga DOS cosas, la cota de la base del bloque y el
    # CORTE A-A' encima de ella. Valio 0.46 mientras el rotulo colgaba del pie del alzado,
    # bajo a 0.10 al mover el rotulo bajo el bloque de la SECCION, y ha vuelto a subir a
    # 0.19 al aparecer la cota, que empuja el CORTE de 15 a 24 cm.
    check("hay una constante para el aire sobre la seccion",
          "public const double AireRotuloAlzado = 0.19;" in lay)
    check("y la cuenta del aire es la del CORTE A-A' y su cota",
          "CORTE A-A'" in lay and "AltoCotaCorte" in lay)

    check("la segunda cara tiene su calculo en el layout",
          "public static double YSegundaCara(" in lay)
    check("y ese calculo suma SEP_CARAS mas el aire del rotulo",
          "yPrimera + largo + SepCaras + AireRotuloAlzado" in lay)

    # Estaba escrito DOS veces: en el layout y a mano en DibujarVertical con un 0.3
    # literal. Coincidian por suerte, y al abrir el hueco habrian dejado de coincidir.
    check("DibujarVertical usa el calculo del layout y no un literal",
          "AlzadoLayout.YSegundaCara(y, largo)" in alz2)
    check("y ya no queda el 0.3 escrito a mano",
          "var y2 = y + largo + 0.3;" not in alz2)

    # El rotulo se LLAMA, no solo se declara: renombrar el metodo dejaba pasar el
    # check anterior porque el texto seguia en el archivo.
    check("el CORTE A-A' se dibuja de verdad",
          "RotuloCorte(x + (ancho / 2), y + alto + (AltoCotaCorte * _f));" in alz2)

    # ------------------------------------------------------------------
    # EL BLOQUE DE SECCION INSERTADO SE ACOTA
    # ------------------------------------------------------------------
    # Esto faltaba, y faltaba por algo que no se ve: la seccion de concreto SI se acota
    # cuando se dibuja en su propia hoja, pero esas cotas NO ENTRAN AL BLOQUE, porque
    # SeccionDrawer.Bloquear se salta a proposito todo lo que este en las capas COTAS y
    # ROTULOS. Asi que al insertar el mismo bloque como CORTE A-A' junto a su alzado,
    # llegaba sin una sola cota: se veia la seccion pero no cuanto medía, con el alzado al
    # lado completamente acotado.
    check("las cotas y el rotulado NO entran al bloque de la seccion",
          'string.Equals(capa, "COTAS", StringComparison.OrdinalIgnoreCase)'
          in leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs")))

    check("por eso el alzado acota el bloque insertado",
          "private void CotasDelCorte(" in alz2
          and "CotasDelCorte(x, y, ancho, alto);" in alz2)

    check("y acota su CAJA REAL, no las medidas capturadas",
          "var caja = Caja(br);" in alz2 and "mx[0] - mn[0]" in alz2)

    check("la base va arriba y la altura a la derecha, como en la macro",
          "x + (ancho / 2), y + alto + off" in alz2
          and "x + ancho + off, y + (alto / 2)" in alz2)

    # Con el texto VACIO, o sea con el numero que mide AutoCAD: las demas cotas del
    # alzado llevan TextOverride con rotulos de armado, que es otra cosa.
    m_cotas_corte = re.search(
        r"private void CotasDelCorte\(.*?\n    \}", alz2, re.S)

    check("se puede leer CotasDelCorte", m_cotas_corte is not None)

    if m_cotas_corte:
        cuerpo = m_cotas_corte.group(0)

        check("las dos cotas del corte muestran el numero medido, no un rotulo",
              cuerpo.count("string.Empty") == 2)
        check("y una va girada, que es la de la altura",
              cuerpo.count("true);") == 1 and cuerpo.count("false);") == 1)
        check("un bloque sin caja no se acota",
              "if (ancho <= 0 || alto <= 0)" in cuerpo)

    check("hay comprobacion de la colocacion contra el VBA",
          os.path.exists(ruta("tools/verificar_layout_alzados.py")))

    check("y de que la cota del corte cabe debajo del CORTE A-A'",
          "la cota de la base cabe por debajo del CORTE"
          in leer(ruta("tools/verificar_layout_alzados.py")))

    # ------------------------------------------------------------------
    # 0b. Modulo nuevo: dibujar planos estructurales
    # ------------------------------------------------------------------
    # ------------------------------------------------------------------
    # SAP2000: el MISMO lector, con otro ProgID
    # ------------------------------------------------------------------
    # CSI comparte la OAPI entre ETABS y SAP2000 —misma interfaz cOAPI, mismo SapModel y
    # mismas llamadas para pisos, marcos y areas— asi que no hace falta un lector aparte:
    # basta decirle a la conexion a quien buscar.
    conx = leer(ruta("client/src/CadLink.Etabs/EtabsConnection.cs"))

    check("la conexion sabe a que programa de CSI va",
          "public enum ProgramaCsi" in conx and "public ProgramaCsi Destino" in conx)
    check("y el ProgID sale del destino, no de una constante fija",
          "private string ProgIdApp => Destino == ProgramaCsi.Sap2000" in conx)
    check("el ProgID de SAP2000 es SapObject, que es el que se escribe mal de memoria",
          '"CSI.SAP2000.API.SapObject"' in conx)
    check("y los Helper de SAP2000 tambien se prueban",
          '"SAP2000v1.Helper"' in conx)
    check("ya no queda el ProgID de ETABS escrito como constante unica",
          "private const string ProgIdEtabs" not in conx)

    # Los mensajes no pueden decir ETABS cuando se pidio SAP2000.
    check("los mensajes dicen a que programa se intento conectar",
          "NombreDelDestino" in conx)

    # Y NINGUNO puede decir ETABS a mano: el usuario pulso «Leer modelo de SAP2000» y le
    # salio «No se pudo leer ETABS», que es lo que reporto.
    sueltos = [l.strip() for l in conx.splitlines()
               if ("_bitacora.Add" in l or "EtabsException(" in l)
               and "ETABS" in l and "NombreDelDestino" not in l
               and "MensajeNoEncontrada" not in l]
    check("ningun mensaje dice ETABS a mano", not sueltos,
          f"{len(sueltos)}: " + "; ".join(sueltos[:2]))

    # ------------------------------------------------------------------
    # LO DE FONDO: la libreria tiene que ser la del programa
    # ------------------------------------------------------------------
    # El ProgID de SAP2000 SI se encontraba —la bitacora decia «Objeto activo
    # 'CSI.SAP2000.API.SapObject': encontrado»— pero luego no se podia sacar el SapModel,
    # porque la libreria cargada seguia siendo ETABSv1.dll y los tipos del enlace temprano
    # (cOAPI, cSapModel) salen de ella. El delator era el error
    # «Object of type 'System.Int32' cannot be converted to type 'ETABSv1.eSlabTypeX'».
    asmb = leer(ruta("client/src/CadLink.Etabs/EtabsAssembly.cs"))

    check("la libreria que se busca depende del programa",
          "public static bool ParaSap2000" in asmb
          and 'ParaSap2000 ? "SAP2000v1.dll" : "ETABSv1.dll"' in asmb)
    check("y tambien la carpeta donde se busca",
          'ParaSap2000 ? "sap2000" : "etabs"' in asmb)
    check("ya no queda el nombre de la dll como constante unica",
          'private const string NombreDll' not in asmb)

    # La cache tiene que distinguir el programa, o leer ETABS y despues SAP2000 en la
    # misma sesion devolveria la libreria de ETABS la segunda vez.
    check("la cache de la libreria distingue el programa",
          "_cargadoParaSap == ParaSap2000" in asmb
          and "_cargadoParaSap = ParaSap2000;" in asmb)

    # EL FALLO GRANDE: el nombre del TIPO Helper lleva el prefijo del ensamblado, y en la
    # libreria de SAP2000 ese prefijo es SAP2000v1, no ETABSv1. Pidiendolo en duro
    # devolvia null, la via del Helper se caia, y todo terminaba en el respaldo fallando
    # con «Object does not match target type».
    check("el prefijo de los tipos depende del programa",
          'PrefijoTipos => Destino == ProgramaCsi.Sap2000 ? "SAP2000v1" : "ETABSv1"' in conx)
    check("y el Helper se pide con ese prefijo, no en duro",
          'asm.GetType(PrefijoTipos + ".Helper")' in conx)
    check("ya no se pide el tipo de ETABS en duro",
          'GetType("ETABSv1.Helper")' not in conx)

    # El nombre del PROCESO tambien depende del programa: buscando siempre 'etabs' nunca
    # se daba con la carpeta de SAP2000, y encima se ofrecia la de ETABS como candidata.
    check("el proceso que se busca depende del programa",
          'p.ProcessName.Contains(CarpetaClave' in asmb)
    check("y en la conexion tambien",
          'EtabsAssembly.ParaSap2000 ? "sap2000" : "etabs"' in conx)

    # Ningun literal de ETABS puede decidir NADA: si queda uno, con SAP2000 se va por el
    # camino de ETABS y el usuario ve «no se pudo leer ETABS», que es lo que reporto.
    for arch, texto in (("EtabsAssembly", asmb), ("EtabsConnection", conx)):
        duros = [l.strip() for l in texto.splitlines()
                 if ('"etabs' in l.lower() or '"ETABS' in l)
                 and "ParaSap2000 ?" not in l
                 and "Destino == ProgramaCsi" not in l
                 and "NombreDelDestino" not in l
                 and 'ETABSv1.Helper", "CSI.ETABS' not in l
                 and not l.startswith("///")]
        check(f"{arch}: ningun literal de ETABS decide el comportamiento",
              not duros, f"{len(duros)}: " + "; ".join(duros[:2]))

    # ------------------------------------------------------------------
    # SAP2000 leia 0 frames y 0 areas: GetLabelNameList es de ETABS
    # ------------------------------------------------------------------
    # Devuelve nombre + etiqueta + piso de una vez, y la etiqueta y el piso son conceptos
    # de ETABS. SAP2000 no tiene ese metodo, asi que el lector se rendia y devolvia cero
    # aunque el modelo tuviera cientos de barras. Los PUNTOS si se leian porque usan
    # GetNameList, que es el comun: 232 puntos y 0 frames era la pista.
    lect = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))

    check("hay respaldo para la lista de nombres",
          "private static (string[] Nombres, string[] Etiquetas, string[] Niveles) "
          "ListaDeNombres(" in lect)

    m_ln = re.search(r"ListaDeNombres\(\s*object\? obj.*?\n    \}", lect, re.S)
    check("se puede leer ListaDeNombres", m_ln is not None)
    if m_ln:
        cuerpo = m_ln.group(0)
        check("intenta primero el metodo de ETABS",
              'Com.Call(obj, "GetLabelNameList"' in cuerpo)
        check("y cae al comun, que es el que tiene SAP2000",
              'Com.Call(obj, "GetNameList"' in cuerpo)
        check("y avisa de que se leyeron sin nivel, en vez de callarlo",
              "m.Avisos.Add(" in cuerpo)

    # Los dos sitios lo usan: si uno se quedara con GetLabelNameList, SAP2000 leeria
    # frames pero no areas, o al reves.
    check("los frames usan el respaldo",
          'ListaDeNombres(frameObj, m, "frames")' in lect)
    check("y las areas tambien",
          'ListaDeNombres(areaObj, m, "áreas")' in lect)
    check("ya no se llama a GetLabelNameList a pelo",
          'Com.Call(frameObj, "GetLabelNameList"' not in lect
          and 'Com.Call(areaObj, "GetLabelNameList"' not in lect)

    # Y el resumen no puede decir ETABS: el modelo puede venir de SAP2000.
    check("el resumen no atribuye el modelo a ETABS",
          "ETABS devolvió" not in leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs")))

    # Y hay que avisarle ANTES de cargar.
    m_con = re.search(r"public void Conectar\(\).*?\n    \}", conx, re.S)
    if m_con:
        check("Conectar le dice a la libreria a quien se le habla",
              "EtabsAssembly.ParaSap2000 = Destino == ProgramaCsi.Sap2000;"
              in m_con.group(0))

    # La pestaña y LA CASILLA. Se pidio elegir el programa en una casilla, no con un
    # boton por programa: con dos botones era facil pulsar el que no tocaba.
    # YA NO HAY PESTAÑA DE ETABS: se pidio meterla DENTRO de la de planos, y ahi esta. El
    # visor del modelo a la derecha de la planta, y la lectura del modelo -conexion, botones y
    # tablas- en un panel plegable, porque se usa una vez al empezar y despues estorba.
    check("el modulo de ETABS vive dentro de la pestaña de planos",
          'Header="ETABS/SAP2000"' not in xaml
          and 'x:Name="EtabsTab" Header="Dibujar planos estructurales"' in xaml
          and 'Header="Lectura del modelo y tablas de elementos"' in xaml)
    # Y EL CANDADO DE LA LICENCIA sigue atado a esa pestaña: sin licencia de ETABS no se
    # puede usar, como antes.
    check("y el candado de la licencia sigue en pie",
          "EtabsTab.IsEnabled = puedeEtabs;" in codigo)
    # LAS DOS VISTAS, UNA A CADA LADO: planta y cortes a la izquierda, 3D a la derecha, al
    # 50% cada una, que es como se pidio.
    check("la planta va a la izquierda y el 3D a la derecha",
          'Grid.Row="2" Grid.Column="0"' in xaml
          and 'Grid.Row="2" Grid.Column="1"' in xaml
          and "Vista en planta y cortes" in xaml
          and xaml.index('Vista en planta y cortes') < xaml.index('Vista del modelo'))
    # LAS DOS VISTAS AL MISMO NIVEL. Con dos StackPanel sueltos, cada lienzo empezaba donde
    # acabara lo que tuviera encima: la derecha llevaba una barra mas -dentro de su pestaña- y
    # el modelo salia medio renglon mas abajo que la planta. Con UNA rejilla de tres filas
    # -titulo, barra y lienzo- la altura de cada fila es la del cel mas alto, asi que los dos
    # lienzos arrancan en la misma linea pase lo que pase con las barras.
    check("las dos vistas quedan al mismo nivel, no disparejas",
          xaml.count("<RowDefinition Height=\"Auto\" />") >= 3
          and 'Grid.Row="1" Grid.Column="0"' in xaml
          and 'Grid.Row="1" Grid.Column="1"' in xaml
          and 'Grid.Row="0" Grid.Column="0"' in xaml)
    # Y LOS TRES LIENZOS, DEL MISMO ALTO: con alturas distintas, aunque arranquen juntos,
    # acabarian en sitios distintos.
    alturas = re.findall(r'x:Name="(?:PlantaCanvas|Vista3DCanvas|ExtruidaCanvas)"[^>]*?Height="(\d+)"',
                         xaml, re.S)
    check("y los tres lienzos miden lo mismo de alto",
          len(alturas) == 3 and len(set(alturas)) == 1,
          f"alturas: {alturas}")
    # LA BARRA DE LAS VISTAS, UNA SOLA: era la misma dentro de cada pestaña, y duplicada solo
    # servia para bajar el lienzo y para que las dos pudieran quedar descuadradas.
    check("la barra de las vistas es una, fuera de las pestañas",
          xaml.count('Tag="ISO"') == 1
          and xaml.count('Tag="ENCUADRAR"') == 1)
    check("hay una casilla para elegir el programa, con los dos",
          'x:Name="ProgramaCsiCombo"' in xaml
          and '<ComboBoxItem Content="ETABS" />' in xaml
          and '<ComboBoxItem Content="SAP2000" />' in xaml
          and 'SelectionChanged="OnProgramaCsiCambiado"' in xaml)
    check("y sus manejadores existen",
          "private void OnProgramaCsiCambiado(" in codigo
          and "private void OnImportModeloCsi(" in codigo)
    check("el boton de leer dice a que programa apunta",
          'x:Name="LeerModeloCsiButton"' in xaml
          and 'Click="OnImportModeloCsi"' in xaml
          and 'LeerModeloCsiButton.Content = $"Leer modelo de {NombreDestinoCsi}"' in codigo)
    check("ya no hay un boton por programa",
          'Click="OnImportSap2000"' not in xaml
          and 'Click="OnImportEtabs"' not in xaml
          and "private void OnImportSap2000(" not in codigo)

    # LA MISMA CASILLA EN LA PESTAÑA DE PLANOS. Se pidio poder elegir ahi tambien de que
    # programa se leen las plantas, y NO es otra opcion aparte: el programa elegido es UNO.
    #
    # Lo que cambio es COMO se mantienen iguales. Iban atadas con un enlace del XAML
    # -SelectedIndex por ElementName, de dos vias- y desde que la casilla de la lectura del
    # modelo vive dentro del panel PLEGADO, ese enlace dejo de servir: la casilla que aun no
    # tenia seleccion escribia su -1 en la otra y las dos se quedaban EN BLANCO, asi que no se
    # veia en que programa se estaba trabajando. Ahora se igualan en el code-behind.
    check("la pestaña de planos tiene su casilla, igualada a la del modelo",
          'x:Name="ProgramaCsiPlanosCombo"' in xaml
          and "ElementName=ProgramaCsiCombo" not in xaml
          and "ProgramaCsiCombo, ProgramaCsiPlanosCombo, ProgramaCsiSeccionesCombo" in codigo)
    check("y su boton dice de que programa lee las plantas",
          'x:Name="LeerPlantasButton"' in xaml
          and 'LeerPlantasButton.Content = $"Leer plantas de {NombreDestinoCsi}"' in codigo)

    # EL DESTINO SALE DE LA CASILLA, EN UN SOLO SITIO, y manda para TODA la pestaña:
    # probar la conexion, leer el modelo, leer los piers y armar los planos. Si una
    # conexion se abriera sin destino, ese boton le hablaria a ETABS con la casilla en
    # SAP2000, que es justo el error que se pidio quitar.
    check("el destino sale de la casilla, en un solo sitio",
          "private EtabsConnection.ProgramaCsi DestinoCsi =>" in codigo
          and "ProgramaCsiCombo?.SelectedIndex == 1" in codigo)
    check("y todas las conexiones de la ventana lo respetan",
          codigo.count("new EtabsConnection { Destino = DestinoCsi }") == 3
          and "new EtabsConnection()" not in codigo)

    # UN solo lector para los dos, o un arreglo entraria en uno y no en el otro.
    check("hay un solo lector para los dos programas",
          codigo.count("private void LeerModeloCsi(") == 1
          and "LeerModeloCsi(DestinoCsi);" in codigo)
    check("y los mensajes no atribuyen a ETABS lo que pudo salir de SAP2000",
          '$"{cx.NombreDelDestino} conectado."' in codigo
          and 'StatusText.Text = "ETABS conectado.";' not in codigo)

    # ------------------------------------------------------------------
    # EL ORDEN DE LAS PESTAÑAS Y LA DE SECCIONES DEL MODELO
    # ------------------------------------------------------------------
    # EL ORDEN CAMBIO A PETICION: la de ETABS/SAP2000 estaba de PENULTIMA -justo antes de
    # Licencia- y se pidio moverla JUNTO A la de dibujar planos, porque se trabaja con las dos
    # a la vez: se lee el modelo, se mira en el visor y se dibuja. Estando en filas distintas
    # del TabControl, cada vuelta eran dos clics y un salto de fila.
    # Solo las pestañas de PRIMER nivel: dentro de la de ETABS hay otro TabControl -las
    # vistas 3D y extruida- y sus pestañas no cuentan aqui. Se distinguen por la sangria.
    orden = [m.group(1) for m in
             re.finditer(r'\n            <TabItem[^>]*?Header="([^"]+)"', xaml)]
    for cabecera in ("Dibujar planos estructurales", "Secciones modelo", "Licencia"):
        check(f"existe la pestaña {cabecera}", cabecera in orden)

    # La de ETABS/SAP2000 YA NO ES UNA PESTAÑA: se pidio meterla dentro de la de planos.
    check("ETABS/SAP2000 ya no es una pestaña suelta", "ETABS/SAP2000" not in orden)

    if all(c in orden for c in ("Dibujar planos estructurales", "Secciones modelo",
                                "Licencia")):
        check("la de planos va antes que la de secciones del modelo",
              orden.index("Dibujar planos estructurales") < orden.index("Secciones modelo"))
        check("Licencia se queda de ultima",
              orden.index("Licencia") == len(orden) - 1)

    # LA TABLA DE SECCIONES DEL MODELO: es la hoja SECCIONES de la macro, con sus mismas
    # columnas, su mismo orden por tipo y su mismo criterio de clasificacion.
    secs = leer(ruta("client/src/CadLink.Etabs/SeccionesModelo.cs"))

    check("la hoja SECCIONES esta portada",
          "public static class SeccionesModelo" in secs
          and "public static List<Fila> Construir(" in secs
          and "public static string ClasificaTipo(" in secs
          and "public static string MaterialDeMuro(" in secs
          and "public static int OrdenDeTipo(" in secs)

    check("con los umbrales de la hoja CONFIG",
          "double CastilloLadoMaxCm = 20" in secs
          and "double DalaPeralteMaxCm = 25" in secs
          and '"TABIQUE,TABICON,BLOCK,BLOQUE,MAMPOSTERIA,LADRILLO,ADOBE"' in secs
          and '"CONCRETO,CONCRETE,C.A.,REFORZADO"' in secs)

    check("y con el orden de tipos de la macro",
          all(f'"{t}" => {n}' in secs for t, n in
              (("CASTILLO", 1), ("COLUMNA", 2), ("DALA", 3), ("TRABE", 4),
               ("CONTRATRABE", 5), ("DIAGONAL", 6), ("MURO", 7), ("LOSA", 8))))

    # Las diez columnas de la hoja, con su nombre tal cual.
    for col in ("TIPO", "SECCION DE ETABS", "FORMA", "MATERIAL", "T3 PERALTE (cm)",
                "T2 ANCHO / ESPESOR (cm)", "TF (cm)", "TW (cm)", "CANTIDAD", "NIVELES"):
        check(f"la tabla tiene la columna {col}",
              f'Header="{col}"' in xaml)

    check("la cuadricula se llena con el modelo que se leyo, sin volver a leer",
          'x:Name="SeccionesModeloGrid"' in xaml
          and "private void LlenarSeccionesModelo(ModeloEtabs modelo)" in codigo
          and "SeccionesModelo.Construir(modelo)" in codigo
          and codigo.count("LlenarSeccionesModelo(modelo);") == 2)
    # TOTALES O INDIVIDUALES: la tabla de elementos que estaba debajo del visor 3D se movio
    # a esta pestaña, y se ve UNA de las dos, no las dos a la vez.
    check("la tabla de elementos vive ahora en la pestaña de secciones",
          'x:Name="ElementosTitulo"' in xaml
          and 'x:Name="AlternarSeccionesButton"' in xaml
          and 'Click="OnAlternarSeccionesModelo"' in xaml)
    check("y el boton cambia entre las dos, sin verlas dobles",
          "private void OnAlternarSeccionesModelo(" in codigo
          and "SeccionesModeloGrid.Visibility = aIndividuales" in codigo
          and 'AlternarSeccionesButton.Content = aIndividuales ? "Ver totales" : "Ver individuales";'
              in codigo)

    check("y se puede copiar a Excel con tabuladores",
          'Click="OnCopiarSeccionesModelo"' in xaml
          and "private void OnCopiarSeccionesModelo(" in codigo
          and "Clipboard.SetText(" in codigo)
    check("su boton tambien dice de que programa lee",
          'x:Name="LeerSeccionesModeloButton"' in xaml
          and 'LeerSeccionesModeloButton.Content = $"Leer secciones de {NombreDestinoCsi}"'
              in codigo)

    # LA TABLA CAMBIA CON LA CASILLA. Con datos de ETABS y la casilla en SAP2000 se estaba
    # viendo la tabla del programa que NO decia la casilla, y sin avisar.
    check("se recuerda de que programa es el modelo que hay en memoria",
          "private EtabsConnection.ProgramaCsi? _destinoLeido;" in codigo
          and codigo.count("_destinoLeido = ") >= 2)
    check("la tabla se vacia cuando la casilla deja de coincidir",
          "private void SincronizarSeccionesConLaCasilla()" in codigo
          and "SeccionesModeloGrid.ItemsSource = null;" in codigo
          and "SincronizarSeccionesConLaCasilla();" in codigo)
    check("y el boton vuelve a LEER en vez de reaprovechar lo del otro programa",
          "_destinoLeido == DestinoCsi)" in codigo)

    # LO QUE HAY DE CADA COSA: longitud de los frames y area de los shell.
    check("la tabla trae la longitud total de los frames",
          "public double? LongitudTotalM { get; set; }" in secs
          and "fila.LongitudTotalM = Math.Round((fila.LongitudTotalM ?? 0) + e.LargoM, 3);" in secs
          and 'Header="LONGITUD TOTAL (m)"' in xaml)
    check("y el area total de los muros y las losas",
          "public double? AreaTotalM2 { get; set; }" in secs
          and "fila.AreaTotalM2 = Math.Round((fila.AreaTotalM2 ?? 0) + e.AreaM2, 3);" in secs
          and 'Header="AREA TOTAL (m²)"' in xaml)
    # El area es la del PAÑO, no la de su proyeccion en planta: un muro es vertical y en
    # planta mediria cero.
    check("el area es la del paño de verdad, con el metodo de Newell",
          "public double AreaM2" in leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
          and "nx += (a.Y - b.Y) * (a.Z + b.Z);"
              in leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs")))

    # EL MATERIAL, EN TODOS. Lo devuelve la misma llamada que las medidas y se estaba
    # tirando: por eso la columna salia en blanco en todo menos en los muros.
    check("el material de la propiedad se guarda",
          "public string Material { get; set; }"
              in leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
          and "e.Material = dims.Material;" in lect
          and "e.Material = prop.Material;" in lect
          and "private static string Material(object?[] a)" in lect)
    check("y en el muro se ven las dos cosas: la clasificada y la del modelo",
          "private static string Material(ElementoEtabs e, Opciones op)" in secs
          and 'return $"{clasificado} ({delModelo})";' in secs)

    # Y fuera el texto que no hacia falta encima de la tabla.
    check("la tabla ya no lleva la tarjeta de explicacion",
          "Que es esta tabla" not in xaml)

    # Y su prueba ejecutable.
    prs = leer(ruta("tools/prueba-secciones-modelo/Program.cs"))
    check("hay prueba ejecutable de la tabla de secciones",
          "using CadLink.Etabs;" in prs
          and "SeccionesModelo.Construir(m)" in prs
          and "EtabsReader.EspesorDesdeNombre" in prs
          and "return fallos == 0 ? 0 : 1;" in prs)

    # ------------------------------------------------------------------
    # EL PUNTO DE INSERCION DEL MARCO: POR ESTO LA BARRA APARECE MOVIDA
    # ------------------------------------------------------------------
    #  Es el «Assign - Frame - Insertion Point» de ETABS. En el modelo la barra se CALCULA
    #  sobre la linea que une sus dos nudos, pero la pieza que se construye -y la que hay que
    #  dibujar- esta donde la ponen su punto cardinal y sus offsets de nudo. Sin leerlo, el
    #  plano sale con las barras en el eje del nudo mientras en la pantalla de ETABS se ven
    #  corridas, y no hay forma de que cuadren.
    ins = leer(ruta("client/src/CadLink.Etabs/PuntoDeInsercion.cs"))
    lec_ins = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    mod_ins = leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))

    check("se lee el punto de insercion del marco, con las dos firmas de la API",
          "GetInsertionPoint_1" in lec_ins
          and '"GetInsertionPoint", a, 1, 2, 3, 4, 5, 6' in lec_ins
          and "private static void LeerPuntoDeInsercion(" in lec_ins)
    check("y hay valvula de escape para volver a la linea de los nudos",
          "public static bool AplicarPuntosDeInsercion { get; set; } = true;" in lec_ins)
    # LOS EJES LOCALES DE CSI, que son los que explican el signo: en la TRABE el eje 2 es
    # vertical y el 3 horizontal -asi que el offset del 3 la mueve en planta-, y en la
    # COLUMNA los dos son horizontales, asi que cualquier offset la mueve.
    check("los ejes locales siguen la convencion de CSI",
          "public static (double[] E1, double[] E2, double[] E3) Ejes(" in ins
          and "e2c = new[] { 0d, 0d, 1d };" in ins
          and "e3c = new[] { dy, -dx, 0d };" in ins
          and "e2c = new[] { 1d, 0d, 0d };" in ins)
    check("el punto cardinal corre el centro de la seccion al lado contrario",
          "public static (double D2, double D3) PorPuntoCardinal(" in ins
          and "var columna = (punto - 1) % 3;" in ins
          and "var fila = (punto - 1) / 3;" in ins
          and "if (punto < 1 || punto > 9)" in ins)
    # t3 se mide sobre el eje 2 y t2 sobre el 3: es la misma regla que ya seguia el lector
    # -«en la columna el ancho se mide sobre el eje 3, al contrario que en la viga»-.
    check("las dimensiones se toman con la regla que ya usaba el lector",
          "var dim2 = vertical ? e.AnchoM : e.PeralteM;" in lec_ins
          and "var dim3 = vertical ? e.PeralteM : e.AnchoM;" in lec_ins)
    # SOLO EN PLANTA: mover la Z de una trabe 2.5 cm no se ve en el plano y podria cambiarle
    # el nivel al que se asigna.
    check("solo se mueve la planta, la Z no se toca",
          "public static (double Dx, double Dy) EnPlanta(" in ins
          and "e.X1 += dxi;" in lec_ins
          and "e.Y2 += dyj;" in lec_ins
          and "e.Z1 +=" not in lec_ins)
    check("el elemento guarda cuanto lo movio, para el diagnostico",
          "public double MovidoXI { get; set; }" in mod_ins
          and "public bool ConPuntoDeInsercion =>" in mod_ins
          and "public int ConPuntoDeInsercion { get; set; }" in mod_ins)
    check("y el resumen del modelo lo dice, que es donde se ve la explicacion",
          "barra(s) van CORRIDAS respecto" in mod_ins
          and "Puntos cardinales distintos del centroide" in mod_ins)
    check("hay prueba ejecutable del punto de insercion",
          "trabe en +X con offset 3 = -0.025: NO se mueve en X" in prs
          and "y se mueve 2.5 cm en Y, que es lo que se ve corrido" in prs
          and "el offset del eje 2 en una trabe no mueve la planta, en X" in prs
          and "columna con el punto 1: media seccion en X" in prs
          and "el punto 8 baja el centro medio peralte" in prs)

    # ------------------------------------------------------------------
    # ETAPA 1 DEL PORT DE LA MACRO DE PLANOS ESTRUCTURALES: LA HOJA CONFIG
    # ------------------------------------------------------------------
    # La macro guarda sus ~260 parametros en la hoja CONFIG, que ella misma crea con
    # CrearHojaConfig. Aqui esa hoja es una tabla en el codigo, renglon por renglon y con
    # su descripcion, y de ella cuelga TODO lo que se dibuje despues: capas, colores,
    # estilos de texto, patrones de hatch, separaciones, cotas y ejes.
    cfgp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/ConfigPlano.cs"))
    capp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CapasPlano.cs"))

    # 266 y no los 261 de CrearHojaConfig: se añadieron CINCO renglones que NO estan en su
    # hoja, y todos porque se pidieron: el juego encima de lo ya dibujado, los rotulos al
    # frente, la capa de las dalas llamada E-CADENA, el respaldo del orden de dibujo por
    # comando y el ajuste de las lineas al pano del castillo.
    check("la hoja CONFIG de la macro esta portada, con sesenta y siete renglones añadidos",
          cfgp.count("        P(") == 327
          and 'P("AIRE_SOBRE_LO_DIBUJADO_M", "5",' in cfgp
          and 'P("CAPAS_TEXTO_AL_FRENTE", "",' in cfgp
          and 'P("CAPA_DALA", "CADENA",' in cfgp
          and 'P("DRAWORDER_POR_COMANDO", "SI",' in cfgp
          and 'P("LINEAS_AL_PANO", "SI",' in cfgp
          and 'P("CAPA_VOLADO", "VOLADO",' in cfgp
          and 'P("APAGAR_CAPA_LOSA", "SI",' in cfgp
          and 'P("LOSA_CONTORNO_FUERA_DE_MUROS", "SI",' in cfgp
          and 'P("VIGAS_CORTAR_EN_CRUCES", "SI",' in cfgp
          and 'P("CIMENTACION_SIN_MUROS_SIN_COLUMNAS", "SI",' in cfgp
          and 'P("CAPAS_AL_FONDO", "LOSA,ARMADO LOSA,VOLADO,LOSACERO,EJES"' in cfgp
          and 'P("VOLADO_POR_NOTA", "SI",' in cfgp
          and 'P("ARMADO_LOSA_BAYONETA", "SI",' in cfgp
          and 'P("ARMADO_LOSA_PARRILLA", "NO",' in cfgp
          and 'P("EJES_UNIR_TOL_CM", "1",' in cfgp
          and 'P("VOLADO_ROTULO_SOLO_ARMADO", "NO",' in cfgp
          and 'P("VOLADO_SIN_DIVISIONES", "SI",' in cfgp)
    check("y con los numeros de version de la macro",
          "public const double VersionConfig = 29;" in cfgp
          and "public const double VersionParche = 50;" in cfgp)

    # La lectura tipada, con las MISMAS reglas: CfgS recorta, CfgT no -y eso importa,
    # porque los espacios de LOSA_TEXTO_2 son los que dejan el hueco del numero-, CfgD
    # acepta la coma decimal y CfgB entiende SI, TRUE, VERDADERO, 1, X y YES.
    check("la lectura tipada respeta las reglas de CfgS / CfgT / CfgD / CfgB",
          "public string Texto(" in cfgp
          and "public string TextoTalCual(" in cfgp
          and "public double Numero(" in cfgp
          and "public bool Bandera(" in cfgp
          and '"SI" or "SÍ" or "TRUE" or "VERDADERO" or "1" or "X" or "YES" => true' in cfgp
          and '"NO" or "FALSE" or "FALSO" or "0" => false' in cfgp)

    # Los renglones que mas se han peleado, con el valor exacto de su macro.
    for par, valor in (("VERSION_CONFIG", "29"), ("VERSION_PARCHE", "50"),
                       ("PREFIJO_CAPAS", "E-"), ("ALTURA_TEXTO", "0.12"),
                       ("OFFSET_Y_INICIAL", "25"),
                       ("SEC_ALTURA", "0.12"), ("CADENA_TEXTO_ALTURA", "0.09"),
                       ("SEPARACION_ENTRE_PLANTAS", "10"),
                       ("LOSA_TEXTO_ALTURA", "0.072"), ("LOSA_HATCH_ESCALA", "0.0475"),
                       ("LOSACERO_HATCH_ESCALA", "0.02"),
                       ("LOSACERO_FRANJA_ANCHO_M", "0.15"),
                       ("COTAS_SEPARACION", "0.75"), ("COTAS_SEPARACION_TOTAL", "1.17"),
                       ("EJES_INICIO_BURBUJA_M", "2"), ("PANO_SOLAPE_CM", "0"),
                       ("PANO_BUSCA_CM", "150"), ("PANO_ALARGAR_MAX_CM", "150"),
                       ("COTA_EXT_LINE_EXT", "0"), ("COTA_EXT_LINE_OFFSET", "0.5"),
                       ("COTA_PRECISION", "3"), ("MALLA_SEP_CM", "15"),
                       ("COLOR_ACERO", "130"), ("COLOR_ARMADO_LOSA", "142"),
                       ("COLOR_CASTILLO", "1"), ("COLOR_DALA", "12")):
        check(f"CONFIG: {par} = {valor}",
              f'P("{par}", "{valor}", ' in cfgp)

    # Y los textos: los estilos, los patrones y las plantillas, sin traducir.
    for par, valor in (("PANO_ALMA_W_MODO", "ALMA"), ("ESTILO_COTA", "COTA_DIM"),
                       ("ESTILO_TEXTO_COTA", "COTA"),
                       ("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES"),
                       ("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS"),
                       ("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS"),
                       ("ROTULO_ESTILO_TEXTO", "HAETTENSCHWEILER"),
                       ("LOSA_HATCH_PATRON", "ANSI37"),
                       ("LOSACERO_HATCH_PATRON", "FLEX"),
                       ("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C"),
                       ("CADENA_SIN_MURO_LINETYPE", "ACAD_ISO02W100"),
                       ("LINETYPE_EJES", "DASHDOT"), ("LINETYPE_TRABE", "PHANTOM2"),
                       ("CAPAS_AL_FRENTE", "CADENA,CADENA DESPLANTE,TRABE,ACERO"),
                       ("CIMENTACION_STORIES", "BASE,CIMENTACION,FOUNDATION"),
                       ("CAPA_CADENA_DESPLANTE", "CADENA DESPLANTE"),
                       ("CAPA_PIERS", "PIERS")):
        check(f"CONFIG: {par} = {valor}",
              f'P("{par}", "{valor}", ' in cfgp)

    # El titulo lleva DOS espacios y el renglon 2 del rotulo de la losa lleva SIETE
    # adelante: son el dato, no un descuido, y recortarlos cambia el dibujo.
    check("el titulo conserva sus dos espacios",
          'P("ROTULO_TITULO", "PLANTA  ESTRUCTURAL", ' in cfgp)
    check("y el rotulo de la losa sus espacios de adelante",
          'P("LOSA_TEXTO_2", "       cm de espesor", ' in cfgp)

    # ------------------------------------------------------------------
    # LAS CAPAS DEL PLANO, CON LOS COLORES DE LA MACRO
    # ------------------------------------------------------------------
    # «NO MODIFIQUES NINGUNA CAPA NI NINGUN COLOR»: los que la macro lleva escritos en el
    # codigo van aqui con ese numero, y los que salen de la hoja se leen de la hoja.
    for capa, color in (("MURO", "6"), ("COLUMNA", "1"), ("CONTRATRABE", "2"),
                        ("LOSA", "8"), ("DIAGONAL", "30"), ("OTROS", "7")):
        check(f"capa E-{capa} color {color}",
              f'PorTipo("{capa}", {color})' in capp)

    check("la trabe va en color 3 con PHANTOM2",
          'PorTipo("TRABE", 3, cfg.Texto("LINETYPE_TRABE", "PHANTOM2"))' in capp)
    check("y el castillo, la dala y el acero toman su color de la hoja",
          'PorTipo("CASTILLO", Color("COLOR_CASTILLO", 1))' in capp
          and 'Capa("DALA", Prefijo + _cfg.Texto("CAPA_DALA", "CADENA")' in capp
          and 'Color("COLOR_ACERO", 130)' in capp)
    check("las capas de servicio, igual que en CrearCapas",
          'Servicio("TEXTO", 7)' in capp
          and 'Servicio("TITULO", Color("COLOR_TITULO", 7, minimo: 0))' in capp
          and 'Servicio("EJES", Color("COLOR_EJES", 8), cfg.Texto("LINETYPE_EJES", "DASHDOT"))' in capp
          and 'Servicio("EJES-BURBUJA", Color("COLOR_BURBUJA_EJES", 4))' in capp
          and 'Servicio("EJES-TEXTO", Color("COLOR_EJES_TEXTO", 6))' in capp
          and 'Servicio("ARMADO LOSA", Color("COLOR_ARMADO_LOSA", 142))' in capp
          and 'Servicio("MAMPOSTERIA", Color("COLOR_MAMPOSTERIA", 30))' in capp
          and 'Servicio("LOSACERO", Color("COLOR_LOSACERO", 6))' in capp
          and 'Servicio("COTAS", Color("COLOR_COTAS", 8))' in capp)
    check("la de los piers es la unica SIN prefijo, como en la macro",
          "public string CapaPiers" in capp
          and 'new Capa(string.Empty, CapaPiers, Color("COLOR_PIERS", 7), string.Empty)' in capp)
    check("un color fuera de rango se regresa al de la macro, no a blanco",
          "return c < minimo || c > 255 ? omision : c;" in capp)
    check("y estan CapaDeTipo, CapasAlFrente y el reconocimiento de lo generado",
          "public string CapaDeTipo(" in capp
          and "public IReadOnlyList<string> CapasAlFrente()" in capp
          and "public bool EsCapaGenerada(" in capp)

    # La prueba EJECUTABLE de las dos piezas: se corre el C# compilado, no un port.
    pr = leer(ruta("tools/prueba-config-plano/Program.cs"))
    check("hay prueba ejecutable de la hoja CONFIG y de las capas",
          "using CadLink.Cad.PlanoEstructural;" in pr
          and "327, ConfigPlano.PorOmision.Count" in pr
          and 'Igual("son las 23 capas", 23, capas.Todas.Count)' in pr
          and "return fallos == 0 ? 0 : 1;" in pr)
    check("y su proyecto apunta al CadLink.Cad de verdad",
          "CadLink.Cad.csproj" in leer(ruta("tools/prueba-config-plano/Prueba.csproj")))

    # Y el modelo se VISUALIZA: es lo que pidio el usuario, no solo leerlo.
    m_lm = re.search(r"private void LeerModeloCsi\(.*?\n    \}", codigo, re.S)
    check("se puede leer LeerModeloCsi", m_lm is not None)
    if m_lm:
        cuerpo = m_lm.group(0)
        check("el modelo leido se manda al visor",
              "_vista.Modelo = modelo;" in cuerpo)
        check("y se redibujan las vistas",
              "RedibujarVistas();" in cuerpo)
        check("y se poblan los niveles para la planta",
              "PoblarNiveles(modelo);" in cuerpo)

    # ------------------------------------------------------------------
    # Modelo 3D en AutoCAD
    # ------------------------------------------------------------------
    m3d = leer(ruta("client/src/CadLink.Cad/Modelo3dDrawer.cs"))

    check("hay dibujante del modelo 3D", "public sealed class Modelo3dDrawer" in m3d)
    check("y boton para dibujarlo", 'x:Name="Modelo3dButton"' in xaml)
    check("y su manejador existe", "private void OnDibujar3dCad(" in codigo)

    # SOLIDOS y no cajas ni lineas: un solido se puede seccionar y acotar en AutoCAD.
    # ------------------------------------------------------------------
    # Las tres llamadas COM que cerraban AutoCAD
    # ------------------------------------------------------------------
    # Ninguna de las tres la ve el compilador: todo va por dynamic. Y una de ellas no
    # lanzaba excepcion, se llevaba AutoCAD por delante, asi que no habia forma de
    # capturarla. Se fijan aqui por texto porque es el unico sitio donde se pueden fijar.

    # 1) AddExtrudedSolid esta en el ESPACIO MODELO, no en la region.
    check("la extrusion se pide al espacio modelo, no a la region",
          "_ms.AddExtrudedSolid(region, largo, 0d)" in m3d)
    check("y ya no se llama sobre la region, que no existe en la API",
          "region.AddExtrudedSolid(" not in m3d)

    # 2) EL CIERRE DE AUTOCAD: AddExtrudedSolid CONSUME la region, asi que borrarla
    #    despues es llamar Delete() sobre un objeto COM ya destruido. Eso no lanza: mata
    #    el proceso.
    # Solo puede quedar UN Borrar(region): el del catch, que si es correcto, porque si la
    # extrusion FALLA la region no se consumio y hay que limpiarla. Lo que no puede haber
    # es uno en el camino de exito.
    check("solo se borra la region si la extrusion FALLO",
          m3d.count("Borrar(region);") == 1,
          f"{m3d.count('Borrar(region);')} Borrar(region), deberia haber 1")

    check("y queda escrito por que no se borra al salir bien",
          "AddExtrudedSolid CONSUME el perfil" in m3d)

    # 3) TransformBy quiere una matriz 4x4 de verdad, no un arreglo plano de 16.
    check("la matriz de colocacion es 4x4, no un arreglo plano",
          "private static double[,] Matriz(" in m3d)
    check("y se construye por filas", "return new[,]" in m3d)

    # Y el contorno va como polilinea LIGERA: AddRegion exige una curva cerrada y PLANA,
    # y una ligera lo es por construccion porque solo tiene X e Y.
    check("el contorno del perfil es una polilinea ligera y plana",
          "_ms.AddLightWeightPolyline(pts)" in m3d)
    check("y ya no una polilinea 3D, que AddRegion no acepta bien",
          "Add3DPoly(" not in m3d)

    check("y el perfil sale de la region de su contorno",
          "_ms.AddRegion(" in m3d)

    # La colocacion va en UNA matriz, no en giros sucesivos: una diagonal no esta en
    # ningun plano comodo y encadenar rotaciones acumula error.
    check("la barra se coloca con una matriz, no con giros sucesivos",
          "solido.TransformBy(Matriz(b.P1, b.P2, largo));" in m3d)

    m_mat = re.search(r"private static double\[,\] Matriz\(.*?\n    \}", m3d, re.S)
    check("se puede leer Matriz", m_mat is not None)
    if m_mat:
        cuerpo = m_mat.group(0)
        # u = Z x w, para que v quede lo mas vertical posible: es lo que hace que una
        # viga salga con el alma de pie y no tumbada al azar.
        check("el marco se apoya en la perpendicular comun con la vertical",
              "var u = new[] { -w[1], w[0], 0d };" in cuerpo)

        # Y el caso de la COLUMNA, que no es raro: son todas las columnas del modelo.
        check("la barra vertical se resuelve aparte, que si no el marco se anula",
              "if (n < 1e-9)" in cuerpo)
        check("y se distingue si va hacia arriba o hacia abajo",
              "w[2] > 0 ? 1d : -1d" in cuerpo)

    # Si una barra no se puede extruir NO se pierde: se dibuja su eje y se dice.
    check("una barra que no se puede extruir se dibuja como eje",
          "if (Eje(b))" in m3d)
    check("y se cuenta en el resumen",
          "solo como eje" in m3d)

    # Las areas no son barras con perfil: se dicen en vez de dibujarlas mal.
    m_o3 = re.search(r"private void OnDibujar3dCad\(.*?\n    \}", codigo, re.S)
    if m_o3:
        cuerpo = m_o3.group(0)
        check("las areas se cuentan aparte y no se extruyen",
              'string.Equals(el.Forma, "AREA"' in cuerpo)
        check("el 3D usa el MISMO contorno que la vista extruida",
              "Perfil2D.De(" in cuerpo)
        check("y avisa si no hay modelo leido",
              "no hay nada que dibujar" in cuerpo)

    check("hay comprobacion numerica del marco de colocacion",
          "marco ortonormal" in leer(ruta("tools/verificar_modelo3d.py")))

    check("hay pestaña de planos estructurales",
          'Header="Dibujar planos estructurales"' in xaml)
    # La planta se MUEVE ahi; el 3D y la extruida se quedan en ETABS.
    check("la planta ya no esta en el visor de ETABS",
          'Header="Vista en planta"' not in xaml)
    check("el 3D sigue en la pestaña de ETABS", 'Header="Vista 3D"' in xaml)
    check("y la extruida tambien", 'Header="Vista extruida"' in xaml)
    check("el lienzo de la planta vive en el modulo nuevo",
          xaml.index('Header="Dibujar planos estructurales"')
          < xaml.index('x:Name="PlantaCanvas"'))

    # La planta tiene sus PROPIAS casillas: son dos pestañas distintas.
    check("la planta tiene sus propios filtros",
          'x:Name="VerColumnasPlanoChk"' in xaml and "OnFiltroPlanoCambiado" in codigo)

    m_pl = re.search(r"private void DibujarPlanta\(\).*?\n    \}", codigo, re.S)
    check("se puede leer DibujarPlanta", m_pl is not None)

    if m_pl:
        # Es el MISMO objeto de vista, asi que hay que devolver los filtros como
        # estaban o tocar una casilla aqui cambiaria en silencio el visor 3D.
        check("los filtros del visor se restauran",
              "RestaurarFiltrosDelVisor()" in m_pl.group(0))

    # ------------------------------------------------------------------
    # 1a. Vista previa: gancho sismico, tiempo real y alzado mas largo
    # ------------------------------------------------------------------
    m_prev = re.search(
        r"private void DibujarAlzadoPrevio\(.*?\n    \}", codigo, re.S)
    check("se puede leer DibujarAlzadoPrevio", m_prev is not None)

    if m_prev:
        cuerpo = m_prev.group(0)

        # El GANCHO. Era lo unico que el usuario no veia en la vista previa,
        # justamente el valor que esta ajustando en la casilla.
        check("la vista previa dibuja el gancho",
              "Estribos.GanchoEfectivo(" in cuerpo)
        # Hacia DENTRO de la pieza: el del lecho superior baja y el del inferior
        # sube. Al reves saldria del concreto.
        check("el gancho va hacia dentro de la pieza",
              "dobleHaciaAbajo: true" in cuerpo and "dobleHaciaAbajo: false" in cuerpo)
        # Los DOS extremos de cada varilla llevan gancho.
        check("el gancho va en los dos extremos",
              "new[] { xIni, xFin }" in cuerpo)
        # Y se dice en el rotulo, para que el numero se vea aunque el gancho salga
        # demasiado corto para dibujarse.
        check("el rotulo dice el gancho", "gancho {a.GanchoCm" in cuerpo)

        # El alzado se estira a lo LARGO: manda el ancho.
        m_esc = re.search(r"var esc = Math\.Min\(anchoDisp / largo, \(alto \* ([\d.]+)\)", cuerpo)
        check("se puede leer la escala de la vista previa", m_esc is not None)
        if m_esc:
            check("el alzado se estira a lo largo (el alto ya no lo aprieta)",
                  float(m_esc.group(1)) >= 0.85,
                  f"el tope de alto es {m_esc.group(1)}, antes 0.55")

    # TIEMPO REAL: la coleccion solo avisa al agregar o quitar filas, no al editar
    # una celda. Sin escuchar PropertyChanged, cambiar el gancho no movia la vista.
    check("la vista previa se actualiza al editar una celda",
          "private void OnFilaEditada(" in codigo)
    n_sub = len(re.findall(r"fila\.PropertyChanged \+= OnFilaEditada", codigo))
    check("cada fila se suscribe, las de arranque y las nuevas", n_sub == 2,
          f"hay {n_sub} suscripciones, se esperaban 2: la inicial y la de la coleccion")
    # Y se DESsuscribe: si no, una fila borrada sigue avisando y redibujando.
    check("y se desuscribe al quitarla",
          "fila.PropertyChanged -= OnFilaEditada" in codigo)

    m_ed = re.search(r"private void OnFilaEditada\(.*?\n    \}", codigo, re.S)
    check("se puede leer OnFilaEditada", m_ed is not None)
    if m_ed:
        # Solo se redibuja la fila que se esta viendo: con cien secciones,
        # redibujar por cada tecla de cualquier fila hace la edicion pesada.
        check("solo redibuja la seccion que se esta viendo",
              "ReferenceEquals(sender, Seleccionada)" in m_ed.group(0))

    # La comprobacion numerica contra el VBA del usuario.
    check("hay comparacion del reparto contra el VBA original",
          os.path.exists(ruta("tools/verificar_estribos_vba.py")))

    # ------------------------------------------------------------------
    # 1b. El diamante no atraviesa las varillas laterales
    # ------------------------------------------------------------------
    # Defecto reportado: la diagonal del diamante cortaba la varilla lateral por la
    # mitad. Esto NO esta en la macro: ahi la cinta va del doblez lateral a la
    # varilla central en linea recta y le pasa por encima. En obra el estribo no
    # atraviesa el acero, lo rodea.
    drawer_ = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("las varillas laterales se anotan", "_varLat.Add((xIzq, y, d / 2))" in drawer_)
    check("y se limpian por seccion", "_varLat.Clear();" in drawer_)

    # La correccion de fondo: el doblez lateral ES la varilla, no un circulo
    # ficticio puesto a su lado.
    # Y la geometria del rombo vive en TrazoDiamante, no en el dibujante: la vista previa
    # de la pestaña de concreto usa la MISMA, que es la unica manera de que las dos no
    # puedan discrepar. Asi que se comprueba ahi.
    trazo_diam = leer(ruta("client/src/CadLink.Cad/TrazoDiamante.cs"))

    check("el doblez lateral puede ser la varilla",
          "private static List<(double X, double Y, double R)> DoblezLateral(" in trazo_diam)
    n_dob = len(re.findall(r"DoblezLateral\(true, cx|DoblezLateral\(false, cx", trazo_diam))
    check("los DOS dobleces usan la varilla", n_dob == 2, f"solo {n_dob}")

    m_dob = re.search(
        r"private static List<\(double X, double Y, double R\)> DoblezLateral\(.*?\n    \}",
        trazo_diam, re.S)
    check("se puede leer DoblezLateral", m_dob is not None)

    if m_dob:
        cuerpo = m_dob.group(0)
        # Solo las varillas de ESE costado: mezclarlas haria que el doblez de la
        # derecha se fuera a abrazar una varilla de la izquierda.
        check("cada doblez mira solo su costado",
              "derecha ? v.X > cx : v.X < cx" in cuerpo)
        # La mas cercana a media altura es la que marca el vertice del rombo.
        check("toma la mas cercana a media altura, o las DOS si el eje cae entre dos",
              "VarillasDelCentro(" in cuerpo and "porY: true" in cuerpo)
        # El recorrido tiene que seguir siendo antihorario: por la derecha de abajo
        # hacia arriba y por la izquierda al contrario.
        check("las dos varillas se ordenan segun el costado",
              "derecha\n            ? seleccion.OrderBy(v => v.Y)" in cuerpo)
        # Sin varillas laterales tiene que seguir funcionando como la macro.
        check("sin varillas laterales usa el circulo ficticio", "ficticio" in cuerpo)

    # La red de seguridad: el doblez solo puede abrazar UNA varilla por costado, y
    # un armado con varias puede tener otra en el camino.
    check("existe la red de seguridad",
          "private static List<(double X, double Y, double R)> RodearLaterales("
          in trazo_diam)
    check("y se llama",
          "return RodearLaterales(centros, dDia, varLat, notas);" in trazo_diam)

    m_rod = re.search(
        r"private static List<\(double X, double Y, double R\)> RodearLaterales\(.*?\n    \}",
        trazo_diam, re.S)
    check("se puede leer RodearLaterales", m_rod is not None)

    if m_rod:
        cuerpo = m_rod.group(0)
        # Varias pasadas: rodear una varilla empuja la cinta y puede cruzar otra.
        check("da varias pasadas", "PasadasRodeo" in cuerpo)
        # Se mira contra las DOS fronteras de la cinta.
        check("mira las dos fronteras de la cinta",
              "Cinta(actual, 0)" in cuerpo and "Cinta(actual, dDia)" in cuerpo)
        # Se inserta en el tramo que atraviesa y en el orden del recorrido, o la
        # cinta sale hecha un nudo.
        check("inserta en el orden del recorrido",
              "candidatas.OrderBy(c => c.T)" in cuerpo)
        # Si la cinta no se puede construir, se vuelve al recorrido de partida:
        # mejor un diamante que cruza una varilla que ningun diamante.
        check("si la cinta no sale, se vuelve atras",
              "return centros;" in cuerpo)

    # La distancia va al SEGMENTO, no a la recta: una varilla mas alla del extremo
    # del tramo no esta atravesada.
    m_dist = re.search(
        r"private static double DistanciaASegmento\(.*?\n    \}", trazo_diam, re.S)
    check("se puede leer DistanciaASegmento", m_dist is not None)
    if m_dist:
        # Se acota al metodo: 'Math.Clamp' aparece en otros sitios del archivo y
        # buscarlo en todo el texto dejaba pasar que se quitara justo de aqui.
        check("la distancia se mide al SEGMENTO, no a la recta",
              "Math.Clamp(" in m_dist.group(0))

    check("hay comprobacion numerica del rodeo",
          "rodea las varillas laterales" in
          leer(ruta("tools/verificar_recorte_diamante.py")))

    # ------------------------------------------------------------------
    # 2. Vista extruida
    # ------------------------------------------------------------------
    check("existe la vista extruida", "public void DibujarExtruido(" in ext)
    check("hay pestaña de vista extruida", 'x:Name="ExtruidaCanvas"' in xaml)
    check("el lienzo extruido recorta lo que se sale",
          re.search(r'x:Name="ExtruidaCanvas"[^>]*?ClipToBounds="True"', xaml, re.S)
          is not None)
    check("la vista extruida se redibuja al cambiar de tamaño",
          "ExtruidaCanvas.SizeChanged" in codigo)
    check("se redibuja con los filtros y los presets",
          "_vista.DibujarExtruido(ExtruidaCanvas);" in codigo)
    m_gira = re.search(r"_girando = e\.ChangedButton.*?;", codigo, re.S)
    check("se puede leer la condicion de giro", m_gira is not None)
    # EL GIRO SE DEFINE POR EXCLUSION desde que la planta usa el boton izquierdo para
    # MOVER: gira todo lo que NO es la planta, y la extruida entra ahi. Antes la condicion
    # nombraba los dos lienzos de volumen; ahora nombra al que se queda fuera.
    check("se puede girar la vista extruida",
          m_gira is not None
          and "!esPlanta" in m_gira.group(0)
          and "var esPlanta = ReferenceEquals(lienzo, PlantaCanvas);" in codigo)

    # ------------------------------------------------------------------
    # LA PESTAÑA DE ETABS/SAP2000: MURO CON ESPESOR, TERNA Y CORTES
    # ------------------------------------------------------------------
    #  EL MURO CON ESPESOR. Cuando GetWall no da el espesor -pasa con las propiedades de
    #  mamposteria- el muro se dibujaba PLANO, como una hoja de papel, y en una vista extruida
    #  eso es justo lo que no se quiere ver. El plano de AutoCAD si lo dibuja con espesor,
    #  porque alla hay un respaldo de 15 cm: aqui se usa EL MISMO, o una de las dos vistas
    #  estaria mintiendo.
    check("el muro de la vista extruida siempre tiene espesor",
          "private static double EspesorDePanel(" in ext
          and "var t = EspesorDePanel(el);" in ext
          and "el.Clase == ClaseElemento.Muro ? 0.15 : 0.10;" in ext)
    # LA LOSA NO: su espesor manda en el armado y en el rotulo, asi que inventarlo seria peor
    # que dejarla plana. Que se note.
    # Y LA LOSA, CON SU ESPESOR REAL: se pidio. El del modelo siempre que este -y ahora llega
    # tambien en las nervadas y las reticulares, que no responden a GetSlab y por eso venian en
    # cero- y, si de verdad no esta, 10 cm: una losa PLANA en una vista extruida se lee como si
    # no tuviera espesor, que es imposible. Y se AVISA de cual salio asi.
    check("la losa de la extruida lleva su espesor real",
          "return el.Clase == ClaseElemento.Muro ? 0.15 : 0.10;" in ext)
    check("el espesor se busca tambien en las losas nervadas y reticulares",
          '"GetSlabRibbed", "GetSlabWaffle"' in lec_ins
          and "var total = Convert.ToDouble(a[1]);" in lec_ins)
    check("y se avisa de la propiedad que no dio su espesor, una vez por seccion",
          "no dio su espesor" in lec_ins
          and "sinEspesor.Add(seccion)" in lec_ins)

    #  LA TERNA XYZ. Estaba dibujada, pero en el mismo gris claro de todo y con linea de 1.2
    #  px: sobre el fondo claro era invisible, y en una vista que se gira, no saber para donde
    #  cae la X deja al modelo sin referencia.
    # LA PLACA DE LA TERNA, TRASLUCIDA Y SIN CONTORNO: se pidio. Opaca tapaba la esquina del
    # modelo -que es donde uno mira para orientarse- y el circulo dibujado competia con los
    # ejes, haciendo parecer que la terna era un objeto del modelo.
    check("la placa de la terna es traslucida y sin contorno",
          "Pincel(0xFF, 0xFF, 0xFF, 0x3C)" in vista
          and "Fill = FondoTerna\n        };" in vista.replace("\r\n", "\n"))

    check("la terna XYZ se ve: colores, flecha y placa",
          "private static readonly Brush ColorEjeX" in vista
          and "private static readonly Brush ColorEjeZ" in vista
          and "private static readonly Brush FondoTerna" in vista
          and "LA PUNTA DE FLECHA" in vista
          and 'Eje(1, 0, 0, "X", ColorEjeX);' in vista)
    check("y usa la MISMA proyeccion que el modelo, no otra formula",
          "var u = (x * ca) - (y * sa);" in vista
          and "var v = -((z * ce) + (d * se));" in vista)

    #  EL CORTE POR UN EJE, que es un ALZADO: se ve solo lo que hay sobre ese eje. Se resuelve
    #  como una REBANADA, no como un plano de espesor cero, porque en un modelo real los muros
    #  de un eje no estan todos exactamente en su ordenada -el eje pasa por el paño y el muro
    #  se modela en su linea media- y un corte de espesor cero se quedaria vacio.
    check("hay corte por un eje en las vistas de volumen",
          "public string CorteEje { get; set; } = string.Empty;" in vista
          and "public bool CorteEnX { get; set; }" in vista
          and "public double CorteEspesorM { get; set; } = 0.6;" in vista
          and "private bool EnElCorte(" in vista
          and "public void SinCorte()" in vista)
    # SE MIRA EL ELEMENTO COMPLETO, no su centro: una trabe que cruza el eje entra aunque su
    # centro este a diez metros. Filtrando por el centro desaparecerian justo las trabes que
    # llegan al eje del corte, que son las que se quieren ver.
    check("el corte mira el elemento completo, no su centro",
          "if (max >= CorteOrdenada - medio && min <= CorteOrdenada + medio)" in vista)
    # Y ADEMAS SE VE EL FONDO DEL LADO QUE SE ELIGE, que es lo que hace que el visor sirva para
    # decidir: se cambia de lado en la lista y se ve al momento si por ahi hay algo.
    check("y el fondo del lado que se mira",
          "return CorteHaciaMas\n            ? min > CorteOrdenada + medio\n"
          "            : max < CorteOrdenada - medio;" in vista)
    # Y SOLO EN LAS VISTAS DE VOLUMEN: la planta YA es un corte horizontal.
    check("el corte no se aplica a la planta",
          "private List<ElementoEtabs> Elementos(bool conCorte = false)" in vista
          and "var elementos = Elementos(conCorte: true);" in vista
          and "var elementos = Elementos(conCorte: true);" in ext)
    # EL CORTE SE DICE EN LA LEYENDA: deja fuera media estructura, asi que tiene que estar
    # escrito o se mira un modelo incompleto creyendo que esta entero.
    check("la leyenda dice por que eje va el corte",
          "corte por el eje {CorteEje}" in vista)
    # Y LA VISTA SE PONE DE FRENTE AL CORTE: un corte visto en isometrica sigue siendo un
    # dibujo torcido.
    check("al elegir el corte la vista se pone de frente",
          "private void OnCorteEjeCambiado(" in codigo
          and "_vista.Azimut = corte.EnX ? 90 : 0;" in codigo
          and "private void PoblarCortes(" in codigo
          and "PoblarCortes(modelo);" in codigo)
    check("la lista de cortes sale de los ejes del modelo, sin repetidos",
          "modelo.Ejes ?? EjesModelo.DesdeGeometria(modelo)" in codigo
          and "CadLink.Cad.PlanoEstructural.EjesPlano.SinRepetidos(" in codigo
          and 'x:Name="CorteEjeCombo"' in xaml
          and 'SelectionChanged="OnCorteEjeCambiado"' in xaml)
    # ENCUADRAR: la salida cuando uno se pierde arrastrando. No toca el giro a proposito.
    check("hay boton de Encuadrar que no cambia el punto de vista",
          'Tag="ENCUADRAR"' in xaml
          and 'case "ENCUADRAR":' in codigo)

    # La camara es UNA, compartida. Duplicar la proyeccion acaba con una vista
    # espejeada respecto a la otra.
    check("la camara esta extraida y compartida",
          "private sealed class Camara" in vista and
          "PrepararCamara(lienzo, elementos) is not Camara cam" in vista)
    n_cam = len(re.findall(r"PrepararCamara\(lienzo, elementos\) is not Camara cam\b",
                           vista + ext))
    check("las dos vistas de volumen usan la misma camara", n_cam == 2,
          f"la usan {n_cam}, se esperaban 2")

    # Volumen de verdad: prismas para las barras y paneles con espesor.
    # La LLAMADA, no la declaracion: renombrar el metodo dejaba pasar el check
    # porque el nombre seguia apareciendo en el archivo. Lo delato una mutacion.
    check("las barras se extruyen como prisma", "CarasDePanel(el) : CarasDeBarra(el)" in ext)
    check("los muros y losas se extruyen su espesor",
          "Escalar(Normalizar(normal), t / 2)" in ext and "cara1[i], cara1[j], cara2[j], cara2[i]" in ext)
    check("hay sombreado por cara", "private static double Brillo(" in ext)

    m_barra = re.search(
        r"private static IEnumerable<List<\(double X, double Y, double Z\)>> CarasDeBarra\("
        r".*?\n    \}", ext, re.S)
    check("se puede leer CarasDeBarra", m_barra is not None)

    if m_barra:
        cuerpo = m_barra.group(0)
        # El triedro degenera si la barra ya es vertical: sin este caso, TODAS las
        # columnas se dibujarian aplastadas.
        check("el triedro no degenera en las columnas",
              "Math.Abs(eje.Item3) > 0.99" in cuerpo)
        # 6 caras: 2 tapas y 4 costados.
        check("el prisma tiene sus seis caras",
              cuerpo.count("yield return") >= 3 and "for (var i = 0; i < 4; i++)" in cuerpo)

    # SE PINTA POR CARA, y ahora ni siquiera por orden: con Z-BUFFER, pixel a pixel. El
    # orden por caras -aunque fuera por cara y no por elemento- no puede resolver dos caras
    # que se ATRAVIESAN, y ese era el caso de la losa que se veia cortada por el muro: cada
    # una esta delante en una parte, asi que no hay orden correcto que elegir.
    check("la extruida pinta cara por cara, con Z-buffer",
          "foreach (var cara in caras)" in ext
          and "lienzoZ.Triangulo(" in ext
          and "caras.OrderByDescending(c => c.Profundidad)" not in ext)
    # La normal por Newell, no por los tres primeros vertices (pueden ser casi
    # colineales en una losa con vertice intermedio).
    check("la normal se calcula por Newell", "NormalDe(" in ext and "a.Y - b.Y" in ext)

    # ------------------------------------------------------------------
    # 3. Piers de muros
    # ------------------------------------------------------------------
    piers = leer(ruta("client/src/CadLink.Etabs/EtabsPiers.cs"))

    check("hay boton de leer piers", 'Click="OnLeerPiers"' in xaml)
    check("el boton dice lo que hace", "Leer piers de muros" in xaml)
    check("existe el manejador", "private void OnLeerPiers(" in codigo)
    check("existe el lector de piers", "public static PiersLeidos Leer(" in piers)

    # Las DOS vias. GetSectionProperties cambio de firma entre versiones, asi que
    # si falla hay que reconstruir los piers desde los paños.
    check("via principal: PierLabel", 'Com.TryGet(cx.SapModel, "PierLabel")' in piers)
    check("respaldo: los paños de muro", '"GetPier"' in piers)
    check("el respaldo se recorre SIEMPRE, no solo al fallar",
          "DesdeLosPanos(cx, r);" in piers and
          piers.index("DesdeLosPanos(cx, r);") > piers.index("LeerPropiedades(pierLabel, r)"))

    # Si no hay piers, hay que DECIR como se asignan en ETABS, no dejar la tabla
    # vacia sin explicacion.
    check("se explica que hacer si no hay piers", "Assign > Shell > Pier Label" in piers)
    check("el detalle por miembro se adjunta",
          "Detalle por miembro" in piers and "Com.Bitacora" in piers)

    # La tabla aparece solo si hay algo: una tabla vacia encima de la de
    # elementos solo estorba.
    check("la tabla de piers se oculta si esta vacia",
          'x:Name="PiersGrid"' in xaml and "PiersGrid.Visibility" in codigo)


# ======================================================================
# 18. Dibujar la planta en AutoCAD
# ======================================================================
def v18_planta_autocad() -> None:
    """El boton «Dibujar en AutoCAD» de la pestaña de planos, y su dibujante."""
    print("\n[18] Dibujar la planta en AutoCAD")

    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))

    # ------------------------------------------------------------------
    # El boton, y donde vive
    # ------------------------------------------------------------------
    check("hay boton «Dibujar en AutoCAD»", 'Content="Dibujar en AutoCAD"' in xaml)
    check("y esta cableado", 'Click="OnDibujarPlantaCad"' in xaml)

    # Tiene que estar DENTRO de la pestaña de planos, no en otra: se dibuja el nivel
    # y los filtros que el usuario esta viendo, asi que el boton va donde se eligen.
    i_tab = xaml.find('Header="Dibujar planos estructurales"')
    i_lic = xaml.find('Header="Licencia"')
    i_btn = xaml.find('x:Name="PlantaCadButton"')

    check("el boton esta en la pestaña de planos estructurales",
          i_tab >= 0 and i_btn > i_tab, f"pestaña en {i_tab}, boton en {i_btn}")
    check("y no se colo en la pestaña de la licencia",
          not (0 <= i_lic < i_btn < i_tab))

    # ------------------------------------------------------------------
    # Se dibuja LO QUE SE VE
    # ------------------------------------------------------------------
    m = re.search(r"private PlantaCad ArmarPlanta\(.*?\n    \}", codigo, re.S)
    check("se puede leer ArmarPlanta", m is not None)
    if m:
        cuerpo = m.group(0)
        check("filtra por el nivel elegido en la lista", "NivelElegido" in cuerpo)
        check("y por los filtros de ESA pestaña", "VisibleEnElPlano" in cuerpo)

    # LAS DOS SOBRECARGAS: la que recibe el ELEMENTO -que manda el castillo de area a la casilla de
    # las columnas y la cadena de area a la de las trabes- y la que recibe la CLASE, con su switch.
    m_vis = re.search(
        r"private bool VisibleEnElPlano\(ElementoEtabs el\).*?_ => false\s*\n    \};",
        codigo, re.S)
    check("se puede leer VisibleEnElPlano", m_vis is not None)
    if m_vis:
        cuerpo = m_vis.group(0)
        # Las casillas del PLANO, no las del visor 3D: son dos juegos distintos y
        # confundirlos haria que el plano saliera con lo que se ve en otra pestaña.
        for caja in ("VerColumnasPlanoChk", "VerTrabesPlanoChk",
                     "VerMurosPlanoChk", "VerLosasPlanoChk"):
            check(f"usa la casilla {caja}", caja in cuerpo)

        for caja in ("VerColumnasChk", "VerTrabesChk", "VerMurosChk", "VerLosasChk"):
            check(f"y NO la del visor 3D ({caja})",
                  not re.search(rf"\b{caja}\b", cuerpo))

    # ------------------------------------------------------------------
    # El permiso es el mismo que el de las secciones
    # ------------------------------------------------------------------
    m_mod = re.search(r"private void AplicarModulos\(\).*?\n    \}", codigo, re.S)
    check("se puede leer AplicarModulos", m_mod is not None)
    if m_mod:
        check("el boton de la planta se apaga sin licencia de dibujo",
              "PlantaCadButton.IsEnabled = puedeDibujar;" in m_mod.group(0))

    m_h = re.search(r"private void OnDibujarPlantaCad\(.*?\n    \}", codigo, re.S)
    check("se puede leer OnDibujarPlantaCad", m_h is not None)
    if m_h:
        cuerpo = m_h.group(0)
        # Apagar un boton no es la medida de seguridad: la comprobacion de verdad va
        # tambien en el codigo que ejecuta la funcion.
        check("y el permiso se vuelve a comprobar al ejecutar",
              'HasFeature("export-dxf")' in cuerpo)
        check("no se dibuja sin modelo leido", "_modeloEtabs is null" in cuerpo)
        check("se avisa si no queda nada que dibujar",
              "plantas.Sum(p => p.Elementos.Count) == 0" in cuerpo)
        # DE UN JALON TODAS LAS PLANTAS, que es como lo hace la macro. La casilla es para
        # cuando se quiere revisar una sola.
        check("se dibujan TODAS las plantas, no solo la del nivel elegido",
              "ArmarTodasLasPlantas(_modeloEtabs)" in cuerpo
              and "dibujante.DibujarTodas(plantas)" in cuerpo)
        check("y hay casilla para dibujar solo una",
              'x:Name="SoloNivelElegidoChk"' in xaml
              and "SoloNivelElegidoChk?.IsChecked == true" in cuerpo)
        check("se tolera que AutoCAD no este abierto",
              "AcadNotAvailableException" in cuerpo)
        check("y el cursor de espera se repone siempre",
              "finally" in cuerpo and "Cursor = Cursors.Arrow;" in cuerpo)

    # ------------------------------------------------------------------
    # El dibujante
    # ------------------------------------------------------------------
    dib = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.cs"))
    dto = leer(ruta("client/src/CadLink.Cad/PlantaCad.cs"))
    mac = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Macro.cs"))
    # La prueba ejecutable de la etapa 4. Se lee AQUI, antes del primer uso: dejarla mas
    # abajo ya reventó dos veces con UnboundLocalError.
    pre = leer(ruta("tools/prueba-ejes-plano/Program.cs"))
    # Y las capas y la prueba de la hoja CONFIG. Se vuelven a leer AQUI a proposito: las de
    # mas arriba son locales de otra comprobacion y no llegan hasta aqui.
    capp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CapasPlano.cs"))
    cfgp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/ConfigPlano.cs"))
    pr = leer(ruta("tools/prueba-config-plano/Program.cs"))

    check("existe PlantaDrawer", "class PlantaDrawer" in dib)
    check("existe el DTO PlantaCad", "class PlantaCad" in dto)

    # CadLink.Cad NO conoce ETABS: quien traduce es la ventana, en un solo sitio.
    proj = leer(ruta("client/src/CadLink.Cad/CadLink.Cad.csproj"))
    check("CadLink.Cad no referencia a CadLink.Etabs", "CadLink.Etabs" not in proj)
    # Se miran los USOS, no el texto: los dos archivos EXPLICAN en un comentario que
    # el espejo de ClaseElemento esta duplicado a proposito, y buscar la cadena a
    # pelo daba por incumplida justo la regla que el comentario documenta.
    for f in ("PlantaDrawer.cs", "PlantaCad.cs"):
        limpio = _sin_comentarios(leer(ruta("client/src/CadLink.Cad/" + f)))
        check(f"{f} no usa tipos de ETABS",
              "CadLink.Etabs" not in limpio and "ClaseElemento" not in limpio)
    check("la ventana es la que traduce", "ClasePlantaDe" in codigo)

    # LAS CAPAS SON LAS DE LA MACRO, no unas propias: antes eran PLANTA-COLUMNAS,
    # PLANTA-TRABES... con sus propios colores, y el plano salia en capas que no eran las
    # suyas. Ahora salen de CapasPlano -E-CASTILLO, E-COLUMNA, E-DALA, E-TRABE...- y la de
    # cada elemento se elige por su TIPO, como en su DibujarElemento.
    check("las capas ya no son unas propias",
          all(v not in dib for v in ('"PLANTA-COLUMNAS"', '"PLANTA-TRABES"',
                                     '"PLANTA-MUROS"', '"PLANTA-LOSAS"')))
    check("y salen de la tabla de la macro",
          "PlanoEstructural.CapasPlano _capas" in dib
          and "_capas.CapaDeTipo(tipo)" in dib
          and '_capas.Prefijo + "TEXTO"' in dib
          and '_capas.Prefijo + "TITULO"' in dib)
    prs_cad = leer(ruta("tools/prueba-secciones-modelo/Program.cs"))

    check("un perfil de acero va a la capa del acero",
          "PlanoEstructural.CapasPlano.EsPerfilAcero(el.Forma)" in dib
          and '_capas.CapaDeTipo("ACERO")' in dib)
    # EL TIPO DEL PLANO, CON LAS NOTAS. Aqui se clasificaba SIN ellas, y por eso las cadenas
    # no salian como cadenas: una «CC 15X25» de 25 cm de peralte pasa de los 20 del criterio
    # por medidas y se iba a E-TRABE, aunque en sus notas dijera CADENA DE CERRAMIENTO. La
    # tabla de secciones si las leia; el dibujo, no.
    check("el tipo lo clasifica la ventana con la regla de la macro Y las notas",
          "SeccionesModelo.ClasificaTipo(el.Clase, el.Seccion, t2, t3, null, el.Notas)"
          in codigo
          and "public string Tipo { get; set; }" in dto)
    # Y LAS TRES CADENAS VAN A LAS CAPAS DE LAS CADENAS: CADENA DE CERRAMIENTO no es el
    # nombre de ninguna capa, asi que sin traducirlo se irian a E-OTROS, que es peor que
    # antes: se dibujarian, pero en una capa que nadie mira.
    check("las tres cadenas van a la capa de las cadenas",
          'if (t.StartsWith("CADENA", StringComparison.OrdinalIgnoreCase))' in capp
          and 'CapaCadenaDesplante' in capp
          and 'CapaDeTipo("DALA")' in capp)
    check("hay prueba ejecutable de las tres cadenas",
          "la de CERRAMIENTO sale con su nombre" in prs_cad
          and "la de cerramiento se ordena con las dalas" in prs_cad
          and "«CADENA» a secas es DALA" in prs_cad)

    # El color se PONE, exista la capa o no: es lo que hace AsegurarCapa en la macro, y es
    # lo que permite que el plano se vea igual aunque el dibujo traiga esas capas de otro
    # sitio con otro color.
    m_cap = re.search(r"public void AsegurarCapas\(\).*?\n    \}", dib, re.S)
    check("se puede leer AsegurarCapas", m_cap is not None)
    if m_cap:
        check("se crean las 23 capas de la tabla, con su color",
              "foreach (var capa in _capas.Todas)" in m_cap.group(0)
              and "lay.Color = capa.Color;" in m_cap.group(0))
        check("y con su tipo de linea",
              "AsegurarTipoDeLinea(capa.TipoDeLinea)" in m_cap.group(0))

    # DIBUJAR TODAS LAS PLANTAS DE UN JALON, con la separacion de la hoja CONFIG.
    m_todas = re.search(r"public Resumen DibujarTodas\(.*?\n    \}", dib, re.S)
    check("se puede leer DibujarTodas", m_todas is not None)
    if m_todas:
        cuerpo = m_todas.group(0)
        check("el paso sale de SEPARACION_ENTRE_PLANTAS, que ahora son 10.00",
              '_cfg.Numero("SEPARACION_ENTRE_PLANTAS", 10)' in cuerpo)
        # Y el juego se pone POR ENCIMA de lo que ya este dibujado, no a una altura fija:
        # asi dibujar dos veces no encima las plantas. Con el dibujo vacio, al origen.
        check("el juego se coloca por encima de lo ya dibujado",
              '_cfg.Numero("AIRE_SOBRE_LO_DIBUJADO_M", 5)' in cuerpo
              and "var tope = TopeDeLoDibujado();" in cuerpo)
        # Y CON EL DIBUJO VACIO, A LA Y DE OFFSET_Y_INICIAL -25-, no al origen: el rotulo
        # de la planta va DEBAJO de las burbujas y de las cotas, asi que pegado al origen se
        # salia por abajo, a la zona de los negativos.
        check("y con el dibujo vacio arranca en OFFSET_Y_INICIAL, que son 25",
              'tope is { } t ? t + aire : _cfg.Numero("OFFSET_Y_INICIAL", 25)' in cuerpo)
        check("y se mide lo que hay en el dibujo para saber donde acaba",
              "internal double? TopeDeLoDibujado()" in mac)
        check("y caben PLANTAS_POR_FILA en cada fila",
              '_cfg.Numero("PLANTAS_POR_FILA", 100)' in cuerpo)
        check("el paso es el MISMO para todas, del rectangulo que las envuelve",
              "foreach (var p in plantas)" in cuerpo and "var pasoX = (xMax - xMin) + hueco;" in cuerpo)
    # LA BASE TAMBIEN SE DIBUJA. GetStories NO devuelve el nivel base, pero el modelo si
    # tiene elementos con Story = «Base» -las cadenas de desplante-, asi que los niveles se
    # sacan de los ELEMENTOS, como StoriesDesdeElementos de la macro.
    mod = leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
    check("los niveles que se dibujan salen de los elementos, asi entra la BASE",
          "public List<NivelEtabs> NivelesConElementos(bool ascendente = true)" in mod
          and "e.Clase != ClaseElemento.Losa" in mod
          and "modelo.NivelesConElementos(ascendente: true)" in codigo)
    check("y la lista de la pestaña tambien la trae",
          "modelo.NivelesConElementos(ascendente: false)" in codigo)
    check("las plantas van del nivel mas bajo al mas alto, como ORDEN_NIVELES = ASC",
          "salida.OrderBy(n => n.ElevacionM).ToList()" in mod)

    # ------------------------------------------------------------------
    # ETAPA 4: EJES CON BURBUJAS, COTAS, ESTILOS Y ROTULO DE LA PLANTA
    # ------------------------------------------------------------------
    # Es lo que convierte un dibujo de elementos en un PLANO. La cuenta va aparte del
    # dibujante -EjesPlano y RotuloPlanta, sin COM- para poder comprobarla sin AutoCAD.
    ejp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/EjesPlano.cs"))
    rtp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/RotuloPlanta.cs"))

    check("la cuenta de los ejes y las cotas esta portada",
          "public double SaleEjes()" in ejp
          and "public double SaleEjesCorto()" in ejp
          and "public double AbajoDeEjes(bool hayEjes)" in ejp
          and "public List<Cota> Cotas(" in ejp
          and "public List<EjeColocado> Verticales(" in ejp
          and "public List<EjeColocado> Horizontales(" in ejp)

    # LOS DOS NUMEROS QUE MAS SE HAN PELEADO EN LA MACRO, y que son INDEPENDIENTES:
    # EJES_INICIO_BURBUJA_M manda las burbujas y COTAS_SEPARACION_TOTAL manda la cota.
    check("las burbujas las manda EJES_INICIO_BURBUJA_M, y solo eso",
          '_cfg.Numero("EJES_INICIO_BURBUJA_M", 2)' in ejp
          and "if (inicio > 0)" in ejp)
    check("y las cotas, COTAS_SEPARACION y COTAS_SEPARACION_TOTAL",
          '_cfg.Numero("COTAS_SEPARACION", 0.75)' in ejp
          and '_cfg.Numero("COTAS_SEPARACION_TOTAL", 1.17)' in ejp)
    check("los cuatro lados se prenden por separado",
          all(f'_cfg.Bandera("COTAS_{lado}", true)' in ejp
              for lado in ("ARRIBA", "ABAJO", "IZQUIERDA", "DERECHA")))
    check("la cota total necesita 3 ejes: con 2 seria la misma linea dos veces",
          "ejes.Count >= 3" in ejp)
    check("la burbuja lleva su anillo y sus rayitas, 3 o 4",
          "public double RadioAnillo()" in ejp
          and '_cfg.Bandera("BURBUJA_CRUZ_4_LINEAS", true)' in ejp)

    # EL ROTULO: CIMENTACION en la base y PLANTA BAJA en Story1, no «STORY1».
    check("el rotulo de la planta esta portado",
          "public string RenglonDelNivel(string story)" in rtp
          and "public bool EsCimentacion(string story)" in rtp
          and "public static int NumeroDeStory(string story)" in rtp)
    check("la base se rotula CIMENTACION",
          '_cfg.Texto("ROTULO_NOMBRE_CIMENTACION", "CIMENTACION")' in rtp)
    check("y el nombre del nivel sale de ROTULO_NIVELES",
          '_cfg.Texto("ROTULO_NIVELES")' in rtp)
    check("la comparacion de la base es EXACTA, para que Basement no cuente",
          "if (p.Length > 0 && t == p)" in rtp)

    # EL DIBUJO: estilos, ejes, cotas, rotulo, mamposteria y draw order.
    check("se crean los estilos de la macro",
          "private void AsegurarEstilosDeLaMacro()" in mac
          and '_cfg.Texto("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES")' in mac
          and '_cfg.Texto("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS")' in mac
          and '_cfg.Texto("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS")' in mac
          and '_cfg.Texto("ESTILO_TEXTO_COTA", "COTA")' in mac)
    check("el de las cotas va en NEGRITA, que solo se puede pedir por el nombre de fuente",
          '_cfg.Bandera("COTA_NEGRITA", true)' in mac
          and "est.SetFont(fuente, negrita, false, 0, 0);" in mac)
    check("y el estilo de cota se arma con las variables DIM y CopyFrom",
          "private void EstiloDeCota()" in mac
          and 'V("DIMTXSTY"' in mac and 'V("DIMEXE"' in mac and 'V("DIMDSEP"' in mac
          and "est.CopyFrom(_doc);" in mac)

    check("se dibujan los ejes con sus burbujas",
          "private void DibujarEjesDeLaPlanta(" in mac
          and "private void Burbuja(" in mac
          and '_capas.Prefijo + "EJES-BURBUJA"' in mac
          and '_capas.Prefijo + "EJES-TEXTO"' in mac)
    check("las rayitas van en la capa de la burbuja, no en la de los ejes",
          "Linea(x1, y1, x2, y2, capaBur);" in mac)
    check("y las cotas, en la capa de cotas y con su estilo",
          "private void CotaAlineada(" in mac
          and '_capas.Prefijo + "COTAS"' in mac
          and "_ms.AddDimAligned(" in mac)
    # EL PUNTO DECIMAL, POR OBJETO. En el estilo -DIMDSEP- no basta: en un AutoCAD en
    # español gana la configuracion regional y las cotas salen con coma. La macro lo pone en
    # CADA cota, y eso es lo que hay que hacer.
    check("cada cota lleva su separador decimal, para que sea PUNTO y no coma",
          "d.DecimalSeparator = sepDecimal;" in mac
          and '_cfg.Texto("COTA_SEPARADOR_DECIMAL", ".")' in mac)

    check("la cota total lleva su linea de extension corta, para no tocar la burbuja",
          '_cfg.Numero("COTA_TOTAL_EXT_LINE_EXT", 0)' in mac
          and "c.EsTotal ? extTotal : -1" in mac)

    check("el rotulo de dos renglones se dibuja debajo de los ejes",
          "private void RotuloDeLaPlanta(" in mac
          and "Ejes.AbajoDeEjes(hayEjes)" in mac
          and "Rot.SeparacionEjes" in mac)
    check("y se MIDE para poder centrarlo, como hace la macro",
          "private double AnchoDeTexto(" in mac
          and "GetBoundingBox" in mac
          and "MoverTexto(t1, x0, y0);" in mac)
    # GetBoundingBox NO se puede llamar con dynamic: devuelve por referencia. Ya se
    # aprendio dos veces en este proyecto, asi que aqui va por reflexion desde el principio.
    check("la medida va por reflexion y no con dynamic",
          "System.Reflection.ParameterModifier(2)" in mac
          and "InvokeMember(" in mac)

    check("el muro de block lleva su polilinea ancha",
          "private bool LineaDeMamposteria(" in mac
          and '_cfg.Numero("MAMPOSTERIA_ANCHO", 0.06)' in mac
          and '_cfg.Numero("MAMPOSTERIA_GAP_M", 0.05)' in mac
          and '_capas.Prefijo + "MAMPOSTERIA"' in mac)
    check("y el material del muro llega desde la ventana, con la regla de la macro",
          "public string Material { get; set; }" in dto
          and "SeccionesModelo.MaterialDeMuro(el.Seccion, el.Notas)" in codigo)

    check("las capas de CAPAS_AL_FRENTE se suben al frente al terminar",
          "private void TraerCapasAlFrente()" in mac
          and 'dict.AddObject("ACAD_SORTENTS", "AcDbSortentsTable")' in mac
          and "tabla.MoveToTop(" in mac
          and '_doc.SetVariable("SORTENTS", 127)' in mac
          and "TraerCapasAlFrente();" in dib)
    # El respaldo de la macro -copiar y borrar- NO se porta: cambia los handles y rompe
    # xrefs, campos y anotaciones asociativas.
    check("y no se recurre a copiar y borrar, que cambia los handles",
          "RecrearAlFrente" not in mac)

    # ------------------------------------------------------------------
    # LOS ROTULOS, ENCIMA DE TODO: DOS PASADAS DEL ORDEN DE DIBUJO
    # ------------------------------------------------------------------
    #  En una sola pasada el orden entre la geometria y los textos lo decidia el recorrido
    #  del dibujo, asi que unas veces el rotulo quedaba encima y otras debajo. Subiendo
    #  primero la geometria y DESPUES los textos, los textos quedan siempre arriba.
    check("los rotulos se suben al frente en una segunda pasada, despues de la geometria",
          "private void SubirCapas(" in mac
          and "SubirCapas(_capas.CapasAlFrente());" in mac
          and "SubirCapas(_capas.CapasDeTextoAlFrente());" in mac
          and mac.find("SubirCapas(_capas.CapasAlFrente());")
              < mac.find("SubirCapas(_capas.CapasDeTextoAlFrente());"))
    # EL MTEXT NO SE SUBE AL FRENTE, y esto es lo que se pidio: tiene que quedar ENCIMA de
    # la polilinea de mamposteria -para eso lleva fondo- pero DEBAJO de las lineas de la
    # cadena y del acero. Sale solo del orden en que se dibuja, asi que la lista va VACIA.
    check("el MTEXT no se sube al frente: queda entre la mamposteria y las lineas",
          "public IReadOnlyList<string> CapasDeTextoAlFrente()" in capp
          and '_cfg.Texto("CAPAS_TEXTO_AL_FRENTE", string.Empty)' in capp
          and 'P("CAPAS_TEXTO_AL_FRENTE", "",' in cfgp
          and "s != piers &&" in capp)
    check("y la prueba lo comprueba",
          'Igual("la lista de capas de texto al frente va VACIA", ""' in pr)

    # CAPA POR CAPA Y EN SU ORDEN, no todas de golpe: cada MoveToTop deja lo suyo encima de
    # lo anterior. Con una sola llamada, el orden entre ellas lo decidia el recorrido del
    # dibujo, y era el motivo de que E-CADENA y E-ACERO siguieran saliendo tapadas.
    check("cada capa se sube por separado y en el orden de la hoja",
          "private bool MoverAlFrente(" in mac
          and "foreach (var capa in capas)" in mac
          and "porCapa[capa]" in mac)
    # Y con RESPALDO: el DRAWORDER de verdad, por comando, para cuando la tabla de orden no
    # se deja usar. Los nombres van con _ delante para que funcione en cualquier idioma.
    check("hay respaldo con el DRAWORDER de verdad",
          "private bool DrawOrderPorComando(" in mac
          and "_.draworder" in mac
          and "(410 . " in mac
          and "_doc.SendCommand(lisp)" in mac
          and '_cfg.Bandera("DRAWORDER_POR_COMANDO", true)' in mac)
    # LA CAPA DE LAS DALAS SE LLAMA E-CADENA, y CAPAS_AL_FRENTE tiene que decir CADENA o no
    # se subiria: es el nombre de la capa lo que se compara.
    check("la capa de las dalas se llama E-CADENA",
          'P("CAPA_DALA", "CADENA",' in cfgp
          and 'P("CAPAS_AL_FRENTE", "CADENA,CADENA DESPLANTE,TRABE,ACERO"' in cfgp
          and 'CapaDeTipo("DALA")' in capp)

    # ------------------------------------------------------------------
    # LAS LINEAS MUEREN EN EL PANO DEL CASTILLO, NO EN SU EJE
    # ------------------------------------------------------------------
    #  En el modelo el muro llega al NUDO -al centro del castillo-, y dibujado asi sus dos
    #  lineas cruzan la seccion de la columna. En obra el muro EMPIEZA en el pano.
    pan = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/PanoDeApoyo.cs"))
    check("existe la cuenta del ajuste al pano",
          "public sealed class PanoDeApoyo" in pan
          and "public Tramo Recortar(" in pan
          and "public static double? SalidaDelMaterial(" in pan
          and '_cfg.Bandera("LINEAS_AL_PANO", true)' in pan)
    check("con los numeros de la hoja: busqueda, solape, alargue y tope",
          '_cfg.Numero("PANO_BUSCA_CM", 150)' in pan
          and '_cfg.Numero("PANO_SOLAPE_CM", 0)' in pan
          and '_cfg.Numero("PANO_ALARGAR_MAX_CM", 150)' in pan
          and '_cfg.Numero("PANO_RECORTE_MAX", 0.4)' in pan)
    # LA MISMA CUENTA ALARGA el muro que quedo corto en el modelo: el recorte sale negativo.
    # Es el detalle elegante de la macro y la mitad que se olvida.
    check("y la misma cuenta alarga el muro que quedo corto",
          "El apoyo queda <b>detrás</b>" in pan or "queda DETRÁS" in pan)
    # El tramo de la trabe se guarda ahora en una variable -tramoTrabe- en lugar de calcularse
    # dentro de la llamada, porque hace falta DOS veces: para dibujarla partida por el vano y para
    # el respaldo de una pieza. Sigue siendo el mismo Pano.Recortar con los mismos argumentos.
    check("el muro y la trabe se dibujan sobre el tramo llevado al pano",
          "var apoyos = p.Elementos.Where(e => e.Clase == ClasePlanta.Columna).ToList();" in dib
          and "var tramo = Pano.Recortar(el, apoyos, cruces);" in dib
          and "var tramoTrabe = Pano.Recortar(el, apoyos, cruces);" in dib
          and "tramoTrabe, punteada))" in dib
          and "PanoDeApoyo.Tramo? tramo = null" in dib)
    # Un castillo INTERMEDIO no recorta nada: si contara, un muro largo con un castillo a un
    # metro de la punta se quedaria cortado por la mitad.
    check("un castillo intermedio no recorta el muro",
          "es un castillo intermedio" in pan)
    check("hay prueba ejecutable del ajuste al pano",
          "el muro arranca en el pano del castillo, no en su eje" in pre
          and "el muro corto se alarga hasta el pano" in pre
          and "entre los patines, a la cara del alma" in pre
          and "un castillo por el que el muro pasa de largo no lo recorta" in pre)

    # LA MAMPOSTERIA SE DESPEGA DEL CASTILLO 5 cm, y solo si el muro llega a 1 m: por debajo
    # de eso los dos huecos se comerian la linea y quedaria un rayon suelto en medio.
    check("la linea de mamposteria se mide desde el pano y se despega 5 cm",
          "PanoDeApoyo.Tramo? tramo = null)" in mac
          and "LineaDeMamposteria(el, x0, y0, tramo);" in dib
          and "if (gap > 0 && largo >= minimo)" in mac)

    # ------------------------------------------------------------------
    # LOS EJES DE SAP2000: LOS DEL MODELO, NI UNO DE MAS
    # ------------------------------------------------------------------
    #  GetGridSys_2 es de ETABS; SAP2000 tiene su cuadricula en GetGridSysCartesian. Sin esa
    #  segunda pasada, en SAP2000 los ejes NUNCA salian del modelo: se deducian, y salia una
    #  burbuja por cada quiebre de muro. Y cada eje de mas se acota.
    lect_sap = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("en SAP2000 la cuadricula se lee con GetGridSysCartesian",
          'Com.CallRet(gridSys, "GetGridSysCartesian"' in lect_sap
          and 'Com.CallRet(gridSys, "GetGridSys_2"' in lect_sap)
    ejm = leer(ruta("client/src/CadLink.Etabs/EjesModelo.cs"))
    check("y si hay que deducirlos, solo de las columnas: no de cada quiebre de muro",
          "if (e.Clase != ClaseElemento.Columna)" in ejm
          and "var conColumnas = xs.Count >= 2 || ys.Count >= 2;" in ejm
          and '"deducida de las columnas del modelo"' in ejm)

    # ------------------------------------------------------------------
    # SAP2000 NO TIENE PISOS: LOS NIVELES SALEN DE LA Z
    # ------------------------------------------------------------------
    #  Los stories son de ETABS. Sin esto, un modelo de SAP llegaba con TODOS los elementos
    #  en un solo nivel sin nombre y el juego de plantas era UNA planta con el edificio
    #  entero encimado. Cada elemento va al nivel de su cota mas ALTA, que es la regla de
    #  ETABS: una columna del suelo al primer piso pertenece al piso de arriba.
    mod_z = leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
    check("sin pisos, los niveles se deducen de la altura en Z",
          "public void NivelesDesdeZ(" in mod_z
          and "e.Story = nivel.Nombre;" in mod_z
          and "if (m.Niveles.Count == 0)" in lect_sap
          and "m.NivelesDesdeZ();" in lect_sap)
    check("y el mas bajo es la BASE, donde van las cadenas de desplante",
          'var nombre = i == 0 && cotas.Count > 1 ? "Base" : $"N{i}";' in mod_z)

    # ------------------------------------------------------------------
    # LA CADENA SIN MURO DE PISO A TECHO, A TRAZOS
    # ------------------------------------------------------------------
    #  Es MarcarCadenasSinMuro: la cadena que no lleva su muro completo debajo sale con
    #  ACAD_ISO02W100, y con muro completo va normal. En la CIMENTACION todas continuas
    #  -CIMENTACION_SIN_PUNTEADA-, porque una cadena de desplante no lleva muro por
    #  definicion y saldrian TODAS punteadas.
    check("la cadena sin muro de piso a techo va con ACAD_ISO02W100",
          "private (string Tipo, double Escala)? LineaDeCadenaSinMuro(" in mac
          and '_cfg.Texto("CADENA_SIN_MURO_LINETYPE", "ACAD_ISO02W100")' in mac
          and "if (el.MuroDePisoATecho)" in mac
          and "public bool MuroDePisoATecho { get; set; }" in dto)
    check("en la cimentacion, todas continuas",
          '_cfg.Bandera("CIMENTACION_SIN_PUNTEADA", true) && Rot.EsCimentacion(p.Nivel)' in mac)
    check("y el tipo de linea va POR OBJETO, no por capa",
          "private void PonerTipoDeLinea(" in dib
          and "PonerTipoDeLinea(p1, lt.Tipo, lt.Escala);" in dib)
    # El dato lo calcula la VENTANA: hay que mirar el nivel de abajo del modelo, y el
    # dibujante solo ve una planta.
    check("y quien sabe si hay muro completo es el modelo, no el dibujante",
          "public bool MuroDePisoATechoBajo(" in mod_z
          and "MuroDePisoATecho = el.Clase == ClaseElemento.Trabe" in codigo)

    # ------------------------------------------------------------------
    # LA VIGA MUERE EN LA CARA DE LA VIGA QUE CRUZA
    # ------------------------------------------------------------------
    #  Es la imagen 2 del usuario: en cada nudo las lineas se cortan, no se cruzan. Se hace
    #  con la HUELLA de la otra barra -un rectangulo largo- y la misma cuenta del rayo.
    check("la viga se corta contra la huella de la que cruza",
          "public static ElementoPlanta Huella(" in pan
          and "PanoDeApoyo.Huella(el, anchoHuella)" in dib
          and '_cfg.Bandera("VIGAS_CORTAR_EN_CRUCES", true)' in dib)
    # Y SOLO contra lo que CRUZA: dos tramos del mismo muro en linea se tocan por la punta,
    # y medirlos uno contra otro dejaria cada tramo la mitad.
    check("y solo contra lo que cruza, no contra lo que sigue en linea",
          "private static bool EsTransversal(" in pan
          and "seno > 0.342" in pan)

    # ------------------------------------------------------------------
    # LA LOSA: ARMADO, VOLADO Y CONTORNO
    # ------------------------------------------------------------------
    los = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/LosaEnPlanta.cs"))
    check("hay cuenta de apoyos, volado y parrilla de la losa",
          "public static class LosaEnPlanta" in los
          and "public static bool EsVolada(" in los
          and "public static List<Segmento> Parrilla(" in los
          and "public static double FraccionApoyada(" in los)
    # LA UNION Y NO LA SUMA: dos cadenas traslapadas cubren su tramo una sola vez.
    check("los apoyos se miden por UNION de tramos, no sumando",
          "public static List<(double A, double B)> Unidos(" in los
          and "return Unidos(tramos).Sum(t => t.B - t.A) / largo;" in los)
    # LA REGLA SEMIABIERTA de la macro: un vertice sobre la linea cuenta UNA vez. Sin ella
    # las parejas se descuadran y media parrilla sale fuera de la losa.
    check("la parrilla se recorta al contorno con la regla semiabierta",
          "public static List<(double A, double B)> Cortes(" in los
          and "if (!((ca <= c && cb > c) || (cb <= c && ca > c)))" in los)
    check("el armado sale con los numeros de la hoja",
          "private void ArmadoDeLosa(" in dib
          and '_cfg.Numero("MALLA_SEP_CM", 15)' in dib
          and '_cfg.Bandera("DIBUJAR_ARMADO_LOSA", true)' in dib
          and '_cfg.Numero("ARMADO_LOSA_ESPESOR_MIN_CM", 8)' in dib
          and '_cfg.Numero("MALLA_MAX_LINEAS", 200)' in dib)
    check("y ajustado al pano: la varilla no se mete en la cadena",
          '_cfg.Bandera("MALLA_AL_PANO", true)' in dib
          and "LosaEnPlanta.TramosFuera(b, huellas, minTramo)" in dib)
    # EL VOLADIZO: su hatch, su capa propia, y E-LOSA apagada.
    # EL VOLADO SE RECONOCE POR SU NOTA, no por la geometria: se pidio que el ANSI37 salga
    # SOLO en las losas cuya etiqueta de nota diga VOLADO. Contar lados apoyados se equivoca
    # en cuanto una cadena viene partida en el modelo, y el achurado aparecia donde no va.
    check("el volado se reconoce por su NOTA",
          "public static bool DiceVolado(" in los
          and '_cfg.Bandera("VOLADO_POR_NOTA", true)' in dib
          and '_cfg.Texto("LOSA_PALABRAS_VOLADO", "VOLADO,VOLADIZO,VOLADA,CANTILEVER")' in dib
          and "public string Notas { get; set; }" in dto
          and "Notas = el.Notas," in codigo)
    check("y el color de E-VOLADO es el 252",
          'P("COLOR_VOLADO", "252",' in cfgp
          and 'Igual("E-VOLADO, la de la losa en voladizo", 252, ColorDe("E-VOLADO"))' in pr)

    # EN EL TABLERO APOYADO VA LA BAYONETA, no la rejilla: la parrilla en todos los tableros
    # llenaba el plano de rejilla azul y tapaba las cadenas.
    # EL ARMADO DEL TABLERO, CON LAS MEDIDAS DE LA MACRO: la bayoneta de seis vertices con
    # sus quiebres a 45, los dos bastones de L/4 con su rayita, y la corrida. Y cada varilla
    # en DOBLE LINEA, que es su DobleLineaDesde.
    check("en el tablero apoyado va la bayoneta, los bastones y la corrida",
          "public static List<Trazo> ArmadoDeTablero(" in los
          and "var barD = 0.0157 * escala;" in los
          and "var corrOff = 0.0344 * escala;" in los
          and "var bastOff = 0.0287 * escala;" in los
          and "var hBaston = largo / 4;" in los
          and '_cfg.Bandera("ARMADO_LOSA_BAYONETA", true)' in dib)
    check("y cada varilla va en doble linea",
          "public static double MedioDiametroDeVarilla(" in los
          and "private void DibujarTrazoDeArmado(" in dib
          and "private object? PolilineaAbierta(" in dib)
    # AL PANO: el armado empieza donde empieza el claro, no en el eje de la cadena.
    check("el armado se mide sobre el tablero llevado al pano",
          '_cfg.Bandera("ARMADO_AL_PANO_CADENA", true)' in dib
          and "LosaEnPlanta.MedioApoyoEnBorde(" in dib)

    # ------------------------------------------------------------------
    # LOS PEDAZOS DEL MESH, EN UN SOLO TABLERO
    # ------------------------------------------------------------------
    #  «Si tengo varias secciones de losa en un mismo tablero, juntalas para que solo de un armado,
    #  ojo, debe estar dentro de los limites de los muros o trabes o cadenas que lo limite: esas 3
    #  losas son solo 1 en realidad, solo se dividio por el mesh en el programa».
    #
    #  Y es asi: esos pedazos NO son losas distintas. El mesh parte la losa -en los nudos de las
    #  trabes, en los ejes, o donde el programa decidio al mallar- y lo que en la obra es UN tablero
    #  de concreto llega al dibujo como tres o cuatro shells. Dibujando cada shell por su cuenta
    #  salian tres armados pequeños dentro del mismo tablero y tres rotulos «Losa de... cm de
    #  espesor... Var. # @... cm.» encimados: la malla del programa de calculo copiada al papel.
    tab = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/TableroDeLosa.cs"))
    pre_tab = leer(ruta("tools/prueba-ejes-plano/Program.cs"))
    check("los pedazos de losa se juntan en tableros",
          "public static class TableroDeLosa" in tab
          and "public sealed record Tablero(" in tab
          and "public static List<Tablero> Agrupar(" in tab
          and "public static bool MismoTablero(" in tab
          and 'P("LOSA_UNIR_TABLEROS", "SI",' in cfgp)
    # EL LIMITE QUE SE PIDIO: la union tiene que quedar dentro de los apoyos que limitan el tablero.
    # Si por la orilla que comparten corre un muro, una trabe o una cadena, son DOS tableros: el
    # apoyo interrumpe el claro y ahi cambia el acero. Se reusa la misma cuenta que lleva el armado
    # al pano -MedioApoyoEnBorde-, que ya sabe distinguir el apoyo que CORRE del que solo cruza.
    check("y no se juntan cuando un apoyo corre por la frontera",
          "public static LosaEnPlanta.Segmento? Frontera(" in tab
          and "public static bool HayApoyoEnLaFrontera(" in tab
          and "Frontera(a, b, tolM) is { } f" in tab
          and "&& !HayApoyoEnLaFrontera(f, huellas, cubre)" in tab)
    # POR LA UNION DE LO QUE LLEVA DEBAJO, NO APOYO POR APOYO. AQUI ESTABA EL FALLO que unio cinco
    # pedazos de dos tableros en uno: se preguntaba «¿este muro recorre la frontera?», y un muro con
    # VANOS de puerta y de ventana llega al dibujo partido en tres o cuatro trozos, de los que
    # ninguno la recorre entera. La respuesta era «no hay apoyo» y los dos tableros se juntaban.
    check("la frontera se mide por union de lo que lleva debajo, no apoyo por apoyo",
          "LosaEnPlanta.FraccionApoyada(frontera, huellas) >= cubre" in tab
          and 'P("LOSA_TABLERO_APOYO_CUBRE", "0.5",' in cfgp
          and '_cfg.Numero("LOSA_TABLERO_APOYO_CUBRE", 0.5)' in dib)
    # Y LA SEGUNDA VUELTA: el apoyo que no esta sobre la orilla comun sino EN MEDIO de los dos. Si
    # andando del centro de uno al centro del otro se pisa un apoyo, son dos tableros.
    check("y se mira si hay un apoyo en medio de los dos",
          "public static bool ApoyoEnMedio(" in tab
          and "&& !ApoyoEnMedio(a, b, huellas);" in tab
          and "PanoDeApoyo.Intervalos(h, ax, ay, ux, uy)" in tab)
    # PERO NO CUENTA LO QUE SE PISA EN EL ARRANQUE -un pedazo estrecho justo encima de un muro se
    # quedaria suelto para siempre- NI LO QUE CAE EN EL HUECO DE UNA L, que no es de este paño.
    check("sin contar el apoyo del propio centro ni el del hueco de una L",
          "if (desde <= minM || hasta >= largo - minM || hasta - desde < minM)" in tab
          and "if (Dentro(a.Vertices, mx, my) || Dentro(b.Vertices, mx, my))" in tab)
    # TOCARSE EN UNA ESQUINA NO ES COMPARTIR ORILLA: dos tableros en diagonal se tocan en un punto.
    check("y tocarse en una esquina no es compartir orilla",
          "if (hasta - desde <= tol)" in tab)
    # LA FUSION DE GRUPOS: si un pedazo resulta vecino de dos grupos, los dos son el mismo tablero.
    # Sin fusionar, una losa mallada en nueve cuadros se descubre en zigzag y quedaban dos o tres
    # tableros donde hay uno.
    check("los grupos vecinos se fusionan, que la malla se descubre en zigzag",
          "suyos[0].AddRange(suyos[k]);" in tab
          and "grupos.Remove(suyos[k]);" in tab)
    # UN TABLERO, UN ARMADO: lo dibuja el pedazo mas grande y sobre la caja del tablero COMPLETO,
    # que es el claro de verdad. Los demas se callan.
    check("un tablero, un armado, medido sobre el tablero completo",
          "var tablero = TableroDe(el);" in dib
          and "if (tablero is not null && !tablero.Manejado(el))" in dib
          and "var ax0 = (tablero?.X0 ?? el.Vertices.Min(v => v.X)) + margen;" in dib
          and "var ancho = tablero?.Ancho ?? (el.Vertices.Max(v => v.X)"
              " - el.Vertices.Min(v => v.X));" in dib)
    # UN TABLERO, UN ROTULO: los tres textos encimados eran esto -un rotulo por pedazo-, y va al
    # CENTRO DEL TABLERO, no al del pedazo.
    check("un tablero, un rotulo, al centro del tablero",
          "var suTablero = TableroDe(el);" in dib
          and "if (suTablero is not null && !suTablero.Manejado(el))" in dib
          and "cx = suTablero.CentroX + x0;" in dib
          and "cy = suTablero.CentroY + y0;" in dib)
    # Y EL ROTULO, DENTRO DEL TABLERO: en una L el centro de la caja cae en el hueco.
    check("y el rotulo no cae en el hueco de un tablero en L",
          "public static bool Dentro(" in tab
          and "if (!g.Any(e => Dentro(e.Vertices, cx, cy)))" in tab)
    # MANDA EL PEDAZO MAS GRANDE: de el salen el espesor y el uso que se rotulan. Es lo honesto
    # cuando el mesh reparte propiedades distintas entre los pedazos de un mismo tablero, que es el
    # caso que se enseño: tres pedazos con tres nombres de seccion.
    check("manda el pedazo mas grande, y se avisa si no coincidian",
          "public static double Area(" in tab
          and "private void AvisarDeLosTableros()" in dib
          and "espesores.Max() - espesores.Min() > 0.01" in dib)
    # LA RAYA DEL MESH NO SE DIBUJA: esa orilla en la obra NO EXISTE, el concreto es continuo. Es la
    # misma raya que ya se quita entre dos voladizos pegados. Y solo la de SU tablero: la que da a
    # otro tablero -la que tiene un apoyo debajo- si se dibuja, porque ahi termina el paño.
    check("la raya del mesh entre pedazos del mismo tablero no se dibuja",
          "private List<IReadOnlyList<(double X, double Y)>> OtrosDelTablero(" in dib
          and '_cfg.Bandera("LOSA_TABLERO_SIN_LINEA_INTERIOR", true)' in dib
          and "PanoDeLosa.ContornoCompartido(t, mismoTablero)" in dib
          and 'P("LOSA_TABLERO_SIN_LINEA_INTERIOR", "SI",' in cfgp)
    # UN VOLADO NO SE JUNTA CON UN ENTREPISO ni una losacero con una losa de concreto: se dibujan
    # distinto y se rotulan distinto, aunque se toquen.
    check("y el volado no se junta con el entrepiso",
          "private string FamiliaDeLaLosa(" in dib
          and 'return "VOLADO";' in dib
          and 'return "LOSACERO";' in dib
          and "el => FamiliaDeLaLosa(el, huellas)));" in dib
          and "familia: e => e.Notas.Contains(\"VOLADO\") ? \"VOLADO\" : \"LOSA\").Count);"
              in pre_tab)
    # SE CALCULAN TODOS ANTES DE DIBUJAR EL PRIMER PAÑO, como los voladizos y por lo mismo: cada
    # pedazo tiene que saber a que tablero pertenece ANTES de decidir si le toca dibujar el armado y
    # el rotulo o callarse.
    check("los tableros se conocen antes de dibujar la primera losa",
          "_tablerosDeLaPlanta.Clear();" in dib
          and dib.index("_tablerosDeLaPlanta.AddRange(TableroDeLosa.Agrupar(")
              < dib.index("if (Losa(el, x0, y0, huellas))"))
    # Y CON LAS HOLGURAS DE LA HOJA: la de pegado es nueva, y la del apoyo sale de las que ya
    # estaban -LOSA_APOYO_TOL_CM y LOSA_APOYO_CUBRE-, que hasta ahora no se usaban en ningun sitio.
    check("con las holguras de la hoja CONFIG",
          '_cfg.Numero("LOSA_TABLERO_TOL_CM", 5) / 100' in dib
          and 'P("LOSA_TABLERO_TOL_CM", "5",' in cfgp)
    # Y CADA TABLERO PARTIDO SE CUENTA CON SU MEDIDA Y SU SITIO: es lo que permite revisar la union
    # sin abrir el modelo. Si uno salio mas grande de lo que es, se ve en su medida y se sabe donde.
    check("y cada tablero unido se dice con su medida y su sitio",
          'Nota($"  · Tablero de {t.Ancho:0.00} × {t.Alto:0.00} m en " +' in dib)
    # Y SU PRUEBA EJECUTABLE, que es lo que comprueba la geometria de verdad y no el texto.
    check("hay prueba ejecutable de los tableros de losa",
          "TableroDeLosa.Agrupar(" in pre_tab
          and 'Igual("los tres pedazos son UN tablero", 1, unSolo.Count);' in pre_tab
          and 'Igual("con una trabe en la frontera son DOS tableros", 2, dosTableros.Count);'
              in pre_tab
          and "TableroDeLosa.HayApoyoEnLaFrontera(frontera.Value, trabeEnMedio)" in pre_tab
          and 'Igual("un muro partido por sus vanos separa los dos tableros",' in pre_tab
          and "2, TableroDeLosa.Agrupar(new List<ElementoPlanta> { pedazoA, pedazoB },"
              " muroConVanos).Count);" in pre_tab
          and "TableroDeLosa.ApoyoEnMedio(" in pre_tab)

    # ------------------------------------------------------------------
    # EL PUNTO DE INSERCION EN LA VISTA EXTRUIDA
    # ------------------------------------------------------------------
    #  «Respeta los insertion point en la vista extruida: las trabes las inserta en su punto
    #  centrico y esta mal, debe ser top center para que el paño coincida con el de la losa, asi
    #  como en ETABS». El punto cardinal de una trabe es casi siempre el 8 -arriba al centro-: su
    #  CARA DE ARRIBA va a la cota de la linea, asi que la trabe cuelga por debajo del piso.
    #  Dibujandola centrada, medio peralte quedaba POR ENCIMA de la losa.
    #
    #  El movimiento en PLANTA ya se aplicaba; la Z no, y es la que se ve en un dibujo con
    #  volumen. Se guarda aparte -MovidoZI/MovidoZJ- y NO se aplica a Z1/Z2 a proposito: de la
    #  elevacion depende el nivel al que se reparte la pieza, y moverla romperia la planta.
    pins = leer(ruta("client/src/CadLink.Etabs/PuntoDeInsercion.cs"))
    dtos = leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
    extr = leer(ruta("client/src/CadLink.App/VistaModelo.Extruida.cs"))
    lector = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    dtop2 = leer(ruta("client/src/CadLink.Cad/PlantaCad.cs"))
    dibp = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.cs"))
    winp = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))

    check("el punto de insercion se calcula tambien en Z",
          "public static (double Dx, double Dy, double Dz) Movimiento(" in pins
          and "return (cx + ox, cy + oy, cz + oz);" in pins)
    # EnPlanta se queda, y ahora tira de Movimiento: una sola cuenta, no dos que se separen.
    check("y EnPlanta se apoya en ella, para no tener dos cuentas",
          "var (dx, dy, _) = Movimiento(" in pins)
    check("el elemento guarda su movimiento en Z sin aplicarlo",
          "public double MovidoZI { get; set; }" in dtos
          and "public double MovidoZJ { get; set; }" in dtos
          and "e.MovidoZI = dzi;" in lector
          and "e.MovidoZJ = dzj;" in lector)
    # Y la Z NO se aplica a las cotas: solo la usa quien dibuja volumen.
    check("y no se toca Z1 ni Z2 con ella",
          "e.Z1 += dzi;" not in lector
          and "e.Z2 += dzj;" not in lector)
    check("la vista extruida lo usa, que es donde se ve",
          "var bz = fin ? el.Z2 + el.MovidoZJ : el.Z1 + el.MovidoZI;" in extr)

    # ------------------------------------------------------------------
    # VARIOS CORTES, Y DONDE UNO QUIERA
    # ------------------------------------------------------------------
    #  «Agrega una opcion de realizar un corte en donde tu quieras si no lo trae los ejes de etabs
    #  o sap, que tu coloques en que valor de X y Y lo quieres; si no encuentras el corte en los
    #  ejes tu lo propones; igual que deje dibujar varios ejes o cortes al mismo tiempo».
    #
    #  Y una regla que evita el error mas facil: si el valor cae SOBRE un eje que existe, el corte
    #  se queda con el NOMBRE de ese eje. Quien escribe «X=4.25» sin saber que ahi esta el eje C
    #  obtiene el corte por C -rotulado C, comparable con la planta- y no uno con nombre inventado.
    cop = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CortesPedidos.cs"))

    check("se pueden pedir varios cortes, por su eje o por su valor",
          "public static Resultado Interpretar(" in cop
          and "public sealed record Peticion(string Id, bool EnX, double Ordenada, bool Propuesto);"
              in cop
          and "public static string NombrePropuesto(bool enX, double ordenada)" in cop)
    check("el valor que cae sobre un eje toma el nombre de ese eje",
          "return mejor.Length > 0" in cop
          and "new Peticion(mejor, enX, valor, false)" in cop
          and "NombrePropuesto(enX, valor), enX, valor, true)" in cop)
    # Con PUNTO decimal: el nombre se rotula y acaba en el nombre de un bloque, asi que con la
    # coma regional el mismo corte se llamaria distinto en dos maquinas.
    check("y el nombre propuesto lleva punto decimal, no coma",
          'ordenada.ToString("0.##", CultureInfo.InvariantCulture)' in cop)
    # LA COMA HACE DOS PAPELES: separa la lista y es el decimal del teclado numerico. Separa salvo
    # cuando va entre dos cifras. Lo ambiguo -«3,4»- se avisa en lugar de adivinarse.
    check("la coma decimal se respeta y lo ambiguo se avisa",
          '@"[;\s]+|(?<![0-9]),|,(?![0-9])"' in cop
          and "public sealed record Resultado(List<Peticion> Cortes, List<string> NoReconocidos);"
              in cop)
    # Sin repetidos: el mismo corte por su nombre y por su valor se pide UNA vez.
    check("sin repetidos, y se queda el nombre del eje",
          "private static void Agregar(List<Peticion> cortes, Peticion nuevo, double tolM)" in cop
          and "Math.Abs(c.Ordenada - nuevo.Ordenada) <= tolM" in cop)
    # UN CAMPO PARA X Y OTRO PARA Y, que es como se pidio: «no tanto como una tabla, si no uno
    # mismo donde quiere cortar». En cada uno caben varias coordenadas separadas por comas, asi que
    # de ahi salen tambien varios cortes de golpe, y se admite el nombre de un eje de esa
    # direccion por si se prefiere decirlo asi.
    check("la ventana los pide en un campo para X y otro para Y",
          "CadLink.Cad.PlanoEstructural.CortesPedidos.Interpretar(" in winp
          and "null, CorteXTxt?.Text, CorteYTxt?.Text, ejesX, ejesY);" in winp
          and 'x:Name="CorteXTxt"' in xaml
          and 'x:Name="CorteYTxt"' in xaml)
    # EL REPARTO LO HACE EL DIBUJANTE: es el unico que sabe cuanto ocupo de verdad cada corte.
    check("y los reparte uno al lado del otro",
          "total += dibujante.DibujarCorte(c, 0, 0);" in winp
          and "var cx = _derechaDelUltimoCorte is { } yaHay"
              in leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Corte.cs")))
    # En esos campos, un nombre de eje tambien vale: escribir «C» en el de las X es pedir el corte
    # por el eje C, y avisar en lugar de dibujarlo seria quedarse corto por nada.
    check("en los campos de X y de Y tambien vale el nombre de un eje",
          "var deEsaDireccion = esX ? enX : enY;" in cop
          and "Agregar(cortes, new Peticion(eje.Id.Trim(), esX, eje.Ordenada, false), tolM);"
              in cop)
    # Lo que no se entendio se dice, y lo propuesto tambien: desde fuera «no salio» es
    # indistinguible de «fallo».
    check("y se avisa de lo que no se entendio y de lo propuesto",
          "No reconocí esto de los campos de corte: " in winp
          and "no caen sobre ningún eje de la cuadrícula" in winp)

    # ------------------------------------------------------------------
    # EN EL CORTE: LO QUE SE CORTA Y EL FONDO, NADA ENCIMADO
    # ------------------------------------------------------------------
    #  «En el corte de dibujo no se deben ver elementos encimandos, se deben ver los que se cortan
    #  justo en la linea y el fondo nada mas».
    #
    #  EL PROBLEMA ERA LA REBANADA: el corte se tomaba con el espesor de la hoja
    #  -CORTE_ESPESOR_CM, 60 cm-, asi que TODO lo que hubiera a 30 cm del eje se dibujaba como
    #  CORTADO: dos muros paralelos, la cadena del muro de al lado y las columnas de la fila
    #  siguiente salian todos a la vez, unos encima de otros.
    #
    #  Cortado es ahora lo que el plano CRUZA DE VERDAD: el elemento con su propio ancho encima del
    #  eje. La holgura solo tapa el desajuste del modelo y se TOPA en 5 cm por lado aunque la hoja
    #  diga 60: quien puso 60 no queria 60 cm de rebanada, queria que el corte no saliera vacio.
    corte = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CorteEnAlzado.cs"))

    check("cortado es lo que el plano cruza de verdad, no una rebanada de 60 cm",
          "private static double Holgura(double espesorM) =>" in corte
          and "Math.Min(Math.Max(espesorM, 0.02), 0.10) / 2;" in corte
          and "var medio = MedioPerpendicular(el, enX) + Holgura(espesorM);" in corte)
    # Con el ancho de cada elemento: un muro de 60 cruza desde mas lejos que uno de 15, y una barra
    # que cruza el corte no necesita ninguno porque su propio eje lo atraviesa.
    check("con el ancho de cada elemento, proyectado",
          "private static double MedioPerpendicular(ElementoPlanta el, bool enX)" in corte
          and "return esp * Math.Abs(enX ? -dy / largo : dx / largo);" in corte)
    # Y EL MISMO MARGEN EN LAS DOS PREGUNTAS, para que nada sea cortado y fondo a la vez: los de la
    # frontera salian dos veces, uno encima del otro.
    check("y el mismo margen para el fondo, que nada sea las dos cosas",
          "var margen = MedioPerpendicular(el, enX) + Holgura(espesorM);" in corte
          and "? min > ordenada + margen" in corte)
    # LO ENCIMADO SE QUITA: la misma silueta dos veces no dice nada, y lo que cae DENTRO de otra
    # pieza del fondo, tampoco.
    check("y las siluetas encimadas se quitan",
          "public static List<Pieza> SinEncimados(List<Pieza> piezas)" in corte
          and "return UnirElFondo(SinEncimados(piezas));" in corte
          and "private static bool Tapa(Pieza grande, Pieza chica)" in corte)
    # DOS REGLAS QUE NO SE NEGOCIAN: lo CORTADO no se quita nunca -es el objeto del corte- y solo
    # se comparan piezas de la MISMA CLASE, que una columna dentro de un muro dice otra cosa.
    # ------------------------------------------------------------------
    # EL CASTILLO DE AREA SE VE CON SU ESPESOR, NO CON SU LARGO
    # ------------------------------------------------------------------
    #  «El castillote de 15, el amarillo, no debe ir asi: solo se debe ver su espesor de 15 cm, no
    #  los 80 cm que mide». Se tomaba AnchoM a secas, y en un castillo de area ese ancho es su
    #  LARGO -«K 15X80» mide 80 a lo largo del muro y 15 de espesor-, asi que un corte que lo cruza
    #  de frente lo pintaba de 80 cm de ancho.
    #
    #  Lo que se ve es la seccion GIRADA medida en la direccion que recorre el corte: la caja que
    #  la envuelve, la misma cuenta con la que se coloca su rotulo en planta. Y de paso arregla la
    #  columna de 20x60 girada, que se veia de 20.
    check("en el corte se ve la seccion proyectada, no su lado mas largo",
          "public static double AnchoVisto(ElementoPlanta el, bool enX)" in corte
          and "return enX ? (b * sa) + (h * ca) : (b * ca) + (h * sa);" in corte
          and "var ancho = AnchoVisto(el, enX);" in corte)

    # ------------------------------------------------------------------
    # EL MURO, HASTA EL PAÑO DE ABAJO DE SU CADENA
    # ------------------------------------------------------------------
    #  «La altura del muro debe ser dibujada hasta el paño inferior de la trabe o cadena». En el
    #  modelo el muro sube hasta la COTA DEL NIVEL, que es el EJE de la cadena, asi que dibujandolo
    #  tal cual se mete el peralte entero dentro de ella: en el corte el muro y la cadena se
    #  pisaban y la cadena perdia su franja.
    check("el muro llega al paño de abajo de su cadena",
          "public static double AlturaQueTapaLaCadena(" in corte
          and "var alto = (zArriba - AlturaQueTapaLaCadena(el, todos)) - zAbajo;" in corte
          and "peralte = Math.Max(peralte, c.PeralteM);" in corte)
    # Solo la que va A LO LARGO del muro y A SU ALTURA: una que lo cruza pasa por encima, y la de
    # la azotea no remata el muro de la planta baja.
    check("y solo la que va a lo largo y a su altura",
          "if (Math.Abs(Math.Max(c.Z1, c.Z2) - arribaDelMuro) > tolM)" in corte
          and "if (Math.Abs((ux * (vy / largoC)) - (uy * (vx / largoC))) > 0.10)" in corte)

    # ------------------------------------------------------------------
    # CADA PIEZA CORTADA, DE SU COLOR
    # ------------------------------------------------------------------
    #  «Las cadenas que se cortan rellenalas de color morado, asi como los castillos es de
    #  amarillo» y «las trabes rellenalas de color verde». No es decoracion: en un corte por un muro
    #  hay tres piezas de concreto distintas a la vista -el castillo que sube, la cadena que cierra
    #  y la trabe que carga- y del contorno solo no se distinguen, porque las tres son un
    #  rectangulo.
    cortedib = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Corte.cs"))
    cfgp = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/ConfigPlano.cs"))

    check("la cadena cortada va morada y la trabe verde",
          "private int ColorDelRellenoEnElCorte(" in cortedib
          and '_cfg.Numero("CORTE_COLOR_RELLENO_CADENA", 6)' in cortedib
          and '_cfg.Numero("CORTE_COLOR_RELLENO_TRABE", 3)' in cortedib
          and 'P("CORTE_COLOR_RELLENO_CADENA", "6",' in cfgp
          and 'P("CORTE_COLOR_RELLENO_TRABE", "3",' in cfgp)
    # El castillo sigue amarillo, con el color de la planta: es la misma pieza en los dos dibujos.
    check("y el castillo sigue amarillo, con el color de la planta",
          "if (p.Clase == ClasePlanta.Columna)\n        {\n            return ColorDelRelleno();"
          in cortedib)
    # Solo las CORTADAS, y la losa y el muro no se rellenan: se leen por su franja y por su paño.
    check("solo las cortadas, y el muro y la losa sin relleno",
          "if (p.Cortada && enSeccion &&" in cortedib
          and "return p.Clase == ClasePlanta.Trabe" in cortedib
          and "            : 0;" in cortedib)

    # ------------------------------------------------------------------
    # EL COLOR Y EL BLOQUE, POR LO QUE DICE LA PIEZA Y NO POR SU CLASE
    # ------------------------------------------------------------------
    #  Aqui estaba la cadena intermedia que no se rellenaba ni salia como bloque, por mas vueltas que
    #  se le dieron: se modela como AREA -un shell- y llega al dibujo con la clase MURO, y el corte
    #  miraba la CLASE, veia un muro y la dejaba vacia, dijeran lo que dijeran sus notas.
    #
    #  La conversion de shells la arregla antes, pero esto NO DEPENDE de ella: si por lo que sea un
    #  elemento llega sin convertir, el corte lo dibuja bien igual. El dato lo pone el modelo en las
    #  property notes -CADENA INTERMEDIA-, asi que no se adivina nada.
    check("el relleno del corte se decide por lo que dice la pieza",
          "public static bool DiceCadena(Pieza p) =>" in corte
          and 'Dicen(p, "CADENA") || Dicen(p, "DALA");' in corte
          and "public static bool DiceTrabe(Pieza p) =>" in corte
          and "if (PlanoEstructural.CorteEnAlzado.DiceCadena(p))" in cortedib
          and "if (PlanoEstructural.CorteEnAlzado.DiceTrabe(p))" in cortedib)
    # Por el TIPO o por las NOTAS, que el tipo llega en blanco cuando el modelo no clasifico.
    check("y se mira su tipo y sus notas, no su clase",
          "private static bool Dicen(Pieza p, string palabra) =>" in corte
          and '(p.Tipo ?? string.Empty).Contains(palabra, StringComparison.OrdinalIgnoreCase)'
              in corte
          and '|| (p.Notas ?? string.Empty).Contains(palabra, StringComparison.OrdinalIgnoreCase);'
              in corte)
    # Y EL BLOQUE IGUAL: sin esto, la cadena que llega como muro se quedaba sin su bloque.
    check("y el bloque del corte tambien",
          "var deBarra = p.Clase == ClasePlanta.Trabe" in cortedib
          and "|| PlanoEstructural.CorteEnAlzado.DiceCadena(p)" in cortedib
          and "|| PlanoEstructural.CorteEnAlzado.DiceTrabe(p);" in cortedib
          and "if (p.Cortada && conBloque && deBarra" in cortedib)

    # ------------------------------------------------------------------
    # EL CONTORNO DE LOS MUROS DEL FONDO NO SE DIBUJA
    # ------------------------------------------------------------------
    #  «El contorno de los muros del fondo borralos, solo deja el contorno de los muros que se cortan
    #  sobre la linea de corte o eje». Y se entiende al verlo: el fondo de un alzado son cinco o seis
    #  paños seguidos, y cada rectangulo mete cuatro lineas que no son de este corte. Del fondo lo
    #  que dice algo es su ACHURADO -la mancha de mamposteria-, no sus aristas.
    #
    #  El contorno se usa igual como LAZO del achurado y se borra despues, que el hatch no es
    #  asociativo. Y se borra solo el MURO: una cadena o una trabe modelada como area llega con la
    #  clase Muro y esas si dejan su contorno, que son piezas y no paño.
    check("los muros del fondo van sin contorno, solo con su achurado",
          "var soloParaAchurar = !p.Cortada" in cortedib
          and "&& p.Clase == ClasePlanta.Muro" in cortedib
          and "&& !PlanoEstructural.CorteEnAlzado.DiceCadena(p)" in cortedib
          and '&& !_cfg.Bandera("CORTE_FONDO_CONTORNO_MUROS", false);' in cortedib
          and "if (soloParaAchurar)\n                {\n                    Borrar(pl);" in cortedib
          and 'P("CORTE_FONDO_CONTORNO_MUROS", "NO",' in cfgp)

    # ------------------------------------------------------------------
    # SE RELLENA LO QUE SE VE EN SECCION, NO EL COSTADO
    # ------------------------------------------------------------------
    #  «Si cortas a lo largo de la seccion solo dale el tipo de linea, pero si lo cortas donde se ve
    #  el armado -que debe ser el lado corto- si rellena la seccion». Es la convencion de cualquier
    #  plano de obra: el relleno dice «aqui el plano cruza la pieza y esto es su seccion, la cara
    #  donde va el armado». Rellenando tambien lo que se ve de costado, el alzado deja de decir por
    #  donde pasa el corte.
    #
    #  El castillo de area «K 15X80» es el caso claro: cortado por su lado de 15 es una seccion -se
    #  rellena- y cortado a lo largo de sus 80 es un costado -solo su linea-. Una seccion CUADRADA
    #  se ve en seccion siempre, asi que un castillo de 15x15 se rellena se corte por donde se
    #  corte, que es lo que ya pasaba y hay que conservar.
    check("se rellena solo lo que se ve en seccion",
          "public static bool PorSuLadoCorto(ElementoPlanta el, bool enX)" in corte
          and "bool EnSeccion = true, string Notas = \"\");" in corte
          and "EnSeccion: PorSuLadoCorto(el, enX))" in corte
          and 'P("CORTE_RELLENAR_SOLO_EN_SECCION", "SI",' in cfgp)
    check("y el dibujante lo mira antes de rellenar",
          "var enSeccion = p.EnSeccion" in cortedib
          and "|| !soloEnSeccion" in cortedib
          and "if (p.Cortada && enSeccion &&" in cortedib)
    # La barra que CRUZA el corte se ve de canto -esa es su seccion, la del armado- y la que corre a
    # lo largo, de costado. El muro y la losa, de costado siempre: se leen por su paño y su franja.
    check("la barra de canto va en seccion y la que corre a lo largo, no",
          "EnSeccion: false, Notas: el.Notas);\n    }" in corte
          and corte.count("EnSeccion: false") >= 3)
    # Una seccion cuadrada se ve en seccion siempre.
    check("y una seccion cuadrada se ve en seccion siempre",
          "if (largo - corto <= 0.02)" in corte
          and "return AnchoVisto(el, enX) < (corto + largo) / 2;" in corte)

    # ------------------------------------------------------------------
    # LAS PIEZAS DEL CORTE, COMO BLOQUE
    # ------------------------------------------------------------------
    #  «El corte no estas poniendo o creando el bloque de la cadena intermedia, tampoco lo estas
    #  rellenando» y «igual crea los bloques de trabes y cadenas o vigas de acero en corte».
    #
    #  Es la misma idea que ya se usa con las columnas en planta: el bloque se llama como la seccion
    #  -con su medida detras- asi que un BLOCKREPLACE cambia de golpe TODAS las cadenas de 15x25 del
    #  corte por el detalle armado, con sus varillas y sus estribos. La medida va en el nombre porque
    #  la misma seccion se ve de dos formas en un corte: de canto son 15x25 y a lo largo son tres
    #  metros por 25, que es otro dibujo y no puede compartir bloque.
    check("las trabes y cadenas del corte van como bloque",
          "private bool PiezaComoBloque(" in cortedib
          and "private string NombreDelBloqueDeLaPieza(" in cortedib
          and "private bool AsegurarBloqueDeLaPieza(" in cortedib
          and 'P("CORTE_PIEZAS_COMO_BLOQUE", "SI",' in cfgp)
    # Con PREFIJO para no chocar con los bloques de la planta: la seccion de una columna se llama
    # igual en los dos dibujos y no es el mismo dibujo -uno es su seccion en planta y el otro su
    # alzado-.
    check("con su prefijo y su medida en el nombre",
          '_cfg.Texto("CORTE_BLOQUE_PREFIJO", "CORTE-")' in cortedib
          and 'var medida = $"{p.Ancho * 100:0.##}X{p.Alto * 100:0.##}";' in cortedib)
    # EL RELLENO VA DENTRO DEL BLOQUE, como en la planta: asi se mueve con el y quien reemplace el
    # bloque por su detalle se lleva el relleno con el cambio. Y la insercion, al CENTRO de la pieza.
    check("con su relleno dentro y la insercion al centro",
          "private void RellenarDentroDelBloqueDelCorte(" in cortedib
          and "cy + p.Z + (p.Alto / 2)," in cortedib)
    # SOLO LA CARA CORTA, LA QUE LLEGA: el bloque de una trabe de 20x30 es su CARA de 20x30 -la
    # seccion donde se dibujan las varillas y los estribos- no el rectangulo de tres metros que se
    # ve cuando el corte va a lo largo de ella. Un bloque de tres metros no se puede reemplazar por
    # ningun detalle armado: no es una seccion, es un costado. Y solo las CORTADAS.
    check("y solo la cara corta de las cortadas",
          "var conBloque = p.EnSeccion" in cortedib
          and "if (p.Cortada && conBloque && deBarra\n"
              "                && PiezaComoBloque(" in cortedib)

    # ------------------------------------------------------------------
    # EL AREA DE LOS MUROS DE MAMPOSTERIA, ACHURADA
    # ------------------------------------------------------------------
    #  «Cuando se vean muros en el corte de fondo, agrega solo en el area de muros de MAMPOSTERIA
    #  -ojo, no de concreto- un hatch AR-BRSTD si es tabique o adobe con escala de 0.0010 color 12,
    #  y si es tabicon o tabique ligero, un hatch AR-B816 con escala de 0.0005 y color 12».
    #
    #  Es una diferencia de obra: un muro de mamposteria se levanta con piezas y mortero y uno de
    #  concreto se cimbra y se cuela, y en el corte se tiene que ver de un golpe cual es cual.
    ham = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/HatchDeMamposteria.cs"))

    check("los muros de mamposteria del corte llevan su achurado",
          "public static Achurado? Para(" in ham
          and 'string patronTabique = "AR-BRSTD", double escalaTabique = 0.0010' in ham
          and 'string patronTabicon = "AR-B816", double escalaTabicon = 0.0005' in ham
          and "int color = 12)" in ham)
    # EL ORDEN IMPORTA: «TABIQUE LIGERO» contiene «TABIQUE», asi que preguntando por el tabique
    # primero el ligero saldria con el aparejo de ladrillo, que es el de la pieza maciza.
    check("y el tabique ligero va con el patron del bloque, no con el del ladrillo",
          ham.index('texto.Contains("TABIQUE LIGERO"') < ham.index('texto.Contains("TABIQUE",'))
    # EL CONCRETO NO LLEVA NINGUNO: en el corte se lee por su paño.
    check("el muro de concreto no lleva achurado",
          "return null;\n    }\n\n    /// <summary>El achurado, con el respaldo" in ham)
    # LOS ACENTOS NO CUENTAN: «TABICON» y «TABICÓN» son la misma palabra, y un muro sin achurado por
    # una tilde es de las cosas que nadie encuentra mirando el plano.
    check("y los acentos no dejan a un muro sin su patron",
          "public static string Normalizar(string? texto)" in ham
          and "'Ó' => 'O'," in ham)
    check("el dibujante lo achura por objeto y no asociativo",
          "private void AchurarMamposteria(" in cortedib
          and "(object)_ms.AddHatch(0, cual.Patron, false, 0)" in cortedib
          and "h.PatternScale = cual.Escala;" in cortedib
          and "h.Color = cual.Color;" in cortedib)
    # LA ESCALA ANTES DE EVALUAR: hay versiones que se quedan con el achurado de la primera
    # evaluacion si la escala se cambia despues.
    check("con la escala antes de evaluar",
          cortedib.index("h.PatternScale = cual.Escala;") < cortedib.index("h.Evaluate();\n"
          "                h.Layer = capa;\n                h.Color = cual.Color;"))
    check("y con sus seis claves en la hoja",
          'P("CORTE_HATCH_MAMPOSTERIA", "SI",' in cfgp
          and 'P("CORTE_HATCH_TABIQUE", "AR-BRSTD",' in cfgp
          and 'P("CORTE_HATCH_TABIQUE_ESCALA", "0.0010",' in cfgp
          and 'P("CORTE_HATCH_TABICON", "AR-B816",' in cfgp
          and 'P("CORTE_HATCH_TABICON_ESCALA", "0.0005",' in cfgp
          and 'P("CORTE_HATCH_MAMPOSTERIA_COLOR", "12",' in cfgp)
    # Y LA PIEZA LLEVA SUS NOTAS, que es de donde sale de que es el muro.
    check("y la pieza del corte lleva las notas del muro",
          "string Notas = \"\");" in corte
          and "EnSeccion: false, Notas: el.Notas)" in corte)

    # PERO NO SE ACHURA DONDE EL CORTE PASA POR CONCRETO: «a los de fondo si, y a los que corta
    # tambien, pero siempre y cuando no corte en un elemento de concreto». Donde el corte pasa por un
    # castillo, una cadena o un muro de concreto, lo que hay ahi es CONCRETO: achurarlo de tabique
    # seria decir que ese trozo se levanto con ladrillos. Un castillo en medio parte el achurado en
    # dos, que es lo que se ve en obra: dos paños de mamposteria con su castillo entre los dos.
    check("no se achura donde el corte pasa por concreto",
          "public static List<(double X1, double X2)> TramosSinConcreto(" in corte
          and "private static bool EsConcretoQueTapa(Pieza q, Pieza muro)" in corte
          and "var tramos = PlanoEstructural.CorteEnAlzado.TramosSinConcreto(p, piezas);"
              in cortedib)
    # Solo las CORTADAS -lo que se ve al fondo no interrumpe lo que esta delante- y solo las que se
    # enciman EN VERTICAL: una cadena tres metros mas arriba no le quita nada al muro.
    check("solo lo cortado, y solo si se encima en vertical",
          "if (ReferenceEquals(q, muro) || !q.Cortada)" in corte
          and "return Math.Min(q.Z + q.Alto, muro.Z + muro.Alto) - Math.Max(q.Z, muro.Z) > Minimo;"
              in corte)
    # Y OTRO MURO DE MAMPOSTERIA NO lo parte -los dos son de piezas- pero uno de concreto si.
    check("otro muro de mamposteria no lo parte, uno de concreto si",
          "|| (q.Clase == ClasePlanta.Muro\n                             && HatchDeMamposteria.Para("
          in corte)
    # EL CASO NORMAL SE ACHURA SOBRE SU PROPIA POLILINEA, sin crear nada; y cuando hay concreto en
    # medio, el contorno de cada tramo es un LAZO DE PASO que se dibuja, se achura y SE BORRA: esas
    # lineas no existen en el muro y dejarlas seria inventar juntas donde no las hay.
    check("y los tramos se achuran con un lazo de paso que se borra",
          "AchurarConPatron(lazo, capa, cual, borrarElLazo: true);" in cortedib
          and "AchurarConPatron(pl, capa, cual, borrarElLazo: false);" in cortedib
          and "if (borrarElLazo)" in cortedib)

    # ------------------------------------------------------------------
    # LA CADENA MODELADA COMO SHELL DE MURO
    # ------------------------------------------------------------------
    #  AQUI ESTABA LA CADENA INTERMEDIA que no se rellenaba ni llevaba bloque, por mas vueltas que se
    #  le dio al relleno: NO ES UN MARCO, ES UN SHELL. Una cadena tambien se modela como area -las
    #  INTERMEDIAS casi siempre, porque se dibujan como un trozo del propio muro- y dibujada como
    #  muro no era una cadena para nada: sin su capa, sin su rotulo, sin relleno en el corte y sin
    #  bloque. Es el hermano de CastilloDeMuro y sale del mismo sitio.
    cdmc = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CadenaDeMuro.cs"))

    check("el shell que dice cadena se dibuja como cadena",
          "public static bool Dice(ElementoPlanta? el)" in cdmc
          and "Clase = ClasePlanta.Trabe," in cdmc
          and "PlanoEstructural.CadenaDeMuro.Normalizar(" in dibp
          and "PlanoEstructural.CadenaDeMuro.Normalizar(c.Elementos, AnchoTrabePorOmision);"
              in cortedib)
    # LA GEOMETRIA de un shell vertical: su largo en planta es el recorrido, el espesor del muro es
    # el ancho de la seccion y su alto en Z es el PERALTE.
    check("con el espesor por ancho y su alto por peralte",
          "var peralte = alto > Nada ? alto : peraltePorOmision;" in cdmc
          and "AnchoM = muro.AnchoM," in cdmc
          and "PeralteM = peralte," in cdmc)
    # LA COTA VA ARRIBA: una barra cuelga de su cota, y con la cota abajo la cadena de cerramiento
    # saldria un peralte por encima del techo.
    check("y la cota en su cara de arriba, que una barra cuelga",
          "Z1 = zArriba,\n            Z2 = zArriba," in cdmc)
    # UN SHELL QUE DICE CASTILLO NO ES UNA CADENA: ese tiene su propia conversion, y si las notas
    # dijeran las dos cosas manda el castillo, que es la pieza vertical.
    check("y el castillo no se la lleva por delante",
          "if (CastilloDeMuro.Dice(el))" in cdmc)
    # EN LA VENTANA sigue a la casilla de las TRABES, como el castillo sigue a la de las columnas.
    check("en la ventana sigue a la casilla de las trabes",
          "CadLink.Cad.PlanoEstructural.CadenaDeMuro.DicenLasNotas(null, el.Notas)" in winp
          and "return VerTrabesPlanoChk.IsChecked == true;" in winp)

    # ------------------------------------------------------------------
    # LOS CASTILLOS DEL FONDO NO SE DIBUJAN
    # ------------------------------------------------------------------
    #  «Los castillos del fondo no se deben ver, solamente los que hayan en el lugar del corte, en
    #  esa linea». En una casa de mamposteria hay un castillo cada dos metros en TODOS los ejes, asi
    #  que el fondo de un alzado se llena de rectangulos verticales que no son de este corte y que
    #  tapan lo que si lo es. Del fondo interesa el paño de los muros y la losa que sigue.
    check("los castillos del fondo no se dibujan en el corte",
          "if (!p.Cortada && p.Clase == ClasePlanta.Columna" in cortedib
          and '&& !_cfg.Bandera("CORTE_FONDO_CON_COLUMNAS", false))' in cortedib
          and 'P("CORTE_FONDO_CON_COLUMNAS", "NO",' in cfgp)

    # ------------------------------------------------------------------
    # LA CADENA INTERMEDIA: SIEMPRE RELLENA Y CON BLOQUE
    # ------------------------------------------------------------------
    #  Se pidio tres veces, y tiene su razon de obra: la INTERMEDIA es la que confina los vanos de
    #  puertas y ventanas y la que remata un antepecho, va metida en el muro y es lo que se viene a
    #  revisar en un corte. Sin relleno se pierde entre las dos lineas del paño, y sin bloque no se
    #  puede cambiar por su detalle armado.
    #
    #  Es la UNICA excepcion a «solo se rellena lo que se ve en seccion»: las demas cadenas y trabes
    #  vistas a lo largo siguen yendo vacias, que es lo que se pidio despues.
    check("la cadena intermedia se rellena y lleva bloque aunque el corte vaya a lo largo",
          "public static bool EsIntermedia(Pieza p) =>" in corte
          and "PlanoEstructural.CorteEnAlzado.EsIntermedia(p)" in cortedib
          and cortedib.count("PlanoEstructural.CorteEnAlzado.EsIntermedia(p)") == 2
          and 'P("CORTE_INTERMEDIA_SIEMPRE", "SI",' in cfgp)
    # Por su TIPO y por sus NOTAS -en femenino y en masculino-, que el tipo puede llegar en blanco.
    check("y se reconoce por su tipo o por sus notas",
          '(p.Tipo ?? string.Empty).Contains("INTERMEDIA"' in corte
          and '(p.Notas ?? string.Empty).Contains("INTERMEDIO"' in corte)
    # La barra del corte lleva sus notas, que es de donde sale el tipo cuando el modelo no clasifico.
    check("la barra del corte lleva sus notas",
          "EnSeccion: false, Notas: el.Notas);" in corte
          and "ancho, peralte, el.Tipo, Notas: el.Notas);" in corte)

    # ------------------------------------------------------------------
    # CADA CORTE, +8 A LA DERECHA DEL ANTERIOR
    # ------------------------------------------------------------------
    #  «Si voy a agregar mas cortes, que los agregue a la derecha +8.00 del ultimo corte existente,
    #  asi para N cantidad de cortes». El reparto lo hace el DIBUJANTE, que es el unico que sabe
    #  cuanto ocupo de verdad cada corte -depende de las piezas que toque, de sus ejes y de sus
    #  cotas-. Calculandolo desde la ventana a ojo, los cortes se encimaban o quedaban a diez metros.
    check("cada corte se encadena a la derecha del anterior",
          "private double? _derechaDelUltimoCorte;" in cortedib
          and "var cx = _derechaDelUltimoCorte is { } yaHay" in cortedib
          and '? yaHay + _cfg.Numero("CORTE_SEPARACION_CORTES_M", 8)' in cortedib
          and 'P("CORTE_SEPARACION_CORTES_M", "8",' in cfgp)
    # Y se le suma lo que sobresale a su derecha -las burbujas de sus ejes y sus cotas-, que si no el
    # siguiente corte se le metia encima de las burbujas.
    check("contando sus burbujas y sus cotas",
          "_derechaDelUltimoCorte = cx + piezas.Max(q => q.X + q.Ancho)" in cortedib
          and "+ Ejes.SaleEjes() + Ejes.RadioBurbuja;" in cortedib)
    # Y la ventana ya no lo calcula: pide los cortes en orden y el dibujante los encadena.
    check("y la ventana ya no lo calcula",
          "total += dibujante.DibujarCorte(c, 0, 0);" in winp
          and "MedidasDelModelo()" not in winp)

    # ------------------------------------------------------------------
    # EL LADO DEL CORTE, EN EL VISOR Y EN TIEMPO REAL
    # ------------------------------------------------------------------
    #  «Haz que cuando elija entre un lado u el otro del corte, en la vista 3D de abajo igual se
    #  actualice en tiempo real». Es lo que convierte la lista en algo util: se elige un lado y se ve
    #  al momento si por ahi hay algo o si el edificio esta del otro lado. Antes habia que dibujar en
    #  AutoCAD para descubrirlo.
    vism = leer(ruta("client/src/CadLink.App/VistaModelo.cs"))

    check("el visor mira el lado del corte y se rehace al cambiarlo",
          "public bool CorteHaciaMas { get; set; } = true;" in vism
          and "return CorteHaciaMas" in vism
          and "private void OnLadoDelCorteCambiado(" in winp
          and 'SelectionChanged="OnLadoDelCorteCambiado"' in xaml
          and "_vista.CorteHaciaMas = LadoDelCorteCombo?.SelectedIndex != 1;" in winp)
    # Y el corte que se dibuja toma el lado del visor: lo que se ve es lo que se dibuja.
    check("y el corte que se dibuja toma el lado del visor",
          "HaciaMas = _vista.CorteHaciaMas" in winp)

    # ------------------------------------------------------------------
    # SOLO LAS CARAS QUE LLEGAN: LO LARGO VA VACIO
    # ------------------------------------------------------------------
    #  «No rellenes de color las cadenas, vigas o trabes largas, dejalas vacias, solo rellena las
    #  caras que llegan de las secciones». La regla vale para TODO, sin excepciones por clase.
    #
    #  Hubo una excepcion para las cadenas y duro poco, con razon: rellenar una cadena de cuatro
    #  metros pinta de morado media fachada del alzado y entierra debajo lo unico que ese relleno
    #  tenia que señalar, que son las caras cortadas. Y la cadena intermedia no pierde nada: la que
    #  confina un vano se ve por su cara -el plano la cruza- y esa si se rellena y si lleva bloque.
    # La UNICA excepcion es la cadena INTERMEDIA -por su tipo, no por su clase-, que se pidio tres
    # veces: es la que confina los vanos y va metida en el muro. Las demas cadenas y trabes vistas a
    # lo largo van vacias.
    check("solo se rellenan las caras que llegan, y la intermedia",
          "var enSeccion = p.EnSeccion\n                                || !soloEnSeccion"
          in cortedib
          and "|| p.Clase != ClasePlanta.Columna;" not in cortedib
          and "PlanoEstructural.CorteEnAlzado.EsIntermedia(p)" in cortedib)

    # ------------------------------------------------------------------
    # EL MURO, HASTA EL PAÑO DEL CASTILLO
    # ------------------------------------------------------------------
    #  «Las lineas de los muros ponlos hasta el paño de los castillos o columnas, no al eje». En el
    #  modelo el muro va de NUDO a NUDO -del eje de un castillo al del siguiente- pero el muro de
    #  verdad arranca en la CARA del castillo, porque contra el se levanta. Dibujado a ejes se mete
    #  medio castillo por cada punta y lo pisa justo donde el castillo tiene que verse entero, que es
    #  donde lleva su armado. Es lo contrario de lo que se hace con la trabe -a ella se le SUMA medio
    #  apoyo- y por eso es la misma cuenta con el signo cambiado.
    check("el muro del corte muere en el paño del castillo",
          "var caraA = MedioApoyoEn(el, enX, min, todos);" in corte
          and "var izquierda = min + caraA;" in corte
          and "var derecha = max - caraB;" in corte)
    # Y si los castillos se comieran el muro entero se deja como estaba: mejor un muro de mas que un
    # hueco donde hay pared.
    check("y si sus apoyos se lo comieran entero, se deja",
          "if (derecha - izquierda <= Minimo)" in corte
          and "izquierda = min;" in corte)

    # ------------------------------------------------------------------
    # QUE LADO DEL CORTE SE MIRA
    # ------------------------------------------------------------------
    #  «Y ver que lado del corte quieres ver: si es en X, si quieres ver lo de arriba o abajo del
    #  corte, y si es en Y el corte, si quieres ver el lado derecho o izquierdo».
    #
    #  Un corte mira hacia un lado: lo de detras se ve y lo de delante se quita. Estaba fijo hacia
    #  las coordenadas mayores, y eso deja cortes en los que no se ve NADA al fondo porque el
    #  edificio esta del otro lado del plano.
    check("se elige que lado del corte se mira",
          "bool haciaMas = true)" in corte
          and "return haciaMas\n            ? min > ordenada + margen\n"
              "            : max < ordenada - margen;" in corte
          and "public bool HaciaMas { get; set; } = true;" in dtop2)
    # Y LAS OPCIONES SE AJUSTAN SOLAS AL CORTE QUE SE PIDE: en uno cuyo plano esta en X los lados
    # son derecha e izquierda; en uno en Y, arriba y abajo. Leer «derecha / arriba» cuando solo una
    # de las dos aplica obliga a traducir mentalmente cada vez, y es donde uno se equivoca de lado.
    # Las dos parejas se quedan cuando se piden cortes en las DOS direcciones a la vez, porque
    # entonces las dos cosas son ciertas. Y sin corte pedido, la lista se apaga.
    check("las opciones del lado se ajustan solas al corte",
          "private void ActualizarLadoDelCorte()" in winp
          and 'hayX ? "derecha (+X)" : "arriba (+Y)"' in winp
          and 'hayX ? "izquierda (-X)" : "abajo (-Y)"' in winp
          and "LadoDelCorteCombo.IsEnabled = conCorte;" in winp)
    # Se recalcula al cambiar el eje, al escribir en los campos y al leer el modelo.
    check("y se recalculan cuando cambia lo pedido",
          "private void OnCortePersonalizadoCambiado(" in winp
          and 'TextChanged="OnCortePersonalizadoCambiado"' in xaml
          and winp.count("ActualizarLadoDelCorte();") >= 4)
    # Solo el TEXTO de las opciones: el indice elegido no se toca, que si no cambiar de corte
    # moveria el lado que el usuario acaba de escoger.
    check("sin tocar el lado que ya eligio el usuario",
          "if (LadoDelCorteCombo.Items[0] is ComboBoxItem arriba)" in winp
          and "arriba.Content = mas;" in winp)

    check("y la ventana lo pregunta",
          'x:Name="LadoDelCorteCombo"' in xaml
          and "HaciaMas = LadoDelCorteCombo?.SelectedIndex != 1" in winp
          and "c.Elementos, c.EnX, c.Ordenada, c.EspesorM,\n"
              '            _cfg.Bandera("CORTE_VER_EL_FONDO", true), c.HaciaMas);' in cortedib)

    # ------------------------------------------------------------------
    # EL FONDO, UNA SILUETA: LA FUNCION «COMO LA DE REVIT»
    # ------------------------------------------------------------------
    #  «Tambien se deben ver los muros de hasta el fondo, quiero hacer una funcion como la de
    #  Revit». En un programa de modelado, de lo que hay detras del plano se ve LA SILUETA, no las
    #  aristas de cada pieza. En un muro de mamposteria el fondo son cinco o seis paños seguidos a
    #  distinta profundidad, y dibujando cada uno por separado el alzado sale con una raya vertical
    #  en cada junta: rayas que no existen, porque ahi el muro sigue.
    check("el fondo se une en una silueta",
          "public static List<Pieza> UnirElFondo(List<Pieza> piezas)" in corte
          and "return UnirElFondo(SinEncimados(piezas));" in corte
          and "private static bool SeUnen(Pieza a, Pieza b)" in corte)
    # UN VANO NO SE UNE -el hueco es un dato del alzado- y LO CORTADO NO SE UNE NUNCA: cada pieza
    # cortada es una pieza de obra.
    check("pero no por encima de un vano, ni lo cortado",
          "var salida = piezas.Where(p => p.Cortada).ToList();" in corte
          and "Math.Min(a.X + a.Ancho, b.X + b.Ancho) >= Math.Max(a.X, b.X) - h;" in corte
          and "|| Math.Abs(a.Z - b.Z) > h" in corte)

    check("lo cortado no se quita nunca, y solo compite con su misma clase",
          "if (!p.Cortada && salida.Any(q => Tapa(q, p)))" in corte
          and "if (grande.Clase != chica.Clase)" in corte
          and ".OrderByDescending(x => x.p.Cortada)" in corte)
    # Que falle UN corte no impide los demas: se avisa y se sigue.
    check("un corte que falla no se lleva a los demas",
          "no se pudo dibujar: {ex.Message}" in winp)

    # ------------------------------------------------------------------
    # SOLO EJES Y CORTES, SIN LA PLANTA
    # ------------------------------------------------------------------
    #  «La opcion de solo dibujar ejes y cortes sin hacer todo el dibujo de planos». Sirve para
    #  montar la cuadricula sobre un plano de arquitectura que ya existe, o para replantear con las
    #  cotas de los ejes y nada mas.
    #
    #  Los elementos SIGUEN LLEGANDO en la planta y eso es a proposito: de ellos salen el
    #  rectangulo que los ejes cubren y el paño al que se corren los de orilla, asi que la
    #  cuadricula cae EN EL MISMO SITIO que caeria con la planta dibujada y la estructura se puede
    #  dibujar despues encima sin que nada se mueva.
    check("se puede dibujar solo la cuadricula y los cortes",
          "public bool SoloEjes { get; set; }" in dtop2
          and "if (p.SoloEjes)" in dibp
          and 'x:Name="SoloEjesCortesChk"' in xaml
          and "p.SoloEjes = soloEjes;" in winp)
    # Y con los ejes y el rotulo, que es lo que se pidio dibujar.
    check("con sus ejes, sus cotas y su rotulo",
          "var cajaSola = Envolvente(p);" in dibp
          and "DibujarEjesDeLaPlanta(\n                p, x0, y0, cajaSola.XMin" in dibp
          and "RotuloDeLaPlanta(p, x0, y0, cajaSola.XMin, cajaSola.YMin, cajaSola.XMax);" in dibp)

    # ------------------------------------------------------------------
    # EL ARMADO, AL PANO DE LA TRABE Y NO A SU EJE
    # ------------------------------------------------------------------
    #  «Cuando el armado de losa llegue a trabe, igual que llegue al paño y no al eje de la trabe,
    #  como a los muros o cadenas». Hace falta porque en el modelo LA LOSA SE DIBUJA HASTA EL EJE
    #  de la trabe que la sostiene: tomando el borde del paño tal cual, la varilla se mete media
    #  trabe dentro de ella, que no es donde empieza el claro ni donde se pone el acero.
    #
    #  Y POR QUE NO ENTRABAN LAS TRABES: la cuenta vieja buscaba el LADO DEL POLIGONO del tablero
    #  en la coordenada extrema Y alineado con los ejes al milimetro de millon -1e-6-. Un tablero
    #  que no es un rectangulo perfecto, o con las coordenadas que trae ETABS, dejaba sin encontrar
    #  ese lado y no corria nada. Ahora se pregunta por los cuatro BORDES DE LA CAJA del armado,
    #  que ahi siempre estan.
    losap = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/LosaEnPlanta.cs"))

    check("el armado llega al pano del apoyo, sea muro, cadena o trabe",
          "public static double MedioApoyoEnBorde(" in losap
          and "ancho = Math.Max(ancho, h.PeralteM);" in losap
          and "return ancho / 2;" in losap)
    # PARALELO al borde, SOBRE su linea y RECORRIENDOLO: una trabe que solo lo cruza no lo apoya.
    check("y solo si va paralelo, sobre su linea y lo recorre",
          "if (Math.Abs((ux * vy) - (uy * vx)) > 0.10)" in losap
          and "(ux * (h.Y1 - borde.Y1))) > tolM)" in losap
          and "< largo * fraccionMin)" in losap)
    # Los cuatro bordes se miden ANTES de mover nada: si se aplicara uno a uno, el segundo se
    # mediria sobre un borde ya corrido y el apoyo caeria fuera de la holgura.
    check("los cuatro bordes se miden antes de mover nada",
          "var pIzq = LosaEnPlanta.MedioApoyoEnBorde(" in dib
          and "var pArriba = LosaEnPlanta.MedioApoyoEnBorde(" in dib
          and dib.index("var pArriba = LosaEnPlanta.MedioApoyoEnBorde(") < dib.index("ax0 += pIzq;"))
    # Y con la holgura del encuentro de la hoja, la misma con la que los muros mueren en el pano.
    check("con la holgura del encuentro de la hoja",
          'var tolPano = _cfg.Numero("PANO_TOLERANCIA_CM", 25) / 100;' in dib)
    # Ya no queda la cuenta vieja, que es la que dejaba fuera a las trabes.
    check("y no queda rastro de la cuenta vieja",
          "private static double MedioApoyo(" not in dib)
    check("y la rejilla se queda apagada, disponible pero no puesta",
          'P("ARMADO_LOSA_PARRILLA", "NO",' in cfgp
          and '_cfg.Bandera("ARMADO_LOSA_PARRILLA", false)' in dib)
    check("hay prueba ejecutable del armado y del volado por nota",
          "la bayoneta tiene SEIS vertices, como en la macro" in pre
          and "la corrida va de lado a lado" in pre
          and "una losa cuya NOTA dice VOLADO es volado" in pre
          and "una losa de azotea normal NO es volado" in pre)

    # EL ROTULO DE LA LOSA, LOS CUATRO RENGLONES DE LA HOJA. Antes se rotulaba el nombre de
    # la propiedad de ETABS, que en el plano no dice nada.
    check("la losa se rotula con los cuatro renglones de la hoja",
          "private string RotuloDeLosa(" in dib
          and '_cfg.TextoTalCual($"LOSA_TEXTO_{i}")' in dib
          and 'linea.Replace("%U", uso).Replace("%E", espesor)' in dib
          and "private string UsoDeLaLosa(" in dib)

    # PERO LA LOSA DE VOLADO, SOLO CON EL ARMADO. Se pidio tal cual: cuando diga VOLADO el
    # rotulo debe decir unicamente «Var. # @ cm. / Ambos sentidos», o sea los renglones 3 y
    # 4, sin el «Losa de ...» ni el espesor. Se reconoce con las MISMAS palabras que el
    # achurado ANSI37, para que rotulo y hatch no discrepen nunca.
    # EL VOLADO SE ROTULA «Losa VOLADO» EN EL PRIMER RENGLON, y sin el renglon del espesor.
    # La palabra sale de las NOTAS de la propiedad de la losa en ETABS -ahi la escribe el
    # ingeniero- y solo si las notas no dicen nada, del nombre de la seccion. Es la MISMA
    # palabra que decide el achurado, asi que rotulo y ANSI37 no se contradicen nunca.
    # EL ROTULO DEL VOLADO LLEVA LOS CUATRO RENGLONES: se pidio el espesor en el segundo y la
    # varilla en el tercero. La bandera de saltarse el espesor se queda en la hoja -en NO-
    # porque el mecanismo sirve, pero por omision no se salta nada.
    check("el volado se rotula «Losa de VOLADO» y con los cuatro renglones",
          'P("VOLADO_TEXTO_1", "Losa de %U",' in cfgp
          and 'P("VOLADO_ROTULO_SOLO_ARMADO", "NO",' in cfgp
          and '_cfg.TextoTalCual("VOLADO_TEXTO_1")' in dib
          and "public static string ArmarRotuloDeLosa(" in dib
          and "if (soloArmado && i == 2)" in dib)
    check("hay prueba ejecutable de los cuatro renglones del volado",
          "el volado lleva CUATRO renglones" in pre
          and "el segundo, el espesor" in pre
          and "el tercero, la varilla" in pre)
    check("y la palabra del volado sale de las NOTAS primero",
          "public static string PalabraVolado(" in los
          and "foreach (var donde in new[] { notas, seccion })" in los
          and "LosaEnPlanta.PalabraVolado(el.Notas, el.Seccion, PalabrasDeVolado())" in dib
          and "uso = palabraVolado;" in dib)
    check("hay prueba ejecutable del rotulo del volado",
          "el volado lleva TRES renglones" in pre
          and "el nombre va en el primero, y los otros dos son el armado" in pre
          and "la palabra sale de las NOTAS, aunque la seccion no diga nada" in pre
          and "la losa normal lleva los cuatro renglones" in pre)

    # LAS LINEAS DE E-ACERO, CONTINUAS: en la hoja de la macro ese renglon va vacio -no
    # toques la linea que tenga el dibujo- y por eso salian a trazos.
    check("las lineas de E-ACERO son continuas",
          'P("LINETYPE_ACERO", "Continuous",' in cfgp
          and 'Igual("y la del acero es CONTINUA", "Continuous", LineaDe("E-ACERO"))' in pr)
    # Y NUNCA A TRAZOS POR OBJETO: es el arreglo de su v50. Una viga de acero nunca lleva
    # muro de piso a techo debajo, asi que la regla de «sin muro -> punteada» se las llevaba
    # TODAS. Con ACERO_LINEA_BYLAYER no se les pone tipo de linea por objeto.
    check("y una viga de acero nunca sale punteada por objeto",
          '_cfg.Bandera("ACERO_LINEA_BYLAYER", true)' in mac
          and "PlanoEstructural.CapasPlano.EsPerfilAcero(el.Forma)" in mac)

    # ------------------------------------------------------------------
    # DONDE HAY ACERO LA LOSA ES LOSACERO, NO CONCRETO
    # ------------------------------------------------------------------
    #  Una losacero NO lleva armado de concreto: lleva las franjas de la lamina con el hatch
    #  FLEX en el sentido corto y su rotulo con el CALIBRE, que sale de las notas de la
    #  seccion de ETABS (LOSACERO CAL 24 -> 24).
    check("la losacero se dibuja con sus franjas de hatch FLEX",
          "public static List<Segmento> Franjas(" in los
          and "public static bool DiceLosacero(" in los
          and "private bool Losacero(" in dib
          and '_cfg.Texto("LOSACERO_HATCH_PATRON", "FLEX")' in dib
          and '_capas.Prefijo + "LOSACERO"' in dib)
    check("y con su rotulo, con el calibre de las notas",
          "public static string Calibre(" in los
          and '_cfg.Texto("LOSACERO_TEXTO_PLANTILLA", "LOSACERO IMSA CALIBRE %C")' in dib
          and '_cfg.Texto("LOSACERO_CALIBRE_OMISION", "24")' in dib)
    # El calibre: primero el numero que sigue a CAL, y si no hay, el ULTIMO del texto.
    check("el calibre sale del numero que sigue a CAL",
          'var cal = t.IndexOf("CAL", StringComparison.Ordinal);' in los)
    # Y EL LECTOR TIENE QUE SABER QUE ES UN DECK: su PropiedadDeLosa prueba GetDeck ANTES de
    # GetSlab, porque la propiedad de una losacero no responde a GetSlab.
    check("el lector pregunta GetDeck antes de GetSlab",
          'Com.CallRet(propArea, "GetDeck"' in lect_sap
          and '"DECK " + (d[6]?.ToString()' in lect_sap)
    check("hay prueba ejecutable de la losacero",
          "una losa que dice DECK es losacero" in pre
          and "LOSACERO CAL 24 da 24" in pre
          and "y todas corren en el sentido corto" in pre)

    # Y LA OTRA MITAD DEL ORDEN DE DIBUJO: la losa y su armado AL FONDO, mas un REGEN. Sin el
    # regen, AutoCAD puede seguir mostrando el orden viejo y eso se ve igual que si no se
    # hubiera aplicado.
    check("la losa y su armado se mandan al fondo",
          "private void BajarCapas(" in mac
          and "private bool MoverAlFondo(" in mac
          and "tabla.MoveToBottom(" in mac
          and "public IReadOnlyList<string> CapasAlFondo()" in capp
          and "BajarCapas(_capas.CapasAlFondo());" in mac)
    check("el DRAWORDER por comando sirve para los dos lados",
          'private bool DrawOrderPorComando(string capa, bool alFrente = true)' in mac
          and 'var donde = alFrente ? "_F" : "_B";' in mac)
    check("y al final se regenera, para que el orden nuevo se vea",
          "private void Regenerar()" in mac
          and "_doc.Regen(1)" in mac
          and "Regenerar();" in mac)

    check("el voladizo lleva su hatch en su propia capa",
          "private bool HatchSobre(" in mac
          and '_cfg.Texto("LOSA_HATCH_PATRON", "ANSI37")' in dib
          and "_capas.CapaVolado" in dib
          and "public string CapaVolado" in capp)
    # LA LINEA DEL VOLADO SE QUEDA, Y COMPLETA: es el borde libre de la losa, lo que se
    # cimbra, asi que no se recorta contra los muros. La misma polilinea es el molde del
    # achurado, asi que no hay que crear una auxiliar para borrarla.
    # LA LINEA DEL VOLADO, SOLO EL CONTORNO EXTERIOR: se pidio que no toque la cadena ni el
    # muro. La polilinea cerrada se sigue creando -un achurado necesita contorno cerrado para
    # nacer- pero solo como MOLDE, y se borra en cuanto el hatch esta puesto.
    check("el volado se achura con un molde y su linea es solo el contorno exterior",
          'P("VOLADO_CONTORNO_FUERA_DE_MUROS", "SI",' in cfgp
          and "var molde = PolilineaCerrada(pts, capa);" in dib
          and "conHatch = HatchSobre(molde, capa," in dib
          and '_cfg.Bandera("VOLADO_CONTORNO_FUERA_DE_MUROS", true)' in dib
          and "LosaEnPlanta.TramosFuera(lado, huellas)" in dib
          and "Borrar(molde);" in dib)
    # ES UN HATCH DE VERDAD, Y POR LA VIA QUE EN ESTE MISMO PROGRAMA SI FUNCIONA. El
    # achurado de las secciones y de las zapatas pasa el lazo por AcadArreglos -la cascada
    # que prueba el arreglo de entidades de varias formas, escrita porque AutoCAD 2026
    # rechaza un object[] pelado con «Invalid object array»- mientras que la planta lo pasaba
    # directo, y con AddHatch de TRES argumentos en vez de los cuatro que usa el relleno de
    # las columnas. Resultado: en la losa el hatch no nacia nunca y siempre acababa cayendo
    # al respaldo de rayitas, que es lo que se veia: lineas dibujadas una por una.
    check("el hatch de la losa se crea por la cascada de arreglos, como el de las secciones",
          "private bool Achurar(" in mac
          and "_ms.AddHatch(0, patron, asociativo, 0)" in mac
          and "AcadArreglos.Llamar(" in mac
          and "arr => { h.AppendOuterLoop(arr); }," in mac)
    check("y se prueban las dos asociatividades antes de rendirse",
          "foreach (var asociativo in new[] { false, true })" in mac
          and "if (Achurar(contorno, capa, patron, escala, anguloGrados, asociativo))" in mac)
    # Y SI LA API NO QUIERE, EL COMANDO -HATCH: lo que sale por ahi sigue siendo un HATCH
    # autentico, con su patron, no una imitacion con lineas.
    check("hay tercera via: el comando -HATCH, que sigue dando un hatch",
          'P("LOSA_HATCH_POR_COMANDO", "SI",' in cfgp
          and "private bool AchurarPorComando(" in mac
          and "._-hatch" in mac
          and "(handent" in mac
          and "hpassoc" in mac
          and "clayer" in mac)
    # EL COMANDO NO PUEDE DARSE POR BUENO SIN MIRAR. SendCommand no falla cuando el comando
    # de dentro se aborta -otro orden de preguntas, un patron que no esta en el acad.pat-, asi
    # que se creia el achurado puesto, se saltaba el respaldo y el voladizo se quedaba SIN
    # NADA: rotulo sobre una losa sin achurar, que es lo que se veia.
    check("el -HATCH por comando se comprueba, no se da por bueno",
          "private object? HatchRecienCreado(" in mac
          and "private int CuantosObjetos(" in mac
          and 'tipo.Contains("Hatch", StringComparison.OrdinalIgnoreCase)' in mac
          and "if (hecho is null)" in mac)
    # Y EL HATCH ASOCIATIVO NO PUEDE PERDER SU MOLDE: se le quita la asociatividad antes de
    # borrarlo, y si AutoCAD no deja, el molde SE QUEDA. Antes que un voladizo sin achurar,
    # una linea de mas por dentro del muro.
    check("al hatch asociativo se le quita la asociatividad antes de borrar el molde",
          "h.AssociativeHatch = false;" in mac
          and "private bool HatchAtadoAlMolde" in mac
          and "if (tramos > 0 && !HatchAtadoAlMolde)" in dib)

    # EL MTEXT DE LA LOSA, DENTRO DE UN BLOQUE, y uno por losa DISTINTA: cambiando el bloque
    # una vez se cambian los veinte rotulos de esa losa. Con veinte MTEXT sueltos hay que
    # escribirlo veinte veces y hay diecinueve ocasiones de que uno quede distinto.
    check("el rotulo de la losa va dentro de un bloque, uno por losa distinta",
          'P("LOSA_TEXTO_BLOQUE", "SI",' in cfgp
          and "private bool RotuloDeLosaComoBloque(" in mac
          and "private bool AsegurarBloqueDeRotulo(" in mac
          and "_bloquesDeRotulo" in mac
          and "if (!RotuloDeLosaComoBloque(el, cx, cy, texto, alturaLosa))" in dib)
    # CON EL PREFIJO DE LA MACRO -«TEXTO LOSA »- y no con uno nuevo: el bloque tiene que
    # llamarse como alla.
    check("y con el prefijo de la macro, no con uno inventado",
          '_cfg.Texto("LOSA_TEXTO_BLOQUE_PREFIJO", "TEXTO LOSA ")' in mac)
    # EL MISMO Mtexto de siempre, solo que dentro del bloque: duplicar la logica del estilo,
    # el ancho automatico y el fondo terminaria con dos rotulos que se ven distinto.
    check("el texto del bloque lo crea el mismo Mtexto, con un dueño distinto",
          "object? dentroDe = null)" in dib
          and "dynamic duenio = dentroDe ?? _ms;" in dib
          and "duenio.AddMText(" in dib)

    # ------------------------------------------------------------------
    # EL CORTE POR UN EJE, DIBUJADO AL LADO DE LA PLANTA
    # ------------------------------------------------------------------
    #  Se pidio: que se dibuje el corte ELEGIDO, a 10 m de la planta estructural. Juntos se
    #  leen: la planta da los espesores y las distancias entre ejes, y el corte las alturas.
    corte = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CorteEnAlzado.cs"))
    cortedib = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Corte.cs"))

    check("la geometria del corte esta aparte y es comprobable sin AutoCAD",
          "public static class CorteEnAlzado" in corte
          and "public static bool Entra(" in corte
          and "public static List<Pieza> Piezas(" in corte)
    # ARRIBA DE LAS PLANTAS, a +10: las plantas se reparten a lo ANCHO -una al lado de la
    # otra- asi que el corte a la derecha acabaria chocando con la planta siguiente en cuanto
    # el modelo tuviera un nivel mas. Encima queda en su propia banda y se lee junto a todas.
    # 10 UNIDADES ARRIBA DE LO YA DIBUJADO, y se pregunta AL DIBUJO. Antes se medía el alto de
    # los ELEMENTOS y se sumaba desde el origen del modelo, pero las plantas no se dibujan en el
    # origen -DibujarTodas las reparte y las sube al tope de lo que hubiera-, asi que el corte
    # caia justo en medio del juego. Con TopeDeLoDibujado el corte queda encima siempre.
    check("el corte se dibuja 10 unidades ARRIBA de lo ya dibujado",
          'P("CORTE_SEPARACION_M", "10",' in cfgp
          and 'P("CORTE_DIBUJAR", "SI",' in cfgp
          and "public int DibujarCorte(" in cortedib
          and '_cfg.Numero("CORTE_SEPARACION_M", 10)' in cortedib
          and "var cy = tope + separacion - zBase;" in cortedib
          and "private double? IzquierdaDeLoDibujado()" in cortedib
          and "Math.Max(_topeDelJuego ?? 0, TopeDeLoDibujado() ?? 0)" in cortedib)
    # NUNCA DEBAJO, tampoco si COM no responde. El tope del juego se CALCULA al repartir las
    # plantas, asi que siempre esta; lo leido del dibujo cubre ademas lo que hubiera de antes.
    # Con solo lo leido, un fallo de lectura dejaba el corte en Y = 10 con las plantas en Y = 40.
    check("el tope del juego se calcula, no solo se pregunta al dibujo",
          "_topeDelJuego = offsetY + (yMax - yMin) + Ejes.SaleEjes()" in dib
          and "private double? _topeDelJuego;" in dib)
    # LOS CASTILLOS, RELLENOS, como en la planta: el relleno es lo que distingue de un golpe el
    # elemento CORTADO del que solo se ve al fondo.
    # Ahora se rellenan TODAS las piezas cortadas que tienen color: el castillo amarillo, la cadena
    # morada y la trabe verde. El color lo decide ColorDelRellenoEnElCorte, y el muro y la losa
    # devuelven 0 -no se rellenan- porque en un alzado se leen por su paño y por su franja.
    check("las piezas cortadas del corte van rellenas, cada una de su color",
          'P("CORTE_RELLENAR_COLUMNAS", "SI",' in cfgp
          and "private void RellenarPieza(object? pl, string capa, int color)" in cortedib
          and "h.Color = color;" in cortedib
          and 'if (p.Cortada && enSeccion && _cfg.Bandera("CORTE_RELLENAR_COLUMNAS", true))'
              in cortedib
          and "var color = ColorDelRellenoEnElCorte(p);" in cortedib)
    # EL ESPESOR DE LA LOSA NO SE INVENTA: esto es un plano, la franja se mide y se acota, asi
    # que un espesor a dedo no es una aproximacion, es un dato falso. Sin el, sale una LINEA.
    check("el espesor de la losa del corte no se inventa",
          "var espesor = el.AnchoM > Minimo ? el.AnchoM : 0;" in corte
          and "p.Alto <= 0.001" in cortedib)
    # EL CORTE, CON SUS EJES Y ACOTADO: sin ejes no se sabe que columna es cual, y sin cotas
    # verticales no dice las alturas de entrepiso, que es el dato que SOLO el corte da.
    check("el corte lleva sus ejes con burbuja",
          'P("CORTE_CON_EJES", "SI",' in cfgp
          and "private void DibujarEjesDelCorte(" in cortedib
          and "public List<(string Id, double Ordenada)> Ejes { get; } = new();" in dto
          and "c.Ejes.Add((e.Id, e.Ordenada));" in codigo)
    check("y se acota en las dos direcciones, con la misma cota de la planta",
          'P("CORTE_ACOTAR", "SI",' in cfgp
          and "private void AcotarElCorte(" in cortedib
          and "CotaAlineada(" in cortedib
          and "las alturas, que es lo que solo el corte dice" in cortedib)
    # Y LOS EJES DEL CORTE SON LOS PERPENDICULARES al del corte: los que se cruzan.
    check("los ejes del corte son los perpendiculares",
          "(q.EnX ? ejesY : ejesX).Select(x => (x.Id, x.Ordenada)).ToList()" in codigo)
    # CADA TIPO SE VE DE UNA FORMA, y es lo que hace que un corte se entienda: la columna de
    # nudo a nudo, la trabe que corre a lo largo entera y con su peralte, la que cruza solo de
    # canto, y el muro como el paño que es.
    check("cada pieza del corte se ve como toca",
          "La <b>trabe o cadena que corre A LO LARGO</b>" in corte
          and "var deCanto = largoBarra <=" in corte
          and "el.Clase == ClasePlanta.Muro" in corte)
    # LAS COTAS DE NIVEL: sin ellas un corte es un monton de rectangulos.
    check("el corte lleva sus lineas de nivel y su rotulo",
          "private void DibujarNivelesDelCorte(" in cortedib
          and "private void RotularElCorte(" in cortedib
          and 'P("CORTE_ROTULO", "CORTE  POR  EL  EJE  %E"' in cfgp)
    # LAS MISMAS CAPAS que la planta: asi apagar E-MURO apaga el muro en los dos dibujos.
    check("el corte usa las capas de la planta, no unas propias",
          'ClasePlanta.Muro => _capas.CapaDeTipo("MURO"),' in cortedib)
    # LA CAPA DEL ALZADO SALE DE LAS NOTAS: se pidio que una cadena de cerramiento o de
    # desplante vaya a las capas de las cadenas y una trabe a E-TRABE. Antes la capa salia solo
    # de la CLASE, asi que todas las barras horizontales caian en E-TRABE y el corte no
    # coincidia con la planta.
    check("la capa del corte sale del tipo, o sea de las notas",
          "private string CapaDeLaPieza(CorteEnAlzado.Pieza p)" in cortedib
          and "return _capas.CapaDeTipo(p.Tipo);" in cortedib
          and "var capa = CapaDeLaPieza(p);" in cortedib)
    # LA LOSA SE DIBUJA: en la extruida se veia y en el plano no aparecia porque se descartaba.
    check("la losa da su franja en el corte",
          "if (el.Clase == ClasePlanta.Losa)" in corte
          and "zArriba - espesor" in corte)
    # UN CORTE ES UNA VISTA: se dibuja tambien lo que queda DETRAS, y a trazos para no
    # confundirlo con lo cortado.
    check("el corte dibuja lo que se ve al fondo, y a trazos",
          'P("CORTE_VER_EL_FONDO", "SI",' in cfgp
          and "public static bool AlFondo(" in corte
          and "private void ALineaDeFondo(" in cortedib
          and "if (!p.Cortada)" in cortedib)
    # LA TRABE, COMPLETA: el concreto llega a la CARA de sus apoyos, no a su eje. Dibujada a
    # ejes deja un hueco en cada punta justo donde mas concreto hay.
    check("la trabe del corte se dibuja completa, hasta la cara de sus apoyos",
          "public static double MedioApoyoEn(" in corte
          and "min - mediaA" in corte
          and "largoBarra + mediaA + mediaB" in corte)
    check("hay prueba ejecutable de la losa, el fondo y la trabe completa",
          "la losa da su franja en el corte" in pre
          and "una columna a 4 m detras del corte se ve al fondo" in pre
          and "la trabe llega a la cara de sus dos apoyos" in pre
          and "medio apoyo donde no hay nada es cero" in pre)
    # LA Z LLEGA HASTA EL DTO: en planta no se usa, pero un corte es un alzado.
    check("el elemento lleva su cota para el corte",
          "public double Z1 { get; set; }" in dto
          and "Z1 = el.Z1," in codigo)
    # Y SE DIBUJA EL QUE ESTE ELEGIDO, con TODOS los niveles: un corte atraviesa el edificio.
    # Ahora se dibujan LOS CORTES PEDIDOS -uno o varios-, cada uno con todos los niveles y
    # corrido para no encimarse con el anterior.
    check("se dibujan los cortes pedidos, con todos los niveles",
          "private int DibujarCorteElegido(" in codigo.replace("DibujarElCorteElegido", "DibujarCorteElegido")
          and "Eje = q.Id," in codigo
          and "foreach (var el in _modeloEtabs.Elementos)" in codigo
          and "total += dibujante.DibujarCorte(c, 0, 0);" in codigo)
    # Y EL DE LA LISTA SIGUE FUNCIONANDO cuando no se escribe nada: quien solo quiere un corte no
    # tiene que aprenderse la sintaxis nueva.
    check("y el de la lista sigue valiendo si no se escribe nada",
          "if (cortes.Count == 0 && _vista.CorteEje.Length > 0)" in codigo
          and "_vista.CorteEje, _vista.CorteEnX, _vista.CorteOrdenada, false));" in codigo)
    check("hay prueba ejecutable del corte",
          "del corte salen TRES piezas" in pre
          and "la trabe se ve entera: 4 m mas medio castillo" in pre
          and "la que cruza se ve solo de canto" in pre
          and "con espesor 0 se usa el minimo, no se queda vacio" in pre)

    check("y las rayitas quedan como ULTIMO recurso, avisando de que no son un hatch",
          "eso NO es un hatch" in mac
          and "private int RayarAMano(" in mac)
    check("el achurado sigue naciendo NO asociativo primero, para sobrevivir al molde",
          "private void Borrar(" in mac)
    # EL RESPALDO DEL ANSI37: un hatch puede fallar por tres motivos que no se ven desde
    # aqui -que el patron no este en el acad.pat del usuario, que MAXHATCH lo rechace por
    # denso, o que la version no lo acepte sobre un contorno recien hecho- y en los tres el
    # voladizo se quedaba SIN MARCAR. Si falla, se raya a mano: lineas a 45 grados
    # recortadas al contorno, que se ven y se imprimen igual.
    # EL ACHURADO SE TIENE QUE VER. El ANSI37 lleva sus lineas a 0.125 de unidad, asi que
    # con la escala literal de la macro -0.0475- la separacion real queda en 5.9 MILIMETROS:
    # en un tablero de 6 x 12 m son mas de dos mil lineas y no se ve un rayado, se ve una
    # MANCHA GRIS uniforme, y en el color 252 parece una sombra. La escala se saca al reves,
    # de la separacion que se quiere ver.
    # LA ESCALA Y EL COLOR LOS DIO EL USUARIO: escala 0.0475 -la de la macro- y color 142. El
    # automatico de la escala se queda disponible pero APAGADO: manda el valor de la macro.
    # Y el color va POR OBJETO, porque los dos datos conviven asi: la capa E-VOLADO en 252
    # -el gris del contorno- y el rayado en 142. Por capa habria que elegir uno de los dos.
    check("el achurado va con la escala de la macro y en color 142",
          'P("LOSA_HATCH_ESCALA", "0.0475"' in cfgp
          and 'P("LOSA_HATCH_ESCALA_AUTO", "NO",' in cfgp
          and 'P("LOSA_HATCH_COLOR", "142",' in cfgp
          and "private int ColorDelAchurado()" in mac
          and "h.Color = ColorDelAchurado();" in mac
          and "EscalaDelHatchDeLosa()," in dib)
    # El automatico se queda, por si algun dia se dibuja en otras unidades.
    check("y el automatico de la escala sigue disponible",
          "public static double EscalaDeHatch(" in dib
          and "separacionM > 0.005 ? separacionM / 0.125 : escalaHoja;" in dib
          and 'P("LOSA_HATCH_SEPARACION_CM", "25",' in cfgp)
    # EL COLOR TAMBIEN EN LAS OTRAS DOS VIAS: el comando lo crea por capa, asi que se le pone
    # despues; y las rayitas del ultimo recurso tienen que verse igual que el hatch.
    check("el color 142 se pone en las tres vias del achurado",
          "((dynamic)hecho).Color = ColorDelAchurado();" in mac
          and "((dynamic)raya).Color = ColorDelAchurado();" in mac)
    check("hay prueba ejecutable de la escala y del color del achurado",
          "la escala del ANSI37 es la de la macro" in pre
          and "el color del achurado es el 142, por objeto" in pre
          and "y el automatico va apagado" in pre)
    # LAS NOTAS SON DE LA PROPIEDAD, no del paño: si el volado comparte seccion con el
    # entrepiso, TODOS los paños de esa seccion salen achurados. Eso se arregla en ETABS, no
    # aqui, y sin la nota no habia manera de verlo.
    check("se avisa de que seccion se tomo por voladizo y por que",
          "private void AvisarDelVolado(" in dib
          and "_voladosAvisados" in dib
          and "dale su propia propiedad de losa en ETABS" in dib)
    # Y EL NOMBRE «VOLADO» NO SE ESCRIBE NUNCA, venga de las notas o del nombre de la
    # seccion: el nombre que se iba a poner sale de la seccion, asi que se comprueba tambien
    # sobre el uso ya resuelto.
    check("y la palabra se busca tambien en el nombre ya resuelto, por si acaso",
          "LosaEnPlanta.PalabraVolado(uso, null, PalabrasDeVolado())" in dib
          and "private string PalabrasDeVolado()" in dib)

    check("si el patron no se puede aplicar, el volado se raya a mano",
          "private int RayarAMano(" in mac
          and "IReadOnlyList<(double X, double Y)>? paraRayar = null," in mac
          and "alPano, x0, y0);" in dib
          and "LosaEnPlanta.Cortes(girado, y, false)" in mac)

    # ------------------------------------------------------------------
    # EL HATCH HASTA EL PAÑO, Y VARIOS VOLADOS COMO UN SOLO PAÑO
    # ------------------------------------------------------------------
    #  En el modelo la losa llega al EJE del muro, porque ahi estan los nudos. Pero el concreto
    #  de la losa llega al PAÑO: medio espesor antes ya es muro, y el rayado se metia por dentro
    #  de la cadena.
    pano = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/PanoDeLosa.cs"))

    check("el molde del achurado se mete hasta el pano del muro",
          "public static List<(double X, double Y)> AlPano(" in pano
          and "public static double MedioAnchoDelMuro(" in pano
          and '_cfg.Bandera("LOSA_HATCH_AL_PANO", true) && huellas.Count > 0' in dib
          and "PanoDeLosa.AlPano(el.Vertices, huellas)" in dib)
    # LAS ESQUINAS SE RECALCULAN CORTANDO los lados movidos: moviendo los vertices uno a uno,
    # un lado con muro y otro sin muro dejarian la esquina abierta o cruzada.
    #
    # Lo que se corta son el ULTIMO tramo de un lado y el PRIMERO del siguiente, que son los
    # pedazos que de verdad llegan a la esquina.
    check("y las esquinas se recalculan cortando los lados, no moviendo vertices",
          "private static (double X, double Y)? Cruce(" in pano
          and "var corte = Cruce(lados[i].Tramos[^1].Recta, lados[j].Tramos[0].Recta);" in pano)

    # ------------------------------------------------------------------
    # EL HATCH LLEGA HASTA LA LINEA DE LA LOSA
    # ------------------------------------------------------------------
    #  Esto era un fallo que se veia en el plano. Se metia el LADO ENTERO en cuanto habia
    #  cualquier huella paralela debajo, asi que un lado de 4 m con una cadena de solo 2 m se
    #  metia completo: en los 2 m libres el achurado del voladizo quedaba a 7.5 cm de la linea
    #  de la losa, con una franja en blanco entre los dos. Se pidio que el hatch llegue hasta el
    #  pano de la losa, o sea hasta su linea.
    #
    #  Ahora el lado se parte en TRAMOS y solo se mete el que tiene muro debajo, asi que el
    #  contorno del molde escalona.
    check("el molde del hatch se mete por tramos, no el lado entero",
          "private static Lado DelLado(" in pano
          and "private sealed record Tramo(" in pano
          and "cubren.Add((desde, hasta, Math.Min(h.PeralteM / 2, maximo)));" in pano
          # Los cortes son los extremos de las huellas proyectados sobre el lado.
          and "cortes.Add(desde)" in pano
          and "cortes.Add(hasta)" in pano)
    # Y UN MURO PERPENDICULAR QUE SOLO TOCA LA ORILLA NO CUENTA: no esta debajo de esa orilla.
    check("un muro perpendicular no mete el pano",
          'Math.Abs((hx * dx) + (hy * dy)) < 0.98' in pano)

    #  DOS VOLADIZOS PEGADOS SON UN SOLO PAÑO: la raya del medio es la orilla que comparten, y
    #  en la obra no existe -el concreto es continuo-. Casi siempre es una losa partida en dos
    #  por un eje, porque en el modelo hace falta el nudo.
    check("varios volados juntos se dibujan con un solo perimetro",
          'P("VOLADO_SIN_DIVISIONES", "SI",' in cfgp
          and "public static bool ContornoCompartido(" in pano
          and "PanoDeLosa.ContornoCompartido(t, vecinas)" in dib
          and "private List<IReadOnlyList<(double X, double Y)>> OtrosVolados(" in dib)
    # SE RECONOCEN TODOS ANTES DE DIBUJAR EL PRIMERO: descubriendolos por el camino, la primera
    # losa dibujaria su raya -aun no sabe de la segunda- y la segunda ya no. Media junta.
    check("y se conocen todos antes de dibujar el primero",
          "_voladosDeLaPlanta.Clear();" in dib
          and "_voladosDeLaPlanta.Add((ClaveDelPano(el), el.Vertices));" in dib)
    # LA OTRA MITAD: el ORIGEN del patron, el mismo para todos, o la junta se sigue viendo
    # porque las lineas de un pano no continuan en el otro.
    check("el patron arranca del mismo origen en todos los paños",
          "h.Origin = new[] { 0d, 0d };" in mac)
    check("hay prueba ejecutable del pano y de los volados pegados",
          "la cadena de 15 mete el pano 7.5 cm" in pre
          and "una cadena perpendicular no mete el pano" in pre
          and "la orilla compartida con el otro volado se reconoce" in pre
          and "se reconoce por la orilla, no por los vertices" in pre)

    # ------------------------------------------------------------------
    # EL TIPO DE LA TABLA, POR LAS NOTAS DE LA PROPIEDAD
    # ------------------------------------------------------------------
    #  Es lo que se pidio y la respuesta a «como puedo hacer que los clasifiques como tipos»:
    #  con las NOTAS. El nombre de la seccion cambia de obra en obra y las medidas se equivocan
    #  en los casos de frontera -una de 15x23.5 pasa de 20 cm, asi que por medidas sale COLUMNA
    #  aunque en obra sea un castillo-. Las notas son el unico sitio donde se dice lo que ES.
    secs_tipo = leer(ruta("client/src/CadLink.Etabs/SeccionesModelo.cs"))
    lec_tipo = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))

    check("el tipo sale de las notas de la propiedad",
          "public static string TipoDeLasNotas(" in secs_tipo
          and "var deLasNotas = TipoDeLasNotas(notas);" in secs_tipo
          and "ClasificaTipo(e.Clase, e.Seccion, t2, t3, op, e.Notas)" in secs_tipo)
    # EL ORDEN NO ES ALFABETICO, es de lo mas especifico a lo mas general: «CONTRATRABE»
    # contiene TRABE, y «CASTILLO AHOGADO EN COLUMNA» contiene las dos.
    check("y lo mas especifico se pregunta primero",
          '("CONTRATRABE", "CONTRATRABE"),' in secs_tipo
          and secs_tipo.index('("CASTILLO", "CASTILLO")')
          < secs_tipo.index('("COLUMNA", "COLUMNA")'))
    # Y LAS NOTAS DE LOS MARCOS, QUE NO SE LEIAN: solo se leian las de muros y losas.
    check("las notas de las secciones de marco ya se leen",
          "private static string NotasDe(object?[] a) =>" in lec_tipo
          and "Material(a), NotasDe(a)) : null;" in lec_tipo
          and "e.Notas = dims.Notas;" in lec_tipo)
    # La prueba de las secciones vive en su propio archivo; aqui se relee, porque las
    # variables de arriba son de otra funcion.
    prs_tipo = leer(ruta("tools/prueba-secciones-modelo/Program.cs"))

    check("hay prueba ejecutable del tipo por notas",
          "CASTILLO en las notas manda" in prs_tipo
          and "CONTRATRABE no se confunde con TRABE" in prs_tipo
          and "por medidas, la de 15x23.5 sale COLUMNA" in prs_tipo)
    check("E-LOSA se queda apagada y E-VOLADO encendida",
          "private void ApagarCapasDeLosa()" in mac
          and "lay.LayerOn = false;" in mac
          and "public IReadOnlyList<string> CapasApagadas()" in capp
          and "ApagarCapasDeLosa();" in dib)
    # EL CONTORNO, SOLO POR FUERA: donde la losa apoya, su pano y el del muro son la misma
    # linea, y dibujarla deja una raya en medio del muro que se lee como una junta.
    check("el contorno de la losa no se dibuja dentro del muro ni de la cadena",
          "public static List<Segmento> TramosFuera(" in los
          and '_cfg.Bandera("LOSA_CONTORNO_FUERA_DE_MUROS", true)' in dib
          and "LosaEnPlanta.Lados(el.Vertices)" in dib)
    check("hay prueba ejecutable de la losa",
          "y el pano SI esta volado" in pre
          and "dos cadenas traslapadas cubren el lado una sola vez" in pre
          and "en la vertical del quiebre, UN tramo y no dos" in pre
          and "un lado entero dentro del muro no se dibuja" in pre)

    # ------------------------------------------------------------------
    # EL MURO BAJO LA CADENA NO SE DIBUJA
    # ------------------------------------------------------------------
    #  Es MarcarMurosTapados. En el modelo el muro y su cadena de cerramiento ocupan LA MISMA
    #  linea en planta, asi que dibujando los dos salen DOS parejas de lineas pegadas: eso
    #  era la raya de mas a cada lado de cada cadena. Se dejan SOLO los muros SIN cadena, que
    #  son los que hay que revisar.
    mbc = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/MuroBajoCadena.cs"))
    check("el muro que va debajo de una cadena no se dibuja",
          "public static class MuroBajoCadena" in mbc
          and "public static Estado Como(" in mbc
          and '_cfg.Bandera("OCULTAR_MURO_BAJO_CADENA", true)' in dib
          and "MuroBajoCadena.Como(" in dib
          and "if (!tapado)" in dib)
    check("con los numeros de la hoja: traslape, tolerancia y que las trabes no cuentan",
          '_cfg.Numero("TRASLAPE_MINIMO", 0.8)' in dib
          and '_cfg.Numero("TOLERANCIA_CADENA_CM", 10)' in dib
          and '_cfg.Bandera("CADENA_INCLUYE_TRABES", false)' in dib
          and "public static bool EsCadena(" in mbc)
    # LA COBERTURA POR UNION, no sumando: dos cadenas traslapadas cubren su tramo una vez.
    check("la cobertura se mide por union de tramos",
          "LosaEnPlanta.Unidos(tramos).Sum(t => t.B - t.A) / largo" in mbc)
    # Y LA MAMPOSTERIA SE QUEDA aunque el muro no se dibuje: es la marca de que ahi va block.
    check("la linea de mamposteria se dibuja aunque el muro este tapado",
          '_cfg.Bandera("MAMPOSTERIA_AUNQUE_TAPADO", true)' in dib)
    # El ancho de la cadena que lo tapa -el eTapaB de la macro- separa el rotulo del pier.
    check("el pier se separa de la cadena que tapa al muro",
          "_anchoDeLaCadena" in dib
          and "public readonly record struct Estado(bool Tapado, double Cobertura, double AnchoCadena)"
              in mbc)
    check("hay prueba ejecutable del muro tapado",
          "el muro con su cadena encima queda TAPADO" in pre
          and "un muro SIN cadena no esta tapado" in pre
          and "una TRABE no tapa el muro por omision" in pre)

    # ------------------------------------------------------------------
    # EL NOMBRE DE LA LOSA: EL DE LA SECCION, SIN LA PALABRA «LOSA»
    # ------------------------------------------------------------------
    #  El renglon ya dice «Losa de», asi que la seccion «LOSA VOLADO» se rotula «Losa de
    #  VOLADO». Sirve para cualquier nombre que use, sin apuntarlo en la hoja.
    check("el rotulo de la losa toma el nombre de la seccion sin la palabra LOSA",
          "public static string SinLaPalabraLosa(" in dib
          and "var deLaSeccion = SinLaPalabraLosa(el.Seccion);" in dib)
    check("y si de la seccion no queda nada, mandan las palabras de la hoja",
          '_cfg.Texto("LOSA_PALABRAS_AZOTEA", "AZOTEA,CUBIERTA,TECHO,ROOF")' in dib
          and '_cfg.Texto("LOSA_USO_POR_OMISION", "ENTREPISO")' in dib)
    check("hay prueba ejecutable del nombre de la losa",
          '«LOSA VOLADO» se rotula VOLADO' in pre
          and 'ni «SLAB 10»' in pre)

    # ------------------------------------------------------------------
    # EN LA BASE, LOS ARRANQUES DE CASTILLOS
    # ------------------------------------------------------------------
    #  En el modelo la columna que va del suelo al primer piso pertenece al piso de ARRIBA,
    #  asi que la planta de cimentacion salia sin un solo arranque. Y con la regla que se
    #  pidio: sin muros que arranquen ahi, no se dibuja ninguno.
    check("en la cimentacion se traen los arranques que desplantan en la base",
          "private void AgregarArranquesDeCimentacion(" in codigo
          and '_cfg is not None' not in codigo
          and 'CfgPlano.Bandera("CIMENTACION_DIBUJA_COLUMNAS", true)' in codigo
          and 'CfgPlano.Numero("CIMENTACION_COLUMNA_TOL_CM", 20)' in codigo)
    check("y sin muros, sin castillos",
          'CfgPlano.Bandera("CIMENTACION_SIN_MUROS_SIN_COLUMNAS", true)' in codigo
          and "var hayMuros = p.Elementos.Any(e => e.Clase == ClasePlanta.Muro)" in codigo)
    # La traduccion del elemento en UN solo sitio: hace falta dos veces -al recorrer el
    # nivel y al traer los arranques- y duplicarla era garantia de que a uno le faltara un
    # dato.
    check("la traduccion del elemento esta en un solo metodo",
          "private ElementoPlanta ComoElementoDePlanta(" in codigo
          and codigo.count("p.Elementos.Add(ComoElementoDePlanta(el, modelo));") == 2)

    # ------------------------------------------------------------------
    # EL ANCHO DEL MTEXT, AUTOMATICO
    # ------------------------------------------------------------------
    #  Width = 0 es «sin ancho definido»: la caja sigue al texto. Hace falta porque al
    #  centrar se centra LA CAJA, y con una caja mas ancha que el texto el rotulo se veia
    #  gordo y corrido respecto a la trabe. Con respaldo: medir el texto ya dibujado.
    check("el ancho del MTEXT es automatico",
          "private void AnchoAutomatico(" in dib
          and "((dynamic)mt).Width = 0d;" in dib
          and "AnchoAutomatico(mt, texto, altura);" in dib)
    check("y si la version no acepta el 0, se mide el texto y se le da su ancho",
          "var caja = CajaEnvolvente(mt);" in dib
          and "((dynamic)mt).Width = medido + (altura * 0.1);" in dib)
    # El anclaje va DESPUES del ancho: cambiar la caja mueve el texto.
    check("el anclaje se pone despues del ancho",
          dib.find("AnchoAutomatico(mt, texto, altura);")
          < dib.find("mt.AttachmentPoint = anclaje;"))

    # ------------------------------------------------------------------
    # LA SECCION DE ACERO, DIBUJADA COMO ES
    # ------------------------------------------------------------------
    #  Antes TODO lo que no era redondo salia como rectangulo, asi que una IR de 25x15 y un
    #  cajon de 25x15 se dibujaban igual: en el plano no habia forma de distinguir el acero
    #  del concreto. Ahora se traza el perfil con sus espesores.
    sec = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/SeccionEnPlanta.cs"))
    check("hay geometria de perfiles para la planta",
          "public static class SeccionEnPlanta" in sec
          and "public static double[] Contorno(" in sec
          and "private static double[] PerfilI(" in sec
          and "private static double[] Canal(" in sec
          and "private static double[] Te(" in sec
          and "private static double[] Angulo(" in sec)
    check("el cajon y el tubo llevan su hueco",
          "public static double[] Hueco(" in sec
          and "public static double RadioInterior(" in sec
          and "ht.AppendInnerLoop(" in dib
          and "ht.AppendInnerLoop(" in mac)
    # SIN ESPESORES NO HAY PERFIL: mejor una caja honesta que una I inventada, que se
    # acotaria mal.
    check("sin espesores se cae al rectangulo, no se inventa el perfil",
          "return Rectangulo(b, h);" in sec
          and "private static bool Valen(" in sec)
    check("el perfil se usa en el bloque y en el camino suelto",
          "SeccionEnPlanta.Contorno(\n                        forma, b, h, el.PatinM, el.AlmaM, el.ParedM)" in mac
          and "SeccionEnPlanta.Contorno(el.Forma, b, h, el.PatinM, el.AlmaM, el.ParedM)" in dib)
    check("los espesores llegan del modelo hasta el dibujante",
          "public double PatinM { get; set; }" in dto
          and "public double AlmaM { get; set; }" in dto
          and "public double ParedM { get; set; }" in dto
          and "PatinM = el.PatinM," in codigo
          and "AlmaM = el.AlmaM," in codigo
          and "ParedM = el.ParedM" in codigo)
    # Un SOLID solo cubre un cuadrilatero CONVEXO, y una I no lo es: el relleno de respaldo
    # va por las PIEZAS de la seccion -los dos patines y el alma-.
    check("el relleno de respaldo va por las piezas de la seccion",
          "public static List<double[]> RectangulosDeRelleno(" in sec
          and "SeccionEnPlanta.RectangulosDeRelleno(" in dib
          and "SeccionEnPlanta.RectangulosDeRelleno(" in mac
          and "private void SolidoGirado(" in dib)
    # En un perfil no van las diagonales: la forma ya dice lo que es, y taparian el alma.
    check("un perfil de acero no lleva las diagonales de la columna",
          "if (!PlanoEstructural.CapasPlano.EsPerfilAcero(el.Forma))" in dib)
    check("la redonda se dibuja con circunferencias, no con un poligono",
          "private bool SeccionRedonda(" in dib
          and "SeccionEnPlanta.EsRedonda(el.Forma)" in dib)
    check("hay prueba ejecutable del perfil de acero",
          'SeccionEnPlanta.Contorno("I", bI, hI, tfI, twI)' in pre
          and "su area es la de dos patines y un alma" in pre
          and "y sus areas suman la del perfil" in pre)

    # LAS COLUMNAS, COMO BLOQUE Y RELLENAS. El bloque se llama como la SECCION, que es lo
    # que permite cambiar de golpe las 30 columnas de una seccion con un BLOCKREPLACE; y el
    # giro va en la INSERCION, que es lo que hace que el reemplazo conserve la orientacion.
    check("la columna se inserta como bloque",
          "private bool ColumnaComoBloque(" in mac
          and "_ms.InsertBlock(" in mac
          and "ColumnaComoBloque(el, cx, cy, b, h)" in dib)
    check("el bloque se llama como la seccion",
          '_cfg.Bandera("BLOQUE_NOMBRE_SECCION", true)' in mac
          and "internal static string LimpiaNombreDeBloque(string s)" in mac)
    check("el giro va en la insercion, no en la geometria",
          '_cfg.Numero("BLOQUE_ROTACION_EXTRA_GRADOS", 0)' in mac)
    # EL GIRO ES EL DEL MODELO. Con 0 todas las columnas salian derechas y una 20x60 girada
    # 90 grados se veia de 20x60 en lugar de 60x20: el plano no coincidia con ETABS.
    check("y el giro es el del modelo, no cero",
          "var grados = el.AnguloGrados + _cfg.Numero(\"BLOQUE_ROTACION_EXTRA_GRADOS\", 0);"
          in mac
          and "public double AnguloGrados { get; set; }" in dto
          and "AnguloGrados = el.AnguloGrados," in codigo)
    check("y va RELLENA, con el color de la hoja",
          '_cfg.Bandera("RELLENAR_COLUMNAS", true)' in mac
          and 'blk.AddHatch(0, "SOLID", true, 0)' in mac
          and "var color = ColorDelRelleno();" in mac
          and '_cfg.Numero("COLOR_RELLENO_BLOQUE", 2)' in dib)
    # EL RESPALDO CON SOLID: un AddHatch dentro de una DEFINICION de bloque falla en varias
    # versiones -el achurado quiere un contorno que ya este en la base de datos- y la columna
    # se quedaba hueca. Un SOLID de cuatro puntos siempre se puede crear.
    check("si el achurado no se deja, el relleno va con un SOLID",
          "private void RellenarDentroDelBloque(" in mac
          and "blk.AddSolid(" in mac
          and "private void RellenarEnPlanta(" in dib
          and "_ms.AddSolid(" in dib)
    # Los cuatro puntos de un SOLID van CRUZADOS: en orden circular sale un monio.
    check("los cuatro puntos del SOLID van cruzados, no en orden circular",
          "new[] { r[0], r[3], 0d },\n                    new[] { r[2], r[3], 0d })" in mac
          and "new[] { p[6], p[7], 0d },\n                    new[] { p[4], p[5], 0d })" in dib)
    # LA SECCION SUELTA, IGUAL DE FIEL: girada y rellena. Antes este camino dibujaba un
    # rectangulo derecho y hueco, asi que cuando el bloque fallaba el plano salia sin
    # orientacion y sin relleno, y sin decir por que.
    check("el camino sin bloque tambien sale girado y relleno",
          "public static double[] EsquinasGiradas(" in dib
          and "SeccionEnPlanta.Colocar(local, cx, cy, el.AnguloGrados)" in dib
          and "RellenarEnPlanta(pl, plHueco, el, cx, cy, b, h, capa);" in dib)
    check("hay prueba ejecutable del giro de la seccion",
          "PlantaDrawer.EsquinasGiradas(0, 0, 0.20, 0.60, 90)" in pre
          and "a 90 grados mide 0.60 de ancho" in pre)
    check("un bloque que ya existe se respeta salvo que la hoja diga lo contrario",
          '_cfg.Bandera("REDEFINIR_BLOQUES", true)' in mac)
    check("y si algo falla se dibuja la seccion suelta, no se pierde la columna",
          "if (ColumnaComoBloque(el, cx, cy, b, h))" in dib)

    # LEER LOS EJES NO PUEDE TIRAR LA LECTURA. Se probo con Com.Get y rompio SAP2000: al
    # pedir «GridSys» salta una excepcion propia -no un fallo de COM- que subia y se llevaba
    # el modelo completo. Los ejes son opcionales: si no se leen, se deducen.
    lect_gs = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("pedir la cuadricula no puede tirar la lectura del modelo",
          'gridSys = Com.TryGet(cx.SapModel, "GridSys");' in lect_gs
          and 'Com.Get(cx.SapModel, "GridSys")' not in lect_gs)

    # LOS EJES DEL MODELO, con su respaldo: GetGridSys_2 no esta en todas las versiones.
    lect_ej = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("la cuadricula se lee del modelo",
          "private static void LeerEjes(EtabsConnection cx, ModeloEtabs m)" in lect_ej
          and '"GetGridSys_2"' in lect_ej)
    check("y si no se puede, se deduce de las columnas y los muros",
          "public static EjesModelo DesdeGeometria(" in leer(
              ruta("client/src/CadLink.Etabs/EjesModelo.cs"))
          and "EjesModelo.DesdeGeometria(modelo)" in codigo)
    check("los verticales se numeran y los horizontales se letran",
          "public static string Letra(int i)" in leer(
              ruta("client/src/CadLink.Etabs/EjesModelo.cs")))

    # Y su prueba ejecutable, leída más arriba.
    check("hay prueba ejecutable de los ejes, las cotas y el rotulo",
          "using CadLink.Cad.PlanoEstructural;" in pre
          and "ejes.SaleEjes()" in pre
          and "ejes.Cotas(" in pre
          and "rot.NombreDeNivel(" in pre
          and "return fallos == 0 ? 0 : 1;" in pre)
    check("y comprueba que mover las cotas NO mueve las burbujas",
          "mover la cota total NO mueve las burbujas" in pre)

    # LOS ROTULOS, DONDE LOS PONE LA MACRO: la columna en la esquina superior derecha y la
    # trabe girada a lo largo de la barra. Todos al centro y horizontales era lo que
    # convertia cada nudo en un borron.
    check("el rotulo de la columna va a la esquina, con su separacion",
          '_cfg.Numero("COLUMNA_TEXTO_SEPARACION_CM", 2)' in dib)
    check("el de la trabe va girado a lo largo de la barra",
          "public static double AnguloLegible(double dx, double dy)" in dib
          and "ang -= 180;" in dib and "ang += 180;" in dib
          and "var ang = AnguloLegible(dx, dy);" in dib)
    check("y el del muro corrido al lado con PIER_SEPARACION_CM",
          '_cfg.Numero("PIER_SEPARACION_CM", 6)' in dib)
    check("el MText acepta giro",
          "double giroGrados = 0," in dib
          and "mt.Rotation = giroGrados * Math.PI / 180;" in dib)

    # ------------------------------------------------------------------
    # EL MTEXT DE VERDAD: CON SU ESTILO, SU ANCHO Y SU FONDO
    # ------------------------------------------------------------------
    #  Los rotulos no aparecian. Dos motivos, los dos aqui:
    #    1) el MTEXT se creaba con ancho 0, y con ancho 0 hay versiones que crean el objeto
    #       y no lo muestran; ademas AttachmentPoint necesita caja con ancho para centrar.
    #    2) el estilo se asignaba y, si no existia en el dibujo, se perdia en silencio.
    #       Ahora se CREA el que falte, que es lo que pidio el usuario.
    check("el MTEXT se crea con ancho, nunca con 0",
          "duenio.AddMText(new[] { x, y, 0d }, ancho, texto)" in dib
          and "var ancho = Math.Max(1, letras) * altura * 0.62;" in dib)
    # EL DUEÑO por omision es el espacio modelo; solo el rotulo de la losa pasa un BLOQUE.
    check("y su dueño por omision sigue siendo el espacio modelo",
          "dynamic duenio = dentroDe ?? _ms;" in dib)
    check("el estilo del rotulo se crea si el dibujo no lo tiene",
          "private void AsegurarEstiloDeTexto(string nombre)" in dib
          and "AsegurarEstiloDeTexto(nombreEstilo);" in dib
          and "_estilosVistos" in dib)
    check("y el estilo va ANTES de la altura, porque el de la macro trae altura fija",
          dib.find("mt.StyleName = nombreEstilo;") < dib.find("mt.Height = altura;"))
    check("el rotulo de la cadena lleva FONDO opaco, como en la macro",
          "mt.BackgroundFill = true;" in dib
          and '_cfg.Bandera("CADENA_TEXTO_FONDO", true)' in dib)
    check("cada familia de rotulos va con SU estilo y SU altura de la hoja",
          '_cfg.Texto("SEC_ESTILO_TEXTO", "TEXTO_SECCIONES")' in dib
          and '_cfg.Texto("CADENA_ESTILO_TEXTO", "TEXTO_CADENAS")' in dib
          and '_cfg.Texto("LOSA_ESTILO_TEXTO", "TEXTO_LOSAS")' in dib
          and "private double AlturaSecciones(double respaldo)" in dib
          and "private double AlturaCadenas(double respaldo)" in dib
          and "private double AlturaLosas(double respaldo)" in dib)
    # La columna se ancla por su esquina INFERIOR IZQUIERDA -la alineacion 12 de la macro-,
    # asi el texto crece hacia arriba y a la derecha y no se mete sobre la seccion.
    check("el rotulo de la columna se ancla por su esquina, no centrado",
          "int anclaje = 5," in dib and "mt.AttachmentPoint = anclaje;" in dib
          and "EstiloSecciones, false, 7)" in dib)

    # ------------------------------------------------------------------
    # EL MURO SE ROTULA CON SU PIER, EN LA CAPA PIERS
    # ------------------------------------------------------------------
    #  Antes se caia a la etiqueta -el nombre de la propiedad- y la planta salia con
    #  «MURO TABICON 2 APLANADOS 15 CM» escrito 31 veces. Si no hay pier, no hay rotulo.
    check("el muro se rotula con su PIER y nada mas",
          "ClasePlanta.Muro => PierDelMuro(el)," in dib
          and "private static string PierDelMuro(ElementoPlanta el)" in dib
          and "public string Pier { get; set; }" in dto)
    check("y el pier va en la capa PIERS, no en la de los textos",
          "_capas.CapaPiers, ang, EstiloSecciones);" in dib)
    check("el pier llega desde la ventana",
          "Pier = el.Pier," in codigo)

    # ------------------------------------------------------------------
    # EL PRIMER Y EL ULTIMO EJE, AL PANO EXTERIOR DEL MURO
    # ------------------------------------------------------------------
    #  Solo esos dos de cada direccion: las cotas de orilla se dan al pano -es lo que se
    #  replantea- y las interiores eje a eje. Y manda el MURO sobre la trabe.
    check("los ejes de orilla se corren al pano exterior del muro",
          "public List<(string Id, double Ordenada)> AlPanoExterior(" in ejp
          and "public double MedioAnchoSobreEje(" in ejp
          and '_cfg.Bandera("EJES_EXTREMOS_AL_PANO", true)' in ejp
          and '_cfg.Numero("EJES_PANO_TOL_CM", 25)' in ejp)
    check("manda el muro sobre la trabe",
          "return deTrabe > 0 ? deTrabe : deApoyo;" in ejp
          and "if (deMuro > 0)" in ejp)

    # ------------------------------------------------------------------
    # EL EJE DE ORILLA CON SOLO UN CASTILLO
    # ------------------------------------------------------------------
    #  «El ultimo eje de izquierda a derecha no esta poniendo la cota a pano». En el eje de
    #  orilla muchas veces NO corre ningun muro a lo largo: solo esta el castillo -un
    #  K 15X15- con los muros y las cadenas LLEGANDO a el en perpendicular. Una columna en
    #  planta es un PUNTO, asi que la comprobacion de los dos extremos no le servia, se
    #  devolvia cero, el eje no se corria y la cota de orilla quedaba A EJE.
    check("un castillo sobre el eje de orilla tambien da pano",
          "if (el.Clase == ClasePlanta.Columna)" in ejp
          and "deApoyo = Math.Max(deApoyo, MedioDeApoyo(el, vertical));" in ejp)
    # Basta con que su CENTRO caiga sobre el eje: un punto no tiene dos extremos que mirar.
    check("basta con que su centro caiga sobre el eje",
          "var centro = vertical" in ejp
          and "Math.Abs(el.X1 - ordenada) <= tol" in ejp)
    # EL GIRO CUENTA: se mide la caja que ENVUELVE a la seccion girada, la misma cuenta con
    # la que se coloca su rotulo, asi que da el mismo pano que el dibujo. Un 15x40 girado 90
    # saca pano a 20 cm del eje, no a 7.5.
    check("y su pano se mide con la seccion YA GIRADA",
          "private static double MedioDeApoyo(ElementoPlanta el, bool vertical)" in ejp
          and "? (b / 2 * ca) + (h / 2 * sa)" in ejp
          and ": (b / 2 * sa) + (h / 2 * ca);" in ejp)
    # Sin medidas, los 15x15 del castillo de siempre: el mismo respaldo que en planta.
    check("un castillo sin medidas cae en los 15x15 de siempre",
          "var b = el.AnchoM > 0 ? el.AnchoM : 0.15;" in ejp
          and "var h = el.PeralteM > 0 ? el.PeralteM : b;" in ejp)

    # ------------------------------------------------------------------
    # EL CASTILLO MODELADO COMO SHELL DE MURO
    # ------------------------------------------------------------------
    #  «Los shells de muro que tengan en property note CASTILLO igual hacerlos bloques y
    #  rellenarlos con amarillo como un frame normal, OJO solo si dice CASTILLO». Un castillo
    #  se puede modelar como frame de 15x15 o como shell angosto -lo que sale al dibujarlo
    #  junto con su muro-, y dibujado como muro salia como dos rayas, sin bloque y sin
    #  relleno: la misma cosa se veia de dos formas distintas en el plano.
    cdm = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CastilloDeMuro.cs"))
    dibp = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.cs"))
    corp = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Corte.cs"))
    winp = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    cfgplano = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/ConfigPlano.cs"))
    macp = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.Macro.cs"))
    rot = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/RotuloPlanta.cs"))
    dtop2 = leer(ruta("client/src/CadLink.Cad/PlantaCad.cs"))

    check("el shell de muro que dice CASTILLO se convierte en castillo",
          "public static bool Dice(ElementoPlanta? el)" in cdm
          and "public static ElementoPlanta Como(" in cdm
          and "public static int Normalizar(" in cdm)
    # SOLO SI DICE CASTILLO, y solo los MUROS: una losa o una trabe con esa nota se quedan
    # como estan, y el NOMBRE DE LA SECCION no cuenta -se pidio la property note-.
    check("solo los muros, y solo por el tipo o las notas",
          "el.Clase != ClasePlanta.Muro" in cdm
          and "return DicenLasNotas(el.Tipo, el.Notas);" in cdm
          and "el.Seccion" not in cdm.split("public static bool Dice")[1].split("}")[0])
    # Se dibuja por el camino de la COLUMNA, que es el del bloque con el nombre de la seccion
    # y el relleno SOLID amarillo -COLOR_RELLENO_BLOQUE, el 2- dentro del bloque.
    check("y se dibuja como columna, que es el camino del bloque y el relleno",
          "Clase = ClasePlanta.Columna," in cdm
          and 'Tipo = Palabra,' in cdm
          and 'Forma = "RECT",' in cdm)
    # El segmento con espesor se vuelve una SECCION EN UN PUNTO, girada: el centro es el punto
    # medio, el ancho el largo del shell y el peralte su espesor.
    check("el shell se vuelve una seccion en su centro, girada",
          "AnchoM = b," in cdm
          and "PeralteM = espesor," in cdm
          and "AnguloGrados = largo > Nada ? Math.Atan2(dy, dx) * 180 / Math.PI : 0" in cdm)
    check("con la bandera para apagarlo",
          '_cfg.Bandera("SHELL_CASTILLO_COMO_COLUMNA", true)' in dibp
          and '_cfg.Bandera("SHELL_CASTILLO_COMO_COLUMNA", true)' in corp)
    # ANTES DE LOS APOYOS: si la conversion llegara despues, los muros moririan en el EJE de
    # este castillo en vez de en su pano, y el contorno de la losa se le metaria por dentro.
    check("y se convierte ANTES de calcular los apoyos y las huellas",
          "PlanoEstructural.CastilloDeMuro.Normalizar(" in dibp
          and dibp.index("PlanoEstructural.CastilloDeMuro.Normalizar(")
              < dibp.index("var apoyos = p.Elementos.Where"))
    check("el corte lo normaliza igual, para que no discuta con la planta",
          "PlanoEstructural.CastilloDeMuro.Normalizar(\n                c.Elementos, "
          "EspesorMuroPorOmision," in corp)
    # LA CASILLA que le toca es la de las COLUMNAS: quien apaga los muros para ver solo la
    # estructura de castillos los perderia todos, y en el plano ya no son muros.
    check("en la ventana sigue a la casilla de las columnas",
          "private bool VisibleEnElPlano(ElementoEtabs el)" in winp
          and "CastilloDeMuro.DicenLasNotas(null, el.Notas)" in winp
          and "return VerColumnasPlanoChk.IsChecked == true;" in winp
          and "if (!VisibleEnElPlano(el))" in winp)

    # ------------------------------------------------------------------
    # Y COMPLETO: EL BLOQUE CON SUS MEDIDAS Y LOS PEDAZOS UNIDOS
    # ------------------------------------------------------------------
    #  «Cuando un shell diga castillo en property notes debes ponerlo COMPLETO como bloque».
    #  Eran dos cosas distintas las que lo dejaban incompleto:
    #
    #  1) EL NOMBRE DEL BLOQUE. En un frame la seccion fija las medidas -«K 15X15» mide 15x15
    #     en todo el modelo-, asi que el bloque puede llamarse como ella. En un SHELL no: la
    #     seccion es la propiedad del muro, que solo fija el ESPESOR, y el largo lo pone cada
    #     shell. Llamando al bloque «MURO 15» a secas, el primer castillo creaba la definicion
    #     y todos los demas se insertaban con las medidas de aquel: uno de 15x40 salia 15x15.
    #  2) LOS PEDAZOS. Un castillo de shell casi nunca llega de una pieza: partido a lo alto
    #     -antepecho y dintel- los dos paneles ocupan el mismo sitio en planta y salian DOS
    #     bloques encimados; partido a lo largo, el castillo salia en dos mitades.
    lector = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))

    # ------------------------------------------------------------------
    # SU NOMBRE ES SU MEDIDA: «K 15X23.5»
    # ------------------------------------------------------------------
    #  «Debes leer las property note que dice CASTILLO y solo de esos sacar su dimension en
    #  planta, y ese dato ocuparlo para nombrar su bloque como su etiqueta». Es lo unico que
    #  sirve: la seccion de un shell es la propiedad del MURO -«MURO 15», que no dice nada de
    #  este castillo- y su etiqueta es el PIER, que en SAP2000 no existe, asi que el castillo
    #  salia sin rotulo y con el nombre de un muro. Ahora se nombra con su medida en planta -el
    #  espesor por el largo- y ese nombre va en la SECCION, que es de donde salen el nombre del
    #  BLOQUE y el rotulo de la planta.
    check("el castillo de area se nombra con su medida en planta",
          "public static string Nombre(string? prefijo, double espesorM, double largoM)" in cdm
          and "Etiqueta = Nombre(prefijo, espesor, b)," in cdm
          and "Seccion = Nombre(prefijo, espesor, b)," in cdm)
    # CON PUNTO DECIMAL SIEMPRE: el nombre acaba siendo un nombre de BLOQUE de AutoCAD, y con la
    # coma de la configuracion regional la misma medida daria dos bloques distintos.
    check("con decimales solo si hacen falta y con punto, no con coma",
          '(m * 100).ToString("0.##", CultureInfo.InvariantCulture)' in cdm)
    check("y el prefijo sale de la hoja",
          '_cfg.Texto("SHELL_CASTILLO_PREFIJO", "K")' in dibp
          and 'P("SHELL_CASTILLO_PREFIJO", "K",' in cfgplano)

    # ------------------------------------------------------------------
    # LAS NOTAS DE LA PROPIEDAD DE AREA, EN LOS DOS PROGRAMAS
    # ------------------------------------------------------------------
    #  AQUI ESTABA EL CASTILLO QUE NO SALIA, y son dos problemas con el mismo sintoma -la
    #  propiedad se queda SIN NOTAS, y sin notas nada dice CASTILLO-:
    #
    #  1) EN ETABS, GetWall declara sus dos primeros datos como ENUMERACIONES -eWallPropType y
    #     eShellType-. Pasandole ceros enteros, contra la interfaz del ensamblado es un choque
    #     de tipos y la invocacion revienta antes de leer nada.
    #  2) EN SAP2000 no existen GetWall ni GetSlab: todas las propiedades de area son SHELL.
    #
    #  Las dos se arreglan igual: se le pregunta a la FIRMA REAL como se llama cada parametro y
    #  se piden «Notes» y «Thickness» POR SU NOMBRE, con cada hueco rellenado con un valor
    #  neutro de su tipo, que es lo que hace pasar a las enumeraciones.
    com = leer(ruta("client/src/CadLink.Etabs/ComLateBinding.cs"))

    check("se puede llamar a la OAPI leyendo los parametros por su nombre",
          "public static Dictionary<string, object?>? CallPorNombre(" in com
          and "args[i] = ValorNeutro(ps[i]);" in com
          and "salida[ps[i].Name ?? i.ToString(CultureInfo.InvariantCulture)] = args[i];" in com)
    # Solo si la OAPI devolvio 0: preguntarle a una losa por GetWall no falla, devuelve error, y
    # con la respuesta vacia pareceria que la propiedad no tiene notas.
    check("y solo se acepta si la OAPI devolvio 0",
          "if (r is not null && Convert.ToInt32(r) != 0)" in com)
    check("las notas del area se buscan en los metodos de ETABS y de SAP2000",
          'new[] { "GetWall", "GetWall_1", "GetShell_1", "GetShell" }' in lector
          and '"GetSlab", "GetSlab_1", "GetDeck", "GetShell_1", "GetShell"' in lector
          and 'Com.CallPorNombre(propArea, m, (0, seccion))' in lector)
    check("y se piden por su nombre, no por su posicion",
          'NumeroDe(d, "Thickness", "Depth", "OverallDepth", "TotalDepth")' in lector
          and 'TextoDe(d, "Notes")' in lector
          and 'TextoDe(d, "MatProp")' in lector)
    # Y si no hay ensamblado que preguntar -solo IDispatch-, las firmas de shell a mano, con el
    # texto recogido sin mirar posiciones.
    check("con el respaldo de las firmas a mano por IDispatch",
          '("GetShell_1", Larga(), 5),' in lector
          and '("GetShell", Corta(), 4),' in lector
          and "plantilla.Skip(1)" in lector
          and ".OfType<string>()" in lector)
    check("los pedazos del mismo castillo se unen en uno",
          "public static bool MismoCastillo(" in cdm
          and "public static ElementoPlanta Unido(" in cdm
          and "elementos.RemoveAt(sobran[k]);" in cdm)
    # MISMA DIRECCION, MISMA LINEA Y QUE SE TOQUEN: con eso, dos castillos distintos separados
    # 15 cm no se unen, y las dos mitades de uno si.
    check("y solo si van igual, en la misma linea y se tocan",
          "Math.Abs((ax * by) - (ay * bx)) > 0.10" in cdm
          and "return Math.Min(a2, b2) >= Math.Max(a1, b1) - tol;" in cdm)
    # EL ESPESOR, EL MAYOR -el pano llega al mas saliente- Y LAS COTAS, del mas bajo al mas
    # alto, que es lo que hace que en el corte salga de una pieza.
    check("el unido toma el espesor mayor y las cotas de punta a punta",
          "AnchoM = piezas.Max(x => x.AnchoM)," in cdm
          and "Z1 = piezas.Min(x => Math.Min(x.Z1, x.Z2))," in cdm
          and "Z2 = piezas.Max(x => Math.Max(x.Z1, x.Z2))" in cdm)
    # LA DIRECCION LA PONE LA PIEZA MAS LARGA: tomando la primera, un pedacito de 5 cm dibujado
    # torcido torceria el castillo entero.
    check("y la direccion la pone la pieza mas larga",
          "var guia = piezas.OrderByDescending(Largo).First();" in cdm)
    check("las claves del castillo de shell estan en la hoja CONFIG",
          'P("SHELL_CASTILLO_COMO_COLUMNA", "SI",' in cfgplano
          and 'P("SHELL_CASTILLO_UNIR_TOL_CM", "2",' in cfgplano
          and 'P("SHELL_CASTILLO_DE_OTRO_NIVEL", "SI",' in cfgplano)

    # ------------------------------------------------------------------
    # SOLO DONDE VA DE PISO A TECHO
    # ------------------------------------------------------------------
    #  «Ya lo colocas pero lo duplicas en los niveles, solo debe aparecer en donde sea de piso a
    #  techo». Antes bastaba con que el castillo TOCARA el nivel -20 cm de holgura-, asi que uno
    #  que muere justo en el nivel se dibujaba en su planta y otra vez en la de arriba, donde en
    #  realidad no hay castillo. Ahora tiene que CUBRIR el entrepiso: la fraccion de
    #  MURO_FRACCION_ENTREPISO, que es la regla que ya usaba la macro para saber si un muro es
    #  completo o es un antepecho. Un castillo de tres niveles lo cubre entero en los tres y sale
    #  en las tres plantas, que es lo correcto: en las tres hay castillo.
    check("el castillo de area solo entra donde va de piso a techo",
          'CfgPlano.Numero("MURO_FRACCION_ENTREPISO", 0.75)' in winp
          and "var minimo = n.AlturaM * fraccion;" in winp
          and "var cubre = Math.Min(zMax, zAlta) - Math.Max(zMin, zBaja);" in winp
          and "if (cubre < minimo)" in winp)
    check("y ya no basta con que lo toque",
          "SHELL_CASTILLO_CRUZA_TOL_CM" not in winp
          and "SHELL_CASTILLO_CRUZA_TOL_CM" not in cfgplano)

    # ------------------------------------------------------------------
    # EL ROTULO DE LA PLANTA, SIEMPRE A LA MISMA ALTURA
    # ------------------------------------------------------------------
    #  «Los rotulados de planta estructural deben estar a -5 de los ejes para que sea siempre
    #  uniforme». Hacian falta las dos cosas: la DISTANCIA -ROTULO_SEPARACION_EJES, ahora 5- y el
    #  PUNTO DE PARTIDA, que era el error de bulto: se medía desde la caja de los ELEMENTOS, y
    #  los ejes bajan mas que ella cuando la planta tiene pocas piezas. Por eso en un juego de
    #  tres plantas los tres rotulos salian escalonados y el de la cimentacion, casi vacia,
    #  aparecia arriba del todo, a la altura de los ejes de arriba.
    check("el rotulo se cuelga de donde ACABARON los ejes, no del dibujo",
          "_abajoDeLosEjes = yMin + dy - Ejes.AbajoDeEjes(true);" in macp
          and "var abajo = _abajoDeLosEjes" in macp
          and "var y0 = abajo - Rot.SeparacionEjes - h1;" in macp)
    # Y mirando tambien las cotas: si una cota baja mas que la burbuja, el rotulo va debajo de la
    # cota y no encima de ella.
    check("y tambien por debajo de las cotas",
          "Math.Min(c.Y1, Math.Min(c.Y2, c.YTexto)) + dy);" in macp)
    # Se reinicia en cada planta: si se quedara el de la anterior, el rotulo de esta se colgaria
    # de una cuadricula que esta en otro sitio del dibujo.
    check("se reinicia en cada planta", "_abajoDeLosEjes = null;" in dibp)
    # LA DISTANCIA VUELVE A 0.50, la de la hoja de la macro: se probo con 5 y se pidio volver.
    # Lo que estaba mal no era la distancia, sino desde donde se medía.
    check("y la distancia es la de la macro, medio metro",
          '_cfg.Numero("ROTULO_SEPARACION_EJES", 0.5)' in rot
          and 'P("ROTULO_SEPARACION_EJES", "0.5",' in cfgplano)

    # ------------------------------------------------------------------
    # EL CASTILLO DE AREA, HASTA EL PANO DEL MURO QUE SE CRUZA
    # ------------------------------------------------------------------
    #  «Cuando sea area un castillo debes sumarle la mitad del espesor de la seccion en el lado
    #  donde se intersecta con otro muro modelado, para que llegue al pano y no se corte antes».
    #  En el modelo LOS MUROS SE DIBUJAN POR SU EJE, asi que el shell del castillo se traza hasta
    #  la LINEA del muro con el que se topa: en el plano el castillo se quedaba a media pared -el
    #  pano del muro seguia mas alla- y parecia cortado.
    #
    #  La cuenta no es «sumale medio espesor y ya», sino HASTA DONDE FALTA: se busca donde cruza
    #  el eje del otro muro y se alarga hasta su cara de mas alla. Con el castillo modelado al
    #  eje sale exactamente el medio espesor que se pidio, y el que YA llegaba al pano no se
    #  alarga: sumar a ciegas lo pasaria de largo justo en ese caso.
    check("el castillo de area se alarga hasta el pano del muro que lo cruza",
          "public static ElementoPlanta AlPanoDeLosMuros(" in cdm
          and "faltaA = Math.Max(faltaA, medio - t);" in cdm
          and "faltaB = Math.Max(faltaB, (t - largo) + medio);" in cdm)
    # SOLO MUROS QUE LO CRUCEN, y solo en la PUNTA: uno que corre en su misma direccion no se
    # cruza -se acompañan- y uno que pasa por el medio no lo alarga. Otro castillo tampoco:
    # entre dos castillos no hay pano que alcanzar.
    check("solo con muros que lo cruzan, y solo en la punta",
          "if (muro.Clase != ClasePlanta.Muro || Dice(muro)" in cdm
          and "if (Math.Abs(den) < 0.1)" in cdm
          and "var alcance = tolM + (medio * 2);" in cdm)
    # Y que el cruce caiga DENTRO del muro: en su prolongacion no hay muro que dé pano.
    check("y con el cruce dentro del muro, no en su prolongacion",
          "if (sMuro < -tolM || sMuro > largoMuro + tolM)" in cdm)
    # Con la holgura del encuentro que ya usa el recorte de los muros: es la misma pregunta.
    check("con la holgura del encuentro de la hoja y su bandera",
          '_cfg.Bandera("SHELL_CASTILLO_AL_PANO", true)' in dibp
          and '_cfg.Numero("PANO_TOLERANCIA_CM", 25) / 100' in dibp
          and 'P("SHELL_CASTILLO_AL_PANO", "SI",' in cfgplano)
    # Y se alarga ANTES de convertirlo, para que la medida que se dibuja y la que nombra al
    # bloque sean la misma: el nombre del bloque tiene que describir al bloque.
    check("se alarga antes de nombrarlo, para que el nombre diga lo que se dibuja",
          "unido = AlPanoDeLosMuros(unido, elementos, espesorPorOmision, tolPanoM);" in cdm
          and cdm.index("AlPanoDeLosMuros(unido")
              < cdm.index("elementos[g[0]] = Como(unido"))

    # ------------------------------------------------------------------
    # EL CABEZAL DE LAS PROPERTY NOTES
    # ------------------------------------------------------------------
    #  Se pidio leerlo igual que los demas. Va ANTES que TRABE y que VIGA en la lista de
    #  palabras: una nota que diga «CABEZAL DE TRABE» es un cabezal, no una trabe. Y su CAPA es
    #  la de las trabes, porque un cabezal es una viga -la que cierra un vano o la que reparte
    #  sobre los apoyos-: sin esa traduccion se iria a E-OTROS, una capa que nadie mira, que es
    #  lo mismo que les pasaba a las tres cadenas.
    secm = leer(ruta("client/src/CadLink.Etabs/SeccionesModelo.cs"))
    capp2 = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CapasPlano.cs"))

    check("CABEZAL se lee de las property notes",
          '("CABEZAL", "CABEZAL"),' in secm
          and secm.index('("CABEZAL", "CABEZAL"),') < secm.index('("TRABE", "TRABE"),'))
    check("y va a la capa de las trabes",
          'if (t.Equals("CABEZAL", StringComparison.OrdinalIgnoreCase))' in capp2
          and 'return CapaDeTipo("TRABE");' in capp2)

    # ------------------------------------------------------------------
    # DE VARIAS CADENAS EN LA MISMA LINEA, SOLO LA MAS ALTA
    # ------------------------------------------------------------------
    #  «Si hay cadena intermedia abajo no lo muestres en planta, en planta solo muestra la cadena
    #  mas alta que exista, solo dibuja una». Un muro de mamposteria lleva TRES cadenas sobre el
    #  mismo paño -desplante, intermedia y cerramiento-: las tres son del mismo nivel y las tres
    #  ocupan LA MISMA LINEA en planta, asi que se dibujaban las tres una encima de la otra, con
    #  tres rotulos pisandose. Y en una planta no hay forma de distinguirlas: no tiene alturas.
    cma = leer(ruta("client/src/CadLink.Cad/PlanoEstructural/CadenaMasAlta.cs"))

    check("de varias cadenas en la misma linea solo se dibuja la mas alta",
          "public static HashSet<ElementoPlanta> Tapadas(" in cma
          and "public static double Arriba(ElementoPlanta el) => Math.Max(el.Z1, el.Z2);" in cma
          and "PlanoEstructural.CadenaMasAlta.Tapadas(p.Elementos, tolCadena)" in dibp)
    # SOLO LAS QUE SE ENCIMAN DE VERDAD: dos tramos seguidos del mismo paño son dos cadenas
    # distintas -una de castillo a castillo y la siguiente de ahi al final- y las dos se dibujan.
    #
    # Y SE TAPA SOLO SI LA CUBREN ENTERA. Esto se corrigio: antes bastaba que la de arriba la
    # solapara mas de la holgura -diez centimetros- para callarla COMPLETA, asi que una cadena
    # corta con una de cerramiento que solo le entraba por la punta desaparecia del plano y en
    # su sitio no quedaba nada. Se mide la UNION de las de arriba, no cada una por su cuenta:
    # si dos se reparten cubrirla, no dejan ningun pedazo sin dibujar y si se calla.
    check("y solo se tapa la que otra le pasa por encima, cubriendola entera",
          "return Math.Min(a2, b2) - Math.Max(a1, b1) >= largoA - tolM;" in cma
          and "private static double LargoCubierto(" in cma
          and "if (LargoCubierto(cubren) >= largoA - tolM)" in cma
          # Sin nadie encima no se calla: en una cadena mas corta que la holgura, la cuenta
          # saldria cierta con cero cubierto.
          and "if (cubren.Count == 0)" in cma)
    # LAS TRABES NO ENTRAN: dos trabes a distinta altura sobre la misma linea son dos vigas de
    # verdad -una de entrepiso y una de azotea- y callar una seria esconder estructura.
    check("las trabes no entran, solo las cadenas y las dalas",
          'StartsWith("CADENA", StringComparison.OrdinalIgnoreCase)' in cma
          and 'Equals("DALA", StringComparison.OrdinalIgnoreCase)' in cma
          and "el.Clase != ClasePlanta.Trabe" in cma)
    # LA TAPADA NO SE DIBUJA NI SE ROTULA: si solo se quitara la geometria, su nombre seguiria
    # escrito en el mismo punto que el de la de arriba, que era la mitad del problema.
    check("la cadena tapada no se dibuja ni se rotula",
          dibp.count("if (_cadenasTapadas.Contains(el))") == 2)
    check("con su bandera y la holgura de la cadena",
          '_cfg.Bandera("CADENA_SOLO_LA_MAS_ALTA", true)' in dibp
          and 'P("CADENA_SOLO_LA_MAS_ALTA", "SI",' in cfgplano)

    # ------------------------------------------------------------------
    # NI EL NOMBRE DE LA CADENA ENCIMA DE UN CASTILLO DE AREA
    # ------------------------------------------------------------------
    #  «Cuando tenga un area sea castillo, no coloques el nombre de la cadena». Y SE MIDE EL
    #  TEXTO, NO SU PUNTO DE INSERCION, que es lo que fallaba: el rotulo es un MTEXT CENTRADO en
    #  la barra, asi que el texto se extiende a los dos lados. Una cadena de 45 cm entre dos
    #  castillos tiene su centro ENTRE los dos -fuera de los dos- y «CC 15X25» mide mas que la
    #  propia cadena: el punto no caia en ningun castillo y el texto los tapaba igual.
    check("el nombre de la cadena no se escribe encima de un castillo de area",
          "public static bool HayCastilloDeAreaEn(" in cdm
          and "public static bool HayCastilloDeAreaBajoElTexto(" in cdm
          and "PlanoEstructural.CastilloDeMuro.HayCastilloDeAreaBajoElTexto(" in dibp
          and 'P("CADENA_ROTULO_EN_CASTILLO_AREA", "NO",' in cfgplano)
    # El ancho del texto, con la misma cuenta de omision que AnchoDeTexto: largo x altura x 0.55.
    check("midiendo el ancho del texto como el resto del dibujante",
          "var medioTexto = texto.Length * altTrabe * 0.55 / 2;" in dibp
          and "cx - ex, cy - ey, cx + ex, cy + ey, castillos, altTrabe))" in dibp)

    # ------------------------------------------------------------------
    # LA REGLA QUE NO DEPENDE DEL TEXTO: EL CASTILLO CUBRE A LA CADENA
    # ------------------------------------------------------------------
    #  En el modelo la cadena llega PARTIDA por sus cruces, asi que EL PEDAZO QUE VA SOBRE EL
    #  CASTILLO es una cadena propia: mide lo que el castillo, su rotulo va al centro de ese
    #  pedazo -o sea justo en medio del castillo- y ahi no cabe ningun nombre. Es el caso de la
    #  imagen: «CC 15X25» escrito a lo largo del K 15X80, con su fondo opaco partiendo el amarillo.
    #
    #  Y distingue los dos casos del plano: la cadena que corre A LO LARGO del castillo queda
    #  cubierta por el y no se rotula; la que LLEGA DE LADO y muere en el solo lo toca en su punta
    #  -de su largo, el castillo cubre el espesor y nada mas- asi que si se rotula.
    check("la cadena que el castillo cubre no se rotula, sin medir el texto",
          "public static bool CubreALaBarra(" in cdm
          and "PlanoEstructural.CastilloDeMuro.CubreALaBarra(el, castillos, tolCastillo)" in dibp
          and "if (comun >= largo * fraccionMin)" in cdm)
    # Con la fraccion, para que una cadena larga que solo lo topa conserve su nombre.
    check("y solo si le cubre la mayor parte",
          "double fraccionMin = 0.6)" in cdm)
    # LOS CASTILLOS SE MIRAN EN LA PLANTA QUE SE ESTA DIBUJANDO, no en un campo que se llena antes:
    # un campo es una fuente de error de mas -si el orden cambia, llega vacio y la regla no se
    # aplica sin decir nada-. La planta siempre esta.
    check("los castillos se miran en la planta, no en un campo",
          "private void Rotulo(ElementoPlanta el, PlantaCad p, double x0, double y0," in dibp
          and "var castillos = p.Elementos.Where(" in dibp
          and "e => e.DeShell && e.Clase == ClasePlanta.Columna).ToList();" in dibp
          and "_castillosDeArea" not in dibp)
    # Cada cinco centimetros: un castillo mide quince, asi que no se cuela entre dos preguntas.
    check("y recorriendo el texto de punta a punta",
          "var pasos = Math.Max(2, (int)Math.Ceiling(largo / 0.05));" in cdm)
    # UN ROTULO NO ES UNA RAYA, ES UNA CAJA: alto por largo, y con el fondo opaco todavia un poco
    # mas. Se recorren sus TRES lineas -el centro y las dos orillas-, que si no un texto que pasa
    # justo al lado tapaba el castillo con media letra y se escapaba.
    check("y midiendo la caja del texto, no solo su linea",
          "foreach (var lado in new[] { 0d, medioAlto, -medioAlto })" in cdm
          and "var medioAlto = altoTexto > 0 ? altoTexto * 0.65 : 0;" in cdm
          and "cx + ex, cy + ey, castillos, altTrabe))" in dibp)
    # SOLO LOS DE AREA -DeShell-: en un castillo de frame el nombre de la cadena nunca ha
    # estorbado, y callarlo seria quitar un dato que si se lee.
    check("y solo con los castillos de AREA, no con los de frame",
          "public bool DeShell { get; set; }" in dtop2
          and "if (!el.DeShell || el.Clase != ClasePlanta.Columna)" in cdm
          and "if (!el.DeShell || el.Clase != ClasePlanta.Columna || el.AnchoM <= Nada)" in cdm)
    # Con el giro deshecho, como el recorte al pano: en un castillo a 45 grados la caja recta
    # diria que si donde no lo hay.
    check("midiendo contra la seccion ya girada",
          "var lx = (rx * ca) + (ry * sa);" in cdm
          and "if (Math.Abs(lx) <= b / 2 && Math.Abs(ly) <= h / 2)" in cdm)

    # ------------------------------------------------------------------
    # EL MURO DE CONCRETO SIN CADENA, EN SU CAPA
    # ------------------------------------------------------------------
    #  «No me dibujas los muros de concreto cuando no tienen cadena, dibujalos en una capa -solo
    #  si no tienen cadena; si tienen cadena dibuja pura cadena, como en mamposteria-: la capa
    #  E-MURO DE CONCRETO». Es la regla de la mamposteria aplicada al concreto: donde hay cadena
    #  manda la cadena -el muro y su cadena ocupan la misma linea en planta y dibujar los dos deja
    #  dos parejas de rayas pegadas- y donde no hay cadena el muro es lo unico que hay. La
    #  diferencia es la CAPA: un muro de concreto es estructura, se arma y se cuela, y tiene que
    #  poderse revisar sin la mamposteria encima.
    check("el muro de concreto sin cadena va a E-MURO DE CONCRETO",
          'P("CAPA_MURO_CONCRETO", "MURO DE CONCRETO",' in cfgplano
          and 'public string CapaMuroConcreto => CapaDeTipo("MURO CONCRETO");' in capp2
          and 'Color("COLOR_MURO_CONCRETO", 4)' in capp2)
    check("y el dibujante la usa solo cuando no lleva cadena",
          "private string CapaDeMuro(ElementoPlanta el, bool tapado)" in dibp
          and 'string.Equals(el.Material, "CONCRETO", StringComparison.OrdinalIgnoreCase)' in dibp
          and "var capaMuro = CapaDeMuro(el, tapado);" in dibp
          and "Barra(el, x0, y0, capaMuro," in dibp)
    # Y se dice en la bitacora cuantos fueron, que es lo que permite saber por que no se ve uno.
    check("y el resumen dice cuantos fueron",
          "_murosDeConcreto++;" in dibp
          and "muro(s) de concreto sin cadena se dibujaron en la capa" in dibp)

    # ------------------------------------------------------------------
    # EL MURO DE CONCRETO EN CIMENTACION: CONTORNO CERRADO Y LEYENDA «MC»
    # ------------------------------------------------------------------
    #  «Cuando dibujes la planta de la cimentacion, para los muros de concreto que digan en
    #  property note CONCRETO, coloca la linea en la base, solo como contorno del muro, y adentro
    #  pon la leyenda MC».
    #
    #  DOS LINEAS, que es su grosor. Se pidio asi: «debe verse su cara inferior representada con 2
    #  lineas que es su grosor». Es lo que Barra() ya hace -los dos panos separados su espesor- y
    #  por eso se usa Barra y no una polilinea cerrada: un contorno cerrado anadiria tapas en los
    #  extremos que ahi no van, porque el muro sigue.
    check("la cara inferior del muro de concreto va con dos lineas",
          'P("MURO_CONCRETO_CONTORNO", "SI",' in cfgplano
          and "if (Barra(el, x0, y0, capaConcreto," in dibp
          and "_contornosMc++;" in dibp)

    #  LA IDENTIFICACION ES POR LA PROPERTY NOTE, y hace falta mirarla APARTE de el.Material:
    #  SeccionesModelo.MaterialDeMuro decide con la nota y el nombre de la seccion JUNTOS y le da
    #  prioridad a la mamposteria, asi que una propiedad llamada "MURO BLOCK 15" cuya nota diga
    #  CONCRETO saldria clasificada como mamposteria. Es justo el caso que se quiere poder
    #  resolver escribiendo la nota.
    check("y se reconoce por la PROPERTY NOTE, no solo por el material",
          'P("MURO_CONCRETO_POR_NOTA", "SI",' in cfgplano
          and "private bool EsMuroDeConcreto(ElementoPlanta el)" in dibp
          and "DiceConcreto(el.Notas)" in dibp
          and 'PALABRAS_CONCRETO' in dibp)

    #  La leyenda va DENTRO, centrada en el tramo ya recortado y girada con el muro. Vive en su
    #  propio metodo porque el muro se dibuja con Barra(), que es la primitiva de TODOS los
    #  elementos de barra y no puede saber nada de leyendas.
    #  SIN FONDO OPACO, y esto es la correccion de «todavia no traes las lineas al frente». El MTEXT
    #  llevaba conFondo:true, y ese fondo TAPA lo que hay detras: en el plano las dos lineas del
    #  muro se veian interrumpidas justo en el «MC».
    #
    #  Y la LEYENDA SE DIBUJA ANTES QUE LAS LINEAS. Las dos van en la misma capa, y al subir una
    #  capa al frente se conserva el orden de dentro: con el texto dibujado despues quedaba encima
    #  de las lineas. Dibujandolo antes, las lineas nacen despues y suben por encima de el.
    check("y la leyenda MC va dentro, centrada, girada y SIN tapar las lineas",
          'P("MURO_CONCRETO_LEYENDA", "MC",' in cfgplano
          and "private void LeyendaDeMuro(" in dibp
          and 'MURO_CONCRETO_LEYENDA_ALTURA' in dibp
          and "AnguloLegible(dx, dy), EstiloSecciones, conFondo: false);" in dibp
          # La altura se limita al grosor del muro: el texto va ENTRE las dos caras.
          and "altura = Math.Min(altura, espesor * 0.7);" in dibp)

    #  El orden en los DOS sitios que dibujan la base: leyenda primero, lineas despues.
    check("y las lineas se dibujan DESPUES de la leyenda, para quedar encima",
          "LeyendaDeMuro(el, x0, y0, capaConcreto, tramo, espesorDelMuro);" in dibp
          and "LeyendaDeMuro(el, x0, y0, capaConcreto, tramoArriba, espesorMuro);" in dibp
          and "if (Barra(el, x0, y0, capaConcreto, espesorMuro, conEje: false, tramoArriba))" in dibp)

    #  Solo en cimentacion por omision: en un entrepiso el muro de concreto convive con la losa y
    #  su armado, y un contorno con leyenda ahi llena el plano.
    check("y por omision solo en la planta de cimentacion",
          'P("MURO_CONCRETO_SOLO_CIMENTACION", "SI",' in cfgplano
          and 'Rot.EsCimentacion(p.Nivel)' in dibp)

    #  El muro al que no le cabe la leyenda se cuenta y se dice: uno corto sin su MC mientras los
    #  de al lado si lo llevan parece un muro de otro material, y eso se malinterpreta en obra.
    check("y el que no le cabe la leyenda se dice",
          "_sinLeyendaMc++;" in dibp
          and "no les cupo la leyenda" in dibp)

    #  Y SE DIBUJA SIEMPRE, TAPADO O NO. Es la correccion de «haz que aparezca siempre»: en
    #  cimentacion casi todos los muros llevan su cadena de desplante encima, asi que
    #  OCULTAR_MURO_BAJO_CADENA los daba por tapados y no se dibujaba NINGUNO. El contorno estaba
    #  dentro del if (!tapado) y la regla de la cadena se lo comia antes de empezar.
    #
    #  La regla sigue siendo la correcta para el muro NORMAL -el muro y su cadena ocupan la misma
    #  linea en planta-, pero la BASE de un muro de concreto es el desplante que hay que colar y
    #  tiene que estar en el plano aunque encima lleve cadena. Por eso va FUERA del if.
    #  Y EL MURO DE CONCRETO ES EL QUE NO LLEVA CADENA DE DESPLANTE. Es la definicion que dio el
    #  usuario -«como los muros de concreto no llevan cadena de desplante»- y la que faltaba: en su
    #  modelo la property note dice TABICON en los 21 muros, asi que atarse a la nota no dibuja
    #  NADA. Pero el plano si los distingue: el de mamposteria lleva su cadena encima y el de
    #  concreto no, porque se cuela con la cimentacion.
    #
    #  Esto INVIERTE lo que habia antes: MURO_CONCRETO_AUNQUE_TAPADO dibujaba la linea AUNQUE
    #  tuviera cadena, que es lo contrario del criterio. El que tiene cadena no la lleva: ahi se ve
    #  la cadena.
    check("el muro sin cadena de desplante se toma como de concreto",
          'P("MURO_SIN_CADENA_ES_CONCRETO", "SI",' in cfgplano
          and "var sinCadena = !bajoCadena.Tapado;" in dibp
          and "&& sinCadena" in dibp
          and 'P("MURO_CONCRETO_AUNQUE_TAPADO"' not in cfgplano)

    #  Y la capa se pide SIN mirar 'tapado': CapaDeMuro() devuelve la capa generica cuando el muro
    #  esta tapado, y aqui se quiere E-MURO DE CONCRETO SIEMPRE, que es lo que se pidio.
    check("y va en E-MURO DE CONCRETO siempre, no en la capa generica",
          "? _capas.CapaMuroConcreto" in dibp
          and "if (!tapado && !dibujado)" in dibp)

    #  SI NO SALIO NI UN MURO DE CONCRETO, SE DICE POR QUE.
    #  Callar aqui hizo perder varias vueltas: el plano sin la linea del muro de concreto se ve
    #  IGUAL en tres casos distintos -la regla no se aplico, la cadena lo tapo, o el muro no esta
    #  clasificado como concreto- y no habia forma de distinguirlos. Era el tercero, y la pista
    #  estaba en otra nota del propio programa: "su linea de mamposteria se dibuja en todos", que
    #  solo puede pasar si NINGUNO es de concreto.
    check("y si ningun muro salio de concreto, el resumen dice por que y con que notas",
          "_muroConcretoVistos" in dibp
          and "_notasDeMuro" in dibp
          and "NINGUNO salió de concreto" in dibp
          and "PALABRAS_CONCRETO" in dibp)

    #  La comparacion normaliza IGUAL que el clasificador -sin espacios ni acentos-, porque
    #  CadLink.Cad no referencia a CadLink.Etabs y no puede reusar EtabsReader.Normalizar. Si una
    #  quitara los espacios y la otra no, el mismo muro saldria de concreto para una y de otra cosa
    #  para la otra.
    check("y la nota se compara normalizada, como en el clasificador",
          "private static string NormalizarNota(string s)" in dibp
          and "char.IsAsciiDigit(c)" in dibp)

    # ------------------------------------------------------------------
    # LA BASE DEL MURO, SOLO SI DEBAJO NO HAY NADA
    # ------------------------------------------------------------------
    #  «Yo solo quiero que los dibujes si abajo del muro no hay nada nada ni otro nivel, que
    #  aplique para diferentes niveles».
    #
    #  Y es la regla correcta: la linea de la base es DONDE EL MURO APOYA. Si debajo hay otro
    #  nivel, el muro apoya en la losa o la trabe de ese nivel, y dibujarle una base es dibujar un
    #  desplante que no existe. Solo el muro que arranca desde lo mas bajo del edificio la lleva.
    #
    #  La comprobacion mira LOS NIVELES DEL MODELO, no la planta que se dibuja: eso es lo que hace
    #  que valga para cualquier nivel sin preguntar en cual estamos.
    plc = leer(ruta("client/src/CadLink.Cad/PlantaCad.cs"))

    check("la planta lleva los niveles del modelo, para saber que hay debajo",
          "public List<(string Nombre, double Z)> Niveles" in plc
          and "p.Niveles.Add((n.Nombre, n.ElevacionM));" in codigo)

    check("y la base solo se dibuja si debajo no hay ningun nivel",
          "private bool NadaDebajoDelMuro(ElementoPlanta el, PlantaCad p)" in dibp
          and "&& nadaAbajo" in dibp
          and "_muroConAlgoAbajo++;" in dibp)

    # ------------------------------------------------------------------
    # LA BASE DE LOS MUROS DE LA PLANTA BAJA, EN LA CIMENTACION
    # ------------------------------------------------------------------
    #  «Pon las lineas de la base del muro de la planta baja en la cimentacion».
    #
    #  Y ERA LO QUE FALTABA. Un muro de planta baja pertenece al story de planta baja, asi que la
    #  planta de cimentacion NO LO TIENE en p.Elementos: el bucle de muros nunca lo vio. Todas las
    #  correcciones anteriores operaban sobre una lista que no contenia esos muros, y por eso no se
    #  dibujaba nada por mas vueltas que se diera.
    #
    #  Van en una lista APARTE, MurosDeArriba, y no mezclados con Elementos: si se anadieran ahi
    #  pasarian por la cadena, el pier, la mamposteria y el recorte que le tocan a un muro de esta
    #  planta, y de ellos se quiere una sola cosa, la linea de su base. Es el mismo camino que ya
    #  seguian los arranques de castillo.
    check("los muros de la planta baja llegan a la planta de cimentacion",
          "public List<ElementoPlanta> MurosDeArriba" in plc
          and "private void AgregarMurosDeArriba(" in codigo
          and "if (EsNivelDeCimentacion(nivel))" in codigo
          and "foreach (var el in p.MurosDeArriba)" in dibp)

    #  Y SOLO LOS DE CONCRETO, sin preguntarle a ETABS de que es el muro: se usa la definicion que
    #  dio el usuario -«los muros de concreto NO LLEVAN CADENA DE DESPLANTE»-. Si no hay cadena
    #  debajo es de concreto y lleva su base; si la hay es de mamposteria y apoya en ella.
    check("y solo se les dibuja la base a los de concreto, por no tener cadena",
          "_basesDeMuroDeArriba++;" in dibp
          and "_muroDeArribaConCadena++;" in dibp
          and 'MURO_SIN_CADENA_ES_CONCRETO' in dibp
          and "LeyendaDeMuro(el, x0, y0, capaConcreto, tramoArriba, espesorMuro);" in dibp)

    # ------------------------------------------------------------------
    # LA CADENA, PARTIDA POR EL VANO DE LA PUERTA
    # ------------------------------------------------------------------
    #  «La parte de la izquierda debe ser continua y la derecha punteada porque es puerta».
    #
    #  Antes la cadena se dibujaba ENTERA de un solo tipo de linea, porque la decision venia de un
    #  bool -MuroDePisoATecho: "tiene muro" o "no tiene"-. Con eso, una cadena con muro en la mitad
    #  y un vano de puerta en la otra salia toda igual y el plano no decia donde esta la puerta.
    #
    #  Los intervalos YA los calculaba ModeloEtabs.MuroDePisoATechoBajo para decidir el bool: se
    #  estaban tirando. TramosConMuroDebajo los devuelve, en fraccion del largo, y el dibujante
    #  parte la cadena por ellos.
    mod = leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))

    check("el modelo dice DONDE hay muro debajo, no solo si lo hay",
          "public List<(double A, double B)> TramosConMuroDebajo(" in mod
          and "public List<(double A, double B)> TramosConMuro" in plc
          and "e.TramosConMuro.AddRange(modelo.TramosConMuroDebajo(el));" in codigo)

    check("y la cadena se dibuja partida: continua con muro, a trazos en el vano",
          'P("CADENA_PARTIR_EN_VANOS", "SI",' in cfgplano
          and "private bool PartirCadenaPorElVano(" in dibp
          and "_cadenasPartidas++;" in dibp
          # El EJE va entero y una sola vez: trocearlo lo dejaria roto en cada puerta.
          and "conEje: false, trozo, tipoLinea);" in dibp)

    #  Y el texto MC va a la capa de TEXTOS, no a la del muro: es lo coherente con el resto del
    #  plano -los rotulos de losa y de seccion ya van ahi- y resuelve de raiz el orden, porque al no
    #  compartir capa con las lineas no puede volver a quedar por encima de ellas.
    check("la leyenda MC va en la capa de textos, no en la del muro",
          "leyenda, altura, CapaTextos," in dibp)

    #  Y LA CAPA DEL MURO DE CONCRETO SE SUBE AL FRENTE, como las cadenas: esas dos lineas son la
    #  base del muro, van sobre el achurado de la losa y sobre las lineas de los ejes, y si quedan
    #  debajo el plano no las ensena aunque esten dibujadas.
    #
    #  Se resuelve en CapasAlFrente() y no solo escrito en la tabla de omision, porque el nombre de
    #  la capa lo decide CAPA_MURO_CONCRETO, que es configurable: dejandolo solo a mano, el dia que
    #  alguien renombre la capa la lista dejaria de coincidir y el muro se quedaria atras EN
    #  SILENCIO.
    check("la capa del muro de concreto se sube al frente, como las cadenas",
          'P("MURO_CONCRETO_AL_FRENTE", "SI",' in cfgplano
          and 'if (_cfg.Bandera("MURO_CONCRETO_AL_FRENTE", true))' in capp2
          and "var muro = CapaMuroConcreto.Trim().ToUpperInvariant();" in capp2
          # Y NO escrita a mano en la lista: la lista sigue siendo la de la macro.
          and 'P("CAPAS_AL_FRENTE", "CADENA,CADENA DESPLANTE,TRABE,ACERO"' in cfgplano)

    #  SOLO PARA MURO DE CONCRETO, pedido expresamente. Y es lo correcto: esta linea es el
    #  desplante de un muro que se cuela, y dibujarla en un muro de mamposteria diria que hay algo
    #  que colar donde no lo hay. Un muro de tabicon apoya en su cadena de desplante, y esa cadena
    #  ya se dibuja por su cuenta.
    #
    #  Consecuencia asumida: si el modelo no trae muros con CONCRETO en su property note, no se
    #  dibuja NADA. No es un fallo del dibujo, y el resumen lo dice con las notas que llegaron.
    check("la base es SOLO del muro de concreto",
          "var baseMc = esMuroConcreto" in dibp
          and 'P("MURO_BASE_TODOS"' not in cfgplano
          and "else if (esMuroConcreto && !nadaAbajo)" in dibp)

    # ------------------------------------------------------------------
    # LA CADENA DE DESPLANTE, SIEMPRE CONTINUA
    # ------------------------------------------------------------------
    #  «Las que digan CADENA DE DESPLANTE, todas sus lineas deben ser continuas, no punteadas, no
    #  importa si hay en niveles arriba». Es lo correcto por lo mismo que en la cimentacion: una
    #  cadena de DESPLANTE no lleva muro debajo POR DEFINICION -desplanta, es la primera-, asi que
    #  la regla de «sin muro debajo va a trazos» se las llevaba TODAS a la punteada y el aviso
    #  dejaba de avisar. Lo que estaba mal era pedirlo solo en el nivel de CIMENTACION: una cadena
    #  de desplante en un nivel intermedio -el arranque de un muro que nace en una losa- es igual
    #  de desplante, y salia punteada. Se mira su TIPO, que sale de las property notes.
    check("la cadena de desplante va continua en cualquier nivel",
          '_cfg.Bandera("CADENA_DESPLANTE_CONTINUA", true)' in macp
          and '.Contains("DESPLANTE", StringComparison.OrdinalIgnoreCase)' in macp
          and 'P("CADENA_DESPLANTE_CONTINUA", "SI",' in cfgplano)
    # Y antes de preguntar por el muro de piso a techo: si no, la respuesta de la ventana mandaria
    # y la de desplante saldria punteada igual.
    check("y se decide antes de mirar si tiene muro debajo",
          macp.index('_cfg.Bandera("CADENA_DESPLANTE_CONTINUA", true)')
          < macp.index("if (el.MuroDePisoATecho)"))

    # ------------------------------------------------------------------
    # POR QUE NO SE VEIA EL CASTILLO DE AREA DEBAJO DE LA CADENA
    # ------------------------------------------------------------------
    #  Dos cosas, las dos comprobadas en el codigo:
    #
    #  1) EL STORY. Un castillo de area se dibuja de corrido en la vista de alzado -de la
    #     cimentacion al cerramiento- y entonces ETABS lo guarda en UN story: el de su punta.
    #     La planta filtra por esa etiqueta, asi que en los demas niveles salia la cadena y
    #     debajo de ella nada. Se trae por GEOMETRIA: si cruza el entrepiso -o llega a el-, en
    #     esa planta hay castillo. Es la misma idea de los arranques de la cimentacion.
    #  2) LAS COTAS. Las de un muro salian de los dos vertices MAS SEPARADOS EN PLANTA, y esos
    #     pueden ser los dos de ABAJO segun el orden en que ETABS devuelva las esquinas: Z1 y
    #     Z2 valian lo mismo, el alto era CERO y en el corte no se dibujaba nada.
    check("las cotas del muro van de la mas baja a la mas alta del paño",
          "e.X1 = coords[ia].X; e.Y1 = coords[ia].Y;" in lector
          and "e.Z1 = zMin;" in lector
          and "e.Z2 = zMax;" in lector)
    check("el castillo de area que cruza el nivel se trae de cualquier story",
          "private void AgregarCastillosDeArea(" in winp
          and "AgregarCastillosDeArea(modelo, p, nivel, yaEstan);" in winp
          and 'CfgPlano.Bandera("SHELL_CASTILLO_DE_OTRO_NIVEL", true)' in winp)

    # ------------------------------------------------------------------
    # EL CORTE ES A LA COTA DEL NIVEL: LO QUE CRUZA SE DIBUJA AHI
    # ------------------------------------------------------------------
    #  Se pidio: "haz el corte al nivel story y todo lo que haya debajo de ese nivel se dibuja
    #  en ese story". La planta se armaba SOLO con lo que ETABS tenia asignado a ese story, y
    #  ETABS asigna cada pieza al piso de su cota MAS ALTA: un muro de corrido por dos niveles
    #  es de un solo story -el de arriba- y desaparecia del plano de abajo.
    #
    #  Ya estaba resuelto para UN caso -el castillo de shell, justo arriba- y con este mismo
    #  razonamiento. Lo que faltaba era que valiera para todo.
    check("lo que cruza el entrepiso se dibuja en ese nivel, sea del story que sea",
          "private void AgregarLoQueCruzaElNivel(" in winp
          and "AgregarLoQueCruzaElNivel(modelo, p, nivel, yaEstan);" in winp
          and 'CfgPlano.Bandera("NIVEL_DIBUJA_LO_QUE_CRUZA", true)' in winp
          and "CruceDeNivel.CruzaBastante(el, n, fraccion)" in winp)

    # Y NADA SE DIBUJA DOS VECES: hay tres pasadas que recogen piezas de otros story por su
    # geometria, asi que las tres comparten el conjunto de lo que ya entro. Sin eso la misma
    # pieza podria entrar por dos caminos y verse doble.
    check("y nada entra dos veces: las pasadas comparten lo que ya esta",
          "var yaEstan = new HashSet<ElementoEtabs>();" in winp
          and winp.count("yaEstan.Contains(el)") >= 3
          and winp.count("yaEstan.Add(el)") >= 4)

    # LA MEDIDA ES CUANTO CUBRE DEL ENTREPISO, no "toca este nivel": una pieza que asoma un
    # centimetro saldria dibujada en dos plantas.
    cruce = leer(ruta("client/src/CadLink.Etabs/CruceDeNivel.cs"))

    check("la medida es cuanto cubre del entrepiso, con su fraccion",
          "public static double Cubre(" in cruce
          and "public static bool CruzaBastante(" in cruce
          and "return Cubre(el, zBaja, zAlta) >= n.AlturaM * f;" in cruce)
    # Y LA Z SE RECORTA AL ENTREPISO, o el corte veria la pieza de tres niveles saliendose.
    check("y las cotas se recortan a ese entrepiso",
          "public static (double Z1, double Z2) RecortadaAlNivel(" in cruce
          and "CruceDeNivel.RecortadaAlNivel(el, n)" in winp)
    # UNA VIGA Y UNA LOSA estan a UNA sola cota: no cruzan nada.
    check("una viga y una losa no cruzan un entrepiso",
          "clase is ClaseElemento.Muro or ClaseElemento.Columna or ClaseElemento.Diagonal"
          in cruce)
    # Por su altura DE VERDAD -los vertices-, no por Z1/Z2, que en un area es el dato flojo.
    check("y se mide por los vertices del area",
          "el.Vertices3D.Min(v => v.Z)" in winp
          and "el.Vertices3D.Max(v => v.Z)" in winp)
    # LA Z SE RECORTA AL ENTREPISO: sin eso, un castillo de tres niveles se dibujaria tres
    # niveles de alto en el corte de uno solo.
    check("la Z se recorta a este entrepiso, para el corte",
          "e.Z1 = Math.Max(zMin, zBaja);" in winp
          and "e.Z2 = Math.Min(zMax, zAlta);" in winp)
    check("y el dibujante los usa para la linea, las burbujas Y las cotas",
          "Ejes.SinRepetidos(p.EjesX), verticales: true, p.Elementos)" in mac
          and "Ejes.SinRepetidos(p.EjesY), verticales: false, p.Elementos)" in mac
          and "Ejes.Verticales(ejesX, yMin, yMax)" in mac
          and "Ejes.Horizontales(ejesY, xMin, xMax)" in mac
          and "ejesX.Select(e => e.Ordenada).ToList()" in mac)
    # Con COPIAS y no sobre la lista de la planta: dibujar dos veces correria los ejes dos
    # veces y la cota total creceria sola.
    check("se trabaja con copias, no se toca la cuadricula de la planta",
          "var salida = ejes.ToList();" in ejp)
    # ------------------------------------------------------------------
    # UN EJE, UNA LINEA: FUERA LOS REPETIDOS, Y LA CAPA AL FONDO
    # ------------------------------------------------------------------
    #  La cuadricula del modelo trae ejes DECLARADOS DOS VECES -uno en el sistema principal
    #  y otro como secundario- y salian dos lineas encima de la otra, dos burbujas
    #  superpuestas y dos cotas pisandose: en el plano se ve como un eje mas grueso.
    check("los ejes repetidos se dibujan UNA sola vez",
          'P("EJES_UNIR_TOL_CM", "1",' in cfgp
          and "public double ToleranciaUnirEjes" in ejp
          and "public static List<(string Id, double Ordenada)> SinRepetidos(" in ejp
          and "Ejes.SinRepetidos(p.EjesX)" in mac)
    # Y tambien en el LECTOR, para que no lleguen duplicados ni al visor ni a la tabla.
    lec = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("y el lector tampoco los mete dos veces",
          "static void Cargar(" in lec
          and "List<EjesModelo.Eje> destino," in lec
          and "const double tol = 0.01;" in lec)
    # ------------------------------------------------------------------
    # «EN SAP2000 ME GENERA MAS EJES DE LOS QUE TENGO»
    # ------------------------------------------------------------------
    #  La cuadricula guarda TODAS las lineas declaradas, visibles o no, y la API las devuelve
    #  todas. En SAP2000 es de lo mas normal tener lineas OCULTAS -se apagan en cuanto sirvieron
    #  para construir y ya no hacen falta-, y esas no son ejes del plano: son lineas de apoyo
    #  que su autor decidio esconder.
    check("los ejes OCULTOS del modelo no se dibujan",
          "Cargar(ejes.X, Com.AsStrings(a[7]), Com.AsDoubles(a[10]), Banderas(a[13]), m)"
          in lec
          and "Cargar(ejes.X, Com.AsStrings(a[7]), Com.AsDoubles(a[9]), Banderas(a[11]), m)"
          in lec
          and "if (mirarVisibles && !visibles[i])" in lec)
    # CON SALVAGUARDA: si el arreglo no cuadra o dice que NINGUNO se ve, no se filtra nada. Un
    # plano con ejes de mas es un problema; un plano SIN ejes es peor, y ese caso no se puede
    # distinguir de un dato mal leido.
    check("y si el dato de visibilidad no cuadra, no se filtra nada",
          "var mirarVisibles = visibles.Length >= ords.Length && visibles.Any(v => v);" in lec)
    # LAS BANDERAS, en las tres formas en que CSI las puede devolver: cambia entre versiones y
    # entre ETABS y SAP2000.
    check("las banderas de visibilidad se leen en cualquiera de sus formas",
          "private static bool[] Banderas(" in lec
          and "private static bool Verdadero(" in lec
          and '"TRUE" or "YES" or "SI"' in lec)
    check("y se avisa de cuantos ejes se saltaron por estar ocultos",
          "OCULTOS en el modelo y no se " in lec)
    # ------------------------------------------------------------------
    # EL SISTEMA DE EJES DE SAP2000 SE LLAMA «GLOBAL», NO «G1»
    # ------------------------------------------------------------------
    #  Aqui estaba el motivo de que en SAP salieran ejes DEDUCIDOS -16 numeros y 26 letras- en
    #  lugar de los que tiene el modelo: si GetNameList no respondia, se probaba «G1», que es
    #  el nombre de omision de ETABS. Con el nombre equivocado la llamada devuelve error y no
    #  hay ejes, aunque esten ahi. Ahora se prueban TODOS los que de el modelo y detras los
    #  tres de convencion: GLOBAL -SAP2000-, G1 -ETABS- y el vacio -el sistema activo-.
    check("se prueban todos los nombres del sistema de ejes, GLOBAL incluido",
          'new[] { "GLOBAL", "G1", string.Empty }' in lec
          and "foreach (var nombre in nombres)" in lec
          and "private static bool LeerCuadricula(" in lec)
    # Y SE DICE DE DONDE SALIERON: leidos del modelo o deducidos. Sin eso, un plano con ejes de
    # mas no se distingue de un modelo con ejes de mas.
    check("y se dice si los ejes se leyeron o se dedujeron",
          "Ejes leídos del modelo: sistema" in lec
          and "los ejes se DEDUCEN de la geometría" in lec)
    # LA DEDUCCION, CON 25 CM DE TOLERANCIA: dos columnas a diez centimetros no son dos ejes,
    # son la misma alineacion con un nudo movido.
    ejm = leer(ruta("client/src/CadLink.Etabs/EjesModelo.cs"))
    check("la deduccion agrupa con 25 cm, no con 5",
          "double tolM = 0.25" in ejm)

    # ------------------------------------------------------------------
    # LAS SECCIONES VARIABLES Y LAS CIRCULARES DE SAP2000
    # ------------------------------------------------------------------
    #  «Salen todas cuadradas cuando son variables e incluso circulares». Una seccion VARIABLE
    #  -non prismatic- no tiene medidas propias: es una lista de tramos con su seccion de
    #  arranque y de llegada, asi que no le responde ningun GetRectangle ni GetCircle, su tipo
    #  no estaba en la lista, y todo acababa en el respaldo: una caja.
    check("la seccion variable se lee por sus tramos",
          "private static Dims? LeerVariable(" in lec
          and '"GetNonPrismatic"' in lec
          and "14 => LeerVariable(propFrame, seccion)" in lec)
    # HEREDA LA FORMA de su seccion de arranque: una variable de circular a circular sale
    # CIRCULAR, que es lo que se pidio.
    check("y hereda la forma de su seccion de arranque",
          "LeerCirculo(propFrame, primera)" in lec
          and "?? LeerTubo(propFrame, primera)" in lec)
    # SIN RECURSION POSIBLE: PorForma vuelve a probar la variable, asi que una variable cuya
    # seccion de arranque fuera otra variable colgaria la lectura del modelo.
    check("y no puede entrar en un bucle sin fin",
          "Aquí NO se llama a PorForma" in lec
          and "PorForma(propFrame, primera)" not in lec)
    # EL TANTEO, CON EL CIRCULO POR DELANTE: su getter es especifico -o es un circulo o falla-
    # mientras que probar rectangulo primero es lo que hacia que una redonda saliera cuadrada.
    check("en el tanteo el circulo va antes que el rectangulo",
          lec.index("?? LeerCirculo(propFrame, seccion)")
          < lec.index("?? LeerRectangulo(propFrame, seccion)"))

    # ------------------------------------------------------------------
    # LAS TRES CASILLAS DEL PROGRAMA, IGUALADAS A MANO
    # ------------------------------------------------------------------
    #  Iban atadas con un enlace del XAML -SelectedIndex por ElementName, de dos vias- y desde
    #  que la casilla de la lectura del modelo vive dentro del panel PLEGADO, ese enlace dejo de
    #  servir: la casilla sin seleccion escribia su -1 en la otra y las dos se quedaban EN
    #  BLANCO. Por eso no se veia en que programa se estaba trabajando.
    xaml_prog = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    cod_prog = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))

    check("las casillas del programa ya no dependen del enlace del XAML",
          "ElementName=ProgramaCsiCombo" not in xaml_prog
          and xaml_prog.count('SelectionChanged="OnProgramaCsiCambiado"') == 3)
    check("y se igualan las tres a mano, con guarda contra el rebote",
          "ProgramaCsiCombo, ProgramaCsiPlanosCombo, ProgramaCsiSeccionesCombo" in cod_prog
          and "if (!_igualandoPrograma && sender is ComboBox tocada" in cod_prog
          and "private bool _igualandoPrograma;" in cod_prog)
    # Y SIN EJE ELEGIDO NO HAY CORTE, PERO SE DICE: salir en silencio es indistinguible de que
    # el corte falle.
    check("sin corte pedido se dice por que no hubo corte",
          "No se dibujó ningún corte porque no se pidió ninguno" in codigo)
    # DRAW ORDER -> SEND TO BACK: la capa de los ejes se baja de ULTIMA, asi que queda
    # debajo de la losa, del armado y de todo lo demas.
    check("la capa de los ejes se manda al fondo, de ultima",
          'P("CAPAS_AL_FONDO", "LOSA,ARMADO LOSA,VOLADO,LOSACERO,EJES"' in cfgp
          and '"CAPAS_AL_FONDO", "LOSA,ARMADO LOSA,VOLADO,LOSACERO,EJES"' in capp)
    check("hay prueba ejecutable de los ejes repetidos",
          "de cinco ejes declarados quedan tres distintos" in pre
          and "E-EJES esta entre las capas que se mandan al fondo" in pre)

    check("hay prueba ejecutable de los ejes al pano",
          "el eje A se corre medio espesor a la IZQUIERDA" in pre
          and "sobre el eje C manda el muro y no la trabe de 40" in pre
          and "un muro perpendicular que cruza el eje no da pano" in pre)

    # Las losas ANTES que trabes y columnas: en AutoCAD el orden de creacion es el
    # orden de dibujo, asi que si se dibujaran al final taparian el resto.
    m_dib = re.search(r"public Resumen Dibujar\(.*?\n    \}", dib, re.S)
    check("se puede leer Dibujar", m_dib is not None)
    if m_dib:
        cuerpo = m_dib.group(0)
        i_losa = cuerpo.find("Losa(el, x0, y0, huellas)")
        i_col = cuerpo.find("Columna(el, x0, y0)")
        check("las losas se dibujan antes que las columnas",
              0 <= i_losa < i_col, f"losa en {i_losa}, columna en {i_col}")

    # La barra se dibuja por sus dos paños, con la normal al eje: asi funciona en
    # cualquier direccion y no solo en las ortogonales.
    m_bar = re.search(r"private bool Barra\(.*?\n    \}", dib, re.S)
    check("se puede leer Barra", m_bar is not None)
    if m_bar:
        cuerpo = m_bar.group(0)
        check("los paños salen de la normal unitaria al eje",
              "-dy / largo" in cuerpo and "dx / largo" in cuerpo)
        check("y un elemento de largo nulo no se dibuja",
              "largo < LargoMinimo" in cuerpo)

    # Lo que el modelo no dio se avisa UNA VEZ, con el total, no una por elemento: con 31
    # muros de tabicon el resumen eran 31 renglones diciendo lo mismo. La macro no avisa de
    # esto: saca el espesor del NOMBRE de la propiedad y, si de ahi tampoco sale, usa
    # ESPESOR_MURO_CM y sigue.
    m_esp = re.search(r"private double Espesor\(.*?\n    \}", dib, re.S)
    check("se puede leer Espesor", m_esp is not None)
    if m_esp:
        check("una medida que falta NO suelta un aviso por elemento",
              "_log.Add(" not in m_esp.group(0)
              and "_sinEspesor++;" in m_esp.group(0))
    check("se cuentan y se avisan de golpe, con el total",
          "internal void ResumirEspesores()" in dib
          and "elemento(s) sin espesor en el modelo" in dib
          and "ResumirEspesores();" in dib)

    # Y el espesor se busca antes en el NOMBRE de la propiedad, como DimsDesdeNombre.
    lect = leer(ruta("client/src/CadLink.Etabs/EtabsReader.cs"))
    check("el espesor se saca del nombre de la propiedad antes de rendirse",
          "public static double EspesorDesdeNombre(string nombre)" in lect
          and "var delNombre = EspesorDesdeNombre(seccion);" in lect
          and "if (delNombre > 0 && delNombre < 1)" in lect)
    check("y se leen las NOTAS de la propiedad, que es de donde sale el material",
          "public string Notas { get; set; }" in leer(ruta("client/src/CadLink.Etabs/ModeloEtabs.cs"))
          and "e.Notas = prop.Notas;" in lect)

    # LOS ROTULOS DE LA PLANTA, COMO LOS PIDE LA HOJA CONFIG: sin los ID, que es lo que
    # llenaba el dibujo de textos encimados.
    m_rot = re.search(r"private void Rotulo\(.*?\n    \}", dib, re.S)
    check("se puede leer Rotulo", m_rot is not None)
    if m_rot:
        cuerpo = m_rot.group(0)
        check("del muro solo se rotula su PIER",
              "ClasePlanta.Muro => PierDelMuro(el)," in cuerpo)
        check("y de columnas y trabes solo la SECCION, sin el ID",
              "ETIQUETA_ID_COLUMNAS y" in cuerpo
              and 'string.IsNullOrWhiteSpace(el.Seccion) ? el.Etiqueta : el.Seccion' in cuerpo
              and '$"{el.Etiqueta}\\P{el.Seccion}"' not in cuerpo)

    check("los fallos se pueden consultar", "IReadOnlyList<string> Fallos" in dib)


# ======================================================================
# 19. Seccion circular, zuncho, encabezado quitado y pestañas arriba
# ======================================================================
def v19_circular_y_ui() -> None:
    """La columna redonda, el zuncho helicoidal y los dos cambios de interfaz."""
    print("\n[19] Seccion circular, zuncho y interfaz")

    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    filas = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))
    tema = leer(ruta("client/src/CadLink.App/Theme/ExcelTabs.xaml"))
    seccad = leer(ruta("client/src/CadLink.Cad/SeccionCad.cs"))
    circ = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Circular.cs"))
    alz = leer(ruta("client/src/CadLink.Cad/AlzadoDrawer.cs"))

    # ------------------------------------------------------------------
    # El cuadro azul, fuera
    # ------------------------------------------------------------------
    # Se comprueba por AUSENCIA de los controles que vivian en el, no por el color:
    # el BrandDarkBrush se sigue usando en otros sitios legitimos.
    for nombre in ("LogoImage", "HeaderProduct", "HeaderCompany"):
        check(f"el encabezado azul ya no tiene {nombre}",
              f'x:Name="{nombre}"' not in xaml)
        check(f"y el codigo ya no lo busca ({nombre})",
              not re.search(rf"\b{nombre}\.", codigo))

    # Lo unico que no estaba en otro sitio SI se conserva, en la barra de estado.
    check("el estado de la licencia sigue estando", 'x:Name="HeaderLicense"' in xaml)
    check("y la version tambien", 'x:Name="HeaderVersion"' in xaml)

    i_lic = xaml.find('x:Name="HeaderLicense"')
    i_tabs = xaml.find('<TabControl x:Name="Sheets"')
    check("la licencia bajo a la barra de estado, debajo de las hojas",
          i_lic > i_tabs, f"licencia en {i_lic}, hojas en {i_tabs}")

    # El logo no se perdio: es el icono de la ventana.
    check("el logo sigue vivo como icono de la ventana",
          "Icon = Branding.Logo;" in codigo)

    # ------------------------------------------------------------------
    # Pestañas arriba
    # ------------------------------------------------------------------
    check("la tira de pestañas va arriba",
          re.search(r'TabStripPlacement"\s+Value="Top"', tema) is not None)
    check("y ya no abajo",
          re.search(r'TabStripPlacement"\s+Value="Bottom"', tema) is None)

    # En el template, la tira tiene que ir ANTES del contenido.
    m_tpl = re.search(
        r'Style x:Key="ExcelTabControlStyle".*?</Style>', tema, re.S)
    check("se puede leer el estilo del contenedor", m_tpl is not None)
    if m_tpl:
        cuerpo = m_tpl.group(0)
        i_panel = cuerpo.find("TabPanel")
        i_cont = cuerpo.find("SelectedContent")
        check("la tira va antes del contenido en el template",
              0 <= i_panel < i_cont, f"tira en {i_panel}, contenido en {i_cont}")

    # La pestaña se abre hacia el contenido, o sea por ABAJO.
    m_item = re.search(r'Style x:Key="ExcelTabItemStyle".*?</Style>', tema, re.S)
    if m_item:
        check("la pestaña se abre hacia abajo, hacia el contenido",
              'BorderThickness="1,1,1,0"' in m_item.group(0))
        check("y se redondea por arriba",
              'CornerRadius="{StaticResource RadioPestana}"' in m_item.group(0)
              and "<CornerRadius x:Key=\"RadioPestana\">7,7,0,0</CornerRadius>" in tema)

    # ------------------------------------------------------------------
    # La forma es POR FILA
    # ------------------------------------------------------------------
    check("la fila sabe si es circular", "public bool EsCircular" in filas)

    # La FORMA se elige en la columna Elemento, no en una casilla aparte. La casilla
    # «Circular» ya no se captura: se quito de la cuadricula.
    check("la forma se elige en el Elemento",
          "EsElementoCircular(_elemento)" in filas)
    check("y ya no hay casilla Circular en la cuadricula",
          'x:Name="ColCircular"' not in xaml)

    # Pero la propiedad SIGUE existiendo, solo para que un .clk guardado antes del
    # cambio abra con sus columnas redondas intactas. Sin esto, un trabajo viejo
    # volveria a salir cuadrado sin avisar.
    check("se conserva la lectura de la casilla vieja por compatibilidad",
          "public string Circular" in filas)
    m_ec = re.search(r"public bool EsCircular =>.*?;", filas, re.S)
    check("se puede leer EsCircular", m_ec is not None)
    if m_ec:
        check("EsCircular mira el Elemento Y la casilla vieja",
              "EsElementoCircular" in m_ec.group(0) and "_circular" in m_ec.group(0))

    # El rotulo del plano dice COLUMNA en los dos casos. «COLUMNA CIRCULAR» es solo
    # el nombre de captura.
    check("hay nombre de rotulo aparte del de captura",
          "public string ElementoRotulo" in filas)
    m_er = re.search(r"public string ElementoRotulo\n    \{.*?\n    \}", filas, re.S)
    if m_er:
        # Cada forma redonda se rotula con el nombre de SU pieza: la columna redonda como
        # COLUMNA y el dado redondo como DADO. Con el atajo de una linea que habia antes,
        # el dado redondo se habria rotulado «COLUMNA» en el plano.
        check("el rotulo de una columna redonda es COLUMNA",
              "return ElementoColumna;" in m_er.group(0))
        check("y el de un dado redondo es DADO",
              "return ElementoDado;" in m_er.group(0))
    check("y los dos mapeadores mandan el nombre de rotulo al dibujo",
          codigo.count("Elemento = r.ElementoRotulo,") == 2,
          f"aparece {codigo.count('Elemento = r.ElementoRotulo,')} vez/veces")

    # TipoDe tiene que reconocerla, o la columna redonda se queda SIN alzado.
    m_td = re.search(r"private static TipoElemento\? TipoDe\(.*?\n    \}", codigo, re.S)
    check("se puede leer TipoDe", m_td is not None)
    if m_td:
        check("TipoDe clasifica la columna redonda como columna",
              "ElementoColumnaCircular" in m_td.group(0))

    check("y columna de zuncho helicoidal", 'x:Name="ColZuncho"' in xaml)

    # ------------------------------------------------------------------
    # Varillas TOTALES, no por lechos
    # ------------------------------------------------------------------
    check("hay conteo total de varillas", "public int NVarTotal" in filas)
    check("y su diametro, que hereda si va vacio",
          "DiamVarTotalEfectivo" in filas)
    check("hay columna N total en la cuadricula", "Binding NVarTotal" in xaml)

    # ------------------------------------------------------------------
    # La celda del ID: prefijo fijo, solo el numero editable
    # ------------------------------------------------------------------
    # Al editar T-01 solo se toca el 01, y el T- no se puede borrar. NO se controla el
    # cursor dentro de la celda —seria fragil— sino que el dato esta SEPARADO.
    check("el prefijo del ID es de solo lectura", "public string PrefijoId =>" in filas)
    check("y el numero es la parte editable", "public string NumeroId" in filas)
    check("escribir el numero recompone el ID con su prefijo",
          "set => Id = PrefijoId + (value ?? string.Empty);" in filas)

    # El caso OTRO sale gratis: sin prefijo, NumeroId ES el ID entero.
    m_num = re.search(r"public string NumeroId\s*\{.*?\n    \}", filas, re.S)
    if m_num:
        check("sin prefijo, la parte editable es el ID entero (el caso OTRO)",
              "p.Length > 0 && id.StartsWith(p" in m_num.group(0))

    # Y el ID avisa de las dos partes, o la celda no se refresca al cambiar el elemento.
    check("el ID avisa de que su prefijo y su numero cambiaron",
          "Raise(nameof(PrefijoId));" in filas and "Raise(nameof(NumeroId));" in filas)

    # La celda es una plantilla: prefijo como texto fijo y cuadro solo para el numero.
    check("la celda del ID usa plantilla, no un cuadro para todo",
          '<DataGridTemplateColumn Header="ID"' in xaml)
    check("el prefijo se pinta fijo en la edicion",
          'Text="{Binding PrefijoId}"' in xaml)
    check("y solo el numero es escribible",
          "Text=\"{Binding NumeroId, UpdateSourceTrigger=PropertyChanged}\"" in xaml)
    check("ya no hay un cuadro de texto para el ID completo",
          'Binding="{Binding Id}"' not in xaml)

    # ------------------------------------------------------------------
    # La tabla se edita en TIEMPO REAL
    # ------------------------------------------------------------------
    # Las celdas confirmaban al SALIR, asi que la vista previa no se movia mientras se
    # escribia. La suscripcion al PropertyChanged de cada fila ya existia; lo que faltaba
    # era que el binding avisara en cada tecla.
    ini_c = xaml.find('x:Name="SeccionesGrid"')
    fin_c = xaml.find("</DataGrid.Columns>", ini_c)
    bloque = xaml[ini_c:fin_c]
    n_bind = len(re.findall(r'Binding="\{Binding \w+', bloque))
    n_real = bloque.count("UpdateSourceTrigger=PropertyChanged")
    check("las celdas de la hoja se confirman mientras se escribe",
          n_real >= n_bind, f"{n_real} en tiempo real de {n_bind} bindings")
    check("y la vista previa escucha la edicion de cada fila",
          "fila.PropertyChanged += OnFilaEditada;" in codigo)

    m_tv = re.search(r"public int TotalVarillas =>.*?;", filas, re.S)
    check("se puede leer TotalVarillas", m_tv is not None)
    if m_tv:
        # En circular es el total y ya: sumar los lechos contaria varillas que no
        # se dibujan.
        check("en circular el total NO suma los lechos",
              "EsCircular" in m_tv.group(0) and "NVarTotal" in m_tv.group(0))

    # El area bruta depende de la forma. Con base x altura en una redonda la
    # cuantia sale un 27 % baja, y una cuantia baja es del lado INSEGURO.
    m_ab = re.search(r"public double AreaBrutaCm2 =>.*?;", filas, re.S)
    check("el area bruta depende de la forma", m_ab is not None)
    if m_ab:
        check("en circular el area bruta es pi*D^2/4",
              "Math.PI" in m_ab.group(0))
    check("y la cuantia usa el area bruta y no base x altura",
          "AreaAceroCm2 / AreaBrutaCm2" in filas)

    # ------------------------------------------------------------------
    # El dibujo circular vive aparte
    # ------------------------------------------------------------------
    check("el motor de dibujo conoce la forma", "public bool Circular" in seccad)
    check("y el diametro y el radio", "public double DiametroCm" in seccad)
    check("hay dibujante circular", "private int DibujarCircular(" in circ)
    check("y se deriva a el desde Dibujar",
          "return DibujarCircular(" in leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs")))

    m_pos = re.search(
        r"private List<\(double X, double Y\)> PosicionesCirculares\(.*?\n    \}",
        circ, re.S)
    check("se puede leer PosicionesCirculares", m_pos is not None)
    if m_pos:
        cuerpo = m_pos.group(0)
        # El radio de paso resta el RADIO de la varilla. Ese medio diametro es el
        # que se olvida, y olvidarlo deja la varilla mordiendo el recubrimiento.
        check("el radio de paso resta rec, zuncho y RADIO de varilla",
              "r - rec - dZun - (dVar / 2)" in cuerpo)
        check("y avisa si las varillas se traslapan", "se traslapan" in cuerpo)

    # El diamante no aplica a un circulo: no hay lechos ni esquinas.
    check("el diamante se descarta en la seccion redonda",
          "EsSi(r.EstriboDiamante) && !r.EsCircular" in codigo)

    # ------------------------------------------------------------------
    # Zuncho: helicoidal o en anillos, y lo elige el usuario
    # ------------------------------------------------------------------
    check("el zuncho sabe si va en helice", "public bool ZunchoHelicoidal" in seccad)
    check("hay helice en el alzado", "private void HeliceDelZuncho(" in alz)
    check("y se elige segun la columna del usuario",
          "a.Circular && a.ZunchoHelicoidal" in alz)

    # ------------------------------------------------------------------
    # ZUNCHO SOLO SI SE PIDIO ZUNCHO. Sin la casilla, son ESTRIBOS.
    # ------------------------------------------------------------------
    # Lo que se pidio: «si no tiene activa la casilla de zunchos, colocar solo EST. como se
    # hace normal; si la tiene activa, entonces si le pones zuncho». El DIBUJO ya era el
    # correcto -capsulas de estribo, no una helice-, pero el ROTULO decia «Zuncho anillos
    # #3 @ 6 cm» en un dado redondo sin la casilla, y un zuncho se pide, se dobla y se paga
    # distinto que un estribo.
    estribos_cs = leer(ruta("client/src/CadLink.Cad/Estribos.cs"))
    secdrw = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("la regla de zuncho-o-estribos vive en un solo sitio",
          "public static bool EsZuncho(bool circular, bool zunchoHelicoidal) =>" in estribos_cs
          and "circular && zunchoHelicoidal;" in estribos_cs)
    check("y queda escrito que una redonda no lleva zuncho por ser redonda",
          "no lleva zuncho por ser" in estribos_cs
          and "lleva estribos normales" in estribos_cs)
    check("el rotulo del alzado la usa",
          "Estribos.EsZuncho(a.Circular, a.ZunchoHelicoidal)" in alz)
    check("el texto del acero transversal del alzado, tambien",
          alz.count("Estribos.EsZuncho(a.Circular, a.ZunchoHelicoidal)") >= 2)
    check("y el rotulo de la seccion",
          "Estribos.EsZuncho(s.Circular, s.ZunchoHelicoidal)" in secdrw)
    check("y el titulo de la vista previa, para que pantalla y papel no se contradigan",
          "Estribos.EsZuncho(s.EsCircular, s.EsZunchoHelicoidal)" in codigo)

    # Y ya no queda ni un rotulo que llame zuncho a un estribo.
    for texto in ('"Zuncho anillos', 'Zuncho {forma}', '"zuncho en anillos"',
                  '? "helic." : "anillos"'):
        check(f"ya no se rotula {texto.strip(chr(34))} sin casilla",
              texto not in alz and texto not in secdrw and texto not in codigo)

    check("sin casilla el alzado dice Est., como cualquier columna",
          '$"Est. {Etiqueta(a.Estribo.Clave)} @ {sep} cm"' in alz
          and '$"Est. {clave} @ {separacionCm:0} cm"' in alz)
    check("y la seccion dice Estr.",
          '$"Estr. {s.Estribo.Clave} @{sep} cm"' in secdrw)
    check("con casilla si dice zuncho, y que es helicoidal",
          '$"Zuncho helic. {Etiqueta(a.Estribo.Clave)} @ {sep} cm"' in alz
          and '$"Zuncho helicoidal {s.Estribo.Clave} @{sep} cm"' in secdrw)
    check("y el aviso del zuncho ofrece quitar el SI para tener estribos",
          "Si lo querías con estribos normales, quita el SI" in alz)

    # La helice se MUESTREA una sola vez y la comparten el dibujo del zuncho y el
    # recorte de las varillas. Si cada uno la calculara por su cuenta, los cortes
    # caerian donde la helice no esta dibujada.
    m_mh = re.search(r"private Helice\? MuestrearHelice\(.*?\n    \}", alz, re.S)
    check("la helice se muestrea aparte", m_mh is not None)
    if m_mh:
        cuerpo = m_mh.group(0)
        # Se acumula la FASE para respetar las zonas L/4-L/2-L/4. Con un periodo
        # fijo, el zuncho saldria con paso constante y la tabla no se cumpliria.
        check("la fase se acumula con el paso de cada zona",
              "fase += 2 * Math.PI * dx / PasoEn(" in cuerpo)
        # El COSENO es la profundidad, y hace falta para saber cuando el zuncho pasa
        # por DELANTE de una varilla.
        check("se guarda el coseno de la fase, que es la profundidad",
              "Math.Cos(fase)" in cuerpo)
        check("hay tope de puntos por si la separacion viene mal",
              "MaxPuntosHelice" in cuerpo)

    check("la muestra se reutiliza y no se recalcula",
          alz.count("MuestrearHelice(") == 2,
          f"se llama {alz.count('MuestrearHelice(')} vez/veces")

    # ------------------------------------------------------------------
    # Dos formas de dibujar el zuncho, segun el modo de la seccion
    # ------------------------------------------------------------------
    # Una polilinea con ancho se dibuja SIEMPRE maciza: no hay version «solo
    # contorno». Asi que la seccion sin relleno necesita otro camino, o el zuncho
    # saldria macizo en un dibujo que va todo en contorno.
    m_h = re.search(r"private void HeliceDelZuncho\(.*?\n    \}", alz, re.S)
    check("se puede leer HeliceDelZuncho", m_h is not None)
    if m_h:
        cuerpo = m_h.group(0)
        check("el modo de la seccion decide como se dibuja el zuncho",
              "HeliceMaciza(" in cuerpo and "HeliceEnContorno(" in cuerpo)

    # El zuncho MACIZO: ancho de polilinea, no hatch. Las dos vias obvias se
    # descartaron con numeros y no se pueden reintroducir:
    #   1. Contorno cerrado con las dos caras radiales -> encierra area CERO,
    #      porque donde el seno es negativo la cara exterior queda por debajo.
    #   2. Banda por la normal -> d/2 supera el radio de curvatura en las crestas.
    # Las dos las encontro tools/verificar_seccion_circular.py.
    m_hm = re.search(r"private void HeliceMaciza\(.*?\n    \}", alz, re.S)
    check("se puede leer HeliceMaciza", m_hm is not None)
    if m_hm:
        cuerpo = m_hm.group(0)
        check("el zuncho macizo es UNA polilinea abierta del eje",
              "cerrada: false" in cuerpo)
        check("el grosor va por ancho de polilinea, no por hatch",
              "AnchoDePolilinea(pl, dZun)" in cuerpo)
        check("y no se intenta rellenar la helice con un hatch",
              'Hatch(bloque, "SOLID"' not in cuerpo)
        check("en modo relleno el zuncho toma el color del estribo",
              "ColorDelZuncho()" in cuerpo)
        check("y se guarda la polilinea para poder repintarla",
              "_zunchoMacizo = pl;" in cuerpo)

    # El color se reaplica DESPUES de ContornosNegros, que si no lo deja negro: el
    # zuncho macizo es una polilinea con ancho, no un hatch, y ContornosNegros repinta
    # todo lo que no sea hatch. Era el motivo de que la helice saliera negra.
    check("el zuncho se repinta despues de ContornosNegros",
          "private void ColorDelZuncho(" in alz
          and "_zunchoMacizo" in alz)

    m_geo_col = re.search(r"private Geo Geometria\(.*?\n    \}", alz, re.S)
    if m_geo_col:
        cuerpo = m_geo_col.group(0)
        i_negros = cuerpo.find("ContornosNegros(bloque, inicio)")
        i_color = cuerpo.find("ColorDelZuncho()")
        check("y el orden es ContornosNegros primero y el color despues",
              0 <= i_negros < i_color,
              f"ContornosNegros en {i_negros}, color en {i_color}")
        check("el zuncho guardado se limpia en cada alzado",
              "_zunchoMacizo = null;" in cuerpo)

    # El zuncho EN CONTORNO: la silueta con el ancho de la varilla.
    m_hc = re.search(r"private void HeliceEnContorno\(.*?\n    \}", alz, re.S)
    check("se puede leer HeliceEnContorno", m_hc is not None)
    if m_hc:
        cuerpo = m_hc.group(0)

        # Las amplitudes r +- d/2 NO son la silueta: las dos valen cero donde el seno
        # vale cero, asi que el zuncho se estrangulaba a 0 mm en cada cruce por el eje,
        # sesenta veces en una columna de 3 m. La silueta es el eje desplazado por su
        # NORMAL, que da ancho constante d.
        check("el contorno no usa las amplitudes r +- d/2",
              "REje + (dZun / 2)" not in cuerpo
              and "REje - (dZun / 2)" not in cuerpo)
        check("el contorno desplaza el eje por su normal",
              "var nx = -ty / m;" in cuerpo and "var ny = tx / m;" in cuerpo)
        check("y el desplazamiento es medio diametro",
              "var w = dZun / 2;" in cuerpo)
        check("van abiertas, que es lo que lo hace seguro",
              "cerrada: false" in cuerpo)
        check("y sin ancho, para que no salgan macizas",
              "AnchoDePolilinea" not in cuerpo)

        # Desplazar por la normal riza en las crestas, porque ahi el radio de curvatura
        # (1.2 mm) es menor que medio diametro (4.8 mm).
        check("los rizos de las crestas se quitan",
              "SinRizos(caraA)" in cuerpo and "SinRizos(caraB)" in cuerpo)

        # Sin tapas las dos caras quedan como dos curvas sueltas que mueren en el aire.
        check("el zuncho en contorno lleva tapas en los extremos",
              "tapaIni" in cuerpo and "tapaFin" in cuerpo)
        check("y las tapas se sacan de las caras SIN recortar",
              cuerpo.find("var tapaIni") < cuerpo.find("caraA = SinRizos(caraA);"))

    # SinRizos DESCARTA los puntos que retroceden, no les aplasta la X: aplastarlos
    # mueve el punto respecto del eje y el ancho de la barra se queda en 6.3 mm de los
    # 9.5 que deberia. Descartandolos el ancho es exacto.
    m_sr = re.search(r"private static double\[\] SinRizos\(.*?\n    \}", alz, re.S)
    check("se puede leer SinRizos", m_sr is not None)
    if m_sr:
        cuerpo = m_sr.group(0)
        check("SinRizos descarta los puntos del rizo", "continue;" in cuerpo)
        check("y no les aplasta la X",
              "pts[2 * i] = pts[2 * (i - 1)];" not in cuerpo)
        check("los extremos se conservan siempre, que llevan las tapas",
              "salida.Add(pts[2 * (n - 1)]);" in cuerpo)

    # ------------------------------------------------------------------
    # Las LLAMADAS del corte que se inserta junto al alzado
    # ------------------------------------------------------------------
    # Bloquear deja fuera las capas COTAS y ROTULOS a proposito, asi que las llamadas
    # no viajan dentro del bloque y el corte que el alzado pone al lado llegaba pelado.
    # NO se arregla metiendolas en el bloque —descentraria su origen y romperia el
    # apoyado por pano inferior— sino REDIBUJANDOLAS junto al bloque insertado, igual
    # que ya hacen el CORTE A-A' y el rotulo del alzado.
    secdrawer = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("el corte insertado junto al alzado recupera sus llamadas",
          "public void LlamadasJuntoAlBloque(" in circ)

    # Y el bloque sigue SIN llevarlas dentro: eso no ha cambiado.
    check("y el bloque sigue excluyendo COTAS y ROTULOS",
          '"ROTULOS", StringComparison.OrdinalIgnoreCase' in secdrawer)

    m_lb = re.search(r"public void LlamadasJuntoAlBloque\(.*?\n    \}", circ, re.S)
    check("se puede leer LlamadasJuntoAlBloque", m_lb is not None)
    if m_lb:
        cuerpo = m_lb.group(0)

        # Reutiliza las MISMAS llamadas que la seccion, no unas nuevas.
        check("reutiliza los leaders de los lechos",
              "LeadersDeLecho(s.Superior" in cuerpo
              and "LeadersDeLecho(s.Inferior" in cuerpo)
        check("y los de las laterales",
              "LeaderVarilla(xIzq" in cuerpo and "LeaderVarilla(xDer" in cuerpo)

        # Y las MISMAS posiciones, sacadas del calculo puro.
        check("usa el calculo puro de posiciones, sin redibujar varillas",
              "PosicionesDeLecho(s.Superior" in cuerpo
              and "PosicionesLaterales(s," in cuerpo)

        # El circulo va por su propio camino: alli no hay lechos.
        check("el corte circular tiene su propio camino",
              "LlamadasCirculoJuntoAlBloque(" in cuerpo)

    # El calculo de posiciones esta SEPARADO del dibujo, o la unica forma de recuperar
    # las posiciones seria volver a dibujar las varillas encima.
    check("hay calculo de posiciones de lecho sin dibujo",
          "private (double[] Esquina, double YEsquina, double[] Intermedia, "
          "double YIntermedia,\n        double YGrupo) PosicionesDeLecho(" in secdrawer)
    check("y de las laterales",
          "PosicionesLaterales(" in secdrawer)

    # Y el dibujo lo USA, en vez de tener su propia copia de la aritmetica.
    m_lecho = re.search(
        r"private \(double\[\] Esquina, double\[\] Intermedia, double Y\) Lecho\(.*?\n    \}",
        secdrawer, re.S)
    if m_lecho:
        cuerpo = m_lecho.group(0)
        check("Lecho usa el calculo puro y no repite el reparto",
              "PosicionesDeLecho(lecho, x0, y0, b, h, rec, dEst, arriba)" in cuerpo)
        check("y ya no calcula el paso por su cuenta",
              "(b - (2 * off)) / (lecho.NEsquina - 1)" not in cuerpo)

    # El alzado AVISA de donde dejo el bloque, y no llama al dibujante de secciones:
    # asi no se mete aqui una dependencia de SeccionCad.
    check("el alzado avisa de donde inserto la seccion",
          "public Action<string, double, double>? TrasInsertarSeccion" in alz)
    check("y avisa DESPUES de apoyar el bloque en su sitio",
          "TrasInsertarSeccion?.Invoke(id, x, y);" in alz)

    m_is = re.search(r"public SeccionPuesta\? InsertarSeccion\(.*?\n    \}", alz, re.S)
    if m_is:
        cuerpo = m_is.group(0)
        i_mover = cuerpo.find("Mover(br,")
        i_avis = cuerpo.find("TrasInsertarSeccion?.Invoke")
        check("el aviso va despues del Mover, o la esquina no estaria en (x,y)",
              0 <= i_mover < i_avis, f"Mover en {i_mover}, aviso en {i_avis}")

    # ------------------------------------------------------------------
    # El GANCHO SISMICO del zuncho circular
    # ------------------------------------------------------------------
    # Antes no existia, y el <remarks> del archivo lo justificaba diciendo que un
    # zuncho circular no lleva gancho porque no tiene esquinas donde doblar. Es falso:
    # lo que ancla un zuncho es el doblez a 135 grados alrededor de una VARILLA con la
    # cola en el nucleo, y la esquina solo era donde estaba la varilla.
    # Devuelve el angulo de la varilla del gancho, que necesita quien recorta el circulo
    # interior del zuncho.
    check("el zuncho circular lleva gancho sismico",
          "private double? GanchoDelZuncho(" in circ)

    check("y ya no se afirma que un zuncho circular no lleva gancho",
          "No hay gancho sísmico en la esquina" not in circ)

    m_gz = re.search(r"private double\? GanchoDelZuncho\(.*?\n    \}", circ, re.S)
    check("se puede leer GanchoDelZuncho", m_gz is not None)

    if m_gz:
        cuerpo = m_gz.group(0)

        # Reutiliza la Cola del estribo rectangular en vez de repetir la geometria.
        check("el gancho circular reutiliza la Cola de la rectangular",
              "Cola(contorno, quads, bx, by, rIn, rOut" in cuerpo)

        # Mismo criterio de longitud que la seccion rectangular: la columna T cruda,
        # sin el 12*db, que es regla del alzado.
        check("usa la columna T tal cual, como la seccion rectangular",
              "s.GanchoCm * _escala" in cuerpo)

        # El doblez envuelve la VARILLA: radio interior = radio de la varilla.
        check("el doblez envuelve la varilla, no la cara del concreto",
              "var rIn = rVar;" in cuerpo and "var rOut = rVar + dZun;" in cuerpo)

        # La cola sale del radio interior girado 45 grados, que es girar el avance 135.
        check("la cola es el radio interior girado 45 grados",
              "(rx - ry) * Rt2I" in cuerpo and "(rx + ry) * Rt2I" in cuerpo)

        # Las normales son las perpendiculares a la cola, no constantes escritas.
        check("las normales de arranque son perpendiculares a la cola",
              "var n1X = -uy;" in cuerpo and "var n1Y = ux;" in cuerpo)

        # LAS DOS colas siempre, igual que el estribo rectangular. Antes se dibujaba una
        # sola en helice, con el argumento de que una espiral es una barra continua con
        # un solo arranque. Es cierto de la barra, pero NO es el detalle que se dibuja:
        # el remate se representa con sus dos ganchos, uno encima del otro y con el de
        # dentro recortado, y asi se lee en tipo 1 y en tipo 2.
        check("van las dos colas, no una",
              "foreach (var (nx, ny) in new[] { (n1X, n1Y), (n2X, n2Y) })" in cuerpo)
        check("y ya no se dibuja una sola en helice",
              "if (!s.ZunchoHelicoidal)" not in cuerpo)

        # El doblez se dibuja tambien como CONTORNO, no solo como relleno: si no, en la
        # seccion tipo 1 el gancho salia como dos colas sueltas sin nada que las uniera.
        # El doblez se dibuja como contorno, para que salga tambien en tipo 1. Y los dos
        # arcos NO estan en la misma situacion, que es lo que faltaba mirar:
        #   - el INTERIOR es tangente al borde del nucleo (rPaso + rVar = rZunInt, exacto)
        #     asi que cae entero dentro y se dibuja COMPLETO;
        #   - el EXTERIOR llega a rZunExt, o sea que ATRAVIESA la banda del zuncho, y su
        #     tramo de dentro es la linea que se veia cruzando y delataba que el gancho
        #     era una pieza pegada encima. Ese tramo NO se dibuja.
        # Los dos arcos del doblez arrancan en la TANGENCIA con la banda, no en el borde
        # de la cola. Es lo que hace que el gancho se lea como continuacion del zuncho:
        #   - el EXTERIOR sigue hasta hacerse tangente al pano exterior, porque
        #     rPaso + rOut = rZunExt exacto; antes se cortaba donde entraba en la banda y
        #     quedaba un tajo plano a media vuelta;
        #   - y el INTERIOR se recorta SOLO por ese lado, el derecho.
        check("el arco exterior del doblez arranca en la tangencia",
              "Agregar(contorno, Arco(bx, by, rOut, aTangente, a1 + Pi));" in cuerpo)

        # El arco INTERIOR no se dibuja: su radio es rVar y su centro es el de la varilla,
        # o sea que es EXACTAMENTE la circunferencia de la varilla, que ya se dibuja. Y
        # donde el doblez se corre mas alla del contorno de la varilla dejaba una linea
        # suelta cruzando, que era la que se veia en el plano.
        check("el arco interior del doblez no se dibuja, que es la varilla misma",
              "Arco(bx, by, rIn," not in cuerpo)

        check("la tangencia es la direccion centro->varilla",
              "var aTangente = Math.Atan2(ry * -1, rx * -1);" in cuerpo)

        check("ya no se recorta el arco contra la banda, ahora sigue hasta la tangencia",
              "ArcoFueraDeLaBanda(" not in circ)

        # Y la cola de dentro se recorta contra el circulo interior del zuncho, que es el
        # equivalente circular del recorte contra la linea recta del estribo.
        check("la cola se recorta contra el nucleo",
              "CruceConElNucleo(poX, poY, ux, uy, cx, cy, rZunInt, gancho)" in cuerpo)

        # La varilla elegida es la de ABAJO, porque la llamada apunta a la de arriba.
        check("el gancho va en la varilla de abajo, lejos de la llamada",
              "if (p.Y < barra.Y)" in cuerpo)

        # Y la cola no puede pasarse del nucleo.
        check("la cola se recorta si no cabe en el nucleo",
              "gancho = tope;" in cuerpo)

    # Se DIBUJA, no solo se declara.
    check("el gancho del zuncho se dibuja de verdad",
          "GanchoDelZuncho(" in circ and circ.count("GanchoDelZuncho(") >= 2)

    # Va DESPUES de las varillas: se abraza a una de ellas.
    m_dc = re.search(r"private int DibujarCircular\(.*?\n    \}", circ, re.S)
    if m_dc:
        cuerpo = m_dc.group(0)
        i_var = cuerpo.find("RellenarVarillas(circulos, rellenosVarilla)")
        i_gan = cuerpo.find("GanchoDelZuncho(")
        check("el gancho se dibuja despues de las varillas",
              0 <= i_var < i_gan, f"varillas en {i_var}, gancho en {i_gan}")

        check("y el gancho se rellena en la seccion rellena",
              "RellenoDelGancho(ganchoQuads, ganchoSectores)" in cuerpo)

    # El relleno borra sus fronteras auxiliares, o quedarian dos contornos sueltos
    # encima del acero.
    m_rg = re.search(r"private void RellenoDelGancho\(.*?\n    \}", circ, re.S)
    check("se puede leer RellenoDelGancho", m_rg is not None)
    if m_rg:
        cuerpo = m_rg.group(0)
        check("el relleno del gancho borra sus fronteras auxiliares",
              "Borrar(t);" in cuerpo)
        check("y usa el sector anular para el doblez y el quad para la cola",
              "SectorAnular(" in cuerpo and "PolyCerrada(" in cuerpo)

    # ------------------------------------------------------------------
    # El circulo interior del zuncho se recorta en el gancho
    # ------------------------------------------------------------------
    # Es un circulo completo y se sube al frente con el resto del contorno, asi que su
    # linea cruzaba POR ENCIMA del doblez y delataba que habia dos piezas superpuestas.
    # Es el mismo problema que el estribo rectangular resuelve con su yTrim.
    check("el circulo interior del zuncho se recorta en el gancho",
          "contorno.Remove(zunInt);" in circ)
    # LA CIRCUNFERENCIA SE BORRA DE VERDAD. Sacarla de la lista de contorno no la quita
    # del dibujo, y eso era el fallo: en la seccion rellena dejaba una linea azul tenue
    # cruzando por delante del hatch —azul porque se habia librado del repintado a negro—
    # y en la de contorno, la linea entera sin recortar.
    check("la circunferencia interior se borra, no solo se saca de la lista",
          "zunIntPorBorrar = zunInt;" in circ
          and "Borrar(zunIntPorBorrar);" in circ)

    # Y el ORDEN es lo critico: tiene que sobrevivir al hatch de concreto, que la usa como
    # frontera, y morir antes del repintado.
    if m_dc:
        cuerpo = m_dc.group(0)
        i_gan = cuerpo.find("zunIntPorBorrar = zunInt;")
        i_hat = cuerpo.find("ParteHatch(zunInt, circulos")
        i_bor = cuerpo.find("Borrar(zunIntPorBorrar);")
        i_neg = cuerpo.find("foreach (var ent in contorno)")

        check("se borra DESPUES del hatch, que la necesita como frontera",
              0 <= i_hat < i_bor,
              f"hatch en {i_hat}, borrado en {i_bor}")

        check("y ANTES del repintado, o no serviria de nada",
              0 <= i_bor < i_neg,
              f"borrado en {i_bor}, repintado en {i_neg}")

    check("y se sustituye por un arco que se salta el gancho",
          "Agregar(contorno, Arco(\n                    cx, cy, rZunInt," in circ)
    check("hay cuenta del hueco que hay que saltarse",
          "private static double HuecoDelGancho(" in circ)

    # El hueco es ASIMETRICO: el circulo interior es tangente al doblez justo en el
    # angulo de la varilla, y el doblez se corre hacia UN SOLO lado. Quitar un trozo
    # simetrico borraba tambien la linea del lado donde no hay nada que la tape.
    check("el hueco arranca EN la varilla y no a los dos lados",
          "anguloGancho.Value + hueco,\n                    anguloGancho.Value));" in circ)
    check("y ya no se usa un semiangulo simetrico",
          "SemiAnguloDelGancho" not in circ)

    m_sa = re.search(r"private static double HuecoDelGancho\(.*?\n    \}", circ, re.S)
    if m_sa:
        c3 = m_sa.group(0)
        # El radio de paso se RECALCULA, no se deduce de los radios del doblez: al
        # intentarlo se contaba dZun dos veces y el hueco salia de mas.
        check("el hueco usa el mismo radio de paso que las varillas",
              "var rPaso = r - rec - dZun - rVar;" in c3)

        # Se resuelve la interseccion de las dos circunferencias, no se estima con un
        # arcotangente del radio del doblez: el cruce real cae bastante antes.
        check("el hueco sale del cruce real de las dos circunferencias",
              "(rZunInt * rZunInt)) / (2 * rPaso)" in c3
              and "Math.Sqrt(disc)" in c3)
        # SIN margen: las dos curvas son el mismo acero —el zuncho que entra en el
        # doblez— asi que tienen que TOCARSE. El grado de margen que hubo aqui son 3.5 mm
        # al radio del zuncho, y lo que se veia es que la linea no llegaba al gancho.
        check("el arco arranca EN el cruce, sin margen, para que toque la curva",
              "return Math.Atan2(Math.Sqrt(disc), a);" in c3)
        check("y ya no queda el grado de margen",
              "+ (Pi / 180)" not in c3)

    # ------------------------------------------------------------------
    # DADO CIRCULAR
    # ------------------------------------------------------------------
    # Es al DADO lo que COLUMNA CIRCULAR es a la COLUMNA: solo cambia la FORMA. Y hay
    # cuatro sitios que tienen que enterarse, no uno: la lista, la forma, el rotulo y el
    # alzado. Con la lista sola, un DADO CIRCULAR se dibujaba como un rectangulo.
    check("DADO CIRCULAR existe como elemento",
          'public const string ElementoDadoCircular = "DADO CIRCULAR";' in filas
          and 'public const string ElementoDado = "DADO";' in filas)
    check("y esta en el desplegable, junto al dado cuadrado",
          "SeccionConcretoRow.ElementoDado," in codigo
          and "SeccionConcretoRow.ElementoDadoCircular," in codigo)
    check("se dibuja REDONDO, como la columna circular",
          "|| e.Equals(ElementoDadoCircular, StringComparison.OrdinalIgnoreCase);" in filas)
    check("pero se rotula DADO, no COLUMNA",
          "return ElementoDado;" in filas
          and "Cada forma redonda se rotula con el nombre de SU pieza" in filas)
    check("y lleva alzado vertical, como el dado cuadrado",
          'e == "DADO" || e == SeccionConcretoRow.ElementoDadoCircular' in codigo)
    check("y comparte el prefijo D- del ID",
          '"DADO CIRCULAR" => "D-",' in filas)

    # ------------------------------------------------------------------
    # ZAPATAS AISLADAS: la pestaña, con las dos familias
    # ------------------------------------------------------------------
    trazo_zap = leer(ruta("client/src/CadLink.Cad/TrazoZapata.cs"))
    zap_row = leer(ruta("client/src/CadLink.App/Models/ZapataAisladaRow.cs"))
    zap_cb = leer(ruta("client/src/CadLink.App/MainWindow.Zapatas.cs"))

    m_tab_zap = re.search(
        r'<TabItem Header="Zapatas Aisladas">.*?\n            </TabItem>', xaml, re.S)

    check("se puede leer la pestaña de zapatas aisladas", m_tab_zap is not None)

    check("la pestaña de zapatas aisladas ya no es un aviso de pendiente",
          m_tab_zap is not None
          and "Modulo pendiente de portar" not in m_tab_zap.group(0))
    check("tiene su cuadricula y su vista previa",
          'x:Name="ZapatasGrid"' in xaml and 'x:Name="ZapataPreviewCanvas"' in xaml)
    check("y su renglon de totales",
          'x:Name="TotalesZapatasText"' in xaml)

    # La geometria vive en CadLink.Cad, no en la vista previa: es la que va a usar tambien
    # el dibujante de AutoCAD.
    check("la geometria de la zapata vive fuera de la vista previa",
          "public static class TrazoZapata" in trazo_zap
          and "public sealed class ZapataCad" in trazo_zap)
    check("y no sabe nada de AutoCAD ni de WPF",
          "_ms" not in trazo_zap and "AcadConnection" not in trazo_zap
          and "System.Windows" not in trazo_zap)

    # LAS DISTANCIAS DE LAS MACROS, una por una. Son lo que se pidio: «dibujalo a la
    # distancia que tiene las macros».
    # LO QUE SE PIDIO: cada zapata a un metro a la IZQUIERDA del pano izquierdo de la
    # anterior, en el corte Y en la planta -las dos usan la misma X-.
    check("las zapatas se acomodan hacia la izquierda, 80 cm entre una y otra",
          "public const double SeparacionIzquierda = 0.8;" in trazo_zap
          and "x -= SeparacionIzquierda + Ancho(anchos, i);" in trazo_zap)
    check("y el tipo ya no cambia el acomodo",
          "El tipo ya no cambia el acomodo" in trazo_zap)
    check("y esta comprobado con numeros, para los dos tipos",
          "justo la separacion de 80 cm" in leer(ruta("tools/prueba-zapata/Program.cs")))

    # LO QUE SE PIDIO: "empezar en x = -0.8", "no lo dibujes a partir del centro". La fila ya no
    # arranca en el origen: la primera zapata queda con su pano DERECHO en -0.8, los mismos 0.8
    # que separan una zapata de la siguiente, y de ahi crece hacia la izquierda.
    check("la fila empieza en x = -0.8 y no en el origen",
          "public const double XArranque = -SeparacionIzquierda;" in trazo_zap
          and "var x = XArranque - Ancho(anchos, 0);" in trazo_zap
          and "var x = 0.0;" not in trazo_zap)
    check("se coloca el pano DERECHO de la primera, por eso se resta su ancho",
          "La PRIMERA con su paño DERECHO en -0.8" in trazo_zap)
    check("y se puede preguntar hasta donde llega la fila por la derecha",
          "public static double XDerechaDeLaFila => XArranque;" in trazo_zap)
    check("queda escrito por que se movio, y como volver atras",
          "empezar en x = −0.8" in trazo_zap
          and "no lo dibujes a partir del centro" in trazo_zap
          and "se le quita el <c>− Ancho(anchos, 0)</c>" in trazo_zap)
    check("y esta comprobado con numeros que ninguna zapata toca el origen",
          "ninguna zapata pasa de x = -0.8 ni toca el origen"
          in leer(ruta("tools/prueba-zapata/Program.cs")))

    check("la separacion entre secciones es la de cada macro",
          "public const double SeparacionCentral = 1.0;" in trazo_zap
          and "public const double SeparacionLindero = 0.8;" in trazo_zap)
    check("el lindero arranca en -3 y en -8, como su macro",
          "public const double LinderoXBase = -3.0;" in trazo_zap
          and "public const double YBaseElevacion = -8.0;" in trazo_zap)
    # El acomodo ya NO depende del tipo: las dos familias crecen hacia la izquierda, un metro
    # entre una y la siguiente. Antes las centrales crecian a la derecha desde cero y los
    # linderos a la izquierda desde -3, y al mezclarlos en una hoja se encimaban.
    check("las dos familias crecen hacia la izquierda",
          "x -= SeparacionIzquierda + Ancho(anchos, i);" in trazo_zap
          and "acumulado += Ancho(anchos, i) + SeparacionCentral;" not in trazo_zap)
    # EL PUNTO DE INSERCION: el corte en -8 y la planta en -15, para las DOS familias.
    check("la planta arranca en -15, sin depender del rotulo",
          "var yPlanta = YPlantaLindero(yZapBot, z.LargoM);" in trazo_zap
          and "public const double PlantaYBaseLindero = -15.0;" in trazo_zap
          and "public const double YBaseElevacion = -8.0;" in trazo_zap)
    check("y queda escrito por que se movian las cotas de la planta",
          "con ella se movían sus cotas" in trazo_zap)
    check("el calculo de la macro central se conserva, documentado",
          "Ya no se usa para colocar la planta" in trazo_zap)

    check("la planta de la central cuelga de la vista de corte",
          "public const double PlantaOffsetY = -3.0;" in trazo_zap
          and "var yFondoCorte = yZapBot - RotuloEscalaOffset;" in trazo_zap)
    check("y la del lindero arranca en -15, o mas abajo si no cabe",
          "public const double PlantaYBaseLindero = -15.0;" in trazo_zap
          and "public const double PlantaSeparacionMin = 1.2;" in trazo_zap)
    check("el dado va centrado en la central y al paño derecho en el lindero",
          "xDadoDer = xDer;" in trazo_zap
          and "xDadoIzq = xCentro - (wDado / 2);" in trazo_zap)
    check("los estribos se reparten en zonas de 25, 50 y 25",
          "largoInterior * 0.25" in trazo_zap and "largoInterior * 0.5" in trazo_zap)
    check("y el dado se salta los primeros, donde esta la parrilla",
          "QuitarPrimeros(centros, z.DobleParrilla ? 2 : 1)" in zap_cb)
    check("la malla cierra la ultima varilla solo si cabe",
          "PlantaFraccionCierre = 0.3" in trazo_zap
          and "fin - ultima > sep * PlantaFraccionCierre" in trazo_zap)

    # La fila: las celdas de la macro, y UNA tabla para las dos familias.
    check("la fila de zapata trae las celdas de la macro",
          "public sealed class ZapataAisladaRow : Row" in zap_row
          and "<c>E4</c> / <c>V4</c>" in zap_row)
    check("y se dice por que es UNA tabla y no dos",
          "no dos tablas: los datos son los mismos" in zap_row
          or "una</b> tabla con una columna de" in zap_row)
    check("la fila sabe pasarse a datos de geometria, en un solo sitio",
          "public ZapataCad AFormatoCad()" in zap_row
          and "dibujarían dos zapatas distintas" in zap_row)
    check("y dice que falta para poder dibujarla",
          "public string Falta" in zap_row)

    check("la coleccion de zapatas vive en DatosProyecto",
          "ObservableCollection<ZapataAisladaRow> ZapatasAisladas" in filas)
    check("y el ejemplo trae una de cada familia",
          "Tipo = ZapataCad.Central, Id = \"Z-1\"" in filas
          and "Tipo = ZapataCad.Lindero, Id = \"ZL-1\"" in filas)

    # La vista previa: elevacion Y planta, con el acomodo real de la fila.
    check("la vista previa de zapatas dibuja elevacion y planta",
          "private void DibujarVistaPreviaZapata()" in zap_cb
          and "private void DibujarPlantaPrevia(" in zap_cb)
    check("y usa el acomodo REAL, con los anchos de todas",
          "TrazoZapata.XBase(z.Tipo, anchos, indice < 0 ? 0 : indice)" in zap_cb
          and "TrazoZapata.Colocar(z, xBase)" in zap_cb)
    # LA MITAD PARA CADA VISTA. Con las dos en el mismo sistema de coordenadas -que es como
    # estaba- la planta cuelga a 3 m de la elevacion en la central y a 15 en el lindero, asi
    # que salian dos dibujos diminutos con un hueco enorme en medio.
    check("cada vista tiene su mitad y su propia escala",
          "private void DibujarElevacionPrevia(" in zap_cb
          and "var wMitad = (ancho - (3 * gap)) / 2;" in zap_cb)
    check("y se dice por que no van en el mismo sistema",
          "dos dibujos diminutos con un" in zap_cb)

    # LAS COTAS: las mismas que pone la macro y en el mismo sitio.
    # LA PREVIA, CON RELLENOS Y COLORES: estaba a puro contorno y se veia vacia. Los colores son
    # los mismos papeles del plano, uno por cosa, y las texturas son el AR-CONC y el EARTH reducidos
    # a un mosaico que se lee en unos centimetros.
    check("la previa lleva rellenos de concreto, plantilla y terreno",
          "private static readonly Brush PincelConcreto" in zap_cb
          and "private static readonly Brush PincelPlantilla" in zap_cb
          and "private static readonly Brush PincelTerreno" in zap_cb
          and "private static Brush Textura(" in zap_cb)
    check("las texturas se congelan, que la previa se redibuja en cada tecla",
          "pincel.Freeze();" in zap_cb
          and "TileMode = TileMode.Tile" in zap_cb)
    check("el terreno solo va a los lados del dado y por encima del lomo",
          "Relleno(PX(a.XBase), PY(a.YTerreno), PX(a.XDadoIzq), PY(a.YZapTop), PincelTerreno);"
          in zap_cb
          and "Relleno(PX(a.XDadoDer), PY(a.YTerreno), PX(a.XDer), PY(a.YZapTop), PincelTerreno);"
          in zap_cb)
    check("los rellenos van ANTES del acero, para no taparlo",
          zap_cb.index("LOS RELLENOS, primero") < zap_cb.index("EL ACERO"))
    check("la previa dibuja las longitudinales del dado con su pata",
          "private void DibujarLongitudinalesPrevias(" in zap_cb
          and "TrazoZapata.BarrasRectangulares(" in zap_cb
          and "var largo = factor * Math.Max(dSup, dInf);" in zap_cb)
    check("y la pata usa los diametros de la casilla, no los 15 fijos",
          "var factor = TrazoZapata.FactorGanchoValido(FactorGanchoElegido);" in zap_cb)
    check("y la transicion 1:6 sale de la misma cuenta que el dibujante",
          "TrazoZapata.Desplazamiento(dxMax, a.YZapTop, a.YDadoTop, recDado)" in zap_cb
          and "dxMax <= TrazoZapata.DesplazamientoMax" in zap_cb)
    check("hay leyenda de colores en el cuadro",
          "private void LeyendaZapata(" in zap_cb
          and "LeyendaZapata(" in zap_cb
          and '"transición 1:6"' in zap_cb)
    check("y el dado de la planta lleva su relleno y su ID",
          "Relleno(PX(hx1), PY(hy2), PX(hx2), PY(hy1), PincelConcreto);" in zap_cb
          and "var idDado = (z.IdDado ?? string.Empty).Trim();" in zap_cb)

    check("la vista previa lleva cotas",
          "private void CotaH(" in zap_cb and "private void CotaV(" in zap_cb)
    check("la elevacion acota los tramos, el espesor y la profundidad",
          "CotaH(PX(a.XDadoIzq), PX(a.XDadoDer), yCad" in zap_cb
          and "CotaV(x1, PY(a.YZapBot), PY(a.YZapTop), z.EspesorM, gris);" in zap_cb
          and "CotaV(x2, PY(a.YPlantillaBot), PY(a.YTerreno)" in zap_cb)
    check("y la planta acota la zapata y el dado",
          "CotaV(PX(a.XBase) - (0.12 * escala), PY(yBot), PY(yTop), z.LargoM, gris);" in zap_cb
          and "CotaV(PX(a.XDer) + (0.10 * escala), PY(hy1), PY(hy2)" in zap_cb
          and "CotaH(PX(hx1), PX(hx2), PY(yTop) - (0.10 * escala)" in zap_cb)
    # La previa tiene que ensenar lo que va a salir: las verticales de la elevacion van a la
    # IZQUIERDA, pegadas a la cimentacion, y con las mismas distancias que usa el dibujante.
    check("la previa saca las verticales a la izquierda, con las distancias del dibujante",
          "var x1 = PX(a.XBase) - (TrazoZapata.AnotacionCotaVert1 * escala);" in zap_cb
          and "var x2 = PX(a.XBase) - (TrazoZapata.AnotacionCotaVert2 * escala);" in zap_cb)
    # Y EL BOTON DE DIBUJAR, con el color de los de concreto y acero: PrimaryButtonStyle.
    check("el boton de dibujar zapatas lleva el color de los otros dos",
          'x:Name="DibujarZapatasButton"' in xaml
          and 'Content="Dibujar zapatas en AutoCAD"' in xaml
          and xaml.split('Content="Dibujar zapatas en AutoCAD"')[1]
              .split("/>")[0].find("PrimaryButtonStyle") > 0
          and 'Content="Revisar zapatas"' in xaml)
    check("los numeros de las cotas van en metros con dos decimales",
          'valorM.ToString("N2"' in zap_cb)

    # EL DADO SE ELIGE DE LA HOJA DE CONCRETO, y la lista se actualiza sola.
    check("el dado se elige de una lista",
          "public static ObservableCollection<string> DadosDisponibles" in zap_row
          and "ZapataAisladaRow.DadosDisponibles" in xaml)
    check("la lista sale de los dados de la hoja de concreto",
          "private void ActualizarDadosDisponibles()" in zap_cb
          and ".Where(s => EsDado(s.Elemento))" in zap_cb)
    check("y se actualiza en cada cambio de esa hoja",
          "ActualizarListasDeZapatas();" in codigo)
    # LO QUE SE REPORTO: "en la seccion de dado no me aparece el que tengo". La lista se refrescaba
    # al AGREGAR o BORRAR una fila, pero no al EDITARLA, y el ID y el elemento se escriben editando:
    # la lista se armaba con la fila en blanco y no volvia a mirarla.
    check("la lista se refresca tambien al EDITAR una fila, no solo al agregarla",
          "ActualizarListasDeZapatas();" in leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
          .split("private void OnFilaEditada")[1].split("private void DatosCambiaron")[0])
    check("y lo mismo en la hoja de acero, que tambien aporta columnas",
          "ActualizarListasDeZapatas();" in leer(ruta("client/src/CadLink.App/MainWindow.Acero.cs"))
          .split("private void OnFilaAceroEditada")[1].split("private void")[0])
    check("queda escrito el defecto que arregla",
          "no me aparece el dado que tengo" in leer(
              ruta("client/src/CadLink.App/MainWindow.xaml.cs")))
    check("se actualiza EN SITIO, no se sustituye la coleccion",
          "sin sustituir la colección" in zap_cb
          and "lista.Clear();" in zap_cb)

    # LA COLUMNA, igual que el dado, y de las DOS hojas: una columna de acero tambien
    # desplanta en una zapata -y es la que hace que el dado remate con placa base-, asi que
    # ofrecer solo las de concreto dejaria la mitad del trabajo fuera de la lista.
    check("la columna se elige de una lista",
          "public static ObservableCollection<string> ColumnasDisponibles" in zap_row
          and "ZapataAisladaRow.ColumnasDisponibles" in xaml)
    check("la lista trae las columnas de las DOS hojas",
          "private void ActualizarColumnasDisponibles()" in zap_cb
          and "EsColumnaDeConcreto(s.Elemento)" in zap_cb
          and "PerfilAceroRow.ElementoColumna.Equals(" in zap_cb)
    check("y cada una dice de que hoja sale",
          '$"{id} (concreto)"' in zap_cb and '$"{id} (acero)"' in zap_cb)
    check("pero se guarda SOLO el ID, que es lo que va al plano",
          "public static string SoloElId(" in zap_row
          and "set => Set(ref _idColumna, SoloElId(value));" in zap_row)
    check("la hoja de acero tambien refresca la lista",
          "ActualizarListasDeZapatas();"
          in leer(ruta("client/src/CadLink.App/MainWindow.Acero.cs")))

    # Lo que se pidio de la lista: que no ofrezca dos entradas iguales. Lo que NO se pidio
    # -y se hizo mal- era prohibir que dos zapatas usen la misma columna.
    check("revisar avisa si la columna no esta capturada",
          "no está capturada, ni en" in zap_cb)

    # UNA MISMA COLUMNA SI PUEDE ESTAR EN VARIAS CIMENTACIONES. Lo que se captura en la
    # hoja de secciones es el TIPO de columna -«C-01» es la de 40x40 con su armado- y ese
    # tipo se repite en todas las zapatas donde toque. Se reportaba como error, y ademas
    # impedia dibujar porque el boton se niega cuando hay problemas.
    check("repetir la columna en varias zapatas YA NO es un error",
          "Una columna se apoya en una sola zapata." not in zap_cb
          and "ya desplanta en otra zapata" not in zap_cb)
    check("y no puede volver a bloquear el dibujo",
          "columnasUsadas.Add(idCol)" not in zap_cb)
    check("en su lugar se CUENTA en cuantas zapatas esta cada columna",
          "out List<string> columnasRepetidas" in zap_cb
          and "desplanta en {par.Value.Count} zapatas" in zap_cb
          and "columnasUsadas.Where(p => p.Value.Count > 1)" in zap_cb)
    check("y se enseña diciendo que es normal, no como reproche",
          "es normal: el ID es el TIPO de columna" in zap_cb)
    check("y queda escrito por que era un error prohibirlo",
          "una misma columna sí puede estar en varias cimentaciones" in zap_cb
          and "impedía dibujar" in zap_cb)
    check("y el XAML tampoco lo llama error",
          "REPETIR LA MISMA COLUMNA EN VARIAS ZAPATAS NO ES UN ERROR" in xaml)

    # ------------------------------------------------------------------
    # LAS MEDIDAS SE TRAEN SOLAS DE LA SECCION ELEGIDA
    # ------------------------------------------------------------------
    # Lo que se pidio: «QUE CUANDO SELECCIONE LA COLUMNA EN AUTOMATICO TENGA LA MEDIDA REAL
    # YA REFERENCIADA SIN NECESIDAD QUE YO LE MUEVA». El ID ya tiene su seccion capturada
    # con su ancho y su recubrimiento; teclearlos otra vez era pedir dos veces el mismo dato.
    check("al elegir la columna o el dado se traen sus medidas",
          "private void ReferenciarMedidas(ZapataAisladaRow fila)" in zap_cb
          and "private void ReferenciarColumna(ZapataAisladaRow fila)" in zap_cb
          and "private void ReferenciarDado(ZapataAisladaRow fila)" in zap_cb)
    check("y se dispara justo al cambiar el ID de la celda",
          "e.PropertyName == nameof(ZapataAisladaRow.IdColumna)" in zap_cb
          and "e.PropertyName == nameof(ZapataAisladaRow.IdDado)" in zap_cb)
    check("la columna de concreto trae su base y su recubrimiento",
          "fila.AnchoColumnaCm = col.BaseCm;" in zap_cb
          and "fila.RecColumnaCm = col.RecubrimientoCm;" in zap_cb)
    check("el perfil de acero trae su peralte, que es lo que se ve en el corte",
          "perfil.PeralteCm > 0 ? perfil.PeralteCm : perfil.AnchoCm" in zap_cb)
    check("y el TIPO de columna se pone solo, concreto o acero",
          "fila.TipoColumna = ZapataAisladaRow.TipoColumnaConcreto;" in zap_cb
          and "fila.TipoColumna = ZapataAisladaRow.TipoColumnaAcero;" in zap_cb)
    check("el dado trae su ancho y su recubrimiento",
          "fila.AnchoDadoCm = dado.BaseCm;" in zap_cb
          and "fila.RecDadoCm = dado.RecubrimientoCm;" in zap_cb)

    # Es una REFERENCIA, no una copia que envejece: si la seccion cambia, la zapata se pone
    # al dia sola. Sin esto, cambiar la columna de 40 a 45 en su hoja dejaria las zapatas
    # dibujandose con 40 y nada lo diria.
    check("y si la seccion cambia despues, la zapata se pone al dia sola",
          "ReferenciarMedidasDeTodas();" in zap_cb
          and "private void ReferenciarMedidasDeTodas()" in zap_cb)
    check("nunca se escribe un cero encima de un dato bueno",
          "if (col.BaseCm > 0)" in zap_cb
          and "if (dado.BaseCm > 0)" in zap_cb)
    check("y queda escrito que la referencia es la seccion",
          "Es una referencia, no una copia que se queda vieja" in zap_cb
          and "la medida real" in zap_cb)
    check("las celdas dicen en su globo que se llenan solas",
          "Se llena sola con el ancho real del dado elegido" in xaml
          and "Se llena sola con la medida real de la columna elegida" in xaml)

    # Un solo sitio decide que es un dado y que es una columna.
    check("que es un dado y que es una columna se decide en un solo sitio",
          "private static bool EsColumnaDeConcreto(string? elemento)" in zap_cb
          and "private static bool EsDado(string? elemento)" in zap_cb
          and ".Where(s => EsDado(s.Elemento))" in zap_cb)
    check("y la celda sigue siendo editable, con su lista en el XAML",
          'ItemsSource="{Binding Source={x:Static models:ZapataAisladaRow.DadosDisponibles}}"'
          in xaml)

    # Los dos botones de la hoja.
    check("la hoja de zapatas tiene su boton de revisar, y funciona",
          'Click="OnRevisarZapatas"' in xaml
          and "private void OnRevisarZapatas(" in zap_cb)
    check("y dice donde se va a dibujar cada una",
          "Donde se va a dibujar cada una" in zap_cb)
    # LO QUE SE PIDIO: «HABILITA EL BOTON DE DIBUJAR ZAPATAS AISLADAS». Ya no esta apagado
    # y ya no avisa de que el dibujante falta: existe.
    m_boton = re.search(r'<Button x:Name="DibujarZapatasButton".*?/>', xaml, re.S)

    check("el boton de dibujar zapatas esta puesto y ENCENDIDO",
          m_boton is not None and 'IsEnabled="False"' not in m_boton.group(0))
    check("y ya no dice que el dibujante es el paso siguiente",
          "el dibujante de zapatas es el paso siguiente" not in xaml
          and "El dibujante de zapatas todavía no está" not in zap_cb)
    check("y quien lo apaga ahora es la licencia, no el XAML",
          "DibujarZapatasButton.IsEnabled = puedeDibujar;" in codigo)
    check("y su globo dice lo que hace de verdad",
          m_boton is not None
          and "su corte y su planta" in m_boton.group(0)
          and "no dibuja si falta algo" in m_boton.group(0))

    # El tipo y el desplanta van por PLANTILLA con ComboBox editable enlazado por Text.
    # Con SelectedItemBinding y la lista llenada desde el code-behind, el enlace pisaba el
    # valor capturado: las dos zapatas del ejemplo salian «de lindero».
    # LA SEPARACION DE ESTRIBOS SE ESCRIBE A MANO. La lista son sugerencias, no una lista
    # cerrada, y con SelectedItemBinding lo que se teclea no llega a la propiedad: se pierde
    # al salir de la celda. Es el mismo patron que la columna «Sep cm» del concreto.
    check("la separacion de estribos de la zapata se puede escribir a mano",
          'Text="{Binding SepEstriboDado, UpdateSourceTrigger=PropertyChanged}"' in xaml
          and "ColZapSepEstribo" not in zap_cb)
    check("y su lista sigue siendo la misma del concreto, un solo sitio",
          'ItemsSource="{Binding Source={x:Static models:SeccionConcretoRow.SeparacionesUsuales}}"'
          in xaml)

    check("el tipo de zapata se enlaza por Text, no por SelectedItem",
          'Text="{Binding Tipo, UpdateSourceTrigger=PropertyChanged}"' in xaml
          and "ColTipoZapata" not in zap_cb)
    check("y se dice por que, que es el defecto que se vio",
          "el enlace lo PISA" in xaml)
    check("se redibuja al cambiar de fila, de tamaño y al editar",
          "private void EngancharVistaPreviaZapata()" in zap_cb
          and "private void OnFilaZapataEditada(" in zap_cb)
    check("y solo si la fila editada es la que se esta viendo",
          "ReferenceEquals(sender, ZapatasGrid.SelectedItem)" in zap_cb)
    check("la pestaña se llena y se enlaza al arrancar",
          "LlenarListasZapatas();" in codigo
          and "EnlazarZapatas();" in codigo
          and "EngancharVistaPreviaZapata();" in codigo)
    check("y se dice lo que a la vista previa le falta todavia",
          "Lo que todavía no está" in zap_cb and "los rellenos de concreto" in zap_cb)

    # ------------------------------------------------------------------
    # EL DIBUJANTE DE ZAPATAS EN AUTOCAD
    # ------------------------------------------------------------------
    # Lo que se pidio: habilitar el boton. Un boton encendido con un dibujante a medias
    # seria peor que el boton apagado, asi que aqui se comprueba el dibujante entero.
    zap_drw = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.cs"))
    zap_pla = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.Planta.cs"))
    zap_trz = leer(ruta("client/src/CadLink.Cad/TrazoZapata.cs"))
    zap_ui = leer(ruta("client/src/CadLink.App/MainWindow.Zapatas.cs"))
    zap_todo = zap_drw + zap_pla

    check("existe el dibujante de zapatas, en dos archivos parciales",
          "public sealed partial class ZapataDrawer" in zap_drw
          and "public sealed partial class ZapataDrawer" in zap_pla)

    # ES UN PORT, RUTINA POR RUTINA. Cada una lleva el nombre del VBA en su comentario, que es
    # lo que permite cotejarlas. La version anterior dibujaba «una zapata» y le faltaba casi
    # todo: el acero del dado y de la columna, el bloque, los rotulos y el modo de relleno.
    for vba in ("DibujarContornoZapataConDado", "DibujarPlantillaConcretoSimple",
                "DibujarPlantillaTexto", "DibujarHatchTerreno", "DibujarHatchConcretoRect",
                "DibujarParrillaZapata", "DibujarBarraLongitudinalUnica",
                "DibujarGanchoContinuoLimpio", "DrawVerticalElementFromAlzados",
                "DrawStirrupsCapsulesFront", "DibujarBarraGanchosRapido",
                "DibujarCaraSegmentada", "DrawBarLineTrimWithOffset", "DrawTwoOffsetSegment",
                "DibujarBreakLine", "PrepararUnionDadoColumna", "PosicionesBarrasElemento",
                "DibujarUnionDadoColumna", "DibujarDesplazamientoVarilla",
                "DibujarBarraVerticalBanda", "CotasAnchosZapataYDado",
                "CotasDoblezGanchosDado", "TextoRotuloElementoVertical",
                "RotularElementoVerticalLeader", "RotularParrillaInferiorZA",
                "RotularParrillaSuperiorZALindero", "AgregarLeaderRecto",
                "DibujarPlantaZapataAislada", "DibujarMallaPlanta", "EmitirBarraYConHueco",
                "DibujarSegBandaX", "DibujarSegBandaY", "RotularMallaPlanta",
                "DibujarBreakLineEntre", "NombreBloqueLibre", "CrearBloqueVacio",
                "InsertarBloqueCentroide", "InsertarBloqueDerecha", "AsegurarEstiloCota",
                "RellenarPoligonoSolido", "RellenarGanchoLSolido",
                "RellenarGanchoParrillaSolido", "RellenarBandaSegmentada", "PuntosArco",
                "ApplyCapsuleProtrusion", "BuildStirrupCentersUniforme", "SeparacionMinima",
                "VarLayerName", "NormalizeDiaLabel", "DibujarCirculoRelleno",
                "AplicarContornoVarilla", "CrearMTextoCentradoMascara", "AgregarTexto"):
        # Algunas viven en TrazoZapata, que es la geometria compartida con la vista previa.
        check(f"esta portada la rutina {vba}", vba in zap_todo or vba in trazo_zap)

    check("y toda la geometria compartida sigue saliendo de TrazoZapata",
          "TrazoZapata.Colocar(z, xBase)" in zap_drw
          and "TrazoZapata.XBase(z.Tipo, anchos, i)" in zap_drw
          and "TrazoZapata.CentrosEstribos(" in zap_drw
          and "TrazoZapata.CentrosUniformes(" in zap_drw
          and "TrazoZapata.HuecoDelDado(z, xIzq, yBot)" in zap_pla
          and "TrazoZapata.Posiciones(" in zap_pla)

    # EL ELEMENTO VERTICAL SE CALCULA TUMBADO Y SE ROTA 90 GRADOS, como la macro. La rotacion
    # se aplica a cada PUNTO al dibujarlo, no recorriendo el dibujo despues.
    check("el dado y la columna se calculan tumbados y se rotan 90 grados",
          "private double GX(double x, double y) => _rot ? _rx0 - (y - _ry0) : x;" in zap_pla
          and "private double GY(double x, double y) => _rot ? _ry0 + (x - _rx0) : y;" in zap_pla
          and "private double GA(double a) => _rot ? a + (Math.PI / 2) : a;" in zap_pla)
    check("y queda escrito que la barra superior local es el paño izquierdo global",
          "la barra «superior» local es la del paño <b>izquierdo</b>" in zap_drw)

    # EL ACERO DE ARRANQUE: es lo que faltaba por completo.
    check("el dado y la columna llevan sus barras de arranque",
          "private void BarraConGanchos(" in zap_drw
          and "BarraConGanchos(xaBot, xbBar, ycSup, dSup, CapaVar(diaSup)" in zap_drw
          and "BarraConGanchos(xaBotInf, xbBar, ycInf, dInf, CapaVar(diaInf)" in zap_drw)
    # EL DOBLEZ, EN DIAMETROS Y CAMBIABLE PARA TODAS. La macro lo trae fijo en 15; la hoja lleva una
    # casilla para poner 40 -o los que hagan falta- y con ese valor salen el dibujo Y SUS COTAS.
    check("el gancho de arranque se mide en diametros y sale de un solo sitio",
          "FactorGancho * dSup" in zap_drw
          and "FactorGancho * dInf" in zap_drw
          and "public double FactorGanchoDiametros { get; set; }" in zap_drw
          and "private double FactorGancho => TrazoZapata.FactorGanchoValido(FactorGanchoDiametros);"
          in zap_drw)
    check("los 15 de la macro quedan como valor por omision, no como unico",
          "public const double FactorGanchoAbajo = 15.0;" in trazo_zap
          and "public static double FactorGanchoValido(double diametros)" in trazo_zap
          and "FactorGanchoMinimo = 6.0" in trazo_zap
          and "FactorGanchoMaximo = 80.0" in trazo_zap)
    check("y las COTAS del doblez usan el mismo factor que el dibujo",
          "xIzq2 = xIzq1 - (FactorGancho * dSup);" in zap_drw
          and "xDer2 = xDer1 + (FactorGancho * dInf);" in zap_drw)
    check("la casilla esta en la hoja de zapatas, no por fila",
          'x:Name="ZapGanchoDiametrosBox"' in xaml
          and "Doblez del gancho de arranque:" in xaml
          and "es una decision del juego entero" in xaml.lower()
             or "decision del juego entero" in xaml)
    check("lo que se captura llega al dibujante",
          "FactorGanchoDiametros = FactorGanchoElegido" in zap_cb
          and "private double FactorGanchoElegido =>" in zap_cb)
    check("y se guarda en el trabajo, con los 15 por omision para un archivo viejo",
          "public double GanchoZapatasDiametros { get; set; } = 15.0;"
          in leer(ruta("client/src/CadLink.App/Models/Proyecto.cs"))
          and "GanchoZapatasDiametros = FactorGanchoElegido" in codigo
          and "FactorGanchoValido(p.GanchoZapatasDiametros)" in codigo)
    check("la casilla dice lo que significa, en centimetros de una #4",
          "diámetros = " in zap_cb
          and 'DiametroCmDeVarilla("#4")' in zap_cb)
    check("y con las intermedias cortadas en cada estribo",
          "private void BarraRectaSegmentada(" in zap_drw
          and "private void CaraSegmentada(" in zap_drw)
    check("los ganchos del lindero doblan LOS DOS a la izquierda",
          "ganchosAmbosIzq" in zap_drw
          and "bendIniSup = true;" in zap_drw)

    # EL GANCHO DE REMATE -el de ARRIBA- DOBLA HACIA ADENTRO EN LAS DOS BARRAS. Antes las
    # dos doblaban al mismo lado y la del paño DERECHO se salia del dado; un gancho fuera
    # del paño se queda en el recubrimiento y no ancla nada. Da igual si la columna es de
    # concreto o de acero: eso cambia el pie de abajo, no el remate.
    # OJO: el elemento vertical se dibuja GIRADO, asi que bendUp en locales es la izquierda
    # en globales; por eso la barra izquierda va con false y la derecha con true.
    check("el gancho de remate de la barra izquierda dobla hacia el nucleo",
          "hookIniSup, bendIniSup, hookFinSup, false, false, false);" in zap_drw)
    check("y el de la derecha tambien, hacia el otro lado",
          "hookIniInf, bendIniInf, hookFinInf, true, true, false);" in zap_drw)
    check("con su explicacion, que es facil de confundir",
          "bendUp en LOCALES es la IZQUIERDA en globales" in zap_drw
          and "bendIniInf = true;" in zap_drw)
    check("y si las patas se alcanzarian, una se sube",
          "private double DesfaseDeLosGanchos(" in zap_drw
          and "(2 * dMax) + 0.005" in zap_drw)
    # El traslape a 1:6 -RELACION_DESPLAZAMIENTO- y, si el dado es tan bajo que no caben esos
    # seis, se AVISA en lugar de dibujar un doblez mas parado y callarlo.
    check("el traslape va a 1:6 SIEMPRE, o no se dibuja",
          "TrazoZapata.Desplazamiento(union.DxMax" in zap_drw
          and "pediría " in zap_drw
          and "de doblez a 1:6 y en el dado solo hay " in zap_drw
          and "RelacionDesplazamiento" in trazo_zap)

    check("la union dado-columna dibuja el desplazamiento de cada barra",
          "private Union PrepararUnion(" in zap_drw
          and "private void DesplazamientoVarilla(" in zap_drw
          and "RelacionDesplazamiento" in zap_drw)
    # EN ORDEN, no por cercania: el de cercania -el de la macro- cruza dos barras cuando la
    # primera del dado queda mas cerca de la segunda de la columna.
    check("y las intermedias se emparejan EN ORDEN, para que no se cruzen",
          "var pares = intermediasIguales ? Math.Min(ordD.Count, ordC.Count) : 0;" in zap_drw
          and "mejorD" not in zap_drw)

    # Estribos y parrillas.
    check("los estribos van en capsula, con su ARCOFFSET y su protrusion",
          "private void CapsulasDeEstribo(" in zap_drw
          and "ArcOffset" in zap_drw
          and "TrazoZapata.Sobresalir(centros)" in zap_drw)
    check("el dado se salta 2 estribos en el lindero y 1 en la central",
          "var omitirEstribos = z.DobleParrilla" in zap_drw
          and "(lindero ? 2 : 1)" in zap_drw)
    check("las parrillas llevan su gancho de sector anular y sus transversales",
          "private void GanchoContinuo(" in zap_drw
          and "radioExt = diam + (diam / 2)" in zap_drw
          and "private void CirculoRelleno(" in zap_pla)

    # EL MODO DE RELLENO (celda B3 de la macro).
    # LO QUE SE PIDIO: el tipo de seccion ARRIBA y para TODAS, como en las secciones de
    # concreto, no una casilla por fila.
    check("el modo de relleno es del juego entero, no de cada zapata",
          "public bool SeccionRellena { get; set; }" in zap_drw
          and "_relleno = SeccionRellena;" in zap_drw
          and "public bool Relleno" not in trazo_zap)
    check("y lo mandan los mismos botones de la hoja de concreto",
          'x:Name="ZapTipo1Radio"' in xaml
          and "ElementName=Tipo1Radio" in xaml
          and "SeccionRellena = ModoElegido == ModoSeccion.Tipo2Rellena" in zap_cb)
    check("y queda escrito por que no va por fila",
          "no es un plano, son dos" in zap_drw)
    check("modo 1: solido 9 + AR-CONC 0.0003 color 251",
          "ColorSolidoRelleno = 9" in zap_drw
          and "EscalaConcretoRelleno = 0.0003" in zap_drw
          and "ColorPatronRelleno = 251" in zap_drw)
    check("modo 2: el AR-CONC de siempre a 0.0005",
          "EscalaConcretoNormal = 0.0005" in zap_drw)
    check("los estribos rellenos van en 152 y el contorno del acero en negro",
          "ColorEstriboRelleno = 152" in zap_drw
          and "ColorContornoNegro = 250" in zap_drw
          and "private void Var(object? ent)" in zap_pla)
    check("y los rellenos solo usan figuras que AutoCAD no rechaza",
          "private void RellenarQuad(" in zap_pla
          and "private void RellenarTriangulo(" in zap_pla
          and "private void RellenarCirculo(" in zap_pla
          and "nunca rechaza" in zap_pla)
    check("la casilla de doble parrilla es una lista de SI y NO",
          'Text="{Binding DobleParrilla, UpdateSourceTrigger=PropertyChanged}"' in xaml
          and 'ItemsSource="{Binding Source={x:Static models:ZapataAisladaRow.SiNo}}"' in xaml)

    # EL BLOQUE DE LA ZAPATA.
    check("la elevacion se mete en un bloque con el nombre de la zapata",
          "public bool ZapataComoBloque { get; set; } = true;" in zap_drw
          and "CrearBloqueVacio(nombreBloque, xBase, yZapBot)" in zap_drw
          and "CapaBloqueZapata" in zap_drw)
    check("y el terreno, las cotas y los rotulos quedan FUERA",
          "// ---------- El terreno: FUERA del bloque ----------" in zap_drw
          and "_cont = _ms;" in zap_drw)
    check("el texto de la plantilla va despues del bloque, para que no lo tape",
          "PlantillaTexto(xBase, yZapBot" in zap_drw
          and "el SOLID del bloque lo taparía" in zap_drw)

    # El acero en su capa por diametro, como el resto del plano.
    check("cada varilla va a su capa VAR_#n",
          'return e.Length == 0 ? "VAR_#3" : "VAR_" + e;' in zap_pla
          and "private void AsegurarCapaVarilla(" in zap_pla)

    # ------------------------------------------------------------------
    # LOS COLORES DE CAPA DE LA MACRO, EN UN SOLO SITIO
    # ------------------------------------------------------------------
    # Se reporto que una varilla del #5 salia BLANCA: la tabla de colores estaba
    # escrita solo en el dibujante de secciones, asi que el de zapatas creaba
    # VAR_#5 sin color y AutoCAD la dejaba en el 7. Ahora la tabla es de todos y el
    # color se FUERZA para las capas de la macro, como hace su CrearCapa.
    capas_cad = leer(ruta("client/src/CadLink.Cad/CapasCad.cs"))

    for capa, color in (("VAR_#2", 150), ("VAR_#2.5", 6), ("VAR_#3", 132),
                        ("VAR_#4", 142), ("VAR_#5", 160), ("VAR_#6", 4),
                        ("VAR_#8", 1), ("VAR_#10", 6), ("VAR_#12", 15),
                        ("TEXTOS", 3), ("CONCRETO", 8), ("ESTRIBOS", 150)):
        check(f"la capa {capa} lleva el color {color} de la macro",
              f'["{capa}"] = {color},' in capas_cad)

    check("la tabla de colores es una sola, y la usan los tres dibujantes",
          "CapasCad.ColorDeCapa(" in zap_pla
          and "CapasCad.ColorDeVarilla(" in leer(
              ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))
          and "CapasCad.ColorDeCapa(capa)" in leer(
              ruta("client/src/CadLink.Cad/AlzadoDrawer.cs")))
    check("la capa de varilla de la zapata se crea CON su color",
          "CrearCapa(capa, CapasCad.ColorDeCapa(capa), forzarColor: true);" in zap_pla)
    check("y a las capas de la macro se les pone su color aunque ya existan",
          "private void CrearCapa(string nombre, int color, bool forzarColor)" in zap_pla
          and "if (color > 0 && (nueva || forzarColor))" in zap_pla
          and "forzarColor: CapasCad.EsDeLaMacro(nombre));" in zap_pla)
    check("un diametro que no este en la tabla se queda sin color, no en blanco",
          "public const int SinColor = -1;" in capas_cad
          and "if (color != CapasCad.SinColor)" in leer(
              ruta("client/src/CadLink.Cad/SeccionDrawer.cs")))

    # Rotulos con leader.
    check("el dado y la columna llevan rotulo con leader",
          "private void RotuloConLeader(" in zap_drw
          and "private void RotuloDelDado(" in zap_drw
          and "private void RotuloDeLaColumna(" in zap_drw)
    check("y en el lindero salen a la IZQUIERDA",
          "LinderoRotuloElemDx" in zap_drw
          and "a.XDadoIzq - LinderoRotuloElemDx" in zap_drw)
    check("las parrillas tambien, con su AMBOS SENTIDOS",
          "private void RotuloParrillaInferior(" in zap_drw
          and "AMBOS SENTIDOS" in zap_drw)

    # ---- LOS ROTULOS DE PARRILLA, EN LA POSICION DE LA MACRO ----
    # Moverlos a ojo fue lo que los dejo encima de las cotas y del titulo. Estas comprobaciones
    # amarran las cuentas TAL CUAL vienen en las macros, con sus sumas y restas.
    check("el rotulo de la parrilla inferior va donde lo pone la macro",
          "var xTexto = xBase - 0.18 + 0.272 - 0.11 + DesplazamientoParrillaInfCentrar;" in zap_drw
          and "var yTexto = yZapBot + 0.1 + 0.4164 - 0.16;" in zap_drw)
    check("y ya no 46 cm mas abajo, encima de las cotas y del titulo",
          "var yTexto = yZapBot - 0.10;" not in zap_drw)
    check("el de la parrilla superior de la CENTRAL sale del pano derecho, no fuera del dibujo",
          "var xTexto = xBase + anchoZapata + 0.16 - 0.4302;" in zap_drw
          and "var yTexto = yZapTop + 0.02 + 0.2908 - 0.16;" in zap_drw
          and "xBase + anchoZapata + 0.10" not in zap_drw)
    check("el de la parrilla superior del LINDERO va centrado sobre el lomo",
          "var yTexto = yZapTop + LinderoRotuloSupDy;" in zap_drw
          and "LinderoRotuloSupDy = 0.23" in zap_drw)
    check("cuando las dos parrillas son distintas se parten en DOS rotulos",
          "anclaje: AnclajeIzquierda" in zap_drw
          and "anclaje: AnclajeDerecha" in zap_drw
          and "DesplazamientoVertical" in zap_drw)
    check("y el MText de verdad recibe ese anclaje",
          "int anclaje = AnclajeCentro" in zap_pla
          and "mt.AttachmentPoint = anclaje;" in zap_pla)
    check("estan todos los desplazamientos de la macro, con su nombre",
          "AnchoMtexto = 0.38" in zap_drw
          and "DesplazamientoInferiorX = -0.4818" in zap_drw
          and "DesplazamientoAmbosSentidos = -0.2" in zap_drw
          and "DesplazamientoInferiorAdicional = 0.15" in zap_drw
          and "DesplazamientoAmbosInferiorX = 0.09" in zap_drw
          and "DesplazamientoYAmbosAnclaje = -0.024" in zap_drw
          and "DesplazamientoYAmbosTexto = -0.011" in zap_drw
          and "DesplazamientoInferiorSuperiorAdicional = 0.0988" in zap_drw
          and "DesplazamientoParrillaInfCentrar = 0.2" in zap_drw)
    check("en la ELEVACION el rotulo no repite el titulo de la parrilla",
          '"PARRILLA INFERIOR", varBarra' not in zap_drw
          and '"PARRILLA SUPERIOR", varBarra' not in zap_drw
          and "PARRILLA INFERIOR" in zap_pla)
    check("la punta del leader cae sobre una varilla de verdad",
          "private static double CirculoMasCercano(" in zap_drw
          and "xPuntaCirc = CirculoMasCercano(" in zap_drw)

    # ---- EL RENGLON DE VARILLAS SUMA LAS DEL MISMO DIAMETRO ----
    check("el rotulo del elemento suma las varillas del mismo diametro",
          "private string TextoBarrasLongitudinales(" in zap_drw
          and "conteos[k] += n;" in zap_drw)
    check("y los conteos del rotulo salen de la seccion, no del dibujo",
          "NVarDadoSup" in zap_trz
          and "NVarIntDadoTotal" in zap_trz
          and "private static void ConteosDelRotulo(" in zap_ui)
    check("con la circular contando TODAS sus varillas",
          "nInt = s.NVarTotal > 2 ? s.NVarTotal - 2 : 0;" in zap_ui
          and "nSup = s.NEsqSup + s.NIntSup;" in zap_ui)
    check("y sin conteos se escriben los diametros, no un total inventado",
          "var hayConteos = nSup > 0 || nInf > 0 || nIntTotal > 0;" in zap_drw)
    check("el leader se saca del borde de la caja del texto",
          "private (double X1, double Y1, double X2, double Y2)? Caja(" in zap_pla
          and "RotuloVertGapLeader" in zap_drw)
    check("y la caja se mide por REFLEXION, no con dynamic",
          "private (double[] Min, double[] Max)? CajaEnvolvente(object ent)" in zap_pla
          and "ParameterModifier" in zap_pla
          and "no se puede invocar con 'dynamic'" in zap_pla)

    # Cotas y rotulos: TODO colgado de la ESQUINA INFERIOR DERECHA, que es lo que se pidio, para
    # que la anotacion entera viaje con la zapata y no se descuelgue cada parte por su lado.
    # LO QUE SE PIDIO, TEXTUAL: "que tan dificil es hacer que las cotas esten a la altura de la
    # seccion de cimentacion, si estas tomando en cuenta mis macros ahi no me hacia eso".
    # Las cotas verticales van A LA IZQUIERDA del pano izquierdo, pegadas a la cimentacion, y el
    # rotulo CENTRADO a 0.32 / 0.41 / 0.49 del desplante. Las dos cosas, como las macros.
    check("las cotas verticales van a la izquierda, pegadas a la cimentacion",
          "CotasVerticales(xBase, yZapBot, yZapTop, yTerreno, r);" in zap_drw
          and "var x1 = xBase - CotaOffsetVert1;" in zap_drw
          and "var x2 = xBase - CotaOffsetVert2;" in zap_drw
          and "xDer + CotaOffsetVert1" not in zap_drw)
    check("y los tres renglones del rotulo van CENTRADOS en el eje",
          "Texto(xCentro, yTitulo," in zap_drw
          and "alineacion: Alineacion.Centro" in zap_drw)
    check("la alineacion de texto es la de la macro: centrado o pegado a la izquierda",
          "private enum Alineacion" in zap_pla
          and "if (alineacion == Alineacion.Centro)" in zap_pla
          and "t.HorizontalAlignment = 4;" in zap_pla)
    check("y ya no queda una alineacion a la derecha que la macro no tiene",
          "Alineacion.Derecha" not in zap_pla
          and "Alineacion.Derecha" not in zap_drw)
    check("la planta acota el largo de la zapata a la izquierda y el del dado a la derecha",
          "PlantaCotaOffsetLargo = 0.12" in zap_pla
          and "Cota(xIzq - PlantaCotaOffsetLargo, yBot" in zap_pla
          and "Cota(xDer + PlantaCotaOffsetDado, dy1" in zap_pla)
    check("y su rotulo va centrado, como la macro",
          "Texto(xCen, yTitulo," in zap_pla
          and "Texto(xCen, yEscala," in zap_pla
          and "alineacion: Alineacion.Centro" in zap_pla)

    check("las cotas de la elevacion son las de la macro",
          "AnotacionCadena = 0.14" in trazo_zap
          and "AnotacionTotal = 0.22" in trazo_zap
          and "AnotacionCotaVert1 = 0.08" in trazo_zap
          and "AnotacionCotaVert2 = 0.16" in trazo_zap)
    # LO QUE SE PIDIO: "solo quiero que las cotas esten en su lugar, no que esten en medio de los
    # dibujos". Las de los 15 diametros de las patas del gancho iban PEGADAS A LA PATA, a 6 cm,
    # y la pata esta DENTRO del dado: la cota caia sobre el concreto, la parrilla y los estribos.
    check("las cotas de las patas del gancho van a 6 cm de su pata, como la macro",
          "AnotacionGancho = 0.06" in trazo_zap
          and "CotaDoblezOffset = TrazoZapata.AnotacionGancho" in zap_drw
          and "yPataSup - offset" in zap_drw
          and "yPataInf + offset" in zap_drw)
    check("se mide el arranque y la punta de cada pata",
          "Cota(xIzq2, yPataSup, xIzq1, yPataSup" in zap_drw
          and "Cota(xDer2, yPataInf, xDer1, yPataInf" in zap_drw)
    check("y queda escrito que bajarlas a un renglon propio salio peor",
          "renglón propio" in zap_drw)
    check("la cota de la plantilla lleva el numero EN MEDIO",
          "d.TextInside = true;" in zap_pla
          and "d.ForceLineInside = true;" in zap_pla
          and "d.TextMovement = 0;" in zap_pla)
    check("y la total arranca del fondo de la plantilla",
          "yPlantillaBot = yZapBot - TrazoZapata.PlantillaEspesor" in zap_drw)
    # ---- TODA LA ANOTACION CUELGA DE LA ESQUINA INFERIOR DERECHA ----
    # LO QUE SE PIDIO, TEXTUAL: "necesito que se alineen con la esquina inferior derecha para que
    # siempre se muevan con ese", "siempre lo pones mas abajo y a la izquierda de las cotas".
    # Antes habia TRES anclas -pano izquierdo para las verticales, desplante para la cadena y
    # fondo de la plantilla para el rotulo, y encima centrado en el eje-, asi que al cambiar el
    # ancho de una zapata cada anotacion se movia en una direccion distinta.
    check("las distancias de la anotacion son las de las macros, y viven juntas",
          "AnotacionCotaVert1 = 0.08" in trazo_zap
          and "AnotacionCotaVert2 = 0.16" in trazo_zap
          and "AnotacionCadena = 0.14" in trazo_zap
          and "AnotacionTotal = 0.22" in trazo_zap
          and "AnotacionGancho = 0.06" in trazo_zap
          and "AnotacionRotulo = 0.32" in trazo_zap)
    check("y el dibujante las toma de ahi, sin volver a escribir los numeros",
          "CotaOffsetVert1 = TrazoZapata.AnotacionCotaVert1" in zap_drw
          and "CotaOffsetCadena = TrazoZapata.AnotacionCadena" in zap_drw
          and "CotaDoblezOffset = TrazoZapata.AnotacionGancho" in zap_drw)
    check("el rotulo cuelga del DESPLANTE, a los 0.32 de la macro",
          "public static double YRotulo(double yZapBot, int renglon)" in trazo_zap
          and "TrazoZapata.YRotulo(yZapBot, 0)" in zap_drw
          and "TrazoZapata.YRotulo(a.YPlantillaBot, 0)" not in zap_drw
          and "RotuloSeparacion" not in trazo_zap)
    check("los saltos entre renglones si son los de la macro",
          "RotuloSalto1 = 0.09" in trazo_zap
          and "RotuloSalto2 = 0.17" in trazo_zap)
    # AQUI ESTABA EL ENCIMADO: no en la posicion del rotulo, sino en el ancho de letra con el que
    # se decide si cabe. Con el 0.62 de la macro el titulo "medía" 1.39 m y nunca se encogia; en el
    # dibujo mide 2.2 m y se salia 40 cm por cada lado, encima del titulo de al lado.
    check("el titulo se mide con el ancho de letra REAL del dibujo, no con el 0.62",
          "public const double FactorLetraTitulo = 1.0;" in trazo_zap
          and "TrazoZapata.FactorLetraTitulo" in zap_drw
          and "TrazoZapata.FactorLetraTitulo" in zap_pla)
    check("y queda escrito que ahi estaba el encimado",
          "AQUÍ ESTABA EL ENCIMADO" in trazo_zap)
    check("y el titulo se encoge si no cabe en su hueco, en vez de meterse en el vecino",
          "public static double AnchoParaElRotulo(double anchoM) => anchoM + SeparacionIzquierda;"
          in trazo_zap
          and "public static double AltoQueQuepa(" in trazo_zap
          and "TrazoZapata.AltoQueQuepa(titulo.Length, AltoTitulo, anchoRotulo," in zap_drw)
    check("la planta usa su propia esquina, con los renglones de la macro",
          "public static double YRotuloPlanta(double yBot, int renglon)" in trazo_zap
          and "PlantaTituloOffset = 0.24" in trazo_zap
          and "PlantaEscalaOffset = 0.33" in trazo_zap
          and "TrazoZapata.YRotuloPlanta(yBot, 0)" in zap_pla)
    # ------------------------------------------------------------------
    # LA TRANSICION DADO -> COLUMNA: DESPLAZAMIENTO DE VARILLA A 1:6
    # ------------------------------------------------------------------
    # El detalle del usuario: "DESPLAZAMIENTO DE VARILLA EN COLUMNA O TRABE, RELACION 1:6".
    # El alto del doblez se calculaba a 1:6 y DESPUES se recortaba a lo que quedaba libre en el
    # dado, asi que en un dado bajo el doblez salia mas parado que el detalle.
    check("el doblez de la transicion se resuelve en un solo sitio, a 1:6",
          "public const double RelacionDesplazamiento = 6.0;" in trazo_zap
          and "public static Transicion Desplazamiento(" in trazo_zap
          and "var alto = RelacionDesplazamiento * Math.Abs(dxMax);" in trazo_zap)
    check("y el alto ya NO se recorta a lo que quede libre en el dado",
          "var hZona = Math.Min(union.Alto, hMaxZona);" not in zap_drw
          and "public double Alto { get; set; }" not in zap_drw
          and "public double DxMax { get; set; }" in zap_drw)
    # Y EL DOBLEZ VIVE DENTRO DEL DADO: si se le deja pasar la junta, arriba ya estan las varillas
    # de la columna y en el plano se ven DUPLICADAS. Es lo que se reporto: "las varillas las
    # duplicas".
    check("el doblez acaba en la junta y no la pasa",
          "&& yDiagTop <= yDadoTop + 1e-9;" in trazo_zap
          and "CruzaLaJunta" not in trazo_zap
          and "varillas duplicadas" in trazo_zap)
    check("y la union no sube mas alla del recubrimiento de la columna",
          "var yZonaTop = yDadoTop + recColM;" in zap_drw
          and "dibujar dos veces la misma varilla" in zap_drw)
    check("si el doblez no cabe, las varillas se dejan RECTAS y se avisa",
          "var aplicarUnion = union.Activa && trans.Cabe && recorteCabe;" in zap_drw
          and "se dejan rectas y la columna se traslapa" in zap_drw)
    # La otra mitad del duplicado: al dado se le recortan las varillas en yZonaBot para que la
    # union siga desde ahi, y ElementoVertical IGNORA un recorte que no deje barra. Si se ignora y
    # la union dibuja igual, salen las dos.
    check("y no se dibuja la union si el recorte del dado no se puede aplicar",
          "var recorteCabe = yZonaBot > yZapBot + recDadoM + subirGanchoDado + 0.02;" in zap_drw
          and "el recorte no llegaba a aplicarse" in zap_drw)
    check("la varilla se dibuja sin vertices de longitud cero",
          "if (yd1 > yBot + 1e-9)" in zap_drw
          and "if (yTop > yd2 + 1e-9)" in zap_drw)
    # LAS BARRAS NO PUEDEN CRUZARSE: emparejado EN ORDEN, no por cercania.
    check("las varillas se emparejan en orden, asi que no pueden cruzarse",
          "var ordD = xIntD.OrderBy(x => x).ToList();" in zap_drw
          and "u.Dobleces.Add((ordD[k], ordC[k], dIntD, CapaVar(diaIntD)));" in zap_drw
          and "DOS BARRAS NO PUEDEN CRUZARSE" in zap_drw)
    check("el corrimiento maximo que se dobla son los 12 cm de la macro",
          "public const double DesplazamientoMax = 0.12;" in trazo_zap
          and "TrazoZapata.DesplazamientoMax" in zap_drw)
    check("y el doblez se dibuja hasta donde dice el 1:6, no hasta la junta",
          "DibujarUnion(union, yZonaBot, yDiagTop, yZonaTop" in zap_drw)

    # LO QUE NO TRAEN LAS MACROS: el caso REDONDO. Se arma con la misma idea que el cuadrado.
    check("las varillas de un elemento redondo se proyectan sobre su diametro",
          "public static BarrasElemento BarrasCirculares(" in trazo_zap
          and "radio * Math.Cos(ang)" in trazo_zap
          and "(Math.PI / 2) + (2 * Math.PI * k / nTotal)" in trazo_zap)
    check("y dos varillas simetricas se ven como UNA en el alzado",
          "xs.Any(v => Math.Abs(v - x) < tol)" in trazo_zap
          and "en el alzado son una sola" in trazo_zap)
    # LA UNION PARTE DE DONDE EL ALZADO DIBUJA LAS VARILLAS, no de otra cuenta: si no, los dobleces
    # no arrancan encima de las varillas y se ven despegados.
    check("la union usa las mismas posiciones que dibuja el alzado",
          "private static TrazoZapata.BarrasElemento BarrasDelElemento(" in zap_drw
          and "TrazoZapata.BarrasRectangulares(xCaraDer, w, recM, dSup, dInf, nInt);" in zap_drw
          and "TrazoZapata.BarrasCirculares(" not in zap_drw
          and "no arrancarían encima de las varillas" in zap_drw)
    check("y la proyeccion del redondo queda lista, marcada como pendiente de enganchar",
          "Todavía no la usa el dibujante" in trazo_zap)
    # EL TEOREMA: UNA zona para todas. El 1:6 fija su ALTO con la varilla que mas se corre; las
    # demas salen mas tendidas y el nudo se ve PAREJO.
    check("hay UNA zona de doblez y la comparten todas las varillas",
          "EL TEOREMA" in zap_drw
          and "DesplazamientoVarilla(x1, x2, yZonaBot, yZonaBot, yDiagTop, yZonaTop, dia, capa);"
          in zap_drw)
    # LO QUE SE PIDIO: "esos tramos de varilla recta que no van a ningun lado, esas ya no van".
    # Eran las varillas del dado sin pareja en la columna, que la macro seguia rectas hasta el tope
    # del dado y quedaban entre los dobleces.
    check("en la zona de doblez SOLO van los dobleces",
          "EN LA ZONA SOLO VAN LOS DOBLECES" in zap_drw
          and "private void DibujarUnion(Union u, double yZonaBot, double yDiagTop, "
              "double yZonaTop)" in zap_drw
          and "u.Rectas" not in zap_drw)
    check("y la rutina de la varilla recta se quito, no se dejo sin usar",
          "private void BarraVerticalBanda(" not in zap_drw
          and "El port de DibujarBarraVerticalBanda YA NO ESTÁ" in zap_drw)
    check("las que se quedan sin pareja se cuentan y se avisan",
          "public int SinPareja { get; set; }" in zap_drw
          and "u.SinPareja = Math.Max(ordD.Count - pares, 0);" in zap_drw
          and "no tienen pareja " in zap_drw)
    # LA OTRA FUENTE DE VARILLAS DUPLICADAS: el recorte que se DESCARTABA. Si no dejaba 2 cm de
    # barra, ElementoVertical lo ignoraba y dibujaba la varilla completa; con el recorte de la zona
    # de dobleces, eso es la varilla entera MAS su doblez encima.
    # EL DADO SIN VARILLAS INTERIORES: la macro tiene un respaldo que faltaba. Si no se dijo el
    # diametro de las intermedias, se usa el de las de esquina:
    #     If Len(NormalizeDiaLabel(txtIntDado)) = 0 Then txtIntDado = txtAA7
    # Sin el, una seccion que declara intermedias pero no su diametro deja el dado sin ninguna.
    check("si falta el diametro de las intermedias, se usa el de las de esquina",
          "var diaIntDado = Diam(z.VarIntDado) > 0 ? z.VarIntDado : z.VarDadoSup;" in zap_drw
          and "var diaIntCol = Diam(z.VarIntColumna) > 0 ? z.VarIntColumna : z.VarColSup;"
          in zap_drw
          and "SIN VARILLAS INTERIORES" in zap_drw)
    check("y el rotulo usa el MISMO diametro que el dibujo",
          "RotuloDelDado(z, a, lindero, diaIntDado);" in zap_drw
          and "RotuloDeLaColumna(z, a, lindero, diaIntCol);" in zap_drw
          and "z.NIntDado, diaInt, z.EstriboDado" in zap_drw)
    # NI UN TRAMO RECTO DONDE ARRANCA EL 1:6: con union, las varillas del dado acaban EXACTAMENTE
    # en yZonaBot. Pedir el recorte no bastaba: por debajo se le restan holguras y margenes.
    check("con union, las varillas del dado acaban justo donde arrancan los dobleces",
          "topeBarras: aplicarUnion ? yZonaBot : null);" in zap_drw
          and "xbBar = x0 + (tope - y0);" in zap_drw
          and "if (!hayCorte && !omitGanchoFin && topeBarras is null)" in zap_drw)
    check("y queda escrito que un milimetro de mas es un tramo recto asomando",
          "es un tramo recto asomando por debajo del 1:6" in zap_drw)

    check("el recorte de las varillas se aplica siempre, recortado si hace falta",
          "var maximo = Math.Max(xb - (xaBot + 0.02), 0);" in zap_drw
          and "xbBar = xb - Math.Min(recorteBarrasFin, maximo);" in zap_drw
          and "NUNCA SE IGNORA" in zap_drw)
    check("y la union comprueba el recorte con la MISMA cuenta, recubrimiento incluido",
          "var recorteCabe = yZonaBot > yZapBot + recDadoM + subirGanchoDado + 0.02;" in zap_drw)
    # LOS ESTRIBOS, AL FRENTE: se dibujan antes que las varillas y en la zona de dobleces quedaban
    # tapados. Es el draw order > bring to front de AutoCAD.
    # LA ZONA SE BARRE ANTES DE DIBUJAR LOS DOBLECES: es el port de RecortarVerticalesZonaDobleces,
    # que FALTABA. El recorte de las varillas del dado pasa por media docena de holguras y cualquiera
    # deja un pedazo asomando dentro del 1:6; la macro no confia en el recorte, barre la zona.
    check("la zona de dobleces se barre antes de dibujar la transicion",
          "private void RecortarVerticalesEnLaZona(" in zap_drw
          and "RecortarVerticalesEnLaZona(\n                        idxAntesDado" in zap_drw
          and "ESTA RUTINA FALTABA" in zap_drw)
    check("solo barre las capas VAR_ dentro de los panos del dado",
          'capa.StartsWith("VAR_", StringComparison.OrdinalIgnoreCase)' in zap_drw
          and "xm < xIzq - 0.02 || xm > xDer + 0.02" in zap_drw)
    check("los estribos NO se barren: esos si van en la zona",
          "Los estribos —capa" in zap_drw
          and "no se tocan" in zap_drw)
    check("lo que venia de mas abajo se recorta, no se pierde",
          "var desdeAbajo = mn[1] < yZonaBot - TrimTolVertical;" in zap_drw
          and "Var(Linea(xm, mn[1], xm, yZonaBot, capa));" in zap_drw)
    check("y se cuenta lo que se quito, para que se sepa",
          "se quitaron {borradas} resto(s) de varilla" in zap_drw)
    check("se barre solo desde donde empieza el acero del dado",
          "var idxAntesDado = CuentaDelContenedor();" in zap_drw
          and "private int CuentaDelContenedor()" in zap_drw)

    check("los estribos se suben al frente al final",
          "private void AlFrente(object cont, List<object> objetos)" in zap_drw
          and "tabla.MoveToTop(arr)" in zap_drw
          and "AlFrente(_cont, _estribos);" in zap_drw)
    check("se apuntan al dibujarlos, con sus dos caras y sus dos puntas",
          "Apuntar(_estribos, e1);" in zap_drw
          and "Apuntar(_estribos, a2);" in zap_drw
          and "private readonly List<object> _estribos = new();" in zap_drw)
    check("y la lista se vacia en cada zapata, para no arrastrar la anterior",
          "_estribos.Clear();" in zap_drw)
    check("el reordenado va por AcadArreglos, que es el que sabe pasar el arreglo",
          'AcadArreglos.Llamar("MoveToTop de la zapata"' in zap_drw)
    check("y queda escrito por que no se le da a cada varilla su propio doblez",
          "deja cada quiebre a una altura distinta" in zap_drw)
    check("la forma de la columna viaja desde su seccion, como la del dado",
          "public bool ColumnaCircular { get; init; }" in trazo_zap
          and "fila.ColumnaCircular = col.EsCircular;" in zap_cb
          and "ColumnaCircular = ColumnaCircular," in zap_row)
    check("y esta comprobado con numeros, con dado y columna redondos",
          "redondo: los extremos caen en la circunferencia del armado"
          in leer(ruta("tools/prueba-zapata/Program.cs"))
          and "ningun doblez se pasa de la junta ni deja de ser 1:6"
          in leer(ruta("tools/prueba-zapata/Program.cs")))

    check("y queda escrito que las cotas NO se mueven de donde las pone la macro",
          "NO SE MUEVEN DE AHÍ" in trazo_zap
          and "a la altura del dado" in trazo_zap)
    check("y el titulo dice CENTRAL o DE LINDERO, como la macro",
          '"ZAPATA AISLADA DE LINDERO' in zap_drw
          and '"ZAPATA AISLADA CENTRAL' in zap_drw)

    # La planta.
    check("la planta recorta la malla en los cruces y en el hueco del dado",
          "private void Malla(" in zap_pla
          and "cortes.Sort(" in zap_pla
          and "private void BarraYConHueco(" in zap_pla)
    check("y va en dos fases: primero los rellenos y despues los contornos",
          "for (var fase = 1; fase <= 2; fase++)" in zap_pla
          and "if (fase == 1)" in zap_pla)
    check("con doble parrilla lleva su linea de rotura en la diagonal",
          "LineaDeRoturaEntre(xIzq, yBot, xDer, yTop)" in zap_pla
          and "PlantaBreaklineColor = 250" in zap_pla)
    check("el dado se INSERTA como bloque, y en el lindero pegado al paño derecho",
          "private bool InsertarBloque(" in zap_pla
          and "alinearDerechaEn" in zap_pla
          and "ExisteBloque(nombre)" in zap_pla)
    check("y si el bloque no esta, se pone un rectangulo Y SE AVISA",
          "_dadosQueFaltan" in zap_pla
          and "no está en el dibujo" in zap_pla)
    check("el ID del dado viaja limpio, sin la hoja entre parentesis",
          "IdDado = SoloElId(IdDado)" in zap_row)
    check("la planta lleva sus cotas y su titulo",
          "PlantaCotaOffset = 0.12" in zap_pla
          and "TrazoZapata.YRotuloPlanta(yBot, 0)" in zap_pla
          and '"VISTA EN PLANTA' in zap_pla)

    # El armado de la COLUMNA sale de su seccion, no se vuelve a capturar.
    check("el armado de la columna se trae de su seccion",
          "fila.VarColSup = col.DiamEsqSup;" in zap_cb
          and "fila.EstriboColumna = col.Estribo;" in zap_cb
          and "public string VarColSup { get; init; }" in trazo_zap)
    check("y en la columna redonda las dos caras llevan la misma varilla",
          "var d = col.DiamVarTotalEfectivo;" in zap_cb
          and "fila.VarColSup = d;" in zap_cb)

    # ------------------------------------------------------------------
    # DADO CIRCULAR EN PLANTA: las varillas llegan al contorno redondo
    # ------------------------------------------------------------------
    # Lo que se pidio. Con el hueco cuadrado, entre la circunferencia y el cuadrado quedaban
    # cuatro esquinas de varilla que en la obra no se cortan.
    check("el dado circular llega a la planta",
          "public bool DadoCircular { get; init; }" in trazo_zap
          and "fila.DadoCircular = dado.EsCircular;" in zap_cb
          and "DadoCircular = DadoCircular," in zap_row)
    check("y con el dado redondo el recorte es la CUERDA del circulo",
          "private (double A, double B)? CorteDelHueco(" in zap_pla
          and "Math.Sqrt(dentro)" in zap_pla
          and "_huecoCircular" in zap_pla)
    check("las dos familias de varillas usan ese corte",
          'CorteDelHueco(y, enX: true, rX' in zap_pla
          and 'CorteDelHueco(x, enX: false, r' in zap_pla)
    check("y el dado redondo se dibuja redondo, con su relleno",
          "private void HatchCirculo(" in zap_pla
          and "Circulo(_hcx, _hcy, wDado / 2, CapaConcreto)" in zap_pla)
    check("y queda escrito el defecto que arregla",
          "cuatro esquinas de varilla" in trazo_zap)

    # ------------------------------------------------------------------
    # EL ARMADO DEL DADO SALE DE SU SECCION, sea redondo o cuadrado
    # ------------------------------------------------------------------
    # Lo que se pidio: en «Arranque 1» y «Arranque 2» van las varillas de las ESQUINAS del dado
    # que se selecciona, el numero de intermedias tambien, y los estribos se leen del dado.
    check("los arranques del dado se traen de su seccion",
          "fila.VarDadoSup = dado.DiamEsqSup;" in zap_cb
          and "fila.VarDadoInf = dado.DiamEsqInfEfectivo;" in zap_cb)
    check("y en el dado redondo, las dos caras llevan su varilla del circulo",
          "fila.VarDadoSup = d;" in zap_cb
          and "dado.DiamVarTotalEfectivo" in zap_cb)
    check("el numero de intermedias tambien sale de la seccion",
          "fila.NIntDado = IntermediasDeLaSeccion(dado);" in zap_cb
          and "fila.NIntDado = dado.NVarTotal > 2 ? (dado.NVarTotal - 2) / 2 : 0;" in zap_cb)
    check("y el estribo del dado y su separacion, tambien",
          "fila.EstriboDado = dado.Estribo;" in zap_cb
          and "fila.SepEstriboDado = dado.SeparacionCm;" in zap_cb)
    check("y queda escrito por que no se captura dos veces",
          "un arranque que no existe" in zap_cb)

    # Las INTERMEDIAS: de la hoja de secciones, y si «Intermedias» va en cero, de los lechos.
    # Sin ellas no hay union de varillas, y la union desaparecia sin decir nada.
    check("las intermedias del dado y de la columna salen de la seccion",
          "private static int IntermediasDeLaSeccion(SeccionConcretoRow s)" in zap_cb
          and "IntermediasDeLaSeccion(col)" in zap_cb
          and "IntermediasDeLaSeccion(dado)" in zap_cb)
    check("y si «Intermedias» va en cero se miran los lechos",
          "Math.Max(s.NIntSup, s.NIntInf)" in zap_cb
          and "private static string DiametroIntermediasDe(SeccionConcretoRow s)" in zap_cb)
    # LA COLUMNA CIRCULAR TAMBIEN TIENE INTERMEDIAS en el alzado: de las N del circulo, dos
    # se ven en las caras y las demas quedan en medio. Con cero, la redonda salia sin
    # intermedias y sin union con el dado.
    check("la columna circular lleva sus intermedias",
          "fila.NIntColumna = col.NVarTotal > 2 ? (col.NVarTotal - 2) / 2 : 0;" in zap_cb)
    check("y el dado circular tambien",
          "fila.NIntDado = dado.NVarTotal > 2 ? (dado.NVarTotal - 2) / 2 : 0;" in zap_cb)

    # LAS LISTAS TRAEN TODO: cuadradas, rectangulares y circulares, de concreto y de acero.
    check("la lista de columnas trae todas las de concreto, no solo dos nombres exactos",
          ".StartsWith(SeccionConcretoRow.ElementoColumna" in zap_cb
          and ".StartsWith(SeccionConcretoRow.ElementoDado" in zap_cb)
    check("y sigue trayendo las de acero",
          "PerfilAceroRow.ElementoColumna.Equals(" in zap_cb)
    check("y queda escrito el defecto de la lista que arregla",
          "se quedaba fuera de la" in zap_cb
          and "COLUMNA RECTANGULAR" in zap_cb)

    # LAS CELDAS YA REFERENCIADAS SE OCULTAN: se rellenan solas desde la seccion.
    m_zg = re.search(r'x:Name="ZapatasGrid".*?</DataGrid>', xaml, re.S)

    check("se puede leer la cuadricula de zapatas", m_zg is not None)

    if m_zg:
        rejilla = m_zg.group(0)

        for col in ("Arranque 1", "Arranque 2", "N int.", "Var int.", "Estribo", "Est. @ cm"):
            i = rejilla.index(f'Header="{col}"')
            j = rejilla.index(">", i)
            check(f"la columna «{col}» esta oculta, porque se rellena sola",
                  'Visibility="Collapsed"' in rejilla[i:j + 1])

    check("y queda escrito por que se ocultan y no se quitan",
          "se siguen guardando en el trabajo" in xaml)

    check("y queda escrito que sin intermedias no hay union",
          "unión de las varillas solo se dibuja" in zap_cb)

    # LAS COTAS DE LA PLANTA, EN ORDEN: cadena y total abajo, largos a los lados.
    # LAS COTAS DE LA PLANTA SON LAS DE LA MACRO. El turno pasado las cambie a cadena y
    # total abajo y estuvo MAL: en la planta ya estaban en orden.
    check("la planta acota el dado arriba y la zapata abajo, como la macro",
          "PlantaCotaOffset = 0.12;" in zap_pla
          and "PlantaCotaOffsetDado = 0.1;" in zap_pla
          and "PlantaCotaNivel2" not in zap_pla
          and "yTop + PlantaCotaOffsetDado" in zap_pla)
    check("y queda escrito por que cada largo va por su lado",
          "Los dos por el mismo lado se montaban" in zap_pla)

    # ------------------------------------------------------------------
    # LAS PATAS DEL DADO: ADENTRO CON COLUMNA DE CONCRETO, AFUERA CON ACERO
    # ------------------------------------------------------------------
    check("las patas del dado doblan segun el tipo de columna",
          "ganchoIniAfuera: z.ColumnaDeConcreto ? 0 : 1" in zap_drw)
    # LO QUE SE PIDIO: la regla es SOLO el tipo de columna, tambien en el lindero. Con
    # concreto las DOS patas van adentro del nucleo; con acero, una adentro y otra afuera.
    check("las dos patas van adentro con columna de concreto, tambien en el lindero",
          "ganchosAmbosIzq: false" in zap_drw
          and "ganchosAmbosIzq: lindero" not in zap_drw)
    check("y queda escrito el defecto que arregla",
          "dejaba una pata saliéndose del dado" in zap_drw)

    # ------------------------------------------------------------------
    # LAS COTAS: SUS VARIABLES ANTES DE CREAR EL ESTILO
    # ------------------------------------------------------------------
    # Aqui estaba el defecto de las cotas gigantes: un estilo creado sin fijar antes las
    # variables se crea con las del dibujo -texto de 0.18 al lado de una zapata de un metro-.
    check("las variables de cota se fijan antes de crear el estilo",
          'Dimvar("DIMTXT", 0.025)' in zap_pla
          and 'Dimvar("DIMASZ", 0.025)' in zap_pla
          and 'Dimvar("DIMEXO", 0.02)' in zap_pla
          and "estilo.CopyFrom(_doc);" in zap_pla)
    check("y queda escrito el defecto que arregla",
          "las cotas gigantes" in zap_pla)
    check("las cotas van en metros con dos decimales",
          'Dimvar("DIMLUNIT", 2)' in zap_pla and 'Dimvar("DIMDEC", 2)' in zap_pla)
    check("y con marcas abiertas, con DIMSAH antes de DIMBLK",
          zap_pla.index('Dimvar("DIMSAH", 0)') < zap_pla.index('Dimvar("DIMBLK", "_OPEN90")'))

    # ------------------------------------------------------------------
    # LA VISTA EN PLANTA, EN SU PROPIO BLOQUE
    # ------------------------------------------------------------------
    # Lo que se pidio: bloque con el dado, las varillas y el contorno; cotas y rotulos FUERA.
    # AQUI ESTABA EL DESFASE DE TODO EL DIBUJO, y es lo que hacia que las cotas se vieran
    # despegadas de la cimentacion: la seccion y la planta se insertaban con la rutina que
    # RECOLOCA el bloque por el centro de su caja -la del dado, que viene de otro dibujo-, y eso
    # arrastraba la geometria 88 cm hacia abajo y 50 cm a la izquierda. Las cotas y los rotulos,
    # que van fuera del bloque, se quedaban en su sitio.
    check("los bloques PROPIOS se insertan en su sitio, sin recolocar",
          "private bool InsertarBloquePropio(" in zap_pla
          and "InsertarBloquePropio(nombreBloque, xBase, yZapBot, CapaBloqueZapata)" in zap_drw
          and "InsertarBloquePropio(nombrePlanta, xIzq, yBot, CapaBloqueZapata)" in zap_pla)
    check("y la seccion ya no pasa por la rutina del centroide",
          "InsertarBloque(nombreBloque" not in zap_drw
          and "InsertarBloque(nombrePlanta" not in zap_pla)
    check("el recolocado por centroide queda SOLO para el bloque del dado",
          "InsertarBloque(id, (dx1 + dx2) / 2, yCen, CapaBloqueDado" in zap_pla
          and "Solo para el bloque del DADO" in zap_pla)
    check("y queda escrita la cuenta del desfase que producia",
          "AQUÍ ESTABA EL DESFASE DE TODO EL DIBUJO" in zap_pla
          and "bajaba 88 cm" in zap_pla)

    check("la planta se mete en su propio bloque",
          '"-PLANTA"' in zap_pla
          and "var plantaEnBloque = false;" in zap_pla
          and "InsertarBloquePropio(nombrePlanta, xIzq, yBot, CapaBloqueZapata)" in zap_pla)
    check("y los rotulos y las cotas quedan FUERA del bloque",
          "Se cierra el bloque: lo que sigue -cotas y rótulos- va en el MODELO." in zap_pla)
    check("y queda escrito por que una cota no puede ir dentro",
          "explotarlo es perder el bloque" in zap_pla)

    # El orden importa: dentro del bloque solo el dibujo, y el cierre ANTES de las cotas.
    m_pla = re.search(r"private void Planta\(ZapataCad z.*?\n    \}", zap_pla, re.S)

    check("se puede leer la planta completa", m_pla is not None)

    if m_pla:
        cuerpo = m_pla.group(0)
        i_malla = cuerpo.index("// ---------- Las mallas ----------")
        i_cierre = cuerpo.index("// Se cierra el bloque")
        i_cotas = cuerpo.index("// ---------- Cotas: LAS DE LA MACRO, en su sitio ----------")
        i_rot = cuerpo.index("// ---------- Rótulos de las mallas ----------")

        check("las mallas van DENTRO del bloque", i_malla < i_cierre)
        check("las cotas, FUERA", i_cotas > i_cierre)
        check("y los rotulos de parrilla, FUERA", i_rot > i_cierre)

    # ------------------------------------------------------------------
    # GUARDAR EL TRABAJO: TODAS LAS HOJAS
    # ------------------------------------------------------------------
    # Lo que se pidio: «cuando guardo trabajo solo se guardan mis secciones de concreto».
    # El .clk guardaba una lista escrita a mano, y cuando llegaron el acero y las zapatas
    # nadie volvio a tocarla: esas dos hojas se perdian al guardar.
    proy = leer(ruta("client/src/CadLink.App/Models/Proyecto.cs"))

    check("el trabajo guarda tambien el acero y las zapatas",
          "public List<FilaGuardada> Acero { get; set; }" in proy
          and "public List<FilaGuardada> Zapatas { get; set; }" in proy)
    check("y se guardan leyendo la fila, para que una columna nueva se guarde sola",
          "public static class FilaSerializable" in proy
          and "public static FilaGuardada Leer(object fila)" in proy
          and "public static void Aplicar(object fila, FilaGuardada guardada)" in proy)
    check("solo lo capturado: nada de columnas calculadas",
          "p.CanRead && p.CanWrite" in proy)
    check("los numeros van en formato invariante",
          "CultureInfo.InvariantCulture" in proy)
    check("un archivo viejo se sigue abriendo",
          "se ignora en silencio" in proy)
    check("las dos hojas se recogen al guardar",
          "FilaSerializable.Leer(a)" in codigo and "FilaSerializable.Leer(z)" in codigo)
    check("y se vuelcan al abrir",
          "_datos.SeccionesAcero.Clear();" in codigo
          and "_datos.ZapatasAisladas.Clear();" in codigo
          and "FilaSerializable.Aplicar(nueva, fila);" in codigo)
    check("y queda escrito por que se perdian",
          "<b>se perdían</b>" in proy and "se captura una vez" in proy)

    # El boton: revisa, se engancha a la sesion abierta y cuenta lo que salio.
    check("el boton de dibujar revisa ANTES de dibujar",
          "if (!RevisarZapatas(out var problemas, out _, out _))" in zap_cb
          and "Corrige esto antes de dibujar las zapatas" in zap_cb)
    check("la revision esta en un solo sitio para los dos botones",
          "private bool RevisarZapatas(" in zap_cb
          and "RevisarZapatas(out var problemas, out var acomodo, out var columnasRepetidas);"
          in zap_cb)
    check("no arranca AutoCAD, se engancha al que ya este abierto",
          "AcadConnection.Connect(launchIfMissing: false)" in zap_cb
          and "new ZapataDrawer(doc, catalogoDeVarillas)" in zap_cb)
    check("y el catalogo de varillas se PASA, no se copia",
          "private static double DiametroCmDeVarilla(string? clave)" in zap_cb
          and "private readonly Func<string?, double> _diametroCm;" in zap_drw
          and "DiametrosCm" not in zap_drw)
    check("los fallos tolerados y las notas se muestran",
          "dibujante.Fallos" in zap_cb
          and "MostrarNotas(" in zap_cb
          and "PERO hubo " in zap_cb)

    # ------------------------------------------------------------------
    # DESHACER (Ctrl+Z)
    # ------------------------------------------------------------------
    # Lo que importa de esto no es que exista el boton: es que NO haya manera de que un
    # cambio se quede fuera del historial. Por eso se guarda una instantanea del trabajo
    # entero en lugar de una lista de «que cambio»: con la lista habria que interceptar
    # cada sitio que toca los datos -las celdas de cinco cuadriculas, agregar y quitar
    # filas, el catalogo que trae las medidas solo- y el camino que alguien agregue mañana
    # y se olvide de registrar deja un cambio que no se puede deshacer.
    hist = leer(ruta("client/src/CadLink.App/Models/Historial.cs"))

    check("hay historial de deshacer",
          "public sealed class Historial" in hist
          and "public sealed class Instantanea" in hist)
    check("la instantanea guarda el trabajo en el formato del archivo",
          "JsonSerializer.Serialize(proyecto, Opciones)" in hist
          and "ProyectoGuardado" in hist)
    check("y se serializa al TOMARLA, no al deshacer",
          "queda una copia inmutable" in hist)
    check("las secciones de acero se clonan aparte, que el .clk no las guarda",
          "acero.Select(p => p.Copia()).ToList()" in hist
          and "todavía no las guarda" in hist)
    check("el historial tiene tope",
          "public const int MaximoPasos = 30;" in hist
          and "_pasos.RemoveFirst();" in hist)
    check("y no apila un paso que no cambia nada",
          "paso.EsIgualA(_pasos.Last?.Value)" in hist)
    check("se puede olvidar el historial, y se dice por que",
          "public void Limpiar()" in hist
          and "sin avisar" in hist)

    perfil_row = leer(ruta("client/src/CadLink.App/Models/PerfilAceroRow.cs"))

    # La fila de acero sabe copiarse, y el ORDEN de las asignaciones importa: primero el
    # perfil -que trae las medidas del catalogo solo- y las medidas despues, o una medida
    # ajustada a mano se perderia al deshacer.
    check("la fila de acero sabe copiarse y compararse",
          "public PerfilAceroRow Copia()" in perfil_row
          and "public bool EsIgualA(PerfilAceroRow? o)" in perfil_row)
    check("y copia el perfil ANTES de las medidas",
          perfil_row.index("Perfil = _perfil") < perfil_row.index("c.PeralteCm = _peralteCm;"))
    check("y se dice por que ese orden no es negociable",
          "El orden de las asignaciones importa y no es negociable" in perfil_row)

    # El enganche: un solo sitio registra, y registra el estado de ANTES.
    check("el historial se registra donde se avisa de un cambio",
          "RegistrarEnHistorial();" in codigo
          and "private void RegistrarEnHistorial()" in codigo)
    check("se apila el estado de ANTES, no el nuevo",
          "_historial.Apilar(_estadoActual);" in codigo
          and "_estadoActual = TomarInstantanea();" in codigo)
    check("el propio deshacer no se apila",
          "if (!_listo || _deshaciendo)" in codigo)
    check("y despues de deshacer, el estado actual es el que se acaba de poner",
          "_estadoActual = paso;" in codigo)

    # Ctrl+Z DENTRO de una celda es el deshacer del cuadro de texto, y ese gana.
    check("Ctrl+Z esta atado a la ventana",
          '<KeyBinding Key="Z" Modifiers="Control" Command="ApplicationCommands.Undo" />'
          in xaml
          and 'Command="ApplicationCommands.Undo"   Executed="OnDeshacer"' in xaml)
    check("y hay boton en la barra y renglon en el menu",
          'x:Name="DeshacerButton"' in xaml
          and 'Header="_Deshacer" Command="ApplicationCommands.Undo"' in xaml)
    check("dentro de una celda gana el deshacer del cuadro de texto",
          "Keyboard.FocusedElement is System.Windows.Controls.TextBox caja && caja.CanUndo"
          in codigo
          and "caja.Undo();" in codigo)

    # Abrir otro trabajo, empezar de cero o cargar el ejemplo BORRAN el historial: deshacer
    # ahi devolveria a otro trabajo, no al cambio anterior.
    check("abrir, nuevo, limpiar y el ejemplo olvidan el historial",
          codigo.count("OlvidarHistorial();") == 4
          and "private void OlvidarHistorial()" in codigo,
          f"{codigo.count('OlvidarHistorial();')} llamadas, se esperan 4")

    # ------------------------------------------------------------------
    # Las notas del ultimo dibujo, FUERA de la vista previa
    # ------------------------------------------------------------------
    # Estaban en una capa semitransparente pegada al borde de abajo de la vista previa, y
    # ahi tapaban justo el rotulo de la seccion y la cota de la base. Con cuatro notas -y
    # el interop de AutoCAD deja cuatro cada vez- se comian un tercio del cuadro.
    check("el cuadro de notas se oculta cuando no hay nada que decir",
          'x:Name="NotasPanel"' in xaml
          and 'Binding="{Binding Text, ElementName=ExportHintText}"' in xaml)

    check("las notas ya no van encima de la vista previa",
          'Grid.Row="4"' in xaml
          and '<Expander x:Name="NotasPanel" IsExpanded="False"' in xaml)
    check("y arrancan plegadas",
          'IsExpanded="False"' in xaml)

    # Y se pliegan en CADA dibujo, no solo al arrancar: si el usuario lo dejo abierto, el
    # dibujo siguiente no tiene por que heredar el panel abierto tapando media pestaña.
    check("hay un solo sitio que escribe las notas",
          "private void MostrarNotas(string texto)" in codigo
          and "NotasPanel.IsExpanded = false;" in codigo)
    # NUEVE: las siete de antes mas las dos de los cortes -lo que no se entendio del campo y los
    # cortes que no caen sobre ningun eje, que van rotulados con su sitio-.
    check("y los sitios que las escriben pasan por ahi",
          codigo.count("MostrarNotas(") == 9
          and codigo.count("ExportHintText.Text =") == 1,
          f"{codigo.count('MostrarNotas(')} llamadas, "
          f"{codigo.count('ExportHintText.Text =')} asignaciones directas")

    # ------------------------------------------------------------------
    # EL GANCHO DEL ESTRIBO EN LA VISTA PREVIA
    # ------------------------------------------------------------------
    # El usuario lo pidio dos veces: en la vista previa se veian dos rectangulos de
    # estribo perfectos y el gancho aparecia por primera vez en AutoCAD, que es justo el
    # detalle que se revisa antes de mandar el plano.
    #
    # Lo que importa de estas comprobaciones no es que dibuje algo, es que dibuje LO MISMO
    # que el dibujante: una vista previa con su propia geometria puede acabar enseñando un
    # gancho que no es el que se va a dibujar.
    check("la vista previa dibuja el gancho del estribo",
          "private void DibujarGanchoPrevio(" in codigo
          and "DibujarGanchoPrevio(s, de, rec, escala, PX, PY," in codigo)

    m_gp = re.search(r"private void DibujarGanchoPrevio\(.*?\n    \}", codigo, re.S)

    check("se puede leer DibujarGanchoPrevio", m_gp is not None)

    if m_gp:
        gp = m_gp.group(0)

        # El centro del doblez, con la MISMA cuenta del dibujante: rec + dEst + rIn de las
        # dos caras. Un signo de mas o de menos aqui pone el gancho fuera del estribo.
        check("el doblez se centra donde lo centra el dibujante",
              "var bx = s.BaseCm - rec - dEst - rIn;" in gp
              and "var by = s.AlturaCm - rec - dEst - rIn;" in gp)
        check("y se envuelve en la varilla de la esquina superior",
              "s.DiamEsqSup" in gp and "var rOut = rIn + dEst;" in gp)

        # Media vuelta, de 315 a 135 grados: son los 135 del gancho de norma.
        check("el doblez barre media vuelta, de 315 a 135 grados",
              "(1.75 * Math.PI) + (k / 24.0 * Math.PI)" in gp)

        # Las dos colas, cada una con sus TRES lineas, y a 225 grados.
        check("salen dos colas hacia el nucleo",
              "const double ux = -rt2I;" in gp and "const double uy = -rt2I;" in gp
              and "(Nx: rt2I, Ny: -rt2I" in gp and "(Nx: -rt2I, Ny: rt2I" in gp)
        check("y cada cola lleva sus tres lineas",
              "(piX, piY, qiX, qiY)" in gp
              and "(poX, poY, qoX, qoY)" in gp
              and "(qiX, qiY, qoX, qoY)" in gp)

        # El recorte de la segunda cola, con la condicion del dibujante.
        check("la segunda cola se recorta con la condicion del dibujante",
              "var tCruce = rOut - (Math.Sqrt(2) * rIn);" in gp
              and "tCruce >= 0 && tCruce <= largo" in gp)
        check("y arranca donde la cruza el estribo",
              "poX = bx + rIn - (Math.Sqrt(2) * rOut);" in gp)

        # Lo que NO se puede dibujar: sin gancho, sin estribo o sin varilla de esquina.
        check("no se dibuja gancho donde no hay de que doblarlo",
              "s.GanchoCm <= 0 || dEst <= 0 || rec <= 0" in gp
              and "!Varilla.TryDiametroCm(s.DiamEsqSup, out var dSup)" in gp)
        check("ni cuando el doblez no cabe en el nucleo",
              "bx <= rec + dEst || by <= rec + dEst" in gp)

    # ------------------------------------------------------------------
    # EL ESTRIBO DIAMANTE EN LA VISTA PREVIA
    # ------------------------------------------------------------------
    # Un diamante no es un rombo: es una cinta cerrada TANGENTE a una serie de circulos.
    # Calcularla por segunda vez en la vista previa es la manera de acabar enseñando un
    # rombo con otro vertice, otra varilla abrazada o esquinas en pico donde el dibujo
    # lleva dobleces redondeados. Asi que la geometria se saco a TrazoDiamante y la usan
    # los dos.
    trazo_dia = leer(ruta("client/src/CadLink.Cad/TrazoDiamante.cs"))
    diam = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Diamante.cs"))

    check("la geometria del diamante vive fuera del dibujante",
          "public static class TrazoDiamante" in trazo_dia
          and "public static List<(double X, double Y, double R)>? Centros(" in trazo_dia
          and "public static (double[] Pts, double[] Bulges)? Cinta(" in trazo_dia)
    check("y no sabe nada de AutoCAD",
          "_ms" not in trazo_dia and "AcadConnection" not in trazo_dia
          and "_log" not in trazo_dia)
    check("las notas se devuelven en lugar de escribirse en el registro",
          "List<string>? notas" in trazo_dia and "notas?.Add(" in trazo_dia)

    # El dibujante DELEGA: no le puede quedar una copia del calculo.
    check("el dibujante usa esa geometria en lugar de la suya",
          "TrazoDiamante.Centros(\n            x1, y1, x2, y2, dDia, _varSup, _varInf, "
          "_varLat, notas);" in diam
          and "TrazoDiamante.Cinta(centros, extra);" in diam)
    check("y no le queda ninguna copia del calculo",
          "private List<(double X, double Y, double R)> RodearLaterales(" not in diam
          and "private List<(double X, double Y, double R)> DoblezLateral(" not in diam
          and "var geo = GeometriaCinta(centros, extra);" in diam)
    check("y las notas del recorrido llegan al registro",
          "foreach (var n in notas)" in diam and "Nota(n);" in diam)

    # La vista previa lo dibuja, con las DOS cintas y su gancho.
    check("la vista previa dibuja el estribo diamante",
          "private void DibujarDiamantePrevio(" in codigo
          and "DibujarDiamantePrevio(s, de, rec, escala, PX, PY," in codigo)

    m_dp = re.search(r"private void DibujarDiamantePrevio\(.*?\n    \}", codigo, re.S)

    check("se puede leer DibujarDiamantePrevio", m_dp is not None)

    if m_dp:
        dp = m_dp.group(0)

        check("la vista previa pide el recorrido a la cuenta compartida",
              "CentrosDelDiamante(s, de, rec, dDia)" in dp
              and "TrazoDiamante.Centros(" not in dp)
        check("y no calcula ningun vertice del rombo por su cuenta",
              "Math.Atan2" not in dp and "tangente" not in dp.lower())
        check("dibuja las DOS cintas, no una linea",
              "foreach (var extra in new[] { 0.0, dDia })" in dp
              and "TrazoDiamante.Cinta(centros, extra)" in dp)
        check("los arcos se muestrean, que un lienzo no tiene bulges",
              "TrazoDiamante.Muestrear(geo.Value.Pts, geo.Value.Bulges, 10)" in dp)
        check("y la cinta se cierra, que es un estribo cerrado",
              "linea.Add(new Point(px(puntos[0].X), py(puntos[0].Y)));" in dp)
        check("el diametro del diamante cae al del estribo si no trae el suyo",
              "dDia = de;" in dp)
        check("y no se dibuja donde no hay diamante",
              "!s.LlevaDiamante || s.EsCircular" in dp)

    check("si lleva diamante lo dice el modelo, no la vista previa",
          "public bool LlevaDiamante =>" in filas)

    # EL 2D Y EL 3D PREGUNTAN LO MISMO. Estuvieron separados y en silencio: el 2D pasaba los
    # lechos COMPLETOS -esquina mas intermedias- y el 3D solo las de esquina. En una seccion con
    # varilla intermedia, el diamante salia en el corte y NO salia en el 3D, porque la varilla
    # intermedia es justo la que el rombo abraza. No era que el 3D no lo dibujara: le estaban
    # preguntando por un armado que no era el de la pieza.
    # La cuenta compartida y el recorrido del 3D viven en la parcial de la seccion 3D.
    s3d = leer(ruta("client/src/CadLink.App/MainWindow.Seccion3D.cs"))

    check("los centros del diamante salen de UNA sola cuenta",
          "private List<(double X, double Y, double R)>? CentrosDelDiamante(" in s3d)

    m_cd = re.search(
        r"private List<\(double X, double Y, double R\)>\? CentrosDelDiamante\(.*?\n    \}",
        s3d, re.S)

    check("se puede leer CentrosDelDiamante", m_cd is not None)

    if m_cd:
        cd = m_cd.group(0)

        # Las CUATRO llamadas: los dos lechos, cada uno con su esquina y su intermedia.
        check("y pasa los lechos COMPLETOS, con las varillas intermedias",
              "s.NIntSup" in cd and "s.NIntInf" in cd
              and "s.NEsqSup" in cd and "s.NEsqInf" in cd
              and cd.count("intermedio: true") == 2
              and cd.count("intermedio: false") == 2)

        check("y las laterales, que son las que el rombo rodea",
              "PosicionesLaterales(s, de, rec)" in cd)

        check("y le pasa las notas a TrazoDiamante, que antes se tiraban",
              "varSup, varInf, varLat, notas)" in cd)

    # Y la vista 3D tiene que usarla, no una copia suya.
    m_r3 = re.search(
        r"private List<\(double X, double Y\)>\? RecorridoDelDiamante3D\(.*?\n    \}",
        s3d, re.S)

    check("se puede leer RecorridoDelDiamante3D", m_r3 is not None)

    if m_r3:
        r3 = m_r3.group(0)

        check("la vista 3D usa la misma cuenta que el corte",
              "CentrosDelDiamante(s, de, rec, dDia, notas)" in r3
              and "PosicionesDeLecho(" not in r3)

        check("y dice POR QUE no hay diamante en lugar de callarse",
              r3.count("notas?.Add(") >= 3)

    # Las posiciones de las varillas se calculan UNA vez: las usan el pintado y el
    # recorrido del diamante. Con dos copias, el rombo podria rodear una varilla que no
    # es la que se ve dibujada.
    check("las posiciones de las varillas salen de un solo sitio",
          "private static List<(double X, double Y, double R)> PosicionesDeLecho(" in codigo
          and "private static List<(double X, double Y, double R)> PosicionesLaterales("
          in codigo)
    check("y el pintado de los lechos usa esas mismas posiciones",
          "foreach (var (x, y, r) in PosicionesDeLecho(" in codigo
          and "foreach (var (x, y, r) in PosicionesLaterales(s, de, rec))" in codigo)

    # Y el gancho del diamante, en el costado izquierdo.
    check("la vista previa dibuja el gancho del diamante",
          "private void DibujarGanchoDiamantePrevio(" in codigo)

    m_gdp = re.search(
        r"private void DibujarGanchoDiamantePrevio\(.*?\n    \}", codigo, re.S)

    check("se puede leer DibujarGanchoDiamantePrevio", m_gdp is not None)

    if m_gdp:
        gdp = m_gdp.group(0)

        check("el gancho del diamante va en el costado izquierdo, como en el dibujo",
              "centros.Where(v => v.X < cx)" in gdp)
        check("y se agarra de la varilla mas centrada de ese costado",
              "Math.Abs(v.Y - cy)" in gdp)
        check("con sus dos colas de tres lineas",
              "new[] { (n1X, n1Y), (n2X, n2Y) }" in gdp
              and "(pInX, pInY, qInX, qInY)" in gdp
              and "(qInX, qInY, qOutX, qOutY)" in gdp)
        check("y el tope del nucleo las recorta",
              "var tope = ((cx - piX) * ux) + ((cy - piY) * uy);" in gdp)

    # ------------------------------------------------------------------
    # Y una prueba que se EJECUTA, no que se porta
    # ------------------------------------------------------------------
    # Todo lo demas de este repositorio comprueba la geometria portandola a Python. Eso
    # comprueba la GEOMETRIA, pero no lo que el codigo compilado hace: un port correcto
    # conviviendo con un C# equivocado da todo en verde. Y aqui paso: el muestreo de los
    # arcos calculaba mal el centro y los puntos se salian del doblez hasta 0.74 cm.
    prueba = leer(ruta("tools/prueba-trazo-diamante/Program.cs"))

    check("hay una prueba que se ejecuta contra el CadLink.Cad compilado",
          "using CadLink.Cad;" in prueba and "static int Main()" in prueba)
    check("comprueba que la cinta es TANGENTE a cada circulo",
          "la cinta interior es tangente a cada circulo" in prueba
          and "y la exterior, al circulo engrosado" in prueba)
    check("y que el muestreo cae sobre el arco del doblez",
          "cada punto del muestreo cae sobre el arco de su doblez" in prueba)
    check("y que el recorrido va antihorario, que es lo que evita el nudo",
          "el recorrido va en sentido antihorario" in prueba)
    check("y devuelve 1 si algo falla, para poder usarla en un script",
          "return 1;" in prueba)
    check("y se explica por que no es un port de Python",
          "no es un\n// port" in prueba or "no es un port" in prueba.replace("\n// ", " "))

    # La hermana: los lectores de celda de la zapata. Los usan LOS DOS -la vista previa y
    # el dibujante de AutoCAD-, asi que un fallo ahi saca un plano distinto de lo revisado.
    prueba_zap = leer(ruta("tools/prueba-zapata/Program.cs"))

    check("hay una prueba ejecutable de los lectores de celda de la zapata",
          "using CadLink.Cad;" in prueba_zap
          and "TrazoZapata.SeparacionM(" in prueba_zap
          and "TrazoZapata.TramosCm(" in prueba_zap)
    check("comprueba lo que la gente escribe de verdad en una celda",
          '"20 cm"' in prueba_zap and '"@15"' in prueba_zap and '"12,5"' in prueba_zap)
    check("y que una celda vacia o en cero NO se lee como separacion cero",
          '"0"' in prueba_zap and "no cero" in prueba_zap)
    check("y que las siete separaciones de la lista corta reparten estribos",
          '"6-12-6", "7-14-7", "8-16-8", "9-18-9", "10-20-10", "15", "20"' in prueba_zap
          and "en orden y dentro" in prueba_zap)
    check("y que el acomodo es el nuevo, para los dos tipos",
          "la segunda a 80 cm a la izquierda de la primera" in prueba_zap
          and "justo la separacion de 80 cm" in prueba_zap)
    check("y devuelve 1 si algo falla, igual que la del diamante",
          "return fallos == 0 ? 0 : 1;" in prueba_zap)

    # ------------------------------------------------------------------
    # LOS DOS ERRORES DE COMPILACION QUE YA HAN SALIDO DOS VECES
    # ------------------------------------------------------------------
    # La aplicacion es WPF y solo compila en Windows, asi que aqui no hay compilador que
    # cace un using que falta. Se comprueba sin compilador, con una tabla de tipos.
    usings = leer(ruta("tools/verificar_usings.py"))

    check("hay un verificador de usings y de llamadas dinamicas",
          "CS0103" in usings and "CS1976" in usings
          and '"System.Windows.Input": [' in usings
          and '"Cursors"' in usings)
    check("y explica los dos errores que ya salieron, con su caso",
          "MainWindow.Zapatas.cs" in usings
          and "new ZapataDrawer(doc, DiametroCmDeVarilla)" in usings)
    # Y el CS1061: un miembro que no existe pero se parece a uno que si. Es el error mas
    # facil de cometer aqui, porque los nombres de los modelos son largos y parecidos:
    # 'DiamEsqSupEfectivo' por 'DiamEsqSup' tumbo la compilacion del usuario.
    check("el verificador caza un miembro que no existe pero se parece",
          "CS1061" in usings
          and "def revisar_miembros_que_no_existen(" in usings
          and "_prefijo_comun(d, nombre) >= 8" in usings)
    check("y no confunde un espacio de nombres con un miembro",
          "ahi los puntos separan ESPACIOS DE NOMBRES" in usings)
    check("ni un miembro de enum",
          "es un miembro de enum, no un error" in usings)
    check("el lecho superior no tiene «efectivo», porque no hereda de nadie",
          "DiamEsqSupEfectivo" not in zap_cb.replace("«DiamEsqSupEfectivo»", "")
          and "fila.VarDadoSup = dado.DiamEsqSup;" in zap_cb)

    check("el archivo de zapatas ya importa System.Windows.Input",
          "using System.Windows.Input;" in zap_cb)
    check("y el catalogo se pasa en una variable con su tipo, no como nombre suelto",
          "Func<string?, double> catalogoDeVarillas = DiametroCmDeVarilla;" in zap_cb
          and "new ZapataDrawer(doc, catalogoDeVarillas)" in zap_cb)

    # El hatch NO puede ser asociativo: el relleno del terreno borra su frontera.
    check("el hatch de la zapata no es asociativo, que borraria su frontera",
          "_cont.AddHatch(0, patron, false)" in zap_pla
          and "no asociativo" in zap_pla)

    # Y el muestreo, que fue el que fallo: el radio con SIGNO en lugar de un apaño.
    check("el muestreo saca el centro del arco con el radio con signo",
          "var radio = cuerda / (2 * Math.Sin(barrido / 2));" in trazo_dia
          and "var d = radio * Math.Cos(barrido / 2);" in trazo_dia)
    check("y se explica el error clasico que evita",
          "salen volteados" in trazo_dia)

    # Y el mismo gancho en la seccion REDONDA, que no lo tenia tampoco.
    check("la vista previa de la redonda dibuja el gancho del zuncho",
          "private void DibujarGanchoZunchoPrevio(" in codigo
          and "DibujarGanchoZunchoPrevio(\n            s, cx, cy, r, rec, dZun, dVar, "
              "rPaso, escala," in codigo)

    m_gz = re.search(r"private void DibujarGanchoZunchoPrevio\(.*?\n    \}", codigo, re.S)

    check("se puede leer DibujarGanchoZunchoPrevio", m_gz is not None)

    if m_gz:
        gz = m_gz.group(0)

        # LA CUENTA QUE NO PUEDE ESTAR EN COORDENADAS DE PANTALLA. El lienzo tiene la Y al
        # reves, y ahi «girar el radio 45 grados» gira para el otro lado: el gancho saldria
        # espejeado, apuntando al lado contrario que en AutoCAD. Por eso se calcula con la
        # Y hacia arriba y se voltea solo al pintar.
        check("el gancho del zuncho se calcula con la Y hacia arriba",
              "double PX(double x) => cx + x;" in gz
              and "double PY(double y) => cy - y;" in gz)

        # La cola es el radio hacia dentro girado 45 grados, la misma formula del dibujante.
        check("la cola es el radio interior girado 45 grados",
              "var ux = (rx - ry) * rt2I;" in gz
              and "var uy = (rx + ry) * rt2I;" in gz)
        check("y las normales son sus perpendiculares",
              "var n1X = -uy;" in gz and "var n1Y = ux;" in gz)

        # De la varilla de ABAJO, para no pisarse con la llamada, que apunta a la de arriba.
        check("se agarra de la varilla de abajo",
              "primera || y < by" in gz)

        # Del doblez, SOLO el arco exterior: el interior es la circunferencia de la varilla.
        check("del doblez se dibuja solo el arco exterior",
              "var aTangente = Math.Atan2(-ry, -rx);" in gz
              and "rOut * Math.Cos(a)" in gz)
        check("y el tope del nucleo recorta la cola",
              "var tope = (-piX * ux) + (-piY * uy);" in gz
              and "tope > 0 && largo > tope" in gz)
        check("y van las dos colas, tambien en helice",
              "new[] { (n1X, n1Y), (n2X, n2Y) }" in gz)

    # ------------------------------------------------------------------
    # El gancho sismico del DIAMANTE
    # ------------------------------------------------------------------
    # Un diamante es un estribo cerrado, asi que sus dos extremos se juntan y ahi van sus
    # ganchos. En el vertice IZQUIERDO, que es donde el rectangular NO tiene el suyo —el
    # suyo esta arriba a la derecha— para que los dos no se monten.
    diam = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Diamante.cs"))

    check("el diamante lleva gancho sismico",
          "private void GanchoDelDiamante(" in diam)
    check("y se dibuja de verdad",
          "GanchoDelDiamante(s, contorno, centros, cx, cy, dDia, conFondoSolido);"
          in diam)

    m_gd = re.search(r"private void GanchoDelDiamante\(.*?\n    \}", diam, re.S)
    check("se puede leer GanchoDelDiamante", m_gd is not None)
    if m_gd:
        cuerpo = m_gd.group(0)

        # Va en el costado IZQUIERDO.
        check("el gancho del diamante va en el costado izquierdo",
              "_varLat.Where(v => v.X < cx)" in cuerpo)

        # Y se engancha a la varilla que el diamante YA abraza ahi, con la misma regla que
        # usa el vertice: si no, el gancho doblaria en un sitio y la cinta en otro.
        check("usa la misma regla del vertice para elegir la varilla",
              "VarillasDelCentro(delLado, cy, porY: true)" in cuerpo)

        # Sin varilla no se dibuja: un gancho sismico rodea una varilla, no dobla en el aire.
        check("sin varilla en ese costado no se dibuja gancho",
              "no hay varillas " in cuerpo)

        # El doblez envuelve la VARILLA.
        check("el doblez envuelve la varilla",
              "var rIn = barra.R;" in cuerpo and "var rOut = rIn + dDia;" in cuerpo)

        # La cola apunta AL NUCLEO: el radio sin girar, que es la regla del estribo
        # rectangular. Girarlo 45 grados es del zuncho circular -alli el acero llega
        # en tangente- y aqui dejaba la cola encima de la propia diagonal del rombo.
        check("la cola del diamante es el radio hacia el nucleo",
              "var ux = cx - barra.X;" in cuerpo and "var uy = cy - barra.Y;" in cuerpo)
        check("y NO se gira 45 grados, que es lo del zuncho circular",
              "Rt2I" not in cuerpo)
        check("y las normales son las perpendiculares a la cola",
              "var n1X = -uy;" in cuerpo)

        # REUTILIZA lo que ya existe: es el tercer sitio que usa esta geometria.
        check("reutiliza la Cola del estribo rectangular",
              "Cola(contorno, quads, barra.X, barra.Y, rIn, rOut" in cuerpo)
        check("y el relleno del gancho del zuncho circular",
              "RellenoDelGancho(quads, sectores)" in cuerpo)

        # NINGUN arco del doblez se dibuja, ni el interior ni el exterior:
        #
        #   * el interior tiene el centro y el radio de la varilla, o sea que ES su
        #     circunferencia, ya trazada;
        #   * y el exterior, entre los dos puntos donde la cinta toca la varilla, ES el
        #     borde exterior de la cinta, tambien ya trazado; y fuera de esos dos puntos
        #     se mete DENTRO del acero de la cinta, o sea que pintaba una raya negra por
        #     dentro del relleno. Medido: entre 0.84 y 3.63 cm de raya, segun la seccion.
        #
        # El gancho del estribo rectangular hace lo mismo desde el principio: tampoco
        # traza el arco de su doblez.
        check("no dibuja el arco interior, que es la varilla misma",
              "Arco(barra.X, barra.Y, rIn," not in cuerpo)
        check("ni el exterior, que es el borde de la cinta",
              "Arco(barra.X, barra.Y, rOut" not in cuerpo)

        # Del exterior si se dibuja UN pedazo, y solo ARRIBA de la varilla: el trozo del
        # doblez que asoma del abrazo de la cinta. Arriba el acero que hay es el doblez del
        # extremo que llega por la diagonal de abajo -la envuelve y sale como la cola de
        # arriba-, asi que ese contorno es CURVO, no una recta. Abajo no, porque ahi el
        # doblez es el del otro extremo, que pasa por debajo de la diagonal.
        check("arriba de la varilla se dibuja el arco del doblez",
              "ArcoDelDoblez(" in cuerpo)
        check("y va del arranque de la cola a la tangencia de la cinta",
              "a1, Math.Atan2(tang.Y - barra.Y, tang.X - barra.X));" in cuerpo)
        check("la tangencia es el extremo del tramo que esta sobre la varilla",
              "var tang = d1 <= d2 ? ext1 : ext2;" in cuerpo)

        # PERO LAS COLAS VAN ENTERAS, con sus tres lineas cada una.
        #
        # Hubo una version que les quitaba la linea interior -la que nace pegada a la
        # varilla-, con el argumento de que el doblez pasa por encima de la varilla. El
        # usuario lo rechazo: eran DOS lineas que le faltaban al gancho, una por cola.
        check("las colas del diamante van con sus tres lineas",
              "sinLineaInterior" not in cuerpo)
        check("y el doblez se rellena",
              "sectores.Add(new[] { barra.X, barra.Y, rIn, rOut, a1, a1 + Pi });"
              in cuerpo)

        # Las dos colas NO se tratan igual: la de arriba ENTERA, porque justo en su arranque
        # acaba el arco del doblez y las dos se empalman tangentes; la de abajo RECORTADA
        # donde sale del acero, porque por ese lado el gancho pasa por debajo.
        #
        # Y la de arriba tampoco se alarga hacia atras: se probo y estaba mal por dos cosas,
        # que el contorno de ahi es CURVO y que alargarla alargaba su relleno -Cola infla el
        # cuadrilatero el espesor del estribo cuando le pasan otro arranque-, asi que el
        # relleno se salia del diamante 1.87 cm. Era el «hatch que sale».
        check("la cola de abajo se recorta donde sale del acero",
              "SalidaDelAceroDelDiamante(" in cuerpo)
        check("y la de arriba no, que ahi acaba el arco",
              "if (iBarra >= 0 && !arriba && geoInt is not null)" in cuerpo)
        check("no queda rastro del intento de alargar la cola",
              "AlcanceConLaCinta" not in diam)
        check("se distinguen por el lado, no por el orden",
              "new[] { (n1X, n1Y, true), (n2X, n2Y, false) }" in cuerpo)
        check("y las dos se le pasan a la misma Cola del rectangular",
              "arranque is not null, arranque?.X ?? 0, arranque?.Y ?? 0" in cuerpo)

        # Y LA LINEA DE LA CINTA QUE PASA ARRIBA DE LA VARILLA SE CORTA CON EL GANCHO.
        #
        # Es lo que se pidio: la linea interior de la cinta se abre un hueco del ancho del
        # brazo por donde el gancho le pasa por encima, para que la diagonal no parezca
        # cortar el gancho. Solo la de ARRIBA -n1X, n1Y-, que es el gancho que va encima; la
        # de abajo pasa por debajo y por eso de ese lado lo que se recorta es la cola.
        check("la linea de la cinta se corta bajo el brazo de arriba",
              "AbrirCintaBajoLaCola(\n            centros, iBarra, n1X, n1Y," in cuerpo)
        check("y la cinta vieja se sustituye por la abierta, no se deja las dos",
              "Borrar(_diamInt);" in cuerpo and "_diamInt = cintaAbierta;" in cuerpo)
        check("y se dice que el hueco es del diamante, no del gancho",
              "no le quita ninguna línea al gancho" in cuerpo
              and "sus tres líneas cada una" in cuerpo)

        # Y la cola se recorta si no cabe en el nucleo.
        check("la cola del diamante se recorta si no cabe",
              "gancho = tope;" in cuerpo)

    # Y la apertura esta puesta con sus cuatro piezas: el metodo, los dos recortes y la
    # polilinea abierta. Se busca la DECLARACION, no el nombre, para no confundirla con la
    # mencion de un comentario.
    for pieza in ("AbrirCintaBajoLaCola", "RecorteDeLaCola", "RecorteDelDoblez",
                  "PolilineaAbierta"):
        check(f"esta el metodo {pieza}",
              re.search(r"private [\w<>?,\.\(\) ]*\b" + pieza + r"\(", diam) is not None)

    check("y el tope del hueco, para que una cuenta mala no borre media diagonal",
          "FraccionMaxHuecoCinta = 0.5" in diam)

    check("se dice que el hueco NO le quita lineas al gancho",
          "Esto no le quita nada al gancho" in diam
          and "sus <b>tres líneas</b>" in diam)

    m_sal = re.search(
        r"private static \(double X, double Y\)\? SalidaDelAceroDelDiamante\(.*?\n    \}",
        diam, re.S)

    check("se puede leer SalidaDelAceroDelDiamante", m_sal is not None)
    if m_sal:
        sal = m_sal.group(0)

        # El borde con el que se recorta es el que DIBUJA la cinta, no una estimacion:
        # los mismos numeros, asi que el recorte cae sobre la linea trazada.
        check("el recorte usa el borde interior que dibuja la cinta",
              "GeometriaCinta(centros, 0)" in cuerpo)
        check("y no se recorta si el cruce cae fuera de la cola o del tramo",
              "t <= 1e-12 || t >= largo || sTramo < -1e-9 || sTramo > 1 + 1e-9" in sal)

    # Cada cola mira SU diagonal, y de eso se encarga una sola funcion: el recorte de una,
    # el alargue de la otra y el hueco de la cinta preguntan los tres por el mismo lado.
    m_tramo = re.search(
        r"TramoDeLaCinta\(\n?.*?\n    \{.*?\n    \}", diam, re.S)

    check("se puede leer TramoDeLaCinta", m_tramo is not None)
    if m_tramo:
        tra = m_tramo.group(0)

        check("el lado se decide comparando con la normal de la cola",
              "ladoLlega >= ladoSale" in tra)
        check("y devuelve el vertice, que es lo que hace falta para abrir la cinta",
              "(2 * previo) + 1" in tra and "(2 * iBarra) + 1" in tra)

    check("el recorte y el arco usan el mismo tramo",
          diam.count("TramoDeLaCinta(") >= 3)

    # La Cola es UNA para los dos ganchos, el del rectangular y el del diamante, y dibuja
    # sus TRES lineas siempre. El parametro para saltarse la interior se quito.
    dib = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("la Cola dibuja sus tres lineas, sin excepciones",
          "sinLineaInterior" not in dib
          and 'Agregar(contorno, Linea(piX, piY, qiX, qiY, "ESTRIBOS"));' in dib)
    check("el gancho del rectangular sigue igual",
          "Cola(contorno, quads, bx, by, rIn, rOut, Rt2I, -Rt2I, ux, uy, gancho, "
          "false, 0, 0);" in dib)

    gancho_py = leer(ruta("tools/verificar_gancho_diamante.py"))

    check("hay comprobacion numerica de la direccion de la cola del diamante",
          "Direccion de la cola del gancho del diamante" in gancho_py)
    check("y de que ninguna linea del gancho queda dentro del acero del diamante",
          "NINGUNA LINEA DEL GANCHO DEBE QUEDAR DENTRO DEL ACERO" in gancho_py)

    # Y esa comprobacion cuenta las TRES lineas de cada cola, que es lo que se recupero:
    # mientras porto la version que se saltaba la interior, daba OK a un gancho al que le
    # faltaban dos lineas.
    check("la comprobacion del gancho cuenta las tres lineas de cada cola",
          "cada cola va con sus tres lineas" in gancho_py
          and "es tangente a la varilla" in gancho_py)
    check("y porta el hueco de la cinta, con sus dos recortes",
          "def hueco_de_la_cinta(" in gancho_py
          and "def recorte_de_la_cola(" in gancho_py
          and "def recorte_del_doblez(" in gancho_py
          and "FRACCION_MAX_HUECO" in gancho_py)

    # Lo que de verdad prueba que el hueco esta bien: se contrasta contra un muestreo
    # independiente del acero del gancho. Si el hueco fuera un numero inventado, dejaria
    # fuera algo de lo que el gancho tapa, o abriria donde no tapa nada.
    check("el hueco se contrasta contra lo que el gancho tapa de verdad",
          "el hueco NO deja fuera nada de lo que el gancho tapa" in gancho_py
          and "y no abre nada que el gancho no tape" in gancho_py)
    check("y la cinta abierta conserva todos los vertices de la cerrada",
          "no se pierde ni un vertice de la cinta" in gancho_py)

    check("hay comprobacion numerica del gancho del zuncho",
          "Gancho sismico del zuncho" in leer(ruta("tools/verificar_seccion_circular.py")))

    # ------------------------------------------------------------------
    # Las varillas se recortan donde el zuncho pasa por DELANTE
    # ------------------------------------------------------------------
    # El zuncho cruza cada varilla dos veces por vuelta, pero solo la tapa cuando
    # pasa por delante. Recortar en todos los cruces la partiria en el doble de
    # trozos y dejaria huecos donde deberia verse entera.
    check("hay calculo de los pasos del zuncho por delante",
          "private static List<double> CrucesFrontales(" in alz)

    m_cf = re.search(r"private static List<double> CrucesFrontales\(.*?\n    \}", alz, re.S)
    check("se puede leer CrucesFrontales", m_cf is not None)
    if m_cf:
        cuerpo = m_cf.group(0)
        check("se filtra por la profundidad, no por el cruce a secas",
              "if (c > 0)" in cuerpo)
        check("una varilla mas afuera que el zuncho no se recorta",
              "Math.Abs(objetivo) > h.REje" in cuerpo)
        check("el cruce se interpola dentro del tramo",
              "d0 / (d0 - d1)" in cuerpo)

    m_vc2 = re.search(r"private void VarillasCirculares\(.*?\n    \}", alz, re.S)
    if m_vc2:
        cuerpo = m_vc2.group(0)
        check("con helice, los cortes salen de los pasos por delante",
              "CrucesFrontales(helice, ys[i])" in cuerpo)
        check("y con anillos se siguen usando sus centros",
              "helice is null" in cuerpo and "? centros" in cuerpo)

    # El helper del ancho, con su via de respaldo.
    m_ap = re.search(r"private bool AnchoDePolilinea\(.*?\n    \}", alz, re.S)
    check("se puede leer AnchoDePolilinea", m_ap is not None)
    if m_ap:
        check("se intenta ConstantWidth", "ConstantWidth = ancho" in m_ap.group(0))
        check("y hay respaldo vertice por vertice", "SetWidth(" in m_ap.group(0))

    # El color del zuncho relleno y el tope de puntos ya se comprueban arriba, en
    # HeliceMaciza y en MuestrearHelice, que es donde viven desde que la helice se
    # partio en tres metodos.

    # Una columna redonda no tiene segunda cara: se veria igual.
    check("la columna redonda no lleva dos alzados", "&& !a.Circular" in alz)

    # Las varillas del circulo se PROYECTAN, y las parejas simetricas coinciden.
    check("las varillas del circulo se proyectan al alzado",
          "private void VarillasCirculares(" in alz)
    m_vc = re.search(r"private void VarillasCirculares\(.*?\n    \}", alz, re.S)
    if m_vc:
        check("y se quitan las que caen una sobre otra",
              "dVar * 0.1" in m_vc.group(0))

    # ------------------------------------------------------------------
    # La vista previa no puede mentir
    # ------------------------------------------------------------------
    check("la vista previa dibuja la seccion redonda",
          "DibujarVistaPreviaCircular" in codigo)
    m_vp = re.search(
        r"private void DibujarVistaPreviaCircular\(.*?\n    \}", codigo, re.S)
    if m_vp:
        cuerpo = m_vp.group(0)
        # Las MISMAS formulas que el dibujo de AutoCAD, o la vista previa miente.
        check("usa el mismo radio de paso que AutoCAD",
              "r - rec - dZun - (dVar / 2)" in cuerpo)
        # En el lienzo la Y baja: el seno va con signo negativo o el reparto sale
        # girado al reves respecto a AutoCAD.
        check("compensa que la Y del lienzo baja",
              "cy - (rPaso * Math.Sin(a))" in cuerpo)

    # La capa de la varilla del circulo tiene que crearse.
    check("se crea la capa de la varilla del circulo",
          "Varilla.Normalizar(s.DiamVarTotalEfectivo)" in codigo)

    # ------------------------------------------------------------------
    # Revisiones propias de la forma
    # ------------------------------------------------------------------
    check("hay revisiones propias de la circular",
          "private static void RevisarCircular(" in codigo)
    check("y las rectangulares siguen aparte",
          "private static void RevisarRectangular(" in codigo)

    m_rc = re.search(
        r"private static void RevisarCircular\(.*?\n    \}", codigo, re.S)
    if m_rc:
        cuerpo = m_rc.group(0)
        check("se revisa que las varillas quepan en el perimetro",
              "Math.Sin(Math.PI / s.NVarTotal)" in cuerpo)
        check("y se avisa de los lechos que no se van a dibujar",
              "capturadas por lechos" in cuerpo)

    # ------------------------------------------------------------------
    # El .clk sigue abriendo trabajos viejos
    # ------------------------------------------------------------------
    proy = leer(ruta("client/src/CadLink.App/Models/Proyecto.cs"))
    for campo in ("Circular", "NVarTotal", "DiamVarTotal", "ZunchoHelicoidal"):
        check(f"el .clk guarda {campo}", f"public string {campo}" in proy
              or f"public int {campo}" in proy)

    # ------------------------------------------------------------------
    # Comprobaciones numericas
    # ------------------------------------------------------------------
    for f in ("verificar_seccion_circular.py", "verificar_diametros_varilla.py"):
        check(f"existe {f}", os.path.exists(ruta("tools/" + f)))

    # La tabla de diametros, en el nominal exacto. El #2 estaba en 0.60 y el
    # nominal es 0.635: el area salia un 12 % baja, y una cuantia baja es del
    # lado inseguro.
    check("los diametros estan en el nominal exacto",
          '["#2"] = 0.635' in filas and '["#10"] = 3.175' in filas)
    check("y ya no en el valor redondeado",
          '["#2"] = 0.60' not in filas and '["#10"] = 3.20' not in filas)
    check("hay formula para comprobar la tabla",
          "public static double NominalCm(" in filas)

    check("hay documento de cotejo con la macro",
          os.path.exists(ruta("docs/comparacion-macro-alzados.md")))

    # Y el documento explica las cotas del bloque de seccion, incluido lo que NO esta
    # bien de ellas: muestran metros, como el resto de las cotas del concreto. Un
    # documento que solo cuenta lo que salio bien no sirve para revisar el plano.
    cotejo_alz = leer(ruta("docs/comparacion-macro-alzados.md"))

    check("el cotejo explica las cotas del bloque de seccion",
          "El bloque de sección del alzado va acotado" in cotejo_alz
          and "CotasDelCorte(x, y, ancho, alto)" in cotejo_alz)
    check("y dice que van fuera del bloque, y por que",
          "excluye las capas COTAS y" in cotejo_alz)
    check("y que el aire sobre la seccion subio de 0.10 a 0.19",
          "de **0.10 a 0.19**" in cotejo_alz)
    check("y avisa de que muestran metros, como las del concreto",
          "**Muestran metros**" in cotejo_alz and "0.30" in cotejo_alz)

    # Y el del concreto explica el gancho del diamante, con las TRES cosas que se
    # probaron y se revirtieron: es lo que evita reintentarlas.
    doc_conc = leer(ruta("docs/macro-secciones-concreto.md"))

    check("el documento del concreto explica el gancho del diamante",
          "sus **tres líneas**" in doc_conc)
    check("y el corte de la linea del diamante con el ancho del brazo",
          "La línea del diamante se corta con el ancho del brazo" in doc_conc
          and "no le quita ninguna línea al gancho" in doc_conc)
    check("y las dos cosas que se probaron y se revirtieron",
          "Dos cosas que se probaron y se revirtieron" in doc_conc
          and "dos líneas que le faltaban al gancho" in doc_conc
          and "1.87 cm" in doc_conc)
    check("y que los ganchos ya se ven en la vista previa",
          "Y los ganchos se ven en la vista previa" in doc_conc
          and "el gancho sale\nespejeado" in doc_conc)
    check("y que el rombo del diamante tambien se ve",
          "Y el rombo también, con la geometría del dibujante" in doc_conc
          and "un diamante **no es un rombo**" in doc_conc)
    check("y explica la prueba que se ejecuta y lo que cazo",
          "Una prueba que se EJECUTA, y lo que cazó" in doc_conc
          and "hasta 0.74 cm" in doc_conc)

    # Y el de acero explica el catalogo de aceros y como se actualiza, que es lo que el
    # usuario pregunto: si tiene que volver a subir el Excel al repositorio.
    doc_acero = leer(ruta("docs/macros-acero.md"))

    check("el documento de acero explica el catalogo de aceros",
          "su Fy y si se consigue" in doc_acero
          and "Tres respuestas, no dos" in doc_acero)
    check("y la traduccion de las columnas de la hoja a las familias",
          "no es un capricho de la hoja" in doc_acero
          and "42 ksi en redondo y 46 en rectangular" in doc_acero)
    check("y contesta que NO hay que volver a subir el Excel",
          "No hace falta volver a subir nada al repositorio" in doc_acero
          and "python3 tools/catalogo_aceros.py docs/ACEROS.xlsx" in doc_acero)

    # ------------------------------------------------------------------
    # «Esa propiedad no existe» no es un fallo del dibujo
    # ------------------------------------------------------------------
    # Cuatro propiedades de cota no estan en todas las versiones de AutoCAD, y se
    # contaban como fallos. El resultado era el aviso «hubo 4 fallo(s), el dibujo
    # puede estar incompleto» en un dibujo perfecto. Un aviso que salta siempre y
    # que no se puede atender enseña al usuario a ignorar TODOS los avisos.
    drawer = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("se distingue una propiedad inexistente de un fallo real",
          "private static bool EsPropiedadInexistente(" in drawer)

    m_pi = re.search(r"private static bool EsPropiedadInexistente\(.*?\n    \}",
                     drawer, re.S)
    check("se puede leer EsPropiedadInexistente", m_pi is not None)
    if m_pi:
        cuerpo = m_pi.group(0)
        # Por HRESULT y no por el texto: el mensaje viene traducido al idioma de
        # AutoCAD, asi que buscar «Nombre desconocido» funcionaria en una
        # instalacion y no en la siguiente.
        check("se comprueba por HRESULT y no por el texto del mensaje",
              "0x80020006" in cuerpo)
        check("y se desenvuelve la excepcion de la reflexion",
              "TargetInvocationException" in cuerpo)

    m_pc = re.search(r"private void PropCota\(.*?\n    \}", drawer, re.S)
    check("se puede leer PropCota", m_pc is not None)
    if m_pc:
        cuerpo = m_pc.group(0)
        check("una propiedad que no existe va a Nota y no a Fallo",
              "EsPropiedadInexistente(ex)" in cuerpo and "Nota(" in cuerpo)

    # ------------------------------------------------------------------
    # Borrado de VARIOS planos
    # ------------------------------------------------------------------
    # SheetGridStyle pone SelectionUnit en CellOrRowHeader, y con eso la tecla Supr
    # actua sobre la CELDA: el DataGrid no borraba ninguna fila. Hay que fijarlo en
    # la propia cuadricula, que es donde gana al estilo.
    m_pg = re.search(r'<DataGrid x:Name="PlanosGrid".*?>', xaml, re.S)
    check("se puede leer la apertura del PlanosGrid", m_pg is not None)
    if m_pg:
        cuerpo = m_pg.group(0)
        check("los planos se seleccionan por fila entera",
              'SelectionUnit="FullRow"' in cuerpo)
        check("y se pueden marcar varios", 'SelectionMode="Extended"' in cuerpo)
        check("y se pueden borrar", 'CanUserDeleteRows="True"' in cuerpo)

    m_qp = re.search(r"private void OnQuitarPlano\(.*?\n    \}", codigo, re.S)
    check("se puede leer OnQuitarPlano", m_qp is not None)
    if m_qp:
        cuerpo = m_qp.group(0)
        check("Quitar borra TODOS los planos marcados",
              "SelectedItems" in cuerpo)
        # La lista se copia ANTES de borrar: recorrer SelectedItems mientras se
        # modifica salta una excepcion o deja filas sin quitar.
        check("y la lista se copia antes de empezar a borrar",
              ".ToList()" in cuerpo)

    # ------------------------------------------------------------------
    # La fecha, con el mes y el ano en letra
    # ------------------------------------------------------------------
    solapa = leer(ruta("client/src/CadLink.App/Models/Solapa.cs"))

    check("la solapa da la fecha con letra", "public string FechaTexto =>" in solapa)
    check("y tambien la larga, por si hace falta el dia",
          "public string FechaTextoLargo =>" in solapa)
    check("el mes va en letra", "MMMM" in solapa)

    # En es-MX y no en la cultura del equipo: el plano se entrega en español pase lo
    # que pase, y en un Windows en ingles saldria «August of 2026».
    check("la fecha del plano no depende del idioma del equipo",
          'GetCultureInfo("es-MX")' in solapa)
    # Se mira el codigo SIN comentarios: el propio archivo menciona CurrentCulture en
    # el comentario que explica por que no se usa, y buscar la palabra a pelo daba por
    # incumplida justo la regla que el comentario documenta.
    check("y no usa CurrentCulture para el rotulo",
          "CurrentCulture" not in _sin_comentarios(solapa))

    # El calendario se queda: sirve para elegir el dia.
    check("el calendario sigue estando", 'x:Name="FechaPicker"' in xaml)
    check("y al lado se ve lo que se va a imprimir",
          'x:Name="FechaTextoLabel"' in xaml)
    check("el texto de la fecha se refresca", "private void RefrescarFecha()" in codigo)

    # Se refresca en los TRES sitios que cambian la fecha, o se quedaria con el mes
    # anterior al abrir un trabajo o al empezar uno nuevo.
    check("se refresca al cambiarla, al abrir y al empezar de nuevo",
          codigo.count("RefrescarFecha();") >= 4,
          f"se llama {codigo.count('RefrescarFecha();')} vez/veces")

    # ------------------------------------------------------------------
    # La pestaña AutoCAD, fuera; y Licencia al final
    # ------------------------------------------------------------------
    check("ya no hay pestaña AutoCAD", '<TabItem Header="AutoCAD">' not in xaml)

    check("los avisos del dibujo siguen a mano", 'x:Name="ExportHintText"' in xaml)

    # ------------------------------------------------------------------
    # La escala de dibujo YA NO SE CAPTURA
    # ------------------------------------------------------------------
    # El dibujo sale siempre con la misma correspondencia, asi que la casilla solo
    # era una forma de descuadrarlo. Vive en una constante, en un solo sitio.
    check("la casilla de la escala de dibujo se retiro",
          'x:Name="ScaleBox"' not in xaml)
    check("la escala vive en una constante",
          re.search(r"private const double EscalaDeDibujo = 0\.01\s*;", codigo) is not None)

    # El valor NO puede ser 1.0 «porque es 1=1»: la correspondencia real es cm
    # capturados a metros dibujados, y con 1.0 una columna de 50 cm saldria de 50 m.
    m_le = re.search(r"private double LeerEscala\(\)[^\n]*", codigo)
    check("se puede leer LeerEscala", m_le is not None)
    if m_le:
        check("LeerEscala devuelve la constante",
              "EscalaDeDibujo" in m_le.group(0))

    # Y lo que SOLO servia al modo DXF, que no estaba implementado, se retiro.
    for muerto in ("OutputPathBox", "ModeComRadio", "ModeDxfRadio", "OnBrowseOutput"):
        check(f"{muerto} se retiro del XAML", muerto not in xaml)
        check(f"y del codigo ({muerto})", muerto not in codigo)

    # Licencia, al final de la tira.
    tabs = re.findall(r'^            <TabItem[^>]*Header="([^"]+)"', xaml, re.M)
    check("se pudieron leer las pestañas de primer nivel", len(tabs) > 5,
          f"{len(tabs)}")
    if tabs:
        check("Licencia es la ultima pestaña", tabs[-1] == "Licencia",
              f"la ultima es '{tabs[-1]}'")

    # ------------------------------------------------------------------
    # El leader del circulo sale por la IZQUIERDA del texto
    # ------------------------------------------------------------------
    m_tl = re.search(r"private void TextoLeader\(.*?\n    \}", drawer, re.S)
    check("se puede leer TextoLeader", m_tl is not None)
    if m_tl:
        cuerpo = m_tl.group(0)
        # 4 = MiddleLeft (el texto crece a la derecha, la linea sale por la
        # izquierda), 6 = MiddleRight, que es lo que quiere la llamada de lecho.
        check("el anclaje del texto de llamada se puede elegir",
              "haciaLaDerecha ? 4 : 6" in cuerpo)

    check("la llamada del circulo pide el anclaje a la izquierda",
          "haciaLaDerecha: true" in circ)

    # ------------------------------------------------------------------
    # El rotulo del alzado va FUERA del bloque, debajo del bloque insertado
    # ------------------------------------------------------------------
    # Antes se metia dentro de la definicion del bloque, y entonces se dibujaba en
    # coordenadas del bloque: caia pegado al pie de la geometria, POR ENCIMA de las
    # cotas que el espacio modelo pone despues, y en el alzado vertical el giro de 90
    # grados se lo llevaba por delante. Ahora va al espacio modelo, debajo del bloque
    # insertado y de sus cotas.
    check("el rotulo del alzado ya no se mete en el bloque",
          "private void RotuloDelBloque(" not in alz)

    # El rotulo cuelga del bloque de la SECCION, no del alzado. Hubo dos malentendidos
    # seguidos: primero iba DENTRO de la definicion del bloque de alzado, y despues
    # colgado del bloque de alzado, cuando "el bloque insertado" era el de la seccion,
    # el del CORTE A-A'. En el modulo de alzados se insertan DOS bloques.
    check("hay rotulo del elemento en el espacio modelo",
          "private void RotuloDelElemento(" in alz)
    check("y ya no cuelga del alzado",
          "RotuloDelAlzado(" not in alz)

    m_rb = re.search(r"private void RotuloDelElemento\(.*?\n    \}", alz, re.S)
    check("se puede leer RotuloDelElemento", m_rb is not None)
    if m_rb:
        cuerpo = m_rb.group(0)
        check("usa los renglones comunes", "LineasDelRotulo(a)" in cuerpo)

        # Centrado bajo el bloque de la seccion, colgando de su pano inferior.
        check("se centra bajo el bloque de la seccion",
              "xSeccion + (anchoSeccion / 2)" in cuerpo)
        check("y cuelga de su pano inferior",
              "yAbajo - (RotuloGap * _f)" in cuerpo)

    # UNO por elemento, aunque el elemento lleve DOS alzados: el rotulo describe el
    # elemento, no una de sus vistas. Colgado del alzado salian dos rotulos iguales.
    check("el rotulo se dibuja una sola vez por elemento",
          alz.count("RotuloDelElemento(a,") == 1,
          f"{alz.count('RotuloDelElemento(a,')} llamada(s)")

    m_de = re.search(r"public double DibujarElemento\(.*?\n    \}", alz, re.S)
    if m_de:
        cuerpo = m_de.group(0)
        check("y se dibuja en DibujarElemento, donde se conoce la seccion",
              "RotuloDelElemento(a, xSec, y, ancho);" in cuerpo)

        # Despues de insertar la seccion: hace falta su ancho medido para centrarlo.
        i_ins = cuerpo.find("InsertarSeccion(a.Id, xSec, y)")
        i_rot = cuerpo.find("RotuloDelElemento(")
        check("despues de insertar la seccion, que es de donde sale su ancho",
              0 <= i_ins < i_rot, f"insercion en {i_ins}, rotulo en {i_rot}")

    # Y los dos caminos de alzado ya NO rotulan.
    for nombre in ("DibujarHorizontal", "DibujarVertical"):
        m_ = re.search(rf"private double {nombre}\(.*?\n    \}}", alz, re.S)
        if m_:
            check(f"{nombre} ya no rotula",
                  "RotuloDelAlzado(" not in m_.group(0)
                  and "RotuloDelElemento(" not in m_.group(0))

    # ------------------------------------------------------------------
    # UNA sola barra arriba, no dos
    # ------------------------------------------------------------------
    # El menu y los botones de acceso rapido gastaban dos filas para ofrecer las
    # mismas acciones. Ahora comparten una, y el Menu va DENTRO del Border de la
    # barra para que los dos grupos queden en la misma linea de base.
    m_barra = re.search(
        r'<Border Grid\.Row="0".*?</Border>', xaml, re.S)
    check("se puede leer la barra de arriba", m_barra is not None)
    if m_barra:
        cuerpo = m_barra.group(0)
        check("el menu vive dentro de la barra", "<Menu " in cuerpo)
        check("y los botones de guardar tambien",
              'Command="ApplicationCommands.Save"' in cuerpo)
        check("y el nombre del archivo abierto", 'x:Name="ArchivoText"' in cuerpo)

    # El Menu no puede seguir siendo un hijo directo del Grid raiz: eso seria la
    # segunda fila que se quito.
    check("el menu ya no es una fila aparte",
          re.search(r'\n        <Menu Grid\.Row=', xaml) is None)

    # ------------------------------------------------------------------
    # Las pestañas NO deben reordenarse al elegir una hoja
    # ------------------------------------------------------------------
    # El TabPanel de WPF mueve la fila de la pestaña seleccionada para dejarla
    # pegada al contenido. Con 12 hojas en dos filas, eso hace que las pestañas
    # SALTEN de sitio en cada clic. Un WrapPanel acomoda igual pero conserva el
    # orden.
    check("la tira de pestañas conserva el orden",
          "<WrapPanel IsItemsHost=\"True\"" in tema)
    check("y ya no usa el TabPanel que reordena",
          "TabPanel IsItemsHost" not in tema)

    # ------------------------------------------------------------------
    # Paneles inmovilizados y encabezado fijo, como en Excel
    # ------------------------------------------------------------------
    m_grid = re.search(r'<DataGrid Grid\.Row="1" x:Name="SeccionesGrid".*?>', xaml, re.S)
    check("se puede leer la apertura del SeccionesGrid", m_grid is not None)
    if m_grid:
        cuerpo = m_grid.group(0)
        check("las primeras columnas quedan inmovilizadas",
              'FrozenColumnCount="2"' in cuerpo)
        check("y el encabezado lleva su propio estilo",
              'ColumnHeaderStyle="{StaticResource EncabezadoHojaStyle}"' in cuerpo)

    check("existe el estilo del encabezado",
          'x:Key="EncabezadoHojaStyle"' in tema)

    # ------------------------------------------------------------------
    # Color de celda por grupo de columnas
    # ------------------------------------------------------------------
    grupos = ["CeldaIdent", "CeldaGeom", "CeldaLechoSup", "CeldaLechoInf",
              "CeldaLateral", "CeldaCircular", "CeldaEstribo", "CeldaAcabado",
              "CeldaCalculada"]

    for g in grupos:
        check(f"existe el estilo {g}", f'x:Key="{g}"' in tema)
        check(f"y alguna columna usa {g}",
              f'CellStyle="{{StaticResource {g}}}"' in xaml)

    # El lecho superior y el inferior tienen que ser de COLORES DISTINTOS: es el par
    # que se confunde al capturar, y pintarlos igual no resolveria nada.
    # Al montar el tema oscuro los fondos de celda dejaron de ser un hex escrito en el
    # Setter y pasaron a ser una brocha de la paleta, que es lo que permite cambiarlos
    # en caliente. Asi que hay que resolver un paso mas: del estilo a la brocha, y de la
    # brocha a su color. Lo que se comprueba sigue siendo lo mismo.
    def color_de_brocha(nombre):
        m_ = re.search(
            rf'<SolidColorBrush x:Key="{nombre}" Color="(#[0-9A-Fa-f]+)"', tema)
        return m_.group(1) if m_ else None

    def fondo_de(clave):
        m_ = re.search(
            rf'x:Key="{clave}".*?Property="Background" '
            rf'Value="\{{StaticResource (\w+)\}}"',
            tema, re.S)

        if m_:
            return color_de_brocha(m_.group(1))

        # Respaldo: si alguien vuelve a poner el hex directo, tambien vale.
        m_ = re.search(
            rf'x:Key="{clave}".*?Property="Background" Value="(#[0-9A-Fa-f]+)"',
            tema, re.S)
        return m_.group(1) if m_ else None

    sup, inf = fondo_de("CeldaLechoSup"), fondo_de("CeldaLechoInf")
    check("el lecho superior y el inferior son de colores distintos",
          sup is not None and inf is not None and sup != inf,
          f"sup {sup}, inf {inf}")

    # Heredan del DataGridCell de serie, o se pierde el resaltado de seleccion y no
    # se ve que fila esta seleccionada.
    check("los estilos de celda heredan del DataGridCell de serie",
          "BasedOn=\"{StaticResource {x:Type DataGridCell}}\"" in tema)

    # Todas las columnas de la hoja tienen que llevar color: una sin asignar se ve
    # como un hueco blanco en medio de los grupos.
    ini_cols = xaml.find('x:Name="SeccionesGrid"')
    fin_cols = xaml.find("</DataGrid.Columns>", ini_cols)
    bloque_cols = xaml[ini_cols:fin_cols]
    # OJO: hay que excluir las etiquetas de PROPIEDAD como
    # <DataGridTemplateColumn.CellTemplate>, que casan con el patron pero no son
    # columnas. Sin el (?!\.) una columna con plantilla contaba tres veces.
    n_cols = len(re.findall(r"<DataGrid\w*Column\b(?!\.)", bloque_cols))
    n_estilos = len(re.findall(r'CellStyle="\{StaticResource Celda', bloque_cols))
    check("todas las columnas de la hoja llevan color",
          n_cols == n_estilos, f"{n_cols} columnas y {n_estilos} con estilo")

    # ------------------------------------------------------------------
    # Vista previa con fondo azul
    # ------------------------------------------------------------------
    # Tema claro / oscuro
    # ------------------------------------------------------------------
    temacs = leer(ruta("client/src/CadLink.App/Tema.cs"))

    check("hay tema claro y oscuro", "public static class Tema" in temacs)
    check("y un boton para cambiarlo", 'x:Name="TemaButton"' in xaml)
    check("el boton vive en la barra de arriba",
          xaml.index('x:Name="TemaButton"') < xaml.index('<TabControl'))
    check("y su manejador existe", "private void OnCambiarTema(" in codigo)

    # El cambio se hace MUTANDO el color de las brochas, no sustituyendo el diccionario:
    # los 221 usos de la paleta son StaticResource y esos no se re-resuelven, asi que
    # cambiar el diccionario con la ventana abierta no repinta nada.
    check("el tema muta el color de las brochas",
          "brocha.Color = color;" in temacs)
    check("y no sustituye el diccionario, que no repintaria",
          "MergedDictionaries" not in temacs)

    # Las dos paletas tienen que tener LAS MISMAS claves, o al cambiar de tema quedarian
    # colores del tema anterior mezclados.
    m_claro = re.search(r"Claro = new\(\)\s*\{(.*?)\n    \};", temacs, re.S)
    m_noche = re.search(r"Noche = new\(\)\s*\{(.*?)\n    \};", temacs, re.S)
    check("se pueden leer las dos paletas",
          m_claro is not None and m_noche is not None)

    if m_claro and m_noche:
        kc = set(re.findall(r'\["(\w+)"\]', m_claro.group(1)))
        kn = set(re.findall(r'\["(\w+)"\]', m_noche.group(1)))
        check("las dos paletas cubren las mismas brochas",
              kc == kn, f"solo en claro: {kc-kn}; solo en oscuro: {kn-kc}")

        # Y toda brocha de la paleta que el XAML use tiene que estar en las dos.
        usadas = set(re.findall(r"\{StaticResource (\w+Brush)\}", xaml + tema))
        declaradas = set(re.findall(r'<SolidColorBrush x:Key="(\w+)"', tema))
        # Solo las que el tema declara Y el XAML usa
        deberian = usadas & declaradas
        # Dos grupos se quedan CLAROS en los dos temas, a proposito, asi que no estan
        # en las paletas:
        #
        #   PreviewFondoBrush  el dibujo de la previa va en tinta oscura, pintada desde
        #                      codigo; sobre fondo oscuro no se veria.
        #   Celda*Brush        son los colores de las columnas de la hoja, la unica cosa
        #                      que separa los 27 grupos al capturar. El usuario pidio
        #                      expresamente conservarlos.
        #   Fila/Acero*Brush   las marcas de la hoja de acero: el fondo suave de la fila
        #                      cuyo acero no se hace en ese perfil, el rojo de su celda y
        #                      el ambar del «verificar». Van con el grupo de arriba y por
        #                      el mismo motivo: las celdas de la hoja se quedan claras en
        #                      los dos temas, asi que la marca que va ENCIMA de ellas
        #                      tambien tiene que quedarse clara. Una marca que cambia de
        #                      tema sobre una celda que no lo cambia deja de contrastar.
        #   Lista*Brush        la ventanita de una lista desplegable se queda clara en
        #                      los dos temas, por lo mismo que la cuadricula: es donde
        #                      se elige un dato, no parte del marco. Se les dio brocha
        #                      propia porque antes tomaban GridRowBrush -que SI cambia
        #                      con el tema- y en oscuro la letra se leia casi negra
        #                      sobre gris oscuro.
        aparte = ({"PreviewFondoBrush"}
                  | {b for b in declaradas if b.startswith("Celda")}
                  | {b for b in declaradas if b.startswith("Lista")}
                  | {b for b in declaradas
                     if b.startswith("FilaAcero") or b.startswith("Acero")})

        faltan = deberian - kc - aparte
        check("toda brocha usada esta en las paletas, salvo las que se quedan claras",
              not faltan, f"faltan: {sorted(faltan)}")

        check("la vista previa se queda clara en los dos temas a proposito",
              "PreviewFondoBrush" not in kc and "PreviewFondoBrush" not in kn)

        # Y los colores de la hoja no los toca NINGUNA de las dos paletas.
        check("los colores de las columnas de la hoja no los cambia el tema",
              not any(k.startswith("Celda") for k in kc | kn),
              f"celdas en las paletas: {sorted(k for k in kc | kn if k.startswith('Celda'))}")

        # Pero SI tienen que seguir declarados como brochas, o el estilo no compila.
        check("aunque siguen siendo brochas de la paleta",
              'x:Key="CeldaLechoSupBrush"' in tema)

        # Lo que si tiene que oscurecerse es todo lo blanco del marco.
        # La cuadricula NO entra en esta lista: va en un gris intermedio a proposito,
        # para que el salto del marco negro a las celdas pastel no sea tan duro.
        for clave in ("WindowBrush", "SurfaceBrush", "CardBrush", "TabStripBrush"):
            hex_osc = re.search(rf'\["{clave}"\] = "#FF(\w{{6}})"', m_noche.group(1))
            claro_es = int(hex_osc.group(1)[:2], 16) if hex_osc else 255
            check(f"en oscuro {clave} es realmente NEGRO, no gris",
                  hex_osc is not None and claro_es < 0x20,
                  f"vale #{hex_osc.group(1) if hex_osc else '?'}")

    # El azul de marca se usa como color de TEXTO en los encabezados y en el boton de
    # guardar, asi que en oscuro tiene que ACLARARSE o desaparece.
    if m_noche:
        cuerpo = m_noche.group(1)
        m_bd = re.search(r'\["BrandDarkBrush"\] = "#FF(\w{6})"', cuerpo)
        check("en oscuro el azul de marca se aclara, porque es color de texto",
              m_bd is not None and int(m_bd.group(1)[:2], 16) > 0x60,
              f"vale #{m_bd.group(1) if m_bd else '?'}")

        # El lecho superior contra el inferior ya se comprueba sobre el XAML, mas
        # arriba, y con esto vale para los dos temas: si el tema no toca esos colores,
        # basta comprobarlos una vez.

    # La preferencia va en LOCALAPPDATA, no en el .clk: el tema es del usuario y de su
    # maquina, no del trabajo. En el proyecto obligaria a subir la version del formato y
    # abrir el archivo de un compañero te cambiaria el tema.
    check("el tema se recuerda en la carpeta del usuario",
          "SpecialFolder.LocalApplicationData" in temacs)
    check("y no se guarda en el proyecto",
          "TemaOscuro" not in leer(ruta("client/src/CadLink.App/Models/Proyecto.cs")))

    # Al cambiar de tema hay que REDIBUJAR los lienzos: su contenido se pinta desde
    # codigo, no con brochas de la paleta, asi que no se enteran solos.
    m_oct = re.search(r"private void OnCambiarTema\(.*?\n    \}", codigo, re.S)
    if m_oct:
        cuerpo = m_oct.group(0)
        check("al cambiar de tema se redibuja la vista previa",
              "DibujarVistaPrevia()" in cuerpo)
        check("y se actualiza el texto del boton",
              "TemaButton.Content" in cuerpo)

    # Y los colores quemados a mano salieron a la paleta, o no podrian cambiar.
    # DynamicResource y no StaticResource: es lo que permite SUSTITUIR el recurso
    # cuando la brocha esta congelada, que es el caso en que el tema no aplicaba.
    check("el fondo de la ventana sale de la paleta",
          'Background="{DynamicResource WindowBrush}"' in xaml)
    # LAS TARJETAS YA NO LLEVAN SU PINTA ESCRITA. Antes cada Border repetia fondo,
    # borde, grosor y radio -y no siempre iguales: habia tarjetas de radio 4 y de
    # radio 0-. Ahora hay UN estilo, asi que lo que se comprueba es que el estilo
    # saque el color de la paleta y que las hojas lo usen, no que el hex este
    # repetido trece veces.
    check("las tarjetas salen de un solo estilo",
          'x:Key="TarjetaStyle"' in tema
          and '<Setter Property="Background" Value="{DynamicResource CardBrush}" />' in tema)
    check("y todas las hojas lo usan",
          xaml.count('Style="{StaticResource TarjetaStyle}"') >= 10)
    check("ninguna tarjeta se quedo con la pinta escrita a mano",
          'Background="{DynamicResource CardBrush}" BorderBrush=' not in xaml)

    # Los RadioButton de «Seccion tipo 1 / tipo 2» y los CheckBox salian con el texto
    # NEGRO por omision de Windows, asi que en tema oscuro desaparecian.
    check("las opciones y casillas tambien siguen el tema",
          '<Style TargetType="RadioButton">' in tema
          and '<Style TargetType="CheckBox">' in tema)

    # Y la cuadricula va en gris INTERMEDIO, no en negro: el salto del marco negro a
    # unas celdas pastel claras seria demasiado duro justo donde esta la vista.
    if m_noche:
        gris = re.search(r'\["GridRowBrush"\] = "#FF(\w{6})"', m_noche.group(1))
        nivel = int(gris.group(1)[:2], 16) if gris else 0
        check("la cuadricula va en un gris intermedio, no en negro",
              gris is not None and 0x30 < nivel < 0x80,
              f"vale #{gris.group(1) if gris else '?'}")
    check("ya no queda el gris de tarjeta escrito a mano",
          '#FFF3F6F9' not in xaml)

    # ------------------------------------------------------------------
    check("hay color de fondo para la vista previa",
          'x:Key="PreviewFondoBrush"' in tema)
    check("y el lienzo de la vista previa lo usa",
          'x:Name="PreviewCanvas"' in xaml
          and 'Background="{StaticResource PreviewFondoBrush}"' in xaml)

    # La linea de titulo es la MISMA para las dos formas: antes la rectangular solo
    # decia elemento e ID y la circular ademas el armado, asi que no se veian igual.
    check("la vista previa tiene una linea de titulo comun",
          "private static string TituloVistaPrevia(" in codigo)
    check("y la usan las dos formas",
          codigo.count("Etiqueta(TituloVistaPrevia(s)") == 2,
          f"la usa {codigo.count('Etiqueta(TituloVistaPrevia(s)')} vez/veces")

    # La vista previa tambien dibuja la helice, o mostraria estribos rectos donde
    # AutoCAD va a dibujar un resorte.
    check("la vista previa dibuja la helice",
          "private void DibujarHelicePrevia(" in codigo)
    check("y se elige segun el zuncho",
          "a.Circular && a.ZunchoHelicoidal" in codigo)

    # ------------------------------------------------------------------
    # Rotulos del alzado de la columna circular
    # ------------------------------------------------------------------
    # Sin esto los tres textos de armado leian lechos VACIOS y salian como «---»:
    # el alzado de la columna redonda se quedaba sin rotulo de armado.
    check("hay texto de armado para el circulo",
          "private static string TextoCirculo(" in alz)
    check("y el alzado vertical lo usa", "TextoCirculo(a)" in alz)

    # Y el acero transversal se llama por su nombre, pero el nombre lo decide LA CASILLA:
    # con zuncho pedido dice «Zuncho helic.», y sin casilla «Est.», como cualquier columna.
    check("hay texto propio del acero transversal",
          "private static string TextoTransversal(" in alz)
    m_tt = re.search(r"private static string TextoTransversal\(.*?\n    \}", alz, re.S)
    if m_tt:
        cuerpo = m_tt.group(0)
        check("con la casilla marcada dice Zuncho helic.", '"Zuncho helic. ' in cuerpo)
        check("y sin la casilla dice Est.", '"Est. ' in cuerpo)
        check("y la decision no la toma el texto, la toma Estribos.EsZuncho",
              "Estribos.EsZuncho(a.Circular, a.ZunchoHelicoidal)" in cuerpo
              and "anillos" not in cuerpo)

    # Lo usan los DOS alzados, el vertical y el horizontal.
    check("los dos alzados usan el texto transversal",
          alz.count("TextoTransversal(a, s[i])") == 2,
          f"lo usan {alz.count('TextoTransversal(a, s[i])')} vez/veces")
    # Antes aqui se exigia que NO hubiera ningun «Est.» fijo en el alzado, porque la
    # circular tenia que decir «Zuncho». Ya no aplica: sin la casilla del zuncho, la
    # circular dice «Est.» a proposito, que es lo que se pidio. Lo que se comprueba ahora es
    # que el texto NO se arme en dos sitios distintos, que era el defecto de fondo.
    check("el texto del transversal se arma en un solo sitio por vista",
          alz.count('$"Est. {clave} @ {separacionCm:0} cm"') == 1
          and alz.count('$"Est. {Etiqueta(a.Estribo.Clave)} @ {sep} cm"') == 1)


# ======================================================================
# 20. Miembros estaticos usados desde OTRA clase sin cualificar (CS0103)
# ======================================================================
def _pila_de_clases(texto: str) -> list[list[str]]:
    """Para cada linea, la PILA de clases que la contienen, de fuera hacia dentro.

    Se cuenta la profundidad de llaves de verdad, en lugar de suponer que la clase
    vigente es la ultima declarada. La diferencia importa en cuanto hay una clase
    ANIDADA: con el atajo, todo lo que viene DESPUES del cierre de la anidada se
    atribuye a ella, y eso producia dos falsos positivos distintos que parecian no
    tener nada que ver entre si:

      - 'MargenCol' de AlzadoLayout se reportaba como usado dentro de Puesto,
        porque Puesto se declara antes del metodo que lo usa.
      - 'Normalizar' de VistaModelo se reportaba como no declarado, porque su
        declaracion cae despues de un tipo anidado y se atribuia a el.

    El texto tiene que venir SIN comentarios ni cadenas, o una llave dentro de una
    cadena descuadra la cuenta.
    """
    salida: list[list[str]] = []
    pila: list[tuple[str, int]] = []      # (nombre, profundidad del cuerpo)
    profundidad = 0
    pendiente: str | None = None          # clase declarada, cuerpo aun sin abrir

    for linea in texto.split("\n"):
        salida.append([nombre for nombre, _ in pila])

        m = re.search(r"\b(?:class|struct|record|interface)\s+(\w+)", linea)
        if m and not re.match(r"\s*(?://|\*)", linea):
            pendiente = m.group(1)

        for ch in linea:
            if ch == "{":
                profundidad += 1
                if pendiente is not None:
                    pila.append((pendiente, profundidad))
                    pendiente = None
            elif ch == "}":
                while pila and pila[-1][1] == profundidad:
                    pila.pop()
                profundidad -= 1

    return salida


def v20_estaticos_sin_cualificar() -> None:
    """Un `const` de otra clase usado a pelo. Rompe la compilacion.

    Por que existe esta seccion: `CadLink.App` no se puede compilar en este
    entorno, asi que un CS0103 llega hasta el usuario. Paso exactamente eso con
    `ElementoColumnaCircular`, declarada en SeccionConcretoRow y usada a pelo
    dentro de DatosProyecto, en el MISMO archivo, que es lo que lo hace facil de
    pasar por alto: parece que esta en ambito y no lo esta.

    La comprobacion v15 no lo caza porque solo mira identificadores en posicion de
    ARGUMENTO de llamada, y este estaba en un inicializador de objeto.
    """
    print("\n[20] Miembros estaticos de otra clase, sin cualificar")

    # ------------------------------------------------------------------
    # 1. Se recogen los miembros estaticos publicos, por clase
    # ------------------------------------------------------------------
    # Solo const y static: los de instancia no pueden usarse sin objeto y el
    # compilador da otro error distinto.
    # Los estaticos PUBLICOS, que son los unicos que otra clase puede usar.
    declarados: dict[str, str] = {}     # nombre del miembro -> clase que lo declara

    # TODOS los miembros de cada clase, de cualquier visibilidad. Hace falta para
    # descartar el falso positivo importante: que la clase que usa el nombre tenga
    # un miembro PROPIO llamado igual. Pasa de verdad y varias veces:
    # AppInfo y Branding tienen cada una su 'Cargar' privado, MainWindow tiene su
    # 'Guardar', y AppConfig tiene su 'RutaLibreriaEtabs' de instancia. Sin esto la
    # comprobacion reporta seis errores que no existen.
    miembros_de: dict[str, set[str]] = {}

    clases_por_archivo: dict[str, list[tuple[int, str]]] = {}

    rutas = [p for p in archivos(".cs", "client/src") if "obj" not in p and "bin" not in p]

    for p in rutas:
        texto = _sin_comentarios(leer(p))
        lineas = texto.split("\n")

        pilas = _pila_de_clases(texto)
        clases_por_archivo[p] = pilas

        def clase_en(idx: int, ps=pilas) -> str:
            """La clase mas interna que contiene la linea."""
            return ps[idx][-1] if idx < len(ps) and ps[idx] else ""

        for i, l in enumerate(lineas):
            # Nombres declarados DENTRO de esta clase. Se recoge de mas a proposito:
            # esto solo sirve para descartar falsos positivos, asi que colar algun
            # nombre extra no hace daño y perder uno si.
            #
            # Hacen falta las dos formas de abajo, y las dos costaron un falso
            # positivo antes de estar bien:
            #
            #   - Metodos y FUNCIONES LOCALES, a cualquier sangria. 'Leer' es una
            #     funcion local dentro de un metodo de MainWindow, a 8 espacios, y
            #     con un patron de solo 4 se reportaba como si fuera el 'Leer' de
            #     EtabsReader.
            #   - El tipo puede llevar PARENTESIS, porque puede ser una tupla.
            #     'Normalizar' devuelve (double X, double Y, double Z) y por eso no
            #     se recogia, aunque estuviera declarada en la misma clase parcial.
            # Metodos y funciones locales. NO se exige emparejar el parentesis de
            # cierre: los parametros pueden llevar parentesis anidados, como en
            # 'Normalizar((double, double, double) v)', y con \([^)]*\) esa linea no
            # casaba y su nombre se perdia.
            #
            # Para no confundir una LLAMADA con una declaracion se pide que la linea
            # no termine en ';'. Asi 'var a = Leer(0);' queda fuera y
            # 'double Leer(int i)' dentro.
            # Se recogen TODOS los identificadores seguidos de '(' de la linea, no
            # solo el primero. Con un patron de un solo nombre se capturaba el
            # equivocado en cuanto el tipo de retorno era una TUPLA: en
            #     private static (double X, double Y, double Z) Normalizar(...)
            # el parentesis de la tupla hace que 'static' parezca el nombre del
            # metodo, y 'Normalizar' no se registraba nunca.
            #
            # Recoger de mas es seguro para lo que esto sirve: solo se usa para
            # descartar falsos positivos, y un nombre seguido de '(' nunca es una
            # constante, que es la clase de error que se quiere cazar.
            if not l.rstrip().endswith(";"):
                for m_met in re.finditer(r"\b(\w+)\s*\(", l):
                    if m_met.group(1) not in _NO_ES_LLAMADA:
                        miembros_de.setdefault(clase_en(i), set()).add(m_met.group(1))

            # Y TRES CLASES DE DECLARACION QUE SE ESCAPABAN, cada una con su falso
            # positivo real detras. Las tres se registran igual que las de arriba:
            # como nombres que en esa clase ya significan algo, para no reportarlos.
            #
            #   1. FUNCION LOCAL DE UNA SOLA EXPRESION. La linea
            #          byte Canal(byte v) => (byte)Math.Clamp(...);
            #      acaba en ';', asi que el bloque de arriba la tomaba por una LLAMADA
            #      y no registraba 'Canal'. Luego, al existir FormaAcero.Canal, sus dos
            #      usos de la linea siguiente se reportaban como estatico sin cualificar.
            m_loc = re.match(
                r"^\s*(?:static\s+)?[\w<>,?\[\]\.]+\s+(\w+)\s*\([^;]*\)\s*(?:=>|\{|$)", l)
            if m_loc and m_loc.group(1) not in _NO_ES_LLAMADA:
                miembros_de.setdefault(clase_en(i), set()).add(m_loc.group(1))

            #   2. VARIABLE O CONSTANTE LOCAL. La linea
            #          const double L = 26;
            #      declara una L dentro de un metodo, y al existir FamiliaPerfil.L sus
            #      tres usos se reportaban. Una local tapa a un estatico de otra clase
            #      exactamente igual que un miembro propio. El 'const' va aparte del
            #      tipo porque son TRES palabras y no dos: con un solo hueco para el
            #      tipo, 'const double L' dejaba a 'double' de nombre y no casaba.
            m_var = re.match(
                r"^\s{8,}(?:(?:const|readonly|static)\s+)*"
                r"(?:var\s+|[\w<>,?\[\]\.]+\s+)(\w+)\s*(?:=[^=>]|;)", l)
            if m_var:
                miembros_de.setdefault(clase_en(i), set()).add(m_var.group(1))

            #   2b. EL NOMBRE DE UN TIPO ANIDADO. En
            #          public sealed record Circulo(double Cx, double Cy, double R);
            #      dentro de TrazoAcero, el 'Circulo' es una declaracion, no un uso, y se
            #      reportaba contra el Circulo de Perfil2D.
            m_tipo = re.match(
                r"^\s+(?:public|private|protected|internal)[\w\s]*?"
                r"\b(?:class|record|struct|interface|enum)\s+(\w+)", l)
            if m_tipo:
                miembros_de.setdefault(clase_en(i), set()).add(m_tipo.group(1))

            #   3. PARAMETROS, incluidos los POSICIONALES DE UN RECORD, que son
            #      propiedades. En
            #          public sealed record PerfilCatalogo(
            #              string Familia,
            #              string Nombre,
            #      cada renglon declara una, y 'Nombre' choca con el FormaAcero.Nombre;
            #      y en
            #          public sealed record Resumen(int Solidos, int Lineas)
            #      los dos van en la misma linea, asi que no basta con mirar renglones
            #      sueltos. Se recogen todas las parejas «tipo nombre» que van pegadas a
            #      una coma o a un parentesis de cierre.
            #      La condicion mira '(' o ')' o una coma al final porque una lista de
            #      parametros se PARTE en varias lineas, y las de en medio no traen
            #      ningun parentesis: el 'string Nombre,' del record de arriba y el
            #      'double T2, double T3, string Forma, ...);' del EtabsReader son las
            #      dos continuaciones, y las dos se escapaban con un solo '(' de guarda.
            if "(" in l or ")" in l or l.rstrip().endswith(","):
                for m_par in re.finditer(
                        r"\b[\w<>,?\[\]\.]+\s+(\w+)\s*(?=[,)])", l):
                    miembros_de.setdefault(clase_en(i), set()).add(m_par.group(1))

                # Y los parametros CON VALOR POR DEFECTO, que no acaban en coma ni en
                # parentesis sino en un '='. Es lo que tiene el record de propiedades:
                #     public sealed record PropiedadesPerfil(
                #         double? PesoKgM = null,
                #         double? AreaCm2 = null,
                # y el 'AreaCm2' se reportaba contra el AreaCm2 de la clase Varilla,
                # siendo su propia declaracion. Se pide un tipo y un nombre separados por
                # espacio, asi que un 'Familia = familia,' de un inicializador de objeto
                # -que solo tiene un nombre antes del '='- no cae aqui.
                for m_def in re.finditer(
                        r"\b[\w<>,?\[\]\.]+\s+(\w+)\s*=\s*[^=]", l):
                    miembros_de.setdefault(clase_en(i), set()).add(m_def.group(1))

            # Propiedades y campos, a CUALQUIER sangria de 4 o mas. Con 4 exactos se
            # perdian los de las clases anidadas, que van a 8: por eso el 'XSeccion'
            # de AlzadoLayout.Puesto se reportaba contra el metodo estatico del mismo
            # nombre de la clase de fuera.
            m_prop = re.match(
                r"^ {4,}(?:public|private|protected|internal)[^;=]*?"
                r"\b(\w+)\s*(?:\{|=>|=|;)", l)
            if m_prop:
                miembros_de.setdefault(clase_en(i), set()).add(m_prop.group(1))

            # Y los estaticos publicos, que son los que se pueden usar desde fuera
            m = re.match(
                r"\s*public\s+(?:const|static\s+readonly|static)\s+"
                r"[\w<>,?\[\]\.]+\s+(\w+)\s*(?:=|\()", l)
            if m:
                declarados[m.group(1)] = clase_en(i)

    check("se recogieron miembros estaticos", len(declarados) > 0,
          f"{len(declarados)}")

    # ------------------------------------------------------------------
    # 2. Cada uso tiene que estar en su clase, o cualificado
    # ------------------------------------------------------------------
    problemas: list[str] = []
    usos_revisados = 0

    # Los tres patrones de cada miembro se compilan UNA vez, no en cada renglon.
    # Con mas de 500 miembros declarados el cache de re se invalida y Python los
    # recompilaba linea por linea: la comprobacion se quedaba colgada minutos y
    # parecia un cuelgue del validador. Es el MISMO patron, solo compilado antes.
    patrones = {
        miembro: (
            re.compile(r"^\s*public\s.*\b" + miembro + r"\b\s*(?:=|\()"),
            re.compile(r"(\.)?\b" + miembro + r"\b"),
            re.compile(r"\s*" + miembro + r"\s*=[^=]"),
        )
        for miembro in declarados
    }

    for p in rutas:
        texto = _sin_comentarios(leer(p))
        lineas = texto.split("\n")
        pilas = clases_por_archivo[p]

        for i, l in enumerate(lineas):
            # La PILA entera, no solo la clase mas interna: desde una clase anidada
            # se ven los miembros de la que la contiene sin cualificar nada.
            pila = pilas[i] if i < len(pilas) else []

            for miembro, duena in declarados.items():
                # Atajo: sin el nombre escrito tal cual, ningun patron puede casar.
                if miembro not in l:
                    continue

                pat_decl, pat_uso, pat_init = patrones[miembro]

                # Uso, no declaracion
                if pat_decl.search(l):
                    continue

                for m in pat_uso.finditer(l):
                    if m.group(1):
                        continue        # ya viene cualificado con algo

                    if duena == "" or duena in pila:
                        continue        # esta en su clase o en una que la contiene

                    # Alguna clase del ambito tiene un miembro propio con ese
                    # nombre: el identificador se resuelve a ESE.
                    #
                    # El "" del final es el ambito de FUERA de toda clase, y hacia
                    # falta: la lista de parametros de un record se escribe ANTES de
                    # que se abra su cuerpo, asi que sus renglones caen en ese ambito
                    # y con la pila vacia no se consultaba nada. Por eso el
                    # 'string Nombre,' del PerfilCatalogo se reportaba contra el
                    # FormaAcero.Nombre, siendo su propia declaracion.
                    if any(miembro in miembros_de.get(c, set()) for c in list(pila) + [""]):
                        continue

                    aqui = pila[-1] if pila else ""

                    # Nombre de propiedad dentro de un inicializador de objeto:
                    # 'Elemento = ...' se resuelve contra el tipo que se construye.
                    if pat_init.match(l):
                        continue

                    usos_revisados += 1

                    problemas.append(
                        f"{rel(p)}:{i+1}: '{miembro}' se declara en {duena} y se usa "
                        f"a pelo dentro de {aqui}")
                    break

    check(f"ningun estatico de otra clase sin cualificar ({usos_revisados} usos "
          f"sospechosos revisados)", not problemas, "; ".join(problemas[:6]))

    # ------------------------------------------------------------------
    # 3. El caso concreto que rompio la compilacion, fijado
    # ------------------------------------------------------------------
    filas = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))

    m_ej = re.search(r"public static DatosProyecto CrearEjemplo\(\).*?\n    \}",
                     filas, re.S)
    check("se puede leer CrearEjemplo", m_ej is not None)
    if m_ej:
        cuerpo = m_ej.group(0)

        # CrearEjemplo vive en DatosProyecto, asi que las constantes de
        # SeccionConcretoRow tienen que ir con el nombre de la clase delante.
        for c in ("ElementoColumnaCircular", "ElementoColumna"):
            # Vale CUALQUIER clase delante, no solo SeccionConcretoRow. Con el nombre
            # fijo, PerfilAceroRow.ElementoColumna -que es de la hoja de acero y esta
            # perfectamente cualificada- salia marcada como uso a pelo.
            usos = re.findall(r"([A-Za-z_][A-Za-z0-9_]*\.)?\b" + c + r"\b", cuerpo)
            sin_cualificar = [u for u in usos if u == ""]
            check(f"en CrearEjemplo, {c} va cualificada",
                  not sin_cualificar,
                  f"{len(sin_cualificar)} uso(s) a pelo")


def main() -> int:
    print("=" * 66)
    print(" Validaciones estaticas de CadLink")
    print(" (esto NO sustituye a compilar: revisa lo que un compilador no ve)")
    print("=" * 66)

    for f in (v1_xml, v2_bat, v3_usings, v4_cs0050, v5_value,
              v6_handlers, v7_names, v8_python, v9_modo, v10_nombres_tapados,
              v11_visor, v12_fidelidad, v13_compilacion,
              v14_bloques_diamante_etabs, v15_cs0103,
              v16_extruida_piers, v17_guardar_y_defaults,
              v18_planta_autocad, v19_circular_y_ui,
              v20_estaticos_sin_cualificar,
              v21_separacion_y_acero,
              v22_zapatas_corridas,
              v23_hoja_zapatas_corridas,
              v24_rediseno):
        f()

    print("\n" + "=" * 66)
    if fallos:
        print(f" RESULTADO: {len(fallos)} comprobacion(es) fallaron")
        for f_ in fallos:
            print(f"   - {f_}")
        print("=" * 66)
        return 1
    print(" RESULTADO: todas las comprobaciones pasaron")
    print("=" * 66)
    return 0





# ======================================================================
#  CS0103: "El nombre 'x' no existe en el contexto actual"
# ======================================================================
# Por que hace falta. Este error se colo ya varias veces, y la ultima fue una
# tonteria: al cambiar ColorNegro() por ColorNegro(ent) en AlzadoDrawer, la
# variable 'ent' no existia todavia en ese punto, porque se declaraba DENTRO del
# bucle de mas abajo. El validador tenia una comprobacion de numero de argumentos
# (CS1501) pero ninguna de si el argumento EXISTE, asi que paso limpio y el
# usuario se comio el error de compilacion.
#
# Lo que se hace aqui es un analisis de ambito de mentirijillas: por cada metodo
# se junta lo que hay declarado en el (parametros, locales, variables de bucle,
# de catch, de patron, de lambda) y lo que hay a nivel de clase en todo el
# proyecto, y se revisa que cada identificador usado COMO ARGUMENTO de una
# llamada este en alguno de los dos sitios.
#
# Es deliberadamente conservador: solo mira identificadores que empiezan en
# minuscula (los locales, por convencion) y solo en posicion de argumento suelto.
# Un analizador completo necesitaria un compilador; esto atrapa el error real que
# se cometio sin inventar fallos donde no hay.
#
# QUE SI DETECTA, comprobado con mutaciones:
#   - un nombre que no existe en ninguna parte
#   - un nombre que existe pero se declara MAS ABAJO del punto donde se usa
#   - un local de otro metodo
#   - un miembro de una clase ANIDADA usado desde la clase de fuera
#   - un delegado o metodo pasado por nombre y mal escrito
#   - un control de XAML que no existe
#
# LIMITACIONES CONOCIDAS, para no confiarse mas de lo debido:
#
#   1. Solo se miran los ARGUMENTOS, nunca el nombre del metodo al que se llama.
#      Comprobado: llamar a 'Proyectar(...)' desde fuera de la clase anidada donde
#      esta declarado NO se detecta. Resolver eso pide sobrecargas y herencia de
#      verdad, o sea un compilador; y a cambio daria falsos positivos con los
#      metodos heredados y los de extension.
#   2. La lista de miembros es la union de todas las clases de primer nivel del
#      proyecto, no la de la clase concreta. Un nombre que exista como miembro de
#      OTRA clase de primer nivel pasa por bueno.

# Palabras que son argumentos validos sin estar declaradas en ninguna parte.
# Construcciones del lenguaje que llevan parentesis y NO son llamadas.
_NO_ES_LLAMADA = {
    "if", "while", "for", "foreach", "switch", "catch", "lock", "using",
    "return", "fixed", "checked", "unchecked", "sizeof", "typeof", "nameof",
    "new", "await", "yield", "throw", "when", "is", "as", "and", "or", "not",
    # 'var (a, b) = ...' es una deconstruccion de tupla, no una llamada.
    "var",
}

_PALABRAS_LIBRES = {
    "true", "false", "null", "this", "base", "value", "default",
    "out", "ref", "in", "var", "new", "nameof", "typeof", "sizeof",
    "string", "int", "double", "bool", "object", "dynamic", "long",
    "short", "byte", "float", "decimal", "char", "void",
}

_DECLARA = [
    # var x = ... / Tipo x = ...
    r"\bvar\s+(\w+)\s*=",
    r"^\s*[\w<>,?\[\]\.]+\s+(\w+)\s*=[^=]",
    # var (a, b) = ...  y  foreach (var (a, b) in ...)
    r"\bvar\s*\(\s*(\w+)\s*,\s*(\w+)\s*\)",
    # foreach (var x in ...) / foreach (Tipo x in ...)
    r"\bforeach\s*\(\s*(?:var|[\w<>,?\[\]\.]+)\s+(\w+)\s+in\b",
    # for (var i = 0; ...)
    r"\bfor\s*\(\s*(?:var|[\w<>,?\[\]\.]+)\s+(\w+)\s*=",
    # catch (Exception ex)
    r"\bcatch\s*\(\s*[\w\.]+\s+(\w+)\s*\)",
    # out var x / is Tipo x
    r"\bout\s+(?:var|[\w<>,?\[\]\.]+)\s+(\w+)",
    r"\bis\s+(?:not\s+)?[\w<>,?\[\]\.]+\s+(\w+)\b",
    # is { } x  /  is { Prop: 1 } x  -> el patron de propiedades TAMBIEN declara. Sin
    # esto, 'if (t is { } c) lista.Add(c);' se reportaba como un CS0103 que no existe.
    r"\bis\s+(?:not\s+)?\{[^{}]*\}\s+(\w+)\b",
    # lambdas: x => ...   y   (x, y) => ...
    r"\(?\s*\b(\w+)\s*\)?\s*=>",
    r"\(\s*(\w+)\s*,\s*(\w+)\s*\)\s*=>",
    # declaracion suelta sin asignar: Tipo x;
    r"^\s*[\w<>,?\[\]\.]+\s+(\w+)\s*;",
    # locales con modificador: const double ux = ...
    r"^\s*(?:const|static|readonly)\s+[\w<>,?\[\]\.]+\s+(\w+)\s*=",
]


def _bloque_llaves(t: str, i: int) -> str | None:
    """Contenido del bloque '{...}' que abre en 't[i]', con las llaves balanceadas."""
    if i >= len(t) or t[i] != "{":
        return None

    # Se limpian comentarios y cadenas del tramo, para que una llave dentro de un
    # texto o de un comentario no descuadre el conteo.
    limpio = _sin_comentarios(t[i:])

    prof = 0
    for j, c in enumerate(limpio):
        if c == "{":
            prof += 1
        elif c == "}":
            prof -= 1
            if prof == 0:
                return limpio[1:j]

    return None


def _nombres_declarados(cuerpo: str, firma: str) -> dict[str, int]:
    """
    Nombres declarados dentro de un metodo, con la POSICION de su declaracion.

    La posicion importa, y mucho. La primera version de esto devolvia solo un
    conjunto de nombres, y por eso NO detecto el error de verdad: se usaba
    'ColorNegro(ent)' antes del bucle, y 'ent' se declaraba DENTRO del bucle, mas
    abajo. El nombre existia en el metodo, asi que el conjunto lo daba por bueno.
    Guardando donde se declara cada uno se puede exigir lo que exige C#: que la
    declaracion vaya ANTES del uso.
    """
    donde: dict[str, int] = {}

    def anota(nombre: str, pos: int) -> None:
        if nombre and (nombre not in donde or pos < donde[nombre]):
            donde[nombre] = pos

    # Los parametros valen desde el principio del cuerpo.
    for trozo in firma.split(","):
        # Se corta el valor por omision ANTES de buscar el nombre: en
        # 'bool forceNewDrawing = false' el ultimo identificador es 'false'.
        s = trozo.split("=")[0].strip()
        if not s:
            continue
        ids = re.findall(r"(\w+)", s)
        if ids:
            anota(ids[-1], -1)

    # Los corchetes con espacios, normalizados: 'object?[ ]' -> 'object?[]'.
    # Se hace SIN cambiar la longitud del texto para que las posiciones sigan
    # valiendo: se sustituye por el mismo numero de caracteres.
    cuerpo = re.sub(r"\[(\s+)\]", lambda m: "[]" + " " * len(m.group(1)), cuerpo)

    for patron in _DECLARA:
        for m in re.finditer(patron, cuerpo, re.M):
            for i, g in enumerate(m.groups(), start=1):
                if g:
                    anota(g, m.start(i))

    # Varios declaradores en una sola linea:
    #     double uMin = double.MaxValue, uMax = double.MinValue;
    for m in re.finditer(r"^[^\S\n]*(?:var|[\w<>,?\[\]\.]+)[^\S\n]+\w+\s*=[^=]",
                         cuerpo, re.M):
        linea_ini = m.start()
        linea = cuerpo[linea_ini:cuerpo.find("\n", linea_ini) if "\n" in cuerpo[linea_ini:] else len(cuerpo)]
        for d in re.finditer(r"(\w+)\s*=(?!=)", linea):
            anota(d.group(1), linea_ini + d.start(1))

    # Declaraciones cuyo TIPO es generico y lleva espacios dentro de los angulos:
    #     Func<string?, double> catalogo = MiMetodo;
    #     Dictionary<string, List<int>> tabla = new();
    # El patron de arriba no las pilla, porque su tipo no admite espacios, y por eso
    # 'catalogoDeVarillas' salia como no declarado: era un falso positivo del analizador,
    # no un error del codigo.
    for m in re.finditer(r"^[^\S\n]*[\w\.]+\s*<[^;=\n]*?>\s+(\w+)\s*=(?!=)",
                         cuerpo, re.M):
        anota(m.group(1), m.start(1))

    # Deconstruccion de tuplas de cualquier tamaño: var (x, y, z, r)
    for m in re.finditer(r"\bvar\s*\(([^()]*)\)", cuerpo):
        for d in re.finditer(r"\w+", m.group(1)):
            anota(d.group(0), m.start(1) + d.start())

    # Funciones locales: sus parametros son locales del metodo.
    #     void Medir(double x, double y) { ... }
    for m in re.finditer(
        r"\b(?:static\s+)?[\w<>,?\[\]\.]+\s+(\w+)\s*\(([^()]*)\)\s*(?:\{|=>)", cuerpo
    ):
        # El NOMBRE de la funcion local tambien es un nombre en ambito: se pasa como
        # delegado, y asi era como se usaba 'APantallaPlanta'.
        anota(m.group(1), m.start(1))

        for parte in m.group(2).split(","):
            ids = re.findall(r"\w+", parte)
            if ids:
                anota(ids[-1], m.start(2))

    return donde


def _miembros_del_proyecto(textos: list[str]) -> set[str]:
    """
    Miembros de las clases de PRIMER NIVEL: los declarados a 4 espacios.

    La sangria es la que separa lo que se ve de lo que no. Este analizador solo
    revisa metodos a 4 espacios, o sea miembros de la clase exterior, y esos no
    pueden llamar a los miembros de una clase anidada sin nombrarla. Recogiendo
    todo junto, 'Camara.APantalla' hacia que un 'APantalla' suelto en la clase de
    fuera pasara por bueno: fue exactamente el error que se colo al usuario.
    """
    nombres: set[str] = set()

    for t in textos:
        # Metodos, propiedades y campos declarados como miembros de la clase.
        for m in re.finditer(
            r"^    (?:\[[^\]]*\]\s*)?"
            r"(?:public|private|protected|internal)\s+"
            r"(?:static\s+|readonly\s+|const\s+|required\s+|virtual\s+|override\s+|"
            r"sealed\s+|abstract\s+|partial\s+|volatile\s+|new\s+|extern\s+|unsafe\s+|"
            r"async\s+)*"
            r"[\w<>,?\[\]\.\(\) ]+?\s+(\w+)\s*(?:=|;|\{|=>|\()",
            t, re.M,
        ):
            nombres.add(m.group(1))

        # Campos sin modificador de acceso, a nivel de clase.
        for m in re.finditer(
            r"^    (?:readonly|static|const)\s+[\w<>,?\[\]\.\(\) ]+?\s+(\w+)", t, re.M
        ):
            nombres.add(m.group(1))

        # Campos por convencion de nombre: un campo cuyo tipo lleva parentesis,
        # como List<(double X, double Y, double R)> _varSup, no lo pilla el patron.
        for m in re.finditer(r"\b(_\w+)\b", t):
            nombres.add(m.group(1))

    return nombres


def v15_cs0103() -> None:
    """Argumentos que no existen en el contexto (CS0103)."""
    print("\n[15] Argumentos que no existen en el contexto (CS0103)")

    rutas = archivos(".cs", "client/src")
    textos = [leer(p) for p in rutas]
    miembros = _miembros_del_proyecto(textos)

    # Los controles con x:Name son CAMPOS de la clase, pero los declara el codigo
    # que genera WPF a partir del XAML y ese archivo no esta en el repositorio. Sin
    # recogerlos de aqui, cada 'PlantaCanvas' o 'ExtruidaCanvas' saldria como
    # inexistente y la comprobacion seria inservible.
    for x in archivos(".xaml", "client/src"):
        for m in re.finditer(r'x:Name="(\w+)"', leer(x)):
            miembros.add(m.group(1))

    # Lo demas que genera o hereda WPF y no aparece declarado en el codigo.
    miembros.update({"InitializeComponent", "Show", "ShowDialog", "Close", "Focus"})

    problemas: list[str] = []
    metodos_revisados = 0
    args_revisados = 0

    for p, texto in zip(rutas, textos):
        for m in re.finditer(
            r"^    (?:public|private|protected|internal)[^\n;=]*?"
            r"\b(\w+)\s*\(([^)]*)\)\s*\r?\n?\s*\{",
            texto, re.M,
        ):
            # Se usa un buscador de LLAVES. Antes aqui se reutilizaba el de
            # parentesis y devolvia None siempre, asi que la comprobacion pasaba
            # sobre cero metodos. Lo delato el contador de cobertura.
            cuerpo = _bloque_llaves(texto, m.end() - 1)
            if cuerpo is None:
                continue

            metodos_revisados += 1

            declarados = _nombres_declarados(cuerpo, m.group(2))
            sin_com = cuerpo   # ya viene sin comentarios ni cadenas

            # Identificadores en posicion de argumento suelto
            for llamada in re.finditer(r"\b(\w+)\s*\(([^()]*)\)", sin_com):
                if llamada.group(1) in _NO_ES_LLAMADA:
                    continue

                for arg in llamada.group(2).split(","):
                    a = arg.strip()

                    # En minuscula se admite tambien 'nombre.Miembro', mirando la
                    # RAIZ: asi se caza un parametro mal escrito, como 'seccion.Id'
                    # donde el parametro se llama 's'.
                    #
                    # En mayuscula solo el nombre SUELTO, que es como se pasa un
                    # metodo o una constante. Con punto seria un tipo
                    # ('MessageBoxButton.OK'), y exigir que los tipos esten
                    # declarados aqui daria falsos positivos sin parar.
                    raiz = (re.fullmatch(r"([a-z_]\w*)(?:\.\w+)*", a)
                            or re.fullmatch(r"([A-Z]\w*)", a))

                    if raiz is None:
                        continue

                    nombre = raiz.group(1)
                    args_revisados += 1

                    if nombre in _PALABRAS_LIBRES or nombre in miembros:
                        continue

                    pos = declarados.get(nombre)

                    if pos is None:
                        problemas.append(
                            f"{rel(p)}: '{nombre}' se pasa a {llamada.group(1)}(...) "
                            f"dentro de {m.group(1)}(...) y no esta declarado ahi"
                        )
                    elif pos > llamada.start(2):
                        # Existe, pero MAS ABAJO. Es el error real que se colo:
                        # usar una variable antes de declararla.
                        problemas.append(
                            f"{rel(p)}: '{nombre}' se pasa a {llamada.group(1)}(...) "
                            f"dentro de {m.group(1)}(...) pero se declara DESPUES"
                        )

    check(
        "los argumentos existen en el contexto (CS0103)",
        not problemas,
        "; ".join(sorted(set(problemas))[:4]),
    )

    # La cobertura a la vista: si las expresiones regulares dejaran de encontrar
    # metodos, la comprobacion pasaria en vacio y no habria como notarlo.
    print(f"        cobertura: {metodos_revisados} metodos, "
          f"{args_revisados} argumentos revisados")
    check("la revision de contexto cubre codigo de verdad",
          metodos_revisados >= 100 and args_revisados >= 200,
          f"{metodos_revisados} metodos, {args_revisados} argumentos")




# ======================================================================
#  17. Guardar el trabajo, f'c por elemento, borrado y solapa
# ======================================================================
def v17_guardar_y_defaults() -> None:
    """Archivo .clk, f'c por tipo de elemento, borrado de planos y campos viejos."""
    print("\n[17] Trabajo .clk, f'c por elemento, borrado")

    filas = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))
    proy = leer(ruta("client/src/CadLink.App/Models/Proyecto.cs"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    diamante = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Diamante.cs"))

    # ------------------------------------------------------------------
    # CS0117: un miembro que no existe en el inicializador de objeto
    # ------------------------------------------------------------------
    # Esta comprobacion existe porque la de CS0103 NO cubre esto: solo mira los
    # ARGUMENTOS de las llamadas, no los nombres de miembro de un inicializador.
    # Al escribir el guardado supuse los nombres de la fila de secciones y salieron
    # nueve distintos ('Separacion' por 'SeparacionCm', 'NSupEsquina' por 'NEsqSup'...).
    # Son nueve errores de compilacion que habrian llegado a la maquina del usuario.
    def propiedades_de(texto: str, clase: str) -> set[str]:
        i = texto.find(f"class {clase}")
        if i < 0:
            return set()
        j = texto.find("class ", i + 10)
        if j < 0:
            j = len(texto)
        return set(re.findall(r"public (?:double|string|int|bool|DateTime)\s+(\w+)",
                              texto[i:j]))

    fila = propiedades_de(filas, "SeccionConcretoRow")
    guardada = propiedades_de(proy, "SeccionGuardada")

    check("se leyeron las propiedades de la fila de secciones", len(fila) >= 15,
          f"solo {len(fila)}")
    check("y las de la seccion guardada", len(guardada) >= 15, f"solo {len(guardada)}")

    if fila and guardada:
        sobran = guardada - fila
        check("lo que se guarda existe en la fila", not sobran,
              "no estan en la fila: " + ", ".join(sorted(sobran)))

    # Y que los inicializadores usen solo nombres que existen.
    for clase, propias in (("SeccionConcretoRow", fila), ("SeccionGuardada", guardada)):
        m = re.search(r"new " + clase + r"\s*\{(.*?)\n\s*\}\);", codigo, re.S)
        check(f"se puede leer el inicializador de {clase}", m is not None)

        if m and propias:
            usados = set(re.findall(r"(\w+) = ", m.group(1)))
            malos = usados - propias
            check(f"el inicializador de {clase} solo usa miembros que existen",
                  not malos, "no existen: " + ", ".join(sorted(malos)))

    # ------------------------------------------------------------------
    # f'c por tipo de elemento
    # ------------------------------------------------------------------
    check("hay f'c por omision segun el elemento",
          "public static string FcPorOmision(" in filas)
    check("castillos y cadenas van a 200",
          'EsDeConfinamiento(elemento) ? "200" : "250"' in filas)
    check("se reconocen castillo Y cadena",
          'StartsWith("CASTILLO"' in filas and 'StartsWith("CADENA"' in filas)
    # Y se aplica al CAMBIAR el elemento, no solo al crear la fila.
    m_el = re.search(r"public string Elemento\s*\{.*?\n    \}", filas, re.S)
    check("se puede leer el Elemento", m_el is not None)
    if m_el:
        check("cambiar el elemento reajusta el f'c",
              "AplicarFcPorOmision();" in m_el.group(0))

    # Pero se puede cambiar a mano, y entonces ya no se toca.
    check("escribir el f'c lo deja fijo", "_fcManual = true;" in filas)
    m_fc = re.search(r"private void AplicarFcPorOmision\(\).*?\n    \}", filas, re.S)
    check("se puede leer AplicarFcPorOmision", m_fc is not None)
    if m_fc:
        check("el automatico respeta lo escrito a mano",
              "if (_fcManual)" in m_fc.group(0))

    # ------------------------------------------------------------------
    # Archivo .clk
    # ------------------------------------------------------------------
    check("la extension es .clk", 'Extension = ".clk"' in proy)
    check("el formato lleva version desde el principio",
          "public int Version { get; set; } = 1;" in proy)
    # Se escribe a un temporal y se cambia: si se escribiera encima y fallara a
    # medias, se perderian el trabajo viejo Y el nuevo.
    m_g = re.search(r"public static void Guardar\(.*?\n    \}", proy, re.S)
    check("se puede leer Guardar", m_g is not None)
    if m_g:
        cuerpo = m_g.group(0)
        check("se guarda primero en un temporal", 'ruta + ".tmp"' in cuerpo)
        check("y se cambia por el bueno al final", "File.Move(temporal, ruta)" in cuerpo)
    # Un archivo de version mas nueva se RECHAZA, no se lee a medias.
    check("se rechaza un archivo de version mas nueva", "p.Version > 1" in proy)
    check("los acentos no se escapan", "UnsafeRelaxedJsonEscaping" in proy)

    # Guardar, guardar como y abrir se disparan desde TRES sitios: el boton de la
    # barra de arriba, el menu Archivo y el teclado. Por eso van por
    # ApplicationCommands y no por Click: asi los tres comparten una sola ruta de
    # codigo y no puede pasar que el boton guarde y el atajo no. Lo que se comprueba
    # es que las tres acciones esten CABLEADAS, no como estan cableadas.
    check("hay acciones de guardar, guardar como y abrir",
          'Executed="OnGuardarTrabajo"' in xaml
          and 'Executed="OnGuardarComo"' in xaml
          and 'Executed="OnAbrirTrabajo"' in xaml)

    for cmd in ("ApplicationCommands.Save", "ApplicationCommands.SaveAs",
                "ApplicationCommands.Open"):
        # Dos usos por comando como minimo: el CommandBinding que lo atiende y algo
        # que lo invoque. Con uno solo habria un comando atendido que nadie dispara.
        check(f"algo invoca {cmd}", xaml.count(cmd) >= 2,
              f"aparece {xaml.count(cmd)} vez/veces")

    # Los atajos prometidos en los ToolTip tienen que existir de verdad. Antes los
    # ToolTip decian Ctrl+G y Ctrl+A y no habia ni un KeyBinding en todo el proyecto.
    for tecla, mod, cmd in (("G", "Control", "Save"),
                            ("A", "Control", "Open"),
                            ("G", "Control+Shift", "SaveAs")):
        check(f"el atajo Ctrl{'+Mayus' if 'Shift' in mod else ''}+{tecla} existe",
              re.search(
                  rf'<KeyBinding\s+Key="{tecla}"\s+Modifiers="{re.escape(mod)}"\s+'
                  rf'Command="ApplicationCommands\.{cmd}"\s*/>', xaml) is not None)

    # ----------------------------------------------------------------
    # Y ARRIBA, como en cualquier programa de Windows
    # ----------------------------------------------------------------
    # Esto es el fondo del asunto y por eso se comprueba por POSICION y no por que
    # exista un boton: guardar vivia dentro de la hoja «Proyecto», al final de un
    # ScrollViewer, asi que para guardar habia que cambiar de pestaña y bajar.
    m_menu = re.search(r"<Menu\b", xaml)
    check("hay una barra de menu", m_menu is not None)

    m_tabs = re.search(r'<TabControl x:Name="Sheets"', xaml)
    check("se puede localizar el TabControl de las hojas", m_tabs is not None)

    if m_menu and m_tabs:
        check("el menu va ANTES de las hojas", m_menu.start() < m_tabs.start())

    check("el menu lleva Archivo, Dibujar y Ayuda",
          "_Archivo" in xaml and "_Dibujar" in xaml and "A_yuda" in xaml)

    # La barra de guardar tiene que estar FUERA de cualquier pestaña, o vuelve a
    # depender de en que hoja este el usuario.
    m_barra = re.search(r'Command="ApplicationCommands\.Save"', xaml)
    if m_barra and m_tabs:
        check("la barra de guardar esta fuera de las pestañas",
              m_barra.start() < m_tabs.start())

    # El nombre del archivo abierto se lee en la barra, no enterrado en una hoja.
    m_arch = re.search(r'x:Name="ArchivoText"', xaml)
    check("el nombre del archivo se ve en la barra de arriba",
          m_arch is not None and m_tabs is not None and m_arch.start() < m_tabs.start())

    # Y ya NO debe quedar el juego viejo de botones dentro de la hoja Proyecto.
    check("los botones de guardar ya no estan dentro de la hoja Proyecto",
          'Click="OnGuardarTrabajo"' not in xaml
          and 'Click="OnGuardarComo"' not in xaml
          and 'Click="OnAbrirTrabajo"' not in xaml)

    # Nuevo suelta la ruta del archivo. Si no lo hiciera, el primer Ctrl+G del
    # trabajo nuevo sobreescribiria en silencio el .clk anterior.
    m_nuevo = re.search(r"private void OnNuevoTrabajo\(.*?\n    \}", codigo, re.S)
    check("se puede leer OnNuevoTrabajo", m_nuevo is not None)
    if m_nuevo:
        check("Nuevo olvida la ruta del archivo anterior",
              "_archivoActual = string.Empty;" in m_nuevo.group(0))
        check("y pregunta antes de borrar lo no guardado",
              "MessageBoxResult.Yes" in m_nuevo.group(0))

    check("Guardar reutiliza el archivo abierto", "_archivoActual" in codigo)

    m_ap = re.search(r"private void AplicarProyecto\(.*?\n    \}", codigo, re.S)
    check("se puede leer AplicarProyecto", m_ap is not None)
    if m_ap:
        # Cargar cien secciones no debe redibujar cien veces.
        check("se apaga el redibujado mientras se carga",
              "_listo = false;" in m_ap.group(0) and "_listo = estaba;" in m_ap.group(0))

    check("hay comprobacion del ida y vuelta del .clk",
          os.path.exists(ruta("tools/verificar_clk.py")))

    # ------------------------------------------------------------------
    # Piers: el arreglo de argumentos se arma con la FIRMA del metodo
    # ------------------------------------------------------------------
    # GetSectionProperties declara diecisiete parametros ByRef y aqui se le
    # pasaban once: de ahi el aviso repetido "no se pudieron leer las medidas del
    # pier" en TODOS los piers, con las etiquetas si leidas. El numero cambia entre
    # versiones, asi que no vale escribirlo a mano.
    com = leer(ruta("client/src/CadLink.Etabs/ComLateBinding.cs"))
    piers = leer(ruta("client/src/CadLink.Etabs/EtabsPiers.cs"))

    check("se puede llamar armando el arreglo por la firma",
          "public static object?[]? CallConFirma(" in com)
    check("los piers lo usan",
          'Com.CallConFirma(pierLabel, "GetSectionProperties", (0, nombre))' in piers)
    check("y ya no se arma un arreglo de tamaño fijo",
          'Com.Call(pierLabel, "GetSectionProperties"' not in piers)

    m_cf = re.search(r"public static object\?\[\]\? CallConFirma\(.*?\n    \}", com, re.S)
    check("se puede leer CallConFirma", m_cf is not None)
    if m_cf:
        cuerpo = m_cf.group(0)
        check("el arreglo se dimensiona con la firma",
              "new object?[ps.Length]" in cuerpo)
        # Los arreglos ByRef van a null (la OAPI los crea) y los numeros a cero: un
        # null en un 'ref int' hace fallar la invocacion.
        check("cada parametro arranca con un valor de su tipo",
              "ValorNeutro(ps[i])" in cuerpo)

    # ------------------------------------------------------------------
    # Borrado de planos y campos viejos fuera
    # ------------------------------------------------------------------
    m_pg = re.search(r'<DataGrid x:Name="PlanosGrid".*?>', xaml, re.S)
    check("se puede leer la reja de planos", m_pg is not None)
    if m_pg:
        check("se puede borrar un plano", 'CanUserDeleteRows="True"' in m_pg.group(0))

    # Los campos que el usuario pidio quitar de Datos del proyecto.
    for campo in ("ExcelPathBox", "NormaCombo", "ProyectoBox", "ClienteBox"):
        check(f"ya no esta la casilla {campo}", f'x:Name="{campo}"' not in xaml)

    check("y el codigo ya no la busca", "ExcelPathBox" not in codigo)

    # ------------------------------------------------------------------
    # El importador de Excel se RETIRO por completo
    # ------------------------------------------------------------------
    # Ofrecia un boton en la barra, otro en la hoja Proyecto y una entrada de menu, y
    # las tres terminaban en el mismo aviso de «no esta implementado». Un boton que
    # solo sirve para decir que no funciona hace dudar de si el problema es del
    # programa o de la hoja de calculo.
    for muerto in ("OnImportExcel", "OnBrowseExcel", "_rutaExcel"):
        check(f"{muerto} se retiro del codigo", muerto not in codigo)
        check(f"y del XAML ({muerto})", muerto not in xaml)

    # Pero queda escrito QUE haria falta para portarlo de verdad.
    check("queda apuntado como portar el importador",
          "docs/macro-secciones-concreto.md" in codigo)

    # ------------------------------------------------------------------
    # Diamante: doblez sobre las DOS mas juntas si no hay una en el eje
    # ------------------------------------------------------------------
    # El doblez vive en TrazoDiamante, con el resto de la geometria del rombo.
    trazo_d = leer(ruta("client/src/CadLink.Cad/TrazoDiamante.cs"))

    check("el doblez lateral admite dos varillas",
          "private static List<(double X, double Y, double R)> DoblezLateral(" in trazo_d)
    check("se mide sobre la Y en los costados", "porY: true" in trazo_d)
    check("VarillasDelCentro sabe medir por Y", "bool porY = false" in trazo_d)
    # Los dos costados se agregan con AddRange: si uno usara Add, una seleccion de
    # dos varillas no cabria y la lista quedaria mal.
    n_ar = len(re.findall(r"centros\.AddRange\(\n            DoblezLateral\(", trazo_d))
    check("los dos dobleces se agregan con AddRange", n_ar == 2, f"son {n_ar}")




# ======================================================================
#  [21] Separacion con lista, y el modulo de secciones de ACERO
# ======================================================================
def v21_separacion_y_acero() -> None:
    print("\n[21] Separacion de estribos con lista, y secciones de acero")

    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    filas = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))
    perfil_row = leer(ruta("client/src/CadLink.App/Models/PerfilAceroRow.cs"))
    codigo = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    acero_cb = leer(ruta("client/src/CadLink.App/MainWindow.Acero.cs"))
    acero_cad = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.Acero.cs"))
    perfil_cad = leer(ruta("client/src/CadLink.Cad/PerfilAceroCad.cs"))

    # ------------------------------------------------------------------
    # La separacion de estribos, con sus valores de siempre
    # ------------------------------------------------------------------
    check("las separaciones usuales viven en un solo sitio",
          "public static readonly string[] SeparacionesUsuales" in filas)

    # LO QUE SE PIDIO: la lista corta. Solo las cinco de tres tramos que se repiten en
    # casi todos los planos, mas las dos unicas de 15 y 20 cm que se usan en parrillas y
    # mallas de zapata. Las demas se teclean a mano, que es lo que la celda permite.
    # 5-10-5 se agrego porque el usuario la usa y la pidio. Encaja en el orden documentado de
    # la lista, que va de la mas cerrada a la mas abierta.
    for sep in ("5-10-5", "6-12-6", "7-14-7", "8-16-8", "9-18-9", "10-20-10", "15", "20"):
        check(f"esta la separacion {sep}", f'"{sep}"' in filas)

    m_seps = re.search(
        r"public static readonly string\[\] SeparacionesUsuales\s*=\s*\{(.*?)\};",
        filas, re.S)

    check("la lista de separaciones no trae nada mas",
          m_seps is not None
          and sorted(re.findall(r'"([^"]+)"', m_seps.group(1)))
          == sorted(["5-10-5", "6-12-6", "7-14-7", "8-16-8", "9-18-9",
                     "10-20-10", "15", "20"]))

    # Las que se quitaron. Se comprueba que NO esten en la lista, no que no esten en el
    # archivo: «10-15-20» sigue apareciendo en los comentarios como ejemplo del formato de
    # varios tramos, y eso esta bien.
    for sep in ("7-14-4", "10-15-20", "5-10-15", "10-20", "30"):
        check(f"y ya no ofrece {sep}, que se teclea a mano",
              m_seps is not None and f'"{sep}"' not in m_seps.group(1))

    check("y queda escrito que la lista se dejo corta a proposito",
          "La lista se dejó corta a propósito" in filas
          and "se teclea a mano en la celda" in filas)

    # La misma lista alimenta las dos hojas: la de concreto y la de zapatas. Es un solo
    # static, asi que recortarlo las recorta las dos, que es lo que se pidio.
    check("la lista es la misma en las dos hojas",
          xaml.count(
              'ItemsSource="{Binding Source={x:Static '
              'models:SeccionConcretoRow.SeparacionesUsuales}}"') >= 2)

    # La celda es un combo EDITABLE enlazado por Text. Con SelectedItemBinding, que es
    # lo que usan las demas columnas de lista, el texto que se teclea a mano no llega a
    # la propiedad y se perderia al salir de la celda.
    check("la celda de separacion es un combo editable",
          'ItemsSource="{Binding Source={x:Static models:SeccionConcretoRow.SeparacionesUsuales}}"'
          in xaml
          and 'IsEditable="True"' in xaml)
    check("y se enlaza por Text, no por SelectedItem",
          'Text="{Binding SeparacionCm, UpdateSourceTrigger=PropertyChanged}"' in xaml)
    check("ya no es una columna de texto pelada",
          'DataGridTextColumn Header="Sep cm"' not in xaml)
    check("el XAML declara el espacio de nombres de los modelos",
          'xmlns:models="clr-namespace:CadLink.App.Models"' in xaml)

    # ------------------------------------------------------------------
    # Las DOCE familias de perfil y las NUEVE formas
    # ------------------------------------------------------------------
    # Familia y forma son dos cosas distintas, y separarlas es lo que arregla el
    # desplegable: antes IS, IC y S se metian dentro de IR «porque son perfiles I», y la
    # lista de la IR ofrecia 573 perfiles de cuatro nomenclaturas revueltas.
    forma_cad = leer(ruta("client/src/CadLink.Cad/FormaAcero.cs"))

    DOCE = ("IR", "IS", "IC", "S", "WT", "C", "CF", "ZF", "L", "OR", "OC", "OS")

    for fam in DOCE:
        check(f"existe la familia {fam}", f'= "{fam}";' in perfil_row)

    check("las doce estan en la lista del desplegable",
          "public static readonly string[] Todas" in perfil_row
          and all(f in perfil_row.split("Todas =")[1].split(";")[0]
                  for f in ("Ir", "Is", "Ic", "Wt", "Cf", "Zf", "Or", "Oc", "Os")))

    # La forma vive en el proyecto de DIBUJO, no en el de la interfaz: es vocabulario
    # del dibujante. Y las constantes de la interfaz son ALIAS de las suyas, no copias,
    # asi que el compilador garantiza que las dos listas dicen lo mismo.
    check("las nueve formas viven en el dibujante",
          "public static class FormaAcero" in forma_cad)

    for f in ("I", "Te", "Angulo", "Canal", "CanalConLabios", "Zeta",
              "TuboRectangular", "TuboRedondo", "RedondoMacizo"):
        check(f"existe la forma {f}", f"public const string {f} = " in forma_cad)

    check("la interfaz usa ALIAS de esas formas, no copias de las cadenas",
          "public const string I = FormaAcero.I;" in perfil_row
          and "public const string Zeta = FormaAcero.Zeta;" in perfil_row)

    check("cada familia sabe con que forma se dibuja",
          "public static string DeLaFamilia(string? familia)" in perfil_row)

    check("las cuatro familias de perfil I comparten la forma I",
          "FamiliaPerfil.Ir or FamiliaPerfil.Is or FamiliaPerfil.Ic or FamiliaPerfil.S => I"
          in perfil_row)

    check("y la forma se ve en la cuadricula, para que se note que la comparten",
          "public string FormaNombre" in perfil_row
          and 'Binding="{Binding FormaNombre}"' in xaml)

    # ------------------------------------------------------------------
    # UNA SOLA CAPA y el rayado de cada macro: NO hay color por familia
    # ------------------------------------------------------------------
    # Se probo a darle una capa y un color a cada una de las doce familias, y se quito:
    # el plano dejaba de parecerse al que ya se venia haciendo. Las cuatro familias
    # portadas tienen cada una su propio rayado, y eso es lo que las distingue.
    check("no queda ninguna tabla de color por familia",
          "ColorAcero" not in forma_cad and "ColorAcero" not in acero_cad)

    check("ni capas por familia: una sola PERFILES, la de las macros",
          'CapaPerfiles = "PERFILES"' in acero_cad
          and "Capa(CapaPerfiles, 7);" in acero_cad
          and 'CapaBase + "-"' not in acero_cad)

    check("y los objetos van por capa, no con el color pegado",
          "c.Color = PorCapa;" in acero_cad)

    # EL RAYADO DE CADA FORMA, patron por patron y color por color, es el de su macro.
    m_rayar = re.search(r"private void RayarPerfil\(.*?\n    \}", acero_cad, re.S)

    check("el rayado se decide en un solo sitio, por forma", m_rayar is not None)

    if m_rayar:
        rayar = m_rayar.group(0)

        # Los pares (patron, color) de las cuatro macros, uno por uno.
        for patron, color, de_quien in (
                ("ANSI32", 252, "el IR"),
                ("SOLID", 4, "el CF"), ("ANSI31", 142, "el CF"),
                ("SOLID", 162, "el OC"), ("ANSI31", 162, "el OC"),
                ("SOLID", 141, "el HSS grande")):
            check(f"esta el rayado {patron} en {color}, de la macro de {de_quien}",
                  f'"{patron}", ' in rayar and f"CapaPerfiles, {color})" in rayar)

        # Las escalas, tal cual. Un rayado con separacion FIJA da la misma densidad en el
        # papel para cualquier tamaño de perfil, que es lo que tiene que hacer.
        for escala in ("0.0009", "0.0008", "0.002"):
            check(f"esta la escala de rayado {escala} de su macro",
                  f"{escala} * _f" in rayar)

        check("y la del tubo cambia a las 5 pulgadas, como su macro",
              "(menorDe5 ? 0.001 : 0.002) * _f" in rayar)

        # Las cinco formas nuevas van agrupadas con la macro de su material: la te, la
        # canal laminada y el angulo con el IR; la zeta con el CF; el macizo con el OC.
        check("la te, la canal laminada y el angulo se rayan como el IR",
              re.search(r"case FormaAcero\.I:\s*case FormaAcero\.Te:\s*"
                        r"case FormaAcero\.Canal:\s*case FormaAcero\.Angulo:", rayar)
              is not None)
        check("la zeta se raya como el CF",
              re.search(r"case FormaAcero\.CanalConLabios:\s*case FormaAcero\.Zeta:",
                        rayar) is not None)
        check("y el redondo macizo como el tubo redondo",
              re.search(r"case FormaAcero\.TuboRedondo:\s*"
                        r"case FormaAcero\.RedondoMacizo:", rayar) is not None)

    # EL PEDIT: de las cuatro macros, solo la del IR engruesa el contorno.
    m_pedit = re.search(r"private void PeditDeLaForma\(.*?\n    \}", acero_cad, re.S)

    check("el PEDIT del contorno se decide en un solo sitio", m_pedit is not None)

    if m_pedit:
        pedit = m_pedit.group(0)

        check("lo llevan las cuatro formas laminadas, como la macro del IR",
              all(f"FormaAcero.{f}" in pedit
                  for f in ("I", "Te", "Canal", "Angulo")))
        check("y NO lo llevan el tubo, el redondo ni las formadas en frio",
              not any(f"FormaAcero.{f}" in pedit
                      for f in ("TuboRectangular", "TuboRedondo", "RedondoMacizo",
                                "CanalConLabios", "Zeta")))

    check("el ancho constante se pide solo desde ahi",
          acero_cad.count("AnchoConstante(pl,") == 1)

    # La familia se ajusta sola cuando el nombre del perfil la delata: un HSS dibujado
    # como IR sale como un perfil I con las medidas de un tubo, y eso no se ve venir.
    check("la familia se deduce del nombre del perfil",
          "public static string? DelNombre(string? perfil)" in perfil_row)
    check("y se aplica al escribir el perfil",
          "var familia = FamiliaPerfil.DelNombre(_perfil);" in perfil_row)

    # La traduccion a nomenclatura mexicana que hacen las macros al rotular.
    check("W se rotula IR", '"IR" + s.Substring(1)' in perfil_row)
    check("HSS se rotula OR", 'Reemplazar(s, "HSS", "OR")' in perfil_row)
    check("PIPE se rotula OC", 'Reemplazar(s, "PIPE", "OC")' in perfil_row)
    check("y el # del calibre se rotula CAL", 's.Replace("#", "CAL ")' in perfil_row)

    # Cada familia pide unas dimensiones y no otras.
    check("hay una columna calculada que dice que falta",
          "public string FaltanDatos" in perfil_row)
    # Y lo pide la FORMA, no la familia: las cuatro familias de perfil I piden las
    # mismas cuatro medidas, asi que escribirlo por familia seria escribirlo cuatro
    # veces y arriesgarse a que una se quede distinta.
    check("lo que falta lo decide la forma, no la familia",
          "var forma = Forma;" in perfil_row)
    check("los redondos no piden ancho, que no lo tienen",
          "if (!esRedondo && _anchoCm <= 0)" in perfil_row)
    check("el macizo tampoco pide espesor: es una barra llena",
          "if (forma != FormaPerfil.RedondoMacizo && _espesorAlmaCm <= 0)" in perfil_row)
    check("el labio solo lo pide la canal con labios",
          "forma == FormaPerfil.CanalConLabios && _labioCm <= 0" in perfil_row)
    check("y el angulo pide sus dos alas con ese nombre",
          '"ala larga"' in perfil_row and '"ala corta"' in perfil_row)

    # El patin angosto de la zeta: la novena medida, que solo usa una familia.
    check("la zeta tiene su patin angosto",
          "public double AnchoMenorCm" in perfil_row)
    check("no puede pasar del ancho, porque entonces no es el angosto",
          "FormaPerfil.Zeta when _anchoMenorCm > _anchoCm" in perfil_row)
    check("y en cero la zeta sale simetrica",
          "AnchoMenorCm > 0 && AnchoMenorCm <= AnchoCm ? AnchoMenorCm : AnchoCm"
          in perfil_cad)
    check("su columna esta en la cuadricula",
          'Header="Ancho 2 cm"' in xaml)

    # ------------------------------------------------------------------
    # El desplegable de elemento y la columna de clasificacion
    # ------------------------------------------------------------------
    for elem in ("VIGA", "COLUMNA", "TENSOR", "PUNTAL", "LARGUERO", "ATIESADOR",
                 "MONTEN", "DIAGONAL"):
        check(f"el elemento {elem} esta en el desplegable", f'"{elem}"' in perfil_row)

    # La clasificacion se pega a CUALQUIER elemento, no solo a la viga. La macro solo lo
    # hacia con la VIGA, y el resultado era que el usuario elegia la clasificacion, la
    # veia en su celda y luego no aparecia en el dibujo sin que nada le dijera por que.
    check("la clasificacion se pega a cualquier elemento",
          "if (clasif.Length > 0 && elem.Length > 0)" in perfil_row)
    check("y ya no se comprueba que el elemento sea VIGA para pegarla",
          "elem == ElementoViga && clasif.Length > 0" not in perfil_row)
    check("la columna se llama Clasificación, no «Clasif. viga»",
          'Header="Clasificación"' in xaml and "Clasif. viga" not in xaml)

    # ------------------------------------------------------------------
    # La pestaña
    # ------------------------------------------------------------------
    # Se mira DENTRO de su TabItem, no en todo el XAML: las demas pestañas por portar
    # siguen llevando su aviso de pendiente, y eso esta bien.
    m_tab = re.search(
        r'<TabItem Header="Secciones Acero">.*?</TabItem>', xaml, re.S)

    check("se puede leer la pestaña de acero", m_tab is not None)

    if m_tab:
        tab = m_tab.group(0)

        check("la pestaña de acero ya no es un aviso de pendiente",
              "Modulo pendiente de portar" not in tab)
        check("tiene su cuadricula", 'x:Name="AceroGrid"' in tab)
        check("y su boton de dibujar", 'Click="OnExportAcero"' in tab)
        # De la tarjeta de ayuda queda UN solo renglon -el de las familias-, que es el que
        # de verdad hace falta: los otros dos explicaban lo que ya dice cada columna y se
        # comian el alto de la tabla. Lo que decian vive ahora en el globo del titulo.
        check("y dice que familias hay",
              'Text="Las doce familias:"' in tab
              and "OC: tubo redondo" in tab)
        check("la ayuda larga se queda en el globo del titulo, no encima de la tabla",
              "Que columna usa cada una" not in tab
              and "cada familia usa las columnas que necesita" in tab)

        # ---------------------------------------------------------------
        # Las columnas de propiedades geometricas
        # ---------------------------------------------------------------
        # Son las 16 que trae el manual. Van al FINAL y son de solo lectura: no se
        # capturan, salen del catalogo. Se comprueba una por una porque el que falte
        # una no se nota mirando la tabla -las 15 que quedan se ven bien- y es justo
        # la que hace falta para revisar un perfil.
        propiedades = (
            "PesoKgM", "AreaCm2", "IxCm4", "SxCm3", "ZxCm3", "RxCm",
            "IyCm4", "SyCm3", "ZyCm3", "RyCm", "RminCm", "JCm4", "CwCm6",
            "IxyCm4", "XbarCm", "YbarCm")

        for prop in propiedades:
            check(f"la tabla de acero trae la propiedad {prop}",
                  f"Binding Propiedades.{prop}," in tab)

        # Y todas de solo lectura: son un dato del manual, no algo que se teclee.
        col_props = re.findall(
            r"<DataGridTextColumn[^>]*Binding=\"\{Binding Propiedades\.[^}]*\}\""
            r"[^/]*/>", tab)

        check("las 16 columnas de propiedades estan en el XAML",
              len(col_props) == 16, f"{len(col_props)} columnas")
        check("y todas son de solo lectura",
              all('IsReadOnly="True"' in c for c in col_props))
        check("y se ven como celda calculada, no como celda que se captura",
              all("CeldaCalculada" in c for c in col_props))

        # ---------------------------------------------------------------
        # El acero: su Fy y si se hace en ese perfil
        # ---------------------------------------------------------------
        # La lista de aceros eran CINCO nombres escritos en el codigo, sin mas dato que su
        # nombre. Ahora salen del catalogo -39, con Fy, Fu y en que secciones se hace cada
        # uno- y la tabla dice las dos cosas que hacen falta: el Fy, que es con lo que se
        # revisa, y si ese acero se consigue en ese perfil.
        check("la tabla de acero trae el Fy del acero",
              'Binding="{Binding FyKgCm2, StringFormat=N0}"' in tab
              and 'Header="Fy kg/cm²"' in tab)
        check("y si el acero se hace en ese perfil",
              'Binding="{Binding AceroDisponibleLeyenda}"' in tab
              and "CeldaDisponibilidadAcero" in tab)
        check("y las dos son de solo lectura, que salen del catalogo",
              tab.count('Binding="{Binding FyKgCm2, StringFormat=N0}"\n'
                        '                                                IsReadOnly="True"')
              == 1)
        check("el globo de la celda del acero trae su detalle",
              "Value=\"{Binding AceroDetalle}\"" in tab)

        # ---------------------------------------------------------------
        # La vista previa de la forma
        # ---------------------------------------------------------------
        # Lo mismo que en concreto: la tabla dice numeros y el perfil se dibuja con
        # una FORMA, asi que un espesor mal capturado no se ve en la tabla y si en el
        # dibujo.
        check("la pestaña de acero tiene su vista previa",
              'x:Name="AceroPreviewCanvas"' in tab)
        check("y la vista previa recorta lo que se sale",
              re.search(r'x:Name="AceroPreviewCanvas".*?ClipToBounds="True"',
                        tab, re.S) is not None)
        check("y la tabla dejo su renglon para que quepa",
              'x:Name="AceroGrid" Grid.Row="1"' in tab
              and 'Grid.Row="2" Height="240"' in tab)

    # La fila se marca cuando el acero no se hace en ese perfil, que es lo que se pidio.
    tema = leer(ruta("client/src/CadLink.App/Theme/ExcelTabs.xaml"))

    check("la fila de acero se marca cuando el acero no se hace en ese perfil",
          'RowStyle="{StaticResource FilaAceroStyle}"' in xaml
          and '<Style x:Key="FilaAceroStyle" TargetType="DataGridRow"' in tema
          and 'Binding="{Binding AceroNoDisponible}" Value="True"' in tema)

    # Y el «verificar» NO se marca en rojo. Es la decision que importa de las tres
    # respuestas: pintar de rojo un acero que si se puede pedir hace cambiar de acero sin
    # necesidad, y darlo por bueno en silencio deja creyendo que ya se confirmo.
    check("el «verificar» va en ambar, no en rojo",
          'Binding="{Binding AceroPorVerificar}" Value="True"' in tema
          and "AceroVerificarBrush" in tema)
    check("y solo el «no se hace» pinta la fila",
          tema.count('Binding="{Binding AceroNoDisponible}" Value="True"') == 2)

    aceros_cs = leer(ruta("client/src/CadLink.App/Models/CatalogoAceros.cs"))

    check("hay un catalogo de aceros que se lee de un archivo",
          'public const string Archivo = "aceros.csv";' in aceros_cs
          and "public static List<AceroCatalogo> Leer(" in aceros_cs)
    check("y el archivo va suelto junto al ejecutable, como el de perfiles",
          "<None Update=\"aceros.csv\">" in leer(
              ruta("client/src/CadLink.App/CadLink.App.csproj")))
    check("el desplegable sale del catalogo, no de una lista escrita a mano",
          "public static string[] Aceros => CatalogoAceros.Nombres;" in perfil_row)

    # LAS TRES RESPUESTAS, y que la de «no se sabe» no es «no».
    check("la disponibilidad tiene tres respuestas",
          'public const string Si = "SI";' in aceros_cs
          and 'public const string Verificar = "VERIFICAR";' in aceros_cs
          and 'public const string No = "NO";' in aceros_cs)
    check("una familia que el catalogo no menciona contesta VERIFICAR, no NO",
          "? v : Verificar;" in aceros_cs)
    check("y un acero que no esta en el catalogo tampoco marca la fila",
          "?? AceroCatalogo.Verificar;" in perfil_row)

    # EL APOSTROFO DEL A-500. Es la unica diferencia entre dos aceros DISTINTOS: el
    # Gr. B es el tubo redondo, con Fy 2955, y el Gr. B' el rectangular, con 3235. Si la
    # comparacion lo tirara junto con los guiones y los espacios, el programa daria un Fy
    # equivocado en un 9 % sin decir nada.
    check("la busqueda de acero ignora guiones y espacios",
          "char.IsLetterOrDigit(c) || c == '\\''" in aceros_cs)
    check("pero NO el apostrofo, que distingue dos aceros",
          "el apóstrofo da un Fy equivocado en un 9 %" in aceros_cs
          or "perder el apóstrofo da un Fy equivocado" in aceros_cs)
    check("y una designacion vieja se guarda como la escribe el catalogo",
          "public static string ComoEnElCatalogo(" in aceros_cs
          and "Set(ref _acero, CatalogoAceros.ComoEnElCatalogo(value));" in perfil_row)

    # DOS sitios avisan de la disponibilidad, y hacen falta los dos: el acero y la
    # FAMILIA. Al cambiar de familia cambia la respuesta aunque el acero sea el mismo -un
    # A-36 se consigue en canal y no en monten-, y sin ese aviso la fila se quedaba con la
    # marca de la familia anterior, que es peor que no tener marca.
    check("cambiar de familia vuelve a preguntar por la disponibilidad",
          "private void RaiseDelAcero()" in perfil_row
          and perfil_row.count("RaiseDelAcero();") == 2,
          f"{perfil_row.count('RaiseDelAcero();')} llamadas")

    # El renglon de totales dice cuantas filas estan marcadas y de donde salio el catalogo.
    check("los totales dicen cuantas filas llevan un acero que no se hace",
          "con un acero que no se hace en ese perfil" in acero_cb)
    check("y de donde salio el catalogo de aceros",
          "CatalogoAceros.Origen" in acero_cb)

    # El generador y su comprobacion.
    gen_aceros = leer(ruta("tools/catalogo_aceros.py"))

    check("hay un generador del catalogo de aceros",
          "COLUMNA_DE_FAMILIA" in gen_aceros)
    check("que traduce las columnas de la hoja a las familias de CadLink",
          '("IR", "W")' in gen_aceros
          and '("OC", "PIPE")' in gen_aceros
          and '("OR", "HSS")' in gen_aceros)
    check("y lee los encabezados en lugar de suponer las letras",
          "def encabezados(filas)" in gen_aceros
          and "Se LEE, no se supone" in gen_aceros)
    check("y avisa de lo que la hoja trae raro, sin corregirlo",
          "def revisar(aceros)" in gen_aceros
          and "Se AVISA, no se corrige" in gen_aceros)

    check("hay comprobacion numerica del catalogo de aceros",
          "El CSV contra la hoja de la que sale"
          in leer(ruta("tools/verificar_catalogo_aceros.py")))
    check("que comprueba que el CSV esta al dia con la hoja",
          "y los mismos datos, acero por acero"
          in leer(ruta("tools/verificar_catalogo_aceros.py")))
    check("y que el ejemplo del programa no arranca con filas marcadas",
          "ninguna fila del ejemplo arranca marcada en rojo"
          in leer(ruta("tools/verificar_catalogo_aceros.py")))

    check("el catalogo de aceros esta generado y trae los 39",
          len([l for l in leer(ruta("client/src/CadLink.App/aceros.csv")).splitlines()
               if l.strip() and not l.startswith("#")]) == 39)
    check("y la hoja de la que sale esta en el repositorio",
          os.path.exists(ruta("docs/ACEROS.xlsx")))

    check("la vista previa de acero se engancha al arrancar",
          "private void EngancharVistaPreviaAcero()" in acero_cb
          and "EngancharVistaPreviaAcero();" in codigo)
    check("existe el dibujo de la vista previa de acero",
          "private void DibujarVistaPreviaAcero()" in acero_cb)
    check("se redibuja al cambiar de fila y al cambiar de tamaño",
          "AceroGrid.SelectionChanged += (_, _) => DibujarVistaPreviaAcero();" in acero_cb
          and "AceroPreviewCanvas.SizeChanged += (_, _) => DibujarVistaPreviaAcero();"
          in acero_cb)

    # EN TIEMPO REAL: al editar una celda se vuelve a dibujar, pero SOLO si la fila
    # editada es la que se esta viendo. Sin esa condicion, editar una fila de arriba
    # cambiaba el dibujo de la de abajo.
    m_edit = re.search(
        r"private void OnFilaAceroEditada\(.*?\n    \}", acero_cb, re.S)

    check("se puede leer OnFilaAceroEditada", m_edit is not None)

    if m_edit:
        edicion = m_edit.group(0)

        check("editar una celda redibuja la vista previa",
              "DibujarVistaPreviaAcero();" in edicion)
        check("y solo si la fila editada es la que se esta viendo",
              "ReferenceEquals(sender, AceroGrid.SelectedItem)" in edicion)

    # La geometria NO se calcula aqui: sale de TrazoAcero, que es el mismo calculo que
    # usa el dibujante de AutoCAD. Una vista previa con su propia cuenta puede acabar
    # enseñando algo distinto de lo que se dibuja, que es justo lo que no puede hacer.
    check("la vista previa usa la geometria del dibujante",
          "TrazoAcero.De(" in acero_cb and "TrazoAcero.Muestrear(" in acero_cb)
    check("y no se calcula ningun vertice a mano en la vista previa",
          "Math.Cos(" not in acero_cb and "Math.Tan(" not in acero_cb)

    # El hueco del tubo tiene que ser HUECO, no del color del fondo: si se pinta del
    # color del fondo, al cambiar el tema deja de ser hueco y se ve el relleno.
    check("el hueco del tubo es hueco de verdad",
          "new GeometryGroup { FillRule = FillRule.EvenOdd }" in acero_cb)

    # EL CS0104 QUE ROMPIO LA COMPILACION EN WINDOWS.
    #
    # La figura se creaba con «new Path», y este proyecto tiene System.IO como using
    # GLOBAL -esta en el .csproj- ademas de System.Windows.Shapes en el archivo. Los dos
    # definen un Path, asi que el nombre a secas es ambiguo y el proyecto NO compilaba.
    # Aqui no se ve, porque el analisis sintactico no resuelve tipos.
    check("la figura de la vista previa usa el alias, no «Path» a secas",
          "using FormaPath = System.Windows.Shapes.Path;" in acero_cb
          and "AceroPreviewCanvas.Children.Add(new FormaPath" in acero_cb
          and "new Path\n" not in acero_cb)
    check("y se dice por que hace falta el alias",
          "referencia ambigua" in acero_cb and "using GLOBAL" in acero_cb)

    # Y hay un script que lo caza, para no volver a enterarse al compilar en Windows.
    ambig = leer(ruta("tools/verificar_ambiguedades.py"))

    check("hay comprobacion de nombres ambiguos sin compilar",
          "Nombres ambiguos: los CS0104" in ambig)
    check("lee los using globales del csproj, no los supone",
          "def globales_del_csproj(" in ambig
          and "<Using\\s+Include=\"([^\"]+)\"" in ambig)
    check("y sabe que en WPF System.IO NO es implicito",
          "es_wpf" in ambig and "IMPLICITOS_BIBLIOTECA" in ambig)
    check("no confunde un comentario con un uso",
          "def sin_comentarios_ni_textos(" in ambig
          and "def sin_directivas_using(" in ambig)
    check("y el detector se prueba contra el error de verdad",
          "caza el «new Path» que rompio la compilacion" in ambig)

    # Y cuando no se puede dibujar se DICE por que, en vez de dejar el cuadro vacio.
    check("la vista previa avisa cuando no puede dibujar",
          "private void AvisoVistaAcero(string texto)" in acero_cb
          and "Selecciona un perfil de la tabla" in acero_cb
          and "No se puede dibujar todavía: falta" in acero_cb)

    for col in ("ColFamilia", "ColElementoAcero", "ColClasificacion", "ColAcero"):
        check(f"la columna {col} esta en el XAML", f'x:Name="{col}"' in xaml)
        check(f"y su lista se llena en el code-behind ({col})",
              f"{col}.ItemsSource" in acero_cb)

    # Las dos llamadas van DENTRO de las de concreto, no en el constructor: Enlazar se
    # vuelve a llamar al cargar el ejemplo, al borrar todo y al empezar de nuevo, y en
    # esos casos _datos es otro objeto.
    check("las listas de acero se llenan con las demas",
          "LlenarListasAcero();" in codigo)
    check("y la cuadricula se enlaza dentro de Enlazar",
          "EnlazarAcero();" in codigo)

    m_enlazar = re.search(r"private void Enlazar\(\).*?\n    \}", codigo, re.S)
    check("se puede leer Enlazar", m_enlazar is not None)
    if m_enlazar:
        check("EnlazarAcero se llama desde Enlazar",
              "EnlazarAcero();" in m_enlazar.group(0))

    check("la coleccion de acero vive en DatosProyecto",
          "ObservableCollection<PerfilAceroRow> SeccionesAcero" in filas)

    # El ejemplo trae UNA DE CADA FAMILIA, y entre las doce se dibujan las nueve formas:
    # asi se ve de una vez todo lo que la hoja sabe hacer, y sobre todo se ve lo que no se
    # nota mirando una fila sola, que es que la IR, la IS, la IC y la S se dibujan iguales.
    m_ejemplo = re.search(
        r"Secciones de acero, UNA DE CADA FAMILIA.*?return d;", filas, re.S)

    check("el ejemplo trae secciones de acero", m_ejemplo is not None)

    if m_ejemplo:
        ejemplo = m_ejemplo.group(0)

        for familia in ("Ir", "Is", "Ic", "S", "Wt", "C", "Cf", "Zf", "L", "Or",
                        "Oc", "Os"):
            check(f"el ejemplo trae una seccion de la familia {familia.upper()}",
                  f"FamiliaPerfil.{familia}," in ejemplo)

        # Y con los NOMBRES DEL MANUAL, no abreviados: si no, el perfil del ejemplo no
        # aparece marcado en el desplegable y parece escrito a mano.
        # El patron tiene que aceptar COMILLAS ESCAPADAS dentro de la cadena: los nombres
        # del IMCA llevan pulgadas -«CF - 6" x 2" x #14»- y en C# eso se escribe con \".
        # Con un [^"]* pelado, el nombre se cortaba en la primera pulgada y salia 'CF - 6\'.
        nombres_ejemplo = [
            m.group(1).replace('\\"', '"')
            for m in re.finditer(
                r'Acero\(FamiliaPerfil\.\w+, "((?:[^"\\]|\\.)*)"', ejemplo)]

        csv_ejemplo = leer(ruta("client/src/CadLink.App/perfiles-acero.csv"))

        fuera_del_catalogo = [n for n in nombres_ejemplo if n not in csv_ejemplo]

        check("los doce perfiles del ejemplo estan en el catalogo",
              len(nombres_ejemplo) == 12 and not fuera_del_catalogo,
              f"{len(nombres_ejemplo)} nombres, fuera: {fuera_del_catalogo}")

        # Los dos elementos nuevos se usan en el ejemplo, para que se vean sin buscarlos.
        check("el ejemplo usa MONTEN y DIAGONAL",
              '"MONTEN"' in ejemplo and '"DIAGONAL"' in ejemplo)

        # Y la clasificacion en un elemento que no es VIGA, que es lo que la macro no
        # dejaba hacer... aunque aqui va en vigas, asi que se comprueba lo otro: que la
        # zeta del ejemplo trae su patin angosto, que es la medida que solo ella usa.
        check("la zeta del ejemplo trae su patin angosto",
              "anchoMenor: 5.4" in ejemplo)

    # Antes de dibujar se revisa lo que NO se puede dibujar.
    check("la hoja de acero se revisa antes de dibujar",
          "private bool RevisarAcero(out List<string> problemas)" in acero_cb)
    check("se revisan los ID repetidos, que son el nombre del bloque",
          "está repetido" in acero_cb)

    # ------------------------------------------------------------------
    # El dibujante
    # ------------------------------------------------------------------
    check("el dibujante de acero es parte de SeccionDrawer",
          "public sealed partial class SeccionDrawer" in acero_cad)
    check("y por eso reusa el Hatch, la cota y el bloque del concreto",
          "Hatch(" in acero_cad and "FormatearCota(" in acero_cad
          and "Bloquear(p.Id, inicio, fin, destino);" in acero_cad)

    check("existe DibujarAcero", "public int DibujarAcero(" in acero_cad)
    check("se dibuja por FORMA, no por familia",
          all(f"case FormaAcero.{f}:" in acero_cad
              for f in ("I", "Te", "Canal", "CanalConLabios", "Zeta", "Angulo",
                        "TuboRectangular", "TuboRedondo", "RedondoMacizo")))
    check("una forma desconocida se avisa, no se dibuja mal",
          "no se reconoce" in acero_cad
          and "FormaAcero.Todas.Contains(p.Forma)" in acero_cad)

    # ------------------------------------------------------------------
    # LA GEOMETRIA VIVE APARTE, EN TrazoAcero
    # ------------------------------------------------------------------
    # Y no es orden por el orden: es lo que hace que la VISTA PREVIA de la pantalla y el
    # dibujo de AutoCAD salgan del MISMO calculo. Con los vertices dentro del dibujante, la
    # vista previa tendria que repetirlos, y una vista previa que calcula la forma por su
    # cuenta puede acabar enseñando algo distinto de lo que se dibuja.
    trazo = leer(ruta("client/src/CadLink.Cad/TrazoAcero.cs"))

    check("la geometria de los perfiles vive en TrazoAcero",
          "public static class TrazoAcero" in trazo
          and "public static Trazo? De(" in trazo)

    # Las siete formas poligonales, cada una con su funcion de vertices.
    for metodo in ("PerfilI", "PerfilTe", "PerfilCanal", "PerfilAngulo", "PerfilCf",
                   "PerfilZeta", "TuboRectangular"):
        check(f"TrazoAcero sabe hacer {metodo}",
              f"{metodo}(" in trazo and f"private static " in trazo)

    check("y las dos redondas salen como circunferencias",
          "CircExterior: new Circulo(" in trazo)

    check("el dibujante ya NO tiene vertices: se los pide a TrazoAcero",
          "TrazoAcero.De(p, x, yAbajo, _escala, espejo)" in acero_cad
          and "private void Trazar(TrazoAcero.Trazo trazo" in acero_cad)

    check("y no queda ninguna funcion de vertices en el dibujante",
          not any(f"private void {m}(" in acero_cad
                  for m in ("PerfilI", "PerfilTe", "PerfilCanal", "PerfilAngulo",
                            "PerfilCf", "PerfilZeta", "PerfilOr", "PerfilOc",
                            "PerfilOs")))

    # El hueco del tubo es una ISLA del rayado, no un agujero: en AutoCAD un hatch con isla
    # deja sin rellenar lo que la isla encierra, que es lo que hace que un tubo se vea tubo.
    check("el hueco del tubo entra como isla del rayado",
          "interior is null ? null : new List<object> { interior }" in acero_cad)

    # El DTO no interpreta nada: llega todo resuelto.
    check("el DTO lleva el ancho que ocupa el dibujo",
          "public double AnchoDibujoCm" in perfil_cad)
    check("y sabe que la zeta ocupa sus dos patines menos el alma",
          "FormaAcero.Zeta => AnchoCm + PatinAngostoCm - EspesorCm" in perfil_cad)

    # Las nueve formas se rayan por el mismo camino, que es el que decide el rayado de
    # cada una: nadie llama a Hatch por su cuenta con colores escritos a mano.
    check("las nueve formas se rayan por el mismo camino",
          acero_cad.count("RayarPerfil(") >= 3)

    # El corte de las cinco pulgadas es de la macro del HSS, y SOLO la afecta a ella.
    check("el corte de las 5 pulgadas es solo del tubo rectangular",
          "PeralteLimitePulg - 0.01" in acero_cad
          and acero_cad.count("menorDe5") >= 3)
    check("el tubo chico lleva el fondo cian de su macro",
          "FondoDelHatch(trama, 4);" in acero_cad)

    # ------------------------------------------------------------------
    # El aparato de la cota, PROPORCIONAL AL PERFIL
    # ------------------------------------------------------------------
    # El catalogo va de un redondo de 0.64 cm a una IS de 190. Con el aparato fijo que
    # venia del concreto -flecha de 2 cm- una cota sobre un angulo de 1.9 cm es mas
    # grande que el perfil y tapa lo que mide.
    check("el aparato de la cota se ajusta al tamaño del perfil",
          "private void PrepararAcero(PerfilAceroCad p)" in acero_cad
          and "var referencia = p.PeralteCm * _escala;" in acero_cad)

    for campo, divisor in (("_gapAcero", "5"), ("_flechaAcero", "15"),
                           ("_textoCotaAcero", "10"), ("_extOffsetAcero", "15"),
                           ("_extExtiendeAcero", "8")):
        check(f"{campo} sale del peralte entre {divisor}",
              f"{campo} = Acotar(referencia / {divisor}," in acero_cad)

    check("y esos valores se le ponen a cada cota por encima de los del concreto",
          'PropCota((object)cota, "ArrowheadSize", _flechaAcero);' in acero_cad
          and 'PropCota((object)cota, "TextHeight", _textoCotaAcero);' in acero_cad)

    # LA SEPARACION DEL RAYADO NO SE TOCA: es la fija de cada macro. Un patron de
    # sombreado con separacion fija da la MISMA densidad en el papel para cualquier
    # tamaño de perfil, que es justo lo que tiene que hacer; ligarlo al peralte deja los
    # grandes con el rayado abierto y los chicos con el rayado cerrado.
    check("el aparato de la cota no toca la separacion del rayado",
          "_escalaHatchAcero" not in acero_cad)

    check("hay comprobacion numerica del aparato de la cota",
          "El aparato de la cota, proporcional al perfil"
          in leer(ruta("tools/verificar_perfiles_acero.py")))

    # NINGUNA COTA PUEDE LLEVAR SU TEXTO POR DEBAJO DE LA BASE DEL PERFIL, porque ahi va
    # el rotulo: cuatro renglones centrados y de hasta un metro de ancho. Un numero ahi
    # acaba encima de su primer renglon, y a la escala de un plano las dos cosas se
    # confunden. Las cotas que lo necesitan llevan el texto DENTRO del hueco del perfil
    # -el de la canal, la escuadra del angulo, el lado libre de la zeta-, que esta vacio.
    debajo = [m for m in re.findall(r"\b(?:y0|yBase|cy) - gap\b", acero_cad)]

    check("ninguna cota de acero pone su texto debajo de la base, donde va el rotulo",
          not debajo, f"{len(debajo)} sitio(s)")

    check("y el rotulo si va debajo, separado con el mismo gap",
          "RotuloAcero(p, centro, yAbajo - _gapAcero);" in acero_cad)

    # El color de fondo de un hatch no es un numero, es un objeto que hay que pedir por
    # su ProgID con la version pegada.
    check("el fondo del hatch prueba varias versiones de AutoCAD",
          '"AutoCAD.AcCmColor." + v' in acero_cad)

    # Los radios del CF se recortan a lo que cabe, como en la macro.
    check("el radio exterior del CF se recorta",
          "Math.Min(ri, Math.Min(b / 2, Math.Min(lip, h / 2)))" in trazo)
    check("y el interior es la mitad, recortada por su cuenta",
          "Math.Min(ri / 2, rIntMax)" in trazo)

    # El peralte del OR es el lado mayor: un tubo capturado al reves es el mismo tubo. Y
    # ahora eso lo dice EL HUECO tambien, no solo el trazo: antes el trazo se volteaba y
    # el hueco no, asi que un tubo capturado al reves se dibujaba estrecho dentro de un
    # hueco ancho y dejaba un agujero en la fila.
    check("el peralte del tubo rectangular es el lado mayor",
          "FormaAcero.TuboRectangular && AnchoCm > 0" in perfil_cad
          and "Math.Max(PeralteCm, AnchoCm)" in perfil_cad)
    check("y su ancho de hueco es el lado menor",
          "FormaAcero.TuboRectangular when AnchoCm > 0 => Math.Min(PeralteCm, AnchoCm)"
          in perfil_cad)

    # ------------------------------------------------------------------
    # La zeta: sus dos dobleces son arcos CONCENTRICOS
    # ------------------------------------------------------------------
    # Una zeta es una lamina de espesor unico doblada dos veces, asi que en cada doblez
    # la cara de dentro y la de fuera son dos arcos separados exactamente el espesor. Y
    # los dos centros interiores caen a DISTINTO lado del alma, porque los dos patines
    # salen a lados contrarios: con los dos al mismo lado -que es como estaba- el
    # contorno de abajo se devolvia sobre si mismo y el rayado salia por fuera.
    check("el radio interior de la zeta es el exterior menos el espesor",
          "var rInt = Math.Max(0, rExt - t);" in trazo)
    check("y sus dos dobleces interiores van a distinto lado del alma",
          "X(xAlmaDer + rInt), yt - t - rInt, 3, 4" in trazo
          and "X(xAlmaIzq - rInt), y0 + t + rInt, 9, 10" in trazo)
    check("hay comprobacion de que el contorno de la zeta no se cruza",
          "def se_cruza(pts" in leer(ruta("tools/verificar_perfiles_acero.py")))
    check("y de que sus arcos son concentricos",
          "son concentricos" in leer(ruta("tools/verificar_perfiles_acero.py")))

    # LO QUE MAS IMPORTA DE LAS COTAS: el factor de escala lineal. El dibujo esta en
    # metros, asi que sin el la cota de un peralte de 30 cm diria «0.30» en un plano
    # rotulado «Acot. cm». Las cuatro macros lo fijan en 100, que es 1/escala.
    check("las cotas de acero llevan el factor de escala lineal",
          'PropCota((object)cota, "LinearScaleFactor", 1 / _escala);' in acero_cad)

    # Y el CF se dibuja con UNA polilinea, no con el contorno mas otra igual para el
    # hatch, que es lo que hacia la macro. Ahora el trazo con dobleces esta compartido
    # con la zeta, que es la otra forma que lleva radios.
    check("las formas con dobleces se trazan con una sola polilinea",
          "PolilineaConBulges(c.Puntos, lista, CapaPerfiles)" in acero_cad)
    check("el bulge sale del barrido real, asi el espejo se resuelve solo",
          "public static double BulgeDesdeCentro(" in trazo)



    # Ninguna forma puede acabar en la capa ESTRIBOS: PolyCerrada la tiene escrita a
    # mano, asi que el acero usa Polilinea, que si respeta la capa que se le da.
    check("ninguna forma de acero pasa por PolyCerrada, que fija la capa ESTRIBOS",
          "PolyCerrada(" not in acero_cad)

    perfiles_py = leer(ruta("tools/verificar_perfiles_acero.py"))

    check("hay comprobacion numerica de las nueve formas",
          all(t in perfiles_py for t in ("CF: la canal formada en frio", "WT: la te",
                                         "C: la canal laminada", "L: el angulo",
                                         "ZF: la zeta", "OS: el redondo macizo")))

    # Y el acomodo se prueba con el CATALOGO ENTERO, que es la hoja mas grande que se
    # puede pedir: 1617 secciones, ni una pisando a la de abajo. La separacion y el
    # origen los LEE del codigo, asi que cambiar la constante y no el script se nota.
    check("el acomodo se comprueba con el catalogo entero",
          "El acomodo: una seccion por renglon, con el CATALOGO ENTERO" in perfiles_py
          and "ninguna de las 1617 secciones se pisa con la de abajo" in perfiles_py)
    check("y la separacion y el origen se leen del codigo, no se copian",
          "const double SeparacionEntreSeccionesCm = (-?[\\d.]+);" in perfiles_py
          and "const double OrigenAceroCm = (-?[\\d.]+);" in perfiles_py)
    check("ya no queda ninguna banda por familia en la comprobacion",
          "SEPARACION_BANDAS" not in perfiles_py
          and "bandas_calculadas" not in perfiles_py)

    # Y una vista de las nueve, porque las comprobaciones numericas dicen si la geometria
    # se sostiene pero no si el perfil se PARECE a lo que tiene que parecer: un area
    # correcta y un contorno limpio son compatibles con una te dibujada boca abajo.
    vista = leer(ruta("tools/vista_formas_acero.py"))

    check("hay una vista de las nueve formas que se puede mirar sin AutoCAD",
          "def puntos_con_arcos(" in vista)
    check("la vista NO copia la geometria: la importa de donde se verifica",
          "verificar_perfiles_acero.py" in vista
          and "def perfil_zeta" not in vista)
    check("y no se dibuja si la geometria no pasa sus comprobaciones",
          "raise SystemExit(1) from e" in vista)
    check("el svg esta generado y es de verdad un svg",
          leer(ruta("docs/formas-acero.svg")).startswith("<svg"))
    check("y la revision de las macros lleva al svg",
          "formas-acero.svg" in leer(ruta("docs/macros-acero.md")))

    # ------------------------------------------------------------------
    # El acero se dibuja A LA IZQUIERDA del origen, desde -0.6
    # ------------------------------------------------------------------
    # Es el xDerechaActual = -0.6 de las macros. Y no es solo acomodo: el concreto
    # crece hacia la derecha desde donde acabe lo que ya haya, asi que con el acero
    # en el semiplano negativo las dos hojas no se pisan nunca.
    check("el acero empieza en -60 cm, el -0.6 de las macros",
          "OrigenAceroCm = -60" in acero_cb)
    check("y crece hacia la izquierda",
          "var xIzquierda = xDerecha - (perfil.AnchoDibujoCm * escala);" in acero_cb)
    check("ya no arranca donde acabe el concreto",
          "dibujante.PosicionInicialX()" not in acero_cb)

    # TODAS LAS SECCIONES EN LA MISMA X, una por renglon.
    #
    # Las macros ponian los perfiles de una familia uno al lado del otro hacia la
    # izquierda. Con los nombres del catalogo IMCA eso no se sostiene: el rotulo va
    # centrado debajo de cada seccion y mide casi un metro, asi que los rotulos se
    # pisaban aunque los perfiles no se tocaran.
    check("la x de la derecha se fija en el origen para CADA seccion",
          acero_cb.count("var xDerecha = OrigenAceroCm * escala;") == 1
          and re.search(r"foreach \(var fila in grupo\)(.|\n)*?"
                        r"var xDerecha = OrigenAceroCm \* escala;", acero_cb) is not None)

    check("ya no se avanza en x de una seccion a la siguiente",
          "xDerecha = xIzquierda - aire" not in acero_cb)

    check("y ya no hay aire horizontal por familia",
          "AireDeLaFamiliaCm" not in acero_cb)

    # El hueco se avanza tambien para los saltados: si no, al redibujar una hoja con
    # dos perfiles ya hechos, los otros dos caerian justo encima de ellos.
    m_export = re.search(
        r"private void OnExportAcero\(.*?\n    \}", acero_cb, re.S)

    check("se puede leer OnExportAcero", m_export is not None)
    if m_export:
        cuerpo_exp = m_export.group(0)

        check("el RENGLON se avanza siempre, tambien para las saltadas",
              "yCm += perfil.AltoDibujoCm + SeparacionEntreSeccionesCm;" in cuerpo_exp
              and cuerpo_exp.count("yCm +=") == 1)
        check("y el saltado solo se descuenta del conteo",
              "if (dibujante.Saltadas.Count == saltadasAntes)" in cuerpo_exp)

        # CADA FAMILIA EN SU BANDA. Las cuatro macros arrancan en la misma x, asi que lo
        # unico que evita que se encimen es la Y: baseY 0 el IR, 2.0 el OR, 3.5 el CF y
        # 5.0 el OC. Sin esto, las cuatro familias caian una encima de otra.
        check("se agrupa por familia para recorrerlas juntas",
              "GroupBy(f => f.Familia)" in cuerpo_exp)
        check("y cada seccion se dibuja a la altura de su renglon",
              "DibujarAcero(perfil, xIzquierda, yCm * escala)" in cuerpo_exp)

        # CADA SECCION EN SU RENGLON, y el siguiente 70 cm por encima de la CIMA de esta.
        # Antes eran cuatro alturas fijas por familia venidas de las macros, y los
        # perfiles de una familia iban en fila hacia la izquierda.
        check("la primera arranca en cero, como la macro del IR",
              "var yCm = 0.0;" in cuerpo_exp)
        check("y se acumula el alto DIBUJADO, no el peralte capturado",
              "perfil.AltoDibujoCm + SeparacionEntreSeccionesCm" in cuerpo_exp)

    # UNA SOLA separacion para las doce familias, en lugar de un aire por familia. Es lo
    # que se gana al apilarlas: ya no hay que darle a cada una su propio hueco segun lo
    # ancho que sea su rotulo, porque no hay nada al lado con lo que chocar.
    check("la separacion entre secciones es de 70 cm",
          "SeparacionEntreSeccionesCm = 70" in acero_cb)

    check("y ya no queda tabla de alturas de banda ni aire por familia",
          "BandaDeLaFamiliaCm" not in acero_cb
          and "TechoDeLaBandaCm" not in acero_cb
          and "MargenDeBandaCm" not in acero_cb
          and "AireDeLaFamiliaCm" not in acero_cb)

    check("el acomodo vertical se comprueba con el catalogo de verdad",
          "el caso peor: la seccion mas alta de cada familia".lower()
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")).lower())

    check("y se comprueba que entre seccion y seccion quedan los 70 cm",
          "y entre ellas quedan los"
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")))

    check("las familias se recorren en el orden de la lista, no en el de captura",
          "OrderBy(g => OrdenDeLaFamilia(g.Key))" in acero_cb)

    # La altura de cada seccion ya no se puede consultar en una tabla, asi que el programa
    # tiene que decir donde quedo cada familia: es la unica manera de saber donde buscar.
    check("se dice a que altura quedo cada familia",
          "bandas.Add(" in acero_cb and "una por renglón" in acero_cb)

    # ------------------------------------------------------------------
    # El catalogo de perfiles: las medidas NO se teclean
    # ------------------------------------------------------------------
    catalogo = leer(ruta("client/src/CadLink.App/Models/CatalogoPerfiles.cs"))
    csv = leer(ruta("client/src/CadLink.App/perfiles-acero.csv"))

    check("existe el catalogo de perfiles",
          "public static class CatalogoPerfiles" in catalogo)
    check("es un archivo de datos, no una tabla dentro del programa",
          'public const string Archivo = "perfiles-acero.csv";' in catalogo)
    check("y se busca en tres sitios",
          "AppContext.BaseDirectory" in catalogo
          and "Directory.GetCurrentDirectory()" in catalogo
          and "LocalApplicationData" in catalogo)
    check("si no aparece, queda una semilla y el programa abre igual",
          "Semilla.ToList()" in catalogo)
    check("y se dice de donde salio el catalogo",
          "public static string Origen" in catalogo
          and "CatalogoPerfiles.Origen" in acero_cb)

    # El lector tiene que tragarse lo que salga de un Excel.
    check("el lector acepta punto y coma o coma de separador",
          "linea.Contains(';') ? ';' : ','" in catalogo)
    check("y punto o coma de decimal",
          "Replace(',', '.')" in catalogo)
    check("se salta comentarios y lineas en blanco",
          "linea.StartsWith('#')" in catalogo)
    check("una cabecera exportada se salta sola",
          "Numero(campos, 2) <= 0" in catalogo)
    check("y una familia vacia se deduce del nombre",
          "FamiliaPerfil.DelNombre(nombre)" in catalogo)

    # La lista del desplegable es de la FILA, porque cada fila puede ser de otra
    # familia. Una lista por columna solo podria ofrecerlas todas mezcladas.
    check("cada fila ofrece los perfiles de SU familia",
          "public string[] PerfilesDeLaFamilia" in perfil_row)
    check("y la celda de perfil se enlaza a esa lista",
          'ItemsSource="{Binding PerfilesDeLaFamilia}"' in xaml)
    check("al cambiar de familia se refresca la lista",
          "Raise(nameof(PerfilesDeLaFamilia));" in perfil_row)

    # Y al elegir un perfil del catalogo se traen sus medidas.
    check("elegir un perfil trae sus medidas",
          "private void TraerDelCatalogo()" in perfil_row
          and "TraerDelCatalogo();" in perfil_row)
    check("se avisa de las seis medidas juntas, no una por una",
          "Raise(nameof(PeralteCm));" in perfil_row
          and "Raise(nameof(RadioCm));" in perfil_row)
    check("un perfil que no esta en el catalogo no borra lo capturado",
          "if (c is null)" in perfil_row)

    # El CSV que se entrega tiene que explicarse solo.
    check("el csv explica como se escribe cada renglon",
          "familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio" in csv)
    check("dice que las medidas van en centimetros",
          "TODAS LAS MEDIDAS EN CENTIMETROS" in csv)

    # Y ya no es la semilla de cuatro: es el catalogo del IMCA del usuario.
    n_perfiles = len([l for l in csv.splitlines()
                      if l.strip() and not l.startswith("#") and l.count(";") >= 6])

    check("el catalogo trae los perfiles del IMCA, no la semilla",
          n_perfiles > 1000, f"solo {n_perfiles}")
    check("y dice de donde salio",
          "Generado del manual IMCA" in csv)

    for familia in DOCE:
        check(f"el catalogo trae perfiles {familia}",
              f"\n{familia};" in csv)

    # La familia IR trae SOLO las W. Es lo que estaba mal: IS, IC y S se metian dentro
    # de IR «porque son perfiles I» y su desplegable ofrecia 573 perfiles de cuatro
    # nomenclaturas revueltas, en el que habia que ir sorteando para encontrar una W.
    irs = [l for l in csv.splitlines() if l.startswith("IR;")]
    irs_ajenos = [l for l in irs if not l.split(";")[1].strip().upper().startswith("W")]

    check("la familia IR trae solo perfiles W", irs and not irs_ajenos,
          f"{len(irs_ajenos)} ajenos: {irs_ajenos[:2]}")

    check("el csv explica la novena columna, la de la zeta",
          "solo la ZF" in csv and "ancho2" in csv)

    # La lista del desplegable NO se ordena alfabeticamente. El manual trae cada familia
    # por peralte creciente y dentro de cada peralte por peso, que es como se busca:
    # primero el peralte que cabe y luego se sube de peso hasta que resista. Ordenar por
    # texto pone la de 10" entre la de 1" y la de 12" y deja la lista inservible.
    check("la lista del desplegable conserva el orden del manual",
          "OrderBy(n => n, StringComparer.OrdinalIgnoreCase)" not in catalogo
          and "No se ordena alfabéticamente" in catalogo)

    # El convertidor del formato del IMCA, que no es una hoja normal: cada familia usa
    # otras columnas y las unidades cambian de una a otra.
    imca = leer(ruta("tools/catalogo_imca.py"))

    check("hay convertidor para el formato del IMCA",
          "def filas_del_libro(ruta)" in imca)
    check("mapea las familias del IMCA a las de CadLink",
          '"HSS": "OR"' in imca and '"PIPE": "OC"' in imca and '"W": "IR"' in imca)

    # YA NO DEJA NINGUNA FAMILIA FUERA. Antes las cinco que no se sabian dibujar -te,
    # angulo, canal laminada, zeta y redondo macizo- se contaban y se descartaban: 499
    # perfiles del manual que el programa no podia ofrecer.
    check("ya no hay familias descartadas por no saber dibujarlas",
          "SIN_FORMA" not in imca)
    check("las doce familias del manual se convierten",
          all(f'"{f}"' in imca for f in ("WT", "L", "C", "ZF", "OS", "IS", "IC", "S")))
    check("y cada una dice con que forma se dibuja",
          "FORMAS = {" in imca and '"redondo macizo"' in imca)

    # Las medidas del angulo NO estan en la hoja: las 144 filas de la familia L tienen
    # todas las columnas de geometria en '-'. Hay que leerlas de la designacion.
    check("el angulo lee sus medidas del NOMBRE, porque la hoja no le da ninguna",
          "def medidas_del_angulo(designacion)" in imca
          and "todas las columnas de geometria en '-'" in imca)
    check("y entiende tanto las alas iguales como las desiguales",
          "if len(valores) == 2:" in imca and "elif len(valores) == 3:" in imca)

    # El redondo macizo trae su diametro en dos columnas y en DOS UNIDADES distintas.
    check("el redondo macizo toma el diametro de la columna en milimetros",
          "esta familia esta en CENTIMETROS" in imca
          and "peralte = numero(c.get(6))" in imca)

    # La zeta trae los dos patines de distinto ancho, y no es una errata.
    check("la zeta convierte sus DOS anchos de patin",
          "ancho2 = numero(c.get(8))" in imca)
    check("y el CSV lleva su novena columna",
          "familia;nombre;peralte;ancho;e_alma;e_patin;labio;radio;ancho2" in imca)
    check("coteja las medidas contra los nominales en pulgadas de la hoja",
          "MM_POR_PULGADA" in imca and "pero su nominal" in imca)
    check("y caza los errores de dedo por proporcion imposible",
          "mas de la sexta parte" in imca
          and "W - 36'' x 442.16 lb/ft" in imca)
    check("el csv se copia junto al ejecutable",
          "perfiles-acero.csv" in leer(ruta("client/src/CadLink.App/CadLink.App.csproj")))

    # Y la herramienta que convierte la hoja de perfiles del usuario en ese CSV. Va
    # como script aparte y no dentro del programa a proposito: el catalogo se escribe
    # una vez y se lee mil, asi que no hay por que arrastrar un lector de xlsx en el
    # ejecutable para algo que se hace el dia que cambia la lista.
    conv = leer(ruta("tools/catalogo_desde_excel.py"))

    check("hay herramienta para convertir la hoja de Excel",
          "def filas_de_xlsx(ruta)" in conv)
    check("lee el xlsx sin bibliotecas de fuera",
          "import zipfile" in conv and "openpyxl" not in conv)
    check("encuentra los encabezados aunque haya titulos arriba",
          "mapear_encabezados(fila)" in conv)
    check("reconoce los nombres de columna de los catalogos",
          '"tw"' in conv and '"bf"' in conv and '"tf"' in conv)
    check("y si la hoja viene en milimetros avisa, no convierte solo",
          "--mm" in conv and "convertir por si mismo lo que PARECE" in conv)

    # ------------------------------------------------------------------
    # Los DOS estilos de texto de las macros de acero
    # ------------------------------------------------------------------
    # Las cuatro macros crean el estilo ACERO y se lo ponen a cada cota; los rotulos
    # van con SECCIONES. El port usaba el de los rotulos para todo, asi que las cotas
    # salian con otra letra y otra altura de las que dicen las macros.
    check("se crea el estilo de texto ACERO de las cotas",
          "private void AsegurarEstiloAcero()" in acero_cad
          and 'EstiloTextoAcero = "ACERO"' in acero_cad)
    check("y se crea junto con el de los rotulos",
          "AsegurarEstiloTexto();" in acero_cad and "AsegurarEstiloAcero();" in acero_cad)
    check("cada cota de acero lleva ese estilo",
          'PropCota((object)cota, "TextStyle", EstiloTextoAcero);' in acero_cad)

    # La altura ya no es el 0.015 fijo de las macros, es proporcional al perfil, y para
    # uno de 30 cm da EXACTAMENTE ese 0.015. Lo comprueba numericamente
    # verificar_perfiles_acero.py; aqui solo que el tope de arriba sea ese.
    check("el tope de la altura de cota sigue siendo el 0.015 de las macros",
          "_textoCotaAcero = Acotar(referencia / 10, 0.4 * Cm, 1.5 * Cm);" in acero_cad)
    check("hay comprobacion de que un perfil de 30 cm sale como antes",
          "un perfil de 30 cm sale con el" in leer(
              ruta("tools/verificar_perfiles_acero.py")))

    # La altura del estilo va en CERO. Un estilo con altura fija manda sobre la del
    # texto, y las cuatro macros le fijan la altura a cada cota por objeto: con el
    # 0.015 que pone la IR en el estilo, esas asignaciones no harian nada.
    check("el estilo ACERO va con altura variable",
          "estilo.Height = 0d;" in acero_cad)

    # ------------------------------------------------------------------
    # El rotulo: su altura y su ancho salen de UNA regla, no de cuatro numeros
    # ------------------------------------------------------------------
    # Las cuatro macros ponian cuatro alturas a mano -0.03 el IR, 0.022 el CF, 0.02 el
    # OC- y solo la del OR tenia una regla: 0.02 si su primer numero no pasaba de 6 y
    # 0.03 si si. Esa es la unica de las cuatro con un motivo -el rotulo se centra bajo
    # el perfil, asi que en uno chico un texto grande sobresale- y es la que se
    # generalizo. Da los mismos numeros donde ellas los daban; lo comprueba
    # verificar_perfiles_acero.py con los cuatro casos.
    check("la altura del rotulo sale del peralte",
          "public double AlturaRotuloCm => Math.Clamp(PeralteCm / 10, 2.0, 3.0);"
          in perfil_cad)
    check("y el ancho de la caja, del renglon mas largo",
          "public double AnchoRotuloCm" in perfil_cad
          and "Math.Max(70, masLargo * AlturaRotuloCm * 0.6)" in perfil_cad)
    check("ya no hay anchos de caja escritos por familia",
          '"OC" ? 2.5 : 0.7' not in acero_cad)
    check("los renglones del rotulo se arman en un solo sitio",
          "public IReadOnlyList<string> LineasRotulo" in perfil_cad
          and "string.Join(\"\\\\P\", p.LineasRotulo)" in acero_cad)

    # Y EL AIRE ENTRE SECCIONES LO MANDA EL ROTULO cuando es mas ancho que el perfil.
    # Un renglon como «PERFIL: IS - 225 mm x 12.7 mm / 750 mm x 9.5 mm» mide casi un
    # metro y el perfil que rotula, 22 cm: con el aire de la macro, dos secciones asi
    # quedan separadas pero sus rotulos se pisan.
    # El ancho del rotulo ya no decide ningun aire horizontal -no hay nada al lado con lo
    # que chocar-, pero si decide el ancho de su caja, que es lo que evita que un nombre de
    # cuarenta y seis caracteres se parta en tres renglones.
    check("el ancho de la caja del rotulo sale de su renglon mas largo",
          "masLargo * AlturaRotuloCm * 0.6" in perfil_cad)
    check("hay comprobacion de que el nombre mas largo del IMCA no se parte",
          "sin partirlo" in leer(ruta("tools/verificar_perfiles_acero.py")))

    # ------------------------------------------------------------------
    # La auditoria de las cuatro macros, escrita
    # ------------------------------------------------------------------
    audit = leer(ruta("docs/macros-acero.md"))

    check("esta escrita la revision de las cuatro macros", len(audit) > 3000)
    check("dice que hace cada una", "Qué hace cada macro" in audit)
    check("y que se repite en las cuatro", "se repite, literalmente" in audit)
    check("apunta las contradicciones que traen",
          "se contradicen en cómo es ese estilo" in audit
          and "El rayado del OC es invisible" in audit)
    check("cuenta las cinco formas que no tenian macro",
          "Las cinco formas que no tenían macro" in audit
          and all(f in audit for f in ("WT", "ZF", "OS", "`L`", "`C`")))
    check("explica que familia y forma son dos cosas distintas",
          "Familia y forma son dos cosas distintas" in audit)
    check("y por que el aparato de la cota tiene que ser proporcional",
          "proporcional al peralte" in audit and "0.64 cm" in audit)
    check("y por que el rayado, en cambio, NO se liga al peralte",
          "misma densidad en el papel" in audit)
    check("explica el acomodo de una seccion por renglon",
          "El acomodo: una sección por renglón, 70 cm entre ellas" in audit
          and "borde derecho en `x = −0.6`" in audit
          and "SeparacionEntreSeccionesCm = 70" in audit)
    check("y por que las bandas por familia estaban mal",
          "también estaba mal" in audit
          and "los rótulos se pisaban" in audit)
    check("y cuenta las constantes que se fueron con las bandas",
          all(c in audit for c in ("AireDeLaFamiliaCm", "BandaDeLaFamiliaCm",
                                   "TechoDeLaBandaCm", "MargenDeBandaCm")))

    check("explica las 16 propiedades del manual",
          "Las propiedades geométricas del manual" in audit
          and all(p in audit for p in ("PesoKgM", "AreaCm2", "IxCm4", "SxCm3",
                                       "ZxCm3", "RxCm", "IyCm4", "SyCm3",
                                       "ZyCm3", "RyCm", "RminCm", "JCm4",
                                       "CwCm6", "IxyCm4", "XbarCm", "YbarCm")))
    check("y que null no es cero",
          "`null` no es cero" in audit and "se queda **vacía**" in audit)
    check("y avisa de los 56 perfiles que no cuadran",
          "**56**" in audit and "erratas de la hoja" in audit)
    check("y de los valores de diseño del CF y del ZF",
          "`Idx` y `Sxe`" in audit and "ancho efectivo" in audit)
    check("y de la pared de diseño de los tubos",
          "FACTOR_PARED_DISEÑO" in audit)

    check("explica que la geometria vive en un solo sitio",
          "la geometría, en un solo sitio" in audit
          and "TrazoAcero.De(p, x, yAbajo, escala," in audit)
    check("y por que la vista previa no calcula la forma por su cuenta",
          "puede acabar\nenseñando algo **distinto** de lo que se dibuja" in audit)
    check("y por que el hueco del tubo es EvenOdd",
          "FillRule = EvenOdd" in audit and "hueco de verdad" in audit)

    check("y lo que sigue faltando",
          "Lo que sigue faltando" in audit and "acuerdo entre alma y patín" in audit)

    check("hay comprobacion numerica del catalogo y del acomodo",
          "El acomodo del acero"
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")))
    check("y del acomodo vertical de las secciones",
          "TODAS las secciones se alinean con su borde derecho en x = -0.6"
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")))
    check("y la vuelta completa Excel a catalogo esta probada",
          "La vuelta completa: Excel -> CSV -> catalogo"
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")))

    # ------------------------------------------------------------------
    # El fallo que vio el usuario: MoveToTop abandonaba la via buena
    # ------------------------------------------------------------------
    # AutoCAD rechazo la llamada por estar ocupado (RPC_E_CALL_REJECTED) y la cascada
    # paso a las otras dos vias, que en AutoCAD 2026 fallan siempre por el tipo del
    # arreglo. El usuario veia tres fallos y el diagnostico culpaba al arreglo.
    arreglos = leer(ruta("client/src/CadLink.Cad/AcadArreglos.cs"))
    conexion = leer(ruta("client/src/CadLink.Cad/AcadConnection.cs"))

    check("se puede saber si AutoCAD estaba ocupado",
          "public static bool EstaOcupado(Exception ex)" in conexion)
    check("los arreglos reintentan LA MISMA via cuando esta ocupado",
          "AcadConnection.EstaOcupado(ex)" in arreglos
          and "intento < IntentosPorOcupado" in arreglos)
    check("y solo pasan a la siguiente via si el error es otro",
          "return Surtio(via);" in arreglos)
    check("con la misma paciencia que el resto de las llamadas",
          "IntentosPorOcupado = 12" in arreglos and "EsperaMs = 250" in arreglos)

    # Y el orden de dibujo, que es estetico, ya no se anuncia como que el dibujo
    # puede estar incompleto.
    seccion = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))
    alzado = leer(ruta("client/src/CadLink.Cad/AlzadoDrawer.cs"))

    check("el reordenado de la seccion se reporta como nota",
          "private bool ConArregloParaOrdenar(" in seccion
          and 'ConArregloParaOrdenar("MoveToTop", objetos,' in seccion
          and 'ConArregloParaOrdenar("MoveToBottom", objetos,' in seccion)
    check("no queda ningun MoveTo reportado como fallo en la seccion",
          'ConArregloDeEntidades("MoveTo' not in seccion)
    check("y el del alzado tambien va como nota",
          "private void FalloDeOrden(" in alzado
          and "FalloDeOrden, Nota);" in alzado)
    # El texto va partido en dos renglones de codigo, asi que se busca por trozos y no
    # por la frase entera: buscarla completa fallaba por el salto de linea del fuente,
    # que es justo el tipo de comprobacion fragil que no hay que escribir.
    check("la nota dice que el dibujo esta completo",
          "no se pudo reordenar" in seccion and "El dibujo está " in seccion
          and "no se pudo reordenar" in alzado and "El alzado está " in alzado)



# ======================================================================
# 22. Las dos macros de ZAPATA CORRIDA
#
#     Lo que se vigila aqui no es que el codigo compile -eso lo hace el
#     compilador- sino que el port siga siendo UN port: que las corridas no
#     se lleven una copia de lo que ya calculan las aisladas, y que no se
#     mezclen los niveles de las dos familias, que son contrarios.
#
#     Los numeros, uno por uno, se comprueban EJECUTANDO el codigo en
#     tools/verificar_zapatas_corridas.py y en tools/prueba-zapata.
# ======================================================================
def v22_zapatas_corridas() -> None:
    print("\n[22] Zapatas corridas: las dos macros")

    trazo = leer(ruta("client/src/CadLink.Cad/TrazoZapataCorrida.cs"))
    datos = leer(ruta("client/src/CadLink.Cad/ZapataCorridaCad.cs"))
    aislada = leer(ruta("client/src/CadLink.Cad/TrazoZapata.cs"))
    doc = leer(ruta("docs/macro-zapatas-corridas.md"))

    check("la geometria de la corrida existe y es una clase estatica",
          "public static class TrazoZapataCorrida" in trazo)

    # ------------------------------------------------------------------
    # Los niveles de las dos familias son CONTRARIOS y no se mezclan
    # ------------------------------------------------------------------
    # La corrida cuelga del terreno -yNivTerr = -3.5- y la aislada tiene el
    # fondo fijo en -8. Si una tomara el numero de la otra, dos zapatas con
    # desplantes distintos saldrian con el terreno a dos alturas.
    check("la corrida cuelga del terreno en -3.5",
          "public const double YNivelTerreno = -3.5;" in trazo)
    # El nombre de la constante de las aisladas SI aparece en el comentario -se
    # cita a proposito-, asi que lo que se vigila es que no se USE.
    check("y no se trae el fondo fijo de las aisladas",
          "-8.0" not in _sin_comentarios(trazo)
          and "YBaseElevacion" not in _sin_comentarios(trazo))
    check("las aisladas conservan el suyo",
          "public const double YBaseElevacion = -8.0;" in aislada)
    check("y queda escrito que las dos familias no lo comparten",
          "comparten este número" in trazo)

    # ------------------------------------------------------------------
    # EL ERROR QUE SE CORRIGIO: la seccion va CENTRADA en su offset
    # ------------------------------------------------------------------
    # Las dos macros hacen xBase = offsetX - anchoZapata / 2. La primera
    # version del port arrancaba en el propio offset y corria media zapata
    # por seccion, con el rotulo -que va centrado en el eje- descuadrado.
    check("xBase resta media zapata al offset",
          "OffsetX(tipo, indice) - (anchoM / 2)" in trazo)
    check("el paso entre secciones son los 2 m de las macros",
          "public const double SeparacionSecciones = 2.0;" in trazo)
    check("el lindero arranca en -2",
          "public const double LinderoPrimerOffset = -2.0;" in trazo)
    check("y crece al lado contrario que la central",
          "LinderoPrimerOffset - (i * SeparacionSecciones)" in trazo
          and "i * SeparacionSecciones" in trazo)
    check("un indice negativo no manda la seccion a otro lado",
          "Math.Max(indice, 0)" in trazo)

    # ------------------------------------------------------------------
    # El muro
    # ------------------------------------------------------------------
    # Las macros NO recortan el muro al pano de la zapata. Recortarlo esconde
    # un espesor mal capturado en lugar de ensenarlo en el dibujo.
    check("el muro no se recorta al pano de la zapata",
          "las macros no lo recortan" in trazo
          and "Math.Min(xMuroDer" not in trazo)
    check("el muro se apoya en la contratrabe cuando la hay",
          "Math.Max(yContratrabeTop, a.YZapTop)" in trazo)
    check("y nunca sale de alto negativo", "Math.Max(yTope, yBase)" in trazo)

    # ------------------------------------------------------------------
    # El muro de enrase: la unica cuenta con truco
    # ------------------------------------------------------------------
    for nombre, valor in (("EnraseAltoObjetivo", "0.08"), ("EnraseJunta", "0.01"),
                          ("EnraseDesfaseLado", "0.01"), ("EnraseAltoMinimo", "0.02")):
        check(f"el enrase trae su {nombre} = {valor}",
              f"public const double {nombre} = {valor};" in trazo)

    check("el reparto se busca hasta 50 piezas",
          "public const int EnraseMaxPiezas = 50;" in trazo)
    check("con n piezas hay n-1 juntas, no n",
          "(hueco - ((n - 1) * EnraseJunta)) / n" in trazo)
    check("y gana el reparto mas cercano a los 8 cm",
          "Math.Abs(alto - EnraseAltoObjetivo)" in trazo
          and "error < mejorError" in trazo)
    check("con menos de 2 cm de hueco no se dibuja enrase",
          "hueco <= EnraseAltoMinimo" in trazo)
    check("y la hilada se enrasa con la caja de la cadena",
          "public static Enrase MuroDeEnrase(double xIzq, double ancho," in trazo
          and "de la caja de la cadena" in trazo)

    # ------------------------------------------------------------------
    # El acero del muro de concreto
    # ------------------------------------------------------------------
    # Los 5 cm son AL EJE de la varilla, no "recubrimiento + medio diametro":
    # asi esta en las dos macros, y cambiarlo mueve el acero de todos los muros.
    check("los ejes del acero van a 5 cm del pano",
          "public const double MuroRetiroAcero = 0.05;" in trazo
          and "m.XIzq + MuroRetiroAcero" in trazo)
    check("un muro delgado pierde la doble parrilla en lugar de cruzar el acero",
          "return new EjesAcero(m.XCentro, m.XCentro, false);" in trazo)
    check("los circulos se reparten con la separacion VERTICAL",
          "double[] CirculosDelMuro(Muro m, double yTerreno, double diam, double sepVertM)"
          in trazo)
    check("y se dibuja uno menos de los que caben, como la macro",
          "var aDibujar = caben - 1;" in trazo)
    check("la pata queda por encima de la parrilla inferior",
          "public static double YDeLaPata(" in trazo)

    # LAS DOS MACROS DOBLAN DISTINTO, y esa es la diferencia que se porto mal
    # la primera vez: no es "hacia el eje de la zapata".
    check("la central dobla cada varilla hacia SU lado",
          "VarillaMuro[] VerticalesCentral(" in trazo
          and "new VarillaMuro(xIzq, yTerreno, yPata, xIzq - doblez, -1)" in trazo
          and "new VarillaMuro(xDer, yTerreno, yPata, xDer + doblez, 1)" in trazo)
    check("el lindero dobla las dos a la izquierda",
          "VarillaMuro[] VerticalesLindero(" in trazo
          and "yPata, XFin(xDer), -1)" in trazo
          and "yPata + sep, XFin(xIzq), -1)" in trazo)
    check("y a dos alturas distintas, con su separacion ajustada",
          "public static double SepDeLosDobleces(" in trazo
          and "LinderoSepDoblecesFactorMin" in trazo)
    check("la pata del lindero se recorta al recubrimiento de la zapata",
          "var xLimIzq = a.XBase + rec + (diamMuro / 2);" in trazo)
    check("el doblez usa el validador de las aisladas, no una copia",
          "TrazoZapata.FactorGanchoValido(factorDoblez)" in trazo)
    check("y los 15 diametros son los mismos que los del dado",
          "FactorDoblezMuro = TrazoZapata.FactorGanchoAbajo" in trazo)

    # ------------------------------------------------------------------
    # La anotacion: cada offset con el nombre de lo que mide
    # ------------------------------------------------------------------
    for nombre, valor in (("CotaAnchoTotal", "0.13"), ("CotaAnchosParciales", "0.075"),
                          ("CotaAlturaTotal", "0.1445"),
                          ("CotaAlturasParciales", "0.0585"),
                          ("RotuloOffset", "0.25"), ("RotuloSalto1", "0.34"),
                          ("RotuloSalto2", "0.42")):
        check(f"la distancia {nombre} vale {valor}",
              f"public const double {nombre} = {valor};" in trazo)

    check("el rotulo se mide desde el fondo de la plantilla",
          "var yFondo = yZapBot - PlantillaEspesor;" in trazo)
    # EL TEXTO DEL NIVEL, A LA IZQUIERDA. Se pidio asi, y con anclaje a la
    # izquierda: la resta de la macro -«xCentro + 0.35 - 0.313»- lo dejaba centrado
    # encima del muro, y en una zapata angosta tapaba el arranque del enrase.
    check("el texto del nivel arranca en el pano izquierdo de la zapata",
          "(a.XBase, a.YTerreno + (AltoTextoNivel / 2) + 0.035);" in trazo
          and "a.XCentro + 0.35 - 0.313" not in trazo)
    check("y se escribe anclado a la izquierda, para que crezca hacia dentro",
          "anclaje: AnclajeIzquierda);" in leer(
              ruta("client/src/CadLink.Cad/ZapataDrawer.Corrida.cs")))

    # ------------------------------------------------------------------
    # Lo que ya existia NO se vuelve a escribir
    # ------------------------------------------------------------------
    check("las parrillas se delegan en la rutina de las aisladas",
          "TrazoZapata.ParrillaEnAlzado(" in trazo
          and "TrazoZapata.Parrilla ParrillaEnAlzado" in trazo)
    check("y no hay constantes de gancho duplicadas",
          "FactorGanchoMinimo" not in trazo and "FactorGanchoMaximo" not in trazo)

    # ------------------------------------------------------------------
    # Los datos de la hoja
    # ------------------------------------------------------------------
    check("los datos traen anotadas las celdas de LAS DOS macros",
          "<c>E4</c> / <c>O4</c>" in datos and "<c>H4</c> / <c>R4</c>" in datos)
    check("el espesor del muro apunta a su celda segun el tipo de muro",
          "<c>H9</c> / <c>R9</c>" in datos and "<c>G7</c> / <c>P7</c>" in datos)
    check("y queda avisado que con mamposteria el acero sube un renglon",
          "suben un renglón" in datos)
    check("el espesor del muro esta en cm y se pasa a metros",
          "EspesorMuroCm / 100.0" in datos)
    check("con la celda vacia el muro sale de 15 cm",
          "EspesorMuroCm > 0 ? EspesorMuroCm / 100.0 : 0.15" in datos)
    check("cada zapata ocupa 16 renglones de la hoja",
          "<b>16 renglones</b>" in datos)
    check("un bloque capturado como 0 no cuenta como bloque",
          "public static bool HayBloque(string? id)" in datos
          and 't != "0"' in datos)
    check("el titulo del lindero no dice «corrida», como en su macro",
          '"ZAPATA DE LINDERO"' in datos and '"ZAPATA CORRIDA CENTRAL"' in datos)

    # ------------------------------------------------------------------
    # El inventario, que es lo que dice que falta
    # ------------------------------------------------------------------
    check("el inventario de las dos macros esta escrito",
          "# Inventario de `ZAPATA CORRIDA CENTRAL V2`" in doc)
    # El inventario tiene que seguir el paso del port: cuando el dibujante no existia
    # decia «Falta», y ahora tiene que decir cual es su archivo. Un inventario que se
    # queda viejo es peor que no tenerlo, porque se lee y se cree.
    check("el inventario apunta al dibujante que existe de verdad",
          "ZapataDrawer.Corrida.cs" in doc)
    check("y a la hoja de captura",
          "pestaña «Zapatas Corridas»" in doc)
    check("y deja por escrito los errores que cazo la comprobacion",
          "Lo que se corrigió al leer el fuente" in doc)


# ======================================================================
# 23. La HOJA de zapatas corridas: la pestana, el modelo y el dibujante
#
#     Esta hoja no se puede compilar en el entorno donde se escribio -la
#     aplicacion es WPF y aqui no hay Windows-, asi que estas
#     comprobaciones son la unica red: que la pestana ya no sea un
#     marcador, que cada nombre del XAML exista en el codigo, que la fila
#     se guarde en el .clk y que el dibujante este enganchado.
# ======================================================================
def v23_hoja_zapatas_corridas() -> None:
    print("\n[23] La hoja de zapatas corridas")

    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    hoja = leer(ruta("client/src/CadLink.App/MainWindow.ZapatasCorridas.cs"))
    fila = leer(ruta("client/src/CadLink.App/Models/ZapataCorridaRow.cs"))
    filas = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))
    ventana = leer(ruta("client/src/CadLink.App/MainWindow.xaml.cs"))
    zapatas = leer(ruta("client/src/CadLink.App/MainWindow.Zapatas.cs"))
    proyecto = leer(ruta("client/src/CadLink.App/Models/Proyecto.cs"))
    drawer = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.Corrida.cs"))

    # ------------------------------------------------------------------
    # La pestana ya NO es un marcador
    # ------------------------------------------------------------------
    # Se mira SOLO el trozo de esta pestana: los otros modulos que faltan
    # -muros de contencion, placa base, conexiones- siguen siendo marcadores a
    # proposito, y buscar el texto en todo el XAML los daba por rotos.
    i_tab = xaml.index('<TabItem Header="Zapatas Corridas">')
    pestana = xaml[i_tab:xaml.index("</TabItem>", i_tab)]

    check("la pestana ya no es un marcador",
          "Modulo pendiente de portar" not in pestana)
    check("y trae su cuadricula, su vista previa y sus totales",
          'x:Name="ZapatasCorridasGrid"' in xaml
          and 'x:Name="ZapataCorridaPreviewCanvas"' in xaml
          and 'x:Name="TotalesZapatasCorridasText"' in xaml)
    check("con los dos botones de siempre y sus estilos",
          'Click="OnRevisarZapatasCorridas"' in xaml
          and 'Style="{StaticResource SecondaryButtonStyle}"' in xaml
          and 'x:Name="DibujarZapatasCorridasButton"' in xaml
          and 'Click="OnExportZapatasCorridas"' in xaml)

    # El estilo de dibujo es del JUEGO: los radios van atados a los de concreto.
    check("el estilo de dibujo esta atado a los botones de la hoja de concreto",
          'x:Name="ZapCorTipo1Radio"' in xaml
          and "IsChecked=\"{Binding IsChecked, ElementName=Tipo1Radio, Mode=TwoWay}\"" in xaml)

    # ------------------------------------------------------------------
    # Cada nombre del XAML existe en el codigo, y al contrario
    # ------------------------------------------------------------------
    # Las de las parrillas ya no salen aqui: son DOS columnas de plantilla -una por
    # parrilla- y su lista va en el XAML con x:Static, porque una celda de plantilla no
    # tiene x:Name al que agarrarse.
    for nombre in ("ColZapCorVarMuro", "ColZapCorVarMuroVert"):
        check(f"la columna {nombre} esta en el XAML y se llena en el codigo",
              f'x:Name="{nombre}"' in xaml and f"{nombre}.ItemsSource" in hoja)

    check("el rotulo del doblez del muro se rellena desde el codigo",
          'x:Name="ZapCorGanchoText"' in xaml and "ZapCorGanchoText.Text" in hoja)

    # ------------------------------------------------------------------
    # Las casillas que solo aplican con un tipo de muro
    # ------------------------------------------------------------------
    # Una zapata con muro de MAMPOSTERIA no tiene armado de muro y una de
    # CONCRETO no lleva cadena de desplante: sus macros ni las leen. Las celdas
    # se apagan solas con IsEnabled enlazado a la fila, sin una linea de codigo.
    tema = leer(ruta("client/src/CadLink.App/Theme/ExcelTabs.xaml"))

    check("existe el estilo de celda que solo aplica con mamposteria",
          'x:Key="CeldaSoloMamposteria"' in tema
          and '<Setter Property="IsEnabled" Value="{Binding MuroEsMamposteria}" />' in tema)
    check("y el que solo aplica con concreto",
          'x:Key="CeldaSoloConcreto"' in tema
          and '<Setter Property="IsEnabled" Value="{Binding MuroEsConcreto}" />' in tema)
    check("los dos se ven apagados, no solo intocables",
          tema.count('<Trigger Property="IsEnabled" Value="False">') >= 2)

    check("la cadena de desplante se apaga con muro de concreto",
          pestana.count("CeldaSoloMamposteria") == 1)
    # Cinco desde que el muro tiene DOS varillas -la horizontal y la vertical-: la de
    # doble parrilla del muro, las dos varillas y las dos separaciones.
    check("y las cinco casillas del armado del muro con mamposteria",
          pestana.count("CeldaSoloConcreto") == 5)

    # La fila tiene que AVISAR de los dos cambios para que la celda se entere.
    check("la fila avisa cuando cambia el tipo de muro",
          "Raise(nameof(MuroEsConcreto));" in fila
          and "Raise(nameof(MuroEsMamposteria));" in fila)

    for manejador in ("OnRevisarZapatasCorridas", "OnExportZapatasCorridas"):
        check(f"el manejador {manejador} existe",
              f"private void {manejador}(object sender, RoutedEventArgs e)" in hoja)

    # ------------------------------------------------------------------
    # Las celdas que se pueden ESCRIBIR van por Text, no por SelectedItem
    # ------------------------------------------------------------------
    # Es el bug que dejaba las zapatas «de lindero»: con SelectedItemBinding y la
    # lista llegando tarde, el enlace pisa el valor capturado.
    for prop, lista in (("Tipo", "ZapataCorridaRow.Tipos"),
                        ("TipoMuro", "ZapataCorridaRow.TiposDeMuro"),
                        ("DobleParrilla", "ZapataCorridaRow.SiNo"),
                        ("MuroDobleParrilla", "ZapataCorridaRow.SiNo"),
                        ("IdContratrabe", "ZapataCorridaRow.ContratrabesDisponibles"),
                        ("IdCadena", "ZapataCorridaRow.CadenasDisponibles")):
        check(f"la celda de {prop} es un combo editable enlazado por Text",
              f"models:{lista}" in xaml
              and f'Text="{{Binding {prop}, UpdateSourceTrigger=PropertyChanged}}"' in xaml)

    # ------------------------------------------------------------------
    # El modelo de fila
    # ------------------------------------------------------------------
    check("la fila existe y hereda de Row",
          "public sealed class ZapataCorridaRow : Row" in fila)
    check("y convierte a geometria en UN solo sitio",
          "public ZapataCorridaCad AFormatoCad()" in fila)
    check("las listas de bloques son estaticas y observables",
          "public static ObservableCollection<string> ContratrabesDisponibles" in fila
          and "public static ObservableCollection<string> CadenasDisponibles" in fila)
    check("el recubrimiento sale de la geometria, no de una casilla",
          "public double RecM => TrazoZapataCorrida.RecPorOmision;" in fila)
    check("la columna «Falta» avisa de la varilla del muro de concreto",
          "la varilla del muro de concreto" in fila)
    check("la coleccion viva esta en los datos del proyecto",
          "public ObservableCollection<ZapataCorridaRow> ZapatasCorridas" in filas)
    check("y el ejemplo trae una de cada tipo",
          "ZapataCorridaCad.Central, Id = \"ZC-1\"" in filas
          and "ZapataCorridaCad.Lindero, Id = \"ZCL-1\"" in filas)
    check("con la contratrabe y la cadena que usan, capturadas en concreto",
          'Elemento = "CONTRATRABE", Id = "CT-1"' in filas
          and 'Elemento = "CADENA DE DESPLANTE", Id = "CD-1"' in filas)

    # ------------------------------------------------------------------
    # Los enganches de la ventana
    # ------------------------------------------------------------------
    check("las listas se llenan al arrancar",
          "LlenarListasZapatasCorridas();" in ventana)
    check("la cuadricula se enlaza con el resto",
          "EnlazarZapatasCorridas();" in ventana)
    check("la vista previa se engancha UNA vez, en el constructor",
          ventana.count("EngancharVistaPreviaZapataCorrida();") == 1)
    check("el boton lo enciende la LICENCIA, no el XAML",
          "DibujarZapatasCorridasButton.IsEnabled = puedeDibujar;" in ventana)
    check("las listas de bloques se refrescan cuando cambia la hoja de concreto",
          "ActualizarListasDeZapatasCorridas();" in ventana)

    # El doblez es UNO para toda la obra: la casilla de las aisladas manda aqui.
    # EL DOBLEZ SE PUEDE CAMBIAR EN LAS DOS HOJAS, y es UN valor: el de la obra.
    # Cada casilla escribe en la otra, con una bandera que corta el ciclo -sin ella,
    # escribir en una disparaba el evento de la otra sin fin y el cursor saltaba-.
    check("la hoja de corridas tiene su propia casilla de doblez",
          'x:Name="ZapCorGanchoBox"' in xaml
          and 'TextChanged="OnGanchoCorridaCambio"' in xaml
          and "private void OnGanchoCorridaCambio(" in hoja)
    check("y escribe en la casilla del juego, que es la que manda",
          "ZapGanchoDiametrosBox.Text = ZapCorGanchoBox.Text;" in hoja)
    check("con la bandera que evita el ciclo entre las dos casillas",
          "_sincronizandoGancho" in hoja)
    check("la casilla de las aisladas sigue poniendo al dia la otra hoja",
          "ActualizarGanchoDeCorridas();" in zapatas
          and "DibujarVistaPreviaZapataCorrida();" in zapatas)

    # ------------------------------------------------------------------
    # Se guarda en el .clk
    # ------------------------------------------------------------------
    check("el proyecto guarda las zapatas corridas en su propia lista",
          "public List<FilaGuardada> ZapatasCorridas" in proyecto)
    check("se escriben al guardar",
          "p.ZapatasCorridas.Add(FilaSerializable.Leer(z));" in ventana)
    check("y se leen al abrir",
          "_datos.ZapatasCorridas.Clear();" in ventana
          and "foreach (var fila in p.ZapatasCorridas)" in ventana)

    # ------------------------------------------------------------------
    # El dibujante
    # ------------------------------------------------------------------
    check("el dibujante de corridas es un parcial del de zapatas",
          "public sealed partial class ZapataDrawer" in drawer)
    check("y no duplica las primitivas de AutoCAD",
          "private object? Linea(" not in drawer
          and "private object? HatchRect(" not in drawer
          and "AcadConnection.Retry" in drawer)
    check("su punto de entrada devuelve un resumen propio",
          "public ResumenCorrida DibujarCorridas(" in drawer
          and "public sealed class ResumenCorrida" in drawer)
    check("cada familia lleva su propio indice de acomodo",
          "var indice = lindero ? iLindero++ : iCentral++;" in drawer)
    check("un fallo en una zapata no aborta el juego",
          'Fallo($"Zapata corrida \'{z.Id}\'", ex);' in drawer)
    check("la contratrabe se inserta ANTES de dibujar la zapata",
          drawer.index("InsertarBloqueApoyado(") < drawer.index("HatchConcreto(xBase"))

    # EL ERROR QUE SE CORRIGIO: se estaba apoyando en el LOMO de la zapata y salia
    # flotando encima. Las dos macros la apoyan en yZapBot, que es el pano de arriba
    # de la plantilla: arranca del desplante y atraviesa el espesor, y de ahi sale
    # que la linea superior de la zapata se interrumpa.
    check("y se apoya en el pano de arriba de la plantilla",
          "lindero ? a.XDer : a.XCentro, a.YZapBot, lindero);" in drawer)
    check("la vista previa la apoya en el mismo sitio",
          "a.YZapBot + TrazoZapataCorrida.ContratrabeAltoPorOmision" in hoja)
    check("la geometria sale de TrazoZapataCorrida y no se recalcula",
          "TrazoZapataCorrida.Colocar(" in drawer
          and "TrazoZapataCorrida.MuroDeEnrase(" in drawer
          and "TrazoZapataCorrida.EjesDelAcero(" in drawer
          and "TrazoZapataCorrida.CirculosDelMuro(" in drawer)
    check("el enrase pinta primero los rellenos y luego los contornos",
          drawer.index("Pasada 1: los rellenos") < drawer.index("Pasada 2: los contornos"))
    check("y manda sus contornos al frente cuando va relleno",
          "AlFrente(_cont, contornos);" in drawer)
    check("el titulo del lindero sale de la clase de datos, con su texto de macro",
          "z.TipoTexto" in drawer)
    check("la hoja llama al dibujante",
          "dibujante.DibujarCorridas(zapatas)" in hoja)

    # ------------------------------------------------------------------
    # LOS ROTULOS Y LOS LEADERS DE LAS MACROS DE CORRIDA
    # ------------------------------------------------------------------
    # UN ROTULO POR PARRILLA, y el mismo para las dos hojas: su cabecera, sus dos
    # varillas con su lecho y dos flechas, una a la varilla de canto y otra a la de
    # punta. Lo que cambia es DONDE se cuelga, y eso lo decide el que dibuja.
    check("hay un rotulo por parrilla, con su cabecera y sus dos flechas",
          "private void RotuloDeParrillaCompleto(" in drawer
          and "La varilla de flexión: por la IZQUIERDA" in drawer
          and "Y la de temperatura: por la DERECHA" in drawer)
    check("las dos varillas de esa parrilla van en el MISMO mtext",
          'texto += "\\n" + segundo;' in drawer)
    check("y el tope es la contratrabe cuando sobresale, y el muro cuando no",
          "var xTopeIzq = Math.Min(a.XMuroIzq, hayCt ? xCtIzq : a.XMuroIzq);" in drawer
          and "var xTopeDer = Math.Max(a.XMuroDer, hayCt ? xCtDer : a.XMuroDer);" in drawer)

    # LA CENTRAL: uno en cada volado, la de abajo a la izquierda y la de arriba a la
    # derecha. «Cada lado» es el volado LIBRE -del pano de la zapata al pano de la
    # contratrabe- y no la cuarta parte del ancho: con la contratrabe de 30 en una
    # zapata de 80, la cuarta parte caia a 20 cm del pano y el renglon se metia dentro
    # del bloque.
    check("la central cuelga uno en cada volado, el de abajo a la izquierda",
          "private static double MitadDelLado(" in drawer
          and "MitadDelLado(xTopeIzq, a.XBase, haciaDerecha: false),\n"
          "                a.XBase, xTopeIzq, xTopeIzq, xTopeDer);" in drawer
          and "MitadDelLado(xTopeDer, a.XDer, haciaDerecha: true),\n"
          "                    xTopeDer, a.XDer, xTopeIzq, xTopeDer);" in drawer)
    check("y ninguno se mete en el bloque del centro",
          "private static double LimiteDelRotulo(" in drawer
          and "xTope + RotuloParrillaHolgura + (AnchoRotuloParrilla / 2)" in drawer
          and "xTope - RotuloParrillaHolgura - (AnchoRotuloParrilla / 2);" in drawer)

    # EL LINDERO: LOS DOS EN EL VOLADO IZQUIERDO, UNO EN CADA MITAD. Ahi el muro esta
    # pegado al pano DERECHO -a su derecha esta la colindancia-, asi que no hay «lado
    # derecho» donde colgar nada y todo el hueco esta a la izquierda.
    check("el lindero cuelga los dos en el volado izquierdo, uno en cada mitad",
          "EL LINDERO: LOS DOS RÓTULOS EN EL VOLADO IZQUIERDO, UNO EN CADA MITAD." in drawer
          and "var xMedio = (a.XBase + xTopeIzq) / 2;" in drawer
          and "(a.XBase + xMedio) / 2, a.XBase, xMedio, xTopeIzq, xTopeDer);" in drawer
          and "(xMedio + xTopeIzq) / 2, xMedio, xTopeIzq, xTopeIzq, xTopeDer);" in drawer)
    check("y con una sola parrilla, uno centrado en todo el volado",
          "(a.XBase + xTopeIzq) / 2, a.XBase, xTopeIzq, xTopeIzq, xTopeDer);" in drawer)
    check("cada rotulo señala varillas de SU franja, no las del otro",
          "private void RotuloDeParrillaCompleto(" in drawer
          and "var xMin = Math.Max(xFranjaMin, p.XCaraIzq + (diam / 2));" in drawer
          and "var xMax = Math.Min(xFranjaMax, p.XCaraDer - (diam / 2));" in drawer)

    # Y NO QUEDA NADA DEL REPARTO VIEJO por tipo de varilla -flexion a un lado y
    # temperatura al otro, apilados-: se cambio por el rotulo por parrilla en las DOS
    # hojas, asi que su codigo se fue.
    check("el reparto viejo por tipo de varilla ya no esta",
          "RotulosDeParrillaCorrida" not in drawer
          and "HuellaRotulos" not in drawer
          and "CarrilDeFlexion" not in drawer
          and "CarrilLibre" not in drawer)

    # EL ROTULO ES UN MTEXT DE VARIOS RENGLONES: en una sola linea medía 30 cm y no
    # cabe en el volado.
    check("los dos textos llevan la C de corrugada, y en dos renglones",
          '$"VAR {etiqueta}C @ {SepTexto(sep)} cm\\n{sufijo}"' in drawer)
    check("el rotulo de parrilla se escribe con ancho de renglon",
          "private const double AnchoRotuloParrilla = 0.22;" in drawer
          and "MtextoAncho(xTexto, yTexto, texto, AnchoRotuloParrilla, AnclajeCentro);" in drawer)
    check("la flecha de temperatura se pega a una varilla de verdad",
          "private static double CirculoMasCercano(" in drawer)

    # LA CABECERA, ARRIBA DE TODO: de que parrilla se habla. Se pidio, y hace falta en
    # cuanto hay dos, porque la palabra del lecho dice en que cama va cada varilla
    # DENTRO de su parrilla, no de que parrilla es.
    check("el rotulo dice de que parrilla es, en su primer renglon",
          'private const string CabeceraParrillaInferior = "PARRILLA INFERIOR";' in drawer
          and 'private const string CabeceraParrillaSuperior = "PARRILLA SUPERIOR";' in drawer
          and "texto = (superior ? CabeceraParrillaSuperior : CabeceraParrillaInferior)"
          in drawer)
    check("y los renglones se cuentan con ella, para que el leader salga del que toca",
          "var renglones = segundo.Length > 0 ? 5 : 3;" in drawer)
    # CADA FLECHA SALE DE SU RENGLON, y con leader QUEBRADO: la cola horizontal sale
    # del renglon de la palabra -el que dice INFERIOR, SUPERIOR o AMBOS SENTIDOS- y
    # de ahi la linea va en diagonal a la varilla. Se pidio asi para que se vea de
    # que renglon sale cada una.
    check("cada flecha arranca en la altura de SU renglon",
          "var yFila1 = yTop - (2.5 * alto);" in drawer
          and "var yFila2 = segundo.Length > 0 ? yTop - (4.5 * alto) : yFila1;" in drawer)

    # Y CON QUIEBRE: cola horizontal desde el renglon y de ahi la diagonal. Recta salia
    # casi a plomo y no se veia de donde arrancaba.
    check("y la linea va quebrada: cola horizontal y luego la diagonal",
          "private List<object> LeaderQuebrado(" in leer(
              ruta("client/src/CadLink.Cad/ZapataDrawer.Planta.cs"))
          and "LeaderQuebrado(xFlexion, p.YBarra, xColaIzq, yFila1, xPalabraIzq, yFila1);"
          in drawer
          and "LeaderQuebrado(xTemp, p.YCirculos, xColaDer, yFila2, xPalabraDer, yFila2));"
          in drawer)
    check("la cola sale por lados contrarios, y no acaba encima de la contratrabe",
          "var xColaIzq = FueraDelBloque(xPalabraIzq - RotuloParrillaCola, xTopeIzq, xTopeDer);"
          in drawer
          and "xPalabraDer + RotuloParrillaCola, xTopeIzq, xTopeDer);" in drawer
          and "private static double FueraDelBloque(" in drawer)

    # LA COLA ARRANCA EN LA PALABRA, no en el borde del bloque: los renglones van
    # CENTRADOS, asi que entre el final de «INFERIOR» y el borde hay aire y la cola
    # parecia suelta. Se mide el renglon de verdad, creandolo y borrandolo.
    check("la cola arranca donde acaba la palabra, medida de verdad",
          "private double AnchoDeRenglon(" in leer(
              ruta("client/src/CadLink.Cad/ZapataDrawer.Planta.cs"))
          and "var xPalabraIzq = xTexto - mitad1;" in drawer
          and "var xPalabraDer = xTexto + (AnchoDeRenglon(palabra2) / 2);" in drawer)
    check("y la cola mide 6 cm, no 3",
          "private const double RotuloParrillaCola = 0.06;" in drawer)
    check("con un solo armado salen las dos flechas igual, como en las macros",
          "Sale TAMBIÉN con un solo armado" in drawer
          and drawer.index("var xTemp = CirculoEnLaFranja(")
          > drawer.index("LeaderQuebrado(xFlexion,"))
    check("y cada una sale por su lado: flexion por la izquierda, temperatura por la derecha",
          "La varilla de flexión: por la IZQUIERDA" in drawer
          and "Y la de temperatura: por la DERECHA" in drawer)
    check("las dos señalan la varilla mas cercana que tienen",
          "var xFlexion = Math.Clamp(x1, xMin, xMax);" in drawer
          and "var xTemp = CirculoEnLaFranja(p.Circulos, x2, xMin, xMax);" in drawer)
    check("y ninguna se mete debajo de la contratrabe para llegar",
          "private static double CirculoEnLaFranja(" in drawer
          and "private static double FueraDelBloque(" in drawer)
    # LOS LEADERS AL FRENTE -bring to front-, que es lo que se pidio: la diagonal cruza
    # por detras del propio bloque de texto, y la mascara del MText la borraba.
    check("los leaders se suben al frente, para que se vean enteros",
          "if (lineas.Count > 0)" in drawer
          and "AlFrente(_cont, lineas);" in drawer
          and "private void EncimaDelLeader(" not in drawer)

    # Y VIVEN EN LA CAPA DE LOS ROTULOS, no en una propia: se pidio para las dos hojas.
    # Con capa aparte hay que apagar dos capas para quitar la anotacion de un plano.
    zap_base = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.cs"))
    zap_planta = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.Planta.cs"))

    check("los leaders van en la capa de los rotulos, en las dos hojas",
          "private const string CapaLeader = CapaRotulos;" in zap_base
          and "(CapaRotulos, CapasCad.ColorDeCapa(CapaTextos))," in zap_planta
          and '"LEADER"' not in zap_base)
    # EL TEXTO DE CADA VARILLA DICE SU LECHO, y cuando los dos sentidos llevan lo
    # mismo se rotula una sola vez. Se pidio asi.
    # Y LA PALABRA SE VOLTEA EN LA PARRILLA DE ARRIBA: ahi la de flexion se amarra
    # por el lomo, asi que es la del lecho SUPERIOR y la de temperatura queda debajo.
    check("la varilla de flexion dice INFERIOR y la de temperatura SUPERIOR",
          'private const string SufijoLechoInferior = "INFERIOR";' in drawer
          and 'private const string SufijoLechoSuperior = "SUPERIOR";' in drawer
          and "varBarra, sepBarra, superior ? SufijoLechoSuperior : SufijoLechoInferior);"
          in drawer
          and "varCirc, sepCirc, superior ? SufijoLechoInferior : SufijoLechoSuperior);"
          in drawer)
    check("con el mismo armado en los dos sentidos sale un solo rotulo",
          'private const string SufijoAmbosSentidos = "AMBOS SENTIDOS";' in drawer
          and "var unSoloArmado = MismoArmado(varBarra, sepBarra, varCirc, sepCirc);" in drawer
          and "TextoParrillaCorrida(varBarra, sepBarra, SufijoAmbosSentidos)" in drawer)
    check("y para eso tienen que coincidir la varilla Y la separacion",
          "private bool MismoArmado(" in drawer
          and "SepTexto(sepA).Equals(SepTexto(sepB), StringComparison.OrdinalIgnoreCase)"
          in drawer)

    # EL RENGLON SE MIDE DESDE EL LOMO DEL CONCRETO, NO DESDE LA VARILLA: asi nunca
    # cae dentro de la seccion, con el espesor que sea, y con doble parrilla se sube
    # solo. Era el error que se veia con zapatas de 50 cm.
    check("el rotulo de parrilla sube 10 cm sobre el lomo de la zapata",
          "private const double RotuloParrillaDy = 0.10;" in drawer
          and "var yTexto = a.YZapTop + RotuloParrillaDy;" in drawer)
    # EL ENRASE Y EL MURO DE CONCRETO, DESPEGADOS 6 CM DE SU PANO, Y POR EL LADO DONDE
    # HAY HUECO: la central por la derecha y el lindero por la IZQUIERDA, donde a la
    # derecha esta la colindancia. Medido desde el pano y no desde el eje de la
    # seccion, la separacion es la misma con cualquier espesor.
    check("el rotulo del enrase va a 6 cm del pano, y por el lado con hueco",
          "private const double RotuloEnraseSeparacion = 0.06;" in drawer
          and "var xPano = lindero ? e.XIzq : e.XIzq + e.Ancho;" in drawer
          and "? xPano - RotuloEnraseSeparacion" in drawer
          and ": xPano + RotuloEnraseSeparacion;" in drawer
          and "e.XIzq - 0.3" not in drawer)
    check("y el del muro de concreto, igual, a 6 cm de su pano en las dos hojas",
          "private const double RotuloMuroSeparacion = 0.06;" in drawer
          and "? m.XIzq - RotuloMuroSeparacion" in drawer
          and ": m.XDer + RotuloMuroSeparacion;" in drawer
          and "m.XIzq - 0.27" not in drawer)
    check("en el lindero los tres rotulos de ese lado se despegan lo mismo",
          "? xCtIzq - RotuloMuroSeparacion" in drawer)

    for rotulo in ("RotuloDelEnrase", "RotuloDeLaContratrabe",
                   "RotuloDeLaCadena", "RotuloDelMuroDeConcreto"):
        check(f"esta el rotulo {rotulo}", f"private void {rotulo}(" in drawer)

    check("y los cuatro se cuelgan con leader",
          drawer.count("Leader(") >= 4)

    # Los anchos de renglon son los de las macros, y el MText los respeta: con
    # Width = 0 el rotulo del enrase saldria en una tira que cruza la zapata de al lado.
    for nombre, valor in (("AnchoRotuloEnrase", "0.26"),
                          ("AnchoRotuloContratrabe", "0.23"),
                          ("AnchoRotuloCadena", "0.26"),
                          ("AnchoRotuloMuroCentral", "0.25"),
                          ("AnchoRotuloMuroLindero", "0.25")):
        check(f"el ancho {nombre} vale {valor}",
              f"private const double {nombre} = {valor};" in drawer)

    check("el MText de esta hoja respeta el ancho de renglon",
          "private object? MtextoAncho(" in drawer and "mt.Width = ancho;" in drawer)
    check("y lleva mascara de fondo, para que el terreno no se lea por detras",
          "mt.BackgroundFill = true;" in drawer)

    # El texto del muro de concreto es el de la macro, con sus abreviaturas.
    check("el rotulo del muro dice HORIZ. y VERT. como en la macro",
          "cm HORIZ." in drawer and "cm VERT." in drawer)
    check("y su varilla lleva la C de corrugada, como las de parrilla",
          'var varHoriz = $"VAR {Etiqueta(z.VarMuro)}C";' in drawer
          and 'var varVert = $"VAR {Etiqueta(z.VarMuroVertical)}C";' in drawer)
    check("las dos varillas del muro llevan su numero, tambien la vertical",
          '$"{varVert} @ {SepTexto(z.SepMuroVert)} cm VERT.";' in drawer)
    check("y con el mismo armado en los dos sentidos se escribe una sola vez",
          "var mismoArmado = varHoriz.Equals(varVert, StringComparison.OrdinalIgnoreCase)"
          in drawer
          and "cm {SufijoAmbosSentidos}\"" in drawer)

    # DOS VARILLAS PARA EL MURO: la horizontal -la que se ve de punta- y la vertical, la
    # que arranca de la zapata con su pata. En la hoja de las macros hay UNA sola
    # casilla; se pidio poder elegir las dos. Vacia la vertical, se usa la horizontal.
    check("el muro tiene su varilla vertical, aparte de la horizontal",
          "public string VarMuroVert { get; init; }" in leer(
              ruta("client/src/CadLink.Cad/ZapataCorridaCad.cs"))
          and "public string VarMuroVertical =>" in leer(
              ruta("client/src/CadLink.Cad/ZapataCorridaCad.cs"))
          and "public string VarMuroVert" in leer(
              ruta("client/src/CadLink.App/Models/ZapataCorridaRow.cs")))
    check("y la hoja la deja elegir, con su columna propia",
          'x:Name="ColZapCorVarMuroVert"' in xaml
          and 'Header="Var muro horiz."' in xaml
          and 'Header="Var muro vert."' in xaml
          and "ColZapCorVarMuroVert.ItemsSource = opcionales;" in hoja)
    check("el dibujo usa la vertical para las patas y la horizontal para los circulos",
          "var diamHoriz = Diam(z.VarMuro);" in drawer
          and "var diamVert = Diam(z.VarMuroVertical);" in drawer
          and "VarillaDelMuro(b, diamVert, capaVert, lindero);" in drawer
          and "HatchCirculoVarilla(xc1, y, diamHoriz / 2, capa);" in drawer)

    # LOS CIRCULOS, TANGENTES A LAS VERTICALES. Iban en el eje del acero y las
    # verticales van corridas de ese eje, asi que el circulo caia DENTRO de la vertical.
    trazo_cor = leer(ruta("client/src/CadLink.Cad/TrazoZapataCorrida.cs"))

    check("los circulos del muro quedan tangentes a la varilla vertical",
          "public static double TangenteALaVertical(" in trazo_cor
          and "var sep = (diamCirculo + diamVertical) / 2;" in trazo_cor
          and "var x = xEje <= cerca.X ? cerca.X - sep : cerca.X + sep;" in trazo_cor)
    check("y se queda dentro del muro, para no asomar por la cara",
          "return Math.Clamp(x, m.XIzq + (diamCirculo / 2), m.XDer - (diamCirculo / 2));"
          in trazo_cor)
    check("la geometria es UNA, compartida por el dibujante y la previa",
          "TrazoZapataCorrida.TangenteALaVertical(" in drawer
          and "TrazoZapataCorrida.TangenteALaVertical(" in hoja)
    check("las verticales se calculan ANTES de los circulos, que se apoyan en ellas",
          drawer.index("var barras = Array.Empty<TrazoZapataCorrida.VarillaMuro>();")
          < drawer.index("var ys = TrazoZapataCorrida.CirculosDelMuro("))

    # EL ROTULO DEL MURO DEL LINDERO: 10 cm del pano IZQUIERDO y anclado a la DERECHA.
    # Iba CENTRADO en ese punto, asi que media caja de texto se metia dentro del muro.
    check("el rotulo del muro del lindero se despega 10 cm y crece hacia el terreno",
          "private const double RotuloMuroSeparacionLindero = 0.10;" in drawer
          and "? m.XIzq - RotuloMuroSeparacionLindero" in drawer
          and "var anclaje = lindero ? AnclajeDerecha : AnclajeIzquierda;" in drawer)
    check("la flecha del muro de concreto va a su pano, no a su eje",
          "var xPunta = lindero ? m.XIzq : m.XDer;" in drawer)
    check("y la del muro de enrase, igual",
          "var xPano = lindero ? e.XIzq : e.XIzq + e.Ancho;" in drawer
          and "Leader(xPano, yCentro, xTexto, yTexto);" in drawer)
    check("y el ultimo renglon dice donde va el acero",
          '"DOBLE PARRILLA" : "PARRILLA AL CENTRO"' in drawer)
    check("el rotulo del enrase es el texto de la macro",
          '"MURO DE ENRASE DE BLOCK DE CEMENTO"' in drawer)

    # ------------------------------------------------------------------
    # EL ACERO DEL MURO: relleno SOLO con la seccion rellena
    # ------------------------------------------------------------------
    # Se pidio expresamente, y coincide con las macros: el relleno de varilla es
    # cosa del B3 = 1. En modo normal la varilla va hueca y el rayado del
    # concreto se sigue viendo por detras.
    check("las varillas de punta del muro solo se rellenan con la seccion rellena",
          "if (_relleno)\n        {\n            RellenarCirculo(" in drawer)
    check("y el tramo recto, el codo y la pata, tambien",
          "RellenarVarillaDelMuro(" in drawer
          and drawer.index("if (_relleno)") < drawer.index("RellenarVarillaDelMuro("))
    check("el relleno toma el color de la capa de la varilla",
          '_ = Hatch(borde, "SOLID", 1, capa, 256);' in drawer)

    # LA VARILLA SE RELLENA DE UNA VEZ, con su contorno completo. Antes eran tres
    # trozos -tramo recto, pata y codo- y entre ellos quedaban dos CUNAS sin pintar
    # en la esquina: se veia un triangulo del color del concreto dentro del doblez.
    check("la varilla se rellena con un solo contorno, no en tres trozos",
          "private void RellenarVarillaDelMuro(" in drawer
          and "RellenarTramoDeVarilla(" not in drawer
          and "RellenarCodoDeVarilla(" not in drawer)
    check("y ese contorno recorre las dos caras, los dos arcos y la punta",
          "La cara de DENTRO" in drawer and "El arco INTERIOR" in drawer
          and "El arco EXTERIOR" in drawer and "la cara de FUERA" in drawer)
    check("los extremos del barrido se toman por lo que son, no por su numero",
          "var angRecto = AnguloCodo(sentido, sentido < 0);" in drawer
          and "var angPata = AnguloCodo(sentido, sentido > 0);" in drawer)
    check("el contorno y el relleno del codo usan los mismos angulos",
          "private static double AnguloCodo(" in drawer
          and drawer.count("AnguloCodo(s") >= 2)
    check("y la geometria de ese contorno se comprueba aparte",
          os.path.exists(ruta("tools/verificar_codo_muro.py")))

    # La contratrabe: la flecha a su esquina superior derecha.
    # La flecha, a la esquina superior del lado por el que se cuelga el rotulo: la
    # derecha en la central y la IZQUIERDA en el lindero, donde el texto va a ese lado
    # y apuntando a la derecha la linea cruzaba el bloque de lado a lado.
    check("la flecha de la contratrabe va a su esquina superior, la del lado del rotulo",
          "var xPunta = lindero ? xCtIzq : xCtDer;" in drawer
          and "var yPunta = yCtTop;" in drawer)
    check("y su rotulo se cuelga del lado donde hay sitio",
          "? xCtIzq - RotuloMuroSeparacion" in drawer
          and "xCtDer + RotuloContratrabeDx - RotuloContratrabeCorrimiento;" in drawer)
    check("y en la central se corre 6 cm a la izquierda, para pegarlo mas al bloque",
          "private const double RotuloContratrabeCorrimiento = 0.06;" in drawer)

    # EL TERRENO SE CIÑE A LO QUE SOBRESALE. Antes era un pano recto por lado, y la
    # contratrabe -que es mas ancha que el muro- salia metida en la tierra.
    check("el terreno de la corrida se ciñe a la forma de cada pieza",
          "private void HatchTerrenoCorrida(" in drawer
          and "private readonly record struct ObstaculoTerreno(" in drawer)
    check("y se le pasan las cuatro piezas que pueden sobresalir",
          drawer.count("obstaculos.Add(new ObstaculoTerreno(") == 4)
    check("las bandas se cosen en un solo contorno por lado",
          "private void HatchEscaleraTerreno(" in drawer
          and "HatchPoligono(pts.ToArray(), CapaTerrenoHatch," in drawer)
    check("y ese hatch de contorno libre existe en el dibujante",
          "private object? HatchPoligono(" in leer(
              ruta("client/src/CadLink.Cad/ZapataDrawer.Planta.cs")))
    check("una banda sin pieza hereda el pano de su vecina",
          "izq[i] = izq[i - 1];" in drawer and "izq[i] = izq[i + 1];" in drawer)
    check("y sin ninguna pieza el terreno vuelve a ser un rectangulo",
          "// Ninguna pieza en toda la altura: el terreno es un rectángulo de lado a lado."
          in drawer)

    # La cadena: el texto SIEMPRE despegado de su pano.
    check("el rotulo de la cadena se despega 5 cm de su pano",
          "private const double RotuloCadenaSeparacion = 0.05;" in drawer
          and "var xIns = xCadIzq - RotuloCadenaSeparacion;" in drawer)

    # El codo se dibuja con ARCOS, y con los radios de CADA macro.
    check("el codo de la pata se dibuja con sus dos arcos",
          "Var(Arco(cxIn, cyIn, rIn, AnguloCodo(s, false), AnguloCodo(s, true), capa));"
          in drawer
          and "Var(Arco(cxOut, cyOut, rOut, AnguloCodo(s, false), AnguloCodo(s, true), capa));"
          in drawer)
    check("con los radios propios de cada macro",
          "var rIn = lindero ? diam : diam / 4;" in drawer
          and "var rOut = lindero ? 2 * diam : diam / 2;" in drawer)

    # Las cotas de las patas van FUERA del bloque, como en las macros.
    check("las cotas de las patas se dibujan fuera del bloque",
          "private void CotasDeLasPatasDelMuro(" in drawer
          and drawer.index("_cont = _ms;") < drawer.index("CotasDeLasPatasDelMuro(a, lindero"))
    check("y en el lindero cada pata lleva su propio offset",
          "sep * TrazoZapataCorrida.CotaDoblezLinderoFraccion" in drawer)

    # Ninguna flecha de rotulo entra en la huella de la contratrabe: es el recorte
    # de franja de las dos macros de corrida -«zonaR = xCTL - 0.02»-.
    aislada_drawer = leer(ruta("client/src/CadLink.Cad/ZapataDrawer.cs"))

    check("las puntas de los leaders se recortan al llegar a la contratrabe",
          "double? xTopePuntas = null)" in aislada_drawer
          and "if (xTopePuntas is not null)" in aislada_drawer)
    check("y la punta de la flecha se queda entre las caras de su acero",
          "var xMin = Math.Max(xFranjaMin, p.XCaraIzq + (diam / 2));" in drawer
          and "var xMax = Math.Min(xFranjaMax, p.XCaraDer - (diam / 2));" in drawer)
    check("las aisladas no lo pasan, asi que siguen igual",
          "z.VarInf, z.SepInf, z.VarInfTrans, z.SepInfTrans);" in aislada_drawer)

    # La previa dice la verdad: colorea solo si el juego va relleno.
    check("la vista previa colorea el acero del muro solo con la seccion rellena",
          "var rellenas = ModoElegido == ModoSeccion.Tipo2Rellena;" in hoja
          and "CirculoCorrida(px(xc1), py(y), r, acero, rellenas);" in hoja)


# ======================================================================
# 24. EL REDISENO DE LA INTERFAZ
#
#     Lo que se vigila no es el gusto -eso no se comprueba- sino que el
#     rediseno siga siendo UN SISTEMA y no una capa de pintura: una escala
#     de radios y una fuente en un solo sitio, los tres Border repetidos
#     convertidos en estilos, y sobre todo QUE NO SE HAYA PERDIDO NADA por
#     el camino. Se pidio expresamente que las vistas previas y los botones
#     que uno puede modificar siguieran ahi.
# ======================================================================
def v24_rediseno() -> None:
    print("\n[24] El rediseno de la interfaz")

    tema = leer(ruta("client/src/CadLink.App/Theme/ExcelTabs.xaml"))
    xaml = leer(ruta("client/src/CadLink.App/MainWindow.xaml"))
    temacs = leer(ruta("client/src/CadLink.App/Tema.cs"))

    # ------------------------------------------------------------------
    # La escala: una fuente y cuatro radios, en un solo sitio
    # ------------------------------------------------------------------
    check("la fuente de la interfaz vive en la paleta",
          '<FontFamily x:Key="FuenteUI">' in tema
          and "Segoe UI Variable Text" in tema)

    for radio, valor in (("RadioChico", "4"), ("RadioBoton", "6"),
                         ("RadioTarjeta", "8"), ("RadioPestana", "7,7,0,0")):
        check(f"esta el radio {radio} = {valor}",
              f'<CornerRadius x:Key="{radio}">{valor}</CornerRadius>' in tema)

    check("y los estilos usan la escala, no numeros suyos",
          tema.count("{StaticResource RadioBoton}") >= 2
          and tema.count("{StaticResource RadioTarjeta}") >= 2)

    # ------------------------------------------------------------------
    # Los botones
    # ------------------------------------------------------------------
    check("el boton principal tiene sombra y foco por teclado",
          "DropShadowEffect" in tema
          and 'x:Name="Foco"' in tema
          and 'Property="IsKeyboardFocused" Value="True"' in tema)
    check("y se hunde al pulsarlo",
          '<TranslateTransform Y="1" />' in tema)
    check("el secundario es de contorno, no otro boton solido",
          'x:Key="SecondaryButtonStyle"' in tema
          and '<Setter Property="BorderThickness" Value="1" />' in tema
          and '<Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />' in tema)
    check("los dos son igual de altos, para que la fila se lea como una pieza",
          '<Setter Property="MinHeight" Value="32" />' in tema)

    # ------------------------------------------------------------------
    # La cuadricula
    # ------------------------------------------------------------------
    # La cabecera pasa de 32 a 40: las columnas de parrilla llevan DOS renglones -la
    # banda de la parrilla y el nombre de la columna- y con 32 el segundo se cortaba.
    check("la cuadricula tiene aire: fila de 26 y cabecera de 40",
          '<Setter Property="RowHeight" Value="26" />' in tema
          and '<Setter Property="ColumnHeaderHeight" Value="40" />' in tema)

    # ------------------------------------------------------------------
    # LA CABECERA COMBINADA DE CADA PARRILLA, en las DOS hojas de zapatas
    # ------------------------------------------------------------------
    # WPF no tiene cabeceras combinadas y recorta cada cabecera a SU columna, asi que
    # con las cuatro columnas de antes el titulo salia en trozos: «PARRILL», «IFERIOR».
    # Ahora cada parrilla es UNA sola columna de plantilla de 350 px -115 + 60 + 115 +
    # 60- que se parte en cuatro por dentro: arriba la banda entera y el renglon de los
    # cuatro nombres, y abajo las cuatro casillas de captura.
    for plantilla in ("CabeceraParrillaInferior", "CabeceraParrillaSuperior",
                      "CeldasParrillaInferior", "CeldasParrillaSuperior"):
        check(f"existe la plantilla de parrilla {plantilla}",
              f'x:Key="{plantilla}"' in tema)

    check("las dos hojas usan las DOS columnas de parrilla, con su cabecera y sus celdas",
          xaml.count('HeaderTemplate="{StaticResource CabeceraParrillaInferior}"') == 2
          and xaml.count('HeaderTemplate="{StaticResource CabeceraParrillaSuperior}"') == 2
          and xaml.count('CellTemplate="{StaticResource CeldasParrillaInferior}"') == 2
          and xaml.count('CellTemplate="{StaticResource CeldasParrillaSuperior}"') == 2)

    check("el titulo de cada grupo es el Header de SU columna, una por parrilla y hoja",
          xaml.count('<DataGridTemplateColumn Header="PARRILLA INFERIOR" Width="350"') == 2
          and xaml.count('<DataGridTemplateColumn Header="PARRILLA SUPERIOR" Width="350"') == 2)

    check("y la banda lo pinta centrado sobre las cuatro casillas",
          'x:Key="BandaParrillaStyle"' in tema
          and tema.count('<TextBlock Text="{Binding}" '
                         'Style="{StaticResource BandaParrillaStyle}" />') == 2)

    check("no queda nada de los dos intentos que fallaron",
          all(f'x:Key="CabeceraParrilla{t}"' not in tema
              for t in ("Inf1", "Inf2", "Inf3", "Inf4", "Sup1", "Sup2", "Sup3", "Sup4"))
          and '<Setter Property="Width" Value="350" />' not in tema
          and 'Margin="-298,0,0,0"' not in tema)

    # La cabecera de parrilla va SIN relleno y ESTIRADA, y el ContentPresenter de la
    # cabecera respeta esa alineacion en lugar de centrar a la fuerza: si no, la
    # cabecera se encoge a lo que mide su texto y los cuatro nombres no caen encima de
    # sus cuatro casillas.
    check("la cabecera de parrilla va sin relleno y estirada",
          'x:Key="CabeceraParrillaStyle"' in tema
          and 'BasedOn="{StaticResource EncabezadoHojaStyle}"' in tema
          and '<Setter Property="HorizontalContentAlignment" Value="Stretch" />' in tema
          and xaml.count('HeaderStyle="{StaticResource CabeceraParrillaStyle}"') == 4)
    check("y la cabecera respeta la alineacion de su contenido",
          'HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"' in tema)

    # EL NUMERO DE VARILLA, CON LA TINTA DE LA CUADRICULA Y NO EN GRIS. El Foreground del
    # ComboBox no basta: el estilo de Windows pinta el valor elegido con su plantilla y ahi
    # el color del control no siempre llega. Puesto en la plantilla del renglon si manda.
    check("el numero de varilla se ve con la tinta de la cuadricula, no en gris",
          'x:Key="ComboSubCeldaStyle"' in tema
          and '<Setter Property="Foreground" Value="{DynamicResource GridTextBrush}" />' in tema
          and '<Setter Property="ItemTemplate">' in tema
          and tema.count('<TextBlock Text="{Binding}"\n                               '
                         'Foreground="{DynamicResource GridTextBrush}" />') == 1)

    # Las cuatro casillas y los cuatro nombres, con el MISMO reparto de ancho en
    # estrella: asi siguen cuadrados aunque se cambie el ancho de la columna.
    check("los cuatro nombres y las cuatro casillas llevan el mismo reparto de ancho",
          tema.count('<ColumnDefinition Width="115*" />') == 8
          and tema.count('<ColumnDefinition Width="60*" />') == 8)

    # LOS NOMBRES DE COLUMNA, los que se pidieron: dicen el LECHO y el TRABAJO de cada
    # varilla, y coinciden con lo que sale rotulado en el plano. Van UNA vez, en la
    # plantilla, porque las dos hojas usan la misma.
    for nombre in ("Var Inf. Flexión", "Var. Sup. Temp.",
                   "Var Sup. Flexión", "Var. Inf. Temp."):
        check(f"la plantilla tiene el nombre {nombre}",
              tema.count(f'Text="{nombre}"') == 1)

    check('y las cuatro casillas de separacion dicen «@ cm»',
          tema.count('Text="@ cm"') == 4)

    check("y no queda ningun nombre viejo de columna de parrilla",
          'Header="Var inf."' not in xaml
          and 'Header="Var sup."' not in xaml
          and 'Header="Var inf. trans."' not in xaml
          and 'Header="Var sup. trans."' not in xaml
          and 'Header="Var Inf. Flexión"' not in xaml)

    # LAS LISTAS DE LAS SUB-CASILLAS. Una celda de plantilla no tiene x:Name -se crea
    # una por fila-, asi que los numeros de varilla se atacan con x:Static, y salen de
    # la MISMA tabla de diametros: Varilla.DiametrosCm.
    modelos = leer(ruta("client/src/CadLink.App/Models/StructuralRows.cs"))
    check("los numeros de varilla se pueden atacar desde el XAML",
          "public static readonly string[] Diametros = DiametrosCm.Keys.ToArray();" in modelos
          and "public static readonly string[] DiametrosOpcionales =" in modelos
          and "new[] { string.Empty }.Concat(Diametros).ToArray();" in modelos)
    check("la parrilla inferior las pide obligatorias y la superior opcionales",
          tema.count('{Binding Source={x:Static models:Varilla.Diametros}}') == 2
          and tema.count('{Binding Source={x:Static models:Varilla.DiametrosOpcionales}}') == 2)
    check("y ya no se rellenan por codigo, que con la columna de plantilla no se puede",
          all(f"{c}.ItemsSource" not in leer(ruta(f"client/src/CadLink.App/{f}"))
              for f, cs in (("MainWindow.ZapatasCorridas.cs",
                             ("ColZapCorVarInf", "ColZapCorVarInfT",
                              "ColZapCorVarSup", "ColZapCorVarSupT")),
                            ("MainWindow.Zapatas.cs",
                             ("ColZapVarInf", "ColZapVarInfT",
                              "ColZapVarSup", "ColZapVarSupT")))
              for c in cs))

    # Las cuatro casillas de captura de cada parrilla, con su enlace
    for prop in ("VarInf", "SepInf", "VarInfTrans", "SepInfTrans",
                 "VarSup", "SepSup", "VarSupTrans", "SepSupTrans"):
        check(f"la casilla de {prop} esta enlazada",
              f'{prop}, UpdateSourceTrigger=PropertyChanged}}' in tema)

    # LAS CASILLAS DE LA PARRILLA SUPERIOR, APAGADAS SI NO HAY DOBLE PARRILLA. Ahora es
    # UNA columna por hoja, y al apagar la celda se apagan sus cuatro casillas de dentro,
    # porque IsEnabled baja por el arbol. Las dos filas tienen EsDobleParrilla.
    check("las casillas de la parrilla superior se apagan sin doble parrilla",
          'x:Key="CeldaSoloDobleParrilla"' in tema
          and '<Setter Property="IsEnabled" Value="{Binding EsDobleParrilla}" />' in tema
          and 'x:Key="CeldaParrillaSup"' in tema
          and 'BasedOn="{StaticResource CeldaSoloDobleParrilla}"' in tema
          and xaml.count('CellStyle="{StaticResource CeldaParrillaSup}"') == 2)
    check("y la fila avisa del cambio, para que se enciendan al poner SI",
          all("Raise(nameof(EsDobleParrilla));" in leer(ruta(f"client/src/CadLink.App/Models/{m}"))
              for m in ("ZapataCorridaRow.cs", "ZapataAisladaRow.cs")))
    check("la seleccion usa el azul del programa y no el del sistema",
          '<Setter Property="Background" Value="{DynamicResource SelectionBrush}" />' in tema)
    check("y la cabecera se cierra con la linea de marca",
          "La LINEA DE MARCA que cierra la cabecera" in tema)

    # ------------------------------------------------------------------
    # Las tres piezas que antes se escribian Border por Border
    # ------------------------------------------------------------------
    for estilo in ("TarjetaStyle", "MarcoPreviaStyle", "BarraTotalesStyle",
                   "TextoTotalesStyle"):
        check(f"existe el estilo {estilo}", f'x:Key="{estilo}"' in tema)

    # LA BARRA DE ARRIBA, SIN NUEVO / ABRIR / GUARDAR / GUARDAR COMO: se pidio dejarlos
    # solo en el menu Archivo, donde estan con su atajo. Los comandos siguen siendo los
    # mismos -ApplicationCommands- asi que los atajos funcionan igual.
    check("la barra ya no repite los botones de archivo",
          'Content="Nuevo" Style="{StaticResource ToolbarButtonStyle}"' not in xaml
          and 'Content="Abrir" Style="{StaticResource ToolbarButtonStyle}"' not in xaml
          and 'Content="Guardar" Style="{StaticResource ToolbarPrimaryButtonStyle}"' not in xaml
          and 'Content="Guardar como..."' not in xaml)
    check("pero siguen en el menu Archivo, con su atajo",
          all(f'Command="ApplicationCommands.{c}"' in xaml
              for c in ("New", "Open", "Save", "SaveAs"))
          and 'InputGestureText="Ctrl+N"' in xaml
          and 'InputGestureText="Ctrl+Mayus+G"' in xaml)
    check("y lo que se usa de verdad sigue en la barra",
          'x:Name="DeshacerButton"' in xaml
          and 'Click="OnValidate"' in xaml
          and 'x:Name="TemaButton"' in xaml)

    # Y la tarjeta de la hoja de acero, con UN solo renglon: los otros dos ocupaban alto de
    # la tabla para explicar lo que ya dice cada columna.
    check("la tarjeta de la hoja de acero deja un solo renglon",
          'Text="Las doce familias:"' in xaml
          and "Que columna usa cada una" not in xaml
          and "Al final de la tabla" not in xaml)

    check("los marcos de vista previa usan su estilo",
          xaml.count('Style="{StaticResource MarcoPreviaStyle}"') >= 4)
    check("las barras de totales tambien",
          xaml.count('Style="{StaticResource BarraTotalesStyle}"') >= 4)

    # ------------------------------------------------------------------
    # NO SE PERDIO NADA: las previas y los botones siguen ahi
    # ------------------------------------------------------------------
    # Es la condicion que se puso al pedir el rediseno, y la que un cambio de
    # estilos puede romper sin que nadie lo note hasta abrir la ventana.
    for lienzo in ("PreviewCanvas", "AceroPreviewCanvas",
                   "ZapataPreviewCanvas", "ZapataCorridaPreviewCanvas"):
        check(f"sigue el lienzo {lienzo}", f'x:Name="{lienzo}"' in xaml)

    check("los cuatro lienzos siguen sobre papel claro",
          xaml.count('Background="{StaticResource PreviewFondoBrush}"') >= 4)

    for boton in ("DibujarZapatasButton", "DibujarZapatasCorridasButton",
                  "TemaButton"):
        check(f"sigue el boton {boton}", f'x:Name="{boton}"' in xaml)

    for handler in ("OnExportZapatas", "OnExportZapatasCorridas",
                    "OnRevisarZapatas", "OnRevisarZapatasCorridas"):
        check(f"sigue enganchado {handler}", f'Click="{handler}"' in xaml)

    check("siguen los radios de estilo de seccion de las tres hojas",
          xaml.count('GroupName="TipoSeccion') >= 3)

    # ------------------------------------------------------------------
    # El tema oscuro sabe de las brochas nuevas
    # ------------------------------------------------------------------
    # Si una brocha nueva no esta en los dos diccionarios, al cambiar de tema se
    # queda con el color del otro: es como se ve un boton azul claro sobre fondo
    # negro. Se comprueba que las tres esten DOS veces, una por tema.
    # ------------------------------------------------------------------
    # LOS DOS ERRORES DEL REDISENO EN TEMA OSCURO
    # ------------------------------------------------------------------
    # 1) La CUADRICULA se queda clara en los dos temas -sus pasteles de columna
    #    son la referencia del usuario-, asi que su texto NO puede seguir al
    #    TextBrush: en oscuro salia claro sobre celda clara y solo se leia la
    #    fila seleccionada.
    check("la cuadricula tiene su propia tinta, oscura en los dos temas",
          'x:Key="GridTextBrush"' in tema
          and temacs.count('["GridTextBrush"]') == 2)
    check("y la usan la cuadricula, sus celdas y sus combos",
          tema.count("{DynamicResource GridTextBrush}") >= 4)
    # Se mira DENTRO del estilo de la cuadricula: los TextBox y ComboBox de
    # formulario si tienen que seguir al tema -viven sobre las tarjetas, que se
    # oscurecen-, asi que buscar la cadena en todo el archivo daba un falso fallo.
    m_grid = re.search(r'Style x:Key="SheetGridStyle".*?</Style>', tema, re.S)

    check("el estilo de la cuadricula usa la tinta propia y no la del tema",
          m_grid is not None
          and "{DynamicResource GridTextBrush}" in m_grid.group(0)
          and '"Foreground" Value="{DynamicResource TextBrush}"' not in m_grid.group(0))

    # 1 bis) LAS LISTAS DESPLEGABLES. Mismo caso que las tablas, y con una vuelta de
    #    tuerca: los renglones se pintaban con GridRowBrush creyendo que era fija, y NO
    #    lo es -en oscuro se va a un gris #4A4A4A-. Con la tinta casi negra encima, la
    #    letra se leia «un poco oscura». Ahora las listas llevan sus CUATRO brochas
    #    propias, que no estan en ninguna paleta y por tanto no las toca el tema.
    check("los renglones de las listas llevan su fondo y su tinta, fijos",
          '<Style TargetType="ComboBoxItem">' in tema
          and '<Setter Property="Background" Value="{StaticResource ListaFondoBrush}" />' in tema
          and '<Setter Property="Foreground" Value="{StaticResource ListaTextoBrush}" />' in tema)
    check("y esas brochas de lista no las cambia ningun tema",
          all(f'x:Key="{b}"' in tema for b in
              ("ListaFondoBrush", "ListaTextoBrush", "ListaResalteBrush", "ListaApagadaBrush"))
          and all(f'["{b}"]' not in temacs for b in
                  ("ListaFondoBrush", "ListaTextoBrush", "ListaResalteBrush",
                   "ListaApagadaBrush")))
    check("y se resaltan con un azul que se lee sobre el papel claro",
          'Value="{StaticResource ListaResalteBrush}" />' in tema)
    check("las listas normales, igual",
          '<Style TargetType="ListBoxItem">' in tema)

    # 1 ter) LA CELDA DE LISTA SIN EDITAR. Le faltaba estilo, asi que se dibujaba con
    #    el ComboBox general: en oscuro el valor de la celda -«CENTRAL»- salia en
    #    letra clara sobre el color claro de la columna.
    check("la celda de lista tiene estilo tambien cuando NO se esta editando",
          '<Style x:Key="ComboCeldaMuestra" TargetType="ComboBox">' in tema
          and '<Setter Property="Foreground" Value="{StaticResource ListaTextoBrush}" />' in tema)
    check("y todas las columnas de lista lo usan",
          xaml.count('ElementStyle="{StaticResource ComboCeldaMuestra}"')
          == xaml.count('EditingElementStyle="{StaticResource ComboCeldaEdicion}"') - 1
          and 'BasedOn="{StaticResource ComboCeldaMuestra}"' in xaml)

    # 2) El MENU lo pintaba WINDOWS: su Popup salia con marco claro -la linea
    #    blanca- porque un Background en el MenuItem no alcanza para el marco.
    check("el menu tiene plantilla propia, con su marco y su sombra",
          'x:Key="MenuTituloTemplate"' in tema
          and 'x:Key="MenuOpcionTemplate"' in tema
          and 'x:Key="MenuSubmenuTemplate"' in tema)
    check("y estan los CUATRO roles, o el que falte vuelve al de Windows",
          all(f'<Trigger Property="Role" Value="{rol}">' in tema
              for rol in ("TopLevelHeader", "TopLevelItem",
                          "SubmenuHeader", "SubmenuItem")))
    check("el marco del menu sale de la paleta",
          'Background="{DynamicResource SurfaceBrush}"' in tema
          and 'BorderBrush="{DynamicResource BorderBrush}"' in tema)
    check("los atajos del menu se siguen viendo",
          'Text="{TemplateBinding InputGestureText}"' in tema)
    check("y la rayita que separa grupos tambien es nuestra",
          '<Style TargetType="Separator">' in tema)

    # ------------------------------------------------------------------
    # EL ESPACIO DE LAS HOJAS
    # ------------------------------------------------------------------
    # La descripcion de cada hoja se comia 40 px de alto en once pestanas. Se
    # paso al GLOBO del titulo: sigue estando, pero no ocupa.
    check("las hojas ya no llevan la descripcion al inicio",
          xaml.count('Style="{StaticResource ModuleSubtitleStyle}"') <= 2)
    check("y la explicacion sigue accesible en el globo del titulo",
          xaml.count('Style="{StaticResource ModuleTitleStyle}"\n'
                     '                                   ToolTip="') >= 8)

    # El alto de fila se queda en 26, como se pidio.
    check("el alto de fila sigue en 26",
          '<Setter Property="RowHeight" Value="26" />' in tema)

    for brocha in ("SelectionBrush", "FocoBrush", "SombraBrush"):
        check(f"{brocha} esta en los dos temas",
              temacs.count(f'["{brocha}"]') == 2)
        check(f"y {brocha} esta declarada en la paleta",
              f'x:Key="{brocha}"' in tema)

if __name__ == "__main__":
    sys.exit(main())
