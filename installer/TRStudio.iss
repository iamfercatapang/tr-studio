; Built by tools/BuildInstaller.ps1, which fills in the version and publishes the app first.
#ifndef AppVersion
  #define AppVersion "1.3.0"
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
; An update from inside the app runs silently, so this opens the app again when it is done.
Filename: "{app}\{#AppExe}"; Flags: nowait runascurrentuser; Check: IsUpdate

[UninstallRun]
; Removes the startup task and gives TRCC its own startup back. Themes and settings stay in ProgramData.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; RunOnceId: "StartupCleanup"; Flags: waituntilterminated runhidden

[Code]
const
  Client = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  AppMutex = 'CoreVisionStudio.SingleInstance';
  UpdateWaitMs = 20000;

{ The app passes /UPDATE=1 when it starts this installer, then quits. }
function IsUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

{ Takes the place of the AppMutex directive, so an update can wait for the app to finish quitting. }
function AppStopped(): Boolean;
var
  Waited: Integer;
begin
  if IsUpdate() then
  begin
    Waited := 0;
    while CheckForMutexes(AppMutex) and (Waited < UpdateWaitMs) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  end;
  while CheckForMutexes(AppMutex) do
    if SuppressibleMsgBox('{#AppName} is running. Quit it from its tray icon, then click Retry.', mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  Result := True;
end;

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
  Result := AppStopped();
  if Result and not HasWebView2() then
    SuppressibleMsgBox('This PC needs the Microsoft Edge WebView2 Runtime for the app''s window. The cooler screen will still run from the tray, but install the runtime from Microsoft to open the window.', mbInformation, MB_OK, IDOK);
end;
