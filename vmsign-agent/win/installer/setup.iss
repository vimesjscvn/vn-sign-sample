; VMSignAgent Windows Installer (InnoSetup)

#define MyAppName "VMSignAgent"
#define MyAppVersion GetEnv('APP_VERSION')
#if MyAppVersion == ""
#define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "VIETNAM MEDICAL SOFTWARE JSC"
#define MyAppURL "https://github.com/vimesjscvn/vn-sign-sample"
#define MyAppExeName "VMSignAgent.exe"
#define MyAppConfigName "VMSignAgent.exe.config"

[Setup]
AppId={{E7A1B3C5-2D4F-4E6A-8B9C-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\build
OutputBaseFilename=VMSignAgent-win-x64-{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
; VMSignAgent is a tray-only app (no top-level window), so Restart Manager
; cannot close it gracefully and Setup stopped with
; "Setup was unable to automatically close all applications".
; force = terminate it instead of asking the user what to do.
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll,*.config
RestartApplications=no

[Languages]
Name: "vietnamese"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Khởi động cùng Windows"; GroupDescription: "Tùy chọn:"

[Files]
; Everything except the config: an upgrade must not wipe the end user's
; phone number / PIN / selected certificate serial.
Source: "..\build\publish\*"; DestDir: "{app}"; Excludes: "{#MyAppConfigName}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Shipped defaults / seed template. The agent copies this to
; %LOCALAPPDATA%\VMSignAgent\VMSignAgent.config on first run and writes there
; from then on, so keep an existing copy: it still holds the settings of users
; upgrading from a version that saved into the install directory.
Source: "..\build\publish\{#MyAppConfigName}"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Gỡ cài đặt {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// Restart Manager cannot shut down a tray-only ApplicationContext app, so kill
// it outright before touching the files it locks.
procedure KillRunningAgent;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#MyAppExeName}',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // let Windows release the file handles before Setup scans for files in use
  Sleep(500);
end;

// Fires when the user clicks Install, i.e. before the "Preparing to Install"
// page where Restart Manager looks for running processes at all.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID = wpReady then
    KillRunningAgent;
  Result := True;
end;

// Fallback for silent installs, which never pass through wpReady.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillRunningAgent;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  KillRunningAgent;
  Result := True;
end;
