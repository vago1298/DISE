; ============================================================================
;  CADLINK - GUION DEL INSTALADOR                          Inno Setup 6
; ============================================================================
;
;  Esto produce UN SOLO ARCHIVO -CadLink-Setup-1.0.0.exe- que el cliente abre
;  con doble clic. Nada de consola, nada de instalar .NET, nada de Python:
;  la aplicacion se publica AUTOCONTENIDA, o sea con su propio motor de .NET
;  metido dentro del ejecutable.
;
;  NO SE COMPILA A MANO. Se compila con  6-crear-instalador.bat, que ademas
;  publica la aplicacion antes y comprueba lo que no se puede comprobar aqui
;  -que la llave publica este puesta, que el servidor no apunte a localhost-.
;
;  ============================================================================
;  POR QUE INNO SETUP Y NO OTRA COSA
;
;  Es gratis, sin regalias ni licencias por instalador; se compila desde la
;  linea de comandos con ISCC.exe, asi que el dia que quieras un servidor de
;  compilacion no hay que cambiar nada; produce un .exe unico que no necesita
;  el instalador de Windows -MSI- ni permisos de administrador; y lleva veinte
;  anos siendo lo que usa medio mundo, asi que ningun antivirus lo mira raro.
;  ============================================================================

#ifndef Version
  #define Version "1.0.0"
#endif

#define Nombre       "CadLink"
#define Empresa      "MiEmpresa"
#define Ejecutable   "CadLink.exe"
#define SitioWeb     "https://cadlink.miempresa.com"

;  DE DONDE SALEN LOS ARCHIVOS. Es la carpeta que deja  dotnet publish, y la
;  misma ruta esta escrita en 6-crear-instalador.bat: si una cambia y la otra
;  no, tools/verificar_instalador.py lo detiene.
#define Publicado    "..\client\src\CadLink.App\bin\Release\net8.0-windows\win-x64\publish"

;  EL ICONO ES OPCIONAL. Si todavia no has puesto tu CADLINK.ico, el instalador
;  se compila igual con el icono de Inno en lugar de reventar. En cuanto copies
;  el archivo a client\src\CadLink.App\Assets\app.ico se usa solo.
#define RutaIcono "..\client\src\CadLink.App\Assets\app.ico"
#if FileExists(AddBackslash(SourcePath) + RutaIcono)
  #define HayIcono
#endif

[Setup]
;  ===========================================================================
;  EL AppId NO SE CAMBIA NUNCA MAS.
;
;  Es con lo que Windows reconoce que la version nueva es la MISMA aplicacion
;  y la actualiza encima. Si algun dia se cambia, el cliente acaba con dos
;  CadLink instalados a la vez, dos accesos directos y dos entradas en Agregar
;  o quitar programas, y ninguna de las dos desinstala a la otra.
;  ===========================================================================
AppId={{7B2C9E14-4A6D-4F58-9C31-2E8D5A0B7F63}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher={#Empresa}
AppPublisherURL={#SitioWeb}
AppSupportURL={#SitioWeb}
VersionInfoVersion={#Version}
VersionInfoCompany={#Empresa}
VersionInfoDescription=Instalador de {#Nombre}

DefaultDirName={autopf}\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
DisableDirPage=auto

;  ===========================================================================
;  SIN PEDIR CONTRASENA DE ADMINISTRADOR.
;
;  En un despacho el ingeniero casi nunca es administrador de su maquina, y un
;  instalador que pide contrasena se queda sin instalar hasta que pase el de
;  sistemas. Con  lowest  se instala en la carpeta del usuario y funciona
;  igual; con  dialog  el que SI sea administrador puede elegir instalarlo en
;  Program Files para todos los usuarios del equipo.
;
;  La licencia, de todos modos, es por usuario de Windows: se guarda cifrada
;  con DPAPI del usuario en %LOCALAPPDATA%\CadLink. La huella del equipo es la
;  misma para todos, asi que activar en dos usuarios NO gasta dos asientos.
;  ===========================================================================
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

;  x64 a secas y no x64compatible, que solo existe desde Inno 6.3: asi el
;  guion compila con cualquier Inno 6 que tenga el que haga el paquete.
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

;  .NET 8 no corre en Windows 8.1 ni en versiones anteriores. Mas vale decirlo
;  aqui, donde el instalador lo explica, que dejar que la app no arranque.
MinVersion=10.0

OutputDir=..\dist
OutputBaseFilename={#Nombre}-Setup-{#Version}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

UninstallDisplayName={#Nombre} {#Version}
UninstallDisplayIcon={app}\{#Ejecutable}

;  SI LA APP ESTA ABIERTA, se le pide cerrarla en lugar de dejar archivos a
;  medio sustituir que obligan a reiniciar.
CloseApplications=yes
RestartApplications=no

LicenseFile=LICENCIA.txt

#ifdef HayIcono
SetupIconFile={#RutaIcono}
#endif

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
;  ===========================================================================
;  1. TODO LO PUBLICADO, MENOS LO QUE NO DEBE SALIR DE AQUI.
;
;  El comodin es a proposito: con la publicacion en un solo archivo casi todo
;  va dentro del .exe, pero si una version futura deja alguna DLL suelta, con
;  una lista fija se quedaria fuera y la app no arrancaria en el cliente -y
;  aqui no, porque aqui esta la carpeta completa-.
;
;  Y LOS Excludes SON LA PARTE SERIA:
;    *.pem, *.key   la llave PRIVADA firma las licencias. Si se cuela en el
;                   instalador, cualquier cliente puede emitirse licencias
;                   validas y todo el esquema de cobro deja de servir. No
;                   deberia estar en esta carpeta jamas, y por si acaso.
;    *.db, *.sqlite la base de licencias lleva los datos de tus clientes.
;    *.pdb          los simbolos de depuracion facilitan desarmar el programa,
;                   y ademas pesan. Se siguen generando, para poder leer un
;                   informe de error, pero se quedan en tu maquina.
;  ===========================================================================
Source: "{#Publicado}\*"; DestDir: "{app}"; \
    Excludes: "*.pdb,*.pem,*.key,*.db,*.sqlite,*.env,cadlink.config.json,perfiles-acero.csv,aceros.csv"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

;  ===========================================================================
;  2. LOS TRES ARCHIVOS QUE EL CLIENTE PUEDE EDITAR: onlyifdoesntexist.
;
;  La configuracion lleva la direccion del servidor y la ruta de SU logo, y
;  los dos catalogos crecen con los perfiles y los aceros que el cliente
;  agrega. Copiarlos encima en cada actualizacion le borraria el trabajo sin
;  avisar, que es de las cosas que hacen que un programa se deje de usar.
;
;  Consecuencia que hay que tener presente: cuando una version traiga
;  catalogos nuevos, al que ya lo tenia instalado NO le llegan. Se le manda
;  el .csv aparte y lo copia, o se le dice que lo borre y reinstale.
;
;  La configuracion lleva ademas uninsneveruninstall: si el cliente
;  desinstala y vuelve a instalar, no tiene que volver a escribir nada.
;  ===========================================================================
Source: "{#Publicado}\cadlink.config.json"; DestDir: "{app}"; \
    Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#Publicado}\perfiles-acero.csv"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#Publicado}\aceros.csv";         DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#Nombre}";       Filename: "{app}\{#Ejecutable}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"; Tasks: escritorio

[Tasks]
Name: "escritorio"; Description: "Crear un icono en el escritorio"; \
    GroupDescription: "Accesos directos:"

[Run]
Filename: "{app}\{#Ejecutable}"; Description: "Abrir {#Nombre} ahora"; \
    Flags: nowait postinstall skipifsilent

;  ===========================================================================
;  LO QUE EL DESINSTALADOR *NO* BORRA, Y ES ADREDE:
;
;  %LOCALAPPDATA%\CadLink\license.dat  -la licencia activada- se queda. Asi,
;  reinstalar no obliga al cliente a volver a activar ni gasta otra activacion
;  en el servidor. Para dar de baja el equipo esta el boton "Liberar este
;  equipo" de la pestana Licencia, que es lo que borra ese archivo.
;
;  Los trabajos del cliente -.clk- viven donde el los guardo y aqui no se
;  tocan.
;  ===========================================================================
