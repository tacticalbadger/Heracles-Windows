; Heracles for Windows: the installer (Inno Setup 7).
;
; Compiled by "tools\build.ps1 package", never by hand: the build says what
; to pack and what to call it.
;
;   ISCC --define=Version=0.3.1 --define=Build=53 --define=Arch=arm64
;        --define=Stage=<the packaged folder> --define=Icon=<Heracles.ico>
;        --define=OutputDir=<folder>
;        --define=OutputName=Heracles-0.3.1-win-arm64-setup Heracles.iss
;
; What it makes installs for the current user only, with no administrator:
; into %LOCALAPPDATA%\Programs\Heracles unless another folder is chosen. The
; app replaces its own folder when it updates itself, so it has to live
; where its user may write, which is also why this is not a Windows
; Installer package: that would put back, or complain about, every file an
; update changed.
;
; Three things follow from the app replacing its whole folder (two renames:
; see app\Heracles\Updates\UpdateInstaller.cs):
;  - The uninstaller is not kept in the app's folder, where the first update
;    would take it away, but in %LOCALAPPDATA%\Heracles\uninstall.
;  - The folder is the app's alone. The installer refuses one that holds
;    other things, replaces all of one that holds Heracles, and the
;    uninstaller removes the folder whole: after an update it holds files
;    this installer never wrote.
;  - The version under "Installed apps" is kept right by the app at start
;    (app\Heracles\Support\InstalledCopy.cs), which has the AppId below too;
;    a test compares the two. Never change the AppId: it is how Windows, and
;    the next installer, know an installed copy.

#ifndef Version
  #error Compile this through tools\build.ps1 package: it passes Version, Build, Arch, Stage, Icon, OutputDir and OutputName.
#endif

#define AppName "Heracles"
#define AppExe "Heracles.exe"
#define AppId "{7E7666E6-6C97-4920-8107-E156E0BFCBB0}"
#define Repository "https://github.com/tacticalbadger/Heracles-Windows"

#if Arch == "arm64"
  #define Processors "arm64"
#elif Arch == "x64"
  #define Processors "x64compatible"
#else
  #error Arch must be arm64 or x64.
#endif

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher=tacticalbadger
AppPublisherURL={#Repository}
AppSupportURL={#Repository}
AppUpdatesURL={#Repository}/releases
VersionInfoVersion={#Version}.{#Build}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} {#Version} Setup ({#Arch})
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile={#Icon}

; For this user, without an administrator, somewhere the app can update itself.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; PrepareToInstall below says more than "the folder exists".
DirExistsWarning=no
; Not in the app's folder: the app replaces that folder when it updates.
UninstallFilesDir={localappdata}\{#AppName}\uninstall

ArchitecturesAllowed={#Processors}
ArchitecturesInstallIn64BitMode={#Processors}
MinVersion=10.0.19041

; The app is asked to be closed, in words, rather than closed by Windows.
CloseApplications=no

SourceDir={#Stage}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/max
SolidCompression=yes
; Setup's own tables are left readable, so that the build can look through
; what it hands out for paths of the machine it was built on.
InternalCompressLevel=none
WizardStyle=modern dynamic

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Over a copy that is already there: all of it goes first, as it does when
; the app updates itself. PrepareToInstall has made sure the folder is the
; app's.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app's folder, whole (see the top of this file), and an update that was
; fetched and never installed. What is the user's stays: the library
; (%APPDATA%\Heracles), and beside the log what they chose about updates
; (updates\preferences.json), which a copy of the app elsewhere on this PC
; goes by as well.
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\{#AppName}\updates\staged"
Type: files; Name: "{localappdata}\{#AppName}\updates\download.part"
Type: dirifempty; Name: "{localappdata}\{#AppName}\uninstall"

[Code]
const
  ClassesKey = 'Software\Classes';
  LinkScheme = 'heracles';
  IdentityKey = 'AppUserModelId\com.heracles.notes.native';
  { The file that says a folder beside the app's is the updater's own. }
  UpdateMarker = '.heracles-update';

{ Whether a program is running from this file: Windows lets nobody open a
  running program for writing. }
function InUse(const FileName: String): Boolean;
var
  Stream: TFileStream;
begin
  Result := False;
  if not FileExists(FileName) then Exit;
  try
    Stream := TFileStream.Create(FileName, fmOpenReadWrite or fmShareExclusive);
    Stream.Free;
  except
    Result := True;
  end;
end;

function HasEntries(const Folder: String): Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if FindFirst(Folder + '\*', Found) then begin
    try
      repeat
        if (Found.Name <> '.') and (Found.Name <> '..') then Result := True;
      until Result or not FindNext(Found);
    finally
      FindClose(Found);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  App: String;
begin
  Result := '';
  App := ExpandConstant('{app}');
  if DirExists(App) and HasEntries(App) and not FileExists(App + '\{#AppExe}') then
    Result := 'The folder ' + App + ' holds other things. {#AppName} needs a folder of its own: choose an empty one, or a new one.'
  else if InUse(App + '\{#AppExe}') then
    Result := '{#AppName} is running from ' + App + '. Close it, then run this installer again.';
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if InUse(ExpandConstant('{app}\{#AppExe}')) then begin
    SuppressibleMsgBox('{#AppName} is running. Close it, then uninstall again.', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ What the app wrote in the registry for itself (its link scheme, and the
  name its reminders go by) is removed with it, but only where it points at
  this copy: another copy of the app, somewhere else, may have been started
  since, and then these are that copy's. }
procedure RemoveRegistrations(const App: String);
var
  Value: String;
begin
  if RegQueryStringValue(HKEY_CURRENT_USER, ClassesKey + '\' + LinkScheme + '\shell\open\command', '', Value) then
    if Pos(Lowercase(App + '\'), Lowercase(Value)) > 0 then
      RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, ClassesKey + '\' + LinkScheme);
  if RegQueryStringValue(HKEY_CURRENT_USER, ClassesKey + '\' + IdentityKey, 'IconUri', Value) then
    if Pos(Lowercase(App + '\'), Lowercase(Value)) = 1 then
      RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, ClassesKey + '\' + IdentityKey);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  App, Beside: String;
begin
  if CurUninstallStep = usUninstall then begin
    App := ExpandConstant('{app}');
    RemoveRegistrations(App);
    { What an update left beside the app's folder, if the app never started
      again to clear it away. Only the updater's own: it marks them. }
    Beside := App + '.update-old';
    if FileExists(Beside + '\' + UpdateMarker) then DelTree(Beside, True, True, True);
    Beside := App + '.update-new';
    if FileExists(Beside + '\' + UpdateMarker) then DelTree(Beside, True, True, True);
  end;
end;
