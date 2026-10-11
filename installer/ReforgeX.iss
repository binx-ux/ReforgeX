#define MyAppName "ReforgeX"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "anthony price"
#define MyAppURL "https://github.com/binx-ux/ReforgeX"
#define MyAppExeName "ReforgeX.exe"

[Setup]
AppId={{7E4A1C90-2B6D-4F83-9A15-C8D03E6B41A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\ReforgeX
DefaultGroupName=ReforgeX
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ReforgeX-Setup
SetupIconFile=..\Assets\reforgex.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\ReforgeX\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ReforgeX"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall ReforgeX"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ReforgeX"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Open ReforgeX"; Flags: nowait postinstall skipifsilent
