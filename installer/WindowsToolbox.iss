; Builds the Windows Toolbox installer. Run through scripts/package.ps1, which passes the defines below.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif

[Setup]
AppId={{6E4B3B5C-2F3A-4C61-9B7E-8D1F0A2C5E47}
AppName=Windows Toolbox
AppVersion={#AppVersion}
AppPublisher=Kenneth Hendricks
AppPublisherURL=https://github.com/kenhendricks00/windows-toolbox
AppSupportURL=https://github.com/kenhendricks00/windows-toolbox/issues
DefaultDirName={autopf}\Windows Toolbox
DefaultGroupName=Windows Toolbox
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
SetupIconFile=..\src\Toolbox.App\Assets\app.ico
UninstallDisplayIcon={app}\WindowsToolbox.exe
OutputDir={#OutputDir}
OutputBaseFilename=WindowsToolbox-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Windows Toolbox"; Filename: "{app}\WindowsToolbox.exe"
Name: "{autodesktop}\Windows Toolbox"; Filename: "{app}\WindowsToolbox.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WindowsToolbox"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\WindowsToolbox.exe"; Description: "{cm:LaunchProgram,Windows Toolbox}"; Flags: nowait postinstall skipifsilent
