"""Revisa que cada tipo de WPF usado en CadLink.App tenga su `using`.

Nace de un error real: MainWindow.Alzado3D.cs usaba Canvas.SetLeft sin
`using System.Windows.Controls`, y el compilador solo dice «CS0103: el nombre
'Canvas' no existe en el contexto actual», que despista bastante porque parece
que falte una referencia cuando lo que falta es el espacio de nombres.

Tiene en cuenta las dos cosas que hacen falta para no dar falsos positivos:
los `using` GLOBALES que declara el .csproj -aqui System.IO- y los ALIAS, que
es como este proyecto resuelve la ambiguedad de `Path`.

Uso:  python3 tools/verificar_usings_wpf.py
Sale con codigo 1 si encuentra algo, para poder encadenarlo en un script.
"""
import re, glob, sys, os

DONDE = {
 'System.Windows.Controls': ['Canvas','ComboBox','TextBlock','Button','Grid','StackPanel',
                             'Border','DataGrid','CheckBox','RadioButton','Expander',
                             'ScrollViewer','TextBox','Image','Label'],
 # 'Path' queda FUERA a proposito: el csproj trae System.IO como using global, asi que
 # 'Path' a secas es el de archivos. Quien quiere la figura de WPF usa el alias FormaPath.
 'System.Windows.Shapes':   ['Ellipse','Line','Polyline','Polygon','Rectangle'],
 'System.Windows.Media':    ['SolidColorBrush','Brush','Color','Colors','Brushes','PointCollection',
                             'GeometryGroup','RectangleGeometry','EllipseGeometry','PathGeometry',
                             'PathFigure','LineSegment','Pen','PenLineJoin','PenLineCap',
                             'FontWeights','ScaleTransform','TranslateTransform'],
 'System.Windows':          ['Point','Rect','Thickness','RoutedEventArgs','Visibility','Window'],
 'System.Windows.Input':    ['MouseButtonEventArgs','MouseEventArgs','MouseWheelEventArgs',
                             'Cursors','MouseButton'],
}
GLOBALES = {'System.IO'}

raiz = os.path.join(os.path.dirname(__file__), '..', 'client', 'src', 'CadLink.App')
raiz = os.path.normpath(raiz)

fallos = []
for f in sorted(glob.glob(os.path.join(raiz, '*.cs'))
                + glob.glob(os.path.join(raiz, 'Models', '*.cs'))):
    txt = open(f, encoding='utf-8').read()
    c = re.sub(r'^\s*using .*$', '', txt, flags=re.M)
    c = re.sub(r'///.*$', '', c, flags=re.M)
    c = re.sub(r'//.*$', '', c, flags=re.M)
    c = re.sub(r'/\*.*?\*/', '', c, flags=re.S)
    usings = set(re.findall(r'^\s*using\s+([\w\.]+)\s*;', txt, flags=re.M)) | GLOBALES
    alias  = set(re.findall(r'^\s*using\s+(\w+)\s*=', txt, flags=re.M))
    for ns, tipos in DONDE.items():
        if ns in usings:
            continue
        for t in tipos:
            if t in alias:
                continue
            if re.search(r'(?<![\w.])' + t + r'(?=\s*[\.\(\{<]|\s+\w)', c):
                fallos.append(f"{os.path.basename(f)}: usa '{t}' y le falta  using {ns};")

fallos = sorted(set(fallos))
print("TIPOS SIN SU USING:", len(fallos))
for x in fallos:
    print("  ", x)
if not fallos:
    print("   ninguno <-- OK")
sys.exit(1 if fallos else 0)
