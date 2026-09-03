; McKuro · 鸣潮启动器 — Inno Setup 安装脚本
; 由 GitHub Actions (setup job) 在 Windows runner 上编译为 Setup.exe
; 本地调试: 安装 Inno Setup 6 后运行 ISCC.exe setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
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
DefaultDirName={code:DefaultDir}
DefaultGroupName=McKuro
UninstallDisplayIcon={app}\{#MyAppExeName}
; 静默更新支持:安装器自动关闭运行中的 McKuro,装完自动重启(应用内自更新传 /VERYSILENT 即零向导)
CloseApplications=yes
RestartApplications=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\artifacts\setup
OutputBaseFilename=McKuro-setup-{#MyAppVersion}
SetupIconFile=..\src\McKuro\Assets\app.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; 允许命令行切到当前用户安装(/CURRENTUSER,无需 UAC):
; 无管理员权限的用户也能静默更新;向导模式不弹权限选择框,保持默认全用户安装
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 排除调试符号/静态库/运行期日志,与 zip 绿色包口径一致
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib,logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\McKuro"

[Code]
// 安装目录默认值:优先 McKuro 启动时自注册的当前安装目录(HKCU,覆盖 zip 便携版无卸载键的场景,
// 见 src\McKuro\Services\InstallLocationRegistry.cs);目录已不存在或从未注册则回退 Program Files。
// 经安装器装过的场景由 Inno 原生 UsePreviousAppDir(默认 yes)直接复用旧目录,优先级更高,不走这里。
function DefaultDir(Param: String): String;
var
  Registered: String;
begin
  if RegQueryStringValue(HKCU, 'Software\McKuro', 'InstallPath', Registered)
     and (Registered <> '') and DirExists(Registered) then
    Result := Registered
  else
    Result := ExpandConstant('{autopf}\McKuro');
end;
