; GoXLR Control Studio — Inno Setup script (LEGACY — ohne Auto-Update)
; Primärweg: Velopack via packaging/velopack-pack.ps1 bzw. .github/workflows/release.yml
; Requires Inno Setup 6+: https://jrsoftware.org/isinfo.php
; Build publish output first:
;   .\packaging\publish.ps1

#define MyAppName "GoXLR Control Studio"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "GoXLR Control Studio Contributors"
#define MyAppExeName "GoXlrControlStudio.exe"

[Setup]
AppId={{A7C0F8D2-4E91-4B6A-9C3E-1D2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GoXlrControlStudio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=GoXlrControlStudio-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
InfoBeforeFile=INSTALL_NOTES.txt
SetupIconFile=..\src\GoXlrControl.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
