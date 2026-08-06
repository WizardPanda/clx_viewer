; Clx Viewer installer (Inno Setup 6)
; Per-user, no admin. Build both variants with:
;   iscc clx-viewer.iss /DAppVersion=1.0.0 /DVariant=fd /DSourceDir=...\fd /DOutputDir=... 
;   iscc clx-viewer.iss /DAppVersion=1.0.0 /DVariant=sc /DSourceDir=...\sc /DOutputDir=...
;
; Variant is "fd" (framework-dependent) or "sc" (self-contained).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Variant
  #define Variant "fd"
#endif
#ifndef SourceDir
  #error "SourceDir must be defined (staged publish output)"
#endif
#ifndef OutputDir
  #define OutputDir "dist"
#endif

#define AppName "Clx Viewer"
#define ClxClsid "{{6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}"
#define ThumbKey "{{E357FCCD-A995-4576-B01F-234630154E96}"

; The net48 variant (.NET Framework 4.8) is a separate product: distinct AppId
; and install directory so it doesn't collide with the .NET 8 build.
#ifdef Net48
  #define ClxAppId "{{191EE1BB-8891-4CBE-9C9A-98149F59E9AA}"
  #define ClxDirName "Clx Viewer (Net48)"
#else
  #define ClxAppId "{{B9C8A2AC-7801-45C0-97A5-93A490686C17}"
  #define ClxDirName "Clx Viewer"
#endif

[Setup]
AppId={#ClxAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WizardPanda
AppPublisherURL=https://github.com/WizardPanda/clx_viewer
DefaultDirName={localappdata}\Programs\{#ClxDirName}
DefaultGroupName={#ClxDirName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ClxViewer-{#AppVersion}-win-x64-{#Variant}-installer
SetupIconFile=..\assets\clxviewer.ico
UninstallDisplayIcon={app}\ClxViewer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ClxViewer.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ClxViewer.exe"; Tasks: desktopicon

[Registry]
; Thumbnail provider COM server
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClxClsid}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClxClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\ClinxThumbnailProvider.dll"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{#ClxClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
; .clx shellex thumbnail handler (+ name alias)
Root: HKCU; Subkey: "Software\Classes\.clx\shellex\{#ThumbKey}"; ValueType: string; ValueName: ""; ValueData: "{#ClxClsid}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.clx\shellex\ThumbnailHandler"; ValueType: string; ValueName: ""; ValueData: "{#ClxClsid}"; Flags: uninsdeletekey
; .clx file association -> ClxViewer.Document
Root: HKCU; Subkey: "Software\Classes\.clx"; ValueType: string; ValueName: ""; ValueData: "ClxViewer.Document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.clx\OpenWithProgids"; ValueType: string; ValueName: "ClxViewer.Document"; ValueData: ""
; ProgID
Root: HKCU; Subkey: "Software\Classes\ClxViewer.Document"; ValueType: string; ValueName: ""; ValueData: "Clinx CLX Capture"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ClxViewer.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ClxViewer.exe"" ""%1"""; Flags: uninsdeletekey

[Code]
procedure SHChangeNotify(wEventId: DWORD; uFlags: DWORD; dwItem1: DWORD; dwItem2: DWORD);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure NotifyShell();
begin
  SHChangeNotify($08000000, 0, 0, 0);  // SHCNE_ASSOCCHANGED
end;

function HasDotNet8Folder(Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if DirExists(Dir) and FindFirst(Dir + '8.*', FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
  end;
end;

// The InstalledVersions registry key is only written by the MSI installer, so
// detect via the shared-framework folders instead (machine + per-user).
function IsDotNet8DesktopInstalled(): Boolean;
begin
  Result := HasDotNet8Folder('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\') or
            HasDotNet8Folder(ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App\'));
end;

function IsSilent(): Boolean;
begin
  Result := (Pos('/VERYSILENT', UpperCase(GetCmdTail())) > 0) or
            (Pos('/SILENT', UpperCase(GetCmdTail())) > 0);
end;

procedure InstallDotNet8DesktopRuntime();
var
  ErrorCode: Integer;
  TmpDir: String;
  RuntimeExe: String;
begin
  TmpDir := ExpandConstant('{tmp}');
  RuntimeExe := TmpDir + '\dotnet8-desktop.exe';
  // curl.exe ships with Windows 10 1803+.
  if Exec('curl.exe', '-sSL -o "' + RuntimeExe + '" https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
      '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
  begin
    if Exec(RuntimeExe, '/install /quiet /norestart', '', SW_HIDE,
        ewWaitUntilTerminated, ErrorCode) then
    begin
      Log('Installed .NET 8 Desktop Runtime (exit ' + IntToStr(ErrorCode) + ')');
    end;
  end
  else
  begin
    MsgBox('Could not download the .NET 8 Desktop Runtime (curl exit ' + IntToStr(ErrorCode) + '). ' +
           'The viewer will not run until it is installed.',
           mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if LowerCase('{#Variant}') = 'fd' then
  begin
    if not IsDotNet8DesktopInstalled() then
    begin
      if IsSilent() then
      begin
        // Fully silent installs: bootstrap the runtime without prompting.
        InstallDotNet8DesktopRuntime();
      end
      else if MsgBox('Clx Viewer needs the .NET 8 Desktop Runtime, which is not installed.' + #13#10#13#10 +
                'Install it now (downloads the official Microsoft installer)?',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        InstallDotNet8DesktopRuntime();
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    NotifyShell();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    NotifyShell();
end;
