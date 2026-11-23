; Inno Setup script for Ferdium Plus
; Compile via:
;   ISCC.exe /DMyAppVersion="1.0.0" /DSourceDir="..\publish\app" /DOutputDir="..\release-assets" /DOutputBaseFilename="FerdiumPlus-Setup-1.0.0-win-x64" installer\FerdiumPlus.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "1.1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\release-assets"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "FerdiumPlus-Setup-" + MyAppVersion + "-win-x64"
#endif

#define MyAppName "Ferdium Plus"
#define MyAppPublisher "Ferdium Plus"
#define MyAppURL "https://github.com/piyushagade/Ferdium-Plus"
#define MyAppExeName "FerdiumPlus.exe"

[Setup]
; Stable GUID so upgrades replace existing installations cleanly
AppId={{8E9B7C54-3F42-4A9E-B2D1-6F0C5A1E8D32}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Default to per-user install (no UAC prompt), allow user to elevate for all-users install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\FerdiumPlus\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FerdiumPlus"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
