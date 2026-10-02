#define AppVersion "0.9.7"
#ifndef PayloadDir
#error PayloadDir is required
#endif
#ifndef DeliveryDir
#error DeliveryDir is required
#endif
#ifndef SourceRoot
#error SourceRoot is required
#endif

[Setup]
AppId={{491203BC-953D-4DCE-AB20-14DDF7FC4B39}
AppName=SIRASD
AppVersion={#AppVersion}
AppVerName=SIRASD {#AppVersion}
AppPublisher=A Solas con Dios A.C.
DefaultDirName={localappdata}\Programs\SIRASD
DefaultGroupName=SIRASD
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
WizardStyle=modern
WizardSizePercent=110
SetupIconFile={#SourceRoot}\Assets\SIRASD.ico
UninstallDisplayIcon={app}\SIRASD.exe
VersionInfoVersion=0.9.7.0
VersionInfoDescription=Instalador de SIRASD
VersionInfoCompany=A Solas con Dios A.C.
OutputDir={#DeliveryDir}
OutputBaseFilename=SIRASD-0.9.7-Instalador-Windows-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=no
RestartApplications=no
AppMutex=Local\SIRASD.Desktop.SingleInstance
InfoBeforeFile={#DeliveryDir}\LEEME-Instalacion.txt

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#PayloadDir}\SIRASD.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DeliveryDir}\LEEME-Instalacion.txt"; DestDir: "{app}"; Flags: ignoreversion

Source: "{#DeliveryDir}\AVISOS-TERCEROS.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SIRASD"; Filename: "{app}\SIRASD.exe"; WorkingDir: "{app}"; Check: not IsPackageCheck
Name: "{group}\Guía de instalación"; Filename: "{app}\LEEME-Instalacion.txt"; Check: not IsPackageCheck
Name: "{userdesktop}\SIRASD"; Filename: "{app}\SIRASD.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsPackageCheck

[Run]
Filename: "{app}\SIRASD.exe"; Description: "Abrir SIRASD"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function IsPackageCheck: Boolean;
begin
  Result := ExpandConstant('{param:CHECKPACKAGE|0}') = '1';
end;
