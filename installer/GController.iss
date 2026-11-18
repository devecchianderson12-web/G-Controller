; Instalador de G Controller (Inno Setup 6).
; Lo compila GitHub Actions: iscc /DMyVersion=1.5.0 installer\GController.iss
; Se instala solo para el usuario (sin pedir administrador), así la actualización
; automática puede reemplazar el .exe igual que en la versión portable.

#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{8E3F2B61-6C1D-4F7A-9B52-3C0A7E4D9A01}
AppName=G Controller
AppVersion={#MyVersion}
AppVerName=G Controller {#MyVersion}
AppPublisher=G Controller
DefaultDirName={localappdata}\Programs\G Controller
DefaultGroupName=G Controller
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=GController-Setup
SetupIconFile=..\obj\app.ico
UninstallDisplayIcon={app}\GController.exe
UninstallDisplayName=G Controller
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Si la app está abierta, el instalador pide cerrarla.
AppMutex=GController.single
CloseApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\GController.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\G Controller"; Filename: "{app}\GController.exe"
Name: "{userdesktop}\G Controller"; Filename: "{app}\GController.exe"; Tasks: desktopicon

[Registry]
; Al desinstalar no queda nada: ni el inicio con Windows ni la configuración.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "GController"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\GController"; ValueType: none; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\GController.exe"; Description: "Abrir G Controller"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im GController.exe"; Flags: runhidden; RunOnceId: "CerrarApp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\GController"
