; Inno Setup script for the RapidRAW plugin for Logi Options+.
; Built by tools\build-installer.ps1, which passes MyAppVersion and SourceDir in.
;
; What the installer does:
;   - per-user install (no admin), default folder %LOCALAPPDATA%\Programs\RapidRAW Logi Plugin,
;     changeable on the directory page
;   - copies the built plugin (bin\, metadata\, actionsymbols\, actionicons\) and tools\
;   - writes %LOCALAPPDATA%\Logi\LogiPluginService\Plugins\RapidRawPlugin.link pointing at
;     the install folder, which is how the Logi Plugin Service discovers a plugin
;   - Start Menu shortcuts: "Apply Lightroom icons", "Restore original icons", uninstall;
;     optional desktop shortcuts for the two icon actions
;   - asks Options+ to reload the plugin at the end
;   - the uninstaller removes the .link, the icon backup, and the install folder

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\bin\Release"
#endif
#ifndef RepoDir
  #define RepoDir ".."
#endif

#define MyAppName "RapidRAW plugin for Logi Options+"
#define MyAppShortName "RapidRAW Logi Plugin"
#define MyAppPublisher "Alex"
#define MyAppURL "https://github.com/jiyang1018/rapidraw-logi-plugin"
#define PluginLinkName "RapidRawPlugin.link"

[Setup]
AppId={{7A9E3C1B-52D4-4E0F-9B6A-2C8F1D3E5A70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppShortName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppShortName}
DefaultGroupName={#MyAppShortName}
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#RepoDir}\dist
OutputBaseFilename=RapidRawPlugin-Setup-{#MyAppVersion}
SetupIconFile=RapidRawPlugin.ico
UninstallDisplayIcon={app}\RapidRawPlugin.ico
UninstallDisplayName={#MyAppShortName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#RepoDir}\LICENSE
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicons"; Description: "Create desktop shortcuts for the icon switch (Apply / Restore)"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The built plugin, in the two-level layout the Logi Plugin Service expects.
Source: "{#SourceDir}\bin\*";           DestDir: "{app}\bin";           Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\metadata\*";      DestDir: "{app}\metadata";      Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\actionsymbols\*"; DestDir: "{app}\actionsymbols"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\actionicons\*";   DestDir: "{app}\actionicons";   Flags: ignoreversion recursesubdirs
; The optional icon switch and its docs.
Source: "{#RepoDir}\tools\use-logi-icons.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#RepoDir}\README.md";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoDir}\LICENSE";    DestDir: "{app}"; Flags: ignoreversion
Source: "RapidRawPlugin.ico";    DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Apply Lightroom icons";   Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\use-logi-icons.ps1"" -Pause";          WorkingDir: "{app}"; IconFilename: "{app}\RapidRawPlugin.ico"; Comment: "Borrow the action icons from Lightroom Classic by Logi (must be installed in Options+)"
Name: "{group}\Restore original icons";  Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\use-logi-icons.ps1"" -Restore -Pause"; WorkingDir: "{app}"; IconFilename: "{app}\RapidRawPlugin.ico"; Comment: "Put the plugin's own icons back"
Name: "{group}\Read me";                 Filename: "{app}\README.md"
Name: "{group}\Uninstall {#MyAppShortName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\RapidRAW plugin - Apply Lightroom icons";  Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\use-logi-icons.ps1"" -Pause";          WorkingDir: "{app}"; IconFilename: "{app}\RapidRawPlugin.ico"; Tasks: desktopicons
Name: "{userdesktop}\RapidRAW plugin - Restore original icons"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\use-logi-icons.ps1"" -Restore -Pause"; WorkingDir: "{app}"; IconFilename: "{app}\RapidRawPlugin.ico"; Tasks: desktopicons

[Run]
Filename: "loupedeck:plugin/RapidRaw/reload"; Flags: shellexec nowait skipifsilent; StatusMsg: "Asking Logi Options+ to load the plugin..."

[UninstallDelete]
Type: files;          Name: "{localappdata}\Logi\LogiPluginService\Plugins\{#PluginLinkName}"
Type: filesandordirs; Name: "{app}\icons-backup"
Type: filesandordirs; Name: "{app}\logs"

[UninstallRun]
Filename: "loupedeck:plugin/RapidRaw/reload"; Flags: shellexec nowait; RunOnceId: "ReloadAfterUninstall"

[Code]
// The Logi Plugin Service discovers sideloaded plugins through a .link file in its
// Plugins folder whose content is the plugin base directory (trailing backslash, as
// the build writes it). Written after the files are in place.
procedure WriteLinkFile();
var
  LinkDir, LinkPath, Target: String;
begin
  LinkDir := ExpandConstant('{localappdata}\Logi\LogiPluginService\Plugins');
  if not DirExists(LinkDir) then
    ForceDirectories(LinkDir);
  LinkPath := LinkDir + '\{#PluginLinkName}';
  Target := ExpandConstant('{app}') + '\';
  if not SaveStringToFile(LinkPath, Target, False) then
    MsgBox('Could not write ' + LinkPath + #13#10 +
           'Create it by hand with this single line as its content:' + #13#10 + Target,
           mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteLinkFile();
end;

function InitializeSetup(): Boolean;
var
  ServiceDir: String;
begin
  Result := True;
  ServiceDir := ExpandConstant('{localappdata}\Logi\LogiPluginService');
  if not DirExists(ServiceDir) then
  begin
    if MsgBox('Logi Options+ (with the Logi Plugin Service) does not seem to be installed for this user.' + #13#10#13#10 +
              'The plugin needs it to run. Install Options+ first, launch it once, then run this setup again.' + #13#10#13#10 +
              'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
