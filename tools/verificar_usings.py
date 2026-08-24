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

  3) MC3072 «la propiedad 'X' no existe en el espacio de nombres XML ...»
     Una propiedad puesta en una COLUMNA de DataGrid que la columna no tiene. Paso con
     ToolTip="..." en un DataGridTextColumn: una columna de DataGrid no es un control
     -no hereda de FrameworkElement-, asi que no tiene ToolTip. El globo va en el
     elemento de la celda, con ElementStyle. Esto NO lo caza validar.py, que solo
     comprueba que el XAML sea XML bien formado, y en Windows tumba la compilacion.

  4) CS1061 «X no contiene una definicion para Y»
     Un miembro que NO EXISTE pero que se parece a uno que si. Paso con
     'dado.DiamEsqSupEfectivo': lo que existe es 'DiamEsqSup' -el lecho superior es la
     base y no hereda de nadie, asi que no tiene «efectivo»- y 'DiamEsqInfEfectivo'.
     Este es el error mas facil de cometer del proyecto, porque los nombres de los
     modelos son largos y parecidos entre si.

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


# ----------------------------------------------------------------------
# Lo que SI acepta una columna de DataGrid. Una columna no es un control: no tiene
# ToolTip, ni Margin, ni Padding, ni IsEnabled, ni Style. Lo que se le pone de mas no
# falla en tiempo de ejecucion: falla al COMPILAR, con MC3072.
# ----------------------------------------------------------------------
COLUMNA_COMUN = {
    "Header", "HeaderStyle", "HeaderStringFormat", "HeaderTemplate",
    "HeaderTemplateSelector", "Width", "MinWidth", "MaxWidth", "CanUserSort",
    "CanUserResize", "CanUserReorder", "DisplayIndex", "IsReadOnly", "SortMemberPath",
    "SortDirection", "Visibility", "CellStyle", "ClipboardContentBinding",
    "DragIndicatorStyle", "Foreground", "Background", "FontFamily", "FontSize",
    "FontStyle", "FontWeight", "x:Name", "x:Uid",
}

COLUMNA_PROPIA = {
    "DataGridTextColumn": {"Binding", "ElementStyle", "EditingElementStyle"},
    "DataGridCheckBoxColumn": {
        "Binding", "ElementStyle", "EditingElementStyle", "IsThreeState"},
    "DataGridHyperlinkColumn": {
        "Binding", "ElementStyle", "EditingElementStyle", "ContentBinding", "TargetName"},
    "DataGridComboBoxColumn": {
        "ElementStyle", "EditingElementStyle", "ItemsSource", "SelectedItemBinding",
        "SelectedValueBinding", "SelectedValuePath", "DisplayMemberPath", "TextBinding"},
    "DataGridTemplateColumn": {
        "CellTemplate", "CellTemplateSelector", "CellEditingTemplate",
        "CellEditingTemplateSelector"},
}

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
                r"|<\s*" + tipo + r"\s*[,>]"

                # EL TIPO GENERICO, ESCRITO EN UNA DECLARACION.
                # Esto FALTABA, y por faltar dejo pasar un error de compilacion hasta
                # Windows: 'public static ObservableCollection<string> X { get; } = new();'
                # no es 'Tipo.Miembro' ni 'new Tipo(...)' -el 'new()' no repite el tipo- ni
                # el tipo DENTRO de otro generico, asi que ninguno de los tres patrones de
                # arriba lo veia. Es justo como se declara una lista observable, o sea el
                # caso mas corriente que hay.
                r"|(?<![\w.])" + tipo + r"\s*<"

                # Y el tipo a secas en una declaracion: 'StringBuilder sb = new();',
                # 'CultureInfo? c;', 'Process p, q;'. Mismo motivo.
                r"|(?<![\w.])" + tipo + r"\??\s+\w+\s*(?:=|;|\)|,)",
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


def revisar_columnas_de_datagrid(ruta, xaml):
    """Ninguna columna de DataGrid con una propiedad que las columnas no tienen."""
    global comprobaciones

    for m in re.finditer(r"<(DataGrid\w*Column)\b((?:[^>\"]|\"[^\"]*\")*?)/?>", xaml, re.S):
        tipo = m.group(1)

        if tipo not in COLUMNA_PROPIA:
            continue

        validas = COLUMNA_COMUN | COLUMNA_PROPIA[tipo]

        for atr in re.finditer(r'([\w:.]+)\s*=\s*"', m.group(2)):
            nombre = atr.group(1)

            # 'DataGridTextColumn.ElementStyle' y demas propiedades escritas con su
            # tipo delante son validas: se comprueba la parte de despues del punto.
            corto = nombre.split(".")[-1] if "." in nombre and ":" not in nombre else nombre

            comprobaciones += 1

            if corto in validas or nombre in validas:
                continue

            linea = xaml[: m.start()].count("\n") + 1

            fallos.append(
                f"{os.path.basename(ruta)}({linea}): {tipo} lleva '{nombre}', y una "
                "columna de DataGrid no tiene esa propiedad: no es un control. Es el "
                "MC3072. Si es un globo o un margen, va en el elemento de la celda, "
                "con ElementStyle o CellStyle."
            )


# ----------------------------------------------------------------------
# Miembros de la BCL, de WPF y de la interop de AutoCAD que se usan por su nombre y NO
# estan declarados en el proyecto. No hace falta que este la lista entera: solo los que
# se parecen a algo nuestro, que son los que darian un falso positivo.
# ----------------------------------------------------------------------
AJENOS = {
    "BackgroundScaleFactor", "UseBackgroundColor", "BackgroundColor", "BackgroundFill",
    "AttachmentPoint", "InsertionPoint", "ParagraphAlignment", "TextAlignmentPoint",
    "HorizontalAlignment", "VerticalAlignment", "VerticalContentAlignment",
    "EntityTransparency", "ConstantWidth", "PatternScale", "HatchStyle", "StyleName",
    "TextInsideAlign", "TextOutsideAlign", "ForceLineInside", "TextMovement",
    "TextRotation", "TextPosition", "TextOverride", "TextInside", "ActiveDimStyle",
    "GetBoundingBox", "AppendOuterLoop", "AddLightWeightPolyline", "AddDimAligned",
    "InsertBlock", "SetVariable", "GetVariable", "ZoomExtents", "ActiveDocument",
    "ModelSpace", "TextStyles", "DimStyles", "Linetypes", "Layers", "Blocks",
    "SelectedItem", "SelectedIndex", "SelectedItems", "SelectedDate", "SelectedValue",
    "ItemsSource", "IsDropDownOpen", "IsExpanded", "IsChecked", "IsEnabled",
    "InvokeMember", "GetProperties", "GetIndexParameters", "PropertyType",
    "ToUpperInvariant", "ToLowerInvariant", "InvariantCulture", "CurrentCulture",
    "OrdinalIgnoreCase", "StringComparison", "NumberStyles", "PropertyChanged",
    "PropertyName", "ContainsKey", "TryGetValue", "FirstOrDefault", "ElementAt",
    "SetDatabaseDefaults", "TrueColor", "ObjectName", "StartPoint", "EndPoint",
    # De la COTA de AutoCAD, por objeto. ExtensionLineExtend se parece a «Extension» de
    # este proyecto y se reportaba como CS1061 sin serlo: es una propiedad de AcadDimension
    # y el objeto es dynamic.
    "ExtensionLineExtend", "DecimalSeparator", "ArrowheadSize", "TextHeight",
    "LinetypeScale", "AddSolid", "MoveToTop", "GetExtensionDictionary",
    "AddMText", "AddText", "AddHatch", "AddCircle", "AddLine", "AddArc", "Evaluate",
    "SetFont", "CopyFrom", "Regen", "Update", "Delete", "Move", "Rotate", "Explode",
    "ScreenUpdating", "ShowDialog", "InitializeComponent", "Children", "Content",
    "MessageBoxButton", "MessageBoxImage", "MessageBoxResult", "RoutedEventArgs",
    "NewItems", "OldItems", "SelectionChanged", "SizeChanged", "ActualWidth",
    "ActualHeight", "StrokeThickness", "StrokeDashArray", "SolidColorBrush",
    "FontFamily", "FontWeight", "FontStyle", "TextAlignment", "TextWrapping",
    "HasFeature", "SetLeft", "SetTop", "SetZIndex", "GetTempPath", "GetFileName",
    "GetFileNameWithoutExtension", "GetDirectoryName", "GetFullPath", "WriteAllText",
    "ReadAllText", "WriteAllBytes", "ReadAllBytes", "AppendAllText", "CreateDirectory",
    "SerializeToUtf8Bytes", "Deserialize", "Serialize", "PropertyNamingPolicy",
    "WriteIndented", "DefaultIgnoreCondition", "AllowTrailingCommas",
    "PropertyChangedEventArgs", "PropertyChangedEventHandler", "CancelEventArgs",
    "NotifyCollectionChangedEventArgs", "DataGridCellEditEndingEventArgs",
}


def _prefijo_comun(a, b):
    n = 0
    for x, y in zip(a, b):
        if x != y:
            break
        n += 1
    return n


def revisar_miembros_que_no_existen(rutas):
    """
    Un miembro que no existe en el proyecto pero se parece MUCHO a uno que si.

    Solo se avisa cuando el nombre comparte al menos 8 caracteres de principio con un
    miembro declarado. Con menos, un miembro de la BCL o de la interop de AutoCAD que
    no este en la lista de arriba daria un aviso falso, y un verificador que avisa de
    lo que esta bien se deja de leer.
    """
    global comprobaciones

    declarados = set()
    textos = {}

    for ruta in rutas:
        with open(ruta, encoding="utf-8") as fh:
            t = sin_comentarios_ni_textos(fh.read())

        # Fuera los 'using' y el 'namespace': ahi los puntos separan ESPACIOS DE NOMBRES
        # -System.Diagnostics, CadLink.Licensing- y no miembros de nada.
        t = re.sub(r"^\s*(?:global\s+)?using[^\n;]*;", " ", t, flags=re.M)
        t = re.sub(r"^\s*namespace[^\n;{]*[;{]", " ", t, flags=re.M)

        textos[ruta] = t

        for m in re.finditer(
            r"\b(?:public|private|protected|internal)\s+"
            r"(?:static\s+|readonly\s+|const\s+|virtual\s+|override\s+|sealed\s+|"
            r"abstract\s+|partial\s+|new\s+|required\s+|async\s+)*"
            r"[\w<>,?\[\]\.\(\) ]+?\s+(\w+)\s*(?:=>|=|;|\{|\()",
            t,
        ):
            declarados.add(m.group(1))

        # Nombres de las columnas del XAML y demas identificadores de la clase.
        for m in re.finditer(r"\b_(\w+)\b", t):
            declarados.add("_" + m.group(1))

        # Los miembros de un ENUM no llevan modificador de acceso, asi que el patron de
        # arriba no los ve: 'ClasePlanta.Diagonal' es un miembro de enum, no un error.
        for m in re.finditer(r"\benum\s+\w+[^{]*\{([^}]*)\}", t, re.S):
            for id_ in re.findall(r"[A-Za-z_]\w*", m.group(1)):
                declarados.add(id_)

        # Los parametros POSICIONALES de un record tampoco llevan modificador, y sin
        # embargo son propiedades publicas: 'Parrilla(double[] Circulos)' declara
        # .Circulos. Sin esto, el dia que aparecio 'CirculosDelMuro' el verificador
        # senalo el .Circulos de la parrilla como si no existiera, que es justo el
        # aviso falso que hace que un verificador se deje de leer.
        for m in re.finditer(r"\brecord\s+(?:struct\s+|class\s+)?\w+\s*\(([^)]*)\)", t):
            for parte in m.group(1).split(","):
                ids = re.findall(r"[A-Za-z_]\w*", parte)
                if ids:
                    declarados.add(ids[-1])

    largos = [d for d in declarados if len(d) >= 8]

    for ruta, t in textos.items():
        for m in re.finditer(r"\.\s*([A-Z][A-Za-z0-9]{7,})\b(?!\s*\()", t):
            nombre = m.group(1)

            if nombre in declarados or nombre in AJENOS:
                continue

            comprobaciones += 1

            parecidos = [
                d for d in largos
                if d != nombre and _prefijo_comun(d, nombre) >= 8
            ]

            if not parecidos:
                continue

            linea = t[: m.start()].count("\n") + 1

            fallos.append(
                f"{os.path.basename(ruta)}({linea}): '{nombre}' no esta declarado en el "
                f"proyecto y se parece a {', '.join(sorted(parecidos)[:3])}. Es el "
                "CS1061: revisa el nombre del miembro."
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

    # Los miembros se revisan contra TODO el cliente: los modelos y la biblioteca de
    # AutoCAD estan en otras carpetas y son justo donde viven los nombres largos.
    cliente = sorted(
        os.path.join(dir_, f)
        for dir_, _, fs in os.walk(os.path.join(RAIZ, "client", "src"))
        for f in fs
        if f.endswith(".cs") and "obj" not in dir_ and "bin" not in dir_
    )

    revisar_miembros_que_no_existen(cliente)

    xamls = sorted(
        os.path.join(dir_, f)
        for dir_, _, fs in os.walk(APP)
        for f in fs
        if f.endswith(".xaml") and "obj" not in dir_ and "bin" not in dir_
    )

    for ruta in xamls:
        with open(ruta, encoding="utf-8") as fh:
            revisar_columnas_de_datagrid(ruta, fh.read())

    print(f" {len(archivos)} archivo(s) de codigo y {len(xamls)} de XAML revisados, "
          f"{comprobaciones} uso(s) comprobados.")
    print()

    if fallos:
        print("=" * 78)
        print(f" HAY {len(fallos)} COSA(S) QUE NO VAN A COMPILAR:")
        print("=" * 78)

        for f in fallos:
            print("  - " + f)

        return 1

    print("=" * 78)
    print(" Todo correcto: cada tipo tiene su using, ninguna llamada dinamica recibe")
    print(" un nombre de metodo suelto y ninguna columna de DataGrid lleva una")
    print(" propiedad que no tiene.")
    print("=" * 78)

    return 0


if __name__ == "__main__":
    sys.exit(main())
