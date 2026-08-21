#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Caza los dos errores de compilacion que este proyecto ha cometido MAS DE UNA VEZ y que
solo aparecen al compilar en Windows, donde estan las bibliotecas de WPF:

  1) CS0103 «El nombre 'X' no existe en el contexto actual»
     Un tipo de WPF usado en un archivo que no importa su espacio de nombres. Paso con
     'Cursors' en MainWindow.Zapatas.cs: el archivo traia System.Windows y
     System.Windows.Media, pero 'Cursors' vive en System.Windows.Input. Los otros
     archivos que lo usan si lo traian, asi que el error solo salia en el nuevo.

  2) CS1976 «No se puede usar un grupo de metodos como argumento de una operacion
     enviada de forma dinamica»
     Pasar el NOMBRE de un metodo -sin invocarlo- a una llamada que se resuelve en
     tiempo de ejecucion porque uno de sus argumentos es 'dynamic'. Paso con
     'new ZapataDrawer(doc, DiametroCmDeVarilla)': 'doc' es dynamic, asi que toda la
     construccion es dinamica, y el enlazador dinamico no puede convertir un grupo de
     metodos a un delegado. Se arregla metiendo el metodo en una variable con su tipo:
     'Func<string?, double> f = DiametroCmDeVarilla;'.

POR QUE ESTO EXISTE
    La aplicacion es WPF y solo compila en Windows. Aqui se comprueba lo que se pueda
    SIN compilador: los usings que hacen falta y los grupos de metodos en llamadas
    dinamicas. No sustituye a compilar, pero estos dos errores ya no vuelven a salir
    despues de una entrega.

    Es deliberadamente una TABLA de tipos conocidos y no un analisis del lenguaje: una
    tabla se entiende, se amplia en una linea y no da falsos positivos raros. Si algun
    dia falta un tipo en la tabla, se agrega cuando se vea el error una vez.

Se usa:
    python3 tools/verificar_usings.py
Devuelve 0 si todo esta bien y 1 si hay algo que corregir.
"""

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(RAIZ, "client", "src", "CadLink.App")

# ----------------------------------------------------------------------
# Los tipos de WPF y de la BCL que se usan en la aplicacion, con su espacio de
# nombres. Solo tipos que se usan como 'Tipo.Miembro' o 'new Tipo(...)': asi no se
# confunde con una PROPIEDAD del mismo nombre -'Cursor', 'Color', 'Background'-, que es
# de donde saldrian los falsos positivos.
# ----------------------------------------------------------------------
TIPOS = {
    "System.Windows": [
        "MessageBox", "MessageBoxButton", "MessageBoxImage", "MessageBoxResult",
        "Application", "Thickness", "GridLength", "Visibility", "TextAlignment",
        "RoutedEventArgs", "SizeChangedEventArgs", "DependencyProperty",
        "FrameworkElement", "DataObject", "Clipboard", "Window",
    ],
    "System.Windows.Input": [
        "Cursors", "Keyboard", "Key", "ModifierKeys", "KeyBinding", "KeyEventArgs",
        "MouseButtonEventArgs", "MouseEventArgs", "ApplicationCommands",
        "CommandBinding", "ExecutedRoutedEventArgs",
    ],
    "System.Windows.Media": [
        "Brushes", "SolidColorBrush", "Colors", "ColorConverter", "FontFamily",
        "RotateTransform", "ScaleTransform", "TranslateTransform", "TransformGroup",
        "VisualTreeHelper", "PathGeometry", "PathFigure", "LineSegment",
        "GeometryGroup", "EllipseGeometry", "RectangleGeometry", "StreamGeometry",
        "DrawingVisual", "FormattedText", "Typeface", "PixelFormats",
        "DoubleCollection",
    ],
    "System.Windows.Shapes": [
        "Ellipse", "Polygon", "Polyline",
    ],
    "System.Windows.Controls": [
        "TextBox", "ComboBox", "CheckBox", "DataGrid", "TextBlock", "Border",
        "StackPanel", "ScrollViewer", "MenuItem", "TabItem",
        "SelectionChangedEventArgs", "TextChangedEventArgs", "Orientation",
        "DataGridCell", "DataGridRow", "DataGridColumn",
    ],
    "System.Windows.Threading": [
        "DispatcherTimer", "DispatcherPriority",
    ],
    "System.Windows.Media.Imaging": [
        "BitmapImage", "PngBitmapEncoder", "RenderTargetBitmap", "BitmapFrame",
    ],
    "System.Collections.ObjectModel": [
        "ObservableCollection", "ReadOnlyObservableCollection",
    ],
    "System.ComponentModel": [
        "PropertyChangedEventArgs", "PropertyChangedEventHandler", "CancelEventArgs",
    ],
    "System.Globalization": [
        "CultureInfo", "NumberStyles",
    ],
    "System.Text": [
        "StringBuilder", "Encoding",
    ],
    "System.Text.Json": [
        "JsonSerializer", "JsonSerializerOptions",
    ],
    "System.Diagnostics": [
        "Process", "Stopwatch", "Debug",
    ],
    "System.Runtime.InteropServices": [
        "Marshal",
    ],
}

# Espacios de nombres que el proyecto trae SOLOS, sin escribirlos en cada archivo:
# los implicitos del SDK mas los que el .csproj agrega con <Using Include="..." />.
# Si se toca esa lista en el .csproj, hay que tocar esta.
GLOBALES = {
    "System", "System.Collections.Generic", "System.IO", "System.Linq",
    "System.Net.Http", "System.Threading", "System.Threading.Tasks",
}

# ----------------------------------------------------------------------
# Utilidades
# ----------------------------------------------------------------------


def sin_comentarios_ni_textos(codigo):
    """El codigo sin comentarios ni literales, para no leer un tipo dentro de un texto."""
    codigo = re.sub(r"/\*.*?\*/", " ", codigo, flags=re.S)
    codigo = re.sub(r"//[^\n]*", " ", codigo)
    codigo = re.sub(r'@"(?:[^"]|"")*"', '""', codigo, flags=re.S)
    codigo = re.sub(r'"(?:\\.|[^"\\])*"', '""', codigo)
    return codigo


def espacios_importados(codigo):
    """Los espacios de nombres que el archivo importa, mas los globales del proyecto."""
    nombres = set(GLOBALES)

    for m in re.finditer(r"^\s*using\s+(?:static\s+)?([A-Za-z_][\w.]*)\s*;", codigo, re.M):
        nombres.add(m.group(1))

    # 'using Alias = System.IO.Path;' NO importa un espacio: importa un tipo. Se anota
    # aparte porque el alias basta para que el nombre exista.
    alias = set(
        m.group(1)
        for m in re.finditer(r"^\s*using\s+(\w+)\s*=\s*[\w.]+\s*;", codigo, re.M)
    )

    # El propio espacio del archivo: 'namespace CadLink.App;'
    m = re.search(r"^\s*namespace\s+([A-Za-z_][\w.]*)\s*;", codigo, re.M)

    if m:
        nombres.add(m.group(1))

    return nombres, alias


fallos = []
comprobaciones = 0


def revisar_usings(ruta, codigo):
    """Cada tipo de la tabla que se use en el archivo tiene que tener su using."""
    global comprobaciones

    limpio = sin_comentarios_ni_textos(codigo)
    espacios, alias = espacios_importados(codigo)

    for espacio, tipos in TIPOS.items():
        for tipo in tipos:
            # Uso como miembro estatico -'Cursors.Wait'- o como constructor -'new Ellipse'.
            # El (?<![\w.]) es lo que evita el falso positivo de 'mt.Color' o '_miTextBox'.
            usado = re.search(
                r"(?<![\w.])" + tipo + r"\s*\.\s*\w"
                r"|new\s+" + tipo + r"\s*[({]"
                r"|<\s*" + tipo + r"\s*[,>]",
                limpio,
            )

            if not usado:
                continue

            comprobaciones += 1

            if espacio in espacios or tipo in alias:
                continue

            # Puede estar escrito con su nombre completo en el propio uso.
            if re.search(re.escape(espacio) + r"\s*\.\s*" + tipo, limpio):
                continue

            linea = limpio[: usado.start()].count("\n") + 1

            fallos.append(
                f"{os.path.basename(ruta)}({linea}): usa '{tipo}' y no importa "
                f"'{espacio}'. Es el CS0103 de siempre: agrega "
                f"'using {espacio};' al principio del archivo."
            )


def revisar_grupos_de_metodos(ruta, codigo):
    """Ningun nombre de metodo suelto en una llamada que lleve un 'dynamic'."""
    global comprobaciones

    limpio = sin_comentarios_ni_textos(codigo)

    # Los metodos declarados en el archivo: son los candidatos a colarse como nombre
    # suelto. Un identificador cualquiera no basta como pista; el nombre de un metodo si.
    metodos = set(
        m.group(1)
        for m in re.finditer(
            r"(?:public|private|protected|internal)\s+(?:static\s+)?"
            r"(?:async\s+)?[\w<>,?\[\]\s.]+?\s+(\w+)\s*\(",
            limpio,
        )
    )

    # Las variables declaradas 'dynamic': 'dynamic doc = ...'.
    dinamicas = set(
        m.group(1) for m in re.finditer(r"\bdynamic\s+(\w+)\s*=", limpio)
    )

    if not dinamicas or not metodos:
        return

    for m in re.finditer(r"(?:new\s+\w+|\.\s*\w+|\b\w+)\s*\(([^()\n]*)\)", limpio):
        args = [a.strip() for a in m.group(1).split(",") if a.strip()]

        if not any(a in dinamicas for a in args):
            continue

        comprobaciones += 1

        for a in args:
            if a in metodos:
                linea = limpio[: m.start()].count("\n") + 1

                fallos.append(
                    f"{os.path.basename(ruta)}({linea}): pasa el metodo '{a}' a una "
                    "llamada que lleva un 'dynamic', asi que se resuelve en tiempo de "
                    "ejecucion y da CS1976. Metelo antes en una variable con su tipo: "
                    f"'Func<...> f = {a};' y pasa 'f'."
                )


def main():
    if not os.path.isdir(APP):
        print(f"No encuentro {APP}")
        return 1

    archivos = sorted(
        os.path.join(dir_, f)
        for dir_, _, fs in os.walk(APP)
        for f in fs
        if f.endswith(".cs") and "obj" not in dir_ and "bin" not in dir_
    )

    print("=" * 78)
    print(" USINGS Y LLAMADAS DINAMICAS DE LA APLICACION WPF")
    print("=" * 78)
    print()

    for ruta in archivos:
        with open(ruta, encoding="utf-8") as fh:
            codigo = fh.read()

        revisar_usings(ruta, codigo)
        revisar_grupos_de_metodos(ruta, codigo)

    print(f" {len(archivos)} archivo(s) revisados, {comprobaciones} uso(s) comprobados.")
    print()

    if fallos:
        print("=" * 78)
        print(f" HAY {len(fallos)} COSA(S) QUE NO VAN A COMPILAR:")
        print("=" * 78)

        for f in fallos:
            print("  - " + f)

        return 1

    print("=" * 78)
    print(" Todo correcto: cada tipo tiene su using y ninguna llamada dinamica")
    print(" recibe un nombre de metodo suelto.")
    print("=" * 78)

    return 0


if __name__ == "__main__":
    sys.exit(main())
