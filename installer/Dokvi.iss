#ifndef AppVersion
  #define AppVersion "2.4.0"
#endif
#ifndef AppBinary
  #define AppBinary "..\dist\Dokvi-" + AppVersion + ".exe"
#endif
#ifdef InstallTest
  #define AppName "Dokvi Installer Test"
  #define AppIdentifier "Dokvi.InstallTest"
  #define AppExe "Dokvi-InstallTest.exe"
  #define AppKey "Dokvi.InstallTest"
  #define ModelId "Dokvi.InstallTest"
  #define OutputName "Dokvi-Test-Setup-" + AppVersion
  #define OpenArguments '""--session-root={app}\test-data\shell"" --shell-probe ""%1""'
#else
  #define AppName "Dokvi"
  #define AppIdentifier "{{E42A86F6-6150-4D31-AD12-5BF77C0C5909}"
  #define AppExe "Dokvi.exe"
  #define AppKey "Dokvi"
  #define ModelId "Dokvi.Desktop"
  #define OutputName "Dokvi-Setup-" + AppVersion
  #define OpenArguments '""%1""'
#endif
#define ProgId AppKey + ".Markdown"

[Setup]
AppId={#AppIdentifier}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=TrooSlash
AppPublisherURL=https://github.com/TrooSlash
AppComments={cm:AppDescription}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern dynamic windows11
WizardSizePercent=105
SetupIconFile=..\ui\icon.ico
WizardSmallImageFile=setup-icon.png
WizardSmallImageFileDynamicDark=setup-icon-dark.png
WizardImageFile=setup-panel.png
WizardImageFileDynamicDark=setup-panel-dark.png
DisableWelcomePage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallLogMode=append
AppMutex={code:ApplicationMutex}
SetupMutex=Dokvi.Setup.{#AppKey}
CloseApplications=no
RestartApplications=no
ChangesAssociations=yes
OutputDir=..\dist
OutputBaseFilename={#OutputName}
Compression=lzma2/normal
SolidCompression=yes
LZMANumBlockThreads=1
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}.0
#ifdef Signing
SignTool=dokvi
SignedUninstaller=yes
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.AppDescription=Чтение и редактирование Markdown
english.AppDescription=Markdown reader and editor
russian.FileDescription=Markdown-документ
english.FileDescription=Markdown document
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.OpenDefaults=Выбрать Dokvi для открытия Markdown-файлов
english.OpenDefaults=Choose Dokvi to open Markdown files
russian.DefaultsHint=Dokvi установлен. Чтобы файлы .md открывались двойным щелчком, выберите Dokvi в настройках Windows.%n%nВ дальнейшем: меню «…» → «Приложение по умолчанию…».%n%nНастройки и несохранённые черновики предыдущей версии сохранены.
english.DefaultsHint=Dokvi is installed. Choose Dokvi in Windows settings to open .md files with a double click.%n%nLater: the "..." menu > Default application.%n%nYour previous settings and unsaved drafts are preserved.
russian.CloseApp=Закройте Dokvi перед установкой или обновлением. Сохраните нужные правки и повторите попытку.
english.CloseApp=Close Dokvi before installing or updating. Save your changes and try again.
russian.NewerInstalled=Установлена более новая версия Dokvi. Используйте установщик той же или более новой версии.
english.NewerInstalled=A newer version of Dokvi is installed. Use the same or a newer installer.
russian.FrameworkMissing=Для Dokvi требуется .NET Framework 4.8. Установите его с сайта Microsoft: https://dotnet.microsoft.com/download/dotnet-framework/net48
english.FrameworkMissing=Dokvi requires .NET Framework 4.8. Install it from Microsoft: https://dotnet.microsoft.com/download/dotnet-framework/net48
russian.RuntimeNote=Для отображения документов будет установлен или обновлён Microsoft Edge WebView2. Потребуется подключение к интернету.
english.RuntimeNote=Microsoft Edge WebView2 will be installed or updated to display documents. An internet connection is required.
russian.RuntimeProgress=Установка Microsoft Edge WebView2…
english.RuntimeProgress=Installing Microsoft Edge WebView2…
russian.RuntimeFailed=Не удалось установить Microsoft Edge WebView2. Проверьте интернет и повторите попытку. Можно установить компонент отдельно с сайта Microsoft: https://developer.microsoft.com/microsoft-edge/webview2/ . Установка Dokvi не выполнена.
english.RuntimeFailed=Microsoft Edge WebView2 could not be installed. Check your internet connection and try again, or install it from https://developer.microsoft.com/microsoft-edge/webview2/ . The Dokvi installation has not been completed.
russian.DataKept=Программа будет удалена. Ваши документы, настройки и черновики останутся на компьютере.
english.DataKept=The application will be removed. Your documents, settings and drafts will remain on this computer.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion
Source: "..\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ui\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\tools\webview2\LICENSE.txt"; DestDir: "{app}"; DestName: "WEBVIEW2-LICENSE.txt"; Flags: ignoreversion
Source: "..\tools\inno\License.txt"; DestDir: "{app}"; DestName: "INNO-SETUP-LICENSE.txt"; Flags: ignoreversion
Source: "..\tools\installer-downloads\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; AppUserModelID: "{#ModelId}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; AppUserModelID: "{#ModelId}"; Tasks: desktopicon

[Registry]
; Stable, version-independent identifiers keep the user's choice across upgrades.
; No UserChoice writes and no replacement of extension defaults.
Root: HKCU; Subkey: "Software\{#AppKey}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#AppKey}"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExe}"",0"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppKey}\Capabilities"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "{cm:FileDescription}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "{cm:FileDescription}"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" {#OpenArguments}"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Document"
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" {#OpenArguments}"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExe}"; ValueType: string; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletekey
#sub RegisterExtension
Root: HKCU; Subkey: "Software\Classes\.{#Extension}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".{#Extension}"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".{#Extension}"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\.{#Extension}\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
#endsub
#define Extension "md"
#emit RegisterExtension
#define Extension "markdown"
#emit RegisterExtension
#define Extension "mdown"
#emit RegisterExtension
#define Extension "mkd"
#emit RegisterExtension

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Parameters: "--default-apps"; Description: "{cm:OpenDefaults}"; Flags: nowait postinstall skipifsilent; Check: OfferDefaultChoice

[Code]
var
  WasInstalled: Boolean;
  RuntimeNote: TNewStaticText;

function ApplicationMutex(Param: String): String;
begin
#ifdef InstallTest
  Result := 'Local\Dokvi.InstallTest.Busy';
#else
  // Match the existing application identity, including pre-Dokvi portable builds.
  Result := 'Local\MDReader.Native.' + Copy(GetSHA256OfString(Utf8Encode(ExpandConstant('{localappdata}\MD Reader Native'))), 1, 20);
#endif
end;

function RuntimePresent: Boolean;
var
  Version: String;
  Key: String;
begin
#ifdef InstallTest
  if ExpandConstant('{param:TEST-RUNTIME-FAILURE|0}') = '1' then begin Result := False; Exit; end;
#endif
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := RegQueryStringValue(HKLM32, Key, 'pv', Version) and (StrToIntDef(Copy(Version, 1, Pos('.', Version) - 1), 0) >= 140);
  if not Result then
    Result := RegQueryStringValue(HKCU, Key, 'pv', Version) and (StrToIntDef(Copy(Version, 1, Pos('.', Version) - 1), 0) >= 140);
end;

function Part(var Version: String): Integer;
var P: Integer;
begin
  P := Pos('.', Version);
  if P = 0 then P := Length(Version) + 1;
  Result := StrToIntDef(Copy(Version, 1, P - 1), 0);
  Delete(Version, 1, P);
end;

function NewerThan(A, B: String): Boolean;
var I, X, Y: Integer;
begin
  Result := False;
  for I := 1 to 4 do begin
    X := Part(A); Y := Part(B);
    if X <> Y then begin Result := X > Y; Exit; end;
  end;
end;

function InitializeSetup: Boolean;
var PreviousVersion: String;
begin
  WasInstalled := RegQueryStringValue(HKCU, 'Software\{#AppKey}', 'Version', PreviousVersion);
  Result := False;
  if WasInstalled and NewerThan(PreviousVersion, '{#AppVersion}') then begin
    SuppressibleMsgBox(CustomMessage('NewerInstalled'), mbError, MB_OK, IDOK); Exit;
  end;
  if not IsDotNetInstalled(net48, 0) then begin
    SuppressibleMsgBox(CustomMessage('FrameworkMissing'), mbError, MB_OK, IDOK); Exit;
  end;
  Result := True;
end;

procedure InitializeWizard;
begin
  RuntimeNote := TNewStaticText.Create(WizardForm);
  RuntimeNote.Parent := WizardForm.SelectDirPage;
  RuntimeNote.SetBounds(0, WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(30), WizardForm.SelectDirPage.ClientWidth, ScaleY(52));
  RuntimeNote.AutoSize := False;
  RuntimeNote.WordWrap := True;
  RuntimeNote.Caption := CustomMessage('RuntimeNote');
  RuntimeNote.Visible := not RuntimePresent;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then WizardForm.FinishedLabel.Caption := CustomMessage('DefaultsHint');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if CheckForMutexes(ApplicationMutex('')) then begin Result := CustomMessage('CloseApp'); Exit; end;
  if RuntimePresent then Exit;
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  WizardForm.PreparingLabel.Caption := CustomMessage('RuntimeProgress');
#ifdef InstallTest
  if ExpandConstant('{param:TEST-RUNTIME-FAILURE|0}') = '1' then
    Log('Test fixture: failed WebView2 deployment; no shared runtime is changed')
  else
#endif
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Log('WebView2 bootstrapper could not be started')
  else
    Log(Format('WebView2 bootstrapper exit code: %d', [ExitCode]));
  if not RuntimePresent then Result := CustomMessage('RuntimeFailed');
end;

function OfferDefaultChoice: Boolean;
begin
  Result := not WasInstalled;
end;

procedure InitializeUninstallProgressForm;
begin
  UninstallProgressForm.PageDescriptionLabel.Caption := CustomMessage('DataKept');
end;
