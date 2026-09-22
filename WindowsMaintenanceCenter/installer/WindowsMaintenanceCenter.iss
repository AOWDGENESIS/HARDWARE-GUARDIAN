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
  ; Whoever compiles this file by hand gets the program folder of the release, not the portable one:
  ; the portable artefact carries its marker inside and would be refused below.
  #define SourceDirectory "..\artifacts\install"
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts\release"
#endif

#define AppName "Windows Maintenance Center"
#define AppPublisher "AOWDGENESIS"
#define AppExeName "WindowsMaintenanceCenter.exe"

; A source folder that contains the portable marker file would install a program that keeps its data
; next to the executable - inside the program folder. That is refused while the setup is compiled.
;
; Until 2026-09-22 this was a runtime check inside InitializeSetup, and it could never fire: the guard
; tested the relative source path of the build machine against the working directory of the machine
; that started the setup. A guard that cannot fire is worse than no guard, because it looks like one.
#if FileExists(SourceDirectory + "\WindowsMaintenanceCenter.portable")
  #error The installer source folder contains the portable marker file. An installed copy must not be portable: publish the program folder without -Portable.
#endif

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
; The program folder is published without the portable marker (scripts/release.ps1 publishes it with
; -OutputDirectory artifacts/install and without -Portable), so the installed copy uses the installed
; layout: data below %ProgramData%\WindowsMaintenanceCenter, never next to the executable.
Source: "{#SourceDirectory}\WindowsMaintenanceCenter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\README.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

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
