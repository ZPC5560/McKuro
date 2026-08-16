; McKuro · 鸣潮启动器 — Inno Setup 安装脚本
; 由 GitHub Actions (setup job) 在 Windows runner 上编译为 Setup.exe
; 本地调试: 安装 Inno Setup 6 后运行 ISCC.exe setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyAppPublishDir
  #define MyAppPublishDir "..\src\McKuro\bin\Release\net10.0\win-x64\publish"
#endif

#define MyAppName "McKuro · 鸣潮启动器"
#define MyAppExeName "McKuro.exe"

[Setup]
AppId={{8E4B7C2A-5D3F-4A9B-B6C1-2E7F0D9A4C55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=McKuro
DefaultDirName={autopf}\McKuro
DefaultGroupName=McKuro
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\artifacts\setup
OutputBaseFilename=McKuro-setup-{#MyAppVersion}
SetupIconFile=..\src\McKuro\Assets\app.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\McKuro"
