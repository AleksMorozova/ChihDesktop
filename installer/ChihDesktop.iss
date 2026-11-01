; Requires Inno Setup 6.4 or newer. Compile only through Build-Installer.ps1.
#ifndef PublishDir
  #error PublishDir must name a verified self-contained publish directory
#endif
#ifndef InstallerOutput
  #error InstallerOutput must name the artifacts output directory
#endif
#ifndef WebViewBootstrapper
  #error WebViewBootstrapper must name the verified Microsoft bootstrapper
#endif
#ifndef BootstrapperSHA256
  #error BootstrapperSHA256 is required
#endif
#ifndef AppVersion
  #error AppVersion must match the verified published executable
#endif

[Setup]
; Permanent product identity: never replace this GUID for an upgrade.
AppId={{DBB073A1-919D-4B98-BF22-6E77BDF22C23}
AppName=ChihDesktop
AppVersion={#AppVersion}
AppPublisher=ChihDesktop contributors
DefaultDirName={localappdata}\Programs\ChihDesktop
DefaultGroupName=ChihDesktop
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
Uninstallable=yes
UninstallDisplayIcon={app}\ChihDesktop.exe
AppMutex=Local\ChihDesktop.DesktopPet
SetupMutex=Local\ChihDesktop.Setup
CloseApplications=no
RestartApplications=no
OutputDir={#InstallerOutput}
OutputBaseFilename=ChihDesktop-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
InfoBeforeFile=..\ASSET-LICENSE.md
SetupIconFile=..\Assets\Branding\chih-icon.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
Name: "webview2"; Description: "Установить Microsoft WebView2 для планировщика (нужен интернет). Без него Чих и нативные напоминания продолжат работать."; Flags: unchecked; Check: NeedsWebView2

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebViewBootstrapper}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\ChihDesktop\ChihDesktop"; Filename: "{app}\ChihDesktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ChihDesktop.exe"; IconIndex: 0
Name: "{userdesktop}\ChihDesktop"; Filename: "{app}\ChihDesktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ChihDesktop.exe"; IconIndex: 0; Tasks: desktopicon

[Run]
Filename: "{app}\ChihDesktop.exe"; Description: "Запустить ChihDesktop"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{app}\ChihDesktop.exe"; Parameters: "--remove-autostart"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveChihDesktopAutostart"

; No Registry/InstallDelete/UninstallDelete entries. Never delete or seed the
; separate {localappdata}\ChihDesktop data directory or change startup on install.
[Code]
const
  WebViewClient = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasRuntimeAt(Root: Integer): Boolean;
var
  Version: String;
  Dot: Integer;
begin
  Result := False;
  if RegQueryStringValue(Root, WebViewClient, 'pv', Version) then begin
    Dot := Pos('.', Version);
    if Dot > 1 then Result := StrToIntDef(Copy(Version, 1, Dot - 1), 0) > 0;
  end;
end;

function NeedsWebView2: Boolean;
begin
  Result := not (HasRuntimeAt(HKLM32) or HasRuntimeAt(HKLM64) or
    HasRuntimeAt(HKCU32) or HasRuntimeAt(HKCU64));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Bootstrapper: String;
begin
  if (CurStep <> ssPostInstall) or not NeedsWebView2 then Exit;
  if WizardIsTaskSelected('webview2') then begin
    try
      ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
      Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      if CompareText(GetSHA256OfFile(Bootstrapper), '{#BootstrapperSHA256}') <> 0 then
        RaiseException('Проверка Microsoft WebView2 bootstrapper не пройдена.');
      if not Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
        Log('WebView2 bootstrapper could not be started.')
      else
        Log(Format('WebView2 bootstrapper exit code: %d', [ExitCode]));
    except
      Log('WebView2 bootstrapper failed. The native pet remains available.');
    end;
  end;
  if NeedsWebView2 then
    SuppressibleMsgBox('Microsoft WebView2 пока недоступен. Чих, настройки и нативные напоминания работают без него.' + #13#10 +
      'Для планировщика установите Evergreen WebView2 Runtime с официального сайта Microsoft. Онлайн bootstrapper требует интернет; для офлайн-компьютера используйте Evergreen Standalone Installer x64.' + #13#10 +
      'https://developer.microsoft.com/microsoft-edge/webview2/', mbInformation, MB_OK, IDOK);
end;
