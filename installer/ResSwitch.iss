; ResSwitch installer (Inno Setup 6). Build with:  ..\build.ps1   (or ISCC.exe /DAppVersion=1.0.0 ResSwitch.iss)
; NOTE: this file contains accented characters and must stay UTF-8 with BOM.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B6D1D9E2-5C0A-4E1B-9B53-3F6A8A0C7D11}
AppName=ResSwitch
AppVersion={#AppVersion}
AppVerName=ResSwitch {#AppVersion}
AppPublisher=ResSwitch
; version resource of Setup.exe: product name and version must match ResSwitch.exe (checked by code signing services)
VersionInfoProductName=ResSwitch
VersionInfoDescription=ResSwitch Setup
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoProductTextVersion={#AppVersion}.0
; per-user by default (%LocalAppData%\Programs\ResSwitch, no admin); the user may choose Program Files
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\ResSwitch
DefaultGroupName=ResSwitch
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ResSwitch-Setup
SetupIconFile=..\ResSwitch\app.ico
UninstallDisplayIcon={app}\ResSwitch.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
; the running instance is closed by our own code below ("ResSwitch.exe /quit")
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
english.StartWithWindows=Start ResSwitch when Windows starts
english.RemoveSettings=Also delete your ResSwitch settings (profiles, shortcut, game rules)?
french.StartWithWindows=Lancer ResSwitch au démarrage de Windows
french.RemoveSettings=Supprimer aussi vos réglages ResSwitch (profils, raccourci, règles de jeux) ?
german.StartWithWindows=ResSwitch beim Start von Windows starten
german.RemoveSettings=Auch Ihre ResSwitch-Einstellungen löschen (Profile, Tastenkürzel, Spielregeln)?
spanish.StartWithWindows=Iniciar ResSwitch al arrancar Windows
spanish.RemoveSettings=¿Eliminar también sus ajustes de ResSwitch (perfiles, atajo, reglas de juegos)?
italian.StartWithWindows=Avvia ResSwitch all'avvio di Windows
italian.RemoveSettings=Eliminare anche le impostazioni di ResSwitch (profili, scorciatoia, regole dei giochi)?
portuguese.StartWithWindows=Iniciar o ResSwitch com o Windows
portuguese.RemoveSettings=Eliminar também as suas definições do ResSwitch (perfis, atalho, regras de jogos)?
dutch.StartWithWindows=ResSwitch starten wanneer Windows start
dutch.RemoveSettings=Ook uw ResSwitch-instellingen verwijderen (profielen, sneltoets, gameregels)?
polish.StartWithWindows=Uruchamiaj ResSwitch wraz z systemem Windows
polish.RemoveSettings=Usunąć także ustawienia ResSwitch (profile, skrót, reguły gier)?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\ResSwitch.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{autoprograms}\ResSwitch"; Filename: "{app}\ResSwitch.exe"
Name: "{autodesktop}\ResSwitch"; Filename: "{app}\ResSwitch.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ResSwitch"; ValueData: """{app}\ResSwitch.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ResSwitch.exe"; Description: "{cm:LaunchProgram,ResSwitch}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CloseRunningInstance;
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\ResSwitch.exe');
  if FileExists(Exe) then
    Exec(Exe, '/quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningInstance;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningInstance;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    // also covers a startup entry enabled later from the app's own settings
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ResSwitch');
  if CurUninstallStep = usPostUninstall then
  begin
    if not UninstallSilent then
      if MsgBox(ExpandConstant('{cm:RemoveSettings}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{userappdata}\ResSwitch'), True, True, True);
  end;
end;
