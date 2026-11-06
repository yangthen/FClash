[Setup]
AppId={{A7237D5E-30E2-4BAA-9D5B-B013555DAF3E}
AppName=FClash
AppVersion={#ReleaseLabel}
AppPublisher=FClash
DefaultDirName={localappdata}\Programs\FClashWinUI
DefaultGroupName=FClash
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\resources\branding\app.ico
UninstallDisplayIcon={app}\FClash.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed={#Architecture}
ArchitecturesInstallIn64BitMode={#Architecture}
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FClash"; Filename: "{app}\FClash.exe"

[Registry]
Root: HKCU; Subkey: "Software\Classes\fclash"; ValueType: string; ValueName: ""; ValueData: "URL:FClash Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\fclash"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\fclash\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FClash.exe"" ""%1"""

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
