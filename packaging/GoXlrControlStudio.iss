; GoXLR Control Studio — Inno Setup script
; Requires Inno Setup 6+: https://jrsoftware.org/isinfo.php
; Build publish output first:
;   dotnet publish src/GoXlrControl.App/GoXlrControl.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/app

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
