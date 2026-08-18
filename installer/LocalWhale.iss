#define MyAppName "LocalWhale"
#define MyAppVersion "0.1.3"
#define MyAppPublisher "LocalWhale"
#define MyAppExeName "LocalWhale.exe"

[Setup]
AppId={{D6BF4C18-E6FA-4D59-8E11-15AB9EBD5D38}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\LocalWhale
DefaultGroupName=LocalWhale
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=LocalWhale-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupIconFile=..\src\LocalWhale.App\Assets\LocalWhale.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoDescription=LocalWhale Setup
VersionInfoProductName=LocalWhale
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0.22000

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他任务:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\redist\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{group}\LocalWhale"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\LocalWhale"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: NeedsWebView2
Filename: "{app}\{#MyAppExeName}"; Description: "启动 LocalWhale"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\LocalWhale"

[Code]
const
  LocalWhaleMutexName = 'Local\LocalWhale.MainWindow';

var
  RestartAfterInstall: Boolean;

function HasUsableVersion(const Version: String): Boolean;
begin
  Result := (Version <> '') and (Version <> '0.0.0.0');
end;

function InitializeSetup(): Boolean;
var
  Attempts: Integer;
begin
  RestartAfterInstall := Pos('/RESTARTAPP', UpperCase(GetCmdTail())) > 0;
  if RestartAfterInstall then
  begin
    // The staged shell update launches this installer while LocalWhale is still
    // running and exits right after. Wait for its single-instance mutex to be
    // released before overwriting files, so no fixed delay is needed.
    // Bounded to 60 x 250 ms = 15 s.
    Attempts := 0;
    while CheckForMutexes(LocalWhaleMutexName) and (Attempts < 60) do
    begin
      Sleep(250);
      Attempts := Attempts + 1;
    end;
  end;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // /RESTARTAPP is passed by LocalWhale's staged shell update: after a silent
  // reinstall, launch the updated app once the new files are in place.
  if (CurStep = ssPostInstall) and RestartAfterInstall then
  begin
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

function NeedsWebView2: Boolean;
var
  MachineVersion: String;
  UserVersion: String;
  ClientKey: String;
begin
  ClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  RegQueryStringValue(HKLM32, ClientKey, 'pv', MachineVersion);
  RegQueryStringValue(HKCU, ClientKey, 'pv', UserVersion);
  Result := not (HasUsableVersion(MachineVersion) or HasUsableVersion(UserVersion));
end;
