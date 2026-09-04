; Inno Setup script for Claude Desktop Tools.
; Build the app first: dotnet publish -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -o ClaudeDesktopTools\bin\publish\win-x64
; Then compile: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ClaudeDesktopTools.iss

#define MyAppName "Claude Desktop Tools"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "AnaCataVC"
#define MyAppExeName "ClaudeDesktopTools.exe"
#define PublishDir "..\ClaudeDesktopTools\bin\publish\win-x64"

[Setup]
AppId={{9F3B3C1E-6A9C-4B7E-9C7B-2E4C3E8B8A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=output
OutputBaseFilename=ClaudeDesktopToolsSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
