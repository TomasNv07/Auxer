; Auxer installer (Inno Setup 6). Build with tools\build-release.ps1, or:
;   ISCC.exe installer\Auxer.iss /DAppVersion=1.0.0
; Per-user install (no admin rights). Downloads the .NET 9 Desktop Runtime only if it's missing.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Auxer"
#define AppExe "Auxer.exe"
#define RuntimeUrl "https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe"
#define RuntimeFile "windowsdesktop-runtime-win-x64.exe"

[Setup]
; AppId identifies the app for upgrades/uninstall. Never change it.
AppId={{E0A55137-D2DA-448A-BFBD-BC00D22C7EB8}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=TomasNv07
AppPublisherURL=https://github.com/TomasNv07/Auxer
AppSupportURL=https://github.com/TomasNv07/Auxer/issues
AppUpdatesURL=https://github.com/TomasNv07/Auxer/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=Auxer-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
WizardSizePercent=100

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Auxer with Windows (in the tray)"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same startup entry the app's own "Start with Windows" switch uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Auxer"; \
  ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Auxer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "CloseAuxer"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

{ After "Install" is clicked: fetch and install the .NET 9 Desktop Runtime if it's missing. }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or DesktopRuntimeInstalled then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', '{#RuntimeFile}', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DownloadPage.SetText('Installing the .NET 9 Desktop Runtime...', 'Auxer needs it to run. Approve the Windows prompt if one appears.');
      if not ShellExec('', ExpandConstant('{tmp}\{#RuntimeFile}'), '/install /quiet /norestart', '',
                       SW_SHOW, ewWaitUntilTerminated, ResultCode)
         or ((ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638)) then
      begin
        Result := MsgBox('The .NET 9 Desktop Runtime could not be installed (code ' + IntToStr(ResultCode) + ').' + #13#10 +
                         'You can install it later from https://dotnet.microsoft.com/download/dotnet/9.0' + #13#10#13#10 +
                         'Continue installing Auxer anyway?', mbError, MB_YESNO) = IDYES;
      end;
    except
      if DownloadPage.AbortedByUser then
        Log('Runtime download cancelled.')
      else
        SuppressibleMsgBox('Could not download the .NET 9 Desktop Runtime: ' + GetExceptionMessage, mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ Close a running Auxer before replacing its files (upgrades). }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Settings: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  { The app can also add itself to startup from its Settings; remove that entry too. }
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Auxer');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Auxer');

  Settings := ExpandConstant('{userappdata}\Auxer');
  if (not UninstallSilent) and DirExists(Settings) then
    if MsgBox('Also delete your Auxer settings and keybinds?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Settings, True, True, True);
end;
