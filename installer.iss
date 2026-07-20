[Setup]
; App Information
AppName=NovaLite
AppVersion=1.2
AppPublisher=Fynn
DefaultDirName={autopf}\NovaLite
DefaultGroupName=NovaLite
UninstallDisplayIcon={app}\NovaLite.exe

; Output Setup File
OutputDir=..\Setup
OutputBaseFilename=NovaLiteSetup
Compression=lzma2
SolidCompression=yes
SetupIconFile=src\NovaLite.UI\Assets\icon.ico
WizardSmallImageFile=src\NovaLite.UI\Assets\icon.png

; Allow user to run without admin rights if they want, but default to admin
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkablealone

[Files]
; IMPORTANT: Run `dotnet publish -c Release -r win-x64 --self-contained` in src\NovaLite.UI before compiling this script!
Source: "src\NovaLite.UI\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Creates the start menu shortcut
Name: "{group}\NovaLite"; Filename: "{app}\NovaLite.exe"
; Creates the desktop shortcut
Name: "{autodesktop}\NovaLite"; Filename: "{app}\NovaLite.exe"; Tasks: desktopicon

[Run]
; Option to launch the app immediately after installation
Filename: "{app}\NovaLite.exe"; Description: "{cm:LaunchProgram,NovaLite}"; Flags: nowait postinstall skipifsilent
