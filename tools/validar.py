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

    check(
        "orden del pintor de lejos a cerca",
        "OrderByDescending" in t and re.search(r"OrderBy\(t => t\.Prof\)", t) is None,
    )

    # La vista en planta dedicada invierte la Y, porque la del lienzo crece
    # hacia abajo y la del modelo hacia arriba
    check(
        "la planta invierte la Y",
        re.search(r"\(h / 2\) - \(\(y - cy\) \* escala\)", t) is not None,
    )

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

    for dentro in ["DADO", "CASTILLO", "TRABE", "CONTRATRABE",
                   "CADENA DE CERRAMIENTO", "CADENA DE DESPLANTE"]:
        check(f"con {dentro} en la lista", f'"{dentro}"' in lista)

    # Y los dos que van por constante
    check("con COLUMNA en la lista",
          "SeccionConcretoRow.ElementoColumna," in lista)
    check("con COLUMNA CIRCULAR en la lista",
          "SeccionConcretoRow.ElementoColumnaCircular" in lista)

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
    check(
        "el radio se protege contra tangente inexistente",
        "Math.Clamp(cc, -0.999999, 0.999999)" in dia,
    )
    check(
        "dos circulos coincidentes no producen NaN",
        re.search(r"if \(d < 1e-7\)\s*\{\s*return null;", dia) is not None,
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
    check(
        "las fronteras de zona solo en el alzado horizontal",
        "conFronteras: !vertical" in est,
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
    # 0.10: el hueco sobre la seccion ya solo carga el CORTE A-A'. Valio 0.46 mientras
    # el rotulo colgaba del pie del alzado; al mover el rotulo bajo el bloque de la
    # SECCION esos 46 cm sobraban y dejaban media banda vacia entre las dos filas.
    check("hay una constante para el aire sobre la seccion",
          "public const double AireRotuloAlzado = 0.10;" in lay)
    check("y la cuenta del aire es la del CORTE A-A'",
          "CORTE A-A'" in lay)

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
          "RotuloCorte(x + (ancho / 2), y + alto);" in alz2)

    check("hay comprobacion de la colocacion contra el VBA",
          os.path.exists(ruta("tools/verificar_layout_alzados.py")))

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

    # La pestaña y el boton.
    check("la pestaña dice ETABS/SAP2000", 'Header="ETABS/SAP2000"' in xaml)
    check("hay boton para leer el modelo de SAP2000",
          'Click="OnImportSap2000"' in xaml)
    check("y su manejador existe", "private void OnImportSap2000(" in codigo)

    # UN solo lector para los dos, o un arreglo entraria en uno y no en el otro.
    check("los dos botones usan el mismo lector",
          "LeerModeloCsi(EtabsConnection.ProgramaCsi.Etabs)" in codigo
          and "LeerModeloCsi(EtabsConnection.ProgramaCsi.Sap2000)" in codigo)

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
    check("el doblez lateral puede ser la varilla",
          "private List<(double X, double Y, double R)> DoblezLateral(" in diamante)
    n_dob = len(re.findall(r"DoblezLateral\(derecha:", diamante))
    check("los DOS dobleces usan la varilla", n_dob == 2, f"solo {n_dob}")

    m_dob = re.search(
        r"private List<\(double X, double Y, double R\)> DoblezLateral\(.*?\n    \}",
        diamante, re.S)
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
    check("existe la red de seguridad", "private List<(double X, double Y, double R)> RodearLaterales(" in diamante)
    check("y se llama", "centros = RodearLaterales(centros, dDia);" in diamante)

    m_rod = re.search(
        r"private List<\(double X, double Y, double R\)> RodearLaterales\(.*?\n    \}",
        diamante, re.S)
    check("se puede leer RodearLaterales", m_rod is not None)

    if m_rod:
        cuerpo = m_rod.group(0)
        # Varias pasadas: rodear una varilla empuja la cinta y puede cruzar otra.
        check("da varias pasadas", "PasadasRodeo" in cuerpo)
        # Se mira contra las DOS fronteras de la cinta.
        check("mira las dos fronteras de la cinta",
              "GeometriaCinta(actual, 0)" in cuerpo and "GeometriaCinta(actual, dDia)" in cuerpo)
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
        r"private static double DistanciaASegmento\(.*?\n    \}", diamante, re.S)
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
    check("se puede girar la vista extruida",
          m_gira is not None and "ExtruidaCanvas" in m_gira.group(0))

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

    # El pintor va por CARA, no por elemento: una trabe que cruza una columna tiene
    # caras delante y detras de ella a la vez.
    check("el orden del pintor es por cara",
          "caras.OrderByDescending(c => c.Profundidad)" in ext)
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

    m_vis = re.search(r"private bool VisibleEnElPlano\(.*?\n    \}", codigo, re.S)
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
              "planta.Elementos.Count == 0" in cuerpo)
        check("se tolera que AutoCAD no este abierto",
              "AcadNotAvailableException" in cuerpo)
        check("y el cursor de espera se repone siempre",
              "finally" in cuerpo and "Cursor = Cursors.Arrow;" in cuerpo)

    # ------------------------------------------------------------------
    # El dibujante
    # ------------------------------------------------------------------
    dib = leer(ruta("client/src/CadLink.Cad/PlantaDrawer.cs"))
    dto = leer(ruta("client/src/CadLink.Cad/PlantaCad.cs"))

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

    # Una capa por tipo de elemento: es lo que se usa para trabajar encima.
    for capa in ("PLANTA-COLUMNAS", "PLANTA-TRABES", "PLANTA-MUROS",
                 "PLANTA-LOSAS", "PLANTA-EJES", "PLANTA-TEXTOS"):
        check(f"hay capa {capa}", f'"{capa}"' in dib)

    # Las capas que ya existen se dejan como estan: pueden llevar el color y la
    # pluma que les puso el usuario.
    m_cap = re.search(r"public void AsegurarCapas\(\).*?\n    \}", dib, re.S)
    check("se puede leer AsegurarCapas", m_cap is not None)
    if m_cap:
        check("una capa que ya existe no se toca",
              "todas.Item(nombre)" in m_cap.group(0))

    # Las losas ANTES que trabes y columnas: en AutoCAD el orden de creacion es el
    # orden de dibujo, asi que si se dibujaran al final taparian el resto.
    m_dib = re.search(r"public Resumen Dibujar\(.*?\n    \}", dib, re.S)
    check("se puede leer Dibujar", m_dib is not None)
    if m_dib:
        cuerpo = m_dib.group(0)
        i_losa = cuerpo.find("ClasePlanta.Losa")
        i_col = cuerpo.find("ClasePlanta.Columna")
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

    # Lo que el modelo no dio se AVISA, no se calla: hay que saberlo antes de acotar.
    m_esp = re.search(r"private double Espesor\(.*?\n    \}", dib, re.S)
    check("se puede leer Espesor", m_esp is not None)
    if m_esp:
        check("una medida que falta se avisa", "_log.Add(" in m_esp.group(0))

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
              'CornerRadius="4,4,0,0"' in m_item.group(0))

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
          "public string ElementoRotulo =>" in filas)
    m_er = re.search(r"public string ElementoRotulo =>.*?;", filas, re.S)
    if m_er:
        check("el rotulo de una columna redonda es COLUMNA",
              "ElementoColumna :" in m_er.group(0))
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

    # El cuadro de notas es una capa sobre la vista previa: tapaba el dibujo.
    check("el cuadro de notas se oculta cuando no hay nada que decir",
          'x:Name="NotasPanel"' in xaml
          and 'Binding="{Binding Text, ElementName=ExportHintText}"' in xaml)

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

        # Y la LINEA interior de las colas se va por lo mismo que el arco interior: nace
        # pegada al acero de la varilla, cuya circunferencia ya esta dibujada.
        check("ni la linea interior de las colas",
              "sinLineaInterior: true" in cuerpo)
        check("pero el doblez si se rellena",
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

        # Y debajo del brazo de arriba se ABRE la linea interior de la cinta, que si no le
        # cruzaba por dentro: en el plano parecia que la diagonal cortaba el gancho.
        check("la cinta se abre bajo el brazo de arriba",
              "AbrirCintaBajoLaCola(" in cuerpo)
        check("la cinta vieja se borra solo si la nueva se creo",
              "if (cintaAbierta is not null)" in cuerpo
              and "Borrar(_diamInt);" in cuerpo
              and "_diamInt = cintaAbierta;" in cuerpo)

        # Y la cola se recorta si no cabe en el nucleo.
        check("la cola del diamante se recorta si no cabe",
              "gancho = tope;" in cuerpo)

    m_abrir = re.search(r"private object\? AbrirCintaBajoLaCola\(.*?\n    \}", diam, re.S)

    check("se puede leer AbrirCintaBajoLaCola", m_abrir is not None)
    if m_abrir:
        abr = m_abrir.group(0)

        # Se mira lo que tapa el gancho, que son DOS piezas. Con solo la cola, el hueco
        # empezaba en la perpendicular a la varilla y dejaba un rabito de linea justo
        # encima de ella; y en una columna alta la cola no llega a cruzar la diagonal, asi
        # que no se abria nada aunque el doblez la tapara igual.
        check("el hueco mira lo que tapan la cola Y el doblez",
              "RecorteDeLaCola(" in abr and "RecorteDelDoblez(" in abr)
        check("y si el gancho no tapa nada, no abre nada",
              "pieza1 is null && pieza2 is null" in abr)

        # El hueco lleva tope, como el recorte del estribo: mas vale una linea cruzando
        # que media diagonal borrada.
        check("el hueco lleva tope de seguridad",
              "FraccionMaxHuecoCinta" in abr and "no se abrió la línea interior" in abr)
        check("y la cinta se vuelve a montar con TODOS sus vertices",
              "for (var k = 1; k <= m; k++)" in abr
              and "nuevosBulges.Add(bulges[v]);" in abr)
        check("la cinta nueva va abierta, no cerrada",
              "PolilineaAbierta(" in abr and "pl.Closed = false;" in diam)

    check("el recorte contra el rectangulo de la cola son cuatro semiplanos",
          "private static (double S0, double S1)? RecorteDeLaCola(" in diam
          and "Nx: -ux, Ny: -uy" in diam)
    check("y el del doblez es el disco de rOut mas la media vuelta",
          "private static (double S0, double S1)? RecorteDelDoblez(" in diam
          and "(fx * ux) + (fy * uy)" in diam)

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

    check("el recorte, el arco y el hueco usan el mismo tramo",
          diam.count("TramoDeLaCinta(") >= 4)

    # La Cola compartida con el estribo rectangular tiene que saber saltarse la linea
    # interior, y SOLO esa: el gancho del rectangular la sigue dibujando.
    dib = leer(ruta("client/src/CadLink.Cad/SeccionDrawer.cs"))

    check("la Cola compartida admite dibujarse sin la linea interior",
          "bool sinLineaInterior = false)" in dib)
    check("y lo que se salta es SOLO la linea interior",
          "if (!sinLineaInterior)\n        {\n            Agregar(contorno, "
          "Linea(piX, piY, qiX, qiY, \"ESTRIBOS\"));" in dib)
    check("el gancho del rectangular sigue con su linea interior",
          "Cola(contorno, quads, bx, by, rIn, rOut, Rt2I, -Rt2I, ux, uy, gancho, "
          "false, 0, 0);" in dib)

    check("hay comprobacion numerica de la direccion de la cola del diamante",
          "Direccion de la cola del gancho del diamante"
          in leer(ruta("tools/verificar_gancho_diamante.py")))
    check("y de que ninguna linea del gancho queda dentro del acero del diamante",
          "NINGUNA LINEA DEL GANCHO DEBE QUEDAR DENTRO DEL ACERO"
          in leer(ruta("tools/verificar_gancho_diamante.py")))

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
        aparte = {"PreviewFondoBrush"} | {b for b in declaradas if b.startswith("Celda")}

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
    check("y las tarjetas tambien, que estaban repetidas once veces",
          xaml.count('Background="{DynamicResource CardBrush}"') >= 10)
    check("las brochas del tema se referencian con DynamicResource",
          xaml.count("{DynamicResource") > 50)
    check("y el tema sabe sustituir la brocha si esta congelada",
          "recursos[clave] = new SolidColorBrush(color);" in temacs)
    check("los menus tambien siguen el tema",
          '<Style TargetType="Menu">' in tema
          and '<Style TargetType="MenuItem">' in tema)

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

    # Y el acero transversal se llama por su nombre: zuncho, no estribo.
    check("hay texto propio del acero transversal",
          "private static string TextoTransversal(" in alz)
    m_tt = re.search(r"private static string TextoTransversal\(.*?\n    \}", alz, re.S)
    if m_tt:
        cuerpo = m_tt.group(0)
        check("en la circular dice Zuncho y no Est.", '"Zuncho ' in cuerpo)
        check("y distingue helice de anillos",
              "helic." in cuerpo and "anillos" in cuerpo)

    # Lo usan los DOS alzados, el vertical y el horizontal.
    check("los dos alzados usan el texto transversal",
          alz.count("TextoTransversal(a, s[i])") == 2,
          f"lo usan {alz.count('TextoTransversal(a, s[i])')} vez/veces")
    check("y ya no queda el texto fijo de estribo",
          'Est. {a.Estribo.Clave} @' not in alz)


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

    for p in rutas:
        texto = _sin_comentarios(leer(p))
        lineas = texto.split("\n")
        pilas = clases_por_archivo[p]

        for i, l in enumerate(lineas):
            # La PILA entera, no solo la clase mas interna: desde una clase anidada
            # se ven los miembros de la que la contiene sin cualificar nada.
            pila = pilas[i] if i < len(pilas) else []

            for miembro, duena in declarados.items():
                # Uso, no declaracion
                if re.search(r"^\s*public\s.*\b" + miembro + r"\b\s*(?:=|\()", l):
                    continue

                for m in re.finditer(r"(\.)?\b" + miembro + r"\b", l):
                    if m.group(1):
                        continue        # ya viene cualificado con algo

                    if duena == "" or duena in pila:
                        continue        # esta en su clase o en una que la contiene

                    # Alguna clase del ambito tiene un miembro propio con ese
                    # nombre: el identificador se resuelve a ESE.
                    if any(miembro in miembros_de.get(c, set()) for c in pila):
                        continue

                    aqui = pila[-1] if pila else ""

                    # Nombre de propiedad dentro de un inicializador de objeto:
                    # 'Elemento = ...' se resuelve contra el tipo que se construye.
                    if re.match(r"\s*" + miembro + r"\s*=[^=]", l):
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
              v21_separacion_y_acero):
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
    check("el doblez lateral admite dos varillas",
          "private List<(double X, double Y, double R)> DoblezLateral(" in diamante)
    check("se mide sobre la Y en los costados", "porY: true" in diamante)
    check("VarillasDelCentro sabe medir por Y", "bool porY = false" in diamante)
    # Los dos costados se agregan con AddRange: si uno usara Add, una seleccion de
    # dos varillas no cabria y la lista quedaria mal.
    n_ar = len(re.findall(r"centros\.AddRange\(DoblezLateral\(", diamante))
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

    for sep in ("6-12-6", "7-14-4", "15"):
        check(f"esta la separacion {sep}", f'"{sep}"' in filas)

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
    # Las cuatro familias de perfil
    # ------------------------------------------------------------------
    for fam in ("IR", "OR", "OC", "CF"):
        check(f"existe la familia {fam}", f'= "{fam}";' in perfil_row)

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
    check("el OC no pide ancho, que es redondo",
          "if (_familia != FamiliaPerfil.Oc && _anchoCm <= 0)" in perfil_row)
    check("el labio solo lo pide el CF",
          "_familia == FamiliaPerfil.Cf && _labioCm <= 0" in perfil_row)

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
        check("y dice que columna usa cada familia",
              "el peralte es el DIAMETRO" in tab)

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
    check("con sus cuatro familias",
          all(f'case "{f}":' in acero_cad for f in ("IR", "OR", "OC", "CF")))
    check("una familia desconocida se avisa, no se dibuja mal",
          "no se reconoce" in acero_cad)

    # El DTO no interpreta nada: llega todo resuelto.
    check("el DTO lleva el ancho que ocupa el dibujo",
          "public double AnchoDibujoCm" in perfil_cad)

    # Los rayados de cada familia, tal como los dejaron las macros.
    check("el IR se raya con ANSI32 a 0.0009 en color 252",
          'Hatch("ANSI32", 0.0009 * _f, pl, null, CapaPerfiles, 252)' in acero_cad)
    check("el OC va con solido y rayado en 162",
          'Hatch("SOLID", 1, exterior, islas, CapaPerfiles, 162)' in acero_cad
          and 'Hatch("ANSI31", 0.002 * _f, exterior, islas, CapaPerfiles, 162)'
          in acero_cad)
    check("el CF va con fondo cian y rayado 142 a 0.0008",
          'Hatch("SOLID", 1, pl, null, CapaPerfiles, 4)' in acero_cad
          and 'Hatch("ANSI31", 0.0008 * _f, pl, null, CapaPerfiles, 142)' in acero_cad)

    # El OR cambia de rayado segun el peralte, y ese corte esta en la macro.
    check("el OR decide el rayado por el peralte en pulgadas",
          "var peralteIn = p.PeralteCm / 2.54;" in acero_cad
          and "PeralteLimitePulg - 0.01" in acero_cad)
    check("el tubo grande se rellena solido",
          'Hatch("SOLID", 1, exterior, islas, CapaPerfiles, 141)' in acero_cad)
    check("y el chico lleva fondo cian", "FondoDelHatch(trama, 4);" in acero_cad)

    # El color de fondo de un hatch no es un numero, es un objeto que hay que pedir por
    # su ProgID con la version pegada.
    check("el fondo del hatch prueba varias versiones de AutoCAD",
          '"AutoCAD.AcCmColor." + v' in acero_cad)

    # Los radios del CF se recortan a lo que cabe, como en la macro.
    check("el radio exterior del CF se recorta",
          "var rExt = Math.Min(ri, Math.Min(b / 2, Math.Min(lip, h / 2)));" in acero_cad)
    check("y el interior es la mitad, recortada por su cuenta",
          "var rInt = Math.Min(ri / 2, rIntMax);" in acero_cad)

    # El peralte del OR es el lado mayor: un tubo capturado al reves es el mismo tubo.
    check("el peralte del OR es el lado mayor",
          "var hOr = Math.Max(b, h);" in acero_cad)

    # LO QUE MAS IMPORTA DE LAS COTAS: el factor de escala lineal. El dibujo esta en
    # metros, asi que sin el la cota de un peralte de 30 cm diria «0.30» en un plano
    # rotulado «Acot. cm». Las cuatro macros lo fijan en 100, que es 1/escala.
    check("las cotas de acero llevan el factor de escala lineal",
          'PropCota((object)cota, "LinearScaleFactor", 1 / _escala);' in acero_cad)

    # Y el CF se dibuja con UNA polilinea, no con el contorno mas otra igual para el
    # hatch, que es lo que hacia la macro.
    check("el CF se dibuja con una sola polilinea con dobleces",
          "PolilineaConBulges(pts, lista, CapaPerfiles)" in acero_cad)
    check("el bulge sale del barrido real, asi el espejo se resuelve solo",
          "private static double BulgeDesdeCentro(" in acero_cad)

    check("hay comprobacion numerica de los cuatro perfiles",
          "CF: la canal formada en frio"
          in leer(ruta("tools/verificar_perfiles_acero.py")))

    # ------------------------------------------------------------------
    # El acero se dibuja A LA IZQUIERDA del origen, desde -0.6
    # ------------------------------------------------------------------
    # Es el xDerechaActual = -0.6 de las macros. Y no es solo acomodo: el concreto
    # crece hacia la derecha desde donde acabe lo que ya haya, asi que con el acero
    # en el semiplano negativo las dos hojas no se pisan nunca.
    check("el acero empieza en -60 cm, el -0.6 de las macros",
          "OrigenAceroCm = -60" in acero_cb)
    check("y crece hacia la izquierda",
          "var xIzquierda = xDerecha - ancho;" in acero_cb)
    check("ya no arranca donde acabe el concreto",
          "dibujante.PosicionInicialX()" not in acero_cb)

    # El hueco se avanza tambien para los saltados: si no, al redibujar una hoja con
    # dos perfiles ya hechos, los otros dos caerian justo encima de ellos.
    m_export = re.search(
        r"private void OnExportAcero\(.*?\n    \}", acero_cb, re.S)

    check("se puede leer OnExportAcero", m_export is not None)
    if m_export:
        cuerpo_exp = m_export.group(0)

        check("el hueco se avanza siempre, tambien para los saltados",
              "xDerecha = xIzquierda - aire;" in cuerpo_exp
              and cuerpo_exp.count("xDerecha = xIzquierda") == 1)
        check("y el saltado solo se descuenta del conteo",
              "if (dibujante.Saltadas.Count == saltadasAntes)" in cuerpo_exp)

        # CADA FAMILIA EN SU BANDA. Las cuatro macros arrancan en la misma x, asi que lo
        # unico que evita que se encimen es la Y: baseY 0 el IR, 2.0 el OR, 3.5 el CF y
        # 5.0 el OC. Sin esto, las cuatro familias caian una encima de otra.
        check("se agrupa por familia para recorrer banda por banda",
              "GroupBy(f => f.Familia)" in cuerpo_exp)
        check("y cada familia se dibuja a la altura de su macro",
              "BandaDeLaFamiliaCm(grupo.Key)" in cuerpo_exp
              and "DibujarAcero(perfil, xIzquierda, y)" in cuerpo_exp)
        check("con el aire que le toca a esa familia",
              "AireDeLaFamiliaCm(grupo.Key)" in cuerpo_exp)
        check("y se avisa si una banda no da de alto",
              "TechoDeLaBandaCm(grupo.Key)" in cuerpo_exp)

    # Las alturas y los huecos son los de las macros, uno por uno.
    for familia, banda in (("Ir", "0"), ("Or", "200"), ("Cf", "350"), ("Oc", "500")):
        check(f"la banda de {familia.upper()} es la de su macro ({banda} cm)",
              f"FamiliaPerfil.{familia} => {banda}," in acero_cb)

    for familia, aire in (("Ir", "45"), ("Or", "55"), ("Oc", "60"), ("Cf", "65")):
        check(f"el aire de {familia.upper()} es el de su macro ({aire} cm)",
              f"FamiliaPerfil.{familia} => {aire}," in acero_cb)

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

    for familia in ("IR", "OR", "OC", "CF"):
        check(f"el catalogo trae perfiles {familia}",
              f"\n{familia};" in csv)

    # El convertidor del formato del IMCA, que no es una hoja normal: cada familia usa
    # otras columnas y las unidades cambian de una a otra.
    imca = leer(ruta("tools/catalogo_imca.py"))

    check("hay convertidor para el formato del IMCA",
          "def filas_del_libro(ruta)" in imca)
    check("mapea las familias del IMCA a las cuatro formas",
          '"HSS": "OR"' in imca and '"PIPE": "OC"' in imca and '"W": "IR"' in imca)
    check("y deja fuera, con motivo, las que no se saben dibujar",
          "SIN_FORMA" in imca and '"WT"' in imca and '"L"' in imca)
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
    check("cada cota de acero lleva ese estilo y su altura",
          'PropCota((object)cota, "TextStyle", EstiloTextoAcero);' in acero_cad
          and 'PropCota((object)cota, "TextHeight", AlturaTextoCotaAcero * _f);'
          in acero_cad)
    check("la altura de la cota es la de las macros, 0.015",
          "AlturaTextoCotaAcero = 0.015" in acero_cad)

    # La altura del estilo va en CERO. Un estilo con altura fija manda sobre la del
    # texto, y las cuatro macros le fijan la altura a cada cota por objeto: con el
    # 0.015 que pone la IR en el estilo, esas asignaciones no harian nada.
    check("el estilo ACERO va con altura variable",
          "estilo.Height = 0d;" in acero_cad)

    # Y las diferencias de rotulo entre macros, que el port tenia unificadas de mas.
    check("el ancho del MText del tubo redondo es 2.5, como su macro",
          'p.Familia == "OC" ? 2.5 : 0.7' in acero_cad)
    check("y el rotulo del CF va 0.05 mas arriba, como su macro",
          '(p.Familia == "CF" ? 0.05 : 0.06)' in acero_cad)

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
    check("y lo que falta por portar",
          "no sabe" in audit and "WT" in audit and "ZF" in audit)

    check("hay comprobacion numerica del catalogo y del acomodo",
          "El acomodo del acero"
          in leer(ruta("tools/verificar_catalogo_y_acomodo.py")))
    check("y de las bandas de cada familia",
          "Las bandas de cada familia"
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

if __name__ == "__main__":
    sys.exit(main())
