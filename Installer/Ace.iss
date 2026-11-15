; Ace for PC installer (Inno Setup 6).
; Build-Release.ps1 runs this after it builds the "publish" folder:
;   ISCC.exe /DAppVer=4.2.0 Installer\Ace.iss
; It makes Ace-Setup.exe: installs Ace for just you (no admin needed) into %LOCALAPPDATA%\Programs\Ace,
; adds Ace to the Start menu (and the desktop if you tick it), and an uninstaller in Settings > Apps.
; Ace still updates itself from GitHub after that. Your saved stuff in %LOCALAPPDATA%\Nimbus is never touched.

#ifndef AppVer
  #define AppVer "4.2.0"
#endif

[Setup]
AppId={{87793990-A9AB-43DD-9FBF-FFA11926F3DD}
AppName=Ace
AppVersion={#AppVer}
AppVerName=Ace {#AppVer}
AppPublisher=stormblazerplayz
AppPublisherURL=https://github.com/Stormblazerplayz/Ace-PC
AppSupportURL=https://github.com/Stormblazerplayz/Ace-PC/issues
AppUpdatesURL=https://github.com/Stormblazerplayz/Ace-PC/releases
VersionInfoVersion={#AppVer}
VersionInfoProductName=Ace
DefaultDirName={localappdata}\Programs\Ace
DisableDirPage=yes
DefaultGroupName=Ace
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
OutputDir=..
OutputBaseFilename=Ace-Setup
SetupIconFile=..\Ace.ico
UninstallDisplayIcon={app}\Ace.exe
UninstallDisplayName=Ace
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; If Ace is open, close it for the install (it opens again at the end).
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Put Ace on the desktop"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Ace"; Filename: "{app}\Ace.exe"; WorkingDir: "{app}"; Comment: "Talk to Ace"
Name: "{autodesktop}\Ace"; Filename: "{app}\Ace.exe"; WorkingDir: "{app}"; Comment: "Talk to Ace"; Tasks: desktopicon

[Registry]
; Ace adds itself to "start with Windows" on its own (Settings > Waking up). Remove that when Ace is uninstalled.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Ace"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
; Logs Ace writes next to itself. Your chats and settings in %LOCALAPPDATA%\Nimbus stay.
Type: files; Name: "{app}\*.log"

[Run]
Filename: "{app}\Ace.exe"; Description: "Open Ace now"; Flags: nowait postinstall skipifsilent
