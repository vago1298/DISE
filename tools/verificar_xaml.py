#!/usr/bin/env python3
"""
Comprueba que el XAML y su code-behind se refieren a cosas que existen.

Son los dos fallos de compilacion mas faciles de cometer al agregar una pestaña, y los dos
salen SOLO al compilar:

  1. El code-behind usa un x:Name que el XAML no declara  -> CS0103, «no existe».
  2. El XAML pone Click="OnAlgo" y ese metodo no existe   -> XamlParseException / error MC.

Aqui se comprueban los dos leyendo los dos archivos, que es lo unico que se puede hacer
sin .NET instalado.
"""

import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(RAIZ, 'client', 'src', 'CadLink.App')
XAML = os.path.join(APP, 'MainWindow.xaml')

# Los .cs que forman la clase parcial MainWindow.
PARCIALES = [
    'MainWindow.xaml.cs',
    'MainWindow.Acero.cs',
    'MainWindow.Grapas.cs',
    'MainWindow.PlacaBase.cs',
    'MainWindow.PreviaInteractiva.cs',
    'MainWindow.Seccion3D.cs',
    'MainWindow.Zapatas.cs',
    'MainWindow.ZapatasCorridas.cs',
]


def leer(ruta):
    with open(ruta, encoding='utf-8') as f:
        return f.read()


def main():
    xaml = leer(XAML)

    codigo = ''
    for p in PARCIALES:
        ruta = os.path.join(APP, p)
        if os.path.exists(ruta):
            codigo += leer(ruta) + '\n'

    nombres = set(re.findall(r'x:Name="([A-Za-z_]\w*)"', xaml))
    manejadores = set(re.findall(r'(?:Click|SelectionChanged|Checked|Unchecked|'
                                 r'TextChanged|MouseDown|MouseUp|MouseMove|KeyDown|'
                                 r'Expanded|Collapsed|CellEditEnding|LoadingRow|'
                                 r'PreviewKeyDown|MouseWheel|MouseLeftButtonDown|'
                                 r'MouseLeftButtonUp|Drop|DragOver)="(On\w+)"', xaml))

    # Los metodos declarados en el code-behind.
    metodos = set(re.findall(r'\b(?:private|public|internal|protected)\s+'
                             r'(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(', codigo))

    fallos = []

    # ---- 1. Los manejadores del XAML existen en el codigo ----
    for m in sorted(manejadores):
        if m not in metodos:
            fallos.append(f'el XAML llama a «{m}» y no hay ningun metodo con ese nombre')

    # ---- 2. Los x:Name que usa el codigo estan declarados en el XAML ----
    # Se buscan solo los que PARECEN de control -acaban en un sufijo conocido- para no
    # confundir una variable local con un control.
    sufijos = ('Grid', 'Text', 'Button', 'Combo', 'Canvas', 'Panel', 'Chk', 'Tab',
               'Col', 'Box', 'Slider', 'Border', 'Expander', 'Item', 'List')

    usados = set()
    for ident in re.findall(r'\b([A-Z]\w*(?:' + '|'.join(sufijos) + r'))\s*(?:\.|\s*is\b)',
                            codigo):
        usados.add(ident)

    # Solo se reclaman los que el XAML declara EN ALGUNA parte o los que empiezan por un
    # prefijo de esta pestaña: lo demas puede ser una clase, no un control.
    for ident in sorted(usados):
        if ident.startswith(('Placas', 'PlacaBase', 'ColPlaca')) and ident not in nombres:
            fallos.append(f'el codigo usa «{ident}» y el XAML no lo declara con x:Name')

    # ---- 3. Lo que esta pestaña necesita, nombre por nombre ----
    obligatorios = [
        'PlacasGrid', 'TotalesPlacasText', 'PlacaBaseButton',
        'PlacasNotasText', 'PlacasNotasPanel', 'PlacaPreviewCanvas',
        'ColPlacaFamilia', 'ColPlacaAcero', 'ColPlacaElectrodo',
    ]

    for nombre in obligatorios:
        if nombre not in nombres:
            fallos.append(f'falta el x:Name «{nombre}» en el XAML')

    obligatorios_metodos = [
        'OnAgregarPlaca', 'OnQuitarPlaca', 'OnDibujarPlacaBase',
        'LlenarListasPlacaBase', 'EnlazarPlacaBase', 'ActualizarTotalesPlacas',
        'EngancharVistaPreviaPlacaBase', 'DibujarVistaPreviaPlacaBase',
        'ReferenciarDadoDePlaca', 'ReferenciarDadosDeTodasLasPlacas',
    ]

    for m in obligatorios_metodos:
        if m not in metodos:
            fallos.append(f'falta el metodo «{m}» en el code-behind')

    # ---- 4. Y que el ciclo de vida los llama ----
    #
    # Es la mitad que se olvida: un metodo que existe y que nadie llama deja la pestaña
    # vacia, y eso no lo detecta el compilador -compila igual-. Solo se ve al abrir la
    # pestaña y encontrarla en blanco.
    llamadas = [
        'LlenarListasPlacaBase();',
        'EnlazarPlacaBase();',
        'EngancharVistaPreviaPlacaBase();',
        'DibujarVistaPreviaPlacaBase();',
        'ReferenciarDadosDeTodasLasPlacas();',
    ]

    for llamada in llamadas:
        if llamada not in codigo:
            fallos.append(f'nadie llama a «{llamada}»')

    # ---- 5. El estilo que apaga las celdas de cartabon, y quien lo usa ----
    tema = os.path.join(APP, 'Theme', 'ExcelTabs.xaml')

    if os.path.exists(tema):
        estilos = leer(tema)

        if 'x:Key="CeldaCartabon"' not in estilos:
            fallos.append('falta el estilo «CeldaCartabon» en Theme/ExcelTabs.xaml')
        elif 'Binding="{Binding ConCartabones}" Value="False"' not in estilos:
            fallos.append('«CeldaCartabon» no se apaga con ConCartabones')

        # Las NUEVE celdas de cartabon tienen que usarlo. Con ocho, la que se quede fuera
        # sigue aceptando lo que se escriba y no habria nada que lo delatara.
        usos = xaml.count('CellStyle="{StaticResource CeldaCartabon}"')

        if usos != 9:
            fallos.append(f'«CeldaCartabon» lo usan {usos} columnas y tienen que ser 9 '
                          '(N, e, L y H de cada sentido, y el espesor de su soldadura)')

    print(f'{len(nombres)} x:Name en el XAML, {len(manejadores)} manejadores, '
          f'{len(metodos)} metodos en el code-behind.')
    print()

    if not fallos:
        print('OK  el XAML y el code-behind coinciden.')
        return 0

    for f in fallos:
        print('FALLA  ' + f)

    print()
    print(f'{len(fallos)} problema(s).')

    return 1


if __name__ == '__main__':
    sys.exit(main())
