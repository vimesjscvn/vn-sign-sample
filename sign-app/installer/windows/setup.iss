; VimesSign Windows Installer (InnoSetup)
; Built by GitHub Actions CI

#define MyAppName "VimesSign"
#define MyAppVersion GetEnv('APP_VERSION')
#if MyAppVersion == ""
#define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "VIETNAM MEDICAL SOFTWARE JSC"
#define MyAppURL "https://github.com/vimesjscvn/vn-sign-sample"
#define MyAppExeName "VMSign.exe"

[Setup]
AppId={{B5A2F8E1-4C3D-4A7B-9E1F-2D3A4B5C6D7E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\..\build
OutputBaseFilename=VimesSign-win-x64-{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

[Languages]
Name: "vietnamese"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng trên Desktop"; GroupDescription: "Biểu tượng:"; Flags: unchecked

[Files]
Source: "..\..\build\win-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\VMSignAgent"; Filename: "{app}\VMSignAgent.exe"; Check: FileExists(ExpandConstant('{app}\VMSignAgent.exe'))
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\VMSignAgent.exe"; Description: "Start VMSignAgent"; Flags: nowait postinstall skipifsilent skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent
