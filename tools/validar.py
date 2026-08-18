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
        codigo = leer(cb)
        txt = leer(x)
        handlers = set()
        for m in re.finditer(
            r'\b(?:Click|Checked|Unchecked|SelectionChanged|TextChanged|Loaded|'
            r'Closing|Closed|MouseDown|MouseUp|KeyDown|KeyUp|GotFocus|LostFocus|'
            r'SizeChanged|PreviewKeyDown|Drop|DragOver)\s*=\s*"([A-Za-z0-9_]+)"',
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

    m = re.search(r'"COLUMNA", "DADO"(.*?)\n\s*\}', codigo, re.S)
    lista = ('"COLUMNA", "DADO"' + m.group(1)) if m else ""
    check("lista de elementos localizada", m is not None)

    for fuera in ["MURO", "LOSA", "DALA", "VIGA"]:
        check(f"sin {fuera} en la lista", f'"{fuera}"' not in lista)

    for dentro in ["COLUMNA", "DADO", "CASTILLO", "TRABE", "CONTRATRABE",
                   "CADENA DE CERRAMIENTO", "CADENA DE DESPLANTE"]:
        check(f"con {dentro} en la lista", f'"{dentro}"' in lista)

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
    n_arr = len(re.findall(r"ConArregloDeEntidades\(", drawer))
    check(
        "las 6 llamadas con arreglo pasan por el envoltorio",
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

    # Las constantes de la macro, con su valor exacto.
    for nombre, valor in [
        ("YBloques", "2.0"),
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
          "InsertarSeccion(a.Id, xSec, AlzadoLayout.YBloques)" in alz2)
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
        check("en la columna el alzado arranca sobre la seccion",
              "var y1 = topeSeccion + SepSecAlz;" in cuerpo and "YAlzado = y1," in cuerpo)

        # La segunda cara, a SEP_CARAS del paño superior de la primera.
        check("la segunda cara va a SEP_CARAS de la primera",
              "y1 + largo + SepCaras" in cuerpo)

        # En la trabe el alzado va a la DERECHA de la seccion, y los dos apoyados.
        check("en la trabe el alzado va al lado de la seccion",
              "XAlzado = x0 + anchoSeccion + SepSecAlz," in cuerpo)
        check("y los dos apoyados en Y_BLOQUES", "YAlzado = YBloques," in cuerpo)

        # Los dos avances, cada uno con sus terminos.
        check("el avance de la columna es blockWidth + alzadoWidth + SEP_SECCIONES",
              "xSec + anchoSeccion + AnchoCotasVertical + SepSecciones" in cuerpo)
        check("el de la trabe incluye el largo y el aire del gancho",
              "x0 + anchoSeccion + SepSecAlz + largo + HookDimOff2 + SepSecciones"
              in cuerpo)

    # El MARGEN_COL solo en la columna, y en un solo sitio.
    check("XSeccion abre el margen solo en la columna",
          "vertical ? x0 + MargenCol : x0" in lay)

    # El rotulo se LLAMA, no solo se declara: renombrar el metodo dejaba pasar el
    # check anterior porque el texto seguia en el archivo.
    check("el CORTE A-A' se dibuja de verdad",
          "RotuloCorte(x + (ancho / 2), y + alto);" in alz2)

    check("hay comprobacion de la colocacion contra el VBA",
          os.path.exists(ruta("tools/verificar_layout_alzados.py")))

    # ------------------------------------------------------------------
    # 0b. Modulo nuevo: dibujar planos estructurales
    # ------------------------------------------------------------------
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


def main() -> int:
    print("=" * 66)
    print(" Validaciones estaticas de CadLink")
    print(" (este entorno no tiene .NET: no se compila, solo se revisa)")
    print("=" * 66)

    for f in (v1_xml, v2_bat, v3_usings, v4_cs0050, v5_value,
              v6_handlers, v7_names, v8_python, v9_modo, v10_nombres_tapados,
              v11_visor, v12_fidelidad, v13_compilacion,
              v14_bloques_diamante_etabs, v15_cs0103,
              v16_extruida_piers, v17_guardar_y_defaults):
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

    check("hay botones de guardar y abrir",
          'Click="OnGuardarTrabajo"' in xaml and 'Click="OnAbrirTrabajo"' in xaml
          and 'Click="OnGuardarComo"' in xaml)
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
    check("la ruta del libro se recuerda aparte", "_rutaExcel" in codigo)

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

if __name__ == "__main__":
    sys.exit(main())
