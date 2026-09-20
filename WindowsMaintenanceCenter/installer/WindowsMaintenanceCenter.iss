; Windows Maintenance Center - installer definition (Inno Setup 6)
;
; Build with scripts/release.ps1, which passes AppVersion, SourceDirectory and OutputDirectory.
; The installer deliberately does:
;   - install only the files of this application (no bundled third-party tools, no drivers)
;   - offer a Start Menu entry and an optional desktop shortcut
;   - uninstall cleanly, and delete user data only after an explicit confirmation
;   - never change security settings, never install drivers, never modify the BIOS

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDirectory
  #define SourceDirectory "..\artifacts\portable"
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts\release"
#endif

#define AppName "Windows Maintenance Center"
#define AppPublisher "AOWDGENESIS"
#define AppExeName "WindowsMaintenanceCenter.exe"

[Setup]
AppId={{9F1C2A34-6F5B-4A72-9E3D-2D7A6A1C5B10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=no
DisableDirPage=no
OutputDir={#OutputDirectory}
OutputBaseFilename=WindowsMaintenanceCenter-Setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupcheck"; Description: "Start {#AppName} after the setup"; GroupDescription: "Optional"; Flags: unchecked

[Files]
; The published portable build is the single source for both delivery forms.
Source: "{#SourceDirectory}\WindowsMaintenanceCenter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\README.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; The portable marker file next to the exe switches the application to portable mode (data below
; the executable). An installed copy must keep its data in %ProgramData%\WindowsMaintenanceCenter, so the
; marker is deliberately NOT installed and the installer refuses a source folder that contains it.
Source: "{#SourceDirectory}\WindowsMaintenanceCenter.portable"; DestDir: "{tmp}"; Flags: dontcopy skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Tasks: startupcheck

[UninstallDelete]
; Nothing is deleted implicitly. The application data is handled in CurUninstallStepChanged and
; only removed after the user confirmed it - settings and audit trail are evidence, not waste.

[Code]
function InitializeSetup(): Boolean;
var
  MarkerInSource: String;
begin
  Result := True;
  MarkerInSource := ExpandConstant('{#SourceDirectory}\WindowsMaintenanceCenter.portable');
  if FileExists(MarkerInSource) then
  begin
    MsgBox('The source folder contains the portable marker file "WindowsMaintenanceCenter.portable".' + #13#10 +
           'That build stores its data next to the executable, which conflicts with an installed copy.' + #13#10#13#10 +
           'Build the installer from a source folder without the marker.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  MachineData: String;
  UserData: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    MachineData := ExpandConstant('{commonappdata}\WindowsMaintenanceCenter');
    UserData := ExpandConstant('{localappdata}\WindowsMaintenanceCenter');

    if DirExists(MachineData) or DirExists(UserData) then
    begin
      if MsgBox('Should the settings, reports and audit logs created by Windows Maintenance Center be deleted as well?' + #13#10 +
                'They live in:' + #13#10 + MachineData + #13#10 + UserData + #13#10#13#10 +
                'If you keep them, a later installation can continue with the same settings and audit trail.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(UserData, True, True, True);
        DelTree(MachineData, True, True, True);
      end;
    end;
  end;
end;
