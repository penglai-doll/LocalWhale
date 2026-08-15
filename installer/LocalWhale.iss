#define MyAppName "LocalWhale"
#define MyAppVersion "0.1.1"
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
function HasUsableVersion(const Version: String): Boolean;
begin
  Result := (Version <> '') and (Version <> '0.0.0.0');
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
