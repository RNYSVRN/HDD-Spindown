; Installer for HDD-Spindown (Inno Setup 6)
; Build: powershell -ExecutionPolicy Bypass -File installer\build.ps1
; Expects the published app in ..\dist\publish (see build.ps1).

#define AppName      "HDD-Spindown"
#define AppExe       "HddSpindown.exe"
#define AppPublisher "Ronny Severin"
#define AppUrl       "https://github.com/RNYSVRN/HDD-Spindown"
#define PublishDir   "..\dist\publish"
#define VerMajor
#define VerMinor
#define VerRev
#define VerBuild
#expr GetVersionComponents(PublishDir + "\" + AppExe, VerMajor, VerMinor, VerRev, VerBuild)
#define AppVersion   Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRev)

[Setup]
; Fixed AppId: stays the same across versions so updates replace the previous installation
AppId={{6E4B1C2A-8F3D-4E7B-9A51-3C2D7F0B9E64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=HDD-Spindown-Setup-{#AppVersion}
SetupIconFile=..\src\HddSpindown\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
; The app requires admin rights anyway; install for all users into "Program Files"
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Detect a running instance before update/uninstall and let it be closed
AppMutex=Local\HddSpindown-7c1e2b
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.LaunchApp=Start {#AppName}
de.LaunchApp={#AppName} starten
en.AutostartTask=Start {#AppName} with Windows (in the notification area)
de.AutostartTask={#AppName} mit Windows starten (im Infobereich)
en.AdditionalOptions=Additional options:
de.AdditionalOptions=Zusätzliche Optionen:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AdditionalOptions}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Do not overwrite the batch templates if the user has customized them
Source: "{#PublishDir}\beforeSleep.bat"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\afterSleep.bat"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Autostart: the same scheduled task the in-app switch creates (highest privileges, notification area)
Filename: "{app}\{#AppExe}"; Parameters: "--register-autostart"; Flags: runhidden waituntilterminated; Tasks: autostart
; runascurrentuser: start with the installer's admin rights (the app requires them, otherwise error 740)
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Remove the autostart task (whether created by the installer or in the app)
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#AppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveAutostartTask"

; Settings in %AppData%\HDD-Spindown are kept on uninstall.
