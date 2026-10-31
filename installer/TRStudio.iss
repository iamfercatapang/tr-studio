; Built by tools/BuildInstaller.ps1, which fills in the version and publishes the app first.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "TR Studio"
#define AppExe "CoreVision.App.exe"

[Setup]
AppId={{6F1C2C0E-5B7A-4C38-9E4D-2A7B91D3E7A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=CoreVisionStudio.SingleInstance
CloseApplications=yes
SetupIconFile=..\src\CoreVision.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\artifacts
OutputBaseFilename=TRStudio-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Removes the startup task and gives TRCC its own startup back. Themes and settings stay in ProgramData.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; RunOnceId: "StartupCleanup"; Flags: waituntilterminated runhidden

[Code]
const
  Client = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + Client, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + Client, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasWebView2() then
    MsgBox('This PC needs the Microsoft Edge WebView2 Runtime for the app''s window. The cooler screen will still run from the tray, but install the runtime from Microsoft to open the window.', mbInformation, MB_OK);
end;
