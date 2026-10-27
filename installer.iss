; =====================================================================
; Inno Setup 6 Script para BenyComes Music Player
; Reproductor Nativo Bit-Perfect para Windows (.NET 8 + WPF / WinUI)
; =====================================================================

#define MyAppName "BenyComes Music Player"
#define MyAppVersion "1.2.001"
#define MyAppPublisher "BenyComes Audio Labs"
#define MyAppURL "https://github.com/MiguelC221/BennyComes"
#define MyAppExeName "BenyComes.exe"
#define SourcePublishDir "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Identificador GUID único de la aplicación
AppId={{8B1E9C72-4B23-4FD2-9908-7264871A5E5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Instalación en {autopf} (Archivos de programa por defecto o AppData local si no es admin)
DefaultDirName={autopf}\BenyComesMusic
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Permisos de usuario estándar (no exige elevación obligatoria a admin a menos que el usuario lo elija)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Directorio y nombre del instalador de salida
OutputDir=installer_output
OutputBaseFilename=BenyComesMusic_Installer_v{#MyAppVersion}
SetupIconFile=Assets\app.ico

; Configuración de compresión óptima
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Destinado a sistemas de 64 bits modernos (Windows 10 / 11)
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Archivos publicados del reproductor (incluye ejecutables, DLLs nativas de NAudio/CSCore/WPF-UI y runtime)
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Icono de la aplicación
Source: "Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Acceso directo en el menú Inicio de Windows
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Comment: "Reproductor de Música Nativo Bit-Perfect"
; Acceso directo opcional en el Escritorio del usuario
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon; Comment: "Reproductor de Música Nativo Bit-Perfect"

[Run]
; Opción para ejecutar la aplicación al terminar de instalar
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
