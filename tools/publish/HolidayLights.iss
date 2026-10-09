; The Holiday Lights setup (owner: app-shell; PRODUCT-SPEC 6.10): one self-contained .exe for the release.
;
; Inno Setup only carries and unpacks the program. The setup unpacks the distribution folder into its temporary folder,
; runs the program's own per-user installer from there (HolidayLights.exe --install) and waits for it, then removes the
; temporary folder. It asks nothing itself (it shows a progress bar while it unpacks), needs no administrator
; rights, writes nothing to the registry and has no uninstaller, so Windows Settings > Apps keeps the one entry the
; program's installer writes, and installing over an earlier version updates it.
;
; /SILENT and /VERYSILENT install without a window (--install --quiet). Arguments that do not start with "/" go to the
; installer unchanged, for example --data-root <folder> --no-system-changes for tests. The exit code is the installer's:
; 0 when installed, 1 when cancelled or failed.
;
; tools/publish/publish.ps1 compiles it:
;   ISCC.exe /DAppVersion=6.0.2 /DFileVersion=6.0.2.0 /DSourceDir=<distribution folder> /DOutputDir=<folder> HolidayLights.iss

#ifndef AppVersion
  #error Define AppVersion (/DAppVersion=6.0.2).
#endif
#ifndef FileVersion
  #error Define FileVersion (/DFileVersion=6.0.2.0).
#endif
#ifndef SourceDir
  #error Define SourceDir (/DSourceDir=<distribution folder>).
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId=HolidayLightsSetup
AppName=Holiday Lights
AppVersion={#AppVersion}
AppVerName=Holiday Lights {#AppVersion}
AppPublisher=StarrLord
AppPublisherURL=https://github.com/starrlord/holidaylights
AppCopyright=Holiday Lights 6 (c) 2026 StarrLord. Based on Holiday Lights 5.4 (c) 1993-2003 Tiger Technologies.
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoProductName=Holiday Lights
VersionInfoDescription=Holiday Lights Setup
VersionInfoCompany=StarrLord
OutputDir={#OutputDir}
OutputBaseFilename=HolidayLights-{#AppVersion}-Setup
SetupIconFile=..\..\src\HolidayLights.App\Assets\HolidayLights.ico
WizardStyle=modern dynamic windows11
WizardSmallImageFile=..\..\assets\icons\png\app-256.png
WizardSmallImageFileDynamicDark=..\..\assets\icons\png\app-256.png
WizardSizePercent=100
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CreateAppDir=no
Uninstallable=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
DisableProgramGroupPage=yes
ShowLanguageDialog=no
CloseApplications=no
RestartApplications=no
SetupMutex=HolidayLightsSetup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle=Holiday Lights Setup
SetupWindowTitle=Holiday Lights Setup
WizardInstalling=Getting Ready
InstallingLabel=Please wait while Holiday Lights Setup gets ready.
StatusExtractFiles=Unpacking files...
ExitSetupMessage=Holiday Lights Setup is not finished. If you exit now, Holiday Lights will not be installed.%n%nExit Setup?

[Files]
Source: "{#SourceDir}\*"; DestDir: "{tmp}\HolidayLights"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
var
  InstallerExitCode: Integer;

function AllowSetForegroundWindow(ProcessId: DWORD): BOOL;
  external 'AllowSetForegroundWindow@user32.dll stdcall';

{ One argument for the installer's command line, quoted when it holds a space (a final backslash doubled so that it
  does not escape the closing quote). }
function QuoteArgument(Argument: String): String;
begin
  if Pos(' ', Argument) = 0 then
    Result := Argument
  else
  begin
    if Argument[Length(Argument)] = '\' then
      Argument := Argument + '\';
    Result := '"' + Argument + '"';
  end;
end;

{ Setup's own switches start with "/"; everything else is meant for the installer. }
function ForwardedArguments: String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to ParamCount do
    if (ParamStr(I) <> '') and (Copy(ParamStr(I), 1, 1) <> '/') then
      Result := Result + ' ' + QuoteArgument(ParamStr(I));
end;

{ Inno Setup shows the Ready page when every page before it is turned off; there is nothing to choose, so it presses
  Install itself once the window is up (a click during the page change is ignored). }
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
    PostMessage(WizardForm.NextButton.Handle, $00F5 { BM_CLICK }, 0, 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, Arguments: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  Folder := ExpandConstant('{tmp}\HolidayLights');
  Arguments := '--install';
  if WizardSilent then
    Arguments := Arguments + ' --quiet';
  Arguments := Arguments + ForwardedArguments;

  { The installer's window takes over from here, in front: this window passes on its right to the foreground before it
    goes (ASFW_ANY). }
  AllowSetForegroundWindow($FFFFFFFF);
  WizardForm.Hide;
  if not Exec(Folder + '\HolidayLights.exe', Arguments, Folder, SW_SHOWNORMAL, ewWaitUntilTerminated, InstallerExitCode) then
  begin
    SuppressibleMsgBox('Holiday Lights Setup could not start the installer: ' + SysErrorMessage(InstallerExitCode), mbCriticalError, MB_OK, IDOK);
    InstallerExitCode := 1;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := InstallerExitCode;
end;
