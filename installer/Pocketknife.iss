; Builds the Pocketknife installer. Run through scripts/package.ps1, which passes the defines below.
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
AppId={{B7B92E62-3A65-4440-9D01-F2B0A43B4C3F}
AppName=Pocketknife
AppVersion={#AppVersion}
AppComments=A PC multitool for Windows
AppPublisher=Kenneth Hendricks
AppPublisherURL=https://github.com/kenhendricks00/Pocketknife
AppSupportURL=https://github.com/kenhendricks00/Pocketknife/issues
DefaultDirName={autopf}\Pocketknife
DefaultGroupName=Pocketknife
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
SetupIconFile=..\src\Pocketknife.App\Assets\app.ico
WizardSmallImageFile=wizard-small-55.png,wizard-small-110.png
UninstallDisplayIcon={app}\Pocketknife.exe
OutputDir={#OutputDir}
OutputBaseFilename=Pocketknife-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Pocketknife"; Filename: "{app}\Pocketknife.exe"
Name: "{autodesktop}\Pocketknife"; Filename: "{app}\Pocketknife.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Pocketknife"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Pocketknife.exe"; Description: "{cm:LaunchProgram,Pocketknife}"; Flags: nowait postinstall skipifsilent
